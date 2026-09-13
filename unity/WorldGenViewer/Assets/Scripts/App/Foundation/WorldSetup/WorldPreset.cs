#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.WorldSetup
{
    public static class WorldPresetIds
    {
        public const string EarthLike = "earth-like";
        public const string OceanWorld = "ocean-world";
        public const string DryWorld = "dry-world";
        public const string HighGravity = "high-gravity";
        public const string LowGravity = "low-gravity";
        public const string GeologicallyActive = "geologically-active";
        public const string Random = "random";
    }

    /// <summary>
    /// Előre beállított paraméterkészlet (WF-UI-002). A felülírások fizikai
    /// értékeit a Core-kötés adja; a Foundation csak alkalmazza őket.
    /// A <see cref="RandomizesAll"/> preset minden paramétert a sémahatárokon
    /// belül sorsol, a felülírások ezután érvényesülnek.
    /// </summary>
    public sealed class WorldPreset
    {
        private readonly List<KeyValuePair<string, ParameterValue>> _overrides;

        public string Id { get; }
        public string NameKey { get; }
        public bool RandomizesAll { get; }
        public IReadOnlyList<KeyValuePair<string, ParameterValue>> Overrides => _overrides;

        public WorldPreset(string id, string nameKey, IEnumerable<KeyValuePair<string, ParameterValue>>? overrides = null, bool randomizesAll = false)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Üres preset-azonosító.", nameof(id));
            Id = id;
            NameKey = nameKey ?? "";
            RandomizesAll = randomizesAll;
            _overrides = overrides == null
                ? new List<KeyValuePair<string, ParameterValue>>()
                : new List<KeyValuePair<string, ParameterValue>>(overrides);
        }

        /// <summary>
        /// A preset paraméterkészlete. Ha egy felülírás nem illik a sémához
        /// (ismeretlen kulcs, rossz típus, tartományon kívül), az a preset
        /// definíciójának hibája: <see cref="ArgumentException"/>.
        /// </summary>
        public ParameterSet CreateParameters(ParameterSchema schema, IEntropySource entropy)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var set = schema.CreateDefaults();
            if (RandomizesAll)
            {
                if (entropy == null) throw new ArgumentNullException(nameof(entropy));
                foreach (var d in schema.Definitions) set.Set(d.Key, RandomValue(d, entropy));
            }
            foreach (var o in _overrides)
            {
                if (!schema.TryGet(o.Key, out var definition))
                    throw new ArgumentException("A '" + Id + "' preset ismeretlen paramétert ír felül: " + o.Key);
                if (definition.Check(o.Value).HasValue)
                    throw new ArgumentException("A '" + Id + "' preset érvénytelen értéket ad: " + o.Key + " = " + o.Value);
                set.Set(o.Key, o.Value);
            }
            return set;
        }

        public static ParameterValue RandomValue(ParameterDefinition definition, IEntropySource entropy)
        {
            switch (definition.Kind)
            {
                case ParameterKind.Real:
                    double u = EntropyMath.NextUnitDouble(entropy);
                    double v = definition.Minimum + u * (definition.Maximum - definition.Minimum);
                    return ParameterValue.Real(Math.Min(Math.Max(v, definition.Minimum), definition.Maximum));
                case ParameterKind.Integer:
                    return ParameterValue.Integer(EntropyMath.NextInclusive(entropy, definition.IntegerMinimum, definition.IntegerMaximum));
                case ParameterKind.Boolean:
                    return ParameterValue.Boolean((entropy.NextUInt64() & 1UL) == 1UL);
                default:
                    int index = (int)EntropyMath.NextInclusive(entropy, 0, definition.Choices.Count - 1);
                    return ParameterValue.Choice(definition.Choices[index]);
            }
        }
    }
}
