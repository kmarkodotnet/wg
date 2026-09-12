using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldGen.Core.Persistence;
using WorldGen.Core.Tectonics;

namespace WorldGen.Cli
{
    /// <summary>
    /// M12 checkpoint / `.worldpkg` (docs/04-decisions.md ND-30 hatókör-
    /// szűkítése): a fájl a VILÁG-DEFINÍCIÓT (bemeneti paramétereket) menti,
    /// NEM a kiszámolt állapotot - ND-30 indoklása szerint a Core minden
    /// része tiszta függvénye a (worldSeed, paraméterek, idő) hármasnak, tehát
    /// a state a definícióból BÁRMIKOR újraszámolható. A fájlban tárolt
    /// `StateHashHex` a MENTÉSKOR kiszámolt <see cref="WorldStateHash"/> -
    /// a betöltés utáni <see cref="VerifyByRecomputation"/> pontosan azt
    /// ellenőrzi, amit ND-30 "amire TÉNYLEGESEN szükség van" névvel jelöl:
    /// hogy ugyanaz a definíció ugyanazt a világot adja-e vissza (I1).
    /// </summary>
    public sealed class WorldPackage
    {
        /// <summary>
        /// Seed-törő formátumváltozás esetén emelendő (CLAUDE.md
        /// "Verziózás és seed-kompatibilitás") - a betöltés explicit hibát
        /// dob ismeretlen verzióra, nem próbál csendben értelmezni egy
        /// jövőbeli, inkompatibilis formátumot.
        /// </summary>
        public const int CurrentFormatVersion = 2;

        public int FormatVersion { get; set; } = CurrentFormatVersion;

        public string WorldSeedHex { get; set; } = "";
        public int PlateCount { get; set; }
        public int Level { get; set; }
        public double DeepTimeMyr { get; set; }

        /// <summary>A mentéskor kiszámolt elevációmező-hash (SHA-256, hex) - ld. osztály-doksi.</summary>
        public string StateHashHex { get; set; } = "";

        [JsonIgnore]
        public ulong WorldSeed => Convert.ToUInt64(WorldSeedHex, 16);

        public static WorldPackage Create(ulong worldSeed, int plateCount, int level, double deepTimeMyr)
        {
            var field = SeaLevelCalibration.ComputeElevationFieldAtTime(worldSeed, plateCount, level, deepTimeMyr);
            byte[] hash = WorldStateHash.ComputeFieldHash(field);
            return new WorldPackage
            {
                WorldSeedHex = worldSeed.ToString("X16"),
                PlateCount = plateCount,
                Level = level,
                DeepTimeMyr = deepTimeMyr,
                StateHashHex = WorldStateHash.ToHexString(hash),
            };
        }

        private static readonly JsonSerializerOptions SerializeOptions = new JsonSerializerOptions { WriteIndented = true };

        public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, SerializeOptions));

        public static WorldPackage Load(string path)
        {
            string json = File.ReadAllText(path);
            WorldPackage? pkg = JsonSerializer.Deserialize<WorldPackage>(json);
            if (pkg == null)
                throw new InvalidDataException($"Érvénytelen .worldpkg fájl (üres/hibás JSON): {path}");
            if (pkg.FormatVersion != CurrentFormatVersion)
                throw new NotSupportedException(
                    $"Nem támogatott .worldpkg formátumverzió: {pkg.FormatVersion} " +
                    $"(ez az eszköz csak {CurrentFormatVersion}-t ismer; az 1-es világok " +
                    "az ND-90 kéregátmenet miatt numerikusan inkompatibilisek).");
            return pkg;
        }

        /// <summary>
        /// Újraszámolja az elevációmezőt a MENTETT paraméterekből, és
        /// összeveti a mentett hash-sel - ez maga a "checkpoint betöltés"
        /// (ND-30: a definíciót mentjük, nem az állapotot).
        /// </summary>
        public bool VerifyByRecomputation(out string recomputedHashHex)
        {
            var field = SeaLevelCalibration.ComputeElevationFieldAtTime(WorldSeed, PlateCount, Level, DeepTimeMyr);
            byte[] hash = WorldStateHash.ComputeFieldHash(field);
            recomputedHashHex = WorldStateHash.ToHexString(hash);
            return string.Equals(recomputedHashHex, StateHashHex, StringComparison.OrdinalIgnoreCase);
        }
    }
}
