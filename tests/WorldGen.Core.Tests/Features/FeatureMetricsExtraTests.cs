using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Features;

/// <summary>
/// M8 kiegeszito panel-metrikak (ND: Coastal complexity + Habitability + Soil
/// fertility) - tiszta aggregaciok, kozvetlen unit-teszttel (mint a tobbi
/// FeatureMetrics). A Soil fertility 2026-09-19 ota NEM blokkolt: a
/// RegolithProfile MVP-je (ND-117) megadja a Depth/WaterRetention bemenetet.
/// </summary>
public class FeatureMetricsExtraTests
{
    [Fact]
    public void CoastTileCountFindsLandBorderingOcean()
    {
        TileId t = TileId.FromFaceLevelUV(0, 4, 5, 5);
        TileId n0 = TileNeighbors.Neighbor(t, (TileDirection)0);
        TileId n1 = TileNeighbors.Neighbor(t, (TileDirection)1);

        // t szarazfold, n0 ocean -> t part-tile. A tobbi szomszed szarazfold.
        var isOcean = new Dictionary<TileId, bool> { [t] = false, [n0] = true, [n1] = false };
        Assert.Equal(1, FeatureMetrics.CoastTileCount(new[] { t }, isOcean));

        // Nincs ocean-szomszed -> 0.
        var noOcean = new Dictionary<TileId, bool> { [t] = false, [n0] = false, [n1] = false };
        Assert.Equal(0, FeatureMetrics.CoastTileCount(new[] { t }, noOcean));

        // Ocean-tile maga sose "part".
        var tOcean = new Dictionary<TileId, bool> { [t] = true, [n0] = true };
        Assert.Equal(0, FeatureMetrics.CoastTileCount(new[] { t }, tOcean));
    }

    [Fact]
    public void CoastalComplexityIsCoastCountOverSqrtLand()
    {
        // 4 szarazfold-tile, ebbol 1 part -> complexity = 1 / sqrt(4) = 0.5.
        TileId t = TileId.FromFaceLevelUV(2, 4, 8, 8);
        TileId ocean = TileNeighbors.Neighbor(t, (TileDirection)0);
        var tiles = new List<TileId> { t,
            TileId.FromFaceLevelUV(2, 4, 8, 9), TileId.FromFaceLevelUV(2, 4, 9, 8), TileId.FromFaceLevelUV(2, 4, 9, 9) };
        var isOcean = new Dictionary<TileId, bool>();
        foreach (TileId x in tiles) isOcean[x] = false;
        isOcean[ocean] = true;

        int coast = FeatureMetrics.CoastTileCount(tiles, isOcean);
        int land = tiles.Count(x => !isOcean[x]);
        double expected = coast / System.Math.Sqrt(land);
        Assert.Equal(expected, FeatureMetrics.CoastalComplexity(tiles, isOcean));
        Assert.Equal(0.0, FeatureMetrics.CoastalComplexity(new List<TileId>(), isOcean)); // ures -> 0
    }

    [Fact]
    public void HabitabilityFractionCountsLandTilesInTemperatureBand()
    {
        var tiles = Enumerable.Range(0, 4).Select(i => TileId.FromFaceLevelUV(1, 4, (uint)i, 0)).ToList();
        var isOcean = new Dictionary<TileId, bool>
        {
            [tiles[0]] = false, [tiles[1]] = false, [tiles[2]] = false, [tiles[3]] = true, // utolso ocean -> nem szamit
        };
        var temp = new Dictionary<TileId, double>
        {
            [tiles[0]] = 290.0,  // lakhato (0..40 C)
            [tiles[1]] = 250.0,  // tul hideg
            [tiles[2]] = 300.0,  // lakhato
            [tiles[3]] = 295.0,  // ocean -> figyelmen kivul
        };
        // 3 szarazfold, ebbol 2 a savban -> 2/3.
        Assert.Equal(2.0 / 3.0, FeatureMetrics.HabitabilityFraction(tiles, temp, isOcean));
        // Egyeni sav.
        Assert.Equal(1.0, FeatureMetrics.HabitabilityFraction(tiles, temp, isOcean, minHabitableK: 240.0, maxHabitableK: 310.0));
    }

    // ---- ND-117: Soil fertility (Depth x WaterRetention, regio-atlag) ----

    private static (TileId Land0, TileId Land1, TileId Ocean) SoilTiles()
        => (TileId.FromFaceLevelUV(1, 4, 3, 3),
            TileId.FromFaceLevelUV(1, 4, 3, 4),
            TileId.FromFaceLevelUV(1, 4, 3, 5));

    [Fact]
    public void SoilFertilityAveragesNormalizedDepthTimesRetentionOverLandOnly()
    {
        (TileId a, TileId b, TileId ocean) = SoilTiles();
        var isOcean = new Dictionary<TileId, bool> { [a] = false, [b] = false, [ocean] = true };
        // referencia 4 m: 2 m -> 0.5, 1 m -> 0.25
        var depth = new Dictionary<TileId, double> { [a] = 2.0, [b] = 1.0, [ocean] = 3.0 };
        var retention = new Dictionary<TileId, double> { [a] = 0.8, [b] = 0.4, [ocean] = 1.0 };

        // (0.5*0.8 + 0.25*0.4) / 2 = (0.4 + 0.1) / 2 = 0.25
        // Az ocean-tile ertekei NEM szamitanak bele sem a szumaba, sem az osztoba.
        Assert.Equal(0.25, FeatureMetrics.SoilFertility(
            new[] { a, b, ocean }, depth, retention, isOcean, depthReferenceMeters: 4.0));
    }

    [Fact]
    public void SoilFertilityIsZeroWithoutLandTiles()
    {
        (TileId a, TileId b, TileId ocean) = SoilTiles();
        var allOcean = new Dictionary<TileId, bool> { [a] = true, [b] = true, [ocean] = true };
        var depth = new Dictionary<TileId, double> { [a] = 2.0, [b] = 2.0, [ocean] = 2.0 };
        var retention = new Dictionary<TileId, double> { [a] = 1.0, [b] = 1.0, [ocean] = 1.0 };

        Assert.Equal(0.0, FeatureMetrics.SoilFertility(new[] { a, b, ocean }, depth, retention, allOcean, 4.0));
        Assert.Equal(0.0, FeatureMetrics.SoilFertility(
            new List<TileId>(), depth, retention, allOcean, 4.0)); // ures -> 0
    }

    /// <summary>A melyseget a referencian felul 1-re vagjuk - a mely regolit
    /// nem "vegtelenul termekenyebb".</summary>
    [Fact]
    public void SoilFertilitySaturatesAboveTheReferenceDepth()
    {
        (TileId a, _, _) = SoilTiles();
        var isOcean = new Dictionary<TileId, bool> { [a] = false };
        var retention = new Dictionary<TileId, double> { [a] = 0.5 };

        double atReference = FeatureMetrics.SoilFertility(
            new[] { a }, new Dictionary<TileId, double> { [a] = 4.0 }, retention, isOcean, 4.0);
        double farAbove = FeatureMetrics.SoilFertility(
            new[] { a }, new Dictionary<TileId, double> { [a] = 1e18 }, retention, isOcean, 4.0);

        Assert.Equal(0.5, atReference);
        Assert.Equal(atReference, farAbove);
    }

    /// <summary>Negativ melyseg (elvben nem fordul elo) sem ad negativ
    /// termekenyseget - a kimenet [0,1]-ben marad.</summary>
    [Fact]
    public void SoilFertilityClampsNegativeDepthToZero()
    {
        (TileId a, _, _) = SoilTiles();
        var isOcean = new Dictionary<TileId, bool> { [a] = false };
        var depth = new Dictionary<TileId, double> { [a] = -5.0 };
        var retention = new Dictionary<TileId, double> { [a] = 1.0 };

        Assert.Equal(0.0, FeatureMetrics.SoilFertility(new[] { a }, depth, retention, isOcean, 4.0));
    }

    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(1.0, 2.0)]
    [InlineData(2.0, 4.0)]
    public void SoilFertilityIsMonotoneInBothFactors(double depthMeters, double deeperMeters)
    {
        (TileId a, _, _) = SoilTiles();
        var isOcean = new Dictionary<TileId, bool> { [a] = false };
        var retention = new Dictionary<TileId, double> { [a] = 0.5 };

        double shallow = FeatureMetrics.SoilFertility(
            new[] { a }, new Dictionary<TileId, double> { [a] = depthMeters }, retention, isOcean, 8.0);
        double deep = FeatureMetrics.SoilFertility(
            new[] { a }, new Dictionary<TileId, double> { [a] = deeperMeters }, retention, isOcean, 8.0);
        Assert.True(deep > shallow, $"melyebb regolit nem adott nagyobb erteket: {deep} <= {shallow}");

        var depthFixed = new Dictionary<TileId, double> { [a] = depthMeters };
        double dry = FeatureMetrics.SoilFertility(
            new[] { a }, depthFixed, new Dictionary<TileId, double> { [a] = 0.2 }, isOcean, 8.0);
        double wet = FeatureMetrics.SoilFertility(
            new[] { a }, depthFixed, new Dictionary<TileId, double> { [a] = 0.9 }, isOcean, 8.0);
        Assert.True(wet > dry, $"nagyobb vizmegtartas nem adott nagyobb erteket: {wet} <= {dry}");
    }

    /// <summary>Tisztasag: ismetelt hivas bitre azonos (nincs rejtett allapot).</summary>
    [Fact]
    public void SoilFertilityIsPure()
    {
        (TileId a, TileId b, TileId ocean) = SoilTiles();
        var isOcean = new Dictionary<TileId, bool> { [a] = false, [b] = false, [ocean] = true };
        var depth = new Dictionary<TileId, double> { [a] = 1.3, [b] = 2.7, [ocean] = 0.0 };
        var retention = new Dictionary<TileId, double> { [a] = 0.31, [b] = 0.77, [ocean] = 0.0 };

        double first = FeatureMetrics.SoilFertility(new[] { a, b, ocean }, depth, retention, isOcean, 5.0);
        double second = FeatureMetrics.SoilFertility(new[] { a, b, ocean }, depth, retention, isOcean, 5.0);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void SoilFertilityRejectsNonPositiveReferenceDepth(double reference)
    {
        (TileId a, _, _) = SoilTiles();
        var isOcean = new Dictionary<TileId, bool> { [a] = false };
        Assert.Throws<System.ArgumentOutOfRangeException>(() => FeatureMetrics.SoilFertility(
            new[] { a }, new Dictionary<TileId, double> { [a] = 1.0 },
            new Dictionary<TileId, double> { [a] = 1.0 }, isOcean, reference));
    }
}
