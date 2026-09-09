using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class ChunkRootOfTests
{
    [Fact]
    public void WalksUpToTheChunkLevelAncestor()
    {
        TileId leaf = TileId.FromFaceLevelUV(face: 0, level: 10, u: 12, v: 20);
        TileId root = DynamicMeshChunking.ChunkRootOf(leaf, chunkLevel: 8);

        Assert.Equal(8, root.Level);
        // A gyoker tenylegesen a levele ose legyen: ha lefele lepkedunk a
        // levelig ugyanazt a Face/level-et kell adnia (a Child()-lancolas
        // nem szukseges a teszthez - eleg, hogy Parent()-lancolva
        // visszakapjuk).
        TileId walkedUp = leaf;
        while (walkedUp.Level > 8) walkedUp = walkedUp.Parent();
        Assert.Equal(walkedUp.Value, root.Value);
    }

    [Fact]
    public void LeafAtExactlyChunkLevelIsItsOwnRoot()
    {
        TileId leaf = TileId.FromFaceLevelUV(0, 8, 3, 5);
        Assert.Equal(leaf.Value, DynamicMeshChunking.ChunkRootOf(leaf, 8).Value);
    }

    [Fact]
    public void LeafBelowChunkLevelIsItsOwnRoot()
    {
        // Nem vart eset normal hasznalatban, de nem szabad kivetelt dobnia
        // vagy negativ szintre menni - a level marad onmaga.
        TileId leaf = TileId.FromFaceLevelUV(0, 5, 1, 1);
        Assert.Equal(leaf.Value, DynamicMeshChunking.ChunkRootOf(leaf, 8).Value);
    }

    [Fact]
    public void IsPure()
    {
        TileId leaf = TileId.FromFaceLevelUV(2, 11, 100, 200);
        Assert.Equal(
            DynamicMeshChunking.ChunkRootOf(leaf, 8).Value,
            DynamicMeshChunking.ChunkRootOf(leaf, 8).Value);
    }
}

public class GroupByChunkTests
{
    [Fact]
    public void EmptyCutGivesEmptyGrouping()
    {
        Dictionary<TileId, HashSet<TileId>> chunks = DynamicMeshChunking.GroupByChunk(new List<TileId>(), 8);
        Assert.Empty(chunks);
    }

    [Fact]
    public void LeavesUnderTheSameAncestorAreGroupedTogether()
    {
        // Ugyanannak a level-8  osnek 4 leszarmazottja level 9-en (a 4 gyerek).
        TileId parent = TileId.FromFaceLevelUV(0, 8, 4, 4);
        var children = new List<TileId>
        {
            parent.Child(0), parent.Child(1), parent.Child(2), parent.Child(3),
        };

        Dictionary<TileId, HashSet<TileId>> chunks = DynamicMeshChunking.GroupByChunk(children, 8);

        Assert.Single(chunks);
        Assert.True(chunks.ContainsKey(parent));
        Assert.Equal(4, chunks[parent].Count);
        foreach (TileId child in children)
            Assert.Contains(child, chunks[parent]);
    }

    [Fact]
    public void LeavesUnderDifferentAncestorsGetSeparateEntries()
    {
        TileId leafA = TileId.FromFaceLevelUV(0, 9, 0, 0);
        TileId leafB = TileId.FromFaceLevelUV(0, 9, 100, 100); // messze A-tol, mas level-8 os

        Dictionary<TileId, HashSet<TileId>> chunks = DynamicMeshChunking.GroupByChunk(new[] { leafA, leafB }, 8);

        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public void DuplicateLeavesDoNotDuplicateInTheSet()
    {
        TileId leaf = TileId.FromFaceLevelUV(0, 9, 1, 1);
        Dictionary<TileId, HashSet<TileId>> chunks = DynamicMeshChunking.GroupByChunk(new[] { leaf, leaf }, 8);
        Assert.Single(chunks[DynamicMeshChunking.ChunkRootOf(leaf, 8)]);
    }
}

public class DiffChunksTests
{
    private static TileId Root(uint u, uint v) => TileId.FromFaceLevelUV(0, 8, u, v);

    [Fact]
    public void IdenticalLeafSetsAreUnchanged()
    {
        TileId root = Root(1, 1);
        var leaves = new HashSet<TileId> { root.Child(0), root.Child(1) };
        var previous = new Dictionary<TileId, HashSet<TileId>> { [root] = new HashSet<TileId>(leaves) };
        var current = new Dictionary<TileId, HashSet<TileId>> { [root] = new HashSet<TileId>(leaves) };

        DynamicMeshChunking.ChunkDiff diff = DynamicMeshChunking.DiffChunks(previous, current);

        Assert.Empty(diff.ChangedOrNewChunks);
        Assert.Empty(diff.RemovedChunks);
        Assert.Equal(1, diff.UnchangedChunkCount);
    }

    [Fact]
    public void NewChunkIsReportedAsChanged()
    {
        TileId root = Root(2, 2);
        var current = new Dictionary<TileId, HashSet<TileId>> { [root] = new HashSet<TileId> { root.Child(0) } };
        var previous = new Dictionary<TileId, HashSet<TileId>>();

        DynamicMeshChunking.ChunkDiff diff = DynamicMeshChunking.DiffChunks(previous, current);

        Assert.Single(diff.ChangedOrNewChunks);
        Assert.Equal(root, diff.ChangedOrNewChunks[0]);
        Assert.Empty(diff.RemovedChunks);
        Assert.Equal(0, diff.UnchangedChunkCount);
    }

    [Fact]
    public void ChunkWithDifferentLeafSetIsReportedAsChanged()
    {
        TileId root = Root(3, 3);
        var previous = new Dictionary<TileId, HashSet<TileId>> { [root] = new HashSet<TileId> { root.Child(0) } };
        // Ugyanaz a chunk-gyoker, de MASIK levelhalmaz (finomodott/osszevonodott).
        var current = new Dictionary<TileId, HashSet<TileId>> { [root] = new HashSet<TileId> { root.Child(0), root.Child(1) } };

        DynamicMeshChunking.ChunkDiff diff = DynamicMeshChunking.DiffChunks(previous, current);

        Assert.Single(diff.ChangedOrNewChunks);
        Assert.Equal(root, diff.ChangedOrNewChunks[0]);
    }

    [Fact]
    public void ChunkMissingFromCurrentIsReportedAsRemoved()
    {
        TileId root = Root(4, 4);
        var previous = new Dictionary<TileId, HashSet<TileId>> { [root] = new HashSet<TileId> { root.Child(0) } };
        var current = new Dictionary<TileId, HashSet<TileId>>();

        DynamicMeshChunking.ChunkDiff diff = DynamicMeshChunking.DiffChunks(previous, current);

        Assert.Empty(diff.ChangedOrNewChunks);
        Assert.Single(diff.RemovedChunks);
        Assert.Equal(root, diff.RemovedChunks[0]);
    }

    [Fact]
    public void MixedScenarioReportsEachChunkCorrectly()
    {
        TileId unchangedRoot = Root(1, 1);
        TileId changedRoot = Root(2, 2);
        TileId removedRoot = Root(3, 3);
        TileId newRoot = Root(4, 4);

        var previous = new Dictionary<TileId, HashSet<TileId>>
        {
            [unchangedRoot] = new HashSet<TileId> { unchangedRoot.Child(0) },
            [changedRoot] = new HashSet<TileId> { changedRoot.Child(0) },
            [removedRoot] = new HashSet<TileId> { removedRoot.Child(0) },
        };
        var current = new Dictionary<TileId, HashSet<TileId>>
        {
            [unchangedRoot] = new HashSet<TileId> { unchangedRoot.Child(0) },
            [changedRoot] = new HashSet<TileId> { changedRoot.Child(1) }, // masik gyerek -> valtozott
            [newRoot] = new HashSet<TileId> { newRoot.Child(0) },
        };

        DynamicMeshChunking.ChunkDiff diff = DynamicMeshChunking.DiffChunks(previous, current);

        Assert.Equal(2, diff.ChangedOrNewChunks.Count);
        Assert.Contains(changedRoot, diff.ChangedOrNewChunks);
        Assert.Contains(newRoot, diff.ChangedOrNewChunks);
        Assert.Single(diff.RemovedChunks);
        Assert.Equal(removedRoot, diff.RemovedChunks[0]);
        Assert.Equal(1, diff.UnchangedChunkCount);
    }

    [Fact]
    public void IsPure()
    {
        TileId root = Root(5, 5);
        var previous = new Dictionary<TileId, HashSet<TileId>> { [root] = new HashSet<TileId> { root.Child(0) } };
        var current = new Dictionary<TileId, HashSet<TileId>> { [root] = new HashSet<TileId> { root.Child(1) } };

        DynamicMeshChunking.ChunkDiff a = DynamicMeshChunking.DiffChunks(previous, current);
        DynamicMeshChunking.ChunkDiff b = DynamicMeshChunking.DiffChunks(previous, current);

        Assert.Equal(a.ChangedOrNewChunks, b.ChangedOrNewChunks);
        Assert.Equal(a.RemovedChunks, b.RemovedChunks);
        Assert.Equal(a.UnchangedChunkCount, b.UnchangedChunkCount);
    }
}
