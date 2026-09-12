using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class WaterLodSourceTests
{
    private static WaterLodSelection Settle(WaterLodSource source)
    {
        WaterLodSelection? result = null;
        for (int i = 0; i < 100; i++)
        {
            result = Select(source,result);
            if (result.ReusedSelection) return result;
        }
        throw new InvalidOperationException("A teszt vízkiválasztása nem stabilizálódott.");
    }

    [Fact]
    public void StableReuseRequiresAnotherFullSelectionAndReportsOnlyCurrentWork()
    {
        var source = new WaterLodSource(3,100,Array.Empty<TileId>());
        var first = Select(source);
        Assert.False(first.RefinementPending);
        var second = Select(source,first);
        Assert.False(second.ReusedSelection);
        Assert.NotSame(first.Leaves,second.Leaves);
        var third = Select(source,second);
        Assert.True(third.ReusedSelection);
        Assert.NotSame(second,third);
        Assert.Same(second.Leaves,third.Leaves);
        Assert.Same(second.ReplacedRoots,third.ReplacedRoots);
        Assert.Equal(0,third.NewSplits); Assert.Equal(0,third.DeferredSplits);
        Assert.Equal(0,third.SelectionMs); Assert.Equal(0,third.BalanceMs);
    }

    [Theory]
    [InlineData(1)] [InlineData(32)] [InlineData(256)]
    public void RepeatedAndMovingSelectionsExactlyMatchFullSelection(int quota)
    {
        var source = new WaterLodSource(3,100,Roots());
        WaterLodSelection? previous = null;
        LodTerrainEvaluationCache? cache = null;
        int reuseCount = 0;
        foreach (double distance in new[] {300.0,120,105,125,300})
        for (int i = 0; i < 20; i++)
        {
            var view = View(distance);
            cache = source.CreateEvaluationCache(view,cache);
            var expected = source.Select(view,previous,7,.06,.04,4000,quota,reuseStableSelection:false);
            var actual = source.Select(view,previous,7,.06,.04,4000,quota,evaluationCache:cache);
            Assert.Equal(expected.Leaves,actual.Leaves);
            Assert.Equal(expected.ReplacedRoots,actual.ReplacedRoots);
            Assert.Equal(expected.DeepestLevel,actual.DeepestLevel);
            Assert.Equal(expected.RefinementPending,actual.RefinementPending);
            if (actual.ReusedSelection)
            {
                reuseCount++;
                Assert.Same(previous!.Leaves,actual.Leaves);
                Assert.Equal(0,actual.NewSplits);
            }
            else
            {
                Assert.Equal(expected.NewSplits,actual.NewSplits);
                Assert.Equal(expected.DeferredSplits,actual.DeferredSplits);
            }
            previous = actual;
        }
        Assert.True(reuseCount > 0);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    [InlineData(10)] [InlineData(11)]
    public void AnyChangedSelectionInputInvalidatesReuse(int change)
    {
        var source = new WaterLodSource(3,100,Roots());
        var stable = Settle(source);
        const double delta = 1e-10;
        var view = new ProjectedLodView(new(120+(change==0?delta:0),0,0),
            new(change==1?delta:0,0,1),new(change==2?delta:0,1,0),new(-1,change==3?delta:0,0),
            Math.PI/3+(change==4?delta:0),1.8+(change==5?delta:0),
            .01+(change==6?delta:0),1000+(change==7?delta:0));
        int level = change==8?6:7, budget = change==11?3999:4000;
        double split = .06+(change==9?delta:0), merge = .04+(change==10?delta:0);
        var actual = source.Select(view,stable,level,split,merge,budget,32);
        Assert.False(actual.ReusedSelection);
        var expected = source.Select(view,stable,level,split,merge,budget,32,reuseStableSelection:false);
        Assert.Equal(expected.Leaves,actual.Leaves);
        Assert.False(source.Select(View(),stable,7,.06,.04,4000,33).ReusedSelection);
    }

    [Fact]
    public void StableFastPathStillValidatesCancellationSourceCacheAndArguments()
    {
        var source = new WaterLodSource(3,100,Roots());
        var stable = Settle(source);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>source.Select(View(),stable,7,.06,.04,4000,32,cancellation.Token));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Select(source,stable,quota:0));
        Assert.Throws<ArgumentNullException>(()=>source.Select(null!,stable,7,.06,.04,4000,32));
        var other = new WaterLodSource(3,100,Roots());
        Assert.Throws<ArgumentException>(()=>Select(other,stable));
        Assert.Throws<ArgumentException>(()=>source.Select(View(),stable,7,.06,.04,4000,32,
            evaluationCache:other.CreateEvaluationCache(View(),null)));
        Assert.Throws<ArgumentException>(()=>source.Select(View(),stable,7,.06,.04,4000,32,
            evaluationCache:source.CreateEvaluationCache(View(121),null)));
        var cache = source.CreateEvaluationCache(View(),null);
        var q = new SurfaceQuad(new(100,0,0),new(100,1,0),new(100,1,1),new(100,0,1));
        cache.Geometry.Record(TileId.FromFaceLevelUV(0,3,4,4),q);
        Assert.False(source.Select(View(),stable,7,.06,.04,4000,32,evaluationCache:cache).ReusedSelection);
        Assert.True(Select(source,stable).ReusedSelection);
    }

    [Fact]
    public void ReusedCoverageAndIndependentResolversRemainImmutableAcrossThreads()
    {
        var source = new WaterLodSource(3,100,Roots());
        var stable = Settle(source);
        var saved = stable.Leaves.ToArray();
        var expected = source.Select(View(),stable,7,.06,.04,4000,32,reuseStableSelection:false);
        var resolver = expected.CreateCornerResolver();
        var corners = expected.Leaves.Select(t=>
        {
            t.GetUV(out uint u,out uint v); return resolver.Corner(t.Face,t.Level,u,v);
        }).ToArray();
        Parallel.For(0,4,_=>
        {
            var reused = Select(source,stable);
            Assert.True(reused.ReusedSelection);
            var actual = reused.CreateCornerResolver();
            for (int i=0;i<saved.Length;i++)
            {
                var t=saved[i];t.GetUV(out uint u,out uint v);
                var p=actual.Corner(t.Face,t.Level,u,v);
                Assert.Equal(corners[i].X,p.X); Assert.Equal(corners[i].Y,p.Y); Assert.Equal(corners[i].Z,p.Z);
            }
        });
        Assert.Equal(saved,stable.Leaves);
    }

    [Fact]
    public void MovingWaterCachePreservesSelectionsAndCannotCrossWorlds()
    {
        var source = new WaterLodSource(3,100,Roots());
        LodTerrainEvaluationCache? cache = null;
        WaterLodSelection? previous = null;
        foreach (double distance in new[] {180.0,150,120,110,125,180,300})
        {
            var view = View(distance);
            var old = cache;
            cache = source.CreateEvaluationCache(view,cache);
            if (old != null) Assert.Same(old.Geometry,cache.Geometry);
            Assert.Same(cache,source.CreateEvaluationCache(view,cache));
            var expected = source.Select(view,previous,7,.06,.04,4000,32);
            var actual = source.Select(view,previous,7,.06,.04,4000,32,evaluationCache:cache);
            Assert.Equal(expected.Leaves,actual.Leaves);
            Assert.Equal(expected.ReplacedRoots,actual.ReplacedRoots);
            Assert.Equal(expected.NewSplits,actual.NewSplits);
            Assert.Equal(expected.DeferredSplits,actual.DeferredSplits);
            previous = actual;
        }
        var other = new WaterLodSource(3,100,Roots());
        Assert.NotSame(cache!.Geometry,other.CreateEvaluationCache(View(),cache).Geometry);
        Assert.Throws<ArgumentException>(()=>other.Select(View(),null,7,.06,.04,4000,32,evaluationCache:cache));
        Assert.Throws<ArgumentException>(()=>source.Select(View(130),null,7,.06,.04,4000,32,evaluationCache:cache));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(1.5)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InspectorHysteresisAlwaysProducesAValidWaterRequest(double factor)
    {
        var source = new WaterLodSource(3,100,Roots());
        double merge = AdaptiveViewState.MergeThreshold(.06,factor);
        Assert.InRange(merge,double.Epsilon,.06-double.Epsilon);
        Assert.True(merge < .06);
        Assert.NotEmpty(source.Select(View(),null,7,.06,merge,4000,32).Leaves);
    }

    private static TileId[] Roots(int level=3) =>
        (from face in Enumerable.Range(0,6)
         from u in Enumerable.Range(0,1<<level)
         from v in Enumerable.Range(0,1<<level)
         select TileId.FromFaceLevelUV(face,level,(uint)u,(uint)v)).ToArray();
    private static ProjectedLodView View(double distance=120)
        => new(new(distance,0,0),new(0,0,1),new(0,1,0),new(-1,0,0),Math.PI/3,1.8,.01,1000);
    private static WaterLodSelection Select(WaterLodSource source, WaterLodSelection? previous=null,
        double distance=120, int budget=4000, int quota=32, double threshold=.06, int maxLevel=7)
        => source.Select(View(distance),previous,maxLevel,threshold,threshold/1.5,budget,quota);
    private static TileId Ancestor(TileId t,int level=3)
    { while(t.Level>level)t=t.Parent();return t; }

    [Fact]
    public void WaterCanRefineWithoutAnyTerrainCutOrTerrainSampler()
    {
        var water = new WaterLodSource(3,100,Roots());
        var result = Select(water);
        Assert.NotEmpty(result.Leaves);
        Assert.Equal(32,result.NewSplits);
        Assert.True(result.RefinementPending);
        // A víz API-ja sem terep-cutot, sem modell-callbacket nem fogad.
        Assert.All(result.Leaves,t=>Assert.True(water.ContainsWater(t)));
    }

    [Fact]
    public void EmptyWaterMaskProducesNoWorkOrHiddenRoots()
    {
        var result=Select(new WaterLodSource(3,100,Array.Empty<TileId>()));
        Assert.Empty(result.Leaves);Assert.Empty(result.ReplacedRoots);
        Assert.Equal(0,result.NewSplits);Assert.False(result.RefinementPending);
        Assert.False(result.TryFindRenderedLeaf(TileId.FromFaceLevelUV(0,7,64,64),out _));
    }

    [Fact]
    public void SourceCopiesAndDeduplicatesTheEmittedWaterMask()
    {
        var root=TileId.FromFaceLevelUV(0,3,4,4);
        var input=new List<TileId>{root,root};
        var source=new WaterLodSource(3,100,input);
        input.Clear();
        Assert.Equal(1,source.WaterRootCount);
        Assert.True(source.ContainsWater(root.Child(0)));
        Assert.False(source.ContainsWater(root.Parent()));
        Assert.False(source.ContainsWater(TileId.FromFaceLevelUV(1,3,4,4)));
    }

    [Theory]
    [InlineData(1)] [InlineData(4)] [InlineData(17)] [InlineData(100)] [InlineData(4000)]
    public void BudgetNeverHidesPartiallyCoveredBaseAndDoesNotFloodLand(int budget)
    {
        var roots=Roots().Where(t=> {t.GetUV(out uint u,out uint v);return (u+v)%3!=0;}).ToArray();
        var source=new WaterLodSource(3,100,roots);
        WaterLodSelection? previous=null;
        for(int wave=0;wave<5;wave++)
        {
            var result=Select(source,previous,budget:budget,quota:5);
            Assert.InRange(result.Leaves.Count,0,budget);
            Assert.InRange(result.NewSplits,0,5);
            Assert.All(result.Leaves,t=>Assert.Contains(Ancestor(t),roots));
            foreach(var root in result.ReplacedRoots)
            {
                Assert.Contains(root,roots);
                double area=result.Leaves.Where(t=>Ancestor(t)==root).Sum(t=>Math.Pow(.25,t.Level-3));
                Assert.Equal(1.0,area,12);
                Assert.DoesNotContain(root,result.Leaves);
            }
            Assert.Equal(result.Leaves.Select(t=>Ancestor(t)).Distinct().OrderBy(t=>t.Value),result.ReplacedRoots);
            var leafSet=result.Leaves.ToHashSet();
            foreach(var leaf in result.Leaves)
            {
                for(var parent=leaf.Parent();parent.Level>3;parent=parent.Parent())
                    Assert.DoesNotContain(parent,leafSet);
            }
            previous=result;
        }
    }

    [Fact]
    public void StationaryWavesConvergeAndZoomBackRestoresStaticWater()
    {
        var source=new WaterLodSource(3,100,Roots());
        WaterLodSelection? previous=null;
        int waves=0;
        do { previous=Select(source,previous); waves++; }
        while(previous.RefinementPending && waves<200);
        Assert.InRange(waves,2,199);
        Assert.False(previous.RefinementPending);
        Assert.Equal(previous.Leaves,Select(source,previous).Leaves);
        var far=Select(source,previous,distance:800);
        Assert.Empty(far.Leaves);Assert.Empty(far.ReplacedRoots);
        Assert.False(far.RefinementPending);
        Assert.True(far.TryFindRenderedLeaf(TileId.FromFaceLevelUV(0,7,64,64),out var fallback));
        Assert.Equal(3,fallback.Level);
    }

    [Fact]
    public void MaximumLevelStopsWaterSubdivision()
    {
        var source=new WaterLodSource(3,100,Roots());
        var result=Select(source,maxLevel:3);
        Assert.Empty(result.Leaves);Assert.False(result.RefinementPending);
        result=Select(source,distance:101,quota:10000,maxLevel:4);
        Assert.NotEmpty(result.Leaves);
        Assert.All(result.Leaves,t=>Assert.Equal(4,t.Level));
        Assert.False(result.RefinementPending);
    }

    [Fact]
    public void WaterThresholdAndSeaRadiusAffectSelection()
    {
        var low=new WaterLodSource(3,95,Roots());
        var high=new WaterLodSource(3,105,Roots());
        var coarse=Select(low,quota:10000,threshold:.08);
        var fine=Select(low,quota:10000,threshold:.04);
        Assert.True(fine.Leaves.Count>coarse.Leaves.Count);
        Assert.False(fine.Leaves.SequenceEqual(Select(high,quota:10000,threshold:.04).Leaves));
    }

    [Fact]
    public void PreviousSelectionAndPublishedListsCannotBeChangedByLaterRequests()
    {
        var source=new WaterLodSource(3,100,Roots());
        var previous=Select(source);var saved=previous.Leaves.ToArray();
        Select(source,previous,distance:110);
        Assert.Equal(saved,previous.Leaves);
        Assert.Throws<NotSupportedException>(()=>((IList<TileId>)previous.Leaves).Clear());
        Assert.Throws<ArgumentException>(()=>Select(new WaterLodSource(3,100,Roots()),previous));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>source.Select(View(),previous,7,.06,.04,4000,32,cancelled.Token));
        Assert.Equal(saved,previous.Leaves);
    }

    [Fact]
    public void ParallelAndReorderedInputProduceTheSameIndependentCut()
    {
        var source=new WaterLodSource(3,100,Roots());
        var expected=Select(source).Leaves;
        Assert.Equal(expected,Select(new WaterLodSource(3,100,Roots().AsEnumerable().Reverse())).Leaves);
        Parallel.For(0,4,_=>Assert.Equal(expected,Select(source).Leaves));
    }

    [Fact]
    public void ResolvedWaterCornersAreFiniteAndTraversalIndependent()
    {
        var source=new WaterLodSource(3,100,Roots());
        var selection=Select(source,distance:101,budget:200,quota:32);
        var forward=selection.CreateCornerResolver();var reverse=selection.CreateCornerResolver();
        var keys=selection.Leaves.SelectMany(t=>
        {
            t.GetUV(out uint u,out uint v);
            return new[]{(t.Face,t.Level,u,v),(t.Face,t.Level,u+1,v),
                (t.Face,t.Level,u+1,v+1),(t.Face,t.Level,u,v+1)};
        }).ToArray();
        var expected=keys.Select(k=>forward.Corner(k.Face,k.Level,k.Item3,k.Item4)).ToArray();
        for(int i=keys.Length-1;i>=0;i--)
        {
            var k=keys[i];var p=reverse.Corner(k.Face,k.Level,k.Item3,k.Item4);
            Assert.Equal(expected[i].X,p.X);Assert.Equal(expected[i].Y,p.Y);Assert.Equal(expected[i].Z,p.Z);
            Assert.True(double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z));
            double radius=Math.Sqrt(p.X*p.X+p.Y*p.Y+p.Z*p.Z);
            // A durva vízhúrra illesztett pont a gömbön belül lehet, kívül nem.
            Assert.InRange(radius,95,100+1e-10);
        }
    }

    [Fact]
    public void WaterPatchBoundaryMatchesUntouchedStaticWaterChord()
    {
        var root=TileId.FromFaceLevelUV(0,3,4,4);
        var source=new WaterLodSource(3,100,new[]{root});
        var result=Select(source,quota:1000,maxLevel:5,threshold:.005);
        Assert.NotEmpty(result.Leaves);
        var resolver=result.CreateCornerResolver();
        SurfacePoint Point(double u,double v)
        {
            TileGeometry.PositionFromFaceUV(0,u/8*2-1,v/8*2-1,out double x,out double y,out double z);
            return new(x*100,y*100,z*100);
        }
        var midpoint=SurfacePoint.Lerp(Point(4,4),Point(4,5),.5);
        var actual=resolver.Corner(0,5,16,18);
        Assert.InRange(Math.Abs(midpoint.X-actual.X),0,1e-9);
        Assert.InRange(Math.Abs(midpoint.Y-actual.Y),0,1e-9);
        Assert.InRange(Math.Abs(midpoint.Z-actual.Z),0,1e-9);
        Assert.True(result.TryFindRenderedLeaf(TileId.FromFaceLevelUV(0,7,65,65),out var leaf));
        Assert.True(leaf.Level>3);
        Assert.Throws<ArgumentException>(()=>result.TryFindRenderedLeaf(root,out _));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidSeaRadiusIsRejected(double radius)
        => Assert.Throws<ArgumentOutOfRangeException>(()=>new WaterLodSource(3,radius,Roots()));

    [Fact]
    public void InvalidMasksAndBudgetsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>new WaterLodSource(9,100,Array.Empty<TileId>()));
        Assert.Throws<ArgumentException>(()=>new WaterLodSource(3,100,Roots(2)));
        var source=new WaterLodSource(3,100,Roots());
        Assert.Throws<ArgumentOutOfRangeException>(()=>Select(source,budget:0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Select(source,quota:0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Select(source,threshold:double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Select(source,maxLevel:2));
    }

    [Theory]
    [InlineData(300)] [InlineData(130)] [InlineData(105)]
    public void ProductionBaseLevelKeepsIndependentWaterBudget(double distance)
    {
        const int budget=8192;
        var water=new WaterLodSource(8,100,Roots(8));
        double threshold=AdaptiveViewState.AngularRadiusForPixelDiameter(12,Math.PI/3,688);
        var result=water.Select(View(distance),null,20,threshold,threshold/1.5,budget,256);
        Assert.InRange(result.Leaves.Count,0,budget);
        Assert.InRange(result.NewSplits,0,256);
        if(distance<140) Assert.NotEmpty(result.Leaves);
        Assert.Equal(6*256*256,water.MaskStorageBytes);
        foreach(var group in result.Leaves.GroupBy(t=>Ancestor(t,8)))
            Assert.Equal(1,group.Sum(t=>Math.Pow(.25,t.Level-8)),12);
    }

    [Fact]
    public void BaseZeroAndCubeFaceBoundaryRemainSupported()
    {
        var source=new WaterLodSource(0,100,Roots(0));
        var view=new ProjectedLodView(new(90,0,90),new(1,0,-1),new(0,1,0),new(-1,0,-1),Math.PI/3,1.8,.01,1000);
        var result=source.Select(view,null,5,.04,.03,1024,256);
        var resolver=result.CreateCornerResolver();
        Assert.NotEmpty(result.Leaves);
        var shared=new Dictionary<(double,double,double),(int Face,SurfacePoint Point)>();
        int crossFaceMatches=0;
        foreach(var leaf in result.Leaves)
        {
            leaf.GetUV(out uint u,out uint v);
            for(uint du=0;du<=1;du++) for(uint dv=0;dv<=1;dv++)
            {
                double n=1<<leaf.Level;
                TileGeometry.PositionFromFaceUV(leaf.Face,(u+du)/n*2-1,(v+dv)/n*2-1,out double x,out double y,out double z);
                var key=(Math.Round(x,12),Math.Round(y,12),Math.Round(z,12));
                var point=resolver.Corner(leaf.Face,leaf.Level,u+du,v+dv);
                if(shared.TryGetValue(key,out var previous))
                {
                    Assert.InRange(Math.Abs(previous.Point.X-point.X),0,1e-9);
                    Assert.InRange(Math.Abs(previous.Point.Y-point.Y),0,1e-9);
                    Assert.InRange(Math.Abs(previous.Point.Z-point.Z),0,1e-9);
                    if(previous.Face!=leaf.Face) crossFaceMatches++;
                }
                shared[key]=(leaf.Face,point);
            }
        }
        Assert.True(crossFaceMatches>0);
    }
}
