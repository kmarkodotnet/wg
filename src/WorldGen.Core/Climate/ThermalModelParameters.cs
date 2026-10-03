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
        public const int ModelVersion = 6;

        // ND-174/175: az aktív bázis szezonális EBM; a régi beta/albedó-proxy csak Legacy módban hat.
        public static readonly ThermalModelParameters Default = new ThermalModelParameters();
        public static readonly ThermalModelParameters Legacy = new ThermalModelParameters(useSeasonalEnergyBalance: false);

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
        /// <summary>ND-142: az anomáliagradiens szél-visszacsatolásának 0..1 erőssége.</summary>
        public double AirFeedbackStrength { get; }

        /// <summary>
        /// ND-168: a 0,555 W/(m² K) meridionális diffúziós együttható
        /// diagnosztikai skálája. Alapértéke 1; nulla értéknél nincs
        /// meridionális hőszállítás.
        /// </summary>
        public double MeridionalTransportScale { get; }
        public bool UseSeasonalEnergyBalance { get; }
        public int SeasonalPhases { get; }

        /// <summary>
        /// A BÁZIS radiatív tagjának albedója. <c>null</c> (az alapértelmezés)
        /// esetén a bázis az ND-160 szerinti BOLYGÓ-albedót
        /// (<see cref="Temperature.AlbedoPlanet"/>) használja — kivéve, ha
        /// <see cref="LegacySurfaceBaselineAlbedo"/> igaz, ami az ND-160 ELŐTTI,
        /// felszíni albedós bázist adja vissza bitre (kizárólag A/B-mérésre).
        ///
        /// MIÉRT NEM A FELSZÍNI ALBEDÓ (ND-160, LEZÁRVA, (1) opció). A bázis
        /// képlete BOLYGÓ-energiamérleg, és a hozzáadott +33 K üvegház-eltolás a
        /// 255 K-es, a ≈ 0,30-as BOLYGÓ-albedós egyensúlyhoz van kalibrálva.
        /// A felszíni albedóval számolt radiatív tag ezért +10,3 K globális
        /// többletet adott (a medián éves átlag +33,7 °C volt a Föld ~+15 °C-ja
        /// helyett), és a hiba VILÁGFÜGGŐ volt: a szárazföld-arány szabta meg.
        ///
        /// Az érték kizárólag a BÁZISRA hat; a tickenkénti anomália-tag
        /// (<see cref="Albedo"/>) továbbra is a felszíni albedót használja —
        /// ott az a fizikailag helyes mennyiség (ND-160, „a SOLVER nem hibás").
        /// </summary>
        public double? BaselineAlbedo { get; }

        /// <summary>
        /// A/B-KAMPÓ: az ND-160 ELŐTTI bázis-albedó (óceánon
        /// <see cref="Temperature.AlbedoOcean"/>, egyébként
        /// <see cref="Temperature.AlbedoLand"/>). Kizárólag a döntés
        /// visszamérésére; a szimuláció alapértelmezésben nem ezt használja.
        /// </summary>
        public bool LegacySurfaceBaselineAlbedo { get; }

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
            double solarConstant = Temperature.DefaultFPeak,
            double airFeedbackStrength = 0.1,
            double? baselineAlbedo = null,
            bool legacySurfaceBaselineAlbedo = false,
            double meridionalTransportScale = 1.0,
            bool useSeasonalEnergyBalance = true,
            int seasonalPhases = SeasonalEnergyBalance.DefaultPhases)
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
            RequireUnit(airFeedbackStrength, nameof(airFeedbackStrength));
            RequireUnit(meridionalTransportScale, nameof(meridionalTransportScale));
            if (seasonalPhases < 4 || seasonalPhases % 2 != 0 || seasonalPhases > 384)
                throw new ArgumentOutOfRangeException(nameof(seasonalPhases));
            if (baselineAlbedo.HasValue) RequireUnit(baselineAlbedo.Value, nameof(baselineAlbedo));

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
            AirFeedbackStrength = airFeedbackStrength;
            MeridionalTransportScale = meridionalTransportScale;
            UseSeasonalEnergyBalance = useSeasonalEnergyBalance;
            SeasonalPhases = seasonalPhases;
            BaselineAlbedo = baselineAlbedo;
            LegacySurfaceBaselineAlbedo = legacySurfaceBaselineAlbedo;
        }

        /// <summary>A bázis radiatív tagjának albedója egy felszíntípusra (ND-160).</summary>
        /// <remarks>
        /// A <paramref name="kind"/> csak a LEGACY ágon számít: az ND-160 utáni
        /// bázis felszíntípus-független, mert bolygó-albedóval dolgozik. A
        /// paraméter azért marad, hogy az A/B-ág bitre visszaadható legyen.
        ///
        /// A konstansok SZÁNDÉKOSAN a <see cref="Temperature"/>-ből jönnek, nem
        /// az <see cref="Albedo"/> paraméterezett értékeiből: a bázis eddig is
        /// bedrótozva ezeket használta.
        /// </remarks>
        public double BaselineAlbedoFor(SurfaceThermalKind kind)
            => BaselineAlbedo ?? (LegacySurfaceBaselineAlbedo
                ? (kind == SurfaceThermalKind.Ocean ? Temperature.AlbedoOcean : Temperature.AlbedoLand)
                : Temperature.AlbedoPlanet);

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
