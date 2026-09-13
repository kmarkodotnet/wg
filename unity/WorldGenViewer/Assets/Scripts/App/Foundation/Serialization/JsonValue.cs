#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace WorldGen.App.Serialization
{
    public enum JsonValueKind
    {
        Null,
        Boolean,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// JSON-érték (ND-106). A szám az eredeti szöveges alakjában tárolódik,
    /// így egy 64 bites egész (seed) veszteség nélkül olvasható vissza
    /// <see cref="TryGetUInt64"/>-gyel. Az objektum megőrzi a tagok sorrendjét,
    /// és egy kulcs legfeljebb egyszer szerepelhet.
    /// A tömb és az objektum módosítható; a null és a logikai értékek megosztott példányok.
    /// </summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonValueKind.Null, null, false);
        public static readonly JsonValue True = new JsonValue(JsonValueKind.Boolean, null, true);
        public static readonly JsonValue False = new JsonValue(JsonValueKind.Boolean, null, false);

        private readonly string? _text;
        private readonly bool _boolean;
        private readonly List<JsonValue>? _items;
        private readonly List<KeyValuePair<string, JsonValue>>? _members;

        public JsonValueKind Kind { get; }

        private JsonValue(JsonValueKind kind, string? text, bool boolean)
        {
            Kind = kind;
            _text = text;
            _boolean = boolean;
            if (kind == JsonValueKind.Array) _items = new List<JsonValue>();
            if (kind == JsonValueKind.Object) _members = new List<KeyValuePair<string, JsonValue>>();
        }

        public static JsonValue FromBoolean(bool value) => value ? True : False;

        public static JsonValue FromString(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new JsonValue(JsonValueKind.String, value, false);
        }

        public static JsonValue FromNumber(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentException("JSON-ban nem ábrázolható szám: " + value, nameof(value));
            return new JsonValue(JsonValueKind.Number, value.ToString("R", CultureInfo.InvariantCulture), false);
        }

        public static JsonValue FromNumber(int value) => new JsonValue(JsonValueKind.Number, value.ToString(CultureInfo.InvariantCulture), false);

        public static JsonValue FromNumber(long value) => new JsonValue(JsonValueKind.Number, value.ToString(CultureInfo.InvariantCulture), false);

        public static JsonValue FromNumber(ulong value) => new JsonValue(JsonValueKind.Number, value.ToString(CultureInfo.InvariantCulture), false);

        /// <summary>A parser által már szintaktikailag ellenőrzött számszöveg.</summary>
        internal static JsonValue FromValidatedNumberText(string text) => new JsonValue(JsonValueKind.Number, text, false);

        public static JsonValue CreateArray() => new JsonValue(JsonValueKind.Array, null, false);

        public static JsonValue CreateObject() => new JsonValue(JsonValueKind.Object, null, false);

        public bool IsNull => Kind == JsonValueKind.Null;

        public bool AsBoolean()
        {
            Require(JsonValueKind.Boolean);
            return _boolean;
        }

        public string AsString()
        {
            Require(JsonValueKind.String);
            return _text!;
        }

        /// <summary>A szám eredeti szöveges alakja.</summary>
        public string NumberText
        {
            get
            {
                Require(JsonValueKind.Number);
                return _text!;
            }
        }

        public bool TryGetBoolean(out bool value)
        {
            value = _boolean;
            return Kind == JsonValueKind.Boolean;
        }

        public bool TryGetString([NotNullWhen(true)] out string? value)
        {
            value = Kind == JsonValueKind.String ? _text : null;
            return value != null;
        }

        /// <summary>Véges double; túlcsorduló (pl. 1e400) számra hamis.</summary>
        public bool TryGetDouble(out double value)
        {
            value = 0;
            if (Kind != JsonValueKind.Number) return false;
            try
            {
                if (!double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            }
            catch (OverflowException)
            {
                return false;
            }
            if (double.IsFinite(value)) return true;
            value = 0;
            return false;
        }

        /// <summary>Csak tört- és kitevőrész nélküli, tartományon belüli egész.</summary>
        public bool TryGetInt64(out long value)
        {
            value = 0;
            return Kind == JsonValueKind.Number && IsIntegerText(_text!)
                && long.TryParse(_text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        public bool TryGetUInt64(out ulong value)
        {
            value = 0;
            return Kind == JsonValueKind.Number && IsIntegerText(_text!) && _text![0] != '-'
                && ulong.TryParse(_text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        public bool TryGetInt32(out int value)
        {
            value = 0;
            return Kind == JsonValueKind.Number && IsIntegerText(_text!)
                && int.TryParse(_text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        // ---- tömb ----

        public int Count
        {
            get
            {
                if (_items != null) return _items.Count;
                if (_members != null) return _members.Count;
                throw new InvalidOperationException("A Count csak tömbön vagy objektumon értelmezett, ez: " + Kind);
            }
        }

        public IReadOnlyList<JsonValue> Items
        {
            get
            {
                Require(JsonValueKind.Array);
                return _items!;
            }
        }

        public JsonValue this[int index]
        {
            get
            {
                Require(JsonValueKind.Array);
                return _items![index];
            }
        }

        public JsonValue Add(JsonValue item)
        {
            Require(JsonValueKind.Array);
            _items!.Add(item ?? throw new ArgumentNullException(nameof(item)));
            return this;
        }

        // ---- objektum ----

        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members
        {
            get
            {
                Require(JsonValueKind.Object);
                return _members!;
            }
        }

        public bool ContainsKey(string name) => IndexOf(name) >= 0;

        public bool TryGetMember(string name, [NotNullWhen(true)] out JsonValue? value)
        {
            int i = IndexOf(name);
            value = i >= 0 ? _members![i].Value : null;
            return value != null;
        }

        /// <summary>A tag értéke, vagy null, ha nincs ilyen kulcs.</summary>
        public JsonValue? GetMember(string name) => TryGetMember(name, out var v) ? v : null;

        /// <summary>Beállít vagy lecserél egy tagot (a meglévő tag pozíciója megmarad).</summary>
        public JsonValue Set(string name, JsonValue value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (value == null) throw new ArgumentNullException(nameof(value));
            int i = IndexOf(name);
            var pair = new KeyValuePair<string, JsonValue>(name, value);
            if (i >= 0) _members![i] = pair;
            else _members!.Add(pair);
            return this;
        }

        public JsonValue Set(string name, string value) => Set(name, FromString(value));

        public JsonValue Set(string name, bool value) => Set(name, FromBoolean(value));

        public JsonValue Set(string name, double value) => Set(name, FromNumber(value));

        public JsonValue Set(string name, int value) => Set(name, FromNumber(value));

        public JsonValue Set(string name, long value) => Set(name, FromNumber(value));

        public bool Remove(string name)
        {
            int i = IndexOf(name);
            if (i < 0) return false;
            _members!.RemoveAt(i);
            return true;
        }

        private int IndexOf(string name)
        {
            Require(JsonValueKind.Object);
            if (name == null) throw new ArgumentNullException(nameof(name));
            for (int i = 0; i < _members!.Count; i++)
                if (string.Equals(_members[i].Key, name, StringComparison.Ordinal)) return i;
            return -1;
        }

        private void Require(JsonValueKind kind)
        {
            if (Kind != kind) throw new InvalidOperationException("JSON-típuseltérés: várt " + kind + ", tényleges " + Kind);
        }

        private static bool IsIntegerText(string text)
        {
            foreach (char ch in text)
                if (ch == '.' || ch == 'e' || ch == 'E') return false;
            return true;
        }

        public override string ToString() => JsonWriter.Write(this, indented: false);
    }
}
