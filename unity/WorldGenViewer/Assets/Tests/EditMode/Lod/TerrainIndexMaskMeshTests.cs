using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod.Tests
{
    /// <summary>Valódi Unity Mesh API-kapu. Csak Editor Test Runnerben fut.</summary>
    public class TerrainIndexMaskMeshTests
    {
        [Test]
        public void PartialMaskRestoresBothSubmeshesWithoutChangingVertices()
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                var vertices = Enumerable.Range(0, 24).Select(i => new Vector3(i, i % 3, 1)).ToArray();
                mesh.vertices = vertices;
                int[] indices = Enumerable.Range(0, 6).SelectMany(i => new[] { i * 4, i * 4 + 1, i * 4 + 2, i * 4, i * 4 + 2, i * 4 + 3 }).ToArray();
                mesh.subMeshCount = 2;
                mesh.SetTriangles(indices.Take(18).ToArray(), 0);
                mesh.SetTriangles(indices.Skip(18).ToArray(), 1);
                Assert.That(mesh.GetSubMesh(1).indexStart, Is.EqualTo(18));
                Bounds bounds = mesh.bounds;
                var mask = new Lod.TerrainIndexMask(0, Enumerable.Range(0, 6).Select(i => i * 6).ToArray(), indices);
                TileId[] roots = { TileId.FromFaceLevelUV(0, 0, 0, 0), TileId.FromFaceLevelUV(4, 0, 0, 0) };
                foreach (var range in mask.SetHidden(roots))
                    mesh.SetIndexBufferData(mask.Indices, range.Start, range.Start, range.Count, MeshUpdateFlags.DontRecalculateBounds);
                Assert.That(mesh.GetTriangles(0), Is.EqualTo(mask.Indices.Take(18).ToArray()));
                Assert.That(mesh.GetTriangles(1), Is.EqualTo(mask.Indices.Skip(18).ToArray()));
                Assert.That(mesh.vertices, Is.EqualTo(vertices));
                Assert.That(mesh.bounds, Is.EqualTo(bounds));
                foreach (var range in mask.SetHidden(Array.Empty<TileId>()))
                    mesh.SetIndexBufferData(mask.Indices, range.Start, range.Start, range.Count, MeshUpdateFlags.DontRecalculateBounds);
                Assert.That(mesh.triangles, Is.EqualTo(indices));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void PositionOnlyUpdateKeepsIndicesNormalsAndColors()
        {
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                var normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
                var colors = new[] { Color.red, Color.green, Color.blue };
                mesh.normals = normals; mesh.colors = colors;
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.SetVertices(new[] { Vector3.zero, Vector3.right * 2, Vector3.up }, 0, 3, MeshUpdateFlags.DontRecalculateBounds);
                Assert.That(mesh.triangles, Is.EqualTo(new[] { 0, 1, 2 }));
                Assert.That(mesh.normals, Is.EqualTo(normals));
                Assert.That(mesh.colors, Is.EqualTo(colors));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
