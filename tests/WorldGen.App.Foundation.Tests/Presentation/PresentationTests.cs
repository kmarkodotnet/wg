using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldGen.App.Audio;
using WorldGen.App.Localization;
using WorldGen.App.Screenshots;
using WorldGen.App.Settings;
using WorldGen.App.WorldInfo;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Presentation
{
    public class VolumeMathTests
    {
        [Theory]
        [InlineData(1.0, 0.0)]
        [InlineData(0.5, -6.020599913279624)]
        [InlineData(0.1, -20.0)]
        [InlineData(0.0, -80.0)]
        [InlineData(-1.0, -80.0)]
        [InlineData(1e-5, -80.0)]
        [InlineData(2.0, 0.0)]
        public void LinearToDecibels(double linear, double expectedDb)
        {
            Assert.Equal(expectedDb, VolumeMath.LinearToDecibels(linear), 9);
        }

        [Fact]
        public void DecibelsToLinearInvertsAndClamps()
        {
            Assert.Equal(1.0, VolumeMath.DecibelsToLinear(0));
            Assert.Equal(0.25, VolumeMath.DecibelsToLinear(VolumeMath.LinearToDecibels(0.25)), 12);
            Assert.Equal(0.0, VolumeMath.DecibelsToLinear(-80));
            Assert.Equal(1.0, VolumeMath.DecibelsToLinear(6));
            Assert.Equal(0.0, VolumeMath.DecibelsToLinear(double.NaN));
            Assert.Equal(-80.0, VolumeMath.LinearToDecibels(double.NaN));
        }
    }

    public class AudioMixModelTests
    {
        [Fact]
        public void GroupsGetOwnVolumeAndMuteActsOnMaster()
        {
            var s = new AudioSettings { Master = 0.5, Music = 0.7 };
            Assert.Equal(VolumeMath.LinearToDecibels(0.5), AudioMixModel.GroupDecibels(s, AudioChannel.Master));
            Assert.Equal(VolumeMath.LinearToDecibels(0.7), AudioMixModel.GroupDecibels(s, AudioChannel.Music));
            Assert.Equal(0.35, AudioMixModel.EffectiveLinear(s, AudioChannel.Music), 12);
            Assert.Equal(0.5, AudioMixModel.EffectiveLinear(s, AudioChannel.Master));

            s.Muted = true;
            Assert.Equal(-80.0, AudioMixModel.GroupDecibels(s, AudioChannel.Master));
            Assert.Equal(VolumeMath.LinearToDecibels(0.7), AudioMixModel.GroupDecibels(s, AudioChannel.Music));
            Assert.Equal(0.0, AudioMixModel.EffectiveLinear(s, AudioChannel.Effects));
        }

        [Fact]
        public void ExposesAllFiveParametersAndClampsUnnormalizedInput()
        {
            var parameters = AudioMixModel.AllParameters(new AudioSettings { Master = 3.0, Ui = double.NaN });
            Assert.Equal(new[] { "MasterVolume", "MusicVolume", "AmbientVolume", "EffectsVolume", "UiVolume" }, parameters.Select(p => p.Key));
            Assert.Equal(0.0, parameters[0].Value);
            Assert.Equal(-80.0, parameters[4].Value);
            Assert.Equal(1.0 * 0.6, AudioMixModel.EffectiveLinear(new AudioSettings { Master = 3.0, Ui = double.NaN }, AudioChannel.Ui), 12);
        }
    }

    public class UiSoundThrottleTests
    {
        [Fact]
        public void LimitsRepeatsPerEvent()
        {
            var throttle = new UiSoundThrottle();
            Assert.True(throttle.TryPlay(UiSoundEvent.Hover, 10.00));
            Assert.False(throttle.TryPlay(UiSoundEvent.Hover, 10.05));
            Assert.True(throttle.TryPlay(UiSoundEvent.Click, 10.05));
            Assert.True(throttle.TryPlay(UiSoundEvent.Hover, 10.08));
            Assert.True(throttle.TryPlay(UiSoundEvent.Hover, 5.0)); // az óra visszaugrott (pl. új session): nem némít el
            Assert.False(throttle.TryPlay(UiSoundEvent.Error, double.NaN));

            throttle.SetMinimumInterval(UiSoundEvent.Click, 1.0);
            Assert.False(throttle.TryPlay(UiSoundEvent.Click, 10.5));
            throttle.Reset();
            Assert.True(throttle.TryPlay(UiSoundEvent.Click, 10.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => throttle.SetMinimumInterval(UiSoundEvent.Back, -1));
        }
    }

    public class FadeEnvelopeTests
    {
        [Fact]
        public void LinearAndEqualPowerCurves()
        {
            var linear = new FadeEnvelope(0, 1, 2.0);
            Assert.Equal(0.25, linear.Tick(0.5), 12);
            linear.Tick(double.NaN);
            linear.Tick(-1);
            Assert.Equal(0.25, linear.Progress, 12);
            linear.Tick(10);
            Assert.True(linear.IsComplete);
            Assert.Equal(1.0, linear.Value);

            for (double t = 0; t <= 1.0; t += 0.125)
            {
                double fadeIn = FadeEnvelope.Evaluate(0, 1, t, FadeCurve.EqualPower);
                double fadeOut = FadeEnvelope.Evaluate(1, 0, t, FadeCurve.EqualPower);
                Assert.Equal(1.0, fadeIn * fadeIn + fadeOut * fadeOut, 12);
            }
            Assert.Equal(Math.Sqrt(0.5), FadeEnvelope.Evaluate(0, 1, 0.5, FadeCurve.EqualPower), 12);
            Assert.Equal(0.3, FadeEnvelope.Evaluate(0.6, 0, 0.5, FadeCurve.Linear), 12);
        }

        [Fact]
        public void ZeroDurationCompletesImmediatelyAndArgumentsAreValidated()
        {
            var instant = new FadeEnvelope(0.2, 0.8, 0);
            Assert.True(instant.IsComplete);
            Assert.Equal(0.8, instant.Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => new FadeEnvelope(0, 1, -1));
            Assert.Throws<ArgumentException>(() => new FadeEnvelope(double.NaN, 1, 1));
        }
    }

    public class MusicDirectorTests
    {
        private static SequentialPlaylist Playlist()
        {
            var p = new SequentialPlaylist();
            p.Set(MusicState.MainMenu, "menu1");
            p.Set(MusicState.Space, "space1", "space2");
            return p;
        }

        [Fact]
        public void PlaylistWrapsAndHandlesUnknownStates()
        {
            var p = Playlist();
            Assert.Equal("space1", p.NextTrack(MusicState.Space, null));
            Assert.Equal("space2", p.NextTrack(MusicState.Space, "space1"));
            Assert.Equal("space1", p.NextTrack(MusicState.Space, "space2"));
            Assert.Equal("space1", p.NextTrack(MusicState.Space, "removed"));
            Assert.Null(p.NextTrack(MusicState.Ocean, null));
            Assert.Throws<ArgumentException>(() => p.Set(MusicState.Life, ""));
        }

        [Fact]
        public void CrossfadesBetweenStates()
        {
            var director = new MusicDirector(Playlist(), crossfadeSeconds: 3.0);
            var log = new List<string>();
            director.TrackStarted += l => log.Add("start " + l.TrackId);
            director.TrackStopped += l => log.Add("stop " + l.TrackId);

            director.RequestState(MusicState.MainMenu);
            Assert.Equal(0.0, director.Current!.Gain);
            director.Tick(1.5);
            Assert.Equal(Math.Sqrt(0.5), director.Current.Gain, 12);
            director.Tick(1.5);
            Assert.Equal(1.0, director.Current.Gain);

            director.RequestState(MusicState.Space);
            director.RequestState(MusicState.Space);
            Assert.Equal("menu1", director.Outgoing!.TrackId);
            director.Tick(1.5);
            double sumOfSquares = director.Current!.Gain * director.Current.Gain + director.Outgoing!.Gain * director.Outgoing.Gain;
            Assert.Equal(1.0, sumOfSquares, 12);
            director.Tick(1.5);

            Assert.Null(director.Outgoing);
            Assert.Equal("space1", director.Current.TrackId);
            Assert.Equal(new[] { "start menu1", "start space1", "stop menu1" }, log);
        }

        [Fact]
        public void InterruptedCrossfadeStopsOldestLayer()
        {
            var director = new MusicDirector(Playlist(), 3.0);
            var stopped = new List<string>();
            director.TrackStopped += l => stopped.Add(l.TrackId);

            director.RequestState(MusicState.MainMenu, immediate: true);
            Assert.Equal(1.0, director.Current!.Gain);
            director.RequestState(MusicState.Space);
            director.Tick(1.0);
            director.RequestState(MusicState.Ocean); // nincs zene: csak kifade

            Assert.Equal(new[] { "menu1" }, stopped);
            Assert.Null(director.Current);
            Assert.Equal("space1", director.Outgoing!.TrackId);
            double gainAtSwitch = director.Outgoing.Gain;
            Assert.True(gainAtSwitch > 0 && gainAtSwitch < 1);
            director.Tick(3.0);
            Assert.Equal(new[] { "menu1", "space1" }, stopped);

            director.RequestState(MusicState.Space, immediate: true);
            director.RequestState(MusicState.None, immediate: true);
            Assert.Null(director.Current);
            Assert.Null(director.Outgoing);
        }

        [Fact]
        public void FinishedTrackAdvancesPlaylistOnlyWhenThereIsAnotherTrack()
        {
            var director = new MusicDirector(Playlist(), 2.0);
            director.RequestState(MusicState.MainMenu, immediate: true);
            director.NotifyTrackFinished();
            Assert.Equal("menu1", director.Current!.TrackId);
            Assert.Null(director.Outgoing);

            director.RequestState(MusicState.Space, immediate: true);
            director.NotifyTrackFinished();
            Assert.Equal("space2", director.Current!.TrackId);
            Assert.Equal("space1", director.Outgoing!.TrackId);
        }
    }

    public class QuantityFormatterTests
    {
        [Theory]
        [InlineData(0.0, "0 yr")]
        [InlineData(350.4, "350 yr")]
        [InlineData(999.6, "1.00 ka")]
        [InlineData(12500.0, "12.5 ka")]
        [InlineData(999999.0, "1.00 Ma")]
        [InlineData(540e6, "540 Ma")]
        [InlineData(2.73e9, "2.73 Ga")]
        [InlineData(1e9, "1.00 Ga")]
        [InlineData(5e12, "5000 Ga")]
        [InlineData(-1.0, "")]
        [InlineData(double.NaN, "")]
        public void FormatsGeologicalAges(double years, string expected)
        {
            Assert.Equal(expected, QuantityFormatter.FormatAge(years));
        }

        [Fact]
        public void FormatsPhysicalQuantitiesInvariantly()
        {
            Assert.Equal("7,021 km", QuantityFormatter.FormatKilometres(7021.4));
            Assert.Equal("63%", QuantityFormatter.FormatPercent(0.634));
            Assert.Equal("12.5%", QuantityFormatter.FormatPercent(0.125, 1));
            Assert.Equal("16.3 °C", QuantityFormatter.FormatCelsius(16.25));
            Assert.Equal("0.0 °C", QuantityFormatter.FormatCelsius(-0.04));
            Assert.Equal("-5.0 °C", QuantityFormatter.FormatCelsius(-5));
            Assert.Equal("1.12 Earth", QuantityFormatter.FormatWithUnit(1.1234, 2, "Earth"));
            Assert.Equal("1,234,567", QuantityFormatter.FormatCount(1234567));
            Assert.Equal("", QuantityFormatter.FormatCelsius(double.PositiveInfinity));
        }

        [Theory]
        [InlineData(45.9, "45 s")]
        [InlineData(720.0, "12 min")]
        [InlineData(5400.0, "1 h 30 min")]
        [InlineData(7200.0, "2 h")]
        [InlineData(360000.0, "100 h")]
        [InlineData(-1.0, "")]
        public void FormatsPlayTime(double seconds, string expected)
        {
            Assert.Equal(expected, QuantityFormatter.FormatDuration(seconds));
        }

        [Theory]
        [InlineData(512L, "512 B")]
        [InlineData(1536L, "1.5 KB")]
        [InlineData(12897485L, "12.3 MB")]
        [InlineData(209715200L, "200 MB")]
        [InlineData(-5L, "")]
        public void FormatsFileSizes(long bytes, string expected)
        {
            Assert.Equal(expected, QuantityFormatter.FormatFileSize(bytes));
        }
    }

    public class PlanetProfileTests
    {
        [Fact]
        public void BuildsRowsOnlyFromProvidedValues()
        {
            var table = EnglishStrings.CreateTable();
            table.Set("en", "tectonic.high", "High");
            var data = new PlanetProfileData
            {
                PlanetName = "Gaia-8214",
                Seed = 42,
                AgeYears = 2.73e9,
                RadiusKm = 7021,
                MassEarths = 1.12,
                SurfaceGravityG = 1.06,
                OceanCoverage = 0.63,
                MeanTemperatureCelsius = 16.3,
                SurfacePressureAtm = 1.14,
                Continents = 5,
                TectonicPlates = 11,
                TectonicActivityKey = "tectonic.high",
            };

            var rows = PlanetProfile.BuildRows(data, table);

            Assert.Equal(new[]
            {
                "Planet=Gaia-8214", "Seed=42", "Age=2.73 Ga", "Radius=7,021 km", "Mass=1.12 Earth", "Surface Gravity=1.06 g",
                "Ocean Coverage=63%", "Mean Temperature=16.3 °C", "Atmospheric Pressure=1.14 atm", "Continents=5",
                "Tectonic Plates=11", "Tectonic Activity=High",
            }, rows.Select(r => table.Get(r.LabelKey) + "=" + r.Value));
            Assert.Empty(table.MissingKeys);

            string text = PlanetProfile.ToPlainText(rows.Take(3).ToList(), table);
            Assert.Equal("Planet  Gaia-8214\nSeed    42\nAge     2.73 Ga", text);
        }

        [Fact]
        public void MissingOrInvalidValuesProduceNoRowsInsteadOfPlaceholders()
        {
            var rows = PlanetProfile.BuildRows(new PlanetProfileData
            {
                PlanetName = "  ",
                AgeYears = -5,
                RadiusKm = double.NaN,
                OceanCoverage = 0.7,
                Continents = -1,
            });
            Assert.Equal(new[] { "profile.oceanCoverage" }, rows.Select(r => r.LabelKey));
            Assert.Empty(PlanetProfile.BuildRows(new PlanetProfileData()));
        }
    }

    public class ScreenshotNamingTests
    {
        [Fact]
        public void NamesAreTimestampedAndUnique()
        {
            var t = new DateTime(2026, 9, 13, 14, 5, 9);
            Assert.Equal("worldgen-20260913-140509.png", ScreenshotNaming.CreateFileName(t, clean: false));
            Assert.Equal("worldgen-20260913-140509-clean.png", ScreenshotNaming.CreateFileName(t, clean: true));

            string dir = Path.Combine(Path.GetTempPath(), "Screenshots");
            var existing = new HashSet<string>
            {
                Path.Combine(dir, "worldgen-20260913-140509.png"),
                Path.Combine(dir, "worldgen-20260913-140509_2.png"),
            };
            Assert.Equal(Path.Combine(dir, "worldgen-20260913-140509_3.png"), ScreenshotNaming.CreateUniquePath(dir, t, false, existing.Contains));
            Assert.Equal(Path.Combine(dir, "worldgen-20260913-140509-clean.png"), ScreenshotNaming.CreateUniquePath(dir, t, true, existing.Contains));
        }

        [Fact]
        public void RequestValidatesSuperSize()
        {
            Assert.False(new ScreenshotRequest(includeUi: false, superSize: 4).IncludeUi);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenshotRequest(true, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenshotRequest(true, 5));
        }
    }
}
