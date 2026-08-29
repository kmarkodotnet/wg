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
    ///
    /// ND-34 (docs/04-decisions.md): további felhasználói visszajelzés —
    /// (a) az óceánfenék is túl erősen "hegyes" lett, holott a valóságban
    /// az óceáni relief szelídebb a kontinentálisnál; (b) a durvaság
    /// egyenletes volt a szárazföldön, holott realisztikusabb, ha van
    /// sík/fennsík ÉS hegyvidék is. Ezért: (1) óceáni tile-ok a zaj
    /// <see cref="OceanicNoiseFactor"/>-szorosát kapják csak; (2) egy
    /// külön, ALACSONY FREKVENCIÁS "hegyvidékiség" maszk (sima fBm, nem
    /// ridged) határozza meg REGIONÁLISAN, mennyire érvényesüljön a
    /// ridged részlet — nagy területek maradhatnak simák, mások
    /// dramatikusan durvák.
    /// </summary>
    public static class CrustElevation
    {
        public const double OceanicBaseMeters = -4000.0;
        public const double ContinentalBaseMeters = 800.0;
        // ND-33: tovabb emelve (2000->3000), ridged multifractalra valtva
        // a sima fBm helyett a nagyobb vizualis kontraszt erdekeben.
        public const double NoiseAmplitudeMeters = 3000.0;

        // ND-37 (docs/04-decisions.md): SZANDEKOSAN a SeaLevelCalibration
        // TargetWaterFraction-je (0.65) ALATT, attol decorrelalva. Korabban
        // 0.55 volt (kb. Fold-szeru arany), de a tile-sulyozott oceani-lemez-
        // arany (~66.7%) majdnem egybeesett a celzott viz-arannyal, ezert a
        // percentilis-alapu tengerszint-kalibracio a tengerszintet melyen az
        // oceani kereg elevaciotartomanyaba (OceanicBaseMeters korul) tuzte
        // ki - nem egy valodi kontinentalis-peremi atmenetnel. Ez adta a
        // felhasznaloi panaszt: a part "falszeruen" magasan logott a
        // tengerszint folott. 0.40-nel merve (level 6, 20 lemez,
        // world_seed=0xA7C944210000): a tile-sulyozott oceani-arany ~35.1%-ra
        // esik (messze a 65%-os viz-cel alatt), igy a percentilis-kalibracio
        // a legalacsonyabb fekvesu KONTINENTALIS tile-okba is belenyul
        // ("kontinentalis self" hatas) - a parti sav atlagos relativ
        // magassaga ~4072m-rol ~242.6m-re csokkent (94%). TEST-EARTH-001
        // valtozatlanul teljesul (65.0% viz, tobb kontinens).
        public const double DefaultOceanicProbability = 0.40;

        // ND-34: az ocean-fenek szelidebb, mint a szarazfold.
        public const double OceanicNoiseFactor = 0.25;

        // ND-34: a "hegyvidekiseg" maszk parameterei - alacsony frekvencia
        // -> nagy, regionalis zonak; a gain a maszk fBm nyers tartomanyat
        // [0,1]-hez kozelebb nyujtja; a bias-power (>1) tobbnyire sik,
        // ritkabban dramatikusan durva teruleteket ad.
        public const double MountainMaskFrequency = 2.5;
        public const int MountainMaskOctaves = 3;
        public const double MountainMaskGain = 1.3;
        public const double MountainMaskBiasPower = 1.5;

        /// <summary>Kéreg-típus lemezenként — determinisztikus Bernoulli-próba.</summary>
        public static bool IsOceanic(ulong worldSeed, int plateId, double oceanicProbability = DefaultOceanicProbability)
        {
            return DeterministicRandom.Chance(
                worldSeed, RandomDomain.Tectonics, (ulong)plateId, 0,
                oceanicProbability, RandomProperty.CrustType);
        }

        /// <summary>
        /// [0,1] regionális "hegyvidékiség" - alacsony frekvenciás, sima
        /// fBm (NEM ridged), hogy nagy, összefüggő zónákat adjon sík/durva
        /// területekre (ND-34).
        /// </summary>
        public static double MountainMask(ulong worldSeed, double x, double y, double z)
        {
            double m = FractalNoise.Fbm(worldSeed, x, y, z, MountainMaskFrequency, MountainMaskOctaves);
            double normalized = Clamp01(m * MountainMaskGain + 0.5);
            return System.Math.Pow(normalized, MountainMaskBiasPower);
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        /// <summary>A tile alap-magassága méterben: kéreg-típus bázis + térben koherens, maszkolt ridged zaj.</summary>
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

            double mask = MountainMask(worldSeed, x, y, z);
            double amplitude = NoiseAmplitudeMeters * (isOceanic ? OceanicNoiseFactor : 1.0);

            return baseValue + noise * mask * amplitude;
        }
    }
}
