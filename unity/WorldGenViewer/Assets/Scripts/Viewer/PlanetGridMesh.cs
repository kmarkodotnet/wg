using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;

namespace WorldGen.Viewer
{
    /// <summary>Megjelenítési kategória - a biome-okon felül a "River" (M7) réteg.</summary>
    internal enum RenderCategory
    {
        Ocean, SeaIce, IceSheet, Tundra, Temperate, Tropical, River,
    }

    /// <summary>
    /// M2 vizuális cél: "szürke gömb, tile-határokkal" - a cubed sphere
    /// rács (src/WorldGen.Core/Grid) mesh-be építve. M4/M5 kiegészítés:
    /// a felszín az elevation-nel radiálisan eltolva (hegyek/óceánmedencék),
    /// és biome szerint színezve. M7 kiegészítés: a folyóhálózat (§33)
    /// kék tile-ként kiemelve, felülírva a biome-színt.
    ///
    /// A geometria a WorldGen.Core-ból jön (TileGeometry, PlateGeneration,
    /// PlateBoundaryEffect, SeaLevelCalibration, Temperature,
    /// BiomeClassification, FlowNetwork) - itt csak Unity Mesh-re
    /// fordítjuk, semmilyen szimulációs számítás nincs duplikálva.
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

            // A MAR verifikalt M4/M7 Core-modulokat hivjuk kozvetlenul -
            // nincs duplikalt elevation-/folyoszamitas.
            Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationFieldAtTime(seed, plateCount, level, deepTimeMyr);
            double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, targetWaterFraction);
            Dictionary<TileId, bool> isOceanField = FlowNetwork.ComputeOceanField(field, seaLevel);

            Dictionary<TileId, long> accumulation = null;
            long riverThreshold = long.MaxValue;
            if (showRivers)
            {
                FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOceanField);
                accumulation = FlowNetwork.FlowAccumulation(field, flood.Parent, flood.FloodOrder);

                var landAcc = new List<long>();
                foreach (var kv in isOceanField)
                    if (!kv.Value) landAcc.Add(accumulation[kv.Key]);
                if (landAcc.Count > 0)
                {
                    landAcc.Sort((a, b) => b.CompareTo(a));
                    int idx = Math.Max(0, Math.Min(landAcc.Count - 1, (int)(riverTargetFraction * landAcc.Count)));
                    riverThreshold = landAcc[idx];
                }
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

                        bool isRiver = showRivers && !isOceanic && accumulation[id] >= riverThreshold;
                        RenderCategory category = isRiver ? RenderCategory.River : ToRenderCategory(biome);

                        // FONTOS: a sarkok magasságát KULON-KULON, a sarok
                        // SAJAT pozicioja alapjan szamoljuk (nem a tile
                        // kozepenek egyetlen erteket hasznaljuk mind a 4
                        // sarokra) - igy a szomszedos tile-ok UGYANAZT az
                        // erteket kapjak a kozos sarokpontjukra, es a
                        // felszin osszeer. Enelkul minden tile a sajat
                        // fuggetlen magassagara "lebeg", rest hagyva a
                        // szomszedok kozott.
                        Vector3 p00 = ToDisplacedVector3(face, uMin, vMin, seed, seeds);
                        Vector3 p10 = ToDisplacedVector3(face, uMax, vMin, seed, seeds);
                        Vector3 p11 = ToDisplacedVector3(face, uMax, vMax, seed, seeds);
                        Vector3 p01 = ToDisplacedVector3(face, uMin, vMax, seed, seeds);

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

        private Vector3 ToDisplacedVector3(
            int face, double uc, double vc, ulong seed, (double X, double Y, double Z)[] seeds)
        {
            TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);

            // A sarokpont SAJAT magassaga - ugyanaz a szamitas, amit a
            // sea-level kalibraciohoz is hasznaltunk (AssignPlate +
            // ElevationWithBoundary), csak most a sarok pozicioban kiertekelve,
            // nem a tile kozepen. Tiszta fuggveny -> ket szomszedos tile,
            // ami ugyanazt a fizikai sarkot osztja, bitre ugyanezt az
            // erteket szamolja ki, tehat a felszin varrat nelkul osszeer.
            //
            // MEGJEGYZES: a tileIdValue parametert (a CrustElevation-beli
            // finom "jitter" zajhoz) itt fix 0-val hivjuk, mert egy
            // sarokpontnak nincs egyetlen "sajat" tile-ja (tobb tile is
            // osztja). Ez azt jelenti, hogy a sarok-alapu megjelenites a
            // finom jittert NEM kapja meg (csak a plate-szintu bazis +
            // hatarhatas latszik) - a hivatalos, TEST-EARTH-001-hez hasznalt
            // tile-kozepu elevation-t (amiben BENNE van a jitter) ez nem
            // erinti, csak a vizualis corner-interpolacio egyszerusodik.
            int plateId = PlateGeneration.AssignPlate(x, y, z, seeds);
            double elevation = PlateBoundaryEffect.ElevationWithBoundary(
                seed, plateId, tileIdValue: 0UL, x, y, z, seeds, out _);

            float displacedRadius = radius + (float)(elevation * elevationScale);
            return BodyFrameConversion.ToUnity(x, y, z) * displacedRadius;
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
