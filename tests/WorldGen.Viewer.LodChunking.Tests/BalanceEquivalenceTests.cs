using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    public class BalanceEquivalenceTests
    {
        [Theory]
        [InlineData(3,20000)] [InlineData(7,20000)] [InlineData(13,20000)]
        [InlineData(3,640)] [InlineData(7,640)] [InlineData(13,640)]
        public void SingleNeighborPassMatchesPreviousTwoPassAlgorithm(int stride,int budget)
        {
            var cut=new HashSet<TileId>();
            for(int f=0;f<6;f++) for(uint u=0;u<8;u++) for(uint v=0;v<8;v++) cut.Add(TileId.FromFaceLevelUV(f,3,u,v));
            for(int i=0;i<75;i++)
            {
                var candidates=cut.Where(t=>t.Level<8).OrderBy(t=>t.Value).ToArray();
                var tile=candidates[(i*stride)%candidates.Length];
                cut.Remove(tile); for(int child=0;child<4;child++) cut.Add(tile.Child(child));
            }
            var expected=new HashSet<TileId>(cut);
            LegacyBalance(expected,3,budget);
            AdaptiveQuadTree.EnforceRestrictedBalance(cut,3,budget,true);
            Assert.True(expected.SetEquals(cut));
        }

        /// <summary>
        /// ND-106: ha a szoros budget MÁR belépéskor blokkol, a kiegyensúlyozás
        /// nem tud egyetlen felosztást sem végrehajtani - a `cut.Count` csak NŐ
        /// (SplitOnce nettó +3), tehát a feltétel többé nem oldódik fel. Eddig
        /// ilyenkor is végigszkennelte a TELJES cutot (|cut| × 4 szomszéd),
        /// hogy aztán semmit ne csináljon.
        ///
        /// Ez a PRODUKCIÓS eset: telített vágásnál `cut.Count == budget`, és a
        /// felhasználó 2026-09-18-i naplójában 68 vágásból 44 volt ilyen.
        /// Mérve: 20 000 levélnél 31 ms, 64 000-nél 78 ms tiszta veszteség;
        /// 180 próbaeseten a balance összideje 9068 ms → 1832 ms, NULLA
        /// megváltozott cut mellett.
        /// </summary>
        [Theory]
        [InlineData(3)] [InlineData(7)] [InlineData(13)]
        public void BudgetBlockedBalanceIsANoOpAndSkipsTheScan(int stride)
        {
            HashSet<TileId> cut=UnbalancedCut(stride);
            var before=new HashSet<TileId>(cut);
            // A budget pontosan a jelenlegi méret: `cut.Count + 3 > budget`.
            int budget=cut.Count;
            var work=new LodSelectionWork(null);

            AdaptiveQuadTree.EnforceRestrictedBalance(cut,3,budget,true,default,work);

            Assert.True(before.SetEquals(cut),"A blokkolt balance megváltoztatta a cutot.");
            Assert.Equal(0,work.BalanceIterations);
            Assert.Equal(0,work.BalanceSplits);
            Assert.Equal(0,work.BalanceLeavesScanned);
        }

        /// <summary>
        /// A kihagyás CSAK a blokkolt esetre vonatkozik: ha van fejtér, a
        /// kiegyensúlyozás változatlanul lefut és felosztásokat végez.
        /// </summary>
        [Theory]
        [InlineData(3)] [InlineData(7)] [InlineData(13)]
        public void BalanceStillRunsWhenTheBudgetHasHeadroom(int stride)
        {
            HashSet<TileId> cut=UnbalancedCut(stride);
            var work=new LodSelectionWork(null);

            AdaptiveQuadTree.EnforceRestrictedBalance(cut,3,20000,true,default,work);

            Assert.True(work.BalanceIterations>0,"Fejtérrel sem futott le a balance.");
            Assert.True(work.BalanceSplits>0,"Fejtérrel sem osztott fel semmit.");
            Assert.True(work.BalanceLeavesScanned>=work.BalanceIterations);
        }

        /// <summary>
        /// A korai kilépés a referencia-implementációval is egyezik a blokkolt
        /// esetben - ugyanaz a feltétel, csak hamarabb kiértékelve.
        /// </summary>
        [Theory]
        [InlineData(3)] [InlineData(7)] [InlineData(13)]
        public void BudgetBlockedResultMatchesTheReferenceImplementation(int stride)
        {
            HashSet<TileId> cut=UnbalancedCut(stride);
            var expected=new HashSet<TileId>(cut);
            int budget=cut.Count;

            LegacyBalance(expected,3,budget);
            AdaptiveQuadTree.EnforceRestrictedBalance(cut,3,budget,true);

            Assert.True(expected.SetEquals(cut));
        }

        /// <summary>A fenti tesztek közös, szándékosan egyenetlen kiindulása.</summary>
        private static HashSet<TileId> UnbalancedCut(int stride)
        {
            var cut=new HashSet<TileId>();
            for(int f=0;f<6;f++) for(uint u=0;u<8;u++) for(uint v=0;v<8;v++) cut.Add(TileId.FromFaceLevelUV(f,3,u,v));
            for(int i=0;i<75;i++)
            {
                var candidates=cut.Where(t=>t.Level<8).OrderBy(t=>t.Value).ToArray();
                var tile=candidates[(i*stride)%candidates.Length];
                cut.Remove(tile); for(int child=0;child<4;child++) cut.Add(tile.Child(child));
            }
            return cut;
        }

        // A checkpoint kétpasszos jelölthalmaza, ugyanazzal a rendezett split-sorrenddel.
        private static void LegacyBalance(HashSet<TileId> cut,int baseLevel,int budget)
        {
            while(true)
            {
                var candidates=new HashSet<TileId>();
                foreach(var tile in cut)
                {
                    if(tile.Level<=baseLevel) continue;
                    candidates.Add(tile);
                    for(int d=0;d<4;d++) if(Cover(TileNeighbors.Neighbor(tile,(TileDirection)d),cut,out var ancestor)) candidates.Add(ancestor);
                }
                var splits=new HashSet<TileId>();
                foreach(var tile in candidates) for(int d=0;d<4;d++)
                    if(Cover(TileNeighbors.Neighbor(tile,(TileDirection)d),cut,out var ancestor) && tile.Level-ancestor.Level>1) splits.Add(ancestor);
                bool changed=false;
                foreach(var tile in splits.OrderBy(t=>t.Value))
                {
                    if(cut.Count+3>budget) return;
                    if(!cut.Remove(tile)) continue;
                    for(int c=0;c<4;c++) cut.Add(tile.Child(c));
                    changed=true;
                }
                if(!changed) return;
            }
        }
        private static bool Cover(TileId tile,HashSet<TileId> cut,out TileId ancestor)
        {
            ancestor=tile;
            while(true) { if(cut.Contains(ancestor)) return true; if(ancestor.Level==0) return false; ancestor=ancestor.Parent(); }
        }
    }
}
