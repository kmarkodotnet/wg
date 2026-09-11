using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>A statikus quadok visszaállítható, részleges indexmaszkja.</summary>
    public sealed class TerrainIndexMask
    {
        public readonly struct Range
        {
            public readonly int Start, Count;
            public Range(int start, int count) { Start = start; Count = count; }
        }

        private readonly int _baseLevel;
        private readonly int[] _offsets, _original;
        private HashSet<TileId> _hidden = new HashSet<TileId>();
        public int[] Indices { get; }
        public int HiddenCount => _hidden.Count;

        /// <summary>ND-75: a sikeresen alkalmazott maszkolás quad-sorszámai; új snapshot.</summary>
        public HashSet<int> CopyHiddenQuadIndices()
        {
            var result = new HashSet<int>();
            foreach (TileId tile in _hidden) result.Add(_original[_offsets[DenseIndex(tile)]] / 4);
            return result;
        }

        public TerrainIndexMask(int baseLevel, int[] offsets, int[] indices)
        {
            if (baseLevel < 0 || baseLevel > 12) throw new ArgumentOutOfRangeException(nameof(baseLevel));
            int side = 1 << baseLevel;
            if (offsets.Length != checked(6 * side * side)) throw new ArgumentException("Hiányos base-index térkép.");
            foreach (int offset in offsets)
                if (offset < 0 || offset > indices.Length - 6) throw new ArgumentException("Érvénytelen quad-index tartomány.");
            _baseLevel = baseLevel;
            _offsets = (int[])offsets.Clone();
            _original = (int[])indices.Clone();
            Indices = (int[])indices.Clone();
        }

        public static int DenseIndex(TileId tile)
        {
            tile.GetUV(out uint u, out uint v);
            int side = 1 << tile.Level;
            return checked(tile.Face * side * side + (int)u * side + (int)v);
        }

        public List<Range> SetHidden(IEnumerable<TileId> hidden)
        {
            var next = new HashSet<TileId>(hidden);
            foreach (TileId root in next)
                if (root.Level != _baseLevel) throw new ArgumentException("A maszkhoz base-szintű tile kell.");
            var starts = new List<int>();
            foreach (TileId root in _hidden)
            {
                if (next.Contains(root)) continue;
                int offset = _offsets[DenseIndex(root)];
                Array.Copy(_original, offset, Indices, offset, 6);
                starts.Add(offset);
            }
            foreach (TileId root in next)
            {
                if (_hidden.Contains(root)) continue;
                int offset = _offsets[DenseIndex(root)];
                // Saját, biztosan érvényes csúcsára degeneráljuk a két háromszöget.
                for (int i = 0; i < 6; i++) Indices[offset + i] = _original[offset];
                starts.Add(offset);
            }
            _hidden = next;
            starts.Sort();
            var ranges = new List<Range>();
            foreach (int start in starts)
            {
                if (ranges.Count > 0)
                {
                    Range previous = ranges[ranges.Count - 1];
                    // Kis hézag átmásolása olcsóbb, mint új natív feltöltőhívás.
                    if (start <= previous.Start + previous.Count + 64)
                    {
                        ranges[ranges.Count - 1] = new Range(previous.Start, start + 6 - previous.Start);
                        continue;
                    }
                }
                ranges.Add(new Range(start, 6));
            }
            return ranges;
        }
    }
}
