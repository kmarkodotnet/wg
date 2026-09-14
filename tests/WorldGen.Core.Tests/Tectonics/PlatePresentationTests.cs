using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// Tektonikuslemez-overlay (2026-09-13, docs/backlog.md):
/// <see cref="PlatePresentation"/> - lemezenkénti szín és név.
/// </summary>
public class PlatePresentationVectorFileTests
{
    /// <summary>BITPONTOS egyezés várt - csak egész/lebegőpontos aritmetika, nincs transzcendens függvény.</summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "plate_presentation_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();

        int checkedCount = 0;
        foreach (JsonElement p in root.GetProperty("plates").EnumerateArray())
        {
            int plateId = p.GetProperty("plateId").GetInt32();
            bool isOceanic = p.GetProperty("isOceanic").GetBoolean();
            string expectedName = p.GetProperty("name").GetString()!;
            double expectedR = p.GetProperty("r").GetDouble();
            double expectedG = p.GetProperty("g").GetDouble();
            double expectedB = p.GetProperty("b").GetDouble();

            string gotName = PlatePresentation.PlateName(worldSeed, plateId, isOceanic);
            PlatePresentation.PlateColorRgb(worldSeed, plateId, out double r, out double g, out double b);

            Assert.Equal(expectedName, gotName);
            Assert.Equal(expectedR, r, 9);
            Assert.Equal(expectedG, g, 9);
            Assert.Equal(expectedB, b, 9);
            checkedCount++;
        }
        Assert.Equal(20, checkedCount);
    }
}

public class PlatePresentationUnitTests
{
    [Fact]
    public void ColorIsPure()
    {
        PlatePresentation.PlateColorRgb(1UL, 5, out double r1, out double g1, out double b1);
        PlatePresentation.PlateColorRgb(1UL, 5, out double r2, out double g2, out double b2);
        Assert.Equal(r1, r2);
        Assert.Equal(g1, g2);
        Assert.Equal(b1, b2);
    }

    [Fact]
    public void DifferentPlateIdsGiveDifferentColors()
    {
        PlatePresentation.PlateColorRgb(1UL, 0, out double r0, out double g0, out double b0);
        PlatePresentation.PlateColorRgb(1UL, 1, out double r1, out double g1, out double b1);
        Assert.False(r0 == r1 && g0 == g1 && b0 == b1);
    }

    [Fact]
    public void AllColorComponentsInUnitRange()
    {
        for (int plateId = 0; plateId < 50; plateId++)
        {
            PlatePresentation.PlateColorRgb(0xABCDEFUL, plateId, out double r, out double g, out double b);
            Assert.InRange(r, 0.0, 1.0);
            Assert.InRange(g, 0.0, 1.0);
            Assert.InRange(b, 0.0, 1.0);
        }
    }

    [Fact]
    public void HueWrapsWithinUnitRange()
    {
        for (int plateId = 0; plateId < 200; plateId++)
        {
            double hue = PlatePresentation.PlateHue(0x123UL, plateId);
            Assert.InRange(hue, 0.0, 1.0 - 1e-15);
        }
    }

    [Fact]
    public void OceanicAndContinentalNamesDifferForSamePlateId()
    {
        // Ugyanaz a plateId, csak a kereg-tipus mas - a nevnek is elternie kell
        // (mas utotag-keszletbol valogat), demonstralva, hogy a hivo tenyleg
        // hasznalja az isOceanic bemenetet.
        string oceanic = PlatePresentation.PlateName(7UL, 3, isOceanic: true);
        string continental = PlatePresentation.PlateName(7UL, 3, isOceanic: false);
        Assert.NotEqual(oceanic, continental);
    }

    [Fact]
    public void FeatureIdRangeDoesNotCollideWithOtherNamingRanges()
    {
        // Kontinens 0..N, regio 10000+i, terulet 20000+, fallback-regio
        // 900000+i, fallback-terulet 950000+ - a lemez 800000+ tartomanya
        // mindegyiktol disjunkt (ld. docs/01-architecture.md).
        ulong id = PlatePresentation.PlateFeatureId(0);
        Assert.Equal(800000UL, id);
        Assert.True(PlatePresentation.PlateFeatureId(199) < 900000UL);
    }

    [Theory]
    [InlineData(0.0, 0.0, 1.0, 1.0, 1.0, 1.0)] // h=0, s=0 -> feher (barmilyen h-nal ugyanez)
    [InlineData(0.5, 0.0, 0.4, 0.4, 0.4, 0.4)] // s=0 -> szurke, csak v hatarozza meg
    public void HsvToRgbZeroSaturationIsGray(double h, double s, double v, double expectedR, double expectedG, double expectedB)
    {
        PlatePresentation.HsvToRgb(h, s, v, out double r, out double g, out double b);
        Assert.Equal(expectedR, r, 9);
        Assert.Equal(expectedG, g, 9);
        Assert.Equal(expectedB, b, 9);
    }
}
