#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace WorldGen.App.WorldSetup
{
    public enum SeedFormat
    {
        /// <summary>Előjel nélküli decimális, 0 … 18446744073709551615.</summary>
        Decimal,

        /// <summary>Negatív decimális a <c>long</c> tartományban; bitre azonos <c>ulong</c>-ra képezve (a viewer <c>long worldSeed</c>-je miatt).</summary>
        SignedDecimal,

        /// <summary>"0x" + 1–16 hexa számjegy.</summary>
        Hexadecimal,

        /// <summary>Bármi más: FNV-1a 64 a levágott szöveg UTF-8 bájtjain (ND-107).</summary>
        Text,
    }

    public readonly struct ParsedSeed
    {
        public ParsedSeed(ulong value, SeedFormat format, string normalizedText)
        {
            Value = value;
            Format = format;
            NormalizedText = normalizedText;
        }

        public ulong Value { get; }
        public SeedFormat Format { get; }

        /// <summary>Számnál a kanonikus decimális alak; szövegnél a levágott szöveg.</summary>
        public string NormalizedText { get; }
    }

    /// <summary>
    /// A felhasználói seed-bemenet értelmezése (WF-UI-002). A szöveg → seed
    /// leképezés STABIL SZERZŐDÉS (ND-107): megosztott szöveges seedek miatt
    /// a módosítása verzióemelés. Konstansok (2026-09-13, Pythonban
    /// ellenőrizve): prím = 2^40 + 2^8 + 0xb3; offset basis = az FNV-0 hash a
    /// "chongo &lt;Landon Curt Noll&gt; /\../\" aláíráson.
    /// </summary>
    public static class SeedCodec
    {
        public const int MaxTextLength = 256;
        public const ulong FnvOffsetBasis = 0xcbf29ce484222325UL;
        public const ulong FnvPrime = 0x100000001b3UL;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static bool TryParse(string? input, out ParsedSeed seed)
        {
            seed = default;
            if (input == null) return false;
            string text = input.Trim();
            if (text.Length == 0 || text.Length > MaxTextLength) return false;

            if (IsAllDigits(text, 0)
                && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong dec))
            {
                seed = new ParsedSeed(dec, SeedFormat.Decimal, FormatDecimal(dec));
                return true;
            }

            if (text.Length > 1 && text[0] == '-' && IsAllDigits(text, 1)
                && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long signed) && signed < 0)
            {
                ulong value = FromSigned(signed);
                seed = new ParsedSeed(value, SeedFormat.SignedDecimal, FormatDecimal(value));
                return true;
            }

            if (text.Length > 2 && text.Length <= 18 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X')
                && ulong.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong hex)
                && IsAllHex(text, 2))
            {
                seed = new ParsedSeed(hex, SeedFormat.Hexadecimal, FormatDecimal(hex));
                return true;
            }

            seed = new ParsedSeed(HashText(text), SeedFormat.Text, text);
            return true;
        }

        public static string FormatDecimal(ulong seed) => seed.ToString(CultureInfo.InvariantCulture);

        /// <summary>"0x" + 16 nagybetűs hexa számjegy.</summary>
        public static string FormatHex(ulong seed) => "0x" + seed.ToString("X16", CultureInfo.InvariantCulture);

        /// <summary>FNV-1a 64 a szöveg UTF-8 bájtjain (normalizálás nélkül).</summary>
        public static ulong HashText(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            ulong hash = FnvOffsetBasis;
            foreach (byte b in Utf8.GetBytes(text))
            {
                hash ^= b;
                hash = unchecked(hash * FnvPrime);
            }
            return hash;
        }

        public static ulong FromSigned(long seed) => unchecked((ulong)seed);

        public static long ToSigned(ulong seed) => unchecked((long)seed);

        private static bool IsAllDigits(string s, int start)
        {
            if (start >= s.Length) return false;
            for (int i = start; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        private static bool IsAllHex(string s, int start)
        {
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }
    }
}
