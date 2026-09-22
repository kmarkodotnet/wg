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
    /// A terrain-bázis lemez-gyorsítótárának formátuma és ÉRVÉNYTELENÍTÉSE
    /// (todo.md 1. tábla 9. sor; ND-122, majd ND-131).
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
    ///
    /// ND-131: a formátum KÉT fogyasztót szolgál ki (három tömbös SAROK-bázis és
    /// egy tömbös TILE-KÖZÉPPONT bázis), ezért a `Kind`/`ArrayCount`
    /// elkülönítése is tesztelt - különben a két gyorsítótár összekeveredhetne.
    /// </summary>
    public class TerrainBasisDiskCacheTests
    {
        private readonly ITestOutputHelper _out;
        public TerrainBasisDiskCacheTests(ITestOutputHelper o) { _out = o; }

        private const ulong Seed = 0xA7C944210000UL;
        private const int Level = 4;
        private const double Epsilon = 1e-4;

        // ================================================================
        // HAROM TOMBOS (statikus sarok) valtozat - ND-63/ND-122
        // ================================================================

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

        private static int CornerCountFor(int level)
        {
            int n = 1 << level;
            int side = n + 1;
            return 6 * side * side;
        }

        private static TerrainBasisDiskCache.Payload BuildCorners(ulong seed, int level)
        {
            int count = CornerCountFor(level);
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

        /// <summary>A viewer oldali újraszámolás megfelelője (három tömb).</summary>
        private static Action<int, TerrainPointBasis[][]> RecomputeCorners(ulong seed, int level)
            => (index, arrays) =>
            {
                CornerPositions(index, level, out double x, out double y, out double z,
                    out double ux, out double uy, out double uz,
                    out double vx, out double vy, out double vz);
                arrays[0][0] = TerrainPointBasis.Compute(seed, x, y, z);
                arrays[1][0] = TerrainPointBasis.Compute(seed, ux, uy, uz);
                arrays[2][0] = TerrainPointBasis.Compute(seed, vx, vy, vz);
            };

        private static TerrainBasisDiskCache.Key CornerKey(ulong seed, int level, int count, double epsilon = Epsilon)
            => new TerrainBasisDiskCache.Key(seed, level, epsilon, count,
                arrayCount: 3, kind: TerrainBasisDiskCache.KindStaticCorners);

        private static byte[] Serialize(in TerrainBasisDiskCache.Key key, TerrainBasisDiskCache.Payload payload)
        {
            using var stream = new MemoryStream();
            TerrainBasisDiskCache.Write(stream, key, payload);
            return stream.ToArray();
        }

        // ================================================================
        // EGY TOMBOS (hidrologiai tile-kozeppont) valtozat - ND-64/ND-131
        // ================================================================

        /// <summary>Ugyanaz a tile-középpont geometria, amit a viewer használ.</summary>
        private static void TileCenterPosition(int index, int level,
            out double x, out double y, out double z)
        {
            int n = 1 << level;
            int faceStride = n * n;
            int face = index / faceStride;
            int faceIndex = index - face * faceStride;
            uint u = (uint)(faceIndex / n);
            uint v = (uint)(faceIndex - (int)u * n);
            WorldGen.Core.Grid.TileGeometry.ToPosition(
                WorldGen.Core.Grid.TileId.FromFaceLevelUV(face, level, u, v), out x, out y, out z);
        }

        private static int TileCenterCountFor(int level)
        {
            int n = 1 << level;
            return 6 * n * n;
        }

        private static TerrainBasisDiskCache.Payload BuildTileCenters(ulong seed, int level)
        {
            int count = TileCenterCountFor(level);
            var bases = new TerrainPointBasis[count];
            for (int i = 0; i < count; i++)
            {
                TileCenterPosition(i, level, out double x, out double y, out double z);
                bases[i] = TerrainPointBasis.Compute(seed, x, y, z);
            }
            return new TerrainBasisDiskCache.Payload(bases);
        }

        private static Action<int, TerrainPointBasis[][]> RecomputeTileCenters(ulong seed, int level)
            => (index, arrays) =>
            {
                TileCenterPosition(index, level, out double x, out double y, out double z);
                arrays[0][0] = TerrainPointBasis.Compute(seed, x, y, z);
            };

        private static TerrainBasisDiskCache.Key TileCenterKey(ulong seed, int level, int count)
            => new TerrainBasisDiskCache.Key(seed, level, 0.0, count,
                arrayCount: 1, kind: TerrainBasisDiskCache.KindTileCenters);

        // ================================================================

        private static void AssertSame(TerrainBasisDiskCache.Payload expected, TerrainBasisDiskCache.Payload actual)
        {
            Assert.Equal(expected.ArrayCount, actual.ArrayCount);
            for (int a = 0; a < expected.ArrayCount; a++)
            {
                Assert.Equal(expected.Arrays[a].Length, actual.Arrays[a].Length);
                for (int i = 0; i < expected.Arrays[a].Length; i++)
                {
                    Assert.Equal(expected.Arrays[a][i].WarpedX, actual.Arrays[a][i].WarpedX);
                    Assert.Equal(expected.Arrays[a][i].WarpedY, actual.Arrays[a][i].WarpedY);
                    Assert.Equal(expected.Arrays[a][i].WarpedZ, actual.Arrays[a][i].WarpedZ);
                    Assert.Equal(expected.Arrays[a][i].PrimaryNoise, actual.Arrays[a][i].PrimaryNoise);
                    Assert.Equal(expected.Arrays[a][i].MountainMask, actual.Arrays[a][i].MountainMask);
                    Assert.Equal(expected.Arrays[a][i].SecondaryNoise, actual.Arrays[a][i].SecondaryNoise);
                }
            }
        }

        /// <summary>Oda-vissza: a betöltött adat BITRE azonos a kiírttal (három tömb).</summary>
        [Fact]
        public void RoundTripIsBitIdentical()
        {
            TerrainBasisDiskCache.Payload original = BuildCorners(Seed, Level);
            int count = original.Arrays[0].Length;
            TerrainBasisDiskCache.Key key = CornerKey(Seed, Level, count);
            byte[] bytes = Serialize(key, original);

            using var stream = new MemoryStream(bytes);
            TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                stream, key, RecomputeCorners(Seed, Level), out string? reason);

            Assert.Null(reason);
            Assert.NotNull(loaded);
            AssertSame(original, loaded!);
            Assert.Equal(TerrainBasisDiskCache.ExpectedFileSize(count, 3), bytes.Length);
            _out.WriteLine($"level {Level} sarok: {count} bejegyzés, {bytes.Length / 1024.0:F1} KiB");
        }

        /// <summary>
        /// ND-131: ugyanaz egy tömbbel - a hidrológiai tile-középpont bázis
        /// alakja. A fájl HARMADA a három tömbösnek, tehát a lemez sem
        /// pazarlódik.
        /// </summary>
        [Fact]
        public void SingleArrayRoundTripIsBitIdentical()
        {
            TerrainBasisDiskCache.Payload original = BuildTileCenters(Seed, Level);
            int count = original.Arrays[0].Length;
            TerrainBasisDiskCache.Key key = TileCenterKey(Seed, Level, count);
            byte[] bytes = Serialize(key, original);

            using var stream = new MemoryStream(bytes);
            TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                stream, key, RecomputeTileCenters(Seed, Level), out string? reason);

            Assert.Null(reason);
            Assert.NotNull(loaded);
            Assert.Equal(1, loaded!.ArrayCount);
            AssertSame(original, loaded);
            Assert.Equal(TerrainBasisDiskCache.ExpectedFileSize(count, 1), bytes.Length);

            // A tartalom pontosan harmada a haromtombosnek (azonos darabszamra).
            Assert.Equal(
                3L * (TerrainBasisDiskCache.ExpectedFileSize(count, 1) - HeaderBytesFor(count, 1)),
                TerrainBasisDiskCache.ExpectedFileSize(count, 3) - HeaderBytesFor(count, 3));
            _out.WriteLine($"level {Level} tile-középpont: {count} bejegyzés, {bytes.Length / 1024.0:F1} KiB");
        }

        private static long HeaderBytesFor(int count, int arrayCount)
            => TerrainBasisDiskCache.ExpectedFileSize(count, arrayCount)
               - (long)arrayCount * count * TerrainBasisDiskCache.BasisByteSize;

        /// <summary>
        /// ND-131, A KÉT GYORSÍTÓTÁR ELKÜLÖNÍTÉSE: egy tile-középpont fájlt
        /// SOHA nem szabad sarok-bázisként betölteni (és fordítva) - akkor sem,
        /// ha a darabszám véletlenül egyezne. A `Kind` és az `ArrayCount` is a
        /// kulcs része, tehát a fejléc-ellenőrzés elutasítja, és a fájlnevük is
        /// különbözik, tehát fel sem tudják írni egymást.
        /// </summary>
        [Fact]
        public void TileCenterAndCornerCachesCannotBeConfused()
        {
            TerrainBasisDiskCache.Payload tileCenters = BuildTileCenters(Seed, Level);
            int count = tileCenters.Arrays[0].Length;
            byte[] bytes = Serialize(TileCenterKey(Seed, Level, count), tileCenters);

            // UGYANAZ a darabszam, de sarok-kulccsal kerve: elutasitas.
            using (var stream = new MemoryStream(bytes))
            {
                TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                    stream, CornerKey(Seed, Level, count), RecomputeCorners(Seed, Level), out string? reason);
                Assert.Null(loaded);
                Assert.Equal("kulcs-elteres", reason);
            }

            // Csak a Kind kulonbozik: akkor is elutasitas.
            var sameShapeOtherKind = new TerrainBasisDiskCache.Key(
                Seed, Level, 0.0, count, arrayCount: 1, kind: TerrainBasisDiskCache.KindStaticCorners);
            using (var stream = new MemoryStream(bytes))
            {
                Assert.Null(TerrainBasisDiskCache.TryRead(
                    stream, sameShapeOtherKind, RecomputeTileCenters(Seed, Level), out string? reason));
                Assert.Equal("kulcs-elteres", reason);
            }

            Assert.NotEqual(
                TileCenterKey(Seed, Level, count).ToFileName(),
                CornerKey(Seed, Level, count).ToFileName());
            Assert.NotEqual(
                TileCenterKey(Seed, Level, count).ToFileName(),
                sameShapeOtherKind.ToFileName());
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
            TerrainBasisDiskCache.Payload foreign = BuildCorners(Seed ^ 0x9E3779B9UL, Level);
            int count = foreign.Arrays[0].Length;
            TerrainBasisDiskCache.Key key = CornerKey(Seed, Level, count);
            // A fejlec a VART kulcsot kapja - az ellenorzoosszeg is helyes lesz.
            byte[] bytes = Serialize(key, foreign);

            using var stream = new MemoryStream(bytes);
            TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                stream, key, RecomputeCorners(Seed, Level), out string? reason);

            Assert.Null(loaded);
            Assert.NotNull(reason);
            Assert.Contains("algoritmus-elteres", reason!);
            _out.WriteLine("elutasítva: " + reason);
        }

        /// <summary>ND-131: ugyanez az egy tömbös úton is érvényes.</summary>
        [Fact]
        public void SingleArrayContentFromADifferentAlgorithmIsRejected()
        {
            TerrainBasisDiskCache.Payload foreign = BuildTileCenters(Seed ^ 0x9E3779B9UL, Level);
            int count = foreign.Arrays[0].Length;
            TerrainBasisDiskCache.Key key = TileCenterKey(Seed, Level, count);
            byte[] bytes = Serialize(key, foreign);

            using var stream = new MemoryStream(bytes);
            TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                stream, key, RecomputeTileCenters(Seed, Level), out string? reason);

            Assert.Null(loaded);
            Assert.Contains("algoritmus-elteres", reason!);
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
            int count = CornerCountFor(8); // a valos, level 8-as meret: 396 294
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

        /// <summary>Kulcs-eltérés (seed / level / epszilon / darabszám / tömbszám / kind) elutasítás.</summary>
        [Fact]
        public void KeyMismatchIsRejected()
        {
            TerrainBasisDiskCache.Payload payload = BuildCorners(Seed, Level);
            int count = payload.Arrays[0].Length;
            byte[] bytes = Serialize(CornerKey(Seed, Level, count), payload);

            var wrongKeys = new (string What, TerrainBasisDiskCache.Key Key)[]
            {
                ("seed", CornerKey(Seed + 1, Level, count)),
                ("level", CornerKey(Seed, Level + 1, count)),
                ("epszilon", CornerKey(Seed, Level, count, Epsilon * 2)),
                ("darabszám", CornerKey(Seed, Level, count + 1)),
                ("tömbszám", new TerrainBasisDiskCache.Key(Seed, Level, Epsilon, count,
                    arrayCount: 1, kind: TerrainBasisDiskCache.KindStaticCorners)),
                ("kind", new TerrainBasisDiskCache.Key(Seed, Level, Epsilon, count,
                    arrayCount: 3, kind: TerrainBasisDiskCache.KindTileCenters)),
            };

            foreach ((string what, TerrainBasisDiskCache.Key key) in wrongKeys)
            {
                using var stream = new MemoryStream(bytes);
                TerrainBasisDiskCache.Payload? loaded = TerrainBasisDiskCache.TryRead(
                    stream, key, RecomputeCorners(Seed, Level), out string? reason);
                Assert.Null(loaded);
                Assert.Equal("kulcs-elteres", reason);
                _out.WriteLine($"{what}: elutasítva");
            }
        }

        /// <summary>Sérülés és csonkolás elutasítása.</summary>
        [Fact]
        public void CorruptionAndTruncationAreRejected()
        {
            TerrainBasisDiskCache.Payload payload = BuildCorners(Seed, Level);
            int count = payload.Arrays[0].Length;
            TerrainBasisDiskCache.Key key = CornerKey(Seed, Level, count);
            byte[] bytes = Serialize(key, payload);

            // EGYETLEN bit a tartalom kozepen.
            byte[] corrupt = (byte[])bytes.Clone();
            corrupt[corrupt.Length / 2] ^= 0x01;
            using (var stream = new MemoryStream(corrupt))
            {
                Assert.Null(TerrainBasisDiskCache.TryRead(stream, key, RecomputeCorners(Seed, Level), out string? reason));
                Assert.Equal("ellenorzoosszeg-elteres", reason);
            }

            // Csonkolt fajl.
            byte[] truncated = new byte[bytes.Length - 100];
            Array.Copy(bytes, truncated, truncated.Length);
            using (var stream = new MemoryStream(truncated))
            {
                Assert.Null(TerrainBasisDiskCache.TryRead(stream, key, RecomputeCorners(Seed, Level), out string? reason));
                Assert.Equal("csonka tartalom", reason);
            }

            // Ures / idegen fajl.
            using (var stream = new MemoryStream(new byte[8]))
            {
                Assert.Null(TerrainBasisDiskCache.TryRead(stream, key, RecomputeCorners(Seed, Level), out string? reason));
                Assert.Equal("csonka fejlec", reason);
            }
        }

        /// <summary>
        /// Méret-terv: a fájlméret pontosan kiszámítható előre, tehát a hívó
        /// könyvtár-kvótát tud tartani. A level 8-as valós méret 54,4 MiB
        /// (sarok, három tömb) és 18,0 MiB (tile-középpont, egy tömb).
        /// </summary>
        [Fact]
        public void FileSizeIsPredictable()
        {
            long corners8 = TerrainBasisDiskCache.ExpectedFileSize(CornerCountFor(8), 3);
            long corners7 = TerrainBasisDiskCache.ExpectedFileSize(CornerCountFor(7), 3);
            long centers8 = TerrainBasisDiskCache.ExpectedFileSize(TileCenterCountFor(8), 1);

            _out.WriteLine($"sarok level 7: {corners7 / 1048576.0:F1} MiB | "
                + $"sarok level 8: {corners8 / 1048576.0:F1} MiB | "
                + $"tile-középpont level 8: {centers8 / 1048576.0:F1} MiB");
            Assert.InRange(corners8 / 1048576.0, 54.0, 55.0);
            Assert.InRange(centers8 / 1048576.0, 17.5, 18.5);
            // A level 8 kb. negyszerese a level 7-nek (a sarokracs +1-es szele miatt nem pontosan).
            Assert.InRange((double)corners8 / corners7, 3.8, 4.2);
            // Egy VILAG teljes gyorsitotara elfer a 512 MiB-os kvota tizedeben.
            Assert.InRange((corners8 + centers8) / 1048576.0, 71.0, 74.0);
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
                CornerKey(Seed, 8, 100).ToFileName(),
                CornerKey(Seed + 1, 8, 100).ToFileName(),
                CornerKey(Seed, 7, 100).ToFileName(),
                CornerKey(Seed, 8, 100, Epsilon * 2).ToFileName(),
                CornerKey(Seed, 8, 101).ToFileName(),
                TileCenterKey(Seed, 8, 100).ToFileName(),
            };
            Assert.Equal(6, names.Count);

            // A kvota-takarito a "basis_*.bin" mintat hasznalja - mindkét fajta
            // fajlnak illeszkednie kell ra, kulonben a tile-kozeppont fajlok
            // soha nem esnenek ki a konyvtarbol.
            foreach (string name in names)
            {
                Assert.StartsWith("basis_", name);
                Assert.EndsWith(".bin", name);
                Assert.True(TerrainBasisDiskCache.IsCurrentFormatFileName(name), name);
            }
        }

        /// <summary>
        /// ND-131: a formatum-valtas utan maradt, KORABBI nevu fajlokat a
        /// takaritas felismeri es torli. Ez a szuro dolga - ha elbukna, minden
        /// formatum-valtas utan ott maradna egy soha tobbe nem olvasott,
        /// tizmegabajtos fajl a felhasznalo gyorsitotaraban.
        /// </summary>
        [Theory]
        // A ND-122-es (elso) formatum neve - pontosan ez maradt a lemezen.
        [InlineData("basis_0000a7c944210000_L8_e3f1a36e2eb1c432d_396294.bin", false)]
        [InlineData("basis_valami_mas.bin", false)]
        [InlineData("basis_k0_0000a7c944210000_L8_e3f1a36e2eb1c432d_396294x3.bin", true)]
        [InlineData("basis_k1_0000a7c944210000_L8_e0000000000000000_393216x1.bin", true)]
        [InlineData("basis_k0_valami.txt", false)]
        public void StaleFormatFileNamesAreRecognized(string fileName, bool isCurrent)
            => Assert.Equal(isCurrent, TerrainBasisDiskCache.IsCurrentFormatFileName(fileName));
    }
}
