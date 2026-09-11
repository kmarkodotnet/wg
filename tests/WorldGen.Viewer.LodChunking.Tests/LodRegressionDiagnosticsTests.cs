using System;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class LodRegressionDiagnosticsTests
{
    [Theory]
    [InlineData(27.300, -107.027, 52.183)]
    [InlineData(36.598, -95.515, 59.121)]
    [InlineData(30.380, -86.083, 55.193)]
    public void ExplicitNullAndRestoredDefaultSelectionAreIdentical(double x, double y, double z)
    {
        double d = Math.Sqrt(x*x+y*y+z*z);
        var a = AdaptiveQuadTree.BuildCut(x,y,z,100,null!,8,20,.010070,.010070/1.5,
            -x/d,-y/d,-z/d,1.1,200000,traversalRootLevel:3,staticBaseLevel:8);
        var b = AdaptiveQuadTree.BuildCut(x,y,z,100,null!,8,20,.010070,.010070/1.5,
            -x/d,-y/d,-z/d,1.1,200000,traversalRootLevel:3,staticBaseLevel:8,surfaceBounds:null);
        Assert.True(a.SetEquals(b));
    }

    [Fact]
    public void RenderedLeafUsesFilteredCoverageNotUnfilteredCut()
    {
        TileId root=TileId.FromFaceLevelUV(0,8,100,100);
        TileId fine=root.Child(0).Child(1);
        var filtered=LodCoverage.Complete(Array.Empty<TileId>(),8);
        Assert.Equal(root,filtered.FindRenderedLeaf(fine,8));
        var complete=LodCoverage.Complete(new[]{fine},8);
        Assert.Equal(fine,complete.FindRenderedLeaf(fine.Child(3),8));
        Assert.Equal(root.Child(3),complete.FindRenderedLeaf(root.Child(3).Child(2),8));
    }

    private static SurfaceQuad Quad(double weight) => new SurfaceQuad(
        new SurfacePoint(-1,-1,weight),new SurfacePoint(1,-1,weight),
        new SurfacePoint(1,1,weight),new SurfacePoint(-1,1,weight));

    [Fact]
    public void ProjectedDiameterUsesViewportAndHomogeneousDivide()
    {
        double expected=Math.Sqrt(500*500+300*300);
        Assert.Equal(expected,AdaptiveViewState.QuadPixelDiameter(Quad(2),1000,600),10);
        Assert.Equal(expected*2,AdaptiveViewState.QuadPixelDiameter(Quad(2),2000,1200),10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidProjectionDoesNotReportAFalsePixelSize(double w)
        => Assert.True(double.IsNaN(AdaptiveViewState.QuadPixelDiameter(Quad(w),1000,600)));

    [Fact]
    public void OneInvalidCornerInvalidatesWholeQuad()
    {
        var q=Quad(2);
        Assert.True(double.IsNaN(AdaptiveViewState.QuadPixelDiameter(new SurfaceQuad(q.P00,q.P10,q.P11,
            new SurfacePoint(1,1,-1)),1000,600)));
    }

    [Fact]
    public void ZeroViewportIsInvalid()
        => Assert.True(double.IsNaN(AdaptiveViewState.QuadPixelDiameter(Quad(2),0,600)));
}
