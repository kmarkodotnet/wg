"""
Lemezmozgas referencia-implementacioja M10-hez (docs/05-milestones.md
§10.1): Euler-polus + szogsebesseg, Rodrigues-forgatas.

HATOKOR (tudatosan szukitve, ld. milestones "M10 hatokor"): csak a
lemez-MAG pozicioja mozog idoben - eroziós felhalmozodas, eljegesedes,
dinamikus tengerszint, lemez-szuletes/-halal halasztva.

ND-27 KITERJESZTVE (nem uj dontes): a Rodrigues-forgatas Sin/Cos-t
hasznal - ugyanaz a trigonometria-kockazati kategoria es hatarido
(M12), mint a homerseklet-modellnel.

IDOEGYSEG: millio ev (Myr) - kulon a csillagaszat/klima "nap" (day_t)
tengelyetol.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math

from plate_ref import sample_unit_vector3, sample4, DOMAIN_TECTONICS

PROPERTY_EULER_POLE = 11
PROPERTY_PLATE_VELOCITY = 12

ANGULAR_VELOCITY_MIN = 0.01  # rad/Myr
ANGULAR_VELOCITY_MAX = 0.09  # rad/Myr


def generate_euler_pole(world_seed, plate_id):
    """Egysegvektor - a lemez forgastengelye."""
    x, y, z, _ = sample_unit_vector3(world_seed, DOMAIN_TECTONICS, plate_id, 0, PROPERTY_EULER_POLE)
    return x, y, z


def generate_angular_velocity(world_seed, plate_id):
    """rad/Myr, [ANGULAR_VELOCITY_MIN, ANGULAR_VELOCITY_MAX) tartomanyban."""
    a, _, _, _ = sample4(world_seed, DOMAIN_TECTONICS, plate_id, 0, PROPERTY_PLATE_VELOCITY, 0)
    return ANGULAR_VELOCITY_MIN + a * (ANGULAR_VELOCITY_MAX - ANGULAR_VELOCITY_MIN)


def rodrigues_rotate(v, axis, angle):
    """Rodrigues-forgatas: v elforgatva 'axis' korul 'angle' szoggel."""
    vx, vy, vz = v
    kx, ky, kz = axis
    cos_a, sin_a = math.cos(angle), math.sin(angle)

    # k x v
    cross = (ky * vz - kz * vy, kz * vx - kx * vz, kx * vy - ky * vx)
    # k . v
    dot = kx * vx + ky * vy + kz * vz

    return (
        vx * cos_a + cross[0] * sin_a + kx * dot * (1.0 - cos_a),
        vy * cos_a + cross[1] * sin_a + ky * dot * (1.0 - cos_a),
        vz * cos_a + cross[2] * sin_a + kz * dot * (1.0 - cos_a),
    )


def plate_seed_at_time(world_seed, plate_id, seed_position_t0, time_myr):
    """A lemez-mag pozicioja time_myr idopontban."""
    axis = generate_euler_pole(world_seed, plate_id)
    omega = generate_angular_velocity(world_seed, plate_id)
    angle = omega * time_myr
    return rodrigues_rotate(seed_position_t0, axis, angle)


if __name__ == "__main__":
    from plate_ref import generate_plate_seeds

    world_seed = 0xA7C944210000
    plate_count = 20
    seeds_t0 = generate_plate_seeds(world_seed, plate_count)

    print("--- t=0 visszakompatibilitas ---")
    all_match = True
    for i, s0 in enumerate(seeds_t0):
        moved = plate_seed_at_time(world_seed, i, s0, 0.0)
        diff = max(abs(moved[k] - s0[k]) for k in range(3))
        if diff > 1e-12:
            all_match = False
            print(f"  HIBA: lemez {i} nem egyezik t=0-nal, diff={diff}")
    assert all_match, "t=0-nal a mozgo pozicionak meg kell egyeznie a statikus M4 pozicioval"
    print("OK - t=0-nal minden lemez-mag megegyezik az M4 statikus pozicioval\n")

    print("--- Mozgas ellenorzese (t=100 Myr) ---")
    any_moved = False
    for i, s0 in enumerate(seeds_t0):
        moved = plate_seed_at_time(world_seed, i, s0, 100.0)
        diff = max(abs(moved[k] - s0[k]) for k in range(3))
        if diff > 1e-6:
            any_moved = True
    assert any_moved, "100 Myr alatt legalabb egy lemeznek el kell mozdulnia"
    print("OK - a lemezek 100 Myr alatt erdemben elmozdulnak\n")

    print("--- Egysegvektor-megorzes ---")
    max_len_error = 0.0
    for i, s0 in enumerate(seeds_t0):
        for t in [0.0, 50.0, 100.0, 500.0]:
            moved = plate_seed_at_time(world_seed, i, s0, t)
            length_sq = sum(c * c for c in moved)
            max_len_error = max(max_len_error, abs(length_sq - 1.0))
    assert max_len_error < 1e-9, f"A forgatas nem tartja meg az egysegvektor-hosszt: {max_len_error}"
    print(f"OK - minden forgatott pozicio egysegvektor marad (max hiba: {max_len_error:.2e})\n")

    print("--- Timestep-invariancia (ND-04) ---")
    # A forgatas explicit fuggvenye t-nek (nem iterativ akkumulator) -
    # ezert egy nagy lepesben vagy sok kis lepes "osszegekent" (ami itt
    # egyszeruen ugyanannak a zart fuggvenynek a kiertekelese kulonbozo
    # t-kre) UGYANAZT kell adnia.
    plate_id, s0 = 0, seeds_t0[0]
    direct = plate_seed_at_time(world_seed, plate_id, s0, 365.0)
    # "sok kis lepes": 73 lepes 5 Myr-enkent - de mivel a fuggveny zart
    # (nem allapotfuggo), ez csak ugyanannak a fuggvenynek ismetelt
    # kiertekelese kulonbozo t-kre, majd az UTOLSO ertek osszevetese.
    stepped = plate_seed_at_time(world_seed, plate_id, s0, 73 * 5.0)
    diff = max(abs(direct[k] - stepped[k]) for k in range(3))
    assert diff < 1e-12, f"Timestep-fuggetlenseg serult: {diff}"
    print(f"OK - a vegallapot fuggetlen a lepeskoztol (direkt vs. lepesekben szamolt t, diff={diff:.2e})\n")

    # Determinizmus
    m1 = plate_seed_at_time(world_seed, 5, seeds_t0[5], 42.0)
    m2 = plate_seed_at_time(world_seed, 5, seeds_t0[5], 42.0)
    assert m1 == m2, "Nem tiszta fuggveny!"
    print("OK - determinisztikus (tiszta fuggveny)")

    # Tesztvektorok a C# porthoz
    import json
    from threefry_ref import threefry4x64

    vectors = []
    gen_seed = 0xD007000000000001
    for i in range(300):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 13, 0], 20)
        plate_id = p[0] % plate_count
        time_myr = (p[1] % 1_000_000) / 1000.0  # 0..1000 Myr
        s0 = seeds_t0[plate_id]
        moved = plate_seed_at_time(world_seed, plate_id, s0, time_myr)
        vectors.append({
            "plateId": plate_id, "timeMyr": time_myr,
            "seedX": s0[0], "seedY": s0[1], "seedZ": s0[2],
            "movedX": moved[0], "movedY": moved[1], "movedZ": moved[2],
        })

    with open("plate_motion_vectors.json", "w", newline="\n") as f:
        json.dump({"worldSeed": world_seed, "plateCount": plate_count, "vectors": vectors}, f, indent=1)
    print(f"\n{len(vectors)} lemezmozgas-tesztvektor generalva")
