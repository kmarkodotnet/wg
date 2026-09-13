using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldGen.App.Foundation.Tests.Saves;
using WorldGen.App.Localization;
using WorldGen.App.Saves;
using WorldGen.App.Settings;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Presentation
{
    public class SettingsScreenModelTests
    {
        private static readonly ScreenResolution[] Modes =
        {
            new ScreenResolution(1920, 1080, 144), new ScreenResolution(1280, 720, 60), new ScreenResolution(1920, 1080, 60),
        };

        private static IReadOnlyList<SettingOption> Options() => SettingsScreenModel.Create(Modes, new[] { "en" });

        [Fact]
        public void EveryOptionChangesExactlyItsOwnCategoryAndSurvivesPersistence()
        {
            foreach (var option in Options())
            {
                var settings = new AppSettings();
                switch (option.Kind)
                {
                    case SettingKind.Toggle:
                        option.SetToggle(settings, !option.GetToggle(settings));
                        break;
                    case SettingKind.Slider:
                        double target = Math.Abs(option.GetNumber(settings) - option.Maximum) < 1e-9 ? option.Minimum : option.Maximum;
                        option.SetNumber(settings, target);
                        break;
                    default:
                        string current = option.GetChoice(settings);
                        var other = option.Choices.FirstOrDefault(c => c.Id != current);
                        if (other == null) continue; // egyetlen nyelv: nincs mire váltani
                        Assert.True(option.SetChoice(settings, other.Id));
                        break;
                }

                Assert.True(option.Category == new AppSettings().DiffCategories(settings), option.Id);

                var reloaded = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings));
                Assert.Empty(reloaded.Issues);
                Assert.True(SettingsCategory.None == settings.DiffCategories(reloaded.Settings), option.Id);
            }
        }

        [Fact]
        public void IdsAreUniqueAndAllLabelsAreLocalized()
        {
            var text = EnglishStrings.CreateTable();
            var options = Options();
            Assert.Equal(options.Count, options.Select(o => o.Id).Distinct().Count());
            Assert.Equal(28, options.Count);
            foreach (var o in options)
            {
                Assert.True(text.TryGet(o.LabelKey, out _), o.LabelKey);
                foreach (var c in o.Choices.Where(c => c.IsLocalizationKey))
                    Assert.True(text.TryGet(c.Label, out _), c.Label);
                Assert.DoesNotContain("[", o.FormatValue(new AppSettings(), text));
            }
            foreach (SettingsCategory category in new[] { SettingsCategory.Graphics, SettingsCategory.Audio, SettingsCategory.Controls, SettingsCategory.Simulation, SettingsCategory.Interface })
                Assert.True(text.TryGet(SettingsScreenModel.CategoryLabelKey(category), out _), category.ToString());
        }

        [Fact]
        public void SlidersSnapClampAndFormat()
        {
            var text = EnglishStrings.CreateTable();
            var options = Options().ToDictionary(o => o.Id);
            var s = new AppSettings();

            options["audio.music"].SetNumber(s, 0.72);
            Assert.Equal(0.7, s.Audio.Music);
            Assert.Equal("70%", options["audio.music"].FormatValue(s, text));
            options["audio.music"].SetNumber(s, 7);
            Assert.Equal(1.0, s.Audio.Music);
            options["audio.music"].SetNumber(s, double.NaN);
            Assert.Equal(1.0, s.Audio.Music);

            options["controls.mouseSensitivity"].SetNumber(s, 1.46);
            Assert.Equal(1.5, s.Controls.MouseSensitivity);
            Assert.Equal("1.5×", options["controls.mouseSensitivity"].FormatValue(s, text));
            Assert.Equal("0.5 s", options["interface.tooltipDelaySeconds"].FormatValue(s, text));
            Assert.Equal("100%", options["interface.uiScale"].FormatValue(s, text));
            Assert.Equal("On", options["graphics.vSync"].FormatValue(s, text));
        }

        [Fact]
        public void ChoicesFormatAndRejectUnknownIds()
        {
            var text = EnglishStrings.CreateTable();
            var options = Options().ToDictionary(o => o.Id);
            var s = new AppSettings();

            var resolution = options["graphics.resolution"];
            Assert.Equal(new[] { "native", "1920x1080@144", "1280x720@60" }, resolution.Choices.Select(c => c.Id));
            Assert.Equal("Native", resolution.FormatValue(s, text));
            Assert.True(resolution.SetChoice(s, "1920x1080@144"));
            Assert.Equal(new ScreenResolution(1920, 1080, 144), s.Graphics.Resolution);
            Assert.Equal("1920 × 1080 @ 144 Hz", resolution.FormatValue(s, text));
            Assert.False(resolution.SetChoice(s, "800x600"));

            Assert.Equal("Unlimited", options["graphics.fpsLimit"].FormatValue(s, text));
            Assert.Equal("30", options["graphics.backgroundFpsLimit"].FormatValue(s, text));
            Assert.Equal("10 min", options["simulation.autosaveInterval"].FormatValue(s, text));
            options["simulation.autosaveInterval"].SetChoice(s, "0");
            Assert.Equal("Off", options["simulation.autosaveInterval"].FormatValue(s, text));
            Assert.Equal("Borderless Window", options["graphics.displayMode"].FormatValue(s, text));

            Assert.Throws<InvalidOperationException>(() => options["graphics.vSync"].GetNumber(s));
            Assert.Throws<InvalidOperationException>(() => options["audio.music"].SetChoice(s, "x"));
        }
    }

    public class SaveSlotRowsTests
    {
        [Fact]
        public void BuildsDisplayRowsInGivenTimeZone()
        {
            var fs = new InMemoryFileSystem();
            string dir = Path.Combine(InMemoryFileSystem.Root, "Saves");
            var repo = new SaveRepository(fs, dir, h => SaveCompatibility.Evaluate(h, 1, "gen-7"));
            repo.Write(repo.CreateFilePath("Gaia", SaveKind.Manual, SaveSamples.Created), SaveSamples.Header("Gaia"), SaveSamples.Sections());
            repo.Write(repo.CreateFilePath("Old", SaveKind.Auto, SaveSamples.Created),
                SaveSamples.Header("Old", SaveSamples.Created.AddHours(1), SaveKind.Auto, generator: "gen-6"), SaveSamples.Sections());
            fs.WriteText(Path.Combine(dir, "broken.wgsave"), "nope");

            var plus2 = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");
            var rows = SaveSlotRows.Build(repo.List(), plus2).ToDictionary(r => r.Title);

            var gaia = rows["Gaia"];
            Assert.Equal("2.73 Ga", gaia.Age);
            Assert.Equal("2026-09-13 12:00", gaia.Created);
            Assert.Equal("2026-09-13 14:00", gaia.LastPlayed);
            Assert.Equal("18446744073709551615", gaia.Seed);
            Assert.Equal("1 h 30 min", gaia.PlayTime);
            Assert.Equal("save.kind.manual", gaia.KindKey);
            Assert.True(gaia.CanLoad);
            Assert.Null(gaia.StatusMessageKey);

            var old = rows["Old"];
            Assert.False(old.CanLoad);
            Assert.True(old.CanRecreateFromConfiguration);
            Assert.Equal("save.compat.generatorChanged", old.StatusMessageKey);
            Assert.Equal("save.kind.auto", old.KindKey);

            var broken = rows["broken"];
            Assert.Equal("", broken.Age);
            Assert.Equal("", broken.Seed);
            Assert.Null(broken.KindKey);
            Assert.Equal("save.compat.corrupted", broken.StatusMessageKey);
            Assert.Equal("4 B", broken.Size);

            var text = EnglishStrings.CreateTable();
            foreach (var row in rows.Values.Where(r => r.KindKey != null)) Assert.True(text.TryGet(row.KindKey!, out _));
            Assert.Equal("", SaveSlotRows.FormatDate(default, TimeZoneInfo.Utc));
        }
    }
}
