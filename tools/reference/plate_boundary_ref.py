"""
Lemezhatar-hatas (hegyseg-proxy) referencia-implementacioja M4-hez
(docs/05-milestones.md §4.3).

HATOKOR (dokumentalt egyszerusites - a lemezek NEM mozognak M4-ben, ld.
§4 hatokor-szakasz, ezert nincs valodi konvergens/divergens/transform
megkulonbozetes - az M10 "Deep time" adja majd hozza a sebesseg-adatot,
ami ezt lehetove tenne). Helyette EGYSEGES "hatar-kozeli kiemelkedes":
minel kozelebb van egy tile a ket lemez kozotti hatarhoz, annal nagyobb
az uplift-bonusz - ez fizikailag durva kozelites (a valosagban a
divergens hatarok SULLYEDNEK, nem emelkednek), de a cel itt csak az
M4 vizualis/szamszeru elfogadasi kriteriuma (TEST-EARTH-001), nem a
teljes tektonikai realizmus.

MODSZER: a "tavolsag a hatartol" a ket legkozelebbi lemez-mag dot-
product-jainak KULONBSEGEBOL (gap) jon - nincs explicit Voronoi-el
konstrukcio, nincs transzcendens fuggveny (nincs Sin/Cos/Acos), tehat
ez BITPONTOS marad minden platformon (nem kell ND-kockazatot vallalni,
szemben a csillagaszati modullal, ld. ND-26).

Kalibracio (level 6, 12 lemez, world_seed=0xA7C944210000, minden
2. tile mintavetelezve, 6144 minta): a gap-percentilisek
5%=0.01165, 10%=0.02342, 20%=0.04773, 50%=0.14220 - a GAP_SCALE=0.04
kb. a legkozelebbi ~15-18%-ot erinti, fizikailag plauzibilis (a
hegyvidek/orogen ovezetek a szarazfold kisebbik reszet teszik ki).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
from crust_elevation_ref import base_elevation

GAP_SCALE = 0.04
UPLIFT_MAX_M = 1500.0


def two_best_dots(position, seeds):
    """A ket legnagyobb dot-product egy pozicio es a lemez-magok kozott."""
    x, y, z = position
    best, second = -2.0, -2.0
    for sx, sy, sz in seeds:
        d = x * sx + y * sy + z * sz
        if d > best:
            second = best
            best = d
        elif d > second:
            second = d
    return best, second


def boundary_uplift(position, seeds, gap_scale=GAP_SCALE, uplift_max=UPLIFT_MAX_M):
    """Hatar-kozeli kiemelkedes-bonusz: minel kisebb a gap, annal nagyobb."""
    best, second = two_best_dots(position, seeds)
    gap = best - second
    if gap >= gap_scale:
        return 0.0
    return uplift_max * (1.0 - gap / gap_scale)


def elevation_with_boundary(world_seed, plate_id, tile_id_value, position, seeds,
                             gap_scale=GAP_SCALE, uplift_max=UPLIFT_MAX_M):
    base, oceanic = base_elevation(world_seed, plate_id, tile_id_value)
    uplift = boundary_uplift(position, seeds, gap_scale, uplift_max)
    return base + uplift, oceanic


if __name__ == "__main__":
    import json
    from plate_ref import generate_plate_seeds, assign_plate
    from morton_ref import tile_id
    from sphere_position_ref import position_from_tile
    from threefry_ref import threefry4x64

    world_seed = 0xA7C944210000
    plate_count = 12
    level = 6
    seeds = generate_plate_seeds(world_seed, plate_count)
    n = 1 << level

    # Plauzibilitas: hany tile kap nemnulla uplift-ot, es mekkora az atlagos/max ertek
    affected = 0
    total = 0
    max_uplift = 0.0
    sum_uplift = 0.0
    for face in range(6):
        for u in range(0, n, 2):
            for v in range(0, n, 2):
                pos = position_from_tile(face, level, u, v)
                up = boundary_uplift(pos, seeds)
                total += 1
                if up > 0:
                    affected += 1
                    sum_uplift += up
                    max_uplift = max(max_uplift, up)

    print(f"Erintett tile-ok (nemnulla uplift): {100.0*affected/total:.1f}% ({affected}/{total})")
    print(f"Max uplift: {max_uplift:.1f}m, atlag (erintetteken): {sum_uplift/affected:.1f}m")
    assert 0.05 < affected / total < 0.35, "A hatar-hatas zonaja tul szuk vagy tul szeles"
    assert abs(max_uplift - UPLIFT_MAX_M) < 1.0, "A max uplift-nak kb UPLIFT_MAX_M-nek kell lennie a hataron"
    print("OK - a lemezhatar-hatas zonaja plauzibilis meretu\n")

    # Determinizmus
    up1 = boundary_uplift((0.5, 0.5, 0.7071), seeds)
    up2 = boundary_uplift((0.5, 0.5, 0.7071), seeds)
    assert up1 == up2, "Nem tiszta fuggveny!"
    print("OK - determinisztikus (tiszta függvény)")

    # Tesztvektorok a C# porthoz
    vectors = []
    gen_seed = 0xB0DE00000000FACE
    for i in range(400):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 8, 0], 20)
        face = p[0] % 6
        u = p[1] % n
        v = p[2] % n
        pos = position_from_tile(face, level, u, v)
        plate_id = assign_plate(pos, seeds)
        tid = tile_id(face, level, u, v)
        elev, oceanic = elevation_with_boundary(world_seed, plate_id, tid, pos, seeds)
        vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "plateId": plate_id, "elevation": elev, "isOceanic": oceanic,
        })

    with open("plate_boundary_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count,
            "gapScale": GAP_SCALE, "upliftMax": UPLIFT_MAX_M,
            "vectors": vectors,
        }, f, indent=1)
    print(f"\n{len(vectors)} hatarhatas-tesztvektor generalva")
