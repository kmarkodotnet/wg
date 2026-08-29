using System;
using WorldGen.Core.Random;

namespace WorldGen.Core.Events
{
    /// <summary>
    /// M11 becsapódás-események (spec §22, docs/04-decisions.md ND-28).
    ///
    /// HATÓKÖR (tudatosan szűkítve, ld. ND-28): csak becsapódás — vulkán,
    /// rift, lemez-hasadás/egyesülés halasztva. Csak kráter átmérő+mélység
    /// — rimHeight/ejectaRadius (spec §22.4) halasztva.
    ///
    /// FIZIKA — forrásból ellenőrzött állandók (ND-28 részletezi a forrásokat):
    ///   - Tranziens kráter átmérő: Schmidt &amp; Housen (1987) / Collins,
    ///     Melosh &amp; Marcus (2005) skálázás.
    ///   - Mélység: mélység/átmérő ≈ 1:5 egyszerű kráterekre.
    ///   - Gyakoriság: ρ(≥D) = 20·D^-2.4 [1/év], D méterben.
    ///   - Sebesség: 15-25 km/s.
    ///   - Szög: P(θ) ∝ sin(2θ) — geometriai tény, zárt alakban invertálva.
    ///
    /// ND-27 OSZTÁLYA KITERJESZTVE (nem új döntés): Math.Pow/Sin/Cos-t
    /// használ a kritikus úton — ugyanaz a trigonometria-kockázat és M12
    /// előtti lezárási határidő, mint a klímánál és a lemezmozgásnál.
    /// </summary>
    public static class ImpactCratering
    {
        public const double EpochYears = 10_000.0;
        public const double MinDiameterMeters = 1_000.0;
        public const double MaxDiameterMeters = 100_000.0;
        public const double ParetoAlpha = 2.4;
        public const double RateCoefficientPerYear = 20.0;
        public const double VelocityMinMetersPerSecond = 15_000.0;
        public const double VelocityMaxMetersPerSecond = 25_000.0;
        public const double ImpactorDensityKgM3 = 3000.0;
        public const double TargetDensityKgM3 = 2700.0;
        public const double GravityMetersPerSecond2 = 9.81;
        public const double DepthToDiameterRatio = 0.2;

        /// <summary>
        /// Várható eseményszám / epoch a D≥MinDiameterMeters küszöbre.
        /// Bernoulli-közelítésben használva a Poisson-ráta helyett, mert
        /// ráta &lt;&lt; 1 (dokumentált egyszerűsítés, ritka eseményekre
        /// szokásos gyakorlat).
        /// </summary>
        public static double EpochProbability()
        {
            double ratePerYear = RateCoefficientPerYear * Math.Pow(MinDiameterMeters, -ParetoAlpha);
            return ratePerYear * EpochYears;
        }

        /// <summary>Schmidt &amp; Housen (1987) / Collins, Melosh &amp; Marcus (2005) skálázás.</summary>
        public static double TransientCraterDiameter(
            double impactorDiameterMeters, double velocityMetersPerSecond, double angleRadians)
        {
            double densityRatio = Math.Pow(ImpactorDensityKgM3 / TargetDensityKgM3, 1.0 / 3.0);
            double sizeTerm = Math.Pow(impactorDiameterMeters, 0.78);
            double velocityTerm = Math.Pow(velocityMetersPerSecond, 0.44);
            double gravityTerm = Math.Pow(GravityMetersPerSecond2, -0.22);
            double angleTerm = Math.Pow(Math.Sin(angleRadians), 1.0 / 3.0);
            return 1.161 * densityRatio * sizeTerm * velocityTerm * gravityTerm * angleTerm;
        }

        /// <summary>
        /// Megkísérli legenerálni a <paramref name="epochIndex"/>-hez tartozó
        /// becsapódás-eseményt. Visszatér <c>false</c>-szal, ha ebben az
        /// epoch-ban nem történt esemény (a leggyakoribb eset).
        /// </summary>
        public static bool TryGenerateImpact(
            ulong worldSeed, long epochIndex,
            out double x, out double y, out double z,
            out double impactorDiameterMeters, out double velocityMetersPerSecond, out double angleRadians,
            out double craterDiameterMeters, out double craterDepthMeters)
        {
            x = y = z = 0.0;
            impactorDiameterMeters = velocityMetersPerSecond = angleRadians = 0.0;
            craterDiameterMeters = craterDepthMeters = 0.0;

            double probability = EpochProbability();
            ulong epochBucket = unchecked((ulong)epochIndex);

            bool occurred = DeterministicRandom.Chance(
                worldSeed, RandomDomain.Events, 0UL, epochBucket,
                probability, RandomProperty.ImpactTrigger);
            if (!occurred)
                return false;

            double uMagnitude = DeterministicRandom.Sample(
                worldSeed, RandomDomain.Events, 0UL, epochBucket, RandomProperty.ImpactMagnitude);
            double diameter = MinDiameterMeters / Math.Pow(1.0 - uMagnitude, 1.0 / ParetoAlpha);
            if (diameter > MaxDiameterMeters)
                diameter = MaxDiameterMeters; // dokumentált biztonsági sapka, nem újra-mintavétel (ND-28)

            DeterministicRandom.SampleUnitVector3(
                worldSeed, RandomDomain.Events, 0UL, epochBucket,
                out x, out y, out z, RandomProperty.ImpactPosition);

            double velocity = DeterministicRandom.SampleRange(
                worldSeed, RandomDomain.Events, 0UL, epochBucket,
                VelocityMinMetersPerSecond, VelocityMaxMetersPerSecond, RandomProperty.ImpactVelocity);

            double uAngle = DeterministicRandom.Sample(
                worldSeed, RandomDomain.Events, 0UL, epochBucket, RandomProperty.ImpactAngle);
            double angle = 0.5 * Math.Acos(1.0 - 2.0 * uAngle);

            double craterDiameter = TransientCraterDiameter(diameter, velocity, angle);

            impactorDiameterMeters = diameter;
            velocityMetersPerSecond = velocity;
            angleRadians = angle;
            craterDiameterMeters = craterDiameter;
            craterDepthMeters = craterDiameter * DepthToDiameterRatio;
            return true;
        }
    }
}
