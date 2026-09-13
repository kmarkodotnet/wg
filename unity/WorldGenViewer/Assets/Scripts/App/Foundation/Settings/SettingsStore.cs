#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WorldGen.App.Storage;

namespace WorldGen.App.Settings
{
    public sealed class SettingsLoadResult
    {
        public AppSettings Settings { get; }

        /// <summary>Nem volt settings-fájl: első indítás (WF-FIRST-001).</summary>
        public bool IsFirstRun { get; }

        /// <summary>A fájl hiányzott, de a <c>.bak</c>-ból helyreállt.</summary>
        public bool RecoveredFromBackup { get; }

        /// <summary>Sérült fájlnál az eredeti bájtok másolata; egyébként null.</summary>
        public string? CorruptFileCopyPath { get; }

        public bool IsFromNewerVersion { get; }

        public IReadOnlyList<SettingsIssue> Issues { get; }

        public SettingsLoadResult(AppSettings settings, bool isFirstRun, bool recoveredFromBackup, string? corruptFileCopyPath,
            bool isFromNewerVersion, IReadOnlyList<SettingsIssue> issues)
        {
            Settings = settings;
            IsFirstRun = isFirstRun;
            RecoveredFromBackup = recoveredFromBackup;
            CorruptFileCopyPath = corruptFileCopyPath;
            IsFromNewerVersion = isFromNewerVersion;
            Issues = issues;
        }
    }

    /// <summary>
    /// A beállítások perzisztens tárolója (WF-SET-001). Atomi írás; sérült
    /// fájl → alapértékek, az eredeti megőrizve; újabb verziójú fájl első
    /// felülírása előtt másolat készül róla. Kifelé mindig klónt ad.
    /// Csak a főszálról használható.
    /// </summary>
    public sealed class SettingsStore
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly IFileSystem _fileSystem;
        private readonly AtomicFileWriter _writer;
        private readonly IClock _clock;
        private AppSettings _current = new AppSettings();
        private byte[]? _newerVersionOriginal;
        private int _newerVersion;

        public string FilePath { get; }

        public SettingsMigrations Migrations { get; } = new SettingsMigrations();

        /// <summary>(új pillanatkép, megváltozott kategóriák) — a kategóriánkénti alkalmazók erre iratkoznak fel.</summary>
        public event Action<AppSettings, SettingsCategory>? Changed;

        public SettingsStore(IFileSystem fileSystem, string filePath, IClock clock)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("Üres settings-út.", nameof(filePath));
            FilePath = filePath;
            _writer = new AtomicFileWriter(fileSystem, keepBackup: true);
        }

        public AppSettings Snapshot => _current.Clone();

        public SettingsLoadResult Load()
        {
            bool recovered = false;
            try
            {
                recovered = _writer.TryRecover(FilePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (!_fileSystem.FileExists(FilePath))
            {
                _current = new AppSettings();
                return new SettingsLoadResult(_current.Clone(), true, recovered, null, false, Array.Empty<SettingsIssue>());
            }

            byte[] bytes;
            try
            {
                bytes = _fileSystem.ReadAllBytes(FilePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _current = new AppSettings();
                var unreadable = new[] { new SettingsIssue(SettingsIssueKind.Corrupt, "$", "A fájl nem olvasható: " + ex.Message) };
                return new SettingsLoadResult(_current.Clone(), false, recovered, null, false, unreadable);
            }

            SettingsReadResult read;
            try
            {
                read = SettingsSerializer.Deserialize(StrictUtf8.GetString(bytes), Migrations);
            }
            catch (DecoderFallbackException ex)
            {
                read = new SettingsReadResult(new AppSettings(),
                    new[] { new SettingsIssue(SettingsIssueKind.Corrupt, "$", "Érvénytelen UTF-8: " + ex.Message) }, 0, true, false);
            }

            string? corruptCopy = read.IsCorrupt ? PreserveCorruptFile(bytes) : null;
            if (read.IsFromNewerVersion)
            {
                _newerVersionOriginal = bytes;
                _newerVersion = read.FileVersion;
            }
            _current = read.Settings;
            return new SettingsLoadResult(_current.Clone(), false, recovered, corruptCopy, read.IsFromNewerVersion, read.Issues);
        }

        public void Save() => WriteFile(_current);

        /// <summary>
        /// Normalizálja és alkalmazza az új beállításokat. Ha nincs változás,
        /// nem ír és nem értesít. Íráshiba esetén a kivétel továbbmegy, és az
        /// aktuális állapot változatlan marad.
        /// </summary>
        public SettingsCategory Apply(AppSettings updated, bool save = true)
        {
            if (updated == null) throw new ArgumentNullException(nameof(updated));
            var next = updated.Clone();
            next.Normalize();
            var diff = _current.DiffCategories(next);
            if (diff == SettingsCategory.None) return diff;
            if (save) WriteFile(next);
            _current = next;
            Changed?.Invoke(_current.Clone(), diff);
            return diff;
        }

        public SettingsCategory RestoreDefaults(SettingsCategory categories, bool save = true)
        {
            var next = _current.Clone();
            next.RestoreDefaults(categories);
            return Apply(next, save);
        }

        private void WriteFile(AppSettings settings)
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) _fileSystem.CreateDirectory(directory);

            if (_newerVersionOriginal != null && directory != null)
            {
                string copy = Path.Combine(directory, "settings.v" + _newerVersion.ToString(CultureInfo.InvariantCulture) + ".json");
                if (!_fileSystem.FileExists(copy)) _writer.WriteAllBytes(copy, _newerVersionOriginal);
                _newerVersionOriginal = null;
            }

            _writer.WriteAllBytes(FilePath, Utf8NoBom.GetBytes(SettingsSerializer.Serialize(settings)));
        }

        private string? PreserveCorruptFile(byte[] bytes)
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(directory)) return null;
            string stem = "settings.corrupt-" + _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string name = FileNameSanitizer.MakeUnique(stem, ".json", n => _fileSystem.FileExists(Path.Combine(directory, n)));
            string path = Path.Combine(directory, name);
            try
            {
                _writer.WriteAllBytes(path, bytes);
                return path;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
