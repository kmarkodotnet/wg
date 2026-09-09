"""
Tavak (spec 35.), jeg/ho (spec 36.) es statikus (A1) eroziós visszahatas
(spec 18.) referencia-implementacioja M7-hez
(docs/backlog.md "M7 | Tavak, jeg/ho, eroziós visszahatas").

HATOKOR (tudatosan szukitve - lasd a feladat also hatarat is):
  - Ez a STATIKUS domborzat-allapotra epul (egyetlen elevacio-mezo, ahogy
    az M4/M7 mar megvan a hydrology_ref.py-ban). NEM az idovel felhalmozodo,
    deep-time eroziós/eljegesedesi lanc (az M10 / ND-44 kulon feladat).
  - A tavak topografiai mennyedes-alapuak (spec 35. elso tetele: "topografiai
    melyedes"). A tobbi kialakulasi mod (gleccser, krater, tektonikus
    medence, folyoelzaras) NEM ehhez a passzhoz tartozik - azok mar most is
    IMPLICIT modon topografiai melyedeskent jelennek meg a domborzatban
    (pl. a krater-mezo mar bevesi magat az elevacioba mashol), tehat ez a
    detektor oket is megtalalja, csak nem a keletkezesi ok szerint
    kulonbozteti meg oket. A "tavak idoben" resz (feltoltodhet, kiszaradhat,
    tullfolyhat, tengerrel kapcsolatba kerulhet) NEM resze ennek a statikus
    passznak - az idofuggo allapot.
  - A jeg/ho csak STATIKUS OSZTALYOZAS (permanens jeg / szezonalis ho /
    nincs), a spec 36.2 "Accumulation > Melt" felteteljet egy homerseklet-
    kuszobre egyszerusitve. A 36.3 gleccserarmlas-diffuzio (vastagsag+lejto
    alapu, eroziot/volgyeket/morenakat/tengerszintet befolyasolo) EXPLICIT
    MODON HALASZTVA - ez onallo, nagyobb feladat (ld. ND-43 lent).
  - Az eroziós pass (18.2) EGYETLEN additiv korrekcio, nem egy idoben
    integralt differencialegyenlet. Az irany a fontos (magas
    flow-accumulation + meredek lejto -> bevagodik; alacsonyabban enyhen
    feltoltodik), a pontos uledek-merleg durva kozelites - ld. ND-43.

Minden modellezesi dontes, amit a spec nem specifikal egyertelmuen (jeg/ho
homerseklet-kuszobok, eroziós egyutthatok, to-melyseg-kuszob), dokumentalva
van docs/04-decisions.md ND-43 alatt.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import json
import math

from hydrology_ref import (
    compute_elevation_and_ocean_field,
    priority_flood,
    flow_accumulation,
)
from neighbor_ref import neighbor, DIRECTIONS
from sphere_position_ref import position_from_tile
from temperature_ref import temperature_kelvin
from threefry_ref import threefry4x64


# ---------------------------------------------------------------------------
# 1. Tavak (spec 35.) - topografiai melyedes-alapu detektalas
# ---------------------------------------------------------------------------

# ND-43: numerikus zaj (lebegopontos kerekites a priority-flood-ban) miatt
# egy minimalis melyseg-kuszob kell, kulonben szinte minden sik tile-t
# "to"-nak jelolnenk egy epsilon-nyi feltoltes miatt.
LAKE_MIN_DEPTH_M = 0.5


def identify_lakes(field, filled, is_ocean, level, min_depth=LAKE_MIN_DEPTH_M):
    """
    Egy tile "to", ha a priority-flood feltoltott (viz-)szintje magasabb,
    mint a nyers elevacio, ES nem ocean - ez PONTOSAN azokat a tile-okat
    jeloli ki, amiket a depresszio-feltoltes "vizzel" toltott fel, hogy
    elerjenek egy lefolyast (topografiai medence, spec 35.).

    A priority-flood korrektsegi tulajdonsaga (Barnes et al.) miatt egy
    osszefuggo medence minden belso (nem-sillponti) tile-ja UGYANARRA a
    feltoltott szintre kerul (a medence kifolyasi/sill pontjanak
    magassagara) - ezert az osszefuggo "to"-tile-ok komponense tipikusan
    egyetlen kozos vizszintet kepvisel. Ritka, tobbszintu (teraszos)
    medekeknel ez nem szigoruan igaz - ott a komponens min/max feltoltott
    szintjet is jelentjuk, es a kettotol elteresre figyelmeztetunk (ld.
    ND-43).

    Visszaad: is_lake (dict), depth (dict, meterben), lake_id (dict, csak a
    to-tile-okra), lakes (lista, minden elem: id/tiles/tileCount/
    surfaceElevation/minSurface/maxSurface/maxDepth/meanDepth).
    """
    is_lake = {}
    depth = {}
    for k in field:
        d = filled[k] - field[k]
        if (not is_ocean[k]) and d > min_depth:
            is_lake[k] = True
            depth[k] = d
        else:
            is_lake[k] = False
            depth[k] = 0.0

    lake_id = {}
    lakes = []
    visited = set()
    for k in field:
        if not is_lake[k] or k in visited:
            continue
        comp = []
        stack = [k]
        visited.add(k)
        while stack:
            cur = stack.pop()
            comp.append(cur)
            face, u, v = cur
            for d in DIRECTIONS:
                nb = neighbor(face, level, u, v, d)
                if is_lake.get(nb, False) and nb not in visited:
                    visited.add(nb)
                    stack.append(nb)

        this_id = len(lakes)
        for t in comp:
            lake_id[t] = this_id
        depths = [depth[t] for t in comp]
        surfaces = [filled[t] for t in comp]
        lakes.append({
            "id": this_id,
            "tiles": comp,
            "tileCount": len(comp),
            "surfaceElevation": sum(surfaces) / len(surfaces),
            "minSurface": min(surfaces),
            "maxSurface": max(surfaces),
            "maxDepth": max(depths),
            "meanDepth": sum(depths) / len(depths),
        })

    return is_lake, depth, lake_id, lakes


# ---------------------------------------------------------------------------
# 2. Jeg es ho (spec 36.) - statikus, homerseklet-kuszobos osztalyozas
# ---------------------------------------------------------------------------

# ND-43: a temperature_ref.temperature_kelvin egy adott naphoz (day_t) a
# napi atlagot adja vissza. Az EVES jellemzeshez ezt tovabb mintavetelezzuk
# a keringesi periodus (orbital_period) menten - ugyanaz a "suru
# mintavetelezes zart formula helyett" elv, mint a temperature_ref.py-ban.
NUM_ANNUAL_SAMPLES = 12

FREEZING_POINT_K = 273.15  # a viz fagyaspontja - fizikai allando, nem becsult ertek

# ND-43: az eves atlaghomerseklet ez ALATT -> a jegmerleg (Accumulation >
# Melt, spec 36.2) tartosan pozitivnak tekintheto, tehat permanens
# jegtakaro. A -15 C-os kuszob a temperature_ref parametereivel (Fold-szeru
# napallando/tengely-dolt/33K uveghazhatas) ugy lett kalibralva (ld. a modul
# also __main__-jenek szelesseg-tablazata), hogy kb. 50-55 fok szelesseg
# folott adjon permanens jeget - plauzibilis analogia a valodi sarkkori
# jegsapkakhoz, DE NEM egy hivatkozott klimatologiai kuszobertek, csupan
# ehhez a leegyszerusitett homerseklet-modellhez illesztett, dokumentalt
# heurisztika.
PERMANENT_ICE_MEAN_THRESHOLD_K = 258.15  # -15 C eves atlag

# ND-43: ha az eves atlag a permanens kuszob folott van, de a leghidegebb
# mintavett honap fagypont alatt van, szezonalis ho keletkezik.
SEASONAL_SNOW_MIN_THRESHOLD_K = FREEZING_POINT_K


def annual_temperature_stats(
    tile_position, orbital_period, rotation_period, axial_tilt,
    is_oceanic, elevation_m, sea_level_m,
    orbital_phase0=0.0, rotation_phase0=0.0, num_samples=NUM_ANNUAL_SAMPLES,
):
    """Eves (mean, min, max) napi-atlag homerseklet, suru mintavetellel a keringesi periodus alatt."""
    temps = []
    for i in range(num_samples):
        day_t = i * (orbital_period / num_samples)
        t = temperature_kelvin(
            tile_position, day_t, orbital_period, rotation_period, axial_tilt,
            is_oceanic, elevation_m, sea_level_m, orbital_phase0, rotation_phase0,
        )
        temps.append(t)
    return sum(temps) / len(temps), min(temps), max(temps)


def classify_ice(mean_annual_k, min_annual_k):
    """-> "permanent_ice" | "seasonal_snow" | "none". Ld. ND-43 a kuszobokrol."""
    if mean_annual_k < PERMANENT_ICE_MEAN_THRESHOLD_K:
        return "permanent_ice"
    if min_annual_k < SEASONAL_SNOW_MIN_THRESHOLD_K:
        return "seasonal_snow"
    return "none"


# ---------------------------------------------------------------------------
# 3. Statikus (A1) eroziós visszahatas (spec 18.2), egyetlen additiv korrekcio
# ---------------------------------------------------------------------------

# ErosionRate = k * Rainfall^alpha * Slope^beta * MaterialFactor (spec 18.2)
#
# ND-43 - a kepletben szereplo tenyezok proxyi ES a leegyszerusites, amit
# ez a statikus (nem idoben integralt) pass alkalmaz:
#   Rainfall     -> flow accumulation normalizalva [0,1]-re (a vizgyujto
#                   meret aranyos a lefolyo csapadekkal - ugyanaz a proxy,
#                   amit a hydrology_ref mar hasznal a folyo-kuszobolesnel).
#   Slope        -> |nyers elevacio(k) - nyers elevacio(parent(k))|,
#                   normalizalva a szarazfoldi maximummal [0,1]-re. A parent
#                   a priority-flood folyasirany-celpontja - FONTOS: ez a
#                   FELTOLTOTT (nem nyers) magassag szerint monoton csokken
#                   celba, a NYERS elevaciokulonbseg elojele ezert nem
#                   feltetlenul "lefele" - abszolut ertekkel kezeljuk, mert
#                   itt csak a lejto MEREDEKSEGE erdekel, nem az iranya.
#   MaterialFactor -> 1.0 (konstans - nincs meg kulon litologia/kozetkemenyseg
#                   modul, ld. docs/00-spec-v1.0.md kesobbi §-ok). Amikor
#                   lesz kozettipus-mezo, ez lesz a csatlakozasi pont.
#   alpha, beta  -> 0.5 / 1.0. Ez a "stream power law" nevu, a folyo-
#                   bevagodas modellezeseben altalanosan hasznalt fuggveny-
#                   alak (E = K*A^m*S^n, tipikus m~0.5, n~1) FUGGVENYFORMAJA
#                   - NEM egy adott publikacio szamertekre hivatkozunk itt,
#                   csak az egyenlet ALAKJAT vesszuk at, a sajat K
#                   egyutthatonkat kulon kalibraljuk (EROSION_MAX_DEPTH_M).
#
# EROSION_MAX_DEPTH_M a "k" egyutthato absztrakcioja: az elmeleti maximalis
# egyszeri-pass bevagodas meter-ben (amikor a normalizalt accumulation ES
# slope is 1.0 - a gyakorlatban ez a ket szelsoseg ritkan esik egybe, tehat
# a tenylegesen megfigyelt maximum ez alatt marad). Ertekét ugy valasztottuk,
# hogy a jelenlegi szarazfoldi elevaciotartomany (kb. 2500-3800m, ld. a
# crust_elevation_ref OCEANIC/CONTINENTAL_BASE + NOISE_AMPLITUDE ertekeit)
# kis toredeket (nagysagrendileg szazalek) tegye ki egyetlen statikus
# passzban - fizikailag egy teljes folyovolgy TOBB geologiai korban alakul
# ki, nem egy lepesben.
EROSION_ALPHA = 0.5
EROSION_BETA = 1.0
EROSION_MAX_DEPTH_M = 250.0
MATERIAL_FACTOR = 1.0

# ND-43: az eroziós anyag hany reszet rakja le a KOZVETLEN lefele-szomszed
# (parent) - a maradek "tovabb szallitodik" (ebben az egy-lepeses
# kozelitesben egyszeruen elvesz / a tengerbe jut, nem koveti tovabb a
# teljes lancot). Csak akkor rakodik le, ha a parent SZARAZFOLD - ha a
# parent ocean, az uledek a tengerfenekre kerul, ami NEM resze ennek a
# szarazfoldi elevacio-mezonek.
DEPOSIT_FRACTION = 0.3


def apply_static_erosion_pass(
    field, parent, is_ocean, accumulation,
    alpha=EROSION_ALPHA, beta=EROSION_BETA, max_depth_m=EROSION_MAX_DEPTH_M,
    material_factor=MATERIAL_FACTOR, deposit_fraction=DEPOSIT_FRACTION,
):
    """
    Egyetlen additiv korrekcio: new_field[k] = field[k] - erosion[k] + deposition[k].
    Csak szarazfoldi tile-okra hat (az ocean-fenek ebben a passzban
    valtozatlan). Visszaadja: new_field, erosion (dict, meter, csak
    szarazfoldre >0), deposition_gain (dict, meter).
    """
    land = [k for k in field if not is_ocean[k]]
    if not land:
        return dict(field), {k: 0.0 for k in field}, {k: 0.0 for k in field}

    max_acc = max(accumulation[k] for k in land)
    max_acc = max(max_acc, 1)

    slope_raw = {}
    for k in land:
        p = parent[k]
        slope_raw[k] = abs(field[k] - field[p]) if p is not None else 0.0
    max_slope = max(slope_raw.values())
    max_slope = max(max_slope, 1e-9)

    erosion = {k: 0.0 for k in field}
    for k in land:
        acc_norm = accumulation[k] / max_acc
        slope_norm = slope_raw[k] / max_slope
        erosion[k] = max_depth_m * (acc_norm ** alpha) * (slope_norm ** beta) * material_factor

    deposition_gain = {k: 0.0 for k in field}
    for k in land:
        p = parent[k]
        if p is not None and (not is_ocean[p]):
            deposition_gain[p] += erosion[k] * deposit_fraction

    new_field = dict(field)
    for k in land:
        new_field[k] = field[k] - erosion[k] + deposition_gain[k]

    return new_field, erosion, deposition_gain


if __name__ == "__main__":
    world_seed = 0xA7C944210000
    plate_count = 20
    level = 6
    orbital_period = 365.25
    rotation_period = 1.0
    axial_tilt = math.radians(23.44)

    print("Elevacio-mezo + tengerszint + priority-flood (hydrology_ref ujrafelhasznalva)...")
    field, sea_level, is_ocean = compute_elevation_and_ocean_field(world_seed, plate_count, level)
    filled, parent, flood_order = priority_flood(field, is_ocean, level)
    accumulation = flow_accumulation(field, parent, flood_order)
    print(f"Tengerszint: {sea_level:.1f}m, {sum(is_ocean.values())}/{len(field)} ocean-tile\n")

    # --- 1. Tavak ---------------------------------------------------------
    print("To-detektalas...")
    is_lake, depth, lake_id, lakes = identify_lakes(field, filled, is_ocean, level)
    lake_tile_count = sum(1 for v in is_lake.values() if v)
    print(f"  {len(lakes)} to, osszesen {lake_tile_count} to-tile")
    if lakes:
        biggest = max(lakes, key=lambda L: L["tileCount"])
        print(f"  legnagyobb to: id={biggest['id']} tileCount={biggest['tileCount']} "
              f"maxDepth={biggest['maxDepth']:.1f}m surfaceElev={biggest['surfaceElevation']:.1f}m")
        # Diagnosztika: hany to nem egysziku (min/max surface elter) - ND-43 szerinti ritka eset
        multi_level = [L for L in lakes if L["maxSurface"] - L["minSurface"] > 0.01]
        print(f"  nem-egysziku (teraszos) tavak: {len(multi_level)}/{len(lakes)}")

    assert len(lakes) > 0, "Legalabb egy topografiai medencenek to-va kellene valnia ezen a terepen"
    for L in lakes:
        for t in L["tiles"]:
            assert not is_ocean[t], "To-tile nem lehet ocean"
            assert filled[t] > field[t] + LAKE_MIN_DEPTH_M - 1e-9, "A to-szintnek a nyers elevacio folott kell lennie"
    print("OK - minden to topografiai melyedesben van, feltoltott szintje a nyers elevacio folott\n")

    # --- 2. Jeg / ho --------------------------------------------------------
    print("Jeg/ho plauzibilitas szelesseg szerint (eves atlag, tengerszinten):")
    for lat_deg in [0, 15, 30, 45, 60, 75, 90]:
        lat = math.radians(lat_deg)
        pos = (math.cos(lat), 0.0, math.sin(lat))
        mean_t, min_t, max_t = annual_temperature_stats(
            pos, orbital_period, rotation_period, axial_tilt, False, 0.0, 0.0,
        )
        cls = classify_ice(mean_t, min_t)
        print(f"  szelesseg={lat_deg:3d} fok: mean={mean_t:6.1f}K min={min_t:6.1f}K -> {cls}")

    equator_pos = (1.0, 0.0, 0.0)
    pole_pos = (0.0, 0.0, 1.0)
    eq_mean, eq_min, _ = annual_temperature_stats(
        equator_pos, orbital_period, rotation_period, axial_tilt, False, 0.0, 0.0,
    )
    pole_mean, pole_min, _ = annual_temperature_stats(
        pole_pos, orbital_period, rotation_period, axial_tilt, False, 0.0, 0.0,
    )
    assert classify_ice(pole_mean, pole_min) == "permanent_ice", "A sarki tile-nak permanens jegesnek kell lennie"
    assert classify_ice(eq_mean, eq_min) == "none", "Az egyenlitoi tile-nak jegmentesnek kell lennie"
    print("\nOK - a sarki tile permanens jeges, az egyenlitoi jegmentes")

    # Magassag hatasa: egy magas hegy az egyenlitonel is lehet jeges
    high_mountain_mean, high_mountain_min, _ = annual_temperature_stats(
        equator_pos, orbital_period, rotation_period, axial_tilt, False, 6000.0, 0.0,
    )
    print(f"\nEgyenlitoi 6000m csucs: mean={high_mountain_mean:.1f}K -> {classify_ice(high_mountain_mean, high_mountain_min)}")
    assert classify_ice(high_mountain_mean, high_mountain_min) in ("permanent_ice", "seasonal_snow"), \
        "Egy eleg magas egyenlitoi csucsnak legalabb szezonalisan hosnak/jegnek kell lennie"
    print("OK - a magassag (lapse rate) helyesen hoz letre hegyi jeget/havat az egyenlitonel is")

    # --- 3. Statikus eroziós pass --------------------------------------------
    print("\nStatikus (A1) eroziós pass...")
    new_field, erosion, deposition_gain = apply_static_erosion_pass(field, parent, is_ocean, accumulation)

    land_tiles = [k for k in field if not is_ocean[k]]
    eroded_tiles = [k for k in land_tiles if erosion[k] > 1e-9]
    print(f"  {len(eroded_tiles)}/{len(land_tiles)} szarazfold-tile-on van merheto bevagodas")

    # A legnagyobb accumulation-u szarazfold-tile (tipikusan a folyo-torkolat
    # / delta kozelebe esik). FONTOS: ennek a SAJAT bevagodasa (erosion[])
    # lehet KICSI (ha a delta lapos), miközben a VEGSO elevacioja megis
    # NOHET, mert a felvizi (magas erosion-u) szomszedai ide rakjak le az
    # uledekuk egy reszet - ez FIZIKAILAG HELYES viselkedes (deltakepzodes,
    # ld. spec 18.2 "Az uledek alacsonyabb helyeken lerakodhat"), NEM hiba.
    # Ezert a "magasabb accumulation -> nagyobb SAJAT bevagodas" allitast a
    # nyers erosion[] szotaron teszteljuk, a vegso new_field-en pedig csak
    # azt, hogy VALAHOL tisztan bevagodik, VALAHOL tisztan lerakodik.
    river_trunk = max(land_tiles, key=lambda k: accumulation[k])
    print(f"  legnagyobb accumulation-u tile: accumulation={accumulation[river_trunk]}, "
          f"sajat bevagodas={erosion[river_trunk]:.1f}m, kapott lerakodas={deposition_gain[river_trunk]:.1f}m, "
          f"elevacio {field[river_trunk]:.1f}m -> {new_field[river_trunk]:.1f}m")

    # A kepletnek (accumulation^alpha * slope^beta) kozvetlenul, a lerakodas
    # zaja NELKUL kell monoton viselkednie - ezt az erosion[] szotaron
    # ellenorizzuk: a legnagyobb sajat-bevagodasu tile-nak erdemben nagyobb
    # erosion-je legyen, mint egy sik/alacsony-accumulation tile-nak.
    steepest_tile = max(land_tiles, key=lambda k: erosion[k])
    flat_tile = min(land_tiles, key=lambda k: accumulation[k])
    print(f"  legnagyobb SAJAT bevagodasu tile: accumulation={accumulation[steepest_tile]}, "
          f"bevagodas={erosion[steepest_tile]:.1f}m")
    print(f"  lapos, alacsony-accumulation tile: accumulation={accumulation[flat_tile]}, "
          f"bevagodas={erosion[flat_tile]:.4f}m")
    assert erosion[steepest_tile] > erosion[flat_tile], \
        "A magas accumulation+lejto kombinaciojanak nagyobb SAJAT bevagodast kell adnia, mint egy lapos tile-nak"
    assert erosion[steepest_tile] > 0.0, "Legalabb egy tile-nak merhetoen be kell vagodnia"

    assert max(erosion.values()) <= EROSION_MAX_DEPTH_M + 1e-6, "A bevagodas nem lephet a kalibralt plafon fole"
    total_deposit = sum(v for v in deposition_gain.values() if v > 0)
    assert total_deposit > 0.0, "Legalabb valahol le kell rakodnia uledeknek"

    net_change = {k: new_field[k] - field[k] for k in land_tiles}
    most_incised = min(net_change, key=lambda k: net_change[k])
    most_deposited = max(net_change, key=lambda k: net_change[k])
    assert net_change[most_incised] < 0.0, "Valahol tisztan be kell vagodnia a domborzatnak"
    assert net_change[most_deposited] > 0.0, "Valahol tisztan le kell rakodnia uledeknek (spec 18.2 utolso mondata)"
    print(f"  osszes lerakodott uledek: {total_deposit:.1f}m (elosztva a lefele-szomszedokon)")
    print(f"  legnagyobb NETTO bevagodas: {net_change[most_incised]:.1f}m, "
          f"legnagyobb NETTO feltoltodes: {net_change[most_deposited]:.1f}m")
    print("OK - a bevagodas a formula szerint skalazodik, es valahol bevagodas, valahol lerakodas tortenik\n")

    # Determinizmus-ellenorzes (ismetelt hivas azonos eredmenyt ad)
    is_lake2, depth2, lake_id2, lakes2 = identify_lakes(field, filled, is_ocean, level)
    new_field2, erosion2, deposition_gain2 = apply_static_erosion_pass(field, parent, is_ocean, accumulation)
    assert is_lake == is_lake2 and lake_id == lake_id2, "A to-detektalas nem determinisztikus!"
    assert erosion == erosion2 and new_field == new_field2, "Az eroziós pass nem determinisztikus!"
    print("OK - determinisztikus (ismetelt hivas azonos eredmenyt ad)\n")

    # --- Tesztvektorok a C# porthoz -----------------------------------------
    # Threefry-mintavetel a mar kiszamolt teljes mezobol (ugyanaz a minta,
    # mint a hydrology_ref.py-ban) - to/jeg/erozio adatokkal kiegeszitve.
    n = 1 << level
    keys = list(field.keys())
    vectors = []
    gen_seed = 0x1AC5_1CE0_E205_0001
    for i in range(500):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 12, 0], 20)
        key = keys[p[0] % len(keys)]
        face, u, v = key
        pos = position_from_tile(face, level, u, v)
        mean_t, min_t, max_t = annual_temperature_stats(
            pos, orbital_period, rotation_period, axial_tilt, is_ocean[key], field[key], sea_level,
        )
        ice_cls = classify_ice(mean_t, min_t)
        vectors.append({
            "face": face, "u": u, "v": v,
            "isOcean": is_ocean[key],
            "rawElevation": field[key],
            "filled": filled[key],
            "isLake": is_lake[key],
            "lakeId": lake_id.get(key),
            "lakeDepth": depth[key],
            "annualMeanTemperatureK": mean_t,
            "annualMinTemperatureK": min_t,
            "iceClass": ice_cls,
            "erosionDepth": erosion[key],
            "depositionGain": deposition_gain[key],
            "newElevation": new_field[key],
        })

    with open("lakes_ice_erosion_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count, "level": level,
            "orbitalPeriod": orbital_period, "rotationPeriod": rotation_period,
            "axialTilt": axial_tilt, "seaLevel": sea_level,
            "lakeMinDepthM": LAKE_MIN_DEPTH_M,
            "permanentIceMeanThresholdK": PERMANENT_ICE_MEAN_THRESHOLD_K,
            "seasonalSnowMinThresholdK": SEASONAL_SNOW_MIN_THRESHOLD_K,
            "erosionAlpha": EROSION_ALPHA, "erosionBeta": EROSION_BETA,
            "erosionMaxDepthM": EROSION_MAX_DEPTH_M, "depositFraction": DEPOSIT_FRACTION,
            "vectors": vectors,
        }, f, indent=1)
    print(f"{len(vectors)} to/jeg/erozio tesztvektor generalva")
