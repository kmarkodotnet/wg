using System;
using System.Collections.Generic;
using WorldGen.App.Lifecycle;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Lifecycle
{
    public class AppStateMachineTests
    {
        private static readonly AppState[] AllStates = (AppState[])Enum.GetValues(typeof(AppState));

        [Fact]
        public void TransitionTableMatchesArchitectureDocument()
        {
            var allowed = new HashSet<(AppState, AppState)>
            {
                (AppState.Boot, AppState.MainMenu),
                (AppState.MainMenu, AppState.WorldCreation),
                (AppState.MainMenu, AppState.Loading),
                (AppState.WorldCreation, AppState.MainMenu),
                (AppState.WorldCreation, AppState.Loading),
                (AppState.Loading, AppState.Simulation),
                (AppState.Loading, AppState.MainMenu),
                (AppState.Simulation, AppState.Paused),
                (AppState.Simulation, AppState.Loading),
                (AppState.Paused, AppState.Simulation),
                (AppState.Paused, AppState.MainMenu),
                (AppState.Paused, AppState.Loading),
            };
            foreach (var from in AllStates)
            {
                if (from != AppState.Quitting) allowed.Add((from, AppState.Quitting));
            }

            foreach (var from in AllStates)
            {
                foreach (var to in AllStates)
                {
                    Assert.True(allowed.Contains((from, to)) == AppStateMachine.IsTransitionAllowed(from, to),
                        from + " -> " + to);
                }
            }
        }

        [Fact]
        public void SeveralWorldsCanBeStartedInOneProcess()
        {
            var machine = new AppStateMachine();
            var changes = new List<AppStateChange>();
            machine.StateChanged += changes.Add;

            Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.MainMenu));
            for (int world = 0; world < 3; world++)
            {
                Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.WorldCreation));
                Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.Loading));
                Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.Simulation));
                Assert.True(machine.IsSessionState);
                Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.Paused));
                Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.MainMenu, "return"));
                Assert.False(machine.IsSessionState);
            }
            Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.Quitting));

            Assert.Equal(1 + 3 * 5 + 1, changes.Count);
            Assert.Equal(AppState.Quitting, machine.Current);
            Assert.Equal("Paused -> MainMenu (return)", changes[5].ToString());
        }

        [Fact]
        public void RejectedTransitionKeepsStateAndRaisesEvent()
        {
            var machine = new AppStateMachine();
            var rejected = new List<string>();
            machine.TransitionRejected += (from, to, reason) => rejected.Add(from + ">" + to + ":" + reason);
            int changes = 0;
            machine.StateChanged += _ => changes++;

            Assert.Equal(TransitionResult.Rejected, machine.RequestTransition(AppState.Simulation, "skip"));
            Assert.Equal(AppState.Boot, machine.Current);
            Assert.Equal(0, changes);
            Assert.Equal(new[] { "Boot>Simulation:skip" }, rejected);
        }

        [Fact]
        public void QuittingIsTerminalAndSameStateIsRejected()
        {
            var machine = new AppStateMachine(AppState.MainMenu);
            Assert.Equal(TransitionResult.Rejected, machine.RequestTransition(AppState.MainMenu));
            Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.Quitting));
            foreach (var target in AllStates)
                Assert.Equal(TransitionResult.Rejected, machine.RequestTransition(target));
        }

        [Fact]
        public void TransitionRequestedFromHandlerIsQueuedAndRunsAfterCurrentNotification()
        {
            var machine = new AppStateMachine(AppState.MainMenu);
            var log = new List<string>();
            TransitionResult? inner = null;
            machine.StateChanged += change =>
            {
                log.Add("enter " + change.To);
                if (change.To == AppState.Loading) inner = machine.RequestTransition(AppState.Simulation, "loaded");
                log.Add("leave " + change.To);
            };

            Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.Loading));
            Assert.Equal(TransitionResult.Queued, inner);
            Assert.Equal(AppState.Simulation, machine.Current);
            Assert.Equal(new[] { "enter Loading", "leave Loading", "enter Simulation", "leave Simulation" }, log);
        }

        [Fact]
        public void QueuedTransitionIsValidatedAgainstStateAtExecutionTime()
        {
            var machine = new AppStateMachine(AppState.MainMenu);
            var rejected = new List<AppState>();
            machine.TransitionRejected += (_, to, __) => rejected.Add(to);
            machine.StateChanged += change =>
            {
                if (change.To == AppState.Loading)
                {
                    machine.RequestTransition(AppState.Simulation);
                    machine.RequestTransition(AppState.WorldCreation); // Simulation → WorldCreation nem engedett
                }
            };

            machine.RequestTransition(AppState.Loading);
            Assert.Equal(AppState.Simulation, machine.Current);
            Assert.Equal(new[] { AppState.WorldCreation }, rejected);
        }

        [Fact]
        public void HandlerExceptionClearsQueueAndMachineStaysUsable()
        {
            var machine = new AppStateMachine(AppState.MainMenu);
            bool throwOnce = true;
            machine.StateChanged += change =>
            {
                if (change.To != AppState.Loading || !throwOnce) return;
                throwOnce = false;
                machine.RequestTransition(AppState.Simulation);
                throw new InvalidOperationException("boom");
            };

            Assert.Throws<InvalidOperationException>(() => machine.RequestTransition(AppState.Loading));
            Assert.Equal(AppState.Loading, machine.Current);

            // A sorba állított Simulation eldobódott; új kérés normálisan fut.
            Assert.Equal(TransitionResult.Completed, machine.RequestTransition(AppState.MainMenu));
            Assert.Equal(AppState.MainMenu, machine.Current);
        }
    }
}
