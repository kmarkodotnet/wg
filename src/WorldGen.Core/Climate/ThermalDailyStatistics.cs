using System;
using System.Collections.ObjectModel;

namespace WorldGen.Core.Climate
{
    /// <summary>Egy teljes modellnap cellánkénti, időben egyenletes hőstatisztikája (ND-144).</summary>
    public sealed class ThermalDailyStatistics
    {
        public long DayIndex { get; }
        public string ModelIdentity { get; }
        public ReadOnlyCollection<double> MeanSurfaceK { get; }
        public ReadOnlyCollection<double> MeanAirK { get; }
        public ReadOnlyCollection<double> MinSurfaceK { get; }
        public ReadOnlyCollection<double> MaxSurfaceK { get; }
        public ReadOnlyCollection<double> MinAirK { get; }
        public ReadOnlyCollection<double> MaxAirK { get; }

        internal ThermalDailyStatistics(long dayIndex, string modelIdentity, double[] meanSurfaceK,
            double[] meanAirK, double[] minSurfaceK, double[] maxSurfaceK,
            double[] minAirK, double[] maxAirK)
        {
            DayIndex = dayIndex;
            ModelIdentity = modelIdentity;
            MeanSurfaceK = Array.AsReadOnly(meanSurfaceK);
            MeanAirK = Array.AsReadOnly(meanAirK);
            MinSurfaceK = Array.AsReadOnly(minSurfaceK);
            MaxSurfaceK = Array.AsReadOnly(maxSurfaceK);
            MinAirK = Array.AsReadOnly(minAirK);
            MaxAirK = Array.AsReadOnly(maxAirK);
        }
    }

    /// <summary>
    /// A solver kanonikus pillanatnyi mezőjéből napi átlag és szélsőérték.
    /// A 96 minta a nap tickjeinek elején készül; a hívás végén az állapot
    /// a következő nap kezdőtickjén áll. Nem szálbiztos: a mezőt és az
    /// állapotot egyetlen számítás használhatja egyszerre.
    /// </summary>
    public static class ThermalDailyStatisticsCalculator
    {
        public static ThermalDailyStatistics Compute(SurfaceTemperatureField field,
            ThermalSnapshot state, long dayIndex)
            => Compute(field, state, dayIndex, null, null, null, null);

        /// <summary>
        /// Ugyanaz, de a nap 96 mintáján a CELLAKÖZÉPPONTBELI SZÉLVEKTORT és a
        /// sebességet is összegzi a megadott tömbökbe (ND-163).
        ///
        /// MIÉRT ÖSSZEGZÉS ÉS NEM ÁTLAG. A hívó több napot fűz egymás után
        /// (éves átlag), ezért az osztást EGYSZER, a végén kell elvégezni —
        /// különben a részátlagok újraátlagolása lebegőpontos hibát vinne be.
        ///
        /// A szél INGYEN jön: a <see cref="SurfaceTemperatureField.Step"/> amúgy
        /// is kiszámolja (<see cref="SurfaceTemperatureField.CaptureCellWind"/>),
        /// tehát a többi kimenet BITRE változatlan.
        /// </summary>
        public static ThermalDailyStatistics Compute(SurfaceTemperatureField field,
            ThermalSnapshot state, long dayIndex,
            double[]? windSumX, double[]? windSumY, double[]? windSumZ, double[]? speedSum)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (state == null) throw new ArgumentNullException(nameof(state));

            long firstTick = checked(dayIndex * SimulationTime.TicksPerDay);
            checked { _ = firstTick + SimulationTime.TicksPerDay; }
            field.StateAt(state, firstTick);

            int count = field.Grid.CellCount;
            var baseline = new double[count];
            var meanSurface = new double[count];
            var meanAir = new double[count];
            var minSurface = new double[count];
            var maxSurface = new double[count];
            var minAir = new double[count];
            var maxAir = new double[count];
            Array.Fill(minSurface, double.PositiveInfinity);
            Array.Fill(minAir, double.PositiveInfinity);
            Array.Fill(maxSurface, double.NegativeInfinity);
            Array.Fill(maxAir, double.NegativeInfinity);

            bool wantWind = windSumX != null;
            if (wantWind)
            {
                if (windSumY == null || windSumZ == null || speedSum == null)
                    throw new ArgumentException("A szél-összegző tömböket együtt kell megadni.", nameof(windSumX));
                if (windSumX!.Length != count || windSumY.Length != count
                    || windSumZ.Length != count || speedSum.Length != count)
                    throw new ArgumentException("A szél-összegző tömbök mérete a cellaszámmal egyezzen.", nameof(windSumX));
                if (!field.CaptureCellWind)
                    throw new ArgumentException(
                        "A szél összegzéséhez a mezőn be kell kapcsolni a CaptureCellWind-et.", nameof(field));
            }

            for (int sample = 0; sample < SimulationTime.TicksPerDay; sample++)
            {
                field.BaselineAt(state.Tick, baseline);
                for (int cell = 0; cell < count; cell++)
                {
                    double surface = baseline[cell] + state.ThetaS[cell];
                    double air = baseline[cell] + state.ThetaA[cell];
                    meanSurface[cell] += surface;
                    meanAir[cell] += air;
                    if (surface < minSurface[cell]) minSurface[cell] = surface;
                    if (surface > maxSurface[cell]) maxSurface[cell] = surface;
                    if (air < minAir[cell]) minAir[cell] = air;
                    if (air > maxAir[cell]) maxAir[cell] = air;
                }
                field.Step(state);
                if (!wantWind) continue;
                // A Step UTÁN olvassuk: a mező ekkor tartalmazza az ADOTT tick
                // középidejéhez tartozó szelet, ugyanazt, amivel a lépés számolt.
                double[] wx = field.CellWindX, wy = field.CellWindY, wz = field.CellWindZ;
                double[] speed = field.CellSpeed;
                for (int cell = 0; cell < count; cell++)
                {
                    windSumX![cell] += wx[cell];
                    windSumY![cell] += wy[cell];
                    windSumZ![cell] += wz[cell];
                    speedSum![cell] += speed[cell];
                }
            }

            for (int cell = 0; cell < count; cell++)
            {
                meanSurface[cell] /= SimulationTime.TicksPerDay;
                meanAir[cell] /= SimulationTime.TicksPerDay;
            }
            return new ThermalDailyStatistics(dayIndex, field.ModelIdentity, meanSurface,
                meanAir, minSurface, maxSurface, minAir, maxAir);
        }
    }
}
