"""
Homerseklet-modell referencia-implementacioja M5-hoz (docs/05-milestones.md
§5.1): Stefan-Boltzmann sugarzasi egyensuly + uveghazhatas + lapse rate.

HATOKOR (tudatosan szukitve, ld. milestones "M5 hatokor"):
  T = T_radiative + T_greenhouse - T_altitude   (T_ocean/T_weather/T_cycle halasztva)

T_greenhouse: a spec (§10.2) csak "derived value"-kent emliti
(greenhouseStrength), zart formula nelkul - meg nincs epitett
AtmosphereLayer modul (osszetetel: CO2/H2O/stb.), ami ebbol szamolna.
Addig egy FIX, VALODI CSILLAGASZATI/KLIMATOLOGIAI ERTEKKEL kozelitjuk:
a Fold tenyleges globalis atlaghomerseklete (~288K) es a legkor nelkuli,
sugarzasi egyensulyi homerseklete (~255K, a globalisan atlagolt "S/4"
kepletbol) kozotti kulonbseg kb. 33K - ez jol dokumentalt, hivatkozhato
fizikai teny, nem kitalalt szam. Ugyanaz a minta, mint ND-10-nel (fix
Fold-szeru ertekek v1.0-ban, kesobb parameterezheto).

ND-27: a Sin/Cos/Pow hasznalata itt ELFOGADOTT kockazat M12 (checkpoint)
elottig, felhasznaloi jovahagyassal.

MODSZER a napi atlag-inszolaciohoz: NEM zart hour-angle formula (az a
tan(latitude)-alapu formula a polusoknal szingularis, tovabbi kulon-
kezelest igenyelne), hanem a mar VERIFIKALT sun_direction_body_frame
SURU MINTAVETELEZESE egy teljes forgas (nap) alatt, es a max(0,cos theta)
atlaga. Ez robusztus, nincs specialis eset a polusoknal (a sarki nappal/
ejszaka magatol, helyesen jon ki a mintavetelezesbol).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math
import json
from astronomy_ref import sun_direction_body_frame
from threefry_ref import threefry4x64

SIGMA = 5.670374419e-8  # Stefan-Boltzmann allando, W/(m^2 K^4)
F_PEAK_DEFAULT = 1361.0  # W/m^2, Fold-szeru naprallando, illusztraciohoz
ALBEDO_OCEAN = 0.06
ALBEDO_LAND = 0.30
LAPSE_RATE_K_PER_M = 0.0065
NUM_DAY_SAMPLES = 24
GREENHOUSE_K_DEFAULT = 33.0  # Fold-szeru uveghazhatas, ld. modul docstring


def daily_average_insolation_factor(
    tile_position, day_t, orbital_period, rotation_period, axial_tilt,
    orbital_phase0=0.0, rotation_phase0=0.0, num_samples=NUM_DAY_SAMPLES,
):
    """max(0,cos theta) atlaga egy teljes forgas (nap) alatt, suru mintavetellel."""
    tx, ty, tz = tile_position
    total = 0.0
    for i in range(num_samples):
        sample_t = day_t + i * (rotation_period / num_samples)
        sx, sy, sz = sun_direction_body_frame(
            sample_t, orbital_period, rotation_period, axial_tilt,
            orbital_phase0, rotation_phase0,
        )
        cos_theta = tx * sx + ty * sy + tz * sz
        total += max(0.0, cos_theta)
    return total / num_samples


def radiative_equilibrium_temperature(avg_insolation_factor, albedo, f_peak=F_PEAK_DEFAULT):
    """T_eq = ((F * avg_factor * (1-A)) / sigma)^(1/4), Kelvinben."""
    absorbed = f_peak * avg_insolation_factor * (1.0 - albedo)
    if absorbed <= 0.0:
        return 0.0  # nincs beeso energia (pl. tartos sarki ejszaka) - nem negativ Kelvin, hanem 0 hatarertek
    return absorbed / SIGMA  # ezt majd a hivo emeli negyedik gyokre - ld. lent


def temperature_kelvin(
    tile_position, day_t, orbital_period, rotation_period, axial_tilt,
    is_oceanic, elevation_m, sea_level_m,
    orbital_phase0=0.0, rotation_phase0=0.0, f_peak=F_PEAK_DEFAULT,
    greenhouse_k=GREENHOUSE_K_DEFAULT,
):
    avg_factor = daily_average_insolation_factor(
        tile_position, day_t, orbital_period, rotation_period, axial_tilt,
        orbital_phase0, rotation_phase0,
    )
    albedo = ALBEDO_OCEAN if is_oceanic else ALBEDO_LAND
    raw = radiative_equilibrium_temperature(avg_factor, albedo, f_peak)
    t_eq = raw ** 0.25 if raw > 0.0 else 0.0

    height_above_sea = max(0.0, elevation_m - sea_level_m)
    t_altitude = LAPSE_RATE_K_PER_M * height_above_sea

    return t_eq + greenhouse_k - t_altitude


if __name__ == "__main__":
    orbital_period = 365.25
    rotation_period = 1.0
    axial_tilt = math.radians(23.44)

    print("Plauzibilitas: homerseklet szelesseg szerint, napejegyenlosegkor (t=0)\n")
    for lat_deg in [0, 15, 30, 45, 60, 75, 90]:
        lat = math.radians(lat_deg)
        # tile pozicio ezen a szelessegen, hosszusag=0 (test-keret x tengelye)
        pos = (math.cos(lat), 0.0, math.sin(lat))
        t_k = temperature_kelvin(
            pos, 0.0, orbital_period, rotation_period, axial_tilt,
            is_oceanic=False, elevation_m=0.0, sea_level_m=0.0,
        )
        print(f"  szelesseg={lat_deg:3d}°: T={t_k:.1f}K ({t_k - 273.15:+.1f}°C)")

    print("\nVart: monoton csokken az egyenlitotol a polus fele")

    print("\nMagassag hatasa (egyenlito, napejegyenloseg):")
    for elev in [0, 1000, 3000, 5000, 8000]:
        pos = (1.0, 0.0, 0.0)
        t_k = temperature_kelvin(
            pos, 0.0, orbital_period, rotation_period, axial_tilt,
            is_oceanic=False, elevation_m=float(elev), sea_level_m=0.0,
        )
        print(f"  magassag={elev:5d}m: T={t_k:.1f}K ({t_k - 273.15:+.1f}°C)")

    print("\n--- Plauzibilitas-ellenorzes ---")
    equator_t = temperature_kelvin((1.0, 0.0, 0.0), 0.0, orbital_period, rotation_period,
                                     axial_tilt, False, 0.0, 0.0)
    pole_t = temperature_kelvin((0.0, 0.0, 1.0), 0.0, orbital_period, rotation_period,
                                  axial_tilt, False, 0.0, 0.0)
    assert equator_t > pole_t, "Az egyenlitonek melegebbnek kell lennie a polusnal"
    print(f"OK - egyenlito ({equator_t:.1f}K) melegebb, mint a polus ({pole_t:.1f}K)")

    sea_level_t = temperature_kelvin((1.0, 0.0, 0.0), 0.0, orbital_period, rotation_period,
                                       axial_tilt, False, 0.0, 0.0)
    mountain_t = temperature_kelvin((1.0, 0.0, 0.0), 0.0, orbital_period, rotation_period,
                                      axial_tilt, False, 5000.0, 0.0)
    assert mountain_t < sea_level_t, "A hegynek hidegebbnek kell lennie, mint a tengerszint"
    print(f"OK - hegy ({mountain_t:.1f}K) hidegebb, mint tengerszint ({sea_level_t:.1f}K)")

    # Determinizmus
    a = temperature_kelvin((0.5, 0.5, 0.7071), 42.0, orbital_period, rotation_period,
                            axial_tilt, True, -1000.0, 0.0)
    b = temperature_kelvin((0.5, 0.5, 0.7071), 42.0, orbital_period, rotation_period,
                            axial_tilt, True, -1000.0, 0.0)
    assert a == b, "Nem tiszta fuggveny!"
    print("OK - determinisztikus (tiszta fuggveny)")

    # Tesztvektorok a C# porthoz
    vectors = []
    gen_seed = 0x7EA0000000000001
    for i in range(150):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 9, 0], 20)
        lat = (p[0] % 1_000_000) / 1_000_000.0 * math.pi - math.pi / 2.0
        lon = (p[1] % 1_000_000) / 1_000_000.0 * 2.0 * math.pi
        day_t = (p[2] % 1_000_000) / 1_000_000.0 * orbital_period
        is_oceanic = (p[3] % 2) == 0
        elevation_m = ((p[3] >> 1) % 10000) - 4000.0

        pos = (math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat))
        t_k = temperature_kelvin(
            pos, day_t, orbital_period, rotation_period, axial_tilt,
            is_oceanic, elevation_m, sea_level_m=0.0,
        )
        vectors.append({
            "x": pos[0], "y": pos[1], "z": pos[2],
            "dayT": day_t, "orbitalPeriod": orbital_period,
            "rotationPeriod": rotation_period, "axialTilt": axial_tilt,
            "isOceanic": is_oceanic, "elevationM": elevation_m, "seaLevelM": 0.0,
            "temperatureK": t_k,
        })

    with open("temperature_vectors.json", "w", newline="\n") as f:
        json.dump({"vectors": vectors}, f, indent=1)
    print(f"\n{len(vectors)} homerseklet-tesztvektor generalva")
