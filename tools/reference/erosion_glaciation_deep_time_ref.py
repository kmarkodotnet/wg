"""
Erozios felhalmozodas + eljegesedes-ciklusok referencia-implementacioja
M10-hez (docs/05-milestones.md M10 sora: "Erozio idovel, eljegesedesi-
ciklusok").

HATOKOR (dokumentalt egyszerusites - ND-44, ld. docs/04-decisions.md):

  1. Erozios felhalmozodas: a spec Sz.17 dH/dt = UpliftRate - ErosionRate
     egyenletet NEM a Sz.18.2 teljes, nemlinearis alakjaban
     (ErosionRate = k * Rainfall^alpha * Slope^beta * MaterialFactor)
     oldjuk meg - ahhoz a Slope maganak a domborzatnak a fuggvenye lenne,
     ami idoben maga is valtozik (ahogy a hegyseg kopik), es ez csak
     numerikus PDE-integralassal lenne kovetheto. Egy ilyen integrator
     LEPESKOZ-FUGGO eredmenyt adna (kulonbozo dt mellett mas
     diszkretizacios hiba halmozodna fel), ami sertene az ND-04
     timestep-invarianciat es vegso soron az I1 determinizmust (ugyanaz
     a vilag mas eredmenyt adna, ha mas lepeskozzel kerdeznenk le).

     Helyette LINEARIS RELAXACIOS kozelites: az erozio a MEGLEVO
     reliefhez (a lemezhatar statikus uplift-bonuszahoz) ARANYOS -
     ez egy dH/dt = k*(H_eq - H) alaku linearis ODE-t ad, aminek van
     ZART, analitikus (exponencialis) megoldasa (ld. lent). Ez a
     "topografiai relaxacios ido" koncepcio egyszerusitett valtozata
     (a geomorfologiaban hasznalt fogalom arra, hogy egy reliefzona
     mennyi ido alatt kozeliti meg az uj egyensulyi allapotat) - a
     konkret idoallando (OROGENIC_RELAXATION_TAU_MYR) es az egyensulyi
     hanyad (EQUILIBRIUM_FRACTION) viszont ILLUSZTRATIV, NEM egy
     publikalt geologiai matbol verifikalt ertek - ld. ND-44, expliciten
     megerositest igenyel.

  2. Eljegesedes-ciklusok: meg nincs kesz klima-modul T_cycle tagja
     (a tools/reference/temperature_ref.py sajat dokumentacioja
     kifejezetten "T_cycle halasztva"-kent jelzi az M5 hatokorben). Ez a
     modul egy ONALLO, egyszeru szinuszos GlobalTempOffset(timeMyr)
     forcing-ot definial, es egy IDEALIZALT, szelesseg-alapu
     homerseklet-profillal (NEM a temperature_ref napi-inszolacios
     integraljaval - az egy kulon, nehezebb sulyu modell, ami a
     nap/keringes fazisat is igenyelne) mutatja meg, hogyan tolodik el a
     jegvonal periodikusan. EZT KESOBB EGYESITENI KELL a valodi klima-
     modul T_cycle tagjaval, ha az elkeszul - ld. ND-44.

A ket alrendszer (erozios relaxacio / eljegesedes) EZ A MODUL SZANDEKOSAN
NEM kapcsolja ossze szamszeruen (pl. "jegkorszakban gyorsabb az erozio") -
az egy tovabbi, kulon dokumentalando modellezesi dontes lenne, amit itt
nyitva hagyunk (ld. ND-44 "kesobbi munka" szakasza).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math
import json

from crust_elevation_ref import base_elevation
from plate_boundary_ref import boundary_uplift, elevation_with_boundary
from plate_ref import generate_plate_seeds, assign_plate
from morton_ref import tile_id
from sphere_position_ref import position_from_tile
from threefry_ref import threefry4x64

# --- 1. Erozios felhalmozodas: exponencialis relaxacio (ND-44) ---

# Relaxacios idoallando (Myr) - ILLUSZTRATIV MODELLEZESI VALASZTAS.
OROGENIC_RELAXATION_TAU_MYR = 50.0

# Az egyensulyi relief hanyada a fiatal (t=0) uplift-bonuszhoz kepest -
# hosszu tavon a hegyseg erodalodik, csak a bonusz egy TOREDEKEN
# stabilizalodik (feltetelezve, hogy a lemezhatar geometriaja nem
# frissul - ld. HATOKOR). ILLUSZTRATIV MODELLEZESI VALASZTAS.
EQUILIBRIUM_FRACTION = 0.35


def _relax_towards(h0, h_eq, time_myr, tau):
    """
    ZART ALAKU megoldasa a dH/dt = (1/tau)*(H_eq - H) linearis ODE-nek
    FIX (konstans) H_eq egyensulyi ertek mellett:

        H(t) = H_eq + (H0 - H_eq) * exp(-t / tau)

    KRITIKUS: H_eq-nak a teljes idointervallumon FIXNEK kell maradnia,
    hogy a chain_relaxation lancolasa (tobb reszidokozre bontva) a
    felcsoport-tulajdonsag miatt bitre (kerekitesi zajon beluli
    pontossaggal) egyezzen az egylepeses kiertekelessel - ha H_eq minden
    lepesben UJRASZAMOLODNA a pillanatnyi H-bol, az ELRONTANA a
    timestep-invarianciat (ez volt az elso implementacio hibaja, ld. git
    tortenet / fejlesztoi jegyzet).
    """
    return h_eq + (h0 - h_eq) * math.exp(-time_myr / tau)


def uplift_relaxation_elevation(uplift_bonus_static, time_myr,
                                 tau=OROGENIC_RELAXATION_TAU_MYR,
                                 eq_fraction=EQUILIBRIUM_FRACTION):
    """
    A hegyseg-relief zart alaku relaxacioja: H_eq = eq_fraction *
    uplift_bonus_static (a FIATAL, t=0-beli bonuszhoz kepesti egyensulyi
    hanyad), H0 = uplift_bonus_static.

    t=0-nal H(0) = H0 = uplift_bonus_static (bitre visszaadja a statikus
    M4 eredmenyt - ld. __main__ "t=0 visszamenoleges kompatibilitas").
    t -> vegtelenben H -> H_eq (a hegyseg "egyensulyi reliefje", ha a
    lemezhatar geometriaja orokre fixen maradna).

    Ez EXPLICIT fuggvenye t-nek, NEM iterativ akkumulator - ugyanaz a
    minta, mint plate_motion_ref.plate_seed_at_time (ND-04).
    """
    h_eq = eq_fraction * uplift_bonus_static
    return _relax_towards(uplift_bonus_static, h_eq, time_myr, tau)


def elevation_at_time(world_seed, plate_id, position, seeds, time_myr,
                       tau=OROGENIC_RELAXATION_TAU_MYR,
                       eq_fraction=EQUILIBRIUM_FRACTION):
    """A tile elevacioja time_myr idopontban: a statikus (nem-orogen)
    alap-elevacio VALTOZATLAN (a bazis-kereg maga nem "kopik el" ebben a
    modellben), csak a lemezhatar uplift-bonusza relaxal az egyensulya
    fele. Bitre megegyezik plate_boundary_ref.elevation_with_boundary-
    val time_myr=0-nal (ld. __main__)."""
    base, oceanic = base_elevation(world_seed, plate_id, position)
    uplift_static = boundary_uplift(world_seed, position, seeds)
    uplift_t = uplift_relaxation_elevation(uplift_static, time_myr, tau, eq_fraction)
    return base + uplift_t, oceanic


def chain_relaxation(h0, total_time_myr, n_steps,
                      tau=OROGENIC_RELAXATION_TAU_MYR,
                      eq_fraction=EQUILIBRIUM_FRACTION):
    """Ugyanaz a ZART formula n_steps darab egyenlo resz-idokozre
    LANCOLVA (minden lepesben az elozo kimenet az uj "H0", de az
    egyensulyi H_eq FIX marad, az EREDETI h0-bol szamolva - ld.
    _relax_towards docstring). Az exponencialis relaxacio FELCSOPORT-
    tulajdonsaga (exp(-a*(t1+t2)) = exp(-a*t1) * exp(-a*t2)) miatt ennek
    BARMELY n_steps-re (numerikus kerekitesi zajon beluli pontossaggal)
    meg kell egyeznie az egylepeses direkt kiertekelessel - ez a
    timestep-invariancia bizonyitasa, NEM csak ket konkret t-re, hanem
    tetszoleges felbontasra (ld. __main__)."""
    h_eq = eq_fraction * h0
    dt = total_time_myr / n_steps
    h = h0
    for _ in range(n_steps):
        h = _relax_towards(h, h_eq, dt, tau)
    return h


def _naive_euler_relaxation(h0, total_time_myr, n_steps,
                             tau=OROGENIC_RELAXATION_TAU_MYR,
                             eq_fraction=EQUILIBRIUM_FRACTION):
    """ELLENPELDA - NEM a hasznalt modell resze. Csak azert van itt, hogy
    a __main__-ben MEGMUTASSUK, miert nem szabad naiv Euler-lepegetessel
    megoldani a dH/dt = k*(H_eq-H) ODE-t: az eredmeny LEPESKOZ-FUGGO
    (mas n_steps mas kimenetet ad), szemben a chain_relaxation zart
    formulajaval, ami lepeskoztol fuggetlen (ld. ND-04)."""
    h_eq = eq_fraction * h0
    dt = total_time_myr / n_steps
    h = h0
    for _ in range(n_steps):
        h += (h_eq - h) / tau * dt
    return h


# --- 2. Eljegesedes-ciklusok: periodikus globalis homerseklet-forcing (ND-44) ---

GLACIATION_PERIOD_MYR = 150.0  # ILLUSZTRATIV - ld. ND-44
GLACIATION_AMPLITUDE_K = 6.0   # ILLUSZTRATIV - ld. ND-44

ICE_THRESHOLD_K = 273.15  # a viz fagyaspontja - fizikai allando (0 Celsius), nem modellezesi szabadsag

# Idealizalt, szelesseg-alapu homerseklet-profil (NEM a temperature_ref
# napi-inszolacios integralja - onallo, egyszerusitett proxy, ld. modul
# HATOKOR). ILLUSZTRATIV, Fold-szeru nagysagrendu ertekek.
T_EQUATOR_K = 300.0
LATITUDE_TEMP_GRADIENT_K_PER_RAD = 38.2  # kb. (300-240)/(pi/2), ld. ND-44


def global_temp_offset(time_myr, period=GLACIATION_PERIOD_MYR,
                        amplitude=GLACIATION_AMPLITUDE_K, phase0=0.0):
    """Periodikus globalis homerseklet-forcing: szinuszos, ZART alaku
    t-ben (nem akkumulator). t=0-nal (phase0=0) az eltolas 0 -
    visszamenolegesen kompatibilis a statikus M5 homerseklet-modellel
    (temperature_ref.temperature_kelvin), ha valaki hozzaadja ehhez az
    eltolashoz."""
    return amplitude * math.sin(2.0 * math.pi * time_myr / period + phase0)


def _clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def ice_line_abs_latitude(time_myr):
    """Az abszolut szelesseg (radian), ahol az idealizalt homerseklet-
    profil pont ICE_THRESHOLD_K - ezen FELUL (a polus fele) jeg. ZART,
    analitikusan (nem numerikus gyokkeresessel) megoldott: a profil
    linearis a szelessegben, a metszespont egyenes behelyettesitessel
    adodik. [0, pi/2]-re vagva (nincs jeg-vonal negativ szelessegen vagy
    a felteken tul)."""
    raw = (T_EQUATOR_K + global_temp_offset(time_myr) - ICE_THRESHOLD_K) / LATITUDE_TEMP_GRADIENT_K_PER_RAD
    return _clamp(raw, 0.0, math.pi / 2.0)


def is_iced(abs_latitude_rad, time_myr):
    """True, ha az adott abszolut szelesseg a jelenlegi jegvonalon TUL
    (a polus fele) esik."""
    return abs_latitude_rad >= ice_line_abs_latitude(time_myr)


if __name__ == "__main__":
    world_seed = 0xA7C944210000
    plate_count = 20
    level = 6
    seeds = generate_plate_seeds(world_seed, plate_count)
    n = 1 << level

    # ------------------------------------------------------------------
    print("--- 1a. t=0 visszamenoleges kompatibilitas (M4 statikus eredmeny) ---")
    sample_positions = []
    for face in range(6):
        for u in range(0, n, 9):
            for v in range(0, n, 11):
                pos = position_from_tile(face, level, u, v)
                sample_positions.append(pos)

    max_diff_t0 = 0.0
    for pos in sample_positions:
        plate_id = assign_plate(pos, seeds)
        tid = 0  # elevation_with_boundary nem hasznalja fel szamitasban, csak jelzesertekkel kell
        expected_elev, expected_oceanic = elevation_with_boundary(world_seed, plate_id, tid, pos, seeds)
        actual_elev, actual_oceanic = elevation_at_time(world_seed, plate_id, pos, seeds, 0.0)
        assert actual_oceanic == expected_oceanic
        max_diff_t0 = max(max_diff_t0, abs(actual_elev - expected_elev))
    assert max_diff_t0 < 1e-9, f"t=0-nal bitre egyeznie kell a statikus M4 elevacioval, diff={max_diff_t0}"
    print(f"OK - {len(sample_positions)} minta, max elteres a statikus M4 elevaciotol: {max_diff_t0:.2e}m\n")

    # ------------------------------------------------------------------
    print("--- 1b. Hosszu tavu relaxacio az egyensuly fele ---")
    # Egy magas uplift-bonuszu pozicio kell - keressuk meg a mintak kozul
    best_pos, best_uplift = None, 0.0
    for pos in sample_positions:
        up = boundary_uplift(world_seed, pos, seeds)
        if up > best_uplift:
            best_uplift, best_pos = up, pos
    assert best_uplift > 0.0, "Kell legyen legalabb egy erdemi uplift-bonuszu minta-pozicio"
    plate_id = assign_plate(best_pos, seeds)
    h0 = best_uplift
    h_eq_expected = EQUILIBRIUM_FRACTION * h0
    elev_t0, _ = elevation_at_time(world_seed, plate_id, best_pos, seeds, 0.0)
    elev_t_far, _ = elevation_at_time(world_seed, plate_id, best_pos, seeds, 2000.0)
    diff_far_from_eq = abs(elev_t_far - (elev_t0 - h0 + h_eq_expected))
    print(f"  uplift-bonusz t=0: {h0:.2f}m, egyensulyi celertek: {h_eq_expected:.2f}m")
    print(f"  elevacio t=0: {elev_t0:.2f}m, elevacio t=2000 Myr: {elev_t_far:.2f}m")
    assert elev_t_far < elev_t0, "2000 Myr utan a hegynek alacsonyabbnak kell lennie (erozio nyert)"
    assert diff_far_from_eq < 1e-3, f"2000 Myr utan gyakorlatilag el kell erni az egyensulyi erteket, diff={diff_far_from_eq}"
    print("OK - a magas uplift-bonuszu pozicio ideje folyaman az egyensulyi relief fele kopik\n")

    # ------------------------------------------------------------------
    print("--- 1c. TIMESTEP-INVARIANCIA (ND-04) - a zart formula lancolasa ---")
    h0_test = 1234.5
    total_t = 365.0
    direct = uplift_relaxation_elevation(h0_test, total_t)
    print(f"  direkt kiertekeles H(t={total_t}) = {direct:.10f}m")
    max_chain_diff = 0.0
    for n_steps in (1, 2, 3, 5, 13, 47, 101, 500):
        stepped = chain_relaxation(h0_test, total_t, n_steps)
        diff = abs(stepped - direct)
        max_chain_diff = max(max_chain_diff, diff)
        print(f"  lancolt (n_steps={n_steps:4d}, dt={total_t / n_steps:8.4f} Myr): H = {stepped:.10f}m  diff={diff:.3e}")
    assert max_chain_diff < 1e-6, f"A zart formula lancolasanak lepeskoztol fuggetlennek kell lennie, max diff={max_chain_diff}"
    print(f"OK - a zart formula BARMELY felbontasban lancolva ugyanazt adja (max elteres: {max_chain_diff:.2e}m, "
          f"gepi lebegopontos kerekitesi zaj szintjen, NEM diszkretizacios hiba)\n")

    print("--- 1d. ELLENPELDA: a naiv Euler-integrator LEPESKOZ-FUGGO (miert nem ezt hasznaljuk) ---")
    euler_results = {}
    for n_steps in (1, 2, 5, 20, 100, 2000):
        euler_results[n_steps] = _naive_euler_relaxation(h0_test, total_t, n_steps)
        print(f"  naiv Euler (n_steps={n_steps:5d}): H = {euler_results[n_steps]:.6f}m  (diff a zart megoldastol: "
              f"{abs(euler_results[n_steps] - direct):.6f}m)")
    euler_spread = max(euler_results.values()) - min(euler_results.values())
    assert euler_spread > 1.0, (
        "A naiv Euler-integratornak ERDEMBEN kulonbozo eredmenyt kell adnia kulonbozo "
        f"lepesszamra (ez bizonyitja, miert nem hasznaljuk) - spread={euler_spread}"
    )
    print(f"OK - a naiv Euler valoban lepeskoz-fuggo (szoras a lepesszamok kozott: {euler_spread:.3f}m) - "
          "EZERT hasznaljuk a zart formulat, nem ezt\n")

    # ------------------------------------------------------------------
    print("--- 2a. Eljegesedes: t=0-nal nincs eltolas ---")
    assert global_temp_offset(0.0) == 0.0
    print("OK - GlobalTempOffset(0) == 0 (visszamenoleges kompatibilitas a statikus M5 modellel)\n")

    print("--- 2b. A jegvonal periodikusan ingadozik ---")
    t_coldest = 0.75 * GLACIATION_PERIOD_MYR  # sin(2*pi*t/T) = -1 -> t = 3T/4 (phase0=0)
    t_warmest = 0.25 * GLACIATION_PERIOD_MYR  # sin(2*pi*t/T) = +1 -> t = T/4
    offset_cold = global_temp_offset(t_coldest)
    offset_warm = global_temp_offset(t_warmest)
    print(f"  leghidegebb idopont (t={t_coldest}Myr): offset={offset_cold:.3f}K")
    print(f"  legmelegebb idopont (t={t_warmest}Myr): offset={offset_warm:.3f}K")
    assert abs(offset_cold - (-GLACIATION_AMPLITUDE_K)) < 1e-9
    assert abs(offset_warm - GLACIATION_AMPLITUDE_K) < 1e-9

    lat_cold = ice_line_abs_latitude(t_coldest)
    lat_warm = ice_line_abs_latitude(t_warmest)
    print(f"  jegvonal szelessege jegkorszakban: {math.degrees(lat_cold):.2f} fok")
    print(f"  jegvonal szelessege interglacialisban: {math.degrees(lat_warm):.2f} fok")
    assert lat_cold < lat_warm, "Jegkorszakban a jegvonalnak kozelebb kell lennie az egyenlitohoz"
    print("OK - a jegvonal az egyenlito fele tolodik jegkorszakban, a polus fele melegkorban\n")

    print("--- 2c. Determinizmus (tiszta fuggvenyek) ---")
    a1 = elevation_at_time(world_seed, 3, sample_positions[0], seeds, 123.0)
    a2 = elevation_at_time(world_seed, 3, sample_positions[0], seeds, 123.0)
    assert a1 == a2, "Nem tiszta fuggveny (elevation_at_time)!"
    b1 = ice_line_abs_latitude(77.0)
    b2 = ice_line_abs_latitude(77.0)
    assert b1 == b2, "Nem tiszta fuggveny (ice_line_abs_latitude)!"
    print("OK - determinisztikus (tiszta fuggvenyek)\n")

    # ------------------------------------------------------------------
    # Tesztvektorok a C# porthoz
    print("--- Tesztvektor-generalas ---")
    erosion_vectors = []
    gen_seed_erosion = 0xE705104E00000001
    for i in range(400):
        p = threefry4x64([i, 0, 0, 0], [gen_seed_erosion, 0, 44, 0], 20)
        face = p[0] % 6
        u = p[1] % n
        v = p[2] % n
        time_myr = (p[3] % 2_000_000) / 1000.0  # 0..2000 Myr
        pos = position_from_tile(face, level, u, v)
        plate_id = assign_plate(pos, seeds)
        elev, oceanic = elevation_at_time(world_seed, plate_id, pos, seeds, time_myr)
        erosion_vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "plateId": plate_id, "timeMyr": time_myr,
            "elevation": elev, "isOceanic": oceanic,
        })

    glaciation_vectors = []
    gen_seed_glac = 0x61ACE00000000001
    for i in range(200):
        p = threefry4x64([i, 0, 0, 0], [gen_seed_glac, 0, 45, 0], 20)
        time_myr = (p[0] % 3_000_000) / 1000.0  # 0..3000 Myr
        lat_raw = (p[1] % 1_000_000) / 1_000_000.0 * (math.pi / 2.0)  # 0..pi/2
        glaciation_vectors.append({
            "timeMyr": time_myr,
            "globalTempOffsetK": global_temp_offset(time_myr),
            "iceLineAbsLatitudeRad": ice_line_abs_latitude(time_myr),
            "sampleAbsLatitudeRad": lat_raw,
            "isIced": is_iced(lat_raw, time_myr),
        })

    with open("erosion_glaciation_deep_time_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count, "level": level,
            "orogenicRelaxationTauMyr": OROGENIC_RELAXATION_TAU_MYR,
            "equilibriumFraction": EQUILIBRIUM_FRACTION,
            "glaciationPeriodMyr": GLACIATION_PERIOD_MYR,
            "glaciationAmplitudeK": GLACIATION_AMPLITUDE_K,
            "iceThresholdK": ICE_THRESHOLD_K,
            "tEquatorK": T_EQUATOR_K,
            "latitudeTempGradientKPerRad": LATITUDE_TEMP_GRADIENT_K_PER_RAD,
            "erosionVectors": erosion_vectors,
            "glaciationVectors": glaciation_vectors,
        }, f, indent=1)
    print(f"{len(erosion_vectors)} erozios + {len(glaciation_vectors)} eljegesedesi tesztvektor generalva")
