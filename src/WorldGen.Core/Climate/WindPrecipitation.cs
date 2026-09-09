using System;
using WorldGen.Core.Terrain;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// M5 szel + nedvesseg/csapadek + idojaras-zaj (spec §30-32, ND-41).
    /// A Python referencia: tools/reference/wind_precipitation_ref.py. Dokumentaltan
    /// EGYSZERUSITETT, de determinisztikus elso kor - a spec csak minosegi
    /// komponenslistat ad, zart formula nelkul (ND-41 rogziti minden dontest).
    ///
    /// FONTOS: mint a Temperature-modul, ez is NYERS Math.Sin/Cos/Asin/Atan2/Tanh-ot
    /// hasznal (nem a DeterministicMath-ot) - ezert a Python referenciahoz csak
    /// TOLERANCIAVAL egyezik (ULP-szintu elteres), es a szigoru cross-platform
    /// bit-determinizmus itt (mint a homerseklet-lancnal) MEG NINCS garantalva.
    /// Ez egy halasztott, kesobb finomithato modul dokumentalt allapota.
    /// </summary>
    public static class WindPrecipitation
    {
        // 1. Szel (§30)
        public const double BaseWindSpeed = 10.0;
        public const double CoriolisDeflectionDeg = 30.0;
        public const double GradientEps = 1.0e-3;
        public const double ThermalWindCoeff = 0.5;
        public const double MountainDeflectionMax = 0.85;
        public const double MountainSlopeScale = 0.5;

        // 2. Parolgas (§31)
        public const double EvapFreezeK = 273.15;
        public const double EvapTempRangeK = 40.0;
        public const double EvapWindCoeff = 0.05;
        public const double EvapWindCap = 30.0;
        public const double EvapBaseRate = 5.0;

        // 3. Csapadek (§31)
        public const double OrographicCoeff = 0.85;
        public const double OrographicScale = 5.0;
        public const double PrecipCoeff = 1.0;

        // 4. Idojaras-zaj (§32) - WEATHER_TIME_DRIFT_AXIS = normalize(0.6,0.8,0) = (0.6,0.8,0)
        public const double WeatherDriftAxisX = 0.6, WeatherDriftAxisY = 0.8, WeatherDriftAxisZ = 0.0;
        public const double WeatherTimeSpeed = 0.01;
        public const double WeatherWarpStrength = 0.6;
        public const double WeatherWarpFrequency = 6.0;
        public const int WeatherWarpOctaves = 2;
        public const double WeatherNoiseFrequency = 10.0;
        public const int WeatherNoiseOctaves = 3;
        public const double WeatherPropertyOffsetPrecipX = 31.4, WeatherPropertyOffsetPrecipY = 27.1, WeatherPropertyOffsetPrecipZ = 19.9;
        public const double MaxWeatherTempDeviationK = 5.0;
        public const double MaxWeatherPrecipDeviationFraction = 0.7;

        private static double Radians(double deg) => deg * Math.PI / 180.0;
        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        /// <summary>Lokalis (kelet, eszak) erinto-egysegvektor-par (Z = polus-tengely).</summary>
        public static void LocalEastNorth(double x, double y, double z,
            out double eastX, out double eastY, out double eastZ,
            out double northX, out double northY, out double northZ)
        {
            double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, z)));
            double lon = Math.Atan2(y, x);
            double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
            double sinLon = Math.Sin(lon), cosLon = Math.Cos(lon);
            eastX = -sinLon; eastY = cosLon; eastZ = 0.0;
            northX = -sinLat * cosLon; northY = -sinLat * sinLon; northZ = cosLat;
        }

        /// <summary>Haromsavos (Hadley/Ferrel/Polar) haromszog-hullam index, [-1,1].</summary>
        public static double ZonalBandIndex(double lat)
        {
            double latDeg = Math.Abs(lat * 180.0 / Math.PI);
            if (latDeg <= 30.0)
            {
                double t = latDeg / 30.0;
                return -Math.Sin(Math.PI * t);
            }
            if (latDeg <= 60.0)
            {
                double t = (latDeg - 30.0) / 30.0;
                return Math.Sin(Math.PI * t);
            }
            double t2 = Math.Min(1.0, (latDeg - 60.0) / 30.0);
            return -Math.Sin(Math.PI * t2);
        }

        private static void Rotate2(double u, double v, double angle, out double ru, out double rv)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            ru = u * c - v * s; rv = u * s + v * c;
        }

        /// <summary>dT/d(kelet), dT/d(eszak) - kozponti veges differencia a TemperatureKelvin-bol, K/radian.</summary>
        public static void TemperatureGradientTangent(
            double x, double y, double z, double dayT, double orbitalPeriod, double rotationPeriod, double axialTilt,
            bool isOceanic, double elevationM, double seaLevelM,
            out double dEast, out double dNorth,
            double orbitalPhase0 = 0.0, double rotationPhase0 = 0.0, double eps = GradientEps)
        {
            LocalEastNorth(x, y, z, out double ex, out double ey, out double ez, out double nx, out double ny, out double nz);

            double TempAt(double px, double py, double pz)
            {
                double len = Math.Sqrt(px * px + py * py + pz * pz);
                if (len < 1e-12) { px = 0; py = 0; pz = 0; } else { px /= len; py /= len; pz /= len; }
                return Temperature.TemperatureKelvin(px, py, pz, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                    isOceanic, elevationM, seaLevelM, orbitalPhase0, rotationPhase0);
            }

            double tEastPlus = TempAt(x + ex * eps, y + ey * eps, z + ez * eps);
            double tEastMinus = TempAt(x - ex * eps, y - ey * eps, z - ez * eps);
            double tNorthPlus = TempAt(x + nx * eps, y + ny * eps, z + nz * eps);
            double tNorthMinus = TempAt(x - nx * eps, y - ny * eps, z - nz * eps);

            dEast = (tEastPlus - tEastMinus) / (2.0 * eps);
            dNorth = (tNorthPlus - tNorthMinus) / (2.0 * eps);
        }

        private static void ApplyMountainDeflection(double windE, double windN, double gradE, double gradN,
            double maxFraction, double slopeScale, out double outE, out double outN)
        {
            double slopeMag = Math.Sqrt(gradE * gradE + gradN * gradN);
            if (slopeMag < 1e-12) { outE = windE; outN = windN; return; }
            double uphillE = gradE / slopeMag, uphillN = gradN / slopeMag;
            double windAlongUphill = windE * uphillE + windN * uphillN;
            if (windAlongUphill <= 0.0) { outE = windE; outN = windN; return; }
            double deflectFraction = maxFraction * Math.Tanh(slopeMag / slopeScale);
            double reduction = deflectFraction * windAlongUphill;
            outE = windE - reduction * uphillE;
            outN = windN - reduction * uphillN;
        }

        /// <summary>A tile WindVector-je: (kelet, eszak) komponens + 3D erinto-vektor.</summary>
        public static void WindVector(
            double x, double y, double z, double dayT, double orbitalPeriod, double rotationPeriod, double axialTilt,
            bool isOceanic, double elevationM, double seaLevelM,
            double elevationGradientEast, double elevationGradientNorth,
            out double windEast, out double windNorth, out double wind3dX, out double wind3dY, out double wind3dZ,
            double orbitalPhase0 = 0.0, double rotationPhase0 = 0.0,
            double baseWindSpeed = BaseWindSpeed, double coriolisDeflectionDeg = CoriolisDeflectionDeg,
            double thermalWindCoeff = ThermalWindCoeff, double mountainDeflectionMax = MountainDeflectionMax,
            double mountainSlopeScale = MountainSlopeScale)
        {
            double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, z)));

            double baseEast = baseWindSpeed * ZonalBandIndex(lat);
            double baseNorth = 0.0;

            TemperatureGradientTangent(x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                isOceanic, elevationM, seaLevelM, out double gradE, out double gradN, orbitalPhase0, rotationPhase0);
            double thermalERaw = gradE * thermalWindCoeff;
            double thermalNRaw = gradN * thermalWindCoeff;

            // math.copysign(1.0, lat): lat>=+0 -> +1, lat<0 -> -1 (netstandard2.1-ben nincs Math.CopySign)
            double coriolisAngle = -(lat < 0.0 ? -1.0 : 1.0) * Radians(coriolisDeflectionDeg);
            Rotate2(thermalERaw, thermalNRaw, coriolisAngle, out double thermalE, out double thermalN);

            double combinedE = baseEast + thermalE;
            double combinedN = baseNorth + thermalN;

            ApplyMountainDeflection(combinedE, combinedN, elevationGradientEast, elevationGradientNorth,
                mountainDeflectionMax, mountainSlopeScale, out windEast, out windNorth);

            LocalEastNorth(x, y, z, out double ex, out double ey, out double ez, out double nx, out double ny, out double nz);
            wind3dX = windEast * ex + windNorth * nx;
            wind3dY = windEast * ey + windNorth * ny;
            wind3dZ = windEast * ez + windNorth * nz;
        }

        /// <summary>Evaporation = f(homerseklet, szel, felszini viz), §31. Monoton no, fagypont alatt 0.</summary>
        public static double Evaporation(double temperatureK, double windSpeed, double surfaceWaterFraction, double baseRate = EvapBaseRate)
        {
            double waterAvail = Clamp01(surfaceWaterFraction);
            double tempFactor = Clamp01((temperatureK - EvapFreezeK) / EvapTempRangeK);
            double windFactor = 1.0 + EvapWindCoeff * Math.Min(Math.Max(0.0, windSpeed), EvapWindCap);
            return baseRate * waterAvail * tempFactor * windFactor;
        }

        /// <summary>Csapadek = (bejovo nedvesseg + lokalis parolgas) * orografikus tenyezo (uplift/rain shadow).</summary>
        public static double Precipitation(
            double evaporationLocal, double incomingMoisture, double windEast, double windNorth,
            double elevationGradientEast, double elevationGradientNorth,
            double orographicCoeff = OrographicCoeff, double orographicScale = OrographicScale, double precipCoeff = PrecipCoeff)
        {
            double moistureAvailable = Math.Max(0.0, incomingMoisture) + Math.Max(0.0, evaporationLocal);
            double uplift = windEast * elevationGradientEast + windNorth * elevationGradientNorth;
            double orographicFactor = 1.0 + orographicCoeff * Math.Tanh(uplift / orographicScale);
            if (orographicFactor < 0.0) orographicFactor = 0.0;
            return precipCoeff * moistureAvailable * orographicFactor;
        }

        /// <summary>W(p,t) = Noise(Warp(p,t), seed) - stateless idonoise (§32), verifikalt WarpPosition/Fbm.</summary>
        public static double WeatherNoiseRaw(ulong worldSeed, double x, double y, double z, double t,
            double propertyOffsetX = 0.0, double propertyOffsetY = 0.0, double propertyOffsetZ = 0.0)
        {
            double sx = x + WeatherDriftAxisX * (WeatherTimeSpeed * t);
            double sy = y + WeatherDriftAxisY * (WeatherTimeSpeed * t);
            double sz = z + WeatherDriftAxisZ * (WeatherTimeSpeed * t);
            double len = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (len < 1e-12) { sx = 0; sy = 0; sz = 0; } else { sx /= len; sy /= len; sz /= len; }

            DomainWarp.WarpPosition(worldSeed, sx, sy, sz, out double wx, out double wy, out double wz,
                WeatherWarpStrength, WeatherWarpFrequency, WeatherWarpOctaves);
            return FractalNoise.Fbm(worldSeed, wx + propertyOffsetX, wy + propertyOffsetY, wz + propertyOffsetZ,
                WeatherNoiseFrequency, WeatherNoiseOctaves);
        }

        /// <summary>§32.3: korlatos homerseklet-deviacio a klima-atlag korul (tanh-tel korlatozva).</summary>
        public static double WeatherTemperatureDeviationK(ulong worldSeed, double x, double y, double z, double t,
            double maxDeviationK = MaxWeatherTempDeviationK)
        {
            double raw = WeatherNoiseRaw(worldSeed, x, y, z, t, 0.0, 0.0, 0.0);
            return maxDeviationK * Math.Tanh(raw);
        }

        public static double WeatherPrecipitationMultiplier(ulong worldSeed, double x, double y, double z, double t,
            double maxDeviationFraction = MaxWeatherPrecipDeviationFraction)
        {
            double raw = WeatherNoiseRaw(worldSeed, x, y, z, t,
                WeatherPropertyOffsetPrecipX, WeatherPropertyOffsetPrecipY, WeatherPropertyOffsetPrecipZ);
            return 1.0 + maxDeviationFraction * Math.Tanh(raw);
        }

        /// <summary>CurrentTemperature = ClimateMeanTemperature + WeatherDeviation (§32.3).</summary>
        public static double CurrentTemperatureK(double climateMeanTemperatureK, ulong worldSeed, double x, double y, double z, double t)
            => climateMeanTemperatureK + WeatherTemperatureDeviationK(worldSeed, x, y, z, t);

        public static double CurrentPrecipitation(double climateMeanPrecipitation, ulong worldSeed, double x, double y, double z, double t)
            => climateMeanPrecipitation * WeatherPrecipitationMultiplier(worldSeed, x, y, z, t);
    }
}
