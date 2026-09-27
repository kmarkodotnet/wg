using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGen.Core.Climate;
using WorldGen.Core.Features;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Features;

/// <summary>
/// ND-127 + ND-152 - a vizgyujtok osszevonasa foldrajzilag osszetartozo
/// regiokba (<see cref="FeatureSegmentation.MergeWatershedsIntoRegions"/>).
///
/// Az ND-152 (A10) ota a PRODUKCIOS ut a HIBRID: a partner-valasztas
/// sulyozott hatarhosszon dol el (biome-klaszter + domborzati tores). Az
/// itteni <c>Merge</c> segedfuggveny ezt hivja, tehat minden ND-127-es
/// garancia-teszt a hibrid uton fut; a tisztan geometriai utat a vegen
/// kulon tesztek meresek melle allitjak.
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
        public Dictionary<TileId, Biome> BiomeOf = new();
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
            BiomeOf = BuildBiomeField(field, isOcean, seaLevel),
            LandTileCount = isOcean.Count(kv => !kv.Value),
        };
    }

    /// <summary>
    /// UGYANAZ a biome-proxy, mint az <c>AreaPartitioningTests</c>-ben es a
    /// Python orakulumban (<c>features_ref.py</c>): a magassag a csapadek
    /// PROXY-ja. Ennek a tesztnek a targya a SZEGMENTALAS, nem a klima,
    /// ezert nem futtatunk teljes nedvesseg-transzportot - de a ket oldal
    /// csak akkor osszemerheto, ha ugyanezt a proxyt hasznalja.
    /// </summary>
    private static Dictionary<TileId, Biome> BuildBiomeField(
        Dictionary<TileId, double> field, Dictionary<TileId, bool> isOcean, double seaLevel)
    {
        var landPrecipProxy = new List<double>();
        foreach (TileId id in field.Keys)
            if (!isOcean[id]) landPrecipProxy.Add(field[id]);
        BiomeClassification.PrecipitationThresholds thresholds =
            BiomeClassification.ComputeThresholds(landPrecipProxy);

        var biomeOf = new Dictionary<TileId, Biome>(field.Count);
        foreach (TileId id in field.Keys)
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double tK = Temperature.TemperatureKelvin(
                x, y, z, 0.0, 365.25, 1.0, 23.44 * Math.PI / 180.0,
                isOcean[id], field[id], seaLevel);
            biomeOf[id] = BiomeClassification.Classify(tK, isOcean[id], field[id], thresholds);
        }
        return biomeOf;
    }

    /// <summary>A PRODUKCIOS ut: hibrid osszevonas (ND-152).</summary>
    private static List<List<TileId>> Merge(World w, int target) =>
        FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target, w.BiomeOf, w.Field);

    /// <summary>
    /// Az ND-127 tisztan geometriai utja - osszehasonlitasi alap. A ket
    /// <c>null</c> szandekosan KIIRT: a hibrid tagok elhagyasa itt dontes,
    /// nem az alapertelmezes veletlen atvetele.
    /// </summary>
    private static List<List<TileId>> MergeGeometric(World w, int target) =>
        FeatureSegmentation.MergeWatershedsIntoRegions(w.Watersheds, target, null, null);

    /// <summary>
    /// A regio-hatar elek kozul mennyi ul domborzati TORESEN, illetve
    /// biome-valtason. Ez a ket szam meri, hogy a hatarok termeszetes
    /// vonalat kovetnek-e. A tores-kuszob ugyanaz, amit az osszevonas
    /// hasznal: a cella-kozi gradiensek p75-e.
    /// </summary>
    private static (double breakShare, double biomeShare) BoundaryAlignment(
        World w, List<List<TileId>> merged, long breakThreshold)
    {
        var regionOf = new Dictionary<TileId, int>();
        for (int i = 0; i < merged.Count; i++)
            foreach (TileId t in merged[i]) regionOf[t] = i;

        int edges = 0, onBreak = 0, onBiomeChange = 0;
        foreach (KeyValuePair<TileId, int> kv in regionOf)
        {
            for (int d = 0; d < 4; d++)
            {
                TileId nb = TileNeighbors.Neighbor(kv.Key, (TileDirection)d);
                if (!regionOf.TryGetValue(nb, out int other) || other == kv.Value) continue;
                edges++;
                long a = (long)Math.Floor(w.Field[kv.Key] + 0.5);
                long b = (long)Math.Floor(w.Field[nb] + 0.5);
                if (Math.Abs(a - b) >= breakThreshold) onBreak++;
                if (w.BiomeOf[kv.Key] != w.BiomeOf[nb]) onBiomeChange++;
            }
        }
        Assert.True(edges > 0, "Nincs egyetlen regio-hatar el sem - a meres ertelmetlen.");
        return ((double)onBreak / edges, (double)onBiomeChange / edges);
    }

    /// <summary>
    /// A tores-kuszob ujraszamolasa a teszt oldalan, az osszevonassal
    /// AZONOS keplettel (cella-kozi elek magassag-gradienseinek p75-e).
    /// Szandekosan onallo implementacio: ha a Core-e elcsuszik, a
    /// hatar-illeszkedes merese is elmozdul.
    /// </summary>
    private static long BreakThreshold(World w)
    {
        var cellOf = new Dictionary<TileId, int>();
        int index = 0;
        foreach (TileId root in w.Watersheds.Keys.OrderBy(k => k.Value))
        {
            foreach (List<TileId> component in ConnectedComponents(w.Watersheds[root]))
            {
                foreach (TileId t in component) cellOf[t] = index;
                index++;
            }
        }

        var gradients = new List<long>();
        foreach (KeyValuePair<TileId, int> kv in cellOf)
        {
            for (int d = 0; d < 4; d++)
            {
                TileId nb = TileNeighbors.Neighbor(kv.Key, (TileDirection)d);
                if (!cellOf.TryGetValue(nb, out int other) || other == kv.Value) continue;
                long a = (long)Math.Floor(w.Field[kv.Key] + 0.5);
                long b = (long)Math.Floor(w.Field[nb] + 0.5);
                gradients.Add(Math.Abs(a - b));
            }
        }
        gradients.Sort();
        int idx = (int)(FeatureSegmentation.RegionMergeTerrainBreakPercentile * gradients.Count);
        if (idx < 0) idx = 0;
        if (idx > gradients.Count - 1) idx = gradients.Count - 1;
        return gradients[idx];
    }

    private static List<List<TileId>> ConnectedComponents(List<TileId> tiles)
    {
        var set = new HashSet<TileId>(tiles);
        var seen = new HashSet<TileId>();
        var components = new List<List<TileId>>();
        foreach (TileId start in tiles)
        {
            if (!seen.Add(start)) continue;
            var component = new List<TileId> { start };
            var queue = new Queue<TileId>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                TileId t = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                    if (set.Contains(nb) && seen.Add(nb)) { component.Add(nb); queue.Enqueue(nb); }
                }
            }
            components.Add(component);
        }
        return components;
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

        List<List<TileId>> merged = Merge(w, expectedTarget);

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
        List<List<TileId>> merged = Merge(w, target);

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
        List<List<TileId>> merged = Merge(w, target);

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
        List<List<TileId>> merged = Merge(w, target);

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
        List<List<TileId>> merged = Merge(w, target);

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
        List<List<TileId>> merged = Merge(w, target);

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
        List<List<TileId>> a = Merge(w, target);
        List<List<TileId>> b = Merge(w, target);

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
        List<List<TileId>> expected = Merge(w, target);

        // Forditott beszurasi sorrend + forditott tile-sorrend a listakban.
        var reversed = new Dictionary<TileId, List<TileId>>();
        foreach (TileId key in w.Watersheds.Keys.OrderByDescending(k => k.Value))
        {
            var tiles = new List<TileId>(w.Watersheds[key]);
            tiles.Reverse();
            reversed[key] = tiles;
        }

        List<List<TileId>> actual = FeatureSegmentation.MergeWatershedsIntoRegions(reversed, target, w.BiomeOf, w.Field);
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], actual[i]);
    }

    /// <summary>A cel-meret ERDEMBEN hat a kimenetre (nem "kimaradt parameter").</summary>
    [Fact]
    public void TargetSizeChangesTheResult()
    {
        World w = BuildWorld(level: 5);
        int small = Merge(w, 10).Count;
        int medium = Merge(w, 65).Count;
        int large = Merge(w, 400).Count;

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
        List<List<TileId>> singleResult = FeatureSegmentation.MergeWatershedsIntoRegions(single, 100, w.BiomeOf, w.Field);
        Assert.Single(singleResult);
        Assert.Single(singleResult[0]);

        // 0 vagy negativ cel: nincs mit elerni, minden cella onallo regio marad.
        int cellCount = Merge(w, 1).Count;
        Assert.Equal(cellCount, Merge(w, 0).Count);
        Assert.Equal(cellCount, Merge(w, -5).Count);
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
        List<List<TileId>> merged = Merge(w, target);
        Assert.Equal(w.LandTileCount, merged.Sum(r => r.Count));
        Assert.All(merged, r => Assert.True(IsConnected(r)));

        List<TileId> biggest = SeaLevelCalibration.CountContinents(w.Field, w.SeaLevel, minSize: 5)
            .OrderByDescending(c => c.Count).First();
        var biggestSet = new HashSet<TileId>(biggest);
        int regionsOnBiggest = merged.Count(r => biggestSet.Contains(r[0]));
        Assert.InRange(regionsOnBiggest, 2, 30);
    }

    // ---------------------------------------------------------------
    // ND-152 (A10) - az ND-05 hibrid maradek ket tagja
    // ---------------------------------------------------------------

    /// <summary>
    /// MINDEN PARAMETER ERDEMBEN HAT: a biome + elevacio atadasa MAS
    /// felosztast ad, mint a tisztan geometriai ut. Ha ez a teszt zold
    /// maradna egy olyan valtozat mellett, ami a ket uj tagot figyelmen
    /// kivul hagyja, akkor a tagok "kimaradt parameterek" lennenek.
    /// </summary>
    [Fact]
    public void HybridTermsChangeTheSegmentation()
    {
        World w = BuildWorld(level: 5);
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);

        List<List<TileId>> hybrid = Merge(w, target);
        List<List<TileId>> geometric = MergeGeometric(w, target);

        bool differs = hybrid.Count != geometric.Count;
        if (!differs)
            for (int i = 0; i < hybrid.Count && !differs; i++)
                differs = !hybrid[i].SequenceEqual(geometric[i]);
        Assert.True(differs, "A hibrid tagok nem valtoztattak semmit a felosztason.");
    }

    /// <summary>
    /// A hibrid tagok CSAK EGYUTT lepnek be: ha a hivo csak a biome-ot vagy
    /// csak az elevaciot adja meg, az ND-127 tisztan geometriai utja fut -
    /// nem egy fel hibrid, csendben. Ez szandekos, es a hatasa bitpontos.
    /// </summary>
    [Fact]
    public void PartialHybridInputFallsBackToTheGeometricPath()
    {
        World w = BuildWorld(level: 5);
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<List<TileId>> geometric = MergeGeometric(w, target);

        List<List<TileId>> biomeOnly = FeatureSegmentation.MergeWatershedsIntoRegions(
            w.Watersheds, target, w.BiomeOf, null);
        List<List<TileId>> elevationOnly = FeatureSegmentation.MergeWatershedsIntoRegions(
            w.Watersheds, target, null, w.Field);

        Assert.Equal(geometric.Count, biomeOnly.Count);
        Assert.Equal(geometric.Count, elevationOnly.Count);
        for (int i = 0; i < geometric.Count; i++)
        {
            Assert.Equal(geometric[i], biomeOnly[i]);
            Assert.Equal(geometric[i], elevationOnly[i]);
        }
    }

    /// <summary>
    /// A MERT javulas rogzitese (ND-152): a regio-hatarok TERMESZETES
    /// vonalakat kovetnek. A tores-kuszob definicioja szerint a cella-kozi
    /// elek 25%-a tores, tehat a "vaktalanul huzott hatar" varhato erteke
    /// ~25% - a geometriai ut ehhez kepest NEM javit (a merese 21%), a
    /// hibrid viszont ketszerezi (43%). Ugyanez a biome-valtasra:
    /// 25% -> 41%.
    ///
    /// A kuszobok szandekosan nem a mert szamok: a teszt azt rogziti, hogy
    /// a hibrid ERDEMBEN jobban illeszkedik, nem azt, hogy pontosan 43,2%.
    /// </summary>
    [Fact]
    public void HybridBoundariesFollowTerrainBreaksAndBiomeChanges()
    {
        World w = BuildWorld();
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        long breakThreshold = BreakThreshold(w);

        (double geoBreak, double geoBiome) = BoundaryAlignment(w, MergeGeometric(w, target), breakThreshold);
        (double hybBreak, double hybBiome) = BoundaryAlignment(w, Merge(w, target), breakThreshold);

        Assert.True(hybBreak > geoBreak * 1.5,
            $"A hibrid hatarok nem ulnek erdemben tobbszor toresen ({hybBreak:P1} vs {geoBreak:P1}).");
        Assert.True(hybBiome > geoBiome * 1.3,
            $"A hibrid hatarok nem kovetik erdemben jobban a biome-valtast ({hybBiome:P1} vs {geoBiome:P1}).");
        Assert.True(hybBreak > 0.35, $"A tores-illeszkedes a vart savon kivul: {hybBreak:P1}.");
        Assert.True(hybBiome > 0.30, $"A biome-illeszkedes a vart savon kivul: {hybBiome:P1}.");
    }

    /// <summary>
    /// A KOMPAKTSAG NEM ROMLIK. Az ND-127 (b) szabalya pont azert lett a
    /// kozos hatar hossza, mert a naiv valtozat elnyulo regiokat epitett; a
    /// sulyozas nem dobhatja el ezt a nyereseget. Mert atlag: 2,42 ->
    /// 2,39 (atmero/gyok-terulet, a legnagyobb landmasson).
    /// </summary>
    [Fact]
    public void HybridDoesNotMakeRegionsStringy()
    {
        World w = BuildWorld();
        int target = FeatureSegmentation.RecommendedRegionTileTarget(w.LandTileCount);
        List<TileId> biggest = SeaLevelCalibration.CountContinents(w.Field, w.SeaLevel, minSize: 5)
            .OrderByDescending(c => c.Count).First();
        var biggestSet = new HashSet<TileId>(biggest);

        double MeanShapeIndex(List<List<TileId>> merged)
        {
            var values = new List<double>();
            foreach (List<TileId> region in merged)
            {
                if (!biggestSet.Contains(region[0])) continue;
                values.Add(Diameter(region) / Math.Sqrt(region.Count));
            }
            Assert.NotEmpty(values);
            return values.Average();
        }

        double geo = MeanShapeIndex(MergeGeometric(w, target));
        double hyb = MeanShapeIndex(Merge(w, target));
        Assert.True(hyb <= geo * 1.15,
            $"A hibrid regiok erdemben nyulvanyosabbak lettek ({hyb:F2} vs {geo:F2}).");
    }

    /// <summary>Ket BFS - racson jo kozelites a halmaz atmerojere.</summary>
    private static int Diameter(List<TileId> tiles)
    {
        var set = new HashSet<TileId>(tiles);
        TileId Far(TileId from, out int distance)
        {
            var dist = new Dictionary<TileId, int> { [from] = 0 };
            var queue = new Queue<TileId>();
            queue.Enqueue(from);
            TileId last = from;
            while (queue.Count > 0)
            {
                TileId t = queue.Dequeue();
                last = t;
                for (int d = 0; d < 4; d++)
                {
                    TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                    if (set.Contains(nb) && !dist.ContainsKey(nb)) { dist[nb] = dist[t] + 1; queue.Enqueue(nb); }
                }
            }
            distance = dist[last];
            return last;
        }
        TileId a = Far(tiles[0], out _);
        Far(a, out int diameter);
        return diameter + 1;
    }

    /// <summary>
    /// A TORES-KUSZOB RELATIV, nem fix meter: level 5-on es level 6-on is
    /// az elek ~negyede szamit toresnek. Ha valaha fix meter-kuszobre
    /// cserelnenk, a ket szint elcsuszna egymastol - es az ND-127
    /// szintfuggetlensegi celja borulna.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void TerrainBreakThresholdIsRelativeAtEveryLevel(int level)
    {
        World w = BuildWorld(level);
        long threshold = BreakThreshold(w);
        Assert.True(threshold > 0, "A tores-kuszob nulla - minden el toresnek szamitana.");

        int edges = 0, breaks = 0;
        var cellTiles = new HashSet<TileId>();
        foreach (List<TileId> tiles in w.Watersheds.Values)
            foreach (TileId t in tiles) cellTiles.Add(t);
        foreach (TileId t in cellTiles)
        {
            for (int d = 0; d < 4; d++)
            {
                TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                if (!cellTiles.Contains(nb)) continue;
                edges++;
                long a = (long)Math.Floor(w.Field[t] + 0.5);
                long b = (long)Math.Floor(w.Field[nb] + 0.5);
                if (Math.Abs(a - b) >= threshold) breaks++;
            }
        }
        Assert.InRange((double)breaks / edges, 0.05, 0.45);
    }

    /// <summary>
    /// AZ ELSULY HATARAI: a legkisebb lehetseges suly POZITIV. Ha nulla
    /// vagy negativ lenne, egy biome-valto tores-hatar eltuntetne a
    /// szomszedsagot, es egy cella cel alatti meretben beragadna - a
    /// "meretpadlo" teszt pont ezt fogna meg, de a konstansok viszonyat
    /// erdemes kulon is lerogziteni.
    /// </summary>
    [Fact]
    public void EdgeWeightStaysPositiveInTheWorstCase()
    {
        int worst = FeatureSegmentation.RegionMergeBaseEdgeWeight
            - FeatureSegmentation.RegionMergeTerrainBreakPenalty;
        Assert.True(worst > 0, $"A legrosszabb eseti elsuly nem pozitiv ({worst}).");

        // A ket hibrid tag azonos nagysagu - egyik sem dominal a masikon.
        Assert.Equal(FeatureSegmentation.RegionMergeTerrainBreakPenalty,
            FeatureSegmentation.RegionMergeSameBiomeBonus);

        // A geometria marad a vezeto jel: 2 tile-os legjobb hatar (2*7) meg
        // mindig veszit egy 4 tile-os legrosszabb hatarral (4*1... de a
        // tores nem feltetlen all fenn) szemben - a konkret ellenorzes:
        int best = FeatureSegmentation.RegionMergeBaseEdgeWeight
            + FeatureSegmentation.RegionMergeSameBiomeBonus;
        Assert.True(2 * best < 4 * FeatureSegmentation.RegionMergeBaseEdgeWeight,
            "A hibrid bonusz elnyomja a hatarhossz-jelet.");
    }
}
