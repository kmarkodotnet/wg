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
        /// A fájl szerkezetének verziója. ND-108 óta a numerikus világmodell
        /// külön generátorverziót kap; seed-töréskor azt kell emelni.
        /// A v3 megakadályozza, hogy a régi olvasó átugorja ezt a kaput.
        /// </summary>
        public const int CurrentFormatVersion = 3;

        // Olvasáskor a hiányzó mező NEM jelent aktuális verziót.
        public int FormatVersion { get; set; }
        public string WorldGeneratorVersion { get; set; } = "";

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
                FormatVersion = CurrentFormatVersion,
                WorldGeneratorVersion = Core.Persistence.WorldGeneratorVersion.Current,
                WorldSeedHex = worldSeed.ToString("X16"),
                PlateCount = plateCount,
                Level = level,
                DeepTimeMyr = deepTimeMyr,
                StateHashHex = WorldStateHash.ToHexString(hash),
            };
        }

        private static readonly JsonSerializerOptions SerializeOptions = new JsonSerializerOptions { WriteIndented = true };

        public void Save(string path)
        {
            EnsureCompatible();
            File.WriteAllText(path, JsonSerializer.Serialize(this, SerializeOptions));
        }

        public static WorldPackage Load(string path)
        {
            string json = File.ReadAllText(path);
            WorldPackage? pkg = JsonSerializer.Deserialize<WorldPackage>(json);
            if (pkg == null)
                throw new InvalidDataException($"Érvénytelen .worldpkg fájl (üres/hibás JSON): {path}");
            pkg.EnsureCompatible();
            return pkg;
        }

        private void EnsureCompatible()
        {
            if (FormatVersion != CurrentFormatVersion)
                throw new NotSupportedException(
                    $"Nem támogatott .worldpkg formátumverzió: {FormatVersion} " +
                    $"(támogatott: {CurrentFormatVersion}). " +
                    (FormatVersion == 1 ? "Az 1-es világok az ND-90 kéregátmenet miatt numerikusan inkompatibilisek." :
                     FormatVersion == 2 ? "A 2-es csomag nem tartalmaz hiteles generátorverziót (ND-108); automatikus migráció nincs." :
                     "Hiányzó vagy ismeretlen formátumverzió."));
            string current = Core.Persistence.WorldGeneratorVersion.Current;
            if (!string.Equals(WorldGeneratorVersion, current, StringComparison.Ordinal))
                throw new NotSupportedException(
                    $"Nem kompatibilis generátorverzió: mentett='{WorldGeneratorVersion ?? ""}', aktuális='{current}' (ND-108). " +
                    "Hiányzó vagy eltérő azonosítóval a világ nem számolható újra azonosként; automatikus migráció nincs.");
        }

        /// <summary>
        /// Újraszámolja az elevációmezőt a MENTETT paraméterekből, és
        /// összeveti a mentett hash-sel - ez maga a "checkpoint betöltés"
        /// (ND-30: a definíciót mentjük, nem az állapotot).
        /// </summary>
        public bool VerifyByRecomputation(out string recomputedHashHex)
        {
            EnsureCompatible();
            var field = SeaLevelCalibration.ComputeElevationFieldAtTime(WorldSeed, PlateCount, Level, DeepTimeMyr);
            byte[] hash = WorldStateHash.ComputeFieldHash(field);
            recomputedHashHex = WorldStateHash.ToHexString(hash);
            return string.Equals(recomputedHashHex, StateHashHex, StringComparison.OrdinalIgnoreCase);
        }
    }
}
