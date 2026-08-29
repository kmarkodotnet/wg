using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

public class PlateMotionVectorFileTests
{
    /// <summary>
    /// A Python referencia (tools/reference/plate_motion_ref.py) által
    /// generált vektorok. TOLERANCIA-alapú összehasonlítás (ND-26/27
    /// osztály - Math.Sin/Cos nem garantáltan bitre azonos platformok
    /// között).
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceWithinTolerance()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "plate_motion_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            int plateId = v.GetProperty("plateId").GetInt32();
            double timeMyr = v.GetProperty("timeMyr").GetDouble();
            double seedX = v.GetProperty("seedX").GetDouble();
            double seedY = v.GetProperty("seedY").GetDouble();
            double seedZ = v.GetProperty("seedZ").GetDouble();
            double expX = v.GetProperty("movedX").GetDouble();
            double expY = v.GetProperty("movedY").GetDouble();
            double expZ = v.GetProperty("movedZ").GetDouble();

            PlateMotion.PlateSeedAtTime(worldSeed, plateId, seedX, seedY, seedZ, timeMyr,
                out double x, out double y, out double z);

            Assert.True(Math.Abs(x - expX) < 1e-9, $"x eltér: {x} vs {expX}");
            Assert.True(Math.Abs(y - expY) < 1e-9, $"y eltér: {y} vs {expY}");
            Assert.True(Math.Abs(z - expZ) < 1e-9, $"z eltér: {z} vs {expZ}");
            checkedCount++;
        }

        Assert.Equal(300, checkedCount);
    }
}

public class PlateMotionStructuralTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;

    [Fact]
    public void AtTimeZeroPositionMatchesSeed()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, 20);
        for (int i = 0; i < seeds.Length; i++)
        {
            PlateMotion.PlateSeedAtTime(WorldSeed, i, seeds[i].X, seeds[i].Y, seeds[i].Z, 0.0,
                out double x, out double y, out double z);

            Assert.True(Math.Abs(x - seeds[i].X) < 1e-12);
            Assert.True(Math.Abs(y - seeds[i].Y) < 1e-12);
            Assert.True(Math.Abs(z - seeds[i].Z) < 1e-12);
        }
    }

    [Fact]
    public void PlatesActuallyMoveOverDeepTime()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, 20);
        bool anyMoved = false;
        for (int i = 0; i < seeds.Length; i++)
        {
            PlateMotion.PlateSeedAtTime(WorldSeed, i, seeds[i].X, seeds[i].Y, seeds[i].Z, 100.0,
                out double x, out double y, out double z);
            double diff = Math.Max(Math.Abs(x - seeds[i].X),
                Math.Max(Math.Abs(y - seeds[i].Y), Math.Abs(z - seeds[i].Z)));
            if (diff > 1e-6) anyMoved = true;
        }
        Assert.True(anyMoved, "100 Myr alatt legalább egy lemeznek el kell mozdulnia");
    }

    [Fact]
    public void RotationPreservesUnitLength()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, 20);
        foreach (double t in new[] { 0.0, 50.0, 100.0, 500.0 })
        {
            for (int i = 0; i < seeds.Length; i++)
            {
                PlateMotion.PlateSeedAtTime(WorldSeed, i, seeds[i].X, seeds[i].Y, seeds[i].Z, t,
                    out double x, out double y, out double z);
                double lenSq = x * x + y * y + z * z;
                Assert.True(Math.Abs(lenSq - 1.0) < 1e-9, $"Nem egységhosszú: {lenSq} (t={t}, plate={i})");
            }
        }
    }

    /// <summary>
    /// ND-04 timestep-invariancia: a forgatás explicit, zárt függvénye
    /// t-nek (nem iteratív állapot-akkumulátor), ezért a végállapot
    /// bitre ugyanaz, függetlenül attól, hogy közvetlenül a végidőponttal
    /// vagy a "lépésekben összeadott" idővel kérdezzük le.
    /// </summary>
    [Fact]
    public void TimestepInvarianceHoldsExactly()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, 20);
        (double X, double Y, double Z) seed0 = seeds[0];

        PlateMotion.PlateSeedAtTime(WorldSeed, 0, seed0.X, seed0.Y, seed0.Z, 365.0,
            out double directX, out double directY, out double directZ);

        double steppedTime = 0.0;
        for (int i = 0; i < 73; i++) steppedTime += 5.0;
        PlateMotion.PlateSeedAtTime(WorldSeed, 0, seed0.X, seed0.Y, seed0.Z, steppedTime,
            out double steppedX, out double steppedY, out double steppedZ);

        Assert.True(Math.Abs(directX - steppedX) < 1e-12);
        Assert.True(Math.Abs(directY - steppedY) < 1e-12);
        Assert.True(Math.Abs(directZ - steppedZ) < 1e-12);
    }

    [Fact]
    public void IsPure()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, 20);
        (double X, double Y, double Z) s = seeds[5];

        PlateMotion.PlateSeedAtTime(WorldSeed, 5, s.X, s.Y, s.Z, 42.0, out double x1, out double y1, out double z1);
        PlateMotion.PlateSeedAtTime(WorldSeed, 5, s.X, s.Y, s.Z, 42.0, out double x2, out double y2, out double z2);

        Assert.Equal(x1, x2);
        Assert.Equal(y1, y2);
        Assert.Equal(z1, z2);
    }

    [Fact]
    public void DifferentPlatesHaveDifferentMotion()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, 20);
        PlateMotion.PlateSeedAtTime(WorldSeed, 0, seeds[0].X, seeds[0].Y, seeds[0].Z, 100.0,
            out double x0, out double y0, out double z0);

        bool anyDifferentMotion = false;
        for (int i = 1; i < seeds.Length; i++)
        {
            PlateMotion.PlateSeedAtTime(WorldSeed, i, seeds[i].X, seeds[i].Y, seeds[i].Z, 100.0,
                out double x, out double y, out double z);
            // Az abszolut pozicio termeszetesen mas (mas a kezdo pozicio is),
            // de a MOZGAS IRANYANAK/tengelyenek is kulonboznie kell -
            // a szogsebesseg/Euler-polus is lemezenkent elteroen sampleolt.
            double omega0 = PlateMotion.GenerateAngularVelocity(WorldSeed, 0);
            double omegaI = PlateMotion.GenerateAngularVelocity(WorldSeed, i);
            if (Math.Abs(omega0 - omegaI) > 1e-9) anyDifferentMotion = true;
        }
        Assert.True(anyDifferentMotion, "Minden lemeznek ugyanaz a szögsebessége - gyanús (kimaradt paraméter)");
    }
}
