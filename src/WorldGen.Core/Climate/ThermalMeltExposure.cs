using System;
using System.Collections.ObjectModel;

namespace WorldGen.Core.Climate
{
    /// <summary>ND-172: mintanapokból évesített levegő-foknap. Nem jégbesorolás.</summary>
    public sealed class ThermalMeltExposure
    {
        public ReadOnlyCollection<double> PositiveDegreeDays { get; }
        public ReadOnlyCollection<double> WarmestSampleDayMeanAirK { get; }
        public ReadOnlyCollection<long> SampleDays { get; }

        private ThermalMeltExposure(double[] pdd, double[] warmest, long[] days)
        {
            PositiveDegreeDays = Array.AsReadOnly(pdd);
            WarmestSampleDayMeanAirK = Array.AsReadOnly(warmest);
            SampleDays = Array.AsReadOnly(days);
        }

        public static ThermalMeltExposure Compute(SurfaceTemperatureField field, int sampleDays = 12)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            long[] days = ThermalAnnualStatisticsCalculator.SampleDayIndices(
                field.Orbit.OrbitalPeriodDays, sampleDays);
            int count = field.Grid.CellCount;
            var state = new ThermalSnapshot(count);
            var baseline = new double[count];
            var pdd = new double[count];
            var warmest = new double[count];
            var daily = new double[count];
            Array.Fill(warmest, double.NegativeInfinity);
            foreach (long day in days)
            {
                field.StateAt(state, checked(day * SimulationTime.TicksPerDay));
                Array.Clear(daily, 0, count);
                for (int tick = 0; tick < SimulationTime.TicksPerDay; tick++)
                {
                    field.BaselineAt(state.Tick, baseline);
                    for (int c = 0; c < count; c++)
                    {
                        double air = baseline[c] + state.ThetaA[c];
                        pdd[c] += TemperatureIndexSnow.PositiveDegreeDays(air,
                            1.0 / SimulationTime.TicksPerDay);
                        daily[c] += air;
                    }
                    field.Step(state);
                }
                for (int c = 0; c < count; c++)
                    warmest[c] = Math.Max(warmest[c], daily[c] / SimulationTime.TicksPerDay);
            }
            double weight = field.Orbit.OrbitalPeriodDays / days.Length;
            for (int c = 0; c < count; c++)
            {
                pdd[c] *= weight;
                if (double.IsNaN(pdd[c]) || double.IsInfinity(pdd[c]))
                    throw new OverflowException("Az éves foknap nem véges.");
            }
            return new ThermalMeltExposure(pdd, warmest, days);
        }
    }
}
