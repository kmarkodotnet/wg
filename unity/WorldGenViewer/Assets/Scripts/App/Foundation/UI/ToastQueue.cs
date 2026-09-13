#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.UI
{
    public enum ToastSeverity
    {
        Info,
        Success,
        Warning,
        Error,
    }

    public sealed class Toast
    {
        internal Toast(int id, ToastSeverity severity, string message, double durationSeconds)
        {
            Id = id;
            Severity = severity;
            Message = message;
            DurationSeconds = durationSeconds;
            RemainingSeconds = durationSeconds;
            RepeatCount = 1;
        }

        public int Id { get; }
        public ToastSeverity Severity { get; }
        public string Message { get; }
        public double DurationSeconds { get; }
        public double RemainingSeconds { get; internal set; }

        /// <summary>Hányszor érkezett ugyanez az üzenet (összevonás); a nézet "×3"-at mutathat.</summary>
        public int RepeatCount { get; internal set; }
    }

    /// <summary>
    /// Nem modális értesítések (WF-UI-004). Legfeljebb N látható, a többi sorban
    /// áll; azonos súlyosságú és szövegű üzenet összevonódik és újraindítja az
    /// idejét. Az idő unscaled másodperc (pause alatt is lejár).
    /// </summary>
    public sealed class ToastQueue
    {
        private readonly List<Toast> _visible = new List<Toast>();
        private readonly LinkedList<Toast> _pending = new LinkedList<Toast>();
        private int _nextId = 1;

        public int MaxVisible { get; }
        public int MaxPending { get; }

        public IReadOnlyList<Toast> Visible => _visible;
        public int PendingCount => _pending.Count;

        /// <summary>A pending sor megtelésekor eldobott üzenetek száma.</summary>
        public int DroppedCount { get; private set; }

        public event Action? Changed;

        public ToastQueue(int maxVisible = 3, int maxPending = 16)
        {
            if (maxVisible < 1) throw new ArgumentOutOfRangeException(nameof(maxVisible));
            if (maxPending < 0) throw new ArgumentOutOfRangeException(nameof(maxPending));
            MaxVisible = maxVisible;
            MaxPending = maxPending;
        }

        public static double DefaultDurationSeconds(ToastSeverity severity)
        {
            switch (severity)
            {
                case ToastSeverity.Warning: return 5.0;
                case ToastSeverity.Error: return 8.0;
                default: return 3.0;
            }
        }

        /// <summary>Az üzenet (vagy az összevont meglévő) azonosítója; 0, ha eldobódott.</summary>
        public int Show(ToastSeverity severity, string message, double? durationSeconds = null)
        {
            if (string.IsNullOrEmpty(message)) throw new ArgumentException("Üres üzenet.", nameof(message));
            double duration = durationSeconds ?? DefaultDurationSeconds(severity);
            if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));

            var existing = Find(severity, message);
            if (existing != null)
            {
                existing.RepeatCount++;
                existing.RemainingSeconds = existing.DurationSeconds;
                Changed?.Invoke();
                return existing.Id;
            }

            var toast = new Toast(_nextId++, severity, message, duration);
            if (_visible.Count < MaxVisible)
            {
                _visible.Add(toast);
            }
            else
            {
                if (MaxPending == 0)
                {
                    DroppedCount++;
                    return 0;
                }
                if (_pending.Count >= MaxPending)
                {
                    _pending.RemoveFirst();
                    DroppedCount++;
                }
                _pending.AddLast(toast);
            }
            Changed?.Invoke();
            return toast.Id;
        }

        public void Tick(double unscaledDeltaSeconds)
        {
            if (!double.IsFinite(unscaledDeltaSeconds) || unscaledDeltaSeconds <= 0) return;
            bool changed = false;
            for (int i = _visible.Count - 1; i >= 0; i--)
            {
                _visible[i].RemainingSeconds -= unscaledDeltaSeconds;
                if (_visible[i].RemainingSeconds > 0) continue;
                _visible.RemoveAt(i);
                changed = true;
            }
            changed |= Promote();
            if (changed) Changed?.Invoke();
        }

        public bool Dismiss(int id)
        {
            for (int i = 0; i < _visible.Count; i++)
            {
                if (_visible[i].Id != id) continue;
                _visible.RemoveAt(i);
                Promote();
                Changed?.Invoke();
                return true;
            }
            for (var node = _pending.First; node != null; node = node.Next)
            {
                if (node.Value.Id != id) continue;
                _pending.Remove(node);
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        public void Clear()
        {
            if (_visible.Count == 0 && _pending.Count == 0) return;
            _visible.Clear();
            _pending.Clear();
            Changed?.Invoke();
        }

        private bool Promote()
        {
            bool promoted = false;
            while (_visible.Count < MaxVisible && _pending.Count > 0)
            {
                _visible.Add(_pending.First!.Value);
                _pending.RemoveFirst();
                promoted = true;
            }
            return promoted;
        }

        private Toast? Find(ToastSeverity severity, string message)
        {
            foreach (var t in _visible)
                if (t.Severity == severity && string.Equals(t.Message, message, StringComparison.Ordinal)) return t;
            foreach (var t in _pending)
                if (t.Severity == severity && string.Equals(t.Message, message, StringComparison.Ordinal)) return t;
            return null;
        }
    }
}
