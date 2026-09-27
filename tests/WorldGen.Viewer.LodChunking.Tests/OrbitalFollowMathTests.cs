using System;
using WorldGen.Core.Astronomy;
using WorldGen.Viewer;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// ND-153 (A11): a pálya menti (éves) kameramód időskálája és szög-segédei.
    /// A tesztek a CLAUDE.md "Tesztelési elvárások" táblájának megfelelően
    /// tisztaságot, paraméter-hatást, élesetet és a Core-ral való egyezést
    /// fognak meg (a mód determinizmus-szempontból nem kritikus út, de a
    /// befagyasztott spin-szög KÉPLETE a Core-ral azonos kell legyen).
    /// </summary>
    public class OrbitalFollowMathTests
    {
        [Fact]
        public void DaysPerSecondCompletesOneOrbitInTheRequestedWallClockTime()
        {
            double perSecond = OrbitalFollowMath.DaysPerSecond(365.25, 60.0);
            Assert.Equal(365.25 / 60.0, perSecond, 12);
            // 60 másodperc alatt PONTOSAN egy kör.
            Assert.Equal(365.25, perSecond * 60.0, 9);
        }

        [Theory]
        [InlineData(0.0, 60.0)]
        [InlineData(-1.0, 60.0)]
        [InlineData(365.25, 0.0)]
        [InlineData(365.25, -5.0)]
        [InlineData(double.NaN, 60.0)]
        public void DaysPerSecondStopsTimeOnInvalidInput(double orbitalPeriodDays, double secondsPerYear)
        {
            Assert.Equal(0.0, OrbitalFollowMath.DaysPerSecond(orbitalPeriodDays, secondsPerYear));
        }

        [Fact]
        public void DaysPerSecondScalesInverselyWithSecondsPerYear()
        {
            double fast = OrbitalFollowMath.DaysPerSecond(365.25, 30.0);
            double slow = OrbitalFollowMath.DaysPerSecond(365.25, 120.0);
            Assert.True(fast > slow);
            Assert.Equal(4.0, fast / slow, 9);
        }

        [Fact]
        public void AdvanceDaysMovesTimeForwardByRateTimesDelta()
        {
            double rate = OrbitalFollowMath.DaysPerSecond(365.25, 60.0);
            double next = OrbitalFollowMath.AdvanceDays(100.0, 1.0 / 60.0, rate, 365.25);
            Assert.Equal(100.0 + rate / 60.0, next, 12);
        }

        [Fact]
        public void AdvanceDaysIsPureForRepeatedCalls()
        {
            double rate = OrbitalFollowMath.DaysPerSecond(365.25, 60.0);
            double first = OrbitalFollowMath.AdvanceDays(7.5, 0.016, rate, 365.25);
            double second = OrbitalFollowMath.AdvanceDays(7.5, 0.016, rate, 365.25);
            Assert.Equal(first, second);
        }

        [Fact]
        public void AdvanceDaysClampsAHitchToTwoPercentOfTheYear()
        {
            double rate = OrbitalFollowMath.DaysPerSecond(365.25, 60.0);
            // 10 másodperces akadás (fordítás/GC): korlát nélkül ~61 nap lenne.
            double next = OrbitalFollowMath.AdvanceDays(0.0, 10.0, rate, 365.25);
            Assert.Equal(365.25 * OrbitalFollowMath.MaxYearFractionPerStep, next, 12);
            Assert.True(next < 365.25 * 0.05);
        }

        [Fact]
        public void AdvanceDaysDoesNotClampANormalFrame()
        {
            double rate = OrbitalFollowMath.DaysPerSecond(365.25, 60.0);
            double delta = 1.0 / 30.0; // 30 fps
            double next = OrbitalFollowMath.AdvanceDays(0.0, delta, rate, 365.25);
            Assert.Equal(delta * rate, next, 12);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.5)]
        [InlineData(double.NaN)]
        public void AdvanceDaysNeverMovesTimeBackwards(double deltaSeconds)
        {
            double rate = OrbitalFollowMath.DaysPerSecond(365.25, 60.0);
            Assert.Equal(42.0, OrbitalFollowMath.AdvanceDays(42.0, deltaSeconds, rate, 365.25));
        }

        [Fact]
        public void AdvanceDaysWithZeroRateHoldsTime()
        {
            Assert.Equal(42.0, OrbitalFollowMath.AdvanceDays(42.0, 1.0, 0.0, 365.25));
        }

        [Fact]
        public void AdvanceDaysWithoutAPeriodStillAdvancesUnclamped()
        {
            // Érvénytelen periódus: nincs mihez képest 2%-ot vágni, de a
            // léptetés maga ne haljon el csendben.
            Assert.Equal(43.0, OrbitalFollowMath.AdvanceDays(42.0, 1.0, 1.0, 0.0), 12);
        }

        [Fact]
        public void YearFractionIsZeroAtTheStartAndWrapsAtOneOrbit()
        {
            Assert.Equal(0.0, OrbitalFollowMath.YearFraction(0.0, 365.25, 0.0), 12);
            Assert.Equal(0.0, OrbitalFollowMath.YearFraction(365.25, 365.25, 0.0), 12);
            Assert.Equal(0.5, OrbitalFollowMath.YearFraction(365.25 * 1.5, 365.25, 0.0), 12);
        }

        [Fact]
        public void YearFractionStaysInUnitRangeAcrossManyOrbits()
        {
            for (int i = 0; i < 2000; i++)
            {
                double t = i * 3.7;
                double f = OrbitalFollowMath.YearFraction(t, 365.25, 0.0);
                Assert.InRange(f, 0.0, 0.9999999999);
            }
        }

        [Fact]
        public void YearFractionHandlesNegativeTime()
        {
            double f = OrbitalFollowMath.YearFraction(-0.25 * 365.25, 365.25, 0.0);
            Assert.Equal(0.75, f, 12);
        }

        [Fact]
        public void YearFractionShiftsWithOrbitalPhase()
        {
            double quarter = Math.PI / 2.0;
            Assert.Equal(0.25, OrbitalFollowMath.YearFraction(0.0, 365.25, quarter), 12);
            Assert.NotEqual(
                OrbitalFollowMath.YearFraction(10.0, 365.25, 0.0),
                OrbitalFollowMath.YearFraction(10.0, 365.25, quarter));
        }

        [Fact]
        public void YearFractionReturnsZeroOnInvalidPeriod()
        {
            Assert.Equal(0.0, OrbitalFollowMath.YearFraction(10.0, 0.0, 0.0));
            Assert.Equal(0.0, OrbitalFollowMath.YearFraction(10.0, -365.0, 0.0));
        }

        [Fact]
        public void DayOfYearMatchesTheFractionTimesThePeriod()
        {
            double t = 900.0;
            double fraction = OrbitalFollowMath.YearFraction(t, 365.25, 0.0);
            Assert.Equal(fraction * 365.25, OrbitalFollowMath.DayOfYear(t, 365.25, 0.0), 12);
            Assert.InRange(OrbitalFollowMath.DayOfYear(t, 365.25, 0.0), 0.0, 365.25);
        }

        [Fact]
        public void DayOfYearReturnsZeroOnInvalidPeriod()
        {
            Assert.Equal(0.0, OrbitalFollowMath.DayOfYear(10.0, 0.0, 0.0));
        }

        [Fact]
        public void RotationAngleMatchesTheCoreBodyFrameFormula()
        {
            // A befagyasztott spin ugyanarról a szögről indul, mint amit a Core
            // SunDirectionBodyFrame használ: ha a szögek egyeznek, a test-keretbe
            // forgatott nap-iránynak is egyeznie kell a kézzel összerakott
            // (pálya-keret + ugyanez a szög) változattal. Itt a KÉPLETET mérjük:
            // a Core `rotationPhase0 + 2*pi*t/rotationPeriod`-ot használ.
            const double rotationPeriod = 1.0, phase0 = 0.31, t = 17.125;
            double expected = phase0 + 2.0 * Math.PI * (t / rotationPeriod);
            Assert.Equal(expected, OrbitalFollowMath.RotationAngle(t, rotationPeriod, phase0), 12);
        }

        [Fact]
        public void RotationAngleAdvancesFullTurnPerRotationPeriod()
        {
            double a = OrbitalFollowMath.RotationAngle(0.0, 1.0, 0.0);
            double b = OrbitalFollowMath.RotationAngle(1.0, 1.0, 0.0);
            Assert.Equal(2.0 * Math.PI, b - a, 12);
        }

        [Fact]
        public void RotationAngleFallsBackToThePhaseOnInvalidPeriod()
        {
            Assert.Equal(0.31, OrbitalFollowMath.RotationAngle(5.0, 0.0, 0.31));
            Assert.Equal(0.31, OrbitalFollowMath.RotationAngle(5.0, -1.0, 0.31));
        }

        [Fact]
        public void WrapAngleFoldsIntoUnitTurn()
        {
            double twoPi = 2.0 * Math.PI;
            Assert.Equal(0.0, OrbitalFollowMath.WrapAngle(0.0), 12);
            Assert.Equal(1.0, OrbitalFollowMath.WrapAngle(1.0 + 3.0 * twoPi), 9);
            Assert.Equal(twoPi - 1.0, OrbitalFollowMath.WrapAngle(-1.0), 9);
            Assert.InRange(OrbitalFollowMath.WrapAngle(twoPi), 0.0, 1e-9);
        }

        [Fact]
        public void WrapAngleIsBoundedForLargeAndInvalidInput()
        {
            Assert.InRange(OrbitalFollowMath.WrapAngle(1.0e9), 0.0, 2.0 * Math.PI);
            Assert.Equal(0.0, OrbitalFollowMath.WrapAngle(double.NaN));
            Assert.Equal(0.0, OrbitalFollowMath.WrapAngle(double.PositiveInfinity));
            Assert.Equal(0.0, OrbitalFollowMath.WrapAngle(double.NegativeInfinity));
        }

        /// <summary>
        /// ND-153 SZÍV-ÁLLÍTÁSA: a befagyasztott spin mellett a kiírt szám
        /// (szub-napponti SZÉLESSÉG) IGAZ marad, mert az csak a pálya-szögtől
        /// és a dőléstől függ - a spin-szög nem mozgatja. A HOSSZÚSÁG viszont
        /// mozdul, ezért azt a panel nem írja ki.
        /// </summary>
        [Fact]
        public void SubsolarLatitudeIsSpinIndependentWhileLongitudeIsNot()
        {
            const double orbitalPeriod = 365.25, tilt = 23.44 * Math.PI / 180.0, t = 120.0;

            double LatitudeWithSpin(double spinAngle)
            {
                OrbitalMechanics.SunDirectionOrbitalFrame(t, orbitalPeriod, 0.0,
                    out double ox, out double oy, out double oz);
                // A Core BodyOrientationMatrix-ával egyenértékű, kézi
                // pálya->test forgatás: R_spin^T * R_tilt^T.
                double cs = Math.Cos(spinAngle), ss = Math.Sin(spinAngle);
                double ct = Math.Cos(tilt), st = Math.Sin(tilt);
                // R_tilt^T (X körüli -tilt)
                double x1 = ox, y1 = ct * oy + st * oz, z1 = -st * oy + ct * oz;
                // R_spin^T (Z körüli -spin)
                double x2 = cs * x1 + ss * y1, y2 = -ss * x1 + cs * y1, z2 = z1;
                OrbitalMechanics.SubsolarPoint(x2, y2, z2, out double lat, out _);
                return lat;
            }

            double latFrozen = LatitudeWithSpin(0.0);
            double latSpinning = LatitudeWithSpin(2.0);
            Assert.Equal(latFrozen, latSpinning, 12);
            // ...és a szélesség tényleg a dőlés sávjában van, nem nulla.
            Assert.InRange(Math.Abs(latFrozen), 1.0e-6, tilt + 1.0e-12);
        }

        /// <summary>
        /// Az évszakos jel ellenőrzése: egy teljes pálya-kör alatt a
        /// szub-napponti szélesség ±dőlés között oszcillál (ez az, aminek a
        /// pálya menti módban a terminátor-vándorláson látszania kell).
        /// </summary>
        [Fact]
        public void SubsolarLatitudeSweepsTheFullTiltRangeOverOneOrbit()
        {
            const double orbitalPeriod = 365.25, rotationPeriod = 1.0;
            const double tilt = 23.44 * Math.PI / 180.0;
            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < 365; i++)
            {
                OrbitalMechanics.SunDirectionBodyFrame(
                    i, orbitalPeriod, rotationPeriod, tilt, 0.0, 0.0,
                    out double x, out double y, out double z);
                OrbitalMechanics.SubsolarPoint(x, y, z, out double lat, out _);
                if (lat < min) min = lat;
                if (lat > max) max = lat;
            }
            Assert.InRange(max, tilt - 0.01, tilt + 1.0e-12);
            Assert.InRange(min, -tilt - 1.0e-12, -tilt + 0.01);
        }
    }
}
