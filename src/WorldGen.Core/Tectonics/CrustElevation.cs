using WorldGen.Core.Random;
using WorldGen.Core.Terrain;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M4 kéreg-típus és alap-eleváció (§4.2, docs/05-milestones.md).
    ///
    /// HATÓKÖR (dokumentált egyszerűsítés, NEM ND — később finomíthető):
    ///   - Kéreg-típus PLATE-szinten (a spec §14.1 Plate struct-ja is így
    ///     modellezi: egy plate egyetlen CrustType mezővel rendelkezik).
    ///
    /// ND-31 (docs/04-decisions.md): az §13.2 "fraktál részlet" (F)
    /// korábban egyszerű, tile-onként FÜGGETLEN (fehér zaj-szerű)
    /// magasság-jitter volt — NEM térben koherens fBm/Perlin, dokumentált,
    /// ismert hiányosság ("túl szabályos" Voronoi-cella határok). MOST már
    /// valódi, térben koherens fBm (<see cref="FractalNoise"/>) — ez töri
    /// meg a lemez-határok túl szabályos alakját organikus
    /// változatossággal.
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

        /// <summary>A tile alap-magassága méterben: kéreg-típus bázis + térben koherens fBm-zaj.</summary>
        public static double BaseElevation(
            ulong worldSeed, int plateId, double x, double y, double z, out bool isOceanic,
            double oceanicProbability = DefaultOceanicProbability)
        {
            isOceanic = IsOceanic(worldSeed, plateId, oceanicProbability);
            double baseValue = isOceanic ? OceanicBaseMeters : ContinentalBaseMeters;
            double noise = FractalNoise.Fbm(worldSeed, x, y, z);
            return baseValue + noise * NoiseAmplitudeMeters;
        }
    }
}
