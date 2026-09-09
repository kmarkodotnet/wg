using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Features;

/// <summary>
/// M8 kiegeszito panel-metrikak (ND: Coastal complexity + Habitability) - tiszta
/// aggregaciok, kozvetlen unit-teszttel (mint a tobbi FeatureMetrics). A Soil
/// fertility BLOKKOLT (nincs talaj-modul), ezert nincs implementalva/tesztelve.
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
}
