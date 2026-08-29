using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Events;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Events;

/// <summary>
/// A Python referencia (tools/reference/volcanism_ref.py) által generált
/// 20 000 epoch-os "történelem" végigjátszása. BITPONTOS egyezés várt
/// (ND-27/ND-29 - mindkét oldal ugyanazt a saját DeterministicMath
/// algoritmust futtatja, nem a rendszer Math-ját).
/// </summary>
public class VolcanicEruptionVectorFileTests
{
    [Fact]
    public void MatchesPythonReferenceHistoryExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "volcanism_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();

        var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        int checkedCount = 0, occurredCount = 0;
        foreach (JsonElement rec in root.GetProperty("records").EnumerateArray())
        {
            long epochIndex = rec.GetProperty("epochIndex").GetInt64();
            bool expectedOccurred = rec.GetProperty("occurred").GetBoolean();

            bool occurred = VolcanicEruption.TryGenerateEruption(
                worldSeed, epochIndex, seeds,
                out double x, out double y, out double z,
                out double volume, out double height, out double radius);

            Assert.True(occurred == expectedOccurred, $"epoch {epochIndex}: occurred eltér");

            if (expectedOccurred)
            {
                occurredCount++;
                Assert.Equal(rec.GetProperty("x").GetDouble(), x);
                Assert.Equal(rec.GetProperty("y").GetDouble(), y);
                Assert.Equal(rec.GetProperty("z").GetDouble(), z);
                Assert.Equal(rec.GetProperty("volumeCubicMeters").GetDouble(), volume);
                Assert.Equal(rec.GetProperty("edificeHeightMeters").GetDouble(), height);
                Assert.Equal(rec.GetProperty("edificeRadiusMeters").GetDouble(), radius);
            }
            checkedCount++;
        }

        Assert.Equal(20_000, checkedCount);
        Assert.True(occurredCount > 0, "A 20 000 epoch alatt legalább egy eseménynek történnie kellett");
    }
}

public class VolcanicEruptionStructuralTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;

    private static (double X, double Y, double Z)[] Seeds() =>
        PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);

    [Fact]
    public void IsPure()
    {
        var seeds = Seeds();
        long epoch = FindFiringEpoch(seeds, 0, 20_000);

        bool o1 = VolcanicEruption.TryGenerateEruption(WorldSeed, epoch, seeds,
            out double x1, out double y1, out double z1, out double v1, out double h1, out double r1);
        bool o2 = VolcanicEruption.TryGenerateEruption(WorldSeed, epoch, seeds,
            out double x2, out double y2, out double z2, out double v2, out double h2, out double r2);

        Assert.True(o1);
        Assert.Equal(o1, o2);
        Assert.Equal(x1, x2); Assert.Equal(y1, y2); Assert.Equal(z1, z2);
        Assert.Equal(v1, v2); Assert.Equal(h1, h2); Assert.Equal(r1, r2);
    }

    [Fact]
    public void EdificeGrowsWithVolume()
    {
        VolcanicEruption.EdificeGeometry(1e12, out double hSmall, out double rSmall);
        VolcanicEruption.EdificeGeometry(5e12, out double hLarge, out double rLarge);

        Assert.True(hLarge > hSmall, "Nagyobb térfogatnak magasabb edifice-t kell adnia");
        Assert.True(rLarge > rSmall, "Nagyobb térfogatnak nagyobb sugarat kell adnia");
    }

    [Fact]
    public void EdificeHeightRadiusRatioMatchesSlopeAngle()
    {
        VolcanicEruption.EdificeGeometry(2e12, out double h, out double r);
        Assert.Equal(VolcanicEruption.TanSlope, h / r, 9);
    }

    [Fact]
    public void AllEventsAreNearPlateBoundaries()
    {
        var seeds = Seeds();
        int checkedEvents = 0;
        for (long epoch = 0; epoch < 20_000; epoch++)
        {
            if (VolcanicEruption.TryGenerateEruption(WorldSeed, epoch, seeds,
                    out double x, out double y, out double z, out _, out _, out _))
            {
                PlateBoundaryEffect.TwoBestDots(x, y, z, seeds, out double best, out double second);
                double gap = best - second;
                Assert.True(gap < VolcanicEruption.GapScale, $"epoch {epoch}: gap={gap} túl nagy");
                checkedEvents++;
            }
        }
        Assert.True(checkedEvents > 0);
    }

    [Fact]
    public void VolumeIsAlwaysWithinVei8Range()
    {
        var seeds = Seeds();
        for (long epoch = 0; epoch < 20_000; epoch++)
        {
            if (VolcanicEruption.TryGenerateEruption(WorldSeed, epoch, seeds,
                    out _, out _, out _, out double volume, out _, out _))
            {
                Assert.True(volume >= VolcanicEruption.Vei8MinVolumeCubicMeters);
                Assert.True(volume <= VolcanicEruption.MaxVolumeCubicMeters);
            }
        }
    }

    [Fact]
    public void DifferentWorldSeedsGiveDifferentHistories()
    {
        var seedsA = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        var seedsB = PlateGeneration.GenerateSeeds(WorldSeed + 1, PlateCount);

        bool anyDifferentEpochFired = false;
        for (long epoch = 0; epoch < 5000; epoch++)
        {
            bool a = VolcanicEruption.TryGenerateEruption(WorldSeed, epoch, seedsA,
                out _, out _, out _, out _, out _, out _);
            bool b = VolcanicEruption.TryGenerateEruption(WorldSeed + 1, epoch, seedsB,
                out _, out _, out _, out _, out _, out _);
            if (a != b) { anyDifferentEpochFired = true; break; }
        }
        Assert.True(anyDifferentEpochFired, "Két különböző világseednek eltérő eseménytörténetet kell adnia");
    }

    [Fact]
    public void PlausibleFrequencyOverLongHistory()
    {
        var seeds = Seeds();
        int occurred = 0;
        for (long epoch = 0; epoch < 20_000; epoch++)
        {
            if (VolcanicEruption.TryGenerateEruption(WorldSeed, epoch, seeds,
                    out _, out _, out _, out _, out _, out _))
                occurred++;
        }
        double expected = VolcanicEruption.EpochProbability() * 20_000;
        double std = Math.Sqrt(20_000 * VolcanicEruption.EpochProbability() * (1 - VolcanicEruption.EpochProbability()));
        Assert.True(Math.Abs(occurred - expected) < 5 * std,
            $"Nem plauzibilis gyakoriság: {occurred} esemény, várt {expected:F1}±{5 * std:F1}");
    }

    private static long FindFiringEpoch((double X, double Y, double Z)[] seeds, long start, long limit)
    {
        for (long epoch = start; epoch < limit; epoch++)
        {
            if (VolcanicEruption.TryGenerateEruption(WorldSeed, epoch, seeds,
                    out _, out _, out _, out _, out _, out _))
                return epoch;
        }
        throw new InvalidOperationException("Nem talalhato tuzelo epoch a megadott tartomanyban");
    }
}
