using System.Collections.Generic;
using WorldGen.Core.Random;

namespace WorldGen.Core.Features
{
    /// <summary>
    /// M8 névgenerálás (§6.3, docs/01-architecture.md): szótag-tő +
    /// biome-alapú "hangulati" utótag.
    ///
    /// HATÓKÖR (dokumentált egyszerűsítés): nincs teljes morfológiai
    /// típusfelismerés (§6.2 — delta/hegylánc/medence mintafelismerés),
    /// csak a domináns biome alapján választott utótag-készlet.
    ///
    /// A szótag-/utótag-választás NAIV modulóval történik (nem a
    /// DeterministicRandom.SampleInt torzításmentes elutasításos
    /// módszerével) — kis (≤30 elemű), kreatív/esztétikai listákra a
    /// torzítás elhanyagolható, és ez pontosan megegyezik a Python
    /// referenciával (tools/reference/features_ref.py).
    /// </summary>
    public static class NameGeneration
    {
        private static readonly string[] Syllables =
        {
            "Au", "Rel", "Ion", "Nor", "Wat", "Ver", "Del", "Rin", "Hal", "Cy",
            "Fros", "Sil", "Tide", "Mar", "Light", "South", "East", "Vel", "Dor",
            "Ka", "Lu", "Mir", "Os", "Pyr", "Quel", "Rha", "Syl", "Thal", "Um",
        };

        private static readonly Dictionary<string, string[]> BiomeSuffixes = new Dictionary<string, string[]>
        {
            ["IceSheet"] = new[] { "Frost", "Rime", "Ice", "Glacier" },
            ["SeaIce"] = new[] { "Frost", "Rime", "Ice" },
            ["Tundra"] = new[] { "Tundra", "Barrens", "Waste" },
            ["Temperate"] = new[] { "Forest", "Woods", "Vale", "Downs" },
            ["Tropical"] = new[] { "Isles", "Verdant", "Reach", "Coast" },
        };

        private static readonly string[] DefaultSuffixes = { "Land", "Reach", "Expanse" };

        private static ulong SampleIntNaive(
            ulong worldSeed, uint propertyId, ulong spatialId, ulong timeBucket, int maxExclusive)
        {
            ulong x = DeterministicRandom.Block(
                worldSeed, RandomDomain.Naming, spatialId, timeBucket, propertyId, 0).X0;
            return x % (ulong)maxExclusive;
        }

        /// <summary>Egy feature (kontinens/régió) neve: "&lt;tő&gt; &lt;utótag&gt;".</summary>
        public static string GenerateName(ulong worldSeed, ulong featureId, string dominantBiome)
        {
            int s1Idx = (int)SampleIntNaive(worldSeed, RandomProperty.SyllableChoice, featureId, 0, Syllables.Length);
            int s2Idx = (int)SampleIntNaive(worldSeed, RandomProperty.SyllableChoice, featureId, 1, Syllables.Length);
            string stem = Syllables[s1Idx] + Syllables[s2Idx].ToLowerInvariant();

            string[] suffixes = BiomeSuffixes.TryGetValue(dominantBiome, out string[] found) ? found : DefaultSuffixes;
            int suffixIdx = (int)SampleIntNaive(worldSeed, RandomProperty.SuffixChoice, featureId, 0, suffixes.Length);

            return $"{stem} {suffixes[suffixIdx]}";
        }
    }
}
