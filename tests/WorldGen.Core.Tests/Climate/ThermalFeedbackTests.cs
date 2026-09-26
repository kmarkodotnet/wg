using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public class ThermalFeedbackTests
{
    private static SurfaceTemperatureField Field(int level = 3, ThermalModelParameters? parameters = null)
    {
        var grid = DenseGridMetrics.Build(level);
        var (kinds, elevation) = ThermalFieldFixture.SyntheticWorld(grid);
        return new SurfaceTemperatureField(grid, kinds, elevation, 0, ThermalFieldFixture.Seed, 0, ThermalFieldFixture.Orbit, parameters);
    }

    [Fact]
    public void CoupledWindMatchesPythonOnEveryCellAndEdgeIncludingSeams()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "testdata", "thermal_feedback_vectors.json")));
        var vectors = document.RootElement;
        Assert.Equal(ThermalModelParameters.ModelVersion, vectors.GetProperty("modelVersion").GetInt32());
        Assert.Equal(ThermalModelParameters.Default.AirFeedbackStrength, vectors.GetProperty("airFeedbackStrength").GetDouble());
        var field = Field(vectors.GetProperty("level").GetInt32());
        var grid = field.Grid;
        var theta = Enumerable.Range(0, grid.CellCount)
            .Select(c => 10.0 * grid.CenterX[c] + 3.0 * grid.CenterY[c] - 2.0 * grid.CenterZ[c]).ToArray();
        var before = (double[])theta.Clone();
        var edges = new double[grid.EdgeCount];
        var speed = new double[grid.CellCount];
        field.Wind.SampleCoupled(vectors.GetProperty("seconds").GetInt64(), theta, edges, speed);
        foreach (var edge in vectors.GetProperty("edges").EnumerateArray())
            Assert.InRange(edges[edge.GetProperty("index").GetInt32()] - edge.GetProperty("velocity").GetDouble(), -1e-9, 1e-9);
        foreach (var cell in vectors.GetProperty("cells").EnumerateArray())
            Assert.InRange(speed[cell.GetProperty("index").GetInt32()] - cell.GetProperty("speed").GetDouble(), -1e-9, 1e-9);
        Assert.Equal(before, theta);
    }

    [Fact]
    public void UniformAirAnomalyAddsNoWindAtDailySnapshot()
    {
        var field = Field(2);
        var theta = Enumerable.Repeat(7.0, field.Grid.CellCount).ToArray();
        var u = new double[field.Grid.EdgeCount]; var v = new double[u.Length];
        var s = new double[theta.Length]; var t = new double[theta.Length];
        field.Wind.SampleCoupled(3 * 86400, theta, u, s);
        field.Wind.EvaluateDay(3, v, t);
        for (int e = 0; e < u.Length; e++) Assert.InRange(u[e] - v[e], -1e-12, 1e-12);
        for (int c = 0; c < s.Length; c++) Assert.InRange(s[c] - t[c], -1e-12, 1e-12);
    }

    [Fact]
    public void AirContrastChangesWindButRemainsBoundedAndIndependentOfCacheHistory()
    {
        var field = Field(2);
        var g = field.Grid;
        var theta = Enumerable.Range(0, g.CellCount).Select(c => 100.0 * g.CenterX[c]).ToArray();
        var u = new double[g.EdgeCount]; var speed = new double[g.CellCount];
        field.Wind.SampleCoupled(-450, new double[theta.Length], u, speed);
        var baseline = (double[])speed.Clone();
        field.Wind.SampleCoupled(-450, theta, u, speed);
        var expectedU = (double[])u.Clone(); var expectedS = (double[])speed.Clone();
        Assert.True(speed.Where((s, c) => Math.Abs(s - baseline[c]) > 0.1).Any());
        Assert.All(u, v => Assert.InRange(v, -40.0000000001, 40.0000000001));
        Assert.All(speed, v => Assert.InRange(v, 0, 40.0000000001));
        field.Wind.SampleCoupled(20 * 86400, theta, u, speed);
        field.Wind.SampleCoupled(-450, theta, u, speed);
        Assert.Equal(expectedU.Select(BitConverter.DoubleToInt64Bits), u.Select(BitConverter.DoubleToInt64Bits));
        Assert.Equal(expectedS.Select(BitConverter.DoubleToInt64Bits), speed.Select(BitConverter.DoubleToInt64Bits));
    }

    [Fact]
    public void DiagnosticsReadTheCoupledWind()
    {
        var field = Field(2);
        var state = new ThermalSnapshot(field.Grid.CellCount);
        field.ResetCanonical(state, 0);
        field.RunTo(state, state.Tick + 12);
        var u = new double[field.Grid.EdgeCount]; var speed = new double[field.Grid.CellCount];
        field.Wind.SampleCoupled(state.Tick * 900, state.ThetaA, u, speed);
        Assert.Equal(speed[10], field.Diagnose(state, 10).WindSpeedMs);
    }

    [Fact]
    public void FeedbackStrengthChangesTheIntegratedTemperature()
    {
        var coupled = Field(2);
        var control = Field(2, new ThermalModelParameters(airFeedbackStrength: 0));
        var a = new ThermalSnapshot(coupled.Grid.CellCount);
        var b = new ThermalSnapshot(control.Grid.CellCount);
        a.Reset(330); b.Reset(330);
        coupled.RunTo(a, 342);
        control.RunTo(b, 342);
        Assert.True(a.ThetaA.Where((value, c) => value != b.ThetaA[c]).Any());
        Assert.True(a.ThetaS.Where((value, c) => value != b.ThetaS[c]).Any());
    }

    [Fact]
    public void ZeroFeedbackStrengthRemovesOnlyTheAnomalyInfluence()
    {
        var field = Field(2, new ThermalModelParameters(airFeedbackStrength: 0));
        var theta = Enumerable.Range(0, field.Grid.CellCount).Select(c => 100.0 * field.Grid.CenterX[c]).ToArray();
        var zero = new double[theta.Length];
        var u = new double[field.Grid.EdgeCount]; var v = new double[u.Length];
        var s = new double[theta.Length]; var t = new double[theta.Length];
        field.Wind.SampleCoupled(34567, theta, u, s);
        field.Wind.SampleCoupled(34567, zero, v, t);
        Assert.Equal(v, u);
        Assert.Equal(t, s);
        Assert.Contains(s, speed => speed > 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalModelParameters(airFeedbackStrength: -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalModelParameters(airFeedbackStrength: 1.01));
    }

    [Fact]
    public void LevelSixSpinUpMeetsOriginalAccuracyGate()
    {
        var field = Field(6);
        var shortRun = new ThermalSnapshot(field.Grid.CellCount);
        var longRun = new ThermalSnapshot(field.Grid.CellCount);
        shortRun.Reset(-10 * 96); longRun.Reset(-30 * 96);
        field.RunTo(shortRun, 0); field.RunTo(longRun, 0);
        double max = 0;
        for (int c = 0; c < field.Grid.CellCount; c++)
        {
            max = Math.Max(max, Math.Abs(shortRun.ThetaS[c] - longRun.ThetaS[c]));
            max = Math.Max(max, Math.Abs(shortRun.ThetaA[c] - longRun.ThetaA[c]));
        }
        Assert.InRange(max, 0, 0.18);
    }

    [Fact]
    public void LongerSpinUpReducesCoupledInitialStateError()
    {
        var field = Field(3);
        var states = new ThermalSnapshot[3];
        for (int run = 0; run < 3; run++)
        {
            states[run] = new ThermalSnapshot(field.Grid.CellCount);
            states[run].Reset(-(run + 1) * 10 * 96);
            field.RunTo(states[run], 0);
        }
        var errors = new double[2];
        for (int run = 0; run < 2; run++)
            for (int c = 0; c < field.Grid.CellCount; c++)
            {
                errors[run] = Math.Max(errors[run], Math.Abs(states[run].ThetaS[c] - states[2].ThetaS[c]));
                errors[run] = Math.Max(errors[run], Math.Abs(states[run].ThetaA[c] - states[2].ThetaA[c]));
            }
        Assert.InRange(errors[0], 0, 0.18);
        Assert.True(errors[1] < errors[0], $"Előfutási hiba nem csökken: {errors[0]} → {errors[1]}");
    }

    [Fact]
    public void InvalidOrAliasedInputIsRejected()
    {
        var field = Field(1);
        var theta = new double[field.Grid.CellCount]; var u = new double[field.Grid.EdgeCount];
        Assert.Throws<ArgumentException>(() => field.Wind.SampleCoupled(0, theta, u, theta));
        theta[0] = double.NaN;
        Assert.Throws<ArgumentException>(() => field.Wind.SampleCoupled(0, theta, u, new double[theta.Length]));
    }
}
