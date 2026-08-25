using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Random;
using Xunit;

namespace WorldGen.Core.Tests;

public class ThreefryKatTests
{
    /// <summary>
    /// A hivatalos DEShawResearch/random123 known-answer testek.
    /// Ha ez elbukik, a mag hibás — semmi más teszt nem értelmezhető.
    /// </summary>
    [Theory]
    // rounds, ctr0..3, key0..3, expected0..3
    [InlineData(20,
        0UL, 0UL, 0UL, 0UL,
        0UL, 0UL, 0UL, 0UL,
        0x09218ebde6c85537UL, 0x55941f5266d86105UL,
        0x4bd25e16282434dcUL, 0xee29ec846bd2e40bUL)]
    [InlineData(20,
        ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue,
        ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue,
        0x29c24097942bba1bUL, 0x0371bbfb0f6f4e11UL,
        0x3c231ffa33f83a1cUL, 0xcd29113fde32d168UL)]
    [InlineData(20,
        0x243f6a8885a308d3UL, 0x13198a2e03707344UL,
        0xa4093822299f31d0UL, 0x082efa98ec4e6c89UL,
        0x452821e638d01377UL, 0xbe5466cf34e90c6cUL,
        0xbe5466cf34e90c6cUL, 0xc0ac29b7c97c50ddUL,
        0xa7e8fde591651bd9UL, 0xbaafd0c30138319bUL,
        0x84a5c1a729e685b9UL, 0x901d406ccebc1ba4UL)]
    [InlineData(13,
        0UL, 0UL, 0UL, 0UL,
        0UL, 0UL, 0UL, 0UL,
        0x4071fabee1dc8e05UL, 0x02ed3113695c9c62UL,
        0x397311b5b89f9d49UL, 0xe21292c3258024bcUL)]
    public void MatchesOfficialKnownAnswerTests(
        int rounds,
        ulong c0, ulong c1, ulong c2, ulong c3,
        ulong k0, ulong k1, ulong k2, ulong k3,
        ulong e0, ulong e1, ulong e2, ulong e3)
    {
        Block4 got = Threefry4x64.Compute(c0, c1, c2, c3, k0, k1, k2, k3, rounds);
        Assert.Equal(e0, got.X0);
        Assert.Equal(e1, got.X1);
        Assert.Equal(e2, got.X2);
        Assert.Equal(e3, got.X3);
    }
}

public class TestVectorFileTests
{
    private record Vector(ulong[] In, string[] Block);

    /// <summary>
    /// A projekt saját tesztvektorai — a Sample() leképezést rögzítik, nem csak a magot.
    /// Ennek MINDEN platformon futnia kell (Windows/Linux/macOS, x64/ARM64).
    /// Egyetlen eltérés is azt jelenti, hogy a seedek nem hordozhatók.
    /// </summary>
    [Fact]
    public void AllProjectVectorsMatch()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "testvectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        Assert.Equal(DeterministicRandom.AlgorithmVersion,
                     root.GetProperty("algorithmVersion").GetUInt64());

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            ulong[] input = v.GetProperty("in").EnumerateArray()
                             .Select(e => e.GetUInt64()).ToArray();
            string[] expected = v.GetProperty("block").EnumerateArray()
                                 .Select(e => e.GetString()!).ToArray();

            Block4 got = DeterministicRandom.Block(
                worldSeed:   input[0],
                domainId:    (uint)input[1],
                spatialId:   input[2],
                timeBucket:  input[3],
                propertyId:  (uint)input[4],
                sampleIndex: input[5]);

            Assert.Equal(expected[0], got.X0.ToString("x16"));
            Assert.Equal(expected[1], got.X1.ToString("x16"));
            Assert.Equal(expected[2], got.X2.ToString("x16"));
            Assert.Equal(expected[3], got.X3.ToString("x16"));
            checkedCount++;
        }

        Assert.Equal(512, checkedCount);
    }
}

public class PurityTests
{
    private const ulong Seed = 0xA7C944210000UL;

    /// <summary>Ismételt hívás ugyanazt adja — nincs rejtett állapot.</summary>
    [Fact]
    public void RepeatedCallsAreIdentical()
    {
        for (ulong i = 0; i < 1000; i++)
        {
            double a = DeterministicRandom.Sample(Seed, RandomDomain.Terrain, i, 0);
            double b = DeterministicRandom.Sample(Seed, RandomDomain.Terrain, i, 0);
            Assert.Equal(a, b);
        }
    }

    /// <summary>
    /// A kulcstulajdonság: a párhuzamos, tetszőleges sorrendű kiértékelés
    /// ugyanazt adja, mint a szekvenciális. Ez teszi lehetővé a §2.3-at.
    /// </summary>
    [Fact]
    public void ParallelEvaluationMatchesSequential()
    {
        const int n = 100_000;

        double[] sequential = new double[n];
        for (int i = 0; i < n; i++)
            sequential[i] = DeterministicRandom.Sample(Seed, RandomDomain.Climate, (ulong)i, 42);

        var parallel = new ConcurrentDictionary<int, double>();
        Parallel.For(0, n, i =>
            parallel[i] = DeterministicRandom.Sample(Seed, RandomDomain.Climate, (ulong)i, 42));

        for (int i = 0; i < n; i++)
            Assert.Equal(sequential[i], parallel[i]);
    }

    /// <summary>Fordított sorrendű kiértékelés sem változtat semmin.</summary>
    [Fact]
    public void ReverseOrderEvaluationMatches()
    {
        const int n = 10_000;
        var forward = new double[n];
        var backward = new double[n];

        for (int i = 0; i < n; i++)
            forward[i] = DeterministicRandom.Sample(Seed, RandomDomain.Tectonics, (ulong)i, 7);
        for (int i = n - 1; i >= 0; i--)
            backward[i] = DeterministicRandom.Sample(Seed, RandomDomain.Tectonics, (ulong)i, 7);

        Assert.Equal(forward, backward);
    }

    /// <summary>
    /// Minden paraméter érdemben változtat a kimeneten. Ez fogja meg azt a hibát,
    /// amikor egy paraméter véletlenül kimarad a kulcs/counter leképezésből —
    /// ilyenkor pl. minden időlépés ugyanazt a zajt adná.
    /// </summary>
    [Fact]
    public void EveryParameterAffectsOutput()
    {
        double base_ = DeterministicRandom.Sample(Seed, RandomDomain.Terrain, 100, 200, 300, 400);

        Assert.NotEqual(base_, DeterministicRandom.Sample(Seed + 1, RandomDomain.Terrain, 100, 200, 300, 400));
        Assert.NotEqual(base_, DeterministicRandom.Sample(Seed, RandomDomain.Climate, 100, 200, 300, 400));
        Assert.NotEqual(base_, DeterministicRandom.Sample(Seed, RandomDomain.Terrain, 101, 200, 300, 400));
        Assert.NotEqual(base_, DeterministicRandom.Sample(Seed, RandomDomain.Terrain, 100, 201, 300, 400));
        Assert.NotEqual(base_, DeterministicRandom.Sample(Seed, RandomDomain.Terrain, 100, 200, 301, 400));
        Assert.NotEqual(base_, DeterministicRandom.Sample(Seed, RandomDomain.Terrain, 100, 200, 300, 401));
    }

    /// <summary>
    /// A domain-szeparáció: két domain ugyanazon a ponton független értéket ad.
    /// Enélkül a domborzat és a klíma korrelálna, ami látható műterméket okozna.
    /// </summary>
    [Fact]
    public void DomainsAreIndependent()
    {
        const int n = 20_000;
        double sumProduct = 0, sumA = 0, sumB = 0, sumA2 = 0, sumB2 = 0;

        for (ulong i = 0; i < n; i++)
        {
            double a = DeterministicRandom.Sample(Seed, RandomDomain.Terrain, i, 0);
            double b = DeterministicRandom.Sample(Seed, RandomDomain.Climate, i, 0);
            sumProduct += a * b; sumA += a; sumB += b; sumA2 += a * a; sumB2 += b * b;
        }

        double cov = sumProduct / n - (sumA / n) * (sumB / n);
        double sdA = Math.Sqrt(sumA2 / n - (sumA / n) * (sumA / n));
        double sdB = Math.Sqrt(sumB2 / n - (sumB / n) * (sumB / n));
        double correlation = cov / (sdA * sdB);

        Assert.True(Math.Abs(correlation) < 0.02,
            $"A domainek korrelálnak: r={correlation:F4}");
    }
}

public class DistributionTests
{
    private const ulong Seed = 0xA7C944210000UL;

    [Fact]
    public void SampleIsInUnitInterval()
    {
        for (ulong i = 0; i < 200_000; i++)
        {
            double v = DeterministicRandom.Sample(Seed, RandomDomain.Terrain, i, 0);
            Assert.InRange(v, 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void SampleIsUniform()
    {
        const int n = 200_000, buckets = 10;
        int[] hist = new int[buckets];
        double sum = 0;

        for (ulong i = 0; i < n; i++)
        {
            double v = DeterministicRandom.Sample(Seed, RandomDomain.Terrain, i, 0);
            hist[Math.Min(buckets - 1, (int)(v * buckets))]++;
            sum += v;
        }

        Assert.InRange(sum / n, 0.495, 0.505);

        // Khi-négyzet, 9 szabadságfok, p=0.001 kritikus érték ~27.88
        double expected = (double)n / buckets;
        double chi2 = hist.Sum(o => (o - expected) * (o - expected) / expected);
        Assert.True(chi2 < 27.88, $"Az eloszlás nem egyenletes: khi²={chi2:F2}");
    }

    /// <summary>
    /// A Sample4 négy értéke független egymástól — nem ugyanaz a szám négyszer,
    /// és nem is korrelált.
    /// </summary>
    [Fact]
    public void Sample4ComponentsAreIndependent()
    {
        const int n = 20_000;
        double sumAB = 0, sumA = 0, sumB = 0, sumA2 = 0, sumB2 = 0;

        for (ulong i = 0; i < n; i++)
        {
            DeterministicRandom.Sample4(Seed, RandomDomain.Terrain, i, 0,
                                        out double a, out double b, out double c, out double d);
            Assert.NotEqual(a, b);
            Assert.NotEqual(c, d);
            sumAB += a * b; sumA += a; sumB += b; sumA2 += a * a; sumB2 += b * b;
        }

        double cov = sumAB / n - (sumA / n) * (sumB / n);
        double sd = Math.Sqrt((sumA2 / n - (sumA / n) * (sumA / n)) *
                              (sumB2 / n - (sumB / n) * (sumB / n)));
        Assert.True(Math.Abs(cov / sd) < 0.02);
    }
}

public class SampleIntTests
{
    private const ulong Seed = 0xA7C944210000UL;

    [Fact]
    public void RespectsBounds()
    {
        for (ulong i = 0; i < 50_000; i++)
        {
            long v = DeterministicRandom.SampleInt(Seed, RandomDomain.Events, i, 0, 5, 12);
            Assert.InRange(v, 5, 11);
        }
    }

    [Fact]
    public void HandlesNegativeRange()
    {
        for (ulong i = 0; i < 10_000; i++)
        {
            long v = DeterministicRandom.SampleInt(Seed, RandomDomain.Events, i, 0, -100, -50);
            Assert.InRange(v, -100, -51);
        }
    }

    /// <summary>
    /// Egy 3-as tartomány nem osztja a 2^64-et, tehát a naiv modulo torzítana.
    /// Az elutasításos módszernek egyenletest kell adnia.
    /// </summary>
    [Fact]
    public void IsUnbiasedForNonPowerOfTwoRange()
    {
        const int n = 300_000;
        int[] hist = new int[3];
        for (ulong i = 0; i < n; i++)
            hist[DeterministicRandom.SampleInt(Seed, RandomDomain.Events, i, 0, 0, 3)]++;

        double expected = (double)n / 3;
        double chi2 = hist.Sum(o => (o - expected) * (o - expected) / expected);
        Assert.True(chi2 < 13.82, $"Torzított: khi²={chi2:F2}, hist=[{string.Join(",", hist)}]");
    }

    [Fact]
    public void ThrowsOnEmptyRange()
    {
        Assert.Throws<ArgumentException>(() =>
            DeterministicRandom.SampleInt(Seed, RandomDomain.Events, 0, 0, 10, 10));
    }
}

public class UnitVectorTests
{
    private const ulong Seed = 0xA7C944210000UL;

    /// <summary>Minden kimenet egységhosszú.</summary>
    [Fact]
    public void VectorsAreNormalized()
    {
        for (ulong i = 0; i < 50_000; i++)
        {
            DeterministicRandom.SampleUnitVector3(
                Seed, RandomDomain.Tectonics, i, 0,
                out double x, out double y, out double z,
                RandomProperty.EulerPole);

            double lenSq = x * x + y * y + z * z;
            Assert.True(Math.Abs(lenSq - 1.0) < 1e-12, $"Nem egységhosszú: {lenSq}");
        }
    }

    /// <summary>
    /// Az eloszlásnak egyenletesnek kell lennie a gömbfelületen.
    /// Archimédész tétele szerint a z komponens egyenletes [-1,1]-en —
    /// ez a legérzékenyebb ellenőrzés, mert a hibás módszerek (pl. gömbi
    /// koordinátákból egyenletes szög) a pólusoknál sűrűsödnének.
    /// </summary>
    [Fact]
    public void DistributionIsUniformOnSphere()
    {
        const int n = 30_000, buckets = 10;
        int[] hist = new int[buckets];
        double sx = 0, sy = 0, sz = 0;

        for (ulong i = 0; i < n; i++)
        {
            DeterministicRandom.SampleUnitVector3(
                Seed, RandomDomain.Tectonics, i, 0,
                out double x, out double y, out double z);

            sx += x; sy += y; sz += z;
            hist[Math.Min(buckets - 1, (int)((z + 1.0) / 2.0 * buckets))]++;
        }

        // Az átlagvektornak nullához kell tartania
        Assert.InRange(sx / n, -0.02, 0.02);
        Assert.InRange(sy / n, -0.02, 0.02);
        Assert.InRange(sz / n, -0.02, 0.02);

        // z egyenletes: khi-négyzet, 9 szabadságfok, p=0.001 kritikus ~27.88
        double expected = (double)n / buckets;
        double chi2 = hist.Sum(o => (o - expected) * (o - expected) / expected);
        Assert.True(chi2 < 27.88, $"A z-eloszlás nem egyenletes: khi²={chi2:F2}");
    }

    /// <summary>Tiszta függvény: az elutasításos ciklus sem visz be állapotot.</summary>
    [Fact]
    public void IsDeterministic()
    {
        for (ulong i = 0; i < 1000; i++)
        {
            DeterministicRandom.SampleUnitVector3(Seed, RandomDomain.Tectonics, i, 0,
                out double x1, out double y1, out double z1);
            DeterministicRandom.SampleUnitVector3(Seed, RandomDomain.Tectonics, i, 0,
                out double x2, out double y2, out double z2);

            Assert.Equal(x1, x2);
            Assert.Equal(y1, y2);
            Assert.Equal(z1, z2);
        }
    }
}
