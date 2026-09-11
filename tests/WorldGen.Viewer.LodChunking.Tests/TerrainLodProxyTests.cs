using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class TerrainLodProxyTests
{
    private static double[] Radii(int level, Func<int, int, int, double> sample)
    {
        int side = (1 << level) + 1;
        var values = new double[6 * side * side];
        for (int face = 0; face < 6; face++)
            for (int u = 0; u < side; u++)
                for (int v = 0; v < side; v++)
                    values[face * side * side + u * side + v] = sample(face, u, v);
        return values;
    }

    [Fact]
    public void BilinearFieldIsExactAtTileCentersAcrossAllFacesAndLevels()
    {
        var proxy = new TerrainLodProxy(3, Radii(3, (f,u,v) => 100+f+u*.2+v*.3+u*v*.01), 90);
        foreach (int level in new[] { 3,4,8,20,28 })
            for (int face=0;face<6;face++)
            {
                uint n=1u<<level, u=n/3, v=n-1;
                double x=(u+.5)*8/n, y=(v+.5)*8/n;
                Assert.Equal(100+face+x*.2+y*.3+x*y*.01,
                    proxy.RadiusAt(TileId.FromFaceLevelUV(face,level,u,v)),10);
            }
    }

    [Fact]
    public void HierarchyPreservesInteriorSamplePeaksAndSeaFloor()
    {
        var values=Radii(3,(f,u,v)=>f==2&&u==3&&v==5?107:98);
        var proxy=new TerrainLodProxy(3,values,101);
        Assert.Equal(107,proxy.RadiusAt(TileId.FromFaceLevelUV(2,0,0,0)));
        Assert.Equal(101,proxy.RadiusAt(TileId.FromFaceLevelUV(1,0,0,0)));
        Assert.Equal(101,proxy.RadiusAt(TileId.FromFaceLevelUV(1,20,100,100)));
        Array.Fill(values,200.0);
        Assert.Equal(107,proxy.RadiusAt(TileId.FromFaceLevelUV(2,0,0,0)));
    }

    [Fact]
    public void ConstantSphereUsesDisplacedCenterAndFootprintForMorph()
    {
        var proxy=new TerrainLodProxy(2,Radii(2,(_,_,_)=>103),90);
        var tile=TileId.FromFaceLevelUV(0,10,512,512);
        proxy.GetMetric(tile,100,out double x,out double y,out double z,out double r);
        AdaptiveQuadTree.GetCenterAndBoundingRadius(tile,103,out double ex,out double ey,out double ez,out double er);
        Assert.Equal(ex,x,10); Assert.Equal(ey,y,10); Assert.Equal(ez,z,10); Assert.Equal(er,r,10);
        double distance=.5, threshold=.01;
        double splitDistance=r/Math.Tan(threshold);
        Assert.InRange(AdaptiveViewState.GeomorphAlpha(distance,splitDistance,.35),0,1);
    }

    private static HashSet<TileId> Cut(TerrainLodProxy? proxy,double distance,IReadOnlyCollection<TileId>? previous=null,int budget=25000)
        => AdaptiveQuadTree.BuildCut(distance,0,0,100,previous!,8,20,.01,.01/1.5,
            -1,0,0,1.1,budget,traversalRootLevel:3,staticBaseLevel:8,terrainProxy:proxy);

    private static int Nadir(HashSet<TileId> cut)
    {
        TileId tile=TileGeometry.FromPosition(1,0,0,20);
        while(tile.Level>8) { if(cut.Contains(tile)) return tile.Level; tile=tile.Parent(); }
        return 8;
    }

    [Fact]
    public void RaisedSurfaceRefinesDeeperWithoutNewSamplingAndReturnsToBase()
    {
        var proxy=new TerrainLodProxy(8,Radii(8,(_,_,_)=>103),100);
        Assert.True(Nadir(Cut(proxy,103.1))>=Nadir(Cut(null,103.1))+3);
        HashSet<TileId>? previous=null;
        foreach(double d in new[]{200,150,120,104,103.1,104,120,150,200})
        {
            previous=Cut(proxy,d,previous);
            Assert.InRange(previous.Count,0,25000);
        }
        Assert.Empty(previous!);
        Assert.True(proxy.StorageBytes<8*1024*1024);
    }

    [Fact]
    public void ReferenceSpherePreservesExistingCutExactly()
    {
        var proxy=new TerrainLodProxy(8,Radii(8,(_,_,_)=>100),100);
        foreach(double d in new[]{150,110,103.1}) Assert.True(Cut(null,d).SetEquals(Cut(proxy,d)));
    }

    [Fact]
    public void SnapshotCanBeReadConcurrentlyAndHistoryOrderDoesNotMatter()
    {
        var proxy=new TerrainLodProxy(8,Radii(8,(f,u,v)=>102+f*.01+u*.0001),100);
        var previous=Cut(proxy,110);
        var expected=Cut(proxy,105,previous);
        Parallel.For(0,4,_=>Assert.True(expected.SetEquals(Cut(proxy,105,previous.Reverse().ToArray()))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRadiusIsRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>new TerrainLodProxy(0,Radii(0,(_,_,_)=>value),100));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new TerrainLodProxy(0,Radii(0,(_,_,_)=>100),value));
    }

    [Fact]
    public void UnsupportedGridAndMismatchedIntegrationAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>new TerrainLodProxy(9,Array.Empty<double>(),100));
        Assert.Throws<ArgumentException>(()=>new TerrainLodProxy(2,new double[5],100));
        var proxy=new TerrainLodProxy(2,Radii(2,(_,_,_)=>100),100);
        Assert.Throws<ArgumentException>(()=>Cut(proxy,110));
        Assert.Throws<ArgumentException>(()=>AdaptiveQuadTree.BuildCut(110,0,0,100,null!,2,10,
            staticBaseLevel:2,terrainProxy:proxy,surfaceBounds:_=>new SurfaceLodBounds(100,0,0,1)));
    }

    [Fact]
    public void ZeroLevelGridAndDifferentBuildSnapshotsRemainIndependent()
    {
        var flat = new TerrainLodProxy(0,Radii(0,(_,_,_)=>100),100);
        var raised = new TerrainLodProxy(0,Radii(0,(_,_,_)=>104),101);
        foreach(int level in new[]{0,1,20,28})
        {
            var tile=TileId.FromFaceLevelUV(5,level,0,0);
            Assert.Equal(100,flat.RadiusAt(tile));
            Assert.Equal(104,raised.RadiusAt(tile));
        }
    }

    [Fact]
    public void NeighboringFineSamplesApproachSameBaseCellBoundary()
    {
        var proxy=new TerrainLodProxy(2,Radii(2,(f,u,v)=>100+u*u+v*.2),90);
        // A bilineáris proxy cellahatáron folytonos; az ellentétes oldalról
        // tartó pontok eltérése a mintatávolsággal nullához tart.
        double previous=double.PositiveInfinity;
        foreach(int level in new[]{4,8,16,28})
        {
            uint boundary=1u<<(level-2);
            var left=TileId.FromFaceLevelUV(0,level,boundary-1,boundary);
            var right=TileId.FromFaceLevelUV(0,level,boundary,boundary);
            double difference=Math.Abs(proxy.RadiusAt(left)-proxy.RadiusAt(right));
            Assert.True(difference<previous);
            previous=difference;
        }
        Assert.True(previous<1e-6);
    }
}
