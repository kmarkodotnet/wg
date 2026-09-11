using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests;

public class SurfaceLodBoundsTests
{
    private static SurfaceLodBounds Sphere(TileId id, double radius)
    {
        TileGeometry.ToPosition(id, out double x, out double y, out double z);
        TileGeometry.GetContinuousBounds(id, out double u0, out double u1, out double v0, out double v1);
        SurfacePoint Point(double u, double v)
        {
            TileGeometry.PositionFromFaceUV(id.Face, u, v, out double px, out double py, out double pz);
            return new SurfacePoint(px * radius, py * radius, pz * radius);
        }
        return SurfaceLodBounds.FromSamples(new SurfacePoint(x * radius, y * radius, z * radius),
            new SurfaceQuad(Point(u0,v0), Point(u1,v0), Point(u1,v1), Point(u0,v1)));
    }

    private static HashSet<TileId> Cut(double clearance, bool displaced, IReadOnlyCollection<TileId>? previous = null)
        => AdaptiveQuadTree.BuildCut(102 + clearance, 0, 0, 100, previous!,
            5, 20, 0.01, 0.01 / 1.5, -1, 0, 0, 0.7, 4000,
            traversalRootLevel: 3, staticBaseLevel: 5,
            surfaceBounds: displaced ? id => Sphere(id, 102) : null);

    private static int Nadir(HashSet<TileId> cut)
    {
        TileId id = TileGeometry.FromPosition(1, 0, 0, 20);
        while (id.Level > 5) { if (cut.Contains(id)) return id.Level; id = id.Parent(); }
        return 5;
    }

    [Fact]
    public void RaisedSurfaceContinuesRefiningWhereReferenceSpherePlateaus()
    {
        int far = Nadir(Cut(0.5, true));
        int near = Nadir(Cut(0.05, true));
        int closest = Nadir(Cut(0.005, true));
        Assert.True(near >= far + 2, $"far={far}, near={near}");
        Assert.True(closest >= near + 2, $"near={near}, closest={closest}");
        Assert.True(closest >= Nadir(Cut(0.005, false)) + 5);
    }

    [Fact]
    public void DisplacedCutPreservesBudgetAndExplicitHistoryRepeatability()
    {
        var previous = Cut(0.05, true);
        var a = Cut(0.005, true, previous);
        var b = Cut(0.005, true, previous.Reverse().ToArray());
        Assert.True(a.SetEquals(b));
        Assert.InRange(a.Count, 1, 12000); // A külön balance-passz legfeljebb 3× budget.
        foreach (TileId leaf in a)
        {
            TileId parent = leaf;
            while (parent.Level > 5) { parent = parent.Parent(); Assert.DoesNotContain(parent, a); }
        }
    }

    [Theory]
    [InlineData(0.002)]
    [InlineData(0.01)]
    [InlineData(0.04)]
    public void MorphStartsAtTheSameSplitDistanceAsSelection(double threshold)
    {
        var bounds = new SurfaceLodBounds(102, 0, 0, 0.03);
        double splitDistance = bounds.Radius / Math.Tan(threshold);
        Assert.Equal(threshold, bounds.AngularRadius(102 + splitDistance, 0, 0), 12);
        Assert.InRange(bounds.MorphAlpha(102 + splitDistance, 0, 0, threshold, 0.6), 0, 1e-10);
        Assert.Equal(1, bounds.MorphAlpha(102 + splitDistance * 0.3, 0, 0, threshold, 0.6));
    }

    [Fact]
    public void SampleBoundsIncludeRaisedCenterAndAllTerrainCorners()
    {
        var center = new SurfacePoint(104, 0, 0);
        var points = new[] { new SurfacePoint(102, -1, -1), new SurfacePoint(103, 1, -1),
            new SurfacePoint(101, 1, 1), new SurfacePoint(102, -1, 1) };
        var bounds = SurfaceLodBounds.FromSamples(center, new SurfaceQuad(points[0], points[1], points[2], points[3]));
        foreach (var p in points) Assert.True(bounds.DistanceTo(p.X, p.Y, p.Z) <= bounds.Radius);
        Assert.Equal(104, bounds.X);
    }

    [Fact]
    public void CoarseSamplesAreExactSubsetOfStaticBaseGrid()
    {
        for (int face = 0; face < 6; face++)
        for (int level = 3; level <= 8; level++)
        {
            uint n = 1u << level;
            foreach (uint u in new[] { 0u, 1u, n / 2, n - 1, n })
            foreach (uint v in new[] { 0u, 1u, n / 2, n - 1, n })
            {
                TileGeometry.PositionFromFaceUV(face, 2.0 * u / n - 1, 2.0 * v / n - 1,
                    out double x, out double y, out double z);
                TileGeometry.PositionFromFaceUV(face, 2.0 * (u << (8 - level)) / 256 - 1,
                    2.0 * (v << (8 - level)) / 256 - 1, out double bx, out double by, out double bz);
                Assert.Equal(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(bx));
                Assert.Equal(BitConverter.DoubleToInt64Bits(y), BitConverter.DoubleToInt64Bits(by));
                Assert.Equal(BitConverter.DoubleToInt64Bits(z), BitConverter.DoubleToInt64Bits(bz));
            }
        }
    }

    [Fact]
    public void CameraInsideBoundsCannotCullPartlyVisiblePatch()
        => Assert.True(new SurfaceLodBounds(102, 0, 0, 2).IntersectsViewCone(101, 0, 0, -1, 0, 0, 0.4));

    [Fact]
    public void RadialStepDoesNotBecomeNonConvergingTessellationError()
    {
        SurfaceLodBounds Patch(double width) => SurfaceLodBounds.FromSamples(new SurfacePoint(102, 0, 0),
            new SurfaceQuad(new SurfacePoint(102, -width, -width), new SurfacePoint(105, width, -width),
                new SurfacePoint(105, width, width), new SurfacePoint(102, -width, width)));
        var coarse = Patch(0.1);
        var fine = Patch(0.01);
        Assert.True(fine.Radius >= 3); // A láthatósági bounds továbbra is tartalmazza a falat.
        Assert.Equal(coarse.FootprintRadius / 10, fine.FootprintRadius, 12);
        Assert.True(fine.AngularRadius(102.5, 0, 0) < coarse.AngularRadius(102.5, 0, 0));
    }

    [Fact]
    public void MorphUsesFootprintInsteadOfRadialStepHeight()
    {
        var a = new SurfaceLodBounds(102, 0, 0, 3, 0.01);
        var b = new SurfaceLodBounds(102, 0, 0, 0.01);
        Assert.Equal(a.AngularRadius(102.5, 0, 0), b.AngularRadius(102.5, 0, 0));
        Assert.Equal(a.MorphAlpha(102.5, 0, 0, 0.01, 0.6), b.MorphAlpha(102.5, 0, 0, 0.01, 0.6));
    }

    [Fact]
    public void ViewConeUsesTangentSphereExtent()
    {
        double angle = 0.4 + Math.Asin(0.5) - 0.01;
        var bounds = new SurfaceLodBounds(10 * Math.Cos(angle), 10 * Math.Sin(angle), 0, 5);
        Assert.True(bounds.IntersectsViewCone(0, 0, 0, 1, 0, 0, 0.4));
        Assert.False(new SurfaceLodBounds(-10, 0, 0, 1).IntersectsViewCone(0, 0, 0, 1, 0, 0, 0.4));
    }

    [Fact]
    public void SurfaceMetricIsNotSilentlyIgnoredOnLegacyPath()
        => Assert.Throws<ArgumentException>(() => AdaptiveQuadTree.BuildCut(103, 0, 0, 100, null!,
            surfaceBounds: id => Sphere(id, 102)));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void InvalidBoundsAreRejected(double radius)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceLodBounds(100, 0, 0, radius));
}
