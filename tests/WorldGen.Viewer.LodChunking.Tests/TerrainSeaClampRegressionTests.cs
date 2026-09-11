using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class TerrainSeaClampRegressionTests
{
    // ND-77 élő log: PerfLog_20260911_212059.txt, 21:21:17 és 21:21:53.
    // Feltöltött testkoordináták, nem új Core-orákulum vagy kézzel átírt vektor.
    public static IEnumerable<object[]> LoggedQuads()
    {
        yield return new object[]{140u,84u,new SurfacePoint(0,-150.575256,54.804909),new[]{
            new SurfacePoint(6.6648330688476563,-90.3529281616211,25.004024505615234),
            new SurfacePoint(7.8386950492858887,-98.061386108398438,27.137239456176758),
            new SurfacePoint(7.8365707397460938,-98.0348129272461,26.483375549316406),
            new SurfacePoint(6.6720871925354,-90.451286315917969,24.43474006652832)},28.816,9.194};
        yield return new object[]{158u,40u,new SurfacePoint(30.437628,-161.051575,57.136280),new[]{
            new SurfacePoint(16.108192443847656,-86.5169448852539,51.85626220703125),
            new SurfacePoint(16.617776870727539,-86.307563781738281,51.730766296386719),
            new SurfacePoint(16.676239013671875,-86.6112060546875,51.193038940429688),
            new SurfacePoint(14.869016647338867,-79.8613510131836,47.203414916992188)},31.678,7.176};
    }
    private const double Sea=101.64804874504665;
    private static double Length(SurfacePoint p)=>Math.Sqrt(p.X*p.X+p.Y*p.Y+p.Z*p.Z);
    private static SurfacePoint Unit(SurfacePoint p)=>p*(1/Length(p));
    private static double Dot(SurfacePoint a,SurfacePoint b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static SurfacePoint Cross(SurfacePoint a,SurfacePoint b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    private static ProjectedLodView View(SurfacePoint camera)
    {
        var forward=Unit(camera)*-1;
        var right=Unit(Cross(forward,new(0,0,1)));
        return new(camera,right,Cross(right,forward),forward,Math.PI/3,1238.0/688,.01,10000);
    }
    private static double[] Grid(uint u,uint v,SurfacePoint[] points)
    {
        var radii=Enumerable.Repeat(Sea,6*257*257).ToArray();
        int i=3*257*257+(int)u*257+(int)v;
        radii[i]=Length(points[0]); radii[i+257]=Length(points[1]);
        radii[i+258]=Length(points[2]); radii[i+1]=Length(points[3]);
        return radii;
    }
    private static double Pixels(ProjectedLodView view,TerrainLodProxy proxy,TileId tile)
    { Assert.True(view.EvaluateTerrain(proxy,tile,out double error)); return 688/Math.Tan(Math.PI/6)*Math.Tan(error); }

    [Theory, MemberData(nameof(LoggedQuads))]
    public void RawAndClampedCornersReproduceTheLoggedMetricDifference(
        uint u,uint v,SurfacePoint camera,SurfacePoint[] points,double drawn,double underestimated)
    {
        var tile=TileId.FromFaceLevelUV(3,8,u,v);
        var radii=Grid(u,v,points);
        // Csak bizonyító kísérlet: a clampet semlegesítő sea-paraméter
        // NEM az éles viewer beállítása, nem elfogadott finomításjavítás.
        var raw=new TerrainLodProxy(8,radii,radii.Min()*.999);
        var clamped=new TerrainLodProxy(8,radii.Select(r=>Math.Max(Sea,r)).ToArray(),Sea);
        var view=View(camera);
        Assert.InRange(Math.Abs(Pixels(view,raw,tile)-drawn),0,.002);
        Assert.InRange(Math.Abs(Pixels(view,clamped,tile)-underestimated),0,.002);
        Assert.True(Pixels(view,raw,tile)>10);
        Assert.True(Pixels(view,clamped,tile)<10);
        // Független vetítés: a kamera irányára merőleges, fókusztávolsággal
        // normalizált síkbeli vektorok közti távolság. Nem hívja a LOD-metrikát.
        var forward=Unit(camera)*-1;
        var projected=points.Select(p=>{var offset=p+camera*-1;return offset*(1/Dot(offset,forward))+forward*-1;}).ToArray();
        double expected=projected.Max(a=>projected.Max(b=>Length(a+b*-1)))*344/Math.Tan(Math.PI/6);
        Assert.InRange(Math.Abs(Pixels(view,raw,tile)-expected),0,.002);
    }

}
