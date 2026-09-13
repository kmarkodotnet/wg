#nullable enable
using System;

namespace WorldGen.App.Serialization
{
    /// <summary>
    /// CRC-32 (IEEE 802.3, reflektált polinom 0xEDB88320, kezdőérték és
    /// záró XOR 0xFFFFFFFF) — a zlib/PNG/ZIP változat. Ellenőrző érték:
    /// "123456789" → 0xCBF43926 (tesztben a Python <c>zlib.crc32</c> ellen mérve).
    /// A mentési konténer sérülés-felismerésére szolgál, nem kriptográfiai.
    /// </summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Append(0, data, 0, data.Length);
        }

        public static uint Compute(byte[] data, int offset, int count) => Append(0, data, offset, count);

        /// <summary>
        /// Folytatólagos számítás: <paramref name="crc"/> egy korábbi (lezárt)
        /// eredmény, üres kezdésnél 0.
        /// </summary>
        public static uint Append(uint crc, byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count) throw new ArgumentOutOfRangeException(nameof(count));
            uint c = ~crc;
            int end = offset + count;
            for (int i = offset; i < end; i++)
                c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return ~c;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
