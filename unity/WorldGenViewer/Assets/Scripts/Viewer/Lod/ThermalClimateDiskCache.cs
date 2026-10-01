using System;
using System.IO;
using WorldGen.Core.Climate;
using WorldGen.Core.Hydrology;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// Az ND-158/159 éves éghajlat LEMEZ-gyorsítótára.
    ///
    /// MIÉRT KELL. A kétmenetes éves éghajlat level 6-on MÉRVE <b>117,1 s</b>
    /// (Release, párhuzamos lokális lépés). Az ND-161 mérése kizárta az olcsóbb
    /// utat (durvább rácson számolt éghajlat: a legjobb változat is minden
    /// hetedik jégcellát rossz helyre tenné, 4× gyorsulásért), tehát a
    /// költséget ARCHITEKTÚRÁVAL kell kezelni. Ez a fele: ugyanazt a világot
    /// másodszor már ne számoljuk újra.
    ///
    /// SZÁNDÉKOSAN MOTORFÜGGETLEN (a <c>WorldGen.Viewer.Lod</c> asmdef
    /// <c>noEngineReferences=true</c>), mint a <see cref="TerrainBasisDiskCache"/>:
    /// a formátum és az érvénytelenítés helyessége Unity nélkül igazolható.
    /// A hívó ad <see cref="Stream"/>-et; ez az osztály nem ismer fájlrendszert.
    ///
    /// ===================================================================
    /// A HÁROM KIKÖTÉS (a TerrainBasisDiskCache-ével azonos), ÉS HOGY ITT
    /// MELYIK HOGYAN TELJESÜL
    /// ===================================================================
    ///
    /// 1. <b>„A cache NEM LEHET world-state."</b> Teljesül szerkezetileg: a
    ///    tartalom tiszta függvénye a kulcsnak, a betöltés bármilyen kétségnél
    ///    <c>null</c>-t ad, nincs „javítás" útvonal és nincs részleges
    ///    betöltés. A fájl törlése csak lassít, nem változtat világot.
    ///
    /// 2. <b>„Eltérő algoritmusverziót NEM tölthet be."</b> Itt NEM működik a
    ///    terrain-cache megoldása (néhány bejegyzés újraszámolása és bitre
    ///    hasonlítása), mert ennek a tartalomnak EGYETLEN bejegyzése sem
    ///    számolható ki a teljes, 117 s-os futás nélkül — a cella-érték a
    ///    teljes rács csatolt megoldásától függ.
    ///
    ///    Helyette <b>RÖVID KANONIKUS ELŐTAG</b>: a hívó
    ///    <see cref="ComputeSolverFingerprint"/>-tel lefuttat
    ///    <see cref="FingerprintTicks"/> ticket a kanonikus kezdőállapotból,
    ///    és a kapott θ-mezőkről hash-t képez. Ez MINDEN numerikus változást
    ///    elkap a solverben, a szélben, a bázisban és a paraméterekben —
    ///    ugyanazt a kódot futtatja, csak sokkal rövidebben. A 96 tickes
    ///    előtag a korábbi modellben level 6-on ~0,4 s volt a 117 s-os teljes
    ///    éves futáshoz képest; az ND-168 utáni költséget újra kell mérni.
    ///
    ///    Kiegészítésként a fejléc tartalmazza az ND-143
    ///    <see cref="SurfaceTemperatureField.ModelIdentity"/> hash-t (a teljes
    ///    fizikai bemenet: seed, geológiai idő, pálya, rács, tengerszint,
    ///    felszíntípusok, magasságok, paraméterek és modellverziók), valamint
    ///    a MINTAVÉTELI szabály eredményét (a mintanapok listáját) — így a
    ///    mintavétel megváltozása is kulcs-eltérést ad.
    ///
    ///    MARADÉK KOCKÁZAT, kimondva: ha valaki KIZÁRÓLAG az AGGREGÁCIÓ
    ///    aritmetikáját írja át (pl. az éves átlag képletét), miközben a
    ///    solver, a mintanapok és a modellazonosító változatlan, azt sem az
    ///    előtag-hash, sem a kulcs nem fogja meg. Ez tudatos kompromisszum;
    ///    az aggregációt a Core tesztjei és a Python-orákulum fedik, és egy
    ///    ilyen változás amúgy is verzió-emeléssel jár (ND-158).
    ///
    /// 3. <b>„Méret-, I/O- és invalidációs terv kell."</b> A méret a
    ///    cellaszámmal lineáris: level 6-on (24 576 cella)
    ///    <b>~3,8 MiB</b> fájlonként — két éves statisztika (10-10 double tömb)
    ///    plusz a két jégosztály- és a felszíntípus-tömb. Ez nagyságrenddel
    ///    kisebb, mint a terrain-bázis 18–54 MiB-ja, tehát a kvóta-nyomás is
    ///    kisebb. A tartalomról 64 bites ellenőrzőösszeg készül
    ///    (csonkolás/sérülés ellen — ez NEM helyettesíti a 2. pontot), és
    ///    bármelyik eltérésnél a betöltés <c>null</c>.
    /// </summary>
    public static class ThermalClimateDiskCache
    {
        /// <summary>"WGTC" + a FÁJLFORMÁTUM (nem az algoritmus) verziója.</summary>
        private const ulong Magic = 0x5747544330303032UL; // "WGTC0002"

        /// <summary>
        /// A MOSTANI fájlformátum névelőtagja — a takarítás ez alapján ismeri
        /// fel az elavult fájlokat. A formátum minden megváltozásakor ezt is
        /// emelni kell, a <see cref="Magic"/> verziószámával együtt.
        /// </summary>
        public const string FileNamePrefix = "climate_k2";

        /// <summary>A takarítás szűrője: ez a fájl a MOSTANI formátum nevét viseli-e.</summary>
        public static bool IsCurrentFormatFileName(string fileName) =>
            fileName != null && fileName.StartsWith(FileNamePrefix, StringComparison.Ordinal)
            && fileName.EndsWith(".bin", StringComparison.Ordinal);

        /// <summary>
        /// Az algoritmus-azonosságot ellenőrző kanonikus előtag hossza tickben.
        /// Egy modellnap (96 tick) — elég hosszú ahhoz, hogy az advekció, a
        /// szél-visszacsatolás és a bázis napi menete is beleszóljon, és elég
        /// rövid ahhoz, hogy a teljes futás ~0,3%-a legyen.
        /// </summary>
        public const int FingerprintTicks = SimulationTime.TicksPerDay;

        /// <summary>A gyorsítótár kulcsa — MINDEN, amitől a tartalom függ.</summary>
        public readonly struct Key : IEquatable<Key>
        {
            /// <summary>ND-143: a világ és a hőmodell bemeneteinek kanonikus azonosítója.</summary>
            public readonly string ModelIdentity;

            public readonly int CellCount;

            /// <summary>A mintanapok — a MINTAVÉTELI szabály eredménye, nem a szabály paraméterei.</summary>
            public readonly long[] SampleDays;

            /// <summary>
            /// A tartós-jég percentilis bitmintája (ND-159), vagy
            /// <see cref="AbsoluteThresholdMarker"/>, ha a régi, abszolút
            /// küszöb volt érvényben. Bitminta, nem <c>double</c>: a kulcs
            /// összehasonlítása így pontosan definiált NaN nélkül is.
            /// </summary>
            public readonly long IcePercentileBits;

            /// <summary>A rövid kanonikus előtag hash-e — ld. az osztály-doksi 2. pontját.</summary>
            public readonly ulong SolverFingerprint;

            public Key(string modelIdentity, int cellCount, long[] sampleDays,
                long icePercentileBits, ulong solverFingerprint)
            {
                ModelIdentity = modelIdentity ?? throw new ArgumentNullException(nameof(modelIdentity));
                CellCount = cellCount;
                SampleDays = sampleDays ?? throw new ArgumentNullException(nameof(sampleDays));
                IcePercentileBits = icePercentileBits;
                SolverFingerprint = solverFingerprint;
            }

            /// <summary>A „nincs percentilis, abszolút küszöb" jelölése. Nem lehet érvényes double bitminta 0..1-ből.</summary>
            public const long AbsoluteThresholdMarker = long.MinValue;

            public static long PercentileBits(double? percentile) =>
                percentile.HasValue ? BitConverter.DoubleToInt64Bits(percentile.Value) : AbsoluteThresholdMarker;

            public bool Equals(Key other)
            {
                if (CellCount != other.CellCount || IcePercentileBits != other.IcePercentileBits
                    || SolverFingerprint != other.SolverFingerprint
                    || !string.Equals(ModelIdentity, other.ModelIdentity, StringComparison.Ordinal)
                    || SampleDays.Length != other.SampleDays.Length)
                    return false;
                for (int i = 0; i < SampleDays.Length; i++)
                    if (SampleDays[i] != other.SampleDays[i]) return false;
                return true;
            }

            public override bool Equals(object? obj) => obj is Key other && Equals(other);

            public override int GetHashCode()
            {
                int hash = ModelIdentity.GetHashCode() ^ (CellCount * 397)
                    ^ IcePercentileBits.GetHashCode() ^ SolverFingerprint.GetHashCode();
                for (int i = 0; i < SampleDays.Length; i++) hash = hash * 31 + SampleDays[i].GetHashCode();
                return hash;
            }

            /// <summary>
            /// Fájlnévbe illő, ütközésmentes alak. A modellazonosító már
            /// SHA-256 hex, de a teljes hossza fölösleges a fájlnévben — a
            /// tartalmi egyezést úgyis a fejléc TELJES kulcsa dönti el, a név
            /// csak gyors keresésre való.
            /// </summary>
            public string ToFileName()
            {
                string shortIdentity = ModelIdentity.Length > 24
                    ? ModelIdentity.Substring(0, 24) : ModelIdentity;
                return $"{FileNamePrefix}_{shortIdentity}_{CellCount}"
                    + $"_d{SampleDays.Length}_p{IcePercentileBits:x16}_f{SolverFingerprint:x16}.bin";
            }
        }

        /// <summary>
        /// Egy éves statisztika tíz tömbje. A <see cref="ThermalAnnualStatistics"/>
        /// csak olvasható nézeteket ad, ezért a kimentéshez és a
        /// visszaépítéshez ez a nyers alak kell.
        /// </summary>
        public sealed class AnnualArrays
        {
            public double[] MeanSurfaceK = Array.Empty<double>();
            public double[] MeanAirK = Array.Empty<double>();
            public double[] MinSurfaceK = Array.Empty<double>();
            public double[] MaxSurfaceK = Array.Empty<double>();
            public double[] MinAirK = Array.Empty<double>();
            public double[] MaxAirK = Array.Empty<double>();
            public double[] MeanWindX = Array.Empty<double>();
            public double[] MeanWindY = Array.Empty<double>();
            public double[] MeanWindZ = Array.Empty<double>();
            public double[] MeanWindSpeedMs = Array.Empty<double>();

            public static AnnualArrays From(ThermalAnnualStatistics stats)
            {
                if (stats == null) throw new ArgumentNullException(nameof(stats));
                return new AnnualArrays
                {
                    MeanSurfaceK = Copy(stats.MeanSurfaceK),
                    MeanAirK = Copy(stats.MeanAirK),
                    MinSurfaceK = Copy(stats.MinSurfaceK),
                    MaxSurfaceK = Copy(stats.MaxSurfaceK),
                    MinAirK = Copy(stats.MinAirK),
                    MaxAirK = Copy(stats.MaxAirK),
                    MeanWindX = CopyRequired(stats.MeanWindX),
                    MeanWindY = CopyRequired(stats.MeanWindY),
                    MeanWindZ = CopyRequired(stats.MeanWindZ),
                    MeanWindSpeedMs = CopyRequired(stats.MeanWindSpeedMs),
                };
            }

            private static double[] Copy(System.Collections.ObjectModel.ReadOnlyCollection<double> source)
            {
                var result = new double[source.Count];
                for (int i = 0; i < result.Length; i++) result[i] = source[i];
                return result;
            }

            private static double[] CopyRequired(System.Collections.ObjectModel.ReadOnlyCollection<double>? source)
            {
                if (source == null)
                    throw new ArgumentException("A cache-hez az éves szélmező minden komponense szükséges.");
                return Copy(source);
            }

            internal double[][] All => new[] { MeanSurfaceK, MeanAirK, MinSurfaceK, MaxSurfaceK, MinAirK, MaxAirK,
                MeanWindX, MeanWindY, MeanWindZ, MeanWindSpeedMs };
        }

        /// <summary>A gyorsítótárazott tartalom — pontosan az, ami a <see cref="ThermalClimate"/>-ből kell.</summary>
        public sealed class Payload
        {
            public AnnualArrays IceFree = new AnnualArrays();
            public AnnualArrays Refined = new AnnualArrays();
            public LakesIceErosion.IceClass[] IceFreeClass = Array.Empty<LakesIceErosion.IceClass>();
            public LakesIceErosion.IceClass[] RefinedClass = Array.Empty<LakesIceErosion.IceClass>();
            public SurfaceThermalKind[] RefinedKinds = Array.Empty<SurfaceThermalKind>();
            public double IceFreeThresholdK;
            public double RefinedThresholdK;
            public double SeasonalSnowThresholdK;
            public int ReclassifiedCells;
            public bool SecondPassSkipped;

            public static Payload From(ThermalClimate climate)
            {
                if (climate == null) throw new ArgumentNullException(nameof(climate));
                int count = climate.RefinedClass.Count;
                var iceFreeClass = new LakesIceErosion.IceClass[count];
                var refinedClass = new LakesIceErosion.IceClass[count];
                var refinedKinds = new SurfaceThermalKind[count];
                for (int c = 0; c < count; c++)
                {
                    iceFreeClass[c] = climate.IceFreeClass[c];
                    refinedClass[c] = climate.RefinedClass[c];
                    refinedKinds[c] = climate.RefinedKinds[c];
                }
                return new Payload
                {
                    IceFree = AnnualArrays.From(climate.IceFree),
                    Refined = AnnualArrays.From(climate.Refined),
                    IceFreeClass = iceFreeClass,
                    RefinedClass = refinedClass,
                    RefinedKinds = refinedKinds,
                    IceFreeThresholdK = climate.IceFreeThresholds.PermanentIceMeanK,
                    RefinedThresholdK = climate.RefinedThresholds.PermanentIceMeanK,
                    SeasonalSnowThresholdK = climate.RefinedThresholds.SeasonalSnowMinK,
                    ReclassifiedCells = climate.ReclassifiedCells,
                    SecondPassSkipped = climate.SecondPassSkipped,
                };
            }
        }

        /// <summary>
        /// A rövid kanonikus előtag hash-e (ld. az osztály-doksi 2. pontját).
        ///
        /// A <paramref name="field"/> friss, kanonikus állapotból indul, és
        /// <see cref="FingerprintTicks"/> ticket lép — ugyanazon a kódúton,
        /// mint a teljes számítás. A hash a θs és θa mezők NYERS bitmintáiból
        /// készül, rögzített sorrendben, tehát bitpontos és platformfüggetlen
        /// (ugyanúgy, ahogy a determinizmus maga).
        ///
        /// A hívó ugyanezt a mezőt használja a teljes számításhoz, ezért a
        /// lenyomat a TÉNYLEGESEN futó kódot méri — nem egy kézzel karbantartott
        /// verziószámot.
        ///
        /// A lenyomat a <paramref name="firstDay"/> BUCKETJÉNEK kanonikus
        /// kezdetétől függ, nem magától a naptól: egy 30 napos bucketen belüli
        /// napok ugyanazt adják. Ez nem hiányosság — a mintanapok listája
        /// külön kulcs-elem (<see cref="Key.SampleDays"/>), tehát a
        /// mintavétel megváltozását az fogja meg, nem ez.
        /// </summary>
        public static ulong ComputeSolverFingerprint(SurfaceTemperatureField field, long firstDay)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            var state = new ThermalSnapshot(field.Grid.CellCount);
            long firstTick = firstDay * SimulationTime.TicksPerDay;
            field.ResetCanonical(state, firstTick);
            for (int i = 0; i < FingerprintTicks; i++) field.Step(state);

            ulong hash = 1469598103934665603UL;
            for (int c = 0; c < state.ThetaS.Length; c++)
            {
                hash = MixDouble(hash, state.ThetaS[c]);
                hash = MixDouble(hash, state.ThetaA[c]);
            }
            return hash;
        }

        /// <summary>A fejléc fix része: magic + cellCount + napszám + percentilis + lenyomat + tartalom-hash.</summary>
        private const int FixedHeaderBytes = 8 + 4 + 4 + 8 + 8 + 8;

        /// <summary>Egy cella bájtmérete: 20 double + 2 jégosztály + 1 felszíntípus.</summary>
        private const int PerCellBytes = 20 * 8 + 3;

        /// <summary>A tartalom fix farka: három küszöb, az átsorolt szám és a rövidzár-jelző.</summary>
        private const int TrailerBytes = 3 * 8 + 4 + 1;

        public static long ExpectedFileSize(int cellCount, int identityByteCount, int sampleDayCount) =>
            FixedHeaderBytes + 4 + identityByteCount + (long)sampleDayCount * 8
            + (long)cellCount * PerCellBytes + TrailerBytes;

        /// <summary>
        /// Kiírja az éghajlatot a streambe. A hívó felel azért, hogy a
        /// <paramref name="key"/> ahhoz a tartalomhoz tartozzon, amit átad —
        /// a betöltés a lenyomattal úgyis ellenőrzi.
        /// </summary>
        public static void Write(Stream stream, in Key key, Payload payload)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            ValidatePayloadShape(key.CellCount, payload);

            byte[] body = Serialize(key, payload);
            ulong hash = Fnv1a64(body);

            var header = new byte[FixedHeaderBytes];
            int offset = 0;
            WriteUInt64(header, ref offset, Magic);
            WriteInt32(header, ref offset, key.CellCount);
            WriteInt32(header, ref offset, key.SampleDays.Length);
            WriteUInt64(header, ref offset, (ulong)key.IcePercentileBits);
            WriteUInt64(header, ref offset, key.SolverFingerprint);
            WriteUInt64(header, ref offset, hash);

            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
        }

        /// <summary>
        /// Betölti az éghajlatot, ha MINDEN ellenőrzés átmegy; különben
        /// <c>null</c> és a <paramref name="rejectionReason"/> mondja meg, miért.
        /// Részleges betöltés nincs.
        /// </summary>
        public static Payload? TryRead(Stream stream, in Key expectedKey, out string? rejectionReason)
        {
            rejectionReason = null;
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (expectedKey.CellCount <= 0) { rejectionReason = "ervenytelen cellaszam"; return null; }

            var header = new byte[FixedHeaderBytes];
            if (!ReadExactly(stream, header, header.Length)) { rejectionReason = "csonka fejlec"; return null; }

            int offset = 0;
            if (ReadUInt64(header, ref offset) != Magic) { rejectionReason = "ismeretlen formatum"; return null; }
            int cellCount = ReadInt32(header, ref offset);
            int sampleDayCount = ReadInt32(header, ref offset);
            long percentileBits = (long)ReadUInt64(header, ref offset);
            ulong fingerprint = ReadUInt64(header, ref offset);
            ulong storedHash = ReadUInt64(header, ref offset);

            if (cellCount != expectedKey.CellCount) { rejectionReason = "cellaszam-elteres"; return null; }
            if (sampleDayCount != expectedKey.SampleDays.Length) { rejectionReason = "mintanap-szam elterese"; return null; }
            if (percentileBits != expectedKey.IcePercentileBits) { rejectionReason = "jegkuszob-elteres"; return null; }
            if (fingerprint != expectedKey.SolverFingerprint) { rejectionReason = "algoritmus-elteres (solver-lenyomat)"; return null; }

            // A tartalom hossza csak a fejléc olvasása UTÁN ismert (a
            // modellazonosító változó hosszú), ezért a maradékot egyben
            // olvassuk be, és a méretet a kibontás ellenőrzi.
            byte[]? body = ReadToEnd(stream);
            if (body == null) { rejectionReason = "olvasasi hiba"; return null; }
            if (Fnv1a64(body) != storedHash) { rejectionReason = "ellenorzoosszeg-elteres"; return null; }

            if (!TryDeserialize(body, cellCount, sampleDayCount, out string identity, out long[] sampleDays,
                    out Payload payload))
            {
                rejectionReason = "csonka vagy ervenytelen tartalom";
                return null;
            }

            if (!string.Equals(identity, expectedKey.ModelIdentity, StringComparison.Ordinal))
            {
                rejectionReason = "modellazonosito-elteres";
                return null;
            }
            for (int i = 0; i < sampleDays.Length; i++)
                if (sampleDays[i] != expectedKey.SampleDays[i])
                {
                    rejectionReason = "mintanap-elteres";
                    return null;
                }

            return payload;
        }

        private static void ValidatePayloadShape(int cellCount, Payload payload)
        {
            if (payload.IceFreeClass.Length != cellCount || payload.RefinedClass.Length != cellCount
                || payload.RefinedKinds.Length != cellCount)
                throw new ArgumentException("A per-cella tombok merete nem egyezik a cellaszammal.", nameof(payload));
            foreach (double[] array in payload.IceFree.All)
                if (array.Length != cellCount)
                    throw new ArgumentException("A jegmentes menet tombjei nem a cellaszam meretuek.", nameof(payload));
            foreach (double[] array in payload.Refined.All)
                if (array.Length != cellCount)
                    throw new ArgumentException("A finomitott menet tombjei nem a cellaszam meretuek.", nameof(payload));
        }

        private static byte[] Serialize(in Key key, Payload payload)
        {
            byte[] identity = System.Text.Encoding.UTF8.GetBytes(key.ModelIdentity);
            int count = key.CellCount;
            long size = 4 + identity.Length + (long)key.SampleDays.Length * 8
                + (long)count * PerCellBytes + TrailerBytes;
            if (size > int.MaxValue) throw new ArgumentException("Tul nagy tartalom.", nameof(payload));

            var body = new byte[(int)size];
            int offset = 0;
            WriteInt32(body, ref offset, identity.Length);
            Buffer.BlockCopy(identity, 0, body, offset, identity.Length);
            offset += identity.Length;
            foreach (long day in key.SampleDays) WriteUInt64(body, ref offset, (ulong)day);

            foreach (double[] array in payload.IceFree.All) WriteDoubles(body, ref offset, array);
            foreach (double[] array in payload.Refined.All) WriteDoubles(body, ref offset, array);
            for (int c = 0; c < count; c++) body[offset++] = (byte)payload.IceFreeClass[c];
            for (int c = 0; c < count; c++) body[offset++] = (byte)payload.RefinedClass[c];
            for (int c = 0; c < count; c++) body[offset++] = (byte)payload.RefinedKinds[c];

            WriteUInt64(body, ref offset, (ulong)BitConverter.DoubleToInt64Bits(payload.IceFreeThresholdK));
            WriteUInt64(body, ref offset, (ulong)BitConverter.DoubleToInt64Bits(payload.RefinedThresholdK));
            WriteUInt64(body, ref offset, (ulong)BitConverter.DoubleToInt64Bits(payload.SeasonalSnowThresholdK));
            WriteInt32(body, ref offset, payload.ReclassifiedCells);
            body[offset++] = payload.SecondPassSkipped ? (byte)1 : (byte)0;
            return body;
        }

        private static bool TryDeserialize(byte[] body, int cellCount, int sampleDayCount,
            out string identity, out long[] sampleDays, out Payload payload)
        {
            identity = string.Empty;
            sampleDays = Array.Empty<long>();
            payload = new Payload();

            int offset = 0;
            if (body.Length < 4) return false;
            int identityLength = ReadInt32(body, ref offset);
            if (identityLength < 0 || identityLength > 1024) return false;

            long expected = 4L + identityLength + (long)sampleDayCount * 8
                + (long)cellCount * PerCellBytes + TrailerBytes;
            if (body.Length != expected) return false;

            identity = System.Text.Encoding.UTF8.GetString(body, offset, identityLength);
            offset += identityLength;
            sampleDays = new long[sampleDayCount];
            for (int i = 0; i < sampleDayCount; i++) sampleDays[i] = (long)ReadUInt64(body, ref offset);

            var iceFree = new AnnualArrays();
            var refined = new AnnualArrays();
            foreach (double[] array in AllocateAll(iceFree, cellCount)) ReadDoubles(body, ref offset, array);
            foreach (double[] array in AllocateAll(refined, cellCount)) ReadDoubles(body, ref offset, array);

            var iceFreeClass = new LakesIceErosion.IceClass[cellCount];
            var refinedClass = new LakesIceErosion.IceClass[cellCount];
            var refinedKinds = new SurfaceThermalKind[cellCount];
            for (int c = 0; c < cellCount; c++)
            {
                byte value = body[offset++];
                if (value > (byte)LakesIceErosion.IceClass.PermanentIce) return false;
                iceFreeClass[c] = (LakesIceErosion.IceClass)value;
            }
            for (int c = 0; c < cellCount; c++)
            {
                byte value = body[offset++];
                if (value > (byte)LakesIceErosion.IceClass.PermanentIce) return false;
                refinedClass[c] = (LakesIceErosion.IceClass)value;
            }
            for (int c = 0; c < cellCount; c++)
            {
                byte value = body[offset++];
                if (value > (byte)SurfaceThermalKind.Ice) return false;
                refinedKinds[c] = (SurfaceThermalKind)value;
            }

            payload = new Payload
            {
                IceFree = iceFree,
                Refined = refined,
                IceFreeClass = iceFreeClass,
                RefinedClass = refinedClass,
                RefinedKinds = refinedKinds,
                IceFreeThresholdK = BitConverter.Int64BitsToDouble((long)ReadUInt64(body, ref offset)),
                RefinedThresholdK = BitConverter.Int64BitsToDouble((long)ReadUInt64(body, ref offset)),
                SeasonalSnowThresholdK = BitConverter.Int64BitsToDouble((long)ReadUInt64(body, ref offset)),
                ReclassifiedCells = ReadInt32(body, ref offset),
                SecondPassSkipped = body[offset] != 0,
            };
            return true;
        }

        private static double[][] AllocateAll(AnnualArrays target, int cellCount)
        {
            target.MeanSurfaceK = new double[cellCount];
            target.MeanAirK = new double[cellCount];
            target.MinSurfaceK = new double[cellCount];
            target.MaxSurfaceK = new double[cellCount];
            target.MinAirK = new double[cellCount];
            target.MaxAirK = new double[cellCount];
            target.MeanWindX = new double[cellCount];
            target.MeanWindY = new double[cellCount];
            target.MeanWindZ = new double[cellCount];
            target.MeanWindSpeedMs = new double[cellCount];
            return target.All;
        }

        private static ulong MixDouble(ulong hash, double value)
        {
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
            for (int b = 0; b < 8; b++)
            {
                hash ^= (byte)(bits >> (b * 8));
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static ulong Fnv1a64(byte[] data)
        {
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static void WriteDoubles(byte[] buffer, ref int offset, double[] values)
        {
            for (int i = 0; i < values.Length; i++)
                WriteUInt64(buffer, ref offset, (ulong)BitConverter.DoubleToInt64Bits(values[i]));
        }

        private static void ReadDoubles(byte[] buffer, ref int offset, double[] values)
        {
            for (int i = 0; i < values.Length; i++)
                values[i] = BitConverter.Int64BitsToDouble((long)ReadUInt64(buffer, ref offset));
        }

        private static void WriteUInt64(byte[] buffer, ref int offset, ulong value)
        {
            for (int b = 7; b >= 0; b--) buffer[offset++] = (byte)(value >> (b * 8));
        }

        private static ulong ReadUInt64(byte[] buffer, ref int offset)
        {
            ulong value = 0;
            for (int b = 0; b < 8; b++) value = (value << 8) | buffer[offset++];
            return value;
        }

        private static void WriteInt32(byte[] buffer, ref int offset, int value)
        {
            for (int b = 3; b >= 0; b--) buffer[offset++] = (byte)(value >> (b * 8));
        }

        private static int ReadInt32(byte[] buffer, ref int offset)
        {
            int value = 0;
            for (int b = 0; b < 4; b++) value = (value << 8) | buffer[offset++];
            return value;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int chunk = stream.Read(buffer, read, count - read);
                if (chunk <= 0) return false;
                read += chunk;
            }
            return true;
        }

        private static byte[]? ReadToEnd(Stream stream)
        {
            using var memory = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                int chunk = stream.Read(buffer, 0, buffer.Length);
                if (chunk <= 0) break;
                memory.Write(buffer, 0, chunk);
            }
            return memory.ToArray();
        }
    }
}
