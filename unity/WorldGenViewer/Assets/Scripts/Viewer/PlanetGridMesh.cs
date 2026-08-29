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
using WorldGen.Core.Terrain;

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

        [Header("M13: Vizfelszin (melysegfuggo szin)")]
        [Tooltip("Meter - a fenyelnyeles jellemzo melysege a 't = 1 - exp(-melyseg/skala)' " +
                 "telitodo gorbeben. Ennyi melyseg utan a vizszin mar kozel a legsotetebb " +
                 "arnyalatnal van; sekelyebb viznel a szin a sekelytol a mely fele fokozatosan sotetedik.")]
        [SerializeField]
        private double waterDepthScaleMeters = 500.0;

        [SerializeField]
        [Tooltip("A legsekelyebb (part menti) viz szine.")]
        private Color shallowWaterColor = new Color(0.20f, 0.65f, 0.65f);

        [SerializeField]
        [Tooltip("A legmelyebb (abisszikus) viz szine - majdnem fekete-kek, a valos " +
                 "oceanban a fenyelnyeles miatt latszo egyszinu sotetseg kozelitese.")]
        private Color deepWaterColor = new Color(0.01f, 0.03f, 0.10f);

        // M8: az utolsó Build() eredményének gyorsítótára - a panel-adatok
        // (ComputePanelData) ezekre épülnek, hogy ne kelljen a teljes
        // elevation-/óceán-/biome-számítást megismételni. Csak a render
        // UTÁN, egy adott Build()-hívásra érvényesek.
        private ulong _lastSeed;
        private Dictionary<TileId, double> _lastField;
        private Dictionary<TileId, bool> _lastIsOcean;
        private Dictionary<TileId, Biome> _lastBiomeOf;
        private double _lastSeaLevel;

        // ND-38: a t=0 (percentilis-kalibrált) víztérfogat gyorsítótára - CSAK
        // a világot meghatározó paraméterek (seed/plateCount/level/
        // targetWaterFraction) változásakor számoljuk újra, a `deepTimeMyr`
        // csúszka mozgatásakor NEM (ld. Build() lent). Enélkül minden egyes
        // deepTimeMyr-lekérdezés újra kiszámolná a t=0 statikus mezőt is,
        // feleslegesen - és ami fontosabb, a "megőrzött térfogat" fogalmának
        // ÉRTELME az, hogy egyetlen rögzített t=0 alapállapotra vonatkozik.
        private bool _hasInitialWaterVolumeCache;
        private ulong _volumeCacheSeed;
        private int _volumeCachePlateCount;
        private int _volumeCacheLevel;
        private double _volumeCacheTargetWaterFraction;
        private double _cachedInitialWaterVolume;

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

            // ND-38: terfogat-megmaradas alapu tengerszint. t=0-nal a REGI,
            // percentilis-modszert hasznaljuk VALTOZATLANUL (bitre ugyanaz a
            // szamitasi lanc, mint korabban) - ez garantalja, hogy a mar
            // vizualisan jovahagyott t=0 render bitre ugyanaz marad. t>0-nal a
            // t=0 statikus mezobol szarmazo, ROGZITETT viztertfogathoz (V0)
            // tartozo egyensulyi szintet keressuk meg - igy a viz-arany
            // TENYLEGESEN elmozdulhat 65%-tol, ahogy a domborzat a
            // lemezmozgas miatt valtozik (nem marad mindig mesterségesen
            // pontosan targetWaterFraction).
            double seaLevel;
            if (deepTimeMyr == 0.0)
            {
                seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, targetWaterFraction);
            }
            else
            {
                EnsureInitialWaterVolumeCache(seed);
                seaLevel = SeaLevelCalibration.CalibrateSeaLevelByVolume(field.Values, _cachedInitialWaterVolume);
            }
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

            // Kulcs = (RenderCategory, bucket). A legtobb kategorianal bucket
            // mindig 0 (egyetlen lapos szin); az RenderCategory.Ocean-nal a
            // bucket a MEGLEVO, mar kiszamolt tengerfenek-elevaciobol
            // (OceanRockBucket) szarmazo finom feny/sotet variacio indexe -
            // igy a tengerfenek nem teljesen egyszinu, de tovabbra sem kell
            // uj szimulacios adat vagy per-vertex szin/shader.
            var verticesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var normalsByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var trianglesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<int>>();

            // A vizfelszin KULON, tile-racsbol epul, de a RenderCategory-tol
            // fuggetlen buckettel (WaterDepthBucket, a helyi melysegbol) -
            // lasd BuildWaterSurface. Nem resze a fenti verticesByKey-nek,
            // mert a vizfelszin egy MASODIK, a tengerfenek folott ulo geometriai
            // reteg (kulon GameObject/mesh), nem egy tovabbi RenderCategory.
            var waterVerticesByBucket = new Dictionary<int, List<Vector3>>();
            var waterNormalsByBucket = new Dictionary<int, List<Vector3>>();
            var waterTrianglesByBucket = new Dictionary<int, List<int>>();
            float waterSurfaceRadius = radius + (float)(seaLevel * elevationScale);

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

                        // A tengerfenek (RenderCategory.Ocean) MEGLEVO
                        // elevation-erteket (a mar kiszamolt fraktal-zajjal
                        // egyutt) hasznaljuk fel egy finom feny/sotet
                        // bucket-hez - nincs uj szimulacios szamitas, csak a
                        // meglevo field[id] es a CrustElevation.OceanicBaseMeters
                        // referenciapont osszevetese (ld. OceanRockBucket).
                        int bucket = category == RenderCategory.Ocean ? OceanRockBucket(elevation) : 0;
                        var key = (category, bucket);

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

                        GetOrAddLists(verticesByKey, normalsByKey, trianglesByKey, key,
                            out List<Vector3> vertices, out List<Vector3> normals, out List<int> triangles);
                        AddQuad(vertices, normals, triangles, p00, p10, p11, p01);

                        // M13: vizfelszin - CSAK a folyekony (nem fagyott)
                        // oceani tile-ok folott, a KALIBRALT tengerszint
                        // sugaranal (nem a sajat, mely tengerfenek-sugaranal).
                        // A SeaIce tile-ok tovabbra is a sajat (jegszinu)
                        // kategoria-szinukon, a sajat magassagukon jelennek
                        // meg - nincs kulon vizreteg felettuk (fagyott
                        // feluletet abrazolnak, nem folyekony vizet).
                        if (isOceanic && biome == Biome.Ocean)
                        {
                            Vector3 wp00 = ToWaterVector3(face, uMin, vMin, waterSurfaceRadius);
                            Vector3 wp10 = ToWaterVector3(face, uMax, vMin, waterSurfaceRadius);
                            Vector3 wp11 = ToWaterVector3(face, uMax, vMax, waterSurfaceRadius);
                            Vector3 wp01 = ToWaterVector3(face, uMin, vMax, waterSurfaceRadius);

                            double depth = seaLevel - elevation;
                            int waterBucket = WaterDepthBucket(depth);
                            if (!waterVerticesByBucket.TryGetValue(waterBucket, out List<Vector3> waterVerts))
                            {
                                waterVerts = new List<Vector3>();
                                waterVerticesByBucket[waterBucket] = waterVerts;
                                waterNormalsByBucket[waterBucket] = new List<Vector3>();
                                waterTrianglesByBucket[waterBucket] = new List<int>();
                            }
                            AddQuad(waterVerts, waterNormalsByBucket[waterBucket], waterTrianglesByBucket[waterBucket],
                                wp00, wp10, wp11, wp01);
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

            BuildMultiMaterialMesh(verticesByKey, normalsByKey, trianglesByKey);
            BuildBorders(borderVerts, borderIndices);
            BuildCraterMarkers(craters, seed, seeds);
            BuildWaterSurface(waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket);

            _lastSeed = seed;
            _lastField = field;
            _lastIsOcean = isOceanField;
            _lastBiomeOf = biomeOf;
            _lastSeaLevel = seaLevel;

            Built.Invoke();
        }

        /// <summary>
        /// ND-38: a t=0 (statikus, percentilis-kalibrált) víztérfogat
        /// gyorsítótárazott kiszámítása - CSAK akkor fut újra a mögöttes
        /// elevation-mező kiszámítása, ha a világot meghatározó paraméterek
        /// (seed/plateCount/level/targetWaterFraction) az utolsó híváshoz
        /// képest változtak.
        /// </summary>
        private void EnsureInitialWaterVolumeCache(ulong seed)
        {
            if (_hasInitialWaterVolumeCache
                && _volumeCacheSeed == seed
                && _volumeCachePlateCount == plateCount
                && _volumeCacheLevel == level
                && _volumeCacheTargetWaterFraction == targetWaterFraction)
            {
                return;
            }

            Dictionary<TileId, double> field0 = SeaLevelCalibration.ComputeElevationField(seed, plateCount, level);
            double seaLevel0 = SeaLevelCalibration.CalibrateSeaLevel(field0.Values, targetWaterFraction);
            _cachedInitialWaterVolume = SeaLevelCalibration.ComputeFloodedVolumeProxy(field0.Values, seaLevel0);

            _volumeCacheSeed = seed;
            _volumeCachePlateCount = plateCount;
            _volumeCacheLevel = level;
            _volumeCacheTargetWaterFraction = targetWaterFraction;
            _hasInitialWaterVolumeCache = true;
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

        /// <summary>
        /// Egy negyszog (4 sarok) hozzaadasa a megadott vertex/normal/
        /// haromszog-listakhoz, a felszin kifele nezo normaljaval es a
        /// megfelelo (CW/CCW) haromszog-sorrenddel. KOZOS a szarazfold-
        /// (elevation-nel eltolt) es a vizfelszin- (fix tengerszint-sugaru)
        /// negyszogekhez - a ket geometria csak a sarokpontok forrasaban
        /// ter el, a negyszog->haromszog logika azonos.
        /// </summary>
        private static void AddQuad(
            List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
            Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01)
        {
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
        }

        /// <summary>Get-or-add segedfuggveny a (kategoria, bucket) kulcsu lista-harmashoz.</summary>
        private static void GetOrAddLists(
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> verticesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> normalsByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<int>> trianglesByKey,
            (RenderCategory Category, int Bucket) key,
            out List<Vector3> vertices, out List<Vector3> normals, out List<int> triangles)
        {
            if (!verticesByKey.TryGetValue(key, out vertices))
            {
                vertices = new List<Vector3>();
                normals = new List<Vector3>();
                triangles = new List<int>();
                verticesByKey[key] = vertices;
                normalsByKey[key] = normals;
                trianglesByKey[key] = triangles;
            }
            else
            {
                normals = normalsByKey[key];
                triangles = trianglesByKey[key];
            }
        }

        private void BuildMultiMaterialMesh(
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> verticesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> normalsByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<int>> trianglesByKey)
        {
            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var submeshTriangleLists = new List<int[]>();
            var materials = new List<Material>();

            // Determinisztikus sorrend (kategoria, majd bucket szerint) - a
            // dictionary bejarasi sorrendjere NEM szabad tamaszkodni (I1),
            // bar itt "csak" a draw call sorrendet befolyasolja, nem a
            // vilagmodellt - a stabil sorrend igy is jobb debugolhatosagot ad.
            var keys = new List<(RenderCategory Category, int Bucket)>(verticesByKey.Keys);
            keys.Sort((a, b) => a.Category != b.Category ? a.Category.CompareTo(b.Category) : a.Bucket.CompareTo(b.Bucket));

            foreach ((RenderCategory Category, int Bucket) key in keys)
            {
                List<Vector3> verts = verticesByKey[key];
                if (verts.Count == 0) continue;

                int offset = allVertices.Count;
                allVertices.AddRange(verts);
                allNormals.AddRange(normalsByKey[key]);

                int[] tris = trianglesByKey[key].ToArray();
                for (int i = 0; i < tris.Length; i++) tris[i] += offset;
                submeshTriangleLists.Add(tris);
                materials.Add(GetOrCreateCategoryMaterial(key));
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
        /// A viz felszinenek pozicioja egy adott tile-sarokban: IRANY a
        /// racsbol (PositionFromFaceUV), de a sugar FIX (a kalibralt
        /// tengerszintnel, `waterSurfaceRadius`) - szemben a szarazfold
        /// ToDisplacedVector3-javal, ahol a sugar tile-onkent (sot
        /// sarkonkent) elter a valodi domborzat szerint. Mivel a sugar
        /// MINDEN vizes sarokra ugyanaz a konstans, ket szomszedos vizes
        /// tile automatikusan varratmentesen illeszkedik - nincs szukseg
        /// a plate-/hatar-/krater-fuggveny kiertekelesere (mint a
        /// ComputeDisplacedRadius-ban), a viz sima, lapos felulet.
        /// </summary>
        private Vector3 ToWaterVector3(int face, double uc, double vc, float waterSurfaceRadius)
        {
            TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);
            return BodyFrameConversion.ToUnity(x, y, z) * waterSurfaceRadius;
        }

        /// <summary>
        /// Melyseg (meter) -> bucket-index a WaterDepthBucketCount lepesu,
        /// de a szemnek sima hatasu telitodo gorbehez: t = 1 - exp(-melyseg/skala).
        /// A t=0-hoz (sekely) a shallowWaterColor, t~1-hez (mely) a
        /// deepWaterColor tartozik (ld. WaterBucketColor) - a valos vizben
        /// tortent fenyelnyeles kozelitese, NEM a vilagmodell resze (csak
        /// megjelenitesi szinvalasztas), ezert a Math.Exp hasznalata itt
        /// NEM erinti a CLAUDE.md ND-23 transzcendens-fuggveny korlatozasat
        /// (az kizarolag a src/WorldGen.Core szimulacios kritikus utjara
        /// vonatkozik, nem a viewer renderelesere).
        /// </summary>
        private int WaterDepthBucket(double depthMeters)
        {
            double clampedDepth = depthMeters < 0.0 ? 0.0 : depthMeters;
            double safeScale = waterDepthScaleMeters > 1.0 ? waterDepthScaleMeters : 1.0;
            double t = 1.0 - Math.Exp(-clampedDepth / safeScale);
            int bucket = (int)Math.Round(t * (WaterDepthBucketCount - 1));
            return bucket < 0 ? 0 : (bucket > WaterDepthBucketCount - 1 ? WaterDepthBucketCount - 1 : bucket);
        }

        private Color WaterBucketColor(int bucket)
        {
            double bucketT = (bucket + 0.5) / WaterDepthBucketCount;
            if (bucketT > 1.0) bucketT = 1.0;
            return Color.Lerp(shallowWaterColor, deepWaterColor, (float)bucketT);
        }

        private const int WaterDepthBucketCount = 12;

        private Dictionary<int, Material> _waterMaterials;

        private Material GetOrCreateWaterMaterial(int bucket)
        {
            _waterMaterials ??= new Dictionary<int, Material>();
            if (_waterMaterials.TryGetValue(bucket, out Material existing) && existing != null)
                return existing;

            Material mat = CreateFlatColorMaterial(WaterBucketColor(bucket));
            _waterMaterials[bucket] = mat;
            return mat;
        }

        /// <summary>
        /// M13 vizfelszin-reteg: minden VIZ ALATTI, FOLYEKONY (biome ==
        /// Ocean, tehat NEM fagyott SeaIce) tile folott egy LAPOS negyszog
        /// a KALIBRALT tengerszint sugaranal - igy folytonos, sima
        /// vizfelszin rajzolodik ki, nem a tengerfenek dombormintaja. A
        /// szint a helyi melyseg (WaterDepthBucket) hatarozza meg.
        ///
        /// EZ VALTJA FEL a regi BuildOceanShell-t (kulonallo, tile-racstol
        /// FUGGETLEN, EGYSEGES szinu primitiv gomb egyetlen fix sugaron).
        /// A regi megoldas hibaja: mivel a hej sugara es szine SEMMILYEN
        /// tile-adatot nem hasznalt, a mely oceani tile-ok folott a
        /// (joval a hej sugara ALATT futo) domborzat-mesh sosem "utkozott"
        /// a hejjal - onnan nezve csak a hej egyseges kek szine latszott.
        /// A sekely, tengerszinthez kozeli tile-oknal viszont a domborzat
        /// majdnem elerte a hej sugarat, ahol a tengerfenek-szin atuthetett
        /// - ez adta a "ket tengerszint / csak a sekely reszek szurkek"
        /// hibat. Mostantol a viz UGYANABBOL a field/isOceanField/seaLevel
        /// adatbol epul, mint a tengerfenek, tehat strukturalisan nem tud
        /// elszakadni tole - nincs kulon, nem-szinkronizalt geometria.
        /// </summary>
        private void BuildWaterSurface(
            Dictionary<int, List<Vector3>> verticesByBucket,
            Dictionary<int, List<Vector3>> normalsByBucket,
            Dictionary<int, List<int>> trianglesByBucket)
        {
            Transform waterChild = transform.Find("WaterSurface");
            GameObject waterGo;
            if (waterChild == null)
            {
                waterGo = new GameObject("WaterSurface");
                waterGo.transform.SetParent(transform, false);
                waterGo.AddComponent<MeshFilter>();
                waterGo.AddComponent<MeshRenderer>();
            }
            else
            {
                waterGo = waterChild.gameObject;
            }

            if (verticesByBucket.Count == 0)
            {
                waterGo.SetActive(false);
                return;
            }
            waterGo.SetActive(true);

            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var submeshTriangleLists = new List<int[]>();
            var materials = new List<Material>();

            var buckets = new List<int>(verticesByBucket.Keys);
            buckets.Sort();

            foreach (int bucket in buckets)
            {
                List<Vector3> verts = verticesByBucket[bucket];
                if (verts.Count == 0) continue;

                int offset = allVertices.Count;
                allVertices.AddRange(verts);
                allNormals.AddRange(normalsByBucket[bucket]);

                int[] tris = trianglesByBucket[bucket].ToArray();
                for (int i = 0; i < tris.Length; i++) tris[i] += offset;
                submeshTriangleLists.Add(tris);
                materials.Add(GetOrCreateWaterMaterial(bucket));
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(allVertices);
            mesh.SetNormals(allNormals);
            mesh.subMeshCount = submeshTriangleLists.Count;
            for (int i = 0; i < submeshTriangleLists.Count; i++)
                mesh.SetTriangles(submeshTriangleLists[i], i);
            mesh.RecalculateBounds();

            waterGo.GetComponent<MeshFilter>().sharedMesh = mesh;
            waterGo.GetComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
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

            Material markerMaterial = CreateFlatColorMaterial(CategoryColor(RenderCategory.Crater, 0));
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
            // ND-36 (domain warping): a lemez-hozzarendeles a WARPOLT
            // poziciot kapja - UGYANAZ a szabaly, mint amit a Core-oldali
            // SeaLevelCalibration/PlateBoundaryEffect hasznal, kulonben ez
            // a sarok-alapu megjelenites inkonzisztens (nem-warpolt)
            // lemezhatarokat mutatna a tile-kozepu adatokhoz kepest.
            DomainWarp.WarpPosition(seed, x, y, z, out double wx, out double wy, out double wz);
            int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
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

        // ND-34-nel mar bevezetett referencia-amplitudo (CrustElevation):
        // az oceani zaj +-(NoiseAmplitudeMeters * OceanicNoiseFactor)
        // korul mozog a MountainMask altal tovabb tompitva - ezt hasznaljuk
        // normalizalasi skalakent a tengerfenek feny/sotet bucket-jehez.
        // NEM uj szimulacios konstans, csak a MAR LETEZO ertekek szorzata.
        private const int OceanRockBucketCount = 5;

        /// <summary>
        /// A tengerfenek (RenderCategory.Ocean) tile-jainak finom feny/
        /// sotet variacioja - a MEGLEVO, mar kiszamolt `elevation` es a
        /// kereg-tipus alap-magassaga (CrustElevation.OceanicBaseMeters)
        /// kulonbsegebol, NEM uj zaj-lekerdezesbol. Igy a mar meglevo
        /// fraktal-domborzat (ND-33/ND-34 ridged multifractal, oceani
        /// tompitassal) latszik a szinben is, nem csak a geometriaban.
        /// </summary>
        private static int OceanRockBucket(double elevation)
        {
            double referenceAmplitude = CrustElevation.NoiseAmplitudeMeters * CrustElevation.OceanicNoiseFactor;
            double normalized = referenceAmplitude > 0.0
                ? (elevation - CrustElevation.OceanicBaseMeters) / referenceAmplitude
                : 0.0;
            normalized = normalized < -1.0 ? -1.0 : (normalized > 1.0 ? 1.0 : normalized);
            double t = (normalized + 1.0) * 0.5; // [-1,1] -> [0,1]
            int bucket = (int)Math.Round(t * (OceanRockBucketCount - 1));
            return bucket < 0 ? 0 : (bucket > OceanRockBucketCount - 1 ? OceanRockBucketCount - 1 : bucket);
        }

        private static Color OceanRockColor(int bucket)
        {
            Color baseColor = new Color(0.34f, 0.33f, 0.30f);
            float t = OceanRockBucketCount > 1 ? (float)bucket / (OceanRockBucketCount - 1) : 0.5f;
            float brightness = Mathf.Lerp(0.82f, 1.18f, t); // sotetebb melyedes -> vilagosabb kiemelkedes
            return new Color(
                Mathf.Clamp01(baseColor.r * brightness),
                Mathf.Clamp01(baseColor.g * brightness),
                Mathf.Clamp01(baseColor.b * brightness),
                baseColor.a);
        }

        private Dictionary<(RenderCategory Category, int Bucket), Material> _categoryMaterials;

        private Material GetOrCreateCategoryMaterial((RenderCategory Category, int Bucket) key)
        {
            _categoryMaterials ??= new Dictionary<(RenderCategory Category, int Bucket), Material>();
            if (_categoryMaterials.TryGetValue(key, out Material existing) && existing != null)
                return existing;

            Material mat = CreateFlatColorMaterial(CategoryColor(key.Category, key.Bucket));
            _categoryMaterials[key] = mat;
            return mat;
        }

        private static Color CategoryColor(RenderCategory category, int bucket) => category switch
        {
            // A tengerfenek MAR NEM egyetlen lapos szin - az OceanRockBucket
            // a MEGLEVO elevation-bol szarmazo finom feny/sotet variaciot ad
            // (ld. OceanRockColor). A viz vizualis jelzeset a KULON
            // BuildWaterSurface reteg adja, a kalibralt tengerszint
            // sugaranal, a helyi melysegtol fuggo szinnel (WaterBucketColor) -
            // igy a tengerfenek (ez a szin) es a viz (kulon reteg) egyutt
            // adjak ki a vegso kepet, nem keverednek ossze egyetlen
            // "Ocean" szinben.
            RenderCategory.Ocean => OceanRockColor(bucket),
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
