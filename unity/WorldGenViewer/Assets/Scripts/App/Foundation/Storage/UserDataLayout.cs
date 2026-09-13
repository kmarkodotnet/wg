#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace WorldGen.App.Storage
{
    /// <summary>
    /// A felhasználói adatok egységes elhelyezése (WF-APP-004). A gyökér a
    /// Unity <c>Application.persistentDataPath</c>; semmi nem kerül a
    /// telepítési könyvtárba, így uninstall és update sem törli.
    /// </summary>
    public sealed class UserDataLayout
    {
        public const string SettingsFileName = "settings.json";

        public string Root { get; }
        public string Saves { get; }
        public string SettingsDirectory { get; }
        public string SettingsFile { get; }
        public string Screenshots { get; }
        public string Logs { get; }
        public string Cache { get; }
        public string Temp { get; }

        public UserDataLayout(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Üres adatgyökér.", nameof(root));
            if (!Path.IsPathRooted(root)) throw new ArgumentException("Az adatgyökérnek abszolút útnak kell lennie: " + root, nameof(root));
            Root = Path.GetFullPath(root);
            Saves = Path.Combine(Root, "Saves");
            SettingsDirectory = Path.Combine(Root, "Settings");
            SettingsFile = Path.Combine(SettingsDirectory, SettingsFileName);
            Screenshots = Path.Combine(Root, "Screenshots");
            Logs = Path.Combine(Root, "Logs");
            Cache = Path.Combine(Root, "Cache");
            Temp = Path.Combine(Root, "Temp");
        }

        public IReadOnlyList<string> AllDirectories => new[] { Saves, SettingsDirectory, Screenshots, Logs, Cache, Temp };

        public void EnsureCreated(IFileSystem fileSystem)
        {
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            foreach (string dir in AllDirectories) fileSystem.CreateDirectory(dir);
        }

        /// <summary>
        /// Igaz, ha <paramref name="candidate"/> a <paramref name="directory"/> maga
        /// vagy alatta van. A Bootstrap ezzel ellenőrizheti, hogy az adatgyökér
        /// nem a telepítési könyvtárban van.
        /// </summary>
        public static bool IsInside(string candidate, string directory)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(directory)) return false;
            string c = WithTrailingSeparator(Path.GetFullPath(candidate));
            string d = WithTrailingSeparator(Path.GetFullPath(directory));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return c.StartsWith(d, comparison);
        }

        private static string WithTrailingSeparator(string path)
        {
            char last = path[path.Length - 1];
            return last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
