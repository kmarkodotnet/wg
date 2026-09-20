using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// ELŐFELTÉTEL a balance munkalistás átírásához (todo.md 1. tábla, 3. sor):
    /// „előbb tisztázni, hogy a fixpont egyértelmű-e a korlátok nélkül — a
    /// jelenlegi kimenet sorrend-függő".
    ///
    /// A kérdés azért fontos, mert egy munkalistás (inkrementális) változat
    /// MÁS SORRENDBEN találja meg és hajtja végre a felosztásokat, mint a
    /// jelenlegi, körökben teljes szkennelést végző ciklus. Ha a fixpont
    /// sorrend-függő lenne, az átírás CSENDBEN más vágást adna.
    ///
    /// AZ ÁLLÍTÁS, amit ez a fájl bizonyít: **korlátok nélkül a 2:1-es
    /// kiegyensúlyozás fixpontja EGYÉRTELMŰ**, tehát a végrehajtási sorrend
    /// nem számít. Az ok szerkezeti: a felosztás MONOTON — egy tile
    /// felosztása soha nem szüntet meg egy másik, már fennálló
    /// szintkülönbség-sértést (a fedő ős csak FINOMABB lehet, a különbség
    /// tehát csak csökken), ezért a „mit kell felosztani" halmaz lezárása
    /// sorrendtől független legkisebb fixpont.
    ///
    /// A jelenlegi kimenet sorrend-függősége NEM ebből jön, hanem a
    /// budget/méret-korlátból: ott a ciklus félbeszakad, és ilyenkor MÁR
    /// számít, melyik felosztások fértek be. Ezt az utolsó teszt rögzíti.
    /// </summary>
    public class BalanceFixpointUniquenessTests
    {
        private const int BaseLevel = 3;

        /// <summary>Korlátlan költségvetés: a fixpont ténylegesen elérhető.</summary>
        private const int NoBudget = int.MaxValue / 4;

        private static HashSet<TileId> UnbalancedCut(int stride, int splits = 75, int maxLevel = 8)
        {
            var cut = new HashSet<TileId>();
            for (int f = 0; f < 6; f++)
                for (uint u = 0; u < 8; u++)
                    for (uint v = 0; v < 8; v++)
                        cut.Add(TileId.FromFaceLevelUV(f, BaseLevel, u, v));
            for (int i = 0; i < splits; i++)
            {
                var candidates = cut.Where(t => t.Level < maxLevel).OrderBy(t => t.Value).ToArray();
                TileId tile = candidates[(i * stride) % candidates.Length];
                cut.Remove(tile);
                for (int c = 0; c < 4; c++) cut.Add(tile.Child(c));
            }
            return cut;
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

        /// <summary>Minden jelenleg sértő (felosztandó) fedő ős.</summary>
        private static List<TileId> Violations(HashSet<TileId> cut)
        {
            var result = new HashSet<TileId>();
            foreach (TileId leaf in cut)
            {
                if (leaf.Level <= BaseLevel) continue;
                for (int d = 0; d < 4; d++)
                    if (Cover(TileNeighbors.Neighbor(leaf, (TileDirection)d), cut, out TileId ancestor)
                        && leaf.Level - ancestor.Level > 1)
                        result.Add(ancestor);
            }
            var ordered = new List<TileId>(result);
            ordered.Sort((a, b) => a.Value.CompareTo(b.Value));
            return ordered;
        }

        /// <summary>
        /// TELJESEN ASZINKRON fixpont-keresés: minden lépésben EGY tetszőleges
        /// sértést választ (az `order` szerint) és azt osztja fel, amíg van
        /// sértés. Ez a leglazább lehetséges végrehajtási sorrend - ha minden
        /// választási stratégia ugyanoda ér, a fixpont egyértelmű.
        /// </summary>
        private static HashSet<TileId> ChaoticFixpoint(HashSet<TileId> start, ulong pickSeed, out int steps)
        {
            var cut = new HashSet<TileId>(start);
            ulong state = pickSeed;
            steps = 0;
            while (true)
            {
                List<TileId> violations = Violations(cut);
                if (violations.Count == 0) return cut;
                state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
                int pick = (int)((state >> 33) % (ulong)violations.Count);
                TileId tile = violations[pick];
                cut.Remove(tile);
                for (int c = 0; c < 4; c++) cut.Add(tile.Child(c));
                steps++;
                Assert.True(steps < 100000, "A fixpont-keresés nem konvergált.");
            }
        }

        /// <summary>
        /// A LÉNYEG: öt gyökeresen eltérő végrehajtási sorrend UGYANAZT a
        /// vágást adja, és megegyezik a termelési (körökben dolgozó)
        /// implementációval is.
        /// </summary>
        [Theory]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(13)]
        public void UnconstrainedFixpointIsIndependentOfExecutionOrder(int stride)
        {
            HashSet<TileId> start = UnbalancedCut(stride);

            var production = new HashSet<TileId>(start);
            AdaptiveQuadTree.EnforceRestrictedBalance(production, BaseLevel, NoBudget, true);

            HashSet<TileId> first = ChaoticFixpoint(start, 1UL, out int firstSteps);
            Assert.True(production.SetEquals(first),
                $"A termelési kör-alapú kimenet eltér az aszinkron fixponttól "
                + $"({production.Count} vs {first.Count} levél).");

            foreach (ulong seed in new ulong[] { 2, 99, 12345, 0xDEADBEEF })
            {
                HashSet<TileId> other = ChaoticFixpoint(start, seed, out int steps);
                Assert.True(first.SetEquals(other),
                    $"A(z) {seed} sorrend más fixpontot adott ({first.Count} vs {other.Count} levél).");
                // A LÉPÉSSZÁM is azonos: a felosztandó tile-ok HALMAZA ugyanaz,
                // csak a sorrendjük más - ez erősebb, mint a puszta halmaz-egyezés.
                Assert.Equal(firstSteps, steps);
            }
        }

        /// <summary>
        /// A monotonitás közvetlenül: egy felosztás UTÁN minden korábbi sértés
        /// sértés marad (a fedő ős csak finomabb lehet). Ez az az invariáns,
        /// amiből az egyértelműség következik - ha ez elromlik, a fenti teszt
        /// is elromlik, de ez mutatja meg, MIÉRT.
        /// </summary>
        [Theory]
        [InlineData(3)]
        [InlineData(7)]
        public void SplittingNeverResolvesAnotherPendingViolation(int stride)
        {
            var cut = new HashSet<TileId>(UnbalancedCut(stride));
            int checkedRounds = 0;

            while (checkedRounds < 12)
            {
                List<TileId> before = Violations(cut);
                if (before.Count == 0) break;

                TileId tile = before[0];
                cut.Remove(tile);
                for (int c = 0; c < 4; c++) cut.Add(tile.Child(c));

                var after = new HashSet<TileId>(Violations(cut));
                foreach (TileId pending in before)
                {
                    if (pending.Equals(tile)) continue; // ezt épp most osztottuk fel
                    Assert.True(after.Contains(pending) || !cut.Contains(pending),
                        $"A(z) {pending} sértés eltűnt anélkül, hogy felosztottuk volna.");
                }
                checkedRounds++;
            }

            Assert.True(checkedRounds > 0, "Egyetlen sértés sem volt - a teszt semmit nem mért.");
        }

        /// <summary>
        /// A MÁSIK OLDAL, hogy a fenti állítás ne legyen túlértelmezve: SZORÍTÓ
        /// budget mellett a kimenet MÁR sorrend-függő, mert a ciklus félbeszakad,
        /// és számít, melyik felosztások fértek be. A munkalistás átírásnak
        /// ezért a korlátos esetben a kör-szemantikát is meg kell őriznie, nem
        /// elég a fixpont egyértelműségére hivatkozni.
        /// </summary>
        [Fact]
        public void WithATightBudgetTheOutcomeDoesDependOnOrder()
        {
            HashSet<TileId> start = UnbalancedCut(7);

            var ascending = new HashSet<TileId>(start);
            AdaptiveQuadTree.EnforceRestrictedBalance(ascending, BaseLevel, start.Count + 30, true);

            // Ugyanaz a budget, de egyesével, FORDÍTOTT sorrendben végrehajtva.
            var descending = new HashSet<TileId>(start);
            int budget = start.Count + 30;
            while (true)
            {
                List<TileId> violations = Violations(descending);
                if (violations.Count == 0) break;
                TileId tile = violations[violations.Count - 1];
                if (descending.Count + 3 > budget) break;
                descending.Remove(tile);
                for (int c = 0; c < 4; c++) descending.Add(tile.Child(c));
            }

            Assert.False(ascending.SetEquals(descending),
                "Szorító budget mellett is sorrend-független lett a kimenet - "
                + "akkor a fenti óvatosság fölösleges, és ezt a tesztet frissíteni kell.");
        }
    }
}
