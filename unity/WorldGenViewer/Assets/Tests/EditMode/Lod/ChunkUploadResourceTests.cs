using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod.Tests
{
    /// <summary>ND-93/94: valódi Unity-erőforrások; futtatás az Editor Test Runnerben.</summary>
    public class ChunkUploadResourceTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static TileId Key(int i) => TileId.FromFaceLevelUV(0, 8, (uint)i, 0);
        private static Type Viewer => Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", true);
        private static object Field(object owner, string name) => Viewer.GetField(name, Private).GetValue(owner);
        private static void Set(object owner, string name, object value) => Viewer.GetField(name, Private).SetValue(owner, value);
        private static object Call(object owner, string name, params object[] args) => Viewer.GetMethod(name, Private).Invoke(owner, args);
        private static object CreateOwner(GameObject parent)
        {
            parent.SetActive(false);
            object owner = parent.AddComponent(Viewer);
            Set(owner, "inactiveTerrainChunkLimit", 0);
            Set(owner, "_nextChunkCacheLogTime", float.MaxValue);
            return owner;
        }

        private static GameObject Target(object owner, int id, bool active, Mesh mesh, bool owned)
        {
            var target = (GameObject)Call(owner, "GetOrCreateChunkRenderTarget", Key(id), active);
            target.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (owned) ((HashSet<Mesh>)Field(owner, "_ownedTerrainChunkMeshes")).Add(mesh);
            if (!active) Call(owner, "RememberInactiveTerrainChunk", Key(id));
            return target;
        }

        [Test]
        public void EvictionReleasesOwnedMeshesAndDiagnosticsButPreservesActiveAndForeignMeshes()
        {
            var parent = new GameObject("ChunkEvictionTest");
            var activeMesh = new Mesh(); var idleMesh = new Mesh(); var spare = new Mesh(); var foreign = new Mesh();
            try
            {
                object owner = CreateOwner(parent);
                var active = Target(owner, 0, true, activeMesh, true);
                var idle = Target(owner, 1, false, idleMesh, true);
                Target(owner, 2, false, foreign, false);
                ((IDictionary)Field(owner, "_spareTerrainMeshes")).Add(Key(1), spare);
                ((HashSet<Mesh>)Field(owner, "_ownedTerrainChunkMeshes")).Add(spare);
                var diagnostics = (IDictionary)Field(owner, "_drawnDiagnosticMeshes");
                diagnostics.Add(idle, null); // A referencia eltávolítását vizsgáljuk, nem a rajzméretet.
                for (int i = 0; i < 3; i++) Call(owner, "TickUnusedTerrainChunks");
                Assert.That(active != null && active.activeSelf, Is.True);
                Assert.That(active!.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(activeMesh));
                Assert.That(idle == null && idleMesh == null && spare == null, Is.True);
                Assert.That(foreign != null, Is.True);
                Assert.That(((IDictionary)Field(owner, "_dynamicChunkGameObjects")).Count, Is.EqualTo(1));
                Assert.That(diagnostics.Count, Is.Zero);
                Assert.That(((HashSet<Mesh>)Field(owner, "_ownedTerrainChunkMeshes")).Count, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                foreach (Mesh mesh in new[] { activeMesh, idleMesh, spare, foreign })
                    if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void WorldClearReleasesOrphansAndSharedOwnedSpareExactlyOnceWithoutDeletingForeignMesh()
        {
            var parent = new GameObject("ChunkClearTest");
            var owned = new Mesh(); var orphan = new Mesh(); var foreign = new Mesh();
            try
            {
                object owner = CreateOwner(parent);
                Target(owner, 0, true, owned, true);
                Target(owner, 1, true, foreign, false);
                var lostTarget = Target(owner, 2, false, orphan, true);
                UnityEngine.Object.DestroyImmediate(lostTarget);
                // Félbemaradt swap szélső esete: ugyanaz a saját mesh két hivatkozásban.
                ((IDictionary)Field(owner, "_spareTerrainMeshes")).Add(Key(0), owned);
                Call(owner, "ClearAllDynamicChunks");
                Assert.That(owned == null && orphan == null, Is.True);
                Assert.That(foreign != null, Is.True);
                Assert.That(((IDictionary)Field(owner, "_dynamicChunkGameObjects")).Count, Is.Zero);
                Assert.That(((IDictionary)Field(owner, "_spareTerrainMeshes")).Count, Is.Zero);
                Assert.That(((HashSet<Mesh>)Field(owner, "_ownedTerrainChunkMeshes")).Count, Is.Zero);
                Assert.That(((InactiveChunkQueue)Field(owner, "_inactiveTerrainChunks")).Count, Is.Zero);
                Call(owner, "ClearAllDynamicChunks");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                foreach (Mesh mesh in new[] { owned, orphan, foreign })
                    if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PublishedChunkIsProtectedAgainstStaleInactiveEntry()
        {
            var parent = new GameObject("ProtectedChunkTest");
            var mesh = new Mesh();
            try
            {
                object owner = CreateOwner(parent);
                var target = Target(owner, 0, false, mesh, true);
                ((IDictionary)Field(owner, "_previousChunkGroups")).Add(Key(0), new HashSet<TileId>());
                Call(owner, "TickUnusedTerrainChunks");
                Assert.That(target != null && mesh != null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void RunStage<T>(T stage) => ((Action)typeof(T).GetField("Run").GetValue(stage))();

        [TestCase(false)] [TestCase(true)]
        public void LegacyUploadOwnsOnlyItsNewMeshEvenWhenUploadFails(bool fail)
        {
            var parent = new GameObject("LegacyChunkOwnershipTest");
            var foreign = new Mesh { vertices = new[] { Vector3.right } };
            Mesh? generated = null;
            try
            {
                object owner = CreateOwner(parent);
                var target = Target(owner, 0, true, foreign, false);
                object data = fail ? null! : Activator.CreateInstance(Viewer.GetNestedType("ConcatenatedMesh", BindingFlags.NonPublic), true);
                if (fail) Assert.Throws<TargetInvocationException>(() =>
                    Call(owner, "UploadConcatenatedMultiMaterialMesh", target, data, true));
                else Call(owner, "UploadConcatenatedMultiMaterialMesh", target, data, true);
                generated = target.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(generated, Is.Not.SameAs(foreign));
                Assert.That(((HashSet<Mesh>)Field(owner, "_ownedTerrainChunkMeshes")).Contains(generated), Is.True);
                Assert.That(foreign.vertices, Is.EqualTo(new[] { Vector3.right }));
                Call(owner, "ClearAllDynamicChunks");
                Assert.That(generated == null, Is.True);
                Assert.That(foreign != null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                if (generated != null) UnityEngine.Object.DestroyImmediate(generated);
                if (foreign != null) UnityEngine.Object.DestroyImmediate(foreign);
            }
        }

        [TestCase(1, false)] [TestCase(2, false)]
        [TestCase(1, true)] [TestCase(2, true)]
        public void ActualPendingQueueCanCancelBetweenTerrainPhasesWithoutChangingPublishedMesh(int completed, bool existing)
        {
            var parent = new GameObject("PartialTerrainStageTest");
            var previous = new Mesh(); var unrelated = new Mesh();
            try
            {
                object owner = CreateOwner(parent);
                GameObject? old = existing ? Target(owner, 0, true, previous, false) : null;
                Target(owner, 1, false, unrelated, true);
                var bufferType = Viewer.GetNestedType("AdaptiveMeshBuffers", BindingFlags.NonPublic);
                object buffers = Activator.CreateInstance(bufferType, true);
                var changedField = bufferType.GetField("ChangedChunkTerrain");
                var changed = (IDictionary)Activator.CreateInstance(changedField.FieldType);
                changed.Add(Key(0), Activator.CreateInstance(Viewer.GetNestedType("ConcatenatedMesh", BindingFlags.NonPublic), true));
                changedField.SetValue(buffers, changed);
                var pendingType = Viewer.GetNestedType("PendingTerrainUpload", BindingFlags.NonPublic);
                object pending = Activator.CreateInstance(pendingType, owner, buffers, 0);
                Set(owner, "_pendingTerrainUpload", pending);
                object batch = pendingType.GetField("Batch").GetValue(pending);
                Type stageType = batch.GetType().GetGenericArguments()[0];
                var run = typeof(ChunkUploadResourceTests).GetMethod("RunStage", BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(stageType).CreateDelegate(typeof(Action<>).MakeGenericType(stageType));
                var step = batch.GetType().GetMethod("StageSlice");
                for (int i = 0; i < completed; i++)
                    Assert.That(step.Invoke(batch, new object[] { run, (Func<double>)(() => 0), 2.0, 1 }), Is.EqualTo(1));
                var cache = (IDictionary)Field(owner, "_dynamicChunkGameObjects");
                Assert.That(cache.Contains(Key(0)), Is.EqualTo(existing || completed == 2));
                Assert.That(((IDictionary)bufferType.GetField("StagedTerrain").GetValue(buffers)).Count,
                    Is.EqualTo(completed == 2 ? 1 : 0));
                Assert.That(((IDictionary)Field(owner, "_drawnDiagnosticMeshes")).Count, Is.Zero);
                Call(owner, "TickUnusedTerrainChunks");
                Assert.That(unrelated != null, Is.True); // Staging alatt még a másik inaktív kulcs is védett.
                Call(owner, "CancelStagedTerrainUpload");
                for (int i = 0; i < 3; i++) Call(owner, "TickUnusedTerrainChunks");
                Assert.That(unrelated == null, Is.True);
                Assert.That(cache.Contains(Key(0)), Is.EqualTo(existing));
                if (existing)
                {
                    Assert.That(old!.activeSelf, Is.True);
                    Assert.That(old.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(previous));
                }
                else Assert.That(((HashSet<Mesh>)Field(owner, "_ownedTerrainChunkMeshes")).Count, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
                if (unrelated != null) UnityEngine.Object.DestroyImmediate(unrelated);
            }
        }
    }
}
