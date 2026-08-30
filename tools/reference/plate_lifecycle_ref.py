"""
Lemez-eletciklus (szuletes/halal) referencia-implementacioja az osszevont
M10 ("Fix plateCount a vilag elejetol") + M11 ("Rift-zona + lemez-hasadas/
egyesules") backlog-tetelhez, docs/00-spec-v1.0.md Sz.16 ("Lemez-szuletes
es lemez-halal") alapjan:

    PlateSplitEvent, PlateMergeEvent, SubductionTermination,
    RiftActivation, HotspotBirth, HotspotDeath

    "Ezeket a world seed es a geodinamikai allapot alapjan utemezzuk."

A spec NEM ad zart kepletet ezekre az esemenyekre (szemben pl. a
becsapodasokkal, ld. impacts_ref.py, ahol Schmidt & Housen (1987) skalazas
van) - itt MINDEN idozitesi/geometriai szabaly ebben a fajlban SZULETIK,
MVP-tervezesi dontesekkel. Reszletek: docs/04-decisions.md ND-45 (a-i
alpontok). EZEK NEM HIVATALOS FORRASBOL VERIFIKALT FIZIKAI KONSTANSOK -
"vilag-tervezesi" (MVP) parameterek, plauzibilitasra hangolva, NEM merve
semmilyen geologiai adatsorhoz. Ezt a CLAUDE.md "ha nincs elerheto
hivatalos forras, jelezd explicit" szabalya szerint itt es a valaszban is
explicit jelezzuk - felhasznaloi megerositest igenyelnek, mielott a C#
portba "vegleges" ertekkent kerulnek.

HATOKOR (ld. ND-45 - a korabbi milestone-ok mintajat kovetve, tudatos
szukites):
  - **ND-04 (timestep-invariancia) A VEZERLO ELV.** Nincs futasidoben
    akkumulalt "stressz-szamlalo" - minden lemez SAJAT, zart-formaju
    "eletrajzi sorsat" (split/merge/semmi + esemenyido) egyetlen
    Threefry-hivasbol kapja, KIZAROLAG a sajat plateId-jabol es a world
    seedbol - a "szuletesi ido" (mikor jott letre split reven) csak egy
    ELTOLAS a mar rogzitett eletciklus-hosszhoz, nem egy masik allapot-
    fuggo bemenet. `PlateTopologyAtTime(seed, t)` MINDEN hivaskor a
    TELJES leszarmazasi fat ujraepiti a gyokerektol - nincs memoizalas,
    nincs mutable modul-szintu allapot -> a "lepesenkent szimulalva" vs.
    "t kozvetlen lekerdezese" kerdes DEFINICIO SZERINT ugyanaz a
    fuggvenyhivas, ld. a __main__ "Timestep-invariancia" blokkjat.
  - RiftActivation + PlateSplitEvent: implementalva (1-2. pont).
  - PlateMergeEvent / SubductionTermination: implementalva, MECHANIKAILAG
    EGYSEGESITVE "lemez-eltavolitas + Voronoi-elnyeles" muveletkent (ND-45d)
    - a ket spec-esemeny fogalmilag kulonbozik (ket lemez egyesulese vs.
      egy lemez teljes elnyelese), de MVP-szinten ugyanaz a mechanika
      szolgalja ki mindkettot (3. pont).
  - HotspotBirth/HotspotDeath: HALASZTVA (4. pont, ND-45f) - nincs meg
    hotspot-modell a projektben egyaltalan (sem statikus, sem dinamikus),
    ez onmagaban kulon milestone-nyi munka; csak a domain/property-id-k
    vannak fenntartva a jovore.
  - A leszarmazasi fa melysege MAX_GENERATION-nel levagva (ND-45e) - ez
    NEM fizikai allitas ("a lemezek 3 hasadas utan mindig stabilizalodnak"),
    hanem vegessegi korlat egy veges teszt-horizonton.

Erre a modulra epul (nem ujraimplementalja):
  - plate_ref.py: lemez-mag generalas (`generate_plate_seeds`), Voronoi-
    hozzarendeles (`assign_plate`), `sample_unit_vector3`/`sample4`/`_block`
    alap-mintavevok.
  - plate_motion_ref.py: Euler-polus (`generate_euler_pole`), szogsebesseg
    (`generate_angular_velocity`), Rodrigues-forgatas (`rodrigues_rotate`) -
    a lemez-mag mozgasa idoben, MINDEN gyermek-lemez ugyanezt hasznalja a
    sajat (uj) Euler-poluasval/szogsebessegevel.
  - impacts_ref.py: minta a "deep-time esemeny, seedbol determinisztikusan
    utemezve" mintazatra (bar ott epoch-alapu Bernoulli-probalgatas van,
    itt egy zart "eletciklus-sorsolas" per lemez - a kulonbseget ld. ND-45a).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math

from plate_ref import (
    M64, DOMAIN_TECTONICS, sample4, sample_unit_vector3, _block,
    generate_plate_seeds, assign_plate,
)
from plate_motion_ref import generate_euler_pole, generate_angular_velocity, rodrigues_rotate
from sphere_position_ref import position_from_tile

# --- Uj RandomProperty-k a Tectonics domainben (ND-45h) ---
# A meglevo src/WorldGen.Core/Random/RandomDomain.cs 10-13-at hasznalja mar
# (PlateSeedPoint, EulerPole, PlateVelocity, CrustType) - az uj ertekek a
# kovetkezo szabad sorszamok, MEG NINCSENEK felvezetve a C# enum-ba (ez a
# C# port feladata lesz, ld. valasz-osszefoglalo).
PROPERTY_LIFECYCLE_ROLL = 14
PROPERTY_SPLIT_AXIS_HINT = 15
PROPERTY_CHILD_PLATE_ID = 16
# 17-19: fenntartva HotspotBirth/HotspotDeath-nek (ND-45f, halasztva)

# --- MVP "vilag-tervezesi" konstansok (ND-45b) - NEM verifikalt kulso ---
# --- forrasbol, plauzibilitasra hangolt, MEGERoSITEST IGENYEL. ---
P_SPLIT = 0.45          # annak az eselye, hogy egy lemez "sorsa" hasadas
P_MERGE = 0.25          # ...vagy egyesules/szubdukcios megszunes
# (a maradek 1 - P_SPLIT - P_MERGE = 0.30 -> sose tortenik esemeny a
# szimulalt horizonton belul - "stabil" lemez)

MIN_LIFESPAN_MYR = 80.0     # a Wilson-ciklus (szuperkontinens-ciklus) nagysagrendje
MAX_LIFESPAN_MYR = 400.0    # utan szabadon valasztott sav, NEM egyetlen forrasbol idezve
RIFT_FRACTION_MIN = 0.40    # a rift a lemez "eletenek" ekkora hanyadanal aktivalodik
RIFT_FRACTION_MAX = 0.85    # (a split-esemeny elott, de ahhoz kozel)

SPLIT_HALF_ANGLE_RAD = 0.12  # ~6.9 fok - uj gyermek-magok szog-eltolasa a
                              # regi mag helyzetetol (ND-45c)
MAX_GENERATION = 3            # leszarmazasi fa melyseg-korlatja (ND-45e)


def plate_lifecycle_roll(world_seed, plate_id):
    """Egyetlen lemez "eletrajzi sorsa" - TISZTA fuggveny (world_seed,
    plate_id)-bol, NEM fugg a szuletesi idotol vagy generaciotol (azok
    kulon, additiv eltolaskent alkalmazodnak a hivo oldalon - ld. ND-45a).

    Visszaad: {"kind": "split"|"merge"|"none", "lifespanMyr": float,
               "riftFractionOfLifespan": float}
    Az utolso mezo csak "split" eseten ertelmes (RiftActivation ideje =
    birth + lifespanMyr * riftFractionOfLifespan).
    """
    a, b, c, _d = sample4(world_seed, DOMAIN_TECTONICS, plate_id & M64, 0, PROPERTY_LIFECYCLE_ROLL, 0)
    if a < P_SPLIT:
        kind = "split"
    elif a < P_SPLIT + P_MERGE:
        kind = "merge"
    else:
        kind = "none"
    lifespan = MIN_LIFESPAN_MYR + b * (MAX_LIFESPAN_MYR - MIN_LIFESPAN_MYR)
    rift_fraction = RIFT_FRACTION_MIN + c * (RIFT_FRACTION_MAX - RIFT_FRACTION_MIN)
    return {"kind": kind, "lifespanMyr": lifespan, "riftFractionOfLifespan": rift_fraction}


def _perpendicular_split_axis(world_seed, plate_id, seed_pos_at_split):
    """Veletlen egysegvektor, ami MEROLEGES seed_pos_at_split-re - ez a
    "rift-vonalra meroleges" tengely, ami korul a ket uj gyermek-mag
    szimmetrikusan szetnyilik (ND-45c). Elutasitasos: ha a nyers hint-
    vektor majdnem parhuzamos lenne a lemez-poziciohoz (elfajulo
    keresztszorzat), ujra mintavetelezunk - ugyanaz az elv, mint
    sample_unit_vector3 sajat belso elutasitasa, csak itt a "time_bucket"
    mezot hasznaljuk elutasitas-szamlalokent (ez a mezo egyebkent nem
    idofuggo tulajdonsagnal fixen 0, ujrahasznositva szabad "csatornakent")."""
    sx, sy, sz = seed_pos_at_split
    i = 0
    while True:
        hx, hy, hz, _ = sample_unit_vector3(world_seed, DOMAIN_TECTONICS, plate_id & M64, i, PROPERTY_SPLIT_AXIS_HINT)
        cx = sy * hz - sz * hy
        cy = sz * hx - sx * hz
        cz = sx * hy - sy * hx
        len_sq = cx * cx + cy * cy + cz * cz
        if len_sq > 0.01:  # nem majdnem-parhuzamos - biztonsagos normalizalni
            inv = 1.0 / math.sqrt(len_sq)
            return cx * inv, cy * inv, cz * inv
        i += 1


def _make_children(world_seed, parent):
    """Egy PlateSplitEvent ket gyermek-lemezet general (ND-45c, ND-45g).

    parent: dict a kovetkezo kulcsokkal: plateId, seedPosAtEvent (a szulo
    pozicioja PONTOSAN a split-idopontban), eventTimeMyr, generation.
    """
    perp = _perpendicular_split_axis(world_seed, parent["plateId"], parent["seedPosAtEvent"])
    children = []
    for child_index in range(2):
        sign = 1.0 if child_index == 0 else -1.0
        seed_at_birth = rodrigues_rotate(parent["seedPosAtEvent"], perp, sign * SPLIT_HALF_ANGLE_RAD)
        # Uj, opak 64-bites plateId: nyers Threefry-kimenet (nem [0,1)-be
        # skalazott minta) - a hash-teljesseg miatt utkozes-valoszinuseg
        # elhanyagolhato ekkora csomopont-szamnal (ND-45g).
        words = _block(world_seed, DOMAIN_TECTONICS, parent["plateId"] & M64, child_index, PROPERTY_CHILD_PLATE_ID, 0)
        child_id = words[0] & M64
        axis = generate_euler_pole(world_seed, child_id)
        omega = generate_angular_velocity(world_seed, child_id)
        children.append({
            "plateId": child_id,
            "birthTimeMyr": parent["eventTimeMyr"],
            "seedPosAtBirth": seed_at_birth,
            "axis": axis,
            "omega": omega,
            "generation": parent["generation"] + 1,
            "parentId": parent["plateId"],
        })
    return children


def resolve_topology(world_seed, time_myr, plate_count, max_generation=MAX_GENERATION):
    """A lemez-topologia time_myr idopontban - PlateTopologyAtTime(seed, t).

    t=0-nal PONTOSAN a statikus M4 lemez-listat adja vissza (plateId
    0..plate_count-1, seed_pos == generate_plate_seeds(...) eredmenye).

    Visszaad: plateId szerint rendezett lista dict-ekkel:
      plateId, currentPos (x,y,z egysegvektor time_myr-nel), axis, omega,
      birthTimeMyr, generation, parentId, eventKind ("split"/"merge"/"none"),
      pendingEventTimeMyr (ha az esemeny meg t utan van; None ha "none").

    TISZTA FUGGVENY: minden hivas a TELJES fat ujraepiti a gyokerektol -
    nincs mutable/modul-szintu allapot, nincs memoizalas (ND-04 -
    timestep-invariancia bizonyitasahoz ez a kulcs-tulajdonsag).
    """
    root_seeds = generate_plate_seeds(world_seed, plate_count)
    pending = [
        {
            "plateId": i,
            "birthTimeMyr": 0.0,
            "seedPosAtBirth": root_seeds[i],
            "axis": generate_euler_pole(world_seed, i),
            "omega": generate_angular_velocity(world_seed, i),
            "generation": 0,
            "parentId": None,
        }
        for i in range(plate_count)
    ]

    active = []
    while pending:
        node = pending.pop()
        roll = plate_lifecycle_roll(world_seed, node["plateId"])
        event_time = node["birthTimeMyr"] + roll["lifespanMyr"]
        generation_capped = node["generation"] >= max_generation
        kind = "none" if generation_capped else roll["kind"]

        elapsed = time_myr - node["birthTimeMyr"]
        pos_now = rodrigues_rotate(node["seedPosAtBirth"], node["axis"], node["omega"] * elapsed) \
            if elapsed != 0.0 else node["seedPosAtBirth"]

        if kind == "none" or event_time > time_myr:
            # Meg nem tortent esemeny (vagy sose fog) - a lemez AKTIV t-nel.
            active.append({
                "plateId": node["plateId"],
                "currentPos": pos_now,
                "axis": node["axis"],
                "omega": node["omega"],
                "birthTimeMyr": node["birthTimeMyr"],
                "generation": node["generation"],
                "parentId": node["parentId"],
                "eventKind": kind,
                "pendingEventTimeMyr": None if kind == "none" else event_time,
                "riftActivationTimeMyr": (
                    node["birthTimeMyr"] + roll["lifespanMyr"] * roll["riftFractionOfLifespan"]
                    if kind == "split" else None
                ),
            })
            continue

        if kind == "merge":
            # PlateMergeEvent / SubductionTermination (ND-45d): a lemez
            # egyszeruen megszunik - a terulete a megmaradt szomszedok
            # kozott automatikusan ujraoszlik a kovetkezo Voronoi-
            # kiertekelesnel (nincs kulon "gyoztes" lemez nyilvantartva).
            continue

        # kind == "split": PlateSplitEvent - ket uj gyermek-lemez.
        seed_pos_at_event = pos_now  # elapsed = event_time - birth mar bennefoglalt
        parent_for_children = {
            "plateId": node["plateId"],
            "seedPosAtEvent": seed_pos_at_event,
            "eventTimeMyr": event_time,
            "generation": node["generation"],
        }
        pending.extend(_make_children(world_seed, parent_for_children))

    active.sort(key=lambda r: r["plateId"])
    return active


def estimate_areas(active_list, level=4):
    """Terulet-becsles (a gomb feluletenek TORTRESZEKENT, 0..1 - ND-45i,
    nincs meg elfogadott bolygo-sugar-konstans ehhez a modulhoz) - nyers
    Voronoi-tile-mintavetellel, csak plauzibilitas-ellenorzeshez/demohoz,
    NEM resze a C# porthoz generalt tesztvektoroknak (draga, es a fo
    algoritmus - az esemeny-utemezes - fuggetlen tole)."""
    seeds_now = [(r["plateId"], r["currentPos"]) for r in active_list]
    n = 1 << level
    counts = {pid: 0 for pid, _ in seeds_now}
    total = 0
    for face in range(6):
        for u in range(n):
            for v in range(n):
                pos = position_from_tile(face, level, u, v)
                best_id, best_dot = None, -2.0
                for pid, spos in seeds_now:
                    dot = pos[0] * spos[0] + pos[1] * spos[1] + pos[2] * spos[2]
                    if dot > best_dot:
                        best_dot = dot
                        best_id = pid
                counts[best_id] += 1
                total += 1
    return {pid: c / total for pid, c in counts.items()}


if __name__ == "__main__":
    import json
    from threefry_ref import threefry4x64

    world_seed = 0xA7C944210000
    plate_count = 10

    print("--- t=0 visszamenoleges kompatibilitas az M4 statikus listaval ---")
    static_seeds = generate_plate_seeds(world_seed, plate_count)
    topo0 = resolve_topology(world_seed, 0.0, plate_count)
    assert len(topo0) == plate_count, "t=0-nal a lemezszamnak meg kell egyeznie plate_count-tal"
    for r in topo0:
        assert r["parentId"] is None and r["generation"] == 0, "t=0-nal minden lemez gyoker (nincs meg split/merge)"
        s0 = static_seeds[r["plateId"]]
        diff = max(abs(r["currentPos"][k] - s0[k]) for k in range(3))
        assert diff < 1e-12, f"lemez {r['plateId']}: t=0 pozicio eltero a statikus M4-tol, diff={diff}"
    print(f"OK - {plate_count} lemez, mind egyezik a statikus M4 lemez-listaval\n")

    print("--- Lemezszam valtozik hosszu ido alatt ---")
    counts_over_time = []
    for t in [0.0, 100.0, 250.0, 400.0, 600.0, 800.0, 1000.0, 1500.0, 2000.0]:
        topo = resolve_topology(world_seed, t, plate_count)
        counts_over_time.append((t, len(topo)))
    print("  t (Myr) -> lemezszam:", counts_over_time)
    distinct_counts = {c for _, c in counts_over_time}
    assert len(distinct_counts) > 1, "A lemezszamnak valtoznia kell idovel (split/merge esemenyek miatt)"
    print("OK - a lemezszam nem allando, split/merge esemenyek tortennek\n")

    print("--- Konkret PlateSplitEvent ellenorzese (terulet ~ felezodik) ---")
    split_plate_id, split_time = None, None
    for pid in range(plate_count):
        roll = plate_lifecycle_roll(world_seed, pid)
        if roll["kind"] == "split" and roll["lifespanMyr"] < 1000.0:
            split_plate_id, split_time = pid, roll["lifespanMyr"]
            break
    assert split_plate_id is not None, "Nem talalhato split-esemeny a demo-parameterekkel - valasszunk mas plate_id-t"
    before = resolve_topology(world_seed, split_time - 1.0, plate_count)
    after = resolve_topology(world_seed, split_time + 1.0, plate_count)
    before_ids = {r["plateId"] for r in before}
    after_ids = {r["plateId"] for r in after}
    assert split_plate_id in before_ids and split_plate_id not in after_ids, \
        "A split-ido kornyeken a szulo-lemeznek el kell tunnie a splitidopont utan"
    assert len(after_ids) == len(before_ids) + 1, "Egy split pontosan +1 lemezt ad"
    children = [r for r in after if r["parentId"] == split_plate_id]
    assert len(children) == 2, "Pontosan ket gyermek-lemeznek kell szuletnie"

    areas_before = estimate_areas(before, level=4)
    areas_after = estimate_areas(after, level=4)
    parent_area = areas_before[split_plate_id]
    child_areas = [areas_after[c["plateId"]] for c in children]
    combined = sum(child_areas)
    print(f"  szulo (lemez {split_plate_id}) terulete split elott: {parent_area:.4f}")
    print(f"  ket gyermek terulete split utan: {child_areas[0]:.4f} + {child_areas[1]:.4f} = {combined:.4f}")
    assert 0.6 * parent_area < combined < 1.4 * parent_area, \
        "A ket gyermek egyuttes teruletenek kozel a szulo eredeti teruletehez kell lennie"
    lo, hi = min(child_areas), max(child_areas)
    assert lo > 0.25 * combined, "A split ne legyen tulsagosan aszimmetrikus (kb. felezodes varhato)"
    print("OK - a split kb. felezi a szulo teruletet a ket gyermek kozott\n")

    print("--- Konkret PlateMergeEvent / SubductionTermination ellenorzese ---")
    merge_plate_id, merge_time = None, None
    for pid in range(plate_count):
        roll = plate_lifecycle_roll(world_seed, pid)
        if roll["kind"] == "merge" and roll["lifespanMyr"] < 1000.0:
            merge_plate_id, merge_time = pid, roll["lifespanMyr"]
            break
    assert merge_plate_id is not None, "Nem talalhato merge-esemeny a demo-parameterekkel"
    before_m = resolve_topology(world_seed, merge_time - 1.0, plate_count)
    after_m = resolve_topology(world_seed, merge_time + 1.0, plate_count)
    before_m_ids = {r["plateId"] for r in before_m}
    after_m_ids = {r["plateId"] for r in after_m}
    assert merge_plate_id in before_m_ids and merge_plate_id not in after_m_ids, \
        "A merge-ido kornyeken a lemeznek el kell tunnie a merge-idopont utan"
    assert len(after_m_ids) == len(before_m_ids) - 1, "Egy merge pontosan -1 lemezt ad (nincs uj plateId)"
    print(f"  lemez {merge_plate_id} megszunt t={merge_time:.1f} Myr-nel, lemezszam {len(before_m_ids)} -> {len(after_m_ids)}")
    print("OK - a merge pontosan eltunteti a lemezt, a terulet a szomszedoke lesz Voronoi-n keresztul\n")

    print("--- RiftActivation megelozi a PlateSplitEvent-et ---")
    r_roll = plate_lifecycle_roll(world_seed, split_plate_id)
    rift_time = r_roll["lifespanMyr"] * r_roll["riftFractionOfLifespan"]
    assert 0.0 < rift_time < r_roll["lifespanMyr"], "A rift-aktivacio a szuletes es a split kozott kell essen"
    topo_at_rift = resolve_topology(world_seed, rift_time + 0.5, plate_count)
    rift_record = next(r for r in topo_at_rift if r["plateId"] == split_plate_id)
    assert rift_record["riftActivationTimeMyr"] is not None, "A split-re sorsolt lemeznek van rift-aktivacios ideje"
    assert abs(rift_record["riftActivationTimeMyr"] - rift_time) < 1e-9
    print(f"  lemez {split_plate_id}: rift aktivacio t={rift_time:.1f} Myr, split t={r_roll['lifespanMyr']:.1f} Myr")
    print("OK - a RiftActivation idopontja korabbi, mint a PlateSplitEvent, es lekerdezheto\n")

    print("--- Timestep-invariancia (ND-04) ---")
    # "Kozvetlen" lekerdezes: egyetlen hivas egy tavoli t-re.
    direct = resolve_topology(world_seed, 725.0, plate_count)
    # "Lepesenkenti szimulacio": vegigmegyunk sok kozbenso t-n (mintha egy
    # step-based hivo kod lenne, ami minden step-nel ujra lekerdezi az
    # allapotot) - MIVEL a fuggveny tiszta es nincs semmilyen mutable
    # allapot/memoizalas kozte, ez semmilyen modon nem "szennyezheti" a
    # vegso eredmenyt.
    for t_step in [50.0, 123.0, 200.0, 333.0, 500.0, 600.0, 700.0]:
        _ = resolve_topology(world_seed, t_step, plate_count)
    stepped_then_direct = resolve_topology(world_seed, 725.0, plate_count)

    def _topology_signature(topo):
        return [
            (r["plateId"], round(r["currentPos"][0], 12), round(r["currentPos"][1], 12),
             round(r["currentPos"][2], 12), r["generation"], r["parentId"])
            for r in topo
        ]

    sig_direct = _topology_signature(direct)
    sig_stepped = _topology_signature(stepped_then_direct)
    assert sig_direct == sig_stepped, "A t=725 lekerdezesnek fuggetlennek kell lennie attol, hogy elotte mas t-ket kerdeztunk-e le"
    assert direct == stepped_then_direct, "Bitre azonos dict-eket varunk (nincs kerekites-eltolodas)"
    print(f"OK - t=725 Myr lekerdezese {len(direct)} lemezt ad, fuggetlenul a kozbenso lekerdezesektol\n")

    print("--- Determinizmus (ismetelt hivas azonos) ---")
    a1 = resolve_topology(world_seed, 333.0, plate_count)
    a2 = resolve_topology(world_seed, 333.0, plate_count)
    assert a1 == a2, "Nem tiszta fuggveny!"
    print("OK - determinisztikus (tiszta fuggveny)\n")

    print("--- Kulonbozo world_seed mas eletciklus-utemezest ad ---")
    roll_a = plate_lifecycle_roll(world_seed, 0)
    roll_b = plate_lifecycle_roll(world_seed + 1, 0)
    assert roll_a != roll_b, "Kulonbozo seed ugyanazt az eletciklus-sorsolast adta - gyanus"
    print("OK - mas world_seed mas lemez-eletrajzot general\n")

    print("--- Tesztvektorok generalasa (C# porthoz) ---")
    # 1) plate_lifecycle_roll tesztvektorok: kis (gyoker-szeru) plateId-k +
    # nehany nagy, 64-bites (gyermek-szeru) szintetikus id, tobb world_seed-re.
    roll_vectors = []
    for ws in [world_seed, 0x1, 0xDEADBEEF, 0xFFFFFFFFFFFFFFFF]:
        for pid in list(range(20)) + [0x0123456789ABCDEF, 0xFEDCBA9876543210, (1 << 63) + 7]:
            roll = plate_lifecycle_roll(ws, pid)
            roll_vectors.append({"worldSeed": ws, "plateId": pid, **roll})

    # 2) Topologia-pillanatkepek: nehany (world_seed, plate_count, timeMyr)
    # harmas, a teljes aktiv-lista rekordokkal.
    topology_vectors = []
    for ws, pc in [(world_seed, 10), (world_seed, 6), (0xC0FFEE, 8)]:
        for t in [0.0, 150.0, 400.0, 900.0, 1800.0]:
            topo = resolve_topology(ws, t, pc)
            topology_vectors.append({
                "worldSeed": ws, "plateCount": pc, "timeMyr": t,
                "plates": [
                    {
                        "plateId": r["plateId"],
                        "posX": r["currentPos"][0], "posY": r["currentPos"][1], "posZ": r["currentPos"][2],
                        "axisX": r["axis"][0], "axisY": r["axis"][1], "axisZ": r["axis"][2],
                        "omega": r["omega"],
                        "birthTimeMyr": r["birthTimeMyr"],
                        "generation": r["generation"],
                        "parentId": r["parentId"],
                        "eventKind": r["eventKind"],
                        "pendingEventTimeMyr": r["pendingEventTimeMyr"],
                        "riftActivationTimeMyr": r["riftActivationTimeMyr"],
                    }
                    for r in topo
                ],
            })

    # Determinizmus-fuzeteszt: a JSON-generalas soran hasznalt threefry
    # ujra-hivasa (a diszkrecionalis szurashoz hasznalt PRNG-csatorna
    # bemutatasa - konzisztens a tobbi *_ref.py mintajaval).
    _probe = threefry4x64([1, 2, 3, 4], [world_seed, 0, 0, 0], 20)
    assert threefry4x64([1, 2, 3, 4], [world_seed, 0, 0, 0], 20) == _probe, "threefry4x64 nem tiszta fuggveny!"

    with open("plate_lifecycle_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed,
            "constants": {
                "pSplit": P_SPLIT, "pMerge": P_MERGE,
                "minLifespanMyr": MIN_LIFESPAN_MYR, "maxLifespanMyr": MAX_LIFESPAN_MYR,
                "riftFractionMin": RIFT_FRACTION_MIN, "riftFractionMax": RIFT_FRACTION_MAX,
                "splitHalfAngleRad": SPLIT_HALF_ANGLE_RAD, "maxGeneration": MAX_GENERATION,
            },
            "rollVectors": roll_vectors,
            "topologyVectors": topology_vectors,
        }, f, indent=1)
    print(f"{len(roll_vectors)} eletciklus-sorsolas + {len(topology_vectors)} topologia-pillanatkep elmentve "
          f"plate_lifecycle_vectors.json-ba")
