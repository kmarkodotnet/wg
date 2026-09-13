#nullable enable
using System;
using System.Globalization;
using System.IO;

namespace WorldGen.App.Screenshots
{
    public sealed class ScreenshotRequest
    {
        public const int MaxSuperSize = 4;

        public ScreenshotRequest(bool includeUi, int superSize = 1)
        {
            if (superSize < 1 || superSize > MaxSuperSize) throw new ArgumentOutOfRangeException(nameof(superSize));
            IncludeUi = includeUi;
            SuperSize = superSize;
        }

        /// <summary>Hamis: „Capture without UI” (WF-SHOT-002).</summary>
        public bool IncludeUi { get; }

        /// <summary>Felbontás-szorzó a nagy felbontású képhez (Unity <c>ScreenCapture</c> superSize).</summary>
        public int SuperSize { get; }
    }

    /// <summary>"worldgen-yyyyMMdd-HHmmss[-clean][_N].png" a Screenshots mappában (WF-SHOT-001).</summary>
    public static class ScreenshotNaming
    {
        public const string Prefix = "worldgen-";
        public const string CleanSuffix = "-clean";
        public const string Extension = ".png";

        public static string CreateFileName(DateTime timestamp, bool clean)
            => Prefix + timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + (clean ? CleanSuffix : "") + Extension;

        public static string CreateUniquePath(string directory, DateTime timestamp, bool clean, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Üres könyvtár.", nameof(directory));
            if (exists == null) throw new ArgumentNullException(nameof(exists));
            string name = CreateFileName(timestamp, clean);
            string stem = name.Substring(0, name.Length - Extension.Length);
            string path = Path.Combine(directory, name);
            for (int n = 2; exists(path); n++)
                path = Path.Combine(directory, stem + "_" + n.ToString(CultureInfo.InvariantCulture) + Extension);
            return path;
        }
    }
}
