#nullable enable
using System;

namespace WorldGen.App.Diagnostics
{
    /// <summary>
    /// Gördülő ablakos képkockaidő-statisztika a debug overlayhez (WF-DIAG-002).
    /// Nem-véges vagy negatív minta nem kerül az ablakba, csak számlálódik.
    /// </summary>
    public sealed class FrameTimeStats
    {
        private readonly double[] _samples;
        private int _next;
        private int _count;

        public FrameTimeStats(int capacity = 120)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _samples = new double[capacity];
        }

        public int Capacity => _samples.Length;
        public int Count => _count;
        public int InvalidSampleCount { get; private set; }

        public void AddSample(double frameMilliseconds)
        {
            if (!double.IsFinite(frameMilliseconds) || frameMilliseconds < 0)
            {
                InvalidSampleCount++;
                return;
            }
            _samples[_next] = frameMilliseconds;
            _next = (_next + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
        }

        public void Reset()
        {
            _next = 0;
            _count = 0;
            InvalidSampleCount = 0;
        }

        public double AverageMilliseconds
        {
            get
            {
                if (_count == 0) return 0;
                double sum = 0;
                for (int i = 0; i < _count; i++) sum += _samples[i];
                return sum / _count;
            }
        }

        /// <summary>1000 / átlag; 0, ha nincs minta vagy az átlag 0.</summary>
        public double FramesPerSecond
        {
            get
            {
                double avg = AverageMilliseconds;
                return avg > 0 ? 1000.0 / avg : 0;
            }
        }

        public double MaxMilliseconds
        {
            get
            {
                double max = 0;
                for (int i = 0; i < _count; i++) max = Math.Max(max, _samples[i]);
                return max;
            }
        }

        /// <summary>Legközelebbi rang szerinti percentilis (p ∈ [0, 1]); 0, ha nincs minta.</summary>
        public double PercentileMilliseconds(double p)
        {
            if (double.IsNaN(p) || p < 0 || p > 1) throw new ArgumentOutOfRangeException(nameof(p));
            if (_count == 0) return 0;
            var sorted = new double[_count];
            Array.Copy(_samples, sorted, _count);
            Array.Sort(sorted);
            int rank = (int)Math.Ceiling(p * _count);
            if (rank < 1) rank = 1;
            return sorted[rank - 1];
        }
    }
}
