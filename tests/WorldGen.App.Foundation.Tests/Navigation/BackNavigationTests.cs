using System;
using System.Collections.Generic;
using WorldGen.App.Lifecycle;
using WorldGen.App.Navigation;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Navigation
{
    public class BackNavigationTests
    {
        private sealed class Handler : IBackHandler
        {
            private readonly Func<bool> _action;

            public Handler(Func<bool> action)
            {
                _action = action;
            }

            public int Calls { get; private set; }

            public bool TryHandleBack()
            {
                Calls++;
                return _action();
            }
        }

        [Theory]
        [InlineData(AppState.Boot, BackAction.None)]
        [InlineData(AppState.MainMenu, BackAction.None)]
        [InlineData(AppState.WorldCreation, BackAction.ReturnToMainMenu)]
        [InlineData(AppState.Loading, BackAction.CancelLoading)]
        [InlineData(AppState.Simulation, BackAction.OpenPauseMenu)]
        [InlineData(AppState.Paused, BackAction.ResumeSimulation)]
        [InlineData(AppState.Quitting, BackAction.None)]
        public void DefaultActionPerState(AppState state, BackAction expected)
        {
            Assert.Equal(expected, BackNavigationRouter.DefaultFor(state));
            Assert.Equal(expected, new BackNavigationRouter().Route(state));
        }

        [Fact]
        public void LatestHandlerWinsAndNonConsumingHandlerPassesThrough()
        {
            var router = new BackNavigationRouter();
            var dialog = new Handler(() => true);
            var passive = new Handler(() => false);
            router.PushHandler(dialog);
            router.PushHandler(passive);

            Assert.Equal(BackAction.HandledByOverlay, router.Route(AppState.Simulation));
            Assert.Equal(1, passive.Calls);
            Assert.Equal(1, dialog.Calls);
        }

        [Fact]
        public void DisposingRegistrationRemovesHandlerOutOfOrder()
        {
            var router = new BackNavigationRouter();
            var a = router.PushHandler(new Handler(() => true));
            var b = router.PushHandler(new Handler(() => true));
            a.Dispose();
            a.Dispose();
            Assert.Equal(1, router.HandlerCount);
            b.Dispose();
            Assert.Equal(0, router.HandlerCount);
            Assert.Equal(BackAction.OpenPauseMenu, router.Route(AppState.Simulation));
        }

        [Fact]
        public void HandlerMayRemoveItselfWhileHandling()
        {
            var router = new BackNavigationRouter();
            IDisposable? token = null;
            token = router.PushHandler(new Handler(() =>
            {
                token!.Dispose();
                return true;
            }));

            Assert.Equal(BackAction.HandledByOverlay, router.Route(AppState.Paused));
            Assert.Equal(BackAction.ResumeSimulation, router.Route(AppState.Paused));
        }

        [Fact]
        public void ScreenStackIsUnwoundBeforeStateDefault()
        {
            var router = new BackNavigationRouter();
            router.Screens.Reset("pause");
            router.Screens.Push("settings");
            router.Screens.Push("graphics");

            Assert.Equal(BackAction.PopScreen, router.Route(AppState.Paused));
            Assert.Equal("settings", router.Screens.Current);
            Assert.Equal(BackAction.PopScreen, router.Route(AppState.Paused));
            Assert.Equal(BackAction.ResumeSimulation, router.Route(AppState.Paused));
            Assert.Equal("pause", router.Screens.Current);
        }

        [Fact]
        public void ScreenStackRules()
        {
            var stack = new ScreenStack();
            int changes = 0;
            stack.Changed += _ => changes++;

            Assert.Throws<InvalidOperationException>(() => stack.Push("settings"));
            Assert.Null(stack.Pop());

            stack.Reset("main");
            stack.Push("settings");
            stack.Push("settings");
            Assert.Equal(2, stack.Count);
            Assert.Equal("main", stack.Root);
            Assert.True(stack.Contains("settings"));

            stack.Reset("pause");
            Assert.Equal(new List<string> { "pause" }, stack.Items);
            Assert.Null(stack.Pop());
            Assert.Throws<ArgumentException>(() => stack.Push(""));
            Assert.Equal(3, changes);
        }
    }
}
