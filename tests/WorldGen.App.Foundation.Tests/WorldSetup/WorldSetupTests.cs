using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.App.Localization;
using WorldGen.App.Serialization;
using WorldGen.App.WorldSetup;
using Xunit;

namespace WorldGen.App.Foundation.Tests.WorldSetup
{
    /// <summary>Előre megadott sorozatot adó entrópia; a végén körbefordul.</summary>
    public sealed class SequenceEntropy : IEntropySource
    {
        private readonly ulong[] _values;
        private int _next;

        public SequenceEntropy(params ulong[] values)
        {
            _values = values;
        }

        public int Calls { get; private set; }

        public ulong NextUInt64()
        {
            Calls++;
            ulong v = _values[_next];
            _next = (_next + 1) % _values.Length;
            return v;
        }
    }

    internal static class TestSchema
    {
        public static ParameterSchema Create() => new ParameterSchema("test.planet-v1", new[]
        {
            ParameterDefinition.Real("ocean.fraction", "param.oceanFraction", 0.0, 1.0, 0.65),
            ParameterDefinition.Integer("plates.count", "param.plateCount", 2, 40, 12),
            ParameterDefinition.Boolean("ice.enabled", "param.ice", true, isAdvanced: true),
            ParameterDefinition.Choice("star.type", "param.starType", new[] { "G", "K", "M" }, "G", isAdvanced: true),
            ParameterDefinition.Real("gravity", "param.gravity", 0.3, 3.0, 1.0, unit: "g"),
        });

        public static WorldPreset[] Presets() => new[]
        {
            new WorldPreset(WorldPresetIds.EarthLike, "preset.earthLike"),
            new WorldPreset(WorldPresetIds.OceanWorld, "preset.oceanWorld", new[]
            {
                new KeyValuePair<string, ParameterValue>("ocean.fraction", ParameterValue.Real(0.95)),
            }),
            new WorldPreset(WorldPresetIds.Random, "preset.random", null, randomizesAll: true),
        };
    }

    public class ParameterTests
    {
        [Fact]
        public void ValuesCompareBitExactlyAndCheckKinds()
        {
            Assert.Equal(ParameterValue.Real(0.1), ParameterValue.Real(0.1));
            Assert.NotEqual(ParameterValue.Real(0.0), ParameterValue.Real(-0.0));
            Assert.NotEqual(ParameterValue.Real(1), ParameterValue.Integer(1));
            Assert.Equal("G", ParameterValue.Choice("G").ToString());
            Assert.Equal("0.1", ParameterValue.Real(0.1).ToString());
            Assert.Throws<ArgumentException>(() => ParameterValue.Real(double.NaN));
            Assert.Throws<ArgumentException>(() => ParameterValue.Choice(""));
            Assert.Throws<InvalidOperationException>(() => ParameterValue.Integer(3).RealValue);
            Assert.True(ParameterValue.Boolean(true) == ParameterValue.Boolean(true));
        }

        [Fact]
        public void DefinitionValidation()
        {
            Assert.Throws<ArgumentException>(() => ParameterDefinition.Real("bad key", "l", 0, 1, 0.5));
            Assert.Throws<ArgumentException>(() => ParameterDefinition.Real("k", "l", 1, 0, 0.5));
            Assert.Throws<ArgumentException>(() => ParameterDefinition.Real("k", "l", 0, 1, 2));
            Assert.Throws<ArgumentException>(() => ParameterDefinition.Integer("k", "l", 0, 10, 11));
            Assert.Throws<ArgumentException>(() => ParameterDefinition.Choice("k", "l", new[] { "a", "a" }, "a"));
            Assert.Throws<ArgumentException>(() => ParameterDefinition.Choice("k", "l", new[] { "a" }, "b"));

            var d = ParameterDefinition.Integer("plates", "l", 2, 40, 12);
            Assert.Null(d.Check(ParameterValue.Integer(2)));
            Assert.Null(d.Check(ParameterValue.Integer(40)));
            Assert.Equal(WorldSetupErrorCode.ParameterOutOfRange, d.Check(ParameterValue.Integer(41)));
            Assert.Equal(WorldSetupErrorCode.ParameterInvalid, d.Check(ParameterValue.Real(12)));
            Assert.Equal(WorldSetupErrorCode.ParameterInvalid,
                ParameterDefinition.Choice("s", "l", new[] { "G" }, "G").Check(ParameterValue.Choice("X")));
        }

        [Fact]
        public void SchemaDefaultsAndValidation()
        {
            var schema = TestSchema.Create();
            var defaults = schema.CreateDefaults();
            Assert.Equal(new[] { "ocean.fraction", "plates.count", "ice.enabled", "star.type", "gravity" }, defaults.Keys);
            Assert.Empty(schema.Validate(defaults));

            var bad = defaults.Clone();
            bad.Remove("gravity");
            bad.Set("ocean.fraction", ParameterValue.Real(1.5));
            bad.Set("plates.count", ParameterValue.Boolean(true));
            bad.Set("legacy.param", ParameterValue.Integer(1));
            Assert.Equal(
                new[] { "ocean.fraction:ParameterOutOfRange", "plates.count:ParameterInvalid", "gravity:ParameterMissing", "legacy.param:UnknownParameter" },
                schema.Validate(bad).Select(i => i.Field + ":" + i.Code));
            Assert.False(defaults.ContentEquals(bad));

            Assert.Throws<ArgumentException>(() => new ParameterSchema("s", new[]
            {
                ParameterDefinition.Boolean("a", "l", true), ParameterDefinition.Boolean("a", "l", false),
            }));
        }

        [Fact]
        public void AllIssueMessageKeysAreLocalized()
        {
            var table = EnglishStrings.CreateTable();
            foreach (WorldSetupErrorCode code in Enum.GetValues(typeof(WorldSetupErrorCode)))
                Assert.True(table.TryGet(new ValidationIssue("x", code).MessageKey, out _), code.ToString());
        }
    }

    public class EntropyTests
    {
        [Fact]
        public void InclusiveRangeUsesRejectionSampling()
        {
            // range 3: 2^64 mod 3 = 1, ezért a 0 elutasítódik
            var source = new SequenceEntropy(0, 5);
            Assert.Equal(12, EntropyMath.NextInclusive(source, 10, 12));
            Assert.Equal(2, source.Calls);

            Assert.Equal(7, EntropyMath.NextInclusive(new SequenceEntropy(123), 7, 7));
            Assert.Equal(-1, EntropyMath.NextInclusive(new SequenceEntropy(ulong.MaxValue), long.MinValue, long.MaxValue));
            Assert.Throws<ArgumentException>(() => EntropyMath.NextInclusive(new SequenceEntropy(1), 5, 4));
        }

        [Fact]
        public void UnitDoubleStaysInHalfOpenRange()
        {
            Assert.Equal(0.0, EntropyMath.NextUnitDouble(new SequenceEntropy(0)));
            double max = EntropyMath.NextUnitDouble(new SequenceEntropy(ulong.MaxValue));
            Assert.True(max < 1.0 && max > 0.9999999);
        }

        [Fact]
        public void CryptoSourceProducesVaryingValues()
        {
            using var source = new CryptoEntropySource();
            var values = Enumerable.Range(0, 8).Select(_ => source.NextUInt64()).ToArray();
            Assert.True(values.Distinct().Count() > 1);
        }
    }

    public class WorldPresetTests
    {
        [Fact]
        public void OverridesApplyOnTopOfDefaults()
        {
            var schema = TestSchema.Create();
            var set = TestSchema.Presets()[1].CreateParameters(schema, new SequenceEntropy(1));
            Assert.Equal(0.95, set["ocean.fraction"].RealValue);
            Assert.Equal(12, set["plates.count"].IntegerValue);
        }

        [Fact]
        public void InvalidPresetDefinitionIsRejected()
        {
            var schema = TestSchema.Create();
            var unknown = new WorldPreset("x", "x", new[] { new KeyValuePair<string, ParameterValue>("nope", ParameterValue.Real(1)) });
            var outOfRange = new WorldPreset("y", "y", new[] { new KeyValuePair<string, ParameterValue>("gravity", ParameterValue.Real(9)) });
            Assert.Throws<ArgumentException>(() => unknown.CreateParameters(schema, new SequenceEntropy(1)));
            Assert.Throws<ArgumentException>(() => outOfRange.CreateParameters(schema, new SequenceEntropy(1)));
        }

        [Fact]
        public void RandomPresetStaysWithinSchemaAndIsExplicit()
        {
            var schema = TestSchema.Create();
            using var entropy = new CryptoEntropySource();
            var preset = new WorldPreset("r", "r", new[] { new KeyValuePair<string, ParameterValue>("star.type", ParameterValue.Choice("M")) }, true);
            var seenPlates = new HashSet<long>();
            for (int i = 0; i < 500; i++)
            {
                var set = preset.CreateParameters(schema, entropy);
                Assert.Empty(schema.Validate(set));
                Assert.Equal("M", set["star.type"].ChoiceValue);
                seenPlates.Add(set["plates.count"].IntegerValue);
            }
            Assert.True(seenPlates.Count > 20, "a 2..40 tartomány nagy része előfordul");
        }
    }

    public class SeedCodecTests
    {
        /// <summary>FNV-1a 64 vektorok, Pythonban a definícióból levezetett konstansokkal számolva (2026-09-13).</summary>
        [Theory]
        [InlineData("", 0xcbf29ce484222325UL)]
        [InlineData("a", 0xaf63dc4c8601ec8cUL)]
        [InlineData("foobar", 0x85944171f73967e8UL)]
        [InlineData("WorldGen", 0xee9f8ddf72e5fe2fUL)]
        [InlineData("Gaia-8214", 0x3c9cdec17d016bbdUL)]
        [InlineData("Föld \U0001F30D", 0x3d88d67bc2b57022UL)]
        [InlineData("Árvíztűrő", 0x546536523ea86837UL)]
        public void TextHashMatchesReference(string text, ulong expected)
        {
            Assert.Equal(expected, SeedCodec.HashText(text));
        }

        [Theory]
        [InlineData("42", 42UL, SeedFormat.Decimal, "42")]
        [InlineData(" 007 ", 7UL, SeedFormat.Decimal, "7")]
        [InlineData("0", 0UL, SeedFormat.Decimal, "0")]
        [InlineData("18446744073709551615", ulong.MaxValue, SeedFormat.Decimal, "18446744073709551615")]
        [InlineData("-1", ulong.MaxValue, SeedFormat.SignedDecimal, "18446744073709551615")]
        [InlineData("-9223372036854775808", 0x8000000000000000UL, SeedFormat.SignedDecimal, "9223372036854775808")]
        [InlineData("0xFF", 255UL, SeedFormat.Hexadecimal, "255")]
        [InlineData("0Xabc", 2748UL, SeedFormat.Hexadecimal, "2748")]
        [InlineData("0xFFFFFFFFFFFFFFFF", ulong.MaxValue, SeedFormat.Hexadecimal, "18446744073709551615")]
        public void ParsesNumericForms(string input, ulong value, SeedFormat format, string normalized)
        {
            Assert.True(SeedCodec.TryParse(input, out var seed));
            Assert.Equal(value, seed.Value);
            Assert.Equal(format, seed.Format);
            Assert.Equal(normalized, seed.NormalizedText);
        }

        [Theory]
        [InlineData("18446744073709551616")]
        [InlineData("-9223372036854775809")]
        [InlineData("0x")]
        [InlineData("0x1FFFFFFFFFFFFFFFF")]
        [InlineData("0xGG")]
        [InlineData("-0")]
        [InlineData("+5")]
        [InlineData("Gaia-8214")]
        [InlineData("12 34")]
        public void EverythingElseIsHashedText(string input)
        {
            Assert.True(SeedCodec.TryParse(input, out var seed));
            Assert.Equal(SeedFormat.Text, seed.Format);
            Assert.Equal(input.Trim(), seed.NormalizedText);
            Assert.Equal(SeedCodec.HashText(input.Trim()), seed.Value);
        }

        [Fact]
        public void RejectsEmptyAndOverlongInput()
        {
            Assert.False(SeedCodec.TryParse(null, out _));
            Assert.False(SeedCodec.TryParse("   ", out _));
            Assert.False(SeedCodec.TryParse(new string('x', SeedCodec.MaxTextLength + 1), out _));
            Assert.True(SeedCodec.TryParse(new string('x', SeedCodec.MaxTextLength), out _));
        }

        [Fact]
        public void SignedConversionMatchesViewerLongSeed()
        {
            const long viewerDefault = 0xA7C944210000L;
            Assert.Equal(viewerDefault, SeedCodec.ToSigned(SeedCodec.FromSigned(viewerDefault)));
            Assert.Equal(-1L, SeedCodec.ToSigned(ulong.MaxValue));
            Assert.Equal("0x0000A7C944210000", SeedCodec.FormatHex(0xA7C944210000UL));
        }
    }

    public class WorldCreationFormTests
    {
        private static WorldCreationForm NewForm(params ulong[] entropy)
            => new WorldCreationForm(TestSchema.Create(), TestSchema.Presets(), new SequenceEntropy(entropy.Length == 0 ? new ulong[] { 123456789 } : entropy));

        [Fact]
        public void StartsWithRandomSeedDefaultNameAndDefaults()
        {
            var form = NewForm(987654321);
            Assert.Equal("987654321", form.SeedText);
            Assert.Equal("World-4321", form.WorldName);
            Assert.Null(form.SelectedPresetId);
            Assert.True(form.CanStart);
            Assert.Equal("World-0042", WorldCreationForm.DefaultWorldName(10042));
        }

        [Fact]
        public void DefaultNameFollowsSeedUntilUserEditsIt()
        {
            var form = NewForm(1, 20002);
            form.SetSeedText("5555");
            Assert.Equal("World-5555", form.WorldName);
            Assert.Equal(20002UL, form.RandomizeSeed());
            Assert.Equal("World-0002", form.WorldName);

            form.SetWorldName("Gaia");
            form.RandomizeSeed();
            Assert.Equal("Gaia", form.WorldName);
        }

        [Fact]
        public void PresetsAndManualEditsTrackSelection()
        {
            var form = NewForm();
            int changes = 0;
            form.Changed += _ => changes++;

            Assert.True(form.ApplyPreset(WorldPresetIds.OceanWorld));
            Assert.Equal(WorldPresetIds.OceanWorld, form.SelectedPresetId);
            Assert.Equal(0.95, form.Parameters["ocean.fraction"].RealValue);

            form.SetParameter("gravity", ParameterValue.Real(1.2));
            Assert.Null(form.SelectedPresetId);
            Assert.False(form.ApplyPreset("missing"));
            Assert.Throws<ArgumentException>(() => form.SetParameter("missing", ParameterValue.Real(1)));

            form.ResetDefaults();
            Assert.True(form.Parameters.ContentEquals(TestSchema.Create().CreateDefaults()));
            Assert.Equal(3, changes);
        }

        [Fact]
        public void InvalidFormCannotBuildRequest()
        {
            var form = NewForm();
            form.SetWorldName("   ");
            form.SetSeedText("");
            form.SetParameter("plates.count", ParameterValue.Integer(99));

            Assert.False(form.CanStart);
            Assert.False(form.TryBuildRequest(out var request, out var issues));
            Assert.Null(request);
            Assert.Equal(new[] { "name:NameEmpty", "seed:SeedInvalid", "plates.count:ParameterOutOfRange" },
                issues.Select(i => i.Field + ":" + i.Code));

            form.SetWorldName(new string('n', WorldCreationForm.MaxNameLength + 1));
            Assert.Contains(form.Validate(), i => i.Code == WorldSetupErrorCode.NameTooLong);
            form.SetWorldName("BadName");
            Assert.Contains(form.Validate(), i => i.Code == WorldSetupErrorCode.NameInvalid);
        }

        [Fact]
        public void SameInputsProduceIdenticalIndependentRequests()
        {
            var form = NewForm();
            form.SetWorldName("  Gaia-8214 ");
            form.SetSeedText("Gaia-8214");
            form.ApplyPreset(WorldPresetIds.OceanWorld);

            Assert.True(form.TryBuildRequest(out var a, out _));
            Assert.True(form.TryBuildRequest(out var b, out _));
            Assert.Equal("Gaia-8214", a!.WorldName);
            Assert.Equal(0x3c9cdec17d016bbdUL, a.Seed);
            Assert.Equal(SeedFormat.Text, a.SeedFormat);
            Assert.Equal(a.Seed, b!.Seed);
            Assert.True(a.Parameters.ContentEquals(b.Parameters));
            Assert.Equal("test.planet-v1", a.SchemaId);
            Assert.Equal(WorldPresetIds.OceanWorld, a.PresetId);

            form.SetParameter("gravity", ParameterValue.Real(2.0));
            Assert.Equal(1.0, a.Parameters["gravity"].RealValue);
        }

        [Fact]
        public void ConfigurationRoundTripKeepsOutOfRangeForValidation()
        {
            var form = NewForm();
            form.SetWorldName("Terra");
            form.SetSeedText("0xFF");
            form.SetParameter("star.type", ParameterValue.Choice("K"));
            var config = form.ToConfiguration("gen-7");
            Assert.Equal(255UL, config.Seed);

            var other = NewForm(42);
            var imported = new ParameterSet();
            imported.Set("gravity", ParameterValue.Real(5.0));
            imported.Set("unknown.key", ParameterValue.Integer(1));
            other.ApplyConfiguration(new WorldConfiguration("Imported", 9, "other", imported, "earth-like", null));

            Assert.Equal("Imported", other.WorldName);
            Assert.Equal("9", other.SeedText);
            Assert.Equal(WorldPresetIds.EarthLike, other.SelectedPresetId);
            Assert.False(other.Parameters.TryGet("unknown.key", out _));
            Assert.Equal(new[] { "gravity:ParameterOutOfRange" }, other.Validate().Select(i => i.Field + ":" + i.Code));

            other.ApplyConfiguration(config);
            Assert.True(other.CanStart);
            Assert.True(other.Parameters.ContentEquals(form.Parameters));
        }
    }

    public class WorldConfigurationCodecTests
    {
        private static WorldConfiguration Sample()
        {
            var schema = TestSchema.Create();
            var set = schema.CreateDefaults();
            set.Set("ocean.fraction", ParameterValue.Real(0.123456789012345));
            set.Set("plates.count", ParameterValue.Integer(17));
            return new WorldConfiguration("Gaia-8214", ulong.MaxValue, schema.SchemaId, set, WorldPresetIds.EarthLike, "gen-7");
        }

        [Fact]
        public void ExportImportRoundTripsExactly()
        {
            var original = Sample();
            string text = WorldConfigurationCodec.Export(original);
            Assert.Contains("\"seed\": \"18446744073709551615\"", text);
            Assert.Contains("\"seedHex\": \"0xFFFFFFFFFFFFFFFF\"", text);
            Assert.EndsWith("}\n", text);

            var result = WorldConfigurationCodec.Import(text, TestSchema.Create(), "gen-7");
            Assert.True(result.Success);
            Assert.Empty(result.Issues);
            var back = result.Configuration!;
            Assert.Equal(original.Seed, back.Seed);
            Assert.Equal(original.WorldName, back.WorldName);
            Assert.Equal(original.PresetId, back.PresetId);
            Assert.True(original.Parameters.ContentEquals(back.Parameters));

            string clip = WorldConfigurationCodec.ToClipboardText(original);
            Assert.DoesNotContain("\n", clip);
            Assert.True(WorldConfigurationCodec.Import(clip, TestSchema.Create()).Success);
        }

        [Theory]
        [InlineData("not json", ConfigurationImportCode.InvalidJson)]
        [InlineData("[]", ConfigurationImportCode.WrongFormat)]
        [InlineData("{\"format\": \"other\"}", ConfigurationImportCode.WrongFormat)]
        [InlineData("{\"format\": \"worldgen.world-configuration\", \"formatVersion\": 2, \"seed\": \"1\"}", ConfigurationImportCode.UnsupportedVersion)]
        [InlineData("{\"format\": \"worldgen.world-configuration\", \"formatVersion\": 1}", ConfigurationImportCode.SeedMissing)]
        [InlineData("{\"format\": \"worldgen.world-configuration\", \"formatVersion\": 1, \"seed\": \"-1\"}", ConfigurationImportCode.SeedInvalid)]
        [InlineData("{\"format\": \"worldgen.world-configuration\", \"formatVersion\": 1, \"seed\": 1.5}", ConfigurationImportCode.SeedInvalid)]
        public void FatalImportErrors(string text, ConfigurationImportCode expected)
        {
            var result = WorldConfigurationCodec.Import(text, TestSchema.Create());
            Assert.False(result.Success);
            var issue = Assert.Single(result.Issues);
            Assert.Equal(expected, issue.Code);
            Assert.True(issue.IsFatal);
        }

        [Fact]
        public void RecoverableProblemsBecomeWarnings()
        {
            const string text = "{\"format\": \"worldgen.world-configuration\", \"formatVersion\": 1, \"seed\": 77," +
                " \"parameterSchema\": \"old.schema\", \"generatorVersion\": \"gen-6\"," +
                " \"parameters\": {\"ocean.fraction\": \"wet\", \"plates.count\": 8, \"ice.enabled\": false, \"star.type\": \"K\", \"legacy\": 1}}";
            var result = WorldConfigurationCodec.Import(text, TestSchema.Create(), "gen-7");

            Assert.True(result.Success);
            var config = result.Configuration!;
            Assert.Equal(77UL, config.Seed);
            Assert.Equal("World-0077", config.WorldName);
            Assert.Equal(0.65, config.Parameters["ocean.fraction"].RealValue);
            Assert.Equal(8, config.Parameters["plates.count"].IntegerValue);
            Assert.Equal(1.0, config.Parameters["gravity"].RealValue);
            Assert.Equal(
                new[]
                {
                    ConfigurationImportCode.NameMissing, ConfigurationImportCode.SchemaMismatch, ConfigurationImportCode.GeneratorVersionMismatch,
                    ConfigurationImportCode.ParameterInvalid, ConfigurationImportCode.ParameterMissing, ConfigurationImportCode.UnknownParameter,
                },
                result.Issues.Select(i => i.Code));
            Assert.All(result.Issues, i => Assert.False(i.IsFatal));
        }

        [Fact]
        public void ParameterJsonUsesNativeTypes()
        {
            var json = WorldConfigurationCodec.ParametersToJson(Sample().Parameters);
            Assert.Equal("{\"ocean.fraction\":0.123456789012345,\"plates.count\":17,\"ice.enabled\":true,\"star.type\":\"G\",\"gravity\":1}",
                JsonWriter.Write(json, indented: false));
        }
    }
}
