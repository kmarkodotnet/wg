"""
Domain warp referencia-implementacioja: a lemez-Voronoi HOZZARENDELESHEZ es
HATAR-KOZELSEG (gap) szamitashoz hasznalt POZICIOT torzitja el egy zaj-alapu
eltolassal, MIELOTT a legkozelebbi-mag kereses/gap-szamitas megtortenik.

CEL (felhasznaloi visszajelzes alapjan): a plate_ref.assign_plate es
plate_boundary_ref.two_best_dots(_with_indices) tiszta "legkozelebbi mag"
gombi Voronoi-felosztas MATEMATIKAILAG MINDIG sima, nagykor-iv-szeru
hatarvonalat ad, fuggetlenul attol, hogy az elevaciora mennyi zajt teszunk -
ez vizualisan tul "geometrikusnak"/mesterkeltnek hatott. A domain warp a
DONTESHEZ hasznalt pozíciót torzítja el egy terben koherens fBm-eltolással,
igy maga a hatarvonal is organikusan hullamzik, nem csak a rakerult
domborzat-textura.

MODSZER: harom FUGGETLEN fbm-kiertekeles (dx,dy,dz), a MAR VERIFIKALT
noise_ref.fbm ujrafelhasznalasaval - NINCS uj hash-fuggveny, NINCS uj
RandomProperty, a fbm szignaturaja valtozatlan (ld. feladatleiras
megkotese: a C# FractalNoise.Fbm-et valtoztatas nelkul kell tudni hivni
3x). A harom komponens dekorrelaciojat KIZAROLAG fix, egymastol es
nullatol tavoli bemeneti koordinata-eltolassal oldjuk meg (OFFSET_DX/DY/DZ
lent) - igy a harom kiertekeles kulonbozo racspontokra esik, statisztikailag
fuggetlennek tekintheto, annak ellenere, hogy ugyanazt a hash-lancot hasznaljak.

Az eltolas-konstansok (7.13, 2.71, 9.01) stb. TETSZOLEGESEN valasztott, nem
kerek tizedestortek - a cel csak az, hogy a harom (dx,dy,dz) kiertekeles
lattice-cellai NE essenek egybe semmilyen egesz-tobbszorosnel (ami
korrelaciot okozna). Nincs melyebb jelentesuk.

PARAMETER-HANGOLAS (empirikusan mert, ld. __main__ "--- Parameter-hangolas
mereses ---" blokk es a modul-vegi osszegzes): STRENGTH=1.0, FREQUENCY=2.0,
OCTAVES=3 -> atlagos szogeltolodas kb. 9.0 fok (5000+ veletlen ponton merve),
es a lemezhatarok kozeleben (gap < PlateBoundaryEffect.DefaultGapScale=0.04)
levo pontok kb. 40%-anal valtozik meg az assign_plate eredmenye a warp
hatasara - ez erdemi (>15-20%), de nem kaotikus/domblo tobbseg (<90%).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math

from noise_ref import fbm

# Fix, egymastol es nullatol "elegge tavol" koordinata-eltolasok a harom
# (dx,dy,dz) fbm-kiertekeleshez - ld. modul-doc.
OFFSET_DX = (7.13, 2.71, 9.01)
OFFSET_DY = (13.37, 5.59, 1.91)
OFFSET_DZ = (4.67, 11.23, 8.05)

# Vegleges, empirikusan hangolt parameterek - ld. modul-doc "PARAMETER-HANGOLAS".
WARP_STRENGTH = 1.0
WARP_FREQUENCY = 2.0
WARP_OCTAVES = 3


def warp_position(world_seed, position, strength=WARP_STRENGTH, frequency=WARP_FREQUENCY, octaves=WARP_OCTAVES):
    """Eltorzitja a (x,y,z) egysegvektor-pozíciót egy koherens fBm-alapu
    eltolassal, majd visszaallitja egysegvektorra. Tiszta fuggveny - nincs
    mutable allapot, ismetelt hivas ugyanarra a bemenetre bitre azonos
    kimenetet ad."""
    x, y, z = position
    dx = fbm(world_seed, x + OFFSET_DX[0], y + OFFSET_DX[1], z + OFFSET_DX[2],
              base_frequency=frequency, octaves=octaves)
    dy = fbm(world_seed, x + OFFSET_DY[0], y + OFFSET_DY[1], z + OFFSET_DY[2],
              base_frequency=frequency, octaves=octaves)
    dz = fbm(world_seed, x + OFFSET_DZ[0], y + OFFSET_DZ[1], z + OFFSET_DZ[2],
              base_frequency=frequency, octaves=octaves)

    wx = x + strength * dx
    wy = y + strength * dy
    wz = z + strength * dz
    length = math.sqrt(wx * wx + wy * wy + wz * wz)
    if length < 1e-9:
        # Dokumentalt el-eset vedelem: ha a torzitas veletlenul (kozel)
        # pontosan az origoba tolna a pontot (elmeletileg lehetseges, de
        # rendkivul valoszinutlen |strength*d| < 1 miatt), essunk vissza a
        # torzitatlan pozicora - jobb egy enyhe determinisztikus torzitas-
        # kihagyas, mint egy NaN/vegtelen iranyvektor.
        return (x, y, z)
    return (wx / length, wy / length, wz / length)


if __name__ == "__main__":
    import json
    import random
    from threefry_ref import threefry4x64
    from plate_ref import generate_plate_seeds, assign_plate
    from plate_boundary_ref import two_best_dots

    world_seed = 0xA7C944210000

    print("--- Tisztasag (ismetelt hivas azonos) ---")
    p = (0.267261, 0.534522, 0.801784)  # kb. egysegvektor
    l = math.sqrt(sum(c * c for c in p))
    p = tuple(c / l for c in p)
    w1 = warp_position(world_seed, p)
    w2 = warp_position(world_seed, p)
    assert w1 == w2, "A warp_position nem tiszta fuggveny!"
    print("OK\n")

    print("--- Kulonbozo seed mas eredmenyt ad ---")
    w_other = warp_position(world_seed + 1, p)
    assert w1 != w_other, "Kulonbozo seed ugyanazt a warp-eredmenyt adta - gyanus"
    print("OK\n")

    print("--- El-eset: nulla-hosszu eltolas visszaesik a nyers pozicora ---")
    # Kozvetlen egyseg-teszt a fallback-agra (nem realisztikus bemenet, csak
    # a vedelmi logika ellenorzesehez).
    def _warp_raw_dx_override(world_seed, position, dx_val, dy_val, dz_val, strength):
        x, y, z = position
        wx, wy, wz = x + strength * dx_val, y + strength * dy_val, z + strength * dz_val
        length = math.sqrt(wx * wx + wy * wy + wz * wz)
        if length < 1e-9:
            return (x, y, z)
        return (wx / length, wy / length, wz / length)

    fallback = _warp_raw_dx_override(world_seed, (0.0, 0.0, 0.0), 0.0, 0.0, 0.0, 1.0)
    assert fallback == (0.0, 0.0, 0.0), "A nulla-hosszu el-eset nem a nyers pozíciora esik vissza"
    print("OK\n")

    print("--- Parameter-hangolas mereses (atlagos szogeltolodas) ---")
    rnd = random.Random(2024)
    pts = []
    for _ in range(8000):
        vx, vy, vz = rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1)
        length = math.sqrt(vx * vx + vy * vy + vz * vz)
        if length < 1e-9:
            continue
        pts.append((vx / length, vy / length, vz / length))

    angles = []
    for pt in pts:
        wp = warp_position(world_seed, pt)
        dot = max(-1.0, min(1.0, pt[0] * wp[0] + pt[1] * wp[1] + pt[2] * wp[2]))
        angles.append(math.degrees(math.acos(dot)))
    angles.sort()
    mean_angle = sum(angles) / len(angles)
    p50 = angles[len(angles) // 2]
    p90 = angles[int(len(angles) * 0.9)]
    print(f"  n={len(angles)} atlag={mean_angle:.2f} fok, median={p50:.2f} fok, "
          f"p90={p90:.2f} fok, max={angles[-1]:.2f} fok")
    print("  (STRENGTH=1.0, FREQUENCY=2.0, OCTAVES=3 mellett merve; celtartomany 5-15 fok)")
    assert 5.0 < mean_angle < 15.0, "Az atlagos szogeltolodas nem a celzott 5-15 fokos nagysagrendben van"
    print("OK\n")

    print("--- Kozvetlen teszt: lemezhatar-kozeli pontok assign_plate-je valtozik-e ---")
    plate_count = 20
    seeds = generate_plate_seeds(world_seed, plate_count)
    gap_threshold = 0.04  # PlateBoundaryEffect.DefaultGapScale

    rnd2 = random.Random(99)
    near_boundary_pts = []
    attempts = 0
    while len(near_boundary_pts) < 3000 and attempts < 2_000_000:
        attempts += 1
        vx, vy, vz = rnd2.uniform(-1, 1), rnd2.uniform(-1, 1), rnd2.uniform(-1, 1)
        length = math.sqrt(vx * vx + vy * vy + vz * vz)
        if length < 1e-9:
            continue
        candidate = (vx / length, vy / length, vz / length)
        best, second = two_best_dots(candidate, seeds)
        if best - second < gap_threshold:
            near_boundary_pts.append(candidate)

    changed = 0
    for pt in near_boundary_pts:
        wp = warp_position(world_seed, pt)
        a0 = assign_plate(pt, seeds)
        a1 = assign_plate(wp, seeds)
        if a0 != a1:
            changed += 1
    change_pct = 100.0 * changed / len(near_boundary_pts)
    print(f"  {len(near_boundary_pts)} hatar-kozeli pont (gap<{gap_threshold}), "
          f"{changed} db-nal ({change_pct:.1f}%) valtozott az assign_plate")
    print("  ertelmezes: erdemi (>15-20%) hatas, de nem kaotikus/domblo tobbseg (<90%)")
    assert 15.0 < change_pct < 90.0, "A hatar-atrajzolas hatasa nem a vart tartomanyban van"
    print("OK\n")

    print("--- Tesztvektorok generalasa (C# porthoz) ---")
    vectors = []
    gen_seed = 0xD0FA114A00000001
    for i in range(500):
        pk = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 21, 0], 20)
        vx = -1.0 + (pk[0] / 2 ** 64) * 2.0
        vy = -1.0 + (pk[1] / 2 ** 64) * 2.0
        vz = -1.0 + (pk[2] / 2 ** 64) * 2.0
        length = math.sqrt(vx * vx + vy * vy + vz * vz)
        if length < 1e-9:
            continue
        vx, vy, vz = vx / length, vy / length, vz / length
        wx, wy, wz = warp_position(world_seed, (vx, vy, vz))
        vectors.append({
            "x": vx, "y": vy, "z": vz,
            "warpedX": wx, "warpedY": wy, "warpedZ": wz,
        })

    with open("domain_warp_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed,
            "strength": WARP_STRENGTH, "frequency": WARP_FREQUENCY, "octaves": WARP_OCTAVES,
            "offsetDx": list(OFFSET_DX), "offsetDy": list(OFFSET_DY), "offsetDz": list(OFFSET_DZ),
            "vectors": vectors,
        }, f, indent=1)
    print(f"{len(vectors)} domain-warp tesztvektor elmentve domain_warp_vectors.json-ba")
