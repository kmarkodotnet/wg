using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Climate;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

/// <summary>
/// M5 "Teljes homerseklet-modell" (ND-42, spec §28.1) - a Python referencia
/// (tools/reference/temperature_ref.py, temperature_kelvin_full) altal generalt
/// 200 vektor. BITPONTOS egyezes vart (a greenhouse/cycle uj agak
/// DeterministicMath.Ln/SinCos-t, a weather a mar verifikalt FractalNoise.Fbm-et
/// hasznaljak - mindegyik a Python orakulummal azonos sajat implementacio).
/// A regi TemperatureKelvin (temperature_vectors.json) valtozatlan - ez KULON
/// reteg (ld. Temperature osztaly-doc).
/// </summary>
public class TemperatureFullVectorFileTests
{
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "temperature_full_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

        int checkedCount = 0;
        foreach (JsonElement v in doc.RootElement.GetProperty("vectors").EnumerateArray())
        {
            ulong worldSeed = v.GetProperty("worldSeed").GetUInt64();
            double x = v.GetProperty("x").GetDouble();
            double y = v.GetProperty("y").GetDouble();
            double z = v.GetProperty("z").GetDouble();
            double dayT = v.GetProperty("dayT").GetDouble();
            double orbitalPeriod = v.GetProperty("orbitalPeriod").GetDouble();
            double rotationPeriod = v.GetProperty("rotationPeriod").GetDouble();
            double axialTilt = v.GetProperty("axialTilt").GetDouble();
            bool isOceanic = v.GetProperty("isOceanic").GetBoolean();
            double elevationM = v.GetProperty("elevationM").GetDouble();
            double seaLevelM = v.GetProperty("seaLevelM").GetDouble();
            double ghgPpm = v.GetProperty("ghgPpm").GetDouble();
            double greenhouseK = v.GetProperty("greenhouseK").GetDouble();
            double oceanBufferingStrength = v.GetProperty("oceanBufferingStrength").GetDouble();
            double weatherAmplitudeK = v.GetProperty("weatherAmplitudeK").GetDouble();
            double tYears = v.GetProperty("tYears").GetDouble();
            double cycleEcc = v.GetProperty("cycleEccentricityAmplitudeK").GetDouble();
            double cycleObl = v.GetProperty("cycleObliquityAmplitudeK").GetDouble();
            double cyclePrec = v.GetProperty("cyclePrecessionAmplitudeK").GetDouble();
            double expected = v.GetProperty("temperatureK").GetDouble();

            double got = Temperature.TemperatureKelvinFull(
                worldSeed, x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                isOceanic, elevationM, seaLevelM,
                greenhouseK: greenhouseK, ghgPpm: ghgPpm,
                oceanBufferingStrength: oceanBufferingStrength, weatherAmplitudeK: weatherAmplitudeK,
                tYears: tYears,
                cycleEccentricityAmplitudeK: cycleEcc, cycleObliquityAmplitudeK: cycleObl,
                cyclePrecessionAmplitudeK: cyclePrec);

            // Tolerancia (mint a meglevo TemperatureTests): a radiativ tag
            // sqrt(sqrt) (determinizmus-biztos, ND-23) vs a Python `**0.25` (pow)
            // ~1 ULP-nyi elteres. Az UJ agak (greenhouse/cycle/weather) bitpontosak
            // (DeterministicMath/FractalNoise), tehat a teljes elteres jol 1e-6 alatt.
            Assert.True(Math.Abs(got - expected) < 1e-6, $"elter: {got} vs {expected}");
            checkedCount++;
        }

        Assert.Equal(200, checkedCount);
    }
}

public class TemperatureFullPropertyTests
{
    private const double OrbitalPeriod = 365.25;
    private const double RotationPeriod = 1.0;
    private static readonly double AxialTilt = 23.44 * Math.PI / 180.0;
    private const ulong Seed = 0xA7C944210000UL;

    [Fact]
    public void GreenhouseAtReferenceIsExactlyBaseValue()
    {
        Assert.Equal(Temperature.DefaultGreenhouseK,
            Temperature.GreenhouseTemperature(Temperature.EarthGhgReferencePpm));
    }

    [Fact]
    public void GreenhouseDoublingAddsSensitivity()
    {
        double g = Temperature.GreenhouseTemperature(Temperature.EarthGhgReferencePpm * 2.0);
        Assert.True(Math.Abs(g - (Temperature.DefaultGreenhouseK + Temperature.GreenhouseSensitivityKPerDoubling)) < 1e-9);
    }

    [Fact]
    public void RepeatedCallsAreIdentical()
    {
        double a = Temperature.TemperatureKelvinFull(Seed, 0.5, 0.5, 0.7071, 42.0, OrbitalPeriod, RotationPeriod, AxialTilt, true, -1000.0, 0.0, tYears: 5000.0);
        double b = Temperature.TemperatureKelvinFull(Seed, 0.5, 0.5, 0.7071, 42.0, OrbitalPeriod, RotationPeriod, AxialTilt, true, -1000.0, 0.0, tYears: 5000.0);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ClimateCycleIsBoundedAndSeedDependent()
    {
        double c1 = Temperature.ClimateCycleTemperatureK(Seed, 12345.0);
        double c2 = Temperature.ClimateCycleTemperatureK(Seed, 12345.0);
        Assert.Equal(c1, c2);
        Assert.NotEqual(c1, Temperature.ClimateCycleTemperatureK(Seed + 1, 12345.0));
        double maxPossible = Temperature.CycleEccentricityAmplitudeK + Temperature.CycleObliquityAmplitudeK + Temperature.CyclePrecessionAmplitudeK;
        Assert.True(Math.Abs(c1) <= maxPossible + 1e-9);
    }

    [Fact]
    public void OceanBuffersSeasonalSwingBelowLand()
    {
        double latMid = 45.0 * Math.PI / 180.0;
        double px = Math.Cos(latMid), pz = Math.Sin(latMid);
        double landSumSq = 0, landSum = 0, oceanSumSq = 0, oceanSum = 0;
        const int n = 12;
        for (int k = 0; k < n; k++)
        {
            double dayT = k * (OrbitalPeriod / n);
            double land = Temperature.TemperatureKelvinFull(Seed, px, 0, pz, dayT, OrbitalPeriod, RotationPeriod, AxialTilt, false, 0, 0, weatherAmplitudeK: 0, cycleEccentricityAmplitudeK: 0, cycleObliquityAmplitudeK: 0, cyclePrecessionAmplitudeK: 0);
            double ocean = Temperature.TemperatureKelvinFull(Seed, px, 0, pz, dayT, OrbitalPeriod, RotationPeriod, AxialTilt, true, 0, 0, weatherAmplitudeK: 0, cycleEccentricityAmplitudeK: 0, cycleObliquityAmplitudeK: 0, cyclePrecessionAmplitudeK: 0);
            landSum += land; landSumSq += land * land;
            oceanSum += ocean; oceanSumSq += ocean * ocean;
        }
        double landVar = landSumSq / n - (landSum / n) * (landSum / n);
        double oceanVar = oceanSumSq / n - (oceanSum / n) * (oceanSum / n);
        Assert.True(oceanVar < landVar, $"Az ocean-buffering kisebb evszakos ingast var: land={landVar}, ocean={oceanVar}");
    }
}
