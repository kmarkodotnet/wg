#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using WorldGen.App.Localization;

namespace WorldGen.App.WorldInfo
{
    /// <summary>Invariáns kultúrájú mennyiség-formázás az információs UI-hoz (WF-WORLD-001).</summary>
    public static class QuantityFormatter
    {
        /// <summary>"350 yr", "12.5 ka", "540 Ma", "2.73 Ga". Negatív vagy nem véges értékre üres szöveg.</summary>
        public static string FormatAge(double years)
        {
            if (!double.IsFinite(years) || years < 0) return "";
            string[] units = { "yr", "ka", "Ma", "Ga" };
            double value = years;
            int unit = 0;
            while (true)
            {
                // Az egység akkor lép tovább, ha a KEREKÍTETT érték érné el az 1000-et (999.6 yr → "1.00 ka", nem "1000 yr").
                int decimals = unit == 0 ? 0 : DecimalsFor(value);
                double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
                if (rounded >= 1000 && unit < units.Length - 1)
                {
                    value /= 1000;
                    unit++;
                    continue;
                }
                return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + " " + units[unit];
            }
        }

        /// <summary>"7,021 km".</summary>
        public static string FormatKilometres(double km) => FormatNumber(km, 0) + " km";

        /// <summary>Hányad (0..1) → "63%".</summary>
        public static string FormatPercent(double fraction, int decimals = 0)
            => double.IsFinite(fraction) ? FormatNumber(fraction * 100.0, decimals) + "%" : "";

        public static string FormatCelsius(double celsius) => double.IsFinite(celsius) ? FormatNumber(celsius, 1) + " °C" : "";

        public static string FormatWithUnit(double value, int decimals, string unit)
            => double.IsFinite(value) ? FormatNumber(value, decimals) + (string.IsNullOrEmpty(unit) ? "" : " " + unit) : "";

        public static string FormatCount(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Játékidő: "45 s", "12 min", "1 h 30 min".</summary>
        public static string FormatDuration(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) return "";
            long total = (long)Math.Floor(seconds);
            if (total < 60) return total.ToString(CultureInfo.InvariantCulture) + " s";
            long minutes = total / 60;
            if (minutes < 60) return minutes.ToString(CultureInfo.InvariantCulture) + " min";
            long hours = minutes / 60;
            long rest = minutes % 60;
            string h = hours.ToString("N0", CultureInfo.InvariantCulture) + " h";
            return rest == 0 ? h : h + " " + rest.ToString(CultureInfo.InvariantCulture) + " min";
        }

        /// <summary>1024-alapú fájlméret: "512 B", "1.5 KB", "12.3 MB".</summary>
        public static string FormatFileSize(long bytes)
        {
            if (bytes < 0) return "";
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            string[] units = { "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = -1;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(value >= 100 ? "F0" : "F1", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        private static int DecimalsFor(double value) => value >= 100 ? 0 : value >= 10 ? 1 : 2;

        private static string FormatNumber(double value, int decimals)
        {
            if (!double.IsFinite(value)) return "";
            double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            if (rounded == 0) rounded = 0; // -0 elkerülése
            return rounded.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// A Planet Profile bemenete. Minden mező opcionális: a Core-kötés csak azt
    /// tölti ki, amit a modell ténylegesen kiszámol (I4 — nincs kitalált érték).
    /// </summary>
    public sealed class PlanetProfileData
    {
        public string? PlanetName { get; set; }
        public ulong? Seed { get; set; }
        public double? AgeYears { get; set; }
        public double? RadiusKm { get; set; }
        public double? MassEarths { get; set; }
        public double? SurfaceGravityG { get; set; }

        /// <summary>0..1.</summary>
        public double? OceanCoverage { get; set; }

        public double? MeanTemperatureCelsius { get; set; }
        public double? SurfacePressureAtm { get; set; }
        public int? Continents { get; set; }
        public int? TectonicPlates { get; set; }

        /// <summary>Lokalizációs kulcs (a besorolás szabálya a Core-kötésé).</summary>
        public string? TectonicActivityKey { get; set; }
    }

    public readonly struct ProfileRow
    {
        public ProfileRow(string labelKey, string value)
        {
            LabelKey = labelKey;
            Value = value;
        }

        public string LabelKey { get; }
        public string Value { get; }
    }

    public static class PlanetProfile
    {
        /// <summary>Sorok a megadott értékekből; hiányzó vagy nem véges érték nem kap sort.</summary>
        public static IReadOnlyList<ProfileRow> BuildRows(PlanetProfileData data, LocalizationTable? text = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var rows = new List<ProfileRow>();
            if (!string.IsNullOrWhiteSpace(data.PlanetName)) rows.Add(new ProfileRow("profile.planet", data.PlanetName!));
            if (data.Seed.HasValue) rows.Add(new ProfileRow("profile.seed", data.Seed.Value.ToString(CultureInfo.InvariantCulture)));
            Add(rows, "profile.age", data.AgeYears, QuantityFormatter.FormatAge);
            Add(rows, "profile.radius", data.RadiusKm, QuantityFormatter.FormatKilometres);
            string earth = text != null ? text.Get("unit.earthMasses") : "Earth";
            Add(rows, "profile.mass", data.MassEarths, v => QuantityFormatter.FormatWithUnit(v, 2, earth));
            Add(rows, "profile.gravity", data.SurfaceGravityG, v => QuantityFormatter.FormatWithUnit(v, 2, "g"));
            Add(rows, "profile.oceanCoverage", data.OceanCoverage, v => QuantityFormatter.FormatPercent(v));
            Add(rows, "profile.meanTemperature", data.MeanTemperatureCelsius, QuantityFormatter.FormatCelsius);
            Add(rows, "profile.pressure", data.SurfacePressureAtm, v => QuantityFormatter.FormatWithUnit(v, 2, "atm"));
            if (data.Continents.HasValue && data.Continents.Value >= 0)
                rows.Add(new ProfileRow("profile.continents", QuantityFormatter.FormatCount(data.Continents.Value)));
            if (data.TectonicPlates.HasValue && data.TectonicPlates.Value >= 0)
                rows.Add(new ProfileRow("profile.plates", QuantityFormatter.FormatCount(data.TectonicPlates.Value)));
            if (!string.IsNullOrEmpty(data.TectonicActivityKey))
                rows.Add(new ProfileRow("profile.tectonicActivity", text != null ? text.Get(data.TectonicActivityKey!) : data.TectonicActivityKey!));
            return rows;
        }

        /// <summary>"Planet: Gaia-8214\nAge  2.73 Ga …" — a vágólapra másoláshoz, lokalizált címkékkel.</summary>
        public static string ToPlainText(IReadOnlyList<ProfileRow> rows, LocalizationTable text)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (text == null) throw new ArgumentNullException(nameof(text));
            int width = 0;
            foreach (var r in rows) width = Math.Max(width, text.Get(r.LabelKey).Length);
            var lines = new List<string>();
            foreach (var r in rows) lines.Add(text.Get(r.LabelKey).PadRight(width + 2) + r.Value);
            return string.Join("\n", lines);
        }

        private static void Add(List<ProfileRow> rows, string labelKey, double? value, Func<double, string> format)
        {
            if (!value.HasValue || !double.IsFinite(value.Value)) return;
            string formatted = format(value.Value);
            if (formatted.Length > 0) rows.Add(new ProfileRow(labelKey, formatted));
        }
    }
}
