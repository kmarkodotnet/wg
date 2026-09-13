#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using WorldGen.App.Controls;
using WorldGen.App.Diagnostics;
using WorldGen.App.Flow;
using WorldGen.App.Lifecycle;
using WorldGen.App.Localization;
using WorldGen.App.Navigation;
using WorldGen.App.Saves;
using WorldGen.App.Screenshots;
using WorldGen.App.Serialization;
using WorldGen.App.Services;
using WorldGen.App.Settings;
using WorldGen.App.Storage;
using WorldGen.App.UI;
using WorldGen.App.Versioning;

namespace WorldGen.App.UnityBinding
{
    /// <summary>
    /// Az alkalmazás kompozíciós gyökere (WF-APP-002/003) a persistent Bootstrap
    /// scene-ben. Felépíti a Foundation-szolgáltatásokat, betölti és alkalmazza a
    /// beállításokat, naplóz, kezeli a fókuszt, a kilépést és a globális hotkeyeket.
    ///
    /// Még NINCS scene-be kötve: amíg a MainMenu nézet (ND-110) és a Core-kötés
    /// hiányzik, az <see cref="enterMainMenuOnStart"/> alapból hamis, így a meglévő
    /// PlanetView-munkafolyamatot nem változtatja meg.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class AppBootstrap : MonoBehaviour
    {
        private const int KeepLogFiles = 10;
        private const string KeyBindingsFileName = "keybindings.json";

        // A duplikált Bootstrap (pl. scene újratöltés) elleni őr; nem szolgáltatás-elérés.
        private static bool s_instanceExists;

        [SerializeField] private AudioMixer? audioMixer = null;

        [Tooltip("ND-108: amíg a Core-nak nincs generátorverziója, ez a helyőrző kerül a mentésekbe.")]
        [SerializeField] private string worldGeneratorVersion = "unversioned-dev";

        [Tooltip("Csak ha a MainMenu nézet és scene már létezik.")]
        [SerializeField] private bool enterMainMenuOnStart = false;

        private bool _owner;
        private bool _hasFocus = true;
        private bool _revertingVideo;
        private AppSettings _current = new AppSettings();
        private AppLogger? _logger;
        private FileLogSink? _fileSink;
        private UnityLogBridge? _logBridge;
        private SettingsStore? _settingsStore;
        private AppStateMachine? _states;
        private SessionManager? _sessions;
        private DialogService? _dialogs;
        private ToastQueue? _toasts;
        private BackNavigationRouter? _navigation;
        private LocalizationTable? _text;
        private AutosaveScheduler? _autosave;
        private SaveRepository? _saves;
        private AppFlowController? _flow;
        private KeyBindingMap? _keyBindings;
        private UnityGraphicsApplier? _graphics;
        private UnityAudioApplier? _audio;
        private VideoModeConfirmation? _videoConfirmation;
        private UnityScreenshotService? _screenshots;
        private ExceptionThrottle? _exceptionThrottle;
        private readonly FrameTimeStats _frameStats = new FrameTimeStats();

        public ServiceRegistry? Services { get; private set; }
        public WorldSessionHostSlot SessionHost { get; } = new WorldSessionHostSlot();
        public SceneFlow? Scenes { get; private set; }
        public UserDataLayout? UserData { get; private set; }
        public BuildInfo? Build { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDisabledDomainReload() => s_instanceExists = false;

        private void Awake()
        {
            if (s_instanceExists)
            {
                Destroy(gameObject);
                return;
            }
            s_instanceExists = true;
            _owner = true;
            DontDestroyOnLoad(gameObject);
            // A háttér-FPS és a fókuszvesztéskori pause a beállításokból jön, nem a Unity alapértelmezéséből.
            Application.runInBackground = true;

            try
            {
                Compose();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _logger?.Error("Boot", "Bootstrap composition failed", ex);
            }
        }

        private void Compose()
        {
            var clock = new SystemClock();
            var fs = new PhysicalFileSystem();
            UserData = new UserDataLayout(Application.persistentDataPath);
            UserData.EnsureCreated(fs);
            Build = UnityEnvironment.LoadBuildInfo();

            _logger = new AppLogger(clock, Debug.isDebugBuild ? LogLevel.Debug : LogLevel.Info);
            var memorySink = new MemoryLogSink(500);
            _logger.AddSink(memorySink);
            OpenLogFile(fs);
            var log = _logger.ForCategory("Boot");
            log.Info(Build.ToLogLine());
            foreach (string line in UnityEnvironment.CollectSystemInfo(Build).ToLogLines()) log.Info(line);
            log.Info("User data: " + UserData.Root);

            _exceptionThrottle = new ExceptionThrottle();
            _logBridge = new UnityLogBridge(_logger, _exceptionThrottle);
            _logBridge.Attach();

            _settingsStore = new SettingsStore(fs, UserData.SettingsFile, clock);
            var loaded = _settingsStore.Load();
            foreach (var issue in loaded.Issues) log.Warning("Settings: " + issue);
            if (loaded.CorruptFileCopyPath != null) log.Warning("Corrupt settings preserved at " + loaded.CorruptFileCopyPath);
            _current = loaded.Settings;

            _text = EnglishStrings.CreateTable();
            _text.CurrentLanguage = _current.Interface.Language;
            _states = new AppStateMachine();
            _states.StateChanged += change => _logger.Info("State", change.ToString());
            _states.TransitionRejected += (from, to, reason) => _logger.Warning("State", "Rejected " + from + " -> " + to + " (" + reason + ")");
            _sessions = new SessionManager(ex => _logger.Error("Session", "Cleanup failed", ex));
            _dialogs = new DialogService(() => _current.Interface.ShowConfirmations);
            _toasts = new ToastQueue();
            _navigation = new BackNavigationRouter();
            _navigation.PushHandler(_dialogs);
            _autosave = new AutosaveScheduler(_current.Simulation.AutosaveIntervalMinutes);
            string generatorVersion = string.IsNullOrWhiteSpace(worldGeneratorVersion) ? "unversioned-dev" : worldGeneratorVersion;
            _saves = new SaveRepository(fs, UserData.Saves,
                header => SaveCompatibility.Evaluate(header, SaveHeaderCodec.CurrentFormatVersion, generatorVersion));
            _flow = new AppFlowController(_states, _sessions, _dialogs, _toasts, _navigation, _text, _autosave, () => _current, SessionHost);
            _flow.QuitRequested += QuitApplication;

            _keyBindings = new KeyBindingMap();
            LoadKeyBindings(fs, log);

            _graphics = new UnityGraphicsApplier(_logger.ForCategory("Graphics"));
            _audio = new UnityAudioApplier(audioMixer, _logger.ForCategory("Audio"));
            _videoConfirmation = new VideoModeConfirmation();
            _videoConfirmation.RevertRequested += RevertVideoSettings;
            ApplySettings(_current, SettingsCategory.All, previousGraphics: null);
            _settingsStore.Changed += OnSettingsChanged;

            _screenshots = gameObject.AddComponent<UnityScreenshotService>();
            _screenshots.Configure(UserData.Screenshots, null);
            _screenshots.ScreenshotSaved += path =>
            {
                _logger.Info("Screenshot", path);
                _toasts.Show(ToastSeverity.Success, _text.Get("toast.screenshotSaved"));
            };
            _screenshots.ScreenshotFailed += ex => _logger.Error("Screenshot", "Capture failed", ex);

            Scenes = new SceneFlow(this, _logger.ForCategory("Scenes"));

            Services = new ServiceRegistry();
            Services.Register(UserData);
            Services.Register(Build);
            Services.Register(_logger);
            Services.Register(_settingsStore);
            Services.Register(_text);
            Services.Register(_states);
            Services.Register(_sessions);
            Services.Register(_dialogs);
            Services.Register(_toasts);
            Services.Register(_navigation);
            Services.Register(_autosave);
            Services.Register(_saves);
            Services.Register(_flow);
            Services.Register(_keyBindings);
            Services.Register(_videoConfirmation);
            Services.Register(_screenshots);
            Services.Register(Scenes);
            Services.Register(_frameStats);
            Services.Register(SessionHost);
            Services.InitializeAll();

            Application.wantsToQuit += OnWantsToQuit;
            log.Info("Bootstrap ready");

            if (enterMainMenuOnStart) _flow.EnterMainMenuFromBoot(loaded.IsFirstRun || _current.Interface.ShowWelcomeOnStartup);
        }

        private void Update()
        {
            if (_flow == null || _toasts == null || _keyBindings == null || _screenshots == null) return;
            double dt = Time.unscaledDeltaTime;
            _frameStats.AddSample(dt * 1000.0);
            _toasts.Tick(dt);
            _videoConfirmation?.Tick(dt);
            _flow.Tick(dt);

            if (KeyBindingInput.WasPressedThisFrame(_keyBindings.GetBinding(InputActionIds.Back))) _flow.HandleBack();
            if (KeyBindingInput.WasPressedThisFrame(_keyBindings.GetBinding(InputActionIds.Screenshot))) _screenshots.Capture(new ScreenshotRequest(true));
            if (KeyBindingInput.WasPressedThisFrame(_keyBindings.GetBinding(InputActionIds.CleanScreenshot))) _screenshots.Capture(new ScreenshotRequest(false));
            if (KeyBindingInput.WasPressedThisFrame(_keyBindings.GetBinding(InputActionIds.QuickSave))) _flow.QuickSave();
            if (KeyBindingInput.WasPressedThisFrame(_keyBindings.GetBinding(InputActionIds.QuickLoad)) && _saves != null) _flow.QuickLoad(_saves.List());
            if (KeyBindingInput.WasPressedThisFrame(_keyBindings.GetBinding(InputActionIds.ToggleDebugOverlay)) && _settingsStore != null)
            {
                var next = _settingsStore.Snapshot;
                next.Interface.ShowDebugOverlay = !next.Interface.ShowDebugOverlay;
                TryApplySettings(next);
            }

            if (_logBridge != null && _text != null && _logBridge.TryTakeFirstException(out _))
                _toasts.Show(ToastSeverity.Error, _text.Get("dialog.error.title"));
        }

        private void OnGUI()
        {
            if (!_current.Interface.ShowDebugOverlay) return;
            var lines = DebugOverlayText.BuildLines(_frameStats, null);
            if (lines.Count == 0) return;
            GUI.Label(new Rect(Screen.width - 340, 10, 330, 20 * lines.Count + 10), string.Join("\n", lines));
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            if (_graphics == null) return;
            var decision = _graphics.ApplyFrameRate(_current.Graphics, _current.Simulation, hasFocus);
            if (decision.PauseSimulation) _flow?.Pause();
        }

        private void OnApplicationQuit()
        {
            _logger?.Info("Boot", "Application quit");
            _logger?.Flush();
        }

        private void OnDestroy()
        {
            if (!_owner) return;
            Application.wantsToQuit -= OnWantsToQuit;
            if (_settingsStore != null) _settingsStore.Changed -= OnSettingsChanged;
            if (_flow != null)
            {
                _flow.QuitRequested -= QuitApplication;
                _flow.Dispose();
            }
            _sessions?.EndSession();
            if (_exceptionThrottle != null && _logger != null)
                foreach (var pair in _exceptionThrottle.GetSuppressedCounts())
                    _logger.Warning("Unity", "Suppressed " + pair.Value + " repeats of: " + pair.Key);
            Services?.ShutdownAll(ex => _logger?.Error("Boot", "Service shutdown failed", ex));
            _logBridge?.Dispose();
            _logger?.Info("Boot", "Shutdown complete");
            _logger?.Flush();
            _fileSink?.Dispose();
            s_instanceExists = false;
        }

        /// <summary>Beállítások alkalmazása a nézetrétegből; íráshiba esetén naplóz és toastot mutat.</summary>
        public bool TryApplySettings(AppSettings updated)
        {
            if (_settingsStore == null) return false;
            try
            {
                _settingsStore.Apply(updated);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger?.Error("Settings", "Saving settings failed", ex);
                if (_text != null) _toasts?.Show(ToastSeverity.Error, _text.Get("dialog.saveFailed.title"));
                return false;
            }
        }

        private void OnSettingsChanged(AppSettings settings, SettingsCategory changed)
        {
            var previous = _current;
            _current = settings;
            ApplySettings(settings, changed, previous.Graphics);
        }

        private void ApplySettings(AppSettings settings, SettingsCategory changed, WorldGen.App.Settings.GraphicsSettings? previousGraphics)
        {
            if ((changed & (SettingsCategory.Graphics | SettingsCategory.Simulation)) != 0)
                _graphics?.ApplyFrameRate(settings.Graphics, settings.Simulation, _hasFocus);
            if ((changed & SettingsCategory.Graphics) != 0)
            {
                _graphics?.ApplyQuality(settings.Graphics.Quality);
                bool displayChanged = previousGraphics == null || VideoModeConfirmation.RequiresConfirmation(previousGraphics, settings.Graphics);
                if (displayChanged) _graphics?.ApplyDisplay(settings.Graphics);
                if (displayChanged && previousGraphics != null && !_revertingVideo) BeginVideoConfirmation(previousGraphics, settings.Graphics);
            }
            if ((changed & SettingsCategory.Audio) != 0) _audio?.Apply(settings.Audio);
            if ((changed & SettingsCategory.Interface) != 0 && _text != null) _text.CurrentLanguage = settings.Interface.Language;
            if ((changed & SettingsCategory.Simulation) != 0 && _autosave != null) _autosave.IntervalMinutes = settings.Simulation.AutosaveIntervalMinutes;
        }

        private void BeginVideoConfirmation(WorldGen.App.Settings.GraphicsSettings previous, WorldGen.App.Settings.GraphicsSettings candidate)
        {
            if (_videoConfirmation == null || _dialogs == null || _text == null) return;
            _videoConfirmation.Begin(previous, candidate);
            var request = DialogRequest.Confirm(_text.Get("dialog.videoConfirm.title"),
                _text.Format("dialog.videoConfirm.message", (int)VideoModeConfirmation.DefaultTimeoutSeconds),
                _text.Get("dialog.button.keep"), _text.Get("dialog.button.revert"), tag: "video-confirm");
            _dialogs.Show(request, result =>
            {
                if (result.IsConfirmed) _videoConfirmation.Confirm();
                else _videoConfirmation.Revert();
            });
        }

        private void RevertVideoSettings(WorldGen.App.Settings.GraphicsSettings previous)
        {
            if (_settingsStore == null) return;
            _dialogs?.CancelActive();
            var next = _settingsStore.Snapshot;
            next.Graphics.DisplayMode = previous.DisplayMode;
            next.Graphics.Resolution = previous.Resolution;
            _revertingVideo = true;
            try
            {
                TryApplySettings(next);
            }
            finally
            {
                _revertingVideo = false;
            }
        }

        private bool OnWantsToQuit() => _flow == null || _flow.OnApplicationWantsToQuit();

        private void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OpenLogFile(PhysicalFileSystem fs)
        {
            if (_logger == null || UserData == null) return;
            try
            {
                var existing = new List<string>();
                foreach (string path in fs.GetFiles(UserData.Logs, LogFileNaming.Extension)) existing.Add(Path.GetFileName(path));
                foreach (string old in LogFileNaming.SelectFilesToDelete(existing, KeepLogFiles - 1))
                    fs.DeleteFile(Path.Combine(UserData.Logs, old));
                string name = LogFileNaming.CreateUniqueFileName(DateTime.Now, n => File.Exists(Path.Combine(UserData.Logs, n)));
                _fileSink = new FileLogSink(Path.Combine(UserData.Logs, name));
                _logger.AddSink(_fileSink);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning("WorldGen log file could not be opened: " + ex.Message);
            }
        }

        private void LoadKeyBindings(PhysicalFileSystem fs, LogChannel log)
        {
            if (_keyBindings == null || UserData == null) return;
            string path = Path.Combine(UserData.SettingsDirectory, KeyBindingsFileName);
            if (!fs.FileExists(path)) return;
            try
            {
                foreach (string issue in _keyBindings.LoadJson(JsonParser.Parse(File.ReadAllText(path))))
                    log.Warning("Key bindings: ignored " + issue);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonFormatException)
            {
                log.Warning("Key bindings could not be read, defaults used", ex);
                _keyBindings.RestoreDefaults();
            }
        }
    }
}
