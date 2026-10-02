using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Climate;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public class TemperatureIndexSnowTests
{
    [Fact]
    public void PythonVectorsAndWaterBalance()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "testdata", "snow_melt_vectors.json")));
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            double D(string name) => v.GetProperty(name).GetDouble();
            double pdd = TemperatureIndexSnow.PositiveDegreeDays(D("airK"), D("days"));
            Assert.Equal(D("pdd"), pdd);
            var result = TemperatureIndexSnow.Step(D("snow"), D("snowfall"), pdd, D("factor"));
            Assert.Equal(D("remaining"), result.RemainingWaterEquivalentM);
            Assert.Equal(D("melt"), result.MeltWaterEquivalentM);
            Assert.Equal(D("potential"), result.PotentialWaterEquivalentM);
            Assert.InRange(Math.Abs(result.RemainingWaterEquivalentM + result.MeltWaterEquivalentM
                - D("snow") - D("snowfall")), 0, 1e-14);
        }
    }

    [Fact]
    public void KnownAnswerAndParameterEffects()
    {
        Assert.Equal(6, TemperatureIndexSnow.PositiveDegreeDays(275.15, 3));
        var result = TemperatureIndexSnow.Step(0.1, 0.02, 6, 0.003);
        Assert.Equal(0.102, result.RemainingWaterEquivalentM, 14);
        Assert.Equal(0.018, result.MeltWaterEquivalentM, 14);
        Assert.Equal(0, TemperatureIndexSnow.Step(0.01, 0, 6, 0.003).RemainingWaterEquivalentM);
        Assert.Equal(0.036, TemperatureIndexSnow.MeltPotential(6, 0.006), 14);
        Assert.Equal(0.018, TemperatureIndexSnow.MeltPotential(3, 0.006), 14);
        // Az átlagolás előtti pozitív rész szükséges: -2/+2 C átlagosan 0 C.
        Assert.Equal(1, TemperatureIndexSnow.PositiveDegreeDays(271.15, 0.5)
            + TemperatureIndexSnow.PositiveDegreeDays(275.15, 0.5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidInputsRejected(double bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TemperatureIndexSnow.PositiveDegreeDays(bad, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TemperatureIndexSnow.PositiveDegreeDays(274, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => TemperatureIndexSnow.Step(bad, 0, 1, 0.003));
        Assert.Throws<ArgumentOutOfRangeException>(() => TemperatureIndexSnow.Step(0, bad, 1, 0.003));
        Assert.Throws<ArgumentOutOfRangeException>(() => TemperatureIndexSnow.Step(0, 0, bad, 0.003));
        Assert.Throws<ArgumentOutOfRangeException>(() => TemperatureIndexSnow.Step(0, 0, 1, bad));
    }

    [Fact]
    public void OverflowRejected()
    {
        Assert.Throws<OverflowException>(() => TemperatureIndexSnow.PositiveDegreeDays(double.MaxValue, 2));
        Assert.Throws<OverflowException>(() => TemperatureIndexSnow.MeltPotential(double.MaxValue, 2));
        Assert.Throws<OverflowException>(() => TemperatureIndexSnow.Step(double.MaxValue, double.MaxValue, 0, 0));
    }

    [Fact]
    public void ParallelCallsAreBitIdentical()
    {
        var values = new double[256];
        Parallel.For(0, values.Length, i => values[i] = TemperatureIndexSnow.Step(
            1, 0.2, TemperatureIndexSnow.PositiveDegreeDays(270 + i / 10.0, 365.25), 0.003).RemainingWaterEquivalentM);
        for (int i = 0; i < values.Length; i++)
            Assert.Equal(values[i], TemperatureIndexSnow.Step(1, 0.2,
                TemperatureIndexSnow.PositiveDegreeDays(270 + i / 10.0, 365.25), 0.003).RemainingWaterEquivalentM);
    }
}

public class ThermalMeltExposureTests : IClassFixture<ThermalAnnualFixture>
{
    private readonly ThermalAnnualFixture _fx;
    public ThermalMeltExposureTests(ThermalAnnualFixture fixture) => _fx = fixture;

    [Fact]
    public void PythonThermalExposureVectors()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "testdata", "thermal_melt_exposure_vectors.json")));
        var actual = ThermalMeltExposure.Compute(_fx.CreateField(_fx.IceFreeKinds), _fx.SampleDays);
        var expected = doc.RootElement;
        Assert.Equal(expected.GetProperty("days").EnumerateArray().Select(x => x.GetInt64()).ToArray(),
            actual.SampleDays.ToArray());
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            Assert.InRange(Math.Abs(expected.GetProperty("pdd")[c].GetDouble()
                - actual.PositiveDegreeDays[c]), 0, 1e-9);
            Assert.InRange(Math.Abs(expected.GetProperty("warmest")[c].GetDouble()
                - actual.WarmestSampleDayMeanAirK[c]), 0, 1e-9);
            Assert.Equal(expected.GetProperty("warmestDay")[c].GetInt64(), actual.WarmestSampleDay[c]);
            Assert.InRange(Math.Abs(expected.GetProperty("warmestBase")[c].GetDouble()
                - actual.WarmestSampleDayMeanBaselineK[c]), 0, 1e-9);
            Assert.InRange(Math.Abs(expected.GetProperty("warmestAnomaly")[c].GetDouble()
                - actual.WarmestSampleDayMeanAirAnomalyK[c]), 0, 1e-9);
            Assert.InRange(Math.Abs(actual.WarmestSampleDayMeanAirK[c]
                - actual.WarmestSampleDayMeanBaselineK[c] - actual.WarmestSampleDayMeanAirAnomalyK[c]), 0, 1e-10);
        }
    }

    [Fact]
    public void ExposureMatchesDailyBoundsAndParallelReplay()
    {
        var field = _fx.CreateField(_fx.IceFreeKinds);
        var exposure = ThermalMeltExposure.Compute(field, _fx.SampleDays);
        field.UseParallelLocalStep = true;
        var replay = ThermalMeltExposure.Compute(field, _fx.SampleDays);
        Assert.Equal(exposure.PositiveDegreeDays.ToArray(), replay.PositiveDegreeDays.ToArray());
        Assert.Equal(exposure.WarmestSampleDayMeanAirK.ToArray(), replay.WarmestSampleDayMeanAirK.ToArray());
        Assert.Equal(exposure.WarmestSampleDay.ToArray(), replay.WarmestSampleDay.ToArray());
        Assert.Equal(exposure.WarmestSampleDayMeanBaselineK.ToArray(), replay.WarmestSampleDayMeanBaselineK.ToArray());
        Assert.Equal(exposure.WarmestSampleDayMeanAirAnomalyK.ToArray(), replay.WarmestSampleDayMeanAirAnomalyK.ToArray());
        var state = new ThermalSnapshot(_fx.Grid.CellCount);
        var lower = new double[_fx.Grid.CellCount];
        var upper = new double[lower.Length];
        var warmest = Enumerable.Repeat(double.NegativeInfinity, lower.Length).ToArray();
        foreach (long day in exposure.SampleDays)
        {
            var stats = ThermalDailyStatisticsCalculator.Compute(field, state, day);
            for (int c = 0; c < lower.Length; c++)
            {
                lower[c] += Math.Max(stats.MeanAirK[c] - 273.15, 0);
                upper[c] += Math.Max(stats.MaxAirK[c] - 273.15, 0);
                warmest[c] = Math.Max(warmest[c], stats.MeanAirK[c]);
                if (day == exposure.WarmestSampleDay[c])
                    Assert.Equal(stats.MeanAirK[c], exposure.WarmestSampleDayMeanAirK[c]);
            }
        }
        double weight = field.Orbit.OrbitalPeriodDays / exposure.SampleDays.Count;
        for (int c = 0; c < lower.Length; c++)
        {
            Assert.Equal(warmest[c], exposure.WarmestSampleDayMeanAirK[c]);
            Assert.InRange(exposure.PositiveDegreeDays[c], lower[c] * weight - 1e-9, upper[c] * weight + 1e-9);
        }
        Assert.Contains(exposure.PositiveDegreeDays, x => x > 0);
        Assert.Contains(exposure.PositiveDegreeDays, x => x == 0);
    }

    [Fact]
    public void InvalidSamplingRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ThermalMeltExposure.Compute(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThermalMeltExposure.Compute(_fx.CreateField(_fx.IceFreeKinds), 0));
        Assert.Throws<ArgumentException>(() => ThermalMeltExposure.Compute(_fx.CreateField(_fx.IceFreeKinds), 9));
    }
}
