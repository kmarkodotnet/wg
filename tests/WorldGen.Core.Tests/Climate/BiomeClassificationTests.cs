using System;
using System.Collections.Generic;
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
        "Desert" => Biome.Desert,
        "Grassland" => Biome.Grassland,
        "TemperateForest" => Biome.TemperateForest,
        "Savanna" => Biome.Savanna,
        "Rainforest" => Biome.Rainforest,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    /// <summary>BITPONTOS egyezés várt - csak küszöb-összehasonlítás, nincs transzcendens függvény.</summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "biome_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        JsonElement th = root.GetProperty("thresholds");
        var thresholds = new BiomeClassification.PrecipitationThresholds(
            th.GetProperty("arid").GetDouble(),
            th.GetProperty("semiArid").GetDouble(),
            th.GetProperty("moist").GetDouble());

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            double temperatureK = v.GetProperty("temperatureK").GetDouble();
            bool isOceanic = v.GetProperty("isOceanic").GetBoolean();
            double precipitation = v.GetProperty("precipitation").GetDouble();
            Biome expected = ParseBiome(v.GetProperty("biome").GetString()!);

            Biome got = BiomeClassification.Classify(temperatureK, isOceanic, precipitation, thresholds);

            Assert.Equal(expected, got);
            checkedCount++;
        }

        Assert.Equal(200, checkedCount);
    }
}

public class BiomeClassificationThresholdTests
{
    private static readonly BiomeClassification.PrecipitationThresholds Th =
        new BiomeClassification.PrecipitationThresholds(1.0, 2.0, 3.0);

    [Theory]
    // A hideg veg csapadektol FUGGETLEN - a sarkvideki "hideg sivatag" is jeg/tundra.
    [InlineData(260.0, false, 5.0, Biome.IceSheet)]
    [InlineData(263.15, false, 5.0, Biome.Tundra)]
    [InlineData(270.0, false, 0.0, Biome.Tundra)]
    // Mersekelt sav (5..20 C), csapadek szerint
    [InlineData(278.15, false, 0.5, Biome.Desert)]
    [InlineData(278.15, false, 1.0, Biome.Desert)]
    [InlineData(280.0, false, 1.5, Biome.Grassland)]
    [InlineData(285.0, false, 2.0, Biome.Grassland)]
    [InlineData(285.0, false, 2.5, Biome.TemperateForest)]
    [InlineData(285.0, false, 3.0, Biome.TemperateForest)]
    [InlineData(285.0, false, 3.5, Biome.Rainforest)]
    // Tropusi sav (20 C folott) - CSAK a "kozepes" sor ter el
    [InlineData(293.15, false, 0.5, Biome.Desert)]
    [InlineData(300.0, false, 1.5, Biome.Grassland)]
    [InlineData(300.0, false, 2.5, Biome.Savanna)]
    [InlineData(300.0, false, 3.5, Biome.Rainforest)]
    // Ocean
    [InlineData(265.0, true, 0.0, Biome.SeaIce)]
    [InlineData(271.15, true, 0.0, Biome.Ocean)]
    [InlineData(280.0, true, 9.0, Biome.Ocean)]
    public void ClassifiesThresholdCasesCorrectly(double temperatureK, bool isOceanic, double precip, Biome expected)
    {
        Assert.Equal(expected, BiomeClassification.Classify(temperatureK, isOceanic, precip, Th));
    }

    [Fact]
    public void IsPure()
    {
        Biome a = BiomeClassification.Classify(285.0, false, 2.5, Th);
        Biome b = BiomeClassification.Classify(285.0, false, 2.5, Th);
        Assert.Equal(a, b);
    }

    /// <summary>
    /// ND-126 LENYEG: a csapadek ERDEMBEN hat. Ha ez elbukik, visszaestunk a
    /// tisztan homersekleti osztalyozasra, ami szelessegi savokat ad.
    /// </summary>
    [Fact]
    public void EveryParameterAffectsOutput()
    {
        Biome baseline = BiomeClassification.Classify(285.0, false, 2.5, Th);

        Assert.NotEqual(baseline, BiomeClassification.Classify(285.0, true, 2.5, Th));   // isOceanic
        Assert.NotEqual(baseline, BiomeClassification.Classify(200.0, false, 2.5, Th));  // homerseklet
        Assert.NotEqual(baseline, BiomeClassification.Classify(285.0, false, 0.1, Th));  // CSAPADEK
        Assert.NotEqual(baseline, BiomeClassification.Classify(285.0, false, 4.0, Th));  // CSAPADEK

        // ...es a kuszobok is hatnak, nem csak az ertek.
        var wetter = new BiomeClassification.PrecipitationThresholds(3.0, 4.0, 5.0);
        Assert.NotEqual(baseline, BiomeClassification.Classify(285.0, false, 2.5, wetter));
    }

    /// <summary>
    /// UGYANAZ a csapadek MAS biome-ot ad a ket homersekleti sávban - ez az,
    /// amitol a szelessegi savok feltorednek.
    /// </summary>
    [Fact]
    public void SamePrecipitationGivesDifferentBiomeInWarmAndCoolBands()
    {
        Assert.Equal(Biome.TemperateForest, BiomeClassification.Classify(285.0, false, 2.5, Th));
        Assert.Equal(Biome.Savanna, BiomeClassification.Classify(300.0, false, 2.5, Th));
    }
}

public class BiomePrecipitationThresholdTests
{
    [Fact]
    public void PercentilesMatchTheReferenceIndexFormula()
    {
        var values = new List<double>();
        for (int i = 0; i < 100; i++) values.Add(i);

        BiomeClassification.PrecipitationThresholds th = BiomeClassification.ComputeThresholds(values);

        // index = (int)(q * n), ugyanaz, mint a Python referenciaban
        Assert.Equal(20.0, th.Arid);
        Assert.Equal(45.0, th.SemiArid);
        Assert.Equal(75.0, th.Moist);
    }

    [Fact]
    public void IsIndependentOfInputOrder()
    {
        var ascending = new List<double>();
        for (int i = 0; i < 500; i++) ascending.Add(i * 0.37);
        var descending = new List<double>(ascending);
        descending.Reverse();

        BiomeClassification.PrecipitationThresholds a = BiomeClassification.ComputeThresholds(ascending);
        BiomeClassification.PrecipitationThresholds b = BiomeClassification.ComputeThresholds(descending);

        Assert.Equal(a.Arid, b.Arid);
        Assert.Equal(a.SemiArid, b.SemiArid);
        Assert.Equal(a.Moist, b.Moist);
    }

    [Fact]
    public void EmptyDistributionGivesZeroCutsSoEverythingPositiveIsWettest()
    {
        BiomeClassification.PrecipitationThresholds th =
            BiomeClassification.ComputeThresholds(new List<double>());

        Assert.Equal(0.0, th.Arid);
        Assert.Equal(0.0, th.SemiArid);
        Assert.Equal(0.0, th.Moist);
        Assert.Equal(Biome.Rainforest, BiomeClassification.Classify(285.0, false, 0.001, th));
        // ...de a pontosan 0 csapadek meg mindig sivatag (<=  arid).
        Assert.Equal(Biome.Desert, BiomeClassification.Classify(285.0, false, 0.0, th));
    }

    [Fact]
    public void SingleValueDistributionIsHandled()
    {
        BiomeClassification.PrecipitationThresholds th =
            BiomeClassification.ComputeThresholds(new List<double> { 4.0 });

        Assert.Equal(4.0, th.Arid);
        Assert.Equal(4.0, th.SemiArid);
        Assert.Equal(4.0, th.Moist);
    }

    [Fact]
    public void NullInputThrows()
    {
        Assert.Throws<ArgumentNullException>(() => BiomeClassification.ComputeThresholds(null!));
    }

    [Fact]
    public void TemperatureOnlyClassifierAnswersOnlyForTheColdAndOceanCases()
    {
        Assert.Equal(Biome.Ocean, BiomeClassification.ClassifyTemperatureOnly(280.0, true));
        Assert.Equal(Biome.SeaIce, BiomeClassification.ClassifyTemperatureOnly(265.0, true));
        Assert.Equal(Biome.IceSheet, BiomeClassification.ClassifyTemperatureOnly(260.0, false));
        Assert.Equal(Biome.Tundra, BiomeClassification.ClassifyTemperatureOnly(270.0, false));
        Assert.Null(BiomeClassification.ClassifyTemperatureOnly(285.0, false));
        Assert.Null(BiomeClassification.ClassifyTemperatureOnly(300.0, false));
    }

    /// <summary>
    /// A VEGETALT szuro: a hideg mintak csapadeka NEM szamit bele a
    /// vagopontokba. A szamok 1:1 a Python referencia onellenorzesevel
    /// (biome_ref.py __main__).
    /// </summary>
    [Fact]
    public void VegetatedLandFilterExcludesColdSamples()
    {
        var samples = new List<(double TemperatureK, double Precipitation)>();
        for (int i = 0; i < 100; i++) samples.Add((250.0, 0.0));        // hideg, szaraz
        for (int i = 0; i < 100; i++) samples.Add((290.0, i));          // vegetalt, 0..99

        BiomeClassification.PrecipitationThresholds veg =
            BiomeClassification.ComputeThresholdsForVegetatedLand(samples);
        Assert.Equal(20.0, veg.Arid);
        Assert.Equal(45.0, veg.SemiArid);
        Assert.Equal(75.0, veg.Moist);

        // Szures NELKUL a hideg nullak lehuznak: ez volt a mert hiba.
        var all = new List<double>();
        foreach ((double _, double p) in samples) all.Add(p);
        BiomeClassification.PrecipitationThresholds unfiltered =
            BiomeClassification.ComputeThresholds(all);
        Assert.Equal(0.0, unfiltered.Arid);
        Assert.Equal(0.0, unfiltered.SemiArid);
        Assert.Equal(50.0, unfiltered.Moist);
    }

    [Fact]
    public void VegetatedLandFilterUsesTheTundraThresholdAsTheCut()
    {
        var samples = new List<(double TemperatureK, double Precipitation)>
        {
            (BiomeClassification.TundraThresholdK - 0.01, 1000.0), // MEG hideg -> kimarad
            (BiomeClassification.TundraThresholdK, 7.0),           // MAR vegetalt -> beszamit
        };

        BiomeClassification.PrecipitationThresholds th =
            BiomeClassification.ComputeThresholdsForVegetatedLand(samples);
        Assert.Equal(7.0, th.Arid);
        Assert.Equal(7.0, th.Moist);
    }

    [Fact]
    public void VegetatedLandFilterRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => BiomeClassification.ComputeThresholdsForVegetatedLand(null!));
    }
}
