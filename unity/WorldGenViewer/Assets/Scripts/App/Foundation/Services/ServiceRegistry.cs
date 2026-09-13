#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace WorldGen.App.Services
{
    /// <summary>Globális, alkalmazás-élettartamú szolgáltatás életciklusa.</summary>
    public interface IAppService
    {
        void Initialize();
        void Shutdown();
    }

    /// <summary>
    /// Explicit kompozíciós gyökér singletonok helyett (WF-APP-003).
    /// Egyetlen példányát a Bootstrap hozza létre és adja tovább.
    /// Regisztráció csak <see cref="InitializeAll"/> előtt; duplikált típus
    /// kivételt dob. Az inicializálás regisztrációs, a leállítás fordított
    /// sorrendben fut; ugyanaz a példány több típus alatt is csak egyszer.
    /// </summary>
    public sealed class ServiceRegistry : IDisposable
    {
        private readonly Dictionary<Type, object> _byType = new Dictionary<Type, object>();
        private readonly List<object> _order = new List<object>();
        private readonly List<IAppService> _initialized = new List<IAppService>();
        private bool _initializeStarted;
        private bool _shutDown;

        public bool IsInitialized { get; private set; }

        public void Register<TService>(TService instance) where TService : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (_initializeStarted)
                throw new InvalidOperationException("Szolgáltatás csak InitializeAll előtt regisztrálható: " + typeof(TService).Name);
            if (_byType.ContainsKey(typeof(TService)))
                throw new InvalidOperationException("A szolgáltatás már regisztrálva van: " + typeof(TService).Name);
            _byType.Add(typeof(TService), instance);
            if (!ContainsReference(_order, instance)) _order.Add(instance);
        }

        public bool IsRegistered<TService>() where TService : class => _byType.ContainsKey(typeof(TService));

        public TService Get<TService>() where TService : class
        {
            if (_byType.TryGetValue(typeof(TService), out var instance)) return (TService)instance;
            throw new InvalidOperationException("Nincs regisztrálva: " + typeof(TService).Name);
        }

        public bool TryGet<TService>([NotNullWhen(true)] out TService? service) where TService : class
        {
            if (_byType.TryGetValue(typeof(TService), out var instance))
            {
                service = (TService)instance;
                return true;
            }
            service = null;
            return false;
        }

        /// <summary>
        /// Minden <see cref="IAppService"/> inicializálása. Ha egy kivételt dob,
        /// a már inicializáltak fordított sorrendben leállnak, és a kivétel továbbmegy.
        /// </summary>
        public void InitializeAll()
        {
            if (_initializeStarted) throw new InvalidOperationException("InitializeAll már lefutott.");
            _initializeStarted = true;
            foreach (object instance in _order)
            {
                if (!(instance is IAppService service)) continue;
                try
                {
                    service.Initialize();
                }
                catch
                {
                    ShutdownAll(null);
                    throw;
                }
                _initialized.Add(service);
            }
            IsInitialized = true;
        }

        /// <summary>
        /// Fordított sorrendű leállítás; idempotens. Egy szolgáltatás kivétele nem
        /// akadályozza a többit: <paramref name="onError"/> kapja, ennek hiányában
        /// a végén <see cref="AggregateException"/>.
        /// </summary>
        public void ShutdownAll(Action<Exception>? onError)
        {
            if (_shutDown) return;
            _shutDown = true;
            IsInitialized = false;
            List<Exception>? failures = null;
            for (int i = _initialized.Count - 1; i >= 0; i--)
            {
                try
                {
                    _initialized[i].Shutdown();
                }
                catch (Exception ex)
                {
                    if (onError != null) onError(ex);
                    else (failures ??= new List<Exception>()).Add(ex);
                }
            }
            _initialized.Clear();
            if (failures != null) throw new AggregateException("Szolgáltatás leállítása közben hiba történt.", failures);
        }

        public void Dispose() => ShutdownAll(null);

        private static bool ContainsReference(List<object> list, object instance)
        {
            foreach (object o in list)
                if (ReferenceEquals(o, instance)) return true;
            return false;
        }
    }
}
