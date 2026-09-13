#nullable enable
using System;
using System.Security.Cryptography;

namespace WorldGen.App.WorldSetup
{
    /// <summary>
    /// Nem determinisztikus entrópia ÚJ seed és véletlen preset sorsolásához
    /// (ND-109). A sorsolt érték explicit kerül a kérésbe és a mentésbe;
    /// a szimuláció soha nem hívja.
    /// </summary>
    public interface IEntropySource
    {
        ulong NextUInt64();
    }

    public sealed class CryptoEntropySource : IEntropySource, IDisposable
    {
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private readonly byte[] _buffer = new byte[8];

        public ulong NextUInt64()
        {
            lock (_buffer)
            {
                _rng.GetBytes(_buffer);
                return BitConverter.ToUInt64(_buffer, 0);
            }
        }

        public void Dispose() => _rng.Dispose();
    }

    public static class EntropyMath
    {
        /// <summary>Egyenletes [0, 1) a felső 53 bitből.</summary>
        public static double NextUnitDouble(IEntropySource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return (source.NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Torzítatlan egyenletes egész [min, max]-ban (elutasító mintavétel).</summary>
        public static long NextInclusive(IEntropySource source, long minimum, long maximum)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (minimum > maximum) throw new ArgumentException("minimum > maximum");
            ulong range = unchecked((ulong)(maximum - minimum) + 1UL);
            if (range == 0) return unchecked((long)source.NextUInt64()); // a teljes 64 bites tartomány
            ulong threshold = unchecked(0UL - range) % range;
            while (true)
            {
                ulong x = source.NextUInt64();
                if (x >= threshold) return unchecked(minimum + (long)(x % range));
            }
        }
    }
}
