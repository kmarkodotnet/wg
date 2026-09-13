#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WorldGen.App.Serialization;

namespace WorldGen.App.Saves
{
    public enum SaveCorruptionReason
    {
        NotASaveFile,
        UnsupportedContainerVersion,
        Truncated,
        HeaderChecksumMismatch,
        HeaderInvalid,
        SectionTableInvalid,
        SectionChecksumMismatch,
        TrailingData,
    }

    public sealed class SaveCorruptedException : Exception
    {
        public SaveCorruptionReason Reason { get; }

        public SaveCorruptedException(SaveCorruptionReason reason, string message)
            : base(message)
        {
            Reason = reason;
        }
    }

    public sealed class SaveSection
    {
        public SaveSection(string name, byte[] data)
        {
            if (!SaveContainer.IsValidSectionName(name)) throw new ArgumentException("Érvénytelen szekciónév: '" + name + "'", nameof(name));
            Name = name;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public string Name { get; }
        public byte[] Data { get; }
    }

    public sealed class SaveFile
    {
        internal SaveFile(SaveHeader header, IReadOnlyList<SaveSection> sections)
        {
            Header = header;
            Sections = sections;
        }

        public SaveHeader Header { get; }
        public IReadOnlyList<SaveSection> Sections { get; }

        public SaveSection? FindSection(string name)
        {
            foreach (var s in Sections)
                if (string.Equals(s.Name, name, StringComparison.Ordinal)) return s;
            return null;
        }
    }

    /// <summary>
    /// A mentési konténer (ND-107), little-endian:
    /// "WGSV" | u16 konténerverzió | u16 fenntartott | u32 fejléchossz | fejléc (UTF-8 JSON) |
    /// u32 fejléc-CRC32 | u32 szekciószám | szekciótábla { u16 névhossz, név, u64 hossz, u32 CRC32 } | adatok.
    /// Minden olvasási hiba <see cref="SaveCorruptedException"/>, soha nem más kivétel
    /// (kivéve a stream saját I/O-hibáit).
    /// </summary>
    public static class SaveContainer
    {
        public const ushort ContainerVersion = 1;
        public const int MaxHeaderBytes = 16 * 1024 * 1024;
        public const int MaxSections = 1024;
        public const int MaxSectionNameBytes = 128;

        private static readonly byte[] Magic = { (byte)'W', (byte)'G', (byte)'S', (byte)'V' };
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Szekciónév: 1–128 karakter, [A-Za-z0-9._-].</summary>
        public static bool IsValidSectionName(string? name)
        {
            if (string.IsNullOrEmpty(name) || name!.Length > MaxSectionNameBytes) return false;
            foreach (char ch in name)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-';
                if (!ok) return false;
            }
            return true;
        }

        public static byte[] ToBytes(SaveHeader header, IReadOnlyList<SaveSection> sections)
        {
            using var ms = new MemoryStream();
            Write(ms, header, sections);
            return ms.ToArray();
        }

        public static void Write(Stream stream, SaveHeader header, IReadOnlyList<SaveSection> sections)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (sections == null) throw new ArgumentNullException(nameof(sections));
            if (sections.Count > MaxSections) throw new ArgumentException("Túl sok szekció.", nameof(sections));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in sections)
            {
                if (s == null) throw new ArgumentException("Null szekció.", nameof(sections));
                if (!names.Add(s.Name)) throw new ArgumentException("Duplikált szekciónév: " + s.Name, nameof(sections));
            }

            byte[] headerBytes = Utf8NoBom.GetBytes(JsonWriter.Write(SaveHeaderCodec.ToJson(header), indented: false));
            if (headerBytes.Length > MaxHeaderBytes) throw new ArgumentException("A fejléc túl nagy.", nameof(header));

            stream.Write(Magic, 0, Magic.Length);
            WriteUInt16(stream, ContainerVersion);
            WriteUInt16(stream, 0);
            WriteUInt32(stream, (uint)headerBytes.Length);
            stream.Write(headerBytes, 0, headerBytes.Length);
            WriteUInt32(stream, Crc32.Compute(headerBytes));

            WriteUInt32(stream, (uint)sections.Count);
            foreach (var s in sections)
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(s.Name);
                WriteUInt16(stream, (ushort)nameBytes.Length);
                stream.Write(nameBytes, 0, nameBytes.Length);
                WriteUInt64(stream, (ulong)s.Data.LongLength);
                WriteUInt32(stream, Crc32.Compute(s.Data));
            }
            foreach (var s in sections) stream.Write(s.Data, 0, s.Data.Length);
        }

        /// <summary>Csak a fejléc (a betöltési listához); a szekciókat nem olvassa.</summary>
        public static SaveHeader ReadHeader(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return ReadHeaderCore(stream);
        }

        public static SaveFile Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var header = ReadHeaderCore(stream);

            uint count = ReadUInt32(stream);
            if (count > MaxSections) throw Corrupt(SaveCorruptionReason.SectionTableInvalid, "Túl sok szekció: " + count);

            var entries = new List<Entry>((int)count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            ulong total = 0;
            for (uint i = 0; i < count; i++)
            {
                ushort nameLength = ReadUInt16(stream);
                if (nameLength == 0 || nameLength > MaxSectionNameBytes)
                    throw Corrupt(SaveCorruptionReason.SectionTableInvalid, "Érvénytelen szekciónév-hossz.");
                string name = Encoding.ASCII.GetString(ReadExact(stream, nameLength));
                if (!IsValidSectionName(name) || !names.Add(name))
                    throw Corrupt(SaveCorruptionReason.SectionTableInvalid, "Érvénytelen vagy duplikált szekciónév: " + name);
                ulong length = ReadUInt64(stream);
                if (length > int.MaxValue) throw Corrupt(SaveCorruptionReason.SectionTableInvalid, "Túl nagy szekció: " + name);
                uint crc = ReadUInt32(stream);
                entries.Add(new Entry(name, (int)length, crc));
                total += length;
            }

            if (stream.CanSeek && total > (ulong)(stream.Length - stream.Position))
                throw Corrupt(SaveCorruptionReason.Truncated, "A szekcióadatok hiányosak.");

            var sections = new List<SaveSection>(entries.Count);
            foreach (var e in entries)
            {
                byte[] data = ReadExact(stream, e.Length);
                if (Crc32.Compute(data) != e.Crc)
                    throw Corrupt(SaveCorruptionReason.SectionChecksumMismatch, "Sérült szekció: " + e.Name);
                sections.Add(new SaveSection(e.Name, data));
            }

            bool trailing = stream.CanSeek ? stream.Position != stream.Length : stream.ReadByte() >= 0;
            if (trailing) throw Corrupt(SaveCorruptionReason.TrailingData, "Váratlan adat a mentés végén.");

            return new SaveFile(header, sections);
        }

        private static SaveHeader ReadHeaderCore(Stream stream)
        {
            var prefix = new byte[8];
            int got = ReadUpTo(stream, prefix, prefix.Length);
            for (int i = 0; i < Magic.Length; i++)
            {
                if (i >= got || prefix[i] != Magic[i]) throw Corrupt(SaveCorruptionReason.NotASaveFile, "Nem WorldGen-mentés.");
            }
            if (got < prefix.Length) throw Corrupt(SaveCorruptionReason.Truncated, "Csonka fejléc.");
            ushort version = (ushort)(prefix[4] | prefix[5] << 8);
            if (version != ContainerVersion)
                throw Corrupt(SaveCorruptionReason.UnsupportedContainerVersion, "Nem támogatott konténerverzió: " + version);

            uint headerLength = ReadUInt32(stream);
            if (headerLength == 0 || headerLength > MaxHeaderBytes) throw Corrupt(SaveCorruptionReason.HeaderInvalid, "Érvénytelen fejléchossz.");
            byte[] headerBytes = ReadExact(stream, (int)headerLength);
            uint crc = ReadUInt32(stream);
            if (Crc32.Compute(headerBytes) != crc) throw Corrupt(SaveCorruptionReason.HeaderChecksumMismatch, "Sérült fejléc.");

            try
            {
                return SaveHeaderCodec.FromJson(JsonParser.Parse(StrictUtf8.GetString(headerBytes)));
            }
            catch (DecoderFallbackException ex)
            {
                throw Corrupt(SaveCorruptionReason.HeaderInvalid, "A fejléc nem UTF-8: " + ex.Message);
            }
            catch (JsonFormatException ex)
            {
                throw Corrupt(SaveCorruptionReason.HeaderInvalid, "A fejléc nem JSON: " + ex.Message);
            }
        }

        private static void WriteUInt16(Stream s, ushort v)
        {
            s.WriteByte((byte)v);
            s.WriteByte((byte)(v >> 8));
        }

        private static void WriteUInt32(Stream s, uint v)
        {
            for (int i = 0; i < 4; i++) s.WriteByte((byte)(v >> (8 * i)));
        }

        private static void WriteUInt64(Stream s, ulong v)
        {
            for (int i = 0; i < 8; i++) s.WriteByte((byte)(v >> (8 * i)));
        }

        private static ushort ReadUInt16(Stream s)
        {
            byte[] b = ReadExact(s, 2);
            return (ushort)(b[0] | b[1] << 8);
        }

        private static uint ReadUInt32(Stream s)
        {
            byte[] b = ReadExact(s, 4);
            return (uint)(b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24);
        }

        private static ulong ReadUInt64(Stream s)
        {
            byte[] b = ReadExact(s, 8);
            ulong v = 0;
            for (int i = 7; i >= 0; i--) v = v << 8 | b[i];
            return v;
        }

        private static byte[] ReadExact(Stream s, int count)
        {
            var buffer = new byte[count];
            if (ReadUpTo(s, buffer, count) != count) throw Corrupt(SaveCorruptionReason.Truncated, "Váratlan fájlvég.");
            return buffer;
        }

        private static int ReadUpTo(Stream s, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = s.Read(buffer, offset, count - offset);
                if (read <= 0) break;
                offset += read;
            }
            return offset;
        }

        private static SaveCorruptedException Corrupt(SaveCorruptionReason reason, string message) => new SaveCorruptedException(reason, message);

        private readonly struct Entry
        {
            public Entry(string name, int length, uint crc)
            {
                Name = name;
                Length = length;
                Crc = crc;
            }

            public string Name { get; }
            public int Length { get; }
            public uint Crc { get; }
        }
    }
}
