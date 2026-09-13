#nullable enable
using System;
using System.Globalization;
using WorldGen.App.Serialization;
using WorldGen.App.Settings;
using WorldGen.App.WorldSetup;

namespace WorldGen.App.Saves
{
    public enum SaveKind
    {
        Manual,
        Auto,
        Quick,
    }

    public sealed class ThumbnailInfo
    {
        public ThumbnailInfo(string sectionName, int width, int height, string format)
        {
            if (string.IsNullOrEmpty(sectionName)) throw new ArgumentException("Üres szekciónév.", nameof(sectionName));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            SectionName = sectionName;
            Width = width;
            Height = height;
            Format = string.IsNullOrEmpty(format) ? "png" : format;
        }

        public string SectionName { get; }
        public int Width { get; }
        public int Height { get; }
        public string Format { get; }
    }

    /// <summary>
    /// A mentés fejléce (WF-SAVE-001, WF-SAVE-005). A betöltési lista csak
    /// ezt olvassa. A <see cref="Parameters"/> nyers JSON-objektum, mert a
    /// listázáshoz nem kell a séma; betöltéskor a
    /// <see cref="WorldConfigurationCodec.ParametersFromJson"/> értelmezi.
    /// </summary>
    public sealed class SaveHeader
    {
        public int SaveFormatVersion { get; set; } = SaveHeaderCodec.CurrentFormatVersion;
        public string ApplicationVersion { get; set; } = "";
        public string WorldGeneratorVersion { get; set; } = "";

        /// <summary>A világ tartós azonosítója (Save As és átnevezés után is azonos); az autosave-rotáció kulcsa.</summary>
        public string WorldId { get; set; } = "";

        public string WorldName { get; set; } = "";
        public ulong Seed { get; set; }
        public string ParameterSchemaId { get; set; } = "";
        public string? PresetId { get; set; }
        public JsonValue Parameters { get; set; } = JsonValue.CreateObject();
        public SimulationQuality SimulationQuality { get; set; } = SimulationQuality.Balanced;

        /// <summary>A modell szimulációs ideje években (a Core-kötés adja; a lista „Age” oszlopa).</summary>
        public double SimulationAgeYears { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime LastPlayedUtc { get; set; }
        public double PlayTimeSeconds { get; set; }
        public SaveKind Kind { get; set; } = SaveKind.Manual;
        public ThumbnailInfo? Thumbnail { get; set; }

        /// <summary>Kis méretű, rétegenkénti kiegészítő állapot (kamera, overlay); ismeretlen tagok megmaradnak.</summary>
        public JsonValue Extensions { get; set; } = JsonValue.CreateObject();

        /// <summary>Új világ tartós azonosítója (ND-109: nem determinisztikus, nem lép a szimulációba).</summary>
        public static string NewWorldId() => Guid.NewGuid().ToString("N");
    }

    public static class SaveHeaderCodec
    {
        public const int CurrentFormatVersion = 1;
        private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public static JsonValue ToJson(SaveHeader h)
        {
            if (h == null) throw new ArgumentNullException(nameof(h));
            var thumbnail = h.Thumbnail == null
                ? JsonValue.Null
                : JsonValue.CreateObject()
                    .Set("section", h.Thumbnail.SectionName)
                    .Set("width", h.Thumbnail.Width)
                    .Set("height", h.Thumbnail.Height)
                    .Set("format", h.Thumbnail.Format);
            return JsonValue.CreateObject()
                .Set("saveFormatVersion", h.SaveFormatVersion)
                .Set("applicationVersion", h.ApplicationVersion ?? "")
                .Set("worldGeneratorVersion", h.WorldGeneratorVersion ?? "")
                .Set("worldId", h.WorldId ?? "")
                .Set("worldName", h.WorldName ?? "")
                .Set("seed", h.Seed.ToString(CultureInfo.InvariantCulture))
                .Set("parameterSchema", h.ParameterSchemaId ?? "")
                .Set("preset", h.PresetId == null ? JsonValue.Null : JsonValue.FromString(h.PresetId))
                .Set("parameters", h.Parameters ?? JsonValue.CreateObject())
                .Set("simulationQuality", h.SimulationQuality.ToString())
                .Set("simulationAgeYears", double.IsFinite(h.SimulationAgeYears) ? JsonValue.FromNumber(h.SimulationAgeYears) : JsonValue.FromNumber(0))
                .Set("createdUtc", FormatDate(h.CreatedUtc))
                .Set("lastPlayedUtc", FormatDate(h.LastPlayedUtc))
                .Set("playTimeSeconds", double.IsFinite(h.PlayTimeSeconds) ? JsonValue.FromNumber(h.PlayTimeSeconds) : JsonValue.FromNumber(0))
                .Set("kind", h.Kind.ToString())
                .Set("thumbnail", thumbnail)
                .Set("extensions", h.Extensions ?? JsonValue.CreateObject());
        }

        /// <summary>
        /// Kötelező: saveFormatVersion, worldName, seed, createdUtc, lastPlayedUtc.
        /// Hiányuk <see cref="SaveCorruptedException"/> (HeaderInvalid). A többi
        /// mező tűrően olvasott, hogy egy újabb formátumú mentés is listázható legyen.
        /// </summary>
        public static SaveHeader FromJson(JsonValue root)
        {
            if (root == null || root.Kind != JsonValueKind.Object) throw Invalid("a fejléc nem objektum");
            var h = new SaveHeader();

            if (!(root.GetMember("saveFormatVersion") is JsonValue v) || !v.TryGetInt32(out int version) || version < 1)
                throw Invalid("saveFormatVersion");
            h.SaveFormatVersion = version;

            if (!(root.GetMember("worldName") is JsonValue n) || !n.TryGetString(out string? name)) throw Invalid("worldName");
            h.WorldName = name;

            if (!(root.GetMember("seed") is JsonValue s) || !s.TryGetString(out string? seedText)
                || !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
                throw Invalid("seed");
            h.Seed = seed;

            h.CreatedUtc = ParseDate(root, "createdUtc") ?? throw Invalid("createdUtc");
            h.LastPlayedUtc = ParseDate(root, "lastPlayedUtc") ?? throw Invalid("lastPlayedUtc");

            h.ApplicationVersion = OptionalString(root, "applicationVersion") ?? "";
            h.WorldGeneratorVersion = OptionalString(root, "worldGeneratorVersion") ?? "";
            h.WorldId = OptionalString(root, "worldId") ?? "";
            h.ParameterSchemaId = OptionalString(root, "parameterSchema") ?? "";
            h.PresetId = OptionalString(root, "preset");

            var parameters = root.GetMember("parameters");
            h.Parameters = parameters != null && parameters.Kind == JsonValueKind.Object ? parameters : JsonValue.CreateObject();

            if (OptionalString(root, "simulationQuality") is string q && q.Length > 0 && char.IsLetter(q[0])
                && Enum.TryParse(q, false, out SimulationQuality quality) && Enum.IsDefined(typeof(SimulationQuality), quality))
                h.SimulationQuality = quality;

            if (root.GetMember("simulationAgeYears") is JsonValue age && age.TryGetDouble(out double years)) h.SimulationAgeYears = years;
            if (root.GetMember("playTimeSeconds") is JsonValue pt && pt.TryGetDouble(out double play) && play >= 0) h.PlayTimeSeconds = play;

            if (OptionalString(root, "kind") is string k && k.Length > 0 && char.IsLetter(k[0])
                && Enum.TryParse(k, false, out SaveKind kind) && Enum.IsDefined(typeof(SaveKind), kind))
                h.Kind = kind;

            var t = root.GetMember("thumbnail");
            if (t != null && t.Kind == JsonValueKind.Object
                && OptionalString(t, "section") is string section && section.Length > 0
                && t.GetMember("width") is JsonValue tw && tw.TryGetInt32(out int width) && width > 0
                && t.GetMember("height") is JsonValue th && th.TryGetInt32(out int height) && height > 0)
            {
                h.Thumbnail = new ThumbnailInfo(section, width, height, OptionalString(t, "format") ?? "png");
            }

            var ext = root.GetMember("extensions");
            h.Extensions = ext != null && ext.Kind == JsonValueKind.Object ? ext : JsonValue.CreateObject();
            return h;
        }

        public static string FormatDate(DateTime utc)
        {
            var value = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        private static DateTime? ParseDate(JsonValue obj, string name)
        {
            string? text = OptionalString(obj, name);
            if (text == null) return null;
            if (!DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
                return null;
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string? OptionalString(JsonValue obj, string name)
            => obj.GetMember(name) is JsonValue node && node.TryGetString(out string? s) ? s : null;

        private static SaveCorruptedException Invalid(string field)
            => new SaveCorruptedException(SaveCorruptionReason.HeaderInvalid, "Érvénytelen vagy hiányzó fejlécmező: " + field);
    }
}
