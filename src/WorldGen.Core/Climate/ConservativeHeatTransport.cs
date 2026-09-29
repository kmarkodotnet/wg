using System;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// ND-167: páronként konzervatív hőfluxus kanonikus rácséleken.
    /// A vezetőképesség bemenet; ennek fizikai kalibrációja még nyitott.
    /// Egy példányt egyszerre egy számítás használjon.
    /// </summary>
    public sealed class ConservativeHeatTransport
    {
        private readonly int _cellCount;
        private readonly int[] _edgeI;
        private readonly int[] _edgeJ;
        private readonly double[] _conductanceWPerK;

        public ConservativeHeatTransport(int cellCount, int[] edgeI, int[] edgeJ,
            double[] conductanceWPerK)
        {
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (edgeI == null) throw new ArgumentNullException(nameof(edgeI));
            if (edgeJ == null) throw new ArgumentNullException(nameof(edgeJ));
            if (conductanceWPerK == null) throw new ArgumentNullException(nameof(conductanceWPerK));
            if (edgeI.Length != edgeJ.Length || edgeI.Length != conductanceWPerK.Length)
                throw new ArgumentException("Az éltömbök hossza eltér.");

            for (int e = 0; e < edgeI.Length; e++)
            {
                if (edgeI[e] < 0 || edgeI[e] >= edgeJ[e] || edgeJ[e] >= cellCount)
                    throw new ArgumentException("Az él végpontjai nem kanonikusak.", nameof(edgeI));
                double g = conductanceWPerK[e];
                if (g < 0.0 || double.IsNaN(g) || double.IsInfinity(g))
                    throw new ArgumentException("A vezetőképesség véges és nemnegatív legyen.", nameof(conductanceWPerK));
            }

            _cellCount = cellCount;
            _edgeI = (int[])edgeI.Clone();
            _edgeJ = (int[])edgeJ.Clone();
            _conductanceWPerK = (double[])conductanceWPerK.Clone();
        }

        /// <summary>
        /// Az egyes cellákba jutó teljesítmény W-ban. Az él i végpontja
        /// Q = G * (Tj - Ti) teljesítményt, j végpontja pontosan -Q-t kap.
        /// A kimeneti tömb nem lehet azonos a hőmérséklet tömbjével.
        /// </summary>
        public void ComputePowerW(double[] temperatureK, double[] powerW)
        {
            if (temperatureK == null) throw new ArgumentNullException(nameof(temperatureK));
            if (powerW == null) throw new ArgumentNullException(nameof(powerW));
            if (temperatureK.Length != _cellCount || powerW.Length != _cellCount)
                throw new ArgumentException("A cellatömbök hossza eltér.");
            if (ReferenceEquals(temperatureK, powerW))
                throw new ArgumentException("A bemenet és a kimenet nem lehet ugyanaz a tömb.", nameof(powerW));
            for (int c = 0; c < _cellCount; c++)
                if (double.IsNaN(temperatureK[c]) || double.IsInfinity(temperatureK[c]))
                    throw new ArgumentException("A hőmérséklet véges legyen.", nameof(temperatureK));

            Array.Clear(powerW, 0, powerW.Length);
            for (int e = 0; e < _edgeI.Length; e++)
            {
                int i = _edgeI[e], j = _edgeJ[e];
                double q = _conductanceWPerK[e] * (temperatureK[j] - temperatureK[i]);
                powerW[i] += q;
                powerW[j] -= q;
            }
        }
    }
}
