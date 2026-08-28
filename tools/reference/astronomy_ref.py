"""
Csillagaszat referencia-implementacio M3-hoz (docs/05-milestones.md):
palya, tengelydoles, forgas, szub-napponti pont, inszolacio.

HATOKOR (tudatosan szukitve M3-ra, nem hianyossag):
  - Kor palya (excentricitas=0). Az excentricitas valodi hatasa (§7.2 a
    specifikacioban) kesobb, amikor a klimamodell (M5) vagy a Milankovic-
    ciklusok (M10, §25) tenylegesen szuksegesse teszik.
  - Egyetlen csillag. Tobbcsillagos rendszer (§6.2) es a csillag hosszu
    tavu fenyesseg-valtozasa (§26) szinten kesobbi fazis.
  - A cel: "megvilagitott gomb, terminatorral - evszakok latszanak a
    terminator mozgasan" (M3 "Kesz, ha" kriteriuma), nem a teljes
    csillagaszati modell.

Koordinatarendszerek:
  - PALYA-KERET (nem forgo, "inercialis"): XY sik = palyasik, Z = palya-
    normalis. X tengely a tavaszponthoz (vernal equinox) igazitva.
  - TEST-KERET (a bolygoval egyutt forgo, dolt): ebben a keretben a
    bolygo egyenlitoje/polusai fixek - IDE kell a nap-iranyt szamolni,
    mert ez hatarozza meg, mit lat a Unity Directional Light-ja (a fenyt
    nem a mesh-hez, hanem ehhez a test-kerethez igazitjuk).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math
import json


def orbital_angle(t, orbital_period, phase0=0.0):
    """A bolygo szoghelyzete a palyan (kor palya -> szog == mean anomaly)."""
    return phase0 + 2.0 * math.pi * (t / orbital_period)


def sun_direction_orbital_frame(t, orbital_period, phase0=0.0):
    """Egysegvektor: bolygo -> csillag irany, a nem-forgo palya-keretben."""
    theta = orbital_angle(t, orbital_period, phase0)
    # A bolygo pozicioja (cos theta, sin theta, 0) * a; a nap iranya ebbol
    # a bolygo felol nezve pontosan -pozicio (normalva, a sugar kiesik).
    return (-math.cos(theta), -math.sin(theta), 0.0)


def _mat_vec(m, v):
    return (
        m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
        m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
        m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2],
    )


def _rot_x(angle):
    c, s = math.cos(angle), math.sin(angle)
    return ((1.0, 0.0, 0.0), (0.0, c, -s), (0.0, s, c))


def _rot_z(angle):
    c, s = math.cos(angle), math.sin(angle)
    return ((c, -s, 0.0), (s, c, 0.0), (0.0, 0.0, 1.0))


def _mat_mat(a, b):
    return tuple(
        tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3))
        for i in range(3)
    )


def _transpose(m):
    return tuple(tuple(m[j][i] for j in range(3)) for i in range(3))


def body_orientation_matrix(axial_tilt, rotation_angle):
    """
    A palya-keretbol a test-keretbe forgato matrix.

    Ket lepes: eloszor a spin-tengelyt megdontjuk (tilt, X tengely korul -
    ez a projekt onkenyes, de kovetkezetes konvencioja, hasonloan a
    TileGeometry lap-bazisahoz), utana a bolygot megforgatjuk a sajat
    (dolt) spin-tengelye korul a napi forgassal.

    v_body = R_spin^T * R_tilt^T * v_orbital  (a ket forgatas inverze,
    fordított sorrendben - mert egy pontot a testbe VISSZA transzformalunk).
    """
    r_tilt = _rot_x(axial_tilt)
    r_spin = _rot_z(rotation_angle)
    # Test -> palya = R_tilt * R_spin (eloszor a sajat, meg nem dolt Z
    # tengelye korul forog a bolygo, majd ezt az egeszet megdontjuk a
    # palya-keretbe). Ami nekunk kell, az ennek inverze:
    # palya -> test = (R_tilt * R_spin)^-1 = R_spin^T * R_tilt^T
    # (forgatasi matrixok inverze = transzponaltjuk)
    body_to_orbital = _mat_mat(r_tilt, r_spin)
    return _transpose(body_to_orbital)


def sun_direction_body_frame(t, orbital_period, rotation_period, axial_tilt,
                              orbital_phase0=0.0, rotation_phase0=0.0):
    """A nap iranya a bolygo SAJAT (forgo, dolt) koordinatarendszereben."""
    sun_orbital = sun_direction_orbital_frame(t, orbital_period, orbital_phase0)
    rotation_angle = rotation_phase0 + 2.0 * math.pi * (t / rotation_period)
    m = body_orientation_matrix(axial_tilt, rotation_angle)
    return _mat_vec(m, sun_orbital)


def subsolar_point(sun_dir_body_frame):
    """Nap-irany (test-keret) -> (szelesseg, hosszusag) radianban."""
    x, y, z = sun_dir_body_frame
    lat = math.asin(max(-1.0, min(1.0, z)))
    lon = math.atan2(y, x)
    return lat, lon


def insolation(sun_dir_body_frame, surface_normal_body_frame, flux):
    """§27: Insolation = F * max(0, cos theta)."""
    x1, y1, z1 = sun_dir_body_frame
    x2, y2, z2 = surface_normal_body_frame
    cos_theta = x1 * x2 + y1 * y2 + z1 * z2
    return flux * max(0.0, cos_theta)


def stellar_flux(luminosity, distance):
    """§7.1: F = L / (4 * pi * r^2)."""
    return luminosity / (4.0 * math.pi * distance * distance)


def declination_closed_form(axial_tilt, t, orbital_period, orbital_phase0=0.0):
    """
    Fuggetlen zart formula a szub-napponti szelesseghez (deklinacio),
    a csillagaszatban jol ismert kepletbol:
        sin(declination) = sin(axial_tilt) * sin(ecliptic_longitude)
    ahol ecliptic_longitude a bolygo palyaszoge a tavaszponttol merve.
    Ez a 3D forgatasmatrix-alapu szamitas FUGGETLEN ellenorzese.
    """
    theta = orbital_angle(t, orbital_period, orbital_phase0)
    # A tavaszpont az, ahol a bolygo pontosan a nap es a tavasz-referencia
    # kozott van ugy, hogy a deklinacio nulla legyen theta=pi/2-nel (ld.
    # onellenorzo teszt lent a pontos fazis-illesztesre).
    ecliptic_longitude = theta
    return math.asin(math.sin(axial_tilt) * math.sin(ecliptic_longitude))


if __name__ == "__main__":
    orbital_period = 365.25
    rotation_period = 1.0
    axial_tilt = math.radians(23.44)  # Fold-szeru ertek, csak illusztraciohoz

    print("Fuggetlen keresztellenorzes: 3D forgatasmatrix vs. zart formula")
    print("(a ket modszernek a deklinaciora bitkozeli pontossaggal egyeznie kell)\n")

    max_diff = 0.0
    for i in range(0, 3653):
        t = i * 0.1
        # rotation_phase0 kivalasztva ugy, hogy t=0-nal a szub-napponti pont
        # a test-keret X tengelyen legyen (delben) - a deklinacio fuggetlen ettol.
        sun_body = sun_direction_body_frame(
            t, orbital_period, rotation_period, axial_tilt,
            orbital_phase0=-math.pi / 2.0,  # tavaszpont t=0-nal (declination=0)
        )
        lat, _ = subsolar_point(sun_body)
        lat_closed = declination_closed_form(
            axial_tilt, t, orbital_period, orbital_phase0=-math.pi / 2.0
        )
        diff = abs(lat - lat_closed)
        max_diff = max(max_diff, diff)

    print(f"Max eltérés 3653 mintán (10 év, 0.1 napos lépés): {max_diff:.3e} rad")
    assert max_diff < 1e-9, "A ket modszer nem egyezik - hiba a forgatas-matekban"
    print("OK - a 3D forgatasmatrix es a zart formula bitkozeli pontossaggal egyezik\n")

    # Illusztracio: napfordulok es napejegyenlosegek (csak tajekoztato print,
    # nem resze a determinisztikus tesztvektor-generalasnak).
    # phase0=0 -> t=0 legyen napejegyenloseg (sin(theta)=0 ekkor).
    print("Deklinacio a palya menten (fokban), phase0=0, t=0 napejegyenloseg:")
    for frac, label in [(0.0, "napejegyenloseg #1"), (0.25, "napfordulo #1"),
                         (0.5, "napejegyenloseg #2"), (0.75, "napfordulo #2")]:
        t = frac * orbital_period
        sun_body = sun_direction_body_frame(
            t, orbital_period, rotation_period, axial_tilt,
            orbital_phase0=0.0,
        )
        lat, _ = subsolar_point(sun_body)
        print(f"  {label:20s} (t={t:6.2f} nap): declinacio = {math.degrees(lat):+.3f} deg")

    print("\nVart: napejegyenlosegek ~0 deg, napfordulok ~+-23.44 deg (ellentetes elojellel)")

    print("\nInszolacio-plauzibilitas (§27):")
    flux = 1361.0  # W/m^2, csak illusztraciohoz (naprol Foldre erkezo fluxus)
    sun_body = sun_direction_body_frame(0.0, orbital_period, rotation_period, axial_tilt)

    ins_subsolar = insolation(sun_body, sun_body, flux)
    assert abs(ins_subsolar - flux) < 1e-9, "Szub-napponti ponton pontosan F kellene legyen"
    print(f"  szub-napponti pont: {ins_subsolar:.3f} W/m^2 (vart: pontosan {flux})")

    antipodal = (-sun_body[0], -sun_body[1], -sun_body[2])
    ins_night = insolation(sun_body, antipodal, flux)
    assert ins_night == 0.0, "Ejszakai oldalon pontosan 0-nak kell lennie"
    print(f"  ejszakai oldal (antipodalis pont): {ins_night:.3f} W/m^2 (vart: 0)")

    # Terminator: a szub-napponti ponttol pontosan 90 fokra (barmely, a nap-
    # iranyra meroleges normal) az inszolacio hataresete - kozel 0, de erzekeny
    # a kerekitesre, ezert csak azt ellenorizzuk, hogy nagyon kicsi.
    perpendicular = (-sun_body[1], sun_body[0], 0.0)  # meroleges sun_body-ra
    ins_terminator = insolation(sun_body, perpendicular, flux)
    assert abs(ins_terminator) < 1e-9, "A terminatoron kozel nullanak kell lennie"
    print(f"  terminator (90 fokos szog): {ins_terminator:.3e} W/m^2 (vart: kozel 0)")
    print("OK - inszolacio plauzibilis")

    # Tesztvektorok a C# porthoz - determinisztikus mintavetel a Threefry
    # oraklummal (nem Python random-mal), a projekt korabbi mintaja szerint.
    from threefry_ref import threefry4x64

    vectors = []
    gen_seed = 0xA57A0BEEF0000001
    for i in range(300):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 5, 0], 20)
        t = (p[0] % 1_000_000) / 1000.0  # 0..1000 nap
        orbital_period_v = 100.0 + (p[1] % 900000) / 1000.0  # 100..1000 nap
        rotation_period_v = 0.1 + (p[2] % 100000) / 100000.0  # 0.1..1.1 nap
        axial_tilt_v = ((p[3] % 900000) / 1000000.0) * math.pi  # 0..~2.83 rad
        phases = threefry4x64([i, 1, 0, 0], [gen_seed, 0, 5, 0], 20)
        orbital_phase0_v = (phases[0] % 1000000) / 1000000.0 * 2.0 * math.pi
        rotation_phase0_v = (phases[1] % 1000000) / 1000000.0 * 2.0 * math.pi

        sun_body_v = sun_direction_body_frame(
            t, orbital_period_v, rotation_period_v, axial_tilt_v,
            orbital_phase0_v, rotation_phase0_v,
        )
        lat_v, lon_v = subsolar_point(sun_body_v)

        vectors.append({
            "t": t, "orbitalPeriod": orbital_period_v,
            "rotationPeriod": rotation_period_v, "axialTilt": axial_tilt_v,
            "orbitalPhase0": orbital_phase0_v, "rotationPhase0": rotation_phase0_v,
            "sunBodyFrame": list(sun_body_v),
            "subsolarLat": lat_v, "subsolarLon": lon_v,
        })

    with open("astronomy_vectors.json", "w", newline="\n") as f:
        json.dump({"vectors": vectors}, f, indent=1)
    print(f"\n{len(vectors)} csillagaszat-tesztvektor generalva")
