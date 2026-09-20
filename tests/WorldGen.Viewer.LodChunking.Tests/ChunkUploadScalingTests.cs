using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// A chunk-csomagolás és a FELTÖLTÉSI munka skálázódása a levél-budgettel
    /// (todo.md 1. tábla 6. sor). Korábban ezt Unity-függőnek mondtam; ez
    /// túlzás volt, mert a <see cref="DynamicMeshChunking"/> teljes egészében
    /// offline fut.
    ///
    /// MIÉRT A CHUNK-SZÁM A FONTOS, NEM A LEVÉLSZÁM. A feltöltés
    /// frame-enkénti korlátja `TerrainUploadsPerFrame = 128` JOB, és a
    /// felhasználó naplói szerint `jobs ≈ 2 × chunk + 3` (81→165, 57→117,
    /// 51→105). Vagyis nagyjából 62 változott chunk fölött a feltöltés a
    /// következő frame-re csúszik - ezt a `TerrainUploadSliceMs = 2` szeletelés
    /// szándékosan így kezeli, de a chunk-szám az, ami ezt eldönti.
    ///
    /// A HARNESS HITELESÍTÉSE. Ugyanezen a nézeten, 32 000-es budgettel a
    /// modell 52 változott chunkot és 4269 újraépítendő levelet ad; a
    /// felhasználó ÉLES naplójában (cutBudget=30138) `chunks=51..81` és
    /// `emittedLeaves=4422..5144` szerepel. A modell tehát a valódi
    /// nagyságrendet adja vissza, nem csak egy belső konzisztencia-játék.
    /// </summary>
    public class ChunkUploadScalingTests
    {
        private readonly ITestOutputHelper _out;
        public ChunkUploadScalingTests(ITestOutputHelper o) { _out = o; }

        private const double Radius = 100;
        private const double Fov = Math.PI / 3;
        private const double Aspect = 16.0 / 9;
        private const int BaseLevel = 8;
        private const int MaxLevel = 20;
        private const int PixelHeight = 1080;

        // A jelenet (PlanetView.unity) tényleges beállításai.
        private const int ChunkLevel = 6;
        private const int MaxLeavesPerChunk = DynamicMeshChunking.DefaultMaxLeavesPerChunk;

        private static readonly double AxisLength = Math.Sqrt(.017 * .017 + .937 * .937 + .349 * .349);
        private static readonly double Ax = .017 / AxisLength, Ay = -.937 / AxisLength, Az = .349 / AxisLength;

        private static HashSet<TileId> Cut(double distance, double yawDegrees, int budget, TileId[] previous)
        {
            double a = yawDegrees * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            double x = Ax * ca + Az * sa;
            double z = -Ax * sa + Az * ca;
            double y = Ay;
            double split = 6 * Fov / PixelHeight;
            double half = Math.Atan(Math.Tan(Fov / 2) * Math.Sqrt(1 + Aspect * Aspect)) * 1.3;
            return AdaptiveQuadTree.BuildCut(x * distance, y * distance, z * distance, Radius,
                previous, BaseLevel, MaxLevel, split, split / 1.5,
                -x, -y, -z, half, budget,
                traversalRootLevel: 3, staticBaseLevel: BaseLevel, work: new LodSelectionWork(null));
        }

        private sealed class Move
        {
            public int CutLeaves, Chunks, MaxChunkLeaves, ChangedChunks, RebuiltLeaves;
            public double RebuiltFraction;
        }

        /// <summary>Konvergált vágás, majd EGY kis (0,25 fokos) kamera-elfordulás.</summary>
        private static Move SmallCameraMove(double distance, int budget)
        {
            HashSet<TileId> cut = Cut(distance, 0, budget, Array.Empty<TileId>());
            var previous = new List<TileId>(cut);
            for (int i = 0; i < 4; i++)
            {
                cut = Cut(distance, 0, budget, previous.ToArray());
                previous = new List<TileId>(cut);
            }

            Dictionary<TileId, HashSet<TileId>> before =
                DynamicMeshChunking.GroupByLeafBudget(cut, ChunkLevel, MaxLeavesPerChunk);
            HashSet<TileId> moved = Cut(distance, 0.25, budget, previous.ToArray());
            Dictionary<TileId, HashSet<TileId>> after =
                DynamicMeshChunking.GroupByLeafBudget(moved, ChunkLevel, MaxLeavesPerChunk, before);

            DynamicMeshChunking.ChunkDiff diff = DynamicMeshChunking.DiffChunks(before, after);
            var m = new Move { CutLeaves = moved.Count, Chunks = after.Count, ChangedChunks = diff.ChangedOrNewChunks.Count };
            foreach (HashSet<TileId> g in after.Values)
                if (g.Count > m.MaxChunkLeaves) m.MaxChunkLeaves = g.Count;
            foreach (TileId root in diff.ChangedOrNewChunks) m.RebuiltLeaves += after[root].Count;
            m.RebuiltFraction = (double)m.RebuiltLeaves / Math.Max(1, m.CutLeaves);
            return m;
        }

        /// <summary>
        /// A LÉNYEG a Play-menethez: a budget hatszorozása (8 000 → 48 000) NEM
        /// hatszorozza a frame-enként feltöltendő CHUNKOK számát. MÉRVE
        /// 2026-09-20-án: 24→46 (1,9x), 16→67 (4,2x), 36→84 (2,3x) a három
        /// zoom-sávban. A küszöb 6x, mert az állítás a SZUBLINEARITÁS, nem egy
        /// konkrét szám.
        /// </summary>
        [Theory]
        [InlineData(130.0)]
        [InlineData(110.0)]
        [InlineData(103.0)]
        public void ChangedChunkCountGrowsSublinearlyWithTheBudget(double distance)
        {
            Move small = SmallCameraMove(distance, 8000);
            Move large = SmallCameraMove(distance, 48000);

            _out.WriteLine($"távolság={distance}: 8000 -> {small.ChangedChunks} chunk / {small.RebuiltLeaves} levél "
                + $"({small.RebuiltFraction:P1}) | 48000 -> {large.ChangedChunks} chunk / {large.RebuiltLeaves} levél "
                + $"({large.RebuiltFraction:P1})");

            Assert.True(large.CutLeaves > small.CutLeaves * 3,
                "A nagyobb budget nem adott érdemben nagyobb vágást - a teszt semmit nem mér.");
            Assert.True(large.ChangedChunks < small.ChangedChunks * 6,
                $"A változott chunkok száma a budgettel legalább arányosan nőtt "
                + $"({small.ChangedChunks} -> {large.ChangedChunks}), tehát a feltöltés "
                + "frame-enkénti terhe is arányosan nő.");
        }

        /// <summary>
        /// Egy kis kamera-mozdulat sosem építi újra a VÁGÁS TÚLNYOMÓ RÉSZÉT.
        /// Ha ez elromlana (pl. a chunk-gyökerek elcsúsznának a kamerával), a
        /// növekvő budget azonnal akadássá válna - a mért legrosszabb eset
        /// 57,6% volt (8 000-es budget, felszín-közel).
        /// </summary>
        [Theory]
        [InlineData(130.0, 8000)]
        [InlineData(130.0, 48000)]
        [InlineData(110.0, 48000)]
        [InlineData(103.0, 48000)]
        public void ASmallMoveNeverRebuildsMostOfTheCut(double distance, int budget)
        {
            Move m = SmallCameraMove(distance, budget);

            Assert.True(m.RebuiltFraction < 0.70,
                $"Egy 0,25 fokos elfordulás a vágás {m.RebuiltFraction:P1}-át építi újra "
                + $"({m.RebuiltLeaves}/{m.CutLeaves} levél).");
            Assert.True(m.MaxChunkLeaves <= MaxLeavesPerChunk,
                $"Egy chunk {m.MaxChunkLeaves} levelet kapott a {MaxLeavesPerChunk}-os korlát ellenére.");
        }
    }
}
