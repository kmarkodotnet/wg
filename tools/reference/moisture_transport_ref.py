"""
Nedvesseg-transzport -> csapadek-mezo referencia-implementacioja (M5, spec 30-31).
A backlog "M5 | Szel, nedvesseg, csapadek" tetel HIANYZO resze: a wind_precipitation_ref
a szelet, parolgast es a PER-TILE precipitation() fuggvenyt adja, de az `incoming_moisture`
kulso bemenet - NINCS a nedvesseget az oceanok folul a szel menten a szarazfold fole
vivo ADVEKCIO. Enelkul a szarazfoldi csapadek ~0. Ez a modul potolja azt: egy
determinisztikus, iterativ racs-advekcio a mar validalt cubed-sphere tile-grafon.

MODELL (tudatosan EGYSZERU, dokumentalt MVP - a konstansok vizualis kalibralast
igenyelnek, mint az ND-41 tobbi erteke):
  1. Minden tile: 3D szel (wind_vector, hegy-elteres nelkul), oceani parolgas-forras
     (evaporation, surface_water=1 oceanon), es a 4 szomszed fele mutato KIFOLYAS-suly
     (max(0, szel . erinto-el-irany)), osszegre normalizalva.
  2. Orografikus csapadek-hanyad: a KIFOLYAS-iranyban (downwind) emelkedo terep tobb
     esot ad (windward), sullyedo kevesebbet (eso-arnyek).
  3. Iterativ advekcio (fix N lepes): az oceanok parologtatnak (forras), minden tile
     lecsapja a nedvesseg egy hanyadat (nyelo), a maradek a szel menti sulyokkal a
     downwind szomszedokba folyik. N lepes utan a csapadek-mezo = nedvesseg * hanyad
     (allando allapotu csapadek-rata).

A lanc a wind_vector-on keresztul nyers math.sin/cos-t hasznal (mint a teljes
wind_precipitation lanc) -> NEM bit-egzakt cross-platform; a C# a vektorokhoz
TOLERANCIAVAL merodik (mint a TemperatureKelvin/WindPrecipitation).

Determinizmus: a tile-bejaras rogzitett (face,u,v) sorrendben tortenik, a szomszedok
a mar KAT-validalt neighbor()-bol jonnek -> a C# ugyanazt a sorrendet/grafot latja.
"""

import json
import math

from hydrology_ref import compute_elevation_and_ocean_field
from sphere_position_ref import position_from_tile
from neighbor_ref import neighbor, DIRECTIONS
from temperature_ref import temperature_kelvin
from wind_precipitation_ref import wind_vector, evaporation

# --- MVP konstansok (vizualis kalibralast igenyelnek, ld. modul-doc) ---
N_ITERATIONS = 24
PRECIP_BASE_FRACTION = 0.05        # az oszlop-nedvesseg ennyi hanyada csapodik le/lepes (sima terep)
OROGRAPHIC_COEFF = 2.0             # a downwind emelkedes esofokozo szorzoja
OROGRAPHIC_ELEV_SCALE = 1000.0     # meter - az emelkedes normalizalo skalaja
DEFAULT_DAY_T = 0.0
DEFAULT_ORBITAL_PERIOD = 365.25
DEFAULT_ROTATION_PERIOD = 1.0
DEFAULT_AXIAL_TILT_DEG = 23.44


def _tangent_edge_dir(pos, npos):
    """A pos-bol az npos fele mutato EGYSEG erinto-vektor (radialis komponens levonva)."""
    dx, dy, dz = npos[0] - pos[0], npos[1] - pos[1], npos[2] - pos[2]
    rad = dx * pos[0] + dy * pos[1] + dz * pos[2]
    tx, ty, tz = dx - rad * pos[0], dy - rad * pos[1], dz - rad * pos[2]
    length = math.sqrt(tx * tx + ty * ty + tz * tz)
    if length < 1e-12:
        return (0.0, 0.0, 0.0)
    return (tx / length, ty / length, tz / length)


def compute_precipitation_field(
    world_seed, plate_count, level,
    day_t=DEFAULT_DAY_T, orbital_period=DEFAULT_ORBITAL_PERIOD,
    rotation_period=DEFAULT_ROTATION_PERIOD, axial_tilt_deg=DEFAULT_AXIAL_TILT_DEG,
    n_iterations=N_ITERATIONS,
    precip_base_fraction=PRECIP_BASE_FRACTION,
    orographic_coeff=OROGRAPHIC_COEFF,
    orographic_elev_scale=OROGRAPHIC_ELEV_SCALE,
):
    field, sea_level, is_ocean = compute_elevation_and_ocean_field(world_seed, plate_count, level)
    axial_tilt = math.radians(axial_tilt_deg)
    n = 1 << level
    keys = [(face, u, v) for face in range(6) for u in range(n) for v in range(n)]

    out_weights = {}
    precip_frac = {}
    evap_source = {}

    for k in keys:
        face, u, v = k
        pos = position_from_tile(face, level, u, v)
        elev = field[k]
        oc = is_ocean[k]

        temp = temperature_kelvin(pos, day_t, orbital_period, rotation_period, axial_tilt, oc, elev, sea_level)
        fe, fn, w3d = wind_vector(
            pos, day_t, orbital_period, rotation_period, axial_tilt, oc, elev, sea_level, 0.0, 0.0)
        speed = math.sqrt(fe * fe + fn * fn)
        evap_source[k] = evaporation(temp, speed, 1.0 if oc else 0.0)

        weights = [0.0, 0.0, 0.0, 0.0]
        total = 0.0
        for d in range(4):
            nb = neighbor(face, level, u, v, DIRECTIONS[d])
            npos = position_from_tile(nb[0], level, nb[1], nb[2])
            edir = _tangent_edge_dir(pos, npos)
            wdot = w3d[0] * edir[0] + w3d[1] * edir[1] + w3d[2] * edir[2]
            outw = wdot if wdot > 0.0 else 0.0
            weights[d] = outw
            total += outw

        uplift = 0.0
        if total > 0.0:
            for d in range(4):
                weights[d] /= total
            for d in range(4):
                nb = neighbor(face, level, u, v, DIRECTIONS[d])
                uplift += weights[d] * (field[nb] - elev)
        out_weights[k] = weights

        pf = precip_base_fraction * (1.0 + orographic_coeff * (uplift / orographic_elev_scale))
        precip_frac[k] = min(1.0, max(0.0, pf))

    moisture = {k: 0.0 for k in keys}
    for _ in range(n_iterations):
        new_m = {k: 0.0 for k in keys}
        for k in keys:
            m = moisture[k]
            if is_ocean[k]:
                m += evap_source[k]
            m -= m * precip_frac[k]  # csapadek-nyelo
            weights = out_weights[k]
            face, u, v = k
            distributed = 0.0
            for d in range(4):
                w = weights[d]
                if w > 0.0:
                    nb = neighbor(face, level, u, v, DIRECTIONS[d])
                    new_m[nb] += m * w
                    distributed += m * w
            new_m[k] += m - distributed  # ami nem folyik el (nincs downwind), helyben marad
        moisture = new_m

    precip = {k: moisture[k] * precip_frac[k] for k in keys}
    return precip, field, sea_level, is_ocean


def _generate_vectors():
    world_seed = 0xA7C944210000
    plate_count = 20
    level = 4  # 6 * 16 * 16 = 1536 tile
    precip, field, sea_level, is_ocean = compute_precipitation_field(world_seed, plate_count, level)

    keys = [(face, u, v) for face in range(6) for u in range(1 << level) for v in range(1 << level)]
    # Minden 11. tile mintavetele (~140 vektor), determinisztikus.
    sampled = keys[::11]
    vectors = []
    for k in sampled:
        face, u, v = k
        vectors.append({
            "face": face, "u": u, "v": v,
            "isOcean": bool(is_ocean[k]),
            "elevation": field[k],
            "precipitation": precip[k],
        })

    out = {
        "params": {
            "worldSeed": world_seed, "plateCount": plate_count, "level": level,
            "nIterations": N_ITERATIONS, "precipBaseFraction": PRECIP_BASE_FRACTION,
            "orographicCoeff": OROGRAPHIC_COEFF, "orographicElevScale": OROGRAPHIC_ELEV_SCALE,
            "dayT": DEFAULT_DAY_T, "orbitalPeriod": DEFAULT_ORBITAL_PERIOD,
            "rotationPeriod": DEFAULT_ROTATION_PERIOD, "axialTiltDeg": DEFAULT_AXIAL_TILT_DEG,
            "seaLevel": sea_level,
        },
        "vectors": vectors,
    }
    return out, precip, is_ocean


if __name__ == "__main__":
    out, precip, is_ocean = _generate_vectors()
    with open("moisture_transport_vectors.json", "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1)

    # Plauzibilitas: a szarazfold-csapadek atlaga legyen POZITIV (van advekcio),
    # es az oceani atlag is (a forras). Ha a szarazfoldi ~0 lenne, a modell nem
    # visz nedvesseget a parton tul.
    land_vals = [p for k, p in precip.items() if not is_ocean[k]]
    ocean_vals = [p for k, p in precip.items() if is_ocean[k]]
    land_mean = sum(land_vals) / len(land_vals) if land_vals else 0.0
    ocean_mean = sum(ocean_vals) / len(ocean_vals) if ocean_vals else 0.0
    print(f"tiles: {len(precip)}  land_mean_precip={land_mean:.5f}  ocean_mean_precip={ocean_mean:.5f}")
    print(f"vectors written: {len(out['vectors'])}")
    assert land_mean > 0.0, "A szarazfoldi csapadek atlaga pozitiv kell legyen (mukodik az advekcio)"
