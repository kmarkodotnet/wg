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
    private const ulong WorldSeed = 0xA7C944210000UL;

    [Fact]
    public void BoundaryZoneCoversAPlausibleFraction()
    {
        const int plateCount = 12;
        const int level = 6;
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, plateCount);

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
                    double uplift = PlateBoundaryEffect.BoundaryUplift(WorldSeed, x, y, z, seeds);
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
        // A max uplift most mar kereg-tipus-fuggo lehet (ND-32) - ha a
        // legkozelebbi hatar epp oceani-oceani, a max ertek a csokkentett
        // sapkat (DefaultUpliftMaxMeters * DefaultOceanicOceanicUpliftFactor)
        // is elerhetne, ezert csak azt varjuk el, hogy a plafonertek EGYIK
        // dokumentalt sapkanal se legyen magasabb.
        Assert.True(maxUplift <= PlateBoundaryEffect.DefaultUpliftMaxMeters + 1.0,
            $"A max uplift nem lehet nagyobb a plafonértéknél: {maxUplift}");
    }

    [Fact]
    public void UpliftIsAlwaysNonNegative()
    {
        var seeds = PlateGeneration.GenerateSeeds(1UL, 10);
        for (int i = 0; i < 2000; i++)
        {
            TileId id = TileId.FromFaceLevelUV(i % 6, 6, (uint)(i * 3 % 64), (uint)(i * 5 % 64));
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double uplift = PlateBoundaryEffect.BoundaryUplift(1UL, x, y, z, seeds);
            Assert.True(uplift >= 0.0);
        }
    }

    [Fact]
    public void OceanicOceanicBoundaryGetsReducedUplift()
    {
        // Ket szintetikus, KEZZEL kontrollalt, KOZELI mag - mindketto
        // oceani-e vagy sem az a worldSeed+plateId Bernoulli-probajatol
        // fugg, ezert addig probalunk seedeket, amig talalunk egy olyan
        // (worldSeed, plateId=0,1) part, ahol mindketto oceani, ES egy
        // masikat, ahol legalabb az egyik kontinentalis - igy direkt
        // osszehasonlithato ugyanazon gap mellett a ket eset uplift-je.
        var seeds = new (double X, double Y, double Z)[]
        {
            (1.0, 0.0, 0.0),
            (0.9999, 0.01414, 0.0), // nagyon kozeli masodik mag -> kis gap -> kozel max uplift
        };
        // Normalizalas
        for (int i = 0; i < seeds.Length; i++)
        {
            double len = Math.Sqrt(seeds[i].X * seeds[i].X + seeds[i].Y * seeds[i].Y + seeds[i].Z * seeds[i].Z);
            seeds[i] = (seeds[i].X / len, seeds[i].Y / len, seeds[i].Z / len);
        }

        ulong oceanicOceanicSeed = 0;
        ulong mixedSeed = 0;
        for (ulong candidate = 1; candidate < 100000; candidate++)
        {
            bool p0Oceanic = CrustElevation.IsOceanic(candidate, 0);
            bool p1Oceanic = CrustElevation.IsOceanic(candidate, 1);
            if (p0Oceanic && p1Oceanic && oceanicOceanicSeed == 0)
                oceanicOceanicSeed = candidate;
            if (!(p0Oceanic && p1Oceanic) && mixedSeed == 0)
                mixedSeed = candidate;
            if (oceanicOceanicSeed != 0 && mixedSeed != 0)
                break;
        }
        Assert.True(oceanicOceanicSeed != 0 && mixedSeed != 0, "Nem találtunk megfelelő teszt-seedeket");

        double upliftOceanicOceanic = PlateBoundaryEffect.BoundaryUplift(
            oceanicOceanicSeed, seeds[0].X, seeds[0].Y, seeds[0].Z, seeds);
        double upliftMixed = PlateBoundaryEffect.BoundaryUplift(
            mixedSeed, seeds[0].X, seeds[0].Y, seeds[0].Z, seeds);

        Assert.True(upliftOceanicOceanic < upliftMixed,
            $"Óceáni-óceáni uplift ({upliftOceanicOceanic}) nem kisebb, mint a kontinentálist is érintő ({upliftMixed})");
    }
}

public class PlateBoundaryEffectEdgeCaseTests
{
    [Fact]
    public void MixedCrustBoundaryBaseIsContinuousFromBothSides()
    {
        ulong worldSeed = FindMixedCrustSeed();
        const double best = 0.75;
        const double boundaryEpsilon = 1e-9;
        const double primaryNoise = 0.2;
        const double mountainMask = 0.6;
        const double secondaryNoise = -0.15;

        double fromPlateZero = CrustElevation.BlendedBaseElevationFromNoiseBasis(
            worldSeed, best + boundaryEpsilon, best, 0, 1,
            primaryNoise, mountainMask, secondaryNoise, out bool zeroOceanic);
        double fromPlateOne = CrustElevation.BlendedBaseElevationFromNoiseBasis(
            worldSeed, best + boundaryEpsilon, best, 1, 0,
            primaryNoise, mountainMask, secondaryNoise, out bool oneOceanic);

        Assert.NotEqual(zeroOceanic, oneOceanic);
        Assert.InRange(Math.Abs(fromPlateZero - fromPlateOne), 0.0, 1e-8);
    }

    [Fact]
    public void MixedCrustBlendIsExactlyInactiveOutsideBoundaryZone()
    {
        ulong worldSeed = FindMixedCrustSeed();
        const double primaryNoise = 0.2;
        const double mountainMask = 0.6;
        const double secondaryNoise = -0.15;
        double expected = CrustElevation.BaseElevationFromNoiseBasis(
            worldSeed, 0, primaryNoise, mountainMask, secondaryNoise, out bool expectedOceanic);

        double actual = CrustElevation.BlendedBaseElevationFromNoiseBasis(
            worldSeed, 0.8, 0.8 - CrustElevation.DefaultBoundaryBlendGap,
            0, 1, primaryNoise, mountainMask, secondaryNoise, out bool actualOceanic);

        Assert.Equal(expectedOceanic, actualOceanic);
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }

    [Fact]
    public void MixedCrustBoundaryUpliftNeverExceedsOneKilometer()
    {
        ulong worldSeed = FindMixedCrustSeed();

        double uplift = PlateBoundaryEffect.BoundaryUpliftFromNearestPlates(
            worldSeed, 0.75, 0.75, 0, 1, mountainMask: 1.0);

        Assert.Equal(1000.0, PlateBoundaryEffect.DefaultUpliftMaxMeters);
        Assert.Equal(1000.0, uplift);
    }

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
            1UL, seeds[0].X, seeds[0].Y, seeds[0].Z, seeds);
        Assert.Equal(0.0, uplift);
    }

    [Fact]
    public void SinglePlateAlwaysGetsZeroUplift()
    {
        // 1 lemeznel nincs "masodik legkozelebbi", a gap vegtelen -> mindig 0.
        var seeds = PlateGeneration.GenerateSeeds(5UL, 1);
        double uplift = PlateBoundaryEffect.BoundaryUplift(5UL, 0.5, 0.5, 0.5, seeds);
        Assert.Equal(0.0, uplift);
    }

    [Fact]
    public void IsPure()
    {
        var seeds = PlateGeneration.GenerateSeeds(9UL, 12);
        double a = PlateBoundaryEffect.BoundaryUplift(9UL, 0.3, 0.4, 0.5, seeds);
        double b = PlateBoundaryEffect.BoundaryUplift(9UL, 0.3, 0.4, 0.5, seeds);
        Assert.Equal(a, b);
    }

    private static ulong FindMixedCrustSeed()
    {
        for (ulong candidate = 1; candidate < 100000; candidate++)
        {
            if (CrustElevation.IsOceanic(candidate, 0) != CrustElevation.IsOceanic(candidate, 1))
                return candidate;
        }

        throw new InvalidOperationException("Nem találtunk vegyes kéregtípusú determinisztikus seedet.");
    }
}
