using System;
using System.Collections.Generic;
using WorldGen.Core.Events;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Events;

/// <summary>M11: a becsapódások mezőre gyakorolt hatása (ImpactCratering.ApplyToField).</summary>
public class ImpactFieldIntegrationTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;

    [Fact]
    public void AtTimeZeroFieldIsUnchanged()
    {
        TileId tile = TileId.FromFaceLevelUV(0, 3, 2, 2);
        var field = new Dictionary<TileId, double> { [tile] = 123.456 };

        Dictionary<TileId, double> result = ImpactCratering.ApplyToField(field, WorldSeed, 0.0);

        Assert.Equal(123.456, result[tile]);
    }

    [Fact]
    public void IsPure()
    {
        TileId tile = TileId.FromFaceLevelUV(0, 4, 5, 5);
        var field = new Dictionary<TileId, double> { [tile] = 0.0 };

        Dictionary<TileId, double> r1 = ImpactCratering.ApplyToField(field, WorldSeed, 300.0);
        Dictionary<TileId, double> r2 = ImpactCratering.ApplyToField(field, WorldSeed, 300.0);

        Assert.Equal(r1[tile], r2[tile]);
    }

    [Fact]
    public void CraterAtTileCenterRemovesFullDepth()
    {
        var craters = new List<ImpactCratering.CraterRecord>
        {
            new ImpactCratering.CraterRecord(1.0, 0.0, 0.0, cosAngularRadius: 0.9, depthMeters: 500.0),
        };

        double delta = ImpactCratering.ElevationDelta(1.0, 0.0, 0.0, craters);

        Assert.Equal(-500.0, delta, 9);
    }

    [Fact]
    public void PointOutsideCraterRadiusIsUnaffected()
    {
        var craters = new List<ImpactCratering.CraterRecord>
        {
            new ImpactCratering.CraterRecord(1.0, 0.0, 0.0, cosAngularRadius: 0.9999, depthMeters: 500.0),
        };

        // Merőleges pont - dot=0, jóval a kráteren kívül.
        double delta = ImpactCratering.ElevationDelta(0.0, 1.0, 0.0, craters);

        Assert.Equal(0.0, delta, 9);
    }

    [Fact]
    public void ElevationDeltaTapersFromCenterToRim()
    {
        var craters = new List<ImpactCratering.CraterRecord>
        {
            new ImpactCratering.CraterRecord(1.0, 0.0, 0.0, cosAngularRadius: 0.5, depthMeters: 1000.0),
        };

        double atCenter = ImpactCratering.ElevationDelta(1.0, 0.0, 0.0, craters);
        // dot = cos(60 fok) = 0.5 -> pontosan a peremen (t=0)
        double atRim = ImpactCratering.ElevationDelta(0.5, Math.Sqrt(3.0) / 2.0, 0.0, craters);
        // felúton a közép és a perem közt (dot kb. 0.75)
        double midpoint = ImpactCratering.ElevationDelta(
            Math.Cos(Math.PI / 6.0), Math.Sin(Math.PI / 6.0), 0.0, craters);

        Assert.Equal(-1000.0, atCenter, 9);
        Assert.Equal(0.0, atRim, 6);
        Assert.True(midpoint < 0.0 && midpoint > -1000.0, "A mélységnek a közép és a perem közt kell lennie");
    }

    [Fact]
    public void MoreElapsedTimeNeverProducesFewerCraters()
    {
        int c1 = ImpactCratering.GenerateCratersUpToTime(WorldSeed, 100.0).Count;
        int c2 = ImpactCratering.GenerateCratersUpToTime(WorldSeed, 400.0).Count;

        Assert.True(c2 >= c1, "Hosszabb idő alatt legalább annyi kráternek kell lennie, mint rövidebb alatt");
    }

    [Fact]
    public void EpochCountForTimeMatchesEpochYears()
    {
        Assert.Equal(0, ImpactCratering.EpochCountForTime(0.0));
        Assert.Equal(100, ImpactCratering.EpochCountForTime(1.0)); // 1 Myr / 10_000 ev = 100 epoch
    }

    [Fact]
    public void CraterDepthAlwaysNonNegativeAfterApplication()
    {
        // Ismert nagy kráter sűrűségű idő-ablak - biztosítjuk, hogy a
        // "tál"-modell ne adjon pozitív (kiemelkedő) eltolást sehol.
        TileId tile = TileId.FromFaceLevelUV(0, 5, 10, 10);
        TileGeometry.ToPosition(tile, out double x, out double y, out double z);
        List<ImpactCratering.CraterRecord> craters = ImpactCratering.GenerateCratersUpToTime(WorldSeed, 500.0);

        double delta = ImpactCratering.ElevationDelta(x, y, z, craters);

        Assert.True(delta <= 0.0, "A becsapódás-modell (rimHeight nélkül) sosem emelhet, csak mélyíthet");
    }
}
