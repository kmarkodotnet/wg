using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        private const int WaterLeafBudget = 8192, WaterSplitsPerRequest = 256;
        private const int WaterColorCacheLimit = 65536;
        private WaterLodSource? _waterLodSource;
        private LodTerrainEvaluationCache? _waterEvaluationCache;
        private WaterLodSelection? _appliedWaterSelection;
        private TerrainIndexMask? _waterIndexMask;
        private Mesh? _staticWaterMesh;
        private bool _requestedIndependentWater;
        private int _activeWaterBuffer;
        private HashSet<int> _drawnHiddenStaticWaterQuads = new HashSet<int>();
        private readonly Dictionary<(int Face, int Level, uint U, uint V), Color> _waterCornerColors = new();
        private int _waterColorSamples;

        // A tényleges emit sorrendjét kapcsoljuk az upload tényleges index-layoutjához.
        private void InitializeIndependentWater(StaticMeshBuckets? buckets, float seaRadius)
        {
            _waterLodSource = null;
            _waterEvaluationCache = null;
            _waterIndexMask = null;
            _staticWaterMesh = null;
            // HIBAJAVITAS (2026-09-20). Ez a metodus UJ WaterLodSource-t hoz
            // letre, a `_appliedWaterSelection` viszont a REGIRE mutat - a
            // `WaterLodSource.Select` pedig ReferenceEquals-szel ellenorzi a
            // forrast, es "Uj viz-snapshothoz uj kivalasztas kell" kivetelt dob.
            //
            // Eddig ez nem latszott, mert a Build() a sajat
            // InvalidateAdaptiveCaches()-eben MAR nullazta - vagyis a helyes
            // allapotot a HIVO tartotta fenn, nem az, aki az elavulast okozza.
            // Amint az ND-50 overlay-utja (ApplyOverlayOnlyRebuild) a
            // BuildStaticBaseLayer()-t a teljes Build NELKUL hivta meg, a
            // kivetel azonnal jott, es a nezegeto TARTOSAN visszaallt a
            // szinkron (fo szalu) ujraepitesre. Ezert a nullazas ODA kerult,
            // AHOL az elavulas keletkezik - igy minden jovobeli hivo biztonsagos.
            //
            // A statikus viz-mesh ilyenkor UJRAIRODOTT, tehat a regi layout
            // rejtett-quad halmazat sem szabad atvinni (ugyanaz az indok, mint
            // az InvalidateAdaptiveCaches `restoreStaticIndices: false`-anal).
            _appliedWaterSelection = null;
            _drawnHiddenStaticWaterQuads = new HashSet<int>();
            if (buckets == null || seaRadius <= 0 || float.IsNaN(seaRadius) || float.IsInfinity(seaRadius)) return;
            GameObject target = transform.Find("WaterSurface").gameObject;
            if (!_drawnDiagnosticMeshes.TryGetValue(target, out DrawnSurface surface)) return;
            Mesh mesh = target.GetComponent<MeshFilter>().sharedMesh;
            int side = 1 << adaptiveBaseLevel;
            var offsets = Enumerable.Repeat(-1, 6 * side * side).ToArray();
            int total = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                SubMeshDescriptor d = mesh.GetSubMesh(s);
                if (d.baseVertex != 0 || d.indexCount != surface.Indices[s].Length)
                    throw new InvalidOperationException("Váratlan statikus víz-index layout.");
                total = Math.Max(total, d.indexStart + d.indexCount);
            }
            var indices = new int[total];
            var roots = new List<TileId>();
            int submesh = 0;
            foreach (List<TileId> tiles in buckets.WaterTiles)
            {
                if (tiles.Count == 0) continue;
                SubMeshDescriptor d = mesh.GetSubMesh(submesh);
                if (d.indexCount != tiles.Count * 6)
                    throw new InvalidOperationException("A víz-emisszió azonosítói nem fedik a mesh-t.");
                Array.Copy(surface.Indices[submesh], 0, indices, d.indexStart, d.indexCount);
                for (int i = 0; i < tiles.Count; i++)
                    offsets[TerrainIndexMask.DenseIndex(tiles[i])] = d.indexStart + i * 6;
                roots.AddRange(tiles);
                submesh++;
            }
            if (submesh != mesh.subMeshCount) throw new InvalidOperationException("Hiányos víz-bucket térkép.");
            _waterIndexMask = new TerrainIndexMask(adaptiveBaseLevel, offsets, indices, allowMissingTiles: true);
            _staticWaterMesh = mesh;
            _waterLodSource = new WaterLodSource(adaptiveBaseLevel, seaRadius, roots);
            PerfLog($"[ND-83 water source] base={adaptiveBaseLevel} roots={roots.Count} seaRadius={seaRadius:R} " +
                $"leafBudget={WaterLeafBudget} splitLimit={WaterSplitsPerRequest}");
        }

        private void PrepareIndependentWaterRequest()
        {
            bool enabled = _requestedProjectedView != null
                && _waterLodSource?.BaseLevel == adaptiveBaseLevel && adaptiveMaxLevel >= adaptiveBaseLevel;
            // A terep-chunk cache-ben lévő régi víz-emisszió nem élheti túl a módváltást.
            if (enabled != _requestedIndependentWater) _previousChunkCache.Clear();
            _requestedIndependentWater = enabled;
        }

        private void ComputeIndependentWater(AdaptiveMeshBuffers target, CancellationToken cancellation)
        {
            if (!_requestedIndependentWater) return;
            var timer = Stopwatch.StartNew();
            _waterEvaluationCache = _waterLodSource!.CreateEvaluationCache(_requestedProjectedView!, _waterEvaluationCache);
            WaterLodSelection selection = _waterLodSource!.Select(_requestedProjectedView!, _appliedWaterSelection,
                adaptiveMaxLevel, _currentTargetAngularRadiusRadians,
                AdaptiveViewState.MergeThreshold(_currentTargetAngularRadiusRadians, mergeHysteresisFactor),
                WaterLeafBudget, WaterSplitsPerRequest, cancellation, _waterEvaluationCache);
            target.WaterSelection = selection;
            target.WaterSelectionMs = timer.Elapsed.TotalMilliseconds;
            // A víz nem morphol: azonos cut és Build esetén az attribútumok is azonosak.
            if (selection.ReusedSelection || (_appliedWaterSelection != null
                && selection.Leaves.SequenceEqual(_appliedWaterSelection.Leaves))) return;
            timer.Restart();
            _waterColorSamples = 0;
            AdaptiveMeshBuffers water = CreateAuxiliaryBuffers(target.WaterSurfaceRadius);
            var positions = selection.CreateCornerResolver(RawWaterPositionQuad);
            var colors = selection.CreateCornerResolver(RawWaterColorQuad);
            if (selection.Leaves.Count > 0)
            {
                var verts = water.WaterVertices[0] = new List<Vector3>(selection.Leaves.Count * 4);
                var normals = water.WaterNormals[0] = new List<Vector3>(selection.Leaves.Count * 4);
                var triangles = water.WaterTriangles[0] = new List<int>(selection.Leaves.Count * 6);
                var rgba = water.WaterColors[0] = new List<Color>(selection.Leaves.Count * 4);
                foreach (TileId leaf in selection.Leaves)
                {
                    cancellation.ThrowIfCancellationRequested();
                    SurfaceQuad p = ResolveWaterQuad(positions, leaf), c = ResolveWaterQuad(colors, leaf);
                    AddQuad(verts, normals, triangles, rgba,
                        WaterColor(c.P00), WaterColor(c.P10), WaterColor(c.P11), WaterColor(c.P01),
                        WaterPosition(p.P00), WaterPosition(p.P10), WaterPosition(p.P11), WaterPosition(p.P01));
                }
            }
            cancellation.ThrowIfCancellationRequested();
            target.IndependentWaterGeometry = water;
            target.WaterEmitMs = timer.Elapsed.TotalMilliseconds;
            target.WaterColorSamples = _waterColorSamples;
        }

        private static Vector3 WaterPosition(SurfacePoint p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        private static Color WaterColor(SurfacePoint p) => new Color((float)p.X, (float)p.Y, (float)p.Z, 1);

        private static SurfaceQuad ResolveWaterQuad(LodCornerResolver resolver, TileId tile)
        {
            tile.GetUV(out uint u, out uint v);
            return new SurfaceQuad(resolver.Corner(tile.Face, tile.Level, u, v),
                resolver.Corner(tile.Face, tile.Level, u + 1, v),
                resolver.Corner(tile.Face, tile.Level, u + 1, v + 1),
                resolver.Corner(tile.Face, tile.Level, u, v + 1));
        }

        private SurfaceQuad RawWaterPositionQuad(TileId tile)
        {
            TileGeometry.GetContinuousBounds(tile, out double u0, out double u1, out double v0, out double v1);
            float r = (float)_waterLodSource!.SeaRadius;
            return new SurfaceQuad(ToSurfacePoint(ToWaterVector3(tile.Face, u0, v0, r)),
                ToSurfacePoint(ToWaterVector3(tile.Face, u1, v0, r)),
                ToSurfacePoint(ToWaterVector3(tile.Face, u1, v1, r)),
                ToSurfacePoint(ToWaterVector3(tile.Face, u0, v1, r)));
        }

        private SurfaceQuad RawWaterColorQuad(TileId tile)
        {
            tile.GetUV(out uint u, out uint v);
            SurfacePoint At(uint cu, uint cv)
            {
                var key = (tile.Face, tile.Level, cu, cv);
                if (!_waterCornerColors.TryGetValue(key, out Color c))
                {
                    // Nyers modellmagasság: nincs terrain-morph, normál vagy biome-emisszió.
                    Vector3 land = GetOrComputePersistentCorner(tile.Face, tile.Level, cu, cv);
                    c = ContinuousWaterCornerColor(land, _adaptiveSeaLevel);
                    _waterColorSamples++;
                    if (_waterCornerColors.Count < WaterColorCacheLimit) _waterCornerColors.Add(key, c);
                }
                return new SurfacePoint(c.r, c.g, c.b);
            }
            return new SurfaceQuad(At(u, v), At(u + 1, v), At(u + 1, v + 1), At(u, v + 1));
        }

        private int ApplyWaterCoverage(IEnumerable<TileId> roots)
        {
            if (_waterIndexMask == null || _staticWaterMesh == null) return 0;
            var ranges = _waterIndexMask.SetHidden(roots);
            int count = 0;
            foreach (TerrainIndexMask.Range range in ranges)
            {
                _staticWaterMesh.SetIndexBufferData(_waterIndexMask.Indices, range.Start, range.Start, range.Count,
                    MeshUpdateFlags.DontRecalculateBounds);
                count += range.Count;
            }
            _drawnHiddenStaticWaterQuads = _waterIndexMask.CopyHiddenQuadIndices();
            if (ranges.Count > 0) _drawnDiagnosticRevision++;
            return count;
        }

        private void ApplyIndependentWater(AdaptiveMeshBuffers b)
        {
            if (b.WaterSelection == null)
            {
                ResetIndependentWaterRendering();
                return;
            }
            var timer = Stopwatch.StartNew();
            int maskCount = 0;
            double waterMaskMs = 0;
            if (b.IndependentWaterGeometry != null)
            {
                // A jelenleg látható bufferhez az új mesh teljes feltöltéséig nem nyúlunk.
                int next = 1 - _activeWaterBuffer;
                AdaptiveMeshBuffers water = b.IndependentWaterGeometry;
                if (b.StagedIndependentWater != null)
                    PublishAuxiliaryUpload("IndependentWater", "IndependentWater" + next, b.StagedIndependentWater);
                else BuildWaterSurface(water.WaterVertices, water.WaterNormals, water.WaterTriangles, water.WaterColors,
                    "IndependentWater" + next);
                var maskTimer = Stopwatch.StartNew();
                maskCount = ApplyWaterCoverage(b.WaterSelection.ReplacedRoots);
                waterMaskMs = maskTimer.Elapsed.TotalMilliseconds;
                Transform previous = transform.Find("IndependentWater" + _activeWaterBuffer);
                if (previous != null) previous.gameObject.SetActive(false);
                _activeWaterBuffer = next;
            }
            _appliedWaterSelection = b.WaterSelection;
            _lodRefinementPending |= b.WaterSelection.RefinementPending;
            PerfLog($"[ND-83 water apply] leaves={b.WaterSelection.Leaves.Count} replacedBase={b.WaterSelection.ReplacedRoots.Count} " +
                $"deepestLevel={b.WaterSelection.DeepestLevel} newSplits={b.WaterSelection.NewSplits} deferredSplits={b.WaterSelection.DeferredSplits} " +
                $"selectionTotal={b.WaterSelectionMs:F2}ms selection={b.WaterSelection.SelectionMs:F2}ms balance={b.WaterSelection.BalanceMs:F2}ms " +
                $"emit={b.WaterEmitMs:F2}ms colorSamples={b.WaterColorSamples} colorCache={_waterCornerColors.Count} " +
                $"upload={timer.Elapsed.TotalMilliseconds:F2}ms waterMask={waterMaskMs:F2}ms " +
                $"staged={b.StagedIndependentWater != null} maskIndices={maskCount} reusedMesh={b.IndependentWaterGeometry == null} " +
                $"selectionReusePolicy=ND98 reusedSelection={b.WaterSelection.ReusedSelection}");
        }

        private void RestoreWaterCoverageAfterFailure()
        {
            if (_waterIndexMask == null) return;
            TerrainIndexMask.Range range = _waterIndexMask.RestoreAll();
            if (range.Count > 0)
            {
                if (_staticWaterMesh == null)
                    throw new InvalidOperationException("ND-92: hiányzó statikus vízmesh a maszk helyreállításához.");
                _staticWaterMesh.SetIndexBufferData(_waterIndexMask.Indices, range.Start, range.Start, range.Count,
                    MeshUpdateFlags.DontRecalculateBounds);
            }
            _drawnHiddenStaticWaterQuads = _waterIndexMask.CopyHiddenQuadIndices();
            _drawnDiagnosticRevision++;
            PerfLog($"[ND-92 water recovery] indices={range.Count} restored=True");
        }

        private void ResetIndependentWaterRendering(bool restoreStaticIndices = true)
        {
            if (restoreStaticIndices) ApplyWaterCoverage(Array.Empty<TileId>());
            _appliedWaterSelection = null;
            // ND-92: puszta kikapcsolás nem igazolja a statikus GPU-maszk visszaállását.
            if (restoreStaticIndices) _drawnHiddenStaticWaterQuads = new HashSet<int>();
            for (int i = 0; i < 2; i++)
            {
                Transform child = transform.Find("IndependentWater" + i);
                if (child != null) child.gameObject.SetActive(false);
            }
        }
    }
}
