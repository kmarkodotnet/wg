using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public class ClimateCalibrationTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;
    private const double OrbitalPeriodDays = 365.25;
    private const double RotationPeriodDays = 1.0;
    private static readonly double AxialTiltRad = 23.44 * Math.PI / 180.0;

    [Fact]
    public void CanonicalWorldHasCalibratedColdLandShareAndLatitudeGradient()
    {
        Dictionary<TileId, double> elevation =
            SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(elevation.Values, TargetWaterFraction);
        DailyInsolationSampleDirections samples = DailyInsolationSampleDirections.Create(
            0.0, OrbitalPeriodDays, RotationPeriodDays, AxialTiltRad);

        int landCount = 0;
        int coldLandCount = 0;
        double[] bandSum = new double[5];
        int[] bandCount = new int[5];

        foreach (KeyValuePair<TileId, double> sample in elevation)
        {
            if (sample.Value < seaLevel)
                continue;

            TileGeometry.ToPosition(sample.Key, out double x, out double y, out double z);
            double temperatureK = Temperature.TemperatureKelvinFromSamples(
                x, y, z, in samples, false, sample.Value, seaLevel);
            landCount++;
            if (temperatureK < BiomeClassification.TundraThresholdK)
                coldLandCount++;

            int band = LatitudeBand(z);
            bandSum[band] += temperatureK - 273.15;
            bandCount[band]++;
        }

        double coldShare = coldLandCount / (double)landCount;
        Assert.InRange(coldShare, 0.16, 0.20);

        double polarMeanC = bandSum[0] / bandCount[0];
        double equatorialMeanC = bandSum[4] / bandCount[4];
        Assert.InRange(polarMeanC, -30.0, -20.0);
        Assert.InRange(equatorialMeanC, 25.0, 30.0);
        Assert.True(equatorialMeanC - polarMeanC < 60.0);
    }

    [Fact]
    public void CanonicalWorldThermalWindStaysInSurfaceWindRange()
    {
        Dictionary<TileId, double> elevation =
            SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(elevation.Values, TargetWaterFraction);
        double[] bandSpeedSum = new double[5];
        int[] bandCount = new int[5];
        double maximumSpeed = 0.0;

        foreach (KeyValuePair<TileId, double> sample in elevation)
        {
            TileGeometry.ToPosition(sample.Key, out double x, out double y, out double z);
            bool isOceanic = sample.Value < seaLevel;
            WindPrecipitation.WindVector(
                x, y, z, 0.0, OrbitalPeriodDays, RotationPeriodDays, AxialTiltRad,
                isOceanic, sample.Value, seaLevel, 0.0, 0.0,
                out double windEast, out double windNorth, out _, out _, out _);
            double speed = Math.Sqrt(windEast * windEast + windNorth * windNorth);
            int band = LatitudeBand(z);
            bandSpeedSum[band] += speed;
            bandCount[band]++;
            maximumSpeed = Math.Max(maximumSpeed, speed);
        }

        double polarMean = bandSpeedSum[0] / bandCount[0];
        double equatorialMean = bandSpeedSum[4] / bandCount[4];
        Assert.InRange(maximumSpeed, 30.0, 40.0);
        Assert.InRange(polarMean, 30.0, 38.0);
        Assert.InRange(equatorialMean, 5.0, 10.0);
    }

    [Fact]
    public void ThermalWindLimitIsSmoothBoundedAndDirectionPreserving()
    {
        WindPrecipitation.LimitThermalWind(300.0, 400.0, out double east, out double north);
        double magnitude = Math.Sqrt(east * east + north * north);

        Assert.True(magnitude < WindPrecipitation.ThermalWindLimit);
        Assert.Equal(0.75, east / north, 12);

        WindPrecipitation.LimitThermalWind(1.0e-15, -1.0e-15, out east, out north);
        Assert.Equal(1.0e-15, east);
        Assert.Equal(-1.0e-15, north);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindPrecipitation.LimitThermalWind(1.0, 0.0, out _, out _, 0.0));
    }

    [Fact]
    public void MeridionalHeatTransportIsSymmetricAndDoesNotWarmTheEquator()
    {
        Assert.Equal(0.0, Temperature.MeridionalHeatTransportK(0.0));
        Assert.Equal(Temperature.MeridionalHeatTransportMaxK,
            Temperature.MeridionalHeatTransportK(1.0));
        Assert.Equal(Temperature.MeridionalHeatTransportK(0.73),
            Temperature.MeridionalHeatTransportK(-0.73));
    }

    private static int LatitudeBand(double z)
    {
        double absoluteLatitudeDegrees = Math.Abs(Math.Asin(z) * 180.0 / Math.PI);
        if (absoluteLatitudeDegrees >= 70.0) return 0;
        if (absoluteLatitudeDegrees >= 50.0) return 1;
        if (absoluteLatitudeDegrees >= 30.0) return 2;
        if (absoluteLatitudeDegrees >= 10.0) return 3;
        return 4;
    }
}
