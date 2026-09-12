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
