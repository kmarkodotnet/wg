"""
TileId <-> 3D pozicio (egysegvektor a gombfeluleten) referencia-implementacio.

A vetites a mar elfogadott (ND-24) "erinto" (tan) warp: a lapon beluli
folytonos [-1,1] koordinatat tan(t*pi/4)-gyel torzitjuk, hogy a tile-teruletek
kiegyenlitettebbek legyenek (ld. cubed_sphere_ref.py).

FONTOS (ND-23b/ND-24): a tan/atan NEM garantaltan bitpontos platformok
kozott. Ezert ez a lekepezes ND-24 szerint KONSTRUKCIOS (baked) - egyszer
szamoljuk ki, nem a szimulacio kritikus utjan. A C# port ezert NEM bitre
egyezo ellenorzest kap a Pythonhoz kepest, hanem tolerancia-alaput (ez a
szandekolt, dokumentalt viselkedes, nem hiany).

Lap-bazis konvencio: minden lapnak van egy normal-, jobb- (right) es
fel- (up) tengelye, index (0=X,1=Y,2=Z) + elojel formajaban. Ez a projekt
sajat, onkenyesen valasztott, de kovetkezetes konvencioja - nem kulso
szabvanyhoz igazodik.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math

# (normal_axis, normal_sign, right_axis, right_sign, up_axis, up_sign)
FACE_BASIS = [
    (0, 1, 2, -1, 1, 1),   # face 0: +X
    (0, -1, 2, 1, 1, 1),   # face 1: -X
    (1, 1, 0, 1, 2, 1),    # face 2: +Y
    (1, -1, 0, 1, 2, -1),  # face 3: -Y
    (2, 1, 0, 1, 1, 1),    # face 4: +Z
    (2, -1, 0, -1, 1, 1),  # face 5: -Z
]


def warp_tan(t):
    return math.tan(t * math.pi / 4.0)


def unwarp_tan(s):
    return math.atan(s) * 4.0 / math.pi


def continuous_uv_from_tile(level, u, v):
    """Tile index -> a tile KOZEPE folytonos [-1,1) lap-koordinataban."""
    n = 1 << level
    uc = (u + 0.5) / n * 2.0 - 1.0
    vc = (v + 0.5) / n * 2.0 - 1.0
    return uc, vc


def tile_from_continuous_uv(level, uc, vc):
    n = 1 << level
    u = int((uc + 1.0) / 2.0 * n)
    v = int((vc + 1.0) / 2.0 * n)
    u = max(0, min(n - 1, u))
    v = max(0, min(n - 1, v))
    return u, v


def position_from_face_uv(face, uc, vc):
    """Folytonos lap-koordinata (mar [-1,1]-ben) -> egysegvektor a gombon."""
    normal_axis, normal_sign, right_axis, right_sign, up_axis, up_sign = FACE_BASIS[face]
    x = warp_tan(uc)
    y = warp_tan(vc)

    p = [0.0, 0.0, 0.0]
    p[normal_axis] = float(normal_sign)
    p[right_axis] += x * right_sign
    p[up_axis] += y * up_sign

    length = math.sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2])
    return (p[0] / length, p[1] / length, p[2] / length)


def position_from_tile(face, level, u, v):
    uc, vc = continuous_uv_from_tile(level, u, v)
    return position_from_face_uv(face, uc, vc)


def face_uv_from_position(x, y, z):
    """Egysegvektor -> (face, folytonos uc, vc a [-1,1] tartomanyban)."""
    p = (x, y, z)
    dominant = max(range(3), key=lambda i: abs(p[i]))
    dominant_sign = 1 if p[dominant] >= 0 else -1

    face = None
    for f, (na, ns, ra, rs, ua, us) in enumerate(FACE_BASIS):
        if na == dominant and ns == dominant_sign:
            face = f
            break
    assert face is not None

    normal_axis, normal_sign, right_axis, right_sign, up_axis, up_sign = FACE_BASIS[face]
    scale = 1.0 / abs(p[normal_axis])
    pn = (p[0] * scale, p[1] * scale, p[2] * scale)

    warped_x = pn[right_axis] * right_sign
    warped_y = pn[up_axis] * up_sign
    uc = unwarp_tan(warped_x)
    vc = unwarp_tan(warped_y)
    return face, uc, vc


def tile_from_position(x, y, z, level):
    face, uc, vc = face_uv_from_position(x, y, z)
    u, v = tile_from_continuous_uv(level, uc, vc)
    return face, u, v


if __name__ == "__main__":
    import json
    from threefry_ref import threefry4x64

    # Onellenorzes: TileId -> pozicio -> TileId roundtrip minden lapon,
    # tobb szinten, sok mintaval.
    failures = 0
    checked = 0
    gen_seed = 0xFEED1234BEEF5678
    for level in [0, 1, 2, 5, 6, 10, 28]:
        n = 1 << level
        for i in range(300):
            p = threefry4x64([i, level, 0, 0], [gen_seed, 0, 2, 0], 20)
            face = p[0] % 6
            u = p[1] % n
            v = p[2] % n

            x, y, z = position_from_tile(face, level, u, v)
            gf, gu, gv = tile_from_position(x, y, z, level)
            checked += 1
            if (gf, gu, gv) != (face, u, v):
                failures += 1
                print(f"HIBA: face={face} level={level} u={u} v={v} -> "
                      f"vissza: face={gf} u={gu} v={gv}")

    print(f"Roundtrip onellenorzes: {checked - failures}/{checked} egyezik")
    assert failures == 0, f"{failures} roundtrip hiba"

    # Tesztvektorok a C# porthoz - TOLERANCIA-ALAPU osszehasonlitasra,
    # NEM bitpontos egyezesre (ld. modul docstring, ND-23b/ND-24).
    vectors = []
    edge_cases = [
        (0, 0, 0, 0), (1, 0, 0, 0), (2, 0, 0, 0),
        (3, 0, 0, 0), (4, 0, 0, 0), (5, 0, 0, 0),
        (4, 6, 0, 0), (4, 6, 63, 63), (4, 6, 0, 63), (4, 6, 63, 0),
        (2, 10, 512, 512),
    ]
    for face, level, u, v in edge_cases:
        x, y, z = position_from_tile(face, level, u, v)
        vectors.append({"face": face, "level": level, "u": u, "v": v,
                         "x": x, "y": y, "z": z})

    gen_seed2 = 0xABAD1DEA00000001
    for i in range(200):
        p = threefry4x64([i, 0, 0, 0], [gen_seed2, 0, 3, 0], 20)
        face = p[0] % 6
        level = 1 + (p[1] % 12)
        n = 1 << level
        u = p[2] % n
        v = p[3] % n
        x, y, z = position_from_tile(face, level, u, v)
        vectors.append({"face": face, "level": level, "u": u, "v": v,
                         "x": x, "y": y, "z": z})

    with open("sphere_position_vectors.json", "w", newline="\n") as f:
        json.dump({"vectors": vectors}, f, indent=1)
    print(f"{len(vectors)} pozicio-tesztvektor generalva")
