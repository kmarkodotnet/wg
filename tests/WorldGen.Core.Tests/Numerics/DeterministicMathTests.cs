using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Numerics;
using Xunit;

namespace WorldGen.Core.Tests.Numerics;

/// <summary>
/// ND-27 lezárása: a DeterministicMath a Python-referenciával (ugyanaz az
/// algoritmus, ugyanazok a bitmanipulációk) BITPONTOSAN kell egyezzen -
/// ez NEM tolerancia-alapú teszt, mert mindkét oldal ugyanazt a saját
/// (nem System-könyvtári) algoritmust futtatja.
/// </summary>
public class DeterministicMathVectorFileTests
{
    [Fact]
    public void SinCosMatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "deterministic_math_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("sinCos").EnumerateArray())
        {
            double x = v.GetProperty("x").GetDouble();
            double expectedSin = v.GetProperty("sin").GetDouble();
            double expectedCos = v.GetProperty("cos").GetDouble();

            DeterministicMath.SinCos(x, out double sin, out double cos);

            Assert.Equal(expectedSin, sin);
            Assert.Equal(expectedCos, cos);
            checkedCount++;
        }
        Assert.Equal(500, checkedCount);
    }

    [Fact]
    public void PowMatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "deterministic_math_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("pow").EnumerateArray())
        {
            double x = v.GetProperty("x").GetDouble();
            double y = v.GetProperty("y").GetDouble();
            double expected = v.GetProperty("result").GetDouble();

            double got = DeterministicMath.Pow(x, y);

            Assert.Equal(expected, got);
            checkedCount++;
        }
        Assert.Equal(500, checkedCount);
    }

    // ---- ND-118: Atan / Atan2 / Asin / Acos / Tanh ----
    //
    // Ugyanaz az elv, mint fent: NINCS tolerancia. A Python és a C# UGYANAZT
    // a műveleti sorrendet futtatja, csak IEEE-754 szerint bitpontos
    // műveletekből (+ - * /, Math.Sqrt), ezért az egyezésnek bitre kell állnia.
    // Ha ez valaha elbukik, az NEM "pontatlanság", hanem azt jelenti, hogy a
    // két implementáció algoritmikusan szétcsúszott.

    private static JsonElement Vectors(string key)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "deterministic_math_vectors.json");
        JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty(key).Clone();
    }

    [Fact]
    public void AtanMatchesPythonReferenceExactly()
    {
        int checkedCount = 0;
        foreach (JsonElement v in Vectors("atan").EnumerateArray())
        {
            double x = v.GetProperty("x").GetDouble();
            Assert.Equal(v.GetProperty("result").GetDouble(), DeterministicMath.Atan(x));
            checkedCount++;
        }
        Assert.Equal(500, checkedCount);
    }

    [Fact]
    public void AsinAndAcosMatchPythonReferenceExactly()
    {
        int checkedCount = 0;
        foreach (JsonElement v in Vectors("asin").EnumerateArray())
        {
            double x = v.GetProperty("x").GetDouble();
            Assert.Equal(v.GetProperty("asin").GetDouble(), DeterministicMath.Asin(x));
            Assert.Equal(v.GetProperty("acos").GetDouble(), DeterministicMath.Acos(x));
            checkedCount++;
        }
        Assert.Equal(500, checkedCount);
    }

    [Fact]
    public void Atan2MatchesPythonReferenceExactly()
    {
        int checkedCount = 0;
        foreach (JsonElement v in Vectors("atan2").EnumerateArray())
        {
            double y = v.GetProperty("y").GetDouble();
            double x = v.GetProperty("x").GetDouble();
            Assert.Equal(v.GetProperty("result").GetDouble(), DeterministicMath.Atan2(y, x));
            checkedCount++;
        }
        Assert.Equal(500, checkedCount);
    }

    [Fact]
    public void TanhMatchesPythonReferenceExactly()
    {
        int checkedCount = 0;
        foreach (JsonElement v in Vectors("tanh").EnumerateArray())
        {
            double x = v.GetProperty("x").GetDouble();
            Assert.Equal(v.GetProperty("result").GetDouble(), DeterministicMath.Tanh(x));
            checkedCount++;
        }
        Assert.Equal(500, checkedCount);
    }
}

public class DeterministicMathStructuralTests
{
    [Fact]
    public void IsPure()
    {
        DeterministicMath.SinCos(1.23456, out double s1, out double c1);
        DeterministicMath.SinCos(1.23456, out double s2, out double c2);
        Assert.Equal(s1, s2);
        Assert.Equal(c1, c2);

        Assert.Equal(DeterministicMath.Pow(2.5, 0.78), DeterministicMath.Pow(2.5, 0.78));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(100.0)]
    [InlineData(-500.0)]
    [InlineData(1000.0)]
    public void SinSquaredPlusCosSquaredIsOne(double x)
    {
        DeterministicMath.SinCos(x, out double s, out double c);
        Assert.True(Math.Abs(s * s + c * c - 1.0) < 1e-9, $"sin^2+cos^2 != 1 x={x}: {s * s + c * c}");
    }

    [Fact]
    public void SinCosMatchesKnownValuesAtOctantBoundaries()
    {
        DeterministicMath.SinCos(0.0, out double s0, out double c0);
        Assert.True(Math.Abs(s0 - 0.0) < 1e-9);
        Assert.True(Math.Abs(c0 - 1.0) < 1e-9);

        DeterministicMath.SinCos(Math.PI / 2.0, out double s90, out double c90);
        Assert.True(Math.Abs(s90 - 1.0) < 1e-9);
        Assert.True(Math.Abs(c90 - 0.0) < 1e-9);

        DeterministicMath.SinCos(Math.PI, out double s180, out double c180);
        Assert.True(Math.Abs(s180 - 0.0) < 1e-9);
        Assert.True(Math.Abs(c180 - (-1.0)) < 1e-9);
    }

    [Fact]
    public void SinIsPeriodic()
    {
        double x = 1.9;
        DeterministicMath.SinCos(x, out double s1, out double c1);
        DeterministicMath.SinCos(x + 2.0 * Math.PI, out double s2, out double c2);
        DeterministicMath.SinCos(x - 4.0 * Math.PI, out double s3, out double c3);

        Assert.True(Math.Abs(s1 - s2) < 1e-9);
        Assert.True(Math.Abs(c1 - c2) < 1e-9);
        Assert.True(Math.Abs(s1 - s3) < 1e-9);
        Assert.True(Math.Abs(c1 - c3) < 1e-9);
    }

    [Fact]
    public void HandlesLargeAngleWithoutBlowingUp()
    {
        DeterministicMath.SinCos(90.0, out double s, out double c); // PlateMotion max nagysagrend
        Assert.True(Math.Abs(s * s + c * c - 1.0) < 1e-9);
    }

    [Fact]
    public void PowOfZeroBaseIsZero()
    {
        Assert.Equal(0.0, DeterministicMath.Pow(0.0, 0.78));
    }

    [Fact]
    public void PowOfOneIsAlwaysOne()
    {
        Assert.True(Math.Abs(DeterministicMath.Pow(1.0, 5.5) - 1.0) < 1e-9);
    }

    [Fact]
    public void PowExponentZeroIsAlwaysOne()
    {
        Assert.True(Math.Abs(DeterministicMath.Pow(42.0, 0.0) - 1.0) < 1e-9);
    }

    [Fact]
    public void PowMatchesSystemMathWithinPlausibilityTolerance()
    {
        Assert.True(Math.Abs(DeterministicMath.Pow(1000.0, 0.78) - Math.Pow(1000.0, 0.78)) < 1e-3);
        Assert.True(Math.Abs(DeterministicMath.Pow(20000.0, 0.44) - Math.Pow(20000.0, 0.44)) < 1e-3);
    }

    [Fact]
    public void LnThrowsForNonPositiveInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicMath.Ln(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicMath.Ln(-1.0));
    }
}

/// <summary>
/// ND-150 (A21): a DeterministicMath Exp/Ln/Pow ÉLESETEI.
///
/// A HIBA, amit ezek fognak meg: az <see cref="DeterministicMath.Exp"/> a
/// végeredményt bit-manipulációval skálázta, és NEM kezelte az exponens
/// tartományon kívülre csúszását. Kb. -710 alatt a levont exponens
/// átcsordult az ELŐJELBITBE, és az eredmény nem 0-hoz tartott, hanem
/// determinisztikus SZEMÉT lett (mérve az ND-150 előtt: exp(-710) =
/// -1,4466e+308, exp(-750) = -6,1457e+290, exp(-4e6) = +4,4595e+145,
/// exp(710) = NaN, exp(1e6) = 6,9566e+271, pow(10, 400) = -3,0943e-217).
/// Ugyanez a Ln-nél: ln(+végtelen) = 709,78, ln(NaN) = 710,19,
/// ln(subnormális) 35 nagyságrendet tévedett.
///
/// Miért KOMOLY: egy csendes, determinisztikus szemét rosszabb, mint egy
/// robbanás — az I1 (determinizmus) nem sérül, de az I3/I4 igen, és a hibát
/// semmi nem jelzi. A korlát ezért a DeterministicMath-ba került, nem
/// modulonként megismételve.
/// </summary>
public class DeterministicMathEdgeCaseTests
{
    // ---------------- Exp: alulcsordulás ----------------

    [Theory]
    [InlineData(-709.0)]
    [InlineData(-710.0)]
    [InlineData(-745.0)]
    [InlineData(-750.0)]
    [InlineData(-1000.0)]
    [InlineData(-1e6)]
    [InlineData(-4e6)]
    [InlineData(-1e300)]
    [InlineData(double.NegativeInfinity)]
    public void ExpUnderflowsToExactZeroNeverToGarbage(double x)
    {
        double got = DeterministicMath.Exp(x);
        Assert.Equal(0.0, got);
        Assert.False(double.IsNaN(got));
    }

    /// <summary>
    /// A LÉNYEG: a régi hiba NEGATÍV (ill. felfelé ugró) értéket adott. Az
    /// exp SOHA nem lehet negatív, és monoton csökkenőnek kell lennie a
    /// teljes alulcsordulási átmeneten át.
    /// </summary>
    [Fact]
    public void ExpIsNonNegativeAndMonotonicAcrossTheUnderflowBoundary()
    {
        double previous = double.MaxValue;
        int samples = 0;
        for (double x = -700.0; x > -760.0; x -= 0.25)
        {
            double got = DeterministicMath.Exp(x);
            Assert.False(double.IsNaN(got));
            Assert.True(got >= 0.0, "exp(" + x + ") = " + got + " negatív");
            Assert.True(got <= previous, "exp(" + x + ") = " + got + " nagyobb az előzőnél");
            previous = got;
            samples++;
        }
        Assert.Equal(240, samples);
    }

    // ---------------- Exp: túlcsordulás ----------------

    [Theory]
    [InlineData(710.0)]
    [InlineData(711.0)]
    [InlineData(1000.0)]
    [InlineData(1e6)]
    [InlineData(1e300)]
    [InlineData(double.PositiveInfinity)]
    public void ExpOverflowsToPositiveInfinityNeverToGarbage(double x)
    {
        double got = DeterministicMath.Exp(x);
        Assert.True(double.IsPositiveInfinity(got), "exp(" + x + ") = " + got);
    }

    [Fact]
    public void ExpIsNonDecreasingAcrossTheOverflowBoundary()
    {
        double previous = 0.0;
        for (double x = 700.0; x < 760.0; x += 0.25)
        {
            double got = DeterministicMath.Exp(x);
            Assert.False(double.IsNaN(got));
            Assert.True(got >= previous, "exp(" + x + ") = " + got + " kisebb az előzőnél");
            previous = got;
        }
        Assert.True(double.IsPositiveInfinity(previous));
    }

    [Fact]
    public void ExpPropagatesNaN()
    {
        Assert.True(double.IsNaN(DeterministicMath.Exp(double.NaN)));
    }

    /// <summary>
    /// A VÉDETT tartomány BITRE VÁLTOZATLAN: a javítás csak azt érinti, ami
    /// eddig szemét volt. A felső határ az utolsó véges érték (~709,78), az
    /// alsó az, ahol a bit-eltolás még normál double-t ad (~-708,396).
    /// </summary>
    [Fact]
    public void ExpStaysFiniteAndPositiveInsideTheValidRange()
    {
        foreach (double x in new[] { -708.0, -700.0, -100.0, -1.0, 0.0, 1.0, 100.0, 700.0, 709.0 })
        {
            double got = DeterministicMath.Exp(x);
            Assert.True(got > 0.0 && !double.IsInfinity(got), "exp(" + x + ") = " + got);
        }
        Assert.Equal(1.0, DeterministicMath.Exp(0.0));
    }

    // ---------------- Ln ----------------

    [Fact]
    public void LnPropagatesNaNInsteadOfReturningGarbage()
    {
        // ND-150 előtt: 710,188178001492 — a NaN <= 0 összehasonlítás hamis,
        // így a bitbontás simán lefutott a NaN bitminta exponensén.
        Assert.True(double.IsNaN(DeterministicMath.Ln(double.NaN)));
    }

    [Fact]
    public void LnOfPositiveInfinityIsPositiveInfinity()
    {
        // ND-150 előtt: 709,782712893384 (a 0x7FF nyers exponensből).
        Assert.True(double.IsPositiveInfinity(DeterministicMath.Ln(double.PositiveInfinity)));
    }

    /// <summary>
    /// Subnormális bemenet: a FrexpBits bitbontása itt hamis mantisszát ad.
    /// ND-150 előtt ln(5e-324) = -709,09 volt a helyes -744,44 helyett.
    /// </summary>
    [Theory]
    [InlineData(double.Epsilon)]      // 2^-1074, a legkisebb subnormális
    [InlineData(1e-320)]
    [InlineData(1e-310)]
    [InlineData(2.0e-308)]
    public void LnHandlesSubnormalInput(double x)
    {
        double got = DeterministicMath.Ln(x);
        // Plauzibilitás a System.Math ellen (nem bitpontos elvárás — az Ln
        // polinomja 1e-8 abszolút hibára van méretezve, ld. ND-27).
        Assert.True(Math.Abs(got - Math.Log(x)) < 1e-8, "ln(" + x + ") = " + got + ", Math.Log = " + Math.Log(x));
    }

    [Fact]
    public void LnIsMonotonicAcrossTheSubnormalBoundary()
    {
        double minNormal = 2.2250738585072014E-308; // 2^-1022
        double previous = DeterministicMath.Ln(minNormal * 4.0);
        foreach (double x in new[] { minNormal * 2.0, minNormal, minNormal / 2.0, minNormal / 4.0, 1e-320, double.Epsilon })
        {
            double got = DeterministicMath.Ln(x);
            Assert.True(got < previous, "ln(" + x + ") = " + got + " nem kisebb az előzőnél");
            previous = got;
        }
    }

    /// <summary>A normál ág BITRE VÁLTOZATLAN: exp(ln(x)) visszaadja x-et.</summary>
    [Fact]
    public void LnRoundTripsThroughExpInsideTheNormalRange()
    {
        foreach (double x in new[] { 1e-300, 1e-100, 1e-3, 1.0, 2.0, 1e3, 1e100, 1e300 })
        {
            double back = DeterministicMath.Exp(DeterministicMath.Ln(x));
            Assert.True(Math.Abs(back - x) <= 1e-9 * Math.Abs(x), "exp(ln(" + x + ")) = " + back);
        }
    }

    // ---------------- Pow ----------------

    [Fact]
    public void PowOverflowsToInfinityAndUnderflowsToZero()
    {
        // ND-150 előtt: -3,0943e-217 illetve -3,2317e+216 — mindkettő szemét,
        // és az ELŐJEL is rossz volt.
        Assert.True(double.IsPositiveInfinity(DeterministicMath.Pow(10.0, 400.0)));
        Assert.Equal(0.0, DeterministicMath.Pow(10.0, -400.0));
    }

    [Fact]
    public void PowKeepsTheDocumentedZeroBaseConvention()
    {
        // A x == 0 vizsgálat SZÁNDÉKOSAN az y == 0 előtt van: a 0^0 = 0,0
        // konvenció az ND-27 óta él, és nem változtatható csendben.
        Assert.Equal(0.0, DeterministicMath.Pow(0.0, 0.0));
        Assert.Equal(0.0, DeterministicMath.Pow(0.0, 2.0));
    }

    [Fact]
    public void PowWithZeroExponentIsExactlyOneEvenForInfiniteBase()
    {
        Assert.Equal(1.0, DeterministicMath.Pow(42.0, 0.0));
        Assert.Equal(1.0, DeterministicMath.Pow(double.PositiveInfinity, 0.0));
    }

    [Fact]
    public void PowPropagatesNaN()
    {
        Assert.True(double.IsNaN(DeterministicMath.Pow(double.NaN, 2.0)));
        Assert.True(double.IsNaN(DeterministicMath.Pow(2.0, double.NaN)));
    }

    // ---------------- Tisztaság (I2) ----------------

    [Fact]
    public void EdgeCaseResultsAreRepeatable()
    {
        foreach (double x in new[] { -750.0, -710.0, 710.0, 1e6, double.NaN })
        {
            double a = DeterministicMath.Exp(x);
            double b = DeterministicMath.Exp(x);
            Assert.Equal(BitConverter.DoubleToInt64Bits(a), BitConverter.DoubleToInt64Bits(b));
        }
    }
}
