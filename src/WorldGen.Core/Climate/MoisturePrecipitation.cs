using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// Nedvesség-transzport → csapadék-mező (M5, spec §30-31). A
    /// <see cref="WindPrecipitation"/> a szelet, párolgást és a PER-TILE
    /// <c>Precipitation</c> függvényt adja, de az <c>incomingMoisture</c> ott
    /// KÜLSŐ bemenet - hiányzik a nedvességet az óceánok fölül a szél mentén a
    /// szárazföld fölé vivő ADVEKCIÓ (enélkül a szárazföldi csapadék ~0). Ez a
    /// modul pótolja: determinisztikus, iteratív rács-advekció a már validált
    /// cubed-sphere tile-gráfon. Python-referencia:
    /// tools/reference/moisture_transport_ref.py.
    ///
    /// A lánc a <see cref="WindPrecipitation.WindVector"/>-on át NYERS
    /// Math.Sin/Cos-t használ (mint a teljes wind/temperature lánc) → NEM
    /// bit-egzakt cross-platform; a vektor-teszt TOLERANCIÁVAL mér.
    ///
    /// A modell rendezés-FÜGGETLEN: minden tile a 4 szomszédjára ÖSSZEGEZ
    /// (kifolyás-súly + orografikus emelkedés), ezért a szomszéd-irányok
    /// sorrendje (Python DIRECTIONS vs C# TileDirection) NEM befolyásolja az
    /// eredményt - csak a szomszédok HALMAZA (az KAT-validált neighbor-tábla).
    ///
    /// A konstansok MVP-értékek (vizuális kalibrálást igényelnek, mint az ND-41
    /// többi konstansa).
    /// </summary>
    public static class MoisturePrecipitation
    {
        public const int DefaultIterations = 24;
        public const double DefaultPrecipBaseFraction = 0.05;
        public const double DefaultOrographicCoeff = 2.0;
        public const double DefaultOrographicElevScale = 1000.0;
        public const double DefaultTargetWaterFraction = 0.65;

        public sealed class PrecipitationField
        {
            public Dictionary<TileId, double> Precipitation = new Dictionary<TileId, double>();
            public Dictionary<TileId, double> Elevation = new Dictionary<TileId, double>();
            public Dictionary<TileId, bool> IsOcean = new Dictionary<TileId, bool>();
            public double SeaLevel;
        }

        public static PrecipitationField Compute(
            ulong worldSeed, int plateCount, int level,
            double dayT = 0.0, double orbitalPeriodDays = 365.25, double rotationPeriodDays = 1.0,
            double axialTiltDegrees = 23.44, int iterations = DefaultIterations,
            double precipBaseFraction = DefaultPrecipBaseFraction,
            double orographicCoeff = DefaultOrographicCoeff, double orographicElevScale = DefaultOrographicElevScale,
            double targetWaterFraction = DefaultTargetWaterFraction)
        {
            Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(worldSeed, plateCount, level);
            double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, targetWaterFraction);
            Dictionary<TileId, bool> isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
            double axialTilt = axialTiltDegrees * Math.PI / 180.0;

            int n = 1 << level;
            var keys = new List<TileId>(6 * n * n);
            for (int face = 0; face <= 5; face++)
                for (uint u = 0; u < n; u++)
                    for (uint v = 0; v < n; v++)
                        keys.Add(TileId.FromFaceLevelUV(face, level, u, v));

            var neighborsOf = new Dictionary<TileId, TileId[]>(keys.Count);
            var weightsOf = new Dictionary<TileId, double[]>(keys.Count);
            var precipFracOf = new Dictionary<TileId, double>(keys.Count);
            var evapSourceOf = new Dictionary<TileId, double>(keys.Count);

            foreach (TileId k in keys)
            {
                TileGeometry.ToPosition(k, out double x, out double y, out double z);
                double elev = field[k];
                bool oc = isOcean[k];

                double temp = Temperature.TemperatureKelvin(
                    x, y, z, dayT, orbitalPeriodDays, rotationPeriodDays, axialTilt, oc, elev, seaLevel);
                WindPrecipitation.WindVector(
                    x, y, z, dayT, orbitalPeriodDays, rotationPeriodDays, axialTilt, oc, elev, seaLevel, 0.0, 0.0,
                    out double we, out double wn, out double w3x, out double w3y, out double w3z);
                double speed = Math.Sqrt(we * we + wn * wn);
                evapSourceOf[k] = WindPrecipitation.Evaporation(temp, speed, oc ? 1.0 : 0.0);

                var nbs = new TileId[4];
                var weights = new double[4];
                double total = 0.0;
                for (int d = 0; d < 4; d++)
                {
                    TileId nb = TileNeighbors.Neighbor(k, (TileDirection)d);
                    nbs[d] = nb;
                    TileGeometry.ToPosition(nb, out double nx, out double ny, out double nz);
                    double dx = nx - x, dy = ny - y, dz = nz - z;
                    double rad = dx * x + dy * y + dz * z;
                    double tx = dx - rad * x, ty = dy - rad * y, tz = dz - rad * z;
                    double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                    double wdot = 0.0;
                    if (len >= 1e-12)
                    {
                        tx /= len; ty /= len; tz /= len;
                        wdot = w3x * tx + w3y * ty + w3z * tz;
                    }
                    double outw = wdot > 0.0 ? wdot : 0.0;
                    weights[d] = outw;
                    total += outw;
                }

                double uplift = 0.0;
                if (total > 0.0)
                {
                    for (int d = 0; d < 4; d++) weights[d] /= total;
                    for (int d = 0; d < 4; d++) uplift += weights[d] * (field[nbs[d]] - elev);
                }
                neighborsOf[k] = nbs;
                weightsOf[k] = weights;

                double pf = precipBaseFraction * (1.0 + orographicCoeff * (uplift / orographicElevScale));
                precipFracOf[k] = pf < 0.0 ? 0.0 : (pf > 1.0 ? 1.0 : pf);
            }

            var moisture = new Dictionary<TileId, double>(keys.Count);
            foreach (TileId k in keys) moisture[k] = 0.0;

            for (int iter = 0; iter < iterations; iter++)
            {
                var newM = new Dictionary<TileId, double>(keys.Count);
                foreach (TileId k in keys) newM[k] = 0.0;
                foreach (TileId k in keys)
                {
                    double m = moisture[k];
                    if (isOcean[k]) m += evapSourceOf[k];
                    m -= m * precipFracOf[k];
                    double[] weights = weightsOf[k];
                    TileId[] nbs = neighborsOf[k];
                    double distributed = 0.0;
                    for (int d = 0; d < 4; d++)
                    {
                        double w = weights[d];
                        if (w > 0.0)
                        {
                            newM[nbs[d]] += m * w;
                            distributed += m * w;
                        }
                    }
                    newM[k] += m - distributed;
                }
                moisture = newM;
            }

            var result = new PrecipitationField { SeaLevel = seaLevel, Elevation = field, IsOcean = isOcean };
            foreach (TileId k in keys)
                result.Precipitation[k] = moisture[k] * precipFracOf[k];
            return result;
        }
    }
}
