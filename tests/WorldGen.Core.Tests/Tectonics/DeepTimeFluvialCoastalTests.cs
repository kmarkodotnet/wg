using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// ND-137 2. kör — folyóvízi bevágódás (dissection) és parti abrázió.
///
/// Python orákulum: tools/reference/erosion_glaciation_deep_time_ref.py
/// (`fluvial_fraction`, `fluvial_dissection_factor`, `coastal_abrasion_delta`).
/// Mindkét oldal ugyanazt a <c>DeterministicMath</c>-ot implementálja, ezért a
/// tűrés szoros (1e-12).
/// </summary>
public class DeepTimeFluvialCoastalVectorFileTests
{
    private const double Tol = 1e-12;

    private static JsonDocument LoadVectors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "erosion_glaciation_deep_time_vectors.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void FluvialDissectionMatchesPythonReference()
    {
        using JsonDocument doc = LoadVectors();
        int n = 0;
        foreach (JsonElement v in doc.RootElement.GetProperty("fluvialDissectionVectors").EnumerateArray())
        {
            double lat = v.GetProperty("absLatitudeRad").GetDouble();
            double t = v.GetProperty("erosionTimeMyr").GetDouble();
            Assert.True(Math.Abs(DeepTimeErosionGlaciation.FluvialFraction(lat)
                - v.GetProperty("fluvialFraction").GetDouble()) < Tol);
            Assert.True(Math.Abs(DeepTimeErosionGlaciation.FluvialDissectionFactor(lat, t)
                - v.GetProperty("dissectionFactor").GetDouble()) < Tol);
            n++;
        }
        Assert.Equal(300, n);
    }

    [Fact]
    public void CoastalAbrasionMatchesPythonReference()
    {
        using JsonDocument doc = LoadVectors();
        int n = 0;
        foreach (JsonElement v in doc.RootElement.GetProperty("coastalAbrasionVectors").EnumerateArray())
        {
            double elev = v.GetProperty("elevationMeters").GetDouble();
            double sea = v.GetProperty("staticSeaLevelMeters").GetDouble();
            double lat = v.GetProperty("absLatitudeRad").GetDouble();
            double t = v.GetProperty("erosionTimeMyr").GetDouble();
            double expected = v.GetProperty("deltaMeters").GetDouble();
            double actual = DeepTimeErosionGlaciation.CoastalAbrasionDelta(elev, sea, lat, t);
            Assert.True(Math.Abs(actual - expected) < 1e-9, $"delta: {actual} vs {expected}");
            n++;
        }
        Assert.Equal(300, n);
    }
}

public class DeepTimeFluvialDissectionPropertyTests
{
    private static double Z(double deg) => Math.Sin(deg * Math.PI / 180.0);

    private static double PrimaryDecay(double deg, double t)
    {
        DeepTimeErosionGlaciation.ReliefDecayFactors(Z(deg), t, out double dp, out _);
        return dp;
    }

    /// <summary>
    /// A modell lényege: a relief ELŐBB NŐ (a fiatal orogén felszabdalódik),
    /// és csak azután kopik le. Enélkül a deep-time csúszka soha nem tud mást,
    /// mint lapítani.
    /// </summary>
    [Fact]
    public void ReliefRisesThenFalls()
    {
        var curve = new List<(double T, double D)>();
        foreach (double t in new[] { 0.0, 10.0, 25.0, 40.0, 60.0, 100.0, 200.0, 500.0, 1000.0, 3000.0 })
            curve.Add((t, PrimaryDecay(0.0, t)));

        Assert.Equal(1.0, curve[0].D);
        (double peakT, double peakD) = curve.MaxBy(kv => kv.D);
        Assert.True(peakD > 1.10, $"a reliefnek érdemben nőnie kell a bevágódás fázisban: {peakD}");
        Assert.True(peakT > 0.0 && peakT <= 100.0, $"a csúcsnak a fiatal fázisban kell lennie: {peakT}");
        Assert.True(curve[^1].D < 0.60, $"telítésben már le kell kopnia: {curve[^1].D}");
    }

    /// <summary>A jégtakaró elnyomja a bevágódást: a gleccser planál, nem szabdal.</summary>
    [Fact]
    public void IceSuppressesFluvialDissection()
    {
        double equator = DeepTimeErosionGlaciation.FluvialFraction(0.0);
        double polar = DeepTimeErosionGlaciation.FluvialFraction(80.0 * Math.PI / 180.0);
        Assert.True(polar < equator, $"poláris {polar} vs egyenlítői {equator}");
        Assert.True(DeepTimeErosionGlaciation.FluvialDissectionFactor(80.0 * Math.PI / 180.0, 60.0) < 1.10);
        Assert.True(DeepTimeErosionGlaciation.FluvialDissectionFactor(0.0, 60.0) > 1.35);
    }

    /// <summary>
    /// Timestep-invariancia (ND-04) a bevágódásra. A D_primary MOST két önálló
    /// exponenciális tényező SZORZATA, ezért a „lépésenként szimulálva"
    /// értelmezés két állapotváltozót tart — mindkettő önállóan félcsoport.
    /// </summary>
    [Fact]
    public void FluvialDissectionIsTimestepInvariant()
    {
        foreach (double deg in new[] { 0.0, 35.0, 60.0 })
        {
            double lat = deg * Math.PI / 180.0;
            double direct = DeepTimeErosionGlaciation.FluvialDissectionFactor(lat, 320.0);
            foreach (int steps in new[] { 1, 2, 5, 31, 200, 2000 })
            {
                double chained = DeepTimeErosionGlaciation.ChainFluvialDissection(lat, 320.0, steps);
                Assert.True(Math.Abs(chained - direct) < 1e-12,
                    $"{deg} fok, steps={steps}: {chained} vs {direct}");
            }
        }
    }

    /// <summary>
    /// A teljes D_primary (csillapítás × bevágódás) is lépésköz-független, ha
    /// a két komponenst KÜLÖN láncoljuk — ez a tétel operatív tartalma.
    /// </summary>
    [Fact]
    public void CombinedPrimaryFactorIsTimestepInvariant()
    {
        TileGeometry.ToPosition(TileId.FromFaceLevelUV(2, 6, 17, 41), out _, out _, out double z);
        double lat = DeepTimeErosionGlaciation.AbsLatitudeRad(z);
        const double total = 640.0;
        DeepTimeErosionGlaciation.ReliefDecayFactors(z, total, out double direct, out _);
        foreach (int steps in new[] { 1, 2, 3, 7, 29, 113, 1000 })
        {
            double chained = DeepTimeErosionGlaciation.ChainReliefDecay(
                                 z, total, steps, DeepTimeErosionGlaciation.PrimaryReliefTauMyr,
                                 DeepTimeErosionGlaciation.PrimaryReliefEqFraction)
                             * DeepTimeErosionGlaciation.ChainFluvialDissection(lat, total, steps);
            Assert.True(Math.Abs(chained - direct) < 1e-12, $"steps={steps}: {chained} vs {direct}");
        }
    }

    [Fact]
    public void DissectionIsPureAndExactlyOneAtZero()
    {
        foreach (double deg in new[] { 0.0, 20.0, 45.0, 90.0 })
        {
            double lat = deg * Math.PI / 180.0;
            Assert.Equal(1.0, DeepTimeErosionGlaciation.FluvialDissectionFactor(lat, 0.0));
            double a = DeepTimeErosionGlaciation.FluvialDissectionFactor(lat, 137.0);
            double b = DeepTimeErosionGlaciation.FluvialDissectionFactor(lat, 137.0);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void DissectionEdgeCasesStayFinite()
    {
        foreach (double deg in new[] { 0.0, 31.0, 49.0, 90.0 })
        {
            double lat = deg * Math.PI / 180.0;
            foreach (double t in new[] { 1e-9, 1.0, 1e5, 1e9, -1e9 })
            {
                double f = DeepTimeErosionGlaciation.FluvialDissectionFactor(lat, t);
                Assert.True(double.IsFinite(f), $"{deg} fok, t={t}: {f}");
            }
        }
    }
}

public class DeepTimeCoastalAbrasionPropertyTests
{
    private const double Sea = -150.0;

    /// <summary>
    /// A sáv a TENGERSZINT felé planál MINDKÉT irányból: a szirt visszavágódik,
    /// a self feltöltődik. Ez a „lerakódás" parti tagja.
    /// </summary>
    [Fact]
    public void BandPlanesTowardSeaLevelFromBothSides()
    {
        double cliff = DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea + 300.0, Sea, 0.0, 400.0);
        double shoal = DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea - 300.0, Sea, 0.0, 400.0);
        Assert.True(cliff < 0.0, $"a tengerszint FELETTI partnak le kell kopnia: {cliff}");
        Assert.True(shoal > 0.0, $"a tengerszint ALATTI selfnek fel kell töltődnie: {shoal}");
        Assert.Equal(0.0, DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea, Sea, 0.0, 400.0));
    }

    /// <summary>A sávon kívül (belföld, mélytenger) nincs hatás.</summary>
    [Fact]
    public void NoEffectOutsideTheBand()
    {
        foreach (double d in new[] { 4000.0, -4000.0, 12000.0, -12000.0 })
        {
            double delta = DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea + d, Sea, 0.0, 400.0);
            Assert.True(Math.Abs(delta) < 1e-6, $"d={d}: {delta}");
        }
    }

    /// <summary>
    /// Soha nem lő túl a tengerszinten: a part nem válhat tengerré és
    /// viszont. Ez a legfontosabb határ — enélkül az abrázió megfordíthatná a
    /// part/tenger relációt, és a partvonal villogna.
    /// </summary>
    [Fact]
    public void NeverOvershootsSeaLevel()
    {
        foreach (double t in new[] { 1.0, 120.0, 1000.0, 1e5, 1e9 })
        {
            for (double d = -1500.0; d <= 1500.0; d += 25.0)
            {
                double delta = DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea + d, Sea, 0.0, t);
                Assert.True(Math.Abs(delta) <= Math.Abs(d) + 1e-9, $"t={t}, d={d}: {delta}");
                Assert.True(d * (d + delta) >= -1e-9, $"előjelváltás t={t}, d={d}: {delta}");
            }
        }
    }

    /// <summary>Jégtakaró alatt kikapcsol (nincs nyílt vízi hullámzás).</summary>
    [Fact]
    public void DisabledUnderTheIceSheet()
    {
        double polar = DeepTimeErosionGlaciation.CoastalAbrasionDelta(
            Sea + 300.0, Sea, 80.0 * Math.PI / 180.0, 400.0);
        Assert.Equal(0.0, polar);
    }

    /// <summary>t = 0-nál és ismeretlen (NaN) statikus tengerszintnél 0,0.</summary>
    [Fact]
    public void DisabledAtZeroTimeAndUnknownSeaLevel()
    {
        Assert.Equal(0.0, DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea + 300.0, Sea, 0.0, 0.0));
        Assert.Equal(0.0, DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea + 300.0, double.NaN, 0.0, 400.0));
    }

    [Fact]
    public void AbrasionIsPure()
    {
        double a = DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea + 137.0, Sea, 0.3, 411.0);
        double b = DeepTimeErosionGlaciation.CoastalAbrasionDelta(Sea + 137.0, Sea, 0.3, 411.0);
        Assert.Equal(a, b);
    }
}

public class DeepTimeRoundTwoIntegrationTests
{
    private const ulong Seed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;

    private static List<TileId> SampleTiles(int stride = 9)
    {
        var list = new List<TileId>();
        int n = 1 << Level;
        for (int face = 0; face < 6; face++)
            for (uint u = 0; u < n; u += (uint)stride)
                for (uint v = 0; v < n; v += (uint)(stride + 2))
                    list.Add(TileId.FromFaceLevelUV(face, Level, u, v));
        return list;
    }

    /// <summary>
    /// A két új tag EGYIKE SEM változtat semmit t = 0-nál — ez a
    /// bit-kompatibilitás garanciája a már jóváhagyott statikus világra.
    /// </summary>
    [Fact]
    public void ZeroErosionTimeIsUnaffectedByRoundTwo()
    {
        var seeds = PlateGeneration.GenerateSeeds(Seed, PlateCount);
        foreach (TileId id in SampleTiles())
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            int pid = PlateGeneration.AssignPlate(x, y, z, seeds);
            double withSeaLevel = DeepTimeErosionGlaciation.ElevationAtTime(
                Seed, pid, x, y, z, seeds, DeepTimeContext.Static.WithStaticSeaLevel(-150.0), out _);
            double withoutSeaLevel = DeepTimeErosionGlaciation.ElevationAtTime(
                Seed, pid, x, y, z, seeds, DeepTimeContext.Static, out _);
            Assert.Equal(withoutSeaLevel, withSeaLevel);
        }
    }

    /// <summary>Párhuzamos == szekvenciális a teljes, 2. körös úton is.</summary>
    [Fact]
    public void ParallelMatchesSequentialWithRoundTwo()
    {
        var seeds = PlateMotion.MovedSeeds(Seed, PlateGeneration.GenerateSeeds(Seed, PlateCount), 600.0);
        List<TileId> tiles = SampleTiles(5);

        double Evaluate(TileId id)
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            int pid = PlateGeneration.AssignPlate(x, y, z, seeds);
            return DeepTimeErosionGlaciation.ElevationAtTime(
                Seed, pid, x, y, z, seeds,
                DeepTimeContext.Uniform(600.0).WithStaticSeaLevel(-150.0), out _);
        }

        var sequential = new double[tiles.Count];
        for (int i = 0; i < tiles.Count; i++)
            sequential[i] = Evaluate(tiles[i]);

        var parallel = new double[tiles.Count];
        Parallel.For(0, tiles.Count, i => parallel[i] = Evaluate(tiles[i]));

        for (int i = 0; i < tiles.Count; i++)
            Assert.Equal(sequential[i], parallel[i]);
    }

    /// <summary>
    /// A parti abrázió TÉNYLEGESEN hat a mezőre: a tengerszint körüli sávban
    /// lévő tile-ok elevációja közelebb kerül a tengerszinthez.
    /// </summary>
    [Fact]
    public void CoastalAbrasionFlattensTheShoreBand()
    {
        var seeds = PlateMotion.MovedSeeds(Seed, PlateGeneration.GenerateSeeds(Seed, PlateCount), 500.0);
        const double staticSea = -150.0;
        int nearShore = 0, movedTowardSeaLevel = 0;
        foreach (TileId id in SampleTiles(3))
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            int pid = PlateGeneration.AssignPlate(x, y, z, seeds);
            double without = DeepTimeErosionGlaciation.ElevationAtTime(
                Seed, pid, x, y, z, seeds, DeepTimeContext.Uniform(500.0), out _);
            double with = DeepTimeErosionGlaciation.ElevationAtTime(
                Seed, pid, x, y, z, seeds,
                DeepTimeContext.Uniform(500.0).WithStaticSeaLevel(staticSea), out _);
            // 5 szigmán túl a Gauss-sáv exp(-12,5) ~ 3,7e-6, tehát a hatás
            // milliméter alatti. 2 szigmánál még ~30 m — az NEM "kívül".
            if (Math.Abs(without - staticSea) > 5.0 * DeepTimeErosionGlaciation.CoastalBandMeters)
            {
                Assert.True(Math.Abs(with - without) < 1.0, "a sávon kívül nem szabad érdemben hatnia");
                continue;
            }
            nearShore++;
            // SOSEM távolodhat a tengerszinttől (nem fordíthatja meg a
            // part/tenger relációt) - ez a kemény korlát.
            Assert.True(Math.Abs(with - staticSea) <= Math.Abs(without - staticSea) + 1e-9,
                $"távolodott a tengerszinttől: {without} -> {with}");
            if (Math.Abs(with - staticSea) < Math.Abs(without - staticSea) - 1e-9)
                movedTowardSeaLevel++;
        }
        Assert.True(nearShore > 0, "kell legyen part-közeli minta");
        // A jégtakaró alatti minták SZÁNDÉKOSAN nem mozdulnak (nincs nyílt vízi
        // hullámzás), ezért nem várható, hogy MINDEGYIK part-közeli elmozduljon.
        Assert.True(movedTowardSeaLevel > nearShore / 2,
            $"a part-közeli minták többségének a tengerszint felé kell mozdulnia: "
            + $"{movedTowardSeaLevel}/{nearShore}");
    }
}
