using System;
using System.Collections.Generic;
using System.Diagnostics;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// M9 (docs/05-milestones.md §9.1) adaptiv, kamera-vezerelt kvadfa-LOD
    /// kivalasztasa a MAR letezo cubed-sphere racson (TileId.Parent()/
    /// Child(), TileNeighbors) - nincs uj koordinata-rendszer, a kvadfa a
    /// meglevo Morton-hierarchiara ul ra (ld. TileId doksi).
    ///
    /// TISZTA, MOTORFUGGETLEN logika: nincs UnityEngine referencia (ld. az
    /// asmdef noEngineReferences=true beallitasat), csak a WorldGen.Core
    /// Grid-modulra epul - ezert plain NUnit/dotnet alol is tesztelheto,
    /// Unity Editor futtatasa nelkul is (bar a projektben ELERHETO a Unity
    /// Editor, es EditMode teszt is keszult hozza, ld. Assets/Tests/EditMode/Lod).
    ///
    /// KOORDINATA-KONVENCIO: minden pozicio-parameter a WorldGen.Core sajat
    /// "test-keret" konvenciojaban ertendo (ugyanaz, mint a TileGeometry-e),
    /// NEM a Unity-re csavart (BodyFrameConversion utani) konvencioban - a
    /// hivo (PlanetGridMesh) felelossege az atvaltas. Igy ez a modul semmit
    /// nem tud a tengelycserelrol, csak sima haromdimenzios tavolsagokkal
    /// szamol - a hivo altal atadott `planetRadius`-szal skalazva.
    ///
    /// HISZTEREZIS ES REPRODUKALHATOSAG - NYITOTT TERVEZESI DONTES,
    /// EXPLICIT DOKUMENTALVA (mert a ket kovetelmeny latszolag ellentmond
    /// egymasnak, ld. docs/05-milestones.md §9.1 "Reprodukalhatosag" sora):
    ///
    /// A hiszterezishez (K_merge &gt; K_split, hogy egy csomopont a kuszob
    /// ket oldalan ne pattogjon frame-rol frame-re) SZUKSEG van az ELOZO
    /// keret aktiv-level allapotara. Ez elsore ellentetesnek tunik azzal,
    /// hogy "ugyanaz a kamera-pozicio -&gt; mindig ugyanaz a cut", FUGGETLENUL
    /// az odajutas utjatol. A feloldas: az elozo keret cut-ja EXPLICIT, a
    /// hivo altal atadott BEMENETI PARAMETER (nem rejtett, modul-szintu
    /// mutable allapot, ld. I2 - a fuggveny maga innentol is tiszta: nincs
    /// static mezo, determinisztikus a KET bemenetbol). A "reprodukalhatosag"
    /// property emiatt pontosan igy ertendo es tesztelt (ld.
    /// AdaptiveQuadTreeTests):
    ///   1. (kamera-pozicio, elozo cut) par -&gt; MINDIG bitre ugyanaz az uj cut.
    ///   2. HIDEG inditas (elozo cut = ures/null) egy adott poziciora is
    ///      MINDIG bitre ugyanazt a cut-ot adja.
    /// Egy adott vegso poziciora, KULONBOZO elozmennyel (pl. felulrol vagy
    /// alulrol kozelitve a hiszterezis-savot), a fuggveny elvben KETFELE, de
    /// mindket iranyban STABIL (tovabb NEM oszcillalo) cut-ot adhat a
    /// K_split..K_merge savon belul - ez SZANDEKOS, maga a hiszterezis
    /// celja, es a milestone "monoton kamera-ut" tesztje pontosan ezt a
    /// stabilitast varja el, NEM az elozmenytol fuggetlen egyertekuseget.
    /// </summary>
    public static class AdaptiveQuadTree
    {
        public const int DefaultBaseLevel = 2;
        public const int DefaultMaxLevel = 11;

        /// <summary>
        /// FAZIS 1 (ND-47, 2026-09-03) - A BEJARAS GYOKERSZINTJENEK
        /// LEVALASZTASA A STATIKUS BASE-ROL.
        ///
        /// PROBLEMA (ld. history/RETROSPECTIVE-2026-09-02-adaptive-lod-saga.md
        /// es a jelen ND-47 elemzes): a korabbi terv egyetlen szintet
        /// (`adaptiveBaseLevel`, elesben 8) hasznalt HAROM szerepre egyszerre:
        /// (1) statikus tavoli-reszletesseg, (2) a kvadfa-bejaras gyokerhalmaza,
        /// (3) a mesh alapja. Emiatt a per-frame `SelectCut` a base-szintu
        /// gyokerek SZAMAVAL aranyos (6*4^8 = 393216) volt, es a `maxLeafCount`
        /// biztonsagi korlat (200000) MAR A BASE-FELSOROLAS KOZBEN bevagott -
        /// a `leafCounter` minden meglatogatott csomopontot szamol, igy a
        /// 393216 gyoker garantaltan atlepte a korlatot, mielott barmi
        /// erdemi finomodas tortent volna. A `Parallel.For` nemdeterminisztikus
        /// sorrendje miatt a keret-kimerulesig eljutott ~51% VELETLENSZERU,
        /// foltszeru finomodast adott (a felhasznalo altal "esetleges"-kent
        /// jelzett tunet).
        ///
        /// MEGOLDAS: a bejaras egy ALACSONY `traversalRootLevel`-rol indul
        /// (keves gyoker), es CSAK oda ereszkedik le, ahol a `staticBaseLevel`
        /// FOLE kell finomitani - a sík (tavoli) regioban a rekurzio azonnal
        /// leall (a csomopont vetitett szogsugara mar a durva szinten a kuszob
        /// alatt van), es SEMMIT nem emittal (azt a statikus reteg fedi). Igy a
        /// meglatogatott csomopontok szama a LATHATO, finomitando regio
        /// meretevel aranyos, NEM a bolygoeval - ez a screen-space LOD lenyege.
        ///
        /// A ket parameter VISSZAFELE-KOMPATIBILIS: a sentinel default-ok
        /// (`traversalRootLevel = DefaultTraversalRootLevelSentinel`,
        /// `staticBaseLevel = NoStaticBaseLevel`) a REGI viselkedest adjak
        /// (a bejaras a `baseLevel`-rol indul, es TELJES particiot emittal),
        /// tehat a meglevo hivok/egyseg-tesztek valtozatlanul mukodnek.
        /// </summary>
        public const int DefaultTraversalRootLevelSentinel = -1;

        /// <summary>
        /// A <c>staticBaseLevel</c> sentinelje: nincs statikus reteg - a
        /// cut TELJES particio (minden megallo csomopont emittalodik, level
        /// szerinti also korlat nelkul). Ez a visszafele-kompatibilis
        /// alapertelmezes. Ha &gt;=0, a bejaras CSAK a `level &gt; staticBaseLevel`
        /// leveleket emittalja (a tobbit a statikus reteg fedi).
        /// </summary>
        public const int NoStaticBaseLevel = -1;

        /// <summary>
        /// KEPERNYO-VETITESI LOD-METRIKA (screen-space-lod branch, felvaltja a
        /// korabbi "distance &lt; factor*rTile" tavolsag/meret-aranyt): a
        /// split/merge dontes a csomopont TENYLEGES VETITETT SZOGSUGARABOL
        /// (radian, ld. Visit() - atan2(rTile,distance), ugyanaz a keplet, mint
        /// az IsWithinViewCone lathatosag-teszteben) szuletik, nem egy nyers
        /// tavolsag-arnybol.
        ///
        /// MIERT: a regi metrika egyetlen, MINDEN szinten egyformán ható
        /// aranyszamot hasznalt - ez azt jelentette, hogy egy adott
        /// splitFactor-hoz tartozo "kivaltasi tavolsag" LINEARISAN skalazodott
        /// a tile meretevel (rTile), ami viszont szintenkent kb. felezodik.
        /// Ha a kamera minimalis (felszinhez legkozelebbi) tavolsaga FIX (pl.
        /// PlanetOrbitCamera minDistance-e), a mely szintek (pl. 18-20)
        /// GYAKORLATILAG SOSEM ertek el, mert a kivaltasi tavolsaguk sokkal
        /// kisebb lenne, mint ameddig a kamera egyaltalan kozelithet - MIG
        /// ugyanaz a splitFactor a durva szinteken (kicsi baseLevel) mar
        /// tul korán, tul messziről kivaltja a finomodast (a felhasznaloi
        /// visszajelzesek szerint tobbszor ismetlodo "kozepes zoomnal
        /// robbanasszeru tile-szam" problema forrasa).
        ///
        /// A szogsugar-alapu metrika ELTUNTETI ezt a feszultseget: egy adott
        /// szogsugar-kuszob MINDEN szinten UGYANAKKORA KEPERNYO-MERETET
        /// (radian, tehat kozvetlenul pixelre valthato a kamera FOV/
        /// felbontasabol - ld. PlanetGridMesh.RecomputeCutAndRebuildAdaptiveMesh)
        /// jelent, fuggetlenul a tavolsagtol es a tile abszolut meretetol -
        /// ez a "textbook correct" LOD-kriterium (screen-space error).
        ///
        /// Hiszterezis (K_merge &lt; K_split, FORDITOTT irany a regi
        /// tavolsag-alapu valtozathoz kepest, mert itt a KISEBB szogkuszob
        /// jelenti "meg mindig felbontast igenyel"-t - ld. Visit()).
        /// </summary>
        public const double DefaultSplitThresholdRadians = 0.03;
        public const double DefaultMergeThresholdRadians = DefaultSplitThresholdRadians / 1.5;

        /// <summary>
        /// Nincs latokup-szures (a teljes gomb "lathatonak" szamit) - ez az
        /// alapertelmezes a visszafele-kompatibilitashoz (pl. a meglevo
        /// egyseg-tesztek, amik nem adnak meg kamera-iranyt).
        /// </summary>
        public const double NoCullingHalfFovRadians = Math.PI;

        /// <summary>
        /// BIZTONSAGI KORLAT (felhasznaloi katasztrofa-jelentes alapjan,
        /// 2026-09-01: egy TENYLEGES cut 6 349 914 tile-t tartalmazott,
        /// ~1.9-60+ masodperces ujraepitest okozva). EREDETILEG az
        /// atan2(rTile,distance) szogsugar-metrika nezesi-szog-vaksaga
        /// (ld. ND-46, MinUsefulCosGrazing) miatt ez a korlat volt az
        /// EGYETLEN vedelem tulzott finomodas ellen - AZ ND-46 JAVITAS
        /// (2026-09-02) UTAN a Visit() sajat maga korlatozza a nezesi-szog-
        /// korrekcio hatasat (MinUsefulCosGrazing also korlat + pi/2-es
        /// felso korlat), tehat ez a szamlalo-alapu korlat mostantol
        /// RETEGZETT, MASODIK vonalu vedelem (barmilyen MAS, elore nem
        /// latott geometriai szeleset esetre), nem az elsodleges fek.
        /// Amint aktivalodik, a tovabbi finomodas egyszeruen leall (a
        /// csomopont "durvabb" marad annal, mint amit a tiszta metrika
        /// kérne) - ez MINDIG jobb, mint egy tobb masodperces/vegtelen
        /// ujraepites.
        /// </summary>
        public const int DefaultMaxLeafCount = 200_000;

        /// <summary>
        /// A raszter-igény szorzója (ld. <see cref="RenderBudgetForViewport"/>).
        ///
        /// A 3,0 a felhasználó 2026-09-18-i naplójából VISSZASZÁMOLT érték.
        /// A rajzolt tile-ok ott 8000 levéllel ekkorák voltak a 8 px-es célhoz
        /// képest, és ennyi levél kellene a célhoz (a levélszám a lineáris
        /// tile-méret NÉGYZETÉVEL skálázódik):
        ///
        ///   zoom-sáv   mért méret     szükséges levél
        ///   30-100     1,40-1,69x     16 000 - 23 000
        ///   10-30      2,13-2,32x     36 000 - 43 000
        ///   3-10       3,76-4,08x    112 000 - 136 000
        ///
        /// A csupasz raszter-igény 1196x710 és 8 px mellett 13 269, tehát a
        /// 3,0-es szorzó (39 807 levél) a két LEGGYAKRABBAN hasznalt savot
        /// (kontinens- es regio-nezet) celra viszi. A legkozelebbi zoom ~8-10x
        /// szorzot kerne; azt a felso korlat tudatosan nem engedi.
        ///
        /// Az eredeti 1,5 a horizont-vagas ELOTTI koltsegekkel szuletett; azota
        /// a selection 7-8x olcsobb, ezert fert bele a nagyobb szorzo.
        /// </summary>
        public const double DefaultRenderBudgetOverhead = 3.0;

        /// <summary>A korábbi kézi érték; kis ablaknál sem megyünk ez alá.</summary>
        public const int MinimumRenderBudget = 8_000;

        /// <summary>
        /// Felső korlát. Két, egymástól független MÉRT ok tartja itt:
        ///
        /// 1. KÖLTSÉG-PARITÁS: 48 000 levélnél a worker-költség
        ///    (selection+balance, meleg cache) 150-158 ms - pontosan annyi,
        ///    amennyi a felhasználónak a változtatások ELŐTT volt 8000 levéllel
        ///    (a naplóban selection p50 = 147 ms). Vagyis hatszoros részletesség
        ///    a korábbi költségen; efölött már többe kerülne, mint eddig.
        /// 2. BALANCE-SZAKADÉK: a kiegyensúlyozás akkor dolgozik érdemben, ha a
        ///    metrika a budget ALATT telítődik, mert akkor marad fejtér a
        ///    felosztásoknak. Mérve (D=103): a budget megkötésekor 0 ms (ND-116
        ///    korai kilépés), de 96 000-es budgetnél - ahol a cut 66 900-nál
        ///    telítődik - 4 kör, 108 felosztás, 320 ms, mert a fixpont-ciklus
        ///    minden körben végigszkenneli a teljes cutot (~3 ms felosztásonként).
        ///    A plafon EMELÉSE tehát nem olcsóbb, hanem drágább: nagyobb
        ///    budgetnél nagyobb eséllyel kerülünk a "budget > telítődés"
        ///    tartományba. Amíg a ciklus teljes újraszkennelés helyett nem
        ///    munkalistával dolgozik, a 48 000 marad.
        /// </summary>
        public const int MaximumRenderBudget = 48_000;

        /// <summary>
        /// A vágás levél-költségvetése a VIEWPORTBÓL számolva, nem kézi számból.
        ///
        /// MIÉRT: a korábbi fix 8000-es érték egy 2026-09-13-as mérés LINEÁRIS
        /// extrapolációjából született ("8000 levél ~ 400-500 ms kérés-idő"), és
        /// a 2026-09-18-i mintavételező napló szerint a rajzolt tile-ok emiatt
        /// 1,4-4,1-szer nagyobbak a 8 px-es célnál: 68 vágásból 44 pontosan
        /// 7998-8000 levélnél telítődött, a kiugró tile-ok 79/95 arányban
        /// <c>stop=leaf-budget</c> okkal álltak meg.
        ///
        /// Az extrapoláció téves volt: a bejárás költségét a LÁTOTT FELÜLET
        /// hajtja, nem a budget. MÉRVE (2026-09-19, meleg metrika-cache, azonos
        /// nézet D=103-nál, csak a budget változik, a horizont-vágás UTÁN):
        ///
        ///   budget   levelek   selection   balance   összesen   (hideg cache)
        ///     8 000    7 998       8 ms       9 ms      17 ms        34 ms
        ///    20 000   19 998      51 ms      22 ms      73 ms        88 ms
        ///    32 000   31 998      72 ms      36 ms     107 ms       143 ms
        ///    48 000   48 000      94 ms      56 ms     150 ms       224 ms
        ///    64 000   63 999     116 ms      77 ms     193 ms       315 ms
        ///    96 000   66 900*     73 ms     327 ms     400 ms       558 ms
        ///
        /// (*) a levélszám itt telítődik - ennyit kér összesen a metrika ebben a
        /// nézetben -, és a balance a <c>maxLeafCount*3</c>-as nem szigorú
        /// korláttól robban meg, nem a tényleges levelektől. Ld.
        /// <see cref="MaximumRenderBudget"/>.
        ///
        /// A főszálat mindez nem érinti: a feltöltés az ND-85 szerint
        /// keretenként <c>TerrainUploadSliceMs = 2</c> ms-ra szeletelt, tehát a
        /// nagyobb mesh több staging-frame, NEM nagyobb akadás. A nagyobb budget
        /// ára latencia.
        ///
        /// A KÉPLET: a képernyő befér-e cél-méretű tile-okkal. 1196x710 és 8 px
        /// mellett 849 160 / 64 = 13 269 levél a csupasz igény, a 3,0-es
        /// szorzóval 39 807. A szorzó fedi a cubed-sphere ~1,3-1,4x-es
        /// tile-terület-szórását (ND-24), a perspektivikus rövidülést és a
        /// restricted-balance ráhagyását - az értéke viszont nem elméletből,
        /// hanem a naplóból VISSZASZÁMOLT levélszükségletből jön, ld.
        /// <see cref="DefaultRenderBudgetOverhead"/>.
        ///
        /// EZ NEM oldja meg a legközelebbi zoomot: ott a tile 3,8-4,1x a célnál,
        /// ami ~8-10x szorzót (112 000-136 000 levelet) kérne. A maradék hézag a
        /// felső korlát tudatos ára.
        /// </summary>
        public static int RenderBudgetForViewport(int pixelWidth, int pixelHeight,
            double targetTilePixelSize, double overhead = DefaultRenderBudgetOverhead,
            int minimum = MinimumRenderBudget, int maximum = MaximumRenderBudget)
        {
            if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
            if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));
            if (!(targetTilePixelSize > 0) || double.IsInfinity(targetTilePixelSize))
                throw new ArgumentOutOfRangeException(nameof(targetTilePixelSize));
            if (!(overhead >= 1) || double.IsInfinity(overhead))
                throw new ArgumentOutOfRangeException(nameof(overhead));
            if (minimum < 1) throw new ArgumentOutOfRangeException(nameof(minimum));
            if (maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));

            // double-ban szamolunk: 8K-nal a pixelszam meg bosegesen pontos.
            double perTile = targetTilePixelSize * targetTilePixelSize;
            double wanted = Math.Ceiling((double)pixelWidth * pixelHeight / perTile) * overhead;
            if (!(wanted > minimum)) return minimum;
            return wanted >= maximum ? maximum : (int)wanted;
        }

        /// <summary>
        /// ND-46 (2026-09-02, felhasznaloi screenshot-diagnozis: egy
        /// vizszintes SAV finomodott a kepernyon, felette/alatta durva
        /// tile-ok maradtak) - also korlat a `cosGrazing` (felulet-normal
        /// es "csomopont -&gt; kamera" irany skalaris szorzata) ertekere,
        /// ami alatt a Visit() mar nem probal finomitani (ld. ott a
        /// reszletes indoklast). Ket celt szolgal egyszerre: (1) megvedi a
        /// `angularRadius / cosGrazing` korrekciot a majdnem-pontos
        /// surolasnal (cosGrazing -&gt; 0) valo vegtelenbe-tartastol, (2)
        /// negativ cosGrazing eseten (a csomopont a lathato felszin
        /// TULOLDALAN) azonnal "nem bovul"-kent zarja le az agat - ingyenes
        /// hatterlap-oldali (backface) korlatozas.
        ///
        /// TOBB KOR FIX ERTEKKEL (2026-09-02): 0.02 -&gt; 0.3 (72.5°->
        /// eloteljesitmeny-regresszio miatt, ld.
        /// history/RETROSPECTIVE-2026-09-02-adaptive-lod-saga.md) -&gt; 0.6428
        /// (50°) -&gt; 0.8660 (30°) -&gt; 0.9848 (10°) - minden korben kiderult,
        /// hogy EGY FIX szog NEM tudja garantalni ugyanazt a "hany tile-t
        /// erint" viselkedest kulonbozo zoom-szinteknel: ugyanaz a `φ`
        /// kuszob teljesen mas ivszog-tartomanyt (es ezaltal kepernyo-
        /// pixel-aranyt) zar be kozeli, mint tavoli kameranal - ld. az
        /// osszefugges pontos levezeteset a DefaultMinUsefulCosGrazing
        /// doksijaban.
        ///
        /// EZERT (2026-09-02, otodik kor) a konstans ERTEK helyett a hivo
        /// (PlanetGridMesh) MOST MAR MINDEN FRAME-BEN UJRASZAMOLJA ezt a
        /// kamera aktualis tavolsagabol (ld. lent, `minUsefulCosGrazing`
        /// parameter a BuildCut/SelectCut/Visit lancban) - ez a konstans
        /// csak VISSZAFELE-KOMPATIBILIS ALAPERTELMEZES marad azoknak a
        /// hivoknak (pl. a legtobb egyseg-teszt), amik nem adnak meg
        /// explicit erteket.
        /// </summary>
        public const double DefaultMinUsefulCosGrazing = 0.9848;

        /// <summary>
        /// A DefaultMinUsefulCosGrazing "fix szog nem mukodik minden zoomra"
        /// problemajanak MEGOLDASA (2026-09-02, otodik kor, felhasznaloi
        /// keres: "barmely zoom eseten a lathato pixelek max fele kerulne
        /// negyedelesre").
        ///
        /// LEVEZETES: legyen a bolygo sugara `R`, a kamera tavolsaga a
        /// kozepponttol `d`, es egy felszini pont ivszoge a nadirtol (a
        /// kamera "alatti" ponttol) `θ`. Ekkor a sulyozasi szog koszinusza
        /// (`φ` = a felulet-normal es a kamera-irany szoge, ld. Visit()):
        ///
        ///   cos(φ) = (d·cos(θ) - R) / √(R² + d² - 2dR·cos(θ))
        ///
        /// (haromszog-geometriabol levezetve: a kamera, a gomb kozeppontja
        /// es a felszini pont haromszogenek oldalai/szogei kozotti
        /// osszefugges - ellenorizve: θ=0-nal cos(φ)=1 [nadir, tokeletesen
        /// szembeni], a horizontnal [θ=arccos(R/d)] cos(φ)=0 [pontosan
        /// 90°, ahogy elvarhato egy erintoleges latosugartol]).
        ///
        /// A "barmely zoomnal ugyanolyan ARANYBAN" celt EGYSZERUSITETT
        /// (felhasznalo altal explicit valasztott, KOZELITO - nem a
        /// tenyleges vetitett-pixel-aranyra pontos) modon ugy erjuk el,
        /// hogy a lathato ivszog-tartomany (0-tol a horizont-szogig)
        /// FELEZOPONTJANAK megfelelo `φ` erteket hasznaljuk kuszobkent -
        /// ez FUGGETLEN a kepernyo-vetites okozta osszenyomastol a horizont
        /// fele (tehat NEM pontosan "a lathato PIXELEK fele", hanem "a
        /// lathato IVSZOG-TARTOMANY fele"), de zoom-fuggetlenul KOVETKEZETES
        /// aranyt ad, es sokkal egyszerubb/olcsobb, mint egy vetitett-
        /// terulet-sulyozott (numerikus) valtozat.
        /// </summary>
        public static double ComputeHalfArcMinUsefulCosGrazing(double cameraDistanceFromCenter, double planetRadius)
        {
            if (planetRadius <= 0.0 || cameraDistanceFromCenter <= planetRadius)
                return DefaultMinUsefulCosGrazing; // degeneralt/a felszin alatt - biztonsagos visszaesés

            double d = cameraDistanceFromCenter;
            double r = planetRadius;
            double horizonArcAngle = Math.Acos(Math.Clamp(r / d, -1.0, 1.0));
            double halfArcAngle = horizonArcAngle / 2.0;
            double cosHalfArc = Math.Cos(halfArcAngle);

            double numerator = d * cosHalfArc - r;
            double denominator = Math.Sqrt(Math.Max(0.0, r * r + d * d - 2.0 * d * r * cosHalfArc));
            if (denominator < 1e-9)
                return DefaultMinUsefulCosGrazing;

            double cosPhiHalf = numerator / denominator;
            // Biztonsagi korlat, ugyanaz a mintazat, mint a tobbi ND-46
            // vedelemnel: SOHA ne engedjunk 1.0-hoz tul kozeli (majdnem
            // semmit at nem engedo) vagy 0-nal kisebb (ertelmetlen) erteket.
            return Math.Clamp(cosPhiHalf, 0.0, 0.999);
        }

        /// <summary>
        /// A teljes M9 lepes: nyers kivalasztas (<see cref="SelectCut"/>),
        /// majd 2:1 kiegyensulyozas (<see cref="EnforceRestrictedBalance"/>).
        /// Ezt hivja a viewer - a ket reszlepes kulon-kulon is publikus a
        /// celzott tesztelhetoseg miatt.
        /// </summary>
        public static HashSet<TileId> BuildCut(
            double cameraX, double cameraY, double cameraZ,
            double planetRadius,
            IReadOnlyCollection<TileId> previousCut,
            int baseLevel = DefaultBaseLevel,
            int maxLevel = DefaultMaxLevel,
            double splitThresholdRadians = DefaultSplitThresholdRadians,
            double mergeThresholdRadians = DefaultMergeThresholdRadians,
            double forwardX = 0.0, double forwardY = 0.0, double forwardZ = 1.0,
            double halfFovRadians = NoCullingHalfFovRadians,
            int maxLeafCount = DefaultMaxLeafCount,
            double minUsefulCosGrazing = DefaultMinUsefulCosGrazing,
            int traversalRootLevel = DefaultTraversalRootLevelSentinel,
            int staticBaseLevel = NoStaticBaseLevel,
            Func<TileId, SurfaceLodBounds>? surfaceBounds = null,
            double baseSplitScale = 1.0,
            TerrainLodProxy? terrainProxy = null, LodSelectionWork? work = null)
        {
            if (!(baseSplitScale > 0 && baseSplitScale <= 1)
                || (baseSplitScale < 1 && !(splitThresholdRadians * baseSplitScale > mergeThresholdRadians)))
                throw new ArgumentOutOfRangeException(nameof(baseSplitScale));
            if (baseSplitScale != 1 && staticBaseLevel < 0)
                throw new ArgumentException("A korábbi első split statikus alapot igényel.", nameof(baseSplitScale));
            if (surfaceBounds != null && staticBaseLevel < 0)
                throw new ArgumentException("A domborzati metrika a prioritásos, statikus alapú úthoz tartozik.", nameof(surfaceBounds));
            if (terrainProxy != null && (staticBaseLevel != terrainProxy.BaseLevel || surfaceBounds != null))
                throw new ArgumentException("A proxy a saját statikus szintjét igényli, ND-71 callback nélkül.", nameof(terrainProxy));
            // FAZIS 2 (ND-47): ha van statikus reteg (uj mod), PRIORITASOS
            // (best-first) finomitas - a legnagyobb kepernyo-hibaju (= a
            // kamerahoz legkozelebbi, kozponti) csempet finomitjuk ELOSZOR, es a
            // `maxLeafCount` koltsegvetes kimerulesekor a maradek (perifériás,
            // kis hibaju) resz marad durvabb. Ez DETERMINISZTIKUS (nincs
            // szal-ütemezes-fuggo sorrend) es megszunteti a korabbi "a korlat
            // haphazard helyen vag be, a nezett kozep nem osztodik" tunetet.
            // Regi modban (staticBaseLevel &lt; 0) a korabbi parhuzamos, DFS-alapu
            // SelectCut marad (teljes particio, nincs koltsegvetes-priorizalas).
            var phaseTimer = work != null ? Stopwatch.StartNew() : null;
            HashSet<TileId> cut = staticBaseLevel >= 0
                ? SelectCutPrioritized(
                    cameraX, cameraY, cameraZ, planetRadius, previousCut,
                    baseLevel, maxLevel, splitThresholdRadians, mergeThresholdRadians,
                    forwardX, forwardY, forwardZ, halfFovRadians, maxLeafCount, minUsefulCosGrazing,
                    traversalRootLevel, staticBaseLevel, surfaceBounds, baseSplitScale, terrainProxy, work)
                : SelectCut(
                    cameraX, cameraY, cameraZ, planetRadius, previousCut,
                    baseLevel, maxLevel, splitThresholdRadians, mergeThresholdRadians,
                    forwardX, forwardY, forwardZ, halfFovRadians, maxLeafCount, minUsefulCosGrazing,
                    traversalRootLevel, staticBaseLevel);
            if (work != null) { work.SelectionMs = phaseTimer!.Elapsed.TotalMilliseconds; phaseTimer.Restart(); }
            EnforceRestrictedBalance(cut, baseLevel, maxLeafCount, work != null,
                work?.Cancellation ?? default, work);
            if (work != null) work.BalanceMs = phaseTimer!.Elapsed.TotalMilliseconds;
            return cut;
        }

        /// <summary>
        /// FAZIS 2 (ND-47): PRIORITASOS (best-first) kvadfa-finomitas
        /// koltsegvetessel. A legnagyobb kepernyo-hibaju (effektiv szogsugar)
        /// levelet finomitjuk eloszor; a `budget` (= maxLeafCount) kimerulesekor
        /// a maradek, kis hibaju (perifériás) resz durvabb marad. A statikus
        /// base-reteg mindig fedi a gombot, tehat a "durvabban hagyott" resz nem
        /// lyuk - csak kevesbe finomitott (a screen-space LOD helyes viselkedese:
        /// reszlet oda, ahova a felhasznalo nez).
        ///
        /// DETERMINIZMUS: a prioritasi sor kulcsa (effektiv hiba, majd
        /// TileId.Value) TELJES rendezes, a bejaras EGYSZALU - tehat ugyanaz a
        /// (kamera, elozo cut) par MINDIG bitre ugyanazt a cutot adja, a
        /// szal-ütemezestol fuggetlenul (szemben a parhuzamos SelectCut korlat-
        /// bevagasaval, ami elvben sorrend-fuggo volt).
        /// </summary>
        private static HashSet<TileId> SelectCutPrioritized(
            double cameraX, double cameraY, double cameraZ,
            double planetRadius,
            IReadOnlyCollection<TileId> previousCut,
            int baseLevel, int maxLevel,
            double splitThresholdRadians, double mergeThresholdRadians,
            double forwardX, double forwardY, double forwardZ,
            double halfFovRadians, int budget, double minUsefulCosGrazing,
            int traversalRootLevel, int staticBaseLevel, Func<TileId, SurfaceLodBounds>? surfaceBounds,
            double baseSplitScale, TerrainLodProxy? terrainProxy, LodSelectionWork? work)
        {
            if (baseLevel < 0 || baseLevel > maxLevel)
                throw new ArgumentOutOfRangeException(nameof(baseLevel));
            if (budget <= 0)
                throw new ArgumentOutOfRangeException(nameof(budget));

            int rootLevel = traversalRootLevel == DefaultTraversalRootLevelSentinel ? baseLevel : traversalRootLevel;
            if (rootLevel < 0 || rootLevel > baseLevel)
                throw new ArgumentOutOfRangeException(nameof(traversalRootLevel));

            double fwdLen = Math.Sqrt(forwardX * forwardX + forwardY * forwardY + forwardZ * forwardZ);
            if (fwdLen < 1e-9)
                throw new ArgumentException("A forward iranyvektor nem lehet nulla hosszu.");
            forwardX /= fwdLen; forwardY /= fwdLen; forwardZ /= fwdLen;

            HashSet<TileId> previousExpanded = BuildExpandedAncestorSet(previousCut, baseLevel);
            double camLen = Math.Sqrt(cameraX * cameraX + cameraY * cameraY + cameraZ * cameraZ);
            var result = new HashSet<TileId>();

            // Prioritasi sor: TOMB-ALAPU BINARIS MAX-KUPAC (FAZIS 3 gyorsitas,
            // ND-47, 2026-09-04) - a korabbi SortedSet<(negHiba,Value)> + 2
            // Dictionary helyett. Az (error, TileId.Value) par EGYEDI (a Value
            // tile-onkent egyedi), tehat TELJES rendezes -> a kupac PONTOSAN
            // ugyanabban a sorrendben ad ki, mint a SortedSet.Min (legnagyobb
            // error, holtverseny legkisebb Value szerint), ezert a cut BITRE
            // AZONOS marad - tisztan gyorsitas (kevesebb allokacio + jobb
            // cache-lokalitas, nincs fa-bejaras/hashelés). A .NET PriorityQueue
            // nem elerheto netstandard2.1/Unity alatt. Az `inView`-t NEM taroljuk:
            // csak in-view csomopontot pusholunk (grazeStop/!inView elobb kiesik),
            // tehat a ciklusban mindig igaz lenne.
            var heap = new List<HeapEntry>();
            int pendingDynamicLeaves = 0;

            void TryEnqueueLeaf(TileId node)
            {
                work?.Cancellation.ThrowIfCancellationRequested();
                // ND-78: a renderer által amúgy is teljesen kihagyott base
                // alá nem építünk eldobásra ítélt leszármazottakat.
                if (node.Level==staticBaseLevel && work?.TrySkipStaticBase(node)==true) return;
                EvaluateNodeForPriority(node, cameraX, cameraY, cameraZ, planetRadius, camLen,
                    forwardX, forwardY, forwardZ, halfFovRadians, minUsefulCosGrazing, staticBaseLevel, surfaceBounds, terrainProxy, work,
                    out double error, out bool inView, out bool horizonCulled, out bool grazeStop);
                if (horizonCulled)
                {
                    work?.Trace?.Record(node,"horizon",error);
                    return; // teljesen a horizont mogott - a statikus fed, nincs teendo
                }
                if (grazeStop || !inView)
                {
                    work?.Trace?.Record(node,grazeStop ? "grazing" : "outside-view",error);
                    work?.CountInvisibleStop();
                    // Nem finomodik tovabb (surolo szog vagy latokupon kivul) -
                    // ha a base fole esik, vegleges level; egyebkent a statikus fed.
                    if (node.Level > staticBaseLevel)
                        result.Add(node);
                    return;
                }
                HeapPush(heap, new HeapEntry(error, node));
                if (node.Level > staticBaseLevel) pendingDynamicLeaves++;
            }

            uint rootN = rootLevel == 0 ? 1u : (1u << rootLevel);
            for (int face = 0; face < 6; face++)
                for (uint u = 0; u < rootN; u++)
                    for (uint v = 0; v < rootN; v++)
                        TryEnqueueLeaf(TileId.FromFaceLevelUV(face, rootLevel, u, v));

            while (heap.Count > 0)
            {
                HeapEntry top = HeapPop(heap);
                TileId node = top.Node;
                if (node.Level > staticBaseLevel) pendingDynamicLeaves--;
                double error = top.Error;

                bool wasExpanded = previousExpanded.Contains(node);
                double threshold = wasExpanded ? mergeThresholdRadians : splitThresholdRadians;
                // ND-73: csak az első, statikus alapot kiváltó felosztás
                // indul korábban. A mélyebb szintek nem kapnak sűrűbb célt.
                // A merge-küszöb változatlan: visszazoomkor nem tartjuk meg
                // a korábbi profilnál tovább a már felesleges base-gyerekeket.
                if (node.Level == staticBaseLevel && !wasExpanded) threshold *= baseSplitScale;
                bool wantSplit = node.Level < maxLevel && error > threshold;
                string stopReason = node.Level >= maxLevel ? "max-level" : "below-threshold";

                // ND-69: a függő frontier is lefoglalt megjelenítési költség.
                // A kivett szülő helyére csak akkor kerülhet négy gyermek,
                // ha mind elfér; budgetnél a szülőt őrizzük meg. A base alatti
                // keresés ingyenes, hiszen az még nem hoz dinamikus levelet.
                if (node.Level >= staticBaseLevel
                    && result.Count + pendingDynamicLeaves + 4 > budget)
                {
                    if (wantSplit) { stopReason = "leaf-budget"; work?.CountBudgetStop(); }
                    wantSplit = false;
                }

                // ND-76: a régi felosztás nem fogyaszt új munkakeretet. Az
                // alapszint alatti keresés szintén ingyenes; nincs új geometria.
                if (wantSplit && node.Level >= staticBaseLevel && !wasExpanded && work != null)
                {
                    wantSplit = work.AllowNewSplit();
                    if (!wantSplit) stopReason = "split-quota";
                }

                if (!wantSplit)
                {
                    work?.Trace?.Record(node,stopReason,error,threshold);
                    // A "leaf-budget"/"split-quota" MAR sajat szamlalot kapott
                    // fentebb; itt csak a SZUKSEGTELEN (helyes) megallas szamol.
                    if (stopReason == "below-threshold" || stopReason == "max-level")
                        work?.CountSufficientStop(stopReason == "max-level");
                    if (node.Level > staticBaseLevel)
                        result.Add(node);
                    continue;
                }

                for (int i = 0; i < 4; i++)
                    TryEnqueueLeaf(node.Child(i));
            }

            return result;
        }

        /// <summary>
        /// FAZIS 3 (ND-47): a prioritasos kivalasztas kupac-eleme (nyers hiba +
        /// csomopont). Az inView-t NEM taroljuk (ld. SelectCutPrioritized).
        /// </summary>
        private readonly struct HeapEntry
        {
            public readonly double Error;
            public readonly TileId Node;
            public HeapEntry(double error, TileId node) { Error = error; Node = node; }
        }

        /// <summary>
        /// `a`-nak MAGASABB-e a prioritasa (kupac-gyoker fele), mint `b`-nek:
        /// nagyobb hiba eloszor, holtverseny a KISEBB TileId.Value szerint. Ez a
        /// TELJES rendezes garantalja, hogy a kupac ugyanugy sorrendez, mint a
        /// korabbi SortedSet (determinizmus, bitre azonos cut).
        /// </summary>
        private static bool HigherPriority(in HeapEntry a, in HeapEntry b)
            => a.Error > b.Error || (a.Error == b.Error && a.Node.Value < b.Node.Value);

        private static void HeapPush(List<HeapEntry> heap, HeapEntry e)
        {
            heap.Add(e);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (HigherPriority(heap[i], heap[parent]))
                {
                    HeapEntry tmp = heap[i]; heap[i] = heap[parent]; heap[parent] = tmp;
                    i = parent;
                }
                else break;
            }
        }

        private static HeapEntry HeapPop(List<HeapEntry> heap)
        {
            HeapEntry top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int n = heap.Count;
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, best = i;
                if (l < n && HigherPriority(heap[l], heap[best])) best = l;
                if (r < n && HigherPriority(heap[r], heap[best])) best = r;
                if (best == i) break;
                HeapEntry tmp = heap[i]; heap[i] = heap[best]; heap[best] = tmp;
                i = best;
            }
            return top;
        }

        /// <summary>
        /// FAZIS 2 (ND-47): egy csomopont kiertekelese a prioritasos
        /// finomitashoz - UGYANAZOKKAL a szabalyokkal, mint a Visit() (horizont-
        /// cull, base-alatti nyers meret vs base-tol anizotrop 1/cosGrazing,
        /// latokup), hogy a ket ut (regi DFS es uj prioritasos) geometriailag
        /// konzisztens legyen.
        /// </summary>
        /// <summary>
        /// Igaz, ha a csomopont MINDEN lehetseges terep-pontja a horizont mogott
        /// van, tehat semmikepp nem lathato.
        ///
        /// MIERT NEM A SIK-TESZT. A lentebbi, nem-proxys agban hasznalt
        /// `C·X + |C|·r &lt; R²` az erintosik-teszt: az CSAK a gomb FELSZINEN levo
        /// pontra helyes. Emelt pontra hamis - egy hegycsucs atkukucskal a
        /// horizonton. Megmerve: a sik-tesztes valtozat 54 probanezetbol 6-ban
        /// MEGVALTOZTATTA a cutot, ketto esetben katasztrofalisan (5636 level
        /// -> 0), mert a limbus kornyeki, ferden nezett, VALOBAN LATHATO
        /// csempeket is kivagta.
        ///
        /// A HELYES FELTETEL szogekben: egy `rP` sugaru pont akkor van a `R`
        /// sugaru takaro mogott `d` tavolsagbol nezve, ha a kameratol mert
        /// szoge nagyobb, mint acos(R/d) + acos(R/rP). Ezt koszinuszban
        /// kifejtve csak szorzas es `Math.Sqrt` kell - transzcendens fuggveny
        /// NEM (a CLAUDE.md szerint az nem bitpontos, es a cut determinizmusa
        /// dokumentalt garancia).
        ///
        /// KONZERVATIV BUROK: a takaro a proxy LEGKISEBB sugara (a legkisebb
        /// takaro rejti a legkevesebbet), a csomopont a LEGNAGYOBB sugaru
        /// burkon ul, es a csempe oldalsó kiterjedeset is levonjuk a szogbol.
        /// </summary>
        private static bool IsBeyondHorizon(TileId node,
            double cameraX, double cameraY, double cameraZ, double cameraLength,
            TerrainLodProxy proxy)
        {
            double occluder = proxy.MinimumRadius, outer = proxy.MaximumRadius;
            // A kamera a takaron belul (vagy elfajult adat): ne vagjunk.
            if (!(cameraLength > occluder) || !(outer >= occluder) || !(occluder > 0)) return false;

            GetCenterAndBoundingRadius(node, outer, out double x, out double y, out double z,
                out double footprint);
            // A csempe oldalso kiterjedese nem nyulhat tul a burkon.
            if (!(footprint < outer)) return false;

            double cosAngle = (cameraX * x + cameraY * y + cameraZ * z) / (cameraLength * outer);
            if (cosAngle > 1.0) cosAngle = 1.0; else if (cosAngle < -1.0) cosAngle = -1.0;
            double sinAngle = Math.Sqrt(Math.Max(0.0, 1.0 - cosAngle * cosAngle));

            // A kiterjedes szoge: sin = footprint / outer.
            double sinExtent = footprint / outer;
            double cosExtent = Math.Sqrt(Math.Max(0.0, 1.0 - sinExtent * sinExtent));

            // cos(szog - kiterjedes): a csempe kamerahoz LEGKOZELEBBI pontja.
            double cosNearest = cosAngle * cosExtent + sinAngle * sinExtent;

            // cos(acos(R/d) + acos(R/rP)) - a lathatosag hatarszoge.
            double a = occluder / cameraLength, b = occluder / outer;
            double cosLimit = a * b
                - Math.Sqrt(Math.Max(0.0, 1.0 - a * a)) * Math.Sqrt(Math.Max(0.0, 1.0 - b * b));

            return cosNearest < cosLimit;
        }

        private static void EvaluateNodeForPriority(
            TileId node,
            double cameraX, double cameraY, double cameraZ, double planetRadius, double camLen,
            double forwardX, double forwardY, double forwardZ, double halfFovRadians,
            double minUsefulCosGrazing, int staticBaseLevel, Func<TileId, SurfaceLodBounds>? surfaceBounds, TerrainLodProxy? terrainProxy, LodSelectionWork? work,
            out double error, out bool inView, out bool horizonCulled, out bool grazeStop)
        {
            error = 0.0; inView = false; horizonCulled = false; grazeStop = false;

            if (work?.View != null && terrainProxy != null)
            {
                // ND-115 (2026-09-19): a vetitett terep-uton EDDIG SEMMILYEN
                // horizont-cull nem futott - a korai `return` miatt a lentebbi
                // kiterjedes-tudatos teszt soha nem hajtodott vegre ezen az
                // agon. Merve (D=103, magassag 3, a lathato sapka a gomb ~1,4%-a):
                // a level-8 szinten 123 152 csomopontot jartunk be es 120 846-ot
                // tettunk kupacra, holott ~5 500 tile lathato. A teljes vagas
                // 191 084 kiertekelesebol 180 564 MAR a base-szint eleresekor
                // megtortent - 96% olyan munka, ami egyetlen levelet sem termel.
                //
                // CSAK A BASE-SZINT ALATT vagunk (`node.Level < staticBaseLevel`):
                // a megtakaritas ugyis a leszallasbol jon (a kivagott csomopont
                // gyerekei letre sem jonnek), a base-szintu csomopontok pedig
                // valtozatlanul a regi uton ertekelodnek ki. Ez a kisebb
                // hatosugaru valtozat; merve ugyanannyit hoz, mint a `<=`
                // (75,5% vs 75,6% kevesebb kiertekeles).
                //
                // NEM allitjuk, hogy a cut bitre valtozatlan. Merve (54 proba-
                // nezet, tavolsag/tengely/dolesszog szerint): 48-ban bitre
                // azonos, 6-ban elter - ezek telitett budgetnel a best-first
                // hatarat tolják el, mert a frontierrol eltunnek a lathatatlan
                // csomopontok. A DONTO ellenorzes ezert a lathato fedettseg:
                // 54 nezet x 21x11 kepernyo-minta alapjan 53-ban VALTOZATLAN,
                // 1-ben JOBB, 0-ban rosszabb - a felszabadult budget a lathato
                // reszre megy. Ld. HorizonCullTests.
                //
                // KONZERVATIV BUROK: a takaro gomb a proxy LEGKISEBB sugara
                // (a legkisebb takaro rejti a legkevesebbet), a csomopont pedig
                // a LEGNAGYOBB sugaru burkon ul (egy hegycsucs atkukucskalhat a
                // horizonton). Igy csak bizonyitottan nem lathato csomopont esik ki.
                if (staticBaseLevel >= 0 && node.Level < staticBaseLevel
                    && IsBeyondHorizon(node, cameraX, cameraY, cameraZ, camLen, terrainProxy))
                {
                    horizonCulled = true;
                    return;
                }
                inView = work.EvaluateTerrain(terrainProxy,node,out error);
                return;
            }

            // ND-71: az alapgömb horizontja és radiális normálisa nem írhatja
            // felül a tényleges domborzatot. A takart terep finomítását egy
            // későbbi, bizonyított terrain-occlusion teszt szűrheti tovább.
            if (surfaceBounds != null)
            {
                SurfaceLodBounds bounds = surfaceBounds(node);
                error = bounds.AngularRadius(cameraX, cameraY, cameraZ);
                inView = bounds.IntersectsViewCone(cameraX, cameraY, cameraZ,
                    forwardX, forwardY, forwardZ, halfFovRadians);
                return;
            }

            GetCenterAndBoundingRadius(node, planetRadius, out double cx, out double cy, out double cz, out double rTile);
            double dx = cameraX - cx, dy = cameraY - cy, dz = cameraZ - cz;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // FAZIS 2 JAVITAS (ND-47, 2026-09-03 masodik elo-meres): a prioritas
            // es a felbontas-dontes a NYERS kepernyo-hibabol (angularRadius =
            // atan2(rTile,distance)) szuletik - NINCS `1/cosGrazing` anizotrop
            // szorzo. Indok: az elozo (boost-os) valtozat a SUROLO (horizont-
            // menti) csempeknek adott magasabb prioritast, mint a nadirnak, ezert
            // a budget a HORIZONT-SAVRA ment, NEM oda, ahova a felhasznalo nez
            // (a bejelentett "nem jo helyen osztodik" tunet - ugyanaz az ND-46-
            // mechanizmus, csak a prioritasban). A nyers angularRadius a
            // LEGKOZELEBBI (kamerahoz kozeli, kepernyo-kozeppontban levo, tipikusan
            // nadir) csempet reszesiti elonyben - oda kerul a reszlet, ahova a
            // felhasznalo nez. A grazing-menti (tavoli) csempek nyers szogmerete
            // kicsi -> keves budgetet visznek el.
            double angularRadius = Math.Atan2(rTile, distance);

            // Kiterjedes-tudatos horizont/hatlap-cull (ld. Visit()).
            double cDotP = cameraX * cx + cameraY * cy + cameraZ * cz;
            if (cDotP + camLen * rTile < planetRadius * planetRadius)
            {
                horizonCulled = true;
                return;
            }

            // Hatter-oldali (backface) biztonsagi cull: ha a csomopont KOZEPPONTJA
            // a lathato felszin tuloldalan van (cosGrazing &lt;= 0), ES base-tol
            // lefele vagyunk, lezarjuk (a kiterjedes-tudatos horizont-cull a
            // reszben lathatokat mar atengedte; ez csak a tisztan hatso oldalt
            // zarja). NINCS agresszive `minUsefulCosGrazing` kuszob tobbe a
            // prioritasos uton - a nyers szogmeret + budget vegzi a rangsorolast.
            if (distance > 1e-9 && (staticBaseLevel < 0 || node.Level >= staticBaseLevel))
            {
                double nx = cx / planetRadius, ny = cy / planetRadius, nz = cz / planetRadius;
                double cosGrazing = (nx * dx + ny * dy + nz * dz) / distance;
                if (cosGrazing <= 0.0)
                {
                    grazeStop = true; // vegleges (durva) level - nem finomodik tovabb
                    error = angularRadius;
                    inView = IsWithinViewCone(cx, cy, cz, rTile, cameraX, cameraY, cameraZ, forwardX, forwardY, forwardZ, halfFovRadians);
                    return;
                }
            }

            error = angularRadius;
            inView = IsWithinViewCone(cx, cy, cz, rTile, cameraX, cameraY, cameraZ, forwardX, forwardY, forwardZ, halfFovRadians);
            // ND-74: az előszűrés most változatlan. Csak az átjutó patch
            // prioritása/osztása kap tereptávolságot; nincs új modellminta.
            if (inView && terrainProxy != null)
            {
                terrainProxy.ScaleMetric(node, planetRadius, ref cx, ref cy, ref cz, ref rTile);
                dx = cameraX - cx; dy = cameraY - cy; dz = cameraZ - cz;
                error = Math.Atan2(rTile, Math.Sqrt(dx * dx + dy * dy + dz * dz));
            }
        }

        /// <summary>
        /// Top-down kvadfa-bejaras a base-level gyokerektol: minden
        /// csomopontnal a kamera-tavolsag es a csomopont befoglalo-sugara
        /// alapjan dont a felbontasrol, hiszterezissel (ld. osztaly-doc).
        /// NEM vegzi el a 2:1 kiegyensulyozast - ld. <see cref="BuildCut"/>.
        ///
        /// LATOKUP-SZURES (forwardX/Y/Z + halfFovRadians): a BASE LEVEL
        /// partíció MINDIG teljes (a gomb minden pontjan van legalabb egy
        /// leaf - ld. a §2.4 NoGaps-elvet, ez itt is garantalt), de a
        /// FINOMODAS (base level feletti felbontas) csak azokra a
        /// csomopontokra tortenik, amik a kamera latokupjaban (vagy annak
        /// kozeleben) vannak. Enelkul egy a felszinhez nagyon kozeli
        /// kamera (kis magassag) a KOROTTE levo TELJES korlapot finomitana
        /// - elore, hatra, oldalra egyarant -, holott a hatra/oldalra eso
        /// resz sosem jelenik meg a kepernyon. Ez okozott korabban
        /// robbanasszeru, de haszontalan tile-szamot.
        /// </summary>
        public static HashSet<TileId> SelectCut(
            double cameraX, double cameraY, double cameraZ,
            double planetRadius,
            IReadOnlyCollection<TileId> previousCut,
            int baseLevel = DefaultBaseLevel,
            int maxLevel = DefaultMaxLevel,
            double splitThresholdRadians = DefaultSplitThresholdRadians,
            double mergeThresholdRadians = DefaultMergeThresholdRadians,
            double forwardX = 0.0, double forwardY = 0.0, double forwardZ = 1.0,
            double halfFovRadians = NoCullingHalfFovRadians,
            int maxLeafCount = DefaultMaxLeafCount,
            double minUsefulCosGrazing = DefaultMinUsefulCosGrazing,
            int traversalRootLevel = DefaultTraversalRootLevelSentinel,
            int staticBaseLevel = NoStaticBaseLevel)
        {
            if (baseLevel < 0 || baseLevel > maxLevel)
                throw new ArgumentOutOfRangeException(nameof(baseLevel), "A baseLevel 0..maxLevel tartomanyban lehet.");

            // FAZIS 1 (ND-47): a bejaras gyokerszintje. Sentinel (-1) eseten a
            // REGI viselkedes: a base-szintrol indul (teljes particio). Ha a
            // hivo explicit alacsonyabb szintet ad meg, a bejaras onnan indul
            // es a base fole finomit (ld. DefaultTraversalRootLevelSentinel).
            int rootLevel = traversalRootLevel == DefaultTraversalRootLevelSentinel ? baseLevel : traversalRootLevel;
            if (rootLevel < 0 || rootLevel > baseLevel)
                throw new ArgumentOutOfRangeException(nameof(traversalRootLevel),
                    "A traversalRootLevel a 0..baseLevel tartomanyban lehet (a bejarasnak legkesobb a base szinten kell athaladnia).");
            if (maxLevel > TileId.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(maxLevel), $"A maxLevel legfeljebb {TileId.MaxLevel} lehet.");
            if (splitThresholdRadians <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(splitThresholdRadians), "A splitThresholdRadians-nak pozitivnak kell lennie.");
            if (mergeThresholdRadians > splitThresholdRadians)
                throw new ArgumentOutOfRangeException(nameof(mergeThresholdRadians),
                    "A mergeThresholdRadians nem lehet nagyobb a splitThresholdRadians-nal (hiszterezis - a szogsugar-metrikanal a KISEBB kuszob jelenti \"meg mindig felbontast igenyel\"-t).");
            if (maxLeafCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxLeafCount), "A maxLeafCount-nak pozitivnak kell lennie.");

            double fwdLen = Math.Sqrt(forwardX * forwardX + forwardY * forwardY + forwardZ * forwardZ);
            if (fwdLen < 1e-9)
                throw new ArgumentException("A forward iranyvektor nem lehet nulla hosszu.");
            forwardX /= fwdLen; forwardY /= fwdLen; forwardZ /= fwdLen;

            HashSet<TileId> previousExpanded = BuildExpandedAncestorSet(previousCut, baseLevel);

            // Parhuzamositas (gpu-calc): a base-level gyokerek EGYMASTOL
            // FUGGETLENUL bejarhatok (a Visit() rekurzio csak OLVASSA a
            // `previousExpanded`-et, es csak HOZZAAD a kimenethez - nincs
            // megosztott mutable allapot, amit at kellene rendezni). Egy
            // ConcurrentBag-be gyujtunk (szalbiztos Add), a vegen egyszer
            // alakitjuk HashSet-te. Mert szukseges: nagy `splitFactor`
            // mellett a SelectCut onmagaban is tobb-tíz-milliszekundumos
            // koltseg lehet (merve), es a base-level gyokerek szama
            // (6*4^baseLevel) tobbnyire jocskan meghaladja a magok szamat,
            // tehat joul skalazodik.
            uint rootN = rootLevel == 0 ? 1u : (1u << rootLevel);
            int rootsPerFace = (int)(rootN * rootN);
            int totalRoots = 6 * rootsPerFace;
            var bag = new System.Collections.Concurrent.ConcurrentBag<TileId>();

            // OLCSO ELOSZURES (kritikus nagy baseLevel-nel, pl. 8 = 393216
            // gyoker): a `Visit()` MAGA draga (GetCenterAndBoundingRadius 4
            // tan/atan-hasznalo TileGeometry-hivassal + latokup-teszt), es
            // baseLevel=8-nal ezt mind a 393216 gyokerre lefuttatni akkor
            // is, ha a VEGEREDMENY "marad alap szinten" - mert 80-320ms-et
            // vett igenybe MINDEN egyes ujraepitesnel (merve), fuggetlenul
            // attol, hogy a mesh-epites mar csak a finomitott reszt erinti.
            // A javitas: egy OLCSO also-becsles (csak TileGeometry.ToPosition,
            // NEM a teljes befoglalo-sugar-szamitas) kiszamitja, hogy egy
            // adott base-level gyoker LEHETSEGES-e egyaltalan hogy finomodjon.
            // Szogsugar-metrikaval (screen-space-lod branch): a gyoker akkor
            // "lehetseges", ha meg a LEGKEDVEZOBB (legkisebb, azaz
            // mergeThresholdRadians-t hasznalo) kuszobbel is elerheto a
            // felbontast kivalto tavolsag - atan2(rTile,d) > mergeThreshold
            // <=> d < rTile/tan(mergeThreshold). A hatarertek KONZERVATIV (a
            // tenyleges max/min terulet-arany felulrol korlatos ND-24
            // szerint, itt egy biztonsagos 2x szorzoval a rTile-on), tehat
            // SOSEM zar ki egy olyan gyokeret, aminek ténylegesen
            // finomodnia kellene - csak a egyertelmuen tavoli, semmikepp
            // nem finomodo gyokereknel sporol.
            //
            // ND-46: a Visit() mostantol a nezesi-szog-korrekcioval (ld. ott)
            // akar `1/MinUsefulCosGrazing`-szeres tavolsagig is finomíthat egy
            // surolo szogu csomopontot - ezt a HATARERTEKET IS bele kell
            // szamitani ide, kulonben az OLCSO ELOSZURES epp azokat a tavoli,
            // de surolo szogben latott gyokereket zarna ki idő előtt, amiket
            // az ND-46 javitas kifejezetten finomitani akarna.
            GetCenterAndBoundingRadius(
                TileId.FromFaceLevelUV(0, rootLevel, 0, 0), planetRadius,
                out _, out _, out _, out double sampleRTile);
            double conservativeMaxRTile = sampleRTile * 2.0;
            double maxRelevantDistance = conservativeMaxRTile / Math.Tan(mergeThresholdRadians) / minUsefulCosGrazing;
            double maxRelevantDistanceSq = maxRelevantDistance * maxRelevantDistance;

            // FAZIS 1 (ND-47): KET, kulonvalasztott szamlalo (az int[1] "boxed"
            // trukk azert kell, mert a Parallel.For lambda es a rekurziv Visit()
            // UGYANAZT a szamlalot kell lassa/novelje az OSSZES gyoker-agban).
            //   * leafCounter  = KIMENETI szamlalo: CSAK a ténylegesen emittalt
            //     (level &gt; staticBaseLevel) leaf-eket szamolja - ez a
            //     RENDER-koltseget hatarolja (maxLeafCount).
            //   * visitCounter = MUNKA-orzo: MINDEN meglatogatott csomopontot
            //     szamol, generoz workCap-pel - defenziv also-vonal barmilyen
            //     elore nem latott bejaras-robbanas ellen (2026-09-01 runaway).
            //
            // MIERT KET SZAMLALO (a regi, egy-szamlalos valtozat hibaja): a
            // korabbi egyetlen szamlalo MINDEN Visit-et a maxLeafCount ELLEN
            // szamolt, beleertve a BASE-ALATTI leereszkedest is (ami az uj
            // modban SEMMIT nem emittal). Igy a below-base bejaras elhasznalta a
            // keretet, mielott a produktiv (nadirhoz kozeli) ag egyaltalan
            // eljutott volna a finomitasig -> a cut esetlegesen URES/foltszeru
            // lett. Mostantol a below-base leereszkedes NEM terheli a kimeneti
            // korlatot; azt a latokup+horizont-kulling hatarolja (screen-space),
            // a workCap pedig csak vegso vedovonal.
            var leafCounter = new int[1];
            var visitCounter = new int[1];
            int workCap = maxLeafCount > int.MaxValue / 8 ? int.MaxValue : maxLeafCount * 8;

            System.Threading.Tasks.Parallel.For(0, totalRoots, rootIndex =>
            {
                int face = rootIndex / rootsPerFace;
                int withinFace = rootIndex % rootsPerFace;
                uint u = (uint)(withinFace / (int)rootN);
                uint v = (uint)(withinFace % (int)rootN);

                TileId root = TileId.FromFaceLevelUV(face, rootLevel, u, v);

                TileGeometry.ToPosition(root, out double rx, out double ry, out double rz);
                double ccx = rx * planetRadius, ccy = ry * planetRadius, ccz = rz * planetRadius;
                double ddx = cameraX - ccx, ddy = cameraY - ccy, ddz = cameraZ - ccz;
                double distSq = ddx * ddx + ddy * ddy + ddz * ddz;

                if (distSq > maxRelevantDistanceSq)
                {
                    // Garantaltan tul messze van barmilyen finomodashoz. FAZIS 1
                    // (ND-47): ha van statikus reteg, a gyoker (rootLevel &lt;=
                    // staticBaseLevel) NEM emittalodik - azt a statikus reteg
                    // fedi; enelkul egy a statikusnal DURVABB level-kerulne a
                    // cut-ba (amit a mesh-epites amugy is atugorna, de a cut-ot
                    // es a balance-t szennyezne). Az EmitLeaf maga novelia a
                    // kimeneti szamlalot, HA ténylegesen emittal.
                    EmitLeaf(bag, root, leafCounter, staticBaseLevel);
                    return;
                }

                Visit(root, cameraX, cameraY, cameraZ, planetRadius,
                    previousExpanded, maxLevel, splitThresholdRadians, mergeThresholdRadians,
                    forwardX, forwardY, forwardZ, halfFovRadians, bag, leafCounter, visitCounter, maxLeafCount, workCap, minUsefulCosGrazing,
                    staticBaseLevel);
            });

            return new HashSet<TileId>(bag);
        }

        private static void Visit(
            TileId node,
            double cameraX, double cameraY, double cameraZ, double planetRadius,
            HashSet<TileId> previousExpanded, int maxLevel, double splitThresholdRadians, double mergeThresholdRadians,
            double forwardX, double forwardY, double forwardZ, double halfFovRadians,
            System.Collections.Concurrent.ConcurrentBag<TileId> cut,
            int[] leafCounter, int[] visitCounter, int maxLeafCount, int workCap, double minUsefulCosGrazing, int staticBaseLevel)
        {
            // BIZTONSAGI KORLAT, JAVITOTT VALTOZAT (2026-09-01, MASODIK
            // kor - az ELSO valtozat NEM volt eleg: az csak a VEGSO levél-
            // szamot korlatozta, de a szamlalot csak akkor novelte, amikor
            // egy csomopont TENYLEGESEN level lett - egy "elszabadult",
            // horizont-kozeli ag viszont akar 20 szintig is rekurzalhatott
            // ANELKUL, hogy egyetlen levelet is hozzaadott volna, tehat a
            // MUNKA (dragaGetCenterAndBoundingRadius+atan2+latokup-teszt
            // MINDEN koztes csomoponton) korlatlan maradt, csak a VEGEREDMENY
            // meret volt korlatos - Profilerrel megerositve: 725ms+
            // "PlanetGridMesh.Update() Self" + 210ms GC.Collect, MIKOZBEN a
            // Stopwatch-figyelmeztetes (RebuildAdaptiveMesh korul) nemán
            // maradt, mert a koltseg NEM RebuildAdaptiveMesh-ben, hanem a
            // MEGELOZO AdaptiveQuadTree.BuildCut hivasban jelentkezett.
            //
            // JAVITAS: a szamlalo MOST MINDEN EGYES Visit()-hivast szamol
            // (nem csak a levelekent vegzodoket), es a korlat-ellenorzes a
            // FUGGVENY LEGELEJEN, MEG A DRAGA SZAMITAS ELOTT tortenik - igy
            // a korlat a TENYLEGES MUNKAMENNYISEGET (megvizsgalt csomopontok
            // szama) hatarolja be, nem csak a vegeredmenyt.
            // MUNKA-orzo (defenziv, generoz workCap): a TELJES bejaras-munkat
            // hatarolja barmilyen elore nem latott degeneracio ellen (ld. a
            // 2026-09-01 runaway-t, ahol egy horizont-kozeli ag emisszio nelkul
            // rekurzalt melyre). Normal esetben SOHA nem aktiv - a tenyleges
            // LOD-dontest a lenti szog/latokup-kriteriumok hozzak.
            if (System.Threading.Interlocked.Increment(ref visitCounter[0]) > workCap)
            {
                EmitLeaf(cut, node, leafCounter, staticBaseLevel);
                return;
            }

            // KIMENETI korlat (FAZIS 1, ND-47): ha mar eleg output (level>base)
            // leaf szuletett, ne finomodjunk tovabb - a JELEN csomopontot
            // durvakent emittaljuk. A below-base leereszkedes NEM novelte a
            // leafCounter-t, tehat ez a korlat a tenyleges RENDER-koltseget
            // hatarolja, NEM a bejarast (a "lazan szinkronizalt" olvasas
            // szandekos: nehany tobblet parhuzamos tulloves megengedett).
            if (System.Threading.Volatile.Read(ref leafCounter[0]) > maxLeafCount)
            {
                EmitLeaf(cut, node, leafCounter, staticBaseLevel);
                return;
            }

            if (node.Level >= maxLevel)
            {
                EmitLeaf(cut, node, leafCounter, staticBaseLevel);
                return;
            }

            GetCenterAndBoundingRadius(node, planetRadius, out double cx, out double cy, out double cz, out double rTile);
            double dx = cameraX - cx, dy = cameraY - cy, dz = cameraZ - cz;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // A csomopont TENYLEGES vetitett szogsugara (radian) - ugyanaz a
            // keplet, mint az IsWithinViewCone-ban. Ez a "screen-space error"
            // metrika: fuggetlen a tavolsagtol/szinttol, kozvetlenul a kamera
            // FOV/felbontasabol szarmazo kuszobbel osszemerheto.
            double angularRadius = Math.Atan2(rTile, distance);

            // ND-46 JAVITAS (2026-09-02, felhasznaloi screenshot-diagnozis: egy
            // vizszintes SAV a kepernyon finomodott, felette/alatta nagy,
            // durva tile-ok maradtak): a fenti `angularRadius` a csomopont
            // befoglalo-gombjenek szogmeretet nezi a kamera-tavolsagbol,
            // FUGGETLENUL attol, milyen SZOGBEN latszik a felulet. Egy gorbult
            // bolygofeluletnel, KOZELI (felszinhez tapadó) kameranal ez azt
            // eredmenyezi, hogy a horizonthoz kozeli (surolo ralatasu) terep
            // EGYENES-VONALU tavolsaga a kameratol NAGYSAGRENDEKKEL nagyobb
            // lehet, mint a kozvetlenul alatta levo (naidr) teruleté - annak
            // ellenere, hogy meg mindig jelentos KEPERNYO-teruletet foglal el
            // (surolo/anizotrop vetites, ld. a klasszikus texture-anisotropic-
            // filtering erveles: egy `theta` szogben latott sik felulet
            // effektiv "surusegenek" fenn kellene maradnia kb. `1/cos(theta)`
            // szorzoval, ahol `theta` a felulet-normal es a kamera-irany
            // szoge). Enelkul a rendszer csak egy szuk, a kamera kozvetlen
            // "aljara" kozpontosult savot/foltot finomit, minden mas latott,
            // de surolo szogu teruletet tulzottan durvan hagy.
            //
            // A felulet-NORMAL egy gombon egyszeruen a kozeppont egysegvektora
            // (cx,cy,cz)/planetRadius - nincs kulon szamitas. `cosGrazing` =
            // a normal es a "csomopont -> kamera" irany skalaris szorzata:
            // 1.0 = a kamera pontosan a csomopont felett all (legjobb eset,
            // nincs korrekcio), 0-hoz kozeledve = surolo ralatas (maximalis
            // korrekcio), negativ = a csomopont a lathato felszin TULOLDALAN
            // van (a bolygo sajat gorbulete elfedi) - ilyenkor a finomitasnak
            // nincs ertelme, azonnal "nem bovul"-kent kezeljuk (ez egyben egy
            // ingyenes, korabban hianyzo hatter-oldali korlatozas is).
            //
            // A korrekcio also korlatja (MinUsefulCosGrazing) MEGAKADALYOZZA,
            // hogy a `1/cosGrazing` szorzo majdnem-pontos surolasnal (cosGrazing
            // -> 0) a vegtelenbe tartson - ez UGYANAZ a fajta vedelem, mint az
            // IsWithinViewCone korabbi (8480cbe) angularRadius-korlatozasa: a
            // korrekcio SOHA nem teheti a hatasos szogsugarat pi/2-nel
            // nagyobba, es kulon retegzett biztonsagi korlatok (leafCounter/
            // balanceSizeCap) is vedik a rendszert barmilyen maradek
            // szelsoseges esettol.
            double effectiveAngularRadius = angularRadius;
            if (distance > 1e-9)
            {
                double nx = cx / planetRadius, ny = cy / planetRadius, nz = cz / planetRadius;
                double cosGrazing = (nx * dx + ny * dy + nz * dz) / distance;

                // FAZIS 1 (ND-47): KITERJEDES-TUDATOS horizont/hatlap-cull,
                // MINDEN szinten ervenyes. Egy P felszini pont a kameratol
                // (C, |C|=camLen) akkor lathato, ha C·P &gt;= R² (a horizont
                // pontosan a C·P = R² sík; ez ekvivalens a cosGrazing&gt;=0-val,
                // de a KITERJEDEST is figyelembe tudjuk venni). A csomopont
                // befoglalo-gombjenek (kozeppont Pc, sugar rTile) LEGjobban
                // lathato pontja C·P &lt;= C·Pc + camLen·rTile, tehat a tile CSAK
                // akkor van TELJESEN a horizonton tul, ha meg ez a felso becsles
                // is &lt; R². Igy SOSEM vagunk le RESZBEN lathato (pl. a nadir
                // fole nyulo, de kozeppontjukkal mar a horizonton tuli, DURVA
                // base-feletti) tile-t - ez volt a kozeppont-alapu cull hibaja
                // (kozeli zoomnal a level-3 nadir-tile kozeppontja mar ~10°-ra,
                // a horizont ~6°-ra volt -> a teljes nadir-oszlop kiesett).
                double camLen = Math.Sqrt(cameraX * cameraX + cameraY * cameraY + cameraZ * cameraZ);
                double cDotP = cameraX * cx + cameraY * cy + cameraZ * cz;
                if (cDotP + camLen * rTile < planetRadius * planetRadius)
                {
                    EmitLeaf(cut, node, leafCounter, staticBaseLevel);
                    return;
                }

                // Az AGRESSZIV anizotrop kuszobot (minUsefulCosGrazing, ~surolo-
                // szog-korlat) csak a base-szinttol LEFELE (node.Level &gt;=
                // staticBaseLevel) alkalmazzuk. Indok: a kuszobot a csomopont
                // KOZEPPONTJABOL szamoljuk, ami egy DURVA (base feletti) tile-nal
                // MESSZE eshet a valodi, nadirhoz kozeli leszarmazottaktol -
                // ilyenkor a kuszob a TELJES reszfat levaghatna, MIELOTT eljutnank
                // a ténylegesen finomitando (magas cosGrazing-u) tile-okig
                // (megfigyelt hiba: kozeli, egyenesen-lefele nezo kameranal a
                // level-3 nadir-tile kozeppontja mar surolo szogben van, igy a
                // teljes nadir-oszlop kiesett -> ures cut). A base-szinttol a
                // tile mar eleg kicsi, hogy a kozeppont-alapu szog egyezzen a
                // regi (base=traversalRoot) viselkedessel. Regi modban
                // (staticBaseLevel &lt; 0) a bejaras amugy is base-rol indul,
                // tehat a feltetel mindig igaz -> valtozatlan viselkedes.
                bool applyGrazingThreshold = staticBaseLevel < 0 || node.Level >= staticBaseLevel;
                if (applyGrazingThreshold)
                {
                    if (cosGrazing <= minUsefulCosGrazing)
                    {
                        EmitLeaf(cut, node, leafCounter, staticBaseLevel);
                        return;
                    }
                    // Anizotrop (surolo-szog) korrekcio - CSAK a base-szinttol
                    // lefele. Itt a cosGrazing garantaltan &gt; minUsefulCosGrazing
                    // &gt; 0 (a fenti check miatt), tehat a hanyados POZITIV.
                    effectiveAngularRadius = Math.Min(angularRadius / cosGrazing, Math.PI / 2.0);
                }
                // FAZIS 1 (ND-47): base ALATT (node.Level &lt; staticBaseLevel) NEM
                // alkalmazzuk a `1/cosGrazing` korrekciot - egy DURVA, a horizontot
                // ATLEPO tile kozeppontja lehet a horizonton TUL (cosGrazing &lt; 0),
                // holott a kozeli sarka meg lathato (ezert a kiterjedes-tudatos
                // horizont-cull helyesen MEGTARTOTTA). Ilyenkor az `angularRadius /
                // cosGrazing` NEGATIV lenne -> a csomopont tévesen "nem akar
                // bovulni"-kent zarodna le, es a teljes (nadirhoz vezeto) reszfa
                // lemetszodne. Base alatt ezert a NYERS angularRadius dont a
                // leereszkedesrol (a durva tile amugy is mindig bovul); az
                // anizotrop finomitas a base-szinttol lep eletbe, ahol a tile mar
                // eleg kicsi, hogy a kozeppontja ne lepje at a horizontot.
            }

            // Hiszterezis: ha ez a csomopont az ELOZO keretben mar fel volt
            // bontva (valamelyik leszarmazottja aktiv volt), a KISEBB
            // K_merge kuszob (mergeThresholdRadians &lt; splitThresholdRadians)
            // kell ahhoz, hogy MEG MINDIG "felbontast igenylo"-nek szamitson
            // (azaz tovabb kell zsugorodnia a kepernyon, mint amennyi eredetileg
            // kivaltotta a felbontast, mielott osszevonodik) - forditott irany
            // a regi tavolsag-alapu valtozathoz kepest, ahol a NAGYOBB kuszob
            // jelentette ugyanezt. Ld. osztaly-doc.
            double threshold = previousExpanded.Contains(node) ? mergeThresholdRadians : splitThresholdRadians;
            bool sizeWantsExpand = effectiveAngularRadius > threshold;

            // Latokup-szures: a meret-kriterium onmagaban NEM eleg -
            // finomitas csak akkor tortenik, ha a csomopont (a SAJAT
            // szogmeretevel bovitett kuszobbel) ténylegesen a kamera
            // latokupjaban van. A sajat szogmeret hozzaadasa azert kell,
            // hogy egy nagy, meg a kup szelen levo csomopont ne essen ki
            // tul korán (a kozeppontja mar kicsit kivul lehet, mig a
            // teste meg reszben belul).
            bool expand = sizeWantsExpand
                && IsWithinViewCone(cx, cy, cz, rTile, cameraX, cameraY, cameraZ, forwardX, forwardY, forwardZ, halfFovRadians);

            if (!expand)
            {
                EmitLeaf(cut, node, leafCounter, staticBaseLevel);
                return;
            }

            for (int i = 0; i < 4; i++)
                Visit(node.Child(i), cameraX, cameraY, cameraZ, planetRadius,
                    previousExpanded, maxLevel, splitThresholdRadians, mergeThresholdRadians,
                    forwardX, forwardY, forwardZ, halfFovRadians, cut, leafCounter, visitCounter, maxLeafCount, workCap, minUsefulCosGrazing,
                    staticBaseLevel);
        }

        /// <summary>
        /// FAZIS 1 (ND-47): egy megallo (tovabb nem finomodo) csomopont
        /// emittalasa a cut-ba. Ha van statikus reteg (staticBaseLevel &gt;= 0),
        /// CSAK a `level &gt; staticBaseLevel` csomopontok kerulnek bele - a
        /// durvabbakat (a bejaras base-ig/base-en leallo agai) a statikus
        /// reteg fedi, ezert oket NEM adjuk a dinamikus cut-hoz. Statikus
        /// reteg nelkul (NoStaticBaseLevel) minden megallo csomopont
        /// emittalodik (regi, teljes-particio viselkedes).
        /// </summary>
        private static void EmitLeaf(System.Collections.Concurrent.ConcurrentBag<TileId> cut, TileId node, int[] leafCounter, int staticBaseLevel)
        {
            if (staticBaseLevel < 0 || node.Level > staticBaseLevel)
            {
                cut.Add(node);
                // KIMENETI szamlalo: csak a ténylegesen emittalt leaf-eket
                // szamoljuk (a below-base leereszkedes NEM emittal -> nem szamit).
                System.Threading.Interlocked.Increment(ref leafCounter[0]);
            }
        }

        /// <summary>
        /// Egy csomopont (gomb-kozeppont + befoglalo sugar) a kamera
        /// latokupjaban van-e - kup-teszt, a csomopont SAJAT szogmeretevel
        /// (angularRadius) bovitve, hogy a kup szelen levo, meg reszben
        /// lathato csomopontok ne essenek ki tul korán. `halfFovRadians`
        /// tartalmazza a hivo altal mar hozzaadott biztonsagi ratartast
        /// (pl. keplet-oldali FOV/aspect + margo) - ez a fuggveny mar csak
        /// a nyers geometriai osszehasonlitast vegzi.
        /// </summary>
        internal static bool IsWithinViewCone(
            double nodeX, double nodeY, double nodeZ, double rTile,
            double cameraX, double cameraY, double cameraZ,
            double forwardX, double forwardY, double forwardZ,
            double halfFovRadians)
        {
            if (halfFovRadians >= Math.PI)
                return true; // nincs szures (a hivo explicit "teljes gomb lathato"-t kert)

            double dx = nodeX - cameraX, dy = nodeY - cameraY, dz = nodeZ - cameraZ;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < 1e-9)
                return true; // a kamera a csomopont belsejeben van - trivialisan "lathato"

            double cosAngle = (dx * forwardX + dy * forwardY + dz * forwardZ) / distance;
            cosAngle = cosAngle < -1.0 ? -1.0 : (cosAngle > 1.0 ? 1.0 : cosAngle);
            double angleToCenter = Math.Acos(cosAngle);
            double angularRadius = Math.Atan2(rTile, distance);

            // GYOKEROK MEGTALALVA (2026-09-01, felhasznaloi /btw-kerdes:
            // "csak azokat a tile-okat kellene kiszamolni, amik latszanak" -
            // ez ravilagitott, hogy a latokup-szures MAGA torott el mely
            // zoomnal, nem csak a levele-korlatok voltak hianyosak). Az
            // angularRadius=atan2(rTile,distance) SZANDEKOSAN hozzaadodik a
            // kuszobhoz (hogy egy nagy, a kup szelen levo csomopont ne
            // essen ki tul koran), DE korabban NEM volt felulrol korlatozva.
            // Ha a kamera nagyon kozel van a felszinhez, egy DURVA (nagy
            // fizikai meretu, pl. base-level) csomopont kozeppontjahoz
            // kepesti tavolsag ossszemerhetove valhat magaval a csomopont
            // meretevel (rTile) - ekkor atan2(rTile,distance) a pi/2-hoz
            // tarthat. A halfFovRadians (~tipikusan 0.3-0.7 rad biztonsagi
            // margoval) + egy ~pi/2-hoz kozeli angularRadius KONNYEN
            // meghaladja pi-t - es mivel angleToCenter maximuma IS pi
            // (Acos ertekkeszlete), a feltetel MINDEN iranyra igazza valik:
            // a latokup-szures TENYLEGESEN MEGSZUNIK, a teljes gomb
            // "lathatonak" szamit. Ez pontosan megmagyarazza, miert
            // ZOOMKOR (kamera kozel a felszinhez) jelentkezett a "cut
            // merete 6291456" (a teljes, szuretlen gomb) - ez volt a
            // GYOKEROK a korabbi vedokorlatok (Visit()/EnforceRestrictedBalance)
            // altal csak TUNETILEG kezelt problemara.
            //
            // JAVITAS: az angularRadius felso korlatja pi/2 - ezen tul a
            // "sajat szogmeret" fogalma mar nem ertelmes (a kamera
            // gyakorlatilag a csomopont befoglalo-gombjan BELUL/azon van),
            // es a MEGENGEDETT kiterjesztes soha nem teheti a teljes
            // osszeget pi-nel nagyobba, ami a teszt ERDEMI (nem degeneralt,
            // mindig-igaz) maradasat garantalja.
            double clampedAngularRadius = Math.Min(angularRadius, Math.PI / 2.0);
            double threshold = Math.Min(halfFovRadians + clampedAngularRadius, Math.PI);
            return angleToCenter <= threshold;
        }

        /// <summary>
        /// Az elozo cut minden levelenek OSSZES ose (a base level-ig) - ez
        /// azoknak a csomopontoknak a halmaza, amik az ELOZO keretben "fel
        /// voltak bontva" (nem maguk voltak a leaf, hanem valamelyik
        /// leszarmazottjuk). Egyszeri, olcso elokeszites (leaf-szam * melyseg),
        /// hogy a bejaras soran O(1) legyen a "wasExpanded" lekerdezes.
        /// </summary>
        private static HashSet<TileId> BuildExpandedAncestorSet(IReadOnlyCollection<TileId> previousCut, int baseLevel)
        {
            var expanded = new HashSet<TileId>();
            if (previousCut == null)
                return expanded;

            foreach (TileId leaf in previousCut)
            {
                TileId current = leaf;
                while (current.Level > baseLevel)
                {
                    current = current.Parent();
                    expanded.Add(current);
                }
            }
            return expanded;
        }

        /// <summary>
        /// A csomopont KOZEPE (TileGeometry.ToPosition szerinti egysegvektor
        /// * planetRadius) es befoglalo sugara (a negy SAROK tavolsaganak
        /// maximuma a kozepponttol). Csak a referencia-gömböt írja le;
        /// a domborzati CPU-út ND-71 óta külön bounds-lekérdezést használ.
        /// </summary>
        public static void GetCenterAndBoundingRadius(
            TileId node, double planetRadius,
            out double centerX, out double centerY, out double centerZ, out double boundingRadius)
        {
            TileGeometry.ToPosition(node, out double cx, out double cy, out double cz);
            centerX = cx * planetRadius;
            centerY = cy * planetRadius;
            centerZ = cz * planetRadius;

            TileGeometry.GetContinuousBounds(node, out double uMin, out double uMax, out double vMin, out double vMax);

            double maxCornerDistSq = 0.0;
            for (int ui = 0; ui < 2; ui++)
            {
                double uc = ui == 0 ? uMin : uMax;
                for (int vi = 0; vi < 2; vi++)
                {
                    double vc = vi == 0 ? vMin : vMax;
                    TileGeometry.PositionFromFaceUV(node.Face, uc, vc, out double px, out double py, out double pz);
                    double dx = px * planetRadius - centerX;
                    double dy = py * planetRadius - centerY;
                    double dz = pz * planetRadius - centerZ;
                    double distSq = dx * dx + dy * dy + dz * dz;
                    if (distSq > maxCornerDistSq) maxCornerDistSq = distSq;
                }
            }
            boundingRadius = Math.Sqrt(maxCornerDistSq);
        }

        /// <summary>
        /// 2:1 kiegyensulyozott (restricted) kvadfa kikenyszeritese a
        /// varratmentes LOD-hatarokhoz (docs/05-milestones.md §9.1): ha egy
        /// aktiv level el-szomszedja TOBB MINT 1 szinttel durvabb, azt a
        /// durvabb szulot kenyszer-felbontjuk (a 4 gyereket teve a cut-ba
        /// helyette) - fixpontig iteralva, mert egy felbontas ujabb, MASIK
        /// szomszednal okozhat egyensulytalansagot. A MAR verifikalt
        /// TileNeighbors tablat hasznalja (M2.4) - nincs uj szomszedsag-
        /// matek. Csak a DURVA oldalt bontjuk fel, a finomat SOSEM vonjuk
        /// ossze - ez garantalja a fixpontig-terminalast (a felbontasok
        /// szama felulrol korlatos: minden csomopont legfeljebb maxLevel-ig
        /// bonthato).
        /// </summary>
        internal static void EnforceRestrictedBalance(HashSet<TileId> cut, int baseLevel, int maxLeafCount = DefaultMaxLeafCount,
            bool strictBudget = false, System.Threading.CancellationToken cancellation = default,
            LodSelectionWork? work = null)
        {
            // MASODIK BIZTONSAGI KORLAT (2026-09-01, HARMADIK kor - a
            // Visit()-beli korlat (ld. ott a doksit) az EnforceRestrictedBalance-t
            // MAGAT nem vedte: ha a Visit()-kori korlat egy SZELSOSEGESEN
            // EGYENETLEN cut-ot hagy hatra (pl. egy level-3 tile kozvetlenul
            // egy level-19 szomszed mellett, mert a globalis szamlalo epp
            // ott allt meg), a 2:1-kiegyensulyozas fixpont-ciklusa ezt akar
            // TOBBSZOR TIZEZERSZER SZETTERJEDO kaszkádban probalhatja
            // kiegyenliteni - a felhasznalonal EZ okozta a "cut merete
            // 6291456" (a TELJES gomb level 10-en, szures nelkul) esetet,
            // MEG A JAVITOTT Visit() UTAN IS. A `cut.Count` felso korlatja
            // itt is: ha a kiegyensulyozas tul messzire vinne a meretet, a
            // ciklus egyszeruen LEALL (a maradek egyenetlenseg VIZUALISAN
            // nem tokeletes - apro varratok lehetnek -, de SOSEM vezet
            // tobbszor-tizmilliós kaszkadhoz).
            int balanceSizeCap = maxLeafCount * 3;

            // ND-116 (2026-09-19): ha a szoros budget MAR belepeskor blokkol,
            // egyetlen felosztas sem tortenhet - a lentebbi ciklus ugyanezt a
            // feltetelt minden split ELOTT ellenorzi, es a `cut.Count` CSAK NO
            // (SplitOnce egyet kivesz, negyet betesz: netto +3). A teljes
            // vegigszkennelesnek tehat nincs kimenete, csak koltsege.
            //
            // EZ A PRODUKCIOS ESET: a telitett vagasnal `cut.Count == budget`,
            // es a felhasznalo 2026-09-18-i naplojaban 68 vagasbol 44 volt
            // ilyen. Merve: 1 kor, 0 split, de |cut| levelet vegigjart -
            // 20 000 levelnel 31 ms, 64 000-nel 78 ms, tiszta veszteseg.
            //
            // A kimenet BIZONYITHATOAN valtozatlan: ugyanaz a feltetel, csak
            // hamarabb ertekelve. A megmarado szintkulonbseget - mint eddig is,
            // amikor a budget blokkolt - a LodCornerResolver illeszti.
            if (strictBudget && cut.Count + 3 > maxLeafCount) return;

            bool changed;
            do
            {
                cancellation.ThrowIfCancellationRequested();
                if (cut.Count > balanceSizeCap)
                    break;
                changed = false;
                work?.CountBalanceIteration(cut.Count);

                // ND-96: minden finom levél már jelölt; a külön jelöltépítés
                // ugyanazt a négy szomszédot kétszer kérdezte le. A base-levél
                // nem mutathat baseLevel alatti aktív ősre, így nem ad új splitet.
                var toSplit = new HashSet<TileId>();
                foreach (TileId leaf in cut)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (leaf.Level <= baseLevel) continue;
                    for (int dirIndex = 0; dirIndex < 4; dirIndex++)
                    {
                        TileId neighbor = TileNeighbors.Neighbor(leaf, (TileDirection)dirIndex);
                        if (TryFindCoveringAncestor(neighbor, cut, out TileId covering)
                            && leaf.Level - covering.Level > 1)
                            toSplit.Add(covering);
                    }
                }

                var orderedSplits = new List<TileId>(toSplit);
                orderedSplits.Sort((a,b) => a.Value.CompareTo(b.Value));
                foreach (TileId ancestor in orderedSplits)
                {
                    cancellation.ThrowIfCancellationRequested();
                    // ND-76: az új munkakeretes út budgetjét a balance sem
                    // lépheti át. A megmaradó szintkülönbséget a resolver illeszti.
                    if (strictBudget && cut.Count + 3 > maxLeafCount) return;
                    // A korlat MID-ITERACIOBAN is ellenorzott (nem csak a
                    // ciklus elejen) - egyetlen iteracio onmagaban is
                    // tobbszorosere nombelheti a cut-ot, ha a toSplit
                    // halmaz nagy, tehat a korai kilepes NELKULOZHETETLEN
                    // a szoros korlathoz.
                    if (cut.Count > balanceSizeCap)
                        break;
                    if (cut.Contains(ancestor))
                    {
                        SplitOnce(cut, ancestor);
                        work?.CountBalanceSplit();
                        changed = true;
                    }
                }
            } while (changed);
        }

        /// <summary>
        /// Megkeresi azt a csomopontot a cut-ban, ami LEFEDI a megadott
        /// (barmilyen szintu) TileId-t - azaz maga a TileId, vagy annak
        /// valamelyik ose. Ha a cut-beli fedes egy LESZARMAZOTTJA (a
        /// szomszed oldala FINOMABB, nem durvabb), a fuggveny false-t ad
        /// vissza - ez a szandekolt viselkedes, ld. az EnforceRestrictedBalance
        /// dokumentaciojat (a finomabb oldal a masik iranybol kerul
        /// ellenorzesre, szimmetrikusan).
        /// </summary>
        private static bool TryFindCoveringAncestor(TileId sameLevelId, HashSet<TileId> cut, out TileId covering)
        {
            TileId current = sameLevelId;
            while (true)
            {
                if (cut.Contains(current))
                {
                    covering = current;
                    return true;
                }
                if (current.Level == 0)
                {
                    covering = default;
                    return false;
                }
                current = current.Parent();
            }
        }

        private static void SplitOnce(HashSet<TileId> cut, TileId node)
        {
            cut.Remove(node);
            for (int i = 0; i < 4; i++)
                cut.Add(node.Child(i));
        }

    }
}
