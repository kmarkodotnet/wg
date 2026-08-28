using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M2 vizuális cél: "szürke gömb, tile-határokkal" - a cubed sphere
    /// rács (src/WorldGen.Core/Grid) mesh-be építve. M4/M5 kiegészítés:
    /// a felszín az elevation-nel radiálisan eltolva (hegyek/óceánmedencék),
    /// és biome szerint színezve (§4.5, §5 render-lépések).
    ///
    /// A geometria a WorldGen.Core-ból jön (TileGeometry, PlateGeneration,
    /// PlateBoundaryEffect, Temperature, BiomeClassification) - itt csak
    /// Unity Mesh-re fordítjuk, semmilyen szimulációs számítás nincs
    /// duplikálva.
    ///
    /// A `radius` egyelőre tetszőleges Unity-egység, NEM valós bolygóméret
    /// (7420 km) - a nagy-világ precíziós kérdés (ND-19, floating origin)
    /// külön lépés, mielőtt ez éles skálán futna. Az elevation (méterben)
    /// ezért `elevationScale`-lel erősen túlrajzolt a láthatóság kedvéért,
    /// nem valós arányban jelenik meg.
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

        private void Start() => Build();

        [ContextMenu("Rebuild")]
        public void Build()
        {
            int n = 1 << level;
            ulong seed = unchecked((ulong)worldSeed);
            var seeds = PlateGeneration.GenerateSeeds(seed, plateCount);

            // 1. kör: minden tile elevation + kereg-tipusa (a tengerszint-
            // kalibraciohoz kell ELOSZOR az egesz mezo, mielott szinezunk).
            int total = 6 * n * n;
            var elevations = new double[total];
            var oceanicFlags = new bool[total];

            int idx = 0;
            for (int face = 0; face <= 5; face++)
            {
                for (uint u = 0; u < n; u++)
                {
                    for (uint v = 0; v < n; v++)
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.ToPosition(id, out double cx, out double cy, out double cz);
                        int plateId = PlateGeneration.AssignPlate(cx, cy, cz, seeds);
                        double elevation = PlateBoundaryEffect.ElevationWithBoundary(
                            seed, plateId, id.Value, cx, cy, cz, seeds, out bool isOceanic);

                        elevations[idx] = elevation;
                        oceanicFlags[idx] = isOceanic;
                        idx++;
                    }
                }
            }

            double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(elevations, targetWaterFraction);
            double axialTiltRad = climateAxialTiltDegrees * Math.PI / 180.0;

            // 2. kör: geometria + szín (biome) építése, az 1. körben kapott
            // elevation/kereg-tipus/tengerszint alapján.
            var verticesByBiome = new Dictionary<Biome, List<Vector3>>();
            var normalsByBiome = new Dictionary<Biome, List<Vector3>>();
            var trianglesByBiome = new Dictionary<Biome, List<int>>();
            foreach (Biome b in AllBiomes)
            {
                verticesByBiome[b] = new List<Vector3>();
                normalsByBiome[b] = new List<Vector3>();
                trianglesByBiome[b] = new List<int>();
            }

            var borderVerts = new List<Vector3>();
            var borderIndices = new List<int>();

            idx = 0;
            for (int face = 0; face <= 5; face++)
            {
                for (uint u = 0; u < n; u++)
                {
                    for (uint v = 0; v < n; v++)
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.GetContinuousBounds(id, out double uMin, out double uMax, out double vMin, out double vMax);
                        TileGeometry.ToPosition(id, out double cx, out double cy, out double cz);

                        double elevation = elevations[idx];
                        bool isOceanic = oceanicFlags[idx];
                        double temperatureK = Temperature.TemperatureKelvin(
                            cx, cy, cz, climateDayT, climateOrbitalPeriodDays, climateRotationPeriodDays,
                            axialTiltRad, isOceanic, elevation, seaLevel);
                        Biome biome = BiomeClassification.Classify(temperatureK, isOceanic);
                        idx++;

                        // FONTOS: a sarkok magasságát KULON-KULON, a sarok
                        // SAJAT pozicioja alapjan szamoljuk (nem a tile
                        // kozepenek egyetlen erteket hasznaljuk mind a 4
                        // sarokra) - igy a szomszedos tile-ok UGYANAZT az
                        // erteket kapjak a kozos sarokpontjukra (a magassag-
                        // fuggveny tiszta, csak a pozitiotol fugg), es a
                        // felszin osszeer. Enelkul minden tile a sajat
                        // fuggetlen magassagara "lebeg", rest hagyva a
                        // szomszedok kozott.
                        Vector3 p00 = ToDisplacedVector3(face, uMin, vMin, seed, seeds);
                        Vector3 p10 = ToDisplacedVector3(face, uMax, vMin, seed, seeds);
                        Vector3 p11 = ToDisplacedVector3(face, uMax, vMax, seed, seeds);
                        Vector3 p01 = ToDisplacedVector3(face, uMin, vMax, seed, seeds);

                        List<Vector3> vertices = verticesByBiome[biome];
                        List<Vector3> normals = normalsByBiome[biome];
                        List<int> triangles = trianglesByBiome[biome];

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

            BuildMultiMaterialMesh(verticesByBiome, normalsByBiome, trianglesByBiome);
            BuildBorders(borderVerts, borderIndices);
        }

        private static readonly Biome[] AllBiomes = (Biome[])Enum.GetValues(typeof(Biome));

        private void BuildMultiMaterialMesh(
            Dictionary<Biome, List<Vector3>> verticesByBiome,
            Dictionary<Biome, List<Vector3>> normalsByBiome,
            Dictionary<Biome, List<int>> trianglesByBiome)
        {
            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var submeshTriangleLists = new List<int[]>();
            var materials = new List<Material>();

            foreach (Biome biome in AllBiomes)
            {
                List<Vector3> verts = verticesByBiome[biome];
                if (verts.Count == 0) continue;

                int offset = allVertices.Count;
                allVertices.AddRange(verts);
                allNormals.AddRange(normalsByBiome[biome]);

                int[] tris = trianglesByBiome[biome].ToArray();
                for (int i = 0; i < tris.Length; i++) tris[i] += offset;
                submeshTriangleLists.Add(tris);
                materials.Add(GetOrCreateBiomeMaterial(biome));
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

        private Dictionary<Biome, Material> _biomeMaterials;

        private Material GetOrCreateBiomeMaterial(Biome biome)
        {
            _biomeMaterials ??= new Dictionary<Biome, Material>();
            if (_biomeMaterials.TryGetValue(biome, out Material existing) && existing != null)
                return existing;

            Material mat = CreateFlatColorMaterial(BiomeColor(biome));
            _biomeMaterials[biome] = mat;
            return mat;
        }

        private static Color BiomeColor(Biome biome) => biome switch
        {
            Biome.Ocean => new Color(0.09f, 0.30f, 0.55f),
            Biome.SeaIce => new Color(0.80f, 0.88f, 0.93f),
            Biome.IceSheet => new Color(0.95f, 0.96f, 0.98f),
            Biome.Tundra => new Color(0.52f, 0.52f, 0.42f),
            Biome.Temperate => new Color(0.22f, 0.52f, 0.20f),
            Biome.Tropical => new Color(0.78f, 0.72f, 0.20f),
            _ => Color.magenta, // ismeretlen biome - szándékosan feltűnő jelzés
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
