using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Climate;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public class TemperatureVectorFileTests
{
    /// <summary>
    /// A Python referencia (tools/reference/temperature_ref.py) által
    /// generált vektorok. TOLERANCIA-alapú összehasonlítás, NEM bitpontos -
    /// Math.Sin/Cos/Pow nem garantáltan bitre azonos platformok között
    /// (ND-27, elfogadott kockázat M12-ig).
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceWithinTolerance()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "temperature_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
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
            double expected = v.GetProperty("temperatureK").GetDouble();

            double got = Temperature.TemperatureKelvin(
                x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                isOceanic, elevationM, seaLevelM);

            Assert.True(Math.Abs(got - expected) < 1e-6, $"eltér: {got} vs {expected}");
            checkedCount++;
        }

        Assert.Equal(150, checkedCount);
    }
}

public class TemperaturePlausibilityTests
{
    private const double OrbitalPeriod = 365.25;
    private const double RotationPeriod = 1.0;
    private const double AxialTiltEarthLike = 0.40910517666747087; // 23.44 deg

    [Fact]
    public void EquatorIsWarmerThanPole()
    {
        double equator = Temperature.TemperatureKelvin(
            1.0, 0.0, 0.0, 0.0, OrbitalPeriod, RotationPeriod, AxialTiltEarthLike,
            isOceanic: false, elevationM: 0.0, seaLevelM: 0.0);
        double pole = Temperature.TemperatureKelvin(
            0.0, 0.0, 1.0, 0.0, OrbitalPeriod, RotationPeriod, AxialTiltEarthLike,
            isOceanic: false, elevationM: 0.0, seaLevelM: 0.0);

        Assert.True(equator > pole, $"Egyenlítő ({equator}K) melegebb kell legyen, mint a pólus ({pole}K)");
    }

    [Fact]
    public void HigherElevationIsColder()
    {
        double seaLevelTemp = Temperature.TemperatureKelvin(
            1.0, 0.0, 0.0, 0.0, OrbitalPeriod, RotationPeriod, AxialTiltEarthLike,
            isOceanic: false, elevationM: 0.0, seaLevelM: 0.0);
        double mountainTemp = Temperature.TemperatureKelvin(
            1.0, 0.0, 0.0, 0.0, OrbitalPeriod, RotationPeriod, AxialTiltEarthLike,
            isOceanic: false, elevationM: 5000.0, seaLevelM: 0.0);

        Assert.True(mountainTemp < seaLevelTemp,
            $"Hegy ({mountainTemp}K) hidegebb kell legyen, mint tengerszint ({seaLevelTemp}K)");
    }

    [Fact]
    public void TemperatureDecreasesMonotonicallyWithLatitudeAtEquinox()
    {
        double previous = double.PositiveInfinity;
        for (int latDeg = 0; latDeg <= 90; latDeg += 15)
        {
            double lat = latDeg * Math.PI / 180.0;
            double x = Math.Cos(lat), z = Math.Sin(lat);
            double t = Temperature.TemperatureKelvin(
                x, 0.0, z, 0.0, OrbitalPeriod, RotationPeriod, AxialTiltEarthLike,
                isOceanic: false, elevationM: 0.0, seaLevelM: 0.0);
            Assert.True(t <= previous + 1e-9, $"{latDeg} foknal nott a homerseklet: {t} > {previous}");
            previous = t;
        }
    }
}

public class TemperaturePurityTests
{
    [Fact]
    public void RepeatedCallsAreIdentical()
    {
        double a = Temperature.TemperatureKelvin(
            0.5, 0.5, 0.7071, 42.0, 365.25, 1.0, 0.4, true, -1000.0, 0.0);
        double b = Temperature.TemperatureKelvin(
            0.5, 0.5, 0.7071, 42.0, 365.25, 1.0, 0.4, true, -1000.0, 0.0);
        Assert.Equal(a, b);
    }

    [Fact]
    public void PrecomputedDailySamplesAreBitIdenticalToDirectPath()
    {
        const double dayT = 42.25;
        const double orbitalPeriod = 365.25;
        const double rotationPeriod = 1.0;
        const double axialTilt = 0.40910517666747087;
        DailyInsolationSampleDirections samples = DailyInsolationSampleDirections.Create(
            dayT, orbitalPeriod, rotationPeriod, axialTilt);

        var points = new[]
        {
            (X: 1.0, Y: 0.0, Z: 0.0, Ocean: false, Elevation: 800.0, Sea: 120.0),
            (X: 0.5, Y: 0.5, Z: 0.7071067811865476, Ocean: true, Elevation: -3200.0, Sea: 50.0),
            (X: -0.321, Y: 0.876, Z: -0.359, Ocean: false, Elevation: 4210.0, Sea: -80.0)
        };

        foreach (var point in points)
        {
            double direct = Temperature.TemperatureKelvin(
                point.X, point.Y, point.Z, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                point.Ocean, point.Elevation, point.Sea);
            double cached = Temperature.TemperatureKelvinFromSamples(
                point.X, point.Y, point.Z, in samples,
                point.Ocean, point.Elevation, point.Sea);

            Assert.Equal(BitConverter.DoubleToInt64Bits(direct), BitConverter.DoubleToInt64Bits(cached));
        }
    }
}

public class TemperatureEdgeCaseTests
{
    [Fact]
    public void OceanicAlbedoGivesDifferentResultThanLand()
    {
        double ocean = Temperature.TemperatureKelvin(
            1.0, 0.0, 0.0, 0.0, 365.25, 1.0, 0.4, isOceanic: true, elevationM: 0.0, seaLevelM: 0.0);
        double land = Temperature.TemperatureKelvin(
            1.0, 0.0, 0.0, 0.0, 365.25, 1.0, 0.4, isOceanic: false, elevationM: 0.0, seaLevelM: 0.0);
        Assert.NotEqual(ocean, land);
    }

    [Fact]
    public void ElevationBelowSeaLevelDoesNotIncreaseTemperature()
    {
        // A tengerszint alatti "eleváció" (óceánfenék) nem hűthet/melegíthet
        // a lapse rate-en keresztül - a víz felszíne mindig tengerszinten van.
        double atSeaLevel = Temperature.TemperatureKelvin(
            1.0, 0.0, 0.0, 0.0, 365.25, 1.0, 0.4, isOceanic: true, elevationM: 0.0, seaLevelM: 0.0);
        double belowSeaLevel = Temperature.TemperatureKelvin(
            1.0, 0.0, 0.0, 0.0, 365.25, 1.0, 0.4, isOceanic: true, elevationM: -3000.0, seaLevelM: 0.0);
        Assert.Equal(atSeaLevel, belowSeaLevel);
    }

    [Fact]
    public void PermanentPolarNightAtEquinoxGivesNonNegativeAbsoluteTemperature()
    {
        // A pólus napejegyenlosegkor majdnem sotetben van (hataresetben) -
        // a homerseklet Kelvin-ben sose lehet negativ.
        double t = Temperature.TemperatureKelvin(
            0.0, 0.0, 1.0, 0.0, 365.25, 1.0, 0.40910517666747087,
            isOceanic: false, elevationM: 0.0, seaLevelM: 0.0);
        Assert.True(t >= 0.0, $"A hőmérséklet nem lehet negatív Kelvin: {t}");
    }
}
