#nullable enable
using System;

namespace WorldGen.App.Settings
{
    /// <summary>
    /// A Settings-képernyő Apply / Cancel / Restore Defaults mintája. A UI a
    /// <see cref="Working"/> példányt szerkeszti; a tároló csak Apply-kor változik.
    /// </summary>
    public sealed class SettingsEditSession
    {
        private readonly SettingsStore _store;

        public AppSettings Working { get; private set; }

        public SettingsEditSession(SettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            Working = store.Snapshot;
        }

        /// <summary>A normalizált munkapéldány eltérése a tárolttól.</summary>
        public SettingsCategory DirtyCategories
        {
            get
            {
                var normalized = Working.Clone();
                normalized.Normalize();
                return _store.Snapshot.DiffCategories(normalized);
            }
        }

        public bool IsDirty => DirtyCategories != SettingsCategory.None;

        public SettingsCategory Apply()
        {
            var changed = _store.Apply(Working);
            Working = _store.Snapshot;
            return changed;
        }

        public void Cancel() => Working = _store.Snapshot;

        /// <summary>Csak a munkapéldányt állítja vissza; az Apply véglegesíti.</summary>
        public void RestoreDefaults(SettingsCategory categories) => Working.RestoreDefaults(categories);
    }
}
