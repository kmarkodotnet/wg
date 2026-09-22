using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Core.Tests.Hydrology;

/// <summary>
/// A <see cref="RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel"/>
/// differenciális igazolása a szekvenciális változat ellen.
///
/// A szekvenciális út marad a REFERENCIA-ORÁKULUM: nem írjuk át, és minden
/// ellenőrzés hozzá méri a párhuzamosat. A cél BITRE azonos kimenet - a
/// pontok koordinátái, a megállási ok, ÉS a `MergedIntoRiverIndex` (vagyis a
/// teljes dendritikus fa) is.
///
/// MIÉRT ENNYIRE SZIGORÚ. A `MergedIntoRiverIndex` fából jön a
/// vízhozam-arányos vonalszélesség (ComputeDischargeWeights). Ha a
/// párhuzamosítás a lefoglalási SORRENDET elmozdítaná, a fa átrendeződne, a
/// folyók alakja viszont nagyjából ugyanaz maradna - vagyis a hiba csendes
/// lenne: csak a vonalak szélessége csúszna el, és azt szemmel senki nem
/// fogja meg.
/// </summary>
public class ParallelRiverNetworkTests
{
    private readonly ITestOutputHelper _out;
    public ParallelRiverNetworkTests(ITestOutputHelper o) { _out = o; }

    private sealed class World
    {
        public (double X, double Y, double Z)[] Seeds = Array.Empty<(double, double, double)>();
        public double SeaLevel;
        public List<TileId> Sources = new();
    }

    /// <summary>Ugyanaz a lánc, amit a viewer futtat a forrás-kiválasztáshoz.</summary>
    private static World BuildWorld(ulong seed, int plateCount, int level, int topK)
    {
        var w = new World();
        w.Seeds = PlateGeneration.GenerateSeeds(seed, plateCount);
        Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(seed, plateCount, level);
        w.SeaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.65);
        Dictionary<TileId, bool> isOcean = FlowNetwork.ComputeOceanField(field, w.SeaLevel);
        MoisturePrecipitation.PrecipitationField precip =
            MoisturePrecipitation.Compute(seed, plateCount, level, targetWaterFraction: 0.65);
        w.Sources = RiverPathTracing.SelectRiverSources(
            field, precip.Precipitation, isOcean, w.SeaLevel, topK);
        return w;
    }

    private static void AssertSameNetwork(
        IReadOnlyList<RiverPathTracing.ContinuousRiverPath> expected,
        IReadOnlyList<RiverPathTracing.ContinuousRiverPath> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            RiverPathTracing.ContinuousRiverPath e = expected[i];
            RiverPathTracing.ContinuousRiverPath a = actual[i];
            Assert.Equal(e.SourceIndex, a.SourceIndex);
            Assert.Equal(e.Termination, a.Termination);
            Assert.Equal(e.MergedIntoRiverIndex, a.MergedIntoRiverIndex);
            Assert.Equal(e.Points.Count, a.Points.Count);
            for (int k = 0; k < e.Points.Count; k++)
            {
                // BITRE: a koordináták ugyanabból a determinisztikus láncból
                // jönnek, tehát nincs helye toleranciának.
                Assert.Equal(e.Points[k].X, a.Points[k].X);
                Assert.Equal(e.Points[k].Y, a.Points[k].Y);
                Assert.Equal(e.Points[k].Z, a.Points[k].Z);
            }
        }
    }

    [Fact]
    public void AlreadyCancelledParallelBuildStopsBeforeTracing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                1UL, Array.Empty<(double X, double Y, double Z)>(), 0.0,
                Array.Empty<TileId>(), fineDepth: 2,
                cancellation: cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationAfterSourceSelectionReachesTheTracer(bool parallel)
    {
        using var cancellation = new CancellationTokenSource();
        var sources = new CancellingSources(cancellation);

        OperationCanceledException exception = Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            if (parallel)
                RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                    1UL, Array.Empty<(double X, double Y, double Z)>(), 0.0,
                    sources, fineDepth: 2, cancellation: cancellation.Token);
            else
                RiverPathTracing.BuildContinuousRiverNetworkFromSources(
                    1UL, Array.Empty<(double X, double Y, double Z)>(), 0.0,
                    sources, fineDepth: 2, cancellation: cancellation.Token);
        });

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public void BoundedSequentialAndParallelNetworksMatchWithCancellationEnabled()
    {
        const ulong seed = 0xA7C944210000UL;
        var seeds = PlateGeneration.GenerateSeeds(seed, 20);
        var sources = new[]
        {
            TileId.FromFaceLevelUV(0, 5, 10, 10),
            TileId.FromFaceLevelUV(0, 5, 10, 10),
            TileId.FromFaceLevelUV(2, 5, 16, 18),
        };
        using var cancellation = new CancellationTokenSource();
        var sequential = RiverPathTracing.BuildContinuousRiverNetworkFromSources(
            seed, seeds, -100000.0, sources, fineDepth: 2,
            escapeNodeBudget: 32, maxSteps: 16, cancellation: cancellation.Token);
        var parallel = RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
            seed, seeds, -100000.0, sources, fineDepth: 2,
            escapeNodeBudget: 32, maxSteps: 16, cancellation: cancellation.Token);

        Assert.Contains(sequential, river => river.Points.Count > 1);
        AssertSameNetwork(sequential, parallel);
        Assert.Equal(RiverPathTracing.ComputeDischargeWeights(sequential),
            RiverPathTracing.ComputeDischargeWeights(parallel));
    }

    private sealed class CancellingSources : IReadOnlyList<TileId>
    {
        private readonly CancellationTokenSource _cancellation;

        public CancellingSources(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public int Count => 1;

        public TileId this[int index]
        {
            get
            {
                _cancellation.Cancel();
                return TileId.FromFaceLevelUV(0, 5, 10, 10);
            }
        }

        public IEnumerator<TileId> GetEnumerator()
        {
            yield return this[0];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <remarks>
    /// A topK SZANDEKOSAN kicsi: a SZEKVENCIALIS orakulum ~0,8 s/folyo, tehat
    /// egy topK=40-es eset egymaga 30 masodperc lenne. Az egyenertekuseget a
    /// megallasi logika donti el, nem a folyok szama - ket vilag, ket
    /// forrasszam, osszefolyasokkal eleg hozza. A nagyobb halozatokat a
    /// tobbi (csak parhuzamos) teszt jarja be.
    /// </remarks>
    [Theory]
    [InlineData(0xA7C944210000UL, 20, 6, 12)]
    [InlineData(0xFEDCBA987654UL, 31, 6, 14)]
    public void ParallelMatchesTheSequentialOracle(ulong seed, int plateCount, int level, int topK)
    {
        World w = BuildWorld(seed, plateCount, level, topK);
        Assert.NotEmpty(w.Sources);

        var sw = Stopwatch.StartNew();
        List<RiverPathTracing.ContinuousRiverPath> sequential =
            RiverPathTracing.BuildContinuousRiverNetworkFromSources(
                seed, w.Seeds, w.SeaLevel, w.Sources, fineDepth: 2);
        sw.Stop();
        double sequentialMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        List<RiverPathTracing.ContinuousRiverPath> parallel =
            RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                seed, w.Seeds, w.SeaLevel, w.Sources, fineDepth: 2);
        sw.Stop();

        int mergedCount = 0;
        foreach (RiverPathTracing.ContinuousRiverPath r in sequential)
            if (r.MergedIntoRiverIndex >= 0) mergedCount++;

        _out.WriteLine($"seed=0x{seed:X} plates={plateCount} topK={topK}: "
            + $"{w.Sources.Count} forrás, {mergedCount} összefolyás | "
            + $"szekvenciális={sequentialMs:F0}ms párhuzamos={sw.Elapsed.TotalMilliseconds:F0}ms "
            + $"({sequentialMs / Math.Max(1.0, sw.Elapsed.TotalMilliseconds):F1}x)");

        AssertSameNetwork(sequential, parallel);
    }

    /// <summary>
    /// A vízhozam-fa is azonos. Ez erősebb, mint a `MergedIntoRiverIndex`
    /// egyenkénti egyezése: a súlyok a TELJES fán halmozódnak, tehát egyetlen
    /// elcsúszott összefolyás is meglátszik itt.
    /// </summary>
    [Fact]
    public void DischargeWeightsAreIdentical()
    {
        World w = BuildWorld(0xA7C944210000UL, 20, 6, 16);

        int[] sequential = RiverPathTracing.ComputeDischargeWeights(
            RiverPathTracing.BuildContinuousRiverNetworkFromSources(
                0xA7C944210000UL, w.Seeds, w.SeaLevel, w.Sources, fineDepth: 2));
        int[] parallel = RiverPathTracing.ComputeDischargeWeights(
            RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                0xA7C944210000UL, w.Seeds, w.SeaLevel, w.Sources, fineDepth: 2));

        Assert.Equal(sequential.Length, parallel.Length);
        int maxWeight = 0;
        for (int i = 0; i < sequential.Length; i++)
        {
            Assert.Equal(sequential[i], parallel[i]);
            if (sequential[i] > maxWeight) maxWeight = sequential[i];
        }
        _out.WriteLine($"{sequential.Length} folyó, legnagyobb vízhozam-súly={maxWeight}");
        Assert.True(maxWeight > 1, "Egyetlen összefolyás sincs - a teszt a fát nem méri.");
    }

    /// <summary>
    /// TISZTASÁG: a párhuzamos változat ismételt hívása ugyanazt adja. A
    /// `Parallel.For` ütemezése futásonként más, tehát ha bárhol megosztott
    /// állapotra épülnénk, ez elbukna.
    /// </summary>
    [Fact]
    public void RepeatedParallelRunsAreIdentical()
    {
        World w = BuildWorld(0x1234567890ABUL, 12, 6, 16);

        List<RiverPathTracing.ContinuousRiverPath> first =
            RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                0x1234567890ABUL, w.Seeds, w.SeaLevel, w.Sources, fineDepth: 2);
        for (int run = 0; run < 2; run++)
        {
            List<RiverPathTracing.ContinuousRiverPath> again =
                RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                    0x1234567890ABUL, w.Seeds, w.SeaLevel, w.Sources, fineDepth: 2);
            AssertSameNetwork(first, again);
        }
    }

    /// <summary>
    /// TÖBB FORRÁS -> TÖBB ÖSSZEFOLYÁS. Ez a felhasználói visszajelzés
    /// magja: "nincs tree alakzat, sosem ér bele egyik a másikba". 12
    /// forrásnál a nyomvonalak gyakorlatilag soha nem találkoznak; ez a teszt
    /// rögzíti, hogy a forrásszám növelése tényleg fát ad, nem csak több
    /// párhuzamos vonalat.
    /// </summary>
    [Fact]
    public void MoreSourcesProduceMoreConfluences()
    {
        const ulong seed = 0xA7C944210000UL;
        int MergedCount(int topK)
        {
            World w = BuildWorld(seed, 20, 6, topK);
            List<RiverPathTracing.ContinuousRiverPath> rivers =
                RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                    seed, w.Seeds, w.SeaLevel, w.Sources, fineDepth: 2);
            int merged = 0;
            foreach (RiverPathTracing.ContinuousRiverPath r in rivers)
                if (r.MergedIntoRiverIndex >= 0) merged++;
            _out.WriteLine($"topK={topK,3}: {w.Sources.Count,3} forrás -> {merged,3} összefolyás "
                + $"({(w.Sources.Count == 0 ? 0 : 100.0 * merged / w.Sources.Count):F0}%)");
            return merged;
        }

        int few = MergedCount(12);
        int many = MergedCount(48);

        Assert.True(many > few,
            $"48 forrás nem adott több összefolyást, mint 12 ({many} vs {few}) - "
            + "akkor a forrásszám növelése nem fát épít, és a #5 visszajelzésre más válasz kell.");
    }
}
