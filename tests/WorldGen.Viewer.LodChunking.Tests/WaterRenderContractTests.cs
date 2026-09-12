using System;
using System.Linq;
using System.Threading;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class WaterRenderContractTests
{
    private static TileId Root(int face) => TileId.FromFaceLevelUV(face, 0, 0, 0);
    private static readonly int[] Original = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };

    [Fact]
    public void SparseMaskUsesUploadedOrderNotTileOrderAndRestoresExactly()
    {
        var mask = new TerrainIndexMask(0, new[] { 6, -1, -1, -1, 0, -1 }, Original, true);
        mask.SetHidden(new[] { Root(0) });
        Assert.Equal(Original.Take(6), mask.Indices.Take(6));
        Assert.All(mask.Indices.Skip(6), index => Assert.Equal(4, index));
        Assert.Equal(new[] { 1 }, mask.CopyHiddenQuadIndices());
        mask.SetHidden(new[] { Root(4) });
        Assert.Equal(Original.Skip(6), mask.Indices.Skip(6));
        Assert.All(mask.Indices.Take(6), index => Assert.Equal(0, index));
        mask.SetHidden(Array.Empty<TileId>());
        Assert.Equal(Original, mask.Indices);
        Assert.Empty(mask.CopyHiddenQuadIndices());
    }

    [Fact]
    public void MissingRootIsRejectedBeforeAnyPreviouslyHiddenQuadIsRestored()
    {
        var mask = new TerrainIndexMask(0, new[] { 0, -1, -1, -1, 6, -1 }, Original, true);
        mask.SetHidden(new[] { Root(0) });
        int[] previous = (int[])mask.Indices.Clone();
        Assert.Throws<ArgumentException>(() => mask.SetHidden(new[] { Root(4), Root(1) }));
        Assert.Equal(previous, mask.Indices);
        Assert.Equal(1, mask.HiddenCount);
        Assert.Empty(mask.SetHidden(new[] { Root(0) }));
    }

    [Fact]
    public void EmptyWaterMaskIsValidButDenseTerrainStillRejectsMissingOffsets()
    {
        int[] offsets = Enumerable.Repeat(-1, 6).ToArray();
        var mask = new TerrainIndexMask(0, offsets, Array.Empty<int>(), true);
        Assert.Empty(mask.SetHidden(Array.Empty<TileId>()));
        Assert.Throws<ArgumentException>(() => mask.SetHidden(new[] { Root(0) }));
        Assert.Throws<ArgumentException>(() => new TerrainIndexMask(0, offsets, Array.Empty<int>()));
        offsets[0] = -2;
        Assert.Throws<ArgumentException>(() => new TerrainIndexMask(0, offsets, Array.Empty<int>(), true));
    }

    [Theory]
    [InlineData(4)] [InlineData(17)] [InlineData(256)]
    public void SelectionAndSparseMaskExchangeOnlyCompletelyCoveredRoots(int budget)
    {
        const int level = 3;
        var wet = new[] { TileId.FromFaceLevelUV(0, level, 4, 4), TileId.FromFaceLevelUV(0, level, 4, 5) };
        var offsets = Enumerable.Repeat(-1, 6 * 64).ToArray();
        offsets[TerrainIndexMask.DenseIndex(wet[0])] = 6;
        offsets[TerrainIndexMask.DenseIndex(wet[1])] = 0;
        var mask = new TerrainIndexMask(level, offsets, Original, true);
        var source = new WaterLodSource(level, 100, wet);
        ProjectedLodView View(double distance) => new(new(distance, 0, 0), new(0, 0, 1), new(0, 1, 0),
            new(-1, 0, 0), Math.PI / 3, 1.8, .01, 1000);
        WaterLodSelection? previous = null;
        for (int wave = 0; wave < 5; wave++)
        {
            var next = source.Select(View(110), previous, 7, .04, .025, budget, 5);
            mask.SetHidden(next.ReplacedRoots);
            foreach (TileId root in wet)
            {
                double dynamicArea = next.Leaves.Where(t => DynamicMeshChunking.ChunkRootOf(t, level).Equals(root))
                    .Sum(t => 1.0 / (1L << (2 * (t.Level - level))));
                double staticArea = next.ReplacedRoots.Contains(root) ? 0 : 1;
                Assert.Equal(1, staticArea + dynamicArea);
            }
            Assert.Equal(next.ReplacedRoots.Count, mask.CopyHiddenQuadIndices().Count);
            previous = next;
        }
        int[] applied = (int[])mask.Indices.Clone();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.Select(View(110), previous, 7, .04, .025, budget, 5, cancel.Token));
        Assert.Equal(applied, mask.Indices);
        var far = source.Select(View(900), previous, 7, .04, .025, budget, 5);
        Assert.Empty(far.Leaves);
        mask.SetHidden(far.ReplacedRoots);
        Assert.Equal(Original, mask.Indices);
    }

    [Fact]
    public void ColorResolverUsesCoarseEdgeColorNotNewFineSampleAtBoundary()
    {
        var root = TileId.FromFaceLevelUV(0, 3, 4, 4);
        var source = new WaterLodSource(3, 100, new[] { root });
        var view = new ProjectedLodView(new(120, 0, 0), new(0, 0, 1), new(0, 1, 0), new(-1, 0, 0), Math.PI / 3, 1.8, .01, 1000);
        var selection = source.Select(view, null, 5, .005, .003, 4000, 1000);
        Assert.NotEmpty(selection.Leaves);
        SurfaceQuad Colors(TileId tile)
        {
            tile.GetUV(out uint u, out uint v);
            double side = 1 << tile.Level;
            // A finom új modellminta tudatosan eltér; az illesztésnek mégis
            // a megmaradó durva színélhez kell ragaszkodnia.
            SurfacePoint At(double x, double y) => new(x / side, y / side, tile.Level / 10.0);
            return new(At(u, v), At(u + 1, v), At(u + 1, v + 1), At(u, v + 1));
        }
        var colors = selection.CreateCornerResolver(Colors);
        var geometry = selection.CreateCornerResolver();
        var actual = colors.Corner(0, 5, 16, 18);
        Assert.Equal(.5, actual.X, 10);
        Assert.Equal(4.5 / 8, actual.Y, 10);
        Assert.Equal(.3, actual.Z, 10);
        Assert.Equal(geometry.CornerOwner(0, 5, 16, 18), colors.CornerOwner(0, 5, 16, 18));
        Assert.Throws<ArgumentNullException>(() => selection.CreateCornerResolver(null!));
    }
}
