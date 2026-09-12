using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        [SerializeField, Min(0), Tooltip("ND-93: megőrzött inaktív terepchunk-kulcsok célkorlátja. A többlet időkeretesen ürül; aktív fedést nem korlátoz.")]
        private int inactiveTerrainChunkLimit = 128;
        private const int ChunkEvictionsPerFrame = 8;
        private const double ChunkEvictionSliceMs = 0.5;
        private readonly InactiveChunkQueue _inactiveTerrainChunks = new();
        private readonly HashSet<Mesh> _ownedTerrainChunkMeshes = new();
        private float _nextChunkCacheLogTime;
        private int _chunkCacheEvictedSinceLog;
        private double _chunkCacheEvictionMsSinceLog;

        private void RememberInactiveTerrainChunk(TileId key)
        {
            bool hasTarget = _dynamicChunkGameObjects.TryGetValue(key, out GameObject target) && target != null;
            if (hasTarget && target!.activeSelf) return;
            if (hasTarget || _spareTerrainMeshes.ContainsKey(key)) _inactiveTerrainChunks.MarkInactive(key);
        }

        private void DestroyOwnedTerrainChunkMesh(Mesh mesh)
        {
            // Kizárólag saját runtime mesh; külső sharedMesh asset nem törölhető.
            if (_ownedTerrainChunkMeshes.Remove(mesh) && mesh != null) SafeDestroy(mesh);
        }

        private void ReleaseTerrainChunkResources(TileId key, bool destroyTarget = true)
        {
            _inactiveTerrainChunks.Remove(key);
            if (_dynamicChunkGameObjects.TryGetValue(key, out GameObject target))
            {
                _dynamicChunkGameObjects.Remove(key);
                if (!object.ReferenceEquals(target, null)) _drawnDiagnosticMeshes.Remove(target);
                if (target != null)
                {
                    MeshFilter filter = target.GetComponent<MeshFilter>();
                    if (filter != null)
                    {
                        Mesh mesh = filter.sharedMesh;
                        filter.sharedMesh = null;
                        DestroyOwnedTerrainChunkMesh(mesh);
                    }
                    if (destroyTarget) SafeDestroy(target);
                }
            }
            if (_spareTerrainMeshes.TryGetValue(key, out Mesh spare))
            {
                _spareTerrainMeshes.Remove(key);
                DestroyOwnedTerrainChunkMesh(spare);
            }
            _drawnDiagnosticRevision++;
        }

        private void TickUnusedTerrainChunks()
        {
            // A worker nem használ Unity-chunkokat; a staging viszont már birtokolhat tartalékot.
            if (_pendingTerrainUpload != null) return;
            int examined = 0;
            long started = Stopwatch.GetTimestamp();
            while (examined < ChunkEvictionsPerFrame && _inactiveTerrainChunks.TryTakeExcess(
                Mathf.Max(0, inactiveTerrainChunkLimit), out TileId key))
            {
                examined++;
                // Védőháló: a publikált CPU-diffhez tartozó cél sem eviktálható.
                bool active = _dynamicChunkGameObjects.TryGetValue(key, out GameObject target)
                    && target != null && target.activeSelf;
                if (!active && !_previousChunkGroups.ContainsKey(key))
                {
                    ReleaseTerrainChunkResources(key);
                    _chunkCacheEvictedSinceLog++;
                }
                if (UploadMilliseconds(Stopwatch.GetTimestamp() - started) >= ChunkEvictionSliceMs) break;
            }
            _chunkCacheEvictionMsSinceLog += UploadMilliseconds(Stopwatch.GetTimestamp() - started);
            if (Time.unscaledTime < _nextChunkCacheLogTime) return;
            _nextChunkCacheLogTime = Time.unscaledTime + 1f;
            PerfLog($"[ND-93 terrain cache] targets={_dynamicChunkGameObjects.Count} ownedMeshes={_ownedTerrainChunkMeshes.Count} " +
                $"spares={_spareTerrainMeshes.Count} inactiveKeys={_inactiveTerrainChunks.Count} limit={Mathf.Max(0, inactiveTerrainChunkLimit)} " +
                $"evicted={_chunkCacheEvictedSinceLog} evictionTotalMs={_chunkCacheEvictionMsSinceLog:F3}");
            _chunkCacheEvictedSinceLog = 0;
            _chunkCacheEvictionMsSinceLog = 0;
        }
    }
}
