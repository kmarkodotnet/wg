"""
Pillanatnyi hőmérsékletmező referencia-implementációja (ND-100–103), level 6.

Ez az orákulum a C# `WorldGen.Core.Climate` hőmodelljének (DenseGridMetrics,
ThermalBaseline, ThermalWind, SurfaceTemperatureField) numerikus szerződése.
A mérőeszközök (`thermal_grid_metrics_ref.py`, `thermal_anomaly_column_ref.py`,
`thermal_advection_ref.py`, `thermal_seasonal_column_ref.py`) eredményei
alapján rögzített séma:

RÁCS (ND-101): level 6, kanonikus index face·64² + u·64 + v; cellaközép a
  TileGeometry.ToPosition szerint; húrsokszög-terület a teljes gömbre
  normalizálva (R = PlanetConstants.RadiusMeters); élek cellánként right, left,
  up, down sorrendben, csak i < j esetén, az i cella oldalának sarkaival; húr-
  élhossz; élközéppont; élnormál az i→j irányba.

IDŐ (ND-101): egész tick, 900 s; másodperc = tick·900; nap = s / 86400.
  Óra-index h = floor(s / 3600), nap-index k = floor(s / 86400).

BÁZIS (ND-100, 2026-09-13-i módosítás): az első futás szerint a napi faktoros
  TemperatureKelvinFull-szerkezet sarki éjszakán ~29 K-t adott. Ezért a
  radiatív tag simított faktort kap (szállítás/tehetetlenség proxy, M13):
      f_napi(h) = 24 mintás napi átlag a dayT = h/24 − 0,5 ablakban
      f_éves    = 12 éves ablak (dayT = j·orbital/12) napi faktorainak átlaga
      f_eff     = (1 − β)·f_napi + β·f_éves,   β = 0,5
      Bs = Ba = T_rad(f_eff) + T_greenhouse + T_ocean − T_alt + T_cycle
      T_ocean   = 0,3·(mean_j T_rad(f_eff_j) − T_rad(f_eff))   (csak óceán)
  Órán belül f_napi és Bs lineárisan interpolálva: X(t) = X_h + (X_{h+1} − X_h)·w.

SZÉL (ND-102): determinisztikus átírás — zonális sáv −sin(6|lat|) z és
  sqrt(1−z²) polinomjaként; kelet = normalize(−y, x, 0), észak = p × kelet;
  termikus komponens a T_rad(f_eff) + 33 − T_alt pont-hőmérséklet véges
  differenciájából (a simított faktor miatt a sarki éjszaka határán is sima),
  30°-os Coriolis-forgatással; orográfia nélkül. Napi snapshot a k − 0,5
  ablakkal, két snapshot között lineáris interpoláció. Élsebesség az
  élközéppontban, a cella szélsebessége a cellaközépben.

LÉPÉS (ND-100–102), tickenként t → t + dt, tm = t + dt/2:
  1. θa advekciója: kompenzált upwind fluxusforma, u(tm), determinisztikus
     részlépések a beáramlási Courant-szám alapján (≤ 0,5);
  2. lokális tagok Crank–Nicolson-IMEX-szel, tm-ben:
     ΔQ = F·(1 − albedo)·( max(0, n·s(tm)) − f_napi(tm) ).

KANONIKUS ÁLLAPOT (ND-101): StateAt(T): S = floor(T/2880)·2880 − 960 tick
  (30 napos bucket, 10 napos spin-up), θ = 0 S-ben, léptetés T-ig. Mérve: a
  10 napos spin-up hibája ≤ 0,18 K.

Paraméterek: docs/reviews/thermal-parameters-sources-2026-09-13.md (M1–M10
jóváhagyva). Ideiglenes, még megerősítendő modellválasztások:
  M11 — a hőcserében U_eff = max(U, 1 m/s) (szélcsend-alsóhatár, a COARE
        gustiness-érvelése alapján: Fairall et al. 2003, 573. o.);
  M12 — édesvíz hőkapacitása = óceáni képlet; jég hőkapacitása = szárazföldi;
  M13 — β = 0,5 radiatív simítás a bázisban és a szél hőmérsékletében.

Kimenet: thermal_field_vectors.json (tesztvektorok a C# porthoz).

NEM produkciós kód - orákulum a python-reference skill szerint.
"""
import json
import math
import sys

import deterministic_math_ref as dm
from neighbor_ref import DIRECTIONS, neighbor
from sphere_position_ref import position_from_face_uv, position_from_tile
from temperature_ref import climate_cycle_temperature_k, greenhouse_temperature

MODEL_VERSION = 1
LEVEL = 6
N = 1 << LEVEL
CELL_COUNT = 6 * N * N
RADIUS_M = 7420000.0

TICK_SECONDS = 900
SECONDS_PER_HOUR = 3600
SECONDS_PER_DAY = 86400
BUCKET_TICKS = 2880
SPIN_UP_TICKS = 960
MAX_INFLOW_COURANT = 0.5

ORBITAL_PERIOD_DAYS = 365.25
ROTATION_PERIOD_DAYS = 1.0
AXIAL_TILT_RAD = 23.44 * math.pi / 180.0
DAY_SAMPLES = 24
ANNUAL_SAMPLES = 12

# Temperature.cs konstansok (kód)
SIGMA = 5.670374419e-8
F_PEAK = 1361.0
ALBEDO_OCEAN_FULL = 0.06
ALBEDO_LAND_FULL = 0.30
LAPSE_RATE_K_PER_M = 0.0065
OCEAN_BUFFERING_STRENGTH = 0.3
SIMPLE_GREENHOUSE_K = 33.0

# WindPrecipitation.cs konstansok (kód)
BASE_WIND_SPEED = 10.0
CORIOLIS_DEFLECTION_DEG = 30.0
GRADIENT_EPS = 1.0e-3
THERMAL_WIND_COEFF = 0.5

# Felszíntípusok (ND-103)
LAND, OCEAN, FRESHWATER, ICE = 0, 1, 2, 3
KIND_NAMES = ["Land", "Ocean", "Freshwater", "Ice"]

# Paraméterek (M1-M10 jóváhagyva, M11-M13 ideiglenes)
ALBEDO = [0.30, 0.06, 0.06, 0.6]                  # Land (kód), Ocean (NSIDC), M2, M1
EMISSIVITY = [0.95, 0.96, 0.96, 0.95]             # M8
SEAWATER_DENSITY = 1025.0                         # M9
SEAWATER_CP = 3991.86795711963                    # TEOS-10 cp0
OCEAN_DEPTH_M = 10.0                              # M3
SOIL_DRY_DENSITY = 1400.0                         # M10
SOIL_SPECIFIC_HEAT = 0.19 * 4186.8                # Kersten 0,19 (víz = 1) J/(kg K)
LAND_DEPTH_M = 0.5                                # M4
AIR_DENSITY = 1.225                               # M9
AIR_CP = 1005.0
AIR_COLUMN_M = 1000.0                             # M5
TRANSFER_COEFF = 1.15e-3                          # M6
AIR_RELAXATION_SECONDS = 4.0 * 86400.0            # M7
MIN_EXCHANGE_WIND_MS = 1.0                        # M11
RADIATIVE_SMOOTHING = 0.5                         # M13

_OCEAN_CS = SEAWATER_DENSITY * SEAWATER_CP * OCEAN_DEPTH_M
_LAND_CS = SOIL_DRY_DENSITY * SOIL_SPECIFIC_HEAT * LAND_DEPTH_M
SURFACE_HEAT_CAPACITY = [_LAND_CS, _OCEAN_CS, _OCEAN_CS, _LAND_CS]  # M12
AIR_HEAT_CAPACITY = AIR_DENSITY * AIR_CP * AIR_COLUMN_M
AIR_RELAXATION = AIR_HEAT_CAPACITY / AIR_RELAXATION_SECONDS
EXCHANGE_PER_MS = AIR_DENSITY * AIR_CP * TRANSFER_COEFF


# ---------------------------------------------------------------------------
# Vektor-segédfüggvények (a C# kóddal azonos műveleti sorrend)
# ---------------------------------------------------------------------------

def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def length(a):
    return math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])


def normalized(a):
    inv = 1.0 / length(a)
    return (a[0] * inv, a[1] * inv, a[2] * inv)


def floor_div(a, b):
    return a // b  # Python egész osztás lefelé kerekít, negatívra is


# ---------------------------------------------------------------------------
# Csillagászat DeterministicMath-tal (OrbitalMechanics.cs szerkezete)
# ---------------------------------------------------------------------------

def _rot_x(angle):
    s, c = dm.sin_cos(angle)
    return (1.0, 0.0, 0.0, 0.0, c, -s, 0.0, s, c)


def _rot_z(angle):
    s, c = dm.sin_cos(angle)
    return (c, -s, 0.0, s, c, 0.0, 0.0, 0.0, 1.0)


def _mul(a, b):
    return (
        a[0] * b[0] + a[1] * b[3] + a[2] * b[6],
        a[0] * b[1] + a[1] * b[4] + a[2] * b[7],
        a[0] * b[2] + a[1] * b[5] + a[2] * b[8],
        a[3] * b[0] + a[4] * b[3] + a[5] * b[6],
        a[3] * b[1] + a[4] * b[4] + a[5] * b[7],
        a[3] * b[2] + a[4] * b[5] + a[5] * b[8],
        a[6] * b[0] + a[7] * b[3] + a[8] * b[6],
        a[6] * b[1] + a[7] * b[4] + a[8] * b[7],
        a[6] * b[2] + a[7] * b[5] + a[8] * b[8],
    )


def sun_direction_body_frame(t_days):
    theta = 0.0 + 2.0 * math.pi * (t_days / ORBITAL_PERIOD_DAYS)
    sin_t, cos_t = dm.sin_cos(theta)
    sx, sy, sz = -cos_t, -sin_t, 0.0
    rotation_angle = 0.0 + 2.0 * math.pi * (t_days / ROTATION_PERIOD_DAYS)
    b = _mul(_rot_x(AXIAL_TILT_RAD), _rot_z(rotation_angle))
    m = (b[0], b[3], b[6], b[1], b[4], b[7], b[2], b[5], b[8])  # transzponált
    return (m[0] * sx + m[1] * sy + m[2] * sz,
            m[3] * sx + m[4] * sy + m[5] * sz,
            m[6] * sx + m[7] * sy + m[8] * sz)


def daily_sample_directions(day_t):
    """DailyInsolationSampleDirections.Create szerkezete."""
    return [sun_direction_body_frame(day_t + i * (ROTATION_PERIOD_DAYS / DAY_SAMPLES))
            for i in range(DAY_SAMPLES)]


def annual_sample_windows():
    return [daily_sample_directions(j * (ORBITAL_PERIOD_DAYS / ANNUAL_SAMPLES)) for j in range(ANNUAL_SAMPLES)]


def average_factor(p, samples):
    total = 0.0
    for s in samples:
        c = p[0] * s[0] + p[1] * s[1] + p[2] * s[2]
        total += max(0.0, c)
    return total / len(samples)


def annual_factor(p, windows):
    total = 0.0
    for w in windows:
        total += average_factor(p, w)
    return total / ANNUAL_SAMPLES


def effective_factor(daily, annual):
    return (1.0 - RADIATIVE_SMOOTHING) * daily + RADIATIVE_SMOOTHING * annual


def radiative_temperature(factor, albedo):
    absorbed = F_PEAK * factor * (1.0 - albedo)
    raw = absorbed / SIGMA if absorbed > 0.0 else 0.0
    return math.sqrt(math.sqrt(raw)) if raw > 0.0 else 0.0


# ---------------------------------------------------------------------------
# Rács (DenseGridMetrics)
# ---------------------------------------------------------------------------

class Grid:
    def __init__(self):
        n = N
        self.center = [None] * CELL_COUNT
        self.area = [0.0] * CELL_COUNT
        self.edge_i, self.edge_j, self.edge_len = [], [], []
        self.edge_mid, self.edge_normal = [], []

        def corner(f, i, j):
            return position_from_face_uv(f, i / n * 2.0 - 1.0, j / n * 2.0 - 1.0)

        pending = []
        raw_total = 0.0
        for f in range(6):
            for u in range(n):
                for v in range(n):
                    idx = f * n * n + u * n + v
                    p00, p10 = corner(f, u, v), corner(f, u + 1, v)
                    p11, p01 = corner(f, u + 1, v + 1), corner(f, u, v + 1)
                    self.center[idx] = position_from_tile(f, LEVEL, u, v)
                    a = 0.5 * (length(cross(sub(p10, p00), sub(p11, p00)))
                               + length(cross(sub(p11, p00), sub(p01, p00))))
                    self.area[idx] = a
                    raw_total += a
                    sides = ((p10, p11), (p00, p01), (p01, p11), (p00, p10))
                    for d_index, d in enumerate(DIRECTIONS):
                        nf, nu, nv = neighbor(f, LEVEL, u, v, d)
                        j = nf * n * n + nu * n + nv
                        if idx < j:
                            pending.append((idx, j, sides[d_index]))

        area_scale = 4.0 * math.pi / raw_total * RADIUS_M * RADIUS_M
        self.area = [a * area_scale for a in self.area]
        for i, j, (a, b) in pending:
            mid = normalized(((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, (a[2] + b[2]) * 0.5))
            nrm = normalized(cross(sub(b, a), mid))
            if dot(nrm, sub(self.center[j], self.center[i])) < 0.0:
                nrm = (-nrm[0], -nrm[1], -nrm[2])
            self.edge_i.append(i)
            self.edge_j.append(j)
            self.edge_len.append(length(sub(b, a)) * RADIUS_M)
            self.edge_mid.append(mid)
            self.edge_normal.append(nrm)


# ---------------------------------------------------------------------------
# Szintetikus teszt-világ (a C# teszt ugyanezt a szabályt valósítja meg)
# ---------------------------------------------------------------------------

def synthetic_world(grid):
    kinds, elevation = [0] * CELL_COUNT, [0.0] * CELL_COUNT
    for k, p in enumerate(grid.center):
        s = 0.8 * p[0] + 0.3 * p[1] + 0.2 * p[2]
        if p[2] > 0.92 or p[2] < -0.92:
            kinds[k] = ICE
            elevation[k] = 300.0
        elif s < 0.05:
            kinds[k] = OCEAN
            elevation[k] = -3000.0
        elif p[0] > 0.5 and p[1] > 0.3 and s < 0.6:
            kinds[k] = FRESHWATER
            elevation[k] = 100.0
        else:
            kinds[k] = LAND
            elevation[k] = 150.0 + 2500.0 * (s - 0.05)
    return kinds, elevation


# ---------------------------------------------------------------------------
# Bázis (ThermalBaseline)
# ---------------------------------------------------------------------------

class Baseline:
    def __init__(self, grid, kinds, elevation, sea_level_m, world_seed, t_years):
        self.grid, self.kinds, self.elevation = grid, kinds, elevation
        self.sea_level_m = sea_level_m
        self.constant = greenhouse_temperature()
        self.cycle = climate_cycle_temperature_k(world_seed, t_years)
        windows = annual_sample_windows()
        self.annual_factor = [0.0] * CELL_COUNT
        self.annual_mean = [0.0] * CELL_COUNT
        for k in range(CELL_COUNT):
            p = grid.center[k]
            window_factors = [average_factor(p, w) for w in windows]
            total = 0.0
            for wf in window_factors:
                total += wf
            annual = total / ANNUAL_SAMPLES
            self.annual_factor[k] = annual
            if kinds[k] == OCEAN:
                total = 0.0
                for wf in window_factors:
                    total += radiative_temperature(effective_factor(wf, annual), ALBEDO_OCEAN_FULL)
                self.annual_mean[k] = total / ANNUAL_SAMPLES
        self.hour_cache = {}

    def hour(self, h):
        """(f_napi[], Bs[]) a h óra-indexre."""
        if h not in self.hour_cache:
            samples = daily_sample_directions(h / 24.0 - 0.5)
            factor, base = [0.0] * CELL_COUNT, [0.0] * CELL_COUNT
            for k in range(CELL_COUNT):
                f = average_factor(self.grid.center[k], samples)
                oceanic = self.kinds[k] == OCEAN
                t_rad = radiative_temperature(effective_factor(f, self.annual_factor[k]),
                                              ALBEDO_OCEAN_FULL if oceanic else ALBEDO_LAND_FULL)
                t_ocean = OCEAN_BUFFERING_STRENGTH * (self.annual_mean[k] - t_rad) if oceanic else 0.0
                t_alt = LAPSE_RATE_K_PER_M * max(0.0, self.elevation[k] - self.sea_level_m)
                factor[k] = f
                base[k] = t_rad + self.constant + t_ocean - t_alt + self.cycle
            if len(self.hour_cache) > 4:
                self.hour_cache.pop(next(iter(self.hour_cache)))
            self.hour_cache[h] = (factor, base)
        return self.hour_cache[h]

    def at(self, t_seconds):
        h = floor_div(t_seconds, SECONDS_PER_HOUR)
        w = (t_seconds - h * SECONDS_PER_HOUR) / float(SECONDS_PER_HOUR)
        f0, b0 = self.hour(h)
        f1, b1 = self.hour(h + 1)
        return ([f0[k] + (f1[k] - f0[k]) * w for k in range(CELL_COUNT)],
                [b0[k] + (b1[k] - b0[k]) * w for k in range(CELL_COUNT)])


# ---------------------------------------------------------------------------
# Determinisztikus szél (ThermalWind)
# ---------------------------------------------------------------------------

_CORIOLIS_SIN, _CORIOLIS_COS = dm.sin_cos(CORIOLIS_DEFLECTION_DEG * math.pi / 180.0)


def east_north(p):
    rho2 = p[0] * p[0] + p[1] * p[1]
    if rho2 < 1.0e-24:
        east = (0.0, 1.0, 0.0)
    else:
        inv = 1.0 / math.sqrt(rho2)
        east = (-p[1] * inv, p[0] * inv, 0.0)
    return east, cross(p, east)


def zonal_band_index(z):
    az = z if z >= 0.0 else -z
    c2 = 1.0 - z * z
    c = math.sqrt(c2) if c2 > 0.0 else 0.0
    sin3 = 3.0 * az - 4.0 * az * az * az
    cos3 = 4.0 * c * c * c - 3.0 * c
    return -(2.0 * sin3 * cos3)


def offset_points(p):
    """A négy véges differenciás pont (kelet+, kelet−, észak+, észak−), normalizálva."""
    east, north = east_north(p)
    e = GRADIENT_EPS
    out = []
    for dx, dy, dz in ((east[0] * e, east[1] * e, east[2] * e),
                       (-east[0] * e, -east[1] * e, -east[2] * e),
                       (north[0] * e, north[1] * e, north[2] * e),
                       (-north[0] * e, -north[1] * e, -north[2] * e)):
        px, py, pz = p[0] + dx, p[1] + dy, p[2] + dz
        ln = math.sqrt(px * px + py * py + pz * pz)
        if ln < 1e-12:
            out.append((0.0, 0.0, 0.0))
        else:
            out.append((px / ln, py / ln, pz / ln))
    return east, north, out


def point_temperature(q, samples, annual, is_oceanic, elevation_m, sea_level_m):
    daily = average_factor(q, samples)
    albedo = ALBEDO_OCEAN_FULL if is_oceanic else ALBEDO_LAND_FULL
    t = radiative_temperature(effective_factor(daily, annual), albedo)
    return t + SIMPLE_GREENHOUSE_K - LAPSE_RATE_K_PER_M * max(0.0, elevation_m - sea_level_m)


def wind_from_temperatures(p, east, north, temps):
    base_east = BASE_WIND_SPEED * zonal_band_index(p[2])
    e = GRADIENT_EPS
    grad_e = (temps[0] - temps[1]) / (2.0 * e)
    grad_n = (temps[2] - temps[3]) / (2.0 * e)
    raw_e = grad_e * THERMAL_WIND_COEFF
    raw_n = grad_n * THERMAL_WIND_COEFF
    s = -_CORIOLIS_SIN if p[2] >= 0.0 else _CORIOLIS_SIN
    thermal_e = raw_e * _CORIOLIS_COS - raw_n * s
    thermal_n = raw_e * s + raw_n * _CORIOLIS_COS
    we = base_east + thermal_e
    wn = 0.0 + thermal_n
    return we, wn, (we * east[0] + wn * north[0], we * east[1] + wn * north[1], we * east[2] + wn * north[2])


class WindField:
    def __init__(self, grid, kinds, elevation, sea_level_m):
        self.grid, self.kinds, self.elevation, self.sea_level_m = grid, kinds, elevation, sea_level_m
        windows = annual_sample_windows()
        self.edge_geom = []
        for e in range(len(grid.edge_i)):
            p = grid.edge_mid[e]
            east, north, pts = offset_points(p)
            self.edge_geom.append((p, east, north, pts, [annual_factor(q, windows) for q in pts]))
        self.cell_geom = []
        for c in range(CELL_COUNT):
            p = grid.center[c]
            east, north, pts = offset_points(p)
            self.cell_geom.append((p, east, north, pts, [annual_factor(q, windows) for q in pts]))
        self.cache = {}

    def _wind(self, geom, samples, owner):
        p, east, north, pts, annual = geom
        oceanic = self.kinds[owner] == OCEAN
        temps = [point_temperature(pts[t], samples, annual[t], oceanic, self.elevation[owner], self.sea_level_m)
                 for t in range(4)]
        return wind_from_temperatures(p, east, north, temps)

    def snapshot(self, k):
        if k not in self.cache:
            g = self.grid
            samples = daily_sample_directions(float(k) - 0.5)
            edge_u = [0.0] * len(g.edge_i)
            for e in range(len(g.edge_i)):
                _, _, w3 = self._wind(self.edge_geom[e], samples, g.edge_i[e])
                edge_u[e] = dot(w3, g.edge_normal[e])
            speed = [0.0] * CELL_COUNT
            for c in range(CELL_COUNT):
                we, wn, _ = self._wind(self.cell_geom[c], samples, c)
                speed[c] = math.sqrt(we * we + wn * wn)
            if len(self.cache) > 2:
                self.cache.pop(next(iter(self.cache)))
            self.cache[k] = (edge_u, speed)
        return self.cache[k]

    def at(self, t_seconds):
        k = floor_div(t_seconds, SECONDS_PER_DAY)
        w = (t_seconds - k * SECONDS_PER_DAY) / float(SECONDS_PER_DAY)
        u0, s0 = self.snapshot(k)
        u1, s1 = self.snapshot(k + 1)
        return ([u0[e] + (u1[e] - u0[e]) * w for e in range(len(u0))],
                [s0[c] + (s1[c] - s0[c]) * w for c in range(CELL_COUNT)])


# ---------------------------------------------------------------------------
# Solver (SurfaceTemperatureField)
# ---------------------------------------------------------------------------

class Field:
    def __init__(self, world_seed=184482873278464, t_years=0.0, sea_level_m=0.0):
        self.grid = Grid()
        self.kinds, self.elevation = synthetic_world(self.grid)
        self.baseline = Baseline(self.grid, self.kinds, self.elevation, sea_level_m, world_seed, t_years)
        self.wind = WindField(self.grid, self.kinds, self.elevation, sea_level_m)

    def advect(self, theta_a, edge_u, dt):
        g = self.grid
        ne = len(edge_u)
        inflow = [0.0] * CELL_COUNT
        div = [0.0] * CELL_COUNT
        for e in range(ne):
            ul = edge_u[e] * g.edge_len[e]
            i, j = g.edge_i[e], g.edge_j[e]
            div[i] += ul
            div[j] -= ul
            if ul > 0.0:
                inflow[j] += ul
            else:
                inflow[i] -= ul
        courant = 0.0
        for c in range(CELL_COUNT):
            v = inflow[c] * dt / g.area[c]
            if v > courant:
                courant = v
        substeps = 1 if courant <= MAX_INFLOW_COURANT else int(math.ceil(courant / MAX_INFLOW_COURANT))
        dts = dt / substeps
        theta = theta_a
        for _ in range(substeps):
            flux = [0.0] * CELL_COUNT
            for e in range(ne):
                i, j = g.edge_i[e], g.edge_j[e]
                ul = edge_u[e] * g.edge_len[e]
                f = ul * (theta[i] if ul > 0.0 else theta[j])
                flux[i] += f
                flux[j] -= f
            theta = [theta[c] - dts / g.area[c] * (flux[c] - theta[c] * div[c]) for c in range(CELL_COUNT)]
        return theta, substeps

    def step(self, tick, theta_s, theta_a):
        dt = float(TICK_SECONDS)
        tm = tick * TICK_SECONDS + TICK_SECONDS // 2
        edge_u, speed = self.wind.at(tm)
        theta_a, substeps = self.advect(theta_a, edge_u, dt)
        factor, base = self.baseline.at(tm)
        sun = sun_direction_body_frame(tm / float(SECONDS_PER_DAY))
        new_s, new_a = [0.0] * CELL_COUNT, [0.0] * CELL_COUNT
        ca, la = AIR_HEAT_CAPACITY, AIR_RELAXATION
        for c in range(CELL_COUNT):
            kind = self.kinds[c]
            p = self.grid.center[c]
            cosz = max(0.0, p[0] * sun[0] + p[1] * sun[1] + p[2] * sun[2])
            q = F_PEAK * (1.0 - ALBEDO[kind]) * (cosz - factor[c])
            bs = base[c]
            ls = 4.0 * EMISSIVITY[kind] * SIGMA * bs * bs * bs
            u_eff = speed[c] if speed[c] > MIN_EXCHANGE_WIND_MS else MIN_EXCHANGE_WIND_MS
            k = EXCHANGE_PER_MS * u_eff
            cs = SURFACE_HEAT_CAPACITY[kind]
            ts, ta = theta_s[c], theta_a[c]
            rs = cs / dt * ts + q - 0.5 * (ls * ts + k * (ts - ta))
            ra = ca / dt * ta + 0.5 * (k * (ts - ta) - la * ta)
            a11 = cs / dt + 0.5 * (ls + k)
            a12 = -0.5 * k
            a21 = -0.5 * k
            a22 = ca / dt + 0.5 * (k + la)
            det = a11 * a22 - a12 * a21
            new_s[c] = (rs * a22 - a12 * ra) / det
            new_a[c] = (a11 * ra - a21 * rs) / det
        return new_s, new_a, substeps

    def run(self, start_tick, ticks, theta_s=None, theta_a=None, progress=False):
        ts = theta_s if theta_s is not None else [0.0] * CELL_COUNT
        ta = theta_a if theta_a is not None else [0.0] * CELL_COUNT
        max_sub = 0
        for n in range(ticks):
            ts, ta, sub_count = self.step(start_tick + n, ts, ta)
            max_sub = max(max_sub, sub_count)
            if progress and n % 96 == 0:
                print(f"  tick {start_tick + n} ({n}/{ticks})", flush=True)
        return ts, ta, max_sub

    @staticmethod
    def canonical_start(target_tick):
        return floor_div(target_tick, BUCKET_TICKS) * BUCKET_TICKS - SPIN_UP_TICKS


def sample_cells():
    return [(i * 7919) % CELL_COUNT for i in range(64)]


def weighted_sums(grid, values):
    total = 0.0
    for a, v in zip(grid.area, values):
        total += a * v
    return total


def main():
    full_canonical = "--no-canonical" not in sys.argv
    field = Field()
    g = field.grid
    cells = sample_cells()
    print(f"Rács és szélgeometria kész: {CELL_COUNT} cella, {len(g.edge_i)} él", flush=True)

    vectors = {
        "modelVersion": MODEL_VERSION, "level": LEVEL, "tickSeconds": TICK_SECONDS,
        "bucketTicks": BUCKET_TICKS, "spinUpTicks": SPIN_UP_TICKS,
        "worldSeed": 184482873278464, "tYears": 0.0, "seaLevelM": 0.0,
        "radiativeSmoothing": RADIATIVE_SMOOTHING,
        "sampleCells": cells,
        "metrics": {
            "edgeCount": len(g.edge_i),
            "totalArea": sum(g.area),
            "cells": [{"index": c, "x": g.center[c][0], "y": g.center[c][1], "z": g.center[c][2],
                       "area": g.area[c]} for c in cells],
            "edges": [{"e": e, "i": g.edge_i[e], "j": g.edge_j[e], "length": g.edge_len[e],
                       "midX": g.edge_mid[e][0], "midY": g.edge_mid[e][1], "midZ": g.edge_mid[e][2],
                       "normalX": g.edge_normal[e][0], "normalY": g.edge_normal[e][1],
                       "normalZ": g.edge_normal[e][2]} for e in range(0, len(g.edge_i), 769)],
        },
    }
    kinds_count = [field.kinds.count(k) for k in range(4)]
    print("Felszíntípusok: " + ", ".join(f"{KIND_NAMES[k]}={kinds_count[k]}" for k in range(4)))
    vectors["kindCounts"] = kinds_count

    hour = 3 * 24 + 9
    factor, base = field.baseline.hour(hour)
    vectors["baseline"] = {"hour": hour, "cycleK": field.baseline.cycle, "greenhouseK": field.baseline.constant,
                           "minBaseK": min(base), "maxBaseK": max(base),
                           "cells": [{"index": c, "kind": field.kinds[c], "elevationM": field.elevation[c],
                                      "annualFactor": field.baseline.annual_factor[c],
                                      "annualMean": field.baseline.annual_mean[c],
                                      "factor": factor[c], "baseK": base[c]} for c in cells]}
    print(f"Bázis kész (óra {hour}): Bs min {min(base):.2f} K, max {max(base):.2f} K", flush=True)

    wind_day = 3
    edge_u, speed = field.wind.snapshot(wind_day)
    max_u = max(abs(u) for u in edge_u)
    vectors["wind"] = {"day": wind_day,
                       "edges": [{"e": e, "u": edge_u[e]} for e in range(0, len(edge_u), 769)],
                       "cells": [{"index": c, "speed": speed[c]} for c in cells],
                       "maxAbsEdgeU": max_u, "maxSpeed": max(speed)}
    print(f"Szél kész (nap {wind_day}): max |u_e| {max_u:.3f} m/s, max sebesség {max(speed):.3f} m/s", flush=True)

    start = 3 * 96 + 37
    ticks = 8
    ts, ta, max_sub = field.run(start, ticks)
    vectors["shortRun"] = {"startTick": start, "ticks": ticks, "maxSubsteps": max_sub,
                           "sumAreaThetaS": weighted_sums(g, ts), "sumAreaThetaA": weighted_sums(g, ta),
                           "cells": [{"index": c, "thetaS": ts[c], "thetaA": ta[c]} for c in cells]}
    print(f"Rövid futás: {ticks} tick {start}-tól, max részlépés {max_sub}, "
          f"θs [{min(ts):+.4f}, {max(ts):+.4f}] K, θa [{min(ta):+.4f}, {max(ta):+.4f}] K", flush=True)

    if full_canonical:
        target = 100
        s0 = Field.canonical_start(target)
        print(f"Kanonikus StateAt({target}): indulás {s0}, {target - s0} tick", flush=True)
        ts, ta, max_sub = field.run(s0, target - s0, progress=True)
        _, base_t = field.baseline.at(target * TICK_SECONDS)
        vectors["canonical"] = {"targetTick": target, "startTick": s0, "maxSubsteps": max_sub,
                                "sumAreaThetaS": weighted_sums(g, ts), "sumAreaThetaA": weighted_sums(g, ta),
                                "minThetaS": min(ts), "maxThetaS": max(ts),
                                "minSurfaceK": min(b + t for b, t in zip(base_t, ts)),
                                "maxSurfaceK": max(b + t for b, t in zip(base_t, ts)),
                                "cells": [{"index": c, "thetaS": ts[c], "thetaA": ta[c],
                                           "surfaceK": base_t[c] + ts[c], "airK": base_t[c] + ta[c]}
                                          for c in cells]}
        print(f"Kanonikus: θs [{min(ts):+.3f}, {max(ts):+.3f}] K, θa [{min(ta):+.3f}, {max(ta):+.3f}] K, "
              f"felszín [{vectors['canonical']['minSurfaceK'] - 273.15:+.1f}, "
              f"{vectors['canonical']['maxSurfaceK'] - 273.15:+.1f}] °C, max részlépés {max_sub}", flush=True)

    with open("thermal_field_vectors.json", "w", newline="\n") as f:
        json.dump(vectors, f, indent=1)
    print("thermal_field_vectors.json kiírva")


if __name__ == "__main__":
    main()
