using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldGen.App.Flow;
using WorldGen.App.Foundation.Tests.Saves;
using WorldGen.App.Foundation.Tests.WorldSetup;
using WorldGen.App.Lifecycle;
using WorldGen.App.Localization;
using WorldGen.App.Navigation;
using WorldGen.App.Saves;
using WorldGen.App.Settings;
using WorldGen.App.UI;
using WorldGen.App.WorldSetup;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Flow
{
    internal sealed class FakeHost : IWorldSessionHost
    {
        public bool HasUnsavedChanges { get; set; }
        public bool CanSave { get; set; }
        public string? CurrentSavePath { get; set; }
        public string? CurrentWorldId { get; set; }
    }

    internal sealed class FlowHarness
    {
        public readonly AppStateMachine States = new AppStateMachine();
        public readonly SessionManager Sessions = new SessionManager();
        public readonly ToastQueue Toasts = new ToastQueue();
        public readonly BackNavigationRouter Navigation = new BackNavigationRouter();
        public readonly LocalizationTable Text = EnglishStrings.CreateTable();
        public readonly AutosaveScheduler Autosave = new AutosaveScheduler(10);
        public readonly AppSettings Settings = new AppSettings();
        public readonly FakeHost Host = new FakeHost();
        public readonly DialogService Dialogs;
        public readonly AppFlowController Flow;
        public readonly List<string> Events = new List<string>();
        public SessionScope? LastScope;

        public FlowHarness()
        {
            Dialogs = new DialogService(() => Settings.Interface.ShowConfirmations);
            Navigation.PushHandler(Dialogs);
            Flow = new AppFlowController(States, Sessions, Dialogs, Toasts, Navigation, Text, Autosave, () => Settings, Host);
            Flow.NewWorldRequested += (r, s) => { Events.Add("new:" + r.WorldName); LastScope = s; };
            Flow.LoadRequested += (p, s) => { Events.Add("load:" + Path.GetFileName(p)); LastScope = s; };
            Flow.SaveRequested += (k, p) => Events.Add("save:" + k + ":" + (p == null ? "-" : Path.GetFileName(p)));
            Flow.CancelLoadingRequested += () => Events.Add("cancel-loading");
            Flow.QuitRequested += () => Events.Add("quit");
        }

        public IReadOnlyList<string> Screens => Navigation.Screens.Items.ToList();

        public WorldCreationRequest Request(string name)
        {
            var form = new WorldCreationForm(TestSchema.Create(), TestSchema.Presets(), new SequenceEntropy(42));
            form.SetWorldName(name);
            Assert.True(form.TryBuildRequest(out var request, out _));
            return request!;
        }

        public void ToMainMenu() => Flow.EnterMainMenuFromBoot(showWelcome: false);

        public void ToSimulation(string name = "Gaia")
        {
            if (States.Current == AppState.Boot) ToMainMenu();
            Assert.True(Flow.OnMainMenuItem(MenuIds.NewWorld, Array.Empty<SaveSlotInfo>()));
            Assert.True(Flow.StartNewWorld(Request(name)));
            Assert.True(Flow.NotifyWorldReady());
        }

        public static IReadOnlyList<SaveSlotInfo> Slots(params SaveHeader[] headers)
        {
            var fs = new InMemoryFileSystem();
            var repo = new SaveRepository(fs, Path.Combine(InMemoryFileSystem.Root, "Saves"), h => SaveCompatibility.Evaluate(h, 1, "gen-7"));
            foreach (var h in headers) repo.Write(repo.CreateFilePath(h.WorldName, h.Kind, h.LastPlayedUtc), h, SaveSamples.Sections());
            return repo.List();
        }
    }

    public class AppFlowControllerTests
    {
        [Fact]
        public void BootShowsWelcomeOverMainMenuAndBackClosesIt()
        {
            var h = new FlowHarness();
            h.Flow.EnterMainMenuFromBoot(showWelcome: true);
            Assert.Equal(new[] { ScreenIds.MainMenu, ScreenIds.Welcome }, h.Screens);
            Assert.Equal(BackAction.PopScreen, h.Flow.HandleBack());
            Assert.Equal(BackAction.None, h.Flow.HandleBack());
            Assert.Equal(AppState.MainMenu, h.States.Current);
            Assert.Throws<InvalidOperationException>(() => h.Flow.EnterMainMenuFromBoot(false));
        }

        /// <summary>WF-QA-002: Main Menu → A → Main Menu → B → Main Menu → C, szivárgás nélkül.</summary>
        [Fact]
        public void SeveralWorldsInOneProcessLeaveNoLiveSession()
        {
            var h = new FlowHarness();
            h.ToMainMenu();
            var disposed = new List<string>();
            foreach (string world in new[] { "A", "B", "C" })
            {
                Assert.True(h.Flow.OnMainMenuItem(MenuIds.NewWorld, Array.Empty<SaveSlotInfo>()));
                Assert.Equal(new[] { ScreenIds.NewWorld }, h.Screens);
                Assert.True(h.Flow.StartNewWorld(h.Request(world)));
                Assert.Equal(AppState.Loading, h.States.Current);
                h.LastScope!.OnDispose(() => disposed.Add(world));
                Assert.True(h.Flow.NotifyWorldReady());
                Assert.Empty(h.Screens);

                Assert.Equal(BackAction.OpenPauseMenu, h.Flow.HandleBack());
                Assert.Equal(new[] { ScreenIds.Pause }, h.Screens);
                Assert.True(h.Flow.OnPauseMenuItem(MenuIds.ReturnToMainMenu));
                Assert.Equal(AppState.MainMenu, h.States.Current);
                Assert.Equal(0, h.Sessions.LiveSessionCount);
            }
            Assert.Equal(new[] { "A", "B", "C" }, disposed);
            Assert.Equal(3, h.Sessions.TotalSessionsStarted);
            Assert.Equal(new[] { "new:A", "new:B", "new:C" }, h.Events);
        }

        [Fact]
        public void UnsavedProgressNeedsConfirmationBeforeReturningToMenu()
        {
            var h = new FlowHarness();
            h.ToSimulation();
            h.Host.HasUnsavedChanges = true;
            h.Flow.Pause();

            Assert.True(h.Flow.OnPauseMenuItem(MenuIds.ReturnToMainMenu));
            Assert.Equal(CommonDialogs.UnsavedProgressTag, h.Dialogs.Active!.Tag);
            Assert.Equal(BackAction.HandledByOverlay, h.Flow.HandleBack()); // ESC = Cancel
            Assert.Equal(AppState.Paused, h.States.Current);
            Assert.Equal(1, h.Sessions.LiveSessionCount);

            h.Flow.OnPauseMenuItem(MenuIds.ReturnToMainMenu);
            h.Dialogs.Resolve(DialogButton.ConfirmId);
            Assert.Equal(AppState.MainMenu, h.States.Current);
            Assert.Equal(0, h.Sessions.LiveSessionCount);
        }

        [Fact]
        public void ContinueLoadsMostRecentCompatibleSave()
        {
            var h = new FlowHarness();
            h.ToMainMenu();
            Assert.False(h.Flow.OnMainMenuItem(MenuIds.Continue, Array.Empty<SaveSlotInfo>()));
            Assert.Equal(AppState.MainMenu, h.States.Current);

            var t = SaveSamples.Created;
            var slots = FlowHarness.Slots(SaveSamples.Header("Old", t), SaveSamples.Header("Recent", t.AddDays(1)),
                SaveSamples.Header("Outdated", t.AddDays(2), generator: "gen-6"));
            Assert.True(h.Flow.OnMainMenuItem(MenuIds.Continue, slots));
            Assert.Equal(AppState.Loading, h.States.Current);
            Assert.Equal(new[] { "load:Recent.wgsave" }, h.Events);
            Assert.Equal(1, h.Sessions.LiveSessionCount);
        }

        [Fact]
        public void LoadingFromPausedWorldReplacesSessionAfterConfirmation()
        {
            var h = new FlowHarness();
            h.ToSimulation();
            var first = h.LastScope!;
            h.Flow.Pause();
            h.Host.HasUnsavedChanges = true;
            var slot = FlowHarness.Slots(SaveSamples.Header("Terra")).Single();

            Assert.True(h.Flow.LoadSave(slot));
            Assert.Equal(AppState.Paused, h.States.Current);
            h.Dialogs.Resolve(DialogButton.ConfirmId);

            Assert.Equal(AppState.Loading, h.States.Current);
            Assert.True(first.IsDisposed);
            Assert.NotSame(first, h.LastScope);
            Assert.Equal(1, h.Sessions.LiveSessionCount);

            var incompatible = FlowHarness.Slots(SaveSamples.Header("New", generator: "gen-6")).Single();
            Assert.False(h.Flow.LoadSave(incompatible));
        }

        [Fact]
        public void LoadingFailureAndCancellationReturnToMenu()
        {
            var h = new FlowHarness();
            h.ToMainMenu();
            h.Flow.OnMainMenuItem(MenuIds.NewWorld, Array.Empty<SaveSlotInfo>());
            h.Flow.StartNewWorld(h.Request("Broken"));
            h.Flow.NotifyLoadingFailed("Out of memory.");

            Assert.Equal(AppState.MainMenu, h.States.Current);
            Assert.Equal(0, h.Sessions.LiveSessionCount);
            Assert.Equal("Load Failed", h.Dialogs.Active!.Title);
            Assert.Contains("Out of memory.", h.Dialogs.Active.Message);
            h.Dialogs.DismissAll();

            h.Flow.OnMainMenuItem(MenuIds.NewWorld, Array.Empty<SaveSlotInfo>());
            h.Flow.StartNewWorld(h.Request("Slow"));
            Assert.Equal(BackAction.CancelLoading, h.Flow.HandleBack());
            Assert.Contains("cancel-loading", h.Events);
            h.Flow.NotifyLoadingCancelled();
            Assert.Equal(AppState.MainMenu, h.States.Current);
            Assert.False(h.Flow.NotifyWorldReady());
        }

        [Fact]
        public void BackFromWorldCreationReturnsToMainMenu()
        {
            var h = new FlowHarness();
            h.ToMainMenu();
            h.Flow.OnMainMenuItem(MenuIds.NewWorld, Array.Empty<SaveSlotInfo>());
            Assert.Equal(BackAction.ReturnToMainMenu, h.Flow.HandleBack());
            Assert.Equal(AppState.MainMenu, h.States.Current);
            Assert.Equal(new[] { ScreenIds.MainMenu }, h.Screens);
        }

        [Fact]
        public void ManualSaveUsesCurrentPathOrSaveAsAndBlocksConcurrentSaves()
        {
            var h = new FlowHarness();
            h.ToSimulation();
            h.Flow.Pause();

            Assert.False(h.Flow.OnPauseMenuItem(MenuIds.SaveWorld)); // CanSave = false (ND-108 előtt)
            h.Host.CanSave = true;
            Assert.True(h.Flow.OnPauseMenuItem(MenuIds.SaveWorld));
            Assert.Equal(new[] { ScreenIds.Pause, ScreenIds.SaveAs }, h.Screens);

            string path = Path.Combine(InMemoryFileSystem.Root, "Saves", "Gaia.wgsave");
            Assert.True(h.Flow.SaveTo(path));
            Assert.Equal(new[] { ScreenIds.Pause }, h.Screens);
            Assert.True(h.Flow.IsSaveInProgress);
            Assert.False(h.Flow.QuickSave());

            h.Flow.NotifySaveCompleted(SaveKind.Manual, true, null);
            Assert.Equal("World saved", h.Toasts.Visible.Single().Message);

            h.Host.CurrentSavePath = path;
            Assert.True(h.Flow.OnPauseMenuItem(MenuIds.SaveWorld));
            h.Flow.NotifySaveCompleted(SaveKind.Manual, false, "Disk full.");
            Assert.Equal("Save Failed", h.Dialogs.Active!.Title);

            Assert.Equal(new[] { "new:Gaia", "save:Manual:Gaia.wgsave", "save:Manual:Gaia.wgsave" }, h.Events);
        }

        [Fact]
        public void AutosaveFiresOnlyWhileSimulatingAndOnlyOnce()
        {
            var h = new FlowHarness();
            h.Settings.Simulation.AutosaveIntervalMinutes = 5;
            h.ToSimulation();

            h.Flow.Tick(299);
            Assert.DoesNotContain(h.Events, e => e.StartsWith("save"));
            h.Flow.Tick(1); // esedékes, de még nem menthető
            Assert.DoesNotContain(h.Events, e => e.StartsWith("save"));

            h.Host.CanSave = true;
            h.Flow.Pause();
            h.Flow.Tick(1000);
            Assert.DoesNotContain(h.Events, e => e.StartsWith("save"));
            h.Flow.Resume();
            h.Flow.Tick(0.016);
            h.Flow.Tick(0.016);
            Assert.Single(h.Events, e => e == "save:Auto:-");

            h.Flow.NotifySaveCompleted(SaveKind.Auto, true, null);
            Assert.Equal("Autosave completed", h.Toasts.Visible.Single().Message);
            h.Flow.Tick(299);
            Assert.Single(h.Events, e => e == "save:Auto:-");

            h.Flow.Tick(1);
            h.Flow.NotifySaveCompleted(SaveKind.Auto, false, "Disk full.");
            Assert.False(h.Dialogs.IsOpen);
            Assert.Contains(h.Toasts.Visible, t => t.Severity == ToastSeverity.Warning);
        }

        [Fact]
        public void QuitFromMenuConfirmsAndRespectsDisabledConfirmations()
        {
            var h = new FlowHarness();
            h.ToMainMenu();
            Assert.True(h.Flow.OnMainMenuItem(MenuIds.Quit, Array.Empty<SaveSlotInfo>()));
            Assert.Equal(CommonDialogs.QuitTag, h.Dialogs.Active!.Tag);
            h.Dialogs.Resolve(DialogButton.CancelId);
            Assert.Equal(AppState.MainMenu, h.States.Current);

            h.Settings.Interface.ShowConfirmations = false;
            h.Flow.OnMainMenuItem(MenuIds.Quit, Array.Empty<SaveSlotInfo>());
            Assert.Equal(AppState.Quitting, h.States.Current);
            Assert.True(h.Flow.IsQuitConfirmed);
            Assert.Equal(new[] { "quit" }, h.Events);
        }

        [Fact]
        public void WindowCloseAsksOnlyWhenProgressWouldBeLost()
        {
            var clean = new FlowHarness();
            clean.ToSimulation();
            Assert.True(clean.Flow.OnApplicationWantsToQuit());
            Assert.Equal(AppState.Quitting, clean.States.Current);
            Assert.Equal(0, clean.Sessions.LiveSessionCount);
            Assert.DoesNotContain("quit", clean.Events);

            var dirty = new FlowHarness();
            dirty.ToSimulation();
            dirty.Host.HasUnsavedChanges = true;
            Assert.False(dirty.Flow.OnApplicationWantsToQuit());
            Assert.True(dirty.Dialogs.IsOpen);
            dirty.Dialogs.Resolve(DialogButton.ConfirmId);
            Assert.Contains("quit", dirty.Events);
            Assert.True(dirty.Flow.OnApplicationWantsToQuit());
        }

        [Fact]
        public void QuickSaveQuickLoadAndDeepTimeAutosave()
        {
            var h = new FlowHarness();
            h.ToSimulation();
            h.Host.CanSave = true;
            h.Host.CurrentWorldId = "0123456789abcdef0123456789abcdef";

            Assert.True(h.Flow.QuickSave());
            h.Flow.NotifySaveCompleted(SaveKind.Quick, true, null);

            var t = SaveSamples.Created;
            var slots = FlowHarness.Slots(
                SaveSamples.Header("Gaia", t, SaveKind.Quick),
                SaveSamples.Header("Other", t.AddDays(1), SaveKind.Quick, worldId: "ffffffffffffffffffffffffffffffff"));
            Assert.True(h.Flow.QuickLoad(slots));
            Assert.Contains("load:Gaia - Quicksave.wgsave", h.Events);

            var again = new FlowHarness();
            again.ToSimulation();
            again.Host.CanSave = true;
            Assert.False(again.Flow.BeforeDeepTimeJump());
            again.Settings.Simulation.AutosaveBeforeDeepTimeJump = true;
            Assert.True(again.Flow.BeforeDeepTimeJump());
            Assert.Contains("save:Auto:-", again.Events);
            Assert.False(again.Flow.QuickLoad(slots)); // nincs világazonosító
        }

        [Fact]
        public void ActionsInWrongStateAreIgnoredAndDisposeUnsubscribes()
        {
            var h = new FlowHarness();
            h.ToMainMenu();
            Assert.False(h.Flow.OnPauseMenuItem(MenuIds.Resume));
            Assert.False(h.Flow.OnMainMenuItem("unknown", Array.Empty<SaveSlotInfo>()));
            Assert.False(h.Flow.StartNewWorld(h.Request("x")));
            Assert.False(h.Flow.Pause());
            Assert.True(h.Flow.OnMainMenuItem(MenuIds.Settings, Array.Empty<SaveSlotInfo>()));
            Assert.True(h.Flow.PopScreen());

            h.Flow.Dispose();
            h.States.RequestTransition(AppState.WorldCreation);
            Assert.Equal(new[] { ScreenIds.MainMenu }, h.Screens);
        }
    }
}
