using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    public class SurfaceOverlaySelectionTests
    {
        [Theory]
        [InlineData(0, false, false, false, SurfaceOverlayMode.None)]
        [InlineData(0, false, false, true, SurfaceOverlayMode.Precipitation)]
        [InlineData(0, false, true, true, SurfaceOverlayMode.WindSpeed)]
        [InlineData(0, true, true, true, SurfaceOverlayMode.TectonicPlates)]
        [InlineData(1, true, true, true, SurfaceOverlayMode.SurfaceTemperature)]
        [InlineData(2, true, true, true, SurfaceOverlayMode.AirTemperature)]
        [InlineData(99, false, false, true, SurfaceOverlayMode.Precipitation)]
        public void LegacyConflictsPreserveVisibleLayer(int thermal, bool tectonic, bool wind, bool precipitation, SurfaceOverlayMode expected)
        {
            Assert.Equal(expected, SurfaceOverlaySelection.FromLegacy(thermal, tectonic, wind, precipitation));
        }

        [Theory]
        [InlineData(SurfaceOverlayMode.None)]
        [InlineData(SurfaceOverlayMode.SurfaceTemperature)]
        [InlineData(SurfaceOverlayMode.AirTemperature)]
        public void ThermalSwitchDoesNotInvalidateVertexColors(SurfaceOverlayMode mode)
        {
            Assert.Equal(SurfaceOverlayMode.None, SurfaceOverlaySelection.VertexColorMode(mode));
        }

        [Theory]
        [InlineData(SurfaceOverlayMode.WindSpeed)]
        [InlineData(SurfaceOverlayMode.Precipitation)]
        [InlineData(SurfaceOverlayMode.TectonicPlates)]
        public void VertexOverlayHasIndependentInvalidation(SurfaceOverlayMode mode)
        {
            Assert.Equal(mode, SurfaceOverlaySelection.VertexColorMode(mode));
            Assert.False(SurfaceOverlaySelection.IsThermal(mode));
        }
    }
}
