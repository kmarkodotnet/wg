using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WorldGen.Core.Persistence;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// ND-143: teljes double hőállapot, explicit big-endian formátumban.
    /// Világ-/verzióeltérés, sérülés vagy nem kanonikus állapot nem tölthető be.
    /// Ez Core-codec; a teljes app-mentés simulation-state bekötése külön feladat.
    /// </summary>
    public static class ThermalCheckpoint
    {
        private const ulong Magic = 0x5747544830303031UL; // WGTH0001
        private const int DigestSize = 32;

        public static byte[] Capture(SurfaceTemperatureField field, ThermalSnapshot state)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.ModelIdentity != field.ModelIdentity || state.ThetaS.Length != field.Grid.CellCount
                || !state.CanonicalStartTick.HasValue)
                throw new ArgumentException("Csak az adott világ kanonikus hőállapota menthető.", nameof(state));
            ValidateTicks(state.CanonicalStartTick.Value, state.Tick);
            using var stream = new MemoryStream();
            WriteUInt64(stream, Magic);
            WriteString(stream, WorldGeneratorVersion.Current);
            WriteUInt64(stream, ThermalModelParameters.ModelVersion);
            WriteString(stream, field.ModelIdentity);
            WriteUInt64(stream, (ulong)field.Grid.CellCount);
            WriteUInt64(stream, unchecked((ulong)state.CanonicalStartTick.Value));
            WriteUInt64(stream, unchecked((ulong)state.Tick));
            for (int c = 0; c < field.Grid.CellCount; c++)
            {
                WriteFiniteDouble(stream, state.ThetaS[c]);
                WriteFiniteDouble(stream, state.ThetaA[c]);
            }
            byte[] payload = stream.ToArray();
            byte[] digest = Digest(payload, payload.Length);
            stream.Write(digest, 0, digest.Length);
            return stream.ToArray();
        }

        /// <summary>Visszaállítás csak az összes ellenőrzés után; hibánál a célállapot változatlan.</summary>
        public static void Restore(SurfaceTemperatureField field, ThermalSnapshot target, byte[] checkpoint)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            if (target.ThetaS.Length != field.Grid.CellCount) throw new ArgumentException("Eltérő célrács.");
            long payloadLength = (long)field.Grid.CellCount * 16;
            // Két korlátos string és a fix fejléc; idegen méretből nem foglalunk memóriát.
            if (checkpoint.Length < payloadLength + 8 * 8 + DigestSize
                || checkpoint.Length > payloadLength + 8 * 8 + 256 + DigestSize)
                throw new InvalidDataException("Hibás hő-checkpoint méret.");
            int end = checkpoint.Length - DigestSize;
            byte[] actual = Digest(checkpoint, end);
            for (int i = 0; i < DigestSize; i++)
                if (actual[i] != checkpoint[end + i]) throw new InvalidDataException("Sérült hő-checkpoint (SHA-256).");
            int offset = 0;
            if (ReadUInt64(checkpoint, ref offset, end) != Magic)
                throw new InvalidDataException("Ismeretlen hő-checkpoint formátum.");
            string generator = ReadString(checkpoint, ref offset, end);
            if (generator != WorldGeneratorVersion.Current)
                throw new InvalidDataException("Eltérő generátorverzió a hő-checkpointban: " + generator);
            if (ReadUInt64(checkpoint, ref offset, end) != ThermalModelParameters.ModelVersion)
                throw new InvalidDataException("Eltérő hőmodellverzió.");
            string identity = ReadString(checkpoint, ref offset, end);
            if (identity != field.ModelIdentity)
                throw new InvalidDataException("A hő-checkpoint másik világhoz vagy paraméterezéshez tartozik.");
            if (ReadUInt64(checkpoint, ref offset, end) != (ulong)field.Grid.CellCount)
                throw new InvalidDataException("Eltérő cellaszám a hő-checkpointban.");
            long start = unchecked((long)ReadUInt64(checkpoint, ref offset, end));
            long tick = unchecked((long)ReadUInt64(checkpoint, ref offset, end));
            ValidateTicks(start, tick);
            if (end - offset != payloadLength) throw new InvalidDataException("Hibás hőállapot-hossz.");
            var decoded = new ThermalSnapshot(field.Grid.CellCount)
            { Tick = tick, CanonicalStartTick = start, ModelIdentity = identity };
            for (int c = 0; c < field.Grid.CellCount; c++)
            {
                decoded.ThetaS[c] = ReadDouble(checkpoint, ref offset, end);
                decoded.ThetaA[c] = ReadDouble(checkpoint, ref offset, end);
            }
            target.CopyFrom(decoded);
        }

        /// <summary>A teljes verziózott termikus állapot hash-e (az elevációhash-től külön).</summary>
        public static string StateHash(SurfaceTemperatureField field, ThermalSnapshot state)
        {
            byte[] checkpoint = Capture(field, state);
            var digest = new byte[DigestSize];
            Array.Copy(checkpoint, checkpoint.Length - DigestSize, digest, 0, DigestSize);
            return WorldStateHash.ToHexString(digest);
        }

        internal static string ComputeModelIdentity(SurfaceTemperatureField field)
        {
            using var stream = new MemoryStream();
            WriteString(stream, "WorldGen.Thermal.Inputs.1");
            WriteString(stream, WorldGeneratorVersion.Current);
            WriteUInt64(stream, ThermalModelParameters.ModelVersion);
            WriteUInt64(stream, (ulong)field.Grid.Level);
            WriteFiniteDouble(stream, field.Grid.RadiusMeters);
            WriteUInt64(stream, field.WorldSeed);
            WriteFiniteDouble(stream, field.TYears);
            WriteFiniteDouble(stream, field.SeaLevelM);
            WriteFiniteDouble(stream, field.Orbit.OrbitalPeriodDays);
            WriteFiniteDouble(stream, field.Orbit.RotationPeriodDays);
            WriteFiniteDouble(stream, field.Orbit.AxialTiltRad);
            var p = field.Parameters;
            WriteFiniteDouble(stream, p.SolarConstant);
            WriteFiniteDouble(stream, p.AirHeatCapacity);
            WriteFiniteDouble(stream, p.AirRelaxation);
            WriteFiniteDouble(stream, p.ExchangePerMetrePerSecond);
            WriteFiniteDouble(stream, p.MinExchangeWindMs);
            WriteFiniteDouble(stream, p.RadiativeSmoothing);
            WriteFiniteDouble(stream, p.AirFeedbackStrength);
            // ND-160: a BÁZIS albedója is az azonosító része. Két érték elég,
            // mert mindhárom módot szétválasztja (bolygó: 0,30/0,30; legacy:
            // 0,06/0,30; explicit a: a/a) — nélküle egy A/B-mérés némán
            // TALÁLATOT kapna a másik mód gyorsítótárára.
            WriteFiniteDouble(stream, p.BaselineAlbedoFor(SurfaceThermalKind.Ocean));
            WriteFiniteDouble(stream, p.BaselineAlbedoFor(SurfaceThermalKind.Land));
            for (int k = 0; k < 4; k++)
            {
                var kind = (SurfaceThermalKind)k;
                WriteFiniteDouble(stream, p.Albedo(kind));
                WriteFiniteDouble(stream, p.Emissivity(kind));
                WriteFiniteDouble(stream, p.SurfaceHeatCapacity(kind));
            }
            // ND-166: az A/B-paraméter nem kaphatja a régi cache-azonosítót.
            // Az alapértéket nem írjuk hozzá, így a korábbi alapmodell
            // checkpointja és lemez-cache-e bitre azonos marad.
            if (p.MeridionalTransportScale != 1.0)
            {
                WriteString(stream, "ND-166-meridional-scale");
                WriteFiniteDouble(stream, p.MeridionalTransportScale);
            }
            for (int c = 0; c < field.Grid.CellCount; c++)
            {
                WriteUInt64(stream, (ulong)field.KindAt(c));
                WriteFiniteDouble(stream, field.ElevationAt(c));
            }
            byte[] payload = stream.ToArray();
            return WorldStateHash.ToHexString(Digest(payload, payload.Length));
        }

        private static void ValidateTicks(long start, long tick)
        {
            try
            {
                long bucket = checked(start + SimulationTime.SpinUpTicks);
                if (bucket % SimulationTime.BucketTicks != 0 || tick < start
                    || tick >= checked(bucket + SimulationTime.BucketTicks))
                    throw new InvalidDataException("Nem kanonikus hő-checkpoint időtartomány.");
                checked { _ = tick * SimulationTime.TickSeconds; }
            }
            catch (OverflowException e) { throw new InvalidDataException("Túlcsorduló hő-checkpoint idő.", e); }
        }

        private static byte[] Digest(byte[] bytes, int count)
        {
            using SHA256 sha = SHA256.Create();
            return sha.ComputeHash(bytes, 0, count);
        }

        private static void WriteUInt64(Stream stream, ulong value)
        {
            for (int i = 7; i >= 0; i--) stream.WriteByte((byte)(value >> (i * 8)));
        }

        private static ulong ReadUInt64(byte[] bytes, ref int offset, int end)
        {
            if (end - offset < 8) throw new InvalidDataException("Csonka hő-checkpoint.");
            ulong value = 0;
            for (int i = 0; i < 8; i++) value = (value << 8) | bytes[offset++];
            return value;
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteUInt64(stream, (ulong)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadString(byte[] bytes, ref int offset, int end)
        {
            ulong length = ReadUInt64(bytes, ref offset, end);
            if (length > 128 || length > (ulong)(end - offset)) throw new InvalidDataException("Hibás fejlécstring.");
            string text = Encoding.UTF8.GetString(bytes, offset, (int)length);
            offset += (int)length;
            return text;
        }

        private static void WriteFiniteDouble(Stream stream, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Nem véges hőállapot.");
            WriteUInt64(stream, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
        }

        private static double ReadDouble(byte[] bytes, ref int offset, int end)
        {
            double value = BitConverter.Int64BitsToDouble(unchecked((long)ReadUInt64(bytes, ref offset, end)));
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Nem véges hőállapot.");
            return value;
        }
    }
}
