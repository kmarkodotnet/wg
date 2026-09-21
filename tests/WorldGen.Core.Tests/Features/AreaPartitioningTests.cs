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
/// Negyedik panelszint ("Terulet"/Area, docs/backlog.md "Navigációs menü és
/// panel-elrendezés" + docs/01-architecture.md §12):
/// <see cref="FeatureSegmentation.PartitionRegionIntoAreas"/>.
/// </summary>
public class AreaPartitioningVectorFileTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;

    private static int CompareTileByFaceUV(TileId a, TileId b)
    {
        if (a.Face != b.Face) return a.Face.CompareTo(b.Face);
        a.GetUV(out uint au, out uint av);
        b.GetUV(out uint bu, out uint bv);
        if (au != bu) return au.CompareTo(bu);
        return av.CompareTo(bv);
    }

    /// <summary>BITPONTOS egyezés várt - csak egész aritmetika, nincs transzcendens függvény.</summary>
    [Fact]
    public void MatchesPythonReferenceAreasExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "features_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        int targetAreaTileCount = root.GetProperty("targetAreaTileCount").GetInt32();

        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);

        var landPrecipProxy = new List<double>();
        foreach (TileId id in field.Keys)
            if (!isOcean[id]) landPrecipProxy.Add(field[id]);
        BiomeClassification.PrecipitationThresholds biomeThresholds =
            BiomeClassification.ComputeThresholds(landPrecipProxy);

        var biomeOf = new Dictionary<TileId, Biome>();
        foreach (TileId id in field.Keys)
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double tK = Temperature.TemperatureKelvin(
                x, y, z, 0.0, 365.25, 1.0, 23.44 * Math.PI / 180.0,
                isOcean[id], field[id], seaLevel);
            // ND-126: a Classify csapadekot is kap. Ennek a tesztnek a targya a
            // SZEGMENTALAS, nem a klima, ezert nem futtatunk teljes
            // nedvesseg-transzportot - a magassagot hasznaljuk olcso,
            // determinisztikus csapadek-PROXY-kent. Csak annyi kell tole,
            // hogy a szarazfoldon legyen tobbfele biome.
            biomeOf[id] = BiomeClassification.Classify(
                tK, isOcean[id], field[id], biomeThresholds);
        }

        Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);
        List<TileId> sizedRootsSorted = regions.Keys
            .Where(r => regions[r].Count >= 5)
            .OrderByDescending(r => regions[r].Count)
            .ThenBy(r => r, Comparer<TileId>.Create(CompareTileByFaceUV))
            .ToList();

        JsonElement areasByRegion = root.GetProperty("areasByRegion");
        int checkedRegions = 0;
        for (int regionIndex = 0; regionIndex < areasByRegion.GetArrayLength(); regionIndex++)
        {
            JsonElement expRegion = areasByRegion[regionIndex];
            List<TileId> tiles = regions[sizedRootsSorted[regionIndex]];
            Assert.Equal(expRegion.GetProperty("regionTileCount").GetInt32(), tiles.Count);

            List<List<TileId>> areas = FeatureSegmentation.PartitionRegionIntoAreas(tiles, targetAreaTileCount);
            JsonElement expAreas = expRegion.GetProperty("areas");
            Assert.Equal(expAreas.GetArrayLength(), areas.Count);

            for (int areaIndex = 0; areaIndex < areas.Count; areaIndex++)
            {
                JsonElement expArea = expAreas[areaIndex];
                List<TileId> area = areas[areaIndex];
                Biome dominant = FeatureSegmentation.DominantBiome(area, biomeOf);
                ulong featureId = (ulong)(20000 + regionIndex * 1000 + areaIndex);
                string name = NameGeneration.GenerateName(WorldSeed, featureId, dominant.ToString());

                Assert.Equal(expArea.GetProperty("name").GetString(), name);
                Assert.Equal(expArea.GetProperty("tileCount").GetInt32(), area.Count);
                Assert.Equal(expArea.GetProperty("dominantBiome").GetString(), dominant.ToString());

                ulong[] expectedMembers = expArea.GetProperty("memberTileIds")
                    .EnumerateArray().Select(e => e.GetUInt64()).OrderBy(v => v).ToArray();
                ulong[] gotMembers = area.Select(t => t.Value).OrderBy(v => v).ToArray();
                Assert.Equal(expectedMembers, gotMembers);
            }
            checkedRegions++;
        }
        Assert.Equal(5, checkedRegions);
    }
}

public class AreaPartitioningStructuralTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;

    private static (Dictionary<TileId, double> field, Dictionary<TileId, bool> isOcean, FlowNetwork.FloodResult flood)
        BuildWorld()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);
        return (field, isOcean, flood);
    }

    [Fact]
    public void EmptyRegionGivesNoAreas()
    {
        List<List<TileId>> areas = FeatureSegmentation.PartitionRegionIntoAreas(new List<TileId>(), 40);
        Assert.Empty(areas);
    }

    [Fact]
    public void SingleTileRegionGivesOneAreaWithThatTile()
    {
        TileId t = TileId.FromFaceLevelUV(0, 5, 1, 1);
        List<List<TileId>> areas = FeatureSegmentation.PartitionRegionIntoAreas(new List<TileId> { t }, 40);
        Assert.Single(areas);
        Assert.Single(areas[0]);
        Assert.Equal(t, areas[0][0]);
    }

    [Fact]
    public void RegionAtOrBelowTargetSizeStaysOneArea()
    {
        var (field, isOcean, flood) = BuildWorld();
        Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);
        // Egy kicsi (<=40 tile), de meg is levo regio - trivialis eset.
        List<TileId> small = regions.Values.First(v => v.Count > 0 && v.Count <= 40);

        List<List<TileId>> areas = FeatureSegmentation.PartitionRegionIntoAreas(small, 40);

        Assert.Single(areas);
        Assert.Equal(small.Count, areas[0].Count);
        Assert.Equal(new HashSet<TileId>(small), new HashSet<TileId>(areas[0]));
    }

    [Fact]
    public void IsPureGivenSameInputTwice()
    {
        var (field, isOcean, flood) = BuildWorld();
        Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);
        List<TileId> big = regions.Values.OrderByDescending(v => v.Count).First();

        List<List<TileId>> a = FeatureSegmentation.PartitionRegionIntoAreas(big, 40);
        List<List<TileId>> b = FeatureSegmentation.PartitionRegionIntoAreas(big, 40);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public void TargetTileCountParameterChangesAreaCount()
    {
        var (field, isOcean, flood) = BuildWorld();
        Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);
        List<TileId> big = regions.Values.OrderByDescending(v => v.Count).First();
        Assert.True(big.Count > 80, "A teszthez egy legalabb 80 tile-os regio kell (a jelen vilagban ez adott).");

        List<List<TileId>> coarse = FeatureSegmentation.PartitionRegionIntoAreas(big, 200);
        List<List<TileId>> fine = FeatureSegmentation.PartitionRegionIntoAreas(big, 20);

        Assert.True(fine.Count > coarse.Count, "Kisebb celmeret tobb teruletet kell adjon.");
    }

    /// <summary>
    /// Minden nagyobb (>=5 tile) régióra: a területek PONTOSAN, átfedés
    /// nélkül lefedik a régió tile-jait, és minden terület a SAJÁT
    /// tile-halmazán belüli 4-szomszédsági gráfban ÖSSZEFÜGGŐ.
    /// </summary>
    [Fact]
    public void AreasExactlyPartitionEveryRegionAndAreEachContiguous()
    {
        var (field, isOcean, flood) = BuildWorld();
        Dictionary<TileId, List<TileId>> regions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);
        var sizedRegions = regions.Values.Where(v => v.Count >= 5).ToList();
        Assert.True(sizedRegions.Count > 50, "Plauzibilitasi also korlat - a jelen vilag 421 regiot ad.");

        foreach (List<TileId> region in sizedRegions)
        {
            List<List<TileId>> areas = FeatureSegmentation.PartitionRegionIntoAreas(region, 40);

            var covered = new HashSet<TileId>();
            foreach (List<TileId> area in areas)
            {
                foreach (TileId t in area)
                    Assert.True(covered.Add(t), $"A tile {t.Value} tobb teruletben is szerepel.");

                var areaSet = new HashSet<TileId>(area);
                var reached = new HashSet<TileId> { area[0] };
                var queue = new Queue<TileId>();
                queue.Enqueue(area[0]);
                while (queue.Count > 0)
                {
                    TileId t = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                        if (areaSet.Contains(nb) && reached.Add(nb)) queue.Enqueue(nb);
                    }
                }
                Assert.Equal(areaSet, reached);
            }
            Assert.Equal(new HashSet<TileId>(region), covered);
        }
    }

    /// <summary>
    /// Két, EGYMÁSTÓL TÁVOLI (nem szomszédos) tile ugyanabba a szintetikus
    /// "régióba" tartozik - ez csak akkor fordulhatna elő egy valódi
    /// vízgyűjtő-régióban, ha két külön szárazföld-darab folyik ugyanabba az
    /// óceán-kifolyásba (a régió DEFINÍCIÓ szerint nem garantáltan
    /// összefüggő). A particionálásnak KÜLÖN komponensként kell kezelnie
    /// mindkettőt, nem összemosnia egyetlen (hamisan "összefüggő") területbe.
    /// </summary>
    [Fact]
    public void DisconnectedRegionSplitsIntoSeparateComponents()
    {
        TileId farA = TileId.FromFaceLevelUV(0, 6, 2, 2);
        TileId farB = TileId.FromFaceLevelUV(3, 6, 40, 40); // masik lap, tavol

        List<List<TileId>> areas = FeatureSegmentation.PartitionRegionIntoAreas(
            new List<TileId> { farA, farB }, targetAreaTileCount: 40);

        Assert.Equal(2, areas.Count);
        var allMembers = areas.SelectMany(a => a).ToHashSet();
        Assert.Equal(new HashSet<TileId> { farA, farB }, allMembers);
    }

    /// <summary>
    /// HIERARCHIA-ROBUSZTUSSÁG (docs/backlog.md "Eltűnő Region/Area kis
    /// landmass esetén", 2026-09-13, 1. javítás): a Viewer-oldali
    /// `PlanetGridMesh.ComputeRegionPanelDataForContinent` fallback-logikáját
    /// (ld. ott a doksit) reprodukálja TISZTÁN Core-primitívekből - minden
    /// (≥5 tile-os, tehát navigálhatóként megjelenő) landmass-nak legalább
    /// egy "régió-szerű" (≥1 tile-os, összefüggő szárazföld a landmasson
    /// belül) tile-csoportja legyen, és minden ilyen csoportnak legalább
    /// egy területe (`PartitionRegionIntoAreas` sosem ad üres listát
    /// nem-üres bemenetre - ld. a többi teszt) - tehát a navigációs
    /// hierarchia SOHA nem futhat 0 régió/0 terület állapotba.
    ///
    /// ND-127 ÓTA a fallback biztonsági háló, nem napi útvonal: a panel-régió
    /// az ÖSSZEVONT (<see cref="FeatureSegmentation.MergeWatershedsIntoRegions"/>)
    /// régió, ami a szárazföld partíciója, tehát méretszűrő nélkül is minden
    /// landmassnak van régiója (lásd
    /// <c>WatershedMergeTests.EveryLandmassHasAtLeastOneMergedRegion</c>). Ez
    /// a teszt a NYERS vízgyűjtő-úton maradt - azt rögzíti, hogy a háló akkor
    /// is tart, ha a szegmentálás megint kihagyna tile-okat.
    /// </summary>
    [Fact]
    public void EveryLandmassHasAtLeastOneRegionAfterFallback()
    {
        var (field, isOcean, flood) = BuildWorld();
        Dictionary<TileId, List<TileId>> allRegions = FeatureSegmentation.FindWatershedRegions(flood.Parent, isOcean);
        var sizedRegions = allRegions.Where(kv => kv.Value.Count >= 5).ToDictionary(kv => kv.Key, kv => kv.Value);
        List<List<TileId>> continents = SeaLevelCalibration.CountContinents(field,
            SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction), minSize: 5);

        Assert.True(continents.Count > 10, "Plauzibilitasi also korlat - a jelen vilag 31 landmasst ad.");

        int fallbackCount = 0;
        foreach (List<TileId> continent in continents)
        {
            var continentTiles = new HashSet<TileId>(continent);

            // UGYANAZ a heurisztika, mint GetRegionGlobalIndicesForContinent:
            // egy regio az ELSO tile-ja alapjan tartozik egy landmasshoz.
            List<List<TileId>> matchingSizedRegions = sizedRegions.Values
                .Where(tiles => tiles.Count > 0 && continentTiles.Contains(tiles[0]))
                .ToList();

            List<TileId> effectiveRegionTiles;
            if (matchingSizedRegions.Count > 0)
            {
                effectiveRegionTiles = matchingSizedRegions[0];
            }
            else
            {
                // Fallback: az EGESZ landmass, mint a Viewer-oldali kodban.
                fallbackCount++;
                effectiveRegionTiles = continent;
            }

            Assert.NotEmpty(effectiveRegionTiles);
            List<List<TileId>> areas = FeatureSegmentation.PartitionRegionIntoAreas(effectiveRegionTiles, 40);
            Assert.NotEmpty(areas);
        }

        // Plauzibilitasi also korlat: a jelen vilagban tobb kis (<20 tile-os)
        // landmass van, aminek MINDEN vizgyujtoje 5 tile alatt van - ha ez
        // 0-ra esne vissza egy jovobeli valtoztatas utan, ez a teszt akkor
        // sem bukna meg (a fallback-ag ettol meg helyes maradna), de ez a
        // szamlalo dokumentalja, hogy a teszt TENYLEGESEN gyakorolja a
        // fallback-agat, nem csak a trivialis esetet.
        Assert.True(fallbackCount > 0, "A teszt nem gyakorolta a fallback-agat - a szintetikus vilag megvaltozott?");
    }

    [Fact]
    public void ZeroOrNegativeTargetTileCountFallsBackToSingleArea()
    {
        TileId a = TileId.FromFaceLevelUV(0, 5, 1, 1);
        TileId b = TileNeighbors.Neighbor(a, TileDirection.Right);
        List<TileId> tiles = new List<TileId> { a, b };

        List<List<TileId>> zero = FeatureSegmentation.PartitionRegionIntoAreas(tiles, targetAreaTileCount: 0);
        List<List<TileId>> negative = FeatureSegmentation.PartitionRegionIntoAreas(tiles, targetAreaTileCount: -5);

        Assert.Single(zero);
        Assert.Single(negative);
    }
}
