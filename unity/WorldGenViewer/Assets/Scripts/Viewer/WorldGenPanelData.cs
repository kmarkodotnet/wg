using System.Collections.Generic;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M8 panel-adatok (docs/01-architecture.md §2) - tisztán adat, semmi
    /// szimulációs logika. Minden mező forrása egy Core-hívás
    /// (PlanetGridMesh.ComputePanelData), I4 szerint visszakövethető.
    ///
    /// HATÓKÖR: csak azok a mezők szerepelnek, amiknek MÁR VAN valós
    /// forrása (ld. docs/05-milestones.md "M8 hatókör") - Habitability,
    /// Coastal complexity, Soil fertility stb. HALASZTVA, mert még nincs
    /// épített talaj-/részletes klíma-modul, ami ezeket számolná.
    /// </summary>
    public sealed class WorldPanelData
    {
        public string Name;
        public string SeedDisplay;
        public double OceanCoveragePercent;
    }

    public sealed class ContinentPanelData
    {
        public string Name;
        public int AreaTiles;
        public int BiomeCount;
        public string DominantBiome;
        public int RiverMouthCount;
        public int RiverBasinCount;
    }

    public sealed class RegionPanelData
    {
        public string Name;
        public int AreaTiles;
        public string DominantBiome;
        public int RiverMouthCount;
    }

    public sealed class WorldGenPanelData
    {
        public WorldPanelData World;
        public List<ContinentPanelData> Continents = new List<ContinentPanelData>();
        public List<RegionPanelData> Regions = new List<RegionPanelData>();
    }
}
