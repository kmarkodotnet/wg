"""
Cubed-sphere vetites referencia-merese: naiv linearis vs. erintо (tan)-warpolt
lapkoordinata, a docs/05-milestones.md M2 §2.2-ben allitott terulet-kiegyenlites
verifikalasara (max/min tile-terulet arany).

A kocka szimmetriaja miatt a torzitas mintazata minden lapon azonos, ezert
eleg egyetlen reprezentativ lapot (z=1 sik) merni.

NEM produkcios kod - csak orakulum/meroeszkoz, a python-reference skill szerint.
"""
import math


def warp_linear(t):
    return t


def warp_tan(t):
    return math.tan(t * math.pi / 4.0)


def face_point(u, v, warp):
    """Egyetlen reprezentativ kockalap (z=1 sik) lokalis (u,v) -> egysegvektor."""
    x, y, z = warp(u), warp(v), 1.0
    length = math.sqrt(x * x + y * y + z * z)
    return (x / length, y / length, z / length)


def solid_angle_triangle(a, b, c):
    """Van Oosterom-Strackee formula: gomb-haromszog terulete (szteradian)."""
    ax, ay, az = a
    bx, by, bz = b
    cx, cy, cz = c
    triple = ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx)
    dot_ab = ax * bx + ay * by + az * bz
    dot_bc = bx * cx + by * cy + bz * cz
    dot_ca = cx * ax + cy * ay + cz * az
    denom = 1.0 + dot_ab + dot_bc + dot_ca
    return 2.0 * math.atan2(abs(triple), denom)


def cell_area(u0, u1, v0, v1, warp):
    p00 = face_point(u0, v0, warp)
    p10 = face_point(u1, v0, warp)
    p11 = face_point(u1, v1, warp)
    p01 = face_point(u0, v1, warp)
    return solid_angle_triangle(p00, p10, p11) + solid_angle_triangle(p00, p11, p01)


def measure(level, warp):
    n = 2 ** level
    step = 2.0 / n
    areas = []
    for i in range(n):
        u0 = -1.0 + i * step
        u1 = u0 + step
        for j in range(n):
            v0 = -1.0 + j * step
            v1 = v0 + step
            areas.append(cell_area(u0, u1, v0, v1, warp))
    return areas


def report(name, areas):
    lo, hi = min(areas), max(areas)
    total = sum(areas)
    print(f"{name}:")
    print(f"  min={lo:.8e}  max={hi:.8e}  arany(max/min)={hi / lo:.4f}")
    print(f"  osszterulet={total:.6f}  (var: 4*pi/6 = {4 * math.pi / 6:.6f}, egy lapra)")


if __name__ == "__main__":
    level = 6
    n = 2 ** level
    print(f"Level {level}  (n={n}x{n} = {n * n} tile / lap)\n")
    report("Naiv linearis (warp(t)=t)", measure(level, warp_linear))
    print()
    report("Erinto-warpolt (warp(t)=tan(t*pi/4))", measure(level, warp_tan))
