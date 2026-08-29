using System;
using WorldGen.Core.Astronomy;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// M5 hőmérséklet-modell (§5.1, docs/05-milestones.md): Stefan-Boltzmann
    /// sugárzási egyensúly + üvegházhatás + lapse rate.
    ///
    /// HATÓKÖR (tudatosan szűkítve): T = T_radiative + T_greenhouse - T_altitude
    /// (T_ocean/T_weather/T_cycle halasztva, ld. milestones "M5 hatókör").
    ///
    /// T_greenhouse: a spec (§10.2) csak "derived value"-ként említi
    /// (greenhouseStrength), zárt formula nélkül — még nincs épített
    /// AtmosphereLayer modul (összetétel: CO2/H2O/stb.), ami ebből
    /// számolná. Addig egy FIX, VALÓDI CSILLAGÁSZATI/KLIMATOLÓGIAI
    /// ÉRTÉKKEL közelítjük: a Föld tényleges globális átlaghőmérséklete
    /// (~288K) és a légkör nélküli, sugárzási egyensúlyi hőmérséklete
    /// (~255K) közötti különbség kb. 33K — jól dokumentált, hivatkozható
    /// fizikai tény, nem kitalált szám. Ugyanaz a minta, mint ND-10-nél
    /// (fix Föld-szerű értékek v1.0-ban, később paraméterezhető).
    ///
    /// ND-27 LEZÁRVA: a nap-irány <see cref="OrbitalMechanics.SunDirectionBodyFrame"/>-en
    /// keresztül már <see cref="Numerics.DeterministicMath"/>-ot használ; a
    /// negyedik-gyök (T^4 egyensúly) sqrt(sqrt(x))-ként EGZAKT (nem
    /// közelítés) - nincs Math.Sin/Cos/Pow a kritikus úton.
    ///
    /// MÓDSZER a napi átlag-inszolációhoz: NEM zárt hour-angle formula (az
    /// tan(latitude)-alapú, a pólusoknál szinguláris lenne), hanem a már
    /// verifikált <see cref="OrbitalMechanics.SunDirectionBodyFrame"/> sűrű
    /// mintavételezése egy teljes forgás (nap) alatt, és a
    /// max(0, cos theta) átlaga — robusztus, nincs speciális eset a
    /// pólusoknál.
    /// </summary>
    public static class Temperature
    {
        public const double Sigma = 5.670374419e-8; // Stefan-Boltzmann állandó, W/(m^2 K^4)
        public const double DefaultFPeak = 1361.0; // W/m^2, Föld-szerű napállandó, illusztrációhoz
        public const double AlbedoOcean = 0.06;
        public const double AlbedoLand = 0.30;
        public const double LapseRateKPerM = 0.0065;
        public const int DefaultNumDaySamples = 24;
        public const double DefaultGreenhouseK = 33.0; // Föld-szerű üvegházhatás, ld. osztály-doc

        /// <summary>max(0,cos theta) átlaga egy teljes forgás (nap) alatt, sűrű mintavétellel.</summary>
        public static double DailyAverageInsolationFactor(
            double x, double y, double z, double dayT,
            double orbitalPeriod, double rotationPeriod, double axialTilt,
            double orbitalPhase0 = 0.0, double rotationPhase0 = 0.0,
            int numSamples = DefaultNumDaySamples)
        {
            double total = 0.0;
            for (int i = 0; i < numSamples; i++)
            {
                double sampleT = dayT + i * (rotationPeriod / numSamples);
                OrbitalMechanics.SunDirectionBodyFrame(
                    sampleT, orbitalPeriod, rotationPeriod, axialTilt,
                    orbitalPhase0, rotationPhase0,
                    out double sx, out double sy, out double sz);
                double cosTheta = x * sx + y * sy + z * sz;
                total += Math.Max(0.0, cosTheta);
            }
            return total / numSamples;
        }

        /// <summary>A tile hőmérséklete Kelvinben.</summary>
        public static double TemperatureKelvin(
            double x, double y, double z, double dayT,
            double orbitalPeriod, double rotationPeriod, double axialTilt,
            bool isOceanic, double elevationM, double seaLevelM,
            double orbitalPhase0 = 0.0, double rotationPhase0 = 0.0, double fPeak = DefaultFPeak,
            double greenhouseK = DefaultGreenhouseK)
        {
            double avgFactor = DailyAverageInsolationFactor(
                x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                orbitalPhase0, rotationPhase0);

            double albedo = isOceanic ? AlbedoOcean : AlbedoLand;
            double absorbed = fPeak * avgFactor * (1.0 - albedo);
            // x^0.25 = sqrt(sqrt(x)) - EGZAKT (nem közelítés), mert mindkét
            // Math.Sqrt IEEE-754 korrekt kerekítésű - jobb, mint akár a
            // DeterministicMath.Pow is tudna adni (ND-27).
            double tEq = absorbed > 0.0 ? Math.Sqrt(Math.Sqrt(absorbed / Sigma)) : 0.0;

            double heightAboveSea = Math.Max(0.0, elevationM - seaLevelM);
            double tAltitude = LapseRateKPerM * heightAboveSea;

            return tEq + greenhouseK - tAltitude;
        }
    }
}
