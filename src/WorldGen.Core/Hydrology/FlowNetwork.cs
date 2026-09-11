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
        private readonly struct FloodQueueNode
        {
            public readonly double Elevation;
            public readonly long Counter;
            public readonly TileId Tile;

            public FloodQueueNode(double elevation, long counter, TileId tile)
            {
                Elevation = elevation;
                Counter = counter;
                Tile = tile;
            }
        }

        /// <summary>
        /// ND-65: ugyanazt az (elevation, counter) teljes rendezést megvalósító
        /// bináris minimum-heap, mint a korábbi SortedSet. A TileId közvetlenül
        /// az elemben van, ezért nem kell külön counter-&gt;tile Dictionary.
        /// </summary>
        private sealed class FloodMinHeap
        {
            private readonly List<FloodQueueNode> _items;

            public FloodMinHeap(int capacity = 0)
            {
                _items = new List<FloodQueueNode>(capacity);
            }

            public int Count => _items.Count;

            public void Push(FloodQueueNode node)
            {
                int index = _items.Count;
                _items.Add(node);
                while (index > 0)
                {
                    int parent = (index - 1) >> 1;
                    if (!Less(node, _items[parent]))
                        break;
                    _items[index] = _items[parent];
                    index = parent;
                }
                _items[index] = node;
            }

            public FloodQueueNode Pop()
            {
                if (_items.Count == 0)
                    throw new InvalidOperationException("Üres priority-flood heap.");

                FloodQueueNode result = _items[0];
                int lastIndex = _items.Count - 1;
                FloodQueueNode last = _items[lastIndex];
                _items.RemoveAt(lastIndex);
                if (lastIndex == 0)
                    return result;

                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= _items.Count)
                        break;
                    int right = left + 1;
                    int smaller = right < _items.Count && Less(_items[right], _items[left])
                        ? right
                        : left;
                    if (!Less(_items[smaller], last))
                        break;
                    _items[index] = _items[smaller];
                    index = smaller;
                }
                _items[index] = last;
                return result;
            }

            private static bool Less(FloodQueueNode left, FloodQueueNode right)
            {
                int elevationOrder = left.Elevation.CompareTo(right.Elevation);
                return elevationOrder < 0
                    || (elevationOrder == 0 && left.Counter.CompareTo(right.Counter) < 0);
            }
        }

        private readonly struct DenseFloodQueueNode
        {
            public readonly double Elevation;
            public readonly long Counter;
            public readonly int Index;

            public DenseFloodQueueNode(double elevation, long counter, int index)
            {
                Elevation = elevation;
                Counter = counter;
                Index = index;
            }
        }

        private sealed class DenseFloodMinHeap
        {
            private readonly List<DenseFloodQueueNode> _items;

            public DenseFloodMinHeap(int capacity)
            {
                _items = new List<DenseFloodQueueNode>(capacity);
            }

            public int Count => _items.Count;

            public void Push(DenseFloodQueueNode node)
            {
                int index = _items.Count;
                _items.Add(node);
                while (index > 0)
                {
                    int parent = (index - 1) >> 1;
                    if (!Less(node, _items[parent]))
                        break;
                    _items[index] = _items[parent];
                    index = parent;
                }
                _items[index] = node;
            }

            public DenseFloodQueueNode Pop()
            {
                if (_items.Count == 0)
                    throw new InvalidOperationException("Ures dense priority-flood heap.");

                DenseFloodQueueNode result = _items[0];
                int lastIndex = _items.Count - 1;
                DenseFloodQueueNode last = _items[lastIndex];
                _items.RemoveAt(lastIndex);
                if (lastIndex == 0)
                    return result;

                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= _items.Count)
                        break;
                    int right = left + 1;
                    int smaller = right < _items.Count && Less(_items[right], _items[left])
                        ? right
                        : left;
                    if (!Less(_items[smaller], last))
                        break;
                    _items[index] = _items[smaller];
                    index = smaller;
                }
                _items[index] = last;
                return result;
            }

            private static bool Less(DenseFloodQueueNode left, DenseFloodQueueNode right)
            {
                int elevationOrder = left.Elevation.CompareTo(right.Elevation);
                return elevationOrder < 0
                    || (elevationOrder == 0 && left.Counter.CompareTo(right.Counter) < 0);
            }
        }

        /// <summary>
        /// ND-67: egy teljes, fix levelu cubed-sphere racs vilagfuggetlen,
        /// face/u/v sorrendu topologiaja. A szomszedok egyszer szamolodnak,
        /// deep-time rebuildenkent mar csak tombindexek olvasodnak.
        /// </summary>
        public sealed class DenseGridTopology
        {
            private readonly TileId[] _tiles;
            private readonly int[] _right;
            private readonly int[] _left;
            private readonly int[] _up;
            private readonly int[] _down;

            private DenseGridTopology(
                int level, TileId[] tiles,
                int[] right, int[] left, int[] up, int[] down)
            {
                Level = level;
                _tiles = tiles;
                _right = right;
                _left = left;
                _up = up;
                _down = down;
            }

            public int Level { get; }
            public int Count => _tiles.Length;

            public TileId TileAt(int index) => _tiles[index];

            public int IndexOf(TileId id)
            {
                if (id.Level != Level)
                    throw new ArgumentException("A TileId szintje nem egyezik a suru topologia szintjevel.", nameof(id));
                int n = 1 << Level;
                return IndexOfUnchecked(id, n);
            }

            public int NeighborIndex(int index, TileDirection direction)
            {
                switch (direction)
                {
                    case TileDirection.Right: return _right[index];
                    case TileDirection.Left: return _left[index];
                    case TileDirection.Up: return _up[index];
                    case TileDirection.Down: return _down[index];
                    default: throw new ArgumentOutOfRangeException(nameof(direction));
                }
            }

            public static DenseGridTopology Create(int level)
            {
                if (level < 0 || level > 30)
                    throw new ArgumentOutOfRangeException(nameof(level));

                long nLong = 1L << level;
                long countLong = checked(6L * nLong * nLong);
                if (countLong > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(level), "A suru topologia elemszama nem fer int indexbe.");

                int n = (int)nLong;
                int faceStride = checked(n * n);
                int count = (int)countLong;
                var tiles = new TileId[count];
                var right = new int[count];
                var left = new int[count];
                var up = new int[count];
                var down = new int[count];

                System.Threading.Tasks.Parallel.For(0, count, index =>
                {
                    int face = index / faceStride;
                    int faceIndex = index - face * faceStride;
                    uint u = (uint)(faceIndex / n);
                    uint v = (uint)(faceIndex - (int)u * n);
                    TileId tile = TileId.FromFaceLevelUV(face, level, u, v);
                    tiles[index] = tile;

                    TileNeighbors.GetAll(tile, out TileId r, out TileId l, out TileId t, out TileId b);
                    right[index] = IndexOfUnchecked(r, n);
                    left[index] = IndexOfUnchecked(l, n);
                    up[index] = IndexOfUnchecked(t, n);
                    down[index] = IndexOfUnchecked(b, n);
                });

                return new DenseGridTopology(level, tiles, right, left, up, down);
            }

            private static int IndexOfUnchecked(TileId id, int n)
            {
                id.GetUV(out uint u, out uint v);
                return id.Face * n * n + (int)u * n + (int)v;
            }
        }

        public sealed class DenseFloodResult
        {
            public double[] Filled = Array.Empty<double>();
            public int[] ParentIndex = Array.Empty<int>();
            public int[] FloodOrder = Array.Empty<int>();

            public Dictionary<TileId, double> ToFilledDictionary(DenseGridTopology topology)
            {
                if (topology == null)
                    throw new ArgumentNullException(nameof(topology));
                if (topology.Count != Filled.Length)
                    throw new ArgumentException("A topologia es a dense flood eredmeny merete elter.", nameof(topology));

                var result = new Dictionary<TileId, double>(Filled.Length);
                for (int i = 0; i < Filled.Length; i++)
                    result[topology.TileAt(i)] = Filled[i];
                return result;
            }
        }

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

            // (Elevation, Counter) a rendezesi kulcs - a Counter garantaltan
            // egyedi. Az ND-65 heap ugyanazt a teljes rendezest hasznalja, de
            // a TileId-t kozvetlenul tarolja, kulon dictionary nelkul.
            var queue = new FloodMinHeap(field.Count);
            long counter = 0;

            foreach (var kv in isOcean)
            {
                if (!kv.Value) continue;
                TileId t = kv.Key;
                filled[t] = field[t];
                parent[t] = null;
                visited.Add(t);
                queue.Push(new FloodQueueNode(field[t], counter, t));
                counter++;
            }

            int order = 0;
            while (queue.Count > 0)
            {
                FloodQueueNode min = queue.Pop();
                TileId current = min.Tile;
                floodOrder[current] = order++;

                TileNeighbors.GetAll(current, out TileId right, out TileId left, out TileId up, out TileId down);
                TryFlood(right, current, min.Elevation, field, filled, parent, visited, queue, ref counter);
                TryFlood(left, current, min.Elevation, field, filled, parent, visited, queue, ref counter);
                TryFlood(up, current, min.Elevation, field, filled, parent, visited, queue, ref counter);
                TryFlood(down, current, min.Elevation, field, filled, parent, visited, queue, ref counter);
            }

            return new FloodResult { Filled = filled, Parent = parent, FloodOrder = floodOrder };
        }

        /// <summary>
        /// ND-67: a Dictionary-alapu PriorityFlood egzakt, suru teljes-racs
        /// megfeleloje. A -1 parent ocean-gyokeret, a -2 el nem ert elemet
        /// jelent; legalabb egy ocean-gyoker kotelezo.
        /// </summary>
        public static DenseFloodResult PriorityFloodDense(
            DenseGridTopology topology, double[] field, bool[] isOcean)
        {
            if (topology == null)
                throw new ArgumentNullException(nameof(topology));
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (isOcean == null)
                throw new ArgumentNullException(nameof(isOcean));
            if (field.Length != topology.Count || isOcean.Length != topology.Count)
                throw new ArgumentException("A dense field, ocean-mezo es topologia merete meg kell egyezzen.");

            int count = field.Length;
            var filled = new double[count];
            var parentIndex = new int[count];
            var floodOrder = new int[count];
            var visited = new bool[count];
            Array.Fill(parentIndex, -2);
            Array.Fill(floodOrder, -1);

            var queue = new DenseFloodMinHeap(count);
            long counter = 0;
            for (int i = 0; i < count; i++)
            {
                if (!isOcean[i])
                    continue;
                filled[i] = field[i];
                parentIndex[i] = -1;
                visited[i] = true;
                queue.Push(new DenseFloodQueueNode(field[i], counter, i));
                counter++;
            }

            if (queue.Count == 0)
                throw new ArgumentException("A dense priority-flood legalabb egy ocean-gyokeret igenyel.", nameof(isOcean));

            int order = 0;
            while (queue.Count > 0)
            {
                DenseFloodQueueNode min = queue.Pop();
                int current = min.Index;
                floodOrder[current] = order++;

                TryFloodDense(topology.NeighborIndex(current, TileDirection.Right), current, min.Elevation,
                    field, filled, parentIndex, visited, queue, ref counter);
                TryFloodDense(topology.NeighborIndex(current, TileDirection.Left), current, min.Elevation,
                    field, filled, parentIndex, visited, queue, ref counter);
                TryFloodDense(topology.NeighborIndex(current, TileDirection.Up), current, min.Elevation,
                    field, filled, parentIndex, visited, queue, ref counter);
                TryFloodDense(topology.NeighborIndex(current, TileDirection.Down), current, min.Elevation,
                    field, filled, parentIndex, visited, queue, ref counter);
            }

            return new DenseFloodResult
            {
                Filled = filled,
                ParentIndex = parentIndex,
                FloodOrder = floodOrder,
            };
        }

        private static void TryFloodDense(
            int candidate, int from, double fromElevation,
            double[] field, double[] filled, int[] parentIndex, bool[] visited,
            DenseFloodMinHeap queue, ref long counter)
        {
            if (visited[candidate])
                return;
            visited[candidate] = true;

            double candidateFilled = Math.Max(field[candidate], fromElevation);
            filled[candidate] = candidateFilled;
            parentIndex[candidate] = from;
            queue.Push(new DenseFloodQueueNode(candidateFilled, counter, candidate));
            counter++;
        }

        private static void TryFlood(
            TileId candidate, TileId from, double fromElevation,
            Dictionary<TileId, double> field, Dictionary<TileId, double> filled,
            Dictionary<TileId, TileId?> parent, HashSet<TileId> visited,
            FloodMinHeap queue,
            ref long counter)
        {
            if (visited.Contains(candidate)) return;
            visited.Add(candidate);

            double candidateFilled = Math.Max(field[candidate], fromElevation);
            filled[candidate] = candidateFilled;
            parent[candidate] = from;
            queue.Push(new FloodQueueNode(candidateFilled, counter, candidate));
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

        /// <summary>
        /// A szárazföld ekkora hányada (0..1) legyen folyó-tile - percentilis-
        /// módszer az accumulation-eloszláson. KÖZÖS hely (M8, docs/04-decisions.md) -
        /// korábban ez a logika csak a Unity PlanetGridMesh.cs-ben (megjelenítési
        /// célra) létezett, duplikálva; a folyó-torkolat számláláshoz (M8 panel-
        /// metrika) is szükség van rá, ezért ide, a Core-ba került.
        /// </summary>
        public static HashSet<TileId> SelectRiverTiles(
            Dictionary<TileId, bool> isOcean, Dictionary<TileId, long> accumulation, double riverTargetFraction)
        {
            var landAcc = new List<long>();
            foreach (KeyValuePair<TileId, bool> kv in isOcean)
                if (!kv.Value) landAcc.Add(accumulation[kv.Key]);

            var riverTiles = new HashSet<TileId>();
            if (landAcc.Count == 0)
                return riverTiles;

            landAcc.Sort((a, b) => b.CompareTo(a));
            int idx = Math.Max(0, Math.Min(landAcc.Count - 1, (int)(riverTargetFraction * landAcc.Count)));
            long threshold = landAcc[idx];

            foreach (KeyValuePair<TileId, bool> kv in isOcean)
                if (!kv.Value && accumulation[kv.Key] >= threshold)
                    riverTiles.Add(kv.Key);
            return riverTiles;
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
