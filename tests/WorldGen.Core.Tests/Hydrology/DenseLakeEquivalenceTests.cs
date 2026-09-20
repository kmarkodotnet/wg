using System;
using System.Collections.Generic;
using System.Diagnostics;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Core.Tests.Hydrology;

/// <summary>
/// A <see cref="LakesIceErosion.IdentifyLakesDense"/> differenciális igazolása
/// a Dictionary-alapú <see cref="LakesIceErosion.IdentifyLakes"/> ellen.
///
/// A Dictionary-út marad a REFERENCIA-ORÁKULUM: nem írjuk át, és minden
/// ellenőrzés hozzá méri a tömbindexelt változatot. A cél nem „hasonló"
/// eredmény, hanem BITRE azonos — a tavak száma, sorrendje, a tile-listák
/// ELEMSORRENDJE és a lebegőpontos statisztikák is.
///
/// Az elemsorrend és a bitpontos statisztika azért követelmény, mert a
/// <c>SurfaceElevation</c>/<c>MeanDepth</c> összegzéssel készül, a
/// lebegőpontos összeadás pedig nem asszociatív: ha a komponens-bejárás
/// sorrendje eltérne, az érték is eltérhetne — csendben, a látható tó-alak
/// megváltozása nélkül.
/// </summary>
public class DenseLakeEquivalenceTests
{
    private readonly ITestOutputHelper _out;
    public DenseLakeEquivalenceTests(ITestOutputHelper o) { _out = o; }

    private sealed class Case
    {
        public FlowNetwork.DenseGridTopology Topology = null!;
        public double[] DenseField = Array.Empty<double>();
        public bool[] DenseOcean = Array.Empty<bool>();
        public double[] DenseFilled = Array.Empty<double>();
        public Dictionary<TileId, double> Field = new();
        public Dictionary<TileId, bool> IsOcean = new();
        public Dictionary<TileId, double> Filled = new();
    }

    /// <summary>
    /// Valódi világ: ugyanaz a lánc, amit a viewer futtat (eleváció-mező →
    /// tengerszint → óceán-maszk → priority flood).
    /// </summary>
    private static Case BuildCase(ulong seed, int plateCount, int level, double targetWater)
    {
        var c = new Case();
        c.Field = SeaLevelCalibration.ComputeElevationField(seed, plateCount, level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(c.Field.Values, targetWater);
        c.IsOcean = FlowNetwork.ComputeOceanField(c.Field, seaLevel);

        c.Topology = FlowNetwork.DenseGridTopology.Create(level);
        int count = c.Topology.Count;
        c.DenseField = new double[count];
        c.DenseOcean = new bool[count];
        for (int i = 0; i < count; i++)
        {
            TileId tile = c.Topology.TileAt(i);
            c.DenseField[i] = c.Field[tile];
            c.DenseOcean[i] = c.IsOcean[tile];
        }

        FlowNetwork.DenseFloodResult flood = FlowNetwork.PriorityFloodDense(c.Topology, c.DenseField, c.DenseOcean);
        c.DenseFilled = flood.Filled;
        c.Filled = flood.ToFilledDictionary(c.Topology);
        return c;
    }

    private static void AssertSameLakes(
        LakesIceErosion.LakeResult expected, LakesIceErosion.DenseLakeResult actual,
        FlowNetwork.DenseGridTopology topology, string what)
    {
        Assert.Equal(expected.Lakes.Count, actual.Lakes.Count);
        for (int i = 0; i < expected.Lakes.Count; i++)
        {
            LakesIceErosion.LakeInfo e = expected.Lakes[i];
            LakesIceErosion.LakeInfo a = actual.Lakes[i];
            Assert.Equal(e.Id, a.Id);
            Assert.Equal(e.TileCount, a.TileCount);
            // BITRE azonos statisztikák - ld. az osztály doksiját.
            Assert.Equal(e.SurfaceElevation, a.SurfaceElevation);
            Assert.Equal(e.MinSurface, a.MinSurface);
            Assert.Equal(e.MaxSurface, a.MaxSurface);
            Assert.Equal(e.MaxDepth, a.MaxDepth);
            Assert.Equal(e.MeanDepth, a.MeanDepth);
            // A tile-lista ELEMSORRENDJE is, nem csak a halmaz.
            Assert.Equal(e.Tiles.Count, a.Tiles.Count);
            for (int t = 0; t < e.Tiles.Count; t++)
                Assert.True(e.Tiles[t].Equals(a.Tiles[t]),
                    $"{what}: a(z) {i}. tó {t}. tile-ja eltér ({e.Tiles[t]} vs {a.Tiles[t]}).");
        }

        // A per-tile mezők is - a konvertált alakon keresztül.
        LakesIceErosion.LakeResult converted = actual.ToLakeResult(topology);
        Assert.Equal(expected.IsLake.Count, converted.IsLake.Count);
        Assert.Equal(expected.Depth.Count, converted.Depth.Count);
        Assert.Equal(expected.LakeId.Count, converted.LakeId.Count);
        foreach (KeyValuePair<TileId, bool> kv in expected.IsLake)
            Assert.Equal(kv.Value, converted.IsLake[kv.Key]);
        foreach (KeyValuePair<TileId, double> kv in expected.Depth)
            Assert.Equal(kv.Value, converted.Depth[kv.Key]);
        foreach (KeyValuePair<TileId, int> kv in expected.LakeId)
            Assert.Equal(kv.Value, converted.LakeId[kv.Key]);
    }

    [Theory]
    [InlineData(0xA7C944210000UL, 20, 5, 0.65)]
    [InlineData(0xA7C944210000UL, 20, 6, 0.65)]
    [InlineData(0x1234567890ABUL, 12, 6, 0.50)]
    [InlineData(0xFEDCBA987654UL, 31, 6, 0.75)]
    [InlineData(0x0000000000001UL, 7, 5, 0.40)]
    public void DenseMatchesTheDictionaryOracle(ulong seed, int plateCount, int level, double targetWater)
    {
        Case c = BuildCase(seed, plateCount, level, targetWater);

        LakesIceErosion.LakeResult expected = LakesIceErosion.IdentifyLakes(c.Field, c.Filled, c.IsOcean);
        LakesIceErosion.DenseLakeResult actual = LakesIceErosion.IdentifyLakesDense(
            c.Topology, c.DenseField, c.DenseFilled, c.DenseOcean);

        _out.WriteLine($"seed=0x{seed:X} plates={plateCount} level={level} water={targetWater}: "
            + $"{c.Topology.Count} tile, {expected.Lakes.Count} tó");
        Assert.True(expected.Lakes.Count > 0, "Egyetlen tó sincs - a teszt semmit nem mér.");
        AssertSameLakes(expected, actual, c.Topology, $"seed=0x{seed:X}");
    }

    /// <summary>
    /// A küszöb is paraméter: más <c>minDepth</c> más tó-halmazt ad, és a két
    /// útnak ott is egyeznie kell. Enélkül az egyezés csak az alapértéken
    /// lenne igazolva.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(25.0)]
    [InlineData(500.0)]
    public void DenseMatchesTheOracleAtOtherDepthThresholds(double minDepth)
    {
        Case c = BuildCase(0xA7C944210000UL, 20, 6, 0.65);

        LakesIceErosion.LakeResult expected = LakesIceErosion.IdentifyLakes(c.Field, c.Filled, c.IsOcean, minDepth);
        LakesIceErosion.DenseLakeResult actual = LakesIceErosion.IdentifyLakesDense(
            c.Topology, c.DenseField, c.DenseFilled, c.DenseOcean, minDepth);

        AssertSameLakes(expected, actual, c.Topology, $"minDepth={minDepth}");
    }

    /// <summary>
    /// Élesetek: nincs tó (minden óceán), illetve minden tile tó. Ezeket a
    /// véletlen világok nem feltétlenül érintik.
    /// </summary>
    [Fact]
    public void DegenerateFieldsMatchToo()
    {
        const int level = 4;
        var topology = FlowNetwork.DenseGridTopology.Create(level);
        int count = topology.Count;

        foreach (bool allOcean in new[] { true, false })
        {
            var denseField = new double[count];
            var denseFilled = new double[count];
            var denseOcean = new bool[count];
            var field = new Dictionary<TileId, double>(count);
            var filled = new Dictionary<TileId, double>(count);
            var isOcean = new Dictionary<TileId, bool>(count);
            for (int i = 0; i < count; i++)
            {
                TileId tile = topology.TileAt(i);
                denseField[i] = 100.0;
                denseFilled[i] = allOcean ? 100.0 : 900.0; // mindenhol 800 m mély "tó"
                denseOcean[i] = allOcean;
                field[tile] = denseField[i];
                filled[tile] = denseFilled[i];
                isOcean[tile] = denseOcean[i];
            }

            LakesIceErosion.LakeResult expected = LakesIceErosion.IdentifyLakes(field, filled, isOcean);
            LakesIceErosion.DenseLakeResult actual = LakesIceErosion.IdentifyLakesDense(
                topology, denseField, denseFilled, denseOcean);

            Assert.Equal(allOcean ? 0 : 1, expected.Lakes.Count);
            AssertSameLakes(expected, actual, topology, allOcean ? "csupa óceán" : "csupa tó");
        }
    }

    /// <summary>A méret-ellenőrzés tényleg fog, nem csak a doksiban van.</summary>
    [Fact]
    public void MismatchedArrayLengthsAreRejected()
    {
        var topology = FlowNetwork.DenseGridTopology.Create(3);
        var ok = new double[topology.Count];
        var okOcean = new bool[topology.Count];
        var shortArray = new double[topology.Count - 1];

        Assert.Throws<ArgumentException>(() =>
            LakesIceErosion.IdentifyLakesDense(topology, shortArray, ok, okOcean));
        Assert.Throws<ArgumentException>(() =>
            LakesIceErosion.IdentifyLakesDense(topology, ok, shortArray, okOcean));
        Assert.Throws<ArgumentException>(() =>
            LakesIceErosion.IdentifyLakesDense(topology, ok, ok, new bool[topology.Count - 1]));
    }

    /// <summary>
    /// A nyereség mérése. A Dictionary-út költségének nagy részét NEM a
    /// tó-keresés adja, hanem a körítés: a kulcsok (face,u,v) szerinti
    /// RENDEZÉSE, három eredmény-Dictionary felépítése, és - a viewerben - a
    /// `ToFilledDictionary` konverzió. A tömbindexelt út mindezt elhagyja.
    /// </summary>
    [Fact]
    public void DenseIsSubstantiallyFaster()
    {
        Case c = BuildCase(0xA7C944210000UL, 20, 7, 0.65);

        // Bemelegítés (JIT), különben az első mérés a fordítást is tartalmazná.
        LakesIceErosion.IdentifyLakes(c.Field, c.Filled, c.IsOcean);
        LakesIceErosion.IdentifyLakesDense(c.Topology, c.DenseField, c.DenseFilled, c.DenseOcean);

        var sw = Stopwatch.StartNew();
        LakesIceErosion.LakeResult expected = LakesIceErosion.IdentifyLakes(c.Field, c.Filled, c.IsOcean);
        sw.Stop();
        double dictMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        LakesIceErosion.DenseLakeResult actual = LakesIceErosion.IdentifyLakesDense(
            c.Topology, c.DenseField, c.DenseFilled, c.DenseOcean);
        sw.Stop();
        double denseMs = sw.Elapsed.TotalMilliseconds;

        _out.WriteLine($"level 7 ({c.Topology.Count} tile, {expected.Lakes.Count} tó): "
            + $"Dictionary={dictMs:F1}ms dense={denseMs:F1}ms ({dictMs / Math.Max(0.001, denseMs):F1}x)");

        AssertSameLakes(expected, actual, c.Topology, "teljesítmény-eset");
        Assert.True(denseMs * 2 < dictMs,
            $"A tömbindexelt út nem gyorsabb kétszeresen ({denseMs:F1} ms vs {dictMs:F1} ms).");
    }
}
