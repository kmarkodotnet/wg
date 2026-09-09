using System;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M10 deep-time: erozios felhalmozodas (exponencialis relaxacio) +
    /// eljegesedes-ciklusok (periodikus globalis homerseklet-forcing). ND-44.
    /// Python referencia: tools/reference/erosion_glaciation_deep_time_ref.py.
    ///
    /// TIMESTEP-INVARIANCIA (ND-04): a relaxacio ZART ALAKU (exp), NEM iterativ
    /// integrator - tetszoleges idofelbontasban lancolva ugyanazt adja (a
    /// felcsoport-tulajdonsag miatt), tehat lepeskoztol fuggetlen.
    ///
    /// MEGJEGYZES (mint a Temperature/WindPrecipitation): a modul nyers Math.Exp/
    /// Math.Sin-t hasznal (a referencia math.exp/sin-jenek megfeleloen), ezert a
    /// cross-platform szigoru bit-determinizmus itt MEG NINCS garantalva (a
    /// DeterministicMath.Exp/Sin-re valtas + vektor-regeneralas kesobbi lepes).
    /// </summary>
    public static class DeepTimeErosionGlaciation
    {
        // 1. Erozios relaxacio
        public const double OrogenicRelaxationTauMyr = 50.0;
        public const double EquilibriumFraction = 0.35;

        // 2. Eljegesedes
        public const double GlaciationPeriodMyr = 150.0;
        public const double GlaciationAmplitudeK = 6.0;
        public const double IceThresholdK = 273.15;
        public const double TEquatorK = 300.0;
        public const double LatitudeTempGradientKPerRad = 38.2;

        /// <summary>H(t) = H_eq + (H0 - H_eq) * exp(-t/tau) - a dH/dt=(1/tau)(H_eq-H) ODE zart megoldasa (FIX H_eq).</summary>
        public static double RelaxTowards(double h0, double hEq, double timeMyr, double tau)
            => hEq + (h0 - hEq) * Math.Exp(-timeMyr / tau);

        /// <summary>A hegyseg-relief relaxacioja: H_eq = eqFraction*upliftStatic, H0 = upliftStatic. t=0 -&gt; upliftStatic.</summary>
        public static double UpliftRelaxationElevation(double upliftBonusStatic, double timeMyr,
            double tau = OrogenicRelaxationTauMyr, double eqFraction = EquilibriumFraction)
        {
            double hEq = eqFraction * upliftBonusStatic;
            return RelaxTowards(upliftBonusStatic, hEq, timeMyr, tau);
        }

        /// <summary>A tile elevacioja timeMyr-nel: statikus alap-elevacio + a lemezhatar uplift-bonusz relaxalt erteke. t=0 = statikus M4.</summary>
        public static double ElevationAtTime(
            ulong worldSeed, int plateId, double x, double y, double z, (double X, double Y, double Z)[] seeds,
            double timeMyr, out bool isOceanic,
            double tau = OrogenicRelaxationTauMyr, double eqFraction = EquilibriumFraction)
        {
            double baseElev = CrustElevation.BaseElevation(worldSeed, plateId, x, y, z, out isOceanic);
            double upliftStatic = PlateBoundaryEffect.BoundaryUplift(worldSeed, x, y, z, seeds);
            double upliftT = UpliftRelaxationElevation(upliftStatic, timeMyr, tau, eqFraction);
            return baseElev + upliftT;
        }

        /// <summary>Ugyanaz a zart formula n_steps darab reszidokozre lancolva (a H_eq FIX, az EREDETI h0-bol) - timestep-invariancia bizonyitas.</summary>
        public static double ChainRelaxation(double h0, double totalTimeMyr, int nSteps,
            double tau = OrogenicRelaxationTauMyr, double eqFraction = EquilibriumFraction)
        {
            double hEq = eqFraction * h0;
            double dt = totalTimeMyr / nSteps;
            double h = h0;
            for (int i = 0; i < nSteps; i++) h = RelaxTowards(h, hEq, dt, tau);
            return h;
        }

        /// <summary>Periodikus globalis homerseklet-forcing (szinuszos, zart t-ben). t=0 (phase0=0) -&gt; 0.</summary>
        public static double GlobalTempOffset(double timeMyr,
            double period = GlaciationPeriodMyr, double amplitude = GlaciationAmplitudeK, double phase0 = 0.0)
            => amplitude * Math.Sin(2.0 * Math.PI * timeMyr / period + phase0);

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Az abszolut szelesseg (rad), ahol az idealizalt profil pont IceThresholdK - ezen felul (polus fele) jeg. [0, pi/2].</summary>
        public static double IceLineAbsLatitude(double timeMyr)
        {
            double raw = (TEquatorK + GlobalTempOffset(timeMyr) - IceThresholdK) / LatitudeTempGradientKPerRad;
            return Clamp(raw, 0.0, Math.PI / 2.0);
        }

        public static bool IsIced(double absLatitudeRad, double timeMyr)
            => absLatitudeRad >= IceLineAbsLatitude(timeMyr);
    }
}
