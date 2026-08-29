"""
Kereg-tipus es alap-elevacio referencia-implementacioja M4-hez
(docs/05-milestones.md §4.2).

HATOKOR (dokumentalt egyszerusites - NEM architekturalis dontes/ND, csak
egy kesobb finomithato reszlet):
  - A kereg-tipus PLATE-szinten van (a spec §14.1 Plate struct-ja is így
    modellezi: egy plate egyetlen CrustType mezovel rendelkezik).

ND-31 (docs/04-decisions.md): az "F fraktal reszlet" (§13.2) korabban
EGYSZERU, tile-onkent FUGGETLEN (feher zaj-szeru) magassag-jitter volt -
NEM terben koherens fBm/Perlin, dokumentalt, ismert hianyossag ("tul
szabalyos" Voronoi-cella hatarok).

ND-33 (docs/04-decisions.md): a sima fBm (ND-31) tovabbra is tul
simanak/eszrevehetetlennek bizonyult vizualisan, meg 4x amplitudoval is
(felhasznaloi visszajelzes: "katasztrofa"). Oka: a sima fBm ELEVE
lekerekitett dombokat ad, nem eles kontraszt. MOST "ridged multifractal"
(spec Sz.13.1 nevesitve is emliti) - minden oktavnal (1-|zaj|)^2, ami
eles gerinceket ad, sokkal nagyobb vizualis kontraszttal.

ND-34 (docs/04-decisions.md): tovabbi felhasznaloi visszajelzes - (a) az
ocean-fenek is tul erosen "hegyes" lett, pedig a valosagban az oceani
relief alapvetoen szelidebb a kontinentalisnal (kiveve a specifikusan
nem modellezett kozeposceani hatak/arkok); (b) a durvasag EGYENLETES
volt a szarazfoldon, holott realisztikusabb, ha van sik/fennsik ES
hegyvidek is, nem mindenhol ugyanannyira "zajos" a felszin. Ket
valtoztatas:
  1. Oceani tile-ok a zaj OCEANIC_NOISE_FACTOR-szorosat kapjak csak.
  2. Egy KULON, ALACSONY FREKVENCIAS "hegyvidekiseg" maszk (sima fBm,
     nem ridged) hatarozza meg REGIONALISAN, hogy a ridged reszlet
     mennyire ervenyesuljon - igy nagy teruletek maradhatnak simak,
     mig masok dramatikusan durvak, nem egyenletes texturat kapva.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
from threefry_ref import threefry4x64
from noise_ref import ridged_multifractal, fbm

M64 = (1 << 64) - 1
SCALE53 = 2.0 ** -53

DOMAIN_TECTONICS = 2
PROPERTY_CRUST_TYPE = 13  # uj RandomProperty - Tectonics domain

# Fold-szeru bazisertekek meterben (csak illusztraciohoz - a vegso
# skalazas majd a tengerszint-kalibracional dol el, §4.4).
OCEANIC_BASE_M = -4000.0
CONTINENTAL_BASE_M = 800.0
NOISE_AMPLITUDE_M = 3000.0  # ND-33: tovabb emelve (2000->3000), ridged
# multifractalra valtva a sima fBm helyett a nagyobb vizualis kontrasztert
OCEANIC_NOISE_FACTOR = 0.25  # ND-34: az ocean-fenek szelidebb, mint a szarazfold
MOUNTAIN_MASK_FREQUENCY = 2.5  # ND-34: alacsony frekvencia -> nagy, regionalis zonak
MOUNTAIN_MASK_OCTAVES = 3
MOUNTAIN_MASK_GAIN = 1.3  # a maszk fBm nyers tartomanyat [0,1]-hez kozelebb nyujtja
MOUNTAIN_MASK_BIAS_POWER = 1.5  # >1: tobbnyire sik, ritkabban dramatikusan durva
OCEANIC_PROBABILITY = 0.55  # kb. Fold-szeru arany a lemezek kozott


def _clamp01(v):
    return 0.0 if v < 0.0 else (1.0 if v > 1.0 else v)


def mountain_mask(world_seed, position):
    """[0,1] regionalis "hegyvidekieseg" - alacsony frekvencias, sima fBm
    (NEM ridged), hogy nagy, osszefuggo zonakat adjon sik/durva teruletekre."""
    x, y, z = position
    m = fbm(world_seed, x, y, z, base_frequency=MOUNTAIN_MASK_FREQUENCY, octaves=MOUNTAIN_MASK_OCTAVES)
    normalized = _clamp01(m * MOUNTAIN_MASK_GAIN + 0.5)
    return normalized ** MOUNTAIN_MASK_BIAS_POWER


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


def base_elevation(world_seed, plate_id, position, oceanic_probability=OCEANIC_PROBABILITY):
    """A tile alap-magassaga meterben: kereg-tipus bazis + terben koherens ridged zaj."""
    oceanic = is_oceanic(world_seed, plate_id, oceanic_probability)
    base = OCEANIC_BASE_M if oceanic else CONTINENTAL_BASE_M
    x, y, z = position
    # ridged_multifractal kb. [0,1]-hez kozeli, atlagosan ~0.7 korul -
    # (r-0.5)*2 -al [-1,1]-hez kozeli, ELOJELES modositova alakitva, hogy
    # tovabbra is ugy hasson, mint egy szimmetrikus magassag-perturbacio
    # (nem csak felfele told).
    r = ridged_multifractal(world_seed, x, y, z)
    noise = (r - 0.5) * 2.0
    mask = mountain_mask(world_seed, position)
    amplitude = NOISE_AMPLITUDE_M * (OCEANIC_NOISE_FACTOR if oceanic else 1.0)
    return base + noise * mask * amplitude, oceanic


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
                elev, _ = base_elevation(world_seed, plate_id, pos)
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
            elev, oceanic = base_elevation(world_seed, plate_id, pos)
            vectors.append({
                "face": face, "level": level, "u": u, "v": v,
                "plateId": plate_id, "elevation": elev, "isOceanic": oceanic,
            })
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count,
            "oceanicFlags": oceanic_flags, "vectors": vectors,
        }, f, indent=1)
    print(f"\n400 elevacio-tesztvektor generalva")
