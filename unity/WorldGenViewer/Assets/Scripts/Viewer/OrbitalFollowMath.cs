using System;

namespace WorldGen.Viewer
{
    /// <summary>
    /// ND-153: a pálya menti (éves) kameramód időskálája és szög-segédei.
    /// Az <see cref="OrbitSurfaceMath"/>/<see cref="ScaleBarMath"/> mintájára
    /// SZÁNDÉKOSAN nulla UnityEngine-referenciával — így Unity Editor nélkül,
    /// `dotnet test` alól közvetlenül tesztelhető (ld.
    /// tests/WorldGen.Viewer.LodChunking.Tests/OrbitalFollowMathTests.cs).
    ///
    /// Tisztán vizuális/UX matematika, NEM szimulációs kritikus út: nincs
    /// I1/I2-érintettség (a `Math.*` hívások itt megengedettek, a kimenet
    /// sosem kerül checkpointba vagy világadatba).
    /// </summary>
    public static class OrbitalFollowMath
    {
        /// <summary>Alapértelmezett valós idő egy teljes pálya-körre.</summary>
        public const double DefaultSecondsPerYear = 60.0;

        /// <summary>
        /// Egy képkockára eső előrehaladás felső korlátja az év arányában.
        /// Egy fordítási/GC-akadás (nagy <c>Time.deltaTime</c>) így nem
        /// ugraszt évszakot, és a hőmodell cél-tickje sem lő el.
        /// </summary>
        public const double MaxYearFractionPerStep = 0.02;

        private const double TwoPi = 2.0 * Math.PI;

        /// <summary>
        /// Napi időlépték ahhoz, hogy egy teljes pálya-kör <paramref name="secondsPerYear"/>
        /// valós másodperc alatt teljen le. Nem-pozitív bemenetre 0-t ad
        /// (megállított idő), nem osztunk nullával.
        /// </summary>
        public static double DaysPerSecond(double orbitalPeriodDays, double secondsPerYear)
        {
            if (!(orbitalPeriodDays > 0.0) || !(secondsPerYear > 0.0)) return 0.0;
            return orbitalPeriodDays / secondsPerYear;
        }

        /// <summary>
        /// A modellidő előreléptetése egy képkockával, a
        /// <see cref="MaxYearFractionPerStep"/> korláttal. A visszaadott érték
        /// monoton nem csökkenő (negatív <paramref name="deltaSeconds"/> nem
        /// forgatja vissza az időt).
        /// </summary>
        public static double AdvanceDays(
            double currentTimeDays, double deltaSeconds, double daysPerSecond, double orbitalPeriodDays)
        {
            if (!(deltaSeconds > 0.0) || !(daysPerSecond > 0.0)) return currentTimeDays;
            double step = deltaSeconds * daysPerSecond;
            if (orbitalPeriodDays > 0.0)
            {
                double cap = orbitalPeriodDays * MaxYearFractionPerStep;
                if (step > cap) step = cap;
            }
            return currentTimeDays + step;
        }

        /// <summary>
        /// Hol tartunk a pályán: [0,1) arány, ahol 0 a <paramref name="orbitalPhase0"/>
        /// szerinti kezdet. Érvénytelen periódusra 0.
        /// </summary>
        public static double YearFraction(double timeDays, double orbitalPeriodDays, double orbitalPhase0)
        {
            if (!(orbitalPeriodDays > 0.0)) return 0.0;
            double fraction = timeDays / orbitalPeriodDays + orbitalPhase0 / TwoPi;
            fraction -= Math.Floor(fraction);
            // A Math.Floor utáni 1.0 (nagyon kicsi negatív maradéknál) kizárva:
            // a hívó [0,1)-re számít (pl. százalék-kiírás, sáv-rajzolás).
            return fraction >= 1.0 ? 0.0 : fraction;
        }

        /// <summary>A pálya-körön eltelt napok száma az aktuális körön belül.</summary>
        public static double DayOfYear(double timeDays, double orbitalPeriodDays, double orbitalPhase0)
        {
            if (!(orbitalPeriodDays > 0.0)) return 0.0;
            return YearFraction(timeDays, orbitalPeriodDays, orbitalPhase0) * orbitalPeriodDays;
        }

        /// <summary>
        /// A bolygó test-keret forgási szöge egy adott időpontban — ugyanaz a
        /// képlet, amit a Core <c>OrbitalMechanics.SunDirectionBodyFrame</c>
        /// használ, hogy a befagyasztott spin ugyanarról a szögről induljon,
        /// mint amin a tengelyforgásos mód épp állt (nincs ugrás módváltáskor).
        /// </summary>
        public static double RotationAngle(double timeDays, double rotationPeriodDays, double rotationPhase0)
        {
            if (!(rotationPeriodDays > 0.0)) return rotationPhase0;
            return rotationPhase0 + TwoPi * (timeDays / rotationPeriodDays);
        }

        /// <summary>Szög [0,2pi)-be hajtva — a naplózás/kiírás olvashatóságáért.</summary>
        public static double WrapAngle(double angle)
        {
            if (double.IsNaN(angle) || double.IsInfinity(angle)) return 0.0;
            double wrapped = angle - TwoPi * Math.Floor(angle / TwoPi);
            if (wrapped < 0.0) wrapped = 0.0;
            return wrapped >= TwoPi ? 0.0 : wrapped;
        }
    }
}
