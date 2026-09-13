#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using WorldGen.App.Serialization;

namespace WorldGen.App.Localization
{
    /// <summary>
    /// Kulcs → szöveg tábla nyelvenként, fallback nyelvvel (WF-UI-007).
    /// Hiányzó kulcsnál "[kulcs]" jelenik meg (a QA-ban azonnal látszik), és a
    /// kulcs a <see cref="MissingKeys"/>-be kerül. A formázás invariáns kultúrájú.
    /// </summary>
    public sealed class LocalizationTable
    {
        private readonly Dictionary<string, Dictionary<string, string>> _languages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);
        private string _currentLanguage;

        public string FallbackLanguage { get; }

        public LocalizationTable(string currentLanguage = "en", string fallbackLanguage = "en")
        {
            if (string.IsNullOrWhiteSpace(fallbackLanguage)) throw new ArgumentException("Üres fallback nyelv.", nameof(fallbackLanguage));
            FallbackLanguage = fallbackLanguage;
            _currentLanguage = string.IsNullOrWhiteSpace(currentLanguage) ? fallbackLanguage : currentLanguage;
        }

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set => _currentLanguage = string.IsNullOrWhiteSpace(value) ? FallbackLanguage : value;
        }

        public IReadOnlyCollection<string> Languages => _languages.Keys;

        public IReadOnlyCollection<string> MissingKeys => _missing;

        public void Set(string language, string key, string text)
        {
            if (string.IsNullOrWhiteSpace(language)) throw new ArgumentException("Üres nyelv.", nameof(language));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Üres kulcs.", nameof(key));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (!_languages.TryGetValue(language, out var table))
            {
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                _languages.Add(language, table);
            }
            table[key] = text;
        }

        public void SetRange(string language, IEnumerable<KeyValuePair<string, string>> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            foreach (var e in entries) Set(language, e.Key, e.Value);
        }

        /// <summary>
        /// Egy nyelv betöltése JSON-objektumból. Az egymásba ágyazott objektumok
        /// pontozott kulcsokká lapulnak ("menu": {"quit": "Quit"} → "menu.quit").
        /// A nem szöveges értékek kimaradnak; az útjuk a visszatérési listában.
        /// </summary>
        public IReadOnlyList<string> LoadLanguage(string language, JsonValue document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var rejected = new List<string>();
            if (document.Kind != JsonValueKind.Object)
            {
                rejected.Add("$");
                return rejected;
            }
            Flatten(language, document, "", rejected);
            return rejected;
        }

        public bool TryGet(string key, out string text)
        {
            if (key != null)
            {
                if (_languages.TryGetValue(_currentLanguage, out var current) && current.TryGetValue(key, out var found))
                {
                    text = found;
                    return true;
                }
                if (_languages.TryGetValue(FallbackLanguage, out var fallback) && fallback.TryGetValue(key, out found))
                {
                    text = found;
                    return true;
                }
            }
            text = "";
            return false;
        }

        public string Get(string key)
        {
            if (TryGet(key, out string text)) return text;
            if (key != null) _missing.Add(key);
            return "[" + key + "]";
        }

        /// <summary><c>string.Format</c> invariáns kultúrával; hibás sablonnál a nyers sablon.</summary>
        public string Format(string key, params object[] args)
        {
            string template = Get(key);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        /// <summary>A fallback nyelv azon kulcsai, amelyek az adott nyelvből hiányoznak (fordítási QA).</summary>
        public IReadOnlyList<string> FindKeysMissingIn(string language)
        {
            var result = new List<string>();
            if (!_languages.TryGetValue(FallbackLanguage, out var fallback)) return result;
            _languages.TryGetValue(language, out var target);
            foreach (string key in fallback.Keys)
                if (target == null || !target.ContainsKey(key)) result.Add(key);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private void Flatten(string language, JsonValue obj, string prefix, List<string> rejected)
        {
            foreach (var member in obj.Members)
            {
                string key = prefix.Length == 0 ? member.Key : prefix + "." + member.Key;
                if (member.Value.Kind == JsonValueKind.Object) Flatten(language, member.Value, key, rejected);
                else if (member.Value.TryGetString(out string? text) && key.Length > 0) Set(language, key, text);
                else rejected.Add(key);
            }
        }
    }
}
