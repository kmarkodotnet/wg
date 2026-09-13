using System;
using System.Linq;
using WorldGen.App.Serialization;
using WorldGen.App.Settings;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Settings
{
    public class SettingsSerializerTests
    {
        /// <summary>A settings-séma v1 rögzített alakja. Ha ez változik, az schema-verzióemelés kérdése.</summary>
        private const string DefaultsJson =
            "{\n" +
            "  \"version\": 1,\n" +
            "  \"graphics\": {\n" +
            "    \"displayMode\": \"Borderless\",\n" +
            "    \"resolution\": null,\n" +
            "    \"vSync\": true,\n" +
            "    \"fpsLimit\": 0,\n" +
            "    \"backgroundFpsLimit\": 30,\n" +
            "    \"quality\": \"High\"\n" +
            "  },\n" +
            "  \"audio\": {\n" +
            "    \"master\": 1,\n" +
            "    \"music\": 0.7,\n" +
            "    \"ambient\": 0.8,\n" +
            "    \"effects\": 0.8,\n" +
            "    \"ui\": 0.6,\n" +
            "    \"muted\": false\n" +
            "  },\n" +
            "  \"controls\": {\n" +
            "    \"mouseSensitivity\": 1,\n" +
            "    \"zoomSensitivity\": 1,\n" +
            "    \"cameraSpeed\": 1,\n" +
            "    \"invertOrbitY\": false\n" +
            "  },\n" +
            "  \"simulation\": {\n" +
            "    \"quality\": \"Balanced\",\n" +
            "    \"autosaveIntervalMinutes\": 10,\n" +
            "    \"autosaveBeforeDeepTimeJump\": false,\n" +
            "    \"maxAutosavesPerWorld\": 3,\n" +
            "    \"pauseWhenUnfocused\": true\n" +
            "  },\n" +
            "  \"interface\": {\n" +
            "    \"language\": \"en\",\n" +
            "    \"uiScale\": 1,\n" +
            "    \"tooltipDelaySeconds\": 0.5,\n" +
            "    \"showConfirmations\": true,\n" +
            "    \"showWelcomeOnStartup\": true,\n" +
            "    \"showDebugOverlay\": false,\n" +
            "    \"reduceMotion\": false\n" +
            "  }\n" +
            "}\n";

        internal static AppSettings AllNonDefault()
        {
            var s = new AppSettings();
            s.Graphics.DisplayMode = DisplayMode.Windowed;
            s.Graphics.Resolution = new ScreenResolution(2560, 1440, 143.856);
            s.Graphics.VSync = false;
            s.Graphics.FpsLimit = 144;
            s.Graphics.BackgroundFpsLimit = 10;
            s.Graphics.Quality = GraphicsQuality.Ultra;
            s.Audio.Master = 0.9;
            s.Audio.Music = 0.25;
            s.Audio.Ambient = 0.33;
            s.Audio.Effects = 0.1;
            s.Audio.Ui = 0;
            s.Audio.Muted = true;
            s.Controls.MouseSensitivity = 2.5;
            s.Controls.ZoomSensitivity = 0.4;
            s.Controls.CameraSpeed = 3;
            s.Controls.InvertOrbitY = true;
            s.Simulation.Quality = SimulationQuality.Accurate;
            s.Simulation.AutosaveIntervalMinutes = 20;
            s.Simulation.AutosaveBeforeDeepTimeJump = true;
            s.Simulation.MaxAutosavesPerWorld = 7;
            s.Simulation.PauseWhenUnfocused = false;
            s.Interface.Language = "hu";
            s.Interface.UiScale = 1.25;
            s.Interface.TooltipDelaySeconds = 1.2;
            s.Interface.ShowConfirmations = false;
            s.Interface.ShowWelcomeOnStartup = false;
            s.Interface.ShowDebugOverlay = true;
            s.Interface.ReduceMotion = true;
            return s;
        }

        [Fact]
        public void DefaultsSerializeToStableSchema()
        {
            Assert.Equal(DefaultsJson, SettingsSerializer.Serialize(new AppSettings()));
        }

        [Fact]
        public void EveryFieldRoundTrips()
        {
            var original = AllNonDefault();
            Assert.Equal(SettingsCategory.All, new AppSettings().DiffCategories(original));

            var read = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original));

            Assert.Empty(read.Issues);
            Assert.False(read.IsCorrupt);
            Assert.Equal(1, read.FileVersion);
            Assert.Equal(SettingsCategory.None, original.DiffCategories(read.Settings));
            Assert.Equal(new ScreenResolution(2560, 1440, 143.856), read.Settings.Graphics.Resolution);
        }

        [Fact]
        public void SerializationIsIdempotent()
        {
            string once = SettingsSerializer.Serialize(AllNonDefault());
            string twice = SettingsSerializer.Serialize(SettingsSerializer.Deserialize(once).Settings);
            Assert.Equal(once, twice);
        }

        [Fact]
        public void InvalidFieldFallsBackIndividually()
        {
            var doc = JsonParser.Parse(SettingsSerializer.Serialize(AllNonDefault()));
            doc.GetMember("graphics")!.Set("vSync", "yes");
            doc.GetMember("audio")!.Set("music", JsonValue.Null);
            doc.GetMember("graphics")!.Set("displayMode", "1");
            doc.GetMember("simulation")!.Set("quality", "accurate");

            var read = SettingsSerializer.Deserialize(JsonWriter.Write(doc));

            Assert.False(read.IsCorrupt);
            Assert.True(read.Settings.Graphics.VSync);
            Assert.Equal(0.7, read.Settings.Audio.Music);
            Assert.Equal(DisplayMode.Borderless, read.Settings.Graphics.DisplayMode);
            Assert.Equal(SimulationQuality.Balanced, read.Settings.Simulation.Quality);
            // a többi mező megmaradt
            Assert.Equal(144, read.Settings.Graphics.FpsLimit);
            Assert.True(read.Settings.Audio.Muted);
            Assert.Equal(
                new[] { "graphics.displayMode", "graphics.vSync", "audio.music", "simulation.quality" },
                read.Issues.Where(i => i.Kind == SettingsIssueKind.Invalid).Select(i => i.Path));
        }

        [Fact]
        public void OutOfRangeValuesAreClampedAndReported()
        {
            var doc = JsonParser.Parse(SettingsSerializer.Serialize(new AppSettings()));
            doc.GetMember("graphics")!.Set("fpsLimit", 10);
            doc.GetMember("graphics")!.Set("resolution", JsonValue.CreateObject().Set("width", 0).Set("height", 1080));
            doc.GetMember("audio")!.Set("master", 2.5);
            doc.GetMember("simulation")!.Set("autosaveIntervalMinutes", 15);
            doc.GetMember("interface")!.Set("language", "x");
            doc.GetMember("interface")!.Set("uiScale", 0.1);

            var read = SettingsSerializer.Deserialize(JsonWriter.Write(doc));

            Assert.Equal(30, read.Settings.Graphics.FpsLimit);
            Assert.False(read.Settings.Graphics.Resolution.IsSpecified);
            Assert.Equal(1.0, read.Settings.Audio.Master);
            Assert.Equal(10, read.Settings.Simulation.AutosaveIntervalMinutes);
            Assert.Equal("en", read.Settings.Interface.Language);
            Assert.Equal(0.75, read.Settings.Interface.UiScale);
            Assert.Equal(
                new[] { "graphics.resolution", "graphics.fpsLimit", "audio.master", "simulation.autosaveIntervalMinutes", "interface.language", "interface.uiScale" },
                read.Issues.Where(i => i.Kind == SettingsIssueKind.OutOfRange).Select(i => i.Path));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{\"version\": 1, \"graphics\": ")]
        [InlineData("[1, 2, 3]")]
        [InlineData("{\"version\": \"abc\"}")]
        [InlineData("{\"version\": 0}")]
        [InlineData("{\"version\": 1.5}")]
        public void CorruptDocumentsYieldDefaults(string text)
        {
            var read = SettingsSerializer.Deserialize(text);
            Assert.True(read.IsCorrupt);
            Assert.Equal(SettingsCategory.None, new AppSettings().DiffCategories(read.Settings));
            Assert.Equal(SettingsIssueKind.Corrupt, Assert.Single(read.Issues).Kind);
        }

        [Fact]
        public void MissingVersionAndCategoriesAreTolerated()
        {
            var read = SettingsSerializer.Deserialize("{\"audio\": {\"muted\": true}}");
            Assert.False(read.IsCorrupt);
            Assert.True(read.Settings.Audio.Muted);
            Assert.Contains(read.Issues, i => i.Kind == SettingsIssueKind.Missing && i.Path == "version");
            Assert.Contains(read.Issues, i => i.Kind == SettingsIssueKind.Missing && i.Path == "graphics");
            Assert.Contains(read.Issues, i => i.Kind == SettingsIssueKind.Missing && i.Path == "audio.master");
        }

        [Fact]
        public void NewerVersionIsReadTolerantlyAndFlagged()
        {
            var read = SettingsSerializer.Deserialize(
                "{\"version\": 99, \"audio\": {\"master\": 0.5, \"spatial\": true}, \"telemetry\": {}}");
            Assert.True(read.IsFromNewerVersion);
            Assert.Equal(99, read.FileVersion);
            Assert.Equal(0.5, read.Settings.Audio.Master);
            Assert.Contains(read.Issues, i => i.Kind == SettingsIssueKind.NewerVersion);
        }

        [Fact]
        public void MigrationsChainStepByStep()
        {
            var migrations = new SettingsMigrations();
            migrations.Register(1, doc => doc.Set("step1", true));
            migrations.Register(2, doc => doc.Set("step2", true));

            Assert.True(migrations.CanMigrate(1, 3));
            Assert.False(migrations.CanMigrate(1, 4));
            var result = migrations.Apply(JsonValue.CreateObject(), 1, 3);
            Assert.True(result.ContainsKey("step1") && result.ContainsKey("step2"));
            Assert.Throws<InvalidOperationException>(() => migrations.Register(1, d => d));
            Assert.Throws<InvalidOperationException>(() => migrations.Apply(JsonValue.CreateObject(), 1, 4));

            var bad = new SettingsMigrations();
            bad.Register(1, _ => JsonValue.CreateArray());
            Assert.Throws<InvalidOperationException>(() => bad.Apply(JsonValue.CreateObject(), 1, 2));
        }

        [Fact]
        public void NonFiniteValuesSetInCodeSerializeAsDefaults()
        {
            var s = new AppSettings();
            s.Audio.Master = double.NaN;
            s.Controls.CameraSpeed = double.PositiveInfinity;
            var read = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(s));
            Assert.Equal(1.0, read.Settings.Audio.Master);
            Assert.Equal(1.0, read.Settings.Controls.CameraSpeed);
        }

        [Theory]
        [InlineData(-5, 0)]
        [InlineData(0, 0)]
        [InlineData(2, 0)]
        [InlineData(3, 5)]
        [InlineData(7, 5)]
        [InlineData(8, 10)]
        [InlineData(15, 10)]
        [InlineData(16, 20)]
        [InlineData(1000, 20)]
        public void AutosaveIntervalSnapsToAllowedValues(int input, int expected)
        {
            Assert.Equal(expected, SimulationSettings.SnapAutosaveInterval(input));
        }

        [Fact]
        public void DiffAndRestoreDefaultsWorkPerCategory()
        {
            var s = AllNonDefault();
            s.RestoreDefaults(SettingsCategory.Audio | SettingsCategory.Interface);
            Assert.Equal(SettingsCategory.Graphics | SettingsCategory.Controls | SettingsCategory.Simulation,
                new AppSettings().DiffCategories(s));

            var clone = s.Clone();
            clone.Graphics.VSync = !clone.Graphics.VSync;
            Assert.Equal(SettingsCategory.Graphics, s.DiffCategories(clone));
            Assert.NotEqual(s.Graphics.VSync, clone.Graphics.VSync);
        }

        [Theory]
        [InlineData("en", true)]
        [InlineData("pt-BR", true)]
        [InlineData(" hu ", true)]
        [InlineData("e", false)]
        [InlineData("1en", false)]
        [InlineData("en_US", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void LanguageTagValidation(string? tag, bool expected)
        {
            Assert.Equal(expected, InterfaceSettings.IsValidLanguageTag(tag));
        }
    }
}
