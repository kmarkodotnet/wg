using System;
using WorldGen.Viewer;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    public class OrbitSurfaceMathTests
    {
        [Theory]
        [InlineData(100.1, 100, 0.01, 0.1)]
        [InlineData(100.1, 100, 0.3, 0.33)]
        [InlineData(99, 100, 0, 0.001)]
        public void ClearancePreservesLegacyGapAndNearPlane(double minimum, double radius, double near, double expected)
            => Assert.Equal(expected, OrbitSurfaceMath.MinimumClearance(minimum, radius, near), 10);

        [Theory]
        [InlineData(100.1, 105, 0.1, 800, 105.1)]
        [InlineData(100.1, 100.02, 0.1, 800, 100.12)]
        [InlineData(300, 105, 0.1, 800, 300)]
        [InlineData(900, 105, 0.1, 800, 800)]
        [InlineData(100, 900, 0.1, 800, 900.1)]
        public void DistanceRespectsMountainsWaterAndConflictingMaximum(double requested, double radius,
            double clearance, double maximum, double expected)
            => Assert.Equal(expected, OrbitSurfaceMath.ClampDistance(requested, radius, clearance, maximum), 10);

        [Theory]
        [InlineData(100)]
        [InlineData(105)]
        [InlineData(200)]
        public void ZoomIsReversibleAndRelativeToLocalSurface(double radius)
        {
            double close = OrbitSurfaceMath.ZoomDistance(radius + 10, radius, 0.1, 2);
            Assert.Equal(10 * Math.Exp(-0.2), close - radius, 10);
            Assert.Equal(radius + 10, OrbitSurfaceMath.ZoomDistance(close, radius, -0.1, 2), 10);
        }

        [Fact]
        public void RepeatedZoomCannotCrossSurfaceAndCanLeaveMinimum()
        {
            double distance = 300;
            for (int i = 0; i < 100; i++)
                distance = OrbitSurfaceMath.ClampDistance(
                    OrbitSurfaceMath.ZoomDistance(distance, 105, 0.1, 2), 105, 0.1, 800);
            Assert.Equal(105.1, distance, 10);
            Assert.True(OrbitSurfaceMath.ZoomDistance(distance, 105, -0.1, 2) > distance);
        }
    }
}
