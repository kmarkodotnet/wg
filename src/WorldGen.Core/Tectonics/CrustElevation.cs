using WorldGen.Core.Random;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M4 kéreg-típus és alap-eleváció (§4.2, docs/05-milestones.md).
    ///
    /// HATÓKÖR (dokumentált egyszerűsítés, NEM ND — később finomítható):
    ///   - Kéreg-típus PLATE-szinten (a spec §14.1 Plate struct-ja is így
    ///     modellezi: egy plate egyetlen CrustType mezővel rendelkezik).
    ///   - Az §13.2 "fraktál részlet" (F) helyett egyszerű, tile-onként
    ///     FÜGGETLEN (fehér zaj-szerű) magasság-jitter — NEM térben koherens
    ///     fBm/Perlin. A makro-szerkezetet a plate-szintű kéreg-típus adja,
    ///     ami már térben koherens (nagy Voronoi-régiók).
    /// </summary>
    public static class CrustElevation
    {
        public const double OceanicBaseMeters = -4000.0;
        public const double ContinentalBaseMeters = 800.0;
        public const double NoiseAmplitudeMeters = 500.0;
        public const double DefaultOceanicProbability = 0.55;

        /// <summary>Kéreg-típus lemezenként — determinisztikus Bernoulli-próba.</summary>
        public static bool IsOceanic(ulong worldSeed, int plateId, double oceanicProbability = DefaultOceanicProbability)
        {
            return DeterministicRandom.Chance(
                worldSeed, RandomDomain.Tectonics, (ulong)plateId, 0,
                oceanicProbability, RandomProperty.CrustType);
        }

        /// <summary>[-1,1) független "jitter" tile-onként — NEM térben koherens (ld. osztály-doc).</summary>
        public static double TileNoiseJitter(ulong worldSeed, ulong tileIdValue)
        {
            double v = DeterministicRandom.Sample(
                worldSeed, RandomDomain.Terrain, tileIdValue, 0, RandomProperty.NoiseGradient);
            return 2.0 * v - 1.0;
        }

        /// <summary>A tile alap-magassága méterben: kéreg-típus bázis + jitter.</summary>
        public static double BaseElevation(
            ulong worldSeed, int plateId, ulong tileIdValue, out bool isOceanic,
            double oceanicProbability = DefaultOceanicProbability)
        {
            isOceanic = IsOceanic(worldSeed, plateId, oceanicProbability);
            double baseValue = isOceanic ? OceanicBaseMeters : ContinentalBaseMeters;
            double jitter = TileNoiseJitter(worldSeed, tileIdValue);
            return baseValue + jitter * NoiseAmplitudeMeters;
        }
    }
}
