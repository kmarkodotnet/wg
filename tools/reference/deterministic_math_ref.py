"""
Determinisztikus Sin/Cos/Pow referencia-implementációja - ND-27 lezárása
(docs/04-decisions.md): a Math.Sin/Cos/Pow rendszerbeli kockázatát számolja
fel a szimulációs kritikus úton (csillagászat, klíma, lemezmozgás,
becsapódás).

MÓDSZER (Option B - saját polinomiális implementáció, ld. ND-26 táblázata):
csak a CLAUDE.md táblázat szerint GARANTÁLTAN bitpontos műveletekre épül:
    + - * /            (IEEE-754 exact-per-op)
    Math.Sqrt           (IEEE-754 korrekt kerekítés kötelező)
    Math.Floor/Round     (IEEE-754 roundToIntegral, EXAKT specifikáció,
                          nem transzcendens közelítés - ld. lent)
    bit-manipuláció      (struct.pack/unpack Pythonban, BitConverter C#-ban -
                          a double IEEE-754 bit-reprezentációjának direkt
                          kezelése, nulla kerekítési bizonytalanság)

SIN/COS: oktáns-redukció (a szöget a legközelebbi k*pi/4-re redukáljuk
[-pi/8, pi/8] tartományba) + Taylor-sor + szög-összeg azonosság a 8
oktáns EXAKT sin/cos értékével (0, +-1, +-sqrt(2)/2).

EXP/LN: IEEE-754 bit-dekompozíció (mantissza/exponens szétválasztás,
mint a C standard frexp/ldexp) + Taylor-sor a szűk [1, sqrt(2)) ill.
[-ln2/2, ln2/2] tartományon.

POW(x,y) = exp(y * ln(x)), x > 0 (x=0, y>0 eseten POW=0, dokumentalt
konvencio).

FONTOS: a cel NEM az, hogy "pontosabb" legyen a valodi math.sin-nel
szemben (barmilyen dokumentalt kozelites elfogadhato), hanem hogy
PLATFORMFUGGETLENUL BITRE AZONOS legyen - csak garantaltan exakt
alapmuveletekbol epul fel.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import math
import struct

TWO_PI = 2.0 * math.pi
PI_OVER_4 = math.pi / 4.0
LN2 = math.log(2.0)

SQRT2_OVER_2 = 0.70710678118654752440084436210485

SIN_OCTANT = [0.0, SQRT2_OVER_2, 1.0, SQRT2_OVER_2, 0.0, -SQRT2_OVER_2, -1.0, -SQRT2_OVER_2]
COS_OCTANT = [1.0, SQRT2_OVER_2, 0.0, -SQRT2_OVER_2, -1.0, -SQRT2_OVER_2, 0.0, SQRT2_OVER_2]


def _round_half_even(x):
    """IEEE-754 roundToIntegralTiesToEven - EXAKT muvelet (nem kozelites)."""
    return float(round(x))


def _reduce_angle(x):
    """Tetszoleges valos szog -> [0, 2*pi) tartomany, csak /,-,floor-ral."""
    revolutions = x / TWO_PI
    frac = revolutions - math.floor(revolutions)
    r = frac * TWO_PI
    if r >= TWO_PI:  # ritka hatarest (kerekitesi felfele-csuszas)
        r -= TWO_PI
    return r


def _taylor_sin(x):
    """x in [-pi/8, pi/8] - Horner-sema x^2-ben."""
    x2 = x * x
    c13, c11, c9, c7, c5, c3 = (
        1.0 / 6227020800.0, -1.0 / 39916800.0, 1.0 / 362880.0,
        -1.0 / 5040.0, 1.0 / 120.0, -1.0 / 6.0,
    )
    poly = c13
    poly = c11 + x2 * poly
    poly = c9 + x2 * poly
    poly = c7 + x2 * poly
    poly = c5 + x2 * poly
    poly = c3 + x2 * poly
    poly = 1.0 + x2 * poly
    return x * poly


def _taylor_cos(x):
    """x in [-pi/8, pi/8] - Horner-sema x^2-ben."""
    x2 = x * x
    c12, c10, c8, c6, c4, c2 = (
        1.0 / 479001600.0, -1.0 / 3628800.0, 1.0 / 40320.0,
        -1.0 / 720.0, 1.0 / 24.0, -1.0 / 2.0,
    )
    poly = c12
    poly = c10 + x2 * poly
    poly = c8 + x2 * poly
    poly = c6 + x2 * poly
    poly = c4 + x2 * poly
    poly = c2 + x2 * poly
    poly = 1.0 + x2 * poly
    return poly


def sin_cos(x):
    """(sin(x), cos(x)) - determinisztikus, csak exakt muveletekbol."""
    r = _reduce_angle(x)
    k = _round_half_even(r / PI_OVER_4)
    base = r - k * PI_OVER_4
    k_mod = int(k) % 8
    sin_base = _taylor_sin(base)
    cos_base = _taylor_cos(base)
    sin_k = SIN_OCTANT[k_mod]
    cos_k = COS_OCTANT[k_mod]
    sin_r = sin_k * cos_base + cos_k * sin_base
    cos_r = cos_k * cos_base - sin_k * sin_base
    return sin_r, cos_r


def sin(x):
    return sin_cos(x)[0]


def cos(x):
    return sin_cos(x)[1]


def _frexp_bits(x):
    """IEEE-754 bit-dekompozicio: x = m * 2^e, m in [1,2). Csak x>0, veges, normal."""
    bits = struct.unpack('>Q', struct.pack('>d', x))[0]
    raw_exponent = (bits >> 52) & 0x7FF
    e = raw_exponent - 1023
    mantissa_bits = (bits & 0x000FFFFFFFFFFFFF) | (1023 << 52)
    m = struct.unpack('>d', struct.pack('>Q', mantissa_bits))[0]
    return m, e


def _scale_by_power_of_two(x, k):
    """x * 2^k, EXAKT bit-manipulacioval (nincs kerekites, ha nem tulcsordul)."""
    bits = struct.unpack('>Q', struct.pack('>d', x))[0]
    raw_exponent = (bits >> 52) & 0x7FF
    new_exponent = raw_exponent + k
    new_bits = (bits & 0x800FFFFFFFFFFFFF) | (new_exponent << 52)
    return struct.unpack('>d', struct.pack('>Q', new_bits & 0xFFFFFFFFFFFFFFFF))[0]


def ln(x):
    """x > 0. m in [1,2) -> y=(m-1)/(m+1) in [0, 1/3), ln(m)=2*atanh(y)-sor."""
    if x <= 0.0:
        raise ValueError("ln csak pozitiv x-re ertelmezett ebben a modulban")
    m, e = _frexp_bits(x)
    y = (m - 1.0) / (m + 1.0)
    y2 = y * y
    c15, c13, c11, c9, c7, c5, c3, c1 = (
        1.0 / 15.0, 1.0 / 13.0, 1.0 / 11.0, 1.0 / 9.0,
        1.0 / 7.0, 1.0 / 5.0, 1.0 / 3.0, 1.0,
    )
    poly = c15
    poly = c13 + y2 * poly
    poly = c11 + y2 * poly
    poly = c9 + y2 * poly
    poly = c7 + y2 * poly
    poly = c5 + y2 * poly
    poly = c3 + y2 * poly
    poly = c1 + y2 * poly
    ln_m = 2.0 * y * poly
    return e * LN2 + ln_m


def exp(x):
    k = _round_half_even(x / LN2)
    r = x - k * LN2  # [-ln2/2, ln2/2]
    c10, c9, c8, c7, c6, c5, c4, c3, c2 = (
        1.0 / 3628800.0, 1.0 / 362880.0, 1.0 / 40320.0, 1.0 / 5040.0,
        1.0 / 720.0, 1.0 / 120.0, 1.0 / 24.0, 1.0 / 6.0, 1.0 / 2.0,
    )
    poly = c10
    poly = c9 + r * poly
    poly = c8 + r * poly
    poly = c7 + r * poly
    poly = c6 + r * poly
    poly = c5 + r * poly
    poly = c4 + r * poly
    poly = c3 + r * poly
    poly = c2 + r * poly
    poly = 1.0 + r * poly
    exp_r = 1.0 + r * poly
    return _scale_by_power_of_two(exp_r, int(k))


def pow_(x, y):
    """x^y, x >= 0. x=0, y>0 -> 0 (dokumentalt konvencio, nem NaN)."""
    if x == 0.0:
        return 0.0
    return exp(y * ln(x))


# ---------------------------------------------------------------------------
# ND-118 kiterjesztes: Atan / Atan2 / Asin / Acos / Tanh
#
# INDOK: a DeterministicMath eddig Sin/Cos/Ln/Exp/Pow-ot fedett, de a
# klima-lanc (WindPrecipitation: Asin/Atan2/Tanh), a folyo-nyomvonal
# (RiverPathTracing: Acos) es a csillagaszat (SubsolarPoint: Asin/Atan2)
# nyers math.* hivasokat hasznal - NEM azert, mert valaki elfelejtette,
# hanem mert NEM VOLT MIRE cserelni. Ez a blokk teremti meg a lehetoseget.
#
# FONTOS: ez a fajl ONMAGABAN nem valtoztat semmit a vilagon. A meglevo
# modulok atallitasa KULON, SEED-TORO lepes (minden ratamaszkodo KAT-vektor
# ujragenerasat igenyli) - ld. ND-118 a docs/04-decisions.md-ben.
# ---------------------------------------------------------------------------

PI_OVER_2 = math.pi / 2.0

# atan Taylor-egyutthatok: atan(t) = t - t^3/3 + t^5/5 - ...
# A harmas felezes utan |t| <= 0.0985, tehat t^2 <= 0.0097 - 10 tag
# eleg a double teljes pontossagahoz (a 21. hatvany mar 1e-21 alatt van).
_ATAN_TERMS = 10


def _atan_core(t):
    """atan(t) Taylor-sorral, |t| <= ~0.1. Csak + - * / muveletek."""
    t2 = t * t
    total = 0.0
    # Hatulrol elore osszegzunk: a legkisebb tagok adodnak ossze eloszor,
    # igy a kerekitesi hiba nem nyeli el oket. A sorrend ROGZITETT, tehat
    # Pythonban es C#-ban bitre azonos.
    for k in range(_ATAN_TERMS - 1, -1, -1):
        sign = 1.0 if (k % 2 == 0) else -1.0
        total = total * t2 + sign / (2.0 * k + 1.0)
    return total * t


def atan(x):
    """atan(x) radianban. Tartomany: (-pi/2, pi/2)."""
    if x != x:            # NaN
        return x
    if x == 0.0:
        return x          # +-0.0 elojelet megtartja
    negative = x < 0.0
    a = -x if negative else x

    # 1) Reciprok-redukcio: a > 1 -> atan(a) = pi/2 - atan(1/a)
    use_complement = a > 1.0
    if use_complement:
        a = 1.0 / a

    # 2) Harmas felezes: atan(a) = 2*atan(a / (1 + sqrt(1 + a^2)))
    #    a <= 1 -> a1 <= 0.4143 -> a2 <= 0.1989 -> a3 <= 0.0985
    for _ in range(3):
        a = a / (1.0 + math.sqrt(1.0 + a * a))

    result = 8.0 * _atan_core(a)

    if use_complement:
        result = PI_OVER_2 - result
    return -result if negative else result


def atan2(y, x):
    """atan2(y, x) radianban, a szokasos kvadrans-konvencioval."""
    if x > 0.0:
        return atan(y / x)
    if x < 0.0:
        if y >= 0.0:
            return atan(y / x) + math.pi
        return atan(y / x) - math.pi
    # x == 0
    if y > 0.0:
        return PI_OVER_2
    if y < 0.0:
        return -PI_OVER_2
    return 0.0


def asin(x):
    """asin(x), |x| <= 1. A tartomanyon kivuli bemenet levagva (nem NaN)."""
    if x != x:
        return x
    negative = x < 0.0
    a = -x if negative else x
    if a >= 1.0:
        return -PI_OVER_2 if negative else PI_OVER_2

    if a <= 0.5:
        # Kozvetlen: asin(a) = atan(a / sqrt(1 - a^2))
        result = atan(a / math.sqrt(1.0 - a * a))
    else:
        # a -> 1 kozeleben az (1 - a*a) kioltana; a felszog-azonossag
        # ezt elkeruli: asin(a) = pi/2 - 2*asin(sqrt((1-a)/2))
        t = math.sqrt((1.0 - a) / 2.0)
        result = PI_OVER_2 - 2.0 * atan(t / math.sqrt(1.0 - t * t))
    return -result if negative else result


def acos(x):
    """acos(x), |x| <= 1. Tartomany: [0, pi]."""
    return PI_OVER_2 - asin(x)


# tanh: a kis-argumentumu hatar, ahol az (1 - exp(-2a)) kiolt.
# a = 1e-6-nal a relativ kiolt-hiba ~1.1e-10, a sorfejtes hibaja a^2/3
# ~3.3e-13 - tehat itt van az atvaltas.
_TANH_SMALL = 1e-6
# e^-40 < 5e-18: (1-t)/(1+t) mar 1.0-ra kerekit.
_TANH_LARGE = 20.0


def tanh(x):
    """tanh(x), a determinisztikus exp-bol epitve."""
    if x != x:
        return x
    negative = x < 0.0
    a = -x if negative else x
    if a < _TANH_SMALL:
        return x          # tanh(x) = x - x^3/3 + ..., a maradek elhanyagolhato
    if a > _TANH_LARGE:
        return -1.0 if negative else 1.0
    t = exp(-2.0 * a)
    result = (1.0 - t) / (1.0 + t)
    return -result if negative else result


if __name__ == "__main__":
    import random

    print("--- Sin/Cos plauzibilitas (valodi math.sin/cos-hoz kepest) ---")
    rnd = random.Random(12345)
    max_err_sin, max_err_cos = 0.0, 0.0
    for _ in range(20000):
        x = rnd.uniform(-200.0, 200.0)  # nagysagrendileg a PlateMotion max szoge korul
        s, c = sin_cos(x)
        max_err_sin = max(max_err_sin, abs(s - math.sin(x)))
        max_err_cos = max(max_err_cos, abs(c - math.cos(x)))
    print(f"  max |sin hiba| = {max_err_sin:.3e}, max |cos hiba| = {max_err_cos:.3e}")
    assert max_err_sin < 1e-9 and max_err_cos < 1e-9, "Tul nagy elteres a valodi sin/cos-tol"
    print("OK\n")

    print("--- Sin^2+Cos^2 = 1 (identitas-plauzibilitas) ---")
    max_identity_err = 0.0
    for _ in range(5000):
        x = rnd.uniform(-1000.0, 1000.0)
        s, c = sin_cos(x)
        max_identity_err = max(max_identity_err, abs(s * s + c * c - 1.0))
    print(f"  max |sin^2+cos^2-1| = {max_identity_err:.3e}")
    assert max_identity_err < 1e-9
    print("OK\n")

    print("--- Exp/Ln/Pow plauzibilitas ---")
    max_err_exp, max_err_ln, max_err_pow = 0.0, 0.0, 0.0
    for _ in range(20000):
        x = rnd.uniform(-20.0, 20.0)
        max_err_exp = max(max_err_exp, abs(exp(x) - math.exp(x)) / max(1.0, abs(math.exp(x))))
    for _ in range(20000):
        x = rnd.uniform(1e-6, 1e8)
        max_err_ln = max(max_err_ln, abs(ln(x) - math.log(x)))
    for _ in range(20000):
        x = rnd.uniform(1e-3, 1e5)
        y = rnd.uniform(-3.0, 3.0)
        expected = x ** y
        max_err_pow = max(max_err_pow, abs(pow_(x, y) - expected) / max(1.0, abs(expected)))
    print(f"  max relativ exp hiba = {max_err_exp:.3e}")
    print(f"  max abszolut ln hiba = {max_err_ln:.3e}")
    print(f"  max relativ pow hiba = {max_err_pow:.3e}")
    assert max_err_exp < 1e-9 and max_err_ln < 1e-8 and max_err_pow < 1e-6
    print("OK\n")

    print("--- Tisztasag ---")
    assert sin_cos(1.23456) == sin_cos(1.23456)
    assert pow_(2.5, 0.78) == pow_(2.5, 0.78)
    print("OK\n")

    print("--- Atan/Asin/Acos/Atan2 plauzibilitas (valodi math.*-hoz kepest) ---")
    max_err_atan = max_err_asin = max_err_acos = max_err_atan2 = 0.0
    for _ in range(20000):
        x = rnd.uniform(-1000.0, 1000.0)
        max_err_atan = max(max_err_atan, abs(atan(x) - math.atan(x)))
    for _ in range(20000):
        x = rnd.uniform(-1.0, 1.0)
        max_err_asin = max(max_err_asin, abs(asin(x) - math.asin(x)))
        max_err_acos = max(max_err_acos, abs(acos(x) - math.acos(x)))
    for _ in range(20000):
        y, x = rnd.uniform(-100.0, 100.0), rnd.uniform(-100.0, 100.0)
        max_err_atan2 = max(max_err_atan2, abs(atan2(y, x) - math.atan2(y, x)))
    print(f"  max |atan hiba|  = {max_err_atan:.3e}")
    print(f"  max |asin hiba|  = {max_err_asin:.3e}")
    print(f"  max |acos hiba|  = {max_err_acos:.3e}")
    print(f"  max |atan2 hiba| = {max_err_atan2:.3e}")
    assert max_err_atan < 1e-9 and max_err_asin < 1e-9
    assert max_err_acos < 1e-9 and max_err_atan2 < 1e-9
    print("OK\n")

    print("--- Asin a tartomany SZELEN (itt bukna a naiv 1-x^2 keplet) ---")
    max_err_edge = 0.0
    for a in [0.9, 0.99, 0.999, 0.99999, 0.9999999, 1.0, -1.0, -0.9999999]:
        max_err_edge = max(max_err_edge, abs(asin(a) - math.asin(a)))
    print(f"  max |asin hiba| a szelen = {max_err_edge:.3e}")
    assert max_err_edge < 1e-9
    print("OK\n")

    print("--- Tanh plauzibilitas + kis-argumentum atvaltas ---")
    max_err_tanh = 0.0
    for _ in range(20000):
        x = rnd.uniform(-25.0, 25.0)
        expected = math.tanh(x)
        max_err_tanh = max(max_err_tanh, abs(tanh(x) - expected) / max(1e-3, abs(expected)))
    for x in [1e-9, 1e-7, 1e-6, 1.000001e-6, 1e-5, 1e-3, 0.5, 19.9, 20.1, 50.0]:
        for sgn in (1.0, -1.0):
            v = sgn * x
            max_err_tanh = max(max_err_tanh, abs(tanh(v) - math.tanh(v)) / max(1e-3, abs(math.tanh(v))))
    print(f"  max relativ tanh hiba = {max_err_tanh:.3e}")
    assert max_err_tanh < 1e-9
    print("OK\n")

    print("--- Identitasok ---")
    for _ in range(5000):
        x = rnd.uniform(-1.0, 1.0)
        assert abs(asin(x) + acos(x) - PI_OVER_2) < 1e-12
        assert abs(sin(asin(x)) - x) < 1e-9
    for _ in range(5000):
        y, x = rnd.uniform(-50.0, 50.0), rnd.uniform(-50.0, 50.0)
        r = math.sqrt(x * x + y * y)
        if r < 1e-9:
            continue
        a = atan2(y, x)
        s_, c_ = sin_cos(a)
        assert abs(c_ * r - x) < 1e-7 and abs(s_ * r - y) < 1e-7
    print("OK\n")

    print("--- Elojel- es elfajult esetek ---")
    assert atan(0.0) == 0.0 and atan2(0.0, 1.0) == 0.0
    assert atan2(1.0, 0.0) == PI_OVER_2 and atan2(-1.0, 0.0) == -PI_OVER_2
    assert atan2(0.0, 0.0) == 0.0
    assert asin(1.0) == PI_OVER_2 and asin(-1.0) == -PI_OVER_2
    assert asin(1.5) == PI_OVER_2 and asin(-1.5) == -PI_OVER_2
    assert acos(1.0) == 0.0
    assert tanh(0.0) == 0.0 and tanh(1e9) == 1.0 and tanh(-1e9) == -1.0
    print("OK\n")

    print("--- Tisztasag (uj fuggvenyek) ---")
    assert atan(0.7) == atan(0.7) and asin(0.3) == asin(0.3)
    assert atan2(1.0, -2.0) == atan2(1.0, -2.0) and tanh(0.9) == tanh(0.9)
    print("OK\n")

    print("--- Tesztvektorok generalasa (C# porthoz) ---")
    import json
    from threefry_ref import threefry4x64

    sincos_vectors, pow_vectors = [], []
    gen_seed = 0xDE7E90000000001
    for i in range(500):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 21, 0], 20)
        x = -1000.0 + (p[0] / 2 ** 64) * 2000.0  # [-1000, 1000)
        s, c = sin_cos(x)
        sincos_vectors.append({"x": x, "sin": s, "cos": c})

        base = 1e-3 + (p[1] / 2 ** 64) * (1e5 - 1e-3)
        exponent = -3.0 + (p[2] / 2 ** 64) * 6.0
        pow_vectors.append({"x": base, "y": exponent, "result": pow_(base, exponent)})

    # ND-118: az UJ fuggvenyek vektorai KULON threefry-kulccsal (property=22),
    # hogy a fenti sinCos/pow vektorok BITRE VALTOZATLANOK maradjanak - azokra
    # mar meglevo C#-tesztek epulnek.
    atan_vectors, asin_vectors, atan2_vectors, tanh_vectors = [], [], [], []
    for i in range(500):
        q = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 22, 0], 20)
        xa = -1000.0 + (q[0] / 2 ** 64) * 2000.0
        atan_vectors.append({"x": xa, "result": atan(xa)})

        xs = -1.0 + (q[1] / 2 ** 64) * 2.0
        asin_vectors.append({"x": xs, "asin": asin(xs), "acos": acos(xs)})

        yy = -100.0 + (q[2] / 2 ** 64) * 200.0
        xx = -100.0 + (q[3] / 2 ** 64) * 200.0
        atan2_vectors.append({"y": yy, "x": xx, "result": atan2(yy, xx)})

        r = threefry4x64([i, 1, 0, 0], [gen_seed, 0, 22, 0], 20)
        xt = -25.0 + (r[0] / 2 ** 64) * 50.0
        tanh_vectors.append({"x": xt, "result": tanh(xt)})

    with open("deterministic_math_vectors.json", "w", newline="\n") as f:
        json.dump({
            "sinCos": sincos_vectors,
            "pow": pow_vectors,
            "atan": atan_vectors,
            "asin": asin_vectors,
            "atan2": atan2_vectors,
            "tanh": tanh_vectors,
        }, f, indent=1)
    print(f"{len(sincos_vectors)} sin/cos + {len(pow_vectors)} pow + "
          f"{len(atan_vectors)} atan + {len(asin_vectors)} asin/acos + "
          f"{len(atan2_vectors)} atan2 + {len(tanh_vectors)} tanh tesztvektor elmentve")
