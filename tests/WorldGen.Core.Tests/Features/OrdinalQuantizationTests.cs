using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Features;

public class OrdinalQuantizationUnitTests
{
    private static readonly double[] Thresholds = { 1.0, 2.0, 3.0, 4.0 };

    [Theory]
    [InlineData(-100.0, OrdinalLevel.Low)]
    [InlineData(0.5, OrdinalLevel.Low)]
    [InlineData(1.5, OrdinalLevel.Moderate)]
    [InlineData(2.5, OrdinalLevel.High)]
    [InlineData(3.5, OrdinalLevel.VeryHigh)]
    [InlineData(4.5, OrdinalLevel.Exceptional)]
    [InlineData(1000.0, OrdinalLevel.Exceptional)]
    public void QuantizeMapsValueToExpectedBand(double value, OrdinalLevel expected)
    {
        Assert.Equal(expected, OrdinalQuantization.Quantize(value, Thresholds));
    }

    [Theory]
    [InlineData(1.0, OrdinalLevel.Moderate)] // pontosan a küszöbön -> a KÖVETKEZŐ sávba esik (nem "<=")
    [InlineData(2.0, OrdinalLevel.High)]
    [InlineData(3.0, OrdinalLevel.VeryHigh)]
    [InlineData(4.0, OrdinalLevel.Exceptional)]
    public void QuantizeIsHalfOpenAtThresholds(double value, OrdinalLevel expected)
    {
        Assert.Equal(expected, OrdinalQuantization.Quantize(value, Thresholds));
    }

    [Fact]
    public void QuantizeThrowsOnWrongThresholdCount()
    {
        Assert.Throws<ArgumentException>(() => OrdinalQuantization.Quantize(1.0, new[] { 1.0, 2.0 }));
    }

    [Fact]
    public void QuantizeThrowsOnNullThresholds()
    {
        Assert.Throws<ArgumentException>(() => OrdinalQuantization.Quantize(1.0, null!));
    }

    [Fact]
    public void QuantizeIsPure()
    {
        OrdinalLevel a = OrdinalQuantization.Quantize(2.7, Thresholds);
        OrdinalLevel b = OrdinalQuantization.Quantize(2.7, Thresholds);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(OrdinalLevel.Low, "Low")]
    [InlineData(OrdinalLevel.Moderate, "Moderate")]
    [InlineData(OrdinalLevel.High, "High")]
    [InlineData(OrdinalLevel.VeryHigh, "Very High")]
    [InlineData(OrdinalLevel.Exceptional, "Exceptional")]
    public void LevelNameMatchesExpectedDisplayString(OrdinalLevel level, string expected)
    {
        Assert.Equal(expected, OrdinalQuantization.LevelName(level));
    }

    [Fact]
    public void CalibratedHabitabilityThresholdsAreStrictlyIncreasing()
    {
        AssertStrictlyIncreasing(OrdinalQuantization.HabitabilityThresholds);
    }

    [Fact]
    public void CalibratedCoastalComplexityThresholdsAreStrictlyIncreasing()
    {
        AssertStrictlyIncreasing(OrdinalQuantization.CoastalComplexityThresholds);
    }

    private static void AssertStrictlyIncreasing(double[] thresholds)
    {
        Assert.Equal(4, thresholds.Length);
        for (int i = 1; i < thresholds.Length; i++)
            Assert.True(thresholds[i] > thresholds[i - 1],
                $"A kalibrált küszöbök nem monoton növekvők: [{i - 1}]={thresholds[i - 1]}, [{i}]={thresholds[i]}");
    }
}

/// <summary>
/// Végponttól-végpontig teszt: egy VALÓS (a kalibrációs mintától FÜGGETLEN,
/// a TEST-EARTH-001 seed-jét használó) világ Habitability-értéke a kalibrált
/// küszöbökkel egy konkrét, determinisztikus sávba esik - regresszió-teszt,
/// ami elkapja, ha a HabitabilityFraction képlete vagy a kalibráció
/// szétcsúszik egymástól.
/// </summary>
public class OrdinalQuantizationIntegrationTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;

    [Fact]
    public void TestEarthWorldHabitabilityQuantizesToAValidBand()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        Dictionary<TileId, bool> isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);

        double axialTiltRad = 23.44 * Math.PI / 180.0;
        var temperatureK = new Dictionary<TileId, double>(field.Count);
        foreach (KeyValuePair<TileId, double> kv in field)
        {
            TileGeometry.ToPosition(kv.Key, out double x, out double y, out double z);
            temperatureK[kv.Key] = Temperature.TemperatureKelvin(
                x, y, z, 0.0, 365.25, 1.0, axialTiltRad, isOcean[kv.Key], kv.Value, seaLevel);
        }

        double habitability = FeatureMetrics.HabitabilityFraction(field.Keys, temperatureK, isOcean);
        OrdinalLevel band = OrdinalQuantization.Quantize(habitability, OrdinalQuantization.HabitabilityThresholds);

        Assert.InRange((int)band, (int)OrdinalLevel.Low, (int)OrdinalLevel.Exceptional);
        // Rögzített regresszió-érték - ha ez megváltozik, vagy a HabitabilityFraction
        // képlete, vagy a kalibráció csúszott el egymáshoz képest. ND-52
        // (2026-09-07, másodlagos zaj amplitúdó 200->900m) újra
        // megváltoztatta a referencia-világ elevációját, ezzel a
        // habitability-sávot is Moderate->Low-ra tolta - Python
        // referenciával összhangban frissítve.
        Assert.Equal(OrdinalLevel.Low, band);
    }
}
