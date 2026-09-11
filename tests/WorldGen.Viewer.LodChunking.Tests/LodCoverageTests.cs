using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class LodCoverageTests
{
    [Fact]
    public void EmptySelectionKeepsEntireStaticBase()
    {
        LodCoverage coverage = LodCoverage.Complete(Array.Empty<TileId>(), 2);
        Assert.Empty(coverage.Roots);
        Assert.Empty(coverage.Leaves);
    }

    [Fact]
    public void SparseSelectionIsCompletedWithoutOverlapsOrExtraRoots()
    {
        var root = TileId.FromFaceLevelUV(0, 2, 1, 1);
        var otherRoot = TileId.FromFaceLevelUV(5, 2, 0, 3);
        var selected = new[] { root.Child(0).Child(3).Child(1), root.Child(2), otherRoot.Child(1) };
        LodCoverage coverage = LodCoverage.Complete(selected, 2);
        Assert.True(coverage.Roots.SetEquals(new[] { root, otherRoot }));
        Assert.All(selected, leaf => Assert.Contains(leaf, coverage.Leaves));
        Assert.True(coverage.FallbackCount > 0);
        foreach (TileId r in coverage.Roots)
        {
            double area = coverage.Leaves.Where(t => DynamicMeshChunking.ChunkRootOf(t, 2).Equals(r))
                .Sum(t => 1.0 / (1L << (2 * (t.Level - 2))));
            Assert.Equal(1.0, area);
            var descendants = new List<TileId> { r };
            for (int level = 2; level < 7; level++)
                descendants = descendants.SelectMany(t => Enumerable.Range(0, 4).Select(t.Child)).ToList();
            foreach (TileId pointCell in descendants)
            {
                int count = 0;
                TileId p = pointCell;
                while (p.Level > 2) { if (coverage.Leaves.Contains(p)) count++; p = p.Parent(); }
                Assert.Equal(1, count);
            }
        }
        Assert.True(coverage.Leaves.SetEquals(LodCoverage.Complete(selected.AsEnumerable().Reverse(), 2).Leaves));
    }

    [Fact]
    public void CompleteSiblingFamilyNeedsNoFallback()
    {
        TileId root = TileId.FromFaceLevelUV(0, 2, 1, 1);
        LodCoverage coverage = LodCoverage.Complete(Enumerable.Range(0, 4).Select(root.Child), 2);
        Assert.Equal(0, coverage.FallbackCount);
        Assert.Equal(4, coverage.Leaves.Count);
    }

    [Fact]
    public void OverlappingInputChoosesAncestorWithoutDuplicateGeometry()
    {
        TileId parent = TileId.FromFaceLevelUV(0, 3, 2, 2);
        LodCoverage coverage = LodCoverage.Complete(new[] { parent, parent.Child(0), parent }, 2);
        Assert.Equal(4, coverage.Leaves.Count);
        Assert.DoesNotContain(parent.Child(0), coverage.Leaves);
    }
}

public class TerrainIndexMaskTests
{
    [Fact]
    public void ChangedRootsPatchOnlyTheirIndicesAndRestoreExactly()
    {
        int[] offsets = { 12, 0, 30, 6, 18, 24 };
        int[] original = Enumerable.Range(1, 36).ToArray();
        var mask = new TerrainIndexMask(0, offsets, original);
        TileId first = TileId.FromFaceLevelUV(0, 0, 0, 0);
        TileId second = TileId.FromFaceLevelUV(2, 0, 0, 0);
        var ranges = mask.SetHidden(new[] { first });
        Assert.Single(ranges);
        Assert.Equal(12, ranges[0].Start);
        Assert.Equal(6, ranges[0].Count);
        for (int i = 0; i < 36; i++) Assert.Equal(i >= 12 && i < 18 ? original[12] : original[i], mask.Indices[i]);
        Assert.Empty(mask.SetHidden(new[] { first }));
        mask.SetHidden(new[] { second });
        Assert.Equal(original.Skip(12).Take(6), mask.Indices.Skip(12).Take(6));
        mask.SetHidden(Array.Empty<TileId>());
        Assert.Equal(original, mask.Indices);
        Assert.Equal(Enumerable.Range(1, 36), original);
        Assert.Equal(0, mask.HiddenCount);
    }

    [Fact]
    public void InvalidRequestDoesNotChangeExistingMask()
    {
        var mask = new TerrainIndexMask(0, Enumerable.Range(0, 6).Select(i => 6 * i).ToArray(), Enumerable.Range(0, 36).ToArray());
        TileId root = TileId.FromFaceLevelUV(0, 0, 0, 0);
        mask.SetHidden(new[] { root });
        int[] before = (int[])mask.Indices.Clone();
        Assert.Throws<ArgumentException>(() => mask.SetHidden(new[] { root.Child(0) }));
        Assert.Equal(before, mask.Indices);
    }

    [Fact]
    public void MorphChangesAreNotHiddenByUnchangedChunkTopology()
    {
        Assert.True(DynamicMeshChunking.SamePositions(new[] { 1f, 2f }, new[] { 1f, 2f }));
        Assert.False(DynamicMeshChunking.SamePositions(new[] { 1f, 2f }, new[] { 1f, 2.00001f }));
        Assert.False(DynamicMeshChunking.SamePositions(new[] { 1f }, new[] { 1f, 2f }));
    }
}
