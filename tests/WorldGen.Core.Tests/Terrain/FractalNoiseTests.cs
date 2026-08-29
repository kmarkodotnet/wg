using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Terrain;

/// <summary>
/// A Python referenciával (tools/reference/noise_ref.py) BITPONTOS
/// egyezés várt - a zaj csak +,-,*,/ és a már verifikált
/// SampleUnitVector3-at használja, nincs tolerancia.
/// </summary>
public class FractalNoiseVectorFileTests
{
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "noise_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            double x = v.GetProperty("x").GetDouble();
            double y = v.GetProperty("y").GetDouble();
            double z = v.GetProperty("z").GetDouble();
            double expected = v.GetProperty("fbm").GetDouble();

            double got = FractalNoise.Fbm(worldSeed, x, y, z);

            Assert.Equal(expected, got);
            checkedCount++;
        }
        Assert.True(checkedCount > 400);
    }
}

public class FractalNoiseStructuralTests
{
    [Fact]
    public void IsPure()
    {
        double a = FractalNoise.Fbm(1, 0.5123, 0.3456, 0.7891);
        double b = FractalNoise.Fbm(1, 0.5123, 0.3456, 0.7891);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeedsGiveDifferentValues()
    {
        double a = FractalNoise.Fbm(1, 0.5123, 0.3456, 0.7891);
        double b = FractalNoise.Fbm(2, 0.5123, 0.3456, 0.7891);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ValueRangeIsPlausible()
    {
        var rnd = new System.Random(42);
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < 5000; i++)
        {
            double x = rnd.NextDouble() * 2 - 1;
            double y = rnd.NextDouble() * 2 - 1;
            double z = rnd.NextDouble() * 2 - 1;
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-9) continue;
            x /= len; y /= len; z /= len;

            double v = FractalNoise.Fbm(0xA7C944210000UL, x, y, z);
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        Assert.True(min > -1.5 && max < 1.5, $"Nem plauzibilis tartomány: [{min}, {max}]");
    }

    [Fact]
    public void IsSpatiallyCoherentNotWhiteNoise()
    {
        const double eps = 1e-4;
        double baseValue = FractalNoise.Fbm(1, 0.5123, 0.3456, 0.7891);
        double nearValue = FractalNoise.Fbm(1, 0.5123 + eps, 0.3456 + eps, 0.7891);
        double farValue = FractalNoise.Fbm(1, 0.1357, 0.9642, 0.2468);

        double nearDiff = Math.Abs(baseValue - nearValue);
        double farDiff = Math.Abs(baseValue - farValue);

        Assert.True(nearDiff < farDiff, "A közeli pontnak sokkal kisebb eltérést kell adnia, mint a távolinak (térbeli koherencia)");
        Assert.True(nearDiff < 0.01, $"Közeli pontok között túl nagy az eltérés: {nearDiff}");
    }

    [Fact]
    public void GradientNoiseSingleOctaveIsPure()
    {
        double a = FractalNoise.GradientNoise3D(5, 2.3, 1.1, 0.4, octave: 2);
        double b = FractalNoise.GradientNoise3D(5, 2.3, 1.1, 0.4, octave: 2);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentOctavesGiveDifferentNoise()
    {
        double a = FractalNoise.GradientNoise3D(5, 2.3, 1.1, 0.4, octave: 0);
        double b = FractalNoise.GradientNoise3D(5, 2.3, 1.1, 0.4, octave: 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void MoreOctavesAddMoreDetailWithoutChangingLowFrequencyTrend()
    {
        // Ket kulonbozo oktavszammal szamolt fBm-nek nem szabad nagysagrendekkel
        // elternie egymastol (a normalizalas miatt), de nem is azonosnak lennie
        // (tobb oktav tobb reszletet ad).
        double few = FractalNoise.Fbm(9, 0.5123, 0.3456, 0.7891, octaves: 1);
        double many = FractalNoise.Fbm(9, 0.5123, 0.3456, 0.7891, octaves: 6);
        Assert.NotEqual(few, many);
        Assert.True(Math.Abs(few - many) < 1.0);
    }
}
