#nullable enable
using System;
using System.Collections.Generic;
using WorldGen.App.Serialization;

namespace WorldGen.App.Controls
{
    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
        Alt = 4,
    }

    /// <summary>
    /// Billentyű + módosítók, szöveges alakban "Ctrl+Shift+F12". A billentyűnév a
    /// Unity <c>KeyCode</c> neve (pl. "F12", "Escape", "Alpha1"); a leképezés a Unity-kötésé.
    /// </summary>
    public readonly struct KeyChord : IEquatable<KeyChord>
    {
        private readonly string? _key;

        public KeyChord(string key, KeyModifiers modifiers = KeyModifiers.None)
        {
            if (!IsValidKeyName(key)) throw new ArgumentException("Érvénytelen billentyűnév: '" + key + "'", nameof(key));
            _key = key;
            Modifiers = modifiers;
        }

        public string Key => _key ?? "";
        public KeyModifiers Modifiers { get; }
        public bool IsEmpty => string.IsNullOrEmpty(_key);

        public static bool TryParse(string? text, out KeyChord chord)
        {
            chord = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text!.Split('+');
            var modifiers = KeyModifiers.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                KeyModifiers m;
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": m = KeyModifiers.Control; break;
                    case "shift": m = KeyModifiers.Shift; break;
                    case "alt": m = KeyModifiers.Alt; break;
                    default: return false;
                }
                if ((modifiers & m) != 0) return false;
                modifiers |= m;
            }
            string key = parts[parts.Length - 1].Trim();
            if (!IsValidKeyName(key)) return false;
            chord = new KeyChord(key, modifiers);
            return true;
        }

        public static bool IsValidKeyName(string? key)
        {
            if (string.IsNullOrEmpty(key) || key!.Length > 32) return false;
            foreach (char ch in key)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9');
                if (!ok) return false;
            }
            string lower = key.ToLowerInvariant();
            return lower != "ctrl" && lower != "control" && lower != "shift" && lower != "alt";
        }

        public bool Equals(KeyChord other)
            => Modifiers == other.Modifiers && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is KeyChord c && Equals(c);

        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Key) * 31 + (int)Modifiers;

        public override string ToString()
        {
            if (IsEmpty) return "";
            string s = "";
            if ((Modifiers & KeyModifiers.Control) != 0) s += "Ctrl+";
            if ((Modifiers & KeyModifiers.Shift) != 0) s += "Shift+";
            if ((Modifiers & KeyModifiers.Alt) != 0) s += "Alt+";
            return s + Key;
        }

        public static bool operator ==(KeyChord left, KeyChord right) => left.Equals(right);

        public static bool operator !=(KeyChord left, KeyChord right) => !left.Equals(right);
    }

    public static class InputActionIds
    {
        public const string Back = "back";
        public const string Screenshot = "screenshot";
        public const string CleanScreenshot = "screenshot-clean";
        public const string QuickSave = "quick-save";
        public const string QuickLoad = "quick-load";
        public const string Help = "help";
        public const string ToggleDebugOverlay = "toggle-debug-overlay";
    }

    public sealed class InputActionDefinition
    {
        public InputActionDefinition(string id, string labelKey, KeyChord defaultChord, bool isRebindable = true)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Üres akcióazonosító.", nameof(id));
            if (defaultChord.IsEmpty) throw new ArgumentException("Hiányzó alapértelmezett billentyű: " + id, nameof(defaultChord));
            Id = id;
            LabelKey = labelKey ?? "";
            DefaultChord = defaultChord;
            IsRebindable = isRebindable;
        }

        public string Id { get; }
        public string LabelKey { get; }
        public KeyChord DefaultChord { get; }
        public bool IsRebindable { get; }
    }

    /// <summary>
    /// Billentyűkiosztás (WF-SET-005 előkészítése): alapértelmezések, ütközésmentes
    /// átállítás, JSON-perzisztálás. A kamera egérvezérlése (orbit, zoom) nem
    /// kiosztható akció, azt a viewer kezeli. A teljes rebinding UI a 0.1.0 után jön.
    /// </summary>
    public sealed class KeyBindingMap
    {
        private readonly List<InputActionDefinition> _actions;
        private readonly Dictionary<string, KeyChord> _bindings = new Dictionary<string, KeyChord>(StringComparer.Ordinal);

        public static IReadOnlyList<InputActionDefinition> CreateDefaultActions() => new[]
        {
            new InputActionDefinition(InputActionIds.Back, "input.back", new KeyChord("Escape"), isRebindable: false),
            new InputActionDefinition(InputActionIds.Screenshot, "input.screenshot", new KeyChord("F12")),
            new InputActionDefinition(InputActionIds.CleanScreenshot, "input.screenshotClean", new KeyChord("F12", KeyModifiers.Shift)),
            new InputActionDefinition(InputActionIds.QuickSave, "input.quickSave", new KeyChord("F5")),
            new InputActionDefinition(InputActionIds.QuickLoad, "input.quickLoad", new KeyChord("F9")),
            new InputActionDefinition(InputActionIds.Help, "input.help", new KeyChord("F1")),
            new InputActionDefinition(InputActionIds.ToggleDebugOverlay, "input.toggleDebugOverlay", new KeyChord("F3")),
        };

        public KeyBindingMap()
            : this(CreateDefaultActions())
        {
        }

        public KeyBindingMap(IEnumerable<InputActionDefinition> actions)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            _actions = new List<InputActionDefinition>(actions);
            var chords = new HashSet<KeyChord>();
            foreach (var a in _actions)
            {
                if (a == null) throw new ArgumentException("Null akció.", nameof(actions));
                if (_bindings.ContainsKey(a.Id)) throw new ArgumentException("Duplikált akció: " + a.Id, nameof(actions));
                if (!chords.Add(a.DefaultChord)) throw new ArgumentException("Ütköző alapértelmezett billentyű: " + a.DefaultChord, nameof(actions));
                _bindings.Add(a.Id, a.DefaultChord);
            }
        }

        public IReadOnlyList<InputActionDefinition> Actions => _actions;

        public KeyChord GetBinding(string actionId)
            => _bindings.TryGetValue(actionId, out var chord) ? chord : throw new ArgumentException("Ismeretlen akció: " + actionId, nameof(actionId));

        public string? FindAction(KeyChord chord)
        {
            foreach (var a in _actions)
                if (_bindings[a.Id] == chord) return a.Id;
            return null;
        }

        /// <summary>Hamis, ha az akció nem kiosztható vagy a billentyű már foglalt (ekkor a foglaló akció azonosítója is visszajön).</summary>
        public bool TrySetBinding(string actionId, KeyChord chord, out string? conflictingActionId)
        {
            conflictingActionId = null;
            var action = Find(actionId) ?? throw new ArgumentException("Ismeretlen akció: " + actionId, nameof(actionId));
            if (!action.IsRebindable || chord.IsEmpty) return false;
            string? owner = FindAction(chord);
            if (owner != null && owner != actionId)
            {
                conflictingActionId = owner;
                return false;
            }
            _bindings[actionId] = chord;
            return true;
        }

        public void RestoreDefaults()
        {
            foreach (var a in _actions) _bindings[a.Id] = a.DefaultChord;
        }

        /// <summary>Csak az átállítható akciók: <c>{ "screenshot": "F12", … }</c>.</summary>
        public JsonValue ToJson()
        {
            var obj = JsonValue.CreateObject();
            foreach (var a in _actions)
                if (a.IsRebindable) obj.Set(a.Id, _bindings[a.Id].ToString());
            return obj;
        }

        /// <summary>
        /// Betöltés tűrően: ismeretlen, hibás vagy nem kiosztható bejegyzés kimarad.
        /// Ha a betöltött kiosztásban ütközés van, a teljes fájl elvetődik, és
        /// minden alapértékre áll (ne legyen elérhetetlen akció). A problémák útjai a visszatérési listában.
        /// </summary>
        public IReadOnlyList<string> LoadJson(JsonValue document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            RestoreDefaults();
            var issues = new List<string>();
            if (document.Kind != JsonValueKind.Object)
            {
                issues.Add("$");
                return issues;
            }

            var candidate = new Dictionary<string, KeyChord>(_bindings, StringComparer.Ordinal);
            foreach (var member in document.Members)
            {
                var action = Find(member.Key);
                if (action == null || !action.IsRebindable || !member.Value.TryGetString(out string? text) || !KeyChord.TryParse(text, out var chord))
                {
                    issues.Add(member.Key);
                    continue;
                }
                candidate[member.Key] = chord;
            }

            var seen = new HashSet<KeyChord>();
            foreach (var a in _actions)
            {
                if (seen.Add(candidate[a.Id])) continue;
                issues.Add("conflict:" + candidate[a.Id]);
                return issues;
            }
            foreach (var pair in candidate) _bindings[pair.Key] = pair.Value;
            return issues;
        }

        private InputActionDefinition? Find(string actionId)
        {
            foreach (var a in _actions)
                if (string.Equals(a.Id, actionId, StringComparison.Ordinal)) return a;
            return null;
        }
    }
}
