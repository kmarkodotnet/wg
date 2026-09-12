using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>ND-96: egy worker világfüggő geometriája; nincs Unity- vagy új Core-minta.</summary>
    public sealed class LodGeometryCache
    {
        private readonly TerrainLodProxy _proxy;
        private readonly int _capacity;
        private readonly Dictionary<TileId, (SurfaceLodBounds Bounds, SurfaceQuad Quad, bool HasQuad)> _proxyGeometry = new();
        private readonly Dictionary<TileId, SurfaceQuad> _actual = new();
        private readonly Dictionary<TileId, (SurfaceLodBounds Bounds, int Revision)> _knownBounds = new();
        private readonly HashSet<TileId> _hasDescendantFeedback = new();
        public int Revision { get; private set; }
        public int Count => _proxyGeometry.Count;
        public int ActualCount => _actual.Count;
        public int BoundsCount => _knownBounds.Count;
        public long Hits { get; private set; }
        public long Computed { get; private set; }
        public long RejectedFeedback { get; private set; }

        public LodGeometryCache(TerrainLodProxy proxy, int capacity = 262144)
        {
            _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public bool Matches(TerrainLodProxy proxy) => ReferenceEquals(_proxy, proxy);

        public int RevisionAt(TileId tile) => _knownBounds.TryGetValue(tile, out var known) ? known.Revision : 0;

        public bool Evaluate(ProjectedLodView view, TileId tile, out double error)
        {
            if (!_proxyGeometry.TryGetValue(tile, out var geometry))
            {
                Computed++;
                geometry = (_proxy.BoundsAt(tile), default, false);
                if (_proxyGeometry.Count < _capacity) _proxyGeometry.Add(tile, geometry);
            }
            else Hits++;
            bool visible = view.Evaluate(geometry.Bounds, out error);
            if (visible && tile.Level >= _proxy.BaseLevel)
            {
                // Az első nézetből kizárt tile sarkait csak későbbi láthatóságkor számoljuk.
                if (!geometry.HasQuad)
                {
                    geometry.Quad = _proxy.QuadAt(tile);
                    geometry.HasQuad = true;
                    if (_proxyGeometry.ContainsKey(tile)) _proxyGeometry[tile] = geometry;
                }
                view.EvaluateVisibleQuad(geometry.Quad, ref error);
            }
            if (_knownBounds.TryGetValue(tile, out var known))
            {
                // A proxy által kizárt valódi csúcs az ősei mögött sem veszhet el.
                // Egy korábban mért lapos szülőquad nem fedheti el a később
                // megismert, nagyobb kiterjedésű gyermekét: ott az összesített bounds mérvadó.
                bool knownVisible = _actual.TryGetValue(tile, out var actual) && !_hasDescendantFeedback.Contains(tile)
                    ? view.EvaluateGeometry(known.Bounds, actual, true, out double actualError)
                    : view.Evaluate(known.Bounds, out actualError);
                if (knownVisible) error = Math.Max(error, actualError);
                visible |= knownVisible;
            }
            return visible;
        }

        public bool Record(TileId tile, SurfaceQuad quad)
        {
            if (_actual.TryGetValue(tile, out var old) && SameQuad(old, quad)) return false;
            int missing = 0;
            TileId ancestor = tile;
            while (true)
            {
                if (!_knownBounds.ContainsKey(ancestor)) missing++;
                if (ancestor.Level == 0) break;
                ancestor = ancestor.Parent();
            }
            // A teljes őslánc vagy elfér, vagy semmit nem módosítunk.
            if (_knownBounds.Count + missing > _capacity) { RejectedFeedback++; return false; }
            var bounds = SurfaceLodBounds.FromQuad(quad);
            _actual[tile] = quad;
            int revision = Revision + 1;
            ancestor = tile;
            while (true)
            {
                if (!ancestor.Equals(tile)) _hasDescendantFeedback.Add(ancestor);
                _knownBounds[ancestor] = (_knownBounds.TryGetValue(ancestor, out var prior) ? Union(prior.Bounds, bounds) : bounds, revision);
                if (ancestor.Level == 0) break;
                ancestor = ancestor.Parent();
            }
            Revision = revision;
            return true;
        }

        private static double Distance(SurfacePoint a, SurfacePoint b)
        { double x = a.X-b.X, y = a.Y-b.Y, z = a.Z-b.Z; return Math.Sqrt(x*x+y*y+z*z); }

        // ND-97: nincs ValueType.Equals miatti boxing/reflexió és nincs geometriai tolerancia.
        private static bool SameQuad(SurfaceQuad a, SurfaceQuad b)
            => SamePoint(a.P00,b.P00) && SamePoint(a.P10,b.P10) && SamePoint(a.P11,b.P11) && SamePoint(a.P01,b.P01);
        private static bool SamePoint(SurfacePoint a, SurfacePoint b)
            => a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z);

        private static SurfaceLodBounds Union(SurfaceLodBounds a, SurfaceLodBounds b)
        {
            double distance = Distance(a.Center, b.Center);
            if (a.Radius >= distance + b.Radius) return a;
            if (b.Radius >= distance + a.Radius) return b;
            double radius = (distance + a.Radius + b.Radius) * .5;
            var center = a.Center + (b.Center + a.Center * -1) * ((radius-a.Radius)/distance);
            return new SurfaceLodBounds(center.X, center.Y, center.Z, radius);
        }
    }
}
