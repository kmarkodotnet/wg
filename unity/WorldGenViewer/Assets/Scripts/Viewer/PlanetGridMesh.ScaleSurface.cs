using UnityEngine;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        private int _cameraSurfaceRevision;
        private int _cachedCameraSurfaceRevision = -1;
        private Vector3 _cachedCameraSurfaceDirection;
        private double _cachedCameraSurfaceRadius;
        internal bool IsSurfaceMeasurementCurrent => !WorldConfigChangedSinceBuild();
        internal int SurfaceMeasurementRevision => _cameraSurfaceRevision;
        internal double SurfaceMeasurementBaseRadius => radius;

        internal bool TryGetCameraSurfaceRadius(Vector3 localDirection, out double surfaceRadius)
        {
            surfaceRadius = 0;
            if (WorldConfigChangedSinceBuild()) return false;
            // Equals pontos komponensegyezés: nincs iránykvantálásból eredő magassághiba.
            if (_cachedCameraSurfaceRevision == _cameraSurfaceRevision
                && _cachedCameraSurfaceDirection.Equals(localDirection))
            {
                surfaceRadius = _cachedCameraSurfaceRadius;
                return true;
            }
            if (!TryGetScaleSurfaceRadius(localDirection, out surfaceRadius)) return false;
            _cachedCameraSurfaceDirection = localDirection;
            _cachedCameraSurfaceRadius = surfaceRadius;
            _cachedCameraSurfaceRevision = _cameraSurfaceRevision;
            return true;
        }

        internal void LogCameraSurface(string message) => PerfLog(message);

        /// <summary>
        /// Az ND-84 leptek sugar-metszesehez a renderrel azonos radiust adja.
        /// A szarazfold a tulrajzolt domborzatot, a viz a tenylegesen renderelt
        /// tengerszintsugarat hasznalja. Nem indit Buildet es nem olvas LOD-ot.
        /// </summary>
        internal bool TryGetScaleSurfaceRadius(Vector3 localDirection, out double surfaceRadius)
        {
            surfaceRadius = 0.0;
            if (!IsSurfaceMeasurementCurrent || _adaptiveSeeds == null || _adaptiveCraters == null
                || localDirection.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            localDirection.Normalize();
            BodyFrameConversion.ToCore(localDirection, out double x, out double y, out double z);
            double elevation = ComputeElevationAtPoint(
                x, y, z, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters, _adaptiveErosionTimeMyr);
            if (double.IsNaN(elevation) || double.IsInfinity(elevation))
                return false;

            double displayedElevation = elevation <= _adaptiveSeaLevel
                ? _adaptiveSeaLevel
                : DisplayElevation(elevation);
            surfaceRadius = radius + displayedElevation * elevationScale;
            return surfaceRadius > 0.0
                && !double.IsNaN(surfaceRadius) && !double.IsInfinity(surfaceRadius);
        }
    }
}
