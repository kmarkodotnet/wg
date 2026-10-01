using System;
using System.Collections.Generic;
using System.Linq;
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

            /// <summary>A24: az effektív felszíni hőkapacitás mélységeinek mérőkampói.</summary>
            public double OceanDepthM = 10.0;
            public double LandDepthM = 0.5;
            public double MeridionalTransportScale = 1.0;

            /// <summary>
            /// ND-159: a vizsgalando radiativ simitasi (beta) ertekek. Ures
            /// lista eseten csak az alapertelmezett modell fut le.
            /// </summary>
            public double[] BetaSweep = Array.Empty<double>();

            /// <summary>ND-159: a tartós-jég percentilis; <c>null</c> = a régi, abszolút küszöb.</summary>
            public double? PermanentIcePercentile = ThermalIceClassification.DefaultPermanentIcePercentile;

            /// <summary>ND-159: a bázis-hőmérséklet tagonkénti felbontása (mi teszi a modellt meleggé).</summary>
            public bool Decompose;

            /// <summary>ND-160: a bázis radiatív tagjának albedója; <c>null</c> = a bolygó-albedó (alapértelmezés).</summary>
            public double? BaselineAlbedo;

            /// <summary>ND-160 A/B: az ND-160 ELŐTTI, felszíni albedós bázis (óceán 0,06 / szárazföld 0,30).</summary>
            public bool LegacySurfaceBaselineAlbedo;

            /// <summary>ND-163: a csapadék-mező A/B-je a háromféle hőmérséklet-/szélforrással.</summary>
            public bool Precipitation;

            /// <summary>ND-164: a BIOME-térkép A/B-je, ha a hőmérséklet-tengely a hőmodell éves LEVEGŐ-átlaga.</summary>
            public bool Biome;

            /// <summary>A viewer biome-tengelyének napi fázisa (climateDayT) — az analitikus alapvonalhoz.</summary>
            public double DayT;

            /// <summary>
            /// ND-161: durvább rácson számolt éghajlat, a teljes szintűvel
            /// összevetve. 0 = nincs összehasonlítás.
            /// </summary>
            public int[] ClimateLevels = Array.Empty<int>();
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
            // A/B: a RÉGI, analitikus jégút ugyanezen a világon. ELŐRE fut, mert
            // az átállás döntéséhez nem elég tudni, mit ad az ÚJ út - azt kell
            // látni, MENNYIVEL más, mint amit a viewer ma megjelenít; és mert a
            // béta-söprés cél-darabszáma is innen jön (ND-159).
            sw.Restart();
            int legacyPermanent = 0, legacySeasonal = 0, legacyNone = 0;
            double legacyMinMean = double.PositiveInfinity, legacyMaxMean = double.NegativeInfinity;
            double legacyMinDaily = double.PositiveInfinity, legacyMaxDaily = double.NegativeInfinity;
            var legacyClass = new LakesIceErosion.IceClass[grid.CellCount];
            for (int c = 0; c < grid.CellCount; c++)
            {
                LakesIceErosion.AnnualTemperatureStats(grid.CenterX[c], grid.CenterY[c], grid.CenterZ[c],
                    options.OrbitalPeriodDays, options.RotationPeriodDays, orbit.AxialTiltRad,
                    kinds[c] == SurfaceThermalKind.Ocean, elevation[c], seaLevel,
                    out double mean, out double min, out double max);
                if (mean < legacyMinMean) legacyMinMean = mean;
                if (mean > legacyMaxMean) legacyMaxMean = mean;
                if (min < legacyMinDaily) legacyMinDaily = min;
                if (max > legacyMaxDaily) legacyMaxDaily = max;
                LakesIceErosion.IceClass legacy = LakesIceErosion.ClassifyIce(mean, min);
                legacyClass[c] = legacy;
                if (legacy == LakesIceErosion.IceClass.PermanentIce) legacyPermanent++;
                else if (legacy == LakesIceErosion.IceClass.SeasonalSnow) legacySeasonal++;
                else legacyNone++;
            }
            double legacyMs = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"RÉGI analitikus út ({legacyMs:F0} ms): tartós={legacyPermanent} " +
                              $"({100.0 * legacyPermanent / grid.CellCount:F2}%), " +
                              $"szezonális hó={legacySeasonal}, nincs={legacyNone}; " +
                              $"éves átlag [{legacyMinMean - 273.15:F1}, {legacyMaxMean - 273.15:F1}] °C");
            Console.WriteLine($"  PILLANATNYI (napi átlag) szélsőértékek: min " +
                              $"{legacyMinDaily - 273.15:F1} °C, max {legacyMaxDaily - 273.15:F1} °C");
            Console.WriteLine("  (történeti összehasonlítás; nem fizikai cél és nem az aktív hőmodell jege)");
            Console.WriteLine();

            var betas = new List<double>();
            if (options.BetaSweep.Length == 0) betas.Add(ThermalModelParameters.Default.RadiativeSmoothing);
            else betas.AddRange(options.BetaSweep);

            foreach (double beta in betas)
            {
                var parameters = new ThermalModelParameters(radiativeSmoothing: beta,
                    oceanDepthM: options.OceanDepthM, landDepthM: options.LandDepthM,
                    meridionalTransportScale: options.MeridionalTransportScale,
                    baselineAlbedo: options.BaselineAlbedo,
                    legacySurfaceBaselineAlbedo: options.LegacySurfaceBaselineAlbedo);
                bool isDefault = beta == ThermalModelParameters.Default.RadiativeSmoothing;
                Console.WriteLine($"--- beta = {beta:0.###}{(isDefault ? "  (a mai alapérték)" : "")}, " +
                                  $"óceánmélység = {options.OceanDepthM:0.###} m, " +
                                  $"talajmélység = {options.LandDepthM:0.###} m" +
                                  $", meridionális skála = {options.MeridionalTransportScale:0.###}" +
                                  (options.BaselineAlbedo.HasValue
                                      ? $", bázis-albedó = {options.BaselineAlbedo.Value:0.###} (explicit)"
                                      : options.LegacySurfaceBaselineAlbedo
                                          ? ", bázis-albedó = FELSZÍNI (ND-160 ELŐTTI, A/B)"
                                          : $", bázis-albedó = {Temperature.AlbedoPlanet:0.###} (bolygó, ND-160)")
                                  + " ---");

                sw.Restart();
                ThermalClimate climate = ThermalClimateCalculator.Compute(grid, kinds, elevation, seaLevel,
                    options.Seed, options.TimeMyr * 1.0e6, orbit, parameters, options.SampleDays,
                    useParallelLocalStep: options.Parallel,
                    permanentIcePercentile: options.PermanentIcePercentile);
                double climateMs = sw.Elapsed.TotalMilliseconds;

                int permanent = climate.CountRefined(LakesIceErosion.IceClass.PermanentIce);
                int seasonal = climate.CountRefined(LakesIceErosion.IceClass.SeasonalSnow);
                int none = climate.CountRefined(LakesIceErosion.IceClass.None);
                int passes = climate.SecondPassSkipped ? 1 : 2;
                Console.WriteLine($"  idő: {climateMs / 1000.0:F1} s ({passes} menet, " +
                                  $"{climateMs / (passes * ticks):F2} ms/tick)");
                Console.WriteLine($"  jég: tartós={permanent} ({100.0 * permanent / grid.CellCount:F2}%), " +
                                  $"szezonális hó={seasonal}, nincs={none}; " +
                                  $"átsorolt a két menet között: {climate.ReclassifiedCells}");
                Console.WriteLine($"  éves felszíni átlag: [{Min(climate.Refined.MeanSurfaceK) - 273.15:F1}, " +
                                  $"{Max(climate.Refined.MeanSurfaceK) - 273.15:F1}] °C; " +
                                  $"levegő: [{Min(climate.Refined.MeanAirK) - 273.15:F1}, " +
                                  $"{Max(climate.Refined.MeanAirK) - 273.15:F1}] °C");
                ReportPhysicalClimate(grid, climate);

                // A PILLANATNYI szélsőértékek. Ez az ND-100 döntő száma: a
                // radiatív simítás pont azért került be, mert simítás nélkül a
                // sarki éjszaka abszurd mélyre ment. Az éves átlag ÖNMAGÁBAN
                // nem dönti el a bétát - a hideg véget EGYÜTT kell nézni.
                Console.WriteLine($"  PILLANATNYI szélsőértékek: felszín min " +
                                  $"{Min(climate.Refined.MinSurfaceK) - 273.15:F1} °C, max " +
                                  $"{Max(climate.Refined.MaxSurfaceK) - 273.15:F1} °C; levegő min " +
                                  $"{Min(climate.Refined.MinAirK) - 273.15:F1} °C, max " +
                                  $"{Max(climate.Refined.MaxAirK) - 273.15:F1} °C");

                // A hideg vég eloszlása: ebből látszik, hogy a jégküszöb
                // átállítása MEDDIG tudna eljutni ennél a bétánál.
                var sorted = new double[grid.CellCount];
                for (int c = 0; c < sorted.Length; c++) sorted[c] = climate.Refined.MeanSurfaceK[c];
                Array.Sort(sorted);
                Console.WriteLine($"  éves átlag percentilisek (°C): P0={sorted[0] - 273.15:F1}, " +
                                  $"P1={Percentile(sorted, 0.01) - 273.15:F1}, " +
                                  $"P5={Percentile(sorted, 0.05) - 273.15:F1}, " +
                                  $"P10={Percentile(sorted, 0.10) - 273.15:F1}, " +
                                  $"P50={Percentile(sorted, 0.50) - 273.15:F1}");

                // Történeti darabszámhoz tartozó eloszlási hely; a szigorú
                // küszöb és az esetleges holtverseny miatt nem pontos inverz.
                if (legacyPermanent > 0 && legacyPermanent <= sorted.Length)
                    Console.WriteLine($"  a régi {legacyPermanent} jégcella rangjához tartozó hőmérséklet: " +
                                      $"{sorted[legacyPermanent - 1] - 273.15:F2} °C (a mai küszöb: " +
                                      $"{LakesIceErosion.PermanentIceMeanThresholdK - 273.15:F2} °C)");

                int agreeBeta = 0;
                for (int c = 0; c < grid.CellCount; c++)
                    if (legacyClass[c] == climate.RefinedClass[c]) agreeBeta++;
                Console.WriteLine($"  egyezés a régi jégosztállyal: {agreeBeta}/{grid.CellCount} " +
                                  $"({100.0 * agreeBeta / grid.CellCount:F1}%)");
                Console.WriteLine($"  HASZNÁLT tartós-jég küszöb: " +
                                  $"{climate.RefinedThresholds.PermanentIceMeanK - 273.15:F2} °C " +
                                  (options.PermanentIcePercentile.HasValue
                                      ? $"(percentilis, q = {options.PermanentIcePercentile.Value:0.###})"
                                      : "(abszolút, ND-43)"));

                if (options.Decompose)
                    Decompose(grid, kinds, elevation, seaLevel, options, orbit, parameters, climate);
                if (options.Precipitation)
                    ComparePrecipitation(grid, elevation, seaLevel, options, orbit, parameters, climate, sw);
                if (options.Biome)
                    CompareBiomes(grid, kinds, elevation, seaLevel, options, orbit, climate);
                foreach (int coarseLevel in options.ClimateLevels)
                {
                    CompareCoarseClimate(coarseLevel, grid, options, orbit, parameters, climate, sw,
                        ownWorld: true, fineElevation: elevation, fineSeaLevel: seaLevel);
                    CompareCoarseClimate(coarseLevel, grid, options, orbit, parameters, climate, sw,
                        ownWorld: false, fineElevation: elevation, fineSeaLevel: seaLevel);
                }
                Console.WriteLine();
            }

            Console.WriteLine($"Teljes futás: {total.Elapsed.TotalSeconds:F1} s");
            return 0;
        }

        /// <summary>
        /// ND-163: a csapadék-mező A/B-je. A kérdés, amit eldönt: mennyivel más
        /// a csapadék, ha a hőmérséklet és/vagy a SZÉL a hőmodellből jön, nem az
        /// analitikus utból — és hogy a szél HOZZÁAD-e a hőmérséklethez képest.
        ///
        /// Három változat, hogy a hatások szétváljanak:
        ///   (0) MAI: analitikus hőmérséklet + analitikus szél
        ///   (1) hőmodell hőmérséklet + analitikus szél
        ///   (2) hőmodell hőmérséklet + hőmodell (éves átlagos) szél
        ///
        /// A mérőszám nem a nyers csapadék-különbség, hanem a SZÁRAZFÖLDI
        /// PERCENTILIS-BESOROLÁS változása is: a biome-ot az ND-126 szerint a
        /// csapadék percentilisei döntik el, tehát egy egyenletes skálázódás
        /// SEMMIT nem változtatna a képen. Ami számít: átrendeződik-e a sorrend.
        /// </summary>
        private static void ComparePrecipitation(DenseGridMetrics grid,
            double[] elevation, double seaLevel, Options options, ThermalOrbit orbit,
            ThermalModelParameters parameters, ThermalClimate climate, Stopwatch sw)
        {
            int level = options.Level;
            var field = new Dictionary<TileId, double>(grid.CellCount);
            var tileOf = new TileId[grid.CellCount];
            int side = grid.Side;
            for (int face = 0; face < 6; face++)
                for (int u = 0; u < side; u++)
                    for (int v = 0; v < side; v++)
                    {
                        int index = DenseGridMetrics.Index(face, u, v, side);
                        TileId id = TileId.FromFaceLevelUV(face, level, (uint)u, (uint)v);
                        tileOf[index] = id;
                        field[id] = elevation[index];
                    }

            // A fogyasztó a VÉGLEGES B menet éghajlatát olvasná. Az ND-163
            // korábbi A/B-je itt még a jégmentes A menetet mérte; az nem a
            // tényleges átállás szélmezője, ezért a B típusait használjuk.
            sw.Restart();
            var thermal = new SurfaceTemperatureField(grid, climate.RefinedKinds.ToArray(), elevation, seaLevel,
                options.Seed, options.TimeMyr * 1.0e6, orbit, parameters);
            ThermalAnnualStatistics annual = ThermalAnnualStatisticsCalculator.Compute(
                thermal, new ThermalSnapshot(grid.CellCount), options.SampleDays, 0, includeWind: true);
            double annualMs = sw.Elapsed.TotalMilliseconds;

            var temperature = new Dictionary<TileId, double>(grid.CellCount);
            var wind = new Dictionary<TileId, SurfaceWindSample>(grid.CellCount);
            var steadiness = new Dictionary<TileId, double>(grid.CellCount);
            double rotationSum = 0.0;
            for (int c = 0; c < grid.CellCount; c++)
            {
                TileId id = tileOf[c];
                if (BitConverter.DoubleToInt64Bits(annual.MeanSurfaceK[c]) !=
                    BitConverter.DoubleToInt64Bits(climate.Refined.MeanSurfaceK[c]))
                    throw new InvalidOperationException($"A szélrögzítés megváltoztatta a B menet hőmérsékletét: {id}.");
                temperature[id] = annual.MeanSurfaceK[c];
                double wx = annual.MeanWindX![c], wy = annual.MeanWindY![c], wz = annual.MeanWindZ![c];
                double speed = annual.MeanWindSpeedMs![c];
                wind[id] = new SurfaceWindSample(wx, wy, wz, speed);
                double netto = Math.Sqrt(wx * wx + wy * wy + wz * wz);
                double persistence = speed > 1e-12 ? netto / speed : 0.0;
                steadiness[id] = persistence;
                rotationSum += persistence;
            }
            double meanSteadiness = rotationSum / grid.CellCount;

            double axialTilt = options.AxialTiltDegrees;
            MoisturePrecipitation.PrecipitationField p0 = MoisturePrecipitation.ComputeFromFields(
                field, seaLevel, options.Seed, level, null, null, 0.0,
                options.OrbitalPeriodDays, options.RotationPeriodDays, axialTilt);
            MoisturePrecipitation.PrecipitationField p1 = MoisturePrecipitation.ComputeFromFields(
                field, seaLevel, options.Seed, level, temperature, null, 0.0,
                options.OrbitalPeriodDays, options.RotationPeriodDays, axialTilt);
            MoisturePrecipitation.PrecipitationField p2 = MoisturePrecipitation.ComputeFromFields(
                field, seaLevel, options.Seed, level, temperature, wind, 0.0,
                options.OrbitalPeriodDays, options.RotationPeriodDays, axialTilt);

            Console.WriteLine($"  [CSAPADÉK A/B, végleges B menet] éves mező széllel: {annualMs / 1000.0:F1} s; " +
                              $"szél-állandóság (|átlagvektor| / átlagsebesség): {meanSteadiness:F3}");
            ReportPrecipitation("(1) hőmérséklet a hőmodellből", p0, p1, field, seaLevel);
            ReportPrecipitation("(2) + szél is a hőmodellből", p0, p2, field, seaLevel);
            ReportPrecipitation("    (2) a (1)-hez képest", p1, p2, field, seaLevel);
            ReportWindPersistence(p1, p2, field, seaLevel, steadiness);
        }

        /// <summary>
        /// Az éves átlagvektor kioltódása és a csapadék-átsorolás kapcsolata.
        /// A sávok diagnosztikai csoportok; nem változtatják meg a modellt.
        /// </summary>
        private static void ReportWindPersistence(
            MoisturePrecipitation.PrecipitationField analyticWind,
            MoisturePrecipitation.PrecipitationField thermalWind,
            Dictionary<TileId, double> elevation, double seaLevel,
            Dictionary<TileId, double> steadiness)
        {
            var tiles = new List<TileId>();
            var baseline = new List<double>();
            var changed = new List<double>();
            foreach (KeyValuePair<TileId, double> kv in elevation)
            {
                if (kv.Value < seaLevel) continue;
                tiles.Add(kv.Key);
                baseline.Add(analyticWind.Precipitation[kv.Key]);
                changed.Add(thermalWind.Precipitation[kv.Key]);
            }
            if (tiles.Count == 0) return;

            int[] oldRanks = QuartileRanks(baseline);
            int[] newRanks = QuartileRanks(changed);
            int[] count = new int[4], moved = new int[4];
            double[] sumDifference = new double[4], sumBaseline = new double[4];
            double[] maxDifference = new double[4];
            for (int i = 0; i < tiles.Count; i++)
            {
                double persistence = steadiness[tiles[i]];
                int band = persistence < 0.25 ? 0 : persistence < 0.5 ? 1 : persistence < 0.75 ? 2 : 3;
                double difference = Math.Abs(changed[i] - baseline[i]);
                count[band]++;
                if (oldRanks[i] != newRanks[i]) moved[band]++;
                sumDifference[band] += difference;
                sumBaseline[band] += baseline[i];
                if (difference > maxDifference[band]) maxDifference[band] = difference;
            }
            string[] labels = { "<0,25", "0,25–0,50", "0,50–0,75", "≥0,75" };
            Console.WriteLine("    Szél-állandóság szerint a szárazföldön (a negyedek globális vágópontokból):");
            for (int band = 0; band < 4; band++)
            {
                if (count[band] == 0) continue;
                Console.WriteLine($"      {labels[band]}: n={count[band]}, negyedváltás={moved[band]} " +
                                  $"({100.0 * moved[band] / count[band]:F1}%), " +
                                  $"átlagos |eltérés|={sumDifference[band] / count[band]:F4} " +
                                  $"({(sumBaseline[band] > 0.0 ? 100.0 * sumDifference[band] / sumBaseline[band] : 0.0):F1}%), " +
                                  $"max={maxDifference[band]:F4}");
            }
        }

        /// <summary>Két csapadék-mező összevetése a SZÁRAZFÖLDÖN, percentilis-besorolással.</summary>
        private static void ReportPrecipitation(string label,
            MoisturePrecipitation.PrecipitationField baseline, MoisturePrecipitation.PrecipitationField other,
            Dictionary<TileId, double> field, double seaLevel)
        {
            var landBase = new List<double>();
            var landOther = new List<double>();
            var tiles = new List<TileId>();
            foreach (KeyValuePair<TileId, double> kv in field)
            {
                if (kv.Value < seaLevel) continue;
                tiles.Add(kv.Key);
                landBase.Add(baseline.Precipitation[kv.Key]);
                landOther.Add(other.Precipitation[kv.Key]);
            }
            if (tiles.Count == 0) { Console.WriteLine($"    {label}: nincs szárazföld"); return; }

            double sumBase = 0.0, sumOther = 0.0, sumAbs = 0.0, maxAbs = 0.0;
            for (int i = 0; i < tiles.Count; i++)
            {
                sumBase += landBase[i];
                sumOther += landOther[i];
                double d = Math.Abs(landOther[i] - landBase[i]);
                sumAbs += d;
                if (d > maxAbs) maxAbs = d;
            }
            double meanBase = sumBase / tiles.Count;

            // Percentilis-besorolás: a két mező szerinti NEGYED-be sorolás egyezése.
            int[] rankBase = QuartileRanks(landBase);
            int[] rankOther = QuartileRanks(landOther);
            int same = 0;
            for (int i = 0; i < tiles.Count; i++) if (rankBase[i] == rankOther[i]) same++;

            Console.WriteLine($"    {label}: átlag {meanBase:F4} → {sumOther / tiles.Count:F4}, " +
                              $"átlagos |eltérés| {sumAbs / tiles.Count:F4} " +
                              $"({(meanBase > 0.0 ? 100.0 * (sumAbs / tiles.Count) / meanBase : 0.0):F1}% az átlaghoz), " +
                              $"max {maxAbs:F4}; NEGYED-besorolás egyezés {same}/{tiles.Count} " +
                              $"({100.0 * same / tiles.Count:F1}%)");
        }

        private static int[] QuartileRanks(List<double> values)
        {
            var sorted = new double[values.Count];
            values.CopyTo(sorted);
            Array.Sort(sorted);
            double q1 = sorted[(int)(0.25 * sorted.Length)];
            double q2 = sorted[(int)(0.50 * sorted.Length)];
            double q3 = sorted[(int)(0.75 * sorted.Length)];
            var ranks = new int[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                double v = values[i];
                ranks[i] = v <= q1 ? 0 : v <= q2 ? 1 : v <= q3 ? 2 : 3;
            }
            return ranks;
        }

        /// <summary>
        /// ND-164: a BIOME-térkép A/B-je. A kérdés, amit eldönt: mennyivel
        /// más a biome-térkép, ha a hőmérséklet-tengely a hőmodell éves
        /// LEVEGŐ-középhőmérséklete, nem a viewer mai, analitikus
        /// <see cref="Temperature.TemperatureKelvinFromSamples"/> értéke.
        ///
        /// MIÉRT A LEVEGŐ, ÉS MIÉRT AZ ÉVES. A Whittaker-jellegű tábla
        /// (ND-126) éves levegő-középhőmérsékletre van kalibrálva, és a
        /// <c>ThermalClimateCalculator.ClassifyBiomes</c> is azt
        /// olvassa. A mai viewer-érték ezzel szemben EGYETLEN nap
        /// (climateDayT) 24 mintás inszoláció-átlaga — tehát nemcsak a modell
        /// más, hanem az IDŐABLAK is.
        ///
        /// Három változat, hogy a két hatás szétváljon:
        ///   (0) MAI: analitikus hőmérséklet + analitikus csapadék
        ///   (1) hőmodell éves LEVEGŐ-átlag + analitikus csapadék
        ///   (2) hőmodell éves LEVEGŐ-átlag + a hőmodell felszíni átlagával
        ///       számolt csapadék — EZ az, amit a 6. fázis fogyasztói
        ///       átállása ténylegesen ad.
        ///
        /// A csapadék-vágópontokat MINDEN változat a SAJÁT mezőjéből kapja
        /// (<see cref="BiomeClassification.ComputeThresholdsForVegetatedLand"/>),
        /// mert a viewer is így csinálja — különben nem azt mérnénk, amit a
        /// felhasználó lát.
        /// </summary>
        private static void CompareBiomes(DenseGridMetrics grid, SurfaceThermalKind[] kinds,
            double[] elevation, double seaLevel, Options options, ThermalOrbit orbit, ThermalClimate climate)
        {
            int level = options.Level;
            int side = grid.Side;
            var field = new Dictionary<TileId, double>(grid.CellCount);
            var tileOf = new TileId[grid.CellCount];
            var isOceanic = new bool[grid.CellCount];
            for (int face = 0; face < 6; face++)
                for (int u = 0; u < side; u++)
                    for (int v = 0; v < side; v++)
                    {
                        int index = DenseGridMetrics.Index(face, u, v, side);
                        TileId id = TileId.FromFaceLevelUV(face, level, (uint)u, (uint)v);
                        tileOf[index] = id;
                        field[id] = elevation[index];
                        isOceanic[index] = kinds[index] == SurfaceThermalKind.Ocean;
                    }

            // A MAI viewer-tengely: egyetlen nap 24 Nap-irányából vett átlag.
            DailyInsolationSampleDirections samples = DailyInsolationSampleDirections.Create(
                options.DayT, options.OrbitalPeriodDays, options.RotationPeriodDays, orbit.AxialTiltRad);
            var analyticK = new double[grid.CellCount];
            for (int c = 0; c < grid.CellCount; c++)
            {
                TileGeometry.ToPosition(tileOf[c], out double x, out double y, out double z);
                analyticK[c] = Temperature.TemperatureKelvinFromSamples(
                    x, y, z, in samples, isOceanic[c], elevation[c], seaLevel);
            }

            // A hőmodell tengelyei: a biome a LEVEGŐ-, a párolgás a FELSZÍNI átlagot kapja.
            var thermalAirK = new double[grid.CellCount];
            var surfaceK = new Dictionary<TileId, double>(grid.CellCount);
            for (int c = 0; c < grid.CellCount; c++)
            {
                thermalAirK[c] = climate.Refined.MeanAirK[c];
                surfaceK[tileOf[c]] = climate.Refined.MeanSurfaceK[c];
            }

            double axialTilt = options.AxialTiltDegrees;
            MoisturePrecipitation.PrecipitationField precipAnalytic = MoisturePrecipitation.ComputeFromFields(
                field, seaLevel, options.Seed, level, null, null, options.DayT,
                options.OrbitalPeriodDays, options.RotationPeriodDays, axialTilt);
            MoisturePrecipitation.PrecipitationField precipThermal = MoisturePrecipitation.ComputeFromFields(
                field, seaLevel, options.Seed, level, surfaceK, null, options.DayT,
                options.OrbitalPeriodDays, options.RotationPeriodDays, axialTilt);

            Biome[] b0 = ClassifyVariant(tileOf, isOceanic, analyticK, precipAnalytic, null);
            Biome[] b1 = ClassifyVariant(tileOf, isOceanic, thermalAirK, precipAnalytic, null);
            Biome[] b2 = ClassifyVariant(tileOf, isOceanic, thermalAirK, precipThermal, null);
            Biome[] b3 = ClassifyVariant(tileOf, isOceanic, thermalAirK, precipThermal, climate.RefinedClass);
            Biome[] b4 = ClassifyVariant(tileOf, isOceanic, thermalAirK, precipThermal, climate.RefinedClass, true);

            int iceLand = 0, iceOcean = 0;
            for (int c = 0; c < grid.CellCount; c++)
                if (climate.RefinedClass[c] == LakesIceErosion.IceClass.PermanentIce)
                {
                    if (isOceanic[c]) iceOcean++; else iceLand++;
                }

            Console.WriteLine($"  [BIOME A/B] hőmérséklet-tengely: analitikus " +
                              $"[{Min(analyticK) - 273.15:F1}, {Max(analyticK) - 273.15:F1}] °C → " +
                              $"hőmodell éves levegő [{Min(thermalAirK) - 273.15:F1}, " +
                              $"{Max(thermalAirK) - 273.15:F1}] °C");
            Console.WriteLine($"    a percentilis tartós jég megoszlása: {iceLand} szárazföldi, {iceOcean} óceáni cella");

            // A DÖNTŐ kérdés a hideg véghez: a (2) tundrája ELTAKARÓDIK-e amúgy is?
            // A viewer render-kategóriája jégre vált ott, ahol a maszk jeget mond
            // (RenderCategory.IceSheet), tehát ha a tundra-cellák a jégmaszkon
            // BELÜL vannak, a (3) NEM vesz el semmit a KÉPBŐL — csak a panelt
            // hozza összhangba azzal, ami látszik (I4).
            int tundra = 0, tundraInsideIce = 0;
            for (int c = 0; c < grid.CellCount; c++)
            {
                if (isOceanic[c] || b2[c] != Biome.Tundra) continue;
                tundra++;
                if (climate.RefinedClass[c] == LakesIceErosion.IceClass.PermanentIce) tundraInsideIce++;
            }
            Console.WriteLine($"    a (2) szárazföldi tundrájából a jégmaszkon BELÜL: " +
                              $"{tundraInsideIce}/{tundra}" +
                              $" ({(tundra > 0 ? 100.0 * tundraInsideIce / tundra : 0.0):F1}%)");
            ReportBiome("(1) csak a hőmérséklet-tengely", b0, b1, isOceanic);
            ReportBiome("(2) + a párolgás hőmérséklete is", b0, b2, isOceanic);
            ReportBiome("    (2) a (1)-hez képest", b1, b2, isOceanic);
            ReportBiome("(3) + a hideg vég a jégosztályból", b0, b3, isOceanic);
            ReportBiome("(4) (3) + tundra a szezonális hóból", b0, b4, isOceanic);
            ReportSeaIce("(0) mai", b0, isOceanic);
            ReportSeaIce("(2) hőmodell tengely", b2, isOceanic);
            ReportSeaIce("(3) jégosztályos hidegvég", b3, isOceanic);
            ReportBiomeHistogram("(0) mai", b0, isOceanic);
            ReportBiomeHistogram("(2) abszolút hidegvég", b2, isOceanic);
            ReportBiomeHistogram("(3) jégosztályos hidegvég", b3, isOceanic);
            ReportBiomeHistogram("(4) + hós tundra", b4, isOceanic);
        }

        /// <summary>Egy biome-változat: saját csapadék-vágópontokkal, ahogy a viewer is számolja.</summary>
        private static Biome[] ClassifyVariant(TileId[] tileOf, bool[] isOceanic, double[] temperatureK,
            MoisturePrecipitation.PrecipitationField precip,
            System.Collections.Generic.IReadOnlyList<LakesIceErosion.IceClass>? iceClass,
            bool snowTundra = false)
        {
            var landSamples = new List<(double TemperatureK, double Precipitation)>(tileOf.Length);
            for (int c = 0; c < tileOf.Length; c++)
                if (!isOceanic[c]) landSamples.Add((temperatureK[c], precip.Precipitation[tileOf[c]]));
            BiomeClassification.PrecipitationThresholds thresholds =
                BiomeClassification.ComputeThresholdsForVegetatedLand(landSamples);

            var result = new Biome[tileOf.Length];
            for (int c = 0; c < tileOf.Length; c++)
                result[c] = BiomeClassification.Classify(
                    temperatureK[c], isOceanic[c], precip.Precipitation[tileOf[c]], thresholds);
            if (iceClass != null)
                for (int c = 0; c < tileOf.Length; c++)
                {
                    if (iceClass[c] == LakesIceErosion.IceClass.PermanentIce)
                        result[c] = isOceanic[c] ? Biome.SeaIce : Biome.IceSheet;
                    else if (snowTundra && !isOceanic[c]
                             && iceClass[c] == LakesIceErosion.IceClass.SeasonalSnow)
                        result[c] = Biome.Tundra;
                }
            return result;
        }

        /// <summary>Két biome-térkép egyezése — külön a szárazföldön, mert a képen az látszik.</summary>
        private static void ReportBiome(string label, Biome[] baseline, Biome[] other, bool[] isOceanic)
        {
            int all = 0, land = 0, landSame = 0, allSame = 0;
            for (int c = 0; c < baseline.Length; c++)
            {
                all++;
                if (baseline[c] == other[c]) allSame++;
                if (isOceanic[c]) continue;
                land++;
                if (baseline[c] == other[c]) landSame++;
            }
            Console.WriteLine($"    {label}: egyezés {allSame}/{all} ({100.0 * allSame / all:F1}%), " +
                              $"szárazföldön {landSame}/{land} " +
                              $"({(land > 0 ? 100.0 * landSame / land : 0.0):F1}%)");
        }

        /// <summary>Az ÓCEÁNI tengeri jég aránya — a sarki jéggyűrű a képen is látszik.</summary>
        private static void ReportSeaIce(string label, Biome[] biomes, bool[] isOceanic)
        {
            int ocean = 0, seaIce = 0;
            for (int c = 0; c < biomes.Length; c++)
            {
                if (!isOceanic[c]) continue;
                ocean++;
                if (biomes[c] == Biome.SeaIce) seaIce++;
            }
            Console.WriteLine($"    tengeri jég {label}: {seaIce}/{ocean} " +
                              $"({(ocean > 0 ? 100.0 * seaIce / ocean : 0.0):F1}%)");
        }

        /// <summary>A szárazföldi biome-megoszlás — ez mutatja meg, MERRE tolódik a kép.</summary>
        private static void ReportBiomeHistogram(string label, Biome[] biomes, bool[] isOceanic)
        {
            var counts = new Dictionary<Biome, int>();
            int land = 0;
            for (int c = 0; c < biomes.Length; c++)
            {
                if (isOceanic[c]) continue;
                land++;
                counts.TryGetValue(biomes[c], out int n);
                counts[biomes[c]] = n + 1;
            }
            var parts = new List<string>();
            foreach (Biome b in new[] { Biome.IceSheet, Biome.Tundra, Biome.Desert, Biome.Grassland,
                                        Biome.TemperateForest, Biome.Savanna, Biome.Rainforest })
            {
                counts.TryGetValue(b, out int n);
                parts.Add($"{b} {(land > 0 ? 100.0 * n / land : 0.0):F1}%");
            }
            Console.WriteLine($"    szárazföldi megoszlás {label}: {string.Join(", ", parts)}");
        }

        /// <summary>
        /// ND-161: ugyanaz az éves éghajlat DURVÁBB rácson, majd a durva
        /// jégosztály felnagyítva a teljes szintre és összevetve.
        ///
        /// A KÉRDÉS. A level-6 kétmenetes futás 117 s; ez a Buildbe szinkron
        /// módon nem fér bele. Mielőtt gyorsítótárat és háttérszálat
        /// építenénk rá, meg kell mérni az olcsóbb választ: az éghajlat SIMA
        /// mező (a hőmérséklet lényegében a szélesség és a magasság
        /// függvénye), tehát lehet, hogy durvább rácson számolva is ugyanazt a
        /// jégmaszkot adja — negyedáron.
        ///
        /// A durva cella indexe: u >> d, v >> d ugyanazon a lapon (a sűrű
        /// index definíció szerint face·n² + u·n + v, tehát a szülő index
        /// egyszerű bitléptetés).
        ///
        /// FIGYELEM: a durva szint SAJÁT eleváció-mezőt és tengerszintet kap
        /// (a világ minden szinten a saját láncából származik), tehát ez nem
        /// puszta átlagolás — a mérés pont azt mondja meg, hogy ez a
        /// különbség számít-e a jégosztályon.
        /// </summary>
        private static void CompareCoarseClimate(int coarseLevel, DenseGridMetrics fineGrid, Options options,
            ThermalOrbit orbit, ThermalModelParameters parameters, ThermalClimate fine, Stopwatch sw,
            bool ownWorld, double[] fineElevation, double fineSeaLevel)
        {
            if (coarseLevel < 1 || coarseLevel >= options.Level)
            {
                Console.WriteLine($"  [durva éghajlat] a {coarseLevel}. szint érvénytelen (1 ≤ L < {options.Level})");
                return;
            }

            DenseGridMetrics coarseGrid = DenseGridMetrics.Build(coarseLevel);
            int coarseSide = coarseGrid.Side;
            int downShift = options.Level - coarseLevel;
            var coarseKinds = new SurfaceThermalKind[coarseGrid.CellCount];
            var coarseElevation = new double[coarseGrid.CellCount];
            double coarseSeaLevel;

            if (ownWorld)
            {
                // A durva szint SAJÁT világa: saját eleváció-lánc és saját
                // kalibrált tengerszint - ahogy a projekt a világot minden
                // szinten definiálja.
                Dictionary<TileId, double> coarseField = SeaLevelCalibration.ComputeElevationField(
                    options.Seed, options.Plates, coarseLevel);
                coarseSeaLevel = SeaLevelCalibration.CalibrateSeaLevel(
                    coarseField.Values, options.TargetWaterFraction);
                for (int face = 0; face < 6; face++)
                    for (int u = 0; u < coarseSide; u++)
                        for (int v = 0; v < coarseSide; v++)
                        {
                            int index = DenseGridMetrics.Index(face, u, v, coarseSide);
                            double h = coarseField[TileId.FromFaceLevelUV(face, coarseLevel, (uint)u, (uint)v)];
                            coarseElevation[index] = h;
                            coarseKinds[index] = h < coarseSeaLevel ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land;
                        }
            }
            else
            {
                // UGYANAZ a világ, csak durvább rácson: a teljes szintű
                // eleváció blokk-átlaga és a teljes szintű tengerszint. Ez
                // választja SZÉT a rácsfelbontás hatását a világdefiníció hatásától.
                coarseSeaLevel = fineSeaLevel;
                int per = 1 << downShift;
                var counts = new int[coarseGrid.CellCount];
                int fineSideLocal = fineGrid.Side;
                for (int face = 0; face < 6; face++)
                    for (int u = 0; u < fineSideLocal; u++)
                        for (int v = 0; v < fineSideLocal; v++)
                        {
                            int coarseIndex = DenseGridMetrics.Index(face, u >> downShift, v >> downShift, coarseSide);
                            coarseElevation[coarseIndex] += fineElevation[
                                DenseGridMetrics.Index(face, u, v, fineSideLocal)];
                            counts[coarseIndex]++;
                        }
                for (int index = 0; index < coarseGrid.CellCount; index++)
                {
                    coarseElevation[index] /= counts[index] > 0 ? counts[index] : per * per;
                    coarseKinds[index] = coarseElevation[index] < coarseSeaLevel
                        ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land;
                }
            }

            sw.Restart();
            ThermalClimate coarse = ThermalClimateCalculator.Compute(coarseGrid, coarseKinds, coarseElevation,
                coarseSeaLevel, options.Seed, options.TimeMyr * 1.0e6, orbit, parameters, options.SampleDays,
                useParallelLocalStep: options.Parallel,
                permanentIcePercentile: options.PermanentIcePercentile);
            double coarseMs = sw.Elapsed.TotalMilliseconds;

            int shift = options.Level - coarseLevel;
            int fineSide = fineGrid.Side;
            int agree = 0, permanentAgree = 0, finePermanent = 0, coarsePermanent = 0;
            double meanAbsDelta = 0.0, maxAbsDelta = 0.0;

            // ND-161 harmadik változat: a durva éves mező + PER-FINOM-CELLA
            // magasságkorrekció. Az ND-100 18. pontja szerint a magasságtag
            // tiszta lokális függvény (−lapse · max(0, h − tengerszint)), tehát
            // a drága solver-rész (advekció, szél, sugárzás) durván számolható,
            // és a finom relief utólag rátehető. A korrekció a MIN-re is
            // ugyanaz az eltolás, mert a teljes profilt mozgatja.
            var correctedMean = new double[fineGrid.CellCount];
            var correctedMin = new double[fineGrid.CellCount];
            for (int face = 0; face < 6; face++)
                for (int u = 0; u < fineSide; u++)
                    for (int v = 0; v < fineSide; v++)
                    {
                        int fineIndex = DenseGridMetrics.Index(face, u, v, fineSide);
                        int coarseIndex = DenseGridMetrics.Index(face, u >> shift, v >> shift, coarseSide);

                        LakesIceErosion.IceClass a = fine.RefinedClass[fineIndex];
                        LakesIceErosion.IceClass b = coarse.RefinedClass[coarseIndex];
                        if (a == b) agree++;
                        if (a == LakesIceErosion.IceClass.PermanentIce) finePermanent++;
                        if (b == LakesIceErosion.IceClass.PermanentIce)
                        {
                            coarsePermanent++;
                            if (a == LakesIceErosion.IceClass.PermanentIce) permanentAgree++;
                        }

                        double delta = Math.Abs(fine.Refined.MeanSurfaceK[fineIndex]
                                                - coarse.Refined.MeanSurfaceK[coarseIndex]);
                        meanAbsDelta += delta;
                        if (delta > maxAbsDelta) maxAbsDelta = delta;

                        correctedMean[fineIndex] = SurfaceTemperatureField.AltitudeCorrectedK(
                            coarse.Refined.MeanSurfaceK[coarseIndex], coarseElevation[coarseIndex],
                            fineElevation[fineIndex], coarseSeaLevel);
                        correctedMin[fineIndex] = SurfaceTemperatureField.AltitudeCorrectedK(
                            coarse.Refined.MinSurfaceK[coarseIndex], coarseElevation[coarseIndex],
                            fineElevation[fineIndex], coarseSeaLevel);
                    }

            int total = fineGrid.CellCount;
            int unionPermanent = finePermanent + coarsePermanent - permanentAgree;
            Console.WriteLine($"  [durva éghajlat, level {coarseLevel}, " +
                              (ownWorld ? "SAJÁT világ" : "LEMINTAVETELEZETT világ") + $"] {coarseMs / 1000.0:F1} s " +
                              $"({coarseGrid.CellCount} cella, {(double)fineGrid.CellCount / coarseGrid.CellCount:F0}× kevesebb)");
            Console.WriteLine($"    jégosztály-egyezés a teljes szinttel: {agree}/{total} " +
                              $"({100.0 * agree / total:F2}%)");
            Console.WriteLine($"    tartós jég: teljes={finePermanent}, durva(felnagyítva)={coarsePermanent}, " +
                              $"metszet={permanentAgree} (Jaccard {(unionPermanent > 0 ? 100.0 * permanentAgree / unionPermanent : 100.0):F1}%)");
            Console.WriteLine($"    éves átlag eltérés: átlag {meanAbsDelta / total:F2} K, max {maxAbsDelta:F2} K");

            // A korrigált változat SAJÁT percentilis-küszöböt kap, a finom
            // eloszlásából - különben a durva küszöböt mérnénk össze egy
            // másik eloszlással, és nem a módszert, hanem az eltolódást.
            ThermalIceClassification.IceThresholds correctedThresholds = options.PermanentIcePercentile.HasValue
                ? ThermalIceClassification.ComputeThresholds(correctedMean, options.PermanentIcePercentile.Value)
                : ThermalIceClassification.IceThresholds.Absolute;

            int cAgree = 0, cPermanent = 0, cPermanentAgree = 0;
            double cMeanDelta = 0.0, cMaxDelta = 0.0;
            for (int c = 0; c < total; c++)
            {
                LakesIceErosion.IceClass corrected = ThermalIceClassification.Classify(
                    correctedMean[c], correctedMin[c], correctedThresholds);
                if (corrected == fine.RefinedClass[c]) cAgree++;
                if (corrected == LakesIceErosion.IceClass.PermanentIce)
                {
                    cPermanent++;
                    if (fine.RefinedClass[c] == LakesIceErosion.IceClass.PermanentIce) cPermanentAgree++;
                }
                double d = Math.Abs(fine.Refined.MeanSurfaceK[c] - correctedMean[c]);
                cMeanDelta += d;
                if (d > cMaxDelta) cMaxDelta = d;
            }
            int cUnion = finePermanent + cPermanent - cPermanentAgree;
            Console.WriteLine($"    + MAGASSÁGKORREKCIÓVAL: egyezés {cAgree}/{total} ({100.0 * cAgree / total:F2}%), " +
                              $"tartós jég={cPermanent}, metszet={cPermanentAgree} " +
                              $"(Jaccard {(cUnion > 0 ? 100.0 * cPermanentAgree / cUnion : 100.0):F1}%), " +
                              $"eltérés átlag {cMeanDelta / total:F2} K, max {cMaxDelta:F2} K");
        }

        /// <summary>
        /// ND-159: a bázis-hőmérséklet TAGONKÉNTI, területtel súlyozott globális átlaga.
        ///
        /// A β-söprés megmutatta, hogy a modell globálisan meleg (medián éves
        /// felszíni átlag +33,7 °C), de nem azt, hogy MELYIK tagtól. A bázis
        /// szerkezete (ND-100):
        ///
        ///   Bs = T_rad(f_eff) + T_üvegház + T_óceán + T_meridionális − T_magasság + T_ciklus
        ///
        /// Minden tag a NYILVÁNOS felületből származtatható, ezért itt nincs új
        /// modellezés: a T_rad-ot a többi tag kivonásával kapjuk vissza (óceánon
        /// a 0,3-as pufferelés miatt egy lineáris egyenletet megoldva).
        /// </summary>
        private static void Decompose(DenseGridMetrics grid, SurfaceThermalKind[] kinds, double[] elevation,
            double seaLevel, Options options, ThermalOrbit orbit, ThermalModelParameters parameters,
            ThermalClimate climate)
        {
            var field = new SurfaceTemperatureField(grid, climate.RefinedKinds.ToArray(), elevation, seaLevel,
                options.Seed, options.TimeMyr * 1.0e6, orbit, parameters);
            ThermalBaseline baseline = field.Baseline;
            ReportTransportBalance(grid, baseline, parameters);

            // A bázis időfüggő (napi faktor), ezért a mintanapok déli
            // időpontjaira átlagolunk - ugyanazokra a napokra, amiket az éves
            // statisztika is használ.
            long[] days = ThermalAnnualStatisticsCalculator.SampleDayIndices(
                options.OrbitalPeriodDays, options.SampleDays);
            var dailyFactor = new double[grid.CellCount];
            var baseK = new double[grid.CellCount];
            var baseSum = new double[grid.CellCount];
            foreach (long day in days)
            {
                baseline.Sample(day * SimulationTime.SecondsPerDay, dailyFactor, baseK);
                for (int c = 0; c < grid.CellCount; c++) baseSum[c] += baseK[c];
            }

            double totalArea = 0.0;
            double wRad = 0.0, wOcean = 0.0, wMerid = 0.0, wAlt = 0.0, wBase = 0.0;
            int coldestCell = 0;
            for (int c = 1; c < grid.CellCount; c++)
                if (climate.Refined.MeanSurfaceK[c] < climate.Refined.MeanSurfaceK[coldestCell])
                    coldestCell = c;
            double coldRad = 0.0, coldOcean = 0.0, coldMerid = 0.0, coldAlt = 0.0, coldBase = 0.0;
            for (int c = 0; c < grid.CellCount; c++)
            {
                double area = grid.Area[c];
                double bs = baseSum[c] / days.Length;
                double merid = baseline.AnnualTransportCorrectionK[c];
                double alt = Temperature.LapseRateKPerM * Math.Max(0.0, elevation[c] - seaLevel);
                double rest = bs - baseline.GreenhouseK - merid + alt - baseline.CycleK;

                double rad, ocean;
                if (climate.RefinedKinds[c] == SurfaceThermalKind.Ocean)
                {
                    // rest = T_rad + 0,3*(annualMeanRad - T_rad) = 0,7*T_rad + 0,3*annualMeanRad
                    double annualMeanRad = baseline.AnnualMeanRadiativeK[c];
                    rad = (rest - Temperature.OceanBufferingStrength * annualMeanRad)
                          / (1.0 - Temperature.OceanBufferingStrength);
                    ocean = Temperature.OceanBufferingStrength * (annualMeanRad - rad);
                }
                else
                {
                    rad = rest;
                    ocean = 0.0;
                }

                totalArea += area;
                wRad += area * rad;
                wOcean += area * ocean;
                wMerid += area * merid;
                wAlt += area * alt;
                wBase += area * bs;
                if (c == coldestCell)
                {
                    coldRad = rad;
                    coldOcean = ocean;
                    coldMerid = merid;
                    coldAlt = alt;
                    coldBase = bs;
                }
            }

            Console.WriteLine("  BÁZIS-FELBONTÁS (területtel súlyozott globális átlag, K):");
            Console.WriteLine($"    T_rad(f_eff)     {wRad / totalArea,8:F2}");
            Console.WriteLine($"    T_üvegház        {baseline.GreenhouseK,8:F2}");
            Console.WriteLine($"    T_meridionális    {wMerid / totalArea,8:F2}");
            Console.WriteLine($"    T_óceán          {wOcean / totalArea,8:F2}");
            Console.WriteLine($"    T_magasság       {-wAlt / totalArea,8:F2}");
            Console.WriteLine($"    T_ciklus         {baseline.CycleK,8:F2}");
            Console.WriteLine($"    = bázis átlag     {wBase / totalArea,8:F2} K " +
                              $"({wBase / totalArea - 273.15:F2} °C)");
            Console.WriteLine($"    LEGHIDEGEBB ÉVES CELLA: index={coldestCell}, " +
                              $"típus={climate.RefinedKinds[coldestCell]}, z={grid.CenterZ[coldestCell]:F3}, " +
                              $"éves={climate.Refined.MeanSurfaceK[coldestCell] - 273.15:F2} °C");
            Console.WriteLine($"      bázis={coldBase - 273.15:F2} °C; radiatív={coldRad:F2} K, " +
                              $"óceáni={coldOcean:F2} K, meridionális={coldMerid:+0.00;-0.00;0.00} K, " +
                              $"magasság=-{coldAlt:F2} K, ciklus={baseline.CycleK:F2} K");
        }

        private static void ReportTransportBalance(DenseGridMetrics grid, ThermalBaseline baseline,
            ThermalModelParameters parameters)
        {
            double[] conductance = MeridionalEnergyBalance.BuildConductance(grid,
                MeridionalEnergyBalance.DiffusionWm2K * parameters.MeridionalTransportScale);
            var transport = new ConservativeHeatTransport(grid.CellCount, grid.EdgeI, grid.EdgeJ, conductance);
            var balanced = new double[grid.CellCount];
            for (int c = 0; c < balanced.Length; c++)
                balanced[c] = baseline.AnnualTargetK[c] + baseline.AnnualTransportCorrectionK[c];
            var power = new double[grid.CellCount];
            transport.ComputePowerW(balanced, power);
            double area = 0.0, netPower = 0.0, correction = 0.0, maxResidual = 0.0;
            for (int c = 0; c < balanced.Length; c++)
            {
                area += grid.Area[c];
                netPower += power[c];
                correction += grid.Area[c] * baseline.AnnualTransportCorrectionK[c];
                double residual = MeridionalEnergyBalance.RadiativeFeedbackWm2K
                    * baseline.AnnualTransportCorrectionK[c] - power[c] / grid.Area[c];
                maxResidual = Math.Max(maxResidual, Math.Abs(residual));
            }
            Console.WriteLine($"  ÉVES MÉRLEG: max maradék={maxResidual:E6} W/m², " +
                $"nettó belső teljesítmény={netPower:E6} W ({netPower / area:E6} W/m²), " +
                $"területi átlagkorrekció={correction / area:E6} K");
        }

        private static void ReportPhysicalClimate(DenseGridMetrics grid, ThermalClimate climate)
        {
            double area = 0.0, surface = 0.0, air = 0.0, warmIceArea = 0.0;
            var polarArea = new double[2];
            var polarAir = new double[2];
            // |z| >= sqrt(3)/2: a 60 fokon túli sáv, trigonometrikus művelet nélkül.
            double polarZ = Math.Sqrt(3.0) / 2.0;
            for (int c = 0; c < grid.CellCount; c++)
            {
                double a = grid.Area[c];
                area += a;
                surface += a * climate.Refined.MeanSurfaceK[c];
                air += a * climate.Refined.MeanAirK[c];
                if (climate.RefinedClass[c] == LakesIceErosion.IceClass.PermanentIce
                    && climate.Refined.MeanSurfaceK[c] >= 273.15)
                    warmIceArea += a;
                if (Math.Abs(grid.CenterZ[c]) < polarZ) continue;
                int hemisphere = grid.CenterZ[c] >= 0.0 ? 0 : 1;
                polarArea[hemisphere] += a;
                polarAir[hemisphere] += a * climate.Refined.MeanAirK[c];
            }
            Console.WriteLine($"  TERÜLETI ÉVES ÁTLAG: felszín={surface / area - 273.15:F3} °C, " +
                $"levegő={air / area - 273.15:F3} °C; meleg tartós jég={100.0 * warmIceArea / area:F3}% bolygóterület");
            for (int h = 0; h < 2; h++)
                Console.WriteLine(polarArea[h] > 0.0
                    ? $"    {(h == 0 ? "északi" : "déli")} sáv (60–90°): levegő={polarAir[h] / polarArea[h] - 273.15:F3} °C"
                    : $"    {(h == 0 ? "északi" : "déli")} sáv (60–90°): nincs cellaközép ezen a rácson");
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

        /// <summary>A rendezett minta q-percentilise - ugyanaz az index-keplet, mint a BiomeClassification-ben.</summary>
        private static double Percentile(double[] sorted, double q)
        {
            int idx = (int)(q * sorted.Length);
            if (idx < 0) idx = 0;
            if (idx > sorted.Length - 1) idx = sorted.Length - 1;
            return sorted[idx];
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
