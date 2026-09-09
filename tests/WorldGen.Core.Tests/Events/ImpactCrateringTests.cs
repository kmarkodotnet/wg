using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Events;
using Xunit;

namespace WorldGen.Core.Tests.Events;

/// <summary>
/// A Python referencia (tools/reference/impacts_ref.py) által generált
/// 20 000 epoch-os "történelem" végigjátszása - minden epoch-ra ellenőrzi,
/// hogy a C# ugyanazt az occurred/nem-occurred döntést és (ha történt
/// esemény) ugyanazokat a mezőket számolja-e ki, mint a Python oráklum.
/// BITPONTOS egyezés várt (ND-27 lezárva - mindkét oldal ugyanazt a saját
/// DeterministicMath/deterministic_math_ref algoritmust futtatja, nem a
/// rendszer Math.Sin/Cos/Pow-ját).
/// </summary>
public class ImpactCrateringVectorFileTests
{
    [Fact]
    public void MatchesPythonReferenceHistoryExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "impacts_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();

        int checkedCount = 0, occurredCount = 0;
        foreach (JsonElement rec in root.GetProperty("records").EnumerateArray())
        {
            long epochIndex = rec.GetProperty("epochIndex").GetInt64();
            bool expectedOccurred = rec.GetProperty("occurred").GetBoolean();

            bool occurred = ImpactCratering.TryGenerateImpact(
                worldSeed, epochIndex,
                out double x, out double y, out double z,
                out double impactorDiameter, out double velocity, out double sinAngle,
                out double craterDiameter, out double craterDepth);

            Assert.True(occurred == expectedOccurred, $"epoch {epochIndex}: occurred eltér");

            if (expectedOccurred)
            {
                occurredCount++;
                Assert.Equal(rec.GetProperty("x").GetDouble(), x);
                Assert.Equal(rec.GetProperty("y").GetDouble(), y);
                Assert.Equal(rec.GetProperty("z").GetDouble(), z);
                Assert.Equal(rec.GetProperty("impactorDiameterMeters").GetDouble(), impactorDiameter);
                Assert.Equal(rec.GetProperty("velocityMetersPerSecond").GetDouble(), velocity);
                Assert.Equal(rec.GetProperty("sinAngle").GetDouble(), sinAngle);
                Assert.Equal(rec.GetProperty("craterDiameterMeters").GetDouble(), craterDiameter);
                Assert.Equal(rec.GetProperty("craterDepthMeters").GetDouble(), craterDepth);
            }
            checkedCount++;
        }

        Assert.Equal(20_000, checkedCount);
        Assert.True(occurredCount > 0, "A 20 000 epoch alatt legalább egy eseménynek történnie kellett");
    }
}

public class ImpactCrateringStructuralTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;

    [Fact]
    public void IsPure()
    {
        long epoch = FindFiringEpoch(WorldSeed, 0, 20_000);

        bool o1 = ImpactCratering.TryGenerateImpact(WorldSeed, epoch,
            out double x1, out double y1, out double z1,
            out double d1, out double v1, out double a1, out double cd1, out double cdep1);
        bool o2 = ImpactCratering.TryGenerateImpact(WorldSeed, epoch,
            out double x2, out double y2, out double z2,
            out double d2, out double v2, out double a2, out double cd2, out double cdep2);

        Assert.True(o1);
        Assert.Equal(o1, o2);
        Assert.Equal(x1, x2); Assert.Equal(y1, y2); Assert.Equal(z1, z2);
        Assert.Equal(d1, d2); Assert.Equal(v1, v2); Assert.Equal(a1, a2);
        Assert.Equal(cd1, cd2); Assert.Equal(cdep1, cdep2);
    }

    [Fact]
    public void DifferentEpochsGiveDifferentPositionsWhenBothFire()
    {
        long e1 = FindFiringEpoch(WorldSeed, 0, 20_000);
        long e2 = FindFiringEpoch(WorldSeed, e1 + 1, 20_000);

        ImpactCratering.TryGenerateImpact(WorldSeed, e1,
            out double x1, out double y1, out double z1,
            out _, out _, out _, out _, out _);
        ImpactCratering.TryGenerateImpact(WorldSeed, e2,
            out double x2, out double y2, out double z2,
            out _, out _, out _, out _, out _);

        Assert.True(Math.Abs(x1 - x2) > 1e-9 || Math.Abs(y1 - y2) > 1e-9 || Math.Abs(z1 - z2) > 1e-9);
    }

    [Fact]
    public void DifferentWorldSeedsGiveDifferentHistories()
    {
        bool anyDifferentEpochFired = false;
        for (long epoch = 0; epoch < 2000; epoch++)
        {
            bool a = ImpactCratering.TryGenerateImpact(WorldSeed, epoch, out _, out _, out _, out _, out _, out _, out _, out _);
            bool b = ImpactCratering.TryGenerateImpact(WorldSeed + 1, epoch, out _, out _, out _, out _, out _, out _, out _, out _);
            if (a != b) { anyDifferentEpochFired = true; break; }
        }
        Assert.True(anyDifferentEpochFired, "Két különböző világseednek eltérő eseménytörténetet kell adnia");
    }

    [Fact]
    public void CraterSizeGrowsWithImpactorDiameter()
    {
        double sin45 = Math.Sin(Math.PI / 4.0);
        double dSmall = ImpactCratering.TransientCraterDiameter(500.0, 20000.0, sin45);
        double dLarge = ImpactCratering.TransientCraterDiameter(5000.0, 20000.0, sin45);
        Assert.True(dLarge > dSmall, "Nagyobb becsapódónak nagyobb krátert kell adnia");
    }

    [Fact]
    public void CraterSizeGrowsWithVelocity()
    {
        double sin45 = Math.Sin(Math.PI / 4.0);
        double dSlow = ImpactCratering.TransientCraterDiameter(1000.0, 15000.0, sin45);
        double dFast = ImpactCratering.TransientCraterDiameter(1000.0, 25000.0, sin45);
        Assert.True(dFast > dSlow, "Nagyobb sebességnek nagyobb krátert kell adnia");
    }

    [Fact]
    public void CraterSizeVariesWithAngle()
    {
        double sinSteep = Math.Sin(Math.PI / 2.0); // 90 fok - merőleges becsapódás
        double sinShallow = Math.Sin(Math.PI / 18.0); // 10 fok - súroló becsapódás
        double dSteep = ImpactCratering.TransientCraterDiameter(1000.0, 20000.0, sinSteep);
        double dShallow = ImpactCratering.TransientCraterDiameter(1000.0, 20000.0, sinShallow);
        Assert.True(dSteep > dShallow, "Merőlegesebb becsapódásnak nagyobb krátert kell adnia");
    }

    [Fact]
    public void DepthIsFixedFractionOfDiameter()
    {
        long epoch = FindFiringEpoch(WorldSeed, 0, 20_000);
        ImpactCratering.TryGenerateImpact(WorldSeed, epoch,
            out _, out _, out _, out _, out _, out _,
            out double craterDiameter, out double craterDepth);
        Assert.Equal(craterDiameter * ImpactCratering.DepthToDiameterRatio, craterDepth, 9);
    }

    [Fact]
    public void PlausibleFrequencyOverLongHistory()
    {
        // 200 Myr (20 000 epoch) alatt a varhato esemenyszam kb.
        // epochProbability * n_epoch - plauzibilitasi teszt, nem egzakt.
        int occurred = 0;
        for (long epoch = 0; epoch < 20_000; epoch++)
        {
            if (ImpactCratering.TryGenerateImpact(WorldSeed, epoch,
                    out _, out _, out _, out _, out _, out _, out _, out _))
                occurred++;
        }
        double expected = ImpactCratering.EpochProbability() * 20_000;
        double std = Math.Sqrt(20_000 * ImpactCratering.EpochProbability() * (1 - ImpactCratering.EpochProbability()));
        Assert.True(Math.Abs(occurred - expected) < 5 * std,
            $"Nem plauzibilis gyakoriság: {occurred} esemény, várt {expected:F1}±{5 * std:F1}");
    }

    [Fact]
    public void MostImpactorsAreSmallHeavyTailDistribution()
    {
        var diameters = new List<double>();
        for (long epoch = 0; epoch < 20_000; epoch++)
        {
            if (ImpactCratering.TryGenerateImpact(WorldSeed, epoch,
                    out _, out _, out _, out double d, out _, out _, out _, out _))
                diameters.Add(d);
        }
        int small = diameters.FindAll(d => d < 5000.0).Count;
        int large = diameters.FindAll(d => d >= 20000.0).Count;
        Assert.True(small > large, "Nehéz-farkú eloszlásnak sokkal több kis eseményt kell adnia, mint nagyot");
    }

    [Fact]
    public void SinAngleIsAlwaysInValidRange()
    {
        for (long epoch = 0; epoch < 20_000; epoch++)
        {
            if (ImpactCratering.TryGenerateImpact(WorldSeed, epoch,
                    out _, out _, out _, out _, out _, out double sinAngle, out _, out _))
            {
                Assert.True(sinAngle >= 0.0 && sinAngle <= 1.0, $"sin(angle) tartományon kívül: {sinAngle}");
            }
        }
    }

    private static long FindFiringEpoch(ulong worldSeed, long start, long limit)
    {
        for (long epoch = start; epoch < limit; epoch++)
        {
            if (ImpactCratering.TryGenerateImpact(worldSeed, epoch,
                    out _, out _, out _, out _, out _, out _, out _, out _))
                return epoch;
        }
        throw new InvalidOperationException("Nem talalhato tuzelo epoch a megadott tartomanyban");
    }
}
