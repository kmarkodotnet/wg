#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using WorldGen.App.Lifecycle;
using WorldGen.App.Localization;
using WorldGen.App.Navigation;
using WorldGen.App.Saves;
using WorldGen.App.Settings;
using WorldGen.App.UI;
using WorldGen.App.WorldSetup;

namespace WorldGen.App.Flow
{
    /// <summary>A képernyőverem azonosítói; a nézetréteg ezekhez rendel nézetet.</summary>
    public static class ScreenIds
    {
        public const string MainMenu = "main-menu";
        public const string Welcome = "welcome";
        public const string NewWorld = "new-world";
        public const string LoadWorld = "load-world";
        public const string SaveAs = "save-as";
        public const string Settings = "settings";
        public const string Help = "help";
        public const string Credits = "credits";
        public const string Pause = "pause";
        public const string Loading = "loading";
    }

    /// <summary>
    /// Az aktuális világ állapota a flow számára. A Core-kötés valósítja meg;
    /// amíg a szimulációs állapot szerializálója nincs bekötve, a <see cref="CanSave"/> hamis.
    /// </summary>
    public interface IWorldSessionHost
    {
        bool HasUnsavedChanges { get; }
        bool CanSave { get; }

        /// <summary>A betöltött vagy utoljára mentett fájl; null, ha még nem volt mentve (→ Save As).</summary>
        string? CurrentSavePath { get; }

        string? CurrentWorldId { get; }
    }

    /// <summary>
    /// A menüakciók, megerősítések, állapotváltások és session-határok egyetlen
    /// helye (WF-APP-001, WF-UI-001/006, WF-SAVE-002/004). Maga nem generál és nem
    /// ment: eseményekkel kéri a Core-kötést, és a <c>Notify…</c> metódusokon
    /// kapja vissza az eredményt. Csak a főszálról használható.
    /// </summary>
    public sealed class AppFlowController : IDisposable
    {
        private readonly AppStateMachine _states;
        private readonly SessionManager _sessions;
        private readonly DialogService _dialogs;
        private readonly ToastQueue _toasts;
        private readonly BackNavigationRouter _navigation;
        private readonly LocalizationTable _text;
        private readonly AutosaveScheduler _autosave;
        private readonly Func<AppSettings> _settings;
        private readonly IWorldSessionHost _host;
        private SaveKind? _saveInProgress;
        private bool _disposed;

        /// <summary>Új világ generálása: a session már nyitva, az erőforrásokat abba kell regisztrálni.</summary>
        public event Action<WorldCreationRequest, SessionScope>? NewWorldRequested;

        /// <summary>Mentés betöltése (fájlút): a session már nyitva.</summary>
        public event Action<string, SessionScope>? LoadRequested;

        /// <summary>Mentés kérése; az út null Auto/Quick esetén (a kötés képzi a <see cref="SaveRepository"/>-val).</summary>
        public event Action<SaveKind, string?>? SaveRequested;

        public event Action? CancelLoadingRequested;

        /// <summary>A kötés erre hívja az <c>Application.Quit()</c>-et.</summary>
        public event Action? QuitRequested;

        public bool IsQuitConfirmed { get; private set; }

        public bool IsSaveInProgress => _saveInProgress.HasValue;

        public AppFlowController(AppStateMachine states, SessionManager sessions, DialogService dialogs, ToastQueue toasts,
            BackNavigationRouter navigation, LocalizationTable text, AutosaveScheduler autosave, Func<AppSettings> settings,
            IWorldSessionHost host)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _autosave = autosave ?? throw new ArgumentNullException(nameof(autosave));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _states.StateChanged += OnStateChanged;
        }

        // ---- indulás, menük ----

        public void EnterMainMenuFromBoot(bool showWelcome)
        {
            if (_states.Current != AppState.Boot) throw new InvalidOperationException("Csak Boot állapotból: " + _states.Current);
            _states.RequestTransition(AppState.MainMenu, "boot");
            if (showWelcome) _navigation.Screens.Push(ScreenIds.Welcome);
        }

        /// <summary>Főmenü-elem aktiválása. Hamis, ha az adott állapotban vagy a mentések alapján nem értelmezhető.</summary>
        public bool OnMainMenuItem(string itemId, IReadOnlyList<SaveSlotInfo> slots)
        {
            if (_states.Current != AppState.MainMenu) return false;
            switch (itemId)
            {
                case MenuIds.NewWorld:
                    return _states.RequestTransition(AppState.WorldCreation, "new-world") == TransitionResult.Completed;
                case MenuIds.Continue:
                    var candidate = SaveRepository.FindContinueCandidate(slots ?? throw new ArgumentNullException(nameof(slots)));
                    return candidate != null && BeginLoad(candidate.FilePath);
                case MenuIds.LoadWorld:
                    return PushScreen(ScreenIds.LoadWorld);
                case MenuIds.Settings:
                    return PushScreen(ScreenIds.Settings);
                case MenuIds.Help:
                    return PushScreen(ScreenIds.Help);
                case MenuIds.Credits:
                    return PushScreen(ScreenIds.Credits);
                case MenuIds.Quit:
                    RequestQuit();
                    return true;
                default:
                    return false;
            }
        }

        public bool OnPauseMenuItem(string itemId)
        {
            if (_states.Current != AppState.Paused) return false;
            switch (itemId)
            {
                case MenuIds.Resume:
                    return Resume();
                case MenuIds.SaveWorld:
                    if (!CanStartSave()) return false;
                    if (_host.CurrentSavePath == null) return PushScreen(ScreenIds.SaveAs);
                    RaiseSave(SaveKind.Manual, _host.CurrentSavePath);
                    return true;
                case MenuIds.SaveWorldAs:
                    return CanStartSave() && PushScreen(ScreenIds.SaveAs);
                case MenuIds.LoadWorld:
                    return PushScreen(ScreenIds.LoadWorld);
                case MenuIds.Settings:
                    return PushScreen(ScreenIds.Settings);
                case MenuIds.Help:
                    return PushScreen(ScreenIds.Help);
                case MenuIds.ReturnToMainMenu:
                    ConfirmDiscard(() => ReturnToMainMenuNow());
                    return true;
                case MenuIds.QuitToDesktop:
                    RequestQuit();
                    return true;
                default:
                    return false;
            }
        }

        // ---- világ indítása és betöltése ----

        public bool StartNewWorld(WorldCreationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_states.Current != AppState.WorldCreation) return false;
            if (_states.RequestTransition(AppState.Loading, "generate") != TransitionResult.Completed) return false;
            var scope = _sessions.BeginSession(request.WorldName);
            NewWorldRequested?.Invoke(request, scope);
            return true;
        }

        /// <summary>A Load World képernyő választása. Futó világból a nem mentett haladásról megerősítést kér.</summary>
        public bool LoadSave(SaveSlotInfo slot)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));
            if (!slot.Compatibility.CanLoadState) return false;
            if (_states.Current == AppState.MainMenu) return BeginLoad(slot.FilePath);
            if (_states.Current != AppState.Paused) return false;
            ConfirmDiscard(() => BeginLoad(slot.FilePath));
            return true;
        }

        public bool NotifyWorldReady()
        {
            if (_states.Current != AppState.Loading) return false;
            if (_states.RequestTransition(AppState.Simulation, "ready") != TransitionResult.Completed) return false;
            _autosave.NotifySaved();
            return true;
        }

        public void NotifyLoadingFailed(string userMessage)
        {
            if (_states.Current != AppState.Loading) return;
            _states.RequestTransition(AppState.MainMenu, "load-failed");
            _dialogs.Show(CommonDialogs.LoadFailed(_text, userMessage ?? ""));
        }

        public void NotifyLoadingCancelled()
        {
            if (_states.Current != AppState.Loading) return;
            _states.RequestTransition(AppState.MainMenu, "load-cancelled");
        }

        // ---- szimuláció, pause ----

        public bool Pause() => _states.Current == AppState.Simulation && _states.RequestTransition(AppState.Paused, "pause") == TransitionResult.Completed;

        public bool Resume() => _states.Current == AppState.Paused && _states.RequestTransition(AppState.Simulation, "resume") == TransitionResult.Completed;

        /// <summary>ESC / Vissza egységesen; a végrehajtott akciót adja vissza.</summary>
        public BackAction HandleBack()
        {
            var action = _navigation.Route(_states.Current);
            switch (action)
            {
                case BackAction.OpenPauseMenu:
                    Pause();
                    break;
                case BackAction.ResumeSimulation:
                    Resume();
                    break;
                case BackAction.ReturnToMainMenu:
                    _states.RequestTransition(AppState.MainMenu, "back");
                    break;
                case BackAction.CancelLoading:
                    CancelLoadingRequested?.Invoke();
                    break;
            }
            return action;
        }

        public bool PopScreen() => _navigation.Screens.Pop() != null;

        // ---- mentés ----

        /// <summary>A Save As képernyő megerősítése a kötés által képzett úttal.</summary>
        public bool SaveTo(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("Üres mentési út.", nameof(filePath));
            if (!_states.IsSessionState || !CanStartSave()) return false;
            if (_navigation.Screens.Current == ScreenIds.SaveAs) _navigation.Screens.Pop();
            RaiseSave(SaveKind.Manual, filePath);
            return true;
        }

        public bool QuickSave()
        {
            if (!_states.IsSessionState || !CanStartSave()) return false;
            RaiseSave(SaveKind.Quick, null);
            return true;
        }

        /// <summary>Az aktuális világ gyorsmentésének betöltése (megerősítéssel, ha van nem mentett haladás).</summary>
        public bool QuickLoad(IReadOnlyList<SaveSlotInfo> slots)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            if (!_states.IsSessionState || _host.CurrentWorldId == null) return false;
            SaveSlotInfo? quick = null;
            foreach (var s in slots)
            {
                if (s.Header == null || s.Header.Kind != SaveKind.Quick || !s.Compatibility.CanLoadState) continue;
                if (!string.Equals(s.Header.WorldId, _host.CurrentWorldId, StringComparison.Ordinal)) continue;
                if (quick == null || s.SortTimeUtc > quick.SortTimeUtc) quick = s;
            }
            if (quick == null) return false;
            string path = quick.FilePath;
            ConfirmDiscard(() => BeginLoad(path));
            return true;
        }

        /// <summary>Deep Time ugrás előtt: ha a beállítás kéri és menthető, autosave-et indít.</summary>
        public bool BeforeDeepTimeJump()
        {
            if (!AutosaveScheduler.ShouldSaveBeforeDeepTimeJump(_settings().Simulation) || !_states.IsSessionState || !CanStartSave()) return false;
            RaiseSave(SaveKind.Auto, null);
            return true;
        }

        public void NotifySaveCompleted(SaveKind kind, bool success, string? userMessage)
        {
            _saveInProgress = null;
            if (success)
            {
                _autosave.NotifySaved();
                _toasts.Show(ToastSeverity.Success, _text.Get(kind == SaveKind.Auto ? "toast.autosaveCompleted" : "toast.worldSaved"));
                return;
            }
            if (kind == SaveKind.Auto)
            {
                // Az autosave hibája ne blokkoljon modális ablakkal; a következő időköznél újrapróbál.
                _autosave.NotifySaved();
                _toasts.Show(ToastSeverity.Warning, _text.Get("dialog.saveFailed.title") + (string.IsNullOrEmpty(userMessage) ? "" : ": " + userMessage));
                return;
            }
            _dialogs.Show(CommonDialogs.SaveFailed(_text, userMessage ?? ""));
        }

        /// <summary>Frame-enként (unscaled idővel): autosave-időzítés.</summary>
        public void Tick(double unscaledDeltaSeconds)
        {
            _autosave.IntervalMinutes = _settings().Simulation.AutosaveIntervalMinutes;
            bool running = _states.Current == AppState.Simulation;
            _autosave.Tick(unscaledDeltaSeconds, running);
            if (running && _autosave.IsDue && CanStartSave()) RaiseSave(SaveKind.Auto, null);
        }

        // ---- kilépés ----

        public void RequestQuit()
        {
            if (IsQuitConfirmed) return;
            bool unsaved = _states.IsSessionState && _host.HasUnsavedChanges;
            _dialogs.Show(CommonDialogs.QuitToDesktop(_text, unsaved), result =>
            {
                if (result.IsConfirmed) QuitNow(raiseEvent: true);
            });
        }

        /// <summary>
        /// Az <c>Application.wantsToQuit</c> kezelője (ablak bezárása, Alt+F4). Nem mentett
        /// haladásnál megerősítést kér és hamisat ad; különben azonnal engedi.
        /// </summary>
        public bool OnApplicationWantsToQuit()
        {
            if (IsQuitConfirmed) return true;
            if (_states.IsSessionState && _host.HasUnsavedChanges)
            {
                RequestQuit();
                return IsQuitConfirmed;
            }
            QuitNow(raiseEvent: false);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _states.StateChanged -= OnStateChanged;
        }

        // ---- belső ----

        private bool BeginLoad(string filePath)
        {
            if (_states.RequestTransition(AppState.Loading, "load") != TransitionResult.Completed) return false;
            var scope = _sessions.BeginSession(Path.GetFileNameWithoutExtension(filePath));
            LoadRequested?.Invoke(filePath, scope);
            return true;
        }

        private void ReturnToMainMenuNow()
        {
            if (_states.Current == AppState.Paused) _states.RequestTransition(AppState.MainMenu, "return-to-menu");
        }

        private void QuitNow(bool raiseEvent)
        {
            if (IsQuitConfirmed) return;
            IsQuitConfirmed = true;
            _states.RequestTransition(AppState.Quitting, "quit");
            if (raiseEvent) QuitRequested?.Invoke();
        }

        private void ConfirmDiscard(Action action)
        {
            if (!_host.HasUnsavedChanges)
            {
                action();
                return;
            }
            _dialogs.Show(CommonDialogs.UnsavedProgress(_text), result =>
            {
                if (result.IsConfirmed) action();
            });
        }

        private bool CanStartSave() => _host.CanSave && !_saveInProgress.HasValue;

        private void RaiseSave(SaveKind kind, string? path)
        {
            _saveInProgress = kind;
            SaveRequested?.Invoke(kind, path);
        }

        private bool PushScreen(string screenId)
        {
            _navigation.Screens.Push(screenId);
            return true;
        }

        private void OnStateChanged(AppStateChange change)
        {
            var screens = _navigation.Screens;
            switch (change.To)
            {
                case AppState.MainMenu:
                    _sessions.EndSession();
                    _saveInProgress = null;
                    screens.Reset(ScreenIds.MainMenu);
                    break;
                case AppState.WorldCreation:
                    screens.Reset(ScreenIds.NewWorld);
                    break;
                case AppState.Loading:
                    screens.Reset(ScreenIds.Loading);
                    break;
                case AppState.Simulation:
                    screens.Clear();
                    break;
                case AppState.Paused:
                    screens.Reset(ScreenIds.Pause);
                    break;
                case AppState.Quitting:
                    _sessions.EndSession();
                    screens.Clear();
                    break;
            }
        }
    }
}
