using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using WorldGen.Core.Climate;
using WorldGen.Core.Events;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;

namespace WorldGen.Viewer
{
    /// <summary>Megjelenítési kategória - a biome-okon felül a "River" (M7) és "Crater" (M11) réteg.</summary>
    internal enum RenderCategory
    {
        Ocean, SeaIce, IceSheet, Tundra, Temperate, Tropical, River, Crater,
    }

    /// <summary>
    /// M2 vizuális cél: "szürke gömb, tile-határokkal" - a cubed sphere
    /// rács (src/WorldGen.Core/Grid) mesh-be építve. M4/M5 kiegészítés:
    /// a felszín az elevation-nel radiálisan eltolva (hegyek/óceánmedencék),
    /// és biome szerint színezve. M7 kiegészítés: a folyóhálózat (§33)
    /// kék tile-ként kiemelve, felülírva a biome-színt. M10 kiegészítés:
    /// az elevation-mező (és a sarok-alapú megjelenítés is) időfüggő -
    /// a lemezek ténylegesen mozognak. M11 kiegészítés: a becsapódások
    /// (§22) mélyedésként hatnak az elevation-mezőre, ÉS az érintett
    /// tile-ok külön kategóriaként (vörösbarna) is jelölve vannak, mert a
    /// jelenlegi rácsfelbontáson a legtöbb kráter kisebb egy tile-nál.
    ///
    /// A geometria a WorldGen.Core-ból jön (TileGeometry, PlateGeneration,
    /// PlateBoundaryEffect, SeaLevelCalibration, Temperature,
    /// BiomeClassification, FlowNetwork, ImpactCratering) - itt csak Unity
    /// Mesh-re fordítjuk, semmilyen szimulációs számítás nincs duplikálva.
    ///
    /// A `radius` egyelőre tetszőleges Unity-egység, NEM valós bolygóméret
    /// (7420 km) - a nagy-világ precíziós kérdés (ND-19, floating origin)
    /// külön lépés, mielőtt ez éles skálán futna. Az elevation (méterben)
    /// ezért `elevationScale`-lel erősen túlrajzolt a láthatóság kedvéért,
    /// nem valós arányban jelenik meg.
    ///
    /// M7 RENDER-HATÓKÖR: a folyó-tile-ok EGYBEN vannak színezve (nem
    /// vékony vonalként a tile-élek mentén) - ez egyszerűbb és
    /// alacsonyabb kockázatú, mint egy külön vonal-topológia (ld. Borders
    /// mintája), és a jelenlegi bolygó-nézeti felbontáson (level 5-6)
    /// elegendő a hálózat alakjának felismeréséhez. Valódi, vékony
    /// folyó-vonalak M9-nél (kontinens/régió nézet) indokoltak, ahol a
    /// felbontás ezt ténylegesen kihasználná.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class PlanetGridMesh : MonoBehaviour
    {
        [SerializeField, Range(0, 8)]
        [Tooltip("LOD-szint. Level 5-6 ajánlott első nézetre (6144-24576 tile).")]
        private int level = 5;

        [SerializeField]
        private float radius = 100f;

        [SerializeField]
        [Tooltip("A tile-határ vonalháló megjelenítése. Magas LOD-szinten (7+) " +
                 "sok ezer vékony vonal moiré-mintázatot ad a képernyőn - ott " +
                 "érdemes kikapcsolni, a szürke felület önmagában marad látható.")]
        private bool showBorders = true;

        [SerializeField]
        [Tooltip("Ha üres, egy alap fekete HDRP/Unlit anyagot hoz létre futásidőben.")]
        private Material borderMaterial;

        [Header("M4: Lemezek + elevation")]
        [SerializeField]
        [Tooltip("long típus (nem ulong) - a Unity Inspector szerializálása " +
                 "long-ra biztosan megbízható; az API-hívásnál castolunk ulong-ra.")]
        private long worldSeed = 0xA7C944210000L;

        [SerializeField]
        [Tooltip("A referencia (TEST-EARTH-001) 20 lemezzel adott 2 kontinenst " +
                 "65% víz mellett a kanonikus worldSeed-nél - ld. docs/04-decisions.md.")]
        private int plateCount = 20;

        [SerializeField]
        [Tooltip("Unity-egység per méter, a domborzat vizuális túlrajzolásához " +
                 "(a valós elevation/bolygóméret arány láthatatlanul kicsi lenne).")]
        private double elevationScale = 0.01;

        // NINCS [Range] itt szandekosan (ld. axialTiltDegrees-nel korabban):
        // a Unity RangeAttribute csak (float,float)/(int,int) konstruktort
        // fogad, double->float NEM implicit konverzio C#-ban.
        [SerializeField]
        private double targetWaterFraction = 0.65;

        [Header("M5: Klíma (a SunController-től FÜGGETLEN referencia-időpont)")]
        [SerializeField]
        private double climateOrbitalPeriodDays = 365.25;

        [SerializeField]
        private double climateRotationPeriodDays = 1.0;

        [SerializeField]
        private double climateAxialTiltDegrees = 23.44;

        [SerializeField]
        [Tooltip("Melyik naphoz (t) tartozó klímaállapotot jelenítse meg - " +
                 "0 = a referencia napéjegyenlőség.")]
        private double climateDayT = 0.0;

        [Header("M10: Deep time (lemezmozgás)")]
        // NINCS [Range] itt szandekosan (ld. axialTiltDegrees-nel korabban):
        // a Unity RangeAttribute csak (float,float)/(int,int) konstruktort
        // fogad, double->float NEM implicit konverzio C#-ban.
        [SerializeField]
        [Tooltip("Millió év (Myr) - mennyi idő telt el a lemez-magok kezdő " +
                 "pozíciójához képest. 0 = a statikus M4 domborzat. A lemezek " +
                 "Euler-pólus körüli forgással (Rodrigues) mozognak - lásd " +
                 "PlateMotion.cs, docs/04-decisions.md ND-27.")]
        private double deepTimeMyr = 0.0;

        [Header("M7: Folyóhálózat")]
        [SerializeField]
        private bool showRivers = true;

        [SerializeField]
        [Tooltip("A szárazföld ekkora hányada (0..1) legyen folyó-tile - " +
                 "ugyanaz a percentilis-módszer, mint a tengerszint-kalibrációnál.")]
        private double riverTargetFraction = 0.03;

        [Header("M11: Becsapódások")]
        [SerializeField]
        [Tooltip("A deepTimeMyr-ig (fent, M10) megtörtént becsapódások megjelenítése. " +
                 "Ugyanaz az idő-csúszka mozgatja mind a lemezeket, mind a becsapódás-" +
                 "történelmet - egyetlen konzisztens 'ennyi idő telt el' fogalom.")]
        private bool showCraters = true;

        // M8: az utolsó Build() eredményének gyorsítótára - a panel-adatok
        // (ComputePanelData) ezekre épülnek, hogy ne kelljen a teljes
        // elevation-/óceán-/biome-számítást megismételni. Csak a render
        // UTÁN, egy adott Build()-hívásra érvényesek.
        private ulong _lastSeed;
        private Dictionary<TileId, double> _lastField;
        private Dictionary<TileId, bool> _lastIsOcean;
        private Dictionary<TileId, Biome> _lastBiomeOf;
        private double _lastSeaLevel;

        [Tooltip("Minden sikeres Build() (Rebuild) végén meghívva - a WorldGenPanelUI " +
                 "ezt hallgatja, hogy egyetlen Rebuild a panelt is frissítse, ne kelljen " +
                 "külön Refresh Panels-t is hívni.")]
        public UnityEvent Built = new UnityEvent();

        private void Start() => Build();

        [ContextMenu("Rebuild")]
        public void Build()
        {
            ulong seed = unchecked((ulong)worldSeed);
            var seeds0 = PlateGeneration.GenerateSeeds(seed, plateCount);

            // FONTOS (M10): a sarok-alapu megjelenites (ToDisplacedVector3)
            // UGYANAZOKKAL az elmozdult seedekkel szamol, mint a tile-kozepu
            // `field` - kulonben a biome (field-bol) mozogna, de a domborzat
            // (ha a statikus t=0 seedeket hasznalna) nem, ami pontosan az a
            // hiba volt, amit a felhasznalo vizualisan eszrevett.
            var seeds = PlateMotion.MovedSeeds(seed, seeds0, deepTimeMyr);

            // M11: a deepTimeMyr-ig megtortent becsapodasok - ugyanaz a lista
            // hasznalva a mezo-korrekcioho (lent), a sarok-alapu megjeleniteshez
            // (ToDisplacedVector3) ES a tile-kategorizalashoz (isCratered) is,
            // hogy mindharom UGYANAZT a "tortenelmet" lassa.
            List<ImpactCratering.CraterRecord> craters = showCraters
                ? ImpactCratering.GenerateCratersUpToTime(seed, deepTimeMyr)
                : new List<ImpactCratering.CraterRecord>();

            // A MAR verifikalt M4/M7/M11 Core-modulokat hivjuk kozvetlenul -
            // nincs duplikalt elevation-/folyoszamitas.
            Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationFieldAtTime(seed, plateCount, level, deepTimeMyr);
            if (craters.Count > 0)
            {
                var craterField = new Dictionary<TileId, double>(field.Count);
                foreach (KeyValuePair<TileId, double> kv in field)
                {
                    TileGeometry.ToPosition(kv.Key, out double fx, out double fy, out double fz);
                    craterField[kv.Key] = kv.Value + ImpactCratering.ElevationDelta(fx, fy, fz, craters);
                }
                field = craterField;
            }
            double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, targetWaterFraction);
            Dictionary<TileId, bool> isOceanField = FlowNetwork.ComputeOceanField(field, seaLevel);

            // A folyo-tile kivalasztas logikaja a Core-ban van (FlowNetwork.
            // SelectRiverTiles) - itt nincs duplikalva szimulacios matek.
            HashSet<TileId> riverTiles = new HashSet<TileId>();
            if (showRivers)
            {
                FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOceanField);
                Dictionary<TileId, long> accumulation = FlowNetwork.FlowAccumulation(field, flood.Parent, flood.FloodOrder);
                riverTiles = FlowNetwork.SelectRiverTiles(isOceanField, accumulation, riverTargetFraction);
            }

            double axialTiltRad = climateAxialTiltDegrees * Math.PI / 180.0;

            var verticesByCategory = new Dictionary<RenderCategory, List<Vector3>>();
            var normalsByCategory = new Dictionary<RenderCategory, List<Vector3>>();
            var trianglesByCategory = new Dictionary<RenderCategory, List<int>>();
            foreach (RenderCategory c in AllCategories)
            {
                verticesByCategory[c] = new List<Vector3>();
                normalsByCategory[c] = new List<Vector3>();
                trianglesByCategory[c] = new List<int>();
            }

            var borderVerts = new List<Vector3>();
            var borderIndices = new List<int>();

            // M8: a biome-eket is elmentjuk tile-onkent, kesobb a panel-
            // adatokhoz (kontinens/regio-szegmentalas, domonans biome).
            var biomeOf = new Dictionary<TileId, Biome>();

            int n = 1 << level;
            for (int face = 0; face <= 5; face++)
            {
                for (uint u = 0; u < n; u++)
                {
                    for (uint v = 0; v < n; v++)
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.GetContinuousBounds(id, out double uMin, out double uMax, out double vMin, out double vMax);
                        TileGeometry.ToPosition(id, out double cx, out double cy, out double cz);

                        double elevation = field[id];
                        bool isOceanic = isOceanField[id];
                        double temperatureK = Temperature.TemperatureKelvin(
                            cx, cy, cz, climateDayT, climateOrbitalPeriodDays, climateRotationPeriodDays,
                            axialTiltRad, isOceanic, elevation, seaLevel);
                        Biome biome = BiomeClassification.Classify(temperatureK, isOceanic);
                        biomeOf[id] = biome;

                        // M11: a becsapodas-erintett tile-ok kulon kategoriaba
                        // kerulnek (a folyo-highlight mintajat kovetve), MERT
                        // a jelenlegi racsfelbontason (LOD 5-7, tile-ok
                        // ~90-3000 km) a legtobb kis krater (1-100 km) nem
                        // mozdit el egyetlen tile-sarkot sem eszreveheto
                        // mertekben - a kategoria-jeloles igy is lathatova
                        // teszi a ritka talalatokat, fuggetlenul a
                        // felbontas-korlattol (ld. ImpactCratering.ApplyToField).
                        bool isCratered = craters.Count > 0 && ImpactCratering.IsInsideAnyCrater(cx, cy, cz, craters);
                        bool isRiver = riverTiles.Contains(id);
                        RenderCategory category = isCratered ? RenderCategory.Crater
                            : isRiver ? RenderCategory.River
                            : ToRenderCategory(biome);

                        // FONTOS: a sarkok magasságát KULON-KULON, a sarok
                        // SAJAT pozicioja alapjan szamoljuk (nem a tile
                        // kozepenek egyetlen erteket hasznaljuk mind a 4
                        // sarokra) - igy a szomszedos tile-ok UGYANAZT az
                        // erteket kapjak a kozos sarokpontjukra, es a
                        // felszin osszeer. Enelkul minden tile a sajat
                        // fuggetlen magassagara "lebeg", rest hagyva a
                        // szomszedok kozott.
                        Vector3 p00 = ToDisplacedVector3(face, uMin, vMin, seed, seeds, craters);
                        Vector3 p10 = ToDisplacedVector3(face, uMax, vMin, seed, seeds, craters);
                        Vector3 p11 = ToDisplacedVector3(face, uMax, vMax, seed, seeds, craters);
                        Vector3 p01 = ToDisplacedVector3(face, uMin, vMax, seed, seeds, craters);

                        List<Vector3> vertices = verticesByCategory[category];
                        List<Vector3> normals = normalsByCategory[category];
                        List<int> triangles = trianglesByCategory[category];

                        int baseIndex = vertices.Count;
                        vertices.Add(p00); vertices.Add(p10); vertices.Add(p11); vertices.Add(p01);

                        Vector3 normal = Vector3.Cross(p10 - p00, p01 - p00).normalized;
                        if (Vector3.Dot(normal, p00) < 0f) normal = -normal;
                        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);

                        Vector3 impliedNormal1 = Vector3.Cross(p10 - p00, p11 - p00);
                        if (Vector3.Dot(impliedNormal1, normal) >= 0f)
                        {
                            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2);
                            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
                        }
                        else
                        {
                            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
                            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
                        }

                        int b = borderVerts.Count;
                        borderVerts.Add(p00); borderVerts.Add(p10); borderVerts.Add(p11); borderVerts.Add(p01);
                        borderIndices.Add(b + 0); borderIndices.Add(b + 1);
                        borderIndices.Add(b + 1); borderIndices.Add(b + 2);
                        borderIndices.Add(b + 2); borderIndices.Add(b + 3);
                        borderIndices.Add(b + 3); borderIndices.Add(b + 0);
                    }
                }
            }

            BuildMultiMaterialMesh(verticesByCategory, normalsByCategory, trianglesByCategory);
            BuildBorders(borderVerts, borderIndices);
            BuildCraterMarkers(craters, seed, seeds);

            _lastSeed = seed;
            _lastField = field;
            _lastIsOcean = isOceanField;
            _lastBiomeOf = biomeOf;
            _lastSeaLevel = seaLevel;

            Built.Invoke();
        }

        /// <summary>
        /// M8 panel-adatok (docs/01-architecture.md §2) - Build() UTÁN
        /// hívható. Csak a Core-modulokat hívja (SeaLevelCalibration,
        /// FlowNetwork, FeatureSegmentation, FeatureMetrics, NameGeneration) -
        /// nincs duplikált szegmentálási/metrika-logika.
        /// </summary>
        public WorldGenPanelData ComputePanelData()
        {
            if (_lastField == null)
                throw new InvalidOperationException("Build() még nem futott le - nincs adat a panelekhez.");

            var data = new WorldGenPanelData();

            double oceanCoverage = FeatureMetrics.OceanCoverageFraction(_lastIsOcean);
            Biome globalDominant = FeatureSegmentation.DominantBiome(_lastField.Keys, _lastBiomeOf);
            string worldName = NameGeneration.GenerateName(_lastSeed, PlanetFeatureId, globalDominant.ToString());
            data.World = new WorldPanelData
            {
                Name = worldName,
                SeedDisplay = worldSeed.ToString("X"),
                OceanCoveragePercent = oceanCoverage * 100.0,
            };

            FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(_lastField, _lastIsOcean);
            Dictionary<TileId, long> accumulation = FlowNetwork.FlowAccumulation(_lastField, flood.Parent, flood.FloodOrder);
            HashSet<TileId> riverTilesForPanels = FlowNetwork.SelectRiverTiles(_lastIsOcean, accumulation, riverTargetFraction);

            Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, _lastIsOcean);
            var sizedRegions = new Dictionary<TileId, List<TileId>>();
            foreach (KeyValuePair<TileId, List<TileId>> kv in regions)
                if (kv.Value.Count >= 5) sizedRegions[kv.Key] = kv.Value;

            List<List<TileId>> continents = SeaLevelCalibration.CountContinents(_lastField, _lastSeaLevel, minSize: 5);
            List<List<TileId>> sortedContinents = new List<List<TileId>>(continents);
            // Meret szerint csokkeno, majd a komponens legkisebb tile-ja
            // masodlagos kulcskent - UGYANAZ az explicit, hordozhato
            // rendezes, mint a C# tesztekben (nem a nyelv gyujtemeny-
            // bejarasi sorrendjere tamaszkodik, ami platformfuggo lehetne).
            sortedContinents.Sort((a, b) =>
            {
                int bySize = b.Count.CompareTo(a.Count);
                return bySize != 0 ? bySize : CompareTileByFaceUV(MinTile(a), MinTile(b));
            });

            for (int i = 0; i < sortedContinents.Count; i++)
            {
                List<TileId> comp = sortedContinents[i];
                Biome dominant = FeatureSegmentation.DominantBiome(comp, _lastBiomeOf);
                string name = NameGeneration.GenerateName(_lastSeed, (ulong)i, dominant.ToString());
                var compSet = new HashSet<TileId>(comp);
                data.Continents.Add(new ContinentPanelData
                {
                    Name = name,
                    AreaTiles = FeatureMetrics.AreaTiles(comp),
                    BiomeCount = FeatureMetrics.BiomeDiversity(comp, _lastBiomeOf),
                    DominantBiome = dominant.ToString(),
                    RiverMouthCount = FeatureMetrics.RiverMouthCount(comp, flood.Parent, _lastIsOcean, riverTilesForPanels),
                    RiverBasinCount = FeatureMetrics.RiverBasinCount(compSet, sizedRegions),
                });
            }

            List<TileId> sortedRegionRoots = new List<TileId>(sizedRegions.Keys);
            sortedRegionRoots.Sort((a, b) =>
            {
                int bySize = sizedRegions[b].Count.CompareTo(sizedRegions[a].Count);
                return bySize != 0 ? bySize : CompareTileByFaceUV(a, b);
            });

            int regionLimit = Math.Min(10, sortedRegionRoots.Count);
            for (int i = 0; i < regionLimit; i++)
            {
                List<TileId> tiles = sizedRegions[sortedRegionRoots[i]];
                Biome dominant = FeatureSegmentation.DominantBiome(tiles, _lastBiomeOf);
                string name = NameGeneration.GenerateName(_lastSeed, (ulong)(10000 + i), dominant.ToString());
                data.Regions.Add(new RegionPanelData
                {
                    Name = name,
                    AreaTiles = FeatureMetrics.AreaTiles(tiles),
                    DominantBiome = dominant.ToString(),
                    RiverMouthCount = FeatureMetrics.RiverMouthCount(tiles, flood.Parent, _lastIsOcean, riverTilesForPanels),
                });
            }

            return data;
        }

        /// <summary>
        /// A "bolygó" (World) nevének feature-id-je - egy sosem ütköző
        /// sentinel (kontinensek 0..N, régiók 10000+ id-t kapnak).
        /// </summary>
        public const ulong PlanetFeatureId = 999_999_999UL;

        private static int CompareTileByFaceUV(TileId a, TileId b)
        {
            if (a.Face != b.Face) return a.Face.CompareTo(b.Face);
            a.GetUV(out uint au, out uint av);
            b.GetUV(out uint bu, out uint bv);
            if (au != bu) return au.CompareTo(bu);
            return av.CompareTo(bv);
        }

        private static TileId MinTile(IEnumerable<TileId> tiles)
        {
            TileId min = default;
            bool first = true;
            foreach (TileId t in tiles)
                if (first || CompareTileByFaceUV(t, min) < 0) { min = t; first = false; }
            return min;
        }

        private static readonly RenderCategory[] AllCategories = (RenderCategory[])Enum.GetValues(typeof(RenderCategory));

        private static RenderCategory ToRenderCategory(Biome biome) => biome switch
        {
            Biome.Ocean => RenderCategory.Ocean,
            Biome.SeaIce => RenderCategory.SeaIce,
            Biome.IceSheet => RenderCategory.IceSheet,
            Biome.Tundra => RenderCategory.Tundra,
            Biome.Temperate => RenderCategory.Temperate,
            Biome.Tropical => RenderCategory.Tropical,
            _ => RenderCategory.Ocean,
        };

        private void BuildMultiMaterialMesh(
            Dictionary<RenderCategory, List<Vector3>> verticesByCategory,
            Dictionary<RenderCategory, List<Vector3>> normalsByCategory,
            Dictionary<RenderCategory, List<int>> trianglesByCategory)
        {
            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var submeshTriangleLists = new List<int[]>();
            var materials = new List<Material>();

            foreach (RenderCategory category in AllCategories)
            {
                List<Vector3> verts = verticesByCategory[category];
                if (verts.Count == 0) continue;

                int offset = allVertices.Count;
                allVertices.AddRange(verts);
                allNormals.AddRange(normalsByCategory[category]);

                int[] tris = trianglesByCategory[category].ToArray();
                for (int i = 0; i < tris.Length; i++) tris[i] += offset;
                submeshTriangleLists.Add(tris);
                materials.Add(GetOrCreateCategoryMaterial(category));
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(allVertices);
            mesh.SetNormals(allNormals);
            mesh.subMeshCount = submeshTriangleLists.Count;
            for (int i = 0; i < submeshTriangleLists.Count; i++)
                mesh.SetTriangles(submeshTriangleLists[i], i);
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
        }

        private void BuildBorders(List<Vector3> borderVerts, List<int> borderIndices)
        {
            Transform borderChild = transform.Find("Borders");

            if (!showBorders)
            {
                if (borderChild != null) borderChild.gameObject.SetActive(false);
                return;
            }

            var borderMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            borderMesh.SetVertices(borderVerts);
            borderMesh.SetIndices(borderIndices, MeshTopology.Lines, 0);
            borderMesh.RecalculateBounds();

            GameObject borderGo;
            if (borderChild == null)
            {
                borderGo = new GameObject("Borders");
                borderGo.transform.SetParent(transform, false);
                borderGo.AddComponent<MeshFilter>();
                MeshRenderer mr = borderGo.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.sharedMaterial = borderMaterial != null ? borderMaterial : CreateFlatColorMaterial(Color.black);
            }
            else
            {
                borderGo = borderChild.gameObject;
                borderGo.SetActive(true);
            }
            borderGo.GetComponent<MeshFilter>().sharedMesh = borderMesh;
        }

        /// <summary>
        /// M11: rácsfelbontástól FÜGGETLEN kráter-markerek (kis gömbök a
        /// becsapódás pozíciójában). SZÜKSÉGES, mert a legnagyobb
        /// modellezett kráter (100 km átmérő) szögmérete még level 7-en is
        /// jóval kisebb egy tile-nál egy 7420 km sugarú bolygón (~0.4° vs.
        /// ~0.7°/tile) - a tile-alapú kategorizálás (Build()-ben, isCratered)
        /// és a sarok-alapú mélyedés emiatt a GYAKORLATBAN szinte sosem
        /// jelenik meg látványosan, még nagy deepTimeMyr mellett sem
        /// (statisztikailag túl ritka, hogy egy krátér épp egy tile-sarkot
        /// találjon el). A markerek ezért, az `elevationScale`-hez
        /// hasonlóan, VIZUÁLIS AFFORDANCE-k - a méretük túlrajzolt (nem
        /// valós arányban), hogy egyáltalán láthatók legyenek ezen a
        /// nézeti távolságon. A pozíciójuk viszont pontosan a generált
        /// adatból jön (I3 invariáns).
        /// </summary>
        private void BuildCraterMarkers(
            List<ImpactCratering.CraterRecord> craters, ulong seed, (double X, double Y, double Z)[] seeds)
        {
            Transform markersParent = transform.Find("CraterMarkers");
            if (markersParent == null)
            {
                var go = new GameObject("CraterMarkers");
                go.transform.SetParent(transform, false);
                markersParent = go.transform;
            }

            for (int i = markersParent.childCount - 1; i >= 0; i--)
                SafeDestroy(markersParent.GetChild(i).gameObject);

            if (!showCraters || craters.Count == 0)
            {
                markersParent.gameObject.SetActive(false);
                return;
            }
            markersParent.gameObject.SetActive(true);

            Material markerMaterial = CreateFlatColorMaterial(CategoryColor(RenderCategory.Crater));
            double maxPossibleDepth = ImpactCratering.MaxDiameterMeters * ImpactCratering.DepthToDiameterRatio;

            foreach (ImpactCratering.CraterRecord crater in craters)
            {
                // A marker a TENYLEGES (elevation-nel eltolt) felszinen ul,
                // nem a nyers `radius`-nal - kulonben a szarazfoldnal/
                // tengerszintnel magasabban/alacsonyabban lebegne, fuggetlenul
                // a valos domborzattol (ezt a felhasznalo eszrevette).
                float surfaceRadius = ComputeDisplacedRadius(crater.X, crater.Y, crater.Z, seed, seeds, craters);

                float sizeFraction = Mathf.Clamp01((float)(crater.DepthMeters / maxPossibleDepth));
                float markerDiameter = Mathf.Lerp(1.0f, 6.0f, sizeFraction);

                // Kifele told a sajat sugaraval, hogy a felszinen "uljon"
                // (ne legyen felig belesullyedve) - tisztan vizualis
                // igazitas, mint egy terkep-tuszog.
                float markerCenterRadius = surfaceRadius + markerDiameter * 0.5f;

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Crater";
                marker.transform.SetParent(markersParent, false);
                marker.transform.localPosition = BodyFrameConversion.ToUnity(crater.X, crater.Y, crater.Z) * markerCenterRadius;
                marker.transform.localScale = Vector3.one * markerDiameter;

                MeshRenderer mr = marker.GetComponent<MeshRenderer>();
                mr.sharedMaterial = markerMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private Vector3 ToDisplacedVector3(
            int face, double uc, double vc, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters)
        {
            TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);
            float displacedRadius = ComputeDisplacedRadius(x, y, z, seed, seeds, craters);
            return BodyFrameConversion.ToUnity(x, y, z) * displacedRadius;
        }

        /// <summary>
        /// A felszín (elevation-nel eltolt) sugara egy adott egységvektor-
        /// pozícióban. KÖZÖS a tile-sarkokkal (ToDisplacedVector3) és a
        /// kráter-markerekkel (BuildCraterMarkers) - enélkül a markerek a
        /// nyers `radius`-nál lebegnének, függetlenül a tényleges (gyakran
        /// jóval alacsonyabb) domborzattól, ahogy azt a felhasználó
        /// vizuálisan észrevette.
        /// </summary>
        private float ComputeDisplacedRadius(
            double x, double y, double z, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters)
        {
            // Ugyanaz a szamitas, amit a sea-level kalibraciohoz is
            // hasznaltunk (AssignPlate + ElevationWithBoundary), csak most
            // egy tetszoleges pontban kiertekelve. Tiszta fuggveny -> ket
            // szomszedos tile, ami ugyanazt a fizikai sarkot osztja, bitre
            // ugyanezt az erteket szamolja ki, tehat a felszin varrat
            // nelkul osszeer.
            //
            // MEGJEGYZES: a tileIdValue parametert (a CrustElevation-beli
            // finom "jitter" zajhoz) itt fix 0-val hivjuk, mert egy
            // sarokpontnak/kraternek nincs egyetlen "sajat" tile-ja. Ez azt
            // jelenti, hogy ez a szamitas a finom jittert NEM kapja meg
            // (csak a plate-szintu bazis + hatarhatas latszik) - a
            // hivatalos, TEST-EARTH-001-hez hasznalt tile-kozepu
            // elevation-t (amiben BENNE van a jitter) ez nem erinti, csak a
            // vizualis corner-interpolacio es a marker-magassag egyszerusodik.
            int plateId = PlateGeneration.AssignPlate(x, y, z, seeds);
            double elevation = PlateBoundaryEffect.ElevationWithBoundary(
                seed, plateId, tileIdValue: 0UL, x, y, z, seeds, out _);

            // M11: a pont SAJAT pozicioja alapjan szamolt becsapodas-korrekcio -
            // ugyanaz a "tiszta fuggveny a pozicioban, nem a tile-ban" elv,
            // ami a lemez-elevaciot is varratmentesse teszi ket szomszedos
            // tile kozott.
            if (craters.Count > 0)
                elevation += ImpactCratering.ElevationDelta(x, y, z, craters);

            return radius + (float)(elevation * elevationScale);
        }

        private Dictionary<RenderCategory, Material> _categoryMaterials;

        private Material GetOrCreateCategoryMaterial(RenderCategory category)
        {
            _categoryMaterials ??= new Dictionary<RenderCategory, Material>();
            if (_categoryMaterials.TryGetValue(category, out Material existing) && existing != null)
                return existing;

            Material mat = CreateFlatColorMaterial(CategoryColor(category));
            _categoryMaterials[category] = mat;
            return mat;
        }

        private static Color CategoryColor(RenderCategory category) => category switch
        {
            RenderCategory.Ocean => new Color(0.09f, 0.30f, 0.55f),
            RenderCategory.SeaIce => new Color(0.80f, 0.88f, 0.93f),
            RenderCategory.IceSheet => new Color(0.95f, 0.96f, 0.98f),
            RenderCategory.Tundra => new Color(0.52f, 0.52f, 0.42f),
            RenderCategory.Temperate => new Color(0.22f, 0.52f, 0.20f),
            RenderCategory.Tropical => new Color(0.78f, 0.72f, 0.20f),
            RenderCategory.River => new Color(0.20f, 0.55f, 0.90f), // vilagosabb kek, mint az ocean - elkulonul
            RenderCategory.Crater => new Color(0.45f, 0.18f, 0.10f), // sotet vorosbarna - jol elkulonul minden biome-tol
            _ => Color.magenta, // ismeretlen kategoria - szandekosan feltuno jelzes
        };

        private static Material CreateFlatColorMaterial(Color color)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogWarning("PlanetGridMesh: a \"HDRP/Lit\" shader nem található.");
                return null; // Unity a beépített rózsaszín default anyagot adja - jól látható jelzés
            }
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }
    }
}
