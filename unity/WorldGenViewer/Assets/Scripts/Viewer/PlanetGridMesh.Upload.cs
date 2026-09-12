using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        [SerializeField, Tooltip("ND-85/86: CPU async terep, víz és határvonal feltöltése több frame-ben, egyetlen kész fedésváltással.")]
        private bool useStagedTerrainUpload = true;
        private const double TerrainUploadSliceMs = 2;
        private const int TerrainUploadsPerFrame = 128;
        private int _uploadConfigRevision, _requestedUploadConfigRevision;
        private bool _stagedUploadDisabledAfterError;
        private PendingTerrainUpload? _pendingTerrainUpload;
        // Egy tartalék a meglévő chunk-cache minden érintett kulcsához.
        private readonly Dictionary<TileId, Mesh> _spareTerrainMeshes = new();

        private sealed class PreparedTerrainUpload
        {
            public readonly Mesh Mesh;
            public readonly Material[] Materials;
            public readonly ConcatenatedMesh Data;
            public PreparedTerrainUpload(Mesh mesh, Material[] materials, ConcatenatedMesh data)
            { Mesh = mesh; Materials = materials; Data = data; }
        }

        private readonly struct TerrainUploadStage
        {
            public readonly string Kind;
            public readonly TileId Key;
            public readonly Action Run;
            public TerrainUploadStage(string kind, Action run, TileId key = default)
            { Kind = kind; Run = run; Key = key; }
        }

        private sealed class StagedTerrainMesh
        {
            public readonly Mesh Mesh;
            public readonly Material[] Materials;
            public readonly ConcatenatedMesh Data;
            public readonly GameObject Target;
            public readonly MeshFilter Filter;
            public readonly MeshRenderer Renderer;
            public readonly DrawnSurface Diagnostic;
            public StagedTerrainMesh(Mesh mesh, Material[] materials, ConcatenatedMesh data, GameObject target)
            {
                Mesh = mesh; Materials = materials; Data = data; Target = target;
                Filter = target.GetComponent<MeshFilter>(); Renderer = target.GetComponent<MeshRenderer>();
                Diagnostic = new DrawnSurface(Renderer, data.Vertices, data.SubmeshTriangles, 2, data.TileIds);
            }
        }

        private sealed class PendingTerrainUpload
        {
            public readonly AdaptiveMeshBuffers Buffers;
            public readonly LodUploadBatch<TerrainUploadStage> Batch;
            public readonly int TerrainJobCount;
            public readonly int ConfigRevision;
            public bool CommitStarted;
            public PendingTerrainUpload(PlanetGridMesh owner, AdaptiveMeshBuffers buffers, int configRevision)
            {
                Buffers = buffers; ConfigRevision = configRevision;
                var jobs = new List<KeyValuePair<TileId, ConcatenatedMesh>>(buffers.ChangedChunkTerrain);
                jobs.AddRange(buffers.PositionOnlyTerrain);
                jobs.Sort((a, b) => a.Key.Value.CompareTo(b.Key.Value));
                TerrainJobCount = jobs.Count;
                var stages = new List<TerrainUploadStage>();
                foreach (var job in jobs)
                {
                    PreparedTerrainUpload? prepared = null;
                    stages.Add(new TerrainUploadStage("terrainMesh", () =>
                        prepared = owner.StageTerrainMesh(buffers, job), job.Key));
                    stages.Add(new TerrainUploadStage("terrainTarget", () =>
                        owner.StageTerrainTarget(buffers, job.Key, prepared
                            ?? throw new InvalidOperationException("Hiányzó előkészített terepmesh.")), job.Key));
                }
                stages.Add(new TerrainUploadStage("legacyWater", () => MeasureAuxiliaryStage(buffers, () =>
                    buffers.StagedLegacyWater = owner.StageWaterUpload("LegacyWater", buffers.PreparedLegacyWater!))));
                if (buffers.PreparedIndependentWater != null)
                    stages.Add(new TerrainUploadStage("oceanWater", () => MeasureAuxiliaryStage(buffers, () =>
                        buffers.StagedIndependentWater = owner.StageWaterUpload("IndependentWater", buffers.PreparedIndependentWater))));
                stages.Add(new TerrainUploadStage("borders", () => MeasureAuxiliaryStage(buffers,
                    () => buffers.StagedBorders = owner.StageBorderUpload(buffers))));
                stages.Add(new TerrainUploadStage("terrainMask", () => owner.StageTerrainCoverage(buffers)));
                Batch = new LodUploadBatch<TerrainUploadStage>(stages);
                buffers.StagedTerrain = new Dictionary<TileId, StagedTerrainMesh>();
            }
        }

        private bool BeginStagedTerrainUpload(AdaptiveMeshBuffers buffers)
        {
            if (!useStagedTerrainUpload || _stagedUploadDisabledAfterError || buffers.ChangedChunkTerrain == null
                || buffers.PreparedLegacyWater == null)
                return false;
            _pendingTerrainUpload = new PendingTerrainUpload(this, buffers, _requestedUploadConfigRevision);
            PerfLog($"[ND-85 upload begin] pipeline=ND94 chunks={_pendingTerrainUpload.TerrainJobCount} jobs={_pendingTerrainUpload.Batch.Count} " +
                $"sliceMs={TerrainUploadSliceMs} maxJobsPerFrame={TerrainUploadsPerFrame}");
            return true;
        }

        private void TickStagedTerrainUpload()
        {
            PendingTerrainUpload pending = _pendingTerrainUpload!;
            if (_fullBuildRequestedAfterCut || WorldConfigChangedSinceBuild()
                || pending.ConfigRevision != _uploadConfigRevision || !useStagedTerrainUpload || useGpuGeometry)
            {
                CancelStagedTerrainUpload();
                return;
            }
            try
            {
                if (pending.Batch.ReadyToCommit)
                {
                    // A végső staging és a commit külön Update: költségeik nem adódnak össze.
                    pending.CommitStarted = true;
                    pending.Batch.Commit(() => CompleteAsyncMeshRequest(pending.Buffers));
                    _pendingTerrainUpload = null;
                    return;
                }
                var timer = Stopwatch.StartNew();
                double maxJobMs = 0;
                string maxJobType = "none";
                TileId maxJobKey = default;
                int staged = pending.Batch.StageSlice(stage =>
                {
                    long started = Stopwatch.GetTimestamp();
                    stage.Run();
                    double elapsedJob = UploadMilliseconds(Stopwatch.GetTimestamp() - started);
                    if (elapsedJob > maxJobMs)
                    { maxJobMs = elapsedJob; maxJobType = stage.Kind; maxJobKey = stage.Key; }
                },
                    () => timer.Elapsed.TotalMilliseconds, TerrainUploadSliceMs, TerrainUploadsPerFrame);
                double elapsed = timer.Elapsed.TotalMilliseconds;
                pending.Buffers.UploadStageFrames++;
                pending.Buffers.UploadStageMs += elapsed;
                pending.Buffers.UploadMaxSliceMs = Math.Max(pending.Buffers.UploadMaxSliceMs, elapsed);
                PerfLog($"[ND-85 upload slice] pipeline=ND94 frame={Time.frameCount} jobs={staged} " +
                    $"done={pending.Batch.CompletedCount}/{pending.Batch.Count} elapsed={elapsed:F2}ms " +
                    $"maxJobType={maxJobType} maxJobMs={maxJobMs:F3} maxJobKey={maxJobKey.Value:X16} " +
                    $"ready={pending.Batch.ReadyToCommit}; published=False");
            }
            catch (Exception error)
            {
                CancelStagedTerrainUpload();
                _stagedUploadDisabledAfterError = true;
                _hasLastCutCameraPosition = false;
                UnityEngine.Debug.LogError("ND-85: a több frame-es upload hibás; a következő kérés a korábbi " +
                    "egylépéses feltöltést használja. " + error);
            }
        }

        private PreparedTerrainUpload StageTerrainMesh(AdaptiveMeshBuffers buffers, KeyValuePair<TileId, ConcatenatedMesh> job)
        {
            _inactiveTerrainChunks.Remove(job.Key);
            long started = Stopwatch.GetTimestamp();
            if (!_spareTerrainMeshes.TryGetValue(job.Key, out Mesh spare) || spare == null)
            {
                spare = new Mesh { indexFormat = IndexFormat.UInt32, name = "LodUpload_" + job.Key.Value };
                _ownedTerrainChunkMeshes.Add(spare);
                _spareTerrainMeshes[job.Key] = spare;
            }
            // Nincs MeshFilter/Renderer hozzárendelés: félkész adat nem rajzolódhat ki.
            UploadTerrainMeshData(spare, job.Value);
            Material[] materials = TerrainMaterials(job.Value);
            buffers.TerrainStageMeshTicks += Stopwatch.GetTimestamp() - started;
            buffers.UploadStagedVertices += job.Value.Vertices.Count;
            return new PreparedTerrainUpload(spare, materials, job.Value);
        }

        private void StageTerrainTarget(AdaptiveMeshBuffers buffers, TileId key, PreparedTerrainUpload prepared)
        {
            long started = Stopwatch.GetTimestamp();
            bool exists = _dynamicChunkGameObjects.TryGetValue(key, out GameObject target) && target != null;
            if (!exists)
            {
                target = GetOrCreateChunkRenderTarget(key, activateNew: false);
                buffers.NewTerrainTargets.Add(key, target);
            }
            else buffers.ReusedTerrainTargets++;
            buffers.StagedTerrain!.Add(key, new StagedTerrainMesh(prepared.Mesh, prepared.Materials, prepared.Data, target!));
            buffers.TerrainStageTargetTicks += Stopwatch.GetTimestamp() - started;
        }

        private void PublishStagedTerrain(AdaptiveMeshBuffers buffers, TileId key)
        {
            StagedTerrainMesh staged = buffers.StagedTerrain![key];
            long started = Stopwatch.GetTimestamp();
            MeshFilter filter = staged.Filter;
            Mesh previous = filter.sharedMesh;
            filter.sharedMesh = staged.Mesh;
            // A régi mesh lesz a következő kérés láthatatlan tartaléka.
            if (previous != null && _ownedTerrainChunkMeshes.Contains(previous)) _spareTerrainMeshes[key] = previous;
            else _spareTerrainMeshes.Remove(key);
            staged.Renderer.sharedMaterials = staged.Materials;
            buffers.TerrainSwapTicks += Stopwatch.GetTimestamp() - started;
            started = Stopwatch.GetTimestamp();
            _drawnDiagnosticMeshes[staged.Target] = staged.Diagnostic;
            _drawnDiagnosticRevision++;
            buffers.TerrainDiagnosticTicks += Stopwatch.GetTimestamp() - started;
            started = Stopwatch.GetTimestamp();
            if (!staged.Target.activeSelf) staged.Target.SetActive(true);
            buffers.TerrainActivationTicks += Stopwatch.GetTimestamp() - started;
        }

        private static double UploadMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private void StageTerrainCoverage(AdaptiveMeshBuffers buffers)
        {
            if (_terrainIndexMask == null) return;
            long started = Stopwatch.GetTimestamp();
            buffers.PreparedTerrainMask = _terrainIndexMask.PrepareHidden(buffers.ReplacedBaseTiles);
            if (buffers.PreparedTerrainMask.Ranges.Count > 0)
                buffers.PreparedTerrainHiddenQuads = buffers.PreparedTerrainMask.CopyHiddenQuadIndices();
            buffers.TerrainMaskPlanTicks = Stopwatch.GetTimestamp() - started;
        }

        private void DiscardPreparedTerrainTargets(AdaptiveMeshBuffers buffers)
        {
            // Kizárólag a még nem publikált kérés új, inaktív céljai saját tulajdonúak.
            foreach (var pair in buffers.NewTerrainTargets)
            {
                if (_dynamicChunkGameObjects.TryGetValue(pair.Key, out GameObject target) && target == pair.Value)
                    _dynamicChunkGameObjects.Remove(pair.Key);
                if (pair.Value != null) SafeDestroy(pair.Value);
            }
            buffers.NewTerrainTargets.Clear();
        }

        private void CancelStagedTerrainUpload()
        {
            if (_pendingTerrainUpload == null) return;
            bool commitStarted = _pendingTerrainUpload.CommitStarted;
            _pendingTerrainUpload.Batch.Cancel();
            if (!commitStarted) DiscardPreparedTerrainTargets(_pendingTerrainUpload.Buffers);
            foreach (var job in _pendingTerrainUpload.Buffers.ChangedChunkTerrain)
                RememberInactiveTerrainChunk(job.Key);
            foreach (var job in _pendingTerrainUpload.Buffers.PositionOnlyTerrain)
                RememberInactiveTerrainChunk(job.Key);
            _pendingTerrainUpload = null;
            _lodRefinementPending = true;
            PerfLog(commitStarted
                ? "[ND-85 upload discarded] commitAttempted=True; a commit hibakezelése állítja vissza a fedést"
                : "[ND-85 upload discarded] published=False; a régi fedés marad");
        }

        private void ClearUploadSpareMeshes()
        {
            CancelStagedTerrainUpload();
            foreach (Mesh mesh in _spareTerrainMeshes.Values)
                DestroyOwnedTerrainChunkMesh(mesh);
            _spareTerrainMeshes.Clear();
            foreach (Mesh mesh in _spareAuxiliaryMeshes.Values)
                if (mesh != null) SafeDestroy(mesh);
            _spareAuxiliaryMeshes.Clear();
        }

        private void OnDisable() => CancelStagedTerrainUpload();
        private void OnDestroy()
        {
            // A szülő megszűnése már törli a gyerekeket; csak saját mesh-einket takarítjuk.
            ClearAllDynamicChunkResources(destroyTargets: false);
            if (_uploadBorderMaterial != null) SafeDestroy(_uploadBorderMaterial);
        }
    }
}
