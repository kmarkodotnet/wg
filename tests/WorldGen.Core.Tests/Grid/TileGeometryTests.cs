using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Grid;

public class TileGeometryVectorFileTests
{
    /// <summary>
    /// A Python referencia (tools/reference/sphere_position_ref.py) altal
    /// generalt pozicio-vektorok. TOLERANCIA-alapu osszehasonlitas, NEM
    /// bitpontos - a Math.Tan/Math.Atan nem garantaltan bitre azonos
    /// platformok kozott (ND-23b/ND-24). A cel az algoritmus-egyezes
    /// ellenorzese, nem a bitpontos determinizmus (azt a "baked" tervezes
    /// biztosítja, nem ez a teszt).
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceWithinTolerance()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "sphere_position_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            int level = v.GetProperty("level").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            double expX = v.GetProperty("x").GetDouble();
            double expY = v.GetProperty("y").GetDouble();
            double expZ = v.GetProperty("z").GetDouble();

            TileId id = TileId.FromFaceLevelUV(face, level, u, w);
            TileGeometry.ToPosition(id, out double x, out double y, out double z);

            Assert.True(Math.Abs(x - expX) < 1e-9, $"x eltér: {x} vs {expX}");
            Assert.True(Math.Abs(y - expY) < 1e-9, $"y eltér: {y} vs {expY}");
            Assert.True(Math.Abs(z - expZ) < 1e-9, $"z eltér: {z} vs {expZ}");
            checkedCount++;
        }

        Assert.Equal(211, checkedCount);
    }
}

public class TileGeometryRoundTripTests
{
    [Theory]
    [InlineData(0, 0, 0u, 0u)]
    [InlineData(1, 0, 0u, 0u)]
    [InlineData(2, 0, 0u, 0u)]
    [InlineData(3, 0, 0u, 0u)]
    [InlineData(4, 0, 0u, 0u)]
    [InlineData(5, 0, 0u, 0u)]
    [InlineData(4, 6, 0u, 0u)]
    [InlineData(4, 6, 63u, 63u)]
    [InlineData(2, 10, 512u, 512u)]
    [InlineData(0, 28, 268435455u, 268435455u)]
    public void PositionThenBackGivesOriginalTile(int face, int level, uint u, uint v)
    {
        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
        TileGeometry.ToPosition(id, out double x, out double y, out double z);
        TileId back = TileGeometry.FromPosition(x, y, z, level);

        Assert.Equal(id, back);
    }

    [Fact]
    public void RoundTripHoldsAcrossAllFacesAndAGridOfLevels()
    {
        foreach (int level in new[] { 0, 1, 2, 5, 6, 9, 11 })
        {
            uint bound = level == 0 ? 1u : (1u << level);
            uint step = Math.Max(1u, bound / 11);
            for (int face = 0; face <= 5; face++)
            {
                for (uint u = 0; u < bound; u += step)
                {
                    for (uint v = 0; v < bound; v += step)
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.ToPosition(id, out double x, out double y, out double z);
                        TileId back = TileGeometry.FromPosition(x, y, z, level);
                        Assert.Equal(id, back);
                    }
                }
            }
        }
    }

    /// <summary>Ismételt hívás azonos eredményt ad ugyanabban a folyamatban.</summary>
    [Fact]
    public void IsPureWithinAProcess()
    {
        TileId id = TileId.FromFaceLevelUV(3, 7, 12, 34);
        TileGeometry.ToPosition(id, out double x1, out double y1, out double z1);
        TileGeometry.ToPosition(id, out double x2, out double y2, out double z2);

        Assert.Equal(x1, x2);
        Assert.Equal(y1, y2);
        Assert.Equal(z1, z2);
    }
}

public class TileGeometryPlausibilityTests
{
    [Fact]
    public void AllPositionsAreUnitLength()
    {
        for (int face = 0; face <= 5; face++)
        {
            for (uint u = 0; u < 64; u += 7)
            {
                for (uint v = 0; v < 64; v += 7)
                {
                    TileId id = TileId.FromFaceLevelUV(face, 6, u, v);
                    TileGeometry.ToPosition(id, out double x, out double y, out double z);
                    double lenSq = x * x + y * y + z * z;
                    Assert.True(Math.Abs(lenSq - 1.0) < 1e-12, $"Nem egységhosszú: {lenSq}");
                }
            }
        }
    }

    /// <summary>Level 0-nál a tan(0)=0 miatt a pozíció pontosan a lap normálisa.</summary>
    [Theory]
    [InlineData(0, 1.0, 0.0, 0.0)]
    [InlineData(1, -1.0, 0.0, 0.0)]
    [InlineData(2, 0.0, 1.0, 0.0)]
    [InlineData(3, 0.0, -1.0, 0.0)]
    [InlineData(4, 0.0, 0.0, 1.0)]
    [InlineData(5, 0.0, 0.0, -1.0)]
    public void Level0IsExactlyTheFaceNormal(int face, double expX, double expY, double expZ)
    {
        TileId id = TileId.FromFaceLevelUV(face, 0, 0, 0);
        TileGeometry.ToPosition(id, out double x, out double y, out double z);

        Assert.Equal(expX, x);
        Assert.Equal(expY, y);
        Assert.Equal(expZ, z);
    }

    /// <summary>Különböző (face,u,v) tile-ok különböző pozíciót adnak.</summary>
    [Fact]
    public void DistinctTilesGiveDistinctPositions()
    {
        TileId a = TileId.FromFaceLevelUV(4, 6, 10, 20);
        TileId b = TileId.FromFaceLevelUV(4, 6, 10, 21);

        TileGeometry.ToPosition(a, out double ax, out double ay, out double az);
        TileGeometry.ToPosition(b, out double bx, out double by, out double bz);

        Assert.False(ax == bx && ay == by && az == bz);
    }
}

public class TileGeometryBoundsTests
{
    [Theory]
    [InlineData(0, 0, 0, 0u, 0u)]
    [InlineData(3, 28, 5, 268435455u, 268435455u)]
    public void BoundsAtOriginAndMaxMatchExpectedRange(int face, int level, int _, uint u, uint v)
    {
        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
        TileGeometry.GetContinuousBounds(id, out double uMin, out double uMax, out double vMin, out double vMax);

        long n = 1L << level;
        Assert.Equal((double)u / n * 2.0 - 1.0, uMin);
        Assert.Equal((double)(u + 1) / n * 2.0 - 1.0, uMax);
        Assert.Equal((double)v / n * 2.0 - 1.0, vMin);
        Assert.Equal((double)(v + 1) / n * 2.0 - 1.0, vMax);
        Assert.True(uMin < uMax && vMin < vMax);
    }

    /// <summary>
    /// Mesh-építéshez kritikus: két szomszédos (azonos lapon lévő) tile
    /// osztott sarokpontja BITRE azonos pozíciót ad mindkét tile
    /// szempontjából - különben a renderelt mesh-en rés (seam) látszana.
    /// </summary>
    [Fact]
    public void AdjacentTilesOnSameFaceShareExactCornerPositions()
    {
        const int face = 2, level = 6;
        TileId a = TileId.FromFaceLevelUV(face, level, 10, 5);
        TileId b = TileId.FromFaceLevelUV(face, level, 11, 5); // a jobb szomszédja

        TileGeometry.GetContinuousBounds(a, out _, out double aUMax, out double aVMin, out double aVMax);
        TileGeometry.GetContinuousBounds(b, out double bUMin, out _, out double bVMin, out double bVMax);

        Assert.Equal(aUMax, bUMin);
        Assert.Equal(aVMin, bVMin);
        Assert.Equal(aVMax, bVMax);

        TileGeometry.PositionFromFaceUV(face, aUMax, aVMin, out double ax, out double ay, out double az);
        TileGeometry.PositionFromFaceUV(face, bUMin, bVMin, out double bx, out double by, out double bz);

        Assert.Equal(ax, bx);
        Assert.Equal(ay, by);
        Assert.Equal(az, bz);
    }
}
