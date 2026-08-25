"""
Szomszedsagi logika referencia-implementacioja es kimerito ellenorzese
(docs/05-milestones.md §2.4 - "itt lesz a hiba").

A modszer: a szomszedot NEM kulon-kulon lekodolt el-tablazattal keressuk
(24 kezzel irt eset, konnyen elronhato), hanem UJRAHASZNALJUK a mar
verifikalt position_from_face_uv / face_uv_from_position fuggvenyeket:
egy tile-tol egy lepesnyit elmozdulunk a folytonos (uc,vc) terben (akar
kilepve a [-1,1] tartomanybol), majd a kapott 3D pontot ujra levetitjuk a
domans-tengely-detektalassal - ez automatikusan a szomszedos lapra "landol",
ha a lepes atlepte az elt.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import json
from sphere_position_ref import position_from_face_uv, face_uv_from_position

DIRECTIONS = ["right", "left", "up", "down"]  # +u, -u, +v, -v


def neighbor(face, level, u, v, direction):
    n = 1 << level
    uc = (u + 0.5) / n * 2.0 - 1.0
    vc = (v + 0.5) / n * 2.0 - 1.0
    step = 2.0 / n

    if direction == "right":
        uc2, vc2 = uc + step, vc
    elif direction == "left":
        uc2, vc2 = uc - step, vc
    elif direction == "up":
        uc2, vc2 = uc, vc + step
    elif direction == "down":
        uc2, vc2 = uc, vc - step
    else:
        raise ValueError(direction)

    if -1.0 < uc2 < 1.0 and -1.0 < vc2 < 1.0:
        # Ugyanazon a lapon marad
        nu = int((uc2 + 1.0) / 2.0 * n)
        nv = int((vc2 + 1.0) / 2.0 * n)
        return face, max(0, min(n - 1, nu)), max(0, min(n - 1, nv))

    # At kell lepni a szomszedos lapra: a raw (nem normalt) pont ket
    # tengelye is hatarra kerul/tulcsordul, a dominans-tengely detektalas
    # automatikusan a masik lapra vezet.
    x, y, z = position_from_face_uv(face, uc2, vc2)
    face2, uc2b, vc2b = face_uv_from_position(x, y, z)

    nu = int((uc2b + 1.0) / 2.0 * n)
    nv = int((vc2b + 1.0) / 2.0 * n)
    return face2, max(0, min(n - 1, nu)), max(0, min(n - 1, nv))


def all_neighbors(face, level, u, v):
    """A tile disztinkt szomszedjai (halmaz - a duplikatumok a sarkoknal
    ide olvadnak ossze, ha ket irany ugyanoda vezet)."""
    result = set()
    for d in DIRECTIONS:
        result.add(neighbor(face, level, u, v, d))
    return result


def is_corner_tile(level, u, v):
    n = 1 << level
    return (u in (0, n - 1)) and (v in (0, n - 1))


if __name__ == "__main__":
    import json
    from threefry_ref import threefry4x64

    # Tesztvektorok a C# porthoz - determinisztikus mintavetel a Threefry
    # oraklummal, minden LOD-szinten es minden iranyban.
    vectors = []
    gen_seed = 0x5EED_1234_AAAA_5555
    for level in [1, 2, 5, 6, 10, 28]:
        n = 1 << level
        for i in range(80):
            p = threefry4x64([i, level, 0, 0], [gen_seed, 0, 4, 0], 20)
            face = p[0] % 6
            u = p[1] % n
            v = p[2] % n
            for d_index, d in enumerate(DIRECTIONS):
                nf, nu, nv = neighbor(face, level, u, v, d)
                vectors.append({
                    "face": face, "level": level, "u": u, "v": v,
                    "direction": d,
                    "nFace": nf, "nU": nu, "nV": nv,
                })
    with open("neighbor_vectors.json", "w", newline="\n") as f:
        json.dump({"directions": DIRECTIONS, "vectors": vectors}, f, indent=1)
    print(f"{len(vectors)} szomszed-tesztvektor generalva\n")

    for level in [3, 4, 5]:
        n = 1 << level
        all_tiles = [(f, u, v) for f in range(6) for u in range(n) for v in range(n)]
        neighbor_map = {t: all_neighbors(t[0], level, t[1], t[2]) for t in all_tiles}

        # NeighborCount
        count_hist = {}
        corner_counts = []
        noncorner_counts = []
        for t, neighbors in neighbor_map.items():
            _, u, v = t
            c = len(neighbors)
            count_hist[c] = count_hist.get(c, 0) + 1
            if is_corner_tile(level, u, v):
                corner_counts.append(c)
            else:
                noncorner_counts.append(c)

        # NeighborSymmetry
        symmetry_failures = 0
        for t, neighbors in neighbor_map.items():
            for nb in neighbors:
                if t not in neighbor_map[nb]:
                    symmetry_failures += 1

        # NoGaps / coverage sanity: minden tile pontosan egyszer szerepel
        assert len(all_tiles) == len(set(all_tiles)) == 6 * n * n

        n_corner_tiles = sum(1 for f, u, v in all_tiles if is_corner_tile(level, u, v))

        print(f"--- level={level} (n={n}) ---")
        print(f"  tile-ok: {len(all_tiles)}, sarok-tile-ok: {n_corner_tiles}")
        print(f"  szomszedszam-hisztogram: {count_hist}")
        print(f"  sarok-tile szomszedszamok (egyedi ertekek): {sorted(set(corner_counts))}")
        print(f"  nem-sarok szomszedszamok (egyedi ertekek): {sorted(set(noncorner_counts))}")
        print(f"  szimmetria-hibak: {symmetry_failures}")
        print()
