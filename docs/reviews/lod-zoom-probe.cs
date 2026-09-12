using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using WorldGen.Viewer.Lod;

// Diagnosztikai próba, nem szimulációs modul és nem Unity-renderteszt.
// A cut/chunk kód linkelt; a geometriai mintavétel double referencia-
// számítás. A hiányzó zajréteg hatását külön mérjük, GPU-futtatás nélkül.
internal static class Program
{
    private const double R = 100;
    private const double Fov = Math.PI / 3;
    private const double Aspect = 16.0 / 9;
    private const ulong Seed = 184482873278464;
    private static readonly (double X, double Y, double Z)[] Seeds = PlateMotion.MovedSeeds(Seed, PlateGeneration.GenerateSeeds(Seed, 20), 0);
    private static readonly double Sea = SeaLevelCalibration.CalibrateSeaLevel(SeaLevelCalibration.ComputeElevationFieldAtTime(Seed, 20, 5, 0).Values, 0.65);
    private static readonly V Axis = new V(0.017, -0.937, 0.349).Unit;

    private static HashSet<TileId> Cut(double d, double height = 1080, int budget = 200000, HashSet<TileId> previous = null, double pixels = 12)
    {
        V c = Axis * d;
        double split = pixels / 2 * Fov / height;
        double half = Math.Atan(Math.Tan(Fov / 2) * Math.Sqrt(1 + Aspect * Aspect)) * 1.3;
        return AdaptiveQuadTree.BuildCut(c.X, c.Y, c.Z, R, previous, 8, 20, split, split / 1.5,
            -Axis.X, -Axis.Y, -Axis.Z, half, budget, traversalRootLevel: 3, staticBaseLevel: 8);
    }

    private static int Cover(V p, HashSet<TileId> cut)
    {
        TileId tile = TileGeometry.FromPosition(p.X, p.Y, p.Z, 20);
        while (tile.Level > 8) { if (cut.Contains(tile)) return tile.Level; tile = tile.Parent(); }
        return 8;
    }

    private static List<V> ScreenPoints(double d)
    {
        V camera = Axis * d, forward = Axis * -1;
        V right = V.Cross(forward, new V(0, 0, 1)).Unit;
        V up = V.Cross(right, forward).Unit;
        var points = new List<V>();
        for (int y = 0; y < 23; y++) for (int x = 0; x < 41; x++)
        {
            V dir = (forward + right * ((2 * (x + 0.5) / 41 - 1) * Aspect * Math.Tan(Fov / 2))
                + up * ((2 * (y + 0.5) / 23 - 1) * Math.Tan(Fov / 2))).Unit;
            double b = V.Dot(camera, dir), disc = b * b - (d * d - R * R);
            if (disc >= 0) points.Add((camera + dir * (-b - Math.Sqrt(disc))).Unit);
        }
        return points;
    }

    private static double Elevation(V p)
    {
        DomainWarp.WarpPosition(Seed, p.X, p.Y, p.Z, out double wx, out double wy, out double wz);
        int plate = PlateGeneration.AssignPlate(wx, wy, wz, Seeds);
        return CrustElevation.BaseElevation(Seed, plate, p.X, p.Y, p.Z, out _)
            + PlateBoundaryEffect.BoundaryUpliftFromWarped(Seed, p.X, p.Y, p.Z, wx, wy, wz, Seeds);
    }
    private static V Point(int face, double u, double v)
    { TileGeometry.PositionFromFaceUV(face, u, v, out double x, out double y, out double z); return new V(x, y, z); }
    private static V Displace(V p) => p * (R + (Sea + (Elevation(p) - Sea) * 1.5) * 0.001);

    private static double RayTriangle(V dir, V a, V b, V c)
    {
        V e1 = b - a, e2 = c - a, h = V.Cross(dir, e2);
        double det = V.Dot(e1, h);
        if (Math.Abs(det) < 1e-12) return double.NaN;
        double f = 1 / det;
        V s = a * -1;
        double u = f * V.Dot(s, h);
        V q = V.Cross(s, e1);
        double v = f * V.Dot(dir, q);
        if (u < -1e-8 || v < -1e-8 || u + v > 1 + 1e-8) return double.NaN;
        return f * V.Dot(e2, q);
    }

    private static void ProbeWaterReuse()
    {
        var roots = new List<TileId>();
        for (int face=0;face<6;face++) for(uint u=0;u<256;u++) for(uint v=0;v<256;v++)
            roots.Add(TileId.FromFaceLevelUV(face,8,u,v));
        var source = new WaterLodSource(8,R,roots);
        double split = AdaptiveViewState.AngularRadiusForPixelDiameter(8,Fov,688);
        ProjectedLodView View(double d) => new ProjectedLodView(new SurfacePoint(d,0,0),
            new SurfacePoint(0,0,1),new SurfacePoint(0,1,0),new SurfacePoint(-1,0,0),Fov,1238.0/688,.01,1000);
        WaterLodSelection Step(ProjectedLodView view,WaterLodSelection previous,
            LodTerrainEvaluationCache cache,bool reuse) => source.Select(view,previous,20,split,split/1.5,8192,256,
                evaluationCache:cache,reuseStableSelection:reuse);
        // JIT-bemelegítés, a forráskészítés és a kiértékelés-ellenőrzés nincs az időben.
        var warmView=View(160);var warmCache=source.CreateEvaluationCache(warmView,null);
        var warm=Step(warmView,null,warmCache,false);Step(warmView,warm,warmCache,true);
        foreach(bool moving in new[]{false,true})
        {
            WaterLodSelection reference=null, reused=null;
            LodTerrainEvaluationCache referenceCache=null,reusedCache=null;
            long referenceBytes=0,reusedBytes=0;double referenceMs=0,reusedMs=0;
            int reuseCount=0,maxLeaves=0,pendingCount=0;
            var timer=new Stopwatch();
            int pairs=moving?60:120;
            for(int i=0;i<pairs;i++)
            {
                double d=moving?(i<30?200-i*3:113+(i-30)*3):new[]{300.0,160,120,105}[i/30];
                var view=View(d);
                referenceCache=source.CreateEvaluationCache(view,referenceCache);
                reusedCache=source.CreateEvaluationCache(view,reusedCache);
                void Run(bool fast)
                {
                    long before=GC.GetAllocatedBytesForCurrentThread();timer.Restart();
                    if(fast) reused=Step(view,reused,reusedCache,true);
                    else reference=Step(view,reference,referenceCache,false);
                    timer.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
                    if(fast){reusedMs+=timer.Elapsed.TotalMilliseconds;reusedBytes+=bytes;}
                    else {referenceMs+=timer.Elapsed.TotalMilliseconds;referenceBytes+=bytes;}
                }
                // Külön előzménylánc; váltakozó sorrend a melegítési torzítás csökkentésére.
                if(i%2==0){Run(false);Run(true);}else{Run(true);Run(false);}
                if(!reference.Leaves.SequenceEqual(reused.Leaves)
                    || !reference.ReplacedRoots.SequenceEqual(reused.ReplacedRoots)
                    || reference.RefinementPending!=reused.RefinementPending)
                    throw new InvalidOperationException($"ND98 water mismatch at {i}");
                if(reused.ReusedSelection)reuseCount++;
                if(reused.RefinementPending)pendingCount++;
                maxLeaves=Math.Max(maxLeaves,reused.Leaves.Count);
            }
            Console.WriteLine($"ND98 WATER moving={moving} pairs={pairs} equal=True reused={reuseCount} pending={pendingCount} maxLeaves={maxLeaves} " +
                $"referenceBytes={referenceBytes} reusedBytes={reusedBytes} referenceMs={referenceMs:F2} reusedMs={reusedMs:F2}");
        }
    }

    private static void Main(string[] args)
    {
        if (args.Contains("--water-reuse")) { ProbeWaterReuse(); return; }
        if (args.Contains("--moving-cache")) { ProbeShorelineMetric(movingCache:true); return; }
        if (args.Contains("--storage-cache")) { ProbeShorelineMetric(movingCache:true,reuseStorage:true); return; }
        if (args.Contains("--closure")) { ProbeShorelineMetric(closure:true); return; }
        if (args.Contains("--closure-tuned")) { ProbeShorelineMetric(closure:true,tuned:true); return; }
        if (args.Contains("--work-cache")) { ProbeShorelineMetric(workCache:true); return; }
        if (args.Contains("--quality")) { ProbeShorelineMetric(true); return; }
        if (args.Contains("--shoreline")) { ProbeShorelineMetric(); return; }
        if (args.Contains("--progressive")) { ProbeProgressiveLod(); return; }
        if (args.Contains("--drawn")) { ProbeDrawnDiagnostics(); return; }
        if (args.Contains("--proxy")) { ProbeTerrainProxy(); return; }
        if (args.Contains("--onset")) { ProbeRefinementOnset(); return; }
        if (args.Contains("--replay")) { ProbeRegressionReplay(); return; }
        if (args.Contains("--surface")) { ProbeSurface(); return; }
        Console.WriteLine($"PROBE .NET {Environment.Version}; ideal sphere camera, aspect={Aspect:F6}; scene seed={Seed}; sea={Sea:F9}");
        Cut(300);
        foreach (double height in new[] { 688.0, 1080.0, 2160.0 })
        foreach (double d in new[] { 300.0, 160, 145, 130, 110, 103, 101.1, 100.1 })
        {
            var sw = Stopwatch.StartNew(); var cut = Cut(d, height); sw.Stop();
            var points = ScreenPoints(d);
            Console.WriteLine($"CUT H={height} d={d:F3} count={cut.Count} maxL={(cut.Count == 0 ? 8 : cut.Max(t => t.Level))} sphereScreenBase={points.Count(p => Cover(p, cut) == 8)}/{points.Count} dotnetCutMs={sw.Elapsed.TotalMilliseconds:F1}");
        }
        var full = Cut(100.6);
        foreach (int budget in new[] { 4000, 25000, 50000, 200000 })
        {
            var limited = Cut(100.6, budget: budget);
            var points = ScreenPoints(100.6);
            int dropped = points.Count(p => Cover(p, full) > 8 && Cover(p, limited) == 8);
            Console.WriteLine($"BUDGET {budget} count={limited.Count} droppedToBase={dropped}/{points.Count} nadirL={Cover(Axis, limited)}");
            var coverage = LodCoverage.Complete(limited, 8);
            Console.WriteLine($"COVERAGE budget={budget} selected={limited.Count} complete={coverage.Leaves.Count} fallback={coverage.FallbackCount} replacedBase={coverage.Roots.Count} (no ocean filtering)");
        }
        var prev = Cut(101.1);
        var next = Cut(100.9, previous: prev);
        foreach (int level in new[] { 8, 11, 13 })
        {
            var a = DynamicMeshChunking.GroupByChunk(prev, level);
            var b = DynamicMeshChunking.GroupByChunk(next, level);
            var diff = DynamicMeshChunking.DiffChunks(a, b);
            int changedLeaves = diff.ChangedOrNewChunks.Sum(t => b[t].Count);
            Console.WriteLine($"CHUNK L={level} groups={b.Count} maxLeaves={b.Values.Max(t => t.Count)} changedGroups={diff.ChangedOrNewChunks.Count} unchanged={diff.UnchangedChunkCount} changedLeaves={changedLeaves}/{next.Count}");
            var changed = new HashSet<TileId>(diff.ChangedOrNewChunks);
            double maxAlphaDelta = 0;
            int stale = 0;
            foreach (var group in b.Where(g => !changed.Contains(g.Key))) foreach (var tile in group.Value)
            {
                double delta = Math.Abs(Alpha(tile, 101.1) - Alpha(tile, 100.9));
                if (delta > 0.000001) stale++;
                maxAlphaDelta = Math.Max(maxAlphaDelta, delta);
            }
            Console.WriteLine($"MORPH L={level} unchangedChunkLeavesWithChangedAlpha={stale} maxAlphaDelta={maxAlphaDelta:F6}");
        }
        int land = 0, hidden = 0, oceanParent = 0, mixed = 0, lostLand = 0, gainedLand = 0;
        var deficits = new List<double>();
        string example = "";
        for (int face = 0; face < 6; face++) for (uint u = 0; u < 256; u += 8) for (uint v = 0; v < 256; v += 8)
        {
            TileId tile = TileId.FromFaceLevelUV(face, 8, u, v);
            TileGeometry.GetContinuousBounds(tile, out double u0, out double u1, out double v0, out double v1);
            V center = Point(face, (u0 + u1) / 2, (v0 + v1) / 2);
            V[] dirs = { Point(face,u0,v0), Point(face,u1,v0), Point(face,u1,v1), Point(face,u0,v1) };
            double ec = Elevation(center);
            DomainWarp.WarpPosition(Seed, center.X, center.Y, center.Z, out double wx, out double wy, out double wz);
            int plate = PlateGeneration.AssignPlate(wx, wy, wz, Seeds);
            double amplitude = CrustElevation.SecondaryNoiseAmplitudeMeters * (CrustElevation.IsOceanic(Seed, plate) ? CrustElevation.OceanicNoiseFactor : 1);
            double withoutSecondary = ec - CrustElevation.SecondaryDetailNoise(Seed, center.X, center.Y, center.Z) * amplitude;
            if (ec >= Sea && withoutSecondary < Sea) lostLand++;
            if (ec < Sea && withoutSecondary >= Sea) gainedLand++;
            if (ec < Sea) { oceanParent++; if (dirs.Any(p => Elevation(p) >= Sea)) mixed++; continue; }
            land++;
            V[] corners = dirs.Select(Displace).ToArray();
            double rb = RayTriangle(center, corners[0], corners[1], corners[2]);
            if (double.IsNaN(rb)) rb = RayTriangle(center, corners[0], corners[2], corners[3]);
            double rf = Displace(center).Length + 0.002;
            if (rb > rf)
            {
                hidden++; deficits.Add(rb - rf);
                if (example == "" && rb - rf > 0.02) example = $"face={face} L8 u={u} v={v} baseRadius={rb:F9} fineRadiusWithBias={rf:F9} delta={rb-rf:F9}";
            }
        }
        deficits.Sort();
        Console.WriteLine($"OCCLUSION sampledBase=6144 landCenters={land} fineCenterBelowBase={hidden} fraction={(double)hidden/land:P2} excessP50={deficits[deficits.Count/2]:F9} excessMax={deficits.Last():F9}");
        Console.WriteLine($"OCCLUSION EXAMPLE {example}");
        Console.WriteLine($"COAST oceanBaseCenters={oceanParent} withLandCorner={mixed}");
        Console.WriteLine($"MODEL_MISMATCH CPU secondary omitted only (NOT GPU execution): lostLand={lostLand}/{land} gainedLand={gainedLand}/{oceanParent}");
    }

    private static void ProbeShorelineMetric(bool quality = false, bool workCache = false, bool movingCache = false, bool closure = false, bool tuned = false, bool reuseStorage = false)
    {
        const int side=257;
        var radii=new double[6*side*side];
        System.Threading.Tasks.Parallel.For(0,radii.Length,i=>
        {
            int face=i/(side*side),local=i%(side*side);
            radii[i]=Displace(Point(face,(local/side)/128.0-1,(local%side)/128.0-1)).Length;
        });
        double seaRadius=R+Sea*.001;
        var oldProxy=new TerrainLodProxy(8,radii.Select(r=>Math.Max(seaRadius,r)).ToArray(),seaRadius);
        // A minimálisnál alacsonyabb vízszint kikapcsolja a clampet a
        // kísérleti proxyban. Ez NEM az éles viewer beállítása.
        var newProxy=new TerrainLodProxy(8,radii,radii.Min()*.999);
        var excluded=new bool[6*256*256];
        System.Threading.Tasks.Parallel.For(0,excluded.Length,i=>
        {
            int face=i/(256*256),local=i%(256*256),u=local/256,v=local%256;
            int c=face*side*side+u*side+v;
            excluded[i]=radii[c]<seaRadius-.0001 && radii[c+1]<seaRadius-.0001
                && radii[c+side]<seaRadius-.0001 && radii[c+side+1]<seaRadius-.0001
                && Elevation(Point(face,(u+.5)/128.0-1,(v+.5)/128.0-1))<Sea;
        });
        bool Skip(TileId tile) { tile.GetUV(out uint u,out uint v);return excluded[tile.Face*256*256+(int)u*256+(int)v]; }
        if (workCache) { ProbeWorkCache(oldProxy, Skip); return; }
        if (movingCache) { ProbeMovingCache(oldProxy, Skip,reuseStorage); return; }
        if (closure) { ProbeClosure(oldProxy,newProxy,Skip,tuned); return; }
        if (quality) { ProbeQuality(oldProxy, Skip); return; }
        Console.WriteLine($"ND78 PREP sea={Sea:R} rawBytes={newProxy.StorageBytes} oldBytes={oldProxy.StorageBytes} newCoreSamplesDuringCut=0");
        double threshold=AdaptiveViewState.AngularRadiusForPixelDiameter(12,Fov,688);
        double baseScale=AdaptiveViewState.AngularRadiusForPixelDiameter(10,Fov,688)/threshold;
        var referenceTerrain=new Dictionary<string,HashSet<TileId>>();
        foreach (V axis in new[]{new V(0,-150.575256,54.804909).Unit,new V(30.437628,-161.051575,57.136280).Unit})
        foreach (int mode in new[]{0,1,2})
        {
            var proxy=mode==1?newProxy:oldProxy;
            HashSet<TileId> previous=null;
            int step=0;
            foreach (double distance in new[]{300,173.575959,160.238840,122.160638,109.957422,173.575959,300})
            {
                V camera=axis*distance, forward=axis*-1;
                V right=V.Cross(forward,new V(0,0,1)).Unit,up=V.Cross(right,forward).Unit;
                SurfacePoint P(V v)=>new SurfacePoint(v.X,v.Y,v.Z);
                var view=new ProjectedLodView(P(camera),P(right),P(up),P(forward),Fov,1238.0/688,.01,10000);
                var work=new LodSelectionWork(view,skipStaticBase:mode!=0?Skip:null);
                var timer=Stopwatch.StartNew();
                previous=AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,previous,8,20,threshold,threshold/1.5,
                    forward.X,forward.Y,forward.Z,1.132737064,200000,traversalRootLevel:3,staticBaseLevel:8,
                    baseSplitScale:baseScale,terrainProxy:proxy,work:work);
                timer.Stop();
                TileId Root(TileId t) { while(t.Level>8)t=t.Parent();return t; }
                var terrain=previous.Where(t=>!Skip(Root(t))).ToHashSet();
                string key=axis.X.ToString("R")+"/"+step++;
                if(mode==0)referenceTerrain[key]=terrain;
                bool same=referenceTerrain[key].SetEquals(terrain);
                Console.WriteLine($"ND78 CUT axisX={axis.X:F4} mode={mode} d={distance:F6} leaves={previous.Count} renderedTerrain={terrain.Count} sameRenderedSet={same} nadir={Cover(axis,terrain)} newSplits={work.NewSplits} skippedBase={work.SkippedStaticBases} ms={timer.Elapsed.TotalMilliseconds:F2}");
            }
        }
        V direction=new V(30.437628,-161.051575,57.136280).Unit;
        foreach(double distance in new[]{122.160638,112.162027})
        foreach(bool early in new[]{false,true})
        {
            V camera=direction*distance,forward=direction*-1;
            V right=V.Cross(forward,new V(0,0,1)).Unit,up=V.Cross(right,forward).Unit;
            SurfacePoint P(V v)=>new SurfacePoint(v.X,v.Y,v.Z);
            var view=new ProjectedLodView(P(camera),P(right),P(up),P(forward),Fov,1238.0/688,.01,10000);
            HashSet<TileId> previous=null;
            int waves=0,totalSplits=0;double totalMs=0;
            while(waves<100)
            {
                var work=new LodSelectionWork(view,1024,skipStaticBase:early?Skip:null);
                var timer=Stopwatch.StartNew();
                previous=AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,previous,8,20,threshold,threshold/1.5,
                    forward.X,forward.Y,forward.Z,1.132737064,200000,traversalRootLevel:3,staticBaseLevel:8,
                    baseSplitScale:baseScale,terrainProxy:oldProxy,work:work);
                timer.Stop();waves++;totalSplits+=work.NewSplits;totalMs+=timer.Elapsed.TotalMilliseconds;
                if(work.DeferredSplits==0)break;
            }
            Console.WriteLine($"ND78 WAVES d={distance:F6} early={early} waves={waves} newSplits={totalSplits} totalCutMs={totalMs:F2}");
        }
    }

    private static void ProbeWorkCache(TerrainLodProxy proxy, Func<TileId,bool> skip)
    {
        double threshold=AdaptiveViewState.AngularRadiusForPixelDiameter(12,Fov,688);
        double baseScale=AdaptiveViewState.AngularRadiusForPixelDiameter(10,Fov,688)/threshold;
        V axis=new V(52.815,-79.658,88.120).Unit;
        foreach (double distance in new[]{130.0,122.161,108.152})
        {
            V camera=axis*distance,forward=axis*-1;
            V right=V.Cross(forward,new V(0,0,1)).Unit,up=V.Cross(right,forward).Unit;
            SurfacePoint P(V v)=>new SurfacePoint(v.X,v.Y,v.Z);
            var view=new ProjectedLodView(P(camera),P(right),P(up),P(forward),Fov,1238.0/688,.01,10000);
            var cache=new LodTerrainEvaluationCache(view,proxy);
            HashSet<TileId> previous=null;
            bool settled=false;
            double referenceMs=0,cachedMs=0;
            int hits=0,computed=0;
            for(int wave=0;wave<100;wave++)
            {
                var reference=new LodSelectionWork(view,1024,captureTrace:true,skipStaticBase:skip);
                var cached=new LodSelectionWork(view,1024,captureTrace:true,skipStaticBase:skip,evaluationCache:cache);
                HashSet<TileId> Cut(LodSelectionWork work)=>AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,previous,
                    8,20,threshold,threshold/1.5,forward.X,forward.Y,forward.Z,1.132737064,200000,
                    traversalRootLevel:3,staticBaseLevel:8,baseSplitScale:baseScale,terrainProxy:proxy,work:work);
                // Váltakozó sorrend a páros mérésben; a modell-előkészítés nincs benne.
                HashSet<TileId> expected,actual;
                if(wave%2==0) {expected=Cut(reference);actual=Cut(cached);}
                else {actual=Cut(cached);expected=Cut(reference);}
                if(!expected.SetEquals(actual) || reference.NewSplits!=cached.NewSplits
                    || reference.DeferredSplits!=cached.DeferredSplits || reference.Trace.Count!=cached.Trace.Count)
                    throw new InvalidOperationException("ND81 cut/trace eltérés");
                foreach(TileId tile in actual)
                {
                    bool found=reference.Trace.TryFindStop(tile,out var ancestor,out var stop);
                    if(found!=cached.Trace.TryFindStop(tile,out var ca,out var cs) || ancestor!=ca
                        || stop.Reason!=cs.Reason || !stop.Error.Equals(cs.Error) || !stop.Threshold.Equals(cs.Threshold))
                        throw new InvalidOperationException("ND81 trace eltérés");
                }
                referenceMs+=reference.SelectionMs+reference.BalanceMs;
                cachedMs+=cached.SelectionMs+cached.BalanceMs;
                hits+=cached.MetricCacheHits;computed+=cached.MetricEvaluations;
                Console.WriteLine($"ND81 WAVE d={distance:F3} wave={wave} leaves={actual.Count} splits={cached.NewSplits} deferred={cached.DeferredSplits} equal=True referenceSelection={reference.SelectionMs:F2} referenceBalance={reference.BalanceMs:F2} cachedSelection={cached.SelectionMs:F2} cachedBalance={cached.BalanceMs:F2} hits={cached.MetricCacheHits} computed={cached.MetricEvaluations} entries={cache.Count}");
                previous=actual;
                if(settled) break;
                settled=cached.DeferredSplits==0;
            }
            Console.WriteLine($"ND81 TOTAL d={distance:F3} referenceCutMs={referenceMs:F2} cachedCutMs={cachedMs:F2} hits={hits} computed={computed} settled={settled}");
        }
    }

    private static void ProbeMovingCache(TerrainLodProxy proxy, Func<TileId,bool> skip,bool reuseStorage=false)
    {
        V axis=new V(52.815,-79.658,88.120).Unit, forward=axis*-1;
        V right=V.Cross(forward,new V(0,0,1)).Unit, up=V.Cross(right,forward).Unit;
        SurfacePoint P(V v)=>new(v.X,v.Y,v.Z);
        foreach(double pixels in new[]{12.0,8.0})
        {
            var coldTimes=new List<double>(); var reusedTimes=new List<double>();
            LodTerrainEvaluationCache cache=null;
            LodTerrainEvaluationCache referenceCache=null;
            long referenceBytes=0,reusedBytes=0;
            HashSet<TileId> previous=null;
            int maxLeaves=0;
            double threshold=AdaptiveViewState.AngularRadiusForPixelDiameter(pixels,Fov,688);
            double baseScale=AdaptiveViewState.EarlierBaseSplitScale(threshold,threshold/1.5,
                AdaptiveViewState.AngularRadiusForPixelDiameter(pixels==8?7:10,Fov,688));
            for(int step=0;step<26;step++)
            {
                double distance=step<13?200-step*7:116+(step-13)*7;
                V camera=axis*distance;
                var view=new ProjectedLodView(P(camera),P(right),P(up),P(forward),Fov,1238.0/688,.01,10000);
                referenceCache=referenceCache==null?new(view,proxy):
                    referenceCache.Matches(view,proxy)?referenceCache:referenceCache.Reproject(view);
                if(cache==null) cache=new(view,proxy);
                else if(reuseStorage) cache.ResetView(view);
                else cache=cache.Reproject(view);
                // ND-81 referencia: nézetenkénti metrika-cache, nézetek közti geometriatárolás nélkül.
                var cold=new LodSelectionWork(view,1024,skipStaticBase:skip,
                    evaluationCache:reuseStorage?referenceCache:new(view,proxy,geometry:new LodGeometryCache(proxy,0)));
                var warm=new LodSelectionWork(view,1024,skipStaticBase:skip,evaluationCache:cache);
                HashSet<TileId> Cut(LodSelectionWork work)=>AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,previous,
                    8,20,threshold,threshold/1.5,forward.X,forward.Y,forward.Z,1.13,200000,
                    traversalRootLevel:3,staticBaseLevel:8,baseSplitScale:baseScale,terrainProxy:proxy,work:work);
                HashSet<TileId> a,b;
                HashSet<TileId> Measured(LodSelectionWork work,ref long bytes)
                { long start=GC.GetAllocatedBytesForCurrentThread(); var result=Cut(work); bytes+=GC.GetAllocatedBytesForCurrentThread()-start; return result; }
                if(step%2==0){a=Measured(cold,ref referenceBytes);b=Measured(warm,ref reusedBytes);}
                else{b=Measured(warm,ref reusedBytes);a=Measured(cold,ref referenceBytes);}
                if(!a.SetEquals(b) || cold.NewSplits!=warm.NewSplits || cold.DeferredSplits!=warm.DeferredSplits)
                    throw new InvalidOperationException("ND96 moving cut mismatch");
                coldTimes.Add(cold.SelectionMs+cold.BalanceMs);reusedTimes.Add(warm.SelectionMs+warm.BalanceMs);
                maxLeaves=Math.Max(maxLeaves,b.Count);previous=b;
            }
            coldTimes.Sort();reusedTimes.Sort();
            Console.WriteLine($"ND{(reuseStorage?97:96)} MOVING pixels={pixels} pairs=26 equal=True maxLeaves={maxLeaves} referenceBytes={referenceBytes} reusedBytes={reusedBytes} " +
                $"coldTotalMs={coldTimes.Sum():F2} reusedTotalMs={reusedTimes.Sum():F2} coldP50={coldTimes[12]:F2} reusedP50={reusedTimes[12]:F2} " +
                $"coldP90={coldTimes[22]:F2} reusedP90={reusedTimes[22]:F2} geometryHits={cache.Geometry.Hits} geometryComputed={cache.Geometry.Computed} geometryEntries={cache.Geometry.Count}");
        }
    }

    private static void ProbeClosure(TerrainLodProxy clamped, TerrainLodProxy raw, Func<TileId,bool> skip, bool tuned)
    {
        V axis=new V(52.815,-79.658,88.120).Unit, forward=axis*-1;
        V right=V.Cross(forward,new V(0,0,1)).Unit, up=V.Cross(right,forward).Unit;
        SurfacePoint P(V v)=>new(v.X,v.Y,v.Z);
        foreach(bool actualProxy in tuned?new[]{false}:new[]{false,true})
        foreach(double pixels in new[]{12.0,8.0})
        {
            TerrainLodProxy proxy=actualProxy?raw:clamped;
            LodTerrainEvaluationCache cache=null;
            HashSet<TileId> previous=null;
            double split=AdaptiveViewState.AngularRadiusForPixelDiameter(pixels,Fov,688);
            double scale=AdaptiveViewState.EarlierBaseSplitScale(split,split/1.5,
                AdaptiveViewState.AngularRadiusForPixelDiameter(pixels==8?(tuned?7:6):10,Fov,688));
            foreach(double distance in new[]{300.0,210,160,127,105,160,300})
            {
                V camera=axis*distance;
                var view=new ProjectedLodView(P(camera),P(right),P(up),P(forward),Fov,1238.0/688,.01,10000);
                cache=cache==null?new(view,proxy):cache.Reproject(view);
                int waves=0,deferred;double ms=0;
                do
                {
                    var work=new LodSelectionWork(view,1024,skipStaticBase:skip,evaluationCache:cache);
                    previous=AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,previous,
                        8,20,split,split/1.5,forward.X,forward.Y,forward.Z,1.13,200000,
                        traversalRootLevel:3,staticBaseLevel:8,baseSplitScale:scale,terrainProxy:proxy,work:work);
                    ms+=work.SelectionMs+work.BalanceMs; waves++;deferred=work.DeferredSplits;
                }while(deferred>0 && waves<256);
                if(deferred>0 || previous.Count>200000) throw new InvalidOperationException("ND96 closure did not converge within budget");
                Console.WriteLine($"ND96 CLOSURE tuned={tuned} raw={actualProxy} pixels={pixels} distance={distance} leaves={previous.Count} waves={waves} cutMs={ms:F2}");
            }
        }
    }

    private static void ProbeQuality(TerrainLodProxy proxy, Func<TileId,bool> skip)
    {
        foreach (V axis in new[]{new V(82.747,-129.398,45.666).Unit,new V(72.764,-132.848,52.281).Unit})
        foreach (double pixels in new[]{12.0,6.0,8.0,9.0})
        {
            double split=AdaptiveViewState.AngularRadiusForPixelDiameter(pixels,Fov,688);
            double scale=AdaptiveViewState.EarlierBaseSplitScale(split,split/1.5,
                AdaptiveViewState.AngularRadiusForPixelDiameter(pixels*5/6,Fov,688));
            HashSet<TileId> previous=null;
            Dictionary<TileId,HashSet<TileId>> previousPacked=null;
            foreach (double distance in new[]{300,263.746,234.064,209.762,189.866,173.576,160.239,127.067,109.957,105.465,103.663,160.239,300})
            {
                V camera=axis*distance,forward=axis*-1;
                V right=V.Cross(forward,new V(0,0,1)).Unit,up=V.Cross(right,forward).Unit;
                SurfacePoint P(V v)=>new SurfacePoint(v.X,v.Y,v.Z);
                var view=new ProjectedLodView(P(camera),P(right),P(up),P(forward),Fov,1238.0/688,.01,10000);
                var work=new LodSelectionWork(view,skipStaticBase:skip);
                var timer=Stopwatch.StartNew();
                previous=AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,previous,8,20,split,split/1.5,
                    forward.X,forward.Y,forward.Z,1.132737064,200000,traversalRootLevel:3,staticBaseLevel:8,
                    baseSplitScale:scale,terrainProxy:proxy,work:work);
                timer.Stop();
                if (previous.Count>200000) throw new InvalidOperationException("A kiválasztási budget sérült.");
                var groups8=DynamicMeshChunking.GroupByChunk(previous,8);
                var groups6=DynamicMeshChunking.GroupByChunk(previous,6);
                Console.WriteLine($"ND79 QUALITY axisX={axis.X:F4} pixels={pixels} d={distance:F3} leaves={previous.Count} nadir={Cover(axis,previous)} newSplits={work.NewSplits} cutMs={timer.Elapsed.TotalMilliseconds:F2} groups8={groups8.Count} groups6={groups6.Count} maxLeaves6={(groups6.Count==0?0:groups6.Values.Max(g=>g.Count))}");
                var rendered=LodCoverage.Complete(previous,8).Leaves;
                var packingTimer=Stopwatch.StartNew();
                var packed=DynamicMeshChunking.GroupByLeafBudget(rendered,6,previousChunks:previousPacked);
                packingTimer.Stop();
                var packedLeaves=packed.Values.SelectMany(g=>g).ToArray();
                if(packedLeaves.Length!=rendered.Count || !rendered.SetEquals(packedLeaves)
                    || packed.Values.Any(g=>g.Count>256)) throw new InvalidOperationException("Hibás chunk-fedés vagy méretkorlát.");
                int actualFixedGroups=DynamicMeshChunking.GroupByChunk(rendered,8).Count;
                Console.WriteLine($"ND80 PACK axisX={axis.X:F4} pixels={pixels} d={distance:F3} rendered={rendered.Count} oldGroups={actualFixedGroups} packedGroups={packed.Count} maxLeaves={(packed.Count==0?0:packed.Values.Max(g=>g.Count))} packingMs={packingTimer.Elapsed.TotalMilliseconds:F2} sameTiles=True");
                previousPacked=packed;
            }
        }
    }

    private static void ProbeDrawnDiagnostics()
    {
        for(int run=0;run<3;run++)
        {
            var m=new RenderedTileDiagnostics(1238,688);
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            var timer=Stopwatch.StartNew();
            const int side=640;
            for(int y=0;y<side;y++) for(int x=0;x<side;x++)
            {
                double x0=2.0*x/side-1,x1=2.0*(x+1)/side-1,y0=2.0*y/side-1,y1=2.0*(y+1)/side-1;
                m.AddQuad(new RenderedTileDiagnostics.ClipPoint(x0,y0,0,1),new RenderedTileDiagnostics.ClipPoint(x1,y0,0,1),
                    new RenderedTileDiagnostics.ClipPoint(x1,y1,0,1),new RenderedTileDiagnostics.ClipPoint(x0,y1,0,1),0,2,1,0,3,2,1,y*side+x);
            }
            timer.Stop();
            long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Console.WriteLine($"DRAWN_PROBE run={run} quads={m.TestedQuads} hits={m.Hits.Count(h=>h.Found)} timeMs={timer.Elapsed.TotalMilliseconds:F2} allocatedInScan={bytes}");
        }
    }

    private static void ProbeProgressiveLod()
    {
        const int side=257;
        var radii=new double[6*side*side];
        System.Threading.Tasks.Parallel.For(0,radii.Length,i=>
        {
            int face=i/(side*side), local=i%(side*side);
            radii[i]=Displace(Point(face,(local/side)/128.0-1,(local%side)/128.0-1)).Length;
        });
        var proxy=new TerrainLodProxy(8,radii,R+Sea*.001);
        double threshold=AdaptiveViewState.AngularRadiusForPixelDiameter(12,Fov,688);
        double baseScale=AdaptiveViewState.EarlierBaseSplitScale(threshold,threshold/1.5,
            AdaptiveViewState.AngularRadiusForPixelDiameter(10,Fov,688));
        int axisIndex=0;
        foreach(V axis in new[]{new V(78.526,-51.823,47.635).Unit,new V(-109.171,-33.389,80.651).Unit})
        {
            axisIndex++;
            V right=V.Cross(axis,new V(0,0,1)).Unit, up=V.Cross(right,axis).Unit;
            SurfacePoint P(V p)=>new SurfacePoint(p.X,p.Y,p.Z);
            HashSet<TileId> previous=null;
            foreach(double d in new[]{300,159.340,139.777,126.663,114.833,105.457,139.777,300})
            {
                V camera=axis*d;
                var view=new ProjectedLodView(P(camera),P(right),P(up),P(axis*-1),Fov,1238.0/688,.01,10000);
                HashSet<TileId> Select(HashSet<TileId> before,LodSelectionWork work)=>AdaptiveQuadTree.BuildCut(
                    camera.X,camera.Y,camera.Z,R,before,8,20,threshold,threshold/1.5,-axis.X,-axis.Y,-axis.Z,
                    1.132737064,200000,traversalRootLevel:3,staticBaseLevel:8,baseSplitScale:baseScale,terrainProxy:proxy,work:work);
                var timer=Stopwatch.StartNew(); var old=Select(previous,null); timer.Stop();
                Console.WriteLine($"ND76 OLD axis={axisIndex} d={d:F3} cut={old.Count} nadir={Cover(axis,old)} ms={timer.Elapsed.TotalMilliseconds:F1}");
                timer.Restart(); var full=Select(previous,new LodSelectionWork(view)); timer.Stop();
                Console.WriteLine($"ND76 FULL axis={axisIndex} d={d:F3} cut={full.Count} nadir={Cover(axis,full)} ms={timer.Elapsed.TotalMilliseconds:F1}");
                int waves=0; double maxMs=0,totalMs=0;
                do
                {
                    var work=new LodSelectionWork(view,1024);
                    timer.Restart(); previous=Select(previous,work); timer.Stop(); waves++;
                    maxMs=Math.Max(maxMs,timer.Elapsed.TotalMilliseconds); totalMs+=timer.Elapsed.TotalMilliseconds;
                    if(waves==1 || work.DeferredSplits==0)
                        Console.WriteLine($"ND76 WAVE axis={axisIndex} d={d:F3} wave={waves} cut={previous.Count} nadir={Cover(axis,previous)} new={work.NewSplits} deferred={work.DeferredSplits} ms={timer.Elapsed.TotalMilliseconds:F1}");
                    if(work.DeferredSplits==0) break;
                } while(waves<400);
                Console.WriteLine($"ND76 SETTLED axis={axisIndex} d={d:F3} waves={waves} maxCutMs={maxMs:F1} totalCutMs={totalMs:F1}");
            }
        }
    }

    private static void ProbeTerrainProxy()
    {
        // A viewerben ezek már kész renderadatok; itt a Core-mintázás
        // külön, nem a proxy/cut időmérésébe rejtett előkészítés.
        const int side = 257;
        var radii = new double[6 * side * side];
        var sampling = Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.For(0, radii.Length, i =>
        {
            int face = i / (side * side), local = i % (side * side);
            radii[i] = Displace(Point(face, (local / side) / 128.0 - 1, (local % side) / 128.0 - 1)).Length;
        });
        sampling.Stop();
        var build = Stopwatch.StartNew();
        var proxy = new TerrainLodProxy(8, radii, R + Sea * .001);
        build.Stop();
        Console.WriteLine($"PROXY sourceSamplingMs={sampling.Elapsed.TotalMilliseconds:F1} buildMs={build.Elapsed.TotalMilliseconds:F1} storageBytes={proxy.StorageBytes} newCoreSamplesDuringCut=0");
        double threshold = AdaptiveViewState.AngularRadiusForPixelDiameter(12, 1.047197543, 688);
        double baseScale = AdaptiveViewState.EarlierBaseSplitScale(threshold, threshold / 1.5,
            AdaptiveViewState.AngularRadiusForPixelDiameter(10, 1.047197543, 688));
        int axisNumber = 0;
        foreach (V axis in new[] { new V(31.581,-127.684,49.049).Unit, new V(-81.192,-47.702,50.118).Unit })
        {
            axisNumber++;
            double actualRadius = Math.Max(R + Sea * .001, Displace(axis).Length);
            double estimate = proxy.RadiusAt(TileGeometry.FromPosition(axis.X,axis.Y,axis.Z,20));
            Console.WriteLine($"PROXY_AXIS {axisNumber} actualRadius={actualRadius:F6} proxyRadius={estimate:F6} errorUnits={estimate-actualRadius:F6}");
            double[] distances = { 173.576,160.239,149.319,140.379,133.060,122.161,114.855,108.152,106.675,104.474,103.663,actualRadius+.5,actualRadius+.1 };
            foreach (bool enabled in new[] { false, true })
            {
                HashSet<TileId> previous = null;
                foreach (double d in distances.Concat(distances.AsEnumerable().Reverse().Skip(1)))
                {
                    V camera = axis * d;
                    var sw = Stopwatch.StartNew();
                    var cut = AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,previous,8,20,
                        threshold,threshold/1.5,-axis.X,-axis.Y,-axis.Z,1.132737064,200000,
                        traversalRootLevel:3,staticBaseLevel:8,baseSplitScale:baseScale,terrainProxy:enabled?proxy:null);
                    sw.Stop();
                    int level = Cover(axis,cut);
                    double alpha = 1;
                    if (level > 8)
                    {
                        TileId parent=TileGeometry.FromPosition(axis.X,axis.Y,axis.Z,level).Parent();
                        AdaptiveQuadTree.GetCenterAndBoundingRadius(parent,R,out double x,out double y,out double z,out double footprint);
                        if (enabled) proxy.ScaleMetric(parent,R,ref x,ref y,ref z,ref footprint);
                        double distance = new V(camera.X-x,camera.Y-y,camera.Z-z).Length;
                        alpha = AdaptiveViewState.GeomorphAlpha(distance,footprint/Math.Tan(threshold*(parent.Level==8?baseScale:1)),.35);
                    }
                    Console.WriteLine($"PROXY_CUT axis={axisNumber} enabled={enabled} d={d:F6} count={cut.Count} nadirL={level} alpha={alpha:F3} ms={sw.Elapsed.TotalMilliseconds:F1}");
                    previous = cut;
                }
            }
        }
    }

    private static void ProbeRefinementOnset()
    {
        // A 18:22:58-as log szárazföldi zoomiránya és vetítése.
        // A kontrollált távolságsor azonos mindkét profilhoz, explicit előzménnyel.
        V axis = new V(31.581,-127.684,49.049).Unit;
        double[] distances = { 173.576,160.239,149.319,140.379,133.060,122.161,114.855,108.152,104.474,103.663 };
        foreach (string profile in new[] { "original", "global10", "base10" })
        {
            double pixels=profile=="global10" ? 10 : 12;
            HashSet<TileId> previous = null;
            double threshold = AdaptiveViewState.AngularRadiusForPixelDiameter(pixels,1.047197543,688);
            double baseScale=profile=="base10" ? AdaptiveViewState.EarlierBaseSplitScale(threshold,threshold/1.5,
                AdaptiveViewState.AngularRadiusForPixelDiameter(10,1.047197543,688)) : 1;
            foreach (double d in distances.Concat(distances.AsEnumerable().Reverse().Skip(1)))
            {
                V camera=axis*d;
                var sw=Stopwatch.StartNew();
                var cut=AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,previous,8,20,
                    threshold,threshold/1.5,-axis.X,-axis.Y,-axis.Z,1.132737064,200000,
                    traversalRootLevel:3,staticBaseLevel:8,baseSplitScale:baseScale);
                sw.Stop();
                int level=Cover(axis,cut);
                double alpha=1;
                if(level>8)
                {
                    var parent=TileGeometry.FromPosition(axis.X,axis.Y,axis.Z,level).Parent();
                    AdaptiveQuadTree.GetCenterAndBoundingRadius(parent,R,out double x,out double y,out double z,out double r);
                    double split=r/Math.Tan(parent.Level==8 ? threshold*baseScale : threshold);
                    alpha=AdaptiveViewState.GeomorphAlpha((camera-new V(x,y,z)).Length,split,profile=="original" ? .6 : .35);
                }
                Console.WriteLine($"ONSET profile={profile} pixels={pixels} d={d:F3} count={cut.Count} nadirL={level} alpha={alpha:F3} cutMs={sw.Elapsed.TotalMilliseconds:F1}");
                previous=cut;
            }
        }
    }

    private static void ProbeRegressionReplay()
    {
        // Az élő logból vett kamerák, közös explicit vetítési paraméterekkel.
        // Az eredeti log nem tárolta a halfFov-t: az 1.1 mindkét változat
        // közös próbabemenete, nem rekonstruált élő FOV. Hideg cut-előzmény.
        var cameras = new[] { new V(27.300,-107.027,52.183), new V(36.598,-95.515,59.121),
            new V(30.380,-86.083,55.193) };
        const double target = 0.010070;
        foreach (V camera in cameras)
        {
            V axis = camera.Unit;
            var points = new Dictionary<(int, int, uint, uint), SurfacePoint>();
            int evaluations = 0;
            SurfacePoint Sample(int f, int l, uint u, uint v)
            {
                var key = (f,l,u,v);
                if (points.TryGetValue(key, out var p)) return p;
                double n = 1 << l;
                V d = Displace(Point(f, 2*u/n-1, 2*v/n-1));
                p = new SurfacePoint((float)d.X, (float)d.Y, (float)d.Z);
                points.Add(key,p); evaluations++; return p;
            }
            // Csak az adott futásban kért statikus pontokat melegítjük elő,
            // majd a mért ND-71 futás pontosan ezeket újrahasználja. A viewer
            // a teljes statikus rácsot már Buildkor elkészítette.
            bool warmStatic = true;
            SurfaceLodBounds Bounds(TileId id)
            {
                id.GetUV(out uint u, out uint v); int f=id.Face, l=id.Level;
                SurfacePoint P(int level,uint cu,uint cv)
                {
                    if (level <= 8) return Sample(f,8,cu << (8-level),cv << (8-level));
                    if (warmStatic)
                    {
                        double n=1<<level;
                        V d=Displace(Point(f,2*cu/n-1,2*cv/n-1));
                        return new SurfacePoint((float)d.X,(float)d.Y,(float)d.Z);
                    }
                    return Sample(f,level,cu,cv);
                }
                return SurfaceLodBounds.FromSamples(P(l+1,2*u+1,2*v+1),
                    new SurfaceQuad(P(l,u,v),P(l,u+1,v),P(l,u+1,v+1),P(l,u,v+1)));
            }
            HashSet<TileId> Run(bool displaced) => AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,null,
                8,20,target,target/1.5,-axis.X,-axis.Y,-axis.Z,1.1,200000,
                traversalRootLevel:3,staticBaseLevel:8,surfaceBounds:displaced ? Bounds : null);
            Run(true); warmStatic=false; evaluations=0;
            foreach (bool displaced in new[] { true, false })
            {
                int before=evaluations;
                var sw=Stopwatch.StartNew(); var cut=Run(displaced); sw.Stop();
                double terrainRadius=Displace(axis).Length;
                Console.WriteLine($"REPLAY d={camera.Length:F3} mode={(displaced ? "ND71" : "ND72-restored-ND70")} " +
                    $"count={cut.Count} nadirL={Cover(axis,cut)} cutMs={sw.Elapsed.TotalMilliseconds:F1} " +
                    $"modelPoints={evaluations-before} clearance={camera.Length-Math.Max(terrainRadius,R+Sea*.001):F4} " +
                    $"underWater={terrainRadius<R+Sea*.001}");
            }
        }
    }

    private static void ProbeSurface()
    {
        // ND-71 célzott próba: valós terrain-mező, kráter nélküli t=0 világ.
        // Nem Unity render/FPS-mérés; a sarok-cache itt önálló referencia.
        // Determinisztikusan keresünk szárazföldi pontot; a korábbi kamera-
        // irány tengerfenékre is nézhet, ami nem látható felszíni próba lenne.
        V axis = default;
        bool found = false;
        double seaRadius = R + Sea * 0.001;
        for (int face = 0; face < 6 && !found; face++)
        for (int u = 0; u < 32 && !found; u++)
        for (int v = 0; v < 32 && !found; v++)
        {
            double cu = (u + 0.5) / 16 - 1, cv = (v + 0.5) / 16 - 1;
            V p = Point(face, cu, cv);
            double r = Displace(p).Length;
            if (r > seaRadius + 0.5 && r < seaRadius + 2
                && Math.Abs(Displace(Point(face, cu + 0.001, cv)).Length - r) < 0.05
                && Math.Abs(Displace(Point(face, cu, cv + 0.001)).Length - r) < 0.05)
            { axis = p; found = true; }
        }
        if (!found) throw new InvalidOperationException("Nem találtunk szárazföldi próbahelyet.");
        double surfaceRadius = Displace(axis).Length;
        Console.WriteLine($"SURFACE seed={Seed} sea={Sea:F9} axis=({axis.X:F9},{axis.Y:F9},{axis.Z:F9}) nadirTerrainR={surfaceRadius:F9}");
        var points = new Dictionary<(int, int, uint, uint), SurfacePoint>();
        SurfacePoint Sample(int face, int level, uint u, uint v)
        {
            var key = (face, level, u, v);
            if (points.TryGetValue(key, out var p)) return p;
            double n = 1 << level;
            V d = Displace(Point(face, 2 * u / n - 1, 2 * v / n - 1));
            p = new SurfacePoint((float)d.X, (float)d.Y, (float)d.Z);
            points.Add(key, p); return p;
        }
        foreach (double clearance in new[] { 0.5, 0.05 })
        {
            var boundsCache = new Dictionary<TileId, SurfaceLodBounds>();
            SurfaceLodBounds Bounds(TileId id)
            {
                if (boundsCache.TryGetValue(id, out var b)) return b;
                id.GetUV(out uint u, out uint v); int f = id.Face, l = id.Level;
                b = SurfaceLodBounds.FromSamples(Sample(f, l + 1, 2 * u + 1, 2 * v + 1),
                    new SurfaceQuad(Sample(f,l,u,v), Sample(f,l,u+1,v), Sample(f,l,u+1,v+1), Sample(f,l,u,v+1)));
                boundsCache.Add(id, b); return b;
            }
            V camera = axis * (surfaceRadius + clearance);
            double target = Math.Atan(6 / (688 / (2 * Math.Tan(Fov / 2))));
            foreach (bool displaced in new[] { false, true })
            {
                var sw = Stopwatch.StartNew();
                var cut = AdaptiveQuadTree.BuildCut(camera.X,camera.Y,camera.Z,R,null,8,20,target,target/1.5,
                    -axis.X,-axis.Y,-axis.Z,1.1,25000,traversalRootLevel:3,staticBaseLevel:8,
                    surfaceBounds:displaced ? Bounds : null);
                sw.Stop();
                Console.WriteLine($"SURFACE clearance={clearance:F3} displaced={displaced} nadirL={Cover(axis,cut)} " +
                    $"maxL={(cut.Count == 0 ? 8 : cut.Max(t=>t.Level))} count={cut.Count} bounds={boundsCache.Count} points={points.Count} ms={sw.Elapsed.TotalMilliseconds:F1}");
            }
        }
    }

    private static double Alpha(TileId tile, double distance)
    {
        AdaptiveQuadTree.GetCenterAndBoundingRadius(tile.Parent(), R, out double x, out double y, out double z, out double r);
        double splitDistance = r / Math.Tan(6 * Fov / 1080);
        return Math.Clamp((splitDistance - (Axis * distance - new V(x,y,z)).Length) / (0.6 * splitDistance), 0, 1);
    }

    private readonly record struct V(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt(Dot(this, this));
        public V Unit => this * (1 / Length);
        public static V operator +(V a, V b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static V operator -(V a, V b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static V operator *(V a, double s) => new(a.X*s,a.Y*s,a.Z*s);
        public static double Dot(V a, V b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        public static V Cross(V a, V b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    }
}
