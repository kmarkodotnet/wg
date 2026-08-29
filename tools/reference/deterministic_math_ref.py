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

    with open("deterministic_math_vectors.json", "w", newline="\n") as f:
        json.dump({"sinCos": sincos_vectors, "pow": pow_vectors}, f, indent=1)
    print(f"{len(sincos_vectors)} sin/cos + {len(pow_vectors)} pow tesztvektor elmentve")
