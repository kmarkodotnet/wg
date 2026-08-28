"""
Kereg-tipus es alap-elevacio referencia-implementacioja M4-hez
(docs/05-milestones.md §4.2).

HATOKOR (tudatosan szukitve, dokumentalt egyszerusites - NEM architekturalis
dontes/ND, csak egy kesobb finomithato reszlet):
  - A kereg-tipus PLATE-szinten van (a spec §14.1 Plate struct-ja is így
    modellezi: egy plate egyetlen CrustType mezovel rendelkezik).
  - Az "F fraktal reszlet" (§13.2) helyett EGYSZERU, tile-onkent FUGGETLEN
    (feher zaj-szeru) magassag-jitter - NEM terben koherens fBm/Perlin.
    A makro-szerkezetet (kontinensek/oceanok) igy is a plate-szintu
    kereg-tipus adja, ami mar terben koherens (a Voronoi-regiok nagyok).
    Valodi koherens zaj kesobbi finomitas, ha a vizualis minoseg
    megkoveteli.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math
from threefry_ref import threefry4x64

M64 = (1 << 64) - 1
SCALE53 = 2.0 ** -53

DOMAIN_TECTONICS = 2
DOMAIN_TERRAIN = 1
PROPERTY_CRUST_TYPE = 13  # uj RandomProperty - Tectonics domain
PROPERTY_NOISE_GRADIENT = 2  # mar letezo - Terrain domain

# Fold-szeru bazisertekek meterben (csak illusztraciohoz - a vegso
# skalazas majd a tengerszint-kalibracional dol el, §4.4).
OCEANIC_BASE_M = -4000.0
CONTINENTAL_BASE_M = 800.0
NOISE_AMPLITUDE_M = 500.0
OCEANIC_PROBABILITY = 0.55  # kb. Fold-szeru arany a lemezek kozott


def _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index):
    key = [
        world_seed & M64,
        ((domain_id & 0xFFFFFFFF) << 32) | (property_id & 0xFFFFFFFF),
        1,
        0,
    ]
    ctr = [spatial_id & M64, time_bucket & M64, sample_index & M64, 0]
    return threefry4x64(ctr, key, 20)


def sample(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index=0):
    x0 = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index)[0]
    return (x0 >> 11) * SCALE53


def is_oceanic(world_seed, plate_id, oceanic_probability=OCEANIC_PROBABILITY):
    """Kereg-tipus lemezenkent - determinisztikus Bernoulli-proba."""
    return sample(world_seed, DOMAIN_TECTONICS, plate_id, 0, PROPERTY_CRUST_TYPE) < oceanic_probability


def tile_noise_jitter(world_seed, tile_id_value):
    """[-1,1) fuggetlen "jitter" tile-onkent - NEM terben koherens (ld. modul docstring)."""
    v = sample(world_seed, DOMAIN_TERRAIN, tile_id_value, 0, PROPERTY_NOISE_GRADIENT)
    return 2.0 * v - 1.0


def base_elevation(world_seed, plate_id, tile_id_value, oceanic_probability=OCEANIC_PROBABILITY):
    """A tile alap-magassaga meterben: kereg-tipus bazis + jitter."""
    oceanic = is_oceanic(world_seed, plate_id, oceanic_probability)
    base = OCEANIC_BASE_M if oceanic else CONTINENTAL_BASE_M
    jitter = tile_noise_jitter(world_seed, tile_id_value)
    return base + jitter * NOISE_AMPLITUDE_M, oceanic


if __name__ == "__main__":
    import json
    from plate_ref import generate_plate_seeds, assign_plate
    from morton_ref import tile_id
    from sphere_position_ref import position_from_tile

    world_seed = 0xA7C944210000
    plate_count = 12
    level = 6

    seeds = generate_plate_seeds(world_seed, plate_count)
    oceanic_flags = [is_oceanic(world_seed, i) for i in range(plate_count)]
    n_oceanic = sum(oceanic_flags)
    print(f"{plate_count} lemez, {n_oceanic} oceani, {plate_count - n_oceanic} kontinentalis\n")
    for i, oc in enumerate(oceanic_flags):
        print(f"  lemez {i:2d}: {'oceani' if oc else 'kontinentalis'}")

    # Determinizmus-ellenorzes
    oceanic_flags2 = [is_oceanic(world_seed, i) for i in range(plate_count)]
    assert oceanic_flags == oceanic_flags2, "A kereg-tipus nem tiszta fuggveny!"
    print("\nOK - kereg-tipus determinisztikus")

    # Elevacio-eloszlas mintavetel egy level-6 racson
    n = 1 << level
    elevations = []
    for face in range(6):
        for u in range(n):
            for v in range(n):
                pos = position_from_tile(face, level, u, v)
                plate_id = assign_plate(pos, seeds)
                tid = tile_id(face, level, u, v)
                elev, _ = base_elevation(world_seed, plate_id, tid)
                elevations.append(elev)

    elevations.sort()
    total = len(elevations)
    mean_elev = sum(elevations) / total
    print(f"\n{total} tile elevacio-mintaja:")
    print(f"  min={elevations[0]:.1f}m  max={elevations[-1]:.1f}m  atlag={mean_elev:.1f}m")

    # Plauzibilitas: az elevacio erosen bimodalis kell legyen (oceani/kontinentalis
    # ket kulon "kupac" a hisztogramban, nem egyetlen sima Gauss-eloszlas).
    below_zero = sum(1 for e in elevations if e < 0)
    print(f"  0 alatt: {100.0 * below_zero / total:.1f}%  (nyers, tengerszint-kalibracio elott)")

    with open("crust_elevation_vectors.json", "w", newline="\n") as f:
        vectors = []
        gen_seed = 0xC0DE00000000BEEF
        for i in range(400):
            p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 7, 0], 20)
            face = p[0] % 6
            u = p[1] % n
            v = p[2] % n
            pos = position_from_tile(face, level, u, v)
            plate_id = assign_plate(pos, seeds)
            tid = tile_id(face, level, u, v)
            elev, oceanic = base_elevation(world_seed, plate_id, tid)
            vectors.append({
                "face": face, "level": level, "u": u, "v": v,
                "plateId": plate_id, "elevation": elev, "isOceanic": oceanic,
            })
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count,
            "oceanicFlags": oceanic_flags, "vectors": vectors,
        }, f, indent=1)
    print(f"\n400 elevacio-tesztvektor generalva")
