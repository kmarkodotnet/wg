namespace WorldGen.Viewer.Lod
{
    /// <summary>ND-141: egyetlen, kölcsönösen kizáró felszíni adatnézet.</summary>
    public enum SurfaceOverlayMode
    {
        None = 0,
        SurfaceTemperature = 1,
        AirTemperature = 2,
        WindSpeed = 3,
        Precipitation = 4,
        TectonicPlates = 5,
    }

    public static class SurfaceOverlaySelection
    {
        /// <summary>A korábbi shader/vertexszín prioritása, kizárólag régi szerializált állapotra.</summary>
        public static SurfaceOverlayMode FromLegacy(int thermalMode, bool tectonic, bool wind, bool precipitation)
        {
            if (thermalMode == 1) return SurfaceOverlayMode.SurfaceTemperature;
            if (thermalMode == 2) return SurfaceOverlayMode.AirTemperature;
            if (tectonic) return SurfaceOverlayMode.TectonicPlates;
            if (wind) return SurfaceOverlayMode.WindSpeed;
            return precipitation ? SurfaceOverlayMode.Precipitation : SurfaceOverlayMode.None;
        }

        public static bool IsThermal(SurfaceOverlayMode mode) =>
            mode == SurfaceOverlayMode.SurfaceTemperature || mode == SurfaceOverlayMode.AirTemperature;

        /// <summary>A textúrás hőnézetek nem változtatnak a háttér vertexszínein.</summary>
        public static SurfaceOverlayMode VertexColorMode(SurfaceOverlayMode mode) =>
            IsThermal(mode) ? SurfaceOverlayMode.None : mode;
    }
}
