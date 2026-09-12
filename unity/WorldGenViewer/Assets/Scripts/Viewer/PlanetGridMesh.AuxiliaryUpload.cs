using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        private sealed class WaterMeshData
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Color> Colors = new();
            public readonly List<int[]> Triangles = new();
            public readonly List<int> Buckets = new();
            public Bounds Bounds;
        }

        private sealed class StagedAuxiliaryMesh
        {
            public Mesh? Mesh;
            public Material[] Materials = Array.Empty<Material>();
            public WaterMeshData? Water;
        }

        private readonly Dictionary<string, Mesh> _spareAuxiliaryMeshes = new();
        private Material? _uploadBorderMaterial;

        // A meglévő BuildWaterSurface azonos bucket-sorrendje és indexeltolása;
        // itt csak managed adat keletkezik, worker-szálon is használható.
        private static WaterMeshData PackWaterMesh(AdaptiveMeshBuffers source, CancellationToken cancellation)
        {
            var data = new WaterMeshData();
            var keys = new List<int>(source.WaterVertices.Keys);
            keys.Sort();
            foreach (int key in keys)
            {
                cancellation.ThrowIfCancellationRequested();
                List<Vector3> vertices = source.WaterVertices[key];
                if (vertices.Count == 0) continue;
                int offset = data.Vertices.Count;
                data.Vertices.AddRange(vertices);
                data.Normals.AddRange(source.WaterNormals[key]);
                data.Colors.AddRange(source.WaterColors[key]);
                int[] indices = source.WaterTriangles[key].ToArray();
                for (int i = 0; i < indices.Length; i++) indices[i] += offset;
                data.Triangles.Add(indices);
                data.Buckets.Add(key);
            }
            data.Bounds = CalculateUploadBounds(data.Vertices);
            cancellation.ThrowIfCancellationRequested();
            return data;
        }

        private static Bounds CalculateUploadBounds(List<Vector3> vertices)
        {
            if (vertices.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            Vector3 min = vertices[0], max = min;
            foreach (Vector3 p in vertices) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            return new Bounds((min + max) * .5f, max - min);
        }

        private void PrepareAuxiliaryUploads(AdaptiveMeshBuffers buffers, CancellationToken cancellation)
        {
            if (!useStagedTerrainUpload || _stagedUploadDisabledAfterError || !useAsyncMeshRebuild || !useChunkedDynamicMesh)
                return;
            var timer = Stopwatch.StartNew();
            buffers.PreparedLegacyWater = PackWaterMesh(buffers, cancellation);
            if (buffers.IndependentWaterGeometry != null)
                buffers.PreparedIndependentWater = PackWaterMesh(buffers.IndependentWaterGeometry, cancellation);
            buffers.PreparedBordersEnabled = showBorders;
            buffers.PreparedBorderBounds = CalculateUploadBounds(buffers.BorderVerts);
            buffers.AuxiliaryPackMs = timer.Elapsed.TotalMilliseconds;
        }

        private Mesh AuxiliarySpare(string key)
        {
            if (!_spareAuxiliaryMeshes.TryGetValue(key, out Mesh mesh) || mesh == null)
                _spareAuxiliaryMeshes[key] = mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "LodUpload_" + key };
            return mesh;
        }

        private StagedAuxiliaryMesh StageWaterUpload(string key, WaterMeshData data)
        {
            var staged = new StagedAuxiliaryMesh { Water = data };
            if (data.Vertices.Count == 0) return staged;
            Mesh mesh = AuxiliarySpare(key);
            mesh.Clear();
            mesh.SetVertices(data.Vertices); mesh.SetNormals(data.Normals); mesh.SetColors(data.Colors);
            mesh.subMeshCount = data.Triangles.Count;
            for (int i = 0; i < data.Triangles.Count; i++) mesh.SetTriangles(data.Triangles[i], i, calculateBounds: false);
            mesh.bounds = data.Bounds;
            var materials = new Material[data.Buckets.Count];
            for (int i = 0; i < materials.Length; i++) materials[i] = GetOrCreateWaterMaterial(data.Buckets[i]);
            staged.Mesh = mesh; staged.Materials = materials;
            return staged;
        }

        private StagedAuxiliaryMesh StageBorderUpload(AdaptiveMeshBuffers buffers)
        {
            var staged = new StagedAuxiliaryMesh();
            if (!buffers.PreparedBordersEnabled || buffers.BorderIndices.Count == 0) return staged;
            Mesh mesh = AuxiliarySpare("Borders");
            mesh.Clear();
            mesh.SetVertices(buffers.BorderVerts);
            mesh.SetIndices(buffers.BorderIndices, MeshTopology.Lines, 0, calculateBounds: false);
            mesh.bounds = buffers.PreparedBorderBounds;
            if (borderMaterial == null && _uploadBorderMaterial == null)
                _uploadBorderMaterial = CreateFlatColorMaterial(Color.black);
            staged.Mesh = mesh;
            staged.Materials = new[] { borderMaterial != null ? borderMaterial : _uploadBorderMaterial! };
            return staged;
        }

        private static void MeasureAuxiliaryStage(AdaptiveMeshBuffers buffers, Action stage)
        {
            var timer = Stopwatch.StartNew();
            stage();
            buffers.AuxiliaryStageMs += timer.Elapsed.TotalMilliseconds;
            buffers.AuxiliaryStageJobs++;
        }

        private void PublishAuxiliaryUpload(string key, string childName, StagedAuxiliaryMesh staged)
        {
            if (staged.Mesh == null)
            {
                Transform old = transform.Find(childName);
                if (old != null) old.gameObject.SetActive(false);
                return;
            }
            GameObject target = GetOrCreateChildRenderTarget(childName);
            MeshFilter filter = target.GetComponent<MeshFilter>();
            Mesh oldMesh = filter.sharedMesh;
            filter.sharedMesh = staged.Mesh;
            if (oldMesh != null) _spareAuxiliaryMeshes[key] = oldMesh;
            else _spareAuxiliaryMeshes.Remove(key);
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = staged.Materials;
            if (staged.Water == null) renderer.shadowCastingMode = ShadowCastingMode.Off;
            else RememberDrawnSurface(target, staged.Water.Vertices, staged.Water.Triangles, 4);
            target.SetActive(true);
        }
    }
}
