using System;
using System.Collections.Generic;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// A `TEST-EARTH-001` közvetlen kódbeli kifejezése (docs/05-milestones.md
/// M4 sora): 50-75% víz, legalább 2 kontinens. A referencia (tools/reference/
/// sea_level_ref.py) ugyanezekkel a paraméterekkel (world_seed, plateCount=20,
/// level=6) mérve pontosan 65.00% vizet és 2 kontinenst adott (7346 + 1221 tile).
/// </summary>
public class TestEarth001Tests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;

    [Fact]
    public void WaterFractionIsBetween50And75Percent()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);

        int water = 0;
        foreach (double e in field.Values)
            if (e < seaLevel) water++;
        double waterFraction = water / (double)field.Count;

        Assert.InRange(waterFraction, 0.50, 0.75);
    }

    [Fact]
    public void HasAtLeastTwoContinents()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var continents = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 5);

        Assert.True(continents.Count >= 2, $"Legalább 2 kontinens várt, kaptunk: {continents.Count}");
    }

    /// <summary>Egzakt egyezés a Python referenciával mért kontinens-méretekkel.</summary>
    [Fact]
    public void ContinentSizesMatchPythonReferenceExactly()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var continents = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 5);

        var sizes = new List<int>();
        foreach (var c in continents)
            sizes.Add(c.Count);
        sizes.Sort((a, b) => b.CompareTo(a));

        Assert.Equal(new List<int> { 7346, 1221 }, sizes);
    }
}

public class SeaLevelCalibrationUnitTests
{
    [Fact]
    public void CalibrateSeaLevelPicksExpectedPercentile()
    {
        var elevations = new List<double> { 5.0, 1.0, 3.0, 2.0, 4.0 }; // sorolatlan, [1,2,3,4,5]-re rendezodik
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(elevations, targetWaterFraction: 0.5);
        // idx = (int)(0.5*5) = 2 -> a rendezett tomb 3. eleme (0-indexeles): 3.0
        Assert.Equal(3.0, seaLevel);
    }

    [Fact]
    public void CalibrateSeaLevelAtZeroPicksMinimum()
    {
        var elevations = new List<double> { 5.0, 1.0, 3.0 };
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(elevations, targetWaterFraction: 0.0);
        Assert.Equal(1.0, seaLevel);
    }

    [Fact]
    public void CalibrateSeaLevelIsPure()
    {
        var elevations = new List<double> { 5.0, 1.0, 3.0, 2.0, 4.0 };
        double a = SeaLevelCalibration.CalibrateSeaLevel(elevations, 0.3);
        double b = SeaLevelCalibration.CalibrateSeaLevel(elevations, 0.3);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ThrowsOnEmptyElevations()
    {
        Assert.Throws<ArgumentException>(() =>
            SeaLevelCalibration.CalibrateSeaLevel(new List<double>(), 0.5));
    }

    [Fact]
    public void CountContinentsOnSmallGridIsDeterministic()
    {
        // Kisebb, olcsobb racs (level 3, 384 tile) a tisztasag/determinizmus
        // ellenorzesehez - nem kell a teljes level 6 minden edge case tesztnel.
        var field = SeaLevelCalibration.ComputeElevationField(1UL, plateCount: 6, level: 3);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.5);

        var a = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 1);
        var b = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 1);

        Assert.Equal(a.Count, b.Count);
    }

    [Fact]
    public void MinSizeFilterRemovesSmallComponents()
    {
        var field = SeaLevelCalibration.ComputeElevationField(1UL, plateCount: 6, level: 3);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.5);

        var unfiltered = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 1);
        var filtered = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 1000);

        Assert.True(filtered.Count <= unfiltered.Count);
    }
}
