using System;
using System.Collections.Generic;

namespace WorldGen.Core.Climate
{
    public enum Biome
    {
        Ocean,
        SeaIce,
        IceSheet,
        Tundra,
        Desert,
        Grassland,
        TemperateForest,
        Savanna,
        Rainforest,
    }

    /// <summary>
    /// M5 biome/jég-osztályozás (§5.2) — ND-10 javasolt alapértelmezése: fix
    /// Föld-szerű küszöbök, nem bolygóparaméter-skálázott (v1.0-ban).
    ///
    /// ND-126 (2026-09-21) — KÉTDIMENZIÓS (Whittaker-jellegű) osztályozás:
    /// HŐMÉRSÉKLET × CSAPADÉK. Korábban az osztályozó KIZÁRÓLAG hőmérsékletet
    /// kapott, és a saját doksija ki is mondta, hogy „a csapadék/nedvesség
    /// (§31) halasztva van, ezért NEM különböztetünk meg pl.
    /// sivatagot/esőerdőt". A csapadék-mező (<see cref="MoisturePrecipitation"/>)
    /// azóta elkészült, de ide nem került be — emiatt négy, tisztán
    /// hőmérsékleti osztály volt, a hőmérséklet pedig lényegében a szélesség
    /// sima függvénye, tehát az eredmény TÖKÉLETES SZÉLESSÉGI SÁVOK lettek.
    /// A felhasználói visszajelzés („a ráktérítő és baktérítő közt minden
    /// sivatag, fölötte-alatta egy darabig zöld, efölött jég — nagyon nem
    /// életszerű") pontosan ezt írta le.
    ///
    /// MÉRVE (seed 0xA7C944210000, level 6, szárazföldi tile-ok): egy
    /// HŐMÉRSÉKLETI osztályon belül a csapadék P10→P90 szórása <b>52–140×</b>
    /// — vagyis ugyanazon a szélességen már eddig is volt száraz és nedves
    /// szárazföld (Szahara és Kongó egy vonalban), csak az osztályozás
    /// eldobta ezt az információt.
    ///
    /// A TÁBLÁZAT (a hideg vég csapadéktól FÜGGETLEN, mint a valóságban is —
    /// a sarkvidéki „hideg sivatag" is jég/tundra marad):
    ///
    /// <code>
    /// csapadék
    ///   ^
    ///   |  nedves    | Rainforest      | Rainforest
    ///   |  közepes   | TemperateForest | Savanna
    ///   |  félszáraz | Grassland       | Grassland
    ///   |  száraz    | Desert          | Desert
    ///   +---------------------------------------------> hőmérséklet
    ///                 5..20 °C           20 °C fölött
    ///
    ///   −10 °C alatt: IceSheet   |   −10..5 °C: Tundra   (csapadéktól függetlenül)
    /// </code>
    ///
    /// A CSAPADÉK-KÜSZÖBÖK PERCENTILISEK, NEM ABSZOLÚT ÉRTÉKEK — és ez nem
    /// kozmetika. A csapadék egysége a modellben nem mm/év, hanem önkényes
    /// nedvesség-egység, ami függ a <see cref="MoisturePrecipitation"/>
    /// paramétereitől (alap-kicsapódási hányad, iterációszám) és a
    /// bolygóparaméterektől. Abszolút küszöb ezért VILÁGFÜGGŐ lenne: egy
    /// szárazabb bolygón minden sivatag, egy nedvesebben minden esőerdő.
    /// Ugyanaz a minta, mint a folyó-forrás kiválasztásnál
    /// (<c>RiverPathTracing.DefaultPrecipPercentile</c>), és ugyanaz a
    /// tanulság, mint az ND-124-ben, ahol egy globális abszolút küszöb és egy
    /// részhalmaz metszete üresnek bizonyult.
    ///
    /// Nincs transzcendens függvény itt — csak küszöb-összehasonlítás a már
    /// kiszámolt hőmérsékleten és csapadékon, tehát BITPONTOS.
    /// </summary>
    public static class BiomeClassification
    {
        public const double OceanFreezingK = 271.15; // ~ -2°C, sós víz fagyáspontja
        public const double IceSheetThresholdK = 263.15; // -10°C
        public const double TundraThresholdK = 278.15; // 5°C
        public const double TemperateThresholdK = 293.15; // 20°C

        /// <summary>
        /// A csapadék-küszöbök percentilisei a SZÁRAZFÖLDI eloszláson.
        /// MVP-értékek (ND-126), hangolhatók — a Földön a szárazföld
        /// nagyjából harmada arid/szemiarid, ezért esik az első két vágás
        /// a 20. és a 45. percentilisre.
        /// </summary>
        public const double AridPercentile = 0.20;
        public const double SemiAridPercentile = 0.45;
        public const double MoistPercentile = 0.75;

        /// <summary>
        /// A három csapadék-vágópont, a szárazföldi eloszlás percentiliseiből.
        /// Érték-típus, nincs benne állapot — a determinizmushoz (I2) a
        /// hívónak EGYSZER kell kiszámolnia, és változatlanul továbbadnia.
        /// </summary>
        public readonly struct PrecipitationThresholds
        {
            public readonly double Arid;
            public readonly double SemiArid;
            public readonly double Moist;

            public PrecipitationThresholds(double arid, double semiArid, double moist)
            {
                Arid = arid;
                SemiArid = semiArid;
                Moist = moist;
            }
        }

        /// <summary>
        /// A csapadék-vágópontok kiszámítása a SZÁRAZFÖLDI csapadék-értékekből.
        /// A hívó CSAK a szárazföldi tile-ok értékeit adja át — az óceáni
        /// értékek benne torzítanák az eloszlást (az óceán fölött keletkezik a
        /// nedvesség, ott szisztematikusan magasabb).
        ///
        /// Üres bemenetnél mindhárom vágópont 0, ami azt jelenti, hogy minden
        /// pozitív csapadék a legnedvesebb osztályba esik. Ez tudatos,
        /// dokumentált konvenció: nincs eloszlás, amihez viszonyítsunk.
        /// </summary>
        public static PrecipitationThresholds ComputeThresholds(IEnumerable<double> landPrecipitation)
        {
            if (landPrecipitation == null) throw new ArgumentNullException(nameof(landPrecipitation));

            var values = new List<double>();
            foreach (double v in landPrecipitation) values.Add(v);
            if (values.Count == 0) return new PrecipitationThresholds(0.0, 0.0, 0.0);

            values.Sort();
            return new PrecipitationThresholds(
                Percentile(values, AridPercentile),
                Percentile(values, SemiAridPercentile),
                Percentile(values, MoistPercentile));
        }

        /// <summary>
        /// A vágópontok a VEGETÁLT szárazföld csapadék-eloszlásából — azokból
        /// a tile-okból, amelyeket a csapadék-tengely EGYÁLTALÁN osztályoz
        /// (<see cref="TundraThresholdK"/> fölött).
        ///
        /// MIÉRT NEM A TELJES SZÁRAZFÖLDBŐL. A hideg tile-okat a hőmérséklet
        /// dönti el (jégtakaró/tundra), a csapadékuk viszont szisztematikusan
        /// 0 közeli — ha benne vannak az eloszlásban, lehúzzák a
        /// percentiliseket, és a meleg sáv MINDEN tile-ja „nedvesnek" látszik.
        /// MÉRVE (seed 0xA7C944210000, level 6): a teljes szárazföldből
        /// számolva az arid vágópont pontosan <b>0,000</b> lett (a
        /// szárazföld több mint 20%-ának nulla a csapadéka), és a
        /// szárazföld 24,4%-a lett esőerdő — a Földön ez ~7%.
        ///
        /// Ez ugyanaz a hibaosztály, mint az ND-124-ben: ott a globális
        /// csapadék-percentilis és a vízgyűjtő-szűrés metszete lett üres,
        /// mert két különböző populációra vonatkozó küszöböt kombináltunk.
        /// </summary>
        public static PrecipitationThresholds ComputeThresholdsForVegetatedLand(
            IEnumerable<(double TemperatureK, double Precipitation)> landSamples)
        {
            if (landSamples == null) throw new ArgumentNullException(nameof(landSamples));

            var vegetated = new List<double>();
            foreach ((double TemperatureK, double Precipitation) sample in landSamples)
                if (sample.TemperatureK >= TundraThresholdK) vegetated.Add(sample.Precipitation);

            return ComputeThresholds(vegetated);
        }

        /// <summary>Ugyanaz az index-képlet, amit a folyó-forrás kiválasztás is használ - szándékosan, hogy egyféle percentilis-konvenció legyen a projektben.</summary>
        private static double Percentile(List<double> sorted, double q)
        {
            int idx = (int)(q * sorted.Count);
            if (idx < 0) idx = 0;
            if (idx > sorted.Count - 1) idx = sorted.Count - 1;
            return sorted[idx];
        }

        /// <summary>
        /// Biome egy pontra. A <paramref name="thresholds"/>-ot a hívó
        /// <see cref="ComputeThresholds"/>-szal állítja elő EGYSZER, a teljes
        /// szárazföldi mezőből — így az osztályozás tiszta függvény marad, és
        /// nem függ attól, milyen sorrendben kérdezzük a tile-okat.
        /// </summary>
        public static Biome Classify(
            double temperatureK, bool isOceanic, double precipitation, PrecipitationThresholds thresholds)
        {
            if (isOceanic)
                return temperatureK < OceanFreezingK ? Biome.SeaIce : Biome.Ocean;

            // A hideg vég csapadéktól FÜGGETLEN: a sarkvidéki "hideg sivatag"
            // is jég/tundra, nem homoksivatag.
            if (temperatureK < IceSheetThresholdK) return Biome.IceSheet;
            if (temperatureK < TundraThresholdK) return Biome.Tundra;

            bool warm = temperatureK >= TemperateThresholdK;

            if (precipitation <= thresholds.Arid) return Biome.Desert;
            if (precipitation <= thresholds.SemiArid) return Biome.Grassland;
            if (precipitation <= thresholds.Moist) return warm ? Biome.Savanna : Biome.TemperateForest;
            return Biome.Rainforest;
        }

        /// <summary>
        /// Csak a HŐMÉRSÉKLETI (csapadéktól független) osztályok: óceán,
        /// tengeri jég, jégtakaró, tundra. A szárazföldi meleg sávokra
        /// <c>null</c>-t ad, mert azokhoz csapadék KELL.
        ///
        /// Azoknak a hívóknak van, akiknek csak az kell, hogy „jég-e" —
        /// nekik ne kelljen csapadék-mezőt felépíteniük. Aki BIOME-ot akar,
        /// a <see cref="Classify"/>-t hívja.
        /// </summary>
        public static Biome? ClassifyTemperatureOnly(double temperatureK, bool isOceanic)
        {
            if (isOceanic)
                return temperatureK < OceanFreezingK ? Biome.SeaIce : Biome.Ocean;
            if (temperatureK < IceSheetThresholdK) return Biome.IceSheet;
            if (temperatureK < TundraThresholdK) return Biome.Tundra;
            return null;
        }
    }
}
