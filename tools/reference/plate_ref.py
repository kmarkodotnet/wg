"""
Lemez-generalas referencia-implementacioja M4-hez (docs/05-milestones.md
§4.1): Euler-polus (mag) pontok N lemezhez + gombi Voronoi tile-hozzarendeles.

HATOKOR (tudatosan szukitve M4-re - ld. docs/05-milestones.md "M4 -
Kovetkezo, reszletes terv" hatokor-szakasza):
  - A lemezek POZICIOJA fix (nincs P(t)=R(wt)P0 mozgas - M10-re marad).
  - Csak a lemez-MAG (seed point) generalasa + Voronoi-hozzarendeles -
    kereg-tipus, elevation, tengerszint kesobbi lepesek (§4.2-4.4).

A mintavetel UGYANAZT az elutasitasos algoritmust hasznalja, mint a C#
DeterministicRandom.SampleUnitVector3 (ld. src/WorldGen.Core/Random/
DeterministicRandom.cs): a Sample4-bol jovo (a,b,c) [-1,1)-be kepezve,
elfogadva ha 1e-12 < lenSq <= 1, kulonben sampleIndex++ es ujra.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math
import json
from threefry_ref import threefry4x64
from sphere_position_ref import position_from_tile

M64 = (1 << 64) - 1
SCALE53 = 2.0 ** -53


def _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index):
    key = [
        world_seed & M64,
        ((domain_id & 0xFFFFFFFF) << 32) | (property_id & 0xFFFFFFFF),
        1,  # ALGORITHM_VERSION, ld. DeterministicRandom.cs
        0,
    ]
    ctr = [spatial_id & M64, time_bucket & M64, sample_index & M64, 0]
    return threefry4x64(ctr, key, 20)


def sample4(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index):
    xs = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index)
    return [(x >> 11) * SCALE53 for x in xs]


def sample_unit_vector3(world_seed, domain_id, spatial_id, time_bucket, property_id):
    """1:1 port a C# DeterministicRandom.SampleUnitVector3 elutasitasos modszererol."""
    i = 0
    while True:
        a, b, c, _ = sample4(world_seed, domain_id, spatial_id, time_bucket, property_id, i)
        px, py, pz = 2.0 * a - 1.0, 2.0 * b - 1.0, 2.0 * c - 1.0
        len_sq = px * px + py * py + pz * pz
        if 1e-12 < len_sq <= 1.0:
            inv = 1.0 / math.sqrt(len_sq)
            return px * inv, py * inv, pz * inv, i  # i: hany elutasitas kellett (diagnosztika)
        i += 1


DOMAIN_TECTONICS = 2
PROPERTY_PLATE_SEED_POINT = 10


def generate_plate_seeds(world_seed, plate_count):
    """N lemez-mag egysegvektor, egyenletesen a gomb feluleten."""
    seeds = []
    for plate_id in range(plate_count):
        x, y, z, rejections = sample_unit_vector3(
            world_seed, DOMAIN_TECTONICS, plate_id, 0, PROPERTY_PLATE_SEED_POINT
        )
        seeds.append((x, y, z))
    return seeds


def assign_plate(position, seeds):
    """Legkozelebbi mag (max dot product = legkisebb gombi tavolsag)."""
    x, y, z = position
    best_id, best_dot = -1, -2.0
    for i, (sx, sy, sz) in enumerate(seeds):
        dot = x * sx + y * sy + z * sz
        if dot > best_dot:
            best_dot = dot
            best_id = i
    return best_id


if __name__ == "__main__":
    world_seed = 0xA7C944210000
    plate_count = 12
    level = 6  # a kesobbi klima-alapszinttel egyezoen, ld. docs/05-milestones §2.3

    seeds = generate_plate_seeds(world_seed, plate_count)
    print(f"{plate_count} lemez-mag generalva, world_seed=0x{world_seed:x}\n")

    n = 1 << level
    tile_counts = [0] * plate_count
    total_tiles = 0
    for face in range(6):
        for u in range(n):
            for v in range(n):
                pos = position_from_tile(face, level, u, v)
                plate_id = assign_plate(pos, seeds)
                tile_counts[plate_id] += 1
                total_tiles += 1

    print(f"Level {level} ({total_tiles} tile) lemez-eloszlas:")
    for i, count in enumerate(tile_counts):
        pct = 100.0 * count / total_tiles
        print(f"  lemez {i:2d}: {count:6d} tile ({pct:5.2f}%)")

    empty = sum(1 for c in tile_counts if c == 0)
    max_pct = 100.0 * max(tile_counts) / total_tiles
    min_pct = 100.0 * min(c for c in tile_counts if c > 0) / total_tiles if empty < plate_count else 0.0
    print(f"\nÜres lemez: {empty}, legnagyobb: {max_pct:.2f}%, legkisebb (nem-üres): {min_pct:.2f}%")
    assert empty == 0, "Nem lehet üres lemez ilyen sok tile mellett - gyanús"
    assert max_pct < 40.0, "Egy lemez ne uralja a gömb csaknem felét"
    print("OK - a lemez-eloszlás plauzibilis")

    # Determinizmus-ellenorzes: ismetelt hivas ugyanazt adja
    seeds2 = generate_plate_seeds(world_seed, plate_count)
    assert seeds == seeds2, "A lemez-generalas nem tiszta fuggveny!"
    print("OK - a lemez-generálás determinisztikus (tiszta függvény)")

    # Kulonbozo world_seed mas lemez-elrendezest ad
    seeds_other = generate_plate_seeds(world_seed + 1, plate_count)
    assert seeds != seeds_other, "Kulonbozo seed ugyanazt a lemez-elrendezest adta - gyanús"
    print("OK - más world_seed más lemez-elrendezést ad")

    # Tesztvektorok a C# porthoz.
    # 1) Mag-pontok tobb (seed, plateCount) kombinaciora.
    seed_vectors = []
    combos = [
        (0xA7C944210000, 12), (0x1, 6), (0xDEADBEEF, 30),
        (0xFFFFFFFFFFFFFFFF, 8), (0x0, 1),
    ]
    for ws, pc in combos:
        pts = generate_plate_seeds(ws, pc)
        seed_vectors.append({
            "worldSeed": ws, "plateCount": pc,
            "seeds": [list(p) for p in pts],
        })

    # 2) Hozzarendeles-tesztek: determinisztikus mintavetelezett pozíciók +
    # a hozzajuk tartozo plateId, egy rogzitett (seed, plateCount) mellett.
    assign_vectors = []
    fixed_seeds = generate_plate_seeds(world_seed, plate_count)
    gen_seed = 0xB17E00000000CAFE
    for i in range(500):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 6, 0], 20)
        face = p[0] % 6
        u = p[1] % n
        v = p[2] % n
        pos = position_from_tile(face, level, u, v)
        plate_id = assign_plate(pos, fixed_seeds)
        assign_vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "plateId": plate_id,
        })

    with open("plate_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count,
            "seedVectors": seed_vectors, "assignVectors": assign_vectors,
        }, f, indent=1)
    print(f"\n{len(seed_vectors)} mag-kombinacio + {len(assign_vectors)} "
          f"hozzarendeles-vektor generalva")
