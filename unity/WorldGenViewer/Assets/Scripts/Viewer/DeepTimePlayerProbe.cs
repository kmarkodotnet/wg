#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace WorldGen.Viewer
{
    // ND-135: kizárólag explicit CLI-kapcsolós Development Player-diagnosztika.
    public sealed class DeepTimePlayerProbe : MonoBehaviour
    {
        [Serializable] private sealed class Run
        {
            public int index, gcCollections;
            public bool warmup;
            public double milliseconds;
            public long monoUsedBefore, monoUsedAfter, unityAllocatedAfter;
        }
        [Serializable] private sealed class Report
        {
            public string status = "running", error, unityVersion, worldConfig, cameraPose, perfLog;
            public int width, height, warmups, measuredRuns;
            public bool phaseProfile, suppressGcDuringBuild;
            public List<Run> runs = new List<Run>();
        }
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static string[] _args;
        private static string Argument(string key)
        {
            int index = Array.IndexOf(_args, key);
            return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
        }
        private static object Field(object owner, string name) => owner.GetType().GetField(name, Fields).GetValue(owner);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            _args = Environment.GetCommandLineArgs();
            if (Argument("-a5-output") == null) return;
            var go = new GameObject("A5 Player measurement");
            DontDestroyOnLoad(go);
            go.AddComponent<DeepTimePlayerProbe>();
        }

        private IEnumerator Start()
        {
            string output = Path.GetFullPath(Argument("-a5-output"));
            if (File.Exists(output) || !Directory.Exists(Path.GetDirectoryName(output)))
            { UnityEngine.Debug.LogError("A5: létező kimenet vagy hiányzó könyvtár."); Application.Quit(2); yield break; }
            var report = new Report { unityVersion = Application.unityVersion,
                warmups = int.Parse(Argument("-a5-warmups") ?? "2"),
                measuredRuns = int.Parse(Argument("-a5-runs") ?? "10"),
                phaseProfile = Array.IndexOf(_args, "-a5-phases") >= 0,
                suppressGcDuringBuild = Array.IndexOf(_args, "-a5-no-gc") >= 0 };
            if (report.warmups < 0 || report.measuredRuns < 1 || (long)report.warmups + report.measuredRuns > (report.suppressGcDuringBuild ? 3 : 50))
            { UnityEngine.Debug.LogError("A5: érvénytelen mintaszám."); Application.Quit(2); yield break; }
            Application.runInBackground = true;
            yield return new WaitForSecondsRealtime(5);
            var planet = FindFirstObjectByType<PlanetGridMesh>();
            var sun = FindFirstObjectByType<SunController>();
            if (planet == null) { Application.Quit(2); yield break; }
            var auto = sun == null ? null : sun.GetType().GetField("autoAdvance", Fields);
            object oldAuto = sun == null ? null : auto.GetValue(sun);
            bool oldProfile = planet.profileDeepTimeAllocations;
            var originalGc = GarbageCollector.GCMode;
            Action save = () => File.WriteAllText(output, JsonUtility.ToJson(report, true));
            double deadline = Time.realtimeSinceStartupAsDouble + 240;
            try
            {
                if (sun != null) auto.SetValue(sun, false);
                report.worldConfig = JsonUtility.ToJson(planet);
                if (Camera.main != null)
                {
                    report.width = Camera.main.pixelWidth; report.height = Camera.main.pixelHeight;
                    report.cameraPose = Camera.main.transform.position.ToString("R") + " / " + Camera.main.transform.rotation.ToString("R");
                }
                planet.profileDeepTimeAllocations = report.phaseProfile;
                save();
                for (int i = 0; i < report.warmups + report.measuredRuns; i++)
                {
                    while (Field(planet, "_cutTask") != null || Field(planet, "_pendingTerrainUpload") != null)
                    {
                        if (Time.realtimeSinceStartupAsDouble > deadline)
                        { report.status = "failed"; report.error = "LOD-időtúllépés"; yield break; }
                        yield return null;
                    }
                    bool completed = false;
                    UnityEngine.Events.UnityAction built = () => completed = true;
                    planet.Built.AddListener(built);
                    var run = new Run { index = i, warmup = i < report.warmups, monoUsedBefore = Profiler.GetMonoUsedSizeLong() };
                    int beforeGc = GC.CollectionCount(0);
                    var timer = new Stopwatch();
                    try
                    {
                        if (report.suppressGcDuringBuild)
                        {
                            if (run.monoUsedBefore > 4L * 1024 * 1024 * 1024) throw new InvalidOperationException("A5: heap-korlát.");
                            GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                            if (GarbageCollector.GCMode != GarbageCollector.Mode.Disabled) throw new InvalidOperationException("A GC-mód nem alkalmazható.");
                        }
                        timer.Start();
                        planet.Build();
                        timer.Stop();
                        run.gcCollections = GC.CollectionCount(0) - beforeGc;
                    }
                    catch (Exception e) { report.status = "failed"; report.error = e.ToString(); }
                    finally { timer.Stop(); GarbageCollector.GCMode = originalGc; planet.Built.RemoveListener(built); }
                    if (report.status == "failed") yield break;
                    if (!completed) { report.status = "failed"; report.error = "Halasztott Build"; yield break; }
                    run.milliseconds = timer.Elapsed.TotalMilliseconds;
                    run.monoUsedAfter = Profiler.GetMonoUsedSizeLong();
                    run.unityAllocatedAfter = Profiler.GetTotalAllocatedMemoryLong();
                    report.runs.Add(run);
                    report.perfLog = (string)Field(planet, "_perfLogPath");
                    save();
                    yield return new WaitForSecondsRealtime(1);
                }
                report.status = "complete";
            }
            finally
            {
                GarbageCollector.GCMode = originalGc;
                if (planet != null) planet.profileDeepTimeAllocations = oldProfile;
                if (sun != null) auto.SetValue(sun, oldAuto);
                save();
                Application.Quit(report.status == "complete" ? 0 : 2);
            }
        }
    }
}
#endif
