using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class LodCornerResolverTests
{
    [Fact]
    public void CoarseMorphUsesActualTriangleDiagonalNotBilinearSaddle()
    {
        var q = new SurfaceQuad(new(0, 0, 0), new(1, 0, 10), new(1, 1, 0), new(0, 1, 10));
        Assert.Equal(0, q.At(.5, .5).Z);
        Assert.Equal(2.5, q.At(.5, .25).Z);
        Assert.Equal(2.5, q.At(.25, .5).Z);
    }

    private static SurfacePoint RawPoint(int face, int level, uint u, uint v, double shift = 0)
    {
        double n = 1L << level;
        TileGeometry.PositionFromFaceUV(face, u / n * 2 - 1, v / n * 2 - 1, out double x, out double y, out double z);
        double r = 100 + x * y * 3 + y * z * 2 + shift;
        return new(x * r, y * r, z * r);
    }

    private static SurfaceQuad RawQuad(TileId id, int baseLevel)
    {
        id.GetUV(out uint u, out uint v);
        // Eltérő szülő-morph szándékos utánzása; a statikus modell folytonos.
        double shift = id.Level > baseLevel ? (id.Value % 19) * .001 : 0;
        return new(RawPoint(id.Face, id.Level, u, v, shift), RawPoint(id.Face, id.Level, u + 1, v, shift),
            RawPoint(id.Face, id.Level, u + 1, v + 1, shift), RawPoint(id.Face, id.Level, u, v + 1, shift));
    }

    private static void EqualPoint(SurfacePoint a, SurfacePoint b)
    {
        Assert.InRange(Math.Abs(a.X - b.X), 0, 1e-9);
        Assert.InRange(Math.Abs(a.Y - b.Y), 0, 1e-9);
        Assert.InRange(Math.Abs(a.Z - b.Z), 0, 1e-9);
    }

    [Fact]
    public void FineEdgeMatchesCoarseEdgeAndStaticBoundary()
    {
        TileId leaf = TileId.FromFaceLevelUV(0, 4, 5, 4);
        LodCoverage coverage = LodCoverage.Complete(new[] { leaf }, 2);
        var resolver = new LodCornerResolver(2, coverage, t => RawQuad(t, 2));
        EqualPoint(resolver.Corner(0, 4, 6, 5), SurfacePoint.Lerp(
            resolver.Corner(0, 3, 3, 2), resolver.Corner(0, 3, 3, 3), .5));
        EqualPoint(resolver.Corner(0, 4, 4, 5), SurfacePoint.Lerp(
            RawPoint(0, 2, 1, 1), RawPoint(0, 2, 1, 2), .25));
    }

    [Fact]
    public void ChunkReuseMustNoticeNeighborRefinementEvenIfOwnLeavesAreUnchanged()
    {
        TileId unchanged = TileId.FromFaceLevelUV(0,4,5,4);
        TileId neighbor = TileId.FromFaceLevelUV(0,3,3,2);
        var before=LodCoverage.Complete(new[]{unchanged},2);
        var after=LodCoverage.Complete(new[]{unchanged,neighbor.Child(0)},2);
        Assert.Contains(unchanged,after.Leaves);
        var a=new LodCornerResolver(2,before,t=>RawQuad(t,2));
        var b=new LodCornerResolver(2,after,t=>RawQuad(t,2));
        (double,double,double)[] Positions(LodCornerResolver resolver)
        {
            var points=new[]{resolver.Corner(0,4,5,4),resolver.Corner(0,4,6,4),
                resolver.Corner(0,4,6,5),resolver.Corner(0,4,5,5)};
            return points.Select(p=>(p.X,p.Y,p.Z)).ToArray();
        }
        Assert.False(DynamicMeshChunking.SamePositions(Positions(a),Positions(b)));
        var repeated=new LodCornerResolver(2,after,t=>RawQuad(t,2));
        Assert.True(DynamicMeshChunking.SamePositions(Positions(b),Positions(repeated)));
    }

    [Theory]
    [InlineData(4000)]
    [InlineData(25000)]
    public void ProductionCutIncludingFallbacksHasResolvableCorners(int budget)
    {
        LodCoverage coverage = LodCoverage.Complete(AdaptiveQuadTreeBudgetTests.Cut(100.6, budget), 8);
        var resolver = new LodCornerResolver(8, coverage, t => RawQuad(t, 8));
        foreach (TileId leaf in coverage.Leaves)
        {
            leaf.GetUV(out uint u, out uint v);
            for (uint du = 0; du <= 1; du++) for (uint dv = 0; dv <= 1; dv++)
            {
                SurfacePoint p = resolver.Corner(leaf.Face, leaf.Level, u + du, v + dv);
                Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z));
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(18)]
    public void CubeFaceEdgesAndCornersHaveOnePositionIndependentOfTraversalOrder(int baseLevel)
    {
        uint n = 1u << baseLevel;
        var selected = new List<TileId>();
        for (int face = 0; face < 6; face++)
        foreach (uint u in new[] { 0u, n - 1 }) foreach (uint v in new[] { 0u, n - 1 })
            selected.Add(TileId.FromFaceLevelUV(face, baseLevel, u, v).Child(0).Child(3));
        LodCoverage coverage = LodCoverage.Complete(selected, baseLevel);
        var corners = new List<(int Face, int Level, uint U, uint V)>();
        foreach (TileId t in coverage.Leaves)
        {
            t.GetUV(out uint u, out uint v);
            corners.Add((t.Face, t.Level, u, v)); corners.Add((t.Face, t.Level, u + 1, v));
            corners.Add((t.Face, t.Level, u + 1, v + 1)); corners.Add((t.Face, t.Level, u, v + 1));
        }
        var resolver = new LodCornerResolver(baseLevel, coverage, t => RawQuad(t, baseLevel));
        var reverse = new LodCornerResolver(baseLevel, coverage, t => RawQuad(t, baseLevel));
        var expected = new Dictionary<(int, int, uint, uint), SurfacePoint>();
        var positions = new Dictionary<(double, double, double), SurfacePoint>();
        var labels = new Dictionary<(double, double, double), (int Face, int Level, uint U, uint V)>();
        int shared = 0;
        foreach (var c in corners)
        {
            SurfacePoint result = resolver.Corner(c.Face, c.Level, c.U, c.V);
            expected[c] = result;
            double side = 1L << c.Level;
            TileGeometry.PositionFromFaceUV(c.Face, c.U / side * 2 - 1, c.V / side * 2 - 1,
                out double x, out double y, out double z);
            var key = (Math.Round(x, 12), Math.Round(y, 12), Math.Round(z, 12));
            if (positions.TryGetValue(key, out SurfacePoint previous))
            {
                Assert.True(Math.Abs(previous.X - result.X) < 1e-9,
                    $"Közös pont: {labels[key]} / {c}, irány={key}, x={previous.X:R}/{result.X:R}");
                EqualPoint(previous, result); shared++;
            }
            positions[key] = result;
            labels[key] = c;
        }
        Assert.True(shared > 50);
        foreach (var c in corners.AsEnumerable().Reverse())
            EqualPoint(expected[c], reverse.Corner(c.Face, c.Level, c.U, c.V));
    }
}
