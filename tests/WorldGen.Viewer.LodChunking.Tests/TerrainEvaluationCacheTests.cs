using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class TerrainEvaluationCacheTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void ResetViewInvalidatesEveryProjectionChange(int variation)
    {
        var proxy=Proxy(); var view=View(variation);
        var cache=new LodTerrainEvaluationCache(View(),proxy);
        var tile=TileId.FromFaceLevelUV(0,4,8,8);
        cache.EvaluateTerrain(proxy,tile,out _,out _);
        cache.ResetView(view);
        Assert.Equal(0,cache.Count);
        Assert.Equal(1,cache.ViewResets);
        bool expected=view.EvaluateTerrain(proxy,tile,out double error);
        Assert.Equal(expected,cache.EvaluateTerrain(proxy,tile,out double actual,out bool hit));
        Assert.False(hit); Assert.Equal(error,actual);
        Assert.Throws<ArgumentException>(()=>new LodSelectionWork(View(),evaluationCache:cache));
    }

    [Fact]
    public void IdenticalViewAndInvalidResetDoNotDiscardValidMetrics()
    {
        var proxy=Proxy(); var cache=new LodTerrainEvaluationCache(View(),proxy);
        var tile=TileId.FromFaceLevelUV(0,4,8,8);
        cache.EvaluateTerrain(proxy,tile,out _,out _);
        cache.ResetView(View());
        Assert.Equal(0,cache.ViewResets);
        Assert.Throws<ArgumentNullException>(()=>cache.ResetView(null!));
        cache.EvaluateTerrain(proxy,tile,out _,out bool hit); Assert.True(hit);
    }

    [Fact]
    public void FeedbackInvalidatesOnlyItsTileAndAncestors()
    {
        var proxy=Proxy(); var view=View(); var cache=new LodTerrainEvaluationCache(view,proxy);
        var leaf=TileId.FromFaceLevelUV(0,5,16,16); var other=TileId.FromFaceLevelUV(1,5,16,16);
        foreach(var tile in new[]{leaf,leaf.Parent(),other}) cache.EvaluateTerrain(proxy,tile,out _,out _);
        var quad=new SurfaceQuad(new(100,-10,-10),new(100,-10,10),new(100,10,10),new(100,10,-10));
        Assert.True(cache.Geometry.Record(leaf,quad));
        foreach(var tile in new[]{leaf,leaf.Parent(),other})
        {
            bool visible=cache.Geometry.Evaluate(view,tile,out double expected);
            Assert.Equal(visible,cache.EvaluateTerrain(proxy,tile,out double actual,out bool hit));
            Assert.Equal(tile==other,hit); Assert.Equal(expected,actual);
        }
        Assert.Equal(3,cache.Count);
        Assert.Equal(2,cache.FeedbackRefreshes);
        Assert.False(cache.Geometry.Record(leaf,quad));
        cache.EvaluateTerrain(proxy,leaf,out _,out bool repeatedHit); Assert.True(repeatedHit);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(7)]
    public void FullCacheRefreshesStaleEntriesWithoutGrowing(int capacity)
    {
        var proxy=Proxy(); var view=View();
        var cache=new LodTerrainEvaluationCache(view,proxy,capacity,new LodGeometryCache(proxy,100));
        var tiles=Enumerable.Range(0,10).Select(i=>TileId.FromFaceLevelUV(0,5,(uint)i,16)).ToArray();
        foreach(var tile in tiles) cache.EvaluateTerrain(proxy,tile,out _,out _);
        foreach(var tile in tiles)
        {
            cache.Geometry.Record(tile,new SurfaceQuad(new(100,-1,-1),new(100,-1,1),new(100,1,1),new(100,1,-1)));
            cache.Geometry.Evaluate(view,tile,out double expected);
            cache.EvaluateTerrain(proxy,tile,out double actual,out bool hit);
            Assert.False(hit); Assert.Equal(expected,actual);
            Assert.Equal(capacity,cache.Count);
        }
    }

    [Fact]
    public void WarmViewResetAndEvaluationReuseStorageWithoutAllocating()
    {
        var proxy=Proxy(); var views=new[]{View(distance:120),View(distance:130),View(distance:140)};
        var cache=new LodTerrainEvaluationCache(views[0],proxy);
        var tiles=Enumerable.Range(0,6).SelectMany(f=>Enumerable.Range(0,16)
            .Select(i=>TileId.FromFaceLevelUV(f,4,(uint)i,8))).ToArray();
        void Scan()
        { foreach(var view in views) { cache.ResetView(view); foreach(var tile in tiles) cache.EvaluateTerrain(proxy,tile,out _,out _); } }
        long allocated=-1; Exception? failure=null;
        var thread=new Thread(()=>
        {
            try
            {
                Scan();
                long before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<10;i++) Scan();
                allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            }
            catch(Exception error) { failure=error; }
        });
        thread.Start(); thread.Join();
        Assert.Null(failure); Assert.Equal(0,allocated);
    }

    private static TerrainLodProxy Proxy() => new(3,
        Enumerable.Range(0, 6*9*9).Select(i => 100 + (i%9)*.1).ToArray(), 90);

    private static ProjectedLodView View(int variation = 0, double distance = 120)
        => new(new SurfacePoint(distance + (variation == 1 ? 1e-10 : 0),0,0),
            new SurfacePoint(variation == 2 ? 1e-10 : 0,0,1),
            new SurfacePoint(0,1,variation == 3 ? 1e-10 : 0),
            new SurfacePoint(-1,variation == 4 ? 1e-10 : 0,0),
            Math.PI/3 + (variation == 5 ? 1e-10 : 0),
            1.8 + (variation == 6 ? 1e-10 : 0),
            .01 + (variation == 7 ? 1e-10 : 0), 1000 + (variation == 8 ? 1e-10 : 0));

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void AnyProjectionChangeInvalidatesCache(int variation)
    {
        var proxy = Proxy();
        var cache = new LodTerrainEvaluationCache(View(), proxy);
        Assert.True(cache.Matches(View(), proxy));
        Assert.False(cache.Matches(View(variation), proxy));
        Assert.Throws<ArgumentException>(() => new LodSelectionWork(View(variation), evaluationCache: cache));
    }

    [Fact]
    public void DifferentProxyCannotReadOldValues()
    {
        var cache = new LodTerrainEvaluationCache(View(), Proxy());
        var other = Proxy();
        Assert.False(cache.Matches(View(), other));
        Assert.Throws<ArgumentException>(() => cache.EvaluateTerrain(other,
            TileId.FromFaceLevelUV(0,3,4,4), out _, out _));
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(262144)]
    public void ColdWarmAndFullCacheReturnExactMetrics(int capacity)
    {
        var view = View(); var proxy = Proxy();
        var cache = new LodTerrainEvaluationCache(view, proxy, capacity);
        var tiles = Enumerable.Range(0,6).SelectMany(f => Enumerable.Range(0,6)
            .Select(l => TileId.FromFaceLevelUV(f,l,0,0))).ToArray();
        for (int pass=0; pass<2; pass++)
        for (int i=0; i<tiles.Length; i++)
        {
            bool expected = view.EvaluateTerrain(proxy, tiles[i], out double error);
            Assert.Equal(expected, cache.EvaluateTerrain(proxy, tiles[i], out double actual, out bool hit));
            Assert.Equal(BitConverter.DoubleToInt64Bits(error), BitConverter.DoubleToInt64Bits(actual));
            Assert.Equal(pass > 0 && i < capacity, hit);
        }
        Assert.Equal(Math.Min(capacity, tiles.Length), cache.Count);
    }

    [Theory]
    [InlineData(5)] [InlineData(262144)]
    public void ProgressiveCutsAndTraceRemainExactAcrossZoomAndThresholdChanges(int capacity)
    {
        var proxy = Proxy();
        HashSet<TileId> previous = null!;
        LodTerrainEvaluationCache? cache = null;
        int hits = 0;
        foreach (double distance in new[] {120.0, 110, 160, 120})
        for (int wave=0; wave<6; wave++)
        {
            var view = View(distance:distance);
            if (cache == null || !cache.Matches(view, proxy)) cache = new(view, proxy, capacity);
            bool Skip(TileId t) => t.Face == 2;
            var reference = new LodSelectionWork(view, 8 + wave, captureTrace:true, skipStaticBase:Skip);
            var cached = new LodSelectionWork(view, 8 + wave, captureTrace:true, skipStaticBase:Skip, evaluationCache:cache);
            double threshold = wave < 3 ? .06 : .05;
            HashSet<TileId> Cut(LodSelectionWork work) => AdaptiveQuadTree.BuildCut(distance,0,0,100,previous,
                3,8,threshold,threshold/1.5,-1,0,0,1.13,20000,traversalRootLevel:1,staticBaseLevel:3,
                terrainProxy:proxy,work:work);
            var expected = Cut(reference); var actual = Cut(cached);
            Assert.True(expected.SetEquals(actual));
            Assert.Equal(reference.NewSplits, cached.NewSplits);
            Assert.Equal(reference.DeferredSplits, cached.DeferredSplits);
            Assert.Equal(reference.SkippedStaticBases, cached.SkippedStaticBases);
            Assert.Equal(reference.MetricEvaluations, cached.MetricEvaluations + cached.MetricCacheHits);
            Assert.Equal(reference.Trace!.Count, cached.Trace!.Count);
            // A teljes teszthierarchián a megálló ős és minden trace-érték egyezzen.
            foreach (var tile in TraceQueries())
            {
                Assert.Equal(reference.Trace.TryFindStop(tile,out var ea,out var es),
                    cached.Trace.TryFindStop(tile,out var aa,out var ac));
                Assert.Equal(ea, aa); Assert.Equal(es.Reason, ac.Reason);
                Assert.Equal(BitConverter.DoubleToInt64Bits(es.Error), BitConverter.DoubleToInt64Bits(ac.Error));
                Assert.Equal(BitConverter.DoubleToInt64Bits(es.Threshold), BitConverter.DoubleToInt64Bits(ac.Threshold));
            }
            hits += cached.MetricCacheHits;
            previous = actual;
        }
        Assert.True(hits > 0);
    }

    private static IEnumerable<TileId> TraceQueries()
    {
        // L8 lekérdezése minden lehetséges megálló őst elér a tesztben.
        for (int f=0; f<6; f++) for (uint u=0; u<256; u++) for (uint v=0; v<256; v++)
            yield return TileId.FromFaceLevelUV(f,8,u,v);
    }

    [Fact]
    public void CancelledWorkCanLeaveOnlyReusablePureMetrics()
    {
        var proxy=Proxy(); var view=View(); var cache=new LodTerrainEvaluationCache(view,proxy);
        using var cancellation=new CancellationTokenSource();
        bool Cancel(TileId tile) { cancellation.Cancel(); return false; }
        Assert.Throws<OperationCanceledException>(() => AdaptiveQuadTree.BuildCut(120,0,0,100,null!,3,8,
            .06,.04,-1,0,0,1.13,20000,traversalRootLevel:1,staticBaseLevel:3,terrainProxy:proxy,
            work:new LodSelectionWork(view,8,cancellation.Token,skipStaticBase:Cancel,evaluationCache:cache)));
        Assert.True(cache.Count > 0);
        var tile=TileId.FromFaceLevelUV(0,3,4,4);
        Assert.Equal(view.EvaluateTerrain(proxy,tile,out double error),cache.EvaluateTerrain(proxy,tile,out double actual,out _));
        Assert.Equal(error,actual);
    }
}
