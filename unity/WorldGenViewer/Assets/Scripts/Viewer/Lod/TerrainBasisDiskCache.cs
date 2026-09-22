using System;
using System.IO;
using WorldGen.Core.Tectonics;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// A terrain-bázis LEMEZ-gyorsítótárának formátuma és ellenőrzése.
    ///
    /// KÉT FOGYASZTÓJA van, ezért a formátum N TÖMBÖS (ld. <see cref="Key.ArrayCount"/>
    /// és <see cref="Key.Kind"/>):
    ///
    ///  - <see cref="KindStaticCorners"/> (ND-63/ND-122): a statikus SAROK-bázis,
    ///    HÁROM tömb (center + u/v eltolt a normál véges-differenciájához).
    ///    Hideg Buildnél élesben MÉRVE 7,4-8,5 s, level 8-on
    ///    3 x 396 294 x 48 bájt = 54,4 MiB.
    ///  - <see cref="KindTileCenters"/> (ND-64/ND-131): a hidrológia
    ///    TILE-KÖZÉPPONT bázisa, EGY tömb (nincs normál-számítás, tehát nincs
    ///    u/v eltolás). Hideg Buildnél élesben MÉRVE 2,27 s (a
    ///    `hydrology(...) terrainBasis=` PerfLog-sor), level 8-on
    ///    393 216 x 48 bájt = 18,0 MiB.
    ///
    /// A `Kind` azért külön kulcs-elem, hogy a két gyorsítótár elkülönítése NE
    /// azon múljon, hogy a darabszámuk ((n+1)² vs n² laponként) és a
    /// tömbszámuk véletlenül úgyis különbözik - az ilyen "esetleges"
    /// megkülönböztetés pont az, ami egy későbbi változtatásnál csendben
    /// elromlik.
    ///
    /// SZÁNDÉKOSAN MOTORFÜGGETLEN (a `WorldGen.Viewer.Lod` asmdef
    /// `noEngineReferences=true`), hogy az offline tesztprojekt fordítsa és
    /// futtassa: a formátum és az érvénytelenítés helyessége így Unity nélkül
    /// igazolható. A hívó ad `Stream`-et; ez az osztály nem ismer fájlrendszert.
    ///
    /// ===================================================================
    /// A HÁROM KIKÖTÉS, AMIT A FELADAT ELŐÍRT, ÉS HOGY MELYIK HOGY TELJESÜL
    /// ===================================================================
    ///
    /// 1. "A cache NEM LEHET world-state." Teljesül szerkezetileg: a tartalom
    ///    tiszta függvénye a kulcsnak (worldSeed, level, epszilon), és a
    ///    betöltés BÁRMILYEN kétségnél `null`-t ad - nincs "javítás" útvonal,
    ///    nincs részleges betöltés. A fájl törlése csak lassít, nem változtat
    ///    világot.
    ///
    /// 2. "Eltérő algoritmusverziót NEM tölthet be." Ezt NEM kézzel emelt
    ///    verziókonstansra bízzuk - az pont az a törékeny megoldás, ami akkor
    ///    bukik el, amikor valaki a `DomainWarp`-ot vagy a
    ///    `CrustElevation.ComputeNoiseBasis`-t módosítja és elfelejti emelni a
    ///    számot. Helyette ÚJRASZÁMOLÁSSAL VALIDÁLUNK: betöltéskor
    ///    <see cref="ValidationSampleCount"/> szélesen szórt bejegyzést
    ///    ÚJRASZÁMOLUNK és BITRE hasonlítunk. Egy algoritmus-változás
    ///    gyakorlatilag minden bejegyzést megváltoztat, tehát már egyetlen
    ///    minta is elkapja; a szórt mintavétel a részleges (pl. csak bizonyos
    ///    tartományt érintő) változást is megfogja.
    ///
    ///    MARADÉK KOCKÁZAT, kimondva: ha egy változás a bejegyzéseknek csak
    ///    töredékét érinti, a mintavétel elvileg átengedheti. 1024 minta
    ///    mellett egy 1%-nyi bejegyzést érintő változás észlelési valószínűsége
    ///    ~99,996%; egyetlen bejegyzést érintőé viszont elhanyagolható. Ez
    ///    tudatos kompromisszum a teljes újraszámolás (= a cache értelmének
    ///    elvesztése) helyett.
    ///
    /// 3. "Méret-, I/O- és invalidációs terv kell." Ld. a hívó oldalon a
    ///    könyvtár-méretkorlátot; itt a formátum oldala: a fejléc minden
    ///    kulcs-elemet tartalmaz, a tartalomról 64 bites ellenőrzőösszeg
    ///    készül (csonkolás/sérülés ellen - ez NEM helyettesíti a 2. pontot,
    ///    mert egy MÁSIK algoritmussal írt fájl ellenőrzőösszege is helyes
    ///    lenne), és bármelyik eltérésnél a betöltés `null`.
    /// </summary>
    public static class TerrainBasisDiskCache
    {
        /// <summary>"WGTB" + a FÁJLFORMÁTUM (nem az algoritmus) verziója.</summary>
        private const ulong Magic = 0x5747544230303032UL; // "WGTB0002"

        /// <summary>
        /// A MOSTANI fájlformátum névelőtagja. A takarítás ez alapján ismeri fel
        /// az elavult (korábbi formátumú) fájlokat - ezeket SOHA nem olvassuk
        /// többé, tehát csak a helyet foglalnák a kvótából.
        ///
        /// A FORMÁTUM MINDEN MEGVÁLTOZÁSAKOR ezt is emelni kell (a `Magic`
        /// verziószámával együtt) - különben a régi fájlok neve illeszkedne, a
        /// tartalmuk viszont nem, és minden hideg Build újra beolvasná és újra
        /// elutasítaná őket.
        /// </summary>
        public const string FileNamePrefix = "basis_k";

        /// <summary>
        /// A takarítás szűrője: ez a fájl a MOSTANI formátum nevét viseli-e.
        /// A hívó (kvóta-kezelés) a `basis_*.bin` mintával gyűjti a fájlokat -
        /// ami a KORÁBBI formátumokat is elkapja -, és ami ezen a szűrőn
        /// elbukik, azt azonnal törli.
        /// </summary>
        public static bool IsCurrentFormatFileName(string fileName) =>
            fileName != null && fileName.StartsWith(FileNamePrefix, StringComparison.Ordinal)
            && fileName.EndsWith(".bin", StringComparison.Ordinal);

        /// <summary>Statikus SAROK-bázis (ND-63): három tömb, normál-epszilonnal.</summary>
        public const int KindStaticCorners = 0;

        /// <summary>Hidrológiai TILE-KÖZÉPPONT bázis (ND-64): egy tömb, epszilon nélkül.</summary>
        public const int KindTileCenters = 1;

        /// <summary>Egy <see cref="TerrainPointBasis"/> bájtmérete a fájlban (6 double).</summary>
        public const int BasisByteSize = 48;

        /// <summary>Az újraszámolással ellenőrzött bejegyzések száma. Ld. az osztály-doksit.</summary>
        public const int ValidationSampleCount = 1024;

        /// <summary>A gyorsítótár kulcsa - MINDEN, amitől a tartalom függ.</summary>
        public readonly struct Key : IEquatable<Key>
        {
            public readonly ulong WorldSeed;
            public readonly int Level;

            /// <summary>
            /// A normál véges-differencia UV-eltolása. CSAK a
            /// <see cref="KindStaticCorners"/> tartalmára hat; a
            /// <see cref="KindTileCenters"/> nem számol normált, ezért ott 0.0
            /// - ez nem "hiányzó" érték, hanem az, hogy a tartalom tényleg nem
            /// függ tőle.
            /// </summary>
            public readonly double NormalSampleEpsilonUV;

            public readonly int Count;

            /// <summary>Hány <see cref="TerrainPointBasis"/> tömb van a fájlban (1 vagy 3).</summary>
            public readonly int ArrayCount;

            /// <summary>Melyik fogyasztó adata - ld. <see cref="KindStaticCorners"/>.</summary>
            public readonly int Kind;

            public Key(ulong worldSeed, int level, double normalSampleEpsilonUV, int count,
                int arrayCount, int kind)
            {
                WorldSeed = worldSeed;
                Level = level;
                NormalSampleEpsilonUV = normalSampleEpsilonUV;
                Count = count;
                ArrayCount = arrayCount;
                Kind = kind;
            }

            public bool Equals(Key other) =>
                WorldSeed == other.WorldSeed && Level == other.Level && Count == other.Count
                && ArrayCount == other.ArrayCount && Kind == other.Kind
                && BitConverter.DoubleToInt64Bits(NormalSampleEpsilonUV)
                   == BitConverter.DoubleToInt64Bits(other.NormalSampleEpsilonUV);

            public override bool Equals(object? obj) => obj is Key other && Equals(other);

            public override int GetHashCode() =>
                WorldSeed.GetHashCode() ^ (Level * 397) ^ Count ^ (ArrayCount * 7919) ^ (Kind * 104729)
                ^ BitConverter.DoubleToInt64Bits(NormalSampleEpsilonUV).GetHashCode();

            /// <summary>Fájlnévbe illő, ütközésmentes alak (a hívó teszi mellé a könyvtárat).</summary>
            public string ToFileName() =>
                $"{FileNamePrefix}{Kind}_{WorldSeed:x16}_L{Level}"
                + $"_e{BitConverter.DoubleToInt64Bits(NormalSampleEpsilonUV):x16}_{Count}x{ArrayCount}.bin";
        }

        /// <summary>
        /// A bázistömbök - a hívó ilyen alakban tartja őket. A sorrend a hívó
        /// dolga, a formátum csak a darabszámot ismeri (ld. <see cref="Key.ArrayCount"/>).
        /// </summary>
        public sealed class Payload
        {
            public readonly TerrainPointBasis[][] Arrays;

            public Payload(params TerrainPointBasis[][] arrays)
            {
                if (arrays == null || arrays.Length == 0)
                    throw new ArgumentException("Legalabb egy tomb kell.", nameof(arrays));
                Arrays = arrays;
            }

            public int ArrayCount => Arrays.Length;
        }

        /// <summary>A fejléc mérete bájtban: magic + seed + level + epszilon + count + arrayCount + kind + hash.</summary>
        private const int HeaderBytes = 8 + 8 + 4 + 8 + 4 + 4 + 4 + 8;

        public static long ExpectedFileSize(int count, int arrayCount) =>
            HeaderBytes + (long)arrayCount * count * BasisByteSize;

        /// <summary>
        /// Kiírja a bázist a streambe. A hívó felel azért, hogy a
        /// <paramref name="key"/> tényleg ahhoz a tartalomhoz tartozzon, amit
        /// átad - ezt a betöltés mintavétele úgyis ellenőrzi.
        /// </summary>
        public static void Write(Stream stream, in Key key, Payload payload)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.ArrayCount != key.ArrayCount)
                throw new ArgumentException("A tombok szama nem egyezik a kulccsal.", nameof(payload));
            foreach (TerrainPointBasis[] array in payload.Arrays)
                if (array.Length != key.Count)
                    throw new ArgumentException("A tömbök mérete nem egyezik a kulcsban megadott darabszámmal.", nameof(payload));

            byte[] body = Serialize(payload, key.Count);
            ulong hash = Fnv1a64(body);

            var header = new byte[HeaderBytes];
            int offset = 0;
            WriteUInt64(header, ref offset, Magic);
            WriteUInt64(header, ref offset, key.WorldSeed);
            WriteInt32(header, ref offset, key.Level);
            WriteUInt64(header, ref offset, (ulong)BitConverter.DoubleToInt64Bits(key.NormalSampleEpsilonUV));
            WriteInt32(header, ref offset, key.Count);
            WriteInt32(header, ref offset, key.ArrayCount);
            WriteInt32(header, ref offset, key.Kind);
            WriteUInt64(header, ref offset, hash);

            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
        }

        /// <summary>
        /// Betölti a bázist, ha MINDEN ellenőrzés átmegy; különben `null`.
        ///
        /// A <paramref name="recompute"/> visszahívás adott indexre ÚJRASZÁMOLJA
        /// a három bázist - ez dönti el, hogy a fájl a MOSTANI algoritmussal
        /// készült-e. A hívó adja, mert a geometria (melyik index melyik
        /// sarokpont) a hívó tudása, nem ezé a formátumé.
        ///
        /// A `null` visszatérésnek NINCS részleges változata: vagy teljes,
        /// ellenőrzött adat jön vissza, vagy semmi.
        /// </summary>
        public static Payload? TryRead(
            Stream stream, in Key expectedKey,
            Action<int, TerrainPointBasis[][]> recompute,
            out string? rejectionReason)
        {
            rejectionReason = null;
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (recompute == null) throw new ArgumentNullException(nameof(recompute));
            if (expectedKey.Count <= 0) { rejectionReason = "ervenytelen darabszam"; return null; }
            if (expectedKey.ArrayCount <= 0) { rejectionReason = "ervenytelen tombszam"; return null; }

            var header = new byte[HeaderBytes];
            if (!ReadExactly(stream, header, header.Length)) { rejectionReason = "csonka fejlec"; return null; }

            int offset = 0;
            if (ReadUInt64(header, ref offset) != Magic) { rejectionReason = "ismeretlen formatum"; return null; }
            ulong seed = ReadUInt64(header, ref offset);
            int level = ReadInt32(header, ref offset);
            double epsilon = BitConverter.Int64BitsToDouble((long)ReadUInt64(header, ref offset));
            int count = ReadInt32(header, ref offset);
            int arrayCount = ReadInt32(header, ref offset);
            int kind = ReadInt32(header, ref offset);
            ulong storedHash = ReadUInt64(header, ref offset);

            var fileKey = new Key(seed, level, epsilon, count, arrayCount, kind);
            if (!fileKey.Equals(expectedKey)) { rejectionReason = "kulcs-elteres"; return null; }

            long bodyLength = (long)arrayCount * count * BasisByteSize;
            if (bodyLength > int.MaxValue) { rejectionReason = "tul nagy tartalom"; return null; }
            var body = new byte[(int)bodyLength];
            if (!ReadExactly(stream, body, body.Length)) { rejectionReason = "csonka tartalom"; return null; }
            if (Fnv1a64(body) != storedHash) { rejectionReason = "ellenorzoosszeg-elteres"; return null; }

            Payload payload = Deserialize(body, count, arrayCount);

            // ALGORITMUS-AZONOSSAG: szelesen szort mintak UJRASZAMOLASSAL.
            var one = new TerrainPointBasis[arrayCount][];
            for (int a = 0; a < arrayCount; a++) one[a] = new TerrainPointBasis[1];
            foreach (int index in ValidationIndices(count))
            {
                recompute(index, one);
                for (int a = 0; a < arrayCount; a++)
                {
                    if (!SameBasis(one[a][0], payload.Arrays[a][index]))
                    {
                        rejectionReason = $"algoritmus-elteres a(z) {index}. bejegyzesnel";
                        return null;
                    }
                }
            }

            return payload;
        }

        /// <summary>
        /// A validációs minta-indexek: DETERMINISZTIKUS, széles szórással. A
        /// lépésköz egy nagy prím maradéka, ami biztosítja, hogy a minták ne
        /// egyetlen lapra vagy tartományra essenek (egy egyszerű `i * count /
        /// N` szabályos rácsot adna, ami épp a periodikus eltéréseket nézné el).
        /// Az első és az utolsó indexet mindig tartalmazza - a csonkolás és az
        /// eltolódás leggyakoribb tünetei ott jelentkeznek.
        /// </summary>
        public static int[] ValidationIndices(int count)
        {
            if (count <= 0) return Array.Empty<int>();
            int samples = Math.Min(ValidationSampleCount, count);
            var result = new int[samples];
            if (samples >= 1) result[0] = 0;
            if (samples >= 2) result[1] = count - 1;

            const long stride = 2654435761L; // Knuth-fele szorzoprim
            long position = 0;
            for (int i = 2; i < samples; i++)
            {
                position = (position + stride) % count;
                result[i] = (int)position;
            }
            return result;
        }

        private static bool SameBasis(in TerrainPointBasis a, in TerrainPointBasis b) =>
            a.WarpedX.Equals(b.WarpedX) && a.WarpedY.Equals(b.WarpedY) && a.WarpedZ.Equals(b.WarpedZ)
            && a.PrimaryNoise.Equals(b.PrimaryNoise) && a.MountainMask.Equals(b.MountainMask)
            && a.SecondaryNoise.Equals(b.SecondaryNoise);

        private static byte[] Serialize(Payload payload, int count)
        {
            var body = new byte[(long)payload.ArrayCount * count * BasisByteSize];
            int offset = 0;
            foreach (TerrainPointBasis[] array in payload.Arrays)
                WriteArray(body, ref offset, array, count);
            return body;
        }

        private static Payload Deserialize(byte[] body, int count, int arrayCount)
        {
            int offset = 0;
            var arrays = new TerrainPointBasis[arrayCount][];
            for (int a = 0; a < arrayCount; a++) arrays[a] = ReadArray(body, ref offset, count);
            return new Payload(arrays);
        }

        private static void WriteArray(byte[] target, ref int offset, TerrainPointBasis[] source, int count)
        {
            for (int i = 0; i < count; i++)
            {
                TerrainPointBasis b = source[i];
                WriteDouble(target, ref offset, b.WarpedX);
                WriteDouble(target, ref offset, b.WarpedY);
                WriteDouble(target, ref offset, b.WarpedZ);
                WriteDouble(target, ref offset, b.PrimaryNoise);
                WriteDouble(target, ref offset, b.MountainMask);
                WriteDouble(target, ref offset, b.SecondaryNoise);
            }
        }

        private static TerrainPointBasis[] ReadArray(byte[] source, ref int offset, int count)
        {
            var result = new TerrainPointBasis[count];
            for (int i = 0; i < count; i++)
            {
                double wx = ReadDouble(source, ref offset);
                double wy = ReadDouble(source, ref offset);
                double wz = ReadDouble(source, ref offset);
                double primary = ReadDouble(source, ref offset);
                double mask = ReadDouble(source, ref offset);
                double secondary = ReadDouble(source, ref offset);
                result[i] = new TerrainPointBasis(wx, wy, wz, primary, mask, secondary);
            }
            return result;
        }

        // Little-endian, EXPLICIT bajtonkenti irassal - a BitConverter
        // endianness-e platformfuggo lenne, a fajlnak viszont hordozhatonak
        // kell lennie (a determinizmus-elvarasok szellemeben).
        private static void WriteUInt64(byte[] target, ref int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) target[offset + i] = (byte)(value >> (8 * i));
            offset += 8;
        }

        private static ulong ReadUInt64(byte[] source, ref int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++) value |= (ulong)source[offset + i] << (8 * i);
            offset += 8;
            return value;
        }

        private static void WriteInt32(byte[] target, ref int offset, int value)
        {
            for (int i = 0; i < 4; i++) target[offset + i] = (byte)(value >> (8 * i));
            offset += 4;
        }

        private static int ReadInt32(byte[] source, ref int offset)
        {
            int value = 0;
            for (int i = 0; i < 4; i++) value |= source[offset + i] << (8 * i);
            offset += 4;
            return value;
        }

        private static void WriteDouble(byte[] target, ref int offset, double value)
            => WriteUInt64(target, ref offset, (ulong)BitConverter.DoubleToInt64Bits(value));

        private static double ReadDouble(byte[] source, ref int offset)
            => BitConverter.Int64BitsToDouble((long)ReadUInt64(source, ref offset));

        /// <summary>FNV-1a 64 bit - sérülés/csonkolás ellen, NEM algoritmus-azonosításra.</summary>
        public static ulong Fnv1a64(byte[] data)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int length)
        {
            int read = 0;
            while (read < length)
            {
                int chunk = stream.Read(buffer, read, length - read);
                if (chunk <= 0) return false;
                read += chunk;
            }
            return true;
        }
    }
}
