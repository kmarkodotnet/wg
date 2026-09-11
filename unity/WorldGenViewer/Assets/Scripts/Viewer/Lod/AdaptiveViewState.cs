using System;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// Egy LOD-kérés testkoordinátás nézete. A kész eredmény ezt igazolja,
    /// nem a közben esetleg továbbmozdult kamera aktuális állapotát (ND-69).
    /// </summary>
    public readonly struct AdaptiveViewState
    {
        public readonly double X, Y, Z, ForwardX, ForwardY, ForwardZ;
        public readonly double VerticalFovRadians, Aspect;
        public readonly int PixelWidth, PixelHeight;

        public AdaptiveViewState(double x, double y, double z,
            double forwardX, double forwardY, double forwardZ,
            double verticalFovRadians, double aspect, int pixelWidth, int pixelHeight)
        {
            X = x; Y = y; Z = z;
            ForwardX = forwardX; ForwardY = forwardY; ForwardZ = forwardZ;
            VerticalFovRadians = verticalFovRadians; Aspect = aspect;
            PixelWidth = pixelWidth; PixelHeight = pixelHeight;
        }

        /// <summary>
        /// A nagy mozgás és a vetítés változása az időkapu után rögtön frissül.
        /// A kis elmozdulás legfeljebb 0,25 s indítási késleltetést kap, nem
        /// vész el. Futó kérés mellett a hívó továbbra sem indít másik workert.
        /// </summary>
        public bool NeedsRefresh(in AdaptiveViewState previous, double movementThreshold,
            double secondsSinceRequest)
        {
            if (ForwardX != previous.ForwardX || ForwardY != previous.ForwardY
                || ForwardZ != previous.ForwardZ || VerticalFovRadians != previous.VerticalFovRadians
                || Aspect != previous.Aspect || PixelWidth != previous.PixelWidth
                || PixelHeight != previous.PixelHeight)
                return true;

            double dx = X - previous.X, dy = Y - previous.Y, dz = Z - previous.Z;
            double distanceSquared = dx * dx + dy * dy + dz * dz;
            if (distanceSquared == 0.0) return false;
            double threshold = Math.Max(0.0, movementThreshold);
            return distanceSquared >= threshold * threshold || secondsSinceRequest >= 0.25;
        }

        /// <summary>Perspektivikus, képközépi pixelsugár → szögsugár.</summary>
        public static double AngularRadiusForPixelDiameter(double pixelDiameter,
            double verticalFovRadians, int pixelHeight)
        {
            if (!(verticalFovRadians > 0.0 && verticalFovRadians < Math.PI))
                throw new ArgumentOutOfRangeException(nameof(verticalFovRadians));
            double focalLengthPixels = Math.Max(1, pixelHeight) / (2.0 * Math.Tan(verticalFovRadians / 2.0));
            return Math.Atan(Math.Max(1.0, pixelDiameter) / (2.0 * focalLengthPixels));
        }

        /// <summary>
        /// ND-72 diagnosztika: a négy sarok homogén clip-(X,Y,W) értékei;
        /// a SurfacePoint.Z itt W. A hívó előbb kizárja a near-plane metszést.
        /// Nem clipping/takarásvizsgálat. Érvénytelen vetítésre NaN jár.
        /// </summary>
        public static double QuadPixelDiameter(SurfaceQuad clipXYW, int width, int height)
        {
            if (width <= 0 || height <= 0) return double.NaN;
            SurfacePoint Project(SurfacePoint p)
            {
                if (!(p.Z > 0) || double.IsInfinity(p.Z))
                    return new SurfacePoint(double.NaN, double.NaN, 0);
                return new SurfacePoint(p.X / p.Z * width * 0.5, p.Y / p.Z * height * 0.5, 0);
            }
            double Distance(SurfacePoint a, SurfacePoint b)
            {
                double x = a.X - b.X, y = a.Y - b.Y;
                return Math.Sqrt(x * x + y * y);
            }
            SurfacePoint a = Project(clipXYW.P00), b = Project(clipXYW.P10);
            SurfacePoint c = Project(clipXYW.P11), d = Project(clipXYW.P01);
            double diameter = Math.Max(Math.Max(Distance(a, b), Distance(a, c)),
                Math.Max(Math.Max(Distance(a, d), Distance(b, c)), Math.Max(Distance(b, d), Distance(c, d))));
            return double.IsInfinity(diameter) ? double.NaN : diameter;
        }

        /// <summary>ND-73: korábbi első split, csak pozitív hiszterézissávval.</summary>
        public static double EarlierBaseSplitScale(double split, double merge, double requested)
            => requested > merge && requested < split ? requested / split : 1.0;

        /// <summary>A splitnél 0, a tartomány végén 1; nincs születési pozícióugrás.</summary>
        public static double GeomorphAlpha(double distance, double splitDistance, double rangeFraction)
        {
            double range = rangeFraction * splitDistance;
            if (range <= 0) return 1;
            return Math.Max(0, Math.Min(1, (splitDistance - distance) / range));
        }
    }
}
