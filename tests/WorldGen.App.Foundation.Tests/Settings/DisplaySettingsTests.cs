using System;
using System.Collections.Generic;
using WorldGen.App.Settings;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Settings
{
    public class VideoModeConfirmationTests
    {
        [Fact]
        public void OnlyDisplayModeOrResolutionChangeNeedsConfirmation()
        {
            var a = new GraphicsSettings();
            var b = a.Clone();
            b.VSync = false;
            b.Quality = GraphicsQuality.Low;
            Assert.False(VideoModeConfirmation.RequiresConfirmation(a, b));
            b.DisplayMode = DisplayMode.Fullscreen;
            Assert.True(VideoModeConfirmation.RequiresConfirmation(a, b));
            var c = a.Clone();
            c.Resolution = new ScreenResolution(1280, 720);
            Assert.True(VideoModeConfirmation.RequiresConfirmation(a, c));
        }

        [Fact]
        public void TimeoutRevertsToPreviousSettings()
        {
            var confirmation = new VideoModeConfirmation(15);
            var previous = new GraphicsSettings { DisplayMode = DisplayMode.Windowed };
            var candidate = new GraphicsSettings { DisplayMode = DisplayMode.Fullscreen };
            var reverted = new List<GraphicsSettings>();
            confirmation.RevertRequested += reverted.Add;

            confirmation.Begin(previous, candidate);
            previous.DisplayMode = DisplayMode.Borderless; // a hívó példányának módosítása nem hat vissza
            confirmation.Tick(10);
            confirmation.Tick(double.NaN);
            confirmation.Tick(-3);
            Assert.Equal(5, confirmation.RemainingSeconds, 9);
            Assert.Equal(VideoConfirmationState.AwaitingConfirmation, confirmation.State);

            confirmation.Tick(6);
            Assert.Equal(VideoConfirmationState.Reverted, confirmation.State);
            Assert.Equal(0, confirmation.RemainingSeconds);
            Assert.Equal(DisplayMode.Windowed, Assert.Single(reverted).DisplayMode);

            confirmation.Tick(100);
            Assert.False(confirmation.Confirm());
            Assert.Single(reverted);
        }

        [Fact]
        public void ConfirmBeforeTimeoutKeepsCandidate()
        {
            var confirmation = new VideoModeConfirmation();
            GraphicsSettings? confirmed = null;
            bool revertRaised = false;
            confirmation.Confirmed += g => confirmed = g;
            confirmation.RevertRequested += _ => revertRaised = true;

            Assert.False(confirmation.Confirm());
            confirmation.Begin(new GraphicsSettings(), new GraphicsSettings { Resolution = new ScreenResolution(1920, 1080, 60) });
            confirmation.Tick(14.9);
            Assert.True(confirmation.Confirm());
            confirmation.Tick(10);

            Assert.Equal(VideoConfirmationState.Confirmed, confirmation.State);
            Assert.Equal(new ScreenResolution(1920, 1080, 60), confirmed!.Resolution);
            Assert.False(revertRaised);
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoModeConfirmation(0));
        }
    }

    public class ResolutionCatalogTests
    {
        private static readonly ScreenResolution[] Reported =
        {
            new ScreenResolution(1920, 1080, 60),
            new ScreenResolution(1280, 720, 60),
            new ScreenResolution(1920, 1080, 144),
            new ScreenResolution(2560, 1440, 59.95),
            new ScreenResolution(0, 0, 60),
            new ScreenResolution(1680, 1050, double.NaN),
            new ScreenResolution(1920, 1200, 60),
        };

        [Fact]
        public void NormalizeDeduplicatesKeepsHighestRefreshAndSorts()
        {
            Assert.Equal(new[]
            {
                new ScreenResolution(2560, 1440, 59.95),
                new ScreenResolution(1920, 1200, 60),
                new ScreenResolution(1920, 1080, 144),
                new ScreenResolution(1680, 1050, 0),
                new ScreenResolution(1280, 720, 60),
            }, ResolutionCatalog.Normalize(Reported));
        }

        [Fact]
        public void SelectBestMapsRequestToAvailableMode()
        {
            var native = new ScreenResolution(1920, 1080);
            Assert.Equal(new ScreenResolution(1920, 1080, 144), ResolutionCatalog.SelectBest(Reported, ScreenResolution.Unspecified, native));
            Assert.Equal(new ScreenResolution(2560, 1440, 59.95),
                ResolutionCatalog.SelectBest(Reported, ScreenResolution.Unspecified, new ScreenResolution(3840, 2160)));
            Assert.Equal(new ScreenResolution(1280, 720, 60), ResolutionCatalog.SelectBest(Reported, new ScreenResolution(1280, 720, 30), native));
            // 3440x1440 nem elérhető: a legnagyobb, ami belefér
            Assert.Equal(new ScreenResolution(2560, 1440, 59.95), ResolutionCatalog.SelectBest(Reported, new ScreenResolution(3440, 1440), native));
            // semmi nem fér bele: a legkisebb
            Assert.Equal(new ScreenResolution(1280, 720, 60), ResolutionCatalog.SelectBest(Reported, new ScreenResolution(800, 600), native));
            Assert.Null(ResolutionCatalog.SelectBest(Array.Empty<ScreenResolution>(), native, native));
        }
    }

    public class FrameRatePolicyTests
    {
        [Theory]
        //        vsync  fps  bgFps focus pauseUnfocused → vSyncCount target pause
        [InlineData(true, 0, 30, true, true, 1, -1, false)]
        [InlineData(true, 60, 30, true, true, 1, -1, false)]
        [InlineData(false, 0, 30, true, true, 0, -1, false)]
        [InlineData(false, 144, 30, true, true, 0, 144, false)]
        [InlineData(true, 0, 30, false, true, 0, 30, true)]
        [InlineData(false, 20, 30, false, false, 0, 30, false)]
        [InlineData(false, 60, 90, false, false, 0, 60, false)]
        [InlineData(true, 0, 0, false, true, 1, -1, true)]
        [InlineData(false, 1000, 0, true, true, 0, 360, false)]
        public void ResolvesUnityFrameRateValues(bool vsync, int fps, int backgroundFps, bool hasFocus, bool pauseWhenUnfocused,
            int expectedVSync, int expectedTarget, bool expectedPause)
        {
            var graphics = new GraphicsSettings { VSync = vsync, FpsLimit = fps, BackgroundFpsLimit = backgroundFps };
            var simulation = new SimulationSettings { PauseWhenUnfocused = pauseWhenUnfocused };
            var decision = FrameRatePolicy.Resolve(graphics, simulation, hasFocus);
            Assert.Equal(expectedVSync, decision.VSyncCount);
            Assert.Equal(expectedTarget, decision.TargetFrameRate);
            Assert.Equal(expectedPause, decision.PauseSimulation);
        }
    }
}
