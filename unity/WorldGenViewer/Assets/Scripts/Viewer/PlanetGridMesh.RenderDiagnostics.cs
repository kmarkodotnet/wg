using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using WorldGen.Core;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        [SerializeField]
        [Tooltip("ND-75: legfeljebb 1 Hz-es háttérmérés a már feltöltött terrain/water mesh-en, " +
                 "17×9 képernyőpontban. Pixelméret, zoom és függő LOD-kérés a PerfLogba. " +
                 "Diagnosztikai költsége külön mérve; nem változtatja a finomítást.")]
        private bool logDrawnTileSizes = true;

        [SerializeField]
        [Tooltip("FELHASZNALOI KERES (2026-09-18): a MAR MEGLEVO, 17x9-es egyenletes " +
                 "kepernyo-raszter MINDEN talalatarol egy-egy GEPILEG OLVASHATO sor a " +
                 "PerfLogba ([tilesample] ...), amiben egyutt van a zoom, a kepernyo- " +
                 "pozicio, az egitest-beli pozicio (lat/lon), a tile szintje es a tile " +
                 "merete pixelben ES kilometerben. Cel: utolagos kiertekeles - adott " +
                 "zoom mellett adott kepernyo-pozicion mekkora a tile. A meglevo " +
                 "[ND-75 row] terkep ember-olvasasra jo, de gepi feldolgozashoz a zoomot " +
                 "kulon fejlecbol kellene hozza illeszteni, ezert kap minden sor sajat " +
                 "zoom-erteket. 153 minta / meres, ~2 masodpercenkent - ha tul sok a " +
                 "naplo, ezt kapcsold ki (a [ND-75 ...] osszefoglalo ettol fuggetlenul megy).")]
        private bool logUniformTileSamples = true;

        /// <summary>
        /// A LEGUTÓBB ALKALMAZOTT vágás munka-könyvelése (felhasználói kérés,
        /// 2026-09-19): "az aktuális összes tile számosság, plusz hány új tile
        /// került kiszámolásra és hány tile kalkulálása lett abbahagyva".
        ///
        /// A három kérdés három blokkja, és a HARMADIKAT szándékosan KÉT
        /// részre vágom, mert összevonva félrevezető lenne (ld.
        /// <see cref="WorldGen.Viewer.Lod.LodSelectionWork"/> és a
        /// CutWorkAccountingTests):
        ///   * ELVONT munka (`BudgetStops` + `DeferredSplits`): a szelekció
        ///     AKARTA, a limit nem engedte - ennyivel rosszabb a kép.
        ///   * SZÜKSÉGTELEN munka (`SufficientStops`, `MaxLevelStops`,
        ///     `InvisibleStops`, `SkippedBases`, `SkippedOceanic`): helyesen
        ///     maradt abba, a kép NEM lett tőle rosszabb.
        /// </summary>
        private readonly struct AppliedCutWork
        {
            public readonly bool Valid;
            /// <summary>Összes kirajzolt tile: a teljes vágás levélszáma.</summary>
            public readonly int TotalLeaves;
            public readonly int DynamicLeaves, WaterLeaves, Budget;
            /// <summary>Újraszámolt: új felosztás és új metrika-kiértékelés.</summary>
            public readonly int NewSplits, ReusedLeaves, MetricComputed, MetricHits;
            /// <summary>Elvont munka.</summary>
            public readonly int BudgetStops, DeferredSplits;
            /// <summary>Szükségtelen, helyesen elhagyott munka.</summary>
            public readonly int SufficientStops, MaxLevelStops, InvisibleStops,
                SkippedBases, SkippedOceanic;

            public AppliedCutWork(int totalLeaves, int dynamicLeaves, int waterLeaves, int budget,
                int newSplits, int reusedLeaves, int metricComputed, int metricHits,
                int budgetStops, int deferredSplits, int sufficientStops, int maxLevelStops,
                int invisibleStops, int skippedBases, int skippedOceanic)
            {
                Valid = true;
                TotalLeaves = totalLeaves; DynamicLeaves = dynamicLeaves;
                WaterLeaves = waterLeaves; Budget = budget;
                NewSplits = newSplits; ReusedLeaves = reusedLeaves;
                MetricComputed = metricComputed; MetricHits = metricHits;
                BudgetStops = budgetStops; DeferredSplits = deferredSplits;
                SufficientStops = sufficientStops; MaxLevelStops = maxLevelStops;
                InvisibleStops = invisibleStops; SkippedBases = skippedBases;
                SkippedOceanic = skippedOceanic;
            }
        }

        /// <summary>A legutóbb alkalmazott vágás könyvelése; a diagnosztika
        /// 2 másodpercenként ezt olvassa ki a főszálon.</summary>
        private AppliedCutWork _appliedCutWork;

        /// <summary>
        /// A <see cref="logUniformTileSamples"/> soraihoz tartozó, a FŐSZÁLON
        /// kiolvasott kontextus (a worker Unity-objektumot nem olvashat).
        /// </summary>
        private readonly struct UniformSampleContext
        {
            public readonly bool Enabled;
            public readonly int Frame;
            public readonly double AltitudeUnits, ZoomRatio, Fov, UnitToKm, DeepTimeMyr;
            public readonly string ViewClass;
            public readonly AppliedCutWork Work;

            public UniformSampleContext(bool enabled, int frame, double altitudeUnits, double zoomRatio,
                double fov, double unitToKm, double deepTimeMyr, string viewClass, in AppliedCutWork work)
            {
                Enabled = enabled; Frame = frame; AltitudeUnits = altitudeUnits; ZoomRatio = zoomRatio;
                Fov = fov; UnitToKm = unitToKm; DeepTimeMyr = deepTimeMyr; ViewClass = viewClass;
                Work = work;
            }
        }

        private sealed class DrawnSurface
        {
            public readonly MeshRenderer Renderer;
            public readonly List<Vector3> Vertices;
            public readonly List<int[]> Indices;
            public readonly int Kind;
            public readonly TileId[]? TileIds;
            public DrawnSurface(MeshRenderer renderer,List<Vector3> vertices,List<int[]> indices,int kind,TileId[]? tileIds)
            { Renderer=renderer; Vertices=vertices; Indices=indices; Kind=kind; TileIds=tileIds; }
        }

        private readonly struct DrawnSurfaceSnapshot
        {
            public readonly List<Vector3> Vertices;
            public readonly List<int[]> Indices;
            public readonly Matrix4x4 ToClip, ToBody;
            public readonly int[] CullModes;
            public readonly int Kind;
            public readonly TileId[]? TileIds;
            public DrawnSurfaceSnapshot(DrawnSurface data, Matrix4x4 toClip, Matrix4x4 toBody, int[] cullModes)
            { Vertices=data.Vertices; Indices=data.Indices; Kind=data.Kind; ToClip=toClip; ToBody=toBody; CullModes=cullModes; TileIds=data.TileIds; }
        }

        private readonly Dictionary<GameObject, DrawnSurface> _drawnDiagnosticMeshes = new Dictionary<GameObject, DrawnSurface>();
        private HashSet<int> _drawnHiddenStaticQuads = new HashSet<int>();
        private Task<string>? _drawnDiagnosticTask;
        private double _nextDrawnDiagnosticTime;
        private long _drawnDiagnosticRevision;
        private float _drawnLodAppliedAt;
        private bool _drawnDiagnosticDisabledAfterError;
        private LodSelectionTrace? _appliedSelectionTrace;
        private LodCoverage? _appliedDiagnosticCoverage;
        private ProjectedLodView? _appliedTraceView;
        private double _appliedTraceThreshold, _appliedTraceBaseThreshold, _appliedTraceMorphRange, _appliedTraceFov;
        private int _appliedTraceBaseLevel, _appliedTracePixelHeight;

        private readonly struct DrawnTraceSnapshot
        {
            public readonly LodSelectionTrace? Trace;
            public readonly LodCoverage? Coverage;
            public readonly TerrainLodProxy? Proxy;
            public readonly ProjectedLodView? RequestView;
            public readonly double PixelScale, Threshold, BaseThreshold, MorphRange;
            public readonly int BaseLevel;
            public DrawnTraceSnapshot(PlanetGridMesh owner)
            {
                Trace=owner._appliedSelectionTrace; Proxy=owner._terrainLodProxy;
                Coverage=Trace!=null ? owner._appliedDiagnosticCoverage : null;
                RequestView=Trace!=null ? owner._appliedTraceView : null;
                PixelScale=owner._appliedTracePixelHeight/Math.Tan(owner._appliedTraceFov*.5);
                Threshold=owner._appliedTraceThreshold; BaseThreshold=owner._appliedTraceBaseThreshold;
                MorphRange=owner._appliedTraceMorphRange; BaseLevel=owner._appliedTraceBaseLevel;
            }
        }

        // Csak sikeres feltöltés után hívható. A listákat az upload után
        // a renderút már nem módosítja; új feltöltés új listareferenciát ad.
        private void RememberDrawnSurface(GameObject target, List<Vector3> vertices, List<int[]> indices, int kind, TileId[]? tileIds=null)
        {
            _drawnDiagnosticMeshes[target] = new DrawnSurface(target.GetComponent<MeshRenderer>(),vertices,indices,kind,tileIds);
            _drawnDiagnosticRevision++;
        }

        private void RememberDrawnPositions(GameObject target, List<Vector3> vertices)
        {
            if (!_drawnDiagnosticMeshes.TryGetValue(target, out DrawnSurface old)) return;
            // Az indexeket a position-only upload NEM írja felül.
            RememberDrawnSurface(target,vertices,old.Indices,old.Kind,old.TileIds);
        }

        private void TickDrawnTileDiagnostics()
        {
            try { TickDrawnTileDiagnosticsCore(); }
            catch (Exception error)
            {
                _drawnDiagnosticDisabledAfterError=true;
                PerfLog("[ND-75 drawn] status=capture-error " + error.Message);
            }
        }

        private void TickDrawnTileDiagnosticsCore()
        {
            if (_drawnDiagnosticTask != null)
            {
                if (!_drawnDiagnosticTask.IsCompleted) return;
                if (_drawnDiagnosticTask.IsFaulted)
                {
                    PerfLog("[ND-75 drawn] status=diagnostic-error " + _drawnDiagnosticTask.Exception?.GetBaseException().Message);
                    _drawnDiagnosticDisabledAfterError=true;
                }
                else if (!_drawnDiagnosticTask.IsCanceled) PerfLog(_drawnDiagnosticTask.Result);
                _drawnDiagnosticTask=null;
            }
            if (!logDrawnTileSizes || _drawnDiagnosticDisabledAfterError || Time.unscaledTime < _nextDrawnDiagnosticTime) return;
            // ND-76: az élő mérés ~0,2 s háttérköltséget mutatott. A mérési
            // definíció változatlan, de 2 másodpercenként indítunk snapshotot.
            _nextDrawnDiagnosticTime=Time.unscaledTime+2;
            Camera cam=GetAdaptiveCamera();
            if (cam==null) return;
            if (!useAdaptiveLod || useGpuGeometry || cam.orthographic)
            {
                PerfLog($"[ND-75 drawn] frame={Time.frameCount} status=unsupported adaptive={useAdaptiveLod} gpuGeometry={useGpuGeometry} orthographic={cam.orthographic}");
                return;
            }
            var capture=Stopwatch.StartNew();
            var surfaces=new List<DrawnSurfaceSnapshot>(_drawnDiagnosticMeshes.Count);
            var removed=new List<GameObject>();
            Matrix4x4 viewProjection=cam.projectionMatrix*cam.worldToCameraMatrix;
            Plane[] frustum=GeometryUtility.CalculateFrustumPlanes(cam);
            foreach (var pair in _drawnDiagnosticMeshes)
            {
                GameObject go=pair.Key;
                if (go==null || !go.activeInHierarchy) { removed.Add(pair.Key); continue; }
                DrawnSurface data=pair.Value;
                MeshRenderer renderer=data.Renderer;
                if (renderer==null || !renderer.enabled || renderer.forceRenderingOff || (cam.cullingMask & (1<<go.layer))==0) continue;
                if (renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                    || !GeometryUtility.TestPlanesAABB(frustum,renderer.bounds)) continue;
                Material[] materials=renderer.sharedMaterials;
                var cullModes=new int[data.Indices.Count];
                for(int i=0;i<cullModes.Length;i++)
                {
                    Material? mat=i<materials.Length?materials[i]:null;
                    // A projekt saját felszínshadere alapból Cull Back;
                    // HDRP/Lit esetén az anyag tényleges cull-beállítása.
                    cullModes[i]=mat==null ? -1 : mat.HasProperty("_CullModeForward") ? (int)mat.GetFloat("_CullModeForward")
                        : mat.HasProperty("_CullMode") ? (int)mat.GetFloat("_CullMode") : 2;
                }
                surfaces.Add(new DrawnSurfaceSnapshot(data,viewProjection*renderer.localToWorldMatrix,
                    transform.worldToLocalMatrix*renderer.localToWorldMatrix,cullModes));
            }
            foreach(GameObject go in removed) _drawnDiagnosticMeshes.Remove(go);
            if(surfaces.Count==0) { PerfLog($"[ND-75 drawn] frame={Time.frameCount} status=no-uploaded-surface"); return; }
            HashSet<int> hidden=_drawnHiddenStaticQuads;
            HashSet<int> hiddenWater=_drawnHiddenStaticWaterQuads;
            Vector3 localCamera=transform.InverseTransformPoint(cam.transform.position);
            BodyFrameConversion.ToCore(localCamera,out double cameraX,out double cameraY,out double cameraZ);
            double distance=Math.Sqrt((double)localCamera.x*localCamera.x+(double)localCamera.y*localCamera.y+(double)localCamera.z*localCamera.z);
            double altitude=distance-radius;
            double zoom=altitude>0?radius/altitude:double.NaN;
            PlanetOrbitCamera orbit=cam.GetComponent<PlanetOrbitCamera>();
            double appliedDistance=_hasLastCutCameraPosition?Math.Sqrt(_lastAppliedCutView.X*_lastAppliedCutView.X
                +_lastAppliedCutView.Y*_lastAppliedCutView.Y+_lastAppliedCutView.Z*_lastAppliedCutView.Z):double.NaN;
            double appliedDelta=_hasLastCutCameraPosition?Math.Sqrt((cameraX-_lastAppliedCutView.X)*(cameraX-_lastAppliedCutView.X)
                +(cameraY-_lastAppliedCutView.Y)*(cameraY-_lastAppliedCutView.Y)+(cameraZ-_lastAppliedCutView.Z)*(cameraZ-_lastAppliedCutView.Z)):double.NaN;
            bool pending=_cutTask!=null || _pendingTerrainUpload!=null;
            double pendingMs=pending?UploadMilliseconds(Stopwatch.GetTimestamp()-_pendingCutRequestedTicks):0;
            int width=cam.pixelWidth,height=cam.pixelHeight;
            if(width<=0 || height<=0) return;
            var trace=new DrawnTraceSnapshot(this);
            capture.Stop();
            string header=FormattableString.Invariant($"[ND-75 drawn] frame={Time.frameCount} capturedAt={Time.unscaledTime:F3}s wall={DateTime.Now:HH:mm:ss.fff} meshRevision={_drawnDiagnosticRevision} ")
                + FormattableString.Invariant($"cameraDistanceUnits={distance:F6} altitudeAboveBaseUnits={altitude:F6} zoomRatioRoverAltitude={zoom:F4} viewClass={(orbit!=null?orbit.CurrentViewLevel.ToString():"unknown")} ")
                + FormattableString.Invariant($"cameraCore=({cameraX:F6},{cameraY:F6},{cameraZ:F6}) fov={cam.fieldOfView:F3} viewport={width}x{height} ")
                + FormattableString.Invariant($"lodPending={pending} pendingClock=ND95 pendingMs={pendingMs:F1} lastLodApplyAgeMs={(Time.unscaledTime-_drawnLodAppliedAt)*1000:F1} ")
                + $"uploadPending={_pendingTerrainUpload != null} uploadStaged={_pendingTerrainUpload?.Batch.CompletedCount ?? 0} "
                + FormattableString.Invariant($"appliedCutCameraDistanceUnits={appliedDistance:F6} appliedCameraDeltaUnits={appliedDelta:F6} ")
                + FormattableString.Invariant($"captureMs={capture.Elapsed.TotalMilliseconds:F2} activeSurfaceMeshes={surfaces.Count} hiddenStaticQuads={hidden.Count} ")
                + $"hiddenStaticWaterQuads={hiddenWater.Count} waterLod=ND83 independentWater={_appliedWaterSelection != null} waterLeaves={_appliedWaterSelection?.Leaves.Count ?? 0} "
                + $"refinementPending={_lodRefinementPending} selectionTraceStops={trace.Trace?.Count ?? 0} terrainIdentity=ND77 "
                + FormattableString.Invariant($"modelSeed={_adaptiveSeed} modelTimeMyr={deepTimeMyr:R} seaLevel={_adaptiveSeaLevel:R} physicalReliefScale={usePhysicalReliefScale} elevationScale={elevationScale:R} reliefExaggeration={terrainReliefExaggeration:R} plateCount={plateCount}");
            // Egy Unity-egyseg hany kilometer: a bolygo fizikai sugara osztva a
            // render-sugarral (radius). Igy a rajzolt elhossz km-re valtható.
            double unitToKm = radius > 0 ? PlanetConstants.RadiusMeters / radius / 1000.0 : double.NaN;
            var sampleContext = new UniformSampleContext(logUniformTileSamples, Time.frameCount,
                altitude, zoom, cam.fieldOfView, unitToKm, deepTimeMyr,
                orbit != null ? orbit.CurrentViewLevel.ToString() : "unknown", _appliedCutWork);
            // A worker értékmátrixokat, kész listákat és immutábilis proxy/
            // trace snapshotot kap. Nem olvassa a futó kérését vagy Unity objektumot.
            _drawnDiagnosticTask=Task.Run(()=>MeasureDrawnSurfaces(surfaces,hidden,hiddenWater,width,height,header,trace,sampleContext));
        }

        private static string MeasureDrawnSurfaces(List<DrawnSurfaceSnapshot> surfaces,HashSet<int> hidden,HashSet<int> hiddenWater,int width,int height,string header,DrawnTraceSnapshot trace,UniformSampleContext sampleContext)
        {
            var timer=Stopwatch.StartNew();
            var measurement=new RenderedTileDiagnostics(width,height);
            int malformed=0;
            for(int mesh=0;mesh<surfaces.Count;mesh++)
            {
                DrawnSurfaceSnapshot surface=surfaces[mesh];
                List<Vector3> vertices=surface.Vertices;
                Matrix4x4 matrix=surface.ToClip;
                for(int s=0;s<surface.Indices.Count;s++)
                {
                    if(surface.CullModes[s]<0) continue;
                    int[] indices=surface.Indices[s];
                    if(indices.Length%6!=0) { malformed++; continue; }
                    for(int i=0;i<indices.Length;i+=6)
                    {
                        int start=indices[i]/4*4;
                        if(surface.Kind==1 && hidden.Contains(start/4)) continue;
                        if(surface.Kind==3 && hiddenWater.Contains(start/4)) continue;
                        if(start<0 || start+3>=vertices.Count) { malformed++; continue; }
                        bool valid=true;
                        for(int j=0;j<6;j++) if(indices[i+j]<start || indices[i+j]>start+3) { valid=false; break; }
                        if(!valid) { malformed++; continue; }
                        measurement.AddQuad(ProjectDrawnVertex(vertices[start],matrix),ProjectDrawnVertex(vertices[start+1],matrix),
                            ProjectDrawnVertex(vertices[start+2],matrix),ProjectDrawnVertex(vertices[start+3],matrix),
                            indices[i]-start,indices[i+1]-start,indices[i+2]-start,
                            indices[i+3]-start,indices[i+4]-start,indices[i+5]-start,surface.Kind,start/4,surface.CullModes[s],mesh);
                    }
                }
            }
            string outliers=FormatTerrainOutliers(measurement,surfaces,trace);
            string samples=FormatUniformTileSamples(measurement,surfaces,sampleContext);
            timer.Stop();
            return FormatDrawnMeasurement(measurement,header,timer.Elapsed.TotalMilliseconds,malformed)+outliers+samples;
        }

        /// <summary>
        /// FELHASZNÁLÓI KÉRÉS (2026-09-18): soronként EGY minta, gépi
        /// feldolgozásra. Az egyenletes 17×9-es képernyő-raszter MINDEN
        /// találatáról kiírja a zoomot, a képernyő-pozíciót, az égitest-beli
        /// pozíciót, a tile szintjét és a tile méretét pixelben ÉS
        /// kilométerben - így a napló utólag önmagában kiértékelhető
        /// ("adott zoom mellett adott képernyő-pozíción mekkora a tile"),
        /// fejléc-összeillesztés nélkül.
        ///
        /// A méretek KÉT forrásból jönnek, szándékosan:
        ///  - pixel: a MÁR FELTÖLTÖTT quad vetítéséből (RenderedTileDiagnostics),
        ///    ez a LOD-cél (`targetTilePixelSize`) közvetlen mértéke;
        ///  - kilométer: UGYANANNAK a quadnak a rajzolt élhosszából, a
        ///    bolygó fizikai sugarával átskálázva - tehát a tényleges
        ///    geometriát méri, nem egy idealizált szögméret-formulát
        ///    (a cubed-sphere tile-területek ~1,3-1,4x arányban változnak,
        ///    ld. ND-24).
        ///
        /// Lat/lon a Core test-keretében: a pólus a Z tengely
        /// (ld. BodyFrameConversion doksi), ezért lat=asin(z), lon=atan2(y,x).
        /// Ez DIAGNOSZTIKA: a trigonometria itt nem esik az I1/ND-23
        /// determinizmus-hatókörbe (nem a világmodell számítási útja).
        /// </summary>
        /// <summary>
        /// A vágás munka-könyvelése MINDEN minta mellé (felhasználói kérés,
        /// 2026-09-19). Szándékosan ismétlődik soronként: a `[tilesample]`
        /// napló egész tervezési elve, hogy egy sor ÖNMAGÁBAN kiértékelhető
        /// legyen, fejléc-összeillesztés nélkül.
        ///
        /// A `cutSaturated` az a származtatott jelzés, ami a leggyorsabban
        /// megmondja, hogy a látott durva tile a budget miatt durva-e.
        /// </summary>
        private static void AppendCutWork(StringBuilder text, in AppliedCutWork w)
        {
            if (!w.Valid) { text.Append("cutWork=none "); return; }

            // 1) Mennyi tile van most.
            text.Append($"cutLeaves={w.TotalLeaves} dynLeaves={w.DynamicLeaves} ");
            text.Append($"waterLeaves={w.WaterLeaves} cutBudget={w.Budget} ");
            text.Append($"cutSaturated={(w.Budget > 0 && w.TotalLeaves + 4 > w.Budget)} ");
            // 2) Mennyi szamolodott UJRA a zoom/forgatas miatt.
            text.Append($"newSplits={w.NewSplits} reusedLeaves={w.ReusedLeaves} ");
            text.Append($"metricComputed={w.MetricComputed} metricReused={w.MetricHits} ");
            // 3a) Abbahagyva, mert ELVONTAK (a kep rosszabb lett tole).
            text.Append($"starvedBudget={w.BudgetStops} starvedQuota={w.DeferredSplits} ");
            // 3b) Abbahagyva, mert SZUKSEGTELEN (a kep nem lett rosszabb).
            text.Append($"skipSufficient={w.SufficientStops} skipMaxLevel={w.MaxLevelStops} ");
            text.Append($"skipInvisible={w.InvisibleStops} skipOceanBase={w.SkippedBases} ");
            text.Append($"skipOceanLeaf={w.SkippedOceanic} ");
        }

        private static string FormatUniformTileSamples(RenderedTileDiagnostics m,
            List<DrawnSurfaceSnapshot> surfaces, in UniformSampleContext ctx)
        {
            if (!ctx.Enabled) return string.Empty;
            var text = new StringBuilder();
            string F(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
            string F6(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
            string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);
            char Source(int kind) => kind == 1 ? 'S' : kind == 2 ? 'D' : kind == 3 ? 'W' : 'w';

            for (int row = 0; row < m.Rows; row++)
            {
                for (int column = 0; column < m.Columns; column++)
                {
                    var hit = m.Hits[row * m.Columns + column];
                    if (!hit.Found) continue;

                    double px = m.SampleX(column), py = m.SampleY(row);
                    text.Append("\n[tilesample] ");
                    // Minden szam MAR invariansan formazott stringkent kerul be
                    // (F/F6/R), ezert a sima interpolacio is kulturafuggetlen.
                    text.Append($"frame={ctx.Frame} altitudeUnits={F6(ctx.AltitudeUnits)} ");
                    text.Append($"zoomRatio={F(ctx.ZoomRatio)} viewClass={ctx.ViewClass} ");
                    text.Append($"fov={F(ctx.Fov)} deepTimeMyr={R(ctx.DeepTimeMyr)} ");
                    AppendCutWork(text, ctx.Work);
                    text.Append($"col={column} row={row} sx={F(px / m.Width)} sy={F(py / m.Height)} ");
                    text.Append($"px={F(px)} py={F(py)} ");
                    text.Append($"source={Source(hit.Surface)} clippedDiameterPx={F(hit.DiameterPx)} ");
                    text.Append($"clippedWidthPx={F(hit.WidthPx)} clippedHeightPx={F(hit.HeightPx)} ");
                    text.Append($"fullQuadDiameterPx={F(hit.FullDiameterPx)} ");

                    DrawnSurfaceSnapshot surface = surfaces[hit.Mesh];
                    int start = hit.Quad * 4;
                    if (surface.TileIds != null && hit.Quad < surface.TileIds.Length)
                    {
                        TileId tile = surface.TileIds[hit.Quad];
                        tile.GetUV(out uint u, out uint v);
                        text.Append($"tile={tile.Value:X16} face={tile.Face} level={tile.Level} u={u} v={v} ");
                    }
                    else text.Append("tile=unknown face=- level=-1 u=- v=- ");

                    if (start + 3 < surface.Vertices.Count)
                    {
                        // A rajzolt quad NEGY sarka a bolygo LOKALIS tereben - ebbol
                        // jon a kozeppont-irany (lat/lon) ES a tenyleges elhossz.
                        Vector3 a = surface.ToBody.MultiplyPoint3x4(surface.Vertices[start]);
                        Vector3 b = surface.ToBody.MultiplyPoint3x4(surface.Vertices[start + 1]);
                        Vector3 c = surface.ToBody.MultiplyPoint3x4(surface.Vertices[start + 2]);
                        Vector3 d = surface.ToBody.MultiplyPoint3x4(surface.Vertices[start + 3]);
                        Vector3 center = (a + b + c + d) * 0.25f;
                        BodyFrameConversion.ToCore(center, out double cx, out double cy, out double cz);
                        double length = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                        double latDeg = length > 1e-9 ? Math.Asin(Math.Max(-1.0, Math.Min(1.0, cz / length))) * 180.0 / Math.PI : double.NaN;
                        double lonDeg = Math.Atan2(cy, cx) * 180.0 / Math.PI;

                        // Atlagos elhossz a negy oldalbol (a quad nem pontosan
                        // negyzet a gombi vetites miatt).
                        double edgeUnits = (Vector3.Distance(a, b) + Vector3.Distance(b, c)
                            + Vector3.Distance(c, d) + Vector3.Distance(d, a)) * 0.25;
                        text.Append($"lat={F(latDeg)} lon={F(lonDeg)} ");
                        text.Append($"tileEdgeKm={F(edgeUnits * ctx.UnitToKm)} tileEdgeUnits={F6(edgeUnits)}");
                    }
                    else text.Append("lat=- lon=- tileEdgeKm=- tileEdgeUnits=-");
                }
            }
            return text.ToString();
        }

        private static string FormatTerrainOutliers(RenderedTileDiagnostics measurement,
            List<DrawnSurfaceSnapshot> surfaces, DrawnTraceSnapshot trace)
        {
            var candidates=new List<int>();
            for (int i=0;i<measurement.Hits.Length;i++)
            {
                var hit=measurement.Hits[i];
                if (hit.Found && (hit.Surface==1 || hit.Surface==2)) candidates.Add(i);
            }
            candidates.Sort((a,b)=>
            {
                int size=measurement.Hits[b].DiameterPx.CompareTo(measurement.Hits[a].DiameterPx);
                return size!=0 ? size : a.CompareTo(b);
            });
            var seen=new HashSet<(int,int)>();
            var text=new StringBuilder();
            var owners=trace.Coverage!=null ? new LodCornerResolver(trace.BaseLevel,trace.Coverage,
                _=>throw new InvalidOperationException("A diagnosztika csak sarokgazdát kérhet, geometriát nem.")) : null;
            int shown=0;
            string F(double value)=>value.ToString("F3",CultureInfo.InvariantCulture);
            foreach (int sample in candidates)
            {
                var hit=measurement.Hits[sample];
                // Legnagyobb tereptile mindig; további nagy találatokból legfeljebb nyolc.
                if (shown>=8 || (shown>0 && hit.DiameterPx<=24)) break;
                if (!seen.Add((hit.Mesh,hit.Quad))) continue;
                shown++;
                DrawnSurfaceSnapshot surface=surfaces[hit.Mesh];
                text.Append($"\n[ND-77 terrain] sampleRow={measurement.Rows-sample/measurement.Columns} sampleCol={sample%measurement.Columns+1} "+
                    $"mesh={hit.Mesh} quad={hit.Quad} source={(hit.Surface==1?"S":"D")} drawnDiameterPx={F(hit.DiameterPx)} "+
                    $"drawnWidthPx={F(hit.WidthPx)} drawnHeightPx={F(hit.HeightPx)} ");
                if (surface.TileIds==null || hit.Quad>=surface.TileIds.Length)
                { text.Append("tile=unknown stop=unavailable"); continue; }
                TileId tile=surface.TileIds[hit.Quad];
                tile.GetUV(out uint u,out uint v);
                text.Append($"tile={tile.Value:X16} face={tile.Face} level={tile.Level} u={u} v={v} ");
                if (trace.Trace!=null && trace.Trace.TryFindStop(tile,out TileId ancestor,out var stop))
                {
                    text.Append($"stop={stop.Reason} stopTile={ancestor.Value:X16} stopLevel={ancestor.Level} "+
                        $"stopRelation={(ancestor.Equals(tile)?"exact":"ancestor-after-balance-or-coverage")} "+
                        FormattableString.Invariant($"stopErrorRad={stop.Error:R} stopThresholdRad={stop.Threshold:R} ")+
                        $"stopErrorPx={F(trace.PixelScale*Math.Tan(stop.Error))} stopThresholdPx={F(trace.PixelScale*Math.Tan(stop.Threshold))} ");
                }
                else text.Append("stop=unavailable ");
                if (trace.Proxy!=null && trace.RequestView!=null)
                {
                    bool visible=trace.RequestView.EvaluateTerrain(trace.Proxy,tile,out double error);
                    double alpha=1;
                    if (tile.Level>trace.BaseLevel)
                    {
                        TileId parent=tile.Parent();
                        trace.RequestView.EvaluateTerrain(trace.Proxy,parent,out double parentError);
                        double threshold=parent.Level==trace.BaseLevel?trace.BaseThreshold:trace.Threshold;
                        alpha=parentError<=0?0:trace.MorphRange<=0?1:
                            Math.Max(0,Math.Min(1,(1-Math.Tan(threshold)/Math.Tan(parentError))/trace.MorphRange));
                    }
                    text.Append($"requestProxyVisible={visible} requestProxyPx={F(trace.PixelScale*Math.Tan(error))} "+
                        $"requestTileMorphAlpha={F(alpha)} ");
                }
                if (owners!=null && tile.Level>trace.BaseLevel)
                {
                    text.Append("cornerOwners=");
                    for (int corner=0;corner<4;corner++)
                    {
                        TileId owner=owners.CornerOwner(tile.Face,tile.Level,
                            u+(corner==1 || corner==2?1u:0u),v+(corner>=2?1u:0u));
                        if (corner>0) text.Append(';');
                        text.Append($"{owner.Value:X16}/L{owner.Level}");
                    }
                    text.Append(' ');
                }
                // Ez a négy FELTÖLTÖTT pont, nem nyers modellminta. A morph és
                // a sarokgazda hatása benne van; a requestTileMorphAlpha önmagában
                // nem írja le a szomszéd által birtokolt sarkok teljes alakítását.
                text.Append("uploadedQuadCore=");
                for (int corner=0;corner<4;corner++)
                {
                    Vector3 point=surface.ToBody.MultiplyPoint3x4(surface.Vertices[hit.Quad*4+corner]);
                    BodyFrameConversion.ToCore(point,out double x,out double y,out double z);
                    if (corner>0) text.Append(';');
                    text.Append(FormattableString.Invariant($"({x:R},{y:R},{z:R})"));
                }
            }
            if (shown==0) text.Append("\n[ND-77 terrain] status=no-sampled-terrain-hit");
            return text.ToString();
        }

        private static RenderedTileDiagnostics.ClipPoint ProjectDrawnVertex(Vector3 p,Matrix4x4 m)
            => new RenderedTileDiagnostics.ClipPoint(
                (double)m.m00*p.x+(double)m.m01*p.y+(double)m.m02*p.z+m.m03,
                (double)m.m10*p.x+(double)m.m11*p.y+(double)m.m12*p.z+m.m13,
                (double)m.m20*p.x+(double)m.m21*p.y+(double)m.m22*p.z+m.m23,
                (double)m.m30*p.x+(double)m.m31*p.y+(double)m.m32*p.z+m.m33);

        private static string FormatDrawnMeasurement(RenderedTileDiagnostics m,string header,double elapsed,int malformed)
        {
            var sizes=new List<double>();
            int[] sources=new int[5];
            foreach(var hit in m.Hits) if(hit.Found) { sizes.Add(hit.DiameterPx); sources[hit.Surface]++; }
            sizes.Sort();
            double Percentile(double fraction)=>sizes.Count==0?double.NaN:sizes[(int)Math.Ceiling(fraction*(sizes.Count-1))];
            string F(double value)=>value.ToString("F2",CultureInfo.InvariantCulture);
            char Source(int kind)=>kind==1?'S':kind==2?'D':kind==3?'W':'w';
            var center=m.Hits[(m.Rows/2)*m.Columns+m.Columns/2];
            var text=new StringBuilder(header);
            text.Append($"\n[ND-75 size] grid={m.Columns}x{m.Rows} hits={sizes.Count}/{m.Hits.Length} sampledTileClippedDiameterPx_p50={F(Percentile(.5))} p90={F(Percentile(.9))} max={F(Percentile(1))} " +
                $"sourceHits_S={sources[1]} D={sources[2]} W={sources[3]} w={sources[4]} testedQuads={m.TestedQuads} invalidQuads={m.InvalidQuads} malformedQuads={malformed} measureMs={F(elapsed)}");
            text.Append("\n[ND-75 center] ");
            text.Append(center.Found?$"source={Source(center.Surface)} clippedWidthPx={F(center.WidthPx)} clippedHeightPx={F(center.HeightPx)} clippedDiameterPx={F(center.DiameterPx)} fullQuadDiameterPx={F(center.FullDiameterPx)}":"no-surface-hit");
            text.Append("\n[ND-75 map] clippedDiameterPx; top-to-bottom,left-to-right; S=staticTerrain D=dynamicTerrain W=staticWater w=dynamicWater .=noHit; sampled mesh-depth, NOT final GPU image");
            for(int row=m.Rows-1;row>=0;row--)
            {
                text.Append("\n[ND-75 row] ");
                for(int column=0;column<m.Columns;column++)
                {
                    if(column>0) text.Append(' ');
                    var hit=m.Hits[row*m.Columns+column];
                    if(!hit.Found) text.Append('.');
                    else { text.Append(Source(hit.Surface)); text.Append(F(hit.DiameterPx)); }
                }
            }
            return text.ToString();
        }
    }
}
