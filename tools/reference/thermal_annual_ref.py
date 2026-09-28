"""
Eves hostatisztika es ketmenetes jegmaszk referencia-implementacioja (ND-158).

Ez az orakulum a C# `ThermalAnnualStatisticsCalculator` es
`ThermalClimateCalculator` numerikus szerzodese. Nem uj fizikai kernel: a
mar verifikalt `thermal_field_ref.py` solverere epul (ND-100-103, ND-142),
es KIZAROLAG az aggregaciot es a ketmenetes jeg-feloldast rogziti.

MIT ROGZIT

1. Napi statisztika (ND-144 ismetlese): egy modellnap 96 tickjenek ELEJI
   allapota; cellankent Ts/Ta atlag, min, max. Az atlag a 96 minta osszege
   osztva 96-tal, novekvo ticksorrendben osszegezve.

2. Eves statisztika (ND-158): `sampleDays` darab mintanap az even belul,
       nap_j = firstDay + floor(j * orbitalPeriodDays / sampleDays),  j = 0..n-1
   A napokat NOVEKVO sorrendben dolgozzuk fel (igy a solver a bucketen
   belul folytathato, a kanonikus ut pedig ettol fuggetlenul azonos).
       mean[c] = (sum_j napi_mean_j[c]) / sampleDays
       min[c]  = min_j napi_min_j[c]
       max[c]  = max_j napi_max_j[c]

3. Ketmenetes jegmaszk (ND-158) - a korkoros fugges feloldasa. A jeg eddig
   BEMENETE volt a homodellnek (albedo/hokapacitas), miuttan a jeg maga a
   homerseklet KIMENETE. Iterativ fixpont helyett ROGZITETT ket menet:
       A menet: a hivo altal adott JEGMENTES felszintipus-terkep
                (Land/Ocean/Freshwater) -> eves statisztika -> jegosztaly
       B menet: ahol az A menet PermanentIce-t adott, a tipus Ice lesz
                -> uj mezo -> eves statisztika -> VEGLEGES jegosztaly
   A ket menet kozotti atsorolt cellak szama MERT diagnosztika, nem kapu.
   Nincs tolerancia-fuggo megallas: a menetszam konstans, tehat az
   eredmeny bitre reprodukalhato (I1).

   A jegosztalyozas a FELSZINI (Ts) eves atlagra es minimumra megy, a
   LakesIceErosion.ClassifyIce valtozatlan kuszobeivel:
       mean < 258,15 K -> PermanentIce
       min  < 273,15 K -> SeasonalSnow
       kulonben None

A kimenet kis racson (level 1) es rovid even keszul, hogy pure Pythonban
percek helyett masodpercek alatt lefusson; az aggregacios szabaly
racsfuggetlen.

Kimenet: thermal_annual_vectors.json

NEM produkcios kod - orakulum a python-reference skill szerint.
"""
import json
import math

import thermal_field_ref as tf

# --- kis, gyorsan futo orakulum-vilag -------------------------------------
LEVEL = 2
ORBITAL_PERIOD_DAYS = 8.0
SAMPLE_DAYS = 4
FIRST_DAY = 0
WORLD_SEED = 184482873278464
T_YEARS = 0.0
SEA_LEVEL_M = 0.0

PERMANENT_ICE_MEAN_K = 258.15
SEASONAL_SNOW_MIN_K = 273.15
ICE_NONE, ICE_SEASONAL, ICE_PERMANENT = 0, 1, 2
ICE_NAMES = ["None", "SeasonalSnow", "PermanentIce"]


def _patch_globals():
    """A thermal_field_ref modulszintu vilagat a kis orakulum-vilagra allitja."""
    tf.LEVEL = LEVEL
    tf.N = 1 << LEVEL
    tf.CELL_COUNT = 6 * tf.N * tf.N
    tf.ORBITAL_PERIOD_DAYS = ORBITAL_PERIOD_DAYS


def oracle_world(grid):
    """
    JEGMENTES (Land/Ocean/Freshwater) orakulum-vilag. A tf.synthetic_world
    helyett sajat: abban a sarki cellak eleve Ice tipusuak, itt viszont a
    ketmenetes utnak epp az a lenyege, hogy a jeg a KIMENETBOL szulessen.
    A sarki fennsik (|z| > 0,6) 7000 m magas, igy a 0,0065 K/m lapse rate
    ~45 K-nel hidegebbre viszi - ettol lesz PermanentIce a B menet bemenete.
    """
    count = tf.CELL_COUNT
    kinds, elevation = [tf.LAND] * count, [0.0] * count
    for k, p in enumerate(grid.center):
        s = 0.8 * p[0] + 0.3 * p[1] + 0.2 * p[2]
        if abs(p[2]) > 0.6:
            kinds[k] = tf.LAND
            elevation[k] = 7000.0
        elif s < 0.05:
            kinds[k] = tf.OCEAN
            elevation[k] = -3000.0
        elif p[0] > 0.5 and p[1] > 0.3 and s < 0.6:
            kinds[k] = tf.FRESHWATER
            elevation[k] = 100.0
        else:
            kinds[k] = tf.LAND
            elevation[k] = 150.0 + 2500.0 * (s - 0.05)
    return kinds, elevation


def make_field(grid, kinds, elevation):
    """Field a MEGADOTT felszintipus-terkeppel (a tf.Field sajat vilagot epitene)."""
    f = tf.Field.__new__(tf.Field)
    f.grid = grid
    f.kinds = list(kinds)
    f.elevation = list(elevation)
    f.baseline = tf.Baseline(grid, f.kinds, f.elevation, SEA_LEVEL_M, WORLD_SEED, T_YEARS)
    f.wind = tf.WindField(grid, f.kinds, f.elevation, SEA_LEVEL_M)
    f.feedback = tf.AirWindFeedback(f.wind)
    return f


class State:
    """A C# ThermalSnapshot megfeleloje: tick + kanonikus kezdotick + anomaliak."""

    def __init__(self, count):
        self.tick = 0
        self.canonical_start = None
        self.theta_s = [0.0] * count
        self.theta_a = [0.0] * count


def state_at(field, state, target_tick):
    """A C# SurfaceTemperatureField.StateAt: folytatas, ha lehet; kulonben kanonikus ujraszamolas."""
    start = tf.Field.canonical_start(target_tick)
    if state.canonical_start != start or state.tick > target_tick:
        state.tick = start
        state.canonical_start = start
        state.theta_s = [0.0] * len(state.theta_s)
        state.theta_a = [0.0] * len(state.theta_a)
    while state.tick < target_tick:
        state.theta_s, state.theta_a, _ = field.step(state.tick, state.theta_s, state.theta_a)
        state.tick += 1


def daily_statistics(field, state, day_index):
    """ND-144: egy modellnap 96 tick-eleji mintajanak atlaga/min/max, cellankent."""
    count = len(state.theta_s)
    ticks_per_day = tf.SECONDS_PER_DAY // tf.TICK_SECONDS
    state_at(field, state, day_index * ticks_per_day)

    mean_s = [0.0] * count
    mean_a = [0.0] * count
    min_s = [math.inf] * count
    max_s = [-math.inf] * count
    min_a = [math.inf] * count
    max_a = [-math.inf] * count
    for _ in range(ticks_per_day):
        _, base = field.baseline.at(state.tick * tf.TICK_SECONDS)
        for c in range(count):
            surface = base[c] + state.theta_s[c]
            air = base[c] + state.theta_a[c]
            mean_s[c] += surface
            mean_a[c] += air
            if surface < min_s[c]:
                min_s[c] = surface
            if surface > max_s[c]:
                max_s[c] = surface
            if air < min_a[c]:
                min_a[c] = air
            if air > max_a[c]:
                max_a[c] = air
        state.theta_s, state.theta_a, _ = field.step(state.tick, state.theta_s, state.theta_a)
        state.tick += 1
    for c in range(count):
        mean_s[c] /= ticks_per_day
        mean_a[c] /= ticks_per_day
    return mean_s, mean_a, min_s, max_s, min_a, max_a


def sample_day_indices(orbital_period_days=ORBITAL_PERIOD_DAYS, sample_days=SAMPLE_DAYS, first_day=FIRST_DAY):
    """nap_j = firstDay + floor(j * orbital / n). Csak szorzas/osztas/floor - bitpontos."""
    return [first_day + int(math.floor(j * orbital_period_days / sample_days)) for j in range(sample_days)]


def annual_statistics(field, state, sample_days=SAMPLE_DAYS, first_day=FIRST_DAY):
    count = len(state.theta_s)
    days = sample_day_indices(tf.ORBITAL_PERIOD_DAYS, sample_days, first_day)
    mean_s = [0.0] * count
    mean_a = [0.0] * count
    min_s = [math.inf] * count
    max_s = [-math.inf] * count
    min_a = [math.inf] * count
    max_a = [-math.inf] * count
    for day in days:
        d_ms, d_ma, d_ns, d_xs, d_na, d_xa = daily_statistics(field, state, day)
        for c in range(count):
            mean_s[c] += d_ms[c]
            mean_a[c] += d_ma[c]
            if d_ns[c] < min_s[c]:
                min_s[c] = d_ns[c]
            if d_xs[c] > max_s[c]:
                max_s[c] = d_xs[c]
            if d_na[c] < min_a[c]:
                min_a[c] = d_na[c]
            if d_xa[c] > max_a[c]:
                max_a[c] = d_xa[c]
    for c in range(count):
        mean_s[c] /= sample_days
        mean_a[c] /= sample_days
    return {"days": days, "meanSurfaceK": mean_s, "meanAirK": mean_a,
            "minSurfaceK": min_s, "maxSurfaceK": max_s, "minAirK": min_a, "maxAirK": max_a}


def classify_ice(mean_annual_k, min_annual_k):
    """LakesIceErosion.ClassifyIce, valtozatlan kuszobokkel."""
    if mean_annual_k < PERMANENT_ICE_MEAN_K:
        return ICE_PERMANENT
    if min_annual_k < SEASONAL_SNOW_MIN_K:
        return ICE_SEASONAL
    return ICE_NONE


def ice_free_kinds(kinds):
    """A ketmenetes ut bemenete: a hivo felszintipus-terkepe Ice NELKUL."""
    return [tf.LAND if k == tf.ICE else k for k in kinds]


def two_pass_climate(grid, base_kinds, elevation):
    assert tf.ICE not in base_kinds, "Az A menet bemenete nem tartalmazhat Ice tipust."
    count = len(base_kinds)

    field_a = make_field(grid, base_kinds, elevation)
    stats_a = annual_statistics(field_a, State(count))
    ice_a = [classify_ice(stats_a["meanSurfaceK"][c], stats_a["minSurfaceK"][c]) for c in range(count)]

    refined = [tf.ICE if ice_a[c] == ICE_PERMANENT else base_kinds[c] for c in range(count)]
    field_b = make_field(grid, refined, elevation)
    stats_b = annual_statistics(field_b, State(count))
    ice_b = [classify_ice(stats_b["meanSurfaceK"][c], stats_b["minSurfaceK"][c]) for c in range(count)]

    reclassified = sum(1 for c in range(count) if ice_a[c] != ice_b[c])
    return {"iceFree": stats_a, "refined": stats_b, "iceFreeClass": ice_a, "refinedClass": ice_b,
            "refinedKinds": refined, "reclassifiedCells": reclassified}


def main():
    _patch_globals()
    grid = tf.Grid()
    base_kinds, elevation = oracle_world(grid)
    count = tf.CELL_COUNT
    print(f"Orakulum-vilag: level {LEVEL}, {count} cella, ev {ORBITAL_PERIOD_DAYS} nap, "
          f"{SAMPLE_DAYS} mintanap", flush=True)
    print("Jegmentes bazis-tipusok: " + ", ".join(
        f"{tf.KIND_NAMES[k]}={base_kinds.count(k)}" for k in range(4)), flush=True)

    climate = two_pass_climate(grid, base_kinds, elevation)

    for name in ("iceFree", "refined"):
        s = climate[name]
        print(f"{name}: napok {s['days']}, Ts atlag [{min(s['meanSurfaceK']):.3f}, "
              f"{max(s['meanSurfaceK']):.3f}] K, Ta atlag [{min(s['meanAirK']):.3f}, "
              f"{max(s['meanAirK']):.3f}] K", flush=True)
    for name, key in (("A menet", "iceFreeClass"), ("B menet", "refinedClass")):
        counts = [climate[key].count(i) for i in range(3)]
        print(f"{name} jeg: " + ", ".join(f"{ICE_NAMES[i]}={counts[i]}" for i in range(3)), flush=True)
    print(f"Atsorolt cellak a ket menet kozott: {climate['reclassifiedCells']}", flush=True)

    vectors = {
        "modelVersion": tf.MODEL_VERSION,
        "level": LEVEL,
        "cellCount": count,
        "orbitalPeriodDays": ORBITAL_PERIOD_DAYS,
        "rotationPeriodDays": tf.ROTATION_PERIOD_DAYS,
        "axialTiltRad": tf.AXIAL_TILT_RAD,
        "sampleDays": SAMPLE_DAYS,
        "firstDay": FIRST_DAY,
        "worldSeed": WORLD_SEED,
        "tYears": T_YEARS,
        "seaLevelM": SEA_LEVEL_M,
        "sampleDayIndices": climate["refined"]["days"],
        "baseKinds": base_kinds,
        "refinedKinds": climate["refinedKinds"],
        "elevationM": elevation,
        "iceFree": climate["iceFree"],
        "refined": climate["refined"],
        "iceFreeClass": climate["iceFreeClass"],
        "refinedClass": climate["refinedClass"],
        "reclassifiedCells": climate["reclassifiedCells"],
    }
    with open("thermal_annual_vectors.json", "w", newline="\n") as f:
        json.dump(vectors, f, indent=1)
    print("thermal_annual_vectors.json kiirva")


if __name__ == "__main__":
    main()
