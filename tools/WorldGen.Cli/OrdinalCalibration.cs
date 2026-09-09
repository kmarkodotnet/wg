using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;

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
        }

        public static Result Run(int worldCount, int plateCount, int level, double targetWaterFraction)
        {
            const double orbitalPeriodDays = 365.25;
            const double rotationPeriodDays = 1.0;
            const double axialTiltDegrees = 23.44;
            const double axialTiltRad = axialTiltDegrees * Math.PI / 180.0;
            const double dayT = 0.0;
            const int minContinentTiles = 5;

            var habitabilitySamples = new List<double>(worldCount);
            var coastalComplexitySamples = new List<double>();

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
            }

            return new Result
            {
                HabitabilitySamples = habitabilitySamples.ToArray(),
                CoastalComplexitySamples = coastalComplexitySamples.ToArray(),
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
