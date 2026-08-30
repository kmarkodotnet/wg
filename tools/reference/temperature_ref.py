"""
Homerseklet-modell referencia-implementacioja M5-hoz (docs/05-milestones.md
§5.1, backlog "M5 | Teljes homerseklet-modell"): Stefan-Boltzmann sugarzasi
egyensuly + uveghazhatas + lapse rate + ocean/weather/cycle tagok (spec §28.1).

HATOKOR - KET RETEG, EGYMAS MELLETT (ld. ND-42, docs/04-decisions.md):

  1. `temperature_kelvin` (EREDETI, SZANDEKOSAN VALTOZATLAN fuggveny-torzs es
     szignatura):
         T = T_radiative + T_greenhouse - T_altitude
     A mar generalt 150 elemu temperature_vectors.json (es a rajta mero C#
     Temperature.cs / TemperatureTests.cs) bitre azonos marad ehhez a
     fuggvenyhez - nem modosult egyetlen sorban sem.

  2. `temperature_kelvin_full` (UJ, a spec §28.1 TELJES egyenlete):
         T = T_radiative + T_greenhouse + T_ocean - T_altitude + T_weather + T_cycle
     Ez az M5 "teljes homerseklet-modell" celallapota. A C# oldalra meg NINCS
     portolva (kulon, kesobbi lepes - lasd ND-42 zaro megjegyzese). A hozza
     tartozo tesztvektorok KULON fajlba kerulnek (temperature_full_vectors.json),
     NEM a temperature_vectors.json-ba - igy a ket generacio nem utkozik, es a
     meglevo C# port nem tores el hallgatolagosan.

T_greenhouse: a spec (§10.2) csak "derived value"-kent emliti
(greenhouseStrength), zart formula nelkul - meg nincs epitett
AtmosphereLayer modul (osszetetel: CO2/H2O/stb.), ami ebbol szamolna.
A bazisertek egy FIX, VALODI CSILLAGASZATI/KLIMATOLOGIAI ERTEKKEL kozelitve:
a Fold tenyleges globalis atlaghomerseklete (~288K) es a legkor nelkuli,
sugarzasi egyensulyi homerseklete (~255K, a globalisan atlagolt "S/4"
kepletbol) kozotti kulonbseg kb. 33K - ez jol dokumentalt, hivatkozhato
fizikai teny, nem kitalalt szam. Ugyanaz a minta, mint ND-10-nel (fix
Fold-szeru ertekek v1.0-ban, kesobb parameterezheto). A `temperature_kelvin_full`-
ban ez a bazisertek egy logaritmikus, "uveghazgaz-koncentracio"-proxy
korrekcioval bovul (ND-42) - a referencia-koncentracion PONTOSAN a
bazisertekre esik vissza (ld. lent, `greenhouse_temperature`).

ND-27 (docs/04-decisions.md): LEZARVA - a Sin/Cos/Ln/Exp helyett a mar
verifikalt `deterministic_math_ref` polinomialis implementaciot hasznaljuk
minden UJ szamitasi agban (greenhouse/cycle), hogy ne nyissunk uj,
nem-dokumentalt transzcendens-kockazatot. MEGJEGYZES (nem ebben a feladatban
javitando): a MAR MEGLEVO T_radiative/T_altitude lanc (astronomy_ref.
sun_direction_body_frame es a `raw ** 0.25` negyedik gyok) meg mindig nyers
math.sin/cos-t es Python beepitett **-ot hasznal - ez a modul ND-27 lezarasa
ELOTTI allapotot tukrozi, es kivul esik ezen a feladaton (a meglevo bazisreteg
nincs atirva).

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
from noise_ref import fbm
import deterministic_math_ref as dm

SIGMA = 5.670374419e-8  # Stefan-Boltzmann allando, W/(m^2 K^4)
F_PEAK_DEFAULT = 1361.0  # W/m^2, Fold-szeru naprallando, illusztraciohoz
ALBEDO_OCEAN = 0.06
ALBEDO_LAND = 0.30
LAPSE_RATE_K_PER_M = 0.0065
NUM_DAY_SAMPLES = 24
GREENHOUSE_K_DEFAULT = 33.0  # Fold-szeru uveghazhatas, ld. modul docstring

# --- UJ (ND-42): T_greenhouse parameterezese ---------------------------------
EARTH_GHG_REFERENCE_PPM = 280.0  # preindusztrialis CO2-koncentracio, Fold-analog referenciapont
GREENHOUSE_SENSITIVITY_K_PER_DOUBLING_DEFAULT = 3.0  # ld. ND-42: IPCC "equilibrium climate sensitivity" kozponti becsles (~1.5-4.5K bizonytalansagi sav), NEM algoritmus-konstans

# --- UJ (ND-42): T_ocean (kontinentalitas-csillapitas) parameterei ----------
OCEAN_BUFFERING_STRENGTH_DEFAULT = 0.3  # ld. ND-42: modellezesi valasztas, nem spec-adat
OCEAN_ANNUAL_SAMPLES = 12  # havi felbontasu evi atlag a napi-atlag inszolaciobol

# --- UJ (ND-42): T_weather (§32.2 stateles idonoise placeholder) -----------
# W(p,t) = Noise(Warp(p,t), seed) - Warp itt egy egyszeru, determinisztikus
# "advekcios" idobeli eltolas (ld. ND-42 hatarvonal-jegyzet: EZ NEM a teljes
# szel/nedvesseg-modell, csak minimalis placeholder, amig a testver-feladat
# wind_precipitation_ref.py el nem keszul es integralhato).
WEATHER_OFFSET = (23.17, -17.59, 31.41)  # tetszolegesen valasztott, nullatol tavoli eltolas - decorrelal a terrain-fbm ugyanazon pontban vett ertekeitol (ld. domain_warp_ref.py mintaja)
WEATHER_TIME_DRIFT = (0.4517, -0.7392, 0.1234)  # tetszoleges, egysegnyinel nem normalt "advekcios" iranyvektor az fbm koordinata-terben
WEATHER_TIME_SCALE_PER_DAY = 0.2  # ennyi fbm-koordinata-egyseget "sodor" a mezo naponta - hatarozza meg, hany nap alatt valtozik erdemben az idojaras
WEATHER_NOISE_FREQUENCY = 3.0
WEATHER_NOISE_OCTAVES = 4
WEATHER_AMPLITUDE_K_DEFAULT = 4.0  # K, ld. spec §32.3 pelda (-4°C tipikus deviacio)

# --- UJ (ND-42): T_cycle (Milankovic-szeru additiv kenyszerito oszcillacio) -
# HATOKOR-HATAR (ld. ND-42 es a feladatleiras): ez CSAK a homerseklet-egyenlet
# egy additiv, idofuggo tagja. A jegtakaro/eljegesedes TENYLEGES kovetkezmenye
# (jegmennyiseg, tengerszint-hatas) MASHOL (M10 eroziós+eljegesedesi ciklusok,
# kulon feladat) implementalando - EZ A MODUL NEM NYUL AHHOZ.
# A periodusok "fizikailag esszeru tartomanybol" SEEDELTEN valasztottak (spec
# §25: "Nem kell foldi periodusokat hasznalni"), NEM a valodi Fold-analog
# 100k/41k/23k eves Milankovic-periodusok.
CYCLE_ECCENTRICITY_PERIOD_YEARS_RANGE = (50_000.0, 500_000.0)
CYCLE_OBLIQUITY_PERIOD_YEARS_RANGE = (20_000.0, 150_000.0)
CYCLE_PRECESSION_PERIOD_YEARS_RANGE = (10_000.0, 50_000.0)
CYCLE_ECCENTRICITY_AMPLITUDE_K_DEFAULT = 2.0
CYCLE_OBLIQUITY_AMPLITUDE_K_DEFAULT = 3.0
CYCLE_PRECESSION_AMPLITUDE_K_DEFAULT = 1.0


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


# ============================================================================
# UJ (M5 "Teljes homerseklet-modell", ND-42): T_greenhouse/T_ocean/T_weather/
# T_cycle + a §28.1 teljes osszegzo fuggveny. A fenti `temperature_kelvin`
# VALTOZATLAN marad - ld. modul-doc "HATOKOR - KET RETEG".
# ============================================================================

def greenhouse_temperature(
    ghg_ppm=EARTH_GHG_REFERENCE_PPM,
    reference_ghg_ppm=EARTH_GHG_REFERENCE_PPM,
    greenhouse_k=GREENHOUSE_K_DEFAULT,
    sensitivity_k_per_doubling=GREENHOUSE_SENSITIVITY_K_PER_DOUBLING_DEFAULT,
):
    """T_greenhouse: Fold-analog bazisertek (33K) + logaritmikus, CO2-szeru
    "uveghazgaz-koncentracio"-proxy korrekcio a referencia-koncentraciohoz
    kepest (ld. ND-42). ghg_ppm == reference_ghg_ppm eseten a korrekcio
    PONTOSAN 0 (ln(1)=0 egzaktul, ld. __main__ plauzibilitas-ellenorzes),
    tehat az alapertelmezett hivas pontosan a Fold-analog bazisertekre esik -
    ugyanaz, mint a regi, fix `GREENHOUSE_K_DEFAULT`.

    A logaritmus-alapu forras-modell (radiative forcing ~ ln(concentration))
    a legkorfizika jol ismert kvalitativ mintaja (CO2-dupolazodas -> kb.
    allando homerseklet-novekmeny) - de a `sensitivity_k_per_doubling`
    ERTEKE (3.0 K/dupolazodas) modellezesi valasztas, nem KAT-szeruen
    verifikalhato algoritmus-konstans (ld. ND-42, megerositest igenyel)."""
    ghg_ppm = max(ghg_ppm, 1e-6)  # vedelem: ln csak pozitiv szamra ertelmezett
    doublings = dm.ln(ghg_ppm / reference_ghg_ppm) / dm.ln(2.0)
    return greenhouse_k + sensitivity_k_per_doubling * doublings


def _annual_mean_radiative_temperature(
    tile_position, orbital_period, rotation_period, axial_tilt, albedo,
    f_peak=F_PEAK_DEFAULT, orbital_phase0=0.0, rotation_phase0=0.0,
    annual_samples=OCEAN_ANNUAL_SAMPLES,
):
    """Az adott tile T_radiative-jenek evi (havi felbontasu) atlaga - a T_ocean
    csillapitas "cel-erteke" (ld. `ocean_buffering_temperature`)."""
    total = 0.0
    for j in range(annual_samples):
        day_t = j * (orbital_period / annual_samples)
        factor = daily_average_insolation_factor(
            tile_position, day_t, orbital_period, rotation_period, axial_tilt,
            orbital_phase0, rotation_phase0,
        )
        raw = radiative_equilibrium_temperature(factor, albedo, f_peak)
        total += raw ** 0.25 if raw > 0.0 else 0.0
    return total / annual_samples


def ocean_buffering_temperature(instant_t_radiative, annual_mean_t_radiative, is_oceanic,
                                 buffering_strength=OCEAN_BUFFERING_STRENGTH_DEFAULT):
    """T_ocean: a viz nagy hoterhetetlensege az ocean-tile-ok pillanatnyi
    homerseklet-szelsoseget (evszakos/napi) az evi atlag fele huzza -
    kontinentalitas-effektus. Szarazfoldon (is_oceanic=False) pontosan 0 -
    ez a regi, ocean nelkuli viselkedes megorzese. A `buffering_strength`
    (0.3 alapertelmezett) modellezesi valasztas (ld. ND-42), nem spec-adat."""
    if not is_oceanic:
        return 0.0
    return buffering_strength * (annual_mean_t_radiative - instant_t_radiative)


def weather_deviation_k(world_seed, tile_position, day_t, amplitude_k=WEATHER_AMPLITUDE_K_DEFAULT):
    """T_weather: §32.2 stateless idonoise minta, W(p,t) = Noise(Warp(p,t), seed).
    Warp itt egy egyszeru, determinisztikus "advekcios" idobeli eltolas
    (a poziciot a WEATHER_TIME_DRIFT iranyban day_t-vel aranyosan tolja el,
    mielott a mar verifikalt `noise_ref.fbm`-et kiertekelnenk) - ld. ND-42:
    ez EGY ONALLO, MINIMALIS PLACEHOLDER, nem a teljes szel/nyomas/
    nedvesseg-alapu idojaras-mezo (spec §32.1). Amikor a testver-feladat
    (szel/nedvesseg/csapadek) elkesziti a `wind_precipitation_ref.py`-t,
    EZT A FUGGVENYT AT KELL NEZNI/EGYESITENI azzal (ld. ND-42 zaro
    megjegyzese) - ne ket fuggetlen idojaras-forras maradjon a rendszerben.

    A §32.3 kovetelmenye ("a weather noise nem irhatja felul a klimat")
    az amplitudo korlatozasaval teljesul: a fbm kimenete kb. [-1.5,1.5]
    tartomanyba esik, tehat a devicio tipikusan +-amplitude_k*1.5 nagysagrendu,
    ADDITIV a klima-atlaghoz kepest (nem szorzo, nem fellulirasi mechanizmus)."""
    x, y, z = tile_position
    drift = day_t * WEATHER_TIME_SCALE_PER_DAY
    wx = x + WEATHER_OFFSET[0] + WEATHER_TIME_DRIFT[0] * drift
    wy = y + WEATHER_OFFSET[1] + WEATHER_TIME_DRIFT[1] * drift
    wz = z + WEATHER_OFFSET[2] + WEATHER_TIME_DRIFT[2] * drift
    n = fbm(world_seed, wx, wy, wz, base_frequency=WEATHER_NOISE_FREQUENCY, octaves=WEATHER_NOISE_OCTAVES)
    return amplitude_k * n


def _lerp_range(raw_uint64, bounds):
    lo, hi = bounds
    frac = (raw_uint64 % 1_000_000) / 1_000_000.0
    return lo + frac * (hi - lo)


def _cycle_component_params(world_seed):
    """Seedelt periodus/fazis harom Milankovic-szeru komponenshez (§25),
    "fizikailag esszeru tartomanyban" (ld. modul-szintu CYCLE_*_RANGE
    konstansok es ND-42) - NEM a valodi excentricitas/tengelydoles-szamitas
    (az M10 hatoskore), csak egy determinisztikus, seed-fuggo oszcillacio-
    parameterezes."""
    p = threefry4x64([0, 0, 0, 0], [world_seed, 0, 41, 0], 20)
    ecc_period = _lerp_range(p[0], CYCLE_ECCENTRICITY_PERIOD_YEARS_RANGE)
    obl_period = _lerp_range(p[1], CYCLE_OBLIQUITY_PERIOD_YEARS_RANGE)
    prec_period = _lerp_range(p[2], CYCLE_PRECESSION_PERIOD_YEARS_RANGE)
    phases = threefry4x64([1, 0, 0, 0], [world_seed, 0, 41, 0], 20)
    ecc_phase = (phases[0] % 1_000_000) / 1_000_000.0 * 2.0 * math.pi
    obl_phase = (phases[1] % 1_000_000) / 1_000_000.0 * 2.0 * math.pi
    prec_phase = (phases[2] % 1_000_000) / 1_000_000.0 * 2.0 * math.pi
    return (ecc_period, ecc_phase), (obl_period, obl_phase), (prec_period, prec_phase)


def climate_cycle_temperature_k(
    world_seed, t_years,
    eccentricity_amplitude_k=CYCLE_ECCENTRICITY_AMPLITUDE_K_DEFAULT,
    obliquity_amplitude_k=CYCLE_OBLIQUITY_AMPLITUDE_K_DEFAULT,
    precession_amplitude_k=CYCLE_PRECESSION_AMPLITUDE_K_DEFAULT,
):
    """T_cycle: harom szinuszos, Milankovic-szeru komponens (eccentricity/
    obliquity/precession, spec §25) osszege - additiv kenyszerito oszcillacio
    a hosszu-tavu (tizezer-millio eves) klimaciklusokhoz. HATASKOR-HATAR:
    ez CSAK a homerseklet-egyenlet bemenete, a jegtakaro/tengerszint tenyleges
    visszahatasat NEM ez a modul szamolja (ld. modul-doc es ND-42)."""
    (ecc_period, ecc_phase), (obl_period, obl_phase), (prec_period, prec_phase) = (
        _cycle_component_params(world_seed)
    )
    ecc_sin, _ = dm.sin_cos(2.0 * math.pi * t_years / ecc_period + ecc_phase)
    obl_sin, _ = dm.sin_cos(2.0 * math.pi * t_years / obl_period + obl_phase)
    prec_sin, _ = dm.sin_cos(2.0 * math.pi * t_years / prec_period + prec_phase)
    return (
        eccentricity_amplitude_k * ecc_sin
        + obliquity_amplitude_k * obl_sin
        + precession_amplitude_k * prec_sin
    )


def temperature_kelvin_full(
    world_seed, tile_position, day_t, orbital_period, rotation_period, axial_tilt,
    is_oceanic, elevation_m, sea_level_m,
    orbital_phase0=0.0, rotation_phase0=0.0, f_peak=F_PEAK_DEFAULT,
    greenhouse_k=GREENHOUSE_K_DEFAULT, ghg_ppm=EARTH_GHG_REFERENCE_PPM,
    ocean_buffering_strength=OCEAN_BUFFERING_STRENGTH_DEFAULT,
    weather_amplitude_k=WEATHER_AMPLITUDE_K_DEFAULT,
    t_years=0.0,
    cycle_eccentricity_amplitude_k=CYCLE_ECCENTRICITY_AMPLITUDE_K_DEFAULT,
    cycle_obliquity_amplitude_k=CYCLE_OBLIQUITY_AMPLITUDE_K_DEFAULT,
    cycle_precession_amplitude_k=CYCLE_PRECESSION_AMPLITUDE_K_DEFAULT,
):
    """Spec §28.1 TELJES egyenlete:
        T = T_radiative + T_greenhouse + T_ocean - T_altitude + T_weather + T_cycle
    Minden uj tag ND-42-ben dokumentalt modellezesi valasztas. Tiszta
    fuggveny (nincs mutable allapot) - ld. __main__ determinizmus-teszt."""
    avg_factor = daily_average_insolation_factor(
        tile_position, day_t, orbital_period, rotation_period, axial_tilt,
        orbital_phase0, rotation_phase0,
    )
    albedo = ALBEDO_OCEAN if is_oceanic else ALBEDO_LAND
    raw = radiative_equilibrium_temperature(avg_factor, albedo, f_peak)
    t_radiative = raw ** 0.25 if raw > 0.0 else 0.0

    t_greenhouse = greenhouse_temperature(ghg_ppm, EARTH_GHG_REFERENCE_PPM, greenhouse_k)

    t_ocean = 0.0
    if is_oceanic:
        annual_mean_t = _annual_mean_radiative_temperature(
            tile_position, orbital_period, rotation_period, axial_tilt, albedo, f_peak,
            orbital_phase0, rotation_phase0,
        )
        t_ocean = ocean_buffering_temperature(t_radiative, annual_mean_t, is_oceanic, ocean_buffering_strength)

    height_above_sea = max(0.0, elevation_m - sea_level_m)
    t_altitude = LAPSE_RATE_K_PER_M * height_above_sea

    t_weather = weather_deviation_k(world_seed, tile_position, day_t, weather_amplitude_k)

    t_cycle = climate_cycle_temperature_k(
        world_seed, t_years, cycle_eccentricity_amplitude_k,
        cycle_obliquity_amplitude_k, cycle_precession_amplitude_k,
    )

    return t_radiative + t_greenhouse + t_ocean - t_altitude + t_weather + t_cycle


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

    # ========================================================================
    # UJ (M5 "Teljes homerseklet-modell", ND-42): temperature_kelvin_full
    # ========================================================================
    print("\n\n=== temperature_kelvin_full (spec §28.1 teljes egyenlet) ===\n")
    demo_seed = 0xA7C944210000

    print("--- T_greenhouse: referencia-koncentracion pontosan a bazisertek ---")
    g_ref = greenhouse_temperature(EARTH_GHG_REFERENCE_PPM)
    assert g_ref == GREENHOUSE_K_DEFAULT, "Referencia-koncentracion egzaktul a bazisertekre kellene esnie"
    print(f"OK - greenhouse_temperature(ref)={g_ref}K == GREENHOUSE_K_DEFAULT={GREENHOUSE_K_DEFAULT}K")

    g_double = greenhouse_temperature(EARTH_GHG_REFERENCE_PPM * 2.0)
    expected_double = GREENHOUSE_K_DEFAULT + GREENHOUSE_SENSITIVITY_K_PER_DOUBLING_DEFAULT
    print(f"  dupla CO2-koncentracion: {g_double:.6f}K (vart kb. {expected_double}K)")
    assert abs(g_double - expected_double) < 1e-9, "CO2-dupolazodasnal pontosan +sensitivity-nek kellene lennie"
    print("OK - logaritmikus CO2-proxy dupolazodasnal a vart erteket adja\n")

    print("--- Klima-atlag (weather/cycle NELKUL) monotonitasa szelesseg szerint ---")
    climate_mean_by_lat = {}
    for lat_deg in [0, 15, 30, 45, 60, 75, 90]:
        lat = math.radians(lat_deg)
        pos = (math.cos(lat), 0.0, math.sin(lat))
        t_full = temperature_kelvin_full(
            demo_seed, pos, 0.0, orbital_period, rotation_period, axial_tilt,
            is_oceanic=False, elevation_m=0.0, sea_level_m=0.0,
            weather_amplitude_k=0.0,
            cycle_eccentricity_amplitude_k=0.0, cycle_obliquity_amplitude_k=0.0,
            cycle_precession_amplitude_k=0.0,
        )
        climate_mean_by_lat[lat_deg] = t_full
        print(f"  szelesseg={lat_deg:3d}°: T_full(klima-atlag)={t_full:.1f}K ({t_full - 273.15:+.1f}°C)")
    lats_sorted = sorted(climate_mean_by_lat)
    values_sorted = [climate_mean_by_lat[l] for l in lats_sorted]
    assert all(values_sorted[i] >= values_sorted[i + 1] for i in range(len(values_sorted) - 1)), \
        "A klima-atlagnak (weather/cycle nelkul) monoton csokkennie kell egyenlitotol polusig"
    print("OK - a strukturalis (radiative+greenhouse+ocean-altitude) tag monoton csokken\n")

    print("--- T_ocean: kontinentalitas - az ocean kisebb evszakos ingast ad, mint a szarazfold ---")
    lat_mid = math.radians(45.0)
    pos_mid = (math.cos(lat_mid), 0.0, math.sin(lat_mid))
    land_temps, ocean_temps = [], []
    for k in range(12):
        day_t = k * (orbital_period / 12.0)
        common_kwargs = dict(
            world_seed=demo_seed, tile_position=pos_mid, day_t=day_t,
            orbital_period=orbital_period, rotation_period=rotation_period, axial_tilt=axial_tilt,
            elevation_m=0.0, sea_level_m=0.0, weather_amplitude_k=0.0,
            cycle_eccentricity_amplitude_k=0.0, cycle_obliquity_amplitude_k=0.0,
            cycle_precession_amplitude_k=0.0,
        )
        land_temps.append(temperature_kelvin_full(is_oceanic=False, **common_kwargs))
        ocean_temps.append(temperature_kelvin_full(is_oceanic=True, **common_kwargs))

    def _variance(values):
        m = sum(values) / len(values)
        return sum((v - m) ** 2 for v in values) / len(values)

    land_var, ocean_var = _variance(land_temps), _variance(ocean_temps)
    print(f"  szarazfold evszakos szorasnegyzet: {land_var:.4f}, ocean: {ocean_var:.4f}")
    assert ocean_var < land_var, "Az ocean-tile-nak kisebb evszakos ingast (varianciat) kellene mutatnia"
    print("OK - az ocean-buffering erdemben csillapitja az evszakos ingast\n")

    print("--- T_weather: bekorlatozott devicio, nem irja felul a klimat (§32.3) ---")
    weather_devs = []
    for k in range(500):
        day_t = k * 3.7
        weather_devs.append(weather_deviation_k(demo_seed, pos_mid, day_t))
    mean_abs_dev = sum(abs(w) for w in weather_devs) / len(weather_devs)
    max_abs_dev = max(abs(w) for w in weather_devs)
    print(f"  n={len(weather_devs)}, atlagos |deviacio|={mean_abs_dev:.3f}K, max={max_abs_dev:.3f}K")
    typical_lat_spread = climate_mean_by_lat[0] - climate_mean_by_lat[90]
    print(f"  (osszehasonlitaskepp: egyenlito-polus klima-kulonbseg = {typical_lat_spread:.1f}K)")
    assert max_abs_dev < typical_lat_spread, \
        "A weather-deviacionak jol a klimatikus szelesseg-kulonbseg alatt kell maradnia (nem irhatja felul a klimat)"
    print("OK - a weather-deviacio bekorlatozott, additiv, nem dominans\n")

    print("--- T_cycle: seed-fuggo periodusok, hatarolt amplitudo ---")
    c1 = climate_cycle_temperature_k(demo_seed, 12345.0)
    c2 = climate_cycle_temperature_k(demo_seed, 12345.0)
    assert c1 == c2, "Nem tiszta fuggveny!"
    c_other_seed = climate_cycle_temperature_k(demo_seed + 1, 12345.0)
    assert c1 != c_other_seed, "Kulonbozo seed ugyanazt a ciklus-erteket adta - gyanus"
    max_possible = (CYCLE_ECCENTRICITY_AMPLITUDE_K_DEFAULT + CYCLE_OBLIQUITY_AMPLITUDE_K_DEFAULT
                    + CYCLE_PRECESSION_AMPLITUDE_K_DEFAULT)
    assert abs(c1) <= max_possible + 1e-9
    print(f"  T_cycle(t=12345 ev) = {c1:.4f}K (elmeleti maximum: +-{max_possible}K)")
    print("OK - determinisztikus, seed-fuggo, hatarolt amplitudo\n")

    print("--- Teljes modell tisztasaga (ismetelt hivas azonos) ---")
    full_a = temperature_kelvin_full(
        demo_seed, (0.5, 0.5, 0.7071), 42.0, orbital_period, rotation_period,
        axial_tilt, True, -1000.0, 0.0, t_years=5000.0,
    )
    full_b = temperature_kelvin_full(
        demo_seed, (0.5, 0.5, 0.7071), 42.0, orbital_period, rotation_period,
        axial_tilt, True, -1000.0, 0.0, t_years=5000.0,
    )
    assert full_a == full_b, "temperature_kelvin_full nem tiszta fuggveny!"
    print(f"OK - determinisztikus, T={full_a:.2f}K\n")

    print("--- Regi temperature_kelvin ERINTETLEN (150 vektor bitre azonos marad) ---")
    print("  (a temperature_vectors.json generalasa fentebb, valtozatlan kodveal tortent)")

    print("\n--- Tesztvektorok generalasa a teljes modellhez (C# port SZAMARA, KULON fajlba) ---")
    full_vectors = []
    gen_seed_full = 0xF011E0000000001
    for i in range(200):
        p = threefry4x64([i, 0, 0, 0], [gen_seed_full, 0, 41, 0], 20)
        lat_v = (p[0] % 1_000_000) / 1_000_000.0 * math.pi - math.pi / 2.0
        lon_v = (p[1] % 1_000_000) / 1_000_000.0 * 2.0 * math.pi
        day_t_v = (p[2] % 1_000_000) / 1_000_000.0 * orbital_period
        is_oceanic_v = (p[3] % 2) == 0
        elevation_v = ((p[3] >> 1) % 10000) - 4000.0

        extra = threefry4x64([i, 1, 0, 0], [gen_seed_full, 0, 41, 0], 20)
        ghg_ppm_v = 100.0 + (extra[0] % 1_000_000) / 1_000_000.0 * 900.0  # 100..1000 ppm
        t_years_v = (extra[1] % 1_000_000) / 1_000_000.0 * 1_000_000.0  # 0..1e6 ev
        world_seed_v = (0xA000000000000000 | (extra[2] & 0x0FFFFFFFFFFFFFFF))

        pos_v = (math.cos(lat_v) * math.cos(lon_v), math.cos(lat_v) * math.sin(lon_v), math.sin(lat_v))
        t_full_v = temperature_kelvin_full(
            world_seed_v, pos_v, day_t_v, orbital_period, rotation_period, axial_tilt,
            is_oceanic_v, elevation_v, sea_level_m=0.0,
            ghg_ppm=ghg_ppm_v, t_years=t_years_v,
        )
        full_vectors.append({
            "worldSeed": world_seed_v, "x": pos_v[0], "y": pos_v[1], "z": pos_v[2],
            "dayT": day_t_v, "orbitalPeriod": orbital_period,
            "rotationPeriod": rotation_period, "axialTilt": axial_tilt,
            "isOceanic": is_oceanic_v, "elevationM": elevation_v, "seaLevelM": 0.0,
            "ghgPpm": ghg_ppm_v, "referenceGhgPpm": EARTH_GHG_REFERENCE_PPM,
            "greenhouseK": GREENHOUSE_K_DEFAULT,
            "oceanBufferingStrength": OCEAN_BUFFERING_STRENGTH_DEFAULT,
            "weatherAmplitudeK": WEATHER_AMPLITUDE_K_DEFAULT,
            "tYears": t_years_v,
            "cycleEccentricityAmplitudeK": CYCLE_ECCENTRICITY_AMPLITUDE_K_DEFAULT,
            "cycleObliquityAmplitudeK": CYCLE_OBLIQUITY_AMPLITUDE_K_DEFAULT,
            "cyclePrecessionAmplitudeK": CYCLE_PRECESSION_AMPLITUDE_K_DEFAULT,
            "temperatureK": t_full_v,
        })

    with open("temperature_full_vectors.json", "w", newline="\n") as f:
        json.dump({
            "note": "A spec §28.1 teljes egyenlete (temperature_kelvin_full) - "
                     "MEG NINCS C#-portolva (ld. ND-42, docs/04-decisions.md). "
                     "KULON fajl a regi temperature_vectors.json-tol, hogy a "
                     "meglevo C# Temperature.cs mero-tesztjei ne torjenek el.",
            "vectors": full_vectors,
        }, f, indent=1)
    print(f"{len(full_vectors)} teljes-modell homerseklet-tesztvektor generalva (temperature_full_vectors.json)")
