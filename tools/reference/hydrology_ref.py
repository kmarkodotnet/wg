"""
Hidrologia referencia-implementacioja M7-hez (docs/05-milestones.md):
depresszio-feltoltes + folyasirany + flow accumulation.

HATOKOR (tudatosan szukitve, ld. milestones "M7 hatokor"): statikus
folyohalozat-felismeres egyetlen elevacio-mezon - nincs idobeli valtozas
(§34), nincs to-kepzodes (§35), nincs jeg/ho (§36), nincs iterativ
eroziós visszahatas a domborzatra.

MODSZER: a Priority-Flood algoritmus (Barnes et al.) EGYSZERRE oldja meg
a depresszio-feltoltest ES a folyasirany-szamitast: minden tile "arasztasi
szuloje" (aki elarasztotta) automatikusan ervenyes folyasirany celpont,
mert:
  1. a szulo mindig alacsonyabb vagy egyenlo feltoltott magassagu, ES
  2. a szulo "arasztasi sorrendje" (flood_order) mindig KISEBB, mint a
     gyereke - ez garantalja, hogy a szulo-lancot kovetve VEGES sok
     lepesben Celba (oceanhoz) erunk, hurok NELKUL (a flood_order szigoruan
     monoton csokken a lanc menten, es az ocean-tile-ok a legkisebb
     flood_order ertekekkel indulnak).

Ez strukturalisan zarja ki a vegtelen ciklust / helyben-ragadast - nem
csak mert eddig igy mertuk, hanem mert a fa-szerkezet (minden tile-nak
pontosan egy szuloje van, az ocean-tile-ok gyokerek) ezt garantalja.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import heapq
import json

from plate_ref import generate_plate_seeds, assign_plate
from plate_boundary_ref import elevation_with_boundary
from sphere_position_ref import position_from_tile
from morton_ref import tile_id
from neighbor_ref import neighbor, DIRECTIONS


def compute_elevation_and_ocean_field(world_seed, plate_count, level, target_water_fraction=0.65):
    """
    Elevacio-mezo + kalibralt tengerszint alapjan ocean-flag minden tile-ra.
    UGYANAZ a szamitas, mint sea_level_ref.compute_elevation_field (valodi
    tile_id-vel, a jitter taggal egyutt) - a hidrologia a MAR VALIDALT
    (TEST-EARTH-001) M4 mezon dolgozik, nem egy elteroen szamolt masikon.
    """
    seeds = generate_plate_seeds(world_seed, plate_count)
    n = 1 << level
    field = {}
    for face in range(6):
        for u in range(n):
            for v in range(n):
                pos = position_from_tile(face, level, u, v)
                plate_id = assign_plate(pos, seeds)
                tid = tile_id(face, level, u, v)
                elev, _ = elevation_with_boundary(world_seed, plate_id, tid, pos, seeds)
                field[(face, u, v)] = elev

    elevations_sorted = sorted(field.values())
    idx = max(0, min(len(elevations_sorted) - 1, int(target_water_fraction * len(elevations_sorted))))
    sea_level = elevations_sorted[idx]

    is_ocean = {k: (e < sea_level) for k, e in field.items()}
    return field, sea_level, is_ocean


def priority_flood(field, is_ocean, level):
    """
    Visszaadja: filled (feltoltott magassag), parent (folyasirany celpont,
    None az ocean-gyokereknel), flood_order (a bejaras sorrendje).
    """
    filled = {}
    parent = {}
    flood_order = {}
    visited = set()
    heap = []  # (filled_elevation, tie_breaker, tile_key)
    counter = 0

    for k, elev in field.items():
        if is_ocean[k]:
            filled[k] = elev
            parent[k] = None
            visited.add(k)
            heapq.heappush(heap, (elev, counter, k))
            counter += 1

    order = 0
    # A pop-sorrend maga a flood_order - de kulon szamlaljuk, mert a heap
    # tie_breaker mezoje csak a heap stabilitasahoz kell, nem szemantikus.
    while heap:
        elev, _, k = heapq.heappop(heap)
        flood_order[k] = order
        order += 1

        face, u, v = k
        for d in DIRECTIONS:
            nb = neighbor(face, level, u, v, d)
            if nb in visited:
                continue
            visited.add(nb)
            nb_filled = max(field[nb], elev)
            filled[nb] = nb_filled
            parent[nb] = k
            heapq.heappush(heap, (nb_filled, counter, nb))
            counter += 1

    return filled, parent, flood_order


def flow_accumulation(field, parent, flood_order):
    """Minden tile accumulation-je: 1 (sajat) + az ot elarasztott (gyerek) tile-ok osszege."""
    accumulation = {k: 1 for k in field}
    # Csokkeno flood_order sorrendben - igy minden tile mar tartalmazza a
    # SAJAT gyerekeinek hozzajarulasat, mire a szulojehez adodik.
    for k in sorted(field.keys(), key=lambda x: -flood_order[x]):
        p = parent[k]
        if p is not None:
            accumulation[p] += accumulation[k]
    return accumulation


def select_river_tiles(is_ocean, accumulation, river_target_fraction):
    """
    A szarazfold ekkora hanyada (0..1) legyen folyo-tile - percentilis-
    modszer az accumulation-eloszlason. UGYANAZ az algoritmus, amit
    eddig csak a Unity PlanetGridMesh.cs hasznalt (megjelenitesi
    celra) - M8-hoz (folyo-torkolat szamlalashoz) at kellett kerulnie
    a Core-ba, hogy ne legyen ket fuggetlen implementacio.
    """
    land_acc = sorted((accumulation[k] for k in is_ocean if not is_ocean[k]), reverse=True)
    if not land_acc:
        return set()
    idx = max(0, min(len(land_acc) - 1, int(river_target_fraction * len(land_acc))))
    threshold = land_acc[idx]
    return {k for k in is_ocean if not is_ocean[k] and accumulation[k] >= threshold}


def river_mouth_count(tiles, parent, is_ocean, river_tiles):
    """Egy tile-halmazban hany folyo-tile folyik KOZVETLENUL oceanba."""
    count = 0
    for t in tiles:
        if t not in river_tiles:
            continue
        p = parent[t]
        if p is not None and is_ocean[p]:
            count += 1
    return count


def verify_all_land_reaches_ocean(field, parent, is_ocean, level, max_steps=None):
    """Minden szarazfold-tile-bol veges lepesben oceanba kell jutni a parent-lancon."""
    n = 1 << level
    if max_steps is None:
        max_steps = 6 * n * n + 10  # barmilyen valos lancnal tobb
    failures = []
    for k in field:
        if is_ocean[k]:
            continue
        current = k
        steps = 0
        while parent[current] is not None:
            current = parent[current]
            steps += 1
            if steps > max_steps:
                failures.append((k, "vegtelen ciklus vagy tul hosszu lanc"))
                break
        else:
            if not is_ocean[current]:
                failures.append((k, f"a lanc vege ({current}) nem ocean"))
    return failures


if __name__ == "__main__":
    world_seed = 0xA7C944210000
    plate_count = 20
    level = 6

    print("Elevacio-mezo + tengerszint szamitasa...")
    field, sea_level, is_ocean = compute_elevation_and_ocean_field(world_seed, plate_count, level)
    print(f"Tengerszint: {sea_level:.1f}m, {sum(is_ocean.values())}/{len(field)} ocean-tile\n")

    print("Priority-flood futtatasa...")
    filled, parent, flood_order = priority_flood(field, is_ocean, level)
    print(f"Kesz: {len(filled)} tile feldolgozva\n")

    print("Ellenorzes: minden szarazfold-tile eleri az oceant...")
    failures = verify_all_land_reaches_ocean(field, parent, is_ocean, level)
    if failures:
        print(f"HIBA: {len(failures)} tile nem eri el az oceant")
        for f in failures[:10]:
            print(f"  {f}")
        raise AssertionError(f"{len(failures)} tile nem eri el az oceant")
    print("OK - minden szarazfold-tile veges lepesben oceanba jut, hurok nelkul\n")

    print("Flow accumulation szamitasa...")
    accumulation = flow_accumulation(field, parent, flood_order)

    # Percentilis-alapu kuszob (ugyanaz a modszer, mint a tengerszintnel,
    # ld. sea_level_ref.calibrate_sea_level) - a folyo-halozatnak a
    # szarazfold KIS reszet kell csak lefednie (valos folyok is igy
    # viselkednek: a vizgyujto-terulet nagy resze NEM maga a folyomeder).
    land_accumulations = sorted(
        (accumulation[k] for k in field if not is_ocean[k]), reverse=True
    )
    river_target_fraction = 0.03  # a szarazfold ~3%-a legyen "folyo"
    idx = max(0, min(len(land_accumulations) - 1, int(river_target_fraction * len(land_accumulations))))
    river_threshold = land_accumulations[idx]

    max_acc = max(accumulation.values())
    land_tiles = sum(1 for k in field if not is_ocean[k])
    river_tiles = sum(1 for k in field if not is_ocean[k] and accumulation[k] >= river_threshold)
    print(f"Max accumulation: {max_acc}")
    print(f"Folyo-kuszob (cel: szarazfold {river_target_fraction:.0%}-a): {river_threshold}")
    print(f"Folyo-tile-ok: {river_tiles}/{land_tiles} szarazfold-tile ({100.0*river_tiles/land_tiles:.2f}%)")

    assert river_tiles > 0, "Legalabb egy folyo-tile-nak lennie kell"
    assert river_tiles < land_tiles * 0.10, "Tul sok tile folyo - a kuszob gyanusan alacsony"
    print("\nOK - a folyo-halozat plauzibilis meretu")

    # Determinizmus-ellenorzes
    filled2, parent2, flood_order2 = priority_flood(field, is_ocean, level)
    assert parent == parent2, "A priority-flood nem determinisztikus!"
    print("OK - determinisztikus (ismetelt hivas azonos eredmenyt ad)")

    # Tesztvektorok a C# porthoz - determinisztikus mintavetel a Threefry
    # oraklummal, a projekt korabbi mintaja szerint.
    from threefry_ref import threefry4x64

    n = 1 << level
    keys = list(field.keys())
    vectors = []
    gen_seed = 0x8AD0000000000001
    for i in range(500):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 11, 0], 20)
        key = keys[p[0] % len(keys)]
        face, u, v = key
        par = parent[key]
        vectors.append({
            "face": face, "u": u, "v": v,
            "isOcean": is_ocean[key],
            "filled": filled[key],
            "parent": None if par is None else {"face": par[0], "u": par[1], "v": par[2]},
            "floodOrder": flood_order[key],
            "accumulation": accumulation[key],
        })

    with open("hydrology_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count, "level": level,
            "targetWaterFraction": 0.65, "riverTargetFraction": river_target_fraction,
            "seaLevel": sea_level, "riverThreshold": river_threshold,
            "vectors": vectors,
        }, f, indent=1)
    print(f"\n{len(vectors)} hidrologia-tesztvektor generalva")
