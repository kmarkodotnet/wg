using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class TerrainDecisionDiagnosticsTests
{
    private static TileId Tile(uint u) => TileId.FromFaceLevelUV(0,3,u,2);

    [Fact]
    public void IdentityFollowsMaterialBucketsRatherThanGlobalEmissionOrder()
    {
        var identity=new TerrainQuadIdentity(new[]{2,0,1,1});
        identity.Add(2,Tile(0)); identity.Add(0,Tile(1));
        identity.Add(3,Tile(2)); identity.Add(0,Tile(3));
        Assert.Equal(new[]{Tile(1),Tile(3),Tile(0),Tile(2)},identity.Complete());
    }

    [Fact]
    public void EmptyIdentityIsValid() => Assert.Empty(new TerrainQuadIdentity(Array.Empty<int>()).Complete());

    [Fact]
    public void MissingIdentityCannotSilentlyBecomeFaceZeroRoot()
        => Assert.Throws<InvalidOperationException>(()=>new TerrainQuadIdentity(new[]{1}).Complete());

    [Fact]
    public void ExcessIdentityIsRejected()
    {
        var identity=new TerrainQuadIdentity(new[]{1}); identity.Add(0,Tile(0));
        Assert.Throws<InvalidOperationException>(()=>identity.Add(0,Tile(1)));
        Assert.Throws<InvalidOperationException>(()=>identity.Add(1,Tile(1)));
    }

    [Fact]
    public void NegativeBucketCountIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(()=>new TerrainQuadIdentity(new[]{-1}));

    [Fact]
    public void SameLocalQuadNumberInDifferentMeshesKeepsWinningMeshIdentity()
    {
        var m=new RenderedTileDiagnostics(100,100,1,1);
        void Add(double z,int mesh)
        {
            m.AddQuad(new(-.5,-.5,z,1),new(.5,-.5,z,1),new(.5,.5,z,1),new(-.5,.5,z,1),
                0,2,1,0,3,2,2,0,mesh:mesh);
        }
        Add(.5,12); Add(0,43); Add(.8,7);
        Assert.True(m.Hits[0].Found);
        Assert.Equal(43,m.Hits[0].Mesh);
        Assert.Equal(0,m.Hits[0].Quad);
    }

    [Fact]
    public void AncestorStopIsNotMisrepresentedAsChildStop()
    {
        var trace=new LodSelectionTrace();
        trace.Record(Tile(1),"below-threshold",.02,.03);
        TileId descendant=Tile(1).Child(2).Child(1);
        Assert.True(trace.TryFindStop(descendant,out var ancestor,out var stop));
        Assert.Equal(Tile(1),ancestor);
        Assert.NotEqual(descendant,ancestor);
        Assert.Equal("below-threshold",stop.Reason);
        Assert.Equal(.02,stop.Error);
        Assert.Equal(.03,stop.Threshold);
        Assert.False(trace.TryFindStop(Tile(2),out _,out _));
    }

    [Fact]
    public void NearestStopWinsOverEarlierAncestor()
    {
        var trace=new LodSelectionTrace();
        trace.Record(Tile(1),"below-threshold",.02,.03);
        trace.Record(Tile(1).Child(0),"max-level",.1,.03);
        Assert.True(trace.TryFindStop(Tile(1).Child(0).Child(1),out var ancestor,out var stop));
        Assert.Equal(Tile(1).Child(0),ancestor);
        Assert.Equal("max-level",stop.Reason);
    }

    private static TerrainLodProxy Proxy() => new(3,Enumerable.Repeat(100.0,6*9*9).ToArray(),90);

    [Fact]
    public void CornerOwnerDiagnosticDoesNotSampleTerrain()
    {
        var leaf=TileId.FromFaceLevelUV(0,4,5,4);
        var coverage=LodCoverage.Complete(new[]{leaf},2);
        var resolver=new LodCornerResolver(2,coverage,_=>throw new Exception("Nem mintázhat."));
        Assert.Equal(TileId.FromFaceLevelUV(0,3,3,2),resolver.CornerOwner(0,4,6,5));
        Assert.Equal(TileId.FromFaceLevelUV(0,2,0,1),resolver.CornerOwner(0,4,4,5));
        Assert.Equal(0,resolver.CornerCount);
    }
    private static ProjectedLodView View() => new(new(120,0,0),new(0,0,1),new(0,1,0),
        new(-1,0,0),Math.PI/3,1.8,.01,1000);
    private static HashSet<TileId> Cut(LodSelectionWork work,int budget=20000,int maxLevel=8)
        => AdaptiveQuadTree.BuildCut(120,0,0,100,null!,3,maxLevel,.06,.04,-1,0,0,1.13,budget,
            traversalRootLevel:1,staticBaseLevel:3,terrainProxy:Proxy(),work:work);

    [Theory]
    [InlineData(8,20000,8)]
    [InlineData(100000,16,8)]
    [InlineData(100000,20000,4)]
    public void TraceDoesNotChangeCutOrWorkCounters(int quota,int budget,int maxLevel)
    {
        var plain=new LodSelectionWork(View(),quota);
        var traced=new LodSelectionWork(View(),quota,captureTrace:true);
        Assert.True(Cut(plain,budget,maxLevel).SetEquals(Cut(traced,budget,maxLevel)));
        Assert.Equal(plain.NewSplits,traced.NewSplits);
        Assert.Equal(plain.DeferredSplits,traced.DeferredSplits);
        Assert.Null(plain.Trace);
        Assert.True(traced.Trace!.Count>0);
    }

    [Theory]
    [InlineData(8,20000,8,"split-quota")]
    [InlineData(100000,16,8,"leaf-budget")]
    [InlineData(100000,20000,4,"max-level")]
    [InlineData(100000,20000,8,"below-threshold")]
    public void RecordsActualStopReason(int quota,int budget,int maxLevel,string expected)
    {
        var work=new LodSelectionWork(View(),quota,captureTrace:true);
        var cut=Cut(work,budget,maxLevel);
        Assert.Contains(cut,tile=>work.Trace!.TryFindStop(tile,out _,out var stop)&&stop.Reason==expected);
    }

    [Fact]
    public void OutsideViewRecordsCulledAncestorWithNoInventedThreshold()
    {
        var work=new LodSelectionWork(View(),100000,captureTrace:true);
        Cut(work);
        var tile=TileId.FromFaceLevelUV(2,8,128,128);
        Assert.True(work.Trace!.TryFindStop(tile,out _,out var stop));
        Assert.Equal("outside-view",stop.Reason);
        Assert.True(double.IsNaN(stop.Threshold));
    }
}
