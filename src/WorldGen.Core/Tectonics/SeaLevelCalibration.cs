using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M4 tengerszint-kalibráció + kontinens-számlálás (§4.4,
    /// docs/05-milestones.md) — a `TEST-EARTH-001` elfogadási kritérium
    /// (50-75% víz, több kontinens) numerikus alapja.
    ///
    /// MÓDSZER: a tengerszintet a magasság-eloszlás PERCENTILISE határozza
    /// meg (nem fix méter-érték) — ez automatikusan biztosítja a célzott
    /// víz-arányt, függetlenül attól, hogy a nyers elevációeloszlás éppen
    /// hogyan alakul.
    /// </summary>
    public static class SeaLevelCalibration
    {
        /// <summary>Minden tile elevációja egy adott (worldSeed, plateCount, level) világon.</summary>
        public static Dictionary<TileId, double> ComputeElevationField(ulong worldSeed, int plateCount, int level)
        {
            var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);
            uint n = level == 0 ? 1u : (1u << level);
            var field = new Dictionary<TileId, double>();

            for (int face = 0; face <= 5; face++)
            {
                for (uint u = 0; u < n; u++)
                {
                    for (uint v = 0; v < n; v++)
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.ToPosition(id, out double x, out double y, out double z);
                        int plateId = PlateGeneration.AssignPlate(x, y, z, seeds);
                        double elevation = PlateBoundaryEffect.ElevationWithBoundary(
                            worldSeed, plateId, id.Value, x, y, z, seeds, out _);
                        field[id] = elevation;
                    }
                }
            }
            return field;
        }

        /// <summary>A percentilis-érték, ami a célzott víz-arányt adja.</summary>
        public static double CalibrateSeaLevel(IEnumerable<double> elevations, double targetWaterFraction)
        {
            var sorted = new List<double>(elevations);
            sorted.Sort();
            if (sorted.Count == 0)
                throw new ArgumentException("Üres eleváció-mező.", nameof(elevations));

            int idx = (int)(targetWaterFraction * sorted.Count);
            idx = Math.Max(0, Math.Min(sorted.Count - 1, idx));
            return sorted[idx];
        }

        /// <summary>Összefüggő szárazföld-komponensek (szélességi bejárás), min. méret szerint szűrve.</summary>
        public static List<List<TileId>> CountContinents(Dictionary<TileId, double> field, double seaLevel, int minSize)
        {
            var land = new HashSet<TileId>();
            foreach (var kv in field)
                if (kv.Value >= seaLevel)
                    land.Add(kv.Key);

            var visited = new HashSet<TileId>();
            var components = new List<List<TileId>>();

            foreach (TileId start in land)
            {
                if (visited.Contains(start))
                    continue;

                var component = new List<TileId>();
                var queue = new Queue<TileId>();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    TileId current = queue.Dequeue();
                    component.Add(current);

                    TileNeighbors.GetAll(current, out TileId right, out TileId left, out TileId up, out TileId down);
                    CheckNeighbor(right, land, visited, queue);
                    CheckNeighbor(left, land, visited, queue);
                    CheckNeighbor(up, land, visited, queue);
                    CheckNeighbor(down, land, visited, queue);
                }
                components.Add(component);
            }

            var sized = new List<List<TileId>>();
            foreach (var c in components)
                if (c.Count >= minSize)
                    sized.Add(c);
            return sized;
        }

        private static void CheckNeighbor(TileId candidate, HashSet<TileId> land, HashSet<TileId> visited, Queue<TileId> queue)
        {
            if (land.Contains(candidate) && !visited.Contains(candidate))
            {
                visited.Add(candidate);
                queue.Enqueue(candidate);
            }
        }
    }
}
