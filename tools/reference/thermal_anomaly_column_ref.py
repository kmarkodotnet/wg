"""
Pillanatnyi hőmodell (ND-100/ND-101) — egycellás, két állapotú (θs, θa) napi
ciklus mérése. NEM generál tesztvektort; a paraméterek, az integrátor és a
napi besugárzási faktor kezelésének kiválasztását segítő mérőeszköz.

Modell (ND-100, egy cella, szél és advekció nélkül):

    Cs·dθs/dt = ΔQ(t) − λs·θs − ksa·(θs − θa)
    Ca·dθa/dt = ksa·(θs − θa) − λa·θa
    ΔQ(t)     = F·(1 − albedo)·( max(0, n·s(t)) − dailyFactor(t) )
    λs        = 4·ε·σ·Bs³

Változók, amelyeket a mérés összevet:

  napi faktor (a bázisban elszámolt napi átlag):
    fixed_t0   — egyszer, a 0. napra számolva (1. mérőkör viselkedése);
    day_start  — minden nap elején újraszámolva, a napon belül állandó;
    centered   — óránként, az adott órára középre igazított 24 mintás
                 ablakkal számolva, köztük lineáris interpolációval;

  integrátor:
    imex_end   — lokális lineáris tagok implicit (visszafelé Euler), a
                 forcing a lépés végén; elsőrendű;
    imex_cn    — lokális lineáris tagok Crank–Nicolson, a forcing a lépés
                 közepén; másodrendű; mindkettő csak + − × / műveletekkel;

  felszín: Ocean, Land (napi csillapítási mélység), Land_0.5m.

Referencia: RK4, 30 s lépés, ugyanazzal a napifaktor-kezeléssel.

1. mérőkör (2026-09-13, rögzített faktor, 60 nap): a napi átlagos θs 45°-on
+7,6 K (óceán) / +14,6 K (szárazföld) volt — évszakos drift; 3600 s-os ticknél
a mintaátlag pontosan egyezett a 24 mintás faktorral, 1800/900 s-nál
~1,2–1,5·10⁻³ eltérés; az imex_end elsőrendű, szárazföldön 900 s-nál ~1 K hiba.

Paraméterforrások: docs/reviews/thermal-parameters-sources-2026-09-13.md.
A `FORRASRA_VAR` jelölésű értékek még nem forrásoltak vagy nincs
megerősítve; csak érzékenységi kiindulásként szerepelnek.

NEM produkciós kód - orákulum/mérőeszköz a python-reference skill szerint.
"""
import math

from astronomy_ref import sun_direction_body_frame
from temperature_ref import daily_average_insolation_factor

SECONDS_PER_DAY = 86400.0

# --- Forrásolt vagy kódból átvett értékek -------------------------------------
SIGMA = 5.670374419e-8          # Temperature.Sigma (kód)
F_PEAK = 1361.0                 # Temperature.DefaultFPeak (kód)
CP_SEAWATER = 3991.86795711963  # TEOS-10 gsw_cp0 (letöltve)
ALBEDO_OCEAN = 0.06             # NSIDC, Temperature.AlbedoOcean
ALBEDO_LAND = 0.30              # Temperature.AlbedoLand (kód)
SOIL_DIFFUSIVITY = 5.0e-7       # IEC 60853-2 (letöltve)
SOIL_SPECIFIC_HEAT_RATIO = 0.19 # Kersten: ~0,19-0,20 (víz = 1), letöltve
CE10N = 1.15e-3                 # Fairall et al. 2003, 5-20 m/s (letöltve)
HELD_SUAREZ_KS_DAYS = 4.0       # MITgcm Held-Suarez k_s (letöltve)

# --- Modellválasztások (docs M1-M10), megerősítésre várnak ----------------------
OCEAN_EFFECTIVE_DEPTH_M = 10.0  # M3
AIR_COLUMN_DEPTH_M = 1000.0     # M5
WIND_SPEED_MS = 5.0             # M6 kiindulás (szélsnapshot nélkül)
EMISSIVITY_WATER = 0.96         # M8 (Sidran 1981, másodlagos forrás)
EMISSIVITY_LAND = 0.95          # M8 (csak keresési kivonat)

# --- FORRASRA_VAR: még nincs letöltött elsődleges forrás ---------------------
FORRASRA_VAR_AIR_DENSITY = 1.225          # kg/m3, US Std Atm 1976 (kivonat)
FORRASRA_VAR_AIR_CP = 1005.0              # J/(kg K) (kivonat)
FORRASRA_VAR_SEAWATER_DENSITY = 1025.0    # kg/m3, TEOS-10 példatartományon belül
FORRASRA_VAR_SOIL_DRY_DENSITY = 1400.0    # kg/m3, M10
FORRASRA_VAR_CALORIE_J_PER_KG_K = 4186.8  # víz = 1 fajhő-arány átváltása

BASELINE_K = 288.0  # vizsgálati konstans bázis, nem modellérték

ORBITAL_PERIOD_DAYS = 365.25
ROTATION_PERIOD_DAYS = 1.0
AXIAL_TILT_RAD = 23.44 * math.pi / 180.0


def diurnal_damping_depth(diffusivity):
    omega = 2.0 * math.pi / SECONDS_PER_DAY
    return math.sqrt(2.0 * diffusivity / omega)


def make_params(kind):
    air_rho_cp = FORRASRA_VAR_AIR_DENSITY * FORRASRA_VAR_AIR_CP
    ca = air_rho_cp * AIR_COLUMN_DEPTH_M
    if kind == "Ocean":
        cs = FORRASRA_VAR_SEAWATER_DENSITY * CP_SEAWATER * OCEAN_EFFECTIVE_DEPTH_M
        albedo, eps = ALBEDO_OCEAN, EMISSIVITY_WATER
    else:
        depth = diurnal_damping_depth(SOIL_DIFFUSIVITY) if kind == "Land" else 0.5
        c_soil = SOIL_SPECIFIC_HEAT_RATIO * FORRASRA_VAR_CALORIE_J_PER_KG_K
        cs = FORRASRA_VAR_SOIL_DRY_DENSITY * c_soil * depth
        albedo, eps = ALBEDO_LAND, EMISSIVITY_LAND
    return {
        "Cs": cs,
        "Ca": ca,
        "absorb": F_PEAK * (1.0 - albedo),
        "lambda_s": 4.0 * eps * SIGMA * BASELINE_K ** 3,
        "lambda_a": ca / (HELD_SUAREZ_KS_DAYS * SECONDS_PER_DAY),
        "ksa": air_rho_cp * CE10N * WIND_SPEED_MS,
    }


def cos_zenith(position, t_seconds):
    s = sun_direction_body_frame(t_seconds / SECONDS_PER_DAY, ORBITAL_PERIOD_DAYS,
                                 ROTATION_PERIOD_DAYS, AXIAL_TILT_RAD)
    return max(0.0, position[0] * s[0] + position[1] * s[1] + position[2] * s[2])


def daily_factor_at(position, day_t):
    return daily_average_insolation_factor(
        position, day_t, ORBITAL_PERIOD_DAYS, ROTATION_PERIOD_DAYS, AXIAL_TILT_RAD)


class DailyFactor:
    """A három napifaktor-kezelés, gyorsítótárral (a mérés futásidejéért)."""

    def __init__(self, mode, position):
        self.mode = mode
        self.position = position
        self.cache = {}

    def _cached(self, key, day_t):
        if key not in self.cache:
            self.cache[key] = daily_factor_at(self.position, day_t)
        return self.cache[key]

    def __call__(self, t_seconds):
        if self.mode == "fixed_t0":
            return self._cached(0, 0.0)
        if self.mode == "day_start":
            day = int(t_seconds // SECONDS_PER_DAY)
            return self._cached(day, float(day))
        hour = t_seconds / 3600.0
        h0 = math.floor(hour)
        w = hour - h0
        f0 = self._cached(h0, h0 / 24.0 - 0.5)
        f1 = self._cached(h0 + 1, (h0 + 1) / 24.0 - 0.5)
        return f0 + (f1 - f0) * w


def forcing(p, position, factor, t):
    return p["absorb"] * (cos_zenith(position, t) - factor(t))


def tendencies(p, q, ts, ta):
    hsa = p["ksa"] * (ts - ta)
    return (q - p["lambda_s"] * ts - hsa) / p["Cs"], (hsa - p["lambda_a"] * ta) / p["Ca"]


def rk4_step(p, position, factor, t, dt, ts, ta):
    q0 = forcing(p, position, factor, t)
    qh = forcing(p, position, factor, t + 0.5 * dt)
    q1 = forcing(p, position, factor, t + dt)
    k1 = tendencies(p, q0, ts, ta)
    k2 = tendencies(p, qh, ts + 0.5 * dt * k1[0], ta + 0.5 * dt * k1[1])
    k3 = tendencies(p, qh, ts + 0.5 * dt * k2[0], ta + 0.5 * dt * k2[1])
    k4 = tendencies(p, q1, ts + dt * k3[0], ta + dt * k3[1])
    return (ts + dt / 6.0 * (k1[0] + 2.0 * k2[0] + 2.0 * k3[0] + k4[0]),
            ta + dt / 6.0 * (k1[1] + 2.0 * k2[1] + 2.0 * k3[1] + k4[1]))


def solve2(a11, a12, a21, a22, b1, b2):
    det = a11 * a22 - a12 * a21
    return (b1 * a22 - a12 * b2) / det, (a11 * b2 - a21 * b1) / det


def imex_end_step(p, position, factor, t, dt, ts, ta):
    q = forcing(p, position, factor, t + dt)
    return solve2(p["Cs"] / dt + p["lambda_s"] + p["ksa"], -p["ksa"],
                  -p["ksa"], p["Ca"] / dt + p["ksa"] + p["lambda_a"],
                  p["Cs"] / dt * ts + q, p["Ca"] / dt * ta)


def imex_cn_step(p, position, factor, t, dt, ts, ta):
    q = forcing(p, position, factor, t + 0.5 * dt)
    ls, la, k = p["lambda_s"], p["lambda_a"], p["ksa"]
    rs = p["Cs"] / dt * ts + q - 0.5 * (ls * ts + k * (ts - ta))
    ra = p["Ca"] / dt * ta + 0.5 * (k * (ts - ta) - la * ta)
    return solve2(p["Cs"] / dt + 0.5 * (ls + k), -0.5 * k,
                  -0.5 * k, p["Ca"] / dt + 0.5 * (k + la), rs, ra)


def run(stepper, p, position, factor, dt, days):
    steps_per_day = int(round(SECONDS_PER_DAY / dt))
    ts = ta = 0.0
    last_day = []
    for n in range(days * steps_per_day):
        t = n * dt
        ts, ta = stepper(p, position, factor, t, dt, ts, ta)
        if n >= (days - 1) * steps_per_day:
            last_day.append((t + dt, ts, ta))
    return last_day


def summarize(samples, position):
    t_max, ts_max, _ = max(samples, key=lambda s: s[1])
    ts_min = min(s[1] for s in samples)
    mean_ts = sum(s[1] for s in samples) / len(samples)
    day_start = samples[0][0] - (samples[1][0] - samples[0][0])
    noon_t = max((day_start + i * 60.0 for i in range(1440)),
                 key=lambda tt: cos_zenith(position, tt))
    lag_h = ((t_max - noon_t) % SECONDS_PER_DAY) / 3600.0
    return 0.5 * (ts_max - ts_min), lag_h, mean_ts


def max_error(got, ref):
    lookup = {round(s[0]): s for s in ref}
    worst = 0.0
    for t, ts, ta in got:
        r = lookup[round(t)]
        worst = max(worst, abs(ts - r[1]), abs(ta - r[2]))
    return worst


def main():
    days = 30
    ref_dt = 30.0
    positions = {"lat 0": (1.0, 0.0, 0.0),
                 "lat 45": (math.sqrt(0.5), 0.0, math.sqrt(0.5))}
    kinds = ("Ocean", "Land", "Land_0.5m")
    modes = ("fixed_t0", "day_start", "centered")
    ticks = (1800.0, 900.0)

    for kind in kinds:
        p = make_params(kind)
        print(f"{kind}: " + ", ".join(f"{k}={v:.4g}" for k, v in p.items()))
    print()
    print("hely    felszín     napifaktor  integrátor  dt[s]  amp[K]  késés[h]  átlag θs[K]  max|hiba|[K]")
    for name, pos in positions.items():
        for kind in kinds:
            p = make_params(kind)
            for mode in modes:
                ref = run(rk4_step, p, pos, DailyFactor(mode, pos), ref_dt, days)
                amp, lag, mts = summarize(ref, pos)
                print(f"{name:7s} {kind:11s} {mode:11s} rk4-ref     {ref_dt:5.0f}  {amp:6.3f}  {lag:8.2f}  {mts:+11.4f}  —")
                for integ, stepper in (("imex_end", imex_end_step), ("imex_cn", imex_cn_step)):
                    for dt in ticks:
                        got = run(stepper, p, pos, DailyFactor(mode, pos), dt, days)
                        gamp, glag, gmts = summarize(got, pos)
                        print(f"{name:7s} {kind:11s} {mode:11s} {integ:10s}  {dt:5.0f}  {gamp:6.3f}  "
                              f"{glag:8.2f}  {gmts:+11.4f}  {max_error(got, ref):.4f}")
            print()


if __name__ == "__main__":
    main()
