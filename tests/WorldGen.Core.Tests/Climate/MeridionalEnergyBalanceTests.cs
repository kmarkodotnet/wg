using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public sealed class MeridionalEnergyBalanceTests
{
    [Fact]
    public void LevelOneGeometryAndCorrectionMatchPythonReference()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "meridional_energy_balance_vectors.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        DenseGridMetrics grid = DenseGridMetrics.Build(root.GetProperty("level").GetInt32());
        double[] target = root.GetProperty("targetK").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        double[] expectedG = root.GetProperty("conductanceWPerK").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        double[] expectedCorrection = root.GetProperty("correctionK").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        double[] actualG = MeridionalEnergyBalance.BuildConductance(grid);
        double[] actualCorrection = MeridionalEnergyBalance.SolveCorrection(grid, target);
        for (int e = 0; e < grid.EdgeCount; e++)
            Assert.True(Math.Abs(actualG[e] - expectedG[e]) <= Math.Max(1.0, expectedG[e]) * 1e-11);
        for (int c = 0; c < grid.CellCount; c++)
            Assert.True(Math.Abs(actualCorrection[c] - expectedCorrection[c]) <= 1e-8);
    }

    [Fact]
    public void AnnualTransportIsAreaNeutralAndConstantFieldIsUnchanged()
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(2);
        var target = new double[grid.CellCount];
        for (int c = 0; c < target.Length; c++)
            target[c] = 280.0 + 25.0 * grid.CenterZ[c];
        double[] correction = MeridionalEnergyBalance.SolveCorrection(grid, target);
        double weighted = 0.0, area = 0.0;
        for (int c = 0; c < target.Length; c++)
        {
            weighted += grid.Area[c] * correction[c];
            area += grid.Area[c];
        }
        Assert.True(Math.Abs(weighted / area) < 1e-9);
        Assert.Contains(correction, value => value > 0.0);
        Assert.Contains(correction, value => value < 0.0);

        Array.Fill(target, 280.0);
        Assert.All(MeridionalEnergyBalance.SolveCorrection(grid, target), value => Assert.Equal(0.0, value));
        Assert.All(MeridionalEnergyBalance.SolveCorrection(grid, target, 0.0), value => Assert.Equal(0.0, value));
    }

    [Fact]
    public void RepeatedAndParallelSolutionsAreBitIdenticalAndSatisfyTheBalance()
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(2);
        var target = new double[grid.CellCount];
        for (int c = 0; c < target.Length; c++)
            target[c] = 275.0 + 24.0 * grid.CenterZ[c] + 3.0 * grid.CenterX[c];
        double[] expected = MeridionalEnergyBalance.SolveCorrection(grid, target);
        double[][] parallel = new double[4][];
        Parallel.For(0, parallel.Length, i => parallel[i] = MeridionalEnergyBalance.SolveCorrection(grid, target));
        foreach (double[] result in parallel)
            for (int c = 0; c < target.Length; c++)
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected[c]), BitConverter.DoubleToInt64Bits(result[c]));

        double[] conductance = MeridionalEnergyBalance.BuildConductance(grid);
        var transport = new ConservativeHeatTransport(grid.CellCount, grid.EdgeI, grid.EdgeJ, conductance);
        var balanced = new double[target.Length];
        for (int c = 0; c < target.Length; c++) balanced[c] = target[c] + expected[c];
        var power = new double[target.Length];
        transport.ComputePowerW(balanced, power);
        for (int c = 0; c < target.Length; c++)
        {
            double fluxWm2 = power[c] / grid.Area[c];
            double feedbackWm2 = MeridionalEnergyBalance.RadiativeFeedbackWm2K * expected[c];
            Assert.True(Math.Abs(fluxWm2 - feedbackWm2) < 1e-7);
            Assert.True(Math.Abs(expected[c]) < 25.0);
        }
    }

    [Fact]
    public void DiffusionParameterAndInvalidEdgesAreExplicit()
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(2);
        var target = new double[grid.CellCount];
        for (int c = 0; c < target.Length; c++) target[c] = 280.0 + 15.0 * grid.CenterZ[c];
        double[] disabled = MeridionalEnergyBalance.SolveCorrection(grid, target, 0.0);
        double[] half = MeridionalEnergyBalance.SolveCorrection(grid, target,
            MeridionalEnergyBalance.DiffusionWm2K * 0.5);
        double[] full = MeridionalEnergyBalance.SolveCorrection(grid, target);
        Assert.All(disabled, value => Assert.Equal(0.0, value));
        Assert.Contains(Enumerable.Range(0, target.Length), c => half[c] != full[c]);
        Assert.Throws<ArgumentException>(() => MeridionalEnergyBalance.SolveCorrection(grid, new double[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeridionalEnergyBalance.BuildConductance(grid, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeridionalEnergyBalance.SolveCorrection(grid, target,
            feedbackWm2K: 0.0));
    }
}
