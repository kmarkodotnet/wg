#nullable enable

namespace WorldGen.App.Lifecycle
{
    /// <summary>Az alkalmazás felső szintű állapotai (WF-APP-001).</summary>
    public enum AppState
    {
        Boot,
        MainMenu,
        WorldCreation,
        Loading,
        Simulation,
        Paused,
        Quitting,
    }

    public enum TransitionResult
    {
        /// <summary>Azonnal lefutott (a sorba állított követő kérésekkel együtt).</summary>
        Completed,

        /// <summary>Egy folyamatban lévő értesítés közben érkezett; annak végén fut le.</summary>
        Queued,

        /// <summary>Az aktuális állapotból nem engedett.</summary>
        Rejected,
    }

    public readonly struct AppStateChange
    {
        public AppState From { get; }
        public AppState To { get; }
        public string Reason { get; }

        public AppStateChange(AppState from, AppState to, string reason)
        {
            From = from;
            To = to;
            Reason = reason ?? "";
        }

        public override string ToString() => From + " -> " + To + (Reason.Length > 0 ? " (" + Reason + ")" : "");
    }
}
