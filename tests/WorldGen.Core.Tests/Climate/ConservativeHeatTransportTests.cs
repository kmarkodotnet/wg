using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public sealed class ConservativeHeatTransportTests
{
    [Fact]
    public void MatchesIndependentReferenceCases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "conservative_heat_transport_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("ND-167", doc.RootElement.GetProperty("decision").GetString());
        foreach (JsonElement testCase in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            int count = testCase.GetProperty("cellCount").GetInt32();
            int[] edgeI = testCase.GetProperty("edgeI").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int[] edgeJ = testCase.GetProperty("edgeJ").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            double[] g = testCase.GetProperty("conductanceWPerK").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            double[] temperature = testCase.GetProperty("temperatureK").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            double[] expected = testCase.GetProperty("powerW").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var transport = new ConservativeHeatTransport(count, edgeI, edgeJ, g);
            var actual = new double[count];
            transport.ComputePowerW(temperature, actual);
            Assert.Equal(expected, actual);

            Array.Fill(actual, double.NaN);
            transport.ComputePowerW(temperature, actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void FullGridFluxConservesEnergyAcrossFaceSeams()
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(2);
        var conductance = new double[grid.EdgeCount];
        var temperature = new double[grid.CellCount];
        for (int e = 0; e < conductance.Length; e++) conductance[e] = 1.0e10;
        for (int c = 0; c < temperature.Length; c++)
            temperature[c] = 260.0 + 25.0 * grid.CenterZ[c] + 8.0 * grid.CenterX[c];

        var transport = new ConservativeHeatTransport(grid.CellCount, grid.EdgeI, grid.EdgeJ, conductance);
        var power = new double[grid.CellCount];
        transport.ComputePowerW(temperature, power);
        double net = 0.0, absolute = 0.0;
        foreach (double p in power) { net += p; absolute += Math.Abs(p); }
        Assert.True(absolute > 0.0);
        Assert.True(Math.Abs(net) <= absolute * 1.0e-14);

        for (int c = 0; c < power.Length; c++) temperature[c] = 270.0;
        transport.ComputePowerW(temperature, power);
        Assert.All(power, p => Assert.Equal(0.0, p));
    }

    [Fact]
    public void InvalidConductanceAndAliasedOutputAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ConservativeHeatTransport(2, new[] { 0 }, new[] { 1 }, new[] { -1.0 }));
        Assert.Throws<ArgumentException>(() => new ConservativeHeatTransport(2, new[] { 1 }, new[] { 0 }, new[] { 1.0 }));
        var transport = new ConservativeHeatTransport(2, new[] { 0 }, new[] { 1 }, new[] { 1.0 });
        var values = new[] { 260.0, 280.0 };
        Assert.Throws<ArgumentException>(() => transport.ComputePowerW(values, values));
    }
}
