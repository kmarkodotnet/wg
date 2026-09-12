using System;
using System.Collections.Generic;
using System.Threading;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>ND-76: immutábilis perspektivikus kamera, testkoordinátás tengelyekkel.</summary>
    public sealed class ProjectedLodView
    {
        private readonly SurfacePoint _camera, _right, _up, _forward;
        private readonly double _tanX, _tanY, _planeX, _planeY, _near, _far;
        public SurfacePoint Camera => _camera;

        // ND-81: nincs tolerancia; eltérő vetület nem használhat régi mérést.
        public bool SameProjection(ProjectedLodView other) => other != null
            && SamePoint(_camera, other._camera) && SamePoint(_right, other._right)
            && SamePoint(_up, other._up) && SamePoint(_forward, other._forward)
            && _tanX == other._tanX && _tanY == other._tanY
            && _near == other._near && _far == other._far;

        private static bool SamePoint(SurfacePoint a, SurfacePoint b)
            => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

        public ProjectedLodView(SurfacePoint camera, SurfacePoint right, SurfacePoint up,
            SurfacePoint forward, double verticalFov, double aspect, double near, double far)
        {
            if (!(verticalFov > 0 && verticalFov < Math.PI) || !(aspect > 0)
                || !(near > 0 && far > near)) throw new ArgumentOutOfRangeException(nameof(verticalFov));
            _camera = camera; _right = Unit(right); _up = Unit(up); _forward = Unit(forward);
            _tanY = Math.Tan(verticalFov * .5); _tanX = _tanY * aspect;
            _planeX = Math.Sqrt(1 + _tanX * _tanX); _planeY = Math.Sqrt(1 + _tanY * _tanY);
            _near = near; _far = far;
        }

        public bool Evaluate(SurfaceLodBounds bounds, out double angularRadius)
        {
            var offset = bounds.Center + _camera * -1;
            double x = Dot(offset, _right), y = Dot(offset, _up), z = Dot(offset, _forward), r = bounds.Radius;
            angularRadius = 0;
            if (z + r < _near || z - r > _far || Math.Abs(x) - z * _tanX > r * _planeX
                || Math.Abs(y) - z * _tanY > r * _planeY) return false;
            // A near síkot metsző bounds nem kaphat kicsi hibát vagy hamis cullt.
            if (z <= r) { angularRadius = Math.PI * .5; return true; }
            // A vetített gömb ellipszisének nagy féltengelye / fókusztávolság.
            // A képszélen sem használjuk a kisebb, kamera–középpont távolságú becslést.
            double projectedRadius = r * Math.Sqrt(Math.Max(0, x*x + y*y + z*z - r*r)) / (z*z - r*r);
            angularRadius = Math.Atan(projectedRadius);
            return true;
        }

        public bool EvaluateTerrain(TerrainLodProxy proxy, TileId tile, out double angularRadius)
        {
            if (!Evaluate(proxy.BoundsAt(tile), out angularRadius)) return false;
            if (tile.Level >= proxy.BaseLevel) EvaluateVisibleQuad(proxy.QuadAt(tile), ref angularRadius);
            return true;
        }

        internal bool EvaluateGeometry(SurfaceLodBounds bounds, SurfaceQuad q, bool useQuad, out double angularRadius)
        {
            if (!Evaluate(bounds,out angularRadius)) return false;
            if (!useQuad) return true;
            EvaluateVisibleQuad(q, ref angularRadius);
            return true;
        }

        internal void EvaluateVisibleQuad(SurfaceQuad q, ref double angularRadius)
        {
            // A gömb csak láthatósági bounds. Osztáskor a valódi alakú proxy-
            // quad vetületét mérjük: egy súroló lapot nem vastagítunk gömbbé.
            SurfacePoint Project(SurfacePoint p)
            {
                p=p+_camera*-1;
                double z=Dot(p,_forward);
                return z<=_near ? new SurfacePoint(double.NaN,0,0)
                    : new SurfacePoint(Dot(p,_right)/z,Dot(p,_up)/z,0);
            }
            SurfacePoint a=Project(q.P00), b=Project(q.P10), c=Project(q.P11), d=Project(q.P01);
            double DistanceSquared(SurfacePoint p,SurfacePoint r)
            { double x=p.X-r.X,y=p.Y-r.Y; return x*x+y*y; }
            double diameterSquared=Math.Max(Math.Max(DistanceSquared(a,b),DistanceSquared(a,c)),
                Math.Max(Math.Max(DistanceSquared(a,d),DistanceSquared(b,c)),Math.Max(DistanceSquared(b,d),DistanceSquared(c,d))));
            if (!double.IsNaN(diameterSquared)) angularRadius=Math.Atan(Math.Sqrt(diameterSquared)*.5);
        }

        public bool EvaluateQuad(SurfaceQuad quad, out double angularRadius)
        {
            return EvaluateGeometry(SurfaceLodBounds.FromQuad(quad), quad, true, out angularRadius);
        }

        private static SurfacePoint Unit(SurfacePoint p)
        {
            double length = Math.Sqrt(Dot(p, p));
            if (!(length > 0) || double.IsInfinity(length)) throw new ArgumentException("Érvénytelen kameratengely.");
            return p * (1 / length);
        }
        private static double Dot(SurfacePoint a, SurfacePoint b) => a.X*b.X + a.Y*b.Y + a.Z*b.Z;
    }

    /// <summary>ND-81: egyetlen worker pontos metrika-cache-e; nem szálbiztos.</summary>
    public sealed class LodTerrainEvaluationCache
    {
        public const int DefaultCapacity = 262144;
        private ProjectedLodView _view;
        private readonly TerrainLodProxy _proxy;
        private readonly int _capacity;
        public LodGeometryCache Geometry { get; }
        private readonly Dictionary<TileId, (bool Visible, double Error, int Revision)> _values
            = new Dictionary<TileId, (bool Visible, double Error, int Revision)>();
        public int Count => _values.Count;
        public long ViewResets { get; private set; }
        public long FeedbackRefreshes { get; private set; }

        public LodTerrainEvaluationCache(ProjectedLodView view, TerrainLodProxy proxy,
            int capacity = DefaultCapacity, LodGeometryCache? geometry = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            Geometry = geometry ?? new LodGeometryCache(proxy, capacity);
            if (!Geometry.Matches(proxy)) throw new ArgumentException("Eltérő geometriavilág.", nameof(geometry));
        }

        public LodTerrainEvaluationCache Reproject(ProjectedLodView view)
            => new LodTerrainEvaluationCache(view, _proxy, _capacity, Geometry);

        /// <summary>Csak lezárt request után, ugyanazon worker használhatja; régi workkel nem osztható meg.</summary>
        public void ResetView(ProjectedLodView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (_view.SameProjection(view)) return;
            _values.Clear();
            _view = view;
            ViewResets++;
        }
        public bool MatchesProxy(TerrainLodProxy proxy) => ReferenceEquals(_proxy, proxy);

        public bool Matches(ProjectedLodView view, TerrainLodProxy proxy)
            => ReferenceEquals(_proxy, proxy) && _view.SameProjection(view);

        public bool EvaluateTerrain(TerrainLodProxy proxy, TileId tile,
            out double angularRadius, out bool cacheHit)
        {
            if (!ReferenceEquals(proxy, _proxy)) throw new ArgumentException("Eltérő terep-proxy.", nameof(proxy));
            int revision = Geometry.RevisionAt(tile);
            bool present = _values.TryGetValue(tile, out var value);
            if (present && value.Revision == revision)
            {
                cacheHit = true; angularRadius = value.Error; return value.Visible;
            }
            cacheHit = false;
            if (present) FeedbackRefreshes++;
            bool visible = Geometry.Evaluate(_view, tile, out angularRadius);
            if (present || _values.Count < _capacity) _values[tile] = (visible, angularRadius, revision);
            return visible;
        }

        internal bool MatchesView(ProjectedLodView view) => _view.SameProjection(view);
    }

    /// <summary>Kérésenkénti munkakeret; nem a világmodell része.</summary>
    public sealed class LodSelectionWork
    {
        public readonly ProjectedLodView? View;
        public readonly int MaxNewSplits;
        public readonly CancellationToken Cancellation;
        public readonly LodSelectionTrace? Trace;
        public readonly Func<TileId,bool>? SkipStaticBase;
        public readonly LodTerrainEvaluationCache? EvaluationCache;
        public int MetricCacheHits { get; private set; }
        public int MetricEvaluations { get; private set; }
        public double SelectionMs { get; internal set; }
        public double BalanceMs { get; internal set; }
        public int SkippedStaticBases { get; private set; }
        public int NewSplits { get; private set; }
        public int DeferredSplits { get; private set; }
        public LodSelectionWork(ProjectedLodView? view, int maxNewSplits = int.MaxValue,
            CancellationToken cancellation = default, bool captureTrace = false, Func<TileId,bool>? skipStaticBase = null,
            LodTerrainEvaluationCache? evaluationCache = null)
        {
            if (maxNewSplits < 1) throw new ArgumentOutOfRangeException(nameof(maxNewSplits));
            View = view; MaxNewSplits = maxNewSplits; Cancellation = cancellation;
            Trace = captureTrace ? new LodSelectionTrace() : null;
            SkipStaticBase = skipStaticBase;
            if (evaluationCache != null && (view == null || !evaluationCache.MatchesView(view)))
                throw new ArgumentException("Eltérő kamera a metrika-cache-ben.", nameof(evaluationCache));
            EvaluationCache = evaluationCache;
        }
        internal bool EvaluateTerrain(TerrainLodProxy proxy, TileId tile, out double error)
        {
            bool hit = false;
            bool visible = EvaluationCache != null
                ? EvaluationCache.EvaluateTerrain(proxy, tile, out error, out hit)
                : View!.EvaluateTerrain(proxy, tile, out error);
            if (hit) MetricCacheHits++; else MetricEvaluations++;
            return visible;
        }
        internal bool TrySkipStaticBase(TileId tile)
        {
            if (SkipStaticBase==null || !SkipStaticBase(tile)) return false;
            SkippedStaticBases++;
            Trace?.Record(tile,"renderer-base-exclusion",0);
            return true;
        }
        internal bool AllowNewSplit()
        {
            if (NewSplits >= MaxNewSplits) { DeferredSplits++; return false; }
            NewSplits++; return true;
        }
    }

    /// <summary>ND-77: tényleges megállások; a balance/fedés utólag eltérhet ezektől.</summary>
    public sealed class LodSelectionTrace
    {
        public readonly struct Stop
        {
            public readonly string Reason;
            public readonly double Error, Threshold;
            public Stop(string reason, double error, double threshold)
            { Reason=reason; Error=error; Threshold=threshold; }
        }
        private readonly Dictionary<TileId, Stop> _stops = new Dictionary<TileId, Stop>();
        public int Count => _stops.Count;
        internal void Record(TileId tile, string reason, double error, double threshold = double.NaN)
            => _stops[tile] = new Stop(reason,error,threshold);
        public bool TryFindStop(TileId tile, out TileId stoppedAncestor, out Stop stop)
        {
            stoppedAncestor=tile;
            while (true)
            {
                if (_stops.TryGetValue(stoppedAncestor,out stop)) return true;
                if (stoppedAncestor.Level==0) return false;
                stoppedAncestor=stoppedAncestor.Parent();
            }
        }
    }

    public static class LodRequestPolicy
    {
        /// <summary>Egyszeri előzés két publikálás között: nincs megszakítási éhezés.</summary>
        public static bool ShouldSupersede(in AdaptiveViewState requested, in AdaptiveViewState current,
            double surfaceAltitude, double ageSeconds, bool alreadySuperseded)
        {
            if (alreadySuperseded || ageSeconds < .12) return false;
            if (requested.VerticalFovRadians != current.VerticalFovRadians || requested.Aspect != current.Aspect
                || requested.PixelWidth != current.PixelWidth || requested.PixelHeight != current.PixelHeight) return true;
            double dx = requested.X-current.X, dy = requested.Y-current.Y, dz = requested.Z-current.Z;
            double limit = Math.Max(.02, Math.Abs(surfaceAltitude)*.2);
            double fx = requested.ForwardX-current.ForwardX, fy = requested.ForwardY-current.ForwardY,
                fz = requested.ForwardZ-current.ForwardZ;
            return dx*dx+dy*dy+dz*dz > limit*limit || fx*fx+fy*fy+fz*fz > .0025;
        }
    }
}
