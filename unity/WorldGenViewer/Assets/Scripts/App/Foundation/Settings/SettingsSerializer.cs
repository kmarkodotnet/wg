#nullable enable
using System;
using System.Collections.Generic;
using WorldGen.App.Serialization;

namespace WorldGen.App.Settings
{
    public enum SettingsIssueKind
    {
        /// <summary>Hiányzó mező vagy kategória; az alapérték lép életbe.</summary>
        Missing,

        /// <summary>Rossz típusú vagy értelmezhetetlen érték; az alapérték lép életbe.</summary>
        Invalid,

        /// <summary>Tartományon kívüli érték; a legközelebbi érvényesre igazítva.</summary>
        OutOfRange,

        /// <summary>A teljes fájl olvashatatlan; minden alapértéken.</summary>
        Corrupt,

        /// <summary>Régebbi verzió, amihez nincs (vagy hibás) migráció.</summary>
        Migration,

        /// <summary>A fájl egy újabb alkalmazásverzióból származik.</summary>
        NewerVersion,
    }

    public sealed class SettingsIssue
    {
        public SettingsIssueKind Kind { get; }

        /// <summary>Pontozott út, pl. "graphics.fpsLimit"; "$" a teljes dokumentum.</summary>
        public string Path { get; }

        public string Message { get; }

        public SettingsIssue(SettingsIssueKind kind, string path, string message)
        {
            Kind = kind;
            Path = path ?? "";
            Message = message ?? "";
        }

        public override string ToString() => Kind + " " + Path + ": " + Message;
    }

    public sealed class SettingsReadResult
    {
        public AppSettings Settings { get; }
        public IReadOnlyList<SettingsIssue> Issues { get; }

        /// <summary>A fájlban talált verzió; 0, ha nem volt értelmezhető.</summary>
        public int FileVersion { get; }

        public bool IsCorrupt { get; }
        public bool IsFromNewerVersion { get; }

        public SettingsReadResult(AppSettings settings, IReadOnlyList<SettingsIssue> issues, int fileVersion, bool isCorrupt, bool isFromNewerVersion)
        {
            Settings = settings;
            Issues = issues;
            FileVersion = fileVersion;
            IsCorrupt = isCorrupt;
            IsFromNewerVersion = isFromNewerVersion;
        }
    }

    /// <summary>Settings-schema migrációk, lépésenként v → v+1, JSON-objektumon.</summary>
    public sealed class SettingsMigrations
    {
        private readonly Dictionary<int, Func<JsonValue, JsonValue>> _steps = new Dictionary<int, Func<JsonValue, JsonValue>>();

        public void Register(int fromVersion, Func<JsonValue, JsonValue> step)
        {
            if (fromVersion < 1) throw new ArgumentOutOfRangeException(nameof(fromVersion));
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (_steps.ContainsKey(fromVersion)) throw new InvalidOperationException("Már regisztrált migráció: v" + fromVersion);
            _steps.Add(fromVersion, step);
        }

        public bool CanMigrate(int fromVersion, int toVersion)
        {
            for (int v = fromVersion; v < toVersion; v++)
                if (!_steps.ContainsKey(v)) return false;
            return true;
        }

        public JsonValue Apply(JsonValue document, int fromVersion, int toVersion)
        {
            var doc = document;
            for (int v = fromVersion; v < toVersion; v++)
            {
                if (!_steps.TryGetValue(v, out var step)) throw new InvalidOperationException("Hiányzó migráció: v" + v);
                doc = step(doc) ?? throw new InvalidOperationException("A v" + v + " migráció null dokumentumot adott.");
                if (doc.Kind != JsonValueKind.Object) throw new InvalidOperationException("A v" + v + " migráció nem objektumot adott.");
            }
            return doc;
        }
    }

    /// <summary>
    /// Verziózott, mezőszinten tűrő settings-szerializálás (WF-SET-001).
    /// Olvasáskor semmilyen bemenet nem dob kivételt: a hibás rész az
    /// alapértékre esik vissza, és <see cref="SettingsIssue"/>-t kap.
    /// </summary>
    public static class SettingsSerializer
    {
        public const int CurrentVersion = 1;

        public static string Serialize(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var copy = settings.Clone();
            copy.Normalize();
            return JsonWriter.Write(ToJson(copy)) + "\n";
        }

        public static JsonValue ToJson(AppSettings settings)
        {
            return JsonValue.CreateObject()
                .Set("version", CurrentVersion)
                .Set("graphics", GraphicsToJson(settings.Graphics))
                .Set("audio", AudioToJson(settings.Audio))
                .Set("controls", ControlsToJson(settings.Controls))
                .Set("simulation", SimulationToJson(settings.Simulation))
                .Set("interface", InterfaceToJson(settings.Interface));
        }

        public static SettingsReadResult Deserialize(string text, SettingsMigrations? migrations = null)
        {
            var issues = new List<SettingsIssue>();
            JsonValue root;
            try
            {
                root = JsonParser.Parse(text ?? "");
            }
            catch (JsonFormatException ex)
            {
                return Corrupt(ex.Message, 0);
            }
            if (root.Kind != JsonValueKind.Object) return Corrupt("A gyökér nem JSON-objektum.", 0);

            int version;
            var versionNode = root.GetMember("version");
            if (versionNode == null)
            {
                issues.Add(new SettingsIssue(SettingsIssueKind.Missing, "version", "Hiányzó verzió; az aktuálisként olvasva."));
                version = CurrentVersion;
            }
            else if (!versionNode.TryGetInt32(out version) || version < 1)
            {
                return Corrupt("Érvénytelen verzió: " + versionNode, 0);
            }

            bool newer = version > CurrentVersion;
            if (newer)
            {
                issues.Add(new SettingsIssue(SettingsIssueKind.NewerVersion, "version",
                    "A fájl újabb verziójú (v" + version + "); az ismert mezők olvasva."));
            }
            else if (version < CurrentVersion)
            {
                if (migrations != null && migrations.CanMigrate(version, CurrentVersion))
                {
                    try
                    {
                        root = migrations.Apply(root, version, CurrentVersion);
                    }
                    catch (Exception ex)
                    {
                        issues.Add(new SettingsIssue(SettingsIssueKind.Migration, "$", "Migráció sikertelen: " + ex.Message));
                    }
                }
                else
                {
                    issues.Add(new SettingsIssue(SettingsIssueKind.Migration, "$",
                        "Nincs migráció v" + version + " → v" + CurrentVersion + "; tűrő olvasás."));
                }
            }

            var s = new AppSettings();
            var r = new FieldReader(issues);

            var g = r.Category(root, "graphics");
            if (g != null)
            {
                r.Enum<DisplayMode>(g, "graphics", "displayMode", v => s.Graphics.DisplayMode = v);
                r.Resolution(g, "graphics", "resolution", v => s.Graphics.Resolution = v);
                r.Bool(g, "graphics", "vSync", v => s.Graphics.VSync = v);
                r.Int(g, "graphics", "fpsLimit", v => s.Graphics.FpsLimit = v);
                r.Int(g, "graphics", "backgroundFpsLimit", v => s.Graphics.BackgroundFpsLimit = v);
                r.Enum<GraphicsQuality>(g, "graphics", "quality", v => s.Graphics.Quality = v);
            }

            var a = r.Category(root, "audio");
            if (a != null)
            {
                r.Double(a, "audio", "master", v => s.Audio.Master = v);
                r.Double(a, "audio", "music", v => s.Audio.Music = v);
                r.Double(a, "audio", "ambient", v => s.Audio.Ambient = v);
                r.Double(a, "audio", "effects", v => s.Audio.Effects = v);
                r.Double(a, "audio", "ui", v => s.Audio.Ui = v);
                r.Bool(a, "audio", "muted", v => s.Audio.Muted = v);
            }

            var c = r.Category(root, "controls");
            if (c != null)
            {
                r.Double(c, "controls", "mouseSensitivity", v => s.Controls.MouseSensitivity = v);
                r.Double(c, "controls", "zoomSensitivity", v => s.Controls.ZoomSensitivity = v);
                r.Double(c, "controls", "cameraSpeed", v => s.Controls.CameraSpeed = v);
                r.Bool(c, "controls", "invertOrbitY", v => s.Controls.InvertOrbitY = v);
            }

            var m = r.Category(root, "simulation");
            if (m != null)
            {
                r.Enum<SimulationQuality>(m, "simulation", "quality", v => s.Simulation.Quality = v);
                r.Int(m, "simulation", "autosaveIntervalMinutes", v => s.Simulation.AutosaveIntervalMinutes = v);
                r.Bool(m, "simulation", "autosaveBeforeDeepTimeJump", v => s.Simulation.AutosaveBeforeDeepTimeJump = v);
                r.Int(m, "simulation", "maxAutosavesPerWorld", v => s.Simulation.MaxAutosavesPerWorld = v);
                r.Bool(m, "simulation", "pauseWhenUnfocused", v => s.Simulation.PauseWhenUnfocused = v);
            }

            var i = r.Category(root, "interface");
            if (i != null)
            {
                r.String(i, "interface", "language", v => s.Interface.Language = v);
                r.Double(i, "interface", "uiScale", v => s.Interface.UiScale = v);
                r.Double(i, "interface", "tooltipDelaySeconds", v => s.Interface.TooltipDelaySeconds = v);
                r.Bool(i, "interface", "showConfirmations", v => s.Interface.ShowConfirmations = v);
                r.Bool(i, "interface", "showWelcomeOnStartup", v => s.Interface.ShowWelcomeOnStartup = v);
                r.Bool(i, "interface", "showDebugOverlay", v => s.Interface.ShowDebugOverlay = v);
                r.Bool(i, "interface", "reduceMotion", v => s.Interface.ReduceMotion = v);
            }

            // A tartományon kívüli értékeket a Normalize igazítja; a különbség útvonalanként jelentve.
            var raw = ToJson(s);
            s.Normalize();
            ReportNormalization(raw, ToJson(s), "", issues);

            return new SettingsReadResult(s, issues, version, isCorrupt: false, isFromNewerVersion: newer);
        }

        internal static JsonValue GraphicsToJson(GraphicsSettings g)
        {
            var resolution = g.Resolution.IsSpecified
                ? JsonValue.CreateObject()
                    .Set("width", g.Resolution.Width)
                    .Set("height", g.Resolution.Height)
                    .Set("refreshRateHz", SafeNumber(g.Resolution.RefreshRateHz))
                : JsonValue.Null;
            return JsonValue.CreateObject()
                .Set("displayMode", g.DisplayMode.ToString())
                .Set("resolution", resolution)
                .Set("vSync", g.VSync)
                .Set("fpsLimit", g.FpsLimit)
                .Set("backgroundFpsLimit", g.BackgroundFpsLimit)
                .Set("quality", g.Quality.ToString());
        }

        internal static JsonValue AudioToJson(AudioSettings a)
        {
            return JsonValue.CreateObject()
                .Set("master", SafeNumber(a.Master))
                .Set("music", SafeNumber(a.Music))
                .Set("ambient", SafeNumber(a.Ambient))
                .Set("effects", SafeNumber(a.Effects))
                .Set("ui", SafeNumber(a.Ui))
                .Set("muted", a.Muted);
        }

        internal static JsonValue ControlsToJson(ControlSettings c)
        {
            return JsonValue.CreateObject()
                .Set("mouseSensitivity", SafeNumber(c.MouseSensitivity))
                .Set("zoomSensitivity", SafeNumber(c.ZoomSensitivity))
                .Set("cameraSpeed", SafeNumber(c.CameraSpeed))
                .Set("invertOrbitY", c.InvertOrbitY);
        }

        internal static JsonValue SimulationToJson(SimulationSettings m)
        {
            return JsonValue.CreateObject()
                .Set("quality", m.Quality.ToString())
                .Set("autosaveIntervalMinutes", m.AutosaveIntervalMinutes)
                .Set("autosaveBeforeDeepTimeJump", m.AutosaveBeforeDeepTimeJump)
                .Set("maxAutosavesPerWorld", m.MaxAutosavesPerWorld)
                .Set("pauseWhenUnfocused", m.PauseWhenUnfocused);
        }

        internal static JsonValue InterfaceToJson(InterfaceSettings i)
        {
            return JsonValue.CreateObject()
                .Set("language", i.Language ?? "")
                .Set("uiScale", SafeNumber(i.UiScale))
                .Set("tooltipDelaySeconds", SafeNumber(i.TooltipDelaySeconds))
                .Set("showConfirmations", i.ShowConfirmations)
                .Set("showWelcomeOnStartup", i.ShowWelcomeOnStartup)
                .Set("showDebugOverlay", i.ShowDebugOverlay)
                .Set("reduceMotion", i.ReduceMotion);
        }

        /// <summary>Nem-véges érték (kódból beállított NaN) null-ként kerül a JSON-ba, nem kivételként.</summary>
        private static JsonValue SafeNumber(double value) => double.IsFinite(value) ? JsonValue.FromNumber(value) : JsonValue.Null;

        private static SettingsReadResult Corrupt(string message, int version)
        {
            var issues = new List<SettingsIssue> { new SettingsIssue(SettingsIssueKind.Corrupt, "$", message) };
            return new SettingsReadResult(new AppSettings(), issues, version, isCorrupt: true, isFromNewerVersion: false);
        }

        private static void ReportNormalization(JsonValue raw, JsonValue normalized, string path, List<SettingsIssue> issues)
        {
            if (raw.Kind == JsonValueKind.Object && normalized.Kind == JsonValueKind.Object)
            {
                foreach (var member in raw.Members)
                {
                    if (member.Key == "version") continue;
                    var other = normalized.GetMember(member.Key);
                    string childPath = path.Length == 0 ? member.Key : path + "." + member.Key;
                    if (other == null) continue;
                    ReportNormalization(member.Value, other, childPath, issues);
                }
                return;
            }
            string before = raw.ToString();
            string after = normalized.ToString();
            if (before != after)
                issues.Add(new SettingsIssue(SettingsIssueKind.OutOfRange, path, before + " → " + after));
        }

        private sealed class FieldReader
        {
            private readonly List<SettingsIssue> _issues;

            public FieldReader(List<SettingsIssue> issues)
            {
                _issues = issues;
            }

            public JsonValue? Category(JsonValue root, string name)
            {
                var node = root.GetMember(name);
                if (node == null)
                {
                    _issues.Add(new SettingsIssue(SettingsIssueKind.Missing, name, "Hiányzó kategória; alapértékek."));
                    return null;
                }
                if (node.Kind != JsonValueKind.Object)
                {
                    _issues.Add(new SettingsIssue(SettingsIssueKind.Invalid, name, "A kategória nem objektum; alapértékek."));
                    return null;
                }
                return node;
            }

            public void Bool(JsonValue obj, string category, string name, Action<bool> set)
            {
                var node = Member(obj, category, name);
                if (node == null) return;
                if (node.TryGetBoolean(out bool v)) set(v);
                else Invalid(category, name, node);
            }

            public void Int(JsonValue obj, string category, string name, Action<int> set)
            {
                var node = Member(obj, category, name);
                if (node == null) return;
                if (node.TryGetInt32(out int v)) set(v);
                else Invalid(category, name, node);
            }

            public void Double(JsonValue obj, string category, string name, Action<double> set)
            {
                var node = Member(obj, category, name);
                if (node == null) return;
                if (node.TryGetDouble(out double v)) set(v);
                else Invalid(category, name, node);
            }

            public void String(JsonValue obj, string category, string name, Action<string> set)
            {
                var node = Member(obj, category, name);
                if (node == null) return;
                if (node.TryGetString(out string? v)) set(v);
                else Invalid(category, name, node);
            }

            public void Enum<TEnum>(JsonValue obj, string category, string name, Action<TEnum> set) where TEnum : struct, Enum
            {
                var node = Member(obj, category, name);
                if (node == null) return;
                if (node.TryGetString(out string? text) && text.Length > 0 && char.IsLetter(text[0])
                    && System.Enum.TryParse(text, false, out TEnum value) && System.Enum.IsDefined(typeof(TEnum), value))
                    set(value);
                else
                    Invalid(category, name, node);
            }

            public void Resolution(JsonValue obj, string category, string name, Action<ScreenResolution> set)
            {
                var node = Member(obj, category, name);
                if (node == null) return;
                if (node.IsNull)
                {
                    set(ScreenResolution.Unspecified);
                    return;
                }
                if (node.Kind == JsonValueKind.Object
                    && node.GetMember("width") is JsonValue w && w.TryGetInt32(out int width)
                    && node.GetMember("height") is JsonValue h && h.TryGetInt32(out int height))
                {
                    double refresh = 0;
                    var rr = node.GetMember("refreshRateHz");
                    if (rr != null && !rr.TryGetDouble(out refresh))
                    {
                        Invalid(category, name + ".refreshRateHz", rr);
                        refresh = 0;
                    }
                    var resolution = new ScreenResolution(width, height, refresh);
                    if (!resolution.IsSpecified)
                    {
                        // A normalizálási diff ezt nem látná (a nem megadott felbontás JSON-ja már null), ezért itt jelentjük.
                        _issues.Add(new SettingsIssue(SettingsIssueKind.OutOfRange, category + "." + name,
                            node + " → null (nem pozitív méret)"));
                    }
                    set(resolution);
                    return;
                }
                Invalid(category, name, node);
            }

            private JsonValue? Member(JsonValue obj, string category, string name)
            {
                var node = obj.GetMember(name);
                if (node == null) _issues.Add(new SettingsIssue(SettingsIssueKind.Missing, category + "." + name, "Hiányzó mező; alapérték."));
                return node;
            }

            private void Invalid(string category, string name, JsonValue node)
            {
                _issues.Add(new SettingsIssue(SettingsIssueKind.Invalid, category + "." + name, "Érvénytelen érték: " + node + "; alapérték."));
            }
        }
    }
}
