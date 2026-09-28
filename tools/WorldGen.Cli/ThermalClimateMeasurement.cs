using System;
using System.Collections.Generic;
using System.Diagnostics;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;

namespace WorldGen.Cli
{
    /// <summary>
    /// Az ND-158 kétmenetes éves éghajlat KÖLTSÉGMÉRÉSE valódi világon.
    ///
    /// MIÉRT CLI, ÉS MIÉRT NEM TESZT. A level-6 futás percekben mérhető; egy
    /// CI-ben futó xUnit-teszt ettől vagy lassú lenne, vagy gépfüggő időkaput
    /// kellene állítania. A mérés ezért explicit, kézzel indított parancs;
    /// a HELYESSÉGET a Core tesztek és a Python-orákulum fedik, ez itt
    /// kizárólag időt és darabszámot mér.
    ///
    /// Nincs benne szimulációs logika: az eleváció a
    /// <see cref="SeaLevelCalibration"/>-ból, az éghajlat a
    /// <see cref="ThermalClimateCalculator"/>-ból jön.
    /// </summary>
    public static class ThermalClimateMeasurement
    {
        public sealed class Options
        {
            public ulong Seed;
            public int Plates;
            public int Level;
            public double TimeMyr;
            public double TargetWaterFraction = 0.65;
            public int SampleDays = ThermalAnnualStatisticsCalculator.DefaultSampleDays;
            public double OrbitalPeriodDays = 365.25;
            public double RotationPeriodDays = 1.0;
            public double AxialTiltDegrees = 23.44;
            public bool Parallel = true;
        }

        public static int Run(Options options)
        {
            var total = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(
                options.Seed, options.Plates, options.Level);
            double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, options.TargetWaterFraction);
            double elevationMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            DenseGridMetrics grid = DenseGridMetrics.Build(options.Level);
            double gridMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var kinds = new SurfaceThermalKind[grid.CellCount];
            var elevation = new double[grid.CellCount];
            int side = grid.Side;
            int oceanCells = 0;
            for (int face = 0; face < 6; face++)
                for (int u = 0; u < side; u++)
                    for (int v = 0; v < side; v++)
                    {
                        int index = DenseGridMetrics.Index(face, u, v, side);
                        TileId id = TileId.FromFaceLevelUV(face, options.Level, (uint)u, (uint)v);
                        double h = field[id];
                        elevation[index] = h;
                        // JÉGMENTES bemenet (ND-158): a tó-réteg nélkül a
                        // felszíntípus tisztán eleváció-kérdés, tehát nem
                        // hivatkozik vissza a hőmérsékletre.
                        bool ocean = h < seaLevel;
                        kinds[index] = ocean ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land;
                        if (ocean) oceanCells++;
                    }
            double inputMs = sw.Elapsed.TotalMilliseconds;

            var orbit = new ThermalOrbit(options.OrbitalPeriodDays, options.RotationPeriodDays,
                options.AxialTiltDegrees * Math.PI / 180.0);
            long[] days = ThermalAnnualStatisticsCalculator.SampleDayIndices(
                options.OrbitalPeriodDays, options.SampleDays);
            long ticks = EstimateTicksPerPass(days);

            Console.WriteLine($"Világ: seed=0x{options.Seed:X}, plates={options.Plates}, level={options.Level}, " +
                              $"t={options.TimeMyr} Myr, tengerszint={seaLevel:F1} m");
            Console.WriteLine($"Rács: {grid.CellCount} cella ({oceanCells} óceáni), {grid.EdgeCount} él");
            Console.WriteLine($"Mintanapok ({days.Length}): {string.Join(", ", days)}");
            Console.WriteLine($"Becsült solver-tick menetenként: {ticks} (kanonikus spin-uppal együtt)");
            Console.WriteLine($"Bemenet: eleváció {elevationMs:F0} ms, rács {gridMs:F0} ms, típusok {inputMs:F0} ms");
            Console.WriteLine("Éves éghajlat számítása (2 menet)...");

            sw.Restart();
            ThermalClimate climate = ThermalClimateCalculator.Compute(grid, kinds, elevation, seaLevel,
                options.Seed, options.TimeMyr * 1.0e6, orbit, sampleDays: options.SampleDays,
                useParallelLocalStep: options.Parallel);
            double climateMs = sw.Elapsed.TotalMilliseconds;

            int permanent = climate.CountRefined(LakesIceErosion.IceClass.PermanentIce);
            int seasonal = climate.CountRefined(LakesIceErosion.IceClass.SeasonalSnow);
            int none = climate.CountRefined(LakesIceErosion.IceClass.None);
            Console.WriteLine($"Éves éghajlat: {climateMs:F0} ms ({climateMs / 1000.0:F1} s), " +
                              $"{climateMs / (2.0 * ticks):F2} ms/tick, párhuzamos lokális lépés={options.Parallel}");
            Console.WriteLine($"Jég (B menet): tartós={permanent}, szezonális hó={seasonal}, nincs={none}");
            Console.WriteLine($"A menet tartós jég: {CountIce(climate.IceFreeClass)}, " +
                              $"átsorolt cella a két menet között: {climate.ReclassifiedCells}");
            Console.WriteLine($"Éves felszíni átlag: [{Min(climate.Refined.MeanSurfaceK) - 273.15:F1}, " +
                              $"{Max(climate.Refined.MeanSurfaceK) - 273.15:F1}] °C; " +
                              $"levegő: [{Min(climate.Refined.MeanAirK) - 273.15:F1}, " +
                              $"{Max(climate.Refined.MeanAirK) - 273.15:F1}] °C");
            Console.WriteLine($"Második menet kihagyva (nem volt tartós jég): {climate.SecondPassSkipped}");

            // A/B: a RÉGI, analitikus jégút ugyanezen a világon. Az átállás
            // döntéséhez nem elég tudni, mit ad az ÚJ út - azt kell látni,
            // MENNYIVEL más, mint amit a viewer ma megjelenít.
            sw.Restart();
            int legacyPermanent = 0, legacySeasonal = 0, legacyNone = 0;
            double legacyMinMean = double.PositiveInfinity, legacyMaxMean = double.NegativeInfinity;
            int agree = 0;
            for (int c = 0; c < grid.CellCount; c++)
            {
                LakesIceErosion.AnnualTemperatureStats(grid.CenterX[c], grid.CenterY[c], grid.CenterZ[c],
                    options.OrbitalPeriodDays, options.RotationPeriodDays, orbit.AxialTiltRad,
                    kinds[c] == SurfaceThermalKind.Ocean, elevation[c], seaLevel,
                    out double mean, out double min, out double max);
                _ = max;
                if (mean < legacyMinMean) legacyMinMean = mean;
                if (mean > legacyMaxMean) legacyMaxMean = mean;
                LakesIceErosion.IceClass legacy = LakesIceErosion.ClassifyIce(mean, min);
                if (legacy == LakesIceErosion.IceClass.PermanentIce) legacyPermanent++;
                else if (legacy == LakesIceErosion.IceClass.SeasonalSnow) legacySeasonal++;
                else legacyNone++;
                if (legacy == climate.RefinedClass[c]) agree++;
            }
            double legacyMs = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"RÉGI analitikus út ({legacyMs:F0} ms): tartós={legacyPermanent}, " +
                              $"szezonális hó={legacySeasonal}, nincs={legacyNone}; " +
                              $"éves átlag [{legacyMinMean - 273.15:F1}, {legacyMaxMean - 273.15:F1}] °C");
            Console.WriteLine($"Egyezés a régi és az új jégosztály között: {agree}/{grid.CellCount} " +
                              $"({100.0 * agree / grid.CellCount:F1}%)");
            Console.WriteLine($"Teljes futás: {total.Elapsed.TotalSeconds:F1} s");
            return 0;
        }

        /// <summary>
        /// Egy menet solver-tickjeinek száma. A napok növekvők, ezért a solver a
        /// bucketen belül folytatható; bucket-váltáskor kanonikusan újraindul
        /// (spin-up + a bucket eleje óta eltelt tickek).
        /// </summary>
        private static long EstimateTicksPerPass(long[] days)
        {
            long ticks = 0;
            long at = long.MinValue;
            long canonical = long.MinValue;
            foreach (long day in days)
            {
                long first = day * SimulationTime.TicksPerDay;
                long start = SimulationTime.CanonicalStartTick(first);
                if (start != canonical || at > first)
                {
                    canonical = start;
                    at = start;
                }
                ticks += first - at + SimulationTime.TicksPerDay;
                at = first + SimulationTime.TicksPerDay;
            }
            return ticks;
        }

        private static int CountIce(System.Collections.Generic.IReadOnlyList<LakesIceErosion.IceClass> classes)
        {
            int n = 0;
            for (int c = 0; c < classes.Count; c++)
                if (classes[c] == LakesIceErosion.IceClass.PermanentIce) n++;
            return n;
        }

        private static double Min(System.Collections.Generic.IReadOnlyList<double> values)
        {
            double m = double.PositiveInfinity;
            for (int i = 0; i < values.Count; i++) if (values[i] < m) m = values[i];
            return m;
        }

        private static double Max(System.Collections.Generic.IReadOnlyList<double> values)
        {
            double m = double.NegativeInfinity;
            for (int i = 0; i < values.Count; i++) if (values[i] > m) m = values[i];
            return m;
        }
    }
}
