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

        // IDEIGLENES DIAGNOSZTIKA (2026-09-13, felhasznaloi keres: rotacio/zoom
        // szaggatas - ld. history/2026-09-13-scale-bar-rugged-terrain-fix.md
        // folytatasa). Szamolja, hanyszor fut le a draga ComputeElevationAtPoint
        // egy-egy PlanetOrbitCamera.RecomputePhysicalScaleBar() hivas alatt.
        // #8 (2026-09-21): a leptekcsik szamitasa MAR HATTERSZALON fut, ezert
        // ez a diagnosztikai szamlalo Interlocked - kulonben a novelesek
        // elveszhetnenek, es a naplozott evalCount alabecsulne.
        private int _scaleSurfaceEvaluationCount;
        internal int ScaleSurfaceEvaluationCount => _scaleSurfaceEvaluationCount;
        internal void ResetScaleSurfaceEvaluationCount()
            => System.Threading.Interlocked.Exchange(ref _scaleSurfaceEvaluationCount, 0);

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
            System.Threading.Interlocked.Increment(ref _scaleSurfaceEvaluationCount);
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
