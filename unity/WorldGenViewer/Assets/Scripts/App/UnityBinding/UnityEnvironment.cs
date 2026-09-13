#nullable enable
using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using WorldGen.App.Diagnostics;
using WorldGen.App.Versioning;

namespace WorldGen.App.UnityBinding
{
    /// <summary>Rendszer- és build-információ a Unity API-ból.</summary>
    public static class UnityEnvironment
    {
        public const string BuildInfoFileName = "build-info.json";

        public static SystemInfoReport CollectSystemInfo(BuildInfo build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            var current = Screen.currentResolution;
            return new SystemInfoReport
            {
                ApplicationVersion = build.ToLogLine(),
                EngineVersion = "Unity " + Application.unityVersion,
                OperatingSystem = SystemInfo.operatingSystem,
                CpuModel = SystemInfo.processorType,
                CpuLogicalCores = SystemInfo.processorCount,
                SystemMemoryMb = SystemInfo.systemMemorySize > 0 ? SystemInfo.systemMemorySize : (long?)null,
                GpuModel = SystemInfo.graphicsDeviceName,
                GraphicsApi = SystemInfo.graphicsDeviceVersion,
                GraphicsMemoryMb = SystemInfo.graphicsMemorySize > 0 ? SystemInfo.graphicsMemorySize : (long?)null,
                DisplayResolution = current.width.ToString(CultureInfo.InvariantCulture) + "x" + current.height.ToString(CultureInfo.InvariantCulture)
                    + "@" + current.refreshRateRatio.value.ToString("0.##", CultureInfo.InvariantCulture),
            };
        }

        /// <summary>
        /// A build-script által írt <c>StreamingAssets/build-info.json</c>; ha nincs
        /// (Editor, kézi build), az <c>Application.version</c> Development csatornával.
        /// </summary>
        public static BuildInfo LoadBuildInfo()
        {
            string path = Path.Combine(Application.streamingAssetsPath, BuildInfoFileName);
            try
            {
                if (File.Exists(path) && BuildInfoCodec.TryParse(File.ReadAllText(path), out var fromFile)) return fromFile!;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            string product = string.IsNullOrWhiteSpace(Application.productName) ? "WorldGen" : Application.productName;
            if (!SemanticVersion.TryParse(Application.version, out var version)) version = new SemanticVersion(0, 0, 0, "dev");
            return new BuildInfo(product, version, 0, ReleaseChannel.Development, null);
        }
    }
}
