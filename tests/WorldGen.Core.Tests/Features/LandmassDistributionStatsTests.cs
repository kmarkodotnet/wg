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

public class LandmassDistributionStatsVectorFileTests
{
    /// <summary>BITPONTOS egyezés várt - csak osztás/összeadás/rendezés, nincs transzcendens függvény.</summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        const ulong worldSeed = 0xA7C944210000UL;
        const int plateCount = 20;
        const int level = 6;
        const double targetWaterFraction = 0.65;

        var field = SeaLevelCalibration.ComputeElevationField(worldSeed, plateCount, level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, targetWaterFraction);
        List<List<TileId>> continents = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 5);

        FeatureSegmentation.LandmassDistributionStats stats = FeatureSegmentation.ComputeLandmassDistributionStats(
            continents.Select(c => (IReadOnlyCollection<TileId>)c).ToList());

        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "features_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement exp = doc.RootElement.GetProperty("landmassDistributionStats");

        Assert.Equal(exp.GetProperty("landmassCount").GetInt32(), stats.LandmassCount);
        Assert.Equal(exp.GetProperty("totalLandTiles").GetInt32(), stats.TotalLandTiles);
        Assert.Equal(exp.GetProperty("largestLandmassShare").GetDouble(), stats.LargestLandmassShare, 9);
        Assert.Equal(exp.GetProperty("top2LandmassShare").GetDouble(), stats.Top2LandmassShare, 9);
        Assert.Equal(exp.GetProperty("medianLandmassSize").GetDouble(), stats.MedianLandmassSize, 9);
        Assert.Equal(exp.GetProperty("p90LandmassSize").GetInt32(), stats.P90LandmassSize);
        Assert.Equal(exp.GetProperty("tinyLandmassCount").GetInt32(), stats.TinyLandmassCount);
        Assert.Equal(exp.GetProperty("giniCoefficient").GetDouble(), stats.GiniCoefficient, 9);
    }
}

/// <summary>
/// 3. probléma (docs/backlog.md "Extrém landmass méreteloszlás"):
/// <see cref="FeatureSegmentation.ComputeLandmassDistributionStats"/>.
/// Szintetikus, kézzel ellenőrzött bemeneteken - a valódi világra vonatkozó
/// bitpontos Python-egyezést a <c>LandmassClassificationVectorFileTests</c>
/// melletti vektor-fájl (`landmassDistributionStats`) fedi le.
/// </summary>
public class LandmassDistributionStatsTests
{
    private static List<TileId> FakeLandmass(int size, int faceOffset)
    {
        var list = new List<TileId>(size);
        for (int i = 0; i < size; i++)
            list.Add(TileId.FromFaceLevelUV(0, 10, (uint)(faceOffset * 1000 + i) % 1024, 0));
        return list;
    }

    [Fact]
    public void EmptyInputGivesAllZeros()
    {
        var stats = FeatureSegmentation.ComputeLandmassDistributionStats(new List<IReadOnlyCollection<TileId>>());
        Assert.Equal(0, stats.LandmassCount);
        Assert.Equal(0, stats.TotalLandTiles);
        Assert.Equal(0.0, stats.LargestLandmassShare);
        Assert.Equal(0.0, stats.GiniCoefficient);
    }

    [Fact]
    public void SingleLandmassIsFullShareAndZeroGini()
    {
        var landmasses = new List<IReadOnlyCollection<TileId>> { FakeLandmass(100, 0) };
        var stats = FeatureSegmentation.ComputeLandmassDistributionStats(landmasses);

        Assert.Equal(1, stats.LandmassCount);
        Assert.Equal(100, stats.TotalLandTiles);
        Assert.Equal(1.0, stats.LargestLandmassShare, 9);
        Assert.Equal(1.0, stats.Top2LandmassShare, 9); // csak 1 van, a "top2" ugyanaz
        Assert.Equal(0.0, stats.GiniCoefficient, 9); // 1 elem - tokeletes "egyenloseg"
    }

    [Fact]
    public void EqualSizedLandmassesGiveZeroGini()
    {
        var landmasses = new List<IReadOnlyCollection<TileId>>
        {
            FakeLandmass(50, 0), FakeLandmass(50, 1), FakeLandmass(50, 2), FakeLandmass(50, 3),
        };
        var stats = FeatureSegmentation.ComputeLandmassDistributionStats(landmasses);

        Assert.Equal(4, stats.LandmassCount);
        Assert.Equal(200, stats.TotalLandTiles);
        Assert.Equal(0.25, stats.LargestLandmassShare, 9);
        Assert.Equal(0.50, stats.Top2LandmassShare, 9);
        Assert.Equal(50.0, stats.MedianLandmassSize, 9);
        Assert.Equal(0.0, stats.GiniCoefficient, 6); // teljesen egyenletes eloszlas
    }

    [Fact]
    public void DominantLandmassGivesHighGiniAndHighShares()
    {
        // Egy 1000 tile-os "szuperkontinens" + 9 db 10 tile-os "sziget" -
        // ez a JELENSEG, amit a felhasznalo jelentett (extrem egyenlotlen).
        var landmasses = new List<IReadOnlyCollection<TileId>> { FakeLandmass(1000, 0) };
        for (int i = 1; i <= 9; i++)
            landmasses.Add(FakeLandmass(10, i));

        var stats = FeatureSegmentation.ComputeLandmassDistributionStats(landmasses);

        Assert.Equal(10, stats.LandmassCount);
        Assert.Equal(1090, stats.TotalLandTiles);
        Assert.True(stats.LargestLandmassShare > 0.9, "A domans landmass aranyanak > 90%-nak kellene lennie.");
        Assert.Equal(9, stats.TinyLandmassCount); // mind a 9 kis sziget < 20 tile
        Assert.True(stats.GiniCoefficient > 0.7, "Extrem egyenlotlen eloszlasnak magas Gini-t kell adnia.");
    }

    [Fact]
    public void MedianHandlesEvenAndOddCounts()
    {
        var odd = new List<IReadOnlyCollection<TileId>> { FakeLandmass(10, 0), FakeLandmass(20, 1), FakeLandmass(30, 2) };
        Assert.Equal(20.0, FeatureSegmentation.ComputeLandmassDistributionStats(odd).MedianLandmassSize, 9);

        var even = new List<IReadOnlyCollection<TileId>>
        {
            FakeLandmass(10, 0), FakeLandmass(20, 1), FakeLandmass(30, 2), FakeLandmass(40, 3),
        };
        Assert.Equal(25.0, FeatureSegmentation.ComputeLandmassDistributionStats(even).MedianLandmassSize, 9);
    }

    [Fact]
    public void IsPureGivenSameInputTwice()
    {
        var landmasses = new List<IReadOnlyCollection<TileId>> { FakeLandmass(37, 0), FakeLandmass(5, 1) };
        var a = FeatureSegmentation.ComputeLandmassDistributionStats(landmasses);
        var b = FeatureSegmentation.ComputeLandmassDistributionStats(landmasses);
        Assert.Equal(a.GiniCoefficient, b.GiniCoefficient);
        Assert.Equal(a.LargestLandmassShare, b.LargestLandmassShare);
    }
}
