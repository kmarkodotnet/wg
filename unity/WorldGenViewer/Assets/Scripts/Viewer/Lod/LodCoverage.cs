using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>Teljes renderpartíció a valóban kiváltandó base-tile-ok alatt.</summary>
    public sealed class LodCoverage
    {
        public readonly HashSet<TileId> Roots = new HashSet<TileId>();
        public readonly HashSet<TileId> Leaves = new HashSet<TileId>();
        public int FallbackCount { get; private set; }

        /// <summary>Az óceáni szűrés UTÁNI partíció levele, vagy az érintetlen statikus alap.</summary>
        public TileId FindRenderedLeaf(TileId sample, int baseLevel)
        {
            if (baseLevel < 0 || baseLevel > sample.Level)
                throw new ArgumentOutOfRangeException(nameof(baseLevel));
            TileId tile = sample;
            while (tile.Level > baseLevel)
            {
                if (Leaves.Contains(tile)) return tile;
                tile = tile.Parent();
            }
            return tile;
        }

        public static LodCoverage Complete(IEnumerable<TileId> selected, int baseLevel)
        {
            if (baseLevel < 0 || baseLevel > TileId.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(baseLevel));
            var result = new LodCoverage();
            var wanted = new HashSet<TileId>();
            var expanded = new HashSet<TileId>();
            foreach (TileId leaf in selected)
            {
                if (leaf.Level <= baseLevel) continue;
                wanted.Add(leaf);
                TileId ancestor = leaf;
                while (ancestor.Level > baseLevel)
                {
                    ancestor = ancestor.Parent();
                    expanded.Add(ancestor);
                }
                result.Roots.Add(ancestor);
            }

            void Visit(TileId node)
            {
                if (wanted.Contains(node) || !expanded.Contains(node))
                {
                    result.Leaves.Add(node);
                    if (!wanted.Contains(node)) result.FallbackCount++;
                    return;
                }
                for (int i = 0; i < 4; i++) Visit(node.Child(i));
            }

            foreach (TileId root in result.Roots) Visit(root);
            return result;
        }
    }
}
