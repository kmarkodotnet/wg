using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Grid;

public class TileIdVectorFileTests
{
    private record Vector(int Face, int Level, uint U, uint V, string Value);

    /// <summary>
    /// A Python referencia (tools/reference/morton_ref.py) altal generalt es
    /// helyben mar ellenorzott (roundtrip + parent) vektorok - a C# portnak
    /// bitre ugyanazt kell adnia.
    /// </summary>
    [Fact]
    public void AllProjectVectorsMatch()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "morton_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        Assert.Equal(TileId.MaxLevel, root.GetProperty("maxLevel").GetInt32());

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            int level = v.GetProperty("level").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            ulong expected = Convert.ToUInt64(v.GetProperty("value").GetString()!, 16);

            TileId id = TileId.FromFaceLevelUV(face, level, u, w);
            Assert.Equal(expected, id.Value);
            Assert.Equal(face, id.Face);
            Assert.Equal(level, id.Level);

            id.GetUV(out uint gotU, out uint gotV);
            Assert.Equal(u, gotU);
            Assert.Equal(w, gotV);
            checkedCount++;
        }

        Assert.Equal(207, checkedCount);
    }
}

public class TileIdRoundTripTests
{
    [Theory]
    [InlineData(0, 0, 0u, 0u)]
    [InlineData(5, 0, 0u, 0u)]
    [InlineData(2, 6, 42u, 17u)]
    [InlineData(0, 28, 268435455u, 268435455u)]
    [InlineData(3, 28, 0u, 268435455u)]
    public void PackThenUnpackReturnsOriginal(int face, int level, uint u, uint v)
    {
        TileId id = TileId.FromFaceLevelUV(face, level, u, v);

        Assert.Equal(face, id.Face);
        Assert.Equal(level, id.Level);
        id.GetUV(out uint gotU, out uint gotV);
        Assert.Equal(u, gotU);
        Assert.Equal(v, gotV);
    }

    [Fact]
    public void RoundTripHoldsAcrossAllFacesAndAGridOfLevels()
    {
        foreach (int level in new[] { 0, 1, 2, 5, 6, 10, 28 })
        {
            uint bound = level == 0 ? 1u : (1u << level);
            uint step = Math.Max(1u, bound / 17); // sűrű, de gyors mintavétel
            for (int face = 0; face <= 5; face++)
            {
                for (uint u = 0; u < bound; u += step)
                {
                    for (uint v = 0; v < bound; v += step)
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        id.GetUV(out uint gotU, out uint gotV);
                        Assert.Equal(u, gotU);
                        Assert.Equal(v, gotV);
                        Assert.Equal(face, id.Face);
                        Assert.Equal(level, id.Level);
                    }
                }
            }
        }
    }

    /// <summary>Ismételt hívás azonos eredményt ad - nincs rejtett állapot.</summary>
    [Fact]
    public void IsPure()
    {
        TileId a = TileId.FromFaceLevelUV(4, 10, 123, 456);
        TileId b = TileId.FromFaceLevelUV(4, 10, 123, 456);
        Assert.Equal(a, b);
        Assert.Equal(a.Value, b.Value);
    }
}

public class TileIdParentChildTests
{
    [Fact]
    public void ParentOfChildIsOriginal()
    {
        TileId parent = TileId.FromFaceLevelUV(2, 6, 42, 17);
        for (int i = 0; i < 4; i++)
        {
            TileId child = parent.Child(i);
            Assert.Equal(parent, child.Parent());
        }
    }

    /// <summary>A 4 gyerek uniója a szülő 2x2-es blokkja (u,v mindkét paritása).</summary>
    [Fact]
    public void FourChildrenCoverTheParentsTwoByTwoBlock()
    {
        TileId parent = TileId.FromFaceLevelUV(1, 5, 10, 20);
        var seen = new System.Collections.Generic.HashSet<(uint, uint)>();

        for (int i = 0; i < 4; i++)
        {
            TileId child = parent.Child(i);
            Assert.Equal(parent.Level + 1, child.Level);
            Assert.Equal(parent.Face, child.Face);
            child.GetUV(out uint u, out uint v);
            Assert.InRange(u, 20u, 21u); // 2*10 .. 2*10+1
            Assert.InRange(v, 40u, 41u); // 2*20 .. 2*20+1
            seen.Add((u, v));
        }

        Assert.Equal(4, seen.Count); // mind a 4 gyerek különböző (u,v) párt kap
    }

    [Fact]
    public void Level0HasNoParent()
    {
        TileId root = TileId.FromFaceLevelUV(0, 0, 0, 0);
        Assert.Throws<InvalidOperationException>(() => root.Parent());
    }

    [Fact]
    public void MaxLevelHasNoChildren()
    {
        TileId leaf = TileId.FromFaceLevelUV(0, TileId.MaxLevel, 0, 0);
        Assert.Throws<InvalidOperationException>(() => leaf.Child(0));
    }
}

public class TileIdEdgeCaseTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(255)]
    public void ThrowsOnInvalidFace(int face)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TileId.FromFaceLevelUV(face, 0, 0, 0));
    }

    [Fact]
    public void ThrowsOnLevelAboveMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TileId.FromFaceLevelUV(0, TileId.MaxLevel + 1, 0, 0));
    }

    [Fact]
    public void ThrowsOnUOutOfRangeForLevel()
    {
        // level 3 -> bound = 8, tehat u=8 mar tul van a hataron
        Assert.Throws<ArgumentOutOfRangeException>(() => TileId.FromFaceLevelUV(0, 3, 8, 0));
    }

    [Fact]
    public void ThrowsOnVOutOfRangeForLevel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TileId.FromFaceLevelUV(0, 3, 0, 8));
    }

    [Fact]
    public void AcceptsMaxValidUVAtGivenLevel()
    {
        // level 3 -> bound = 8, tehat 7 meg ervenyes
        TileId id = TileId.FromFaceLevelUV(0, 3, 7, 7);
        id.GetUV(out uint u, out uint v);
        Assert.Equal(7u, u);
        Assert.Equal(7u, v);
    }

    [Fact]
    public void Level0OnlyAcceptsZeroZero()
    {
        TileId id = TileId.FromFaceLevelUV(0, 0, 0, 0);
        id.GetUV(out uint u, out uint v);
        Assert.Equal(0u, u);
        Assert.Equal(0u, v);
        Assert.Throws<ArgumentOutOfRangeException>(() => TileId.FromFaceLevelUV(0, 0, 1, 0));
    }
}
