#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.UI
{
    public sealed class MenuItem
    {
        public string Id { get; }
        public string LabelKey { get; }
        public bool IsEnabled { get; internal set; }
        public bool IsVisible { get; internal set; }

        public bool IsSelectable => IsEnabled && IsVisible;

        public MenuItem(string id, string labelKey, bool isEnabled = true, bool isVisible = true)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Üres menüazonosító.", nameof(id));
            Id = id;
            LabelKey = labelKey ?? "";
            IsEnabled = isEnabled;
            IsVisible = isVisible;
        }
    }

    /// <summary>
    /// Billentyűzettel és egérrel egységesen kezelhető menü (WF-UI-001):
    /// fel/le körbefordul és átugorja a letiltott/rejtett elemeket, az egér
    /// hover kiválaszt, a letiltott elem nem aktiválható.
    /// </summary>
    public sealed class MenuModel
    {
        private readonly List<MenuItem> _items;

        public IReadOnlyList<MenuItem> Items => _items;

        /// <summary>-1, ha nincs kiválasztható elem.</summary>
        public int SelectedIndex { get; private set; } = -1;

        public MenuItem? Selected => SelectedIndex >= 0 ? _items[SelectedIndex] : null;

        public event Action<MenuModel>? SelectionChanged;

        /// <summary>Elem engedélyezettsége vagy láthatósága változott.</summary>
        public event Action<MenuModel>? ItemsChanged;

        public MenuModel(IEnumerable<MenuItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items = new List<MenuItem>(items);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in _items)
            {
                if (item == null) throw new ArgumentException("Null menüelem.", nameof(items));
                if (!ids.Add(item.Id)) throw new ArgumentException("Duplikált menüazonosító: " + item.Id, nameof(items));
            }
            SelectFirst();
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        public bool SelectFirst() => SetSelection(FindFrom(0, +1, includeStart: true));

        public bool Select(string id)
        {
            int i = IndexOf(id);
            return i >= 0 && _items[i].IsSelectable && SetSelection(i);
        }

        public bool MoveNext() => Move(+1);

        public bool MovePrevious() => Move(-1);

        /// <summary>Egér hover: kiválasztja, ha kiválasztható. Nem kiválaszthatónál a kijelölés marad.</summary>
        public bool Hover(int index)
        {
            if (index < 0 || index >= _items.Count || !_items[index].IsSelectable) return false;
            SetSelection(index);
            return true;
        }

        /// <summary>Enter / kattintás a kiválasztott elemen: az azonosító, vagy null.</summary>
        public string? Activate() => Selected != null && Selected.IsSelectable ? Selected.Id : null;

        public string? ActivateAt(int index)
        {
            if (index < 0 || index >= _items.Count || !_items[index].IsSelectable) return null;
            SetSelection(index);
            return _items[index].Id;
        }

        public void SetEnabled(string id, bool enabled) => Update(id, item => item.IsEnabled = enabled);

        public void SetVisible(string id, bool visible) => Update(id, item => item.IsVisible = visible);

        private void Update(string id, Action<MenuItem> change)
        {
            int i = IndexOf(id);
            if (i < 0) throw new ArgumentException("Ismeretlen menüazonosító: " + id, nameof(id));
            var item = _items[i];
            bool wasSelectable = item.IsSelectable;
            change(item);
            ItemsChanged?.Invoke(this);
            if (SelectedIndex == i && wasSelectable && !item.IsSelectable)
            {
                int next = FindFrom(i, +1, includeStart: false);
                SetSelection(next);
            }
            else if (SelectedIndex < 0 && item.IsSelectable)
            {
                SetSelection(i);
            }
        }

        private bool Move(int direction)
        {
            int start = SelectedIndex < 0 ? (direction > 0 ? _items.Count - 1 : 0) : SelectedIndex;
            int next = FindFrom(start, direction, includeStart: false);
            return next >= 0 && SetSelection(next);
        }

        private int FindFrom(int start, int direction, bool includeStart)
        {
            int n = _items.Count;
            if (n == 0) return -1;
            for (int step = includeStart ? 0 : 1; step <= n; step++)
            {
                int i = ((start + direction * step) % n + n) % n;
                if (_items[i].IsSelectable) return i;
            }
            return -1;
        }

        private bool SetSelection(int index)
        {
            if (index == SelectedIndex) return index >= 0;
            SelectedIndex = index;
            SelectionChanged?.Invoke(this);
            return index >= 0;
        }
    }

    public static class MenuIds
    {
        public const string NewWorld = "new-world";
        public const string Continue = "continue";
        public const string LoadWorld = "load-world";
        public const string Settings = "settings";
        public const string Help = "help";
        public const string Credits = "credits";
        public const string Quit = "quit";

        public const string Resume = "resume";
        public const string SaveWorld = "save-world";
        public const string SaveWorldAs = "save-world-as";
        public const string ReturnToMainMenu = "return-to-main-menu";
        public const string QuitToDesktop = "quit-to-desktop";
    }

    public static class MainMenuDefinition
    {
        /// <summary>
        /// A Continue csak folytatható (kompatibilis) mentésnél, a Load csak
        /// bármilyen listázható mentésnél aktív. Kezdő kijelölés: Continue, ha
        /// aktív, különben New World.
        /// </summary>
        public static MenuModel Build(bool hasContinuableSave, bool hasAnySave)
        {
            var menu = new MenuModel(new[]
            {
                new MenuItem(MenuIds.NewWorld, "menu.main.newWorld"),
                new MenuItem(MenuIds.Continue, "menu.main.continue", hasContinuableSave),
                new MenuItem(MenuIds.LoadWorld, "menu.main.loadWorld", hasAnySave),
                new MenuItem(MenuIds.Settings, "menu.main.settings"),
                new MenuItem(MenuIds.Help, "menu.main.help"),
                new MenuItem(MenuIds.Credits, "menu.main.credits"),
                new MenuItem(MenuIds.Quit, "menu.main.quit"),
            });
            if (hasContinuableSave) menu.Select(MenuIds.Continue);
            return menu;
        }
    }

    public static class PauseMenuDefinition
    {
        public static MenuModel Build(bool canSave, bool hasAnySave)
        {
            return new MenuModel(new[]
            {
                new MenuItem(MenuIds.Resume, "menu.pause.resume"),
                new MenuItem(MenuIds.SaveWorld, "menu.pause.saveWorld", canSave),
                new MenuItem(MenuIds.SaveWorldAs, "menu.pause.saveWorldAs", canSave),
                new MenuItem(MenuIds.LoadWorld, "menu.pause.loadWorld", hasAnySave),
                new MenuItem(MenuIds.Settings, "menu.pause.settings"),
                new MenuItem(MenuIds.Help, "menu.pause.help"),
                new MenuItem(MenuIds.ReturnToMainMenu, "menu.pause.returnToMainMenu"),
                new MenuItem(MenuIds.QuitToDesktop, "menu.pause.quitToDesktop"),
            });
        }
    }
}
