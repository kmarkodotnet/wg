using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        // ND-76: új felosztásokra vonatkozó munkaadag, nem alacsonyabb végső LOD.
        private const int NewSplitsPerRequest = 1024;
        private bool _lodRefinementPending, _cutSupersededSinceApply;
        private CancellationTokenSource? _cutCancellation;
        private ProjectedLodView? _requestedProjectedView;
        // ND-81: csak a cut/emit worker írja; diagnosztikai task nem használja.
        private LodTerrainEvaluationCache? _terrainEvaluationCache;

        /// <summary>
        /// A vágás levél-költségvetése. <c>adaptiveRenderBudget == 0</c> esetén
        /// a viewportból számoljuk (<see cref="AdaptiveQuadTree.RenderBudgetForViewport"/>
        /// - ott a mérési tábla és az indoklás), egyébként a mezőt kézi
        /// felülbírálásnak vesszük, hogy Play közben sweepelhető maradjon.
        /// Érvénytelen viewportnál (0 pixel, pl. rejtett kamera) a korábbi
        /// kézi alapértékre esünk vissza, nem dobunk kivételt a render-úton.
        /// </summary>
        private int ResolveRenderBudget(int pixelWidth, int pixelHeight)
        {
            if (adaptiveRenderBudget > 0) return adaptiveRenderBudget;
            if (pixelWidth <= 0 || pixelHeight <= 0 || !(targetTilePixelSize > 0))
                return AdaptiveQuadTree.MinimumRenderBudget;
            return AdaptiveQuadTree.RenderBudgetForViewport(pixelWidth, pixelHeight, targetTilePixelSize);
        }

        private void PrepareTerrainEvaluationCache(TerrainLodProxy? proxy)
        {
            if (_requestedProjectedView == null || proxy == null) _terrainEvaluationCache = null;
            else if (_terrainEvaluationCache != null && _terrainEvaluationCache.MatchesProxy(proxy))
                _terrainEvaluationCache.ResetView(_requestedProjectedView);
            else _terrainEvaluationCache = new LodTerrainEvaluationCache(_requestedProjectedView, proxy);
        }
        private double _pendingSurfaceAltitude;
        private Dictionary<TileId, CachedLodChunk> _previousChunkCache = new Dictionary<TileId, CachedLodChunk>();

        private sealed class CachedLodChunk
        {
            public readonly List<Vector3> ResolvedPositions;
            public readonly ConcatenatedMesh Terrain;
            public readonly AdaptiveMeshBuffers Auxiliary;
            public CachedLodChunk(List<Vector3> resolvedPositions, ConcatenatedMesh terrain, AdaptiveMeshBuffers auxiliary)
            { ResolvedPositions=resolvedPositions; Terrain=terrain; Auxiliary=auxiliary; }
        }

        private ProjectedLodView CaptureProjectedLodView(Camera cam, double x, double y, double z)
        {
            SurfacePoint Axis(Vector3 world)
            {
                BodyFrameConversion.ToCore(transform.InverseTransformDirection(world), out double a, out double b, out double c);
                return new SurfacePoint(a,b,c);
            }
            return new ProjectedLodView(new SurfacePoint(x,y,z), Axis(cam.transform.right), Axis(cam.transform.up),
                Axis(cam.transform.forward), cam.fieldOfView*Mathf.Deg2Rad, cam.aspect, cam.nearClipPlane, cam.farClipPlane);
        }

        private void SupersedeObsoleteCut(Camera cam)
        {
            if (_cutTask == null || _cutCancellation == null || _cutCancellation.IsCancellationRequested) return;
            BodyFrameConversion.ToCore(transform.InverseTransformPoint(cam.transform.position), out double x, out double y, out double z);
            var current = CaptureAdaptiveView(cam,x,y,z);
            if (!LodRequestPolicy.ShouldSupersede(_pendingCutView,current,_pendingSurfaceAltitude,
                Time.unscaledTime-_pendingCutRequestedRealtime,_cutSupersededSinceApply)) return;
            _cutSupersededSinceApply = true;
            _cutCancellation.Cancel();
            PerfLog("[ND-76 request] superseded=True; a következő kérés az aktuális kamerát használja");
        }

        private static AdaptiveMeshBuffers CreateAuxiliaryBuffers(float waterRadius) => new AdaptiveMeshBuffers
        {
            WaterVertices = new Dictionary<int,List<Vector3>>(), WaterNormals = new Dictionary<int,List<Vector3>>(),
            WaterTriangles = new Dictionary<int,List<int>>(), WaterColors = new Dictionary<int,List<Color>>(),
            BorderVerts = new List<Vector3>(), BorderIndices = new List<int>(), WaterSurfaceRadius = waterRadius,
        };

        private List<Vector3> CaptureResolvedPositions(List<TileId> leaves)
        {
            var positions = new List<Vector3>(leaves.Count*4);
            foreach (TileId leaf in leaves)
            {
                GetAdaptiveCorners(leaf, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d);
                positions.Add(a); positions.Add(b); positions.Add(c); positions.Add(d);
            }
            return positions;
        }

        private void CaptureGeometryFeedback(AdaptiveMeshBuffers buffers, CancellationToken cancellation)
        {
            if (_terrainEvaluationCache == null || _requestedProjectedView == null || _requestedTerrainLodProxy == null) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            SurfacePoint CorePoint(Vector3 point)
            {
                BodyFrameConversion.ToCore(point, out double x, out double y, out double z);
                return new SurfacePoint(x, y, z);
            }
            void Measure(ConcatenatedMesh mesh)
            {
                if (mesh.TileIds == null) { buffers.GeometryFeedbackUnidentified++; return; }
                for (int i = 0; i < mesh.TileIds.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    TileId tile = mesh.TileIds[i];
                    if (tile.Level >= adaptiveMaxLevel) continue;
                    buffers.GeometryFeedbackMeasured++;
                    int start = i * 4;
                    var quad = new SurfaceQuad(CorePoint(mesh.Vertices[start]), CorePoint(mesh.Vertices[start+1]),
                        CorePoint(mesh.Vertices[start+2]), CorePoint(mesh.Vertices[start+3]));
                    if (!_requestedProjectedView.EvaluateQuad(quad, out double actual)
                        || actual <= _currentTargetAngularRadiusRadians * 1.01) continue;
                    bool visible = _terrainEvaluationCache.EvaluateTerrain(_requestedTerrainLodProxy, tile, out double predicted, out _);
                    if ((visible && predicted >= actual * .99) || !_terrainEvaluationCache.Geometry.Record(tile, quad)) continue;
                    buffers.GeometryFeedbackAdded++;
                }
            }
            // Csak a kész, tényleges emissziós mesh: nincs új modellminta és
            // nincs frame-enkénti GPU-readback. A következő cut új nézettel is mérhet.
            if (buffers.TerrainConcat != null) Measure(buffers.TerrainConcat);
            else
            {
                var keys = new List<TileId>(buffers.ChunkCache.Keys);
                keys.Sort((a,b) => a.Value.CompareTo(b.Value));
                foreach (TileId key in keys) Measure(buffers.ChunkCache[key].Terrain);
            }
            buffers.GeometryRefinementPending = buffers.GeometryFeedbackAdded > 0
                && buffers.Cut.Count + 4 <= adaptiveRenderBudget;
            buffers.GeometryFeedbackMs = timer.Elapsed.TotalMilliseconds;
        }

        // A víz színe is függhet a morpholt terrain-pozíciótól, ezért csak a
        // teljesen azonos feloldott geometria esetén használjuk újra az emitjét.
        private static void AppendAuxiliaryBuffers(AdaptiveMeshBuffers source, AdaptiveMeshBuffers target)
        {
            foreach (var pair in source.WaterVertices)
            {
                int key = pair.Key;
                if (!target.WaterVertices.TryGetValue(key, out List<Vector3> vertices))
                {
                    target.WaterVertices[key] = vertices = new List<Vector3>();
                    target.WaterNormals[key] = new List<Vector3>(); target.WaterTriangles[key] = new List<int>();
                    target.WaterColors[key] = new List<Color>();
                }
                int start = vertices.Count;
                vertices.AddRange(pair.Value); target.WaterNormals[key].AddRange(source.WaterNormals[key]);
                target.WaterColors[key].AddRange(source.WaterColors[key]);
                foreach (int index in source.WaterTriangles[key]) target.WaterTriangles[key].Add(index+start);
            }
            int borderStart = target.BorderVerts.Count;
            target.BorderVerts.AddRange(source.BorderVerts);
            foreach (int index in source.BorderIndices) target.BorderIndices.Add(index+borderStart);
        }
    }
}
