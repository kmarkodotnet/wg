#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.Settings
{
    /// <summary>
    /// A platform által jelentett felbontáslista tisztítása és a mentett
    /// felbontás leképezése egy ténylegesen elérhetőre (WF-SET-002).
    /// </summary>
    public static class ResolutionCatalog
    {
        /// <summary>
        /// Érvénytelen elemek eldobása, méretenként egy elem (a legnagyobb
        /// frissítési frekvenciával), rendezés: pixelszám, majd szélesség szerint csökkenő.
        /// </summary>
        public static IReadOnlyList<ScreenResolution> Normalize(IEnumerable<ScreenResolution> modes)
        {
            if (modes == null) throw new ArgumentNullException(nameof(modes));
            var bySize = new Dictionary<long, ScreenResolution>();
            foreach (var m in modes)
            {
                if (!m.IsSpecified) continue;
                double refresh = double.IsFinite(m.RefreshRateHz) && m.RefreshRateHz > 0 ? m.RefreshRateHz : 0;
                long key = ((long)m.Width << 32) | (uint)m.Height;
                if (!bySize.TryGetValue(key, out var existing) || refresh > existing.RefreshRateHz)
                    bySize[key] = new ScreenResolution(m.Width, m.Height, refresh);
            }
            var list = new List<ScreenResolution>(bySize.Values);
            list.Sort((a, b) =>
            {
                int c = b.PixelCount.CompareTo(a.PixelCount);
                return c != 0 ? c : b.Width.CompareTo(a.Width);
            });
            return list;
        }

        /// <summary>
        /// A kért felbontás elérhető párja. Nincs megadva → a natív, ha elérhető,
        /// különben a legnagyobb. Pontos méretegyezés → az. Egyébként a legnagyobb,
        /// ami belefér a kértbe; ha egyik sem fér bele, a legkisebb.
        /// Üres listánál null.
        /// </summary>
        public static ScreenResolution? SelectBest(IReadOnlyList<ScreenResolution> available, ScreenResolution requested, ScreenResolution native)
        {
            var modes = Normalize(available);
            if (modes.Count == 0) return null;

            if (!requested.IsSpecified)
            {
                foreach (var m in modes)
                    if (m.SameSize(native)) return m;
                return modes[0];
            }

            foreach (var m in modes)
                if (m.SameSize(requested)) return m;

            foreach (var m in modes)
                if (m.Width <= requested.Width && m.Height <= requested.Height) return m;

            return modes[modes.Count - 1];
        }
    }

    /// <summary>A Unity <c>QualitySettings.vSyncCount</c> és <c>Application.targetFrameRate</c> értékei.</summary>
    public readonly struct FrameRateDecision
    {
        /// <summary>A platform alapértelmezése (Unity: -1).</summary>
        public const int PlatformDefaultFrameRate = -1;

        public int VSyncCount { get; }
        public int TargetFrameRate { get; }
        public bool PauseSimulation { get; }

        public FrameRateDecision(int vSyncCount, int targetFrameRate, bool pauseSimulation)
        {
            VSyncCount = vSyncCount;
            TargetFrameRate = targetFrameRate;
            PauseSimulation = pauseSimulation;
        }
    }

    /// <summary>VSync, FPS-limit és fókuszvesztés (WF-SET-002, WF-SET-006).</summary>
    public static class FrameRatePolicy
    {
        public static FrameRateDecision Resolve(GraphicsSettings graphics, SimulationSettings simulation, bool hasFocus)
        {
            if (graphics == null) throw new ArgumentNullException(nameof(graphics));
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            var g = graphics.Clone();
            g.Normalize();
            bool pause = !hasFocus && simulation.PauseWhenUnfocused;

            if (!hasFocus && g.BackgroundFpsLimit > 0)
            {
                int limit = g.FpsLimit > 0 ? Math.Min(g.FpsLimit, g.BackgroundFpsLimit) : g.BackgroundFpsLimit;
                return new FrameRateDecision(0, limit, pause);
            }

            // VSync mellett a Unity figyelmen kívül hagyja a targetFrameRate-et, ezért ott nincs külön limit.
            if (g.VSync) return new FrameRateDecision(1, FrameRateDecision.PlatformDefaultFrameRate, pause);

            return new FrameRateDecision(0, g.FpsLimit > 0 ? g.FpsLimit : FrameRateDecision.PlatformDefaultFrameRate, pause);
        }
    }
}
