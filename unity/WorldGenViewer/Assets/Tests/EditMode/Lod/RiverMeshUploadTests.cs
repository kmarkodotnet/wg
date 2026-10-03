#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
// ND-179: valódi Unity-geometria, atomikus csere és erőforrás-élettartam.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using WorldGen.Core.Hydrology;


public class RiverMeshUploadTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }

    [Test]
    public void CameraDirectionIsValidBeforeCameraStart()
    {
        var parent = new GameObject("RiverInitialCameraTest");
        parent.SetActive(false);
        try
        {
            var type = Type.GetType("WorldGen.Viewer.PlanetOrbitCamera, Assembly-CSharp", true);
            var camera = parent.AddComponent(type);
            type.GetField("initialYaw", F).SetValue(camera, 60f);
            type.GetField("initialPitch", F).SetValue(camera, 30f);
            var direction = type.GetProperty("CurrentViewDirection", F);
            Assert.AreEqual(Quaternion.Euler(30f, 60f, 0f) * Vector3.back, (Vector3)direction.GetValue(camera));
            type.GetField("_initialized", F).SetValue(camera, true);
            type.GetField("_yaw", F).SetValue(camera, -20f);
            type.GetField("_pitch", F).SetValue(camera, 10f);
            Assert.AreEqual(Quaternion.Euler(10f, -20f, 0f) * Vector3.back, (Vector3)direction.GetValue(camera));
        }
        finally { UnityEngine.Object.DestroyImmediate(parent); }
    }

    [Test]
    public void FineProgressPreservesOverviewAndIgnoresStaleGenerations()
    {
        var parent = new GameObject("RiverOverviewTest");
        parent.SetActive(false);
        try
        {
            var type = Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", true);
            var owner = parent.AddComponent(type);
            var overview = new List<RiverPathTracing.ContinuousRiverPath>();
            for (int i = 0; i < 96; i++)
                overview.Add(new RiverPathTracing.ContinuousRiverPath { SourceIndex = i });
            type.GetField("showRivers", F).SetValue(owner, true);
            type.GetField("_adaptiveRefinedRiverPaths", F).SetValue(owner, overview);
            var pendingType = type.GetNestedType("PendingRiverNetwork", F);
            var stateType = type.GetNestedType("RiverMeshBuildState", F);
            var staging = Activator.CreateInstance(stateType, true);
            type.GetField("_pendingRiverMesh", F).SetValue(owner, staging);
            var completion = new System.Threading.Tasks.TaskCompletionSource<List<RiverPathTracing.ContinuousRiverPath>>();
            type.GetField("_riverRefinementTask", F).SetValue(owner, completion.Task);
            var apply = type.GetMethod("TryApplyCompletedRiverRefinement", F);
            void Progress(int generation, int count)
            {
                var pending = Activator.CreateInstance(pendingType, true);
                pendingType.GetField("Generation", F).SetValue(pending, generation);
                pendingType.GetField("Paths", F).SetValue(pending, overview);
                pendingType.GetField("FineCompletedCount", F).SetValue(pending, count);
                type.GetField("_pendingRiverNetwork", F).SetValue(owner, pending);
                apply.Invoke(owner, null);
            }
            Progress(0, 4);
            Assert.AreSame(overview, type.GetField("_adaptiveRefinedRiverPaths", F).GetValue(owner));
            Assert.AreSame(staging, type.GetField("_pendingRiverMesh", F).GetValue(owner));
            Assert.AreEqual(4, type.GetField("_riverFineCompletedCount", F).GetValue(owner));
            Progress(-1, 16);
            Assert.AreEqual(4, type.GetField("_riverFineCompletedCount", F).GetValue(owner));
            Assert.AreSame(staging, type.GetField("_pendingRiverMesh", F).GetValue(owner));
            var fine = new List<RiverPathTracing.ContinuousRiverPath>(overview);
            completion.SetResult(fine);
            apply.Invoke(owner, null);
            Assert.AreSame(fine, type.GetField("_adaptiveRefinedRiverPaths", F).GetValue(owner));
            Assert.AreEqual(96, type.GetField("_riverFineCompletedCount", F).GetValue(owner));
            Assert.IsFalse((bool)type.GetField("_adaptiveRiverPathsArePreview", F).GetValue(owner));
        }
        finally { UnityEngine.Object.DestroyImmediate(parent); }
    }
    [TestCase(1)]
    [TestCase(20)]
    public void ChunkingPreservesTrianglesAndPublishesOnlyCompleteGenerations(int cancellationCount)
    {
        GameObject owner=null;
        try
        {
            owner=new GameObject("A8ChunkTest");
            var viewer=(Behaviour)owner.AddComponent(Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", true)); viewer.enabled=false;
            var type=Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp", true);
            var ribbon=type.GetMethod("AddRiverRibbon",F);
            var points=new List<Vector3>();
            for(int i=0;i<10000;i++) points.Add(new Vector3(i*0.001f,100f,(float)Math.Sin(i*0.01)*0.1f));
            List<Vector3> FullVerts=new List<Vector3>(),FullNormals=new List<Vector3>();
            List<int> FullTriangles=new List<int>();
            ribbon.Invoke(null,new object[]{FullVerts,FullNormals,FullTriangles,points,0.008f,0,-1});
            var chunks=new List<(List<Vector3> V,List<Vector3> N,List<int> T)>();
            foreach(var range in new[]{(0,8192),(8191,1809)})
            {
                var v=new List<Vector3>();var n=new List<Vector3>();var indices=new List<int>();
                ribbon.Invoke(null,new object[]{v,n,indices,points,0.008f,range.Item1,range.Item2});
                chunks.Add((v,n,indices));
            }
            int originalIndex=0;
            foreach(var chunk in chunks)
            {
                Check(chunk.V.Count<=16384,"vertex limit");
                foreach(int index in chunk.T)
                {
                    int expected=FullTriangles[originalIndex++];
                    Check(chunk.V[index].Equals(FullVerts[expected]),"triangle vertex changed");
                    Check(chunk.N[index].Equals(FullNormals[expected]),"triangle normal changed");
                }
            }
            Check(originalIndex==FullTriangles.Count,"triangle count changed");
            Check(chunks[0].V[16382].Equals(chunks[1].V[0]) && chunks[0].V[16383].Equals(chunks[1].V[1]),"seam mismatch");
            type.GetField("showRivers",F).SetValue(viewer,true);
            var paths=new List<RiverPathTracing.ContinuousRiverPath>{new RiverPathTracing.ContinuousRiverPath()};
            type.GetField("_adaptiveRefinedRiverPaths",F).SetValue(viewer,paths);
            var stateType=type.GetNestedType("RiverMeshBuildState",F);
            var chunkType=type.GetNestedType("RiverMeshChunk",F);
            object NewState()
            {
                var state=Activator.CreateInstance(stateType,true);
                stateType.GetField("Generation",F).SetValue(state,type.GetField("_riverRefinementGeneration",F).GetValue(viewer));
                stateType.GetField("Paths",F).SetValue(state,paths);
                stateType.GetField("GeometryComplete",F).SetValue(state,true);
                var list=(IList)stateType.GetField("Chunks",F).GetValue(state);
                foreach(var data in chunks)
                {
                    var chunk=Activator.CreateInstance(chunkType,true);
                    chunkType.GetField("Vertices",F).SetValue(chunk,data.V);
                    chunkType.GetField("Normals",F).SetValue(chunk,data.N);
                    chunkType.GetField("Triangles",F).SetValue(chunk,data.T);
                    list.Add(chunk);
                }
                type.GetField("_pendingRiverMesh",F).SetValue(viewer,state);
                return state;
            }
            var old=new GameObject("Rivers");old.transform.SetParent(owner.transform,false);
            type.GetMethod("BuildRiverChunk",F).Invoke(viewer,new object[]{old,chunks[1].V,chunks[1].N,chunks[1].T});
            var oldMesh=old.GetComponent<MeshFilter>().sharedMesh;
            var advance=type.GetMethod("AdvanceRiverMeshBuild",F);
            for (int attempt = 0; attempt < cancellationCount; attempt++)
            {
                NewState();advance.Invoke(viewer,null);
                Check(old.activeSelf && old.GetComponent<MeshFilter>().sharedMesh==oldMesh,"partial publication");
                Check(!owner.transform.Find("RiversPending").gameObject.activeSelf,"pending root visible");
                var pendingMesh=owner.transform.Find("RiversPending").GetComponent<MeshFilter>().sharedMesh;
                type.GetMethod("CancelRiverRefinement",F).Invoke(viewer,null);
                Check(owner.transform.Find("RiversPending")==null && pendingMesh==null,"cancel resource leak");
                Check(old!=null && oldMesh!=null,"cancel destroyed current mesh");
            }
            NewState();advance.Invoke(viewer,null);advance.Invoke(viewer,null);
            Check(type.GetField("_pendingRiverMesh",F).GetValue(viewer)==null,"upload incomplete");
            var current=owner.transform.Find("Rivers");
            Check(current!=null && current.gameObject.activeSelf,"new root inactive");
            Check(current.GetComponentsInChildren<MeshFilter>().Length==2,"chunks missing");
            var riverMaterial = current.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreEqual("WorldGen/RiverOverlay", riverMaterial.shader.name);
            Assert.IsTrue(riverMaterial.shader.isSupported);
            foreach (var renderer in current.GetComponentsInChildren<MeshRenderer>())
                Assert.AreSame(riverMaterial, renderer.sharedMaterial);
            Check(old==null && oldMesh==null,"retired resource leak");
            type.GetMethod("DestroyRiverRoot",F).Invoke(viewer,new object[]{current.gameObject});

        }
        finally
        {
            if(owner!=null)UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
