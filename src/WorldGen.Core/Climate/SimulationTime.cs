using System;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// A pillanatnyi hőmodell egész tickes Core-ideje (ND-101). Egy tick
    /// <see cref="TickSeconds"/> másodperc; a másodperc egész, a csillagászati
    /// nap <c>másodperc / 86400</c>. A Viewer ebből származtatja a fény és az
    /// overlay idejét; a Unity <c>deltaTime</c> csak az idősebességet
    /// gyűjtheti, solver-lépés nem lehet belőle.
    /// </summary>
    public readonly struct SimulationTime : IEquatable<SimulationTime>
    {
        public const int TickSeconds = 900;
        public const int SecondsPerHour = 3600;
        public const int SecondsPerDay = 86400;
        public const int TicksPerDay = SecondsPerDay / TickSeconds;

        /// <summary>A hőmező 30 napos kanonikus bucketje (ND-101).</summary>
        public const long BucketTicks = 2880;

        /// <summary>Spin-up a bucket kezdete előtt, 10 nap (ND-101).</summary>
        public const long SpinUpTicks = 960;

        public readonly long Tick;

        public SimulationTime(long tick) => Tick = tick;

        public long Seconds => Tick * TickSeconds;
        public double Days => Seconds / (double)SecondsPerDay;

        /// <summary>
        /// Az adott napértékhez tartozó utolsó egész tick (lefelé kerekítve).
        /// Prezentációs bemenetből (pl. Inspector-idő) csak ezen keresztül
        /// készülhet Core-idő.
        /// </summary>
        public static SimulationTime FromDaysFloor(double days)
        {
            if (double.IsNaN(days) || double.IsInfinity(days))
                throw new ArgumentOutOfRangeException(nameof(days));
            double ticks = Math.Floor(days * SecondsPerDay / TickSeconds);
            if (ticks > long.MaxValue / TickSeconds || ticks < long.MinValue / TickSeconds)
                throw new ArgumentOutOfRangeException(nameof(days));
            return new SimulationTime((long)ticks);
        }

        /// <summary>Lefelé kerekítő egész osztás negatív számlálóra is.</summary>
        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0)))
                q--;
            return q;
        }

        /// <summary>A tick bucketjének kanonikus spin-up kezdőtickje.</summary>
        public static long CanonicalStartTick(long targetTick)
            => FloorDiv(targetTick, BucketTicks) * BucketTicks - SpinUpTicks;

        public bool Equals(SimulationTime other) => Tick == other.Tick;
        public override bool Equals(object obj) => obj is SimulationTime other && Equals(other);
        public override int GetHashCode() => Tick.GetHashCode();
        public override string ToString() => $"tick {Tick} ({Days:0.######} nap)";
    }
}
