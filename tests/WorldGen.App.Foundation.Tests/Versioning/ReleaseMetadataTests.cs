using System;
using System.IO;
using WorldGen.App.Flow;
using WorldGen.App.Versioning;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Versioning
{
    public class BuildInfoCodecTests
    {
        [Fact]
        public void RoundTripsAllFields()
        {
            var info = new BuildInfo("WorldGen", SemanticVersion.Parse("0.1.0-alpha+abc"), 12, ReleaseChannel.Alpha,
                new DateTime(2026, 9, 13, 10, 11, 12, DateTimeKind.Utc));
            string json = BuildInfoCodec.ToJson(info);
            Assert.True(BuildInfoCodec.TryParse(json, out var back));
            Assert.Equal(info.ToLogLine(), back!.ToLogLine());
            Assert.Equal("0.1.0-alpha+abc", back.Version.ToString());
            Assert.Equal(DateTimeKind.Utc, back.BuildDateUtc!.Value.Kind);
        }

        [Fact]
        public void OptionalFieldsDefaultAndInvalidInputIsRejected()
        {
            Assert.True(BuildInfoCodec.TryParse("{\"productName\": \"WorldGen\", \"version\": \"1.0.0-beta\"}", out var minimal));
            Assert.Equal(0, minimal!.BuildNumber);
            Assert.Equal(ReleaseChannel.Beta, minimal.Channel);
            Assert.Null(minimal.BuildDateUtc);

            Assert.False(BuildInfoCodec.TryParse(null, out _));
            Assert.False(BuildInfoCodec.TryParse("not json", out _));
            Assert.False(BuildInfoCodec.TryParse("{\"productName\": \"\", \"version\": \"1.0.0\"}", out _));
            Assert.False(BuildInfoCodec.TryParse("{\"productName\": \"W\", \"version\": \"1.0\"}", out _));
            Assert.False(BuildInfoCodec.TryParse("{\"productName\": \"W\", \"version\": \"1.0.0\", \"buildNumber\": -1}", out _));
            Assert.False(BuildInfoCodec.TryParse("{\"productName\": \"W\", \"version\": \"1.0.0\", \"buildDateUtc\": \"yesterday\"}", out _));
        }
    }

    public class ReleaseIdentityTests
    {
        private const string Valid =
            "{\"format\": \"worldgen.release-identity\", \"formatVersion\": 1, \"productName\": \"WorldGen\", \"companyName\": \"WorldGen\"," +
            " \"executableName\": \"WorldGen\", \"version\": \"0.1.0-alpha\", \"fallbackScenes\": [\"Assets/PlanetView.unity\"]}";

        [Fact]
        public void ParsesIdentityAndDerivesArtifactNames()
        {
            var identity = ReleaseIdentity.Parse(Valid);
            Assert.Equal("WorldGen-0.1.0-alpha-win64", identity.BuildFolderName);
            Assert.Equal("WorldGen-0.1.0-alpha-win64.zip", identity.PortableZipFileName);
            Assert.Equal("WorldGen-0.1.0-alpha-win64-setup", identity.InstallerBaseName);
            Assert.Equal("0.1.0.7", identity.NumericFileVersion(7));
            Assert.Equal(new[] { "Assets/PlanetView.unity" }, identity.FallbackScenes);
            Assert.Throws<ArgumentOutOfRangeException>(() => identity.NumericFileVersion(70000));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{\"format\": \"other\", \"formatVersion\": 1}")]
        [InlineData("{\"format\": \"worldgen.release-identity\", \"formatVersion\": 2}")]
        [InlineData("{\"format\": \"worldgen.release-identity\", \"formatVersion\": 1, \"productName\": \"W\", \"companyName\": \"C\", \"executableName\": \"World Gen\", \"version\": \"0.1.0\"}")]
        [InlineData("{\"format\": \"worldgen.release-identity\", \"formatVersion\": 1, \"productName\": \"W:G\", \"companyName\": \"C\", \"executableName\": \"W\", \"version\": \"0.1.0\"}")]
        [InlineData("{\"format\": \"worldgen.release-identity\", \"formatVersion\": 1, \"productName\": \"W\", \"companyName\": \"C\", \"executableName\": \"W\", \"version\": \"v1\"}")]
        [InlineData("{\"format\": \"worldgen.release-identity\", \"formatVersion\": 1, \"productName\": \"W\", \"companyName\": \"C\", \"executableName\": \"W\", \"version\": \"0.1.0\", \"fallbackScenes\": [\"PlanetView.unity\"]}")]
        public void RejectsInvalidIdentity(string json)
        {
            Assert.Throws<FormatException>(() => ReleaseIdentity.Parse(json));
        }

        /// <summary>A repóban lévő tényleges fájl érvényes (a build-script és a csomagolók ezt olvassák).</summary>
        [Fact]
        public void RepositoryIdentityFileIsValid()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "WorldGen.sln"))) dir = Path.GetDirectoryName(dir)!;
            Assert.NotNull(dir);
            var identity = ReleaseIdentity.Parse(File.ReadAllText(Path.Combine(dir!, "tools", "release", "release-identity.json")));
            Assert.Equal("WorldGen", identity.ExecutableName);
        }
    }

    public class WorldSessionHostSlotTests
    {
        private sealed class Host : IWorldSessionHost
        {
            public bool HasUnsavedChanges => true;
            public bool CanSave => true;
            public string? CurrentSavePath => "a.wgsave";
            public string? CurrentWorldId => "id";
        }

        [Fact]
        public void ForwardsToTargetOrReportsNothingToSave()
        {
            var slot = new WorldSessionHostSlot();
            Assert.False(slot.HasUnsavedChanges);
            Assert.False(slot.CanSave);
            Assert.Null(slot.CurrentSavePath);
            slot.Target = new Host();
            Assert.True(slot.HasUnsavedChanges && slot.CanSave);
            Assert.Equal("a.wgsave", slot.CurrentSavePath);
            Assert.Equal("id", slot.CurrentWorldId);
        }
    }
}
