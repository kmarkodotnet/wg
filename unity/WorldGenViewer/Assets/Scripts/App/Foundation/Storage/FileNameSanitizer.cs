#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace WorldGen.App.Storage
{
    /// <summary>
    /// Felhasználói név (világnév) → biztonságos fájlnév-törzs. A tiltott
    /// karakterkészlet explicit a Windows-szabály szerint, NEM a futtató
    /// platform <c>Path.GetInvalidFileNameChars</c>-ából, hogy a mentésnév
    /// minden platformon ugyanaz legyen.
    /// </summary>
    public static class FileNameSanitizer
    {
        public const int DefaultMaxLength = 64;

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        public static string Sanitize(string? name, string fallback = "World", int maxLength = DefaultMaxLength)
        {
            if (string.IsNullOrWhiteSpace(fallback)) throw new ArgumentException("Üres fallback név.", nameof(fallback));
            if (maxLength < 8) throw new ArgumentOutOfRangeException(nameof(maxLength));

            var sb = new StringBuilder();
            bool lastWasSpace = false;
            foreach (char ch in name ?? "")
            {
                char c = IsInvalid(ch) ? '_' : ch;
                if (char.IsWhiteSpace(c))
                {
                    if (lastWasSpace) continue;
                    c = ' ';
                    lastWasSpace = true;
                }
                else
                {
                    lastWasSpace = false;
                }
                sb.Append(c);
            }

            string result = TrimEdges(sb.ToString());
            if (result.Length > maxLength)
            {
                int cut = maxLength;
                if (char.IsHighSurrogate(result[cut - 1])) cut--;
                result = TrimEdges(result.Substring(0, cut));
            }
            if (result.Length == 0) return fallback;
            if (IsReserved(result)) result = "_" + result;
            return result;
        }

        /// <summary>"Név.ext", foglaltság esetén "Név (2).ext", "Név (3).ext" …</summary>
        public static string MakeUnique(string stem, string extension, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(stem)) throw new ArgumentException("Üres fájlnév-törzs.", nameof(stem));
            if (exists == null) throw new ArgumentNullException(nameof(exists));
            extension ??= "";
            string candidate = stem + extension;
            for (int n = 2; exists(candidate); n++)
                candidate = stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + extension;
            return candidate;
        }

        private static bool IsInvalid(char ch)
        {
            if (ch < 32) return true;
            switch (ch)
            {
                case '<':
                case '>':
                case ':':
                case '"':
                case '/':
                case '\\':
                case '|':
                case '?':
                case '*':
                    return true;
                default:
                    return false;
            }
        }

        private static string TrimEdges(string s) => s.Trim(' ').TrimEnd('.', ' ');

        private static bool IsReserved(string name)
        {
            int dot = name.IndexOf('.');
            string stem = (dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ');
            foreach (string reserved in ReservedNames)
                if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
