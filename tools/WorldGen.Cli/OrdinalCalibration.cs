using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;

namespace WorldGen.Cli
{
    /// <summary>
    /// M8 ND-09 kalibrációs eszköz (docs/04-decisions.md: "Előre kalibrált,
    /// ~1000 világból, verziózva"). N világot generál (seed=1..N, egyébként
    /// rögzített, Föld-analóg paraméterekkel), kiszámolja a MÁR LÉTEZŐ
    /// folytonos metrikákat (World-szintű Habitability, kontinens-szintű
    /// Coastal complexity), és a kvintilis-vágópontokat (20/40/60/80
    /// percentilis) írja ki - ezekből lesz az <see cref="OrdinalQuantization"/>
    /// hardcode-olt, verziózott küszöb-táblája a Core-ban.
    ///
    /// NEM Python-referencia-alapú (mint a szimulációs algoritmusok) - ez egy
    /// tiszta statisztikai aggregáció a MÁR verifikált Core-metrikákon, ugyanaz
    /// a besorolás, mint a FeatureMetrics aggregációi (ld. ott: "determinisztikus,
    /// nem igényel új modellt, Python-referencia nélkül").
    /// </summary>
    public static class OrdinalCalibration
    {
        public sealed class Result
        {
            public double[] HabitabilitySamples = Array.Empty<double>();
            public double[] CoastalComplexitySamples = Array.Empty<double>();

            /// <summary>
            /// ND-117: REGIO-szintu minta (a "Soil fertility" panel-mezo a §2.3
            /// regio-tablaban all), vizgyujto-regionkent egy ertek.
            /// </summary>
            public double[] SoilFertilitySamples = Array.Empty<double>();
        }

        // `includeSoilFertility` (ND-117): a talaj-termekenyseg mintavetele a
        // TELJES regolit-lancot igenyli (erozio + nedvesseg + evi homerseklet),
        // ami MERVE ~3,2 s / vilag level=6-on, szemben a masik ket metrika
        // toredek-masodpercevel. Ezert kulon kapcsolo: aki csak a regi ket
        // metrikat szamolja ujra, annak ne lassuljon a futas a tobbszorosere.
        public static Result Run(int worldCount, int plateCount, int level, double targetWaterFraction,
            bool includeSoilFertility = false)
        {
            const double orbitalPeriodDays = 365.25;
            const double rotationPeriodDays = 1.0;
            const double axialTiltDegrees = 23.44;
            const double axialTiltRad = axialTiltDegrees * Math.PI / 180.0;
            const double dayT = 0.0;
            const int minContinentTiles = 5;
            // ND-117: ugyanaz a kuszob-logika, mint a kontinensnel - egy-ket
            // tile-os "regio" statisztikailag zaj, nem panel-alany. ND-127 ota
            // ez csak az onallo apro szigeteket zarja ki (az osszevont regiok
            // egyebkent elerik a cel-meretet).
            const int minRegionTiles = 5;

            var habitabilitySamples = new List<double>(worldCount);
            var coastalComplexitySamples = new List<double>();
            var soilFertilitySamples = new List<double>();

            for (int seedIndex = 1; seedIndex <= worldCount; seedIndex++)
            {
                ulong seed = unchecked((ulong)seedIndex);
                Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationFieldAtTime(seed, plateCount, level, 0.0);
                double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, targetWaterFraction);
                Dictionary<TileId, bool> isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);

                var temperatureK = new Dictionary<TileId, double>(field.Count);
                foreach (KeyValuePair<TileId, double> kv in field)
                {
                    TileGeometry.ToPosition(kv.Key, out double x, out double y, out double z);
                    bool oceanic = isOcean[kv.Key];
                    temperatureK[kv.Key] = Temperature.TemperatureKelvin(
                        x, y, z, dayT, orbitalPeriodDays, rotationPeriodDays, axialTiltRad, oceanic, kv.Value, seaLevel);
                }

                double habitability = FeatureMetrics.HabitabilityFraction(field.Keys, temperatureK, isOcean);
                habitabilitySamples.Add(habitability);

                List<List<TileId>> continents = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: minContinentTiles);
                foreach (List<TileId> continent in continents)
                {
                    var tileSet = new HashSet<TileId>(continent);
                    coastalComplexitySamples.Add(FeatureMetrics.CoastalComplexity(tileSet, isOcean));
                }

                if (!includeSoilFertility) continue;

                // ND-117: a regolit-lanc SAJAT vilagot szamol (ComputeField), a
                // fenti `field`/`isOcean` nem adhato at neki - a ket ut ugyanarra
                // a seedre ugyanazt a domborzatot kapja, csak a ComputeField
                // ezen felul eroziot/csapadekot/homersekletet is futtat.
                RegolithModel.RegolithField regolith = RegolithModel.ComputeField(
                    seed, plateCount, level,
                    orbitalPeriodDays: orbitalPeriodDays,
                    rotationPeriodDays: rotationPeriodDays,
                    axialTiltDegrees: axialTiltDegrees);

                // ND-127: a minta populacioja UGYANAZ, amit a panel mutat -
                // az OSSZEVONT regio, nem a nyers vizgyujto. Ez nem kozmetika:
                // a ket eloszlas merve kulonbozik (p20/p80 vizgyujton
                // 0,1928/0,2625, osszevont region 0,1357/0,2406), tehat a regi
                // kuszobokkel az osszevont regiok tobb mint 40%-a esne a
                // legalso savba a 20% helyett.
                int landTiles = 0;
                foreach (bool oceanic in regolith.IsOcean.Values)
                    if (!oceanic) landTiles++;

                Dictionary<TileId, List<TileId>> watersheds =
                    FeatureSegmentation.FindWatershedRegions(regolith.Parent, regolith.IsOcean);
                List<List<TileId>> soilRegions = FeatureSegmentation.MergeWatershedsIntoRegions(
                    watersheds, FeatureSegmentation.RecommendedRegionTileTarget(landTiles));
                foreach (List<TileId> region in soilRegions)
                {
                    if (region.Count < minRegionTiles) continue;
                    soilFertilitySamples.Add(FeatureMetrics.SoilFertility(
                        region, regolith.DepthMeters, regolith.WaterRetention, regolith.IsOcean,
                        RegolithModel.DepthAbsoluteCapM));
                }
            }

            return new Result
            {
                HabitabilitySamples = habitabilitySamples.ToArray(),
                CoastalComplexitySamples = coastalComplexitySamples.ToArray(),
                SoilFertilitySamples = soilFertilitySamples.ToArray(),
            };
        }

        /// <summary>Kvintilis-vágópontok (p20/p40/p60/p80) - a "nearest-rank" módszerrel, hogy determinisztikus/hordozható legyen (nincs interpoláció-választási kétértelműség).</summary>
        public static double[] QuintileThresholds(double[] samples)
        {
            if (samples.Length == 0)
                throw new ArgumentException("Üres mintahalmaz.", nameof(samples));
            double[] sorted = (double[])samples.Clone();
            Array.Sort(sorted);
            double[] thresholds = new double[4];
            for (int i = 0; i < 4; i++)
            {
                double fraction = (i + 1) * 0.2;
                int idx = Math.Min(sorted.Length - 1, (int)Math.Ceiling(fraction * sorted.Length) - 1);
                idx = Math.Max(0, idx);
                thresholds[i] = sorted[idx];
            }
            return thresholds;
        }
    }
}
