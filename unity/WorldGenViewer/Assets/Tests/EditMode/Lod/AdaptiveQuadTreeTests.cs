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
            const double splitFactor = 2.5;
            const double mergeFactor = splitFactor * 1.5;

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

            // 1) Nagyon kozel -> felbomlik (a gyoker eltunik a cut-bol).
            double closeDist = 0.5 * splitFactor * rTile;
            CameraAtOffset(closeDist, out double closeX, out double closeY, out double closeZ);
            HashSet<TileId> expanded = AdaptiveQuadTree.SelectCut(
                closeX, closeY, closeZ,
                PlanetRadius, previousCut: null, baseLevel, maxLevel, splitFactor, mergeFactor);
            Assert.IsFalse(expanded.Contains(root), "A kozeli kameranak fel kellett volna bontania a gyoker csomopontot.");

            // 2) Kisse tavolabb, de MEG a hiszterezis-savon BELUL (split es
            // merge kuszob KOZOTT) - mivel az ELOZO keretben fel volt
            // bontva, a nagyobb K_merge kuszobot kell hasznalnia, tehat
            // MEG NEM vonja ossze (nincs oszcillacio).
            double bandDist = (splitFactor + mergeFactor) / 2.0 * rTile;
            CameraAtOffset(bandDist, out double bandX, out double bandY, out double bandZ);
            HashSet<TileId> stillExpanded = AdaptiveQuadTree.SelectCut(
                bandX, bandY, bandZ,
                PlanetRadius, previousCut: expanded, baseLevel, maxLevel, splitFactor, mergeFactor);
            Assert.IsFalse(stillExpanded.Contains(root),
                "A hiszterezis-savon belul a csomopontnak FELBONTVA kellett volna maradnia (nincs azonnali osszevonas).");

            // 3) Ugyanez a tavolsag, de HIDEG inditassal (nincs elozmeny) -
            // ekkor a kisebb K_split kuszobot hasznalja, es mivel bandDist
            // > splitFactor*rTile, MAR NEM bontja fel - ez mutatja meg,
            // hogy a hiszterezis valoban az ELOZMENYTOL fugg, nem csak a
            // tavolsagtol.
            HashSet<TileId> coldAtBand = AdaptiveQuadTree.SelectCut(
                bandX, bandY, bandZ,
                PlanetRadius, previousCut: null, baseLevel, maxLevel, splitFactor, mergeFactor);
            Assert.IsTrue(coldAtBand.Contains(root),
                "Hideg inditasnal a savon beluli tavolsagnak MAR NEM kellett volna felbontania a csomopontot.");

            // 4) A merge-kuszobon TUL -> vegre osszevonodik.
            double farDist = 1.1 * mergeFactor * rTile;
            CameraAtOffset(farDist, out double farX, out double farY, out double farZ);
            HashSet<TileId> merged = AdaptiveQuadTree.SelectCut(
                farX, farY, farZ,
                PlanetRadius, previousCut: expanded, baseLevel, maxLevel, splitFactor, mergeFactor);
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
                splitFactor: 1.8, mergeFactor: 2.7);

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
                splitFactor: 1.8, mergeFactor: 2.7);

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
            double closeDist = 0.5 * 2.5 * rTile;
            double camX = cx + dirX * closeDist, camY = cy + dirY * closeDist, camZ = cz + dirZ * closeDist;

            // Kamera "elfele" nez a bolygotol (kifele, +dir) - a tile a
            // latokupon KIVUL esik, annak ellenere, hogy tavolsag szerint
            // felbontando lenne.
            HashSet<TileId> awayCut = AdaptiveQuadTree.SelectCut(
                camX, camY, camZ, PlanetRadius, previousCut: null,
                baseLevel: 2, maxLevel: 8, splitFactor: 2.5, mergeFactor: 3.75,
                forwardX: dirX, forwardY: dirY, forwardZ: dirZ, halfFovRadians: 0.2);
            Assert.IsTrue(awayCut.Contains(root),
                "A kamerat61 elfele eso (a latokupon kivuli) csomopontnak durvanak kellett volna maradnia.");

            // Ugyanaz a pozicio, de a kamera A BOLYGO/tile fele nez (befele,
            // -dir) - ekkor a tavolsag-kriterium ES a latokup egyutt mar
            // felbontja.
            HashSet<TileId> towardCut = AdaptiveQuadTree.SelectCut(
                camX, camY, camZ, PlanetRadius, previousCut: null,
                baseLevel: 2, maxLevel: 8, splitFactor: 2.5, mergeFactor: 3.75,
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
                baseLevel: baseLevel, maxLevel: 16, splitFactor: 61.0, mergeFactor: 91.5);

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
