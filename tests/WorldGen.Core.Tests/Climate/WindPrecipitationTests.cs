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

    /// <summary>
    /// A <see cref="WindPrecipitation.WindVectorFromTemperatureGradient"/> a
    /// WindVector kozos, kiemelt masodik fele - BITRE azonos eredmenyt kell
    /// adnia, ha ugyanazt a homerseklet-gradienst kapja, amit a WindVector
    /// belul maga szamolna. Ez az invariansa annak, hogy a kiemelés NEM
    /// seed-toro (a megjelenito azert hasznalja, hogy a dragabb belso
    /// gradiens-szamitast egy olcsobb, sajat forrasu gradienssel valtsa ki).
    /// </summary>
    [Fact]
    public void WindVectorFromTemperatureGradientIsBitIdenticalToWindVector()
    {
        const double orbitalPeriod = 365.25, rotationPeriod = 1.0;
        double axialTilt = 23.44 * Math.PI / 180.0;
        // NEM System.Random (CLAUDE.md tiltja): egy sajat, determinisztikus
        // LCG, ami minden platformon/futasnal UGYANAZT a mintahalmazt adja.
        ulong state = 20260920UL;
        Func<double> next = () =>
        {
            state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
            return (state >> 11) * (1.0 / 9007199254740992.0);
        };
        int checkedPoints = 0;

        for (int i = 0; i < 500; i++)
        {
            double lat = Math.Asin(2.0 * next() - 1.0);
            double lon = 2.0 * Math.PI * next();
            double x = Math.Cos(lat) * Math.Cos(lon);
            double y = Math.Cos(lat) * Math.Sin(lon);
            double z = Math.Sin(lat);
            double dayT = 500.0 * next();
            bool isOceanic = next() < 0.5;
            double elevation = -4000.0 + 12000.0 * next();
            double seaLevel = 1500.0;
            // Elesetek is: nulla es nagyon meredek elevacio-gradiens.
            double gradE = i % 5 == 0 ? 0.0 : -5e5 + 1e6 * next();
            double gradN = i % 7 == 0 ? 0.0 : -5e5 + 1e6 * next();

            WindPrecipitation.WindVector(
                x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                isOceanic, elevation, seaLevel, gradE, gradN,
                out double we, out double wn, out double w3x, out double w3y, out double w3z);

            WindPrecipitation.TemperatureGradientTangent(
                x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                isOceanic, elevation, seaLevel, out double tge, out double tgn);

            WindPrecipitation.WindVectorFromTemperatureGradient(
                x, y, z, tge, tgn, gradE, gradN,
                out double we2, out double wn2, out double w3x2, out double w3y2, out double w3z2);

            Assert.Equal(we, we2);
            Assert.Equal(wn, wn2);
            Assert.Equal(w3x, w3x2);
            Assert.Equal(w3y, w3y2);
            Assert.Equal(w3z, w3z2);
            checkedPoints++;
        }

        Assert.Equal(500, checkedPoints);
    }

    /// <summary>
    /// A homerseklet-gradiens ERDEMBEN hat a kimenetre - kulonben az elozo
    /// teszt akkor is zold lenne, ha a gradienst valahol elejtenenk.
    /// </summary>
    [Fact]
    public void TemperatureGradientActuallyChangesTheWind()
    {
        double x = 0.6, y = 0.5, z = 0.62449979983983983;
        double len = Math.Sqrt(x * x + y * y + z * z);
        x /= len; y /= len; z /= len;

        WindPrecipitation.WindVectorFromTemperatureGradient(
            x, y, z, 0.0, 0.0, 0.0, 0.0,
            out double we0, out double wn0, out _, out _, out _);
        WindPrecipitation.WindVectorFromTemperatureGradient(
            x, y, z, 50.0, -30.0, 0.0, 0.0,
            out double we1, out double wn1, out _, out _, out _);

        Assert.True(Math.Abs(we1 - we0) > 1e-9 || Math.Abs(wn1 - wn0) > 1e-9);
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
