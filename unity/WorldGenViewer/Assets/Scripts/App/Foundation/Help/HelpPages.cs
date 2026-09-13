#nullable enable
using System;
using System.Collections.Generic;
using WorldGen.App.Controls;
using WorldGen.App.Localization;

namespace WorldGen.App.Help
{
    public sealed class HelpSection
    {
        public HelpSection(string headingKey, string bodyKey)
        {
            if (string.IsNullOrEmpty(bodyKey)) throw new ArgumentException("Üres szövegkulcs.", nameof(bodyKey));
            HeadingKey = headingKey ?? "";
            BodyKey = bodyKey;
        }

        /// <summary>Üres, ha a szakasznak nincs címe.</summary>
        public string HeadingKey { get; }

        public string BodyKey { get; }
    }

    public sealed class HelpPage
    {
        public HelpPage(string id, string titleKey, IReadOnlyList<HelpSection> sections)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Üres oldalazonosító.", nameof(id));
            Id = id;
            TitleKey = titleKey ?? "";
            Sections = sections ?? throw new ArgumentNullException(nameof(sections));
        }

        public string Id { get; }
        public string TitleKey { get; }
        public IReadOnlyList<HelpSection> Sections { get; }
    }

    public static class HelpPageIds
    {
        public const string Controls = "controls";
        public const string DeepTime = "deep-time";
    }

    /// <summary>
    /// A Help-oldalak szerkezete (WF-HELP-001, WF-HELP-003). A szöveg a lokalizációs
    /// táblában van. Az overlay-help (WF-HELP-002) a Core-kötéssel jön, mert a
    /// skálák és a jelentések még változnak.
    /// </summary>
    public static class HelpPages
    {
        public static IReadOnlyList<HelpPage> CreateDefault() => new[]
        {
            new HelpPage(HelpPageIds.Controls, "help.controls.title", new[]
            {
                new HelpSection("help.controls.camera.heading", "help.controls.camera.body"),
                new HelpSection("help.controls.navigation.heading", "help.controls.navigation.body"),
                new HelpSection("help.controls.keys.heading", "help.controls.keys.body"),
            }),
            new HelpPage(HelpPageIds.DeepTime, "help.deepTime.title", new[]
            {
                new HelpSection("help.deepTime.what.heading", "help.deepTime.what.body"),
                new HelpSection("help.deepTime.determinism.heading", "help.deepTime.determinism.body"),
                new HelpSection("help.deepTime.approximation.heading", "help.deepTime.approximation.body"),
            }),
        };

        /// <summary>Egy szakasz szövege; a billentyűs szakasz az aktuális kiosztásból épül (ne avuljon el átállításkor).</summary>
        public static string ResolveBody(HelpSection section, LocalizationTable text, KeyBindingMap bindings)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (section.BodyKey != "help.controls.keys.body") return text.Get(section.BodyKey);
            var lines = new List<string>();
            foreach (var action in bindings.Actions)
                lines.Add(bindings.GetBinding(action.Id) + " — " + text.Get(action.LabelKey));
            return text.Format(section.BodyKey, string.Join("\n", lines));
        }
    }
}
