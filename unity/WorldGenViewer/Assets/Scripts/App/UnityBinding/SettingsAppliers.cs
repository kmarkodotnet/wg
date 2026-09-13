#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using WorldGen.App.Audio;
using WorldGen.App.Diagnostics;
using WorldGen.App.Settings;
using AppAudioSettings = WorldGen.App.Settings.AudioSettings;
using AppGraphicsSettings = WorldGen.App.Settings.GraphicsSettings;

namespace WorldGen.App.UnityBinding
{
    /// <summary>A grafikai beállítások alkalmazása a Unity Screen / QualitySettings API-ra (WF-SET-002, WF-SET-006).</summary>
    public sealed class UnityGraphicsApplier
    {
        private readonly LogChannel _log;

        public UnityGraphicsApplier(LogChannel log)
        {
            _log = log;
        }

        public FrameRateDecision ApplyFrameRate(AppGraphicsSettings graphics, SimulationSettings simulation, bool hasFocus)
        {
            var decision = FrameRatePolicy.Resolve(graphics, simulation, hasFocus);
            QualitySettings.vSyncCount = decision.VSyncCount;
            Application.targetFrameRate = decision.TargetFrameRate;
            return decision;
        }

        public int ApplyQuality(GraphicsQuality quality)
        {
            int index = QualityLevelMapper.Resolve(quality, QualitySettings.names);
            if (index < 0) return index;
            if (index != QualitySettings.GetQualityLevel())
            {
                QualitySettings.SetQualityLevel(index, true);
                _log.Info("Quality level " + quality + " -> '" + QualitySettings.names[index] + "'");
            }
            return index;
        }

        /// <summary>
        /// Kijelzőmód és felbontás. Az Editorban a Game view nem állítható, ott csak
        /// naplóz. A választott (ténylegesen elérhető) felbontást adja vissza.
        /// </summary>
        public ScreenResolution ApplyDisplay(AppGraphicsSettings graphics)
        {
            var g = graphics.Clone();
            g.Normalize();
            var available = new List<ScreenResolution>();
            foreach (var r in Screen.resolutions) available.Add(new ScreenResolution(r.width, r.height, r.refreshRateRatio.value));
            var current = Screen.currentResolution;
            var native = new ScreenResolution(current.width, current.height, current.refreshRateRatio.value);
            var chosen = ResolutionCatalog.SelectBest(available, g.Resolution, native) ?? native;
            var mode = ToFullScreenMode(g.DisplayMode);

            if (Application.isEditor)
            {
                _log.Info("Display change skipped in Editor: " + g.DisplayMode + " " + chosen);
                return chosen;
            }
            Screen.SetResolution(chosen.Width, chosen.Height, mode, ToRefreshRate(chosen.RefreshRateHz));
            _log.Info("Display " + g.DisplayMode + " " + chosen);
            return chosen;
        }

        public static FullScreenMode ToFullScreenMode(DisplayMode mode)
        {
            switch (mode)
            {
                case DisplayMode.Fullscreen: return FullScreenMode.ExclusiveFullScreen;
                case DisplayMode.Windowed: return FullScreenMode.Windowed;
                default: return FullScreenMode.FullScreenWindow;
            }
        }

        private static RefreshRate ToRefreshRate(double hz)
        {
            if (!(hz > 0)) return new RefreshRate { numerator = 0, denominator = 1 };
            return new RefreshRate { numerator = (uint)Math.Round(hz * 1000.0), denominator = 1000 };
        }
    }

    /// <summary>
    /// Hangbeállítások az AudioMixer exponált paramétereire (WF-AUDIO-001, WF-SET-004).
    /// Mixer nélkül (még nincs asset) az <c>AudioListener.volume</c>-ot állítja.
    /// </summary>
    public sealed class UnityAudioApplier
    {
        private readonly AudioMixer? _mixer;
        private readonly LogChannel _log;
        private readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.Ordinal);

        public UnityAudioApplier(AudioMixer? mixer, LogChannel log)
        {
            _mixer = mixer;
            _log = log;
        }

        public void Apply(AppAudioSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (_mixer == null)
            {
                AudioListener.volume = (float)AudioMixModel.EffectiveLinear(settings, AudioChannel.Master);
                return;
            }
            AudioListener.volume = 1f;
            foreach (var parameter in AudioMixModel.AllParameters(settings))
            {
                if (!_mixer.SetFloat(parameter.Key, (float)parameter.Value) && _reportedMissing.Add(parameter.Key))
                    _log.Warning("AudioMixer has no exposed parameter '" + parameter.Key + "'");
            }
        }
    }
}
