using System;
using System.Collections.Generic;
using WorldGen.App.Lifecycle;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Lifecycle
{
    public class SessionTests
    {
        private sealed class Probe : IDisposable
        {
            private readonly List<string> _log;
            private readonly string _name;

            public Probe(List<string> log, string name)
            {
                _log = log;
                _name = name;
            }

            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                _log.Add(_name);
            }
        }

        [Fact]
        public void CleanupRunsInReverseRegistrationOrderExactlyOnce()
        {
            var log = new List<string>();
            var scope = new SessionScope(1, "A");
            var first = scope.Track(new Probe(log, "renderer"));
            scope.OnDispose(() => log.Add("unsubscribe"));
            scope.Track(new Probe(log, "overlay"));
            Assert.Equal(3, scope.PendingCleanupCount);

            scope.Dispose();
            scope.Dispose();

            Assert.Equal(new[] { "overlay", "unsubscribe", "renderer" }, log);
            Assert.Equal(1, first.DisposeCount);
            Assert.True(scope.IsDisposed);
            Assert.Equal(0, scope.PendingCleanupCount);
        }

        [Fact]
        public void FailingCleanupDoesNotStopOthersAndIsReportedToHandler()
        {
            var errors = new List<Exception>();
            var log = new List<string>();
            var scope = new SessionScope(1, "A", errors.Add);
            scope.OnDispose(() => log.Add("a"));
            scope.OnDispose(() => throw new InvalidOperationException("x"));
            scope.OnDispose(() => log.Add("c"));
            scope.OnDispose(() => throw new ArgumentException("y"));

            scope.Dispose();

            Assert.Equal(new[] { "c", "a" }, log);
            Assert.Equal(2, errors.Count);
        }

        [Fact]
        public void FailingCleanupWithoutHandlerThrowsAggregateAfterAllCleanups()
        {
            var log = new List<string>();
            var scope = new SessionScope(1, "A");
            scope.OnDispose(() => log.Add("a"));
            scope.OnDispose(() => throw new InvalidOperationException("x"));

            var ex = Assert.Throws<AggregateException>(() => scope.Dispose());
            Assert.Single(ex.InnerExceptions);
            Assert.Equal(new[] { "a" }, log);
        }

        [Fact]
        public void RegisteringAfterDisposeThrows()
        {
            var scope = new SessionScope(1, "A");
            scope.Dispose();
            Assert.Throws<ObjectDisposedException>(() => scope.OnDispose(() => { }));
        }

        [Fact]
        public void ManagerKeepsAtMostOneLiveSessionAcrossWorldChanges()
        {
            var manager = new SessionManager();
            var ended = new List<string>();
            manager.SessionEnded += s => ended.Add(s.Name);
            var probes = new List<Probe>();
            var log = new List<string>();

            foreach (string world in new[] { "World A", "World B", "World C" })
            {
                var scope = manager.BeginSession(world);
                probes.Add(scope.Track(new Probe(log, world)));
                Assert.Equal(1, manager.LiveSessionCount);
                Assert.Same(scope, manager.Current);
            }

            Assert.True(manager.EndSession());
            Assert.False(manager.EndSession());

            Assert.Equal(0, manager.LiveSessionCount);
            Assert.Equal(3, manager.TotalSessionsStarted);
            Assert.Null(manager.Current);
            Assert.Equal(new[] { "World A", "World B", "World C" }, ended);
            Assert.All(probes, p => Assert.Equal(1, p.DisposeCount));
        }

        [Fact]
        public void DisposingScopeDirectlyUpdatesManager()
        {
            var manager = new SessionManager();
            var scope = manager.BeginSession("A");
            scope.Dispose();
            Assert.Equal(0, manager.LiveSessionCount);
            Assert.Null(manager.Current);
            var next = manager.BeginSession("B");
            Assert.NotEqual(scope.Id, next.Id);
            Assert.Equal(1, manager.LiveSessionCount);
        }
    }
}
