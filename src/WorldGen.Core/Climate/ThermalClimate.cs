using System;
using System.Collections.ObjectModel;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// Egy világ éves éghajlata a hőmodellből, a jégmaszk körkörös
    /// függésének feloldásával (ND-158). Ez az a fogyasztói adatút, amit
    /// a biome-osztályozás, a jég és a párolgás olvashat.
    /// </summary>
    public sealed class ThermalClimate
    {
        /// <summary>Az A menet (jégmentes felszíntípusok) éves statisztikája.</summary>
        public ThermalAnnualStatistics IceFree { get; }

        /// <summary>A B menet (az A jégmaszkjával felülírt felszíntípusok) éves statisztikája — EZ az autoritatív.</summary>
        public ThermalAnnualStatistics Refined { get; }

        /// <summary>Az A menetből kapott jégosztály (a B menet felszíntípusainak forrása).</summary>
        public ReadOnlyCollection<LakesIceErosion.IceClass> IceFreeClass { get; }

        /// <summary>A B menetből kapott, VÉGLEGES jégosztály cellánként.</summary>
        public ReadOnlyCollection<LakesIceErosion.IceClass> RefinedClass { get; }

        /// <summary>A B menet felszíntípusai (az A menet bemenete + <see cref="SurfaceThermalKind.Ice"/>).</summary>
        public ReadOnlyCollection<SurfaceThermalKind> RefinedKinds { get; }

        /// <summary>
        /// Azoknak a celláknak a száma, ahol a két menet jégosztálya ELTÉR.
        /// MÉRT diagnosztika, nem kapu: a menetszám akkor is kettő marad, ha ez
        /// nem nulla (lásd ND-158, „miért nem fixpont-iteráció").
        /// </summary>
        public int ReclassifiedCells { get; }

        /// <summary>Az A menet jég-vágópontjai (percentilis módban az A menet saját eloszlásából).</summary>
        public ThermalIceClassification.IceThresholds IceFreeThresholds { get; }

        /// <summary>A B menet jég-vágópontjai — EZEK az autoritáns küszöbök.</summary>
        public ThermalIceClassification.IceThresholds RefinedThresholds { get; }

        /// <summary>
        /// Igaz, ha a B menet SZÁMÍTÁSA elmaradt, mert az A menet egyetlen
        /// tartós jeget sem talált. Ilyenkor a B menet felszíntípus-térképe
        /// definíció szerint azonos az A-éval, tehát az azonos bemenetű mező
        /// BITRE ugyanazt adná — a kihagyás nem közelítés, csak a fölösleges
        /// második futás elhagyása (ND-158). A kimenet változatlan.
        /// </summary>
        public bool SecondPassSkipped { get; }

        internal ThermalClimate(ThermalAnnualStatistics iceFree, ThermalAnnualStatistics refined,
            LakesIceErosion.IceClass[] iceFreeClass, LakesIceErosion.IceClass[] refinedClass,
            SurfaceThermalKind[] refinedKinds, int reclassifiedCells, bool secondPassSkipped,
            ThermalIceClassification.IceThresholds iceFreeThresholds,
            ThermalIceClassification.IceThresholds refinedThresholds)
        {
            SecondPassSkipped = secondPassSkipped;
            IceFreeThresholds = iceFreeThresholds;
            RefinedThresholds = refinedThresholds;
            IceFree = iceFree;
            Refined = refined;
            IceFreeClass = Array.AsReadOnly(iceFreeClass);
            RefinedClass = Array.AsReadOnly(refinedClass);
            RefinedKinds = Array.AsReadOnly(refinedKinds);
            ReclassifiedCells = reclassifiedCells;
        }

        /// <summary>Hány cella esik az adott jégosztályba a VÉGLEGES (B menet) maszkban.</summary>
        public int CountRefined(LakesIceErosion.IceClass iceClass)
        {
            int n = 0;
            for (int c = 0; c < RefinedClass.Count; c++)
                if (RefinedClass[c] == iceClass) n++;
            return n;
        }
    }

    /// <summary>
    /// A 6. fázis fogyasztói adatútja (ND-158): éves éghajlat a hőmodellből,
    /// KÉT RÖGZÍTETT MENETBEN.
    ///
    /// A KÖRKÖRÖS FÜGGÉS. A <see cref="SurfaceTemperatureField"/> bemenete a
    /// felszíntípus-térkép, mert abból jön az albedó, az emisszivitás és a
    /// hőkapacitás (<see cref="ThermalModelParameters"/>). A jég viszont a
    /// hőmérséklet KIMENETE: „tartós jég ott van, ahol az éves átlag −15 °C
    /// alatt marad" (<see cref="LakesIceErosion.ClassifyIce"/>). Eddig ezt a
    /// kört a viewer úgy vágta el, hogy a solver a Build során már kész,
    /// RÉGI (analitikus <see cref="Temperature"/>-ből származó) jégmezőt kapott
    /// bemenetnek — tehát a hőmodell jege sosem a hőmodellből jött (ND-143).
    ///
    /// A FELOLDÁS. A hívó JÉGMENTES felszíntípus-térképet ad (Land / Ocean /
    /// Freshwater — ez tisztán eleváció- és tó-kérdés, nincs benne
    /// hőmérséklet). Ebből:
    /// <list type="number">
    /// <item>A menet: éves statisztika → jégosztály;</item>
    /// <item>B menet: ahol az A tartós jeget adott, a típus
    /// <see cref="SurfaceThermalKind.Ice"/> lesz → új mező → éves statisztika
    /// → VÉGLEGES jégosztály.</item>
    /// </list>
    ///
    /// MIÉRT NEM FIXPONT-ITERÁCIÓ. Egy „amíg nem változik" ciklus leállása
    /// tolerancia- és sorrendfüggő lenne, oszcilláló cellák mellett pedig akár
    /// végtelen — az I1 (bitre azonos világ) így nem tartható. A menetszám
    /// ezért KONSTANS kettő, és az eltérés MÉRVE van
    /// (<see cref="ThermalClimate.ReclassifiedCells"/>), nem elrejtve.
    ///
    /// A JÉG FELSZÍNI (Ts) éves átlagra és minimumra osztályozódik — a
    /// jégtakaró felszíni jelenség. A küszöböket az ND-159 óta a
    /// <see cref="ThermalIceClassification"/> adja: a tartós jég vágópontja az
    /// adott MENET SAJÁT eloszlásából vett percentilis, a szezonális hóé
    /// továbbra is az abszolút fagypont. A <c>permanentIcePercentile: null</c>
    /// a régi, abszolút (−15 °C) szabályra vált vissza — összehasonlításhoz.
    ///
    /// KÖVETKEZMÉNY a menetszámra: percentilis módban az A menet KONSTRUKCIÓ
    /// SZERINT talál tartós jeget, ezért az ND-158 bitazonos rövidzára
    /// (a második menet kihagyása) gyakorlatilag sosem lép be — a költség a
    /// teljes kétmenetes érték. Ez mérve van, lásd ND-159.
    /// </summary>
    public static class ThermalClimateCalculator
    {
        /// <summary>
        /// A hívó felszíntípus-térképének jégmentesítése: a
        /// <see cref="SurfaceThermalKind.Ice"/> cellák <see cref="SurfaceThermalKind.Land"/>-dé
        /// válnak. Azoknak a hívóknak, akiknek még van régi, hőmérsékletből
        /// származó jégmezőjük — így annak a HATÁSA nem szivárog be az A menetbe.
        /// </summary>
        public static SurfaceThermalKind[] IceFreeKinds(SurfaceThermalKind[] kinds)
        {
            if (kinds == null) throw new ArgumentNullException(nameof(kinds));
            var result = new SurfaceThermalKind[kinds.Length];
            for (int c = 0; c < kinds.Length; c++)
                result[c] = kinds[c] == SurfaceThermalKind.Ice ? SurfaceThermalKind.Land : kinds[c];
            return result;
        }

        public static ThermalClimate Compute(DenseGridMetrics grid, SurfaceThermalKind[] iceFreeKinds,
            double[] elevationM, double seaLevelM, ulong worldSeed, double tYears, ThermalOrbit orbit,
            ThermalModelParameters? parameters = null, int sampleDays = ThermalAnnualStatisticsCalculator.DefaultSampleDays,
            long firstDay = 0, bool useParallelLocalStep = false,
            double? permanentIcePercentile = ThermalIceClassification.DefaultPermanentIcePercentile)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (iceFreeKinds == null) throw new ArgumentNullException(nameof(iceFreeKinds));
            for (int c = 0; c < iceFreeKinds.Length; c++)
                if (iceFreeKinds[c] == SurfaceThermalKind.Ice)
                    throw new ArgumentException(
                        $"Az A menet bemenete nem tartalmazhat jeget (a(z) {c}. cella Ice) — a jég a hőmodell " +
                        "KIMENETE, nem bemenete. Használd az IceFreeKinds() metódust.", nameof(iceFreeKinds));

            ThermalAnnualStatistics iceFreeStats = RunPass(grid, iceFreeKinds, elevationM, seaLevelM,
                worldSeed, tYears, orbit, parameters, sampleDays, firstDay, useParallelLocalStep);
            ThermalIceClassification.IceThresholds iceFreeThresholds =
                ThresholdsFor(iceFreeStats, permanentIcePercentile);
            LakesIceErosion.IceClass[] iceFreeClass =
                ThermalIceClassification.ClassifyAll(iceFreeStats, iceFreeThresholds);

            int count = grid.CellCount;
            var refinedKinds = new SurfaceThermalKind[count];
            bool anyPermanentIce = false;
            for (int c = 0; c < count; c++)
            {
                bool ice = iceFreeClass[c] == LakesIceErosion.IceClass.PermanentIce;
                anyPermanentIce |= ice;
                refinedKinds[c] = ice ? SurfaceThermalKind.Ice : iceFreeKinds[c];
            }

            // Ha az A menet sehol nem talált tartós jeget, a B menet
            // felszíntípus-térképe AZONOS az A-éval, tehát ugyanaz a mező
            // ugyanabból az állapotból ugyanazt adná. A második futás
            // elhagyása így bitazonos, nem közelítés — a level-6 mérés
            // szerint viszont a felére viszi az időt (ND-158).
            ThermalAnnualStatistics refinedStats = anyPermanentIce
                ? RunPass(grid, refinedKinds, elevationM, seaLevelM, worldSeed, tYears, orbit,
                    parameters, sampleDays, firstDay, useParallelLocalStep)
                : iceFreeStats;
            ThermalIceClassification.IceThresholds refinedThresholds = anyPermanentIce
                ? ThresholdsFor(refinedStats, permanentIcePercentile)
                : iceFreeThresholds;
            LakesIceErosion.IceClass[] refinedClass = anyPermanentIce
                ? ThermalIceClassification.ClassifyAll(refinedStats, refinedThresholds)
                : (LakesIceErosion.IceClass[])iceFreeClass.Clone();

            int reclassified = 0;
            for (int c = 0; c < count; c++)
                if (iceFreeClass[c] != refinedClass[c]) reclassified++;

            return new ThermalClimate(iceFreeStats, refinedStats, iceFreeClass, refinedClass,
                refinedKinds, reclassified, !anyPermanentIce, iceFreeThresholds, refinedThresholds);
        }

        private static ThermalAnnualStatistics RunPass(DenseGridMetrics grid, SurfaceThermalKind[] kinds,
            double[] elevationM, double seaLevelM, ulong worldSeed, double tYears, ThermalOrbit orbit,
            ThermalModelParameters? parameters, int sampleDays, long firstDay, bool useParallelLocalStep)
        {
            var field = new SurfaceTemperatureField(grid, kinds, elevationM, seaLevelM, worldSeed, tYears,
                orbit, parameters);
            field.UseParallelLocalStep = useParallelLocalStep;
            var state = new ThermalSnapshot(grid.CellCount);
            return ThermalAnnualStatisticsCalculator.Compute(field, state, sampleDays, firstDay);
        }

        /// <summary>
        /// A menet jég-vágópontjai. <c>null</c> percentilis esetén a RÉGI,
        /// abszolút szabály (−15 °C éves átlag) — összehasonlításhoz és a
        /// korábbi viselkedés reprodukálásához; különben az adott menet SAJÁT
        /// eloszlásából vett percentilis (ND-159).
        /// </summary>
        private static ThermalIceClassification.IceThresholds ThresholdsFor(
            ThermalAnnualStatistics annual, double? permanentIcePercentile)
            => permanentIcePercentile.HasValue
                ? ThermalIceClassification.ComputeThresholds(annual.MeanSurfaceK, permanentIcePercentile.Value)
                : ThermalIceClassification.IceThresholds.Absolute;

        /// <summary>Jégosztály cellánként a RÉGI, abszolút küszöbbel (összehasonlításhoz).</summary>
        public static LakesIceErosion.IceClass[] ClassifyIce(ThermalAnnualStatistics annual)
        {
            if (annual == null) throw new ArgumentNullException(nameof(annual));
            return ThermalIceClassification.ClassifyAll(annual, ThermalIceClassification.IceThresholds.Absolute);
        }

        /// <summary>
        /// Biome cellánként a VÉGLEGES éves statisztikából és a csapadék-mezőből.
        ///
        /// A hőmérsékleti tengely az éves LEVEGŐ-középhőmérséklet
        /// (<see cref="ThermalAnnualStatistics.MeanAirK"/>), nem a felszíné: a
        /// Whittaker-jellegű tábla (ND-126) éves levegő-középhőmérsékletre van
        /// kalibrálva, és a vegetáció is a levegőt „érzi". A jég/tundra
        /// hidegvégét ugyanez a tengely dönti el, tehát a
        /// <see cref="BiomeClassification"/> küszöbei változatlanok.
        ///
        /// A csapadék-vágópontokat a hívó adja, EGYSZER kiszámolva
        /// (<see cref="BiomeClassification.ComputeThresholdsForVegetatedLand"/>) —
        /// az osztályozás így tiszta függvény marad.
        /// </summary>
        public static Biome[] ClassifyBiomes(ThermalAnnualStatistics annual, bool[] isOceanic,
            double[] precipitation, BiomeClassification.PrecipitationThresholds thresholds)
        {
            if (annual == null) throw new ArgumentNullException(nameof(annual));
            if (isOceanic == null) throw new ArgumentNullException(nameof(isOceanic));
            if (precipitation == null) throw new ArgumentNullException(nameof(precipitation));
            int count = annual.MeanAirK.Count;
            if (isOceanic.Length != count || precipitation.Length != count)
                throw new ArgumentException("Az óceán-maszk és a csapadék-mező mérete a cellaszámmal egyezzen.");

            var result = new Biome[count];
            for (int c = 0; c < count; c++)
                result[c] = BiomeClassification.Classify(annual.MeanAirK[c], isOceanic[c], precipitation[c], thresholds);
            return result;
        }
    }
}
