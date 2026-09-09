using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Climate;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public class BiomeClassificationVectorFileTests
{
    private static Biome ParseBiome(string s) => s switch
    {
        "Ocean" => Biome.Ocean,
        "SeaIce" => Biome.SeaIce,
        "IceSheet" => Biome.IceSheet,
        "Tundra" => Biome.Tundra,
        "Temperate" => Biome.Temperate,
        "Tropical" => Biome.Tropical,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    /// <summary>BITPONTOS egyezés várt - csak küszöb-összehasonlítás, nincs transzcendens függvény.</summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "biome_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            double temperatureK = v.GetProperty("temperatureK").GetDouble();
            bool isOceanic = v.GetProperty("isOceanic").GetBoolean();
            Biome expected = ParseBiome(v.GetProperty("biome").GetString()!);

            Biome got = BiomeClassification.Classify(temperatureK, isOceanic);

            Assert.Equal(expected, got);
            checkedCount++;
        }

        Assert.Equal(200, checkedCount);
    }
}

public class BiomeClassificationThresholdTests
{
    [Theory]
    [InlineData(260.0, false, Biome.IceSheet)]
    [InlineData(263.15, false, Biome.Tundra)]
    [InlineData(270.0, false, Biome.Tundra)]
    [InlineData(278.15, false, Biome.Temperate)]
    [InlineData(285.0, false, Biome.Temperate)]
    [InlineData(293.15, false, Biome.Tropical)]
    [InlineData(300.0, false, Biome.Tropical)]
    [InlineData(265.0, true, Biome.SeaIce)]
    [InlineData(271.15, true, Biome.Ocean)]
    [InlineData(280.0, true, Biome.Ocean)]
    public void ClassifiesThresholdCasesCorrectly(double temperatureK, bool isOceanic, Biome expected)
    {
        Assert.Equal(expected, BiomeClassification.Classify(temperatureK, isOceanic));
    }

    [Fact]
    public void IsPure()
    {
        Biome a = BiomeClassification.Classify(280.0, false);
        Biome b = BiomeClassification.Classify(280.0, false);
        Assert.Equal(a, b);
    }

    [Fact]
    public void EveryParameterAffectsOutput()
    {
        Biome baseline = BiomeClassification.Classify(280.0, false);
        Assert.NotEqual(baseline, BiomeClassification.Classify(280.0, true));
        Assert.NotEqual(baseline, BiomeClassification.Classify(200.0, false));
    }
}
