#nullable enable
using System;

namespace WorldGen.App.Lifecycle
{
    /// <summary>
    /// Egyszerre legfeljebb egy aktív világ-session. Új session nyitása az
    /// előzőt lezárja. A <see cref="LiveSessionCount"/> a WF-QA-002
    /// (Main Menu → A → Main Menu → B …) szivárgás-ellenőrzés mérőszáma:
    /// session-váltások után is legfeljebb 1 lehet.
    /// </summary>
    public sealed class SessionManager
    {
        private readonly Action<Exception>? _cleanupErrorHandler;
        private int _nextId = 1;

        public SessionScope? Current { get; private set; }

        /// <summary>Létrehozott, de még nem dispose-olt session-ök száma.</summary>
        public int LiveSessionCount { get; private set; }

        public int TotalSessionsStarted { get; private set; }

        public event Action<SessionScope>? SessionStarted;
        public event Action<SessionScope>? SessionEnded;

        public SessionManager(Action<Exception>? cleanupErrorHandler = null)
        {
            _cleanupErrorHandler = cleanupErrorHandler;
        }

        public SessionScope BeginSession(string name)
        {
            EndSession();
            var scope = new SessionScope(_nextId++, name, _cleanupErrorHandler);
            scope.Disposed += OnScopeDisposed;
            Current = scope;
            LiveSessionCount++;
            TotalSessionsStarted++;
            SessionStarted?.Invoke(scope);
            return scope;
        }

        /// <summary>Lezárja az aktív sessiont. Igaz, ha volt mit lezárni.</summary>
        public bool EndSession()
        {
            var scope = Current;
            if (scope == null) return false;
            scope.Dispose();
            return true;
        }

        private void OnScopeDisposed(SessionScope scope)
        {
            scope.Disposed -= OnScopeDisposed;
            LiveSessionCount--;
            if (ReferenceEquals(Current, scope)) Current = null;
            SessionEnded?.Invoke(scope);
        }
    }
}
