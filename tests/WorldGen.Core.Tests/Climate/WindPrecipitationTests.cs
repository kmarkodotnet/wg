using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Climate;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

/// <summary>
/// M5 szel/nedvesseg/csapadek/idojaras (ND-41) - a Python referencia
/// (wind_precipitation_ref.py) 3x300 vektora. TOLERANCIA (1e-6): a modul nyers
/// Math.Sin/Cos/Asin/Atan2/Tanh-ot hasznal (mint a Temperature-lanc), tehat
/// ULP-szintu elteres a Python math-hoz kepest (ld. WindPrecipitation osztaly-doc).
/// </summary>
public class WindPrecipitationVectorFileTests
{
    private const double Tol = 1e-6;

    private static JsonDocument Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "wind_precipitation_vectors.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void WindVectorsMatchPythonReference()
    {
        using JsonDocument doc = Load();
        int n = 0;
        foreach (JsonElement v in doc.RootElement.GetProperty("windVectors").EnumerateArray())
        {
            WindPrecipitation.WindVector(
                v.GetProperty("x").GetDouble(), v.GetProperty("y").GetDouble(), v.GetProperty("z").GetDouble(),
                v.GetProperty("dayT").GetDouble(), v.GetProperty("orbitalPeriod").GetDouble(),
                v.GetProperty("rotationPeriod").GetDouble(), v.GetProperty("axialTilt").GetDouble(),
                v.GetProperty("isOceanic").GetBoolean(), v.GetProperty("elevationM").GetDouble(), v.GetProperty("seaLevelM").GetDouble(),
                v.GetProperty("elevationGradientEast").GetDouble(), v.GetProperty("elevationGradientNorth").GetDouble(),
                out double we, out double wn, out double w3x, out double w3y, out double w3z);

            Assert.True(Math.Abs(we - v.GetProperty("windEast").GetDouble()) < Tol);
            Assert.True(Math.Abs(wn - v.GetProperty("windNorth").GetDouble()) < Tol);
            JsonElement w3 = v.GetProperty("wind3d");
            Assert.True(Math.Abs(w3x - w3[0].GetDouble()) < Tol);
            Assert.True(Math.Abs(w3y - w3[1].GetDouble()) < Tol);
            Assert.True(Math.Abs(w3z - w3[2].GetDouble()) < Tol);
            n++;
        }
        Assert.Equal(300, n);
    }

    [Fact]
    public void EvaporationPrecipitationMatchPythonReference()
    {
        using JsonDocument doc = Load();
        int n = 0;
        foreach (JsonElement v in doc.RootElement.GetProperty("evaporationPrecipitationVectors").EnumerateArray())
        {
            double evap = WindPrecipitation.Evaporation(
                v.GetProperty("temperatureK").GetDouble(), v.GetProperty("windSpeed").GetDouble(),
                v.GetProperty("surfaceWaterFraction").GetDouble());
            double precip = WindPrecipitation.Precipitation(
                evap, v.GetProperty("incomingMoisture").GetDouble(),
                v.GetProperty("windEast").GetDouble(), v.GetProperty("windNorth").GetDouble(),
                v.GetProperty("elevationGradientEast").GetDouble(), v.GetProperty("elevationGradientNorth").GetDouble());

            Assert.True(Math.Abs(evap - v.GetProperty("evaporation").GetDouble()) < Tol);
            Assert.True(Math.Abs(precip - v.GetProperty("precipitation").GetDouble()) < Tol);
            n++;
        }
        Assert.Equal(300, n);
    }

    [Fact]
    public void WeatherNoiseMatchesPythonReference()
    {
        using JsonDocument doc = Load();
        ulong worldSeed = doc.RootElement.GetProperty("worldSeed").GetUInt64();
        int n = 0;
        foreach (JsonElement v in doc.RootElement.GetProperty("weatherVectors").EnumerateArray())
        {
            double x = v.GetProperty("x").GetDouble(), y = v.GetProperty("y").GetDouble(), z = v.GetProperty("z").GetDouble();
            double t = v.GetProperty("t").GetDouble();
            double climateMeanC = v.GetProperty("climateMeanTemperatureC").GetDouble();

            double dev = WindPrecipitation.WeatherTemperatureDeviationK(worldSeed, x, y, z, t);
            double cur = WindPrecipitation.CurrentTemperatureK(climateMeanC, worldSeed, x, y, z, t);
            double mult = WindPrecipitation.WeatherPrecipitationMultiplier(worldSeed, x, y, z, t);

            Assert.True(Math.Abs(dev - v.GetProperty("weatherDeviationK").GetDouble()) < Tol);
            Assert.True(Math.Abs(cur - v.GetProperty("currentTemperatureC").GetDouble()) < Tol);
            Assert.True(Math.Abs(mult - v.GetProperty("precipitationMultiplier").GetDouble()) < Tol);
            n++;
        }
        Assert.Equal(300, n);
    }
}

public class WindPrecipitationPropertyTests
{
    [Fact]
    public void ZonalBandIndexHasExpectedSigns()
    {
        double D2R = Math.PI / 180.0;
        Assert.True(Math.Abs(WindPrecipitation.ZonalBandIndex(0 * D2R) - 0.0) < 1e-9);
        Assert.True(WindPrecipitation.ZonalBandIndex(15 * D2R) < 0);   // Hadley: keleti
        Assert.True(WindPrecipitation.ZonalBandIndex(45 * D2R) > 0);   // Ferrel: nyugati
        Assert.True(WindPrecipitation.ZonalBandIndex(75 * D2R) < 0);   // Polar: keleti
    }

    [Fact]
    public void EvaporationMonotoneAndZeroBelowFreezing()
    {
        Assert.True(WindPrecipitation.Evaporation(305, 5, 1) > WindPrecipitation.Evaporation(260, 5, 1));
        Assert.True(WindPrecipitation.Evaporation(305, 20, 1) > WindPrecipitation.Evaporation(305, 0, 1));
        Assert.True(WindPrecipitation.Evaporation(305, 5, 1) > WindPrecipitation.Evaporation(305, 5, 0));
        Assert.Equal(0.0, WindPrecipitation.Evaporation(WindPrecipitation.EvapFreezeK - 10, 5, 1));
    }

    [Fact]
    public void OrographicWindwardWetterThanLeeward()
    {
        double windward = WindPrecipitation.Precipitation(2, 3, 1, 0, 1, 0);
        double flat = WindPrecipitation.Precipitation(2, 3, 1, 0, 0, 0);
        double leeward = WindPrecipitation.Precipitation(2, 3, 1, 0, -1, 0);
        Assert.True(windward > flat && flat > leeward);
        Assert.True(leeward >= 0.0);
    }

    [Fact]
    public void WeatherDeviationStrictlyBounded()
    {
        const ulong seed = 0xA7C944210000UL;
        double lat = 48 * Math.PI / 180, lon = 19 * Math.PI / 180;
        double px = Math.Cos(lat) * Math.Cos(lon), py = Math.Cos(lat) * Math.Sin(lon), pz = Math.Sin(lat);
        for (int i = 0; i < 2000; i++)
        {
            double t = i * 3.7;
            double dev = WindPrecipitation.WeatherTemperatureDeviationK(seed, px, py, pz, t);
            Assert.True(Math.Abs(dev) < WindPrecipitation.MaxWeatherTempDeviationK);
        }
    }
}
