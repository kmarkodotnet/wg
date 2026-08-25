using System;
using System.Runtime.CompilerServices;

namespace WorldGen.Core.Grid
{
    /// <summary>
    /// Tile-azonosító a cubed sphere rácson.
    ///
    /// Bit-layout (docs/05-milestones.md §2.1):
    ///   [63:61] face   (3 bit, 0-5)
    ///   [60:56] level  (5 bit, 0-28)
    ///   [55:0]  morton (56 bit; u a páros, v a páratlan biteken összefésülve)
    ///
    /// A szülő puszta bitművelettel adódik (morton &gt;&gt; 2), ami a
    /// LOD-invarianciát (spec §70.5) és a hierarchikus noise-t (spec §52)
    /// triviálissá teszi. Tiszta érték-típus: nincs mögötte állapot.
    /// </summary>
    public readonly struct TileId : IEquatable<TileId>
    {
        public const int MaxLevel = 28;

        private const int FaceShift = 61;
        private const int LevelShift = 56;
        private const ulong MortonMask = (1UL << 56) - 1;
        private const ulong LevelMask = 0x1F;

        public readonly ulong Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TileId(ulong value)
        {
            Value = value;
        }

        public byte Face
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (byte)(Value >> FaceShift);
        }

        public int Level
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((Value >> LevelShift) & LevelMask);
        }

        public ulong Morton
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value & MortonMask;
        }

        /// <summary>Lapon belüli tile-koordináták, [0, 2^level) tartományban.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetUV(out uint u, out uint v)
        {
            ulong morton = Morton;
            u = (uint)CompactBits(morton);
            v = (uint)CompactBits(morton >> 1);
        }

        public static TileId FromFaceLevelUV(int face, int level, uint u, uint v)
        {
            if ((uint)face > 5)
                throw new ArgumentOutOfRangeException(nameof(face), "A face 0-5 tartományban lehet.");
            if ((uint)level > MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level), $"A level 0-{MaxLevel} tartományban lehet.");

            uint bound = level == 0 ? 1u : (1u << level);
            if (u >= bound)
                throw new ArgumentOutOfRangeException(nameof(u), $"u a [0,{bound}) tartományban lehet a level={level} szinten.");
            if (v >= bound)
                throw new ArgumentOutOfRangeException(nameof(v), $"v a [0,{bound}) tartományban lehet a level={level} szinten.");

            ulong morton = SpreadBits(u) | (SpreadBits(v) << 1);
            ulong value = ((ulong)face << FaceShift) | ((ulong)level << LevelShift) | morton;
            return new TileId(value);
        }

        /// <summary>A szülő tile, eggyel alacsonyabb LOD-szinten.</summary>
        public TileId Parent()
        {
            int level = Level;
            if (level == 0)
                throw new InvalidOperationException("A level 0 tile-nak nincs szülője.");

            ulong value = ((ulong)Face << FaceShift)
                        | ((ulong)(level - 1) << LevelShift)
                        | (Morton >> 2);
            return new TileId(value);
        }

        /// <summary>A négy gyerek tile egyike. Index: bit0 = u LSB, bit1 = v LSB.</summary>
        public TileId Child(int index)
        {
            if ((uint)index > 3)
                throw new ArgumentOutOfRangeException(nameof(index), "Az index 0-3 tartományban lehet.");
            int level = Level;
            if (level >= MaxLevel)
                throw new InvalidOperationException($"A level {MaxLevel} (max) tile-nak nincs gyereke.");

            ulong value = ((ulong)Face << FaceShift)
                        | ((ulong)(level + 1) << LevelShift)
                        | ((Morton << 2) | (uint)index);
            return new TileId(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SpreadBits(uint x)
        {
            ulong v = x;
            v = (v | (v << 16)) & 0x0000FFFF0000FFFFUL;
            v = (v | (v << 8)) & 0x00FF00FF00FF00FFUL;
            v = (v | (v << 4)) & 0x0F0F0F0F0F0F0F0FUL;
            v = (v | (v << 2)) & 0x3333333333333333UL;
            v = (v | (v << 1)) & 0x5555555555555555UL;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong CompactBits(ulong v)
        {
            v &= 0x5555555555555555UL;
            v = (v | (v >> 1)) & 0x3333333333333333UL;
            v = (v | (v >> 2)) & 0x0F0F0F0F0F0F0F0FUL;
            v = (v | (v >> 4)) & 0x00FF00FF00FF00FFUL;
            v = (v | (v >> 8)) & 0x0000FFFF0000FFFFUL;
            v = (v | (v >> 16)) & 0x00000000FFFFFFFFUL;
            return v;
        }

        public bool Equals(TileId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TileId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(TileId a, TileId b) => a.Value == b.Value;
        public static bool operator !=(TileId a, TileId b) => a.Value != b.Value;
        public override string ToString() => $"TileId(face={Face}, level={Level}, morton=0x{Morton:x14})";
    }
}
