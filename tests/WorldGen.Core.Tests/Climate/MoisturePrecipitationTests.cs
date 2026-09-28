using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

/// <summary>
/// Nedvesség-transzport → csapadék (M5). A Python-referencia
/// (moisture_transport_ref.py) 140 mintavett tile csapadék-értéke. A lánc a
/// WindVector-on át nyers Math.Sin/Cos-t használ (mint a teljes wind/temperature
/// lánc), és 24 advekciós iteráción halmozódik, ezért TOLERANCIÁVAL mérünk
/// (mint a TemperatureKelvin/WindPrecipitation), nem bit-egzaktul.
/// </summary>
public class MoisturePrecipitationVectorFileTests
{
    [Fact]
    public void PrecipitationMatchesPythonReferenceWithinTolerance()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "moisture_transport_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement p = doc.RootElement.GetProperty("params");

        ulong worldSeed = p.GetProperty("worldSeed").GetUInt64();
        int plateCount = p.GetProperty("plateCount").GetInt32();
        int level = p.GetProperty("level").GetInt32();

        MoisturePrecipitation.PrecipitationField result =
            MoisturePrecipitation.Compute(worldSeed, plateCount, level);

        // A tengerszint a mezőből EGZAKT (percentilis, csak +-*/ ) -> egyezzen.
        Assert.Equal(p.GetProperty("seaLevel").GetDouble(), result.SeaLevel, 6);

        int n = 0;
        double maxAbsDiff = 0.0;
        foreach (JsonElement v in doc.RootElement.GetProperty("vectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            TileId t = TileId.FromFaceLevelUV(face, level, u, w);

            Assert.Equal(v.GetProperty("isOcean").GetBoolean(), result.IsOcean[t]);
            // Az elevacio EGZAKT (ugyanaz a mezo, mint a hidrologiaban).
            Assert.True(Math.Abs(v.GetProperty("elevation").GetDouble() - result.Elevation[t]) < 1e-6);

            double expected = v.GetProperty("precipitation").GetDouble();
            double got = result.Precipitation[t];
            double diff = Math.Abs(expected - got);
            if (diff > maxAbsDiff) maxAbsDiff = diff;
            Assert.True(diff < 1e-6,
                $"precip eltérés tile ({face},{u},{w}): várt {expected}, kapott {got}, diff {diff}");
            n++;
        }

        Assert.Equal(140, n);
        // A tolerancia bőven tartható legyen (diagnosztika, ha valaha közelítené).
        Assert.True(maxAbsDiff < 1e-6, $"max abs diff = {maxAbsDiff}");
    }

    [Fact]
    public void LandPrecipitationIsPositiveOnAverage()
    {
        // A plauzibilitas: az advekcio a parton TUL is visz nedvesseget, tehat a
        // szarazfoldi csapadek atlaga POZITIV (nem ~0, mint advekcio nelkul).
        MoisturePrecipitation.PrecipitationField r =
            MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 4);

        double landSum = 0.0; int landCount = 0;
        foreach (KeyValuePair<TileId, double> kv in r.Precipitation)
        {
            if (r.IsOcean[kv.Key]) continue;
            landSum += kv.Value; landCount++;
        }
        Assert.True(landCount > 0);
        Assert.True(landSum / landCount > 0.0, "A szárazföldi átlag-csapadéknak pozitívnak kell lennie");
    }

    [Fact]
    public void IsDeterministic()
    {
        MoisturePrecipitation.PrecipitationField a = MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 3);
        MoisturePrecipitation.PrecipitationField b = MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 3);
        foreach (KeyValuePair<TileId, double> kv in a.Precipitation)
            Assert.Equal(kv.Value, b.Precipitation[kv.Key]);
    }

    /// <summary>
    /// ND-164: a paraméteres út hőmérséklet-bemenettel. <c>null</c> esetén
    /// BITRE a régi eredmény — ez az, ami minden korábbi hívót megvéd.
    /// </summary>
    [Fact]
    public void ParametricOverloadWithNullTemperatureIsBitIdentical()
    {
        MoisturePrecipitation.PrecipitationField legacy =
            MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 3);
        MoisturePrecipitation.PrecipitationField withNull =
            MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 3, null);

        Assert.Equal(legacy.SeaLevel, withNull.SeaLevel);
        Assert.Equal(legacy.Precipitation.Count, withNull.Precipitation.Count);
        foreach (KeyValuePair<TileId, double> kv in legacy.Precipitation)
            Assert.Equal(kv.Value, withNull.Precipitation[kv.Key]);
    }

    /// <summary>A hőmérséklet-bemenet ÉRDEMBEN hat, és hiányzó tile explicit hiba.</summary>
    [Fact]
    public void ParametricOverloadReadsTheGivenTemperature()
    {
        MoisturePrecipitation.PrecipitationField legacy =
            MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 3);

        // Egyenletes, meleg mező: a párolgás mindenhol azonos, tehát a
        // csapadék-mintázatnak meg KELL változnia a szélességfüggőhöz képest.
        var uniform = new Dictionary<TileId, double>(legacy.Precipitation.Count);
        foreach (TileId id in legacy.Precipitation.Keys) uniform[id] = 300.0;
        MoisturePrecipitation.PrecipitationField warm =
            MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 3, uniform);

        bool anyDifferent = false;
        foreach (KeyValuePair<TileId, double> kv in legacy.Precipitation)
            if (kv.Value != warm.Precipitation[kv.Key]) { anyDifferent = true; break; }
        Assert.True(anyDifferent, "A megadott hőmérsékletnek hatnia kell a csapadékra.");

        // Ismételt hívás bitre azonos (tisztaság).
        MoisturePrecipitation.PrecipitationField again =
            MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 3, uniform);
        foreach (KeyValuePair<TileId, double> kv in warm.Precipitation)
            Assert.Equal(kv.Value, again.Precipitation[kv.Key]);

        // Hiányzó tile: explicit hiba, nem csendes visszaesés az analitikusra.
        var incomplete = new Dictionary<TileId, double>(uniform);
        foreach (TileId id in legacy.Precipitation.Keys) { incomplete.Remove(id); break; }
        Assert.Throws<ArgumentException>(() =>
            MoisturePrecipitation.Compute(0xA7C944210000UL, 20, 3, incomplete));
    }
}
