using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

/// <summary>
/// Az ND-158 orákulum-világa: a <c>thermal_annual_ref.py</c> <c>oracle_world</c>
/// szabálya. Jégmentes (Land/Ocean/Freshwater), sarki fennsíkkal — épp azért,
/// hogy a kétmenetes út tényleg keletkeztessen jeget.
/// </summary>
public sealed class ThermalAnnualFixture
{
    public const ulong Seed = 184482873278464UL;

    public JsonElement Vectors { get; }
    public DenseGridMetrics Grid { get; }
    public SurfaceThermalKind[] IceFreeKinds { get; }
    public double[] Elevation { get; }
    public ThermalOrbit Orbit { get; }
    public int SampleDays { get; }

    public ThermalAnnualFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "thermal_annual_vectors.json");
        Vectors = JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        Grid = DenseGridMetrics.Build(Vectors.GetProperty("level").GetInt32());
        (IceFreeKinds, Elevation) = OracleWorld(Grid);
        Orbit = new ThermalOrbit(
            Vectors.GetProperty("orbitalPeriodDays").GetDouble(),
            Vectors.GetProperty("rotationPeriodDays").GetDouble(),
            Vectors.GetProperty("axialTiltRad").GetDouble());
        SampleDays = Vectors.GetProperty("sampleDays").GetInt32();
    }

    /// <summary>A Python-referencia <c>oracle_world</c> szabálya.</summary>
    public static (SurfaceThermalKind[] kinds, double[] elevation) OracleWorld(DenseGridMetrics grid)
    {
        var kinds = new SurfaceThermalKind[grid.CellCount];
        var elevation = new double[grid.CellCount];
        for (int k = 0; k < grid.CellCount; k++)
        {
            double x = grid.CenterX[k], y = grid.CenterY[k], z = grid.CenterZ[k];
            double s = 0.8 * x + 0.3 * y + 0.2 * z;
            if (Math.Abs(z) > 0.6) { kinds[k] = SurfaceThermalKind.Land; elevation[k] = 7000.0; }
            else if (s < 0.05) { kinds[k] = SurfaceThermalKind.Ocean; elevation[k] = -3000.0; }
            else if (x > 0.5 && y > 0.3 && s < 0.6) { kinds[k] = SurfaceThermalKind.Freshwater; elevation[k] = 100.0; }
            else { kinds[k] = SurfaceThermalKind.Land; elevation[k] = 150.0 + 2500.0 * (s - 0.05); }
        }
        return (kinds, elevation);
    }

    public SurfaceTemperatureField CreateField(SurfaceThermalKind[] kinds)
        => new SurfaceTemperatureField(Grid, kinds, Elevation, 0.0, Seed, 0.0, Orbit);

    /// <summary>A RÉGI, abszolút küszöbű klíma (a `permanentIcePercentile: null` út).</summary>
    public ThermalClimate ComputeClimate()
        => ThermalClimateCalculator.Compute(Grid, IceFreeKinds, Elevation, 0.0, Seed, 0.0, Orbit,
            sampleDays: SampleDays, permanentIcePercentile: null);

    /// <summary>Az ND-159 PERCENTILIS küszöbű klíma — ez az alapértelmezett út.</summary>
    public ThermalClimate ComputePercentileClimate()
        => ThermalClimateCalculator.Compute(Grid, IceFreeKinds, Elevation, 0.0, Seed, 0.0, Orbit,
            sampleDays: SampleDays);

    public double Percentile => Vectors.GetProperty("permanentIcePercentile").GetDouble();
}

/// <summary>Éves statisztika (ND-158) — ismert-válasz, tisztaság, élesetek.</summary>
public class ThermalAnnualStatisticsTests : IClassFixture<ThermalAnnualFixture>
{
    /// <summary>
    /// A hőlánc az ND-24 konstrukciós tan-warpján át nyers Math-ot is érint,
    /// ezért a Python-összevetés — az ND-142 szélvektoraival egyezően —
    /// 1e-9 toleranciájú, nem byte-egzakt.
    /// </summary>
    private const double Tolerance = 1e-9;

    private readonly ThermalAnnualFixture _fx;

    public ThermalAnnualStatisticsTests(ThermalAnnualFixture fixture) => _fx = fixture;

    [Fact]
    public void OracleWorldMatchesTheReferenceInputs()
    {
        JsonElement baseKinds = _fx.Vectors.GetProperty("baseKinds");
        JsonElement elevation = _fx.Vectors.GetProperty("elevationM");
        Assert.Equal(_fx.Grid.CellCount, baseKinds.GetArrayLength());
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            Assert.Equal(baseKinds[c].GetInt32(), (int)_fx.IceFreeKinds[c]);
            Assert.InRange(_fx.Elevation[c] - elevation[c].GetDouble(), -1e-9, 1e-9);
        }
    }

    [Fact]
    public void SampleDaysFollowTheFloorFormula()
    {
        long[] days = ThermalAnnualStatisticsCalculator.SampleDayIndices(8.0, 4);
        Assert.Equal(new long[] { 0, 2, 4, 6 }, days);

        // Föld-szerű év, 12 minta: 365,25/12 = 30,4375 nap lépésköz.
        long[] earth = ThermalAnnualStatisticsCalculator.SampleDayIndices(365.25, 12);
        Assert.Equal(new long[] { 0, 30, 60, 91, 121, 152, 182, 213, 243, 273, 304, 334 }, earth);

        // Eltolt évkezdet: minden minta ugyanannyival tolódik.
        long[] shifted = ThermalAnnualStatisticsCalculator.SampleDayIndices(365.25, 12, 1000);
        for (int j = 0; j < earth.Length; j++) Assert.Equal(earth[j] + 1000, shifted[j]);
    }

    [Fact]
    public void RejectsSampleCountThatWouldCountTheSameDayTwice()
    {
        // 4 napos év, 12 minta: a floor-képlet ismétlődő napokat adna, és az
        // átlag CSENDBEN kétszer súlyozná ugyanazt a napot.
        Assert.Throws<ArgumentException>(() => ThermalAnnualStatisticsCalculator.SampleDayIndices(4.0, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThermalAnnualStatisticsCalculator.SampleDayIndices(8.0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThermalAnnualStatisticsCalculator.SampleDayIndices(0.0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThermalAnnualStatisticsCalculator.SampleDayIndices(double.PositiveInfinity, 4));
    }

    [Fact]
    public void IceFreePassMatchesThePythonOracle()
    {
        SurfaceTemperatureField field = _fx.CreateField(_fx.IceFreeKinds);
        ThermalAnnualStatistics annual = ThermalAnnualStatisticsCalculator.Compute(
            field, new ThermalSnapshot(_fx.Grid.CellCount), _fx.SampleDays);
        AssertMatches(_fx.Vectors.GetProperty("absolute").GetProperty("iceFree"), annual);
    }

    [Fact]
    public void AnnualMeanIsTheEqualWeightedMeanOfTheSampledDays()
    {
        SurfaceTemperatureField field = _fx.CreateField(_fx.IceFreeKinds);
        long[] days = ThermalAnnualStatisticsCalculator.SampleDayIndices(
            _fx.Orbit.OrbitalPeriodDays, _fx.SampleDays);

        var sumSurface = new double[_fx.Grid.CellCount];
        var minSurface = new double[_fx.Grid.CellCount];
        Array.Fill(minSurface, double.PositiveInfinity);
        var manual = new ThermalSnapshot(_fx.Grid.CellCount);
        foreach (long day in days)
        {
            ThermalDailyStatistics stats = ThermalDailyStatisticsCalculator.Compute(field, manual, day);
            for (int c = 0; c < sumSurface.Length; c++)
            {
                sumSurface[c] += stats.MeanSurfaceK[c];
                if (stats.MinSurfaceK[c] < minSurface[c]) minSurface[c] = stats.MinSurfaceK[c];
            }
        }

        ThermalAnnualStatistics annual = ThermalAnnualStatisticsCalculator.Compute(
            _fx.CreateField(_fx.IceFreeKinds), new ThermalSnapshot(_fx.Grid.CellCount), _fx.SampleDays);
        Assert.Equal(days, annual.SampleDays);
        for (int c = 0; c < sumSurface.Length; c++)
        {
            Assert.Equal(sumSurface[c] / days.Length, annual.MeanSurfaceK[c]);
            Assert.Equal(minSurface[c], annual.MinSurfaceK[c]);
            Assert.InRange(annual.MeanSurfaceK[c], annual.MinSurfaceK[c], annual.MaxSurfaceK[c]);
            Assert.InRange(annual.MeanAirK[c], annual.MinAirK[c], annual.MaxAirK[c]);
        }
    }

    [Fact]
    public void RepeatedCallsAndDirtyStatesGiveTheSameResult()
    {
        SurfaceTemperatureField field = _fx.CreateField(_fx.IceFreeKinds);
        ThermalAnnualStatistics first = ThermalAnnualStatisticsCalculator.Compute(
            field, new ThermalSnapshot(_fx.Grid.CellCount), _fx.SampleDays);

        // Bepiszkolt állapot: egy másik, KÉSŐBBI nap kiszámolása után ugyanaz
        // az állapotobjektum megy be — a kanonikus útnak ki kell javítania.
        var reused = new ThermalSnapshot(_fx.Grid.CellCount);
        ThermalDailyStatisticsCalculator.Compute(field, reused, 900);
        ThermalAnnualStatistics second = ThermalAnnualStatisticsCalculator.Compute(field, reused, _fx.SampleDays);

        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            Assert.Equal(first.MeanSurfaceK[c], second.MeanSurfaceK[c]);
            Assert.Equal(first.MeanAirK[c], second.MeanAirK[c]);
            Assert.Equal(first.MinSurfaceK[c], second.MinSurfaceK[c]);
            Assert.Equal(first.MaxAirK[c], second.MaxAirK[c]);
        }
        Assert.Equal(field.ModelIdentity, second.ModelIdentity);
    }

    [Fact]
    public void ParallelLocalStepGivesTheSameAnnualStatistics()
    {
        ThermalAnnualStatistics sequential = ThermalAnnualStatisticsCalculator.Compute(
            _fx.CreateField(_fx.IceFreeKinds), new ThermalSnapshot(_fx.Grid.CellCount), _fx.SampleDays);
        SurfaceTemperatureField parallel = _fx.CreateField(_fx.IceFreeKinds);
        parallel.UseParallelLocalStep = true;
        ThermalAnnualStatistics got = ThermalAnnualStatisticsCalculator.Compute(
            parallel, new ThermalSnapshot(_fx.Grid.CellCount), _fx.SampleDays);
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            Assert.Equal(sequential.MeanSurfaceK[c], got.MeanSurfaceK[c]);
            Assert.Equal(sequential.MeanAirK[c], got.MeanAirK[c]);
        }
    }

    [Fact]
    public void EverySampledDayAffectsTheResult()
    {
        ThermalAnnualStatistics four = ThermalAnnualStatisticsCalculator.Compute(
            _fx.CreateField(_fx.IceFreeKinds), new ThermalSnapshot(_fx.Grid.CellCount), 4);
        ThermalAnnualStatistics two = ThermalAnnualStatisticsCalculator.Compute(
            _fx.CreateField(_fx.IceFreeKinds), new ThermalSnapshot(_fx.Grid.CellCount), 2);
        ThermalAnnualStatistics shifted = ThermalAnnualStatisticsCalculator.Compute(
            _fx.CreateField(_fx.IceFreeKinds), new ThermalSnapshot(_fx.Grid.CellCount), 4, 1);
        Assert.NotEqual(four.MeanSurfaceK[0], two.MeanSurfaceK[0]);
        Assert.NotEqual(four.MeanSurfaceK[0], shifted.MeanSurfaceK[0]);
        Assert.Equal(2, two.SampleDays.Count);
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ThermalAnnualStatisticsCalculator.Compute(
            null!, new ThermalSnapshot(_fx.Grid.CellCount)));
        Assert.Throws<ArgumentNullException>(() => ThermalAnnualStatisticsCalculator.Compute(
            _fx.CreateField(_fx.IceFreeKinds), null!));
    }

    internal static void AssertMatches(JsonElement expected, ThermalAnnualStatistics got)
    {
        AssertSeries(expected.GetProperty("meanSurfaceK"), got.MeanSurfaceK);
        AssertSeries(expected.GetProperty("meanAirK"), got.MeanAirK);
        AssertSeries(expected.GetProperty("minSurfaceK"), got.MinSurfaceK);
        AssertSeries(expected.GetProperty("maxSurfaceK"), got.MaxSurfaceK);
        AssertSeries(expected.GetProperty("minAirK"), got.MinAirK);
        AssertSeries(expected.GetProperty("maxAirK"), got.MaxAirK);

        JsonElement days = expected.GetProperty("days");
        Assert.Equal(days.GetArrayLength(), got.SampleDays.Count);
        for (int j = 0; j < got.SampleDays.Count; j++)
            Assert.Equal(days[j].GetInt64(), got.SampleDays[j]);
    }

    private static void AssertSeries(JsonElement expected, System.Collections.Generic.IReadOnlyList<double> got)
    {
        Assert.Equal(expected.GetArrayLength(), got.Count);
        for (int c = 0; c < got.Count; c++)
            Assert.InRange(got[c] - expected[c].GetDouble(), -Tolerance, Tolerance);
    }
}

/// <summary>Kétmenetes jégmaszk és a fogyasztói átkötés (ND-158).</summary>
public class ThermalClimateTests : IClassFixture<ThermalAnnualFixture>
{
    private readonly ThermalAnnualFixture _fx;

    public ThermalClimateTests(ThermalAnnualFixture fixture) => _fx = fixture;

    [Theory]
    [InlineData("absolute")]
    [InlineData("percentile")]
    public void TwoPassClimateMatchesThePythonOracle(string mode)
    {
        bool percentile = mode == "percentile";
        ThermalClimate climate = percentile ? _fx.ComputePercentileClimate() : _fx.ComputeClimate();
        JsonElement expected = _fx.Vectors.GetProperty(mode);

        ThermalAnnualStatisticsTests.AssertMatches(expected.GetProperty("iceFree"), climate.IceFree);
        ThermalAnnualStatisticsTests.AssertMatches(expected.GetProperty("refined"), climate.Refined);

        JsonElement iceFreeClass = expected.GetProperty("iceFreeClass");
        JsonElement refinedClass = expected.GetProperty("refinedClass");
        JsonElement refinedKinds = expected.GetProperty("refinedKinds");
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            Assert.Equal(iceFreeClass[c].GetInt32(), (int)climate.IceFreeClass[c]);
            Assert.Equal(refinedClass[c].GetInt32(), (int)climate.RefinedClass[c]);
            Assert.Equal(refinedKinds[c].GetInt32(), (int)climate.RefinedKinds[c]);
        }
        Assert.Equal(expected.GetProperty("reclassifiedCells").GetInt32(), climate.ReclassifiedCells);
        Assert.Equal(expected.GetProperty("secondPassSkipped").GetBoolean(), climate.SecondPassSkipped);
        Assert.InRange(climate.IceFreeThresholds.PermanentIceMeanK
            - expected.GetProperty("iceFreeThresholdK").GetDouble(), -1e-9, 1e-9);
        Assert.InRange(climate.RefinedThresholds.PermanentIceMeanK
            - expected.GetProperty("refinedThresholdK").GetDouble(), -1e-9, 1e-9);
    }

    [Fact]
    public void ThePercentileThresholdSelectsTheColdestFraction()
    {
        ThermalClimate climate = _fx.ComputePercentileClimate();
        int count = _fx.Grid.CellCount;

        // A vágópont a rendezett minta q-indexű eleme, és a „kisebb mint”
        // összehasonlítás pontosan a nála hidegebb cellákat választja ki.
        var sorted = new double[count];
        for (int c = 0; c < count; c++) sorted[c] = climate.Refined.MeanSurfaceK[c];
        Array.Sort(sorted);
        int idx = (int)(_fx.Percentile * count);
        Assert.Equal(sorted[idx], climate.RefinedThresholds.PermanentIceMeanK);

        int colder = 0;
        for (int c = 0; c < count; c++)
            if (climate.Refined.MeanSurfaceK[c] < climate.RefinedThresholds.PermanentIceMeanK) colder++;
        Assert.Equal(colder, climate.CountRefined(LakesIceErosion.IceClass.PermanentIce));
        Assert.True(colder <= idx, "A vágópontnál hidegebb cellák száma nem haladhatja meg a percentilis-indexet.");

        // A szezonális hó küszöbe ABSZOLÚT marad (ND-159).
        Assert.Equal(LakesIceErosion.SeasonalSnowMinThresholdK, climate.RefinedThresholds.SeasonalSnowMinK);
    }

    [Fact]
    public void ThePercentileAndAbsoluteModesGiveDifferentWorlds()
    {
        ThermalClimate absolute = _fx.ComputeClimate();
        ThermalClimate percentile = _fx.ComputePercentileClimate();

        Assert.NotEqual(absolute.CountRefined(LakesIceErosion.IceClass.PermanentIce),
            percentile.CountRefined(LakesIceErosion.IceClass.PermanentIce));
        Assert.NotEqual(absolute.RefinedThresholds.PermanentIceMeanK,
            percentile.RefinedThresholds.PermanentIceMeanK);

        // A küszöb a B menet felszínítípusait is megváltoztatja, tehát
        // a két mód két különböző hőmezőt ad — nem csak más címkéket.
        bool anyDifferentTemperature = false;
        for (int c = 0; c < _fx.Grid.CellCount; c++)
            if (absolute.Refined.MeanSurfaceK[c] != percentile.Refined.MeanSurfaceK[c])
                anyDifferentTemperature = true;
        Assert.True(anyDifferentTemperature);

        Assert.Equal(LakesIceErosion.PermanentIceMeanThresholdK, absolute.RefinedThresholds.PermanentIceMeanK);
    }

    [Fact]
    public void PercentileThresholdIsPureAndHandlesEdges()
    {
        var values = new double[] { 5.0, 1.0, 4.0, 2.0, 3.0 };
        ThermalIceClassification.IceThresholds a =
            ThermalIceClassification.ComputeThresholds(values, 0.4);
        ThermalIceClassification.IceThresholds b =
            ThermalIceClassification.ComputeThresholds(new double[] { 3.0, 2.0, 1.0, 5.0, 4.0 }, 0.4);
        Assert.Equal(a.PermanentIceMeanK, b.PermanentIceMeanK);   // sorrendfüggetlen
        Assert.Equal(3.0, a.PermanentIceMeanK);                   // idx = (int)(0,4*5) = 2

        // Üres eloszlás: nincs mihez viszonyítani, tehát nincs tartós jég.
        ThermalIceClassification.IceThresholds empty =
            ThermalIceClassification.ComputeThresholds(Array.Empty<double>());
        Assert.Equal(double.NegativeInfinity, empty.PermanentIceMeanK);
        Assert.Equal(LakesIceErosion.IceClass.None,
            ThermalIceClassification.Classify(1.0, 1000.0, empty));

        // Szélső percentilisek.
        Assert.Equal(1.0, ThermalIceClassification.ComputeThresholds(values, 0.0).PermanentIceMeanK);
        Assert.Equal(5.0, ThermalIceClassification.ComputeThresholds(values, 1.0).PermanentIceMeanK);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThermalIceClassification.ComputeThresholds(values, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThermalIceClassification.ComputeThresholds(values, -0.1));
        Assert.Throws<ArgumentNullException>(
            () => ThermalIceClassification.ComputeThresholds(null!));
    }

    [Fact]
    public void TheSecondPassActuallySeesTheIceItCreated()
    {
        ThermalClimate climate = _fx.ComputeClimate();

        // A kétmenetes út csak akkor bizonyít bármit, ha az első menet
        // TÉNYLEG tartós jeget talált (különben a két menet azonos lenne).
        Assert.True(climate.CountRefined(LakesIceErosion.IceClass.PermanentIce) > 0,
            "Az orákulum-világ nem termelt tartós jeget - a kétmenetes út nincs kipróbálva.");

        bool anyIce = false, anyDifference = false;
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            if (climate.RefinedKinds[c] == SurfaceThermalKind.Ice) anyIce = true;
            if (climate.Refined.MeanSurfaceK[c] != climate.IceFree.MeanSurfaceK[c]) anyDifference = true;
        }
        Assert.True(anyIce);
        Assert.True(anyDifference, "A jég-albedó nem hatott a második menet hőmérsékletére.");
    }

    [Fact]
    public void WarmWorldSkipsTheSecondPassWithoutChangingTheResult()
    {
        // Tengerszinti óceánbolygó: az A menet nem talál tartós jeget, ezért a
        // B menet felszíntípusai azonosak lennének - a számítás kihagyható.
        var kinds = new SurfaceThermalKind[_fx.Grid.CellCount];
        var elevation = new double[_fx.Grid.CellCount];
        for (int c = 0; c < kinds.Length; c++) { kinds[c] = SurfaceThermalKind.Ocean; elevation[c] = -3000.0; }

        ThermalClimate climate = ThermalClimateCalculator.Compute(_fx.Grid, kinds, elevation, 0.0,
            ThermalAnnualFixture.Seed, 0.0, _fx.Orbit, sampleDays: _fx.SampleDays,
            permanentIcePercentile: null);

        Assert.Equal(0, climate.CountRefined(LakesIceErosion.IceClass.PermanentIce));
        Assert.True(climate.SecondPassSkipped);
        Assert.Equal(0, climate.ReclassifiedCells);
        for (int c = 0; c < kinds.Length; c++)
        {
            Assert.Equal(climate.IceFree.MeanSurfaceK[c], climate.Refined.MeanSurfaceK[c]);
            Assert.Equal(SurfaceThermalKind.Ocean, climate.RefinedKinds[c]);
        }

        // A jeges orákulum-világon viszont TÉNYLEG lefut a második menet.
        Assert.False(_fx.ComputeClimate().SecondPassSkipped);

        // ÉS: percentilis módban még EZEN a forró világon sem marad el a
        // második menet, mert a küszöb konstrukció szerint talál jeget.
        // Ez az ND-159 vállalt ára — a rövidzár gyakorlatilag kiesik.
        ThermalClimate pct = ThermalClimateCalculator.Compute(_fx.Grid, kinds, elevation, 0.0,
            ThermalAnnualFixture.Seed, 0.0, _fx.Orbit, sampleDays: _fx.SampleDays);
        Assert.False(pct.SecondPassSkipped);
        Assert.True(pct.CountRefined(LakesIceErosion.IceClass.PermanentIce) > 0);
    }

    [Fact]
    public void IceIsAnOutputNotAnInput()
    {
        var withIce = (SurfaceThermalKind[])_fx.IceFreeKinds.Clone();
        withIce[0] = SurfaceThermalKind.Ice;
        Assert.Throws<ArgumentException>(() => ThermalClimateCalculator.Compute(
            _fx.Grid, withIce, _fx.Elevation, 0.0, ThermalAnnualFixture.Seed, 0.0, _fx.Orbit,
            sampleDays: _fx.SampleDays));

        SurfaceThermalKind[] cleaned = ThermalClimateCalculator.IceFreeKinds(withIce);
        Assert.Equal(SurfaceThermalKind.Land, cleaned[0]);
        for (int c = 1; c < cleaned.Length; c++) Assert.Equal(_fx.IceFreeKinds[c], cleaned[c]);
    }

    [Fact]
    public void ClimateIsReproducibleAndSeedSensitive()
    {
        ThermalClimate a = _fx.ComputeClimate();
        ThermalClimate b = _fx.ComputeClimate();
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            Assert.Equal(a.Refined.MeanSurfaceK[c], b.Refined.MeanSurfaceK[c]);
            Assert.Equal(a.RefinedClass[c], b.RefinedClass[c]);
        }

        // A klíma-ciklus (ND-44) a seedből és a geológiai időből jön, ezért a
        // másik seed másik modellazonosítót és másik mezőt ad.
        ThermalClimate other = ThermalClimateCalculator.Compute(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation,
            0.0, ThermalAnnualFixture.Seed + 1, 0.0, _fx.Orbit, sampleDays: _fx.SampleDays);
        Assert.NotEqual(a.Refined.ModelIdentity, other.Refined.ModelIdentity);
    }

    [Fact]
    public void AbsoluteModeStillMatchesTheUnchangedLakesIceThresholds()
    {
        ThermalClimate climate = _fx.ComputeClimate();
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            LakesIceErosion.IceClass expected = LakesIceErosion.ClassifyIce(
                climate.Refined.MeanSurfaceK[c], climate.Refined.MinSurfaceK[c]);
            Assert.Equal(expected, climate.RefinedClass[c]);
        }
    }

    [Fact]
    public void BiomeConsumerReadsTheAnnualAirMean()
    {
        ThermalClimate climate = _fx.ComputeClimate();
        int count = _fx.Grid.CellCount;
        var isOceanic = new bool[count];
        var precipitation = new double[count];
        for (int c = 0; c < count; c++)
        {
            isOceanic[c] = _fx.IceFreeKinds[c] == SurfaceThermalKind.Ocean;
            precipitation[c] = 1.0 + 0.001 * c;
        }
        var thresholds = new BiomeClassification.PrecipitationThresholds(1.01, 1.03, 1.06);

        Biome[] biomes = ThermalClimateCalculator.ClassifyBiomes(climate.Refined, isOceanic, precipitation, thresholds);
        for (int c = 0; c < count; c++)
            Assert.Equal(
                BiomeClassification.Classify(climate.Refined.MeanAirK[c], isOceanic[c], precipitation[c], thresholds),
                biomes[c]);

        Assert.Throws<ArgumentException>(() => ThermalClimateCalculator.ClassifyBiomes(
            climate.Refined, new bool[count - 1], precipitation, thresholds));
    }

    /// <summary>
    /// ND-164: a hideg véget a JÉGOSZTÁLY dönti el, nem a
    /// <see cref="BiomeClassification"/> abszolút küszöbei.
    /// </summary>
    [Fact]
    public void BiomeColdEndComesFromTheIceClass()
    {
        ThermalClimate climate = _fx.ComputePercentileClimate();
        int count = _fx.Grid.CellCount;
        var isOceanic = new bool[count];
        var precipitation = new double[count];
        for (int c = 0; c < count; c++)
        {
            isOceanic[c] = _fx.IceFreeKinds[c] == SurfaceThermalKind.Ocean;
            precipitation[c] = 1.0 + 0.001 * c;
        }
        var thresholds = new BiomeClassification.PrecipitationThresholds(1.01, 1.03, 1.06);

        Biome[] withoutIce = ThermalClimateCalculator.ClassifyBiomes(
            climate.Refined, isOceanic, precipitation, thresholds);
        Biome[] withIce = ThermalClimateCalculator.ClassifyBiomes(
            climate.Refined, isOceanic, precipitation, thresholds, climate.RefinedClass);

        int forced = 0;
        for (int c = 0; c < count; c++)
        {
            if (climate.RefinedClass[c] == LakesIceErosion.IceClass.PermanentIce)
            {
                // A tartós jég MINDIG felülír, hőmérséklettől és csapadéktól függetlenül.
                Assert.Equal(isOceanic[c] ? Biome.SeaIce : Biome.IceSheet, withIce[c]);
                forced++;
            }
            else
            {
                // A többi cella BITRE a jégosztály nélküli eredmény - a meleg vég érintetlen.
                Assert.Equal(withoutIce[c], withIce[c]);
            }
        }

        // A percentilis küszöb KONSTRUKCIÓ SZERINT talál tartós jeget (ND-159),
        // tehát ha ez nulla lenne, a teszt nem bizonyítana semmit.
        Assert.True(forced > 0, "A percentilis küszöbnek tartós jeget kell adnia.");
    }

    /// <summary>Tisztaság és élesetek a jégosztályos túlterhelésre.</summary>
    [Fact]
    public void BiomeIceClassOverloadIsPureAndValidatesInput()
    {
        ThermalClimate climate = _fx.ComputePercentileClimate();
        int count = _fx.Grid.CellCount;
        var isOceanic = new bool[count];
        var precipitation = new double[count];
        for (int c = 0; c < count; c++) precipitation[c] = 0.5;
        var thresholds = new BiomeClassification.PrecipitationThresholds(0.1, 0.2, 0.3);

        Biome[] first = ThermalClimateCalculator.ClassifyBiomes(
            climate.Refined, isOceanic, precipitation, thresholds, climate.RefinedClass);
        Biome[] second = ThermalClimateCalculator.ClassifyBiomes(
            climate.Refined, isOceanic, precipitation, thresholds, climate.RefinedClass);
        Assert.Equal(first, second);

        Assert.Throws<ArgumentNullException>(() => ThermalClimateCalculator.ClassifyBiomes(
            climate.Refined, isOceanic, precipitation, thresholds, null!));
        Assert.Throws<ArgumentException>(() => ThermalClimateCalculator.ClassifyBiomes(
            climate.Refined, isOceanic, precipitation, thresholds,
            new LakesIceErosion.IceClass[count - 1]));
    }

    /// <summary>
    /// ND-160 (LEZÁRVA, (1) opció): a bázis radiatív tagja BOLYGÓ-albedóval
    /// számol, felszíntípus-függetlenül. A FELSZÍNI albedó paraméterezése a
    /// bázist nem mozdítja (az a solver anomália-tagjának mennyisége), és a
    /// LEGACY kampó bitre visszaadja az ND-160 előtti kevert albedót.
    /// </summary>
    [Fact]
    public void BaselineUsesPlanetaryAlbedoAndTheLegacyHookReproducesTheOldMix()
    {
        var defaults = new ThermalModelParameters();
        Assert.Null(defaults.BaselineAlbedo);
        Assert.False(defaults.LegacySurfaceBaselineAlbedo);
        foreach (SurfaceThermalKind kind in new[] { SurfaceThermalKind.Land, SurfaceThermalKind.Ocean,
                                                    SurfaceThermalKind.Ice, SurfaceThermalKind.Freshwater })
            Assert.Equal(Temperature.AlbedoPlanet, defaults.BaselineAlbedoFor(kind));

        // Egyedi FELSZÍNI albedó: a bázis NEM mozdul (a solver anomália-tagja igen).
        var custom = new ThermalModelParameters(landAlbedo: 0.55, oceanAlbedo: 0.44);
        Assert.Equal(Temperature.AlbedoPlanet, custom.BaselineAlbedoFor(SurfaceThermalKind.Land));
        Assert.Equal(Temperature.AlbedoPlanet, custom.BaselineAlbedoFor(SurfaceThermalKind.Ocean));
        Assert.Equal(0.55, custom.Albedo(SurfaceThermalKind.Land));

        // A/B-kampó: pontosan az ND-160 ELŐTTI kevert albedó.
        var legacy = new ThermalModelParameters(legacySurfaceBaselineAlbedo: true);
        Assert.Equal(Temperature.AlbedoOcean, legacy.BaselineAlbedoFor(SurfaceThermalKind.Ocean));
        Assert.Equal(Temperature.AlbedoLand, legacy.BaselineAlbedoFor(SurfaceThermalKind.Land));
        Assert.Equal(Temperature.AlbedoLand, legacy.BaselineAlbedoFor(SurfaceThermalKind.Ice));
        Assert.Equal(Temperature.AlbedoLand, legacy.BaselineAlbedoFor(SurfaceThermalKind.Freshwater));

        // Az explicit érték MINDKETTŐT felülírja, a legacy ágat is.
        var explicitAlbedo = new ThermalModelParameters(baselineAlbedo: 0.42, legacySurfaceBaselineAlbedo: true);
        Assert.Equal(0.42, explicitAlbedo.BaselineAlbedoFor(SurfaceThermalKind.Land));
        Assert.Equal(0.42, explicitAlbedo.BaselineAlbedoFor(SurfaceThermalKind.Ocean));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalModelParameters(baselineAlbedo: 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalModelParameters(baselineAlbedo: -0.1));
    }

    /// <summary>
    /// ND-160 HATÁSA a BÁZISRA, cellatípus szerint szétválasztva. Ez a teszt
    /// mondja ki, mit tesz pontosan a döntés: a szárazföldi bázis BITRE
    /// változatlan (a felszíni szárazföld-albedó eddig is 0,30 volt), az
    /// óceáni bázis viszont ~19–20 K-nel hidegebb — ez a +10,3 K-es globális
    /// többlet forrása. Az explicit bolygó-albedós kampó pedig bitre az
    /// alapértelmezés.
    /// </summary>
    [Fact]
    public void PlanetaryBaselineCoolsTheOceanAndLeavesLandBitIdentical()
    {
        var legacy = new ThermalBaseline(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0.0,
            ThermalAnnualFixture.Seed, 0.0, _fx.Orbit,
            new ThermalModelParameters(legacySurfaceBaselineAlbedo: true));
        var planet = new ThermalBaseline(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0.0,
            ThermalAnnualFixture.Seed, 0.0, _fx.Orbit);
        var explicitPlanet = new ThermalBaseline(_fx.Grid, _fx.IceFreeKinds, _fx.Elevation, 0.0,
            ThermalAnnualFixture.Seed, 0.0, _fx.Orbit,
            new ThermalModelParameters(baselineAlbedo: Temperature.AlbedoPlanet));

        int count = _fx.Grid.CellCount;
        var legacyBase = new double[count];
        var planetBase = new double[count];
        var explicitBase = new double[count];
        var factor = new double[count];
        const long hour = 81;
        legacy.EvaluateHour(hour, factor, legacyBase);
        planet.EvaluateHour(hour, factor, planetBase);
        explicitPlanet.EvaluateHour(hour, factor, explicitBase);

        double minOceanDrop = double.PositiveInfinity, maxOceanDrop = double.NegativeInfinity;
        int oceanCells = 0, landCells = 0;
        for (int c = 0; c < count; c++)
        {
            Assert.Equal(planetBase[c], explicitBase[c]);
            if (_fx.IceFreeKinds[c] == SurfaceThermalKind.Ocean)
            {
                oceanCells++;
                double drop = legacyBase[c] - planetBase[c];
                if (drop < minOceanDrop) minOceanDrop = drop;
                if (drop > maxOceanDrop) maxOceanDrop = drop;
            }
            else
            {
                landCells++;
                Assert.Equal(legacyBase[c], planetBase[c]);
            }
        }

        Assert.True(oceanCells > 0 && landCells > 0, "A próbavilágban legyen óceán és szárazföld is.");
        Assert.True(minOceanDrop > 0.0, $"Az óceáni bázis mindenhol hűl; a legkisebb esés {minOceanDrop:F3} K.");
        Assert.InRange(maxOceanDrop, 13.0, 21.0);
    }
}
