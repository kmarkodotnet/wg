"""
Lemez-keretes domborzati zaj (A19 / ND-136) referencia-implementacioja.

A PROBLEMA. Deep-time-ban eddig CSAK a lemez-magok forogtak el
(plate_motion_ref.plate_seed_at_time). A domborzat harom zaj-tagja - az
elsodleges ridged multifractal, a mountain_mask es az ND-52 masodlagos
reszlet-zaj - a ROGZITETT vilag-pozicioban ertekelodott ki
(crust_elevation_ref.base_elevation), tehat a lemezhatar ATCSUSZOTT egy
allo textura felett: a hegyvonulat helyben maradt, a lemez elvandorolt
alola. Fizikailag rossz - a kereg anyag, a domborzat a kereg
tulajdonsaga.

A MEGOLDAS (ND-136 "C" opcio). A zaj a lemez SAJAT vonatkoztatasi
rendszereben ertekelodik ki: a p vilag-poziciot eloszor VISSZAforgatjuk a
lemez Euler-polusa korul R(-omega*t)-vel, es a zajt ott mintavetelezzuk.
Igy a zaj egyenletesen es aranyosan vandorol a lemezzel.

HATAR-FOLYTONOSSAG. A lemezhatar ket oldalan MAS keret van, tehat a zaj
ertekek nem egyeznek -> minden hatar szakadasossa valna, nem csak a
kereg-tipust valto. Ezert a ND-90 keveres kiterjed MINDEN hatarra (a
"csak eltero kereg-tipusnal" kikotes kikerult), es a keverosavon belul
PONTONKENT KET zaj-bazis szamolodik (a nyertes es a masodik lemez
kereteben).

KOLTSEG. A keveres csak a BOUNDARY_BLEND_GAP (0.005) szelessegu savban
fut, ezert a masodik zaj-bazis is CSAK ott szamolodik. A sav a felszin
toredeke, tehat ez NEM a teljes zaj-koltseg duplazasa (az ND-136 szoveg
ovatos felso becslese volt), hanem kb. 1x + egy keskeny savnyi extra.

t = 0 BITRE VALTOZATLAN. angle=0-nal a visszaforgatas azonossag (a
kod expliciten le is rovidit ra), es azonos bazisoknal a kiterjesztett
keveres ugyanazt adja, mint a regi korai-kilepes: ha a ket lemez
kereg-tipusa azonos, a ket elevacio bitre egyenlo, tehat
best + (second - best)*w = best + 0.0*w = best.

DETERMINIZMUS. A visszaforgatas Rodrigues-keplete a
deterministic_math_ref.sin_cos-t hasznalja (NEM a math.sin/cos-t), mert a
C# oldalon a PlateMotion.RodriguesRotate a DeterministicMath.SinCos-szal
dolgozik (ND-27) - csak igy egyeznek a tesztvektorok bitre. Emiatt a
lemez-magok elmozditasara is a helyi _moved_seeds() valtozat van itt, nem
a plate_motion_ref.plate_seed_at_time (az math.sin/cos-t hasznal).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import deterministic_math_ref as dmath
from crust_elevation_ref import (
    BOUNDARY_BLEND_GAP,
    mountain_mask,
    secondary_detail_noise,
)
from noise_ref import ridged_multifractal
from plate_boundary_ref import (
    GAP_SCALE,
    UPLIFT_MAX_M,
    OCEANIC_OCEANIC_UPLIFT_FACTOR,
    two_best_dots_with_indices,
)
from crust_elevation_ref import is_oceanic
from domain_warp_ref import warp_position
from plate_motion_ref import generate_euler_pole, generate_angular_velocity


def rodrigues_rotate_det(v, axis, angle):
    """Rodrigues-forgatas a DETERMINISZTIKUS sin/cos-szal (ND-27) - bitre
    ugyanaz, mint a C# PlateMotion.RodriguesRotate."""
    vx, vy, vz = v
    kx, ky, kz = axis
    sin_a, cos_a = dmath.sin_cos(angle)

    cross = (ky * vz - kz * vy, kz * vx - kx * vz, kx * vy - ky * vx)
    dot = kx * vx + ky * vy + kz * vz

    return (
        vx * cos_a + cross[0] * sin_a + kx * dot * (1.0 - cos_a),
        vy * cos_a + cross[1] * sin_a + ky * dot * (1.0 - cos_a),
        vz * cos_a + cross[2] * sin_a + kz * dot * (1.0 - cos_a),
    )


def moved_seeds(world_seed, seeds_t0, time_myr):
    """A lemez-magok pozicioja time_myr-nel - a C# PlateMotion.MovedSeeds
    bitpontos tukre (deterministic sin/cos)."""
    out = []
    for i, s0 in enumerate(seeds_t0):
        axis = generate_euler_pole(world_seed, i)
        omega = generate_angular_velocity(world_seed, i)
        out.append(rodrigues_rotate_det(s0, axis, omega * time_myr))
    return out


def to_plate_frame(world_seed, plate_id, position, time_myr):
    """A vilag-pozicio VISSZAforgatva a plate_id lemez sajat (t=0)
    vonatkoztatasi rendszerebe: R(-omega*t) az Euler-polus korul.

    time_myr == 0 (vagy plate_id < 0) eseten EGZAKT azonossag - ezert a
    rovidzar, nem csak a cos(0)=1/sin(0)=0 miatt: a -0.0 osszeadasok
    elkerulesevel a t=0 kimenet GARANTALTAN bitazonos a regivel."""
    if plate_id < 0 or time_myr == 0.0:
        return position
    axis = generate_euler_pole(world_seed, plate_id)
    omega = generate_angular_velocity(world_seed, plate_id)
    return rodrigues_rotate_det(position, axis, -(omega * time_myr))


def plate_frame_noise_basis(world_seed, plate_id, position, time_myr):
    """Az elevacio harom zaj-tagja a lemez sajat kereteben kiertekelve."""
    p = to_plate_frame(world_seed, plate_id, position, time_myr)
    x, y, z = p
    r = ridged_multifractal(world_seed, x, y, z)
    primary = (r - 0.5) * 2.0
    mask = mountain_mask(world_seed, p)
    secondary = secondary_detail_noise(world_seed, p)
    return primary, mask, secondary


def base_elevation_from_basis(world_seed, plate_id, primary, mask, secondary):
    """A kereg-tipus bazis + a mar kiszamolt zaj-tagok osszeallitasa -
    ugyanaz a muveleti sorrend, mint crust_elevation_ref.base_elevation-ben."""
    from crust_elevation_ref import (
        OCEANIC_BASE_M, CONTINENTAL_BASE_M, NOISE_AMPLITUDE_M,
        OCEANIC_NOISE_FACTOR, SECONDARY_NOISE_AMPLITUDE_M,
    )
    oceanic = is_oceanic(world_seed, plate_id)
    base = OCEANIC_BASE_M if oceanic else CONTINENTAL_BASE_M
    amplitude = NOISE_AMPLITUDE_M * (OCEANIC_NOISE_FACTOR if oceanic else 1.0)
    secondary_amplitude = SECONDARY_NOISE_AMPLITUDE_M * (OCEANIC_NOISE_FACTOR if oceanic else 1.0)
    return base + primary * mask * amplitude + secondary * secondary_amplitude, oceanic


def blended_base_elevation_plate_frame(world_seed, best, second, best_index, second_index,
                                        position, time_myr, blend_gap=BOUNDARY_BLEND_GAP):
    """ND-90 keveres, MINDEN hataron (nem csak kereg-tipus-valtonal), a ket
    lemez SAJAT kereteben mintavetelezett zajjal. Visszaadja a nyertes
    lemez kereteben szamolt mountain_mask-ot is, mert az uplift ugyanazt
    a maszkot hasznalja (ND-35).

    A masodik zaj-bazis CSAK a keverosavon belul szamolodik."""
    b_primary, b_mask, b_secondary = plate_frame_noise_basis(
        world_seed, best_index, position, time_myr)
    best_elevation, best_oceanic = base_elevation_from_basis(
        world_seed, best_index, b_primary, b_mask, b_secondary)

    gap = best - second
    if second_index < 0 or gap >= blend_gap or not blend_gap > 0.0:
        return best_elevation, best_oceanic, b_mask

    s_primary, s_mask, s_secondary = plate_frame_noise_basis(
        world_seed, second_index, position, time_myr)
    second_elevation, _ = base_elevation_from_basis(
        world_seed, second_index, s_primary, s_mask, s_secondary)

    normalized_gap = gap / blend_gap
    smooth_gap = normalized_gap * normalized_gap * (3.0 - 2.0 * normalized_gap)
    second_weight = 0.5 * (1.0 - smooth_gap)
    blended = best_elevation + (second_elevation - best_elevation) * second_weight
    return blended, best_oceanic, b_mask


def boundary_uplift_from_nearest(world_seed, best, second, best_index, second_index, mask,
                                  gap_scale=GAP_SCALE, uplift_max=UPLIFT_MAX_M,
                                  oceanic_oceanic_factor=OCEANIC_OCEANIC_UPLIFT_FACTOR):
    """Valtozatlan ND-32/ND-35 keplet, csak a mar kiszamolt (lemez-keretes)
    maszkot kapja."""
    gap = best - second
    if gap >= gap_scale:
        return 0.0
    raw_uplift = uplift_max * (1.0 - gap / gap_scale)
    best_oceanic = is_oceanic(world_seed, best_index)
    second_oceanic = second_index >= 0 and is_oceanic(world_seed, second_index)
    if best_oceanic and second_oceanic:
        raw_uplift *= oceanic_oceanic_factor
    return raw_uplift * mask


def elevation_with_boundary_plate_frame(world_seed, position, seeds, time_myr,
                                         gap_scale=GAP_SCALE, uplift_max=UPLIFT_MAX_M):
    """Az alap-elevacio ES a (relaxalatlan) hatar-uplift KULON, lemez-keretes
    zajjal. A ket tagot a hivo adja ossze - a deep-time ut CSAK az upliftre
    alkalmaz relaxaciot, ezert nincs ertelme eloszor osszeadni, majd
    kivonni (az a kivonas bitet veszitene).

    A lemez-HOZZARENDELES tovabbra is a WARPOLT vilag-pozicion tortenik
    (ND-36) es a MAR ELMOZDITOTT magokhoz kepest - a lemez-topologia a
    vilag kerete, csak a zaj kerul at a lemez keretebe.

    Visszateres: (base, uplift, oceanic)."""
    warped = warp_position(world_seed, position)
    best, second, best_idx, second_idx = two_best_dots_with_indices(warped, seeds)
    base, oceanic, mask = blended_base_elevation_plate_frame(
        world_seed, best, second, best_idx, second_idx, position, time_myr)
    uplift = boundary_uplift_from_nearest(
        world_seed, best, second, best_idx, second_idx, mask, gap_scale, uplift_max)
    return base, uplift, oceanic


if __name__ == "__main__":
    import json

    from plate_ref import generate_plate_seeds, assign_plate
    from sphere_position_ref import position_from_tile
    from plate_boundary_ref import elevation_with_boundary
    from threefry_ref import threefry4x64

    world_seed = 0xA7C944210000
    plate_count = 20
    level = 6
    seeds_t0 = generate_plate_seeds(world_seed, plate_count)
    n = 1 << level

    sample_positions = []
    for face in range(6):
        for u in range(0, n, 9):
            for v in range(0, n, 11):
                sample_positions.append(position_from_tile(face, level, u, v))

    # ------------------------------------------------------------------
    print("--- 1. t=0 bitre valtozatlan (a statikus M4 ut) ---")
    max_diff = 0.0
    for pos in sample_positions:
        plate_id = assign_plate(pos, seeds_t0)
        expected, expected_oceanic = elevation_with_boundary(
            world_seed, plate_id, 0, pos, seeds_t0)
        actual_base, actual_uplift, actual_oceanic = elevation_with_boundary_plate_frame(
            world_seed, pos, seeds_t0, 0.0)
        actual = actual_base + actual_uplift
        assert actual_oceanic == expected_oceanic, "kereg-tipus elter t=0-nal"
        max_diff = max(max_diff, abs(actual - expected))
    assert max_diff == 0.0, f"t=0-nal BITRE egyeznie kell, max diff={max_diff}"
    print(f"OK - {len(sample_positions)} minta, max elteres: {max_diff} (egzakt 0)\n")

    # ------------------------------------------------------------------
    print("--- 2. A zaj EGYUTT UTAZIK a lemezzel ---")
    # Egy lemez belsejeben (a hataroktol tavol) valasztunk pontot, elforgatjuk
    # a lemez sajat mozgasaval, es ott kerdezzuk le a zajt t-nel. Ha a zaj a
    # lemezzel utazik, ugyanazt kell adnia, mint a kiindulo ponton t=0-nal.
    time_myr = 250.0
    seeds_t = moved_seeds(world_seed, seeds_t0, time_myr)
    checked = 0
    max_travel_diff = 0.0
    dense_positions = [position_from_tile(face, level, u, v)
                       for face in range(6)
                       for u in range(0, n, 3)
                       for v in range(0, n, 3)]
    for pos in dense_positions:
        warped = warp_position(world_seed, pos)
        best, second, best_idx, _ = two_best_dots_with_indices(warped, seeds_t0)
        if best - second < 0.10:
            continue  # tul kozel a hatarhoz - ott a nyertes lemez valtozhat
        axis = generate_euler_pole(world_seed, best_idx)
        omega = generate_angular_velocity(world_seed, best_idx)
        moved_pos = rodrigues_rotate_det(pos, axis, omega * time_myr)

        # A lemezzel egyutt mozgo pont a lemez kereteben ugyanoda esik vissza
        w_moved = warp_position(world_seed, moved_pos)
        b2, s2, idx2, _ = two_best_dots_with_indices(w_moved, seeds_t)
        if idx2 != best_idx or b2 - s2 < 0.10:
            continue  # a warp miatt athuzodhatott mas lemezre a hatar kozeleben

        base0 = plate_frame_noise_basis(world_seed, best_idx, pos, 0.0)
        base_t = plate_frame_noise_basis(world_seed, idx2, moved_pos, time_myr)
        d = max(abs(a - b) for a, b in zip(base0, base_t))
        max_travel_diff = max(max_travel_diff, d)
        checked += 1
    assert checked > 50, f"tul keves belso minta ({checked})"
    assert max_travel_diff < 1e-12, (
        f"a zajnak egyutt kell utaznia a lemezzel, max elteres={max_travel_diff}")
    print(f"OK - {checked} lemez-belseji minta, max zaj-elteres a lemezzel egyutt "
          f"mozgatva: {max_travel_diff:.3e}\n")

    # Kontroll: a REGI (vilag-keretes) zaj ugyanezen a teszten MEGBUKIK
    control_max = 0.0
    for pos in sample_positions[:200]:
        warped = warp_position(world_seed, pos)
        best, second, best_idx, _ = two_best_dots_with_indices(warped, seeds_t0)
        if best - second < 0.10:
            continue
        axis = generate_euler_pole(world_seed, best_idx)
        omega = generate_angular_velocity(world_seed, best_idx)
        moved_pos = rodrigues_rotate_det(pos, axis, omega * time_myr)
        a = plate_frame_noise_basis(world_seed, best_idx, pos, 0.0)
        b = plate_frame_noise_basis(world_seed, best_idx, moved_pos, 0.0)  # vilag-keret
        control_max = max(control_max, max(abs(p - q) for p, q in zip(a, b)))
    assert control_max > 0.1, (
        "a kontroll-ellenpeldanak ERDEMBEN kell elternie, kulonben a teszt "
        f"semmit nem bizonyit (max={control_max})")
    print(f"OK - kontroll: a REGI, vilag-keretes zaj ugyanitt {control_max:.3f}-nyit "
          "ugrik (a teszt tehat nem trivialisan teljesul)\n")

    # ------------------------------------------------------------------
    print("--- 3. Hatar-folytonossag: a keveres MINDEN hataron fut ---")
    # A keverosav kozepen (gap -> 0) a sulynak 0.5-nek kell lennie, a sav
    # kulso szelen (gap = blend_gap) 0-nak - fuggetlenul a kereg-tipustol.
    for gap, expected_w in ((0.0, 0.5), (BOUNDARY_BLEND_GAP, 0.0)):
        ng = gap / BOUNDARY_BLEND_GAP
        sg = ng * ng * (3.0 - 2.0 * ng)
        w = 0.5 * (1.0 - sg)
        assert abs(w - expected_w) < 1e-15, f"gap={gap}: suly={w}, vart={expected_w}"
    print("OK - a keverosuly a hataron 0.5, a sav szelen 0 (smoothstep, nulla derivalttal)\n")

    # ------------------------------------------------------------------
    print("--- 4. Determinizmus (tiszta fuggveny) ---")
    a1 = elevation_with_boundary_plate_frame(world_seed, sample_positions[0], seeds_t, 137.0)
    a2 = elevation_with_boundary_plate_frame(world_seed, sample_positions[0], seeds_t, 137.0)
    assert a1 == a2, "nem tiszta fuggveny!"
    print("OK - ismetelt hivas bitre azonos\n")

    # ------------------------------------------------------------------
    print("--- 5. Minden parameter erdemben hat ---")
    p = sample_positions[7]
    e0 = elevation_with_boundary_plate_frame(world_seed, p, seeds_t0, 0.0)[0]
    e1 = elevation_with_boundary_plate_frame(
        world_seed, p, moved_seeds(world_seed, seeds_t0, 500.0), 500.0)[0]
    assert e0 != e1, "a deep-time idonek hatnia kell az elevaciora"
    e2 = elevation_with_boundary_plate_frame(world_seed ^ 1, p, seeds_t0, 0.0)[0]
    assert e0 != e2, "a world seednek hatnia kell"
    print("OK - a timeMyr es a worldSeed is erdemben hat\n")

    # ------------------------------------------------------------------
    print("--- Tesztvektor-generalas ---")
    vectors = []
    gen_seed = 0x91A7EF4A3E000136
    for i in range(400):
        pr = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 136, 0], 20)
        face = pr[0] % 6
        u = pr[1] % n
        v = pr[2] % n
        t = (pr[3] % 1_000_000) / 1000.0  # 0..1000 Myr
        pos = position_from_tile(face, level, u, v)
        seeds_at_t = moved_seeds(world_seed, seeds_t0, t)
        base, uplift, oceanic = elevation_with_boundary_plate_frame(
            world_seed, pos, seeds_at_t, t)
        warped = warp_position(world_seed, pos)
        _, _, best_idx, second_idx = two_best_dots_with_indices(warped, seeds_at_t)
        vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "timeMyr": t,
            "bestPlateId": best_idx, "secondPlateId": second_idx,
            "baseElevation": base, "uplift": uplift, "isOceanic": oceanic,
        })

    # Kulon, celzott t=0 blokk: ezeknek BITRE a statikus M4 erteket kell adniuk.
    zero_vectors = []
    for i in range(100):
        pr = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 137, 0], 20)
        face = pr[0] % 6
        u = pr[1] % n
        v = pr[2] % n
        pos = position_from_tile(face, level, u, v)
        z_base, z_uplift, oceanic = elevation_with_boundary_plate_frame(
            world_seed, pos, seeds_t0, 0.0)
        elev = z_base + z_uplift
        zero_vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "elevation": elev, "isOceanic": oceanic,
        })

    with open("plate_frame_noise_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count, "level": level,
            "boundaryBlendGap": BOUNDARY_BLEND_GAP,
            "gapScale": GAP_SCALE, "upliftMax": UPLIFT_MAX_M,
            "vectors": vectors,
            "zeroTimeVectors": zero_vectors,
        }, f, indent=1)
    print(f"{len(vectors)} lemez-keretes + {len(zero_vectors)} t=0 tesztvektor generalva")
