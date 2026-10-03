using System;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Climate
{
    /// <summary>ND-174: periodikus szezonális bázis, effektív bolygó-albedóval.</summary>
    public sealed class SeasonalEnergyBalance
    {
        public const int DefaultPhases = 48;
        public const double OutgoingInterceptWm2 = 210.0;
        public const double FeedbackWm2K = 2.09;
        private readonly double[][] _temperatureC;
        public int PhaseCount => _temperatureC.Length;
        public double OrbitalPeriodDays { get; }
        public double[] AnnualMeanK { get; }
        public double[] AnnualTargetK { get; }
        public double MaxResidualWm2 { get; }

        public SeasonalEnergyBalance(DenseGridMetrics grid, SurfaceThermalKind[] kinds, double[] elevation,
            double seaLevel, double cycleK, ThermalOrbit orbit, ThermalModelParameters parameters,
            int phases = DefaultPhases)
        {
            if (grid == null || kinds == null || elevation == null || parameters == null)
                throw new ArgumentNullException("Hiányos szezonális bemenet.");
            if (kinds.Length != grid.CellCount || elevation.Length != grid.CellCount)
                throw new ArgumentException("Eltérő szezonális cellaszám.");
            if (phases < 4 || phases % 2 != 0) throw new ArgumentOutOfRangeException(nameof(phases));
            int count = grid.CellCount;
            OrbitalPeriodDays = orbit.OrbitalPeriodDays;
            var capacity = new double[count];
            var forcing = new double[phases][];
            AnnualMeanK = new double[count]; AnnualTargetK = new double[count];
            for (int c = 0; c < count; c++)
                capacity[c] = parameters.SurfaceHeatCapacity(kinds[c] == SurfaceThermalKind.Ice && elevation[c] < seaLevel
                    ? SurfaceThermalKind.Ocean : kinds[c]) + parameters.AirHeatCapacity;
            for (int phase = 0; phase < phases; phase++)
            {
                var samples = DailyInsolationSampleDirections.Create(phase * orbit.OrbitalPeriodDays / phases - 0.5,
                    orbit.OrbitalPeriodDays, orbit.RotationPeriodDays, orbit.AxialTiltRad);
                forcing[phase] = new double[count];
                for (int c = 0; c < count; c++)
                {
                    double z = grid.CenterZ[c];
                    double albedo = kinds[c] == SurfaceThermalKind.Ice ? 0.62 : 0.30 + 0.078 * (1.5 * z * z - 0.5);
                    double absorbed = parameters.SolarConstant * samples.AverageFactor(grid.CenterX[c], grid.CenterY[c], z) * (1.0 - albedo);
                    double offset = cycleK - Temperature.LapseRateKPerM * Math.Max(0.0, elevation[c] - seaLevel);
                    forcing[phase][c] = absorbed - OutgoingInterceptWm2 + FeedbackWm2K * offset;
                    AnnualTargetK[c] += forcing[phase][c];
                }
            }
            double[] conductance = MeridionalEnergyBalance.BuildConductance(grid,
                MeridionalEnergyBalance.DiffusionWm2K * parameters.MeridionalTransportScale);
            _temperatureC = PeriodicHeatBalance.Solve(grid.Area, grid.EdgeI, grid.EdgeJ, conductance,
                capacity, forcing, orbit.OrbitalPeriodDays * SimulationTime.SecondsPerDay, FeedbackWm2K);
            var transport = new ConservativeHeatTransport(count, grid.EdgeI, grid.EdgeJ, conductance);
            var power = new double[count];
            double dt = orbit.OrbitalPeriodDays * SimulationTime.SecondsPerDay / phases;
            double maximum = 0;
            for (int phase = 0; phase < phases; phase++)
            {
                double[] row = _temperatureC[phase], previous = _temperatureC[(phase + phases - 1) % phases];
                transport.ComputePowerW(row, power);
                for (int c = 0; c < count; c++)
                {
                    AnnualMeanK[c] += row[c];
                    double error = capacity[c] * (row[c] - previous[c]) / dt + FeedbackWm2K * row[c]
                        - power[c] / grid.Area[c] - forcing[phase][c];
                    maximum = Math.Max(maximum, Math.Abs(error));
                }
            }
            MaxResidualWm2 = maximum;
            if (double.IsNaN(maximum) || maximum > 0.001)
                throw new ArithmeticException("A szezonális energiamérleg maradéka túl nagy: " + maximum);
            for (int c = 0; c < count; c++)
            {
                AnnualMeanK[c] = AnnualMeanK[c] / phases + 273.15;
                AnnualTargetK[c] = AnnualTargetK[c] / phases / FeedbackWm2K + 273.15;
            }
        }

        public double TemperatureK(int cell, double day)
        {
            if (double.IsNaN(day) || double.IsInfinity(day)) throw new ArgumentOutOfRangeException(nameof(day));
            if (cell < 0 || cell >= AnnualMeanK.Length) throw new ArgumentOutOfRangeException(nameof(cell));
            double fraction = day / OrbitalPeriodDays;
            double position = (fraction - Math.Floor(fraction)) * PhaseCount;
            int a = (int)position;
            if (a == PhaseCount) a = 0;
            double w = position - Math.Floor(position);
            double first = _temperatureC[a][cell], second = _temperatureC[(a + 1) % PhaseCount][cell];
            return first + (second - first) * w + 273.15;
        }

        public void Sample(double day, double[] output)
        {
            if (output == null || output.Length != AnnualMeanK.Length) throw new ArgumentException("Eltérő kimenetméret.");
            for (int c = 0; c < output.Length; c++) output[c] = TemperatureK(c, day);
        }
    }
}
