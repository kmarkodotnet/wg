using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

public class CrustElevationVectorFileTests
{
    /// <summary>
    /// A Python referencia (tools/reference/crust_elevation_ref.py) által
    /// generált vektorok. BITPONTOS egyezés várt (ND-27/ND-31 lezárt
    /// megoldása - DeterministicMath/FractalNoise mindkét oldalon
    /// ugyanaz a saját algoritmus, nem a rendszer könyvtára).
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "crust_elevation_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();

        JsonElement expectedFlags = root.GetProperty("oceanicFlags");
        int plateCount = expectedFlags.GetArrayLength();
        for (int i = 0; i < plateCount; i++)
        {
            bool expected = expectedFlags[i].GetBoolean();
            bool got = CrustElevation.IsOceanic(worldSeed, i);
            Assert.Equal(expected, got);
        }

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            int level = v.GetProperty("level").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            int plateId = v.GetProperty("plateId").GetInt32();
            double expectedElevation = v.GetProperty("elevation").GetDouble();
            bool expectedOceanic = v.GetProperty("isOceanic").GetBoolean();

            TileId id = TileId.FromFaceLevelUV(face, level, u, w);
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double elevation = CrustElevation.BaseElevation(worldSeed, plateId, x, y, z, out bool isOceanic);

            Assert.Equal(expectedOceanic, isOceanic);
            Assert.Equal(expectedElevation, elevation);
            checkedCount++;
        }

        Assert.Equal(400, checkedCount);
    }
}

public class CrustElevationPurityTests
{
    [Fact]
    public void RepeatedCallsAreIdentical()
    {
        double a = CrustElevation.BaseElevation(123UL, 3, 0.6, 0.5, 0.7, out bool oa);
        double b = CrustElevation.BaseElevation(123UL, 3, 0.6, 0.5, 0.7, out bool ob);
        Assert.Equal(a, b);
        Assert.Equal(oa, ob);
    }
}

public class CrustElevationParameterSensitivityTests
{
    [Fact]
    public void DifferentPlateIdCanChangeOceanicFlag()
    {
        // Nem minden plateId ad mas eredmenyt (Bernoulli), de sok mintan
        // biztosan lesz kulonbseg - ha mind ugyanaz, az gyanus (kimaradt parameter).
        bool first = CrustElevation.IsOceanic(1UL, 0);
        bool anyDifferent = false;
        for (int i = 1; i < 50; i++)
        {
            if (CrustElevation.IsOceanic(1UL, i) != first)
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.True(anyDifferent, "50 lemez közül egynek se tér el a kéreg-típusa - gyanús");
    }

    [Fact]
    public void DifferentPositionGivesDifferentNoise()
    {
        CrustElevation.BaseElevation(1UL, 0, 0.6, 0.5, 0.7, out _);
        double a = CrustElevation.BaseElevation(1UL, 0, 0.6, 0.5, 0.7, out _);
        double b = CrustElevation.BaseElevation(1UL, 0, -0.2, 0.9, 0.3, out _);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentWorldSeedChangesElevation()
    {
        double a = CrustElevation.BaseElevation(1UL, 0, 0.6, 0.5, 0.7, out _);
        double b = CrustElevation.BaseElevation(2UL, 0, 0.6, 0.5, 0.7, out _);
        Assert.NotEqual(a, b);
    }
}

public class CrustElevationPlausibilityTests
{
    [Fact]
    public void OceanicAndContinentalElevationsAreClearlySeparatedBands()
    {
        const ulong worldSeed = 42UL;
        double maxOceanic = double.NegativeInfinity;
        double minContinental = double.PositiveInfinity;

        var rnd = new System.Random(7);
        for (int i = 0; i < 2000; i++)
        {
            (double x, double y, double z) = RandomUnitVector(rnd);
            double elev = CrustElevation.BaseElevation(worldSeed, plateId: 0, x, y, z, out bool oceanic);
            if (oceanic) maxOceanic = Math.Max(maxOceanic, elev);
        }
        for (int i = 0; i < 2000; i++)
        {
            (double x, double y, double z) = RandomUnitVector(rnd);
            double elev = CrustElevation.BaseElevation(worldSeed, plateId: 1, x, y, z, out bool oceanic);
            if (!oceanic) minContinental = Math.Min(minContinental, elev);
        }

        // A zaj-amplitudo (+-500m korul, fBm-normalt) nem lophatja at a ket
        // bazis (-4000 vs +800) kozotti szakadekot, meg jelentos tulcsordulassal
        // szamolva sem - ha az egyik plateId oceani a masik kontinentalis
        // (ami Bernoulli-proba miatt tobbnyire igy lesz kulon plateId-knal).
        Assert.True(maxOceanic < minContinental,
            $"Az óceáni és kontinentális sávok átfedik egymást: maxOceanic={maxOceanic}, minContinental={minContinental}");
    }

    private static (double X, double Y, double Z) RandomUnitVector(System.Random rnd)
    {
        while (true)
        {
            double x = rnd.NextDouble() * 2 - 1;
            double y = rnd.NextDouble() * 2 - 1;
            double z = rnd.NextDouble() * 2 - 1;
            double lenSq = x * x + y * y + z * z;
            if (lenSq > 1e-9 && lenSq <= 1.0)
            {
                double inv = 1.0 / Math.Sqrt(lenSq);
                return (x * inv, y * inv, z * inv);
            }
        }
    }
}

public class CrustElevationEdgeCaseTests
{
    [Fact]
    public void ZeroOceanicProbabilityAlwaysGivesContinental()
    {
        for (int i = 0; i < 100; i++)
            Assert.False(CrustElevation.IsOceanic(7UL, i, oceanicProbability: 0.0));
    }

    [Fact]
    public void OneOceanicProbabilityAlwaysGivesOceanic()
    {
        for (int i = 0; i < 100; i++)
            Assert.True(CrustElevation.IsOceanic(7UL, i, oceanicProbability: 1.0));
    }
}
