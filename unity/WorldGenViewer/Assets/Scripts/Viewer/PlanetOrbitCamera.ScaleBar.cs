using System;
using UnityEngine;
using WorldGen.Core;

namespace WorldGen.Viewer
{
    public partial class PlanetOrbitCamera
    {
        private const int ScaleBarFailureGraceCount = 8;

        [Header("M9: Fizikai lépték (ND-84)")]
        [SerializeField] private bool showPhysicalScaleBar = true;
        [SerializeField, Range(80f, 320f)] private float scaleBarTargetWidthPixels = 180f;
        [SerializeField, Range(12f, 100f)] private float scaleBarBottomMarginPixels = 36f;
        [SerializeField, Range(0.05f, 1f)] private float scaleBarRefreshIntervalSeconds = 0.15f;

        private Camera? _scaleBarCamera;
        private PlanetGridMesh? _scaleBarPlanet;
        private GUIStyle? _scaleBarLabelStyle;
        private bool _scaleBarValid;
        private int _scaleBarConsecutiveFailures;
        private float _scaleBarPixelWidth;
        private double _scaleBarDistanceMeters;
        private float _scaleReferenceCenterX;
        private float _scaleReferenceCenterY;
        private float _scaleViewportBottomGuiY;
        private float _nextScaleBarRefreshTime;
        private Transform? _scaleSampleTarget;
        private Matrix4x4 _scaleSampleView, _scaleSampleProjection, _scaleSampleTargetMatrix;
        private Rect _scaleSampleViewport;
        private int _scaleSampleWorldRevision, _scaleSampleScreenHeight;

        private void OnGUI()
        {
            if (!showPhysicalScaleBar)
            {
                InvalidateScaleBar();
                return;
            }

            // A frissítési időköz alatt sem rajzolhatunk régi nézethez tartozó számot.
            if (_scaleBarValid && !IsScaleBarContextCurrent()) InvalidateScaleBar();

            if (Event.current.type == EventType.Repaint
                && Time.unscaledTime >= _nextScaleBarRefreshTime)
            {
                _nextScaleBarRefreshTime = Time.unscaledTime
                    + Mathf.Max(0.05f, scaleBarRefreshIntervalSeconds);
                RecomputePhysicalScaleBar();
            }

            DrawPhysicalScaleBar();
        }

        private void RecomputePhysicalScaleBar()
        {
            if (_scaleBarCamera == null) _scaleBarCamera = GetComponent<Camera>();
            if (target == null) _scaleBarPlanet = null;
            else if (_scaleBarPlanet == null || _scaleBarPlanet.transform != target)
                _scaleBarPlanet = target.GetComponent<PlanetGridMesh>();
            if (_scaleBarCamera == null || _scaleBarCamera.orthographic
                || _scaleBarCamera.fieldOfView <= 0f || _scaleBarPlanet == null || target == null
                || !_scaleBarPlanet.IsSurfaceMeasurementCurrent)
            {
                InvalidateScaleBar();
                return;
            }

            Vector3 scale = target.lossyScale;
            float scaleMagnitude = Mathf.Max(1f, Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (!(scale.x > 0f) || float.IsInfinity(scaleMagnitude)
                || Mathf.Abs(scale.x - scale.y) > scaleMagnitude * 1e-5f
                || Mathf.Abs(scale.x - scale.z) > scaleMagnitude * 1e-5f)
            {
                InvalidateScaleBar();
                return;
            }

            Rect viewport = _scaleBarCamera.pixelRect;
            if (viewport.width < 20f || viewport.height <= 0f)
            {
                InvalidateScaleBar();
                return;
            }
            if (_scaleBarValid && !IsScaleBarContextCurrent()) InvalidateScaleBar();
            _scaleReferenceCenterX = viewport.x + viewport.width * 0.5f;
            _scaleReferenceCenterY = viewport.y + viewport.height * 0.5f;
            _scaleViewportBottomGuiY = Screen.height - viewport.y;
            double preferredWidth = Math.Min(
                Math.Max(20.0, scaleBarTargetWidthPixels),
                Math.Max(20.0, viewport.width * 0.45));
            if (!ScaleBarMath.TryFindMeasurableWidth(
                TryMeasureCenteredSurfaceDistance,
                preferredWidth,
                20.0,
                out double maximumWidth,
                out double maximumDistance))
            {
                RegisterTransientScaleBarFailure();
                return;
            }

            // A felirat mindig kerek érték; meredek domborzaton (nem folytonos
            // távolság, 2026-09-13) a csík szélessége a legközelebbi mért
            // távolsághoz igazodik - ld. ScaleBarMath.TrySolveScale.
            if (!ScaleBarMath.TrySolveScale(
                TryMeasureCenteredSurfaceDistance,
                maximumWidth,
                maximumDistance,
                4,
                out double solvedWidth,
                out double roundDistance,
                out _,
                out _,
                out _))
            {
                RegisterTransientScaleBarFailure();
                return;
            }

            _scaleBarPixelWidth = (float)solvedWidth;
            _scaleBarDistanceMeters = roundDistance;
            _scaleBarConsecutiveFailures = 0;
            _scaleBarValid = true;
            CaptureScaleBarContext();
        }

        private void CaptureScaleBarContext()
        {
            _scaleSampleTarget = target;
            _scaleSampleView = _scaleBarCamera!.worldToCameraMatrix;
            _scaleSampleProjection = _scaleBarCamera.projectionMatrix;
            _scaleSampleTargetMatrix = target.localToWorldMatrix;
            _scaleSampleViewport = _scaleBarCamera.pixelRect;
            _scaleSampleWorldRevision = _scaleBarPlanet!.SurfaceMeasurementRevision;
            _scaleSampleScreenHeight = Screen.height;
        }

        private bool IsScaleBarContextCurrent()
        {
            return target != null && _scaleSampleTarget == target && _scaleBarCamera != null
                && _scaleBarPlanet != null && _scaleBarPlanet.transform == target
                && _scaleBarPlanet.IsSurfaceMeasurementCurrent
                && _scaleBarPlanet.SurfaceMeasurementRevision == _scaleSampleWorldRevision
                && _scaleSampleView.Equals(_scaleBarCamera.worldToCameraMatrix)
                && _scaleSampleProjection.Equals(_scaleBarCamera.projectionMatrix)
                && _scaleSampleTargetMatrix.Equals(target.localToWorldMatrix)
                && _scaleSampleViewport.Equals(_scaleBarCamera.pixelRect)
                && _scaleSampleScreenHeight == Screen.height;
        }

        private void InvalidateScaleBar()
        {
            _scaleBarConsecutiveFailures = ScaleBarFailureGraceCount;
            _scaleBarValid = false;
        }

        private void RegisterTransientScaleBarFailure()
        {
            if (!IsScaleBarContextCurrent())
            {
                InvalidateScaleBar();
                return;
            }
            _scaleBarConsecutiveFailures++;
            if (_scaleBarConsecutiveFailures >= ScaleBarFailureGraceCount)
                _scaleBarValid = false;
        }

        private bool TryMeasureCenteredSurfaceDistance(double pixelWidth, out double distanceMeters)
        {
            distanceMeters = 0.0;
            if (!(pixelWidth > 0.0)
                || !TrySurfaceDirectionAtScreenPoint(
                    _scaleReferenceCenterX - (float)pixelWidth * 0.5f,
                    _scaleReferenceCenterY,
                    out ScaleBarMath.Vector3d left)
                || !TrySurfaceDirectionAtScreenPoint(
                    _scaleReferenceCenterX + (float)pixelWidth * 0.5f,
                    _scaleReferenceCenterY,
                    out ScaleBarMath.Vector3d right))
            {
                return false;
            }

            distanceMeters = ScaleBarMath.GreatCircleDistanceMeters(
                left, right, PlanetConstants.RadiusMeters);
            return distanceMeters > 0.0
                && !double.IsNaN(distanceMeters) && !double.IsInfinity(distanceMeters);
        }

        private bool TrySurfaceDirectionAtScreenPoint(float screenX, float screenY, out ScaleBarMath.Vector3d direction)
        {
            direction = default(ScaleBarMath.Vector3d);
            Ray worldRay = _scaleBarCamera!.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
            Vector3 localOrigin = target.InverseTransformPoint(worldRay.origin);
            Vector3 localNext = target.InverseTransformPoint(worldRay.origin + worldRay.direction);
            Vector3 localRayDirection = localNext - localOrigin;
            var origin = new ScaleBarMath.Vector3d(localOrigin.x, localOrigin.y, localOrigin.z);
            var rayDirection = new ScaleBarMath.Vector3d(
                localRayDirection.x, localRayDirection.y, localRayDirection.z);
            return ScaleBarMath.TryIntersectRadialSurface(
                origin, rayDirection, _scaleBarPlanet!.SurfaceMeasurementBaseRadius, TryGetDisplayedRadius, out direction);
        }

        private bool TryGetDisplayedRadius(ScaleBarMath.Vector3d direction, out double displayedRadius)
        {
            return _scaleBarPlanet!.TryGetScaleSurfaceRadius(
                new Vector3((float)direction.X, (float)direction.Y, (float)direction.Z),
                out displayedRadius);
        }

        private void DrawPhysicalScaleBar()
        {
            EnsureScaleBarStyle();
            float centerX = _scaleReferenceCenterX > 0f ? _scaleReferenceCenterX : Screen.width * 0.5f;
            float viewportBottom = _scaleViewportBottomGuiY > 0f ? _scaleViewportBottomGuiY : Screen.height;
            float baselineY = viewportBottom - scaleBarBottomMarginPixels;
            string label = _scaleBarValid
                ? FormatScaleDistance(_scaleBarDistanceMeters) + "  (képközép)"
                : "Lépték: —";
            float width = _scaleBarValid ? _scaleBarPixelWidth : scaleBarTargetWidthPixels;
            float left = centerX - width * 0.5f;

            DrawScaleRect(
                new Rect(centerX - 126f, baselineY - 30f, 252f, 51f),
                new Color(0f, 0f, 0f, 0.58f));
            GUI.Label(new Rect(centerX - 120f, baselineY - 28f, 240f, 22f), label, _scaleBarLabelStyle!);
            if (_scaleBarValid)
            {
                DrawScaleRect(new Rect(left - 1f, baselineY - 1f, width + 2f, 3f), Color.black);
                DrawScaleRect(new Rect(left, baselineY, width, 1f), Color.white);
                DrawScaleRect(new Rect(left - 1f, baselineY - 7f, 3f, 15f), Color.black);
                DrawScaleRect(new Rect(left, baselineY - 6f, 1f, 13f), Color.white);
                DrawScaleRect(new Rect(left + width - 1f, baselineY - 7f, 3f, 15f), Color.black);
                DrawScaleRect(new Rect(left + width, baselineY - 6f, 1f, 13f), Color.white);

                // A perspektivikus meres konkret referenciapontja, nem globalis
                // kepernyoskala: egy visszafogott kereszt jeloli a kep kozepet.
                float markerY = Screen.height - _scaleReferenceCenterY;
                DrawScaleRect(new Rect(_scaleReferenceCenterX - 5f, markerY, 11f, 1f), new Color(1f, 1f, 1f, 0.65f));
                DrawScaleRect(new Rect(_scaleReferenceCenterX, markerY - 5f, 1f, 11f), new Color(1f, 1f, 1f, 0.65f));
            }
        }

        private void EnsureScaleBarStyle()
        {
            if (_scaleBarLabelStyle != null)
                return;

            _scaleBarLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }

        private static void DrawScaleRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static string FormatScaleDistance(double meters)
        {
            if (meters < 1000.0)
                return meters >= 10.0 ? meters.ToString("0") + " m" : meters.ToString("0.#") + " m";

            double kilometers = meters / 1000.0;
            return kilometers >= 10.0
                ? kilometers.ToString("0") + " km"
                : kilometers.ToString("0.#") + " km";
        }
    }
}
