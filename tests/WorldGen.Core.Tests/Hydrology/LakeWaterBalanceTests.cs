using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Core.Tests.Hydrology;

/// <summary>
/// ND-198 (2026-10-06): a tavak VÍZMÉRLEGE.
///
/// MIERT KELL: a to eddig tisztan TOPOGRAFIAI volt - a priority-flood minden
/// zart melyedest a kifolyasi szintig toltott, vizmerleg nelkul. MERVE a
/// termekben: 531 lathato toból 227 (42,7%) vizgyujtojeben PONTOSAN nulla a
/// csapadek, es a teljes to-terfogat a Fold tavainak tobb mint tizszerese.
///
/// A szintetikus vilag KEZZEL EPITETT lefolyas-szulo terkeppel dolgozik, hogy
/// a vizgyujto pontosan ismert legyen - nem fugg a priority-flood
/// viselkedesetol.
/// </summary>
public class LakeWaterBalanceTests
{
    private readonly ITestOutputHelper _out;
    public LakeWaterBalanceTests(ITestOutputHelper o) { _out = o; }

    private const int Level = 5;

    /// <summary>
    /// Egy medence: `lakeTiles` darab to-tile (egyre magasabb terep), folotte
    /// `catchmentTiles` darab vizgyujto-tile, ami a to legalso tile-jaba folyik.
    /// A tavat a `LakeResult` kezzel kapja meg (a detektalas nem a teszt targya).
    /// </summary>
    private static void BuildWorld(
        int lakeTileCount, int catchmentTileCount, double precipPerTile, double evaporationPerTile,
        out LakesIceErosion.LakeResult lakes,
        out Dictionary<TileId, double> terrain,
        out Dictionary<TileId, TileId?> parent,
        out Dictionary<TileId, double> precipitation,
        out Dictionary<TileId, double> evaporation,
        out List<TileId> lakeTiles)
    {
        terrain = new Dictionary<TileId, double>();
        parent = new Dictionary<TileId, TileId?>();
        precipitation = new Dictionary<TileId, double>();
        evaporation = new Dictionary<TileId, double>();
        lakeTiles = new List<TileId>();

        // A to tile-jai: v = 0, u = 0..n-1, a terep 100 m-enkent emelkedik.
        for (int i = 0; i < lakeTileCount; i++)
        {
            TileId t = TileId.FromFaceLevelUV(0, Level, (uint)i, 0);
            lakeTiles.Add(t);
            terrain[t] = 1000.0 + 100.0 * i;
            precipitation[t] = precipPerTile;
            evaporation[t] = evaporationPerTile;
            parent[t] = i == 0 ? (TileId?)null : lakeTiles[0];
        }
        // Vizgyujto: v = 1, mind a to LEGALSO tile-jaba folyik.
        for (int i = 0; i < catchmentTileCount; i++)
        {
            TileId t = TileId.FromFaceLevelUV(0, Level, (uint)i, 1);
            terrain[t] = 3000.0;
            precipitation[t] = precipPerTile;
            evaporation[t] = evaporationPerTile;
            parent[t] = lakeTiles[0];
        }

        var info = new LakesIceErosion.LakeInfo
        {
            Id = 0,
            Tiles = new List<TileId>(lakeTiles),
            TileCount = lakeTileCount,
            SurfaceElevation = 1000.0 + 100.0 * lakeTileCount,
            MaxDepth = 100.0 * lakeTileCount,
        };
        lakes = new LakesIceErosion.LakeResult();
        lakes.Lakes.Add(info);
    }

    [Fact]
    public void LakeWithoutAnyPrecipitationDisappears()
    {
        BuildWorld(4, 20, precipPerTile: 0.0, evaporationPerTile: 1.0,
            out var lakes, out var terrain, out var parent, out var precip, out var evap, out _);

        List<LakeWaterBalance.BalancedLake> balanced = LakeWaterBalance.Balance(
            lakes, terrain, parent, precip, evap, Level);

        Assert.Single(balanced);
        Assert.False(balanced[0].Exists);
        Assert.Equal(0, balanced[0].TileCount);
        Assert.Equal(0.0, balanced[0].Inflow);
    }

    [Fact]
    public void WetCatchmentKeepsTheLakeAtTheTopographicLevel()
    {
        // 24 vizgyujto-tile x 1,0 csapadek x 0,3 lefolyas = 7,2 egyseg
        // bearamlas + a to sajat 4 tile-janak csapadeka (4,0) = 11,2; a
        // parolgas 4 x 1,0 = 4 egyseg, tehat a to VEGIG megmarad.
        BuildWorld(4, 24, precipPerTile: 1.0, evaporationPerTile: 1.0,
            out var lakes, out var terrain, out var parent, out var precip, out var evap, out _);

        List<LakeWaterBalance.BalancedLake> balanced = LakeWaterBalance.Balance(
            lakes, terrain, parent, precip, evap, Level);

        Assert.True(balanced[0].Exists);
        Assert.Equal(4, balanced[0].TileCount);
        Assert.Equal(lakes.Lakes[0].SurfaceElevation, balanced[0].SurfaceElevation);
        _out.WriteLine($"inflow={balanced[0].Inflow} evaporation={balanced[0].Evaporation}");
    }

    /// <summary>
    /// A LENYEG: ha a bearamlas csak egy KISEBB tofelszint tud fedezni, a to
    /// ZSUGORODIK - nem tunik el, es nem is marad teli.
    /// </summary>
    [Fact]
    public void LimitedInflowShrinksTheLakeToTheBalanceLevel()
    {
        // 10 vizgyujto-tile x 1,0 x 0,3 = 3,0 egyseg bearamlas + a to sajat
        // 6 tile-jara hullo 6 x 1,0... az utobbi a tofelszin csapadeka, ami
        // SZINTFUGGETLENUL a teljes to-halmazra szamol (a modell egyszerusitese).
        // A parolgas tile-onkent 2,0 - tehat kb. 4-5 tile fedezheto.
        BuildWorld(6, 10, precipPerTile: 1.0, evaporationPerTile: 2.0,
            out var lakes, out var terrain, out var parent, out var precip, out var evap,
            out List<TileId> lakeTiles);

        List<LakeWaterBalance.BalancedLake> balanced = LakeWaterBalance.Balance(
            lakes, terrain, parent, precip, evap, Level);

        Assert.True(balanced[0].Exists);
        Assert.True(balanced[0].TileCount > 0 && balanced[0].TileCount < 6,
            "a to nem zsugorodott: " + balanced[0].TileCount);
        // A merleg-szint a LEGALSO tile-okat tartja meg, tehat a felszin a
        // topografiai szint ALATT van.
        Assert.True(balanced[0].SurfaceElevation < lakes.Lakes[0].SurfaceElevation);
        // ...es a parolgas tenyleg nem haladja meg a bearamlast.
        Assert.True(balanced[0].Evaporation <= balanced[0].Inflow);
        _out.WriteLine($"tiles={balanced[0].TileCount} surface={balanced[0].SurfaceElevation} "
            + $"inflow={balanced[0].Inflow} evaporation={balanced[0].Evaporation}");
    }

    [Fact]
    public void MoreRunoffKeepsMoreWater()
    {
        BuildWorld(6, 10, precipPerTile: 1.0, evaporationPerTile: 2.0,
            out var lakes, out var terrain, out var parent, out var precip, out var evap, out _);

        int Tiles(double runoff) => LakeWaterBalance.Balance(
            lakes, terrain, parent, precip, evap, Level, runoff)[0].TileCount;

        int dry = Tiles(0.0);
        int wet = Tiles(1.0);
        Assert.True(wet >= dry, "a nagyobb lefolyasi hanyad kevesebb vizet tartott: " + wet + " vs " + dry);
        _out.WriteLine($"runoff 0,0 -> {dry} tile; runoff 1,0 -> {wet} tile");
    }

    [Fact]
    public void IsPureAndIndependentOfDictionaryInsertionOrder()
    {
        BuildWorld(6, 24, precipPerTile: 1.0, evaporationPerTile: 2.0,
            out var lakes, out var terrain, out var parent, out var precip, out var evap, out _);

        List<LakeWaterBalance.BalancedLake> a = LakeWaterBalance.Balance(
            lakes, terrain, parent, precip, evap, Level);
        List<LakeWaterBalance.BalancedLake> b = LakeWaterBalance.Balance(
            lakes, terrain, parent, precip, evap, Level);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].TileCount, b[i].TileCount);
            Assert.Equal(BitConverter.DoubleToInt64Bits(a[i].SurfaceElevation),
                BitConverter.DoubleToInt64Bits(b[i].SurfaceElevation));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a[i].Inflow),
                BitConverter.DoubleToInt64Bits(b[i].Inflow));
        }

        // Fordított beszúrási sorrendű szótárakkal BITRE ugyanaz.
        var terrain2 = new Dictionary<TileId, double>();
        var parent2 = new Dictionary<TileId, TileId?>();
        var precip2 = new Dictionary<TileId, double>();
        var evap2 = new Dictionary<TileId, double>();
        var keys = new List<TileId>(terrain.Keys);
        keys.Reverse();
        foreach (TileId t in keys)
        {
            terrain2[t] = terrain[t];
            precip2[t] = precip[t];
            evap2[t] = evap[t];
            if (parent.TryGetValue(t, out TileId? p)) parent2[t] = p;
        }
        List<LakeWaterBalance.BalancedLake> c = LakeWaterBalance.Balance(
            lakes, terrain2, parent2, precip2, evap2, Level);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].TileCount, c[i].TileCount);
            Assert.Equal(BitConverter.DoubleToInt64Bits(a[i].Inflow),
                BitConverter.DoubleToInt64Bits(c[i].Inflow));
        }
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        BuildWorld(4, 10, 1.0, 1.0, out var lakes, out var terrain, out var parent,
            out var precip, out var evap, out _);
        Assert.Throws<ArgumentNullException>(() => LakeWaterBalance.Balance(
            null!, terrain, parent, precip, evap, Level));
        Assert.Throws<ArgumentNullException>(() => LakeWaterBalance.Balance(
            lakes, terrain, parent, null!, evap, Level));
        Assert.Throws<ArgumentOutOfRangeException>(() => LakeWaterBalance.Balance(
            lakes, terrain, parent, precip, evap, Level, -0.1));
    }

}
