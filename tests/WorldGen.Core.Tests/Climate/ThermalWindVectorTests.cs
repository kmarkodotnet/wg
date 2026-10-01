using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

/// <summary>
/// ND-163: a cellaközéppontbeli szélvektor kivezetése a csatolt szélből, és
/// a nedvesség-transzport szél-bemenete.
///
/// A HANGSÚLY A BITAZONOSSÁGON van: ez a lépés NEM új numerika — a vektort a
/// belső <c>Wind(...)</c> eddig is kiszámolta, csak eldobtuk. Amit bizonyítani
/// kell: (a) a meglévő kimenetek bitre változatlanok, (b) az új vektor és a
/// régi sebesség konzisztens, (c) a régi hívási utak bitre változatlanok.
/// </summary>
public class ThermalWindVectorTests
{
    private const ulong Seed = 184482873278464UL;
    private static readonly ThermalOrbit Orbit = new ThermalOrbit(365.25, 1.0, 23.44 * Math.PI / 180.0);

    private static (DenseGridMetrics grid, ThermalWind wind, double[] theta) Fixture(int level = 2)
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(level);
        var kinds = new SurfaceThermalKind[grid.CellCount];
        var elevation = new double[grid.CellCount];
        for (int c = 0; c < grid.CellCount; c++)
        {
            bool ocean = grid.CenterX[c] < 0.1;
            kinds[c] = ocean ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land;
            elevation[c] = ocean ? -3000.0 : 400.0 + 3000.0 * Math.Abs(grid.CenterZ[c]);
        }
        var wind = new ThermalWind(grid, kinds, elevation, 0.0, Orbit, ThermalModelParameters.Default);
        var theta = new double[grid.CellCount];
        for (int c = 0; c < grid.CellCount; c++)
            theta[c] = 6.0 * grid.CenterX[c] - 4.0 * grid.CenterY[c] + 2.5 * grid.CenterZ[c];
        return (grid, wind, theta);
    }

    [Fact]
    public void TheVectorOverloadLeavesEveryExistingOutputBitIdentical()
    {
        (DenseGridMetrics grid, ThermalWind wind, double[] theta) = Fixture();
        const long seconds = 5 * 86400 + 12345;

        var edgeOld = new double[grid.EdgeCount];
        var speedOld = new double[grid.CellCount];
        wind.SampleCoupled(seconds, theta, edgeOld, speedOld);

        var edgeNew = new double[grid.EdgeCount];
        var speedNew = new double[grid.CellCount];
        var wx = new double[grid.CellCount];
        var wy = new double[grid.CellCount];
        var wz = new double[grid.CellCount];
        wind.SampleCoupled(seconds, theta, edgeNew, speedNew, wx, wy, wz);

        Assert.Equal(edgeOld, edgeNew);     // BITRE
        Assert.Equal(speedOld, speedNew);   // BITRE
    }

    [Fact]
    public void TheVectorLiesInTheTangentPlaneAndItsLengthIsTheSpeed()
    {
        (DenseGridMetrics grid, ThermalWind wind, double[] theta) = Fixture();
        var edge = new double[grid.EdgeCount];
        var speed = new double[grid.CellCount];
        var wx = new double[grid.CellCount];
        var wy = new double[grid.CellCount];
        var wz = new double[grid.CellCount];
        wind.SampleCoupled(3 * 86400, theta, edge, speed, wx, wy, wz);

        for (int c = 0; c < grid.CellCount; c++)
        {
            // Érintősík: a vektor merőleges a cellaközépponti sugárirányra.
            double radial = wx[c] * grid.CenterX[c] + wy[c] * grid.CenterY[c] + wz[c] * grid.CenterZ[c];
            Assert.InRange(radial, -1e-9, 1e-9);

            double length = Math.Sqrt(wx[c] * wx[c] + wy[c] * wy[c] + wz[c] * wz[c]);
            Assert.InRange(length - speed[c], -1e-9, 1e-9);

            // ND-142: a teljes sebesség korlátos marad.
            Assert.InRange(speed[c], 0.0, 40.0);
        }
    }

    [Fact]
    public void TheVectorRespondsToTheAirAnomalyJustLikeTheSpeedDoes()
    {
        (DenseGridMetrics grid, ThermalWind wind, double[] theta) = Fixture();
        var edge = new double[grid.EdgeCount];
        var speed = new double[grid.CellCount];
        var wx = new double[grid.CellCount];
        var wy = new double[grid.CellCount];
        var wz = new double[grid.CellCount];
        wind.SampleCoupled(2 * 86400, theta, edge, speed, wx, wy, wz);

        // Konstans anomália: nincs gradiens, tehát a bázisszelet kapjuk vissza.
        var flat = new double[grid.CellCount];
        Array.Fill(flat, 7.0);
        var edge2 = new double[grid.EdgeCount];
        var speed2 = new double[grid.CellCount];
        var fx = new double[grid.CellCount];
        var fy = new double[grid.CellCount];
        var fz = new double[grid.CellCount];
        wind.SampleCoupled(2 * 86400, flat, edge2, speed2, fx, fy, fz);

        bool anyDifference = false;
        for (int c = 0; c < grid.CellCount; c++)
            if (wx[c] != fx[c] || wy[c] != fy[c] || wz[c] != fz[c]) anyDifference = true;
        Assert.True(anyDifference, "A nem konstans anomáliának meg kell változtatnia a szélvektort.");
    }

    [Fact]
    public void RepeatedCallsAreBitIdenticalAndTheInputIsNotModified()
    {
        (DenseGridMetrics grid, ThermalWind wind, double[] theta) = Fixture();
        var before = (double[])theta.Clone();
        var edge = new double[grid.EdgeCount];
        var speed = new double[grid.CellCount];
        var wx = new double[grid.CellCount];
        var wy = new double[grid.CellCount];
        var wz = new double[grid.CellCount];
        wind.SampleCoupled(86400, theta, edge, speed, wx, wy, wz);

        var wx2 = new double[grid.CellCount];
        var wy2 = new double[grid.CellCount];
        var wz2 = new double[grid.CellCount];
        wind.SampleCoupled(86400, theta, new double[grid.EdgeCount], new double[grid.CellCount], wx2, wy2, wz2);

        Assert.Equal(wx, wx2);
        Assert.Equal(wy, wy2);
        Assert.Equal(wz, wz2);
        Assert.Equal(before, theta);
    }

    [Fact]
    public void RejectsPartialOrMismatchedVectorArrays()
    {
        (DenseGridMetrics grid, ThermalWind wind, double[] theta) = Fixture();
        var edge = new double[grid.EdgeCount];
        var speed = new double[grid.CellCount];
        var ok = new double[grid.CellCount];

        // A három tömböt együtt kell megadni.
        Assert.Throws<ArgumentException>(() => wind.SampleCoupled(0, theta, edge, speed, ok, null, null));
        Assert.Throws<ArgumentException>(() => wind.SampleCoupled(0, theta, edge, speed, null, ok, ok));
        // Méret-eltérés.
        Assert.Throws<ArgumentException>(
            () => wind.SampleCoupled(0, theta, edge, speed, new double[1], ok, ok));
        // A bemenet nem lehet egyben kimenet.
        Assert.Throws<ArgumentException>(() => wind.SampleCoupled(0, theta, edge, speed, theta, ok, ok));
    }
}

/// <summary>Az ÉVES átlagos szél az éghajlat-adatúton belül (ND-163).</summary>
public class ThermalAnnualWindTests
{
    private const ulong Seed = 184482873278464UL;
    private static readonly ThermalOrbit Orbit = new ThermalOrbit(8.0, 1.0, 23.44 * Math.PI / 180.0);

    private static SurfaceTemperatureField Field()
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(1);
        var kinds = new SurfaceThermalKind[grid.CellCount];
        var elevation = new double[grid.CellCount];
        for (int c = 0; c < grid.CellCount; c++)
        {
            bool ocean = grid.CenterX[c] < 0.1;
            kinds[c] = ocean ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land;
            elevation[c] = ocean ? -3000.0 : 500.0 + 2000.0 * Math.Abs(grid.CenterZ[c]);
        }
        return new SurfaceTemperatureField(grid, kinds, elevation, 0.0, Seed, 0.0, Orbit);
    }

    [Fact]
    public void AskingForTheWindLeavesEveryTemperatureOutputBitIdentical()
    {
        SurfaceTemperatureField a = Field();
        ThermalAnnualStatistics without = ThermalAnnualStatisticsCalculator.Compute(
            a, new ThermalSnapshot(a.Grid.CellCount), 4);

        SurfaceTemperatureField b = Field();
        ThermalAnnualStatistics with = ThermalAnnualStatisticsCalculator.Compute(
            b, new ThermalSnapshot(b.Grid.CellCount), 4, 0, includeWind: true);

        Assert.Null(without.MeanWindX);
        Assert.NotNull(with.MeanWindX);
        for (int c = 0; c < a.Grid.CellCount; c++)
        {
            Assert.Equal(without.MeanSurfaceK[c], with.MeanSurfaceK[c]);   // BITRE
            Assert.Equal(without.MeanAirK[c], with.MeanAirK[c]);
            Assert.Equal(without.MinSurfaceK[c], with.MinSurfaceK[c]);
            Assert.Equal(without.MaxAirK[c], with.MaxAirK[c]);
        }
    }

    [Fact]
    public void TheMeanVectorIsNeverLongerThanTheMeanSpeed()
    {
        SurfaceTemperatureField field = Field();
        ThermalAnnualStatistics annual = ThermalAnnualStatisticsCalculator.Compute(
            field, new ThermalSnapshot(field.Grid.CellCount), 4, 0, includeWind: true);

        for (int c = 0; c < field.Grid.CellCount; c++)
        {
            double length = Math.Sqrt(
                annual.MeanWindX![c] * annual.MeanWindX[c]
                + annual.MeanWindY![c] * annual.MeanWindY[c]
                + annual.MeanWindZ![c] * annual.MeanWindZ[c]);
            // Haromszog-egyenlotlenseg: a vektorok atlaganak hossza <= a
            // hosszak atlaga. A kulonbseg a szelirany EVES FORGASA - valodi
            // fizikai tartalom, nem hiba.
            Assert.True(length <= annual.MeanWindSpeedMs![c] + 1e-9,
                $"A(z) {c}. cellán a vektorátlag hossza ({length}) meghaladja az átlagsebességet.");
            Assert.True(annual.MeanWindSpeedMs[c] >= 0.0);
        }
    }

    [Fact]
    public void TheAnnualWindIsReproducible()
    {
        ThermalAnnualStatistics first = ThermalAnnualStatisticsCalculator.Compute(
            Field(), new ThermalSnapshot(Field().Grid.CellCount), 4, 0, includeWind: true);
        ThermalAnnualStatistics second = ThermalAnnualStatisticsCalculator.Compute(
            Field(), new ThermalSnapshot(Field().Grid.CellCount), 4, 0, includeWind: true);
        for (int c = 0; c < first.MeanWindX!.Count; c++)
        {
            Assert.Equal(first.MeanWindX[c], second.MeanWindX![c]);
            Assert.Equal(first.MeanWindSpeedMs![c], second.MeanWindSpeedMs![c]);
        }
    }

    [Fact]
    public void TheDailyWindSumRequiresCaptureAndMatchingArrays()
    {
        SurfaceTemperatureField field = Field();
        int n = field.Grid.CellCount;
        var state = new ThermalSnapshot(n);
        var a = new double[n];

        // CaptureCellWind nélkül nem összegezhető.
        Assert.False(field.CaptureCellWind);
        Assert.Throws<ArgumentException>(
            () => ThermalDailyStatisticsCalculator.Compute(field, state, 0, a, a, a, a));

        field.CaptureCellWind = true;
        Assert.Throws<ArgumentException>(
            () => ThermalDailyStatisticsCalculator.Compute(field, state, 0, a, null, a, a));
        Assert.Throws<ArgumentException>(
            () => ThermalDailyStatisticsCalculator.Compute(field, state, 0, new double[1], a, a, a));

        // Kikapcsolva a hozzáférés explicit hiba, nem csendes null.
        field.CaptureCellWind = false;
        Assert.Throws<InvalidOperationException>(() => _ = field.CellWindX);
    }
}

/// <summary>A nedvesség-transzport szél- és hőmérséklet-bemenete (ND-158/ND-163).</summary>
public class MoisturePrecipitationFieldInputTests
{
    private const ulong Seed = 0xA7C944210000UL;
    private const int Level = 3;

    private static Dictionary<TileId, double> Elevation(out double seaLevel)
    {
        var field = new Dictionary<TileId, double>();
        int n = 1 << Level;
        for (int face = 0; face <= 5; face++)
            for (uint u = 0; u < n; u++)
                for (uint v = 0; v < n; v++)
                {
                    TileId id = TileId.FromFaceLevelUV(face, Level, u, v);
                    TileGeometry.ToPosition(id, out double x, out double y, out double z);
                    field[id] = x < 0.1 ? -2500.0 : 300.0 + 2000.0 * Math.Abs(z);
                }
        seaLevel = 0.0;
        return field;
    }

    [Fact]
    public void NullFieldsReproduceTheOldPathBitIdentically()
    {
        Dictionary<TileId, double> field = Elevation(out double seaLevel);
        MoisturePrecipitation.PrecipitationField legacy =
            MoisturePrecipitation.ComputeFromElevationField(field, seaLevel, Seed, Level);
        MoisturePrecipitation.PrecipitationField explicitNulls =
            MoisturePrecipitation.ComputeFromFields(field, seaLevel, Seed, Level, null, null);

        foreach (KeyValuePair<TileId, double> kv in legacy.Precipitation)
            Assert.Equal(kv.Value, explicitNulls.Precipitation[kv.Key]);
    }

    [Fact]
    public void AnExplicitWindFieldChangesTheResultAndIsActuallyRead()
    {
        Dictionary<TileId, double> field = Elevation(out double seaLevel);
        MoisturePrecipitation.PrecipitationField baseline =
            MoisturePrecipitation.ComputeFromElevationField(field, seaLevel, Seed, Level);

        // Szándékosan MÁS szél: mindenhol tisztán keleti, fix sebességgel.
        var wind = new Dictionary<TileId, SurfaceWindSample>();
        foreach (TileId id in field.Keys)
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            ThermalWind.EastNorth(x, y, z, out double ex, out double ey, out double ez,
                out _, out _, out _);
            const double speed = 8.0;
            wind[id] = new SurfaceWindSample(ex * speed, ey * speed, ez * speed, speed);
        }

        MoisturePrecipitation.PrecipitationField steered =
            MoisturePrecipitation.ComputeFromFields(field, seaLevel, Seed, Level, null, wind);

        bool anyDifference = false;
        foreach (KeyValuePair<TileId, double> kv in baseline.Precipitation)
            if (kv.Value != steered.Precipitation[kv.Key]) anyDifference = true;
        Assert.True(anyDifference, "A megadott szélmezőnek meg kell változtatnia a csapadékot.");
    }

    [Fact]
    public void AMissingWindTileIsAnExplicitErrorNotASilentFallback()
    {
        Dictionary<TileId, double> field = Elevation(out double seaLevel);
        var wind = new Dictionary<TileId, SurfaceWindSample>();
        bool first = true;
        foreach (TileId id in field.Keys)
        {
            if (first) { first = false; continue; }  // egyetlen tile hiányzik
            wind[id] = new SurfaceWindSample(0.0, 0.0, 0.0, 1.0);
        }

        Assert.Throws<ArgumentException>(
            () => MoisturePrecipitation.ComputeFromFields(field, seaLevel, Seed, Level, null, wind));
    }

    [Fact]
    public void TemperatureAndWindCanBeSuppliedTogether()
    {
        Dictionary<TileId, double> field = Elevation(out double seaLevel);
        var temperature = new Dictionary<TileId, double>();
        var wind = new Dictionary<TileId, SurfaceWindSample>();
        foreach (TileId id in field.Keys)
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            ThermalWind.EastNorth(x, y, z, out double ex, out double ey, out double ez, out _, out _, out _);
            temperature[id] = 290.0 - 30.0 * Math.Abs(z);
            wind[id] = new SurfaceWindSample(ex * 5.0, ey * 5.0, ez * 5.0, 5.0);
        }

        MoisturePrecipitation.PrecipitationField both =
            MoisturePrecipitation.ComputeFromFields(field, seaLevel, Seed, Level, temperature, wind);
        MoisturePrecipitation.PrecipitationField windOnly =
            MoisturePrecipitation.ComputeFromFields(field, seaLevel, Seed, Level, null, wind);

        bool anyDifference = false;
        foreach (KeyValuePair<TileId, double> kv in windOnly.Precipitation)
            if (kv.Value != both.Precipitation[kv.Key]) anyDifference = true;
        Assert.True(anyDifference, "A hőmérséklet-mezőnek a szél mellett is hatnia kell (párolgás).");
    }

    [Fact]
    public void ClimateFieldConveniencePathUsesTheSamePrecipitationInputs()
    {
        const int plateCount = 20;
        Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(Seed, plateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values,
            MoisturePrecipitation.DefaultTargetWaterFraction);
        var temperature = new Dictionary<TileId, double>();
        var wind = new Dictionary<TileId, SurfaceWindSample>();
        foreach (TileId id in field.Keys)
        {
            temperature[id] = 284.0;
            wind[id] = new SurfaceWindSample(0.0, 0.0, 0.0, 4.0);
        }

        MoisturePrecipitation.PrecipitationField direct =
            MoisturePrecipitation.ComputeFromFields(field, seaLevel, Seed, Level, temperature, wind);
        MoisturePrecipitation.PrecipitationField wrapped =
            MoisturePrecipitation.ComputeWithClimateFields(Seed, plateCount, Level, temperature, wind);
        foreach (KeyValuePair<TileId, double> kv in direct.Precipitation)
            Assert.Equal(kv.Value, wrapped.Precipitation[kv.Key]);
    }
}
