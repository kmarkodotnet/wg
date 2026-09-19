"""
Talaj / regolit referencia-implementacioja (docs/00-spec-v1.0.md SS39
"Talaj / regolit"), MVP-hatokorrel (docs/01-architecture.md SS13).

A spec teljes `RegolithProfile`-ja 10 mezot sorol fel: Depth, Porosity,
WaterRetention, MineralDiversity, PhosphorusAvailability,
NitrogenAvailability, Iron, Sulfur, Salinity, pHProxy. Ez a modul
TUDATOSAN CSAK HAROM mezot szamol (Depth, Porosity, WaterRetention) - ld.
docs/01-architecture.md SS13.1 a teljes hatokor-indoklasert. Rovid osszefoglalo:

  - A spec 6 forrast sorol fel: alapkozet, vulkanizmus, erozio, uledek,
    viz, homerseklet. Ebbol jelenleg CSAK NEGY (erozio, uledek, viz,
    homerseklet) all rendelkezesre VALODI, tile-onkent VALTOZO Core-
    kimenetkent:
      * erozio/uledek -> `lakes_ice_erosion_ref.apply_static_erosion_pass`
        (mar KAT-vektoros, M7-es modul);
      * viz -> `moisture_transport_ref.compute_precipitation_field`
        (mar KAT-vektoros, M5-os modul);
      * homerseklet -> `lakes_ice_erosion_ref.annual_temperature_stats`
        (mar KAT-vektoros).
  - Az "alapkozet" forras a Core-ban JELENLEG csak egy PLATE-szintu
    bool (`isOceanic`, ld. CrustElevation.cs) - es minden szarazfoldi
    tile ugyanazt az erteket kapja (`isOceanic=False`), tehat NULLA
    tile-kozi VARIANCIAT ad szarazfoldon belul. Amit hasznalni tudunk
    belole: a LEJTO (elevacio-gradiens) mint a csupasz-kozet-kitettseg
    proxyja - ez MAR a docs/01-architecture.md biome-tablazataban is
    szerepel dontesi valtozokent ("Csupasz szikla ... talajmelyseg <
    0.1 m, lejto > 25 fok"), tehat NEM uj feltalalas, hanem a MAR
    dokumentalt kapcsolat kovetese. Valodi litologia/asvanyos-osszetetel
    NINCS modellezve.
  - A "vulkanizmus" forras a Core-ban CSAK epizodikus, ritka VEI8
    esemenyekkent letezik (`VolcanicEruption.cs`, ND-29) - nincs
    perzisztalt, tile-onkenti hamu-/tefra-lerakodas mezo, amit
    felhasznalhatnank. Egy ilyen mezo felepitese (tavolsag-alapu
    lecsengessel, deep-time-ban felhalmozva) ONALLO, ujabb feladat -
    lasd ND-117 a docs/04-decisions.md-ben.

  Emiatt a MARADEK 7 mezo (MineralDiversity, PhosphorusAvailability,
  NitrogenAvailability, Iron, Sulfur, Salinity, pHProxy) MIND litologia/
  vulkanizmus-fuggo lenne, es MIND HALASZTVA marad - kitalalt ertek
  helyett inkabb hianyzik (I4 invarians).

FONTOS TERVEZESI TULAJDONSAG: ez a modul KIZAROLAG a CLAUDE.md tablazata
szerint GARANTALTAN bitpontos muveleteket hasznal (+ - * /, abs, min, max
- nincs Math.Sin/Cos/Exp/Log/Pow a szamitasi lancban), ES NEM hasznal uj
veletlenszam-mintavetelt (nincs uj RandomDomain/RandomProperty igeny) - a
harom kimenet TISZTAN a mar meglevo, VERIFIKALT Core-kimenetek (elevacio/
lejto, erozio, uledek, csapadek, homerseklet) determinisztikus, algebrai
fuggvenye. Emiatt (a Temperature/WindPrecipitation/DeepTimeErosion
modulokkal ellentetben) ennek a modulnak a C# portja ELVBEN BITPONTOSAN
egyezhet a Python-referenciaval, tolerancia nelkul - ezt a C# oldal
donti el majd a tenyleges implementaciokor.

MODELLDONTESEK (a spec csak minosegi forraslistat ad, zart keplet
nelkul - ugyanaz a helyzet, mint SS30-32-nel, ld. ND-41 mintajat):

1. DEPTH (regolit-melyseg, meter). Ket, egymast ERDEMBEN nem
   helyettesito tag osszege:
     - "in-situ" alap: a meredek lejto lekopik/csupasz kozetet mutat
       (DEPTH_MAX_M-tol nullaig, NEGYZETES lecsengessel a normalizalt
       lejtovel - a negyzetes alak azert, hogy a maganos/enyhe lejtok
       viszonylag keveset veszitsenek, a valoban meredek (~1.0 normalizalt)
       tile-ok viszont gyorsan nullara fussanak, konzisztensen a
       biome-tablazat "lejto > 25 fok -> csupasz szikla" hatarahoz);
     - uledek-hozzajarulas: a mar verifikalt statikus eroziós pass
       DepositionGain-je (m) 1:1 aranyban NOVELI a melyseget (friss
       uledek = uj regolit-anyag), az Erosion (m, tulnyomoreszt a
       ALAPKOZETBE valo bevagodas, nem a vekony regolit-sapka
       eltavolitasa) kis egyutthatoval CSOKKENTI.
   Also/felso korlat: [0, DEPTH_ABSOLUTE_CAP_M].

2. POROSITY (0..1, dimenziomentes). Ket VALODI forrasu tag:
     - fagyas-olvadas ("periglacialis aprozodas"): a fizikai
       kozetaprozodas AKKOR a legintenzivebb, amikor az EVES ATLAG-
       homerseklet a fagypont KORUL van (sokszor at- es visszafagy),
       nem amikor tartosan fagyott VAGY tartosan meleg - ezert egy
       "satorfuggveny" (tent function): a fagyponton csucsosodik,
       linearisan lecseng +-FREEZE_THAW_HALF_RANGE_K-n tul nullara;
     - kompakcio: minel FRISSEBB/TOBB az uledek-hozzajarulas a
       melysegben (finom szemcsek, tomorebb csomagolas), annal
       ALACSONYABB a porozitas.
   Also/felso korlat: [0, 1].

3. WATER RETENTION (0..1, dimenziomentes, KAPACITAS - nem aktualis
   nedvesseg-tartalom). Harom VALODI forrasu tag sulyozott osszege:
     - porozitas (tobb pórus -> tobb potencialis vizterfogat);
     - melyseg-hanyad (vastagabb regolit-oszlop -> tobb tarolt viz
       osszesen, "field capacity" jellegu ossztarolo-kepesseg proxy);
     - csapadek-hanyad (a `moisture_transport_ref` mar VERIFIKALT
       csapadek-mezoje - nedvesebb klimaban a matrafolyamatok tobb
       finom/agyagos, jobban vizmegtarto anyagot termelnek - ez a
       "viz" forras KOZVETLEN bekotese).
   A harom egyutthato osszege PONTOSAN 1.0 (0.5+0.3+0.2), tehat a
   also/felso korlat [0,1] a gyakorlatban ritkan aktiv (csak a
   fagyas-olvadas felso sapkaja miatt lehetseges porosity>1 hatasat
   zarja ki elmeletileg - dokumentalt biztonsagi korlat).

Oceani tile-okra a harom kimenet MINDEGYIKE 0.0 (a regolit ebben az
MVP-ben SZARAZFOLDI fogalom - a tengerfeneki uledek mas jelenseg, nincs
resze ennek a modellnek).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import json

from hydrology_ref import (
    compute_elevation_and_ocean_field,
    priority_flood,
    flow_accumulation,
)
from lakes_ice_erosion_ref import (
    apply_static_erosion_pass,
    annual_temperature_stats,
    FREEZING_POINT_K,
)
from moisture_transport_ref import compute_precipitation_field
from sphere_position_ref import position_from_tile
from threefry_ref import threefry4x64

import math

# --- 1. Depth ---------------------------------------------------------------
# A lejto-lecsengest a compute_regolith_profile SZANDEKOSAN negyzetes
# alakban szamolja (ld. modul-doc 1. pont) - nincs kulon konstans hozza,
# a kitevo a kepletbe van irva (slope_factor = (1-slopeNorm)*(1-slopeNorm)).
DEPTH_MAX_M = 2.0                  # m, sik/zavartalan terep "in-situ" regolit-melysege (illusztrativ)
DEPTH_DEPOSITION_GAIN_COEFF = 1.0  # 1 m uledek = 1 m tobblet regolit-melyseg
DEPTH_EROSION_STRIP_COEFF = 0.02   # a bevagodas nagyresze alapkozetet erint, csak kis toredeke a vekony regolit-sapkat
DEPTH_ABSOLUTE_CAP_M = 5.0         # m, plauzibilitasi felso sapka (magas-uledek delta/allufium terulet)

# --- 2. Porosity -------------------------------------------------------------
POROSITY_BASE = 0.35                  # dimenziomentes, laza/toredezett regolit tipikus also alapertek
POROSITY_FREEZE_THAW_COEFF = 0.25     # a fagyas-olvadas maximalis hozzajarulasa
POROSITY_COMPACTION_COEFF = 0.15      # a friss uledek-kompakcio maximalis levonasa
FREEZE_THAW_HALF_RANGE_K = 15.0       # K, a "satorfuggveny" felszelessege a fagypont korul

# --- 3. Water retention -------------------------------------------------------
RETENTION_POROSITY_COEFF = 0.5
RETENTION_DEPTH_COEFF = 0.3
RETENTION_PRECIP_COEFF = 0.2
PRECIP_REFERENCE = 2.0    # a moisture_transport_ref dimenziomentes csapadek-proxyjanak illusztrativ "magas" referenciaerteke


def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def compute_regolith_profile(
    is_ocean, slope_norm, erosion_depth_m, deposition_gain_m,
    annual_mean_temperature_k, precipitation,
    depth_max_m=DEPTH_MAX_M,
    deposition_gain_coeff=DEPTH_DEPOSITION_GAIN_COEFF,
    erosion_strip_coeff=DEPTH_EROSION_STRIP_COEFF,
    depth_absolute_cap_m=DEPTH_ABSOLUTE_CAP_M,
    porosity_base=POROSITY_BASE,
    freeze_thaw_coeff=POROSITY_FREEZE_THAW_COEFF,
    compaction_coeff=POROSITY_COMPACTION_COEFF,
    freeze_thaw_half_range_k=FREEZE_THAW_HALF_RANGE_K,
    retention_porosity_coeff=RETENTION_POROSITY_COEFF,
    retention_depth_coeff=RETENTION_DEPTH_COEFF,
    retention_precip_coeff=RETENTION_PRECIP_COEFF,
    precip_reference=PRECIP_REFERENCE,
):
    """A RegolithProfile MVP harom mezoje (Depth [m], Porosity [0..1],
    WaterRetention [0..1]) - TISZTA fuggveny, kizarolag +-*/abs/min/max.
    Oceani tile-ra mindharom kimenet 0.0 (ld. modul-doc)."""
    if is_ocean:
        return 0.0, 0.0, 0.0

    slope_norm_c = _clamp(slope_norm, 0.0, 1.0)
    slope_factor = (1.0 - slope_norm_c) * (1.0 - slope_norm_c)
    base_depth = depth_max_m * slope_factor
    depth = base_depth + deposition_gain_coeff * max(0.0, deposition_gain_m) \
        - erosion_strip_coeff * max(0.0, erosion_depth_m)
    depth = _clamp(depth, 0.0, depth_absolute_cap_m)

    freeze_thaw_delta = abs(annual_mean_temperature_k - FREEZING_POINT_K)
    freeze_thaw_activity = max(0.0, 1.0 - freeze_thaw_delta / freeze_thaw_half_range_k)
    deposition_fraction = min(1.0, max(0.0, deposition_gain_m) / depth_max_m)
    porosity = porosity_base + freeze_thaw_coeff * freeze_thaw_activity \
        - compaction_coeff * deposition_fraction
    porosity = _clamp(porosity, 0.0, 1.0)

    precip_factor = min(1.0, max(0.0, precipitation) / precip_reference)
    depth_fraction = min(1.0, depth / depth_max_m)
    water_retention = retention_porosity_coeff * porosity \
        + retention_depth_coeff * depth_fraction \
        + retention_precip_coeff * precip_factor
    water_retention = _clamp(water_retention, 0.0, 1.0)

    return depth, porosity, water_retention


def compute_regolith_field(
    world_seed, plate_count, level,
    orbital_period=365.25, rotation_period=1.0, axial_tilt_deg=23.44,
):
    """Teljes racs: elevacio/lejto + statikus eroziós pass + csapadek +
    eves homerseklet-statisztika -> RegolithProfile MVP mezoje minden tile-ra."""
    field, sea_level, is_ocean = compute_elevation_and_ocean_field(world_seed, plate_count, level)
    filled, parent, flood_order = priority_flood(field, is_ocean, level)
    accumulation = flow_accumulation(field, parent, flood_order)
    _, erosion, deposition_gain = apply_static_erosion_pass(field, parent, is_ocean, accumulation)
    precipitation, _, _, _ = compute_precipitation_field(world_seed, plate_count, level)

    axial_tilt = math.radians(axial_tilt_deg)

    land = [k for k in field if not is_ocean[k]]
    slope_raw = {}
    for k in land:
        p = parent[k]
        slope_raw[k] = abs(field[k] - field[p]) if p is not None else 0.0
    max_slope = max(slope_raw.values()) if slope_raw else 0.0
    max_slope = max(max_slope, 1e-9)

    depth = {}
    porosity = {}
    water_retention = {}
    mean_temp = {}
    slope_norm = {}
    for k in field:
        if is_ocean[k]:
            depth[k], porosity[k], water_retention[k] = 0.0, 0.0, 0.0
            mean_temp[k] = None
            slope_norm[k] = 0.0
            continue
        face, u, v = k
        pos = position_from_tile(face, level, u, v)
        mean_t, _min_t, _max_t = annual_temperature_stats(
            pos, orbital_period, rotation_period, axial_tilt, is_ocean[k], field[k], sea_level,
        )
        s_norm = slope_raw[k] / max_slope
        d, p_, wr = compute_regolith_profile(
            False, s_norm, erosion[k], deposition_gain[k], mean_t, precipitation[k],
        )
        depth[k], porosity[k], water_retention[k] = d, p_, wr
        mean_temp[k] = mean_t
        slope_norm[k] = s_norm

    return {
        "field": field, "seaLevel": sea_level, "isOcean": is_ocean,
        "erosion": erosion, "depositionGain": deposition_gain,
        "precipitation": precipitation, "slopeNorm": slope_norm, "meanTempK": mean_temp,
        "depth": depth, "porosity": porosity, "waterRetention": water_retention,
    }


if __name__ == "__main__":
    print("--- 1. Tisztasag: ismetelt hivas azonos eredmenyt ad ---")
    args = (False, 0.3, 12.0, 0.8, 280.0, 1.1)
    r1 = compute_regolith_profile(*args)
    r2 = compute_regolith_profile(*args)
    assert r1 == r2, "compute_regolith_profile nem tiszta fuggveny!"
    print(f"  compute_regolith_profile{args} = {r1}")
    print("OK - ismetelt hivas bitre azonos\n")

    print("--- 2. Oceani tile: mindharom kimenet nulla, barmilyen tobbi bemenettel ---")
    for extreme in [
        (True, 0.0, 0.0, 0.0, 273.15, 0.0),
        (True, 1.0, 1e18, 1e18, 1e18, 1e18),
        (True, -5.0, -5.0, -5.0, -100.0, -5.0),
    ]:
        r = compute_regolith_profile(*extreme)
        assert r == (0.0, 0.0, 0.0), f"Oceani tile nem nulla profilt adott: {extreme} -> {r}"
    print("OK - oceani tile mindig (0,0,0) profilt ad, szelsoseges bemenetekkel is\n")

    print("--- 3. slope_norm: meredek lejto csokkenti a melyseget (0.1m-nel a biome-tablazat kuszobe alatt) ---")
    d_flat, _, _ = compute_regolith_profile(False, 0.0, 0.0, 0.0, 288.0, 1.0)
    d_mid, _, _ = compute_regolith_profile(False, 0.5, 0.0, 0.0, 288.0, 1.0)
    d_steep, _, _ = compute_regolith_profile(False, 1.0, 0.0, 0.0, 288.0, 1.0)
    print(f"  slope_norm=0.0 -> depth={d_flat:.4f}m  slope_norm=0.5 -> depth={d_mid:.4f}m  slope_norm=1.0 -> depth={d_steep:.4f}m")
    assert d_flat > d_mid > d_steep, "A melysegnek szigoruan csokkennie kell a lejtovel"
    assert d_steep == 0.0, "Maximalis (normalizalt=1.0) lejton nulla melysegnek kell lennie (nincs erozio/uledek-tag)"
    assert d_flat == DEPTH_MAX_M, "Sik terepen (0 lejto, 0 erozio/uledek) pontosan DEPTH_MAX_M-nek kell lennie"
    print("OK - a melyseg szigoruan monoton csokken a normalizalt lejtovel\n")

    print("--- 4. deposition_gain / erosion_depth: erdemi, ellentetes iranyu hatas a melysegre ---")
    d_no_dep, _, _ = compute_regolith_profile(False, 0.0, 0.0, 0.0, 288.0, 1.0)
    d_high_dep, _, _ = compute_regolith_profile(False, 0.0, 0.0, 3.0, 288.0, 1.0)
    d_high_ero, _, _ = compute_regolith_profile(False, 0.0, 250.0, 0.0, 288.0, 1.0)
    print(f"  deposition=0 -> {d_no_dep:.4f}m  deposition=3m -> {d_high_dep:.4f}m  erosion=250m -> {d_high_ero:.4f}m")
    assert d_high_dep > d_no_dep, "Tobb uledek-hozzajarulasnak novelnie kell a melyseget"
    assert d_high_ero < d_no_dep, "Nagyobb bevagodasnak csokkentenie kell a melyseget"
    assert d_high_dep == DEPTH_ABSOLUTE_CAP_M, "A magas uledek-hozzajarulasnak a felso sapkat kell elernie ebben a pelda-esetben"
    print("OK - a deposition/erosion erdemben, ellentetes iranyban hat a melysegre\n")

    print("--- 5. Porosity: fagyas-olvadas 'satorfuggveny' - a fagyponton csucsosodik ---")
    _, p_at_freeze, _ = compute_regolith_profile(False, 0.0, 0.0, 0.0, FREEZING_POINT_K, 0.0)
    _, p_far_cold, _ = compute_regolith_profile(False, 0.0, 0.0, 0.0, FREEZING_POINT_K - 100.0, 0.0)
    _, p_far_warm, _ = compute_regolith_profile(False, 0.0, 0.0, 0.0, FREEZING_POINT_K + 100.0, 0.0)
    _, p_near_cold, _ = compute_regolith_profile(False, 0.0, 0.0, 0.0, FREEZING_POINT_K - 5.0, 0.0)
    print(f"  T=fagypont -> porosity={p_at_freeze:.4f}  T=fagypont-100K -> {p_far_cold:.4f}  T=fagypont+100K -> {p_far_warm:.4f}  T=fagypont-5K -> {p_near_cold:.4f}")
    assert p_at_freeze == POROSITY_BASE + POROSITY_FREEZE_THAW_COEFF, "A fagyponton a fagyas-olvadas tagnak maximalisnak kell lennie"
    assert p_at_freeze > p_near_cold > p_far_cold, "A porozitasnak csokkennie kell, ahogy tavolodunk a fagyponttol (hideg iranyba)"
    assert p_at_freeze > p_far_warm, "A porozitasnak csokkennie kell, ahogy tavolodunk a fagyponttol (meleg iranyba)"
    assert p_far_cold == p_far_warm == POROSITY_BASE, "Tavol a fagyponttol (a satorfuggveny tartomanyan tul) az alapertekre kell esnie"
    print("OK - a porozitas a fagyponton csucsosodo satorfuggvenyt kovet, tavol tole az alapertekre esik\n")

    print("--- 6. Porosity: kompakcio (uledek-hozzajarulas csokkenti) ---")
    _, p_no_dep, _ = compute_regolith_profile(False, 0.0, 0.0, 0.0, FREEZING_POINT_K, 0.0)
    _, p_full_dep, _ = compute_regolith_profile(False, 0.0, 0.0, DEPTH_MAX_M, FREEZING_POINT_K, 0.0)
    print(f"  deposition=0 -> porosity={p_no_dep:.4f}  deposition=DEPTH_MAX_M -> porosity={p_full_dep:.4f}")
    assert p_full_dep < p_no_dep, "A friss uledek-hozzajarulasnak csokkentenie kell a porozitast (kompakcio)"
    assert abs((p_no_dep - p_full_dep) - POROSITY_COMPACTION_COEFF) < 1e-12, "A teljes kompakcio-levonasnak pontosan a kalibralt egyutthatonak kell lennie deposition=DEPTH_MAX_M-nel"
    print("OK - a kompakcio erdemben csokkenti a porozitast\n")

    print("--- 7. WaterRetention: csapadek (viz forras) erdemi hatasa ---")
    _, _, wr_dry = compute_regolith_profile(False, 0.0, 0.0, 0.0, 288.0, 0.0)
    _, _, wr_wet = compute_regolith_profile(False, 0.0, 0.0, 0.0, 288.0, PRECIP_REFERENCE)
    _, _, wr_extreme = compute_regolith_profile(False, 0.0, 0.0, 0.0, 288.0, 1e18)
    _, _, wr_negative = compute_regolith_profile(False, 0.0, 0.0, 0.0, 288.0, -5.0)
    print(f"  precip=0 -> wr={wr_dry:.4f}  precip=PRECIP_REFERENCE -> wr={wr_wet:.4f}  precip=1e18 -> wr={wr_extreme:.4f}  precip=-5 -> wr={wr_negative:.4f}")
    assert wr_wet > wr_dry, "Tobb csapadeknak novelnie kell a vizmegtarto-kepesseget"
    assert wr_extreme == wr_wet, "A csapadek-hatasnak PRECIP_REFERENCE folott telitodnie kell (nincs tovabbi novekedes)"
    assert wr_negative == wr_dry, "Negativ csapadek (ervenytelen bemenet) ugy kezelendo, mint a nulla (nincs negativ vizmegtartas)"
    assert 0.0 <= wr_dry <= 1.0 and 0.0 <= wr_wet <= 1.0 and 0.0 <= wr_extreme <= 1.0, "A vizmegtartas mindig [0,1]-ben marad"
    print("OK - a csapadek erdemben, telitodo modon hat a vizmegtarto-kepessegre, negativ bemenet nem tor at\n")

    print("--- 8. Porosity/Depth/WaterRetention minden kimenete [0,1]/[0,cap] tartomanyban marad szelsoseges bemenetekkel ---")
    extreme_cases = [
        (False, 0.0, 0.0, 0.0, 0.0, 0.0),
        (False, 1.0, 1e18, 1e18, 1e18, 1e18),
        (False, 0.5, 1e18, 0.0, -1e18, 0.0),
        (False, 0.5, 0.0, 1e18, 1e18, -1e18),
    ]
    for case in extreme_cases:
        d, p_, wr = compute_regolith_profile(*case)
        assert 0.0 <= d <= DEPTH_ABSOLUTE_CAP_M, f"Depth tullepte a korlatot: {case} -> depth={d}"
        assert 0.0 <= p_ <= 1.0, f"Porosity tullepte a korlatot: {case} -> porosity={p_}"
        assert 0.0 <= wr <= 1.0, f"WaterRetention tullepte a korlatot: {case} -> waterRetention={wr}"
    print("OK - minden kimenet a dokumentalt tartomanyban marad szelsoseges (1e18, -1e18) bemenetekkel is\n")

    print("--- 9. Teljes racs egy valodi vilagon (Nereida-7 seed, level=4) ---")
    # level=4 (6*16*16=1536 tile), UGYANAZ a valasztas es indoklas, mint a
    # moisture_transport_ref.py-ban: a csapadek-advekcio (N_ITERATIONS=24,
    # minden lepesben a teljes racs x4 szomszed) tiszta Pythonban level=6-nal
    # (24576 tile) mar tulsagosan lassu egy ismetelt (determinizmus-ellenorzo)
    # futtatashoz - level=4 mar reprezentativ mintat ad a plauzibilitashoz.
    world_seed = 0xA7C944210000
    plate_count = 20
    level = 4
    world = compute_regolith_field(world_seed, plate_count, level)
    land_keys = [k for k in world["field"] if not world["isOcean"][k]]
    land_depth = [world["depth"][k] for k in land_keys]
    land_porosity = [world["porosity"][k] for k in land_keys]
    land_wr = [world["waterRetention"][k] for k in land_keys]
    mean_depth = sum(land_depth) / len(land_depth)
    mean_porosity = sum(land_porosity) / len(land_porosity)
    mean_wr = sum(land_wr) / len(land_wr)
    print(f"  {len(land_keys)} szarazfold-tile: mean_depth={mean_depth:.4f}m  mean_porosity={mean_porosity:.4f}  mean_waterRetention={mean_wr:.4f}")
    assert mean_depth > 0.0, "A szarazfoldi atlag-melysegnek pozitivnak kell lennie"
    assert 0.0 < mean_porosity < 1.0, "A szarazfoldi atlag-porozitasnak (0,1)-ben kell lennie"
    assert 0.0 < mean_wr < 1.0, "A szarazfoldi atlag-vizmegtartasnak (0,1)-ben kell lennie"
    ocean_keys = [k for k in world["field"] if world["isOcean"][k]]
    assert all(world["depth"][k] == 0.0 and world["porosity"][k] == 0.0 and world["waterRetention"][k] == 0.0 for k in ocean_keys), \
        "Minden oceani tile-nak nulla profilt kell adnia a teljes racson is"
    print("OK - a teljes-racs futtatas plauzibilis, oceani tile-ok nullak\n")

    print("--- 10. Determinizmus: teljes racs ketszeri futtatasa bitre azonos ---")
    world2 = compute_regolith_field(world_seed, plate_count, level)
    assert world["depth"] == world2["depth"], "A teljes-racs Depth nem determinisztikus!"
    assert world["porosity"] == world2["porosity"], "A teljes-racs Porosity nem determinisztikus!"
    assert world["waterRetention"] == world2["waterRetention"], "A teljes-racs WaterRetention nem determinisztikus!"
    print("OK - ketszeri futtatas bitre azonos eredmenyt ad\n")

    print("--- Tesztvektorok generalasa a C# porthoz ---")

    # A) "Tiszta fuggveny" vektorok - szeles, veletlenszeruen (threefry-vel,
    # determinisztikusan) mintavett bemenet-tartomany, a teljes racs nelkul
    # kozvetlenul tesztelheto compute_regolith_profile-hoz.
    unit_vectors = []
    gen_seed = 0x50110000000001
    for i in range(500):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 39, 0], 20)
        is_ocean = (p[0] % 10) == 0  # ~10% oceani eset, hogy a nulla-agat is fedje
        slope_norm = (p[1] % 1_000_001) / 1_000_000.0  # [0,1]
        p2 = threefry4x64([i, 1, 0, 0], [gen_seed, 0, 39, 0], 20)
        erosion_depth = (p2[0] % 1_000_001) / 1_000_000.0 * 250.0       # [0,250]
        deposition_gain = (p2[1] % 1_000_001) / 1_000_000.0 * 5.0        # [0,5]
        mean_temp = 200.0 + (p2[2] % 1_000_001) / 1_000_000.0 * 150.0    # [200,350] K
        precipitation = (p2[3] % 1_000_001) / 1_000_000.0 * 10.0         # [0,10]

        d, por, wr = compute_regolith_profile(
            is_ocean, slope_norm, erosion_depth, deposition_gain, mean_temp, precipitation,
        )
        unit_vectors.append({
            "isOcean": bool(is_ocean), "slopeNorm": slope_norm,
            "erosionDepthM": erosion_depth, "depositionGainM": deposition_gain,
            "annualMeanTemperatureK": mean_temp, "precipitation": precipitation,
            "depthM": d, "porosity": por, "waterRetention": wr,
        })

    # B) Teljes-racs, valodi vilagbol threefry-vel mintavett tile-ok (ugyanaz
    # a minta, mint a mar meglevo moisture_transport_ref/lakes_ice_erosion_ref
    # modulokban) - vegponttol-vegpontig integracios ellenorzeshez.
    keys = list(world["field"].keys())
    field_vectors = []
    for i in range(300):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 40, 0], 20)
        key = keys[p[0] % len(keys)]
        face, u, v = key
        field_vectors.append({
            "face": face, "u": u, "v": v,
            "isOcean": bool(world["isOcean"][key]),
            "rawElevation": world["field"][key],
            "erosionDepthM": world["erosion"][key],
            "depositionGainM": world["depositionGain"][key],
            "precipitation": world["precipitation"][key],
            "slopeNorm": world["slopeNorm"][key],
            "annualMeanTemperatureK": world["meanTempK"][key],
            "depthM": world["depth"][key],
            "porosity": world["porosity"][key],
            "waterRetention": world["waterRetention"][key],
        })

    out = {
        "worldSeed": world_seed, "plateCount": plate_count, "level": level,
        "seaLevel": world["seaLevel"],
        "constants": {
            "depthMaxM": DEPTH_MAX_M,
            "depthDepositionGainCoeff": DEPTH_DEPOSITION_GAIN_COEFF,
            "depthErosionStripCoeff": DEPTH_EROSION_STRIP_COEFF,
            "depthAbsoluteCapM": DEPTH_ABSOLUTE_CAP_M,
            "porosityBase": POROSITY_BASE,
            "porosityFreezeThawCoeff": POROSITY_FREEZE_THAW_COEFF,
            "porosityCompactionCoeff": POROSITY_COMPACTION_COEFF,
            "freezeThawHalfRangeK": FREEZE_THAW_HALF_RANGE_K,
            "freezingPointK": FREEZING_POINT_K,
            "retentionPorosityCoeff": RETENTION_POROSITY_COEFF,
            "retentionDepthCoeff": RETENTION_DEPTH_COEFF,
            "retentionPrecipCoeff": RETENTION_PRECIP_COEFF,
            "precipReference": PRECIP_REFERENCE,
        },
        "unitVectors": unit_vectors,
        "fieldVectors": field_vectors,
    }
    with open("regolith_vectors.json", "w", newline="\n") as f:
        json.dump(out, f, indent=1)
    print(f"{len(unit_vectors)} tiszta-fuggveny + {len(field_vectors)} teljes-racs tesztvektor elmentve regolith_vectors.json-ba")
