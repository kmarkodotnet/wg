using System;
using System.Threading;
using System.Threading.Tasks;
using WorldGen.App.Loading;
using WorldGen.App.Localization;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Loading
{
    public class LoadingProgressTrackerTests
    {
        [Fact]
        public void WeightedMonotonicProgressWithSkippedStage()
        {
            var tracker = new LoadingProgressTracker(WorldGenerationStages.CreateDefault()); // súlyok 2,3,2,1,2
            var s = tracker.Snapshot();
            Assert.Equal(0, s.Overall);
            Assert.Equal(-1, s.StageIndex);

            tracker.BeginStage(WorldGenerationStages.Crust);
            tracker.ReportStageProgress(0.5);
            Assert.Equal(0.1, tracker.Snapshot().Overall, 12);
            Assert.Equal("loading.crust", tracker.Snapshot().MessageKey);

            tracker.BeginStage(WorldGenerationStages.Plates);
            Assert.Equal(0.2, tracker.Snapshot().Overall, 12);
            tracker.ReportStageProgress(0.5);
            tracker.ReportStageProgress(0.2);
            tracker.ReportStageProgress(double.NaN);
            Assert.Equal(0.35, tracker.Snapshot().Overall, 12);

            tracker.BeginStage(WorldGenerationStages.Oceans); // a climate kimarad → késznek számít
            Assert.Equal(0.7, tracker.Snapshot().Overall, 12);
            tracker.ReportStageProgress(7);
            Assert.Equal(0.8, tracker.Snapshot().Overall, 12);

            tracker.BeginStage(WorldGenerationStages.Renderer);
            Assert.False(tracker.Snapshot().IsCancellable);
            Assert.Throws<InvalidOperationException>(() => tracker.BeginStage(WorldGenerationStages.Crust));
            Assert.Throws<ArgumentException>(() => tracker.BeginStage("unknown"));

            tracker.Complete();
            s = tracker.Snapshot();
            Assert.Equal(1.0, s.Overall);
            Assert.True(s.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => tracker.BeginStage(WorldGenerationStages.Renderer));
        }

        [Fact]
        public void FailureIsReported()
        {
            var tracker = new LoadingProgressTracker(WorldGenerationStages.CreateDefault());
            tracker.BeginStage(WorldGenerationStages.Crust);
            tracker.Fail("out of memory");
            var s = tracker.Snapshot();
            Assert.True(s.IsFailed);
            Assert.Equal("out of memory", s.FailureMessage);
            Assert.Throws<InvalidOperationException>(() => tracker.BeginStage(WorldGenerationStages.Plates));
        }

        [Fact]
        public async Task ConcurrentReportingIsObservedMonotonically()
        {
            var tracker = new LoadingProgressTracker(WorldGenerationStages.CreateDefault());
            var worker = Task.Run(() =>
            {
                foreach (var stage in tracker.Stages)
                {
                    tracker.BeginStage(stage.Id);
                    for (int i = 1; i <= 2000; i++) tracker.ReportStageProgress(i / 2000.0);
                }
                tracker.Complete();
            });

            double last = 0;
            while (!worker.IsCompleted)
            {
                double now = tracker.Snapshot().Overall;
                Assert.True(now >= last, now + " < " + last);
                last = now;
                Thread.Yield();
            }
            await worker;
            Assert.Equal(1.0, tracker.Snapshot().Overall);
        }

        [Fact]
        public void ValidatesStages()
        {
            Assert.Throws<ArgumentException>(() => new LoadingProgressTracker(new LoadingStage[0]));
            Assert.Throws<ArgumentException>(() => new LoadingProgressTracker(new[] { new LoadingStage("a", "", 1), new LoadingStage("a", "", 1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LoadingStage("a", "", 0));

            var table = EnglishStrings.CreateTable();
            foreach (var stage in WorldGenerationStages.CreateDefault())
                Assert.True(table.TryGet(stage.MessageKey, out _), stage.MessageKey);
        }
    }

    public class ProgressDisplaySmootherTests
    {
        [Fact]
        public void NeverOvershootsNeverGoesBackAndConverges()
        {
            var smoother = new ProgressDisplaySmoother();
            double shown = 0;
            for (int frame = 0; frame < 30; frame++)
            {
                double next = smoother.Update(0.5, 1.0 / 60);
                Assert.True(next >= shown && next <= 0.5);
                shown = next;
            }
            Assert.True(shown > 0.4);

            Assert.Equal(shown, smoother.Update(0.2, 1.0 / 60));
            Assert.Equal(shown, smoother.Update(double.NaN, 1.0 / 60));
            Assert.Equal(shown, smoother.Update(1.0, 0));

            for (int frame = 0; frame < 600; frame++) smoother.Update(1.0, 1.0 / 60);
            Assert.Equal(1.0, smoother.Displayed);

            smoother.Reset();
            Assert.Equal(0, smoother.Displayed);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressDisplaySmoother(0));
        }
    }
}
