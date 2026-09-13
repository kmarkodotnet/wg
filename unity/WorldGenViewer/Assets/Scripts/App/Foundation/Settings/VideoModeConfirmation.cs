#nullable enable
using System;

namespace WorldGen.App.Settings
{
    public enum VideoConfirmationState
    {
        Idle,
        AwaitingConfirmation,
        Confirmed,
        Reverted,
    }

    /// <summary>
    /// Videobeállítás-rollback (WF-SET-002): kijelzőmód- vagy felbontásváltás
    /// után a felhasználónak adott időn belül meg kell erősítenie, különben a
    /// korábbi beállítás visszaáll. Az időt unscaled (valós) másodpercben kapja,
    /// mert pause alatt is futnia kell.
    /// </summary>
    public sealed class VideoModeConfirmation
    {
        public const double DefaultTimeoutSeconds = 15.0;

        private GraphicsSettings? _previous;
        private GraphicsSettings? _candidate;

        public double TimeoutSeconds { get; }
        public double RemainingSeconds { get; private set; }
        public VideoConfirmationState State { get; private set; } = VideoConfirmationState.Idle;

        public GraphicsSettings? Previous => _previous?.Clone();
        public GraphicsSettings? Candidate => _candidate?.Clone();

        /// <summary>Visszaállítást kér: a paraméter a korábbi beállítás.</summary>
        public event Action<GraphicsSettings>? RevertRequested;

        public event Action<GraphicsSettings>? Confirmed;

        public VideoModeConfirmation(double timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (!double.IsFinite(timeoutSeconds) || timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            TimeoutSeconds = timeoutSeconds;
        }

        /// <summary>Kell-e megerősítés: a kijelzőmód vagy a felbontás változott.</summary>
        public static bool RequiresConfirmation(GraphicsSettings before, GraphicsSettings after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            return before.DisplayMode != after.DisplayMode || !before.Resolution.Equals(after.Resolution);
        }

        public void Begin(GraphicsSettings previous, GraphicsSettings candidate)
        {
            _previous = (previous ?? throw new ArgumentNullException(nameof(previous))).Clone();
            _candidate = (candidate ?? throw new ArgumentNullException(nameof(candidate))).Clone();
            RemainingSeconds = TimeoutSeconds;
            State = VideoConfirmationState.AwaitingConfirmation;
        }

        public void Tick(double unscaledDeltaSeconds)
        {
            if (State != VideoConfirmationState.AwaitingConfirmation) return;
            if (!double.IsFinite(unscaledDeltaSeconds) || unscaledDeltaSeconds <= 0) return;
            RemainingSeconds = Math.Max(0, RemainingSeconds - unscaledDeltaSeconds);
            if (RemainingSeconds <= 0) Revert();
        }

        public bool Confirm()
        {
            if (State != VideoConfirmationState.AwaitingConfirmation) return false;
            State = VideoConfirmationState.Confirmed;
            Confirmed?.Invoke(_candidate!.Clone());
            return true;
        }

        public bool Revert()
        {
            if (State != VideoConfirmationState.AwaitingConfirmation) return false;
            State = VideoConfirmationState.Reverted;
            RemainingSeconds = 0;
            RevertRequested?.Invoke(_previous!.Clone());
            return true;
        }
    }
}
