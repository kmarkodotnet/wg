using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    public class LodGeometryCacheTests
    {
        private static TerrainLodProxy Proxy() => new(3, Enumerable.Repeat(100.0, 6*9*9).ToArray(), 100);
        private static ProjectedLodView View(double distance = 130) => new(new SurfacePoint(distance,0,0),
            new SurfacePoint(0,0,1), new SurfacePoint(0,1,0), new SurfacePoint(-1,0,0), Math.PI/3,1.6,.01,10000);
        private static SurfaceQuad Patch(double half) => new(new SurfacePoint(100,-half,-half),
            new SurfacePoint(100,-half,half),new SurfacePoint(100,half,half),new SurfacePoint(100,half,-half));

        [Theory]
        [InlineData(0)] [InlineData(7)] [InlineData(262144)]
        public void ReprojectionPreservesExactMetricsAndBoundedGeometry(int capacity)
        {
            var proxy = Proxy(); var cache = new LodTerrainEvaluationCache(View(),proxy,capacity);
            var geometry = cache.Geometry;
            foreach (double distance in new[] {130.0,120,110,115,140})
            {
                var view = View(distance); cache = cache.Reproject(view);
                Assert.Same(geometry, cache.Geometry);
                foreach (TileId tile in Enumerable.Range(0,6).SelectMany(f=>Enumerable.Range(0,6)
                    .Select(l=>TileId.FromFaceLevelUV(f,l,0,0))))
                {
                    bool visible = view.EvaluateTerrain(proxy,tile,out double expected);
                    Assert.Equal(visible,cache.EvaluateTerrain(proxy,tile,out double actual,out _));
                    Assert.Equal(expected,actual);
                }
                Assert.InRange(geometry.Count,0,capacity);
            }
            if (capacity>0) Assert.True(geometry.Hits>0);
        }

        [Fact]
        public void ActualGeometryInvalidatesSameViewMetricAndExpandsAncestorVisibility()
        {
            var proxy=Proxy(); var view=View(); var cache=new LodTerrainEvaluationCache(view,proxy);
            // A nézettel ellentétes lap proxyját egy valóban előállított látható patch felülírhatja.
            var tile=TileId.FromFaceLevelUV(1,5,16,16);
            cache.EvaluateTerrain(proxy,tile,out double before,out _);
            cache.EvaluateTerrain(proxy,tile,out _,out bool hit); Assert.True(hit);
            Assert.True(cache.Geometry.Record(tile,Patch(12)));
            Assert.True(cache.EvaluateTerrain(proxy,tile,out double after,out hit));
            Assert.False(hit); Assert.True(after>before);
            view.EvaluateQuad(Patch(12),out double expected);
            Assert.True(after>=expected);
            var ancestor=tile.Parent();
            while(true)
            {
                Assert.True(cache.EvaluateTerrain(proxy,ancestor,out _,out _));
                if(ancestor.Level==0) break; ancestor=ancestor.Parent();
            }
            int revision=cache.Geometry.Revision;
            Assert.False(cache.Geometry.Record(tile,Patch(12)));
            Assert.Equal(revision,cache.Geometry.Revision);
        }

        [Fact]
        public void FeedbackUsesGeometryNotStalePixelsAfterZoomOut()
        {
            var proxy=Proxy(); var tile=TileId.FromFaceLevelUV(0,8,128,128);
            var cache=new LodTerrainEvaluationCache(View(110),proxy);
            cache.Geometry.Record(tile,Patch(1));
            cache.EvaluateTerrain(proxy,tile,out double close,out _);
            var far=cache.Reproject(View(300));
            far.EvaluateTerrain(proxy,tile,out double distant,out _);
            Assert.True(distant<close/10);
        }

        [Fact]
        public void PreviouslyMeasuredFlatParentCannotHideLargerKnownChild()
        {
            var proxy=Proxy(); var view=View(); var cache=new LodTerrainEvaluationCache(view,proxy);
            var parent=TileId.FromFaceLevelUV(0,4,8,8);
            cache.Geometry.Record(parent,Patch(1));
            cache.EvaluateTerrain(proxy,parent,out double before,out _);
            cache.Geometry.Record(parent.Child(0),Patch(15));
            view.EvaluateQuad(Patch(15),out double childError);
            cache.EvaluateTerrain(proxy,parent,out double after,out _);
            Assert.True(after>=childError); Assert.True(after>before);
        }

        [Theory]
        [InlineData(0)] [InlineData(2)] [InlineData(6)]
        public void FullFeedbackCacheDoesNotPartiallyStoreAnAncestorChain(int capacity)
        {
            var cache=new LodGeometryCache(Proxy(),capacity);
            Assert.False(cache.Record(TileId.FromFaceLevelUV(0,7,64,64),Patch(1)));
            Assert.Equal(0,cache.ActualCount); Assert.Equal(0,cache.BoundsCount); Assert.Equal(0,cache.Revision);
            Assert.Equal(1,cache.RejectedFeedback);
        }

        [Fact]
        public void SourceIdentityCannotCrossWorlds()
        {
            var proxy=Proxy(); var geometry=new LodGeometryCache(proxy);
            Assert.Throws<ArgumentException>(()=>new LodTerrainEvaluationCache(View(),Proxy(),geometry:geometry));
        }

        [Fact]
        public void OversizedActualLeafIsSplitOnNextProgressiveCut()
        {
            var proxy=Proxy(); var view=View(130); var cache=new LodTerrainEvaluationCache(view,proxy);
            var tile=TileId.FromFaceLevelUV(0,6,32,32);
            var previous=new HashSet<TileId>(Enumerable.Range(0,4).Select(i=>tile.Parent().Child(i)));
            HashSet<TileId> Cut()=>AdaptiveQuadTree.BuildCut(130,0,0,100,previous,3,7,.12,.08,
                -1,0,0,1.1,10000,traversalRootLevel:1,staticBaseLevel:3,terrainProxy:proxy,
                work:new LodSelectionWork(view,1000,evaluationCache:cache));
            var before=Cut();
            cache.Geometry.Record(tile,Patch(15));
            var after=Cut();
            Assert.DoesNotContain(tile,after);
            Assert.Contains(after,t=>t.Level>tile.Level && IsDescendant(t,tile));
            Assert.False(before.SetEquals(after));
        }

        private static bool IsDescendant(TileId tile,TileId ancestor)
        { while(tile.Level>ancestor.Level) tile=tile.Parent(); return tile.Equals(ancestor); }

        [Fact]
        public void IdenticalFeedbackDoesNotAllocateOrChangeRevision()
        {
            var cache=new LodGeometryCache(Proxy());
            var tile=TileId.FromFaceLevelUV(0,5,16,16); var quad=Patch(1);
            cache.Record(tile,quad); int revision=cache.Revision;
            long allocated=-1; bool changed=false; Exception? failure=null;
            var thread=new System.Threading.Thread(()=>
            {
                try
                {
                    cache.Record(tile,quad);
                    long before=GC.GetAllocatedBytesForCurrentThread();
                    for(int i=0;i<1000;i++) changed|=cache.Record(tile,quad);
                    allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                }
                catch(Exception error) { failure=error; }
            });
            thread.Start(); thread.Join();
            Assert.Null(failure); Assert.False(changed); Assert.Equal(revision,cache.Revision);
            Assert.Equal(0,allocated);
        }

        [Fact]
        public void NewDefaultsSplitVisibleBaseBeforePreviousTenPixelOnset()
        {
            var proxy=Proxy(); var view=View(2200);
            var tile=TileId.FromFaceLevelUV(0,3,0,0);
            Assert.True(view.EvaluateTerrain(proxy,tile,out double error));
            Assert.InRange(583/Math.Tan(Math.PI/6)*Math.Tan(error),7.001,9.999);
            HashSet<TileId> Cut(double pixels,double initial)
            {
                double split=AdaptiveViewState.AngularRadiusForPixelDiameter(pixels,Math.PI/3,583);
                double merge=AdaptiveViewState.MergeThreshold(split,1.5);
                double scale=AdaptiveViewState.EarlierBaseSplitScale(split,merge,
                    AdaptiveViewState.AngularRadiusForPixelDiameter(initial,Math.PI/3,583));
                return AdaptiveQuadTree.BuildCut(2200,0,0,100,Array.Empty<TileId>(),3,7,split,merge,
                    maxLeafCount:10000,traversalRootLevel:1,staticBaseLevel:3,baseSplitScale:scale,
                    terrainProxy:proxy,work:new LodSelectionWork(view,1000));
            }
            Assert.DoesNotContain(Cut(12,10),t=>IsDescendant(t,tile));
            Assert.Contains(Cut(8,7),t=>t.Level>3 && IsDescendant(t,tile));
        }

        [Fact]
        public void LoggedOversizedShoreQuadCannotRemainBehindSmallProxy()
        {
            // PerfLog_20260912_140221, 14:05:40.291: tényleges emissziós sarkok.
            var quad=new SurfaceQuad(
                new(51.2711067199707,-60.202106475830078,61.698127746582031),
                new(51.753681182861328,-60.390823364257813,61.891532897949219),
                new(51.8567008972168,-60.140777587890625,62.014766693115234),
                new(50.439212799072266,-58.862873077392578,60.697048187255859));
            var camera=new System.Numerics.Vector3(52.594116f,-84.707642f,78.634064f);
            var forward=-System.Numerics.Vector3.Normalize(camera);
            var right=System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(forward,System.Numerics.Vector3.UnitZ));
            var up=System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(right,forward));
            SurfacePoint P(System.Numerics.Vector3 p)=>new(p.X,p.Y,p.Z);
            var view=new ProjectedLodView(P(camera),P(right),P(up),P(forward),Math.PI/3,957.0/583,.01,10000);
            var proxy=Proxy(); var cache=new LodTerrainEvaluationCache(view,proxy);
            var tile=TileId.FromFaceLevelUV(4,9,482,4);
            Assert.True(view.EvaluateQuad(quad,out double actual));
            double diameter=2*(583/(2*Math.Tan(Math.PI/6)))*Math.Tan(actual);
            // A log kameratengelyei nem állnak rendelkezésre: közelítő, középre néző kamera, nem pixelpontos replay.
            Assert.InRange(diameter,24,30);
            cache.EvaluateTerrain(proxy,tile,out double before,out _);
            Assert.True(cache.Geometry.Record(tile,quad));
            Assert.True(cache.EvaluateTerrain(proxy,tile,out double after,out _));
            Assert.True(after>=actual); Assert.True(after>before*2);
        }
    }
}
