#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using WorldGen.App.Serialization;

namespace WorldGen.App.WorldSetup
{
    /// <summary>Reprodukálható világ-leírás: név, seed, séma, paraméterek, generátorverzió (WF-WORLD-002).</summary>
    public sealed class WorldConfiguration
    {
        public WorldConfiguration(string worldName, ulong seed, string schemaId, ParameterSet parameters, string? presetId, string? generatorVersion)
        {
            WorldName = worldName ?? "";
            Seed = seed;
            SchemaId = schemaId ?? "";
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            PresetId = presetId;
            GeneratorVersion = generatorVersion;
        }

        public string WorldName { get; }
        public ulong Seed { get; }
        public string SchemaId { get; }
        public ParameterSet Parameters { get; }
        public string? PresetId { get; }
        public string? GeneratorVersion { get; }
    }

    public enum ConfigurationImportCode
    {
        // végzetes
        InvalidJson,
        WrongFormat,
        UnsupportedVersion,
        SeedMissing,
        SeedInvalid,

        // figyelmeztetés
        NameMissing,
        SchemaMismatch,
        GeneratorVersionMismatch,
        ParameterMissing,
        ParameterInvalid,
        UnknownParameter,
    }

    public sealed class ConfigurationImportIssue
    {
        public ConfigurationImportIssue(ConfigurationImportCode code, string detail)
        {
            Code = code;
            Detail = detail ?? "";
        }

        public ConfigurationImportCode Code { get; }

        /// <summary>Paraméterkulcs, várt/kapott érték vagy parser-hiba; a UI ezt a lokalizált szöveg mellé teheti.</summary>
        public string Detail { get; }

        public bool IsFatal => Code <= ConfigurationImportCode.SeedInvalid;

        public override string ToString() => Code + (Detail.Length > 0 ? ": " + Detail : "");
    }

    public sealed class ConfigurationImportResult
    {
        internal ConfigurationImportResult(WorldConfiguration? configuration, IReadOnlyList<ConfigurationImportIssue> issues)
        {
            Configuration = configuration;
            Issues = issues;
        }

        public WorldConfiguration? Configuration { get; }
        public IReadOnlyList<ConfigurationImportIssue> Issues { get; }
        public bool Success => Configuration != null;
    }

    /// <summary>
    /// Export / import JSON és vágólap-szöveg. A seed decimális SZÖVEG, hogy
    /// más JSON-eszközök (double-ként olvasva) se csonkítsák a 64 bitet.
    /// </summary>
    public static class WorldConfigurationCodec
    {
        public const string FormatId = "worldgen.world-configuration";
        public const int FormatVersion = 1;

        public static JsonValue ToJson(WorldConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            return JsonValue.CreateObject()
                .Set("format", FormatId)
                .Set("formatVersion", FormatVersion)
                .Set("worldName", configuration.WorldName)
                .Set("seed", SeedCodec.FormatDecimal(configuration.Seed))
                .Set("seedHex", SeedCodec.FormatHex(configuration.Seed))
                .Set("parameterSchema", configuration.SchemaId)
                .Set("generatorVersion", configuration.GeneratorVersion == null ? JsonValue.Null : JsonValue.FromString(configuration.GeneratorVersion))
                .Set("preset", configuration.PresetId == null ? JsonValue.Null : JsonValue.FromString(configuration.PresetId))
                .Set("parameters", ParametersToJson(configuration.Parameters));
        }

        /// <summary>Fájlba mentéshez: tagolt, LF, záró sortöréssel.</summary>
        public static string Export(WorldConfiguration configuration) => JsonWriter.Write(ToJson(configuration)) + "\n";

        /// <summary>"Copy World Parameters": egysoros JSON.</summary>
        public static string ToClipboardText(WorldConfiguration configuration) => JsonWriter.Write(ToJson(configuration), indented: false);

        public static JsonValue ParametersToJson(ParameterSet parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            var obj = JsonValue.CreateObject();
            foreach (string key in parameters.Keys)
            {
                var v = parameters[key];
                switch (v.Kind)
                {
                    case ParameterKind.Real: obj.Set(key, JsonValue.FromNumber(v.RealValue)); break;
                    case ParameterKind.Integer: obj.Set(key, JsonValue.FromNumber(v.IntegerValue)); break;
                    case ParameterKind.Boolean: obj.Set(key, v.BooleanValue); break;
                    default: obj.Set(key, v.ChoiceValue); break;
                }
            }
            return obj;
        }

        /// <summary>Paraméterek beolvasása a séma típusai szerint; hiányzó/hibás → alapérték + figyelmeztetés.</summary>
        public static ParameterSet ParametersFromJson(JsonValue? obj, ParameterSchema schema, List<ConfigurationImportIssue> issues)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (issues == null) throw new ArgumentNullException(nameof(issues));
            var set = new ParameterSet();
            bool isObject = obj != null && obj.Kind == JsonValueKind.Object;
            foreach (var d in schema.Definitions)
            {
                var node = isObject ? obj!.GetMember(d.Key) : null;
                if (node == null)
                {
                    issues.Add(new ConfigurationImportIssue(ConfigurationImportCode.ParameterMissing, d.Key));
                    set.Set(d.Key, d.DefaultValue);
                    continue;
                }
                if (TryRead(d.Kind, node, out var value)) set.Set(d.Key, value);
                else
                {
                    issues.Add(new ConfigurationImportIssue(ConfigurationImportCode.ParameterInvalid, d.Key + " = " + node));
                    set.Set(d.Key, d.DefaultValue);
                }
            }
            if (isObject)
            {
                foreach (var member in obj!.Members)
                    if (!schema.TryGet(member.Key, out _)) issues.Add(new ConfigurationImportIssue(ConfigurationImportCode.UnknownParameter, member.Key));
            }
            return set;
        }

        public static ConfigurationImportResult Import(string text, ParameterSchema schema, string? currentGeneratorVersion = null)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var issues = new List<ConfigurationImportIssue>();
            JsonValue root;
            try
            {
                root = JsonParser.Parse(text ?? "");
            }
            catch (JsonFormatException ex)
            {
                return Fatal(issues, ConfigurationImportCode.InvalidJson, ex.Message);
            }
            if (root.Kind != JsonValueKind.Object
                || !(root.GetMember("format") is JsonValue f) || !f.TryGetString(out string? format) || format != FormatId)
                return Fatal(issues, ConfigurationImportCode.WrongFormat, "");

            if (!(root.GetMember("formatVersion") is JsonValue fv) || !fv.TryGetInt32(out int version) || version < 1 || version > FormatVersion)
                return Fatal(issues, ConfigurationImportCode.UnsupportedVersion, root.GetMember("formatVersion")?.ToString() ?? "");

            var seedNode = root.GetMember("seed");
            if (seedNode == null) return Fatal(issues, ConfigurationImportCode.SeedMissing, "");
            ulong seed;
            if (seedNode.TryGetString(out string? seedText))
            {
                if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                    return Fatal(issues, ConfigurationImportCode.SeedInvalid, seedText);
            }
            else if (!seedNode.TryGetUInt64(out seed))
            {
                return Fatal(issues, ConfigurationImportCode.SeedInvalid, seedNode.ToString());
            }

            string name = root.GetMember("worldName") is JsonValue n && n.TryGetString(out string? nv) ? nv.Trim() : "";
            if (name.Length == 0)
            {
                issues.Add(new ConfigurationImportIssue(ConfigurationImportCode.NameMissing, ""));
                name = WorldCreationForm.DefaultWorldName(seed);
            }

            string schemaId = root.GetMember("parameterSchema") is JsonValue s && s.TryGetString(out string? sv) ? sv : "";
            if (!string.Equals(schemaId, schema.SchemaId, StringComparison.Ordinal))
                issues.Add(new ConfigurationImportIssue(ConfigurationImportCode.SchemaMismatch, schemaId + " → " + schema.SchemaId));

            string? generator = root.GetMember("generatorVersion") is JsonValue g && g.TryGetString(out string? gv) ? gv : null;
            if (generator != null && currentGeneratorVersion != null && !string.Equals(generator, currentGeneratorVersion, StringComparison.Ordinal))
                issues.Add(new ConfigurationImportIssue(ConfigurationImportCode.GeneratorVersionMismatch, generator + " → " + currentGeneratorVersion));

            string? preset = root.GetMember("preset") is JsonValue p && p.TryGetString(out string? pv) ? pv : null;
            var parameters = ParametersFromJson(root.GetMember("parameters"), schema, issues);

            return new ConfigurationImportResult(new WorldConfiguration(name, seed, schema.SchemaId, parameters, preset, generator), issues);
        }

        private static bool TryRead(ParameterKind kind, JsonValue node, out ParameterValue value)
        {
            value = default;
            switch (kind)
            {
                case ParameterKind.Real:
                    if (!node.TryGetDouble(out double d)) return false;
                    value = ParameterValue.Real(d);
                    return true;
                case ParameterKind.Integer:
                    if (!node.TryGetInt64(out long l)) return false;
                    value = ParameterValue.Integer(l);
                    return true;
                case ParameterKind.Boolean:
                    if (!node.TryGetBoolean(out bool b)) return false;
                    value = ParameterValue.Boolean(b);
                    return true;
                default:
                    if (!node.TryGetString(out string? c) || c.Length == 0) return false;
                    value = ParameterValue.Choice(c);
                    return true;
            }
        }

        private static ConfigurationImportResult Fatal(List<ConfigurationImportIssue> issues, ConfigurationImportCode code, string detail)
        {
            issues.Add(new ConfigurationImportIssue(code, detail));
            return new ConfigurationImportResult(null, issues);
        }
    }
}
