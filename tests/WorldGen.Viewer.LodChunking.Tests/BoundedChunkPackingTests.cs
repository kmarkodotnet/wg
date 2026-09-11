using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class BoundedChunkPackingTests
{
    private static readonly TileId Root = TileId.FromFaceLevelUV(3, 6, 20, 30);
    private static HashSet<TileId> Descendants(TileId root, int depth)
    {
        var leaves = new HashSet<TileId> { root };
        for (int d = 0; d < depth; d++)
            leaves = leaves.SelectMany(t => Enumerable.Range(0, 4).Select(t.Child)).ToHashSet();
        return leaves;
    }
    private static void AssertPartition(IEnumerable<TileId> cut, Dictionary<TileId, HashSet<TileId>> groups, int limit)
    {
        var expected = cut.ToHashSet();
        var actual = groups.Values.SelectMany(g => g).ToArray();
        Assert.Equal(expected.Count, actual.Length);
        Assert.True(expected.SetEquals(actual));
        foreach (var group in groups)
        {
            Assert.InRange(group.Value.Count, 1, limit);
            foreach (var leaf in group.Value)
                Assert.Equal(group.Key, DynamicMeshChunking.ChunkRootOf(leaf, group.Key.Level));
        }
    }
    private static void AssertSame(Dictionary<TileId, HashSet<TileId>> a, Dictionary<TileId, HashSet<TileId>> b)
    {
        Assert.Equal(a.Keys.OrderBy(t => t.Value), b.Keys.OrderBy(t => t.Value));
        foreach (var group in a) Assert.True(group.Value.SetEquals(b[group.Key]));
    }

    [Fact]
    public void EmptyCutHasNoChunks() => Assert.Empty(DynamicMeshChunking.GroupByLeafBudget(Array.Empty<TileId>(), 6));

    [Fact]
    public void ShallowLeavesPackWithoutChangingTileIdentity()
    {
        var cut = Descendants(Root, 3);
        var old = DynamicMeshChunking.GroupByChunk(cut, 8);
        var packed = DynamicMeshChunking.GroupByLeafBudget(cut, 6);
        Assert.Equal(16, old.Count);
        Assert.Single(packed);
        AssertPartition(cut, packed, 256);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(64)]
    [InlineData(256)]
    public void DeepRegionNeverExceedsLimit(int limit)
    {
        var cut = Descendants(Root, 5);
        var packed = DynamicMeshChunking.GroupByLeafBudget(cut, 6, limit);
        AssertPartition(cut, packed, limit);
        Assert.Equal(cut.Count / limit, packed.Count);
    }

    [Fact]
    public void ThresholdHysteresisPreventsSplitMergeChatter()
    {
        var all = Descendants(Root, 5).OrderBy(t => t.Value).ToArray();
        var a = DynamicMeshChunking.GroupByLeafBudget(all.Take(256), 6);
        Assert.Single(a);
        var b = DynamicMeshChunking.GroupByLeafBudget(all.Take(257), 6, previousChunks: a);
        Assert.True(b.Count > 1);
        var c = DynamicMeshChunking.GroupByLeafBudget(all.Take(256), 6, previousChunks: b);
        Assert.DoesNotContain(Root, c.Keys);
        var d = DynamicMeshChunking.GroupByLeafBudget(all.Take(128), 6, previousChunks: c);
        Assert.Single(d);
        Assert.Contains(Root, d.Keys);
        AssertPartition(all.Take(128), d, 256);
    }

    [Fact]
    public void SplitAndMergeDiffRemoveOldParentsAndChildren()
    {
        var small = Descendants(Root, 3);
        var big = Descendants(Root, 5);
        var before = DynamicMeshChunking.GroupByLeafBudget(small, 6);
        var after = DynamicMeshChunking.GroupByLeafBudget(big, 6, previousChunks: before);
        var split = DynamicMeshChunking.DiffChunks(before, after);
        Assert.Contains(Root, split.RemovedChunks);
        Assert.Equal(after.Count, split.ChangedOrNewChunks.Count);
        var back = DynamicMeshChunking.GroupByLeafBudget(small, 6, previousChunks: after);
        var merge = DynamicMeshChunking.DiffChunks(after, back);
        Assert.Equal(after.Count, merge.RemovedChunks.Count);
        Assert.Equal(new[] { Root }, merge.ChangedOrNewChunks);
        AssertSame(before, back);
    }

    [Fact]
    public void UnrelatedRegionIsReusedAndPreviousStateIsNotMutated()
    {
        var other = TileId.FromFaceLevelUV(1, 6, 10, 12);
        var fixedLeaves = Descendants(other, 2);
        var beforeCut = Descendants(Root, 3).Concat(fixedLeaves).ToHashSet();
        var before = DynamicMeshChunking.GroupByLeafBudget(beforeCut, 6);
        var snapshot = before.ToDictionary(p => p.Key, p => p.Value.ToHashSet());
        var after = DynamicMeshChunking.GroupByLeafBudget(Descendants(Root, 4).Concat(fixedLeaves), 6, previousChunks: before);
        var diff = DynamicMeshChunking.DiffChunks(before, after);
        Assert.Equal(1, diff.UnchangedChunkCount);
        Assert.DoesNotContain(other, diff.ChangedOrNewChunks);
        Assert.DoesNotContain(other, diff.RemovedChunks);
        AssertSame(snapshot, before);
        AssertPartition(beforeCut, before, 256);
    }

    [Fact]
    public async Task InputOrderAndParallelEvaluationDoNotChangePartition()
    {
        var leaves = Descendants(Root, 4);
        var before = DynamicMeshChunking.GroupByLeafBudget(leaves, 6, 64);
        var expected = DynamicMeshChunking.GroupByLeafBudget(leaves, 6, previousChunks: before);
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() =>
            DynamicMeshChunking.GroupByLeafBudget(i % 2 == 0 ? leaves : leaves.Reverse(), 6, previousChunks: before))));
        foreach (var run in runs) AssertSame(expected, run);
    }

    [Fact]
    public void CoarsenedCutLeafOverridesOldDeepPartition()
    {
        var before = DynamicMeshChunking.GroupByLeafBudget(Descendants(Root, 4), 6, 4);
        var after = DynamicMeshChunking.GroupByLeafBudget(new[] { Root }, 6, 4, before);
        Assert.Single(after);
        AssertPartition(new[] { Root }, after, 4);
    }

    [Fact]
    public void CoarseLeavesFacesAndMaximumLevelRemainValid()
    {
        var cut = new[] { TileId.FromFaceLevelUV(0, 0, 0, 0), TileId.FromFaceLevelUV(5, 28, 0, 0),
            TileId.FromFaceLevelUV(5, 28, 1, 0) };
        AssertPartition(cut, DynamicMeshChunking.GroupByLeafBudget(cut, 6, 1), 1);
    }

    [Fact]
    public void DuplicateInputIsNotEmittedTwice()
        => AssertPartition(new[] { Root }, DynamicMeshChunking.GroupByLeafBudget(new[] { Root, Root }, 6), 256);

    [Fact]
    public void CancellationDuringEnumerationDoesNotChangePreviousPartition()
    {
        var leaves = Descendants(Root, 3);
        var before = DynamicMeshChunking.GroupByLeafBudget(leaves, 6);
        using var cancellation = new CancellationTokenSource();
        IEnumerable<TileId> CancelledInput()
        {
            yield return leaves.First();
            cancellation.Cancel();
            yield return leaves.Last();
        }
        Assert.Throws<OperationCanceledException>(() => DynamicMeshChunking.GroupByLeafBudget(
            CancelledInput(), 6, previousChunks: before, cancellation: cancellation.Token));
        AssertPartition(leaves, before, 256);
    }

    [Theory]
    [InlineData(-1, 256)]
    [InlineData(29, 256)]
    [InlineData(6, 0)]
    [InlineData(6, -1)]
    public void InvalidConfigurationIsRejected(int level, int limit)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DynamicMeshChunking.GroupByLeafBudget(new[] { Root }, level, limit));

    [Fact]
    public void MinimumLevelControlsPackingGranularity()
    {
        var cut = Descendants(Root, 3);
        Assert.Single(DynamicMeshChunking.GroupByLeafBudget(cut, 6));
        Assert.Equal(16, DynamicMeshChunking.GroupByLeafBudget(cut, 8).Count);
    }

    [Fact]
    public void RegroupingPreservesCoverageAndResolvedGeometryByTile()
    {
        var coverage = LodCoverage.Complete(new[] { Root.Child(0).Child(1).Child(2).Child(3) }, 6);
        var old = DynamicMeshChunking.GroupByChunk(coverage.Leaves, 8);
        var packed = DynamicMeshChunking.GroupByLeafBudget(coverage.Leaves, 6, 8);
        AssertPartition(coverage.Leaves, packed, 8);
        SurfacePoint Point(TileId tile, uint u, uint v)
        {
            double n = 1L << tile.Level;
            TileGeometry.PositionFromFaceUV(tile.Face, u / n * 2 - 1, v / n * 2 - 1,
                out double x, out double y, out double z);
            double r = 100 + x*y*3 + y*z*2;
            return new SurfacePoint(x*r, y*r, z*r);
        }
        SurfaceQuad Raw(TileId tile)
        {
            tile.GetUV(out uint u, out uint v);
            return new SurfaceQuad(Point(tile,u,v),Point(tile,u+1,v),Point(tile,u+1,v+1),Point(tile,u,v+1));
        }
        Dictionary<TileId,(double,double,double)[]> Resolve(Dictionary<TileId,HashSet<TileId>> groups)
        {
            var resolver = new LodCornerResolver(6, coverage, Raw);
            var result = new Dictionary<TileId,(double,double,double)[]>();
            foreach (var group in groups.Values)
                foreach (var tile in group.OrderBy(t=>t.Value))
                {
                    tile.GetUV(out uint u,out uint v);
                    var points = new[]{resolver.Corner(tile.Face,tile.Level,u,v),resolver.Corner(tile.Face,tile.Level,u+1,v),
                        resolver.Corner(tile.Face,tile.Level,u+1,v+1),resolver.Corner(tile.Face,tile.Level,u,v+1)};
                    result.Add(tile,points.Select(p=>(p.X,p.Y,p.Z)).ToArray());
                }
            return result;
        }
        var a=Resolve(old); var b=Resolve(packed);
        foreach(var tile in coverage.Leaves) Assert.Equal(a[tile],b[tile]);
    }
}
