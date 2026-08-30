using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using WorldGen.Viewer.Lod;
using Debug = UnityEngine.Debug;

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
        [Tooltip("REFERENCIA-szint (M2-M8): a tengerszint-kalibráció és a " +
                 "panel-adatok (ComputePanelData) mindig EZEN a fix szinten " +
                 "számolódnak, FÜGGETLENÜL a kamerától. " +
                 "FONTOS - GYAKORI TÉVESZTÉS: ha `useAdaptiveLod` be van " +
                 "kapcsolva (alapból igen), ez a mező NEM szabályozza a " +
                 "ténylegesen renderelt felszín részletességét - ahhoz az " +
                 "'M9: Adaptiv kvadfa-LOD' szakasz `adaptiveMaxLevel` mezője " +
                 "tartozik, lent. Level 5-6 ajánlott ennek a referencia-" +
                 "passznak (6144-24576 tile).")]
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
        private Color shallowWaterColor = new Color(0.18f, 0.48f, 0.52f);

        [SerializeField]
        [Tooltip("A legmelyebb (abisszikus) viz szine - majdnem fekete-kek, a valos " +
                 "oceanban a fenyelnyeles miatt latszo egyszinu sotetseg kozelitese.")]
        private Color deepWaterColor = new Color(0.05f, 0.14f, 0.26f);

        [Header("M9: Adaptiv kvadfa-LOD (docs/05-milestones.md §9)")]
        [SerializeField]
        [Tooltip("Ha be van kapcsolva, a fix `level`-es Build() UTAN egy kamera-" +
                 "vezerelt, valtozo-szintu adaptiv mesh valtja fel a renderelt " +
                 "geometriat (a referencia-szintu field/seaLevel/panel-adatok " +
                 "valtozatlanul a fix `level`-en szamolodnak, ld. §9.3). " +
                 "Kikapcsolva a viselkedes BITRE ugyanaz, mint M9 elott.")]
        private bool useAdaptiveLod = true;

        [SerializeField, Range(0, 6)]
        [Tooltip("A kvadfa gyoker-szintje - EZ A MINDIG GARANTALT, zoomolas " +
                 "nelkul is lathato minimum-reszletesseg (a finomodas ezen " +
                 "FELUL, a kamera latokupjaban tortenik). 4-re levezetve " +
                 "(ld. adaptiveSplitFactor doksija) - kis levelen kezdunk, " +
                 "mert a splitFactor mar TAVOLROL (300 egysegnel is) elkezd " +
                 "finomitani, tehat a bazisnak nem kell olyan magasnak lennie, " +
                 "mint amikor meg nem volt latokup-fuggo finomodas.")]
        private int adaptiveBaseLevel = 4;

        [SerializeField, Range(0, 18)]
        [Tooltip("A kvadfa max melysege (nem bomlik finomabbra ennel). " +
                 "MERT PONT 16: a `minDistance` (kb. a felszin) kozeleben, " +
                 "`adaptiveSplitFactor`=32.5 mellett a rendszer MAGATOL, " +
                 "termeszetes hatarkent all meg kb. level 16-on - ennel " +
                 "magasabbra allitani nem ad tobb reszletet, csak feleslegesen " +
                 "tagabb Inspector-tartomanyt.")]
        private int adaptiveMaxLevel = 18;

        [SerializeField]
        [Tooltip("K_split - felbontasi kuszob (tavolsag/befoglalo-sugar arany). " +
                 "K_merge = 1.5x ennek (hiszterezis), ld. AdaptiveQuadTree. " +
                 "LEVEZETVE (nem probalgatva): egy tile kb. theta=2*rTile/d " +
                 "szog alatt latszik `d` tavolsagbol - ha azt akarjuk, hogy a " +
                 "lathato mezoben (FOV) legalabb T tile ferjen el egy iranyban, " +
                 "a celzott szogmeret theta_target=FOV/sqrt(T), amibol " +
                 "splitFactor=2/theta_target=2*sqrt(T)/FOV_rad. T=17 " +
                 "(kb. 289 tile-cel a lathato mezoben - a felhasznaloval " +
                 "egyeztetett, teljesitmenyileg meg biro celszam, ld. " +
                 "docs/05-milestones.md §9) es FOV=60deg mellett ez ~32.5. " +
                 "FONTOS ELOFELTETEL: ez a kepzet CSAK a latokup-szuressel " +
                 "(ld. forwardX/Y/Z + halfFovRadians a RecomputeCutAndRebuildAdaptiveMesh-ben) " +
                 "egyutt mukodik jol - szures nelkul a kamera KORULI teljes " +
                 "korlapot probalna ennyire finomitani, ami tobbszaz-ezres, " +
                 "hasznalhatatlan tile-szamot adott korabban.")]
        private double adaptiveSplitFactor = 61.0;

        [SerializeField]
        [Tooltip("Biztonsagi szorzo a kamera FOV/aspect-jabol szamolt " +
                 "latokup-felszoghoz (ld. RecomputeCutAndRebuildAdaptiveMesh) - " +
                 "1-nel nagyobb erdemes, hogy a kup SZELEN levo, meg reszben " +
                 "lathato csomopontok se essenek ki tul korán.")]
        private double fovSafetyMargin = 1.3;

        [SerializeField]
        [Tooltip("Unity-egyseg: mennyit kell mozdulnia a kameranak (a bolygo " +
                 "kozeppontjahoz kepest) ket kvadfa-ujraszamolas kozott. Enelkul " +
                 "minden egyes frame-ben ujraszamolna a cut-ot es ujraepitene a " +
                 "mesh-t, feleslegesen (ld. §9.1 'csak amikor a kamera erdemben " +
                 "mozdul').")]
        private float adaptiveCameraMoveThreshold = 0.5f;

        [SerializeField]
        [Tooltip("Geomorphing (§9.2): a split utan megjeleno csucsok ekkora " +
                 "hanyada (a K_split-tavolsaghoz kepesti tortresz) alatt erik el " +
                 "a teljes (nem-morpholt) veglegeset pozíciójukat. Minel nagyobb, " +
                 "annal fokozatosabb az atmenet.")]
        private double geomorphRangeFraction = 0.6;

        [SerializeField]
        [Tooltip("Ha ures, a Camera.main-t hasznalja - explicit beallithato, ha " +
                 "tobb kamera van a jelenetben (pl. UI-kamera is).")]
        private Camera adaptiveCameraOverride;

        [SerializeField]
        [Tooltip("A perzisztens sarok-cache (ND-39 'C' + §9.4) maximalis meret " +
                 "elemszamban - efole LRU-eviction tortenik, hogy a memoria ne " +
                 "nojon korlatlanul hosszan tarto kamera-mozgas soran.")]
        private int cornerCacheMaxSize = 300_000;

        [SerializeField]
        [Tooltip("A per-tile klasszifikacios cache (elevacio/homerseklet/biome/ " +
                 "ocean, ld. AdaptiveTileClassification) maximalis meret " +
                 "elemszamban - ez a LEGDRAGABB szamitas gyorsitotara " +
                 "(Temperature.TemperatureKelvin napi 24 mintaveteles " +
                 "inszolacio-atlaggal), enelkul minden kamera-mozgas-kivaltotta " +
                 "ujraepites a TELJES cutot ujraszamolna - ez okozta a sulyos " +
                 "lefagyast nagy adaptiveBaseLevel mellett.")]
        private int tileClassificationCacheMaxSize = 300_000;

        [SerializeField]
        [Tooltip("Minimum ido (masodperc) ket adaptiv ujraepites kozott, " +
                 "MEG AKKOR IS, ha a kamera kozben tobbszor is atlepte a " +
                 "mozgas-kuszobot - enelkul folyamatos egerhuzas/zoom kozben " +
                 "MINDEN frame-ben ujraepitene, ami meg a per-tile cache " +
                 "mellett is felesleges terhelest jelentene nagy cut-meretnel.")]
        private float minSecondsBetweenAdaptiveRebuilds = 0.1f;

        [SerializeField]
        [Tooltip("Diagnosztikai kuszob (ms): ha egy adaptiv mesh-ujraepites " +
                 "ennel tovabb tart, figyelmezteto uzenet - ld. §9.5. NEM allit " +
                 "meg semmit, csak jelez (a tenyleges frame-koltsegvetes-alapu " +
                 "amortizacio/Job-System aszinkron epites halasztott munka, ld. " +
                 "docs/04-decisions.md ND-40).")]
        private double adaptiveRebuildWarningMs = 50.0;

        private HashSet<TileId> _currentCut;
        private readonly Dictionary<(int Face, int Level, uint CornerU, uint CornerV), Vector3> _persistentCornerCache = new();
        private readonly LinkedList<(int Face, int Level, uint CornerU, uint CornerV)> _cornerCacheLru = new();
        private readonly Dictionary<(int Face, int Level, uint CornerU, uint CornerV), LinkedListNode<(int Face, int Level, uint CornerU, uint CornerV)>> _cornerCacheLruNodes = new();

        private bool _hasLastCutCameraPosition;
        private Vector3 _lastCutCameraPosition;
        private double _lastCutCameraCoreX, _lastCutCameraCoreY, _lastCutCameraCoreZ;

        // Az adaptiv ujraepiteshez szukseges "vilag-kontextus", amit a Build()
        // egyszer szamol ki (referencia-szinten) - az Update()-ben futo
        // ujraszamolasok ezt hasznaljak ujra, nem szamoljak ujra minden frame-ben.
        private ulong _adaptiveSeed;
        private (double X, double Y, double Z)[] _adaptiveSeeds;
        private List<ImpactCratering.CraterRecord> _adaptiveCraters;
        private double _adaptiveSeaLevel;
        private HashSet<TileId> _adaptiveRiverTiles;
        private double _adaptiveAxialTiltRad;

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

        /// <summary>
        /// M9: amig a jatek fut, a kamera-vezerelt kvadfa-cut ujraszamolasa -
        /// CSAK akkor, ha a kamera erdemben mozdult (ld. adaptiveCameraMoveThreshold
        /// dokumentacioja) VAGY az adaptiv beallitasok (base/max level, split
        /// faktor stb.) valtoztak az Inspectorban (ld. OnValidate) - kulonben
        /// a beallitasok Play kozbeni modositasa NEM latszana, amig a kamera
        /// nem mozdul (a felhasznalo eppen ezt eszlelte hibakent). Nem fut
        /// Edit modeban (a MonoBehaviour.Update() alapertelmezesen csak Play
        /// modeban hivodik, az osztalynak nincs [ExecuteAlways]-e) - ugyanaz a
        /// viselkedes, mint a PlanetOrbitCamera egerhuzas-figyeleset.
        /// </summary>
        private void Update()
        {
            if (!useAdaptiveLod || _lastField == null)
                return;

            Camera cam = GetAdaptiveCamera();
            if (cam == null)
                return;

            BodyFrameConversion.ToCore(transform.InverseTransformPoint(cam.transform.position), out double camX, out double camY, out double camZ);
            var camPos = new Vector3((float)camX, (float)camY, (float)camZ);

            bool movedEnough = !_hasLastCutCameraPosition || Vector3.Distance(camPos, _lastCutCameraPosition) >= adaptiveCameraMoveThreshold;
            if (!movedEnough && !_adaptiveConfigDirty)
                return;

            // Ido-alapu fekezes: folyamatos egerhuzas/zoom kozben a mozgas-
            // kuszob magaban meg mindig FRAME-enkent atlepheto lenne - ez a
            // masodik korlat biztositja, hogy nagy cut-meretnel (magas
            // adaptiveBaseLevel) se probaljon tobbszor ujraepiteni
            // masodpercenkent, mint amennyit minSecondsBetweenAdaptiveRebuilds
            // enged. A camPos/dirty-allapotot NEM valtoztatjuk itt - a
            // KOVETKEZO Update()-ben ujra megprobalja, amint a kuszob lejart
            // (tehat a valtozas nem vesz el, csak kesik).
            if (Time.unscaledTime - _lastAdaptiveRebuildRealtime < minSecondsBetweenAdaptiveRebuilds)
                return;

            _adaptiveConfigDirty = false;
            _lastAdaptiveRebuildRealtime = Time.unscaledTime;
            RecomputeCutAndRebuildAdaptiveMesh(cam, camX, camY, camZ);
        }

        private float _lastAdaptiveRebuildRealtime = float.NegativeInfinity;

        private Camera GetAdaptiveCamera() => adaptiveCameraOverride != null ? adaptiveCameraOverride : Camera.main;

        // M9: igaz, ha az Inspectorban valtozott valamelyik adaptiv-LOD mezo
        // (ld. OnValidate) - az Update() ezt is figyeli a kamera-mozgas
        // kuszobe mellett, kulonben Play kozbeni Inspector-modositas csak a
        // KOVETKEZO kamera-mozgaskor latszana, ami felreveznto ("nem tortent
        // semmi" - pedig csak nem volt ujraszamolasi ok).
        private bool _adaptiveConfigDirty;

        /// <summary>
        /// Unity minden Inspector-mezo-modositas UTAN meghivja (Edit ES Play
        /// modeban is) - itt csak egy dirty-flaget allitunk, a tenyleges
        /// ujraszamolas az Update()-ben tortenik (OnValidate-bol NEM biztonsagos
        /// kozvetlenul Mesh-t epiteni/GameObject-et letrehozni, ld. Unity-
        /// dokumentacio).
        /// </summary>
        private void OnValidate()
        {
            _adaptiveConfigDirty = true;
        }

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

            // ND-39 "C" opcio (sarok-dedupliakcio): egy belso sarokpontot
            // (ugyanazon LAPON belul) akar 4 szomszedos tile is MEGOSZT -
            // enelkul a ComputeDisplacedRadius (AssignPlate + warp +
            // ElevationWithBoundaryFromWarped, ld. odalejjebb) minden egyes
            // sarokra AKAR 4-SZER futna le feleslegesen. A kulcs (face,
            // cornerU, cornerV) a lap-lokalis EGESZ racsponthoz kotott
            // (n = 1<<level, cornerU/cornerV in [0,n]) - lapon belul ez
            // pontosan ugyanazt a folytonos (uc,vc)-t adja, amit a korabbi,
            // redundans hivas is hasznalt (ld. GetOrComputeCorner), tehat
            // BITRE AZONOS eredmenyt ad, csak egyszer szamolva. A kulcs NEM
            // von ossze sarkokat KULONBOZO lapok kozott (a kockale-elek/
            // sarkok menten) - ott a jelenlegi kod is FUGGETLENUL, lapankent
            // szamolja ki ugyanazt a geometriai pontot (ld. TileGeometry.
            // PositionFromFaceUV), es ez a viselkedes VALTOZATLAN marad -
            // nem tesz hozza es nem vesz el semmit a lap-hatarok kezelesebol.
            var cornerCache = new Dictionary<(int Face, uint CornerU, uint CornerV), Vector3>();

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
                        Vector3 p00 = GetOrComputeCorner(cornerCache, face, u, v, n, seed, seeds, craters);
                        Vector3 p10 = GetOrComputeCorner(cornerCache, face, u + 1, v, n, seed, seeds, craters);
                        Vector3 p11 = GetOrComputeCorner(cornerCache, face, u + 1, v + 1, n, seed, seeds, craters);
                        Vector3 p01 = GetOrComputeCorner(cornerCache, face, u, v + 1, n, seed, seeds, craters);

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

            // M9: a tovabbi (Update()-ben futo) adaptiv ujraepitesek ezt a
            // referencia-szinten mar kiszamolt kontextust hasznaljak ujra -
            // ld. §9.3 "a tengerszint EGYSZER, a FIX referencia-szinten
            // szamolodik, es az adaptiv renderer konstans bemenetkent kezeli".
            _adaptiveSeed = seed;
            _adaptiveSeeds = seeds;
            _adaptiveCraters = craters;
            _adaptiveSeaLevel = seaLevel;
            _adaptiveRiverTiles = riverTiles;
            _adaptiveAxialTiltRad = axialTiltRad;

            // Uj referencia-allapot -> a per-tile cache-ek (sarok +
            // klasszifikacio) ervenytelenek, mert regi vilagallapotra
            // (elozo seed/deepTimeMyr/craterek/seaLevel) vonatkoznanak.
            InvalidateAdaptiveCaches();

            if (useAdaptiveLod)
            {
                Camera cam = GetAdaptiveCamera();
                if (cam != null)
                {
                    BodyFrameConversion.ToCore(transform.InverseTransformPoint(cam.transform.position), out double camX, out double camY, out double camZ);
                    RecomputeCutAndRebuildAdaptiveMesh(cam, camX, camY, camZ);
                }
            }

            Built.Invoke();
        }

        /// <summary>
        /// M9 kozponti belepesi pontja: uj cut szamolasa a MEGLEVO cut-bol
        /// (hiszterezis, ld. AdaptiveQuadTree), majd a mesh teljes ujraepitese
        /// a cut-bol. A `Built.Invoke()`-ot NEM hivja ujra - az csak a Build()
        /// (referencia-szintu passz) vegen tuzel, a panel-adatok szempontjabol
        /// ez az esemeny releváns, nem az egyes adaptiv ujraepitesek.
        /// </summary>
        private void RecomputeCutAndRebuildAdaptiveMesh(Camera cam, double camX, double camY, double camZ)
        {
            // Vedelmi korlat Inspector-hiba ellen (pl. base > max eseten az
            // AdaptiveQuadTree kivetelt dobna minden Update()-ben) - a [Range]
            // attributumok kulon-kulon mar korlatoznak, de az egymashoz
            // kepesti sorrendet nem.
            int effectiveBaseLevel = Math.Min(adaptiveBaseLevel, adaptiveMaxLevel);
            double effectiveSplitFactor = Math.Max(adaptiveSplitFactor, 0.01);

            // Latokup-szures (kritikus a teljesitmenyhez, ld. AdaptiveQuadTree
            // doksija): iranyvektor (Unity vilag -> Core-keret, IRANY, tehat
            // InverseTransformDirection, NEM InverseTransformPoint - nincs
            // eltolas) + a kamera TENYLEGES FOV/aspect-jabol szamolt felszog,
            // biztonsagi margoval. Enelkul a rendszer a kamera KORULI teljes
            // korlapot finomitana, fuggetlenul attol, mi latszik a kepernyon -
            // ez okozott korabban tobbszaz-ezres, hasznalhatatlan tile-szamot.
            Vector3 localForward = transform.InverseTransformDirection(cam.transform.forward);
            BodyFrameConversion.ToCore(localForward, out double fwdX, out double fwdY, out double fwdZ);

            double verticalFovRad = cam.fieldOfView * Mathf.Deg2Rad;
            double horizontalFovRad = 2.0 * Math.Atan(Math.Tan(verticalFovRad / 2.0) * Math.Max(0.01f, cam.aspect));
            double halfFovRadians = Math.Max(verticalFovRad, horizontalFovRad) / 2.0 * fovSafetyMargin;

            _currentCut = AdaptiveQuadTree.BuildCut(
                camX, camY, camZ, radius, _currentCut,
                effectiveBaseLevel, adaptiveMaxLevel, effectiveSplitFactor, effectiveSplitFactor * 1.5,
                fwdX, fwdY, fwdZ, halfFovRadians);

            _lastCutCameraCoreX = camX;
            _lastCutCameraCoreY = camY;
            _lastCutCameraCoreZ = camZ;
            _lastCutCameraPosition = new Vector3((float)camX, (float)camY, (float)camZ);
            _hasLastCutCameraPosition = true;

            var stopwatch = Stopwatch.StartNew();
            RebuildAdaptiveMesh();
            stopwatch.Stop();
            if (stopwatch.Elapsed.TotalMilliseconds > adaptiveRebuildWarningMs)
            {
                Debug.LogWarning(
                    $"PlanetGridMesh: adaptiv ujraepites {stopwatch.Elapsed.TotalMilliseconds:F1}ms " +
                    $"(kuszob {adaptiveRebuildWarningMs}ms, cut merete {_currentCut.Count}) - " +
                    "ha ez rendszeres, csokkentsd az adaptiveMaxLevel-t vagy noveld az " +
                    "adaptiveCameraMoveThreshold-ot (ld. docs/05-milestones.md §9.5).");
            }
        }

        /// <summary>
        /// A jelenlegi `_currentCut` (valtozo-szintu aktiv level-ek) mesh-be
        /// epitese - ugyanazokat a megjelenitesi segedfuggvenyeket hasznalja,
        /// mint a fix-szintu Build() (BuildMultiMaterialMesh/BuildBorders/
        /// BuildWaterSurface/BuildCraterMarkers), csak a bemeneti tile-halmaz
        /// valtozo szintu es a per-tile adatok (eleváció/biome/ocean) PONTSZERUEN,
        /// az adott level-en szamolodnak (ld. EmitAdaptiveTile), nem egy elore
        /// kiszamolt, egyetlen-szintu dictionary-bol.
        /// </summary>
        private void RebuildAdaptiveMesh()
        {
            if (_currentCut == null)
                return;

            // GPU-CALC / teljesitmeny: a DRAGA per-tile Core-kiertekeleseket
            // (klasszifikacio: eleváció+homerseklet+biome; sarkak: eleváció a
            // sarokpontokban) TOBB SZALON, elore kiszamoljuk es a cache-be
            // toltjuk - a WorldGen.Core lanc igazoltan tiszta fuggvenyekbol
            // all (nincs megosztott mutable allapot, nincs heap-allokacio
            // hivasonkent), tehat Parallel.For-ral biztonsagosan
            // parhuzamosithato. A CACHE-BE IRAS maga NEM parhuzamos (a
            // Dictionary/LinkedList LRU nem szalbiztos) - ezert ket fazisu:
            // (1) parhuzamosan szamoljuk a hianyzo ertekeket kulon
            // tombbe, (2) egyszalon irjuk be a cache-be. Az ezutani
            // EmitAdaptiveTile-hivasok mar csupa cache-talalatot csak
            // olvasnak, tehat gyorsak maradnak.
            TileId[] leaves = new TileId[_currentCut.Count];
            _currentCut.CopyTo(leaves);
            PrecomputeClassificationsInParallel(leaves);
            PrecomputeCornersInParallel(leaves);

            var verticesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var normalsByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var trianglesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<int>>();
            var waterVerticesByBucket = new Dictionary<int, List<Vector3>>();
            var waterNormalsByBucket = new Dictionary<int, List<Vector3>>();
            var waterTrianglesByBucket = new Dictionary<int, List<int>>();
            var borderVerts = new List<Vector3>();
            var borderIndices = new List<int>();
            float waterSurfaceRadius = radius + (float)(_adaptiveSeaLevel * elevationScale);

            foreach (TileId leaf in _currentCut)
            {
                EmitAdaptiveTile(
                    leaf, verticesByKey, normalsByKey, trianglesByKey,
                    waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket,
                    borderVerts, borderIndices, waterSurfaceRadius);
            }

            BuildMultiMaterialMesh(verticesByKey, normalsByKey, trianglesByKey);
            BuildBorders(borderVerts, borderIndices);
            // MEGJEGYZES: BuildCraterMarkers() SZANDEKOSAN NINCS itt - a
            // krater-markerek GameObject.CreatePrimitive()-mel dolgoznak,
            // ami Unity-ben soronkent DRAGA (nem csak egy Mesh-adat-frissites).
            // A krater-lista (_adaptiveCraters) a kamera-kivaltotta adaptiv
            // ujraepitesek kozott NEM valtozik (csak a Build() valtoztatja,
            // ott mar meghivodik lent) - ide betenni azt jelentette, hogy
            // MOZGAS KOZBEN, masodpercenkent akar 10-szer ujra le- es
            // felepitette az OSSZES kratert, ami a felhasznalo altal eszlelt
            // "teljesen halott" egerkezeles fo oka volt.
            BuildWaterSurface(waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket);

            EvictCornerCacheIfNeeded();
            EvictTileClassificationCacheIfNeeded();
        }

        /// <summary>
        /// Egyetlen aktiv level (a cut egy eleme) hozzaadasa a mesh-epito
        /// listakhoz. A per-tile adatok (elevacio/hőmérséklet/biome/ocean)
        /// PONTSZERUEN, a tile KOZEPPONTJABAN szamolodnak (ugyanazok a Core-
        /// fuggvenyek, mint a fix-szintu Build()-ben, ld. ComputeElevationAtPoint),
        /// FUGGETLENUL attol, hogy a level 6 (referencia) vagy annal melyebb/
        /// sekelyebb.
        /// </summary>
        private void EmitAdaptiveTile(
            TileId id,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> verticesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> normalsByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<int>> trianglesByKey,
            Dictionary<int, List<Vector3>> waterVerticesByBucket,
            Dictionary<int, List<Vector3>> waterNormalsByBucket,
            Dictionary<int, List<int>> waterTrianglesByBucket,
            List<Vector3> borderVerts, List<int> borderIndices,
            float waterSurfaceRadius)
        {
            AdaptiveTileClassification classification = GetOrComputeTileClassification(id);
            double elevation = classification.Elevation;
            bool isOceanic = classification.IsOceanic;
            Biome biome = classification.Biome;
            RenderCategory category = classification.Category;
            int bucket = classification.Bucket;
            var key = (category, bucket);

            GetOrAddLists(verticesByKey, normalsByKey, trianglesByKey, key,
                out List<Vector3> vertices, out List<Vector3> normals, out List<int> triangles);

            GetAdaptiveCorners(id, out Vector3 p00, out Vector3 p10, out Vector3 p11, out Vector3 p01);
            AddQuad(vertices, normals, triangles, p00, p10, p11, p01);

            if (isOceanic && biome == Biome.Ocean)
            {
                TileGeometry.GetContinuousBounds(id, out double uMin, out double uMax, out double vMin, out double vMax);
                Vector3 wp00 = ToWaterVector3(id.Face, uMin, vMin, waterSurfaceRadius);
                Vector3 wp10 = ToWaterVector3(id.Face, uMax, vMin, waterSurfaceRadius);
                Vector3 wp11 = ToWaterVector3(id.Face, uMax, vMax, waterSurfaceRadius);
                Vector3 wp01 = ToWaterVector3(id.Face, uMin, vMax, waterSurfaceRadius);

                double depth = _adaptiveSeaLevel - elevation;
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

        /// <summary>
        /// Egy level-6(-referencia) folyo-tile-e - a leaf a level fole (finomabb)
        /// eseten a level-referencia osere visszasetalva (a Morton-hierarchia
        /// miatt olcso bitmuvelet), level ALATTI (durvabb) leaf eseten NEM
        /// (egy durva leaf tobb referencia-tile-ot fedne le, nincs egyertelmu
        /// egyezes - dokumentalt egyszerusites, ld. a feladat osszefoglaloja).
        /// </summary>
        private bool IsAdaptiveRiverTile(TileId id)
        {
            if (id.Level < level || _adaptiveRiverTiles == null)
                return false;
            TileId current = id;
            while (current.Level > level)
                current = current.Parent();
            return _adaptiveRiverTiles.Contains(current);
        }

        /// <summary>
        /// Egy aktiv level 4 sarka - a "fine" (valodi, eltolt) pozicio a
        /// perzisztens sarok-cache-bol, geomorphing-gal (§9.2) a szulo-quad
        /// bilinearis interpolaciojabol szarmazo "coarse" pozicio fele
        /// blendelve, amig a level a base level folott van.
        /// </summary>
        private void GetAdaptiveCorners(TileId id, out Vector3 p00, out Vector3 p10, out Vector3 p11, out Vector3 p01)
        {
            id.GetUV(out uint u, out uint v);
            int lvl = id.Level;

            Vector3 fine00 = GetOrComputePersistentCorner(id.Face, lvl, u, v);
            Vector3 fine10 = GetOrComputePersistentCorner(id.Face, lvl, u + 1, v);
            Vector3 fine11 = GetOrComputePersistentCorner(id.Face, lvl, u + 1, v + 1);
            Vector3 fine01 = GetOrComputePersistentCorner(id.Face, lvl, u, v + 1);

            if (lvl <= adaptiveBaseLevel)
            {
                p00 = fine00; p10 = fine10; p11 = fine11; p01 = fine01;
                return;
            }

            double alpha = ComputeGeomorphAlpha(id);
            if (alpha >= 1.0)
            {
                p00 = fine00; p10 = fine10; p11 = fine11; p01 = fine01;
                return;
            }

            TileId parent = id.Parent();
            parent.GetUV(out uint pu, out uint pv);
            Vector3 parent00 = GetOrComputePersistentCorner(parent.Face, parent.Level, pu, pv);
            Vector3 parent10 = GetOrComputePersistentCorner(parent.Face, parent.Level, pu + 1, pv);
            Vector3 parent11 = GetOrComputePersistentCorner(parent.Face, parent.Level, pu + 1, pv + 1);
            Vector3 parent01 = GetOrComputePersistentCorner(parent.Face, parent.Level, pu, pv + 1);

            // A gyerek 4 sarka a szulo [0,1]x[0,1] lap-lokalis tereben pontosan
            // {0, 0.5, 1} relativ koordinatakra esik (a gyerek cornerU/cornerV
            // a szulo cornerU/cornerV * 2 + {0,1,2}) - ezert nincs szukseg
            // altalanos interpolaciora, csak erre a harom esetre.
            double relU0 = (u - pu * 2) / 2.0;
            double relV0 = (v - pv * 2) / 2.0;
            double relU1 = (u + 1 - pu * 2) / 2.0;
            double relV1 = (v + 1 - pv * 2) / 2.0;

            Vector3 coarse00 = BilinearOnQuad(parent00, parent10, parent11, parent01, relU0, relV0);
            Vector3 coarse10 = BilinearOnQuad(parent00, parent10, parent11, parent01, relU1, relV0);
            Vector3 coarse11 = BilinearOnQuad(parent00, parent10, parent11, parent01, relU1, relV1);
            Vector3 coarse01 = BilinearOnQuad(parent00, parent10, parent11, parent01, relU0, relV1);

            float a = (float)alpha;
            p00 = Vector3.Lerp(coarse00, fine00, a);
            p10 = Vector3.Lerp(coarse10, fine10, a);
            p11 = Vector3.Lerp(coarse11, fine11, a);
            p01 = Vector3.Lerp(coarse01, fine01, a);
        }

        private static Vector3 BilinearOnQuad(Vector3 c00, Vector3 c10, Vector3 c11, Vector3 c01, double u, double v)
        {
            Vector3 top = Vector3.Lerp(c00, c10, (float)u);
            Vector3 bottom = Vector3.Lerp(c01, c11, (float)u);
            return Vector3.Lerp(top, bottom, (float)v);
        }

        /// <summary>
        /// Geomorph-faktor (§9.2): 0 = a szulo (coarse) feluletet mutatja, ami
        /// FOLYTONOSAN illeszkedik ahhoz, amit a szulo meg aktiv leaf-kent
        /// mutatott - tehat a split PILLANATABAN (amikor a tavolsag eppen
        /// eleri a splitFactor*rParent hatart) nincs pozicio-ugras (I3/vizualis
        /// folytonossag). Ahogy a kamera tovabb kozelit, alpha 1-hez tart (a
        /// valodi, finom feluletre). A hiszterezis-savban (splitFactor..
        /// mergeFactor*rParent) a formula 0-ra vagodik - ez azt jelenti, hogy a
        /// mar felbontott, de a kameratol tavolabb kerult gyerekek egyszeruen
        /// visszamutatjak a szulo feluletet (nincs artefaktum, csak felesleges,
        /// de vizualisan a szuloevel azonos geometria).
        /// </summary>
        private double ComputeGeomorphAlpha(TileId childId)
        {
            TileId parent = childId.Parent();
            AdaptiveQuadTree.GetCenterAndBoundingRadius(parent, radius, out double pcx, out double pcy, out double pcz, out double rParent);
            double dx = _lastCutCameraCoreX - pcx, dy = _lastCutCameraCoreY - pcy, dz = _lastCutCameraCoreZ - pcz;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            double splitDistance = adaptiveSplitFactor * rParent;
            double morphRange = geomorphRangeFraction * splitDistance;
            if (morphRange <= 0.0)
                return 1.0;

            double alpha = (splitDistance - distance) / morphRange;
            return alpha < 0.0 ? 0.0 : (alpha > 1.0 ? 1.0 : alpha);
        }

        /// <summary>
        /// Perzisztens (frame-eken/ujraepiteseken at megmarado) sarok-cache -
        /// az ND-39 "C" opciobeli, per-Build() eldobott cache §9.4 szerinti
        /// LRU-va emelt valtozata. A kulcs (face, level, cornerU, cornerV)
        /// - a level EXPLICIT resze a kulcsnak (szemben a regi, egyetlen-
        /// szintu cache-szel), mert kulonbozo aktiv csomopontok kulonbozo
        /// szinten kernek sarkokat.
        /// </summary>
        private Vector3 GetOrComputePersistentCorner(int face, int lvl, uint cornerU, uint cornerV)
        {
            var key = (face, lvl, cornerU, cornerV);
            if (_persistentCornerCache.TryGetValue(key, out Vector3 cached))
            {
                TouchLru(key);
                return cached;
            }

            Vector3 p = ComputeCorner(face, lvl, cornerU, cornerV);
            _persistentCornerCache[key] = p;
            _cornerCacheLruNodes[key] = _cornerCacheLru.AddLast(key);
            return p;
        }

        /// <summary>Cache-tol fuggetlen, szalbiztos sarok-szamitas - ld. PrecomputeCornersInParallel.</summary>
        private Vector3 ComputeCorner(int face, int lvl, uint cornerU, uint cornerV)
        {
            int n = 1 << lvl;
            double uc = (double)cornerU / n * 2.0 - 1.0;
            double vc = (double)cornerV / n * 2.0 - 1.0;
            return ToDisplacedVector3(face, uc, vc, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters);
        }

        /// <summary>
        /// Az osszes, a `leaves` altal (SAJAT + geomorphing-hoz szukseges
        /// SZULO) igenyelt sarok osszegyujtese, majd a MEG NEM cache-elt
        /// sarkak tobb szalon (Parallel.For) valo elore-kiszamitasa -
        /// ugyanaz a ket-fazisu minta, mint PrecomputeClassificationsInParallel.
        /// </summary>
        private void PrecomputeCornersInParallel(TileId[] leaves)
        {
            var needed = new HashSet<(int Face, int Level, uint CornerU, uint CornerV)>();
            foreach (TileId id in leaves)
            {
                id.GetUV(out uint u, out uint v);
                int lvl = id.Level;
                AddCornerKeys(needed, id.Face, lvl, u, v);

                if (lvl > adaptiveBaseLevel)
                {
                    TileId parent = id.Parent();
                    parent.GetUV(out uint pu, out uint pv);
                    AddCornerKeys(needed, parent.Face, parent.Level, pu, pv);
                }
            }

            var missing = new List<(int Face, int Level, uint CornerU, uint CornerV)>(needed.Count);
            foreach (var key in needed)
                if (!_persistentCornerCache.ContainsKey(key))
                    missing.Add(key);
            if (missing.Count == 0)
                return;

            var results = new Vector3[missing.Count];
            System.Threading.Tasks.Parallel.For(0, missing.Count, i =>
            {
                var k = missing[i];
                results[i] = ComputeCorner(k.Face, k.Level, k.CornerU, k.CornerV);
            });

            for (int i = 0; i < missing.Count; i++)
            {
                var key = missing[i];
                _persistentCornerCache[key] = results[i];
                _cornerCacheLruNodes[key] = _cornerCacheLru.AddLast(key);
            }
        }

        private static void AddCornerKeys(HashSet<(int Face, int Level, uint CornerU, uint CornerV)> set, int face, int lvl, uint u, uint v)
        {
            set.Add((face, lvl, u, v));
            set.Add((face, lvl, u + 1, v));
            set.Add((face, lvl, u + 1, v + 1));
            set.Add((face, lvl, u, v + 1));
        }

        private void TouchLru((int Face, int Level, uint CornerU, uint CornerV) key)
        {
            if (!_cornerCacheLruNodes.TryGetValue(key, out var node))
                return;
            _cornerCacheLru.Remove(node);
            _cornerCacheLruNodes[key] = _cornerCacheLru.AddLast(key);
        }

        private void EvictCornerCacheIfNeeded()
        {
            while (_persistentCornerCache.Count > cornerCacheMaxSize && _cornerCacheLru.Count > 0)
            {
                var oldest = _cornerCacheLru.First.Value;
                _cornerCacheLru.RemoveFirst();
                _cornerCacheLruNodes.Remove(oldest);
                _persistentCornerCache.Remove(oldest);
            }
        }

        /// <summary>
        /// Egy tile TELJES (elevacio/ocean/homerseklet/biome/krater/folyo/
        /// kategoria) besorolasa - a legdragabb resze a lancnak a
        /// `Temperature.TemperatureKelvin` (napi 24 mintaveteles inszolacio-
        /// atlag, ld. temperature_ref.py mintaja), ami tile-onkent tobb tucat
        /// trigonometriai kiertekelest jelent. EGY ADOTT TileId-re ez a
        /// besorolas NEM valtozik, amig a Build() ota nem futott ujra
        /// referencia-szintu passz (a vilagot meghatarozo parameterek -
        /// seed/deepTimeMyr/craterek/seaLevel - fixek addig) - ezert
        /// PERZISZTENSEN gyorsitotarazzuk, kulonben MINDEN egyes kamera-
        /// mozgas-kivaltotta ujraepites a TELJES cut osszes tile-jara
        /// (tobb tizezerre `adaptiveBaseLevel`=6-nal) ujraszamolna ezt -
        /// pontosan ez okozta a felhasznalo altal eszlelt teljes lefagyast.
        /// A cache-t a Build() (uj referencia-passz) tortli, ld. ott.
        /// </summary>
        private readonly struct AdaptiveTileClassification
        {
            public readonly double Elevation;
            public readonly bool IsOceanic;
            public readonly Biome Biome;
            public readonly RenderCategory Category;
            public readonly int Bucket;

            public AdaptiveTileClassification(double elevation, bool isOceanic, Biome biome, RenderCategory category, int bucket)
            {
                Elevation = elevation;
                IsOceanic = isOceanic;
                Biome = biome;
                Category = category;
                Bucket = bucket;
            }
        }

        private readonly Dictionary<TileId, AdaptiveTileClassification> _tileClassificationCache = new();
        private readonly LinkedList<TileId> _tileClassificationLru = new();
        private readonly Dictionary<TileId, LinkedListNode<TileId>> _tileClassificationLruNodes = new();

        private AdaptiveTileClassification GetOrComputeTileClassification(TileId id)
        {
            if (_tileClassificationCache.TryGetValue(id, out AdaptiveTileClassification cached))
            {
                if (_tileClassificationLruNodes.TryGetValue(id, out var node))
                {
                    _tileClassificationLru.Remove(node);
                    _tileClassificationLruNodes[id] = _tileClassificationLru.AddLast(id);
                }
                return cached;
            }

            AdaptiveTileClassification data = ComputeTileClassification(id);
            _tileClassificationCache[id] = data;
            _tileClassificationLruNodes[id] = _tileClassificationLru.AddLast(id);
            return data;
        }

        /// <summary>
        /// A TENYLEGES (drága) szamitas, cache-tol FUGGETLENUL - csak a
        /// (kizarolag olvasott, Build() ota valtozatlan) `_adaptive*` mezoket
        /// es a bemeneti `id`-t hasznalja, tehat SZALBIZTOS: tobb szalrol
        /// egyszerre, kulonbozo `id`-kre biztonsagosan hivhato (ld.
        /// PrecomputeClassificationsInParallel). NEM ir a cache-be - azt a
        /// hivo vegzi, EGYSZALON (ld. ott).
        /// </summary>
        private AdaptiveTileClassification ComputeTileClassification(TileId id)
        {
            TileGeometry.ToPosition(id, out double cx, out double cy, out double cz);
            double elevation = ComputeElevationAtPoint(cx, cy, cz, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters);

            // Az oceani besorolas itt KOZVETLENUL a pontszeru elevaciobol jon
            // (elevation < referencia-szintu seaLevel), NEM a level-fuggo
            // FlowNetwork.ComputeOceanField-bol (az csak egyetlen, fix szinten
            // ertelmezett) - ez a ket forras a t=0, fix-szintu esetben
            // ugyanazt adja (mindketto ugyanabbol az elevation-bol es
            // seaLevel-bol szarmazik), csak itt pontszeruen, tetszoleges
            // level-re altalanositva.
            bool isOceanic = elevation < _adaptiveSeaLevel;

            double temperatureK = Temperature.TemperatureKelvin(
                cx, cy, cz, climateDayT, climateOrbitalPeriodDays, climateRotationPeriodDays,
                _adaptiveAxialTiltRad, isOceanic, elevation, _adaptiveSeaLevel);
            Biome biome = BiomeClassification.Classify(temperatureK, isOceanic);

            bool isCratered = _adaptiveCraters.Count > 0 && ImpactCratering.IsInsideAnyCrater(cx, cy, cz, _adaptiveCraters);
            bool isRiver = showRivers && IsAdaptiveRiverTile(id);
            RenderCategory category = isCratered ? RenderCategory.Crater
                : isRiver ? RenderCategory.River
                : ToRenderCategory(biome);

            int bucket = category == RenderCategory.Ocean ? OceanRockBucket(elevation) : 0;

            return new AdaptiveTileClassification(elevation, isOceanic, biome, category, bucket);
        }

        /// <summary>
        /// A `leaves` altal igenyelt, MEG NEM cache-elt klasszifikaciok
        /// tobb szalon (`Parallel.For`) valo elore-kiszamitasa, majd
        /// EGYSZALU beirasa a cache-be. A WorldGen.Core lanc (DomainWarp,
        /// PlateGeneration, PlateBoundaryEffect, ImpactCratering,
        /// Temperature, BiomeClassification) igazoltan tiszta fuggvenyekbol
        /// all (nincs megosztott mutable allapot, nincs hivasonkenti heap-
        /// allokacio) - ld. a GPU-CALC teljesitmeny-vizsgalat jegyzokonyvet -
        /// ezert `Parallel.For`-ral biztonsagosan parhuzamosithato.
        /// </summary>
        private void PrecomputeClassificationsInParallel(TileId[] leaves)
        {
            var missing = new List<TileId>(leaves.Length);
            foreach (TileId id in leaves)
                if (!_tileClassificationCache.ContainsKey(id))
                    missing.Add(id);
            if (missing.Count == 0)
                return;

            var results = new AdaptiveTileClassification[missing.Count];
            System.Threading.Tasks.Parallel.For(0, missing.Count, i =>
            {
                results[i] = ComputeTileClassification(missing[i]);
            });

            for (int i = 0; i < missing.Count; i++)
            {
                TileId id = missing[i];
                _tileClassificationCache[id] = results[i];
                _tileClassificationLruNodes[id] = _tileClassificationLru.AddLast(id);
            }
        }

        private void EvictTileClassificationCacheIfNeeded()
        {
            while (_tileClassificationCache.Count > tileClassificationCacheMaxSize && _tileClassificationLru.Count > 0)
            {
                TileId oldest = _tileClassificationLru.First.Value;
                _tileClassificationLru.RemoveFirst();
                _tileClassificationLruNodes.Remove(oldest);
                _tileClassificationCache.Remove(oldest);
            }
        }

        /// <summary>
        /// A vilagot meghatarozo referencia-allapot (seed/deepTimeMyr/craterek/
        /// seaLevel) megvaltozasakor (uj Build()) a per-tile cache-ek (sarok +
        /// klasszifikacio) ERVENYTELENEK - kulonben regi, mar nem ervenyes
        /// eleváció/hőmérséklet ertekeket adnanak vissza uj vilagallapotra.
        /// </summary>
        private void InvalidateAdaptiveCaches()
        {
            _persistentCornerCache.Clear();
            _cornerCacheLru.Clear();
            _cornerCacheLruNodes.Clear();
            _tileClassificationCache.Clear();
            _tileClassificationLru.Clear();
            _tileClassificationLruNodes.Clear();
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

        /// <summary>
        /// ND-39 "C" opcio (sarok-deduplikacio): egy (face, cornerU, cornerV)
        /// EGESZ lap-lokalis racspontot (ld. Build() cornerCache-doc) CSAK
        /// EGYSZER szamol ki - a masodik/harmadik/negyedik hivas (a
        /// szomszedos tile-oktol) a cache-bol olvas. A cornerU/cornerV a
        /// TileGeometry.GetContinuousBounds ugyanazon kepletevel
        /// (u/n*2-1) alakul folytonos (uc,vc)-va, mint korabban a
        /// kozvetlen ToDisplacedVector3(face, uMin/uMax, vMin/vMax, ...)
        /// hivas hasznalta - tehat BITRE AZONOS bemenetet ad at a tiszta
        /// ToDisplacedVector3-nak, csak ritkabban hivva.
        /// </summary>
        private Vector3 GetOrComputeCorner(
            Dictionary<(int Face, uint CornerU, uint CornerV), Vector3> cache,
            int face, uint cornerU, uint cornerV, int n,
            ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters)
        {
            var key = (face, cornerU, cornerV);
            if (cache.TryGetValue(key, out Vector3 cached))
                return cached;

            double uc = (double)cornerU / n * 2.0 - 1.0;
            double vc = (double)cornerV / n * 2.0 - 1.0;
            Vector3 p = ToDisplacedVector3(face, uc, vc, seed, seeds, craters);
            cache[key] = p;
            return p;
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
            double elevation = ComputeElevationAtPoint(x, y, z, seed, seeds, craters);
            return radius + (float)(elevation * elevationScale);
        }

        /// <summary>
        /// A nyers eleváció (méter) egy tetszőleges ponton - a
        /// <see cref="ComputeDisplacedRadius"/>-ból kiemelve (M9), hogy az
        /// adaptív renderer (EmitAdaptiveTile) is felhasználhassa a
        /// tile-KÖZÉPPONT ocean/biome/hőmérséklet besorolásához, ugyanazzal a
        /// számítási lánccal, amit a sarok-alapú megjelenítés is használ.
        ///
        /// Ugyanaz a szamitas, amit a sea-level kalibraciohoz is hasznaltunk
        /// (AssignPlate + ElevationWithBoundary), csak most egy tetszoleges
        /// pontban kiertekelve. Tiszta fuggveny -&gt; ket szomszedos tile, ami
        /// ugyanazt a fizikai sarkot osztja, bitre ugyanezt az erteket
        /// szamolja ki, tehat a felszin varrat nelkul osszeer.
        ///
        /// MEGJEGYZES: a tileIdValue parametert (a CrustElevation-beli
        /// finom "jitter" zajhoz) itt fix 0-val hivjuk, mert egy
        /// sarokpontnak/kraternek nincs egyetlen "sajat" tile-ja. Ez azt
        /// jelenti, hogy ez a szamitas a finom jittert NEM kapja meg
        /// (csak a plate-szintu bazis + hatarhatas latszik) - a
        /// hivatalos, TEST-EARTH-001-hez hasznalt tile-kozepu
        /// elevation-t (amiben BENNE van a jitter) ez nem erinti, csak a
        /// vizualis corner-interpolacio es a marker-magassag egyszerusodik.
        /// ND-36 (domain warping): a lemez-hozzarendeles a WARPOLT
        /// poziciot kapja - UGYANAZ a szabaly, mint amit a Core-oldali
        /// SeaLevelCalibration/PlateBoundaryEffect hasznal, kulonben ez
        /// a sarok-alapu megjelenites inkonzisztens (nem-warpolt)
        /// lemezhatarokat mutatna a tile-kozepu adatokhoz kepest.
        ///
        /// ND-39 "C" opcio (warp-hoisting): a warp CSAK EGYSZER fut le
        /// pontonkent - az AssignPlate ES a BoundaryUplift (az uj
        /// ElevationWithBoundaryFromWarped-en keresztul) UGYANAZT a mar
        /// kiszamitott (wx,wy,wz)-t hasznalja, ahelyett hogy a
        /// BoundaryUplift sajat maga ujraszamolna a WarpPosition-t
        /// ugyanarra a pontra (ld. src/WorldGen.Core/Tectonics/
        /// PlateBoundaryEffect.cs, docs/04-decisions.md ND-39).
        /// </summary>
        private static double ComputeElevationAtPoint(
            double x, double y, double z, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters)
        {
            DomainWarp.WarpPosition(seed, x, y, z, out double wx, out double wy, out double wz);
            int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
            double elevation = PlateBoundaryEffect.ElevationWithBoundaryFromWarped(
                seed, plateId, tileIdValue: 0UL, x, y, z, wx, wy, wz, seeds, out _);

            // M11: a pont SAJAT pozicioja alapjan szamolt becsapodas-korrekcio -
            // ugyanaz a "tiszta fuggveny a pozicioban, nem a tile-ban" elv,
            // ami a lemez-elevaciot is varratmentesse teszi ket szomszedos
            // tile kozott.
            if (craters.Count > 0)
                elevation += ImpactCratering.ElevationDelta(x, y, z, craters);

            return elevation;
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
