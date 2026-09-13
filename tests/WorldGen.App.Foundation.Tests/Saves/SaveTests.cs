using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WorldGen.App.Localization;
using WorldGen.App.Saves;
using WorldGen.App.Serialization;
using WorldGen.App.Settings;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Saves
{
    internal static class SaveSamples
    {
        public static readonly DateTime Created = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);

        public static SaveHeader Header(string name = "Gaia-8214", DateTime? lastPlayed = null, SaveKind kind = SaveKind.Manual,
            string worldId = "0123456789abcdef0123456789abcdef", string generator = "gen-7", int formatVersion = 1)
        {
            return new SaveHeader
            {
                SaveFormatVersion = formatVersion,
                ApplicationVersion = "0.1.0-alpha",
                WorldGeneratorVersion = generator,
                WorldId = worldId,
                WorldName = name,
                Seed = 18446744073709551615UL,
                ParameterSchemaId = "test.planet-v1",
                PresetId = "earth-like",
                Parameters = JsonValue.CreateObject().Set("ocean.fraction", 0.65).Set("plates.count", 12),
                SimulationQuality = SimulationQuality.Accurate,
                SimulationAgeYears = 2.73e9,
                CreatedUtc = Created,
                LastPlayedUtc = lastPlayed ?? Created.AddHours(2),
                PlayTimeSeconds = 5400.5,
                Kind = kind,
                Thumbnail = new ThumbnailInfo("thumbnail", 256, 144, "png"),
                Extensions = JsonValue.CreateObject().Set("camera", JsonValue.CreateObject().Set("altitudeKm", 12000.0)),
            };
        }

        public static SaveSection[] Sections() => new[]
        {
            new SaveSection("simulation-state", Enumerable.Range(0, 5000).Select(i => (byte)(i * 31)).ToArray()),
            new SaveSection("thumbnail", new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            new SaveSection("empty", Array.Empty<byte>()),
        };
    }

    /// <summary>Nem kereshető stream (pl. hálózati vagy tömörített forrás szimulálására).</summary>
    internal sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }

        // Szándékosan legfeljebb 7 bájtot ad egyszerre: a részleges olvasást is teszteli.
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 7));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public class SaveHeaderCodecTests
    {
        [Fact]
        public void AllFieldsRoundTrip()
        {
            var h = SaveSamples.Header();
            var back = SaveHeaderCodec.FromJson(JsonParser.Parse(JsonWriter.Write(SaveHeaderCodec.ToJson(h))));

            Assert.Equal(JsonWriter.Write(SaveHeaderCodec.ToJson(h)), JsonWriter.Write(SaveHeaderCodec.ToJson(back)));
            Assert.Equal(ulong.MaxValue, back.Seed);
            Assert.Equal(DateTimeKind.Utc, back.CreatedUtc.Kind);
            Assert.Equal(h.LastPlayedUtc, back.LastPlayedUtc);
            Assert.Equal(256, back.Thumbnail!.Width);
            Assert.Equal(SimulationQuality.Accurate, back.SimulationQuality);
            Assert.Equal("2026-09-13T10:00:00.000Z", SaveHeaderCodec.ToJson(h).GetMember("createdUtc")!.AsString());
        }

        [Theory]
        [InlineData("saveFormatVersion")]
        [InlineData("worldName")]
        [InlineData("seed")]
        [InlineData("createdUtc")]
        [InlineData("lastPlayedUtc")]
        public void RequiredFieldsAreEnforced(string field)
        {
            var json = SaveHeaderCodec.ToJson(SaveSamples.Header());
            json.Remove(field);
            var ex = Assert.Throws<SaveCorruptedException>(() => SaveHeaderCodec.FromJson(json));
            Assert.Equal(SaveCorruptionReason.HeaderInvalid, ex.Reason);
        }

        [Fact]
        public void OptionalFieldsAreTolerant()
        {
            var json = SaveHeaderCodec.ToJson(SaveSamples.Header());
            json.Set("kind", "Teleport").Set("simulationQuality", 3).Set("thumbnail", "bad").Set("parameters", 5).Set("futureField", true);
            var h = SaveHeaderCodec.FromJson(json);
            Assert.Equal(SaveKind.Manual, h.Kind);
            Assert.Equal(SimulationQuality.Balanced, h.SimulationQuality);
            Assert.Null(h.Thumbnail);
            Assert.Equal(0, h.Parameters.Count);
        }

        [Fact]
        public void LocalTimesAreStoredAsUtc()
        {
            var local = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Local);
            Assert.Equal(SaveHeaderCodec.FormatDate(local.ToUniversalTime()), SaveHeaderCodec.FormatDate(local));
            Assert.Equal(32, SaveHeader.NewWorldId().Length);
        }
    }

    public class SaveContainerTests
    {
        [Fact]
        public void RoundTripsHeaderAndSections()
        {
            byte[] bytes = SaveContainer.ToBytes(SaveSamples.Header(), SaveSamples.Sections());

            Assert.Equal(new byte[] { (byte)'W', (byte)'G', (byte)'S', (byte)'V', 1, 0, 0, 0 }, bytes.Take(8).ToArray());
            int headerLength = BitConverter.ToInt32(bytes, 8);
            Assert.StartsWith("{\"saveFormatVersion\":1,", Encoding.UTF8.GetString(bytes, 12, headerLength));

            var file = SaveContainer.Read(new MemoryStream(bytes));
            Assert.Equal("Gaia-8214", file.Header.WorldName);
            Assert.Equal(new[] { "simulation-state", "thumbnail", "empty" }, file.Sections.Select(s => s.Name));
            Assert.Equal(SaveSamples.Sections()[0].Data, file.FindSection("simulation-state")!.Data);
            Assert.Empty(file.FindSection("empty")!.Data);
            Assert.Null(file.FindSection("missing"));
        }

        [Fact]
        public void HeaderCanBeReadWithoutSectionData()
        {
            byte[] bytes = SaveContainer.ToBytes(SaveSamples.Header(), SaveSamples.Sections());
            int headerEnd = 12 + BitConverter.ToInt32(bytes, 8) + 4;
            var header = SaveContainer.ReadHeader(new MemoryStream(bytes.Take(headerEnd).ToArray()));
            Assert.Equal("Gaia-8214", header.WorldName);
        }

        [Fact]
        public void NonSeekablePartialReadsWork()
        {
            byte[] bytes = SaveContainer.ToBytes(SaveSamples.Header(), SaveSamples.Sections());
            Assert.Equal(3, SaveContainer.Read(new NonSeekableStream(bytes)).Sections.Count);
            var withTrailing = bytes.Concat(new byte[] { 0 }).ToArray();
            Assert.Equal(SaveCorruptionReason.TrailingData,
                Assert.Throws<SaveCorruptedException>(() => SaveContainer.Read(new NonSeekableStream(withTrailing))).Reason);
        }

        private static SaveCorruptionReason ReadFailure(byte[] bytes)
            => Assert.Throws<SaveCorruptedException>(() => SaveContainer.Read(new MemoryStream(bytes))).Reason;

        [Fact]
        public void DetectsEveryKindOfDamage()
        {
            byte[] good = SaveContainer.ToBytes(SaveSamples.Header(), SaveSamples.Sections());
            int headerLength = BitConverter.ToInt32(good, 8);

            byte[] headerFlip = (byte[])good.Clone();
            headerFlip[20] ^= 0x01;
            Assert.Equal(SaveCorruptionReason.HeaderChecksumMismatch, ReadFailure(headerFlip));

            byte[] dataFlip = (byte[])good.Clone();
            dataFlip[good.Length - 10] ^= 0xFF; // a thumbnail előtti simulation-state vége
            Assert.Equal(SaveCorruptionReason.SectionChecksumMismatch, ReadFailure(dataFlip));

            Assert.Equal(SaveCorruptionReason.Truncated, ReadFailure(good.Take(good.Length - 1).ToArray()));
            Assert.Equal(SaveCorruptionReason.Truncated, ReadFailure(good.Take(12 + headerLength / 2).ToArray()));
            Assert.Equal(SaveCorruptionReason.TrailingData, ReadFailure(good.Concat(new byte[] { 1, 2 }).ToArray()));

            byte[] magic = (byte[])good.Clone();
            magic[0] = (byte)'X';
            Assert.Equal(SaveCorruptionReason.NotASaveFile, ReadFailure(magic));
            Assert.Equal(SaveCorruptionReason.NotASaveFile, ReadFailure(Array.Empty<byte>()));
            Assert.Equal(SaveCorruptionReason.NotASaveFile, ReadFailure(Encoding.ASCII.GetBytes("{\"json\": true}")));
            Assert.Equal(SaveCorruptionReason.Truncated, ReadFailure(new byte[] { (byte)'W', (byte)'G', (byte)'S', (byte)'V', 1, 0 }));

            byte[] version = (byte[])good.Clone();
            version[4] = 2;
            Assert.Equal(SaveCorruptionReason.UnsupportedContainerVersion, ReadFailure(version));

            byte[] hugeHeader = (byte[])good.Clone();
            BitConverter.GetBytes(SaveContainer.MaxHeaderBytes + 1).CopyTo(hugeHeader, 8);
            Assert.Equal(SaveCorruptionReason.HeaderInvalid, ReadFailure(hugeHeader));
        }

        [Fact]
        public void ValidChecksumButInvalidHeaderContentIsHeaderInvalid()
        {
            foreach (byte[] payload in new[] { Encoding.UTF8.GetBytes("not json"), new byte[] { 0xFF, 0xFE }, Encoding.UTF8.GetBytes("{\"worldName\":\"x\"}") })
            {
                var ms = new MemoryStream();
                ms.Write(new byte[] { (byte)'W', (byte)'G', (byte)'S', (byte)'V', 1, 0, 0, 0 }, 0, 8);
                ms.Write(BitConverter.GetBytes(payload.Length), 0, 4);
                ms.Write(payload, 0, payload.Length);
                ms.Write(BitConverter.GetBytes(Crc32.Compute(payload)), 0, 4);
                ms.Write(BitConverter.GetBytes(0), 0, 4);
                Assert.Equal(SaveCorruptionReason.HeaderInvalid, ReadFailure(ms.ToArray()));
            }
        }

        [Fact]
        public void WriterRejectsInvalidSections()
        {
            Assert.Throws<ArgumentException>(() => new SaveSection("bad name", new byte[0]));
            Assert.Throws<ArgumentException>(() => new SaveSection("", new byte[0]));
            Assert.Throws<ArgumentException>(() => SaveContainer.ToBytes(SaveSamples.Header(),
                new[] { new SaveSection("a", new byte[1]), new SaveSection("a", new byte[2]) }));
        }
    }

    public class SaveCompatibilityTests
    {
        [Theory]
        [InlineData(2, "gen-7", 1, false, SaveCompatibilityLevel.Incompatible, IncompatibilityReason.NewerFormat)]
        [InlineData(1, "gen-7", 2, false, SaveCompatibilityLevel.Incompatible, IncompatibilityReason.NoMigrationPath)]
        [InlineData(1, "gen-7", 2, true, SaveCompatibilityLevel.Migratable, IncompatibilityReason.None)]
        [InlineData(1, "gen-6", 2, true, SaveCompatibilityLevel.ConfigurationOnly, IncompatibilityReason.GeneratorChanged)]
        [InlineData(1, "gen-6", 1, false, SaveCompatibilityLevel.ConfigurationOnly, IncompatibilityReason.GeneratorChanged)]
        [InlineData(1, "", 1, false, SaveCompatibilityLevel.ConfigurationOnly, IncompatibilityReason.GeneratorChanged)]
        [InlineData(1, "gen-7", 1, false, SaveCompatibilityLevel.Compatible, IncompatibilityReason.None)]
        public void EvaluatesCompatibility(int saveFormat, string saveGenerator, int currentFormat, bool migratable,
            SaveCompatibilityLevel level, IncompatibilityReason reason)
        {
            var header = SaveSamples.Header(generator: saveGenerator, formatVersion: saveFormat);
            var result = SaveCompatibility.Evaluate(header, currentFormat, "gen-7", (_, __) => migratable);
            Assert.Equal(level, result.Level);
            Assert.Equal(reason, result.Reason);
        }

        [Fact]
        public void MessagesAreLocalizedAndGeneratorIsRequired()
        {
            var table = EnglishStrings.CreateTable();
            foreach (IncompatibilityReason reason in Enum.GetValues(typeof(IncompatibilityReason)))
            {
                string? key = new SaveCompatibilityResult(SaveCompatibilityLevel.Incompatible, reason).MessageKey;
                if (key != null) Assert.True(table.TryGet(key, out _), key);
            }
            Assert.Throws<ArgumentException>(() => SaveCompatibility.Evaluate(SaveSamples.Header(), 1, ""));
        }
    }

    public class SaveRepositoryTests
    {
        private static readonly string SavesDir = Path.Combine(InMemoryFileSystem.Root, "Saves");

        private static SaveRepository NewRepository(InMemoryFileSystem fs)
            => new SaveRepository(fs, SavesDir, h => SaveCompatibility.Evaluate(h, SaveHeaderCodec.CurrentFormatVersion, "gen-7"));

        [Fact]
        public void ListsNewestFirstFlagsDamageAndPicksContinue()
        {
            var fs = new InMemoryFileSystem();
            var repo = NewRepository(fs);
            var t = SaveSamples.Created;

            repo.Write(repo.CreateFilePath("Old", SaveKind.Manual, t), SaveSamples.Header("Old", t.AddDays(1)), SaveSamples.Sections());
            repo.Write(repo.CreateFilePath("Newest but outdated", SaveKind.Manual, t),
                SaveSamples.Header("Newest but outdated", t.AddDays(5), generator: "gen-6"), SaveSamples.Sections());
            repo.Write(repo.CreateFilePath("Recent", SaveKind.Manual, t), SaveSamples.Header("Recent", t.AddDays(3)), SaveSamples.Sections());
            fs.NowUtc = t.AddDays(4);
            fs.WriteText(Path.Combine(SavesDir, "garbage.wgsave"), "this is not a save");
            fs.WriteText(Path.Combine(SavesDir, "notes.txt"), "ignored");

            var slots = repo.List();

            Assert.Equal(new[] { "Newest but outdated", "garbage", "Recent", "Old" }, slots.Select(s => s.DisplayName));
            Assert.Equal(SaveCorruptionReason.NotASaveFile, slots[1].Corruption);
            Assert.False(slots[1].Compatibility.CanLoadState);
            Assert.Equal(SaveCompatibilityLevel.ConfigurationOnly, slots[0].Compatibility.Level);
            Assert.Equal("Recent", SaveRepository.FindContinueCandidate(slots)!.DisplayName);
            Assert.Null(SaveRepository.FindContinueCandidate(new SaveSlotInfo[0]));
        }

        [Fact]
        public void EmptyOrMissingDirectoryListsNothing()
        {
            Assert.Empty(NewRepository(new InMemoryFileSystem()).List());
        }

        [Fact]
        public void FilePathsAreSanitizedUniqueAndTyped()
        {
            var fs = new InMemoryFileSystem();
            var repo = NewRepository(fs);
            var t = new DateTime(2026, 9, 13, 14, 5, 9, DateTimeKind.Utc);

            string first = repo.CreateFilePath("Gaia: 8214?", SaveKind.Manual, t);
            Assert.Equal(Path.Combine(SavesDir, "Gaia_ 8214_.wgsave"), first);
            repo.Write(first, SaveSamples.Header(), SaveSamples.Sections());
            Assert.Equal(Path.Combine(SavesDir, "Gaia_ 8214_ (2).wgsave"), repo.CreateFilePath("Gaia: 8214?", SaveKind.Manual, t));
            Assert.Equal(Path.Combine(SavesDir, "Gaia - Autosave 20260913-140509.wgsave"), repo.CreateFilePath("Gaia", SaveKind.Auto, t));
            Assert.Equal(Path.Combine(SavesDir, "Gaia - Quicksave.wgsave"), repo.CreateFilePath("Gaia", SaveKind.Quick, t));
        }

        [Fact]
        public void WritesStayInsideSavesDirectory()
        {
            var repo = NewRepository(new InMemoryFileSystem());
            Assert.Throws<ArgumentException>(() => repo.Write(Path.Combine(InMemoryFileSystem.Root, "x.wgsave"), SaveSamples.Header(), SaveSamples.Sections()));
            Assert.Throws<ArgumentException>(() => repo.Write(Path.Combine(SavesDir, "..", "x.wgsave"), SaveSamples.Header(), SaveSamples.Sections()));
            Assert.Throws<ArgumentException>(() => repo.Write(Path.Combine(SavesDir, "x.json"), SaveSamples.Header(), SaveSamples.Sections()));
        }

        [Fact]
        public void FailedOverwriteKeepsPreviousSave()
        {
            var fs = new InMemoryFileSystem();
            var repo = NewRepository(fs);
            string path = repo.CreateFilePath("Gaia", SaveKind.Manual, SaveSamples.Created);
            repo.Write(path, SaveSamples.Header("Gaia v1"), SaveSamples.Sections());

            fs.FailNextReplace = true;
            Assert.Throws<IOException>(() => repo.Write(path, SaveSamples.Header("Gaia v2"), SaveSamples.Sections()));

            Assert.Equal("Gaia v1", repo.Load(path).Header.WorldName);
            Assert.Single(repo.List());
        }

        [Fact]
        public void DeleteRemovesSaveAndBackup()
        {
            var fs = new InMemoryFileSystem();
            var repo = NewRepository(fs);
            string path = repo.CreateFilePath("Gaia", SaveKind.Manual, SaveSamples.Created);
            repo.Write(path, SaveSamples.Header(), SaveSamples.Sections());
            repo.Write(path, SaveSamples.Header(), SaveSamples.Sections());
            Assert.True(fs.FileExists(path + ".bak"));

            repo.Delete(path);
            Assert.Equal(0, fs.FileCount);
        }

        [Fact]
        public void AutosaveRotationKeepsNewestPerWorld()
        {
            var fs = new InMemoryFileSystem();
            var repo = NewRepository(fs);
            var t = SaveSamples.Created;
            const string worldA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            const string worldB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            for (int i = 0; i < 4; i++)
            {
                var when = t.AddMinutes(10 * i);
                repo.Write(repo.CreateFilePath("A", SaveKind.Auto, when), SaveSamples.Header("A", when, SaveKind.Auto, worldA), SaveSamples.Sections());
            }
            repo.Write(repo.CreateFilePath("A", SaveKind.Manual, t), SaveSamples.Header("A", t, SaveKind.Manual, worldA), SaveSamples.Sections());
            repo.Write(repo.CreateFilePath("B", SaveKind.Auto, t), SaveSamples.Header("B", t, SaveKind.Auto, worldB), SaveSamples.Sections());

            var toDelete = SaveRepository.SelectAutosavesToDelete(repo.List(), worldA, keep: 2);

            Assert.Equal(new[] { "A - Autosave 20260913-101000.wgsave", "A - Autosave 20260913-100000.wgsave" }, toDelete.Select(Path.GetFileName));
            Assert.Empty(SaveRepository.SelectAutosavesToDelete(repo.List(), worldB, keep: 2));
        }

        [Fact]
        public void PhysicalRoundTrip()
        {
            using var dir = new TempDirectory();
            var saves = Path.Combine(dir.Path, "Saves");
            var repo = new SaveRepository(new WorldGen.App.Storage.PhysicalFileSystem(), saves,
                h => SaveCompatibility.Evaluate(h, 1, "gen-7"));
            string path = repo.CreateFilePath("Terra", SaveKind.Manual, SaveSamples.Created);
            repo.Write(path, SaveSamples.Header("Terra"), SaveSamples.Sections());

            var slot = Assert.Single(repo.List());
            Assert.Equal(SaveCompatibilityLevel.Compatible, slot.Compatibility.Level);
            Assert.Equal(new FileInfo(path).Length, slot.SizeBytes);
            Assert.Equal(SaveSamples.Sections()[0].Data, repo.Load(path).Sections[0].Data);
        }
    }

    public class AutosaveSchedulerTests
    {
        [Fact]
        public void CountsOnlyRunningSimulationTimeAndStaysDueUntilSaved()
        {
            var scheduler = new AutosaveScheduler(10);
            Assert.False(scheduler.Tick(3600, isSimulationRunning: false));
            Assert.Equal(0, scheduler.ElapsedSeconds);

            Assert.False(scheduler.Tick(599, true));
            Assert.Equal(1, scheduler.RemainingSeconds, 9);
            Assert.True(scheduler.Tick(1, true));
            Assert.True(scheduler.IsDue);
            Assert.False(scheduler.Tick(100, true));

            scheduler.NotifySaved();
            Assert.False(scheduler.IsDue);
            Assert.Equal(600, scheduler.RemainingSeconds);
            Assert.False(scheduler.Tick(double.NaN, true));
        }

        [Fact]
        public void IntervalSnapsAndZeroDisables()
        {
            var scheduler = new AutosaveScheduler(15);
            Assert.Equal(10, scheduler.IntervalMinutes);
            scheduler.Tick(600, true);
            Assert.True(scheduler.IsDue);
            scheduler.IntervalMinutes = 0;
            Assert.False(scheduler.IsEnabled);
            Assert.False(scheduler.IsDue);
            Assert.False(scheduler.Tick(100000, true));
            Assert.True(double.IsPositiveInfinity(scheduler.RemainingSeconds));
            Assert.True(AutosaveScheduler.ShouldSaveBeforeDeepTimeJump(new SimulationSettings { AutosaveBeforeDeepTimeJump = true }));
        }
    }

    public class SaveErrorClassifierTests
    {
        [Fact]
        public void MapsExceptionsToUserFacingKinds()
        {
            Assert.Equal(SaveErrorKind.DiskFull, SaveErrorClassifier.Classify(new IOException("full", unchecked((int)0x80070070))));
            Assert.Equal(SaveErrorKind.DiskFull, SaveErrorClassifier.Classify(new IOException("full", unchecked((int)0x80070027))));
            Assert.Equal(SaveErrorKind.DiskFull, SaveErrorClassifier.Classify(new IOException("ENOSPC", 28)));
            Assert.Equal(SaveErrorKind.AccessDenied, SaveErrorClassifier.Classify(new UnauthorizedAccessException()));
            Assert.Equal(SaveErrorKind.PathTooLong, SaveErrorClassifier.Classify(new PathTooLongException()));
            Assert.Equal(SaveErrorKind.NotFound, SaveErrorClassifier.Classify(new FileNotFoundException()));
            Assert.Equal(SaveErrorKind.NotFound, SaveErrorClassifier.Classify(new DirectoryNotFoundException()));
            Assert.Equal(SaveErrorKind.Corrupted, SaveErrorClassifier.Classify(new SaveCorruptedException(SaveCorruptionReason.Truncated, "x")));
            Assert.Equal(SaveErrorKind.AccessDenied, SaveErrorClassifier.Classify(new AggregateException(new UnauthorizedAccessException())));
            Assert.Equal(SaveErrorKind.Unknown, SaveErrorClassifier.Classify(new IOException("other")));
            Assert.Equal(SaveErrorKind.Unknown, SaveErrorClassifier.Classify(new InvalidOperationException()));

            var table = EnglishStrings.CreateTable();
            foreach (SaveErrorKind kind in Enum.GetValues(typeof(SaveErrorKind)))
                Assert.True(table.TryGet(SaveErrorClassifier.MessageKey(kind), out _), kind.ToString());
        }
    }
}
