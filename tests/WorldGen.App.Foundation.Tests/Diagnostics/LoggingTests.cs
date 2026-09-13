using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldGen.App.Diagnostics;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Diagnostics
{
    public class LoggingTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 13, 12, 3, 4, 123, DateTimeKind.Utc);

        private sealed class ThrowingSink : ILogSink
        {
            public void Write(in LogEntry entry) => throw new IOException("disk");

            public void Flush() => throw new IOException("disk");
        }

        [Fact]
        public void FiltersByMinimumLevelAndStampsWithClock()
        {
            var clock = new ManualClock(T0);
            var sink = new MemoryLogSink();
            var logger = new AppLogger(clock, LogLevel.Info);
            logger.AddSink(sink);

            logger.Log(LogLevel.Debug, "Boot", "hidden");
            logger.Info("Boot", "started");
            clock.Advance(TimeSpan.FromSeconds(1));
            logger.Warning("Save", "slow");
            logger.MinimumLevel = LogLevel.Trace;
            logger.Log(LogLevel.Trace, "Boot", "now visible");

            var entries = sink.Snapshot();
            Assert.Equal(new[] { "started", "slow", "now visible" }, entries.Select(e => e.Message));
            Assert.Equal(T0, entries[0].TimestampUtc);
            Assert.Equal(T0.AddSeconds(1), entries[1].TimestampUtc);
            Assert.Equal("Save", entries[1].Category);
            Assert.False(logger.IsEnabled(LogLevel.Trace - 1));
        }

        [Fact]
        public void FaultySinkNeverThrowsAndOtherSinksStillReceive()
        {
            var logger = new AppLogger(new ManualClock(T0));
            var good = new MemoryLogSink();
            logger.AddSink(new ThrowingSink());
            logger.AddSink(good);

            logger.Error("Save", "failed", new InvalidOperationException("x"));
            logger.Flush();

            Assert.Equal(1, good.Count);
            Assert.Equal(2, logger.SinkFailureCount);
        }

        [Fact]
        public void FormatIsStableAndIncludesException()
        {
            var entry = new LogEntry(T0, LogLevel.Warning, "Save", "disk nearly full", null);
            Assert.Equal("2026-09-13T12:03:04.123Z [WARN ] Save: disk nearly full", LogFormatter.Format(entry));

            var withException = new LogEntry(T0, LogLevel.Error, "", "boom", new InvalidOperationException("bad state"));
            string text = LogFormatter.Format(withException);
            Assert.StartsWith("2026-09-13T12:03:04.123Z [ERROR] boom\nSystem.InvalidOperationException: bad state", text);
            Assert.DoesNotContain("\r", text);
        }

        [Fact]
        public void MemorySinkKeepsNewestEntriesInOrder()
        {
            var sink = new MemoryLogSink(3);
            for (int i = 0; i < 5; i++)
                sink.Write(new LogEntry(T0, LogLevel.Info, "", "m" + i, null));
            Assert.Equal(new[] { "m2", "m3", "m4" }, sink.Snapshot().Select(e => e.Message));
            sink.Clear();
            Assert.Empty(sink.Snapshot());
            Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryLogSink(0));
        }

        [Fact]
        public void ConcurrentLoggingLosesNoEntries()
        {
            var sink = new MemoryLogSink(10000);
            var logger = new AppLogger(new ManualClock(T0));
            logger.AddSink(sink);

            Parallel.For(0, 8, t =>
            {
                for (int i = 0; i < 1000; i++) logger.Info("T" + t, "msg");
            });

            Assert.Equal(8000, sink.Count);
        }

        [Fact]
        public void FileSinkWritesUtf8WithoutBomLfAndAppends()
        {
            using var dir = new TempDirectory();
            string path = Path.Combine(dir.Path, "Logs", "worldgen-20260913-120304.log");

            using (var sink = new FileLogSink(path))
            {
                sink.Write(new LogEntry(T0, LogLevel.Info, "Boot", "első indítás", null));
            }
            using (var sink = new FileLogSink(path))
            {
                sink.Write(new LogEntry(T0, LogLevel.Error, "Boot", "második", null));
                sink.Dispose();
                sink.Write(new LogEntry(T0, LogLevel.Error, "Boot", "dispose után: eldobva", null));
            }

            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            string text = Encoding.UTF8.GetString(bytes);
            Assert.Equal(
                "2026-09-13T12:03:04.123Z [INFO ] Boot: első indítás\n2026-09-13T12:03:04.123Z [ERROR] Boot: második\n",
                text);
        }

        [Fact]
        public void DefaultLogChannelIsNoOp()
        {
            var channel = default(LogChannel);
            Assert.Equal("", channel.Category);
            channel.Info("nothing happens");

            var sink = new MemoryLogSink();
            var logger = new AppLogger(new ManualClock(T0));
            logger.AddSink(sink);
            logger.ForCategory("Audio").Warning("clip missing");
            Assert.Equal("Audio", sink.Snapshot()[0].Category);
            Assert.Equal(LogLevel.Warning, sink.Snapshot()[0].Level);
        }
    }

    public class LogFileNamingTests
    {
        [Fact]
        public void CreatesExpectedName()
        {
            Assert.Equal("worldgen-20260913-140509.log", LogFileNaming.CreateFileName(new DateTime(2026, 9, 13, 14, 5, 9)));
        }

        [Fact]
        public void UniqueNameAddsSequenceSuffix()
        {
            var t = new DateTime(2026, 9, 13, 14, 5, 9);
            var existing = new[] { "worldgen-20260913-140509.log", "worldgen-20260913-140509-2.log" };
            Assert.Equal("worldgen-20260913-140509-3.log", LogFileNaming.CreateUniqueFileName(t, existing.Contains));
        }

        [Theory]
        [InlineData("worldgen-20260913-140509.log", true, 1)]
        [InlineData("worldgen-20260913-140509-2.log", true, 2)]
        [InlineData("worldgen-20260913-140509-1.log", false, 0)]
        [InlineData("worldgen-20260913-140509-x.log", false, 0)]
        [InlineData("worldgen-20261313-140509.log", false, 0)]
        [InlineData("worldgen-20260913-140509.txt", false, 0)]
        [InlineData("other-20260913-140509.log", false, 0)]
        [InlineData("worldgen-.log", false, 0)]
        public void ParsesOnlyOwnPattern(string name, bool valid, int sequence)
        {
            Assert.Equal(valid, LogFileNaming.TryParse(name, out _, out int seq));
            if (valid) Assert.Equal(sequence, seq);
        }

        [Fact]
        public void RetentionKeepsNewestAndIgnoresForeignFiles()
        {
            var names = new[]
            {
                "worldgen-20260910-080000.log",
                "notes.log",
                "worldgen-20260913-140509.log",
                "worldgen-20260913-140509-2.log",
                "worldgen-20260912-235959.log",
            };

            Assert.Equal(new[] { "worldgen-20260912-235959.log", "worldgen-20260910-080000.log" },
                LogFileNaming.SelectFilesToDelete(names, 2));
            Assert.Equal(4, LogFileNaming.SelectFilesToDelete(names, 0).Count);
            Assert.Empty(LogFileNaming.SelectFilesToDelete(names, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => LogFileNaming.SelectFilesToDelete(names, -1));
        }
    }

    public class SystemInfoReportTests
    {
        [Fact]
        public void OmitsUnavailableValues()
        {
            var report = new SystemInfoReport
            {
                ApplicationVersion = "WorldGen 0.1.0-alpha",
                CpuModel = "Ryzen 9",
                CpuLogicalCores = 16,
                SystemMemoryMb = 32000,
                GpuModel = "RTX",
            };
            Assert.Equal(
                new[] { "Application: WorldGen 0.1.0-alpha", "CPU: Ryzen 9 (16 logical cores)", "RAM: 32000 MB", "GPU: RTX" },
                report.ToLogLines());
        }
    }

    public class FrameTimeStatsTests
    {
        [Fact]
        public void ComputesRollingStatistics()
        {
            var stats = new FrameTimeStats(4);
            Assert.Equal(0, stats.FramesPerSecond);
            Assert.Equal(0, stats.PercentileMilliseconds(0.95));

            foreach (double ms in new[] { 10.0, 20.0, 30.0, 40.0 }) stats.AddSample(ms);
            Assert.Equal(25.0, stats.AverageMilliseconds);
            Assert.Equal(40.0, stats.FramesPerSecond);
            Assert.Equal(40.0, stats.MaxMilliseconds);
            Assert.Equal(20.0, stats.PercentileMilliseconds(0.5));
            Assert.Equal(40.0, stats.PercentileMilliseconds(0.95));
            Assert.Equal(10.0, stats.PercentileMilliseconds(0));

            stats.AddSample(50.0); // a 10.0 kiesik az ablakból
            Assert.Equal(35.0, stats.AverageMilliseconds);
            Assert.Equal(4, stats.Count);
        }

        [Fact]
        public void InvalidSamplesAreCountedNotStored()
        {
            var stats = new FrameTimeStats(4);
            stats.AddSample(double.NaN);
            stats.AddSample(double.PositiveInfinity);
            stats.AddSample(-1);
            stats.AddSample(16.0);
            Assert.Equal(1, stats.Count);
            Assert.Equal(3, stats.InvalidSampleCount);
            stats.Reset();
            Assert.Equal(0, stats.Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => stats.PercentileMilliseconds(1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTimeStats(0));
        }
    }
}
