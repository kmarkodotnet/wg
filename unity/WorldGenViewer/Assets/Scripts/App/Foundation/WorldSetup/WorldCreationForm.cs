#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace WorldGen.App.WorldSetup
{
    /// <summary>Érvényes, generálásra kész kérés; csak <see cref="WorldCreationForm"/> hozhatja létre.</summary>
    public sealed class WorldCreationRequest
    {
        internal WorldCreationRequest(string worldName, ParsedSeed seed, string schemaId, ParameterSet parameters, string? presetId)
        {
            WorldName = worldName;
            Seed = seed.Value;
            SeedFormat = seed.Format;
            SeedText = seed.NormalizedText;
            SchemaId = schemaId;
            Parameters = parameters;
            PresetId = presetId;
        }

        public string WorldName { get; }
        public ulong Seed { get; }
        public SeedFormat SeedFormat { get; }
        public string SeedText { get; }
        public string SchemaId { get; }

        /// <summary>Független másolat; az űrlap későbbi szerkesztése nem hat rá.</summary>
        public ParameterSet Parameters { get; }

        public string? PresetId { get; }
    }

    /// <summary>
    /// A New World képernyő modellje (WF-UI-002): név, seed, random seed,
    /// presetek, Advanced, validáció, Reset Defaults. Érvénytelen állapotból
    /// nem épül <see cref="WorldCreationRequest"/>.
    /// Csak a főszálról használható.
    /// </summary>
    public sealed class WorldCreationForm
    {
        public const int MaxNameLength = 64;

        private readonly List<WorldPreset> _presets;
        private readonly IEntropySource _entropy;
        private bool _nameEditedByUser;

        public ParameterSchema Schema { get; }
        public IReadOnlyList<WorldPreset> Presets => _presets;

        public string WorldName { get; private set; } = "";
        public string SeedText { get; private set; } = "";

        /// <summary>Az aktuális paraméterek. Módosítani csak <see cref="SetParameter"/>-rel szabad (preset-jelölés miatt).</summary>
        public ParameterSet Parameters { get; private set; }

        /// <summary>Null = egyéni (egy paraméter kézzel módosult).</summary>
        public string? SelectedPresetId { get; private set; }

        public bool AdvancedExpanded { get; set; }

        public event Action<WorldCreationForm>? Changed;

        public WorldCreationForm(ParameterSchema schema, IEnumerable<WorldPreset> presets, IEntropySource entropy, string? initialPresetId = null)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _entropy = entropy ?? throw new ArgumentNullException(nameof(entropy));
            _presets = new List<WorldPreset>(presets ?? throw new ArgumentNullException(nameof(presets)));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in _presets)
                if (!ids.Add(p.Id)) throw new ArgumentException("Duplikált preset-azonosító: " + p.Id, nameof(presets));

            Parameters = schema.CreateDefaults();
            if (initialPresetId != null && !ApplyPresetCore(initialPresetId))
                throw new ArgumentException("Ismeretlen kezdő preset: " + initialPresetId, nameof(initialPresetId));
            RandomizeSeedCore();
        }

        /// <summary>A seedből képzett alapnév, amíg a felhasználó nem ír saját nevet: "World-0042".</summary>
        public static string DefaultWorldName(ulong seed)
            => "World-" + (seed % 10000UL).ToString("D4", CultureInfo.InvariantCulture);

        public void SetWorldName(string name)
        {
            WorldName = name ?? "";
            _nameEditedByUser = true;
            Changed?.Invoke(this);
        }

        public void SetSeedText(string text)
        {
            SeedText = text ?? "";
            UpdateDefaultName();
            Changed?.Invoke(this);
        }

        public ulong RandomizeSeed()
        {
            ulong seed = RandomizeSeedCore();
            Changed?.Invoke(this);
            return seed;
        }

        public void SetParameter(string key, ParameterValue value)
        {
            if (!Schema.TryGet(key, out _)) throw new ArgumentException("A sémában nincs ilyen paraméter: " + key, nameof(key));
            Parameters.Set(key, value);
            SelectedPresetId = null;
            Changed?.Invoke(this);
        }

        public bool ApplyPreset(string presetId)
        {
            if (!ApplyPresetCore(presetId)) return false;
            Changed?.Invoke(this);
            return true;
        }

        /// <summary>A paraméterek alapértékre állnak; a név és a seed marad.</summary>
        public void ResetDefaults()
        {
            Parameters = Schema.CreateDefaults();
            SelectedPresetId = null;
            Changed?.Invoke(this);
        }

        public IReadOnlyList<ValidationIssue> Validate()
        {
            var issues = new List<ValidationIssue>();
            string name = WorldName.Trim();
            if (name.Length == 0) issues.Add(new ValidationIssue("name", WorldSetupErrorCode.NameEmpty));
            else if (name.Length > MaxNameLength) issues.Add(new ValidationIssue("name", WorldSetupErrorCode.NameTooLong));
            else if (ContainsControlCharacter(name)) issues.Add(new ValidationIssue("name", WorldSetupErrorCode.NameInvalid));

            if (!SeedCodec.TryParse(SeedText, out _)) issues.Add(new ValidationIssue("seed", WorldSetupErrorCode.SeedInvalid));

            issues.AddRange(Schema.Validate(Parameters));
            return issues;
        }

        public bool CanStart => Validate().Count == 0;

        public bool TryBuildRequest(out WorldCreationRequest? request, out IReadOnlyList<ValidationIssue> issues)
        {
            issues = Validate();
            request = null;
            if (issues.Count > 0) return false;
            SeedCodec.TryParse(SeedText, out var seed);
            request = new WorldCreationRequest(WorldName.Trim(), seed, Schema.SchemaId, Parameters.Clone(), SelectedPresetId);
            return true;
        }

        /// <summary>Export / másolás (WF-WORLD-002). Érvénytelen seednél <see cref="InvalidOperationException"/>.</summary>
        public WorldConfiguration ToConfiguration(string? generatorVersion)
        {
            if (!SeedCodec.TryParse(SeedText, out var seed)) throw new InvalidOperationException("Érvénytelen seed, nem exportálható.");
            return new WorldConfiguration(WorldName.Trim(), seed.Value, Schema.SchemaId, Parameters.Clone(), SelectedPresetId, generatorVersion);
        }

        /// <summary>
        /// Importált konfiguráció betöltése az űrlapba. A sémában nem szereplő
        /// paraméterek kimaradnak, a hiányzók alapértéket kapnak. A tartományon
        /// kívüli értékek megmaradnak, hogy a validáció megmutassa őket
        /// (nincs csendes igazítás).
        /// </summary>
        public void ApplyConfiguration(WorldConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            WorldName = configuration.WorldName;
            _nameEditedByUser = true;
            SeedText = SeedCodec.FormatDecimal(configuration.Seed);
            var set = Schema.CreateDefaults();
            foreach (string key in configuration.Parameters.Keys)
            {
                if (Schema.TryGet(key, out _)) set.Set(key, configuration.Parameters[key]);
            }
            Parameters = set;
            SelectedPresetId = configuration.PresetId != null && FindPreset(configuration.PresetId) != null ? configuration.PresetId : null;
            Changed?.Invoke(this);
        }

        private bool ApplyPresetCore(string presetId)
        {
            var preset = FindPreset(presetId);
            if (preset == null) return false;
            Parameters = preset.CreateParameters(Schema, _entropy);
            SelectedPresetId = preset.Id;
            return true;
        }

        private WorldPreset? FindPreset(string presetId)
        {
            foreach (var p in _presets)
                if (string.Equals(p.Id, presetId, StringComparison.Ordinal)) return p;
            return null;
        }

        private ulong RandomizeSeedCore()
        {
            ulong seed = _entropy.NextUInt64();
            SeedText = SeedCodec.FormatDecimal(seed);
            UpdateDefaultName();
            return seed;
        }

        private void UpdateDefaultName()
        {
            if (!_nameEditedByUser && SeedCodec.TryParse(SeedText, out var seed)) WorldName = DefaultWorldName(seed.Value);
        }

        private static bool ContainsControlCharacter(string s)
        {
            foreach (char ch in s)
                if (ch < 32 || ch == 127) return true;
            return false;
        }
    }
}
