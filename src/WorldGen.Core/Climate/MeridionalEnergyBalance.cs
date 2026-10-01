using System;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// ND-168: az éves radiatív célmező energiamegmaradó, meridionális
    /// kiegyenlítése. Referencia: tools/reference/meridional_energy_balance_ref.py.
    /// </summary>
    public static class MeridionalEnergyBalance
    {
        public const double DiffusionWm2K = 0.555;
        public const double RadiativeFeedbackWm2K = 2.09;
        public const int Iterations = 256;

        /// <summary>Az élek földrajzi vezetőképessége W/K egységben.</summary>
        public static double[] BuildConductance(DenseGridMetrics grid, double diffusionWm2K = DiffusionWm2K)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (!(diffusionWm2K >= 0.0) || double.IsInfinity(diffusionWm2K))
                throw new ArgumentOutOfRangeException(nameof(diffusionWm2K));
            var conductance = new double[grid.EdgeCount];
            double radius = grid.RadiusMeters;
            for (int e = 0; e < grid.EdgeCount; e++)
            {
                int i = grid.EdgeI[e], j = grid.EdgeJ[e];
                double dx = grid.CenterX[j] - grid.CenterX[i];
                double dy = grid.CenterY[j] - grid.CenterY[i];
                double dz = grid.CenterZ[j] - grid.CenterZ[i];
                double distance = radius * Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double x = grid.EdgeMidX[e], y = grid.EdgeMidY[e], z = grid.EdgeMidZ[e];
                double rho = Math.Sqrt(x * x + y * y);
                if (rho <= 1e-12) continue;
                double northX = -z * x / rho, northY = -z * y / rho, northZ = rho;
                double projection = grid.EdgeNormalX[e] * northX
                    + grid.EdgeNormalY[e] * northY + grid.EdgeNormalZ[e] * northZ;
                conductance[e] = diffusionWm2K * radius * radius * (grid.EdgeLength[e] / distance)
                    * projection * projection;
            }
            return conductance;
        }

        /// <summary>
        /// Megoldja a λ A δ − P(δ) = P(T₀) éves mérleget. A kapott
        /// korrekció területi átlaga nulla. A lépésszám rögzített.
        /// </summary>
        public static double[] SolveCorrection(DenseGridMetrics grid, double[] targetK,
            double diffusionWm2K = DiffusionWm2K, double feedbackWm2K = RadiativeFeedbackWm2K)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (targetK == null || targetK.Length != grid.CellCount)
                throw new ArgumentException("A célmező cellaszáma eltér.", nameof(targetK));
            if (!(feedbackWm2K > 0.0) || double.IsInfinity(feedbackWm2K))
                throw new ArgumentOutOfRangeException(nameof(feedbackWm2K));
            double[] g = BuildConductance(grid, diffusionWm2K);
            var transport = new ConservativeHeatTransport(grid.CellCount, grid.EdgeI, grid.EdgeJ, g);
            int count = grid.CellCount;
            var diagonal = new double[count];
            for (int c = 0; c < count; c++) diagonal[c] = feedbackWm2K * grid.Area[c];
            for (int e = 0; e < grid.EdgeCount; e++)
            {
                diagonal[grid.EdgeI[e]] += g[e];
                diagonal[grid.EdgeJ[e]] += g[e];
            }

            var source = new double[count];
            transport.ComputePowerW(targetK, source);
            var correction = new double[count];
            var residual = (double[])source.Clone();
            var z = new double[count];
            var direction = new double[count];
            double rz = 0.0;
            for (int c = 0; c < count; c++)
            {
                z[c] = residual[c] / diagonal[c];
                direction[c] = z[c];
                rz += residual[c] * z[c];
            }
            if (rz == 0.0) return correction;

            var power = new double[count];
            var applied = new double[count];
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                transport.ComputePowerW(direction, power);
                double denominator = 0.0;
                for (int c = 0; c < count; c++)
                {
                    applied[c] = feedbackWm2K * grid.Area[c] * direction[c] - power[c];
                    denominator += direction[c] * applied[c];
                }
                if (denominator == 0.0) break;
                double alpha = rz / denominator;
                double nextRz = 0.0;
                for (int c = 0; c < count; c++)
                {
                    correction[c] += alpha * direction[c];
                    residual[c] -= alpha * applied[c];
                    z[c] = residual[c] / diagonal[c];
                    nextRz += residual[c] * z[c];
                }
                if (nextRz == 0.0) break;
                double beta = nextRz / rz;
                for (int c = 0; c < count; c++)
                    direction[c] = z[c] + beta * direction[c];
                rz = nextRz;
            }

            double areaSum = 0.0, weightedSum = 0.0;
            for (int c = 0; c < count; c++)
            {
                areaSum += grid.Area[c];
                weightedSum += grid.Area[c] * correction[c];
            }
            double mean = weightedSum / areaSum;
            for (int c = 0; c < count; c++) correction[c] -= mean;
            return correction;
        }
    }
}
