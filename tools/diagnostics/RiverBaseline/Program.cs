using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Climate;

internal static class Program
{
    private const ulong Seed = 0xA7C944210000UL;

    // Diagnosztika: a ritka escape-/összefolyási ugrások ne maradjanak
    // rejtve a teljes pontszám mögött. Nem módosítja a követést.
    private static void WriteGeometryReport(string prefix,
        IReadOnlyList<RiverPathTracing.ContinuousRiverPath> rivers)
    {
        using var report = new StreamWriter(prefix + "-geometry.csv");
        report.WriteLine("index,points,lengthKm,longEdges,longEdgeLengthKm,maxEdgeKm,mergeEdgeKm,sharpTurns");
        double total = 0.0, longTotal = 0.0, maxMerge = 0.0;
        int merges = 0, totalTurns = 0;
        long totalPoints = 0;
        foreach (var river in rivers)
        {
            double length = 0.0, longLength = 0.0, maxEdge = 0.0, lastEdge = 0.0;
            int longEdges = 0, turns = 0;
            for (int i = 1; i < river.Points.Count; i++)
            {
                var a = river.Points[i - 1]; var b = river.Points[i];
                double dx = b.X-a.X, dy = b.Y-a.Y, dz = b.Z-a.Z;
                double norm = Math.Sqrt(dx*dx+dy*dy+dz*dz);
                double meters = norm * WorldGen.Core.PlanetConstants.RadiusMeters;
                lastEdge = meters; length += meters; maxEdge = Math.Max(maxEdge, meters);
                if (meters > 75.0) { longEdges++; longLength += meters; }
                if (i > 1 && norm > 0.0)
                {
                    var p = river.Points[i - 2];
                    double px=a.X-p.X, py=a.Y-p.Y, pz=a.Z-p.Z;
                    double previousNorm=Math.Sqrt(px*px+py*py+pz*pz);
                    if(previousNorm>0.0 && (dx*px+dy*py+dz*pz)/(norm*previousNorm)<Math.Cos(Math.PI/6.0)) turns++;
                }
            }
            double mergeEdge = river.Termination == RiverPathTracing.TerminationReason.Merged ? lastEdge : 0.0;
            if (river.Termination == RiverPathTracing.TerminationReason.Merged) merges++;
            totalTurns += turns; totalPoints += river.Points.Count;
            total += length; longTotal += longLength; maxMerge = Math.Max(maxMerge, mergeEdge);
            report.WriteLine(FormattableString.Invariant($"{river.SourceIndex},{river.Points.Count},{length/1000.0:R},{longEdges},{longLength/1000.0:R},{maxEdge/1000.0:R},{mergeEdge/1000.0:R},{turns}"));
        }
        Console.WriteLine(FormattableString.Invariant($"geometry: lengthKm={total/1000.0:F3} longEdgePercent={100.0*longTotal/Math.Max(total,1.0):F2} maxMergeEdgeKm={maxMerge/1000.0:F3} merges={merges} sharpTurns={totalTurns} points={totalPoints}"));
    }

    // ================= ND-196 MEROPAD (2026-10-06) =======================
    // A folyo-elhelyezes CSAPADEK-FUGGESE, ugyanaz a harom tabla, amit az
    // ND-196 merese adott: (1) a forrasok globalis szarazfoldi csapadek-
    // percentilise medencenkent, (2) a forrasok biome-megoszlasa, (3) a
    // NYOMVONAL biome-megoszlasa a szarazfoldi alaphoz mert gazdagodassal.
    //
    // KET MEZON mer, mert MERVE nem ugyanazt mutatjak:
    //   "biome"   - ANALITIKUS csapadek+homerseklet (a viewer ELONEZETE),
    //   "thermal" - a HOMODELL eves eghajlatabol (ez van a felhasznalo
    //               kepernyojen, es ezen latszik a 2,45x-os Desert-
    //               gazdagodas; itt a szarazfold tobb mint felenek PONTOSAN
    //               nulla a csapadeka).
    // A "thermal" ut UGYANAZT a Core-lancot futtatja, amit a viewer
    // hatterszala: ThermalClimateCalculator.Compute -> Refined.MeanSurfaceK
    // + eves szel -> MoisturePrecipitation.ComputeWithClimateFields.
    // KOZELITES, dokumentaltan: a biome itt a NYERS tile-erteket osztalyozza,
    // a viewer az interpolalt sarok-tablat; a felszintipus-terkep Land/Ocean
    // (a viewer to-tile-jai a hidrologia-szinten, level 7-8-on allnak, tehat
    // a level 5-os kulcsokkal ott sem talalkoznak).
    private static void ReportNd196(
        string prefix, string label,
        MoisturePrecipitation.PrecipitationField precip,
        Dictionary<TileId, double> biomeTemperatureK,
        Dictionary<TileId, bool>? permanentIce,
        FlowNetwork.FloodResult flood,
        List<TileId> sources,
        List<RiverPathTracing.ContinuousRiverPath> network,
        int level)
    {
        var vegetatedSamples = new List<(double TemperatureK, double Precipitation)>();
        var landPrecipSorted = new List<double>();
        foreach (KeyValuePair<TileId, bool> kv in precip.IsOcean)
        {
            if (kv.Value) continue;
            vegetatedSamples.Add((biomeTemperatureK[kv.Key], precip.Precipitation[kv.Key]));
            landPrecipSorted.Add(precip.Precipitation[kv.Key]);
        }
        landPrecipSorted.Sort();
        var thresholds = BiomeClassification.ComputeThresholdsForVegetatedLand(vegetatedSamples);

        // Tile -> szarazfoldi csapadek-percentilis, ELORE kiszamolva: a
        // nyomvonal ~470 000 pontjara a lineáris kereses 1e9 muvelet lenne.
        var percentileOfTile = new Dictionary<TileId, double>(landPrecipSorted.Count);
        foreach (KeyValuePair<TileId, bool> kv in precip.IsOcean)
        {
            if (kv.Value) continue;
            double value = precip.Precipitation[kv.Key];
            int lo = 0, hi = landPrecipSorted.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (landPrecipSorted[mid] < value) lo = mid + 1; else hi = mid;
            }
            percentileOfTile[kv.Key] = 100.0 * lo / Math.Max(1, landPrecipSorted.Count);
        }

        var biomeOf = new Dictionary<Biome, int>();
        var biomeAt = new Dictionary<TileId, Biome>(precip.IsOcean.Count);
        int landTiles = 0, zeroPrecipLand = 0;
        foreach (KeyValuePair<TileId, bool> kv in precip.IsOcean)
        {
            bool ice = permanentIce != null && permanentIce.TryGetValue(kv.Key, out bool i) && i;
            Biome biome = permanentIce == null
                ? BiomeClassification.Classify(
                    biomeTemperatureK[kv.Key], kv.Value, precip.Precipitation[kv.Key], thresholds)
                : BiomeClassification.ClassifyWithIce(
                    biomeTemperatureK[kv.Key], kv.Value, precip.Precipitation[kv.Key], thresholds, ice);
            biomeAt[kv.Key] = biome;
            if (kv.Value) continue;
            landTiles++;
            if (precip.Precipitation[kv.Key] <= 0.0) zeroPrecipLand++;
            biomeOf.TryGetValue(biome, out int c);
            biomeOf[biome] = c + 1;
        }
        Console.WriteLine(FormattableString.Invariant(
            $"[{label}] field: landTiles={landTiles} zeroPrecipLand={zeroPrecipLand} ({100.0 * zeroPrecipLand / Math.Max(1, landTiles):F1}%) arid={thresholds.Arid:R} semiArid={thresholds.SemiArid:R} moist={thresholds.Moist:R}"));

        // Percentilis: a szarazfoldi tile-ok hany szazalekanak KISEBB a
        // csapadeka. Nulla csapadeku tile-ra 0,0 - ugyanaz a konvencio, amit
        // az ND-196 naploja hasznalt.
        double PercentileOf(double value)
        {
            int below = 0;
            for (int i = 0; i < landPrecipSorted.Count; i++)
                if (landPrecipSorted[i] < value) below++;
            return 100.0 * below / Math.Max(1, landPrecipSorted.Count);
        }

        // Medence-hozzarendeles: melyik vizgyujtobe esik egy forras, es mekkora
        // az adott medence JELOLT-csapadek-osszege (ez a kvota sulya).
        Dictionary<TileId, List<TileId>> basinsOf =
            WorldGen.Core.Features.FeatureSegmentation.FindWatershedRegions(flood.Parent, precip.IsOcean);
        var basinOutlets = new List<TileId>(basinsOf.Keys);
        basinOutlets.Sort((a, b) =>
        {
            int bySize = basinsOf[b].Count.CompareTo(basinsOf[a].Count);
            return bySize != 0 ? bySize : a.Value.CompareTo(b.Value);
        });
        var basinIndexOfTile = new Dictionary<TileId, int>();
        for (int b = 0; b < basinOutlets.Count; b++)
            foreach (TileId t in basinsOf[basinOutlets[b]]) basinIndexOfTile[t] = b;
        var sourcesInBasin = new Dictionary<int, List<TileId>>();
        foreach (TileId s in sources)
        {
            int index = basinIndexOfTile.TryGetValue(s, out int found) ? found : -1;
            if (!sourcesInBasin.TryGetValue(index, out List<TileId>? list))
            {
                list = new List<TileId>();
                sourcesInBasin[index] = list;
            }
            list.Add(s);
        }

        int zeroSources = 0;
        var sourcePercentiles = new List<double>();
        using (var basinReport = new StreamWriter(prefix + "-nd196-" + label + "-basins.csv"))
        {
            basinReport.WriteLine("basin,tiles,candidates,candidatePrecipSum,sources,sourcePercentiles");
            for (int b = 0; b < basinOutlets.Count; b++)
            {
                List<TileId> basin = basinsOf[basinOutlets[b]];
                int candidates = 0;
                double weight = 0.0;
                foreach (TileId t in basin)
                {
                    if (!precip.Elevation.TryGetValue(t, out double elevation)) continue;
                    if (elevation < precip.SeaLevel + RiverPathTracing.DefaultMinElevAboveSeaM) continue;
                    if (!precip.Precipitation.TryGetValue(t, out double p)) continue;
                    candidates++;
                    if (p > 0.0) weight += p;
                }
                sourcesInBasin.TryGetValue(b, out List<TileId>? chosen);
                int chosenCount = chosen == null ? 0 : chosen.Count;
                if (basin.Count < RiverPathTracing.DefaultMinBasinTiles && chosenCount == 0) continue;
                var percentileText = new List<string>();
                if (chosen != null)
                    foreach (TileId s in chosen)
                    {
                        double pct = PercentileOf(precip.Precipitation[s]);
                        sourcePercentiles.Add(pct);
                        if (precip.Precipitation[s] <= 0.0) zeroSources++;
                        percentileText.Add(pct.ToString("F1", CultureInfo.InvariantCulture));
                    }
                string row = FormattableString.Invariant(
                    $"{b},{basin.Count},{candidates},{weight:R},{chosenCount},{string.Join(" ", percentileText)}");
                basinReport.WriteLine(row);
                Console.WriteLine($"[{label}] basin " + row);
            }
        }
        sourcePercentiles.Sort();
        double medianPct = sourcePercentiles.Count == 0 ? 0.0 : sourcePercentiles[sourcePercentiles.Count / 2];
        Console.WriteLine(FormattableString.Invariant(
            $"[{label}] sources: count={sources.Count} zeroPrecip={zeroSources} ({100.0 * zeroSources / Math.Max(1, sources.Count):F1}%) medianPercentile={medianPct:F1}"));

        var sourceBiome = new Dictionary<Biome, int>();
        foreach (TileId s in sources)
        {
            Biome biome = biomeAt[s];
            sourceBiome.TryGetValue(biome, out int c);
            sourceBiome[biome] = c + 1;
        }

        var pathPoints = new Dictionary<Biome, long>();
        var pathMeters = new Dictionary<Biome, double>();
        long totalPathPoints = 0, oceanPoints = 0, zeroPrecipPathPoints = 0;
        double totalPathMeters = 0.0, pathPercentileSum = 0.0;
        var pathPercentiles = new List<double>();
        foreach (var river in network)
        {
            for (int i = 0; i < river.Points.Count; i++)
            {
                var p = river.Points[i];
                TileId tile = TileGeometry.FromPosition(p.X, p.Y, p.Z, level);
                if (!biomeAt.TryGetValue(tile, out Biome biome)) continue;
                if (precip.IsOcean[tile]) { oceanPoints++; continue; }
                double meters = 0.0;
                if (i > 0)
                {
                    var a = river.Points[i - 1];
                    double dx = p.X - a.X, dy = p.Y - a.Y, dz = p.Z - a.Z;
                    meters = Math.Sqrt(dx * dx + dy * dy + dz * dz) * WorldGen.Core.PlanetConstants.RadiusMeters;
                }
                if (percentileOfTile.TryGetValue(tile, out double tilePercentile))
                {
                    pathPercentiles.Add(tilePercentile);
                    pathPercentileSum += tilePercentile;
                    if (precip.Precipitation[tile] <= 0.0) zeroPrecipPathPoints++;
                }
                pathPoints.TryGetValue(biome, out long pc);
                pathPoints[biome] = pc + 1;
                pathMeters.TryGetValue(biome, out double pm);
                pathMeters[biome] = pm + meters;
                totalPathPoints++;
                totalPathMeters += meters;
            }
        }
        pathPercentiles.Sort();
        double pathMedianPercentile = pathPercentiles.Count == 0 ? 0.0
            : pathPercentiles[pathPercentiles.Count / 2];
        double pathMeanPercentile = pathPercentiles.Count == 0 ? 0.0
            : pathPercentileSum / pathPercentiles.Count;
        Console.WriteLine(FormattableString.Invariant(
            $"[{label}] network: rivers={network.Count} landPoints={totalPathPoints} oceanPoints={oceanPoints} landKm={totalPathMeters / 1000.0:F1}"));
        // A BIOME-TOL FUGGETLEN kapu: a nyomvonal alatti szarazfold csapadek-
        // percentilise. Ez nem fugg a biome-vagopontoktol es az interpolacios
        // konvenciotol, tehat ez az ND-196 elsodleges merteke.
        Console.WriteLine(FormattableString.Invariant(
            $"[{label}] pathPrecip: medianPercentile={pathMedianPercentile:F1} meanPercentile={pathMeanPercentile:F1} zeroPrecipPoints={zeroPrecipPathPoints} ({100.0 * zeroPrecipPathPoints / Math.Max(1, totalPathPoints):F1}%)"));

        using (var enrich = new StreamWriter(prefix + "-nd196-" + label + "-enrichment.csv"))
        {
            enrich.WriteLine("biome,pathPointPercent,pathLengthPercent,sourcePercent,landBasePercent,enrichment");
            var order = new List<Biome>
            {
                Biome.Desert, Biome.Grassland, Biome.Savanna,
                Biome.TemperateForest, Biome.Rainforest, Biome.Tundra, Biome.IceSheet,
            };
            foreach (Biome biome in order)
            {
                pathPoints.TryGetValue(biome, out long pc);
                pathMeters.TryGetValue(biome, out double pm);
                sourceBiome.TryGetValue(biome, out int sc);
                biomeOf.TryGetValue(biome, out int lc);
                double pathPct = 100.0 * pc / Math.Max(1, totalPathPoints);
                double lengthPct = 100.0 * pm / Math.Max(1.0, totalPathMeters);
                double sourcePct = 100.0 * sc / Math.Max(1, sources.Count);
                double basePct = 100.0 * lc / Math.Max(1, landTiles);
                double enrichment = basePct <= 0.0 ? 0.0 : pathPct / basePct;
                string row = FormattableString.Invariant(
                    $"{biome},{pathPct:F2},{lengthPct:F2},{sourcePct:F2},{basePct:F2},{enrichment:F2}");
                enrich.WriteLine(row);
                Console.WriteLine($"[{label}] enrich " + row);
            }
        }
    }

    /// <summary>
    /// ND-196 "thermal" ut: a HOMODELL eves eghajlatabol szamolt csapadek-mezo,
    /// ugyanazzal a Core-lanccal, amit a viewer hatterszala futtat.
    /// </summary>
    private static MoisturePrecipitation.PrecipitationField ComputeThermalPrecipitation(
        MoisturePrecipitation.PrecipitationField analytic, int level, double axialTiltDegrees,
        out Dictionary<TileId, double> meanAirK, out Dictionary<TileId, bool> permanentIce)
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(level);
        var kinds = new SurfaceThermalKind[grid.CellCount];
        var elevation = new double[grid.CellCount];
        int side = grid.Side;
        for (int face = 0; face < 6; face++)
            for (int u = 0; u < side; u++)
                for (int v = 0; v < side; v++)
                {
                    int index = DenseGridMetrics.Index(face, u, v, side);
                    TileId id = TileId.FromFaceLevelUV(face, level, (uint)u, (uint)v);
                    elevation[index] = analytic.Elevation[id];
                    kinds[index] = analytic.IsOcean[id] ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land;
                }

        var orbit = new ThermalOrbit(365.25, 1.0, axialTiltDegrees * Math.PI / 180.0);
        var climateTimer = Stopwatch.StartNew();
        ThermalClimate climate = ThermalClimateCalculator.Compute(
            grid, kinds, elevation, analytic.SeaLevel, Seed, 0.0, orbit,
            useParallelLocalStep: false, includeRefinedWind: true);
        Console.WriteLine(FormattableString.Invariant(
            $"thermalClimateMs={climateTimer.Elapsed.TotalMilliseconds:F0} reclassified={climate.ReclassifiedCells} physicalIce={climate.RefinedPhysicalIce != null}"));

        var surfaceAllK = new Dictionary<TileId, double>(grid.CellCount);
        var wind = new Dictionary<TileId, SurfaceWindSample>(grid.CellCount);
        meanAirK = new Dictionary<TileId, double>(grid.CellCount);
        permanentIce = new Dictionary<TileId, bool>(grid.CellCount);
        for (int face = 0; face < 6; face++)
            for (int u = 0; u < side; u++)
                for (int v = 0; v < side; v++)
                {
                    int index = DenseGridMetrics.Index(face, u, v, side);
                    TileId id = TileId.FromFaceLevelUV(face, level, (uint)u, (uint)v);
                    surfaceAllK[id] = climate.Refined.MeanSurfaceK[index];
                    meanAirK[id] = climate.Refined.MeanAirK[index];
                    wind[id] = new SurfaceWindSample(
                        climate.Refined.MeanWindX![index], climate.Refined.MeanWindY![index],
                        climate.Refined.MeanWindZ![index], climate.Refined.MeanWindSpeedMs![index]);
                    permanentIce[id] = climate.RefinedPhysicalIce != null
                        && climate.RefinedPhysicalIce.PersistenceMarginK[index] < 0.0;
                }

        return MoisturePrecipitation.ComputeWithClimateFields(
            Seed, 20, level, surfaceAllK, wind, 0.0, 365.25, 1.0, axialTiltDegrees,
            targetWaterFraction: 0.65);
    }

    private static int Main(string[] args)
    {
        double timeMyr = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 0.0;
        string output = args.Length > 1 ? args[1] : "artifacts/a8-river-baseline";
        double traceStep = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 50.0;
        // ND-186: az osszefolyasi terbeli tolerancia soporheto, hogy a
        // "mennyi valodi osszefolyas van" kerdes MERT legyen, ne feltetelezett.
        double mergeRadius = args.Length > 4
            ? double.Parse(args[4], CultureInfo.InvariantCulture)
            : ClaimedRiverPoints.DefaultMergeRadiusMeters;
        int limit = args.Length > 2 && args[2] != "endcheck" && args[2] != "biome" && args[2] != "thermal" && args[2] != "independent" && args[2] != "coarse" && args[2] != "lakecross" && args[2] != "parallel" && args[2] != "parallel2" && args[2] != "parallel4"
            ? int.Parse(args[2], CultureInfo.InvariantCulture) : 96;
        Directory.CreateDirectory(output);
        var preparation = Stopwatch.StartNew();
        var initialSeeds = PlateGeneration.GenerateSeeds(Seed, 20);
        var movedSeeds = timeMyr == 0.0 ? initialSeeds : PlateMotion.MovedSeeds(Seed, initialSeeds, timeMyr);
        var precipitation = MoisturePrecipitation.Compute(
            Seed, 20, 5, 10.0, 365.25, 1.0, 23.44,
            targetWaterFraction: 0.65);
        var flood = FlowNetwork.PriorityFlood(precipitation.Elevation, precipitation.IsOcean);
        // ND-189: a forras-szures a KOVETO finom mezojen (worldSeed + seeds +
        // context) es a durva to-mezon (flood.Filled) tortenik.
        var sources = RiverPathTracing.SelectRiverSourcesPerBasin(
            precipitation.Elevation, precipitation.Precipitation,
            precipitation.IsOcean, flood.Parent, precipitation.SeaLevel,
            worldSeed: Seed, seeds: movedSeeds,
            context: DeepTimeContext.AtPlateTime(timeMyr),
            floodFilled: flood.Filled);
        string prefix = Path.Combine(output, "t" + timeMyr.ToString("0.###", CultureInfo.InvariantCulture));
        using var sourceFile = new StreamWriter(prefix + "-sources.csv");
        sourceFile.WriteLine("index,basinRound,tileIdHex");
        for (int i = 0; i < sources.Count; i++)
            sourceFile.WriteLine($"{i},{i / 6},{sources[i].Value:X16}");
        sourceFile.Flush();
        Console.WriteLine($"seed={Seed:X16} timeMyr={timeMyr:R} seaLevel={precipitation.SeaLevel:R} sources={sources.Count} preparationMs={preparation.Elapsed.TotalMilliseconds:F1} cores={Environment.ProcessorCount}");
        // ND-196 (2026-10-06): a ket mero-ut. Ld. a ReportNd196 fejleceben a
        // meroypad leirasat es a kozelitesek listajat.
        if (args.Length > 2 && (args[2] == "biome" || args[2] == "thermal"))
        {
            bool thermalField = args[2] == "thermal";
            double axialTiltDegrees = 23.44;
            MoisturePrecipitation.PrecipitationField measured = precipitation;
            Dictionary<TileId, double> biomeTemperatureK;
            Dictionary<TileId, bool>? permanentIce = null;
            if (thermalField)
            {
                measured = ComputeThermalPrecipitation(
                    precipitation, 5, axialTiltDegrees, out biomeTemperatureK, out permanentIce);
            }
            else
            {
                double axialTilt = axialTiltDegrees * Math.PI / 180.0;
                biomeTemperatureK = new Dictionary<TileId, double>(precipitation.Elevation.Count);
                foreach (KeyValuePair<TileId, double> kv in precipitation.Elevation)
                {
                    TileGeometry.ToPosition(kv.Key, out double tx, out double ty, out double tz);
                    biomeTemperatureK[kv.Key] = Temperature.TemperatureKelvin(
                        tx, ty, tz, 0.0, 365.25, 1.0, axialTilt,
                        precipitation.IsOcean[kv.Key], kv.Value, precipitation.SeaLevel);
                }
            }

            // A forras-kivalasztas es a halozat UGYANAZON a mezon, amit mérunk.
            FlowNetwork.FloodResult measuredFlood = thermalField
                ? FlowNetwork.PriorityFlood(measured.Elevation, measured.IsOcean)
                : flood;
            List<TileId> measuredSources = thermalField
                ? RiverPathTracing.SelectRiverSourcesPerBasin(
                    measured.Elevation, measured.Precipitation, measured.IsOcean,
                    measuredFlood.Parent, measured.SeaLevel,
                    worldSeed: Seed, seeds: movedSeeds,
                    context: DeepTimeContext.AtPlateTime(timeMyr),
                    floodFilled: measuredFlood.Filled)
                : sources;
            var nd196Timer = Stopwatch.StartNew();
            List<RiverPathTracing.ContinuousRiverPath> nd196Network =
                RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                    Seed, movedSeeds, measured.SeaLevel, measuredSources,
                    RiverPathTracing.DefaultFineDepth, stepMeters: traceStep,
                    maxDegreeOfParallelism: 4, context: DeepTimeContext.AtPlateTime(timeMyr),
                    mergeRadiusMeters: mergeRadius);
            Console.WriteLine(FormattableString.Invariant(
                $"networkMs={nd196Timer.Elapsed.TotalMilliseconds:F0} sources={measuredSources.Count}"));
            ReportNd196(prefix, args[2], measured, biomeTemperatureK, permanentIce,
                measuredFlood, measuredSources, nd196Network, 5);
            return 0;
        }

        if (args.Length > 2 && args[2] == "endcheck")
        {
            var terrain = SeaLevelCalibration.ComputeElevationFieldAtTime(Seed, 20, 8, DeepTimeContext.AtPlateTime(timeMyr));
            var ocean = FlowNetwork.ComputeOceanField(terrain, precipitation.SeaLevel);
            var drainage = FlowNetwork.PriorityFlood(terrain, ocean);
            var lakes = LakesIceErosion.IdentifyLakes(terrain, drainage.Filled, ocean);
            var accepted = new HashSet<TileId>();
            foreach (var lake in lakes.Lakes)
                if (lake.TileCount >= 6 && lake.MaxDepth >= 40.0)
                    foreach (var tile in lake.Tiles) accepted.Add(tile);
            using var report = new StreamWriter(prefix + "-pit-lakes.csv");
            report.WriteLine("index,lengthKm,terrainM,filledM,depthM,visibleLake");
            foreach (var row in File.ReadAllLines(prefix + "-parallel-endpoints.csv"))
            {
                var columns = row.Split(',');
                if (columns[1] != "Pit") continue;
                double Parse(int i) => double.Parse(columns[i], CultureInfo.InvariantCulture);
                var tile = TileGeometry.FromPosition(Parse(5), Parse(6), Parse(7), 8);
                var line = FormattableString.Invariant($"{columns[0]},{columns[4]},{terrain[tile]:R},{drainage.Filled[tile]:R},{drainage.Filled[tile]-terrain[tile]:R},{accepted.Contains(tile)}");
                report.WriteLine(line); Console.WriteLine(line);
            }
            return 0;
        }
        if (args.Length > 2 && args[2] == "coarse")
        {
            var coarseTimer = Stopwatch.StartNew();
            var coarse = RiverPathTracing.BuildRiverNetworkFromSources(
                Seed, movedSeeds, precipitation.SeaLevel, sources,
                RiverPathTracing.DefaultFineDepth, context: DeepTimeContext.AtPlateTime(timeMyr));
            long coarsePoints = 0;
            int coarseMerges = 0;
            var coarseClaimed = new Dictionary<TileId, int>();
            for (int i = 0; i < coarse.Count; i++)
            {
                var river = coarse[i];
                coarsePoints += river.Path.Count;
                if (river.Termination == RiverPathTracing.TerminationReason.Merged)
                {
                    coarseMerges++;
                    if (river.Path.Count == 0 ||
                        !coarseClaimed.ContainsKey(river.Path[river.Path.Count - 1]))
                        throw new InvalidOperationException("Missing coarse merge owner at " + i);
                }
                foreach (TileId tile in river.Path)
                    if (!coarseClaimed.ContainsKey(tile)) coarseClaimed[tile] = i;
            }
            Console.WriteLine($"coarseMs={coarseTimer.Elapsed.TotalMilliseconds:F1} rivers={coarse.Count} points={coarsePoints} merges={coarseMerges}");
            return 0;
        }
        if (args.Length > 2 && args[2] == "independent")
        {
            using var tailRows = new StreamWriter(prefix + "-independent-tails.csv");
            tailRows.AutoFlush = true;
            tailRows.WriteLine("index,claimedSteps,independentSteps,extraSteps,independentMs,termination");
            foreach (string line in File.ReadAllLines(prefix + "-profile.csv"))
            {
                string[] fields = line.Split(',');
                if (fields[0] == "index" || fields[8] != "Merged") continue;
                int index = int.Parse(fields[0], CultureInfo.InvariantCulture);
                int claimedSteps = int.Parse(fields[6], CultureInfo.InvariantCulture);
                var timer = Stopwatch.StartNew();
                var unclaimed = RiverPathTracing.TraceRiverPathContinuous(
                    Seed, movedSeeds, precipitation.SeaLevel, sources[index], index,
                    RiverPathTracing.DefaultFineDepth,
                    claimed: null,
                    stepMeters: 50.0, context: DeepTimeContext.AtPlateTime(timeMyr));
                int extraSteps = unclaimed.ClaimCheckIndices.Count - claimedSteps;
                tailRows.WriteLine(string.Join(",", index, claimedSteps,
                    unclaimed.ClaimCheckIndices.Count, extraSteps,
                    timer.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    unclaimed.Termination));
                Console.WriteLine($"source={index} extraSteps={extraSteps} independentMs={timer.Elapsed.TotalMilliseconds:F1}");
            }
            return 0;
        }
        // ND-187 MERES (2026-10-03, felhasznaloi visszajelzes): mennyire
        // vagnak at a folyok a LATHATO tavakon, es hany ag indul/vegzodik
        // olyan ponton, ami a megjelenitett vizfelszin alatt van. A
        // TileGeometry hasznalata itt DIAGNOSZTIKA (ND-24 a SZIMULACIOS
        // kritikus utra szol), a kimenet nem megy vissza a modellbe.
        if (args.Length > 2 && args[2] == "lakecross")
        {
            // ND-187: a to-jeloles melyseg-kuszobe SOPORHETO, hogy mert
            // alapon valasszuk, ne becslesbol.
            double submergedMinDepth = args.Length > 5
                ? double.Parse(args[5], CultureInfo.InvariantCulture)
                : RiverPathTracing.DefaultSubmergedMinDepthMeters;
            // ND-188: az escape-szakasz PONTSURUSEGE is soporheto - ez donti
            // el, hogy az "attekintes" tenyleg olcsobb-e a veglegesnel.
            double escapeEmit = args.Length > 6
                ? double.Parse(args[6], CultureInfo.InvariantCulture)
                : RiverPathTracing.DefaultContinuousEscapeEmitMeters;
            var lakeTimer = Stopwatch.StartNew();
            var network = RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                Seed, movedSeeds, precipitation.SeaLevel, sources,
                RiverPathTracing.DefaultFineDepth, stepMeters: traceStep,
                maxDegreeOfParallelism: 4, context: DeepTimeContext.AtPlateTime(timeMyr),
                mergeRadiusMeters: mergeRadius, submergedMinDepthMeters: submergedMinDepth,
                escapeEmitMeters: escapeEmit);
            long networkPoints = 0;
            foreach (var r in network) networkPoints += r.Points.Count;
            using (var self = Process.GetCurrentProcess())
                Console.WriteLine(FormattableString.Invariant(
                    $"networkMs={lakeTimer.Elapsed.TotalMilliseconds:F1} rivers={network.Count} points={networkPoints} stepM={traceStep:F1} escapeEmitM={escapeEmit:F1} submergedMinDepthM={submergedMinDepth:F1} peakWorkingSetBytes={self.PeakWorkingSet64}"));

            // UGYANAZ a terep- es tolanc, amit az "endcheck" hasznal, hogy a
            // ket mereset ossze lehessen vetni.
            var terrain = SeaLevelCalibration.ComputeElevationFieldAtTime(
                Seed, 20, 8, DeepTimeContext.AtPlateTime(timeMyr));
            var ocean = FlowNetwork.ComputeOceanField(terrain, precipitation.SeaLevel);
            var drainage = FlowNetwork.PriorityFlood(terrain, ocean);
            var lakeResult = LakesIceErosion.IdentifyLakes(terrain, drainage.Filled, ocean);
            var lakeOfTile = new Dictionary<TileId, int>();
            int visibleLakes = 0;
            double visibleLakeTiles = 0;
            foreach (var lake in lakeResult.Lakes)
            {
                if (lake.TileCount < 6 || lake.MaxDepth < 40.0) continue;
                visibleLakes++;
                visibleLakeTiles += lake.TileCount;
                foreach (TileId t in lake.Tiles) lakeOfTile[t] = lake.Id;
            }
            Console.WriteLine(FormattableString.Invariant(
                $"visibleLakes={visibleLakes} lakeTiles={visibleLakeTiles:F0} (level 8)"));

            using var report = new StreamWriter(prefix + "-lakecross.csv");
            report.WriteLine("index,termination,lengthKm,lakeLengthKm,lakePercent,lakeSegments,crossings,longestCrossingKm,endsInLake,startsBelowSea,startsInLake,spanKm,spanPercent,spanCount,spanInLakeKm,lakeNotSpanKm");
            double totalLength = 0.0, totalLakeLength = 0.0, longestCrossingAll = 0.0;
            int riversWithCrossing = 0, riversEndingInLake = 0, riversStartingInLake = 0, riversStartingBelowSea = 0;
            int totalCrossings = 0;
            // ND-187: a Core SAJAT jelolese (SubmergedSpans) vs a LATHATO tavak.
            double totalSpanLength = 0.0, totalSpanInLake = 0.0, totalLakeNotSpan = 0.0;
            int totalSpanCount = 0;
            foreach (var river in network)
            {
                double length = 0.0, lakeLength = 0.0, longestCrossing = 0.0, currentSegment = 0.0;
                int segments = 0, crossings = 0;
                bool inLake = false;
                var first = river.Points[0];
                TileId firstTile = TileGeometry.FromPosition(first.X, first.Y, first.Z, 8);
                bool startsInLake = lakeOfTile.ContainsKey(firstTile);
                bool startsBelowSea = terrain[firstTile] < precipitation.SeaLevel;
                for (int i = 1; i < river.Points.Count; i++)
                {
                    var a = river.Points[i - 1]; var b = river.Points[i];
                    double dx = b.X-a.X, dy = b.Y-a.Y, dz = b.Z-a.Z;
                    double meters = Math.Sqrt(dx*dx+dy*dy+dz*dz) * WorldGen.Core.PlanetConstants.RadiusMeters;
                    length += meters;
                    TileId tile = TileGeometry.FromPosition(b.X, b.Y, b.Z, 8);
                    bool nowInLake = lakeOfTile.ContainsKey(tile);
                    if (nowInLake)
                    {
                        lakeLength += meters;
                        currentSegment += meters;
                        if (!inLake) { segments++; inLake = true; }
                    }
                    else if (inLake)
                    {
                        // KILEPETT a tobol, tehat ATVAGTA - nem ott ert veget.
                        crossings++;
                        if (currentSegment > longestCrossing) longestCrossing = currentSegment;
                        currentSegment = 0.0;
                        inLake = false;
                    }
                }
                // A Core jelolese: mely ELEK esnek egy SubmergedSpans tartomanyba.
                var submerged = new bool[river.Points.Count];
                foreach ((int Start, int End) span in river.SubmergedSpans)
                    for (int i = span.Start; i <= span.End && i < submerged.Length; i++) submerged[i] = true;
                double spanLength = 0.0, spanInLake = 0.0, lakeNotSpan = 0.0;
                for (int i = 1; i < river.Points.Count; i++)
                {
                    var a = river.Points[i - 1]; var b = river.Points[i];
                    double dx = b.X-a.X, dy = b.Y-a.Y, dz = b.Z-a.Z;
                    double meters = Math.Sqrt(dx*dx+dy*dy+dz*dz) * WorldGen.Core.PlanetConstants.RadiusMeters;
                    bool inSpan = submerged[i - 1] && submerged[i];
                    bool inLakeTile = lakeOfTile.ContainsKey(TileGeometry.FromPosition(b.X, b.Y, b.Z, 8));
                    if (inSpan) spanLength += meters;
                    if (inSpan && inLakeTile) spanInLake += meters;
                    if (!inSpan && inLakeTile) lakeNotSpan += meters;
                }
                totalSpanLength += spanLength; totalSpanInLake += spanInLake; totalLakeNotSpan += lakeNotSpan;
                totalSpanCount += river.SubmergedSpans.Count;
                var last = river.Points[river.Points.Count - 1];
                bool endsInLake = lakeOfTile.ContainsKey(TileGeometry.FromPosition(last.X, last.Y, last.Z, 8));
                totalLength += length; totalLakeLength += lakeLength;
                if (crossings > 0) riversWithCrossing++;
                if (endsInLake) riversEndingInLake++;
                if (startsInLake) riversStartingInLake++;
                if (startsBelowSea) riversStartingBelowSea++;
                totalCrossings += crossings;
                if (longestCrossing > longestCrossingAll) longestCrossingAll = longestCrossing;
                report.WriteLine(FormattableString.Invariant(
                    $"{river.SourceIndex},{river.Termination},{length/1000.0:R},{lakeLength/1000.0:R},{100.0*lakeLength/Math.Max(length,1.0):R},{segments},{crossings},{longestCrossing/1000.0:R},{endsInLake},{startsBelowSea},{startsInLake},{spanLength/1000.0:R},{100.0*spanLength/Math.Max(length,1.0):R},{river.SubmergedSpans.Count},{spanInLake/1000.0:R},{lakeNotSpan/1000.0:R}"));
            }
            Console.WriteLine(FormattableString.Invariant(
                $"lakecross: totalKm={totalLength/1000.0:F3} lakeKm={totalLakeLength/1000.0:F3} lakePercent={100.0*totalLakeLength/Math.Max(totalLength,1.0):F2} riversWithCrossing={riversWithCrossing}/{network.Count} crossings={totalCrossings} longestCrossingKm={longestCrossingAll/1000.0:F3} endsInLake={riversEndingInLake} startsInLake={riversStartingInLake} startsBelowSea={riversStartingBelowSea}"));
            Console.WriteLine(FormattableString.Invariant(
                $"spans: spanKm={totalSpanLength/1000.0:F3} spanPercent={100.0*totalSpanLength/Math.Max(totalLength,1.0):F2} spanCount={totalSpanCount} spanInLakeKm={totalSpanInLake/1000.0:F3} spanInLakePercent={100.0*totalSpanInLake/Math.Max(totalSpanLength,1.0):F2} lakeNotCoveredKm={totalLakeNotSpan/1000.0:F3} lakeCoveredPercent={100.0*(totalLakeLength-totalLakeNotSpan)/Math.Max(totalLakeLength,1.0):F2}"));
            return 0;
        }

        if (args.Length > 2 && (args[2] == "parallel" || args[2] == "parallel2" || args[2] == "parallel4"))
        {
            int workers = args[2] == "parallel2" ? 2 : args[2] == "parallel4" ? 4 : -1;
            using var process = Process.GetCurrentProcess();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            var parallelTimer = Stopwatch.StartNew();
            List<RiverPathTracing.ContinuousRiverPath> parallel =
                RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                    Seed, movedSeeds, precipitation.SeaLevel, sources,
                    RiverPathTracing.DefaultFineDepth, stepMeters: traceStep,
                    maxDegreeOfParallelism: workers, context: DeepTimeContext.AtPlateTime(timeMyr),
                    mergeRadiusMeters: mergeRadius,
                    onRiverCompleted: river => Console.WriteLine($"committed={river.SourceIndex + 1} elapsedMs={parallelTimer.Elapsed.TotalMilliseconds:F1} end={river.Termination} merge={river.MergedIntoRiverIndex}"));
            double elapsedMs = parallelTimer.Elapsed.TotalMilliseconds;
            double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            int[] parallelWeights = RiverPathTracing.ComputeDischargeWeights(parallel);
            string parallelFingerprint = Fingerprint(sources, parallel, parallelWeights);
            long pointCount = 0;
            foreach (var river in parallel) pointCount += river.Points.Count;
            long peakWorkingSet = process.PeakWorkingSet64;
            WriteGeometryReport(prefix, parallel);
            using (var endpoints = new StreamWriter(prefix + "-parallel-endpoints.csv"))
            {
                endpoints.WriteLine("index,termination,mergedInto,points,lengthKm,endX,endY,endZ");
                foreach (var river in parallel)
                {
                    double lengthM = 0.0;
                    for (int p = 1; p < river.Points.Count; p++)
                    {
                        var a = river.Points[p - 1]; var b = river.Points[p];
                        double dx = a.X-b.X, dy = a.Y-b.Y, dz = a.Z-b.Z;
                        lengthM += Math.Sqrt(dx*dx+dy*dy+dz*dz) * WorldGen.Core.PlanetConstants.RadiusMeters;
                    }
                    var end = river.Points[river.Points.Count - 1];
                    endpoints.WriteLine(FormattableString.Invariant($"{river.SourceIndex},{river.Termination},{river.MergedIntoRiverIndex},{river.Points.Count},{lengthM/1000.0:R},{end.X:R},{end.Y:R},{end.Z:R}"));
                }
            }
            File.WriteAllText(prefix + (workers < 0 ? "-parallel" : "-parallel" + workers) + "-fingerprint.txt",
                $"sha256={parallelFingerprint}{Environment.NewLine}" +
                $"rivers={parallel.Count}{Environment.NewLine}" +
                $"networkMs={elapsedMs:F1}{Environment.NewLine}" +
                $"cpuMs={cpuMs:F1}{Environment.NewLine}" +
                $"points={pointCount}{Environment.NewLine}" +
                $"peakWorkingSetBytes={peakWorkingSet}{Environment.NewLine}" +
                $"weights={string.Join(",", parallelWeights)}{Environment.NewLine}");
            Console.WriteLine($"parallelMs={elapsedMs:F1} cpuMs={cpuMs:F1} workers={workers} rivers={parallel.Count} points={pointCount} peakWorkingSetBytes={peakWorkingSet} sha256={parallelFingerprint}");
            return 0;
        }

        var claimed = new ClaimedRiverPoints(mergeRadius);
        var rivers = new List<RiverPathTracing.ContinuousRiverPath>();
        using var rows = new StreamWriter(prefix + "-profile.csv");
        rows.AutoFlush = true;
        rows.WriteLine("index,basinRound,tileIdHex,traceMs,claimMs,points,steps,pitEscapeCalls,termination,mergedInto,mergePointIndex,claimedCount");
        var networkTimer = Stopwatch.StartNew();
        int count = Math.Min(limit, sources.Count);
        for (int i = 0; i < count; i++)
        {
            int pitCalls = 0;
            var timer = Stopwatch.StartNew();
            var river = RiverPathTracing.TraceRiverPathContinuous(
                Seed, movedSeeds, precipitation.SeaLevel, sources[i], i,
                RiverPathTracing.DefaultFineDepth, claimed,
                stepMeters: 50.0, onPitEscape: () => pitCalls++, context: DeepTimeContext.AtPlateTime(timeMyr));
            double traceMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            foreach (var point in river.Points)
                claimed.Add(point, i);
            double claimMs = timer.Elapsed.TotalMilliseconds;
            rivers.Add(river);
            int mergePointIndex = river.Termination == RiverPathTracing.TerminationReason.Merged
                ? river.Points.Count - 2 : -1;
            rows.WriteLine(string.Join(",", i, i / 6, sources[i].Value.ToString("X16"),
                traceMs.ToString("F3", CultureInfo.InvariantCulture),
                claimMs.ToString("F3", CultureInfo.InvariantCulture), river.Points.Count,
                river.ClaimCheckIndices.Count, pitCalls, river.Termination,
                river.MergedIntoRiverIndex, mergePointIndex, claimed.Count));
            Console.WriteLine($"source={i} basin={i / 6} traceMs={traceMs:F1} claimMs={claimMs:F1} points={river.Points.Count} steps={river.ClaimCheckIndices.Count} pit={pitCalls} end={river.Termination} merge={river.MergedIntoRiverIndex} elapsedMs={networkTimer.Elapsed.TotalMilliseconds:F1}");
        }
        int[] weights = RiverPathTracing.ComputeDischargeWeights(rivers);
        if (count <= 6)
        {
            var canonical = RiverPathTracing.BuildContinuousRiverNetworkFromSources(
                Seed, movedSeeds, precipitation.SeaLevel, sources.GetRange(0, count),
                RiverPathTracing.DefaultFineDepth, stepMeters: 50.0, context: DeepTimeContext.AtPlateTime(timeMyr));
            for (int i = 0; i < count; i++)
            {
                if (rivers[i].Termination != canonical[i].Termination ||
                    rivers[i].MergedIntoRiverIndex != canonical[i].MergedIntoRiverIndex ||
                    rivers[i].Points.Count != canonical[i].Points.Count)
                    throw new InvalidOperationException("Canonical river metadata mismatch at " + i);
                for (int p = 0; p < rivers[i].Points.Count; p++)
                {
                    var a = rivers[i].Points[p];
                    var b = canonical[i].Points[p];
                    if (BitConverter.DoubleToInt64Bits(a.X) != BitConverter.DoubleToInt64Bits(b.X) ||
                        BitConverter.DoubleToInt64Bits(a.Y) != BitConverter.DoubleToInt64Bits(b.Y) ||
                        BitConverter.DoubleToInt64Bits(a.Z) != BitConverter.DoubleToInt64Bits(b.Z))
                        throw new InvalidOperationException("Canonical river coordinate mismatch at " + i + ":" + p);
                }
            }
            Console.WriteLine("canonicalMatch=True");
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(rivers.Count);
        for (int i = 0; i < rivers.Count; i++)
        {
            var river = rivers[i];
            writer.Write(sources[i].Value);
            writer.Write(river.SourceIndex);
            writer.Write((int)river.Termination);
            writer.Write(river.MergedIntoRiverIndex);
            writer.Write(weights[i]);
            writer.Write(river.Points.Count);
            foreach (var point in river.Points)
            {
                writer.Write(BitConverter.DoubleToInt64Bits(point.X));
                writer.Write(BitConverter.DoubleToInt64Bits(point.Y));
                writer.Write(BitConverter.DoubleToInt64Bits(point.Z));
            }
            if (buffer.Length > 1024 * 1024)
            {
                hash.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
                buffer.SetLength(0);
                buffer.Position = 0;
            }
        }
        hash.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
        string fingerprint = BitConverter.ToString(hash.GetHashAndReset()).Replace("-", "").ToLowerInvariant();
        File.WriteAllText(prefix + "-fingerprint.txt", $"sha256={fingerprint}{Environment.NewLine}rivers={rivers.Count}{Environment.NewLine}networkMs={networkTimer.Elapsed.TotalMilliseconds:F1}{Environment.NewLine}weights={string.Join(",", weights)}{Environment.NewLine}");
        Console.WriteLine($"networkMs={networkTimer.Elapsed.TotalMilliseconds:F1} sha256={fingerprint}");
        return 0;
    }

    private static string Fingerprint(
        IReadOnlyList<TileId> sources,
        IReadOnlyList<RiverPathTracing.ContinuousRiverPath> rivers,
        IReadOnlyList<int> weights)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(rivers.Count);
        for (int i = 0; i < rivers.Count; i++)
        {
            var river = rivers[i];
            writer.Write(sources[i].Value);
            writer.Write(river.SourceIndex);
            writer.Write((int)river.Termination);
            writer.Write(river.MergedIntoRiverIndex);
            writer.Write(weights[i]);
            writer.Write(river.Points.Count);
            foreach (var point in river.Points)
            {
                writer.Write(BitConverter.DoubleToInt64Bits(point.X));
                writer.Write(BitConverter.DoubleToInt64Bits(point.Y));
                writer.Write(BitConverter.DoubleToInt64Bits(point.Z));
            }
            if (buffer.Length > 1024 * 1024)
            {
                hash.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
                buffer.SetLength(0);
                buffer.Position = 0;
            }
        }
        hash.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
        return BitConverter.ToString(hash.GetHashAndReset()).Replace("-", "").ToLowerInvariant();
    }
}
