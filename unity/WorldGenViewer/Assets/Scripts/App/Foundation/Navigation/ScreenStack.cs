#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.Navigation
{
    /// <summary>
    /// Menüképernyők verme egy állapoton belül (pl. Paused: pause → settings → graphics).
    /// A gyökér nem pattintható le: a gyökérnél a Vissza az állapot-alapértelmezésre esik.
    /// </summary>
    public sealed class ScreenStack
    {
        private readonly List<string> _screens = new List<string>();

        public int Count => _screens.Count;

        public string? Current => _screens.Count > 0 ? _screens[_screens.Count - 1] : null;

        public string? Root => _screens.Count > 0 ? _screens[0] : null;

        public IReadOnlyList<string> Items => _screens;

        public event Action<ScreenStack>? Changed;

        /// <summary>Új gyökér, a verem többi eleme törlődik (állapotváltáskor).</summary>
        public void Reset(string rootScreenId)
        {
            ValidateId(rootScreenId);
            _screens.Clear();
            _screens.Add(rootScreenId);
            Changed?.Invoke(this);
        }

        public void Clear()
        {
            if (_screens.Count == 0) return;
            _screens.Clear();
            Changed?.Invoke(this);
        }

        public void Push(string screenId)
        {
            ValidateId(screenId);
            if (_screens.Count == 0) throw new InvalidOperationException("Push előtt Reset kell (nincs gyökér).");
            if (string.Equals(Current, screenId, StringComparison.Ordinal)) return;
            _screens.Add(screenId);
            Changed?.Invoke(this);
        }

        /// <summary>Visszalép egy szintet; null, ha csak a gyökér (vagy semmi) maradt.</summary>
        public string? Pop()
        {
            if (_screens.Count <= 1) return null;
            string top = _screens[_screens.Count - 1];
            _screens.RemoveAt(_screens.Count - 1);
            Changed?.Invoke(this);
            return top;
        }

        public bool Contains(string screenId) => _screens.Contains(screenId);

        private static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A képernyő-azonosító nem lehet üres.", nameof(id));
        }
    }
}
