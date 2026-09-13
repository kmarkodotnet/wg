#nullable enable
using System;
using System.Collections.Generic;
using WorldGen.App.Lifecycle;

namespace WorldGen.App.Navigation
{
    /// <summary>Mit kell tennie a hívónak egy ESC / Vissza után.</summary>
    public enum BackAction
    {
        None,

        /// <summary>Egy regisztrált kezelő (dialógus, legördülő) elnyelte.</summary>
        HandledByOverlay,

        /// <summary>A képernyőverem egy szintet visszalépett.</summary>
        PopScreen,

        OpenPauseMenu,
        ResumeSimulation,
        ReturnToMainMenu,
        CancelLoading,
    }

    public interface IBackHandler
    {
        /// <summary>Igaz, ha a kezelő elnyelte a Vissza-eseményt.</summary>
        bool TryHandleBack();
    }

    /// <summary>
    /// Egységes ESC-kezelés (WF-UI-008). Sorrend: a legutóbb regisztrált
    /// kezelő elöl → képernyőverem → állapot-alapértelmezés.
    /// </summary>
    public sealed class BackNavigationRouter
    {
        private readonly List<IBackHandler> _handlers = new List<IBackHandler>();

        public ScreenStack Screens { get; }

        public int HandlerCount => _handlers.Count;

        public BackNavigationRouter(ScreenStack? screens = null)
        {
            Screens = screens ?? new ScreenStack();
        }

        /// <summary>Regisztrál egy kezelőt; a visszaadott token dispose-a eltávolítja (sorrendtől függetlenül).</summary>
        public IDisposable PushHandler(IBackHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers.Add(handler);
            return new Registration(this, handler);
        }

        public BackAction Route(AppState state)
        {
            var snapshot = _handlers.ToArray();
            for (int i = snapshot.Length - 1; i >= 0; i--)
                if (snapshot[i].TryHandleBack()) return BackAction.HandledByOverlay;

            if (Screens.Pop() != null) return BackAction.PopScreen;

            return DefaultFor(state);
        }

        public static BackAction DefaultFor(AppState state)
        {
            switch (state)
            {
                case AppState.Simulation: return BackAction.OpenPauseMenu;
                case AppState.Paused: return BackAction.ResumeSimulation;
                case AppState.WorldCreation: return BackAction.ReturnToMainMenu;
                case AppState.Loading: return BackAction.CancelLoading;
                default: return BackAction.None;
            }
        }

        private void Remove(IBackHandler handler)
        {
            for (int i = _handlers.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_handlers[i], handler)) continue;
                _handlers.RemoveAt(i);
                return;
            }
        }

        private sealed class Registration : IDisposable
        {
            private BackNavigationRouter? _router;
            private readonly IBackHandler _handler;

            public Registration(BackNavigationRouter router, IBackHandler handler)
            {
                _router = router;
                _handler = handler;
            }

            public void Dispose()
            {
                _router?.Remove(_handler);
                _router = null;
            }
        }
    }
}
