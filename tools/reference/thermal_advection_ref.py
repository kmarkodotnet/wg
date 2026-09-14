"""
Pillanatnyi hőmodell (ND-102) — upwind véges térfogatú advekció mérése a
cubed-sphere rácson. NEM generál tesztvektort; a séma és az élsebesség
kiválasztását segítő mérőeszköz.

Nyitott kérdések, amelyekre mér (ND-102):
  1. divergens szélnél tiszta fluxusforma vagy `θ·div(u)` kompenzáció;
  4. élnormál-sebesség: analitikusan az élközéppontban vagy a két szomszéd
     cellaközép sebességének átlagából.

Rácsgeometria (ND-101, 3. kérdés, B javaslat): húrsokszög-terület a teljes
gömbre normalizálva, húr-élhossz; a cellasarkok a projekt tan-warp vetületéből
(ND-24). Az élnormál a gömbre merőleges élsíkban, a szomszéd felé mutat.

Séma (explicit Euler, csak transzport, lokális tagok nélkül):
    F_e   = u_e · L_e · θ_upwind                 (élenként egyszer számolva)
    C:  θ_i' = θ_i − dt/A_i · Σ_e ±F_e
    A:  θ_i' = θ_i − dt/A_i · (Σ_e ±F_e − θ_i · Σ_e ±u_e·L_e)

Mérések:
  statikus (level 6): élhossz-aszimmetria a két oldalról; merevtest-forgás
      diszkrét divergenciája a két élsebesség-változattal;
  konstans mező (level 4): divergens (pólus felé összetartó) széllel;
  folt (level 4 és 5): merevtest-forgás egy teljes körülfordulásig,
      a z tengely és egy kockasarkokon áthaladó tengely körül —
      energiamegmaradás, csúcsérték-csökkenés (numerikus diffúzió),
      tömegközéppont-hiba.

NEM produkciós kód - orákulum/mérőeszköz a python-reference skill szerint.
"""
import math

from neighbor_ref import DIRECTIONS, neighbor
from sphere_position_ref import position_from_face_uv, position_from_tile

RADIUS_M = 7420000.0  # PlanetConstants.RadiusMeters
WIND_MS = 20.0        # merevtest-forgás egyenlítői sebessége (vizsgálati érték)


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def scale(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(a):
    return math.sqrt(dot(a, a))


def normalize(a):
    return scale(a, 1.0 / norm(a))


class Grid:
    def __init__(self, level):
        n = 1 << level
        self.level = level
        self.count = 6 * n * n
        self.center = [None] * self.count
        self.area = [0.0] * self.count
        # élek: (i, j, L, midpoint, normal i->j), minden rendezetlen pár egyszer
        self.edges = []
        self.asym = 0.0

        def idx(f, u, v):
            return f * n * n + u * n + v

        def corner(f, i, j):
            return position_from_face_uv(f, i / n * 2.0 - 1.0, j / n * 2.0 - 1.0)

        half_edges = {}
        for f in range(6):
            for u in range(n):
                for v in range(n):
                    i = idx(f, u, v)
                    p00, p10 = corner(f, u, v), corner(f, u + 1, v)
                    p11, p01 = corner(f, u + 1, v + 1), corner(f, u, v + 1)
                    self.center[i] = position_from_tile(f, level, u, v)
                    self.area[i] = 0.5 * (norm(cross(sub(p10, p00), sub(p11, p00)))
                                          + norm(cross(sub(p11, p00), sub(p01, p00))))
                    sides = {"right": (p10, p11), "left": (p00, p01),
                             "up": (p01, p11), "down": (p00, p10)}
                    for d in DIRECTIONS:
                        nf, nu, nv = neighbor(f, level, u, v, d)
                        half_edges[(i, idx(nf, nu, nv))] = sides[d]

        total = sum(self.area)
        area_scale = 4.0 * math.pi / total * RADIUS_M * RADIUS_M
        self.area = [a * area_scale for a in self.area]

        for (i, j), (a, b) in half_edges.items():
            if i > j:
                continue
            a2, b2 = half_edges[(j, i)]
            length = norm(sub(a, b))
            self.asym = max(self.asym, abs(length - norm(sub(a2, b2))) / length)
            mid = normalize(scale(add(a, b), 0.5))
            normal = normalize(cross(sub(b, a), mid))
            if dot(normal, sub(self.center[j], self.center[i])) < 0.0:
                normal = scale(normal, -1.0)
            self.edges.append((i, j, length * RADIUS_M, mid, normal))


def solid_body(axis):
    k = normalize(axis)
    return lambda p: scale(cross(k, p), WIND_MS)


def converging_to_pole(p):
    z = (0.0, 0.0, 1.0)
    return scale(sub(z, scale(p, dot(z, p))), WIND_MS)


def edge_velocities(grid, wind, mode):
    out = []
    for i, j, length, mid, normal in grid.edges:
        if mode == "midpoint":
            un = dot(wind(mid), normal)
        else:
            un = dot(scale(add(wind(grid.center[i]), wind(grid.center[j])), 0.5), normal)
        out.append(un)
    return out


def divergence(grid, un):
    div = [0.0] * grid.count
    for (i, j, length, _, _), u in zip(grid.edges, un):
        div[i] += u * length
        div[j] -= u * length
    return div


def step(grid, un, theta, dt, compensated):
    flux_sum = [0.0] * grid.count
    for (i, j, length, _, _), u in zip(grid.edges, un):
        f = u * length * (theta[i] if u > 0.0 else theta[j])
        flux_sum[i] += f
        flux_sum[j] -= f
    if compensated:
        div = divergence(grid, un)
        return [theta[k] - dt / grid.area[k] * (flux_sum[k] - theta[k] * div[k]) for k in range(grid.count)]
    return [theta[k] - dt / grid.area[k] * flux_sum[k] for k in range(grid.count)]


def min_edge(grid):
    return min(e[2] for e in grid.edges)


def blob(grid, p0, r=0.35):
    out = []
    for c in grid.center:
        d2 = dot(sub(c, p0), sub(c, p0)) / (r * r)
        out.append((1.0 - d2) ** 2 if d2 < 1.0 else 0.0)
    return out


def centroid(grid, theta):
    acc = (0.0, 0.0, 0.0)
    for c, a, t in zip(grid.center, grid.area, theta):
        acc = add(acc, scale(c, a * t))
    return normalize(acc)


def static_report(level):
    g = Grid(level)
    print(f"Statikus, level {level}: {g.count} cella, {len(g.edges)} él, "
          f"élhossz-aszimmetria max {g.asym:.2e}, min él {min_edge(g) / 1000:.1f} km")
    for axis_name, axis in (("z", (0.0, 0.0, 1.0)), ("(1,1,1)", (1.0, 1.0, 1.0))):
        for mode in ("midpoint", "center_avg"):
            div = divergence(g, edge_velocities(g, solid_body(axis), mode))
            rel = max(abs(d) / (WIND_MS * math.sqrt(a)) for d, a in zip(div, g.area))
            print(f"  merevtest {axis_name:8s} {mode:10s}: max |Σu·L| / (U·√A) = {rel:.3e}")
    print()


def constant_report(level, steps):
    g = Grid(level)
    dt = 0.5 * min_edge(g) / WIND_MS
    for mode in ("midpoint", "center_avg"):
        un = edge_velocities(g, converging_to_pole, mode)
        for compensated in (False, True):
            theta = [1.0] * g.count
            for _ in range(steps):
                theta = step(g, un, theta, dt, compensated)
            dev = max(abs(t - 1.0) for t in theta)
            name = "A kompenzált" if compensated else "C tiszta   "
            print(f"Konstans mező, level {level}, pólus felé összetartó szél, {steps} lépés, "
                  f"{mode:10s} {name}: max |θ−1| = {dev:.3e}")
    print()


def blob_report(level, axis_name, axis, p0):
    g = Grid(level)
    dt_cfl = 0.5 * min_edge(g) / WIND_MS
    period = 2.0 * math.pi * RADIUS_M / WIND_MS
    steps = int(math.ceil(period / dt_cfl))
    dt = period / steps
    for mode in ("midpoint", "center_avg"):
        un = edge_velocities(g, solid_body(axis), mode)
        for compensated in (False, True):
            theta = blob(g, p0)
            e0 = sum(a * t for a, t in zip(g.area, theta))
            peak0 = max(theta)
            for _ in range(steps):
                theta = step(g, un, theta, dt, compensated)
            e1 = sum(a * t for a, t in zip(g.area, theta))
            c = centroid(g, theta)
            err_km = math.acos(max(-1.0, min(1.0, dot(c, normalize(p0))))) * RADIUS_M / 1000.0
            name = "A kompenzált" if compensated else "C tiszta   "
            print(f"Folt, level {level}, tengely {axis_name:8s} {steps:4d} lépés, {mode:10s} {name}: "
                  f"energia Δ={(e1 - e0) / e0:+.3e}  csúcs {max(theta) / peak0:.3f}×  "
                  f"min θ={min(theta):+.2e}  tömegközéppont-hiba {err_km:.0f} km")
    print()


def main():
    static_report(6)
    constant_report(4, 200)
    blob_report(4, "z", (0.0, 0.0, 1.0), (1.0, 0.0, 0.0))
    blob_report(4, "(1,1,1)", (1.0, 1.0, 1.0), normalize((1.0, -1.0, 0.0)))
    blob_report(5, "z", (0.0, 0.0, 1.0), (1.0, 0.0, 0.0))


if __name__ == "__main__":
    main()
