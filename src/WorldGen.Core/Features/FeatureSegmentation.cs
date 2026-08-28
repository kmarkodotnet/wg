using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Features
{
    /// <summary>
    /// M8 szegmentálás (§6.1, docs/01-architecture.md): kontinensek (a
    /// már meglévő <see cref="Tectonics.SeaLevelCalibration.CountContinents"/>
    /// M7-ből újrahasznosítva) + régiók (vízgyűjtő-alapú, a
    /// <see cref="Hydrology.FlowNetwork"/> szülő-fájából).
    ///
    /// HATÓKÖR: a régió EGYSZERŰEN a vízgyűjtő-csoport (minden szárazföld-
    /// tile ahhoz az óceán-"kifolyáshoz" tartozik, amihez végül lefolyik)
    /// — NEM a teljes vízgyűjtő ∪ biome-klaszter ∪ domborzati-törés
    /// hibrid (ND-05).
    /// </summary>
    public static class FeatureSegmentation
    {
        /// <summary>
        /// Minden szárazföld-tile-t az óceán-"kifolyás" (a szülő-láncon
        /// az első óceán-tile) szerint csoportosít.
        /// </summary>
        public static Dictionary<TileId, List<TileId>> FindWatershedRegions(
            Dictionary<TileId, TileId?> parent, Dictionary<TileId, bool> isOcean)
        {
            var regions = new Dictionary<TileId, List<TileId>>();

            foreach (var kv in isOcean)
            {
                if (kv.Value) continue; // csak szárazföld
                TileId tile = kv.Key;
                TileId current = tile;

                while (true)
                {
                    TileId? next = parent[current];
                    if (!next.HasValue) break; // nem várt (szárazföld-tile-nak mindig van szülője)
                    if (isOcean[next.Value])
                    {
                        if (!regions.TryGetValue(next.Value, out List<TileId> list))
                        {
                            list = new List<TileId>();
                            regions[next.Value] = list;
                        }
                        list.Add(tile);
                        break;
                    }
                    current = next.Value;
                }
            }

            return regions;
        }

        /// <summary>A leggyakoribb biome egy tile-halmazban.</summary>
        public static Biome DominantBiome(IEnumerable<TileId> tiles, Dictionary<TileId, Biome> biomeOf)
        {
            var counts = new Dictionary<Biome, int>();
            foreach (TileId t in tiles)
            {
                Biome b = biomeOf[t];
                counts[b] = counts.TryGetValue(b, out int c) ? c + 1 : 1;
            }
            return counts.OrderByDescending(kv => kv.Value).First().Key;
        }
    }
}
