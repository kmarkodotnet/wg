using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Features;

/// <summary>
/// M8 morfológiai típusfelismerés (FeatureSegmentation.ClassifyLandform) - tiszta,
/// determinisztikus geometriai/elevation-aggregáció, közvetlen unit-teszttel
/// (mint a többi FeatureMetrics/Segmentation aggregáció, nincs külső referencia).
/// A tengerszint 0, így az elevációk közvetlenül "tengerszint feletti méter".
/// </summary>
public class LandformClassificationTests
{
    private static (List<TileId> tiles, Dictionary<TileId, double> elev) Block(int face, int level, uint u0, uint v0, int w, int h, double elevation)
    {
        var tiles = new List<TileId>();
        var elev = new Dictionary<TileId, double>();
        for (uint du = 0; du < w; du++)
            for (uint dv = 0; dv < h; dv++)
            {
                TileId t = TileId.FromFaceLevelUV(face, level, u0 + du, v0 + dv);
                tiles.Add(t);
                elev[t] = elevation;
            }
        return (tiles, elev);
    }

    [Fact]
    public void FlatLowRegionIsPlain()
    {
        var (tiles, elev) = Block(0, 5, 8, 8, 4, 4, 120.0); // 16 tile, lapos, alacsony
        var isOcean = new Dictionary<TileId, bool>();
        Assert.Equal(FeatureSegmentation.LandformType.Plain,
            FeatureSegmentation.ClassifyLandform(tiles, elev, isOcean, 0.0));
    }

    [Fact]
    public void HighAndFlatIsPlateau()
    {
        var (tiles, elev) = Block(0, 5, 8, 8, 4, 4, 1000.0); // magas + lapos
        var isOcean = new Dictionary<TileId, bool>();
        Assert.Equal(FeatureSegmentation.LandformType.Plateau,
            FeatureSegmentation.ClassifyLandform(tiles, elev, isOcean, 0.0));
    }

    [Fact]
    public void HighAndRuggedIsMountains()
    {
        var (tiles, elev) = Block(0, 5, 8, 8, 4, 4, 1500.0);
        // Nagy relief: egy csúcs 3500-ig, a többi 1500 -> mean magas, relief >= 1500.
        elev[tiles[0]] = 3500.0;
        var isOcean = new Dictionary<TileId, bool>();
        Assert.Equal(FeatureSegmentation.LandformType.Mountains,
            FeatureSegmentation.ClassifyLandform(tiles, elev, isOcean, 0.0));
    }

    [Fact]
    public void LowInteriorWithHighRimIsBasin()
    {
        var (tiles, elev) = Block(0, 5, 8, 8, 3, 3, 50.0); // 9 tile, alacsony belső
        elev[tiles[0]] = 800.0; // perem-kiemelkedés -> relief 750, a mean így is alacsony
        var isOcean = new Dictionary<TileId, bool>();
        Assert.Equal(FeatureSegmentation.LandformType.Basin,
            FeatureSegmentation.ClassifyLandform(tiles, elev, isOcean, 0.0));
    }

    [Fact]
    public void SmallMostlyCoastalRegionIsIsland()
    {
        var (tiles, elev) = Block(1, 5, 10, 10, 2, 2, 100.0); // 4 tile
        // Minden tile-nak legyen óceán-szomszédja (part-arány 1.0).
        var isOcean = new Dictionary<TileId, bool>();
        foreach (TileId t in tiles)
            for (int d = 0; d < 4; d++)
            {
                TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                if (!tiles.Contains(nb))
                    isOcean[nb] = true;
            }
        Assert.Equal(FeatureSegmentation.LandformType.Island,
            FeatureSegmentation.ClassifyLandform(tiles, elev, isOcean, 0.0));
    }

    [Fact]
    public void ModerateReliefInlandIsLowland()
    {
        // relief 500 (a plain-max 400 es a basin-min 600 kozott), mean 300 (nem basin).
        var (tiles, elev) = Block(0, 5, 8, 8, 4, 4, 300.0);
        elev[tiles[0]] = 800.0; // relief = 500
        var isOcean = new Dictionary<TileId, bool>();
        Assert.Equal(FeatureSegmentation.LandformType.Lowland,
            FeatureSegmentation.ClassifyLandform(tiles, elev, isOcean, 0.0));
    }

    [Fact]
    public void EmptyRegionIsLowland()
    {
        Assert.Equal(FeatureSegmentation.LandformType.Lowland,
            FeatureSegmentation.ClassifyLandform(
                new List<TileId>(), new Dictionary<TileId, double>(), new Dictionary<TileId, bool>(), 0.0));
    }

    [Fact]
    public void IsPureAndDeterministic()
    {
        var (tiles, elev) = Block(2, 5, 5, 5, 4, 4, 1000.0);
        var isOcean = new Dictionary<TileId, bool>();
        FeatureSegmentation.LandformType a = FeatureSegmentation.ClassifyLandform(tiles, elev, isOcean, 0.0);
        FeatureSegmentation.LandformType b = FeatureSegmentation.ClassifyLandform(tiles, elev, isOcean, 0.0);
        Assert.Equal(a, b);
        Assert.Equal("plateau", FeatureSegmentation.LandformTypeName(a));
    }
}
