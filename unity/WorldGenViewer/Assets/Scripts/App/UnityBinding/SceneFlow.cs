#nullable enable
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldGen.App.Diagnostics;

namespace WorldGen.App.UnityBinding
{
    public static class SceneNames
    {
        /// <summary>Persistent: az <see cref="AppBootstrap"/> él benne, soha nem töltődik ki.</summary>
        public const string Bootstrap = "Bootstrap";

        public const string MainMenu = "MainMenu";
        public const string WorldSimulation = "WorldSimulation";
    }

    /// <summary>
    /// Additív tartalom-scene csere a persistent Bootstrap mellett (WF-APP-002):
    /// az előző tartalom-scene kitöltése, a nem használt assetek felszabadítása,
    /// majd az új betöltése. Egyszerre egy csere futhat.
    /// </summary>
    public sealed class SceneFlow
    {
        private readonly MonoBehaviour _coroutineHost;
        private readonly LogChannel _log;

        public SceneFlow(MonoBehaviour coroutineHost, LogChannel log)
        {
            _coroutineHost = coroutineHost != null ? coroutineHost : throw new ArgumentNullException(nameof(coroutineHost));
            _log = log;
        }

        public string? ActiveContentScene { get; private set; }
        public bool IsBusy { get; private set; }

        /// <summary>Hamis, ha már fut egy csere, vagy a scene nincs a buildben.</summary>
        public bool SwitchTo(string sceneName, Action<float>? progress = null, Action<bool>? completed = null)
        {
            if (string.IsNullOrEmpty(sceneName)) throw new ArgumentException("Üres scene-név.", nameof(sceneName));
            if (IsBusy) return false;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                _log.Error("Scene is not in the build: " + sceneName);
                completed?.Invoke(false);
                return false;
            }
            _coroutineHost.StartCoroutine(SwitchRoutine(sceneName, progress, completed));
            return true;
        }

        private IEnumerator SwitchRoutine(string sceneName, Action<float>? progress, Action<bool>? completed)
        {
            IsBusy = true;
            progress?.Invoke(0f);

            if (ActiveContentScene != null)
            {
                var unload = SceneManager.UnloadSceneAsync(ActiveContentScene);
                if (unload != null)
                {
                    while (!unload.isDone) yield return null;
                }
                ActiveContentScene = null;
                var cleanup = Resources.UnloadUnusedAssets();
                while (!cleanup.isDone) yield return null;
            }

            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                _log.Error("Scene load could not start: " + sceneName);
                IsBusy = false;
                completed?.Invoke(false);
                yield break;
            }
            while (!load.isDone)
            {
                // A Unity 0.9-nél áll meg aktiválás előtt; ezt 1.0-ra skálázzuk.
                progress?.Invoke(Mathf.Clamp01(load.progress / 0.9f));
                yield return null;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid()) SceneManager.SetActiveScene(scene);
            ActiveContentScene = sceneName;
            _log.Info("Scene active: " + sceneName);
            progress?.Invoke(1f);
            IsBusy = false;
            completed?.Invoke(true);
        }
    }
}
