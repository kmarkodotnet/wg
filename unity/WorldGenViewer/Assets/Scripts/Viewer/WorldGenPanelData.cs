using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M8 panel-adatok (docs/01-architecture.md §2) - tisztán adat, semmi
    /// szimulációs logika. Minden mező forrása egy Core-hívás
    /// (PlanetGridMesh.ComputePanelData), I4 szerint visszakövethető.
    ///
    /// HATÓKÖR: csak azok a mezők szerepelnek, amiknek MÁR VAN valós
    /// forrása (ld. docs/05-milestones.md "M8 hatókör"). Habitability
    /// (World) és Coastal complexity (Continent) a docs/01-architecture.md
    /// §2.1/§2.2 szerinti mezők, a FeatureMetrics dokumentált HEURISZTIKA-
    /// egyszerűsítésével (ld. ott). Soil fertility TOVÁBBRA IS HALASZTVA,
    /// mert nincs épített talaj-modul, ami számolná (I4: nincs kitalált
    /// érték - inkább hiányzik a mező, mint hogy placeholder legyen).
    /// </summary>
    public sealed class WorldPanelData
    {
        public string Name;
        public string SeedDisplay;
        public double OceanCoveragePercent;

        /// <summary>§2.1 "Habitability" - a FeatureMetrics.HabitabilityFraction
        /// (folyékony-víz-sáv heurisztika) alapján, a TELJES szárazföldre.</summary>
        public double HabitabilityPercent;

        /// <summary>Az ordinális sáv (§2.4, ND-09 kalibráció) - "High + sáv" a spec-példa szerint.</summary>
        public string HabitabilityLevel;
    }

    public sealed class ContinentPanelData
    {
        public string Name;
        public int AreaTiles;
        public int BiomeCount;
        public string DominantBiome;
        public int RiverMouthCount;
        public int RiverBasinCount;

        /// <summary>§2.2 "Coastal complexity" - FeatureMetrics.CoastalComplexity
        /// (part-tile-szám / sqrt(terület) alak-heurisztika, ld. ott).</summary>
        public double CoastalComplexity;

        /// <summary>Az ordinális sáv (§2.4, ND-09 kalibráció) - "High" a spec-példa szerint.</summary>
        public string CoastalComplexityLevel;

        /// <summary>
        /// A kontinens tile-jainak egységgömb-irány-átlaga (normalizálva),
        /// MÁR Unity world-frame egységvektorként (a bolygó LOKÁLIS
        /// terében, a `planetGridMesh.transform` forgatása/skálázása
        /// ELŐTT - ha a bolygó transform-ja el van forgatva, a hívónak
        /// `planetGridMesh.transform.TransformDirection`-t kell rá
        /// alkalmaznia). A felhasználói kérésre bevezetett "kattints a
        /// névre, a kamera odaugrik" funkció (ld.
        /// `PlanetOrbitCamera.FlyToDirection`) célpontja.
        /// </summary>
        public Vector3 CenterDirection;
    }

    public sealed class RegionPanelData
    {
        public string Name;
        public int AreaTiles;
        public string DominantBiome;
        public int RiverMouthCount;

        /// <summary>§2.3 - a névbe (utótag) MÁR beépített morfológiai típus
        /// (FeatureSegmentation.ClassifyLandform) kiírva is, hogy a panel
        /// I4 szerint visszakövethető legyen (ne csak a névből derüljön ki).</summary>
        public string LandformType;

        /// <summary>Ld. ContinentPanelData.CenterDirection doksi - ugyanaz a minta, régió-szinten.</summary>
        public Vector3 CenterDirection;
    }

    public sealed class WorldGenPanelData
    {
        public WorldPanelData World;
        public List<ContinentPanelData> Continents = new List<ContinentPanelData>();
        public List<RegionPanelData> Regions = new List<RegionPanelData>();
    }
}
