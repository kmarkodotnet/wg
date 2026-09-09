using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Grid;

public class TileNeighborsVectorFileTests
{
    private static TileDirection ParseDirection(string s) => s switch
    {
        "right" => TileDirection.Right,
        "left" => TileDirection.Left,
        "up" => TileDirection.Up,
        "down" => TileDirection.Down,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    /// <summary>
    /// A Python referencia (tools/reference/neighbor_ref.py) altal generalt
    /// szomszed-vektorok. Az iranyitas/lapvaltas EGZAKT egyezest var (nem
    /// toleranciat), mert a kimenet egesz (face,u,v) index - ha ez elter,
    /// az valodi algoritmus-elteres, nem lebegopontos zaj.
    /// </summary>
    [Fact]
    public void AllProjectVectorsMatch()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "neighbor_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            int level = v.GetProperty("level").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            TileDirection dir = ParseDirection(v.GetProperty("direction").GetString()!);
            int expFace = v.GetProperty("nFace").GetInt32();
            uint expU = v.GetProperty("nU").GetUInt32();
            uint expV = v.GetProperty("nV").GetUInt32();

            TileId id = TileId.FromFaceLevelUV(face, level, u, w);
            TileId got = TileNeighbors.Neighbor(id, dir);
            got.GetUV(out uint gotU, out uint gotV);

            Assert.Equal(expFace, got.Face);
            Assert.Equal((int)expU, (int)gotU);
            Assert.Equal((int)expV, (int)gotV);
            checkedCount++;
        }

        Assert.Equal(1920, checkedCount);
    }
}

public class TileNeighborsExhaustiveTests
{
    /// <summary>
    /// Kimerítő ellenőrzés egy teljes level-4 rácson (1536 tile): minden
    /// tile-nak (a 24 lap-sarok-tile is) pontosan 4 disztinkt szomszédja
    /// van, és a szomszédság mindig szimmetrikus. Ez ugyanaz a mérés, ami
    /// a docs/05-milestones.md §2.4 eredeti "3 szomszéd a sarkokon"
    /// állítását megcáfolta (ld. ND-25) - itt a C# saját implementációját
    /// ellenőrizzük, nem csak a Python-hoz való egyezést.
    /// </summary>
    [Fact]
    public void EveryTileHasExactlyFourSymmetricNeighbors()
    {
        const int level = 4;
        const uint n = 1u << level;

        var allTiles = new List<TileId>();
        for (int face = 0; face <= 5; face++)
            for (uint u = 0; u < n; u++)
                for (uint v = 0; v < n; v++)
                    allTiles.Add(TileId.FromFaceLevelUV(face, level, u, v));

        Assert.Equal(6 * (int)n * (int)n, allTiles.Count);

        var neighborSets = new Dictionary<TileId, HashSet<TileId>>();
        foreach (TileId t in allTiles)
        {
            TileNeighbors.GetAll(t, out TileId r, out TileId l, out TileId up, out TileId down);
            var set = new HashSet<TileId> { r, l, up, down };
            Assert.Equal(4, set.Count); // 4 disztinkt szomszéd, sarkon is
            neighborSets[t] = set;
        }

        int symmetryFailures = 0;
        foreach (var (tile, neighbors) in neighborSets)
        {
            foreach (TileId nb in neighbors)
            {
                if (!neighborSets[nb].Contains(tile))
                    symmetryFailures++;
            }
        }

        Assert.Equal(0, symmetryFailures);
    }

    /// <summary>A 24 lap-sarok-tile külön kiemelve: ők sem kivételek.</summary>
    [Fact]
    public void CornerTilesAlsoHaveFourNeighbors()
    {
        const int level = 5;
        uint n = 1u << level;
        uint[] extremes = { 0, n - 1 };

        int checkedCorners = 0;
        for (int face = 0; face <= 5; face++)
        {
            foreach (uint u in extremes)
            {
                foreach (uint v in extremes)
                {
                    TileId corner = TileId.FromFaceLevelUV(face, level, u, v);
                    TileNeighbors.GetAll(corner, out TileId r, out TileId l, out TileId up, out TileId down);
                    var set = new HashSet<TileId> { r, l, up, down };
                    Assert.Equal(4, set.Count);
                    checkedCorners++;
                }
            }
        }

        Assert.Equal(24, checkedCorners);
    }

    /// <summary>Ismételt hívás azonos eredményt ad ugyanabban a folyamatban.</summary>
    [Fact]
    public void IsPureWithinAProcess()
    {
        TileId id = TileId.FromFaceLevelUV(2, 6, 0, 30);
        TileId a = TileNeighbors.Neighbor(id, TileDirection.Left);
        TileId b = TileNeighbors.Neighbor(id, TileDirection.Left);
        Assert.Equal(a, b);
    }
}

/// <summary>
/// `DiagonalNeighbor` (ld. osztály-doksi a Grid/TileNeighbors.cs-ben) - a
/// PlanetGridMesh felhő-sarok-átlagolás varrat-javítása közben (2026-09-06)
/// felmerült, majd MÉRÉSSEL elvetett kísérlet egy pontos átlós-szomszéd
/// képletre. A tesztek itt NEM azt bizonyítják, hogy a metódus mindig
/// helyes - éppen ellenkezőleg, RÖGZÍTIK a mért korlátozást (a lap éle/
/// csúcsa közelében az oda-vissza kör nem zárul), hogy ha valaki a
/// jövőben megpróbálja "kijavítani" vagy újrafelhasználni ezt a
/// segédfüggvényt, tudja, mire számítson, és ne lepődjön meg, ha a hívó
/// (`PlanetGridMesh.PrecipAndOceanFractionAtCorner`) nem ezt használja.
/// </summary>
public class TileNeighborsDiagonalTests
{
    private static TileDirection Opposite(TileDirection d) => d switch
    {
        TileDirection.Right => TileDirection.Left,
        TileDirection.Left => TileDirection.Right,
        TileDirection.Up => TileDirection.Down,
        TileDirection.Down => TileDirection.Up,
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    [Fact]
    public void AlwaysReturnsAValidTile()
    {
        const int level = 5;
        uint n = 1u << level;
        for (int face = 0; face <= 5; face++)
        {
            for (uint u = 0; u < n; u += 3)
            {
                for (uint v = 0; v < n; v += 3)
                {
                    TileId t = TileId.FromFaceLevelUV(face, level, u, v);
                    foreach (var (h, vd) in new[]
                    {
                        (TileDirection.Right, TileDirection.Up), (TileDirection.Right, TileDirection.Down),
                        (TileDirection.Left, TileDirection.Up), (TileDirection.Left, TileDirection.Down),
                    })
                    {
                        TileId diag = TileNeighbors.DiagonalNeighbor(t, h, vd);
                        Assert.InRange((int)diag.Face, 0, 5);
                        Assert.Equal(level, diag.Level);
                    }
                }
            }
        }
    }

    [Fact]
    public void IsPure()
    {
        TileId t = TileId.FromFaceLevelUV(2, 6, 10, 20);
        TileId a = TileNeighbors.DiagonalNeighbor(t, TileDirection.Right, TileDirection.Up);
        TileId b = TileNeighbors.DiagonalNeighbor(t, TileDirection.Right, TileDirection.Up);
        Assert.Equal(a, b);
    }

    [Fact]
    public void MatchesBothTwoStepOrdersAwayFromFaceBoundaries()
    {
        // A lap belsejeben (nem a szelen) NINCS kozbeni keret-valtas, tehat
        // a ket ket-lepeses sorrendnek (Right->Up es Up->Right) es az
        // egy-lepeses DiagonalNeighbor-nak MINDHARMNAK egyeznie kell.
        const int level = 5;
        uint n = 1u << level;
        TileId t = TileId.FromFaceLevelUV(0, level, n / 2, n / 2); // biztosan belso tile
        TileId rightUp = TileNeighbors.Neighbor(TileNeighbors.Neighbor(t, TileDirection.Right), TileDirection.Up);
        TileId upRight = TileNeighbors.Neighbor(TileNeighbors.Neighbor(t, TileDirection.Up), TileDirection.Right);
        TileId diag = TileNeighbors.DiagonalNeighbor(t, TileDirection.Right, TileDirection.Up);

        Assert.Equal(rightUp.Value, upRight.Value);
        Assert.Equal(rightUp.Value, diag.Value);
    }

    /// <summary>
    /// DOKUMENTÁLT, MÉRT KORLÁTOZÁS (NEM regresszió-védelem a "helyes"
    /// viselkedésre - ELLENKEZŐLEG, ez rögzíti, hogy a kör NEM zárul a
    /// lap éle/csúcsa közelében). Ha ez az arány jelentősen megváltozna
    /// egy jövőbeli refaktornál, az azt jelezné, hogy a `TileGeometry`
    /// vetítés-logika módosult - érdemes újra megmérni, VAJON a hívó
    /// (`PlanetGridMesh.PrecipAndOceanFractionAtCorner`) `crossedFace`
    /// kihagyás-logikája még mindig szükséges-e.
    /// </summary>
    [Fact]
    public void RoundTripFailsOnlyNearFaceBoundaries_KnownLimitation()
    {
        const int level = 5;
        uint n = 1u << level;
        int checkedCount = 0;
        int roundTripFailures = 0;

        for (int face = 0; face <= 5; face++)
        {
            for (uint u = 0; u < n; u++)
            {
                for (uint v = 0; v < n; v++)
                {
                    TileId t = TileId.FromFaceLevelUV(face, level, u, v);
                    foreach (var (h, vd) in new[]
                    {
                        (TileDirection.Right, TileDirection.Up), (TileDirection.Right, TileDirection.Down),
                        (TileDirection.Left, TileDirection.Up), (TileDirection.Left, TileDirection.Down),
                    })
                    {
                        checkedCount++;
                        TileId diag = TileNeighbors.DiagonalNeighbor(t, h, vd);
                        TileId back = TileNeighbors.DiagonalNeighbor(diag, Opposite(h), Opposite(vd));
                        if (back.Value != t.Value) roundTripFailures++;
                    }
                }
            }
        }

        Assert.Equal(24576, checkedCount);
        Assert.Equal(768, roundTripFailures); // ~3.125% - a lap ele/csucsa kozeleben, ld. doksi
    }
}

public class TileNeighborsCoverageTests
{
    /// <summary>
    /// NoGaps-jellegű lefedettségi ellenőrzés: sok véletlen pozíció a
    /// gömbön mindig érvényes, tartományon belüli tile-t ad vissza - nincs
    /// lyuk vagy kivétel-dobás a lefedésben.
    /// </summary>
    [Fact]
    public void EveryDirectionOnEveryFaceProducesAValidTile()
    {
        const int level = 6;
        uint n = 1u << level;

        for (int face = 0; face <= 5; face++)
        {
            for (uint u = 0; u < n; u += 5)
            {
                for (uint v = 0; v < n; v += 5)
                {
                    TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                    foreach (TileDirection dir in new[] { TileDirection.Right, TileDirection.Left, TileDirection.Up, TileDirection.Down })
                    {
                        TileId nb = TileNeighbors.Neighbor(id, dir);
                        Assert.InRange((int)nb.Face, 0, 5);
                        Assert.Equal(level, nb.Level);
                        nb.GetUV(out uint nu, out uint nv);
                        Assert.InRange(nu, 0u, n - 1);
                        Assert.InRange(nv, 0u, n - 1);
                    }
                }
            }
        }
    }
}
