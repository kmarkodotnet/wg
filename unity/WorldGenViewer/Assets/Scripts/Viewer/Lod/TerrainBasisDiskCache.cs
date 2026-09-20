using System;
using System.IO;
using WorldGen.Core.Tectonics;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// A statikus sarok-terrain-bázis (ND-63) LEMEZ-gyorsítótárának formátuma és
    /// ellenőrzése. Hideg Buildnél a bázis kiszámítása élesben MÉRVE
    /// 7,4-8,5 másodperc (a `terrainBasis=` PerfLog-sor), level 8-on
    /// 3 x 396 294 x 48 bájt = 54,4 MiB nyers adat.
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
        private const ulong Magic = 0x5747544230303031UL; // "WGTB0001"

        /// <summary>Egy <see cref="TerrainPointBasis"/> bájtmérete a fájlban (6 double).</summary>
        public const int BasisByteSize = 48;

        /// <summary>Az újraszámolással ellenőrzött bejegyzések száma. Ld. az osztály-doksit.</summary>
        public const int ValidationSampleCount = 1024;

        /// <summary>A gyorsítótár kulcsa - MINDEN, amitől a tartalom függ.</summary>
        public readonly struct Key : IEquatable<Key>
        {
            public readonly ulong WorldSeed;
            public readonly int Level;
            public readonly double NormalSampleEpsilonUV;
            public readonly int Count;

            public Key(ulong worldSeed, int level, double normalSampleEpsilonUV, int count)
            {
                WorldSeed = worldSeed;
                Level = level;
                NormalSampleEpsilonUV = normalSampleEpsilonUV;
                Count = count;
            }

            public bool Equals(Key other) =>
                WorldSeed == other.WorldSeed && Level == other.Level && Count == other.Count
                && BitConverter.DoubleToInt64Bits(NormalSampleEpsilonUV)
                   == BitConverter.DoubleToInt64Bits(other.NormalSampleEpsilonUV);

            public override bool Equals(object? obj) => obj is Key other && Equals(other);

            public override int GetHashCode() =>
                WorldSeed.GetHashCode() ^ (Level * 397) ^ Count
                ^ BitConverter.DoubleToInt64Bits(NormalSampleEpsilonUV).GetHashCode();

            /// <summary>Fájlnévbe illő, ütközésmentes alak (a hívó teszi mellé a könyvtárat).</summary>
            public string ToFileName() =>
                $"basis_{WorldSeed:x16}_L{Level}_e{BitConverter.DoubleToInt64Bits(NormalSampleEpsilonUV):x16}_{Count}.bin";
        }

        /// <summary>A három bázistömb - a hívó ilyen alakban tartja őket.</summary>
        public sealed class Payload
        {
            public TerrainPointBasis[] Center;
            public TerrainPointBasis[] U;
            public TerrainPointBasis[] V;

            public Payload(TerrainPointBasis[] center, TerrainPointBasis[] u, TerrainPointBasis[] v)
            {
                Center = center; U = u; V = v;
            }
        }

        /// <summary>A fejléc mérete bájtban: magic + seed + level + epszilon + count + hash.</summary>
        private const int HeaderBytes = 8 + 8 + 4 + 8 + 4 + 8;

        public static long ExpectedFileSize(int count) =>
            HeaderBytes + 3L * count * BasisByteSize;

        /// <summary>
        /// Kiírja a bázist a streambe. A hívó felel azért, hogy a
        /// <paramref name="key"/> tényleg ahhoz a tartalomhoz tartozzon, amit
        /// átad - ezt a betöltés mintavétele úgyis ellenőrzi.
        /// </summary>
        public static void Write(Stream stream, in Key key, Payload payload)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Center.Length != key.Count || payload.U.Length != key.Count || payload.V.Length != key.Count)
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
            Action<int, TerrainPointBasis[], TerrainPointBasis[], TerrainPointBasis[]> recompute,
            out string? rejectionReason)
        {
            rejectionReason = null;
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (recompute == null) throw new ArgumentNullException(nameof(recompute));
            if (expectedKey.Count <= 0) { rejectionReason = "ervenytelen darabszam"; return null; }

            var header = new byte[HeaderBytes];
            if (!ReadExactly(stream, header, header.Length)) { rejectionReason = "csonka fejlec"; return null; }

            int offset = 0;
            if (ReadUInt64(header, ref offset) != Magic) { rejectionReason = "ismeretlen formatum"; return null; }
            ulong seed = ReadUInt64(header, ref offset);
            int level = ReadInt32(header, ref offset);
            double epsilon = BitConverter.Int64BitsToDouble((long)ReadUInt64(header, ref offset));
            int count = ReadInt32(header, ref offset);
            ulong storedHash = ReadUInt64(header, ref offset);

            var fileKey = new Key(seed, level, epsilon, count);
            if (!fileKey.Equals(expectedKey)) { rejectionReason = "kulcs-elteres"; return null; }

            long bodyLength = 3L * count * BasisByteSize;
            if (bodyLength > int.MaxValue) { rejectionReason = "tul nagy tartalom"; return null; }
            var body = new byte[(int)bodyLength];
            if (!ReadExactly(stream, body, body.Length)) { rejectionReason = "csonka tartalom"; return null; }
            if (Fnv1a64(body) != storedHash) { rejectionReason = "ellenorzoosszeg-elteres"; return null; }

            Payload payload = Deserialize(body, count);

            // ALGORITMUS-AZONOSSAG: szelesen szort mintak UJRASZAMOLASSAL.
            var oneCenter = new TerrainPointBasis[1];
            var oneU = new TerrainPointBasis[1];
            var oneV = new TerrainPointBasis[1];
            foreach (int index in ValidationIndices(count))
            {
                recompute(index, oneCenter, oneU, oneV);
                if (!SameBasis(oneCenter[0], payload.Center[index])
                    || !SameBasis(oneU[0], payload.U[index])
                    || !SameBasis(oneV[0], payload.V[index]))
                {
                    rejectionReason = $"algoritmus-elteres a(z) {index}. bejegyzesnel";
                    return null;
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
            var body = new byte[3L * count * BasisByteSize];
            int offset = 0;
            WriteArray(body, ref offset, payload.Center, count);
            WriteArray(body, ref offset, payload.U, count);
            WriteArray(body, ref offset, payload.V, count);
            return body;
        }

        private static Payload Deserialize(byte[] body, int count)
        {
            int offset = 0;
            TerrainPointBasis[] center = ReadArray(body, ref offset, count);
            TerrainPointBasis[] u = ReadArray(body, ref offset, count);
            TerrainPointBasis[] v = ReadArray(body, ref offset, count);
            return new Payload(center, u, v);
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
