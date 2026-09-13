#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace WorldGen.App.Diagnostics
{
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Fatal,
    }

    public readonly struct LogEntry
    {
        public DateTime TimestampUtc { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
        public Exception? Exception { get; }

        public LogEntry(DateTime timestampUtc, LogLevel level, string category, string message, Exception? exception)
        {
            TimestampUtc = timestampUtc;
            Level = level;
            Category = category ?? "";
            Message = message ?? "";
            Exception = exception;
        }
    }

    public interface ILogSink
    {
        void Write(in LogEntry entry);
        void Flush();
    }

    public static class LogFormatter
    {
        /// <summary>"2026-09-13T12:03:04.123Z [INFO ] Category: message" (+ kivétel a következő sorokban).</summary>
        public static string Format(in LogEntry entry)
        {
            var sb = new StringBuilder(64 + entry.Message.Length);
            sb.Append(entry.TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            sb.Append(" [").Append(LevelTag(entry.Level)).Append("] ");
            if (entry.Category.Length > 0) sb.Append(entry.Category).Append(": ");
            sb.Append(entry.Message);
            if (entry.Exception != null)
            {
                sb.Append('\n');
                sb.Append(entry.Exception.ToString().Replace("\r\n", "\n"));
            }
            return sb.ToString();
        }

        public static string LevelTag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return "TRACE";
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO ";
                case LogLevel.Warning: return "WARN ";
                case LogLevel.Error: return "ERROR";
                case LogLevel.Fatal: return "FATAL";
                default: return "?????";
            }
        }
    }
}
