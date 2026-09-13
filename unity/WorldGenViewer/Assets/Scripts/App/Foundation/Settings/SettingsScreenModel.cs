#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using WorldGen.App.Localization;

namespace WorldGen.App.Settings
{
    public enum SettingKind
    {
        Toggle,
        Slider,
        Choice,
    }

    public sealed class SettingChoice
    {
        public SettingChoice(string id, string label, bool isLocalizationKey)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Üres választás-azonosító.", nameof(id));
            Id = id;
            Label = label ?? "";
            IsLocalizationKey = isLocalizationKey;
        }

        public string Id { get; }

        /// <summary>Lokalizációs kulcs, vagy (pl. felbontásnál) kész szöveg.</summary>
        public string Label { get; }

        public bool IsLocalizationKey { get; }

        public string Resolve(LocalizationTable text) => IsLocalizationKey ? text.Get(Label) : Label;
    }

    /// <summary>
    /// Egy beállítás leírása a Settings képernyőhöz (WF-SET-001…006), UI-technológiától
    /// függetlenül (ND-110). A nézet ebből rajzol, és a <see cref="SettingsEditSession.Working"/>
    /// példányt módosítja rajta keresztül.
    /// </summary>
    public sealed class SettingOption
    {
        private readonly Func<AppSettings, bool>? _getToggle;
        private readonly Action<AppSettings, bool>? _setToggle;
        private readonly Func<AppSettings, double>? _getNumber;
        private readonly Action<AppSettings, double>? _setNumber;
        private readonly Func<double, LocalizationTable, string>? _formatNumber;
        private readonly Func<AppSettings, string>? _getChoice;
        private readonly Action<AppSettings, string>? _setChoice;

        private SettingOption(string id, SettingsCategory category, SettingKind kind,
            Func<AppSettings, bool>? getToggle, Action<AppSettings, bool>? setToggle,
            double minimum, double maximum, double step,
            Func<AppSettings, double>? getNumber, Action<AppSettings, double>? setNumber, Func<double, LocalizationTable, string>? formatNumber,
            IReadOnlyList<SettingChoice>? choices, Func<AppSettings, string>? getChoice, Action<AppSettings, string>? setChoice)
        {
            Id = id;
            Category = category;
            Kind = kind;
            _getToggle = getToggle;
            _setToggle = setToggle;
            Minimum = minimum;
            Maximum = maximum;
            Step = step;
            _getNumber = getNumber;
            _setNumber = setNumber;
            _formatNumber = formatNumber;
            Choices = choices ?? Array.Empty<SettingChoice>();
            _getChoice = getChoice;
            _setChoice = setChoice;
        }

        /// <summary>Pl. "audio.music"; a címke kulcsa "settings." + Id.</summary>
        public string Id { get; }

        public string LabelKey => "settings." + Id;
        public SettingsCategory Category { get; }
        public SettingKind Kind { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Step { get; }
        public IReadOnlyList<SettingChoice> Choices { get; }

        internal static SettingOption Toggle(string id, SettingsCategory category, Func<AppSettings, bool> get, Action<AppSettings, bool> set)
            => new SettingOption(id, category, SettingKind.Toggle, get, set, 0, 0, 0, null, null, null, null, null, null);

        internal static SettingOption Slider(string id, SettingsCategory category, double minimum, double maximum, double step,
            Func<AppSettings, double> get, Action<AppSettings, double> set, Func<double, LocalizationTable, string> format)
            => new SettingOption(id, category, SettingKind.Slider, null, null, minimum, maximum, step, get, set, format, null, null, null);

        internal static SettingOption Choice(string id, SettingsCategory category, IReadOnlyList<SettingChoice> choices,
            Func<AppSettings, string> get, Action<AppSettings, string> set)
            => new SettingOption(id, category, SettingKind.Choice, null, null, 0, 0, 0, null, null, null, choices, get, set);

        public bool GetToggle(AppSettings settings) => Require(_getToggle, SettingKind.Toggle)(settings);

        public void SetToggle(AppSettings settings, bool value) => Require(_setToggle, SettingKind.Toggle)(settings, value);

        public double GetNumber(AppSettings settings) => Require(_getNumber, SettingKind.Slider)(settings);

        /// <summary>A lépésközre és a tartományra igazít (a lebegőpontos maradékot 10 tizedesre kerekítve).</summary>
        public void SetNumber(AppSettings settings, double value)
        {
            var set = Require(_setNumber, SettingKind.Slider);
            if (!double.IsFinite(value)) return;
            double clamped = Math.Min(Math.Max(value, Minimum), Maximum);
            double snapped = Minimum + Math.Round((clamped - Minimum) / Step, MidpointRounding.AwayFromZero) * Step;
            set(settings, Math.Round(Math.Min(snapped, Maximum), 10));
        }

        public string GetChoice(AppSettings settings) => Require(_getChoice, SettingKind.Choice)(settings);

        /// <summary>Hamis, ha az azonosító nem szerepel a választások között.</summary>
        public bool SetChoice(AppSettings settings, string choiceId)
        {
            var set = Require(_setChoice, SettingKind.Choice);
            foreach (var c in Choices)
            {
                if (!string.Equals(c.Id, choiceId, StringComparison.Ordinal)) continue;
                set(settings, choiceId);
                return true;
            }
            return false;
        }

        /// <summary>Az aktuális érték megjeleníthető szövege.</summary>
        public string FormatValue(AppSettings settings, LocalizationTable text)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (text == null) throw new ArgumentNullException(nameof(text));
            switch (Kind)
            {
                case SettingKind.Toggle:
                    return text.Get(GetToggle(settings) ? "settings.value.on" : "settings.value.off");
                case SettingKind.Slider:
                    return _formatNumber!(GetNumber(settings), text);
                default:
                    string id = GetChoice(settings);
                    foreach (var c in Choices)
                        if (string.Equals(c.Id, id, StringComparison.Ordinal)) return c.Resolve(text);
                    return id;
            }
        }

        private T Require<T>(T? accessor, SettingKind kind) where T : class
        {
            if (Kind != kind || accessor == null) throw new InvalidOperationException("A '" + Id + "' beállítás típusa " + Kind + ", nem " + kind + ".");
            return accessor;
        }
    }

    /// <summary>A Settings képernyő teljes, kategóriánként rendezett opciólistája.</summary>
    public static class SettingsScreenModel
    {
        public static readonly int[] FpsLimitChoices = { 0, 30, 60, 120, 144, 165, 240, 360 };
        public static readonly int[] BackgroundFpsChoices = { 0, 5, 15, 30, 60 };

        public static IReadOnlyList<SettingOption> Create(IReadOnlyList<ScreenResolution> availableResolutions, IEnumerable<string> languages)
        {
            if (availableResolutions == null) throw new ArgumentNullException(nameof(availableResolutions));
            if (languages == null) throw new ArgumentNullException(nameof(languages));
            const SettingsCategory G = SettingsCategory.Graphics;
            const SettingsCategory A = SettingsCategory.Audio;
            const SettingsCategory C = SettingsCategory.Controls;
            const SettingsCategory S = SettingsCategory.Simulation;
            const SettingsCategory I = SettingsCategory.Interface;

            var resolutionChoices = new List<SettingChoice> { new SettingChoice("native", "settings.value.native", true) };
            foreach (var r in ResolutionCatalog.Normalize(availableResolutions))
                resolutionChoices.Add(new SettingChoice(ResolutionId(r), ResolutionLabel(r), false));

            var languageChoices = new List<SettingChoice>();
            foreach (string lang in languages)
                languageChoices.Add(new SettingChoice(lang, "settings.interface.language." + lang, true));
            if (languageChoices.Count == 0) languageChoices.Add(new SettingChoice("en", "settings.interface.language.en", true));

            return new[]
            {
                SettingOption.Choice("graphics.displayMode", G, EnumChoices<DisplayMode>("settings.graphics.displayMode."),
                    s => s.Graphics.DisplayMode.ToString(), (s, v) => s.Graphics.DisplayMode = ParseEnum<DisplayMode>(v)),
                SettingOption.Choice("graphics.resolution", G, resolutionChoices,
                    s => s.Graphics.Resolution.IsSpecified ? ResolutionId(s.Graphics.Resolution) : "native",
                    (s, v) => s.Graphics.Resolution = v == "native" ? ScreenResolution.Unspecified : FindResolution(availableResolutions, v)),
                SettingOption.Toggle("graphics.vSync", G, s => s.Graphics.VSync, (s, v) => s.Graphics.VSync = v),
                SettingOption.Choice("graphics.fpsLimit", G, IntChoices(FpsLimitChoices, "settings.value.unlimited"),
                    s => s.Graphics.FpsLimit.ToString(CultureInfo.InvariantCulture), (s, v) => s.Graphics.FpsLimit = ParseInt(v)),
                SettingOption.Choice("graphics.backgroundFpsLimit", G, IntChoices(BackgroundFpsChoices, "settings.value.off"),
                    s => s.Graphics.BackgroundFpsLimit.ToString(CultureInfo.InvariantCulture), (s, v) => s.Graphics.BackgroundFpsLimit = ParseInt(v)),
                SettingOption.Choice("graphics.quality", G, EnumChoices<GraphicsQuality>("settings.graphics.quality."),
                    s => s.Graphics.Quality.ToString(), (s, v) => s.Graphics.Quality = ParseEnum<GraphicsQuality>(v)),

                SettingOption.Slider("audio.master", A, 0, 1, 0.05, s => s.Audio.Master, (s, v) => s.Audio.Master = v, Percent),
                SettingOption.Slider("audio.music", A, 0, 1, 0.05, s => s.Audio.Music, (s, v) => s.Audio.Music = v, Percent),
                SettingOption.Slider("audio.ambient", A, 0, 1, 0.05, s => s.Audio.Ambient, (s, v) => s.Audio.Ambient = v, Percent),
                SettingOption.Slider("audio.effects", A, 0, 1, 0.05, s => s.Audio.Effects, (s, v) => s.Audio.Effects = v, Percent),
                SettingOption.Slider("audio.ui", A, 0, 1, 0.05, s => s.Audio.Ui, (s, v) => s.Audio.Ui = v, Percent),
                SettingOption.Toggle("audio.muted", A, s => s.Audio.Muted, (s, v) => s.Audio.Muted = v),

                SettingOption.Slider("controls.mouseSensitivity", C, ControlSettings.MinSensitivity, ControlSettings.MaxSensitivity, 0.1,
                    s => s.Controls.MouseSensitivity, (s, v) => s.Controls.MouseSensitivity = v, Multiplier),
                SettingOption.Slider("controls.zoomSensitivity", C, ControlSettings.MinSensitivity, ControlSettings.MaxSensitivity, 0.1,
                    s => s.Controls.ZoomSensitivity, (s, v) => s.Controls.ZoomSensitivity = v, Multiplier),
                SettingOption.Slider("controls.cameraSpeed", C, ControlSettings.MinSensitivity, ControlSettings.MaxSensitivity, 0.1,
                    s => s.Controls.CameraSpeed, (s, v) => s.Controls.CameraSpeed = v, Multiplier),
                SettingOption.Toggle("controls.invertOrbitY", C, s => s.Controls.InvertOrbitY, (s, v) => s.Controls.InvertOrbitY = v),

                SettingOption.Choice("simulation.quality", S, EnumChoices<SimulationQuality>("settings.simulation.quality."),
                    s => s.Simulation.Quality.ToString(), (s, v) => s.Simulation.Quality = ParseEnum<SimulationQuality>(v)),
                SettingOption.Choice("simulation.autosaveInterval", S, MinuteChoices(SimulationSettings.AllowedAutosaveIntervals),
                    s => s.Simulation.AutosaveIntervalMinutes.ToString(CultureInfo.InvariantCulture), (s, v) => s.Simulation.AutosaveIntervalMinutes = ParseInt(v)),
                SettingOption.Toggle("simulation.autosaveBeforeDeepTimeJump", S,
                    s => s.Simulation.AutosaveBeforeDeepTimeJump, (s, v) => s.Simulation.AutosaveBeforeDeepTimeJump = v),
                SettingOption.Slider("simulation.maxAutosavesPerWorld", S, 1, 20, 1,
                    s => s.Simulation.MaxAutosavesPerWorld, (s, v) => s.Simulation.MaxAutosavesPerWorld = (int)Math.Round(v),
                    (v, _) => v.ToString("0", CultureInfo.InvariantCulture)),
                SettingOption.Toggle("simulation.pauseWhenUnfocused", S, s => s.Simulation.PauseWhenUnfocused, (s, v) => s.Simulation.PauseWhenUnfocused = v),

                SettingOption.Choice("interface.language", I, languageChoices, s => s.Interface.Language, (s, v) => s.Interface.Language = v),
                SettingOption.Slider("interface.uiScale", I, InterfaceSettings.MinUiScale, InterfaceSettings.MaxUiScale, 0.05,
                    s => s.Interface.UiScale, (s, v) => s.Interface.UiScale = v, Percent),
                SettingOption.Slider("interface.tooltipDelaySeconds", I, 0, InterfaceSettings.MaxTooltipDelaySeconds, 0.1,
                    s => s.Interface.TooltipDelaySeconds, (s, v) => s.Interface.TooltipDelaySeconds = v,
                    (v, _) => v.ToString("0.0", CultureInfo.InvariantCulture) + " s"),
                SettingOption.Toggle("interface.showConfirmations", I, s => s.Interface.ShowConfirmations, (s, v) => s.Interface.ShowConfirmations = v),
                SettingOption.Toggle("interface.showWelcomeOnStartup", I, s => s.Interface.ShowWelcomeOnStartup, (s, v) => s.Interface.ShowWelcomeOnStartup = v),
                SettingOption.Toggle("interface.showDebugOverlay", I, s => s.Interface.ShowDebugOverlay, (s, v) => s.Interface.ShowDebugOverlay = v),
                SettingOption.Toggle("interface.reduceMotion", I, s => s.Interface.ReduceMotion, (s, v) => s.Interface.ReduceMotion = v),
            };
        }

        public static string CategoryLabelKey(SettingsCategory category) => "settings.category." + category.ToString().ToLowerInvariant();

        public static string ResolutionId(ScreenResolution r) => r.ToString();

        public static string ResolutionLabel(ScreenResolution r)
        {
            string s = r.Width.ToString(CultureInfo.InvariantCulture) + " × " + r.Height.ToString(CultureInfo.InvariantCulture);
            return r.RefreshRateHz > 0 ? s + " @ " + r.RefreshRateHz.ToString("0.##", CultureInfo.InvariantCulture) + " Hz" : s;
        }

        private static string Percent(double v, LocalizationTable _) => Math.Round(v * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

        private static string Multiplier(double v, LocalizationTable _) => v.ToString("0.0", CultureInfo.InvariantCulture) + "×";

        private static IReadOnlyList<SettingChoice> EnumChoices<TEnum>(string labelPrefix) where TEnum : struct, Enum
        {
            var list = new List<SettingChoice>();
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
                list.Add(new SettingChoice(value.ToString(), labelPrefix + value, true));
            return list;
        }

        private static IReadOnlyList<SettingChoice> IntChoices(int[] values, string zeroLabelKey)
        {
            var list = new List<SettingChoice>();
            foreach (int v in values)
            {
                string id = v.ToString(CultureInfo.InvariantCulture);
                list.Add(v == 0 ? new SettingChoice(id, zeroLabelKey, true) : new SettingChoice(id, id, false));
            }
            return list;
        }

        private static IReadOnlyList<SettingChoice> MinuteChoices(int[] values)
        {
            var list = new List<SettingChoice>();
            foreach (int v in values)
            {
                string id = v.ToString(CultureInfo.InvariantCulture);
                list.Add(v == 0 ? new SettingChoice(id, "settings.value.off", true) : new SettingChoice(id, id + " min", false));
            }
            return list;
        }

        private static TEnum ParseEnum<TEnum>(string id) where TEnum : struct, Enum
            => Enum.TryParse(id, false, out TEnum value) ? value : throw new ArgumentException("Ismeretlen érték: " + id);

        private static int ParseInt(string id) => int.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture);

        private static ScreenResolution FindResolution(IReadOnlyList<ScreenResolution> available, string id)
        {
            foreach (var r in ResolutionCatalog.Normalize(available))
                if (ResolutionId(r) == id) return r;
            throw new ArgumentException("Ismeretlen felbontás: " + id);
        }
    }
}
