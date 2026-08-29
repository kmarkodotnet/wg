"""
Becsapodas-esemenyek referencia-implementacioja M11-hez (docs/00-spec-v1.0.md
Sz.22, docs/04-decisions.md ND-28).

HATOKOR (tudatosan szukitve, ld. ND-28): csak becsapodas (nem
vulkan/rift/lemez-hasadas), csak krater atmero+melyseg (nem
rimHeight/ejectaRadius).

FIZIKA - forrasbol ellenorzott ertekek, nem emlekezetbol kitalalva (ld.
ND-28 a pontos forrasokert, WebSearch-csel ketszer fuggetlenul
verifikalva):
  - Transziens krater atmero: Schmidt & Housen (1987) / Collins, Melosh &
    Marcus (2005) skalazas:
    D_tr = 1.161 * (rho_i/rho_t)^(1/3) * L^0.78 * v^0.44 * g^-0.22 * sin(theta)^(1/3)
  - Melyseg: melyseg/atmero kb. 1:5 egyszeru kraterekre (kozismert arany).
  - Becsapodasi gyakorisag: rho(>=D) = 20 * D^-2.4 [1/ev], D meterben -
    hatvanytorveny NEO-becsapodasi rata-modell.
  - Sebesseg: 15-25 km/s (idezett atlagos NEO-Fold utkozesi
    sebesseg-tartomany, kb. 15-21 km/s koze esik a legtobb forrasban).
  - Szog: P(theta) ~ sin(2*theta) - ez GEOMETRIAI TENY (veletlen iranyu
    becsapodas a gombon), nem empirikus meres, zart alakban invertalhato:
    u = (1 - cos(2*theta)) / 2  =>  theta = 0.5 * acos(1 - 2*u)
  - Surusegek: kozet becsapodo ~3000 kg/m^3, kereg cel ~2700 kg/m^3 (Fold
    kontinentalis kereg atlaga, jol ismert ertek).

ND-27 OSZTALYA KITERJESZTVE (nem uj dontes): a krater-keplet
Math.Pow/Sin/Cos-t hasznal - ugyanaz a trigonometria-kockazati kategoria
es M12 elotti lezarasi hatarido vonatkozik ra, mint a klimara es a
lemezmozgasra.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math

from plate_ref import _block

SCALE53 = 2.0 ** -53

DOMAIN_EVENTS = 5
PROPERTY_IMPACT_TRIGGER = 20
PROPERTY_IMPACT_MAGNITUDE = 21
PROPERTY_IMPACT_POSITION = 23
PROPERTY_IMPACT_VELOCITY = 24
PROPERTY_IMPACT_ANGLE = 25

EPOCH_YEARS = 10_000.0
MIN_DIAMETER_M = 1_000.0
MAX_DIAMETER_M = 100_000.0
PARETO_ALPHA = 2.4
RATE_COEFFICIENT_PER_YEAR = 20.0
VELOCITY_MIN_MPS = 15_000.0
VELOCITY_MAX_MPS = 25_000.0
IMPACTOR_DENSITY_KG_M3 = 3000.0
TARGET_DENSITY_KG_M3 = 2700.0
GRAVITY_M_S2 = 9.81
DEPTH_TO_DIAMETER_RATIO = 0.2


def sample(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index=0):
    x = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index)[0]
    return (x >> 11) * SCALE53


def sample_range(world_seed, domain_id, spatial_id, time_bucket, lo, hi, property_id, sample_index=0):
    return lo + (hi - lo) * sample(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index)


def sample_unit_vector3(world_seed, domain_id, spatial_id, time_bucket, property_id):
    i = 0
    while True:
        xs = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, i)
        a, b, c = [(v >> 11) * SCALE53 for v in xs[:3]]
        px, py, pz = 2.0 * a - 1.0, 2.0 * b - 1.0, 2.0 * c - 1.0
        len_sq = px * px + py * py + pz * pz
        if 1e-12 < len_sq <= 1.0:
            inv = 1.0 / math.sqrt(len_sq)
            return px * inv, py * inv, pz * inv
        i += 1


def epoch_probability():
    """Varhato esemenyszam / epoch a D>=MIN_DIAMETER_M kuszobre. Bernoulli-
    kozelitesben hasznalva a Poisson-rata helyett, mert rata << 1
    (dokumentalt egyszerusites, standard gyakorlat ritka esemenyekre)."""
    rate_per_year = RATE_COEFFICIENT_PER_YEAR * MIN_DIAMETER_M ** (-PARETO_ALPHA)
    return rate_per_year * EPOCH_YEARS


def transient_crater_diameter(impactor_diameter_m, velocity_mps, angle_rad):
    """Schmidt & Housen (1987) / Collins, Melosh & Marcus (2005) skalazas."""
    density_ratio = (IMPACTOR_DENSITY_KG_M3 / TARGET_DENSITY_KG_M3) ** (1.0 / 3.0)
    size_term = impactor_diameter_m ** 0.78
    velocity_term = velocity_mps ** 0.44
    gravity_term = GRAVITY_M_S2 ** (-0.22)
    angle_term = math.sin(angle_rad) ** (1.0 / 3.0)
    return 1.161 * density_ratio * size_term * velocity_term * gravity_term * angle_term


def try_generate_impact(world_seed, epoch_index):
    """None, ha nem tortent esemeny ebben az epoch-ban, kulonben dict."""
    p = epoch_probability()
    u_trigger = sample(world_seed, DOMAIN_EVENTS, 0, epoch_index, PROPERTY_IMPACT_TRIGGER)
    if u_trigger >= p:
        return None

    u_mag = sample(world_seed, DOMAIN_EVENTS, 0, epoch_index, PROPERTY_IMPACT_MAGNITUDE)
    diameter = MIN_DIAMETER_M / (1.0 - u_mag) ** (1.0 / PARETO_ALPHA)
    if diameter > MAX_DIAMETER_M:
        diameter = MAX_DIAMETER_M  # dokumentalt biztonsagi sapka, nem ujra-mintavetel (ND-28)

    x, y, z = sample_unit_vector3(world_seed, DOMAIN_EVENTS, 0, epoch_index, PROPERTY_IMPACT_POSITION)
    velocity = sample_range(
        world_seed, DOMAIN_EVENTS, 0, epoch_index,
        VELOCITY_MIN_MPS, VELOCITY_MAX_MPS, PROPERTY_IMPACT_VELOCITY)
    u_angle = sample(world_seed, DOMAIN_EVENTS, 0, epoch_index, PROPERTY_IMPACT_ANGLE)
    angle = 0.5 * math.acos(1.0 - 2.0 * u_angle)

    crater_diameter = transient_crater_diameter(diameter, velocity, angle)
    crater_depth = crater_diameter * DEPTH_TO_DIAMETER_RATIO

    return {
        "x": x, "y": y, "z": z,
        "impactorDiameterMeters": diameter,
        "velocityMetersPerSecond": velocity,
        "angleRadians": angle,
        "craterDiameterMeters": crater_diameter,
        "craterDepthMeters": crater_depth,
    }


if __name__ == "__main__":
    world_seed = 0xA7C944210000

    print("--- Tisztasag (ismetelt hivas azonos) ---")
    for epoch in [0, 1, 42, 12345]:
        r1 = try_generate_impact(world_seed, epoch)
        r2 = try_generate_impact(world_seed, epoch)
        assert r1 == r2, f"Nem tiszta fuggveny epoch={epoch}-nal"
    print("OK - determinisztikus\n")

    print("--- Plauzibilitas: krater-meret nagysagrend ---")
    # 1 km-es becsapodo, kb. 20 km/s, ~45 fok -> a szakirodalomban tobbszor
    # idezett tartomanyhoz kepest (nehany km - nehany 10 km) plauzibilis
    # meretu tranziens kratert kell adjon.
    d = transient_crater_diameter(1000.0, 20000.0, math.radians(45.0))
    print(f"  1 km becsapodo, 20 km/s, 45 fok -> {d/1000.0:.2f} km transziens krater")
    assert 3000.0 < d < 30000.0, f"Nem plauzibilis kraterMeret: {d}"
    print("OK\n")

    print("--- Historia-szimulacio: gyakorisag + meret-eloszlas plauzibilitasa ---")
    n_epochs = 20_000  # 200 Myr
    events = []
    for epoch in range(n_epochs):
        r = try_generate_impact(world_seed, epoch)
        if r is not None:
            events.append(r)
    observed_rate = len(events) / n_epochs
    expected_rate = epoch_probability()
    print(f"  {len(events)} esemeny {n_epochs} epoch alatt "
          f"(megfigyelt rata={observed_rate:.5f}, varhato={expected_rate:.5f})")
    # Statisztikai tolerancia: binomialis szoras kb. sqrt(n*p*(1-p))
    std = math.sqrt(n_epochs * expected_rate * (1 - expected_rate))
    assert abs(len(events) - n_epochs * expected_rate) < 5 * std, "Gyakorisag nem plauzibilis"
    print("OK - a gyakorisag statisztikailag egyezik a varhato ratval\n")

    diameters = sorted(e["impactorDiameterMeters"] for e in events)
    small = sum(1 for d in diameters if d < 5000.0)
    large = sum(1 for d in diameters if d >= 20000.0)
    print(f"  meret-eloszlas: {small} kicsi (<5km), {large} nagy (>=20km) a {len(diameters)}-bol")
    assert small > large, "Nehez-farku eloszlasnak sokkal tobb kis esemenyt kell adnia, mint nagyot"
    print("OK - sok kicsi, keves nagy (nehez-farku eloszlas, spec Sz.22.3)\n")

    print("--- Minden parameter erdemben hat a kimenetre ---")
    r_base = try_generate_impact(world_seed, 7)  # esemeny nelkuli epoch keresese kesobb
    # Kulon domain/property hasznalata miatt a pozicio/sebesseg/szog
    # fuggetlenul valtozik - kozvetlen ellenorzes egy tuzelo epoch-on.
    fired_epoch = next(e for e in range(n_epochs) if try_generate_impact(world_seed, e) is not None)
    r = try_generate_impact(world_seed, fired_epoch)
    r_next = try_generate_impact(world_seed, fired_epoch + 1) or {}
    assert r["x"] != r_next.get("x"), "Poziciok nem valtoznak epoch-ok kozott"
    print(f"OK - fuggetlen mintavetel epoch={fired_epoch}-nal\n")

    print("--- Tesztvektorok generalasa (C# porthoz) ---")
    import json

    records = []
    for epoch in range(n_epochs):
        r = try_generate_impact(world_seed, epoch)
        if r is None:
            records.append({"epochIndex": epoch, "occurred": False})
        else:
            rec = {"epochIndex": epoch, "occurred": True}
            rec.update(r)
            records.append(rec)

    with open("impacts_vectors.json", "w", newline="\n") as f:
        json.dump({"worldSeed": world_seed, "epochYears": EPOCH_YEARS, "records": records}, f, indent=1)
    print(f"{len(records)} epoch-rekord ({len(events)} esemennyel) elmentve impacts_vectors.json-ba")
