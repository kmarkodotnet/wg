using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class EarlyOceanExclusionTests
{
    private static ProjectedLodView View(double distance=120)=>new(new(distance,0,0),new(0,0,1),new(0,1,0),new(-1,0,0),Math.PI/3,1.8,.01,1000);
    private static HashSet<TileId> Cut(LodSelectionWork work,HashSet<TileId>? previous=null,double distance=120,int budget=20000)
        => AdaptiveQuadTree.BuildCut(distance,0,0,100,previous!,3,8,.06,.04,-1,0,0,1.13,budget,
            traversalRootLevel:1,staticBaseLevel:3,terrainProxy:new TerrainLodProxy(3,Enumerable.Repeat(100.0,6*9*9).ToArray(),90),work:work);
    private static TileId Root(TileId t) { while(t.Level>3)t=t.Parent();return t; }

    [Fact]
    public void ExcludedBaseConsumesNoSplitQuotaAndHasExplicitTrace()
    {
        var oldWork=new LodSelectionWork(View(),1);
        var old=Cut(oldWork);
        TileId excluded=Root(old.OrderBy(t=>t.Value).First());
        var work=new LodSelectionWork(View(),1,captureTrace:true,skipStaticBase:t=>t==excluded);
        var cut=Cut(work);
        Assert.DoesNotContain(cut,t=>Root(t)==excluded);
        Assert.NotEmpty(cut);
        Assert.Equal(1,work.NewSplits);
        Assert.Equal(1,work.SkippedStaticBases);
        Assert.True(work.Trace!.TryFindStop(excluded,out TileId at,out var stop));
        Assert.Equal(excluded,at); Assert.Equal("renderer-base-exclusion",stop.Reason);
    }

    [Fact]
    public void ExcludingAllBaseTilesStopsInsteadOfRequestingEndlessRefinement()
    {
        int calls=0;
        var work=new LodSelectionWork(View(),1,skipStaticBase:t=>{Assert.Equal(3,t.Level);calls++;return true;});
        Assert.Empty(Cut(work));
        Assert.True(calls>0); Assert.Equal(calls,work.SkippedStaticBases);
        Assert.Equal(0,work.NewSplits); Assert.Equal(0,work.DeferredSplits);
    }

    [Fact]
    public void NonExcludedCoastPreservesOriginalCutAndBudgetCounters()
    {
        var old=new LodSelectionWork(View(),8);
        var work=new LodSelectionWork(View(),8,skipStaticBase:_=>false);
        Assert.True(Cut(old).SetEquals(Cut(work)));
        Assert.Equal(old.NewSplits,work.NewSplits); Assert.Equal(old.DeferredSplits,work.DeferredSplits);
        Assert.Equal(0,work.SkippedStaticBases);
    }

    [Fact]
    public void PreviouslyExpandedExcludedRegionReturnsToStaticCoverageWithoutMutatingPreviousCut()
    {
        var old=Cut(new LodSelectionWork(View())); var copy=old.ToHashSet();
        var work=new LodSelectionWork(View(),8,skipStaticBase:_=>true);
        Assert.Empty(Cut(work,old));
        Assert.True(copy.SetEquals(old));
        Assert.Equal(0,work.DeferredSplits);
    }

    [Fact]
    public void RemainingRegionsKeepCompleteCoverageAndZoomBackWorks()
    {
        HashSet<TileId>? previous=null;
        for(int i=0;i<200;i++)
        {
            var work=new LodSelectionWork(View(),8,skipStaticBase:t=>{t.GetUV(out uint u,out _);return t.Face==0&&u<4;});
            previous=Cut(work,previous);
            var coverage=LodCoverage.Complete(previous,3);
            Assert.DoesNotContain(coverage.Roots,t=>{t.GetUV(out uint u,out _);return t.Face==0&&u<4;});
            foreach(var root in coverage.Roots)
                Assert.Equal(1,coverage.Leaves.Where(t=>Root(t)==root).Sum(t=>Math.Pow(.25,t.Level-3)),12);
            if(work.DeferredSplits==0) break;
            Assert.True(i<199);
        }
        Assert.Empty(Cut(new LodSelectionWork(View(800),skipStaticBase:_=>false),previous,800));
    }

    [Fact]
    public void CancellationIsCheckedBeforeExclusionCallback()
    {
        using var cts=new CancellationTokenSource();cts.Cancel();
        var work=new LodSelectionWork(View(),cancellation:cts.Token,skipStaticBase:_=>throw new Exception("Nem hívható."));
        Assert.Throws<OperationCanceledException>(()=>Cut(work));
    }

    [Fact]
    public void ExclusionStillHonorsLeafBudget()
    {
        var work=new LodSelectionWork(View(),skipStaticBase:t=>{t.GetUV(out uint u,out _);return u<4;});
        Assert.InRange(Cut(work,budget:16).Count,0,16);
        Assert.Equal(0,work.DeferredSplits);
    }
}
