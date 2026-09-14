using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Features;
using Xunit;

namespace WorldGen.Core.Tests.Features;

/// <summary>
/// 2. probléma (docs/backlog.md "Continent és Island fogalmak
/// szétválasztása"): <see cref="FeatureSegmentation.ClassifyLandmass"/>.
/// </summary>
public class LandmassClassificationVectorFileTests
{
    /// <summary>BITPONTOS egyezés várt - csak osztás/összehasonlítás, nincs transzcendens függvény.</summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "features_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        int totalLandTiles = root.GetProperty("totalLandTiles").GetInt32();

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("landmassClassifications").EnumerateArray())
        {
            int areaTiles = v.GetProperty("areaTiles").GetInt32();
            string expected = v.GetProperty("landmassClass").GetString()!;

            FeatureSegmentation.LandmassClass got = FeatureSegmentation.ClassifyLandmass(areaTiles, totalLandTiles);

            Assert.Equal(expected, got.ToString());
            checkedCount++;
        }
        Assert.Equal(31, checkedCount);
    }
}

public class LandmassClassificationUnitTests
{
    [Theory]
    [InlineData(501, 10000, FeatureSegmentation.LandmassClass.Continent)]   // 5.01% > 5%
    [InlineData(51, 10000, FeatureSegmentation.LandmassClass.LargeIsland)]  // 0.51%, (0.5%, 5%] sávban
    [InlineData(6, 10000, FeatureSegmentation.LandmassClass.Island)]        // 0.06%, (0.05%, 0.5%] sávban
    [InlineData(1, 10000, FeatureSegmentation.LandmassClass.Islet)]         // 0.01%
    public void ClassifiesByLandShareThresholds(int landmassTiles, int totalLand, FeatureSegmentation.LandmassClass expected)
    {
        Assert.Equal(expected, FeatureSegmentation.ClassifyLandmass(landmassTiles, totalLand));
    }

    [Fact]
    public void ExactlyAtThresholdIsExclusive()
    {
        // A hatarertek MAGA (>, nem >=) a KISEBB kategoriaba esik - ne
        // legyen "51%-a a foldnek pont kontinens, 50%-a mar nem" tipusu
        // meglepetes egy kerek szamnal.
        Assert.Equal(FeatureSegmentation.LandmassClass.LargeIsland,
            FeatureSegmentation.ClassifyLandmass(500, 10000)); // pontosan 5.0%
        Assert.Equal(FeatureSegmentation.LandmassClass.Island,
            FeatureSegmentation.ClassifyLandmass(50, 10000)); // pontosan 0.5%
        Assert.Equal(FeatureSegmentation.LandmassClass.Islet,
            FeatureSegmentation.ClassifyLandmass(5, 10000)); // pontosan 0.05%
    }

    [Fact]
    public void ZeroOrNegativeInputsFallBackToIsletWithoutThrowing()
    {
        Assert.Equal(FeatureSegmentation.LandmassClass.Islet, FeatureSegmentation.ClassifyLandmass(0, 10000));
        Assert.Equal(FeatureSegmentation.LandmassClass.Islet, FeatureSegmentation.ClassifyLandmass(5, 0));
        Assert.Equal(FeatureSegmentation.LandmassClass.Islet, FeatureSegmentation.ClassifyLandmass(-3, 10000));
        Assert.Equal(FeatureSegmentation.LandmassClass.Islet, FeatureSegmentation.ClassifyLandmass(5, -10));
    }

    [Fact]
    public void WholePlanetIsAlwaysContinent()
    {
        Assert.Equal(FeatureSegmentation.LandmassClass.Continent, FeatureSegmentation.ClassifyLandmass(10000, 10000));
    }

    [Fact]
    public void NameLookupCoversAllEnumValues()
    {
        Assert.Equal("continent", FeatureSegmentation.LandmassClassName(FeatureSegmentation.LandmassClass.Continent));
        Assert.Equal("large island", FeatureSegmentation.LandmassClassName(FeatureSegmentation.LandmassClass.LargeIsland));
        Assert.Equal("island", FeatureSegmentation.LandmassClassName(FeatureSegmentation.LandmassClass.Island));
        Assert.Equal("islet", FeatureSegmentation.LandmassClassName(FeatureSegmentation.LandmassClass.Islet));
    }
}
