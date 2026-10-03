using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Climate;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public class PeriodicHeatBalanceTests
{
    [Fact]
    public void AnalyticAndPythonVectors()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "periodic_heat_balance_vectors.json")));
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            double[] D(string name) => v.GetProperty(name).EnumerateArray().Select(x => x.GetDouble()).ToArray();
            int[] I(string name) => v.GetProperty(name).EnumerateArray().Select(x => x.GetInt32()).ToArray();
            double[][] forcing = v.GetProperty("forcing").EnumerateArray().Select(row => row.EnumerateArray().Select(x => x.GetDouble()).ToArray()).ToArray();
            var actual = PeriodicHeatBalance.Solve(D("area"), I("edge_i"), I("edge_j"), D("conductance"), D("capacity"), forcing, v.GetProperty("period").GetDouble(), v.GetProperty("feedback").GetDouble());
            for (int j = 0; j < actual.Length; j++)
                for (int c = 0; c < actual[j].Length; c++)
                    Assert.InRange(Math.Abs(actual[j][c] - v.GetProperty("expected")[j][c].GetDouble()), 0, 1e-10);
        }
    }

    [Fact]
    public void CapacityDampsAndDelaysSeasonalResponseAndCallsAreIndependent()
    {
        double[][] forcing = { new[] { 1.0 }, new[] { 0.0 }, new[] { -1.0 }, new[] { 0.0 } };
        double[][] Run(double capacity) => PeriodicHeatBalance.Solve(new[] { 1.0 }, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<double>(), new[] { capacity }, forcing, 4, 1);
        var a = Run(1); var b = Run(10);
        Assert.Equal(0.4, a[0][0], 12); Assert.Equal(0.2, a[1][0], 12);
        Assert.True(Math.Abs(b[0][0]) < a[0][0]); Assert.True(b[1][0] > 0);
        Parallel.For(0, 8, _ => { var replay = Run(1); for (int j = 0; j < 4; j++) Assert.Equal(a[j][0], replay[j][0]); });
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(0));
    }
}

public class SeasonalEnergyBalanceTests : IClassFixture<ThermalAnnualFixture>
{
    private readonly ThermalAnnualFixture _fx;
    public SeasonalEnergyBalanceTests(ThermalAnnualFixture fixture) => _fx = fixture;

    [Fact]
    public void SharpIceBoundaryAtLevelSixClosesTheFullEnergyBalance()
    {
        var grid = WorldGen.Core.Grid.DenseGridMetrics.Build(6);
        var kinds = new SurfaceThermalKind[grid.CellCount];
        var elevation = new double[grid.CellCount];
        for (int c = 0; c < grid.CellCount; c++)
        {
            bool ice = grid.CenterZ[c] > 0.5;
            kinds[c] = ice ? SurfaceThermalKind.Ice : SurfaceThermalKind.Ocean;
            elevation[c] = ice ? 7000 : -3000;
        }
        var model = new SeasonalEnergyBalance(grid, kinds, elevation, 0, 0,
            new ThermalOrbit(365.25, 1, 23.44 * Math.PI / 180), ThermalModelParameters.Default);
        Assert.InRange(model.MaxResidualWm2, 0, 0.001);
        Assert.All(model.AnnualMeanK, t => Assert.InRange(t, 100, 400));
    }

    [Fact]
    public void PythonPlanetOracleAndPeriodicEnergyBalance()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "seasonal_energy_balance_vectors.json")));
        var data = doc.RootElement;
        var model = new SeasonalEnergyBalance(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0, data.GetProperty("cycle").GetDouble(),
            new ThermalOrbit(365.25, 1, 23.44 * Math.PI / 180), ThermalModelParameters.Default);
        Assert.InRange(model.MaxResidualWm2, 0, 1e-8);
        for (int j = 0; j < model.PhaseCount; j++)
            for (int c = 0; c < _fx.Grid.CellCount; c++)
                Assert.InRange(Math.Abs(model.TemperatureK(c, j * 365.25 / model.PhaseCount) - 273.15 - data.GetProperty("temperatureC")[j][c].GetDouble()), 0, 1e-8);
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            Assert.Equal(model.TemperatureK(c, 0), model.TemperatureK(c, 365.25));
            Assert.Equal(model.TemperatureK(c, 0), model.TemperatureK(c, -365.25));
        }
    }
}
