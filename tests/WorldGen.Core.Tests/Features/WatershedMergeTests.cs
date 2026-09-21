using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Features;

/// <summary>
/// ND-127 - a vizgyujtok osszevonasa foldrajzilag osszetartozo regiokba
/// (<see cref="FeatureSegmentation.MergeWatershedsIntoRegions"/>).
///
/// A teszt-vilag UGYANAZ, mint az <c>AreaPartitioningVectorFileTests</c>-e
/// (seed, lemezszam, level, vizarany), igy a Python-orakulum
/// <c>features_vectors.json</c>-ja mindkettonek kozos.
/// </summary>
public class WatershedMergeTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;

    private sealed class World
    {
        public Dictionary<TileId, double> Field = new();
        public Dictionary<TileId, bool> IsOcean = new();
        public Dictionary<TileId, List<TileId>> Watersheds = new();
        public double SeaLevel;
        public int LandTileCount;
    }

    private static World BuildWorld(int level = Level)
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);
        return new World
        {
            Field = field,
            IsOcean = isOcean,
            SeaLevel = seaLevel,
            Watersheds = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean),
            LandTileCount = isOcean.Count(kv => !kv.Value),
        };
    }

    private static bool IsConnected(List<TileId> tiles)
    {
        var set = new HashSet<TileId>(tiles);
        var seen = new HashSet<TileId> { tiles[0] };
        var queue = new Queue<TileId>();
        queue.Enqueue(tiles[0]);
        while (queue.Count > 0)
        {
            TileId t = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                if (set.Contains(nb) && seen.Add(nb)) queue.Enqueue(nb);
            }
        }
        return seen.Count == set.Count;
    }

    /// <summary>
    /// ISMERT-VALASZ: bitpontos egyezes a Python-orakulummal. Csak egesz
    /// aritmetika van benne, tehat itt nincs tolerancia - a tile-halmazoknak
    /// ELEMROL ELEMRE egyeznie kell, a regiok sorrendjevel egyutt.
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceMergedRegionsExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "features_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        World w = BuildWorld();
        int expectedTarget = root.GetProperty("regionTargetTileCount").GetInt32();
        Assert.Equal(expectedTarget, FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount));

        List<List<TileId>> merged = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, expectedTarget);

        JsonElement expected = root.GetProperty("mergedRegions");
        Assert.Equal(expected.GetArrayLength(), merged.Count);
        for (int i = 0; i < merged.Count; i++)
        {
            JsonElement exp = expected[i];
            Assert.Equal(exp.GetProperty("tileCount").GetInt32(), merged[i].Count);
            Assert.Equal(exp.GetProperty("minTileId").GetUInt64(), merged[i][0].Value);

            JsonElement members = exp.GetProperty("memberTileIds");
            Assert.Equal(members.GetArrayLength(), merged[i].Count);
            for (int k = 0; k < merged[i].Count; k++)
                Assert.Equal(members[k].GetUInt64(), merged[i][k].Value);
        }
    }

    /// <summary>
    /// PARTICIO: a kimenet PONTOSAN a bemeneti vizgyujtok tile-jait fedi,
    /// atfedes nelkul. Ez az a garancia, ami miatt a panel-retegben eltunhet
    /// a ">=5 tile" szuro - az hagyta ki a szarazfold felet a navigaciobol.
    /// </summary>
    [Fact]
    public void MergedRegionsPartitionTheLandExactly()
    {
        World w = BuildWorld();
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> merged = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);

        var all = new List<TileId>();
        foreach (List<TileId> region in merged) all.AddRange(region);
        var expected = new HashSet<TileId>(w.Watersheds.Values.SelectMany(v => v));

        Assert.Equal(expected.Count, all.Count); // nincs atfedes
        Assert.Equal(expected, new HashSet<TileId>(all)); // nincs kimarado tile
        Assert.Equal(w.LandTileCount, all.Count); // a vizgyujtok a teljes szarazfoldet lefedik
    }

    /// <summary>TERBELI OSSZEFUGGOSEG: pont ez volt a panasz - egy regio tobb, egymastol elszakadt foldadarab volt.</summary>
    [Fact]
    public void EveryMergedRegionIsSpatiallyConnected()
    {
        World w = BuildWorld();
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> merged = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);

        Assert.All(merged, region => Assert.True(IsConnected(region),
            $"Egy osszevont regio ({region.Count} tile, min TileId {region[0].Value}) NEM osszefuggo."));
    }

    /// <summary>
    /// Egy regio sosem lep at landmass-hataron. Ez az osszefuggosegbol
    /// kovetkezik (ket kulon landmass definicio szerint nem szomszedos), de a
    /// navigacios menu EPIT ra (a kontinens -> regio hozzarendeles),
    /// ezert kulon is lerogzitjuk.
    /// </summary>
    [Fact]
    public void NoMergedRegionSpansTwoLandmasses()
    {
        World w = BuildWorld();
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> merged = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);

        var landmassOf = new Dictionary<TileId, int>();
        List<List<TileId>> landmasses = SeaLevelCalibration.CountContinents(w.Field, w.SeaLevel, minSize: 1);
        for (int i = 0; i < landmasses.Count; i++)
            foreach (TileId t in landmasses[i]) landmassOf[t] = i;

        foreach (List<TileId> region in merged)
        {
            int first = landmassOf[region[0]];
            Assert.All(region, t => Assert.Equal(first, landmassOf[t]));
        }
    }

    /// <summary>
    /// MERETPADLO: minden regio eleri a cel-meretet, KIVEVE amelyiknek nincs
    /// szomszedja (onallo kis sziget) - ott nincs mivel osszevonni.
    /// </summary>
    [Fact]
    public void EveryMergedRegionReachesTheTargetOrIsAnIsolatedLandmass()
    {
        World w = BuildWorld();
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> merged = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);

        var regionOf = new Dictionary<TileId, int>();
        for (int i = 0; i < merged.Count; i++)
            foreach (TileId t in merged[i]) regionOf[t] = i;

        foreach (List<TileId> region in merged)
        {
            if (region.Count >= target) continue;
            int index = regionOf[region[0]];
            bool hasNeighborRegion = region.Any(t =>
            {
                for (int d = 0; d < 4; d++)
                {
                    TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                    if (regionOf.TryGetValue(nb, out int other) && other != index) return true;
                }
                return false;
            });
            Assert.False(hasNeighborRegion,
                $"Egy cel alatti regio ({region.Count} < {target} tile) meg mindig osszevonhato lett volna.");
        }
    }

    /// <summary>
    /// NAVIGACIOS INVARIANS: minden megjeleno (>=5 tile-os) landmassnak van
    /// legalabb egy regioja - meretszuro nelkul, tehat a viewer fallback
    /// (egesz-landmass) aga mar nem napi utvonal. Korabban ez NEM allt: a
    /// >=5 tile-os szuro miatt egy kis landmass nulla regioval maradhatott.
    /// </summary>
    [Fact]
    public void EveryLandmassHasAtLeastOneMergedRegion()
    {
        World w = BuildWorld(level: 5);
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> merged = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);

        List<List<TileId>> landmasses = SeaLevelCalibration.CountContinents(w.Field, w.SeaLevel, minSize: 5);
        Assert.NotEmpty(landmasses);
        foreach (List<TileId> landmass in landmasses)
        {
            var set = new HashSet<TileId>(landmass);
            Assert.Contains(merged, r => set.Contains(r[0]));
        }
    }

    /// <summary>TISZTASAG: ugyanaz a bemenet -> bitre ugyanaz a kimenet.</summary>
    [Fact]
    public void MergeIsPure()
    {
        World w = BuildWorld();
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> a = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);
        List<List<TileId>> b = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i], b[i]);
    }

    /// <summary>
    /// SORRENDFUGGETLENSEG (I2): ugyanaz a vizgyujto-halmaz MAS beszurasi
    /// sorrendu szotarbol adva bitre ugyanazt a felosztast adja. A
    /// Dictionary bejarasi sorrendje a beszurasi sorrendtol fugg, tehat ez a
    /// teszt tenylegesen megfogna egy "elso nyer" jellegu dontest.
    /// </summary>
    [Fact]
    public void MergeIsIndependentOfInputDictionaryOrder()
    {
        World w = BuildWorld(level: 5);
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> expected = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);

        // Forditott beszurasi sorrend + forditott tile-sorrend a listakban.
        var reversed = new Dictionary<TileId, List<TileId>>();
        foreach (TileId key in w.Watersheds.Keys.OrderByDescending(k => k.Value))
        {
            var tiles = new List<TileId>(w.Watersheds[key]);
            tiles.Reverse();
            reversed[key] = tiles;
        }

        List<List<TileId>> actual = FeatureSegmentation.MergeWatershedsIntoRegions(reversed, target);
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], actual[i]);
    }

    /// <summary>A cel-meret ERDEMBEN hat a kimenetre (nem "kimaradt parameter").</summary>
    [Fact]
    public void TargetSizeChangesTheResult()
    {
        World w = BuildWorld(level: 5);
        int small = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, 10).Count;
        int medium = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, 65).Count;
        int large = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, 400).Count;

        Assert.True(small > medium, $"Kisebb cel-merethez tobb regio kell ({small} vs {medium}).");
        Assert.True(medium > large, $"Nagyobb cel-merethez kevesebb regio kell ({medium} vs {large}).");
    }

    /// <summary>ELESETEK: ures bemenet, egyetlen tile, 0/negativ cel-meret.</summary>
    [Fact]
    public void EdgeCases()
    {
        Assert.Empty(FeatureSegmentation.MergeWatershedsIntoRegions(
            new Dictionary<TileId, List<TileId>>(), 100));
        Assert.Empty(FeatureSegmentation.MergeWatershedsIntoRegions(null!, 100));

        World w = BuildWorld(level: 5);
        TileId firstRoot = w.Watersheds.Keys.OrderBy(k => k.Value).First();
        var single = new Dictionary<TileId, List<TileId>>
        {
            [firstRoot] = new List<TileId> { w.Watersheds[firstRoot][0] },
        };
        List<List<TileId>> singleResult = FeatureSegmentation.MergeWatershedsIntoRegions(single, 100);
        Assert.Single(singleResult);
        Assert.Single(singleResult[0]);

        // 0 vagy negativ cel: nincs mit elerni, minden cella onallo regio marad.
        int cellCount = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, 1).Count;
        Assert.Equal(cellCount, FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, 0).Count);
        Assert.Equal(cellCount, FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, -5).Count);
    }

    /// <summary>A cel-meret a szarazfold 3%-a, felezopont-kerekitessel, minimum 1.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(1, 1)]
    [InlineData(2151, 65)]   // level 5, TestEarth001
    [InlineData(8602, 258)]  // level 6, TestEarth001
    public void RecommendedTargetIsThreePercentOfLand(int landTiles, int expected)
    {
        Assert.Equal(expected, FeatureSegmentation.RecommendedRegionTileTarget(landTiles));
    }

    /// <summary>
    /// A MERT javulas rogzitese (ND-127) - ha valaki visszaallitja a nyers
    /// vizgyujto-szegmentalast, ez bukik el eloszor. A szamok a szegmentalas
    /// ALAPMERESEBOL jonnek, nem talalomra valasztott kuszobok.
    /// </summary>
    [Fact]
    public void MergeFixesTheMeasuredCoherenceDefects()
    {
        World w = BuildWorld(level: 5);

        // Elotte: a panel >=5 tile-os szuroje a szarazfold felet eldobta,
        // es a megmarado regiok tobb mint harmada terben szetesett.
        var sized = w.Watersheds.Values.Where(v => v.Count >= 5).ToList();
        int sizedTiles = sized.Sum(v => v.Count);
        Assert.True(sizedTiles < 0.6 * w.LandTileCount,
            $"A nyers vizgyujto-szegmentalas tobbet fed le, mint varnank ({sizedTiles}/{w.LandTileCount}).");
        Assert.Contains(sized, v => !IsConnected(v));

        // Utana: teljes lefedes, minden regio osszefuggo, es a legnagyobb
        // landmass regioszama egy szamjegyu marad (menuben hasznalhato).
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> merged = FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target);
        Assert.Equal(w.LandTileCount, merged.Sum(r => r.Count));
        Assert.All(merged, r => Assert.True(IsConnected(r)));

        List<TileId> biggest = SeaLevelCalibration.CountContinents(w.Field, w.SeaLevel, minSize: 5)
            .OrderByDescending(c => c.Count).First();
        var biggestSet = new HashSet<TileId>(biggest);
        int regionsOnBiggest = merged.Count(r => biggestSet.Contains(r[0]));
        Assert.InRange(regionsOnBiggest, 2, 30);
    }
}
