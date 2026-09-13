#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using WorldGen.App.Versioning;

namespace WorldGen.App.EditorTools
{
    /// <summary>
    /// Windows x64 Release build (WF-BUILD-001, ND-111). Menüből vagy batch módban:
    /// <c>Unity.exe -batchmode -quit -projectPath unity/WorldGenViewer
    /// -executeMethod WorldGen.App.EditorTools.WorldGenBuild.BuildWindowsRelease
    /// [-buildNumber 12] [-outputRoot path]</c>.
    ///
    /// A kiadási identitás a <c>tools/release/release-identity.json</c>-ból jön, és csak a
    /// build idejére kerül a PlayerSettings-be (utána visszaáll, a ProjectSettings nem
    /// koszolódik). A <c>StreamingAssets/build-info.json</c> szintén csak a build alatt létezik.
    /// </summary>
    public static class WorldGenBuild
    {
        private const string MenuPath = "WorldGen/Build/Windows x64 Release";

        [MenuItem(MenuPath)]
        public static void BuildWindowsReleaseFromMenu() => Run(0, null);

        /// <summary>Batch-belépési pont; hibánál 1-es kilépési kóddal lép ki.</summary>
        public static void BuildWindowsRelease()
        {
            string[] args = Environment.GetCommandLineArgs();
            int buildNumber = 0;
            string? outputRoot = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-buildNumber" && !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out buildNumber))
                    Fail("Invalid -buildNumber: " + args[i + 1]);
                if (args[i] == "-outputRoot") outputRoot = args[i + 1];
            }
            bool ok = Run(buildNumber, outputRoot);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static bool Run(int buildNumber, string? outputRoot)
        {
            string identityPath = Path.Combine(RepositoryRoot, "tools", "release", "release-identity.json");
            ReleaseIdentity identity;
            try
            {
                identity = ReleaseIdentity.Parse(File.ReadAllText(identityPath));
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                Debug.LogError("[WorldGenBuild] Release identity could not be read (" + identityPath + "): " + ex.Message);
                return false;
            }

            string[] scenes = ResolveScenes(identity);
            if (scenes.Length == 0)
            {
                Debug.LogError("[WorldGenBuild] No scenes to build: EditorBuildSettings is empty and fallbackScenes are missing or not found.");
                return false;
            }

            string root = outputRoot ?? Path.Combine(RepositoryRoot, "artifacts", "builds");
            string folder = Path.Combine(root, identity.BuildFolderName);
            string exePath = Path.Combine(folder, identity.ExecutableName + ".exe");

            var buildInfo = new BuildInfo(identity.ProductName, identity.Version, buildNumber, BuildInfo.InferChannel(identity.Version), DateTime.UtcNow);
            string streamingAssets = Path.Combine(Application.dataPath, "StreamingAssets");
            bool createdStreamingAssets = !Directory.Exists(streamingAssets);
            string buildInfoPath = Path.Combine(streamingAssets, "build-info.json");

            string previousProduct = PlayerSettings.productName;
            string previousCompany = PlayerSettings.companyName;
            string previousVersion = PlayerSettings.bundleVersion;
            try
            {
                Directory.CreateDirectory(streamingAssets);
                File.WriteAllText(buildInfoPath, BuildInfoCodec.ToJson(buildInfo));
                AssetDatabase.Refresh();

                PlayerSettings.productName = identity.ProductName;
                PlayerSettings.companyName = identity.CompanyName;
                PlayerSettings.bundleVersion = identity.Version.ToDisplayString();

                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = exePath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };
                Debug.Log("[WorldGenBuild] Building " + buildInfo.ToLogLine() + " -> " + exePath);
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                Debug.Log("[WorldGenBuild] Result " + summary.result + ", " + summary.totalErrors + " errors, "
                    + summary.totalWarnings + " warnings, " + (summary.totalSize / (1024 * 1024)) + " MB, " + summary.totalTime);
                return summary.result == BuildResult.Succeeded;
            }
            finally
            {
                PlayerSettings.productName = previousProduct;
                PlayerSettings.companyName = previousCompany;
                PlayerSettings.bundleVersion = previousVersion;
                DeleteWithMeta(buildInfoPath);
                if (createdStreamingAssets && Directory.Exists(streamingAssets) && Directory.GetFileSystemEntries(streamingAssets).Length == 0)
                {
                    Directory.Delete(streamingAssets);
                    DeleteWithMeta(streamingAssets);
                }
                AssetDatabase.Refresh();
            }
        }

        private static string[] ResolveScenes(ReleaseIdentity identity)
        {
            var result = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled && File.Exists(Path.Combine(RepositoryRoot, "unity", "WorldGenViewer", scene.path))) result.Add(scene.path);
            if (result.Count > 0) return result.ToArray();
            foreach (string scene in identity.FallbackScenes)
            {
                if (File.Exists(Path.Combine(RepositoryRoot, "unity", "WorldGenViewer", scene))) result.Add(scene);
                else Debug.LogWarning("[WorldGenBuild] Fallback scene not found: " + scene);
            }
            return result.ToArray();
        }

        private static void DeleteWithMeta(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
        }

        private static void Fail(string message)
        {
            Debug.LogError("[WorldGenBuild] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw new ArgumentException(message);
        }
    }
}
