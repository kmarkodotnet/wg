using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class PreparedTerrainMaskTests
{
    private static TileId Root(int face) => TileId.FromFaceLevelUV(face, 0, 0, 0);
    private static int[] Original(int count) => Enumerable.Range(0, count)
        .SelectMany(i => new[] { i * 4, i * 4 + 1, i * 4 + 2, i * 4, i * 4 + 2, i * 4 + 3 }).ToArray();
    private static TerrainIndexMask Create() => new(0, Enumerable.Range(0, 6).Select(i => i * 6).ToArray(), Original(6));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedPartialUploadCanRestoreEveryGpuIndex(int successfulRanges)
    {
        const int level = 2, side = 4, count = 96;
        TileId Tile(int i) => TileId.FromFaceLevelUV(i / 16, level, (uint)(i / side % side), (uint)(i % side));
        int[] original = Original(count);
        var mask = new TerrainIndexMask(level, Enumerable.Range(0, count).Select(i => i * 6).ToArray(), original);
        mask.SetHidden(new[] { Tile(0), Tile(80) });
        int[] gpu = (int[])mask.Indices.Clone();
        var ranges = mask.ApplyPrepared(mask.PrepareHidden(new[] { Tile(40) }));
        Assert.Equal(3, ranges.Count);
        // A CPU már új állapotú, a szimulált natív hiba a tartományok között állít meg.
        foreach (var range in ranges.Take(successfulRanges))
            Array.Copy(mask.Indices, range.Start, gpu, range.Start, range.Count);
        var recovery = mask.RestoreAll();
        Array.Copy(mask.Indices, recovery.Start, gpu, recovery.Start, recovery.Count);
        Assert.Equal(original, mask.Indices);
        Assert.Equal(original, gpu);
        Assert.Empty(mask.CopyHiddenQuadIndices());
    }

    [Fact]
    public void FullRecoveryInvalidatesPlansAndStillUploadsWhenCpuAlreadyVisible()
    {
        var mask = Create();
        var plan = mask.PrepareHidden(new[] { Root(0) });
        mask.ApplyPrepared(plan);
        var pending = mask.PrepareHidden(new[] { Root(1) });
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var range = mask.RestoreAll();
            Assert.Equal(0, range.Start);
            Assert.Equal(36, range.Count);
            Assert.Equal(Original(6), mask.Indices);
            Assert.Empty(mask.CopyHiddenQuadIndices());
            Assert.Throws<InvalidOperationException>(() => mask.ApplyPrepared(pending));
        }
        Assert.Equal(new[] { 0 }, plan.CopyHiddenQuadIndices());
        mask.ApplyPrepared(mask.PrepareHidden(new[] { Root(2) }));
        Assert.Equal(new[] { 2 }, mask.CopyHiddenQuadIndices());
    }

    [Fact]
    public void FullRecoveryAllowsAnEmptySparseSurface()
    {
        var mask = new TerrainIndexMask(0, Enumerable.Repeat(-1, 6).ToArray(), Array.Empty<int>(), true);
        var pending = mask.PrepareHidden(Array.Empty<TileId>());
        var range = mask.RestoreAll();
        Assert.Equal(0, range.Start);
        Assert.Equal(0, range.Count);
        Assert.Empty(mask.Indices);
        Assert.Throws<InvalidOperationException>(() => mask.ApplyPrepared(pending));
    }

    [Fact]
    public void PreparationLeavesPublishedStateUntouchedAndApplyRestoresOldRoots()
    {
        var mask = Create();
        mask.SetHidden(new[] { Root(0) });
        int[] before = (int[])mask.Indices.Clone();
        var plan = mask.PrepareHidden(new[] { Root(3) });
        Assert.Equal(before, mask.Indices);
        Assert.Equal(new[] { 0 }, mask.CopyHiddenQuadIndices());
        Assert.Equal(new[] { 3 }, plan.CopyHiddenQuadIndices());
        mask.ApplyPrepared(plan);
        var expected = Original(6);
        Array.Fill(expected, 12, 18, 6);
        Assert.Equal(expected, mask.Indices);
        Assert.Equal(new[] { 3 }, mask.CopyHiddenQuadIndices());
        mask.SetHidden(Array.Empty<TileId>());
        Assert.Equal(Original(6), mask.Indices);
    }

    [Fact]
    public void ForeignStaleAndRepeatedPlansFailBeforeChangingIndices()
    {
        var mask = Create();
        var other = Create();
        var plan = mask.PrepareHidden(new[] { Root(0) });
        Assert.Throws<InvalidOperationException>(() => other.ApplyPrepared(plan));
        Assert.Equal(Original(6), other.Indices);
        var competing = mask.PrepareHidden(new[] { Root(1) });
        mask.ApplyPrepared(plan);
        int[] after = (int[])mask.Indices.Clone();
        Assert.Throws<InvalidOperationException>(() => mask.ApplyPrepared(plan));
        Assert.Throws<InvalidOperationException>(() => mask.ApplyPrepared(competing));
        Assert.Equal(after, mask.Indices);
    }

    [Fact]
    public void LegacyUpdateInvalidatesAnEarlierPreparedPlanEvenForSameCoverage()
    {
        var mask = Create();
        var plan = mask.PrepareHidden(new[] { Root(2) });
        mask.SetHidden(Array.Empty<TileId>());
        Assert.Throws<InvalidOperationException>(() => mask.ApplyPrepared(plan));
        Assert.Equal(Original(6), mask.Indices);
    }

    [Fact]
    public void InputAndDiagnosticSnapshotDoNotAliasThePreparedState()
    {
        var mask = Create();
        var input = new List<TileId> { Root(2), Root(2) };
        var plan = mask.PrepareHidden(input);
        input.Clear();
        var snapshot = plan.CopyHiddenQuadIndices();
        snapshot.Clear();
        mask.ApplyPrepared(plan);
        Assert.Equal(1, mask.HiddenCount);
        Assert.Equal(new[] { 2 }, mask.CopyHiddenQuadIndices());
        Assert.Empty(mask.PrepareHidden(new[] { Root(2) }).Ranges);
    }

    [Fact]
    public void InvalidInputOrMissingSurfaceCannotMutateOrInvalidateValidPlan()
    {
        var mask = new TerrainIndexMask(0, new[] { 0, -1, -1, -1, -1, -1 }, Original(1), true);
        var plan = mask.PrepareHidden(new[] { Root(0) });
        Assert.Throws<ArgumentException>(() => mask.PrepareHidden(new[] { Root(1) }));
        Assert.Throws<ArgumentException>(() => mask.PrepareHidden(new[] { TileId.FromFaceLevelUV(0, 1, 0, 0) }));
        Assert.Throws<ArgumentNullException>(() => mask.PrepareHidden(null!));
        Assert.Throws<ArgumentNullException>(() => mask.ApplyPrepared(null!));
        Assert.Equal(Original(1), mask.Indices);
        mask.ApplyPrepared(plan);
        Assert.All(mask.Indices, index => Assert.Equal(0, index));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedRangesCoverEveryChangeAndPreserveEveryUnchangedIndex(bool reverse)
    {
        const int level = 2, side = 4, count = 6 * side * side;
        var roots = Enumerable.Range(0, count).Select(i =>
            TileId.FromFaceLevelUV(i / (side * side), level, (uint)(i / side % side), (uint)(i % side))).ToArray();
        // A dense tile-sorrendtől eltérő material/index sorrend.
        var offsets = Enumerable.Range(0, count).Select(i => (count - 1 - i) * 6).ToArray();
        var original = Original(count);
        var mask = new TerrainIndexMask(level, offsets, original);
        for (int step = 0; step < 20; step++)
        {
            var hidden = roots.Where((_, i) => (i * 7 + step * 3) % 13 < step % 9).ToArray();
            var expected = (int[])original.Clone();
            foreach (var root in hidden)
            {
                int offset = offsets[TerrainIndexMask.DenseIndex(root)];
                Array.Fill(expected, original[offset], offset, 6);
            }
            var before = (int[])mask.Indices.Clone();
            var plan = mask.PrepareHidden(reverse ? Enumerable.Reverse(hidden) : hidden);
            Assert.Equal(before, mask.Indices);
            var touched = new bool[original.Length];
            int previousEnd = 0;
            foreach (var range in plan.Ranges)
            {
                Assert.True(range.Start >= previousEnd);
                Assert.InRange(range.Count, 6, original.Length - range.Start);
                Array.Fill(touched, true, range.Start, range.Count);
                previousEnd = range.Start + range.Count;
            }
            mask.ApplyPrepared(plan);
            Assert.Equal(expected, mask.Indices);
            for (int i = 0; i < expected.Length; i++)
                if (!touched[i]) Assert.Equal(before[i], expected[i]);
        }
    }
}
