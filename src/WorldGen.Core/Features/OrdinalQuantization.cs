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
    /// folytonos metrikákra készül - <see cref="FeatureMetrics.
    /// HabitabilityFraction"/> (World), <see cref="FeatureMetrics.
    /// CoastalComplexity"/> (Continent), és 2026-09-19 óta <see
    /// cref="FeatureMetrics.SoilFertility"/> (Region, ND-117). A spec többi
    /// ordinális mezője (Climate variability, Tectonic activity, Volcanism,
    /// Population support, Biodiversity potential, Flood frequency
    /// stb.) BLOKKOLT marad, mert nincs még alattuk kiszámolható folytonos
    /// metrika (légkör-/tektonika-aktivitás modul hiányzik) - ezekhez
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
        /// ND-09 kalibráció **v2 (2026-09-19)**: N=500 világ (seed=1..500),
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
        ///
        /// MIÉRT v2. A képlet NEM változott — a VILÁGOK igen. Az ND-52
        /// (másodlagos domborzat-zaj, 2026-09-07) minden pozícióra
        /// megváltoztatta a `BaseElevation` kimenetét, ami eltolta a
        /// Habitability-eloszlást is. A v1 értékei ettől kezdve egy már nem
        /// létező világgenerátorhoz tartoztak. Bizonyíték: ugyanaz a
        /// CLI-parancs v1-ben 14947, most 14895 kontinens-mintát adott.
        ///
        /// v1 (2026-09-05): 0.715299, 0.753081, 0.782376, 0.816671
        ///
        /// FIGYELEM: ezt az elavulást SEMMILYEN teszt nem fogta meg, mert az
        /// OrdinalQuantizationTests szintetikus küszöbökkel ({1,2,3,4})
        /// dolgozik, a kalibrált értékeket pedig N=500 világ nélkül nem lehet
        /// ellenőrizni (a futás ~30 perc). A kalibrációt ezért KÉZZEL kell
        /// újrafuttatni minden olyan változás után, ami az elevációs láncot
        /// érinti — ez nem automatizált kapu.
        /// </summary>
        public static readonly double[] HabitabilityThresholds =
        {
            0.702860, 0.747966, 0.786213, 0.823878,
        };

        /// <summary>
        /// ND-09 kalibráció **v2 (2026-09-19)**: ugyanaz az N=500 világ, de a
        /// MINTA minden (legalább 5 tile méretű) kontinens <see
        /// cref="FeatureMetrics.CoastalComplexity"/>-értéke (nem világonként
        /// egy érték - kontinensenként, összesen 14895 kontinens-minta),
        /// ugyanazzal a CLI-paranccsal. Az újrakalibrálás oka ugyanaz, mint a
        /// Habitability-nél (ld. ott).
        ///
        /// v1 (2026-09-05, 14947 minta): 2.449490, 2.828427, 3.544745, 5.467934
        /// Az első két vágópont változatlan — a felső kettő mozdult.
        /// </summary>
        public static readonly double[] CoastalComplexityThresholds =
        {
            2.449490, 2.828427, 3.528211, 5.239956,
        };

        /// <summary>
        /// ND-117 kalibráció **v2** (ND-127, 2026-09-21): N=500 világ, a minta
        /// minden ÖSSZEVONT (`MergeWatershedsIntoRegions`) RÉGIÓ <see
        /// cref="FeatureMetrics.SoilFertility"/>-értéke — összesen 23 492
        /// régió-minta —, a
        /// `dotnet run --project tools/WorldGen.Cli -- calibrate-ordinals
        /// --count 500 --plates 20 --level 6 --water 0.65 --soil true`
        /// paranccsal. Régió-szintű, mert a "Soil fertility" a §2.3 RÉGIÓ-
        /// panel mezője.
        ///
        /// MIÉRT KELLETT ÚJRA (v1 → v2): a v1 populációja a ≥5 tile-os NYERS
        /// VÍZGYŰJTŐ volt (192 337 minta), az ND-127 óta viszont a panel
        /// összevont régiót mutat — nagyobb halmaz, tehát simább átlag. A két
        /// eloszlás UGYANAZON az 500 világon különbözik: v1 (vízgyűjtő)
        /// p20/p80 = 0,1946/0,2596, v2 (összevont régió) 0,1577/0,2302. A
        /// v2-populáció MEDIÁN fölötti negyede (p40 = 0,1940) épp a v1 p20
        /// vágópontja alatt van — a régi küszöbökkel tehát az összevont
        /// régiók ~40%-a esne a legalsó kvintilisbe a 20% helyett. A
        /// kvantálás akkor mond igazat, ha a küszöbök populációja UGYANAZ,
        /// amit a panel osztályoz.
        ///
        /// SZŰK ELOSZLÁS, tudatosan rögzítve: a négy vágópont 0.1577 és
        /// 0.2302 közé esik, tehát a középső három sáv keskeny (a p20..p80
        /// tartomány szélessége 0.073). A panelen ez azt jelenti, hogy a
        /// metrika kis változása is sávot lépthet — a kvintilis-felosztás
        /// ettől még helyes (definíció szerint egyenlő gyakoriságú sávokat
        /// ad), de a sáv-váltás NEM jelent nagy fizikai különbséget.
        ///
        /// ÚJRAKALIBRÁLANDÓ, ha a SoilFertility képlete, a RegolithProfile
        /// MVP-je (ND-117), az elevációs lánc VAGY a régió-definíció
        /// (ND-127: `RegionTargetLandSharePercent`) változik. Ha a
        /// `minerality` tag valaha bekerül a képletbe, ez a kalibráció
        /// ÉRVÉNYÉT VESZTI.
        /// </summary>
        public static readonly double[] SoilFertilityThresholds =
        {
            0.157734, 0.193976, 0.215019, 0.230240,
        };
    }
}
