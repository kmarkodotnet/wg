using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Hydrology;

public class FlowNetworkVectorFileTests
{
    /// <summary>
    /// A Python referencia (tools/reference/hydrology_ref.py) által
    /// generált vektorok. BITPONTOS egyezés várt - a priority-flood csak
    /// max/összehasonlítás műveleteket használ, nincs transzcendens
    /// függvény (az elevation-mező maga már bitre verifikált M4-ből).
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "hydrology_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        int level = root.GetProperty("level").GetInt32();
        double targetWaterFraction = root.GetProperty("targetWaterFraction").GetDouble();

        var field = SeaLevelCalibration.ComputeElevationField(worldSeed, plateCount, level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, targetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);
        Dictionary<TileId, long> accumulation = FlowNetwork.FlowAccumulation(field, flood.Parent, flood.FloodOrder);

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            TileId id = TileId.FromFaceLevelUV(face, level, u, w);

            bool expectedIsOcean = v.GetProperty("isOcean").GetBoolean();
            double expectedFilled = v.GetProperty("filled").GetDouble();
            int expectedFloodOrder = v.GetProperty("floodOrder").GetInt32();
            long expectedAccumulation = v.GetProperty("accumulation").GetInt64();

            Assert.Equal(expectedIsOcean, isOcean[id]);
            Assert.Equal(expectedFilled, flood.Filled[id]);
            Assert.Equal(expectedFloodOrder, flood.FloodOrder[id]);
            Assert.Equal(expectedAccumulation, accumulation[id]);

            JsonElement parentEl = v.GetProperty("parent");
            if (parentEl.ValueKind == JsonValueKind.Null)
            {
                Assert.False(flood.Parent[id].HasValue);
            }
            else
            {
                int pFace = parentEl.GetProperty("face").GetInt32();
                uint pu = parentEl.GetProperty("u").GetUInt32();
                uint pv = parentEl.GetProperty("v").GetUInt32();
                TileId expectedParent = TileId.FromFaceLevelUV(pFace, level, pu, pv);
                Assert.True(flood.Parent[id].HasValue);
                Assert.Equal(expectedParent, flood.Parent[id]!.Value);
            }
            checkedCount++;
        }

        Assert.Equal(500, checkedCount);
    }
}

public class FlowNetworkStructuralTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;

    /// <summary>
    /// A milestone "Kész, ha" kritériuma (M7): minden szárazföld-tile-ból
    /// véges lépésben óceánba jutunk, hurok nélkül. Ez STRUKTURÁLISAN
    /// garantált (a parent-lánc egy fa, óceán-gyökerekkel), nem csak
    /// méréssel bizonyított.
    /// </summary>
    [Fact]
    public void AllLandTilesReachOceanWithNoInfiniteLoop()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);

        List<TileId> failures = FlowNetwork.VerifyAllLandReachesOcean(
            field, flood.Parent, isOcean, maxSteps: field.Count + 10);

        Assert.Empty(failures);
    }

    [Fact]
    public void FilledElevationIsMonotonicallyNonDecreasingAwayFromOcean()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);

        foreach (TileId id in field.Keys)
        {
            TileId? parent = flood.Parent[id];
            if (!parent.HasValue) continue; // óceán-gyökér
            Assert.True(flood.Filled[id] >= flood.Filled[parent.Value] - 1e-9,
                $"A feltöltött magasságnak a szülőnél nagyobb-egyenlőnek kell lennie: {id}");
        }
    }

    [Fact]
    public void EveryTileHasStrictlyLargerFloodOrderThanItsParent()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);

        foreach (TileId id in field.Keys)
        {
            TileId? parent = flood.Parent[id];
            if (!parent.HasValue) continue;
            Assert.True(flood.FloodOrder[id] > flood.FloodOrder[parent.Value],
                $"A gyerek flood order-jének nagyobbnak kell lennie a szülőénél: {id}");
        }
    }

    [Fact]
    public void RiverNetworkCoversAPlausibleSmallFractionOfLand()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);
        Dictionary<TileId, long> accumulation = FlowNetwork.FlowAccumulation(field, flood.Parent, flood.FloodOrder);

        var landAccumulations = new List<long>();
        foreach (TileId id in field.Keys)
            if (!isOcean[id])
                landAccumulations.Add(accumulation[id]);
        landAccumulations.Sort((a, b) => b.CompareTo(a));

        int idx = (int)(0.03 * landAccumulations.Count);
        long riverThreshold = landAccumulations[idx];

        int riverTiles = landAccumulations.FindAll(a => a >= riverThreshold).Count;
        Assert.True(riverTiles > 0);
        Assert.True(riverTiles < landAccumulations.Count * 0.10);
    }

    [Fact]
    public void IsDeterministic()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);

        FlowNetwork.FloodResult a = FlowNetwork.PriorityFlood(field, isOcean);
        FlowNetwork.FloodResult b = FlowNetwork.PriorityFlood(field, isOcean);

        foreach (TileId id in field.Keys)
        {
            Assert.Equal(a.Filled[id], b.Filled[id]);
            Assert.Equal(a.Parent[id], b.Parent[id]);
            Assert.Equal(a.FloodOrder[id], b.FloodOrder[id]);
        }
    }

    [Fact]
    public void HeapMatchesPreviousSortedSetImplementationExactly()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, level: 5);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);

        FlowNetwork.FloodResult expected = PriorityFloodSortedSetReference(field, isOcean);
        FlowNetwork.FloodResult actual = FlowNetwork.PriorityFlood(field, isOcean);

        foreach (TileId id in field.Keys)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Filled[id]), BitConverter.DoubleToInt64Bits(actual.Filled[id]));
            Assert.Equal(expected.Parent[id], actual.Parent[id]);
            Assert.Equal(expected.FloodOrder[id], actual.FloodOrder[id]);
        }
    }

    [Fact]
    public void DenseFloodMatchesDictionaryFloodExactly()
    {
        const int denseLevel = 5;
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, denseLevel);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult expected = FlowNetwork.PriorityFlood(field, isOcean);

        FlowNetwork.DenseGridTopology topology = FlowNetwork.DenseGridTopology.Create(denseLevel);
        var denseField = new double[topology.Count];
        var denseOcean = new bool[topology.Count];
        for (int i = 0; i < topology.Count; i++)
        {
            TileId id = topology.TileAt(i);
            denseField[i] = field[id];
            denseOcean[i] = isOcean[id];
        }

        FlowNetwork.DenseFloodResult actual = FlowNetwork.PriorityFloodDense(topology, denseField, denseOcean);
        Dictionary<TileId, double> convertedFilled = actual.ToFilledDictionary(topology);
        for (int i = 0; i < topology.Count; i++)
        {
            TileId id = topology.TileAt(i);
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Filled[id]), BitConverter.DoubleToInt64Bits(actual.Filled[i]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Filled[id]), BitConverter.DoubleToInt64Bits(convertedFilled[id]));
            Assert.Equal(expected.FloodOrder[id], actual.FloodOrder[i]);

            TileId? expectedParent = expected.Parent[id];
            if (expectedParent.HasValue)
                Assert.Equal(topology.IndexOf(expectedParent.Value), actual.ParentIndex[i]);
            else
                Assert.Equal(-1, actual.ParentIndex[i]);

            Assert.Equal(topology.IndexOf(TileNeighbors.Neighbor(id, TileDirection.Right)),
                topology.NeighborIndex(i, TileDirection.Right));
            Assert.Equal(topology.IndexOf(TileNeighbors.Neighbor(id, TileDirection.Left)),
                topology.NeighborIndex(i, TileDirection.Left));
            Assert.Equal(topology.IndexOf(TileNeighbors.Neighbor(id, TileDirection.Up)),
                topology.NeighborIndex(i, TileDirection.Up));
            Assert.Equal(topology.IndexOf(TileNeighbors.Neighbor(id, TileDirection.Down)),
                topology.NeighborIndex(i, TileDirection.Down));
        }
    }

    private static FlowNetwork.FloodResult PriorityFloodSortedSetReference(
        Dictionary<TileId, double> field, Dictionary<TileId, bool> isOcean)
    {
        var result = new FlowNetwork.FloodResult();
        var visited = new HashSet<TileId>();
        var queue = new SortedSet<(double Elevation, long Counter)>();
        var counterToTile = new Dictionary<long, TileId>();
        long counter = 0;

        foreach (var pair in isOcean)
        {
            if (!pair.Value) continue;
            TileId tile = pair.Key;
            result.Filled[tile] = field[tile];
            result.Parent[tile] = null;
            visited.Add(tile);
            queue.Add((field[tile], counter));
            counterToTile[counter] = tile;
            counter++;
        }

        int order = 0;
        while (queue.Count > 0)
        {
            (double Elevation, long Counter) min = queue.Min;
            queue.Remove(min);
            TileId current = counterToTile[min.Counter];
            result.FloodOrder[current] = order++;

            TileNeighbors.GetAll(current, out TileId right, out TileId left, out TileId up, out TileId down);
            TryFloodReference(right, current, min.Elevation, field, result, visited, queue, counterToTile, ref counter);
            TryFloodReference(left, current, min.Elevation, field, result, visited, queue, counterToTile, ref counter);
            TryFloodReference(up, current, min.Elevation, field, result, visited, queue, counterToTile, ref counter);
            TryFloodReference(down, current, min.Elevation, field, result, visited, queue, counterToTile, ref counter);
        }

        return result;
    }

    private static void TryFloodReference(
        TileId candidate, TileId from, double fromElevation,
        Dictionary<TileId, double> field, FlowNetwork.FloodResult result,
        HashSet<TileId> visited, SortedSet<(double Elevation, long Counter)> queue,
        Dictionary<long, TileId> counterToTile, ref long counter)
    {
        if (visited.Contains(candidate)) return;
        visited.Add(candidate);
        double candidateFilled = Math.Max(field[candidate], fromElevation);
        result.Filled[candidate] = candidateFilled;
        result.Parent[candidate] = from;
        queue.Add((candidateFilled, counter));
        counterToTile[counter] = candidate;
        counter++;
    }
}

public class FlowNetworkEdgeCaseTests
{
    [Fact]
    public void OceanTilesHaveNullParentAndAccumulationExcludesThemAsChildren()
    {
        const ulong worldSeed = 1UL;
        const int plateCount = 6;
        const int level = 3;
        var field = SeaLevelCalibration.ComputeElevationField(worldSeed, plateCount, level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.5);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);

        foreach (TileId id in field.Keys)
        {
            if (isOcean[id])
                Assert.False(flood.Parent[id].HasValue);
        }
    }

    [Fact]
    public void EveryTileGetsAFloodOrderAssigned()
    {
        const ulong worldSeed = 2UL;
        const int plateCount = 6;
        const int level = 3;
        var field = SeaLevelCalibration.ComputeElevationField(worldSeed, plateCount, level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.5);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);

        Assert.Equal(field.Count, flood.FloodOrder.Count);
    }
}
