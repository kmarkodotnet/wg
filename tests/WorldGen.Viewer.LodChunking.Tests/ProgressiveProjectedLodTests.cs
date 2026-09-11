using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class ProgressiveProjectedLodTests
{
    private static TerrainLodProxy Proxy(int level, Func<int,int,int,double>? radius = null)
    {
        int side = (1<<level)+1;
        var data = new double[6*side*side];
        for(int f=0;f<6;f++) for(int u=0;u<side;u++) for(int v=0;v<side;v++)
            data[f*side*side+u*side+v] = radius?.Invoke(f,u,v) ?? 100;
        return new TerrainLodProxy(level,data,90);
    }

    private static ProjectedLodView View(double distance = 120, double aspect = 1.8)
        => new(new SurfacePoint(distance,0,0),new SurfacePoint(0,0,1),new SurfacePoint(0,1,0),
            new SurfacePoint(-1,0,0),Math.PI/3,aspect,.01,1000);

    [Theory]
    [InlineData(50,0,0,false)]
    [InlineData(115,0,30,false)]
    [InlineData(115,30,0,false)]
    [InlineData(125,0,0,false)]
    [InlineData(110,0,0,true)]
    [InlineData(100,0,0,true)]
    public void RectangularFrustumRejectsOnlyOutsideBounds(double x,double y,double z,bool expected)
    {
        // Az első eset külön rövid far síkkal: a teszt geometriai, nincs világmodell.
        var view = new ProjectedLodView(new SurfacePoint(120,0,0),new SurfacePoint(0,0,1),
            new SurfacePoint(0,1,0),new SurfacePoint(-1,0,0),Math.PI/3,1.8,.01,40);
        Assert.Equal(expected,view.Evaluate(new SurfaceLodBounds(x,y,z,1),out _));
    }

    [Fact]
    public void NearIntersectionIsVisibleAndHasLargeError()
    {
        Assert.True(View().Evaluate(new SurfaceLodBounds(120,0,0,1),out double error));
        Assert.Equal(Math.PI*.5,error);
    }

    [Fact]
    public void OnAxisProjectedSphereMatchesTangentGeometry()
    {
        Assert.True(View().Evaluate(new SurfaceLodBounds(100,0,0,2),out double error));
        Assert.Equal(Math.Asin(2.0/20),error,12);
        Assert.True(View().Evaluate(new SurfaceLodBounds(100,0,15,2),out double edge));
        Assert.True(edge>error);
    }

    [Fact]
    public void BoundsIncludeKnownSteepBaseCornersAndShrinkWithSubdivision()
    {
        const int level=3;
        var proxy=Proxy(level,(f,u,v)=>100+u*2+v*.3);
        foreach(int face in Enumerable.Range(0,6))
        {
            TileId tile=TileId.FromFaceLevelUV(face,level,3,4);
            SurfaceLodBounds bounds=proxy.BoundsAt(tile);
            for(int du=0;du<2;du++) for(int dv=0;dv<2;dv++)
            {
                TileGeometry.PositionFromFaceUV(face,(3+du)/8.0*2-1,(4+dv)/8.0*2-1,
                    out double x,out double y,out double z);
                double radius=100+(3+du)*2+(4+dv)*.3;
                Assert.True(bounds.DistanceTo(x*radius,y*radius,z*radius)<=bounds.Radius+1e-9);
            }
            Assert.True(proxy.BoundsAt(tile.Child(0)).Radius<bounds.Radius);
        }
    }

    [Fact]
    public void CoarseBoundsIncludeInteriorSamplePeak()
    {
        var proxy=Proxy(3,(f,u,v)=>f==0&&u==3&&v==5?140:100);
        var tile=TileId.FromFaceLevelUV(0,1,0,1);
        TileGeometry.PositionFromFaceUV(0,-.25,.25,out double x,out double y,out double z);
        var bounds=proxy.BoundsAt(tile);
        Assert.True(bounds.DistanceTo(x*140,y*140,z*140)<=bounds.Radius);
    }

    private static HashSet<TileId> Cut(TerrainLodProxy proxy, HashSet<TileId>? previous, LodSelectionWork work,
        double distance=120, int budget=20000)
        => AdaptiveQuadTree.BuildCut(distance,0,0,100,previous!,proxy.BaseLevel,8,.06,.04,
            -1,0,0,1.13,budget,traversalRootLevel:1,staticBaseLevel:proxy.BaseLevel,terrainProxy:proxy,work:work);

    [Fact]
    public void SmallWorkWavesConvergeWhileCameraIsStationaryAndPreserveCoverage()
    {
        var proxy=Proxy(3);
        HashSet<TileId>? previous=null;
        var firstWork=new LodSelectionWork(View(),8);
        previous=Cut(proxy,previous,firstWork);
        Assert.Equal(8,firstWork.NewSplits);
        Assert.True(firstWork.DeferredSplits>0);
        int waves=1;
        for(;waves<300;waves++)
        {
            var work=new LodSelectionWork(View(),8);
            previous=Cut(proxy,previous,work);
            Assert.InRange(work.NewSplits,0,8);
            var coverage=LodCoverage.Complete(previous,3);
            foreach(var root in coverage.Roots)
            {
                double area=coverage.Leaves.Where(t=>Ancestor(t,3)==root).Sum(t=>Math.Pow(.25,t.Level-3));
                Assert.Equal(1,area,12);
            }
            if(work.DeferredSplits==0) break;
        }
        Assert.InRange(waves,1,299);
        // A hiszterézis miatt az előzmény nélküli cut nem kötelezően azonos;
        // ugyanebből az előzményből a korlátlan kérés viszont már nem változtathat.
        Assert.True(previous.SetEquals(Cut(proxy,previous,new LodSelectionWork(View()))));
        Assert.Empty(Cut(proxy,previous,new LodSelectionWork(View(800),8),800));
    }

    private static TileId Ancestor(TileId tile,int level)
    { while(tile.Level>level) tile=tile.Parent(); return tile; }

    [Fact]
    public void SteepVisibleQuadIsNotMistakenForSmallFlatFootprint()
    {
        var proxy=Proxy(8,(f,u,v)=>f==0&&u==200&&v==128?125:100);
        TileId tile=TileId.FromFaceLevelUV(0,8,199,127);
        var view=View(159.34);
        Assert.True(view.EvaluateTerrain(proxy,tile,out double projected));
        proxy.GetMetric(tile,100,out double x,out double y,out double z,out double footprint);
        double old=Math.Atan2(footprint,Math.Sqrt((159.34-x)*(159.34-x)+y*y+z*z));
        Assert.True(projected>old*3);
        var work=new LodSelectionWork(view);
        var cut=AdaptiveQuadTree.BuildCut(159.34,0,0,100,null!,8,16,.01,.0067,-1,0,0,1.13,20000,
            traversalRootLevel:3,staticBaseLevel:8,terrainProxy:proxy,work:work);
        Assert.Contains(cut,t=>t.Level>8&&Ancestor(t,8)==tile);
        Assert.InRange(cut.Count,1,20000);
    }

    [Fact]
    public void ProjectedQuadErrorMatchesIndependentCornerProjection()
    {
        var proxy=Proxy(3,(f,u,v)=>100+u*.1+v*.3);
        var tile=TileId.FromFaceLevelUV(0,5,17,16);
        var quad=proxy.QuadAt(tile);
        var points=new[]{quad.P00,quad.P10,quad.P11,quad.P01};
        double diameter=0;
        foreach(var a in points) foreach(var b in points)
        {
            double x=a.Z/(120-a.X)-b.Z/(120-b.X), y=a.Y/(120-a.X)-b.Y/(120-b.X);
            diameter=Math.Max(diameter,Math.Sqrt(x*x+y*y));
        }
        Assert.True(View().EvaluateTerrain(proxy,tile,out double error));
        Assert.Equal(Math.Atan(diameter*.5),error,12);
    }

    [Fact]
    public void StrictBalanceHonorsBudgetAndEnumerationOrder()
    {
        var coarse=TileId.FromFaceLevelUV(0,2,1,1);
        var fine=TileId.FromFaceLevelUV(0,5,16,12);
        var cut=new HashSet<TileId>{coarse,fine};
        AdaptiveQuadTree.EnforceRestrictedBalance(cut,2,2,strictBudget:true);
        Assert.Equal(2,cut.Count);
        var other=new HashSet<TileId>{fine,coarse};
        AdaptiveQuadTree.EnforceRestrictedBalance(other,2,2,strictBudget:true);
        Assert.True(cut.SetEquals(other));
    }

    [Fact]
    public void BudgetExhaustionDoesNotRequestEndlessContinuation()
    {
        var proxy=Proxy(3);
        var work=new LodSelectionWork(View(),100000);
        var cut=Cut(proxy,null,work,budget:16);
        Assert.InRange(cut.Count,0,16);
        Assert.Equal(0,work.DeferredSplits);
    }

    [Fact]
    public void CancelledSelectionPublishesNoResultAndKeepsPreviousCut()
    {
        var proxy=Proxy(3);
        var previous=Cut(proxy,null,new LodSelectionWork(View(),8));
        var copy=previous.ToHashSet();
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>Cut(proxy,previous,new LodSelectionWork(View(),8,cancellation.Token)));
        Assert.True(previous.SetEquals(copy));
    }

    [Fact]
    public void WorkOrderIsIndependentOfPreviousHashSetEnumeration()
    {
        var proxy=Proxy(3);
        var previous=Cut(proxy,null,new LodSelectionWork(View(),8));
        var a=Cut(proxy,previous,new LodSelectionWork(View(),8));
        var b=Cut(proxy,previous.Reverse().ToHashSet(),new LodSelectionWork(View(),8));
        Assert.True(a.SetEquals(b));
    }

    private static AdaptiveViewState State(double distance) => new(distance,0,0,-1,0,0,Math.PI/3,1.8,1238,688);

    [Fact]
    public void SupersedingIsAltitudeRelativeAndCannotStarvePublishing()
    {
        var old=State(110); var current=State(106);
        Assert.True(LodRequestPolicy.ShouldSupersede(old,current,7,.5,false));
        Assert.False(LodRequestPolicy.ShouldSupersede(old,current,7,.5,true));
        Assert.False(LodRequestPolicy.ShouldSupersede(old,current,7,.01,false));
        Assert.False(LodRequestPolicy.ShouldSupersede(old,old,7,.5,false));
        Assert.False(LodRequestPolicy.ShouldSupersede(old,current,100,.5,false));
    }
}
