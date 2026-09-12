using System;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;
using P = WorldGen.Viewer.Lod.RenderedTileDiagnostics.ClipPoint;

namespace WorldGen.Viewer.Lod.Tests;

public class RenderedTileDiagnosticsTests
{
    private static void Quad(RenderedTileDiagnostics m,double half=.5,double depth=0,int kind=1,int cull=2,bool reverse=false)
    {
        m.AddQuad(new P(-half,-half,depth,1),new P(half,-half,depth,1),new P(half,half,depth,1),new P(-half,half,depth,1),
            0,reverse?1:2,reverse?2:1,0,reverse?2:3,reverse?3:2,kind,0,cull);
    }
    private static RenderedTileDiagnostics.Hit Center(RenderedTileDiagnostics m)=>m.Hits[(m.Rows/2)*m.Columns+m.Columns/2];

    [Fact]
    public void ActualUploadedQuadSizeComesFromProjectionAndViewport()
    {
        var m=new RenderedTileDiagnostics(200,100,5,3);
        Quad(m);
        var hit=Center(m);
        Assert.True(hit.Found);
        Assert.Equal(100,hit.WidthPx,10); Assert.Equal(50,hit.HeightPx,10);
        Assert.Equal(Math.Sqrt(12500),hit.DiameterPx,10);
        Assert.Equal(hit.DiameterPx,hit.FullDiameterPx,10);
    }

    [Fact]
    public void CameraZoomChangesSizeEvenWithoutANewMeshOrLodCut()
    {
        var before=new RenderedTileDiagnostics(200,100,5,3);
        var after=new RenderedTileDiagnostics(200,100,5,3);
        Quad(before,.2); Quad(after,.4);
        Assert.Equal(2*Center(before).WidthPx,Center(after).WidthPx,10);
        Assert.Equal(2*Center(before).DiameterPx,Center(after).DiameterPx,10);
    }

    [Fact]
    public void ResolutionAndAspectAffectActualPixelDimensions()
    {
        var m=new RenderedTileDiagnostics(400,100,5,3);
        Quad(m);
        Assert.Equal(200,Center(m).WidthPx,10); Assert.Equal(50,Center(m).HeightPx,10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrontmostUploadedSurfaceWinsIndependentOfSubmissionOrder(bool waterFirst)
    {
        var m=new RenderedTileDiagnostics(200,100,5,3);
        if(waterFirst) Quad(m,.3,-.5,3);
        Quad(m,.5,.5,1);
        if(!waterFirst) Quad(m,.3,-.5,3);
        Assert.Equal(3,Center(m).Surface);
        Assert.Equal(60,Center(m).WidthPx,10);
    }

    [Fact]
    public void ScreenClippingDoesNotPretendOffscreenWidthWasDrawn()
    {
        var m=new RenderedTileDiagnostics(200,100,5,3);
        Quad(m,2);
        var hit=Center(m);
        Assert.Equal(200,hit.WidthPx,10); Assert.Equal(100,hit.HeightPx,10);
        Assert.Equal(Math.Sqrt(50000),hit.DiameterPx,10);
        Assert.Equal(2*hit.DiameterPx,hit.FullDiameterPx,10);
        Assert.All(m.Hits,h=>Assert.True(h.Found));
    }

    [Fact]
    public void NearPlaneCrossingIsClippedInsteadOfDroppingVisibleTriangle()
    {
        var m=new RenderedTileDiagnostics(200,100,5,3);
        m.AddQuad(new P(-.5,-.5,-2,1),new P(.5,-.5,0,1),new P(.5,.5,0,1),new P(-.5,.5,-2,1),0,2,1,0,3,2,2,0);
        var hit=Center(m);
        Assert.True(hit.Found);
        Assert.Equal(50,hit.WidthPx,9); Assert.Equal(50,hit.HeightPx,9);
        Assert.True(double.IsNaN(hit.FullDiameterPx));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    public void OutsideNearOrFarPlaneHasNoHit(double depth)
    {
        var m=new RenderedTileDiagnostics(200,100);
        Quad(m,.5,depth);
        Assert.DoesNotContain(m.Hits,h=>h.Found);
    }

    [Fact]
    public void BehindCameraAndInvalidVerticesAreNotMeasuredAsHugeTiles()
    {
        var m=new RenderedTileDiagnostics(200,100);
        m.AddQuad(new P(-.5,-.5,0,-1),new P(.5,-.5,0,-1),new P(.5,.5,0,-1),new P(-.5,.5,0,-1),0,2,1,0,3,2,1,0);
        Quad(m,double.NaN);
        Assert.DoesNotContain(m.Hits,h=>h.Found);
        Assert.Equal(1,m.InvalidQuads);
    }

    [Fact]
    public void RealTriangleWindingControlsBackfaceCulling()
    {
        var back=new RenderedTileDiagnostics(200,100);
        Quad(back,reverse:true);
        Assert.False(Center(back).Found);
        var both=new RenderedTileDiagnostics(200,100);
        Quad(both,cull:0,reverse:true);
        Assert.True(Center(both).Found);
        var front=new RenderedTileDiagnostics(200,100);
        Quad(front,cull:1);
        Assert.False(Center(front).Found);
    }

    [Fact]
    public void PerspectiveDivideUsesActualW()
    {
        var m=new RenderedTileDiagnostics(200,100);
        m.AddQuad(new P(-.5,-.5,0,2),new P(.5,-.5,0,2),new P(.5,.5,0,2),new P(-.5,.5,0,2),0,2,1,0,3,2,2,0);
        Assert.Equal(50,Center(m).WidthPx,10);
        Assert.Equal(25,Center(m).HeightPx,10);
    }

    [Fact]
    public void MeshMaskSnapshotDoesNotChangeWhenLaterCoverageChanges()
    {
        var offsets=Enumerable.Range(0,6).Select(i=>i*6).ToArray();
        var indices=Enumerable.Range(0,6).SelectMany(i=>new[]{i*4,i*4+2,i*4+1,i*4,i*4+3,i*4+2}).ToArray();
        var mask=new TerrainIndexMask(0,offsets,indices);
        var tile=TileId.FromFaceLevelUV(2,0,0,0);
        mask.SetHidden(new[]{tile});
        var snapshot=mask.CopyHiddenQuadIndices();
        mask.SetHidden(Array.Empty<TileId>());
        Assert.Contains(2,snapshot);
        Assert.Empty(mask.CopyHiddenQuadIndices());
    }

    [Fact]
    public void SpatialSamplesDistinguishFineCenterFromCoarsePeriphery()
    {
        var m=new RenderedTileDiagnostics(200,100,5,3);
        Quad(m,.9,.5,1);
        Quad(m,.2,0,2);
        Assert.Equal(2,Center(m).Surface);
        Assert.Equal(1,m.Hits[0].Surface);
        Assert.True(m.Hits[0].DiameterPx>Center(m).DiameterPx);
    }

    [Fact]
    public void ScanDoesNotAllocatePerQuad()
    {
        var m=new RenderedTileDiagnostics(200,100);
        long allocated=-1;
        // Csak a szkennelés szálát mérjük, nem a párhuzamos tesztrunner munkaszálát.
        // A Thread/closure és az assert a mérésen kívül van; a 0 byte-os feltétel marad.
        var scan=new System.Threading.Thread(()=>
        {
            Quad(m);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++) Quad(m,.5);
            allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        });
        scan.Start(); scan.Join();
        Assert.Equal(0,allocated);
    }
}
