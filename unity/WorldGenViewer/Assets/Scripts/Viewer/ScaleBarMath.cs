using System;

namespace WorldGen.Viewer
{
    /// <summary>
    /// UnityEngine-fuggetlen geometria a kamerafuggo fizikai leptekhez.
    /// A viewer es a parancssori regresszioteszt ugyanazt a forrast hasznalja.
    /// </summary>
    internal static class ScaleBarMath
    {
        internal struct Vector3d
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;

            public Vector3d(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public static Vector3d operator +(Vector3d a, Vector3d b)
                => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

            public static Vector3d operator *(Vector3d value, double scale)
                => new Vector3d(value.X * scale, value.Y * scale, value.Z * scale);

            public double MagnitudeSquared => X * X + Y * Y + Z * Z;

            public bool TryNormalize(out Vector3d normalized)
            {
                double magnitudeSquared = MagnitudeSquared;
                if (!(magnitudeSquared > 0.0) || !IsFinite(magnitudeSquared))
                {
                    normalized = default(Vector3d);
                    return false;
                }

                double inverseMagnitude = 1.0 / Math.Sqrt(magnitudeSquared);
                normalized = this * inverseMagnitude;
                return IsFinite(normalized.X) && IsFinite(normalized.Y) && IsFinite(normalized.Z);
            }

            public static double Dot(Vector3d a, Vector3d b)
                => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        internal delegate bool SurfaceRadiusProvider(Vector3d direction, out double radius);
        internal delegate bool DistanceForPixelWidth(double pixelWidth, out double distanceMeters);

        internal static bool TryIntersectSphere(
            Vector3d rayOrigin,
            Vector3d rayDirection,
            double sphereRadius,
            out Vector3d surfaceDirection)
        {
            surfaceDirection = default(Vector3d);
            if (!(sphereRadius > 0.0) || !IsFinite(sphereRadius))
                return false;

            double a = rayDirection.MagnitudeSquared;
            double c = rayOrigin.MagnitudeSquared - sphereRadius * sphereRadius;
            if (!(a > 0.0) || !IsFinite(a) || !(c > 0.0))
                return false;

            double halfB = Vector3d.Dot(rayOrigin, rayDirection);
            double discriminant = halfB * halfB - a * c;
            if (!(discriminant >= 0.0) || !IsFinite(discriminant))
                return false;

            double root = Math.Sqrt(discriminant);
            double t = (-halfB - root) / a;
            if (!(t >= 0.0))
                t = (-halfB + root) / a;
            if (!(t >= 0.0) || !IsFinite(t))
                return false;

            return (rayOrigin + rayDirection * t).TryNormalize(out surfaceDirection);
        }

        internal static bool TryIntersectRadialSurface(
            Vector3d rayOrigin,
            Vector3d rayDirection,
            double initialRadius,
            SurfaceRadiusProvider radiusProvider,
            out Vector3d surfaceDirection)
        {
            surfaceDirection = default(Vector3d);
            if (radiusProvider == null || !(initialRadius > 0.0))
                return false;

            if (TryIntersectRadialSurfaceFixedPoint(
                rayOrigin, rayDirection, initialRadius, radiusProvider, out surfaceDirection))
            {
                return true;
            }

            // Kozeli kameranal egy koztes sugar nagyobb lehet a kamera
            // kozepponttol mert tavolsaganal. Ilyenkor a segedgomb iteracioja
            // hibasan belso kameranak latja a helyzetet. A kozvetlen elojelezett
            // gyokkereses a sugar menten az elso tenyleges felszinmetszest adja.
            return TryIntersectRadialSurfaceBracketed(
                rayOrigin, rayDirection, initialRadius, radiusProvider, out surfaceDirection);
        }

        private static bool TryIntersectRadialSurfaceFixedPoint(
            Vector3d rayOrigin,
            Vector3d rayDirection,
            double initialRadius,
            SurfaceRadiusProvider radiusProvider,
            out Vector3d surfaceDirection)
        {
            surfaceDirection = default(Vector3d);

            double radius = initialRadius;
            Vector3d direction = default(Vector3d);
            for (int i = 0; i < 24; i++)
            {
                if (!TryIntersectSphere(rayOrigin, rayDirection, radius, out direction)
                    || !radiusProvider(direction, out double nextRadius)
                    || !(nextRadius > 0.0) || !IsFinite(nextRadius))
                {
                    return false;
                }

                // A renderelt halo float pontossagu; a szigorubb kuszob ervenyes
                // mereseket is villogtathatott meredek felszinperemeknel.
                double tolerance = Math.Max(1e-5, nextRadius * 1e-7);
                if (Math.Abs(nextRadius - radius) <= tolerance)
                {
                    surfaceDirection = direction;
                    return true;
                }

                // A csillapitas megelozi a mintak valtakozasat eles partvonalnal.
                radius = (radius + nextRadius) * 0.5;
            }

            // Nem konvergens part-/horizonthelyzetben inkabb nincs kijelzes,
            // mint latszolag pontos, de hamis ertek.
            return false;
        }

        private static bool TryIntersectRadialSurfaceBracketed(
            Vector3d rayOrigin,
            Vector3d rayDirection,
            double initialRadius,
            SurfaceRadiusProvider radiusProvider,
            out Vector3d surfaceDirection)
        {
            surfaceDirection = default(Vector3d);
            if (!rayDirection.TryNormalize(out Vector3d normalizedRay))
                return false;

            double closestT = -Vector3d.Dot(rayOrigin, normalizedRay);
            if (!(closestT > 0.0) || !IsFinite(closestT)
                || !TryEvaluateRadialResidual(
                    rayOrigin, normalizedRay, 0.0, radiusProvider,
                    out _, out double outsideResidual)
                || !(outsideResidual > 0.0))
            {
                return false;
            }

            double lowT = 0.0;
            double highT = 0.0;
            Vector3d highDirection = default(Vector3d);
            bool bracketed = false;
            double remainingFraction = 0.5;
            for (int i = 1; i <= 24; i++)
            {
                double fraction = 1.0 - remainingFraction;
                remainingFraction *= 0.5;
                double candidateT = closestT * fraction;
                if (!TryEvaluateRadialResidual(
                    rayOrigin, normalizedRay, candidateT, radiusProvider,
                    out Vector3d candidateDirection, out double residual))
                {
                    return false;
                }

                if (residual <= 0.0)
                {
                    highT = candidateT;
                    highDirection = candidateDirection;
                    bracketed = true;
                    break;
                }

                lowT = candidateT;
            }

            if (!bracketed)
                return false;

            double distanceTolerance = Math.Max(1e-7, initialRadius * 1e-8);
            for (int i = 0; i < 40 && highT - lowT > distanceTolerance; i++)
            {
                double middleT = (lowT + highT) * 0.5;
                if (!TryEvaluateRadialResidual(
                    rayOrigin, normalizedRay, middleT, radiusProvider,
                    out Vector3d middleDirection, out double residual))
                {
                    return false;
                }

                if (residual > 0.0)
                {
                    lowT = middleT;
                }
                else
                {
                    highT = middleT;
                    highDirection = middleDirection;
                }
            }

            surfaceDirection = highDirection;
            return true;
        }

        private static bool TryEvaluateRadialResidual(
            Vector3d rayOrigin,
            Vector3d normalizedRay,
            double distance,
            SurfaceRadiusProvider radiusProvider,
            out Vector3d direction,
            out double residual)
        {
            direction = default(Vector3d);
            residual = 0.0;
            Vector3d point = rayOrigin + normalizedRay * distance;
            if (!point.TryNormalize(out direction)
                || !radiusProvider(direction, out double surfaceRadius)
                || !(surfaceRadius > 0.0) || !IsFinite(surfaceRadius))
            {
                return false;
            }

            double pointRadius = Math.Sqrt(point.MagnitudeSquared);
            residual = pointRadius - surfaceRadius;
            return IsFinite(residual);
        }

        internal static double GreatCircleDistanceMeters(
            Vector3d firstDirection,
            Vector3d secondDirection,
            double physicalRadiusMeters)
        {
            if (!(physicalRadiusMeters > 0.0)
                || !firstDirection.TryNormalize(out Vector3d first)
                || !secondDirection.TryNormalize(out Vector3d second))
            {
                return double.NaN;
            }

            double cosine = Math.Max(-1.0, Math.Min(1.0, Vector3d.Dot(first, second)));
            return Math.Acos(cosine) * physicalRadiusMeters;
        }

        internal static double NiceDistanceAtOrBelow(double maximumMeters)
        {
            if (!(maximumMeters > 0.0) || !IsFinite(maximumMeters))
                return 0.0;

            double power = 1.0;
            while (maximumMeters >= power * 10.0) power *= 10.0;
            while (maximumMeters < power) power /= 10.0;

            double normalized = maximumMeters / power;
            if (normalized >= 5.0) return 5.0 * power;
            if (normalized >= 2.0) return 2.0 * power;
            return power;
        }

        internal static bool TrySolvePixelWidth(
            DistanceForPixelWidth distanceForWidth,
            double maximumPixelWidth,
            double targetDistanceMeters,
            out double pixelWidth,
            out double solvedDistanceMeters)
        {
            pixelWidth = 0.0;
            solvedDistanceMeters = 0.0;
            if (distanceForWidth == null || !(maximumPixelWidth > 0.0)
                || !(targetDistanceMeters > 0.0)
                || !distanceForWidth(maximumPixelWidth, out double maximumDistance)
                || maximumDistance + 1e-9 < targetDistanceMeters)
            {
                return false;
            }

            double low = 0.0;
            double high = maximumPixelWidth;
            double distance = maximumDistance;
            for (int i = 0; i < 24; i++)
            {
                double middle = (low + high) * 0.5;
                if (!distanceForWidth(middle, out distance) || !IsFinite(distance))
                    return false;

                if (distance < targetDistanceMeters) low = middle;
                else high = middle;
            }

            pixelWidth = (low + high) * 0.5;
            if (!distanceForWidth(pixelWidth, out solvedDistanceMeters))
                return false;

            double allowedError = Math.Max(0.01, targetDistanceMeters * 1e-5);
            return Math.Abs(solvedDistanceMeters - targetDistanceMeters) <= allowedError;
        }

        /// <summary>
        /// Mindig kerek (1/2/5) léptékérték, a csík szélessége igazodik hozzá.
        ///
        /// 2026-09-13-i élő hiba: 111× domborzat-túlrajzolás mellett a
        /// képernyőpontok radiális felszínmetszése hegygerincen átugorhat, ezért
        /// a távolság a pixelszélesség szerint nem folytonos, és a pontos
        /// felezéses megoldás (<see cref="TrySolvePixelWidth"/>) nem létezik; a
        /// lépték korábban üres maradt. Most legfeljebb
        /// <paramref name="roundAttempts"/> egyre kisebb kerek értéket próbálunk.
        /// Ha valamelyik pontosan megoldható, az a kimenet
        /// (<paramref name="exact"/> = true). Különben a legkisebb relatív
        /// eltérésű kerek érték és az ahhoz legközelebbi mért távolságú
        /// csíkszélesség (<paramref name="relativeError"/> a mért és a kiírt
        /// érték eltérése). A felirat (<paramref name="distanceMeters"/>) mindig
        /// a kerek érték.
        /// </summary>
        internal static bool TrySolveScale(
            DistanceForPixelWidth distanceForWidth,
            double maximumPixelWidth,
            double maximumDistanceMeters,
            int roundAttempts,
            out double pixelWidth,
            out double distanceMeters,
            out bool exact,
            out int attemptsUsed,
            out double relativeError)
        {
            pixelWidth = 0.0;
            distanceMeters = 0.0;
            exact = false;
            attemptsUsed = 0;
            relativeError = double.PositiveInfinity;
            if (distanceForWidth == null || !(maximumPixelWidth > 0.0)
                || !(maximumDistanceMeters > 0.0) || !IsFinite(maximumDistanceMeters))
            {
                return false;
            }

            double nice = NiceDistanceAtOrBelow(maximumDistanceMeters);
            for (int i = 0; i < roundAttempts && nice > 0.0; i++)
            {
                attemptsUsed++;
                if (TryBracketPixelWidth(distanceForWidth, maximumPixelWidth, maximumDistanceMeters, nice,
                        out double width, out double measured, out bool isExact))
                {
                    double error = Math.Abs(measured - nice) / nice;
                    if (isExact)
                    {
                        pixelWidth = width;
                        distanceMeters = nice;
                        exact = true;
                        relativeError = error;
                        return true;
                    }
                    if (error < relativeError)
                    {
                        pixelWidth = width;
                        distanceMeters = nice;
                        relativeError = error;
                    }
                }
                nice = NiceDistanceAtOrBelow(nice * 0.999);
            }

            return distanceMeters > 0.0 && pixelWidth > 0.0;
        }

        /// <summary>
        /// Felezés a <paramref name="targetDistanceMeters"/> körül; a sikertelen
        /// mintavétel szűkíti a felső határt, nem szakítja meg a keresést. A két
        /// végpont közül a célhoz közelebbi mért távolságú szélességet adja.
        /// </summary>
        private static bool TryBracketPixelWidth(
            DistanceForPixelWidth distanceForWidth,
            double maximumPixelWidth,
            double maximumDistanceMeters,
            double targetDistanceMeters,
            out double bestWidth,
            out double bestDistance,
            out bool exact)
        {
            bestWidth = maximumPixelWidth;
            bestDistance = maximumDistanceMeters;
            exact = false;
            if (maximumDistanceMeters + 1e-9 < targetDistanceMeters)
                return false;

            double low = 0.0, high = maximumPixelWidth;
            double lowWidth = double.NaN, lowDistance = double.NaN;
            double highWidth = maximumPixelWidth, highDistance = maximumDistanceMeters;
            for (int i = 0; i < 24; i++)
            {
                double middle = (low + high) * 0.5;
                if (!distanceForWidth(middle, out double distance) || !IsFinite(distance) || !(distance > 0.0))
                {
                    high = middle;
                    continue;
                }
                if (distance < targetDistanceMeters)
                {
                    low = middle;
                    lowWidth = middle;
                    lowDistance = distance;
                }
                else
                {
                    high = middle;
                    highWidth = middle;
                    highDistance = distance;
                }
            }

            bestWidth = highWidth;
            bestDistance = highDistance;
            if (!double.IsNaN(lowWidth)
                && Math.Abs(lowDistance - targetDistanceMeters) < Math.Abs(highDistance - targetDistanceMeters))
            {
                bestWidth = lowWidth;
                bestDistance = lowDistance;
            }
            exact = Math.Abs(bestDistance - targetDistanceMeters) <= Math.Max(0.01, targetDistanceMeters * 1e-5);
            return bestWidth > 0.0;
        }

        internal static bool TryFindMeasurableWidth(
            DistanceForPixelWidth distanceForWidth,
            double preferredPixelWidth,
            double minimumPixelWidth,
            out double measurablePixelWidth,
            out double distanceMeters)
        {
            measurablePixelWidth = preferredPixelWidth;
            distanceMeters = 0.0;
            if (distanceForWidth == null || !(preferredPixelWidth > 0.0)
                || !(minimumPixelWidth > 0.0) || minimumPixelWidth > preferredPixelWidth)
            {
                return false;
            }

            for (int i = 0; i < 16; i++)
            {
                if (distanceForWidth(measurablePixelWidth, out distanceMeters)
                    && distanceMeters > 0.0 && IsFinite(distanceMeters))
                {
                    return true;
                }

                if (measurablePixelWidth <= minimumPixelWidth)
                    break;
                measurablePixelWidth = Math.Max(minimumPixelWidth, measurablePixelWidth * 0.75);
            }

            return false;
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
