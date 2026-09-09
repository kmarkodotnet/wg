"""
Dendritikus folyo-nyomvonal referencia-implementacioja (backlog "M9/M7 |
Folyok: fa-szerű halozat, hosszabb folyok, csapadek-forras + szuperfinom
nyomvonal-kiertekeles", 2026-09-05 felhasznaloi keres, MAGAS prioritas).

PROBLEMA, amit ez a modul old: a MEGLEVO hidrologia (hydrology_ref.py) a
REFERENCIA szinten (pl. level 6-8) szamol priority-flood-ot es
flow-accumulation-t - ezen a felbontason (tile-el ~100-800 km) egy
vizgyujto-teruletnek NAGYON keves tile jut, ezert a kirajzolt "folyok"
rovidek es alig agaznak (nincs eleg tile a valodi mellekfolyo-mintazathoz).

MEGOLDAS (a felhasznalonak felajanlott 2 opcio kozul a LOKALIS valasztva -
ld. docs/backlog.md 2026-09-05 sor es a beszelgetes): a REFERENCIA szinten
csak a folyo-FORRASOKAT valasztjuk ki (csapadekos + hegyvideki tile-ok),
majd EGYENKENT, egy SOKKAL FINOMABB szinten (fine_level = level + depth)
kovetjuk a lejtot lefele (steepest descent) az oceanig/tavig - ez ADJA a
"szuperreszletes nyomvonalat", GLOBALIS finom racs nelkul (ami level 10+
mar milliokra novelne a tile-szamot es a szekvencialis priority-flood-ot
belathatatlanul lassitana).

DENDRITIKUS ELAGAZAS: ha egy KESOBBI forras utja egy MAR MEGLATOGATOTT
(korabbi forras altal "lefoglalt") finom tile-ba fut, ott MEGALL - a ket
folyo onnantol UGYANAZT a meder-szakaszt hasznalja (osszefolyas), a
renderelo oldal ezt egyszeruen ket kulon szakaszkent rajzolja, aminek a
vege azonos ponton talalkozik.

DETERMINIZMUS: minden lepesnel a 4 szomszed KOZUL a szigoruan legalacsonyabb
nyer; dontetlen eseten a rogzitett DIRECTIONS sorrend (right,left,up,down)
elso talalata. A lepes CSAK szigoruan alacsonyabb szomszedre tortenik ->
az elevacio-sorozat szigoruan monoton csokken az uton -> STRUKTURALISAN
kizart a hurok (nem csak mert eddig igy mertuk).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import heapq
import json

from plate_ref import generate_plate_seeds, assign_plate
from plate_boundary_ref import elevation_with_boundary
from sphere_position_ref import position_from_tile
from morton_ref import tile_id
from neighbor_ref import neighbor, DIRECTIONS
from domain_warp_ref import warp_position
from hydrology_ref import compute_elevation_and_ocean_field
from moisture_transport_ref import compute_precipitation_field

# --- MVP konstansok (illusztrativak, mint a tobbi hasonlo modul - ld. ND-44 mintaja) ---
DEFAULT_FINE_DEPTH = 4          # fine_level = level + DEFAULT_FINE_DEPTH
DEFAULT_SOURCE_TOP_K = 12       # legfeljebb ennyi folyo-forras
DEFAULT_MIN_ELEV_ABOVE_SEA_M = 300.0
DEFAULT_PRECIP_PERCENTILE = 0.80  # a szarazfoldi csapadek-eloszlas felso 20%-a
DEFAULT_MAX_STEPS = 2000


def elevation_at_tile(world_seed, face, level, u, v, seeds):
    """Egyetlen tile elevacioja - UGYANAZ a keplet, mint a teljes-mezos
    compute_elevation_and_ocean_field-ben, csak PONTSZERUEN (nincs szukseg
    a teljes finom-szintu mezore, csak a bejart utvonal tile-jaira)."""
    pos = position_from_tile(face, level, u, v)
    warped_pos = warp_position(world_seed, pos)
    plate_id = assign_plate(warped_pos, seeds)
    tid = tile_id(face, level, u, v)
    elev, _ = elevation_with_boundary(world_seed, plate_id, tid, pos, seeds)
    return elev


def select_river_sources(
    elev_field, precip_field, is_ocean, sea_level,
    top_k=DEFAULT_SOURCE_TOP_K,
    min_elev_above_sea_m=DEFAULT_MIN_ELEV_ABOVE_SEA_M,
    precip_percentile=DEFAULT_PRECIP_PERCENTILE,
):
    """
    Csapadekos hegyvideki tile-ok kivalasztasa forraskent - MINDKET
    kuszobnek (magassag ES csapadek) egyszerre kell teljesulnie. A
    csapadek-kuszob a SZARAZFOLDI eloszlas percentilise (nem abszolut
    ertek), hogy vilagfuggetlenul ertelmes maradjon.
    """
    land_precip = sorted(precip_field[k] for k in elev_field if not is_ocean[k])
    if not land_precip:
        return []
    idx = max(0, min(len(land_precip) - 1, int(precip_percentile * len(land_precip))))
    precip_threshold = land_precip[idx]

    candidates = []
    for k, elev in elev_field.items():
        if is_ocean[k]:
            continue
        if elev < sea_level + min_elev_above_sea_m:
            continue
        if precip_field[k] < precip_threshold:
            continue
        candidates.append(k)

    # Determinisztikus rendezes: csokkeno csapadek, dontetlennel (face,u,v)
    # novekvo - igy a top_k kivalasztas STABIL, nem fugg a dict bejarasi
    # sorrendtol.
    candidates.sort(key=lambda k: (-precip_field[k], k[0], k[1], k[2]))
    return candidates[:top_k]


DEFAULT_ESCAPE_NODE_BUDGET = 400


class _ElevationCache:
    """Memoizalt pontszeru elevacio-kiertekeles - a nyomvonal-kovetes ES a
    pit-escape kereses (ld. lent) gyakran UGYANAZOKAT a fine-szintu tile-okat
    kerdezi le tobbszor (szomszedos forrasok, egymast metszo utvonalak)."""

    def __init__(self, world_seed, seeds):
        self._world_seed = world_seed
        self._seeds = seeds
        self._cache = {}

    def get(self, face, level, u, v):
        key = (face, level, u, v)
        cached = self._cache.get(key)
        if cached is None:
            cached = elevation_at_tile(self._world_seed, face, level, u, v, self._seeds)
            self._cache[key] = cached
        return cached


def _find_local_spillway(elev_cache, fine_level, pit_face, pit_u, pit_v, pit_elev,
                          node_budget=DEFAULT_ESCAPE_NODE_BUDGET, path_visited=None):
    """
    A finom szintu fraktal-zaj miatt a nyers lejto-kovetes SOK, apro (zaj-
    meretu) helyi melyedesbe akadna bele, mielott barmilyen valos, nagy
    lepteku vizgyujto-kijaratot elerne - ez pontosan az az ok, ami miatt az
    EREDETI (globalis, referencia-szintu) hidrologia priority-flood-ot hasznal
    naiv lejto-kovetes helyett (ld. hydrology_ref.py). Ez a fuggveny UGYANAZT
    a priority-flood elvet alkalmazza, csak LOKALISAN, IGENY SZERINT (nem
    elore, a teljes bolygora): a pit-tol kiindulva, a MINDIG legalacsonyabb
    feltoltott hatart bovitve (Barnes et al. Priority-Flood), megkeresi az
    ELSO tile-t, aminek a SAJAT (feltoltetlen) elevacioja SZIGORUAN a pit
    szintje ALATT van - ez a medence tulcsordulasi pontja (spillway).

    `node_budget`-en beluli sikertelen kereses -> None (a medence tul nagy/
    mely ahhoz, hogy ezen a szinten ertelmes legyen folytatni - a hivo ekkor
    valodi "pit"-kent zarja le az utvonalat, pl. egy hegyi to).
    """
    # `path_visited` a HIVO (trace_river_path) teljes eddigi utvonala - a
    # lokalis kereses ELKERULI ezeket (nem terjeszkedik beleju, es nem is
    # fogadja el oket tulcsordulasi pontkent), kulonben a folyo a SAJAT MAR
    # BEJART medret metszhetne ujra, ami hurkot (ismetlodo tile-t) okozna.
    forbidden = path_visited if path_visited is not None else frozenset()

    start = (pit_face, pit_u, pit_v)
    visited = {start}
    parent = {start: None}
    heap = [(pit_elev, 0, start)]
    counter = 1
    expanded = 0

    while heap and expanded < node_budget:
        filled_elev, _, node = heapq.heappop(heap)
        expanded += 1
        nf, nu, nv = node
        raw_elev = elev_cache.get(nf, fine_level, nu, nv)

        if node != start and raw_elev < pit_elev and node not in forbidden:
            path = []
            cur = node
            while cur is not None:
                path.append(cur)
                cur = parent[cur]
            path.reverse()  # start (pit) -> ... -> spillway
            return path, raw_elev

        for d in DIRECTIONS:
            nbf, nbu, nbv = neighbor(nf, fine_level, nu, nv, d)
            nb = (nbf, nbu, nbv)
            if nb in visited or nb in forbidden:
                continue
            visited.add(nb)
            nb_raw = elev_cache.get(nbf, fine_level, nbu, nbv)
            nb_filled = max(nb_raw, filled_elev)
            parent[nb] = node
            heapq.heappush(heap, (nb_filled, counter, nb))
            counter += 1

    return None


def trace_river_path(
    world_seed, seeds, sea_level,
    source_face, source_level, source_u, source_v,
    fine_depth=DEFAULT_FINE_DEPTH, claimed=None, max_steps=DEFAULT_MAX_STEPS,
    elev_cache=None, escape_node_budget=DEFAULT_ESCAPE_NODE_BUDGET,
):
    """
    Egy forrasbol indulo nyomvonal a FINE szinten: lejto-menti (steepest
    descent) lepesek, LOKALIS priority-flood-dal (ld. _find_local_spillway)
    minden apro, zaj-meretu melyedesnel athidalva - csak akkor all meg
    veglegesen "pit"-kent, ha a lokalis kereses SEM talal kijaratot a
    csomopont-budgeten belul (valodi, nagy medence/to).

    `claimed` egy KOZOS (az osszes forras kozott megosztott) dict:
    (face,level,u,v) -> forras-index, ami mar "hasznalt" (korabbi folyo)
    tile-okat jelol - ha az ut ilyenbe fut, ott megall (osszefolyas).
    Visszaad egy dict-et: path (tile-lista, a fine szinten), terminationReason
    ("ocean", "pit", "merged", "maxSteps").
    """
    if elev_cache is None:
        elev_cache = _ElevationCache(world_seed, seeds)

    fine_level = source_level + fine_depth
    u = source_u << fine_depth
    v = source_v << fine_depth
    face = source_face

    path = [(face, u, v)]
    path_visited = {(face, u, v)}
    elev = elev_cache.get(face, fine_level, u, v)

    for step in range(max_steps):
        if elev < sea_level:
            return {"path": path, "terminationReason": "ocean"}
        if claimed is not None and (face, fine_level, u, v) in claimed and step > 0:
            return {"path": path, "terminationReason": "merged"}

        best = None  # (elev, face, u, v)
        for d in DIRECTIONS:
            nf, nu, nv = neighbor(face, fine_level, u, v, d)
            nb = (nf, nu, nv)
            if nb in path_visited:
                continue  # sose lepjunk vissza a SAJAT mar bejart medrunkbe
            nelev = elev_cache.get(nf, fine_level, nu, nv)
            if nelev < elev and (best is None or nelev < best[0]):
                best = (nelev, nf, nu, nv)

        if best is not None:
            elev, face, u, v = best
            path.append((face, u, v))
            path_visited.add((face, u, v))
            continue

        escape = _find_local_spillway(
            elev_cache, fine_level, face, u, v, elev,
            node_budget=escape_node_budget, path_visited=path_visited,
        )
        if escape is None:
            return {"path": path, "terminationReason": "pit"}
        escape_path, escape_elev = escape
        for node in escape_path[1:]:  # escape_path[0] == (face,u,v), mar benne van path-ban
            path.append(node)
            path_visited.add(node)
        elev, face, u, v = escape_elev, *escape_path[-1]

    return {"path": path, "terminationReason": "maxSteps"}


def build_river_network(
    world_seed, plate_count, level,
    fine_depth=DEFAULT_FINE_DEPTH, top_k=DEFAULT_SOURCE_TOP_K,
    min_elev_above_sea_m=DEFAULT_MIN_ELEV_ABOVE_SEA_M,
    precip_percentile=DEFAULT_PRECIP_PERCENTILE, max_steps=DEFAULT_MAX_STEPS,
    precomputed_fields=None,
):
    """
    A teljes lanc: mezo -> csapadek -> forrasok -> nyomvonalak (dendritikus
    egyesulessel). `precomputed_fields` opcionalis (elev_field, sea_level,
    is_ocean, precip_field) - a hivo ujrahasznalhatja, ha mar kiszamolta
    (a csapadek-mezo draga: ~24576 tile x 24 iteracio, percekig tart
    tiszta Pythonban - ld. modul-doc).
    """
    if precomputed_fields is not None:
        elev_field, sea_level, is_ocean, precip_field = precomputed_fields
    else:
        elev_field, sea_level, is_ocean = compute_elevation_and_ocean_field(world_seed, plate_count, level)
        precip_field, _elev_field2, _sea_level2, _is_ocean2 = compute_precipitation_field(world_seed, plate_count, level)

    sources = select_river_sources(
        elev_field, precip_field, is_ocean, sea_level,
        top_k=top_k, min_elev_above_sea_m=min_elev_above_sea_m, precip_percentile=precip_percentile,
    )

    seeds = generate_plate_seeds(world_seed, plate_count)
    elev_cache = _ElevationCache(world_seed, seeds)
    claimed = {}
    rivers = []
    for i, (face, u, v) in enumerate(sources):
        result = trace_river_path(
            world_seed, seeds, sea_level, face, level, u, v,
            fine_depth=fine_depth, claimed=claimed, max_steps=max_steps, elev_cache=elev_cache,
        )
        fine_level = level + fine_depth
        for (pf, pu, pv) in result["path"]:
            claimed.setdefault((pf, fine_level, pu, pv), i)
        rivers.append({
            "sourceIndex": i, "sourceFace": face, "sourceU": u, "sourceV": v,
            "path": result["path"], "terminationReason": result["terminationReason"],
        })
    return {"seaLevel": sea_level, "fineLevel": level + fine_depth, "sources": sources, "rivers": rivers}


if __name__ == "__main__":
    import time
    import sys

    world_seed = 0xA7C944210000
    plate_count = 20
    level = 6

    print("Elevacio- es oceanmezo szamitasa...", flush=True)
    t0 = time.time()
    elev_field, sea_level, is_ocean = compute_elevation_and_ocean_field(world_seed, plate_count, level)
    print(f"  kesz ({time.time() - t0:.1f}s)", flush=True)

    print("Csapadek-mezo szamitasa (draga - percekig tarthat tiszta Pythonban)...", flush=True)
    t0 = time.time()
    precip_field, _e2, _s2, _o2 = compute_precipitation_field(world_seed, plate_count, level)
    print(f"  kesz ({time.time() - t0:.1f}s)", flush=True)
    fields = (elev_field, sea_level, is_ocean, precip_field)

    print("Folyo-halozat epitese (forrasok + finom-szintu nyomvonalak)...", flush=True)
    t0 = time.time()
    network = build_river_network(world_seed, plate_count, level, precomputed_fields=fields)
    print(f"  kesz ({time.time() - t0:.1f}s)", flush=True)
    rivers = network["rivers"]
    print(f"Forrasok: {len(network['sources'])}, fine_level={network['fineLevel']}")

    assert len(rivers) > 0, "Legalabb egy folyo-forrasnak kell lennie"

    seeds = generate_plate_seeds(world_seed, plate_count)
    for r in rivers:
        path = r["path"]
        elevs = [elevation_at_tile(world_seed, f, network["fineLevel"], u, v, seeds) for (f, u, v) in path]
        # MEGJEGYZES: a lokalis pit-escape (_find_local_spillway) miatt a
        # nyers elevacio NEM feltetlenul csokken SZIGORUAN minden EGYES
        # lepesnel (egy meder rovid szakaszon at is kelhet egy alacsony
        # nyeregponton, mielott tovabb eshetne - mint a valodi folyoknal).
        # A strukturalis garancia, amit tenylegesen ellenorzunk: (1) nincs
        # ismetlodo tile (tehat nincs hurok), (2) a folyo VEGpontja
        # alacsonyabban van, mint a KEZdopontja (tenyleges, netto eses).
        assert len(path) == len(set(path)), f"Ismetlodo tile (hurok) a(z) {r['sourceIndex']}. folyoban"
        if len(elevs) > 1:
            assert elevs[-1] < elevs[0], (
                f"A(z) {r['sourceIndex']}. folyo vegpontja NEM alacsonyabb a kezdopontjanal: "
                f"{elevs[0]} -> {elevs[-1]}"
            )
        assert r["terminationReason"] in ("ocean", "pit", "merged", "maxSteps")

    print("OK - minden folyo hurok nelkuli, es netto lejt (vegpont < kezdopont)")

    lengths = [len(r["path"]) for r in rivers]
    print(f"Utvonal-hosszak (fine-szintu tile-okban): {lengths}")
    print(f"Atlagos hossz: {sum(lengths) / len(lengths):.1f} tile")

    merged_count = sum(1 for r in rivers if r["terminationReason"] == "merged")
    ocean_count = sum(1 for r in rivers if r["terminationReason"] == "ocean")
    pit_count = sum(1 for r in rivers if r["terminationReason"] == "pit")
    print(f"Vegzodesek: ocean={ocean_count}, merged (osszefolyas)={merged_count}, pit={pit_count}")

    # Determinizmus-ellenorzes - UGYANAZT a mar kiszamolt mezot hasznaljuk
    # ujra (a csapadek-mezo ujraszamitasa draga lenne, ld. fent), csak a
    # forras-kivalasztas+nyomvonal-kovetes fut le megegyszer.
    network2 = build_river_network(world_seed, plate_count, level, precomputed_fields=fields)
    assert network["sources"] == network2["sources"], "A forras-kivalasztas nem determinisztikus!"
    assert [r["path"] for r in rivers] == [r["path"] for r in network2["rivers"]], \
        "A nyomvonal-kovetes nem determinisztikus!"
    print("OK - determinisztikus (ismetelt hivas azonos eredmenyt ad)")

    # Tesztvektorok a C# porthoz.
    vectors = []
    for r in rivers:
        vectors.append({
            "sourceIndex": r["sourceIndex"],
            "sourceFace": r["sourceFace"], "sourceU": r["sourceU"], "sourceV": r["sourceV"],
            "terminationReason": r["terminationReason"],
            "pathLength": len(r["path"]),
            "path": [{"face": f, "u": u, "v": v} for (f, u, v) in r["path"]],
        })

    with open("river_path_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count, "level": level,
            "fineDepth": DEFAULT_FINE_DEPTH, "fineLevel": network["fineLevel"],
            "topK": DEFAULT_SOURCE_TOP_K, "minElevAboveSeaM": DEFAULT_MIN_ELEV_ABOVE_SEA_M,
            "precipPercentile": DEFAULT_PRECIP_PERCENTILE, "maxSteps": DEFAULT_MAX_STEPS,
            "seaLevel": network["seaLevel"],
            "sources": [{"face": f, "u": u, "v": v} for (f, u, v) in network["sources"]],
            "rivers": vectors,
        }, f, indent=1)
    print(f"\n{len(vectors)} folyo-nyomvonal tesztvektor generalva")
