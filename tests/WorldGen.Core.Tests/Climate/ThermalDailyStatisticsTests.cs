using System;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

public sealed class ThermalDailyStatisticsTests
{
    private static SurfaceTemperatureField CreateField(ulong seed = 17)
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(1);
        (SurfaceThermalKind[] kinds, double[] elevation) = ThermalFieldFixture.SyntheticWorld(grid);
        return new SurfaceTemperatureField(grid, kinds, elevation, 0.0, seed, 0.0, ThermalFieldFixture.Orbit);
    }

    [Fact]
    public void DayUsesEveryTickStartInCanonicalOrder()
    {
        SurfaceTemperatureField field = CreateField();
        var state = new ThermalSnapshot(field.Grid.CellCount);
        ThermalDailyStatistics day = ThermalDailyStatisticsCalculator.Compute(field, state, 0);

        var manual = new ThermalSnapshot(field.Grid.CellCount);
        field.StateAt(manual, 0);
        var baseline = new double[field.Grid.CellCount];
        int cell = 3;
        double sumSurface = 0.0, sumAir = 0.0;
        double minSurface = double.PositiveInfinity, maxSurface = double.NegativeInfinity;
        for (int tick = 0; tick < SimulationTime.TicksPerDay; tick++)
        {
            field.BaselineAt(manual.Tick, baseline);
            double surface = baseline[cell] + manual.ThetaS[cell];
            sumSurface += surface;
            sumAir += baseline[cell] + manual.ThetaA[cell];
            minSurface = Math.Min(minSurface, surface);
            maxSurface = Math.Max(maxSurface, surface);
            field.Step(manual);
        }

        Assert.Equal(0, day.DayIndex);
        Assert.Equal(field.ModelIdentity, day.ModelIdentity);
        Assert.Equal(SimulationTime.TicksPerDay, state.Tick);
        Assert.Equal(sumSurface / SimulationTime.TicksPerDay, day.MeanSurfaceK[cell]);
        Assert.Equal(sumAir / SimulationTime.TicksPerDay, day.MeanAirK[cell]);
        Assert.Equal(minSurface, day.MinSurfaceK[cell]);
        Assert.Equal(maxSurface, day.MaxSurfaceK[cell]);
        Assert.Equal(manual.ThetaS, state.ThetaS);
        Assert.Equal(manual.ThetaA, state.ThetaA);
    }

    [Fact]
    public void DayIsIndependentOfPreviousQueriesAndSeed()
    {
        SurfaceTemperatureField field = CreateField();
        var reused = new ThermalSnapshot(field.Grid.CellCount);
        ThermalDailyStatisticsCalculator.Compute(field, reused, 4);
        ThermalDailyStatistics afterOtherDay = ThermalDailyStatisticsCalculator.Compute(field, reused, 1);
        ThermalDailyStatistics fresh = ThermalDailyStatisticsCalculator.Compute(
            CreateField(), new ThermalSnapshot(field.Grid.CellCount), 1);
        for (int cell = 0; cell < field.Grid.CellCount; cell++)
        {
            Assert.Equal(fresh.MeanSurfaceK[cell], afterOtherDay.MeanSurfaceK[cell]);
            Assert.Equal(fresh.MeanAirK[cell], afterOtherDay.MeanAirK[cell]);
            Assert.InRange(fresh.MeanSurfaceK[cell], fresh.MinSurfaceK[cell], fresh.MaxSurfaceK[cell]);
            Assert.InRange(fresh.MeanAirK[cell], fresh.MinAirK[cell], fresh.MaxAirK[cell]);
        }
        ThermalDailyStatistics differentSeed = ThermalDailyStatisticsCalculator.Compute(
            CreateField(18), new ThermalSnapshot(field.Grid.CellCount), 1);
        Assert.NotEqual(fresh.ModelIdentity, differentSeed.ModelIdentity);
        Assert.NotEqual(fresh.MeanSurfaceK[0], differentSeed.MeanSurfaceK[0]);
    }

    [Fact]
    public void RejectsOverflowingDayWithoutChangingState()
    {
        SurfaceTemperatureField field = CreateField();
        var state = new ThermalSnapshot(field.Grid.CellCount);
        Assert.Throws<OverflowException>(() => ThermalDailyStatisticsCalculator.Compute(field, state, long.MaxValue));
        Assert.Equal(0, state.Tick);
    }
}
