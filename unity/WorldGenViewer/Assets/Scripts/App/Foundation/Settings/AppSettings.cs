#nullable enable
using System;
using System.Globalization;

namespace WorldGen.App.Settings
{
    public enum DisplayMode
    {
        Fullscreen,
        Borderless,
        Windowed,
    }

    public enum GraphicsQuality
    {
        Low,
        Medium,
        High,
        Ultra,
    }

    /// <summary>
    /// A szimuláció pontossága, a grafikai minőségtől függetlenül (WF-SET-003).
    /// A konkrét jelentését a Core-kötés adja; seed-törő lehet, ezért a
    /// mentés fejléce is tárolja.
    /// </summary>
    public enum SimulationQuality
    {
        Fast,
        Balanced,
        Accurate,
    }

    [Flags]
    public enum SettingsCategory
    {
        None = 0,
        Graphics = 1,
        Audio = 2,
        Controls = 4,
        Simulation = 8,
        Interface = 16,
        All = Graphics | Audio | Controls | Simulation | Interface,
    }

    /// <summary>Képernyőfelbontás; a 0 frissítési frekvencia „nincs megadva”.</summary>
    public readonly struct ScreenResolution : IEquatable<ScreenResolution>
    {
        public static readonly ScreenResolution Unspecified = default;

        public int Width { get; }
        public int Height { get; }
        public double RefreshRateHz { get; }

        public ScreenResolution(int width, int height, double refreshRateHz = 0)
        {
            Width = width;
            Height = height;
            RefreshRateHz = refreshRateHz;
        }

        public bool IsSpecified => Width > 0 && Height > 0;

        public long PixelCount => (long)Width * Height;

        public bool SameSize(ScreenResolution other) => Width == other.Width && Height == other.Height;

        public bool Equals(ScreenResolution other)
            => Width == other.Width && Height == other.Height && RefreshRateHz.Equals(other.RefreshRateHz);

        public override bool Equals(object? obj) => obj is ScreenResolution r && Equals(r);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Width * 397 ^ Height) * 397 ^ RefreshRateHz.GetHashCode();
            }
        }

        public override string ToString()
        {
            if (!IsSpecified) return "unspecified";
            string s = Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture);
            return RefreshRateHz > 0 ? s + "@" + RefreshRateHz.ToString("0.##", CultureInfo.InvariantCulture) : s;
        }
    }

    public sealed class GraphicsSettings
    {
        public const int MinFpsLimit = 30;
        public const int MaxFpsLimit = 360;
        public const int MinBackgroundFpsLimit = 5;
        public const int MaxResolutionDimension = 16384;

        public DisplayMode DisplayMode { get; set; } = DisplayMode.Borderless;

        /// <summary>Nincs megadva → a monitor natív felbontása.</summary>
        public ScreenResolution Resolution { get; set; } = ScreenResolution.Unspecified;

        public bool VSync { get; set; } = true;

        /// <summary>0 = korlátlan; egyébként <see cref="MinFpsLimit"/>..<see cref="MaxFpsLimit"/>.</summary>
        public int FpsLimit { get; set; }

        /// <summary>Fókusz nélküli FPS-korlát; 0 = nincs külön korlát.</summary>
        public int BackgroundFpsLimit { get; set; } = 30;

        public GraphicsQuality Quality { get; set; } = GraphicsQuality.High;

        public GraphicsSettings Clone() => (GraphicsSettings)MemberwiseClone();

        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(DisplayMode), DisplayMode)) DisplayMode = DisplayMode.Borderless;
            if (!Enum.IsDefined(typeof(GraphicsQuality), Quality)) Quality = GraphicsQuality.High;

            var r = Resolution;
            if (!r.IsSpecified || r.Width > MaxResolutionDimension || r.Height > MaxResolutionDimension)
            {
                Resolution = ScreenResolution.Unspecified;
            }
            else if (!double.IsFinite(r.RefreshRateHz) || r.RefreshRateHz < 0 || r.RefreshRateHz > 1000)
            {
                Resolution = new ScreenResolution(r.Width, r.Height, 0);
            }

            FpsLimit = FpsLimit <= 0 ? 0 : Math.Min(Math.Max(FpsLimit, MinFpsLimit), MaxFpsLimit);
            BackgroundFpsLimit = BackgroundFpsLimit <= 0 ? 0 : Math.Min(Math.Max(BackgroundFpsLimit, MinBackgroundFpsLimit), MaxFpsLimit);
        }
    }

    public sealed class AudioSettings
    {
        public double Master { get; set; } = 1.0;
        public double Music { get; set; } = 0.7;
        public double Ambient { get; set; } = 0.8;
        public double Effects { get; set; } = 0.8;
        public double Ui { get; set; } = 0.6;
        public bool Muted { get; set; }

        public AudioSettings Clone() => (AudioSettings)MemberwiseClone();

        public void Normalize()
        {
            var d = new AudioSettings();
            Master = SettingsMath.Clamp(Master, 0, 1, d.Master);
            Music = SettingsMath.Clamp(Music, 0, 1, d.Music);
            Ambient = SettingsMath.Clamp(Ambient, 0, 1, d.Ambient);
            Effects = SettingsMath.Clamp(Effects, 0, 1, d.Effects);
            Ui = SettingsMath.Clamp(Ui, 0, 1, d.Ui);
        }
    }

    public sealed class ControlSettings
    {
        public const double MinSensitivity = 0.1;
        public const double MaxSensitivity = 5.0;

        public double MouseSensitivity { get; set; } = 1.0;
        public double ZoomSensitivity { get; set; } = 1.0;
        public double CameraSpeed { get; set; } = 1.0;
        public bool InvertOrbitY { get; set; }

        public ControlSettings Clone() => (ControlSettings)MemberwiseClone();

        public void Normalize()
        {
            MouseSensitivity = SettingsMath.Clamp(MouseSensitivity, MinSensitivity, MaxSensitivity, 1.0);
            ZoomSensitivity = SettingsMath.Clamp(ZoomSensitivity, MinSensitivity, MaxSensitivity, 1.0);
            CameraSpeed = SettingsMath.Clamp(CameraSpeed, MinSensitivity, MaxSensitivity, 1.0);
        }
    }

    public sealed class SimulationSettings
    {
        /// <summary>Engedett autosave-időközök percben (WF-SAVE-004); 0 = kikapcsolva.</summary>
        public static readonly int[] AllowedAutosaveIntervals = { 0, 5, 10, 20 };

        public SimulationQuality Quality { get; set; } = SimulationQuality.Balanced;
        public int AutosaveIntervalMinutes { get; set; } = 10;
        public bool AutosaveBeforeDeepTimeJump { get; set; }
        public int MaxAutosavesPerWorld { get; set; } = 3;
        public bool PauseWhenUnfocused { get; set; } = true;

        public SimulationSettings Clone() => (SimulationSettings)MemberwiseClone();

        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(SimulationQuality), Quality)) Quality = SimulationQuality.Balanced;
            AutosaveIntervalMinutes = SnapAutosaveInterval(AutosaveIntervalMinutes);
            MaxAutosavesPerWorld = Math.Min(Math.Max(MaxAutosavesPerWorld, 1), 20);
        }

        /// <summary>A legközelebbi engedett érték; döntetlennél a kisebb (gyakoribb mentés).</summary>
        public static int SnapAutosaveInterval(int minutes)
        {
            if (minutes <= 0) return 0;
            int best = AllowedAutosaveIntervals[0];
            foreach (int allowed in AllowedAutosaveIntervals)
            {
                if (Math.Abs(allowed - minutes) < Math.Abs(best - minutes)) best = allowed;
            }
            return best;
        }
    }

    public sealed class InterfaceSettings
    {
        public const double MinUiScale = 0.75;
        public const double MaxUiScale = 2.0;
        public const double MaxTooltipDelaySeconds = 3.0;

        public string Language { get; set; } = "en";
        public double UiScale { get; set; } = 1.0;
        public double TooltipDelaySeconds { get; set; } = 0.5;
        public bool ShowConfirmations { get; set; } = true;
        public bool ShowWelcomeOnStartup { get; set; } = true;
        public bool ShowDebugOverlay { get; set; }
        public bool ReduceMotion { get; set; }

        public InterfaceSettings Clone() => (InterfaceSettings)MemberwiseClone();

        public void Normalize()
        {
            Language = IsValidLanguageTag(Language) ? Language.Trim() : "en";
            UiScale = SettingsMath.Clamp(UiScale, MinUiScale, MaxUiScale, 1.0);
            TooltipDelaySeconds = SettingsMath.Clamp(TooltipDelaySeconds, 0, MaxTooltipDelaySeconds, 0.5);
        }

        /// <summary>Egyszerű nyelvcímke: 2–16 karakter, betű vagy kötőjel, betűvel kezdődik.</summary>
        public static bool IsValidLanguageTag(string? tag)
        {
            if (tag == null) return false;
            string t = tag.Trim();
            if (t.Length < 2 || t.Length > 16 || !IsAsciiLetter(t[0])) return false;
            foreach (char ch in t)
                if (!IsAsciiLetter(ch) && ch != '-') return false;
            return true;
        }

        private static bool IsAsciiLetter(char ch) => (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
    }

    /// <summary>
    /// A teljes, perzisztált beállításkészlet (WF-SET-001). Módosítható
    /// munkapéldány; a <see cref="SettingsStore"/> mindig klónt ad ki.
    /// </summary>
    public sealed class AppSettings
    {
        public GraphicsSettings Graphics { get; private set; } = new GraphicsSettings();
        public AudioSettings Audio { get; private set; } = new AudioSettings();
        public ControlSettings Controls { get; private set; } = new ControlSettings();
        public SimulationSettings Simulation { get; private set; } = new SimulationSettings();
        public InterfaceSettings Interface { get; private set; } = new InterfaceSettings();

        public AppSettings Clone()
        {
            return new AppSettings
            {
                Graphics = Graphics.Clone(),
                Audio = Audio.Clone(),
                Controls = Controls.Clone(),
                Simulation = Simulation.Clone(),
                Interface = Interface.Clone(),
            };
        }

        public void Normalize()
        {
            Graphics.Normalize();
            Audio.Normalize();
            Controls.Normalize();
            Simulation.Normalize();
            Interface.Normalize();
        }

        public void RestoreDefaults(SettingsCategory categories)
        {
            if ((categories & SettingsCategory.Graphics) != 0) Graphics = new GraphicsSettings();
            if ((categories & SettingsCategory.Audio) != 0) Audio = new AudioSettings();
            if ((categories & SettingsCategory.Controls) != 0) Controls = new ControlSettings();
            if ((categories & SettingsCategory.Simulation) != 0) Simulation = new SimulationSettings();
            if ((categories & SettingsCategory.Interface) != 0) Interface = new InterfaceSettings();
        }

        /// <summary>Azok a kategóriák, amelyek szerializált alakja eltér.</summary>
        public SettingsCategory DiffCategories(AppSettings other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var diff = SettingsCategory.None;
            if (SettingsSerializer.GraphicsToJson(Graphics).ToString() != SettingsSerializer.GraphicsToJson(other.Graphics).ToString())
                diff |= SettingsCategory.Graphics;
            if (SettingsSerializer.AudioToJson(Audio).ToString() != SettingsSerializer.AudioToJson(other.Audio).ToString())
                diff |= SettingsCategory.Audio;
            if (SettingsSerializer.ControlsToJson(Controls).ToString() != SettingsSerializer.ControlsToJson(other.Controls).ToString())
                diff |= SettingsCategory.Controls;
            if (SettingsSerializer.SimulationToJson(Simulation).ToString() != SettingsSerializer.SimulationToJson(other.Simulation).ToString())
                diff |= SettingsCategory.Simulation;
            if (SettingsSerializer.InterfaceToJson(Interface).ToString() != SettingsSerializer.InterfaceToJson(other.Interface).ToString())
                diff |= SettingsCategory.Interface;
            return diff;
        }
    }

    internal static class SettingsMath
    {
        /// <summary>Tartományba szorítás; nem-véges érték helyett az alapérték.</summary>
        public static double Clamp(double value, double min, double max, double fallback)
        {
            if (!double.IsFinite(value)) return fallback;
            return value < min ? min : value > max ? max : value;
        }
    }
}
