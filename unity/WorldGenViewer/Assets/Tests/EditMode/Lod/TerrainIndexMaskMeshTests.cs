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
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void CommitFailureRestoresFullStaticIndicesAndCleansUpEvenWhenRecoveryFails(bool missingMesh, bool missingWaterMesh)
        {
            var parent = new GameObject("FailedTerrainCommitTest");
            parent.SetActive(false);
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            var waterMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            try
            {
                var viewer = Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", true);
                var owner = parent.AddComponent(viewer);
                var vertices = Enumerable.Range(0, 24).Select(i => new Vector3(i, i % 3, 1)).ToArray();
                int[] original = Enumerable.Range(0, 6)
                    .SelectMany(i => new[] { i * 4, i * 4 + 1, i * 4 + 2, i * 4, i * 4 + 2, i * 4 + 3 }).ToArray();
                mesh.vertices = vertices;
                mesh.subMeshCount = 2;
                mesh.SetTriangles(original.Take(18).ToArray(), 0);
                mesh.SetTriangles(original.Skip(18).ToArray(), 1);
                var bounds = mesh.bounds;
                var mask = new Lod.TerrainIndexMask(0, Enumerable.Range(0, 6).Select(i => i * 6).ToArray(), original);
                foreach (var range in mask.SetHidden(new[] { TileId.FromFaceLevelUV(0, 0, 0, 0) }))
                    mesh.SetIndexBufferData(mask.Indices, range.Start, range.Start, range.Count, MeshUpdateFlags.DontRecalculateBounds);
                var oldSnapshot = mask.CopyHiddenQuadIndices();
                // A natív restore előtti kivétel állapota: CPU már visszaállt, GPU még nem.
                mask.ApplyPrepared(mask.PrepareHidden(Array.Empty<TileId>()));
                Assert.That(mesh.triangles, Is.Not.EqualTo(original));
                viewer.GetField("_terrainIndexMask", flags).SetValue(owner, mask);
                viewer.GetField("_drawnHiddenStaticQuads", flags).SetValue(owner, oldSnapshot);
                waterMesh.vertices = vertices;
                waterMesh.triangles = original;
                var waterMask = new Lod.TerrainIndexMask(0, Enumerable.Range(0, 6).Select(i => i * 6).ToArray(), original);
                foreach (var range in waterMask.SetHidden(new[] { TileId.FromFaceLevelUV(3, 0, 0, 0) }))
                    waterMesh.SetIndexBufferData(waterMask.Indices, range.Start, range.Start, range.Count, MeshUpdateFlags.DontRecalculateBounds);
                var oldWaterSnapshot = waterMask.CopyHiddenQuadIndices();
                waterMask.ApplyPrepared(waterMask.PrepareHidden(Array.Empty<TileId>()));
                viewer.GetField("_waterIndexMask", flags).SetValue(owner, waterMask);
                viewer.GetField("_staticWaterMesh", flags).SetValue(owner, missingWaterMesh ? null : waterMesh);
                viewer.GetField("_drawnHiddenStaticWaterQuads", flags).SetValue(owner, oldWaterSnapshot);
                viewer.GetField("_hasLastCutCameraPosition", flags).SetValue(owner, true);
                parent.GetComponent<MeshFilter>().sharedMesh = missingMesh ? null : mesh;
                var chunk = (GameObject)viewer.GetMethod("GetOrCreateChunkRenderTarget", flags)
                    .Invoke(owner, new object[] { TileId.FromFaceLevelUV(0, 8, 0, 0), true });
                foreach (string name in new[] { "DynamicRefined", "DynamicWater", "DynamicBorders", "IndependentWater0", "IndependentWater1" })
                    new GameObject(name).transform.SetParent(parent.transform, false);

                // Hibás commit-bemenet ugyanabba a valódi catch ágba lép; nincs runtime hibainjektáló kapcsoló.
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                    viewer.GetMethod("ApplyAdaptiveMeshBuffers", flags).Invoke(owner, new object[] { null! }));
                if (missingMesh || missingWaterMesh)
                {
                    Assert.That(error!.InnerException, Is.TypeOf<AggregateException>());
                    Assert.That(((AggregateException)error.InnerException!).InnerExceptions.Count,
                        Is.EqualTo(1 + (missingMesh ? 1 : 0) + (missingWaterMesh ? 1 : 0)));
                }
                else Assert.That(error!.InnerException, Is.TypeOf<NullReferenceException>());
                if (missingMesh)
                {
                    Assert.That(viewer.GetField("_drawnHiddenStaticQuads", flags).GetValue(owner), Is.SameAs(oldSnapshot));
                }
                else
                {
                    Assert.That(mesh.triangles, Is.EqualTo(original));
                    Assert.That(mesh.GetSubMesh(1).indexStart, Is.EqualTo(18));
                    Assert.That(mesh.vertices, Is.EqualTo(vertices));
                    Assert.That(mesh.bounds, Is.EqualTo(bounds));
                    Assert.That(viewer.GetField("_drawnHiddenStaticQuads", flags).GetValue(owner), Is.Empty);
                }
                if (missingWaterMesh)
                    Assert.That(viewer.GetField("_drawnHiddenStaticWaterQuads", flags).GetValue(owner), Is.SameAs(oldWaterSnapshot));
                else
                {
                    Assert.That(waterMesh.triangles, Is.EqualTo(original));
                    Assert.That(viewer.GetField("_drawnHiddenStaticWaterQuads", flags).GetValue(owner), Is.Empty);
                }
                Assert.That(chunk.activeSelf, Is.False);
                foreach (string name in new[] { "DynamicRefined", "DynamicWater", "DynamicBorders", "IndependentWater0", "IndependentWater1" })
                    Assert.That(parent.transform.Find(name).gameObject.activeSelf, Is.False);
                Assert.That(viewer.GetField("_hasLastCutCameraPosition", flags).GetValue(owner), Is.False);
                Assert.That(mask.Indices, Is.EqualTo(original));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(waterMesh);
            }
        }

        [TestCase(1f, true)]
        [TestCase(2f, true)]
        [TestCase(1f, false)]
        public void CameraTransformUsesLocalSurfaceCacheAndNearClipClearance(float scale, bool follow)
        {
            var planetObject = new GameObject("CameraSurfaceTestPlanet");
            var cameraObject = new GameObject("CameraSurfaceTestCamera");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            try
            {
                var planetType = Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", true);
                var cameraType = Type.GetType("WorldGen.Viewer.PlanetOrbitCamera, Assembly-CSharp", true);
                var planet = planetObject.AddComponent(planetType);
                cameraObject.AddComponent<Camera>().nearClipPlane = 0.3f;
                var orbit = cameraObject.AddComponent(cameraType);
                planetObject.transform.position = new Vector3(17, 23, -9);
                planetObject.transform.rotation = Quaternion.Euler(15, 35, 5);
                planetObject.transform.localScale = Vector3.one * scale;
                cameraType.GetField("target", flags).SetValue(orbit, planetObject.transform);
                cameraType.GetField("distance", flags).SetValue(orbit, 100.1f);
                cameraType.GetField("followLocalSurface", flags).SetValue(orbit, follow);
                planetType.GetMethod("SnapshotWorldConfig", flags).Invoke(planet, null);
                // Előállított cache-fixture: nem drága világépítést, hanem a valódi
                // kamera -> cache -> transzformáció bekötést ellenőrizzük.
                planetType.GetField("_cachedCameraSurfaceRevision", flags).SetValue(planet,
                    planetType.GetField("_cameraSurfaceRevision", flags).GetValue(planet));
                planetType.GetField("_cachedCameraSurfaceDirection", flags).SetValue(planet,
                    planetObject.transform.InverseTransformDirection(Vector3.back));
                planetType.GetField("_cachedCameraSurfaceRadius", flags).SetValue(planet, 105.0);
                cameraType.GetMethod("ApplyTransform", flags).Invoke(orbit, null);
                float expected = follow ? 105f * scale + 0.33f : 100.1f;
                Assert.That(Vector3.Distance(cameraObject.transform.position, planetObject.transform.position),
                    Is.EqualTo(expected).Within(0.0001f));
                Assert.That((float)cameraType.GetProperty("AltitudeAboveSurface").GetValue(orbit),
                    Is.EqualTo(follow ? 0.33f : 0.1f).Within(0.0001f));
                // A zoom/Apply ismétlése nem mozdítja tovább kifelé a kamerát.
                cameraType.GetMethod("ApplyTransform", flags).Invoke(orbit, null);
                Assert.That((float)cameraType.GetField("distance", flags).GetValue(orbit),
                    Is.EqualTo(expected).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(planetObject);
            }
        }

        [Test]
        public void PreparedMaskLeavesLiveSubmeshesUnchangedUntilCommitAndRestoresThem()
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                var vertices = Enumerable.Range(0, 24).Select(i => new Vector3(i, i % 3, 1)).ToArray();
                int[] original = Enumerable.Range(0, 6)
                    .SelectMany(i => new[] { i * 4, i * 4 + 1, i * 4 + 2, i * 4, i * 4 + 2, i * 4 + 3 }).ToArray();
                mesh.vertices = vertices;
                mesh.subMeshCount = 2;
                mesh.SetTriangles(original.Take(18).ToArray(), 0);
                mesh.SetTriangles(original.Skip(18).ToArray(), 1);
                Assert.That(mesh.GetSubMesh(1).indexStart, Is.EqualTo(18));
                var bounds = mesh.bounds;
                var mask = new Lod.TerrainIndexMask(0, Enumerable.Range(0, 6).Select(i => i * 6).ToArray(), original);
                var plan = mask.PrepareHidden(new[] { TileId.FromFaceLevelUV(0, 0, 0, 0), TileId.FromFaceLevelUV(4, 0, 0, 0) });
                Assert.That(mask.Indices, Is.EqualTo(original));
                Assert.That(mesh.triangles, Is.EqualTo(original));
                Assert.That(mask.CopyHiddenQuadIndices(), Is.Empty);
                foreach (var range in mask.ApplyPrepared(plan))
                    mesh.SetIndexBufferData(mask.Indices, range.Start, range.Start, range.Count, MeshUpdateFlags.DontRecalculateBounds);
                Assert.That(mesh.GetTriangles(0), Is.EqualTo(mask.Indices.Take(18).ToArray()));
                Assert.That(mesh.GetTriangles(1), Is.EqualTo(mask.Indices.Skip(18).ToArray()));
                Assert.That(mesh.vertices, Is.EqualTo(vertices));
                Assert.That(mesh.bounds, Is.EqualTo(bounds));
                Assert.Throws<InvalidOperationException>(() => mask.ApplyPrepared(plan));
                foreach (var range in mask.ApplyPrepared(mask.PrepareHidden(Array.Empty<TileId>())))
                    mesh.SetIndexBufferData(mask.Indices, range.Start, range.Start, range.Count, MeshUpdateFlags.DontRecalculateBounds);
                Assert.That(mesh.triangles, Is.EqualTo(original));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PreparedTerrainTargetsStayUnpublishedUntilCommitAndCancellationPreservesExisting(bool existing, bool commit)
        {
            // A tényleges viewer stage/publish/discard út; inaktív szülőn nem indul Build.
            var parent = new GameObject("PreparedTerrainTest");
            parent.SetActive(false);
            Mesh? previous = null, uploaded = null;
            try
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var viewer = Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", true);
                var owner = parent.AddComponent(viewer);
                var key = TileId.FromFaceLevelUV(0, 8, 0, 0);
                var cache = (System.Collections.IDictionary)viewer.GetField("_dynamicChunkGameObjects", flags).GetValue(owner);
                var diagnostics = (System.Collections.IDictionary)viewer.GetField("_drawnDiagnosticMeshes", flags).GetValue(owner);
                if (existing)
                {
                    var old = (GameObject)viewer.GetMethod("GetOrCreateChunkRenderTarget", flags)
                        .Invoke(owner, new object[] { key, true });
                    previous = new Mesh();
                    previous.vertices = new[] { Vector3.right };
                    old.GetComponent<MeshFilter>().sharedMesh = previous;
                }
                var bufferType = viewer.GetNestedType("AdaptiveMeshBuffers", System.Reflection.BindingFlags.NonPublic);
                object buffer = Activator.CreateInstance(bufferType, true);
                var stagedField = bufferType.GetField("StagedTerrain");
                stagedField.SetValue(buffer, Activator.CreateInstance(stagedField.FieldType));
                var dataType = viewer.GetNestedType("ConcatenatedMesh", System.Reflection.BindingFlags.NonPublic);
                object data = Activator.CreateInstance(dataType, true);
                var pairType = typeof(System.Collections.Generic.KeyValuePair<,>).MakeGenericType(typeof(TileId), dataType);
                object pair = Activator.CreateInstance(pairType, key, data);
                object prepared = viewer.GetMethod("StageTerrainMesh", flags).Invoke(owner, new[] { buffer, pair });
                Assert.That(cache.Count, Is.EqualTo(existing ? 1 : 0));
                Assert.That(diagnostics.Count, Is.Zero);
                viewer.GetMethod("StageTerrainTarget", flags).Invoke(owner, new[] { buffer, (object)key, prepared });
                var target = (GameObject)cache[key];
                var staged = (System.Collections.IDictionary)stagedField.GetValue(buffer);
                uploaded = (Mesh)staged[key].GetType().GetField("Mesh").GetValue(staged[key]);
                Assert.That(target.activeSelf, Is.EqualTo(existing));
                Assert.That(target.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(previous));
                Assert.That(diagnostics.Count, Is.Zero);
                Assert.That(((System.Collections.IDictionary)bufferType.GetField("NewTerrainTargets").GetValue(buffer)).Count,
                    Is.EqualTo(existing ? 0 : 1));
                if (commit)
                {
                    viewer.GetMethod("PublishStagedTerrain", flags).Invoke(owner, new object[] { buffer, key });
                    Assert.That(target.activeSelf, Is.True);
                    Assert.That(target.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(uploaded));
                    Assert.That(diagnostics.Contains(target), Is.True);
                }
                else
                {
                    viewer.GetMethod("DiscardPreparedTerrainTargets", flags).Invoke(owner, new[] { buffer });
                    Assert.That(cache.Contains(key), Is.EqualTo(existing));
                    Assert.That(diagnostics.Count, Is.Zero);
                    if (existing)
                    {
                        Assert.That(target.activeSelf, Is.True);
                        Assert.That(target.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(previous));
                        Assert.That(previous!.vertices[0], Is.EqualTo(Vector3.right));
                    }
                    else Assert.That(target == null, Is.True);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
                if (uploaded != null) UnityEngine.Object.DestroyImmediate(uploaded);
            }
        }

        [Test]
        public void WorkerWaterPackingPreservesBucketOrderAttributesAndSourceIndices()
        {
            // Reflection: az asmdef teszt nem hivatkozhat a predefined Assembly-CSharpra.
            // A tényleges viewer packert futtatjuk, nem annak tesztbeli másolatát.
            var viewer = Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", throwOnError: true);
            var bufferType = viewer.GetNestedType("AdaptiveMeshBuffers", System.Reflection.BindingFlags.NonPublic);
            object buffer = Activator.CreateInstance(bufferType, nonPublic: true);
            var a = new[] { Vector3.zero, Vector3.right, Vector3.right + Vector3.up, Vector3.up }.ToList();
            var b = a.Select(p => p + Vector3.forward * 2).ToList();
            var vertices = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Vector3>>
            { [5] = b, [2] = new(), [0] = a };
            var normals = vertices.ToDictionary(kv => kv.Key, kv => kv.Value.Select(_ => Vector3.forward).ToList());
            var colors = vertices.ToDictionary(kv => kv.Key, kv => kv.Value.Select(_ => kv.Key == 0 ? Color.red : Color.blue).ToList());
            var indices = vertices.ToDictionary(kv => kv.Key,
                kv => (kv.Value.Count == 0 ? Array.Empty<int>() : new[] { 0, 1, 2, 0, 2, 3 }).ToList());
            bufferType.GetField("WaterVertices").SetValue(buffer, vertices);
            bufferType.GetField("WaterNormals").SetValue(buffer, normals);
            bufferType.GetField("WaterColors").SetValue(buffer, colors);
            bufferType.GetField("WaterTriangles").SetValue(buffer, indices);
            object packed = viewer.GetMethod("PackWaterMesh", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new[] { buffer, (object)System.Threading.CancellationToken.None });
            object Field(string name) => packed.GetType().GetField(name).GetValue(packed);
            Assert.That(Field("Buckets"), Is.EqualTo(new[] { 0, 5 }));
            Assert.That(Field("Vertices"), Is.EqualTo(a.Concat(b).ToArray()));
            Assert.That(Field("Normals"), Is.EqualTo(Enumerable.Repeat(Vector3.forward, 8).ToArray()));
            Assert.That(Field("Colors"), Is.EqualTo(Enumerable.Repeat(Color.red, 4).Concat(Enumerable.Repeat(Color.blue, 4)).ToArray()));
            var triangles = (System.Collections.Generic.List<int[]>)Field("Triangles");
            Assert.That(triangles[0], Is.EqualTo(new[] { 0, 1, 2, 0, 2, 3 }));
            Assert.That(triangles[1], Is.EqualTo(new[] { 4, 5, 6, 4, 6, 7 }));
            Assert.That(indices[5], Is.EqualTo(new[] { 0, 1, 2, 0, 2, 3 }));
            Assert.That(Field("Bounds"), Is.EqualTo(new Bounds(new Vector3(.5f, .5f, 1), new Vector3(1, 1, 2))));
            a[0] = Vector3.one * 99;
            Assert.That(((System.Collections.Generic.List<Vector3>)Field("Vertices"))[0], Is.EqualTo(Vector3.zero));
        }

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

        [Test]
        public void SparseWaterMaskPreservesMissingRootsAndRestoresUploadedSubmeshes()
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                var vertices = Enumerable.Range(0, 8).Select(i => new Vector3(i, i % 3, 1)).ToArray();
                mesh.vertices = vertices;
                int[] first = { 0, 1, 2, 0, 2, 3 }, second = { 4, 5, 6, 4, 6, 7 };
                mesh.subMeshCount = 2;
                mesh.SetTriangles(first, 0); mesh.SetTriangles(second, 1);
                int start = mesh.GetSubMesh(1).indexStart;
                var indices = new int[start + 6];
                Array.Copy(first, indices, 6); Array.Copy(second, 0, indices, start, 6);
                var mask = new Lod.TerrainIndexMask(0, new[] { start, -1, -1, 0, -1, -1 }, indices, true);
                foreach (var range in mask.SetHidden(new[] { TileId.FromFaceLevelUV(0, 0, 0, 0) }))
                    mesh.SetIndexBufferData(mask.Indices, range.Start, range.Start, range.Count, MeshUpdateFlags.DontRecalculateBounds);
                Assert.That(mesh.GetTriangles(0), Is.EqualTo(first));
                Assert.That(mesh.GetTriangles(1), Is.EqualTo(Enumerable.Repeat(4, 6).ToArray()));
                Assert.That(mask.CopyHiddenQuadIndices(), Is.EquivalentTo(new[] { 1 }));
                Assert.Throws<ArgumentException>(() => mask.SetHidden(new[] { TileId.FromFaceLevelUV(1, 0, 0, 0) }));
                foreach (var range in mask.SetHidden(Array.Empty<TileId>()))
                    mesh.SetIndexBufferData(mask.Indices, range.Start, range.Start, range.Count, MeshUpdateFlags.DontRecalculateBounds);
                Assert.That(mesh.GetTriangles(0), Is.EqualTo(first));
                Assert.That(mesh.GetTriangles(1), Is.EqualTo(second));
                Assert.That(mesh.vertices, Is.EqualTo(vertices));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void StagedMeshesDoNotChangeLiveFiltersUntilWholeBatchCommits()
        {
            var target = new GameObject("UploadTest");
            var previous = new Mesh();
            var next = new Mesh();
            var other = new Mesh();
            try
            {
                MeshFilter filter = target.AddComponent<MeshFilter>();
                previous.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                previous.triangles = new[] { 0, 1, 2 };
                filter.sharedMesh = previous;
                var batch = new Lod.LodUploadBatch<Mesh>(new[] { next, other });
                void Stage(Mesh mesh)
                {
                    mesh.vertices = new[] { Vector3.zero, Vector3.right * 2, Vector3.up * 2 };
                    mesh.triangles = new[] { 0, 1, 2 };
                }
                batch.StageSlice(Stage, () => 0, 2, 1);
                Assert.That(filter.sharedMesh, Is.SameAs(previous));
                Assert.That(filter.sharedMesh.vertices[1], Is.EqualTo(Vector3.right));
                Assert.Throws<InvalidOperationException>(() => batch.Commit(() => filter.sharedMesh = next));
                batch.StageSlice(Stage, () => 0, 2, 1);
                Assert.That(filter.sharedMesh, Is.SameAs(previous));
                batch.Commit(() => filter.sharedMesh = next);
                Assert.That(filter.sharedMesh, Is.SameAs(next));
                Assert.That(filter.sharedMesh.vertices[1], Is.EqualTo(Vector3.right * 2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(previous);
                UnityEngine.Object.DestroyImmediate(next);
                UnityEngine.Object.DestroyImmediate(other);
            }
        }
    }
}
