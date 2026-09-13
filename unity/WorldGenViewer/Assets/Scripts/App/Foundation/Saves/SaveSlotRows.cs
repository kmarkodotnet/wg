#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using WorldGen.App.WorldInfo;

namespace WorldGen.App.Saves
{
    /// <summary>A Load World lista egy sora megjeleníthető szövegekkel (WF-SAVE-003).</summary>
    public sealed class SaveSlotRow
    {
        internal SaveSlotRow(SaveSlotInfo slot, string title, string age, string created, string lastPlayed, string seed,
            string playTime, string size, string? kindKey)
        {
            Slot = slot;
            Title = title;
            Age = age;
            Created = created;
            LastPlayed = lastPlayed;
            Seed = seed;
            PlayTime = playTime;
            Size = size;
            KindKey = kindKey;
        }

        public SaveSlotInfo Slot { get; }
        public string Title { get; }

        /// <summary>Üres, ha a fejléc nem olvasható (nincs kitalált érték).</summary>
        public string Age { get; }

        public string Created { get; }
        public string LastPlayed { get; }
        public string Seed { get; }
        public string PlayTime { get; }
        public string Size { get; }

        /// <summary>"save.kind.manual" / ".auto" / ".quick"; null, ha nincs fejléc.</summary>
        public string? KindKey { get; }

        public bool CanLoad => Slot.Compatibility.CanLoadState;

        /// <summary>A generátor változott: „új világ ezekkel a paraméterekkel” ajánlható.</summary>
        public bool CanRecreateFromConfiguration => Slot.Compatibility.Level == SaveCompatibilityLevel.ConfigurationOnly;

        public string? StatusMessageKey => Slot.Compatibility.MessageKey;
    }

    public static class SaveSlotRows
    {
        public const string DateFormat = "yyyy-MM-dd HH:mm";

        /// <summary>Sorok a megadott sorrendben; a dátumok a megadott időzónában (a nézet a helyi időzónát adja).</summary>
        public static IReadOnlyList<SaveSlotRow> Build(IReadOnlyList<SaveSlotInfo> slots, TimeZoneInfo timeZone)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            if (timeZone == null) throw new ArgumentNullException(nameof(timeZone));
            var rows = new List<SaveSlotRow>(slots.Count);
            foreach (var slot in slots)
            {
                var h = slot.Header;
                string size = QuantityFormatter.FormatFileSize(slot.SizeBytes);
                if (h == null)
                {
                    rows.Add(new SaveSlotRow(slot, slot.DisplayName, "", "", FormatDate(slot.FileLastWriteUtc, timeZone), "", "", size, null));
                    continue;
                }
                rows.Add(new SaveSlotRow(slot, slot.DisplayName,
                    QuantityFormatter.FormatAge(h.SimulationAgeYears),
                    FormatDate(h.CreatedUtc, timeZone),
                    FormatDate(h.LastPlayedUtc, timeZone),
                    h.Seed.ToString(CultureInfo.InvariantCulture),
                    QuantityFormatter.FormatDuration(h.PlayTimeSeconds),
                    size,
                    "save.kind." + h.Kind.ToString().ToLowerInvariant()));
            }
            return rows;
        }

        public static string FormatDate(DateTime utc, TimeZoneInfo timeZone)
        {
            if (utc == default) return "";
            var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(asUtc, timeZone).ToString(DateFormat, CultureInfo.InvariantCulture);
        }
    }
}
