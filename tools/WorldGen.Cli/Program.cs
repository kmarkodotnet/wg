using System;
using System.Collections.Generic;
using WorldGen.Core.Persistence;
using WorldGen.Core.Tectonics;

namespace WorldGen.Cli
{
    /// <summary>
    /// M12 "worldgen verify" CLI (docs/04-decisions.md ND-30 hatókör-
    /// szűkítése): a hash-függvény ÉS a checkpoint (.worldpkg) köré épített
    /// vékony parancssori réteg. Nincs benne szimulációs logika - minden
    /// tényleges számítás a már verifikált Core-hívásokból jön
    /// (SeaLevelCalibration, WorldStateHash), ill. a <see cref="WorldPackage"/>
    /// checkpoint-formátumból.
    ///
    /// Parancsok:
    ///   worldgen hash --seed &lt;hex&gt; --plates &lt;n&gt; --level &lt;n&gt; [--time &lt;myr&gt;]
    ///   worldgen verify --seed &lt;hex&gt; --plates &lt;n&gt; --level &lt;n&gt; [--time &lt;myr&gt;] --expect &lt;hex&gt;
    ///   worldgen checkpoint save --seed &lt;hex&gt; --plates &lt;n&gt; --level &lt;n&gt; [--time &lt;myr&gt;] --out &lt;path&gt;
    ///   worldgen checkpoint verify --in &lt;path&gt;
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || args[0] is "-h" or "--help")
                {
                    PrintUsage();
                    return args.Length == 0 ? 1 : 0;
                }

                switch (args[0])
                {
                    case "hash":
                        return RunHash(ParseOptions(args, 1));
                    case "verify":
                        return RunVerify(ParseOptions(args, 1));
                    case "checkpoint":
                        return RunCheckpoint(args);
                    case "calibrate-ordinals":
                        return RunCalibrateOrdinals(ParseOptions(args, 1));
                    default:
                        Console.Error.WriteLine($"Ismeretlen parancs: {args[0]}");
                        PrintUsage();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Hiba: {ex.Message}");
                return 1;
            }
        }

        private static int RunCheckpoint(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("A 'checkpoint' parancshoz 'save' vagy 'verify' alparancs kell.");
                return 1;
            }

            Dictionary<string, string> opts = ParseOptions(args, 2);
            switch (args[1])
            {
                case "save":
                {
                    ulong seed = ParseSeed(RequireOption(opts, "seed"));
                    int plates = int.Parse(RequireOption(opts, "plates"));
                    int level = int.Parse(RequireOption(opts, "level"));
                    double time = opts.TryGetValue("time", out string? t) ? double.Parse(t) : 0.0;
                    string outPath = RequireOption(opts, "out");

                    WorldPackage pkg = WorldPackage.Create(seed, plates, level, time);
                    pkg.Save(outPath);
                    Console.WriteLine($"Mentve: {outPath} (hash={pkg.StateHashHex})");
                    return 0;
                }
                case "verify":
                {
                    string inPath = RequireOption(opts, "in");
                    WorldPackage pkg = WorldPackage.Load(inPath);
                    bool ok = pkg.VerifyByRecomputation(out string recomputed);
                    if (ok)
                    {
                        Console.WriteLine($"OK - a(z) {inPath} checkpoint újraszámolva ugyanazt a hash-t adja: {recomputed}");
                        return 0;
                    }
                    Console.WriteLine($"MISMATCH - mentett hash: {pkg.StateHashHex}, újraszámolt: {recomputed}");
                    return 1;
                }
                default:
                    Console.Error.WriteLine($"Ismeretlen 'checkpoint' alparancs: {args[1]}");
                    return 1;
            }
        }

        private static int RunHash(Dictionary<string, string> opts)
        {
            ulong seed = ParseSeed(RequireOption(opts, "seed"));
            int plates = int.Parse(RequireOption(opts, "plates"));
            int level = int.Parse(RequireOption(opts, "level"));
            double time = opts.TryGetValue("time", out string? t) ? double.Parse(t) : 0.0;

            string hashHex = ComputeHashHex(seed, plates, level, time);
            Console.WriteLine(hashHex);
            return 0;
        }

        private static int RunVerify(Dictionary<string, string> opts)
        {
            ulong seed = ParseSeed(RequireOption(opts, "seed"));
            int plates = int.Parse(RequireOption(opts, "plates"));
            int level = int.Parse(RequireOption(opts, "level"));
            double time = opts.TryGetValue("time", out string? t) ? double.Parse(t) : 0.0;
            string expected = RequireOption(opts, "expect");

            string actual = ComputeHashHex(seed, plates, level, time);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"OK - {actual}");
                return 0;
            }
            Console.WriteLine($"MISMATCH - várt: {expected}, kapott: {actual}");
            return 1;
        }

        private static int RunCalibrateOrdinals(Dictionary<string, string> opts)
        {
            int count = opts.TryGetValue("count", out string? c) ? int.Parse(c) : 500;
            int plates = opts.TryGetValue("plates", out string? p) ? int.Parse(p) : 20;
            int level = opts.TryGetValue("level", out string? l) ? int.Parse(l) : 6;
            double water = opts.TryGetValue("water", out string? w) ? double.Parse(w) : 0.65;
            // ND-117: alapból KI, mert a regolit-lánc világonként ~3,2 s
            // level=6-on (mérve), szemben a másik két metrika töredék-
            // másodpercével. A közös ParseOptions MINDEN opcióhoz értéket vár,
            // ezért `--soil true` az alak - és az értéket tényleg kiolvassuk,
            // nehogy a `--soil false` is bekapcsolja.
            bool soil = opts.TryGetValue("soil", out string? sv)
                && bool.TryParse(sv, out bool soilValue) && soilValue;

            Console.WriteLine($"Kalibráció: {count} világ, plates={plates}, level={level}, targetWaterFraction={water}, soil={soil}...");
            OrdinalCalibration.Result result = OrdinalCalibration.Run(count, plates, level, water, soil);

            double[] habThresholds = OrdinalCalibration.QuintileThresholds(result.HabitabilitySamples);
            double[] coastThresholds = OrdinalCalibration.QuintileThresholds(result.CoastalComplexitySamples);

            Console.WriteLine($"\nHabitability (N={result.HabitabilitySamples.Length} világ):");
            Console.WriteLine($"  p20={habThresholds[0]:F6} p40={habThresholds[1]:F6} p60={habThresholds[2]:F6} p80={habThresholds[3]:F6}");
            Console.WriteLine($"\nCoastalComplexity (N={result.CoastalComplexitySamples.Length} kontinens-minta):");
            Console.WriteLine($"  p20={coastThresholds[0]:F6} p40={coastThresholds[1]:F6} p60={coastThresholds[2]:F6} p80={coastThresholds[3]:F6}");

            if (result.SoilFertilitySamples.Length > 0)
            {
                double[] soilThresholds = OrdinalCalibration.QuintileThresholds(result.SoilFertilitySamples);
                Console.WriteLine();
                Console.WriteLine($"SoilFertility (N={result.SoilFertilitySamples.Length} régió-minta):");
                Console.WriteLine($"  p20={soilThresholds[0]:F6} p40={soilThresholds[1]:F6} p60={soilThresholds[2]:F6} p80={soilThresholds[3]:F6}");
            }
            return 0;
        }

        private static string ComputeHashHex(ulong seed, int plates, int level, double time)
        {
            var field = SeaLevelCalibration.ComputeElevationFieldAtTime(seed, plates, level, time);
            byte[] hash = WorldStateHash.ComputeFieldHash(field);
            return WorldStateHash.ToHexString(hash);
        }

        private static ulong ParseSeed(string raw)
        {
            string s = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw.Substring(2) : raw;
            return Convert.ToUInt64(s, 16);
        }

        private static string RequireOption(Dictionary<string, string> opts, string name)
        {
            if (!opts.TryGetValue(name, out string? value))
                throw new ArgumentException($"Hiányzó kötelező opció: --{name}");
            return value;
        }

        private static Dictionary<string, string> ParseOptions(string[] args, int startIndex)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = startIndex; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Váratlan argumentum (opciónak --val kell kezdődnie): {args[i]}");
                string key = args[i].Substring(2);
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Az opciónak ('--{key}') érték is kell.");
                result[key] = args[i + 1];
                i++;
            }
            return result;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine(
                "worldgen - determinisztikus világ hash/checkpoint eszköz (M12, ND-30)\n\n" +
                "  worldgen hash --seed <hex> --plates <n> --level <n> [--time <myr>]\n" +
                "  worldgen verify --seed <hex> --plates <n> --level <n> [--time <myr>] --expect <hex>\n" +
                "  worldgen checkpoint save --seed <hex> --plates <n> --level <n> [--time <myr>] --out <path>\n" +
                "  worldgen checkpoint verify --in <path>\n" +
                "  worldgen calibrate-ordinals [--count <n>] [--plates <n>] [--level <n>] [--water <0..1>]\n");
        }
    }
}
