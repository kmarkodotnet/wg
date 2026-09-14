using System;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Astronomy;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Climate;

/// <summary>
/// Közös, egyszer felépített level-6 hőmező a Python-referencia
/// (tools/reference/thermal_field_ref.py) szintetikus teszt-világán.
/// </summary>
public sealed class ThermalFieldFixture
{
    public const ulong Seed = 184482873278464UL;
    public static readonly ThermalOrbit Orbit = new ThermalOrbit(365.25, 1.0, 23.44 * Math.PI / 180.0);

    public JsonElement Vectors { get; }
    public DenseGridMetrics Grid { get; }
    public SurfaceThermalKind[] Kinds { get; }
    public double[] Elevation { get; }

    public ThermalFieldFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "thermal_field_vectors.json");
        Vectors = JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        Grid = DenseGridMetrics.Build(6);
        (Kinds, Elevation) = SyntheticWorld(Grid);
    }

    public SurfaceTemperatureField CreateField(ThermalModelParameters? parameters = null)
        => new SurfaceTemperatureField(Grid, Kinds, Elevation, 0.0, Seed, 0.0, Orbit, parameters);

    /// <summary>A Python-referencia <c>synthetic_world</c> szabálya.</summary>
    public static (SurfaceThermalKind[] kinds, double[] elevation) SyntheticWorld(DenseGridMetrics grid)
    {
        var kinds = new SurfaceThermalKind[grid.CellCount];
        var elevation = new double[grid.CellCount];
        for (int k = 0; k < grid.CellCount; k++)
        {
            double x = grid.CenterX[k], y = grid.CenterY[k], z = grid.CenterZ[k];
            double s = 0.8 * x + 0.3 * y + 0.2 * z;
            if (z > 0.92 || z < -0.92) { kinds[k] = SurfaceThermalKind.Ice; elevation[k] = 300.0; }
            else if (s < 0.05) { kinds[k] = SurfaceThermalKind.Ocean; elevation[k] = -3000.0; }
            else if (x > 0.5 && y > 0.3 && s < 0.6) { kinds[k] = SurfaceThermalKind.Freshwater; elevation[k] = 100.0; }
            else { kinds[k] = SurfaceThermalKind.Land; elevation[k] = 150.0 + 2500.0 * (s - 0.05); }
        }
        return (kinds, elevation);
    }
}

public class SurfaceTemperatureFieldReferenceTests : IClassFixture<ThermalFieldFixture>
{
    private readonly ThermalFieldFixture _fx;

    public SurfaceTemperatureFieldReferenceTests(ThermalFieldFixture fx) => _fx = fx;

    private static void Near(double expected, double actual, double tolerance, string what)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"{what}: várt {expected:R}, kapott {actual:R}, eltérés {Math.Abs(expected - actual):E3} > {tolerance:E1}");
    }

    private static void NearRelative(double expected, double actual, double relTolerance, string what)
    {
        double scale = Math.Max(Math.Abs(expected), 1e-300);
        Assert.True(Math.Abs(expected - actual) / scale <= relTolerance,
            $"{what}: várt {expected:R}, kapott {actual:R}, relatív eltérés {Math.Abs(expected - actual) / scale:E3} > {relTolerance:E1}");
    }

    [Fact]
    public void MetricsMatchReference()
    {
        JsonElement m = _fx.Vectors.GetProperty("metrics");
        Assert.Equal(m.GetProperty("edgeCount").GetInt32(), _fx.Grid.EdgeCount);
        double total = 0.0;
        for (int c = 0; c < _fx.Grid.CellCount; c++) total += _fx.Grid.Area[c];
        NearRelative(m.GetProperty("totalArea").GetDouble(), total, 1e-12, "összterület");

        foreach (JsonElement cell in m.GetProperty("cells").EnumerateArray())
        {
            int c = cell.GetProperty("index").GetInt32();
            Near(cell.GetProperty("x").GetDouble(), _fx.Grid.CenterX[c], 1e-14, $"CenterX[{c}]");
            Near(cell.GetProperty("y").GetDouble(), _fx.Grid.CenterY[c], 1e-14, $"CenterY[{c}]");
            Near(cell.GetProperty("z").GetDouble(), _fx.Grid.CenterZ[c], 1e-14, $"CenterZ[{c}]");
            NearRelative(cell.GetProperty("area").GetDouble(), _fx.Grid.Area[c], 1e-12, $"Area[{c}]");
        }
        foreach (JsonElement edge in m.GetProperty("edges").EnumerateArray())
        {
            int e = edge.GetProperty("e").GetInt32();
            Assert.Equal(edge.GetProperty("i").GetInt32(), _fx.Grid.EdgeI[e]);
            Assert.Equal(edge.GetProperty("j").GetInt32(), _fx.Grid.EdgeJ[e]);
            NearRelative(edge.GetProperty("length").GetDouble(), _fx.Grid.EdgeLength[e], 1e-12, $"EdgeLength[{e}]");
            Near(edge.GetProperty("midX").GetDouble(), _fx.Grid.EdgeMidX[e], 1e-13, $"EdgeMidX[{e}]");
            Near(edge.GetProperty("midY").GetDouble(), _fx.Grid.EdgeMidY[e], 1e-13, $"EdgeMidY[{e}]");
            Near(edge.GetProperty("midZ").GetDouble(), _fx.Grid.EdgeMidZ[e], 1e-13, $"EdgeMidZ[{e}]");
            Near(edge.GetProperty("normalX").GetDouble(), _fx.Grid.EdgeNormalX[e], 1e-12, $"EdgeNormalX[{e}]");
            Near(edge.GetProperty("normalY").GetDouble(), _fx.Grid.EdgeNormalY[e], 1e-12, $"EdgeNormalY[{e}]");
            Near(edge.GetProperty("normalZ").GetDouble(), _fx.Grid.EdgeNormalZ[e], 1e-12, $"EdgeNormalZ[{e}]");
        }
    }

    [Fact]
    public void KindCountsMatchReference()
    {
        int[] counts = new int[4];
        foreach (SurfaceThermalKind k in _fx.Kinds) counts[(int)k]++;
        int i = 0;
        foreach (JsonElement expected in _fx.Vectors.GetProperty("kindCounts").EnumerateArray())
            Assert.Equal(expected.GetInt32(), counts[i++]);
    }

    [Fact]
    public void BaselineMatchesReference()
    {
        JsonElement b = _fx.Vectors.GetProperty("baseline");
        SurfaceTemperatureField field = _fx.CreateField();
        Near(b.GetProperty("cycleK").GetDouble(), field.Baseline.CycleK, 1e-12, "cycleK");
        Near(b.GetProperty("greenhouseK").GetDouble(), field.Baseline.GreenhouseK, 1e-12, "greenhouseK");

        var factor = new double[_fx.Grid.CellCount];
        var baseK = new double[_fx.Grid.CellCount];
        field.Baseline.EvaluateHour(b.GetProperty("hour").GetInt64(), factor, baseK);

        double min = double.MaxValue, max = double.MinValue;
        foreach (double v in baseK) { min = Math.Min(min, v); max = Math.Max(max, v); }
        Near(b.GetProperty("minBaseK").GetDouble(), min, 1e-9, "min bázis");
        Near(b.GetProperty("maxBaseK").GetDouble(), max, 1e-9, "max bázis");

        foreach (JsonElement cell in b.GetProperty("cells").EnumerateArray())
        {
            int c = cell.GetProperty("index").GetInt32();
            Assert.Equal(cell.GetProperty("kind").GetInt32(), (int)_fx.Kinds[c]);
            Near(cell.GetProperty("elevationM").GetDouble(), _fx.Elevation[c], 1e-9, $"elevation[{c}]");
            Near(cell.GetProperty("annualFactor").GetDouble(), field.Baseline.AnnualFactor[c], 1e-12, $"annualFactor[{c}]");
            Near(cell.GetProperty("annualMean").GetDouble(), field.Baseline.AnnualMeanRadiativeK[c], 1e-9, $"annualMean[{c}]");
            Near(cell.GetProperty("factor").GetDouble(), factor[c], 1e-12, $"factor[{c}]");
            Near(cell.GetProperty("baseK").GetDouble(), baseK[c], 1e-9, $"baseK[{c}]");
        }
    }

    [Fact]
    public void WindMatchesReference()
    {
        JsonElement w = _fx.Vectors.GetProperty("wind");
        SurfaceTemperatureField field = _fx.CreateField();
        var edgeU = new double[_fx.Grid.EdgeCount];
        var speed = new double[_fx.Grid.CellCount];
        field.Wind.EvaluateDay(w.GetProperty("day").GetInt64(), edgeU, speed);

        foreach (JsonElement edge in w.GetProperty("edges").EnumerateArray())
        {
            int e = edge.GetProperty("e").GetInt32();
            Near(edge.GetProperty("u").GetDouble(), edgeU[e], 1e-9, $"u[{e}]");
        }
        foreach (JsonElement cell in w.GetProperty("cells").EnumerateArray())
        {
            int c = cell.GetProperty("index").GetInt32();
            Near(cell.GetProperty("speed").GetDouble(), speed[c], 1e-9, $"speed[{c}]");
        }
        double maxU = 0.0, maxSpeed = 0.0;
        foreach (double u in edgeU) maxU = Math.Max(maxU, Math.Abs(u));
        foreach (double s in speed) maxSpeed = Math.Max(maxSpeed, s);
        Near(w.GetProperty("maxAbsEdgeU").GetDouble(), maxU, 1e-9, "max |u|");
        Near(w.GetProperty("maxSpeed").GetDouble(), maxSpeed, 1e-9, "max szélsebesség");
    }

    [Fact]
    public void ShortRunMatchesReference()
    {
        JsonElement r = _fx.Vectors.GetProperty("shortRun");
        SurfaceTemperatureField field = _fx.CreateField();
        var state = new ThermalSnapshot(_fx.Grid.CellCount) { };
        state.CopyFrom(new ThermalSnapshot(_fx.Grid.CellCount));
        SetTick(state, r.GetProperty("startTick").GetInt64());

        int maxSubsteps = 0;
        int ticks = r.GetProperty("ticks").GetInt32();
        for (int n = 0; n < ticks; n++)
        {
            field.Step(state);
            maxSubsteps = Math.Max(maxSubsteps, field.LastSubsteps);
        }
        Assert.Equal(r.GetProperty("maxSubsteps").GetInt32(), maxSubsteps);
        AssertState(r, state, 1e-9, 1e-9);
    }

    [Fact]
    public void CanonicalStateMatchesReference()
    {
        if (!_fx.Vectors.TryGetProperty("canonical", out JsonElement r))
            throw new InvalidOperationException("A tesztvektorokból hiányzik a kanonikus futás (thermal_field_ref.py --no-canonical nélkül generálandó).");

        SurfaceTemperatureField field = _fx.CreateField();
        var state = new ThermalSnapshot(_fx.Grid.CellCount);
        long target = r.GetProperty("targetTick").GetInt64();
        Assert.Equal(r.GetProperty("startTick").GetInt64(), SimulationTime.CanonicalStartTick(target));
        field.StateAt(state, target);
        Assert.Equal(target, state.Tick);
        AssertState(r, state, 1e-7, 1e-8);

        var baseK = new double[_fx.Grid.CellCount];
        field.BaselineAt(target, baseK);
        double minSurface = double.MaxValue, maxSurface = double.MinValue;
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            double s = baseK[c] + state.ThetaS[c];
            Assert.False(double.IsNaN(s) || double.IsInfinity(s), $"nem véges felszíni hőmérséklet a(z) {c}. cellán");
            minSurface = Math.Min(minSurface, s);
            maxSurface = Math.Max(maxSurface, s);
        }
        Near(r.GetProperty("minSurfaceK").GetDouble(), minSurface, 1e-7, "min felszíni hőmérséklet");
        Near(r.GetProperty("maxSurfaceK").GetDouble(), maxSurface, 1e-7, "max felszíni hőmérséklet");
        foreach (JsonElement cell in r.GetProperty("cells").EnumerateArray())
        {
            int c = cell.GetProperty("index").GetInt32();
            Near(cell.GetProperty("surfaceK").GetDouble(), baseK[c] + state.ThetaS[c], 1e-7, $"surfaceK[{c}]");
            Near(cell.GetProperty("airK").GetDouble(), baseK[c] + state.ThetaA[c], 1e-7, $"airK[{c}]");
        }
    }

    private void AssertState(JsonElement r, ThermalSnapshot state, double absTolerance, double relSumTolerance)
    {
        double sumS = 0.0, sumA = 0.0;
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            sumS += _fx.Grid.Area[c] * state.ThetaS[c];
            sumA += _fx.Grid.Area[c] * state.ThetaA[c];
        }
        double totalArea = 4.0 * Math.PI * PlanetConstants.RadiusMeters * PlanetConstants.RadiusMeters;
        Near(r.GetProperty("sumAreaThetaS").GetDouble(), sumS, relSumTolerance * totalArea, "Σ A·θs");
        Near(r.GetProperty("sumAreaThetaA").GetDouble(), sumA, relSumTolerance * totalArea, "Σ A·θa");
        foreach (JsonElement cell in r.GetProperty("cells").EnumerateArray())
        {
            int c = cell.GetProperty("index").GetInt32();
            Near(cell.GetProperty("thetaS").GetDouble(), state.ThetaS[c], absTolerance, $"θs[{c}]");
            Near(cell.GetProperty("thetaA").GetDouble(), state.ThetaA[c], absTolerance, $"θa[{c}]");
        }
    }

    internal static void SetTick(ThermalSnapshot state, long tick) => state.Reset(tick);
}

public class SurfaceTemperatureFieldBehaviourTests : IClassFixture<ThermalFieldFixture>
{
    private readonly ThermalFieldFixture _fx;

    public SurfaceTemperatureFieldBehaviourTests(ThermalFieldFixture fx) => _fx = fx;

    private ThermalSnapshot Run(SurfaceTemperatureField field, long startTick, int ticks)
    {
        var state = new ThermalSnapshot(_fx.Grid.CellCount);
        SurfaceTemperatureFieldReferenceTests.SetTick(state, startTick);
        for (int n = 0; n < ticks; n++) field.Step(state);
        return state;
    }

    private static void AssertBitEqual(ThermalSnapshot a, ThermalSnapshot b)
    {
        Assert.Equal(a.Tick, b.Tick);
        for (int c = 0; c < a.ThetaS.Length; c++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.ThetaS[c]), BitConverter.DoubleToInt64Bits(b.ThetaS[c]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.ThetaA[c]), BitConverter.DoubleToInt64Bits(b.ThetaA[c]));
        }
    }

    [Fact]
    public void GridInvariantsHold()
    {
        DenseGridMetrics g = _fx.Grid;
        Assert.Equal(24576, g.CellCount);
        Assert.Equal(2 * g.CellCount, g.EdgeCount);
        var degree = new int[g.CellCount];
        double total = 0.0;
        for (int c = 0; c < g.CellCount; c++)
        {
            Assert.True(g.Area[c] > 0.0);
            total += g.Area[c];
        }
        for (int e = 0; e < g.EdgeCount; e++)
        {
            Assert.True(g.EdgeI[e] < g.EdgeJ[e]);
            degree[g.EdgeI[e]]++;
            degree[g.EdgeJ[e]]++;
            double n = Math.Sqrt(g.EdgeNormalX[e] * g.EdgeNormalX[e] + g.EdgeNormalY[e] * g.EdgeNormalY[e] + g.EdgeNormalZ[e] * g.EdgeNormalZ[e]);
            Assert.InRange(n, 1.0 - 1e-12, 1.0 + 1e-12);
            Assert.True(g.EdgeLength[e] > 0.0);
        }
        Assert.All(degree, d => Assert.Equal(4, d));
        double expected = 4.0 * Math.PI * PlanetConstants.RadiusMeters * PlanetConstants.RadiusMeters;
        Assert.InRange(total / expected, 1.0 - 1e-12, 1.0 + 1e-12);
    }

    [Fact]
    public void RepeatedRunsAreBitIdentical()
    {
        ThermalSnapshot a = Run(_fx.CreateField(), 5000, 6);
        ThermalSnapshot b = Run(_fx.CreateField(), 5000, 6);
        AssertBitEqual(a, b);
    }

    [Fact]
    public void ParallelLocalStepMatchesSequential()
    {
        SurfaceTemperatureField sequential = _fx.CreateField();
        SurfaceTemperatureField parallel = _fx.CreateField();
        parallel.UseParallelLocalStep = true;
        AssertBitEqual(Run(sequential, 7000, 6), Run(parallel, 7000, 6));
    }

    [Fact]
    public void ContinuingWithinBucketMatchesFreshCanonicalState()
    {
        SurfaceTemperatureField field = _fx.CreateField();
        var continued = new ThermalSnapshot(_fx.Grid.CellCount);
        field.StateAt(continued, 40);
        field.StateAt(continued, 70);

        var fresh = new ThermalSnapshot(_fx.Grid.CellCount);
        _fx.CreateField().StateAt(fresh, 70);
        AssertBitEqual(continued, fresh);
    }

    [Fact]
    public void CanonicalContinuationRequiresSameStartTick()
    {
        long bucket = SimulationTime.BucketTicks;
        SurfaceTemperatureField field = _fx.CreateField();

        var previousBucket = new ThermalSnapshot(_fx.Grid.CellCount);
        field.ResetCanonical(previousBucket, bucket - 1);
        Assert.Equal(-SimulationTime.SpinUpTicks, previousBucket.Tick);
        Assert.True(SurfaceTemperatureField.CanContinue(previousBucket, bucket - 1));
        Assert.False(SurfaceTemperatureField.CanContinue(previousBucket, bucket + 1));

        var nextBucket = new ThermalSnapshot(_fx.Grid.CellCount);
        field.ResetCanonical(nextBucket, bucket + 1);
        Assert.Equal(bucket - SimulationTime.SpinUpTicks, nextBucket.Tick);
        Assert.True(SurfaceTemperatureField.CanContinue(nextBucket, bucket + 1));
        Assert.False(SurfaceTemperatureField.CanContinue(nextBucket, bucket - SimulationTime.SpinUpTicks - 1));

        // Vizsgálati nullázás nem kanonikus: sosem folytatható.
        var probe = new ThermalSnapshot(_fx.Grid.CellCount);
        probe.Reset(0);
        Assert.False(SurfaceTemperatureField.CanContinue(probe, 10));
        Assert.False(SurfaceTemperatureField.CanContinue(null, 10));

        // Másolat megtartja a kanonikus kezdőticket.
        var copy = new ThermalSnapshot(_fx.Grid.CellCount);
        copy.CopyFrom(nextBucket);
        Assert.True(SurfaceTemperatureField.CanContinue(copy, bucket + 1));
    }

    [Fact]
    public void AdvectionPreservesConstantFieldWithDivergentWind()
    {
        SurfaceTemperatureField field = _fx.CreateField();
        var edgeU = new double[_fx.Grid.EdgeCount];
        var speed = new double[_fx.Grid.CellCount];
        field.Wind.EvaluateDay(3, edgeU, speed);

        int n = _fx.Grid.CellCount;
        var theta = new double[n];
        Array.Fill(theta, 1.0);
        int sub = SurfaceTemperatureField.AdvectCompensatedUpwind(_fx.Grid, edgeU, theta, SimulationTime.TickSeconds,
            new double[n], new double[n], new double[n], new double[n]);
        Assert.True(sub >= 1);
        foreach (double t in theta)
            Assert.InRange(t, 1.0 - 1e-12, 1.0 + 1e-12);
    }

    [Fact]
    public void AdvectionConservesEnergyForDivergenceFreeWind()
    {
        DenseGridMetrics g = _fx.Grid;
        var edgeU = new double[g.EdgeCount];
        const double speed = 20.0;
        for (int e = 0; e < g.EdgeCount; e++)
        {
            // merevtest-forgás a z tengely körül az élközéppontban: v = U·(−y, x, 0)
            edgeU[e] = speed * (-g.EdgeMidY[e] * g.EdgeNormalX[e] + g.EdgeMidX[e] * g.EdgeNormalY[e]);
        }
        int n = g.CellCount;
        var theta = new double[n];
        for (int c = 0; c < n; c++)
        {
            double dx = g.CenterX[c] - 1.0, dy = g.CenterY[c], dz = g.CenterZ[c];
            double d2 = (dx * dx + dy * dy + dz * dz) / (0.35 * 0.35);
            theta[c] = d2 < 1.0 ? (1.0 - d2) * (1.0 - d2) : 0.0;
        }
        double before = 0.0;
        for (int c = 0; c < n; c++) before += g.Area[c] * theta[c];
        var inflow = new double[n]; var div = new double[n]; var flux = new double[n]; var scratch = new double[n];
        for (int step = 0; step < 40; step++)
            SurfaceTemperatureField.AdvectCompensatedUpwind(g, edgeU, theta, SimulationTime.TickSeconds, inflow, div, flux, scratch);
        double after = 0.0, min = double.MaxValue;
        for (int c = 0; c < n; c++) { after += g.Area[c] * theta[c]; min = Math.Min(min, theta[c]); }
        Assert.InRange(after / before, 1.0 - 1e-12, 1.0 + 1e-12);
        Assert.True(min >= -1e-15, "az upwind séma nem adhat negatív értéket nemnegatív kezdőmezőből");
    }

    [Fact]
    public void ZonalBandMatchesLegacyAsinFormula()
    {
        for (int i = -1000; i <= 1000; i++)
        {
            double z = i / 1000.0;
            double legacy = WindPrecipitation.ZonalBandIndex(Math.Asin(z));
            Assert.InRange(ThermalWind.ZonalBandIndex(z) - legacy, -1e-12, 1e-12);
        }
    }

    [Fact]
    public void EastNorthMatchesLegacyBasis()
    {
        foreach (double[] p in new[] { new[] { 1.0, 0.0, 0.0 }, new[] { 0.6, 0.64, 0.48 }, new[] { -0.3, 0.1, -0.948683 } })
        {
            double len = Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
            double x = p[0] / len, y = p[1] / len, z = p[2] / len;
            ThermalWind.EastNorth(x, y, z, out double ex, out double ey, out double ez, out double nx, out double ny, out double nz);
            WindPrecipitation.LocalEastNorth(x, y, z, out double lex, out double ley, out double lez, out double lnx, out double lny, out double lnz);
            Assert.InRange(ex - lex, -1e-12, 1e-12); Assert.InRange(ey - ley, -1e-12, 1e-12); Assert.InRange(ez - lez, -1e-12, 1e-12);
            Assert.InRange(nx - lnx, -1e-12, 1e-12); Assert.InRange(ny - lny, -1e-12, 1e-12); Assert.InRange(nz - lnz, -1e-12, 1e-12);
        }
    }

    [Fact]
    public void EveryParameterChangesTheResult()
    {
        ThermalSnapshot reference = Run(_fx.CreateField(), 330, 4);
        var variants = new (string name, ThermalModelParameters p)[]
        {
            ("landAlbedo", new ThermalModelParameters(landAlbedo: 0.25)),
            ("oceanAlbedo", new ThermalModelParameters(oceanAlbedo: 0.08)),
            ("freshwaterAlbedo", new ThermalModelParameters(freshwaterAlbedo: 0.1)),
            ("iceAlbedo", new ThermalModelParameters(iceAlbedo: 0.7)),
            ("waterEmissivity", new ThermalModelParameters(waterEmissivity: 0.99)),
            ("landEmissivity", new ThermalModelParameters(landEmissivity: 0.9)),
            ("oceanDepthM", new ThermalModelParameters(oceanDepthM: 20.0)),
            ("landDepthM", new ThermalModelParameters(landDepthM: 0.2)),
            ("airColumnM", new ThermalModelParameters(airColumnM: 800.0)),
            ("transferCoefficient", new ThermalModelParameters(transferCoefficient: 1.0e-3)),
            ("airRelaxationDays", new ThermalModelParameters(airRelaxationDays: 2.0)),
            ("minExchangeWindMs", new ThermalModelParameters(minExchangeWindMs: 30.0)),
            ("radiativeSmoothing", new ThermalModelParameters(radiativeSmoothing: 0.3)),
        };
        foreach (var (name, p) in variants)
        {
            ThermalSnapshot got = Run(_fx.CreateField(p), 330, 4);
            bool differs = false;
            for (int c = 0; c < got.ThetaS.Length && !differs; c++)
                differs = got.ThetaS[c] != reference.ThetaS[c] || got.ThetaA[c] != reference.ThetaA[c];
            Assert.True(differs, $"a(z) {name} paraméter nem változtatta meg az eredményt");
        }
    }

    [Fact]
    public void DiurnalCyclePeaksAfterNoonAndOceanVariesLessThanLand()
    {
        SurfaceTemperatureField field = _fx.CreateField();
        var state = new ThermalSnapshot(_fx.Grid.CellCount);
        long start = 20 * SimulationTime.TicksPerDay;
        field.StateAt(state, start);

        int land = FindCell(SurfaceThermalKind.Land, 0.0);
        int ocean = FindCell(SurfaceThermalKind.Ocean, 0.0);
        double landMin = double.MaxValue, landMax = double.MinValue, oceanMin = double.MaxValue, oceanMax = double.MinValue;
        long landMaxTick = 0, noonTick = 0;
        double bestCos = -1.0;
        for (int n = 0; n < SimulationTime.TicksPerDay; n++)
        {
            field.Step(state);
            double ts = state.ThetaS[land];
            if (ts > landMax) { landMax = ts; landMaxTick = state.Tick; }
            landMin = Math.Min(landMin, ts);
            oceanMin = Math.Min(oceanMin, state.ThetaS[ocean]);
            oceanMax = Math.Max(oceanMax, state.ThetaS[ocean]);

            OrbitalMechanics.SunDirectionBodyFrame(state.Tick * SimulationTime.TickSeconds / 86400.0,
                365.25, 1.0, ThermalFieldFixture.Orbit.AxialTiltRad, 0.0, 0.0, out double sx, out double sy, out double sz);
            double cos = _fx.Grid.CenterX[land] * sx + _fx.Grid.CenterY[land] * sy + _fx.Grid.CenterZ[land] * sz;
            if (cos > bestCos) { bestCos = cos; noonTick = state.Tick; }
        }
        long lagTicks = ((landMaxTick - noonTick) % SimulationTime.TicksPerDay + SimulationTime.TicksPerDay) % SimulationTime.TicksPerDay;
        Assert.InRange(lagTicks, 1, 6 * 4);
        Assert.True(oceanMax - oceanMin < landMax - landMin,
            $"óceáni napi ingás {oceanMax - oceanMin:F3} K, szárazföldi {landMax - landMin:F3} K");
    }

    private int FindCell(SurfaceThermalKind kind, double targetZ)
    {
        int best = -1;
        double bestScore = double.MaxValue;
        for (int c = 0; c < _fx.Grid.CellCount; c++)
        {
            if (_fx.Kinds[c] != kind) continue;
            double score = Math.Abs(_fx.Grid.CenterZ[c] - targetZ);
            if (score < bestScore) { bestScore = score; best = c; }
        }
        Assert.True(best >= 0);
        return best;
    }

    [Fact]
    public void PolarNightStaysPhysicallyBounded()
    {
        SurfaceTemperatureField field = _fx.CreateField();
        var baseK = new double[_fx.Grid.CellCount];
        // napforduló környéke (pályafázis 0-tól negyed év) - az egyik pólus éjszakában
        long tick = (long)(91.3 * SimulationTime.TicksPerDay);
        field.BaselineAt(tick, baseK);
        foreach (double b in baseK)
            Assert.InRange(b, 150.0, 360.0);
    }

    [Fact]
    public void TimeHelpersHandleNegativeAndBoundaryValues()
    {
        Assert.Equal(-1L, SimulationTime.FloorDiv(-1, 4));
        Assert.Equal(-1L, SimulationTime.FloorDiv(-4, 4));
        Assert.Equal(-2L, SimulationTime.FloorDiv(-5, 4));
        Assert.Equal(0L, SimulationTime.FloorDiv(3, 4));
        Assert.Equal(-SimulationTime.SpinUpTicks, SimulationTime.CanonicalStartTick(0));
        Assert.Equal(SimulationTime.BucketTicks - SimulationTime.SpinUpTicks, SimulationTime.CanonicalStartTick(SimulationTime.BucketTicks));
        Assert.Equal(-SimulationTime.BucketTicks - SimulationTime.SpinUpTicks, SimulationTime.CanonicalStartTick(-1));
        Assert.Equal(96L, SimulationTime.FromDaysFloor(1.0).Tick);
        Assert.Equal(-1L, SimulationTime.FromDaysFloor(-1e-9).Tick);
        Assert.Throws<ArgumentOutOfRangeException>(() => SimulationTime.FromDaysFloor(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => SimulationTime.FromDaysFloor(double.PositiveInfinity));
    }

    [Fact]
    public void InvalidInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalModelParameters(oceanDepthM: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalModelParameters(iceAlbedo: 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalModelParameters(radiativeSmoothing: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalOrbit(0.0, 1.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DenseGridMetrics.Build(0));
        Assert.Throws<ArgumentException>(() =>
            new SurfaceTemperatureField(_fx.Grid, new SurfaceThermalKind[10], _fx.Elevation, 0.0, 1, 0.0, ThermalFieldFixture.Orbit));
        var badElevation = (double[])_fx.Elevation.Clone();
        badElevation[5] = double.NaN;
        Assert.Throws<ArgumentException>(() =>
            new SurfaceTemperatureField(_fx.Grid, _fx.Kinds, badElevation, 0.0, 1, 0.0, ThermalFieldFixture.Orbit));
    }

    [Fact]
    public void AltitudeCorrectionAppliesOnlyAboveSeaLevel()
    {
        Assert.Equal(280.0, SurfaceTemperatureField.AltitudeCorrectedK(280.0, -100.0, -500.0, 0.0));
        Assert.Equal(280.0 - 0.0065 * 1000.0, SurfaceTemperatureField.AltitudeCorrectedK(280.0, 500.0, 1500.0, 0.0), 12);
        Assert.Equal(280.0 + 0.0065 * 500.0, SurfaceTemperatureField.AltitudeCorrectedK(280.0, 500.0, -20.0, 0.0), 12);
    }

    [Fact]
    public void DiagnosticsAreConsistentWithState()
    {
        SurfaceTemperatureField field = _fx.CreateField();
        var state = new ThermalSnapshot(_fx.Grid.CellCount);
        field.StateAt(state, 50);
        int cell = FindCell(SurfaceThermalKind.Land, 0.3);
        ThermalCellDiagnostics d = field.Diagnose(state, cell);
        Assert.Equal(SurfaceThermalKind.Land, d.Kind);
        Assert.Equal(d.BaselineK + state.ThetaS[cell], d.SurfaceK, 12);
        Assert.Equal(d.BaselineK + state.ThetaA[cell], d.AirK, 12);
        Assert.Equal(0.0, d.MixingWm2);
        Assert.InRange(d.CosZenith, 0.0, 1.0);
        Assert.False(double.IsNaN(d.AdvectionWm2));
    }
}
