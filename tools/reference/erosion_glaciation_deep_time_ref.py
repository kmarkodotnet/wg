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

import deterministic_math_ref as dmath
from crust_elevation_ref import (
    SECONDARY_NOISE_FREQUENCY, OCEANIC_BASE_M, CONTINENTAL_BASE_M,
    PRIMARY_RELIEF_SPHERICAL_MEAN, SECONDARY_RELIEF_SPHERICAL_MEAN,
)
from crust_elevation_ref import blended_base_elevation
from domain_warp_ref import warp_position
from plate_boundary_ref import boundary_uplift, elevation_with_boundary, two_best_dots_with_indices
from plate_frame_noise_ref import elevation_with_boundary_plate_frame, moved_seeds
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
    return h_eq + (h0 - h_eq) * dmath.exp(-time_myr / tau)


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
                       eq_fraction=EQUILIBRIUM_FRACTION,
                       erosion_time_myr=None):
    """A tile elevacioja time_myr idopontban.

    Harom idofuggo hatas, mindharom ZART alakban (ND-04):
      1. a zaj a lemez SAJAT kereteben mintavetelezodik (ND-136 / A19) -
         ezt a `time_myr` vezerli;
      2. a lemezhatar uplift-bonusz exponencialis relaxacioja (ND-44);
      3. ND-137 (A20): a ket relief-zajtag AMPLITUDOJANAK hullamhossz-
         szelektiv, hidrologia-vezerelt csillapitasa
         (`relief_decay_factors`) - ez a tulajdonkeppeni EROZIO.

    A `seeds` a MAR ELMOZDITOTT lemez-magokat varja (a hivo dolga
    eloallitani, ld. plate_frame_noise_ref.moved_seeds).

    Az `erosion_time_myr` SZANDEKOSAN kulon allithato a `time_myr`-tol: a
    viewerben az "Erozio (kopas)" kapcsolo a kopast a lemezmozgas
    megtartasa mellett is kikapcsolhatja. None -> `time_myr`.

    Bitre megegyezik plate_boundary_ref.elevation_with_boundary-val
    time_myr=0-nal (ld. __main__)."""
    if erosion_time_myr is None:
        erosion_time_myr = time_myr
    primary_decay, secondary_decay = relief_decay_factors(position, erosion_time_myr)
    base, uplift_static, oceanic = elevation_with_boundary_plate_frame(
        world_seed, position, seeds, time_myr,
        primary_decay=primary_decay, secondary_decay=secondary_decay)
    uplift_t = uplift_relaxation_elevation(uplift_static, erosion_time_myr, tau, eq_fraction)
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
    return amplitude * dmath.sin(2.0 * math.pi * time_myr / period + phase0)


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


# --- 3. ND-137 / A20: hidrologia-vezerelt, ZART ALAKU relief-erozio ---
#
# A korabbi modell teljes "erozioja" a lemezhatar-uplift bonusz relaxacioja
# volt (1. szakasz): az ALAP-elevacio - az elsodleges ridged zaj es a
# masodlagos, regionalis hullamzas - idoben TELJESEN valtozatlan maradt.
# Pontosan ezt a hianyt jelezte a felhasznalo (todo2 A20).
#
# A MODELL. Az alap-elevacio harom tagbol all (crust_elevation_ref):
#
#     h = base_c  +  primary * mask * A_p  +  secondary * A_s
#
# ahol `base_c` a KEREG-TIPUS bazisszintje (a lemez vastagsagabol/uszasabol
# adodo szint, NEM felszini relief - ezert nem is erodalodik), a masik ket
# tag pedig a valodi felszini relief. Az erozio ezek AMPLITUDOJAT
# csillapitja, KULON idoallandoval:
#
#     h(t) = base_c + primary * mask * (A_p * D_p(t)) + secondary * (A_s * D_s(t))
#     D_i(t) = eq_i + (1 - eq_i) * exp(-t_eff / tau_i)
#
# MIERT KET IDOALLANDO - ez a modell lenyege. Az erozio HULLAMHOSSZ-
# SZELEKTIV: a rovid hullamhosszu relief (eles gerincek, volgyek) tobb
# nagysagrenddel gyorsabban kopik, mint a regionalis lepteku domborzati
# hullamzas. Ezert nez ki egy 1 milliard eves pajzs SIMANAK, de nem
# teljesen laposnak. A linearis lejto-diffuzio (dh/dt = kappa * lap(h)) egy
# k hullamszamu komponensre pontosan exp(-kappa*k^2*t)-t ad; a linearis
# stream-power (n=1) ~exp(-t/tau)-t, tau ~ L-lel. A ket hatar kozott
# valasztottunk: tau ARANYOS a hullamhosszal, tehat
#
#     tau_secondary / tau_primary = f_primary / f_secondary
#
# ami a MEGLEVO zaj-frekvenciakbol SZARMAZTATOTT szam (8.0 / 0.50929... =
# 5*pi ~ 15.708), NEM egy ujabb szabadon valasztott konstans.
#
# MIERT ZART ALAKU (ND-04). D_i(t) minden pontban tiszta fuggvenye a
# (pozicio, t) parnak. Nincs akkumulalt allapot, nincs iteracio, nincs
# lepeskoz - a csuszka 0 -> 500 -> 1000 Myr utja ugyanazt adja, mint a
# kozvetlen 1000 Myr (exponencialis felcsoport-tulajdonsag), pontosan ugy,
# mint az uplift-relaxacional. Ehhez KELL, hogy a helyi eroziós hatekonysag
# t-FUGGETLEN legyen - ezert a statikus, zonalis profilt hasznaljuk, nem a
# pillanatnyi (mar erodalt) domborzatbol szamolt meredekseget.
#
# MIERT VAN BENNE LERAKODAS IS. A csillapitas NEM "lehuzas": ahol a zajtag
# NEGATIV (medence, volgytalp, intramontan arok), ott a csillapitas
# FELEMELI a felszint a bazisszint fele - a medence FELTOLTODIK, mikozben a
# csucs lekopik. A relief-mezon belul ez tomeg-ATRENDEZES, nem
# tomeg-eltuntetes (a __main__ 3d. pontja ezt meri).
#
# MI NINCS BENNE (nyitva marad, ld. ND-137). Valodi, a folyohalozat menten
# LEFELE szallitott hordalek: vizgyujto-terulet-sulyozott bevagodas,
# delta-epites, medencekozi tomegatvitel. Az nem allithato elo pontonkenti
# zart alakban, mert a vizgyujto terulet globalis, racs-alapu mennyiseg -
# az az ND-137 (C) opcio, kulon dontessel.

# A vizhajtotta erozio zonalis ("harom-cellas") csapadek-proxyja. UGYANAZ a
# modellezesi szint, mint az `ice_line_abs_latitude` idealizalt, szelesseg-
# alapu homerseklet-profilja: NEM a racs-alapu moisture_transport_ref-et
# hasznalja (az iterativ, racsra kotott, es nem bit-egzakt), hanem egy
# pontonkent kiertekelheto, bit-egzakt zonalis profilt.
EROSION_WATER_FLOOR = 0.15        # sivatagi/polaris minimum (nem nulla: szel, fagy)
EROSION_EQUATOR_WEIGHT = 1.0      # ITCZ, cos^8 alaku (keskeny egyenlitoi sav)
EROSION_MIDLAT_WEIGHT = 0.55      # mersekelt ovi viharpalya
EROSION_MIDLAT_CENTER_RAD = 50.0 * math.pi / 180.0
EROSION_MIDLAT_WIDTH_RAD = 12.0 * math.pi / 180.0

# A jeg sokkal hatekonyabb eroziv agens, mint a folyo (gleccservolgyek,
# cirkuszok, lenyesett pajzsok). ILLUSZTRATIV nagysagrend - ld. ND-137.
GLACIAL_EROSIVITY = 3.0

# Az elsodleges (rovid hullamhosszu, ridged) relief idoallandoja es
# egyensulyi hanyada. ILLUSZTRATIV, vizualis kalibralast igenyel - ND-137.
PRIMARY_RELIEF_TAU_MYR = 250.0
PRIMARY_RELIEF_EQ_FRACTION = 0.30

# A masodlagos (regionalis) relief idoallandoja: SZARMAZTATOTT, a ket zaj
# frekvenciajanak aranyabol (tau ~ hullamhossz). Nem szabad parameter.
PRIMARY_NOISE_BASE_FREQUENCY = 8.0  # noise_ref.ridged_multifractal alapertelmezese
SECONDARY_RELIEF_TAU_MYR = PRIMARY_RELIEF_TAU_MYR * (
    PRIMARY_NOISE_BASE_FREQUENCY / SECONDARY_NOISE_FREQUENCY)
SECONDARY_RELIEF_EQ_FRACTION = 0.60


def abs_latitude_rad(position):
    """A pozicio abszolut foldrajzi szelessege radianban. A z tengely a
    polaris tengely - ugyanaz a konvencio, mint astronomy_ref.subsolar_point
    (lat = asin(z))."""
    z = position[2]
    if z < 0.0:
        z = -z
    if z > 1.0:
        z = 1.0
    return dmath.asin(z)


def zonal_water_factor(abs_lat):
    """Dimenziotlan, t-FUGGETLEN eroziós hatekonysag a szelesseg alapjan:
    nedves egyenlito (ITCZ) + nedves mersekelt ov (viharpalya), koztuk
    szubtropusi sivatagov, a polusok fele szarazsag.

    A cos^8 tag csak SZORZASOKBOL all (cos^2 -> ^4 -> ^8), tehat bitpontos;
    a cos maga es a Gauss-tag a determinisztikus DeterministicMath-bol jon."""
    c = dmath.cos(abs_lat)
    c2 = c * c
    c4 = c2 * c2
    c8 = c4 * c4
    d = (abs_lat - EROSION_MIDLAT_CENTER_RAD) / EROSION_MIDLAT_WIDTH_RAD
    mid = EROSION_MIDLAT_WEIGHT * dmath.exp(-0.5 * d * d)
    return EROSION_WATER_FLOOR + EROSION_EQUATOR_WEIGHT * c8 + mid


def glaciated_fraction(abs_lat):
    """Az ido azon HANYADA (egy teljes eljegesedesi ciklusra atlagolva),
    amikor az adott szelesseg jeggel fedett.

    A pont akkor jeges, ha ice_line_abs_latitude(s) <= abs_lat, azaz

        (T_EQ + A*sin(theta) - T_ICE) / G <= abs_lat
        sin(theta) <= u,   u = (G*abs_lat - T_EQ + T_ICE) / A

    A sin(theta) <= u feltetel idoaranya egy teljes perioduson ZART alakban
    az arkusz-szinuszbol adodik:  0.5 + asin(u)/pi.

    KOZELITES (dokumentalt): reszperiodusra ez a SZEKULARIS atlag, nem az
    egzakt mertek - egesz sok periodusra egzakt. Cserebe LINEARIS t-ben,
    tehat a lancolhatosag (ND-04) egzaktul teljesul, es az erozio monoton
    no (nem "visszakopik" egy interglacialisban, ami fizikai keptelenseg
    lenne). Az eljegesedesi ciklus 150 Myr, a deep-time csuszka Gyr-lepteku.

    MEGJEGYZES: az alapertelmezett konstansokkal az ice_line clamp-je SOHA
    nem aktiv (|u| = 1 hatarai 31.3 fok es 49.3 fok, a [0, pi/2] vagas a sin
    ertekkeszleten kivul esne), tehat a zart alak PONTOSAN azt a feltetelt
    irja le, amit az is_iced."""
    u = ((LATITUDE_TEMP_GRADIENT_K_PER_RAD * abs_lat - T_EQUATOR_K + ICE_THRESHOLD_K)
         / GLACIATION_AMPLITUDE_K)
    if u <= -1.0:
        return 0.0
    if u >= 1.0:
        return 1.0
    return 0.5 + dmath.asin(u) / math.pi


def erosion_efficiency(abs_lat):
    """A helyi, t-fuggetlen eroziós hatekonysag: folyovizi + jegaramlasi
    tag. Dimenziotlan; 1.0 korul "atlagos" nedves mersekelt ovi ertek."""
    return zonal_water_factor(abs_lat) + GLACIAL_EROSIVITY * glaciated_fraction(abs_lat)


def effective_erosion_time_myr(position, erosion_time_myr):
    """A pontban ERVENYES eroziós ido: a valos ido a helyi hatekonysaggal
    skalazva. Tiszta fuggvenye a (pozicio, t) parnak, es LINEARIS t-ben -
    ezert marad sertetlen a csillapitas felcsoport-tulajdonsaga (ND-04)."""
    if erosion_time_myr == 0.0:
        return 0.0
    return erosion_efficiency(abs_latitude_rad(position)) * erosion_time_myr


# Az a kitevo-hatar, ahol az exp mar ugyis 1e-304 alatti, tehat a csillapito
# bitre az egyensulyi hanyad.
#
# MIERT KELL EXPLICIT KORLAT: a deterministic_math_ref.exp a vegeredmenyt
# bit-manipulacioval skalazza (_scale_by_power_of_two), es NEM kezeli az
# exponens-alulcsordulast - kb. -710 alatt nem 0-hoz tart, hanem SZEMETET ad
# (a levont exponens atcsordul az elojelbitbe). A modellben ez sosem fordulna
# elo (a deep-time csuszka Gyr-lepteku, a kitevo ~80 alatt marad), de egy
# csendes, determinisztikus szemet rosszabb, mint egy explicit hatar. Kulon
# feljegyezve - ez a DeterministicMath sajat hianyossaga, nem ezé a modulé.
MAX_DECAY_EXPONENT = 700.0


def relief_decay(effective_time_myr, tau, eq_fraction):
    """D(t) = eq + (1 - eq) * exp(-t_eff / tau).

    t_eff = 0-nal EGZAKT 1.0 (rovidzar) - igy a hivo oldalan az
    `amplitude * 1.0` szorzas bitre valtozatlan marad."""
    if effective_time_myr == 0.0:
        return 1.0
    exponent = -effective_time_myr / tau
    if exponent < -MAX_DECAY_EXPONENT:
        return eq_fraction
    if exponent > MAX_DECAY_EXPONENT:
        exponent = MAX_DECAY_EXPONENT
    return eq_fraction + (1.0 - eq_fraction) * dmath.exp(exponent)


def relief_decay_factors(position, erosion_time_myr):
    """(D_primary, D_secondary) az adott pontban, adott eroziós idonel."""
    t_eff = effective_erosion_time_myr(position, erosion_time_myr)
    return (relief_decay(t_eff, PRIMARY_RELIEF_TAU_MYR, PRIMARY_RELIEF_EQ_FRACTION),
            relief_decay(t_eff, SECONDARY_RELIEF_TAU_MYR, SECONDARY_RELIEF_EQ_FRACTION))


def chain_relief_decay(total_time_myr, n_steps, position, tau, eq_fraction):
    """A csillapitas n_steps resz-idokozre LANCOLVA - a timestep-invariancia
    (ND-04) bizonyitasa a relief-eroziora is. A D(t) alakja miatt a
    lancolas a (D - eq) resz szorzata: (D_1 - eq) * ... / (1-eq)^(n-1).
    Itt EGYSZERUEN azt csinaljuk, amit a csuszka: minden lepesben az
    AKTUALIS relief-amplitudot csillapitjuk a MEGMARADT resszel, fix eq
    celertekkel - ugyanaz a minta, mint a chain_relaxation."""
    t_eff_total = effective_erosion_time_myr(position, total_time_myr)
    dt_eff = t_eff_total / n_steps
    a = 1.0  # a relief amplitudo-hanyada
    for _ in range(n_steps):
        a = eq_fraction + (a - eq_fraction) * dmath.exp(-dt_eff / tau)
    return a


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
    print(f"  uplift-bonusz t=0: {h0:.2f}m, egyensulyi celertek: {h_eq_expected:.2f}m")
    print(f"  elevacio t=0: {elev_t0:.2f}m")

    # ND-136 ota az ALAP-elevacio is t-fuggo (a zaj a lemez kereteben
    # ertekelodik ki, es a lemez-magok is elmozdulnak), ezert a "2000 Myr
    # mulva alacsonyabb" naiv osszehasonlitas nem a relaxaciot merne, hanem
    # a lemez ala csuszott UJ zaj-mintat is. A relaxacio allitasa ezert
    # UGYANANNAL a t-nel ellenorzodik:
    #     elev(t) == base(t) + eq_fraction * uplift_static(t).
    t_far = 2000.0
    seeds_far = moved_seeds(world_seed, seeds, t_far)
    # ND-137 (A20): a bazis-elevacio is t-fuggo lett (a relief-tagok
    # amplitudoja csillapodik), ezert az ellenorzeshez UGYANAZOKAT a
    # csillapito tenyezoket kell hasznalni, amiket az elevation_at_time.
    dp_far, ds_far = relief_decay_factors(best_pos, t_far)
    far_base, far_uplift, _ = elevation_with_boundary_plate_frame(
        world_seed, best_pos, seeds_far, t_far,
        primary_decay=dp_far, secondary_decay=ds_far)
    elev_t_far, _ = elevation_at_time(world_seed, plate_id, best_pos, seeds_far, t_far)
    expected_far = far_base + EQUILIBRIUM_FRACTION * far_uplift
    diff_far_from_eq = abs(elev_t_far - expected_far)
    print(f"  t={t_far} Myr: uplift-bonusz {far_uplift:.2f}m, elevacio {elev_t_far:.2f}m, "
          f"egyensulyi varakozas {expected_far:.2f}m")
    assert elev_t_far <= far_base + far_uplift, (
        "2000 Myr utan a hegynek alacsonyabbnak kell lennie a relaxalatlan erteknel")
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

    # ==================================================================
    # 3. ND-137 / A20 - relief-erozio
    # ==================================================================
    print("--- 3a. A zonalis csapadek-proxy harom-cellas alakja ---")
    profile = {deg: zonal_water_factor(math.radians(deg))
               for deg in (0, 10, 28, 40, 50, 60, 75, 90)}
    for deg, w in profile.items():
        print(f"  |lat| = {deg:2d} fok: W = {w:.4f}")
    assert profile[0] > profile[28], "Az egyenlitonek nedvesebbnek kell lennie a szubtropusnal"
    assert profile[50] > profile[28], "A viharpalyanak nedvesebbnek kell lennie a szubtropusnal"
    assert profile[50] > profile[90], "A polusnak szarazabbnak kell lennie a viharpalyanal"
    assert min(profile.values()) >= EROSION_WATER_FLOOR
    print("OK - ket nedves ov (ITCZ, viharpalya), koztuk szubtropusi sivatagov, "
          f"dinamika-tartomany {min(profile.values()):.3f}..{max(profile.values()):.3f}\n")

    print("--- 3b. ISMERT-VALASZ: a jeges idohanyad zart alakja vs. numerikus integral ---")
    # A zart alak (arkusz-szinusz) ELLENORZESE brute-force idomintavetelezessel
    # EGESZ sok perioduson - ez a modul sajat "kulso referenciaja".
    n_samples = 2_000_000
    n_periods = 40
    max_frac_diff = 0.0
    for deg in (20, 32, 35, 40, 45, 48, 55, 80):
        lat = math.radians(deg)
        hits = 0
        total_t = n_periods * GLACIATION_PERIOD_MYR
        for i in range(n_samples):
            t_s = (i + 0.5) * total_t / n_samples
            if is_iced(lat, t_s):
                hits += 1
        numeric = hits / n_samples
        closed = glaciated_fraction(lat)
        diff = abs(numeric - closed)
        max_frac_diff = max(max_frac_diff, diff)
        print(f"  |lat| = {deg:2d} fok: zart = {closed:.6f}, numerikus = {numeric:.6f}, elteres = {diff:.2e}")
    # A turohatart a MINTAVETELEZES korlatozza, nem a zart alak: 40 periodus
    # alatt ~80 jeg-hatarkereszteles van, mindegyik legfeljebb egy mintanyit
    # teved -> a varhato hiba ~80/2e6 = 4e-5 nagysagrendu.
    assert max_frac_diff < 1e-4, f"A zart alaknak egyeznie kell a numerikus integrallal, max diff={max_frac_diff}"
    print(f"OK - a zart alak egyezik a numerikus idointegrallal (max elteres: {max_frac_diff:.2e}, "
          "a mintavetelezes felbontasan)\n")

    print("--- 3c. TIMESTEP-INVARIANCIA (ND-04) a relief-csillapitasra ---")
    chain_pos = sample_positions[7]
    t_total = 640.0
    direct_p, direct_s = relief_decay_factors(chain_pos, t_total)
    print(f"  direkt: D_primary = {direct_p:.12f}, D_secondary = {direct_s:.12f}")
    max_decay_chain_diff = 0.0
    for n_steps in (1, 2, 3, 7, 29, 113, 1000):
        cp = chain_relief_decay(t_total, n_steps, chain_pos,
                                PRIMARY_RELIEF_TAU_MYR, PRIMARY_RELIEF_EQ_FRACTION)
        cs = chain_relief_decay(t_total, n_steps, chain_pos,
                                SECONDARY_RELIEF_TAU_MYR, SECONDARY_RELIEF_EQ_FRACTION)
        d = max(abs(cp - direct_p), abs(cs - direct_s))
        max_decay_chain_diff = max(max_decay_chain_diff, d)
        print(f"  lancolt (n_steps={n_steps:5d}): D_p = {cp:.12f}, D_s = {cs:.12f}, diff = {d:.3e}")
    assert max_decay_chain_diff < 1e-12, (
        f"A csillapitasnak lepeskoztol fuggetlennek kell lennie, max diff={max_decay_chain_diff}")
    print(f"OK - a csillapitas BARMELY felbontasban lancolva ugyanazt adja "
          f"(max elteres: {max_decay_chain_diff:.2e})\n")

    print("--- 3d. LERAKODAS: a medencek FELTOLTODNEK, nem csak a csucsok kopnak ---")
    # Kontinentalis mintak kellenek: a medence a kereg-bazis ALATT van
    # (negativ relief-jarulek), a csucs folotte. Az erozio MINDKETTOT a
    # bazis fele mozgatja - ez tomeg-ATRENDEZES a relief-mezon belul.
    # A vizsgalat a BAZIS-ELEVACIOra vonatkozik (a relief-mezore), nem a
    # teljes elevaciora: az utobbiban benne van a lemezhatar-uplift is, ami
    # kulon, sajat idoallandoval relaxal (1. szakasz) es elfedne a kepet.
    t_dep = 500.0
    seeds_dep = moved_seeds(world_seed, seeds, t_dep)
    # Egy lemez, keveres nelkul: a hataron a keveres KET kulonbozo
    # kereg-bazisszintet vegyit, ott a "bazisszint" nem egyertelmu.
    from plate_frame_noise_ref import plate_frame_noise_basis, base_elevation_from_basis
    rose, fell, max_rise, max_fall = 0, 0, 0.0, 0.0
    rel0, rel1 = [], []
    for pos in sample_positions:
        dp, ds = relief_decay_factors(pos, t_dep)
        pid = assign_plate(pos, seeds_dep)
        pr, mk, sc = plate_frame_noise_basis(world_seed, pid, pos, t_dep)
        b0, oc = base_elevation_from_basis(world_seed, pid, pr, mk, sc)
        b1, _ = base_elevation_from_basis(world_seed, pid, pr, mk, sc, dp, ds)
        base_c = OCEANIC_BASE_M if oc else CONTINENTAL_BASE_M
        # Az EGYES relief-tagok kulon-kulon a SAJAT gombfelszini atlaguk
        # fele mozognak: a felette levo lekopik, az alatta levo FELTOLTODIK,
        # es egyik sem lepi at az atlagot (nincs tullovés).
        for value, mean, decay in ((pr * mk, PRIMARY_RELIEF_SPHERICAL_MEAN, dp),
                                   (sc, SECONDARY_RELIEF_SPHERICAL_MEAN, ds)):
            eroded = mean + (value - mean) * decay
            assert abs(eroded - mean) <= abs(value - mean) + 1e-12, "A tagnak az atlag fele kell mozognia"
            assert (value - mean) * (eroded - mean) >= 0.0, "A tag nem lohet tul az atlagon"
        rel0.append(b0 - base_c)
        rel1.append(b1 - base_c)
        if b1 > b0 + 1e-9:
            rose += 1
            max_rise = max(max_rise, b1 - b0)
        elif b1 < b0 - 1e-9:
            fell += 1
            max_fall = max(max_fall, b0 - b1)
    def _stdev(xs):
        m = sum(xs) / len(xs)
        return math.sqrt(sum((x - m) ** 2 for x in xs) / len(xs))

    sd0, sd1 = _stdev(rel0), _stdev(rel1)
    print(f"  {rose} minta EMELKEDETT (max +{max_rise:.1f} m, feltoltodes), "
          f"{fell} minta SULLYEDT (max -{max_fall:.1f} m, lekopas), t = {t_dep} Myr")
    print(f"  a relief SZORASA (a simasag merteke): {sd0:.1f} m -> {sd1:.1f} m "
          f"({100.0 * (1.0 - sd1 / sd0):.1f}% csokkenes)")
    assert rose > 0 and fell > 0, (
        "Mindket iranynak elo kell fordulnia: enelkul a modell nem erozio, hanem egyiranyu lehuzas")
    assert sd1 < sd0, "A relief-mezonek OSSZESSEGEBEN simulnia kell (csokkeno szoras)"
    print("OK - a csillapitas a zajtagok atlaga fele mozgat: a csucs kopik, a medence feltoltodik,\n"
          "     es a relief-mezo egesze simul (tomeg-ATRENDEZES, nem egyiranyu lehuzas)\n")

    print("--- 3e. ATLAGTARTAS: a kontinensek nem sullyednek el a sajat sulyuktol ---")
    # Nulla fele relaxalva a kontinensek ~490 m-t sullyednenek (fiktiv
    # tomegveszteseg, mert nincs izosztazia-modell); az atlag fele relaxalva
    # a GLOBALIS atlagnak ~valtozatlannak kell maradnia.
    t_mass = 3000.0  # gyakorlatilag telitett allapot
    seeds_mass = moved_seeds(world_seed, seeds, t_mass)
    s_before, s_after, cnt = 0.0, 0.0, 0
    for pos in sample_positions:
        dp, ds = relief_decay_factors(pos, t_mass)
        pid = assign_plate(pos, seeds_mass)
        pr, mk, sc = plate_frame_noise_basis(world_seed, pid, pos, t_mass)
        b0, _ = base_elevation_from_basis(world_seed, pid, pr, mk, sc)
        b1, _ = base_elevation_from_basis(world_seed, pid, pr, mk, sc, dp, ds)
        s_before += b0
        s_after += b1
        cnt += 1
    drift = s_after / cnt - s_before / cnt
    print(f"  atlagos bazis-elevacio t={t_mass} Myr-nel: {s_before / cnt:.1f} m -> {s_after / cnt:.1f} m "
          f"(eltolodas {drift:+.1f} m)")
    assert abs(drift) < 60.0, (
        f"A telitett erozio nem tolhatja el erdemben a globalis atlagot (eltolodas {drift} m) - "
        "kulonben a tengerszint-kalibracio (ND-38) elarasztana a vilagot")
    print("OK - a globalis atlag gyakorlatilag valtozatlan (a maradek a zaj-atlag "
          "seed-fuggo szorasa, nem rendszeres lehuzas)\n")

    print("--- 3f. Minden parameter erdemben hat, es az erozio LATHATO a domborzaton ---")
    # A csuszka MINDEN allasaban mennyit mozdul a domborzat ahhoz kepest,
    # mintha csak a lemezek mozognanak (erosionTimeMyr = 0)?
    # A lemez-ido FIXEN 0: igy a kulonbseg TISZTAN az erozio muve (kulonben a
    # mozgo lemezek alatt mas-mas terepet hasonlitanank ossze).
    growth = []
    for t_vis in (100.0, 250.0, 500.0, 1000.0, 2000.0):
        land, sea = [], []
        for pos in sample_positions:
            pid = assign_plate(pos, seeds)
            e_noerode, oc = elevation_at_time(world_seed, pid, pos, seeds, 0.0, erosion_time_myr=0.0)
            e_erode, _ = elevation_at_time(world_seed, pid, pos, seeds, 0.0, erosion_time_myr=t_vis)
            (sea if oc else land).append(abs(e_erode - e_noerode))
        land_mean = sum(land) / len(land)
        growth.append(land_mean)
        print(f"  t = {t_vis:7.1f} Myr: kontinentalis atlag {land_mean:6.1f} m (max {max(land):7.1f} m), "
              f"oceani atlag {sum(sea) / len(sea):5.1f} m")
    assert all(b > a for a, b in zip(growth, growth[1:])), (
        "A hatasnak monoton nonie kell az idovel")
    assert growth[-1] > 200.0, (
        "A deep-time eroziónak ERDEMBEN meg kell valtoztatnia a domborzatot (todo2 A20 elvarasa), "
        f"kontinentalis atlag={growth[-1]}")
    # A szelesseg tenylegesen szamit (nem uniform kopas):
    def _pos_at_lat(deg):
        r = math.radians(deg)
        return (math.cos(r), 0.0, math.sin(r))

    # A differencialt kopast a TELITES ELOTT kell merni: 2 Gyr-nel mar minden
    # ov az egyensulyi hanyadanal all, tehat ott nem latszana a kulonbseg.
    t_zone = 200.0
    wet = relief_decay_factors(_pos_at_lat(0.0), t_zone)     # egyenlito (ITCZ)
    dry = relief_decay_factors(_pos_at_lat(28.0), t_zone)    # szubtropusi sivatagov
    polar = relief_decay_factors(_pos_at_lat(80.0), t_zone)  # allando jegtakaro
    print(f"  t = {t_zone} Myr, D_primary: egyenlito {wet[0]:.4f} < szubtropus {dry[0]:.4f} "
          f"(a sivatag ORZ), polaris/jegtakaro {polar[0]:.4f}")
    print(f"           D_secondary (regionalis hullamhossz): egyenlito {wet[1]:.4f}")
    assert wet[0] < dry[0], "A nedves egyenlitonek jobban kell kopnia, mint a szubtropusi sivatagnak"
    assert polar[0] < dry[0], "A jegtakaronak (glacialis erozio) jobban kell koptatnia a sivatagnal"
    assert wet[1] > wet[0], "A regionalis (hosszu hullamhosszu) reliefnek LASSABBAN kell kopnia"
    print("OK - differencialt erozio: nedves ov es jegtakaro kop, a sivatag orzi a reliefet;\n"
          "     a regionalis hullamhossz tullep a rovid hullamhosszun\n")

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
        # ND-136: a lemez-magok ELMOZDULNAK time_myr-ig, es a zaj ezek
        # kereteben ertekelodik ki - a vektorok igy a TENYLEGES deep-time
        # utat merik, nem a t=0 magokkal vett hibridet.
        seeds_at_t = moved_seeds(world_seed, seeds, time_myr)
        plate_id = assign_plate(pos, seeds_at_t)
        elev, oceanic = elevation_at_time(world_seed, plate_id, pos, seeds_at_t, time_myr)
        erosion_vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "plateId": plate_id, "timeMyr": time_myr,
            "elevation": elev, "isOceanic": oceanic,
        })

    # ND-137 (A20): a relief-erozios reteg SAJAT vektorai - a zonalis
    # csapadek-proxy, a jeges idohanyad, az effektiv erozios ido es a ket
    # csillapito tenyezo KULON is merve, hogy a C#-eltérés forrasa
    # azonosithato legyen (ne csak a vegso elevacio-kulonbseg latszodjon).
    relief_erosion_vectors = []
    gen_seed_relief = 0xA20E7051040E0001
    for i in range(400):
        p = threefry4x64([i, 0, 0, 0], [gen_seed_relief, 0, 137, 0], 20)
        face = p[0] % 6
        u = p[1] % n
        v = p[2] % n
        erosion_t = (p[3] % 3_000_000) / 1000.0  # 0..3000 Myr
        pos = position_from_tile(face, level, u, v)
        lat = abs_latitude_rad(pos)
        t_eff = effective_erosion_time_myr(pos, erosion_t)
        dp, ds = relief_decay_factors(pos, erosion_t)
        relief_erosion_vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "erosionTimeMyr": erosion_t,
            "absLatitudeRad": lat,
            "zonalWaterFactor": zonal_water_factor(lat),
            "glaciatedFraction": glaciated_fraction(lat),
            "effectiveErosionTimeMyr": t_eff,
            "primaryReliefDecay": dp,
            "secondaryReliefDecay": ds,
        })

    # Az erozios ido es a lemez-ido SZETVALASZTASA (a viewer "Erozio
    # (kopas)" kapcsoloja): ugyanaz a t a zajra, de erosionTimeMyr=0.
    split_time_vectors = []
    gen_seed_split = 0xA20591170000001
    for i in range(120):
        p = threefry4x64([i, 0, 0, 0], [gen_seed_split, 0, 137, 1], 20)
        face = p[0] % 6
        u = p[1] % n
        v = p[2] % n
        plate_t = (p[3] % 2_000_000) / 1000.0
        erosion_t = (p[2] % 2_000_000) / 1000.0
        pos = position_from_tile(face, level, u, v)
        seeds_at_t = moved_seeds(world_seed, seeds, plate_t)
        pid = assign_plate(pos, seeds_at_t)
        elev, oceanic = elevation_at_time(
            world_seed, pid, pos, seeds_at_t, plate_t, erosion_time_myr=erosion_t)
        split_time_vectors.append({
            "face": face, "level": level, "u": u, "v": v,
            "plateId": pid, "plateTimeMyr": plate_t, "erosionTimeMyr": erosion_t,
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
            "erosionWaterFloor": EROSION_WATER_FLOOR,
            "erosionEquatorWeight": EROSION_EQUATOR_WEIGHT,
            "erosionMidLatWeight": EROSION_MIDLAT_WEIGHT,
            "erosionMidLatCenterRad": EROSION_MIDLAT_CENTER_RAD,
            "erosionMidLatWidthRad": EROSION_MIDLAT_WIDTH_RAD,
            "glacialErosivity": GLACIAL_EROSIVITY,
            "primaryReliefTauMyr": PRIMARY_RELIEF_TAU_MYR,
            "primaryReliefEqFraction": PRIMARY_RELIEF_EQ_FRACTION,
            "secondaryReliefTauMyr": SECONDARY_RELIEF_TAU_MYR,
            "secondaryReliefEqFraction": SECONDARY_RELIEF_EQ_FRACTION,
            "erosionVectors": erosion_vectors,
            "reliefErosionVectors": relief_erosion_vectors,
            "splitTimeVectors": split_time_vectors,
            "glaciationVectors": glaciation_vectors,
        }, f, indent=1)
    print(f"{len(erosion_vectors)} erozios + {len(relief_erosion_vectors)} relief-erozios + "
          f"{len(split_time_vectors)} szetvalasztott-ido + {len(glaciation_vectors)} "
          "eljegesedesi tesztvektor generalva")
