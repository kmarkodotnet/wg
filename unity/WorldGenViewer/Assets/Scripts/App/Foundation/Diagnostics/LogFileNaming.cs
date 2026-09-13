#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace WorldGen.App.Diagnostics
{
    /// <summary>
    /// "worldgen-yyyyMMdd-HHmmss[-N].log" fájlnevek és a megőrzési szabály.
    /// Az időbélyeg a hívó által adott időpont (a Unity-kötés helyi időt ad,
    /// mert a felhasználó ezt keresi a mappában).
    /// </summary>
    public static class LogFileNaming
    {
        public const string Prefix = "worldgen-";
        public const string Extension = ".log";
        private const string StampFormat = "yyyyMMdd-HHmmss";

        public static string CreateFileName(DateTime timestamp)
            => Prefix + timestamp.ToString(StampFormat, CultureInfo.InvariantCulture) + Extension;

        /// <summary>Ha az alapnév már létezik (két indítás ugyanabban a másodpercben), "-2", "-3" … utótag.</summary>
        public static string CreateUniqueFileName(DateTime timestamp, Func<string, bool> exists)
        {
            if (exists == null) throw new ArgumentNullException(nameof(exists));
            string stem = Prefix + timestamp.ToString(StampFormat, CultureInfo.InvariantCulture);
            string name = stem + Extension;
            for (int n = 2; exists(name); n++)
                name = stem + "-" + n.ToString(CultureInfo.InvariantCulture) + Extension;
            return name;
        }

        public static bool TryParse(string fileName, out DateTime timestamp, out int sequence)
        {
            timestamp = default;
            sequence = 1;
            if (fileName == null) return false;
            if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) || !fileName.EndsWith(Extension, StringComparison.Ordinal))
                return false;
            string body = fileName.Substring(Prefix.Length, fileName.Length - Prefix.Length - Extension.Length);
            if (body.Length < StampFormat.Length) return false;
            string stamp = body.Substring(0, StampFormat.Length);
            if (!DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
                return false;
            string rest = body.Substring(StampFormat.Length);
            if (rest.Length == 0) return true;
            if (rest[0] != '-' || rest.Length == 1) return false;
            return int.TryParse(rest.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence >= 2;
        }

        /// <summary>
        /// A törlendő naplófájlnevek: a mintára illeszkedők közül mind, ami a
        /// legújabb <paramref name="keepNewest"/> darabon kívül esik.
        /// A mintára nem illeszkedő fájlokhoz nem nyúl.
        /// </summary>
        public static IReadOnlyList<string> SelectFilesToDelete(IEnumerable<string> fileNames, int keepNewest)
        {
            if (fileNames == null) throw new ArgumentNullException(nameof(fileNames));
            if (keepNewest < 0) throw new ArgumentOutOfRangeException(nameof(keepNewest));
            var parsed = new List<KeyValuePair<string, KeyValuePair<DateTime, int>>>();
            foreach (string name in fileNames)
            {
                if (TryParse(name, out var stamp, out int seq))
                    parsed.Add(new KeyValuePair<string, KeyValuePair<DateTime, int>>(name, new KeyValuePair<DateTime, int>(stamp, seq)));
            }
            // Legújabb elöl; azonos időbélyegnél a nagyobb sorszám az újabb.
            parsed.Sort((a, b) =>
            {
                int c = b.Value.Key.CompareTo(a.Value.Key);
                return c != 0 ? c : b.Value.Value.CompareTo(a.Value.Value);
            });
            var result = new List<string>();
            for (int i = keepNewest; i < parsed.Count; i++) result.Add(parsed[i].Key);
            return result;
        }
    }
}
