#nullable enable
using System;

namespace WorldGen.App.Diagnostics
{
    /// <summary>Korlátos gyűrűpuffer a legutóbbi bejegyzéseknek (játékon belüli konzol, tesztek).</summary>
    public sealed class MemoryLogSink : ILogSink
    {
        private readonly object _gate = new object();
        private readonly LogEntry[] _buffer;
        private int _start;
        private int _count;

        public MemoryLogSink(int capacity = 256)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new LogEntry[capacity];
        }

        public int Capacity => _buffer.Length;

        public int Count
        {
            get { lock (_gate) return _count; }
        }

        public void Write(in LogEntry entry)
        {
            lock (_gate)
            {
                int index = (_start + _count) % _buffer.Length;
                _buffer[index] = entry;
                if (_count < _buffer.Length) _count++;
                else _start = (_start + 1) % _buffer.Length;
            }
        }

        public void Flush()
        {
        }

        /// <summary>A bejegyzések a legrégebbitől a legújabbig.</summary>
        public LogEntry[] Snapshot()
        {
            lock (_gate)
            {
                var result = new LogEntry[_count];
                for (int i = 0; i < _count; i++) result[i] = _buffer[(_start + i) % _buffer.Length];
                return result;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _start = 0;
                _count = 0;
                Array.Clear(_buffer, 0, _buffer.Length);
            }
        }
    }
}
