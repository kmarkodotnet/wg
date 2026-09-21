using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// ND-125: a KANONIKUS lemez-hozzarendeles
/// (<see cref="PlateGeneration.AssignPlateWarped"/>) tesztjei.
///
/// MIERT KELL: a nyers gombi Voronoi MATEMATIKAILAG KONVEX - minden cella
/// nagykor-ivekkel hatarolt sokszog. A vilagmodell ezert a WARPOLT poziciot
/// hasznalja (ND-36), a tektonikus overlay viszont a NYERSET hasznalta,
/// tehat egy MASIK felosztast rajzolt. A felhasznaloi visszajelzes
/// ("rendkivul szabalyosak, szinte mindegyik egy negyszog vagy haromszog")
/// ezt a kulonbseget latta.
///
/// Az itteni tesztek NEM a warp parametereit rogzitik (azok ND-36 alatt
/// hangoltak), hanem azt a ket allitast, amin a javitas all:
/// (1) a ket felosztas ERDEMBEN kulonbozik, es
/// (2) a warpolt hatara LATHATOAN szabalytalanabb.
/// Ha barmelyik megszunne, a javitas ertelmet vesztene - csendben.
/// </summary>
public class WarpedPlateAssignmentTests
{
    private readonly ITestOutputHelper _out;
    public WarpedPlateAssignmentTests(ITestOutputHelper o) { _out = o; }

    private const int Level = 6;
    private const int PlateCount = 20;

    private static readonly ulong[] Seeds = { 0xA7C944210000UL, 0x1234UL, 0xDEADBEEFUL };

    private static List<TileId> AllTiles(int level)
    {
        int side = 1 << level;
        var tiles = new List<TileId>(6 * side * side);
        for (int face = 0; face < 6; face++)
            for (uint v = 0; v < side; v++)
                for (uint u = 0; u < side; u++)
                    tiles.Add(TileId.FromFaceLevelUV(face, level, u, v));
        return tiles;
    }

    [Fact]
    public void WarpedAssignmentEqualsWarpThenAssign()
    {
        const ulong seed = 0xA7C944210000UL;
        var plateSeeds = PlateGeneration.GenerateSeeds(seed, PlateCount);

        foreach (TileId t in AllTiles(4))
        {
            TileGeometry.ToPosition(t, out double x, out double y, out double z);
            DomainWarp.WarpPosition(seed, x, y, z, out double wx, out double wy, out double wz);

            Assert.Equal(
                PlateGeneration.AssignPlate(wx, wy, wz, plateSeeds),
                PlateGeneration.AssignPlateWarped(seed, x, y, z, plateSeeds));
        }
    }

    [Fact]
    public void IsPure()
    {
        const ulong seed = 0x1234UL;
        var plateSeeds = PlateGeneration.GenerateSeeds(seed, PlateCount);

        foreach (TileId t in AllTiles(3))
        {
            TileGeometry.ToPosition(t, out double x, out double y, out double z);
            int a = PlateGeneration.AssignPlateWarped(seed, x, y, z, plateSeeds);
            int b = PlateGeneration.AssignPlateWarped(seed, x, y, z, plateSeeds);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void WorldSeedActuallyAffectsTheWarpedAssignment()
    {
        var plateSeeds = PlateGeneration.GenerateSeeds(0xA7C944210000UL, PlateCount);

        // UGYANAZOK a magpontok, MAS vilag-seed -> a warp mas, tehat a
        // hozzarendelesnek is mas-nak kell lennie. Ha ez elbukik, a
        // worldSeed parameter kimaradt a lekepezesbol.
        int differ = 0, total = 0;
        foreach (TileId t in AllTiles(4))
        {
            TileGeometry.ToPosition(t, out double x, out double y, out double z);
            total++;
            if (PlateGeneration.AssignPlateWarped(0xA7C944210000UL, x, y, z, plateSeeds)
                != PlateGeneration.AssignPlateWarped(0x1234UL, x, y, z, plateSeeds))
                differ++;
        }
        Assert.True(differ > total / 100,
            "a vilag-seed alig hat a warpolt hozzarendelesre: " + differ + "/" + total);
    }

    /// <summary>
    /// (1) A ket felosztas ERDEMBEN kulonbozik. Ha valaki a warpot kiveszi
    /// valamelyik utbol, az nem "kozel azonos" eredmenyt ad, hanem a bolygo
    /// otodet-negyedet maskepp osztja fel.
    /// </summary>
    [Fact]
    public void RawAndWarpedAssignmentsDifferOnAFifthOfThePlanet()
    {
        List<TileId> tiles = AllTiles(Level);
        foreach (ulong seed in Seeds)
        {
            var plateSeeds = PlateGeneration.GenerateSeeds(seed, PlateCount);
            int differ = 0;
            foreach (TileId t in tiles)
            {
                TileGeometry.ToPosition(t, out double x, out double y, out double z);
                if (PlateGeneration.AssignPlate(x, y, z, plateSeeds)
                    != PlateGeneration.AssignPlateWarped(seed, x, y, z, plateSeeds))
                    differ++;
            }
            double fraction = (double)differ / tiles.Count;
            _out.WriteLine("seed 0x" + seed.ToString("X") + ": elteres " + (100.0 * fraction).ToString("F1") + "%");
            Assert.True(fraction > 0.15,
                "a warp hatasa eltunt: csak " + (100.0 * fraction).ToString("F1") + "% elteres");
        }
    }

    /// <summary>
    /// (2) A LENYEG: a warpolt lemezhatar LATHATOAN szabalytalanabb. A metrika
    /// a kerulet/sqrt(terulet) izoperimetrikus hanyados - konvex, sima hataru
    /// alakzatnal kicsi, huzagos hatarnal nagy. Mert ertekek (level 6, harom
    /// seed): nyers 4,8-5,1, warpolt 7,0-7,6.
    /// </summary>
    [Fact]
    public void WarpedPlateBoundariesAreMarkedlyMoreIrregularThanRawVoronoi()
    {
        List<TileId> tiles = AllTiles(Level);
        foreach (ulong seed in Seeds)
        {
            var plateSeeds = PlateGeneration.GenerateSeeds(seed, PlateCount);

            var raw = new Dictionary<TileId, int>(tiles.Count);
            var warped = new Dictionary<TileId, int>(tiles.Count);
            foreach (TileId t in tiles)
            {
                TileGeometry.ToPosition(t, out double x, out double y, out double z);
                raw[t] = PlateGeneration.AssignPlate(x, y, z, plateSeeds);
                warped[t] = PlateGeneration.AssignPlateWarped(seed, x, y, z, plateSeeds);
            }

            double rawRatio = MeanIsoperimetricRatio(raw);
            double warpedRatio = MeanIsoperimetricRatio(warped);
            _out.WriteLine("seed 0x" + seed.ToString("X") + ": kerulet/sqrt(terulet) nyers="
                + rawRatio.ToString("F2") + " warpolt=" + warpedRatio.ToString("F2")
                + "  (" + (warpedRatio / rawRatio).ToString("F2") + "x)");

            Assert.True(warpedRatio > 1.3 * rawRatio,
                "a warpolt hatar nem eleg szabalytalan: " + warpedRatio.ToString("F2")
                + " vs nyers " + rawRatio.ToString("F2"));
        }
    }

    /// <summary>Kerulet/sqrt(terulet) atlaga a nem ures lemezeken, tile-szamban.</summary>
    private static double MeanIsoperimetricRatio(Dictionary<TileId, int> assignment)
    {
        var area = new int[PlateCount];
        var perimeter = new int[PlateCount];
        foreach (KeyValuePair<TileId, int> kv in assignment)
        {
            int p = kv.Value;
            area[p]++;
            TileNeighbors.GetAll(kv.Key, out TileId r, out TileId l, out TileId up, out TileId dn);
            if (assignment.TryGetValue(r, out int a) && a != p) perimeter[p]++;
            if (assignment.TryGetValue(l, out int b) && b != p) perimeter[p]++;
            if (assignment.TryGetValue(up, out int c) && c != p) perimeter[p]++;
            if (assignment.TryGetValue(dn, out int d) && d != p) perimeter[p]++;
        }

        double sum = 0.0;
        int used = 0;
        for (int p = 0; p < PlateCount; p++)
        {
            if (area[p] == 0) continue;
            used++;
            sum += perimeter[p] / Math.Sqrt(area[p]);
        }
        return sum / used;
    }
}
