using System;
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

        /// <summary>
        /// Egy régió (szárazföld-tile-halmaz) durva MORFOLÓGIAI típusa a
        /// régiónév-generáláshoz (§6.1 "morfológiai típusfelismerés"). TISZTA,
        /// determinisztikus geometriai/elevation-aggregáció (nincs új modell,
        /// nincs random) - a szomszédság-tábla (part-tile) és a per-tile
        /// eleváció alapján. HATÓKÖR: heurisztikus, NÉV-ízesítő közelítés (mint
        /// az "Area" tile-számlálás) - a küszöbök dokumentált MVP-értékek,
        /// hangolhatók. Nem geológiai osztályozás.
        /// </summary>
        public enum LandformType { Lowland, Plain, Plateau, Mountains, Basin, Island }

        public static string LandformTypeName(LandformType t) => t switch
        {
            LandformType.Mountains => "mountains",
            LandformType.Plateau => "plateau",
            LandformType.Basin => "basin",
            LandformType.Plain => "plain",
            LandformType.Island => "island",
            _ => "lowland",
        };

        /// <summary>
        /// A régió morfológiai típusa. A döntési sorrend fontos (első találat
        /// nyer): sziget (kicsi + túlnyomóan part) → hegyvidék (magas + tagolt)
        /// → fennsík (magas + lapos) → medence (alacsony belső + magas relief)
        /// → síkság (lapos) → alföld (alapértelmezés).
        /// </summary>
        public static LandformType ClassifyLandform(
            IReadOnlyCollection<TileId> tiles,
            Dictionary<TileId, double> elevationM, Dictionary<TileId, bool> isOcean, double seaLevelM,
            double mountainMeanAboveM = 1200.0, double mountainReliefM = 1500.0,
            double plateauMeanAboveM = 900.0, double plateauReliefMaxM = 700.0,
            double plainReliefMaxM = 400.0,
            double basinMeanAboveMaxM = 150.0, double basinReliefMinM = 600.0,
            int islandMaxTiles = 12, double islandCoastRatioMin = 0.6)
        {
            if (tiles == null || tiles.Count == 0)
                return LandformType.Lowland;

            double sum = 0.0, min = double.PositiveInfinity, max = double.NegativeInfinity;
            int coast = 0;
            foreach (TileId t in tiles)
            {
                double e = elevationM[t];
                sum += e;
                if (e < min) min = e;
                if (e > max) max = e;
                for (int d = 0; d < 4; d++)
                {
                    TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                    if (isOcean.TryGetValue(nb, out bool o) && o) { coast++; break; }
                }
            }

            double area = tiles.Count;
            double meanAbove = sum / area - seaLevelM;
            double relief = max - min;
            double coastRatio = coast / area;

            if (tiles.Count <= islandMaxTiles && coastRatio >= islandCoastRatioMin)
                return LandformType.Island;
            if (meanAbove >= mountainMeanAboveM && relief >= mountainReliefM)
                return LandformType.Mountains;
            if (meanAbove >= plateauMeanAboveM && relief <= plateauReliefMaxM)
                return LandformType.Plateau;
            if (relief >= basinReliefMinM && meanAbove <= basinMeanAboveMaxM)
                return LandformType.Basin;
            if (relief <= plainReliefMaxM)
                return LandformType.Plain;
            return LandformType.Lowland;
        }
    }
}
