using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Features
{
    /// <summary>
    /// M8 aggregált panel-metrikák (§6, docs/01-architecture.md §2, I4):
    /// minden mezőnek egyértelmű, visszakövethető számítási lánca van.
    ///
    /// HATÓKÖR: "Area" tile-számlálás, NEM valódi km² (dokumentált
    /// közelítés, ld. tools/reference/features_ref.py fejléce és ND-24 -
    /// a cubed-sphere tile-területek ~1.3-1.4x arányban változnak).
    /// </summary>
    public static class FeatureMetrics
    {
        /// <summary>Egy tile-halmaz "területe" tile-számlálásban.</summary>
        public static int AreaTiles(ICollection<TileId> tiles) => tiles.Count;

        /// <summary>Egy tile-halmazban előforduló egyedi biome-ok száma.</summary>
        public static int BiomeDiversity(IEnumerable<TileId> tiles, Dictionary<TileId, Climate.Biome> biomeOf)
        {
            var present = new HashSet<Climate.Biome>();
            foreach (TileId t in tiles)
                present.Add(biomeOf[t]);
            return present.Count;
        }

        /// <summary>
        /// Hány folyó-tile folyik KÖZVETLENÜL óceánba egy adott tile-halmazon
        /// (kontinens vagy régió) belül - a "folyó-torkolatok száma" panel-mező
        /// (§8.3) forrása.
        /// </summary>
        public static int RiverMouthCount(
            IEnumerable<TileId> tiles, Dictionary<TileId, TileId?> parent,
            Dictionary<TileId, bool> isOcean, HashSet<TileId> riverTiles)
        {
            int count = 0;
            foreach (TileId t in tiles)
            {
                if (!riverTiles.Contains(t))
                    continue;
                TileId? p = parent[t];
                if (p.HasValue && isOcean[p.Value])
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Az óceán aránya a mezőben (0..1) - a "World Overview" panel
        /// "Ocean coverage" mezőjének forrása (docs/01-architecture.md §2.1).
        /// </summary>
        public static double OceanCoverageFraction(Dictionary<TileId, bool> isOcean)
        {
            if (isOcean.Count == 0)
                return 0.0;
            int oceanCount = 0;
            foreach (bool v in isOcean.Values)
                if (v) oceanCount++;
            return (double)oceanCount / isOcean.Count;
        }

        /// <summary>
        /// Hány DISTINCT vízgyűjtő-régió (a <see cref="FeatureSegmentation.
        /// FindWatershedRegions"/> eredménye, kifolyás-tile szerint
        /// azonosítva) metsz bele egy adott tile-halmazba (kontinensbe) -
        /// a "River basins" panel-mező (§2.2) forrása. Egy régió akkor
        /// számít bele, ha LEGALÁBB EGY tile-ja a halmazban van.
        /// </summary>
        public static int RiverBasinCount(
            HashSet<TileId> tiles, Dictionary<TileId, List<TileId>> regions)
        {
            int count = 0;
            foreach (KeyValuePair<TileId, List<TileId>> region in regions)
            {
                foreach (TileId t in region.Value)
                {
                    if (tiles.Contains(t))
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }
    }
}
