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
    /// ismert hiányosság ("túl szabályos" Voronoi-cella határok).
    ///
    /// ND-33 (docs/04-decisions.md): a sima fBm (ND-31) továbbra is túl
    /// simának/észrevehetetlennek bizonyult vizuálisan, még 4x
    /// amplitúdóval is (felhasználói visszajelzés). MOST "ridged
    /// multifractal" (<see cref="FractalNoise.RidgedMultifractal"/>, spec
    /// §13.1 nevesítve is említi) — éles gerinceket ad, jóval nagyobb
    /// vizuális kontraszttal, mint a sima fBm lekerekített dombjai.
    /// </summary>
    public static class CrustElevation
    {
        public const double OceanicBaseMeters = -4000.0;
        public const double ContinentalBaseMeters = 800.0;
        // ND-33: tovabb emelve (2000->3000), ridged multifractalra valtva
        // a sima fBm helyett a nagyobb vizualis kontraszt erdekeben.
        public const double NoiseAmplitudeMeters = 3000.0;
        public const double DefaultOceanicProbability = 0.55;

        /// <summary>Kéreg-típus lemezenként — determinisztikus Bernoulli-próba.</summary>
        public static bool IsOceanic(ulong worldSeed, int plateId, double oceanicProbability = DefaultOceanicProbability)
        {
            return DeterministicRandom.Chance(
                worldSeed, RandomDomain.Tectonics, (ulong)plateId, 0,
                oceanicProbability, RandomProperty.CrustType);
        }

        /// <summary>A tile alap-magassága méterben: kéreg-típus bázis + térben koherens ridged zaj.</summary>
        public static double BaseElevation(
            ulong worldSeed, int plateId, double x, double y, double z, out bool isOceanic,
            double oceanicProbability = DefaultOceanicProbability)
        {
            isOceanic = IsOceanic(worldSeed, plateId, oceanicProbability);
            double baseValue = isOceanic ? OceanicBaseMeters : ContinentalBaseMeters;
            // ridged_multifractal kb. [0,1]-hez kozeli, atlagosan ~0.7
            // korul - (r-0.5)*2-vel [-1,1]-hez kozeli, ELOJELES
            // modositova alakitva, hogy tovabbra is szimmetrikus
            // magassag-perturbaciokent hasson (nem csak felfele told).
            double r = FractalNoise.RidgedMultifractal(worldSeed, x, y, z);
            double noise = (r - 0.5) * 2.0;
            return baseValue + noise * NoiseAmplitudeMeters;
        }
    }
}
