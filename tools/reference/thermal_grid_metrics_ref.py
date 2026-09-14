"""
Level-6 rácsmetrika mérése a pillanatnyi hőmodellhez (ND-101, 3. nyitott kérdés).

Kérdés: elég-e a véges térfogatú sémához a csak `+ - * / sqrt` műveletekkel
számolható húrsokszög-terület és húr-élhossz, vagy a pontos gömbi terület/ív
kell (ami `atan`/`acos` nélkül nem számolható, és a DeterministicMath-ban
nincs ilyen függvény)?

Mérések a teljes level-6 rácson (6 lap, 64x64 cella lapoként):
  - cellánként a pontos gömbi terület (Van Oosterom–Strackee, két háromszög)
    és a húrsokszög (két síkháromszög) területének relatív eltérése;
  - ugyanez a teljes összegre normalizált húrterülettel;
  - élenként a gömbi ív és a húr hosszának relatív eltérése;
  - a cellaterületek max/min aránya.

A cellasarkok a projekt tan-warp vetületéből jönnek (sphere_position_ref,
ND-24). Csak mérőeszköz, nem generál tesztvektort.

NEM produkciós kód - orákulum/mérőeszköz a python-reference skill szerint.
"""
import math

from cubed_sphere_ref import solid_angle_triangle
from sphere_position_ref import position_from_face_uv

LEVEL = 6


def corner(face, n, i, j):
    """Az (i, j) rácspont (0..n) egységvektora a lapon."""
    return position_from_face_uv(face, i / n * 2.0 - 1.0, j / n * 2.0 - 1.0)


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(a):
    return math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])


def chord_triangle_area(a, b, c):
    return 0.5 * norm(cross(sub(b, a), sub(c, a)))


def arc_length(a, b):
    """Nagykör-ív egységgömbön (atan2 alak, numerikusan stabil)."""
    return math.atan2(norm(cross(a, b)), a[0] * b[0] + a[1] * b[1] + a[2] * b[2])


def main():
    n = 1 << LEVEL
    sph, chord = [], []
    edge_rel = []
    for face in range(6):
        for i in range(n):
            for j in range(n):
                p00 = corner(face, n, i, j)
                p10 = corner(face, n, i + 1, j)
                p11 = corner(face, n, i + 1, j + 1)
                p01 = corner(face, n, i, j + 1)
                sph.append(solid_angle_triangle(p00, p10, p11) + solid_angle_triangle(p00, p11, p01))
                chord.append(chord_triangle_area(p00, p10, p11) + chord_triangle_area(p00, p11, p01))
                # Minden cellának két saját élét mérjük (alsó és bal), így
                # a lapon belüli élek egyszer, a lapperemek is szerepelnek.
                for a, b in ((p00, p10), (p00, p01)):
                    arc = arc_length(a, b)
                    edge_rel.append((arc - norm(sub(a, b))) / arc)

    total_sph = sum(sph)
    total_chord = sum(chord)
    scale = total_sph / total_chord
    rel = [(c - s) / s for s, c in zip(sph, chord)]
    rel_norm = [(c * scale - s) / s for s, c in zip(sph, chord)]

    print(f"Level {LEVEL}: {len(sph)} cella")
    print(f"  gömbi összterület = {total_sph:.12f}  (4*pi = {4 * math.pi:.12f})")
    print(f"  húrsokszög összterület = {total_chord:.12f}")
    print(f"  cellaterület max/min arány (gömbi) = {max(sph) / min(sph):.6f}")
    print(f"  húr vs gömbi relatív eltérés: min={min(rel):.3e} max={max(rel):.3e}")
    print(f"  normalizált húr vs gömbi:     min={min(rel_norm):.3e} max={max(rel_norm):.3e}")
    print(f"  húr vs ív élhossz relatív eltérés: min={min(edge_rel):.3e} max={max(edge_rel):.3e}")
    print(f"  átlagos cellaél (7 420 km sugárral): "
          f"{math.sqrt(total_sph / len(sph)) * 7420.0:.1f} km")


if __name__ == "__main__":
    main()
