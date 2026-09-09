using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Hydrology;

/// <summary>
/// M7 tavak/jeg/erozio (ND-43) - a Python referencia (lakes_ice_erosion_ref.py)
/// TEST-EARTH-001 mezojere szamolt 500 vektora. A teljes pipeline-t
/// (elevation + tengerszint + priority-flood + accumulation) a mar verifikalt
/// SeaLevelCalibration/FlowNetwork adja; erre epul a to/jeg/erozio. A float
/// mezoknel 1e-6 tolerancia (a homerseklet-lanc nyers sin/cos-a + az erozios
/// Math.Pow miatt); a diszkret mezok (isOcean/isLake/lakeId/iceClass) egzaktak.
/// </summary>
public class LakesIceErosionVectorFileTests
{
    private const double Tol = 1e-6;

    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "lakes_ice_erosion_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        int level = root.GetProperty("level").GetInt32();
        double orbitalPeriod = root.GetProperty("orbitalPeriod").GetDouble();
        double rotationPeriod = root.GetProperty("rotationPeriod").GetDouble();
        double axialTilt = root.GetProperty("axialTilt").GetDouble();
        double expectedSeaLevel = root.GetProperty("seaLevel").GetDouble();

        // Teljes pipeline (a hydrology_ref.compute_elevation_and_ocean_field megfeleloje).
        var field = SeaLevelCalibration.ComputeElevationField(worldSeed, plateCount, level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.65);
        Assert.True(Math.Abs(seaLevel - expectedSeaLevel) < 1e-6, $"tengerszint elter: {seaLevel} vs {expectedSeaLevel}");
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);
        Dictionary<TileId, long> accumulation = FlowNetwork.FlowAccumulation(field, flood.Parent, flood.FloodOrder);

        LakesIceErosion.LakeResult lakes = LakesIceErosion.IdentifyLakes(field, flood.Filled, isOcean);
        LakesIceErosion.ErosionResult erosion = LakesIceErosion.ApplyStaticErosionPass(field, flood.Parent, isOcean, accumulation);

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            TileId key = TileId.FromFaceLevelUV(face, level, u, w);

            Assert.Equal(v.GetProperty("isOcean").GetBoolean(), isOcean[key]);
            Assert.True(Math.Abs(field[key] - v.GetProperty("rawElevation").GetDouble()) < Tol);
            Assert.True(Math.Abs(flood.Filled[key] - v.GetProperty("filled").GetDouble()) < Tol);

            Assert.Equal(v.GetProperty("isLake").GetBoolean(), lakes.IsLake[key]);
            Assert.True(Math.Abs(lakes.Depth[key] - v.GetProperty("lakeDepth").GetDouble()) < Tol);
            JsonElement lakeIdEl = v.GetProperty("lakeId");
            if (lakeIdEl.ValueKind == JsonValueKind.Null)
                Assert.False(lakes.LakeId.ContainsKey(key));
            else
                Assert.Equal(lakeIdEl.GetInt32(), lakes.LakeId[key]);

            TileGeometry.ToPosition(key, out double x, out double y, out double z);
            LakesIceErosion.AnnualTemperatureStats(x, y, z, orbitalPeriod, rotationPeriod, axialTilt,
                isOcean[key], field[key], seaLevel, out double mean, out double min, out _);
            Assert.True(Math.Abs(mean - v.GetProperty("annualMeanTemperatureK").GetDouble()) < Tol);
            Assert.True(Math.Abs(min - v.GetProperty("annualMinTemperatureK").GetDouble()) < Tol);
            Assert.Equal(v.GetProperty("iceClass").GetString(), LakesIceErosion.IceClassName(LakesIceErosion.ClassifyIce(mean, min)));

            Assert.True(Math.Abs(erosion.Erosion[key] - v.GetProperty("erosionDepth").GetDouble()) < Tol);
            Assert.True(Math.Abs(erosion.DepositionGain[key] - v.GetProperty("depositionGain").GetDouble()) < Tol);
            Assert.True(Math.Abs(erosion.NewField[key] - v.GetProperty("newElevation").GetDouble()) < Tol);
            checkedCount++;
        }
        Assert.Equal(500, checkedCount);
    }
}

public class LakesIceErosionPropertyTests
{
    private const double OrbitalPeriod = 365.25, RotationPeriod = 1.0;
    private static readonly double AxialTilt = 23.44 * Math.PI / 180.0;

    [Fact]
    public void PoleIsPermanentIceEquatorIsNone()
    {
        LakesIceErosion.AnnualTemperatureStats(0, 0, 1, OrbitalPeriod, RotationPeriod, AxialTilt, false, 0, 0, out double pm, out double pmin, out _);
        LakesIceErosion.AnnualTemperatureStats(1, 0, 0, OrbitalPeriod, RotationPeriod, AxialTilt, false, 0, 0, out double em, out double emin, out _);
        Assert.Equal(LakesIceErosion.IceClass.PermanentIce, LakesIceErosion.ClassifyIce(pm, pmin));
        Assert.Equal(LakesIceErosion.IceClass.None, LakesIceErosion.ClassifyIce(em, emin));
    }

    [Fact]
    public void HighEquatorialPeakGetsIceOrSnow()
    {
        LakesIceErosion.AnnualTemperatureStats(1, 0, 0, OrbitalPeriod, RotationPeriod, AxialTilt, false, 6000.0, 0, out double m, out double mn, out _);
        Assert.NotEqual(LakesIceErosion.IceClass.None, LakesIceErosion.ClassifyIce(m, mn));
    }
}
