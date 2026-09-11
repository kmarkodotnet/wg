using System;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class AdaptiveViewStateTests
{
    private static AdaptiveViewState View(double z = 100.6, double fx = 0,
        double fov = Math.PI / 3, double aspect = 16.0 / 9,
        int width = 1920, int height = 1080)
        => new(0, 0, z, fx, 0, -1, fov, aspect, width, height);

    [Fact]
    public void StationaryViewDoesNotScheduleRepeatedWork()
        => Assert.False(View().NeedsRefresh(View(), 0, 100));

    [Fact]
    public void SubthresholdZoomEventuallyRefreshesEvenAfterCameraStops()
    {
        Assert.False(View(100.59).NeedsRefresh(View(), 1, 0.1));
        Assert.True(View(100.59).NeedsRefresh(View(), 1, 0.25));
    }

    [Fact]
    public void LargeMotionDoesNotWaitForTrailingRefresh()
        => Assert.True(View(99.5).NeedsRefresh(View(), 1, 0.1));

    [Fact]
    public void ViewChangedDuringWorkerStillNeedsFollowup()
    {
        AdaptiveViewState requested = View();
        AdaptiveViewState now = View(100.59);
        Assert.True(now.NeedsRefresh(requested, 1, 0.5));
        Assert.False(now.NeedsRefresh(now, 1, 0.5));
    }

    [Theory]
    [InlineData(0.1, Math.PI / 3, 16.0 / 9, 1920, 1080)]
    [InlineData(0, Math.PI / 4, 16.0 / 9, 1920, 1080)]
    [InlineData(0, Math.PI / 3, 2, 1920, 1080)]
    [InlineData(0, Math.PI / 3, 16.0 / 9, 1280, 1080)]
    [InlineData(0, Math.PI / 3, 16.0 / 9, 1920, 720)]
    public void OrientationAndProjectionChangesRefreshWithoutTranslation(
        double fx, double fov, double aspect, int width, int height)
        => Assert.True(View(fx: fx, fov: fov, aspect: aspect, width: width, height: height)
            .NeedsRefresh(View(), 1, 0.1));

    [Theory]
    [InlineData(30, 720)]
    [InlineData(60, 1080)]
    [InlineData(100, 2160)]
    public void PixelThresholdRoundTripsThroughPerspectiveProjection(double fovDegrees, int height)
    {
        double fov = fovDegrees * Math.PI / 180;
        double angle = AdaptiveViewState.AngularRadiusForPixelDiameter(12, fov, height);
        double projectedDiameter = height * Math.Tan(angle) / Math.Tan(fov / 2);
        Assert.Equal(12, projectedDiameter, 10);
    }
}

public class OceanRefinementTests
{
    [Fact]
    public void FullySubmergedSamplesCanStillBeSkipped()
        => Assert.True(OceanRefinement.CanSkip(true, 100, 90, 90, 90, 90));

    [Theory]
    [InlineData(101, 90, 90, 90)]
    [InlineData(90, 101, 90, 90)]
    [InlineData(90, 90, 101, 90)]
    [InlineData(90, 90, 90, 101)]
    [InlineData(100, 90, 90, 90)]
    [InlineData(double.NaN, 90, 90, 90)]
    public void CoastalOrUncertainCornerCannotDisableRefinement(double a, double b, double c, double d)
        => Assert.False(OceanRefinement.CanSkip(true, 100, a, b, c, d));

    [Fact]
    public void LandCenterAlwaysRefinesEvenWithSubmergedCorners()
        => Assert.False(OceanRefinement.CanSkip(false, 100, 90, 90, 90, 90));
}
