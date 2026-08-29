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
/// TOLERANCIA-alapú (ND-27/ND-28 osztály - Math.Pow/Sin/Cos nem
/// garantáltan bitre azonos platformok között).
/// </summary>
public class ImpactCrateringVectorFileTests
{
    [Fact]
    public void MatchesPythonReferenceHistoryWithinTolerance()
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
                out double impactorDiameter, out double velocity, out double angle,
                out double craterDiameter, out double craterDepth);

            Assert.True(occurred == expectedOccurred, $"epoch {epochIndex}: occurred eltér");

            if (expectedOccurred)
            {
                occurredCount++;
                Assert.True(Math.Abs(x - rec.GetProperty("x").GetDouble()) < 1e-9);
                Assert.True(Math.Abs(y - rec.GetProperty("y").GetDouble()) < 1e-9);
                Assert.True(Math.Abs(z - rec.GetProperty("z").GetDouble()) < 1e-9);
                Assert.True(Math.Abs(impactorDiameter - rec.GetProperty("impactorDiameterMeters").GetDouble()) < 1e-6);
                Assert.True(Math.Abs(velocity - rec.GetProperty("velocityMetersPerSecond").GetDouble()) < 1e-6);
                Assert.True(Math.Abs(angle - rec.GetProperty("angleRadians").GetDouble()) < 1e-9);
                Assert.True(Math.Abs(craterDiameter - rec.GetProperty("craterDiameterMeters").GetDouble()) < 1e-6);
                Assert.True(Math.Abs(craterDepth - rec.GetProperty("craterDepthMeters").GetDouble()) < 1e-6);
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
        int occurredA = 0, occurredB = 0;
        for (long epoch = 0; epoch < 2000; epoch++)
        {
            if (ImpactCratering.TryGenerateImpact(WorldSeed, epoch,
                    out _, out _, out _, out _, out _, out _, out _, out _))
                occurredA++;
            if (ImpactCratering.TryGenerateImpact(WorldSeed + 1, epoch,
                    out _, out _, out _, out _, out _, out _, out _, out _))
                occurredB++;
        }
        // Nem a szamnak kell elterjnie feltetlenul, hanem annak, hogy MASIK
        // seed mas epoch-okban tuzel - kulon ellenorizve lent.
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
        double dSmall = ImpactCratering.TransientCraterDiameter(500.0, 20000.0, Math.PI / 4.0);
        double dLarge = ImpactCratering.TransientCraterDiameter(5000.0, 20000.0, Math.PI / 4.0);
        Assert.True(dLarge > dSmall, "Nagyobb becsapódónak nagyobb krátert kell adnia");
    }

    [Fact]
    public void CraterSizeGrowsWithVelocity()
    {
        double dSlow = ImpactCratering.TransientCraterDiameter(1000.0, 15000.0, Math.PI / 4.0);
        double dFast = ImpactCratering.TransientCraterDiameter(1000.0, 25000.0, Math.PI / 4.0);
        Assert.True(dFast > dSlow, "Nagyobb sebességnek nagyobb krátert kell adnia");
    }

    [Fact]
    public void CraterSizeVariesWithAngle()
    {
        double dSteep = ImpactCratering.TransientCraterDiameter(1000.0, 20000.0, Math.PI / 2.0); // 90 fok
        double dShallow = ImpactCratering.TransientCraterDiameter(1000.0, 20000.0, Math.PI / 18.0); // 10 fok
        Assert.True(dSteep > dShallow, "Merőlegesebb becsapódásnak nagyobb krátert kell adnia");
    }

    [Fact]
    public void DepthIsFixedFractionOfDiameter()
    {
        double diameter = ImpactCratering.TransientCraterDiameter(1000.0, 20000.0, Math.PI / 4.0);
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
