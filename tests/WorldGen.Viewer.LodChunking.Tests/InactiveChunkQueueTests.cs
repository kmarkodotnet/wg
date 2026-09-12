using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests
{
    public class InactiveChunkQueueTests
    {
        private static TileId Key(int i) => TileId.FromFaceLevelUV(0, 4, (uint)i, 0);

        [Fact]
        public void EmptyAndNegativeLimitAreHandled()
        {
            var queue = new InactiveChunkQueue();
            Assert.False(queue.TryTakeExcess(0, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.TryTakeExcess(-1, out _));
        }

        [Fact]
        public void OldestIsRemovedAndDuplicateDoesNotMoveIt()
        {
            var queue = new InactiveChunkQueue();
            queue.MarkInactive(Key(0)); queue.MarkInactive(Key(1)); queue.MarkInactive(Key(0));
            Assert.Equal(2, queue.Count);
            Assert.True(queue.TryTakeExcess(1, out TileId key));
            Assert.Equal(Key(0), key);
            Assert.False(queue.TryTakeExcess(1, out _));
        }

        [Fact]
        public void ReactivationRemovesOldEntryAndNextRetirementIsNewest()
        {
            var queue = new InactiveChunkQueue();
            queue.MarkInactive(Key(0)); queue.MarkInactive(Key(1));
            queue.Remove(Key(0)); queue.Remove(Key(0)); queue.MarkInactive(Key(0));
            Assert.True(queue.TryTakeExcess(0, out TileId first));
            Assert.Equal(Key(1), first);
            Assert.True(queue.TryTakeExcess(0, out TileId second));
            Assert.Equal(Key(0), second);
        }

        [Fact]
        public void ClearDropsAllHistoryAndAllowsSameKeyAgain()
        {
            var queue = new InactiveChunkQueue();
            queue.MarkInactive(Key(0)); queue.Clear();
            Assert.Equal(0, queue.Count);
            queue.MarkInactive(Key(0));
            Assert.True(queue.TryTakeExcess(0, out TileId key));
            Assert.Equal(Key(0), key);
        }

        [Theory]
        [InlineData(0)] [InlineData(3)] [InlineData(8)]
        public void RepeatedUseAndEvictionMatchIndependentList(int limit)
        {
            var queue = new InactiveChunkQueue();
            var expected = new List<TileId>();
            for (int i = 0; i < 500; i++)
            {
                TileId key = Key(i * 7 % 16);
                if (i % 3 == 0) { queue.Remove(key); expected.Remove(key); }
                else { queue.MarkInactive(key); if (!expected.Contains(key)) expected.Add(key); }
                bool shouldEvict = expected.Count > limit;
                Assert.Equal(shouldEvict, queue.TryTakeExcess(limit, out TileId removed));
                if (shouldEvict) { Assert.Equal(expected[0], removed); expected.RemoveAt(0); }
                Assert.Equal(expected.Count, queue.Count);
            }
        }
    }
}
