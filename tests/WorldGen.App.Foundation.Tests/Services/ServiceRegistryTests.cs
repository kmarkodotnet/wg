using System;
using System.Collections.Generic;
using WorldGen.App.Services;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Services
{
    public class ServiceRegistryTests
    {
        private interface IAlpha
        {
        }

        private interface IBeta
        {
        }

        private sealed class Service : IAppService, IAlpha, IBeta
        {
            private readonly List<string> _log;
            private readonly bool _failInit;
            private readonly bool _failShutdown;

            public Service(List<string> log, string name, bool failInit = false, bool failShutdown = false)
            {
                _log = log;
                Name = name;
                _failInit = failInit;
                _failShutdown = failShutdown;
            }

            public string Name { get; }

            public void Initialize()
            {
                _log.Add("init " + Name);
                if (_failInit) throw new InvalidOperationException("init " + Name);
            }

            public void Shutdown()
            {
                _log.Add("shutdown " + Name);
                if (_failShutdown) throw new InvalidOperationException("shutdown " + Name);
            }
        }

        private sealed class Settings
        {
        }

        [Fact]
        public void RegisterGetAndTryGet()
        {
            var registry = new ServiceRegistry();
            var settings = new Settings();
            registry.Register(settings);

            Assert.Same(settings, registry.Get<Settings>());
            Assert.True(registry.TryGet<Settings>(out var found));
            Assert.Same(settings, found);
            Assert.True(registry.IsRegistered<Settings>());
            Assert.False(registry.TryGet<IAlpha>(out _));
            Assert.Throws<InvalidOperationException>(() => registry.Get<IAlpha>());
        }

        [Fact]
        public void DuplicateRegistrationThrows()
        {
            var registry = new ServiceRegistry();
            registry.Register(new Settings());
            Assert.Throws<InvalidOperationException>(() => registry.Register(new Settings()));
        }

        [Fact]
        public void InitializesInOrderShutsDownInReverseAndSharedInstanceOnce()
        {
            var log = new List<string>();
            var registry = new ServiceRegistry();
            var logging = new Service(log, "logging");
            var audio = new Service(log, "audio");
            registry.Register<IAlpha>(logging);
            registry.Register<IBeta>(logging);
            registry.Register(audio);

            registry.InitializeAll();
            Assert.True(registry.IsInitialized);
            registry.Dispose();
            registry.Dispose();

            Assert.Equal(new[] { "init logging", "init audio", "shutdown audio", "shutdown logging" }, log);
            Assert.False(registry.IsInitialized);
        }

        [Fact]
        public void RegistrationAfterInitializeThrows()
        {
            var registry = new ServiceRegistry();
            registry.InitializeAll();
            Assert.Throws<InvalidOperationException>(() => registry.Register(new Settings()));
            Assert.Throws<InvalidOperationException>(() => registry.InitializeAll());
        }

        [Fact]
        public void FailedInitializationShutsDownAlreadyInitializedServices()
        {
            var log = new List<string>();
            var registry = new ServiceRegistry();
            registry.Register<IAlpha>(new Service(log, "a"));
            registry.Register<IBeta>(new Service(log, "b", failInit: true));

            Assert.Throws<InvalidOperationException>(() => registry.InitializeAll());
            Assert.Equal(new[] { "init a", "init b", "shutdown a" }, log);
            Assert.False(registry.IsInitialized);
        }

        [Fact]
        public void ShutdownErrorsDoNotStopOtherServices()
        {
            var log = new List<string>();
            var errors = new List<Exception>();
            var registry = new ServiceRegistry();
            registry.Register<IAlpha>(new Service(log, "a"));
            registry.Register<IBeta>(new Service(log, "b", failShutdown: true));
            registry.InitializeAll();

            registry.ShutdownAll(errors.Add);

            Assert.Single(errors);
            Assert.Equal(new[] { "init a", "init b", "shutdown b", "shutdown a" }, log);
        }
    }
}
