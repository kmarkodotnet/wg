"""
Pillanatnyi hőmodell (ND-100 módosítás) — egycellás mérés a bázis sarki
éjszakai hibájára adott két javítási irányra.

Kiváltó ok (thermal_field_ref.py első futása, 2026-09-13): a napi faktoros
bázis (TemperatureKelvinFull szerkezete) sarki éjszakán a radiatív tag nullára
esése miatt ~29 K-t adott, a termikus szél a sarki éjszaka határán a T^(1/4)
végtelen deriváltja miatt ~5700 m/s-ot.

1. mérőkör (A változat, éves átlagos bázis, az évszakot a solver integrálja),
   2026-09-13: második év Ts 80°-os óceánon −28 … +22 °C; a kanonikus
   újraindítás hibája óceánon 60 nap spin-up után is 5,3 K (45°) / 11,9 K
   (80°) — az óceán ~40 napos memóriája miatt. ELVETVE.

2. mérőkör (S változat, ez a fájl): marad az évszakos bázis, de a sugárzási
   faktor az éves átlag felé simul (szállítás/tehetetlenség proxy, M13):
       f_eff = (1 − β)·f_napi + β·f_éves
       Bs = Ba = T_rad(f_eff) + T_greenhouse + T_ocean − T_alt   (ciklus 0)
       T_ocean = 0,3·(éves átlag T_rad(f_eff_j) − T_rad(f_eff))  (csak óceán)
       ΔQ(t)  = F·(1 − albedo)·( max(0, n·s(t)) − f_napi(t) )
   f_napi óránként, középre igazított 24 mintás ablakkal, órán belül lineárisan.

Mérések (szél és advekció nélkül, CN-IMEX, 900 s), β ∈ {0,3; 0,5}:
  - egy teljes év Ts minimuma/maximuma/átlaga (bázis + anomália);
  - bázis minimuma sarki éjszakán;
  - 10 napos, nulla kezdőállapotú spin-up hibája a folyamatos futáshoz képest.

NEM generál tesztvektort. NEM produkciós kód - mérőeszköz a python-reference skill szerint.
"""
import math

import deterministic_math_ref as dm

SIGMA = 5.670374419e-8
F_PEAK = 1361.0
ORBITAL_PERIOD_DAYS = 365.25
AXIAL_TILT_RAD = 23.44 * math.pi / 180.0
TICK = 900
TICKS_PER_DAY = 96

CS = {"Land": 1400.0 * (0.19 * 4186.8) * 0.5, "Ocean": 1025.0 * 3991.86795711963 * 10.0}
ALBEDO = {"Land": 0.30, "Ocean": 0.06}
EMISSIVITY = {"Land": 0.95, "Ocean": 0.96}
CA = 1.225 * 1005.0 * 1000.0
LA = CA / (4.0 * 86400.0)
KSA = 1.225 * 1005.0 * 1.15e-3 * 5.0


def sun(t_days):
    st, ct = dm.sin_cos(2.0 * math.pi * (t_days / ORBITAL_PERIOD_DAYS))
    sx, sy = -ct, -st
    s_t, c_t = dm.sin_cos(AXIAL_TILT_RAD)
    s_r, c_r = dm.sin_cos(2.0 * math.pi * t_days)
    return (c_r * sx + c_t * s_r * sy, -s_r * sx + c_t * c_r * sy, -s_t * sy)


def factor_window(p, start_day):
    total = 0.0
    for i in range(24):
        s = sun(start_day + i * (1.0 / 24))
        total += max(0.0, p[0] * s[0] + p[1] * s[1] + p[2] * s[2])
    return total / 24


def t_rad(factor, albedo):
    absorbed = F_PEAK * factor * (1.0 - albedo)
    return math.sqrt(math.sqrt(absorbed / SIGMA)) if absorbed > 0.0 else 0.0


class Cell:
    def __init__(self, lat_deg, kind, beta):
        lat = lat_deg * math.pi / 180.0
        self.lat_deg = lat_deg
        self.p = (math.cos(lat), 0.0, math.sin(lat))
        self.kind, self.beta = kind, beta
        windows = [factor_window(self.p, j * (ORBITAL_PERIOD_DAYS / 12)) for j in range(12)]
        self.annual = sum(windows) / 12
        self.annual_mean_trad = sum(t_rad(self.eff(w), ALBEDO[kind]) for w in windows) / 12
        self.hour_cache = {}

    def eff(self, daily):
        return (1.0 - self.beta) * daily + self.beta * self.annual

    def hour(self, h):
        if h not in self.hour_cache:
            daily = factor_window(self.p, h / 24.0 - 0.5)
            tr = t_rad(self.eff(daily), ALBEDO[self.kind])
            t_ocean = 0.3 * (self.annual_mean_trad - tr) if self.kind == "Ocean" else 0.0
            base = tr + 33.0 + t_ocean
            if len(self.hour_cache) > 3:
                self.hour_cache.pop(next(iter(self.hour_cache)))
            self.hour_cache[h] = (daily, base)
        return self.hour_cache[h]

    def at(self, t_seconds):
        h = t_seconds // 3600
        w = (t_seconds - h * 3600) / 3600.0
        d0, b0 = self.hour(h)
        d1, b1 = self.hour(h + 1)
        return d0 + (d1 - d0) * w, b0 + (b1 - b0) * w

    def step(self, tick, ts, ta):
        dt = float(TICK)
        tm_s = tick * TICK + TICK // 2
        daily, bs = self.at(tm_s)
        s = sun(tm_s / 86400.0)
        cosz = max(0.0, self.p[0] * s[0] + self.p[1] * s[1] + self.p[2] * s[2])
        q = F_PEAK * (1.0 - ALBEDO[self.kind]) * (cosz - daily)
        ls = 4.0 * EMISSIVITY[self.kind] * SIGMA * bs * bs * bs
        k, cs = KSA, CS[self.kind]
        rs = cs / dt * ts + q - 0.5 * (ls * ts + k * (ts - ta))
        ra = CA / dt * ta + 0.5 * (k * (ts - ta) - LA * ta)
        a11, a12, a21, a22 = cs / dt + 0.5 * (ls + k), -0.5 * k, -0.5 * k, CA / dt + 0.5 * (k + LA)
        det = a11 * a22 - a12 * a21
        return (rs * a22 - a12 * ra) / det, (a11 * ra - a21 * rs) / det, bs


def measure(beta):
    print(f"β = {beta}")
    targets = (40, 120, 200, 280, 350)
    for lat in (0.0, 45.0, 80.0):
        for kind in ("Land", "Ocean"):
            c = Cell(lat, kind, beta)
            ts = ta = 0.0
            lo, hi, total, cnt, base_lo = 1e9, -1e9, 0.0, 0, 1e9
            snap = {}
            spin_start = {t - 10: t for t in targets}
            for n in range(385 * TICKS_PER_DAY):
                if n % TICKS_PER_DAY == 0:
                    snap[n // TICKS_PER_DAY] = (ts, ta)
                ts, ta, bs = c.step(n, ts, ta)
                if n >= 20 * TICKS_PER_DAY:
                    surf = bs + ts
                    lo, hi = min(lo, surf), max(hi, surf)
                    total += surf
                    cnt += 1
                    base_lo = min(base_lo, bs)
            worst = 0.0
            for start, target in spin_start.items():
                s_ts = s_ta = 0.0
                for n in range(start * TICKS_PER_DAY, target * TICKS_PER_DAY):
                    s_ts, s_ta, _ = c.step(n, s_ts, s_ta)
                ref = snap[target]
                worst = max(worst, abs(s_ts - ref[0]), abs(s_ta - ref[1]))
            print(f"  lat {lat:4.0f} {kind:5s}: Ts min {lo - 273.15:+6.1f} °C  max {hi - 273.15:+6.1f} °C  "
                  f"átlag {total / cnt - 273.15:+6.1f} °C  bázis min {base_lo - 273.15:+6.1f} °C  "
                  f"10 napos spin-up hiba {worst:.3f} K")
    print()


def main():
    for beta in (0.3, 0.5):
        measure(beta)


if __name__ == "__main__":
    main()
