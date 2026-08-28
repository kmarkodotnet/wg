using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

public class PlateBoundaryEffectVectorFileTests
{
    /// <summary>
    /// A Python referencia (tools/reference/plate_boundary_ref.py) által
    /// generált vektorok. BITPONTOS egyezés várt - a gap-alapú uplift csak
    /// szorzást/összeadást használ, nincs benne transzcendens függvény.
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "plate_boundary_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            int level = v.GetProperty("level").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            int plateId = v.GetProperty("plateId").GetInt32();
            double expectedElevation = v.GetProperty("elevation").GetDouble();
            bool expectedOceanic = v.GetProperty("isOceanic").GetBoolean();

            TileId id = TileId.FromFaceLevelUV(face, level, u, w);
            TileGeometry.ToPosition(id, out double x, out double y, out double z);

            double elevation = PlateBoundaryEffect.ElevationWithBoundary(
                worldSeed, plateId, id.Value, x, y, z, seeds, out bool isOceanic);

            Assert.Equal(expectedOceanic, isOceanic);
            Assert.Equal(expectedElevation, elevation);
            checkedCount++;
        }

        Assert.Equal(400, checkedCount);
    }
}

public class PlateBoundaryEffectPlausibilityTests
{
    [Fact]
    public void BoundaryZoneCoversAPlausibleFraction()
    {
        const ulong worldSeed = 0xA7C944210000UL;
        const int plateCount = 12;
        const int level = 6;
        var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        uint n = 1u << level;
        int total = 0, affected = 0;
        double maxUplift = 0.0;

        for (int face = 0; face <= 5; face++)
        {
            for (uint u = 0; u < n; u += 2)
            {
                for (uint v = 0; v < n; v += 2)
                {
                    TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                    TileGeometry.ToPosition(id, out double x, out double y, out double z);
                    double uplift = PlateBoundaryEffect.BoundaryUplift(x, y, z, seeds);
                    total++;
                    if (uplift > 0.0)
                    {
                        affected++;
                        maxUplift = Math.Max(maxUplift, uplift);
                    }
                }
            }
        }

        double fraction = affected / (double)total;
        Assert.True(fraction > 0.05 && fraction < 0.35,
            $"A határ-hatás zónája túl szűk vagy túl széles: {fraction:P1}");
        Assert.True(Math.Abs(maxUplift - PlateBoundaryEffect.DefaultUpliftMaxMeters) < 1.0,
            $"A max uplift-nak kb. a plafonértéknek kell lennie a határon: {maxUplift}");
    }

    [Fact]
    public void UpliftIsAlwaysNonNegative()
    {
        var seeds = PlateGeneration.GenerateSeeds(1UL, 10);
        for (int i = 0; i < 2000; i++)
        {
            TileId id = TileId.FromFaceLevelUV(i % 6, 6, (uint)(i * 3 % 64), (uint)(i * 5 % 64));
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double uplift = PlateBoundaryEffect.BoundaryUplift(x, y, z, seeds);
            Assert.True(uplift >= 0.0);
        }
    }
}

public class PlateBoundaryEffectEdgeCaseTests
{
    [Fact]
    public void ExactSeedPositionGetsZeroUpliftWhenWellSeparated()
    {
        // Szintetikus, KEZZEL KONTROLLALT magpontok (nem a valodi
        // lemez-generalasbol) - igy a teszt nem fugg attol, hogy egy adott
        // veletlen seed eppen mennyire szori szet a magokat. Egy korabbi
        // verzio PlateGeneration.GenerateSeeds(5UL, 8)-at hasznalt, de ott
        // veletlenul ket mag nagyon kozel esett egymashoz (gap=0.00157) -
        // ez hamis bukast okozott volna, mert az allitas ("a sajat mag
        // pontjaban nincs kozeli masodik lemez") nem univerzalisan igaz
        // veletlen pontokra, csak jol szetszort pontokra.
        var seeds = new (double X, double Y, double Z)[]
        {
            (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
            (-1.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, -1.0),
        };
        double uplift = PlateBoundaryEffect.BoundaryUplift(
            seeds[0].X, seeds[0].Y, seeds[0].Z, seeds);
        Assert.Equal(0.0, uplift);
    }

    [Fact]
    public void SinglePlateAlwaysGetsZeroUplift()
    {
        // 1 lemeznel nincs "masodik legkozelebbi", a gap vegtelen -> mindig 0.
        var seeds = PlateGeneration.GenerateSeeds(5UL, 1);
        double uplift = PlateBoundaryEffect.BoundaryUplift(0.5, 0.5, 0.5, seeds);
        Assert.Equal(0.0, uplift);
    }

    [Fact]
    public void IsPure()
    {
        var seeds = PlateGeneration.GenerateSeeds(9UL, 12);
        double a = PlateBoundaryEffect.BoundaryUplift(0.3, 0.4, 0.5, seeds);
        double b = PlateBoundaryEffect.BoundaryUplift(0.3, 0.4, 0.5, seeds);
        Assert.Equal(a, b);
    }
}
