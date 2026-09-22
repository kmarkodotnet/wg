"""
Szel + nedvesseg/csapadek + idojaras-zaj referencia-implementacioja M5-hoz
(docs/00-spec-v1.0.md SS30 "Szel", SS31 "Nedvesseg es csapadek", SS32
"Idojaras").

HATOKOR es MODSZERTAN (ND-41, docs/04-decisions.md - IDE dokumentalva
minden olyan pont, ahol a spec csak minosegi leirast ad, zart keplet
nelkul):

A spec SS30-32 NEM ad zart formulat - csak minosegi komponenslistat
("Evaporation = f(temperature, wind, surfaceWater)", "moist air ->
mountain -> uplift -> precipitation", "W(p,t) = Noise(Warp(p,t), seed)").
Ez a modul egy dokumentaltan EGYSZERUSITETT, de fizikailag plauzibilis,
DETERMINISZTIKUS elso kort valosit meg - nem klimafizikai pontossag a
cel, hanem tesztelheto tiszta fuggvenyek, kesobb finomithato parameterekkel
(ugyanaz a minta, mint temperature_ref.py-ban, ld. ND-10/ND-26/ND-27).

FONTOS TERVEZESI DONTES: ez a modul NEM fer hozza a tile-racshoz/szomszed-
tablahoz (az egy kesobbi, motor-oldali integracios lepes). Ezert minden,
ami "szomszed tile adatatol fugg" (homerseklet-gradiens, elevacio-gradiens,
felso-szelbol erkezo nedvesseg), vagy (a) veges differenciakent SZAMOLT
a mar meglevo, VERIFIKALT temperature_kelvin fuggvenybol a lokalis erinto-
sikban (homerseklet-gradiens - ezt kifejezetten a feladat kerte), vagy
(b) KOZVETLEN BEMENETKENT VART (elevacio-gradiens, bejovo nedvesseg) -
ugyanaz a minta, mint ahogy temperature_ref.py is kozvetlen bemenetkent
varja az elevation_m/sea_level_m erteket ahelyett, hogy ujraszamolna a
kereg-modellt. Ez kesobb, a tenyleges racs-integraciokor konnyen
csatolhato a valodi szomszed-lekerdezeshez.

A NEGY FEDETT RESZ:

1. WindVector (SS30): szelessegi alapcellak (Hadley/Ferrel/Polar proxy,
   haromsavos, szog-fuggo eloszlassal) + Coriolis-proxy (felteke-fuggo
   forgatas a termikus szelkomponensen) + homersekleti nyomasgradiens
   (temperature_kelvin veges differenciaja a lokalis erinto-sikban) +
   hegyek elteri`to hatasa (elevacio-gradiens-alapu "into-hill" komponens
   csokkentes, tanh-korlatozott ero"sseggel).

   DONTES - haromsavos index alakja: haromszog-hullam (0 -> csucs -> 0)
   minden 30 fokos savban, ELOJELLEL valtakozva (Hadley: negativ/keleti,
   Ferrel: pozitiv/nyugati, Polar: negativ/keleti). Ez NEM tetszoleges:
   a nullatmenetek pontosan 0/30/60/90 foknal vannak, ami kvalitativan
   megfelel a valodi "csendovek" (doldrums ~0 fok, lo-szelessegek ~30 fok,
   szubpolaris melynyomas ~60 fok) es "szelovek" (passzat, nyugati szelek,
   polaris keleti szelek) valtakozasanak - nem meresi ertek, hanem
   kvalitativ proxy, dokumentalva.

   DONTES - Coriolis-proxy formaja: a klasszikus geosztrofikus kep szerint
   a nyomasgradiens-vezerelte szelet a Coriolis-ero JOBBRA teriti el az
   E-fel-tekén, BALRA a D-fel-tekén. Ezt egy FIX szogu forgatassal
   kozelitjuk a termikus szelkomponensen (nem az egesz szelvektoron - a
   szelessegi alapcellak iranya mar ugyis felteke-szimmetrikus a sajat
   haromsavos elojel-valtasa miatt, ld. fent). A forgas szoge
   CORIOLIS_DEFLECTION_DEG_DEFAULT (illusztrativ ertek, NEM meres),
   elojele -sign(szelesseg).

   DONTES - homerseklet-gradiens szamitasa: veges differencia a lokalis
   kelet/eszak erinto-iranyban, GRADIENT_EPS radian lepessel, a hivo altal
   fix is_oceanic/elevation_m ertekkel (a szomszed tile-ok tenyleges
   ocean/elevacio erteket NEM ismerjuk ebben az orakulumban - dokumentalt
   egyszerusites, ld. fent).

   DONTES - termikus komponens korlatozasa (ND-126b): a ketdimenzios
   termikus vektor iranyat megtartva a nagysagat
   30 m/s * tanh(|v| / 30 m/s) alakban korlatozzuk. Ez sima, nem hoz letre
   kemeny clamp-hatart, es a zonalis alapkomponenst nem vagja le.

   DONTES - hegyi elteri`tes: az elevacio-gradiens IRANYABA eso szelkomponenst
   (azaz a "feldomb" iranyu reszt) csokkentjuk egy tanh-tel korlatozott
   arannyal (MOUNTAIN_DEFLECTION_MAX_DEFAULT), a lejto"vel parhuzamos
   ("kontur menti") komponenst valtozatlanul hagyva - ez a "szel megkeruli
   a hegyet" viselkedes egyszeru proxy-ja anelkul, hogy teljes folyadek-
   dinamikai szimulaciot igenyelne.

2. Evaporation (SS31): f(homerseklet, szel, felszini viz) - monoton no"
   a homerseklettel (fagypont felett linearisan telitodik), a szelsebesseggel
   (korlatozott egyutthatoval) es a felszini viz-hanyaddal (0..1).

3. Csapadek (SS31): alap nedvessegtranszport (bejovo nedvesseg + lokalis
   parolgas osszege) SZOROZVA egy orografikus tenyezovel, ami a
   szel-vektor es az elevacio-gradiens SKALARIS SZORZATABOL (uplift-
   proxy) szamolt, tanh-korlatozott tenyezo: pozitiv (feldomb-iranyu szel,
   "uplift") -> tobb csapadek; negativ (lejto"-iranyu szel, "eso-arnyek"/
   rain shadow) -> kevesebb csapadek.

4. Idojaras-zaj (SS32): W(p,t) = Noise(Warp(p,t), seed) - a MAR VERIFIKALT
   noise_ref.fbm es domain_warp_ref.warp_position UJRAFELHASZNALASAVAL
   (nincs uj zaj-algoritmus). Az ido-fuggest egy FIX, tetszolegesen
   valasztott "sodrasi" iranyban (WEATHER_TIME_DRIFT_AXIS) valo lassu
   pozicio-eltolassal valositjuk meg - igy W(p,t) STATELESS (barmely
   (p,t) parra kozvetlenul kiertekelheto, nincs szimulacios lepeskenyszer).

   DONTES - korlatozas modja (SS32.3, "a weather noise nem irhatja felul
   a klimat"): TANH, nem kemeny CLAMP. A noise_ref.fbm sajat plauzibilitas-
   tesztje szerint ([-1.5,1.5] koruli tartomany, ld. noise_ref.py __main__)
   ritkan enyhen tullephet a [-1,1] tartomanyt. Kemeny clamp() eseten ez
   egy LATHATO, ELES "plafon"-t adna a devicaio-mezoben (mestersegesen
   lapos foltok, ahol a zaj epp tullepte a hatart) - a tanh() ehelyett
   SIMAN, aszimptotikusan telitodik, nincs elesseg-artefaktum, es
   |tanh(x)| < 1 barmely veges x-re, tehat a deviacio SZIGORUAN korlatos
   marad a klimatikus atlag korul, ahogy az SS32.3 kifejezetten megkoveteli.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math

import deterministic_math_ref as dm
from temperature_ref import temperature_kelvin
from noise_ref import fbm
from domain_warp_ref import warp_position

# ---------------------------------------------------------------------------
# 1. Szel (SS30)
# ---------------------------------------------------------------------------

BASE_WIND_SPEED_DEFAULT = 10.0        # m/s, illusztrativ (foldi passzat-nagysagrend)
CORIOLIS_DEFLECTION_DEG_DEFAULT = 30.0  # fok, illusztrativ proxy-szog, NEM meres
GRADIENT_EPS = 1.0e-3                  # radian, veges differencia lepeskoz
THERMAL_WIND_COEFF_DEFAULT = 0.5       # m/s per (K/radian), illusztrativ skalazas
THERMAL_WIND_LIMIT_DEFAULT = 30.0       # m/s, ND-126b sima termikus komponens-korlat
MOUNTAIN_DEFLECTION_MAX_DEFAULT = 0.85  # a "feldomb" szelkomponens max. csokkentesi aranya
MOUNTAIN_SLOPE_SCALE_DEFAULT = 0.5     # meredekseg-proxy skala (ld. lent, dimenziomentes)


def _normalize(v):
    x, y, z = v
    length = math.sqrt(x * x + y * y + z * z)
    if length < 1e-12:
        return (0.0, 0.0, 0.0)
    return (x / length, y / length, z / length)


def _limit_thermal_wind(east, north, limit=THERMAL_WIND_LIMIT_DEFAULT):
    if not (limit > 0.0) or math.isinf(limit):
        raise ValueError("thermal wind limit must be positive and finite")
    magnitude = math.sqrt(east * east + north * north)
    if magnitude < 1.0e-12:
        return east, north
    limited = limit * dm.tanh(magnitude / limit)
    scale = limited / magnitude
    return east * scale, north * scale


def _add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _scale(v, s):
    return (v[0] * s, v[1] * s, v[2] * s)


def local_east_north(position):
    """Lokalis (kelet, eszak) erinto-egysegvektor-par egy gomb-egysegvektor
    pozicioban. Konvencio: Z tengely = polus-tengely (ugyanaz, mint
    temperature_ref/astronomy_ref: lat = asin(z), lon = atan2(y, x)) -
    ez a projekt mar hasznalt, kovetkezetes szelesseg/hosszusag-konvencioja."""
    x, y, z = position
    lat = math.asin(max(-1.0, min(1.0, z)))
    lon = math.atan2(y, x)
    sin_lat, cos_lat = math.sin(lat), math.cos(lat)
    sin_lon, cos_lon = math.sin(lon), math.cos(lon)
    east = (-sin_lon, cos_lon, 0.0)
    north = (-sin_lat * cos_lon, -sin_lat * sin_lon, cos_lat)
    return east, north


def _zonal_band_index(lat):
    """Haromsavos (Hadley/Ferrel/Polar) haromszog-hullam index, [-1,1].
    Negativ = keleti (passzat/polaris keleti szel), pozitiv = nyugati
    (kozepszelessegi nyugati szelek). Nullatmenet pontosan 0/30/60/90
    foknal - ld. modul-doc "DONTES - haromsavos index alakja"."""
    lat_deg = abs(math.degrees(lat))
    if lat_deg <= 30.0:
        t = lat_deg / 30.0
        return -math.sin(math.pi * t)
    elif lat_deg <= 60.0:
        t = (lat_deg - 30.0) / 30.0
        return math.sin(math.pi * t)
    else:
        t = min(1.0, (lat_deg - 60.0) / 30.0)
        return -math.sin(math.pi * t)


def _rotate2(u, v, angle):
    c, s = math.cos(angle), math.sin(angle)
    return u * c - v * s, u * s + v * c


def temperature_gradient_tangent(
    position, day_t, orbital_period, rotation_period, axial_tilt,
    is_oceanic, elevation_m, sea_level_m,
    orbital_phase0=0.0, rotation_phase0=0.0, eps=GRADIENT_EPS,
):
    """dT/d(kelet), dT/d(eszak) - kozponti veges differencia a
    temperature_kelvin mar verifikalt fuggvenyebol, K/radian egysegben."""
    east, north = local_east_north(position)

    def temp_at(p):
        return temperature_kelvin(
            p, day_t, orbital_period, rotation_period, axial_tilt,
            is_oceanic, elevation_m, sea_level_m,
            orbital_phase0, rotation_phase0,
        )

    pe_plus = _normalize(_add(position, _scale(east, eps)))
    pe_minus = _normalize(_add(position, _scale(east, -eps)))
    pn_plus = _normalize(_add(position, _scale(north, eps)))
    pn_minus = _normalize(_add(position, _scale(north, -eps)))

    d_east = (temp_at(pe_plus) - temp_at(pe_minus)) / (2.0 * eps)
    d_north = (temp_at(pn_plus) - temp_at(pn_minus)) / (2.0 * eps)
    return d_east, d_north


def _apply_mountain_deflection(wind_e, wind_n, grad_e, grad_n, max_fraction, slope_scale):
    """A szelvektor "feldomb"-iranyu komponenset csokkenti (a "kontur menti"
    komponens erintetlen marad) - ld. modul-doc "DONTES - hegyi elteri`tes"."""
    slope_mag = math.sqrt(grad_e * grad_e + grad_n * grad_n)
    if slope_mag < 1e-12:
        return wind_e, wind_n
    uphill_e, uphill_n = grad_e / slope_mag, grad_n / slope_mag
    wind_along_uphill = wind_e * uphill_e + wind_n * uphill_n
    if wind_along_uphill <= 0.0:
        return wind_e, wind_n  # a szel mar nem "feldomb" iranyu, nincs mit elteriteni
    deflect_fraction = max_fraction * math.tanh(slope_mag / slope_scale)
    reduction = deflect_fraction * wind_along_uphill
    return wind_e - reduction * uphill_e, wind_n - reduction * uphill_n


def wind_vector(
    position, day_t, orbital_period, rotation_period, axial_tilt,
    is_oceanic, elevation_m, sea_level_m,
    elevation_gradient_east, elevation_gradient_north,
    orbital_phase0=0.0, rotation_phase0=0.0,
    base_wind_speed=BASE_WIND_SPEED_DEFAULT,
    coriolis_deflection_deg=CORIOLIS_DEFLECTION_DEG_DEFAULT,
    thermal_wind_coeff=THERMAL_WIND_COEFF_DEFAULT,
    mountain_deflection_max=MOUNTAIN_DEFLECTION_MAX_DEFAULT,
    mountain_slope_scale=MOUNTAIN_SLOPE_SCALE_DEFAULT,
    thermal_wind_limit=THERMAL_WIND_LIMIT_DEFAULT,
):
    """A tile WindVector-je: (kelet, eszak) erinto-sik komponens + a
    megfelelo 3D erinto-vektor a gomb-feluleten. elevation_gradient_east/
    north: dimenziomentes lejto-proxy (rise/run-szeru, tipikus plauzibilis
    tartomany kb. [-2,2] - meredek hegy kb. 1, sik terep kb. 0), KOZVETLEN
    BEMENET (ld. modul-doc)."""
    lat = math.asin(max(-1.0, min(1.0, position[2])))

    zonal = base_wind_speed * _zonal_band_index(lat)
    base_east, base_north = zonal, 0.0

    grad_e, grad_n = temperature_gradient_tangent(
        position, day_t, orbital_period, rotation_period, axial_tilt,
        is_oceanic, elevation_m, sea_level_m, orbital_phase0, rotation_phase0,
    )
    thermal_e_raw = grad_e * thermal_wind_coeff
    thermal_n_raw = grad_n * thermal_wind_coeff
    thermal_e_raw, thermal_n_raw = _limit_thermal_wind(
        thermal_e_raw, thermal_n_raw, thermal_wind_limit,
    )

    coriolis_angle = -math.copysign(1.0, lat) * math.radians(coriolis_deflection_deg)
    thermal_e, thermal_n = _rotate2(thermal_e_raw, thermal_n_raw, coriolis_angle)

    combined_e = base_east + thermal_e
    combined_n = base_north + thermal_n

    final_e, final_n = _apply_mountain_deflection(
        combined_e, combined_n, elevation_gradient_east, elevation_gradient_north,
        mountain_deflection_max, mountain_slope_scale,
    )

    east_vec, north_vec = local_east_north(position)
    wind_3d = (
        final_e * east_vec[0] + final_n * north_vec[0],
        final_e * east_vec[1] + final_n * north_vec[1],
        final_e * east_vec[2] + final_n * north_vec[2],
    )
    return final_e, final_n, wind_3d


# ---------------------------------------------------------------------------
# 2. Parolgas (SS31)
# ---------------------------------------------------------------------------

EVAP_FREEZE_K = 273.15       # fagypont, Kelvinben
EVAP_TEMP_RANGE_K = 40.0     # ennyi K-lel fagypont felett teli`todik a homerseklet-tenyezo
EVAP_WIND_COEFF = 0.05       # tovabbi parolgas-noves egyseg szelsebessegenkent (m/s)
EVAP_WIND_CAP = 30.0         # m/s, e felett a szel-hatas mar nem no tovabb
EVAP_BASE_RATE = 5.0         # mm/nap, illusztrativ csucsertek (meleg, szeles, nyilt viz)


def _clamp01(v):
    return max(0.0, min(1.0, v))


def evaporation(temperature_k, wind_speed, surface_water_fraction, base_rate=EVAP_BASE_RATE):
    """Evaporation = f(temperature, wind, surfaceWater), SS31.
    Monoton no mindharom bemenettel; fagypont alatt (kb.) nulla."""
    water_avail = _clamp01(surface_water_fraction)
    temp_factor = _clamp01((temperature_k - EVAP_FREEZE_K) / EVAP_TEMP_RANGE_K)
    wind_factor = 1.0 + EVAP_WIND_COEFF * min(max(0.0, wind_speed), EVAP_WIND_CAP)
    return base_rate * water_avail * temp_factor * wind_factor


# ---------------------------------------------------------------------------
# 3. Csapadek (SS31): orografikus komponens + alap nedvessegtranszport
# ---------------------------------------------------------------------------

OROGRAPHIC_COEFF_DEFAULT = 0.85   # max. +-85% modositas az uplift/rain-shadow miatt
OROGRAPHIC_SCALE_DEFAULT = 5.0    # uplift-proxy skala (szel . gradiens mertekegyseg-fuggo)
PRECIP_COEFF_DEFAULT = 1.0        # mm/nap per mm/nap "elerheto nedvesseg" egyseg


def precipitation(
    evaporation_local, incoming_moisture, wind_east, wind_north,
    elevation_gradient_east, elevation_gradient_north,
    orographic_coeff=OROGRAPHIC_COEFF_DEFAULT,
    orographic_scale=OROGRAPHIC_SCALE_DEFAULT,
    precip_coeff=PRECIP_COEFF_DEFAULT,
):
    """Csapadek = (bejovo nedvesseg + lokalis parolgas) * orografikus
    tenyezo. Az orografikus tenyezo a szelvektor es az elevacio-gradiens
    skalaris szorzatabol (uplift-proxy) szamolt, tanh-korlatozott
    tenyezo: pozitiv (feldomb-iranyu szel) -> tobb csapadek (uplift),
    negativ (lejto-iranyu szel) -> kevesebb csapadek (rain shadow)."""
    moisture_available = max(0.0, incoming_moisture) + max(0.0, evaporation_local)
    uplift = wind_east * elevation_gradient_east + wind_north * elevation_gradient_north
    orographic_factor = 1.0 + orographic_coeff * math.tanh(uplift / orographic_scale)
    orographic_factor = max(0.0, orographic_factor)
    return precip_coeff * moisture_available * orographic_factor


# ---------------------------------------------------------------------------
# 4. Idojaras-zaj (SS32): W(p,t) = Noise(Warp(p,t), seed)
# ---------------------------------------------------------------------------

WEATHER_TIME_DRIFT_AXIS = _normalize((0.6, 0.8, 0.0))  # onkenyes, fix "sodrasi" irany
WEATHER_TIME_SPEED = 0.01        # radian/nap, illusztrativ, tetszolegesen valasztott
WEATHER_WARP_STRENGTH = 0.6
WEATHER_WARP_FREQUENCY = 6.0     # a domain_warp_ref-nel (2.0) finomabb, "viharcella"-skala
WEATHER_WARP_OCTAVES = 2
WEATHER_NOISE_FREQUENCY = 10.0
WEATHER_NOISE_OCTAVES = 3

# Decorrelacios eltolasok a kulonbozo idojaras-tulajdonsagokhoz (ugyanaz a
# minta, mint domain_warp_ref OFFSET_DX/DY/DZ-je: onkenyes, nullatol es
# egymastol tavoli konstansok, hogy a ket fbm-kiertekeles ne essen egybe).
WEATHER_PROPERTY_OFFSET_TEMPERATURE = (0.0, 0.0, 0.0)
WEATHER_PROPERTY_OFFSET_PRECIP = (31.4, 27.1, 19.9)

MAX_WEATHER_TEMP_DEVIATION_K = 5.0          # ld. SS32.3 peldaja (-4C), ennel tagabb sav
MAX_WEATHER_PRECIP_DEVIATION_FRACTION = 0.7  # csapadek 30%-170%-a a klimatikus atlagnak


def weather_noise_raw(world_seed, position, t, property_offset=(0.0, 0.0, 0.0)):
    """W(p,t) = Noise(Warp(p,t), seed) - stateless, tetszoleges (p,t)-re
    kozvetlenul kiertekelheto. A mar verifikalt warp_position/fbm
    ujrafelhasznalasaval, uj hash-algoritmus nelkul."""
    shifted = _normalize(_add(position, _scale(WEATHER_TIME_DRIFT_AXIS, WEATHER_TIME_SPEED * t)))
    wx, wy, wz = warp_position(
        world_seed, shifted,
        strength=WEATHER_WARP_STRENGTH, frequency=WEATHER_WARP_FREQUENCY, octaves=WEATHER_WARP_OCTAVES,
    )
    ox, oy, oz = property_offset
    return fbm(
        world_seed, wx + ox, wy + oy, wz + oz,
        base_frequency=WEATHER_NOISE_FREQUENCY, octaves=WEATHER_NOISE_OCTAVES,
    )


def weather_temperature_deviation_k(world_seed, position, t, max_deviation_k=MAX_WEATHER_TEMP_DEVIATION_K):
    """SS32.3: korlatos deviacio a klimatikus atlag korul - tanh-tel
    korlatozva (ld. modul-doc "DONTES - korlatozas modja")."""
    raw = weather_noise_raw(world_seed, position, t, WEATHER_PROPERTY_OFFSET_TEMPERATURE)
    return max_deviation_k * math.tanh(raw)


def weather_precipitation_multiplier(world_seed, position, t, max_deviation_fraction=MAX_WEATHER_PRECIP_DEVIATION_FRACTION):
    raw = weather_noise_raw(world_seed, position, t, WEATHER_PROPERTY_OFFSET_PRECIP)
    return 1.0 + max_deviation_fraction * math.tanh(raw)


def current_temperature_k(climate_mean_temperature_k, world_seed, position, t):
    """CurrentTemperature = ClimateMeanTemperature + WeatherDeviation, SS32.3."""
    return climate_mean_temperature_k + weather_temperature_deviation_k(world_seed, position, t)


def current_precipitation(climate_mean_precipitation, world_seed, position, t):
    return climate_mean_precipitation * weather_precipitation_multiplier(world_seed, position, t)


if __name__ == "__main__":
    import json
    from threefry_ref import threefry4x64

    orbital_period = 365.25
    rotation_period = 1.0
    axial_tilt = math.radians(23.44)
    world_seed = 0xA7C944210000

    def pos_at_lat(lat_deg, lon_deg=0.0):
        lat, lon = math.radians(lat_deg), math.radians(lon_deg)
        return (math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat))

    print("--- 1. Szel: haromsavos zonalis index plauzibilitasa ---")
    for lat_deg, expect in [(0.0, 0.0), (15.0, -1.0), (30.0, 0.0), (45.0, 1.0),
                             (60.0, 0.0), (75.0, -1.0), (90.0, 0.0)]:
        idx = _zonal_band_index(math.radians(lat_deg))
        print(f"  lat={lat_deg:5.1f} deg: index={idx:+.4f} (vart elojel/csucs: {expect:+.1f})")
        assert abs(idx - expect) < 1e-9, f"zonal index nem egyezik a vart ertekkel lat={lat_deg}"
    print("OK - Hadley(negativ)/Ferrel(pozitiv)/Polar(negativ) valtakozas, nullatmenet 0/30/60/90-nel\n")

    print("--- 2. Coriolis-proxy: felteke-fuggo iranyvaltas ---")
    angle_north = -math.copysign(1.0, math.radians(30.0)) * math.radians(CORIOLIS_DEFLECTION_DEG_DEFAULT)
    angle_south = -math.copysign(1.0, math.radians(-30.0)) * math.radians(CORIOLIS_DEFLECTION_DEG_DEFAULT)
    print(f"  E-felteke (lat=+30): forgatasi szog={math.degrees(angle_north):+.1f} deg")
    print(f"  D-felteke (lat=-30): forgatasi szog={math.degrees(angle_south):+.1f} deg")
    assert angle_north == -angle_south, "A Coriolis-proxy szognek pontosan ellentetesnek kell lennie a ket felteken"
    print("OK - a ket felteke forgatasi szoge pontosan ellentetes elojelu\n")

    print("--- 3. Hegyi elteri`tes plauzibilitasa ---")
    # Szel egyenesen "feldomb" iranyba fuj (kelet fele, a gradiens is kelet fele mutat)
    wind_e_before, wind_n_before = 10.0, 0.0
    e_flat, n_flat = _apply_mountain_deflection(wind_e_before, wind_n_before, 0.0, 0.0, MOUNTAIN_DEFLECTION_MAX_DEFAULT, MOUNTAIN_SLOPE_SCALE_DEFAULT)
    e_steep, n_steep = _apply_mountain_deflection(wind_e_before, wind_n_before, 1.0, 0.0, MOUNTAIN_DEFLECTION_MAX_DEFAULT, MOUNTAIN_SLOPE_SCALE_DEFAULT)
    print(f"  sik terepen: szel=({e_flat:.3f},{n_flat:.3f})  meredek hegy feldomb-iranyban: szel=({e_steep:.3f},{n_steep:.3f})")
    assert e_flat == wind_e_before, "Sik terepen (nulla gradiens) a szelnek valtozatlannak kell maradnia"
    assert e_steep < wind_e_before, "Feldomb iranyu szelnek csokkennie kell meredek hegynel"
    # Szel a hegyto"l elfele (lejto-iranyban) fuj - nem szabad elteri`teni
    e_downhill, n_downhill = _apply_mountain_deflection(-10.0, 0.0, 1.0, 0.0, MOUNTAIN_DEFLECTION_MAX_DEFAULT, MOUNTAIN_SLOPE_SCALE_DEFAULT)
    assert e_downhill == -10.0, "Lejto-iranyu szelnek nem szabad elteri`tve lennie"
    print("OK - feldomb-iranyu szel csokken meredek terepen, lejto-iranyu szel valtozatlan\n")

    print("--- 4. Teljes wind_vector: determinizmus (tiszta fuggveny) ---")
    pos = pos_at_lat(35.0, 10.0)
    args = (pos, 12.5, orbital_period, rotation_period, axial_tilt, False, 800.0, 0.0, 0.3, -0.1)
    w1 = wind_vector(*args)
    w2 = wind_vector(*args)
    assert w1 == w2, "A wind_vector nem tiszta fuggveny!"
    print(f"  wind_vector(35N,10E) = east={w1[0]:.4f} north={w1[1]:.4f} 3D={tuple(round(c,4) for c in w1[2])}")
    print("OK - ismetelt hivas bitre azonos eredmenyt ad\n")

    print("--- 5. Parolgas plauzibilitasa (monotonitas) ---")
    e_cold = evaporation(260.0, 5.0, 1.0)
    e_warm = evaporation(305.0, 5.0, 1.0)
    assert e_warm > e_cold, "A parolgasnak nonie kell a homerseklettel"
    e_calm = evaporation(305.0, 0.0, 1.0)
    e_windy = evaporation(305.0, 20.0, 1.0)
    assert e_windy > e_calm, "A parolgasnak nonie kell a szelsebesseggel"
    e_dry = evaporation(305.0, 5.0, 0.0)
    e_wet = evaporation(305.0, 5.0, 1.0)
    assert e_wet > e_dry, "A parolgasnak nonie kell a felszini viz-hanyaddal"
    e_frozen = evaporation(EVAP_FREEZE_K - 10.0, 5.0, 1.0)
    print(f"  hideg(260K)={e_cold:.3f}  meleg(305K)={e_warm:.3f}  szelcsend={e_calm:.3f}  szeles={e_windy:.3f}  szaraz={e_dry:.3f}  nedves={e_wet:.3f}  fagypont-alatt={e_frozen:.3f}")
    assert e_frozen == 0.0, "Fagypont alatt (a modell szerint) nem lehet parolgas"
    print("OK - a parolgas monoton no homerseklettel/szellel/felszini vizzel, fagypont alatt nulla\n")

    print("--- 6. Csapadek: orografikus komponens es 'rain shadow' ---")
    evap_local, incoming = 2.0, 3.0
    # Szel es gradiens egybevago (feldomb, uplift) vs. szemben allo (lejto, rain shadow)
    precip_windward = precipitation(evap_local, incoming, wind_east=1.0, wind_north=0.0,
                                     elevation_gradient_east=1.0, elevation_gradient_north=0.0)
    precip_leeward = precipitation(evap_local, incoming, wind_east=1.0, wind_north=0.0,
                                    elevation_gradient_east=-1.0, elevation_gradient_north=0.0)
    precip_flat = precipitation(evap_local, incoming, wind_east=1.0, wind_north=0.0,
                                 elevation_gradient_east=0.0, elevation_gradient_north=0.0)
    print(f"  szelfogo (windward) oldal: {precip_windward:.4f} mm/nap")
    print(f"  sik terep (nincs domborzat-hatas): {precip_flat:.4f} mm/nap")
    print(f"  eso-arnyek (leeward/rain shadow) oldal: {precip_leeward:.4f} mm/nap")
    assert precip_windward > precip_flat > precip_leeward, "Szelfogo oldal > sik > eso-arnyek oldal kell legyen"
    assert precip_leeward >= 0.0, "A csapadek soha nem lehet negativ"
    print("OK - a szelfogo oldalon tobb, az eso-arnyekban kevesebb csapadek hullik, mint sik terepen\n")

    print("--- 7. Idojaras-zaj: hatarossag es a klima nem-felulirasa (SS32.3) ---")
    sample_pos = pos_at_lat(48.0, 19.0)
    climate_mean_t = 18.0  # C, csak a peldahoz (a spec SS32.3 pontosan ezt hasznalja)
    devs = []
    for i in range(4000):
        t = i * 3.7
        dev = weather_temperature_deviation_k(world_seed, sample_pos, t)
        devs.append(dev)
        assert abs(dev) < MAX_WEATHER_TEMP_DEVIATION_K, "A homerseklet-deviacio tullepte a korlatot!"
    print(f"  {len(devs)} mintavett idopont, deviacio tartomany: [{min(devs):.3f}, {max(devs):.3f}] K (korlat: +-{MAX_WEATHER_TEMP_DEVIATION_K})")
    # Stateless: ugyanaz a (p,t) mindig ugyanazt az erteket adja, fuggetlenul a hivasi sorrendtol
    a = weather_temperature_deviation_k(world_seed, sample_pos, 100.0)
    b = weather_temperature_deviation_k(world_seed, sample_pos, 100.0)
    assert a == b, "A weather noise nem stateless/tiszta fuggveny!"
    print("OK - a homerseklet-deviacio szigoruan a korlaton belul marad, stateless\n")

    mults = []
    for i in range(4000):
        t = i * 5.3
        m = weather_precipitation_multiplier(world_seed, sample_pos, t)
        mults.append(m)
        lo = 1.0 - MAX_WEATHER_PRECIP_DEVIATION_FRACTION
        hi = 1.0 + MAX_WEATHER_PRECIP_DEVIATION_FRACTION
        assert lo < m < hi, "A csapadek-szorzo tullepte a korlatot!"
    print(f"  csapadek-szorzo tartomany: [{min(mults):.3f}, {max(mults):.3f}] (korlat: [{1 - MAX_WEATHER_PRECIP_DEVIATION_FRACTION:.2f}, {1 + MAX_WEATHER_PRECIP_DEVIATION_FRACTION:.2f}])")
    print("OK - a csapadek-szorzo szigoruan a korlaton belul marad\n")

    print("--- SS32.3 konkret pelda-alaku ellenorzes ---")
    # Nem varjuk el pontosan a -4C-t (az a spec illusztraciaja, nem egy
    # meghatarozott tesztvektor), csak azt, hogy a keplet alakja stimmel:
    # CurrentTemperature = ClimateMeanTemperature + WeatherDeviation.
    ct = current_temperature_k(climate_mean_t, world_seed, sample_pos, 42.0)
    wd = weather_temperature_deviation_k(world_seed, sample_pos, 42.0)
    assert abs(ct - (climate_mean_t + wd)) < 1e-12
    print(f"  ClimateMeanTemperature={climate_mean_t:.1f}  WeatherDeviation={wd:+.3f}  CurrentTemperature={ct:.3f}")
    print("OK - a keplet pontosan az SS32.3 alakjat koveti\n")

    print("--- Tesztvektorok generalasa a C# porthoz ---")
    wind_vectors = []
    gen_seed = 0x5E17D0000000001
    for i in range(300):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 30, 0], 20)
        lat = (p[0] % 1_000_000) / 1_000_000.0 * math.pi - math.pi / 2.0
        lon = (p[1] % 1_000_000) / 1_000_000.0 * 2.0 * math.pi
        day_t = (p[2] % 1_000_000) / 1_000_000.0 * orbital_period
        is_oceanic = (p[3] % 2) == 0
        elevation_m = ((p[3] >> 1) % 9000) - 3000.0
        p2 = threefry4x64([i, 1, 0, 0], [gen_seed, 0, 30, 0], 20)
        grad_e = ((p2[0] % 2_000_001) / 1_000_000.0) - 1.0  # [-1,1]
        grad_n = ((p2[1] % 2_000_001) / 1_000_000.0) - 1.0  # [-1,1]

        pos = (math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat))
        east_c, north_c, wind3d = wind_vector(
            pos, day_t, orbital_period, rotation_period, axial_tilt,
            is_oceanic, elevation_m, 0.0, grad_e, grad_n,
        )
        wind_vectors.append({
            "x": pos[0], "y": pos[1], "z": pos[2],
            "dayT": day_t, "orbitalPeriod": orbital_period,
            "rotationPeriod": rotation_period, "axialTilt": axial_tilt,
            "isOceanic": is_oceanic, "elevationM": elevation_m, "seaLevelM": 0.0,
            "elevationGradientEast": grad_e, "elevationGradientNorth": grad_n,
            "windEast": east_c, "windNorth": north_c, "wind3d": list(wind3d),
        })

    evap_precip_vectors = []
    for i in range(300):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 31, 0], 20)
        temp_k = 230.0 + (p[0] % 1_000_000) / 1_000_000.0 * 90.0  # 230..320K
        wind_speed = (p[1] % 1_000_000) / 1_000_000.0 * 35.0      # 0..35 m/s
        water_frac = (p[2] % 1_000_000) / 1_000_000.0             # 0..1
        p2 = threefry4x64([i, 1, 0, 0], [gen_seed, 0, 31, 0], 20)
        incoming = (p2[0] % 1_000_000) / 1_000_000.0 * 10.0       # 0..10 mm/nap
        w_e = ((p2[1] % 2_000_001) / 1_000_000.0 - 1.0) * 20.0    # [-20,20]
        w_n = ((p2[2] % 2_000_001) / 1_000_000.0 - 1.0) * 20.0
        g_e = (p2[3] % 2_000_001) / 1_000_000.0 - 1.0             # [-1,1]
        p3 = threefry4x64([i, 2, 0, 0], [gen_seed, 0, 31, 0], 20)
        g_n = (p3[0] % 2_000_001) / 1_000_000.0 - 1.0

        evap = evaporation(temp_k, wind_speed, water_frac)
        precip = precipitation(evap, incoming, w_e, w_n, g_e, g_n)
        evap_precip_vectors.append({
            "temperatureK": temp_k, "windSpeed": wind_speed, "surfaceWaterFraction": water_frac,
            "incomingMoisture": incoming, "windEast": w_e, "windNorth": w_n,
            "elevationGradientEast": g_e, "elevationGradientNorth": g_n,
            "evaporation": evap, "precipitation": precip,
        })

    weather_vectors = []
    for i in range(300):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 32, 0], 20)
        lat = (p[0] % 1_000_000) / 1_000_000.0 * math.pi - math.pi / 2.0
        lon = (p[1] % 1_000_000) / 1_000_000.0 * 2.0 * math.pi
        t = (p[2] % 1_000_000) / 1_000.0  # 0..1000 nap
        climate_mean_temp = -20.0 + (p[3] % 1_000_000) / 1_000_000.0 * 60.0  # -20..40C
        pos = (math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat))

        dev_t = weather_temperature_deviation_k(world_seed, pos, t)
        cur_t = current_temperature_k(climate_mean_temp, world_seed, pos, t)
        mult_p = weather_precipitation_multiplier(world_seed, pos, t)
        weather_vectors.append({
            "x": pos[0], "y": pos[1], "z": pos[2], "t": t,
            "climateMeanTemperatureC": climate_mean_temp,
            "weatherDeviationK": dev_t, "currentTemperatureC": cur_t,
            "precipitationMultiplier": mult_p,
        })

    out = {
        "worldSeed": world_seed,
        "constants": {
            "baseWindSpeedDefault": BASE_WIND_SPEED_DEFAULT,
            "coriolisDeflectionDegDefault": CORIOLIS_DEFLECTION_DEG_DEFAULT,
            "thermalWindCoeffDefault": THERMAL_WIND_COEFF_DEFAULT,
            "mountainDeflectionMaxDefault": MOUNTAIN_DEFLECTION_MAX_DEFAULT,
            "mountainSlopeScaleDefault": MOUNTAIN_SLOPE_SCALE_DEFAULT,
            "orographicCoeffDefault": OROGRAPHIC_COEFF_DEFAULT,
            "orographicScaleDefault": OROGRAPHIC_SCALE_DEFAULT,
            "maxWeatherTempDeviationK": MAX_WEATHER_TEMP_DEVIATION_K,
            "maxWeatherPrecipDeviationFraction": MAX_WEATHER_PRECIP_DEVIATION_FRACTION,
        },
        "windVectors": wind_vectors,
        "evaporationPrecipitationVectors": evap_precip_vectors,
        "weatherVectors": weather_vectors,
    }
    with open("wind_precipitation_vectors.json", "w", newline="\n") as f:
        json.dump(out, f, indent=1)
    print(f"{len(wind_vectors)} szel + {len(evap_precip_vectors)} parolgas/csapadek + "
          f"{len(weather_vectors)} idojaras-zaj tesztvektor elmentve wind_precipitation_vectors.json-ba")
