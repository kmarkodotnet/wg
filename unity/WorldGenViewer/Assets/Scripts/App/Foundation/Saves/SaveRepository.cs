#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WorldGen.App.Storage;

namespace WorldGen.App.Saves
{
    public enum SaveCompatibilityLevel
    {
        Compatible,
        Migratable,

        /// <summary>A generátor változott: a seed és a paraméterek újrahasznosíthatók, az állapot nem.</summary>
        ConfigurationOnly,

        Incompatible,
    }

    public enum IncompatibilityReason
    {
        None,
        NewerFormat,
        NoMigrationPath,
        GeneratorChanged,
        Corrupted,
        Unreadable,
    }

    public readonly struct SaveCompatibilityResult
    {
        public SaveCompatibilityResult(SaveCompatibilityLevel level, IncompatibilityReason reason)
        {
            Level = level;
            Reason = reason;
        }

        public SaveCompatibilityLevel Level { get; }
        public IncompatibilityReason Reason { get; }

        public bool CanLoadState => Level == SaveCompatibilityLevel.Compatible || Level == SaveCompatibilityLevel.Migratable;

        /// <summary>Lokalizációs kulcs; null, ha nincs mit kiírni.</summary>
        public string? MessageKey
        {
            get
            {
                switch (Reason)
                {
                    case IncompatibilityReason.NewerFormat: return "save.compat.newerFormat";
                    case IncompatibilityReason.NoMigrationPath: return "save.compat.noMigration";
                    case IncompatibilityReason.GeneratorChanged: return "save.compat.generatorChanged";
                    case IncompatibilityReason.Corrupted: return "save.compat.corrupted";
                    case IncompatibilityReason.Unreadable: return "save.error.accessDenied";
                    default: return null;
                }
            }
        }
    }

    /// <summary>A mentés kompatibilitási szabálya (WF-SAVE-005, architektúra-doksi §9).</summary>
    public static class SaveCompatibility
    {
        public static SaveCompatibilityResult Evaluate(SaveHeader header, int currentFormatVersion, string currentGeneratorVersion,
            Func<int, int, bool>? canMigrate = null)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (currentFormatVersion < 1) throw new ArgumentOutOfRangeException(nameof(currentFormatVersion));
            if (string.IsNullOrEmpty(currentGeneratorVersion))
                throw new ArgumentException("Az aktuális generátorverzió nem lehet üres (ld. ND-108).", nameof(currentGeneratorVersion));

            if (header.SaveFormatVersion > currentFormatVersion)
                return new SaveCompatibilityResult(SaveCompatibilityLevel.Incompatible, IncompatibilityReason.NewerFormat);
            if (header.SaveFormatVersion < currentFormatVersion && (canMigrate == null || !canMigrate(header.SaveFormatVersion, currentFormatVersion)))
                return new SaveCompatibilityResult(SaveCompatibilityLevel.Incompatible, IncompatibilityReason.NoMigrationPath);
            if (!string.Equals(header.WorldGeneratorVersion, currentGeneratorVersion, StringComparison.Ordinal))
                return new SaveCompatibilityResult(SaveCompatibilityLevel.ConfigurationOnly, IncompatibilityReason.GeneratorChanged);
            if (header.SaveFormatVersion < currentFormatVersion)
                return new SaveCompatibilityResult(SaveCompatibilityLevel.Migratable, IncompatibilityReason.None);
            return new SaveCompatibilityResult(SaveCompatibilityLevel.Compatible, IncompatibilityReason.None);
        }

        public static SaveCompatibilityResult Corrupted() => new SaveCompatibilityResult(SaveCompatibilityLevel.Incompatible, IncompatibilityReason.Corrupted);

        public static SaveCompatibilityResult Unreadable() => new SaveCompatibilityResult(SaveCompatibilityLevel.Incompatible, IncompatibilityReason.Unreadable);
    }

    public sealed class SaveSlotInfo
    {
        internal SaveSlotInfo(string filePath, long sizeBytes, DateTime fileLastWriteUtc, SaveHeader? header,
            SaveCorruptionReason? corruption, string? readError, SaveCompatibilityResult compatibility)
        {
            FilePath = filePath;
            SizeBytes = sizeBytes;
            FileLastWriteUtc = fileLastWriteUtc;
            Header = header;
            Corruption = corruption;
            ReadError = readError;
            Compatibility = compatibility;
        }

        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);
        public long SizeBytes { get; }
        public DateTime FileLastWriteUtc { get; }

        /// <summary>Null, ha a fejléc nem olvasható (sérült vagy hozzáférési hiba).</summary>
        public SaveHeader? Header { get; }

        public SaveCorruptionReason? Corruption { get; }
        public string? ReadError { get; }
        public SaveCompatibilityResult Compatibility { get; }

        public string DisplayName => Header != null && Header.WorldName.Length > 0
            ? Header.WorldName
            : Path.GetFileNameWithoutExtension(FilePath);

        /// <summary>Rendezési idő: a fejléc utolsó játékideje, ennek hiányában a fájl módosítási ideje.</summary>
        public DateTime SortTimeUtc => Header?.LastPlayedUtc ?? FileLastWriteUtc;
    }

    /// <summary>
    /// A Saves könyvtár kezelése (WF-SAVE-002/003/004): fejléc-only listázás,
    /// Continue-jelölt, atomi írás, törlés, autosave-rotáció. Minden írás a
    /// saves-könyvtáron belül marad.
    /// </summary>
    public sealed class SaveRepository
    {
        public const string Extension = ".wgsave";
        public const int MaxWorldNameStemLength = 48;

        private readonly IFileSystem _fileSystem;
        private readonly AtomicFileWriter _writer;
        private readonly Func<SaveHeader, SaveCompatibilityResult> _compatibility;

        public string Directory { get; }

        public SaveRepository(IFileSystem fileSystem, string savesDirectory, Func<SaveHeader, SaveCompatibilityResult> compatibility)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            if (string.IsNullOrEmpty(savesDirectory)) throw new ArgumentException("Üres saves-könyvtár.", nameof(savesDirectory));
            _compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
            Directory = savesDirectory;
            _writer = new AtomicFileWriter(fileSystem, keepBackup: true);
        }

        /// <summary>Az összes mentés, legutóbb játszott elöl. Sérült fájl is szerepel (jelölve), hogy törölhető legyen.</summary>
        public IReadOnlyList<SaveSlotInfo> List()
        {
            var result = new List<SaveSlotInfo>();
            foreach (string path in _fileSystem.GetFiles(Directory, Extension))
            {
                long size = 0;
                DateTime written = default;
                try
                {
                    size = _fileSystem.GetFileLength(path);
                    written = _fileSystem.GetLastWriteTimeUtc(path);
                    SaveHeader header;
                    using (var stream = _fileSystem.OpenRead(path)) header = SaveContainer.ReadHeader(stream);
                    result.Add(new SaveSlotInfo(path, size, written, header, null, null, _compatibility(header)));
                }
                catch (SaveCorruptedException ex)
                {
                    result.Add(new SaveSlotInfo(path, size, written, null, ex.Reason, ex.Message, SaveCompatibility.Corrupted()));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Add(new SaveSlotInfo(path, size, written, null, null, ex.Message, SaveCompatibility.Unreadable()));
                }
            }
            result.Sort((a, b) =>
            {
                int c = b.SortTimeUtc.CompareTo(a.SortTimeUtc);
                return c != 0 ? c : string.CompareOrdinal(a.FileName, b.FileName);
            });
            return result;
        }

        /// <summary>A Continue célja: a legutóbb játszott, állapotában betölthető mentés.</summary>
        public static SaveSlotInfo? FindContinueCandidate(IReadOnlyList<SaveSlotInfo> slots)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            SaveSlotInfo? best = null;
            foreach (var s in slots)
            {
                if (!s.Compatibility.CanLoadState) continue;
                if (best == null || s.SortTimeUtc > best.SortTimeUtc) best = s;
            }
            return best;
        }

        /// <summary>
        /// Új mentés útja. Manual: "Név.wgsave" (foglaltnál sorszámmal);
        /// Auto: "Név - Autosave yyyyMMdd-HHmmss.wgsave"; Quick: "Név - Quicksave.wgsave"
        /// (szándékosan fix, felülírandó).
        /// </summary>
        public string CreateFilePath(string worldName, SaveKind kind, DateTime utcNow)
        {
            string stem = FileNameSanitizer.Sanitize(worldName, "World", MaxWorldNameStemLength);
            switch (kind)
            {
                case SaveKind.Quick:
                    return Path.Combine(Directory, stem + " - Quicksave" + Extension);
                case SaveKind.Auto:
                    stem += " - Autosave " + utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    break;
            }
            string name = FileNameSanitizer.MakeUnique(stem, Extension, n => _fileSystem.FileExists(Path.Combine(Directory, n)));
            return Path.Combine(Directory, name);
        }

        public void Write(string filePath, SaveHeader header, IReadOnlyList<SaveSection> sections)
        {
            EnsureInside(filePath);
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (sections == null) throw new ArgumentNullException(nameof(sections));
            _fileSystem.CreateDirectory(Directory);
            _writer.Write(filePath, stream => SaveContainer.Write(stream, header, sections));
        }

        /// <summary>Teljes betöltés CRC-ellenőrzéssel; sérülésnél <see cref="SaveCorruptedException"/>.</summary>
        public SaveFile Load(string filePath)
        {
            EnsureInside(filePath);
            using var stream = _fileSystem.OpenRead(filePath);
            return SaveContainer.Read(stream);
        }

        public void Delete(string filePath)
        {
            EnsureInside(filePath);
            _fileSystem.DeleteFile(filePath);
            _fileSystem.DeleteFile(AtomicFileWriter.GetBackupPath(filePath));
        }

        /// <summary>Egy világ autosave-jei közül a legújabb <paramref name="keep"/> darabon kívüliek (WF-SAVE-004).</summary>
        public static IReadOnlyList<string> SelectAutosavesToDelete(IReadOnlyList<SaveSlotInfo> slots, string worldId, int keep)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            if (string.IsNullOrEmpty(worldId)) throw new ArgumentException("Üres világazonosító.", nameof(worldId));
            if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep));
            var autosaves = new List<SaveSlotInfo>();
            foreach (var s in slots)
            {
                if (s.Header != null && s.Header.Kind == SaveKind.Auto && string.Equals(s.Header.WorldId, worldId, StringComparison.Ordinal))
                    autosaves.Add(s);
            }
            autosaves.Sort((a, b) =>
            {
                int c = b.SortTimeUtc.CompareTo(a.SortTimeUtc);
                return c != 0 ? c : string.CompareOrdinal(b.FileName, a.FileName);
            });
            var result = new List<string>();
            for (int i = keep; i < autosaves.Count; i++) result.Add(autosaves[i].FilePath);
            return result;
        }

        private void EnsureInside(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("Üres mentési út.", nameof(filePath));
            string? parent = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (parent == null || !string.Equals(Path.GetFullPath(Directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("A mentés csak a saves-könyvtárba írható: " + filePath, nameof(filePath));
            if (!filePath.EndsWith(Extension, StringComparison.Ordinal))
                throw new ArgumentException("A mentés kiterjesztése " + Extension + " kell legyen: " + filePath, nameof(filePath));
        }
    }
}
