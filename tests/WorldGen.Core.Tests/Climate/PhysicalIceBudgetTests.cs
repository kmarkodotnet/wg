using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGen.Core.Climate;
using WorldGen.Core.Hydrology;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public class PhysicalIceBudgetTests
{
    [Fact]
    public void PythonVectorsAndPhysicalBoundaries()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "testdata", "physical_ice_budget_vectors.json")));
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            var actual = PhysicalIceBudget.Evaluate(v.GetProperty("temperature").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                v.GetProperty("period").GetDouble(), v.GetProperty("precipitation").GetDouble(),
                (SurfaceThermalKind)v.GetProperty("kind").GetInt32());
            var expected = v.GetProperty("expected");
            Assert.Equal(expected.GetProperty("snow").GetDouble(), actual.SnowfallM, 12);
            Assert.Equal(expected.GetProperty("melt").GetDouble(), actual.MeltPotentialM, 12);
            Assert.Equal(expected.GetProperty("margin").GetDouble(), actual.PersistenceMarginK, 12);
            Assert.Equal(expected.GetProperty("classification").GetInt32(), (int)actual.Classification);
        }
        Assert.Equal(LakesIceErosion.IceClass.None,
            PhysicalIceBudget.Evaluate(new[] { 250.0 }, 365.25, 0, SurfaceThermalKind.Land).Classification);
        Assert.Throws<ArgumentException>(() => PhysicalIceBudget.Evaluate(new[] { 270.0 }, 0, 1, SurfaceThermalKind.Land));
        Assert.Throws<ArgumentException>(() => PhysicalIceBudget.Evaluate(new[] { 270.0 }, 365, -1, SurfaceThermalKind.Land));
        var thresholds = new BiomeClassification.PrecipitationThresholds(0.1, 0.2, 0.3);
        Assert.Equal(Biome.Tundra, BiomeClassification.ClassifyWithIce(250, false, 0, thresholds, false));
        Assert.Equal(Biome.Ocean, BiomeClassification.ClassifyWithIce(250, true, 0, thresholds, false));
        Assert.Equal(Biome.SeaIce, BiomeClassification.ClassifyWithIce(270, true, 0, thresholds, true));
    }

    [Fact]
    public void CalibrationPreservesAreaMeanAndDryWorldStaysDry()
    {
        double[] area = { 1, 2, 3 }, proxy = { 0, 2, 4 };
        var actual = PhysicalIceBudget.CalibratePrecipitation(area, proxy);
        Assert.Equal(0.97, actual.Select((x, i) => x * area[i]).Sum() / area.Sum(), 12);
        Assert.Equal(2 * actual[1], actual[2]);
        Assert.All(PhysicalIceBudget.CalibratePrecipitation(area, new double[3]), x => Assert.Equal(0, x));
        Assert.Throws<ArgumentException>(() => PhysicalIceBudget.CalibratePrecipitation(area, new[] { 0.0, -1, 4 }));
        Assert.Throws<OverflowException>(() => PhysicalIceBudget.CalibratePrecipitation(new[] { double.MaxValue }, new[] { 2.0 }));
    }
}

public class SeasonalCoupledTests : IClassFixture<ThermalAnnualFixture>
{
    private readonly ThermalAnnualFixture _fx;
    public SeasonalCoupledTests(ThermalAnnualFixture fixture) => _fx = fixture;

    [Fact]
    public void ActiveBaselineWindAndDailyStateMatchIndependentPythonOracle()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "testdata", "seasonal_coupled_vectors.json")));
        var expected = doc.RootElement;
        var field = new SurfaceTemperatureField(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0,
            ThermalAnnualFixture.Seed, 0, _fx.Orbit);
        var state = new ThermalSnapshot(_fx.Grid.CellCount);
        long tick = expected.GetProperty("tick").GetInt64();
        field.StateAt(state, tick);
        void Compare(string key, double[] values)
        {
            var data = expected.GetProperty(key);
            for (int i = 0; i < values.Length; i++) Assert.InRange(Math.Abs(values[i] - data[i].GetDouble()), 0, 1e-8);
        }
        Compare("thetaS", state.ThetaS); Compare("thetaA", state.ThetaA);
        var baseline = new double[_fx.Grid.CellCount];
        field.Baseline.Sample(tick * SimulationTime.TickSeconds, new double[baseline.Length], baseline);
        Compare("baselineK", baseline);
        var edges = new double[_fx.Grid.EdgeCount]; var speeds = new double[baseline.Length];
        field.Wind.SampleCoupled(tick * SimulationTime.TickSeconds, state.ThetaA, edges, speeds);
        Compare("edgeWind", edges); Compare("cellSpeed", speeds);
        var annual = ThermalAnnualStatisticsCalculator.Compute(field, new ThermalSnapshot(baseline.Length), 4);
        for (int c = 0; c < baseline.Length; c++)
        {
            Assert.InRange(Math.Abs(annual.MeanAirK[c] - expected.GetProperty("annual").GetProperty("meanAirK")[c].GetDouble()), 0, 1e-8);
            Assert.InRange(Math.Abs(annual.MeanSurfaceK[c] - expected.GetProperty("annual").GetProperty("meanSurfaceK")[c].GetDouble()), 0, 1e-8);
        }
        var parallel = new SurfaceTemperatureField(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0,
            ThermalAnnualFixture.Seed, 0, _fx.Orbit)
        { UseParallelLocalStep = true };
        var replay = new ThermalSnapshot(baseline.Length);
        parallel.StateAt(replay, tick);
        Assert.Equal(state.ThetaS, replay.ThetaS); Assert.Equal(state.ThetaA, replay.ThetaA);
    }

    [Fact]
    public void PhysicalClassificationIsNotAPercentileAndHighWetTerrainCanRetainIce()
    {
        var a = ThermalClimateCalculator.Compute(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0,
            ThermalAnnualFixture.Seed, 0, _fx.Orbit, sampleDays: 4, permanentIcePercentile: 0.01);
        var b = ThermalClimateCalculator.Compute(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0,
            ThermalAnnualFixture.Seed, 0, _fx.Orbit, sampleDays: 4, permanentIcePercentile: 0.9);
        Assert.NotNull(a.RefinedPhysicalIce);
        Assert.Equal(a.RefinedClass, b.RefinedClass);
        Assert.Contains(LakesIceErosion.IceClass.PermanentIce, a.RefinedClass);
        for (int c = 0; c < _fx.Grid.CellCount; c++)
            Assert.Equal(a.RefinedClass[c] == LakesIceErosion.IceClass.PermanentIce,
                a.RefinedPhysicalIce!.PersistenceMarginK[c] < 0);
    }

    [Fact]
    public void SeasonalPhaseRefinementConvergesAndDeepTimeIsInsideEnergyBalance()
    {
        var orbit = new ThermalOrbit(365.25, 1, 23.44 * Math.PI / 180);
        SeasonalEnergyBalance Model(int phases, double cycle = 0) => new SeasonalEnergyBalance(_fx.Grid,
            _fx.IceFreeKinds, _fx.Elevation, 0, cycle, orbit, ThermalModelParameters.Default, phases);
        var a = Model(24); var b = Model(48); var c = Model(96);
        double coarse = 0, fine = 0;
        for (int day = 0; day < 365; day += 3)
            for (int cell = 0; cell < _fx.Grid.CellCount; cell++)
            {
                coarse += Math.Abs(a.TemperatureK(cell, day) - b.TemperatureK(cell, day));
                fine += Math.Abs(b.TemperatureK(cell, day) - c.TemperatureK(cell, day));
            }
        Assert.True(fine < coarse, $"Fázisfinomítás: 24→48={coarse}, 48→96={fine}");
        var warm = Model(48, 4);
        for (int cell = 0; cell < _fx.Grid.CellCount; cell++)
            Assert.InRange(warm.TemperatureK(cell, 90) - b.TemperatureK(cell, 90), 4 - 1e-8, 4 + 1e-8);
        var baseline = new ThermalBaseline(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0,
            ThermalAnnualFixture.Seed, 25e6, orbit);
        double expectedCycle = Temperature.ClimateCycleTemperatureK(ThermalAnnualFixture.Seed, 25e6)
            + WorldGen.Core.Tectonics.DeepTimeErosionGlaciation.GlobalTempOffset(25);
        Assert.Equal(expectedCycle, baseline.CycleK);
    }
}
