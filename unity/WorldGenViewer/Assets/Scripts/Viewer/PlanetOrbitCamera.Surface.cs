using System.Diagnostics;
using UnityEngine;

namespace WorldGen.Viewer
{
    public partial class PlanetOrbitCamera
    {
        [Header("M9: Helyi felszínkövetés (ND-91)")]
        [SerializeField, Tooltip("A helyi modellfelszín felett tartja a kamerát. Pontmintás korlát, nem teljes mesh-ütközésvizsgálat.")]
        private bool followLocalSurface = true;

        private PlanetGridMesh? _surfacePlanet;
        private Camera? _surfaceCamera;
        private float _localSurfaceRadius;
        private bool _hasLocalSurface;
        private bool _surfaceSampleValid;
        private float _nextSurfaceLogTime;
        private float _surfaceCorrection;
        private double _surfaceSampleMs;

        private float EffectiveSurfaceRadius => followLocalSurface && _hasLocalSurface
            ? _localSurfaceRadius : surfaceRadius;

        private void RefreshLocalSurface()
        {
            _surfaceSampleValid = false;
            if (!followLocalSurface || target == null)
            {
                _hasLocalSurface = false;
                return;
            }
            if (_surfacePlanet == null || _surfacePlanet.transform != target)
            {
                _surfacePlanet = target.GetComponent<PlanetGridMesh>();
                _hasLocalSurface = false;
            }
            if (_surfacePlanet == null) return;
            Vector3 scale = target.lossyScale;
            if (!(scale.x > 0f) || Mathf.Abs(scale.x - scale.y) > scale.x * 1e-5f
                || Mathf.Abs(scale.x - scale.z) > scale.x * 1e-5f)
            {
                _hasLocalSurface = false;
                return;
            }
            long started = Stopwatch.GetTimestamp();
            Vector3 localDirection = target.InverseTransformDirection(CurrentViewDirection);
            if (_surfacePlanet.TryGetCameraSurfaceRadius(localDirection, out double localRadius))
            {
                _localSurfaceRadius = (float)(localRadius * scale.x);
                _hasLocalSurface = true;
                _surfaceSampleValid = true;
            }
            // Új Build előtt nem keverjük a régi snapshotot az új paraméterekkel.
            // A korábbi sugár megmarad, de alapgömb alatti fallback nem engedett.
            else if (_hasLocalSurface) _localSurfaceRadius = Mathf.Max(surfaceRadius, _localSurfaceRadius);
            _surfaceSampleMs += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }

        private void ConstrainSurfaceDistance()
        {
            if (!followLocalSurface) return;
            if (_surfaceCamera == null) _surfaceCamera = GetComponent<Camera>();
            double clearance = OrbitSurfaceMath.MinimumClearance(minDistance, surfaceRadius,
                _surfaceCamera != null ? _surfaceCamera.nearClipPlane : 0f);
            float next = (float)OrbitSurfaceMath.ClampDistance(distance, EffectiveSurfaceRadius, clearance, maxDistance);
            _surfaceCorrection = Mathf.Max(_surfaceCorrection, next - distance);
            distance = next;
        }

        private void LateUpdate()
        {
            // A target tengelyforgása/Buildje az Update után is változhatott.
            if (followLocalSurface) ApplyTransform();
            if (Time.unscaledTime < _nextSurfaceLogTime) return;
            _nextSurfaceLogTime = Time.unscaledTime + 1f;
            if (_surfacePlanet != null)
                _surfacePlanet.LogCameraSurface($"[ND-91 camera surface] enabled={followLocalSurface} " +
                    $"source={(_surfaceSampleValid ? "model" : "fallback")} distanceUnits={distance:F6} " +
                    $"surfaceRadiusUnits={EffectiveSurfaceRadius:F6} altitudeUnits={AltitudeAboveSurface:F6} " +
                    $"outwardCorrectionUnits={_surfaceCorrection:F6} sampleTotalMs={_surfaceSampleMs:F3}");
            _surfaceCorrection = 0f;
            _surfaceSampleMs = 0;
        }
    }
}
