using System;
using System.Collections.Generic;
using WorldGen.Core.Hydrology;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// Jégosztályozás a hőmodell éves statisztikájából, PERCENTILIS tartós-jég
    /// küszöbbel (ND-159).
    ///
    /// MIÉRT NEM ABSZOLÚT A KÜSZÖB. A <see cref="LakesIceErosion.ClassifyIce"/>
    /// eredeti szabálya „éves átlag &lt; −15 °C → tartós jég" volt. Az ND-159
    /// mérése szerint ez a hőmodellre átvezetve NULLA jégcellát ad (a mai,
    /// analitikus úton látható 1727 helyett), mert a hőmodell abszolút szintje
    /// más: a medián éves felszíni átlag +33,7 °C, tehát a modell globálisan
    /// meleg. Az abszolút küszöb ezért nem azt méri, amit mérni akar.
    ///
    /// Ugyanaz a tanulság, mint az ND-126 csapadék-küszöbeinél és a folyó-forrás
    /// kiválasztásnál: ha a mennyiség egysége/szintje modell- és világfüggő,
    /// a küszöbnek az ELOSZLÁSBÓL kell jönnie, nem abszolút számból.
    ///
    /// MIÉRT MARAD ABSZOLÚT A SZEZONÁLIS HÓ. A <c>min &lt; 273,15 K</c> teszt
    /// azt kérdezi, hogy a víz megfagy-e valaha az adott cellán — ez VALÓDI
    /// fizikai küszöb, és a hőmodell PILLANATNYI mezője (β = 0,5 mellett
    /// −33,1 °C-ig megy) értelmesen el is éri. A „tartós jég" ezzel szemben a
    /// felhalmozódás/olvadás mérlegének proxyja, amit a modell nem számol.
    ///
    /// MIÉRT NINCS METSZET A KETTŐBŐL. Kézenfekvő volna a percentilist egy
    /// abszolút plafonnal is megvágni („a leghidegebb N%, DE csak fagypont
    /// alatt"). MÉRVE ez majdnem üres halmaz: β = 0,5 mellett a leghidegebb
    /// cella éves átlaga −1,7 °C, a 7. percentilis már ~+7 °C — egy 0 °C-os
    /// plafon néhány cellát hagyna meg. Pontosan az ND-124 hibaosztálya (ott a
    /// globális abszolút küszöb és egy részhalmaz metszete lett üres), ezért
    /// tudatosan NINCS metszet.
    ///
    /// ÁRA, EXPLICITEN: a tartós jég aránya így KONSTRUKCIÓ SZERINT ugyanaz
    /// minden világon — egy forró bolygón is lesz „jégsapka". Ezt az ND-159
    /// döntés tudomásul veszi; a modell abszolút szintjének hitelessége külön
    /// tétel (ND-42 / B14b).
    /// </summary>
    public static class ThermalIceClassification
    {
        /// <summary>
        /// A tartós jég alapértelmezett percentilise. MÉRT érték: a ma látható
        /// (analitikus úton számolt) jégtakaró a cellák <b>6,85%-a</b> level
        /// 5-ön és <b>7,03%-a</b> level 6-on ugyanazon a világon (ND-159) —
        /// a 0,07 ennek a kerek megfelelője, nem szemre választott szám.
        /// </summary>
        public const double DefaultPermanentIcePercentile = 0.07;

        /// <summary>
        /// A két jég-vágópont. Érték-típus, nincs benne állapot: a hívó
        /// EGYSZER számolja ki az eloszlásból, és változatlanul adja tovább —
        /// így az osztályozás tiszta függvény marad (I2), és nem függ attól,
        /// milyen sorrendben kérdezzük a cellákat.
        /// </summary>
        public readonly struct IceThresholds
        {
            /// <summary>Tartós jég: éves átlag ennél kisebb (percentilis-vágópont).</summary>
            public readonly double PermanentIceMeanK;

            /// <summary>Szezonális hó: éves minimum ennél kisebb (abszolút fagypont).</summary>
            public readonly double SeasonalSnowMinK;

            public IceThresholds(double permanentIceMeanK, double seasonalSnowMinK)
            {
                PermanentIceMeanK = permanentIceMeanK;
                SeasonalSnowMinK = seasonalSnowMinK;
            }

            /// <summary>A régi, abszolút szabály — összehasonlításhoz és a korábbi viselkedéshez.</summary>
            public static IceThresholds Absolute => new IceThresholds(
                LakesIceErosion.PermanentIceMeanThresholdK, LakesIceErosion.SeasonalSnowMinThresholdK);
        }

        /// <summary>
        /// A tartós-jég vágópont az éves felszíni középhőmérsékletek
        /// eloszlásából. A vágópont maga is CELLAÉRTÉK (a rendezett minta
        /// q-indexű eleme), ezért a „kisebb mint" összehasonlítás pontosan a
        /// nála hidegebb cellákat választja ki.
        ///
        /// Üres bemenetnél a vágópont <see cref="double.NegativeInfinity"/>:
        /// nincs eloszlás, amihez viszonyítsunk, tehát nincs tartós jég sem.
        /// Ez tudatos, dokumentált konvenció (az ND-126 üres-eset szabályának
        /// megfelelője).
        /// </summary>
        public static IceThresholds ComputeThresholds(IReadOnlyList<double> annualMeanSurfaceK,
            double permanentIcePercentile = DefaultPermanentIcePercentile)
        {
            if (annualMeanSurfaceK == null) throw new ArgumentNullException(nameof(annualMeanSurfaceK));
            if (!(permanentIcePercentile >= 0.0 && permanentIcePercentile <= 1.0))
                throw new ArgumentOutOfRangeException(nameof(permanentIcePercentile), "A 0..1 tartományba eső érték szükséges.");

            int count = annualMeanSurfaceK.Count;
            if (count == 0)
                return new IceThresholds(double.NegativeInfinity, LakesIceErosion.SeasonalSnowMinThresholdK);

            var sorted = new double[count];
            for (int c = 0; c < count; c++) sorted[c] = annualMeanSurfaceK[c];
            Array.Sort(sorted);

            // Ugyanaz az index-képlet, amit a BiomeClassification és a
            // folyó-forrás kiválasztás használ - szándékosan, hogy egyféle
            // percentilis-konvenció legyen a projektben.
            int idx = (int)(permanentIcePercentile * count);
            if (idx < 0) idx = 0;
            if (idx > count - 1) idx = count - 1;
            return new IceThresholds(sorted[idx], LakesIceErosion.SeasonalSnowMinThresholdK);
        }

        /// <summary>Jégosztály egy cellára. Tiszta függvény: csak összehasonlítás, bitpontos.</summary>
        public static LakesIceErosion.IceClass Classify(double meanAnnualK, double minAnnualK, IceThresholds thresholds)
        {
            if (meanAnnualK < thresholds.PermanentIceMeanK) return LakesIceErosion.IceClass.PermanentIce;
            if (minAnnualK < thresholds.SeasonalSnowMinK) return LakesIceErosion.IceClass.SeasonalSnow;
            return LakesIceErosion.IceClass.None;
        }

        /// <summary>Jégosztály cellánként egy kész éves statisztikából, a saját eloszlásából vett küszöbbel.</summary>
        public static LakesIceErosion.IceClass[] ClassifyAll(ThermalAnnualStatistics annual,
            IceThresholds thresholds)
        {
            if (annual == null) throw new ArgumentNullException(nameof(annual));
            var result = new LakesIceErosion.IceClass[annual.MeanSurfaceK.Count];
            for (int c = 0; c < result.Length; c++)
                result[c] = Classify(annual.MeanSurfaceK[c], annual.MinSurfaceK[c], thresholds);
            return result;
        }
    }
}
