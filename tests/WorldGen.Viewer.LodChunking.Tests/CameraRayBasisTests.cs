using System;
using WorldGen.Viewer;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// A léptékcsík HÁTTÉRSZÁLAS számításához kellő, Unity-független
    /// perspektív sugár (#8, 2026-09-21). A `Camera.ScreenPointToRay` csak a
    /// fő szálról hívható, ezért a sugarat tiszta aritmetikával állítjuk elő.
    ///
    /// EZ A FÁJL AZT MÉRI, ami offline eldönthető: a képlet belső
    /// tulajdonságait (középpont, szimmetria, szögek, oldalarány). Azt, hogy
    /// a kimenet MEGEGYEZIK-E a Unity `ScreenPointToRay`-jével, offline NEM
    /// lehet igazolni - arra az élő Editorban van egy egyszeri
    /// összehasonlítás (PlanetOrbitCamera.ScaleBar), és ezt a munkamegosztást
    /// szándékosan mondjuk ki itt is.
    /// </summary>
    public class CameraRayBasisTests
    {
        private readonly ITestOutputHelper _out;
        public CameraRayBasisTests(ITestOutputHelper o) { _out = o; }

        private static ScaleBarMath.CameraRayBasis Basis(double fovDegrees = 60.0, double aspect = 16.0 / 9.0)
        {
            double tanHalf = Math.Tan(fovDegrees * Math.PI / 360.0);
            return new ScaleBarMath.CameraRayBasis(
                new ScaleBarMath.Vector3d(0, 0, -300),
                new ScaleBarMath.Vector3d(0, 0, 1),   // elore
                new ScaleBarMath.Vector3d(1, 0, 0),   // jobbra
                new ScaleBarMath.Vector3d(0, 1, 0),   // fel
                tanHalf, aspect);
        }

        private static double Angle(ScaleBarMath.Vector3d a, ScaleBarMath.Vector3d b)
        {
            a.TryNormalize(out ScaleBarMath.Vector3d na);
            b.TryNormalize(out ScaleBarMath.Vector3d nb);
            double dot = Math.Max(-1.0, Math.Min(1.0, ScaleBarMath.Vector3d.Dot(na, nb)));
            return Math.Acos(dot);
        }

        /// <summary>A viewport közepén a sugár PONTOSAN előre mutat.</summary>
        [Fact]
        public void CenterOfViewportLooksStraightAhead()
        {
            ScaleBarMath.CameraRayBasis basis = Basis();
            ScaleBarMath.Vector3d direction = ScaleBarMath.RayDirectionAtNdc(basis, 0.0, 0.0);

            Assert.Equal(0.0, direction.X);
            Assert.Equal(0.0, direction.Y);
            Assert.Equal(1.0, direction.Z);
            Assert.True(basis.IsUsable);
        }

        /// <summary>
        /// A vízszintes kitérés szöge pontosan `atan(ndcX * tan(fovH/2))`,
        /// ahol `tan(fovH/2) = tan(fovV/2) * oldalarány`. Ez a perspektív
        /// projekció definíciója - ha ez elromlik, a mért távolság (és így a
        /// kiírt lépték) rendszeresen téves lenne.
        /// </summary>
        [Theory]
        [InlineData(60.0, 16.0 / 9.0, 1.0)]
        [InlineData(60.0, 16.0 / 9.0, 0.5)]
        [InlineData(60.0, 16.0 / 9.0, -0.25)]
        [InlineData(35.0, 4.0 / 3.0, 0.8)]
        [InlineData(90.0, 21.0 / 9.0, 0.3)]
        public void HorizontalAngleFollowsTheProjectionDefinition(double fov, double aspect, double ndcX)
        {
            ScaleBarMath.CameraRayBasis basis = Basis(fov, aspect);
            ScaleBarMath.Vector3d direction = ScaleBarMath.RayDirectionAtNdc(basis, ndcX, 0.0);

            double tanHalfHorizontal = Math.Tan(fov * Math.PI / 360.0) * aspect;
            double expected = Math.Atan(Math.Abs(ndcX) * tanHalfHorizontal);
            double actual = Angle(basis.Forward, direction);

            _out.WriteLine($"fov={fov} aspect={aspect:F3} ndcX={ndcX}: "
                + $"elvart={expected * 180 / Math.PI:F4}° kapott={actual * 180 / Math.PI:F4}°");
            Assert.InRange(Math.Abs(actual - expected), 0.0, 1e-12);
        }

        /// <summary>
        /// A viewport SZÉLE pontosan a fél látószögnél van - vízszintesen és
        /// függőlegesen is. Ez az, ami a léptékcsík pixel-hosszát a valódi
        /// szöghöz kapcsolja.
        /// </summary>
        [Theory]
        [InlineData(60.0, 16.0 / 9.0)]
        [InlineData(45.0, 1.0)]
        public void ViewportEdgesSitAtTheHalfFieldOfView(double fov, double aspect)
        {
            ScaleBarMath.CameraRayBasis basis = Basis(fov, aspect);
            double halfVertical = fov * Math.PI / 360.0;
            double halfHorizontal = Math.Atan(Math.Tan(halfVertical) * aspect);

            Assert.InRange(Math.Abs(Angle(basis.Forward,
                ScaleBarMath.RayDirectionAtNdc(basis, 0.0, 1.0)) - halfVertical), 0.0, 1e-12);
            Assert.InRange(Math.Abs(Angle(basis.Forward,
                ScaleBarMath.RayDirectionAtNdc(basis, 1.0, 0.0)) - halfHorizontal), 0.0, 1e-12);
        }

        /// <summary>
        /// Szimmetria: a középponttól egyenlő távolságra lévő két pont
        /// EGYENLŐ szöget zár be az előre iránnyal. A léptékcsík pontosan két
        /// ilyen, a középpontra szimmetrikus pontot mér - ha a szimmetria
        /// sérülne, a mért szélesség a kamera-elfordulástól függően
        /// ugrálna.
        /// </summary>
        [Theory]
        [InlineData(0.1)]
        [InlineData(0.5)]
        [InlineData(0.95)]
        public void SymmetricOffsetsGiveEqualAngles(double offset)
        {
            ScaleBarMath.CameraRayBasis basis = Basis();
            double left = Angle(basis.Forward, ScaleBarMath.RayDirectionAtNdc(basis, -offset, 0.0));
            double right = Angle(basis.Forward, ScaleBarMath.RayDirectionAtNdc(basis, offset, 0.0));

            Assert.InRange(Math.Abs(left - right), 0.0, 1e-15);
        }

        /// <summary>
        /// Az OLDALARÁNY érdemben hat: ugyanaz az NDC szélesebb viewporton
        /// nagyobb szöget jelent. Enélkül a teszt akkor is zöld lenne, ha az
        /// oldalarány kimaradna a képletből - és a léptékcsík minden nem-16:9
        /// ablakban téves lenne.
        /// </summary>
        [Fact]
        public void AspectRatioActuallyMatters()
        {
            double narrow = Angle(Basis(60.0, 1.0).Forward,
                ScaleBarMath.RayDirectionAtNdc(Basis(60.0, 1.0), 0.5, 0.0));
            double wide = Angle(Basis(60.0, 21.0 / 9.0).Forward,
                ScaleBarMath.RayDirectionAtNdc(Basis(60.0, 21.0 / 9.0), 0.5, 0.0));

            _out.WriteLine($"1:1 -> {narrow * 180 / Math.PI:F2}°, 21:9 -> {wide * 180 / Math.PI:F2}°");
            Assert.True(wide > narrow * 1.5,
                "Az oldalarány nem hat érdemben - valószínűleg kimaradt a képletből.");
        }

        /// <summary>Degenerált bemenetet a bázis maga jelez, nem NaN-t terjeszt.</summary>
        [Fact]
        public void DegenerateBasisIsReportedNotSilentlyUsed()
        {
            var zeroFov = new ScaleBarMath.CameraRayBasis(
                default, new ScaleBarMath.Vector3d(0, 0, 1), new ScaleBarMath.Vector3d(1, 0, 0),
                new ScaleBarMath.Vector3d(0, 1, 0), 0.0, 1.0);
            Assert.False(zeroFov.IsUsable);

            var zeroAspect = new ScaleBarMath.CameraRayBasis(
                default, new ScaleBarMath.Vector3d(0, 0, 1), new ScaleBarMath.Vector3d(1, 0, 0),
                new ScaleBarMath.Vector3d(0, 1, 0), 0.5, 0.0);
            Assert.False(zeroAspect.IsUsable);

            var zeroForward = new ScaleBarMath.CameraRayBasis(
                default, default, new ScaleBarMath.Vector3d(1, 0, 0),
                new ScaleBarMath.Vector3d(0, 1, 0), 0.5, 1.0);
            Assert.False(zeroForward.IsUsable);

            Assert.True(Basis().IsUsable);
        }
    }
}
