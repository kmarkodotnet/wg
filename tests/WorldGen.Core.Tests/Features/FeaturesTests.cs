using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGen.Core.Climate;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Features;

public class NameGenerationVectorFileTests
{
    /// <summary>BITPONTOS egyezés várt - csak egész aritmetika, nincs transzcendens függvény.</summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "features_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("nameVectors").EnumerateArray())
        {
            ulong featureId = v.GetProperty("featureId").GetUInt64();
            string biome = v.GetProperty("biome").GetString()!;
            string expected = v.GetProperty("name").GetString()!;

            string got = NameGeneration.GenerateName(worldSeed, featureId, biome);

            Assert.Equal(expected, got);
            checkedCount++;
        }

        Assert.Equal(300, checkedCount);
    }
}

public class NameGenerationUnitTests
{
    [Fact]
    public void IsPure()
    {
        string a = NameGeneration.GenerateName(1UL, 42UL, "Temperate");
        string b = NameGeneration.GenerateName(1UL, 42UL, "Temperate");
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentFeatureIdGivesDifferentName()
    {
        string a = NameGeneration.GenerateName(1UL, 42UL, "Temperate");
        string b = NameGeneration.GenerateName(1UL, 43UL, "Temperate");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void UnknownBiomeFallsBackToDefaultSuffixesWithoutThrowing()
    {
        string name = NameGeneration.GenerateName(1UL, 1UL, "NotARealBiome");
        Assert.False(string.IsNullOrWhiteSpace(name));
    }
}

public class FeatureSegmentationStructuralTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;

    private static int CompareTileByFaceUV(TileId a, TileId b)
    {
        if (a.Face != b.Face) return a.Face.CompareTo(b.Face);
        a.GetUV(out uint au, out uint av);
        b.GetUV(out uint bu, out uint bv);
        if (au != bu) return au.CompareTo(bu);
        return av.CompareTo(bv);
    }

    private static TileId MinTile(IEnumerable<TileId> tiles)
    {
        TileId min = default;
        bool first = true;
        foreach (TileId t in tiles)
        {
            if (first || CompareTileByFaceUV(t, min) < 0) { min = t; first = false; }
        }
        return min;
    }

    /// <summary>
    /// Teljes M8 csővezeték a Python referenciával azonos, EXPLICIT
    /// (nem a nyelv beépített dict/set bejárási sorrendjére támaszkodó)
    /// rendezéssel: méret csökkenő, majd (face,u,v) mint másodlagos kulcs.
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceContinentsAndRegionsExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "features_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);
        Dictionary<TileId, long> accumulation = FlowNetwork.FlowAccumulation(field, flood.Parent, flood.FloodOrder);
        HashSet<TileId> riverTiles = FlowNetwork.SelectRiverTiles(isOcean, accumulation, riverTargetFraction: 0.03);

        var biomeOf = new Dictionary<TileId, Biome>();
        foreach (TileId id in field.Keys)
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double tK = Temperature.TemperatureKelvin(
                x, y, z, 0.0, 365.25, 1.0, 23.44 * Math.PI / 180.0,
                isOcean[id], field[id], seaLevel);
            biomeOf[id] = BiomeClassification.Classify(tK, isOcean[id]);
        }

        Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);
        Dictionary<TileId, List<TileId>> sizedRegions = regions
            .Where(kv => kv.Value.Count >= 5)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        List<List<TileId>> continents = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 5);
        List<List<TileId>> sortedContinents = continents
            .OrderByDescending(c => c.Count)
            .ThenBy(c => MinTile(c), Comparer<TileId>.Create(CompareTileByFaceUV))
            .ToList();

        int checkedContinents = 0;
        JsonElement expectedContinents = root.GetProperty("continents");
        Assert.Equal(expectedContinents.GetArrayLength(), sortedContinents.Count);
        for (int i = 0; i < sortedContinents.Count; i++)
        {
            JsonElement exp = expectedContinents[i];
            List<TileId> comp = sortedContinents[i];
            var biomesPresent = new HashSet<Biome>(comp.Select(t => biomeOf[t]));
            Biome dominant = FeatureSegmentation.DominantBiome(comp, biomeOf);
            string name = NameGeneration.GenerateName(WorldSeed, (ulong)i, dominant.ToString());

            int mouths = FeatureMetrics.RiverMouthCount(comp, flood.Parent, isOcean, riverTiles);
            int basins = FeatureMetrics.RiverBasinCount(new HashSet<TileId>(comp), sizedRegions);

            Assert.Equal(exp.GetProperty("name").GetString(), name);
            Assert.Equal(exp.GetProperty("areaTiles").GetInt32(), comp.Count);
            Assert.Equal(exp.GetProperty("biomeCount").GetInt32(), biomesPresent.Count);
            Assert.Equal(exp.GetProperty("dominantBiome").GetString(), dominant.ToString());
            Assert.Equal(exp.GetProperty("riverMouthCount").GetInt32(), mouths);
            Assert.Equal(exp.GetProperty("riverBasinCount").GetInt32(), basins);
            checkedContinents++;
        }
        Assert.Equal(2, checkedContinents);

        double oceanCoverage = FeatureMetrics.OceanCoverageFraction(isOcean);
        Assert.Equal(root.GetProperty("worldOceanCoverage").GetDouble(), oceanCoverage, 9);

        List<TileId> sizedRootsSorted = regions.Keys
            .Where(root2 => regions[root2].Count >= 5)
            .OrderByDescending(root2 => regions[root2].Count)
            .ThenBy(root2 => root2, Comparer<TileId>.Create(CompareTileByFaceUV))
            .ToList();

        JsonElement expectedRegions = root.GetProperty("regions");
        Assert.Equal(expectedRegions.GetArrayLength(), sizedRootsSorted.Count);
        int checkedRegions = 0;
        for (int i = 0; i < sizedRootsSorted.Count; i++)
        {
            JsonElement exp = expectedRegions[i];
            List<TileId> tiles = regions[sizedRootsSorted[i]];
            Biome dominant = FeatureSegmentation.DominantBiome(tiles, biomeOf);
            string name = NameGeneration.GenerateName(WorldSeed, (ulong)(10000 + i), dominant.ToString());

            int mouths = FeatureMetrics.RiverMouthCount(tiles, flood.Parent, isOcean, riverTiles);

            Assert.Equal(exp.GetProperty("name").GetString(), name);
            Assert.Equal(exp.GetProperty("areaTiles").GetInt32(), tiles.Count);
            Assert.Equal(exp.GetProperty("dominantBiome").GetString(), dominant.ToString());
            Assert.Equal(exp.GetProperty("riverMouthCount").GetInt32(), mouths);
            checkedRegions++;
        }
        Assert.Equal(12, checkedRegions);
    }

    [Fact]
    public void EveryLandTileBelongsToAtMostOneWatershedRegion()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);

        Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);

        var seen = new HashSet<TileId>();
        foreach (var kv in regions)
        {
            foreach (TileId t in kv.Value)
            {
                Assert.True(seen.Add(t), $"A tile {t} egynél több régióhoz tartozik");
            }
        }
    }

    [Fact]
    public void OceanTilesAreNeverPartOfAWatershedRegion()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);

        Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);

        foreach (var kv in regions)
        {
            foreach (TileId t in kv.Value)
                Assert.False(isOcean[t]);
        }
    }
}
