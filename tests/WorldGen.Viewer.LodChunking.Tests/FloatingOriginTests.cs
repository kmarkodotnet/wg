using System;
using WorldGen.Core;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// ND-19 (A12) — a floating origin matematikai magja. A CLAUDE.md
    /// teszt-táblázatának minden sora szerepel: ismert-válasz (az IEEE-754
    /// float32 ULP-je zárt formulából), tisztaság, minden paraméter érdemben
    /// hat, eloszlás/plauzibilitás (a MÉRT precízió-nyereség), és élesetek.
    /// </summary>
    public class FloatingOriginTests
    {
        // A viewer jelenlegi léptéke: PlanetGridMesh.radius = 100 Unity-egység,
        // a fizikai sugár PlanetConstants.RadiusMeters (7 420 000 m).
        private const double ViewerRadiusUnits = 100.0;

        private static double MetersPerUnit(double radiusUnits) => PlanetConstants.RadiusMeters / radiusUnits;

        // ---------------------------------------------------------------
        // Ismert válasz: a float32 ULP zárt formulából
        // ---------------------------------------------------------------

        [Theory]
        [InlineData(1.0, 1.0 / 8388608.0)]          // [1,2) oktáv: 2^-23
        [InlineData(1.9999, 1.0 / 8388608.0)]       // ugyanaz az oktáv
        [InlineData(2.0, 2.0 / 8388608.0)]          // [2,4)
        [InlineData(100.0, 64.0 / 8388608.0)]       // [64,128) — a viewer felszíne
        [InlineData(0.5, 0.5 / 8388608.0)]          // [0.5,1)
        [InlineData(-100.0, 64.0 / 8388608.0)]      // előjel-független
        [InlineData(7420000.0, 4194304.0 / 8388608.0)] // [2^22,2^23) — a valós sugár méterben
        public void Float32UlpMatchesTheClosedFormExponentStep(double magnitude, double expected)
        {
            Assert.Equal(expected, FloatingOrigin.Float32Ulp(magnitude));
        }

        /// <summary>
        /// Független ellenőrzés: az ULP ténylegesen a két szomszédos float32
        /// érték távolsága. A `BitConverter`-es szomszéd-lépés nem használja
        /// a tesztelt kódot, tehát valódi külső referencia.
        /// </summary>
        [Theory]
        [InlineData(1.0)]
        [InlineData(100.0)]
        [InlineData(0.125)]
        [InlineData(7420000.0)]
        [InlineData(1e-20)]
        public void Float32UlpEqualsTheDistanceToTheNextRepresentableFloat(double magnitude)
        {
            float f = (float)magnitude;
            float next = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(f) + 1);
            // A szomszéd-lépés és a számolt ULP BITRE azonos (mindkettő
            // 2-hatvány), ezért pontos egyenlőséget várunk el.
            Assert.Equal((double)next - f, FloatingOrigin.Float32Ulp(magnitude));
        }

        // ---------------------------------------------------------------
        // A MÉRÉS, amiért ND-19 egyáltalán létezik
        // ---------------------------------------------------------------

        /// <summary>
        /// A float32 felszíni pozíció-hibája LÉPTÉK-INVARIÁNS: a modell-sugár
        /// megváltoztatása (100 ↔ 7 420 000 Unity-egység) NEM javít és nem
        /// romlik érdemben — ugyanaz a néhány deciméteres fizikai kvantálás.
        /// Ez zárja ki azt a (kézenfekvő, de hibás) következtetést, hogy a
        /// jelenlegi kis Unity-lépték megvéd a precíziós problémától.
        /// </summary>
        [Fact]
        public void SurfaceQuantizationIsScaleInvariant()
        {
            double smallScale = FloatingOrigin.ResolutionMeters(ViewerRadiusUnits, MetersPerUnit(ViewerRadiusUnits));
            double realScale = FloatingOrigin.ResolutionMeters(
                PlanetConstants.RadiusMeters, MetersPerUnit(PlanetConstants.RadiusMeters));

            // Mindkettő a 2^-23 … 2^-22 relatív sávban van (a bináris oktávon
            // belüli elhelyezkedés az egyetlen különbség), tehát a fizikai
            // kvantálás legfeljebb 2x-es faktoron belül azonos.
            Assert.InRange(smallScale, 0.4, 1.0);
            Assert.InRange(realScale, 0.4, 1.0);
            Assert.InRange(smallScale / realScale, 0.5, 2.0);
        }

        /// <summary>
        /// A NYERESÉG mérése: ugyanaz a felszíni pont, abszolút emittálva
        /// (origó = bolygóközép) és egy 1 km-es lokális patch origójához
        /// képest emittálva. A javulás nagyságrendje az, amiért a
        /// méterszintű közeli zoom (A11/M9) egyáltalán lehetséges lesz.
        /// </summary>
        [Fact]
        public void AnchoringToANearbyOriginImprovesSurfacePrecisionByOrdersOfMagnitude()
        {
            double metersPerUnit = MetersPerUnit(ViewerRadiusUnits);
            double absolute = FloatingOrigin.ResolutionMeters(ViewerRadiusUnits, metersPerUnit);

            // 1 km-es lokális patch a 7420 km-es bolygón: 1000 / 74200 egység.
            double patchUnits = 1000.0 / metersPerUnit;
            double anchored = FloatingOrigin.ResolutionMeters(patchUnits, metersPerUnit);

            Assert.True(anchored * 1000.0 < absolute,
                $"az origó-relatív felbontás ({anchored} m) nem legalább 1000x finomabb az abszolútnál ({absolute} m)");
            Assert.True(anchored < 1e-3, $"1 km-es patchen 1 mm alatti felbontás elvárt, mért: {anchored} m");
        }

        /// <summary>
        /// A ténylegesen VÁLASZTOTT origóval mért, végponttól végpontig tartó
        /// hiba: egy felszíni pont abszolút float32 kerekítése vs. az
        /// origó-relatív kerekítés, ugyanarra a pontra. Nem formula, hanem a
        /// kivonás-majd-cast út tényleges eredménye.
        /// </summary>
        [Fact]
        public void LocalRoundTripErrorIsFarSmallerThanTheAbsoluteOne()
        {
            // Egy tetszőleges, NEM rács-illesztett felszíni pont.
            double r = ViewerRadiusUnits + 0.000123456789;
            double x = r * 0.5773502691896258, y = r * 0.5773502691896258, z = r * 0.5773502691896258;

            double absoluteError = Error(x, y, z, (float)x, (float)y, (float)z);

            // A kamera 0,01 egység (≈740 m) magasan, ugyanabban az irányban.
            double camScale = (ViewerRadiusUnits + 0.01) / r;
            bool rebased = FloatingOrigin.TryAdvance(
                default, x * camScale, y * camScale, z * camScale, 0.01, ViewerRadiusUnits, out RenderOrigin origin);
            Assert.True(rebased);

            origin.ToLocal(x, y, z, out float lx, out float ly, out float lz);
            origin.ToAbsolute(lx, ly, lz, out double ax, out double ay, out double az);
            double localError = Math.Sqrt((ax - x) * (ax - x) + (ay - y) * (ay - y) + (az - z) * (az - z));

            Assert.True(localError * 100.0 < absoluteError,
                $"lokális hiba {localError}, abszolút hiba {absoluteError} — nincs legalább 100x javulás");
        }

        private static double Error(double x, double y, double z, float fx, float fy, float fz)
        {
            double dx = fx - x, dy = fy - y, dz = fz - z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // ---------------------------------------------------------------
        // Rács-illesztés és rebase-hiszterézis
        // ---------------------------------------------------------------

        [Fact]
        public void SnapPutsTheOriginOnTheLatticeWithinHalfACell()
        {
            const double cell = 0.25;
            RenderOrigin o = FloatingOrigin.Snap(100.37, -3.9, 12.5, cell);

            Assert.Equal(0.0, o.X / cell - Math.Floor(o.X / cell));
            Assert.Equal(0.0, o.Y / cell - Math.Floor(o.Y / cell));
            Assert.Equal(0.0, o.Z / cell - Math.Floor(o.Z / cell));
            Assert.True(o.ChebyshevDistanceTo(100.37, -3.9, 12.5) <= cell * 0.5 + 1e-12);
            Assert.Equal(cell, o.CellUnits);
        }

        /// <summary>
        /// Tisztaság (CLAUDE.md teszt-tábla): ugyanaz a bemenet mindig ugyanaz
        /// a kimenet, és a `Snap` nem függ attól, melyik oldalról érkezik a
        /// kamera — a cellahatáron is egyetlen válasza van.
        /// </summary>
        [Fact]
        public void SnapIsPureAndMonotoneAcrossTheCellBoundary()
        {
            const double cell = 0.5;
            RenderOrigin a = FloatingOrigin.Snap(0.25, 0.25, 0.25, cell);
            RenderOrigin b = FloatingOrigin.Snap(0.25, 0.25, 0.25, cell);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Z, b.Z);

            // A pontos fél (0.25 = cellahatár középpontja a floor+0.5 alakban)
            // FELFELÉ kerekít, és a szomszédos minták monoton növekvő origót adnak.
            double previous = double.NegativeInfinity;
            for (int i = -8; i <= 8; i++)
            {
                double camera = i * cell * 0.25;
                double snapped = FloatingOrigin.Snap(camera, 0.0, 0.0, cell).X;
                Assert.True(snapped >= previous, $"nem monoton: camera={camera}, snapped={snapped}, previous={previous}");
                previous = snapped;
            }
        }

        /// <summary>
        /// A hiszterézis LÉNYEGE: a cellahatár mentén oda-vissza mozgó kamera
        /// nem indít rebase-t minden képkockán. Enélkül minden rebase teljes
        /// geometria-újraépítés lenne — ez az a hibaosztály, ami csak élő
        /// Play-ben, teljesítmény-összeomlásként jelentkezne.
        /// </summary>
        [Fact]
        public void CameraJitterAtACellBoundaryDoesNotRebase()
        {
            const double altitude = 1.0;   // cell = 1.0
            double cx = 100.5, cy = 0.0, cz = 0.0;
            FloatingOrigin.TryAdvance(default, cx, cy, cz, altitude, ViewerRadiusUnits, out RenderOrigin origin);

            int rebases = 0;
            for (int i = 0; i < 200; i++)
            {
                // ±0.5 cellányi billegés a határ körül.
                double jitter = (i % 2 == 0) ? 0.5 : -0.5;
                if (FloatingOrigin.TryAdvance(origin, cx + jitter, cy, cz, altitude, ViewerRadiusUnits, out RenderOrigin next))
                {
                    rebases++;
                    origin = next;
                }
            }
            Assert.Equal(0, rebases);
        }

        [Fact]
        public void SustainedTranslationEventuallyRebases()
        {
            const double altitude = 1.0;
            FloatingOrigin.TryAdvance(default, 100.0, 0.0, 0.0, altitude, ViewerRadiusUnits, out RenderOrigin origin);

            int rebases = 0;
            for (int i = 1; i <= 100; i++)
            {
                if (FloatingOrigin.TryAdvance(origin, 100.0 + i * 0.1, 0.0, 0.0, altitude, ViewerRadiusUnits, out RenderOrigin next))
                {
                    rebases++;
                    origin = next;
                }
            }
            // 10 egység út, 1.0-es cellával, 1.5-es küszöbbel: néhány rebase,
            // nem száz (képkockánkénti) és nem nulla (soha nem követ).
            Assert.InRange(rebases, 3, 12);
            Assert.True(origin.ChebyshevDistanceTo(110.0, 0.0, 0.0) <= FloatingOrigin.RebaseFactor * 1.0);
        }

        /// <summary>
        /// Minden paraméter érdemben hat (CLAUDE.md teszt-tábla): a magasság a
        /// rács-léptéken keresztül, a kamerapozíció az origón keresztül.
        /// </summary>
        [Fact]
        public void AltitudeChangeAloneForcesANewCellAndThusARebase()
        {
            FloatingOrigin.TryAdvance(default, 100.0, 0.0, 0.0, 4.0, ViewerRadiusUnits, out RenderOrigin high);
            Assert.Equal(4.0, high.CellUnits);

            bool rebased = FloatingOrigin.TryAdvance(high, 100.0, 0.0, 0.0, 0.5, ViewerRadiusUnits, out RenderOrigin low);
            Assert.True(rebased, "a zoom másik bináris oktávba lépett, ezért új origó kell");
            Assert.Equal(0.5, low.CellUnits);
        }

        [Theory]
        [InlineData(4.0, 4.0)]
        [InlineData(5.9, 4.0)]
        [InlineData(1.0, 1.0)]
        [InlineData(0.75, 0.5)]
        [InlineData(0.1, 0.0625)]
        [InlineData(1024.0, 1024.0)]
        public void RecommendedCellIsTheLargestPowerOfTwoNotAboveTheAltitude(double altitude, double expected)
        {
            Assert.Equal(expected, FloatingOrigin.RecommendedCellUnits(altitude));
        }

        // ---------------------------------------------------------------
        // Regresszió: az origó SOSEM lehet rosszabb az abszolútnál
        // ---------------------------------------------------------------

        /// <summary>
        /// ÉLŐ PLAY-MÉRÉSBŐL SZÁRMAZÓ REGRESSZIÓ (2026-09-27). A felső
        /// cella-korlát nélkül a bolygó-nézeti magasságokon (a jelenetben
        /// 200-364 egység egy 100-as sugarú bolygó fölött) a javasolt
        /// rács-lépés 128-256 lett: az origó MESSZEBB került a kamerától,
        /// mint maga a bolygóközép, és a naplózott nyereség-faktor 0,5 ill.
        /// 0,3 volt — vagyis az "origó-relatív" út ROSSZABB volt az
        /// abszolútnál. Ez a teszt a teljes használt magasság-tartományon
        /// kiköti, hogy a nyereség soha nem eshet 1 alá.
        /// </summary>
        [Theory]
        [InlineData(700.0)]   // a jelenet maxDistance-e körül
        [InlineData(364.4)]   // a naplóban mért, hibát adó magasság
        [InlineData(199.98)]  // a naplóban mért induló magasság
        [InlineData(100.0)]   // pontosan a bolygósugár
        [InlineData(64.0)]
        [InlineData(10.0)]
        [InlineData(0.33)]    // a mai minimum (nearClip=0.3)
        [InlineData(0.001)]
        public void AnchoredResolutionIsNeverWorseThanTheAbsoluteOne(double altitude)
        {
            double metersPerUnit = MetersPerUnit(ViewerRadiusUnits);
            // A kamera a felszín felett, tetszőleges irányban.
            double r = ViewerRadiusUnits + altitude;
            const double k = 0.5773502691896258;
            FloatingOrigin.TryAdvance(default, r * k, r * k, r * k, altitude, ViewerRadiusUnits,
                out RenderOrigin origin);

            double anchored = FloatingOrigin.ResolutionMeters(
                FloatingOrigin.LocalRadiusUnits(origin, ViewerRadiusUnits), metersPerUnit);
            double absolute = FloatingOrigin.ResolutionMeters(ViewerRadiusUnits, metersPerUnit);

            Assert.True(anchored <= absolute,
                $"magassag={altitude}: origo-relativ {anchored} m ROSSZABB az abszolut {absolute} m-nel " +
                $"(origo={origin})");
        }

        [Fact]
        public void PlanetViewAltitudesUseThePlanetCentreOrigin()
        {
            // 364 egység magasan a teljes gömb látszik: nincs mit nyerni az
            // eltolással, tehát bolygóközép az origó.
            FloatingOrigin.TryAdvance(default, 464.4, 0.0, 0.0, 364.4, ViewerRadiusUnits, out RenderOrigin far);
            Assert.True(far.IsPlanetCenter);

            // Felszín közelében viszont kamera-illesztett.
            FloatingOrigin.TryAdvance(default, 100.33, 0.0, 0.0, 0.33, ViewerRadiusUnits, out RenderOrigin near);
            Assert.False(near.IsPlanetCenter);
        }

        /// <summary>
        /// A két üzemmód közötti faktor-2 hiszterézis-sáv: a küszöb (= a
        /// bolygósugár) körül lebegő kamera nem billeghet módot
        /// képkockánként. Ugyanaz a hibaosztály, mint a cellahatáron.
        /// </summary>
        [Fact]
        public void HoveringAtTheModeThresholdDoesNotFlipModesEveryFrame()
        {
            FloatingOrigin.TryAdvance(default, 200.0, 0.0, 0.0, 100.0, ViewerRadiusUnits, out RenderOrigin origin);
            Assert.True(origin.IsPlanetCenter);

            int rebases = 0;
            for (int i = 0; i < 200; i++)
            {
                // A sugár körül ±10% billegés: a kilépéshez a fél sugár alá kellene menni.
                double altitude = (i % 2 == 0) ? 110.0 : 90.0;
                if (FloatingOrigin.TryAdvance(origin, ViewerRadiusUnits + altitude, 0.0, 0.0,
                        altitude, ViewerRadiusUnits, out RenderOrigin next))
                {
                    rebases++;
                    origin = next;
                }
            }
            Assert.Equal(0, rebases);

            // A fél sugár alatt viszont ténylegesen átvált kamera-illesztettre.
            Assert.True(FloatingOrigin.TryAdvance(origin, 140.0, 0.0, 0.0, 40.0, ViewerRadiusUnits,
                out RenderOrigin anchored));
            Assert.False(anchored.IsPlanetCenter);
        }

        /// <summary>
        /// Bolygóközepű módban a rács-lépés a felső korláton PINNELT, ezért a
        /// magasság változása (ami egyébként oktávonként új cellát adna) NEM
        /// jelent rebase-t: az origó számértéke nem változik, tehát felesleges
        /// geometria-újraépítést sem kérhet.
        /// </summary>
        [Fact]
        public void ZoomingWithinPlanetViewDoesNotRebaseThePlanetCentreOrigin()
        {
            FloatingOrigin.TryAdvance(default, 800.0, 0.0, 0.0, 700.0, ViewerRadiusUnits, out RenderOrigin origin);
            Assert.True(origin.IsPlanetCenter);

            int rebases = 0;
            for (double altitude = 700.0; altitude >= 100.0; altitude -= 5.0)
            {
                if (FloatingOrigin.TryAdvance(origin, ViewerRadiusUnits + altitude, 0.0, 0.0,
                        altitude, ViewerRadiusUnits, out RenderOrigin next))
                {
                    rebases++;
                    origin = next;
                }
            }
            Assert.Equal(0, rebases);
        }

        [Theory]
        [InlineData(100.0, 64.0)]      // 100/1.5 = 66.7 -> 64
        [InlineData(1.0, 0.5)]         // 1/1.5 = 0.667 -> 0.5
        [InlineData(7420000.0, 4194304.0)]
        public void MaximumUsefulCellKeepsTheRebaseThresholdInsideTheRadius(double radius, double expected)
        {
            double cap = FloatingOrigin.MaximumUsefulCellUnits(radius);
            Assert.Equal(expected, cap);
            Assert.True(FloatingOrigin.RebaseFactor * cap <= radius);
        }

        // ---------------------------------------------------------------
        // Élesetek
        // ---------------------------------------------------------------

        [Fact]
        public void ZeroAndTinyAltitudesFallBackToTheMinimumCell()
        {
            Assert.Equal(FloatingOrigin.MinimumCellUnits, FloatingOrigin.RecommendedCellUnits(0.0));
            Assert.Equal(FloatingOrigin.MinimumCellUnits, FloatingOrigin.RecommendedCellUnits(1e-300));
            Assert.Equal(FloatingOrigin.MinimumCellUnits, FloatingOrigin.RecommendedCellUnits(-0.0));
        }

        [Fact]
        public void NegativeAltitudeIsTreatedByMagnitude()
        {
            // A kamera a modellfelszín ALATT is lehet (ND-91 pontmintás korlát),
            // ilyenkor a magasság negatív - ez nem hiba, csak nagyságrend.
            Assert.Equal(FloatingOrigin.RecommendedCellUnits(2.0), FloatingOrigin.RecommendedCellUnits(-2.0));
        }

        [Fact]
        public void NonFiniteInputsAreRejectedRatherThanSilentlyProducingGarbage()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FloatingOrigin.Float32Ulp(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FloatingOrigin.Float32Ulp(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => FloatingOrigin.RecommendedCellUnits(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FloatingOrigin.Snap(1, 2, 3, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FloatingOrigin.Snap(1, 2, 3, -1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderOrigin(double.NaN, 0, 0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FloatingOrigin.ResolutionMeters(1.0, 0.0));
        }

        [Fact]
        public void Float32UlpAtZeroIsTheDenormalStep()
        {
            Assert.Equal(FloatingOrigin.Float32DenormalStep, FloatingOrigin.Float32Ulp(0.0));
            Assert.Equal(FloatingOrigin.Float32DenormalStep, FloatingOrigin.Float32Ulp(-0.0));
        }

        /// <summary>
        /// A `default(RenderOrigin)` ÉRVÉNYTELEN, nem "bolygóközép" — ugyanaz a
        /// hibaosztály-védelem, mint az A22 `DeepTimeContext` NaN-os
        /// tengerszintjénél. Ha legálisnak tűnne, egy elfelejtett inicializálás
        /// CSENDBEN visszaállítaná az abszolút (precíziót vesztő) emittálást.
        /// </summary>
        [Fact]
        public void DefaultOriginIsInvalidWhileTheExplicitPlanetCenterIsNot()
        {
            RenderOrigin implicitOrigin = default;
            Assert.False(implicitOrigin.IsValid);
            Assert.Equal("RenderOrigin(invalid)", implicitOrigin.ToString());

            RenderOrigin center = RenderOrigin.PlanetCenter(1.0);
            Assert.True(center.IsValid);
            Assert.True(center.IsPlanetCenter);

            // Az érvénytelen origó MINDIG rebase-t kér, tehát sose marad használatban.
            Assert.True(FloatingOrigin.TryAdvance(implicitOrigin, 100.0, 0.0, 0.0, 1.0, ViewerRadiusUnits, out _));
        }

        /// <summary>
        /// A bolygóközepű origó EGYENÉRTÉKŰ a floating origin előtti,
        /// abszolút emittálással — ez a viselkedés-megtartó viszonyítási pont.
        /// </summary>
        [Fact]
        public void PlanetCenterOriginReproducesTheAbsoluteCast()
        {
            RenderOrigin center = RenderOrigin.PlanetCenter(1.0);
            const double x = 100.37, y = -3.9, z = 12.5;
            center.ToLocal(x, y, z, out float lx, out float ly, out float lz);
            Assert.Equal((float)x, lx);
            Assert.Equal((float)y, ly);
            Assert.Equal((float)z, lz);
        }

        [Fact]
        public void ToLocalAndToAbsoluteRoundTripExactlyForRepresentableOffsets()
        {
            RenderOrigin o = FloatingOrigin.Snap(100.0, 0.0, 0.0, 0.5);
            o.ToLocal(100.25, 0.5, -0.125, out float lx, out float ly, out float lz);
            o.ToAbsolute(lx, ly, lz, out double x, out double y, out double z);
            Assert.Equal(100.25, x);
            Assert.Equal(0.5, y);
            Assert.Equal(-0.125, z);
        }

        [Fact]
        public void ToStringIsCultureInvariant()
        {
            RenderOrigin o = new RenderOrigin(100.5, -0.25, 0.0, 0.5);
            string text = o.ToString();
            Assert.Contains("100.5", text);
            Assert.Contains("-0.25", text);
            Assert.Contains("cell=0.5", text);
        }
    }
}
