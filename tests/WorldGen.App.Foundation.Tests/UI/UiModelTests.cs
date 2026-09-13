using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.App.Localization;
using WorldGen.App.Navigation;
using WorldGen.App.Serialization;
using WorldGen.App.UI;
using Xunit;

namespace WorldGen.App.Foundation.Tests.UI
{
    public class LocalizationTableTests
    {
        [Fact]
        public void FallsBackAndMarksMissingKeys()
        {
            var table = new LocalizationTable("hu", "en");
            table.Set("en", "menu.quit", "Quit");
            table.Set("en", "menu.help", "Help");
            table.Set("HU", "menu.quit", "Kilépés");

            Assert.Equal("Kilépés", table.Get("menu.quit"));
            Assert.Equal("Help", table.Get("menu.help"));
            Assert.Equal("[menu.credits]", table.Get("menu.credits"));
            Assert.Equal(new[] { "menu.credits" }, table.MissingKeys);
            Assert.False(table.TryGet("menu.credits", out _));
            Assert.Equal(new[] { "menu.help" }, table.FindKeysMissingIn("hu"));

            table.CurrentLanguage = "";
            Assert.Equal("en", table.CurrentLanguage);
            Assert.Equal("Quit", table.Get("menu.quit"));
        }

        [Fact]
        public void FormatsInvariantlyAndSurvivesBadTemplates()
        {
            var table = new LocalizationTable();
            table.Set("en", "age", "Age {0:0.00} Ga");
            table.Set("en", "broken", "Value {1");
            Assert.Equal("Age 2.73 Ga", table.Format("age", 2.7312));
            Assert.Equal("Value {1", table.Format("broken", 5));
        }

        [Fact]
        public void LoadsNestedJsonAndReportsNonStrings()
        {
            var table = new LocalizationTable();
            var doc = JsonParser.Parse("{\"menu\": {\"main\": {\"quit\": \"Quit\"}, \"count\": 3}, \"title\": \"WORLDGEN\"}");
            var rejected = table.LoadLanguage("en", doc);
            Assert.Equal(new[] { "menu.count" }, rejected);
            Assert.Equal("Quit", table.Get("menu.main.quit"));
            Assert.Equal("WORLDGEN", table.Get("title"));
            Assert.Equal(new[] { "$" }, table.LoadLanguage("en", JsonValue.CreateArray()));
        }

        [Fact]
        public void EnglishStringsCoverAllFoundationKeys()
        {
            var table = EnglishStrings.CreateTable();
            var keys = MainMenuDefinition.Build(true, true).Items.Select(i => i.LabelKey)
                .Concat(PauseMenuDefinition.Build(true, true).Items.Select(i => i.LabelKey));
            foreach (string key in keys) Assert.True(table.TryGet(key, out _), key);

            var dialogs = new[]
            {
                CommonDialogs.UnsavedProgress(table),
                CommonDialogs.QuitToDesktop(table, true),
                CommonDialogs.QuitToDesktop(table, false),
                CommonDialogs.DeleteSave(table, "Gaia"),
                CommonDialogs.SaveFailed(table, "Disk full."),
                CommonDialogs.LoadFailed(table, "Damaged."),
            };
            foreach (var d in dialogs)
            {
                Assert.DoesNotContain("[", d.Title);
                Assert.DoesNotContain("[", d.Message);
                Assert.All(d.Buttons, b => Assert.DoesNotContain("[", b.Label));
            }
            Assert.Empty(table.MissingKeys);
        }
    }

    public class DialogServiceTests
    {
        private static DialogRequest Info(string title, string? tag = null) => DialogRequest.Information(title, "m", "OK", tag);

        [Fact]
        public void QueuesDialogsAndResolvesInOrder()
        {
            var service = new DialogService();
            var log = new List<string>();
            service.ActiveChanged += r => log.Add("active:" + (r?.Title ?? "none"));

            Assert.True(service.Show(DialogRequest.Confirm("A", "m", "Yes", "No"), r => log.Add("closed A:" + r.ButtonId + ":" + r.IsConfirmed)));
            Assert.True(service.Show(Info("B"), r => log.Add("closed B:" + r.ButtonId)));
            Assert.Equal("A", service.Active!.Title);
            Assert.Equal(1, service.PendingCount);

            Assert.False(service.Resolve("missing"));
            Assert.True(service.Resolve(DialogButton.ConfirmId));
            Assert.True(service.Resolve(DialogButton.OkId));
            Assert.False(service.IsOpen);

            Assert.Equal(new[] { "active:A", "active:B", "closed A:confirm:True", "active:none", "closed B:ok" }, log);
        }

        [Fact]
        public void EscapeCancelsOrClosesAndIsAlwaysConsumedWhileOpen()
        {
            var service = new DialogService();
            var router = new BackNavigationRouter();
            router.PushHandler(service);
            DialogResult? result = null;

            service.Show(DialogRequest.Confirm("Unsaved", "m", "Continue", "Cancel", destructive: true), r => result = r);
            Assert.Equal(BackAction.HandledByOverlay, router.Route(WorldGen.App.Lifecycle.AppState.Paused));
            Assert.True(result!.Value.WasCancelled);
            Assert.False(result.Value.IsConfirmed);

            service.Show(Info("Note"), r => result = r);
            Assert.True(service.CancelActive());
            Assert.Equal(DialogButton.OkId, result!.Value.ButtonId);

            var noCancel = new DialogRequest(DialogKind.Warning, "Pick", "m",
                new[] { new DialogButton("a", "A"), new DialogButton("b", "B") });
            service.Show(noCancel);
            Assert.False(service.CancelActive());
            Assert.True(service.TryHandleBack());
            Assert.True(service.IsOpen);
            Assert.Equal(BackAction.ResumeSimulation, new BackNavigationRouter().Route(WorldGen.App.Lifecycle.AppState.Paused));
        }

        [Fact]
        public void SameTagIsNotQueuedTwice()
        {
            var service = new DialogService();
            Assert.True(service.Show(Info("A", "quit")));
            Assert.True(service.Show(Info("B")));
            Assert.True(service.Show(Info("C", "save")));
            Assert.False(service.Show(Info("A2", "quit")));
            Assert.False(service.Show(Info("C2", "save")));
            Assert.Equal(2, service.PendingCount);
        }

        [Fact]
        public void SkippableDialogIsAutoConfirmedWhenConfirmationsAreDisabled()
        {
            bool enabled = false;
            var service = new DialogService(() => enabled);
            var table = EnglishStrings.CreateTable();
            DialogResult? result = null;

            Assert.True(service.Show(CommonDialogs.UnsavedProgress(table), r => result = r));
            Assert.False(service.IsOpen);
            Assert.True(result!.Value.WasAutoConfirmed);
            Assert.True(result.Value.IsConfirmed);
            Assert.Equal(DialogButton.ConfirmId, result.Value.ButtonId);

            result = null;
            service.Show(CommonDialogs.DeleteSave(table, "Gaia"), r => result = r);
            Assert.True(service.IsOpen);
            Assert.Null(result);
            Assert.Contains("\"Gaia\"", service.Active!.Message);

            enabled = true;
            service.DismissAll();
            service.Show(CommonDialogs.UnsavedProgress(table));
            Assert.True(service.IsOpen);
        }

        [Fact]
        public void DismissAllCancelsEverything()
        {
            var service = new DialogService();
            var results = new List<DialogResult>();
            service.Show(DialogRequest.Confirm("A", "m", "Y", "N"), results.Add);
            service.Show(Info("B"), results.Add);
            service.DismissAll();
            Assert.False(service.IsOpen);
            Assert.Equal(0, service.PendingCount);
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.WasCancelled));
        }

        [Fact]
        public void CallbackMayOpenFollowUpDialog()
        {
            var service = new DialogService();
            service.Show(DialogRequest.Confirm("Save failed?", "m", "Y", "N"),
                _ => service.Show(Info("Details")));
            service.Resolve(DialogButton.ConfirmId);
            Assert.Equal("Details", service.Active!.Title);
        }

        [Fact]
        public void RequestValidation()
        {
            Assert.Throws<ArgumentException>(() => new DialogRequest(DialogKind.Information, "t", "m", Array.Empty<DialogButton>()));
            Assert.Throws<ArgumentException>(() => new DialogRequest(DialogKind.Information, "t", "m",
                new[] { new DialogButton("a", "A"), new DialogButton("a", "B") }));
            Assert.Throws<ArgumentException>(() => new DialogRequest(DialogKind.Information, "t", "m",
                new[] { new DialogButton("a", "A", DialogButtonRole.Cancel), new DialogButton("b", "B", DialogButtonRole.Cancel) }));
            var onlyCancel = new DialogRequest(DialogKind.Information, "t", "m", new[] { new DialogButton("x", "X", DialogButtonRole.Cancel) });
            Assert.Equal("x", onlyCancel.PrimaryButton.Id);
        }
    }

    public class ToastQueueTests
    {
        [Fact]
        public void ShowsUpToMaxAndPromotesPendingOnExpiry()
        {
            var queue = new ToastQueue(maxVisible: 2);
            int changes = 0;
            queue.Changed += () => changes++;
            queue.Show(ToastSeverity.Success, "World saved");
            queue.Show(ToastSeverity.Error, "Save failed");
            queue.Show(ToastSeverity.Info, "Screenshot saved");
            Assert.Equal(2, queue.Visible.Count);
            Assert.Equal(1, queue.PendingCount);

            queue.Tick(3.0); // a success (3 s) lejár, a pending info bekerül
            Assert.Equal(new[] { "Save failed", "Screenshot saved" }, queue.Visible.Select(t => t.Message));
            Assert.Equal(0, queue.PendingCount);
            queue.Tick(4.0); // az info (3 s) lejár, az error (8 s, még 5 s) marad
            Assert.Equal(new[] { "Save failed" }, queue.Visible.Select(t => t.Message));
            Assert.Equal(5, changes);
        }

        [Fact]
        public void IdenticalMessagesMergeAndRestartTimer()
        {
            var queue = new ToastQueue();
            int id = queue.Show(ToastSeverity.Info, "Simulation paused");
            queue.Tick(2.5);
            Assert.Equal(id, queue.Show(ToastSeverity.Info, "Simulation paused"));
            Assert.NotEqual(id, queue.Show(ToastSeverity.Warning, "Simulation paused"));

            var toast = queue.Visible[0];
            Assert.Equal(2, toast.RepeatCount);
            Assert.Equal(3.0, toast.RemainingSeconds);
        }

        [Fact]
        public void DismissAndOverflow()
        {
            var queue = new ToastQueue(maxVisible: 1, maxPending: 2);
            int a = queue.Show(ToastSeverity.Info, "a");
            queue.Show(ToastSeverity.Info, "b");
            int c = queue.Show(ToastSeverity.Info, "c");
            queue.Show(ToastSeverity.Info, "d"); // "b" kiesik
            Assert.Equal(1, queue.DroppedCount);

            Assert.True(queue.Dismiss(c));
            Assert.True(queue.Dismiss(a));
            Assert.Equal("d", queue.Visible[0].Message);
            Assert.False(queue.Dismiss(a));

            var none = new ToastQueue(maxVisible: 1, maxPending: 0);
            none.Show(ToastSeverity.Info, "x");
            Assert.Equal(0, none.Show(ToastSeverity.Info, "y"));
            queue.Clear();
            Assert.Empty(queue.Visible);
        }

        [Fact]
        public void DurationsAndValidation()
        {
            Assert.Equal(3.0, ToastQueue.DefaultDurationSeconds(ToastSeverity.Info));
            Assert.Equal(3.0, ToastQueue.DefaultDurationSeconds(ToastSeverity.Success));
            Assert.Equal(5.0, ToastQueue.DefaultDurationSeconds(ToastSeverity.Warning));
            Assert.Equal(8.0, ToastQueue.DefaultDurationSeconds(ToastSeverity.Error));
            var queue = new ToastQueue();
            Assert.Throws<ArgumentException>(() => queue.Show(ToastSeverity.Info, ""));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.Show(ToastSeverity.Info, "x", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToastQueue(0));
        }
    }

    public class TooltipTests
    {
        [Fact]
        public void CatalogLoadsFromJson()
        {
            var doc = JsonParser.Parse(
                "{\"param.tectonicActivity\": {\"title\": \"Tectonic Activity\", \"summary\": \"Plate motion rate.\", \"details\": \"Higher values...\"}," +
                " \"overlay.temperature\": {\"title\": \"Temperature\"}," +
                " \"broken\": {\"summary\": \"no title\"}, \"alsoBroken\": 5}");
            var catalog = TooltipCatalog.FromJson(doc, out var rejected);

            Assert.Equal(2, catalog.Count);
            Assert.Equal(new[] { "broken", "alsoBroken" }, rejected);
            Assert.True(catalog.TryGet("param.tectonicActivity", out var content));
            Assert.True(content!.HasDetails);
            Assert.True(catalog.TryGet("overlay.temperature", out var plain));
            Assert.False(plain!.HasDetails);
            Assert.Throws<InvalidOperationException>(() => catalog.Add("overlay.temperature", new TooltipContent("x", "y")));
        }

        [Fact]
        public void DelayThenSummaryThenDetails()
        {
            var tracker = new TooltipHoverTracker(showDelaySeconds: 0.5, detailDelaySeconds: 1.5);
            tracker.SetTarget("param.seed");
            Assert.Equal(TooltipVisibility.Hidden, tracker.Visibility);
            tracker.Tick(0.4);
            Assert.Equal(TooltipVisibility.Hidden, tracker.Visibility);
            tracker.Tick(0.1);
            Assert.Equal(TooltipVisibility.Summary, tracker.Visibility);
            tracker.Tick(1.4);
            Assert.Equal(TooltipVisibility.Summary, tracker.Visibility);
            tracker.Tick(0.1);
            Assert.Equal(TooltipVisibility.Detailed, tracker.Visibility);
        }

        [Fact]
        public void WarmHandoffShowsNextTooltipImmediately()
        {
            var tracker = new TooltipHoverTracker(0.5);
            tracker.SetTarget("a");
            tracker.Tick(0.6);
            tracker.SetTarget("b");
            Assert.Equal(TooltipVisibility.Summary, tracker.Visibility);

            tracker.SetTarget(null);
            tracker.Tick(0.2);
            tracker.SetTarget("c");
            Assert.Equal(TooltipVisibility.Summary, tracker.Visibility);

            tracker.SetTarget(null);
            tracker.Tick(0.31);
            tracker.SetTarget("d");
            Assert.Equal(TooltipVisibility.Hidden, tracker.Visibility);
        }

        [Fact]
        public void ExpandDetailsAndZeroDelay()
        {
            var tracker = new TooltipHoverTracker(0);
            tracker.ExpandDetails();
            Assert.Equal(TooltipVisibility.Hidden, tracker.Visibility);
            tracker.SetTarget("a");
            Assert.Equal(TooltipVisibility.Summary, tracker.Visibility);
            tracker.ExpandDetails();
            Assert.Equal(TooltipVisibility.Detailed, tracker.Visibility);
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.ShowDelaySeconds = -1);
        }
    }

    public class MenuModelTests
    {
        [Fact]
        public void MainMenuWithoutSavesDisablesContinueAndLoad()
        {
            var menu = MainMenuDefinition.Build(hasContinuableSave: false, hasAnySave: false);
            Assert.Equal(MenuIds.NewWorld, menu.Selected!.Id);
            Assert.False(menu.Items[menu.IndexOf(MenuIds.Continue)].IsEnabled);

            Assert.True(menu.MoveNext());
            Assert.Equal(MenuIds.Settings, menu.Selected!.Id);
            Assert.True(menu.MovePrevious());
            Assert.True(menu.MovePrevious());
            Assert.Equal(MenuIds.Quit, menu.Selected!.Id);

            Assert.False(menu.Hover(menu.IndexOf(MenuIds.Continue)));
            Assert.Null(menu.ActivateAt(menu.IndexOf(MenuIds.LoadWorld)));
            Assert.Equal(MenuIds.Quit, menu.Activate());
        }

        [Fact]
        public void MainMenuWithSaveSelectsContinue()
        {
            var menu = MainMenuDefinition.Build(hasContinuableSave: true, hasAnySave: true);
            Assert.Equal(MenuIds.Continue, menu.Activate());
            Assert.Equal(MenuIds.Help, menu.ActivateAt(menu.IndexOf(MenuIds.Help)));
            Assert.Equal(MenuIds.Help, menu.Selected!.Id);
        }

        [Fact]
        public void DisablingSelectedItemMovesSelection()
        {
            var menu = new MenuModel(new[] { new MenuItem("a", "A"), new MenuItem("b", "B"), new MenuItem("c", "C") });
            int selectionChanges = 0;
            menu.SelectionChanged += _ => selectionChanges++;
            menu.Select("b");
            menu.SetEnabled("b", false);
            Assert.Equal("c", menu.Selected!.Id);
            menu.SetVisible("c", false);
            Assert.Equal("a", menu.Selected!.Id);
            menu.SetEnabled("a", false);
            Assert.Equal(-1, menu.SelectedIndex);
            Assert.Null(menu.Activate());
            Assert.False(menu.MoveNext());

            menu.SetEnabled("b", true);
            Assert.Equal("b", menu.Selected!.Id);
            Assert.Equal(5, selectionChanges);
            Assert.Throws<ArgumentException>(() => menu.SetEnabled("zzz", true));
        }

        [Fact]
        public void PauseMenuRespectsCanSave()
        {
            var menu = PauseMenuDefinition.Build(canSave: false, hasAnySave: true);
            Assert.Equal(MenuIds.Resume, menu.Selected!.Id);
            Assert.True(menu.MoveNext());
            Assert.Equal(MenuIds.LoadWorld, menu.Selected!.Id);
            Assert.Throws<ArgumentException>(() => new MenuModel(new[] { new MenuItem("a", "A"), new MenuItem("a", "B") }));
        }
    }
}
