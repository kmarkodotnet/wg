using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod.Tests
{
    public class GeometryFeedbackTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Fields).SetValue(owner, value);
        private static object Get(object owner, string name) => owner.GetType().GetField(name, Fields).GetValue(owner);

        [TestCase(8,20,200000,1,true)]
        [TestCase(8,20,1,1,false)]
        [TestCase(80,20,200000,0,false)]
        [TestCase(8,9,200000,0,false)]
        public void EmittedUnityCornersFeedCoreProjectionWithLimits(int pixels,int maxLevel,int budget,int added,bool pending)
        {
            var target = new GameObject("GeometryFeedbackFixture");
            target.SetActive(false);
            try
            {
                Type type = Type.GetType("WorldGen.Viewer.PlanetGridMesh, Assembly-CSharp",true);
                Component planet = target.AddComponent(type);
                // A 14:05:40-es log tényleges quadja, Core koordinátákban.
                var points = new[] {
                    new SurfacePoint(51.2711067199707,-60.202106475830078,61.698127746582031),
                    new SurfacePoint(51.753681182861328,-60.390823364257813,61.891532897949219),
                    new SurfacePoint(51.8567008972168,-60.140777587890625,62.014766693115234),
                    new SurfacePoint(50.439212799072266,-58.862873077392578,60.697048187255859) };
                var camera = new Vector3(52.594116f,-84.707642f,78.634064f);
                Vector3 forward = -camera.normalized, right = Vector3.Cross(forward,Vector3.forward).normalized;
                Vector3 up = Vector3.Cross(right,forward).normalized;
                SurfacePoint P(Vector3 p) => new SurfacePoint(p.x,p.y,p.z);
                var view = new ProjectedLodView(P(camera),P(right),P(up),P(forward),Math.PI/3,957.0/583,.01,10000);
                var proxy = new TerrainLodProxy(3,Enumerable.Repeat(100.0,6*9*9).ToArray(),100);
                var cache = new LodTerrainEvaluationCache(view,proxy);
                var tile = TileId.FromFaceLevelUV(4,9,482,4);
                Set(planet,"_requestedProjectedView",view);
                Set(planet,"_requestedTerrainLodProxy",proxy);
                Set(planet,"_terrainEvaluationCache",cache);
                Set(planet,"adaptiveMaxLevel",maxLevel);
                Set(planet,"adaptiveRenderBudget",budget);
                Set(planet,"_currentTargetAngularRadiusRadians",AdaptiveViewState.AngularRadiusForPixelDiameter(pixels,Math.PI/3,583));
                object mesh = Activator.CreateInstance(type.GetNestedType("ConcatenatedMesh",BindingFlags.NonPublic),true);
                Set(mesh,"TileIds",new[] {tile});
                // Az emit listája Unity lokális koordináta, tehát Y/Z csere szükséges.
                ((List<Vector3>)Get(mesh,"Vertices")).AddRange(points.Select(p=>new Vector3((float)p.X,(float)p.Z,(float)p.Y)));
                object buffers = Activator.CreateInstance(type.GetNestedType("AdaptiveMeshBuffers",BindingFlags.NonPublic),true);
                Set(buffers,"TerrainConcat",mesh); Set(buffers,"Cut",new HashSet<TileId>{tile});
                type.GetMethod("CaptureGeometryFeedback",Fields).Invoke(planet,new[] {buffers,(object)CancellationToken.None});
                Assert.That(Get(buffers,"GeometryFeedbackAdded"),Is.EqualTo(added));
                Assert.That(Get(buffers,"GeometryRefinementPending"),Is.EqualTo(pending));
                if (added > 0)
                {
                    view.EvaluateQuad(new SurfaceQuad(points[0],points[1],points[2],points[3]),out double actual);
                    cache.EvaluateTerrain(proxy,tile,out double corrected,out _);
                    Assert.That(corrected,Is.GreaterThanOrEqualTo(actual-1e-8));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(target); }
        }
    }
}
