#nullable enable
using System;
using System.IO;
using System.Text;

namespace WorldGen.App.Diagnostics
{
    /// <summary>
    /// Fájlba író sink, UTF-8 (BOM nélkül), LF sorvéggel. A megadott szinttől
    /// (alapból Warning) minden bejegyzés után lemezre ürít, hogy összeomláskor
    /// a hiba ne vesszen el a pufferben.
    /// </summary>
    public sealed class FileLogSink : ILogSink, IDisposable
    {
        private readonly object _gate = new object();
        private StreamWriter? _writer;

        public string FilePath { get; }
        public LogLevel FlushLevel { get; }

        public FileLogSink(string filePath, LogLevel flushLevel = LogLevel.Warning)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("Üres naplófájl-út.", nameof(filePath));
            FilePath = filePath;
            FlushLevel = flushLevel;
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = false };
        }

        public void Write(in LogEntry entry)
        {
            lock (_gate)
            {
                if (_writer == null) return;
                _writer.WriteLine(LogFormatter.Format(entry));
                if (entry.Level >= FlushLevel) _writer.Flush();
            }
        }

        public void Flush()
        {
            lock (_gate) _writer?.Flush();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_writer == null) return;
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }
        }
    }
}
