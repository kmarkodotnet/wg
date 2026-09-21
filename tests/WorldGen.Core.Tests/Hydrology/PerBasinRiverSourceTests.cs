using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Core.Tests.Hydrology;

/// <summary>
/// ND-124 (A): a VIZGYUJTONKENTI forras-kivalasztas
/// (<see cref="RiverPathTracing.SelectRiverSourcesPerBasin"/>) tesztjei.
///
/// MIERT KELL: a globalis "legcsapadekosabb top-K" a legnedvesebb
/// hegyvidekek KOZOTT szorja szet a forrasokat, ezert a nyomvonalak kulon
/// medencekben futnak a tengerig es ritkan talalkoznak - a dendritikus fa
/// sekely marad. A vizgyujtonkenti kvota EGY medencen belulre teszi a
/// forrasokat, igy kozos torkolat fele tartanak.
///
/// A szintetikus tesztek KEZZEL EPITETT folyas-szulo (floodParent) terkepet
/// hasznalnak, hogy a vizgyujto-szegmentalas kimenete pontosan ismert
/// legyen - nem fuggenek a priority-flood viselkedesetol.
/// </summary>
public class PerBasinRiverSourceTests
{
    private readonly ITestOutputHelper _out;
    public PerBasinRiverSourceTests(ITestOutputHelper o) { _out = o; }

    private const int Level = 5;

    /// <summary>
    /// Szintetikus vilag: ket folyo-lanc (kulon oceani torkolattal) + egy
    /// nagyon kicsi harmadik. Minden szarazfold-tile ugyanazon a soron
    /// fekszik, a szulo a szomszedos u-index fele mutat, a lanc vege egy
    /// ocean-tile.
    /// </summary>
    private static void BuildSyntheticWorld(
        int bigBasinTiles, int smallBasinTiles, int tinyBasinTiles,
        out Dictionary<TileId, double> elev, out Dictionary<TileId, double> precip,
        out Dictionary<TileId, bool> isOcean, out Dictionary<TileId, TileId?> parent,
        out List<TileId> bigChain, out List<TileId> smallChain)
    {
        var elevLocal = new Dictionary<TileId, double>();
        var precipLocal = new Dictionary<TileId, double>();
        var oceanLocal = new Dictionary<TileId, bool>();
        var parentLocal = new Dictionary<TileId, TileId?>();

        List<TileId> Chain(int face, uint v, int count, double precipBase)
        {
            var chain = new List<TileId>();
            for (int i = 0; i < count; i++)
            {
                TileId t = TileId.FromFaceLevelUV(face, Level, (uint)i, v);
                chain.Add(t);
                elevLocal[t] = 2000.0;
                // A csapadek a lanc elejen a legnagyobb, es monoton csokken -
                // igy a "legnedvesebb K" DETERMINISZTIKUSAN az elso K tile.
                precipLocal[t] = precipBase + (count - i);
                oceanLocal[t] = false;
            }
            TileId mouth = TileId.FromFaceLevelUV(face, Level, (uint)count, v);
            elevLocal[mouth] = -1000.0;
            precipLocal[mouth] = 0.0;
            oceanLocal[mouth] = true;
            parentLocal[mouth] = null;
            for (int i = 0; i < count; i++)
                parentLocal[chain[i]] = i + 1 < count ? chain[i + 1] : mouth;
            return chain;
        }

        bigChain = Chain(0, 0, bigBasinTiles, 100.0);
        smallChain = Chain(0, 1, smallBasinTiles, 50.0);
        Chain(0, 2, tinyBasinTiles, 10.0);

        elev = elevLocal;
        precip = precipLocal;
        isOcean = oceanLocal;
        parent = parentLocal;
    }

    [Fact]
    public void QuotaIsPerBasinAndBasinsAreOrderedBySize()
    {
        BuildSyntheticWorld(20, 12, 3, out var elev, out var precip, out var isOcean,
            out var parent, out List<TileId> big, out List<TileId> small);

        List<TileId> sources = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, seaLevel: 0.0,
            basinCount: 2, sourcesPerBasin: 3, minElevAboveSeaM: 300.0,
            minSeparationMeters: 0.0, minBasinTiles: 4);

        // 2 medence x 3 forras, es a NAGYOBB medence (20 tile) forrasai
        // allnak elol - a rendezes meret szerint csokkeno.
        Assert.Equal(6, sources.Count);
        for (int i = 0; i < 3; i++) Assert.Equal(big[i].Value, sources[i].Value);
        for (int i = 0; i < 3; i++) Assert.Equal(small[i].Value, sources[3 + i].Value);
    }

    [Fact]
    public void BasinsSmallerThanMinTilesAreSkipped()
    {
        BuildSyntheticWorld(20, 12, 3, out var elev, out var precip, out var isOcean,
            out var parent, out _, out _);

        // A harmadik lanc csak 3 tile - a 4-es kuszob alatt, tehat 3 medencet
        // kerve is csak 2-bol kapunk forrast.
        List<TileId> sources = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, seaLevel: 0.0,
            basinCount: 3, sourcesPerBasin: 2, minElevAboveSeaM: 300.0,
            minSeparationMeters: 0.0, minBasinTiles: 4);
        Assert.Equal(4, sources.Count);

        // Ha viszont a kuszobot leengedjuk, a harmadik medence is bejon.
        List<TileId> withTiny = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, seaLevel: 0.0,
            basinCount: 3, sourcesPerBasin: 2, minElevAboveSeaM: 300.0,
            minSeparationMeters: 0.0, minBasinTiles: 2);
        Assert.Equal(6, withTiny.Count);
    }

    [Fact]
    public void MinimumSeparationThinsOutNeighbouringSources()
    {
        BuildSyntheticWorld(20, 12, 3, out var elev, out var precip, out var isOcean,
            out var parent, out _, out _);

        List<TileId> dense = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, seaLevel: 0.0,
            basinCount: 1, sourcesPerBasin: 5, minElevAboveSeaM: 300.0,
            minSeparationMeters: 0.0, minBasinTiles: 4);
        Assert.Equal(5, dense.Count);

        // Level 5 tile-el kb. 313 km; 3000 km-es minimum tavolsaggal a
        // szomszedos jeloltek kiesnek, tehat KEVESEBB forras marad.
        List<TileId> sparse = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, seaLevel: 0.0,
            basinCount: 1, sourcesPerBasin: 5, minElevAboveSeaM: 300.0,
            minSeparationMeters: 3_000_000.0, minBasinTiles: 4);
        Assert.True(sparse.Count < dense.Count,
            "a minimalis tavolsag nem ritkitott: " + sparse.Count + " vs " + dense.Count);

        // ...es a megmaradt forrasok tenyleg tavolabb vannak egymastol.
        for (int i = 0; i < sparse.Count; i++)
        {
            TileGeometry.ToPosition(sparse[i], out double xi, out double yi, out double zi);
            for (int j = i + 1; j < sparse.Count; j++)
            {
                TileGeometry.ToPosition(sparse[j], out double xj, out double yj, out double zj);
                double dot = Math.Max(-1.0, Math.Min(1.0, xi * xj + yi * yj + zi * zj));
                double meters = WorldGen.Core.Numerics.DeterministicMath.Acos(dot) * PlanetConstants.RadiusMeters;
                Assert.True(meters >= 3_000_000.0 - 1.0,
                    "tul kozeli forras-par: " + (meters / 1000.0).ToString("F0") + " km");
            }
        }
    }

    [Fact]
    public void ElevationThresholdExcludesLowlandSources()
    {
        BuildSyntheticWorld(20, 12, 3, out var elev, out var precip, out var isOcean,
            out var parent, out List<TileId> big, out _);

        // A legnedvesebb ket tile-t lesullyesztjuk a kuszob ala.
        elev[big[0]] = 100.0;
        elev[big[1]] = 100.0;

        List<TileId> sources = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, seaLevel: 0.0,
            basinCount: 1, sourcesPerBasin: 2, minElevAboveSeaM: 300.0,
            minSeparationMeters: 0.0, minBasinTiles: 4);

        Assert.Equal(2, sources.Count);
        Assert.Equal(big[2].Value, sources[0].Value);
        Assert.Equal(big[3].Value, sources[1].Value);
    }

    [Fact]
    public void IsPureAndOrderIndependentOfDictionaryInsertion()
    {
        BuildSyntheticWorld(20, 12, 3, out var elev, out var precip, out var isOcean,
            out var parent, out _, out _);

        List<TileId> a = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, 0.0, 2, 3, 300.0, 0.0, 4);
        List<TileId> b = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, 0.0, 2, 3, 300.0, 0.0, 4);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) Assert.Equal(a[i].Value, b[i].Value);

        // Ugyanaz a vilag, FORDITOTT beszurasi sorrendu szotarakkal: a
        // kimenetnek bitre azonosnak kell lennie (nincs szotar-bejarasi
        // sorrendtol valo fugges).
        var elev2 = new Dictionary<TileId, double>();
        var precip2 = new Dictionary<TileId, double>();
        var isOcean2 = new Dictionary<TileId, bool>();
        var parent2 = new Dictionary<TileId, TileId?>();
        var keys = new List<TileId>(elev.Keys);
        keys.Reverse();
        foreach (TileId t in keys)
        {
            elev2[t] = elev[t];
            precip2[t] = precip[t];
            isOcean2[t] = isOcean[t];
            if (parent.TryGetValue(t, out TileId? p)) parent2[t] = p;
        }

        List<TileId> c = RiverPathTracing.SelectRiverSourcesPerBasin(
            elev2, precip2, isOcean2, parent2, 0.0, 2, 3, 300.0, 0.0, 4);
        Assert.Equal(a.Count, c.Count);
        for (int i = 0; i < a.Count; i++) Assert.Equal(a[i].Value, c[i].Value);
    }

    [Fact]
    public void EmptyWorldAndInvalidArgumentsAreHandled()
    {
        List<TileId> empty = RiverPathTracing.SelectRiverSourcesPerBasin(
            new Dictionary<TileId, double>(), new Dictionary<TileId, double>(),
            new Dictionary<TileId, bool>(), new Dictionary<TileId, TileId?>(), 0.0);
        Assert.Empty(empty);

        BuildSyntheticWorld(20, 12, 3, out var elev, out var precip, out var isOcean,
            out var parent, out _, out _);
        Assert.Throws<ArgumentOutOfRangeException>(() => RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, 0.0, basinCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RiverPathTracing.SelectRiverSourcesPerBasin(
            elev, precip, isOcean, parent, 0.0, sourcesPerBasin: 0));
        Assert.Throws<ArgumentNullException>(() => RiverPathTracing.SelectRiverSourcesPerBasin(
            null!, precip, isOcean, parent, 0.0));
    }

    /// <summary>
    /// A LENYEG, valodi vilagon: a vizgyujtonkenti kvota TOBB osszefolyast ad,
    /// mint az UGYANANNYI forrast hasznalo globalis top-K. A DURVA
    /// (tile-szintu) halozatot hasznaljuk, mert az olcso - a folytonos koveto
    /// ugyanezt mutatja, csak 20-30 masodpercert.
    /// </summary>
    [Fact]
    public void PerBasinQuotaGivesDeeperTreeThanGlobalTopKOnRealWorld()
    {
        const ulong seed = 0xA7C944210000UL;
        const int level = 6;
        var seeds = PlateGeneration.GenerateSeeds(seed, 20);
        var field = SeaLevelCalibration.ComputeElevationField(seed, 20, level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.65);
        var isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
        var precip = MoisturePrecipitation.Compute(seed, 20, level, targetWaterFraction: 0.65);
        var flood = FlowNetwork.PriorityFlood(field, isOcean);

        List<TileId> perBasin = RiverPathTracing.SelectRiverSourcesPerBasin(
            field, precip.Precipitation, isOcean, flood.Parent, seaLevel);
        List<TileId> globalTopK = RiverPathTracing.SelectRiverSources(
            field, precip.Precipitation, isOcean, seaLevel, perBasin.Count);

        Assert.Equal(globalTopK.Count, perBasin.Count);

        int MergeCount(List<TileId> sources)
        {
            var rivers = RiverPathTracing.BuildRiverNetworkFromSources(
                seed, seeds, seaLevel, sources, RiverPathTracing.DefaultFineDepth);
            int merged = 0;
            foreach (RiverPathTracing.RiverPath r in rivers)
                if (r.Termination == RiverPathTracing.TerminationReason.Merged) merged++;
            return merged;
        }

        int mergedPerBasin = MergeCount(perBasin);
        int mergedGlobal = MergeCount(globalTopK);
        _out.WriteLine("forras=" + perBasin.Count + "  osszefolyas: medence="
            + mergedPerBasin + ", globalis=" + mergedGlobal);

        Assert.True(mergedPerBasin > mergedGlobal,
            "a medence-kvota nem adott tobb osszefolyast: " + mergedPerBasin + " vs " + mergedGlobal);
    }
}
