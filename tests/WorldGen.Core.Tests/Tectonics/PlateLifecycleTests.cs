using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// Lemez-eletciklus (ND-45) - a Python referencia (plate_lifecycle_ref.py)
/// 92 eletciklus-sorsolas + 15 topologia-pillanatkep vektora. A lanc VEGIG
/// bit-egzakt (Sample4/DeterministicRandom + Rodrigues DeterministicMath.SinCos-szal),
/// ezert EGZAKT egyezes vart (Assert.Equal).
/// </summary>
public class PlateLifecycleVectorFileTests
{
    [Fact]
    public void RollVectorsMatchPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "plate_lifecycle_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

        int n = 0;
        foreach (JsonElement v in doc.RootElement.GetProperty("rollVectors").EnumerateArray())
        {
            ulong worldSeed = v.GetProperty("worldSeed").GetUInt64();
            ulong plateId = v.GetProperty("plateId").GetUInt64();
            PlateLifecycle.LifecycleRoll roll = PlateLifecycle.PlateLifecycleRoll(worldSeed, plateId);
            Assert.Equal(v.GetProperty("kind").GetString(), PlateLifecycle.EventKindName(roll.Kind));
            Assert.Equal(v.GetProperty("lifespanMyr").GetDouble(), roll.LifespanMyr);
            Assert.Equal(v.GetProperty("riftFractionOfLifespan").GetDouble(), roll.RiftFractionOfLifespan);
            n++;
        }
        Assert.Equal(92, n);
    }

    [Fact]
    public void TopologyVectorsMatchPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "plate_lifecycle_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

        int nSnap = 0;
        foreach (JsonElement snap in doc.RootElement.GetProperty("topologyVectors").EnumerateArray())
        {
            ulong worldSeed = snap.GetProperty("worldSeed").GetUInt64();
            int plateCount = snap.GetProperty("plateCount").GetInt32();
            double timeMyr = snap.GetProperty("timeMyr").GetDouble();

            List<PlateLifecycle.PlateRecord> topo = PlateLifecycle.ResolveTopology(worldSeed, timeMyr, plateCount);
            JsonElement plates = snap.GetProperty("plates");
            Assert.Equal(plates.GetArrayLength(), topo.Count);

            int idx = 0;
            foreach (JsonElement p in plates.EnumerateArray())
            {
                // A DISZKRET mezok (plateId/generation/parentId/eventKind/darabszam)
                // bit-egzaktak -> a fa-struktura pontosan egyezik. A POZICIO
                // (kompozit Rodrigues-forgatas) ~1 ULP-nyit elterhet (a DeterministicMath.SinCos
                // ujraosszegzese uj szogekre), ezert a float-mezoknel 1e-9 tolerancia.
                const double tol = 1e-9;
                PlateLifecycle.PlateRecord r = topo[idx];
                Assert.Equal(p.GetProperty("plateId").GetUInt64(), r.PlateId);
                Assert.True(Math.Abs(p.GetProperty("posX").GetDouble() - r.PosX) < tol);
                Assert.True(Math.Abs(p.GetProperty("posY").GetDouble() - r.PosY) < tol);
                Assert.True(Math.Abs(p.GetProperty("posZ").GetDouble() - r.PosZ) < tol);
                Assert.True(Math.Abs(p.GetProperty("axisX").GetDouble() - r.AxisX) < tol);
                Assert.True(Math.Abs(p.GetProperty("axisY").GetDouble() - r.AxisY) < tol);
                Assert.True(Math.Abs(p.GetProperty("axisZ").GetDouble() - r.AxisZ) < tol);
                Assert.True(Math.Abs(p.GetProperty("omega").GetDouble() - r.Omega) < tol);
                Assert.True(Math.Abs(p.GetProperty("birthTimeMyr").GetDouble() - r.BirthTimeMyr) < tol);
                Assert.Equal(p.GetProperty("generation").GetInt32(), r.Generation);

                JsonElement parentId = p.GetProperty("parentId");
                if (parentId.ValueKind == JsonValueKind.Null)
                    Assert.Null(r.ParentId);
                else
                    Assert.Equal(parentId.GetUInt64(), r.ParentId);

                Assert.Equal(p.GetProperty("eventKind").GetString(), PlateLifecycle.EventKindName(r.Event));
                AssertNullableEqual(p.GetProperty("pendingEventTimeMyr"), r.PendingEventTimeMyr);
                AssertNullableEqual(p.GetProperty("riftActivationTimeMyr"), r.RiftActivationTimeMyr);
                idx++;
            }
            nSnap++;
        }
        Assert.Equal(15, nSnap);
    }

    private static void AssertNullableEqual(JsonElement el, double? actual)
    {
        if (el.ValueKind == JsonValueKind.Null)
            Assert.Null(actual);
        else
        {
            Assert.NotNull(actual);
            Assert.Equal(el.GetDouble(), actual.Value);
        }
    }
}

public class PlateLifecyclePropertyTests
{
    private const ulong Seed = 0xA7C944210000UL;

    [Fact]
    public void RootTopologyMatchesStaticPlatesAtTimeZero()
    {
        const int plateCount = 10;
        var seeds = PlateGeneration.GenerateSeeds(Seed, plateCount);
        List<PlateLifecycle.PlateRecord> topo = PlateLifecycle.ResolveTopology(Seed, 0.0, plateCount);
        Assert.Equal(plateCount, topo.Count);
        foreach (PlateLifecycle.PlateRecord r in topo)
        {
            Assert.Null(r.ParentId);
            Assert.Equal(0, r.Generation);
            var s = seeds[(int)r.PlateId];
            Assert.True(Math.Abs(r.PosX - s.X) < 1e-12 && Math.Abs(r.PosY - s.Y) < 1e-12 && Math.Abs(r.PosZ - s.Z) < 1e-12);
        }
    }

    [Fact]
    public void PlateCountChangesOverTime()
    {
        var counts = new HashSet<int>();
        foreach (double t in new[] { 0.0, 250.0, 600.0, 1000.0, 2000.0 })
            counts.Add(PlateLifecycle.ResolveTopology(Seed, t, 10).Count);
        Assert.True(counts.Count > 1, "A lemezszamnak valtoznia kell idovel (split/merge)");
    }

    [Fact]
    public void TimestepInvariant()
    {
        List<PlateLifecycle.PlateRecord> direct = PlateLifecycle.ResolveTopology(Seed, 725.0, 10);
        foreach (double t in new[] { 50.0, 123.0, 333.0, 500.0, 700.0 })
            PlateLifecycle.ResolveTopology(Seed, t, 10);
        List<PlateLifecycle.PlateRecord> after = PlateLifecycle.ResolveTopology(Seed, 725.0, 10);
        Assert.Equal(direct.Count, after.Count);
        for (int i = 0; i < direct.Count; i++)
        {
            Assert.Equal(direct[i].PlateId, after[i].PlateId);
            Assert.Equal(direct[i].PosX, after[i].PosX);
        }
    }
}
