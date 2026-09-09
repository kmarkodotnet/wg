using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

public class PlateGenerationVectorFileTests
{
    /// <summary>
    /// A Python referencia (tools/reference/plate_ref.py) által generált
    /// vektorok. A magpont-generálás (SampleUnitVector3-ra épül, csak
    /// Math.Sqrt-tel) BITPONTOS egyezést vár - ld. ND-23a.
    /// </summary>
    [Fact]
    public void SeedGenerationMatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "plate_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int checkedCombos = 0;
        foreach (JsonElement combo in root.GetProperty("seedVectors").EnumerateArray())
        {
            ulong worldSeed = combo.GetProperty("worldSeed").GetUInt64();
            int plateCount = combo.GetProperty("plateCount").GetInt32();
            JsonElement expectedSeeds = combo.GetProperty("seeds");

            var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);
            Assert.Equal(plateCount, seeds.Length);

            for (int i = 0; i < plateCount; i++)
            {
                JsonElement exp = expectedSeeds[i];
                Assert.Equal(exp[0].GetDouble(), seeds[i].X);
                Assert.Equal(exp[1].GetDouble(), seeds[i].Y);
                Assert.Equal(exp[2].GetDouble(), seeds[i].Z);
            }
            checkedCombos++;
        }

        Assert.Equal(5, checkedCombos);
    }

    /// <summary>A hozzárendelés (egész plateId) triviálisan egzakt, nem toleranciás.</summary>
    [Fact]
    public void AssignmentMatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "plate_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("assignVectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            int level = v.GetProperty("level").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            int expectedPlateId = v.GetProperty("plateId").GetInt32();

            TileId id = TileId.FromFaceLevelUV(face, level, u, w);
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            int plateId = PlateGeneration.AssignPlate(x, y, z, seeds);

            Assert.Equal(expectedPlateId, plateId);
            checkedCount++;
        }

        Assert.Equal(500, checkedCount);
    }
}

public class PlateGenerationPurityTests
{
    [Fact]
    public void RepeatedCallsAreIdentical()
    {
        var seeds1 = PlateGeneration.GenerateSeeds(0xA7C944210000UL, 12);
        var seeds2 = PlateGeneration.GenerateSeeds(0xA7C944210000UL, 12);
        Assert.Equal(seeds1, seeds2);
    }

    [Fact]
    public void ParallelEvaluationMatchesSequential()
    {
        const ulong worldSeed = 0xA7C944210000UL;
        const int plateCount = 20;
        var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        var positions = new (double X, double Y, double Z)[5000];
        for (int i = 0; i < positions.Length; i++)
        {
            TileId id = TileId.FromFaceLevelUV(i % 6, 6, (uint)(i * 7 % 64), (uint)(i * 13 % 64));
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            positions[i] = (x, y, z);
        }

        var sequential = new int[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            sequential[i] = PlateGeneration.AssignPlate(positions[i].X, positions[i].Y, positions[i].Z, seeds);

        var parallel = new ConcurrentDictionary<int, int>();
        Parallel.For(0, positions.Length, i =>
            parallel[i] = PlateGeneration.AssignPlate(positions[i].X, positions[i].Y, positions[i].Z, seeds));

        for (int i = 0; i < positions.Length; i++)
            Assert.Equal(sequential[i], parallel[i]);
    }
}

public class PlateGenerationParameterSensitivityTests
{
    [Fact]
    public void DifferentWorldSeedGivesDifferentSeeds()
    {
        var a = PlateGeneration.GenerateSeeds(1UL, 10);
        var b = PlateGeneration.GenerateSeeds(2UL, 10);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentPlateCountGivesDifferentSeedArray()
    {
        var a = PlateGeneration.GenerateSeeds(42UL, 10);
        var b = PlateGeneration.GenerateSeeds(42UL, 15);
        Assert.NotEqual(a.Length, b.Length);
        // Az első 10 lemez-mag AZONOS kell legyen - a spatialId (plateId) fixen
        // indexeli a mintavételt, nem a plateCount-tól függ (fontos: ha plateCount
        // nő, a korábbi lemezek nem "csúsznak el" - ez lenne a hibás viselkedés.
        for (int i = 0; i < 10; i++)
            Assert.Equal(a[i], b[i]);
    }
}

public class PlateGenerationPlausibilityTests
{
    /// <summary>Sok tile-mintán egy lemez se legyen üres, és egy se uralja a gömb nagy részét.</summary>
    [Fact]
    public void DistributionIsPlausibleAcrossFullGrid()
    {
        const ulong worldSeed = 0xA7C944210000UL;
        const int plateCount = 12;
        const int level = 6;
        var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        var counts = new int[plateCount];
        uint n = 1u << level;
        int total = 0;
        for (int face = 0; face <= 5; face++)
        {
            for (uint u = 0; u < n; u++)
            {
                for (uint v = 0; v < n; v++)
                {
                    TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                    TileGeometry.ToPosition(id, out double x, out double y, out double z);
                    int plateId = PlateGeneration.AssignPlate(x, y, z, seeds);
                    counts[plateId]++;
                    total++;
                }
            }
        }

        Assert.All(counts, c => Assert.True(c > 0, "Nem lehet üres lemez ekkora mintán"));
        double maxFraction = counts.Max() / (double)total;
        Assert.True(maxFraction < 0.40, $"Egy lemez ne uralja a gömb nagy részét: {maxFraction:P1}");
    }
}

public class PlateGenerationEdgeCaseTests
{
    [Fact]
    public void SingleAssignmentAlwaysReturnsThatPlate()
    {
        var seeds = PlateGeneration.GenerateSeeds(7UL, 1);
        int id = PlateGeneration.AssignPlate(0.5, 0.5, 0.5, seeds);
        Assert.Equal(0, id);
    }

    [Fact]
    public void ExactSeedPositionAssignsToOwnPlate()
    {
        var seeds = PlateGeneration.GenerateSeeds(99UL, 15);
        for (int i = 0; i < seeds.Length; i++)
        {
            int id = PlateGeneration.AssignPlate(seeds[i].X, seeds[i].Y, seeds[i].Z, seeds);
            Assert.Equal(i, id);
        }
    }

    [Fact]
    public void AllGeneratedSeedsAreUnitLength()
    {
        var seeds = PlateGeneration.GenerateSeeds(0xDEADBEEFUL, 30);
        foreach (var (x, y, z) in seeds)
        {
            double lenSq = x * x + y * y + z * z;
            Assert.True(Math.Abs(lenSq - 1.0) < 1e-12, $"Nem egységhosszú: {lenSq}");
        }
    }
}
