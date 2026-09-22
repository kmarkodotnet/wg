using System;
using System.IO;
using System.Linq;
using WorldGen.App.Saves;
using WorldGen.App.Serialization;
using WorldGen.App.UnityBinding;
using WorldGen.Core.Persistence;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Saves
{
    public class CoreSavePolicyTests
    {
        private static readonly string SavesDir = Path.Combine(InMemoryFileSystem.Root, "Saves");

        private static SaveRepository Repository(InMemoryFileSystem fs) =>
            new SaveRepository(fs, SavesDir, CoreSavePolicy.Evaluate);

        private static SaveHeader CurrentHeader()
        {
            var header = CoreSavePolicy.CreateHeader();
            header.WorldName = "Versioned world";
            header.Seed = ulong.MaxValue;
            header.CreatedUtc = SaveSamples.Created;
            header.LastPlayedUtc = SaveSamples.Created;
            return header;
        }

        [Theory]
        [InlineData("0.0.1")]
        [InlineData("99.0.0")]
        public void CoreVersionIsStampedAndRoundTripsIndependentlyOfAppVersion(string appVersion)
        {
            var fs = new InMemoryFileSystem();
            var repo = Repository(fs);
            var header = CurrentHeader();
            header.ApplicationVersion = appVersion;
            string path = repo.CreateFilePath(header.WorldName, SaveKind.Manual, header.CreatedUtc);
            repo.Write(path, header, SaveSamples.Sections());

            var slot = Assert.Single(repo.List());
            Assert.Equal(SaveCompatibilityLevel.Compatible, slot.Compatibility.Level);
            Assert.Same(slot, SaveRepository.FindContinueCandidate(new[] { slot }));
            var loaded = repo.Load(path);
            Assert.Equal(WorldGeneratorVersion.Current, loaded.Header.WorldGeneratorVersion);
            Assert.Equal(appVersion, loaded.Header.ApplicationVersion);
            Assert.Equal(ulong.MaxValue, loaded.Header.Seed);
            Assert.Equal(SaveSamples.Sections()[0].Data, loaded.Sections[0].Data);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("0")]
        [InlineData("999")]
        [InlineData("01")]
        [InlineData("unversioned-dev")]
        public void DifferentGeneratorIsConfigurationOnlyAndDirectLoadIsBlocked(string version)
        {
            var fs = new InMemoryFileSystem();
            var repo = Repository(fs);
            var header = CurrentHeader();
            header.WorldGeneratorVersion = version;
            header.Parameters = JsonValue.CreateObject().Set("plates.count", 20);
            string path = repo.CreateFilePath(header.WorldName, SaveKind.Manual, header.CreatedUtc);
            repo.Write(path, header, SaveSamples.Sections());
            byte[] before = fs.ReadAllBytes(path);

            var slot = Assert.Single(repo.List());
            Assert.Equal(SaveCompatibilityLevel.ConfigurationOnly, slot.Compatibility.Level);
            Assert.Null(SaveRepository.FindContinueCandidate(new[] { slot }));
            var error = Assert.Throws<SaveIncompatibleException>(() => repo.Load(path));
            Assert.Equal(IncompatibilityReason.GeneratorChanged, error.Compatibility.Reason);
            Assert.Equal("save.compat.generatorChanged", error.Compatibility.MessageKey);
            var configuration = repo.LoadHeader(path);
            Assert.Equal(version, configuration.WorldGeneratorVersion);
            Assert.Equal(header.Seed, configuration.Seed);
            Assert.Equal(JsonWriter.Write(header.Parameters), JsonWriter.Write(configuration.Parameters));
            Assert.Equal(before, fs.ReadAllBytes(path));
        }

        [Fact]
        public void MissingGeneratorIsNotSilentlyUpgradedByCodec()
        {
            var json = SaveHeaderCodec.ToJson(CurrentHeader());
            json.Remove("worldGeneratorVersion");
            var header = SaveHeaderCodec.FromJson(json);
            Assert.Equal("", header.WorldGeneratorVersion);
            Assert.Equal(SaveCompatibilityLevel.ConfigurationOnly, CoreSavePolicy.Evaluate(header).Level);
        }

        [Theory]
        [InlineData(0, IncompatibilityReason.NoMigrationPath)]
        [InlineData(2, IncompatibilityReason.NewerFormat)]
        public void MatchingGeneratorDoesNotOverrideFormat(int format, IncompatibilityReason reason)
        {
            var header = CurrentHeader();
            header.SaveFormatVersion = format;
            var result = CoreSavePolicy.Evaluate(header);
            Assert.False(result.CanLoadState);
            Assert.Equal(reason, result.Reason);
        }

        [Fact]
        public void LoadRechecksFileAfterCompatibleListingAndRejectsBeforePayload()
        {
            var fs = new InMemoryFileSystem();
            var repo = Repository(fs);
            var header = CurrentHeader();
            string path = repo.CreateFilePath(header.WorldName, SaveKind.Manual, header.CreatedUtc);
            repo.Write(path, header, SaveSamples.Sections());
            Assert.True(Assert.Single(repo.List()).Compatibility.CanLoadState);

            header.WorldGeneratorVersion = "obsolete";
            byte[] changed = SaveContainer.ToBytes(header, SaveSamples.Sections());
            // Csak a teljes fejléc marad meg: a szekciók elérése sérüléshibát adna.
            using (var stream = new MemoryStream(changed))
            {
                SaveContainer.ReadHeader(stream);
                fs.WriteFile(path, changed.Take((int)stream.Position).ToArray());
            }
            var error = Assert.Throws<SaveIncompatibleException>(() => repo.Load(path));
            Assert.Equal(IncompatibilityReason.GeneratorChanged, error.Compatibility.Reason);
        }

        [Fact]
        public void HeaderGateAlsoWorksWithoutSeekingAndAfterChecksumValidation()
        {
            var header = CurrentHeader();
            byte[] data = SaveContainer.ToBytes(header, SaveSamples.Sections());
            bool checkedHeader = false;
            using (var stream = new NonSeekableStream(data))
            {
                var file = SaveContainer.Read(stream, h =>
                {
                    checkedHeader = true;
                    Assert.True(CoreSavePolicy.Evaluate(h).CanLoadState);
                });
                Assert.Equal(SaveSamples.Sections()[0].Data, file.Sections[0].Data);
            }
            Assert.True(checkedHeader);
            checkedHeader = false;
            data[12] ^= 1; // A fejléc első bájtja; CRC már nem egyezik.
            using (var stream = new MemoryStream(data))
            {
                Assert.Throws<SaveCorruptedException>(() => SaveContainer.Read(stream, _ => checkedHeader = true));
                Assert.False(checkedHeader);
            }
        }
    }
}
