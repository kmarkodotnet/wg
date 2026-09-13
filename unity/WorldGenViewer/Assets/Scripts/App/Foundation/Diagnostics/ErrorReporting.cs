#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WorldGen.App.Versioning;

namespace WorldGen.App.Diagnostics
{
    public readonly struct ExceptionOccurrence
    {
        public ExceptionOccurrence(string signature, int count, bool shouldLog)
        {
            Signature = signature;
            Count = count;
            ShouldLog = shouldLog;
        }

        public string Signature { get; }
        public int Count { get; }
        public bool ShouldLog { get; }

        /// <summary>Az első előfordulás: ekkor érdemes a felhasználót (egyszer) értesíteni.</summary>
        public bool IsFirst => Count == 1;
    }

    /// <summary>
    /// Ismétlődő kivételek ritkítása. A Unity egy Update-ben dobott kivételt
    /// minden frame-ben újra jelent; ez nélküle percek alatt megtöltené a naplót.
    /// Aláírás: az üzenet első sora + a stack trace első sora. Szálbiztos.
    /// </summary>
    public sealed class ExceptionThrottle
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public ExceptionThrottle(int maxLoggedPerSignature = 3)
        {
            if (maxLoggedPerSignature < 1) throw new ArgumentOutOfRangeException(nameof(maxLoggedPerSignature));
            MaxLoggedPerSignature = maxLoggedPerSignature;
        }

        public int MaxLoggedPerSignature { get; }

        public ExceptionOccurrence Register(string message, string? stackTrace)
        {
            string signature = FirstLine(message) + "|" + FirstLine(stackTrace);
            lock (_gate)
            {
                _counts.TryGetValue(signature, out int count);
                count++;
                _counts[signature] = count;
                return new ExceptionOccurrence(signature, count, count <= MaxLoggedPerSignature);
            }
        }

        /// <summary>A ritkítás miatt ki nem írt előfordulások száma aláírásonként (kilépéskori összesítéshez).</summary>
        public IReadOnlyList<KeyValuePair<string, int>> GetSuppressedCounts()
        {
            lock (_gate)
            {
                var result = new List<KeyValuePair<string, int>>();
                foreach (var pair in _counts)
                    if (pair.Value > MaxLoggedPerSignature) result.Add(new KeyValuePair<string, int>(pair.Key, pair.Value - MaxLoggedPerSignature));
                result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                return result;
            }
        }

        private static string FirstLine(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int end = text!.IndexOfAny(new[] { '\r', '\n' });
            return (end >= 0 ? text.Substring(0, end) : text).Trim();
        }
    }

    /// <summary>
    /// Hibajelentés-fájl (WF-DIAG-001 „crash log”): build, rendszer, a hiba és a
    /// legutóbbi naplóbejegyzések. Nem küld semmit sehova (nincs telemetria).
    /// </summary>
    public static class ErrorReport
    {
        public const string Prefix = "error-";
        public const string Extension = ".txt";

        public static string CreateFileName(DateTime timestamp)
            => Prefix + timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Extension;

        public static string Build(DateTime utcNow, BuildInfo? build, SystemInfoReport? system, string errorText, IReadOnlyList<LogEntry>? recentEntries)
        {
            var sb = new StringBuilder();
            sb.Append("WorldGen error report\n");
            sb.Append("Time: ").Append(utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append('\n');
            if (build != null) sb.Append("Build: ").Append(build.ToLogLine()).Append('\n');
            if (system != null)
                foreach (string line in system.ToLogLines()) sb.Append(line).Append('\n');
            sb.Append("\n--- Error ---\n").Append((errorText ?? "").Replace("\r\n", "\n")).Append('\n');
            if (recentEntries != null && recentEntries.Count > 0)
            {
                sb.Append("\n--- Recent log (").Append(recentEntries.Count.ToString(CultureInfo.InvariantCulture)).Append(" entries) ---\n");
                foreach (var entry in recentEntries) sb.Append(LogFormatter.Format(entry)).Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>A debug overlay egy frame-re vonatkozó, nem a képkockaidőből származó adatai; a hiányzó érték nem kap sort.</summary>
    public sealed class DebugOverlaySample
    {
        public double? SimulationStepMilliseconds { get; set; }
        public long? GpuMemoryMegabytes { get; set; }
        public int? CurrentLod { get; set; }
        public int? ActiveChunks { get; set; }
    }

    /// <summary>Debug overlay szövege (WF-DIAG-002); fejlesztői eszköz, szándékosan nem lokalizált.</summary>
    public static class DebugOverlayText
    {
        public static IReadOnlyList<string> BuildLines(FrameTimeStats stats, DebugOverlaySample? sample)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            var lines = new List<string>();
            var c = CultureInfo.InvariantCulture;
            if (stats.Count > 0)
            {
                lines.Add("FPS " + stats.FramesPerSecond.ToString("F1", c));
                lines.Add("Frame " + stats.AverageMilliseconds.ToString("F1", c) + " ms  p95 "
                    + stats.PercentileMilliseconds(0.95).ToString("F1", c) + "  max " + stats.MaxMilliseconds.ToString("F1", c));
            }
            if (sample != null)
            {
                if (sample.SimulationStepMilliseconds.HasValue && double.IsFinite(sample.SimulationStepMilliseconds.Value))
                    lines.Add("Sim step " + sample.SimulationStepMilliseconds.Value.ToString("F2", c) + " ms");
                if (sample.GpuMemoryMegabytes.HasValue) lines.Add("GPU memory " + sample.GpuMemoryMegabytes.Value.ToString("N0", c) + " MB");
                if (sample.CurrentLod.HasValue) lines.Add("LOD " + sample.CurrentLod.Value.ToString(c));
                if (sample.ActiveChunks.HasValue) lines.Add("Active chunks " + sample.ActiveChunks.Value.ToString("N0", c));
            }
            return lines;
        }
    }
}
