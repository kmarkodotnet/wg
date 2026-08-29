using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Terrain;

/// <summary>
/// A Python referenciával (tools/reference/domain_warp_ref.py) BITPONTOS
/// egyezés várt - a warp csak a már verifikált FractalNoise.Fbm-et, illetve
/// +,-,*,/ és Math.Sqrt-et használ, nincs tolerancia (ND-36).
/// </summary>
public class DomainWarpVectorFileTests
{
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "domain_warp_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            double x = v.GetProperty("x").GetDouble();
            double y = v.GetProperty("y").GetDouble();
            double z = v.GetProperty("z").GetDouble();
            double expectedX = v.GetProperty("warpedX").GetDouble();
            double expectedY = v.GetProperty("warpedY").GetDouble();
            double expectedZ = v.GetProperty("warpedZ").GetDouble();

            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);

            Assert.Equal(expectedX, wx);
            Assert.Equal(expectedY, wy);
            Assert.Equal(expectedZ, wz);
            checkedCount++;
        }
        Assert.True(checkedCount > 400);
    }
}

public class DomainWarpStructuralTests
{
    [Fact]
    public void IsPure()
    {
        DomainWarp.WarpPosition(1UL, 0.267261, 0.534522, 0.801784, out double ax, out double ay, out double az);
        DomainWarp.WarpPosition(1UL, 0.267261, 0.534522, 0.801784, out double bx, out double by, out double bz);
        Assert.Equal(ax, bx);
        Assert.Equal(ay, by);
        Assert.Equal(az, bz);
    }

    [Fact]
    public void DifferentSeedsGiveDifferentResults()
    {
        DomainWarp.WarpPosition(1UL, 0.267261, 0.534522, 0.801784, out double ax, out double ay, out double az);
        DomainWarp.WarpPosition(2UL, 0.267261, 0.534522, 0.801784, out double bx, out double by, out double bz);
        Assert.False(ax == bx && ay == by && az == bz);
    }

    [Fact]
    public void OutputIsAlwaysUnitLength()
    {
        var rnd = new System.Random(7);
        for (int i = 0; i < 5000; i++)
        {
            double x = rnd.NextDouble() * 2 - 1;
            double y = rnd.NextDouble() * 2 - 1;
            double z = rnd.NextDouble() * 2 - 1;
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-9) continue;
            x /= len; y /= len; z /= len;

            DomainWarp.WarpPosition(0xA7C944210000UL, x, y, z, out double wx, out double wy, out double wz);
            double outLen = Math.Sqrt(wx * wx + wy * wy + wz * wz);
            Assert.True(Math.Abs(outLen - 1.0) < 1e-9, $"A warpolt vektor nem egységvektor: {outLen}");
        }
    }

    [Fact]
    public void ParallelMatchesSequential()
    {
        const ulong worldSeed = 0xA7C944210000UL;
        var rnd = new System.Random(42);
        var points = new (double X, double Y, double Z)[2000];
        for (int i = 0; i < points.Length; i++)
        {
            double x = rnd.NextDouble() * 2 - 1;
            double y = rnd.NextDouble() * 2 - 1;
            double z = rnd.NextDouble() * 2 - 1;
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-9) len = 1.0;
            points[i] = (x / len, y / len, z / len);
        }

        var sequential = new (double X, double Y, double Z)[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            DomainWarp.WarpPosition(worldSeed, points[i].X, points[i].Y, points[i].Z,
                out double wx, out double wy, out double wz);
            sequential[i] = (wx, wy, wz);
        }

        var parallel = new (double X, double Y, double Z)[points.Length];
        Parallel.For(0, points.Length, i =>
        {
            DomainWarp.WarpPosition(worldSeed, points[i].X, points[i].Y, points[i].Z,
                out double wx, out double wy, out double wz);
            parallel[i] = (wx, wy, wz);
        });

        for (int i = 0; i < points.Length; i++)
        {
            Assert.Equal(sequential[i].X, parallel[i].X);
            Assert.Equal(sequential[i].Y, parallel[i].Y);
            Assert.Equal(sequential[i].Z, parallel[i].Z);
        }
    }

    [Fact]
    public void StrengthParameterHasEffect()
    {
        DomainWarp.WarpPosition(0xA7C944210000UL, 0.267261, 0.534522, 0.801784,
            out double x0, out double y0, out double z0, strength: 0.0);
        DomainWarp.WarpPosition(0xA7C944210000UL, 0.267261, 0.534522, 0.801784,
            out double x1, out double y1, out double z1, strength: 1.0);

        // strength=0 eseten a torzitas nulla, tehat a nyers (normalizalt)
        // pozíciót kell visszaadnia - ez igazolja, hogy a strength parameter
        // ERDEMBEN hat a kimenetre.
        Assert.False(x0 == x1 && y0 == y1 && z0 == z1);
    }

    [Fact]
    public void FrequencyParameterHasEffect()
    {
        DomainWarp.WarpPosition(0xA7C944210000UL, 0.267261, 0.534522, 0.801784,
            out double x0, out double y0, out double z0, frequency: 2.0);
        DomainWarp.WarpPosition(0xA7C944210000UL, 0.267261, 0.534522, 0.801784,
            out double x1, out double y1, out double z1, frequency: 6.0);

        Assert.False(x0 == x1 && y0 == y1 && z0 == z1);
    }

    [Fact]
    public void OctavesParameterHasEffect()
    {
        DomainWarp.WarpPosition(0xA7C944210000UL, 0.267261, 0.534522, 0.801784,
            out double x0, out double y0, out double z0, octaves: 1);
        DomainWarp.WarpPosition(0xA7C944210000UL, 0.267261, 0.534522, 0.801784,
            out double x1, out double y1, out double z1, octaves: 5);

        Assert.False(x0 == x1 && y0 == y1 && z0 == z1);
    }

    [Fact]
    public void AverageAngularDisplacementIsInPlausibleRange()
    {
        // A Python referencia (domain_warp_ref.py) 8000 mintan ~9.0 fokot
        // mert a vegleges (Strength=1.0, Frequency=2.0, Octaves=3)
        // parameterekkel, celzott nagysagrend 5-15 fok - ld.
        // docs/04-decisions.md ND-36.
        var rnd = new System.Random(2024);
        double sumDeg = 0.0;
        int n = 4000;
        for (int i = 0; i < n; i++)
        {
            double x = rnd.NextDouble() * 2 - 1;
            double y = rnd.NextDouble() * 2 - 1;
            double z = rnd.NextDouble() * 2 - 1;
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-9) { i--; continue; }
            x /= len; y /= len; z /= len;

            DomainWarp.WarpPosition(0xA7C944210000UL, x, y, z, out double wx, out double wy, out double wz);
            double dot = x * wx + y * wy + z * wz;
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            sumDeg += Math.Acos(dot) * (180.0 / Math.PI);
        }
        double meanDeg = sumDeg / n;
        Assert.True(meanDeg > 5.0 && meanDeg < 15.0,
            $"Az átlagos szögeltolódás nem az 5-15 fokos célzott nagyságrendben van: {meanDeg:F2} fok");
    }
}

public class DomainWarpEdgeCaseTests
{
    [Fact]
    public void AxisAlignedUnitVectorsProduceUnitOutput()
    {
        (double X, double Y, double Z)[] axes =
        {
            (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
        };
        foreach (var (x, y, z) in axes)
        {
            DomainWarp.WarpPosition(0xA7C944210000UL, x, y, z, out double wx, out double wy, out double wz);
            double len = Math.Sqrt(wx * wx + wy * wy + wz * wz);
            Assert.True(Math.Abs(len - 1.0) < 1e-9);
        }
    }

    [Fact]
    public void ZeroWorldSeedProducesFiniteUnitVector()
    {
        DomainWarp.WarpPosition(0UL, 0.5, 0.5, 0.707107, out double wx, out double wy, out double wz);
        Assert.False(double.IsNaN(wx) || double.IsNaN(wy) || double.IsNaN(wz));
        double len = Math.Sqrt(wx * wx + wy * wy + wz * wz);
        Assert.True(Math.Abs(len - 1.0) < 1e-9);
    }

    [Fact]
    public void MaxWorldSeedProducesFiniteUnitVector()
    {
        DomainWarp.WarpPosition(ulong.MaxValue, 0.5, -0.5, 0.707107, out double wx, out double wy, out double wz);
        Assert.False(double.IsNaN(wx) || double.IsNaN(wy) || double.IsNaN(wz));
        double len = Math.Sqrt(wx * wx + wy * wy + wz * wz);
        Assert.True(Math.Abs(len - 1.0) < 1e-9);
    }
}
