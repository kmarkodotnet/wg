using System;

namespace WorldGen.Viewer
{
    /// <summary>ND-91: viewer-egységű radiális kamerakorlát, Core-függés nélkül.</summary>
    public static class OrbitSurfaceMath
    {
        public static double MinimumClearance(double legacyMinimum, double baseRadius, double nearClip)
        {
            return Math.Max(0.001, Math.Max(legacyMinimum - baseRadius, nearClip * 1.1));
        }

        public static double ClampDistance(double requested, double localRadius, double clearance, double maximum)
        {
            double minimum = localRadius + clearance;
            // A hibásan alacsony felső határ nem tolhatja a kamerát a felszínbe.
            return Math.Max(minimum, Math.Min(requested, Math.Max(minimum, maximum)));
        }

        public static double ZoomDistance(double distance, double localRadius, double scroll, double sensitivity)
        {
            return localRadius + Math.Max(0.001, distance - localRadius) * Math.Exp(-scroll * sensitivity);
        }
    }
}
