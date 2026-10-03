using System;
using WorldGen.Core.Numerics;

namespace WorldGen.Core.Climate
{
    /// <summary>ND-174: periodikus energiamérleg, visszalépő Euler + Fourier/COCG.</summary>
    public static class PeriodicHeatBalance
    {
        public const int Iterations = 1024;

        public static double[][] Solve(double[] area, int[] edgeI, int[] edgeJ, double[] conductance,
            double[] capacity, double[][] forcing, double periodSeconds, double feedback = 2.09)
        {
            if (area == null || edgeI == null || edgeJ == null || conductance == null || capacity == null || forcing == null)
                throw new ArgumentNullException("Az energiamérleg bemenete nem lehet null.");
            int count = area.Length, phases = forcing.Length;
            if (count == 0 || capacity.Length != count || phases < 4 || phases % 2 != 0
                || edgeI.Length != edgeJ.Length || edgeI.Length != conductance.Length)
                throw new ArgumentException("Érvénytelen rács vagy páros évfázisszám.");
            Positive(periodSeconds); Positive(feedback);
            for (int c = 0; c < count; c++) { Positive(area[c]); Positive(capacity[c]); }
            for (int e = 0; e < edgeI.Length; e++)
                if (edgeI[e] < 0 || edgeI[e] >= count || edgeJ[e] < 0 || edgeJ[e] >= count
                    || edgeI[e] == edgeJ[e] || !Finite(conductance[e]) || conductance[e] < 0)
                    throw new ArgumentException("Érvénytelen él.");
            foreach (double[] phase in forcing)
            {
                if (phase == null || phase.Length != count) throw new ArgumentException("Eltérő fázisméret.");
                foreach (double value in phase) if (!Finite(value)) throw new ArgumentException("Nem véges forrás.");
            }

            double dt = periodSeconds / phases;
            var result = new double[phases][];
            for (int j = 0; j < phases; j++) result[j] = new double[count];
            var sine = new double[phases]; var cosine = new double[phases];
            var source = new Pair[count]; var mass = new Pair[count]; var diagonal = new Pair[count];
            var x = new Pair[count]; var r = new Pair[count]; var z = new Pair[count];
            var direction = new Pair[count]; var applied = new Pair[count];
            for (int harmonic = 0; harmonic <= phases / 2; harmonic++)
            {
                for (int j = 0; j < phases; j++)
                    DeterministicMath.SinCos(2.0 * Math.PI * harmonic * j / phases, out sine[j], out cosine[j]);
                DeterministicMath.SinCos(2.0 * Math.PI * harmonic / phases, out double si, out double co);
                var shift = new Pair((1.0 - co) / dt, si / dt);
                for (int c = 0; c < count; c++)
                {
                    Pair total = default;
                    for (int j = 0; j < phases; j++) total += new Pair(cosine[j], -sine[j]) * forcing[j][c];
                    source[c] = total * area[c] / phases;
                    mass[c] = (new Pair(feedback, 0) + shift * capacity[c]) * area[c];
                    diagonal[c] = mass[c];
                    x[c] = default;
                }
                for (int e = 0; e < edgeI.Length; e++)
                {
                    diagonal[edgeI[e]] += new Pair(conductance[e], 0);
                    diagonal[edgeJ[e]] += new Pair(conductance[e], 0);
                }
                double initial = 0;
                Pair rz = default;
                for (int c = 0; c < count; c++)
                {
                    r[c] = source[c]; z[c] = r[c] / diagonal[c]; direction[c] = z[c];
                    rz += r[c] * z[c]; initial += r[c].NormSquared;
                }
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    double norm = 0, maxResidual = 0;
                    for (int c = 0; c < count; c++)
                    {
                        norm += r[c].NormSquared;
                        maxResidual = Math.Max(maxResidual, Math.Sqrt(r[c].NormSquared) / area[c]);
                    }
                    // Rögzített felső korlát; a gépi pontosság után a további osztás kerülendő.
                    if (maxResidual <= 1e-11 || norm <= initial * 1e-28 || rz.NormSquared == 0) break;
                    Apply(mass, edgeI, edgeJ, conductance, direction, applied);
                    Pair denominator = default;
                    for (int c = 0; c < count; c++) denominator += direction[c] * applied[c];
                    if (denominator.NormSquared == 0) break;
                    Pair alpha = rz / denominator;
                    Pair next = default;
                    for (int c = 0; c < count; c++)
                    {
                        x[c] += alpha * direction[c]; r[c] -= alpha * applied[c];
                        z[c] = r[c] / diagonal[c]; next += r[c] * z[c];
                    }
                    Pair beta = next / rz;
                    for (int c = 0; c < count; c++) direction[c] = z[c] + beta * direction[c];
                    rz = next;
                }
                Apply(mass, edgeI, edgeJ, conductance, x, applied);
                bool fallback = false;
                for (int c = 0; c < count; c++)
                {
                    double error = Math.Sqrt((applied[c] - source[c]).NormSquared) / area[c];
                    if (!Finite(error) || error > 1e-5) fallback = true;
                }
                if (fallback)
                {
                    // A komplex bilineáris szorzat nem nulla reziduumnál is eltűnhet.
                    // A szigorúan diagonáldomináns rendszer Jacobi-iterációja konvergens.
                    Array.Clear(x, 0, count);
                    for (int iteration = 0; iteration < 8192; iteration++)
                    {
                        Apply(mass, edgeI, edgeJ, conductance, x, applied);
                        double maximum = 0;
                        for (int c = 0; c < count; c++)
                            maximum = Math.Max(maximum, Math.Sqrt((applied[c] - source[c]).NormSquared) / area[c]);
                        if (maximum <= 1e-9) break;
                        for (int c = 0; c < count; c++) x[c] += (source[c] - applied[c]) / diagonal[c];
                    }
                    Apply(mass, edgeI, edgeJ, conductance, x, applied);
                }
                for (int c = 0; c < count; c++)
                {
                    double error = Math.Sqrt((applied[c] - source[c]).NormSquared) / area[c];
                    if (!Finite(error) || error > 1e-5)
                        throw new ArithmeticException("A periodikus hőmérleg harmonikus maradéka túl nagy: " + error + ", harmonic=" + harmonic);
                }
                int multiplicity = harmonic == 0 || 2 * harmonic == phases ? 1 : 2;
                for (int j = 0; j < phases; j++)
                    for (int c = 0; c < count; c++)
                        result[j][c] += multiplicity * (x[c].Real * cosine[j] - x[c].Imaginary * sine[j]);
            }
            return result;
        }

        private static void Apply(Pair[] mass, int[] i, int[] j, double[] g, Pair[] x, Pair[] output)
        {
            for (int c = 0; c < x.Length; c++) output[c] = mass[c] * x[c];
            for (int e = 0; e < i.Length; e++)
            {
                Pair flux = (x[i[e]] - x[j[e]]) * g[e];
                output[i[e]] += flux; output[j[e]] -= flux;
            }
        }

        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        private static void Positive(double x)
        {
            if (!Finite(x) || x <= 0) throw new ArgumentOutOfRangeException(nameof(x));
        }

        // Explicit double-műveleti sorrend: nincs runtime-függő Complex-osztás.
        private readonly struct Pair
        {
            public readonly double Real, Imaginary;
            public Pair(double real, double imaginary) { Real = real; Imaginary = imaginary; }
            public double NormSquared => Real * Real + Imaginary * Imaginary;
            public static Pair operator +(Pair a, Pair b) => new Pair(a.Real + b.Real, a.Imaginary + b.Imaginary);
            public static Pair operator -(Pair a, Pair b) => new Pair(a.Real - b.Real, a.Imaginary - b.Imaginary);
            public static Pair operator *(Pair a, Pair b) => new Pair(a.Real * b.Real - a.Imaginary * b.Imaginary,
                a.Real * b.Imaginary + a.Imaginary * b.Real);
            public static Pair operator *(Pair a, double b) => new Pair(a.Real * b, a.Imaginary * b);
            public static Pair operator /(Pair a, double b) => new Pair(a.Real / b, a.Imaginary / b);
            public static Pair operator /(Pair a, Pair b) => new Pair(
                (a.Real * b.Real + a.Imaginary * b.Imaginary) / b.NormSquared,
                (a.Imaginary * b.Real - a.Real * b.Imaginary) / b.NormSquared);
        }
    }
}
