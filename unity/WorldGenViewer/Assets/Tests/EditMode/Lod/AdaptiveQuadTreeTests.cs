#nullable disable
// A tesztek szandekosan atadnak `null`-t a `previousCut`-nak (elso cut, nincs
// elozo) - nullable-strict forditasnal ez CS8625, ami az EGESZ Unity-projekt
// forditasat blokkolhatja (igy a viewer-valtozasok/uj Inspector-mezok sem
// jelennek meg). A nullable itt kikapcsolva - a null-atadas a SelectCut
// dokumentalt, ervenyes hasznalata.
using System.Collections.Generic;
using NUnit.Framework;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer.Lod.Tests
{
    /// <summary>
    /// M9 (docs/05-milestones.md §9.1 "Kotelezo tesztek") - a kvadfa
    /// kivalasztasi/kiegyensulyozasi logika TISZTA es unit-tesztelheto
    /// resze. Csak <see cref="AdaptiveQuadTree"/> PUBLIKUS API-jat hivja
    /// (BuildCut/SelectCut) - a belso reszleteket (EnforceRestrictedBalance
    /// stb.) fuggetlenul, a sajat (nem ugyanazt a kodutat hasznalo)
    /// ellenorzo logikaval vizsgalja, hogy ne "onmagat tesztelje".
    /// </summary>
    public class AdaptiveQuadTreeTests
    {
        private const double PlanetRadius = 100.0;

        /// <summary>
        /// Regresszio-teszt egy ELES katasztrofara (2026-09-01, felhasznaloi
        /// jelentes): a szogsugar-metrika horizont-kozeli/erintoleges
        /// rálátásnál lancreakcio-szeruen, extrem melysegig finomíthat -
        /// egy valos futasban a cut merete 6 349 914 tile volt egyetlen
        /// ujraepitesnel. A DefaultMaxLeafCount biztonsagi korlatnak
        /// garantalnia kell, hogy ez TOBBE NE fordulhasson elo, barmilyen
        /// (akar szandekosan szelsoseges) kamera-geometria mellett.
        /// </summary>
        [Test]
        public void SafetyCapPreventsRunawayLeafExplosion()
        {
            const int tinyMaxLeafCount = 100;
            // Kamera kozvetlenul a felszin felett, sekely szoggel nezve -
            // pontosan az a fajta "horizont-kozeli, felszin-kozeli" geometria,
            // ami a jelentett katasztrofat kivaltotta.
            double camX = PlanetRadius + 0.05, camY = 0.0, camZ = 0.0;

            HashSet<TileId> cut = AdaptiveQuadTree.SelectCut(
                camX, camY, camZ, PlanetRadius, previousCut: null,
                baseLevel: 4, maxLevel: 20,
                splitThresholdRadians: 0.001, mergeThresholdRadians: 0.0006,
                forwardX: 0.0, forwardY: 1.0, forwardZ: 0.0, // erintolegesen "oldalra" nez, nem a felszin fele
                halfFovRadians: AdaptiveQuadTree.NoCullingHalfFovRadians,
                maxLeafCount: tinyMaxLeafCount);

            // A "lazan szinkronizalt" korlat miatt tobb szal egyszerre atlepheti
            // egy kicsit - de nagysagrendekkel KEVESEBBNEK kell lennie, mint egy
            // valodi robbanas (millios nagysagrend) eseten lenne.
            Assert.Less(cut.Count, tinyMaxLeafCount * 20,
                $"A biztonsagi korlatnak meg kellett volna allitania a finomodast tinyMaxLeafCount={tinyMaxLeafCount} korul, de a cut merete {cut.Count} lett.");
        }

        /// <summary>
        /// MASODIK kor a fenti katasztrofara: a Visit()-korlat onmagaban
        /// NEM volt eleg, mert egy SZELSOSEGESEN EGYENETLEN (a globalis
        /// szamlalo tetszoleges pillanataban "megallitott") cutot a 2:1
        /// kiegyensulyozas fixpont-ciklusa lancreakcio-szeruen felfujhatja -
        /// a felhasznalonal ez UTOLAG, MEG A JAVITOTT Visit() UTAN IS
        /// "cut merete 6291456"-ot (a teljes gomb level 10-en, szures
        /// nelkul) eredmenyezett. Ez a teszt a TELJES BuildCut (SelectCut +
        /// EnforceRestrictedBalance) csővezeteket vizsgalja egyutt, hogy a
        /// masodik korlat (EnforceRestrictedBalance sajat balanceSizeCap-je)
        /// is tartson.
        /// </summary>
        [Test]
        public void SafetyCapPreventsRunawayBalanceCascade()
        {
            const int tinyMaxLeafCount = 100;
            double camX = PlanetRadius + 0.05, camY = 0.0, camZ = 0.0;

            HashSet<TileId> cut = AdaptiveQuadTree.BuildCut(
                camX, camY, camZ, PlanetRadius, previousCut: null,
                baseLevel: 4, maxLevel: 20,
                splitThresholdRadians: 0.001, mergeThresholdRadians: 0.0006,
                forwardX: 0.0, forwardY: 1.0, forwardZ: 0.0,
                halfFovRadians: AdaptiveQuadTree.NoCullingHalfFovRadians,
                maxLeafCount: tinyMaxLeafCount);

            // A kiegyensulyozas a balanceSizeCap-ig (maxLeafCount*3) engedi
            // nőni a cutot, tehat a felso korlat ennek megfeleloen tagabb,
            // mint a SelectCut-only tesztben, de MEG MINDIG nagysagrendekkel
            // kisebb, mint egy valodi (milliós) kaszkad lenne.
            Assert.Less(cut.Count, tinyMaxLeafCount * 100,
                $"A teljes BuildCut (kiegyensulyozassal egyutt) tul nagy cutot adott: {cut.Count}.");
        }

        /// <summary>
        /// A VALODI gyokerok regresszios tesztje (2026-09-01, felhasznaloi
        /// /btw-kerdes nyoman feltarva): az IsWithinViewCone-ban az
        /// angularRadius=atan2(rTile,distance) tag korabban NEM volt felulrol
        /// korlatozva - felszin-kozeli kameranal egy DURVA (nagy fizikai
        /// meretu) csomopontra ez pi/2-hoz tarthatott, es a
        /// halfFovRadians+angularRadius osszeg meghaladhatta pi-t, amitol a
        /// latokup-teszt MINDEN iranyra igazza degeneralodott (a teljes
        /// gomb "lathatonak" szamitott, fuggetlenul a tenyleges nezesi
        /// iranytol). Ez a teszt egy SZUK latoszoggel es a felszinhez
        /// KOZELI kameraval igazolja, hogy a latokup-szures TENYLEGESEN
        /// megkulonboztet - NEM az osszes alap-szintu gyoker kerul a cutba.
        /// </summary>
        [Test]
        public void ViewConeDoesNotDegenerateAtCloseRange()
        {
            const int baseLevel = 3;
            const int maxLevel = 7;
            // Kozvetlenul a felszin felett - ez a "durva csomopont fizikai
            // merete osszemerhetove valik a tavolsaggal" geometria, ami a
            // hibas kepletet korabban degeneralta.
            double camX = PlanetRadius + 0.01, camY = 0.0, camZ = 0.0;
            double fwdX = -1.0, fwdY = 0.0, fwdZ = 0.0; // befele, a felszin fele nez
            const double narrowHalfFov = 0.05; // ~2.9 fok - szandekosan szuk

            HashSet<TileId> cut = AdaptiveQuadTree.SelectCut(
                camX, camY, camZ, PlanetRadius, previousCut: null,
                baseLevel: baseLevel, maxLevel: maxLevel,
                splitThresholdRadians: 0.01, mergeThresholdRadians: 0.006, // agresszivan finomitani "akaro" kuszob
                forwardX: fwdX, forwardY: fwdY, forwardZ: fwdZ,
                halfFovRadians: narrowHalfFov);

            // A base-level particio MINDIG teljes (6*4^baseLevel), tehat a
            // cut sosem lehet ennel kisebb - de ha a latokup-szures
            // MUKODIK, csak a nezesi iranyhoz KOZELI nehany tile finomodik,
            // NEM az egesz gomb egeszen maxLevel-ig. A degeneralt (regi,
            // hibas) kepletttel a teljes gomb finomodott volna maxLevel-ig
            // (6*4^7 = 98304 helyett akar milliós nagysagrend).
            long totalBaseTiles = 6L * (1L << baseLevel) * (1L << baseLevel);
            long fullyRefinedWorstCase = 6L * (1L << maxLevel) * (1L << maxLevel);
            Assert.Less(cut.Count, fullyRefinedWorstCase / 10,
                $"A szuk latokupnek csak a nezesi irany kozeleben levo tile-okat kellett volna finomitania, " +
                $"nem a teljes gombot - cut merete {cut.Count} (alap: {totalBaseTiles}, teljesen finomitva: {fullyRefinedWorstCase} lenne).");
        }

        // Kis level-tartomany a kimerito partício-ellenorzeshez (NoGaps) -
        // level 11-nel ez 4^11 cellat jelentene lapankent, ami tul sok
        // lenne egy unit tesztben materializalni; level 1..5 mar kello
        // melyseget ad a vegyes-felbontasu esetek lefedesehez.
        private const int SmallBaseLevel = 1;
        private const int SmallMaxLevel = 5;

        [Test]
        public void SelectCut_FarCamera_KeepsBaseLevelLeaves()
        {
            // A bolygo kozeppontjatol nagyon messze allo kamera semmilyen
            // csomopontot nem bont fel - a cut pontosan a base-level
            // gyokerekbol all (6 * 4^baseLevel darab).
            HashSet<TileId> cut = AdaptiveQuadTree.SelectCut(
                cameraX: 0, cameraY: 0, cameraZ: 1_000_000.0,
                planetRadius: PlanetRadius,
                previousCut: null,
                baseLevel: 2, maxLevel: 8);

            int expectedRootCount = 6 * (1 << 2) * (1 << 2);
            Assert.AreEqual(expectedRootCount, cut.Count);
            foreach (TileId t in cut)
                Assert.AreEqual(2, t.Level);
        }

        [Test]
        public void BuildCut_Deterministic_SamePositionAndHistoryGivesSameCut()
        {
            var previous = new HashSet<TileId> { TileId.FromFaceLevelUV(0, 2, 1, 1) };

            HashSet<TileId> a = AdaptiveQuadTree.BuildCut(0.3, 0.4, 5.0, PlanetRadius, previous, baseLevel: 2, maxLevel: 6);
            HashSet<TileId> b = AdaptiveQuadTree.BuildCut(0.3, 0.4, 5.0, PlanetRadius, previous, baseLevel: 2, maxLevel: 6);

            Assert.IsTrue(a.SetEquals(b), "Ugyanaz a (kamera, elozo cut) par nem adott bitre ugyanazt a cut-ot.");
        }

        [Test]
        public void BuildCut_Deterministic_ColdStartSamePositionGivesSameCut()
        {
            HashSet<TileId> a = AdaptiveQuadTree.BuildCut(50.0, 10.0, -20.0, PlanetRadius, previousCut: null, baseLevel: 2, maxLevel: 7);
            HashSet<TileId> b = AdaptiveQuadTree.BuildCut(50.0, 10.0, -20.0, PlanetRadius, previousCut: null, baseLevel: 2, maxLevel: 7);

            Assert.IsTrue(a.SetEquals(b), "Hideg inditasnal ugyanaz a pozicio nem adott bitre ugyanazt a cut-ot.");
        }

        [Test]
        public void SelectCut_Hysteresis_StaysExpandedInsideBand_ThenMergesBeyondMergeFactor()
        {
            const int baseLevel = 2;
            const int maxLevel = 6;
            // Szogsugar-kuszobok (screen-space-lod) - a regi tavolsag/meret-
            // arany (splitFactor/mergeFactor) helyett. mergeThreshold < splitThreshold
            // (ld. AdaptiveQuadTree osztaly-doc, forditott irany a regi
            // metrikahoz kepest).
            const double splitThreshold = 0.3;
            const double mergeThreshold = 0.2;

            TileId root = TileId.FromFaceLevelUV(0, baseLevel, 0, 0);
            AdaptiveQuadTree.GetCenterAndBoundingRadius(root, PlanetRadius, out double cx, out double cy, out double cz, out double rTile);

            // Iranyvektor a gomb kozeppontjabol a tile kozepe fele - a
            // kamerat a tile KOZEPPONTJATOL ezen az iranyon (radialisan
            // kifele) mozgatjuk, NEM az origotol mert tavolsagra allitjuk -
            // kulonben a kamera->kozeppont tavolsag |planetRadius-offset|
            // lenne, nem maga az offset (a tile kozeppontja mar eleve
            // planetRadius tavolsagra van az origotol).
            double len = System.Math.Sqrt(cx * cx + cy * cy + cz * cz);
            double dirX = cx / len, dirY = cy / len, dirZ = cz / len;
            void CameraAtOffset(double offset, out double x, out double y, out double z)
            {
                x = cx + dirX * offset; y = cy + dirY * offset; z = cz + dirZ * offset;
            }

            // A tavolsag, amelynel a tile vetitett szogsugara PONTOSAN a
            // megadott kuszobbel egyezik: atan2(rTile,d)=theta <=> d=rTile/tan(theta).
            double DistanceForAngularRadius(double angularRadiusRadians) => rTile / System.Math.Tan(angularRadiusRadians);

            // 1) Nagyon kozel (a split-kuszobnel jovaltal kisebb tavolsag,
            // tehat a szogsugar egyertelmuen meghaladja a splitThreshold-ot)
            // -> felbomlik (a gyoker eltunik a cut-bol).
            double closeDist = 0.5 * DistanceForAngularRadius(splitThreshold);
            CameraAtOffset(closeDist, out double closeX, out double closeY, out double closeZ);
            HashSet<TileId> expanded = AdaptiveQuadTree.SelectCut(
                closeX, closeY, closeZ,
                PlanetRadius, previousCut: null, baseLevel, maxLevel, splitThreshold, mergeThreshold);
            Assert.IsFalse(expanded.Contains(root), "A kozeli kameranak fel kellett volna bontania a gyoker csomopontot.");

            // 2) Olyan tavolsag, ahol a szogsugar PONTOSAN a ket kuszob
            // KOZOTT van (a hiszterezis-savban) - mivel az ELOZO keretben
            // fel volt bontva, a kisebb K_merge kuszobot kell hasznalnia
            // (meg mindig "felbontast igenyel"-nek szamit), tehat MEG NEM
            // vonja ossze (nincs oszcillacio).
            double midThreshold = (splitThreshold + mergeThreshold) / 2.0;
            double bandDist = DistanceForAngularRadius(midThreshold);
            CameraAtOffset(bandDist, out double bandX, out double bandY, out double bandZ);
            HashSet<TileId> stillExpanded = AdaptiveQuadTree.SelectCut(
                bandX, bandY, bandZ,
                PlanetRadius, previousCut: expanded, baseLevel, maxLevel, splitThreshold, mergeThreshold);
            Assert.IsFalse(stillExpanded.Contains(root),
                "A hiszterezis-savon belul a csomopontnak FELBONTVA kellett volna maradnia (nincs azonnali osszevonas).");

            // 3) Ugyanez a tavolsag, de HIDEG inditassal (nincs elozmeny) -
            // ekkor a NAGYOBB K_split kuszobot hasznalja, es mivel a
            // szogsugar (midThreshold) MAR KISEBB, mint splitThreshold, NEM
            // bontja fel - ez mutatja meg, hogy a hiszterezis valoban az
            // ELOZMENYTOL fugg, nem csak a tavolsagtol/mereytol.
            HashSet<TileId> coldAtBand = AdaptiveQuadTree.SelectCut(
                bandX, bandY, bandZ,
                PlanetRadius, previousCut: null, baseLevel, maxLevel, splitThreshold, mergeThreshold);
            Assert.IsTrue(coldAtBand.Contains(root),
                "Hideg inditasnal a savon beluli tavolsagnak MAR NEM kellett volna felbontania a csomopontot.");

            // 4) A merge-kuszobon TUL (szogsugar < mergeThreshold) -> vegre
            // osszevonodik.
            double farDist = 1.1 * DistanceForAngularRadius(mergeThreshold);
            CameraAtOffset(farDist, out double farX, out double farY, out double farZ);
            HashSet<TileId> merged = AdaptiveQuadTree.SelectCut(
                farX, farY, farZ,
                PlanetRadius, previousCut: expanded, baseLevel, maxLevel, splitThreshold, mergeThreshold);
            Assert.IsTrue(merged.Contains(root), "A merge-kuszobon tul a csomopontnak ossze kellett volna vonodnia.");
        }

        [Test]
        public void BuildCut_RestrictedBalance_NeighborLevelDifferenceAtMostOne()
        {
            // Vegyes felbontast kikenyszerito kamera-pozicio: kozel az egyik
            // level-1 gyokerhez, hogy annak kornyeke felbomoljon, a tavoli
            // resz pedig durva maradjon - pontosan az az eset, ahol a 2:1
            // egyensuly nelkul repedes keletkezne.
            TileId nearRoot = TileId.FromFaceLevelUV(0, SmallBaseLevel, 0, 0);
            AdaptiveQuadTree.GetCenterAndBoundingRadius(nearRoot, PlanetRadius, out double cx, out double cy, out double cz, out _);

            HashSet<TileId> cut = AdaptiveQuadTree.BuildCut(
                cx, cy, cz, PlanetRadius, previousCut: null,
                baseLevel: SmallBaseLevel, maxLevel: SmallMaxLevel,
                splitThresholdRadians: 0.4, mergeThresholdRadians: 0.27);

            Assert.Greater(cut.Count, 6 * (1 << SmallBaseLevel) * (1 << SmallBaseLevel),
                "A tesztnek tenylegesen vegyes felbontast kell eloallitania, kulonben az egyensuly-teszt ures.");

            foreach (TileId leaf in cut)
            {
                for (int dirIndex = 0; dirIndex < 4; dirIndex++)
                {
                    TileId sameLevelNeighbor = TileNeighbors.Neighbor(leaf, (TileDirection)dirIndex);
                    int? neighborCoveringLevel = FindCoveringLevelIndependently(sameLevelNeighbor, cut);
                    if (neighborCoveringLevel.HasValue)
                    {
                        int diff = leaf.Level - neighborCoveringLevel.Value;
                        Assert.LessOrEqual(diff, 1,
                            $"Level-kulonbseg {leaf} es a szomszedos fedo csomopont (level {neighborCoveringLevel}) kozott: {diff} > 1.");
                    }
                    // Ha nincs fedo os a cut-ban, a szomszed oldala FINOMABB -
                    // ezt a masik iranybol, a finomabb level ellenorzese
                    // fedi le (szimmetrikus bejaras az osszes leaf-en).
                }
            }
        }

        [Test]
        public void BuildCut_IsExactPartitionOfTheSphere_NoGapsNoOverlap()
        {
            // Ugyanaz az elv, mint az M2 NoGaps tesztje (docs/05-milestones.md
            // §2.4), csak adaptiv (vegyes szintu) cut-ra: minden level-eket
            // egy KOZOS referencia-szintre (SmallMaxLevel) bontva a
            // lefedett cellak halmazanak pontosan az egesz gombot kell
            // adnia, atfedes nelkul.
            TileId nearRoot = TileId.FromFaceLevelUV(2, SmallBaseLevel, 0, 0);
            AdaptiveQuadTree.GetCenterAndBoundingRadius(nearRoot, PlanetRadius, out double cx, out double cy, out double cz, out _);

            HashSet<TileId> cut = AdaptiveQuadTree.BuildCut(
                cx, cy, cz, PlanetRadius, previousCut: null,
                baseLevel: SmallBaseLevel, maxLevel: SmallMaxLevel,
                splitThresholdRadians: 0.4, mergeThresholdRadians: 0.27);

            var coveredCells = new HashSet<(int Face, int U, int V)>();
            long totalCellsCovered = 0;
            foreach (TileId leaf in cut)
            {
                int cellSpan = 1 << (SmallMaxLevel - leaf.Level);
                leaf.GetUV(out uint leafU, out uint leafV);
                int baseCellU = (int)leafU * cellSpan;
                int baseCellV = (int)leafV * cellSpan;

                for (int du = 0; du < cellSpan; du++)
                {
                    for (int dv = 0; dv < cellSpan; dv++)
                    {
                        var cell = (leaf.Face, baseCellU + du, baseCellV + dv);
                        Assert.IsTrue(coveredCells.Add(cell), $"Atfedes talalva a {cell} cellan.");
                        totalCellsCovered++;
                    }
                }
            }

            long expectedTotal = 6L * (1L << SmallMaxLevel) * (1L << SmallMaxLevel);
            Assert.AreEqual(expectedTotal, totalCellsCovered, "A cut nem fedi le pontosan a teljes gombot (hezag vagy tullefedes).");
        }

        [Test]
        public void SelectCut_Frustum_LookingAway_StaysCoarse_EvenIfClose()
        {
            // M9 utolagos kiegeszites: a tavolsag-kriterium ONMAGABAN nem
            // eleg - egy a kamera MOGOTT/mellett levo, de tavolsag szerint
            // "kozeli" csomopont nem finomodhat, ha a kamera nem arra nez
            // (kulonben a kamera KORULI teljes gomb finomodna, fuggetlenul
            // attol, mi latszik a kepernyon - ez okozott korabban
            // hasznalhatatlanul nagy tile-szamot kozeli zoomnal).
            TileId root = TileId.FromFaceLevelUV(0, 2, 0, 0);
            AdaptiveQuadTree.GetCenterAndBoundingRadius(root, PlanetRadius, out double cx, out double cy, out double cz, out double rTile);
            double len = System.Math.Sqrt(cx * cx + cy * cy + cz * cz);
            double dirX = cx / len, dirY = cy / len, dirZ = cz / len;
            const double splitThreshold = 0.3;
            const double mergeThreshold = 0.2;
            double closeDist = 0.5 * (rTile / System.Math.Tan(splitThreshold));
            double camX = cx + dirX * closeDist, camY = cy + dirY * closeDist, camZ = cz + dirZ * closeDist;

            // Kamera "elfele" nez a bolygotol (kifele, +dir) - a tile a
            // latokupon KIVUL esik, annak ellenere, hogy meret szerint
            // felbontando lenne.
            HashSet<TileId> awayCut = AdaptiveQuadTree.SelectCut(
                camX, camY, camZ, PlanetRadius, previousCut: null,
                baseLevel: 2, maxLevel: 8, splitThresholdRadians: splitThreshold, mergeThresholdRadians: mergeThreshold,
                forwardX: dirX, forwardY: dirY, forwardZ: dirZ, halfFovRadians: 0.2);
            Assert.IsTrue(awayCut.Contains(root),
                "A kamerat61 elfele eso (a latokupon kivuli) csomopontnak durvanak kellett volna maradnia.");

            // Ugyanaz a pozicio, de a kamera A BOLYGO/tile fele nez (befele,
            // -dir) - ekkor a meret-kriterium ES a latokup egyutt mar
            // felbontja.
            HashSet<TileId> towardCut = AdaptiveQuadTree.SelectCut(
                camX, camY, camZ, PlanetRadius, previousCut: null,
                baseLevel: 2, maxLevel: 8, splitThresholdRadians: splitThreshold, mergeThresholdRadians: mergeThreshold,
                forwardX: -dirX, forwardY: -dirY, forwardZ: -dirZ, halfFovRadians: 0.5);
            Assert.IsFalse(towardCut.Contains(root),
                "A latokupon beluli, kozeli csomopontnak fel kellett volna bontania.");
        }

        [Test]
        public void SelectCut_Frustum_DefaultNoCulling_MatchesFullSphereBehavior()
        {
            // Visszafele-kompatibilitas: ha a hivo nem ad meg latokupot
            // (a regi API-hasznalat), a viselkedesnek valtozatlannak kell
            // maradnia - a teljes gomb "lathatonak" szamit.
            HashSet<TileId> cut = AdaptiveQuadTree.SelectCut(
                0, 0, 1_000_000.0, PlanetRadius, previousCut: null, baseLevel: 2, maxLevel: 8);
            Assert.AreEqual(6 * 4 * 4, cut.Count);
        }

        /// <summary>
        [Test]
        public void BuildCut_HighBaseLevel_AllRootsPresentAndBalanced()
        {
            // INCREMENTAL-MESH-BUFFERS: adaptiveBaseLevel=8 (393216 gyoker)
            // tamogatasahoz az AdaptiveQuadTree.SelectCut/EnforceRestrictedBalance
            // egy OLCSO elozetes tavolsag-szures utan kihagyja a draga
            // GetCenterAndBoundingRadius/TileNeighbors kiertekelest a
            // egyertelmuen tavoli, nem finomodo gyokerekre - ez a teszt azt
            // ellenorzi, hogy ez az eloszures NEM hagy ki egyetlen base-
            // level gyokeret sem a vegso cut-bol, es a 2:1 egyensuly
            // tovabbra is sertetlen marad ezen a leptéken.
            const int baseLevel = 8;
            HashSet<TileId> cut = AdaptiveQuadTree.BuildCut(
                100.3, 30.1, 95.0, PlanetRadius, previousCut: null,
                baseLevel: baseLevel, maxLevel: 16, splitThresholdRadians: 0.05, mergeThresholdRadians: 0.033);

            long expectedBaseCount = 6L * (1L << baseLevel) * (1L << baseLevel);
            int atLeastBase = 0;
            foreach (TileId leaf in cut)
                if (leaf.Level >= baseLevel) atLeastBase++;
            Assert.AreEqual(cut.Count, atLeastBase, "Minden cut-elemnek legalabb baseLevel szintunek kell lennie.");
            Assert.GreaterOrEqual(cut.Count, expectedBaseCount,
                "Az olcso eloszures nem hagyhat ki egyetlen base-level gyokeret sem a vegso cut-bol.");

            foreach (TileId leaf in cut)
            {
                for (int dirIndex = 0; dirIndex < 4; dirIndex++)
                {
                    TileId sameLevelNeighbor = TileNeighbors.Neighbor(leaf, (TileDirection)dirIndex);
                    int? neighborCoveringLevel = FindCoveringLevelIndependently(sameLevelNeighbor, cut);
                    if (neighborCoveringLevel.HasValue)
                        Assert.LessOrEqual(leaf.Level - neighborCoveringLevel.Value, 1,
                            $"Level-kulonbseg {leaf} es a szomszedos fedo csomopont (level {neighborCoveringLevel}) kozott.");
                }
            }
        }

        /// <summary>
        /// Regresszio-teszt egy 2026-09-02-i felhasznaloi jelentesre: a
        /// "Use Gpu Geometry" bekapcsolasa utan is "cut merete 6291456"
        /// (=6*4^10, a TELJES, szuretlen level-10 gomb) jelentkezett kozeli
        /// zoomnal, holott az IsWithinViewCone-fix (8480cbe) es a ket
        /// biztonsagi korlat (Visit()/EnforceRestrictedBalance) mar korabban
        /// bekerult. Ez a teszt a PlanetGridMesh.RecomputeCutAndRebuildAdaptiveMesh
        /// VALODI hivasi parametereivel (radius=100, adaptiveBaseLevel=8,
        /// adaptiveMaxLevel=20, targetTilePixelSize=48, mergeHysteresisFactor=1.5,
        /// fovSafetyMargin=1.3, PlanetOrbitCamera.minDistance=100.1, egyenesen
        /// lefele nezo kamera) reprodukalja a jelenetet - EGY ALLAPOTBOL SEM
        /// (hideg inditas, fokozatos zoom-szekvencia, VAGY egy szintetikus,
        /// MAR degeneralt 6 291 456 elemu previousCut-bol inditva) sikerult a
        /// jelenlegi kodmentessel visszakapni a katasztrofalis meretet - a
        /// legrosszabb eset is stabilan ~400 000 korul marad. Ha ez a teszt
        /// egyszer piros lenne, az azt jelentene, hogy a kod TENYLEGESEN
        /// regresszalt (nem csak elavult Unity-forditas okozta a
        /// felhasznaloi jelenetet - ld. history/2026-09-02-session.md).
        /// </summary>
        [Test]
        public void RealCallSiteParameters_NeverReachesReportedCatastrophicSize()
        {
            const double radius = 100.0;
            const int baseLevel = 8;
            const int maxLevel = 20;

            double verticalFovRad = 60.0 * System.Math.PI / 180.0;
            double aspect = 16.0 / 9.0;
            double horizontalFovRad = 2.0 * System.Math.Atan(System.Math.Tan(verticalFovRad / 2.0) * aspect);
            double halfFovRadians = System.Math.Max(verticalFovRad, horizontalFovRad) / 2.0 * 1.3;
            double pixelsPerRadian = 1080.0 / verticalFovRad;
            double splitThresholdRadians = (48.0 / 2.0) / pixelsPerRadian;
            double mergeThresholdRadians = splitThresholdRadians / 1.5;

            const long reportedCatastrophicSize = 6_291_456L; // = 6*4^10, a teljes szuretlen gomb
            long sanityCeiling = reportedCatastrophicSize / 4;

            // 1) Hideg inditas, kozvetlenul a minDistance-en, egyenesen lefele nezve.
            HashSet<TileId> coldStart = AdaptiveQuadTree.BuildCut(
                0, 0, 100.1, radius, previousCut: null,
                baseLevel, maxLevel, splitThresholdRadians, mergeThresholdRadians,
                0, 0, -1, halfFovRadians);
            Assert.Less(coldStart.Count, sanityCeiling,
                $"Hideg inditas: cut.Count={coldStart.Count:N0} tul kozel a jelentett katasztrofalis merethez.");

            // 2) Szintetikus, MAR degeneralt (teljes level-10 gomb) previousCut -
            // a hiszterezis NE tartsa fenn a degeneralt allapotot.
            var staleFullSphere = new HashSet<TileId>();
            uint n = 1u << 10;
            for (int face = 0; face < 6; face++)
                for (uint u = 0; u < n; u++)
                    for (uint v = 0; v < n; v++)
                        staleFullSphere.Add(TileId.FromFaceLevelUV(face, 10, u, v));
            Assert.AreEqual(reportedCatastrophicSize, staleFullSphere.Count, "Onellenorzes: a szintetikus previousCut valoban a jelentett katasztrofalis meret.");

            HashSet<TileId> recovered = AdaptiveQuadTree.BuildCut(
                0, 0, 100.1, radius, staleFullSphere,
                baseLevel, maxLevel, splitThresholdRadians, mergeThresholdRadians,
                0, 0, -1, halfFovRadians);
            Assert.Less(recovered.Count, sanityCeiling,
                $"Degeneralt previousCut-bol EGY tovabbi BuildCut utan is: cut.Count={recovered.Count:N0} - a hiszterezis fenntartja a katasztrofat.");
        }

        /// <summary>
        /// Regresszio-teszt ND-46-ra (2026-09-02, felhasznaloi screenshot-
        /// diagnozis: egy vizszintes SAV finomodott a kepernyon, felette/
        /// alatta durva tile-ok maradtak). A gyokerok: az atan2(rTile,distance)
        /// szogsugar-metrika a nezesi SZOGET (surolo vs elolnezeti ralatas)
        /// nem vette figyelembe - kozeli, felszin-tapado kameranal a horizont-
        /// kozeli terep egyenes-vonalu tavolsaga aranytalanul nagy, holott meg
        /// jelentos kepernyo-teruletet foglal el.
        ///
        /// Ez a teszt EGYENESEN A JAVITAS ELOTTI (2026-09-02-i elso korbeli)
        /// ELO DIAG-ertekekkel reprodukalja a jelenetet: azzal a pontos
        /// kamera-pozicioval/nezesi-iranyal, ami a javitas ELOTT
        /// cut.Count=393651-et adott (=~ 435 tile-lal a base felett, level 9-ig
        /// terjedve) - a javitas UTAN ennel TOBB es MELYEBB finomodast varunk
        /// (a surolo szogu, tavoli, de meg latott terulet mostantol korrigalt
        /// szogsugarral dol).
        ///
        /// A HATARERTEKEK a MinUsefulCosGrazing aktualis (2026-09-02, negyedik
        /// kor, felhasznaloi keresre 10°-ra szigoritott: `cos(10°)≈0.9848`)
        /// ertekehez vannak hangolva - EZEN a szigorusagi szinten a korrekcio
        /// EBBEN a konkret (szelsosegesen felszin-kozeli, ~118 egyseg
        /// tavolsagu de erintoleges nezetu) kameraallasban MAR CSAK MINIMALIS
        /// hatasu (a korabbi 30°-nal meg ~2600, 10°-nal mar csak par tucat
        /// tobblet-tile) - ld. a MinUsefulCosGrazing doksijat a
        /// "tile-kozeppont-alapu szog-szamitas szelsoseges kozelsegnel"
        /// jelensegrol. A teszt ezert csak azt igazolja, hogy a mechanizmus
        /// EGYALTALAN aktiv (nem teljesen null-hatasu), NEM azt, hogy
        /// erdemi/mely finomitast ad EBBEN a konkret esetben - ha 10°-nal
        /// szigorubb ertek kerul be, ELOSZOR ellenorizd a MinUsefulCosGrazing
        /// aktualis erteket, mielott hibat feltetelezel.
        /// </summary>
        [Test]
        public void GrazingAngleCorrection_RefinesFarButStillVisibleTerrain()
        {
            const double radius = 100.0;
            const int baseLevel = 8;
            const int maxLevel = 20;
            const double camX = 43.425, camY = -79.359, camZ = 43.393;
            const double fwdX = -0.433, fwdY = 0.791, fwdZ = -0.432;
            const double halfFovRadians = 1.18306;
            const double splitThresholdRadians = 0.012693;
            const double mergeThresholdRadians = 0.008462;

            HashSet<TileId> cut = AdaptiveQuadTree.BuildCut(
                camX, camY, camZ, radius, previousCut: null,
                baseLevel, maxLevel, splitThresholdRadians, mergeThresholdRadians,
                fwdX, fwdY, fwdZ, halfFovRadians);

            long baseOnlyCount = 6L * (1L << baseLevel) * (1L << baseLevel);
            int maxLevelReached = baseLevel;
            foreach (TileId leaf in cut)
                if (leaf.Level > maxLevelReached) maxLevelReached = leaf.Level;

            Assert.Greater(cut.Count, baseOnlyCount,
                $"A nezesi-szog-korrekcio mechanizmusanak MEG MINDIG aktivnak kell lennie (legalabb 1 tile-lal tobb, mint a puszta base-level {baseOnlyCount:N0}) - kapott: {cut.Count:N0}.");
            Assert.GreaterOrEqual(maxLevelReached, 9,
                $"A korrekcionak legalabb a base-level (8) folotti szintre kellene juttatnia valamennyi tile-t - elert legmelyebb szint: {maxLevelReached}.");
        }

        /// <summary>
        /// ND-46, otodik kor: felhasznaloi keres ("barmely zoom eseten a
        /// lathato pixelek/ivszog max fele kerulne negyedelesre") - egy FIX
        /// szog nem tudta ezt garantalni, ezert a kuszob most a kamera
        /// AKTUALIS tavolsagabol szamolodik (ComputeHalfArcMinUsefulCosGrazing).
        /// Ez a teszt a levezetett zart formula hatarertekeit es monoton
        /// viselkedeset ellenorzi FUGGETLENUL a BuildCut-tol.
        /// </summary>
        [Test]
        public void ComputeHalfArcMinUsefulCosGrazing_IsValidAndMonotonicAcrossZoom()
        {
            const double radius = 100.0;

            // Minden ertelmes tavolsagra (100.1-tol 10000-ig) a kapott
            // koszinusznak (0,1) tartomanyban kell lennie - SOHA nem lehet
            // 0 (ami vegtelen korrekcios szorzot adna) vagy 1/annal nagyobb
            // (ami gyakorlatilag kikapcsolna a korrekciot).
            double[] distances = { 100.1, 105, 120, 150, 200, 300, 1000, 10000 };
            double previousCos = double.NaN;
            foreach (double d in distances)
            {
                double cos = AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(d, radius);
                Assert.Greater(cos, 0.0, $"d={d}: a kuszobnek pozitivnak kell lennie.");
                Assert.Less(cos, 1.0, $"d={d}: a kuszobnek 1-nel kisebbnek kell lennie (kulonben semmi sem finomodhatna).");

                // MONOTON viselkedes: minel KOZELEBB van a kamera (kisebb d),
                // annal MEGENGEDOBB (KISEBB cosPhiHalf) kuszobnek kell lennie
                // (a `cosGrazing &lt;= minUsefulCosGrazing` felteteles minel
                // kisebb `minUsefulCosGrazing` mellett minel ritkabban utasit
                // el) - ld. a fuggveny doksijaban levezetett geometriai
                // indoklast (kozeli kameranal mar egy kicsi tavolodas is
                // nagy szog-elterest okoz, tehat a kuszobnek engednie kell).
                // Iteracios sorrendunk NOVEKVO tavolsag szerint halad, tehat
                // a cos-nak SZIGORUAN NONIE kell minden lepesnel.
                if (!double.IsNaN(previousCos))
                    Assert.Greater(cos, previousCos, $"d={d}: a kuszobnek szigoruan nonie kellene a tavolsaggal (tavolabb = szigorubb).");
                previousCos = cos;
            }
        }

        /// <summary>
        /// Degeneralt bemenetek (kamera a felszinen/alatta, vagy ervenytelen
        /// sugar) eseten a fuggvenynek biztonsagosan a statikus alapertelmezesre
        /// kell visszaesnie, NEM szabad NaN-t/vegtelent/kivetelt adnia.
        /// </summary>
        [Test]
        public void ComputeHalfArcMinUsefulCosGrazing_DegenerateInputsFallBackSafely()
        {
            const double radius = 100.0;
            Assert.AreEqual(AdaptiveQuadTree.DefaultMinUsefulCosGrazing,
                AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(radius, radius));
            Assert.AreEqual(AdaptiveQuadTree.DefaultMinUsefulCosGrazing,
                AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(radius * 0.5, radius));
            Assert.AreEqual(AdaptiveQuadTree.DefaultMinUsefulCosGrazing,
                AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(200.0, 0.0));
        }

        // ================= FAZIS 1 (ND-47): bejaras levalasztasa a base-rol =================
        // A traversalRootLevel (alacsony induló szint) + staticBaseLevel (a statikus
        // reteg fedi a base-t es alatta) parameterek uj modja. A tesztek a
        // PlanetGridMesh valos hivasi profiljahoz (radius=100, base=8, max=20,
        // 48px cel, ~60fok FOV) hasonlo parametereket hasznalnak.

        private const int Phase1Base = 8;
        private const int Phase1Max = 20;
        private const int Phase1RootLevel = 3;
        // ~48px cel, 60fok FOV, 1080p -> screen-space szogsugar-kuszob.
        private const double Phase1Split = (48.0 / 2.0) / (1080.0 / (60.0 * System.Math.PI / 180.0));
        private const double Phase1Merge = Phase1Split / 1.5;

        private static HashSet<TileId> Phase1BuildCut(double cx, double cy, double cz, double minGraze, double halfFov = System.Math.PI)
        {
            return AdaptiveQuadTree.BuildCut(
                cx, cy, cz, PlanetRadius, previousCut: null,
                Phase1Base, Phase1Max, Phase1Split, Phase1Merge,
                0, 0, -1, halfFov, maxLeafCount: AdaptiveQuadTree.DefaultMaxLeafCount,
                minUsefulCosGrazing: minGraze,
                traversalRootLevel: Phase1RootLevel, staticBaseLevel: Phase1Base);
        }

        [Test]
        public void Phase1_FarCamera_EmptyDynamicCut()
        {
            // Tavoli kamera: minden a base-kuszob alatt van -> a STATIKUS reteg
            // fedi, a dinamikus cut URES (a bejaras a base fole nem finomit).
            HashSet<TileId> cut = Phase1BuildCut(0, 0, 1_000_000.0, 0.5, halfFov: 1.2);
            Assert.AreEqual(0, cut.Count, $"Tavoli kameranal a dinamikus cut-nak uresnek kell lennie, kapott: {cut.Count}.");
        }

        [Test]
        public void Phase1_CloseCamera_AllAboveBase_AndReachesDepth()
        {
            double camDist = PlanetRadius + 0.6; // kozel a felszinhez
            double graze = AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(camDist, PlanetRadius);
            HashSet<TileId> cut = Phase1BuildCut(0, 0, camDist, graze, halfFov: 1.13);

            Assert.Greater(cut.Count, 0, "Kozeli kameranal a dinamikus cut nem lehet ures.");
            foreach (TileId t in cut)
                Assert.Greater(t.Level, Phase1Base, $"Minden dinamikus level a base ({Phase1Base}) FOLOTT kell legyen, kapott: {t.Level}.");
            int maxLevel = 0;
            foreach (TileId t in cut) if (t.Level > maxLevel) maxLevel = t.Level;
            Assert.GreaterOrEqual(maxLevel, Phase1Base + 3, $"A kozeli zoomnak legalabb base+3 szintig kellene finomitania, elert: {maxLevel}.");

            // Korlatos (NEM a 393216 base-gyokerhez kotve).
            Assert.Less(cut.Count, 100_000, $"A screen-space cut-nak korlatosnak kell lennie, kapott: {cut.Count}.");
        }

        [Test]
        public void Phase1_Deterministic_SamePositionGivesSameCut()
        {
            double camDist = PlanetRadius + 0.6;
            double graze = AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(camDist, PlanetRadius);
            HashSet<TileId> a = Phase1BuildCut(0, 0, camDist, graze, halfFov: 1.13);
            HashSet<TileId> b = Phase1BuildCut(0, 0, camDist, graze, halfFov: 1.13);
            Assert.IsTrue(a.SetEquals(b), "Az uj mod nem determinisztikus: ugyanaz a kamera nem adott bitre ugyanazt a cut-ot.");
        }

        [Test]
        public void Phase1_NoAncestorDescendantOverlap()
        {
            // Ket quadfa-tile PONTOSAN akkor fed at, ha egyik a masik ose. A
            // cut-ban EGYETLEN ilyen par sem lehet.
            double camDist = PlanetRadius + 0.6;
            double graze = AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(camDist, PlanetRadius);
            HashSet<TileId> cut = Phase1BuildCut(0, 0, camDist, graze, halfFov: 1.13);

            foreach (TileId leaf in cut)
            {
                TileId a = leaf;
                while (a.Level > 0)
                {
                    a = a.Parent();
                    Assert.IsFalse(cut.Contains(a), $"Atfedes: {leaf} es annak ose {a} is a cut-ban van.");
                }
            }
        }

        [Test]
        public void Phase1_DynamicNeighborBalance_AtMostOneLevel()
        {
            // A dinamikus MESH varratmentessegehez a dinamikus<->dinamikus
            // szomszedok legfeljebb 1 szint kulonbseguek. (A dinamikus<->statikus
            // hatart NEM koveteljuk: a statikus base mindig fed, lyuk nincs -
            // ld. ND-47, a vizualis simitas Fazis 5.)
            double camDist = PlanetRadius + 0.6;
            double graze = AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(camDist, PlanetRadius);
            HashSet<TileId> cut = Phase1BuildCut(0, 0, camDist, graze, halfFov: 1.13);

            foreach (TileId leaf in cut)
            {
                for (int d = 0; d < 4; d++)
                {
                    TileId nb = TileNeighbors.Neighbor(leaf, (TileDirection)d);
                    int? covLevel = FindCoveringLevelIndependently(nb, cut);
                    if (covLevel.HasValue)
                        Assert.LessOrEqual(leaf.Level - covLevel.Value, 1,
                            $"Dinamikus szomszed-szintkulonbseg {leaf} (level {leaf.Level}) es a fedo (level {covLevel}) kozott > 1.");
                }
            }
        }

        [Test]
        public void Phase2_BudgetLimited_DeterministicAndBounded()
        {
            // FAZIS 2 (ND-47): prioritasos koltsegvetes. Egy szuk budget mellett
            // a cut a budget korul marad, determinisztikus (nem szal-ütemezes-
            // fuggo), es minden level>base.
            double camDist = PlanetRadius + 0.6;
            double graze = AdaptiveQuadTree.ComputeHalfArcMinUsefulCosGrazing(camDist, PlanetRadius);
            const int budget = 4000;
            HashSet<TileId> a = AdaptiveQuadTree.BuildCut(
                0, 0, camDist, PlanetRadius, previousCut: null,
                Phase1Base, Phase1Max, Phase1Split, Phase1Merge,
                0, 0, -1, 1.13, maxLeafCount: budget, minUsefulCosGrazing: graze,
                traversalRootLevel: Phase1RootLevel, staticBaseLevel: Phase1Base);
            HashSet<TileId> b = AdaptiveQuadTree.BuildCut(
                0, 0, camDist, PlanetRadius, previousCut: null,
                Phase1Base, Phase1Max, Phase1Split, Phase1Merge,
                0, 0, -1, 1.13, maxLeafCount: budget, minUsefulCosGrazing: graze,
                traversalRootLevel: Phase1RootLevel, staticBaseLevel: Phase1Base);

            Assert.IsTrue(a.SetEquals(b), "A budget-korlatos prioritasos kivalasztas nem determinisztikus.");
            Assert.Less(a.Count, budget * 3, $"A cut-nak a budget nagysagrendjeben kell maradnia, kapott: {a.Count}.");
            foreach (TileId t in a)
                Assert.Greater(t.Level, Phase1Base, "Minden dinamikus level a base folott kell legyen budget-korlat alatt is.");
        }

        /// <summary>
        /// A cut-ban levo, a megadott (barmilyen szintu) TileId-t lefedo
        /// csomopont szintje - FUGGETLEN ujraimplementacio (nem hivja az
        /// AdaptiveQuadTree belso EnforceRestrictedBalance-et), hogy a
        /// balance-teszt tenylegesen a kimenetet ellenorizze, ne onmagat.
        /// </summary>
        private static int? FindCoveringLevelIndependently(TileId sameLevelId, HashSet<TileId> cut)
        {
            TileId current = sameLevelId;
            while (true)
            {
                if (cut.Contains(current))
                    return current.Level;
                if (current.Level == 0)
                    return null;
                current = current.Parent();
            }
        }
    }
}
