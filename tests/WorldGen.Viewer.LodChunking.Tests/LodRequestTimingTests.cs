using System;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    public class LodRequestTimingTests
    {
        [Theory]
        [InlineData(0L)]
        [InlineData(300000000000L)]
        public void PhasesPartitionWallTimeIndependentlyOfEarlierBuild(long offset)
        {
            var timing = new LodRequestTiming(offset + 100, offset + 105, offset + 405,
                offset + 410, offset + 430, offset + 432, 1000);
            Assert.Equal(5, timing.QueueMs);
            Assert.Equal(300, timing.WorkerMs);
            Assert.Equal(5, timing.ReadyWaitMs);
            Assert.Equal(20, timing.StagingWallMs);
            Assert.Equal(2, timing.CommitMs);
            Assert.Equal(332, timing.TotalMs);
            Assert.Equal(timing.TotalMs, timing.QueueMs + timing.WorkerMs + timing.ReadyWaitMs
                + timing.StagingWallMs + timing.CommitMs);
        }

        [Fact]
        public void ReadyWorkerWaitingForMainThreadDoesNotBecomeUploadWork()
        {
            var timing = new LodRequestTiming(0, 0, 300, 9300, 9305, 9306, 1000);
            Assert.Equal(9000, timing.ReadyWaitMs);
            Assert.Equal(5, timing.StagingWallMs);
            Assert.Equal(1, timing.CommitMs);
        }

        [Fact]
        public void SingleUploadHasNoArtificialStagingDelay()
        {
            var timing = new LodRequestTiming(1, 1, 2, 2, 2, 3, 2000);
            Assert.Equal(0, timing.QueueMs);
            Assert.Equal(0, timing.ReadyWaitMs);
            Assert.Equal(0, timing.StagingWallMs);
            Assert.Equal(1, timing.TotalMs);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void InvalidClockFrequencyIsRejected(long frequency)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new LodRequestTiming(0, 0, 0, 0, 0, 0, frequency));

        [Theory]
        [InlineData(2, 1, 3, 4, 5, 6)]
        [InlineData(0, 2, 1, 4, 5, 6)]
        [InlineData(0, 1, 3, 2, 5, 6)]
        [InlineData(0, 1, 2, 4, 3, 6)]
        [InlineData(0, 1, 2, 3, 5, 4)]
        public void MisorderedPhasesAreNotSilentlyReported(long start, long worker, long ready,
            long observed, long commit, long end)
            => Assert.Throws<ArgumentException>(() => new LodRequestTiming(start, worker, ready, observed, commit, end, 1000));
    }
}
