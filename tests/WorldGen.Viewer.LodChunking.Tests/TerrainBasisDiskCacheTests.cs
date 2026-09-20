using System;
using System.Collections.Generic;
using System.IO;
using WorldGen.Core.Tectonics;
using WorldGen.Viewer.Lod;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// A statikus terrain-bázis lemez-gyorsítótárának formátuma és
    /// ÉRVÉNYTELENÍTÉSE (todo.md 1. tábla 9. sor).
    ///
    /// A feladat három kikötést támasztott, és mindhárom itt van igazolva:
    /// a cache nem lehet world-state (bármely kétségnél `null`, nincs részleges
    /// betöltés), eltérő algoritmusverziót nem tölthet be, és legyen méret-/
    /// I/O-/invalidációs terve.
    ///
    /// A LEGFONTOSABB TESZT az algoritmus-eltérés elutasítása. Ha az elbukna, a
    /// gyorsítótár CSENDBEN egy másik világot töltene be - az I1 determinizmus-
    /// invariáns sérülése, aminek semmilyen látható tünete nem lenne azon kívül,
    /// hogy a domborzat "valahogy más".
    /// </summary>
    public class TerrainBasisDiskCacheTests
    {
        private readonly ITestOutputHelper _out;
        public TerrainBasisDiskCacheTests(ITestOutputHelper o) { _out = o; }

        private const ulong Seed = 0xA7C944210000UL;
        private const int Level = 4;
        private const double Epsilon = 1e-4;

        /// <summary>Ugyanaz a sarok-geometria, amit a viewer használ (face, u, v -> pozíció).</summary>
        private static void CornerPositions(int index, int level,
            out double x, out double y, out double z,
            out double ux, out double uy, out double uz,
            out double vx, out double vy, out double vz)
        {
            int n = 1 << level;
            int side = n + 1;
            int faceStride = side * side;
            int face = index / faceStride;
            int faceIndex = index - face * faceStride;
            uint cornerU = (uint)(faceIndex / side);
            uint cornerV = (uint)(faceIndex - (int)cornerU * side);
            double uc = (double)cornerU / n * 2.0 - 1.0;
            double vc = (double)cornerV / n * 2.0 - 1.0;

            WorldGen.Core.Grid.TileGeometry.PositionFromFaceUV(face, uc, vc, out x, out y, out z);
            WorldGen.Core.Grid.TileGeometry.PositionFromFaceUV(face, uc + Epsilon, vc, out ux, out uy, out uz);
            WorldGen.Core.Grid.TileGeometry.PositionFromFaceUV(face, uc, vc + Epsilon, out vx, out vy, out vz);
        }

        private static int CountFor(int level)
        {
            int n = 1 << level;
            int side = n + 1;
            return 6 * side * side;
        }

        private static TerrainBasisDiskCache.Payload Build(ulong seed, int level)
        {
            int count = CountFor(level);
            var center = new TerrainPointBasis[count];
            var u = new TerrainPointBasis[count];
            var v = new TerrainPointBasis[count];
            for (int i = 0; i < count; i++)
            {
                CornerPositions(i, level, out double x, out double y, out double z,
                    out double ux, out double uy, out double uz,
                    out double vx, out double vy, out double vz);
                center[i] = TerrainPointBasis.Compute(seed, x, y, z);
                u[i] = TerrainPointBasis.Compute(seed, ux, uy, uz);
                v[i] = TerrainPointBasis.Compute(seed, vx, vy, vz);
            }
            return new TerrainBasisDiskCache.Payload(center, u, v);
        }

        /// <summary>A viewer oldali újraszámolás megfelelője.</summary>
        private static Action<int, TerrainPointBasis[], TerrainPointBasis[], TerrainPointBasis[]> Recompute(
            ulong seed, int level) => (index, center, u, v) =>
            {
                CornerPositions(index, level, out double x, out double y, out double z,
                    out double ux, out double uy, out double uz,
                    out double vx, out double vy, out double vz);
                center[0] = TerrainPointBasis.Compute(seed, x, y, z);
                u[0] = TerrainPointBasis.Compute(seed, ux, uy, uz);
                v[0] = TerrainPointBasis.Compute(seed, vx, vy, vz);
            };

        private static byte[] Serialize(ulong seed, int level, TerrainBasisDiskCache.Payload payload)
        {
            var key = new TerrainBasisDiskCache.Key(seed, level, Epsilon, payload.Center.Length);
            using var stream = new MemoryStream();
            TerrainBasisDiskCache.Write(stream, key, payload);
            return stream.ToArray();
        }

        private static void AssertSame(TerrainBasisDiskCache.Payload expected, TerrainBasisDiskCache.Payload actual)
        {
            Assert.Equal(expected.Center.Length, actual.Center.Length);
            for (int i = 0; i < expected.Center.Length; i++)
            {
                Assert.Equal(expected.Center[i].WarpedX, actual.Center[i].WarpedX);
                Assert.Equal(expected.Center[i].WarpedY, actual.Center[i].WarpedY);
                Assert.Equal(expected.Center[i].WarpedZ, actual.Center[i].WarpedZ);
                Assert.Equal(expected.Center[i].PrimaryNoise, actual.Center[i].PrimaryNoise);
                Assert.Equal(expected.Center[i].MountainMask, actual.Center[i].MountainMask);
                Assert.Equal(expected.Center[i].SecondaryNoise, actual.Center[i].SecondaryNoise);
                Assert.Equal(expected.U[i].PrimaryNoise, actual.U[i].PrimaryNoise);
                Assert.Equal(expected.V[i].PrimaryNoise, actual.V[i].PrimaryNoise);
            }
        }

        /// <summary>Oda-vissza: a betöltött adat BITRE azonos a kiírttal.</summary>
        [Fact]
        public void RoundTripIsBitIdentical()
        {
            TerrainBasisDiskCache.Payload original = Build(Seed, Level);
            byte[] bytes = Serialize(Seed, Level, original);
            var key = new TerrainBasisDiskCache.Key(Seed, Level, Epsilon, original.Center.Length);

            using var stream = new MemoryStream(bytes);
            TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                stream, key, Recompute(Seed, Level), out string? reason);

            Assert.Null(reason);
            Assert.NotNull(loaded);
            AssertSame(original, loaded!);
            Assert.Equal(TerrainBasisDiskCache.ExpectedFileSize(original.Center.Length), bytes.Length);
            _out.WriteLine($"level {Level}: {original.Center.Length} bejegyzés, {bytes.Length / 1024.0:F1} KiB");
        }

        /// <summary>
        /// A LÉNYEG: ha a fájl egy MÁSIK algoritmussal készült, a betöltés
        /// elutasítja. Az "másik algoritmust" úgy állítjuk elő, hogy MÁS
        /// SEED-del számoljuk ki a tartalmat, de a fejlécbe a várt kulcsot
        /// írjuk - pontosan az a helyzet, amit egy elfelejtett
        /// verzió-emelés okozna: érvényes fejléc, helyes ellenőrzőösszeg,
        /// HIBÁS tartalom.
        /// </summary>
        [Fact]
        public void ContentFromADifferentAlgorithmIsRejected()
        {
            TerrainBasisDiskCache.Payload foreign = Build(Seed ^ 0x9E3779B9UL, Level);
            // A fejlec a VART kulcsot kapja - az ellenorzoosszeg is helyes lesz.
            byte[] bytes = Serialize(Seed, Level, foreign);
            var key = new TerrainBasisDiskCache.Key(Seed, Level, Epsilon, foreign.Center.Length);

            using var stream = new MemoryStream(bytes);
            TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                stream, key, Recompute(Seed, Level), out string? reason);

            Assert.Null(loaded);
            Assert.NotNull(reason);
            Assert.Contains("algoritmus-elteres", reason!);
            _out.WriteLine("elutasítva: " + reason);
        }

        /// <summary>
        /// Az előző teszt akkor is átmenne, ha a validáció MINDENT újraszámolna
        /// (ami értelmetlenné tenné a cache-t). Ez rögzíti, hogy a mintavétel
        /// KORLÁTOZOTT: 1024 bejegyzés, a teljes halmaz helyett - és hogy az
        /// indexek szélesen szórtak, nem egyetlen tartományban ülnek.
        /// </summary>
        [Fact]
        public void ValidationSamplesAreBoundedAndWellSpread()
        {
            int count = CountFor(8); // a valos, level 8-as meret: 396 294
            int[] indices = TerrainBasisDiskCache.ValidationIndices(count);

            Assert.Equal(TerrainBasisDiskCache.ValidationSampleCount, indices.Length);
            Assert.True(indices.Length * 100 < count,
                "A mintavétel nem korlátozott - a cache értelmét vesztené.");

            // Elso es utolso index mindig benne van (csonkolas/eltolodas).
            Assert.Equal(0, indices[0]);
            Assert.Equal(count - 1, indices[1]);

            // Szoras: minden hatodnyi tartomanyba essen minta (6 kockalap).
            var buckets = new HashSet<int>();
            foreach (int index in indices) buckets.Add(index * 6 / count);
            Assert.Equal(6, buckets.Count);

            // Nincs ismetlodes (a prim-lepteku bejaras nem fordulhat korbe koran).
            var distinct = new HashSet<int>(indices);
            Assert.True(distinct.Count > indices.Length * 0.99,
                $"A minták {indices.Length - distinct.Count} ismétlődést tartalmaznak.");
            _out.WriteLine($"level 8: {count} bejegyzés, {indices.Length} minta, "
                + $"{distinct.Count} egyedi, mind a 6 lapon");
        }

        /// <summary>Kulcs-eltérés (seed / level / epszilon / darabszám) elutasítás.</summary>
        [Fact]
        public void KeyMismatchIsRejected()
        {
            TerrainBasisDiskCache.Payload payload = Build(Seed, Level);
            byte[] bytes = Serialize(Seed, Level, payload);
            int count = payload.Center.Length;

            var wrongKeys = new (string What, TerrainBasisDiskCache.Key Key)[]
            {
                ("seed", new TerrainBasisDiskCache.Key(Seed + 1, Level, Epsilon, count)),
                ("level", new TerrainBasisDiskCache.Key(Seed, Level + 1, Epsilon, count)),
                ("epszilon", new TerrainBasisDiskCache.Key(Seed, Level, Epsilon * 2, count)),
                ("darabszám", new TerrainBasisDiskCache.Key(Seed, Level, Epsilon, count + 1)),
            };

            foreach ((string what, TerrainBasisDiskCache.Key key) in wrongKeys)
            {
                using var stream = new MemoryStream(bytes);
                TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                    stream, key, Recompute(Seed, Level), out string? reason);
                Assert.Null(loaded);
                Assert.Equal("kulcs-elteres", reason);
                _out.WriteLine($"{what}: elutasítva");
            }
        }

        /// <summary>Sérülés és csonkolás elutasítása.</summary>
        [Fact]
        public void CorruptionAndTruncationAreRejected()
        {
            TerrainBasisDiskCache.Payload payload = Build(Seed, Level);
            byte[] bytes = Serialize(Seed, Level, payload);
            var key = new TerrainBasisDiskCache.Key(Seed, Level, Epsilon, payload.Center.Length);

            // EGYETLEN bit a tartalom kozepen.
            byte[] corrupt = (byte[])bytes.Clone();
            corrupt[corrupt.Length / 2] ^= 0x01;
            using (var stream = new MemoryStream(corrupt))
            {
                Assert.Null(TerrainBasisDiskCache.TryRead(stream, key, Recompute(Seed, Level), out string? reason));
                Assert.Equal("ellenorzoosszeg-elteres", reason);
            }

            // Csonkolt fajl.
            byte[] truncated = new byte[bytes.Length - 100];
            Array.Copy(bytes, truncated, truncated.Length);
            using (var stream = new MemoryStream(truncated))
            {
                Assert.Null(TerrainBasisDiskCache.TryRead(stream, key, Recompute(Seed, Level), out string? reason));
                Assert.Equal("csonka tartalom", reason);
            }

            // Ures / idegen fajl.
            using (var stream = new MemoryStream(new byte[8]))
            {
                Assert.Null(TerrainBasisDiskCache.TryRead(stream, key, Recompute(Seed, Level), out string? reason));
                Assert.Equal("csonka fejlec", reason);
            }
        }

        /// <summary>
        /// Méret-terv: a fájlméret pontosan kiszámítható előre, tehát a hívó
        /// könyvtár-kvótát tud tartani. A level 8-as valós méret 54,4 MiB.
        /// </summary>
        [Fact]
        public void FileSizeIsPredictable()
        {
            long level8 = TerrainBasisDiskCache.ExpectedFileSize(CountFor(8));
            long level7 = TerrainBasisDiskCache.ExpectedFileSize(CountFor(7));

            _out.WriteLine($"level 7: {level7 / 1048576.0:F1} MiB | level 8: {level8 / 1048576.0:F1} MiB");
            Assert.InRange(level8 / 1048576.0, 54.0, 55.0);
            // A level 8 kb. negyszerese a level 7-nek (a sarokracs +1-es szele miatt nem pontosan).
            Assert.InRange((double)level8 / level7, 3.8, 4.2);
        }

        /// <summary>
        /// A fájlnév a teljes kulcsot kódolja, tehát két eltérő kulcs sosem
        /// írja felül egymást a könyvtárban.
        /// </summary>
        [Fact]
        public void FileNamesAreDistinctForDistinctKeys()
        {
            var names = new HashSet<string>
            {
                new TerrainBasisDiskCache.Key(Seed, 8, Epsilon, 100).ToFileName(),
                new TerrainBasisDiskCache.Key(Seed + 1, 8, Epsilon, 100).ToFileName(),
                new TerrainBasisDiskCache.Key(Seed, 7, Epsilon, 100).ToFileName(),
                new TerrainBasisDiskCache.Key(Seed, 8, Epsilon * 2, 100).ToFileName(),
                new TerrainBasisDiskCache.Key(Seed, 8, Epsilon, 101).ToFileName(),
            };
            Assert.Equal(5, names.Count);
        }
    }
}
