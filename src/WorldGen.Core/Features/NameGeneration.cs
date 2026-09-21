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

            // ND-126: a ket homersekleti osztaly (Temperate/Tropical) helyere
            // ot, csapadek szerint is megkulonboztetett biome lepett. A
            // nev-kulcs a Biome.ToString(), tehat ezeknek EGYEZNIUK kell az
            // enum nevekkel - kulonben csendben a DefaultSuffixes jon.
            ["Desert"] = new[] { "Desert", "Sands", "Dunes", "Barrens" },
            ["Grassland"] = new[] { "Steppe", "Plains", "Prairie", "Downs" },
            ["TemperateForest"] = new[] { "Forest", "Woods", "Vale", "Downs" },
            ["Savanna"] = new[] { "Savanna", "Veld", "Reach", "Flats" },
            ["Rainforest"] = new[] { "Jungle", "Verdant", "Canopy", "Coast" },

            // Tektonikuslemez-overlay (2026-09-13, docs/backlog.md) -
            // NEM biome, csak a MEGLÉVŐ biome-alapú utótag-kiválasztást
            // hasznosítja újra (ld. PlatePresentation.PlateName) a
            // kéreg-típushoz illő "hangulati" utótaggal, hogy ne a
            // (ide nem is tartozó) biome-hangzású nevek jöjjenek ki.
            ["OceanicCrust"] = new[] { "Trench", "Abyss", "Rise", "Deep" },
            ["ContinentalCrust"] = new[] { "Craton", "Shield", "Massif", "Plate" },
        };

        private static readonly string[] DefaultSuffixes = { "Land", "Reach", "Expanse" };

        /// <summary>
        /// Morfológiai típus szerinti utótag-készlet (docs/01-architecture.md
        /// §2.3: "Northwatch Range", "Halcyon Basin" stb. - a régiónév a
        /// FELISMERT morfológiai típust tükrözi, nem csak a domináns biome-ot).
        /// <see cref="FeatureSegmentation.LandformType.Lowland"/>
        /// SZÁNDÉKOSAN hiányzik innen - arra a biome-alapú utótag marad
        /// (ld. "Frosthold Tundra" a doksi-példák közt: sík, jellegtelen
        /// terepnél a klíma, nem a domborzat adja a névízt).
        /// </summary>
        private static readonly Dictionary<FeatureSegmentation.LandformType, string[]> LandformSuffixes =
            new Dictionary<FeatureSegmentation.LandformType, string[]>
        {
            [FeatureSegmentation.LandformType.Mountains] = new[] { "Range", "Peaks", "Highlands" },
            [FeatureSegmentation.LandformType.Plateau] = new[] { "Plateau", "Mesa", "Tableland" },
            [FeatureSegmentation.LandformType.Basin] = new[] { "Basin", "Hollow", "Depression" },
            [FeatureSegmentation.LandformType.Island] = new[] { "Isle", "Cay", "Atoll" },
            [FeatureSegmentation.LandformType.Plain] = new[] { "Plain", "Flats", "Steppe" },
        };

        private static ulong SampleIntNaive(
            ulong worldSeed, uint propertyId, ulong spatialId, ulong timeBucket, int maxExclusive)
        {
            ulong x = DeterministicRandom.Block(
                worldSeed, RandomDomain.Naming, spatialId, timeBucket, propertyId, 0).X0;
            return x % (ulong)maxExclusive;
        }

        /// <summary>Egy feature (kontinens/régió) neve: "&lt;tő&gt; &lt;utótag&gt;".</summary>
        public static string GenerateName(ulong worldSeed, ulong featureId, string dominantBiome)
            => GenerateName(worldSeed, featureId, dominantBiome, SuffixesFor(dominantBiome, null));

        /// <summary>
        /// Ugyanaz, mint a fenti, de a morfológiai TÍPUS (ha van, azaz nem
        /// <see cref="FeatureSegmentation.LandformType.Lowland"/>) ELSŐBBSÉGET
        /// élvez a biome-alapú utótaggal szemben (docs/01-architecture.md
        /// §2.3 - "Northwatch Range", "Halcyon Basin"). A szótag-tő
        /// (SyllableChoice) és a suffix-index (SuffixChoice) UGYANAZT a
        /// RandomProperty-t használja, mint a biome-alapú verzió - nincs új
        /// random-doménszám, tehát nem seed-törő (a régi 3-paraméteres
        /// hívók bitre változatlan nevet kapnak).
        /// </summary>
        public static string GenerateName(
            ulong worldSeed, ulong featureId, string dominantBiome, FeatureSegmentation.LandformType landform)
            => GenerateName(worldSeed, featureId, dominantBiome, SuffixesFor(dominantBiome, landform));

        private static string[] SuffixesFor(string dominantBiome, FeatureSegmentation.LandformType? landform)
        {
            if (landform.HasValue && LandformSuffixes.TryGetValue(landform.Value, out string[] landformSuffixes))
                return landformSuffixes;
            return BiomeSuffixes.TryGetValue(dominantBiome, out string[] found) ? found : DefaultSuffixes;
        }

        private static string GenerateName(ulong worldSeed, ulong featureId, string dominantBiome, string[] suffixes)
        {
            int s1Idx = (int)SampleIntNaive(worldSeed, RandomProperty.SyllableChoice, featureId, 0, Syllables.Length);
            int s2Idx = (int)SampleIntNaive(worldSeed, RandomProperty.SyllableChoice, featureId, 1, Syllables.Length);
            string stem = Syllables[s1Idx] + Syllables[s2Idx].ToLowerInvariant();

            int suffixIdx = (int)SampleIntNaive(worldSeed, RandomProperty.SuffixChoice, featureId, 0, suffixes.Length);

            return $"{stem} {suffixes[suffixIdx]}";
        }
    }
}
