#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.Lifecycle
{
    /// <summary>
    /// Centralizált állapotváltás (WF-APP-001). Minden váltás a
    /// <see cref="RequestTransition"/>-ön megy át; nem engedett váltás nem
    /// kivétel, hanem <see cref="TransitionResult.Rejected"/> + esemény, hogy
    /// egy UI-hiba ne döntse le az alkalmazást.
    ///
    /// Újrabelépés: a <see cref="StateChanged"/> kezelőjéből kért váltás sorba
    /// áll, és az aktuális értesítés után fut le, az akkori állapothoz mérten.
    /// Ha egy kezelő kivételt dob, a sor kiürül, a kivétel továbbmegy.
    /// Csak a főszálról hívható.
    /// </summary>
    public sealed class AppStateMachine
    {
        private readonly Queue<KeyValuePair<AppState, string>> _pending = new Queue<KeyValuePair<AppState, string>>();
        private bool _dispatching;

        public AppState Current { get; private set; }

        public event Action<AppStateChange>? StateChanged;

        /// <summary>Elutasított váltás: (aktuális, kért, ok).</summary>
        public event Action<AppState, AppState, string>? TransitionRejected;

        public AppStateMachine(AppState initial = AppState.Boot)
        {
            Current = initial;
        }

        public bool IsSessionState => Current == AppState.Simulation || Current == AppState.Paused;

        public bool CanTransitionTo(AppState target) => IsTransitionAllowed(Current, target);

        /// <summary>Az architektúra-doksi §2 táblázata. Ugyanabba az állapotba váltás nem engedett.</summary>
        public static bool IsTransitionAllowed(AppState from, AppState to)
        {
            if (from == to) return false;
            if (from == AppState.Quitting) return false;
            if (to == AppState.Quitting) return true;
            switch (from)
            {
                case AppState.Boot:
                    return to == AppState.MainMenu;
                case AppState.MainMenu:
                    return to == AppState.WorldCreation || to == AppState.Loading;
                case AppState.WorldCreation:
                    return to == AppState.MainMenu || to == AppState.Loading;
                case AppState.Loading:
                    return to == AppState.Simulation || to == AppState.MainMenu;
                case AppState.Simulation:
                    return to == AppState.Paused || to == AppState.Loading;
                case AppState.Paused:
                    return to == AppState.Simulation || to == AppState.MainMenu || to == AppState.Loading;
                default:
                    return false;
            }
        }

        public TransitionResult RequestTransition(AppState target, string reason = "")
        {
            reason ??= "";
            if (_dispatching)
            {
                _pending.Enqueue(new KeyValuePair<AppState, string>(target, reason));
                return TransitionResult.Queued;
            }

            if (!IsTransitionAllowed(Current, target))
            {
                TransitionRejected?.Invoke(Current, target, reason);
                return TransitionResult.Rejected;
            }

            _dispatching = true;
            try
            {
                Apply(target, reason);
                while (_pending.Count > 0)
                {
                    var next = _pending.Dequeue();
                    if (IsTransitionAllowed(Current, next.Key))
                        Apply(next.Key, next.Value);
                    else
                        TransitionRejected?.Invoke(Current, next.Key, next.Value);
                }
            }
            catch
            {
                _pending.Clear();
                throw;
            }
            finally
            {
                _dispatching = false;
            }
            return TransitionResult.Completed;
        }

        private void Apply(AppState target, string reason)
        {
            var change = new AppStateChange(Current, target, reason);
            Current = target;
            StateChanged?.Invoke(change);
        }
    }
}
