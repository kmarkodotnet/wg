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

        // ====================================================================
        // M5 "Teljes homerseklet-modell" (ND-42, spec §28.1):
        //   T = T_radiative + T_greenhouse + T_ocean - T_altitude + T_weather + T_cycle
        // A fenti TemperatureKelvin VALTOZATLAN (ket reteg egymas mellett). A
        // Python referencia: tools/reference/temperature_ref.py
        // (temperature_kelvin_full), tesztvektorok: temperature_full_vectors.json.
        // ====================================================================

        // T_greenhouse parameterek (ND-42).
        public const double EarthGhgReferencePpm = 280.0;
        public const double GreenhouseSensitivityKPerDoubling = 3.0;

        // T_ocean (kontinentalitas-csillapitas) parameterek.
        public const double OceanBufferingStrength = 0.3;
        public const int OceanAnnualSamples = 12;

        // T_weather (§32.2 stateless idonoise placeholder) parameterek.
        public const double WeatherOffsetX = 23.17, WeatherOffsetY = -17.59, WeatherOffsetZ = 31.41;
        public const double WeatherTimeDriftX = 0.4517, WeatherTimeDriftY = -0.7392, WeatherTimeDriftZ = 0.1234;
        public const double WeatherTimeScalePerDay = 0.2;
        public const double WeatherNoiseFrequency = 3.0;
        public const int WeatherNoiseOctaves = 4;
        public const double WeatherAmplitudeK = 4.0;

        // T_cycle (Milankovic-szeru additiv oszcillacio) parameterek (§25).
        public const double CycleEccentricityPeriodYearsLo = 50_000.0, CycleEccentricityPeriodYearsHi = 500_000.0;
        public const double CycleObliquityPeriodYearsLo = 20_000.0, CycleObliquityPeriodYearsHi = 150_000.0;
        public const double CyclePrecessionPeriodYearsLo = 10_000.0, CyclePrecessionPeriodYearsHi = 50_000.0;
        public const double CycleEccentricityAmplitudeK = 2.0;
        public const double CycleObliquityAmplitudeK = 3.0;
        public const double CyclePrecessionAmplitudeK = 1.0;

        /// <summary>
        /// T_greenhouse: Fold-analog bazisertek (33K) + logaritmikus, CO2-szeru
        /// koncentracio-proxy korrekcio a referencia-koncentraciohoz kepest
        /// (ND-42). ghgPpm == referenceGhgPpm eseten a korrekcio EGZAKTUL 0
        /// (Ln(1)=0), tehat visszaesik a fix bazisertekre.
        /// </summary>
        public static double GreenhouseTemperature(
            double ghgPpm = EarthGhgReferencePpm,
            double referenceGhgPpm = EarthGhgReferencePpm,
            double greenhouseK = DefaultGreenhouseK,
            double sensitivityKPerDoubling = GreenhouseSensitivityKPerDoubling)
        {
            if (ghgPpm < 1e-6) ghgPpm = 1e-6; // Ln csak pozitivra
            double doublings = Numerics.DeterministicMath.Ln(ghgPpm / referenceGhgPpm) / Numerics.DeterministicMath.Ln(2.0);
            return greenhouseK + sensitivityKPerDoubling * doublings;
        }

        /// <summary>A radiativ egyensulyi homerseklet NYERS erteke (absorbed/sigma), a 0.25 hatvany ELOTT.</summary>
        private static double RadiativeRaw(double avgInsolationFactor, double albedo, double fPeak)
        {
            double absorbed = fPeak * avgInsolationFactor * (1.0 - albedo);
            return absorbed > 0.0 ? absorbed / Sigma : 0.0;
        }

        /// <summary>Az adott tile T_radiative-janak evi (havi felbontasu) atlaga - a T_ocean csillapitas cel-erteke.</summary>
        private static double AnnualMeanRadiativeTemperature(
            double x, double y, double z, double orbitalPeriod, double rotationPeriod, double axialTilt,
            double albedo, double fPeak, double orbitalPhase0, double rotationPhase0, int annualSamples)
        {
            double total = 0.0;
            for (int j = 0; j < annualSamples; j++)
            {
                double dayT = j * (orbitalPeriod / annualSamples);
                double factor = DailyAverageInsolationFactor(
                    x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt, orbitalPhase0, rotationPhase0);
                double raw = RadiativeRaw(factor, albedo, fPeak);
                total += raw > 0.0 ? Math.Sqrt(Math.Sqrt(raw)) : 0.0;
            }
            return total / annualSamples;
        }

        /// <summary>T_weather: §32.2 stateless idonoise (advekcios idobeli eltolas + fbm). Bekorlatozott, additiv devicio.</summary>
        public static double WeatherDeviationK(ulong worldSeed, double x, double y, double z, double dayT,
            double amplitudeK = WeatherAmplitudeK)
        {
            double drift = dayT * WeatherTimeScalePerDay;
            double wx = x + WeatherOffsetX + WeatherTimeDriftX * drift;
            double wy = y + WeatherOffsetY + WeatherTimeDriftY * drift;
            double wz = z + WeatherOffsetZ + WeatherTimeDriftZ * drift;
            double n = Terrain.FractalNoise.Fbm(worldSeed, wx, wy, wz, WeatherNoiseFrequency, WeatherNoiseOctaves);
            return amplitudeK * n;
        }

        private static double LerpRange(ulong raw, double lo, double hi)
        {
            double frac = (double)(raw % 1_000_000UL) / 1_000_000.0;
            return lo + frac * (hi - lo);
        }

        /// <summary>T_cycle: harom szinuszos, Milankovic-szeru komponens (§25) osszege, seed-fuggo periodus/fazis.</summary>
        public static double ClimateCycleTemperatureK(
            ulong worldSeed, double tYears,
            double eccentricityAmplitudeK = CycleEccentricityAmplitudeK,
            double obliquityAmplitudeK = CycleObliquityAmplitudeK,
            double precessionAmplitudeK = CyclePrecessionAmplitudeK)
        {
            Random.Block4 p = Random.Threefry4x64.Compute(0, 0, 0, 0, worldSeed, 0, 41, 0, 20);
            double eccPeriod = LerpRange(p.X0, CycleEccentricityPeriodYearsLo, CycleEccentricityPeriodYearsHi);
            double oblPeriod = LerpRange(p.X1, CycleObliquityPeriodYearsLo, CycleObliquityPeriodYearsHi);
            double precPeriod = LerpRange(p.X2, CyclePrecessionPeriodYearsLo, CyclePrecessionPeriodYearsHi);

            Random.Block4 ph = Random.Threefry4x64.Compute(1, 0, 0, 0, worldSeed, 0, 41, 0, 20);
            double eccPhase = (double)(ph.X0 % 1_000_000UL) / 1_000_000.0 * 2.0 * Math.PI;
            double oblPhase = (double)(ph.X1 % 1_000_000UL) / 1_000_000.0 * 2.0 * Math.PI;
            double precPhase = (double)(ph.X2 % 1_000_000UL) / 1_000_000.0 * 2.0 * Math.PI;

            Numerics.DeterministicMath.SinCos(2.0 * Math.PI * tYears / eccPeriod + eccPhase, out double eccSin, out _);
            Numerics.DeterministicMath.SinCos(2.0 * Math.PI * tYears / oblPeriod + oblPhase, out double oblSin, out _);
            Numerics.DeterministicMath.SinCos(2.0 * Math.PI * tYears / precPeriod + precPhase, out double precSin, out _);

            return eccentricityAmplitudeK * eccSin + obliquityAmplitudeK * oblSin + precessionAmplitudeK * precSin;
        }

        /// <summary>Spec §28.1 TELJES egyenlete. Tiszta fuggveny (nincs mutable allapot).</summary>
        public static double TemperatureKelvinFull(
            ulong worldSeed, double x, double y, double z, double dayT,
            double orbitalPeriod, double rotationPeriod, double axialTilt,
            bool isOceanic, double elevationM, double seaLevelM,
            double orbitalPhase0 = 0.0, double rotationPhase0 = 0.0, double fPeak = DefaultFPeak,
            double greenhouseK = DefaultGreenhouseK, double ghgPpm = EarthGhgReferencePpm,
            double oceanBufferingStrength = OceanBufferingStrength,
            double weatherAmplitudeK = WeatherAmplitudeK,
            double tYears = 0.0,
            double cycleEccentricityAmplitudeK = CycleEccentricityAmplitudeK,
            double cycleObliquityAmplitudeK = CycleObliquityAmplitudeK,
            double cyclePrecessionAmplitudeK = CyclePrecessionAmplitudeK)
        {
            double avgFactor = DailyAverageInsolationFactor(
                x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt, orbitalPhase0, rotationPhase0);
            double albedo = isOceanic ? AlbedoOcean : AlbedoLand;
            double raw = RadiativeRaw(avgFactor, albedo, fPeak);
            double tRadiative = raw > 0.0 ? Math.Sqrt(Math.Sqrt(raw)) : 0.0;

            double tGreenhouse = GreenhouseTemperature(ghgPpm, EarthGhgReferencePpm, greenhouseK);

            double tOcean = 0.0;
            if (isOceanic)
            {
                double annualMean = AnnualMeanRadiativeTemperature(
                    x, y, z, orbitalPeriod, rotationPeriod, axialTilt, albedo, fPeak,
                    orbitalPhase0, rotationPhase0, OceanAnnualSamples);
                tOcean = oceanBufferingStrength * (annualMean - tRadiative);
            }

            double heightAboveSea = Math.Max(0.0, elevationM - seaLevelM);
            double tAltitude = LapseRateKPerM * heightAboveSea;

            double tWeather = WeatherDeviationK(worldSeed, x, y, z, dayT, weatherAmplitudeK);
            double tCycle = ClimateCycleTemperatureK(worldSeed, tYears,
                cycleEccentricityAmplitudeK, cycleObliquityAmplitudeK, cyclePrecessionAmplitudeK);

            return tRadiative + tGreenhouse + tOcean - tAltitude + tWeather + tCycle;
        }
    }
}
