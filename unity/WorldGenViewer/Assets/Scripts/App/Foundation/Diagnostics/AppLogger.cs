#nullable enable
using System;
using System.Collections.Generic;
using WorldGen.App.Storage;

namespace WorldGen.App.Diagnostics
{
    /// <summary>
    /// Szálbiztos alkalmazásnapló (WF-DIAG-001). A naplózás soha nem dobhat
    /// kivételt a hívóra: a hibás sink hibája elnyelődik és számlálódik.
    /// </summary>
    public sealed class AppLogger
    {
        private readonly object _gate = new object();
        private readonly List<ILogSink> _sinks = new List<ILogSink>();
        private readonly IClock _clock;
        private LogLevel _minimumLevel;
        private int _sinkFailureCount;

        public AppLogger(IClock clock, LogLevel minimumLevel = LogLevel.Info)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _minimumLevel = minimumLevel;
        }

        public LogLevel MinimumLevel
        {
            get { lock (_gate) return _minimumLevel; }
            set { lock (_gate) _minimumLevel = value; }
        }

        public int SinkFailureCount
        {
            get { lock (_gate) return _sinkFailureCount; }
        }

        public void AddSink(ILogSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            lock (_gate) _sinks.Add(sink);
        }

        public bool RemoveSink(ILogSink sink)
        {
            lock (_gate) return _sinks.Remove(sink);
        }

        public bool IsEnabled(LogLevel level)
        {
            lock (_gate) return level >= _minimumLevel;
        }

        public void Log(LogLevel level, string category, string message, Exception? exception = null)
        {
            lock (_gate)
            {
                if (level < _minimumLevel) return;
                var entry = new LogEntry(_clock.UtcNow, level, category, message, exception);
                foreach (var sink in _sinks)
                {
                    try
                    {
                        sink.Write(entry);
                    }
                    catch
                    {
                        _sinkFailureCount++;
                    }
                }
            }
        }

        public void Flush()
        {
            lock (_gate)
            {
                foreach (var sink in _sinks)
                {
                    try
                    {
                        sink.Flush();
                    }
                    catch
                    {
                        _sinkFailureCount++;
                    }
                }
            }
        }

        public void Info(string category, string message) => Log(LogLevel.Info, category, message);

        public void Warning(string category, string message, Exception? exception = null) => Log(LogLevel.Warning, category, message, exception);

        public void Error(string category, string message, Exception? exception = null) => Log(LogLevel.Error, category, message, exception);

        public LogChannel ForCategory(string category) => new LogChannel(this, category);
    }

    /// <summary>Egy kategóriára szűkített, olcsón továbbadható naplózó.</summary>
    public readonly struct LogChannel
    {
        private readonly AppLogger? _logger;
        private readonly string? _category;

        /// <summary>Alapértelmezett (default) struct esetén üres, és minden hívás no-op.</summary>
        public string Category => _category ?? "";

        public LogChannel(AppLogger logger, string category)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _category = category ?? "";
        }

        public void Debug(string message) => _logger?.Log(LogLevel.Debug, Category, message);

        public void Info(string message) => _logger?.Log(LogLevel.Info, Category, message);

        public void Warning(string message, Exception? exception = null) => _logger?.Log(LogLevel.Warning, Category, message, exception);

        public void Error(string message, Exception? exception = null) => _logger?.Log(LogLevel.Error, Category, message, exception);
    }
}
