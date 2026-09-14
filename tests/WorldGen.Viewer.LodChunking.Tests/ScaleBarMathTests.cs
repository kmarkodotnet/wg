using System;
using Xunit;

namespace WorldGen.Viewer
{
    public sealed class ScaleBarMathTests
    {
        [Fact]
        public void SphereIntersectionReturnsNearSurfaceDirection()
        {
            var origin = new ScaleBarMath.Vector3d(0.0, 0.0, -3.0);
            var ray = new ScaleBarMath.Vector3d(0.0, 0.0, 1.0);

            Assert.True(ScaleBarMath.TryIntersectSphere(origin, ray, 1.0, out ScaleBarMath.Vector3d hit));
            Assert.Equal(0.0, hit.X, 12);
            Assert.Equal(0.0, hit.Y, 12);
            Assert.Equal(-1.0, hit.Z, 12);
        }

        [Fact]
        public void SphereIntersectionRejectsSkyAndInsideCamera()
        {
            var outside = new ScaleBarMath.Vector3d(0.0, 0.0, -3.0);
            var away = new ScaleBarMath.Vector3d(0.0, 0.0, -1.0);
            var inside = new ScaleBarMath.Vector3d(0.0, 0.0, 0.5);

            Assert.False(ScaleBarMath.TryIntersectSphere(outside, away, 1.0, out _));
            Assert.False(ScaleBarMath.TryIntersectSphere(inside, away, 1.0, out _));
        }

        [Fact]
        public void ConstantRadialSurfaceMatchesSphere()
        {
            var origin = new ScaleBarMath.Vector3d(0.0, 0.0, -3.0);
            var ray = new ScaleBarMath.Vector3d(0.2, 0.0, 1.0);

            Assert.True(ScaleBarMath.TryIntersectRadialSurface(
                origin, ray, 1.0, ConstantRadius, out ScaleBarMath.Vector3d hit));
            Assert.Equal(1.0, hit.MagnitudeSquared, 11);
        }

        [Fact]
        public void DirectionDependentRadialSurfaceConverges()
        {
            var origin = new ScaleBarMath.Vector3d(0.0, 0.0, -150.0);
            var ray = new ScaleBarMath.Vector3d(0.32, 0.0, 1.0);

            bool hit = ScaleBarMath.TryIntersectRadialSurface(
                origin, ray, 100.0, DirectionDependentRadius,
                out ScaleBarMath.Vector3d surfaceDirection);

            Assert.True(hit);
            Assert.Equal(1.0, surfaceDirection.MagnitudeSquared, 9);
        }

        [Fact]
        public void NearSurfaceCameraUsesBracketedIntersectionWhenIntermediateSphereContainsCamera()
        {
            var origin = new ScaleBarMath.Vector3d(0.0, 0.0, -100.01);
            var ray = new ScaleBarMath.Vector3d(0.1, 0.0, 1.0);

            bool hit = ScaleBarMath.TryIntersectRadialSurface(
                origin, ray, 100.0, SteepNearbyRelief,
                out ScaleBarMath.Vector3d surfaceDirection);

            Assert.True(hit);
            Assert.True(surfaceDirection.X > 0.0);
            Assert.Equal(1.0, surfaceDirection.MagnitudeSquared, 9);
        }

        [Fact]
        public void GreatCircleUsesPhysicalRadius()
        {
            var x = new ScaleBarMath.Vector3d(1.0, 0.0, 0.0);
            var y = new ScaleBarMath.Vector3d(0.0, 1.0, 0.0);

            double distance = ScaleBarMath.GreatCircleDistanceMeters(x, y, 1000.0);

            Assert.Equal(Math.PI * 500.0, distance, 10);
        }

        [Theory]
        [InlineData(7800.0, 5000.0)]
        [InlineData(4999.0, 2000.0)]
        [InlineData(2.1, 2.0)]
        [InlineData(0.8, 0.5)]
        public void NiceDistanceUsesOneTwoFiveSeries(double maximum, double expected)
        {
            Assert.Equal(expected, ScaleBarMath.NiceDistanceAtOrBelow(maximum), 12);
        }

        [Fact]
        public void PixelWidthSolverHandlesNonLinearProjection()
        {
            Assert.True(ScaleBarMath.TrySolvePixelWidth(
                SquaredDistance, 50.0, 400.0, out double width, out double distance));

            Assert.InRange(width, 19.9999, 20.0001);
            Assert.InRange(distance, 399.996, 400.004);
        }

        [Theory]
        [InlineData(1280, 720, 45.0, 101.0, 100.0, 7420000.0)]
        [InlineData(1920, 1080, 60.0, 300.0, 100.0, 7420000.0)]
        [InlineData(2560, 1080, 75.0, 800.0, 100.0, 7420000.0)]
        [InlineData(1024, 1024, 35.0, 150.0, 100.0, 6371000.0)]
        public void CenteredPerspectiveScaleSolvesAcrossCameraConfigurations(
            int width, int height, double verticalFovDegrees,
            double cameraDistance, double displayRadius, double physicalRadiusMeters)
        {
            double maximumWidth = Math.Min(180.0, width * 0.45);
            bool DistanceAtWidth(double pixelWidth, out double meters)
            {
                var origin = new ScaleBarMath.Vector3d(0.0, 0.0, -cameraDistance);
                var leftRay = PerspectiveRay(-pixelWidth * 0.5, width, height, verticalFovDegrees);
                var rightRay = PerspectiveRay(pixelWidth * 0.5, width, height, verticalFovDegrees);
                if (!ScaleBarMath.TryIntersectSphere(origin, leftRay, displayRadius, out ScaleBarMath.Vector3d left)
                    || !ScaleBarMath.TryIntersectSphere(origin, rightRay, displayRadius, out ScaleBarMath.Vector3d right))
                {
                    meters = 0.0;
                    return false;
                }

                meters = ScaleBarMath.GreatCircleDistanceMeters(left, right, physicalRadiusMeters);
                return true;
            }

            Assert.True(ScaleBarMath.TryFindMeasurableWidth(
                DistanceAtWidth, maximumWidth, 20.0,
                out double measurableWidth, out double maximumDistance));
            double targetDistance = ScaleBarMath.NiceDistanceAtOrBelow(maximumDistance);
            Assert.True(ScaleBarMath.TrySolvePixelWidth(
                DistanceAtWidth, measurableWidth, targetDistance, out double solvedWidth, out double solvedDistance));

            Assert.InRange(solvedWidth, 0.0, measurableWidth);
            Assert.InRange(
                Math.Abs(solvedDistance - targetDistance),
                0.0,
                Math.Max(0.01, targetDistance * 1e-5));
        }

        [Fact]
        public void ScaleSolverKeepsFirstRoundValueOnContinuousSurface()
        {
            Assert.True(ScaleBarMath.TrySolveScale(
                SquaredDistance, 50.0, 2500.0, 4,
                out double width, out double distance, out bool exact, out int attempts, out double error));

            Assert.True(exact);
            Assert.Equal(1, attempts);
            Assert.Equal(2000.0, distance);
            Assert.InRange(width, 44.72, 44.73);
            Assert.InRange(error, 0.0, 1e-5);
        }

        [Fact]
        public void ScaleSolverUsesSmallerRoundValueAcrossRidgeJump()
        {
            // 2026-09-13-i élő hiba: hegygerincen átugró sugár miatt a távolság
            // a 2000 km-es célnál ugrik (1500 km → 3000 km), pontos szélesség nincs.
            Assert.True(RidgeJumpDistance(180.0, out double maximumDistance));
            Assert.True(ScaleBarMath.TrySolveScale(
                RidgeJumpDistance, 180.0, maximumDistance, 4,
                out double width, out double distance, out bool exact, out int attempts, out _));

            Assert.True(exact);
            Assert.Equal(2, attempts);
            Assert.Equal(1000000.0, distance);
            Assert.InRange(width, 99.999, 100.001);
        }

        [Fact]
        public void ScaleSolverKeepsRoundLabelAndAdjustsWidthWhenNoExactSolutionExists()
        {
            Assert.True(StaircaseDistance(180.0, out double maximumDistance));
            Assert.True(ScaleBarMath.TrySolveScale(
                StaircaseDistance, 180.0, maximumDistance, 4,
                out double width, out double distance, out bool exact, out int attempts, out double error));

            Assert.False(exact);
            Assert.Equal(4, attempts);
            Assert.Equal(10000000.0, distance);
            Assert.InRange(width, 99.0, 101.0);
            Assert.InRange(error, 0.0, 1e-3);
            Assert.Equal(distance, ScaleBarMath.NiceDistanceAtOrBelow(distance), 12);
        }

        [Fact]
        public void ScaleSolverContinuesPastFailedProbe()
        {
            Assert.True(ScaleBarMath.TrySolveScale(
                FailsBetween130And140Pixels, 180.0, 1800000.0, 4,
                out double width, out double distance, out bool exact, out int attempts, out _));

            Assert.True(exact);
            Assert.Equal(1, attempts);
            Assert.Equal(1000000.0, distance);
            Assert.InRange(width, 99.999, 100.001);
        }

        [Fact]
        public void ScaleSolverRejectsInvalidInput()
        {
            Assert.False(ScaleBarMath.TrySolveScale(null!, 180.0, 1000.0, 4, out _, out _, out _, out _, out _));
            Assert.False(ScaleBarMath.TrySolveScale(SquaredDistance, 0.0, 1000.0, 4, out _, out _, out _, out _, out _));
            Assert.False(ScaleBarMath.TrySolveScale(SquaredDistance, 180.0, double.NaN, 4, out _, out _, out _, out _, out _));
        }

        private static bool RidgeJumpDistance(double width, out double distance)
        {
            distance = width < 150.0 ? width * 10000.0 : 3000000.0 + (width - 150.0) * 1000.0;
            return true;
        }

        private static bool StaircaseDistance(double width, out double distance)
        {
            distance = Math.Floor(width) * 100000.0 + 3333.0;
            return true;
        }

        private static bool FailsBetween130And140Pixels(double width, out double distance)
        {
            distance = width * 10000.0;
            return width < 130.0 || width > 140.0;
        }

        [Fact]
        public void MeasurableWidthShrinksAwayFromSky()
        {
            Assert.True(ScaleBarMath.TryFindMeasurableWidth(
                FailsAboveOneHundredPixels, 180.0, 20.0,
                out double width, out double distance));

            Assert.InRange(width, 20.0, 100.0);
            Assert.Equal(width * 10.0, distance, 10);
        }

        private static bool ConstantRadius(ScaleBarMath.Vector3d direction, out double radius)
        {
            radius = 1.0;
            return true;
        }

        private static bool DirectionDependentRadius(
            ScaleBarMath.Vector3d direction, out double radius)
        {
            radius = 100.0 + 8.0 * direction.X;
            return true;
        }

        private static bool SteepNearbyRelief(
            ScaleBarMath.Vector3d direction, out double radius)
        {
            radius = 100.0 + 2000.0 * Math.Max(0.0, direction.X);
            return true;
        }

        private static bool SquaredDistance(double width, out double distance)
        {
            distance = width * width;
            return true;
        }

        private static bool FailsAboveOneHundredPixels(double width, out double distance)
        {
            distance = width * 10.0;
            return width <= 100.0;
        }

        private static ScaleBarMath.Vector3d PerspectiveRay(
            double horizontalPixelOffset,
            int width,
            int height,
            double verticalFovDegrees)
        {
            double aspect = (double)width / height;
            double tangent = Math.Tan(verticalFovDegrees * Math.PI / 360.0);
            double normalizedX = 2.0 * horizontalPixelOffset / width;
            return new ScaleBarMath.Vector3d(normalizedX * aspect * tangent, 0.0, 1.0);
        }
    }
}
