using System;

namespace WorldGen.Core.Climate
{
    /// <summary>ND-172: explicit hótároló; minden vízmennyiség m vízegyenérték.</summary>
    public static class TemperatureIndexSnow
    {
        public const double ReferenceSnowMeltFactor = 0.003;

        /// <summary>Pozitív foknap (K·nap); egy nap 86400 SI-másodperc.</summary>
        public static double PositiveDegreeDays(double airK, double durationDays)
        {
            Validate(airK, nameof(airK));
            Validate(durationDays, nameof(durationDays));
            return Checked(Math.Max(airK - 273.15, 0.0) * durationDays);
        }

        public static double MeltPotential(double positiveDegreeDays, double meltFactor)
        {
            Validate(positiveDegreeDays, nameof(positiveDegreeDays));
            Validate(meltFactor, nameof(meltFactor));
            return Checked(positiveDegreeDays * meltFactor);
        }

        /// <summary>A havazás a lépés elején érkezik; a készlet korlátozza az olvadást.</summary>
        public static SnowMeltResult Step(double snow, double snowfall,
            double positiveDegreeDays, double meltFactor)
        {
            Validate(snow, nameof(snow));
            Validate(snowfall, nameof(snowfall));
            double available = Checked(snow + snowfall);
            double potential = MeltPotential(positiveDegreeDays, meltFactor);
            double melt = Math.Min(available, potential);
            return new SnowMeltResult(available - melt, melt, potential);
        }

        private static void Validate(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }

        private static double Checked(double value)
        {
            if (double.IsInfinity(value)) throw new OverflowException("A hómérleg túlcsordult.");
            return value;
        }
    }

    public readonly struct SnowMeltResult
    {
        public double RemainingWaterEquivalentM { get; }
        public double MeltWaterEquivalentM { get; }
        public double PotentialWaterEquivalentM { get; }

        internal SnowMeltResult(double remaining, double melt, double potential)
        {
            RemainingWaterEquivalentM = remaining;
            MeltWaterEquivalentM = melt;
            PotentialWaterEquivalentM = potential;
        }
    }
}
