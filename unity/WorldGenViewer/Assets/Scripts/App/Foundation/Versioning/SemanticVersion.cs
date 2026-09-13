#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace WorldGen.App.Versioning
{
    /// <summary>
    /// SemVer 2.0.0 verzió (MAJOR.MINOR.PATCH[-prerelease][+build]).
    /// Az egyenlőség és a sorrend a build-metaadatot a szabvány szerint
    /// figyelmen kívül hagyja; a <see cref="ToString"/> viszont kiírja.
    /// </summary>
    public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        /// <summary>Üres, ha kiadási (nem előzetes) verzió.</summary>
        public string PreRelease { get; }

        /// <summary>Üres, ha nincs build-metaadat.</summary>
        public string BuildMetadata { get; }

        public bool IsPreRelease => PreRelease.Length > 0;

        public SemanticVersion(int major, int minor, int patch, string preRelease = "", string buildMetadata = "")
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));
            preRelease ??= "";
            buildMetadata ??= "";
            if (preRelease.Length > 0 && !AreValidIdentifiers(preRelease, rejectNumericLeadingZero: true))
                throw new ArgumentException("Érvénytelen pre-release azonosító: " + preRelease, nameof(preRelease));
            if (buildMetadata.Length > 0 && !AreValidIdentifiers(buildMetadata, rejectNumericLeadingZero: false))
                throw new ArgumentException("Érvénytelen build-metaadat: " + buildMetadata, nameof(buildMetadata));
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease;
            BuildMetadata = buildMetadata;
        }

        public static SemanticVersion Parse(string text)
        {
            if (TryParse(text, out var version)) return version;
            throw new FormatException("Nem szemantikus verzió: '" + text + "'");
        }

        public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
        {
            version = null;
            if (string.IsNullOrEmpty(text)) return false;
            string s = text!;

            string build = "";
            int plus = s.IndexOf('+');
            if (plus >= 0)
            {
                build = s.Substring(plus + 1);
                s = s.Substring(0, plus);
                if (build.Length == 0 || !AreValidIdentifiers(build, rejectNumericLeadingZero: false)) return false;
            }

            string pre = "";
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                pre = s.Substring(dash + 1);
                s = s.Substring(0, dash);
                if (pre.Length == 0 || !AreValidIdentifiers(pre, rejectNumericLeadingZero: true)) return false;
            }

            string[] core = s.Split('.');
            if (core.Length != 3) return false;
            if (!TryParseCoreNumber(core[0], out int major)
                || !TryParseCoreNumber(core[1], out int minor)
                || !TryParseCoreNumber(core[2], out int patch))
                return false;

            version = new SemanticVersion(major, minor, patch, pre, build);
            return true;
        }

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null) return 1;
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;

            // Kiadási verzió nagyobb, mint bármely előzetes ugyanazon a számhármason.
            if (!IsPreRelease) return other.IsPreRelease ? 1 : 0;
            if (!other.IsPreRelease) return -1;

            string[] a = PreRelease.Split('.');
            string[] b = other.PreRelease.Split('.');
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                c = CompareIdentifier(a[i], b[i]);
                if (c != 0) return c;
            }
            return a.Length.CompareTo(b.Length);
        }

        public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is SemanticVersion v && Equals(v);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Major;
                h = h * 397 ^ Minor;
                h = h * 397 ^ Patch;
                h = h * 397 ^ StringComparer.Ordinal.GetHashCode(PreRelease);
                return h;
            }
        }

        /// <summary>Teljes alak, build-metaadattal együtt.</summary>
        public override string ToString()
        {
            string s = Major.ToString(CultureInfo.InvariantCulture) + "."
                + Minor.ToString(CultureInfo.InvariantCulture) + "."
                + Patch.ToString(CultureInfo.InvariantCulture);
            if (PreRelease.Length > 0) s += "-" + PreRelease;
            if (BuildMetadata.Length > 0) s += "+" + BuildMetadata;
            return s;
        }

        /// <summary>Build-metaadat nélküli alak (felhasználói kijelzéshez).</summary>
        public string ToDisplayString()
        {
            string s = Major.ToString(CultureInfo.InvariantCulture) + "."
                + Minor.ToString(CultureInfo.InvariantCulture) + "."
                + Patch.ToString(CultureInfo.InvariantCulture);
            return PreRelease.Length > 0 ? s + "-" + PreRelease : s;
        }

        public static bool operator ==(SemanticVersion? left, SemanticVersion? right)
            => left is null ? right is null : left.Equals(right);

        public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

        public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

        public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

        public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

        public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

        private static int CompareIdentifier(string a, string b)
        {
            bool aNum = IsNumeric(a);
            bool bNum = IsNumeric(b);
            if (aNum && bNum)
            {
                // Hosszabb (vezető nulla nélküli) szám nagyobb; azonos hossznál ordinális.
                int len = a.Length.CompareTo(b.Length);
                return len != 0 ? len : string.CompareOrdinal(a, b);
            }
            if (aNum) return -1;
            if (bNum) return 1;
            return Math.Sign(string.CompareOrdinal(a, b));
        }

        private static bool TryParseCoreNumber(string s, out int value)
        {
            value = 0;
            if (s.Length == 0 || !IsNumeric(s)) return false;
            if (s.Length > 1 && s[0] == '0') return false;
            return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static bool AreValidIdentifiers(string dotted, bool rejectNumericLeadingZero)
        {
            foreach (string id in dotted.Split('.'))
            {
                if (id.Length == 0) return false;
                foreach (char ch in id)
                {
                    bool ok = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '-';
                    if (!ok) return false;
                }
                if (rejectNumericLeadingZero && id.Length > 1 && id[0] == '0' && IsNumeric(id)) return false;
            }
            return true;
        }

        private static bool IsNumeric(string s)
        {
            if (s.Length == 0) return false;
            foreach (char ch in s)
                if (ch < '0' || ch > '9') return false;
            return true;
        }
    }
}
