using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using WorldGen.Core;
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
        // ND-126: a ket homersekleti osztaly (Temperate/Tropical) helyere ot,
        // csapadek szerint is megkulonboztetett biome lepett.
        Ocean, SeaIce, IceSheet, Tundra,
        Desert, Grassland, TemperateForest, Savanna, Rainforest,
        River, Crater, Lake,
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
    /// A `radius` tetszőleges Unity-egység, a fizikai sugár 7420 km. Az ND-88
    /// fizikai relief-módjában az elevation skálája ebből a két sugárból
    /// származik, ezért a vízszintes ND-84 km-lépték és a függőleges relief
    /// azonos fizikai arányt használ. A régi művészi túlrajzolás kapcsolható
    /// tartalék marad.
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
    public partial class PlanetGridMesh : MonoBehaviour
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

        /// <summary>A worldSeed elojel nelkuli formaja - a StarField (csillagos hatter) hasznalja, mert a determinisztikus katalogus a MAR meglevo DeterministicRandom API-t hivja, ami ulong seedet var.</summary>
        public ulong WorldSeedUnsigned => unchecked((ulong)worldSeed);

        [SerializeField]
        [Tooltip("A referencia (TEST-EARTH-001) 20 lemezzel adott 2 kontinenst " +
                 "65% víz mellett a kanonikus worldSeed-nél - ld. docs/04-decisions.md.")]
        private int plateCount = 20;

        [SerializeField]
        [Tooltip("Ha igaz, a viewer a függőleges domborzatot is fizikai 1:1 arányban " +
                 "rajzolja az ND-84 km-léptékhez: elevationScale = radius / 7 420 000 m, " +
                 "terrainReliefExaggeration = 1. A lemezperem így nem tűnhet több száz " +
                 "kilométer magasnak pusztán megjelenítési túlrajzolás miatt. Kikapcsolva " +
                 "az alábbi két művészi skálaparaméter ismét szabadon használható.")]
        private bool usePhysicalReliefScale = true;

        [SerializeField]
        [Tooltip("Tartalék művészi Unity-egység/méter skála; csak kikapcsolt " +
                 "usePhysicalReliefScale mellett érvényes.")]
        private double elevationScale = 100.0 / PlanetConstants.RadiusMeters;

        [SerializeField]
        [Tooltip("MEGJELENITESI fuggoleges tulrajzolas (VERTICAL EXAGGERATION) - a " +
                 "domborzati relief (fraktal zaj + tektonikai lepesek) a TENGERSZINTRE " +
                 "PIVOTALVA ennyiszeresere nagyitva jelenik meg. FONTOS: ez CSAK a " +
                 "megjelenitest skalazza, a VILAGMODELLT (kontinensek, partvonal, " +
                 "folyok, tengerszint) NEM valtoztatja - ezert a partvonalak es a " +
                 "kontinensek alakja valtozatlan marad, csak a hegyek magasabbak es " +
                 "az oceanarok melyebbek lesznek. Fizikai relief-módban mindig 1. " +
                 "(A world-modell zaj-amplitudo " +
                 "novelese ezzel szemben SZETZUZNA a kontinenseket - ld. ND-33/34, " +
                 "7x-nel a TEST-EARTH-001 44 kontinensrol 2-re esett.) 1 = nincs " +
                 "tulrajzolas (a korabbi viselkedes); 1.5 = enyhen markansabb " +
                 "domborzat (felhasznaloi keres, 2026-09-03); 5-10 = eros.")]
        private double terrainReliefExaggeration = 1.0;

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

        [SerializeField]
        [Range(0f, 1000f)]
        [Tooltip("Deep-time CSÚSZKA (Myr, 0..1000 = 0..1 Gyr) - húzható vezérlő az " +
                 "időhöz. A számítás a double deepTimeMyr-t használja (azt a Unity " +
                 "[Range] nem tudja csúszkaként mutatni), ez a float proxy csak a " +
                 "húzáshoz van, kétirányban szinkronban a fenti mezővel. Play közben " +
                 "húzva végigscrubbolható a lemezmozgás + erózió + becsapódások.")]
        private float deepTimeSliderMyr = 0f;

        [SerializeField]
        [Tooltip("Deep-time erózió (DeepTimeErosionGlaciation, ND-44): a lemezhatár-" +
                 "hegységek uplift-bónusza az idővel (deepTimeMyr) a maradvány-értékére " +
                 "(EquilibriumFraction, ~35%) relaxál - a hegyek lekopnak, tau≈50 Myr " +
                 "időállandóval. Az alap-elevációt nem érinti. t=0-nál nincs hatása.")]
        private bool showDeepTimeErosion = true;

        [SerializeField]
        [Tooltip("Képernyőn (Game view) megjelenő, húzható deep-time csúszka + " +
                 "gyorskapcsolók (IMGUI/OnGUI, nem kell hozzá Canvas/prefab). " +
                 "A bal felső sarokban jelenik meg Play közben.")]
        private bool showOnScreenControls = true;

        [SerializeField]
        [Tooltip("A képernyős deep-time csúszka felső vége (Myr).")]
        private float onScreenDeepTimeMaxMyr = 1000f;

        [Header("M13: Felszín-világítás (folytonos felszín)")]
        [SerializeField]
        [Tooltip("A folytonos felszín (VertexColorUnlit shader) VALÓS IDEJŰ világításához " +
                 "használt Nap (Directional Light). Ha üres, a jelenet fő Directional " +
                 "Lightját keresi meg (RenderSettings.sun, vagy az első Directional Light).")]
        private Light surfaceSunLight;
        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Ambiens padló - az éjszakai (Nap felé nem néző) oldal se legyen teljesen " +
                 "fekete (halvány csillag-/légköri fény szimulációja). FELHASZNÁLÓI " +
                 "VISSZAJELZÉS (2026-09-06): a korábbi 0.35 alapérték miatt az éjszakai oldal " +
                 "\"homályosnak/ködösnek\" tűnt (a felszín mindig legalább 35%-ig kivilágítva " +
                 "maradt, elmosva a domborzat kontrasztját), NEM ténylegesen sötétnek - " +
                 "0.04-re csökkentve, hogy az éjszakai oldal valóban sötét legyen, csak épp ne " +
                 "essen tiszta feketébe.")]
        private float surfaceAmbient = 0.04f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("A FELHŐ-anyag SAJÁT ambiens-értéke - KÜLÖN mezőként, mert a `surfaceAmbient` " +
                 "mezőt korábban (code-review-ban feltárt hiba, 2026-09-06) VÉLETLENÜL a felhő-" +
                 "anyagra is ráírtuk (`UpdateSurfaceLightingUniforms`) - amikor a felszín " +
                 "éjszakai sötétítéséhez `surfaceAmbient`-et 0.35-ről 0.04-re csökkentettük, ez " +
                 "ÉSZREVÉTLENÜL a felhőket is majdnem feketére sötétítette éjszaka, felülírva a " +
                 "`CloudUnlit.shader` saját, külön kalibrált 0.55-ös alapértékét. Most a felhő " +
                 "a SAJÁT mezőjét kapja, a felszín-sötétítés nem hat rá.")]
        private float cloudAmbient = 0.55f;

        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("Spekuláris erősség a folytonos felszínen (víz-csillanás, nedves szikla). " +
                 "TÖRTÉNET (2026-09-06/07): a felhasználó szerint a jég/folyó \"brutálisan " +
                 "csillogott\" - diagnosztikai céllal 0-ra állítva, DE a csillogás VÁLTOZATLAN " +
                 "maradt (screenshot igazolta) - ez bebizonyította, hogy a valódi ok NEM ez az " +
                 "anyag, hanem a HDRP Bloom post-processing túl alacsony küszöbe " +
                 "(DefaultSettingsVolumeProfile.asset, threshold 0->1.05, ld. docs/04-decisions.md). " +
                 "A Bloom-javítás után visszaállítva a korábbi, ésszerű kalibrációra (0.12).")]
        private float surfaceSpecularStrength = 0.12f;
        [SerializeField]
        [Range(1f, 128f)]
        [Tooltip("Alacsonyabb érték = SZÉLESEBB, lágyabb csillanás-folt. FELHASZNALOI " +
                 "VISSZAJELZES (2026-09-06): a csillanás tile-ról tile-ra \"pattog\" - " +
                 "24-ről 8-ra csökkentve. (A csillanás valódi oka végül a HDRP Bloom " +
                 "túl alacsony küszöbe volt, nem ez az érték - ld. surfaceSpecularStrength doksija.)")]
        private float surfaceShininess = 8f;

        [Header("M7: Folyóhálózat")]
        [SerializeField]
        private bool showRivers = true;

        [SerializeField]
        [Tooltip("A szárazföld ekkora hányada (0..1) legyen folyó-tile - " +
                 "ugyanaz a percentilis-módszer, mint a tengerszint-kalibrációnál.")]
        private double riverTargetFraction = 0.03;

        [SerializeField]
        [Tooltip("A folyó-vonal FOLYTONOS nyomvonal-követésének lépésköze méterben (ND-49) " +
                 "- a durva (~13 km-es) tile-középpontok helyett a felszín érintő-síkjában " +
                 "futó lejtő-követéssel a FORRÁSTÓL a TERMÉSZETES végállapotig (óceán/pit) " +
                 "rajzolja ki a nyomvonalat, mesterséges lépésszám-vágás nélkül. HÁTTÉR-SZÁLON " +
                 "fut (nem blokkolja a Build()-et) - mérve: 12 referencia-folyóra összesen " +
                 "kb. 6-10s 50m-es lépésköznél. Kisebb érték = finomabb, de lassabb.")]
        private double riverRefinementStepMeters = 50.0;

        [SerializeField]
        [Tooltip("A folyó-VONALAK sugár-irányú kiemelése a felszín fölé (z-fighting ellen). " +
                 "A folyók vékony vonal-hálózatként (a tényleges lefolyás-fa mentén) " +
                 "renderelődnek, nem tile-kitöltésként (BuildRiverNetwork). " +
                 "FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06): \"a folyók továbbra is " +
                 "szaggatottak... kirajzolási probléma\" - GYÖKÉROK: a folyó-vonal minden " +
                 "pontja a FOLYTONOS (finom, ~50m lépésközű) elevációmezőt olvassa ki " +
                 "(RiverPositionOnSurface -> ComputeDisplacedRadius), de a ténylegesen " +
                 "RENDERELT terep egy DARABOSAN LINEÁRIS háromszög-mesh, aminek csúcsai a " +
                 "sokkal DURVÁBB adaptív LOD-tile-határokon vannak. A domborzat-zaj " +
                 "amplitúdója (CrustElevation.NoiseAmplitudeMeters = 3000 m) sok " +
                 "nagyságrenddel meghaladja a korábbi 0.02 (elevationScale=0.01 mellett " +
                 "mindössze ~2 méteres) sugár-eltolást - a két felület (folytonos vs. " +
                 "darabos-lineáris) eltérése tipikus terepen simán meghaladhatja ezt, ezért " +
                 "a folyó-vonal helyenként a terep-mesh ALÁ süllyed (Z-fighting/takarás), " +
                 "ami szaggatottnak LÁTSZIK, pedig a nyomvonal-ADAT folytonos. 0.5-re " +
                 "emelve (~50 m ekvivalens) - ez csak enyhíti (a legdurvább LOD-szinteken " +
                 "továbbra is előfordulhat), a teljes megoldás a folyó-pont terep-mesh " +
                 "TÉNYLEGES lokális magasságára vetítését igényelné, nem csak a folytonos " +
                 "mezőre.")]
        private float riverLineRadialBias = 0.5f;

        [SerializeField]
        [Tooltip("A folyó-SZALAG (nem vékony vonal, hanem a vízhozammal arányosan " +
                 "szélesedő mesh-szalag) fél-szélessége a LEGKISEBB (egyetlen forrás-ágú, " +
                 "vízhozam-súly=1) folyónál, Unity-egységben. Egy nagyobb folyó (több " +
                 "beleolvadó mellékfolyóval) ennek sqrt(vízhozam-súly)-szorosát kapja - ld. " +
                 "RiverPathTracing.ComputeDischargeWeights doksi (a súly a TÉNYLEGES " +
                 "dendritikus összefolyás-fából jön, nem dekoratív becslés). " +
                 "FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06): \"a folyó-vonal szélessége nem " +
                 "korrelál azzal, mennyi vizet szállít\" - korábban MINDEN folyó azonos " +
                 "vékony vonal volt (MeshTopology.Lines), most mesh-szalag, torkolat felé " +
                 "szélesedő.")]
        private float riverBaseHalfWidth = 0.08f;

        [Header("M7: Tavak + jég")]
        [SerializeField]
        [Tooltip("Beltavak (topográfiai zárt medencék, a priority-flood 'filled' " +
                 "mezőjéből, LakesIceErosion.IdentifyLakes) és az állandó jégtakaró " +
                 "(éves hőmérséklet-statisztikából, LakesIceErosion.ClassifyIce) " +
                 "megjelenítése a referencia-szinten. A tó kék, a jég fehér.")]
        private bool showLakesIce = true;

        [SerializeField]
        [Tooltip("Csak ennyi (vagy több) összefüggő tile-ból álló tavakat jelenítünk meg - " +
                 "kiszűri a sok apró, blokkos helyi mélyedést (zaj). Nagyobb = kevesebb, nagyobb tó.")]
        private int minLakeTiles = 6;

        [SerializeField]
        [Tooltip("Csak ennyi méternél mélyebb tavak (a legmélyebb pont a feltöltési szint alatt).")]
        private double minLakeDepthMeters = 40.0;

        [SerializeField]
        [Range(4, 8)]
        [Tooltip("A FOLYÓK és TAVAK dedikált (finomabb) számítási szintje - a durva " +
                 "megjelenítési 'Level'-től FÜGGETLENÜL, mert a folyó/tó külön réteg. " +
                 "Magasabb = kevésbé blokkos vízrajz, de lassabb Build (a mező+áramlás " +
                 "ezen a szinten számolódik). 7-8 ajánlott. Level alá nem csökken.")]
        private int hydrologyLevel = 7;

        [Header("M5: Szél-sebesség overlay")]
        [SerializeField]
        [Tooltip("Ha be van kapcsolva, a teljes felszínt (szárazföld + óceán + " +
                 "vízfelszín) a per-tile SZÉLSEBESSÉG szerint színezi (WindPrecipitation." +
                 "WindVector nagysága), a biome/óceán-szín helyett. Diagnosztikus overlay - " +
                 "a világmodell nem változik. Kék=szélcsend ... piros=viharos.")]
        private bool windSpeedOverlay = false;

        [SerializeField]
        [Tooltip("A szín-rámpa felső vége (m/s): ekkora (vagy nagyobb) szélsebességnél " +
                 "teljesen piros. A tipikus zonális alap ~10 m/s (BaseWindSpeed), a termikus " +
                 "szél ezt tovább növeli, ezért 15 egy jó kezdőérték.")]
        private double windSpeedColorMaxMs = 15.0;

        [Header("M5: Csapadék overlay")]
        [SerializeField]
        [Tooltip("Ha be van kapcsolva, a felszínt a per-tile CSAPADÉK szerint színezi " +
                 "(MoisturePrecipitation nedvesség-advekció: óceán-forrás → szél menti " +
                 "transzport → orografikus lecsapódás), a referencia-szinten. Aridtól " +
                 "(homok/barna) a csapadékosig (zöld → türkiz). A világmodell nem változik.")]
        private bool precipitationOverlay = false;

        [SerializeField]
        [Tooltip("A csapadék-szín-rámpa felső vége (a modell dimenziómentes egységében). " +
                 "Az óceáni átlag ~5, a szárazföldi ~1, ezért 4-6 jó kezdőérték.")]
        private double precipitationColorMax = 5.0;

        [Header("M6: Felhő-réteg (MVP)")]
        [SerializeField]
        [Tooltip("Egy második, átlátszó gömbhéj a felszín fölött - a felhő-sűrűséget a MÁR " +
                 "meglévő csapadék-mezőből (MoisturePrecipitation) vezeti le (I3: nincs kézzel " +
                 "festett felhőtextúra) - ahol sok a csapadék, ott sűrűbb/átlátszatlanabb a " +
                 "felhő. Referencia-szintű, statikus (mint a víz/határ-réteg) - a kamera-mozgás " +
                 "nem érinti. NEM a spec §13 teljes volumetrikus felhő-modellje (ND-21 még " +
                 "nyitott) - ez egy egyszerű, gyors MVP-közelítés, vizuális kalibrálást igényel.")]
        private bool showClouds = false;

        [SerializeField]
        [Tooltip("A felhőréteg magassága a felszín fölött, méterben - MEGSZORONVA az " +
                 "elevationScale-lel, hogy a domborzat vizuális túlrajzolásával konzisztens " +
                 "magasságban tűnjön fel (ne süllyedjen bele a túlzott hegyekbe). Kétszer " +
                 "csökkentve (8000→3000→1500) felhasználói visszajelzések alapján - a régi " +
                 "érték + a kiegyensúlyozatlan lefedettség együtt \"köd\"-szerű, nem " +
                 "felhő-szerű hatást keltett.")]
        private double cloudAltitudeMeters = 1500.0;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("A felhőréteg maximális átlátszatlansága (alfa) a LEGCSAPADÉKOSABB " +
                 "területek fölött, a küszöb+gamma-görbítés UTÁN.")]
        private float cloudMaxOpacity = 0.65f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("PERCENTILIS-küszöb (0..1) a felhő-sűrűséghez, KÜLÖN számolva az óceáni " +
                 "és a szárazföldi csapadék-eloszláson (mint a folyó-forrás kiválasztásnál, " +
                 "ND-49) - EZ ALATT a felhő teljesen láthatatlan. FONTOS, MÉRÉSSEL FELTÁRT " +
                 "OK: a nyers MoisturePrecipitation-skála az óceán fölött kb. 4×-e a " +
                 "szárazföldinek (mért átlag: óceán 4.67, szárazföld 1.27, egy valós " +
                 "világon) - egy KÖZÖS, abszolút küszöb ezért a felhasználói visszajelzés " +
                 "szerint \"az óceánokat teljesen befedte, a kontinenseket elkerülte\". A " +
                 "domain-relatív percentilis-küszöb mindkét oldalon KIEGYENSÚLYOZOTT " +
                 "arányban (pontosan (1-threshold)*100%) enged át felhőt.")]
        private double cloudDensityThreshold = 0.45;

        [SerializeField]
        [Tooltip("A küszöb fölötti relatív sűrűséget ERRE a kitevőre emeljük, mielőtt " +
                 "az alfát számolnánk - > 1 esetén csak a VALÓBAN csapadékos csúcsok " +
                 "adnak sűrű felhőt, a küszöb közeli, gyengén csapadékos területek " +
                 "erősen halványak maradnak (nem lineáris átmenet).")]
        private double cloudDensityGamma = 1.3;

        [SerializeField]
        [Tooltip("FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06): \"olyan mintha nem mozogna\" - a " +
                 "felhőréteg korábban TELJESEN STATIKUS volt, csak Build()-kor frissült. " +
                 "Bekapcsolva a réteg periodikusan (ld. cloudDriftRebuildIntervalSeconds) " +
                 "újraépül, a MÁR MEGLÉVŐ WindPrecipitation.WeatherPrecipitationMultiplier " +
                 "időfüggő zajával megszorozva a klimatológiai csapadék-átlagot minden " +
                 "sarokban - I3-kompatibilis (a mozgás forrása egy valódi, a worldSeedből " +
                 "generált idő-koherens mező, nem dekoratív UV-csúsztatás).")]
        private bool cloudDriftEnabled = false;

        /// <summary>
        /// Melyik kameramód aktív - a `SunController` (és később a pálya
        /// menti mód `PlanetOrbitCamera`-kiegészítése) ezt olvassa, hogy
        /// eldöntse, hogyan forgassa a bolygót/fényt/csillagmezőt. `Free`:
        /// jelenlegi viselkedés (a Planet mindig identitáson marad, a fény
        /// forog a test-keretben). `AxialRotation`: a Planet TÉNYLEGESEN
        /// forog (a Nap/csillagok fixek) - ld. SunController.ApplySunDirection.
        /// `OrbitalFollow`: MÉG NEM implementálva, egyelőre a Free-vel
        /// megegyező viselkedést kap.
        /// </summary>
        public enum CameraViewMode { Free, AxialRotation, OrbitalFollow }

        [Header("Kamera-mód (felhasználói kérés, 2026-09-10)")]
        [SerializeField]
        [Tooltip("Szabad kamera: a jelenlegi viselkedés (a bolygó mesh sosem forog, " +
                 "csak a fény/nap/csillagok). Tengelyforgás: a bolygó TÉNYLEGESEN forog, " +
                 "a Nap/csillagok fixek - a tengelyforgás vizuálisan láthatóvá válik. " +
                 "Pálya mentén: MÉG NEM implementálva (egyelőre Szabad kamera-ként viselkedik).")]
        private CameraViewMode cameraViewMode = CameraViewMode.Free;

        public CameraViewMode CurrentCameraViewMode => cameraViewMode;

        [SerializeField]
        [Tooltip("A felhő-sodródási 'idő' (a WeatherPrecipitationMultiplier `t` paramétere) " +
                 "ennyivel nő másodpercenként - a WindPrecipitation.WeatherTimeSpeed " +
                 "konstanssal együtt határozza meg a tényleges vizuális sodródás sebességét. " +
                 "KALIBRÁLATLAN - élő Unity-ellenőrzés hátra.")]
        private float cloudDriftTimeScale = 3.0f;

        [SerializeField]
        [Tooltip("Milyen gyakran épül újra a felhő-réteg a sodródás animálásához (másodperc) - " +
                 "a teljes felhő-rácson újraszámolt DomainWarp+Fbm zaj nem olcsó, ezért ez KÜLÖN, " +
                 "a kamera-vezérelt LOD-frissítéstől független fékezés.")]
        private float cloudDriftRebuildIntervalSeconds = 1.5f;

        // Fut(hat) 0-rol indulva, minden Update()-ben elorehalad (a
        // cloudDriftTimeScale-lel skalazva) - CSAK a felho-reteg
        // idofuggo suruseg-mintazatat vezerli, a vilag DETERMINISZTIKUS
        // allapotat NEM erinti (tisztan render-oldali animacio, mint a
        // kamera pozicioja is).
        private double _cloudDriftTime;
        private float _lastCloudDriftRebuildRealtime = float.NegativeInfinity;

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

        [SerializeField, Range(0, 10)]
        [Tooltip("A kvadfa gyoker-szintje - EZ A MINDIG GARANTALT, zoomolas " +
                 "nelkul is lathato minimum-reszletesseg (a finomodas ezen " +
                 "FELUL, a kamera latokupjaban tortenik). INCREMENTAL-MESH-" +
                 "BUFFERS OTA: ez a szint EGYSZER epul fel (BuildStaticBaseLayer), " +
                 "es a kamera-mozgas TOBBET NEM erinti - ezert 8 (393216 tile) " +
                 "is biztonsagosan hasznalhato alapertek, nem csak a korabbi, " +
                 "'minden ujraepitesnel ujraszamolodik' architekturahoz " +
                 "igazitott kisebb ertek (4).")]
        private int adaptiveBaseLevel = 8;

        [SerializeField, Range(0, 20)]
        [Tooltip("A kvadfa max melysege (nem bomlik finomabbra ennel). " +
                 "SCREEN-SPACE-LOD OTA: mivel a felbontasi kuszob most a " +
                 "kamera TENYLEGES FOV/felbontasabol szamolt vetitett " +
                 "pixel-meretbol jon (ld. targetTilePixelSize), nem egy fix " +
                 "tavolsag/meret-aranybol, ez a szint tenylegesen elerheto a " +
                 "kamera minDistance-enel is - a regi metrikaval (adaptiveSplitFactor) " +
                 "ez matematikailag NEM volt igaz (a mely szintek gyakorlatilag " +
                 "sosem aktivalodtak).")]
        private int adaptiveMaxLevel = 20;

        [SerializeField]
        [Tooltip("A vetített tile-metrika pixelátmérő-célja, FOV/felbontás alapján. " +
                 "Kisebb érték több tile-t és korábbi finomodást jelent, nagyobb számítási költséggel. " +
                 "Nem garantált maximum az eltolt terepen vagy telített budgetnél. " +
                 "ND-73: az első base-felosztás külön, korábbi célt kaphat.")]
        private double targetTilePixelSize = 8.0;

        [SerializeField]
        [Tooltip("ND-73: csak a statikus alap ELSŐ felosztásának pixelcélja. " +
                 "A mélyebb szintek targetTilePixelSize értéke nem változik. " +
                 "A merge-küszöb és a normál split közé kell esnie; különben nincs előrehozás.")]
        private double initialRefinementPixelSize = 7.0;

        [SerializeField]
        [Tooltip("ND-74: a kész statikus terepmintákból becsült felszíntávolság vezérli a CPU LOD-ot és morphot. " +
                 "Több közeli részletet, de több mesh-munkát is jelenthet. Nem szigorú pixelhibakorlát. " +
                 "Kikapcsolva az előző gömbös metrika; base > 8 és GPU-geometria esetén szintén gömbös fallback.")]
        private bool useTerrainLodProxy = true;

        [SerializeField, Range(0, 8)]
        [Tooltip("FAZIS 1 (ND-47): a kvadfa-bejaras GYOKERSZINTJE - LEVALASZTVA " +
                 "a statikus base-tol (adaptiveBaseLevel). A bejaras errol az " +
                 "alacsony szintrol indul, es CSAK oda ereszkedik le, ahol a base " +
                 "FOLE kell finomitani; a sík/tavoli reszen a rekurzio azonnal " +
                 "leall. Igy a per-frame koltseg a LATHATO, finomitando regio " +
                 "meretevel aranyos, NEM a base-szintu csempek szamaval (6*4^8 = " +
                 "393216) - ez szuntette meg a maxLeafCount-korlat idő-előtti " +
                 "bevagasat es az 'esetleges', foltszeru finomodast. Kicsi ertek " +
                 "(2-3) ajanlott: eleg gyoker a jo parhuzamos terhelesElosztashoz, " +
                 "de elhanyagolhato fix koltseg. Ne legyen nagyobb az " +
                 "adaptiveBaseLevel-nel.")]
        private int adaptiveTraversalRootLevel = 3;

        [SerializeField]
        [Tooltip("FAZIS 2 (ND-47): a dinamikus (level>base) csempek MAXIMALIS " +
                 "szama egy ujraepitesnel - PRIORITASOS koltsegvetes. A " +
                 "kivalasztas a legnagyobb kepernyo-hibaju (kamerahoz legkozelebbi, " +
                 "kozponti) csempeket finomitja ELOSZOR, es ennyi level>base " +
                 "csempenel megall; a maradek (perifériás) resz durvabb marad (a " +
                 "statikus base fedi, lyuk nincs). Ez hatarolja a RENDER-koltseget " +
                 "(a RebuildAdaptiveMesh nagyjabol ennyi csempet epit ujra). Kisebb " +
                 "ertek = gyorsabb, de kevesebb finom reszlet a periferian; nagyobb " +
                 "= reszletesebb, de lassabb ujraepites. MEGJEGYZES: a valoban SIMA " +
                 "(60fps) mukodeshez a Fazis 3 (inkrementalis/aszinkron mesh) kell - " +
                 "ez a koltsegvetes csak a hitch NAGYSAGAT csokkenti, nem szunteti meg. " +
                 "2026-09-18: 200000 -> 8000. A korabbi 200000 GYAKORLATILAG SOSEM " +
                 "lepett eletbe: a PerfLog_20260913_184215-ben a cut 52-rol 32795 " +
                 "dinamikus levelre nott egy munkamenet alatt (hiszterezis: a mar " +
                 "finomitott regio 'ragadosan' finom marad), es a keres-ido ezzel " +
                 "egyutt 100-350ms-rol 1300-1900ms-ra - ez a felhasznalo altal " +
                 "jelzett, munkamenet kozben ROSSZABBODO rotacio/zoom szaggatas. A " +
                 "mert (levelszam, ido) parokbol linearisan extrapolalva ~8000 level " +
                 "a ~400-500ms-os keres-idonek felel meg. EZ EGY KIINDULO ERTEK: a " +
                 "mezo Play kozben is allithato (OnValidate -> _adaptiveConfigDirty " +
                 "-> chunk-cache urites, azonnal hat), tehat elo munkamenetben " +
                 "sweepelheto a simasag/reszletesseg kompromisszum.\n\n" +
                 "2026-09-19: A FENTI LINEARIS EXTRAPOLACIO TEVES VOLT. Meres " +
                 "szerint a bejaras koltseget a LATOTT FELULET hajtja, nem a " +
                 "budget: 3x budget csak +28% worker-ido (155 -> 199 ms), mert a " +
                 "vagas ugyis ~200 000 tile-t latogat meg 8000 levelert. A fix " +
                 "8000 viszont 1,4-4,1x-re hizlalta a rajzolt tile-okat a 8 px-es " +
                 "celhoz kepest. EZERT: 0 = AUTOMATIKUS, a viewportbol szamolva " +
                 "(AdaptiveQuadTree.RenderBudgetForViewport - ott a teljes meresi " +
                 "tabla). Pozitiv ertek = kezi felulbiralas, a regi viselkedes.")]
        private int adaptiveRenderBudget;

        [SerializeField]
        [Tooltip("FAZIS 3 (ND-47): a BuildCut (kvadfa-kivalasztas) egy WORKER " +
                 "szalon fusson, ne a fo (renderelo) szalon - igy a kivalasztas " +
                 "koltsege (magas reszletnel 200-590ms) NEM okoz frame-akadast; a " +
                 "regi mesh latszik, amig az uj cut elkeszul. A mesh-UPLOAD (Unity " +
                 "API) tovabbra is a fo szalon tortenik, amint a task kesz. A " +
                 "BuildCut TISZTA fuggveny (nulla megosztott allapot/Unity-API), " +
                 "ezert ez biztonsagos. Single-flight + try/catch fallback: " +
                 "barmilyen hiba eseten a kovetkezo kor a REGI szinkron uton fut. " +
                 "Ha gyanus viselkedes, kapcsold KI a regi, szinkron viselkedeshez.")]
        private bool useAsyncMeshRebuild = true;

        // FAZIS 3: a folyamatban levo async BuildCut+emit task (single-flight) es
        // a KICKOFF-kori kamera-pozicio (az alkalmazaskori konyveleshez). A task a
        // WORKER szalon eloallitja a teljes (fel-nem-toltott) geometriat; a fo
        // szal csak feltolti (TryApplyCompletedAsyncCut -> ApplyAdaptiveMeshBuffers).
        private System.Threading.Tasks.Task<AdaptiveMeshBuffers>? _cutTask;
        private double _pendingCutCamX, _pendingCutCamY, _pendingCutCamZ;
        // Ha az async ut EGYSZER hibazik, tartosan visszaallunk a szinkron utra
        // (kulonben egy determinista hiba vegtelenul ujraprobalna).
        private bool _asyncMeshRebuildDisabledAfterError;

        [SerializeField]
        [Tooltip("A dinamikus CPU-terep külön chunkokba kerül. Csak a megváltozott " +
                 "topológia vagy feloldott geometria épül újra; a víz és border feltöltése " +
                 "egyelőre globális. A csomagméretet a useBoundedDynamicChunks szabályozza.")]
        private bool useChunkedDynamicMesh = true;

        [SerializeField]
        [Range(0, 20)]
        [Tooltip("ND-80: a legdurvább engedett chunk-szint. A túl sok levelet tartalmazó " +
                 "terület automatikusan kisebb chunkokra oszlik. A régi fix módban " +
                 "továbbra is legalább az adaptiveBaseLevel érvényes.")]
        private int dynamicChunkLevel = 6;

        [SerializeField]
        [Tooltip("ND-80: legfeljebb 256 tereplevél/chunk, összevonás 128-nál. " +
                 "Kikapcsolva a korábbi fix chunkszint használható összehasonlításhoz.")]
        private bool useBoundedDynamicChunks = true;

        // A LEGUTOBB feltoltott dinamikus chunk-csoportositas (chunk-gyoker ->
        // a benne levo levelek) - ez a hiszterezishez/diffhez hasonlo bemenet:
        // a KOVETKEZO ComputeAdaptiveMeshBuffersCpu ebbol allapitja meg, mely
        // chunk-ok valtoztak. A worker szal CSAK OLVASSA (ugyanaz a mintazat,
        // mint a _currentCut-nal) - a fo szal irja, amikor mar nem fut task.
        private Dictionary<TileId, HashSet<TileId>> _previousChunkGroups = new Dictionary<TileId, HashSet<TileId>>();
        private Dictionary<TileId, List<Vector3>> _previousChunkPositions = new Dictionary<TileId, List<Vector3>>();
        private TerrainIndexMask? _terrainIndexMask;
        private LodCornerResolver? _activeCornerResolver;
        private Matrix4x4 _requestedLodLocalToClip, _requestedLodLocalToCamera;
        private int _requestedLodPixelWidth, _requestedLodPixelHeight;
        private float _requestedLodNearClip;
        private bool _fullBuildRequestedAfterCut;
        // Chunk-gyoker -> a chunk SAJAT GameObject-je (MeshFilter+MeshRenderer).
        // Explicit dictionary (nem transform.Find(nev)), hogy a teljes-torles
        // (uj vilag, ld. InvalidateAdaptiveCaches) O(chunk-szam) legyen, ne
        // O(chunk-szam * gyerekek-szama).
        private readonly Dictionary<TileId, GameObject> _dynamicChunkGameObjects = new Dictionary<TileId, GameObject>();

        [SerializeField]
        [Tooltip("Hiszterezis-szorzo a targetTilePixelSize-hoz (screen-space-lod): " +
                 "az osszevonashoz a tile-nak ENNEL az arannyal KISEBBRE kell " +
                 "zsugorodnia a kepernyon, mint amennyi eredetileg kivaltotta a " +
                 "felbontast - 1-nel nagyobb ertek akadalyozza meg, hogy egy " +
                 "csomopont a kuszob ket oldalan pattogjon frame-rol frame-re.")]
        private double mergeHysteresisFactor = 1.5;

        [SerializeField]
        [Tooltip("Biztonsagi szorzo a kamera FOV/aspect-jabol szamolt " +
                 "latokup-felszoghoz (ld. RecomputeCutAndRebuildAdaptiveMesh) - " +
                 "1-nel nagyobb erdemes, hogy a kup SZELEN levo, meg reszben " +
                 "lathato csomopontok se essenek ki tul korán.")]
        private double fovSafetyMargin = 1.3;

        [SerializeField]
        [Tooltip("Unity-egység: ekkora mozgás az időkapu után rögtön új LOD-kérést indít. " +
                 "Kisebb mozgás is frissül, legfeljebb 0,25 s indítási késleltetéssel; " +
                 "a futó worker és a minSecondsBetweenAdaptiveRebuilds továbbra is korlátoz.")]
        private float adaptiveCameraMoveThreshold = 0.5f;

        [SerializeField]
        [Tooltip("Geomorphing (§9.2): a split utan megjeleno csucsok ekkora " +
                 "hanyada (a K_split-tavolsaghoz kepesti tortresz) alatt erik el " +
                 "a teljes (nem-morpholt) veglegeset pozíciójukat. Minel nagyobb, " +
                 "annal fokozatosabb az atmenet.")]
        private double geomorphRangeFraction = 0.35;

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
        [Tooltip("INCREMENTAL-MESH-BUFFERS: a base-level tile-ok EGYSZER, " +
                 "'StaticBase' retegkent epulnek fel (this.gameObject + " +
                 "'WaterSurface'/'Borders'), es TOBBET NEM erintve maradnak - " +
                 "a mozgas-kivaltotta ujraepites CSAK a finomitott (level > " +
                 "adaptiveBaseLevel) reteget erinti, KULON GameObject-eken " +
                 "('DynamicRefined'/'DynamicWater'/'DynamicBorders'). Mivel a " +
                 "finomitott tile-ok geomorphing miatt POZICIO-FOLYTONOSAN " +
                 "illeszkednek a statikus szulo-tile felszinehez, a ket reteg " +
                 "UGYANAZON a fizikai helyen atfedne (Z-fighting) - ez a mezo " +
                 "egy PICI, sajat-iranyu kifele-tolast ad a dinamikus reteg " +
                 "csucsainak, hogy egyertelmuen a statikus reteg ELE keruljon.")]
        private float dynamicLayerRadialBias = 0.002f;

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

        // SCREEN-SPACE-LOD: az utolso RecomputeCutAndRebuildAdaptiveMesh
        // hivasban szamolt szogsugar-kuszob - a ComputeGeomorphAlpha ebbol
        // szarmaztatja a split-tavolsagot (a regi, fix adaptiveSplitFactor
        // helyett), mert a kuszob most a kamera FOV/felbontasa alapjan
        // szamolodik minden ujraepiteskor, nem egy Inspector-konstans.
        private double _currentTargetAngularRadiusRadians = AdaptiveQuadTree.DefaultSplitThresholdRadians;
        private double _currentBaseTargetAngularRadiusRadians = AdaptiveQuadTree.DefaultSplitThresholdRadians;
        private double _currentGeomorphRangeFraction = 0.35;
        private TerrainLodProxy? _terrainLodProxy;
        private TerrainLodProxy? _requestedTerrainLodProxy;
        private readonly Dictionary<(int Face, int Level, uint CornerU, uint CornerV), Vector3> _persistentCornerCache = new();
        private readonly LinkedList<(int Face, int Level, uint CornerU, uint CornerV)> _cornerCacheLru = new();
        private readonly Dictionary<(int Face, int Level, uint CornerU, uint CornerV), LinkedListNode<(int Face, int Level, uint CornerU, uint CornerV)>> _cornerCacheLruNodes = new();

        // TELJESITMENY (felhasznaloi visszajelzes: a sarkonkenti szin
        // bevezetese utan a lassulas "konstanssa" valt, nem csak
        // idonkenti tuske): a folytonos sarok-szin MOST MAR a pozicio-
        // sarok-cache-hez HASONLOAN, UGYANABBAN a Parallel.For passzban
        // (PrecomputeCornersInParallel) szamolodik, NEM az egyszalu
        // EmitAdaptiveTile ciklusban - igy a Temperature.TemperatureKelvin-
        // hivasok (~1.45us/hivas, de tile-onkent tobbszorosen jelentkezve)
        // parhuzamosan futnak, nem adodnak ossze egyszalu koltsegkent.
        // MELLEKHATAS (elonyos): mivel a SZIN is a MEGOSZTOTT sarok-
        // gyorsitotarban el, a szomszedos tile-ok UGYANAZT a szin-erteket
        // kapjak a kozos sarokpontjukra - ez a korabbi (tile-onkent
        // fuggetlenul szamolt) sarok-szinnel szemben VARRAT NELKULI
        // atmenetet ad, nem csak kisebb lepcsot.
        private readonly Dictionary<(int Face, int Level, uint CornerU, uint CornerV), Color> _persistentCornerColorCache = new();

        // ND-55 (2026-09-09): a szin-cache-hez hasonlo, MEGOSZTOTT sarok-
        // NORMAL cache - ld. ComputeCornerNormalViaFiniteDifference doksija.
        private readonly Dictionary<(int Face, int Level, uint CornerU, uint CornerV), Vector3> _persistentCornerNormalCache = new();

        // ND-63: a statikus base-level sarokpontok és az ND-55 normál két
        // offset-mintájának world-seed-függő, de deep-time-független terrain-
        // bázisa. Tömör tömb, nem Dictionary: level 8-on a három tömb nyers
        // adata kb. 54,4 MiB. Deep-time rebuildkor megmarad, seed/base-level
        // váltáskor teljesen újraépül.
        private ulong _staticTerrainBasisSeed;
        private int _staticTerrainBasisLevel = -1;
        private TerrainPointBasis[] _staticCornerCenterBasis = Array.Empty<TerrainPointBasis>();
        private TerrainPointBasis[] _staticCornerUBasis = Array.Empty<TerrainPointBasis>();
        private TerrainPointBasis[] _staticCornerVBasis = Array.Empty<TerrainPointBasis>();

        // ND-66: a TELJES base-grid nem ritka halmaz, hanem szabalyos,
        // face/u/v szerint kozvetlenul indexelheto tomb. Ezek a tombok a
        // statikus emit tobb millio Dictionary/LRU muveletet valtjak ki, es
        // Build utan a dinamikus LOD base-szintu szulo-lekerdezeseit is
        // kiszolgaljak. A numerikus ertekek ugyanazok, csak a tarolas mas.
        private int _staticRenderDataLevel = -1;
        private AdaptiveTileClassification[] _staticTileClassifications = Array.Empty<AdaptiveTileClassification>();
        private Vector3[] _staticCornerPositions = Array.Empty<Vector3>();
        private Vector3[] _staticCornerNormals = Array.Empty<Vector3>();
        private Color[] _staticCornerColors = Array.Empty<Color>();

        // ND-63/ND-64: a level-8 tile-KOZEPPONTOK azonos terrain-bazisat
        // hasznalja a hidrologiai elevation field es a base-level tile-
        // klasszifikacio. 393 216 * 48 byte ~= 18 MiB, plusz a TileId tomb.
        private ulong _tileCenterTerrainBasisSeed;
        private int _tileCenterTerrainBasisLevel = -1;
        private TileId[] _tileCenterTerrainIds = Array.Empty<TileId>();
        private TerrainPointBasis[] _tileCenterTerrainBasis = Array.Empty<TerrainPointBasis>();
        private FlowNetwork.DenseGridTopology? _hydrologyDenseTopology;

        private bool _hasLastCutCameraPosition;
        private AdaptiveViewState _lastAppliedCutView;
        private AdaptiveViewState _pendingCutView;
        private float _pendingCutRequestedRealtime;
        private long _pendingCutRequestedTicks;
        private double _lastCutCameraCoreX, _lastCutCameraCoreY, _lastCutCameraCoreZ;

        // Az adaptiv ujraepiteshez szukseges "vilag-kontextus", amit a Build()
        // egyszer szamol ki (referencia-szinten) - az Update()-ben futo
        // ujraszamolasok ezt hasznaljak ujra, nem szamoljak ujra minden frame-ben.
        private ulong _adaptiveSeed;
        private (double X, double Y, double Z)[] _adaptiveSeeds;
        private List<ImpactCratering.CraterRecord> _adaptiveCraters;
        private double _adaptiveSeaLevel;
        private HashSet<TileId> _adaptiveRiverTiles;
        // Referencia-szintu folyo-tile -> a DOWNSTREAM (flood.Parent) tile, amihez
        // a folyo-vonal koti (dendritikus lefolyas-fa). Ld. BuildRiverNetwork.
        private Dictionary<TileId, TileId> _adaptiveRiverParent;
        private HashSet<TileId> _adaptiveLakeTiles;
        // Referencia-szintu szarazfoldi tile -> eves atlaghomerseklet (mar a
        // deep-time eljegesedes-eltolassal, ld. Build()). NEM boolean - a
        // tenyleges PermanentIce-dontes leaf-szinten, zajjal perturbalva
        // tortenik (IsAdaptiveIceTile), hogy a partvonal ne legyen blokkos.
        private Dictionary<TileId, double> _adaptiveIceMeanK;
        // Szurt tavak: tile -> lapos to-felszin (feltoltesi) elevacio, a
        // BuildLakeSurface lapos vizfelszin-rajzaehoz.
        private Dictionary<TileId, double> _adaptiveLakeSurface;
        // Referencia-szintu csapadek-mezo (MoisturePrecipitation) a csapadek-overlayhez.
        private Dictionary<TileId, double> _adaptivePrecip;

        /// <summary>
        /// ND-126: a csapadek-vagopontok a SZARAZFOLDI eloszlasbol, a Build()
        /// elejen EGYSZER kiszamolva. Azert mezo es nem per-hivas szamitas,
        /// mert a biome-osztalyozas tiszta fuggveny kell maradjon: ugyanaz a
        /// harom vagopont vonatkozik minden tile-ra, fuggetlenul attol,
        /// milyen sorrendben kerdezzuk oket.
        /// </summary>
        private BiomeClassification.PrecipitationThresholds _adaptiveBiomeThresholds;

        /// <summary>
        /// ND-130: a referencia-szintu csapadek-mezo SAROK-ertekei (lap-lokalis
        /// (n+1)x(n+1) racs lapokent), a <see cref="PrecipitationAtCore"/>
        /// bilinearis interpolaciojahoz. Egyszer epul (Build), utana csak
        /// OLVASSUK -> szalbiztos a parhuzamos klasszifikaciobol.
        /// </summary>
        private double[] _adaptivePrecipCorners;
        private int _adaptivePrecipCornerLevel = -1;

        /// <summary>
        /// Egy pont csapadeka a REFERENCIA-szintu mezobol, a tile-sarkok
        /// kozott BILINEARISAN interpolalva. Csak OLVAS (Build ota valtozatlan)
        /// -> szalbiztos a parhuzamos sarok-/tile-klasszifikaciobol.
        ///
        /// ND-130 (felhasznaloi visszajelzes: "a zold biom nagy negyszog alaku
        /// regiokban jelenik meg"). A KORABBI valtozat a pontot tartalmazo
        /// REFERENCIA-TILE erteket adta vissza valtozatlanul. A referencia-szint
        /// a jelenetben `level`=5, ahol egy tile ~288 km oldalu - a csapadek
        /// tehat egy 288 km-es, TENGELYPARHUZAMOS lepcsos fuggveny volt, es
        /// mivel az ND-126 ota a biome-osztalyozas BEMENETE, a szarazfoldi
        /// biome-hatarok pontosan ezeket a negyzet-eleket vettek fel.
        ///
        /// A javitas ugyanaz a minta, amit a FELHO-reteg mar hasznal
        /// (<see cref="PrecipAndOceanFractionAtCorner"/> + GPU-interpolacio):
        /// tile-SAROK ertekek (a sarkot oszto tile-ok atlaga, lap-hataron is
        /// helyesen, TileNeighbors-szal), majd a saroknegyes kozott bilinearis
        /// interpolacio a pont tile-on BELULI helye szerint. A mezo ettol
        /// folytonos (C0) lesz, tehat a biome-hatar sima szintvonal, nem
        /// tile-el. NEM talal ki adatot: a MAR MEGLEVO mezo ertekeit
        /// interpolalja, tehat I3/I4 ervenyben marad.
        ///
        /// Ha nincs csapadek-mezo (elvben nem fordulhat elo, mert az ND-126
        /// ota a Build() MINDIG kiszamolja), 0-t ad - az a legszarazabb
        /// osztaly, tehat lathato (sivatagos) jelzes, nem csendes hiba.
        /// </summary>
        private double PrecipitationAtCore(double cx, double cy, double cz)
        {
            if (_adaptivePrecip == null) return 0.0;
            if (_adaptivePrecipCorners == null || _adaptivePrecipCornerLevel != level)
            {
                // Tartalek: a sarok-tabla meg nem epult fel (elvben csak a
                // Build kozbeni, tabla elotti hivasoknal) - a regi, tile-szintu
                // ertek helyes, csak blokkos.
                TileId fallback = TileGeometry.FromPosition(cx, cy, cz, level);
                return _adaptivePrecip.TryGetValue(fallback, out double fp) ? fp : 0.0;
            }

            TileGeometry.ToFaceUV(cx, cy, cz, out int face, out double uc, out double vc);
            int n = 1 << level;
            int side = n + 1;
            double fu = (uc + 1.0) * 0.5 * n;
            double fv = (vc + 1.0) * 0.5 * n;
            int u = (int)fu;
            int v = (int)fv;
            if (u < 0) u = 0; else if (u > n - 1) u = n - 1;
            if (v < 0) v = 0; else if (v > n - 1) v = n - 1;
            double s = fu - u;
            double t = fv - v;
            if (s < 0.0) s = 0.0; else if (s > 1.0) s = 1.0;
            if (t < 0.0) t = 0.0; else if (t > 1.0) t = 1.0;

            int baseIndex = face * side * side;
            double c00 = _adaptivePrecipCorners[baseIndex + u * side + v];
            double c10 = _adaptivePrecipCorners[baseIndex + (u + 1) * side + v];
            double c11 = _adaptivePrecipCorners[baseIndex + (u + 1) * side + (v + 1)];
            double c01 = _adaptivePrecipCorners[baseIndex + u * side + (v + 1)];
            return (c00 * (1.0 - s) + c10 * s) * (1.0 - t) + (c01 * (1.0 - s) + c11 * s) * t;
        }

        /// <summary>
        /// ND-130: a sarok-tabla felepitese a referencia-szintu csapadek-mezobol.
        /// Minden lap-lokalis (cu,cv) racspontra a sarkot oszto (legfeljebb 4)
        /// tile atlaga - a MAR MEGLEVO, lap-hatarra is helyes
        /// <see cref="PrecipAndOceanFractionAtCorner"/>-rel (az TileNeighbors-t
        /// hasznal, nem nyers index-aritmetikat; a kockale-elek menti varrat
        /// elkerulesenek mert indoklasat ld. ott).
        /// </summary>
        private void BuildPrecipitationCornerTable(MoisturePrecipitation.PrecipitationField field)
        {
            if (field == null || level < 0 || level > 12)
            {
                _adaptivePrecipCorners = null;
                _adaptivePrecipCornerLevel = -1;
                return;
            }

            int n = 1 << level;
            int side = n + 1;
            var corners = new double[6 * side * side];
            for (int face = 0; face < 6; face++)
            {
                int baseIndex = face * side * side;
                for (int cu = 0; cu <= n; cu++)
                {
                    for (int cv = 0; cv <= n; cv++)
                    {
                        // A sarkot oszto negy tile-t UGY fogjuk at, hogy a
                        // kivalasztott tile a lapon BELUL legyen, a ket irany
                        // pedig a sarok fele mutasson.
                        uint tu = (uint)(cu == n ? n - 1 : cu);
                        uint tv = (uint)(cv == n ? n - 1 : cv);
                        TileDirection hDir = cu == n ? TileDirection.Right : TileDirection.Left;
                        TileDirection vDir = cv == n ? TileDirection.Up : TileDirection.Down;
                        TileId tile = TileId.FromFaceLevelUV(face, level, tu, tv);
                        (double precip, double _) = PrecipAndOceanFractionAtCorner(
                            field.Precipitation, field.IsOcean, tile, hDir, vDir);
                        corners[baseIndex + cu * side + cv] = precip;
                    }
                }
            }

            _adaptivePrecipCorners = corners;
            _adaptivePrecipCornerLevel = level;
        }
        // ND-49 (2026-09-06, HARMADSZOR ujragondolva felhasznaloi
        // visszajelzesek utan - ld. RiverPathTracing.TraceRiverPathContinuous
        // osztaly-doksi es docs/04-decisions.md ND-49 3. kiegeszites): a
        // folyo a FORRASTOL a TERMESZETES vegallapotaig (ocean/pit/merged)
        // fut, folytonos, a felszin erinto-sikjaban futo lejto-kovetessel,
        // MESTERSEGES lepesszam-vagas NELKUL. EZ DRAGA (merve: 12 referencia-
        // folyora osszesen kb. 6-10s SZEKVENCIALISAN, mert a megosztott
        // `claimed` terkep miatt NEM parhuzamosithato folyononkent) - ezert
        // HATTER-SZALON (LongRunning) fut, NEM blokkolva a Build()-et/a fo
        // szalat; amig nincs kesz, nincs folyo-mesh, es amint a task vegez,
        // a folyo-mesh felepul a finomitott utvonallal.
        private System.Threading.Tasks.Task<List<RiverPathTracing.ContinuousRiverPath>> _riverRefinementTask;
        private System.Threading.CancellationTokenSource _riverRefinementCancellation;
        private List<RiverPathTracing.ContinuousRiverPath> _adaptiveRefinedRiverPaths;
        // M9/M7 "vonal-szélesség nem korrelál a vízhozammal" (backlog,
        // 2026-09-06) - a RiverPathTracing.ComputeDischargeWeights kimenete,
        // a finomitott utvonalakkal EGYUTT frissul (ld.
        // TryApplyCompletedRiverRefinement). Index = riverIdx, ugyanaz mint
        // _adaptiveRefinedRiverPaths-nal.
        private int[] _adaptiveRiverDischargeWeights;
        // Generacio-szamlalo: ha egy UJABB Build() mar elindult, mire egy
        // KORABBI finomitasi task befejezodik, az elavult eredmenyt eldobjuk
        // (nem irjuk felul vele a mar ujabb allapotot).
        private int _riverRefinementGeneration;
        // A JELENLEG futo _riverRefinementTask-hoz tartozo generacio - ezt
        // hasonlitjuk a (kesobb mar tovabbnott) _riverRefinementGeneration-hoz
        // a task befejezesekor, hogy elavult eredmenyt sose alkalmazzunk.
        private int _pendingRiverRefinementGeneration;
        private MoisturePrecipitation.PrecipitationField _riverSourcePrecipField;
        private List<TileId> _riverSourceCache;
        private Stopwatch _riverRefinementStopwatch;

        private void CancelRiverRefinement()
        {
            System.Threading.CancellationTokenSource cancellation = _riverRefinementCancellation;
            System.Threading.Tasks.Task<List<RiverPathTracing.ContinuousRiverPath>> task = _riverRefinementTask;
            if (cancellation != null)
            {
                PerfLog($"[ND-132 river cancel] generation={_pendingRiverRefinementGeneration} completed={task?.IsCompleted} elapsedMs={_riverRefinementStopwatch?.Elapsed.TotalMilliseconds:F1}");
                cancellation.Cancel();
                if (task == null || task.IsCompleted)
                {
                    _ = task?.Exception;
                    cancellation.Dispose();
                }
                else
                {
                    task.ContinueWith(
                        completedTask =>
                        {
                            _ = completedTask.Exception;
                            cancellation.Dispose();
                        },
                        System.Threading.CancellationToken.None,
                        System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,
                        System.Threading.Tasks.TaskScheduler.Default);
                }
                _riverRefinementCancellation = null;
            }
            _riverRefinementTask = null;
        }

        private void StartRiverRefinement(
            ulong seed, (double X, double Y, double Z)[] seeds,
            double seaLevel, IReadOnlyList<TileId> sources)
        {
            if (!showRivers || sources == null || sources.Count == 0)
                return;

            var sourceSnapshot = new List<TileId>(sources);
            double stepMeters = riverRefinementStepMeters;
            _riverRefinementCancellation = new System.Threading.CancellationTokenSource();
            System.Threading.CancellationToken cancellation = _riverRefinementCancellation.Token;
            _pendingRiverRefinementGeneration = _riverRefinementGeneration;
            _riverRefinementStopwatch = Stopwatch.StartNew();
            _riverRefinementTask = System.Threading.Tasks.Task.Factory.StartNew(
                () => RiverPathTracing.BuildContinuousRiverNetworkFromSources(
                    seed, seeds, seaLevel, sourceSnapshot,
                    RiverPathTracing.DefaultFineDepth, stepMeters,
                    cancellation: cancellation),
                cancellation, System.Threading.Tasks.TaskCreationOptions.LongRunning,
                System.Threading.Tasks.TaskScheduler.Default);
            PerfLog($"[ND-132 river start] generation={_pendingRiverRefinementGeneration} sources={sourceSnapshot.Count} scheduler=dedicated-sequential afterBuild=True");
        }
        private double _adaptiveAxialTiltRad;
        private double _adaptiveErosionTimeMyr;
        private DailyInsolationSampleDirections _adaptiveDailyInsolationSamples;

        // M8: az utolsó Build() eredményének gyorsítótára - a panel-adatok
        // (ComputePanelData) ezekre épülnek, hogy ne kelljen a teljes
        // elevation-/óceán-/biome-számítást megismételni. Csak a render
        // UTÁN, egy adott Build()-hívásra érvényesek.
        private ulong _lastSeed;
        private Dictionary<TileId, double> _lastField;
        private Dictionary<TileId, bool> _lastIsOcean;
        private Dictionary<TileId, Biome> _lastBiomeOf;
        private double _lastSeaLevel;
        // A navigacios menu (docs/backlog.md "Navigacios menu es panel-
        // elrendezes") negyedik szintjehez: az utolso ComputePanelData()
        // hivas regio-listaja, UGYANABBAN a sorrendben, mint a
        // data.Regions - hogy a kivalasztott regio indexebol lusta modon
        // (csak akkor, amikor a felhasznalo tenylegesen bele-zoomol)
        // lekerdezhetok legyenek a teruletei, a teljes flood/regio-
        // szamitas ujra-futtatasa nelkul.
        private FlowNetwork.FloodResult _lastFlood;
        private HashSet<TileId> _lastRiverTilesForPanels;
        /// <summary>
        /// ND-127: a panel/navigacio regioi MAR NEM a nyers vizgyujtok (azok
        /// a torkolat ocean-tile-ja szerint kulcsolodnak, tehat terben
        /// szetesnek es a szarazfold felet meretszuro dobta el), hanem a
        /// `FeatureSegmentation.MergeWatershedsIntoRegions` osszevont,
        /// osszefuggo regioi - meret szerint csokkeno, kanonikus sorrendben.
        /// A lista INDEXE a regio globalis azonositoja a navigacioban.
        /// </summary>
        private List<List<TileId>> _lastPanelRegions;
        private List<List<TileId>> _lastSortedContinents;
        // A legutobbi Build()-ben kiszamolt csapadek-mezo (vagy null, ha
        // egyik felhasznaloja - precipitationOverlay/showRivers/showClouds -
        // sem volt bekapcsolva) - ld. ApplyCloudOnlyRebuild doksi: ebbol
        // epul ujra CSAK a felho-reteg, teljes Build() nelkul, ha kizarolag
        // a felho-parameterek valtoztak.
        private MoisturePrecipitation.PrecipitationField _lastPrecipField;

        /// <summary>
        /// ND-117: a szarazfold-tile-ok NYERS evi kozephomerseklete (jegesedesi
        /// eltolas NELKUL), a jeg-klasszifikacio ciklusabol eltéve. A
        /// `RegolithModel` ezt varja bemenetnek. URES, ha a `showLakesIce` ki
        /// van kapcsolva - akkor a Soil fertility panel-mezo NEM jelenik meg
        /// (I4: inkabb hianyozzon, mint hogy kitalalt homersekletbol szamoljuk).
        /// </summary>
        private Dictionary<TileId, double> _lastLandMeanTemperatureK = new Dictionary<TileId, double>();

        /// <summary>
        /// ND-117: a Soil fertility-hez kellő statikus eróziós passz eredménye,
        /// a <see cref="ComputePanelData"/>-ból eltéve. Azért MEZŐ, mert a
        /// navigációs menü régió-sorait a LUSTA
        /// <see cref="BuildRegionPanelDataFromTiles"/> építi, jóval a
        /// panel-számítás után — az pedig enélkül nem tudná kiszámolni a
        /// talaj-termékenységet (ez volt a 2026-09-20-án élő Editorban
        /// megtalált hiba: a top-10 listában megvolt az érték, a menüben nem).
        /// </summary>
        private LakesIceErosion.ErosionResult? _lastSoilErosion;

        /// <summary>A BuildClouds() TISZTA (Unity API-t nem hívó) geometria-eredménye - háttérszálon is biztonságosan építhető, ld. ComputeCloudMeshData.</summary>
        private sealed class CloudMeshData
        {
            public List<Vector3> Vertices;
            public List<Vector3> Normals;
            public List<int> Triangles;
            public List<Color> Colors;
        }

        // FELHASZNALOI VISSZAJELZES (2026-09-06): "az akadás megszűnt amint
        // kikapcsoltam a felhő sodródást" - a periodikus (cloudDriftRebuildIntervalSeconds)
        // ujraepites korabban a FO SZALON, SZINKRON modon futtatta a draga
        // DomainWarp/Fbm-alapu WeatherPrecipitationMultiplier-t MINDEN
        // csapadek-sarokra minden alkalommal, ami frame-hitchet okozott - ez
        // egy folyamatosan mozgo elemen (pl. a forgo csillagegen, ld.
        // StarField) volt a legfeltunobb. UGYANAZ a hatterszalas minta, mint
        // a folyo-finomitasnal (_riverRefinementTask): a TISZTA szamitas
        // (ComputeCloudMeshData, nincs Unity API-hivas) Task.Run-ban fut, a
        // fo szal csak a KESZ eredmenyt tolti fel a mesh-be
        // (LateUpdate -> TryApplyCompletedCloudRebuild). SINGLE-FLIGHT: ha
        // egy szamitas MAR fut, egy ujabb kérés (config-valtozas VAGY
        // sodrodas-tick) nem indit masodikat, hanem varja, amíg a
        // futo befejezodik - igy sose halmozodik fel parhuzamos munka.
        private System.Threading.Tasks.Task<CloudMeshData> _cloudRebuildTask;
        private int _cloudRebuildGeneration;
        private int _pendingCloudRebuildGeneration;

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

        private void Start()
        {
            SynchronizePhysicalReliefScale();
            Built.AddListener(RefreshNavigationCache);
            Build();
        }

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
            // ND-104: a hőmező háttérmunkája és snapshot-átvétele független a
            // LOD-ág korai visszatéréseitől.
            UpdateThermalOverlay();
            TickUnusedTerrainChunks();
            if (!useAdaptiveLod)
            {
                CancelStagedTerrainUpload();
                // Kikapcsolás után is teljesüljön a worker miatt elhalasztott
                // nyilvános Build-kérés; a régi adaptív eredményt már nem rajzoljuk.
                if (_fullBuildRequestedAfterCut && (_cutTask == null || _cutTask.IsCompleted))
                {
                    if (_cutTask != null && _cutTask.IsFaulted)
                        Debug.LogWarning($"Elhagyott adaptív kérés hibája: {_cutTask.Exception?.GetBaseException()}");
                    _cutTask = null;
                    Build();
                }
                return;
            }

            // ONGYOGYITAS Unity "hot reload" utan (felhasznaloi eszrevetel,
            // 2026-09-01: Play alatti szkript-ujraforditas utan a mozgatas
            // "gyorsnak" TUNT, de a tile-szam SOHA nem valtozott - kiderult,
            // hogy nem gyors volt, hanem TELJESEN LEALLT). Ok: a _lastField/
            // _currentCut/_hasLastCutCameraPosition sima privat mezok
            // (nincs [SerializeField]), Play kozbeni domain-reload-nal
            // nullazodnak/false-ra allnak, DE a Start() NEM fut le ujra egy
            // mar aktiv komponensen - az Update() korabban emiatt csendben,
            // orokre visszatert volna minden frame-ben (`_lastField == null`),
            // szemmel "leallasnak" nem, hanem hamis "minden gyors" erzetnek
            // latszva. Most ehelyett ujraepiti magat.
            if (_lastField == null)
            {
                Build();
                return;
            }

            Camera cam = GetAdaptiveCamera();
            if (cam == null)
                return;

            if (_pendingTerrainUpload != null)
            {
                TickStagedTerrainUpload();
                return;
            }

            // FAZIS 3 (ND-47): ha van folyamatban levo async BuildCut, kezeljuk
            // (single-flight). Amig fut, NEM inditunk ujat es nem epitunk ujra -
            // a regi mesh latszik. Amint kesz, a fo szalon alkalmazzuk a cut-ot
            // es felepitjuk a mesh-t (Unity-API csak itt).
            if (_cutTask != null)
            {
                SupersedeObsoleteCut(cam);
                TryApplyCompletedAsyncCut();
                return;
            }

            // Vilag-parameter valtozas (pl. a deepTimeMyr csuszka huzasa, vagy a
            // show*/overlay kapcsolok) -> TELJES Build: a mezo/tengerszint/erozio
            // ES a statikus alapreteg (a felszin dontő tobbsege) is ebbol
            // szarmazik, nem eleg a kamera-vezerelt dinamikus LOD-ujraepites.
            // FEKEZVE ugyanazzal az ido-korlattal, mint a kameramozgas, hogy
            // folyamatos csuszka-huzas kozben ne inditson masodpercenkent tobb
            // (a nagy alapreteg miatt draga) teljes ujraepitest - a valtozas nem
            // vesz el, csak a kovetkezo, fek-utani Update-ben hajtodik vegre.
            if (_fullBuildRequestedAfterCut || WorldConfigChangedSinceBuild())
            {
                if (Time.unscaledTime - _lastAdaptiveRebuildRealtime < minSecondsBetweenAdaptiveRebuilds)
                    return;
                _lastAdaptiveRebuildRealtime = Time.unscaledTime;
                Build();
                return;
            }

            // ND-50: csak a SZINEZES valtozott (overlay ki/be vagy egy
            // szin-skala hatara). Ez NEM indit teljes Build()-et, tehat nem
            // szamol ujra mezot/hidrologiat, es nem dobja el a hatterszalon
            // futo folyo-finomitast - ld. ApplyOverlayOnlyRebuild.
            if (OverlayConfigChangedSinceBuild())
            {
                if (Time.unscaledTime - _lastAdaptiveRebuildRealtime < minSecondsBetweenAdaptiveRebuilds)
                    return;
                _lastAdaptiveRebuildRealtime = Time.unscaledTime;
                ApplyOverlayOnlyRebuild();
                return;
            }

            // Csak a felho-reteg PARAMETEREI valtoztak (a vilag tobbi resze
            // nem) - ld. ApplyCloudOnlyRebuild doksi: ez NEM inditja a
            // teljes Build()-et, tehat NEM dobja el a hatterszalon futo
            // folyo-finomitast (annak generacio-szamlaloja erintetlen marad).
            // FEKEZVE (code-review-ban feltart hianyossag, potolva
            // 2026-09-06): korabban ez az ag NEM hasznalta a
            // minSecondsBetweenAdaptiveRebuilds fekezest, mint a szomszedos
            // agak - egy folyamatos csuszka-huzas ezert MINDEN FRAME-BEN
            // ujraepitette a felho-halot (a resort + redundans sarok-
            // szamitas miatt ez nem is annyira olcso).
            if (CloudConfigChangedSinceBuild())
            {
                if (Time.unscaledTime - _lastAdaptiveRebuildRealtime < minSecondsBetweenAdaptiveRebuilds)
                    return;
                _lastAdaptiveRebuildRealtime = Time.unscaledTime;
                ApplyCloudOnlyRebuild();
                return;
            }

            // Csak a folyo-vonal SUGAR-IRANYU eltolasa valtozott - a
            // BuildRiverNetwork a MAR meglevo, cache-elt adatokbol
            // (_adaptiveRefinedRiverPaths) rajzol
            // ujra, nem indit uj szimulaciot/finomitast, tehat ez sem
            // erinti a hatterszalon futo folyo-finomitas generaciojat.
            if (RiverLineConfigChangedSinceBuild())
            {
                if (Time.unscaledTime - _lastAdaptiveRebuildRealtime < minSecondsBetweenAdaptiveRebuilds)
                    return;
                _lastAdaptiveRebuildRealtime = Time.unscaledTime;
                BuildRiverNetwork();
                SnapshotRiverLineConfig();
                return;
            }

            // Felho-sodrodas (backlog "Felhő-mozgás", 2026-09-06): periodikus,
            // SAJAT fekezesu felho-csak-ujraepites, hogy a korabban teljesen
            // statikus reteg lathatoan mozogjon/valtozzon akkor is, ha SEMMI
            // mas nem valtozott (kamera all, egy csuszkat sem mozgat senki).
            // A `showClouds`/`cloudDriftEnabled`/`_lastPrecipField` feltetel
            // UGYANAZ, mint amit az ApplyCloudOnlyRebuild belul ellenoriz -
            // itt csak azert kell, hogy `_cloudDriftTime` NE haladjon,
            // amikor a reteg amugy sincs lathato/szamolva.
            if (showClouds && cloudDriftEnabled && _lastPrecipField != null)
            {
                _cloudDriftTime += Time.unscaledDeltaTime * cloudDriftTimeScale;
                if (Time.unscaledTime - _lastCloudDriftRebuildRealtime >= cloudDriftRebuildIntervalSeconds)
                {
                    _lastCloudDriftRebuildRealtime = Time.unscaledTime;
                    ApplyCloudOnlyRebuild();
                    return;
                }
            }

            BodyFrameConversion.ToCore(transform.InverseTransformPoint(cam.transform.position), out double camX, out double camY, out double camZ);
            AdaptiveViewState view = CaptureAdaptiveView(cam, camX, camY, camZ);
            bool viewChanged = !_hasLastCutCameraPosition || DesiredTerrainLodProxy() != _requestedTerrainLodProxy || view.NeedsRefresh(
                _lastAppliedCutView, adaptiveCameraMoveThreshold,
                Time.unscaledTime - _lastAdaptiveRebuildRealtime);
            if (!viewChanged && !_adaptiveConfigDirty && !_lodRefinementPending)
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

            if (_adaptiveConfigDirty) _previousChunkCache.Clear();
            _adaptiveConfigDirty = false;
            _lastAdaptiveRebuildRealtime = Time.unscaledTime;
            // A szél-overlay ki/be kapcsolasa modfuggo sarok-szineket ad -
            // ilyenkor a perzisztens szin-cache ELAVUL. Fo szalon, MIELOTT
            // barmilyen (akar async) ujraepites indulna (a _cutTask!=null ag
            // fentebb mar visszatert, tehat itt nincs folyamatban levo build).
            InvalidateColorCacheIfModeChanged();
            RecomputeCutAndRebuildAdaptiveMesh(cam, camX, camY, camZ);
        }

        private bool _colorCacheWindMode;
        private bool _colorCacheTectonicMode;

        /// <summary>
        /// A windSpeedOverlay (és a tektonikus overlay) valtozasakor a
        /// perzisztens sarok-szin-cache (_persistentCornerColorCache) elavul
        /// (mas szamitasi ag adja a szint) - egyszeri urites modvaltaskor. A
        /// sarok-POZICIO cache (_persistentCornerCache) ervenyes marad, mert
        /// a geometria nem fugg az overlaytol.
        /// </summary>
        private void InvalidateColorCacheIfModeChanged()
        {
            if (_colorCacheWindMode != windSpeedOverlay || _colorCacheTectonicMode != tectonicPlateOverlay)
                InvalidateOverlayColorCaches();
        }

        /// <summary>
        /// MINDEN overlay-fuggo szin-gyorsitotar uritese egy helyen.
        ///
        /// 2026-09-20: a `_waterCornerColors` KORABBAN HIANYZOTT innen - a
        /// vizfelszin sarok-szinei ugyanugy overlay-fuggoek
        /// (ContinuousWaterCornerColor), de csak a TELJES ujraepites
        /// (ResetWorldState) uritette oket. Amig minden overlay-valtozas
        /// teljes Build()-et inditott, ez nem latszott; most, hogy az
        /// <see cref="ApplyOverlayOnlyRebuild"/> olcso agon megy, latszana -
        /// ezert ide kerult.
        /// </summary>
        private void InvalidateOverlayColorCaches()
        {
            _persistentCornerColorCache.Clear();
            _waterCornerColors.Clear();
            _previousChunkCache.Clear();
            // ND-66: a tombos base-szin ugyanugy modfuggo, mint a
            // Dictionary-cache. A statikus mesh mar feltoltott szineit ez
            // nem irja at, de kesobbi base-sarok lekerdezes nem kaphat
            // elavult overlay-erteket.
            _staticCornerColors = Array.Empty<Color>();
            _colorCacheWindMode = windSpeedOverlay;
            _colorCacheTectonicMode = tectonicPlateOverlay;
        }

        private float _lastAdaptiveRebuildRealtime = float.NegativeInfinity;

        // IDEIGLENES TELJESITMENY-DIAGNOSZTIKA (2026-09-02, felhasznaloi keres:
        // "adaptiv ujraepites" lassulasanak reszletes, fajlba naplozott
        // visszafejtese). Munkamenetenkent EGY fajl (`unity/WorldGenViewer/
        // Logs/PerfLog_<inditasi-idobelyeg>.txt` - a Logs/ mappa MAR
        // gitignore-olt, tehat ez sosem kerul commitba). SZANDEKOSAN
        // Application.dataPath-bol szarmaztatva (NEM a src/WorldGen.Core
        // determinisztikus reteget erinti - ez tisztan Unity-viewer debug-
        // eszkoz, a DateTime.Now hasznalata itt NEM az I1/I2 megsertese,
        // csak egy fajlnev-idobelyeg).
        private string _perfLogPath;

        private void EnsurePerfLogPath()
        {
            // A `_perfLogPath` haromertekű: null = meg nincs init; "" = init
            // SIKERTELEN volt (a naplozas tartosan kikapcsolva, NE probaljuk
            // ujra minden hivasnal); egyeb = ervenyes ut. A diagnosztikai naplo
            // SOHA nem allithatja meg a viewert (ArgumentException "Empty path
            // name" - pl. ha az Application.dataPath valamiert nem elerheto),
            // ezert az egesz init try/catch-elt.
            if (_perfLogPath != null)
                return;
            try
            {
                string dataPath = Application.dataPath;
                string logsDir = string.IsNullOrEmpty(dataPath) ? "Logs" : System.IO.Path.Combine(dataPath, "..", "Logs");
                System.IO.Directory.CreateDirectory(logsDir);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = System.IO.Path.Combine(logsDir, $"PerfLog_{timestamp}.txt");
                System.IO.File.AppendAllText(path,
                    $"=== PlanetGridMesh perf log started {DateTime.Now:O} ===\n" +
                    $"adaptiveBaseLevel={adaptiveBaseLevel} adaptiveMaxLevel={adaptiveMaxLevel} " +
                    $"targetTilePixelSize={targetTilePixelSize} initialRefinementPixelSize={initialRefinementPixelSize} " +
                    $"radius={radius}\n");
                _perfLogPath = path; // CSAK sikeres init utan
            }
            catch
            {
                _perfLogPath = ""; // sentinel: sikertelen -> a PerfLog ezutan kihagyja
            }
        }

        private void PerfLog(string line)
        {
            EnsurePerfLogPath();
            if (string.IsNullOrEmpty(_perfLogPath))
                return;
            try { System.IO.File.AppendAllText(_perfLogPath, line + "\n"); }
            catch { /* a diagnosztikai naplo sosem dobhat a hivo fele */ }
        }

        private Camera GetAdaptiveCamera() => adaptiveCameraOverride != null ? adaptiveCameraOverride : Camera.main;

        private AdaptiveViewState CaptureAdaptiveView(Camera cam, double x, double y, double z)
        {
            BodyFrameConversion.ToCore(transform.InverseTransformDirection(cam.transform.forward),
                out double fx, out double fy, out double fz);
            return new AdaptiveViewState(x, y, z, fx, fy, fz,
                cam.fieldOfView * Mathf.Deg2Rad, cam.aspect, cam.pixelWidth, cam.pixelHeight);
        }

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
        private float _lastDeepTimeSliderMyr;

        /// <summary>
        /// A kepernyos Gyr-szoveges mezo aktualisan megjelenitett szovege es az
        /// utoljara SIKERESEN beolvasott ertek (Gyr). A buffert csak akkor irjuk
        /// felul a deepTimeMyr-bol, ha az MASHONNAN (csuszka/Inspector) valtozott
        /// - igy a meg be nem fejezett gepeles (pl. "0.") nem veszik el minden
        /// frame-ben.
        /// </summary>
        private string _gyrInputText = "0.000";
        private double _gyrInputParsedValue;

        /// <summary>
        /// A jobb felső "Kamera állása" doboz (<see cref="DrawCameraStatePanel"/>)
        /// forrása - ugyanaz az önfeloldó minta, mint a <see cref="WorldGenPanelUI"/>
        /// EnsureOrbitCameraReference-je (Play közbeni hot-reload után is
        /// pótolja magát, ld. ott a doksit).
        /// </summary>
        private PlanetOrbitCamera _hudOrbitCamera;

        // ---- Navigációs menü (docs/backlog.md "Navigációs menü és
        // panel-elrendezés", 2026-09-13) - bolygó/kontinens/régió/terület
        // négy szintje, breadcrumb + lista + vissza-gomb. Ugyanaz az OnGUI
        // (nem Canvas/EventSystem) alap, mint a Deep time doboz - valódi
        // GUI.Button kattintás, nincs padded-link kompromisszum.
        private enum NavigationLevel { Planet, Continent, Region, Area }

        private NavigationLevel _navLevel = NavigationLevel.Planet;
        private int _navContinentIndex = -1;
        private int _navRegionGlobalIndex = -1;
        private int _navAreaIndex = -1;
        private string _navContinentName = "";
        private string _navRegionName = "";
        private string _navAreaName = "";

        private WorldGenPanelData _navPanelData;
        private readonly Dictionary<int, List<RegionPanelData>> _navRegionsByContinent = new Dictionary<int, List<RegionPanelData>>();
        private readonly Dictionary<int, List<int>> _navRegionGlobalIndicesByContinent = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, List<AreaPanelData>> _navAreasByRegion = new Dictionary<int, List<AreaPanelData>>();
        private Vector2 _navListScroll;

        /// <summary>
        /// Minden sikeres Build() után (ld. Start() - Built.AddListener) -
        /// a navigációs gyorsítótárak a régi világra vonatkoznának, ezért
        /// törlődnek, és a nézet visszaáll bolygó-szintre (a korábbi
        /// kontinens-/régió-index egy ÚJ világon értelmetlen/érvénytelen
        /// lenne).
        /// </summary>
        private void RefreshNavigationCache()
        {
            _navPanelData = ComputePanelData();
            _navRegionsByContinent.Clear();
            _navRegionGlobalIndicesByContinent.Clear();
            _navAreasByRegion.Clear();
            _navLevel = NavigationLevel.Planet;
            _navContinentIndex = -1;
            _navRegionGlobalIndex = -1;
            _navAreaIndex = -1;
        }

        private List<RegionPanelData> GetRegionsForContinentCached(int continentIndex)
        {
            if (!_navRegionsByContinent.TryGetValue(continentIndex, out List<RegionPanelData> regions))
            {
                regions = ComputeRegionPanelDataForContinent(continentIndex, out List<int> globalIndices);
                _navRegionsByContinent[continentIndex] = regions;
                _navRegionGlobalIndicesByContinent[continentIndex] = globalIndices;
            }
            return regions;
        }

        private List<AreaPanelData> GetAreasForRegionCached(int globalRegionIndex)
        {
            if (!_navAreasByRegion.TryGetValue(globalRegionIndex, out List<AreaPanelData> areas))
            {
                areas = ComputeAreaPanelData(globalRegionIndex);
                _navAreasByRegion[globalRegionIndex] = areas;
            }
            return areas;
        }

        /// <summary>
        /// A navigációs menü célpont-irányának BOLYGÓ-LOKÁLIS ->
        /// UNITY-VILÁGTÉR konverziója. A <see cref="CentroidDirection"/> a
        /// bolygó saját (forgatás előtti) terében ad irányt, a
        /// <see cref="PlanetOrbitCamera.FlyToDirection"/> viszont
        /// VILÁGTERI irányt vár. A kettő `CameraViewMode.Free`-ben
        /// megegyezik (a SunController identitáson tartja a bolygó
        /// forgatását), de `CameraViewMode.AxialRotation`-ben a bolygó
        /// TÉNYLEGESEN forog (tengelydőlés * spin, ld. SunController) -
        /// enélkül a kamera a forgásszöggel elvétve, rossz helyre repül.
        /// Csak a FORGATÁST alkalmazzuk (nem TransformDirection-t), hogy
        /// egy esetleges nem-egységnyi skála ne torzítsa az irányt.
        /// </summary>
        private Vector3 ToWorldDirection(Vector3 planetLocalDirection)
        {
            return transform.rotation * planetLocalDirection;
        }

        private void NavigateToPlanet()
        {
            _navLevel = NavigationLevel.Planet;
            _navContinentIndex = -1;
            _navRegionGlobalIndex = -1;
            _navAreaIndex = -1;
            if (_hudOrbitCamera != null)
                _hudOrbitCamera.FlyToDirection(
                    _hudOrbitCamera.CurrentViewDirection,
                    _hudOrbitCamera.SuggestedAltitude(PlanetOrbitCamera.ViewLevel.Planet));
        }

        private void NavigateToContinent(int continentIndex)
        {
            if (_navPanelData == null || continentIndex < 0 || continentIndex >= _navPanelData.Continents.Count) return;
            _navLevel = NavigationLevel.Continent;
            _navContinentIndex = continentIndex;
            _navRegionGlobalIndex = -1;
            _navAreaIndex = -1;
            _navContinentName = _navPanelData.Continents[continentIndex].Name;
            if (_hudOrbitCamera != null)
                _hudOrbitCamera.FlyToDirection(
                    ToWorldDirection(_navPanelData.Continents[continentIndex].CenterDirection),
                    _hudOrbitCamera.SuggestedAltitude(PlanetOrbitCamera.ViewLevel.Continent));
        }

        private void NavigateToRegion(int continentIndex, int globalRegionIndex)
        {
            List<RegionPanelData> regions = GetRegionsForContinentCached(continentIndex);
            List<int> globalIndices = _navRegionGlobalIndicesByContinent[continentIndex];
            int localIndex = globalIndices.IndexOf(globalRegionIndex);
            if (localIndex < 0) return;
            _navLevel = NavigationLevel.Region;
            _navContinentIndex = continentIndex;
            _navRegionGlobalIndex = globalRegionIndex;
            _navAreaIndex = -1;
            _navRegionName = regions[localIndex].Name;
            if (_hudOrbitCamera != null)
                _hudOrbitCamera.FlyToDirection(
                    ToWorldDirection(regions[localIndex].CenterDirection),
                    _hudOrbitCamera.SuggestedAltitude(PlanetOrbitCamera.ViewLevel.Region));
        }

        private void NavigateToArea(int areaIndex)
        {
            List<AreaPanelData> areas = GetAreasForRegionCached(_navRegionGlobalIndex);
            if (areaIndex < 0 || areaIndex >= areas.Count) return;
            _navLevel = NavigationLevel.Area;
            _navAreaIndex = areaIndex;
            _navAreaName = areas[areaIndex].Name;
            if (_hudOrbitCamera != null)
                _hudOrbitCamera.FlyToDirection(
                    ToWorldDirection(areas[areaIndex].CenterDirection),
                    _hudOrbitCamera.SuggestedAltitude(PlanetOrbitCamera.ViewLevel.Region) * 0.5f);
        }

        private void NavigateBack()
        {
            if (_navLevel == NavigationLevel.Area) NavigateToRegion(_navContinentIndex, _navRegionGlobalIndex);
            else if (_navLevel == NavigationLevel.Region) NavigateToContinent(_navContinentIndex);
            else if (_navLevel == NavigationLevel.Continent) NavigateToPlanet();
        }

        // FELHASZNALOI KERES (2026-09-13): teljes tizes-lepteku sor 1 evtol
        // 100 millio evig - a korabbi sorbol hianyzott a "100ky" (a 10ky->1my
        // lepes emiatt 100x-os volt, nem 10x-os).
        private static readonly string[] DeepTimeStepLabels =
        {
            "1y", "10y", "100y", "1ky", "10ky", "100ky", "1my", "10my", "100my"
        };

        // A deepTimeMyr belso egysege millio ev (Myr).
        private static readonly double[] DeepTimeStepMyr =
        {
            0.000001, 0.00001, 0.0001, 0.001, 0.01, 0.1, 1.0, 10.0, 100.0
        };

        private const int DeepTimeButtonsPerRow = 9;
        private const int DeepTimeButtonRowCount = 1;

        private void SynchronizePhysicalReliefScale()
        {
            if (!usePhysicalReliefScale)
                return;

            elevationScale = radius / PlanetConstants.RadiusMeters;
            terrainReliefExaggeration = 1.0;
        }

        private void OnValidate()
        {
            SynchronizePhysicalReliefScale();
            _uploadConfigRevision++;
            _adaptiveConfigDirty = true;

            // Deep-time csuszka <-> deepTimeMyr ketiranyu szinkron: amelyik
            // EPP valtozott (a csuszka huzasa VAGY a double mezo begepelese),
            // az frissiti a masikat. A szamitas mindig a double deepTimeMyr-t
            // hasznalja (a csuszka csak Inspector-vezerlo, ld. a mezo tooltipje).
            if (deepTimeSliderMyr != _lastDeepTimeSliderMyr)
            {
                deepTimeMyr = deepTimeSliderMyr;
                _lastDeepTimeSliderMyr = deepTimeSliderMyr;
            }
            else if ((float)deepTimeMyr != deepTimeSliderMyr)
            {
                deepTimeSliderMyr = (float)deepTimeMyr;
                _lastDeepTimeSliderMyr = deepTimeSliderMyr;
            }
        }

        // A vilagot MEGHATAROZO (teljes Build()-et igenylo) parameterek
        // pillanatkepe. Barmelyik valtozasa (pl. a deepTimeMyr csuszka huzasa)
        // TELJES ujraszamolast igenyel - a statikus alapreteg (BuildStaticBaseLayer)
        // ES a mezo/tengerszint/erozio is ezekbol szarmazik, nem eleg a dinamikus
        // LOD-ujraepites. (A tisztan LOD-vezerlo mezok - budget, base level stb. -
        // NEM ide tartoznak: azokat a _adaptiveConfigDirty ag kezeli.)
        private bool _hasWorldConfigSnapshot;
        private long _wcWorldSeed; private int _wcPlateCount, _wcLevel, _wcAdaptiveBaseLevel;
        private float _wcRadius;
        private double _wcDeepTime, _wcTargetWater, _wcRiverFrac;
        private double _wcDayT, _wcOrbital, _wcRotation, _wcAxialTilt, _wcRelief, _wcElevScale;
        private bool _wcCraters, _wcRivers, _wcLakesIce, _wcErosion;
        private int _wcHydroLevel, _wcMinLakeTiles;
        private double _wcMinLakeDepth;

        private void SnapshotWorldConfig()
        {
            _cameraSurfaceRevision++;
            _hasWorldConfigSnapshot = true;
            _wcWorldSeed = worldSeed; _wcPlateCount = plateCount; _wcLevel = level;
            _wcAdaptiveBaseLevel = adaptiveBaseLevel; _wcRadius = radius;
            _wcDeepTime = deepTimeMyr; _wcTargetWater = targetWaterFraction; _wcRiverFrac = riverTargetFraction;
            _wcDayT = climateDayT; _wcOrbital = climateOrbitalPeriodDays;
            _wcRotation = climateRotationPeriodDays; _wcAxialTilt = climateAxialTiltDegrees;
            _wcRelief = terrainReliefExaggeration; _wcElevScale = elevationScale;
            _wcCraters = showCraters; _wcRivers = showRivers; _wcLakesIce = showLakesIce;
            _wcErosion = showDeepTimeErosion;
            _wcHydroLevel = hydrologyLevel; _wcMinLakeTiles = minLakeTiles;
            _wcMinLakeDepth = minLakeDepthMeters;
        }

        // FELHASZNALOI IGENY (2026-09-06, code-review-ban feltarva): a
        // felho-fixhez hasonloan a folyo-vonal SUGAR-IRANYU eltolasa
        // (riverLineRadialBias, csak z-fighting elleni vizualis trukk) is
        // KORABBAN a teljes WorldConfigChangedSinceBuild-en ment at - MOST
        // KULON figyeljuk, es CSAK a folyo-vonal-reteget epitjuk ujra a MAR
        // meglevo cache-elt adatokbol (BuildRiverNetwork nem indit uj
        // szimulaciot/finomitast), a tobbi reteg (es a folyo-finomitasi
        // generacio) erintetlen marad. FONTOS: a windSpeedOverlay/
        // precipitationOverlay/windSpeedColorMaxMs/precipitationColorMax
        // TOVABBRA IS a teljes WorldConfigChangedSinceBuild-en megy at,
        // MERT ezek a FO terep-mesh SZINET valtoztatjak (nem egy kulon
        // reteget, mint a felho/folyo-vonal) - ehhez a teljes statikus+
        // dinamikus mesh ujraszinezese kellene egy kulon, csak-szinezo
        // lepessel, ami egy nagyobb, kulon tervezest igenylo refaktor
        // (ld. docs/04-decisions.md nyitott pont) - NEM oldjuk meg csendben.
        private bool _hasRiverLineConfigSnapshot;
        private double _wcRiverLineBias;

        private void SnapshotRiverLineConfig()
        {
            _hasRiverLineConfigSnapshot = true;
            _wcRiverLineBias = riverLineRadialBias;
        }

        private bool RiverLineConfigChangedSinceBuild()
        {
            if (!_hasRiverLineConfigSnapshot) return true;
            return _wcRiverLineBias != riverLineRadialBias;
        }

        private bool WorldConfigChangedSinceBuild()
        {
            if (!_hasWorldConfigSnapshot) return true;
            return _wcWorldSeed != worldSeed || _wcPlateCount != plateCount || _wcLevel != level
                || _wcAdaptiveBaseLevel != adaptiveBaseLevel || _wcRadius != radius
                || _wcDeepTime != deepTimeMyr || _wcTargetWater != targetWaterFraction || _wcRiverFrac != riverTargetFraction
                || _wcDayT != climateDayT || _wcOrbital != climateOrbitalPeriodDays
                || _wcRotation != climateRotationPeriodDays || _wcAxialTilt != climateAxialTiltDegrees
                || _wcRelief != terrainReliefExaggeration || _wcElevScale != elevationScale
                || _wcCraters != showCraters || _wcRivers != showRivers || _wcLakesIce != showLakesIce
                || _wcErosion != showDeepTimeErosion
                || _wcHydroLevel != hydrologyLevel || _wcMinLakeTiles != minLakeTiles
                || _wcMinLakeDepth != minLakeDepthMeters;
        }

        // ND-50 LEZARASA (2026-09-20). Az overlay-mezok KORABBAN a teljes
        // WorldConfigChangedSinceBuild-en mentek at, tehat egy overlay
        // ki-/bekapcsolasa TELJES Build()-et inditott: ujraszamolta a mezot, a
        // tengerszintet, a hidrologiat, az eroziot es a csapadekot, majd
        // bumpolta a folyo-finomitasi generaciot - vagyis ELDOBTA a
        // hatterszalon futo, tobb masodperces folyo-finomitast. Ez UGYANAZ a
        // hibaosztaly, amit a felhore (ApplyCloudOnlyRebuild) es a folyo-
        // vonalra mar kijavitottunk.
        //
        // Az ND-50 azert maradt nyitva, mert ezek NEM kulon reteg, hanem a FO
        // terep-mesh szinet cserelik. A megoldas viszont nem igenyelt mely
        // mesh-refaktort: a BuildStaticBaseLayer() MAR ONALLO es kizarolag a
        // CACHE-ELT vilagallapotbol (_adaptiveSeed/_adaptiveSeeds/
        // _adaptiveCraters/_adaptiveSeaLevel + a terrain-bazis cache) dolgozik
        // - szimulaciot NEM futtat. Eleg tehat az overlay-mezoket sajat agra
        // tenni, a szin-cache-eket uriteni, es CSAK a statikus alapreteget
        // ujraepiteni; a dinamikus chunkokat az _adaptiveConfigDirty viszi.
        private bool _hasOverlayConfigSnapshot;
        private bool _ocWindOverlay, _ocPrecipOverlay, _ocTectonicOverlay;
        private double _ocWindMax, _ocPrecipMax;

        private void SnapshotOverlayConfig()
        {
            _hasOverlayConfigSnapshot = true;
            _ocWindOverlay = windSpeedOverlay;
            _ocPrecipOverlay = precipitationOverlay;
            _ocTectonicOverlay = tectonicPlateOverlay;
            _ocWindMax = windSpeedColorMaxMs;
            _ocPrecipMax = precipitationColorMax;
        }

        private bool OverlayConfigChangedSinceBuild()
        {
            if (!_hasOverlayConfigSnapshot) return true;
            return _ocWindOverlay != windSpeedOverlay || _ocPrecipOverlay != precipitationOverlay
                || _ocTectonicOverlay != tectonicPlateOverlay
                || _ocWindMax != windSpeedColorMaxMs || _ocPrecipMax != precipitationColorMax;
        }

        /// <summary>
        /// Csak a SZINEZES valtozott (overlay ki/be, vagy egy szin-skala
        /// felso hatara) - a geometria, a mezok es a hidrologia valtozatlan.
        /// Uriti a szin-cache-eket es ujraepiti a statikus alapreteget a MAR
        /// kiszamolt vilagallapotbol; a dinamikus chunkokat a kovetkezo
        /// Update() szinezi ujra (_adaptiveConfigDirty). A folyo-finomitasi
        /// es a felho-generacio ERINTETLEN marad.
        ///
        /// KET DEGENERALT ESET esik vissza teljes Build()-re:
        ///  - meg SOHA nem futott Build (nincs mibol ujraepiteni);
        ///  - a csapadek-overlayt kapcsoljak be, de csapadek-mezo meg nem
        ///    szamolodott (`_adaptivePrecip == null`, mert eddig sem
        ///    showRivers, sem showClouds, sem ez az overlay nem kellett).
        ///    Enelkul az overlay CSENDBEN nulla csapadekot rajzolna mindenhova
        ///    - ugyanaz a csapda, amit az ApplyCloudOnlyRebuild is kezel.
        /// </summary>
        private void ApplyOverlayOnlyRebuild()
        {
            if (!_hasWorldConfigSnapshot || _staticRenderDataLevel < 0)
            {
                Build();
                return;
            }
            if (precipitationOverlay && _adaptivePrecip == null)
            {
                Build();
                return;
            }

            InvalidateOverlayColorCaches();
            BuildStaticBaseLayer(reuseClassifications: true);
            // A dinamikus (kamera-vezerelt) chunkok szinei is elavultak: a
            // kovetkezo Update ezt latva epiti ujra oket.
            _adaptiveConfigDirty = true;
            SnapshotOverlayConfig();
        }

        // FELHASZNALOI IGENY (2026-09-06, egy masik vizsgalat feltarta): a
        // felho-reteg TISZTAN VIZUALIS parameterei (showClouds, magassag,
        // atlatszatlansag, kuszob/gamma) KORABBAN a FENTI, teljes
        // WorldConfigChangedSinceBuild-be tartoztak - barmelyikuk valtozasa
        // TELJES Build()-et inditott, ami ELDOBTA a hatterszalon futo,
        // tobb masodperces folyo-finomitast (uj generacio, regi eredmeny
        // eldobva). Ha valaki folyamatosan mozgat egy felho-csuszkat, a
        // finomitas SOHA nem tud lefutni - a felhasznalo mindig a regi,
        // durva (tile-kozeppontos) folyo-vonalat latja, fuggetlenul attol,
        // hogy a Core-oldali algoritmus helyesen mukodik. KULON figyeljuk
        // ezert a felho-parametereket, es CSAK a felho-reteget epitjuk
        // ujra (a MAR kiszamitott, cache-elt _lastPrecipField-bol, uj
        // MoisturePrecipitation.Compute NELKUL) - a tobbi statikus/dinamikus
        // reteg (es a folyo-finomitasi generacio) erintetlen marad.
        private bool _hasCloudConfigSnapshot;
        private bool _wcShowClouds;
        private double _wcCloudAltitude, _wcCloudOpacity, _wcCloudThreshold, _wcCloudGamma;

        private void SnapshotCloudConfig()
        {
            _hasCloudConfigSnapshot = true;
            _wcShowClouds = showClouds;
            _wcCloudAltitude = cloudAltitudeMeters;
            _wcCloudOpacity = cloudMaxOpacity;
            _wcCloudThreshold = cloudDensityThreshold;
            _wcCloudGamma = cloudDensityGamma;
        }

        private bool CloudConfigChangedSinceBuild()
        {
            if (!_hasCloudConfigSnapshot) return true;
            return _wcShowClouds != showClouds || _wcCloudAltitude != cloudAltitudeMeters
                || _wcCloudOpacity != cloudMaxOpacity || _wcCloudThreshold != cloudDensityThreshold
                || _wcCloudGamma != cloudDensityGamma;
        }

        /// <summary>
        /// Csak a felho-reteget epiti ujra, a MAR kiszamolt (cache-elt)
        /// csapadek-mezobol - lasd a mezok feletti doksit. Ha meg SOHA nem
        /// szamolodott csapadek-mezo (`_lastPrecipField == null` - pl.
        /// eddig sem showRivers, sem precipitationOverlay, sem showClouds
        /// nem volt bekapcsolva), akkor EHHEZ tenylegesen TELJES Build()
        /// kell (ki kell szamolni a mezot) - ez a RITKA elfajult eset ide
        /// esik vissza. A FEKEZES (`minSecondsBetweenAdaptiveRebuilds`) MAR
        /// a hivo `Update()`-ben megtortent (`_lastAdaptiveRebuildRealtime`
        /// mar frissult, mielott ez a metodus lefutott) - itt UJRA
        /// ellenorizni HIBAS lenne (a kulonbseg kozvetlenul a frissites
        /// utan mindig a kuszob alatt lenne, tehat a `Build()` SOHA nem
        /// futna le ebben az agban).
        ///
        /// FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06): "az akadás megszűnt
        /// amint kikapcsoltam a felhő sodródást" - a felhő-elrejtés
        /// (`!showClouds`) olcsó (csak `GameObject.SetActive`), ezért
        /// SZINKRON marad, de a TÉNYLEGES sűrűség-újraszámítás mostantól
        /// háttérszálon fut (ld. <see cref="TryApplyCompletedCloudRebuild"/>).
        /// SINGLE-FLIGHT: ha egy korábbi számítás még fut, ez a hívás
        /// nem indít másodikat (a KÖVETKEZŐ debounce-ütemben újra
        /// megpróbálja, a KÉSZ eredmény felszabadítja a "csatornát").
        /// </summary>
        private void ApplyCloudOnlyRebuild()
        {
            if (_lastPrecipField == null)
            {
                Build();
                return;
            }
            if (!showClouds)
            {
                BuildClouds(null);
                SnapshotCloudConfig();
                return;
            }
            if (_cloudRebuildTask != null)
                return; // MAR fut egy szamitas - a kovetkezo tick ujra probalja, amint felszabadul.

            MoisturePrecipitation.PrecipitationField precipField = _lastPrecipField;
            double cloudTime = cloudDriftEnabled ? _cloudDriftTime : 0.0;
            ulong seed = _adaptiveSeed;
            float radiusParam = radius;
            double elevationScaleParam = elevationScale;
            double altitudeParam = cloudAltitudeMeters;
            double thresholdParam = cloudDensityThreshold;
            double gammaParam = cloudDensityGamma;
            float opacityParam = cloudMaxOpacity;

            _cloudRebuildGeneration++;
            _pendingCloudRebuildGeneration = _cloudRebuildGeneration;
            _cloudRebuildTask = System.Threading.Tasks.Task.Run(() =>
                ComputeCloudMeshData(
                    precipField, cloudTime, seed, radiusParam, elevationScaleParam,
                    altitudeParam, thresholdParam, gammaParam, opacityParam));
            SnapshotCloudConfig();
        }

        /// <summary>
        /// Ha a háttérszálon futó <see cref="ComputeCloudMeshData"/>
        /// elkészült (ld. ApplyCloudOnlyRebuild), itt (fő szál) alkalmazzuk.
        /// Ugyanaz a generáció-elavulás-védelem, mint a folyó-finomításnál
        /// (TryApplyCompletedRiverRefinement) - bár a felhő-réteg esetén ez
        /// GYAKORLATBAN ritkán fordulhat elő (single-flight véd a
        /// párhuzamos indítástól), védelemként megtartva.
        /// </summary>
        private void TryApplyCompletedCloudRebuild()
        {
            if (_cloudRebuildTask == null || !_cloudRebuildTask.IsCompleted)
                return;

            System.Threading.Tasks.Task<CloudMeshData> task = _cloudRebuildTask;
            _cloudRebuildTask = null;

            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning(
                    "PlanetGridMesh: a háttérszálon futó felhő-újraszámítás hibával zárult - " +
                    $"a réteg a korábbi állapotában marad látható. Hiba: {task.Exception?.GetBaseException()}");
                return;
            }

            if (_pendingCloudRebuildGeneration != _cloudRebuildGeneration)
                return; // elavult - egy ujabb kérés már felülírta, mire ez elkészült

            ApplyCloudMeshData(task.Result);
        }

        /// <summary>
        /// Kepernyos (Game view) deep-time csuszka + gyorskapcsolok - IMGUI,
        /// nem kell hozza Canvas/prefab/jelenet-modositas. A mezok kozvetlen
        /// allitasa utan az Update() veszi eszre a valtozast
        /// (WorldConfigChangedSinceBuild) es epit ujra (fekezve). Fejlesztoi/
        /// diagnosztikai vezerlo - a showOnScreenControls kikapcsolja.
        /// </summary>
        /// <summary>
        /// FELHASZNÁLÓI KÉRÉS (2026-09-13, jóváhagyott mockup - ld.
        /// docs/backlog.md "Navigációs menü és panel-elrendezés"): a
        /// vezérlők BAL oldalon (Navigáció felül, alatta Deep time, alatta
        /// Rétegek), az olvasók (kamera állása, kiválasztott elem infója)
        /// JOBB oldalon - a korábbi, mindent jobbra tevő elrendezés helyett.
        /// </summary>
        private void OnGUI()
        {
            if (!showOnScreenControls)
                return;

            const float pad = 12f, w = 460f, rowH = 22f;
            float xLeft = pad;
            float xRight = Mathf.Max(pad, Screen.width - w - pad);
            float yLeft = pad;
            float yRight = pad;

            yLeft += DrawNavigationPanel(xLeft, yLeft, w, rowH);
            yLeft += 6f;
            yLeft += DrawDeepTimePanel(xLeft, yLeft, w, rowH);
            yLeft += 6f;
            DrawLayersPanel(xLeft, yLeft, w, rowH);

            yRight += DrawCameraStatePanel(xRight, yRight, w, rowH);
            yRight += 6f;
            yRight += DrawSelectedInfoPanel(xRight, yRight, w, rowH);

            // Tektonikuslemez-overlay jelmagyarázat - csak akkor foglal helyet,
            // ha az overlay be van kapcsolva (DrawTectonicPlateLegend 0-t ad
            // vissza egyébként).
            yRight += 6f;
            DrawTectonicPlateLegend(xRight, yRight, w, rowH);
        }

        private float DrawDeepTimePanel(float x, float y, float w, float rowH)
        {
            float startY = y;
            // A kilenc pozitiv leptek egy sorban, alattuk a kilenc negativ
            // leptek egy masodik sorban fer el. Tartsuk a hatteret
            // ugyanabbol a sorszambol szamolva, hogy egyetlen vezerlo se
            // logjon ki a dobozbol.
            const float panelRowCount = 6f;
            GUI.Box(new Rect(x - 6f, y - 6f, w + 12f, rowH * panelRowCount + 16f), "Deep time");
            y += rowH * 0.6f;

            GUI.Label(new Rect(x, y, w, rowH), $"Idő: {deepTimeMyr:F1} Myr  ({deepTimeMyr / 1000.0:F3} Gyr)");
            y += rowH;

            float sliderVal = GUI.HorizontalSlider(new Rect(x, y + 6f, w, rowH), (float)deepTimeMyr, 0f, onScreenDeepTimeMaxMyr);
            y += rowH;
            if (Mathf.Abs(sliderVal - (float)deepTimeMyr) > 1e-4f)
            {
                deepTimeMyr = sliderVal;
                deepTimeSliderMyr = sliderVal;
                _lastDeepTimeSliderMyr = sliderVal;
                _gyrInputParsedValue = deepTimeMyr / 1000.0;
                _gyrInputText = FormatDeepTimeGyr(_gyrInputParsedValue);
                // Az Update() innen a WorldConfigChangedSinceBuild-en at, fekezve epit ujra.
            }

            DrawDeepTimeStepButtons(x, y, w, rowH, 1.0);
            y += rowH * DeepTimeButtonRowCount;
            DrawDeepTimeStepButtons(x, y, w, rowH, -1.0);
            y += rowH * DeepTimeButtonRowCount;

            // Kezi Gyr-bevitel (milliard ev) - a beirt szoveget NEM alkalmazzuk
            // azonnal (kulonben minden ertelmes reszprefixnel - pl. "0.2" utan
            // meg egy "5"-nel - ujraepitene a vilagot). Csak Enter/Tovabbi
            // lenyomasara VAGY az "Alkalmaz" gombra kerul at a deepTimeMyr-be.
            // A szoveges mezot csak akkor irjuk felul a deepTimeMyr-bol, ha az
            // MASHONNAN (csuszka fent, vagy Inspector) valtozott, kulonben a
            // meg be nem fejezett gepeles minden frame-ben elveszne.
            const string gyrFieldControlName = "DeepTimeGyrInput";
            double currentGyr = deepTimeMyr / 1000.0;
            if (Math.Abs(currentGyr - _gyrInputParsedValue) > 1e-9
                && GUI.GetNameOfFocusedControl() != gyrFieldControlName)
            {
                _gyrInputParsedValue = currentGyr;
                _gyrInputText = FormatDeepTimeGyr(currentGyr);
            }
            GUI.Label(new Rect(x, y, 90f, rowH), "Gyr kézzel:");
            GUI.SetNextControlName(gyrFieldControlName);
            bool enterPressedInField = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == gyrFieldControlName;
            _gyrInputText = GUI.TextField(new Rect(x + 95f, y, w - 165f, rowH), _gyrInputText);
            bool applyClicked = GUI.Button(new Rect(x + w - 65f, y, 65f, rowH), "Alkalmaz");
            if (enterPressedInField || applyClicked)
            {
                if (double.TryParse(_gyrInputText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedGyr)
                    && double.IsFinite(parsedGyr) && parsedGyr >= 0.0)
                {
                    double clampedGyr = Math.Min(parsedGyr, onScreenDeepTimeMaxMyr / 1000.0);
                    deepTimeMyr = clampedGyr * 1000.0;
                    deepTimeSliderMyr = (float)deepTimeMyr;
                    _lastDeepTimeSliderMyr = deepTimeSliderMyr;
                    _gyrInputParsedValue = clampedGyr;
                    _gyrInputText = FormatDeepTimeGyr(clampedGyr);
                    // Az Update() innen a WorldConfigChangedSinceBuild-en at, fekezve epit ujra.
                }
                if (enterPressedInField)
                    Event.current.Use();
            }
            y += rowH;

            return (y - startY) + 10f;
        }

        /// <summary>
        /// A korábbi Deep time dobozból kiemelt overlay-/kamera-mód
        /// kapcsolók, KÜLÖN dobozban (ld. docs/backlog.md "Navigációs menü
        /// és panel-elrendezés" - "fenntartott hely" a jövőbeli hőmérséklet-
        /// overlay blokknak, ami a hőmodell-terv 11./22. döntése szerint
        /// SZINTÉN ide, ebbe a dobozba kerül majd).
        /// </summary>
        private void DrawLayersPanel(float x, float y, float w, float rowH)
        {
            float panelRowCount = 5f + ThermalOverlayPanelRows;
            GUI.Box(new Rect(x - 6f, y - 6f, w + 12f, rowH * panelRowCount + 16f), "Rétegek");
            y += rowH * 0.6f;

            showDeepTimeErosion = GUI.Toggle(new Rect(x, y + 4f, w * 0.5f, rowH), showDeepTimeErosion, " Erózió (kopás)");
            bool windToggle = GUI.Toggle(new Rect(x + w * 0.5f, y + 4f, w * 0.5f, rowH), windSpeedOverlay, " Szél-overlay");
            if (windToggle && !windSpeedOverlay)
                tectonicPlateOverlay = false; // az overlay-ek kolcsonosen kizarjak egymast
            windSpeedOverlay = windToggle;
            y += rowH;
            showLakesIce = GUI.Toggle(new Rect(x, y + 4f, w * 0.5f, rowH), showLakesIce, " Tavak+jég");
            showCraters = GUI.Toggle(new Rect(x + w * 0.5f, y + 4f, w * 0.5f, rowH), showCraters, " Kráterek");
            y += rowH;
            bool precipToggle = GUI.Toggle(new Rect(x, y + 4f, w * 0.5f, rowH), precipitationOverlay, " Csapadék-overlay");
            if (precipToggle && !precipitationOverlay)
            {
                windSpeedOverlay = false; // a ket overlay kolcsonosen kizarja egymast (a szel megy elobb)
                tectonicPlateOverlay = false;
            }
            precipitationOverlay = precipToggle;
            showClouds = GUI.Toggle(new Rect(x + w * 0.5f, y + 4f, w * 0.5f, rowH), showClouds, " Felhők (MVP)");
            y += rowH;
            cloudDriftEnabled = GUI.Toggle(new Rect(x, y + 4f, w * 0.5f, rowH), cloudDriftEnabled, " Felhő-sodródás");

            // Tektonikuslemez-overlay (2026-09-13, docs/backlog.md) - a tobbi
            // felszin-szinezo overlay-vel (szel/csapadek/ho) kolcsonosen
            // kizarja egymast, UGYANAZZAL a mintaval, mint a szel/csapadek
            // par: csak az AKTIVALODO (false->true) valtasra reagalunk.
            bool tectonicToggle = GUI.Toggle(new Rect(x + w * 0.5f, y + 4f, w * 0.5f, rowH), tectonicPlateOverlay, " Tektonikus lemezek");
            if (tectonicToggle && !tectonicPlateOverlay)
            {
                windSpeedOverlay = false;
                precipitationOverlay = false;
                thermalOverlayMode = ThermalOverlayMode.Off;
            }
            tectonicPlateOverlay = tectonicToggle;
            y += rowH;

            // Kamera-mód: 3 kölcsönösen kizáró váltógomb (ugyanaz a minta,
            // mint a windSpeedOverlay/precipitationOverlay kizárásnál) -
            // csak az AKTIVÁLÓDÓ (false->true) váltásra reagálunk, hogy
            // sose lehessen mindet egyszerre kikapcsolni kattintással.
            bool freeToggle = GUI.Toggle(new Rect(x, y + 4f, w * 0.5f, rowH), cameraViewMode == CameraViewMode.Free, " Szabad kamera");
            bool axialToggle = GUI.Toggle(new Rect(x + w * 0.5f, y + 4f, w * 0.5f, rowH), cameraViewMode == CameraViewMode.AxialRotation, " Tengelyforgás");
            if (freeToggle && cameraViewMode != CameraViewMode.Free) cameraViewMode = CameraViewMode.Free;
            else if (axialToggle && cameraViewMode != CameraViewMode.AxialRotation) cameraViewMode = CameraViewMode.AxialRotation;
            y += rowH;

            // ND-104: pillanatnyi hőmérséklet-overlay blokk (PlanetGridMesh.ThermalOverlay.cs).
            DrawThermalOverlayRows(x, y, w, rowH);
        }

        /// <summary>
        /// A navigációs doboz: breadcrumb, "Vissza" gomb, gördíthető lista a
        /// jelenlegi szint gyerekeiről (kontinensek / régiók / területek).
        /// Valódi <see cref="GUI.Button"/> kattintás - NEM a Canvas/TMP
        /// padded-link kompromisszum (az a megoldás 2026-09-13-án megszűnt,
        /// ld. history/2026-09-13-legacy-panel-removal.md; itt OnGUI-ban nincs
        /// EventSystem/GraphicRaycaster-függés, tehát valódi gomb használható).
        /// </summary>
        private float DrawNavigationPanel(float x, float y, float w, float rowH)
        {
            EnsureNavigationCamera();
            const int maxVisibleRows = 7;
            float listHeight = rowH * maxVisibleRows;
            float boxHeight = rowH * 2.6f + listHeight + 16f;
            GUI.Box(new Rect(x - 6f, y - 6f, w + 12f, boxHeight), "Navigáció");

            float rowY = y + rowH * 0.6f;
            DrawBreadcrumb(x, rowY, w, rowH);
            rowY += rowH;

            if (_navLevel != NavigationLevel.Planet)
            {
                if (GUI.Button(new Rect(x, rowY, 90f, rowH), "‹ Vissza"))
                    NavigateBack();
            }
            GUI.Label(new Rect(x + 100f, rowY, w - 100f, rowH), $"Szint: {NavigationLevelLabel(_navLevel)}");
            rowY += rowH;

            DrawNavigationList(new Rect(x, rowY, w, listHeight), rowH);

            return boxHeight + 10f;
        }

        private void EnsureNavigationCamera()
        {
            if (_hudOrbitCamera == null)
                _hudOrbitCamera = FindObjectOfType<PlanetOrbitCamera>();
        }

        private static string NavigationLevelLabel(NavigationLevel level) => level switch
        {
            NavigationLevel.Planet => "Bolygó",
            NavigationLevel.Continent => "Kontinens",
            NavigationLevel.Region => "Régió",
            _ => "Terület",
        };

        private void DrawBreadcrumb(float x, float y, float w, float rowH)
        {
            float cx = x;
            DrawCrumb(ref cx, y, rowH, "Bolygó", _navLevel == NavigationLevel.Planet, NavigateToPlanet);

            if (_navLevel >= NavigationLevel.Continent && _navContinentIndex >= 0)
            {
                DrawCrumbSeparator(ref cx, y, rowH);
                int capturedContinent = _navContinentIndex;
                DrawCrumb(ref cx, y, rowH, _navContinentName, _navLevel == NavigationLevel.Continent,
                    () => NavigateToContinent(capturedContinent));
            }
            if (_navLevel >= NavigationLevel.Region && HasSelectedRegion())
            {
                DrawCrumbSeparator(ref cx, y, rowH);
                int capturedContinent = _navContinentIndex;
                int capturedRegion = _navRegionGlobalIndex;
                DrawCrumb(ref cx, y, rowH, _navRegionName, _navLevel == NavigationLevel.Region,
                    () => NavigateToRegion(capturedContinent, capturedRegion));
            }
            if (_navLevel == NavigationLevel.Area && _navAreaIndex >= 0)
            {
                DrawCrumbSeparator(ref cx, y, rowH);
                DrawCrumb(ref cx, y, rowH, _navAreaName, true, null);
            }
        }

        private static void DrawCrumbSeparator(ref float cx, float y, float rowH)
        {
            const float sepWidth = 14f;
            GUI.Label(new Rect(cx, y, sepWidth, rowH), "/");
            cx += sepWidth;
        }

        private static void DrawCrumb(ref float cx, float y, float rowH, string label, bool isCurrent, Action onClick)
        {
            Vector2 size = GUI.skin.button.CalcSize(new GUIContent(label));
            float width = Mathf.Max(40f, size.x);
            var rect = new Rect(cx, y, width, rowH);
            if (isCurrent || onClick == null)
                GUI.Label(rect, label, GUI.skin.box);
            else if (GUI.Button(rect, label))
                onClick();
            cx += width + 2f;
        }

        private void DrawNavigationList(Rect area, float rowH)
        {
            if (_navPanelData == null) return;

            List<(string label, string meta, Action onClick)> items = BuildNavigationListItems();

            float contentHeight = Mathf.Max(area.height, items.Count * rowH);
            var viewRect = new Rect(0, 0, area.width - 20f, contentHeight);
            _navListScroll = GUI.BeginScrollView(area, _navListScroll, viewRect);
            for (int i = 0; i < items.Count; i++)
            {
                var (label, meta, onClick) = items[i];
                var rowRect = new Rect(0, i * rowH, viewRect.width, rowH);
                string display = string.IsNullOrEmpty(meta) ? label : $"{label}   ({meta})";
                if (onClick != null && GUI.Button(rowRect, display))
                    onClick();
                else if (onClick == null)
                    GUI.Label(rowRect, display);
            }
            GUI.EndScrollView();
        }

        /// <summary>
        /// A lista MINDIG a jelenlegi szint GYEREKEIT mutatja (amire tovább
        /// lehet zoomolni) - bolygón kontinensek, kontinensen régiók,
        /// régión területek. A terület a legmélyebb (levél) szint, ott a
        /// lista üres - a "Vissza" gomb és a breadcrumb innen is működik.
        /// </summary>
        private List<(string label, string meta, Action onClick)> BuildNavigationListItems()
        {
            var items = new List<(string, string, Action)>();
            if (_navLevel == NavigationLevel.Planet)
            {
                for (int i = 0; i < _navPanelData.Continents.Count; i++)
                {
                    int captured = i;
                    ContinentPanelData c = _navPanelData.Continents[i];
                    items.Add((c.Name, $"{c.LandmassClass}, {c.DominantBiome}, {c.AreaTiles} tile", () => NavigateToContinent(captured)));
                }
            }
            else if (_navLevel == NavigationLevel.Continent && _navContinentIndex >= 0)
            {
                List<RegionPanelData> regions = GetRegionsForContinentCached(_navContinentIndex);
                List<int> globalIndices = _navRegionGlobalIndicesByContinent[_navContinentIndex];
                for (int i = 0; i < regions.Count; i++)
                {
                    int capturedGlobal = globalIndices[i];
                    RegionPanelData r = regions[i];
                    items.Add((r.Name, $"{r.LandformType}, {r.AreaTiles} tile", () => NavigateToRegion(_navContinentIndex, capturedGlobal)));
                }
            }
            else if (_navLevel == NavigationLevel.Region && HasSelectedRegion())
            {
                List<AreaPanelData> areas = GetAreasForRegionCached(_navRegionGlobalIndex);
                for (int i = 0; i < areas.Count; i++)
                {
                    int captured = i;
                    AreaPanelData a = areas[i];
                    items.Add((a.Name, $"{a.LandformType}, {a.AreaTiles} tile", () => NavigateToArea(captured)));
                }
            }
            // NavigationLevel.Area: nincs tovabbi gyerek - a lista ures marad.
            return items;
        }

        /// <summary>
        /// A jobb oldali "kiválasztott elem" doboz - a jelenlegi navigációs
        /// szintnek megfelelő adatokat mutatja (bolygó/kontinens/régió/
        /// terület), mind a Core-forrású <see cref="WorldGenPanelData"/>-ból.
        /// </summary>
        private float DrawSelectedInfoPanel(float x, float y, float w, float rowH)
        {
            const int rowCount = 6; // a legtobb sort hasznalo (Continent) ag hataroz
            float boxHeight = rowH * (rowCount + 0.6f) + 16f;
            string title = "Kiválasztott: " + NavigationLevelLabel(_navLevel);
            GUI.Box(new Rect(x - 6f, y - 6f, w + 12f, boxHeight), title);
            float rowY = y + rowH * 0.6f;

            if (_navPanelData == null)
            {
                GUI.Label(new Rect(x, rowY, w, rowH), "Nincs adat - a Build() még nem futott le.");
                return boxHeight + 10f;
            }

            string[] lines;
            if (_navLevel == NavigationLevel.Planet)
            {
                WorldPanelData wd = _navPanelData.World;
                lines = new[]
                {
                    $"Név: {wd.Name}",
                    $"Óceán-borítottság: {wd.OceanCoveragePercent:F1}%",
                    $"Élhetőség: {wd.HabitabilityPercent:F1}% ({wd.HabitabilityLevel})",
                    $"Seed: {wd.SeedDisplay}",
                };
            }
            else if (_navLevel == NavigationLevel.Continent && _navContinentIndex >= 0)
            {
                ContinentPanelData c = _navPanelData.Continents[_navContinentIndex];
                lines = new[]
                {
                    $"Név: {c.Name}",
                    $"Típus: {c.LandmassClass}",
                    $"Terület: {c.AreaTiles} tile, {c.BiomeCount} biome",
                    $"Domináns biome: {c.DominantBiome}",
                    $"Folyótorkolat: {c.RiverMouthCount}, vízgyűjtő: {c.RiverBasinCount}",
                    $"Part-tagoltság: {c.CoastalComplexity:F2} ({c.CoastalComplexityLevel})",
                };
            }
            else if (_navLevel == NavigationLevel.Region && HasSelectedRegion())
            {
                List<RegionPanelData> regions = GetRegionsForContinentCached(_navContinentIndex);
                List<int> globalIndices = _navRegionGlobalIndicesByContinent[_navContinentIndex];
                int localIndex = globalIndices.IndexOf(_navRegionGlobalIndex);
                RegionPanelData r = regions[localIndex];
                var regionLines = new List<string>
                {
                    $"Név: {r.Name}",
                    $"Terület: {r.AreaTiles} tile",
                    $"Domináns biome: {r.DominantBiome}",
                    $"Morfológia: {r.LandformType}",
                    $"Folyótorkolat: {r.RiverMouthCount}",
                };
                // ND-117: a talaj-termékenység CSAK akkor jelenik meg, ha
                // ténylegesen kiszámolható (kell hozzá az évi középhőmérséklet,
                // amit a jég-klasszifikáció ad - `showLakesIce`). Az I4 szerint
                // inkább hiányzik a sor, mint hogy pótolt értéket mutasson.
                if (r.SoilFertility.HasValue)
                    regionLines.Add($"Talaj-termékenység: {r.SoilFertility.Value:F3} ({r.SoilFertilityLevel})");
                lines = regionLines.ToArray();
            }
            else if (_navLevel == NavigationLevel.Area && _navAreaIndex >= 0)
            {
                List<AreaPanelData> areas = GetAreasForRegionCached(_navRegionGlobalIndex);
                AreaPanelData a = areas[_navAreaIndex];
                lines = new[]
                {
                    $"Név: {a.Name}",
                    $"Terület: {a.AreaTiles} tile",
                    $"Domináns biome: {a.DominantBiome}",
                    $"Morfológia: {a.LandformType}",
                };
            }
            else
            {
                lines = new[] { "Nincs kiválasztott elem." };
            }

            foreach (string line in lines)
            {
                GUI.Label(new Rect(x, rowY, w, rowH), line);
                rowY += rowH;
            }

            return boxHeight + 10f;
        }

        /// <summary>
        /// FELHASZNÁLÓI KÉRÉS (2026-09-13, ld. docs/backlog.md "Navigációs
        /// menü és panel-elrendezés"): a Deep time doboz fölé kerülő, önálló
        /// "Kamera" doboz - nézetszint, felszín feletti magasság (Unity
        /// egységben, NEM km - a km-es átváltás önálló, még nyitott feladat,
        /// ld. "Precíz, kamerafüggő kilométeres léptékcsík" backlog-sor; itt
        /// nem szabad hamis pontosságú km-számot mutatni) és a nézetirány.
        /// Minden érték a MÁR MEGLÉVŐ <see cref="PlanetOrbitCamera"/> publikus
        /// állapotából jön (I4) - nincs itt kitalált vagy újraszámolt adat.
        /// Visszaadja a doboz teljes magasságát (a hívó ehhez képest tolja
        /// lejjebb a Deep time dobozt).
        /// </summary>
        private float DrawCameraStatePanel(float x, float y, float width, float rowHeight)
        {
            if (_hudOrbitCamera == null)
                _hudOrbitCamera = FindObjectOfType<PlanetOrbitCamera>();

            const float headerGap = 0.6f;
            const int rowCount = 2;
            float boxHeight = rowHeight * (rowCount + headerGap) + 10f;
            GUI.Box(new Rect(x - 6f, y - 6f, width + 12f, boxHeight), "Kamera állása");

            float rowY = y + rowHeight * headerGap;
            if (_hudOrbitCamera == null)
            {
                GUI.Label(new Rect(x, rowY, width, rowHeight), "Nincs PlanetOrbitCamera a jelenetben.");
                return boxHeight + 10f;
            }

            Vector3 dir = _hudOrbitCamera.CurrentViewDirection;
            GUI.Label(
                new Rect(x, rowY, width, rowHeight),
                $"Nézetszint: {_hudOrbitCamera.CurrentViewLevel}   " +
                $"Magasság: {_hudOrbitCamera.AltitudeAboveSurface:F2} (Unity egység)");
            rowY += rowHeight;
            GUI.Label(
                new Rect(x, rowY, width, rowHeight),
                $"Nézetirány: ({dir.x:F3}, {dir.y:F3}, {dir.z:F3})");

            return boxHeight + 10f;
        }

        private void DrawDeepTimeStepButtons(float x, float y, float width, float rowHeight, double direction)
        {
            float buttonWidth = width / DeepTimeButtonsPerRow;
            string sign = direction < 0.0 ? "-" : "+";
            for (int i = 0; i < DeepTimeStepLabels.Length; i++)
            {
                int row = i / DeepTimeButtonsPerRow;
                int column = i - row * DeepTimeButtonsPerRow;
                if (GUI.Button(
                    new Rect(x + column * buttonWidth, y + row * rowHeight, buttonWidth, rowHeight),
                    sign + DeepTimeStepLabels[i]))
                {
                    ApplyDeepTimeStep(direction * DeepTimeStepMyr[i]);
                }
            }
        }

        private void ApplyDeepTimeStep(double deltaMyr)
        {
            double maxMyr = Math.Max(0.0, onScreenDeepTimeMaxMyr);
            deepTimeMyr = Math.Max(0.0, Math.Min(maxMyr, deepTimeMyr + deltaMyr));
            deepTimeSliderMyr = (float)deepTimeMyr;
            _lastDeepTimeSliderMyr = deepTimeSliderMyr;
            _gyrInputParsedValue = deepTimeMyr / 1000.0;
            _gyrInputText = FormatDeepTimeGyr(_gyrInputParsedValue);
        }

        private static string FormatDeepTimeGyr(double value)
        {
            // Kilenc tizedes Gyr-ben pontosan megjeleniti az 1 eves lepest is.
            return value.ToString("0.#########", CultureInfo.InvariantCulture);
        }

        [ContextMenu("Rebuild")]
        public void Build()
        {
            CancelRiverRefinement();
            SynchronizePhysicalReliefScale();
            CancelStagedTerrainUpload();
            // A nyilvános Build-gomb se üríthesse a worker által használt
            // világ-/sarokcache-eket. A kérés a single-flight után teljesül.
            if (_cutTask != null)
            {
                _fullBuildRequestedAfterCut = true;
                return;
            }
            _fullBuildRequestedAfterCut = false;
            // TELJESITMENY-DIAGNOSZTIKA: Build() a VILAGOT MEGHATAROZO
            // parameterek (pl. deepTimeMyr) barmelyikenek valtozasakor teljes
            // egeszeben ujrafut (WorldConfigChangedSinceBuild). A felhasznalo
            // ~1 perces ujraszamolast jelzett - ez a szakaszos PerfLog megmutatja,
            // MELYIK fazis viszi az idot a kovetkezo Unity-futasnal (Logs/PerfLog_*.txt).
            var buildTotalStopwatch = Stopwatch.StartNew();
            var buildPhaseStopwatch = Stopwatch.StartNew();

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
            PerfLog($"Build() seeds+craters={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms craters.Count={craters.Count}");
            buildPhaseStopwatch.Restart();

            // A MAR verifikalt M4/M7/M11 Core-modulokat hivjuk kozvetlenul -
            // nincs duplikalt elevation-/folyoszamitas.
            Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationFieldAtTime(seed, plateCount, level, deepTimeMyr);
            // A becsapodas mezo-hatasa a MAR kiszamolt krater-listaval, a Core
            // ImpactCratering.ApplyToField-jen keresztul - igy a mezo-hatas
            // BITRE ugyanaz a fuggveny (ElevationDelta), mint a sarok/tile-kozep
            // (ComputeElevationAtPoint) kiertekelese, es a viewer NEM duplikal
            // szimulacios matekot (korabban ez a ciklus inline volt).
            field = ImpactCratering.ApplyToField(field, craters);

            // M10 deep-time erozio a tile-KOZEPU mezore is (nem csak a
            // renderelt geometriara): igy a tengerszint-kalibracio ES az ocean/
            // folyo/to/jeg besorolas UGYANAZT az erodalt domborzatot latja, mint
            // a ComputeElevationAtPoint-bol szarmazo sarok-geometria. A
            // field[tile] = baseElev(+jitter) + uplift (+crater); CSAK az uplift-
            // reszt relaxaljuk (+= relaxedUplift - uplift), a base/jitter/crater
            // valtozatlan. Ugyanaz a Core-keplet, mint ComputeElevationAtPoint-ban.
            _adaptiveErosionTimeMyr = showDeepTimeErosion ? deepTimeMyr : 0.0;
            field = ApplyDeepTimeErosionToField(field, seed, seeds, _adaptiveErosionTimeMyr);
            PerfLog($"Build() elevation+crater+erosion(level={level}, tiles={field.Count})={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            buildPhaseStopwatch.Restart();

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
            // A fuggoleges tulrajzolas (terrainReliefExaggeration) a tengerszintre
            // pivotal, ezert a _adaptiveSeaLevel-nek MAR a (regi, teljes) Build()
            // vertex-szamitasa (ToDisplacedVector3 -> ComputeDisplacedRadius) elott
            // ervenyesnek kell lennie - nem csak a kesobbi (798) beallitaskor.
            _adaptiveSeaLevel = seaLevel;
            // UGYANEZ AZ OK a seed/seeds/craters mezokre: a statikus teljes-mesh
            // ag (lentebbi face-ciklus) a ContinuousCornerColor-on at MAR ezekre
            // tamaszkodhat (szel-overlay: ComputeElevationAtPoint), tehat a
            // kesobbi (lenti) beallitas elott, MAR itt ervenyesnek kell lenniuk -
            // kulonben a szel-overlay-ben null _adaptiveSeeds -> NullReference.
            _adaptiveSeed = seed;
            _adaptiveSeeds = seeds;
            _adaptiveCraters = craters;
            // _adaptiveErosionTimeMyr MAR beallitva fentebb (a mezo-erozio elott),
            // hogy a sarok/adaptiv ComputeElevationAtPoint es a mezo UGYANAZT a
            // relaxacios idot lassa.
            Dictionary<TileId, bool> isOceanField = FlowNetwork.ComputeOceanField(field, seaLevel);
            PerfLog($"Build() seaLevel+oceanField={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms seaLevel={seaLevel:F1}");
            buildPhaseStopwatch.Restart();

            // A regi, level-8-as folyo-tile/parent mezoket mar sem a render,
            // sem a panel nem fogyasztja: a render a dendritikus halozatot
            // hasznalja, a panel pedig a sajat level-5 referencia-floodjat
            // szamolja. Uresen tartjuk oket a kesobbi teljes eltavolitasig.
            HashSet<TileId> riverTiles = new HashSet<TileId>();
            HashSet<TileId> lakeTiles = new HashSet<TileId>();
            Dictionary<TileId, TileId> riverParent = null;
            Dictionary<TileId, double> lakeSurface = null;
            double hydroFieldMs = 0.0;
            double hydroFloodMs = 0.0;
            double hydroLakesMs = 0.0;
            double hydroTerrainBasisMs = 0.0;
            double hydroTopologyMs = 0.0;
            double hydroDenseKernelMs = 0.0;
            double hydroDenseConversionMs = 0.0;
            bool hydroTerrainBasisReused = false;
            bool hydroTopologyReused = false;
            bool denseFloodUsed = false;
            var hydrologySubphaseStopwatch = Stopwatch.StartNew();
            if (showLakesIce)
            {
                // A tavak DEDIKÁLT, FINOMABB szinten (hydrologyLevel)
                // számolódnak - a durva megjelenítési `level`-től FÜGGETLENÜL.
                // A mezőre UGYANAZT alkalmazzuk (kráter + erózió), mint a
                // megjelenítésire, és a KÖZÖS tengerszintet használjuk a
                // konzisztens partvonalhoz.
                int hydroLevel = Mathf.Clamp(hydrologyLevel, level, 8);
                Dictionary<TileId, double> hydroField;
                Dictionary<TileId, bool> hydroOcean;
                double[]? hydroDenseField = null;
                bool[]? hydroDenseOcean = null;
                if (hydroLevel == level)
                {
                    hydroField = field;
                    hydroOcean = isOceanField;
                }
                else
                {
                    hydroTerrainBasisReused = EnsureTileCenterTerrainBasisCache(seed, hydroLevel);
                    hydroTerrainBasisMs = hydrologySubphaseStopwatch.Elapsed.TotalMilliseconds;
                    hydroField = BuildElevationFieldFromCachedTileCenters(
                        seed, seeds, craters, _adaptiveErosionTimeMyr, hydroLevel,
                        out hydroDenseField);
                    hydroOcean = FlowNetwork.ComputeOceanField(hydroField, seaLevel);
                    hydroDenseOcean = new bool[hydroDenseField.Length];
                    for (int i = 0; i < hydroDenseField.Length; i++)
                        hydroDenseOcean[i] = hydroDenseField[i] < seaLevel;
                }
                hydroFieldMs = hydrologySubphaseStopwatch.Elapsed.TotalMilliseconds;

                hydrologySubphaseStopwatch.Restart();
                // A `filledForLakes` CSAK a Dictionary-alapu to-detektalashoz
                // kell; a suru agon vegig tombbel dolgozunk (ld. lentebb).
                Dictionary<TileId, double>? filledForLakes = null;
                double[]? denseFilledForLakes = null;
                FlowNetwork.DenseGridTopology? lakeTopology = null;
                if (hydroDenseField != null && hydroDenseOcean != null)
                {
                    hydroTopologyReused = _hydrologyDenseTopology != null
                        && _hydrologyDenseTopology.Level == hydroLevel;
                    if (!hydroTopologyReused)
                        _hydrologyDenseTopology = FlowNetwork.DenseGridTopology.Create(hydroLevel);
                    hydroTopologyMs = hydrologySubphaseStopwatch.Elapsed.TotalMilliseconds;
                    lakeTopology = _hydrologyDenseTopology!;

                    FlowNetwork.DenseFloodResult denseFlood = FlowNetwork.PriorityFloodDense(
                        lakeTopology, hydroDenseField, hydroDenseOcean);
                    hydroDenseKernelMs = hydrologySubphaseStopwatch.Elapsed.TotalMilliseconds - hydroTopologyMs;
                    // A korabbi `ToFilledDictionary` KIMARAD: a feltoltott mezo
                    // egyetlen fogyasztoja a to-detektalas volt, ami mostantol
                    // tombindexelt. Elesben MERVE 12-14 ms/Build tiszta veszteseg.
                    denseFilledForLakes = denseFlood.Filled;
                    hydroDenseConversionMs = 0.0;
                    denseFloodUsed = true;
                }
                else
                {
                    FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(hydroField, hydroOcean);
                    filledForLakes = flood.Filled;
                }
                hydroFloodMs = hydrologySubphaseStopwatch.Elapsed.TotalMilliseconds;

                hydrologySubphaseStopwatch.Restart();
                // Beltavak: a 'filled' es az eredeti mezo kulonbsege > kuszob,
                // szarazfoldon (LakesIceErosion.IdentifyLakes, §33 topografiai to-detektalas).
                // SZURES: csak a JELENTOS tavak (eleg nagy tile-szam ES eleg mely) -
                // igy eltunik a sok apro, blokkos helyi melyedes (zaj). A tavakat
                // LAPOS vizfelszinkent rajzoljuk a feltoltesi szinten (BuildLakeSurface),
                // ezert per-tile eltaroljuk a to-felszin (flat) elevaciojat.
                // TOMBINDEXELT ut, ha van suru adat: a Dictionary-valtozat
                // koltsegenek nagy resze nem a to-kereses, hanem a koritese - a
                // kulcsok (face,u,v) szerinti RENDEZESE es harom eredmeny-
                // Dictionary felepitese. A ket ut kimenete BITRE azonos (a suru
                // index definicio szerint ugyanaz a lexikografikus sorrend),
                // amit a DenseLakeEquivalenceTests tobb vilagon, tobb kuszobbel
                // es a tile-listak ELEMSORRENDJEIG igazol.
                // MERVE (level 7, 98 304 tile): 17,9 ms -> 1,0 ms (18,3x);
                // a naplo szerint elesben a `lakes=` sor 180-190 ms volt.
                List<LakesIceErosion.LakeInfo> lakeInfos =
                    denseFilledForLakes != null && lakeTopology != null
                        && hydroDenseField != null && hydroDenseOcean != null
                    ? LakesIceErosion.IdentifyLakesDense(
                        lakeTopology, hydroDenseField, denseFilledForLakes, hydroDenseOcean).Lakes
                    : LakesIceErosion.IdentifyLakes(hydroField, filledForLakes!, hydroOcean).Lakes;
                lakeSurface = new Dictionary<TileId, double>();
                foreach (LakesIceErosion.LakeInfo lake in lakeInfos)
                {
                    if (lake.TileCount < minLakeTiles || lake.MaxDepth < minLakeDepthMeters)
                        continue;
                    foreach (TileId t in lake.Tiles)
                    {
                        lakeTiles.Add(t);
                        lakeSurface[t] = lake.SurfaceElevation;
                    }
                }
                hydroLakesMs = hydrologySubphaseStopwatch.Elapsed.TotalMilliseconds;
            }
            PerfLog(
                $"Build() hydrology(hydroLevel={Mathf.Clamp(hydrologyLevel, level, 8)}, " +
                $"coarseRiversSkipped=True, lakes={lakeTiles.Count})={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms " +
                $"[field={hydroFieldMs:F1}ms terrainBasis={hydroTerrainBasisMs:F1}ms " +
                $"(reused={hydroTerrainBasisReused}) denseFlood={denseFloodUsed} " +
                $"topology={hydroTopologyMs:F1}ms (reused={hydroTopologyReused}) " +
                $"flood={hydroFloodMs:F1}ms (kernel={hydroDenseKernelMs:F1}ms, " +
                $"conversion={hydroDenseConversionMs:F1}ms) lakes={hydroLakesMs:F1}ms " +
                $"(dense={denseFloodUsed})]");
            buildPhaseStopwatch.Restart();

            double axialTiltRad = climateAxialTiltDegrees * Math.PI / 180.0;
            // Ugyanaz az ok, mint a seed/seeds/craters-nel: a statikus viz-ag
            // (ContinuousWaterCornerColor) a szel-overlay-ben MAR ezt hasznalja.
            _adaptiveAxialTiltRad = axialTiltRad;
            // ND-64: a napi 24 Nap-irany minden tile-nal azonos. Egyszer
            // allitjuk elo, a klasszifikacio csak a pontonkenti dot-productokat
            // es a valtozatlan homerseklet-kepletet futtatja.
            _adaptiveDailyInsolationSamples = DailyInsolationSampleDirections.Create(
                climateDayT, climateOrbitalPeriodDays, climateRotationPeriodDays, axialTiltRad);

            // M7 allando jegtakaro: az EVES homerseklet-statisztikabol
            // (LakesIceErosion.AnnualTemperatureStats + ClassifyIce) a referencia-
            // szinten EGYSZER. Draga (12 mintavetel/tile, mindegyik teljes
            // Temperature-lanc), ezert csak itt (Build), es csak szarazfoldre -
            // az oceani jeget mar a biome (SeaIce) adja. A finomabb (level feletti)
            // tile-ok az os referencia-tile jeg-allapotat oroklik (IsAdaptiveIceTile).
            //
            // M10 eljegesedes-ciklus (ND-44, DeepTimeErosionGlaciation.GlobalTempOffset):
            // egy periodikus globalis homerseklet-eltolast adunk MINDEN mintahoz,
            // pontosan ugy, ahogy az ND-44 dokumentacioja eloirja ("t=0-nal az
            // eltolas 0, ha valaki hozzaadja az eltolast"). deepTimeMyr=0-nal
            // GlobalTempOffset(0)=amplitude*sin(0)=0 PONTOSAN (bizonyitva:
            // DeepTimeErosionGlaciationTests.GlobalTempOffsetZeroAtStart), tehat
            // a t=0 allando jegtakaro-besorolas BITRE valtozatlan marad - csak
            // t>0-nal kezd a sarki jegsapka a ciklus szerint hullamzani. Az
            // erozio-kapcsolotol (showDeepTimeErosion) FUGGETLENUL, a NYERS
            // deepTimeMyr-t hasznalja - a ket alrendszer ND-44 szerint
            // szandekosan NINCS osszekapcsolva. HATOKOR: csak a SZARAZFOLDI
            // allando jegtakaro-besorolast erinti (mint eddig is), a tengeri jeg
            // (SeaIce) es az altalanos biome-hatarok (tundra/tropusi stb.) NEM
            // mozdulnak ezzel egyutt - ez tudatos hatokor-szukites, kulon
            // munka lenne a Temperature.TemperatureKelvin osszes hivasi helyere
            // athuzni az eltolast.
            double glaciationOffsetK = DeepTimeErosionGlaciation.GlobalTempOffset(deepTimeMyr);
            // MINDEN szarazfoldi referencia-tile-ra taroljuk az eves atlag-
            // homersekletet (nem csak a mar PermanentIce-nek minosulteket) -
            // a tenyleges kuszob-osszevetes leaf-szinten, zajjal perturbalva
            // tortenik (IsAdaptiveIceTile), igy egy hatarhoz kozeli tile
            // gyermekei kozott egyesek jegesnek, masok nem-jegesnek
            // minosulhetnek a referencia-szintu atlag KORUL.
            Dictionary<TileId, double> iceMeanK = new Dictionary<TileId, double>();
            var landMeanTemperatureK = new Dictionary<TileId, double>();
            if (showLakesIce)
            {
                foreach (KeyValuePair<TileId, bool> kv in isOceanField)
                {
                    if (kv.Value) continue; // ocean -> SeaIce a biome-bol, nem itt
                    TileId t = kv.Key;
                    TileGeometry.ToPosition(t, out double ix, out double iy, out double iz);
                    LakesIceErosion.AnnualTemperatureStats(ix, iy, iz,
                        climateOrbitalPeriodDays, climateRotationPeriodDays, axialTiltRad,
                        false, field[t], seaLevel, out double meanK, out _, out _);
                    iceMeanK[t] = meanK + glaciationOffsetK;
                    // ND-117: a NYERS evi kozephomerseklet (eltolas NELKUL) a
                    // regolit-modell bemenete is. MERVE: ez a ciklus 8602
                    // szarazfold-tile-ra 936 ms level=6-on - messze a legdragabb
                    // resze a talaj-lancnak. Mivel a jeg-klasszifikacio ugyis
                    // kiszamolja, ELTESSZUK, es a panel innen dolgozik: igy a
                    // Soil fertility tenyleges tobbletkoltsege a Build()-ben
                    // csak a statikus erozios passz (~36 ms), nem ~1000 ms.
                    landMeanTemperatureK[t] = meanK;
                }
            }
            _lastLandMeanTemperatureK = landMeanTemperatureK;
            PerfLog($"Build() ice(iceTiles={iceMeanK.Count})={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            buildPhaseStopwatch.Restart();

            // Kulcs = (RenderCategory, bucket). A legtobb kategorianal bucket
            // mindig 0 (egyetlen lapos szin); az RenderCategory.Ocean-nal a
            // bucket a MEGLEVO, mar kiszamolt tengerfenek-elevaciobol
            // (OceanRockBucket) szarmazo finom feny/sotet variacio indexe -
            // igy a tengerfenek nem teljesen egyszinu, de tovabbra sem kell
            // uj szimulacios adat vagy per-vertex szin/shader.
            var verticesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var normalsByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var trianglesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<int>>();
            var colorsByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Color>>();

            // A vizfelszin KULON, tile-racsbol epul, de a RenderCategory-tol
            // fuggetlen buckettel (WaterDepthBucket, a helyi melysegbol) -
            // lasd BuildWaterSurface. Nem resze a fenti verticesByKey-nek,
            // mert a vizfelszin egy MASODIK, a tengerfenek folott ulo geometriai
            // reteg (kulon GameObject/mesh), nem egy tovabbi RenderCategory.
            var waterVerticesByBucket = new Dictionary<int, List<Vector3>>();
            var waterNormalsByBucket = new Dictionary<int, List<Vector3>>();
            var waterTrianglesByBucket = new Dictionary<int, List<int>>();
            var waterColorsByBucket = new Dictionary<int, List<Color>>();
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
            var cornerNormalCache = new Dictionary<(int Face, uint CornerU, uint CornerV), Vector3>();

            int n = 1 << level;
            for (int face = 0; face <= 5; face++)
            {
                for (uint u = 0; u < n; u++)
                {
                    for (uint v = 0; v < n; v++)
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.ToPosition(id, out double cx, out double cy, out double cz);

                        double elevation = field[id];
                        bool isOceanic = isOceanField[id];
                        double temperatureK = TemperatureKelvinAt(cx, cy, cz, axialTiltRad, isOceanic, elevation, seaLevel);
                        Biome biome = BiomeClassification.Classify(
                            temperatureK, isOceanic, PrecipitationAtCore(cx, cy, cz), _adaptiveBiomeThresholds);
                        biomeOf[id] = biome;

                        // TELJESITMENY (2026-09-10, felhasznaloi keres, cel <1s
                        // teljes Build()-re): ha useAdaptiveLod be van kapcsolva,
                        // az EGESZ lenti geometria-epites (sarkok, AddQuad, viz-
                        // felszin, hatarok) ELDOBODIK, mert a BuildStaticBaseLayer()
                        // RONGTON felulirja a gameObject mesh-et (ld. lejjebb a
                        // "MEGJEGYZES" kommentet) - a PerfLog korabban mert 24s-os
                        // Build()-bol ~1.5s (a legdragabb fix, kockazatmentes
                        // resze) volt ez a PAZAROLT munka. Csak a fenti biomeOf[id]
                        // (M8 panel-statisztikahoz kell) marad KOTELEZO - a tobbi
                        // (kraterek/tavak/jeg-kategoria, sarok-pozicio/normal/szin,
                        // AddQuad, vizfelszin, hatarvonal) at van ugorva.
                        if (useAdaptiveLod)
                            continue;

                        TileGeometry.GetContinuousBounds(id, out double uMin, out double uMax, out double vMin, out double vMax);

                        // M11: a becsapodas-erintett tile-ok kulon kategoriaba
                        // kerulnek (a folyo-highlight mintajat kovetve), MERT
                        // a jelenlegi racsfelbontason (LOD 5-7, tile-ok
                        // ~90-3000 km) a legtobb kis krater (1-100 km) nem
                        // mozdit el egyetlen tile-sarkot sem eszreveheto
                        // mertekben - a kategoria-jeloles igy is lathatova
                        // teszi a ritka talalatokat, fuggetlenul a
                        // felbontas-korlattol (ld. ImpactCratering.ApplyToField).
                        bool isCratered = craters.Count > 0 && ImpactCratering.IsInsideAnyCrater(cx, cy, cz, craters);
                        // A folyok MOSTANTOL kulon VONAL-retegkent (BuildRiverNetwork)
                        // renderelodnek, NEM tile-kategoriakent - ezert itt nincs
                        // River-agkitoltes (a folyo alatti tile a biome-szinet mutatja).
                        bool isLake = lakeTiles.Contains(id);
                        // Ez a statikus (nem-adaptiv) alapreteg PONTOSAN a
                        // referencia-szinten renderel (id == referencia-tile),
                        // ezert ugyanazt a zajos-kuszob kepletet szamoljuk itt
                        // helyben (lasd IsAdaptiveIceTile doksija), nem
                        // hasznalhatjuk magat a metodust, mert az a MAR
                        // BEALLITOTT _adaptiveIceMeanK/_adaptiveSeed mezokre
                        // tamaszkodik, amik csak a Build() VEGEN allnak be.
                        bool isIce = iceMeanK.TryGetValue(id, out double iceRefMeanK)
                            && iceRefMeanK + IceBoundaryJitterAmplitudeK * FractalNoise.Fbm(
                                seed, cx, cy, cz, IceBoundaryJitterFrequency, IceBoundaryJitterOctaves)
                                < LakesIceErosion.PermanentIceMeanThresholdK;
                        // ND-57: ugyanaz a jitter-minta, mint a fenti isIce-nal,
                        // a tengeri jeg (Ocean/SeaIce) hataranak - ld. IsAdaptiveSeaIce doksija.
                        bool isSeaIceRendered = isOceanic && (temperatureK
                            + IceBoundaryJitterAmplitudeK * FractalNoise.Fbm(
                                seed, cx, cy, cz, IceBoundaryJitterFrequency, IceBoundaryJitterOctaves)
                            < BiomeClassification.OceanFreezingK);
                        // ND-59: a nyers `biome`-fallback helyett jitterelt
                        // valtozatot hasznalunk - ld. JitteredRenderBiome doksi
                        // (kulonben a szarazfoldi jeg/tundra hatar is
                        // szabalyos, latitude-szimmetrikus kor lenne a polusnal).
                        RenderCategory category = isCratered ? RenderCategory.Crater
                            : isIce ? RenderCategory.IceSheet
                            : isLake ? RenderCategory.Lake
                            : isOceanic ? (isSeaIceRendered ? RenderCategory.SeaIce : RenderCategory.Ocean)
                            : ToRenderCategory(JitteredRenderBiome(cx, cy, cz, temperatureK, isOceanic, seed));

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

                        GetOrAddLists(verticesByKey, normalsByKey, trianglesByKey, colorsByKey, key,
                            out List<Vector3> vertices, out List<Vector3> normals, out List<int> triangles, out List<Color> colors);
                        // ND-55: a lejto-erzekeny, DE tile-hatarokon folytonos
                        // normal - ld. ComputeCornerNormalViaFiniteDifference.
                        Vector3 pn00 = GetOrComputeCornerNormal(cornerNormalCache, face, u, v, n, seed, seeds, craters);
                        Vector3 pn10 = GetOrComputeCornerNormal(cornerNormalCache, face, u + 1, v, n, seed, seeds, craters);
                        Vector3 pn11 = GetOrComputeCornerNormal(cornerNormalCache, face, u + 1, v + 1, n, seed, seeds, craters);
                        Vector3 pn01 = GetOrComputeCornerNormal(cornerNormalCache, face, u, v + 1, n, seed, seeds, craters);
                        if (IsContinuousTerrainCategory(category))
                        {
                            Color cc00 = ContinuousCornerColor(p00, isOceanic, seaLevel, axialTiltRad);
                            Color cc10 = ContinuousCornerColor(p10, isOceanic, seaLevel, axialTiltRad);
                            Color cc11 = ContinuousCornerColor(p11, isOceanic, seaLevel, axialTiltRad);
                            Color cc01 = ContinuousCornerColor(p01, isOceanic, seaLevel, axialTiltRad);
                            AddQuad(vertices, normals, triangles, colors, cc00, cc10, cc11, cc01, pn00, pn10, pn11, pn01, p00, p10, p11, p01);
                        }
                        else
                        {
                            Color cUniform = CategoryColor(category, bucket);
                            AddQuad(vertices, normals, triangles, colors, cUniform, cUniform, cUniform, cUniform, pn00, pn10, pn11, pn01, p00, p10, p11, p01);
                        }

                        // M13: vizfelszin a KALIBRALT tengerszint sugaranal,
                        // MINDEN oceani tile fole (nyilt viz ES tengeri jeg).
                        // A SeaIce is a tengerszinten, laposan ul (jegszinnel) -
                        // korabban a sajat MELY fenek-magassagan renderelodott
                        // vizreteg nelkul, ezert godorkent latszott a polusnal
                        // (ld. EmitAdaptiveTile azonos javitasa).
                        if (isOceanic && (biome == Biome.Ocean || biome == Biome.SeaIce))
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
                                waterColorsByBucket[waterBucket] = new List<Color>();
                            }
                            // ND-60 (felhasznaloi visszajelzes: "az ocean szinet ne
                            // befolyasolja, hogy milyen kozel van a polushoz"): a
                            // korabbi isSeaIceRendered ? feher : kek valasztas a
                            // DURVA (level=5) statikus racson egy latitude-tisztan
                            // kor alaku, eles hatart adott (bizonyitottan: sem K-
                            // jitter, sem pozicio-warp nem tudta megtorni, ld.
                            // docs/04-decisions.md ND-60 reszletei). A viz szine
                            // mostantol MINDIG a valodi melysegbol jon, fuggetlenul
                            // a homerseklettol/szelessegtol.
                            Color wc00 = ContinuousWaterCornerColor(p00, seaLevel);
                            Color wc10 = ContinuousWaterCornerColor(p10, seaLevel);
                            Color wc11 = ContinuousWaterCornerColor(p11, seaLevel);
                            Color wc01 = ContinuousWaterCornerColor(p01, seaLevel);
                            AddQuad(waterVerts, waterNormalsByBucket[waterBucket], waterTrianglesByBucket[waterBucket],
                                waterColorsByBucket[waterBucket], wc00, wc10, wc11, wc01, wp00, wp10, wp11, wp01);
                        }

                        if (showBorders)
                        {
                            int b = borderVerts.Count;
                            borderVerts.Add(p00); borderVerts.Add(p10); borderVerts.Add(p11); borderVerts.Add(p01);
                            borderIndices.Add(b + 0); borderIndices.Add(b + 1);
                            borderIndices.Add(b + 1); borderIndices.Add(b + 2);
                            borderIndices.Add(b + 2); borderIndices.Add(b + 3);
                            borderIndices.Add(b + 3); borderIndices.Add(b + 0);
                        }
                    }
                }
            }

            // MEGJEGYZES: ha useAdaptiveLod be van kapcsolva, ezt a fix-
            // szintu (`level`) mesh-et LENT a BuildStaticBaseLayer() rogton
            // felulirja `this.gameObject`-en (adaptiveBaseLevel-en epul ujra) -
            // ez a hivas csak akkor marad a vegso eredmeny, ha useAdaptiveLod
            // KI van kapcsolva (M9 elotti viselkedes, valtozatlanul).
            BuildMultiMaterialMesh(verticesByKey, normalsByKey, trianglesByKey, colorsByKey, gameObject);
            BuildBorders(borderVerts, borderIndices, "Borders");
            BuildCraterMarkers(craters, seed, seeds);
            BuildWaterSurface(waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket, waterColorsByBucket, "WaterSurface");
            PerfLog(useAdaptiveLod
                ? $"Build() biome-only adaptive preparation(level={level}, tiles={n * n * 6})={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms"
                : $"Build() legacy geometry loop(level={level}, tiles={n * n * 6})={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            buildPhaseStopwatch.Restart();

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
            _adaptiveRiverParent = riverParent;
            _adaptiveLakeTiles = lakeTiles;
            _adaptiveLakeSurface = lakeSurface;
            _adaptiveIceMeanK = iceMeanK;
            _adaptiveAxialTiltRad = axialTiltRad;

            // M5 csapadek-mezo (MoisturePrecipitation nedvesseg-advekcio) - a
            // csapadek-overlayhez, a dendritikus folyo-halozat forras-
            // kivalasztasahoz (RiverPathTracing.SelectRiverSources) ES a
            // felho-reteghez (BuildClouds) EGYARANT kell, ezert MOST BARMELYIK
            // felhasznalasi mod eseten kiszamoljuk (draga: per-tile szel + 24
            // advekcios iteracio a referencia-szinten). A Core-fuggveny a
            // sajat (t=0, krater/erozio nelkuli) mezojen szamol - klima-
            // kozelites, nem a deepTime-eltolt domborzatbol; ez egy vizualis/
            // forras-kivalasztasi reteg, nem a vilagmodell resze.
            // ND-126 ota a csapadek-mezo MINDIG kell: a biome-osztalyozas
            // bemenete lett (korabban csak az overlay/folyok/felhok kertek).
            // A gyorsitotar miatt ez cache-talalatnal 0,0 ms, hidegen
            // 300-400 ms - a Build() tobbi reszehez kepest kicsi, es
            // enelkul a szarazfoldi biome-ok nem szamolhatok ki.
            const bool needsPrecipField = true;
            MoisturePrecipitation.PrecipitationField precipField = needsPrecipField
                ? GetOrComputePrecipitationField(seed, plateCount, level, targetWaterFraction)
                : null;
            _adaptivePrecip = precipField?.Precipitation;
            // ND-130: a sarok-tabla MEG a vagopontok elott - a kuszoboket
            // UGYANABBOL a (mar interpolalt) fuggvenybol kell szamolni, amivel
            // kesobb osztalyozunk, kulonben a 20/45/75 percentilis mas
            // eloszlasra vonatkozna, mint amit a biome-dontes lat.
            BuildPrecipitationCornerTable(precipField);

            // ND-126: a vagopontok CSAK a VEGETALT szarazfoldbol - az oceani
            // ertekek benne torzitanak (a nedvesseg az ocean folott
            // keletkezik), a hideg szarazfoldeket pedig a homerseklet donti
            // el, a csapadekuk viszont 0 koruli. Ld.
            // ComputeThresholdsForVegetatedLand doksi a mert indoklassal.
            if (precipField != null)
            {
                var landSamples = new List<(double TemperatureK, double Precipitation)>(
                    precipField.Precipitation.Count);
                foreach (KeyValuePair<TileId, double> pkv in precipField.Precipitation)
                {
                    if (precipField.IsOcean[pkv.Key]) continue;
                    TileGeometry.ToPosition(pkv.Key, out double px, out double py, out double pz);
                    double pt = TemperatureKelvinAt(px, py, pz, axialTiltRad, false,
                        precipField.Elevation[pkv.Key], precipField.SeaLevel);
                    // ND-130: a NYERS `pkv.Value` helyett az INTERPOLALT ertek a
                    // tile kozepen - ez az, amit a biome-osztalyozas is latni fog.
                    landSamples.Add((pt, PrecipitationAtCore(px, py, pz)));
                }
                _adaptiveBiomeThresholds =
                    BiomeClassification.ComputeThresholdsForVegetatedLand(landSamples);
            }
            _lastPrecipField = precipField;
            PerfLog($"Build() precipitation(enabled={needsPrecipField}, "
                + $"cacheHit={(needsPrecipField ? _precipCacheHit.ToString() : "n/a")})"
                + $"={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            buildPhaseStopwatch.Restart();

            // HIBAJAVITAS (code-review-ban feltart hianyossag, 2026-09-06): a
            // Build() korabban SOHA nem novelte a `_cloudRebuildGeneration`-t,
            // szemben a folyo-finomitas `_riverRefinementGeneration`-jevel,
            // ami MINDEN Build()-ben bumpolodik - emiatt egy MEG FUTO
            // hatterszalas felho-ujraszamitas (a periodikus sodrodas-tick
            // inditotta) a Build() UTAN is "aktualisnak" tunt a sajat
            // elavulas-ellenorzese szerint, es CSENDBEN felulirhatta a MOST
            // frissen epitett, korrekt felho-reteget egy regi (Build() elotti
            // vilagallapotra vonatkozo) eredmennyel. A generacio-bumpolassal
            // (es a task-referencia elengedesevel - a MEG futo Task.Run maga
            // nem allithato meg, de az eredmenye mostantol felismerhetoen
            // elavult lesz) ez a hibaosztaly kizarva, UGYANUGY mint a folyo-
            // finomitasnal.
            _cloudRebuildGeneration++;
            _cloudRebuildTask = null;

            // M6/M13 felho-reteg MVP - ld. showClouds doksija: a suruseget a
            // MAR kiszamolt csapadek-mezobol vezetjuk le, nincs kulon
            // szimulacios lepes.
            BuildClouds(showClouds ? precipField : null, cloudDriftEnabled ? _cloudDriftTime : 0.0);
            PerfLog($"Build() clouds(enabled={showClouds})={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            buildPhaseStopwatch.Restart();

            // ND-132: csak a forraslista deep-time-fuggetlen. A TENYLEGES
            // nyomvonal a JELENLEGI (deep-time-mozgatott) seedeket kapja,
            // ezert azt minden vilagallapothoz ujra kell szamolni - de nem a
            // fo szalon. A t=0 csapadek-/oceanmezobol szarmazo forraslistat
            // viszont valtozatlan kulccsal ujrahasznaljuk.
            //
            // ND-124 (A), 2026-09-21: a forrasokat VIZGYUJTONKENTI kvotaval
            // valasztjuk (BuildRiverNetworkPerBasin), nem globalis top-K-val.
            // A globalis top-K a legnedvesebb hegyvidekek KOZOTT szorta szet
            // a forrasokat, ezert a nyomvonalak kulon medencekben futottak a
            // tengerig es ritkan talalkoztak - merve 48 forrasbol 8
            // osszefolyas es EGYETLEN 3-as vagy nagyobb vizhozam-sulyu folyo
            // sem. Vizgyujtonkenti kvotaval (16x6) 40 osszefolyas es 13 ilyen
            // folyo. Ez a "nincs tree alakzat" visszajelzes javitasa.
            List<TileId> riverSources = null;
            bool riverSourceCacheHit = false;
            if (showRivers)
            {
                riverSourceCacheHit = ReferenceEquals(_riverSourcePrecipField, precipField)
                    && _riverSourceCache != null;
                if (riverSourceCacheHit)
                {
                    riverSources = _riverSourceCache;
                }
                else
                {
                    FlowNetwork.FloodResult riverSourceFlood = FlowNetwork.PriorityFlood(
                        precipField.Elevation, precipField.IsOcean);
                    riverSources = RiverPathTracing.SelectRiverSourcesPerBasin(
                        precipField.Elevation, precipField.Precipitation,
                        precipField.IsOcean, riverSourceFlood.Parent, precipField.SeaLevel);
                    _riverSourceCache = riverSources;
                    _riverSourcePrecipField = precipField;
                }
            }

            PerfLog($"Build() riverSources(enabled={showRivers}, cacheHit={riverSourceCacheHit}, count={riverSources?.Count ?? 0})={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            buildPhaseStopwatch.Restart();

            _adaptiveRefinedRiverPaths = null;
            _adaptiveRiverDischargeWeights = null;
            _riverRefinementGeneration++;

            // Uj referencia-allapot -> a per-tile cache-ek (sarok +
            // klasszifikacio) ervenytelenek, mert regi vilagallapotra
            // (elozo seed/deepTimeMyr/craterek/seaLevel) vonatkoznanak.
            InvalidateAdaptiveCaches();

            if (useAdaptiveLod)
            {
                // INCREMENTAL-MESH-BUFFERS: a statikus alap-reteg (MINDEN
                // base-level tile, egyszer) elobb epul fel - ez felulirja a
                // fent (a fix `level`-en) mar felepult mesh-t `this.gameObject`-
                // en a HELYES adaptiveBaseLevel-en. Utana a dinamikus
                // (finomitott) reteg mar CSAK a kamera koruli, level feletti
                // resz-t epiti fel, kulon GameObject-eken.
                BuildStaticBaseLayer();
                PerfLog($"Build() BuildStaticBaseLayer(adaptiveBaseLevel={adaptiveBaseLevel})={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");
                buildPhaseStopwatch.Restart();

                Camera cam = GetAdaptiveCamera();
                if (cam != null)
                {
                    BodyFrameConversion.ToCore(transform.InverseTransformPoint(cam.transform.position), out double camX, out double camY, out double camZ);
                    RecomputeCutAndRebuildAdaptiveMesh(cam, camX, camY, camZ);
                    PerfLog($"Build() adaptive cut (camera)={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");
                    buildPhaseStopwatch.Restart();
                }
            }

            // Folyo-VONAL-reteg (referencia-szintu, statikus) - egyszer, a
            // kamera-mozgas nem erinti. A tile-fill folyo-kategoria helyett.
            BuildRiverNetwork();

            // Lapos to-vizfelszin (szurt tavak feltoltesi szintjen) - a blokkos
            // sotet tile-kitoltes helyett sima kek vizfelulet.
            BuildLakeSurface();
            PerfLog($"Build() riverNetwork+lakeSurface={buildPhaseStopwatch.Elapsed.TotalMilliseconds:F1}ms");

            // A vilag most ezekre a parameterekre epult fel - a kovetkezo
            // Update() ehhez kepest figyeli a valtozast (deepTimeMyr csuszka stb.).
            SnapshotWorldConfig();
            SnapshotOverlayConfig();
            SnapshotCloudConfig();
            SnapshotRiverLineConfig();
            Built.Invoke();
            buildTotalStopwatch.Stop();
            PerfLog($"Build() TELJES = {buildTotalStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            if (isActiveAndEnabled)
                StartRiverRefinement(seed, seeds, precipField?.SeaLevel ?? seaLevel, riverSources);
        }

        [Tooltip("A5: statikus Build fázisidő, főszálú managed allokáció és GC-számlálók. A worker/native memória külön Profiler-mérést igényel.")]
        public bool profileDeepTimeAllocations = false;

        // Csak diagnosztika: a számlálók nem befolyásolják a világmodell eredményét.
        // A rögzítés előre foglalt tömbbe ír, a szöveg/I/O a mérés UTÁN készül.
        private sealed class StaticBuildAllocationProfile : IDisposable
        {
            private struct Sample
            {
                public string Name;
                public double Milliseconds, ThreadCpuMilliseconds;
                public long MainThreadBytes, HeapDeltaBytes;
                public int Gen0, Gen1, Gen2;
            }

            private readonly PlanetGridMesh _owner;
            private readonly Sample[] _samples = new Sample[32];
            private readonly bool _allocationCounterSupported;
            private int _count;
            private string _name;
            private long _startTicks, _startBytes, _startHeap, _startCpu;
            private int _gen0, _gen1, _gen2;
            private bool _complete;

            // Csak Windows-diagnosztika: kernel+user CPU-idő, 100 ns
            // egységben. Más platformon/hibánál -1; a rövid fázisoknál az OS
            // számláló felbontása miatt nulla is lehet. Nem szimulációs óra.
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            private static extern IntPtr GetCurrentThread();
            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
#endif
            private static long ReadThreadCpu()
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user) ? kernel + user : -1;
#else
                return -1;
#endif
            }

            public StaticBuildAllocationProfile(PlanetGridMesh owner)
            {
                _owner = owner;
                // Unity Mono alatt az API létezhet úgy is, hogy mindig nullát ad.
                // Ismert allokációval ellenőrizzük, a mért szakaszokon KÍVÜL.
                long before = GC.GetAllocatedBytesForCurrentThread();
                var probe = new byte[1024];
                _allocationCounterSupported = GC.GetAllocatedBytesForCurrentThread() - before >= probe.Length;
                GC.KeepAlive(probe);
                Next("WorldGen.StaticBase.enumerate");
            }

            public void Next(string name)
            {
                EndSample();
                _name = name;
                UnityEngine.Profiling.Profiler.BeginSample(name);
                _startHeap = GC.GetTotalMemory(false);
                _gen0 = GC.CollectionCount(0);
                _gen1 = GC.CollectionCount(1);
                _gen2 = GC.CollectionCount(2);
                _startBytes = GC.GetAllocatedBytesForCurrentThread();
                _startCpu = ReadThreadCpu();
                _startTicks = Stopwatch.GetTimestamp();
            }

            private void EndSample()
            {
                if (_name == null) return;
                long endTicks = Stopwatch.GetTimestamp();
                long endCpu = ReadThreadCpu();
                long endBytes = GC.GetAllocatedBytesForCurrentThread();
                var sample = new Sample
                {
                    Name = _name,
                    Milliseconds = (endTicks - _startTicks) * 1000.0 / Stopwatch.Frequency,
                    ThreadCpuMilliseconds = _startCpu >= 0 && endCpu >= _startCpu ? (endCpu - _startCpu) / 10000.0 : -1,
                    MainThreadBytes = _allocationCounterSupported ? endBytes - _startBytes : -1,
                    Gen0 = GC.CollectionCount(0) - _gen0,
                    Gen1 = GC.CollectionCount(1) - _gen1,
                    Gen2 = GC.CollectionCount(2) - _gen2,
                    HeapDeltaBytes = GC.GetTotalMemory(false) - _startHeap
                };
                UnityEngine.Profiling.Profiler.EndSample();
                _name = null;
                _samples[_count++] = sample;
            }

            public void Complete()
            {
                EndSample();
                _complete = true;
            }

            public void Dispose()
            {
                EndSample();
                _owner.PerfLog(FormattableString.Invariant(
                    $"[A5 static profile] seed={_owner.worldSeed} timeMyr={_owner.deepTimeMyr:R} baseLevel={_owner.adaptiveBaseLevel} borders={_owner.showBorders} complete={_complete} samples={_count} allocationCounterSupported={_allocationCounterSupported} unavailableBytes=-1 allocationScope=main-thread gcScope=process heapScope=process-net allocationExcludesWorkersAndNative=True"));
                for (int i = 0; i < _count; i++)
                {
                    Sample s = _samples[i];
                    _owner.PerfLog(FormattableString.Invariant(
                        $"[A5 static phase] name={s.Name} ms={s.Milliseconds:F3} mainThreadBytes={s.MainThreadBytes} gc0={s.Gen0} gc1={s.Gen1} gc2={s.Gen2} heapDeltaBytes={s.HeapDeltaBytes} threadCpuMs={s.ThreadCpuMilliseconds:F3}"));
                }
            }
        }

        /// <summary>
        /// INCREMENTAL-MESH-BUFFERS: a TELJES bolygo `adaptiveBaseLevel`-en,
        /// EGYSZER felepitve - ez a mindig-lathato "padlo", amit a kamera
        /// mozgasa SOHA nem erint tobbet (ld. RecomputeCutAndRebuildAdaptiveMesh/
        /// RebuildAdaptiveMesh, ami mar csak a level feletti, finomitott
        /// reszt dolgozza fel). Ez teszi lehetove, hogy `adaptiveBaseLevel`
        /// akar 8 (393216 tile) is lehessen anelkul, hogy minden egyes
        /// kameramozgas ujra kiertekelne/ujraepitene mind a 393k tile-t -
        /// az egyszeri epitesi koltseg (par szaz ms, parhuzamositva) ELKULONUL
        /// a folyamatos mozgas-kivaltotta koltsegtol (ami mostantol csak a
        /// kis, finomitott reszre vonatkozik).
        /// </summary>
        /// <param name="reuseClassifications">
        /// ND-50 (2026-09-20): a tile-KLASSZIFIKACIO (kategoria + bucket) NEM
        /// fugg az overlaytol - az elevaciobol, a tengerszintbol es a
        /// biome-bol szarmazik, amik overlay-valtaskor valtozatlanok. MERVE:
        /// ez a fazis 453 ms a 1660 ms-os overlay-ujraepitesbol (27%).
        /// CSAK az <see cref="ApplyOverlayOnlyRebuild"/> adja true-val, ahol
        /// konstrukcio szerint semmi olyan nem valtozott, ami a besorolast
        /// befolyasolna; a kivancsi hivo ne allitsa at. Ha a meglevo tomb
        /// merete/szintje nem stimmel, a kod BIZTONSAGOSAN ujraszamol.
        /// </param>
        private void BuildStaticBaseLayer(bool reuseClassifications = false)
        {
            using var allocationProfile = profileDeepTimeAllocations ? new StaticBuildAllocationProfile(this) : null;
            var totalStopwatch = Stopwatch.StartNew();
            var phaseStopwatch = Stopwatch.StartNew();
            int n = 1 << adaptiveBaseLevel;
            var baseTiles = new List<TileId>(6 * n * n);
            for (int face = 0; face <= 5; face++)
                for (uint u = 0; u < (uint)n; u++)
                    for (uint v = 0; v < (uint)n; v++)
                        baseTiles.Add(TileId.FromFaceLevelUV(face, adaptiveBaseLevel, u, v));

            TileId[] leaves = baseTiles.ToArray();
            double enumerateMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.terrainBasis");
            phaseStopwatch.Restart();
            bool terrainBasisReused = EnsureStaticTerrainBasisCache();
            double terrainBasisMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.tileCenterBasis");
            phaseStopwatch.Restart();
            bool tileCenterBasisReused = EnsureTileCenterTerrainBasisCache(_adaptiveSeed, adaptiveBaseLevel);
            double tileCenterBasisMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.classification");
            phaseStopwatch.Restart();
            bool useDenseStaticData = adaptiveBaseLevel <= 8;
            bool classificationsReused = reuseClassifications && useDenseStaticData
                && _staticRenderDataLevel == adaptiveBaseLevel
                && _staticTileClassifications.Length == leaves.Length;
            ClassificationDiag classificationDiag = classificationsReused
                ? new ClassificationDiag(0, false, 0, 0)
                : useDenseStaticData
                    ? PrecomputeStaticClassificationsInParallel(leaves)
                    : PrecomputeClassificationsInParallel(leaves, forceCpu: true);
            double classificationMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.corners");
            phaseStopwatch.Restart();
            (int NeededCount, int MissingCount) cornerDiag = useDenseStaticData
                ? PrecomputeStaticCornersInParallel()
                : PrecomputeCornersInParallel(leaves);
            double cornersMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.terrainProxy");
            phaseStopwatch.Restart();
            // A pozíciók már tartalmazzák a tengerszintet, reliefet és az
            // aktuális világot. Semmilyen új Core-mintát nem kérünk itt.
            _terrainLodProxy = null;
            if (useDenseStaticData)
            {
                var radii = new double[_staticCornerPositions.Length];
                for (int i = 0; i < radii.Length; i++)
                {
                    Vector3 p = _staticCornerPositions[i];
                    radii[i] = Math.Sqrt((double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z);
                }
                _terrainLodProxy = new TerrainLodProxy(adaptiveBaseLevel, radii,
                    radius + _adaptiveSeaLevel * elevationScale);
            }
            double terrainProxyMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.bucketPrepare");
            var verticesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var normalsByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var trianglesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<int>>();
            var colorsByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Color>>();
            var waterVerticesByBucket = new Dictionary<int, List<Vector3>>();
            var waterNormalsByBucket = new Dictionary<int, List<Vector3>>();
            var waterTrianglesByBucket = new Dictionary<int, List<int>>();
            var waterColorsByBucket = new Dictionary<int, List<Color>>();
            var borderVerts = new List<Vector3>(showBorders ? checked(leaves.Length * 4) : 0);
            var borderIndices = new List<int>(showBorders ? checked(leaves.Length * 8) : 0);
            float waterSurfaceRadius = radius + (float)(_adaptiveSeaLevel * elevationScale);

            phaseStopwatch.Restart();
            StaticMeshBuckets? staticBuckets = useDenseStaticData
                ? CreateStaticMeshBuckets(
                    verticesByKey, normalsByKey, trianglesByKey, colorsByKey,
                    waterVerticesByBucket, waterNormalsByBucket,
                    waterTrianglesByBucket, waterColorsByBucket)
                : null;
            double bucketPrepareMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.emit");
            phaseStopwatch.Restart();
            // ND-123: ha van suru statikus adat ES nincs hatarvonal-reteg, a
            // 393 216 tile emitje PARHUZAMOSAN fut (ld.
            // EmitStaticBaseLayerInParallel). Minden mas esetben a korabbi,
            // egyszalu ciklus - ez egyben a referencia-implementacio is.
            bool parallelEmit = useDenseStaticData && staticBuckets != null && !showBorders;
            if (parallelEmit)
            {
                EmitStaticBaseLayerInParallel(
                    leaves, staticBuckets!, verticesByKey, normalsByKey, trianglesByKey, colorsByKey,
                    waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket, waterColorsByBucket,
                    borderVerts, borderIndices, waterSurfaceRadius);
            }
            else
            {
                for (int i = 0; i < leaves.Length; i++)
                {
                    EmitAdaptiveTile(
                        leaves[i], verticesByKey, normalsByKey, trianglesByKey, colorsByKey,
                        waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket, waterColorsByBucket,
                        borderVerts, borderIndices, waterSurfaceRadius, radialBias: 0f,
                        staticDenseIndex: useDenseStaticData ? i : -1,
                        staticBuckets: staticBuckets);
                }
            }
            double emitMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.meshAssemble");
            phaseStopwatch.Restart();
            ConcatenatedMesh staticMesh = ConcatenateMultiMaterialBuckets(verticesByKey, normalsByKey, trianglesByKey, colorsByKey);
            AttachTerrainTileIds(staticMesh, leaves);
            allocationProfile?.Next("WorldGen.StaticBase.meshUpload");
            UploadConcatenatedMultiMaterialMesh(gameObject, staticMesh);
            allocationProfile?.Next("WorldGen.StaticBase.meshMask");
            InitializeTerrainIndexMask(staticMesh, leaves);
            double meshMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.borders");
            phaseStopwatch.Restart();
            BuildBorders(borderVerts, borderIndices, "Borders");
            double bordersMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.water.setup");
            phaseStopwatch.Restart();
            BuildWaterSurface(waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket, waterColorsByBucket, "WaterSurface", allocationProfile);
            allocationProfile?.Next("WorldGen.StaticBase.water.layout");
            InitializeIndependentWater(staticBuckets, waterSurfaceRadius, allocationProfile);
            double waterMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Next("WorldGen.StaticBase.eviction");
            phaseStopwatch.Restart();
            EvictCornerCacheIfNeeded();
            EvictTileClassificationCacheIfNeeded();
            double evictionMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            allocationProfile?.Complete();
            totalStopwatch.Stop();
            PerfLog(
                $"  BuildStaticBaseLayer reszletek: total={totalStopwatch.Elapsed.TotalMilliseconds:F1}ms " +
                $"enumerate={enumerateMs:F1}ms | terrainBasis={terrainBasisMs:F1}ms " +
                $"(reused={terrainBasisReused}) | " +
                $"tileCenterBasis={tileCenterBasisMs:F1}ms (reused={tileCenterBasisReused}) | " +
                $"denseStatic={useDenseStaticData} | classificationsReused={classificationsReused} | " +
                $"parallelEmit={parallelEmit} | " +
                $"classification={classificationMs:F1}ms (missing={classificationDiag.MissingCount}, " +
                $"usedGpu={classificationDiag.UsedGpu}, gpuDispatch={classificationDiag.GpuDispatchMs:F1}ms, " +
                $"cpuTempLoop={classificationDiag.CpuTemperatureLoopMs:F1}ms) | " +
                $"corners={cornersMs:F1}ms (needed={cornerDiag.NeededCount}, missing={cornerDiag.MissingCount}) | " +
                $"terrainProxy={terrainProxyMs:F1}ms proxyBytes={_terrainLodProxy?.StorageBytes ?? 0} proxyNewCoreSamples=0 | " +
                $"bucketPrepare={bucketPrepareMs:F1}ms | emit={emitMs:F1}ms | mesh={meshMs:F1}ms | borders={bordersMs:F1}ms | " +
                $"water={waterMs:F1}ms | eviction={evictionMs:F1}ms");
        }

        private sealed class StaticMeshBuckets
        {
            public readonly List<Vector3>[] TerrainVertices;
            public readonly List<Vector3>[] TerrainNormals;
            public readonly List<int>[] TerrainTriangles;
            public readonly List<Color>[] TerrainColors;
            public readonly List<Vector3>[] WaterVertices;
            public readonly List<Vector3>[] WaterNormals;
            public readonly List<int>[] WaterTriangles;
            public readonly List<Color>[] WaterColors;
            public readonly List<TileId>[] WaterTiles;

            public StaticMeshBuckets(int[] terrainQuadCounts, int[] waterQuadCounts)
            {
                TerrainVertices = new List<Vector3>[terrainQuadCounts.Length];
                TerrainNormals = new List<Vector3>[terrainQuadCounts.Length];
                TerrainTriangles = new List<int>[terrainQuadCounts.Length];
                TerrainColors = new List<Color>[terrainQuadCounts.Length];
                for (int i = 0; i < terrainQuadCounts.Length; i++)
                {
                    int quads = terrainQuadCounts[i];
                    TerrainVertices[i] = new List<Vector3>(checked(quads * 4));
                    TerrainNormals[i] = new List<Vector3>(checked(quads * 4));
                    TerrainTriangles[i] = new List<int>(checked(quads * 6));
                    TerrainColors[i] = new List<Color>(checked(quads * 4));
                }

                WaterVertices = new List<Vector3>[waterQuadCounts.Length];
                WaterNormals = new List<Vector3>[waterQuadCounts.Length];
                WaterTriangles = new List<int>[waterQuadCounts.Length];
                WaterColors = new List<Color>[waterQuadCounts.Length];
                WaterTiles = new List<TileId>[waterQuadCounts.Length];
                for (int i = 0; i < waterQuadCounts.Length; i++)
                {
                    int quads = waterQuadCounts[i];
                    WaterVertices[i] = new List<Vector3>(checked(quads * 4));
                    WaterNormals[i] = new List<Vector3>(checked(quads * 4));
                    WaterTriangles[i] = new List<int>(checked(quads * 6));
                    WaterColors[i] = new List<Color>(checked(quads * 4));
                    WaterTiles[i] = new List<TileId>(quads);
                }
            }
        }

        private StaticMeshBuckets CreateStaticMeshBuckets(
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> verticesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> normalsByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<int>> trianglesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Color>> colorsByKey,
            Dictionary<int, List<Vector3>> waterVerticesByBucket,
            Dictionary<int, List<Vector3>> waterNormalsByBucket,
            Dictionary<int, List<int>> waterTrianglesByBucket,
            Dictionary<int, List<Color>> waterColorsByBucket)
        {
            int terrainSlotCount = ((int)RenderCategory.Lake + 1) * OceanRockBucketCount;
            var terrainQuadCounts = new int[terrainSlotCount];
            var waterQuadCounts = new int[WaterDepthBucketCount];
            for (int i = 0; i < _staticTileClassifications.Length; i++)
            {
                AdaptiveTileClassification classification = _staticTileClassifications[i];
                int terrainSlot = StaticTerrainBucketIndex(classification.Category, classification.Bucket);
                terrainQuadCounts[terrainSlot]++;
                if (classification.IsOceanic
                    && (classification.Biome == Biome.Ocean || classification.Biome == Biome.SeaIce))
                {
                    int waterBucket = WaterDepthBucket(_adaptiveSeaLevel - classification.Elevation);
                    waterQuadCounts[waterBucket]++;
                }
            }

            var buckets = new StaticMeshBuckets(terrainQuadCounts, waterQuadCounts);
            for (int categoryIndex = 0; categoryIndex <= (int)RenderCategory.Lake; categoryIndex++)
            {
                var category = (RenderCategory)categoryIndex;
                for (int bucket = 0; bucket < OceanRockBucketCount; bucket++)
                {
                    int slot = StaticTerrainBucketIndex(category, bucket);
                    if (terrainQuadCounts[slot] == 0)
                        continue;
                    var key = (category, bucket);
                    verticesByKey[key] = buckets.TerrainVertices[slot];
                    normalsByKey[key] = buckets.TerrainNormals[slot];
                    trianglesByKey[key] = buckets.TerrainTriangles[slot];
                    colorsByKey[key] = buckets.TerrainColors[slot];
                }
            }

            for (int bucket = 0; bucket < WaterDepthBucketCount; bucket++)
            {
                if (waterQuadCounts[bucket] == 0)
                    continue;
                waterVerticesByBucket[bucket] = buckets.WaterVertices[bucket];
                waterNormalsByBucket[bucket] = buckets.WaterNormals[bucket];
                waterTrianglesByBucket[bucket] = buckets.WaterTriangles[bucket];
                waterColorsByBucket[bucket] = buckets.WaterColors[bucket];
            }
            return buckets;
        }

        private static int StaticTerrainBucketIndex(RenderCategory category, int bucket)
            => (int)category * OceanRockBucketCount + bucket;

        private TerrainLodProxy? DesiredTerrainLodProxy()
            => useTerrainLodProxy && adaptiveMaxLevel >= adaptiveBaseLevel
                && _terrainLodProxy?.BaseLevel == adaptiveBaseLevel ? _terrainLodProxy : null;

        /// <summary>
        /// M9 kozponti belepesi pontja: uj cut szamolasa a MEGLEVO cut-bol
        /// (hiszterezis, ld. AdaptiveQuadTree), majd a mesh teljes ujraepitese
        /// a cut-bol. A `Built.Invoke()`-ot NEM hivja ujra - az csak a Build()
        /// (referencia-szintu passz) vegen tuzel, a panel-adatok szempontjabol
        /// ez az esemeny releváns, nem az egyes adaptiv ujraepitesek.
        /// </summary>
        private void RecomputeCutAndRebuildAdaptiveMesh(Camera cam, double camX, double camY, double camZ)
        {
            _requestedUploadConfigRevision = _uploadConfigRevision;
            // Vedelmi korlat Inspector-hiba ellen (pl. base > max eseten az
            // AdaptiveQuadTree kivetelt dobna minden Update()-ben) - a [Range]
            // attributumok kulon-kulon mar korlatoznak, de az egymashoz
            // kepesti sorrendet nem.
            int effectiveBaseLevel = Math.Min(adaptiveBaseLevel, adaptiveMaxLevel);

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

            // JAVITVA (2026-09-02, felhasznaloi kepernyokep: a kep szelen/tetejen
            // durva, a kozepen finom reszletek - "gray periphery" tunet): a
            // korabbi `max(vertikalis, horizontalis)/2` NEM a teljes teglalap
            // alaku frustum lefedesehez szukseges szoget adja, hanem csak a
            // KEPERNYO-TENGELYEK menten mert felszoget - a frustum SAROKPONTJAI
            // (pl. bal-felso) ENNEL NAGYOBB szogben allnak a nezesi iranytol,
            // kulonosen szelsoseges (nem ~1:1) kepernyo-aranynal (a
            // felhasznalonal 2.23:1 volt merve). A helyes, konzervativ also
            // korlat az "atlos felszog": atan(sqrt(tan(halfH)^2+tan(halfV)^2)) -
            // ez PONTOSAN a frustum sarkaig tarto szog, tehat garantalja, hogy
            // a latokup-teszt (IsWithinViewCone) a KEPERNYO EGESZ TERULETET
            // lefedje, nem csak a kozepso savot.
            double halfVerticalFovRad = verticalFovRad / 2.0;
            double halfHorizontalFovRad = horizontalFovRad / 2.0;
            double diagonalHalfFovRad = Math.Atan(Math.Sqrt(
                Math.Tan(halfHorizontalFovRad) * Math.Tan(halfHorizontalFovRad) +
                Math.Tan(halfVerticalFovRad) * Math.Tan(halfVerticalFovRad)));
            double halfFovRadians = diagonalHalfFovRad * fovSafetyMargin;

            // SCREEN-SPACE-LOD: a celzott pixel-atmerobol (targetTilePixelSize)
            // a kamera TENYLEGES fuggoleges FOV-jabol es kepernyo-magassagabol
            // (pixelben) szamoljuk a tenyleges szogsugar-kuszobot, amit az
            // AdaptiveQuadTree.Visit() az atan2(rTile,distance) vetitett
            // szogsugarral hasonlit ossze - ld. AdaptiveQuadTree.
            // DefaultSplitThresholdRadians doksija a regi (tavolsag/meret-arany)
            // metrika miert nem volt eleg: az FUGGETLEN volt a kamera tenyleges
            // felbontasatol/FOV-jatol, itt viszont NEM az.
            double effectiveTargetPixelSize = Math.Max(targetTilePixelSize, 1.0);
            double screenHeightPixels = Math.Max(1, cam.pixelHeight);
            double targetAngularRadiusRadians = AdaptiveViewState.AngularRadiusForPixelDiameter(
                effectiveTargetPixelSize, verticalFovRad, (int)screenHeightPixels);
            double mergeAngularRadiusRadians = AdaptiveViewState.MergeThreshold(targetAngularRadiusRadians, mergeHysteresisFactor);
            _currentTargetAngularRadiusRadians = targetAngularRadiusRadians;
            double initialAngularRadius = AdaptiveViewState.AngularRadiusForPixelDiameter(
                initialRefinementPixelSize, verticalFovRad, (int)screenHeightPixels);
            double baseSplitScale = AdaptiveViewState.EarlierBaseSplitScale(
                targetAngularRadiusRadians, mergeAngularRadiusRadians, initialAngularRadius);
            _currentBaseTargetAngularRadiusRadians = targetAngularRadiusRadians * baseSplitScale;
            _currentGeomorphRangeFraction = geomorphRangeFraction;
            // A cut és az utána futó emit ugyanazt az immutábilis snapshotot
            // használja, akkor is, ha a kapcsolót közben átállítják.
            TerrainLodProxy? terrainProxy = _requestedTerrainLodProxy = DesiredTerrainLodProxy();
            _requestedProjectedView = terrainProxy != null && !cam.orthographic ? CaptureProjectedLodView(cam,camX,camY,camZ) : null;
            PrepareTerrainEvaluationCache(terrainProxy);
            PrepareIndependentWaterRequest();

            // DIAGNOSZTIKAI RES JAVITVA (2026-09-01): a Stopwatch korabban
            // CSAK a RebuildAdaptiveMesh()-t merte - egy valos katasztrofa-
            // esetben viszont a koltseg a BuildCut() SAJAT bejarasaban
            // jelentkezett (a szogsugar-metrika horizont-kozeli hibaja
            // miatt), es a Console NEMA maradt, holott a Profiler 725ms+
            // "PlanetGridMesh.Update() Self" idot mutatott - mert pont ez a
            // resz nem volt idozitve. Most MINDKETTO (BuildCut + Rebuild)
            // kulon-kulon idozitve, hogy legkozelebb azonnal lathato legyen,
            // MELYIK resz a koltseges.
            // FELHASZNALOI KERES (2026-09-02, otodik kor): "barmely zoom
            // eseten a lathato pixelek/ivszog max fele kerulne negyedelesre" -
            // egy FIX MinUsefulCosGrazing-szog nem tudja ezt garantalni
            // (ugyanaz a szog teljesen mas ivszog-/pixel-aranyt zar be
            // kozeli, mint tavoli kameranal, ld. AdaptiveQuadTree.
            // ComputeHalfArcMinUsefulCosGrazing doksijaban a levezetest) -
            // ezert a kuszob MOST MAR minden ujraepitesnel a kamera
            // AKTUALIS kozeppont-tavolsagabol szamolodik ujra (olcso,
            // zart formulas szamitas, nincs plusz Core-hivas).
            double camDistanceFromCenter = Math.Sqrt(camX * camX + camY * camY + camZ * camZ);
            double dynamicMinUsefulCosGrazing = AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(camDistanceFromCenter, radius);

            // FAZIS 1 (ND-47): a bejaras a statikus base-tol LEVALASZTOTT,
            // alacsony gyokerszintrol indul (adaptiveTraversalRootLevel), es a
            // statikus reteg (staticBaseLevel = effectiveBaseLevel) FOLE finomit
            // - a base-szintu (es az alatti) regiot a statikus mesh fedi, ezert
            // a dinamikus cut CSAK a level > base leveleket tartalmazza. Igy a
            // BuildCut koltsege es a maxLeafCount-korlat mar NEM a 393216
            // base-gyokerhez van kotve (ld. AdaptiveQuadTree Fazis 1 doksi).
            int traversalRootLevel = Math.Min(adaptiveTraversalRootLevel, effectiveBaseLevel);
            // 0 = automatikus, a viewportbol; pozitiv = kezi felulbiralas.
            int renderBudget = ResolveRenderBudget(cam.pixelWidth, cam.pixelHeight);
            // ND-72: az ND-71 ágankénti terepmintázása élőben súlyos regresszió.
            // A gömbös ND-70 kiválasztás marad; csak EGY renderelt nadírt mérünk.
            _requestedLodLocalToCamera = cam.worldToCameraMatrix * transform.localToWorldMatrix;
            _requestedLodLocalToClip = cam.projectionMatrix * _requestedLodLocalToCamera;
            _requestedLodPixelWidth = cam.pixelWidth;
            _requestedLodPixelHeight = cam.pixelHeight;
            _requestedLodNearClip = cam.nearClipPlane;

            // FAZIS 3 (ND-47): a BuildCut TISZTA statikus fuggveny - worker
            // szalon futtatva a kivalasztas koltsege NEM a fo szalon jelentkezik.
            // A hiszterezishez az ELOZO cut kell bemenetkent (a task csak OLVASSA;
            // a fo szal a _currentCut-ot csak az ALKALMAZASKOR - TryApplyCompletedAsyncCut,
            // Update - irja, amikor mar nem fut task). Az alkalmazas (a mesh-upldoad,
            // Unity-API) az Update()-ben, a fo szalon tortenik.
            if (useAsyncMeshRebuild && !_asyncMeshRebuildDisabledAfterError)
            {
                // FAZIS 3: a TELJES nehez munka (BuildCut + a geometria-emit) a
                // WORKER szalon fut; a fo szalon csak a mesh-feltoltes marad
                // (ApplyAdaptiveMeshBuffers, TryApplyCompletedAsyncCut). Igy a
                // budget nagyra vehető ANELKUL, hogy a fo szal beakadna - ez
                // szunteti meg a "zoom kozben osszevonja a tile-okat" tunetet is
                // (az a fix, tul kicsi budget ujraosztasa volt).
                IReadOnlyCollection<TileId> previousCut = _currentCut;
                _pendingCutCamX = camX; _pendingCutCamY = camY; _pendingCutCamZ = camZ;
                _pendingCutView = CaptureAdaptiveView(cam, camX, camY, camZ);
                _pendingCutRequestedRealtime = Time.unscaledTime;
                // A frame-óra a Build kezdetén ragadhat; a mérés külön monotón órát használ.
                _pendingCutRequestedTicks = Stopwatch.GetTimestamp();
                _pendingSurfaceAltitude = camDistanceFromCenter - (terrainProxy != null
                    ? terrainProxy.RadiusAt(TileGeometry.FromPosition(camX,camY,camZ,effectiveBaseLevel)) : radius);
                _cutCancellation = new System.Threading.CancellationTokenSource();
                var cancellation = _cutCancellation.Token;
                var selectionWork = new LodSelectionWork(_requestedProjectedView, NewSplitsPerRequest, cancellation,
                    captureTrace: logDrawnTileSizes,
                    skipStaticBase: _requestedProjectedView!=null ? IsBaseAncestorOceanic : (Func<TileId,bool>?)null,
                    evaluationCache: _terrainEvaluationCache);
                // FAZIS 5 (ND-47) geomorph-pontossag: a worker-emit
                // (ComputeGeomorphAlpha) a _lastCutCameraCore*-bol szamolja a
                // geomorph-alfat. Ezt MAR ITT (a task inditasa ELOTT, a fo
                // szalon) a JELEN kameraallasra allitjuk - kulonben a worker az
                // ELOZO cut kamerajat hasznalna, es a geomorph enyhen elcsuszna
                // ahhoz a poziciohoz kepest, amire a cut valojaban keszul. (A
                // TryApply is beallitja - idempotens.)
                _lastCutCameraCoreX = camX; _lastCutCameraCoreY = camY; _lastCutCameraCoreZ = camZ;
                _cutTask = System.Threading.Tasks.Task.Run(() =>
                {
                    long workerStartedTicks = Stopwatch.GetTimestamp();
                    var cutStopwatch = Stopwatch.StartNew();
                    HashSet<TileId> cut = AdaptiveQuadTree.BuildCut(
                        camX, camY, camZ, radius, previousCut,
                        effectiveBaseLevel, adaptiveMaxLevel, targetAngularRadiusRadians, mergeAngularRadiusRadians,
                        fwdX, fwdY, fwdZ, halfFovRadians, maxLeafCount: renderBudget,
                        minUsefulCosGrazing: dynamicMinUsefulCosGrazing,
                        traversalRootLevel: traversalRootLevel, staticBaseLevel: effectiveBaseLevel,
                        baseSplitScale: baseSplitScale, terrainProxy: terrainProxy, work: selectionWork);
                    cutStopwatch.Stop();
                    cancellation.ThrowIfCancellationRequested();
                    AdaptiveMeshBuffers buffers = ComputeAdaptiveMeshBuffersCpu(cut, cancellation);
                    buffers.NewSplits = selectionWork.NewSplits;
                    buffers.DeferredSplits = selectionWork.DeferredSplits;
                    buffers.RenderBudget = renderBudget;
                    buffers.BudgetStops = selectionWork.BudgetStops;
                    buffers.SufficientStops = selectionWork.BelowThresholdStops;
                    buffers.MaxLevelStops = selectionWork.MaxLevelStops;
                    buffers.InvisibleStops = selectionWork.InvisibleStops;
                    buffers.SelectionTrace = selectionWork.Trace;
                    buffers.SkippedSelectionBases = selectionWork.SkippedStaticBases;
                    buffers.CutMs = cutStopwatch.Elapsed.TotalMilliseconds;
                    buffers.SelectionMs = selectionWork.SelectionMs;
                    buffers.BalanceMs = selectionWork.BalanceMs;
                    buffers.BalanceIterations = selectionWork.BalanceIterations;
                    buffers.BalanceSplits = selectionWork.BalanceSplits;
                    buffers.MetricCacheHits = selectionWork.MetricCacheHits;
                    buffers.MetricEvaluations = selectionWork.MetricEvaluations;
                    buffers.MetricCacheEntries = selectionWork.EvaluationCache?.Count ?? 0;
                    buffers.WorkerStartedTicks = workerStartedTicks;
                    buffers.WorkerReadyTicks = Stopwatch.GetTimestamp();
                    return buffers;
                });
                PerfLog(
                    $"[{DateTime.Now:HH:mm:ss.fff}] RecomputeCut (async kickoff): " +
                    $"cam=({camX:F3},{camY:F3},{camZ:F3}) tavolsag-origotol={camDistanceFromCenter:F3} " +
                    $"target={targetAngularRadiusRadians:F6} budget={renderBudget} baseLevel={effectiveBaseLevel} maxLevel={adaptiveMaxLevel} " +
                    $"halfFov={halfFovRadians:F9} verticalFov={verticalFovRad:F9} aspect={cam.aspect:F6} viewport={cam.pixelWidth}x{cam.pixelHeight} " +
                    $"baseTarget={_currentBaseTargetAngularRadiusRadians:F9} morphRange={_currentGeomorphRangeFraction:F3} " +
                    $"terrainProxy={terrainProxy != null} proxyRequested={useTerrainLodProxy} " +
                    $"projectedTerrain={_requestedProjectedView != null} newSplitLimit={NewSplitsPerRequest}");
                return; // az alkalmazas (feltoltes) az Update()-ben, amint a task kesz
            }

            var buildCutStopwatch = Stopwatch.StartNew();
            _currentCut = AdaptiveQuadTree.BuildCut(
                camX, camY, camZ, radius, _currentCut,
                effectiveBaseLevel, adaptiveMaxLevel, targetAngularRadiusRadians, mergeAngularRadiusRadians,
                fwdX, fwdY, fwdZ, halfFovRadians, maxLeafCount: renderBudget,
                minUsefulCosGrazing: dynamicMinUsefulCosGrazing,
                traversalRootLevel: traversalRootLevel, staticBaseLevel: effectiveBaseLevel,
                baseSplitScale: baseSplitScale, terrainProxy: terrainProxy,
                work: _requestedProjectedView != null ? new LodSelectionWork(_requestedProjectedView,
                    skipStaticBase: IsBaseAncestorOceanic, evaluationCache: _terrainEvaluationCache) : null);
            buildCutStopwatch.Stop();

            _lastCutCameraCoreX = camX;
            _lastCutCameraCoreY = camY;
            _lastCutCameraCoreZ = camZ;
            PerfLog(
                $"[{DateTime.Now:HH:mm:ss.fff}] RecomputeCutAndRebuildAdaptiveMesh HIVAS: " +
                $"cam=({camX:F3},{camY:F3},{camZ:F3}) tavolsag-origotol={camDistanceFromCenter:F3} " +
                $"fwd=({fwdX:F3},{fwdY:F3},{fwdZ:F3}) halfFovRadians={halfFovRadians:F5} " +
                $"targetAngularRadiusRadians={targetAngularRadiusRadians:F6} mergeAngularRadiusRadians={mergeAngularRadiusRadians:F6} " +
                $"effectiveBaseLevel={effectiveBaseLevel} adaptiveMaxLevel={adaptiveMaxLevel} " +
                $"dynamicMinUsefulCosGrazing={dynamicMinUsefulCosGrazing:F4} (phi={Math.Acos(dynamicMinUsefulCosGrazing) * 180.0 / Math.PI:F2}deg) " +
                $"cam.fieldOfView={cam.fieldOfView} cam.aspect={cam.aspect:F4} cam.pixelHeight={cam.pixelHeight}");
            PerfLog($"  BuildCut={buildCutStopwatch.Elapsed.TotalMilliseconds:F2}ms cut.Count={_currentCut.Count} " +
                DescribeSurfaceLod(_currentCut, camX, camY, camZ));

            var stopwatch = Stopwatch.StartNew();
            RebuildAdaptiveMesh();
            _lastAppliedCutView = CaptureAdaptiveView(cam, camX, camY, camZ);
            _hasLastCutCameraPosition = true;
            _lodRefinementPending = _appliedWaterSelection?.RefinementPending == true;
            _cutSupersededSinceApply = false;
            _appliedSelectionTrace = null; // A szinkron tartalékút nem rögzít megállási trace-t.
            _appliedDiagnosticCoverage = null;
            _appliedCutWork = default; // ...és munka-könyvelést sem: `cutWork=none`.
            stopwatch.Stop();
            PerfLog($"  TELJES (BuildCut+RebuildAdaptiveMesh) = {(buildCutStopwatch.Elapsed.TotalMilliseconds + stopwatch.Elapsed.TotalMilliseconds):F2}ms");
            if (buildCutStopwatch.Elapsed.TotalMilliseconds > adaptiveRebuildWarningMs
                || stopwatch.Elapsed.TotalMilliseconds > adaptiveRebuildWarningMs)
            {
                Debug.LogWarning(
                    $"PlanetGridMesh: BuildCut {buildCutStopwatch.Elapsed.TotalMilliseconds:F1}ms + " +
                    $"adaptiv ujraepites {stopwatch.Elapsed.TotalMilliseconds:F1}ms " +
                    $"(kuszob {adaptiveRebuildWarningMs}ms, cut merete {_currentCut.Count}) - " +
                    "ha ez rendszeres, csokkentsd az adaptiveMaxLevel-t vagy noveld az " +
                    "adaptiveCameraMoveThreshold-ot (ld. docs/05-milestones.md §9.5). " +
                    $"Reszletes naplo: {_perfLogPath}");
            }
        }

        /// <summary>
        /// FAZIS 3 (ND-47): egy elkeszult async BuildCut task alkalmazasa a FO
        /// szalon - a cut atvetele, majd a mesh felepitese (Unity-API). Hiba
        /// eseten (task.IsFaulted) naplo + eldobas: a kovetkezo Update-korben a
        /// mozgas-kuszob ujra kivaltja a szamitast (szinkron fallback marad, ha a
        /// hiba tartos). A KICKOFF-kori kamera-poziciot konyveljuk el (arra a
        /// poziciora ervenyes a cut).
        /// </summary>
        private void TryApplyCompletedAsyncCut()
        {
            if (_cutTask == null || !_cutTask.IsCompleted)
                return;

            System.Threading.Tasks.Task<AdaptiveMeshBuffers> task = _cutTask;
            _cutTask = null;

            bool superseded = _cutCancellation?.IsCancellationRequested == true;
            _cutCancellation?.Dispose();
            _cutCancellation = null;
            if ((!task.IsFaulted && superseded) || task.IsCanceled
                || (task.IsFaulted && task.Exception?.GetBaseException() is OperationCanceledException))
            {
                _lodRefinementPending = true;
                PerfLog("[ND-76 request] discarded=True; félkész geometria nem került a rendererbe");
                return;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                _asyncMeshRebuildDisabledAfterError = true;
                Debug.LogError(
                    "PlanetGridMesh: az async BuildCut+emit hibaval zarult - TARTOSAN visszaallok a " +
                    "szinkron (fo szalu) utra (a jelenlegi mukodes garantalt). Kerlek jelezd ezt a " +
                    $"hibauzenetet: {task.Exception?.GetBaseException()}");
                return;
            }

            AdaptiveMeshBuffers buffers = task.Result;
            buffers.WorkerObservedTicks = Stopwatch.GetTimestamp();
            if (BeginStagedTerrainUpload(buffers)) return;
            CompleteAsyncMeshRequest(buffers);
        }

        private void CompleteAsyncMeshRequest(AdaptiveMeshBuffers buffers)
        {
            long commitStartedTicks = Stopwatch.GetTimestamp();
            _lastCutCameraCoreX = _pendingCutCamX;
            _lastCutCameraCoreY = _pendingCutCamY;
            _lastCutCameraCoreZ = _pendingCutCamZ;
            // FAZIS 3: a geometria MAR keszen van (worker szal), itt CSAK a Unity
            // mesh-feltoltes tortenik (fo szal) - ez a maradek fo-szal-koltseg,
            // ami sokkal kisebb, mint a teljes emit volt.
            var stopwatch = Stopwatch.StartNew();
            ApplyAdaptiveMeshBuffers(buffers);
            _currentCut = buffers.Cut;
            _appliedCutWork = new AppliedCutWork(
                buffers.Cut?.Count ?? 0, buffers.DynamicLeafCount,
                buffers.WaterSelection?.Leaves.Count ?? 0, buffers.RenderBudget,
                buffers.NewSplits, buffers.ReusedLeaves,
                buffers.MetricEvaluations, buffers.MetricCacheHits,
                buffers.BudgetStops, buffers.DeferredSplits,
                buffers.SufficientStops, buffers.MaxLevelStops, buffers.InvisibleStops,
                buffers.SkippedSelectionBases, buffers.SkippedOceanicCount);
            _appliedSelectionTrace = buffers.SelectionTrace;
            _appliedDiagnosticCoverage = buffers.DiagnosticCoverage;
            _appliedTraceView = _requestedProjectedView;
            _appliedTraceThreshold = _currentTargetAngularRadiusRadians;
            _appliedTraceBaseThreshold = _currentBaseTargetAngularRadiusRadians;
            _appliedTraceMorphRange = _currentGeomorphRangeFraction;
            _appliedTraceBaseLevel = adaptiveBaseLevel;
            _appliedTracePixelHeight = _requestedLodPixelHeight;
            _appliedTraceFov = _pendingCutView.VerticalFovRadians;
            _lastAppliedCutView = _pendingCutView;
            _hasLastCutCameraPosition = true;
            _lodRefinementPending = buffers.DeferredSplits > 0 || buffers.GeometryRefinementPending
                || buffers.WaterSelection?.RefinementPending == true;
            _cutSupersededSinceApply = false;
            stopwatch.Stop();
            long committedTicks = Stopwatch.GetTimestamp();
            var requestTiming = new LodRequestTiming(_pendingCutRequestedTicks, buffers.WorkerStartedTicks,
                buffers.WorkerReadyTicks, buffers.WorkerObservedTicks, commitStartedTicks, committedTicks,
                Stopwatch.Frequency);
            PerfLog($"[ND-95 request timing] requestTicks={_pendingCutRequestedTicks} " +
                $"queueMs={requestTiming.QueueMs:F3} workerMs={requestTiming.WorkerMs:F3} " +
                $"readyWaitMs={requestTiming.ReadyWaitMs:F3} stagingWallMs={requestTiming.StagingWallMs:F3} " +
                $"commitMs={requestTiming.CommitMs:F3} totalMs={requestTiming.TotalMs:F3} focused={Application.isFocused}");
            PerfLog($"  [async apply ND-76] mesh-feltoltes={stopwatch.Elapsed.TotalMilliseconds:F2}ms " +
                $"uploadMode={(buffers.StagedTerrain != null ? "ND85" : "single")} stageFrames={buffers.UploadStageFrames} " +
                $"stageTotal={buffers.UploadStageMs:F2}ms maxSlice={buffers.UploadMaxSliceMs:F2}ms stagedVertices={buffers.UploadStagedVertices} " +
                $"auxPipeline={(buffers.StagedLegacyWater != null ? "ND86" : "single")} auxPack={buffers.AuxiliaryPackMs:F2}ms " +
                $"auxStage={buffers.AuxiliaryStageMs:F2}ms auxJobs={buffers.AuxiliaryStageJobs} " +
                $"terrainPublish={buffers.TerrainPublishMs:F2}ms legacyAuxPublish={buffers.LegacyAuxPublishMs:F2}ms " +
                $"terrainPipeline={(buffers.StagedTerrain != null ? "ND94" : "single")} " +
                $"stageMesh={UploadMilliseconds(buffers.TerrainStageMeshTicks):F2}ms stageTarget={UploadMilliseconds(buffers.TerrainStageTargetTicks):F2}ms " +
                $"newTargets={buffers.NewTerrainTargets.Count} reusedTargets={buffers.ReusedTerrainTargets} " +
                $"terrainSwap={UploadMilliseconds(buffers.TerrainSwapTicks):F2}ms terrainDiagnostic={UploadMilliseconds(buffers.TerrainDiagnosticTicks):F2}ms " +
                $"terrainActivate={UploadMilliseconds(buffers.TerrainActivationTicks):F2}ms terrainDeactivate={UploadMilliseconds(buffers.TerrainDeactivationTicks):F2}ms " +
                $"terrainMask={buffers.TerrainMaskMs:F2}ms waterPublish={buffers.WaterPublishMs:F2}ms eviction={buffers.EvictionMs:F2}ms " +
                $"terrainMaskMode={(buffers.PreparedTerrainMask != null ? "ND89" : "single")} " +
                $"maskPlan={UploadMilliseconds(buffers.TerrainMaskPlanTicks):F2}ms maskApply={UploadMilliseconds(buffers.TerrainMaskApplyTicks):F2}ms " +
                $"maskUpload={UploadMilliseconds(buffers.TerrainMaskUploadTicks):F2}ms maskSnapshot={UploadMilliseconds(buffers.TerrainMaskSnapshotTicks):F2}ms maskRanges={buffers.TerrainMaskRanges} " +
                $"cut.Count={_currentCut.Count} dynLeaves={buffers.DynamicLeafCount} " +
                $"skippedOceanic={buffers.SkippedOceanicCount} cut={buffers.CutMs:F2}ms " +
                $"workCache=ND81 selection={buffers.SelectionMs:F2}ms balance={buffers.BalanceMs:F2}ms " +
                $"metricHits={buffers.MetricCacheHits} metricComputed={buffers.MetricEvaluations} metricEntries={buffers.MetricCacheEntries} " +
                $"geometryCache=ND96 geometryEntries={_terrainEvaluationCache?.Geometry.Count ?? 0} " +
                $"metricStorage=ND97 metricViewResets={_terrainEvaluationCache?.ViewResets ?? 0} metricFeedbackRefreshes={_terrainEvaluationCache?.FeedbackRefreshes ?? 0} " +
                $"geometryHits={_terrainEvaluationCache?.Geometry.Hits ?? 0} geometryComputed={_terrainEvaluationCache?.Geometry.Computed ?? 0} " +
                $"feedbackEntries={_terrainEvaluationCache?.Geometry.ActualCount ?? 0} feedbackAdded={buffers.GeometryFeedbackAdded} " +
                $"feedbackBounds={_terrainEvaluationCache?.Geometry.BoundsCount ?? 0} feedbackRejected={_terrainEvaluationCache?.Geometry.RejectedFeedback ?? 0} " +
                $"feedbackMeasured={buffers.GeometryFeedbackMeasured} feedbackUnidentified={buffers.GeometryFeedbackUnidentified} " +
                $"feedbackMs={buffers.GeometryFeedbackMs:F2} feedbackPending={buffers.GeometryRefinementPending} " +
                $"filter={buffers.FilterMs:F2}ms classification={buffers.ClassificationMs:F2}ms " +
                $"corners={buffers.CornersMs:F2}ms emit={buffers.EmitMs:F2}ms " +
                $"resolveCheck={buffers.ResolveCheckMs:F2}ms auxiliaryCopy={buffers.AuxiliaryCopyMs:F2}ms tileEmit={buffers.TileEmitMs:F2}ms " +
                $"changedChunks={buffers.ChangedChunkTerrain?.Count ?? 0}/{buffers.NewChunkGroups?.Count ?? 0} " +
                $"positionOnlyChunks={buffers.PositionOnlyTerrain?.Count ?? 0} " +
                $"chunkPacking={(buffers.BoundedChunks ? "ND80" : "fixed")} chunkMinLevel={buffers.MinimumChunkLevel} " +
                $"chunkLeafLimit={buffers.ChunkLeafLimit} maxChunkLeaves={buffers.MaxChunkLeaves} grouping={buffers.GroupingMs:F2}ms " +
                $"emittedLeaves={buffers.EmittedLeaves} reusedLeaves={buffers.ReusedLeaves} newSplits={buffers.NewSplits} deferredSplits={buffers.DeferredSplits} " +
                // Munka-konyveles: elvont (starved*) vs szuksegtelen (skip*)
                // megallasok - ld. LodSelectionWork doksija. A `deferredSplits`
                // ES a `starvedQuota` UGYANAZ a szam, a masik nevvel a
                // `[tilesample]` sorokhoz illeszkedik.
                $"cutBudget={buffers.RenderBudget} budgetMode={(adaptiveRenderBudget > 0 ? "manual" : "viewport")} " +
                $"starvedBudget={buffers.BudgetStops} starvedQuota={buffers.DeferredSplits} " +
                $"skipSufficient={buffers.SufficientStops} skipMaxLevel={buffers.MaxLevelStops} " +
                $"skipInvisible={buffers.InvisibleStops} " +
                $"balanceIterations={buffers.BalanceIterations} balanceSplits={buffers.BalanceSplits} " +
                $"earlyOceanExclusion=ND78 skippedSelectionBases={buffers.SkippedSelectionBases} " +
                $"fallbackLeaves={buffers.FallbackLeafCount} replacedBase={buffers.ReplacedBaseTiles.Count} " +
                $"maskIndices={buffers.MaskIndexCount} " +
                DescribeSurfaceLod(_currentCut, _pendingCutCamX, _pendingCutCamY, _pendingCutCamZ) + " " +
                $"requestAgeClock=ND95 requestAge={requestTiming.TotalMs:F1}ms");
            if (stopwatch.Elapsed.TotalMilliseconds > adaptiveRebuildWarningMs)
            {
                Debug.LogWarning(
                    $"PlanetGridMesh: [async] mesh-feltoltes {stopwatch.Elapsed.TotalMilliseconds:F1}ms " +
                    $"(cut merete {_currentCut.Count}) - a BuildCut ES az emit is a fo szalon KIVUL futott; ez a " +
                    "maradek a Unity mesh-upload (SetVertices/SetTriangles) koltsege. Ha rendszeresen magas, " +
                    $"csokkentsd az adaptiveRenderBudget-et. Reszletes naplo: {_perfLogPath}");
            }
        }

        /// <summary>
        /// FAZIS 3 (ND-47): a dinamikus mesh WORKER SZALON eloallitott,
        /// meg-fel-nem-toltott geometriaja (tiszta adat, nulla Unity-objektum) -
        /// a fo szal ebbol egyetlen lepesben feltolti a Unity mesh-eket
        /// (ApplyAdaptiveMeshBuffers). Igy a draga emit (per-tile korrekcio+
        /// sarok+szin+haromszogeles, elesben ~230ms 50k tile-nal) NEM a fo
        /// (renderelo) szalon fut.
        /// </summary>
        private sealed class AdaptiveMeshBuffers
        {
            public long WorkerStartedTicks, WorkerReadyTicks, WorkerObservedTicks;
            public bool GeometryRefinementPending;
            public int GeometryFeedbackAdded;
            public int GeometryFeedbackMeasured, GeometryFeedbackUnidentified;
            public double GeometryFeedbackMs;
            public HashSet<TileId> Cut;
            public Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> Vertices;
            public Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> Normals;
            public Dictionary<(RenderCategory Category, int Bucket), List<int>> Triangles;
            public Dictionary<(RenderCategory Category, int Bucket), List<Color>> Colors;
            public Dictionary<int, List<Vector3>> WaterVertices;
            public Dictionary<int, List<Vector3>> WaterNormals;
            public Dictionary<int, List<int>> WaterTriangles;
            public Dictionary<int, List<Color>> WaterColors;
            public List<Vector3> BorderVerts;
            public List<int> BorderIndices;
            public float WaterSurfaceRadius;
            public WaterLodSelection? WaterSelection;
            public AdaptiveMeshBuffers? IndependentWaterGeometry;
            public double WaterSelectionMs, WaterEmitMs;
            public int WaterColorSamples;
            public Dictionary<TileId, StagedTerrainMesh>? StagedTerrain;
            public int UploadStageFrames, UploadStagedVertices;
            public double UploadStageMs, UploadMaxSliceMs;
            public WaterMeshData? PreparedLegacyWater, PreparedIndependentWater;
            public bool PreparedBordersEnabled;
            public Bounds PreparedBorderBounds;
            public StagedAuxiliaryMesh? StagedLegacyWater, StagedIndependentWater, StagedBorders;
            public double AuxiliaryPackMs, AuxiliaryStageMs;
            public int AuxiliaryStageJobs;
            public double TerrainPublishMs, LegacyAuxPublishMs, TerrainMaskMs, WaterPublishMs, EvictionMs;
            public long TerrainStageMeshTicks, TerrainStageTargetTicks, TerrainSwapTicks, TerrainDiagnosticTicks,
                TerrainActivationTicks, TerrainDeactivationTicks;
            public readonly Dictionary<TileId, GameObject> NewTerrainTargets = new();
            public int ReusedTerrainTargets;
            public TerrainIndexMask.PreparedUpdate? PreparedTerrainMask;
            public HashSet<int>? PreparedTerrainHiddenQuads;
            public long TerrainMaskPlanTicks, TerrainMaskApplyTicks, TerrainMaskUploadTicks, TerrainMaskSnapshotTicks;
            public int TerrainMaskRanges;
            public int DynamicLeafCount;
            public int SkippedOceanicCount;
            public int FallbackLeafCount;
            public int MaskIndexCount;
            public HashSet<TileId> ReplacedBaseTiles = new();
            public Dictionary<TileId, ConcatenatedMesh> PositionOnlyTerrain = new();
            public Dictionary<TileId, List<Vector3>> NewChunkPositions = new();
            public double CutMs, FilterMs, ClassificationMs, CornersMs, EmitMs;
            public double SelectionMs, BalanceMs, ResolveCheckMs, AuxiliaryCopyMs, TileEmitMs;
            public int MetricCacheHits, MetricEvaluations, MetricCacheEntries;
            public int EmittedLeaves, ReusedLeaves, NewSplits, DeferredSplits;
            // A megallasok konyvelese - ld. LodSelectionWork doksija: elvont
            // munka (BudgetStops + DeferredSplits) vs szuksegtelen munka
            // (SufficientStops + MaxLevelStops + InvisibleStops + Skipped*).
            public int BudgetStops, SufficientStops, MaxLevelStops, InvisibleStops;
            /// <summary>ND-116: a 2:1 kiegyensulyozas tenyleges munkaja.</summary>
            public int BalanceIterations, BalanceSplits;
            /// <summary>A vagashoz TENYLEGESEN hasznalt level-budget (ResolveRenderBudget).</summary>
            public int RenderBudget = AdaptiveQuadTree.MinimumRenderBudget;
            public int SkippedSelectionBases;
            public bool BoundedChunks;
            public int ChunkLeafLimit, MaxChunkLeaves, MinimumChunkLevel;
            public double GroupingMs;
            public LodSelectionTrace? SelectionTrace;
            public LodCoverage? DiagnosticCoverage;
            public Dictionary<TileId, CachedLodChunk> ChunkCache = new();
            public bool HasNadirDiagnostic, NadirOceanBlocked, NadirUnderWater;
            public int NadirTerrainLevel;
            public double NadirSurfaceClearance, NadirMorphAlpha, NadirDiagnosticMs;
            public double NadirProxyRadiusError = double.NaN;
            public SurfaceQuad NadirTerrainQuad;
            // FAZIS 3 (ND-47): a terep-mesh MAR konkatenalt (worker szalon) verzioja
            // - a fo szal csak feltolti (UploadConcatenatedMultiMaterialMesh).
            // CSAK a NEM chunkolt uton hasznalt (useChunkedDynamicMesh=false).
            public ConcatenatedMesh TerrainConcat;

            // CHUNKOLT UT (useChunkedDynamicMesh=true): csak a VALTOZOTT/UJ
            // chunk-ok kapnak uj, konkatenalt geometriat; a valtozatlanok
            // GameObject-jehez a fo szal HOZZA SEM NYUL. `RemovedChunkRoots` azok
            // a chunk-gyokerek, amiknek MAR NINCS levele az uj cutban - ezek
            // GameObject-jet a fo szal deaktivalja. `NewChunkGroups` a TELJES uj
            // chunk-csoportositas, ami a kovetkezo korben `_previousChunkGroups`-kent
            // szolgal a diffhez.
            public Dictionary<TileId, ConcatenatedMesh> ChangedChunkTerrain;
            public List<TileId> RemovedChunkRoots;
            public Dictionary<TileId, HashSet<TileId>> NewChunkGroups;
        }

        /// <summary>
        /// FAZIS 3 (ND-47): a dinamikus geometria eloallitasa (leaf-gyujtes +
        /// per-tile klasszifikacio/sarok/emit) TISZTA adatba, Unity-API NELKUL -
        /// WORKER SZALON is biztonsagosan futtathato. A klasszifikaciot CPU-ra
        /// kenyszeriti (forceCpu), mert a GPU-dispatch csak a fo szalon
        /// megengedett. A cache-ekbe (klasszifikacio/sarok) IR - ez single-flight
        /// mellett biztonsagos (egyszerre egy worker, a fo szal a task futasa
        /// alatt nem nyul a cache-ekhez; ld. TryApplyCompletedAsyncCut).
        /// </summary>
        private AdaptiveMeshBuffers ComputeAdaptiveMeshBuffersCpu(HashSet<TileId> cut, System.Threading.CancellationToken cancellation = default)
        {
            var phaseStopwatch = Stopwatch.StartNew();
            var b = new AdaptiveMeshBuffers
            {
                Cut = cut,
                WaterVertices = new Dictionary<int, List<Vector3>>(),
                WaterNormals = new Dictionary<int, List<Vector3>>(),
                WaterTriangles = new Dictionary<int, List<int>>(),
                WaterColors = new Dictionary<int, List<Color>>(),
                BorderVerts = new List<Vector3>(),
                BorderIndices = new List<int>(),
                WaterSurfaceRadius = radius + (float)(_adaptiveSeaLevel * elevationScale),
            };
            ComputeIndependentWater(b, cancellation);
            phaseStopwatch.Restart();

            var dynamicLeaves = new List<TileId>();
            foreach (TileId t in cut)
            {
                if (t.Level <= adaptiveBaseLevel)
                    continue;
                if (IsBaseAncestorOceanic(t))
                {
                    b.SkippedOceanicCount++;
                    continue;
                }
                dynamicLeaves.Add(t);
            }
            LodCoverage coverage = LodCoverage.Complete(dynamicLeaves, adaptiveBaseLevel);
            b.ReplacedBaseTiles = coverage.Roots;
            b.FallbackLeafCount = coverage.FallbackCount;
            var leaves = new TileId[coverage.Leaves.Count];
            coverage.Leaves.CopyTo(leaves);
            Array.Sort(leaves, (a, c) => a.Value.CompareTo(c.Value));
            b.DynamicLeafCount = leaves.Length;
            b.FilterMs = phaseStopwatch.Elapsed.TotalMilliseconds;

            cancellation.ThrowIfCancellationRequested();

            phaseStopwatch.Restart();
            PrecomputeClassificationsInParallel(leaves, forceCpu: true);
            b.ClassificationMs = phaseStopwatch.Elapsed.TotalMilliseconds;
            cancellation.ThrowIfCancellationRequested();
            phaseStopwatch.Restart();
            PrecomputeCornersInParallel(leaves);
            b.CornersMs = phaseStopwatch.Elapsed.TotalMilliseconds;
            cancellation.ThrowIfCancellationRequested();
            phaseStopwatch.Restart();

            b.DiagnosticCoverage = logDrawnTileSizes ? coverage : null;
            _activeCornerResolver = new LodCornerResolver(adaptiveBaseLevel, coverage, GetRawSurfaceQuad);
            try
            {
                if (useChunkedDynamicMesh)
                {
                    // ND-76: topológia + feloldott csúcspozíciók alapján a
                    // változatlan chunk teljes terrain/víz/border emitje kimarad.
                    // A víz/border feltöltése továbbra is globális.
                    var groupingTimer = Stopwatch.StartNew();
                    b.BoundedChunks = useBoundedDynamicChunks;
                    b.ChunkLeafLimit = b.BoundedChunks ? DynamicMeshChunking.DefaultMaxLeavesPerChunk : 0;
                    int chunkLevel = Math.Clamp(dynamicChunkLevel,
                        b.BoundedChunks ? 0 : adaptiveBaseLevel, adaptiveMaxLevel);
                    b.MinimumChunkLevel = chunkLevel;
                    Dictionary<TileId, HashSet<TileId>> newGroups = b.BoundedChunks
                        ? DynamicMeshChunking.GroupByLeafBudget(leaves, chunkLevel, b.ChunkLeafLimit,
                            _previousChunkGroups, cancellation)
                        : DynamicMeshChunking.GroupByChunk(leaves, chunkLevel);
                    foreach (var group in newGroups.Values)
                        b.MaxChunkLeaves = Math.Max(b.MaxChunkLeaves, group.Count);
                    b.GroupingMs = groupingTimer.Elapsed.TotalMilliseconds;
                    DynamicMeshChunking.ChunkDiff diff = DynamicMeshChunking.DiffChunks(_previousChunkGroups, newGroups);
                    var changedSet = new HashSet<TileId>(diff.ChangedOrNewChunks);

                    var changedChunkTerrain = new Dictionary<TileId, ConcatenatedMesh>();
                    b.PositionOnlyTerrain = new Dictionary<TileId, ConcatenatedMesh>();
                    b.NewChunkPositions = new Dictionary<TileId, List<Vector3>>();
                    var chunkTimer = new Stopwatch();

                    foreach (KeyValuePair<TileId, HashSet<TileId>> group in newGroups)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        chunkTimer.Restart();
                        bool changed = changedSet.Contains(group.Key);
                        var orderedLeaves = new List<TileId>(group.Value);
                        orderedLeaves.Sort((a, c) => a.Value.CompareTo(c.Value));
                        List<Vector3> resolved = CaptureResolvedPositions(orderedLeaves);
                        bool hasCached = _previousChunkCache.TryGetValue(group.Key, out CachedLodChunk cached);
                        bool canReuse = !changed && hasCached
                            && DynamicMeshChunking.SamePositions(cached.ResolvedPositions, resolved);
                        b.ResolveCheckMs += chunkTimer.Elapsed.TotalMilliseconds;
                        if (canReuse)
                        {
                            b.ChunkCache[group.Key] = cached;
                            b.NewChunkPositions[group.Key] = cached.Terrain.Vertices;
                            chunkTimer.Restart();
                            AppendAuxiliaryBuffers(cached.Auxiliary,b);
                            b.AuxiliaryCopyMs += chunkTimer.Elapsed.TotalMilliseconds;
                            b.ReusedLeaves += orderedLeaves.Count;
                            continue;
                        }
                        chunkTimer.Restart();
                        AdaptiveMeshBuffers auxiliary = CreateAuxiliaryBuffers(b.WaterSurfaceRadius);
                        var vertsByKey = new Dictionary<(RenderCategory, int), List<Vector3>>();
                        var normalsByKey = new Dictionary<(RenderCategory, int), List<Vector3>>();
                        var trisByKey = new Dictionary<(RenderCategory, int), List<int>>();
                        var colorsByKey = new Dictionary<(RenderCategory, int), List<Color>>();

                        for (int leafIndex = 0; leafIndex < orderedLeaves.Count; leafIndex++)
                        {
                            TileId leaf = orderedLeaves[leafIndex];
                            EmitAdaptiveTile(
                                leaf, vertsByKey, normalsByKey, trisByKey, colorsByKey,
                                auxiliary.WaterVertices, auxiliary.WaterNormals, auxiliary.WaterTriangles, auxiliary.WaterColors,
                                auxiliary.BorderVerts, auxiliary.BorderIndices, b.WaterSurfaceRadius, dynamicLayerRadialBias,
                                replaceStaticTerrain: true, resolvedPositions: resolved, resolvedStart: leafIndex * 4);
                        }

                        ConcatenatedMesh mesh = ConcatenateMultiMaterialBuckets(vertsByKey, normalsByKey, trisByKey, colorsByKey);
                        AttachTerrainTileIds(mesh, orderedLeaves);
                        b.ChunkCache[group.Key] = new CachedLodChunk(resolved,mesh,auxiliary);
                        b.EmittedLeaves += orderedLeaves.Count;
                        b.TileEmitMs += chunkTimer.Elapsed.TotalMilliseconds;
                        chunkTimer.Restart();
                        AppendAuxiliaryBuffers(auxiliary,b);
                        b.AuxiliaryCopyMs += chunkTimer.Elapsed.TotalMilliseconds;
                        b.NewChunkPositions[group.Key] = mesh.Vertices;
                        if (changed || !hasCached || !_previousChunkPositions.TryGetValue(group.Key, out List<Vector3> previousPositions))
                            changedChunkTerrain[group.Key] = mesh;
                        else if (!DynamicMeshChunking.SamePositions(previousPositions, mesh.Vertices))
                            b.PositionOnlyTerrain[group.Key] = mesh;
                    }

                    b.ChangedChunkTerrain = changedChunkTerrain;
                    b.RemovedChunkRoots = diff.RemovedChunks;
                    b.NewChunkGroups = newGroups;
                }
                else
                {
                    b.Vertices = new Dictionary<(RenderCategory, int), List<Vector3>>();
                    b.Normals = new Dictionary<(RenderCategory, int), List<Vector3>>();
                    b.Triangles = new Dictionary<(RenderCategory, int), List<int>>();
                    b.Colors = new Dictionary<(RenderCategory, int), List<Color>>();
                    foreach (TileId leaf in leaves)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        EmitAdaptiveTile(
                            leaf, b.Vertices, b.Normals, b.Triangles, b.Colors,
                            b.WaterVertices, b.WaterNormals, b.WaterTriangles, b.WaterColors,
                            b.BorderVerts, b.BorderIndices, b.WaterSurfaceRadius, dynamicLayerRadialBias, replaceStaticTerrain: true);
                        b.EmittedLeaves++;
                    }
                    // FAZIS 3 (ND-47): a terep-bucketek konkatenalasa MAR ITT, a worker
                    // szalon - a fo szalon (ApplyAdaptiveMeshBuffers) mar csak a natv
                    // Unity mesh-feltoltes marad (a korabbi fo-szalu AddRange +
                    // RecalculateBounds lekerul).
                    b.TerrainConcat = ConcatenateMultiMaterialBuckets(b.Vertices, b.Normals, b.Triangles, b.Colors);
                    AttachTerrainTileIds(b.TerrainConcat, leaves);
                }

                b.EmitMs = phaseStopwatch.Elapsed.TotalMilliseconds;
                CaptureGeometryFeedback(b, cancellation);
                var diagnosticStopwatch = Stopwatch.StartNew();
                CaptureNadirDiagnostic(b, coverage);
                b.NadirDiagnosticMs = diagnosticStopwatch.Elapsed.TotalMilliseconds;
                PrepareAuxiliaryUploads(b, cancellation);
                return b;
            }
            finally { _activeCornerResolver = null; }
        }

        /// <summary>
        /// FAZIS 3 (ND-47): a WORKER altal eloallitott geometria feltoltese a
        /// Unity mesh-ekbe - ez a resz Unity-API-t hasznal, tehat KIZAROLAG a FO
        /// szalon (Update -> TryApplyCompletedAsyncCut) fut.
        /// </summary>
        private void ApplyAdaptiveMeshBuffers(AdaptiveMeshBuffers b)
        {
            try { ApplyAdaptiveMeshBuffersCore(b); }
            catch (Exception applyError)
            {
                // Sikertelen feltöltés nem igazol új fedést. Visszaállítjuk
                // az alapot, a félkész dinamikus geometriát kikapcsoljuk.
                var errors = new List<Exception> { applyError };
                try
                {
                    // A két statikus réteg helyreállítását egymás hibája se akadályozza.
                    try { RestoreTerrainCoverageAfterFailure(); }
                    catch (Exception recoveryError) { errors.Add(recoveryError); }
                    try { RestoreWaterCoverageAfterFailure(); }
                    catch (Exception recoveryError) { errors.Add(recoveryError); }
                }
                finally
                {
                    ResetIndependentWaterRendering(restoreStaticIndices: false);
                    foreach (var chunk in _dynamicChunkGameObjects)
                    {
                        if (chunk.Value != null) chunk.Value.SetActive(false);
                        RememberInactiveTerrainChunk(chunk.Key);
                    }
                    foreach (string name in new[] { "DynamicRefined", "DynamicWater", "DynamicBorders" })
                    {
                        Transform child = transform.Find(name);
                        if (child != null) child.gameObject.SetActive(false);
                    }
                    _previousChunkGroups.Clear();
                    _previousChunkPositions.Clear();
                    _previousChunkCache.Clear();
                    _appliedSelectionTrace = null;
                    _appliedDiagnosticCoverage = null;
                    _appliedCutWork = default;
                    _hasLastCutCameraPosition = false;
                }
                if (errors.Count > 1)
                    throw new AggregateException("ND-92: a LOD-commit és a statikus fedés helyreállítása is hibás.", errors);
                throw;
            }
            // Külön a mesh-alkalmazás hibakezelésétől: pusztán megfigyelés.
            if (b.HasNadirDiagnostic)
                PerfLog(DescribeNadirDiagnostic(b));
        }

        private void CaptureNadirDiagnostic(AdaptiveMeshBuffers b, LodCoverage coverage)
        {
            double x = _lastCutCameraCoreX, y = _lastCutCameraCoreY, z = _lastCutCameraCoreZ;
            double distance = Math.Sqrt(x * x + y * y + z * z);
            if (distance < 1e-9) return;
            TileId sample = TileGeometry.FromPosition(x, y, z, Math.Max(adaptiveBaseLevel, adaptiveMaxLevel));
            TileId leaf = coverage.FindRenderedLeaf(sample, adaptiveBaseLevel);
            // Pontosan ugyanaz a morph/közösél-feloldás, mint az emitnél;
            // az _activeCornerResolver a teljes kérés lezárásáig él.
            GetAdaptiveCorners(leaf, out Vector3 a, out Vector3 c, out Vector3 d, out Vector3 e);
            b.NadirTerrainQuad = new SurfaceQuad(ToSurfacePoint(a), ToSurfacePoint(c), ToSurfacePoint(d), ToSurfacePoint(e));
            b.NadirTerrainLevel = leaf.Level;
            b.NadirOceanBlocked = IsBaseAncestorOceanic(sample);
            b.NadirMorphAlpha = leaf.Level > adaptiveBaseLevel ? ComputeGeomorphAlpha(leaf) : 1;
            // Egyetlen modellpont kérésenként; semmilyen kiválasztási döntést nem befolyásol.
            double terrainRadius = ComputeDisplacedRadius(x / distance, y / distance, z / distance,
                _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters);
            double seaRadius = radius + _adaptiveSeaLevel * elevationScale;
            b.NadirUnderWater = terrainRadius < seaRadius;
            b.NadirSurfaceClearance = distance - Math.Max(terrainRadius, seaRadius);
            if (_requestedTerrainLodProxy != null)
                b.NadirProxyRadiusError = _requestedTerrainLodProxy.RadiusAt(sample) - Math.Max(terrainRadius, seaRadius);
            b.HasNadirDiagnostic = true;
        }

        private string DescribeNadirDiagnostic(AdaptiveMeshBuffers b)
        {
            SurfacePoint Clip(SurfacePoint p)
            {
                var local = new Vector4((float)p.X, (float)p.Y, (float)p.Z, 1);
                Vector4 camera = _requestedLodLocalToCamera * local;
                if (-camera.z <= _requestedLodNearClip)
                    return new SurfacePoint(0, 0, -1); // Nem osztunk a kamera mögötti/near-plane ponttal.
                Vector4 clip = _requestedLodLocalToClip * local;
                return new SurfacePoint(clip.x, clip.y, clip.w);
            }
            SurfaceQuad q = b.NadirTerrainQuad;
            double pixels = AdaptiveViewState.QuadPixelDiameter(
                new SurfaceQuad(Clip(q.P00), Clip(q.P10), Clip(q.P11), Clip(q.P01)),
                _requestedLodPixelWidth, _requestedLodPixelHeight);
            return $"  [ND-72 render] nadirTerrainL={b.NadirTerrainLevel} " +
                $"nadirOceanBlocked={b.NadirOceanBlocked} nadirUnderWater={b.NadirUnderWater} " +
                $"nadirSurfaceClearanceUnits={b.NadirSurfaceClearance:F6} nadirMorphAlpha={b.NadirMorphAlpha:F3} " +
                $"nadirTerrainQuadPx={pixels:F2} diagnostic={b.NadirDiagnosticMs:F2}ms " +
                $"proxyRadiusErrorUnits={b.NadirProxyRadiusError:F6} " +
                "(request-view; terrain quad, not water/occlusion; NaN=invalid projection)";
        }

        private void ApplyAdaptiveMeshBuffersCore(AdaptiveMeshBuffers b)
        {
            var publishTimer = Stopwatch.StartNew();
            if (b.ChangedChunkTerrain != null)
            {
                Transform oldUnchunked = transform.Find("DynamicRefined");
                if (oldUnchunked != null) oldUnchunked.gameObject.SetActive(false);
                // CSAK a VALTOZOTT/UJ chunk-ok GameObject-jet toltjuk fel ujra -
                // ez a chunkolas teljes celja (ld. useChunkedDynamicMesh doksija).
                foreach (KeyValuePair<TileId, ConcatenatedMesh> kv in b.ChangedChunkTerrain)
                {
                    if (b.StagedTerrain != null) PublishStagedTerrain(b, kv.Key);
                    else
                    {
                        GameObject chunkGo = GetOrCreateChunkRenderTarget(kv.Key);
                        chunkGo.SetActive(true);
                        UploadConcatenatedMultiMaterialMesh(chunkGo, kv.Value, ownChunkMesh: true);
                    }
                }
                foreach (KeyValuePair<TileId, ConcatenatedMesh> kv in b.PositionOnlyTerrain)
                {
                    if (b.StagedTerrain != null)
                    {
                        PublishStagedTerrain(b, kv.Key);
                        continue;
                    }
                    Mesh mesh = _dynamicChunkGameObjects[kv.Key].GetComponent<MeshFilter>().sharedMesh;
                    mesh.SetVertices(kv.Value.Vertices, 0, kv.Value.Vertices.Count, MeshUpdateFlags.DontRecalculateBounds);
                    mesh.bounds = new Bounds((kv.Value.BoundsMin + kv.Value.BoundsMax) * .5f,
                        kv.Value.BoundsMax - kv.Value.BoundsMin);
                    RememberDrawnPositions(_dynamicChunkGameObjects[kv.Key], kv.Value.Vertices);
                }
                // ND-93: a már nem használt chunkok a korlátos inaktív sorba kerülnek.
                long deactivateStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                foreach (TileId removedRoot in b.RemovedChunkRoots)
                {
                    if (_dynamicChunkGameObjects.TryGetValue(removedRoot, out GameObject? removedGo) && removedGo != null)
                        removedGo.SetActive(false);
                    RememberInactiveTerrainChunk(removedRoot);
                }
                b.TerrainDeactivationTicks = System.Diagnostics.Stopwatch.GetTimestamp() - deactivateStarted;
                _previousChunkGroups = b.NewChunkGroups;
                _previousChunkPositions = b.NewChunkPositions;
                _previousChunkCache = b.ChunkCache;
            }
            else
            {
                GameObject dynamicTerrainGo = GetOrCreateChildRenderTarget("DynamicRefined");
                dynamicTerrainGo.SetActive(true);
                foreach (var chunk in _dynamicChunkGameObjects)
                {
                    if (chunk.Value != null) chunk.Value.SetActive(false);
                    RememberInactiveTerrainChunk(chunk.Key);
                }
                _previousChunkGroups.Clear();
                _previousChunkPositions.Clear();
                _previousChunkCache.Clear();
                // FAZIS 3: a terep MAR konkatenalt (worker szalon) - itt csak feltoltjuk.
                UploadConcatenatedMultiMaterialMesh(dynamicTerrainGo, b.TerrainConcat);
            }
            b.TerrainPublishMs = publishTimer.Elapsed.TotalMilliseconds;
            publishTimer.Restart();
            if (b.StagedBorders != null) PublishAuxiliaryUpload("Borders", "DynamicBorders", b.StagedBorders);
            else BuildBorders(b.BorderVerts, b.BorderIndices, "DynamicBorders");
            if (b.StagedLegacyWater != null) PublishAuxiliaryUpload("LegacyWater", "DynamicWater", b.StagedLegacyWater);
            else BuildWaterSurface(b.WaterVertices, b.WaterNormals, b.WaterTriangles, b.WaterColors, "DynamicWater");
            b.LegacyAuxPublishMs = publishTimer.Elapsed.TotalMilliseconds;
            publishTimer.Restart();
            b.MaskIndexCount = ApplyTerrainCoverage(b.ReplacedBaseTiles, b);
            b.TerrainMaskMs = publishTimer.Elapsed.TotalMilliseconds;
            publishTimer.Restart();
            ApplyIndependentWater(b);
            b.WaterPublishMs = publishTimer.Elapsed.TotalMilliseconds;
            _drawnLodAppliedAt = Time.unscaledTime;
            publishTimer.Restart();
            EvictCornerCacheIfNeeded();
            EvictTileClassificationCacheIfNeeded();
            b.EvictionMs = publishTimer.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// A jelenlegi `_currentCut` (valtozo-szintu aktiv level-ek) mesh-be
        /// epitese. ND-128 (2026-09-21) ota EGYETLEN ut van: a CPU-emit
        /// (`ComputeAdaptiveMeshBuffersCpu`) - ugyanaz, amit az aszinkron ag
        /// is futtat worker szalon, tehat a ket ut azonos fedest/varratot ad
        /// (ND-70). A korabbi GPU-geometria ag torolve: merve LASSABB volt
        /// (sarok-dedup nelkul tile-onkent 5x szamolta a zaj-lancot),
        /// geomorphing nelkul futott, es az elavult shader-elevacio miatt a
        /// tile-ok ~22%-at a tengerszint MASIK oldalara tette volna.
        /// A metodus itteni, kulon CPU-aga IS torolve lett: elerhetetlen
        /// (halott) kod volt, mert a GPU-kapcsolo nelkul a fenti hivas mar
        /// visszatert. Ld. docs/04-decisions.md ND-128 es ND-120.
        /// </summary>
        private void RebuildAdaptiveMesh()
        {
            if (_currentCut == null)
                return;

            ApplyAdaptiveMeshBuffers(ComputeAdaptiveMeshBuffersCpu(_currentCut));
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
            Dictionary<(RenderCategory Category, int Bucket), List<Color>> colorsByKey,
            Dictionary<int, List<Vector3>> waterVerticesByBucket,
            Dictionary<int, List<Vector3>> waterNormalsByBucket,
            Dictionary<int, List<int>> waterTrianglesByBucket,
            Dictionary<int, List<Color>> waterColorsByBucket,
            List<Vector3> borderVerts, List<int> borderIndices,
            float waterSurfaceRadius, float radialBias, int staticDenseIndex = -1,
            StaticMeshBuckets? staticBuckets = null, bool replaceStaticTerrain = false,
            List<Vector3>? resolvedPositions = null, int resolvedStart = 0,
            bool emitTerrain = true, bool emitWater = true)
        {
            bool useStaticDenseData = staticDenseIndex >= 0
                && id.Level == _staticRenderDataLevel
                && (uint)staticDenseIndex < (uint)_staticTileClassifications.Length;
            AdaptiveTileClassification classification = useStaticDenseData
                ? _staticTileClassifications[staticDenseIndex]
                : GetOrComputeTileClassification(id);
            double elevation = classification.Elevation;
            bool isOceanic = classification.IsOceanic;
            Biome biome = classification.Biome;
            RenderCategory category = classification.Category;
            int bucket = classification.Bucket;
            var key = (category, bucket);

            List<Vector3> vertices;
            List<Vector3> normals;
            List<int> triangles;
            List<Color> colors;
            if (staticBuckets != null)
            {
                int terrainSlot = StaticTerrainBucketIndex(category, bucket);
                vertices = staticBuckets.TerrainVertices[terrainSlot];
                normals = staticBuckets.TerrainNormals[terrainSlot];
                triangles = staticBuckets.TerrainTriangles[terrainSlot];
                colors = staticBuckets.TerrainColors[terrainSlot];
            }
            else
            {
                GetOrAddLists(verticesByKey, normalsByKey, trianglesByKey, colorsByKey, key,
                    out vertices, out normals, out triangles, out colors);
            }

            id.GetUV(out uint u, out uint v);
            int lvl = id.Level;
            Vector3 p00, p10, p11, p01;
            Vector3 pn00, pn10, pn11, pn01;
            int staticCorner00 = -1;
            int staticCorner10 = -1;
            int staticCorner11 = -1;
            int staticCorner01 = -1;
            if (useStaticDenseData)
            {
                int side = (1 << lvl) + 1;
                staticCorner00 = id.Face * side * side + (int)u * side + (int)v;
                staticCorner10 = staticCorner00 + side;
                staticCorner11 = staticCorner10 + 1;
                staticCorner01 = staticCorner00 + 1;
                p00 = _staticCornerPositions[staticCorner00];
                p10 = _staticCornerPositions[staticCorner10];
                p11 = _staticCornerPositions[staticCorner11];
                p01 = _staticCornerPositions[staticCorner01];
                pn00 = _staticCornerNormals[staticCorner00];
                pn10 = _staticCornerNormals[staticCorner10];
                pn11 = _staticCornerNormals[staticCorner11];
                pn01 = _staticCornerNormals[staticCorner01];
            }
            else
            {
                if (resolvedPositions != null)
                {
                    p00 = resolvedPositions[resolvedStart]; p10 = resolvedPositions[resolvedStart + 1];
                    p11 = resolvedPositions[resolvedStart + 2]; p01 = resolvedPositions[resolvedStart + 3];
                }
                else GetAdaptiveCorners(id, out p00, out p10, out p11, out p01);
                pn00 = GetOrComputePersistentCornerNormal(id.Face, lvl, u, v);
                pn10 = GetOrComputePersistentCornerNormal(id.Face, lvl, u + 1, v);
                pn11 = GetOrComputePersistentCornerNormal(id.Face, lvl, u + 1, v + 1);
                pn01 = GetOrComputePersistentCornerNormal(id.Face, lvl, u, v + 1);
            }
            if (radialBias != 0f && !replaceStaticTerrain)
            {
                p00 += p00.normalized * radialBias;
                p10 += p10.normalized * radialBias;
                p11 += p11.normalized * radialBias;
                p01 += p01.normalized * radialBias;
            }
            // ND-55: a lejto-erzekeny, DE tile-hatarokon (es LOD-hatarokon
            // is, mert a kulcs a face/level/uv, nem a hivo tile) folytonos
            // normal - ld. ComputeCornerNormalViaFiniteDifference. Ugyanaz
            // a MEGOSZTOTT sarok-cache-mintazat, mint a szinnel
            // (_persistentCornerColorCache) - PrecomputeCornersInParallel
            // MAR feltoltotte parhuzamosan.
            if (!emitTerrain)
            {
                // ND-123: a parhuzamos statikus emit ELSO passza CSAK a terep-
                // quadokat irja (terep-slot szerint particionalva), a MASODIK
                // CSAK a viz-quadokat (viz-bucket szerint). Igy minden szal
                // KIZAROLAGOS listakra ir, es a sorrend bucketen belul
                // valtozatlanul tile-index szerint novekvo - ld.
                // EmitStaticBaseLayerInParallel.
            }
            else if (IsContinuousTerrainCategory(category))
            {
                // TELJESITMENY: a szin a MEGOSZTOTT sarok-cache-bol jon
                // (PrecomputeCornersInParallel MAR feltoltotte parhuzamosan),
                // NEM itt, az egyszalu EmitAdaptiveTile-ban szamolodik ujra -
                // ld. _persistentCornerColorCache doksija.
                Color cc00 = useStaticDenseData ? _staticCornerColors[staticCorner00] : GetOrComputePersistentCornerColor(id.Face, lvl, u, v);
                Color cc10 = useStaticDenseData ? _staticCornerColors[staticCorner10] : GetOrComputePersistentCornerColor(id.Face, lvl, u + 1, v);
                Color cc11 = useStaticDenseData ? _staticCornerColors[staticCorner11] : GetOrComputePersistentCornerColor(id.Face, lvl, u + 1, v + 1);
                Color cc01 = useStaticDenseData ? _staticCornerColors[staticCorner01] : GetOrComputePersistentCornerColor(id.Face, lvl, u, v + 1);
                AddQuad(vertices, normals, triangles, colors, cc00, cc10, cc11, cc01, pn00, pn10, pn11, pn01, p00, p10, p11, p01);
            }
            else
            {
                Color cUniform = CategoryColor(category, bucket);
                AddQuad(vertices, normals, triangles, colors, cUniform, cUniform, cUniform, cUniform, pn00, pn10, pn11, pn01, p00, p10, p11, p01);
            }

            // A vizfelszin (tengerszintnel) MINDEN oceani tile fole kerul -
            // beleertve a SEAICE-t is. Enelkul a tengeri jeg a sajat MELY
            // oceanfenek-magassagan renderelodott (kulon vizreteg nelkul),
            // ezert a kornyezo, tengerszintu nyilt-oceani viz melle egy
            // "beszakadt" godorkent latszott (felhasznaloi eszrevetel: a
            // polusnal a jeg melyen beszakadt, a tenger nem tolti be). A
            // felszin OPAK, ezert a jegszinu, tengerszintu lap elrejti a mely
            // fenekgeometriat - a tengeri jeg most a nyilt vizzel egy szinten,
            // laposan ul, csak FEHER (jeg) szinnel a kek helyett.
            if (emitWater && isOceanic && (biome == Biome.Ocean || biome == Biome.SeaIce)
                && !(replaceStaticTerrain && _requestedIndependentWater && _waterLodSource!.ContainsWater(id)))
            {
                TileGeometry.GetContinuousBounds(id, out double uMin, out double uMax, out double vMin, out double vMax);
                Vector3 wp00 = ToWaterVector3(id.Face, uMin, vMin, waterSurfaceRadius);
                Vector3 wp10 = ToWaterVector3(id.Face, uMax, vMin, waterSurfaceRadius);
                Vector3 wp11 = ToWaterVector3(id.Face, uMax, vMax, waterSurfaceRadius);
                Vector3 wp01 = ToWaterVector3(id.Face, uMin, vMax, waterSurfaceRadius);
                if (radialBias != 0f)
                {
                    wp00 += wp00.normalized * radialBias;
                    wp10 += wp10.normalized * radialBias;
                    wp11 += wp11.normalized * radialBias;
                    wp01 += wp01.normalized * radialBias;
                }

                double depth = _adaptiveSeaLevel - elevation;
                int waterBucket = WaterDepthBucket(depth);
                List<Vector3> waterVerts;
                List<Vector3> waterNormals;
                List<int> waterTriangles;
                List<Color> waterColors;
                if (staticBuckets != null)
                {
                    staticBuckets.WaterTiles[waterBucket].Add(id);
                    waterVerts = staticBuckets.WaterVertices[waterBucket];
                    waterNormals = staticBuckets.WaterNormals[waterBucket];
                    waterTriangles = staticBuckets.WaterTriangles[waterBucket];
                    waterColors = staticBuckets.WaterColors[waterBucket];
                }
                else if (!waterVerticesByBucket.TryGetValue(waterBucket, out waterVerts))
                {
                    waterVerts = new List<Vector3>();
                    waterNormals = new List<Vector3>();
                    waterTriangles = new List<int>();
                    waterColors = new List<Color>();
                    waterVerticesByBucket[waterBucket] = waterVerts;
                    waterNormalsByBucket[waterBucket] = waterNormals;
                    waterTrianglesByBucket[waterBucket] = waterTriangles;
                    waterColorsByBucket[waterBucket] = waterColors;
                }
                else
                {
                    waterNormals = waterNormalsByBucket[waterBucket];
                    waterTriangles = waterTrianglesByBucket[waterBucket];
                    waterColors = waterColorsByBucket[waterBucket];
                }
                // ND-60: ld. a statikus alapreteg azonos javitasa - a viz szine
                // MINDIG a valodi melysegbol jon, fuggetlenul a homerseklettol/
                // szelessegtol (a korabbi isSeaIceRendered-fehér kapcsolo a
                // durva racson egy latitude-tisztan kor alaku, eles hatart adott).
                Color wc00 = ContinuousWaterCornerColor(p00, _adaptiveSeaLevel);
                Color wc10 = ContinuousWaterCornerColor(p10, _adaptiveSeaLevel);
                Color wc11 = ContinuousWaterCornerColor(p11, _adaptiveSeaLevel);
                Color wc01 = ContinuousWaterCornerColor(p01, _adaptiveSeaLevel);
                AddQuad(waterVerts, waterNormals, waterTriangles,
                    waterColors, wc00, wc10, wc11, wc01, wp00, wp10, wp11, wp01);
            }

            // ND-65: BuildBorders kikapcsolt allapotban eldobja a listakat,
            // ezert ilyenkor ne epitsunk fel 393k tile-nyi hasznalatlan adatot.
            if (showBorders)
            {
                int b = borderVerts.Count;
                borderVerts.Add(p00); borderVerts.Add(p10); borderVerts.Add(p11); borderVerts.Add(p01);
                borderIndices.Add(b + 0); borderIndices.Add(b + 1);
                borderIndices.Add(b + 1); borderIndices.Add(b + 2);
                borderIndices.Add(b + 2); borderIndices.Add(b + 3);
                borderIndices.Add(b + 3); borderIndices.Add(b + 0);
            }
        }

        /// <summary>
        /// Egy level-6(-referencia) folyo-tile-e - a leaf a level fole (finomabb)
        /// eseten a level-referencia osere visszasetalva (a Morton-hierarchia
        /// miatt olcso bitmuvelet), level ALATTI (durvabb) leaf eseten NEM
        /// (egy durva leaf tobb referencia-tile-ot fedne le, nincs egyertelmu
        /// egyezes - dokumentalt egyszerusites, ld. a feladat osszefoglaloja).
        /// </summary>
        private bool IsAdaptiveLakeTile(TileId id) => IsInReferenceLevelSet(id, _adaptiveLakeTiles);

        // Jegsapka-partvonal zaj-perturbacio (backlog "Pólusi jég — klímamodell-
        // vezérelt zajos partvonal", 2026-09-06): amplitudo Kelvinben, frekvencia
        // a DomainWarp (kontinens-lepteku, 2.0) mintajahoz kepest jelentosen
        // magasabb, mert itt PARTVONAL-lepteku (reference-tile-on beluli)
        // hullamzas a cel, nem kontinens-lepteku eltolas. KALIBRALATLAN - elo
        // Unity-ellenorzes hatra, ld. docs/backlog.md.
        private const double IceBoundaryJitterAmplitudeK = 4.0;
        private const double IceBoundaryJitterFrequency = 24.0;
        private const int IceBoundaryJitterOctaves = 3;

        /// <summary>
        /// FELHASZNALOI VISSZAJELZES (backlog, 2026-09-06): a polusi jegsapka
        /// partvonala "tul tisztan", fix mintakent nezett ki. Gyokerok: a
        /// jeg-klasszifikacio (AnnualTemperatureStats+ClassifyIce, draga - 12
        /// mintavetel/tile, teljes Temperature-lanc mindegyiknel) CSAK a
        /// referencia-szinten fut (ld. Build()), es minden finomabb leaf tile
        /// az OS referencia-tile AZONOS eredmenyet orokolte - ez pontosan a
        /// folyo-/to-blokkosodassal (ld. ND-49) azonos hibaosztaly: nagy,
        /// szogletes, referencia-tile-meretu foltok, nem valodi partvonal.
        /// JAVITAS: a referencia-szintu meanK-t (olcso dictionary-lookup, NINCS
        /// ujra teljes Temperature-lanc leaf-enkent) egy LEAF-POZICIOFUGGO,
        /// terben koherens zajjal (FractalNoise.Fbm - MAR verifikalt primitiv,
        /// ugyanaz mint a DomainWarp/ND-36 hasznal) perturbaljuk, MIELOTT a
        /// kuszobbel osszevetnenk. Ez valodi, a klimamezobol (nem kezzel
        /// rajzolt mintabol) szarmazo irregularitast ad a hatarnak - I3-
        /// kompatibilis, mert a zaj maga is a worldSeedbol determinisztikusan
        /// szarmazo mezo (nincs uj hash-fuggveny, nincs uj RandomProperty).
        /// </summary>
        private bool IsAdaptiveIceTile(TileId id)
        {
            if (!TryGetReferenceAncestorValue(id, _adaptiveIceMeanK, out double referenceMeanK))
                return false;

            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double jitterK = IceBoundaryJitterAmplitudeK * FractalNoise.Fbm(
                _adaptiveSeed, x, y, z, IceBoundaryJitterFrequency, IceBoundaryJitterOctaves);
            return referenceMeanK + jitterK < LakesIceErosion.PermanentIceMeanThresholdK;
        }

        /// <summary>
        /// ND-57: ugyanaz a jitter-minta, mint IsAdaptiveIceTile, de a
        /// tengeri jeg (Ocean/SeaIce) hatarara - a Core BiomeClassification
        /// ezt zaj nelkul dontene el (tisztan OceanFreezingK kuszob), ami a
        /// szelesseg-szimmetrikus oceani homerseklet miatt geometriailag
        /// tokeletes kort adna mindket polusnal. Csak a RENDER kategoria
        /// dontesenel hasznaljuk - a temperatureK/biome ertekek (es az abbol
        /// szamolt statisztikak) valtozatlanok maradnak.
        /// </summary>
        private bool IsAdaptiveSeaIce(double x, double y, double z, double temperatureK)
        {
            double jitterK = IceBoundaryJitterAmplitudeK * FractalNoise.Fbm(
                _adaptiveSeed, x, y, z, IceBoundaryJitterFrequency, IceBoundaryJitterOctaves);
            return temperatureK + jitterK < BiomeClassification.OceanFreezingK;
        }

        /// <summary>
        /// FELHASZNALOI VISSZAJELZES (2026-09-10): a polusoknal egy szabalyos
        /// KOR alaku, szurke folt latszik a domborzat "alatt", amit itt-ott
        /// felulir a valodi terep (hegy, to, krater). Gyokerok: a render-
        /// kategoria ternariusban, ha a tile NEM krater/tavas es a jitterelt
        /// `isIce` (evi-atlag alapu, ld. IsAdaptiveIceTile) hamis, a kod a
        /// nyers `ToRenderCategory(biome)`-ra esik vissza - de ez a `biome`
        /// a Core BiomeClassification.Classify(temperatureK, isOceanic)
        /// PILLANATNYI, JITTER NELKULI homersekletebol jon
        /// (BiomeClassification.cs: `temperatureK &lt; IceSheetThresholdK`).
        /// A szarazfoldi pillanatnyi homerseklet a polusoknal kozel tisztan
        /// szelesseg/evszak-fuggo (sik, alacsony domborzatu teruleteken
        /// szinte semmi nem torzitja) - ez ugyanaz a hibaosztaly, mint az
        /// ND-57 (tengeri jeg), csak a SZARAZFOLDI biome-eldontesnel: KET
        /// fuggetlen jegreteg van (a jitterelt `isIce` ES a nyers `biome`
        /// sajat IceSheet-besorolasa), es mivel `isIce` csak HOZZAAD jeget,
        /// sose vesz el, a nyers, jitter nelkuli kor mindig "atsejlik", ahol
        /// az `isIce` epp hamis. Javitas: a RENDER-kategoria fallback-agan
        /// (nem a Core `biomeOf[id]`/statisztikakon!) ugyanazt a jitter-t
        /// alkalmazzuk a homersekletre, mint az ND-57/IsAdaptiveIceTile mar
        /// hasznalja - igy a szarazfoldi biome-hatarok (jeg/tundra/mersekelt/
        /// tropusi) is szervesen szabalytalanok lesznek, nem csak a tengeri.
        /// </summary>
        private Biome JitteredRenderBiome(double x, double y, double z, double temperatureK, bool isOceanic, ulong seed)
        {
            double jitterK = IceBoundaryJitterAmplitudeK * FractalNoise.Fbm(
                seed, x, y, z, IceBoundaryJitterFrequency, IceBoundaryJitterOctaves);
            // ND-126: a jitter SZANDEKOSAN csak a homersekletre hat, a
            // csapadekra nem. A jitter celja (ND-59) a polusi jeg/tundra
            // hatar szabalyos korenek megtorese; a csapadek-hatarok mar
            // eleve szabalytalanok (a nedvesseg-transzport a domborzatot
            // koveti), azokat nem kell rongyolni.
            return BiomeClassification.Classify(
                temperatureK + jitterK, isOceanic, PrecipitationAtCore(x, y, z), _adaptiveBiomeThresholds);
        }

        /// <summary>Ld. IsInReferenceLevelSet doksi - ugyanaz a minta, de erteket (nem csak tagsagot) ad vissza.</summary>
        private bool TryGetReferenceAncestorValue(TileId id, Dictionary<TileId, double> referenceValues, out double value)
        {
            value = 0.0;
            if (id.Level < level || referenceValues == null)
                return false;
            TileId current = id;
            while (current.Level > level)
                current = current.Parent();
            return referenceValues.TryGetValue(current, out value);
        }

        /// <summary>
        /// A tavak/jeg a referencia-szinten (level) egyszer szamolodnak; egy
        /// finomabb (level feletti) tile az OS referencia-tile allapotat orokli
        /// (Morton-hierarchia, olcso bitmuvelet). Csak OLVAS
        /// (Build ota valtozatlan halmaz), ezert szalbiztos a parhuzamos
        /// klasszifikaciobol (ComputeTileClassification) is.
        /// </summary>
        private bool IsInReferenceLevelSet(TileId id, HashSet<TileId> referenceSet)
        {
            if (id.Level < level || referenceSet == null)
                return false;
            TileId current = id;
            while (current.Level > level)
                current = current.Parent();
            return referenceSet.Contains(current);
        }

        /// <summary>
        /// Egy aktiv level 4 sarka - a "fine" (valodi, eltolt) pozicio a
        /// perzisztens sarok-cache-bol, geomorphing-gal (§9.2) a szulo-quad
        /// háromszög-interpolációjából származó coarse pozíció felé blendelve.
        /// ND-70: a CPU-s fedéscsere a közös sarkokat/éleket is feloldja.
        /// </summary>
        private void GetAdaptiveCorners(TileId id, out Vector3 p00, out Vector3 p10, out Vector3 p11, out Vector3 p01)
        {
            if (_activeCornerResolver == null || id.Level <= adaptiveBaseLevel)
            {
                GetUnstitchedAdaptiveCorners(id, out p00, out p10, out p11, out p01);
                return;
            }
            id.GetUV(out uint u, out uint v);
            p00 = ToUnityPoint(_activeCornerResolver.Corner(id.Face, id.Level, u, v));
            p10 = ToUnityPoint(_activeCornerResolver.Corner(id.Face, id.Level, u + 1, v));
            p11 = ToUnityPoint(_activeCornerResolver.Corner(id.Face, id.Level, u + 1, v + 1));
            p01 = ToUnityPoint(_activeCornerResolver.Corner(id.Face, id.Level, u, v + 1));
        }

        private static SurfacePoint ToSurfacePoint(Vector3 p) => new SurfacePoint(p.x, p.y, p.z);
        private static Vector3 ToUnityPoint(SurfacePoint p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        private SurfaceQuad GetRawSurfaceQuad(TileId id)
        {
            GetUnstitchedAdaptiveCorners(id, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d);
            return new SurfaceQuad(ToSurfacePoint(a), ToSurfacePoint(b), ToSurfacePoint(c), ToSurfacePoint(d));
        }

        private void GetUnstitchedAdaptiveCorners(TileId id, out Vector3 p00, out Vector3 p10, out Vector3 p11, out Vector3 p01)
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

            Vector3 coarse00 = InterpolateTriangulatedQuad(parent00, parent10, parent11, parent01, relU0, relV0);
            Vector3 coarse10 = InterpolateTriangulatedQuad(parent00, parent10, parent11, parent01, relU1, relV0);
            Vector3 coarse11 = InterpolateTriangulatedQuad(parent00, parent10, parent11, parent01, relU1, relV1);
            Vector3 coarse01 = InterpolateTriangulatedQuad(parent00, parent10, parent11, parent01, relU0, relV1);

            float a = (float)alpha;
            p00 = Vector3.Lerp(coarse00, fine00, a);
            p10 = Vector3.Lerp(coarse10, fine10, a);
            p11 = Vector3.Lerp(coarse11, fine11, a);
            p01 = Vector3.Lerp(coarse01, fine01, a);
        }

        private static Vector3 InterpolateTriangulatedQuad(Vector3 c00, Vector3 c10, Vector3 c11, Vector3 c01, double u, double v)
        {
            var quad = new SurfaceQuad(ToSurfacePoint(c00), ToSurfacePoint(c10), ToSurfacePoint(c11), ToSurfacePoint(c01));
            return ToUnityPoint(quad.At(u, v));
        }

        /// <summary>
        /// Geomorph-faktor (§9.2): 0 = a szulo (coarse) feluletet mutatja, ami
        /// FOLYTONOSAN illeszkedik ahhoz, amit a szulo meg aktiv leaf-kent
        /// mutatott - tehat a split PILLANATABAN (amikor a vetitett szogsugar
        /// eppen eleri a kuszobot) nincs pozicio-ugras (I3/vizualis
        /// folytonossag). Ahogy a kamera tovabb kozelit, alpha 1-hez tart (a
        /// valodi, finom feluletre). A hiszterezis-savon tul a formula 0-ra
        /// vagodik - ez azt jelenti, hogy a mar felbontott, de a kameratol
        /// tavolabb kerult gyerekek egyszeruen visszamutatjak a szulo
        /// feluletet (nincs artefaktum, csak felesleges, de vizualisan a
        /// szuloevel azonos geometria).
        ///
        /// SCREEN-SPACE-LOD: a splitDistance most a _currentTargetAngularRadiusRadians-
        /// bol (a legutobbi RecomputeCutAndRebuildAdaptiveMesh hivas kamera-
        /// FOV/felbontas-fuggo kuszobebol) szarmazik, NEM egy fix Inspector-
        /// konstansbol (ld. AdaptiveQuadTree.Visit()-ben az azonos elvu
        /// atan2(rTile,distance) osszehasonlitas - itt az inverz iranyban,
        /// a kuszobbol szamoljuk vissza a tavolsagot).
        /// </summary>
        private double ComputeGeomorphAlpha(TileId childId)
        {
            TileId parent = childId.Parent();
            double threshold = parent.Level == adaptiveBaseLevel
                ? _currentBaseTargetAngularRadiusRadians : _currentTargetAngularRadiusRadians;
            if (_requestedProjectedView != null && _requestedTerrainLodProxy != null)
            {
                double error;
                if (_terrainEvaluationCache != null)
                    _terrainEvaluationCache.EvaluateTerrain(_requestedTerrainLodProxy, parent, out error, out _);
                else _requestedProjectedView.EvaluateTerrain(_requestedTerrainLodProxy, parent, out error);
                if (error <= 0) return 0;
                double fraction = _currentGeomorphRangeFraction;
                return fraction <= 0 ? 1 : Math.Max(0,Math.Min(1,(1-Math.Tan(threshold)/Math.Tan(error))/fraction));
            }
            // ND-74: ugyanaz a gömb/proxy snapshot, mint a kiválasztásnál;
            // itt sem maradhat drága ND-71 magasság-callback.
            AdaptiveQuadTree.GetCenterAndBoundingRadius(parent, radius, out double pcx, out double pcy, out double pcz, out double rParent);
            if (_requestedTerrainLodProxy != null)
                _requestedTerrainLodProxy.ScaleMetric(parent, radius, ref pcx, ref pcy, ref pcz, ref rParent);
            double dx = _lastCutCameraCoreX - pcx, dy = _lastCutCameraCoreY - pcy, dz = _lastCutCameraCoreZ - pcz;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            double splitDistance = rParent / Math.Tan(Math.Max(threshold, 1e-9));
            return AdaptiveViewState.GeomorphAlpha(distance, splitDistance, _currentGeomorphRangeFraction);
        }

        private string DescribeSurfaceLod(HashSet<TileId> cut, double x, double y, double z)
        {
            int deepest = adaptiveBaseLevel;
            foreach (TileId leaf in cut) deepest = Math.Max(deepest, leaf.Level);
            int nadir = adaptiveBaseLevel;
            if (x * x + y * y + z * z > 1e-12)
            {
                TileId tile = TileGeometry.FromPosition(x, y, z, adaptiveMaxLevel);
                while (tile.Level > adaptiveBaseLevel)
                {
                    if (cut.Contains(tile)) { nadir = tile.Level; break; }
                    tile = tile.Parent();
                }
            }
            // A cut óceáni szűrés ELŐTTI adata; a dynLeaves külön renderadat.
            return $"surfaceMetric=False terrainProxy={_requestedTerrainLodProxy != null} projectedTerrain={_requestedProjectedView != null} deepestCutL={deepest} nadirCutL={nadir} surfaceBounds=0";
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
            if (TryGetStaticCornerIndex(face, lvl, cornerU, cornerV, _staticCornerPositions.Length, out int staticIndex))
                return _staticCornerPositions[staticIndex];

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

        /// <summary>
        /// A folytonos sarok-szin megosztott, gyorsitotarazott valtozata -
        /// PrecomputeCornersInParallel MAR feltoltotte parhuzamosan a
        /// szukseges sarkakra, tehat itt tipikusan tiszta cache-talalat
        /// (O(1), UJ Core-/Temperature-kiertekeles nelkul). Ha meg is
        /// hianyozna (pl. a fallback-utvonalon), a mar cache-elt/szamolt
        /// POZICIOBOL vezeti le, ugyanugy nem parhuzamositott ujra-
        /// szamolassal, mint a pozicio-cache sajat fallback-ja.
        /// </summary>
        private Color GetOrComputePersistentCornerColor(int face, int lvl, uint cornerU, uint cornerV)
        {
            if (TryGetStaticCornerIndex(face, lvl, cornerU, cornerV, _staticCornerColors.Length, out int staticIndex))
                return _staticCornerColors[staticIndex];

            var key = (face, lvl, cornerU, cornerV);
            if (_persistentCornerColorCache.TryGetValue(key, out Color cached))
                return cached;

            Vector3 p = GetOrComputePersistentCorner(face, lvl, cornerU, cornerV);
            Color c = ContinuousCornerColorAuto(p);
            _persistentCornerColorCache[key] = c;
            return c;
        }

        /// <summary>
        /// ND-55: a folytonos, lejto-erzekeny sarok-NORMAL megosztott,
        /// gyorsitotarazott valtozata - ld. ComputeCornerNormalViaFiniteDifference.
        /// A PrecomputeCornersInParallel altalaban MAR feltoltotte, itt
        /// tipikusan tiszta cache-talalat.
        /// </summary>
        private Vector3 GetOrComputePersistentCornerNormal(int face, int lvl, uint cornerU, uint cornerV)
        {
            if (TryGetStaticCornerIndex(face, lvl, cornerU, cornerV, _staticCornerNormals.Length, out int staticIndex))
                return _staticCornerNormals[staticIndex];

            var key = (face, lvl, cornerU, cornerV);
            if (_persistentCornerNormalCache.TryGetValue(key, out Vector3 cached))
                return cached;

            Vector3 n = ComputeCornerNormal(face, lvl, cornerU, cornerV);
            _persistentCornerNormalCache[key] = n;
            return n;
        }

        /// <summary>Cache-tol fuggetlen, szalbiztos sarok-szamitas - ld. PrecomputeCornersInParallel.</summary>
        private Vector3 ComputeCorner(int face, int lvl, uint cornerU, uint cornerV)
        {
            int n = 1 << lvl;
            double uc = (double)cornerU / n * 2.0 - 1.0;
            double vc = (double)cornerV / n * 2.0 - 1.0;
            return ToDisplacedVector3(face, uc, vc, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters);
        }

        /// <summary>ND-55: cache-tol fuggetlen, szalbiztos sarok-NORMAL szamitas - ld. PrecomputeCornersInParallel.</summary>
        private Vector3 ComputeCornerNormal(int face, int lvl, uint cornerU, uint cornerV)
        {
            Vector3 center = ComputeCorner(face, lvl, cornerU, cornerV);
            return ComputeCornerNormal(face, lvl, cornerU, cornerV, center);
        }

        /// <summary>
        /// A mar kiszamolt kozeppontot ujrahasznalo normal-ut. A center ugyanazt
        /// a ComputeCorner-hivast jelenti, mint amit a regi overload belul vegzett,
        /// ezert a kimenet bitre valtozatlan, csak egy teljes elevation-kiertekeles
        /// marad el minden uj saroknal.
        /// </summary>
        private Vector3 ComputeCornerNormal(int face, int lvl, uint cornerU, uint cornerV, Vector3 center)
        {
            int n = 1 << lvl;
            double uc = (double)cornerU / n * 2.0 - 1.0;
            double vc = (double)cornerV / n * 2.0 - 1.0;
            return ComputeCornerNormalViaFiniteDifference(
                face, uc, vc, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters, center);
        }

        /// <summary>
        /// Az osszes, a `leaves` altal (SAJAT + geomorphing-hoz szukseges
        /// SZULO) igenyelt sarok osszegyujtese, majd a MEG NEM cache-elt
        /// sarkak tobb szalon (Parallel.For) valo elore-kiszamitasa -
        /// ugyanaz a ket-fazisu minta, mint PrecomputeClassificationsInParallel.
        /// </summary>
        /// <summary>Visszaadja: (szukseges egyedi sarok-kulcsok szama, ebbol hany volt cache-miss).</summary>
        private (int NeededCount, int MissingCount) PrecomputeStaticCornersInParallel()
        {
            int n = 1 << adaptiveBaseLevel;
            int side = n + 1;
            int faceStride = checked(side * side);
            int count = checked(6 * faceStride);
            var positions = new Vector3[count];
            var colors = new Color[count];
            var normals = new Vector3[count];

            System.Threading.Tasks.Parallel.For(0, count, index =>
            {
                int face = index / faceStride;
                int faceIndex = index - face * faceStride;
                uint cornerU = (uint)(faceIndex / side);
                uint cornerV = (uint)(faceIndex - (int)cornerU * side);
                double uc = (double)cornerU / n * 2.0 - 1.0;
                double vc = (double)cornerV / n * 2.0 - 1.0;

                Vector3 p;
                Vector3 normal;
                if (TryGetStaticTerrainBasis(
                    face, adaptiveBaseLevel, cornerU, cornerV,
                    out TerrainPointBasis centerBasis,
                    out TerrainPointBasis uBasis,
                    out TerrainPointBasis vBasis))
                {
                    p = ToDisplacedVector3FromBasis(
                        face, uc, vc, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters, in centerBasis);
                    normal = ComputeCornerNormalViaFiniteDifferenceFromBasis(
                        face, uc, vc, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters,
                        p, in uBasis, in vBasis);
                }
                else
                {
                    p = ComputeCorner(face, adaptiveBaseLevel, cornerU, cornerV);
                    normal = ComputeCornerNormal(face, adaptiveBaseLevel, cornerU, cornerV, p);
                }

                positions[index] = p;
                normals[index] = normal;
            });

            // #9 (2026-09-20): a SZIN kulon, MASODIK fazisban szamolodik. Ok: a
            // szel-overlay sarok-szine a szomszedos base-szintu sarkok
            // elevaciojabol veszi az elevacio-gradienst
            // (TryStaticCornerElevationGradient), es amig a szin ugyanabban a
            // Parallel.For-ban keszult, mint a pozicio, addig a szomszedok meg
            // nem leteztek -> minden sarok a draga, pontonkenti veges
            // differenciara esett vissza (MERVE: a Build 4,8 s-rol 15,8 s-ra
            // nott). A szetvalasztas utan a szin-fazis mar a KESZ pozicio-
            // tombot latja. A ket fazis kulon-kulon is teljesen parhuzamos, es
            // egyik sem fugg a sajat fazisan beluli sorrendtol.
            _staticCornerPositions = positions;
            _staticCornerNormals = normals;
            // Szandekosan URES, amig a szin-fazis fut: aki kozben szint kerne,
            // essen a cache-elt utra, ne olvasson egy ELOZO vilaghoz tartozo,
            // veletlenul azonos hosszu tombot.
            _staticCornerColors = Array.Empty<Color>();
            _staticRenderDataLevel = adaptiveBaseLevel;

            System.Threading.Tasks.Parallel.For(0, count, index =>
            {
                colors[index] = ContinuousCornerColorAuto(positions[index]);
            });

            _staticCornerColors = colors;
            return (count, count);
        }

        private bool TryGetStaticCornerIndex(
            int face, int level, uint cornerU, uint cornerV, int dataLength,
            out int index)
        {
            if (level == _staticRenderDataLevel && face >= 0 && face < 6)
            {
                int n = 1 << level;
                if (cornerU <= n && cornerV <= n)
                {
                    int side = n + 1;
                    index = face * side * side + (int)cornerU * side + (int)cornerV;
                    if ((uint)index < (uint)dataLength)
                        return true;
                }
            }

            index = -1;
            return false;
        }

        private (int NeededCount, int MissingCount) PrecomputeCornersInParallel(TileId[] leaves)
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
                if (!TryGetStaticCornerIndex(key.Face, key.Level, key.CornerU, key.CornerV, _staticCornerPositions.Length, out _)
                    && (!_persistentCornerCache.ContainsKey(key)
                        || !_persistentCornerColorCache.ContainsKey(key)
                        || !_persistentCornerNormalCache.ContainsKey(key)))
                    missing.Add(key);
            if (missing.Count == 0)
                return (needed.Count, 0);

            var results = new Vector3[missing.Count];
            var colorResults = new Color[missing.Count];
            var normalResults = new Vector3[missing.Count];
            System.Threading.Tasks.Parallel.For(0, missing.Count, i =>
            {
                var k = missing[i];
                // Részleges cache: egy fallback betölthette a POZÍCIÓT,
                // illetve a szín-cache külön is invalidálódhat. A hiányzó
                // attribútumokat ugyanitt, párhuzamosan kell kiegészíteni.
                if (_persistentCornerCache.TryGetValue(k, out Vector3 existing))
                {
                    results[i] = existing;
                    colorResults[i] = _persistentCornerColorCache.TryGetValue(k, out Color color)
                        ? color : ContinuousCornerColorAuto(existing);
                    normalResults[i] = _persistentCornerNormalCache.TryGetValue(k, out Vector3 normal)
                        ? normal : ComputeCornerNormal(k.Face, k.Level, k.CornerU, k.CornerV, existing);
                    return;
                }
                if (TryGetStaticTerrainBasis(
                    k.Face, k.Level, k.CornerU, k.CornerV,
                    out TerrainPointBasis centerBasis,
                    out TerrainPointBasis uBasis,
                    out TerrainPointBasis vBasis))
                {
                    int n = 1 << k.Level;
                    double uc = (double)k.CornerU / n * 2.0 - 1.0;
                    double vc = (double)k.CornerV / n * 2.0 - 1.0;
                    Vector3 p = ToDisplacedVector3FromBasis(
                        k.Face, uc, vc, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters, in centerBasis);
                    results[i] = p;
                    colorResults[i] = ContinuousCornerColorAuto(p);
                    normalResults[i] = ComputeCornerNormalViaFiniteDifferenceFromBasis(
                        k.Face, uc, vc, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters,
                        p, in uBasis, in vBasis);
                }
                else
                {
                    Vector3 p = ComputeCorner(k.Face, k.Level, k.CornerU, k.CornerV);
                    results[i] = p;
                    colorResults[i] = ContinuousCornerColorAuto(p);
                    // A normal veges differenciajanak kozeppontja bitre ugyanaz a
                    // pont, amit fent mar kiszamoltunk. A regi kod ezt minden uj
                    // sarokra egy teljes elevation-lanccal ujra eloallitotta.
                    normalResults[i] = ComputeCornerNormal(k.Face, k.Level, k.CornerU, k.CornerV, p);
                }
            });

            for (int i = 0; i < missing.Count; i++)
            {
                var key = missing[i];
                _persistentCornerCache[key] = results[i];
                _persistentCornerColorCache[key] = colorResults[i];
                _persistentCornerNormalCache[key] = normalResults[i];
                if (_cornerCacheLruNodes.ContainsKey(key)) TouchLru(key);
                else _cornerCacheLruNodes[key] = _cornerCacheLru.AddLast(key);
            }
            return (needed.Count, missing.Count);
        }

        /// <summary>
        /// ND-63: a teljes statikus rács három időfüggetlen terrain-bázisát
        /// egyszer számolja ki world seedenként/base levelenként. A lokális
        /// tömbök csak a teljes Parallel.For sikere után kerülnek a mezőkbe,
        /// ezért kivételnél nem maradhat félkész cache.
        /// </summary>
        private bool EnsureStaticTerrainBasisCache()
        {
            // A jelenlegi teljes-világ render célpontja level 8. Level 9-10-en
            // a három bázistömb 4x/16x nagyobb lenne; ott a már önmagában is
            // extrém statikus mesh mellett ne okozzunk további kontrollálatlan
            // memóriaugrást. Az általános, nem cache-elt exact út megmarad.
            if (adaptiveBaseLevel > 8)
            {
                _staticCornerCenterBasis = Array.Empty<TerrainPointBasis>();
                _staticCornerUBasis = Array.Empty<TerrainPointBasis>();
                _staticCornerVBasis = Array.Empty<TerrainPointBasis>();
                _staticTerrainBasisLevel = -1;
                return false;
            }

            int n = 1 << adaptiveBaseLevel;
            int side = n + 1;
            int faceStride = checked(side * side);
            int count = checked(6 * faceStride);
            if (_staticTerrainBasisSeed == _adaptiveSeed
                && _staticTerrainBasisLevel == adaptiveBaseLevel
                && _staticCornerCenterBasis.Length == count
                && _staticCornerUBasis.Length == count
                && _staticCornerVBasis.Length == count)
                return true;

            // EGYETLEN geometria-fuggveny a szamolashoz ES a lemez-gyorsitotar
            // ujraszamolasos ellenorzesehez. Szandekosan kozos: ha a ketto
            // kulon lenne es elcsusznanak, az ellenorzes vagy MINDENT
            // elutasitana (a gyorsitotar ertelmetlenne valna), vagy - rosszabb -
            // mas ponton ellenorizne, mint amit betoltott.
            void ComputeAt(int index, TerrainPointBasis[] center, TerrainPointBasis[] u, TerrainPointBasis[] v,
                int centerSlot)
            {
                int face = index / faceStride;
                int faceIndex = index - face * faceStride;
                uint cornerU = (uint)(faceIndex / side);
                uint cornerV = (uint)(faceIndex - (int)cornerU * side);
                double uc = (double)cornerU / n * 2.0 - 1.0;
                double vc = (double)cornerV / n * 2.0 - 1.0;

                TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);
                center[centerSlot] = TerrainPointBasis.Compute(_adaptiveSeed, x, y, z);

                TileGeometry.PositionFromFaceUV(
                    face, uc + NormalSampleEpsilonUV, vc,
                    out double ux, out double uy, out double uz);
                u[centerSlot] = TerrainPointBasis.Compute(_adaptiveSeed, ux, uy, uz);

                TileGeometry.PositionFromFaceUV(
                    face, uc, vc + NormalSampleEpsilonUV,
                    out double vx, out double vy, out double vz);
                v[centerSlot] = TerrainPointBasis.Compute(_adaptiveSeed, vx, vy, vz);
            }

            var cacheKey = new TerrainBasisDiskCache.Key(
                _adaptiveSeed, adaptiveBaseLevel, NormalSampleEpsilonUV, count,
                arrayCount: 3, kind: TerrainBasisDiskCache.KindStaticCorners);
            if (TryLoadTerrainBasisFromDisk(cacheKey,
                    (index, arrays) => ComputeAt(index, arrays[0], arrays[1], arrays[2], 0),
                    out TerrainBasisDiskCache.Payload cached))
            {
                _staticCornerCenterBasis = cached.Arrays[0];
                _staticCornerUBasis = cached.Arrays[1];
                _staticCornerVBasis = cached.Arrays[2];
                _staticTerrainBasisSeed = _adaptiveSeed;
                _staticTerrainBasisLevel = adaptiveBaseLevel;
                return false;
            }

            var centerBasis = new TerrainPointBasis[count];
            var uBasis = new TerrainPointBasis[count];
            var vBasis = new TerrainPointBasis[count];
            System.Threading.Tasks.Parallel.For(0, count, index =>
            {
                ComputeAt(index, centerBasis, uBasis, vBasis, index);
            });

            _staticCornerCenterBasis = centerBasis;
            _staticCornerUBasis = uBasis;
            _staticCornerVBasis = vBasis;
            _staticTerrainBasisSeed = _adaptiveSeed;
            _staticTerrainBasisLevel = adaptiveBaseLevel;
            SaveTerrainBasisToDisk(cacheKey,
                new TerrainBasisDiskCache.Payload(centerBasis, uBasis, vBasis));
            return false;
        }

        private bool TryGetStaticTerrainBasis(
            int face, int level, uint cornerU, uint cornerV,
            out TerrainPointBasis centerBasis,
            out TerrainPointBasis uBasis,
            out TerrainPointBasis vBasis)
        {
            if (level == _staticTerrainBasisLevel
                && _staticTerrainBasisSeed == _adaptiveSeed)
            {
                int n = 1 << level;
                int side = n + 1;
                if (face >= 0 && face < 6 && cornerU <= n && cornerV <= n)
                {
                    int index = face * side * side + (int)cornerU * side + (int)cornerV;
                    centerBasis = _staticCornerCenterBasis[index];
                    uBasis = _staticCornerUBasis[index];
                    vBasis = _staticCornerVBasis[index];
                    return true;
                }
            }

            centerBasis = default;
            uBasis = default;
            vBasis = default;
            return false;
        }

        /// <summary>
        /// ND-64: egy adott, legfeljebb level-8 rács minden tile-középpontjához
        /// előállítja az időfüggetlen terrain-bázist. A célkonfigurációban a
        /// hydrologyLevel és adaptiveBaseLevel egyaránt 8, ezért ugyanaz a tömb
        /// szolgálja ki a két legdrágább fogyasztót.
        ///
        /// ND-131 (todo2.md A2): ez eddig CSAK MEMÓRIÁBAN gyorsítótárazott
        /// (seed+szint kulccsal), ezért minden HIDEG Build újraszámolta -
        /// élesben MÉRVE 2 267 ms (a `hydrology(...) terrainBasis=` sor).
        /// Mostantól ugyanazt a validált LEMEZ-gyorsítótárat használja, mint a
        /// statikus sarok-bázis (ND-122): kulcs + ellenőrzőösszeg + 1024
        /// bejegyzés ÚJRASZÁMOLÁSOS ellenőrzése, bármilyen eltérésnél
        /// újraszámolás. A tile-AZONOSÍTÓK számítása olcsó (nincs benne
        /// terep-matek), ezért azok NEM kerülnek lemezre - csak a drága bázis.
        /// </summary>
        private bool EnsureTileCenterTerrainBasisCache(ulong seed, int targetLevel)
        {
            if (targetLevel < 0 || targetLevel > 8)
            {
                _tileCenterTerrainIds = Array.Empty<TileId>();
                _tileCenterTerrainBasis = Array.Empty<TerrainPointBasis>();
                _tileCenterTerrainBasisLevel = -1;
                return false;
            }

            int n = 1 << targetLevel;
            int faceStride = checked(n * n);
            int count = checked(6 * faceStride);
            if (_tileCenterTerrainBasisSeed == seed
                && _tileCenterTerrainBasisLevel == targetLevel
                && _tileCenterTerrainIds.Length == count
                && _tileCenterTerrainBasis.Length == count)
                return true;

            TileId TileIdAt(int index)
            {
                int face = index / faceStride;
                int faceIndex = index - face * faceStride;
                uint u = (uint)(faceIndex / n);
                uint v = (uint)(faceIndex - (int)u * n);
                return TileId.FromFaceLevelUV(face, targetLevel, u, v);
            }

            // Az ID-k eloallitasa olcso (tiszta bitmuvelet), ezert MINDIG
            // futtatjuk - a lemez-gyorsitotar csak a draga bazist tarolja.
            var ids = new TileId[count];
            System.Threading.Tasks.Parallel.For(0, count, index => ids[index] = TileIdAt(index));

            void ComputeBasisAt(int index, TerrainPointBasis[] target, int slot)
            {
                TileGeometry.ToPosition(TileIdAt(index), out double x, out double y, out double z);
                target[slot] = TerrainPointBasis.Compute(seed, x, y, z);
            }

            // Az epszilon 0.0: ez a bazis NEM szamol normalt, tehat a tartalma
            // tenylegesen nem fugg a veges-differencia eltolastol (ld. a
            // TerrainBasisDiskCache.Key.NormalSampleEpsilonUV doksijat).
            var cacheKey = new TerrainBasisDiskCache.Key(
                seed, targetLevel, 0.0, count,
                arrayCount: 1, kind: TerrainBasisDiskCache.KindTileCenters);
            TerrainPointBasis[] bases;
            if (TryLoadTerrainBasisFromDisk(cacheKey,
                    (index, arrays) => ComputeBasisAt(index, arrays[0], 0),
                    out TerrainBasisDiskCache.Payload cached))
            {
                bases = cached.Arrays[0];
            }
            else
            {
                bases = new TerrainPointBasis[count];
                System.Threading.Tasks.Parallel.For(0, count, index => ComputeBasisAt(index, bases, index));
                SaveTerrainBasisToDisk(cacheKey, new TerrainBasisDiskCache.Payload(bases));
            }

            _tileCenterTerrainIds = ids;
            _tileCenterTerrainBasis = bases;
            _tileCenterTerrainBasisSeed = seed;
            _tileCenterTerrainBasisLevel = targetLevel;
            return false;
        }

        private bool TryGetTileCenterTerrainBasis(TileId id, out TerrainPointBasis basis)
        {
            if (id.Level == _tileCenterTerrainBasisLevel
                && _tileCenterTerrainBasisSeed == _adaptiveSeed)
            {
                int n = 1 << id.Level;
                id.GetUV(out uint u, out uint v);
                int index = id.Face * n * n + (int)u * n + (int)v;
                if ((uint)index < (uint)_tileCenterTerrainBasis.Length)
                {
                    basis = _tileCenterTerrainBasis[index];
                    return true;
                }
            }

            basis = default;
            return false;
        }

        /// <summary>
        /// A korábbi háromlépcsős field-láncot (elevation, kráter, erózió)
        /// egy passzban futtatja a cache-elt bázisból. A lebegőpontos sorrend
        /// szándékosan ugyanaz: (base+uplift), majd +crater, végül
        /// +(relaxedUplift-uplift).
        /// </summary>
        private Dictionary<TileId, double> BuildElevationFieldFromCachedTileCenters(
            ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters, double erosionTimeMyr,
            int targetLevel, out double[] denseValues)
        {
            if (_tileCenterTerrainBasisSeed != seed
                || _tileCenterTerrainBasisLevel != targetLevel
                || _tileCenterTerrainIds.Length != _tileCenterTerrainBasis.Length)
                throw new InvalidOperationException("A tile-középpont terrain-bázis cache nincs előkészítve.");

            var values = new double[_tileCenterTerrainIds.Length];
            System.Threading.Tasks.Parallel.For(0, values.Length, i =>
            {
                TileId id = _tileCenterTerrainIds[i];
                TileGeometry.ToPosition(id, out double x, out double y, out double z);
                _tileCenterTerrainBasis[i].Evaluate(
                    seed, seeds, out double baseElevation, out double uplift, out _);

                double elevation = baseElevation + uplift;
                if (craters.Count > 0)
                    elevation += ImpactCratering.ElevationDelta(x, y, z, craters);
                if (erosionTimeMyr != 0.0)
                {
                    double relaxedUplift = DeepTimeErosionGlaciation.UpliftRelaxationElevation(
                        uplift, erosionTimeMyr);
                    elevation += relaxedUplift - uplift;
                }
                values[i] = elevation;
            });

            var field = new Dictionary<TileId, double>(values.Length);
            for (int i = 0; i < values.Length; i++)
                field[_tileCenterTerrainIds[i]] = values[i];
            denseValues = values;
            return field;
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

        /// <summary>
        /// A statikus alapréteg (BuildStaticBaseLayer) ÖNMAGÁBAN már kb.
        /// 6*n*n tile-t / 6*(n+1)² sarkot igényel a cache-ekben
        /// (n=2^adaptiveBaseLevel - pl. adaptiveBaseLevel=8-nál 393216
        /// tile / ~396300 sarok). Ha az Inspectorban beállított
        /// cornerCacheMaxSize/tileClassificationCacheMaxSize EZ ALATT van
        /// (a korábbi 300000-es alapérték adaptiveBaseLevel=8-nál MÁR
        /// ALATTA volt ennek), a cache MÉG A STATIKUS RÉTEG SAJÁT ADATAIT
        /// SEM tudja egyszerre tartani - minden dinamikus (kamera-mozgatta)
        /// újraépítés kényszerűen kilakoltatja a statikus réteg egy részét,
        /// ami a KÖVETKEZŐ hivatkozáskor (pl. egy finomított levél
        /// bázis-szintű szülőjének sarkai, PrecomputeCornersInParallel)
        /// ÚJRASZÁMOLÁSRA kényszeríti azt - ez sérti a "statikus réteg =
        /// egyszer épül" tervezési célt, és folyamatos cache-thrashingot
        /// okoz MINDEN kamera-mozgásnál (a felhasználó által jelzett
        /// "egérmozgásra brutálisan megfolyik" tünet). A tényleges
        /// kilakoltatási küszöb ezért mindig LEGALÁBB a statikus réteg
        /// aktuális lábnyoma + tartalék, függetlenül az Inspector-értéktől
        /// (ami felfelé, TÖBBLET tartalékra még mindig módosítható).
        /// </summary>
        private int EffectiveCacheMinimum(int configuredMax)
        {
            long n = 1L << adaptiveBaseLevel;
            long baseFootprint = 6L * (n + 1) * (n + 1); // biztonsagos felso becsles
            long withHeadroom = baseFootprint + 100_000; // dinamikus reteg tovabbi munkakeszlete
            long effective = Math.Max(configuredMax, withHeadroom);
            return effective > int.MaxValue ? int.MaxValue : (int)effective;
        }

        private void EvictCornerCacheIfNeeded()
        {
            int effectiveMax = EffectiveCacheMinimum(cornerCacheMaxSize);
            while (_persistentCornerCache.Count > effectiveMax && _cornerCacheLru.Count > 0)
            {
                var oldest = _cornerCacheLru.First.Value;
                _cornerCacheLru.RemoveFirst();
                _cornerCacheLruNodes.Remove(oldest);
                _persistentCornerCache.Remove(oldest);
                _persistentCornerColorCache.Remove(oldest);
                _persistentCornerNormalCache.Remove(oldest);
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
            public readonly double TemperatureK;

            public AdaptiveTileClassification(double elevation, bool isOceanic, Biome biome, RenderCategory category, int bucket, double temperatureK)
            {
                Elevation = elevation;
                IsOceanic = isOceanic;
                Biome = biome;
                Category = category;
                Bucket = bucket;
                TemperatureK = temperatureK;
            }
        }

        private readonly Dictionary<TileId, AdaptiveTileClassification> _tileClassificationCache = new();
        private readonly LinkedList<TileId> _tileClassificationLru = new();
        private readonly Dictionary<TileId, LinkedListNode<TileId>> _tileClassificationLruNodes = new();
        private readonly Dictionary<TileId, bool> _baseOceanRefinementMask = new();

        private bool TryGetStaticTileClassification(TileId id, out AdaptiveTileClassification classification)
        {
            if (id.Level == _staticRenderDataLevel)
            {
                int n = 1 << id.Level;
                id.GetUV(out uint u, out uint v);
                int index = id.Face * n * n + (int)u * n + (int)v;
                if ((uint)index < (uint)_staticTileClassifications.Length)
                {
                    classification = _staticTileClassifications[index];
                    return true;
                }
            }

            classification = default;
            return false;
        }

        /// <summary>
        /// ND-69: az óceáni középpont mellett a base négy sarka is víz alatt
        /// legyen. Így a már az alapmesh-en látható part nem veszhet el a
        /// finomításból. Nem bizonyítja, hogy a teljes tile-belső víz alatti!
        /// A döntés base-tile-onként egyszer készül, majd világváltásig él.
        /// </summary>
        private bool IsBaseAncestorOceanic(TileId id)
        {
            TileId current = id;
            while (current.Level > adaptiveBaseLevel)
                current = current.Parent();
            if (_baseOceanRefinementMask.TryGetValue(current, out bool canSkip)) return canSkip;
            if (!TryGetStaticTileClassification(current, out AdaptiveTileClassification baseClass)
                && !_tileClassificationCache.TryGetValue(current, out baseClass))
                return false;
            if (!baseClass.IsOceanic || elevationScale <= 0 || terrainReliefExaggeration <= 0)
            {
                _baseOceanRefinementMask[current] = false;
                return false;
            }

            current.GetUV(out uint u, out uint v);
            // A float vertex-kerekítés közelében ne tiltsunk. A kisebb vízsugár
            // szigorúbb feltétel: a bizonytalan parti pontok finomodnak.
            double waterRadius = radius + _adaptiveSeaLevel * elevationScale - 0.0001;
            canSkip = OceanRefinement.CanSkip(true, waterRadius * waterRadius,
                SquaredRadius(GetOrComputePersistentCorner(current.Face, current.Level, u, v)),
                SquaredRadius(GetOrComputePersistentCorner(current.Face, current.Level, u + 1, v)),
                SquaredRadius(GetOrComputePersistentCorner(current.Face, current.Level, u + 1, v + 1)),
                SquaredRadius(GetOrComputePersistentCorner(current.Face, current.Level, u, v + 1)));
            _baseOceanRefinementMask[current] = canSkip;
            return canSkip;
        }

        private static double SquaredRadius(Vector3 p)
            => (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;

        private AdaptiveTileClassification GetOrComputeTileClassification(TileId id)
        {
            if (TryGetStaticTileClassification(id, out AdaptiveTileClassification denseClassification))
                return denseClassification;

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
            double elevation;
            bool isCratered;
            if (TryGetTileCenterTerrainBasis(id, out TerrainPointBasis basis))
            {
                elevation = ComputeElevationAtPointFromBasis(
                    cx, cy, cz, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters,
                    _adaptiveErosionTimeMyr, in basis, out isCratered);
            }
            else
            {
                elevation = ComputeElevationAtPoint(
                    cx, cy, cz, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters,
                    _adaptiveErosionTimeMyr, out isCratered);
            }

            // Az oceani besorolas itt KOZVETLENUL a pontszeru elevaciobol jon
            // (elevation < referencia-szintu seaLevel), NEM a level-fuggo
            // FlowNetwork.ComputeOceanField-bol (az csak egyetlen, fix szinten
            // ertelmezett) - ez a ket forras a t=0, fix-szintu esetben
            // ugyanazt adja (mindketto ugyanabbol az elevation-bol es
            // seaLevel-bol szarmazik), csak itt pontszeruen, tetszoleges
            // level-re altalanositva.
            bool isOceanic = elevation < _adaptiveSeaLevel;

            double temperatureK = TemperatureKelvinAt(cx, cy, cz, _adaptiveAxialTiltRad, isOceanic, elevation, _adaptiveSeaLevel);
            Biome biome = BiomeClassification.Classify(
                temperatureK, isOceanic, PrecipitationAtCore(cx, cy, cz), _adaptiveBiomeThresholds);

            bool isLake = showLakesIce && IsAdaptiveLakeTile(id);
            bool isIce = showLakesIce && IsAdaptiveIceTile(id);
            // ND-57 (felhasznaloi visszajelzes: "fix, adott magassagi foknal
            // levo jeg kirajzolas, kor alaku"): a Biome.SeaIce/Ocean hatar a
            // Core-ban TISZTA homerseklet-kuszob (BiomeClassification.
            // OceanFreezingK), zaj/jitter NELKUL - mivel az oceani homerseklet
            // majdnem tokeletesen szelesseg-szimmetrikus, ez egy geometriailag
            // tokeletes kort ad mindket polusnal. Ugyanaz a mintazat, mint a
            // szarazfoldi jegsapka-hataron (IsAdaptiveIceTile) mar korabban -
            // csak render-kategoria dontesnel, a Core `biome`-ot (es az abbol
            // szamolt statisztikakat) NEM erinti.
            bool isSeaIceRendered = showLakesIce && isOceanic && IsAdaptiveSeaIce(cx, cy, cz, temperatureK);
            // ND-59: ld. JitteredRenderBiome doksi - a nyers biome-fallback
            // jitter nelkul szabalyos kort adna a polusi jeg/tundra hataran.
            RenderCategory category = isCratered ? RenderCategory.Crater
                : isIce ? RenderCategory.IceSheet
                : isLake ? RenderCategory.Lake
                : isOceanic ? (isSeaIceRendered ? RenderCategory.SeaIce : RenderCategory.Ocean)
                : ToRenderCategory(JitteredRenderBiome(cx, cy, cz, temperatureK, isOceanic, _adaptiveSeed)); // folyok: kulon vonal-reteg (BuildRiverNetwork)

            int bucket = category == RenderCategory.Ocean ? OceanRockBucket(elevation) : 0;

            return new AdaptiveTileClassification(elevation, isOceanic, biome, category, bucket, temperatureK);
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
        /// <summary>Ideiglenes teljesitmeny-diagnosztika (2026-09-02) - reszletek: PerfLog doksija.</summary>
        private readonly struct ClassificationDiag
        {
            public readonly int MissingCount;
            public readonly bool UsedGpu;
            public readonly double GpuDispatchMs;
            public readonly double CpuTemperatureLoopMs;
            public ClassificationDiag(int missingCount, bool usedGpu, double gpuDispatchMs, double cpuTemperatureLoopMs)
            {
                MissingCount = missingCount; UsedGpu = usedGpu; GpuDispatchMs = gpuDispatchMs; CpuTemperatureLoopMs = cpuTemperatureLoopMs;
            }
        }

        private ClassificationDiag PrecomputeClassificationsInParallel(TileId[] leaves, bool forceCpu = false)
        {
            // ND-120 LEZARVA (2026-09-21, felhasznaloi dontes "A"): a GPU-
            // KLASSZIFIKACIO utja TOROLVE. Elesben mar korabban elerhetetlen
            // volt (mindharom hivo `forceCpu: true`-t adott az ND-47 3. fazisa
            // ota; a naplokban `usedGpu=True` utoljara 2026-09-11-en szerepelt),
            // a shader `BaseElevationF`-je viszont KET Core-ujitassal - ND-52
            // masodlagos zaj es ND-90 hatarkeveres - le volt maradva. MERVE
            // (GpuShaderElevationParityTests): a tile-ok ~22%-a mas oldalra
            // kerult volna a tengerszinthez kepest, ~24%-a mas biome-ot kapott
            // volna. Egy meg nem valosult optimalizacio maradvanya nem erte meg
            // az allando I1-kockazatot: minden Core-valtozast kezzel kellett
            // volna utanavezetni benne, es egy elfelejtett port CSENDBEN mas
            // vilagot osztalyozott volna.
            //
            // A `forceCpu` parameter SZANDEKOSAN MEGMARAD: az async emit-ut
            // ezzel jelzi, hogy worker szalrol fut. Ma mar nincs mas ag, de a
            // hivasi felulet igy valtozatlan, es egy jovobeli, ELLENORZOTT
            // GPU-ut ide illeszkedne vissza.

            var missing = new List<TileId>(leaves.Length);
            foreach (TileId id in leaves)
                if (!_tileClassificationCache.ContainsKey(id))
                    missing.Add(id);
            if (missing.Count == 0)
                return new ClassificationDiag(0, false, 0, 0);

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
            return new ClassificationDiag(missing.Count, false, 0, 0);
        }

        /// <summary>
        /// ND-66: a teljes base-grid klasszifikacioja kozvetlen indexu tombbe.
        /// Deep-time/CPU agon nincs Dictionary- es LinkedList-feltoltes; a
        /// kiserleti t=0 GPU-ag eredmenyet valtozatlanul a regi ut allitja elo,
        /// majd ugyanebbe a tombbe masoljuk.
        /// </summary>
        private ClassificationDiag PrecomputeStaticClassificationsInParallel(TileId[] leaves)
        {
            // ND-69: a statikus CPU-geometria és az óceáni finomítás-szűrés
            // ugyanazt a teljes modellt kapja; a GPU-ból hiányzik egy zajréteg.
            var results = new AdaptiveTileClassification[leaves.Length];
            System.Threading.Tasks.Parallel.For(0, leaves.Length, i =>
            {
                results[i] = ComputeTileClassification(leaves[i]);
            });
            _staticTileClassifications = results;
            _staticRenderDataLevel = adaptiveBaseLevel;
            return new ClassificationDiag(leaves.Length, false, 0, 0);
        }

        private void EvictTileClassificationCacheIfNeeded()
        {
            int effectiveMax = EffectiveCacheMinimum(tileClassificationCacheMaxSize);
            while (_tileClassificationCache.Count > effectiveMax && _tileClassificationLru.Count > 0)
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
            // A Build eddigre már újraírta a statikus víz mesh-ét: a régi
            // layout indexeit tilos az új világra visszamásolni.
            ResetIndependentWaterRendering(restoreStaticIndices: false);
            _drawnHiddenStaticWaterQuads = new HashSet<int>();
            Transform legacyWater = transform.Find("DynamicWater");
            if (legacyWater != null) legacyWater.gameObject.SetActive(false);
            _waterLodSource = null;
            _waterEvaluationCache = null;
            _waterIndexMask = null;
            _staticWaterMesh = null;
            _requestedIndependentWater = false;
            _waterCornerColors.Clear();
            _cutCancellation?.Dispose();
            _cutCancellation = null;
            _previousChunkCache.Clear();
            _lodRefinementPending = false;
            _cutSupersededSinceApply = false;
            _requestedProjectedView = null;
            _terrainEvaluationCache = null;
            _appliedSelectionTrace = null;
            _appliedDiagnosticCoverage = null;
            _appliedCutWork = default;
            _appliedTraceView = null;
            _drawnDiagnosticMeshes.Clear();
            _drawnHiddenStaticQuads = new HashSet<int>();
            _drawnDiagnosticRevision++;
            _drawnLodAppliedAt = Time.unscaledTime;
            _terrainLodProxy = null;
            _requestedTerrainLodProxy = null;
            _terrainIndexMask = null;
            _activeCornerResolver = null;
            _persistentCornerCache.Clear();
            _persistentCornerColorCache.Clear();
            _persistentCornerNormalCache.Clear();
            _cornerCacheLru.Clear();
            _cornerCacheLruNodes.Clear();
            _tileClassificationCache.Clear();
            _baseOceanRefinementMask.Clear();
            _tileClassificationLru.Clear();
            _tileClassificationLruNodes.Clear();
            _staticTileClassifications = Array.Empty<AdaptiveTileClassification>();
            _staticCornerPositions = Array.Empty<Vector3>();
            _staticCornerNormals = Array.Empty<Vector3>();
            _staticCornerColors = Array.Empty<Color>();
            _staticRenderDataLevel = -1;
            // A szin-cache-t is uritettuk (fent) - a mod-flaget szinkronban
            // tartjuk, kulonben a kovetkezo Update() feleslegesen ujra uritene.
            _colorCacheWindMode = windSpeedOverlay;
            _colorCacheTectonicMode = tectonicPlateOverlay;
            // Uj referencia-allapot -> a korabbi dinamikus-chunk GameObject-ek
            // a REGI vilagot mutatnak - torolni kell oket, kulonben a chunk-diff
            // (ami csak az UJ cuthoz kepesti valtozast nezi) nem feltetlenul
            // erinti mindet, es regi geometria maradna lathato.
            ClearAllDynamicChunks();
        }

        // ====================================================================
        // A csapadek-mezo CACHE-E (todo.md 1. tabla 5. sor, 2026-09-20).
        //
        // A MERES: a `Build() precipitation(enabled=True)` sor a naplokban
        // 322-410 ms, MINDEN Build-ben ujra. Deep-time csuszkazasnal ez
        // tisztan elvesztegetett ido, mert:
        //
        // A MoisturePrecipitation.Compute a deepTime erteket MEG SEM KAPJA.
        // Sajat, t=0-as mezot szamol
        // (SeaLevelCalibration.ComputeElevationField), krater es erozio
        // nelkul - klima-kozelites, nem a deepTime-eltolt domborzat (ezt a
        // hivas feletti komment is rogziti). Vagyis a deepTimeMyr valtozasa
        // NEM valtoztatja meg az eredmenyt, csak ujraszamoltatja.
        //
        // A CACHE-KULCS TETELES LELTARA. A Core-fuggveny TISZTA (I2), tehat a
        // kulcs PONTOSAN az atadott argumentumok halmaza - se tobb, se
        // kevesebb. A hivas nyolc erteket ad at:
        //
        //   1. seed                       <- (ulong)worldSeed    [Inspector]
        //   2. plateCount                 <- plateCount          [Inspector]
        //   3. level                      <- level               [Inspector]
        //   4. dayT                       <- climateDayT         [Inspector]
        //   5. orbitalPeriodDays          <- climateOrbitalPeriodDays  [Inspector]
        //   6. rotationPeriodDays         <- climateRotationPeriodDays [Inspector]
        //   7. axialTiltDegrees           <- climateAxialTiltDegrees   [Inspector]
        //   8. targetWaterFraction        <- targetWaterFraction  [Inspector]
        //
        // A tobbi Compute-parameter (iterations, precipBaseFraction,
        // orographicCoeff, orographicElevScale) ALAPERTELMEZETT marad, tehat
        // forditasi ideju konstans - nincs Inspector-mezo, ami invalidalna.
        // HA valaki ezek barmelyiket kesobb Inspector-mezove teszi, ODA KELL
        // VENNI A KULCSHOZ, kulonben a cache csendben elavult mezot ad.
        //
        // AMI SZANDEKOSAN NINCS A KULCSBAN:
        //   - deepTimeMyr: a fuggveny meg sem kapja (ld. fent) - EPP EZ a
        //     cache ertelme;
        //   - showCraters / showDeepTimeErosion / terrainReliefExaggeration /
        //     elevationScale / adaptiveBaseLevel / hydrologyLevel: a Core-ut
        //     egyiket sem lathatja;
        //   - precipitationOverlay / showRivers / showClouds: csak azt
        //     dontik el, KELL-E a mezo, az erteket nem befolyasoljak.
        //
        // MEGOSZTOTT OBJEKTUM. A cache ugyanazt a PrecipitationField peldanyt
        // adja vissza tobb Build-nek, tehat CSAK AKKOR helyes, ha senki nem
        // IRJA a tartalmat. Ellenorizve (2026-09-20): a viewer minden
        // felhasznalasa olvasas (TryGetValue / foreach / tovabbadas), es a
        // RiverPathTracing.SelectRiverSources is csak sajat, lokalis
        // gyujtemenyekbe ir. AKI EZT MEGVALTOZTATJA, annak a cache-t is
        // masolora kell cserelnie.
        //
        // NINCS eletciklus-alapu uritese (nem torli a Build/ResetWorldState):
        // a kulcs mindent tartalmaz, ami az erteket meghatarozza, tehat egy
        // "regi" bejegyzes azonos kulcson azonos ertek. Ez szandekos - az
        // eletciklus-alapu uritesnek epp az lenne a hibaja, amit elkerulunk.
        // ====================================================================
        private bool _hasPrecipCache;
        private ulong _pcSeed;
        private int _pcPlateCount, _pcLevel;
        private double _pcDayT, _pcOrbital, _pcRotation, _pcAxialTilt, _pcTargetWater;
        private MoisturePrecipitation.PrecipitationField _pcField;

        private MoisturePrecipitation.PrecipitationField GetOrComputePrecipitationField(
            ulong precipSeed, int precipPlateCount, int precipLevel, double precipTargetWater)
        {
            if (_hasPrecipCache && _pcField != null
                && _pcSeed == precipSeed && _pcPlateCount == precipPlateCount && _pcLevel == precipLevel
                && _pcDayT == climateDayT && _pcOrbital == climateOrbitalPeriodDays
                && _pcRotation == climateRotationPeriodDays && _pcAxialTilt == climateAxialTiltDegrees
                && _pcTargetWater == precipTargetWater)
            {
                _precipCacheHit = true;
                return _pcField;
            }

            _precipCacheHit = false;
            _pcField = MoisturePrecipitation.Compute(precipSeed, precipPlateCount, precipLevel,
                climateDayT, climateOrbitalPeriodDays, climateRotationPeriodDays,
                climateAxialTiltDegrees, targetWaterFraction: precipTargetWater);
            _pcSeed = precipSeed; _pcPlateCount = precipPlateCount; _pcLevel = precipLevel;
            _pcDayT = climateDayT; _pcOrbital = climateOrbitalPeriodDays;
            _pcRotation = climateRotationPeriodDays; _pcAxialTilt = climateAxialTiltDegrees;
            _pcTargetWater = precipTargetWater;
            _hasPrecipCache = true;
            return _pcField;
        }

        /// <summary>Diagnosztika a PerfLoghoz: az utolso keres talalat volt-e.</summary>
        private bool _precipCacheHit;

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

            // §2.1 Habitability: a referencia-szintu (_lastField) TELJES
            // homerseklet-mezo kell hozza - ez itt szamolodik (nem a Build()
            // ota cache-elve), UGYANAZZAL a klima-parameterezessel es
            // _adaptiveAxialTiltRad-dal, amit a Build() a biome-besoroláshoz
            // hasznalt. Csak a referencia-szint tile-szamaval aranyos
            // (level=5 alapertelmezesnel 6144 hivas) - nem draga.
            var temperatureK = new Dictionary<TileId, double>(_lastField.Count);
            foreach (KeyValuePair<TileId, double> kv in _lastField)
            {
                TileGeometry.ToPosition(kv.Key, out double tx, out double ty, out double tz);
                bool tOceanic = _lastIsOcean[kv.Key];
                temperatureK[kv.Key] = TemperatureKelvinAt(tx, ty, tz, _adaptiveAxialTiltRad, tOceanic, kv.Value, _lastSeaLevel);
            }
            double habitability = FeatureMetrics.HabitabilityFraction(_lastField.Keys, temperatureK, _lastIsOcean);

            data.World = new WorldPanelData
            {
                Name = worldName,
                SeedDisplay = worldSeed.ToString("X"),
                OceanCoveragePercent = oceanCoverage * 100.0,
                HabitabilityPercent = habitability * 100.0,
                HabitabilityLevel = OrdinalQuantization.LevelName(
                    OrdinalQuantization.Quantize(habitability, OrdinalQuantization.HabitabilityThresholds)),
            };

            FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(_lastField, _lastIsOcean);
            Dictionary<TileId, long> accumulation = FlowNetwork.FlowAccumulation(_lastField, flood.Parent, flood.FloodOrder);
            HashSet<TileId> riverTilesForPanels = FlowNetwork.SelectRiverTiles(_lastIsOcean, accumulation, riverTargetFraction);

            // ND-117 (Soil fertility): a regolit-profilhoz erozio/uledek kell.
            // A dragabb bemenetek MAR megvannak: az elevacio, a flood es az
            // accumulation fent, az evi kozephomerseklet pedig a jeg-ciklusbol
            // (`_lastLandMeanTemperatureK`). Igy a panel-mezo tenyleges
            // tobbletkoltsege csak ez a passz - MERVE ~36 ms level=6-on.
            // Ha nincs homerseklet (showLakesIce ki), a mezo elmarad.
            LakesIceErosion.ErosionResult? soilErosion = _lastLandMeanTemperatureK.Count > 0
                ? LakesIceErosion.ApplyStaticErosionPass(_lastField, flood.Parent, _lastIsOcean, accumulation)
                : null;
            _lastSoilErosion = soilErosion;

            Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, _lastIsOcean);
            // A NYERS vizgyujtok tovabbra is kellenek EGY mezohoz: a
            // kontinens-panel "River basins" szama tenylegesen azt kerdezi,
            // hany kulon vizgyujto metsz bele a kontinensbe (a >=5 tile-os
            // szuro ott a statisztikai zajt vagja le). A NAVIGACIOS regio
            // viszont mar nem ez - ld. lent, ND-127.
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
            _lastSortedContinents = sortedContinents;

            // 2. problema (docs/backlog.md "Continent es Island fogalmak
            // szetvalasztasa") - a landmass-osztalyozashoz a VILAG teljes
            // szarazfold-tile-szama kell (a tengerszint-kalibraciotol
            // fuggetlen, stabil mertek - ld. FeatureSegmentation doksija).
            int totalLandTiles = 0;
            foreach (bool isOceanTile in _lastIsOcean.Values)
                if (!isOceanTile) totalLandTiles++;

            for (int i = 0; i < sortedContinents.Count; i++)
            {
                List<TileId> comp = sortedContinents[i];
                Biome dominant = FeatureSegmentation.DominantBiome(comp, _lastBiomeOf);
                string name = NameGeneration.GenerateName(_lastSeed, (ulong)i, dominant.ToString());
                var compSet = new HashSet<TileId>(comp);
                double coastalComplexity = FeatureMetrics.CoastalComplexity(compSet, _lastIsOcean);
                FeatureSegmentation.LandmassClass landmassClass = FeatureSegmentation.ClassifyLandmass(comp.Count, totalLandTiles);
                data.Continents.Add(new ContinentPanelData
                {
                    Name = name,
                    AreaTiles = FeatureMetrics.AreaTiles(comp),
                    BiomeCount = FeatureMetrics.BiomeDiversity(comp, _lastBiomeOf),
                    DominantBiome = dominant.ToString(),
                    RiverMouthCount = FeatureMetrics.RiverMouthCount(comp, flood.Parent, _lastIsOcean, riverTilesForPanels),
                    RiverBasinCount = FeatureMetrics.RiverBasinCount(compSet, sizedRegions),
                    CoastalComplexity = coastalComplexity,
                    CoastalComplexityLevel = OrdinalQuantization.LevelName(
                        OrdinalQuantization.Quantize(coastalComplexity, OrdinalQuantization.CoastalComplexityThresholds)),
                    LandmassClass = FeatureSegmentation.LandmassClassName(landmassClass),
                    CenterDirection = CentroidDirection(comp),
                });
            }

            // ND-127: a navigacios/panel-regio a vizgyujtok OSSZEVONT,
            // terben osszefuggo csoportja - nem a nyers, torkolat szerint
            // kulcsolt vizgyujto. A Core garantalja, hogy ez a szarazfold
            // PARTICIOJA (minden tile pontosan egy regioban), tehat itt
            // NINCS tobb meretszuro: korabban a >=5 tile-os szuro a
            // szarazfold 50%-at (level 5) hagyta ki a navigaciobol.
            List<List<TileId>> panelRegions = FeatureSegmentation.MergeWatershedsIntoRegions(
                regions, FeatureSegmentation.RecommendedRegionTileTarget(totalLandTiles));

            // ld. a mezok doksijat: a navigacios menu negyedik szintje ebbol
            // dolgozik, a TELJES (nem csak a WorldGenPanelData.Regions top 10-e)
            // sorrendben, hogy egy mely regioba is bele lehessen zoomolni.
            _lastFlood = flood;
            _lastRiverTilesForPanels = riverTilesForPanels;
            _lastPanelRegions = panelRegions;

            int regionLimit = Math.Min(10, panelRegions.Count);
            for (int i = 0; i < regionLimit; i++)
            {
                List<TileId> tiles = panelRegions[i];
                Biome dominant = FeatureSegmentation.DominantBiome(tiles, _lastBiomeOf);
                // §2.3: a régiónév utótagja a FELISMERT morfológiai típust
                // tükrözi (pl. "Northwatch Range"), nem csak a domináns
                // biome-ot - Lowland-nál (nincs kiugró domborzat) a nevgeneralas
                // magatol visszaesik a biome-alapu utotagra (ld. NameGeneration).
                FeatureSegmentation.LandformType landform = FeatureSegmentation.ClassifyLandform(
                    tiles, _lastField, _lastIsOcean, _lastSeaLevel);
                string name = NameGeneration.GenerateName(_lastSeed, (ulong)(10000 + i), dominant.ToString(), landform);
                double? soilFertility = ComputeRegionSoilFertility(tiles, soilErosion);
                data.Regions.Add(new RegionPanelData
                {
                    Name = name,
                    AreaTiles = FeatureMetrics.AreaTiles(tiles),
                    DominantBiome = dominant.ToString(),
                    RiverMouthCount = FeatureMetrics.RiverMouthCount(tiles, flood.Parent, _lastIsOcean, riverTilesForPanels),
                    LandformType = FeatureSegmentation.LandformTypeName(landform),
                    CenterDirection = CentroidDirection(tiles),
                    SoilFertility = soilFertility,
                    SoilFertilityLevel = soilFertility.HasValue
                        ? OrdinalQuantization.LevelName(OrdinalQuantization.Quantize(
                            soilFertility.Value, OrdinalQuantization.SoilFertilityThresholds))
                        : null,
                });
            }

            return data;
        }


        /// <summary>
        /// ND-117: egy régió "Soil fertility" folytonos értéke, a MÁR meglévő
        /// Build-kimenetekből. Null, ha nincs évi középhőmérséklet
        /// (`showLakesIce` kikapcsolva) vagy nincs eróziós eredmény - az I4
        /// szerint ilyenkor a panel-mező HIÁNYZIK, nem kap pótolt értéket.
        ///
        /// A per-tile profil UGYANAZ a `RegolithModel.ComputeProfile`, amit a
        /// Core-teszt 500/500 vektoron bitpontosan igazol a Python orákulum
        /// ellen - itt nincs duplikált talaj-matek, csak a bemenetek összekötése.
        /// </summary>
        private double? ComputeRegionSoilFertility(
            List<TileId> tiles, LakesIceErosion.ErosionResult? erosion)
        {
            if (erosion == null || _lastLandMeanTemperatureK.Count == 0) return null;
            if (_lastPrecipField == null) return null;

            var depth = new Dictionary<TileId, double>(tiles.Count);
            var retention = new Dictionary<TileId, double>(tiles.Count);
            Dictionary<TileId, double> slopeNorm = RegolithModel.ComputeSlopeNorm(
                _lastField, _lastFlood.Parent, _lastIsOcean);

            foreach (TileId t in tiles)
            {
                if (_lastIsOcean[t]) continue;
                if (!_lastLandMeanTemperatureK.TryGetValue(t, out double meanK)) return null;
                erosion.Erosion.TryGetValue(t, out double erosionDepth);
                erosion.DepositionGain.TryGetValue(t, out double deposition);
                _lastPrecipField.Precipitation.TryGetValue(t, out double precipitation);
                slopeNorm.TryGetValue(t, out double slope);

                RegolithProfile profile = RegolithModel.ComputeProfile(
                    isOcean: false, slopeNorm: slope,
                    erosionDepthM: erosionDepth, depositionGainM: deposition,
                    annualMeanTemperatureK: meanK, precipitation: precipitation);
                depth[t] = profile.DepthMeters;
                retention[t] = profile.WaterRetention;
            }

            return FeatureMetrics.SoilFertility(
                tiles, depth, retention, _lastIsOcean, RegolithModel.DepthAbsoluteCapM);
        }

        /// <summary>
        /// A "bolygó" (World) nevének feature-id-je - egy sosem ütköző
        /// sentinel (kontinensek 0..N, régiók 10000+ id-t kapnak).
        /// </summary>
        public const ulong PlanetFeatureId = 999_999_999UL;

        /// <summary>docs/01-architecture.md §12 - a "terület" célmérete tile-ban.</summary>
        private const int AreaTargetTileCount = 40;

        /// <summary>
        /// A navigációs menü (docs/backlog.md "Navigációs menü és panel-
        /// elrendezés") számára: MELY globális régió-indexek (a
        /// <see cref="ComputePanelData"/> régió-sorrendje, NEM csak a
        /// `WorldGenPanelData.Regions` top 10-e) tartoznak az adott kontinenshez.
        /// Egy régió tile-jainak ELSŐ eleme alapján dönt. ND-127 óta ez NEM
        /// heurisztika, hanem pontos: az összevont régió térben összefüggő,
        /// két landmass pedig definíció szerint nem szomszédos, tehát egy
        /// régió minden tile-ja ugyanazon a landmasson van (a Core-teszt
        /// `NoMergedRegionSpansTwoLandmasses` ezt rögzíti). Korábban a nyers
        /// vízgyűjtő-régió szétesett, és az első tile csak a "túlnyomó
        /// többségre" volt igaz.
        /// </summary>
        private List<int> GetRegionGlobalIndicesForContinent(int continentIndex)
        {
            var result = new List<int>();
            if (_lastSortedContinents == null || _lastPanelRegions == null) return result;
            if (continentIndex < 0 || continentIndex >= _lastSortedContinents.Count) return result;

            var continentTiles = new HashSet<TileId>(_lastSortedContinents[continentIndex]);
            for (int i = 0; i < _lastPanelRegions.Count; i++)
            {
                List<TileId> tiles = _lastPanelRegions[i];
                if (tiles.Count > 0 && continentTiles.Contains(tiles[0]))
                    result.Add(i);
            }
            // A Core mar meret szerint csokkenoen adja vissza a regiokat,
            // tehat ez a reszhalmaz is az. A rendezes megis explicit, ES a
            // dontetlent a GLOBALIS INDEX donti el: a List.Sort NEM stabil,
            // tehat azonos meretu regiok sorrendje enelkul a bemeneti
            // elrendezesen mulna.
            result.Sort((a, b) =>
            {
                int bySize = _lastPanelRegions[b].Count.CompareTo(_lastPanelRegions[a].Count);
                return bySize != 0 ? bySize : a.CompareTo(b);
            });
            return result;
        }

        /// <summary>
        /// HIERARCHIA-ROBUSZTUSSÁG (docs/backlog.md "Eltűnő Region/Area kis
        /// landmass esetén", 2026-09-13, 1. javítás - GYÖKÉROK): a régió
        /// (vízgyűjtő-csoport) létezésétől FÜGGETLENÜL a `Count &gt;= 5`
        /// méretszűrést kapja (ld. <see cref="GetRegionGlobalIndicesForContinent"/>).
        /// Egy kis (pl. ≤19 tile-os) landmass gyakran TÖBB, egymástól
        /// független, kis helyi vízgyűjtőre esik szét, és ha EGYIK sem éri
        /// el az 5 tile-t, a landmassnak nulla megjelenő régiója (és emiatt
        /// nulla területe) lett. Ez sérti a navigációs menü alap-
        /// invariánsát: egy navigálható landmassnak MINDIG legyen legalább
        /// 1 régió-gyereke.
        ///
        /// A kis, biztonságos javítás: ha a normál (méretszűrt) régió-lista
        /// üres, egyetlen FALLBACK régióként a landmass TELJES tile-
        /// halmazát adjuk vissza - nem változtat a `FindWatershedRegions`/
        /// `sizedRegions` Core-szemantikáján (I1/I2-kompatibilis, tiszta
        /// függvény a MÁR meglévő adatokból), csak a navigáció soha nem
        /// akad el 0 gyerekkel.
        ///
        /// ND-127 ÓTA EZ BIZTONSÁGI HÁLÓ, nem napi útvonal: az összevont
        /// régiók a szárazföld PARTÍCIÓJÁT adják (nincs méretszűrő), tehát
        /// minden landmassnak van legalább egy régiója. Megtartjuk, mert
        /// nulla költségű, és egy jövőbeli szegmentálás-csere megint
        /// kihagyhat tile-okat. A fallback régió negyedik szintje (Area)
        /// automatikusan nem-üres, mert
        /// <see cref="FeatureSegmentation.PartitionRegionIntoAreas"/> már
        /// garantáltan legalább 1 területet ad bármely nem-üres bemenetre.
        ///
        /// FeatureId-tartomány (nem ütközik a normál régió 10000+i vagy a
        /// terület 20000+ tartományával - ld. docs/01-architecture.md §12.3):
        /// fallback régió = 900000+kontinensIndex, fallback régió területei
        /// = 950000+kontinensIndex*1000+területIndex.
        /// </summary>
        private const ulong FallbackRegionFeatureIdBase = 900000;
        private const ulong FallbackAreaFeatureIdBase = 950000;

        /// <summary>
        /// A régió-index NEGATÍV kódolása jelzi a fallback (egész-landmass)
        /// régiót - `-1` marad "nincs kiválasztva" (a navigációs mezők
        /// eredeti alapértéke), ezért a kódolás -2-től indul.
        /// </summary>
        private static int EncodeFallbackRegionIndex(int continentIndex) => -(continentIndex + 2);
        private static bool IsFallbackRegionIndex(int index) => index <= -2;
        private static int DecodeFallbackContinentIndex(int fallbackIndex) => -fallbackIndex - 2;

        /// <summary>
        /// Van-e ténylegesen kiválasztott régió - NEM `_navRegionGlobalIndex &gt;= 0`,
        /// mert a fallback régió-index SZÁNDÉKOSAN negatív (ld. fent) - csak
        /// a "nincs kiválasztva" alapérték (-1) számít üresnek.
        /// </summary>
        private bool HasSelectedRegion() => _navRegionGlobalIndex != -1;

        /// <summary>
        /// Egy adott kontinens ÖSSZES (nem csak a `WorldGenPanelData.Regions` top 10-es)
        /// régiója - a navigációs menü régió-listájához. A visszaadott lista
        /// indexe és a <paramref name="globalIndices"/> (kimenő paraméter)
        /// PÁRHUZAMOS - a kettő együtt azonosítja, hogy egy listaelemre
        /// kattintva melyik régió-indexre (normál VAGY fallback) kell
        /// navigálni. GARANTÁLTAN nem-üres listát ad vissza minden
        /// nem-üres landmass-ra - ld. a fallback-doksit fent.
        /// </summary>
        public List<RegionPanelData> ComputeRegionPanelDataForContinent(int continentIndex, out List<int> globalIndices)
        {
            globalIndices = GetRegionGlobalIndicesForContinent(continentIndex);
            var result = new List<RegionPanelData>(Math.Max(1, globalIndices.Count));
            foreach (int i in globalIndices)
                result.Add(BuildRegionPanelData(i));

            if (result.Count == 0 && _lastSortedContinents != null
                && continentIndex >= 0 && continentIndex < _lastSortedContinents.Count
                && _lastSortedContinents[continentIndex].Count > 0)
            {
                List<TileId> allTiles = _lastSortedContinents[continentIndex];
                ulong featureId = FallbackRegionFeatureIdBase + (ulong)continentIndex;
                result.Add(BuildRegionPanelDataFromTiles(allTiles, featureId));
                globalIndices = new List<int> { EncodeFallbackRegionIndex(continentIndex) };
            }
            return result;
        }

        private RegionPanelData BuildRegionPanelData(int globalRegionIndex)
        {
            List<TileId> tiles = _lastPanelRegions[globalRegionIndex];
            return BuildRegionPanelDataFromTiles(tiles, (ulong)(10000 + globalRegionIndex));
        }

        private RegionPanelData BuildRegionPanelDataFromTiles(List<TileId> tiles, ulong featureId)
        {
            Biome dominant = FeatureSegmentation.DominantBiome(tiles, _lastBiomeOf);
            FeatureSegmentation.LandformType landform = FeatureSegmentation.ClassifyLandform(
                tiles, _lastField, _lastIsOcean, _lastSeaLevel);
            string name = NameGeneration.GenerateName(_lastSeed, featureId, dominant.ToString(), landform);
            double? soilFertility = ComputeRegionSoilFertility(tiles, _lastSoilErosion);
            return new RegionPanelData
            {
                Name = name,
                AreaTiles = FeatureMetrics.AreaTiles(tiles),
                DominantBiome = dominant.ToString(),
                RiverMouthCount = FeatureMetrics.RiverMouthCount(tiles, _lastFlood.Parent, _lastIsOcean, _lastRiverTilesForPanels),
                LandformType = FeatureSegmentation.LandformTypeName(landform),
                CenterDirection = CentroidDirection(tiles),
                SoilFertility = soilFertility,
                SoilFertilityLevel = soilFertility.HasValue
                    ? OrdinalQuantization.LevelName(OrdinalQuantization.Quantize(
                        soilFertility.Value, OrdinalQuantization.SoilFertilityThresholds))
                    : null,
            };
        }

        /// <summary>
        /// Negyedik panelszint (docs/01-architecture.md §12): egy adott
        /// régió (normál globális index VAGY fallback-kódolt landmass-index,
        /// ld. <see cref="IsFallbackRegionIndex"/>) területei. LUSTA - csak
        /// akkor számol, amikor a felhasználó ténylegesen bele-zoomol egy
        /// régióba, nem minden régióhoz előre.
        /// </summary>
        public List<AreaPanelData> ComputeAreaPanelData(int regionIndexOrFallback)
        {
            if (IsFallbackRegionIndex(regionIndexOrFallback))
            {
                int continentIndex = DecodeFallbackContinentIndex(regionIndexOrFallback);
                if (_lastSortedContinents == null || continentIndex < 0 || continentIndex >= _lastSortedContinents.Count)
                    return new List<AreaPanelData>();
                List<TileId> tiles = _lastSortedContinents[continentIndex];
                ulong areaFeatureIdBase = FallbackAreaFeatureIdBase + (ulong)continentIndex * 1000;
                return BuildAreaPanelDataFromTiles(tiles, areaFeatureIdBase);
            }

            if (_lastPanelRegions == null || regionIndexOrFallback < 0 || regionIndexOrFallback >= _lastPanelRegions.Count)
                return new List<AreaPanelData>();

            List<TileId> regionTiles = _lastPanelRegions[regionIndexOrFallback];
            ulong normalAreaFeatureIdBase = (ulong)(20000 + regionIndexOrFallback * 1000);
            return BuildAreaPanelDataFromTiles(regionTiles, normalAreaFeatureIdBase);
        }

        private List<AreaPanelData> BuildAreaPanelDataFromTiles(List<TileId> tiles, ulong areaFeatureIdBase)
        {
            var result = new List<AreaPanelData>();
            List<List<TileId>> areas = FeatureSegmentation.PartitionRegionIntoAreas(tiles, AreaTargetTileCount);
            for (int i = 0; i < areas.Count; i++)
            {
                List<TileId> area = areas[i];
                Biome dominant = FeatureSegmentation.DominantBiome(area, _lastBiomeOf);
                FeatureSegmentation.LandformType landform = FeatureSegmentation.ClassifyLandform(
                    area, _lastField, _lastIsOcean, _lastSeaLevel);
                ulong featureId = areaFeatureIdBase + (ulong)i;
                string name = NameGeneration.GenerateName(_lastSeed, featureId, dominant.ToString(), landform);
                result.Add(new AreaPanelData
                {
                    Name = name,
                    AreaTiles = area.Count,
                    DominantBiome = dominant.ToString(),
                    LandformType = FeatureSegmentation.LandformTypeName(landform),
                    CenterDirection = CentroidDirection(area),
                });
            }
            return result;
        }

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

        /// <summary>
        /// Egy tile-halmaz (régió/kontinens) "középpont-iránya" UNITY
        /// WORLD-FRAME (a bolygó lokális terében, forgatás/skálázás
        /// előtt) egységvektorként: a tagok Core body-frame egységvektorai
        /// összegének normalizáltja, MÁR átkonvertálva
        /// `BodyFrameConversion.ToUnity`-vel. Ez a gömbön egy ÉSSZERŰ
        /// közelítés a centroidra (nem egzakt gömbi súlypont, de egy
        /// kontinens/régió méretű, nem-antipodális tile-halmazra jól
        /// működik) - PONTOSAN elég a navigációs menü "kattints a névre, a
        /// kamera odaugrik" funkciójának (`PlanetOrbitCamera.FlyToDirection`)
        /// célpontjához, nem egy szimulációs mennyiség.
        /// </summary>
        private static Vector3 CentroidDirection(IEnumerable<TileId> tiles)
        {
            double sx = 0, sy = 0, sz = 0;
            int count = 0;
            foreach (TileId t in tiles)
            {
                TileGeometry.ToPosition(t, out double x, out double y, out double z);
                sx += x; sy += y; sz += z;
                count++;
            }
            if (count == 0) return Vector3.forward;
            Vector3 sum = BodyFrameConversion.ToUnity(sx, sy, sz);
            return sum.sqrMagnitude > 1e-12f ? sum.normalized : Vector3.forward;
        }

        private static RenderCategory ToRenderCategory(Biome biome) => biome switch
        {
            Biome.Ocean => RenderCategory.Ocean,
            Biome.SeaIce => RenderCategory.SeaIce,
            Biome.IceSheet => RenderCategory.IceSheet,
            Biome.Tundra => RenderCategory.Tundra,
            Biome.Desert => RenderCategory.Desert,
            Biome.Grassland => RenderCategory.Grassland,
            Biome.TemperateForest => RenderCategory.TemperateForest,
            Biome.Savanna => RenderCategory.Savanna,
            Biome.Rainforest => RenderCategory.Rainforest,
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
        // 2026-09-05: a fix iranyu Lambert-BESUTES (korabbi `BakedLightDirection`
        // + `Shade`) MEGSZUNT. Ok: a folytonos felszin unlit shadere miatt nem
        // volt valos feny-valasz; a besutott, FIX iranyu Lambert nem kovette a
        // Napot es nem adott spekularist (a lapos HDRP/Lit kategoriak viszont
        // igen -> inkonzisztens, "megszunt a feny-visszaverodes"). Most a
        // VertexColorUnlit shader VALOS IDOBEN vilagit (a C# altal atadott
        // Nap-irannyal), ezert a vertex-szin a TISZTA felszin-szin.

        /// <summary>Egyetlen szint minden sarkara (uniform quad-szin) - a River/Crater/SeaIce flat-color eseteknek.</summary>
        private static void AddQuad(
            List<Vector3> vertices, List<Vector3> normals, List<int> triangles, List<Color> colors, Color color,
            Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01)
        {
            AddQuad(vertices, normals, triangles, colors, color, color, color, color, p00, p10, p11, p01);
        }

        /// <summary>
        /// A felszin (gomb-kozeliteses) normalja EGY csucspontra: a sajat
        /// pozicio iranya. Ez STRUKTURALISAN soha nem lehet NaN/nulla egy
        /// bolygofelszini pontra (a pozicio hossza kb. a `radius`, sosincs
        /// az origo kozeleben) - ld. az AddQuad-beli 2026-09-09-es
        /// magyarazatot arrol, miert valtottunk a korabbi, degeneralt
        /// quadnal NaN-t adhato cross-product-alapu lapos normalrol erre.
        /// Ha a bemeno pozicio maga MAR NaN/vegtelen (egy KORABBI, ettol
        /// fuggetlen szamitasi hiba a pozicio-lancban), ez a vedelem NEM
        /// tudja helyrehozni - de ez egy strukturalisan sokkal ritkabb,
        /// kulon vizsgalando eset (a pozicio-szamitas nem oszt/nem
        /// normalizal degeneralt bemeneten, ellentetben a regi cross-
        /// producttal).
        /// </summary>
        private static Vector3 SafeSurfaceNormal(Vector3 pos)
        {
            float sqrMag = pos.sqrMagnitude;
            if (sqrMag >= 1e-6f && sqrMag < float.PositiveInfinity)
                return pos.normalized;
            return Vector3.up;
        }

        private static int _nanVertexColorLogCount = 0;
        private const int MaxNaNVertexColorLogs = 20;

        /// <summary>
        /// Diagnosztikai vedelmi halo (2026-09-09): NaN/Infinity vertex-szin
        /// eseten a rootcause-hoz pozicioval egyutt logol (max
        /// <see cref="MaxNaNVertexColorLogs"/>-szor, hogy ne arassza el a
        /// konzolt), es lathato MAGENTA-ra cserel, hogy a jelenseg (feher
        /// villanas -&gt; magenta) egyertelmuen ehhez a forrashoz kotheto
        /// legyen egy kepernyokep alapjan.
        /// </summary>
        private static Color SanitizeVertexColor(Color c, Vector3 pos)
        {
            bool finite = !float.IsNaN(c.r) && !float.IsNaN(c.g) && !float.IsNaN(c.b) && !float.IsNaN(c.a)
                && !float.IsInfinity(c.r) && !float.IsInfinity(c.g) && !float.IsInfinity(c.b) && !float.IsInfinity(c.a);
            if (finite)
                return c;
            // ND-123: a statikus emit MAR PARHUZAMOS, ezert ez a diagnosztikai
            // szamlalo Interlocked - kulonben ket szal ugyanazt az erteket
            // olvasva tulloghatna a korlaton (a szam maga diagnosztika, de a
            // konzol elarasztasa ellen vedo korlatnak mukodnie kell).
            if (System.Threading.Interlocked.Increment(ref _nanVertexColorLogCount) <= MaxNaNVertexColorLogs)
            {
                Debug.LogWarning($"[NaN-diag] NaN/Infinity vertex color detected at pos={pos} (radius={pos.magnitude}), color=({c.r},{c.g},{c.b},{c.a}) -> replaced with magenta. count={_nanVertexColorLogCount}");
            }
            return new Color(1f, 0f, 1f, 1f);
        }

        /// <summary>
        /// SARKONKENT KULON szin (c00/c10/c11/c01, a p00/p10/p11/p01
        /// pozicioknak megfelelo sorrendben) - ez adja a GPU-interpolalt,
        /// folytonos (nem tile-egeszre-egyenletes) szinatmenetet a
        /// haromszogon BELUL. A sarkok kozos POZICIOJA mar most is
        /// megosztott a szomszedos tile-okkal (corner cache), de a SZIN
        /// (meg) nem - ket szomszedos tile hatarán ezert lehet egy kicsi,
        /// de a regi (egesz-tile-egyenletes) allapotnal jelentosen kisebb
        /// lepcso, amig a szin-sarok-cache kesobb ezt is megoldja.
        /// </summary>
        private static void AddQuad(
            List<Vector3> vertices, List<Vector3> normals, List<int> triangles, List<Color> colors,
            Color c00, Color c10, Color c11, Color c01,
            Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01)
        {
            int baseIndex = vertices.Count;
            vertices.Add(p00); vertices.Add(p10); vertices.Add(p11); vertices.Add(p01);

            // 2026-09-09 HARMADIK VALTOZAT (13-15. kor utan, ld.
            // history/2026-09-06-...-session.md): a 15. kor a LAPOS,
            // lejto-erzekeny cross-product normalt TELJESEN lecserelte
            // egy tisztan gomb-iranyu (`normalize(sajat pozicio)`) normalra
            // - ez NaN-biztos volt, DE elveszett vele a domborzat vizualis
            // magassag-erzete (minden pont ugy arnyalodott, mintha tokeletes
            // gomb lenne, fuggetlenul a tenyleges lejto-dolestol -
            // felhasznaloi visszajelzes: "elveszett a vizualis magassag
            // erzete"). VISSZAALLITVA a lejto-erzekeny LAPOS normalra mint
            // ELSODLEGES forras (ez adja a domborzat-arnyalast), de a NaN
            // elleni vedelem MOST MAR HELYESEN: a 14. kor felismerese szerint
            // egy NaN-nal vegzett `<`/`>` osszehasonlitas IEEE-754 szerint
            // MINDIG false, ezert a `!(sqrMagnitude >= kuszob)` (tagadott)
            // forma kell, hogy NaN eseten IS a biztonsagos agba fusson -
            // ez a helyes valtozat, amit a 15. kor tesztelet nelkul
            // felulirt egy meg drasztikusabb megoldassal, pedig ez onmagaban
            // mar elegendo lehetett volna.
            Vector3 rawNormal = Vector3.Cross(p10 - p00, p01 - p00);
            Vector3 normal;
            if (!(rawNormal.sqrMagnitude >= 1e-12f))
            {
                // Degeneralt quad (vagy mar NaN a bemeneten) - a negy
                // sarokpont sajat, STRUKTURALISAN sosem NaN gomb-iranyu
                // normaljainak atlagara esunk vissza (ld. SafeSurfaceNormal).
                Vector3 avgNormal = SafeSurfaceNormal(p00) + SafeSurfaceNormal(p10)
                    + SafeSurfaceNormal(p11) + SafeSurfaceNormal(p01);
                normal = avgNormal.sqrMagnitude >= 1e-12f ? avgNormal.normalized : Vector3.up;
            }
            else
            {
                normal = rawNormal.normalized;
            }
            if (Vector3.Dot(normal, p00) < 0f) normal = -normal;
            normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);

            // 2026-09-05: a fix iranyu Lambert-BESUTES MEGSZUNT - a folytonos
            // felszin mostantol a VertexColorUnlit(->Lit) shaderben, VALOS IDOBEN
            // vilagitodik (a mozgo Nappal + spekularis), a fenti `normal`-t
            // hasznalva. Igy a vertex-szin a TISZTA felszin-szin, es a
            // vilagitas nincs ketszer alkalmazva (a lapos HDRP/Lit kategoriak
            // ugyis a valos Nappal vilagitanak - most a folytonos felszin is).
            //
            // 2026-09-09 DIAGNOSZTIKA: ha a c00..c01 barmelyike NaN/Infinity
            // komponenst tartalmaz (pl. egy Math.Exp/oszt-nulla lanc a
            // szin-szamitasban), a GPU-n tovabb terjed (NaN*barmi=NaN) es
            // FEHERKENT jelenik meg, FUGGETLENUL minden fenyezestol - ugyanaz
            // a mechanizmus, mint a fenti normal-NaN eseten. Ha itt talalunk
            // ilyet, EGYSZER logoljuk a pozicioval egyutt (gyokerokozo
            // beazonositasahoz) es MAGENTA-ra kicsereljuk, hogy a
            // felhasznaloi screenshot egyertelmuen mutassa: ha a feher folt
            // magentara valt, ez a forras.
            c00 = SanitizeVertexColor(c00, p00);
            c10 = SanitizeVertexColor(c10, p10);
            c11 = SanitizeVertexColor(c11, p11);
            c01 = SanitizeVertexColor(c01, p01);
            colors.Add(c00); colors.Add(c10); colors.Add(c11); colors.Add(c01);

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

        /// <summary>
        /// ND-55 (2026-09-09): AddQuad valtozat, ami a normalokat KIVULROL,
        /// MAR MEGOSZTOTTAN cache-elt, veges-differencia-alapu ertekkent
        /// kapja (ld. ComputeCornerNormalViaFiniteDifference/
        /// GetOrComputePersistentCornerNormal) - EZ adja a szomszedos tile-ok
        /// kozott FOLYTONOS (nem tile-hataronkent ugralo), DE tovabbra is a
        /// tenyleges lejtest tukrozo normal-mezot a szarazfoldi kategoriakhoz.
        /// A viz/to/folyo/krater tovabbra is a REGI (belsoleg szamolt lapos
        /// normalu) AddQuad-ot hasznalja - azoknal a lapos/gomb-kozelites
        /// elegendo (viz-felszin valojaban IS gomb, a kis reteg-elemek meg
        /// nem is IGENYELNek finom lejto-arnyalast).
        /// </summary>
        private static void AddQuad(
            List<Vector3> vertices, List<Vector3> normals, List<int> triangles, List<Color> colors,
            Color c00, Color c10, Color c11, Color c01,
            Vector3 n00, Vector3 n10, Vector3 n11, Vector3 n01,
            Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01)
        {
            int baseIndex = vertices.Count;
            vertices.Add(p00); vertices.Add(p10); vertices.Add(p11); vertices.Add(p01);

            n00 = SafeSurfaceNormal(n00.sqrMagnitude >= 1e-6f && n00.sqrMagnitude < float.PositiveInfinity ? n00 : p00);
            n10 = SafeSurfaceNormal(n10.sqrMagnitude >= 1e-6f && n10.sqrMagnitude < float.PositiveInfinity ? n10 : p10);
            n11 = SafeSurfaceNormal(n11.sqrMagnitude >= 1e-6f && n11.sqrMagnitude < float.PositiveInfinity ? n11 : p11);
            n01 = SafeSurfaceNormal(n01.sqrMagnitude >= 1e-6f && n01.sqrMagnitude < float.PositiveInfinity ? n01 : p01);
            normals.Add(n00); normals.Add(n10); normals.Add(n11); normals.Add(n01);

            c00 = SanitizeVertexColor(c00, p00);
            c10 = SanitizeVertexColor(c10, p10);
            c11 = SanitizeVertexColor(c11, p11);
            c01 = SanitizeVertexColor(c01, p01);
            colors.Add(c00); colors.Add(c10); colors.Add(c11); colors.Add(c01);

            // Winding-dontes: a negy (mar biztonsagos) normal osszege eleg
            // durva referenciakent - nem kell a pontos lapos normal.
            Vector3 refNormal = n00 + n10 + n11 + n01;
            Vector3 impliedNormal1 = Vector3.Cross(p10 - p00, p11 - p00);
            if (Vector3.Dot(impliedNormal1, refNormal) >= 0f)
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

        /// <summary>Get-or-add segedfuggveny a (kategoria, bucket) kulcsu lista-negyeshez.</summary>
        private static void GetOrAddLists(
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> verticesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> normalsByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<int>> trianglesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Color>> colorsByKey,
            (RenderCategory Category, int Bucket) key,
            out List<Vector3> vertices, out List<Vector3> normals, out List<int> triangles, out List<Color> colors)
        {
            if (!verticesByKey.TryGetValue(key, out vertices))
            {
                vertices = new List<Vector3>();
                normals = new List<Vector3>();
                triangles = new List<int>();
                colors = new List<Color>();
                verticesByKey[key] = vertices;
                normalsByKey[key] = normals;
                trianglesByKey[key] = triangles;
                colorsByKey[key] = colors;
            }
            else
            {
                normals = normalsByKey[key];
                triangles = trianglesByKey[key];
                colors = colorsByKey[key];
            }
        }

        /// <summary>
        /// FAZIS 3 (ND-47): a per-bucket geometria KONKATENALASA egyetlen
        /// vertex/index-bufferbe + befoglalo doboz - TISZTA managed muvelet
        /// (nulla Unity-API), ezert WORKER SZALON is futtathato (ld.
        /// ComputeAdaptiveMeshBuffersCpu). A Unity mesh-feltoltes
        /// (UploadConcatenatedMultiMaterialMesh) ebbol mar csak masol a natv
        /// bufferbe - a korabbi, fo szalon vegzett AddRange + RecalculateBounds
        /// (elesben ~15-25ms) igy lekerul a fo szalrol.
        /// </summary>
        private sealed class ConcatenatedMesh
        {
            public TileId[]? TileIds;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int[]> SubmeshTriangles = new List<int[]>();
            public readonly List<(RenderCategory Category, int Bucket)> SubmeshKeys = new List<(RenderCategory, int)>();
            public Vector3 BoundsMin, BoundsMax;
        }

        private void AttachTerrainTileIds(ConcatenatedMesh mesh, IEnumerable<TileId> emissions)
        {
            var buckets = new Dictionary<(RenderCategory, int), int>();
            var counts = new int[mesh.SubmeshKeys.Count];
            for (int i=0;i<counts.Length;i++)
            {
                buckets.Add(mesh.SubmeshKeys[i],i);
                counts[i]=mesh.SubmeshTriangles[i].Length/6;
            }
            var identity = new TerrainQuadIdentity(counts);
            foreach (TileId tile in emissions)
            {
                // A diagnosztika nem indíthat új Core-kiértékelést.
                if (!TryGetStaticTileClassification(tile,out AdaptiveTileClassification classification)
                    && !_tileClassificationCache.TryGetValue(tile,out classification)) return;
                if (!buckets.TryGetValue((classification.Category,classification.Bucket),out int bucket)) return;
                identity.Add(bucket,tile);
            }
            mesh.TileIds=identity.Complete();
            if (mesh.TileIds.Length*4!=mesh.Vertices.Count)
                throw new InvalidOperationException("Nem quadonként tárolt terrain mesh.");
        }

        private void InitializeTerrainIndexMask(ConcatenatedMesh data, TileId[] tiles)
        {
            Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
            var starts = new Dictionary<(RenderCategory, int), int>();
            int total = 0;
            for (int i = 0; i < data.SubmeshKeys.Count; i++)
            {
                SubMeshDescriptor descriptor = mesh.GetSubMesh(i);
                if (descriptor.baseVertex != 0 || descriptor.indexCount != data.SubmeshTriangles[i].Length)
                    throw new InvalidOperationException("Váratlan statikus mesh-index layout.");
                starts[data.SubmeshKeys[i]] = descriptor.indexStart;
                total = Math.Max(total, descriptor.indexStart + descriptor.indexCount);
            }
            var indices = new int[total];
            for (int i = 0; i < data.SubmeshKeys.Count; i++)
                Array.Copy(data.SubmeshTriangles[i], 0, indices, starts[data.SubmeshKeys[i]], data.SubmeshTriangles[i].Length);
            var offsets = new int[tiles.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                AdaptiveTileClassification classification = _staticRenderDataLevel == adaptiveBaseLevel
                    ? _staticTileClassifications[i] : GetOrComputeTileClassification(tiles[i]);
                var key = (classification.Category, classification.Bucket);
                offsets[TerrainIndexMask.DenseIndex(tiles[i])] = starts[key];
                starts[key] += 6;
            }
            _terrainIndexMask = new TerrainIndexMask(adaptiveBaseLevel, offsets, indices);
            _drawnHiddenStaticQuads = _terrainIndexMask.CopyHiddenQuadIndices();
        }

        private void RestoreTerrainCoverageAfterFailure()
        {
            if (_terrainIndexMask == null) return;
            TerrainIndexMask.Range range = _terrainIndexMask.RestoreAll();
            if (range.Count > 0)
            {
                Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
                mesh.SetIndexBufferData(_terrainIndexMask.Indices, range.Start, range.Start, range.Count,
                    MeshUpdateFlags.DontRecalculateBounds);
            }
            // Csak sikeres natív helyreállítást állítunk a rajzolt diagnosztikában.
            _drawnHiddenStaticQuads = _terrainIndexMask.CopyHiddenQuadIndices();
            _drawnDiagnosticRevision++;
            PerfLog($"[ND-92 terrain recovery] indices={range.Count} restored=True");
        }

        private int ApplyTerrainCoverage(IEnumerable<TileId> replacedRoots, AdaptiveMeshBuffers? buffers = null)
        {
            if (_terrainIndexMask == null) return 0;
            long started = Stopwatch.GetTimestamp();
            IReadOnlyList<TerrainIndexMask.Range> ranges = buffers?.PreparedTerrainMask != null
                ? _terrainIndexMask.ApplyPrepared(buffers.PreparedTerrainMask)
                : _terrainIndexMask.SetHidden(replacedRoots);
            if (buffers != null) buffers.TerrainMaskApplyTicks = Stopwatch.GetTimestamp() - started;
            started = Stopwatch.GetTimestamp();
            int count = 0;
            if (ranges.Count > 0)
            {
                Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
                foreach (TerrainIndexMask.Range range in ranges)
                {
                    mesh.SetIndexBufferData(_terrainIndexMask.Indices, range.Start, range.Start, range.Count,
                        MeshUpdateFlags.DontRecalculateBounds);
                    count += range.Count;
                }
            }
            if (buffers != null)
            {
                buffers.TerrainMaskUploadTicks = Stopwatch.GetTimestamp() - started;
                buffers.TerrainMaskRanges = ranges.Count;
            }
            started = Stopwatch.GetTimestamp();
            if (ranges.Count > 0)
            {
                _drawnHiddenStaticQuads = buffers?.PreparedTerrainHiddenQuads ?? _terrainIndexMask.CopyHiddenQuadIndices();
                _drawnDiagnosticRevision++;
            }
            if (buffers != null) buffers.TerrainMaskSnapshotTicks = Stopwatch.GetTimestamp() - started;
            return count;
        }

        private static ConcatenatedMesh ConcatenateMultiMaterialBuckets(
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> verticesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> normalsByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<int>> trianglesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Color>> colorsByKey)
        {
            var cm = new ConcatenatedMesh();

            // Determinisztikus sorrend (kategoria, majd bucket szerint) - a
            // dictionary bejarasi sorrendjere NEM szabad tamaszkodni (I1),
            // bar itt "csak" a draw call sorrendet befolyasolja, nem a
            // vilagmodellt - a stabil sorrend igy is jobb debugolhatosagot ad.
            var keys = new List<(RenderCategory Category, int Bucket)>(verticesByKey.Keys);
            keys.Sort((a, b) => a.Category != b.Category ? a.Category.CompareTo(b.Category) : a.Bucket.CompareTo(b.Bucket));

            // ND-134: az összes bemenet már kész; a növekvő listák újrafoglalása
            // L8-on több mint 100 MiB fölösleges átmeneti tömböt hozott létre.
            int vertexCount = 0, normalCount = 0, colorCount = 0;
            foreach (var key in keys)
            {
                if (verticesByKey[key].Count == 0) continue;
                vertexCount = checked(vertexCount + verticesByKey[key].Count);
                normalCount = checked(normalCount + normalsByKey[key].Count);
                colorCount = checked(colorCount + colorsByKey[key].Count);
            }
            cm.Vertices.Capacity = vertexCount;
            cm.Normals.Capacity = normalCount;
            cm.Colors.Capacity = colorCount;
            cm.SubmeshTriangles.Capacity = keys.Count;
            cm.SubmeshKeys.Capacity = keys.Count;

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach ((RenderCategory Category, int Bucket) key in keys)
            {
                List<Vector3> verts = verticesByKey[key];
                if (verts.Count == 0) continue;

                int offset = cm.Vertices.Count;
                cm.Vertices.AddRange(verts);
                cm.Normals.AddRange(normalsByKey[key]);
                cm.Colors.AddRange(colorsByKey[key]);
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 v = verts[i];
                    if (v.x < min.x) min.x = v.x; if (v.y < min.y) min.y = v.y; if (v.z < min.z) min.z = v.z;
                    if (v.x > max.x) max.x = v.x; if (v.y > max.y) max.y = v.y; if (v.z > max.z) max.z = v.z;
                }

                List<int> triList = trianglesByKey[key];
                int[] tris = triList.ToArray();
                for (int i = 0; i < tris.Length; i++) tris[i] += offset;
                cm.SubmeshTriangles.Add(tris);
                cm.SubmeshKeys.Add(key);
            }
            cm.BoundsMin = min; cm.BoundsMax = max;
            return cm;
        }

        /// <summary>
        /// FAZIS 3 (ND-47): a MAR konkatenalt geometria feltoltese a Unity
        /// mesh-be (Unity-API -> KIZAROLAG fo szal). A befoglalo dobozt a
        /// (workeren) elore szamolt min/max-bol allitjuk be, tehat NINCS
        /// RecalculateBounds (ami a fo szalon minden vertexen vegigmenne).
        /// </summary>
        private void UploadConcatenatedMultiMaterialMesh(GameObject targetGo, ConcatenatedMesh cm, bool ownChunkMesh = false)
        {
            _drawnDiagnosticMeshes.Remove(targetGo);
            // A meglevo Mesh ujrahasznositasa (Clear + ujratoltes) elkeruli az
            // ismetelt natv objektum-letrehozast (ld. korabbi teljesitmeny-fix).
            MeshFilter meshFilter = targetGo.GetComponent<MeshFilter>();
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null || (ownChunkMesh && !_ownedTerrainChunkMeshes.Contains(mesh)))
            {
                mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                if (ownChunkMesh)
                {
                    // A legacy upload hibája után is elérhető/takarítható legyen.
                    _ownedTerrainChunkMeshes.Add(mesh);
                    meshFilter.sharedMesh = mesh;
                }
            }
            UploadTerrainMeshData(mesh, cm);
            meshFilter.sharedMesh = mesh;
            targetGo.GetComponent<MeshRenderer>().sharedMaterials = TerrainMaterials(cm);
            RememberDrawnSurface(targetGo, cm.Vertices, cm.SubmeshTriangles,
                targetGo == gameObject ? 1 : 2, cm.TileIds);
        }

        private static void UploadTerrainMeshData(Mesh mesh, ConcatenatedMesh cm)
        {
            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(cm.Vertices);
            mesh.SetNormals(cm.Normals);
            mesh.SetColors(cm.Colors);
            mesh.subMeshCount = cm.SubmeshTriangles.Count;
            for (int i = 0; i < cm.SubmeshTriangles.Count; i++)
                mesh.SetTriangles(cm.SubmeshTriangles[i], i, calculateBounds: false);
            if (cm.Vertices.Count > 0)
                mesh.bounds = new Bounds((cm.BoundsMin + cm.BoundsMax) * 0.5f, cm.BoundsMax - cm.BoundsMin);

        }

        private Material[] TerrainMaterials(ConcatenatedMesh cm)
        {
            var materials = new List<Material>(cm.SubmeshKeys.Count);
            for (int i = 0; i < cm.SubmeshKeys.Count; i++)
                materials.Add(GetOrCreateCategoryMaterial(cm.SubmeshKeys[i]));
            return materials.ToArray();
        }

        // Visszafele-kompatibilis kompozicio (a SZINKRON ut + BuildStaticBaseLayer
        // hasznalja): konkatenal, majd feltolt - a viselkedes valtozatlan.
        private void BuildMultiMaterialMesh(
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> verticesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> normalsByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<int>> trianglesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Color>> colorsByKey,
            GameObject targetGo)
        {
            ConcatenatedMesh cm = ConcatenateMultiMaterialBuckets(verticesByKey, normalsByKey, trianglesByKey, colorsByKey);
            UploadConcatenatedMultiMaterialMesh(targetGo, cm);
        }

        /// <summary>
        /// Get-or-create egy `this` alatti gyerek GameObject-et MeshFilter+
        /// MeshRenderer-rel - a statikus alap- es dinamikus finomitott
        /// reteg kulon-kulon GameObject-en el (mindketto sajat Mesh-t es
        /// draw call-t kap), hogy egyik se irja felul a masikat.
        /// </summary>
        private GameObject GetOrCreateChildRenderTarget(string name)
        {
            Transform child = transform.Find(name);
            if (child != null)
                return child.gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            return go;
        }

        /// <summary>
        /// Get-or-create egy dinamikus-chunk GameObject-et (ld. useChunkedDynamicMesh
        /// doksija) - a `_dynamicChunkGameObjects` explicit terkepen at, nem
        /// `transform.Find`-dal (O(1) a chunk-szamban, nem O(gyerekek szama)).
        /// </summary>
        private GameObject GetOrCreateChunkRenderTarget(TileId chunkRoot, bool activateNew = true)
        {
            _inactiveTerrainChunks.Remove(chunkRoot);
            if (_dynamicChunkGameObjects.TryGetValue(chunkRoot, out GameObject? go) && go != null)
                return go;

            go = CreateInactiveChunkRenderTarget(transform, chunkRoot);
            if (activateNew) go.SetActive(true);
            _dynamicChunkGameObjects[chunkRoot] = go;
            return go;
        }

        private static GameObject CreateInactiveChunkRenderTarget(Transform parent, TileId chunkRoot)
        {
            var target = new GameObject("Chunk_" + chunkRoot.Value);
            target.SetActive(false);
            try
            {
                target.transform.SetParent(parent, false);
                target.AddComponent<MeshFilter>();
                target.AddComponent<MeshRenderer>();
                return target;
            }
            catch
            {
                SafeDestroy(target);
                throw;
            }
        }

        /// <summary>
        /// Minden dinamikus-chunk GameObject torlese + a chunk-csoportositas
        /// nullazasa - UJ VILAG (Build(), ld. InvalidateAdaptiveCaches) eseten
        /// kell, kulonben a regi vilag chunk-jai (amiket az uj cut inkrementalis
        /// diffje nem feltetlenul erint) LATHATOK maradnanak a regi geometriaval.
        /// </summary>
        private void ClearAllDynamicChunks()
        {
            ClearAllDynamicChunkResources(destroyTargets: true);
        }

        private void ClearAllDynamicChunkResources(bool destroyTargets)
        {
            CancelStagedTerrainUpload();
            foreach (TileId key in new List<TileId>(_dynamicChunkGameObjects.Keys))
                ReleaseTerrainChunkResources(key, destroyTargets);
            ClearUploadSpareMeshes();
            // Egy kívülről már törölt célobjektum sem hagyhat saját natív mesh-t maga után.
            foreach (Mesh mesh in _ownedTerrainChunkMeshes)
                if (mesh != null) SafeDestroy(mesh);
            _ownedTerrainChunkMeshes.Clear();
            _inactiveTerrainChunks.Clear();
            _previousChunkCache.Clear();
            _dynamicChunkGameObjects.Clear();
            _previousChunkGroups.Clear();
            _previousChunkPositions.Clear();
        }

        private void BuildBorders(List<Vector3> borderVerts, List<int> borderIndices, string childName)
        {
            Transform borderChild = transform.Find(childName);

            if (!showBorders)
            {
                if (borderChild != null) borderChild.gameObject.SetActive(false);
                return;
            }

            GameObject borderGo;
            if (borderChild == null)
            {
                borderGo = new GameObject(childName);
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

            // TELJESITMENY: meglevo Mesh ujrahasznositasa uj peldany helyett
            // (ld. BuildMultiMaterialMesh doksija - GPU-oldali stallt okoz
            // gyakori ujra-allokacio nagy mesh-eknel).
            MeshFilter borderMeshFilter = borderGo.GetComponent<MeshFilter>();
            Mesh borderMesh = borderMeshFilter.sharedMesh;
            if (borderMesh == null)
                borderMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            else
                borderMesh.Clear();
            borderMesh.indexFormat = IndexFormat.UInt32;
            borderMesh.SetVertices(borderVerts);
            borderMesh.SetIndices(borderIndices, MeshTopology.Lines, 0);
            borderMesh.RecalculateBounds();
            borderMeshFilter.sharedMesh = borderMesh;
        }

        /// <summary>
        /// M10 deep-time erozio egy tile-KOZEPU mezore: CSAK az uplift-reszt
        /// relaxaljuk (+= relaxedUplift - uplift), a base/jitter/crater valtozatlan.
        /// Ugyanaz a Core-keplet, mint ComputeElevationAtPoint-ban. erosionTimeMyr=0
        /// -> identitas (a mezot valtozatlanul adja vissza). Kozos a megjelenitesi
        /// es a (finomabb) hidrologia-mezohoz.
        /// </summary>
        private static Dictionary<TileId, double> ApplyDeepTimeErosionToField(
            Dictionary<TileId, double> field, ulong seed, (double X, double Y, double Z)[] seeds, double erosionTimeMyr)
        {
            if (erosionTimeMyr == 0.0)
                return field;

            // TELJESITMENY: ugyanaz a minta, mint a Core SeaLevelCalibration.
            // ComputeElevationFieldWithSeeds-nel - tile-onkent FUGGETLEN, tiszta
            // szamitas (nincs tile-ok kozotti megosztott allapot), ezert
            // Parallel.For-ral kulon tombokbe irva, majd egyszalu Dictionary-
            // epitessel bitre valtozatlan eredmenyt ad, csak nem szekvencialisan.
            // `hydrologyLevel`=8-nal (393k tile) ez korabban tobb masodperces,
            // EGYSZALU passz volt minden deepTimeMyr-valtaskor.
            var keys = new TileId[field.Count];
            var baseValues = new double[field.Count];
            int idx = 0;
            foreach (KeyValuePair<TileId, double> kv in field)
            {
                keys[idx] = kv.Key;
                baseValues[idx] = kv.Value;
                idx++;
            }

            var erodedValues = new double[field.Count];
            System.Threading.Tasks.Parallel.For(0, keys.Length, i =>
            {
                TileGeometry.ToPosition(keys[i], out double ex, out double ey, out double ez);
                DomainWarp.WarpPosition(seed, ex, ey, ez, out double ewx, out double ewy, out double ewz);
                double uplift = PlateBoundaryEffect.BoundaryUpliftFromWarped(seed, ex, ey, ez, ewx, ewy, ewz, seeds);
                double relaxedUplift = DeepTimeErosionGlaciation.UpliftRelaxationElevation(uplift, erosionTimeMyr);
                erodedValues[i] = baseValues[i] + (relaxedUplift - uplift);
            });

            var eroded = new Dictionary<TileId, double>(field.Count);
            for (int i = 0; i < keys.Length; i++)
                eroded[keys[i]] = erodedValues[i];
            return eroded;
        }

        private Material _riverLineMaterial;

        /// <summary>
        /// M9/M7 (ND-49) dendritikus folyó-hálózat mesh-SZALAGKÉNT (nem
        /// vékony vonalként - ld. `riverBaseHalfWidth` doksi, felhasználói
        /// visszajelzés 2026-09-06: "a szélessége nem korrelál azzal,
        /// mennyi vizet szállít").
        /// A háttér-szálon futó FOLYTONOS nyomvonal-követés (<see
        /// cref="RiverPathTracing.BuildContinuousRiverNetworkFromSources"/>,
        /// ld. Build()) eredményét használja: a felszín érintő-síkjában futó,
        /// kb. riverRefinementStepMeters felbontású pontsorozatot, a hozzá
        /// tartozó `_adaptiveRiverDischargeWeights`-ből levezetett
        /// szélességgel. Amíg az aktuális deep-time állapot finomítása nincs
        /// kész, a korábbi állapot érvénytelen vonala helyett nincs folyó-mesh.
        /// EGYSZER épül fel (Build() vagy a finomítás elkészültekor), a
        /// kamera-mozgás nem érinti.
        /// </summary>
        private void BuildRiverNetwork()
        {
            Transform child = transform.Find("Rivers");
            if (!showRivers || _adaptiveRefinedRiverPaths == null || _adaptiveRefinedRiverPaths.Count == 0)
            {
                if (child != null) child.gameObject.SetActive(false);
                return;
            }

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            var ribbonPoints = new List<Vector3>();
            for (int riverIdx = 0; riverIdx < _adaptiveRefinedRiverPaths.Count; riverIdx++)
            {
                List<(double X, double Y, double Z)> refinedPath = _adaptiveRefinedRiverPaths[riverIdx].Points;

                if (refinedPath != null && refinedPath.Count >= 2)
                {
                    // A finomitott ut mar suru/folytonos-koveto - egyenes
                    // szakaszok is simanak latszanak, nincs szukseg Catmull-
                    // Rom-ra (az akar el is torzithatna a szamolt lejto-
                    // iranytol).
                    ribbonPoints.Clear();
                    for (int i = 0; i < refinedPath.Count; i++)
                        ribbonPoints.Add(RiverPositionOnSurface(refinedPath[i].X, refinedPath[i].Y, refinedPath[i].Z));

                    int weight = _adaptiveRiverDischargeWeights != null && riverIdx < _adaptiveRiverDischargeWeights.Length
                        ? _adaptiveRiverDischargeWeights[riverIdx] : 1;
                    float halfWidth = riverBaseHalfWidth * Mathf.Sqrt(weight);
                    AddRiverRibbon(verts, normals, triangles, ribbonPoints, halfWidth);
                }
            }

            BuildRivers(verts, normals, triangles);
        }

        /// <summary>
        /// Egy folyó-nyomvonalat (`points`, felszínre már pozicionálva) egy
        /// állandó `halfWidth` fél-szélességű mesh-szalaggá alakít - minden
        /// ponthoz a SZOMSZÉDOS pontok középső-differenciájából vett érintő-
        /// irányra merőleges, a helyi felszín-normálissal (a pont saját
        /// iránya a gömbközépponttól) egy síkban lévő "oldal"-vektort számol,
        /// hogy a szalag ÉLEI egymáshoz simuljanak (nem hagynak rést/
        /// átfedést enyhe kanyarokban sem, szemben egy per-szegmens
        /// független kvad-sorozattal). A háromszög-body a helyi normálishoz
        /// igazított forgásiránnyal épül (mint <see cref="AddQuad"/>), hogy
        /// az `HDRP/Lit` (opak, backface-culled) anyaggal helyesen, kifelé
        /// nézve jelenjen meg.
        /// </summary>
        private static void AddRiverRibbon(
            List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
            IReadOnlyList<Vector3> points, float halfWidth)
        {
            int n = points.Count;
            if (n < 2 || halfWidth <= 0f) return;

            var leftVerts = new Vector3[n];
            var rightVerts = new Vector3[n];
            var radials = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                Vector3 p = points[i];
                Vector3 tangent = i == 0 ? (points[1] - points[0])
                    : i == n - 1 ? (points[n - 1] - points[n - 2])
                    : (points[i + 1] - points[i - 1]);
                Vector3 radial = p.sqrMagnitude > 1e-12f ? p.normalized : Vector3.up;
                Vector3 side = Vector3.Cross(radial, tangent);
                if (side.sqrMagnitude < 1e-12f)
                    side = Vector3.Cross(radial, Mathf.Abs(Vector3.Dot(radial, Vector3.up)) < 0.99f ? Vector3.up : Vector3.right);
                side = side.normalized;
                leftVerts[i] = p - side * halfWidth;
                rightVerts[i] = p + side * halfWidth;
                radials[i] = radial;
            }

            int baseIndex = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                vertices.Add(leftVerts[i]); vertices.Add(rightVerts[i]);
                normals.Add(radials[i]); normals.Add(radials[i]);
            }

            for (int i = 0; i < n - 1; i++)
            {
                int l0 = baseIndex + i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                Vector3 geomNormal = Vector3.Cross(rightVerts[i] - leftVerts[i], leftVerts[i + 1] - leftVerts[i]);
                if (Vector3.Dot(geomNormal, radials[i]) >= 0f)
                {
                    triangles.Add(l0); triangles.Add(r0); triangles.Add(l1);
                    triangles.Add(r0); triangles.Add(r1); triangles.Add(l1);
                }
                else
                {
                    triangles.Add(l0); triangles.Add(l1); triangles.Add(r0);
                    triangles.Add(r0); triangles.Add(l1); triangles.Add(r1);
                }
            }
        }

        /// <summary>
        /// Egy Core-keretbeli (worldSeed-fuggetlen, egysegvektor) pozicio
        /// megjelenitesi helye a domborzat feluleten - UGYANAZ a keplet, mint
        /// <see cref="RiverTileCenterOnSurface"/>, csak tetszoleges (nem
        /// tile-hoz kotott) pontra, ld. RiverPathTracing.TraceRiverPathContinuous.
        /// </summary>
        private Vector3 RiverPositionOnSurface(double x, double y, double z)
        {
            float displacedRadius = ComputeDisplacedRadius(x, y, z, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters);
            Vector3 p = BodyFrameConversion.ToUnity(x, y, z) * displacedRadius;
            return p + p.normalized * riverLineRadialBias;
        }

        /// <summary>Uniform Catmull-Rom spline-pont a [p1,p2] szakaszon, t in [0,1] (p0/p3 az irányítótangensek).</summary>
        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Egy tile KÖZEPÉNEK pozíciója a domborzat felszínén, kis sugár-biasszal (folyó-vonalhoz).</summary>
        private Vector3 RiverTileCenterOnSurface(TileId tile)
        {
            int n = 1 << tile.Level;
            tile.GetUV(out uint u, out uint v);
            double uc = (u + 0.5) / n * 2.0 - 1.0;
            double vc = (v + 0.5) / n * 2.0 - 1.0;
            Vector3 p = ToDisplacedVector3(tile.Face, uc, vc, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters);
            return p + p.normalized * riverLineRadialBias;
        }

        private Material _lakeSurfaceMaterial;

        /// <summary>
        /// ND-129: a part menti tó-tile-ok al-osztása N×N al-quadra. A lapos
        /// quad a gömb HÚRJA: egy level-8 (~36 km) tile közepe R·θ²/8 ≈ 25 m-rel
        /// a helyes sugár ALATT van, ami a part menti sávban a terep alá viszi
        /// a vízfelszínt (MÉRVE, élő Editor: a tó-sarkok 14,8%-a a vízszinttől
        /// ±25 m-en belül van). N=4 (level 10) mellett a behúrás 25 m → 1,6 m.
        /// EGYENLETES al-osztás a part menti tile-okon: így két al-osztott
        /// szomszéd között nincs T-csomópont; a durván maradt szomszéd felé az
        /// él-pontokat a húrra ejtjük (ld. SnapEdgeToChord).
        /// </summary>
        private const int LakeSurfaceSubdivision = 4;

        /// <summary>
        /// ND-129: ha egy tile MIND a négy sarka legalább ennyivel a vízszint
        /// alatt van, a tile egyetlen quad marad - ott a húr-behúrás
        /// láthatatlan (a terep sosem metszi a vízfelszínt a tile-on belül).
        /// </summary>
        private const double LakeSurfaceFlatMarginMeters = 40.0;

        /// <summary>
        /// ND-129: hányszor terjeszthető TOVÁBB a vízfelszín a tó-tile-ok
        /// 1-gyűrűjén túl. A további körök CSAK azok körül futnának, amik
        /// TELJESEN víz alá kerültek (mind a négy sarok a vízszint alatt) -
        /// ott ugyanis a part még mindig nyers tile-él marad.
        ///
        /// AZ ÉRTÉK 1, MÉRÉS ALAPJÁN. Az első gyűrű 9281 tile-jából 129 (1,4%)
        /// kerül teljesen víz alá. A 4 körös változat élőben MÉRVE: +528 tile
        /// (+2,5%), DE a nyitott (teljesen elöntött) határ NEM fogyott el,
        /// hanem NŐTT (129 → 154), és az idő 724 ms → 1054 ms. Vagyis a
        /// terjesztés nem konvergál: a LEFOLYÁSNÁL a völgytalp hosszan a
        /// feltöltési szint alatt marad, tehát a tó a folyó medrében „elfolyna",
        /// miközben a blokkos maradék ugyanakkora. Egy gyűrű a jó kompromisszum;
        /// a konstans emelése egysoros, ha a vizuális ítélet mást mond.
        /// </summary>
        private const int LakeSurfaceMaxRingExpansions = 1;

        /// <summary>
        /// LAPOS tó-vízfelszín a (szűrt) tavak feltöltési szintjén: opak, kék,
        /// megvilágított lap a tó-tile-ok fölé a tó-felszín sugaránál. EGYSZER
        /// épül fel (Build), referencia-szintű, statikus - mint a folyó-réteg.
        ///
        /// ND-129 (felhasználói visszajelzés: "a tavak zoomra nagy tile-okból
        /// összerakottnak néznek ki"). A KORÁBBI változat tile-onként EGYETLEN
        /// quadot rakott le, és a vízfelszín a tó-tile halmaz szélén VÉGET ért
        /// a tile-határon - ezért a partvonal tengelypárhuzamos, 36 km-es
        /// lépcsőkből állt. A partvonal forrása MOSTANTÓL a TEREP: a vízfelszín
        /// TÚLNYÚLIK a valódi parton (tó-tile-ok + 1 gyűrű szomszéd), és a már
        /// renderelt, adaptív terep-mesh vágja ki belőle a partot a z-bufferrel.
        /// Így a partvonal NULLA új eleváció-kiértékelésből adódik, egyetlen
        /// definíció szerint (a terep), és a részletessége AUTOMATIKUSAN
        /// követi a terep-LOD-ot (level 20-ig) - vagyis zoomra magától finomodik.
        ///
        /// MÉRT alap a kiterjesztés biztonságához (élő Editor, seed
        /// 0xA7C944210000, a legnagyobb tó 404 tile-ja): az 1-gyűrű
        /// szomszédok 78,1%-a VEGYES (a vízszint áthalad rajtuk) és EGYETLEN
        /// EGY SINCS, amelyik teljes egészében víz alatt lenne - egy gyűrűnyi
        /// kiterjesztés tehát sehol nem önt el egész tile-t. A csupa víz
        /// FELETTI sarkú gyűrű-tile-okat (mérve 21,9%) kihagyjuk: ott a
        /// vízfelszín amúgy is teljesen takarva lenne.
        /// </summary>
        private void BuildLakeSurface()
        {
            Transform child = transform.Find("LakeSurface");
            if (!showLakesIce || _adaptiveLakeSurface == null || _adaptiveLakeSurface.Count == 0)
            {
                if (child != null) child.gameObject.SetActive(false);
                return;
            }

            var lakeSurfaceStopwatch = Stopwatch.StartNew();
            int lakeLevel = -1;
            foreach (KeyValuePair<TileId, double> first in _adaptiveLakeSurface) { lakeLevel = first.Key.Level; break; }

            // 1. Sarok-elevációk. A level-8 SAROK terrain-bázisa MÁR MEGVAN
            //    (ND-63/ND-122 lemez-cache, EnsureStaticTerrainBasisCache) -
            //    ezért itt nincs új TerrainPointBasis.Compute, csak Evaluate.
            //    A cache lap-lokális sarok-rácspontra kulcsol, tehát a négy
            //    szomszédos tile ugyanazt a sarkot EGYSZER számolja ki.
            var cornerElevations = new Dictionary<(int Face, uint U, uint V), double>();
            int cornerBasisHits = 0, cornerBasisMisses = 0;

            double CornerElevation(int face, uint cornerU, uint cornerV)
            {
                var key = (face, cornerU, cornerV);
                if (cornerElevations.TryGetValue(key, out double cached)) return cached;
                int n = 1 << lakeLevel;
                double uc = (double)cornerU / n * 2.0 - 1.0;
                double vc = (double)cornerV / n * 2.0 - 1.0;
                TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);
                double elevation;
                if (TryGetStaticTerrainBasis(face, lakeLevel, cornerU, cornerV,
                        out TerrainPointBasis basis, out _, out _))
                {
                    cornerBasisHits++;
                    elevation = ComputeElevationAtPointFromBasis(
                        x, y, z, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters,
                        _adaptiveErosionTimeMyr, in basis);
                }
                else
                {
                    cornerBasisMisses++;
                    elevation = ComputeElevationAtPoint(
                        x, y, z, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters, _adaptiveErosionTimeMyr);
                }
                cornerElevations[key] = elevation;
                return elevation;
            }

            // 2. A RAJZOLT tile-halmaz: a tó-tile-ok + a szomszédjaik.
            //    Egy gyűrű-tile a szomszédos tó vízszintjét kapja; ha többhöz is
            //    hozzáér (ritka), a LEGALACSONYABBAT - az a konzervatív (legkisebb
            //    túlnyúlású) választás, és a minimum sorrendfüggetlen.
            //    A gyűrű CSAK ott terjed tovább, ahol az új tile TELJESEN víz alá
            //    került (ott a part még mindig nyers tile-él lenne) - legfeljebb
            //    LakeSurfaceMaxRingExpansions körig, ld. a konstans doksiját.
            bool FullyFlooded(TileId id, double surface)
            {
                id.GetUV(out uint fu, out uint fv);
                return CornerElevation(id.Face, fu, fv) < surface
                    && CornerElevation(id.Face, fu + 1, fv) < surface
                    && CornerElevation(id.Face, fu + 1, fv + 1) < surface
                    && CornerElevation(id.Face, fu, fv + 1) < surface;
            }

            var surfaceByTile = new Dictionary<TileId, double>(_adaptiveLakeSurface);
            var frontier = new List<TileId>(_adaptiveLakeSurface.Keys);
            int ringRounds = 0, ringUnfinished = 0;
            for (int round = 0; round < LakeSurfaceMaxRingExpansions; round++)
            {
                var added = new List<TileId>();
                foreach (TileId from in frontier)
                {
                    double surface = surfaceByTile[from];
                    for (int d = 0; d < 4; d++)
                    {
                        TileId nb = TileNeighbors.Neighbor(from, (TileDirection)d);
                        if (_adaptiveLakeSurface.ContainsKey(nb)) continue;
                        if (surfaceByTile.TryGetValue(nb, out double existing))
                        {
                            if (surface < existing) surfaceByTile[nb] = surface;
                            continue;
                        }
                        surfaceByTile[nb] = surface;
                        added.Add(nb);
                    }
                }
                ringRounds++;
                if (added.Count == 0) break;
                var next = new List<TileId>();
                foreach (TileId id in added)
                    if (FullyFlooded(id, surfaceByTile[id])) next.Add(id);
                frontier = next;
                ringUnfinished = next.Count;
                if (next.Count == 0) break;
            }

            // 3. Tile-onkénti besorolás: kihagyott / lapos (1 quad) / al-osztott.
            //    A "kihagyott" az a tile, aminek MIND a négy sarka a vízszint
            //    FÖLÖTT van - ott a terep úgyis teljesen eltakarná a vizet.
            var subdivided = new HashSet<TileId>();
            var drawn = new Dictionary<TileId, double>(surfaceByTile.Count);
            int skipped = 0;
            // DIAGNOSZTIKA (ND-129): hany GYURU-tile kerul TELJES egeszeben viz
            // ala? Az ilyen tile-nal a vizfelszin a gyurun TULRA is folytatodna,
            // tehat egy gyuru nem elegendo - a partvonal ott megint nyers
            // tile-el lenne. A mert ertek dontse el, kell-e masodik gyuru.
            int ringFullyFlooded = 0;
            foreach (KeyValuePair<TileId, double> kv in surfaceByTile)
            {
                TileId id = kv.Key;
                double surface = kv.Value;
                id.GetUV(out uint u, out uint v);
                double e00 = CornerElevation(id.Face, u, v);
                double e10 = CornerElevation(id.Face, u + 1, v);
                double e11 = CornerElevation(id.Face, u + 1, v + 1);
                double e01 = CornerElevation(id.Face, u, v + 1);
                if (e00 > surface && e10 > surface && e11 > surface && e01 > surface) { skipped++; continue; }
                if (!_adaptiveLakeSurface.ContainsKey(id)
                    && e00 < surface && e10 < surface && e11 < surface && e01 < surface)
                    ringFullyFlooded++;
                drawn[id] = surface;
                if (surface - e00 < LakeSurfaceFlatMarginMeters
                    || surface - e10 < LakeSurfaceFlatMarginMeters
                    || surface - e11 < LakeSurfaceFlatMarginMeters
                    || surface - e01 < LakeSurfaceFlatMarginMeters)
                    subdivided.Add(id);
            }

            // 4. Emisszió. A csúcsszám ITT MÁR PONTOSAN ISMERT (a besorolás
            //    lezárult), ezért előre foglalunk - egy ~1 M csúcsos mesh-nél a
            //    List<T> duplázgatása önmagában tíz MB-os újramásolás.
            const int n4 = LakeSurfaceSubdivision;
            int expectedQuads = (drawn.Count - subdivided.Count) + subdivided.Count * n4 * n4;
            int expectedVerts = expectedQuads * 4;
            var verts = new List<Vector3>(expectedVerts);
            var normals = new List<Vector3>(expectedVerts);
            var tris = new List<int>(expectedQuads * 6);
            var colors = new List<Color>(expectedVerts);
            Color lakeColor = new Color(0.13f, 0.40f, 0.62f); // edesviz-kek
            var grid = new Vector3[n4 + 1, n4 + 1];
            int quadCount = 0;
            foreach (KeyValuePair<TileId, double> kv in drawn)
            {
                TileId t = kv.Key;
                float r = radius + (float)(DisplayElevation(kv.Value) * elevationScale);
                TileGeometry.GetContinuousBounds(t, out double uMin, out double uMax, out double vMin, out double vMax);
                if (!subdivided.Contains(t))
                {
                    Vector3 q00 = ToWaterVector3(t.Face, uMin, vMin, r);
                    Vector3 q10 = ToWaterVector3(t.Face, uMax, vMin, r);
                    Vector3 q11 = ToWaterVector3(t.Face, uMax, vMax, r);
                    Vector3 q01 = ToWaterVector3(t.Face, uMin, vMax, r);
                    AddQuad(verts, normals, tris, colors, lakeColor, q00, q10, q11, q01);
                    quadCount++;
                    continue;
                }

                for (int i = 0; i <= n4; i++)
                {
                    double uc = uMin + (uMax - uMin) * i / n4;
                    for (int j = 0; j <= n4; j++)
                    {
                        double vc = vMin + (vMax - vMin) * j / n4;
                        grid[i, j] = ToWaterVector3(t.Face, uc, vc, r);
                    }
                }

                // T-CSOMÓPONT: ha a szomszéd RAJZOLT, de NEM al-osztott, az ő
                // éle egyetlen egyenes húr a két közös sarok között, a miénk
                // viszont a helyes sugáron futó törtvonal - a kettő között
                // hajszálrés maradna. Ezért a KÖZÖS él belső pontjait a húrra
                // ejtjük. Ahol a szomszéd NINCS rajzolva, ott nincs mihez
                // illeszteni, és a húrra ejtés csak fölöslegesen süllyesztené
                // a vízfelszín szélét - ott a helyes sugár marad.
                if (IsCoarseDrawnNeighbor(t, TileDirection.Left, drawn, subdivided))
                    SnapEdgeToChord(grid, n4, 0, false);
                if (IsCoarseDrawnNeighbor(t, TileDirection.Right, drawn, subdivided))
                    SnapEdgeToChord(grid, n4, n4, false);
                if (IsCoarseDrawnNeighbor(t, TileDirection.Down, drawn, subdivided))
                    SnapEdgeToChord(grid, n4, 0, true);
                if (IsCoarseDrawnNeighbor(t, TileDirection.Up, drawn, subdivided))
                    SnapEdgeToChord(grid, n4, n4, true);

                for (int i = 0; i < n4; i++)
                {
                    for (int j = 0; j < n4; j++)
                    {
                        AddQuad(verts, normals, tris, colors, lakeColor,
                            grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                        quadCount++;
                    }
                }
            }

            GameObject go;
            if (child == null)
            {
                go = new GameObject("LakeSurface");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                _lakeSurfaceMaterial ??= CreateVertexColorMaterial();
                mr.sharedMaterial = _lakeSurfaceMaterial;
            }
            else
            {
                go = child.gameObject;
                go.SetActive(true);
            }

            MeshFilter mf = go.GetComponent<MeshFilter>();
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            else mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;

            PerfLog(
                $"[ND-129 lake surface] level={lakeLevel} lakeTiles={_adaptiveLakeSurface.Count} " +
                $"withRing={surfaceByTile.Count} drawn={drawn.Count} skippedAboveWater={skipped} " +
                $"subdivided={subdivided.Count} (N={n4}) ringRounds={ringRounds} " +
                $"ringFullyFlooded={ringFullyFlooded} stillFlooded={ringUnfinished} " +
                $"quads={quadCount} verts={verts.Count} " +
                $"cornerSamples={cornerElevations.Count} (basisHits={cornerBasisHits} misses={cornerBasisMisses}) " +
                $"{lakeSurfaceStopwatch.Elapsed.TotalMilliseconds:F1}ms");
        }

        /// <summary>
        /// ND-129: a <paramref name="direction"/> irányú szomszéd RAJZOLT-e, de
        /// NEM al-osztott - vagyis keletkezne-e T-csomópont a közös élen.
        /// </summary>
        private static bool IsCoarseDrawnNeighbor(
            TileId tile, TileDirection direction,
            Dictionary<TileId, double> drawn, HashSet<TileId> subdivided)
        {
            TileId nb = TileNeighbors.Neighbor(tile, direction);
            return drawn.ContainsKey(nb) && !subdivided.Contains(nb);
        }

        /// <summary>
        /// ND-129: egy al-osztott tile EGYIK élének belső pontjait a két
        /// végpont közti EGYENESRE (húrra) ejti - ez az, amit a szomszéd durva
        /// quad éle ténylegesen kirajzol, tehát így nem marad rés. Az
        /// <paramref name="alongV"/> azt mondja meg, melyik irányban fut az él:
        /// hamis = fix i (u-szélső oszlop), igaz = fix j (v-szélső sor).
        /// </summary>
        private static void SnapEdgeToChord(Vector3[,] grid, int n, int fixedIndex, bool alongV)
        {
            Vector3 a = alongV ? grid[0, fixedIndex] : grid[fixedIndex, 0];
            Vector3 b = alongV ? grid[n, fixedIndex] : grid[fixedIndex, n];
            for (int k = 1; k < n; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, (float)k / n);
                if (alongV) grid[k, fixedIndex] = p;
                else grid[fixedIndex, k] = p;
            }
        }

        /// <summary>
        /// A csapadék-mező (diszkrét, referencia-szintű tile-értékek)
        /// SIMÍTÁSA egy tile-sarokra: a `tile` és a `horizontalDir`/
        /// `verticalDir` irányú szomszédja + az átlós szomszéd
        /// csapadékának ÉS óceán-arányának átlaga. A csapadék - ellentétben
        /// az elevációval - NEM egy LOD-független, pontszerűen kiértékelhető
        /// folytonos függvény (globális, iteratív advekció eredménye, ld.
        /// MoisturePrecipitation.Compute), ezért itt a MÁR meglévő diszkrét
        /// tile-értékek egyszerű szomszéd-átlagolása adja a "sarok-értéket" -
        /// ez teszi lehetővé, hogy a felhő-kvadok GPU-interpolált, folytonos
        /// sűrűség-átmenetet kapjanak a mesterségesen éles tile-határ
        /// helyett (ld. BuildClouds doksi).
        ///
        /// FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06, "egyetlen függőleges
        /// vonalban... a rendszer eleje és vége nem match-el"): egy KORÁBBI
        /// verzió a szomszédokat NYERS (u,v) INDEX-ARITMETIKÁVAL kereste meg
        /// (`cornerU±1` a SAJÁT face-en belül), és a lap SZÉLÉN/SARKÁN
        /// egyszerűen KIHAGYTA a hiányzó szomszédokat - ez a kocka-lapok
        /// (jellemzően négy, egyenlítő menti oldal-lap) HATÁRÁN, ami a
        /// gömbön PONTOSAN egy pólustól-pólusig futó, "függőlegesnek" tűnő
        /// meridián-vonal, ÉLES varratot okozott. JAVÍTÁS: a NEM-átlós
        /// szomszédokat (`hNb`/`vNb`) a bizonyítottan helyes, lap-határt
        /// geometriailag kezelő <see cref="TileNeighbors.Neighbor"/>-ral
        /// keressük meg - ez UGYANAZ a mechanizmus, amit maga a
        /// MoisturePrecipitation.Compute is használ az advekcióhoz.
        ///
        /// AZ ÁTLÓS SZOMSZÉDRA (`diagNb`) EZ NEM TERJESZTHETŐ KI EGYSZERŰEN -
        /// MÉRÉSSEL ELLENŐRIZVE (ld. <see cref="TileNeighbors.DiagonalNeighbor"/>
        /// doksi): sem a két egymást követő `Neighbor`-hívás
        /// (`Neighbor(hNb, verticalDir)`), sem egy egy-lépéses geometriai
        /// átlós-lépés nem ad mindig egyértelműen "helyes" eredményt a
        /// kocka-lapok ÉLE/CSÚCSA közelében - a fogalom ott matematikailag
        /// sem egyértelmű. EZÉRT: ha akár a vízszintes, akár a függőleges
        /// lépés lap-határt lépett át (`hNb.Face != tile.Face` vagy
        /// `vNb.Face != tile.Face`), EGYSZERŰEN KIHAGYJUK az átlós mintát -
        /// a sarok-átlag ilyenkor csak 3 tile-ból (tile+hNb+vNb) számol, egy
        /// pontatlan negyedik érték beleerőltetése helyett. Ez a lap-határ
        /// menti tile-ok kis hányadát (mérve: kb. 3%) érinti enyhén kevésbé
        /// simított sarok-értékkel, de SOHA nem ad geometriailag rossz
        /// (más térbeli helyről vett) mintát.
        /// </summary>
        private static (double Precip, double OceanFraction) PrecipAndOceanFractionAtCorner(
            Dictionary<TileId, double> precip, Dictionary<TileId, bool> isOcean,
            TileId tile, TileDirection horizontalDir, TileDirection verticalDir)
        {
            TileId hNb = TileNeighbors.Neighbor(tile, horizontalDir);
            TileId vNb = TileNeighbors.Neighbor(tile, verticalDir);
            bool crossedFace = hNb.Face != tile.Face || vNb.Face != tile.Face;

            double sum = 0.0;
            int count = 0;
            int oceanCount = 0;
            void Add(TileId t)
            {
                if (precip.TryGetValue(t, out double p))
                {
                    sum += p;
                    count++;
                    if (isOcean.TryGetValue(t, out bool oc) && oc) oceanCount++;
                }
            }
            Add(tile); Add(hNb); Add(vNb);
            if (!crossedFace)
                Add(TileNeighbors.Neighbor(hNb, verticalDir));
            return count > 0 ? (sum / count, (double)oceanCount / count) : (0.0, 0.0);
        }

        /// <summary>A `sortedValues` (növekvő sorrendben rendezett) lista `p` (0..1) percentilise - egyszerű, lineáris interpoláció nélküli index-választás (elég egy vizuális küszöbhöz).</summary>
        private static double PercentileValue(List<double> sortedValues, double p)
        {
            if (sortedValues.Count == 0) return 0.0;
            int idx = (int)(Math.Clamp(p, 0.0, 1.0) * (sortedValues.Count - 1));
            return sortedValues[idx];
        }

        private Material _cloudMaterial;

        /// <summary>
        /// M6/M13 felhő-réteg MVP (ld. showClouds mező doksija): egy második,
        /// átlátszó gömbhéj a felszín fölött, aminek a sűrűsége a MÁR
        /// kiszámolt csapadék-mezőből jön (nincs kézzel festett textúra, I3).
        /// `precipField == null` -> a réteg elrejtve (showClouds kikapcsolva,
        /// VAGY egyetlen más réteg sem igényelte a csapadék-mező kiszámítását
        /// ebben a Build()-ben).
        ///
        /// FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06, "katasztrófa... lehangoló"):
        /// az ELSŐ verzió tile-onként EGY egyenletes alfa-értéket adott mind a
        /// 4 sarokra - ez PONTOSAN ugyanaz a hiba-osztály volt, mint a
        /// folyó/tó korábbi blokkosodása: a referencia-szintű (~100-800 km-es)
        /// tile-határok élesen látszottak, "hatalmas blokk-felhőket" adva.
        /// JAVÍTÁS (két rész, a MÁR bevált <see cref="ContinuousCornerColor"/>/
        /// sarok-cache mintát követve, ld. Build()-ben a terep-szín):
        ///
        /// 1. SAROK-INTERPOLÁLT SŰRŰSÉG: a csapadék-mező (ellentétben az
        ///    elevációval) NEM egy LOD-független, pontszerűen kiértékelhető
        ///    folytonos függvény - egy teljes tile-hálón futó, 24 lépéses
        ///    iteratív advekció eredménye (ld. MoisturePrecipitation.Compute),
        ///    tehát nincs "ContinuousPrecipitationAtPosition". Helyette minden
        ///    RÁCSPONTRA (tile-sarokra) a 4 ÉRINTKEZŐ tile csapadékának és
        ///    óceán-arányának átlagát számítjuk (<see
        ///    cref="PrecipAndOceanFractionAtCorner"/>), és ezt adjuk át a MÁR
        ///    létező, sarkonként-külön-színt fogadó `AddQuad` overloadnak - a
        ///    GPU lineárisan interpolál a kvadon belül, a szomszédos kvadok
        ///    közös sarkuknál AZONOS értéket kapnak, tehát a tile-határ
        ///    eltűnik (mint a terep/víz színénél).
        /// 2. DOMAIN-RELATÍV PERCENTILIS-KÜSZÖB + GAMMA-GÖRBÍTÉS: MÉRVE (egy
        ///    valós világon) az óceán fölötti nyers csapadék-eloszlás
        ///    ÁTLAGA ~4×-e a szárazföldinek (óceán 4.67 vs. szárazföld 1.27)
        ///    - egy KÖZÖS, abszolút küszöb/normalizáló ezért a felhasználói
        ///    visszajelzés szerint "az óceánokat teljesen befedte, a
        ///    kontinenseket elkerülte". A küszöböt és a felső "padlót" KÜLÖN
        ///    percentilisként számítjuk az óceáni és a szárazföldi csapadék-
        ///    eloszláson (mint a folyó-forrás kiválasztásnál, ND-49). FONTOS,
        ///    MÉRÉSSEL FELTÁRT CSAPDA: egy KORÁBBI változat magát a KÜSZÖBÖT
        ///    interpolálta a rácspont óceán-arányával - mivel a legtöbb
        ///    szárazföld PART KÖZELÉBEN van (tehát pozitív óceán-arányú
        ///    sarkokkal érintkezik), ez a küszöböt szisztematikusan az
        ///    óceáni (magasabb) érték felé tolta a part menti szárazföldnél,
        ///    "büntetve" pont azt a területet, ahol a legtöbb szárazföld
        ///    ténylegesen van ("a szárazföld felett továbbra is alig van
        ///    felhő" - felhasználói visszajelzés). JAVÍTÁS: a NYERS
        ///    csapadékot KÜLÖN alakítjuk mindkét domain saját floor/ceil-jével,
        ///    és a KÉSZ (0..1) sűrűséget interpoláljuk az óceán-arány szerint
        ///    - így egy TISZTA szárazföldi sarok a szárazföldi eloszláshoz
        ///    mérve kap sűrűséget, függetlenül az óceán közelségétől.
        ///
        /// FELHASZNÁLÓI VISSZAJELZÉS (backlog "Felhő-mozgás", 2026-09-06):
        /// "olyan mintha nem mozogna" - a réteg korábban csak Build()-kor
        /// frissült. A `cloudTime` (0.0 = nincs sodródás) a MÁR meglévő,
        /// verifikált <see cref="WindPrecipitation.WeatherPrecipitationMultiplier"/>
        /// -t hívja meg minden SAROK saját pozíciójában, és ezzel szorozza
        /// meg a klimatológiai csapadék-átlagot, MIELŐTT a küszöb/gamma-
        /// alakítás lefutna - ez a MÁR a Core-ban létező, idő-koherens
        /// "időjárás-zaj" mechanizmus (§32), amit eddig semmi nem hívott meg
        /// nem-nulla `t`-vel. I3-kompatibilis: a mozgás forrása egy valódi,
        /// a worldSeedből generált mező, nem dekoratív UV-csúsztatás.
        ///
        /// Ez a metódus SZINKRON marad (a Build()-en belüli, egyszeri hívás
        /// nem probléma) - a PERIODIKUS (felhő-sodródás/config-változás)
        /// újraépítés a háttérszálas <see cref="ComputeCloudMeshData"/>/
        /// <see cref="ApplyCloudOnlyRebuild"/> útvonalon megy, ld. ott.
        /// </summary>
        private void BuildClouds(MoisturePrecipitation.PrecipitationField? precipField, double cloudTime = 0.0)
        {
            if (precipField == null)
            {
                Transform child = transform.Find("Clouds");
                if (child != null) child.gameObject.SetActive(false);
                return;
            }

            CloudMeshData data = ComputeCloudMeshData(
                precipField, cloudTime, _adaptiveSeed, radius, elevationScale,
                cloudAltitudeMeters, cloudDensityThreshold, cloudDensityGamma, cloudMaxOpacity);
            ApplyCloudMeshData(data);
        }

        /// <summary>
        /// A felhő-réteg GEOMETRIA-számítása - TISZTA (nincs Unity API-hívás,
        /// csak Vector3/Color POD-adat épül), ezért háttérszálról (Task.Run)
        /// is biztonsággal hívható, ld. ApplyCloudOnlyRebuild doksi. MINDEN
        /// bemenetet EXPLICIT PARAMÉTERKÉNT kap ahelyett, hogy közvetlenül a
        /// mezőket (`this.cloudDensityThreshold` stb.) olvasná - így egy
        /// KÖZBEN (a számítás futása alatt) módosuló Inspector-csúszka nem
        /// okoz versenyhelyzetet a háttérszállal.
        /// </summary>
        private CloudMeshData ComputeCloudMeshData(
            MoisturePrecipitation.PrecipitationField precipField, double cloudTime, ulong seed,
            float radiusParam, double elevationScaleParam, double cloudAltitudeMetersParam,
            double cloudDensityThresholdParam, double cloudDensityGammaParam, float cloudMaxOpacityParam)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            var colors = new List<Color>();
            float cloudRadius = radiusParam + (float)(cloudAltitudeMetersParam * elevationScaleParam);
            var cornerDensityCache = new Dictionary<(TileId Tile, int Corner), float>();

            // Domain-relativ percentilis-kuszob es -padlo, KULON az ocean es a
            // szarazfold csapadek-eloszlasan (ld. cloudDensityThreshold doksi -
            // a nyers skala ocean felett mert ~4x magasabb, ez torzitotta el
            // a regi, kozos-kuszobos verziot).
            const double CeilingPercentile = 0.97;
            var oceanVals = new List<double>();
            var landVals = new List<double>();
            foreach (KeyValuePair<TileId, double> kv0 in precipField.Precipitation)
                (precipField.IsOcean.TryGetValue(kv0.Key, out bool oc) && oc ? oceanVals : landVals).Add(kv0.Value);
            oceanVals.Sort(); landVals.Sort();
            double oceanFloor = PercentileValue(oceanVals, cloudDensityThresholdParam);
            double oceanCeil = Math.Max(oceanFloor + 1e-6, PercentileValue(oceanVals, CeilingPercentile));
            double landFloor = PercentileValue(landVals, cloudDensityThresholdParam);
            double landCeil = Math.Max(landFloor + 1e-6, PercentileValue(landVals, CeilingPercentile));

            // cornerIdx: 0=balalso(Left+Down) 1=jobbalso(Right+Down) 2=jobbfelso(Right+Up) 3=balfelso(Left+Up)
            // - egyezik a p00/p10/p11/p01 (uMin/vMin .. uMin/vMax) sorrenddel lent.
            //
            // FONTOS, MÉRÉSSEL FELTÁRT CSAPDA: az ELSŐ verzió a KÜSZÖBÖT
            // interpolálta az óceán-arány szerint (`floor = lerp(landFloor,
            // oceanFloor, oceanFrac)`) - ez a legtöbb szárazföld esetén
            // (ami tipikusan PART KÖZELÉBEN van, tehát oceanFrac > 0) a
            // küszöböt az óceáni (magasabb) érték felé tolta, gyakorlatilag
            // "büntetve" a part menti szárazföldet - a felhasználó szerint
            // "a szárazföld felett továbbra is alig van felhő". JAVÍTÁS: a
            // NYERS csapadékot KÜLÖN alakítjuk mindkét domain saját
            // floor/ceil-jével, és a KÉSZ (0..1) sűrűséget interpoláljuk az
            // óceán-arány szerint - így egy TISZTA szárazföldi sarok
            // (oceanFrac=0) pontosan a szárazföldi eloszláshoz mérve kap
            // sűrűséget, függetlenül attól, hogy közel van-e az óceánhoz.
            float DensityAt(TileId tile, TileDirection hDir, TileDirection vDir, int cornerIdx)
            {
                var key = (tile, cornerIdx);
                if (cornerDensityCache.TryGetValue(key, out float cached))
                    return cached;
                (double precip, double oceanFrac) = PrecipAndOceanFractionAtCorner(precipField.Precipitation, precipField.IsOcean, tile, hDir, vDir);
                if (cloudTime != 0.0)
                {
                    // A SAROK sajat (nem a tile-kozepponti) pozicioja kell -
                    // ket szomszedos tile UGYANAZT a sarkot osztja, tehat
                    // UGYANAZT a szorzot kell kapnia (kulonben a mar
                    // megoldott varrat-hiba terne vissza, ld. BuildClouds doksi).
                    TileGeometry.GetContinuousBounds(tile, out double cuMin, out double cuMax, out double cvMin, out double cvMax);
                    double cu = (cornerIdx == 1 || cornerIdx == 2) ? cuMax : cuMin;
                    double cv = (cornerIdx == 2 || cornerIdx == 3) ? cvMax : cvMin;
                    TileGeometry.PositionFromFaceUV(tile.Face, cu, cv, out double cx2, out double cy2, out double cz2);
                    precip *= WindPrecipitation.WeatherPrecipitationMultiplier(seed, cx2, cy2, cz2, cloudTime);
                }
                double landShaped = precip <= landFloor ? 0.0 : Math.Pow(Math.Min(1.0, (precip - landFloor) / (landCeil - landFloor)), cloudDensityGammaParam);
                double oceanShaped = precip <= oceanFloor ? 0.0 : Math.Pow(Math.Min(1.0, (precip - oceanFloor) / (oceanCeil - oceanFloor)), cloudDensityGammaParam);
                float shaped = (float)(landShaped + (oceanShaped - landShaped) * oceanFrac);
                cornerDensityCache[key] = shaped;
                return shaped;
            }

            foreach (KeyValuePair<TileId, double> kv in precipField.Precipitation)
            {
                TileId t = kv.Key;
                float d00 = DensityAt(t, TileDirection.Left, TileDirection.Down, 0);
                float d10 = DensityAt(t, TileDirection.Right, TileDirection.Down, 1);
                float d11 = DensityAt(t, TileDirection.Right, TileDirection.Up, 2);
                float d01 = DensityAt(t, TileDirection.Left, TileDirection.Up, 3);
                if (d00 <= 0.001f && d10 <= 0.001f && d11 <= 0.001f && d01 <= 0.001f)
                    continue; // mind a 4 sarok gyakorlatilag szaraz - nincs ertelme geometriat generalni ra
                Color c00 = new Color(1f, 1f, 1f, d00 * cloudMaxOpacityParam);
                Color c10 = new Color(1f, 1f, 1f, d10 * cloudMaxOpacityParam);
                Color c11 = new Color(1f, 1f, 1f, d11 * cloudMaxOpacityParam);
                Color c01 = new Color(1f, 1f, 1f, d01 * cloudMaxOpacityParam);
                TileGeometry.GetContinuousBounds(t, out double uMin, out double uMax, out double vMin, out double vMax);
                Vector3 p00 = ToWaterVector3(t.Face, uMin, vMin, cloudRadius);
                Vector3 p10 = ToWaterVector3(t.Face, uMax, vMin, cloudRadius);
                Vector3 p11 = ToWaterVector3(t.Face, uMax, vMax, cloudRadius);
                Vector3 p01 = ToWaterVector3(t.Face, uMin, vMax, cloudRadius);
                AddQuad(verts, normals, tris, colors, c00, c10, c11, c01, p00, p10, p11, p01);
            }

            return new CloudMeshData { Vertices = verts, Normals = normals, Triangles = tris, Colors = colors };
        }

        /// <summary>Fő szálon (Unity API) alkalmazza a (esetleg háttérszálon számolt) kész felhő-geometriát.</summary>
        private void ApplyCloudMeshData(CloudMeshData data)
        {
            Transform child = transform.Find("Clouds");
            GameObject go;
            if (child == null)
            {
                go = new GameObject("Clouds");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                _cloudMaterial ??= CreateCloudMaterial();
                mr.sharedMaterial = _cloudMaterial;
            }
            else
            {
                go = child.gameObject;
                go.SetActive(true);
            }

            MeshFilter mf = go.GetComponent<MeshFilter>();
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            else mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetColors(data.Colors);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }

        /// <summary>
        /// A folyó-SZALAG mesh feltöltése - korábban `MeshTopology.Lines`
        /// volt (vékony, egységes vastagságú vonal), MOST `MeshTopology.
        /// Triangles` egy tömör, a vízhozammal arányosan szélesedő szalag-
        /// geometriával (ld. `AddRiverRibbon`/`BuildRiverNetwork` doksi) -
        /// ezért normálisokat is kap (a régi vonal-topológiának nem
        /// kellett, az `HDRP/Lit` anyag háromszögekhez viszont igen).
        /// </summary>
        private void BuildRivers(List<Vector3> verts, List<Vector3> normals, List<int> triangles)
        {
            Transform child = transform.Find("Rivers");
            GameObject go;
            if (child == null)
            {
                go = new GameObject("Rivers");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                _riverLineMaterial ??= CreateFlatColorMaterial(CategoryColor(RenderCategory.River, 0));
                mr.sharedMaterial = _riverLineMaterial;
            }
            else
            {
                go = child.gameObject;
                go.SetActive(true);
            }

            MeshFilter mf = go.GetComponent<MeshFilter>();
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            else mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
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

        /// <summary>
        /// A `bucket` paraméter csak a hívó oldali (BuildWaterSurface)
        /// submesh-csoportosítás miatt maradt meg a szignatúrában - a
        /// TÉNYLEGES színt mostantól a vertex-szín adja (ContinuousWaterColor,
        /// AddQuad-ban állítva), ezért minden bucket UGYANAZT a KÜLÖN
        /// víz-anyagot kapja (ld. CreateWaterSurfaceMaterial doksija -
        /// KORÁBBAN a szárazfölddel megosztott `_vertexColorMaterial`-t
        /// használta, ez okozott tul eros csillanast).
        /// </summary>
        private Material GetOrCreateWaterMaterial(int bucket)
        {
            return CreateWaterSurfaceMaterial();
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
            Dictionary<int, List<int>> trianglesByBucket,
            Dictionary<int, List<Color>> colorsByBucket,
            string childName, StaticBuildAllocationProfile allocationProfile = null)
        {
            Transform waterChild = transform.Find(childName);
            GameObject waterGo;
            if (waterChild == null)
            {
                waterGo = new GameObject(childName);
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
            _drawnDiagnosticMeshes.Remove(waterGo);

            allocationProfile?.Next("WorldGen.StaticBase.water.assemble");
            var buckets = new List<int>(verticesByBucket.Keys);
            buckets.Sort();

            // ND-134: ugyanaz az előfoglalás, mint a terrain összefűzésénél.
            int vertexCount = 0, normalCount = 0, colorCount = 0;
            foreach (int bucket in buckets)
            {
                if (verticesByBucket[bucket].Count == 0) continue;
                vertexCount = checked(vertexCount + verticesByBucket[bucket].Count);
                normalCount = checked(normalCount + normalsByBucket[bucket].Count);
                colorCount = checked(colorCount + colorsByBucket[bucket].Count);
            }
            var allVertices = new List<Vector3>(vertexCount);
            var allNormals = new List<Vector3>(normalCount);
            var allColors = new List<Color>(colorCount);
            var submeshTriangleLists = new List<int[]>(buckets.Count);
            var materials = new List<Material>(buckets.Count);

            foreach (int bucket in buckets)
            {
                List<Vector3> verts = verticesByBucket[bucket];
                if (verts.Count == 0) continue;

                int offset = allVertices.Count;
                allVertices.AddRange(verts);
                allNormals.AddRange(normalsByBucket[bucket]);
                allColors.AddRange(colorsByBucket[bucket]);

                int[] tris = trianglesByBucket[bucket].ToArray();
                for (int i = 0; i < tris.Length; i++) tris[i] += offset;
                submeshTriangleLists.Add(tris);
                materials.Add(GetOrCreateWaterMaterial(bucket));
            }

            // TELJESITMENY: meglevo Mesh ujrahasznositasa (ld. BuildMultiMaterialMesh doksija).
            allocationProfile?.Next("WorldGen.StaticBase.water.upload");
            MeshFilter waterMeshFilter = waterGo.GetComponent<MeshFilter>();
            Mesh mesh = waterMeshFilter.sharedMesh;
            if (mesh == null)
                mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            else
                mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(allVertices);
            mesh.SetNormals(allNormals);
            mesh.SetColors(allColors);
            mesh.subMeshCount = submeshTriangleLists.Count;
            for (int i = 0; i < submeshTriangleLists.Count; i++)
                mesh.SetTriangles(submeshTriangleLists[i], i);
            mesh.RecalculateBounds();

            waterMeshFilter.sharedMesh = mesh;
            waterGo.GetComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            allocationProfile?.Next("WorldGen.StaticBase.water.diagnostics");
            RememberDrawnSurface(waterGo, allVertices, submeshTriangleLists,
                childName == "WaterSurface" ? 3 : 4);
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

        /// <summary>ND-55: GetOrComputeCorner normal-valtozata a statikus (nem-adaptiv) alaprethez.</summary>
        private Vector3 GetOrComputeCornerNormal(
            Dictionary<(int Face, uint CornerU, uint CornerV), Vector3> normalCache,
            int face, uint cornerU, uint cornerV, int n,
            ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters)
        {
            var key = (face, cornerU, cornerV);
            if (normalCache.TryGetValue(key, out Vector3 cached))
                return cached;

            double uc = (double)cornerU / n * 2.0 - 1.0;
            double vc = (double)cornerV / n * 2.0 - 1.0;
            Vector3 nrm = ComputeCornerNormalViaFiniteDifference(face, uc, vc, seed, seeds, craters);
            normalCache[key] = nrm;
            return nrm;
        }

        private Vector3 ToDisplacedVector3(
            int face, double uc, double vc, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters)
        {
            TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);
            float displacedRadius = ComputeDisplacedRadius(x, y, z, seed, seeds, craters);
            return BodyFrameConversion.ToUnity(x, y, z) * displacedRadius;
        }

        private Vector3 ToDisplacedVector3FromBasis(
            int face, double uc, double vc, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters, in TerrainPointBasis basis)
        {
            TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);
            double elevation = ComputeElevationAtPointFromBasis(
                x, y, z, seed, seeds, craters, _adaptiveErosionTimeMyr, in basis);
            float displacedRadius = radius + (float)(DisplayElevation(elevation) * elevationScale);
            return BodyFrameConversion.ToUnity(x, y, z) * displacedRadius;
        }

        // Kicsi, ROGZITETT (nem tile-merettol fuggo) UV-eltolas a lejto-erzekeny
        // normal veges-differencia szamitasahoz - ld. ComputeCornerNormalViaFiniteDifference.
        private const double NormalSampleEpsilonUV = 1e-4;

        /// <summary>
        /// 2026-09-09 (ND-55, felhasznaloi visszajelzes: "elveszett a
        /// vizualis magassag erzete" -&gt; visszaallitva a lapos normal -&gt;
        /// "totál visszaallt a csillogas"): sem a TISZTAN lejto-erzekeny
        /// LAPOS (quadonkenti cross-product), sem a TISZTAN gomb-iranyu
        /// (magassag-vak) normal nem volt jo egyedul - az elso diszkret,
        /// tile-hataronkent ugralo spekularis foltot ad ("villamlas"), a
        /// masodik teljesen elveszti a domborzat-arnyalast. Ez a fuggveny
        /// egy HARMADIK, mindket problemat megoldo modszert ad: a normalt
        /// KIS, ROGZITETT UV-eltolassal (NEM a hivo quad SAJAT, tile-meretu
        /// sarok-tavolsagaval) veges differenciaval szamolja - mivel az
        /// eltolas fuggetlen attol, melyik (akar eltero LOD-szintu) tile
        /// kerdezi, UGYANAZON (face,uc,vc) pontra MINDIG UGYANAZT a normalt
        /// adja, tehat a szomszedos tile-ok automatikusan MEGOSZTOTT,
        /// FOLYTONOS normal-erteket kapnak a kozos sarkukon (nincs tobbe
        /// ugras), MIKOZBEN a normal tovabbra is a TENYLEGES helyi
        /// domborzat-lejtesbol szarmazik (nem egy lejtes-vak gomb-
        /// kozelitesbol), tehat a magassag-erzet is megmarad.
        ///
        /// TELJESITMENY: ez 2 TOVABBI ToDisplacedVector3 (teljes elevacio-
        /// kiertekeles) hivast jelent SAROKONKENT - ezert KIZAROLAG a mar
        /// letezo sarok-cache-eken (GetOrComputeCornerNormal /
        /// GetOrComputePersistentCornerNormal) keresztul szabad hasznalni,
        /// SOHA nem kozvetlenul quadonkent - igy a tobbletkoltseg csak az
        /// EGYEDI, meg nem cache-elt sarkakra jelentkezik, ugyanugy bekorlat-
        /// ozva, mint a mar meglevo sarok-szin cache tobbletkoltsege
        /// (ld. _persistentCornerColorCache/PrecomputeCornersInParallel -
        /// a tanulsag a Full-homodell-kiserlet teljesitmeny-regressziojabol,
        /// hogy IDEGEN, nem-cachelt, ismetlodo drage hivas soha nem kerulhet
        /// egy szazezres-hivasszamu ciklusba).
        /// </summary>
        private Vector3 ComputeCornerNormalViaFiniteDifference(
            int face, double uc, double vc, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters)
        {
            Vector3 center = ToDisplacedVector3(face, uc, vc, seed, seeds, craters);
            return ComputeCornerNormalViaFiniteDifference(face, uc, vc, seed, seeds, craters, center);
        }

        private Vector3 ComputeCornerNormalViaFiniteDifference(
            int face, double uc, double vc, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters, Vector3 center)
        {
            Vector3 alongU = ToDisplacedVector3(face, uc + NormalSampleEpsilonUV, vc, seed, seeds, craters) - center;
            Vector3 alongV = ToDisplacedVector3(face, uc, vc + NormalSampleEpsilonUV, seed, seeds, craters) - center;
            Vector3 rawNormal = Vector3.Cross(alongU, alongV);
            Vector3 normal;
            if (!(rawNormal.sqrMagnitude >= 1e-12f))
                normal = SafeSurfaceNormal(center);
            else
                normal = rawNormal.normalized;
            if (Vector3.Dot(normal, center) < 0f) normal = -normal;
            return normal;
        }

        private Vector3 ComputeCornerNormalViaFiniteDifferenceFromBasis(
            int face, double uc, double vc, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters, Vector3 center,
            in TerrainPointBasis uBasis, in TerrainPointBasis vBasis)
        {
            Vector3 alongU = ToDisplacedVector3FromBasis(
                face, uc + NormalSampleEpsilonUV, vc, seed, seeds, craters, in uBasis) - center;
            Vector3 alongV = ToDisplacedVector3FromBasis(
                face, uc, vc + NormalSampleEpsilonUV, seed, seeds, craters, in vBasis) - center;
            Vector3 rawNormal = Vector3.Cross(alongU, alongV);
            Vector3 normal;
            if (!(rawNormal.sqrMagnitude >= 1e-12f))
                normal = SafeSurfaceNormal(center);
            else
                normal = rawNormal.normalized;
            if (Vector3.Dot(normal, center) < 0f) normal = -normal;
            return normal;
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
            double elevation = ComputeElevationAtPoint(x, y, z, seed, seeds, craters, _adaptiveErosionTimeMyr);
            return radius + (float)(DisplayElevation(elevation) * elevationScale);
        }

        /// <summary>
        /// MEGJELENITESI fuggoleges tulrajzolas (terrainReliefExaggeration): a
        /// vilag-elevaciot (meter) a tengerszintre PIVOTALVA nagyitja. A
        /// tengerszinten allo pont HELYBEN marad (partvonal-megorzes), a hegyek
        /// magasabbak, az arok melyebbek lesznek - a VILAGMODELL valtozatlan.
        /// K=1 -> identitas (korabbi viselkedes).
        /// </summary>
        private double DisplayElevation(double worldElevation)
            => _adaptiveSeaLevel + (worldElevation - _adaptiveSeaLevel) * terrainReliefExaggeration;

        /// <summary>
        /// A <see cref="DisplayElevation"/> inverze: egy MAR eltolt (megjelenitett)
        /// sarok-pozicio sugarabol visszaadja a VILAG-elevaciot (meter). Ezt a
        /// szin/homerseklet/oceanicitas/vizmelyseg szamitasok hasznaljak, amiknek
        /// a VALOS elevacio kell, nem a tulrajzolt - kulonben a tulrajzolas a
        /// biome-hatarokat/vizmelyseget is eltorzitana.
        /// </summary>
        private double WorldElevationFromDisplacedRadius(double displacedMagnitude)
        {
            double displayElevation = (displacedMagnitude - radius) / elevationScale;
            double k = terrainReliefExaggeration == 0.0 ? 1.0 : terrainReliefExaggeration;
            return _adaptiveSeaLevel + (displayElevation - _adaptiveSeaLevel) / k;
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
            List<ImpactCratering.CraterRecord> craters, double erosionTimeMyr)
            => ComputeElevationAtPoint(x, y, z, seed, seeds, craters, erosionTimeMyr, out _);

        /// <summary>
        /// Ugyanaz, mint a fenti, de <paramref name="isCratered"/>-ben EGY
        /// menetben (a MAR SZUKSEGES ElevationDelta-hivas melloterme kent)
        /// visszaadja azt is, hogy a pont kráteren belul esik-e - igy a hivo
        /// (pl. ComputeTileClassification) nem jarja vegig MEGEGYSZER a
        /// kráter-listat egy kulon IsInsideAnyCrater-hivassal.
        /// </summary>
        private static double ComputeElevationAtPoint(
            double x, double y, double z, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters, double erosionTimeMyr, out bool isCratered)
        {
            TerrainPointBasis basis = TerrainPointBasis.Compute(seed, x, y, z);
            return ComputeElevationAtPointFromBasis(
                x, y, z, seed, seeds, craters, erosionTimeMyr, in basis, out isCratered);
        }

        private static double ComputeElevationAtPointFromBasis(
            double x, double y, double z, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters, double erosionTimeMyr,
            in TerrainPointBasis basis)
            => ComputeElevationAtPointFromBasis(
                x, y, z, seed, seeds, craters, erosionTimeMyr, in basis, out _);

        private static double ComputeElevationAtPointFromBasis(
            double x, double y, double z, ulong seed, (double X, double Y, double Z)[] seeds,
            List<ImpactCratering.CraterRecord> craters, double erosionTimeMyr,
            in TerrainPointBasis basis, out bool isCratered)
        {
            isCratered = false;
            basis.Evaluate(seed, seeds, out double baseElevation, out double uplift, out _);

            // M10 deep-time erozio (DeepTimeErosionGlaciation): a lemezhatar-
            // uplift-BONUSZ idovel relaxal a MEGLEVO ertekenek eqFraction-jara
            // (a hegyseg lekopik), az alap-elevacio (baseElevation) valtozatlan.
            // A szetbontas (base + uplift) BITRE UGYANAZ, mint az eredeti
            // ElevationWithBoundaryFromWarped osszege - es erosionTimeMyr=0-nal
            // az UpliftRelaxationElevation identitas (exp(0)=1), tehat a t=0
            // viselkedes VALTOZATLAN. NEM uj szimulacios matek: a MAR VERIFIKALT
            // Core-fuggvenyeket hivja (BaseElevation + BoundaryUpliftFromWarped +
            // UpliftRelaxationElevation).
            double relaxedUplift = DeepTimeErosionGlaciation.UpliftRelaxationElevation(uplift, erosionTimeMyr);
            double elevation = baseElevation + relaxedUplift;

            // M11: a pont SAJAT pozicioja alapjan szamolt becsapodas-korrekcio -
            // ugyanaz a "tiszta fuggveny a pozicioban, nem a tile-ban" elv,
            // ami a lemez-elevaciot is varratmentesse teszi ket szomszedos
            // tile kozott.
            if (craters.Count > 0)
                elevation += ImpactCratering.ElevationDelta(x, y, z, craters, out isCratered);

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
            // ND-52 (code review, 2026-09-07): a masodlagos reszlet-zaj
            // amplitudoja is bele kell szamitson a referenciaba, kulonben
            // a hozza adott +-50m (oceani szorzoval) korulbelul a legfelso
            // ~6.7%-nyi tile-t idejekorautan a szelso bucket-be szoritja,
            // finoman osszenyomva a tengerfenek szin-atmenetet.
            double referenceAmplitude = (CrustElevation.NoiseAmplitudeMeters + CrustElevation.SecondaryNoiseAmplitudeMeters) * CrustElevation.OceanicNoiseFactor;
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

        // ============================================================
        // FOLYTONOS ARNYALAS (screen-space-lod utani vizualis panasz:
        // "totál pixeles", "itt-ott reszletes, mashol homogen" - a
        // gyokerok a diszkret, tile-onkent EGYETLEN lapos kategoria-szin
        // volt, NEM a LOD/poligonszam. Az alabbi fuggvenyek UGYANAZOKAT
        // a horgony-szineket hasznaljak, mint a CategoryColor/
        // OceanRockColor/WaterBucketColor, csak sima (smoothstep/
        // exponencialis) atmenettel a kuszobok korul, diszkret bucket-
        // kerekites NELKUL - a hivo oldal ezt egyenesen VERTEX-SZINKENT
        // adja at (nem uj anyag/submesh), tehat a mar meglevo (Category,
        // Bucket) csoportositas/anyag-hatarok csak render-batch-elesi
        // reszletek maradnak, a szin maga a haromszogeken belul es a
        // szomszedos tile-ok kozott is folytonosan valtozik.
        // ============================================================

        private static readonly Color IceSheetColorContinuous = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color TundraColorContinuous = new Color(0.52f, 0.52f, 0.42f);
        private static readonly Color DesertColorContinuous = new Color(0.84f, 0.74f, 0.50f);
        private static readonly Color GrasslandColorContinuous = new Color(0.70f, 0.71f, 0.40f);
        private static readonly Color SavannaColorContinuous = new Color(0.58f, 0.58f, 0.24f);
        private static readonly Color TemperateForestColorContinuous = new Color(0.22f, 0.52f, 0.20f);
        private static readonly Color RainforestColorContinuous = new Color(0.09f, 0.35f, 0.13f);

        /// <summary>Atmenet-sav fele (Kelvin) a biome-kuszobok korul - a kuszobok kozti (15K) resnel joval kisebb, nincs atfedes.</summary>
        private const double BiomeBlendHalfWidthK = 4.0;

        /// <summary>0..1 sima (smoothstep) atmenet <paramref name="thresholdK"/> korul, [threshold-halfWidth, threshold+halfWidth] savban.</summary>
        private static double SmoothTransition01(double thresholdK, double temperatureK)
        {
            double t = (temperatureK - (thresholdK - BiomeBlendHalfWidthK)) / (2.0 * BiomeBlendHalfWidthK);
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>Simitott 0..1 atmenet ket csapadek-vagopont kozott (smoothstep).</summary>
        private static float PrecipBlend01(double precipitation, double from, double to)
        {
            if (to <= from) return precipitation > from ? 1f : 0f;
            double t = (precipitation - from) / (to - from);
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            return (float)(t * t * (3.0 - 2.0 * t));
        }

        /// <summary>
        /// A csapadek-tengely menti szin EGY homersekleti savban: szaraz ->
        /// nedves iranyban Desert -> Grassland -> (kozepes) -> Rainforest, a
        /// BiomeClassification.ComputeThresholds vagopontjai kozott lagyan
        /// atvezetve. A "kozepes" horgony a hivotol jon, mert a mersekelt
        /// savban TemperateForest, a tropusiban Savanna.
        ///
        /// A legnedvesebb szakasz szelessege a felso ket vagopont tavolsaga -
        /// nincs felso vagopont, amihez normalizalni lehetne, es ez a
        /// tavolsag ugyanabbol az eloszlasbol jon, tehat vilagfuggetlen.
        /// </summary>
        private static Color PrecipitationAxisColor(
            double precipitation, Color middle, BiomeClassification.PrecipitationThresholds th)
        {
            Color c = Color.Lerp(DesertColorContinuous, GrasslandColorContinuous,
                PrecipBlend01(precipitation, th.Arid, th.SemiArid));
            c = Color.Lerp(c, middle, PrecipBlend01(precipitation, th.SemiArid, th.Moist));
            c = Color.Lerp(c, RainforestColorContinuous,
                PrecipBlend01(precipitation, th.Moist, th.Moist + (th.Moist - th.SemiArid)));
            return c;
        }

        /// <summary>
        /// Folytonos szarazfoldi biome-szin. ND-126 ota KETDIMENZIOS: a hideg
        /// vegen (jeg/tundra) csak a homerseklet szamit - a sarkvideki "hideg
        /// sivatag" is jeg/tundra -, a vegetalt savokban viszont a CSAPADEK
        /// adja a szint, es a homerseklet csak a "kozepes" horgonyt valtja
        /// (mersekelt erdo vs szavanna).
        ///
        /// Ugyanazok a kuszobok es ugyanaz a logika, mint a
        /// BiomeClassification.Classify-ban - csak diszkret switch helyett
        /// lagyan atvezetve (I3: a kep a vilagmodellbol kovetkezik, nem egy
        /// kulon szinezesi szabalybol).
        /// </summary>
        private static Color ContinuousLandBiomeColor(
            double temperatureK, double precipitation, BiomeClassification.PrecipitationThresholds th)
        {
            Color cool = PrecipitationAxisColor(precipitation, TemperateForestColorContinuous, th);
            Color warm = PrecipitationAxisColor(precipitation, SavannaColorContinuous, th);
            Color vegetated = Color.Lerp(cool, warm,
                (float)SmoothTransition01(BiomeClassification.TemperateThresholdK, temperatureK));

            Color c = Color.Lerp(IceSheetColorContinuous, TundraColorContinuous,
                (float)SmoothTransition01(BiomeClassification.IceSheetThresholdK, temperatureK));
            c = Color.Lerp(c, vegetated,
                (float)SmoothTransition01(BiomeClassification.TundraThresholdK, temperatureK));
            return c;
        }

        /// <summary>Ugyanaz a keplet, mint OceanRockBucket+OceanRockColor egyutt, de bucket-kerekites nelkul - folytonos t.</summary>
        private static Color ContinuousOceanRockColor(double elevation)
        {
            // ND-52 (code review, 2026-09-07): a masodlagos reszlet-zaj
            // amplitudoja is bele kell szamitson a referenciaba, kulonben
            // a hozza adott +-50m (oceani szorzoval) korulbelul a legfelso
            // ~6.7%-nyi tile-t idejekorautan a szelso bucket-be szoritja,
            // finoman osszenyomva a tengerfenek szin-atmenetet.
            double referenceAmplitude = (CrustElevation.NoiseAmplitudeMeters + CrustElevation.SecondaryNoiseAmplitudeMeters) * CrustElevation.OceanicNoiseFactor;
            double normalized = referenceAmplitude > 0.0
                ? (elevation - CrustElevation.OceanicBaseMeters) / referenceAmplitude
                : 0.0;
            normalized = normalized < -1.0 ? -1.0 : (normalized > 1.0 ? 1.0 : normalized);
            double t = (normalized + 1.0) * 0.5;
            Color baseColor = new Color(0.34f, 0.33f, 0.30f);
            float brightness = Mathf.Lerp(0.82f, 1.18f, (float)t);
            return new Color(
                Mathf.Clamp01(baseColor.r * brightness),
                Mathf.Clamp01(baseColor.g * brightness),
                Mathf.Clamp01(baseColor.b * brightness),
                baseColor.a);
        }

        /// <summary>Ugyanaz a keplet, mint WaterDepthBucket+WaterBucketColor egyutt, de bucket-kerekites nelkul - folytonos t.</summary>
        private Color ContinuousWaterColor(double depthMeters)
        {
            double clampedDepth = depthMeters < 0.0 ? 0.0 : depthMeters;
            double safeScale = waterDepthScaleMeters > 1.0 ? waterDepthScaleMeters : 1.0;
            double t = 1.0 - Math.Exp(-clampedDepth / safeScale);
            return Color.Lerp(shallowWaterColor, deepWaterColor, (float)t);
        }

        /// <summary>
        /// A tenylegesen kirajzolt VERTEX-szin egy (kategoria,eleváció,
        /// homerseklet) harmasra - a River/Crater kategoriak tudatosan a
        /// REGI, lapos CategoryColor-t kapjak (kis terulet/jelolo jellegu,
        /// a folytonossag itt kevesbe kritikus) - az Ocean/SeaIce/IceSheet/
        /// Tundra/Temperate/Tropical (a felszin dontő tobbsege) kapja a
        /// folytonos szinezest.
        ///
        /// ND-58 (2026-09-09, felhasznaloi visszajelzes: a polusi tengeri
        /// jeg felett a felszin EJSZAKA is vilagosszurke maradt, ELES
        /// sokszogletes hatarokkal): a SeaIce KORABBAN a River/Crater-hez
        /// hasonloan "kis terulet/jelolo jellegunek" volt minositve, DE az
        /// ND-57 (jitterelt SeaIce/Ocean hatar) ota egesz sarki
        /// jegsapkanyi, NAGY, osszefuggo teruletet fedhet le - a regi
        /// feltetelezes mar nem all. A lapos CategoryColor+HDRP/Lit
        /// anyag (1) NEM hasznalja a surfaceAmbient-et (sajat HDRP
        /// sky-ambient lattat, ezert maradt vilagos ejszaka is), es (2)
        /// quadonkent EGYETLEN, azonos szint ad (nincs sarkonkenti
        /// interpolacio a szomszedokkal), ami az eles, sokszogletes
        /// hatarvonalat okozta. A folytonos utvonalra valtas mindkettot
        /// javitja: a VertexColorUnlit (ambient-helyes sotetedes) es a
        /// mar meglevo sarok-szin-cache (sima atmenet) automatikusan
        /// vonatkozik ra.
        /// </summary>
        // ND-126: az itt korabban allo ContinuousSurfaceColor(...) TOROLVE.
        // Halott kod volt - egyetlen hivoja sem maradt (csak a sajat
        // definicioja es egy hivatkozo komment), es a Temperate/Tropical
        // kategoriakra hivatkozott, amiket az ND-126 megszuntetett. Az elo
        // ut a ContinuousCornerColor.

        /// <summary>Ocean/SeaIce + a szarazfoldi biome-ok - a folytonos vertex-szint kapo, vertex-szin-anyagos kategoriak (ND-58).</summary>
        private static bool IsContinuousTerrainCategory(RenderCategory category) =>
            category == RenderCategory.Ocean || category == RenderCategory.SeaIce
            || category == RenderCategory.IceSheet || category == RenderCategory.Tundra
            || category == RenderCategory.Desert || category == RenderCategory.Grassland
            || category == RenderCategory.TemperateForest || category == RenderCategory.Savanna
            || category == RenderCategory.Rainforest;

        /// <summary>
        /// A folytonos felszín-szín EGY SAROKPONTRA, a mar KISZAMOLT
        /// (eltolt) sarok-pozicioból, ÚJ Core-kiertekeles NELKUL - a
        /// domborzat-eltolas (radius + elevation*elevationScale) invertalasa
        /// visszaadja az elevaciot, a pozicio iranya (normalized) pedig a
        /// hőmérséklet-szamitashoz kello egyenletvektor. Ez az "óceánon
        /// belüli pixelesség" javitasa: a korabbi verzio egy EGESZ tile-ra
        /// egyetlen (kozeppontban szamolt) szint adott, emiatt nagy, ritkan
        /// finomodo tile-oknal (pl. nyilt oceanon) tomb-szeruen blokkos
        /// maradt a kep - most a 4 sarok KULON szint kap, es a GPU
        /// interpolalja folytonosan a haromszogon belul.
        /// </summary>
        private Color ContinuousCornerColor(Vector3 displacedCornerPos, bool isOceanic, double seaLevelForColor, double axialTiltRadForColor)
        {
            if (tectonicPlateOverlay)
                return TectonicPlateColorAt(displacedCornerPos);
            if (windSpeedOverlay)
                return WindSpeedColorAt(displacedCornerPos, isOceanic, seaLevelForColor, axialTiltRadForColor);
            if (precipitationOverlay)
                return PrecipitationColorAt(displacedCornerPos);

            double elevation = WorldElevationFromDisplacedRadius(displacedCornerPos.magnitude);
            if (isOceanic)
                return ContinuousOceanRockColor(elevation);

            Vector3 dir = displacedCornerPos.normalized;
            BodyFrameConversion.ToCore(dir, out double cx, out double cy, out double cz);
            double temperatureK = TemperatureKelvinAt(cx, cy, cz, axialTiltRadForColor, isOceanic, elevation, seaLevelForColor);
            return ContinuousLandBiomeColor(
                temperatureK, PrecipitationAtCore(cx, cy, cz), _adaptiveBiomeThresholds);
        }

        /// <summary>
        /// ND-54 (2026-09-09): a SAROK-szinezes az egyszeru <see cref="Temperature.
        /// TemperatureKelvin"/>-t hasznalja, ami NEM tartalmaz jeg-albedo
        /// visszacsatolast/ocean-hopuffert (azok csak a "Full" modellben,
        /// <see cref="Temperature.TemperatureKelvinFull"/>, ld. ND-42/§28.1
        /// vannak). Sarki NYAR alatt (a tengely a Nap fele billen) a Nap SOHA
        /// nem nyugszik le a polusnal, ezert a Simple keplet a polust
        /// MELEGEBBNEK szamolhatja, mint az egyenlitot (kiszamolva: ~319K a
        /// polusnal vs ~303K az egyenlitonel a jelenlegi 23.44 fokos
        /// tengelydolessel) - ez "Tropical" (sarga) szint adhat a
        /// leghidegebbnek szant pontnak (ld. docs/06-user-verification-
        /// checklist.md 12. pont, 17. kor).
        ///
        /// KIPROBALVA, VISSZAALLITVA: a Full modellre valtas (worldSeed/
        /// tYears-fuggo <see cref="Temperature.ClimateCycleTemperatureK"/>/
        /// <see cref="Temperature.GreenhouseTemperature"/>) a bolygot
        /// TELJESEN SOTETTE tette - valoszinu ok: ezek POZICIOFUGGETLEN
        /// erteket adnak, megis MINDEN EGYES sarokra/tile-ra ujra lefutottak
        /// (2x Threefry4x64 hash + 3x SinCos hivasonkent), ami a
        /// szazezres nagysagrendu hivasszamnal (PrecomputeCornersInParallel/
        /// ComputeTileClassification) sulyos teljesitmeny-regressziot
        /// okozhatott (a build/rebuild soha nem fejezodott be rendesen -
        /// innen a "tok sotet" tunet). **NYITOTT KOVETKEZO LEPES**: a Full
        /// modell UJRA bevezetheto, ha a worldSeed/tYears-fuggo (poziciotol
        /// FUGGETLEN) tGreenhouse/tCycle tagokat Build()-enkent EGYSZER
        /// szamoljuk ki (cache-elve), es csak a poziciofuggo tRadiative/
        /// tAltitude/tWeather szamolodik ujra soronkent - ezt MEG NEM
        /// implementaltuk.
        /// </summary>
        private double TemperatureKelvinAt(
            double cx, double cy, double cz, double axialTiltRad, bool isOceanic,
            double elevation, double seaLevel)
        {
            // Az axialTiltRad parameter a korabbi hivasi felulet resze; minden
            // hivo a Build-ben beallitott _adaptiveAxialTiltRad erteket adja.
            // A cache is pontosan ebbol keszul, igy a numerikus eredmeny azonos.
            return Temperature.TemperatureKelvinFromSamples(
                cx, cy, cz, in _adaptiveDailyInsolationSamples,
                isOceanic, elevation, seaLevel);
        }

        /// <summary>
        /// ContinuousCornerColor, de az isOceanic-ot MAGABOL a sarok-
        /// pozicioból (elevacio a kalibralt _adaptiveSeaLevel-hez kepest)
        /// vezeti le, nem egy konkret tile klasszifikaciojabol - ez kell a
        /// megosztott sarok-szin-cache-hez (PrecomputeCornersInParallel),
        /// mert egy sarkot TOBB, akar eltero klasszifikaciojú tile is
        /// hasznalhat, es a sarok SAJAT (konzisztens) elevacioja a helyes
        /// forras, nem barmelyik hivo tile-e.
        /// </summary>
        private Color ContinuousCornerColorAuto(Vector3 displacedCornerPos)
        {
            double elevation = WorldElevationFromDisplacedRadius(displacedCornerPos.magnitude);
            bool isOceanic = elevation < _adaptiveSeaLevel;
            return ContinuousCornerColor(displacedCornerPos, isOceanic, _adaptiveSeaLevel, _adaptiveAxialTiltRad);
        }

        /// <summary>
        /// Ugyanaz, mint <see cref="ContinuousCornerColor"/>, de VIZ
        /// melysegehez - a vizfelszin sarka MAGA fix sugaru (a kalibralt
        /// tengerszintnel), tehat NEM abbol, hanem a MEGFELELO szarazfold-
        /// sarok (ugyanaz az UV) mar kiszamolt elevaciojabol vezetjuk le a
        /// helyi melyseget.
        /// </summary>
        private Color ContinuousWaterCornerColor(Vector3 displacedLandCornerPos, double seaLevelForColor)
        {
            // Overlay modban a vizfelszin is az overlayt mutatja (kulonben a kek
            // viz eltakarna az oceanok feletti reteget).
            if (tectonicPlateOverlay)
                return TectonicPlateColorAt(displacedLandCornerPos);
            if (windSpeedOverlay)
                return WindSpeedColorAt(displacedLandCornerPos, true, seaLevelForColor, _adaptiveAxialTiltRad);
            if (precipitationOverlay)
                return PrecipitationColorAt(displacedLandCornerPos);

            double elevation = WorldElevationFromDisplacedRadius(displacedLandCornerPos.magnitude);
            double depth = seaLevelForColor - elevation;
            return ContinuousWaterColor(depth);
        }

        /// <summary>
        /// M5 szél-overlay: egy sarokpont per-tile SZÉLSEBESSÉGE szín-rámpára
        /// képezve. A szél a MAR VERIFIKALT WorldGen.Core.Climate.WindPrecipitation.
        /// WindVector-bol jon (zonalis alap + termikus szel + Coriolis + hegy-
        /// elteres); az elevation-gradienst az EGYSZERU veges differencia adja a
        /// MEGLEVO elevation-mezobol (ComputeElevationAtPoint) - ez NEM uj
        /// szimulacios matek, csak egy letezo mezo numerikus derivaltja, ugyanaz
        /// az elv, mint a TemperatureGradientTangent-nel. Tiszta olvasas -&gt;
        /// szalbiztos a parhuzamos sarok-szin-elszamitasbol (1982).
        /// </summary>
        private Color WindSpeedColorAt(Vector3 displacedCornerPos, bool isOceanic, double seaLevelForColor, double axialTiltRadForColor)
        {
            double elevation = WorldElevationFromDisplacedRadius(displacedCornerPos.magnitude);
            Vector3 dir = displacedCornerPos.normalized;
            BodyFrameConversion.ToCore(dir, out double cx, out double cy, out double cz);

            // ND-119 (2026-09-21, felhasznaloi dontes "A"): a hegy-elteres tag
            // NULLA - PONTOSAN ugy, ahogy a vilagmodell sajat csapadek-mezoje
            // hivja (MoisturePrecipitation.Compute -> WindVector(..., 0.0, 0.0)).
            //
            // MIERT. Korabban az overlay sarkonkent kiszamolta az elevacio-
            // gradienst es atadta, a SZIMULACIO viszont soha nem hasznalta -
            // vagyis az overlay nem a modell szelet mutatta (I3/I4). Raadasul a
            // tag `maxFraction * tanh(slopeMag / 0.5)` alakja a valos, 10^4
            // nagysagrendu gradienseknel MINDIG telitesben van, tehat kizarolag
            // a gradiens IRANYATOL fuggott - az pedig erosen skalafuggo, igy a
            // kep azon mult, milyen lepteku derivalast valasztottunk. Ez zajt
            // adott, nem informaciot.
            //
            // MELLEKHATAS: a sarkonkenti elevacio-gradiens szamitasa teljesen
            // elmarad (ez volt a WindSpeedColorAt koltsegenek 89%-a).
            const double gradE = 0.0, gradN = 0.0;
            // #9: a WindVector koltsegenek a masik fele a BELSO
            // TemperatureGradientTangent volt (4 teljes Temperature.
            // TemperatureKelvin, elesben MERVE 28,4 us/sarok). Ugyanazt a
            // gradienst az ND-64 elore szamolt napi Nap-irany mintaibol
            // 5,0 us alatt megkapjuk, es a KETTO BITRE AZONOS: a
            // TemperatureKelvinFromSamples ugyanaz a keplet, csak a Nap-
            // iranyokat nem szamolja ujra, a mintak pedig PONTOSAN a
            // climateDayT/orbitalis/forgasi/_adaptiveAxialTiltRad ertekekbol
            // keszultek (ld. DailyInsolationSampleDirections.Create a Build-ben).
            TemperatureGradientTangentFromSamples(
                cx, cy, cz, isOceanic, elevation, seaLevelForColor,
                out double tempGradE, out double tempGradN);
            WindPrecipitation.WindVectorFromTemperatureGradient(
                cx, cy, cz, tempGradE, tempGradN, gradE, gradN,
                out double windEast, out double windNorth, out _, out _, out _);

            double speed = Math.Sqrt(windEast * windEast + windNorth * windNorth);
            double maxMs = windSpeedColorMaxMs <= 0.0 ? 1.0 : windSpeedColorMaxMs;
            float t = (float)Math.Min(1.0, speed / maxMs);
            return WindSpeedRamp(t);
        }

        /// <summary>
        /// dT/d(kelet), dT/d(eszak) K/radian-ban, kozponti veges differenciaval -
        /// UGYANAZ a keplet es UGYANAZ az eps, mint a
        /// <see cref="WindPrecipitation.TemperatureGradientTangent"/>-ben, de a
        /// homerseklet az ND-64 elore szamolt napi Nap-irany mintaibol jon
        /// (<see cref="TemperatureKelvinAt"/>), nem 4 friss Nap-irany-sorozatbol.
        /// Csak OLVAS -> szalbiztos a parhuzamos sarok-szin-szamitasbol.
        /// </summary>
        private void TemperatureGradientTangentFromSamples(
            double x, double y, double z, bool isOceanic, double elevation, double seaLevel,
            out double dEast, out double dNorth)
        {
            WindPrecipitation.LocalEastNorth(x, y, z,
                out double ex, out double ey, out double ez, out double nx, out double ny, out double nz);
            const double eps = WindPrecipitation.GradientEps;
            double tEP = TemperatureAtNormalized(x + ex * eps, y + ey * eps, z + ez * eps, isOceanic, elevation, seaLevel);
            double tEM = TemperatureAtNormalized(x - ex * eps, y - ey * eps, z - ez * eps, isOceanic, elevation, seaLevel);
            double tNP = TemperatureAtNormalized(x + nx * eps, y + ny * eps, z + nz * eps, isOceanic, elevation, seaLevel);
            double tNM = TemperatureAtNormalized(x - nx * eps, y - ny * eps, z - nz * eps, isOceanic, elevation, seaLevel);
            dEast = (tEP - tEM) / (2.0 * eps);
            dNorth = (tNP - tNM) / (2.0 * eps);
        }

        private double TemperatureAtNormalized(
            double px, double py, double pz, bool isOceanic, double elevation, double seaLevel)
        {
            double len = Math.Sqrt(px * px + py * py + pz * pz);
            if (len < 1e-12) { px = 0; py = 0; pz = 0; } else { px /= len; py /= len; pz /= len; }
            return TemperatureKelvinAt(px, py, pz, _adaptiveAxialTiltRad, isOceanic, elevation, seaLevel);
        }

        private double ElevationAtDir(double px, double py, double pz)
        {
            double len = Math.Sqrt(px * px + py * py + pz * pz);
            if (len < 1e-12) { px = 0; py = 0; pz = 0; } else { px /= len; py /= len; pz /= len; }
            return ComputeElevationAtPoint(px, py, pz, _adaptiveSeed, _adaptiveSeeds, _adaptiveCraters, _adaptiveErosionTimeMyr);
        }

        /// <summary>Szél-sebesség szín-rámpa: kék (szélcsend) -&gt; cián/zöld -&gt; sárga -&gt; piros (viharos).</summary>
        private static Color WindSpeedRamp(float t)
        {
            t = Mathf.Clamp01(t);
            Color c0 = new Color(0.12f, 0.20f, 0.55f); // szelcsend
            Color c1 = new Color(0.10f, 0.65f, 0.60f); // mersekelt
            Color c2 = new Color(0.90f, 0.80f, 0.20f); // eros
            Color c3 = new Color(0.85f, 0.18f, 0.12f); // viharos
            if (t < 1f / 3f) return Color.Lerp(c0, c1, t * 3f);
            if (t < 2f / 3f) return Color.Lerp(c1, c2, (t - 1f / 3f) * 3f);
            return Color.Lerp(c2, c3, (t - 2f / 3f) * 3f);
        }

        /// <summary>
        /// M5 csapadek-overlay: egy sarokpont csapadeka a REFERENCIA-szintu
        /// csapadek-mezobol (_adaptivePrecip), a sarkot tartalmazo referencia-tile
        /// szerint (TileGeometry.FromPosition) - a GPU a sarkok kozott interpolal,
        /// igy a diszkret per-tile mezo is folytonosnak latszik. Csak OLVAS
        /// (Build ota valtozatlan) -> szalbiztos a parhuzamos sarok-szinbol.
        /// </summary>
        private Color PrecipitationColorAt(Vector3 displacedCornerPos)
        {
            Vector3 dir = displacedCornerPos.normalized;
            BodyFrameConversion.ToCore(dir, out double cx, out double cy, out double cz);
            // ND-130: UGYANAZ az (interpolalt) fuggveny, amit a biome-osztalyozas
            // hasznal - kulonben az overlay mast mutatna, mint amit a felszin
            // szine tenylegesen kovet.
            double precip = PrecipitationAtCore(cx, cy, cz);
            double maxMm = precipitationColorMax <= 0.0 ? 1.0 : precipitationColorMax;
            float f = (float)Math.Min(1.0, precip / maxMm);
            return PrecipitationRamp(f);
        }

        /// <summary>Csapadek szin-rampa: arid (homok/barna) -&gt; felszaraz zold -&gt; zold -&gt; csapadekos turkiz.</summary>
        private static Color PrecipitationRamp(float t)
        {
            t = Mathf.Clamp01(t);
            Color c0 = new Color(0.80f, 0.72f, 0.45f); // arid - homok/barna
            Color c1 = new Color(0.55f, 0.62f, 0.30f); // felszaraz - fakozold
            Color c2 = new Color(0.18f, 0.55f, 0.25f); // mersekelt - zold
            Color c3 = new Color(0.10f, 0.45f, 0.48f); // csapadekos - sotet turkiz
            if (t < 1f / 3f) return Color.Lerp(c0, c1, t * 3f);
            if (t < 2f / 3f) return Color.Lerp(c1, c2, (t - 1f / 3f) * 3f);
            return Color.Lerp(c2, c3, (t - 2f / 3f) * 3f);
        }

        private Dictionary<(RenderCategory Category, int Bucket), Material> _categoryMaterials;

        private Material GetOrCreateCategoryMaterial((RenderCategory Category, int Bucket) key)
        {
            _categoryMaterials ??= new Dictionary<(RenderCategory Category, int Bucket), Material>();
            if (_categoryMaterials.TryGetValue(key, out Material existing) && existing != null)
                return existing;

            // Ocean/SeaIce/IceSheet/Tundra/Temperate/Tropical (a felszin
            // dontő tobbsege - ND-58 ota a SeaIce is ide tartozik) MOSTANTOL
            // folytonos vertex-szint kap (ContinuousSurfaceColor, AddQuad-ban
            // allitva) - az anyagnak ezert NEM szabad felulirnia egy sajat
            // lapos _BaseColor-ral, csak at kell engednie a vertex-szint
            // (CreateVertexColorMaterial). A River/Crater (kis terulet/
            // jelolo jellegu) tovabbra is a regi, lapos CategoryColor-t
            // hasznalja.
            Material mat = IsContinuousTerrainCategory(key.Category)
                ? CreateVertexColorMaterial()
                : CreateFlatColorMaterial(CategoryColor(key.Category, key.Bucket));
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
            // 2026-09-07: felhasznaloi visszajelzes szerint a jeg/folyo
            // "brutalisan csillog" - 6 kor (shader-specular, HDRP-Smoothness,
            // scene-staleness, nulla-diagnosztika, Bloom-kuszob, tobb
            // renderelesi reteg kikapcsolasa egyenkent - clouds/craters/
            // lakes+ice/starfield/gizmos/overlay-k) MIND kizarva screenshottal
            // es elo Unity-tesztekkel. Uj hipotezis: a korabbi (0.80-0.98
            // kozotti, tehat MAJDNEM TISZTA FEHER) alapszinek onmagukban,
            // BARMILYEN fenyezes NELKUL is "izzo"-nak/csillogonak
            // hathattak - ez megmagyarazna, miert volt HATASTALAN minden
            // eddigi feny/anyag/post-processing valtoztatas (egyik sem
            // erinti a nyers, tomor alapszint). Tompitva egy realisztikusabb,
            // kevesbe vakito jegszin-tartomanyra.
            RenderCategory.SeaIce => new Color(0.72f, 0.78f, 0.84f),
            RenderCategory.IceSheet => new Color(0.80f, 0.83f, 0.87f),
            RenderCategory.Tundra => new Color(0.52f, 0.52f, 0.42f),
            // ND-126: szarazfoldi biome-szinek szaraz -> nedves iranyban.
            // Homok -> szalma -> olajzold -> zold -> melyzold; a ket
            // "kozepes" osztaly (Savanna/TemperateForest) szandekosan kozeli,
            // de elkulonitheto arnyalat.
            RenderCategory.Desert => new Color(0.84f, 0.74f, 0.50f),
            RenderCategory.Grassland => new Color(0.70f, 0.71f, 0.40f),
            RenderCategory.Savanna => new Color(0.58f, 0.58f, 0.24f),
            RenderCategory.TemperateForest => new Color(0.22f, 0.52f, 0.20f),
            RenderCategory.Rainforest => new Color(0.09f, 0.35f, 0.13f),
            RenderCategory.River => new Color(0.20f, 0.55f, 0.90f), // vilagosabb kek, mint az ocean - elkulonul
            RenderCategory.Lake => new Color(0.12f, 0.42f, 0.62f), // edesviz-to: melyebb, kicsit zoldesebb kek, mint a folyo/ocean
            RenderCategory.Crater => new Color(0.45f, 0.18f, 0.10f), // sotet vorosbarna - jol elkulonul minden biome-tol
            _ => Color.magenta, // ismeretlen kategoria - szandekosan feltuno jelzes
        };

        // FELHASZNALOI VISSZAJELZES (2026-09-07, élő Unity-teszt): "a folyó és
        // a jég még mindig roppant mód csillog az űrből, távolról". Gyökérok:
        // ez a fuggveny a MEGLEVO, BE NEM ALLITOTT HDRP/Lit alap-Smoothness-t
        // (~0.5, kozepesen fenyes) hagyta - a HDRP fizikailag-korrekt, nagyon
        // eros Nap-fenyerosseg mellett ez eros, szeles spekularis "izzast" ad,
        // KULONOSEN a River/SeaIce kategoriakon (ezek EZT az anyagot hasznaljak,
        // NEM a kulon hangolt VertexColorUnlit.shader-t - ld. surfaceSpecular-
        // Strength/surfaceShininess, ami csak a folytonos Ocean/IceSheet/
        // Tundra/Temperate/Tropical felszinre vonatkozik). Javitva: alacsony,
        // rogzitett Smoothness/nulla Metallic minden ezzel az anyaggal
        // renderelt kategoriara (River/SeaIce/Crater/hatarvonal/tartalek-
        // anyagok) - egyik sem SZANDEKOLTAN csillogo felulet.
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        // TORTENET (2026-09-07): 0.08-ra csokkentve, majd diagnosztikai
        // cellal 0-ra - screenshot igazolta, hogy a csillogas EZ UTAN IS
        // valtozatlan maradt, tehat ez az anyag SOHA nem volt a valodi ok.
        // A tenyleges gyokerok a HDRP Bloom post-processing tul alacsony
        // kuszobe ES a HDRISky tul magas exposure-je volt
        // (DefaultSettingsVolumeProfile.asset - ld. docs/04-decisions.md).
        // Mindket javitas utan a felhasznalo szerint "picit jobb, de meg
        // mindig nagyon fenylik" - tehat MARADT egy ambient/reflexios
        // hozzajarulas. Vegleg 0-ra allitva (a korabbi 0.08 IS engedett
        // valamennyi HDRP indirekt specularis/tukrozodo valaszt a most mar
        // csokkentett, de nem nulla eg-fenyessegre) - a HDRP BRDF-je
        // Smoothness=0-nal elmeletileg semennyi specularis/tukrozo valaszt
        // nem ad, csak tiszta diffuz valaszt.
        private const float FlatMaterialSmoothness = 0.0f;

        private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");

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
            mat.SetFloat(SmoothnessId, FlatMaterialSmoothness);
            mat.SetFloat(MetallicId, 0f);
            // Futasidoben (Editor ShaderGUI nelkul) letrehozott HDRP anyagnal
            // nem garantalt, hogy az Emissive Color alapertelmezetten fekete -
            // explicit nullazva, nehogy egy nem-szandekolt emisszios
            // hozzajarulas maradjon (biztonsagi intezkedes, nem bizonyitott ok).
            mat.SetColor(EmissiveColorId, Color.black);
            return mat;
        }

        private Material _vertexColorMaterial;

        /// <summary>
        /// A folytonos (Ocean/IceSheet/Tundra/Temperate/Tropical + víz)
        /// felszín anyaga - a "Shaders/VertexColorUnlit.shader" NINCS élő
        /// Unity-tesztelve ebből a fejlesztői környezetből (ld. a shader
        /// fejlécét is): ha nem található vagy hibásan fordul, a régi,
        /// lapos HDRP/Lit anyagra esik vissza (semleges szürke), hogy a
        /// bolygó SOSE tűnjön el/legyen rózsaszín, csak a folytonos
        /// árnyalás maradjon el, amíg ki nem javítjuk.
        /// </summary>
        private Material CreateVertexColorMaterial()
        {
            if (_vertexColorMaterial != null)
                return _vertexColorMaterial;

            Shader shader = Shader.Find("WorldGen/VertexColorUnlit");
            if (shader == null)
            {
                Debug.LogWarning("PlanetGridMesh: a \"WorldGen/VertexColorUnlit\" shader nem található - " +
                    "a folytonos árnyalás nem lesz látható, semleges szürke HDRP/Lit anyagra esik vissza.");
                _vertexColorMaterial = CreateFlatColorMaterial(new Color(0.5f, 0.5f, 0.5f));
                return _vertexColorMaterial;
            }
            _vertexColorMaterial = new Material(shader);
            UpdateSurfaceLightingUniforms(); // azonnal ertelmes fenybeallitas (ne varjunk a kovetkezo frame-re)
            return _vertexColorMaterial;
        }

        private Material _waterSurfaceMaterial;

        // FELHASZNALOI VISSZAJELZES (2026-09-07): "a folyo es a jeg brutalisan
        // csillog" - 6 kor fenyezesi/anyag/post-processing valtoztatas es
        // szisztematikus reteg-kizaras (felhok/krateretek/tavak+jeg/csillagok/
        // gizmo-k/Nap/overlay-k mind kikapcsolva egyenkent) sem oldotta meg
        // teljesen. DONTO NYOM: a felhasznalo eszrevette, hogy "Tavak+jeg"
        // KIKAPCSOLASAVAL a folt MEG FENYESEBB lesz - ez azert van, mert
        // ilyenkor az addig jeggel fedett terulet sima, LIKVID viz-feluletre
        // valt (BuildWaterSurface), ami egy TOKELETESEN SIMA gombhej a
        // kalibralt tengerszint sugaranal - a bumpy/durva terep-mesh-hez
        // kepest ENNEK koherens normalja MISSZI, SZELESEBB "tukor-glintet"
        // ad ugyanolyan specular-parameterek mellett (ld. valodi
        // muholdkepeken lathato "sun glint" jelenseg oceanon). A viz
        // KORABBAN a szarazfolddel MEGOSZTOTT `_vertexColorMaterial`-t
        // hasznalta (`GetOrCreateWaterMaterial` -> `CreateVertexColorMaterial`)
        // - emiatt a viz UGYANAZT a specular-erosseget kapta, mint a
        // szikla/tundra, holott egy sima gombhejon UGYANAZ a specular-ertek
        // sokkal koncentraltabb/fenyesebb hatast ad. Kulon anyag, kulon,
        // JOVAL alacsonyabb specular-parameterekkel.
        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("Spekuláris erősség a vízfelszínen (KÜLÖN a száraz felszíntől, " +
                 "2026-09-07 óta) - a sima, gömbhéj-vízfelszín koherens normálja miatt " +
                 "ugyanaz a specular-érték sokkal koncentráltabb/fényesebb csillanást ad, " +
                 "mint a durva terepen, ezért ez jóval alacsonyabb, mint a " +
                 "surfaceSpecularStrength.")]
        private float waterSpecularStrength = 0.02f;
        [SerializeField]
        [Range(1f, 128f)]
        private float waterShininess = 8f;

        /// <summary>
        /// A vízfelszín (BuildWaterSurface: óceán+tó) SAJÁT, a szárazföldtől
        /// (`_vertexColorMaterial`) KÜLÖN anyaga - ugyanaz a shader
        /// (VertexColorUnlit), de saját, alacsonyabb specular-paraméterekkel,
        /// mert a sima gömbhéj-geometria koherens normálja miatt a víz sokkal
        /// erősebb, koncentráltabb "napcsillanást" ad ugyanolyan specular-
        /// erősség mellett, mint a durva, bumpy szárazföld/jég-mesh.
        /// </summary>
        private Material CreateWaterSurfaceMaterial()
        {
            if (_waterSurfaceMaterial != null)
                return _waterSurfaceMaterial;

            Shader shader = Shader.Find("WorldGen/VertexColorUnlit");
            if (shader == null)
            {
                _waterSurfaceMaterial = CreateFlatColorMaterial(new Color(0.2f, 0.3f, 0.4f));
                return _waterSurfaceMaterial;
            }
            _waterSurfaceMaterial = new Material(shader);
            UpdateSurfaceLightingUniforms();
            return _waterSurfaceMaterial;
        }

        /// <summary>
        /// M6/M13 felhő-réteg anyaga - a "Shaders/CloudUnlit.shader" (a
        /// VertexColorUnlit átlátszó, spekuláris nélküli változata) SEM ÉLŐ
        /// Unity-tesztelt ebből a fejlesztői környezetből. Ha nem található,
        /// egy fix, félig-átlátszó HDRP/Lit anyagra esik vissza (a felhőréteg
        /// ekkor egyenletes, nem csapadék-vezérelt - jól látható jelzés,
        /// hogy a shader hiányzik, nem hogy a modell hibás).
        /// </summary>
        private Material CreateCloudMaterial()
        {
            Shader shader = Shader.Find("WorldGen/CloudUnlit");
            if (shader == null)
            {
                Debug.LogWarning("PlanetGridMesh: a \"WorldGen/CloudUnlit\" shader nem található - " +
                    "a felhőréteg egyenletes, nem csapadék-vezérelt anyagra esik vissza.");
                return CreateFlatColorMaterial(new Color(1f, 1f, 1f, 0.5f));
            }
            return new Material(shader);
        }

        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int AmbientId = Shader.PropertyToID("_Ambient");
        private static readonly int SpecStrengthId = Shader.PropertyToID("_SpecStrength");
        private static readonly int ShininessId = Shader.PropertyToID("_Shininess");
        private Light _cachedSurfaceSun;

        /// <summary>
        /// A folytonos felszin-shader (VertexColorUnlit->Lit) Nap-uniformjainak
        /// frissitese a jelenet Directional Lightjabol. Minden frame (LateUpdate)
        /// fut, hogy a mozgo Nap (SunController) arnyekolasat/csillanas kovesse.
        /// Explicit uniform, mert a HDRP legacy feny-valtozoi SRP alatt nem
        /// garantaltan toltodnek ki (ld. a shader fejleceben).
        /// </summary>
        // IDEIGLENES DIAGNOSZTIKAI KAPCSOLO (2026-09-07, felhasznaloi jovahagyassal):
        // a felhasznalo kikapcsolta a Directional Light-ot elo Unity-tesztben,
        // es SEMMI NEM VALTOZOTT - ez kizarja az OSSZES eddigi feny-fuggo
        // hipotezist (specular, diffuz, bloom). Gyanu: a sajat shaderunk
        // (VertexColorUnlit) NEM Unity beepitett feny-rendszeret hasznalja,
        // hanem a C# minden frame-ben KEZZEL olvassa ki a Light.color/
        // transform.forward erteket es tolja be uniformkent - egy KIKAPCSOLT
        // GameObject komponens-ertekei NEM nullazodnak, tehat a shader
        // valoszinuleg MINDIG "teljesen megvilagitott" allapotot szamol,
        // fuggetlenul attol, hogy a feny GameObject aktiv-e. Ez a kapcsolo
        // IGAZ ertek eseten EXPLICIT (0,0,0) Nap-szint es 0 ambienst kenyszerit
        // - ha a polusok EZUTAN IS fenylenek, az VEGLEGESEN bizonyitja, hogy a
        // jelenseg NEM fenyezesi eredetu (alapszin/emisszio/mas forras kell).
        // Erdemes false-ra allitani/eltavolitani, amint a diagnozis lezarult.
        [SerializeField]
        private bool diagForceZeroLighting = false;

        private void UpdateSurfaceLightingUniforms()
        {
            if (_vertexColorMaterial == null && _cloudMaterial == null && _waterSurfaceMaterial == null)
                return;

            Light sun = surfaceSunLight != null ? surfaceSunLight
                : (_cachedSurfaceSun != null ? _cachedSurfaceSun : FindSurfaceSun());
            _cachedSurfaceSun = sun;

            // A Directional Light a sajat +Z (forward) mentén sugaroz, tehat a
            // felszintol a Nap fele mutato irany a -forward (ld. SunController).
            Vector3 toSun = sun != null ? -sun.transform.forward : new Vector3(0.4f, 0.6f, 0.7f).normalized;
            Color sunColor = diagForceZeroLighting ? Color.black : (sun != null ? sun.color : Color.white);
            float effectiveAmbient = diagForceZeroLighting ? 0f : surfaceAmbient;

            if (_vertexColorMaterial != null)
            {
                _vertexColorMaterial.SetVector(SunDirId, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
                _vertexColorMaterial.SetColor(SunColorId, sunColor);
                _vertexColorMaterial.SetFloat(AmbientId, effectiveAmbient);
                _vertexColorMaterial.SetFloat(SpecStrengthId, surfaceSpecularStrength);
                _vertexColorMaterial.SetFloat(ShininessId, surfaceShininess);
            }
            // A felho-anyagnak (CloudUnlit) nincs spekularis/shininess property-je
            // (nem indokolt, hogy egy felho csillanjon) - csak Nap-irany/szin/ambiens.
            // HIBA JAVITVA (2026-09-07): a diagForceZeroLighting korabban NEM
            // erintette a cloudAmbient-et (kulon mezo a surfaceAmbient-tol) -
            // igy egy suru/feher felho-terulet MEG A "fekete Nap" teszt alatt
            // is 0.55-os alap-fenyesseget kaphatott, ami magyarazhatta a
            // polusok/szigetek kozeleben megmaradt fenyes foltokat, ha ott
            // suru a csapadek.
            if (_cloudMaterial != null)
            {
                _cloudMaterial.SetVector(SunDirId, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
                _cloudMaterial.SetColor(SunColorId, sunColor);
                _cloudMaterial.SetFloat(AmbientId, diagForceZeroLighting ? 0f : cloudAmbient);
            }
            // A vizfelszin SAJAT, alacsonyabb specular-parametereket kap - ld.
            // CreateWaterSurfaceMaterial doksija (a sima gombhej-geometria
            // koherens normalja miatt ugyanaz a specular-ertek sokkal
            // koncentraltabb csillanast ad, mint a durva szarazfoldon).
            if (_waterSurfaceMaterial != null)
            {
                _waterSurfaceMaterial.SetVector(SunDirId, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
                _waterSurfaceMaterial.SetColor(SunColorId, sunColor);
                _waterSurfaceMaterial.SetFloat(AmbientId, effectiveAmbient);
                _waterSurfaceMaterial.SetFloat(SpecStrengthId, waterSpecularStrength);
                _waterSurfaceMaterial.SetFloat(ShininessId, waterShininess);
            }
        }

        private Light FindSurfaceSun()
        {
            // A TENYLEGES Nap az, amit az ido forgat: a SunController-t hordozo
            // Light. Ezt kell olvasni, kulonben egy masik (statikus) Directional
            // Lightra eshetnenk, es a folytonos felszin arnyekolasa NEM kovetne a
            // mozgo Napot (felhasznaloi eszrevetel 2026-09-05).
            SunController sc = FindFirstObjectByType<SunController>();
            if (sc != null)
            {
                Light scLight = sc.GetComponent<Light>();
                if (scLight != null)
                    return scLight;
            }
            if (RenderSettings.sun != null)
                return RenderSettings.sun;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                    return l;
            return null;
        }

        private void LateUpdate()
        {
            // A folytonos felszin valos ideju vilagitasa a mozgo Napot koveti.
            // Olcso (egyetlen megosztott anyag nehany uniformja), minden frame.
            UpdateSurfaceLightingUniforms();

            // ND-49 kiegeszites: a hatter-szalon futo folyo-finomitas
            // eredmenyenek fo-szalu atvetele, amint elkeszult - ld.
            // TryApplyCompletedRiverRefinement doksija.
            TryApplyCompletedRiverRefinement();

            // Felhasznaloi visszajelzes (2026-09-06): a periodikus felho-
            // sodrodas frame-hitchet okozott - a hatter-szalon futo
            // ujraszamitas eredmenyenek fo-szalu atvetele, ld.
            // TryApplyCompletedCloudRebuild doksija.
            TryApplyCompletedCloudRebuild();
            TickDrawnTileDiagnostics();
        }

        /// <summary>
        /// Ha a háttér-szálon futó <see cref="RiverPathTracing.
        /// BuildContinuousRiverNetworkFromSources"/>-számítás (ld. Build())
        /// elkészült, itt (fő szál) alkalmazzuk: eltároljuk az eredményt és
        /// újraépítjük a folyó-mesh-t a finomított nyomvonallal. Ha
        /// időközben egy ÚJABB Build() futott le (más generáció), az
        /// eredményt ELDOBJUK - az már egy régi világállapotra vonatkozna.
        /// </summary>
        private void TryApplyCompletedRiverRefinement()
        {
            if (_riverRefinementTask == null || !_riverRefinementTask.IsCompleted)
                return;

            System.Threading.Tasks.Task<List<RiverPathTracing.ContinuousRiverPath>> task = _riverRefinementTask;
            _riverRefinementTask = null;
            if (_riverRefinementCancellation != null)
            {
                _riverRefinementCancellation.Dispose();
                _riverRefinementCancellation = null;
            }

            if (task.IsCanceled)
                return;

            if (task.IsFaulted)
            {
                Debug.LogWarning(
                    "PlanetGridMesh: a háttér-szálon futó folyó-finomítás hibával zárult - " +
                    $"a folyóvonalak nem jelennek meg. Hiba: {task.Exception?.GetBaseException()}");
                return;
            }

            if (_pendingRiverRefinementGeneration != _riverRefinementGeneration)
                return; // elavult - egy ujabb Build() mar futott, amig ez a task dolgozott

            _adaptiveRefinedRiverPaths = task.Result;
            _adaptiveRiverDischargeWeights = RiverPathTracing.ComputeDischargeWeights(_adaptiveRefinedRiverPaths);
            PerfLog($"[ND-132 river ready] generation={_pendingRiverRefinementGeneration} elapsedMs={_riverRefinementStopwatch?.Elapsed.TotalMilliseconds:F1} rivers={_adaptiveRefinedRiverPaths.Count}");
            WarnIfAnyRiverHitMaxSteps(_adaptiveRefinedRiverPaths);
            var riverMeshStopwatch = Stopwatch.StartNew();
            BuildRiverNetwork();
            PerfLog($"[ND-132 river mesh] elapsedMs={riverMeshStopwatch.Elapsed.TotalMilliseconds:F1}");
        }

        /// <summary>
        /// Code-review-ban feltart hianyossag potlasa (2026-09-06): a
        /// `TerminationReason.MaxSteps` (a 4millios biztonsagi korlat, NEM
        /// termeszetes Ocean/Pit vegallapot) korabban NEMAN elnyelodott -
        /// a kirajzolt vonal egyszeruen megallt a levegoben, kozeppen a
        /// terepen, minden jelzes nelkul. Ez legalabb egy fejlesztoi
        /// figyelmeztetest ad, hogy a jelensegre fel lehessen figyelni (pl.
        /// ha egy adott vilag-seedre rendszeresen elofordul, az a hurok-
        /// vedelem vagy az escape-koltsegvetes tovabbi hangolasat jelezhetne).
        /// </summary>
        private static void WarnIfAnyRiverHitMaxSteps(List<RiverPathTracing.ContinuousRiverPath> rivers)
        {
            if (rivers == null) return;
            for (int i = 0; i < rivers.Count; i++)
            {
                if (rivers[i].Termination == RiverPathTracing.TerminationReason.MaxSteps)
                {
                    Debug.LogWarning(
                        $"PlanetGridMesh: a(z) {i}. folyó folytonos finomítása a lépésszám-biztonsági " +
                        "korlátig futott (MaxSteps) - NEM ért el természetes Ocean/Pit végállapotot. " +
                        "A kirajzolt vonal a terep közepén szakad meg. Ritka, elfajult eset - ha " +
                        "rendszeresen előfordul, a hurok-védelem vagy az escape-paraméterek " +
                        "hangolása indokolt lehet.");
                }
            }
        }
    }
}
