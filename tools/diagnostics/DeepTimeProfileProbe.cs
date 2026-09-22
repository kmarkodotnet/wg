using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using WorldGen.Viewer;

// Unity Pipeline run_script; nem Assets-komponens, nem ment scene-t.
public static class DeepTimeProfileProbe
{
    [Serializable] public sealed class Config
    {
        public string output;
        public int warmups = 2, runs = 10;
        public bool profile, callStacks, hashGeometry;
        public double[] deepTimesMyr;
    }
    [Serializable] public sealed class Run
    {
        public int index;
        public bool warmup;
        public double milliseconds;
        public long unityAllocatedNetBytes, meshBytes;
        public double deepTimeMyr;
        public long unityAllocatedBytes, monoUsedBytes, monoReservedBytes;
        public string cameraPose;
        public List<string> geometryHashes = new List<string>();
    }
    [Serializable] public sealed class Marker
    {
        public string name;
        public double milliseconds;
        public long gcBytes;
        public int gcAllocations;
        public double gcAllocationMilliseconds;
        public List<string> gcEvents = new List<string>();
        public List<string> slowEvents = new List<string>();
    }
    [Serializable] public sealed class Allocation
    {
        public string phase;
        public long bytes;
        public List<string> stack = new List<string>();
    }
    [Serializable] public sealed class Frame
    {
        public int frameIndex, buildIndex, observedThreads;
        public long otherThreadGcBytesInBuildWindow;
        public List<Marker> markers = new List<Marker>();
        public List<Allocation> largeAllocations = new List<Allocation>();
        public List<WorkerThread> workerThreads = new List<WorkerThread>();
    }
    [Serializable] public sealed class WorkerThread
    {
        public string name;
        public long gcBytes;
        public List<string> gcEvents = new List<string>();
    }
    [Serializable] public sealed class Report
    {
        public string status, error, unityVersion, worldConfig, cameraConfig, perfLog;
        public string cameraPose;
        public int viewportWidth, viewportHeight;
        public Config config;
        public List<Run> runs = new List<Run>();
        public List<Frame> frames = new List<Frame>();
    }

    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Fields).GetValue(owner);
    private static Action _stop;

    public static string Start()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            throw new InvalidOperationException("Aktív, nem szünetelő Play mód kell.");
        if (ProfilerDriver.enabled || ProfilerDriver.deepProfiling)
            throw new InvalidOperationException("A meglévő Profiler-menetet nem módosítjuk.");
        if (_stop != null) throw new InvalidOperationException("Már fut mérés.");
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        Config config = JsonUtility.FromJson<Config>(File.ReadAllText(Path.Combine(root, "artifacts/a5-run-config.json")));
        if (config == null || string.IsNullOrWhiteSpace(config.output) || config.warmups < 0 || config.runs < 1 || config.warmups + (long)config.runs > 50)
            throw new ArgumentException("Kimeneti út és 1–50 összes mérés szükséges, nemnegatív bemelegítéssel.");
        if (config.callStacks && !config.profile)
            throw new ArgumentException("A hívásláncokhoz Profiler-menet szükséges.");
        if (config.hashGeometry && config.profile)
            throw new ArgumentException("A mesh-hash külön, Profiler nélküli menetben fusson: a hash maga sok mintát generál.");
        string output = Path.GetFullPath(Path.Combine(root, config.output));
        if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A kimenet csak a repón belül lehet.");
        if (File.Exists(output) || (config.profile && File.Exists(Path.ChangeExtension(output, ".data"))))
            throw new IOException("A mért eredményt nem írjuk felül.");
        if (!Directory.Exists(Path.GetDirectoryName(output)))
            throw new DirectoryNotFoundException("A kimeneti könyvtárat a mérés előtt hozd létre.");
        var planet = UnityEngine.Object.FindFirstObjectByType<PlanetGridMesh>();
        if (planet == null) throw new InvalidOperationException("Nincs PlanetGridMesh.");
        var sun = UnityEngine.Object.FindFirstObjectByType<SunController>();
        double oldTime = (double)Field(planet, "deepTimeMyr");
        float oldSlider = (float)Field(planet, "deepTimeSliderMyr");
        float oldLastSlider = (float)Field(planet, "_lastDeepTimeSliderMyr");
        bool changeTime = config.deepTimesMyr != null && config.deepTimesMyr.Length > 0;
        if (changeTime)
        {
            foreach (double time in config.deepTimesMyr)
                if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 || time > 1000)
                    throw new ArgumentException("A mért világidő 0..1000 Myr között lehet.");
            if (config.deepTimesMyr[(config.runs - 1) % config.deepTimesMyr.Length] != oldTime)
                throw new ArgumentException("A mérési sorozat a kiinduló világidővel végződjön.");
        }
        var autoField = sun == null ? null : sun.GetType().GetField("autoAdvance", Fields);
        object oldAuto = sun == null ? null : autoField.GetValue(sun);
        bool oldBackground = Application.runInBackground;
        bool oldProfile = planet.profileDeepTimeAllocations;
        bool oldEditor = ProfilerDriver.profileEditor;
        bool oldCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
        bool oldMemory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
        var oldRecord = ProfilerDriver.memoryRecordMode;
        var report = new Report { config = config, status = "running", unityVersion = Application.unityVersion,
            worldConfig = EditorJsonUtility.ToJson(planet), cameraConfig = Camera.main == null ? "" : EditorJsonUtility.ToJson(Camera.main) };
        if (Camera.main != null)
        {
            report.cameraPose = Camera.main.transform.position.ToString("R") + " / " + Camera.main.transform.rotation.ToString("R");
            report.viewportWidth = Camera.main.pixelWidth;
            report.viewportHeight = Camera.main.pixelHeight;
        }
        int count = 0, lastFrame = ProfilerDriver.lastFrameIndex;
        double next = EditorApplication.timeSinceStartup + 2, deadline = next + 240;
        bool collecting = false;
        UnityEditor.EditorApplication.CallbackFunction tick = null;
        Action save = () =>
        {
            // A Unity natív JsonUtility a futás közben betöltött assembly
            // összetett mezőit elhagyhatja; a .NET serializer nem használ asset-metaadatot.
            using (var stream = new MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Report)).WriteObject(stream, report);
                File.WriteAllText(output, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
            }
        };
        _stop = () =>
        {
            EditorApplication.update -= tick;
            Application.runInBackground = oldBackground;
            if (planet != null) planet.profileDeepTimeAllocations = oldProfile;
            if (planet != null && changeTime)
            {
                planet.GetType().GetField("deepTimeMyr", Fields).SetValue(planet, oldTime);
                planet.GetType().GetField("deepTimeSliderMyr", Fields).SetValue(planet, oldSlider);
                planet.GetType().GetField("_lastDeepTimeSliderMyr", Fields).SetValue(planet, oldLastSlider);
            }
            if (sun != null) autoField.SetValue(sun, oldAuto);
            ProfilerDriver.enabled = false;
            ProfilerDriver.profileEditor = oldEditor;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, oldCpu);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, oldMemory);
            ProfilerDriver.memoryRecordMode = oldRecord;
            _stop = null;
            save();
        };
        tick = () =>
        {
            try
            {
                if (!EditorApplication.isPlaying || planet == null) throw new InvalidOperationException("A Play-menet megszakadt.");
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("A mérési menet nem fejeződött be 240 s alatt.");
                if (EditorApplication.timeSinceStartup < next) return;
                if (collecting)
                {
                    Frame frame = FindBuildFrame(lastFrame + 1, ProfilerDriver.lastFrameIndex, count - 1, config.callStacks);
                    if (frame == null) return;
                    report.frames.Add(frame);
                    collecting = false;
                    save();
                }
                if (count >= config.runs + config.warmups)
                {
                    if (config.profile) ProfilerDriver.SaveProfile(Path.ChangeExtension(output, ".data"));
                    report.status = "complete";
                    _stop();
                    return;
                }
                // A halasztott/összevont kérést nem számoljuk mérésnek.
                if (Field(planet, "_cutTask") != null || Field(planet, "_pendingTerrainUpload") != null) return;
                if (changeTime && count >= config.warmups)
                {
                    double time = config.deepTimesMyr[(count - config.warmups) % config.deepTimesMyr.Length];
                    planet.GetType().GetField("deepTimeMyr", Fields).SetValue(planet, time);
                    planet.GetType().GetField("deepTimeSliderMyr", Fields).SetValue(planet, (float)time);
                    planet.GetType().GetField("_lastDeepTimeSliderMyr", Fields).SetValue(planet, (float)time);
                }
                bool completed = false;
                UnityEngine.Events.UnityAction built = () => completed = true;
                planet.Built.AddListener(built);
                long memoryBefore = Profiler.GetTotalAllocatedMemoryLong();
                lastFrame = ProfilerDriver.lastFrameIndex;
                var stopwatch = Stopwatch.StartNew();
                if (config.profile) Profiler.BeginSample("A5.ControlledBuild");
                try { planet.Build(); }
                finally
                {
                    if (config.profile) Profiler.EndSample();
                    stopwatch.Stop();
                    planet.Built.RemoveListener(built);
                }
                if (!completed) throw new InvalidOperationException("A Build váratlanul halasztva maradt.");
                var run = new Run { index = count, warmup = count < config.warmups,
                    milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    deepTimeMyr = (double)Field(planet, "deepTimeMyr"),
                    unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                    monoUsedBytes = Profiler.GetMonoUsedSizeLong(),
                    monoReservedBytes = Profiler.GetMonoHeapSizeLong(),
                    cameraPose = Camera.main == null ? "" : Camera.main.transform.position.ToString("R") + " / " + Camera.main.transform.rotation.ToString("R"),
                    unityAllocatedNetBytes = Profiler.GetTotalAllocatedMemoryLong() - memoryBefore,
                    meshBytes = Profiler.GetRuntimeMemorySizeLong(planet.GetComponent<MeshFilter>().sharedMesh) };
                // A tartalmi ellenőrzés külön menet: nem része az idő-/allokációmintának.
                if (config.hashGeometry)
                    foreach (string child in new[] { "", "WaterSurface", "LakeSurface" })
                    {
                        Transform target = child.Length == 0 ? planet.transform : planet.transform.Find(child);
                        Mesh mesh = target == null ? null : target.GetComponent<MeshFilter>()?.sharedMesh;
                        run.geometryHashes.Add(child + ":" + (mesh == null ? "missing" : HashMesh(mesh)));
                    }
                report.runs.Add(run);
                report.perfLog = (string)Field(planet, "_perfLogPath");
                count++;
                collecting = config.profile;
                next = EditorApplication.timeSinceStartup + 1;
                save();
            }
            catch (Exception e) { report.status = "failed"; report.error = e.ToString(); _stop(); }
        };
        try
        {
            save();
            Application.runInBackground = true;
            if (sun != null) autoField.SetValue(sun, false);
            planet.profileDeepTimeAllocations = config.profile;
            if (config.profile)
            {
                ProfilerDriver.profileEditor = true;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
                ProfilerDriver.memoryRecordMode = config.callStacks ? ProfilerMemoryRecordMode.GCAlloc : ProfilerMemoryRecordMode.None;
                ProfilerDriver.enabled = true;
            }
            EditorApplication.update += tick;
        }
        catch (Exception e) { report.status = "failed"; report.error = e.ToString(); _stop(); throw; }
        return output;
    }

    private static Frame FindBuildFrame(int first, int last, int buildIndex, bool stacks)
    {
        for (int f = Math.Max(first, ProfilerDriver.firstFrameIndex); f <= last; f++)
        {
            using (var data = ProfilerDriver.GetRawFrameDataView(f, 0))
            {
                if (!data.valid) continue;
                for (int i = 0; i < data.sampleCount; i++)
                {
                    if (data.GetSampleName(i) != "A5.ControlledBuild") continue;
                    var result = new Frame { frameIndex = f, buildIndex = buildIndex };
                    int end = i + data.GetSampleChildrenCountRecursive(i);
                    for (int m = i; m <= end; m++)
                    {
                        string name = data.GetSampleName(m);
                        if (m != i && !name.StartsWith("WorldGen.StaticBase.", StringComparison.Ordinal)) continue;
                        int markerEnd = m + data.GetSampleChildrenCountRecursive(m);
                        var marker = new Marker { name = name, milliseconds = data.GetSampleTimeMs(m) };
                        for (int j = m + 1; j <= markerEnd; j++)
                        {
                            string child = data.GetSampleName(j);
                            if (child == "GC.Alloc")
                            {
                                long bytes = data.GetSampleMetadataAsLong(j, 0);
                                marker.gcBytes += bytes;
                                marker.gcAllocations++;
                                marker.gcAllocationMilliseconds += data.GetSampleTimeMs(j);
                                if (stacks && m != i && bytes >= 1024 * 1024 && result.largeAllocations.Count < 100)
                                {
                                    var allocation = new Allocation { phase = name, bytes = bytes };
                                    var addresses = new List<ulong>();
                                    data.GetSampleCallstack(j, addresses);
                                    foreach (ulong address in addresses)
                                    {
                                        var info = data.ResolveMethodInfo(address);
                                        allocation.stack.Add(info.methodName + " " + info.sourceFileName + ":" + info.sourceFileLine);
                                    }
                                    result.largeAllocations.Add(allocation);
                                }
                            }
                            else if (child.IndexOf("GC", StringComparison.OrdinalIgnoreCase) >= 0)
                                marker.gcEvents.Add(child + "=" + data.GetSampleTimeMs(j).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "ms");
                            if (data.GetSampleTimeMs(j) >= 20 && marker.slowEvents.Count < 40)
                                marker.slowEvents.Add(child + "=" + data.GetSampleTimeMs(j).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "ms");
                        }
                        result.markers.Add(marker);
                    }
                    double start = data.GetSampleStartTimeMs(i), stop = start + data.GetSampleTimeMs(i);
                    for (int thread = 1; ; thread++)
                    {
                        using (var other = ProfilerDriver.GetRawFrameDataView(f, thread))
                        {
                            if (!other.valid) break;
                            result.observedThreads++;
                            var worker = new WorkerThread { name = other.threadName };
                            for (int s = 0; s < other.sampleCount; s++)
                            {
                                double sampleStart = other.GetSampleStartTimeMs(s);
                                double duration = other.GetSampleTimeMs(s);
                                string sampleName = other.GetSampleName(s);
                                if (sampleName == "GC.Alloc" && sampleStart >= start && sampleStart <= stop)
                                {
                                    worker.gcBytes += other.GetSampleMetadataAsLong(s, 0);
                                    long bytes = other.GetSampleMetadataAsLong(s, 0);
                                    if (stacks && bytes >= 1024 * 1024 && result.largeAllocations.Count < 160)
                                    {
                                        var allocation = new Allocation { phase = "worker:" + other.threadName, bytes = bytes };
                                        var addresses = new List<ulong>();
                                        other.GetSampleCallstack(s, addresses);
                                        foreach (ulong address in addresses)
                                        {
                                            var info = other.ResolveMethodInfo(address);
                                            allocation.stack.Add(info.methodName + " " + info.sourceFileName + ":" + info.sourceFileLine);
                                        }
                                        result.largeAllocations.Add(allocation);
                                    }
                                }
                                else if (sampleName != null && sampleName.IndexOf("GC", StringComparison.OrdinalIgnoreCase) >= 0 && sampleStart <= stop && sampleStart + duration >= start && worker.gcEvents.Count < 100)
                                    worker.gcEvents.Add(sampleName + "=" + duration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "ms");
                            }
                            result.otherThreadGcBytesInBuildWindow += worker.gcBytes;
                            if (worker.gcBytes > 0 || worker.gcEvents.Count > 0) result.workerThreads.Add(worker);
                        }
                    }
                    return result;
                }
            }
        }
        return null;
    }

    private static string HashMesh(Mesh mesh)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            using (var stream = new System.Security.Cryptography.CryptoStream(Stream.Null, sha, System.Security.Cryptography.CryptoStreamMode.Write))
            using (var writer = new BinaryWriter(stream))
            {
                var vertices = mesh.vertices;
                writer.Write(vertices.Length);
                foreach (var v in vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                var normals = mesh.normals;
                writer.Write(normals.Length);
                foreach (var n in normals) { writer.Write(n.x); writer.Write(n.y); writer.Write(n.z); }
                var colors = mesh.colors;
                writer.Write(colors.Length);
                foreach (var c in colors) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
                writer.Write(mesh.subMeshCount);
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    int[] indices = mesh.GetIndices(i);
                    writer.Write(indices.Length);
                    foreach (int index in indices) writer.Write(index);
                }
                var bounds = mesh.bounds;
                writer.Write(bounds.center.x); writer.Write(bounds.center.y); writer.Write(bounds.center.z);
                writer.Write(bounds.size.x); writer.Write(bounds.size.y); writer.Write(bounds.size.z);
            }
            return BitConverter.ToString(sha.Hash).Replace("-", "");
        }
    }
}
