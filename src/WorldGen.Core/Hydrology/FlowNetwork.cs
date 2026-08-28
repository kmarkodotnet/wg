using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;

namespace WorldGen.Core.Hydrology
{
    /// <summary>
    /// M7 hidrológia (§33, docs/05-milestones.md): depresszió-feltöltés +
    /// folyásirány + flow accumulation, statikus elevation-mezőn.
    ///
    /// HATÓKÖR (tudatosan szűkítve): nincs időbeli változás (§34), tavak
    /// (§35), jég/hó (§36), iteratív eróziós visszahatás — ld. milestones
    /// "M7 hatókör".
    ///
    /// MÓDSZER: a Priority-Flood algoritmus (Barnes et al.) EGYSZERRE oldja
    /// meg a depresszió-feltöltést ÉS a folyásirány-számítást — minden
    /// tile "árasztási szülője" (aki elárasztotta) automatikusan érvényes
    /// folyásirány-célpont, mert (1) mindig alacsonyabb vagy egyenlő
    /// feltöltött magasságú, és (2) az árasztási sorrendje mindig KISEBB,
    /// mint a gyereké — ez garantálja, hogy a szülő-láncot követve VÉGES
    /// sok lépésben óceánhoz jutunk, hurok nélkül (a láncstruktúra maga
    /// egy fa/erdő, az óceán-tile-ok a gyökerek).
    /// </summary>
    public static class FlowNetwork
    {
        public static Dictionary<TileId, bool> ComputeOceanField(Dictionary<TileId, double> field, double seaLevel)
        {
            var result = new Dictionary<TileId, bool>();
            foreach (var kv in field)
                result[kv.Key] = kv.Value < seaLevel;
            return result;
        }

        public sealed class FloodResult
        {
            public Dictionary<TileId, double> Filled = new Dictionary<TileId, double>();
            public Dictionary<TileId, TileId?> Parent = new Dictionary<TileId, TileId?>();
            public Dictionary<TileId, int> FloodOrder = new Dictionary<TileId, int>();
        }

        public static FloodResult PriorityFlood(Dictionary<TileId, double> field, Dictionary<TileId, bool> isOcean)
        {
            var filled = new Dictionary<TileId, double>();
            var parent = new Dictionary<TileId, TileId?>();
            var floodOrder = new Dictionary<TileId, int>();
            var visited = new HashSet<TileId>();

            // (Elevation, Counter) a rendezési kulcs - a Counter garantáltan
            // egyedi, ezért a SortedSet-nek sosem kell TileId-t
            // összehasonlítania (azt csak egy kulon dictionary-bol nezzuk ki).
            var queue = new SortedSet<(double Elevation, long Counter)>();
            var counterToTile = new Dictionary<long, TileId>();
            long counter = 0;

            foreach (var kv in isOcean)
            {
                if (!kv.Value) continue;
                TileId t = kv.Key;
                filled[t] = field[t];
                parent[t] = null;
                visited.Add(t);
                queue.Add((field[t], counter));
                counterToTile[counter] = t;
                counter++;
            }

            int order = 0;
            while (queue.Count > 0)
            {
                (double Elevation, long Counter) min = queue.Min;
                queue.Remove(min);
                TileId current = counterToTile[min.Counter];
                floodOrder[current] = order++;

                TileNeighbors.GetAll(current, out TileId right, out TileId left, out TileId up, out TileId down);
                TryFlood(right, current, min.Elevation, field, filled, parent, visited, queue, counterToTile, ref counter);
                TryFlood(left, current, min.Elevation, field, filled, parent, visited, queue, counterToTile, ref counter);
                TryFlood(up, current, min.Elevation, field, filled, parent, visited, queue, counterToTile, ref counter);
                TryFlood(down, current, min.Elevation, field, filled, parent, visited, queue, counterToTile, ref counter);
            }

            return new FloodResult { Filled = filled, Parent = parent, FloodOrder = floodOrder };
        }

        private static void TryFlood(
            TileId candidate, TileId from, double fromElevation,
            Dictionary<TileId, double> field, Dictionary<TileId, double> filled,
            Dictionary<TileId, TileId?> parent, HashSet<TileId> visited,
            SortedSet<(double Elevation, long Counter)> queue, Dictionary<long, TileId> counterToTile,
            ref long counter)
        {
            if (visited.Contains(candidate)) return;
            visited.Add(candidate);

            double candidateFilled = Math.Max(field[candidate], fromElevation);
            filled[candidate] = candidateFilled;
            parent[candidate] = from;
            queue.Add((candidateFilled, counter));
            counterToTile[counter] = candidate;
            counter++;
        }

        /// <summary>Minden tile accumulation-je: 1 (saját) + az őt elárasztott (gyerek) tile-ok összege.</summary>
        public static Dictionary<TileId, long> FlowAccumulation(
            Dictionary<TileId, double> field, Dictionary<TileId, TileId?> parent, Dictionary<TileId, int> floodOrder)
        {
            var accumulation = new Dictionary<TileId, long>();
            foreach (TileId k in field.Keys) accumulation[k] = 1;

            var sortedKeys = new List<TileId>(field.Keys);
            sortedKeys.Sort((a, b) => floodOrder[b].CompareTo(floodOrder[a])); // csökkenő flood order

            foreach (TileId k in sortedKeys)
            {
                TileId? p = parent[k];
                if (p.HasValue)
                    accumulation[p.Value] += accumulation[k];
            }
            return accumulation;
        }

        /// <summary>Minden szárazföld-tile-ból a szülő-láncot követve véges lépésben óceánba jutunk-e.</summary>
        public static List<TileId> VerifyAllLandReachesOcean(
            Dictionary<TileId, double> field, Dictionary<TileId, TileId?> parent, Dictionary<TileId, bool> isOcean,
            int maxSteps)
        {
            var failures = new List<TileId>();
            foreach (TileId k in field.Keys)
            {
                if (isOcean[k]) continue;
                TileId current = k;
                int steps = 0;
                bool reachedOcean = false;
                while (true)
                {
                    TileId? next = parent[current];
                    if (!next.HasValue) break; // lánc vége - elvileg csak óceán-tile-nál fordulhat elő
                    current = next.Value;
                    steps++;
                    if (isOcean[current]) { reachedOcean = true; break; }
                    if (steps > maxSteps) break;
                }
                if (!reachedOcean) failures.Add(k);
            }
            return failures;
        }
    }
}
