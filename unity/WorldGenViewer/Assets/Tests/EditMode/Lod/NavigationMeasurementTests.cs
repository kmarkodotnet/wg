using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace WorldGen.Viewer.Lod.Tests
{
    /// <summary>ND-95: a valódi Assembly-CSharp bekötés, drága világépítés nélkül.</summary>
    public class NavigationMeasurementTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _planetObject = null!, _cameraObject = null!;
        private Component _planet = null!, _orbit = null!;
        private Camera _camera = null!;

        private static object Call(object owner, string name, params object[] args)
            => owner.GetType().GetMethod(name, Hidden).Invoke(owner, args);
        private static void Set(object owner, string name, object value)
            => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
        private static object Get(object owner, string name)
            => owner.GetType().GetField(name, Hidden).GetValue(owner);

        [SetUp]
        public void SetUp()
        {
            _planetObject = new GameObject("MeasurementPlanet");
            _cameraObject = new GameObject("MeasurementCamera");
            _planetObject.SetActive(false);
            _cameraObject.SetActive(false);
            _planet = _planetObject.AddComponent(Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", true));
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.nearClipPlane = .3f;
            _camera.pixelRect = new Rect(0, 0, 960, 600);
            _orbit = _cameraObject.AddComponent(Type.GetType("WorldGen.Viewer.PlanetOrbitCamera, Assembly-CSharp", true));
            Set(_orbit, "target", _planetObject.transform);
            Set(_orbit, "_scaleBarCamera", _camera);
            Set(_orbit, "_scaleBarPlanet", _planet);
            Call(_planet, "SnapshotWorldConfig");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_cameraObject);
            UnityEngine.Object.DestroyImmediate(_planetObject);
        }

        private void CaptureValidScale()
        {
            Call(_orbit, "CaptureScaleBarContext");
            Set(_orbit, "_scaleBarValid", true);
            Assert.That(Call(_orbit, "IsScaleBarContextCurrent"), Is.True);
        }

        [TestCase("camera")]
        [TestCase("target")]
        [TestCase("projection")]
        [TestCase("viewport")]
        [TestCase("worldPending")]
        [TestCase("worldBuilt")]
        [TestCase("otherTarget")]
        public void GraceCannotReuseScaleAfterMeasurementContextChanges(string change)
        {
            CaptureValidScale();
            switch (change)
            {
                case "camera": _cameraObject.transform.position += Vector3.forward; break;
                case "target": _planetObject.transform.rotation = Quaternion.Euler(0, 15, 0); break;
                case "projection": _camera.fieldOfView = 75; break;
                case "viewport": _camera.pixelRect = new Rect(20, 0, 800, 600); break;
                case "worldPending": Set(_planet, "deepTimeMyr", 10.0); break;
                case "worldBuilt": Call(_planet, "SnapshotWorldConfig"); break;
                case "otherTarget": Set(_orbit, "target", _cameraObject.transform); break;
            }
            Assert.That(Call(_orbit, "IsScaleBarContextCurrent"), Is.False);
            Call(_orbit, "RegisterTransientScaleBarFailure");
            Assert.That(Get(_orbit, "_scaleBarValid"), Is.False);
        }

        [Test]
        public void SameViewRetainsSevenTransientFailuresButNotEight()
        {
            CaptureValidScale();
            for (int i = 0; i < 7; i++) Call(_orbit, "RegisterTransientScaleBarFailure");
            Assert.That(Get(_orbit, "_scaleBarValid"), Is.True);
            Call(_orbit, "RegisterTransientScaleBarFailure");
            Assert.That(Get(_orbit, "_scaleBarValid"), Is.False);
        }

        [Test]
        public void SwitchingToTargetWithoutPlanetDoesNotKeepPreviousModel()
        {
            CaptureValidScale();
            Set(_orbit, "target", _cameraObject.transform);
            Call(_orbit, "RecomputePhysicalScaleBar");
            Assert.That(Get(_orbit, "_scaleBarPlanet"), Is.Null);
            Assert.That(Get(_orbit, "_scaleBarValid"), Is.False);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void DegenerateOrMirroredScaleIsInvalid(float scale)
        {
            CaptureValidScale();
            _planetObject.transform.localScale = Vector3.one * scale;
            Call(_orbit, "RecomputePhysicalScaleBar");
            Assert.That(Get(_orbit, "_scaleBarValid"), Is.False);
        }

        private void SetCachedRadius(double radius)
        {
            Set(_planet, "_cachedCameraSurfaceRevision", Get(_planet, "_cameraSurfaceRevision"));
            var direction = (Vector3)_orbit.GetType().GetProperty("CurrentViewDirection").GetValue(_orbit);
            Set(_planet, "_cachedCameraSurfaceDirection", _planetObject.transform.InverseTransformDirection(direction));
            Set(_planet, "_cachedCameraSurfaceRadius", radius);
        }

        [TestCase(1f)]
        [TestCase(2f)]
        public void FlyToKeepsRequestedAltitudeAtEveryLocalSurface(float scale)
        {
            _planetObject.transform.localScale = Vector3.one * scale;
            _planetObject.transform.position = new Vector3(10, 20, 30);
            foreach (double radius in new[] { 100.02, 105.0, 110.0, 100.02 })
            {
                Set(_orbit, "_yaw", (float)radius);
                SetCachedRadius(radius);
                Call(_orbit, "ApplyFlyToAltitude", 5f);
                Assert.That(Vector3.Distance(_cameraObject.transform.position, _planetObject.transform.position),
                    Is.EqualTo(radius * scale + 5).Within(.0001));
                Assert.That(_orbit.GetType().GetProperty("AltitudeAboveSurface").GetValue(_orbit),
                    Is.EqualTo(5f).Within(.0001));
            }
        }

        [TestCase(-5f, 105.33f)]
        [TestCase(900f, 800f)]
        public void FlyToStillHonorsNearPlaneAndMaximum(float altitude, float expected)
        {
            SetCachedRadius(105);
            Call(_orbit, "ApplyFlyToAltitude", altitude);
            Assert.That(Get(_orbit, "distance"), Is.EqualTo(expected).Within(.0001));
        }

        [Test]
        public void FlyToUsesNewWorldRadiusAndSupportsLegacyMode()
        {
            SetCachedRadius(105);
            Call(_orbit, "ApplyFlyToAltitude", 5f);
            Call(_planet, "SnapshotWorldConfig");
            SetCachedRadius(115);
            Call(_orbit, "ApplyFlyToAltitude", 5f);
            Assert.That(Get(_orbit, "distance"), Is.EqualTo(120f).Within(.0001));
            Set(_orbit, "followLocalSurface", false);
            Call(_orbit, "ApplyFlyToAltitude", 5f);
            Assert.That(Get(_orbit, "distance"), Is.EqualTo(105f).Within(.0001));
        }

        [Test]
        public void ZeroDurationCoroutineUsesFinalDirectionAndAltitude()
        {
            // A tényleges coroutine végpontja is a magasságos útra legyen bekötve.
            Set(_orbit, "_pitch", 15f);
            Set(_orbit, "_yaw", 40f);
            SetCachedRadius(112);
            Set(_orbit, "_pitch", 0f);
            Set(_orbit, "_yaw", 0f);
            var routine = (System.Collections.IEnumerator)Call(_orbit, "FlyToCoroutine", 15f, 40f, 7f, 0f);
            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(Get(_orbit, "distance"), Is.EqualTo(119f).Within(.0001));
            Assert.That(Get(_orbit, "_pitch"), Is.EqualTo(15f));
            Assert.That(Get(_orbit, "_yaw"), Is.EqualTo(40f));
        }
    }
}
