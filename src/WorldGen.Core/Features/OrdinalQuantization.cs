using System;

namespace WorldGen.Core.Features
{
    /// <summary>5-sávos ordinális skála (docs/01-architecture.md §2 - "Low".."Exceptional" jellegű panel-mezők).</summary>
    public enum OrdinalLevel { Low, Moderate, High, VeryHigh, Exceptional }

    /// <summary>
    /// M8 ordinális kvantálás (§2.4, docs/04-decisions.md ND-09): egy
    /// folytonos metrikát (pl. Habitability 0..1) 5 sávra bont, ELŐRE
    /// KALIBRÁLT (nem futásidőben, világonként újraszámolt) küszöbökkel -
    /// enélkül egyetlen világ szélsőséges értéke eltolná a skálát, és a
    /// "High" jelző világonként mást jelentene (I4 sérülne: a panel-mező
    /// nem lenne összehasonlítható világok között).
    ///
    /// HATÓKÖR (ND-09, tudatosan szűkítve): a kalibráció CSAK a MÁR LÉTEZŐ
    /// folytonos metrikákra készült - <see cref="FeatureMetrics.
    /// HabitabilityFraction"/> (World) és <see cref="FeatureMetrics.
    /// CoastalComplexity"/> (Continent). A spec többi ordinális mezője
    /// (Climate variability, Tectonic activity, Volcanism, Population
    /// support, Biodiversity potential, Flood frequency, Soil fertility
    /// stb.) BLOKKOLT marad, mert nincs még alattuk kiszámolható folytonos
    /// metrika (soil-/légkör-/tektonika-aktivitás modul hiányzik) - ezekhez
    /// ÚJ kalibráció kell, amint a metrika elkészül.
    /// </summary>
    public static class OrdinalQuantization
    {
        public static string LevelName(OrdinalLevel level) => level switch
        {
            OrdinalLevel.Low => "Low",
            OrdinalLevel.Moderate => "Moderate",
            OrdinalLevel.High => "High",
            OrdinalLevel.VeryHigh => "Very High",
            _ => "Exceptional",
        };

        /// <summary>
        /// 5 sávra bont 4 vágóponttal (monoton növekvőnek KELL lenniük - ezt a
        /// kalibráció, nem ez a függvény, garantálja). <paramref name="value"/>
        /// az első küszöb ALATT "Low", az utolsó fölött "Exceptional".
        /// </summary>
        public static OrdinalLevel Quantize(double value, double[] thresholds)
        {
            if (thresholds == null || thresholds.Length != 4)
                throw new ArgumentException("Pontosan 4 küszöbérték szükséges (5 sáv: Low..Exceptional).", nameof(thresholds));

            if (value < thresholds[0]) return OrdinalLevel.Low;
            if (value < thresholds[1]) return OrdinalLevel.Moderate;
            if (value < thresholds[2]) return OrdinalLevel.High;
            if (value < thresholds[3]) return OrdinalLevel.VeryHigh;
            return OrdinalLevel.Exceptional;
        }

        /// <summary>
        /// ND-09 kalibráció v1 (2026-09-05): N=500 világ (seed=1..500),
        /// plateCount=20, level=6, targetWaterFraction=0.65, Föld-analóg
        /// klíma (orbitalPeriodDays=365.25, rotationPeriodDays=1.0,
        /// axialTiltDegrees=23.44, dayT=0) - a
        /// `dotnet run --project tools/WorldGen.Cli -- calibrate-ordinals
        /// --count 500 --plates 20 --level 6 --water 0.65` paranccsal
        /// (tools/WorldGen.Cli/OrdinalCalibration.cs) generálva, kvintilis-
        /// vágópontok (20/40/60/80 percentilis, nearest-rank módszer) a
        /// World-szintű <see cref="FeatureMetrics.HabitabilityFraction"/>
        /// eloszlásán. ÚJRAKALIBRÁLANDÓ, ha a HabitabilityFraction
        /// képlete/sávhatárai vagy a fenti referencia-paraméterek
        /// (plateCount/level/targetWaterFraction) változnak.
        /// </summary>
        public static readonly double[] HabitabilityThresholds =
        {
            0.715299, 0.753081, 0.782376, 0.816671,
        };

        /// <summary>
        /// ND-09 kalibráció v1 (2026-09-05): ugyanaz az N=500 világ, de a
        /// MINTA minden (legalább 5 tile méretű) kontinens <see
        /// cref="FeatureMetrics.CoastalComplexity"/>-értéke (nem világonként
        /// egy érték - kontinensenként, összesen 14947 kontinens-minta),
        /// ugyanazzal a CLI-paranccsal.
        /// </summary>
        public static readonly double[] CoastalComplexityThresholds =
        {
            2.449490, 2.828427, 3.544745, 5.467934,
        };
    }
}
