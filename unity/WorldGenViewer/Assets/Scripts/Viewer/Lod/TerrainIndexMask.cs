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
        private long _revision;
        public int[] Indices { get; }
        public int HiddenCount => _hidden.Count;

        /// <summary>ND-75: a sikeresen alkalmazott maszkolás quad-sorszámai; új snapshot.</summary>
        public HashSet<int> CopyHiddenQuadIndices()
        {
            var result = new HashSet<int>();
            foreach (TileId tile in _hidden) result.Add(_original[_offsets[DenseIndex(tile)]] / 4);
            return result;
        }

        public TerrainIndexMask(int baseLevel, int[] offsets, int[] indices, bool allowMissingTiles = false)
        {
            if (baseLevel < 0 || baseLevel > 12) throw new ArgumentOutOfRangeException(nameof(baseLevel));
            int side = 1 << baseLevel;
            if (offsets.Length != checked(6 * side * side)) throw new ArgumentException("Hiányos base-index térkép.");
            foreach (int offset in offsets)
                if (!(allowMissingTiles && offset == -1) && (offset < 0 || offset > indices.Length - 6))
                    throw new ArgumentException("Érvénytelen quad-index tartomány.");
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

        /// <summary>ND-89: saját, revízióhoz kötött terv; előkészítése nem publikál.</summary>
        public sealed class PreparedUpdate
        {
            internal readonly TerrainIndexMask Owner;
            internal readonly long Revision;
            internal readonly HashSet<TileId> Hidden;
            internal readonly int[] RestoreOffsets, HideOffsets;
            public IReadOnlyList<Range> Ranges { get; }

            internal PreparedUpdate(TerrainIndexMask owner, long revision, HashSet<TileId> hidden,
                List<int> restore, List<int> hide, List<Range> ranges)
            {
                Owner = owner; Revision = revision; Hidden = hidden;
                RestoreOffsets = restore.ToArray(); HideOffsets = hide.ToArray();
                Ranges = ranges.AsReadOnly();
            }

            public HashSet<int> CopyHiddenQuadIndices()
            {
                var result = new HashSet<int>();
                foreach (TileId tile in Hidden)
                    result.Add(Owner._original[Owner._offsets[DenseIndex(tile)]] / 4);
                return result;
            }
        }

        // A viewer főszálas staging-sora használja; nem párhuzamos írás/olvasás API.
        public PreparedUpdate PrepareHidden(IEnumerable<TileId> hidden)
        {
            var next = new HashSet<TileId>(hidden);
            foreach (TileId root in next)
            {
                if (root.Level != _baseLevel) throw new ArgumentException("A maszkhoz base-szintű tile kell.");
                if (_offsets[DenseIndex(root)] < 0) throw new ArgumentException("Nem létező felszíni quad nem rejthető el.");
            }
            var starts = new List<int>();
            var restore = new List<int>();
            var hide = new List<int>();
            foreach (TileId root in _hidden)
            {
                if (next.Contains(root)) continue;
                int offset = _offsets[DenseIndex(root)];
                restore.Add(offset);
                starts.Add(offset);
            }
            foreach (TileId root in next)
            {
                if (_hidden.Contains(root)) continue;
                int offset = _offsets[DenseIndex(root)];
                hide.Add(offset);
                starts.Add(offset);
            }
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
            return new PreparedUpdate(this, _revision, next, restore, hide, ranges);
        }

        public IReadOnlyList<Range> ApplyPrepared(PreparedUpdate update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (!ReferenceEquals(update.Owner, this) || update.Revision != _revision)
                throw new InvalidOperationException("Idegen vagy elavult fedésmaszk-terv.");
            foreach (int offset in update.RestoreOffsets) Array.Copy(_original, offset, Indices, offset, 6);
            foreach (int offset in update.HideOffsets)
                // Saját, biztosan érvényes csúcsára degeneráljuk a két háromszöget.
                for (int i = 0; i < 6; i++) Indices[offset + i] = _original[offset];
            _hidden = update.Hidden;
            _revision++;
            return update.Ranges;
        }

        public List<Range> SetHidden(IEnumerable<TileId> hidden)
        {
            return new List<Range>(ApplyPrepared(PrepareHidden(hidden)));
        }

        /// <summary>
        /// ND-92: natív feltöltési hiba után a CPU-halmazon kívül is maradhat
        /// rejtett GPU-quad. A teljes tartományt újra fel kell tölteni, akkor is,
        /// ha a CPU szerint nincs rejtett quad. Minden korábbi terv érvénytelen.
        /// </summary>
        public Range RestoreAll()
        {
            Array.Copy(_original, Indices, _original.Length);
            // Ne módosítsuk a korábbi PreparedUpdate által is birtokolt halmazt.
            _hidden = new HashSet<TileId>();
            _revision++;
            return new Range(0, Indices.Length);
        }
    }
}
