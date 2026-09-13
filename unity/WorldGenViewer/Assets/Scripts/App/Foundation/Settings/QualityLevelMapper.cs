#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.Settings
{
    /// <summary>
    /// A 4 fokozatú <see cref="GraphicsQuality"/> leképezése a projekt Unity
    /// quality-szintjeire (<c>QualitySettings.names</c>). A HDRP-sablon 3 szintet ad
    /// („High Fidelity”, „Balanced”, „Performant”), csökkenő minőségi sorrendben,
    /// ezért a sorrend nem, csak a név megbízható. Ismert nevek: a legnagyobb, ami
    /// nem haladja meg a kértet (Ultra → High Fidelity); ha egyik név sem ismert,
    /// a pozíció szerint növekvő sorrendet feltételez.
    /// </summary>
    public static class QualityLevelMapper
    {
        private static readonly string[][] Aliases =
        {
            new[] { "very low", "low", "fastest", "fast", "performant", "performance" },
            new[] { "medium", "simple", "good", "balanced" },
            new[] { "high", "beautiful", "high fidelity", "quality" },
            new[] { "ultra", "fantastic", "very high", "epic", "cinematic" },
        };

        /// <summary>A quality-szint indexe; -1, ha a lista üres.</summary>
        public static int Resolve(GraphicsQuality quality, IReadOnlyList<string> levelNames)
        {
            if (levelNames == null) throw new ArgumentNullException(nameof(levelNames));
            int n = levelNames.Count;
            if (n == 0) return -1;
            int requested = Math.Min(Math.Max((int)quality, 0), 3);

            // A fokozat nevével pontosan egyező szint nyer (pl. a Unity alapsablon "Low"-ja a "Very Low" előtt).
            string exact = ((GraphicsQuality)requested).ToString();
            for (int i = 0; i < n; i++)
                if (string.Equals(levelNames[i]?.Trim(), exact, StringComparison.OrdinalIgnoreCase)) return i;

            int best = -1;
            int bestRank = -1;
            int lowest = -1;
            int lowestRank = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int rank = RankOf(levelNames[i]);
                if (rank < 0) continue;
                if (rank <= requested && rank > bestRank)
                {
                    best = i;
                    bestRank = rank;
                }
                if (rank < lowestRank)
                {
                    lowest = i;
                    lowestRank = rank;
                }
            }
            if (best >= 0) return best;
            if (lowest >= 0) return lowest;
            return (int)Math.Round(requested * (n - 1) / 3.0, MidpointRounding.AwayFromZero);
        }

        public static int RankOf(string? levelName)
        {
            if (levelName == null) return -1;
            string name = levelName.Trim().ToLowerInvariant();
            for (int rank = 0; rank < Aliases.Length; rank++)
                foreach (string alias in Aliases[rank])
                    if (name == alias) return rank;
            return -1;
        }
    }
}
