using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using Xunit;

namespace WorldGen.Core.Tests.Hydrology;

/// <summary>
/// A folytonos folyó-nyomkövető v2 (ND-186, az ND-180 "C" opciója) tesztjei a
/// Python-orákulum (`tools/reference/river_continuous_ref.py`) SZINTETIKUS,
/// analitikus domborzatain.
///
/// MIÉRT SZINTETIKUS domborzaton. A valódi bolygó elevációs láncát (domain
/// warp + lemezkeret + erózió) a Python oldal nem tudja bitre reprodukálni, a
/// JAVÍTOTT HIBAOSZTÁLYOK viszont analitikus síkon, völgyben, medencében és
/// két mederben PONTOSAN előállnak - és az orákulum ugyanazt a kódot méri,
/// amit a termék futtat (a követő az <see cref="RiverPathTracing.IElevationSampler"/>
/// -en keresztül paraméterezhető).
/// </summary>
public class ContinuousRiverV2Tests
{
    private static JsonDocument LoadVectors() => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "river_continuous_vectors.json")));

    // ------------------------------------------------------------------
    // Szintetikus domborzatok - a Python `TERRAINS` szótár C# párjai.
    // KIZÁRÓLAG + - * / műveletek, hogy bitre ugyanazt adják.
    // ------------------------------------------------------------------
    private const double BaseElevationMeters = 3_000.0;
    private const double SlopeMetersPerUnit = 40_000.0;
    private const double WallMetersPerUnit2 = 40_000_000.0;

    private readonly struct PlaneTerrain : RiverPathTracing.IElevationSampler
    {
        public double Sample(double x, double y, double z) => BaseElevationMeters - SlopeMetersPerUnit * x;
    }

    private readonly struct ValleyTerrain : RiverPathTracing.IElevationSampler
    {
        public double Sample(double x, double y, double z) =>
            BaseElevationMeters - SlopeMetersPerUnit * x + WallMetersPerUnit2 * y * y;
    }

    private readonly struct BasinTerrain : RiverPathTracing.IElevationSampler
    {
        private const double CenterX = 0.030;
        private const double Depth = 800.0;
        private const double Radius = 0.020;

        public double Sample(double x, double y, double z)
        {
            double dx = x - CenterX;
            double d2 = dx * dx + y * y;
            double radius2 = Radius * Radius;
            double bowl = d2 < radius2 ? -Depth * (1.0 - d2 / radius2) : 0.0;
            return BaseElevationMeters - SlopeMetersPerUnit * x + bowl;
        }
    }

    private readonly struct TwinValleyTerrain : RiverPathTracing.IElevationSampler
    {
        private readonly double _half;
        public TwinValleyTerrain(double separation) => _half = separation * 0.5;

        public double Sample(double x, double y, double z)
        {
            double left = y + _half;
            double right = y - _half;
            double nearest = Math.Min(left * left, right * right);
            return BaseElevationMeters - SlopeMetersPerUnit * x + WallMetersPerUnit2 * nearest;
        }
    }

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        return (x / length, y / length, z / length);
    }

    private static List<RiverPathTracing.ContinuousRiverPath> BuildNetwork(
        string terrain, IReadOnlyList<(double X, double Y, double Z)> sources,
        double seaLevel, TracerParameters parameters)
    {
        var claimed = new ClaimedRiverPoints(parameters.MergeRadiusMeters);
        var rivers = new List<RiverPathTracing.ContinuousRiverPath>();
        for (int i = 0; i < sources.Count; i++)
        {
            RiverPathTracing.ContinuousRiverPath river = Trace(terrain, sources[i], i, seaLevel, claimed, parameters);
            rivers.Add(river);
            foreach ((double X, double Y, double Z) p in river.Points) claimed.Add(p, i);
        }
        return rivers;
    }

    private static RiverPathTracing.ContinuousRiverPath Trace(
        string terrain, (double X, double Y, double Z) source, int index, double seaLevel,
        ClaimedRiverPoints? claimed, TracerParameters p)
    {
        switch (terrain)
        {
            case "plane":
                return RiverPathTracing.TraceContinuousFrom(default(PlaneTerrain), source, index, seaLevel, claimed,
                    p.StepMeters, p.SensingRadiusMeters, p.RingDirections, p.EscapeCellMeters, p.EscapeNodeBudget, p.MaxSteps,
                    RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
                    p.EscapeEmitMeters);
            case "valley":
                return RiverPathTracing.TraceContinuousFrom(default(ValleyTerrain), source, index, seaLevel, claimed,
                    p.StepMeters, p.SensingRadiusMeters, p.RingDirections, p.EscapeCellMeters, p.EscapeNodeBudget, p.MaxSteps,
                    RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
                    p.EscapeEmitMeters);
            case "basin":
                return RiverPathTracing.TraceContinuousFrom(default(BasinTerrain), source, index, seaLevel, claimed,
                    p.StepMeters, p.SensingRadiusMeters, p.RingDirections, p.EscapeCellMeters, p.EscapeNodeBudget, p.MaxSteps,
                    RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
                    p.EscapeEmitMeters);
            case "twin_near":
                return RiverPathTracing.TraceContinuousFrom(new TwinValleyTerrain(0.0004), source, index, seaLevel, claimed,
                    p.StepMeters, p.SensingRadiusMeters, p.RingDirections, p.EscapeCellMeters, p.EscapeNodeBudget, p.MaxSteps,
                    RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
                    p.EscapeEmitMeters);
            case "twin_far":
                return RiverPathTracing.TraceContinuousFrom(new TwinValleyTerrain(0.0120), source, index, seaLevel, claimed,
                    p.StepMeters, p.SensingRadiusMeters, p.RingDirections, p.EscapeCellMeters, p.EscapeNodeBudget, p.MaxSteps,
                    RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
                    p.EscapeEmitMeters);
            default:
                throw new ArgumentOutOfRangeException(nameof(terrain), terrain);
        }
    }

    private readonly struct TracerParameters
    {
        public readonly double StepMeters;
        public readonly double SensingRadiusMeters;
        public readonly int RingDirections;
        public readonly double EscapeCellMeters;
        public readonly int EscapeNodeBudget;
        public readonly long MaxSteps;
        public readonly double MergeRadiusMeters;
        public readonly double EscapeEmitMeters;

        public TracerParameters(JsonElement json)
        {
            StepMeters = json.GetProperty("step_meters").GetDouble();
            SensingRadiusMeters = json.GetProperty("sensing_radius_meters").GetDouble();
            EscapeCellMeters = json.GetProperty("escape_cell_meters").GetDouble();
            EscapeNodeBudget = json.GetProperty("escape_node_budget").GetInt32();
            MaxSteps = json.GetProperty("max_steps").GetInt64();
            MergeRadiusMeters = json.GetProperty("merge_radius_meters").GetDouble();
            EscapeEmitMeters = json.GetProperty("escape_emit_meters").GetDouble();
            RingDirections = RiverPathTracing.DefaultContinuousRingDirections;
        }
    }

    // ------------------------------------------------------------------
    // 1. Ismert-válasz teszt: a Python-orákulum MINDEN pontja bitre.
    // ------------------------------------------------------------------
    [Fact]
    public void PythonOracleVectorsMatchBitExactly()
    {
        using JsonDocument doc = LoadVectors();
        Assert.Equal(PlanetConstants.RadiusMeters, doc.RootElement.GetProperty("radiusMeters").GetDouble());

        // A bitpontos iránytábla és az atan-mentes rács KAT-vektorai is az
        // orákulumból jönnek - ezek a v2 két új, önálló építőköve.
        foreach (JsonProperty table in doc.RootElement.GetProperty("ringTables").EnumerateObject())
        {
            int count = int.Parse(table.Name, System.Globalization.CultureInfo.InvariantCulture);
            (double X, double Y)[] actual = RiverDirectionTable.Build(count);
            Assert.Equal(table.Value.GetArrayLength(), actual.Length);
            for (int k = 0; k < actual.Length; k++)
            {
                Assert.Equal(table.Value[k][0].GetDouble(), actual[k].X);
                Assert.Equal(table.Value[k][1].GetDouble(), actual[k].Y);
            }
        }
        foreach (JsonElement cell in doc.RootElement.GetProperty("latticeCells").EnumerateArray())
        {
            JsonElement point = cell.GetProperty("point");
            CubeFaceLattice.FromPosition(point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble(),
                cell.GetProperty("level").GetInt32(), out int face, out int i, out int j);
            JsonElement expected = cell.GetProperty("cell");
            Assert.Equal(expected[0].GetInt32(), face);
            Assert.Equal(expected[1].GetInt32(), i);
            Assert.Equal(expected[2].GetInt32(), j);
        }

        int caseCount = 0, pointCount = 0, spanPointCount = 0;
        foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name = c.GetProperty("name").GetString()!;
            string terrain = c.GetProperty("terrain").GetString()!;
            double seaLevel = c.GetProperty("seaLevel").GetDouble();
            var parameters = new TracerParameters(c.GetProperty("params"));

            var sources = new List<(double X, double Y, double Z)>();
            foreach (JsonElement s in c.GetProperty("sources").EnumerateArray())
            {
                sources.Add((s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
            }

            List<RiverPathTracing.ContinuousRiverPath> actual =
                BuildNetwork(terrain, sources, seaLevel, parameters);
            JsonElement expectedRivers = c.GetProperty("rivers");
            Assert.Equal(expectedRivers.GetArrayLength(), actual.Count);

            for (int i = 0; i < actual.Count; i++)
            {
                JsonElement expected = expectedRivers[i];
                RiverPathTracing.ContinuousRiverPath river = actual[i];
                Assert.Equal(expected.GetProperty("sourceIndex").GetInt32(), river.SourceIndex);
                Assert.Equal(expected.GetProperty("termination").GetString(), river.Termination.ToString());
                Assert.Equal(expected.GetProperty("mergedIntoRiverIndex").GetInt32(), river.MergedIntoRiverIndex);
                JsonElement points = expected.GetProperty("points");
                Assert.Equal(points.GetArrayLength(), river.Points.Count);
                for (int k = 0; k < river.Points.Count; k++)
                {
                    // BITRE: a Python és a C# ugyanazokat az IEEE-754
                    // műveleteket végzi ugyanabban a sorrendben.
                    Assert.Equal(points[k][0].GetDouble(), river.Points[k].X);
                    Assert.Equal(points[k][1].GetDouble(), river.Points[k].Y);
                    Assert.Equal(points[k][2].GetDouble(), river.Points[k].Z);
                    pointCount++;
                }
                JsonElement claimChecks = expected.GetProperty("claimCheckIndices");
                Assert.Equal(claimChecks.GetArrayLength(), river.ClaimCheckIndices.Count);
                for (int k = 0; k < river.ClaimCheckIndices.Count; k++)
                    Assert.Equal(claimChecks[k].GetInt32(), river.ClaimCheckIndices[k]);

                // ND-187: a viz alatti (to-)tartomanyok is az orakulumhoz
                // mertek - ugyanazon indexekkel, mint a Pythonban.
                JsonElement spans = expected.GetProperty("submergedSpans");
                Assert.Equal(spans.GetArrayLength(), river.SubmergedSpans.Count);
                int previousEnd = -1;
                for (int k = 0; k < river.SubmergedSpans.Count; k++)
                {
                    Assert.Equal(spans[k][0].GetInt32(), river.SubmergedSpans[k].Start);
                    Assert.Equal(spans[k][1].GetInt32(), river.SubmergedSpans[k].End);
                    // Invariansok: a Points-on belul, diszjunkt es novekvo.
                    Assert.True(river.SubmergedSpans[k].Start > previousEnd);
                    Assert.True(river.SubmergedSpans[k].Start < river.SubmergedSpans[k].End);
                    Assert.True(river.SubmergedSpans[k].End < river.Points.Count);
                    previousEnd = river.SubmergedSpans[k].End;
                    spanPointCount += river.SubmergedSpans[k].End - river.SubmergedSpans[k].Start + 1;
                }
            }
            Assert.False(string.IsNullOrEmpty(name));
            caseCount++;
        }
        // A ket oldal KUSZOBEI ne csuszhassanak el csendben: az orakulum
        // `defaults` blokkja es a C# konstansok ugyanazt az erteket adjak.
        JsonElement defaults = doc.RootElement.GetProperty("defaults");
        Assert.Equal(RiverPathTracing.DefaultContinuousEscapeEmitMeters,
            defaults.GetProperty("escapeEmitMeters").GetDouble());
        Assert.Equal(RiverPathTracing.DefaultSubmergedMinDepthMeters,
            defaults.GetProperty("submergedMinDepthMeters").GetDouble());
        Assert.Equal(RiverPathTracing.DefaultMergeRadiusMeters,
            defaults.GetProperty("mergeRadiusMeters").GetDouble());
        Assert.Equal(8, caseCount);
        Assert.True(pointCount > 800, $"A vektorfájl túl kevés pontot fedett le: {pointCount}");
        // A medence-esetek LEFEDIK a viz alatti tartomanyokat - ha ez nullara
        // esne, a span-osszehasonlitas uresen "zold" lenne.
        Assert.True(spanPointCount > 50,
            $"A vektorfájl túl kevés víz alatti pontot fedett le: {spanPointCount}");
    }

    // ------------------------------------------------------------------
    // 2. Az ND-180 elfogadási kapuja: nincs többkilométeres összefolyási
    //    teleport, és nincs a lépésköznél érdemben hosszabb él.
    // ------------------------------------------------------------------
    [Fact]
    public void MergeEdgeIsBoundedByToleranceAndNoCoarseEdgesRemain()
    {
        using JsonDocument doc = LoadVectors();
        int merges = 0, escapes = 0;
        foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var parameters = new TracerParameters(c.GetProperty("params"));
            string terrain = c.GetProperty("terrain").GetString()!;
            double seaLevel = c.GetProperty("seaLevel").GetDouble();
            var sources = new List<(double X, double Y, double Z)>();
            foreach (JsonElement s in c.GetProperty("sources").EnumerateArray())
                sources.Add((s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));

            foreach (RiverPathTracing.ContinuousRiverPath river in BuildNetwork(terrain, sources, seaLevel, parameters))
            {
                Assert.True(river.Points.Count >= 2);
                for (int k = 1; k < river.Points.Count; k++)
                {
                    double edge = SphereWalk.ChordMeters(river.Points[k - 1], river.Points[k]);
                    bool isMergeEdge = river.Termination == RiverPathTracing.TerminationReason.Merged
                                       && k == river.Points.Count - 1;
                    double limit = isMergeEdge
                        ? parameters.MergeRadiusMeters
                        : Math.Max(parameters.StepMeters, parameters.EscapeEmitMeters) * 1.5;
                    Assert.True(edge <= limit + 1e-6,
                        $"{c.GetProperty("name").GetString()}: {edge:F1} m él a {limit:F1} m korlát felett.");
                }
                if (river.Termination == RiverPathTracing.TerminationReason.Merged) merges++;
            }
            if (c.GetProperty("name").GetString() == "basin-escape") escapes++;
        }
        Assert.True(merges >= 3, $"A vektorok nem fedik le az összefolyást: {merges}");
        Assert.Equal(1, escapes);
    }

    // ------------------------------------------------------------------
    // 3. Tisztaság: ismételt hívás bitre ugyanazt adja.
    // ------------------------------------------------------------------
    [Fact]
    public void RepeatedCallIsBitIdentical()
    {
        var parameters = FastParameters();
        (double X, double Y, double Z) source = Normalize(0.0, 0.0008, 1.0);
        RiverPathTracing.ContinuousRiverPath a = Trace("valley", source, 0, 0.0, null, parameters);
        RiverPathTracing.ContinuousRiverPath b = Trace("valley", source, 0, 0.0, null, parameters);
        Assert.Equal(a.Points.Count, b.Points.Count);
        for (int i = 0; i < a.Points.Count; i++)
        {
            Assert.Equal(a.Points[i].X, b.Points[i].X);
            Assert.Equal(a.Points[i].Y, b.Points[i].Y);
            Assert.Equal(a.Points[i].Z, b.Points[i].Z);
        }
    }

    // ------------------------------------------------------------------
    // 4. Minden paraméter érdemben hat a kimenetre.
    // ------------------------------------------------------------------
    [Fact]
    public void EveryParameterChangesTheResult()
    {
        var baseline = FastParameters();
        (double X, double Y, double Z) source = Normalize(0.0, 0.0008, 1.0);
        int basePoints = Trace("valley", source, 0, 0.0, null, baseline).Points.Count;

        int halfStep = RiverPathTracing.TraceContinuousFrom(default(ValleyTerrain), source, 0, 0.0, null,
            baseline.StepMeters * 0.5, baseline.SensingRadiusMeters, baseline.RingDirections,
            baseline.EscapeCellMeters, baseline.EscapeNodeBudget, baseline.MaxSteps,
            RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
            baseline.EscapeEmitMeters).Points.Count;
        Assert.NotEqual(basePoints, halfStep);

        RiverPathTracing.ContinuousRiverPath wideSensing = RiverPathTracing.TraceContinuousFrom(
            default(ValleyTerrain), source, 0, 0.0, null,
            baseline.StepMeters, baseline.SensingRadiusMeters * 4.0, baseline.RingDirections,
            baseline.EscapeCellMeters, baseline.EscapeNodeBudget, baseline.MaxSteps,
            RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
            baseline.EscapeEmitMeters);
        RiverPathTracing.ContinuousRiverPath narrowSensing = Trace("valley", source, 0, 0.0, null, baseline);
        Assert.False(SamePath(wideSensing, narrowSensing), "Az érzékelési sugár nem hatott a nyomvonalra.");

        RiverPathTracing.ContinuousRiverPath manyDirections = RiverPathTracing.TraceContinuousFrom(
            default(ValleyTerrain), source, 0, 0.0, null,
            baseline.StepMeters, baseline.SensingRadiusMeters, 32,
            baseline.EscapeCellMeters, baseline.EscapeNodeBudget, baseline.MaxSteps,
            RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
            baseline.EscapeEmitMeters);
        Assert.False(SamePath(manyDirections, narrowSensing), "A jelölt-irányok száma nem hatott a nyomvonalra.");

        // A tengerszint a lezárást mozdítja el.
        Assert.NotEqual(basePoints, Trace("valley", source, 0, 1_000.0, null, baseline).Points.Count);

        // A lépésszám-korlát valóban vág.
        RiverPathTracing.ContinuousRiverPath clipped = RiverPathTracing.TraceContinuousFrom(
            default(ValleyTerrain), source, 0, 0.0, null,
            baseline.StepMeters, baseline.SensingRadiusMeters, baseline.RingDirections,
            baseline.EscapeCellMeters, baseline.EscapeNodeBudget, 10,
            RiverPathTracing.DefaultVisitedLatticeLevel, RiverPathTracing.DefaultEscapeShortcutCells,
            baseline.EscapeEmitMeters);
        Assert.Equal(RiverPathTracing.TerminationReason.MaxSteps, clipped.Termination);
    }

    // ------------------------------------------------------------------
    // 5. Élesetek és a szerződések.
    // ------------------------------------------------------------------
    [Fact]
    public void EdgeCasesAndContracts()
    {
        // A medence-eset ESCAPE-et használ, és utána eléri az óceánt.
        var basin = FastParameters(escapeCellMeters: 20_000.0);
        RiverPathTracing.ContinuousRiverPath river =
            Trace("basin", Normalize(0.0, 0.0, 1.0), 0, 0.0, null, basin);
        Assert.Equal(RiverPathTracing.TerminationReason.Ocean, river.Termination);

        // Hurokmentesség: egyetlen pont sem fordul elő kétszer a
        // hurok-védelem rácsán (ez a strukturális garancia mérése).
        var seen = new HashSet<long>();
        foreach ((double X, double Y, double Z) p in river.Points)
        {
            long key = CubeFaceLattice.KeyFromPosition(p.X, p.Y, p.Z,
                RiverPathTracing.DefaultVisitedLatticeLevel);
            seen.Add(key);
        }
        Assert.True(seen.Count >= river.Points.Count - 1,
            $"A nyomvonal {river.Points.Count - seen.Count} pontja ugyanabba a 14 m-es cellába esett.");

        // Forrás a tengerszint alatt: azonnali Ocean, egyetlen ponttal.
        RiverPathTracing.ContinuousRiverPath drowned =
            Trace("plane", Normalize(0.0, 0.0, 1.0), 0, 10_000.0, null, FastParameters());
        Assert.Equal(RiverPathTracing.TerminationReason.Ocean, drowned.Termination);
        Assert.Single(drowned.Points);

        // Üres összefolyási index: nincs Merged.
        Assert.NotEqual(RiverPathTracing.TerminationReason.Merged,
            Trace("plane", Normalize(0.0, 0.0, 1.0), 0, 0.0, new ClaimedRiverPoints(), FastParameters()).Termination);

        // A bucket-rács szintje a toleranciából SZÁMOLT, és a lefedési
        // feltétel (tolerancia <= a garantált legkisebb cella) mindig áll.
        foreach (double radius in new[] { 1.0, 50.0, 100.0, 500.0, 2_000.0, 100_000.0 })
        {
            int level = ClaimedRiverPoints.LatticeLevelFor(radius);
            Assert.True(CubeFaceLattice.MinCellMeters(level) >= radius,
                $"{radius} m: level {level} cellája kisebb a toleranciánál.");
            Assert.InRange(level, 1, CubeFaceLattice.MaxLevel);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClaimedRiverPoints(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClaimedRiverPoints(1e9));

        // A jelölt-irány tábla EGZAKTUL szimmetrikus (szögfelezéssel épül,
        // tehát trigonometria nélkül) - a k. és a (k + n/2). irány bitre
        // egymás ellentettje, különben a gradiens-összeg nem lenne átlagmentes.
        foreach (int count in new[] { 4, 8, 16, 32 })
        {
            (double X, double Y)[] table = RiverDirectionTable.Build(count);
            Assert.Equal(count, table.Length);
            for (int k = 0; k < count; k++)
            {
                (double X, double Y) opposite = table[(k + count / 2) % count];
                Assert.Equal(-opposite.X, table[k].X);
                Assert.Equal(-opposite.Y, table[k].Y);
                Assert.Equal(1.0, Math.Sqrt(table[k].X * table[k].X + table[k].Y * table[k].Y), 15);
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => RiverDirectionTable.Build(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => RiverDirectionTable.Build(12));
    }

    private static bool SamePath(
        RiverPathTracing.ContinuousRiverPath a, RiverPathTracing.ContinuousRiverPath b)
    {
        if (a.Points.Count != b.Points.Count) return false;
        for (int i = 0; i < a.Points.Count; i++)
        {
            if (a.Points[i].X != b.Points[i].X || a.Points[i].Y != b.Points[i].Y || a.Points[i].Z != b.Points[i].Z)
                return false;
        }
        return true;
    }

    private static TracerParameters FastParameters(double escapeCellMeters = 40_000.0)
    {
        using JsonDocument doc = JsonDocument.Parse(
            "{\"step_meters\":5000.0,\"sensing_radius_meters\":20000.0,\"escape_cell_meters\":"
            + escapeCellMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"escape_node_budget\":4000,\"max_steps\":400,\"merge_radius_meters\":20000.0"
            + ",\"escape_emit_meters\":5000.0}");
        return new TracerParameters(doc.RootElement);
    }
}
