using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// M10 deep-time erozio/eljegesedes (ND-44) - a Python referencia
/// (erosion_glaciation_deep_time_ref.py) 400 erozios + 200 eljegesedesi vektora.
/// 1e-6 tolerancia (nyers Math.Exp/Sin, mint a Temperature-lanc); isOceanic/isIced egzakt.
/// </summary>
public class DeepTimeErosionGlaciationVectorFileTests
{
    private const double Tol = 1e-6;

    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "erosion_glaciation_deep_time_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        int level = root.GetProperty("level").GetInt32();
        (double X, double Y, double Z)[] seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        int nE = 0;
        foreach (JsonElement v in root.GetProperty("erosionVectors").EnumerateArray())
        {
            TileId key = TileId.FromFaceLevelUV(v.GetProperty("face").GetInt32(), level,
                v.GetProperty("u").GetUInt32(), v.GetProperty("v").GetUInt32());
            TileGeometry.ToPosition(key, out double x, out double y, out double z);
            int plateId = PlateGeneration.AssignPlate(x, y, z, seeds);
            Assert.Equal(v.GetProperty("plateId").GetInt32(), plateId);

            double elev = DeepTimeErosionGlaciation.ElevationAtTime(worldSeed, plateId, x, y, z, seeds,
                v.GetProperty("timeMyr").GetDouble(), out bool oceanic);
            Assert.True(Math.Abs(elev - v.GetProperty("elevation").GetDouble()) < Tol, $"elev elter: {elev}");
            Assert.Equal(v.GetProperty("isOceanic").GetBoolean(), oceanic);
            nE++;
        }
        Assert.Equal(400, nE);

        int nG = 0;
        foreach (JsonElement v in root.GetProperty("glaciationVectors").EnumerateArray())
        {
            double timeMyr = v.GetProperty("timeMyr").GetDouble();
            double sampleLat = v.GetProperty("sampleAbsLatitudeRad").GetDouble();
            Assert.True(Math.Abs(DeepTimeErosionGlaciation.GlobalTempOffset(timeMyr) - v.GetProperty("globalTempOffsetK").GetDouble()) < Tol);
            Assert.True(Math.Abs(DeepTimeErosionGlaciation.IceLineAbsLatitude(timeMyr) - v.GetProperty("iceLineAbsLatitudeRad").GetDouble()) < Tol);
            Assert.Equal(v.GetProperty("isIced").GetBoolean(), DeepTimeErosionGlaciation.IsIced(sampleLat, timeMyr));
            nG++;
        }
        Assert.Equal(200, nG);
    }
}

public class DeepTimeErosionGlaciationPropertyTests
{
    [Fact]
    public void RelaxationIsTimestepInvariant()
    {
        const double h0 = 1234.5, totalT = 365.0;
        double direct = DeepTimeErosionGlaciation.UpliftRelaxationElevation(h0, totalT);
        foreach (int nSteps in new[] { 1, 2, 3, 5, 13, 47, 101, 500 })
        {
            double stepped = DeepTimeErosionGlaciation.ChainRelaxation(h0, totalT, nSteps);
            Assert.True(Math.Abs(stepped - direct) < 1e-6, $"nSteps={nSteps}: {stepped} vs {direct}");
        }
    }

    [Fact]
    public void GlobalTempOffsetZeroAtStart()
    {
        Assert.Equal(0.0, DeepTimeErosionGlaciation.GlobalTempOffset(0.0));
    }

    [Fact]
    public void IceLineCloserToEquatorInGlacial()
    {
        double coldest = 0.75 * DeepTimeErosionGlaciation.GlaciationPeriodMyr;
        double warmest = 0.25 * DeepTimeErosionGlaciation.GlaciationPeriodMyr;
        Assert.True(DeepTimeErosionGlaciation.IceLineAbsLatitude(coldest) < DeepTimeErosionGlaciation.IceLineAbsLatitude(warmest));
    }

    [Fact]
    public void MountainErodesTowardEquilibrium()
    {
        const double h0 = 3000.0;
        double t0 = DeepTimeErosionGlaciation.UpliftRelaxationElevation(h0, 0.0);
        double tFar = DeepTimeErosionGlaciation.UpliftRelaxationElevation(h0, 3000.0);
        Assert.Equal(h0, t0);
        Assert.True(tFar < t0);
        Assert.True(Math.Abs(tFar - DeepTimeErosionGlaciation.EquilibriumFraction * h0) < 1e-3);
    }
}
