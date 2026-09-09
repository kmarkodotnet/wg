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
