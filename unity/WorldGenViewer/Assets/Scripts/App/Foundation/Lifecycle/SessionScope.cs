#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.Lifecycle
{
    /// <summary>
    /// Egy világ-session élettartamához kötött erőforrások és leiratkozások
    /// gyűjtője (WF-APP-002, WF-QA-002). Dispose-kor fordított sorrendben
    /// takarít; egy takarító kivétele nem akadályozza a többit. A hibák a
    /// konstruktorban kapott kezelőhöz kerülnek, ennek hiányában a végén
    /// <see cref="AggregateException"/>-ként dobódnak.
    /// Csak a főszálról használható.
    /// </summary>
    public sealed class SessionScope : IDisposable
    {
        private readonly List<Action> _cleanups = new List<Action>();
        private readonly Action<Exception>? _errorHandler;

        public int Id { get; }
        public string Name { get; }
        public bool IsDisposed { get; private set; }

        /// <summary>A még le nem futott takarítások száma.</summary>
        public int PendingCleanupCount => _cleanups.Count;

        public event Action<SessionScope>? Disposed;

        public SessionScope(int id, string name, Action<Exception>? errorHandler = null)
        {
            Id = id;
            Name = name ?? "";
            _errorHandler = errorHandler;
        }

        /// <summary>A session végén dispose-olandó objektum; visszaadja magát a láncoláshoz.</summary>
        public T Track<T>(T disposable) where T : IDisposable
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            OnDispose(disposable.Dispose);
            return disposable;
        }

        /// <summary>Tetszőleges takarítás, pl. eseményről leiratkozás.</summary>
        public void OnDispose(Action cleanup)
        {
            if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
            if (IsDisposed) throw new ObjectDisposedException("SessionScope '" + Name + "'");
            _cleanups.Add(cleanup);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            List<Exception>? failures = null;
            for (int i = _cleanups.Count - 1; i >= 0; i--)
            {
                try
                {
                    _cleanups[i]();
                }
                catch (Exception ex)
                {
                    (failures ??= new List<Exception>()).Add(ex);
                }
            }
            _cleanups.Clear();

            Disposed?.Invoke(this);

            if (failures == null) return;
            if (_errorHandler != null)
            {
                foreach (var ex in failures) _errorHandler(ex);
                return;
            }
            throw new AggregateException("A session '" + Name + "' takarítása közben hiba történt.", failures);
        }
    }
}
