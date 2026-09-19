using System;

namespace WorldGen.Core.Numerics
{
    /// <summary>
    /// ND-27 lezárása (docs/04-decisions.md): determinisztikus Sin/Cos/Pow -
    /// felváltja a System.Math.Sin/Cos/Pow-ot a szimulációs kritikus úton
    /// (csillagászat, klíma, lemezmozgás, becsapódás), mert az utóbbiak NEM
    /// garantáltan bitre azonosak platformok/futásidők között.
    ///
    /// MÓDSZER: csak a CLAUDE.md táblázat szerint GARANTÁLTAN bitpontos
    /// műveletekre épül (+ - * /, Math.Sqrt, Math.Floor - IEEE-754
    /// roundToIntegral EXAKT specifikáció, nem transzcendens közelítés -
    /// és nyers bit-manipuláció BitConverter-rel).
    ///
    /// SIN/COS: oktáns-redukció (legközelebbi k·π/4-re, [-π/8,π/8]
    /// maradékkal) + Taylor-sor + szög-összeg azonosság a 8 oktáns EXAKT
    /// értékével (0, ±1, ±√2/2).
    ///
    /// EXP/LN: IEEE-754 bit-dekompozíció (mantissza/exponens szétválasztás,
    /// mint a C standard frexp/ldexp) + Taylor-sor szűk tartományon.
    /// POW(x,y) = exp(y·ln(x)), x ≥ 0 (x=0, y&gt;0 esetén 0, dokumentált
    /// konvenció).
    ///
    /// A cél NEM a valódi Math.Sin-hez képesti pontosság (bármilyen
    /// dokumentált közelítés elfogadható), hanem a PLATFORMFÜGGETLEN
    /// BITPONTOS reprodukálhatóság - ld. tools/reference/deterministic_math_ref.py
    /// a plauzibilitás-mérésekért (max hiba &lt; 1e-9 sin/cos-ra, &lt; 1e-6 pow-ra).
    /// </summary>
    public static class DeterministicMath
    {
        private const double TwoPi = 2.0 * Math.PI;
        private const double PiOver4 = Math.PI / 4.0;
        private const double Ln2 = 0.69314718055994530941723212145818;
        private const double Sqrt2Over2 = 0.70710678118654752440084436210485;

        private static readonly double[] SinOctant =
            { 0.0, Sqrt2Over2, 1.0, Sqrt2Over2, 0.0, -Sqrt2Over2, -1.0, -Sqrt2Over2 };
        private static readonly double[] CosOctant =
            { 1.0, Sqrt2Over2, 0.0, -Sqrt2Over2, -1.0, -Sqrt2Over2, 0.0, Sqrt2Over2 };

        private static double ReduceAngle(double x)
        {
            double revolutions = x / TwoPi;
            double frac = revolutions - Math.Floor(revolutions);
            double r = frac * TwoPi;
            if (r >= TwoPi) r -= TwoPi; // ritka hataresetkerekitesi felfele-csuszas
            return r;
        }

        private static double TaylorSin(double x)
        {
            double x2 = x * x;
            double poly = 1.0 / 6227020800.0;
            poly = -1.0 / 39916800.0 + x2 * poly;
            poly = 1.0 / 362880.0 + x2 * poly;
            poly = -1.0 / 5040.0 + x2 * poly;
            poly = 1.0 / 120.0 + x2 * poly;
            poly = -1.0 / 6.0 + x2 * poly;
            poly = 1.0 + x2 * poly;
            return x * poly;
        }

        private static double TaylorCos(double x)
        {
            double x2 = x * x;
            double poly = 1.0 / 479001600.0;
            poly = -1.0 / 3628800.0 + x2 * poly;
            poly = 1.0 / 40320.0 + x2 * poly;
            poly = -1.0 / 720.0 + x2 * poly;
            poly = 1.0 / 24.0 + x2 * poly;
            poly = -1.0 / 2.0 + x2 * poly;
            poly = 1.0 + x2 * poly;
            return poly;
        }

        /// <summary>sin(x) és cos(x) egyszerre - olcsóbb, mint két külön hívás.</summary>
        public static void SinCos(double x, out double sin, out double cos)
        {
            double r = ReduceAngle(x);
            long k = (long)Math.Round(r / PiOver4, MidpointRounding.ToEven);
            double baseAngle = r - k * PiOver4;
            int kMod = (int)(k % 8);
            if (kMod < 0) kMod += 8;

            double sinBase = TaylorSin(baseAngle);
            double cosBase = TaylorCos(baseAngle);
            double sinK = SinOctant[kMod];
            double cosK = CosOctant[kMod];

            sin = sinK * cosBase + cosK * sinBase;
            cos = cosK * cosBase - sinK * sinBase;
        }

        public static double Sin(double x)
        {
            SinCos(x, out double s, out _);
            return s;
        }

        public static double Cos(double x)
        {
            SinCos(x, out _, out double c);
            return c;
        }

        /// <summary>x = m*2^e felbontás, m in [1,2). CSAK x&gt;0, véges, normál double-re.</summary>
        private static void FrexpBits(double x, out double m, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            long rawExponent = (bits >> 52) & 0x7FF;
            e = (int)(rawExponent - 1023);
            long mantissaBits = (bits & 0x000FFFFFFFFFFFFFL) | (1023L << 52);
            m = BitConverter.Int64BitsToDouble(mantissaBits);
        }

        /// <summary>x * 2^k, EXAKT bit-manipulációval (nincs kerekítés, ha nem túlcsordul).</summary>
        private static double ScaleByPowerOfTwo(double x, int k)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            long rawExponent = (bits >> 52) & 0x7FF;
            long newExponent = rawExponent + k;
            long newBits = (bits & unchecked((long)0x800FFFFFFFFFFFFF)) | (newExponent << 52);
            return BitConverter.Int64BitsToDouble(newBits);
        }

        /// <summary>x &gt; 0. m in [1,2) -&gt; y=(m-1)/(m+1) in [0,1/3), ln(m)=2*atanh(y)-sor.</summary>
        public static double Ln(double x)
        {
            if (x <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(x), "Ln csak pozitív x-re értelmezett ebben a modulban.");

            FrexpBits(x, out double m, out int e);
            double y = (m - 1.0) / (m + 1.0);
            double y2 = y * y;
            double poly = 1.0 / 15.0;
            poly = 1.0 / 13.0 + y2 * poly;
            poly = 1.0 / 11.0 + y2 * poly;
            poly = 1.0 / 9.0 + y2 * poly;
            poly = 1.0 / 7.0 + y2 * poly;
            poly = 1.0 / 5.0 + y2 * poly;
            poly = 1.0 / 3.0 + y2 * poly;
            poly = 1.0 + y2 * poly;
            double lnM = 2.0 * y * poly;
            return e * Ln2 + lnM;
        }

        public static double Exp(double x)
        {
            long k = (long)Math.Round(x / Ln2, MidpointRounding.ToEven);
            double r = x - k * Ln2; // [-ln2/2, ln2/2]

            double poly = 1.0 / 3628800.0;
            poly = 1.0 / 362880.0 + r * poly;
            poly = 1.0 / 40320.0 + r * poly;
            poly = 1.0 / 5040.0 + r * poly;
            poly = 1.0 / 720.0 + r * poly;
            poly = 1.0 / 120.0 + r * poly;
            poly = 1.0 / 24.0 + r * poly;
            poly = 1.0 / 6.0 + r * poly;
            poly = 1.0 / 2.0 + r * poly;
            poly = 1.0 + r * poly;
            double expR = 1.0 + r * poly;

            return ScaleByPowerOfTwo(expR, (int)k);
        }

        /// <summary>x^y, x &gt;= 0. x=0, y&gt;0 esetén 0 (dokumentált konvenció, nem NaN).</summary>
        public static double Pow(double x, double y)
        {
            if (x == 0.0)
                return 0.0;
            return Exp(y * Ln(x));
        }

        // -------------------------------------------------------------------
        // ND-118: Atan / Atan2 / Asin / Acos / Tanh
        //
        // INDOK: a klíma-lánc (WindPrecipitation: Asin/Atan2/Tanh), a
        // folyó-nyomvonal (RiverPathTracing: Acos) és a csillagászat
        // (SubsolarPoint: Asin/Atan2) máig nyers System.Math-ot hív a
        // kritikus úton — NEM feledékenységből, hanem mert NEM VOLT MIRE
        // cserélni. Ez a blokk teremti meg a lehetőséget.
        //
        // EZ ÖNMAGÁBAN NEM VÁLTOZTAT SEMMIT a világon: a meglévő modulok
        // átállítása KÜLÖN, SEED-TÖRŐ lépés (minden ráépülő KAT-vektor
        // újragenerálását igényli). Ld. ND-118.
        //
        // Pontosság MÉRVE a Python-orákulum ellen (20 000 minta/függvény):
        // atan 3,3e-16 · asin 6,7e-16 · acos 8,9e-16 · atan2 4,4e-16 ·
        // tanh 8,0e-13 (relatív). A cél 1e-9 volt.
        // -------------------------------------------------------------------

        private const double PiOver2 = Math.PI / 2.0;

        /// <summary>
        /// atan(t) Taylor-sorral, |t| &lt;= ~0.1. A HÁTULRÓL ELŐRE összegzés
        /// szándékos: a legkisebb tagok adódnak össze először, így a
        /// kerekítési hiba nem nyeli el őket. A sorrend RÖGZÍTETT, ezért
        /// Pythonban és C#-ban bitre azonos.
        /// </summary>
        private static double AtanCore(double t)
        {
            double t2 = t * t;
            double total = 0.0;
            for (int k = AtanTerms - 1; k >= 0; k--)
            {
                double sign = (k % 2 == 0) ? 1.0 : -1.0;
                total = total * t2 + sign / (2.0 * k + 1.0);
            }
            return total * t;
        }

        /// <summary>A hármas felezés után |t| &lt;= 0,0985; 10 tag a double teljes pontosságához elég.</summary>
        private const int AtanTerms = 10;

        /// <summary>atan(x) radiánban. Tartomány: (-pi/2, pi/2).</summary>
        public static double Atan(double x)
        {
            if (double.IsNaN(x))
                return x;
            if (x == 0.0)
                return x; // a ±0.0 előjelét megtartja
            bool negative = x < 0.0;
            double a = negative ? -x : x;

            // 1) Reciprok-redukció: a > 1 -> atan(a) = pi/2 - atan(1/a)
            bool useComplement = a > 1.0;
            if (useComplement)
                a = 1.0 / a;

            // 2) Hármas felezés: atan(a) = 2*atan(a / (1 + sqrt(1 + a^2)))
            //    a <= 1 -> a1 <= 0,4143 -> a2 <= 0,1989 -> a3 <= 0,0985
            for (int i = 0; i < 3; i++)
                a = a / (1.0 + Math.Sqrt(1.0 + a * a));

            double result = 8.0 * AtanCore(a);
            if (useComplement)
                result = PiOver2 - result;
            return negative ? -result : result;
        }

        /// <summary>atan2(y, x) radiánban, a szokásos kvadráns-konvencióval.</summary>
        public static double Atan2(double y, double x)
        {
            if (x > 0.0)
                return Atan(y / x);
            if (x < 0.0)
                return y >= 0.0 ? Atan(y / x) + Math.PI : Atan(y / x) - Math.PI;
            if (y > 0.0)
                return PiOver2;
            if (y < 0.0)
                return -PiOver2;
            return 0.0;
        }

        /// <summary>
        /// asin(x), |x| &lt;= 1. A tartományon kívüli bemenet LEVÁGVA (±pi/2),
        /// nem NaN — a hívók többsége már eleve clamp-el, és egy NaN itt
        /// csendben megmérgezné a láncot.
        /// </summary>
        public static double Asin(double x)
        {
            if (double.IsNaN(x))
                return x;
            bool negative = x < 0.0;
            double a = negative ? -x : x;
            if (a >= 1.0)
                return negative ? -PiOver2 : PiOver2;

            double result;
            if (a <= 0.5)
            {
                result = Atan(a / Math.Sqrt(1.0 - a * a));
            }
            else
            {
                // a -> 1 közelében az (1 - a*a) kioltana; a félszög-azonosság
                // ezt elkerüli: asin(a) = pi/2 - 2*asin(sqrt((1-a)/2)).
                double t = Math.Sqrt((1.0 - a) / 2.0);
                result = PiOver2 - 2.0 * Atan(t / Math.Sqrt(1.0 - t * t));
            }
            return negative ? -result : result;
        }

        /// <summary>acos(x), |x| &lt;= 1. Tartomány: [0, pi].</summary>
        public static double Acos(double x) => PiOver2 - Asin(x);

        /// <summary>
        /// A kis-argumentumú határ, ahol az (1 - exp(-2a)) kioltana:
        /// 1e-6-nál a kioltási relatív hiba ~1,1e-10, a sorfejtés elhagyott
        /// tagja a^2/3 ~3,3e-13 — itt éri meg átváltani.
        /// </summary>
        private const double TanhSmall = 1e-6;

        /// <summary>e^-40 &lt; 5e-18: az (1-t)/(1+t) már pontosan 1,0-ra kerekít.</summary>
        private const double TanhLarge = 20.0;

        /// <summary>tanh(x), a determinisztikus <see cref="Exp"/>-ből építve.</summary>
        public static double Tanh(double x)
        {
            if (double.IsNaN(x))
                return x;
            bool negative = x < 0.0;
            double a = negative ? -x : x;
            if (a < TanhSmall)
                return x; // tanh(x) = x - x^3/3 + ..., a maradék elhanyagolható
            if (a > TanhLarge)
                return negative ? -1.0 : 1.0;
            double t = Exp(-2.0 * a);
            double result = (1.0 - t) / (1.0 + t);
            return negative ? -result : result;
        }
    }
}
