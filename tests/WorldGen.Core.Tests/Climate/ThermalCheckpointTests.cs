using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public class ThermalCheckpointTests
{
    [Fact]
    public void BinaryFormatAndHashesMatchIndependentPythonOracle()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "testdata", "thermal_checkpoint_vectors.json")));
        var grid = DenseGridMetrics.Build(1);
        var field = new SurfaceTemperatureField(grid, new SurfaceThermalKind[grid.CellCount],
            new double[grid.CellCount], 0, 1, 0, ThermalFieldFixture.Orbit);
        var state = new ThermalSnapshot(field.Grid.CellCount);
        field.ResetCanonical(state, 0);
        Assert.Equal(doc.RootElement.GetProperty("modelIdentity").GetString(), field.ModelIdentity);
        Assert.Equal(doc.RootElement.GetProperty("stateHash").GetString(), ThermalCheckpoint.StateHash(field, state));
        Assert.Equal(Convert.FromHexString(doc.RootElement.GetProperty("checkpointHex").GetString()!),
            ThermalCheckpoint.Capture(field, state));
    }

    private static SurfaceTemperatureField Field(ulong seed = 1, double years = 0, double sea = 0,
        ThermalModelParameters? parameters = null, ThermalOrbit? orbit = null, bool changeKind = false,
        bool changeElevation = false)
    {
        var grid = DenseGridMetrics.Build(1);
        var (kinds, elevation) = ThermalFieldFixture.SyntheticWorld(grid);
        if (changeKind) kinds[0] = kinds[0] == SurfaceThermalKind.Ice ? SurfaceThermalKind.Land : SurfaceThermalKind.Ice;
        if (changeElevation) elevation[0] += 1;
        return new SurfaceTemperatureField(grid, kinds, elevation, sea, seed, years, orbit ?? ThermalFieldFixture.Orbit, parameters);
    }

    [Fact]
    public void CheckpointResumesToBitIdenticalStateAndHashOnFreshSolver()
    {
        var field = Field();
        var state = new ThermalSnapshot(field.Grid.CellCount);
        field.ResetCanonical(state, 0);
        field.RunTo(state, -940);
        byte[] bytes = ThermalCheckpoint.Capture(field, state);
        Assert.Equal("WGTH0001", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        var fresh = Field();
        var restored = new ThermalSnapshot(fresh.Grid.CellCount);
        ThermalCheckpoint.Restore(fresh, restored, bytes);
        Assert.Equal(bytes, ThermalCheckpoint.Capture(fresh, restored));
        field.RunTo(state, -920);
        fresh.UseParallelLocalStep = true;
        fresh.RunTo(restored, -920);
        Assert.Equal(state.ThetaS.Select(BitConverter.DoubleToInt64Bits), restored.ThetaS.Select(BitConverter.DoubleToInt64Bits));
        Assert.Equal(state.ThetaA.Select(BitConverter.DoubleToInt64Bits), restored.ThetaA.Select(BitConverter.DoubleToInt64Bits));
        Assert.Equal(ThermalCheckpoint.StateHash(field, state), ThermalCheckpoint.StateHash(fresh, restored));
        fresh.Step(restored);
        Assert.NotEqual(ThermalCheckpoint.StateHash(field, state), ThermalCheckpoint.StateHash(fresh, restored));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)]
    public void WorldInputsArePartOfIdentityAndForeignStateCannotContinue(int input)
    {
        var field = Field();
        var other = input switch
        {
            0 => Field(seed: 2), 1 => Field(years: 1), 2 => Field(sea: 1),
            3 => Field(parameters: new ThermalModelParameters(airColumnM: 1100)),
            4 => Field(orbit: new ThermalOrbit(360, 1, 0.4)),
            5 => Field(changeKind: true), 6 => Field(changeElevation: true),
            // ND-160: a BÁZIS albedója is bemenet — különben egy A/B-mérés
            // némán a másik mód gyorsítótárára/checkpointjára találna rá.
            8 => Field(parameters: new ThermalModelParameters(legacySurfaceBaselineAlbedo: true)),
            9 => Field(parameters: new ThermalModelParameters(baselineAlbedo: 0.25)),
            _ => Field(parameters: new ThermalModelParameters(airFeedbackStrength: 0.25))
        };
        Assert.NotEqual(field.ModelIdentity, other.ModelIdentity);
        var state = new ThermalSnapshot(field.Grid.CellCount);
        field.ResetCanonical(state, 0);
        var destination = new ThermalSnapshot(field.Grid.CellCount);
        Assert.Throws<InvalidDataException>(() => ThermalCheckpoint.Restore(other, destination, ThermalCheckpoint.Capture(field, state)));
        Assert.Throws<ArgumentException>(() => other.Step(state));
        Assert.Null(destination.ModelIdentity);
    }

    [Theory]
    [InlineData("truncate")] [InlineData("corrupt")] [InlineData("generator")]
    [InlineData("model")] [InlineData("nan")] [InlineData("tick")]
    public void InvalidCheckpointLeavesDestinationUntouched(string damage)
    {
        var field = Field();
        var state = new ThermalSnapshot(field.Grid.CellCount);
        field.ResetCanonical(state, 0);
        byte[] bytes = ThermalCheckpoint.Capture(field, state);
        var destination = new ThermalSnapshot(field.Grid.CellCount);
        destination.Reset(123);
        destination.ThetaS[0] = 42;
        if (damage == "truncate") Array.Resize(ref bytes, bytes.Length - 1);
        else if (damage == "corrupt") bytes[bytes.Length - 1] ^= 1;
        else
        {
            int generatorLength = (int)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8, 8));
            int modelOffset = 16 + generatorLength;
            int dataOffset = bytes.Length - 32 - state.ThetaS.Length * 16;
            if (damage == "generator") bytes[16] = (byte)'1';
            if (damage == "model") BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(modelOffset, 8), 2);
            if (damage == "nan") BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(dataOffset, 8), BitConverter.DoubleToInt64Bits(double.NaN));
            if (damage == "tick") BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(dataOffset - 8, 8), long.MaxValue);
            SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
        }
        Assert.Throws<InvalidDataException>(() => ThermalCheckpoint.Restore(field, destination, bytes));
        Assert.Equal(123, destination.Tick);
        Assert.Equal(42, destination.ThetaS[0]);
        Assert.Null(destination.ModelIdentity);
    }

    [Fact]
    public void NonCanonicalSnapshotCannotBeSaved()
    {
        var field = Field();
        var state = new ThermalSnapshot(field.Grid.CellCount);
        field.Step(state);
        Assert.Throws<ArgumentException>(() => ThermalCheckpoint.Capture(field, state));
    }
}
