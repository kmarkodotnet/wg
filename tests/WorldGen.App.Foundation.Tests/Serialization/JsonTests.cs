using System;
using System.Linq;
using System.Text;
using WorldGen.App.Serialization;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Serialization
{
    public class JsonTests
    {
        [Fact]
        public void ParsesNestedDocumentPreservingMemberOrder()
        {
            var doc = JsonParser.Parse(" { \"version\": 1, \"graphics\": { \"vSync\": true, \"quality\": \"High\" },\n \"list\": [1, -2.5e3, null, false] } ");
            Assert.Equal(JsonValueKind.Object, doc.Kind);
            Assert.Equal(new[] { "version", "graphics", "list" }, doc.Members.Select(m => m.Key));
            Assert.True(doc.GetMember("version")!.TryGetInt32(out int version));
            Assert.Equal(1, version);
            Assert.True(doc.GetMember("graphics")!.GetMember("vSync")!.AsBoolean());
            Assert.Equal("High", doc.GetMember("graphics")!.GetMember("quality")!.AsString());
            var list = doc.GetMember("list")!;
            Assert.Equal(4, list.Count);
            Assert.True(list[1].TryGetDouble(out double d));
            Assert.Equal(-2500.0, d);
            Assert.True(list[2].IsNull);
            Assert.False(list[3].AsBoolean());
            Assert.Null(doc.GetMember("missing"));
        }

        [Fact]
        public void IndentedWriterOutputIsExactAndRoundTrips()
        {
            var obj = JsonValue.CreateObject()
                .Set("version", 1)
                .Set("name", "Gaia \"8214\"")
                .Set("empty", JsonValue.CreateArray())
                .Set("nested", JsonValue.CreateObject().Set("ratio", 0.1).Set("on", true))
                .Set("items", JsonValue.CreateArray().Add(JsonValue.FromNumber(1)).Add(JsonValue.Null));

            const string expected = "{\n  \"version\": 1,\n  \"name\": \"Gaia \\\"8214\\\"\",\n  \"empty\": [],\n"
                + "  \"nested\": {\n    \"ratio\": 0.1,\n    \"on\": true\n  },\n  \"items\": [\n    1,\n    null\n  ]\n}";
            string text = JsonWriter.Write(obj);
            Assert.Equal(expected, text);
            Assert.Equal(text, JsonWriter.Write(JsonParser.Parse(text)));
            Assert.Equal("{\"version\":1,\"name\":\"Gaia \\\"8214\\\"\",\"empty\":[],\"nested\":{\"ratio\":0.1,\"on\":true},\"items\":[1,null]}",
                obj.ToString());
        }

        [Fact]
        public void SixtyFourBitSeedSurvivesExactly()
        {
            var doc = JsonParser.Parse("{\"seed\": 18446744073709551615, \"neg\": -9223372036854775808}");
            Assert.True(doc.GetMember("seed")!.TryGetUInt64(out ulong seed));
            Assert.Equal(ulong.MaxValue, seed);
            Assert.False(doc.GetMember("seed")!.TryGetInt64(out _));
            Assert.True(doc.GetMember("neg")!.TryGetInt64(out long neg));
            Assert.Equal(long.MinValue, neg);
            Assert.False(doc.GetMember("neg")!.TryGetUInt64(out _));
            Assert.Equal("18446744073709551615", JsonWriter.Write(JsonValue.FromNumber(ulong.MaxValue)));
        }

        [Theory]
        [InlineData("1.5")]
        [InlineData("1e3")]
        [InlineData("2147483648")]
        public void IntegerAccessorsRejectNonIntegerOrOutOfRange(string text)
        {
            Assert.False(JsonParser.Parse(text).TryGetInt32(out _));
        }

        [Fact]
        public void DoubleRoundTripsBitExactly()
        {
            foreach (double value in new[] { 0.1, 1.0 / 3.0, -1e-300, 1.7976931348623157e308, 5e-324 })
            {
                string text = JsonWriter.Write(JsonValue.FromNumber(value));
                Assert.True(JsonParser.Parse(text).TryGetDouble(out double back));
                Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(back));
            }
            Assert.Throws<ArgumentException>(() => JsonValue.FromNumber(double.NaN));
            Assert.Throws<ArgumentException>(() => JsonValue.FromNumber(double.NegativeInfinity));
            Assert.False(JsonParser.Parse("1e400").TryGetDouble(out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{")]
        [InlineData("[1,]")]
        [InlineData("{\"a\":1,}")]
        [InlineData("01")]
        [InlineData("1.")]
        [InlineData(".5")]
        [InlineData("-")]
        [InlineData("+1")]
        [InlineData("1e")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("'a'")]
        [InlineData("\"abc")]
        [InlineData("\"\\x\"")]
        [InlineData("\"\t\"")]
        [InlineData("\"\\u12G4\"")]
        [InlineData("\"\\u12\"")]
        [InlineData("{\"a\":1,\"a\":2}")]
        [InlineData("[1] 2")]
        [InlineData("tru")]
        [InlineData("nul")]
        [InlineData("trueX")]
        [InlineData("{a:1}")]
        [InlineData("/*c*/1")]
        [InlineData("[1 2]")]
        public void RejectsInvalidDocuments(string text)
        {
            Assert.Throws<JsonFormatException>(() => JsonParser.Parse(text));
            Assert.False(JsonParser.TryParse(text, out var value, out string? error));
            Assert.Null(value);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void ErrorReportsLineAndColumn()
        {
            var ex = Assert.Throws<JsonFormatException>(() => JsonParser.Parse("{\n  \"a\": x\n}"));
            Assert.Equal(2, ex.Line);
            Assert.Equal(8, ex.Column);
            Assert.Equal(9, ex.Position);
        }

        [Fact]
        public void DepthLimitIsEnforced()
        {
            string ok = new string('[', 64) + new string(']', 64);
            string tooDeep = new string('[', 65) + new string(']', 65);
            Assert.Equal(1, JsonParser.Parse(ok).Count);
            Assert.Throws<JsonFormatException>(() => JsonParser.Parse(tooDeep));
            Assert.Throws<JsonFormatException>(() => JsonParser.Parse("[[[]]]", maxDepth: 2));
        }

        [Fact]
        public void HandlesEscapesUnicodeAndBom()
        {
            Assert.Equal("é\n/\"\\\b\f\r\t", JsonParser.Parse("\"\\u00e9\\n\\/\\\"\\\\\\b\\f\\r\\t\"").AsString());
            Assert.Equal("\U0001F30D", JsonParser.Parse("\"\\ud83c\\udf0d\"").AsString());
            Assert.Equal("Föld 🌍", JsonParser.Parse("\uFEFF\"Föld 🌍\"").AsString());
            Assert.Equal("\"\\u0001\\u001f é\"", JsonWriter.Write(JsonValue.FromString("\u0001\u001f é")));
        }

        [Fact]
        public void ObjectEditingKeepsPositionsAndChecksTypes()
        {
            var obj = JsonValue.CreateObject().Set("a", 1).Set("b", 2).Set("c", 3);
            obj.Set("b", "two");
            Assert.Equal(new[] { "a", "b", "c" }, obj.Members.Select(m => m.Key));
            Assert.True(obj.Remove("a"));
            Assert.False(obj.Remove("a"));
            Assert.Equal(2, obj.Count);
            Assert.True(obj.ContainsKey("c"));

            Assert.Throws<InvalidOperationException>(() => obj.AsString());
            Assert.Throws<InvalidOperationException>(() => obj.Add(JsonValue.Null));
            Assert.Throws<InvalidOperationException>(() => JsonValue.FromString("x").Count);
            Assert.False(JsonValue.FromString("1").TryGetDouble(out _));
            Assert.False(JsonValue.FromNumber(1).TryGetString(out _));
        }

        [Fact]
        public void Utf8EncodedWriterOutputParsesBack()
        {
            var obj = JsonValue.CreateObject().Set("name", "Árvíztűrő tükörfúrógép");
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonWriter.Write(obj));
            Assert.Equal("Árvíztűrő tükörfúrógép", JsonParser.Parse(Encoding.UTF8.GetString(bytes)).GetMember("name")!.AsString());
        }
    }

    public class Crc32Tests
    {
        /// <summary>A várt értékek a Python zlib.crc32-vel mérve (2026-09-13).</summary>
        [Theory]
        [InlineData("123456789", 0xCBF43926u)]
        [InlineData("", 0x00000000u)]
        [InlineData("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
        public void MatchesZlibReference(string ascii, uint expected)
        {
            Assert.Equal(expected, Crc32.Compute(Encoding.ASCII.GetBytes(ascii)));
        }

        [Fact]
        public void IncrementalEqualsWholeAndRespectsBounds()
        {
            byte[] data = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog");
            uint running = 0;
            for (int i = 0; i < data.Length; i += 7)
                running = Crc32.Append(running, data, i, Math.Min(7, data.Length - i));
            Assert.Equal(Crc32.Compute(data), running);
            Assert.Equal(Crc32.Compute(Encoding.ASCII.GetBytes("quick")), Crc32.Compute(data, 4, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => Crc32.Compute(data, 40, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => Crc32.Compute(data, -1, 1));
        }
    }
}
