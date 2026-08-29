"""
Determinisztikus 3D gradiens-zaj (Perlin-stilusu, "Improving Noise" Ken
Perlin 2002 kvintikus fade-gorbevel) + fBm referencia-implementacioja
(docs/00-spec-v1.0.md Sz.13.2 "F: fractal detail", docs/04-decisions.md
ND-31).

CEL: a CrustElevation.TileNoiseJitter jelenlegi FEHER ZAJA (tile-onkent
FUGGETLEN, terben NEM koherens - ld. CrustElevation.cs osztaly-doc) helyett
valodi, terben koherens fBm - ez tori meg a lemez-Voronoi-cellak "tul
szabalyos" hatarat organikus valtozatossaggal.

MODSZER: a racspont-gradiensek hash-eleset UJRAFELHASZNALJA a mar
verifikalt sample_unit_vector3 elutasitasos mintavetelt (nincs uj,
ellenorizetlen hash-fuggveny) - a racs-koordinatak (ix,iy,iz) es az
oktav-index a MAR HASZNALT (spatial_id, time_bucket) parba csomagolva.

A kvintikus fade-gorbe (6t^5-15t^4+10t^3) es a trilinearis interpolacio
TISZTA POLINOM/aritmetika - nincs uj transzcendens-kockazat (ND-27
osztalya nem bovul).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math

from plate_ref import sample_unit_vector3

DOMAIN_TERRAIN = 1
PROPERTY_NOISE_GRADIENT = 2

M32 = 0xFFFFFFFF


def _pack_lattice(ix, iy, iz, octave):
    spatial_id = ((ix & M32) << 32) | (iy & M32)
    time_bucket = ((octave & M32) << 32) | (iz & M32)
    return spatial_id, time_bucket


def _lattice_gradient(world_seed, ix, iy, iz, octave):
    spatial_id, time_bucket = _pack_lattice(ix, iy, iz, octave)
    gx, gy, gz, _ = sample_unit_vector3(world_seed, DOMAIN_TERRAIN, spatial_id, time_bucket, PROPERTY_NOISE_GRADIENT)
    return gx, gy, gz


def _fade(t):
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0)


def _lerp(a, b, t):
    return a + t * (b - a)


def gradient_noise3d(world_seed, x, y, z, octave=0):
    """3D Perlin-stilusu gradiens-zaj egy (x,y,z) pontban."""
    ix0 = math.floor(x)
    iy0 = math.floor(y)
    iz0 = math.floor(z)
    ix1, iy1, iz1 = ix0 + 1, iy0 + 1, iz0 + 1
    fx, fy, fz = x - ix0, y - iy0, z - iz0

    u, v, w = _fade(fx), _fade(fy), _fade(fz)

    def corner(cx, cy, cz, dx, dy, dz):
        gx, gy, gz = _lattice_gradient(world_seed, cx, cy, cz, octave)
        return gx * dx + gy * dy + gz * dz

    n000 = corner(ix0, iy0, iz0, fx, fy, fz)
    n100 = corner(ix1, iy0, iz0, fx - 1, fy, fz)
    n010 = corner(ix0, iy1, iz0, fx, fy - 1, fz)
    n110 = corner(ix1, iy1, iz0, fx - 1, fy - 1, fz)
    n001 = corner(ix0, iy0, iz1, fx, fy, fz - 1)
    n101 = corner(ix1, iy0, iz1, fx - 1, fy, fz - 1)
    n011 = corner(ix0, iy1, iz1, fx, fy - 1, fz - 1)
    n111 = corner(ix1, iy1, iz1, fx - 1, fy - 1, fz - 1)

    nx00 = _lerp(n000, n100, u)
    nx10 = _lerp(n010, n110, u)
    nx01 = _lerp(n001, n101, u)
    nx11 = _lerp(n011, n111, u)

    nxy0 = _lerp(nx00, nx10, v)
    nxy1 = _lerp(nx01, nx11, v)

    return _lerp(nxy0, nxy1, w)


def fbm(world_seed, x, y, z, base_frequency=8.0, octaves=5, persistence=0.5, lacunarity=2.0):
    """Fractal Brownian Motion: oktavok osszege, [-1,1]-hez kozeli tartomanyba normalva."""
    total = 0.0
    amplitude = 1.0
    frequency = base_frequency
    max_amplitude = 0.0
    for octave in range(octaves):
        total += gradient_noise3d(world_seed, x * frequency, y * frequency, z * frequency, octave) * amplitude
        max_amplitude += amplitude
        amplitude *= persistence
        frequency *= lacunarity
    return total / max_amplitude


if __name__ == "__main__":
    import random
    rnd = random.Random(2024)

    print("--- Tisztasag ---")
    assert fbm(123, 0.3, 0.5, 0.7) == fbm(123, 0.3, 0.5, 0.7)
    print("OK\n")

    print("--- Ertektartomany plauzibilitasa ---")
    values = []
    for _ in range(20000):
        # veletlen egysegvektor a gombön
        vx, vy, vz = rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1)
        length = math.sqrt(vx * vx + vy * vy + vz * vz)
        if length < 1e-9:
            continue
        vx, vy, vz = vx / length, vy / length, vz / length
        values.append(fbm(0xA7C944210000, vx, vy, vz))
    lo, hi = min(values), max(values)
    mean = sum(values) / len(values)
    print(f"  tartomany: [{lo:.4f}, {hi:.4f}], atlag: {mean:.4f}")
    assert -1.5 < lo and hi < 1.5, "Az fBm ertektartomanya nem plauzibilis"
    assert abs(mean) < 0.1, "Az fBm-nek kozel nulla atlagunak kellene lennie (szimmetrikus zaj)"
    print("OK\n")

    print("--- Terbeli koherencia: kozeli pontok hasonlo erteket adnak ---")
    base = (1.0, 0.0, 0.0)
    near_diffs, far_diffs = [], []
    for _ in range(2000):
        eps = 1e-4
        nx = base[0] + rnd.uniform(-eps, eps)
        ny = base[1] + rnd.uniform(-eps, eps)
        nz = base[2] + rnd.uniform(-eps, eps)
        length = math.sqrt(nx * nx + ny * ny + nz * nz)
        nx, ny, nz = nx / length, ny / length, nz / length
        near_diffs.append(abs(fbm(1, *base) - fbm(1, nx, ny, nz)))

        fx, fy, fz = rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1)
        length = math.sqrt(fx * fx + fy * fy + fz * fz)
        if length < 1e-9:
            continue
        fx, fy, fz = fx / length, fy / length, fz / length
        far_diffs.append(abs(fbm(1, *base) - fbm(1, fx, fy, fz)))

    mean_near = sum(near_diffs) / len(near_diffs)
    mean_far = sum(far_diffs) / len(far_diffs)
    print(f"  atlagos elteres kozeli pontok kozt: {mean_near:.6f}, tavoli pontok kozt: {mean_far:.6f}")
    assert mean_near < mean_far / 10.0, "A zajnak terben koherensnek kell lennie (kozeli != feher zaj)"
    print("OK - a zaj terben koherens (nem feher zaj)\n")

    print("--- Kulonbozo seed mas mezot ad ---")
    # Szandekosan NEM racs-igazitott pont (0.5,0.5,0.5 minden 2^n
    # frekvencian pontosan racspontra esne, ahol fx=fy=fz=0 es a
    # trilinearis interpolacio degeneralt - ez nem hiba, csak rossz
    # tesztpont-valasztas lenne).
    v1 = fbm(1, 0.5123, 0.3456, 0.7891)
    v2 = fbm(2, 0.5123, 0.3456, 0.7891)
    assert v1 != v2
    print("OK\n")

    print("--- Tesztvektorok generalasa (C# porthoz) ---")
    import json
    from threefry_ref import threefry4x64

    vectors = []
    gen_seed = 0xF0157E0000000001
    for i in range(500):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 17, 0], 20)
        vx = -1.0 + (p[0] / 2 ** 64) * 2.0
        vy = -1.0 + (p[1] / 2 ** 64) * 2.0
        vz = -1.0 + (p[2] / 2 ** 64) * 2.0
        length = math.sqrt(vx * vx + vy * vy + vz * vz)
        if length < 1e-9:
            continue
        vx, vy, vz = vx / length, vy / length, vz / length
        value = fbm(0xA7C944210000, vx, vy, vz)
        vectors.append({"x": vx, "y": vy, "z": vz, "fbm": value})

    with open("noise_vectors.json", "w", newline="\n") as f:
        json.dump({"worldSeed": 0xA7C944210000, "vectors": vectors}, f, indent=1)
    print(f"{len(vectors)} zaj-tesztvektor elmentve noise_vectors.json-ba")
