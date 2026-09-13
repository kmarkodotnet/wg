using System;
using WorldGen.App.Versioning;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Versioning
{
    public class SemanticVersionTests
    {
        [Fact]
        public void ParsesAllComponents()
        {
            var v = SemanticVersion.Parse("1.20.3-rc.1+sha.5114f85");
            Assert.Equal(1, v.Major);
            Assert.Equal(20, v.Minor);
            Assert.Equal(3, v.Patch);
            Assert.Equal("rc.1", v.PreRelease);
            Assert.Equal("sha.5114f85", v.BuildMetadata);
            Assert.True(v.IsPreRelease);
            Assert.Equal("1.20.3-rc.1+sha.5114f85", v.ToString());
            Assert.Equal("1.20.3-rc.1", v.ToDisplayString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("1.2")]
        [InlineData("1.2.3.4")]
        [InlineData("01.2.3")]
        [InlineData("1.02.3")]
        [InlineData("1.2.3-")]
        [InlineData("1.2.3-01")]
        [InlineData("1.2.3-alpha..1")]
        [InlineData("1.2.3+")]
        [InlineData("1.2.3-al pha")]
        [InlineData("-1.2.3")]
        [InlineData("a.b.c")]
        [InlineData("v1.2.3")]
        [InlineData("99999999999.0.0")]
        public void RejectsInvalidText(string text)
        {
            Assert.False(SemanticVersion.TryParse(text, out _));
            Assert.Throws<FormatException>(() => SemanticVersion.Parse(text));
        }

        [Fact]
        public void NullTextIsRejected()
        {
            Assert.False(SemanticVersion.TryParse(null, out var v));
            Assert.Null(v);
        }

        /// <summary>A semver.org 2.0.0 §11 példasora, szigorúan növekvő.</summary>
        [Fact]
        public void PrecedenceFollowsSemVerSpecificationExample()
        {
            string[] ordered =
            {
                "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
                "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "2.0.0", "2.1.0", "2.1.1",
            };
            for (int i = 0; i < ordered.Length - 1; i++)
            {
                var lower = SemanticVersion.Parse(ordered[i]);
                var higher = SemanticVersion.Parse(ordered[i + 1]);
                Assert.True(lower < higher, ordered[i] + " < " + ordered[i + 1]);
                Assert.True(higher > lower);
                Assert.True(lower.CompareTo(higher) < 0);
                Assert.True(higher.CompareTo(lower) > 0);
            }
        }

        [Fact]
        public void BuildMetadataIsIgnoredForEqualityButKeptInText()
        {
            var a = SemanticVersion.Parse("0.1.0-alpha+build.1");
            var b = SemanticVersion.Parse("0.1.0-alpha+build.2");
            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a.ToString(), b.ToString());
        }

        [Theory]
        [InlineData("0.0.0")]
        [InlineData("0.1.0-alpha")]
        [InlineData("10.20.30-x-y.0.z+001.002")]
        public void RoundTripsThroughText(string text)
        {
            var v = SemanticVersion.Parse(text);
            Assert.Equal(text, v.ToString());
            Assert.Equal(v, SemanticVersion.Parse(v.ToString()));
        }

        [Fact]
        public void ConstructorValidatesArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(-1, 0, 0));
            Assert.Throws<ArgumentException>(() => new SemanticVersion(1, 0, 0, "01"));
            Assert.Throws<ArgumentException>(() => new SemanticVersion(1, 0, 0, "", "a..b"));
        }
    }

    public class BuildInfoTests
    {
        [Theory]
        [InlineData("0.1.0-alpha", ReleaseChannel.Alpha)]
        [InlineData("0.1.0-alpha.3", ReleaseChannel.Alpha)]
        [InlineData("0.2.0-beta", ReleaseChannel.Beta)]
        [InlineData("1.0.0-rc.2", ReleaseChannel.ReleaseCandidate)]
        [InlineData("0.11.0-dev", ReleaseChannel.Development)]
        [InlineData("0.11.0-nightly", ReleaseChannel.Development)]
        [InlineData("1.0.0", ReleaseChannel.Release)]
        public void InfersChannelFromPreRelease(string version, ReleaseChannel expected)
        {
            Assert.Equal(expected, BuildInfo.InferChannel(SemanticVersion.Parse(version)));
        }

        [Fact]
        public void DisplayVersionShowsBuildNumberOnlyWhenPositive()
        {
            var v = SemanticVersion.Parse("0.1.0-alpha+abc");
            Assert.Equal("v0.1.0-alpha", new BuildInfo("WorldGen", v).DisplayVersion);
            Assert.Equal("v0.1.0-alpha (build 12)", new BuildInfo("WorldGen", v, 12).DisplayVersion);
        }

        [Fact]
        public void LogLineContainsVersionChannelAndDate()
        {
            var info = new BuildInfo("WorldGen", SemanticVersion.Parse("0.1.0-alpha"), 7,
                new DateTime(2026, 9, 13, 10, 11, 12, DateTimeKind.Utc));
            Assert.Equal("WorldGen 0.1.0-alpha build 7 channel Alpha built 2026-09-13T10:11:12Z", info.ToLogLine());
        }

        [Fact]
        public void ParsesRepositoryVersionFileFormat()
        {
            const string content = "0.11.0-dev\n\nWorldGen — determinisztikus bolygó-világgenerátor.\n";
            Assert.True(BuildInfo.TryParseVersionFile(content, out var v));
            Assert.Equal("0.11.0-dev", v!.ToString());
            Assert.True(BuildInfo.TryParseVersionFile("\r\n  1.2.3\r\n", out var w));
            Assert.Equal("1.2.3", w!.ToString());
            Assert.False(BuildInfo.TryParseVersionFile("", out _));
            Assert.False(BuildInfo.TryParseVersionFile("not a version\n1.2.3", out _));
        }

        [Fact]
        public void RejectsInvalidArguments()
        {
            var v = SemanticVersion.Parse("1.0.0");
            Assert.Throws<ArgumentException>(() => new BuildInfo(" ", v));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildInfo("WorldGen", v, -1));
        }
    }
}
