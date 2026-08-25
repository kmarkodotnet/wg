"""
Morton-kod (bit-interleaving) es TileId referencia-implementacio.
Cel: a C# TileId port verifikalasa es tesztvektorok generalasa.
Ez NEM produkcios kod - csak orakulum, a python-reference skill szerint.

Bit-layout (docs/05-milestones.md §2.1):
  [63:61] face   (3 bit, 0-5)
  [60:56] level  (5 bit, 0-28)
  [55:0]  morton (56 bit; u a paros biteken, v a paratlan biteken)
"""
import json
from threefry_ref import threefry4x64, M64

MAX_LEVEL = 28


def spread_bits(x):
    """28 bites bemenet -> minden masodik bitre szorva (56 bites eredmeny)."""
    x &= (1 << 28) - 1
    x = (x | (x << 16)) & 0x0000FFFF0000FFFF
    x = (x | (x << 8)) & 0x00FF00FF00FF00FF
    x = (x | (x << 4)) & 0x0F0F0F0F0F0F0F0F
    x = (x | (x << 2)) & 0x3333333333333333
    x = (x | (x << 1)) & 0x5555555555555555
    return x


def compact_bits(x):
    """Az interleaving inverze: minden masodik bit -> 28 bites ertek."""
    x &= 0x5555555555555555
    x = (x | (x >> 1)) & 0x3333333333333333
    x = (x | (x >> 2)) & 0x0F0F0F0F0F0F0F0F
    x = (x | (x >> 4)) & 0x00FF00FF00FF00FF
    x = (x | (x >> 8)) & 0x0000FFFF0000FFFF
    x = (x | (x >> 16)) & 0xFFFFFFFF
    return x & 0x0FFFFFFF


def morton_encode(u, v):
    return spread_bits(u) | (spread_bits(v) << 1)


def morton_decode(m):
    return compact_bits(m), compact_bits(m >> 1)


def tile_id(face, level, u, v):
    assert 0 <= face <= 5
    assert 0 <= level <= MAX_LEVEL
    assert 0 <= u < (1 << level) or level == 0 and u == 0
    assert 0 <= v < (1 << level) or level == 0 and v == 0
    morton = morton_encode(u, v)
    return (face << 61) | (level << 56) | morton


def decompose(value):
    face = value >> 61
    level = (value >> 56) & 0x1F
    morton = value & ((1 << 56) - 1)
    u, v = morton_decode(morton)
    return face, level, u, v


def parent(value):
    face, level, _, _ = decompose(value)
    morton = value & ((1 << 56) - 1)
    return (face << 61) | ((level - 1) << 56) | (morton >> 2)


def child(value, index):
    assert 0 <= index <= 3
    face, level, _, _ = decompose(value)
    morton = value & ((1 << 56) - 1)
    return (face << 61) | ((level + 1) << 56) | ((morton << 2) | index)


if __name__ == "__main__":
    vectors = []

    # 1. Kezzel valasztott elesetek
    edge_cases = [
        (0, 0, 0, 0),
        (5, 0, 0, 0),
        (0, MAX_LEVEL, (1 << MAX_LEVEL) - 1, (1 << MAX_LEVEL) - 1),
        (3, MAX_LEVEL, 0, (1 << MAX_LEVEL) - 1),
        (2, 6, 63, 0),
        (2, 6, 0, 63),
        (2, 6, 42, 17),
    ]
    for face, level, u, v in edge_cases:
        vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "value": f"{tile_id(face, level, u, v):016x}",
        })

    # 2. Determinisztikusan generalt tomeg - a Threefry oracle-bol
    gen_seed = 0xC0FFEE1234ABCDEF
    for i in range(200):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 1, 0], 20)
        face = p[0] % 6
        level = 1 + (p[1] % MAX_LEVEL)
        u = p[2] % (1 << level)
        v = p[3] % (1 << level)
        vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "value": f"{tile_id(face, level, u, v):016x}",
        })

    # Roundtrip + parent/child ellenorzes minden vektorra (ha nem all fenn, hiba)
    for vec in vectors:
        val = int(vec["value"], 16)
        f, l, u, v = decompose(val)
        assert f == vec["face"] and l == vec["level"] and u == vec["u"] and v == vec["v"], vec
        if l > 0:
            par = parent(val)
            pf, pl, pu, pv = decompose(par)
            assert pf == f and pl == l - 1 and pu == u // 2 and pv == v // 2

    out = {"maxLevel": MAX_LEVEL, "vectors": vectors}
    with open("morton_vectors.json", "w", newline="\n") as f:
        json.dump(out, f, indent=1)

    print(f"{len(vectors)} TileId tesztvektor generalva es helyben ellenorizve (roundtrip + parent)")
