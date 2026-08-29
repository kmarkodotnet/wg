"""
Szuper-vulkáni (VEI8) események referencia-implementációja M11-hez
(docs/00-spec-v1.0.md Sz.21, docs/04-decisions.md ND-29).

HATOKOR (tudatosan szukitve, ld. ND-29): CSAK a szuper-vulkani (VEI8,
>=1e12 m^3 tefra) esemenyeket modellezzuk epoch-alapu ritka esemenykent.
A "hatter" vulkanossag (VEI3-7, gyakori) NAGYSAGRENDEKKEL gyakoribb,
mint amit egy epoch-Bernoulli modell kezelni tud (pl. VEI5 ~1500 varhato
esemeny/10000-eves-epoch) - strukturalisan mas modellt igenyelne, kulon
feladat. A spec maga is kulon kategoriakent kezeli a SuperVolcanicEruption/
SuperVolcano fogalmat.

FIZIKA - forrasbol ellenorzott ertekek (WebSearch, tobb fuggetlen forras
egyezesevel):
  - VEI8 kuszob: >=1e12 m^3 tefra (hivatalos VEI-skala also hatara).
  - Meret-eloszlas: minden VEI-lepes (10x terfogat) kb. 6-7x ritkabb ->
    hatvanytorveny N(>=V) ~ V^-beta, beta = log10(6.5) ~= 0.8129.
  - Gyakorisag: VEI8 ~1-2/millio ev -> rata ~= 1.5e-6/ev a kuszobnel.
  - Felso sapka (dokumentalt, nem ujra-mintavetelezett): 5e12 m^3 - a
    La Garita Caldera/Fish Canyon Tuff kitores (~28 millio eve), a
    valaha ismert LEGNAGYOBB vulkankitores becsult terfogata.
  - Geometria: pajzsvulkan-kup, lejtoszog ~6 fok (idezett tartomany
    2-10 / 4-8 fok kozepe). V = pi*h^3/(3*tan^2(theta)) kup-terfogatbol
    h = (3*V*tan^2(theta)/pi)^(1/3), r = h/tan(theta).
  - Pozicio: PlateBoundaryEffect "gap" (ket legkozelebbi lemez-mag
    dot-product kulonbsege) UJRAFELHASZNALVA - elutasitasos mintavetel,
    1-gap/gapScale elfogadasi valoszinuseggel, igy a pozicio a
    lemezhatarok kore koncentralodik.

ND-27 osztalya: nincs uj transzcendens-kockazat - csak
deterministic_math_ref.pow_ (kobgyok) es egy FIX, konstrukcios idejű
tan(6 fok) (nem futasidejű trigonometria).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math

from plate_ref import _block, sample4, generate_plate_seeds, assign_plate
from plate_boundary_ref import two_best_dots
from deterministic_math_ref import pow_ as det_pow

SCALE53 = 2.0 ** -53

DOMAIN_EVENTS = 5
PROPERTY_VOLCANIC_TRIGGER = 22
PROPERTY_VOLCANIC_MAGNITUDE = 26
PROPERTY_VOLCANIC_POSITION = 27

EPOCH_YEARS = 10_000.0
VEI8_MIN_VOLUME_M3 = 1.0e12
MAX_VOLUME_M3 = 5.0e12  # La Garita / Fish Canyon Tuff - legnagyobb ismert kitores
VOLUME_FREQUENCY_EXPONENT = math.log10(6.5)  # ~0.8129
VEI8_RATE_PER_YEAR = 1.5e-6  # ~1-2 esemeny / millio ev

SLOPE_ANGLE_DEGREES = 6.0  # pajzsvulkan tipikus lejtoszoge (2-10 / 4-8 fok kozepe)
TAN_SLOPE = math.tan(math.radians(SLOPE_ANGLE_DEGREES))  # konstrukcios idejű konstans

GAP_SCALE = 0.04  # ugyanaz, mint PlateBoundaryEffect.DefaultGapScale


def sample(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index=0):
    x = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index)[0]
    return (x >> 11) * SCALE53


# Fix bemenetu ertek egyszer kiszamolva - a rata-egyutthato ugy van
# kalibralva, hogy N(>=VEI8_MIN_VOLUME_M3) PONTOSAN VEI8_RATE_PER_YEAR legyen.
_RATE_COEFFICIENT = VEI8_RATE_PER_YEAR * (VEI8_MIN_VOLUME_M3 ** VOLUME_FREQUENCY_EXPONENT)


def epoch_probability():
    """Varhato esemenyszam / epoch a VEI8 kuszobre (Bernoulli-kozelites)."""
    rate_per_year = _RATE_COEFFICIENT * (VEI8_MIN_VOLUME_M3 ** (-VOLUME_FREQUENCY_EXPONENT))
    return rate_per_year * EPOCH_YEARS


def edifice_geometry(volume_m3):
    """Pajzsvulkan-kup magassaga es sugara a kitores terfogatabol."""
    h = det_pow(3.0 * volume_m3 * TAN_SLOPE * TAN_SLOPE / math.pi, 1.0 / 3.0)
    r = h / TAN_SLOPE
    return h, r


def sample_position_near_boundary(world_seed, epoch_bucket, seeds):
    """Elutasitasos mintavetel: egyenletes gombi pont, elfogadva a
    lemezhatar-kozelseggel aranyos valoszinuseggel."""
    i = 0
    while True:
        xs = _block(world_seed, DOMAIN_EVENTS, 0, epoch_bucket, PROPERTY_VOLCANIC_POSITION, i)
        a, b, c, d = [(v >> 11) * SCALE53 for v in xs]
        px, py, pz = 2.0 * a - 1.0, 2.0 * b - 1.0, 2.0 * c - 1.0
        len_sq = px * px + py * py + pz * pz
        if 1e-12 < len_sq <= 1.0:
            inv = 1.0 / math.sqrt(len_sq)
            cx, cy, cz = px * inv, py * inv, pz * inv
            best, second = two_best_dots((cx, cy, cz), seeds)
            gap = best - second
            accept_prob = (1.0 - gap / GAP_SCALE) if gap < GAP_SCALE else 0.0
            if d < accept_prob:
                return cx, cy, cz
        i += 1


def try_generate_supervolcano(world_seed, epoch_index, seeds):
    """None, ha nem tortent esemeny, kulonben dict."""
    p = epoch_probability()
    u_trigger = sample(world_seed, DOMAIN_EVENTS, 0, epoch_index, PROPERTY_VOLCANIC_TRIGGER)
    if u_trigger >= p:
        return None

    u_mag = sample(world_seed, DOMAIN_EVENTS, 0, epoch_index, PROPERTY_VOLCANIC_MAGNITUDE)
    volume = VEI8_MIN_VOLUME_M3 / det_pow(1.0 - u_mag, 1.0 / VOLUME_FREQUENCY_EXPONENT)
    if volume > MAX_VOLUME_M3:
        volume = MAX_VOLUME_M3  # dokumentalt biztonsagi sapka, nem ujra-mintavetel (ND-29)

    x, y, z = sample_position_near_boundary(world_seed, epoch_index, seeds)
    height, radius = edifice_geometry(volume)

    return {
        "x": x, "y": y, "z": z,
        "volumeCubicMeters": volume,
        "edificeHeightMeters": height,
        "edificeRadiusMeters": radius,
    }


if __name__ == "__main__":
    world_seed = 0xA7C944210000
    plate_count = 20
    seeds = generate_plate_seeds(world_seed, plate_count)

    print("--- Tisztasag (ismetelt hivas azonos) ---")
    for epoch in [0, 1, 42, 12345]:
        r1 = try_generate_supervolcano(world_seed, epoch, seeds)
        r2 = try_generate_supervolcano(world_seed, epoch, seeds)
        assert r1 == r2, f"Nem tiszta fuggveny epoch={epoch}-nal"
    print("OK - determinisztikus\n")

    print("--- Plauzibilitas: edifice-meret nagysagrend ---")
    h, r = edifice_geometry(VEI8_MIN_VOLUME_M3)
    print(f"  VEI8 kuszob ({VEI8_MIN_VOLUME_M3:.0e} m^3) -> magassag={h:.0f} m, sugar={r:.0f} m")
    # Realis vulkan-meretek (nehany szaz - nehany ezer meter magassag,
    # tiz-egynehany km sugar) tartomanyaba kell essen.
    assert 300.0 < h < 5000.0, f"Nem plauzibilis magassag: {h}"
    assert 5000.0 < r < 100000.0, f"Nem plauzibilis sugar: {r}"
    print("OK\n")

    print("--- Historia-szimulacio: gyakorisag plauzibilitasa ---")
    n_epochs = 20_000  # 200 Myr - ugyanaz a lepteku, mint impacts_ref.py
    events = []
    for epoch in range(n_epochs):
        r = try_generate_supervolcano(world_seed, epoch, seeds)
        if r is not None:
            events.append(r)
    observed_rate = len(events) / n_epochs
    expected_rate = epoch_probability()
    print(f"  {len(events)} esemeny {n_epochs} epoch alatt "
          f"(megfigyelt rata={observed_rate:.5f}, varhato={expected_rate:.5f})")
    std = math.sqrt(n_epochs * expected_rate * (1 - expected_rate))
    assert abs(len(events) - n_epochs * expected_rate) < 5 * std, "Gyakorisag nem plauzibilis"
    print("OK - a gyakorisag statisztikailag egyezik a varhato ratval\n")

    if events:
        print("--- Pozicio-plauzibilitas: a hataroktol vett tavolsag ---")
        gaps = []
        for e in events:
            best, second = two_best_dots((e["x"], e["y"], e["z"]), seeds)
            gaps.append(best - second)
        max_gap = max(gaps)
        print(f"  max gap a {len(gaps)} esemeny kozott: {max_gap:.4f} (kuszob: {GAP_SCALE})")
        assert max_gap < GAP_SCALE, "Egy esemeny sem eshet a hatar-savon kivul"
        print("OK - minden esemeny a lemezhatarok kozeleben van\n")

    print("--- Tesztvektorok generalasa (C# porthoz) ---")
    import json

    records = []
    for epoch in range(n_epochs):
        r = try_generate_supervolcano(world_seed, epoch, seeds)
        if r is None:
            records.append({"epochIndex": epoch, "occurred": False})
        else:
            rec = {"epochIndex": epoch, "occurred": True}
            rec.update(r)
            records.append(rec)

    with open("volcanism_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count,
            "epochYears": EPOCH_YEARS, "records": records,
        }, f, indent=1)
    print(f"{len(records)} epoch-rekord ({len(events)} esemennyel) elmentve volcanism_vectors.json-ba")
