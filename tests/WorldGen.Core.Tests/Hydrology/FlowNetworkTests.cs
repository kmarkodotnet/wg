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
