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
