using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// ND-82: a meglévő víz-base-fedés immutábilis forrása. Nem ocean classifier:
    /// a hívó a tényleges víz-emisszió TileId-jait és tengerszintsugarát adja át.
    /// A terep cutjától, magasságától és munkakeretétől független.
    /// </summary>
    public sealed class WaterLodSource
    {
        private readonly bool[] _waterRoots;
        private readonly TerrainLodProxy _surface;
        public int BaseLevel { get; }
        public double SeaRadius { get; }
        public int WaterRootCount { get; }
        public long MaskStorageBytes => _waterRoots.Length * sizeof(bool);
        public long ProxyStorageBytes => _surface.StorageBytes;

        public WaterLodSource(int baseLevel, double seaRadius, IEnumerable<TileId> waterRoots)
        {
            if (baseLevel < 0 || baseLevel > 8) throw new ArgumentOutOfRangeException(nameof(baseLevel));
            if (!(seaRadius > 0) || double.IsInfinity(seaRadius)) throw new ArgumentOutOfRangeException(nameof(seaRadius));
            if (waterRoots == null) throw new ArgumentNullException(nameof(waterRoots));
            BaseLevel = baseLevel;
            SeaRadius = seaRadius;
            int baseSide = 1 << baseLevel;
            _waterRoots = new bool[6 * baseSide * baseSide];
            int rootCount = 0;
            foreach (TileId root in waterRoots)
            {
                if (root.Level != baseLevel) throw new ArgumentException("Csak víz-base-tile adható át.", nameof(waterRoots));
                int index = TerrainIndexMask.DenseIndex(root);
                if (!_waterRoots[index]) { _waterRoots[index] = true; rootCount++; }
            }
            WaterRootCount = rootCount;
            // A meglévő, tesztelt metrika állandó sugarú esete. Nincs új
            // világmintavétel; a sűrű tárolás további optimalizálása külön feladat.
            int side = (1 << baseLevel) + 1;
            var radii = new double[6 * side * side];
            for (int i = 0; i < radii.Length; i++) radii[i] = seaRadius;
            _surface = new TerrainLodProxy(baseLevel, radii, seaRadius);
        }

        public bool ContainsWater(TileId tile)
        {
            if (tile.Level < BaseLevel) return false;
            while (tile.Level > BaseLevel) tile = tile.Parent();
            return _waterRoots[TerrainIndexMask.DenseIndex(tile)];
        }

        /// <summary>
        /// Csak a víz geometriai részletigényét számolja. A split/merge szögek
        /// a víz saját pixelcéljából származzanak, nem a terep munkakeretéből.
        /// Megszakítás/kivétel nem módosítja a korábbi eredményt.
        /// </summary>
        public WaterLodSelection Select(ProjectedLodView view, WaterLodSelection? previous,
            int maxLevel, double splitThresholdRadians, double mergeThresholdRadians,
            int maxLeafCount, int maxNewSplits, CancellationToken cancellation = default)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (previous != null && !ReferenceEquals(previous.Source, this))
                throw new ArgumentException("Új víz-snapshothoz új kiválasztás kell.", nameof(previous));
            if (maxLevel < BaseLevel || maxLevel > TileId.MaxLevel) throw new ArgumentOutOfRangeException(nameof(maxLevel));
            if (!(splitThresholdRadians > 0 && splitThresholdRadians < Math.PI / 2))
                throw new ArgumentOutOfRangeException(nameof(splitThresholdRadians));
            if (!(mergeThresholdRadians > 0 && mergeThresholdRadians < splitThresholdRadians))
                throw new ArgumentOutOfRangeException(nameof(mergeThresholdRadians));
            if (maxLeafCount < 1 || maxLeafCount > int.MaxValue / 3) throw new ArgumentOutOfRangeException(nameof(maxLeafCount));
            if (maxNewSplits < 1) throw new ArgumentOutOfRangeException(nameof(maxNewSplits));
            cancellation.ThrowIfCancellationRequested();
            var work = new LodSelectionWork(view, maxNewSplits, cancellation,
                skipStaticBase: tile => !_waterRoots[TerrainIndexMask.DenseIndex(tile)]);
            SurfacePoint camera = view.Camera;
            var cut = WaterRootCount == 0 ? new HashSet<TileId>() : AdaptiveQuadTree.BuildCut(
                camera.X, camera.Y, camera.Z, SeaRadius,
                previous != null ? previous.Leaves : Array.Empty<TileId>(),
                BaseLevel, maxLevel, splitThresholdRadians, mergeThresholdRadians,
                maxLeafCount: maxLeafCount, traversalRootLevel: Math.Min(3, BaseLevel),
                staticBaseLevel: BaseLevel, terrainProxy: _surface, work: work);
            cancellation.ThrowIfCancellationRequested();
            var coverage = LodCoverage.Complete(cut, BaseLevel);
            cancellation.ThrowIfCancellationRequested();
            // Az itt használt projected út teljes testvérfedést őriz. Ha ez
            // később változna, nem publikálunk a keretet túllépő tervet.
            if (coverage.Leaves.Count > maxLeafCount)
                throw new InvalidOperationException("A teljes vízfedés túllépte a levélkeretet.");
            return new WaterLodSelection(this, coverage, work);
        }

        // A resolver a határ másik oldalán az érintetlen víz-alapsíkra is
        // illeszt. Geometriát ott is kérhet, ahol nincs rajzolható víz-tile.
        internal SurfaceQuad QuadAt(TileId tile) => _surface.QuadAt(tile);
    }

    /// <summary>
    /// Külön víz-LOD eredmény és varratillesztett geometriai terv; még nem
    /// renderpublikáció. A statikus víz csak sikeres mesh-csere után rejthető el.
    /// </summary>
    public sealed class WaterLodSelection
    {
        private readonly LodCoverage _coverage;
        internal WaterLodSource Source { get; }
        public ReadOnlyCollection<TileId> Leaves { get; }
        public ReadOnlyCollection<TileId> ReplacedRoots { get; }
        public int NewSplits { get; }
        public int DeferredSplits { get; }
        public int DeepestLevel { get; }
        public bool RefinementPending => DeferredSplits > 0;
        public double SelectionMs { get; }
        public double BalanceMs { get; }

        internal WaterLodSelection(WaterLodSource source, LodCoverage coverage, LodSelectionWork work)
        {
            Source = source;
            _coverage = coverage;
            Leaves = SortedSnapshot(coverage.Leaves);
            ReplacedRoots = SortedSnapshot(coverage.Roots);
            int deepest = source.BaseLevel;
            foreach (TileId leaf in Leaves) deepest = Math.Max(deepest, leaf.Level);
            DeepestLevel = deepest;
            NewSplits = work.NewSplits; DeferredSplits = work.DeferredSplits;
            SelectionMs = work.SelectionMs; BalanceMs = work.BalanceMs;
        }

        private static ReadOnlyCollection<TileId> SortedSnapshot(IEnumerable<TileId> values)
        {
            var result = new List<TileId>(values);
            result.Sort((a,b) => a.Value.CompareTo(b.Value));
            return result.AsReadOnly();
        }

        /// <summary>A mintacella legalább DeepestLevel szintű legyen.</summary>
        public bool TryFindRenderedLeaf(TileId sample, out TileId leaf)
        {
            leaf = default;
            if (!Source.ContainsWater(sample)) return false;
            if (sample.Level < DeepestLevel) throw new ArgumentException("Túl durva vízmintacella.", nameof(sample));
            leaf = _coverage.FindRenderedLeaf(sample, Source.BaseLevel);
            return true;
        }

        /// <summary>Kérésenként/worker-szálanként külön resolver; nem megosztott cache.</summary>
        public LodCornerResolver CreateCornerResolver()
            => new LodCornerResolver(Source.BaseLevel, _coverage, Source.QuadAt);
    }
}
