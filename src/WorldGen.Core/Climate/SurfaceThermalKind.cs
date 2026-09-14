namespace WorldGen.Core.Climate
{
    /// <summary>
    /// Termikus felszíntípus a pillanatnyi hőmodellhez (ND-103). A numerikus
    /// értékek rögzítettek: a tesztvektorok és a paramétertáblák indexként
    /// használják őket.
    /// </summary>
    public enum SurfaceThermalKind : byte
    {
        Land = 0,
        Ocean = 1,
        Freshwater = 2,
        Ice = 3,
    }

    /// <summary>A hőmodellhez használt körpálya és forgás (napokban, radiánban).</summary>
    public readonly struct ThermalOrbit
    {
        public readonly double OrbitalPeriodDays;
        public readonly double RotationPeriodDays;
        public readonly double AxialTiltRad;

        public ThermalOrbit(double orbitalPeriodDays, double rotationPeriodDays, double axialTiltRad)
        {
            if (!(orbitalPeriodDays > 0.0)) throw new System.ArgumentOutOfRangeException(nameof(orbitalPeriodDays));
            if (!(rotationPeriodDays > 0.0)) throw new System.ArgumentOutOfRangeException(nameof(rotationPeriodDays));
            OrbitalPeriodDays = orbitalPeriodDays;
            RotationPeriodDays = rotationPeriodDays;
            AxialTiltRad = axialTiltRad;
        }
    }
}
