#nullable enable
using System;
using System.IO;
using WorldGen.App.Settings;

namespace WorldGen.App.Saves
{
    /// <summary>
    /// Időalapú autosave (WF-SAVE-004). Csak futó szimuláció alatt telik
    /// (pause, menü, betöltés alatt nem). Esedékessé válás után addig esedékes
    /// marad, amíg a hívó a mentést vissza nem igazolja.
    /// </summary>
    public sealed class AutosaveScheduler
    {
        private int _intervalMinutes;

        public AutosaveScheduler(int intervalMinutes)
        {
            IntervalMinutes = intervalMinutes;
        }

        /// <summary>Az engedett értékek egyikére igazítva (0 / 5 / 10 / 20).</summary>
        public int IntervalMinutes
        {
            get => _intervalMinutes;
            set
            {
                _intervalMinutes = SimulationSettings.SnapAutosaveInterval(value);
                if (_intervalMinutes == 0) IsDue = false;
            }
        }

        public bool IsEnabled => _intervalMinutes > 0;
        public double ElapsedSeconds { get; private set; }
        public bool IsDue { get; private set; }

        public double RemainingSeconds => IsEnabled ? Math.Max(0, _intervalMinutes * 60.0 - ElapsedSeconds) : double.PositiveInfinity;

        /// <summary>Igaz abban a hívásban, amelyben az autosave esedékessé vált.</summary>
        public bool Tick(double unscaledDeltaSeconds, bool isSimulationRunning)
        {
            if (!IsEnabled || !isSimulationRunning || IsDue) return false;
            if (!double.IsFinite(unscaledDeltaSeconds) || unscaledDeltaSeconds <= 0) return false;
            ElapsedSeconds += unscaledDeltaSeconds;
            if (ElapsedSeconds < _intervalMinutes * 60.0) return false;
            IsDue = true;
            return true;
        }

        /// <summary>Bármilyen sikeres mentés (kézi is) után: az időzítő újraindul.</summary>
        public void NotifySaved()
        {
            ElapsedSeconds = 0;
            IsDue = false;
        }

        public static bool ShouldSaveBeforeDeepTimeJump(SimulationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return settings.AutosaveBeforeDeepTimeJump;
        }
    }

    public enum SaveErrorKind
    {
        DiskFull,
        AccessDenied,
        PathTooLong,
        Corrupted,
        NotFound,
        Unknown,
    }

    /// <summary>
    /// Kivétel → felhasználóbarát kategória (WF-SAVE-002). A kivétel maga a
    /// naplóba kerül; a UI csak a lokalizált üzenetet mutatja.
    /// </summary>
    public static class SaveErrorClassifier
    {
        private const int WindowsDiskFull = unchecked((int)0x80070070);       // ERROR_DISK_FULL (112)
        private const int WindowsHandleDiskFull = unchecked((int)0x80070027); // ERROR_HANDLE_DISK_FULL (39)
        private const int UnixNoSpace = 28;                                   // ENOSPC (Linux / macOS)

        public static SaveErrorKind Classify(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
                return Classify(aggregate.InnerExceptions[0]);

            switch (exception)
            {
                case SaveCorruptedException _: return SaveErrorKind.Corrupted;
                case UnauthorizedAccessException _: return SaveErrorKind.AccessDenied;
                case PathTooLongException _: return SaveErrorKind.PathTooLong;
                case FileNotFoundException _: return SaveErrorKind.NotFound;
                case DirectoryNotFoundException _: return SaveErrorKind.NotFound;
                case IOException io when io.HResult == WindowsDiskFull || io.HResult == WindowsHandleDiskFull || io.HResult == UnixNoSpace:
                    return SaveErrorKind.DiskFull;
                default:
                    return SaveErrorKind.Unknown;
            }
        }

        public static string MessageKey(SaveErrorKind kind)
        {
            switch (kind)
            {
                case SaveErrorKind.DiskFull: return "save.error.diskFull";
                case SaveErrorKind.AccessDenied: return "save.error.accessDenied";
                case SaveErrorKind.PathTooLong: return "save.error.pathTooLong";
                case SaveErrorKind.Corrupted: return "save.error.corrupted";
                case SaveErrorKind.NotFound: return "save.error.notFound";
                default: return "save.error.unknown";
            }
        }
    }
}
