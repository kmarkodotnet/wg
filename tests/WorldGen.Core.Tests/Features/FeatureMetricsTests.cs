using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using Xunit;

namespace WorldGen.Core.Tests.Features;

public class FeatureMetricsStructuralTests
{
    [Fact]
    public void AreaTilesCountsTiles()
    {
        TileId a = TileId.FromFaceLevelUV(0, 3, 1, 1);
        TileId b = TileId.FromFaceLevelUV(0, 3, 2, 2);
        Assert.Equal(2, FeatureMetrics.AreaTiles(new List<TileId> { a, b }));
        Assert.Equal(0, FeatureMetrics.AreaTiles(new List<TileId>()));
    }

    [Fact]
    public void BiomeDiversityCountsDistinctBiomes()
    {
        TileId a = TileId.FromFaceLevelUV(0, 3, 1, 1);
        TileId b = TileId.FromFaceLevelUV(0, 3, 2, 2);
        TileId c = TileId.FromFaceLevelUV(0, 3, 3, 3);
        var biomeOf = new Dictionary<TileId, Biome>
        {
            [a] = Biome.Temperate, [b] = Biome.Temperate, [c] = Biome.Tropical,
        };

        int diversity = FeatureMetrics.BiomeDiversity(new[] { a, b, c }, biomeOf);

        Assert.Equal(2, diversity); // Temperate + Tropical, a/b duplikalt
    }

    [Fact]
    public void RiverMouthCountOnlyCountsRiverTilesFlowingIntoOcean()
    {
        TileId ocean = TileId.FromFaceLevelUV(0, 3, 0, 0);
        TileId riverMouth = TileId.FromFaceLevelUV(0, 3, 1, 0);
        TileId riverInland = TileId.FromFaceLevelUV(0, 3, 2, 0);
        TileId nonRiverLand = TileId.FromFaceLevelUV(0, 3, 3, 0);

        var parent = new Dictionary<TileId, TileId?>
        {
            [riverMouth] = ocean,          // kozvetlenul oceanba folyik
            [riverInland] = riverMouth,    // masik folyo-tile-ba folyik, NEM kozvetlenul oceanba
            [nonRiverLand] = ocean,        // oceanba "folyik", de nem folyo-tile
        };
        var isOcean = new Dictionary<TileId, bool>
        {
            [ocean] = true, [riverMouth] = false, [riverInland] = false, [nonRiverLand] = false,
        };
        var riverTiles = new HashSet<TileId> { riverMouth, riverInland };

        int mouths = FeatureMetrics.RiverMouthCount(
            new[] { riverMouth, riverInland, nonRiverLand }, parent, isOcean, riverTiles);

        Assert.Equal(1, mouths); // csak riverMouth szamit
    }

    [Fact]
    public void RiverMouthCountIsZeroForEmptyTileSet()
    {
        int mouths = FeatureMetrics.RiverMouthCount(
            new List<TileId>(),
            new Dictionary<TileId, TileId?>(),
            new Dictionary<TileId, bool>(),
            new HashSet<TileId>());
        Assert.Equal(0, mouths);
    }

    [Fact]
    public void OceanCoverageFractionComputesCorrectRatio()
    {
        var isOcean = new Dictionary<TileId, bool>
        {
            [TileId.FromFaceLevelUV(0, 3, 0, 0)] = true,
            [TileId.FromFaceLevelUV(0, 3, 1, 0)] = true,
            [TileId.FromFaceLevelUV(0, 3, 2, 0)] = false,
            [TileId.FromFaceLevelUV(0, 3, 3, 0)] = false,
        };
        Assert.Equal(0.5, FeatureMetrics.OceanCoverageFraction(isOcean), 9);
    }

    [Fact]
    public void OceanCoverageFractionIsZeroForEmptyField()
    {
        Assert.Equal(0.0, FeatureMetrics.OceanCoverageFraction(new Dictionary<TileId, bool>()));
    }

    [Fact]
    public void RiverBasinCountCountsOnlyIntersectingRegions()
    {
        TileId a = TileId.FromFaceLevelUV(0, 3, 0, 0);
        TileId b = TileId.FromFaceLevelUV(0, 3, 1, 0);
        TileId c = TileId.FromFaceLevelUV(0, 3, 2, 0);
        TileId outsideRegionTile = TileId.FromFaceLevelUV(0, 3, 5, 5);

        var continentTiles = new HashSet<TileId> { a, b };
        var regions = new Dictionary<TileId, List<TileId>>
        {
            [a] = new List<TileId> { a }, // metszi a kontinenst
            [c] = new List<TileId> { b, c }, // metszi (b benne van)
            [outsideRegionTile] = new List<TileId> { outsideRegionTile }, // nem metszi
        };

        Assert.Equal(2, FeatureMetrics.RiverBasinCount(continentTiles, regions));
    }

    [Fact]
    public void RiverBasinCountIsZeroForEmptyRegions()
    {
        var tiles = new HashSet<TileId> { TileId.FromFaceLevelUV(0, 3, 0, 0) };
        Assert.Equal(0, FeatureMetrics.RiverBasinCount(tiles, new Dictionary<TileId, List<TileId>>()));
    }
}

public class SelectRiverTilesTests
{
    private static (Dictionary<TileId, bool> IsOcean, Dictionary<TileId, long> Accumulation) BuildSample()
    {
        var isOcean = new Dictionary<TileId, bool>();
        var accumulation = new Dictionary<TileId, long>();
        for (uint i = 0; i < 10; i++)
        {
            TileId t = TileId.FromFaceLevelUV(0, 4, i, 0);
            isOcean[t] = false;
            accumulation[t] = i + 1; // 1..10
        }
        return (isOcean, accumulation);
    }

    [Fact]
    public void HigherFractionSelectsMoreTiles()
    {
        var (isOcean, accumulation) = BuildSample();

        HashSet<TileId> small = FlowNetwork.SelectRiverTiles(isOcean, accumulation, 0.1);
        HashSet<TileId> large = FlowNetwork.SelectRiverTiles(isOcean, accumulation, 0.5);

        Assert.True(large.Count >= small.Count, "Nagyobb célaránynak legalább annyi folyó-tile-t kell adnia");
    }

    [Fact]
    public void SelectsHighestAccumulationTilesFirst()
    {
        var (isOcean, accumulation) = BuildSample();

        HashSet<TileId> rivers = FlowNetwork.SelectRiverTiles(isOcean, accumulation, 0.2);

        // A legmagasabb accumulation-u tile-nak (i=9, ertek=10) mindig benne kell lennie.
        TileId highest = TileId.FromFaceLevelUV(0, 4, 9, 0);
        Assert.Contains(highest, rivers);
    }

    [Fact]
    public void OceanTilesAreNeverSelected()
    {
        var (isOcean, accumulation) = BuildSample();
        TileId oceanTile = TileId.FromFaceLevelUV(0, 4, 0, 1);
        isOcean[oceanTile] = true;
        accumulation[oceanTile] = 999; // szandekosan magas, hogy a hiba kiderulne

        HashSet<TileId> rivers = FlowNetwork.SelectRiverTiles(isOcean, accumulation, 1.0);

        Assert.DoesNotContain(oceanTile, rivers);
    }

    [Fact]
    public void NoLandTilesReturnsEmptySet()
    {
        var isOcean = new Dictionary<TileId, bool> { [TileId.FromFaceLevelUV(0, 3, 0, 0)] = true };
        var accumulation = new Dictionary<TileId, long> { [TileId.FromFaceLevelUV(0, 3, 0, 0)] = 5 };

        HashSet<TileId> rivers = FlowNetwork.SelectRiverTiles(isOcean, accumulation, 0.5);

        Assert.Empty(rivers);
    }

    [Fact]
    public void IsPure()
    {
        var (isOcean, accumulation) = BuildSample();

        HashSet<TileId> r1 = FlowNetwork.SelectRiverTiles(isOcean, accumulation, 0.3);
        HashSet<TileId> r2 = FlowNetwork.SelectRiverTiles(isOcean, accumulation, 0.3);

        Assert.Equal(r1, r2);
    }
}
