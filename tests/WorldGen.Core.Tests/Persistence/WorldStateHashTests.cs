using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Grid;
using WorldGen.Core.Persistence;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Persistence;

/// <summary>
/// A Python referencia (tools/reference/state_hash_ref.py) hash-ével
/// BITPONTOS egyezés várt - a SHA-256 garantáltan determinisztikus, nincs
/// tolerancia ebben a láncban.
/// </summary>
public class WorldStateHashVectorFileTests
{
    [Fact]
    public void MatchesPythonReferenceHashExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "state_hash_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        int level = root.GetProperty("level").GetInt32();
        string expectedHash = root.GetProperty("expectedHash").GetString()!;
        int expectedTileCount = root.GetProperty("tileCount").GetInt32();

        Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(worldSeed, plateCount, level);
        Assert.Equal(expectedTileCount, field.Count);

        byte[] hash = WorldStateHash.ComputeFieldHash(field);
        string hexHash = WorldStateHash.ToHexString(hash);

        Assert.Equal(expectedHash, hexHash);
    }
}

public class WorldStateHashStructuralTests
{
    private static Dictionary<TileId, double> SampleField()
    {
        return new Dictionary<TileId, double>
        {
            [TileId.FromFaceLevelUV(0, 3, 1, 1)] = 100.5,
            [TileId.FromFaceLevelUV(1, 3, 2, 2)] = -50.25,
            [TileId.FromFaceLevelUV(2, 3, 3, 3)] = 0.0,
        };
    }

    [Fact]
    public void IsPure()
    {
        Dictionary<TileId, double> field = SampleField();
        byte[] h1 = WorldStateHash.ComputeFieldHash(field);
        byte[] h2 = WorldStateHash.ComputeFieldHash(field);
        Assert.Equal(WorldStateHash.ToHexString(h1), WorldStateHash.ToHexString(h2));
    }

    [Fact]
    public void IsIndependentOfDictionaryInsertionOrder()
    {
        TileId a = TileId.FromFaceLevelUV(0, 3, 1, 1);
        TileId b = TileId.FromFaceLevelUV(1, 3, 2, 2);
        TileId c = TileId.FromFaceLevelUV(2, 3, 3, 3);

        var forward = new Dictionary<TileId, double> { [a] = 1.0, [b] = 2.0, [c] = 3.0 };
        var reverse = new Dictionary<TileId, double> { [c] = 3.0, [b] = 2.0, [a] = 1.0 };

        string h1 = WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(forward));
        string h2 = WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(reverse));

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void IsSensitiveToSingleValueChange()
    {
        Dictionary<TileId, double> field = SampleField();
        string h1 = WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(field));

        TileId key = TileId.FromFaceLevelUV(0, 3, 1, 1);
        field[key] = field[key] + 1e-9;
        string h2 = WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(field));

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void IsSensitiveToKeyChangeEvenWithSameValues()
    {
        TileId a = TileId.FromFaceLevelUV(0, 3, 1, 1);
        TileId aOther = TileId.FromFaceLevelUV(0, 3, 1, 2);

        var field1 = new Dictionary<TileId, double> { [a] = 5.0 };
        var field2 = new Dictionary<TileId, double> { [aOther] = 5.0 };

        Assert.NotEqual(
            WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(field1)),
            WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(field2)));
    }

    [Fact]
    public void EmptyFieldProducesConsistentHash()
    {
        var empty = new Dictionary<TileId, double>();
        string h1 = WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(empty));
        string h2 = WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(empty));
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length);
    }

    [Fact]
    public void HexStringIsAlways64LowercaseHexCharacters()
    {
        string hex = WorldStateHash.ToHexString(WorldStateHash.ComputeFieldHash(SampleField()));
        Assert.Equal(64, hex.Length);
        foreach (char c in hex)
            Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), $"Nem hex karakter: {c}");
    }
}
