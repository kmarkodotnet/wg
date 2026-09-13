#nullable enable
using System;
using System.Collections.Generic;
using WorldGen.App.Settings;

namespace WorldGen.App.Audio
{
    /// <summary>A mixer-csoportok (WF-AUDIO-001): Master → Music / Ambient / Effects / Ui.</summary>
    public enum AudioChannel
    {
        Master,
        Music,
        Ambient,
        Effects,
        Ui,
    }

    /// <summary>
    /// Hangerő-átváltás a Unity AudioMixer dB-paramétereihez. ND-109: a
    /// <c>Math.Log10</c> / <c>Math.Pow</c> itt megengedett, mert nem a
    /// szimulációs úton van.
    /// </summary>
    public static class VolumeMath
    {
        /// <summary>A Unity mixer alsó határa; ez a gyakorlati némítás.</summary>
        public const double SilenceDecibels = -80.0;

        public static double LinearToDecibels(double linear)
        {
            if (double.IsNaN(linear) || linear <= 0) return SilenceDecibels;
            double clamped = linear > 1 ? 1 : linear;
            return Math.Max(SilenceDecibels, 20.0 * Math.Log10(clamped));
        }

        public static double DecibelsToLinear(double decibels)
        {
            if (double.IsNaN(decibels) || decibels <= SilenceDecibels) return 0;
            double linear = Math.Pow(10.0, decibels / 20.0);
            return linear > 1 ? 1 : linear;
        }
    }

    /// <summary>
    /// A beállításokból a mixer exponált paraméterei. A Unity mixer a
    /// hierarchiában maga szoroz, ezért egy gyermekcsoport csak a saját
    /// hangerejét kapja; a némítás a Master csoporton hat.
    /// </summary>
    public static class AudioMixModel
    {
        public static string ExposedParameterName(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Master: return "MasterVolume";
                case AudioChannel.Music: return "MusicVolume";
                case AudioChannel.Ambient: return "AmbientVolume";
                case AudioChannel.Effects: return "EffectsVolume";
                case AudioChannel.Ui: return "UiVolume";
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }

        public static double GroupDecibels(AudioSettings settings, AudioChannel channel)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (channel == AudioChannel.Master && settings.Muted) return VolumeMath.SilenceDecibels;
            return VolumeMath.LinearToDecibels(OwnLinear(settings, channel));
        }

        /// <summary>A hallható végső szint (mixeren kívüli fogyasztóknak): Master × csatorna, némítva 0.</summary>
        public static double EffectiveLinear(AudioSettings settings, AudioChannel channel)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.Muted) return 0;
            var s = settings.Clone();
            s.Normalize();
            return channel == AudioChannel.Master ? s.Master : s.Master * OwnLinear(s, channel);
        }

        /// <summary>Minden exponált paraméter (név, dB) — a kötés egy ciklusban alkalmazza.</summary>
        public static IReadOnlyList<KeyValuePair<string, double>> AllParameters(AudioSettings settings)
        {
            var result = new List<KeyValuePair<string, double>>();
            foreach (AudioChannel c in Enum.GetValues(typeof(AudioChannel)))
                result.Add(new KeyValuePair<string, double>(ExposedParameterName(c), GroupDecibels(settings, c)));
            return result;
        }

        private static double OwnLinear(AudioSettings s, AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Master: return s.Master;
                case AudioChannel.Music: return s.Music;
                case AudioChannel.Ambient: return s.Ambient;
                case AudioChannel.Effects: return s.Effects;
                case AudioChannel.Ui: return s.Ui;
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }
    }

    public enum UiSoundEvent
    {
        Hover,
        Click,
        Back,
        Confirm,
        Error,
    }

    /// <summary>
    /// UI-hangok ritkítása (WF-AUDIO-004, „ne legyen agresszív”): eseményenként
    /// minimális időköz. Az idő monoton, unscaled másodperc (pl. <c>Time.unscaledTimeAsDouble</c>).
    /// </summary>
    public sealed class UiSoundThrottle
    {
        private readonly Dictionary<UiSoundEvent, double> _minimumIntervals = new Dictionary<UiSoundEvent, double>
        {
            [UiSoundEvent.Hover] = 0.08,
            [UiSoundEvent.Click] = 0.03,
            [UiSoundEvent.Back] = 0.05,
            [UiSoundEvent.Confirm] = 0.10,
            [UiSoundEvent.Error] = 0.25,
        };

        private readonly Dictionary<UiSoundEvent, double> _lastPlayed = new Dictionary<UiSoundEvent, double>();

        public double GetMinimumInterval(UiSoundEvent sound) => _minimumIntervals[sound];

        public void SetMinimumInterval(UiSoundEvent sound, double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            _minimumIntervals[sound] = seconds;
        }

        /// <summary>Igaz, ha a hang most lejátszható (és ezt rögzíti).</summary>
        public bool TryPlay(UiSoundEvent sound, double nowSeconds)
        {
            if (!double.IsFinite(nowSeconds)) return false;
            if (_lastPlayed.TryGetValue(sound, out double last) && nowSeconds >= last && nowSeconds - last < _minimumIntervals[sound])
                return false;
            _lastPlayed[sound] = nowSeconds;
            return true;
        }

        public void Reset() => _lastPlayed.Clear();
    }
}
