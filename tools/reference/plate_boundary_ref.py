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
from crust_elevation_ref import blended_base_elevation, is_oceanic, mountain_mask
from domain_warp_ref import warp_position

GAP_SCALE = 0.04
UPLIFT_MAX_M = 1000.0  # ND-90: a felgyurodes kulon komponense legfeljebb 1 km
OCEANIC_OCEANIC_UPLIFT_FACTOR = 0.15  # ND-32: oceani-oceani hataron csokkentett uplift
# ND-35: felhasznaloi visszajelzes - a parti sav MINDIG maximalis
# kiemelkedest kapott (az ocean-kontinens hatar is lemezhatar), irrealisan
# magas "falat" huzva a tengerszint fole szinte minden parton. Az uplift
# MOST MAR a domborzat fraktal-reszletevel megegyezo regionalis maszkkal
# van szorozva - sik regioban a parti sav sem kap maximalis kiemelkedest.


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


def two_best_dots_with_indices(position, seeds):
    """Ugyanaz, mint two_best_dots, de a ket legkozelebbi lemez INDEXET is
    visszaadja (kereg-tipus lekerdezesehez, ND-32)."""
    x, y, z = position
    best, second = -2.0, -2.0
    best_index, second_index = -1, -1
    for i, (sx, sy, sz) in enumerate(seeds):
        d = x * sx + y * sy + z * sz
        if d > best:
            second, second_index = best, best_index
            best, best_index = d, i
        elif d > second:
            second, second_index = d, i
    return best, second, best_index, second_index


def boundary_uplift(world_seed, position, seeds, gap_scale=GAP_SCALE, uplift_max=UPLIFT_MAX_M,
                     oceanic_oceanic_factor=OCEANIC_OCEANIC_UPLIFT_FACTOR):
    """Hatar-kozeli kiemelkedes-bonusz: minel kisebb a gap, annal nagyobb.
    Oceani-oceani hataron oceanic_oceanic_factor-ral csokkentve (ND-32) -
    a valosagban ott vulkani szigetivek epulnek, nem kontinentalis-utkozes
    lepteku hegylancok. A regionalis hegyvidekiseg-maszkkal is szorozva
    (ND-35) - sik regioban a parti sav sem kap maximalis kiemelkedest.

    A hatar-KOZELSEG (gap) szamitasa a WARPOLT pozicion tortenik
    (domain_warp_ref.warp_position) - igy maga a hatarvonal (es az uplift-
    zona) is organikusan hullamzik, nem a nyers Voronoi-cellak eles,
    nagykor-iv-szeru hatara menten fut. A mountain_mask viszont
    SZANDEKOSAN a NYERS position-t kapja: az egy regionalis
    hegyvidekiseg-textura-maszk, a terep-reszlet resze, nem a lemez-
    topologia - nem indokolt ugyanazzal a warp-torzitassal osszekotni."""
    warped_position = warp_position(world_seed, position)
    best, second, best_idx, second_idx = two_best_dots_with_indices(warped_position, seeds)
    gap = best - second
    if gap >= gap_scale:
        return 0.0
    raw_uplift = uplift_max * (1.0 - gap / gap_scale)

    best_oceanic = is_oceanic(world_seed, best_idx)
    second_oceanic = second_idx >= 0 and is_oceanic(world_seed, second_idx)
    if best_oceanic and second_oceanic:
        raw_uplift *= oceanic_oceanic_factor

    mask = mountain_mask(world_seed, position)
    return raw_uplift * mask


def elevation_with_boundary(world_seed, plate_id, tile_id_value, position, seeds,
                             gap_scale=GAP_SCALE, uplift_max=UPLIFT_MAX_M):
    warped_position = warp_position(world_seed, position)
    best, second, best_idx, second_idx = two_best_dots_with_indices(warped_position, seeds)
    base, oceanic = blended_base_elevation(
        world_seed, best, second, best_idx, second_idx, position)
    uplift = boundary_uplift(world_seed, position, seeds, gap_scale, uplift_max)
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
                up = boundary_uplift(world_seed, pos, seeds)
                total += 1
                if up > 0:
                    affected += 1
                    sum_uplift += up
                    max_uplift = max(max_uplift, up)

    print(f"Erintett tile-ok (nemnulla uplift): {100.0*affected/total:.1f}% ({affected}/{total})")
    print(f"Max uplift: {max_uplift:.1f}m, atlag (erintetteken): {sum_uplift/affected:.1f}m")
    assert 0.05 < affected / total < 0.35, "A hatar-hatas zonaja tul szuk vagy tul szeles"
    # ND-35 ota az uplift a regionalis hegyvidekieseg-maszkkal is szorozva
    # van, ezert a max ertek NEM feltetlenul eri el UPLIFT_MAX_M-et - csak
    # azt varjuk el, hogy sose legyen annal nagyobb.
    assert max_uplift <= UPLIFT_MAX_M + 1.0, "A max uplift nem lehet nagyobb a plafonertekenel"
    print("OK - a lemezhatar-hatas zonaja plauzibilis meretu\n")

    # Determinizmus
    up1 = boundary_uplift(world_seed, (0.5, 0.5, 0.7071), seeds)
    up2 = boundary_uplift(world_seed, (0.5, 0.5, 0.7071), seeds)
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
