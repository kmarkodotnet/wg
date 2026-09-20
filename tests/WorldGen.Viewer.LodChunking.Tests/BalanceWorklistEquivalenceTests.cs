using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// ND-121: az <see cref="AdaptiveQuadTree.EnforceRestrictedBalance"/>
    /// INKREMENTÁLIS jelöltkeresésének differenciális igazolása.
    ///
    /// A korábbi implementáció minden körben a TELJES cutot végigjárta; az új
    /// csak az első körben teszi ezt, utána a *frontier*-t (az előző körben
    /// keletkezett gyerekek + a sértést kiváltó levelek). A kör-szemantika
    /// változatlan, tehát a kimenetnek BITRE azonosnak kell lennie — a
    /// korlátos esetekben is, ahol a kimenet már sorrend-függő (ld.
    /// <see cref="BalanceFixpointUniquenessTests"/>).
    ///
    /// Ez a fájl a referencia-implementációt (a régi, teljes szkennelésű
    /// ciklus) tartja meg orákulumként, és véletlen, egyenetlen vágásokon
    /// hasonlítja össze a kettőt — szándékosan sok konfiguráción, mert az
    /// első, HIBÁS változatomat pont egy ilyen eset buktatta le (a frontierből
    /// hiányoztak a sértést kiváltó finom levelek).
    /// </summary>
    public class BalanceWorklistEquivalenceTests
    {
        private readonly ITestOutputHelper _out;
        public BalanceWorklistEquivalenceTests(ITestOutputHelper o) { _out = o; }

        private const int BaseLevel = 3;

        /// <summary>A régi, KÖRÖNKÉNT TELJES SZKENNELÉSŰ implementáció, orákulumként.</summary>
        private static void ReferenceBalance(HashSet<TileId> cut, int baseLevel, int maxLeafCount, bool strictBudget)
        {
            int balanceSizeCap = maxLeafCount > int.MaxValue / 3 ? int.MaxValue : maxLeafCount * 3;
            if (strictBudget && cut.Count + 3 > maxLeafCount) return;

            bool changed;
            do
            {
                if (cut.Count > balanceSizeCap) break;
                changed = false;

                var toSplit = new HashSet<TileId>();
                foreach (TileId leaf in cut)
                {
                    if (leaf.Level <= baseLevel) continue;
                    for (int d = 0; d < 4; d++)
                        if (Cover(TileNeighbors.Neighbor(leaf, (TileDirection)d), cut, out TileId covering)
                            && leaf.Level - covering.Level > 1)
                            toSplit.Add(covering);
                }

                var ordered = new List<TileId>(toSplit);
                ordered.Sort((a, b) => a.Value.CompareTo(b.Value));
                foreach (TileId ancestor in ordered)
                {
                    if (strictBudget && cut.Count + 3 > maxLeafCount) return;
                    if (cut.Count > balanceSizeCap) break;
                    if (cut.Contains(ancestor))
                    {
                        cut.Remove(ancestor);
                        for (int c = 0; c < 4; c++) cut.Add(ancestor.Child(c));
                        changed = true;
                    }
                }
            } while (changed);
        }

        private static bool Cover(TileId tile, HashSet<TileId> cut, out TileId ancestor)
        {
            ancestor = tile;
            while (true)
            {
                if (cut.Contains(ancestor)) return true;
                if (ancestor.Level == 0) return false;
                ancestor = ancestor.Parent();
            }
        }

        /// <summary>
        /// Véletlen, ERŐSEN egyenetlen vágás: néhány helyen mélyre fúr (ez adja
        /// a hosszú kaszkádokat), máshol a durva alapszint marad.
        /// </summary>
        private static HashSet<TileId> RandomUnbalancedCut(ulong seed, int splits, int maxLevel)
        {
            ulong state = seed;
            double Next()
            {
                state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
                return (state >> 11) * (1.0 / 9007199254740992.0);
            }

            var cut = new HashSet<TileId>();
            for (int f = 0; f < 6; f++)
                for (uint u = 0; u < 8; u++)
                    for (uint v = 0; v < 8; v++)
                        cut.Add(TileId.FromFaceLevelUV(f, BaseLevel, u, v));

            // Néhány "fúrási pont", ahol egymás után mélyre megyünk - enélkül a
            // véletlen felosztás egyenletes lenne, és nem adna nagy szintugrást.
            for (int i = 0; i < splits; i++)
            {
                var candidates = cut.Where(t => t.Level < maxLevel).OrderBy(t => t.Value).ToArray();
                if (candidates.Length == 0) break;
                TileId tile = candidates[(int)(Next() * candidates.Length) % candidates.Length];
                // 70% esellyel MELYRE furunk ugyanott, 30% esellyel uj helyre lepunk.
                int depth = Next() < 0.7 ? 4 : 1;
                for (int d = 0; d < depth && tile.Level < maxLevel; d++)
                {
                    if (!cut.Remove(tile)) break;
                    for (int c = 0; c < 4; c++) cut.Add(tile.Child(c));
                    tile = tile.Child((int)(Next() * 4) % 4);
                }
            }
            return cut;
        }

        [Fact]
        public void MatchesTheFullScanReferenceAcrossManyRandomCutsAndBudgets()
        {
            int cases = 0;
            long referenceScans = 0, worklistScans = 0;

            for (ulong seed = 1; seed <= 40; seed++)
            {
                foreach (int maxLevel in new[] { 7, 10, 14 })
                {
                    HashSet<TileId> start = RandomUnbalancedCut(seed, 60, maxLevel);

                    // Budgetek: korlatlan, boseges, epp-hogy, es SZORITO (ahol a
                    // kimenet mar sorrend-fuggo, tehat a legerzekenyebb eset).
                    foreach (int budget in new[]
                    {
                        int.MaxValue / 4,
                        start.Count * 4,
                        start.Count + 120,
                        start.Count + 12,
                        start.Count,
                    })
                    {
                        var expected = new HashSet<TileId>(start);
                        ReferenceBalance(expected, BaseLevel, budget, true);

                        var actual = new HashSet<TileId>(start);
                        var work = new LodSelectionWork(null);
                        AdaptiveQuadTree.EnforceRestrictedBalance(actual, BaseLevel, budget, true, default, work);

                        Assert.True(expected.SetEquals(actual),
                            $"Eltérés: seed={seed} maxLevel={maxLevel} budget={budget} "
                            + $"(referencia {expected.Count} levél, munkalistás {actual.Count}, "
                            + $"szimmetrikus különbség {expected.Except(actual).Count() + actual.Except(expected).Count()}).");

                        var refWork = new LodSelectionWork(null);
                        var refCut = new HashSet<TileId>(start);
                        CountingReferenceBalance(refCut, BaseLevel, budget, refWork);
                        referenceScans += refWork.BalanceLeavesScanned;
                        worklistScans += work.BalanceLeavesScanned;
                        cases++;
                    }
                }
            }

            _out.WriteLine($"{cases} eset | bejart levelek: referencia={referenceScans:N0} munkalistas={worklistScans:N0} "
                + $"({(double)referenceScans / Math.Max(1, worklistScans):F1}x kevesebb)");
            Assert.True(cases >= 500, $"Csak {cases} esetet mertunk.");
            Assert.True(worklistScans * 2 < referenceScans,
                $"A munkalistás út nem járt be lényegesen kevesebb levelet "
                + $"({worklistScans:N0} vs {referenceScans:N0}).");
        }

        /// <summary>A referencia, de a bejárt levelek számlálásával.</summary>
        private static void CountingReferenceBalance(HashSet<TileId> cut, int baseLevel, int maxLeafCount, LodSelectionWork work)
        {
            int balanceSizeCap = maxLeafCount > int.MaxValue / 3 ? int.MaxValue : maxLeafCount * 3;
            if (cut.Count + 3 > maxLeafCount) return;
            bool changed;
            do
            {
                if (cut.Count > balanceSizeCap) break;
                changed = false;
                work.CountBalanceIteration(cut.Count);
                var toSplit = new HashSet<TileId>();
                foreach (TileId leaf in cut)
                {
                    if (leaf.Level <= baseLevel) continue;
                    for (int d = 0; d < 4; d++)
                        if (Cover(TileNeighbors.Neighbor(leaf, (TileDirection)d), cut, out TileId covering)
                            && leaf.Level - covering.Level > 1)
                            toSplit.Add(covering);
                }
                var ordered = new List<TileId>(toSplit);
                ordered.Sort((a, b) => a.Value.CompareTo(b.Value));
                foreach (TileId ancestor in ordered)
                {
                    if (cut.Count + 3 > maxLeafCount) return;
                    if (cut.Count > balanceSizeCap) break;
                    if (cut.Contains(ancestor))
                    {
                        cut.Remove(ancestor);
                        for (int c = 0; c < 4; c++) cut.Add(ancestor.Child(c));
                        work.CountBalanceSplit();
                        changed = true;
                    }
                }
            } while (changed);
        }

        /// <summary>
        /// Nagy, realisztikus vágás: itt mérhető a tényleges nyereség, és itt
        /// derülne ki, ha a frontier valamit kihagyna (a kaszkád hosszabb).
        /// </summary>
        [Fact]
        public void LargeCascadeMatchesTheReferenceAndScansFarFewerLeaves()
        {
            HashSet<TileId> start = RandomUnbalancedCut(777, 400, maxLevel: 16);
            int budget = int.MaxValue / 4;

            var expected = new HashSet<TileId>(start);
            var referenceWork = new LodSelectionWork(null);
            var refTimer = Stopwatch.StartNew();
            CountingReferenceBalance(expected, BaseLevel, budget, referenceWork);
            refTimer.Stop();

            var actual = new HashSet<TileId>(start);
            var work = new LodSelectionWork(null);
            var timer = Stopwatch.StartNew();
            AdaptiveQuadTree.EnforceRestrictedBalance(actual, BaseLevel, budget, true, default, work);
            timer.Stop();

            _out.WriteLine(
                $"kiindulás={start.Count} levél -> kiegyensúlyozva={actual.Count} | "
                + $"körök: ref={referenceWork.BalanceIterations} új={work.BalanceIterations} | "
                + $"bejárt levelek: ref={referenceWork.BalanceLeavesScanned:N0} új={work.BalanceLeavesScanned:N0} | "
                + $"idő: ref={refTimer.Elapsed.TotalMilliseconds:F1}ms új={timer.Elapsed.TotalMilliseconds:F1}ms");

            Assert.True(expected.SetEquals(actual), "A nagy kaszkád kimenete eltér a referenciától.");
            Assert.Equal(referenceWork.BalanceSplits, work.BalanceSplits);
            Assert.Equal(referenceWork.BalanceIterations, work.BalanceIterations);
            // MERVE 2026-09-20: 129 381 -> 25 683 bejart level (5,0x), 53,5 ms -> 11,5 ms.
            // A kuszob 4x, hogy a jovobeli hangolasok ne tegyek torekenne.
            Assert.True(work.BalanceLeavesScanned * 4 < referenceWork.BalanceLeavesScanned,
                $"A nyereség kisebb 4x-nél ({work.BalanceLeavesScanned:N0} vs {referenceWork.BalanceLeavesScanned:N0}).");
        }
    }
}
