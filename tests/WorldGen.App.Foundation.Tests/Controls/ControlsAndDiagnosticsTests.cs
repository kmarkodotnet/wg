using System;
using System.Linq;
using System.Threading.Tasks;
using WorldGen.App.Controls;
using WorldGen.App.Diagnostics;
using WorldGen.App.Help;
using WorldGen.App.Localization;
using WorldGen.App.Serialization;
using WorldGen.App.Settings;
using WorldGen.App.Versioning;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Controls
{
    public class KeyChordTests
    {
        [Theory]
        [InlineData("F12", "F12", KeyModifiers.None)]
        [InlineData("shift+F12", "Shift+F12", KeyModifiers.Shift)]
        [InlineData(" Alt + Control + S ", "Ctrl+Alt+S", KeyModifiers.Control | KeyModifiers.Alt)]
        [InlineData("Ctrl+Shift+Alt+Alpha1", "Ctrl+Shift+Alt+Alpha1", KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)]
        public void ParsesToCanonicalText(string text, string canonical, KeyModifiers modifiers)
        {
            Assert.True(KeyChord.TryParse(text, out var chord));
            Assert.Equal(canonical, chord.ToString());
            Assert.Equal(modifiers, chord.Modifiers);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Shift")]
        [InlineData("Shift+")]
        [InlineData("Shift+Shift+F1")]
        [InlineData("Meta+F1")]
        [InlineData("F1+F2")]
        [InlineData("Page Up")]
        [InlineData(null)]
        public void RejectsInvalidText(string? text)
        {
            Assert.False(KeyChord.TryParse(text, out _));
        }

        [Fact]
        public void EqualityIgnoresKeyCase()
        {
            Assert.Equal(new KeyChord("f12", KeyModifiers.Shift), new KeyChord("F12", KeyModifiers.Shift));
            Assert.Equal(new KeyChord("f12").GetHashCode(), new KeyChord("F12").GetHashCode());
            Assert.NotEqual(new KeyChord("F12"), new KeyChord("F12", KeyModifiers.Shift));
            Assert.True(default(KeyChord).IsEmpty);
            Assert.Throws<ArgumentException>(() => new KeyChord("Ctrl"));
        }
    }

    public class KeyBindingMapTests
    {
        [Fact]
        public void DefaultsAreUniqueAndBackIsFixed()
        {
            var map = new KeyBindingMap();
            Assert.Equal(7, map.Actions.Count);
            Assert.Equal(InputActionIds.CleanScreenshot, map.FindAction(new KeyChord("F12", KeyModifiers.Shift)));
            Assert.False(map.TrySetBinding(InputActionIds.Back, new KeyChord("Q"), out var none));
            Assert.Null(none);
            Assert.Throws<ArgumentException>(() => map.GetBinding("missing"));
            Assert.Throws<ArgumentException>(() => new KeyBindingMap(new[]
            {
                new InputActionDefinition("a", "", new KeyChord("F1")), new InputActionDefinition("b", "", new KeyChord("f1")),
            }));
        }

        [Fact]
        public void RebindingRejectsConflictsAndRestores()
        {
            var map = new KeyBindingMap();
            Assert.False(map.TrySetBinding(InputActionIds.QuickSave, new KeyChord("F9"), out var owner));
            Assert.Equal(InputActionIds.QuickLoad, owner);
            Assert.True(map.TrySetBinding(InputActionIds.QuickSave, new KeyChord("S", KeyModifiers.Control), out _));
            Assert.Equal("Ctrl+S", map.GetBinding(InputActionIds.QuickSave).ToString());
            Assert.True(map.TrySetBinding(InputActionIds.QuickSave, new KeyChord("S", KeyModifiers.Control), out _));
            map.RestoreDefaults();
            Assert.Equal("F5", map.GetBinding(InputActionIds.QuickSave).ToString());
        }

        [Fact]
        public void JsonRoundTripAllowsSwapsAndRejectsConflictingFiles()
        {
            var map = new KeyBindingMap();
            var doc = map.ToJson();
            Assert.False(doc.ContainsKey(InputActionIds.Back));

            var swapped = JsonParser.Parse("{\"quick-save\": \"F9\", \"quick-load\": \"F5\", \"back\": \"Q\", \"nope\": \"F2\", \"help\": 5, \"screenshot\": \"Bad Key\"}");
            var issues = map.LoadJson(swapped);
            Assert.Equal(new[] { "back", "nope", "help", "screenshot" }, issues);
            Assert.Equal("F9", map.GetBinding(InputActionIds.QuickSave).ToString());
            Assert.Equal("F5", map.GetBinding(InputActionIds.QuickLoad).ToString());
            Assert.Equal("Escape", map.GetBinding(InputActionIds.Back).ToString());

            var fresh = new KeyBindingMap();
            Assert.Empty(fresh.LoadJson(JsonParser.Parse(JsonWriter.Write(map.ToJson()))));
            Assert.Equal("F9", fresh.GetBinding(InputActionIds.QuickSave).ToString());

            var conflict = fresh.LoadJson(JsonParser.Parse("{\"help\": \"F12\"}"));
            Assert.Equal(new[] { "conflict:F12" }, conflict);
            Assert.Equal("F1", fresh.GetBinding(InputActionIds.Help).ToString());
            Assert.Equal("F5", fresh.GetBinding(InputActionIds.QuickSave).ToString());
            Assert.Equal(new[] { "$" }, fresh.LoadJson(JsonValue.CreateArray()));
        }
    }

    public class ErrorReportingTests
    {
        [Fact]
        public void ThrottleLogsFirstOccurrencesAndCountsTheRest()
        {
            var throttle = new ExceptionThrottle(maxLoggedPerSignature: 2);
            var first = throttle.Register("NullReferenceException: x\nmore", "PlanetGridMesh.Update ()\nother frame");
            Assert.True(first.IsFirst && first.ShouldLog);
            Assert.True(throttle.Register("NullReferenceException: x", "PlanetGridMesh.Update ()\ndifferent tail").ShouldLog);
            var third = throttle.Register("NullReferenceException: x", "PlanetGridMesh.Update ()");
            Assert.False(third.ShouldLog);
            Assert.Equal(3, third.Count);
            Assert.True(throttle.Register("IOException: disk", null).IsFirst);

            var suppressed = throttle.GetSuppressedCounts();
            Assert.Equal("NullReferenceException: x|PlanetGridMesh.Update ()", suppressed.Single().Key);
            Assert.Equal(1, suppressed.Single().Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExceptionThrottle(0));
        }

        [Fact]
        public void ThrottleIsThreadSafe()
        {
            var throttle = new ExceptionThrottle();
            Parallel.For(0, 1000, _ => throttle.Register("E", "at X"));
            Assert.Equal(1001, throttle.Register("E", "at X").Count);
        }

        [Fact]
        public void ReportContainsBuildSystemErrorAndRecentLog()
        {
            var t = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
            var build = new BuildInfo("WorldGen", SemanticVersion.Parse("0.1.0-alpha"), 3);
            var system = new SystemInfoReport { OperatingSystem = "Windows 11", GpuModel = "RTX" };
            var recent = new[] { new LogEntry(t, LogLevel.Warning, "Save", "slow disk", null) };

            string report = ErrorReport.Build(t, build, system, "InvalidOperationException: boom\r\n  at X", recent);

            Assert.Equal(
                "WorldGen error report\nTime: 2026-09-13T12:00:00Z\nBuild: WorldGen 0.1.0-alpha build 3 channel Alpha\n" +
                "OS: Windows 11\nGPU: RTX\n\n--- Error ---\nInvalidOperationException: boom\n  at X\n\n" +
                "--- Recent log (1 entries) ---\n2026-09-13T12:00:00.000Z [WARN ] Save: slow disk\n",
                report);
            Assert.Equal("error-20260913-140509.txt", ErrorReport.CreateFileName(new DateTime(2026, 9, 13, 14, 5, 9)));
            Assert.StartsWith("WorldGen error report\nTime:", ErrorReport.Build(t, null, null, "x", null));
        }

        [Fact]
        public void DebugOverlayListsOnlyAvailableValues()
        {
            var stats = new FrameTimeStats(4);
            Assert.Empty(DebugOverlayText.BuildLines(stats, null));
            foreach (double ms in new[] { 16.0, 16.0, 18.0, 30.0 }) stats.AddSample(ms);
            var lines = DebugOverlayText.BuildLines(stats, new DebugOverlaySample { SimulationStepMilliseconds = 2.345, ActiveChunks = 1280 });
            Assert.Equal(new[] { "FPS 50.0", "Frame 20.0 ms  p95 30.0  max 30.0", "Sim step 2.35 ms", "Active chunks 1,280" }, lines);
        }
    }

    public class HelpPagesTests
    {
        [Fact]
        public void AllHelpTextIsLocalizedAndKeyListFollowsBindings()
        {
            var text = EnglishStrings.CreateTable();
            var bindings = new KeyBindingMap();
            foreach (var page in HelpPages.CreateDefault())
            {
                Assert.True(text.TryGet(page.TitleKey, out _), page.TitleKey);
                foreach (var section in page.Sections)
                {
                    Assert.True(text.TryGet(section.HeadingKey, out _), section.HeadingKey);
                    Assert.DoesNotContain("[", HelpPages.ResolveBody(section, text, bindings));
                }
            }

            var keys = HelpPages.CreateDefault().Single(p => p.Id == HelpPageIds.Controls).Sections.Last();
            string body = HelpPages.ResolveBody(keys, text, bindings);
            Assert.Contains("Shift+F12 — Screenshot without UI", body);
            bindings.TrySetBinding(InputActionIds.Screenshot, new KeyChord("P"), out _);
            Assert.Contains("P — Screenshot\n", HelpPages.ResolveBody(keys, text, bindings));
            Assert.Empty(text.MissingKeys);
        }
    }

    public class QualityLevelMapperTests
    {
        private static readonly string[] Hdrp = { "High Fidelity", "Balanced", "Performant" };
        private static readonly string[] UnityDefault = { "Very Low", "Low", "Medium", "High", "Very High", "Ultra" };

        [Theory]
        [InlineData(GraphicsQuality.Low, 2)]
        [InlineData(GraphicsQuality.Medium, 1)]
        [InlineData(GraphicsQuality.High, 0)]
        [InlineData(GraphicsQuality.Ultra, 0)]
        public void MapsHdrpTemplateLevelsByName(GraphicsQuality quality, int expected)
        {
            Assert.Equal(expected, QualityLevelMapper.Resolve(quality, Hdrp));
        }

        [Theory]
        [InlineData(GraphicsQuality.Low, 1)]
        [InlineData(GraphicsQuality.Medium, 2)]
        [InlineData(GraphicsQuality.High, 3)]
        [InlineData(GraphicsQuality.Ultra, 5)]
        public void ExactNamesWinOnUnityDefaultLevels(GraphicsQuality quality, int expected)
        {
            Assert.Equal(expected, QualityLevelMapper.Resolve(quality, UnityDefault));
        }

        [Fact]
        public void UnknownOrPartialNamesFallBackSensibly()
        {
            var unknown = new[] { "A", "B", "C" };
            Assert.Equal(0, QualityLevelMapper.Resolve(GraphicsQuality.Low, unknown));
            Assert.Equal(1, QualityLevelMapper.Resolve(GraphicsQuality.Medium, unknown));
            Assert.Equal(1, QualityLevelMapper.Resolve(GraphicsQuality.High, unknown));
            Assert.Equal(2, QualityLevelMapper.Resolve(GraphicsQuality.Ultra, unknown));
            Assert.Equal(1, QualityLevelMapper.Resolve(GraphicsQuality.Ultra, new[] { "Custom", "Balanced" }));
            Assert.Equal(0, QualityLevelMapper.Resolve(GraphicsQuality.Low, new[] { "Balanced", "High" }));
            Assert.Equal(-1, QualityLevelMapper.Resolve(GraphicsQuality.High, Array.Empty<string>()));
        }
    }
}
