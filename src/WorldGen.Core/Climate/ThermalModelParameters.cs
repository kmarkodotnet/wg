using System;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// A pillanatnyi hőmodell (ND-100) verziózott együtthatói. Az alapértékek
    /// forrásait a docs/reviews/thermal-parameters-sources-2026-09-13.md
    /// tartalmazza; az M1–M10 modellválasztásokat a felhasználó 2026-09-13-án
    /// jóváhagyta. Ideiglenes, még megerősítendő: M11 (szélcsend-alsóhatár a
    /// hőcserében), M12 (édesvíz/jég hőkapacitása) és M13 (radiatív simítás a
    /// bázisban és a szél hőmérsékletében).
    ///
    /// A származtatott értékek a Python-referenciával
    /// (tools/reference/thermal_field_ref.py) azonos műveleti sorrendben
    /// készülnek.
    /// </summary>
    public sealed class ThermalModelParameters
    {
        public const int ModelVersion = 2;

        public static readonly ThermalModelParameters Default = new ThermalModelParameters();

        private readonly double[] _albedo;
        private readonly double[] _emissivity;
        private readonly double[] _surfaceHeatCapacity;

        public double SolarConstant { get; }
        public double AirHeatCapacity { get; }
        public double AirRelaxation { get; }
        public double ExchangePerMetrePerSecond { get; }
        public double MinExchangeWindMs { get; }

        /// <summary>
        /// β az <c>f_eff = (1 − β)·f_napi + β·f_éves</c> radiatív faktorban (M13).
        /// </summary>
        public double RadiativeSmoothing { get; }

        public ThermalModelParameters(
            double landAlbedo = 0.30,
            double oceanAlbedo = 0.06,
            double freshwaterAlbedo = 0.06,
            double iceAlbedo = 0.6,
            double waterEmissivity = 0.96,
            double landEmissivity = 0.95,
            double seawaterDensity = 1025.0,
            double seawaterSpecificHeat = 3991.86795711963,
            double oceanDepthM = 10.0,
            double soilDryDensity = 1400.0,
            double soilSpecificHeatRatio = 0.19,
            double calorieJoulesPerKgK = 4186.8,
            double landDepthM = 0.5,
            double airDensity = 1.225,
            double airSpecificHeat = 1005.0,
            double airColumnM = 1000.0,
            double transferCoefficient = 1.15e-3,
            double airRelaxationDays = 4.0,
            double minExchangeWindMs = 1.0,
            double radiativeSmoothing = 0.5,
            double solarConstant = Temperature.DefaultFPeak)
        {
            RequirePositive(seawaterDensity, nameof(seawaterDensity));
            RequirePositive(seawaterSpecificHeat, nameof(seawaterSpecificHeat));
            RequirePositive(oceanDepthM, nameof(oceanDepthM));
            RequirePositive(soilDryDensity, nameof(soilDryDensity));
            RequirePositive(soilSpecificHeatRatio, nameof(soilSpecificHeatRatio));
            RequirePositive(calorieJoulesPerKgK, nameof(calorieJoulesPerKgK));
            RequirePositive(landDepthM, nameof(landDepthM));
            RequirePositive(airDensity, nameof(airDensity));
            RequirePositive(airSpecificHeat, nameof(airSpecificHeat));
            RequirePositive(airColumnM, nameof(airColumnM));
            RequirePositive(transferCoefficient, nameof(transferCoefficient));
            RequirePositive(airRelaxationDays, nameof(airRelaxationDays));
            RequirePositive(minExchangeWindMs, nameof(minExchangeWindMs));
            RequirePositive(solarConstant, nameof(solarConstant));
            RequireUnit(landAlbedo, nameof(landAlbedo));
            RequireUnit(oceanAlbedo, nameof(oceanAlbedo));
            RequireUnit(freshwaterAlbedo, nameof(freshwaterAlbedo));
            RequireUnit(iceAlbedo, nameof(iceAlbedo));
            RequireUnit(waterEmissivity, nameof(waterEmissivity));
            RequireUnit(landEmissivity, nameof(landEmissivity));
            RequireUnit(radiativeSmoothing, nameof(radiativeSmoothing));

            double oceanCs = seawaterDensity * seawaterSpecificHeat * oceanDepthM;
            double soilSpecificHeat = soilSpecificHeatRatio * calorieJoulesPerKgK;
            double landCs = soilDryDensity * soilSpecificHeat * landDepthM;

            _albedo = new[] { landAlbedo, oceanAlbedo, freshwaterAlbedo, iceAlbedo };
            _emissivity = new[] { landEmissivity, waterEmissivity, waterEmissivity, landEmissivity };
            _surfaceHeatCapacity = new[] { landCs, oceanCs, oceanCs, landCs };

            SolarConstant = solarConstant;
            AirHeatCapacity = airDensity * airSpecificHeat * airColumnM;
            AirRelaxation = AirHeatCapacity / (airRelaxationDays * 86400.0);
            ExchangePerMetrePerSecond = airDensity * airSpecificHeat * transferCoefficient;
            MinExchangeWindMs = minExchangeWindMs;
            RadiativeSmoothing = radiativeSmoothing;
        }

        public double Albedo(SurfaceThermalKind kind) => _albedo[(int)kind];
        public double Emissivity(SurfaceThermalKind kind) => _emissivity[(int)kind];
        public double SurfaceHeatCapacity(SurfaceThermalKind kind) => _surfaceHeatCapacity[(int)kind];

        /// <summary><c>(1 − β)·daily + β·annual</c>, a Python-referenciával azonos sorrendben.</summary>
        public double EffectiveFactor(double dailyFactor, double annualFactor)
            => (1.0 - RadiativeSmoothing) * dailyFactor + RadiativeSmoothing * annualFactor;

        private static void RequirePositive(double value, string name)
        {
            if (!(value > 0.0) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "Pozitív, véges érték szükséges.");
        }

        private static void RequireUnit(double value, string name)
        {
            if (!(value >= 0.0 && value <= 1.0))
                throw new ArgumentOutOfRangeException(name, "A 0..1 tartományba eső érték szükséges.");
        }
    }
}
