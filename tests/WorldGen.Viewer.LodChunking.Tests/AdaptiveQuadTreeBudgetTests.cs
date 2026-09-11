using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class AdaptiveQuadTreeBudgetTests
{
    private const double Radius = 100;
    private const double Fov = Math.PI / 3;
    private const double Aspect = 16.0 / 9;
    private static readonly V Axis = new V(.017, -.937, .349).Unit;

    internal static HashSet<TileId> Cut(double distance, int budget, int height = 1080,
        IReadOnlyCollection<TileId>? previous = null)
    {
        V c = Axis * distance;
        // A diagnózis eredeti küszöbe: az összehasonlítás kizárólag a
        // frontier/budget javítását méri, nem a külön pixelképlet-változást.
        double split = 6 * Fov / height;
        double half = Math.Atan(Math.Tan(Fov / 2) * Math.Sqrt(1 + Aspect * Aspect)) * 1.3;
        return AdaptiveQuadTree.BuildCut(c.X, c.Y, c.Z, Radius, previous!, 8, 20,
            split, split / 1.5, -Axis.X, -Axis.Y, -Axis.Z, half, budget,
            traversalRootLevel: 3, staticBaseLevel: 8);
    }

    private static int CoverageLevel(V point, HashSet<TileId> cut)
    {
        TileId tile = TileGeometry.FromPosition(point.X, point.Y, point.Z, 20);
        while (tile.Level > 8)
        {
            if (cut.Contains(tile)) return tile.Level;
            tile = tile.Parent();
        }
        return 8;
    }

    [Theory]
    [InlineData(4000)]
    [InlineData(25000)]
    [InlineData(50000)]
    public void BudgetKeepsRefinedCoverageAcrossViewport(int budget)
    {
        const double distance = 100.6;
        HashSet<TileId> cut = Cut(distance, budget);
        Assert.InRange(cut.Count, 1, budget);
        Assert.True(CoverageLevel(Axis, cut) > 8, "A nadir finomítása elveszett.");
        AssertViewportRefined(distance, cut);
        AssertNoOverlaps(cut);
    }

    [Theory]
    [InlineData(103)]
    [InlineData(100.1)]
    public void FourKViewportDoesNotFallBackToBaseWhenBudgetSaturates(double distance)
    {
        HashSet<TileId> cut = Cut(distance, 200000, 2160);
        AssertViewportRefined(distance, cut);
        AssertNoOverlaps(cut);
    }

    [Fact]
    public void SaturatedSelectionIsRepeatableWithExplicitHistory()
    {
        HashSet<TileId> previous = Cut(100.7, 4000);
        HashSet<TileId> a = Cut(100.6, 4000, previous: previous);
        HashSet<TileId> b = Cut(100.6, 4000, previous: previous.Reverse().ToArray());
        Assert.True(a.SetEquals(b));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TooSmallBudgetDoesNotCreatePartialSiblingFamily(int budget)
        => Assert.Empty(Cut(100.6, budget));

    private static void AssertNoOverlaps(HashSet<TileId> cut)
    {
        foreach (TileId leaf in cut)
        {
            TileId parent = leaf;
            while (parent.Level > 8)
            {
                parent = parent.Parent();
                Assert.DoesNotContain(parent, cut);
            }
        }
    }

    private static void AssertViewportRefined(double distance, HashSet<TileId> cut)
    {
        V camera = Axis * distance, forward = Axis * -1;
        V right = V.Cross(forward, new V(0, 0, 1)).Unit;
        V up = V.Cross(right, forward).Unit;
        for (int y = 0; y < 23; y++) for (int x = 0; x < 41; x++)
        {
            V direction = (forward
                + right * ((2 * (x + .5) / 41 - 1) * Aspect * Math.Tan(Fov / 2))
                + up * ((2 * (y + .5) / 23 - 1) * Math.Tan(Fov / 2))).Unit;
            double b = V.Dot(camera, direction);
            double disc = b * b - (distance * distance - Radius * Radius);
            Assert.True(disc >= 0);
            V point = (camera + direction * (-b - Math.Sqrt(disc))).Unit;
            Assert.True(CoverageLevel(point, cut) > 8, $"Base-re esett képernyőminta: ({x},{y}).");
        }
    }

    private readonly struct V
    {
        public readonly double X, Y, Z;
        public V(double x, double y, double z) { X = x; Y = y; Z = z; }
        public V Unit => this * (1 / Math.Sqrt(Dot(this, this)));
        public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V operator *(V a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(V a, V b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static V Cross(V a, V b) => new(a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }
}
