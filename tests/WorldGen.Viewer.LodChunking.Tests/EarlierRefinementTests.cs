using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class EarlierRefinementTests
{
    private static readonly double Length=Math.Sqrt(31.581*31.581+127.684*127.684+49.049*49.049);
    private static readonly double X=31.581/Length, Y=-127.684/Length, Z=49.049/Length;
    private static readonly double Split=AdaptiveViewState.AngularRadiusForPixelDiameter(12,1.047197543,688);
    private static readonly double Scale=AdaptiveViewState.EarlierBaseSplitScale(Split,Split/1.5,
        AdaptiveViewState.AngularRadiusForPixelDiameter(10,1.047197543,688));

    private static HashSet<TileId> Cut(double distance, bool early, IReadOnlyCollection<TileId>? previous=null)
        => AdaptiveQuadTree.BuildCut(X*distance,Y*distance,Z*distance,100,previous!,8,20,
            Split,Split/1.5,-X,-Y,-Z,1.132737064,200000,traversalRootLevel:3,staticBaseLevel:8,
            baseSplitScale:early ? Scale : 1);

    private static TileId Nadir(HashSet<TileId> cut)
    {
        TileId id=TileGeometry.FromPosition(X,Y,Z,20);
        while(id.Level>8) { if(cut.Contains(id)) return id; id=id.Parent(); }
        return id;
    }

    [Fact]
    public void FirstSplitStartsEarlierWithoutIncreasingDeepLevelTarget()
    {
        Assert.Equal(8,Nadir(Cut(149.319,false)).Level);
        Assert.Equal(9,Nadir(Cut(149.319,true)).Level);
        foreach(double d in new[]{108.152,104.474,103.663})
            Assert.True(Cut(d,false).SetEquals(Cut(d,true)));
    }

    [Fact]
    public void RoundTripPreservesBudgetAndReturnsToStaticBase()
    {
        double[] distances={173.576,160.239,149.319,140.379,133.060,122.161,114.855,108.152,104.474,103.663};
        HashSet<TileId>? old=null,early=null;
        int oldPeak=0,newPeak=0;
        foreach(double d in distances.Concat(distances.AsEnumerable().Reverse().Skip(1)))
        {
            old=Cut(d,false,old); early=Cut(d,true,early);
            oldPeak=Math.Max(oldPeak,old.Count); newPeak=Math.Max(newPeak,early.Count);
            Assert.InRange(early.Count,0,200000);
        }
        Assert.Empty(early!);
        Assert.True(newPeak<=oldPeak*1.3,$"baseline peak={oldPeak}, early peak={newPeak}");
    }

    [Fact]
    public void EarlierBaseThresholdIsStableWithExplicitHistory()
    {
        var a=Cut(149.319,true);
        Assert.True(a.SetEquals(Cut(149.319,true,a)));
        Assert.True(Cut(140.379,true,a).SetEquals(Cut(140.379,true,a.Reverse().ToArray())));
    }

    [Fact]
    public void EarlierBaseMorphUsesEarlierBaseSplitDistance()
    {
        var leaf=Nadir(Cut(140.379,true));
        AdaptiveQuadTree.GetCenterAndBoundingRadius(leaf.Parent(),100,out double x,out double y,out double z,out double r);
        double distance=Math.Sqrt(Math.Pow(X*140.379-x,2)+Math.Pow(Y*140.379-y,2)+Math.Pow(Z*140.379-z,2));
        double oldAlpha=AdaptiveViewState.GeomorphAlpha(distance,r/Math.Tan(Split),.6);
        double newAlpha=AdaptiveViewState.GeomorphAlpha(distance,r/Math.Tan(Split*Scale),.35);
        Assert.InRange(oldAlpha,.10,.11);
        Assert.InRange(newAlpha,.62,.64);
    }

    [Fact]
    public void MorphRemainsContinuousAndReachesFullDetailSooner()
    {
        const double splitDistance=100;
        Assert.Equal(0,AdaptiveViewState.GeomorphAlpha(100,splitDistance,.35));
        Assert.Equal(0,AdaptiveViewState.GeomorphAlpha(110,splitDistance,.35));
        Assert.Equal(1,AdaptiveViewState.GeomorphAlpha(65,splitDistance,.35));
        double previous=0;
        for(int d=100;d>=0;d--)
        {
            double alpha=AdaptiveViewState.GeomorphAlpha(d,splitDistance,.35);
            Assert.InRange(alpha,previous,1);
            Assert.True(alpha>=AdaptiveViewState.GeomorphAlpha(d,splitDistance,.6));
            previous=alpha;
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(16)]
    public void IncompatibleInitialTargetDoesNotRemoveHysteresis(double requested)
        => Assert.Equal(1,AdaptiveViewState.EarlierBaseSplitScale(12,8,requested));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(0.5)]
    public void InvalidBaseScaleIsRejected(double scale)
        => Assert.Throws<ArgumentOutOfRangeException>(() => AdaptiveQuadTree.BuildCut(150,0,0,100,null!,
            splitThresholdRadians:.012,mergeThresholdRadians:.008,staticBaseLevel:8,baseSplitScale:scale));
}
