#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace WorldGen.App.WorldSetup
{
    public enum ParameterKind
    {
        Real,
        Integer,
        Boolean,
        Choice,
    }

    /// <summary>Egy generálási paraméter értéke. A valós érték mindig véges.</summary>
    public readonly struct ParameterValue : IEquatable<ParameterValue>
    {
        private readonly double _real;
        private readonly long _integer;
        private readonly bool _boolean;
        private readonly string? _choice;

        public ParameterKind Kind { get; }

        private ParameterValue(ParameterKind kind, double real, long integer, bool boolean, string? choice)
        {
            Kind = kind;
            _real = real;
            _integer = integer;
            _boolean = boolean;
            _choice = choice;
        }

        public static ParameterValue Real(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentException("A paraméterérték nem lehet NaN vagy végtelen.", nameof(value));
            return new ParameterValue(ParameterKind.Real, value, 0, false, null);
        }

        public static ParameterValue Integer(long value) => new ParameterValue(ParameterKind.Integer, 0, value, false, null);

        public static ParameterValue Boolean(bool value) => new ParameterValue(ParameterKind.Boolean, 0, 0, value, null);

        public static ParameterValue Choice(string choiceId)
        {
            if (string.IsNullOrEmpty(choiceId)) throw new ArgumentException("Üres választás-azonosító.", nameof(choiceId));
            return new ParameterValue(ParameterKind.Choice, 0, 0, false, choiceId);
        }

        public double RealValue => Kind == ParameterKind.Real ? _real : throw Mismatch(ParameterKind.Real);
        public long IntegerValue => Kind == ParameterKind.Integer ? _integer : throw Mismatch(ParameterKind.Integer);
        public bool BooleanValue => Kind == ParameterKind.Boolean ? _boolean : throw Mismatch(ParameterKind.Boolean);
        public string ChoiceValue => Kind == ParameterKind.Choice ? _choice! : throw Mismatch(ParameterKind.Choice);

        /// <summary>Bitpontos egyezés (a valós értéknél is), mert a paraméter a szimulációba lép.</summary>
        public bool Equals(ParameterValue other)
        {
            if (Kind != other.Kind) return false;
            switch (Kind)
            {
                case ParameterKind.Real: return BitConverter.DoubleToInt64Bits(_real) == BitConverter.DoubleToInt64Bits(other._real);
                case ParameterKind.Integer: return _integer == other._integer;
                case ParameterKind.Boolean: return _boolean == other._boolean;
                default: return string.Equals(_choice, other._choice, StringComparison.Ordinal);
            }
        }

        public override bool Equals(object? obj) => obj is ParameterValue v && Equals(v);

        public override int GetHashCode()
        {
            switch (Kind)
            {
                case ParameterKind.Real: return BitConverter.DoubleToInt64Bits(_real).GetHashCode();
                case ParameterKind.Integer: return _integer.GetHashCode() ^ 0x1000;
                case ParameterKind.Boolean: return _boolean ? 0x2001 : 0x2000;
                default: return StringComparer.Ordinal.GetHashCode(_choice ?? "");
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case ParameterKind.Real: return _real.ToString("R", CultureInfo.InvariantCulture);
                case ParameterKind.Integer: return _integer.ToString(CultureInfo.InvariantCulture);
                case ParameterKind.Boolean: return _boolean ? "true" : "false";
                default: return _choice ?? "";
            }
        }

        public static bool operator ==(ParameterValue left, ParameterValue right) => left.Equals(right);

        public static bool operator !=(ParameterValue left, ParameterValue right) => !left.Equals(right);

        private InvalidOperationException Mismatch(ParameterKind expected)
            => new InvalidOperationException("Paramétertípus-eltérés: várt " + expected + ", tényleges " + Kind);
    }

    public enum WorldSetupErrorCode
    {
        NameEmpty,
        NameTooLong,
        NameInvalid,
        SeedInvalid,
        ParameterMissing,
        ParameterOutOfRange,
        ParameterInvalid,
        UnknownParameter,
    }

    public sealed class ValidationIssue
    {
        /// <summary>"name", "seed", vagy a paraméter kulcsa.</summary>
        public string Field { get; }

        public WorldSetupErrorCode Code { get; }

        public ValidationIssue(string field, WorldSetupErrorCode code)
        {
            Field = field ?? "";
            Code = code;
        }

        /// <summary>Lokalizációs kulcs a felhasználói hibaszöveghez.</summary>
        public string MessageKey
        {
            get
            {
                switch (Code)
                {
                    case WorldSetupErrorCode.NameEmpty: return "worldCreation.error.nameEmpty";
                    case WorldSetupErrorCode.NameTooLong: return "worldCreation.error.nameTooLong";
                    case WorldSetupErrorCode.NameInvalid: return "worldCreation.error.nameInvalid";
                    case WorldSetupErrorCode.SeedInvalid: return "worldCreation.error.seedInvalid";
                    case WorldSetupErrorCode.ParameterMissing: return "worldCreation.error.parameterMissing";
                    case WorldSetupErrorCode.ParameterOutOfRange: return "worldCreation.error.parameterOutOfRange";
                    default: return "worldCreation.error.parameterInvalid";
                }
            }
        }

        public override string ToString() => Field + ": " + Code;
    }

    /// <summary>
    /// Egy generálási paraméter leírása (WF-UI-002). A Core-kötés tölti fel a
    /// valós Core-paraméterekből; a Foundation csak a határokat és a típust ismeri.
    /// </summary>
    public sealed class ParameterDefinition
    {
        public string Key { get; }
        public string LabelKey { get; }
        public string? TooltipKey { get; }
        public ParameterKind Kind { get; }

        /// <summary>Real és Integer esetén a zárt tartomány; egyébként 0.</summary>
        public double Minimum { get; }
        public double Maximum { get; }

        public long IntegerMinimum { get; }
        public long IntegerMaximum { get; }

        public ParameterValue DefaultValue { get; }
        public IReadOnlyList<string> Choices { get; }
        public string Unit { get; }
        public bool IsAdvanced { get; }

        private ParameterDefinition(string key, string labelKey, string? tooltipKey, ParameterKind kind, double minimum, double maximum,
            long integerMinimum, long integerMaximum, ParameterValue defaultValue, IReadOnlyList<string> choices, string unit, bool isAdvanced)
        {
            if (!IsValidKey(key)) throw new ArgumentException("Érvénytelen paraméterkulcs: '" + key + "'", nameof(key));
            Key = key;
            LabelKey = labelKey ?? "";
            TooltipKey = tooltipKey;
            Kind = kind;
            Minimum = minimum;
            Maximum = maximum;
            IntegerMinimum = integerMinimum;
            IntegerMaximum = integerMaximum;
            DefaultValue = defaultValue;
            Choices = choices;
            Unit = unit ?? "";
            IsAdvanced = isAdvanced;
            if (Check(defaultValue) != null) throw new ArgumentException("Az alapérték érvénytelen: " + key, nameof(defaultValue));
        }

        public static ParameterDefinition Real(string key, string labelKey, double minimum, double maximum, double defaultValue,
            string unit = "", bool isAdvanced = false, string? tooltipKey = null)
        {
            if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
                throw new ArgumentException("Érvénytelen tartomány: " + key);
            return new ParameterDefinition(key, labelKey, tooltipKey, ParameterKind.Real, minimum, maximum, 0, 0,
                ParameterValue.Real(defaultValue), Array.Empty<string>(), unit, isAdvanced);
        }

        public static ParameterDefinition Integer(string key, string labelKey, long minimum, long maximum, long defaultValue,
            string unit = "", bool isAdvanced = false, string? tooltipKey = null)
        {
            if (minimum > maximum) throw new ArgumentException("Érvénytelen tartomány: " + key);
            return new ParameterDefinition(key, labelKey, tooltipKey, ParameterKind.Integer, minimum, maximum, minimum, maximum,
                ParameterValue.Integer(defaultValue), Array.Empty<string>(), unit, isAdvanced);
        }

        public static ParameterDefinition Boolean(string key, string labelKey, bool defaultValue, bool isAdvanced = false, string? tooltipKey = null)
            => new ParameterDefinition(key, labelKey, tooltipKey, ParameterKind.Boolean, 0, 0, 0, 0,
                ParameterValue.Boolean(defaultValue), Array.Empty<string>(), "", isAdvanced);

        public static ParameterDefinition Choice(string key, string labelKey, IEnumerable<string> choices, string defaultChoice,
            bool isAdvanced = false, string? tooltipKey = null)
        {
            if (choices == null) throw new ArgumentNullException(nameof(choices));
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string c in choices)
            {
                if (string.IsNullOrEmpty(c) || !seen.Add(c)) throw new ArgumentException("Üres vagy duplikált választás: " + key);
                list.Add(c);
            }
            if (list.Count == 0) throw new ArgumentException("Legalább egy választás kell: " + key);
            return new ParameterDefinition(key, labelKey, tooltipKey, ParameterKind.Choice, 0, 0, 0, 0,
                ParameterValue.Choice(defaultChoice), list, "", isAdvanced);
        }

        /// <summary>Null, ha az érték érvényes; egyébként a hiba kódja.</summary>
        public WorldSetupErrorCode? Check(ParameterValue value)
        {
            if (value.Kind != Kind) return WorldSetupErrorCode.ParameterInvalid;
            switch (Kind)
            {
                case ParameterKind.Real:
                    return value.RealValue < Minimum || value.RealValue > Maximum ? WorldSetupErrorCode.ParameterOutOfRange : (WorldSetupErrorCode?)null;
                case ParameterKind.Integer:
                    return value.IntegerValue < IntegerMinimum || value.IntegerValue > IntegerMaximum ? WorldSetupErrorCode.ParameterOutOfRange : (WorldSetupErrorCode?)null;
                case ParameterKind.Choice:
                    foreach (string c in Choices)
                        if (string.Equals(c, value.ChoiceValue, StringComparison.Ordinal)) return null;
                    return WorldSetupErrorCode.ParameterInvalid;
                default:
                    return null;
            }
        }

        public static bool IsValidKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (char ch in key!)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-';
                if (!ok) return false;
            }
            return true;
        }
    }

    /// <summary>Paraméterkulcs → érték, beszúrási sorrendben.</summary>
    public sealed class ParameterSet
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, ParameterValue> _values = new Dictionary<string, ParameterValue>(StringComparer.Ordinal);

        public int Count => _order.Count;

        public IReadOnlyList<string> Keys => _order;

        public void Set(string key, ParameterValue value)
        {
            if (!ParameterDefinition.IsValidKey(key)) throw new ArgumentException("Érvénytelen paraméterkulcs: '" + key + "'", nameof(key));
            if (!_values.ContainsKey(key)) _order.Add(key);
            _values[key] = value;
        }

        public bool TryGet(string key, out ParameterValue value) => _values.TryGetValue(key, out value);

        public ParameterValue this[string key] => _values.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException("Nincs ilyen paraméter: " + key);

        public bool Remove(string key)
        {
            if (!_values.Remove(key)) return false;
            _order.Remove(key);
            return true;
        }

        public ParameterSet Clone()
        {
            var copy = new ParameterSet();
            foreach (string key in _order) copy.Set(key, _values[key]);
            return copy;
        }

        /// <summary>Ugyanazok a kulcsok, bitpontosan azonos értékekkel (sorrendtől függetlenül).</summary>
        public bool ContentEquals(ParameterSet other)
        {
            if (other == null || other.Count != Count) return false;
            foreach (var pair in _values)
                if (!other._values.TryGetValue(pair.Key, out var v) || !v.Equals(pair.Value)) return false;
            return true;
        }
    }

    public sealed class ParameterSchema
    {
        private readonly List<ParameterDefinition> _definitions;
        private readonly Dictionary<string, ParameterDefinition> _byKey = new Dictionary<string, ParameterDefinition>(StringComparer.Ordinal);

        /// <summary>A séma azonosítója (mentésbe és exportba kerül), pl. "worldgen.core.planet-v1".</summary>
        public string SchemaId { get; }

        public IReadOnlyList<ParameterDefinition> Definitions => _definitions;

        public ParameterSchema(string schemaId, IEnumerable<ParameterDefinition> definitions)
        {
            if (string.IsNullOrWhiteSpace(schemaId)) throw new ArgumentException("Üres séma-azonosító.", nameof(schemaId));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            SchemaId = schemaId;
            _definitions = new List<ParameterDefinition>(definitions);
            foreach (var d in _definitions)
            {
                if (d == null) throw new ArgumentException("Null paraméterdefiníció.", nameof(definitions));
                if (_byKey.ContainsKey(d.Key)) throw new ArgumentException("Duplikált paraméterkulcs: " + d.Key, nameof(definitions));
                _byKey.Add(d.Key, d);
            }
        }

        public bool TryGet(string key, [NotNullWhen(true)] out ParameterDefinition? definition)
        {
            definition = null;
            return key != null && _byKey.TryGetValue(key, out definition);
        }

        public ParameterSet CreateDefaults()
        {
            var set = new ParameterSet();
            foreach (var d in _definitions) set.Set(d.Key, d.DefaultValue);
            return set;
        }

        public IReadOnlyList<ValidationIssue> Validate(ParameterSet values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var issues = new List<ValidationIssue>();
            foreach (var d in _definitions)
            {
                if (!values.TryGet(d.Key, out var v))
                {
                    issues.Add(new ValidationIssue(d.Key, WorldSetupErrorCode.ParameterMissing));
                    continue;
                }
                var code = d.Check(v);
                if (code.HasValue) issues.Add(new ValidationIssue(d.Key, code.Value));
            }
            foreach (string key in values.Keys)
                if (!_byKey.ContainsKey(key)) issues.Add(new ValidationIssue(key, WorldSetupErrorCode.UnknownParameter));
            return issues;
        }
    }
}
