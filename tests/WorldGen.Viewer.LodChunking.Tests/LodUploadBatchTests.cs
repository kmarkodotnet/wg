using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

public class LodUploadBatchTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TerrainPairCanYieldAfterMeshWithoutPublishingOrPreparingTarget(bool cancel)
    {
        bool mesh = false, target = false, visible = false;
        double clock = 0;
        var batch = new LodUploadBatch<Action>(new Action[]
        {
            () => { mesh = true; clock += 2.1; },
            () => { Assert.True(mesh); target = true; clock += 0.2; }
        });
        Assert.Equal(1, batch.StageSlice(job => job(), () => clock, 2, 128));
        Assert.True(mesh);
        Assert.False(target);
        Assert.False(visible);
        Assert.Throws<InvalidOperationException>(() => batch.Commit(() => visible = true));
        if (cancel)
        {
            batch.Cancel();
            Assert.Throws<InvalidOperationException>(() => batch.Commit(() => visible = true));
            Assert.False(target);
        }
        else
        {
            Assert.Equal(1, batch.StageSlice(job => job(), () => clock, 2, 128));
            Assert.False(visible);
            batch.Commit(() => visible = target);
            Assert.True(visible);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void MixedSurfaceStageFailureNeverPublishesEarlierLayers(int failAt)
    {
        var visible = new[] { "old terrain", "old coast", "old ocean", "old borders" };
        var staged = new string[4];
        var jobs = Enumerable.Range(0, 4).Select<int, Action>(index => () =>
        {
            if (index == failAt) throw new ApplicationException();
            staged[index] = "new " + index;
        }).ToArray();
        var batch = new LodUploadBatch<Action>(jobs);
        Assert.Throws<ApplicationException>(() =>
        {
            while (!batch.ReadyToCommit) batch.StageSlice(job => job(), () => 0, 2, 1);
        });
        Assert.Throws<InvalidOperationException>(() => batch.Commit(() => visible = staged));
        Assert.Equal(new[] { "old terrain", "old coast", "old ocean", "old borders" }, visible);
        Assert.Equal(failAt, batch.CompletedCount);
    }

    [Fact]
    public void EmptyAuxiliaryLayersAreClearedOnlyAtJointCommit()
    {
        int[] visible = { 10, 20, 30, 40 };
        int[] prepared = { 100, 0, 300, 0 };
        var staged = new int[4];
        var jobs = Enumerable.Range(0, 4).Select<int, Action>(i => () => staged[i] = prepared[i]).ToArray();
        var batch = new LodUploadBatch<Action>(jobs);
        for (int i = 0; i < 4; i++)
        {
            batch.StageSlice(job => job(), () => 0, 2, 1);
            Assert.Equal(new[] { 10, 20, 30, 40 }, visible);
        }
        batch.Commit(() => visible = staged);
        Assert.Equal(prepared, visible);
    }

    [Fact]
    public void BudgetStopsBetweenMeshesAndNeverPublishesPartialData()
    {
        var batch = new LodUploadBatch<int>(Enumerable.Range(1, 5));
        var staged = new List<int>();
        int[] visible = { 100 };
        double clock = 0;
        void Stage(int item) { staged.Add(item); clock += 1; }
        Assert.Equal(2, batch.StageSlice(Stage, () => clock, 2, 64));
        Assert.Equal(new[] { 100 }, visible);
        Assert.False(batch.ReadyToCommit);
        Assert.Throws<InvalidOperationException>(() => batch.Commit(() => visible = staged.ToArray()));
        Assert.Equal(2, batch.StageSlice(Stage, () => clock, 2, 64));
        Assert.Equal(1, batch.StageSlice(Stage, () => clock, 2, 64));
        Assert.True(batch.ReadyToCommit);
        Assert.Equal(new[] { 100 }, visible);
        batch.Commit(() => visible = staged.ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, visible);
        Assert.False(batch.ReadyToCommit);
        Assert.Throws<InvalidOperationException>(() => batch.Commit(() => { }));
    }

    [Theory]
    [InlineData(1)] [InlineData(64)]
    public void ItemLimitBoundsWorkEvenWhenClockDoesNotAdvance(int limit)
    {
        var batch = new LodUploadBatch<int>(Enumerable.Range(0, 100));
        Assert.Equal(limit, batch.StageSlice(_ => { }, () => 0, 2, limit));
        Assert.Equal(limit, batch.CompletedCount);
    }

    [Fact]
    public void OversizedSingleUploadStillMakesProgressAndNoSecondUploadStarts()
    {
        var batch = new LodUploadBatch<int>(new[] { 1, 2 });
        double clock = 100;
        Assert.Equal(1, batch.StageSlice(_ => clock += 50, () => clock, 2, 64));
        Assert.Equal(1, batch.CompletedCount);
    }

    [Fact]
    public void CancelledTransactionCannotCommitOrResume()
    {
        var batch = new LodUploadBatch<int>(new[] { 1, 2 });
        batch.StageSlice(_ => { }, () => 0, 2, 1);
        batch.Cancel(); batch.Cancel();
        Assert.False(batch.ReadyToCommit);
        Assert.Throws<InvalidOperationException>(() => batch.Commit(() => throw new Exception("Nem futhat le.")));
        Assert.Throws<InvalidOperationException>(() => batch.StageSlice(_ => { }, () => 0, 2, 1));
    }

    [Fact]
    public void StageFailureDoesNotCountFailedMeshOrAllowCommit()
    {
        var batch = new LodUploadBatch<int>(new[] { 1, 2, 3 });
        var staged = new List<int>();
        Assert.Throws<ApplicationException>(() => batch.StageSlice(item =>
        {
            if (item == 2) throw new ApplicationException();
            staged.Add(item);
        }, () => 0, 2, 64));
        Assert.Equal(new[] { 1 }, staged);
        Assert.Equal(1, batch.CompletedCount);
        Assert.False(batch.ReadyToCommit);
        Assert.Throws<InvalidOperationException>(() => batch.Commit(() => { }));
    }

    [Fact]
    public void EmptyBatchCanCommitExactlyOnceAndRejectsReentrantCommit()
    {
        var batch = new LodUploadBatch<int>(Array.Empty<int>());
        Assert.True(batch.ReadyToCommit);
        Assert.Equal(0, batch.StageSlice(_ => throw new Exception(), () => 0, 2, 64));
        int count = 0;
        batch.Commit(() =>
        {
            count++;
            Assert.Throws<InvalidOperationException>(() => batch.Commit(() => count++));
        });
        Assert.Equal(1, count);
    }

    [Fact]
    public void CommitFailureCannotPublishAgain()
    {
        var batch = new LodUploadBatch<int>(Array.Empty<int>());
        Assert.Throws<ApplicationException>(() => batch.Commit(() => throw new ApplicationException()));
        Assert.False(batch.ReadyToCommit);
        Assert.Throws<InvalidOperationException>(() => batch.Commit(() => { }));
    }

    [Fact]
    public void InputIsCopiedAndCancellationInsideStageStopsFollowingJobs()
    {
        var input = new[] { 1, 2 };
        var batch = new LodUploadBatch<int>(input);
        input[0] = 99;
        var seen = new List<int>();
        Assert.Equal(1, batch.StageSlice(item => { seen.Add(item); batch.Cancel(); }, () => 0, 2, 64));
        Assert.Equal(new[] { 1 }, seen);
        Assert.False(batch.ReadyToCommit);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidBudgetIsRejectedWithoutConsumingWork(double budget)
    {
        var batch = new LodUploadBatch<int>(new[] { 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.StageSlice(_ => { }, () => 0, budget, 1));
        Assert.Equal(0, batch.CompletedCount);
    }

    [Fact]
    public void NullCallbacksAndInvalidItemLimitAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new LodUploadBatch<int>(null!));
        var batch = new LodUploadBatch<int>(new[] { 1 });
        Assert.Throws<ArgumentNullException>(() => batch.StageSlice(null!, () => 0, 2, 1));
        Assert.Throws<ArgumentNullException>(() => batch.StageSlice(_ => { }, null!, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.StageSlice(_ => { }, () => 0, 2, 0));
        Assert.Throws<ArgumentNullException>(() => batch.Commit(null!));
        Assert.Equal(0, batch.CompletedCount);
    }
}
