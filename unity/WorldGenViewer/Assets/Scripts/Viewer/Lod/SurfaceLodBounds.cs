using System;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// ND-71 offline kísérlet: ND-72 óta NEM az aktív viewer metrikája.
    /// A mintavételezett, eltolt patch metrikája. A minták közötti
    /// ismeretlen terepmaximumra nem ad bizonyított korlátot.
    /// </summary>
    public readonly struct SurfaceLodBounds
    {
        public readonly double X, Y, Z, Radius, FootprintRadius;
        public SurfacePoint Center => new SurfacePoint(X, Y, Z);

        public SurfaceLodBounds(double x, double y, double z, double radius, double? footprintRadius = null)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(radius) || radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            double footprint = footprintRadius ?? radius;
            if (!Finite(footprint) || footprint < 0 || footprint > radius)
                throw new ArgumentOutOfRangeException(nameof(footprintRadius));
            X = x; Y = y; Z = z; Radius = radius; FootprintRadius = footprint;
        }

        public static SurfaceLodBounds FromSamples(SurfacePoint center, SurfaceQuad quad)
        {
            double r = Math.Max(Math.Max(Distance(center, quad.P00), Distance(center, quad.P10)),
                Math.Max(Distance(center, quad.P11), Distance(center, quad.P01)));
            // A radiális magasságugrás nem válhat minden mélységen azonos
            // tesszellációs hibává: a mintasűrűséget a patch érintősíkbeli
            // kiterjedése vezérli. A teljes bounds megmarad a láthatósághoz.
            double footprint = Math.Max(Math.Max(TangentDistance(center, quad.P00), TangentDistance(center, quad.P10)),
                Math.Max(TangentDistance(center, quad.P11), TangentDistance(center, quad.P01)));
            return new SurfaceLodBounds(center.X, center.Y, center.Z, r, Math.Min(r, footprint));
        }

        // ND-96: a kész quad vetítéséhez csak a befoglaló gömb kell, érintősík-metrika nem.
        public static SurfaceLodBounds FromQuad(SurfaceQuad quad)
        {
            var center = (quad.P00 + quad.P10 + quad.P11 + quad.P01) * .25;
            double radius = Math.Max(Math.Max(Distance(center, quad.P00), Distance(center, quad.P10)),
                Math.Max(Distance(center, quad.P11), Distance(center, quad.P01)));
            return new SurfaceLodBounds(center.X, center.Y, center.Z, radius);
        }

        public double DistanceTo(double x, double y, double z)
            => Math.Sqrt((x - X) * (x - X) + (y - Y) * (y - Y) + (z - Z) * (z - Z));

        public double AngularRadius(double x, double y, double z)
            => Math.Atan2(FootprintRadius, DistanceTo(x, y, z));

        public double MorphAlpha(double x, double y, double z, double threshold, double rangeFraction)
        {
            double splitDistance = FootprintRadius / Math.Tan(Math.Max(threshold, 1e-9));
            double range = rangeFraction * splitDistance;
            if (range <= 0) return 1;
            return Math.Max(0, Math.Min(1, (splitDistance - DistanceTo(x, y, z)) / range));
        }

        /// <summary>A befoglaló gömb nézetkúptesztje; belülről nem dobhatja el a patch-et.</summary>
        public bool IntersectsViewCone(double x, double y, double z,
            double forwardX, double forwardY, double forwardZ, double halfFov)
        {
            double d = DistanceTo(x, y, z);
            if (halfFov >= Math.PI || d <= Radius || d < 1e-9) return true;
            double cosine = ((X - x) * forwardX + (Y - y) * forwardY + (Z - z) * forwardZ) / d;
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, cosine)));
            return angle <= halfFov + Math.Asin(Math.Min(1, Radius / d));
        }

        private static double TangentDistance(SurfacePoint center, SurfacePoint point)
        {
            double length = Math.Sqrt(center.X * center.X + center.Y * center.Y + center.Z * center.Z);
            if (length < 1e-12) return Distance(center, point);
            double nx = center.X / length, ny = center.Y / length, nz = center.Z / length;
            double dx = point.X - center.X, dy = point.Y - center.Y, dz = point.Z - center.Z;
            double radial = dx * nx + dy * ny + dz * nz;
            dx -= radial * nx; dy -= radial * ny; dz -= radial * nz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double Distance(SurfacePoint a, SurfacePoint b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)
                + (a.Z - b.Z) * (a.Z - b.Z));
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
