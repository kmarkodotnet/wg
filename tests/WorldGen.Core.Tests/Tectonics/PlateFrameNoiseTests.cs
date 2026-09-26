using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// ND-136 (A19) — a domborzati zaj a lemez SAJÁT vonatkoztatási
/// rendszerében értékelődik ki, tehát együtt vándorol a kéreggel.
/// Python referencia: <c>tools/reference/plate_frame_noise_ref.py</c>.
///
/// A KAT-vektorok a referencia <c>plate_frame_noise_vectors.json</c>-jából
/// jönnek; a visszaforgatás ott is a <c>DeterministicMath.SinCos</c> bitpontos
/// tükrét használja (ND-27), ezért a tolerancia szűk.
/// </summary>
public class PlateFrameNoiseVectorFileTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void MatchesPythonReference()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "plate_frame_noise_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        int level = root.GetProperty("level").GetInt32();
        var seeds0 = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        int n = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            TileId id = TileId.FromFaceLevelUV(
                v.GetProperty("face").GetInt32(), level,
                v.GetProperty("u").GetUInt32(), v.GetProperty("v").GetUInt32());
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double timeMyr = v.GetProperty("timeMyr").GetDouble();

            var seeds = PlateMotion.MovedSeeds(worldSeed, seeds0, timeMyr);
            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            PlateBoundaryEffect.TwoBestDots(
                wx, wy, wz, seeds, out _, out _, out int bestIndex, out int secondIndex);
            Assert.Equal(v.GetProperty("bestPlateId").GetInt32(), bestIndex);
            Assert.Equal(v.GetProperty("secondPlateId").GetInt32(), secondIndex);

            PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime(
                worldSeed, x, y, z, wx, wy, wz, seeds, DeepTimeContext.AtPlateTime(timeMyr),
                out double baseElevation, out double uplift, out bool oceanic);

            Assert.True(Math.Abs(baseElevation - v.GetProperty("baseElevation").GetDouble()) < Tol,
                $"base eltér: {baseElevation} vs {v.GetProperty("baseElevation").GetDouble()} (t={timeMyr})");
            Assert.True(Math.Abs(uplift - v.GetProperty("uplift").GetDouble()) < Tol,
                $"uplift eltér: {uplift} vs {v.GetProperty("uplift").GetDouble()} (t={timeMyr})");
            Assert.Equal(v.GetProperty("isOceanic").GetBoolean(), oceanic);
            n++;
        }
        Assert.Equal(400, n);

        // A t=0 blokk a REGRESSZIÓS kapu: ezeknek a statikus M4 értékkel kell
        // egyezniük, nem csak a referenciával.
        int nZero = 0;
        foreach (JsonElement v in root.GetProperty("zeroTimeVectors").EnumerateArray())
        {
            TileId id = TileId.FromFaceLevelUV(
                v.GetProperty("face").GetInt32(), level,
                v.GetProperty("u").GetUInt32(), v.GetProperty("v").GetUInt32());
            TileGeometry.ToPosition(id, out double x, out double y, out double z);

            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds0);
            double actual = PlateBoundaryEffect.ElevationWithBoundaryFromWarped(
                worldSeed, plateId, id.Value, x, y, z, wx, wy, wz, seeds0, out bool oceanic);

            Assert.True(Math.Abs(actual - v.GetProperty("elevation").GetDouble()) < Tol);
            Assert.Equal(v.GetProperty("isOceanic").GetBoolean(), oceanic);
            nZero++;
        }
        Assert.Equal(100, nZero);
    }
}

public class PlateFrameNoisePropertyTests
{
    private const ulong Seed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;

    private static IEnumerable<(double X, double Y, double Z)> SamplePositions(int step)
    {
        uint n = 1u << Level;
        for (int face = 0; face <= 5; face++)
            for (uint u = 0; u < n; u += (uint)step)
                for (uint v = 0; v < n; v += (uint)step)
                {
                    TileGeometry.ToPosition(
                        TileId.FromFaceLevelUV(face, Level, u, v),
                        out double x, out double y, out double z);
                    yield return (x, y, z);
                }
    }

    /// <summary>
    /// A LÉNYEG: ha egy lemez-belseji pontot a lemez SAJÁT mozgásával
    /// elforgatunk, a domborzati zaj ugyanaz marad. Ez az A19 elvárása —
    /// „ha a lemez felfelé megy, akkor a zaj is menjen felfelé egyenletesen
    /// és arányosan vele".
    /// </summary>
    [Fact]
    public void NoiseTravelsWithThePlate()
    {
        const double timeMyr = 250.0;
        var seeds0 = PlateGeneration.GenerateSeeds(Seed, PlateCount);
        var seedsT = PlateMotion.MovedSeeds(Seed, seeds0, timeMyr);

        int checkedCount = 0;
        double maxDiff = 0.0;
        foreach ((double x, double y, double z) in SamplePositions(3))
        {
            DomainWarp.WarpPosition(Seed, x, y, z, out double wx, out double wy, out double wz);
            PlateBoundaryEffect.TwoBestDots(
                wx, wy, wz, seeds0, out double best, out double second, out int bestIndex, out _);
            if (best - second < 0.10)
                continue; // határ közelében a nyertes lemez válthat

            PlateMotion.PlateSeedAtTime(Seed, bestIndex, x, y, z, timeMyr,
                out double mx, out double my, out double mz);

            DomainWarp.WarpPosition(Seed, mx, my, mz, out double mwx, out double mwy, out double mwz);
            PlateBoundaryEffect.TwoBestDots(
                mwx, mwy, mwz, seedsT, out double b2, out double s2, out int movedIndex, out _);
            if (movedIndex != bestIndex || b2 - s2 < 0.10)
                continue;

            CrustElevation.ComputeNoiseBasisInPlateFrame(
                Seed, bestIndex, 0.0, x, y, z,
                out double p0, out double m0, out double s0);
            CrustElevation.ComputeNoiseBasisInPlateFrame(
                Seed, movedIndex, timeMyr, mx, my, mz,
                out double p1, out double m1, out double s1);

            maxDiff = Math.Max(maxDiff, Math.Abs(p0 - p1));
            maxDiff = Math.Max(maxDiff, Math.Abs(m0 - m1));
            maxDiff = Math.Max(maxDiff, Math.Abs(s0 - s1));
            checkedCount++;
        }

        Assert.True(checkedCount > 50, $"túl kevés lemez-belseji minta: {checkedCount}");
        Assert.True(maxDiff < 1e-12, $"a zajnak együtt kell utaznia a lemezzel, max eltérés={maxDiff}");
    }

    /// <summary>
    /// ELLENPÉLDA — a régi, világ-keretes mintavétel ugyanezen a próbán
    /// nagyságrendekkel elbukik. Enélkül a fenti teszt triviálisan is
    /// teljesülhetne (pl. ha a zaj konstans lenne).
    /// </summary>
    [Fact]
    public void WorldFrameNoiseWouldNotTravelWithThePlate()
    {
        const double timeMyr = 250.0;
        var seeds0 = PlateGeneration.GenerateSeeds(Seed, PlateCount);

        double maxDiff = 0.0;
        foreach ((double x, double y, double z) in SamplePositions(7))
        {
            DomainWarp.WarpPosition(Seed, x, y, z, out double wx, out double wy, out double wz);
            PlateBoundaryEffect.TwoBestDots(
                wx, wy, wz, seeds0, out double best, out double second, out int bestIndex, out _);
            if (best - second < 0.10)
                continue;

            PlateMotion.PlateSeedAtTime(Seed, bestIndex, x, y, z, timeMyr,
                out double mx, out double my, out double mz);

            CrustElevation.ComputeNoiseBasis(Seed, x, y, z, out double p0, out _, out _);
            CrustElevation.ComputeNoiseBasis(Seed, mx, my, mz, out double p1, out _, out _);
            maxDiff = Math.Max(maxDiff, Math.Abs(p0 - p1));
        }

        Assert.True(maxDiff > 0.1,
            $"a világ-keretes zajnak ÉRDEMBEN el kell térnie, különben a párja semmit nem bizonyít (max={maxDiff})");
    }

    /// <summary>
    /// t = 0-nál a visszaforgatás EGZAKT azonosság, és a kiterjesztett
    /// keverés bitre a régi eredményt adja — a statikus világok nem törnek.
    /// </summary>
    [Fact]
    public void ZeroTimeIsBitIdenticalToTheStaticPath()
    {
        var seeds = PlateGeneration.GenerateSeeds(Seed, PlateCount);

        foreach ((double x, double y, double z) in SamplePositions(5))
        {
            PlateMotion.ToPlateFrame(Seed, 3, 0.0, x, y, z, out double px, out double py, out double pz);
            Assert.Equal(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(px));
            Assert.Equal(BitConverter.DoubleToInt64Bits(y), BitConverter.DoubleToInt64Bits(py));
            Assert.Equal(BitConverter.DoubleToInt64Bits(z), BitConverter.DoubleToInt64Bits(pz));

            DomainWarp.WarpPosition(Seed, x, y, z, out double wx, out double wy, out double wz);
            int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);

            // A régi út újraszámolva, kézzel (a "keverés csak eltérő
            // kéregtípusnál" szabállyal együtt) - így a teszt akkor is
            // megfogná a regressziót, ha valaki a produkciós utat rontja el.
            PlateBoundaryEffect.TwoBestDots(
                wx, wy, wz, seeds, out double best, out double second,
                out int bestIndex, out int secondIndex);
            CrustElevation.ComputeNoiseBasis(
                Seed, x, y, z, out double primary, out double mask, out double secondary);
            double expected = LegacyBlend(
                Seed, best, second, bestIndex, secondIndex, primary, mask, secondary)
                + PlateBoundaryEffect.BoundaryUpliftFromNearestPlates(
                    Seed, best, second, bestIndex, secondIndex, mask);

            double actual = PlateBoundaryEffect.ElevationWithBoundaryFromWarpedAtTime(
                Seed, plateId, 0UL, x, y, z, wx, wy, wz, seeds, DeepTimeContext.Static, out _);

            Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        }
    }

    /// <summary>Az ND-90 KORÁBBI alakja: keverés CSAK eltérő kéregtípusnál.</summary>
    private static double LegacyBlend(
        ulong worldSeed, double best, double second, int bestIndex, int secondIndex,
        double primary, double mask, double secondary,
        double blendGap = CrustElevation.DefaultBoundaryBlendGap)
    {
        double bestElevation = CrustElevation.BaseElevationFromNoiseBasis(
            worldSeed, bestIndex, primary, mask, secondary, out bool bestOceanic);
        double gap = best - second;
        if (secondIndex < 0 || gap >= blendGap)
            return bestElevation;
        double secondElevation = CrustElevation.BaseElevationFromNoiseBasis(
            worldSeed, secondIndex, primary, mask, secondary, out bool secondOceanic);
        if (bestOceanic == secondOceanic)
            return bestElevation;
        double normalizedGap = gap / blendGap;
        double smoothGap = normalizedGap * normalizedGap * (3.0 - 2.0 * normalizedGap);
        double secondWeight = 0.5 * (1.0 - smoothGap);
        return bestElevation + (secondElevation - bestElevation) * secondWeight;
    }

    /// <summary>Tisztaság: ismételt hívás bitre azonos (nincs rejtett állapot, I2).</summary>
    [Fact]
    public void IsPure()
    {
        var seeds = PlateMotion.MovedSeeds(Seed, PlateGeneration.GenerateSeeds(Seed, PlateCount), 333.0);
        TileGeometry.ToPosition(TileId.FromFaceLevelUV(2, Level, 13, 41), out double x, out double y, out double z);
        DomainWarp.WarpPosition(Seed, x, y, z, out double wx, out double wy, out double wz);

        PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime(
            Seed, x, y, z, wx, wy, wz, seeds, DeepTimeContext.AtPlateTime(333.0),
            out double b1, out double u1, out bool o1);
        PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime(
            Seed, x, y, z, wx, wy, wz, seeds, DeepTimeContext.AtPlateTime(333.0),
            out double b2, out double u2, out bool o2);

        Assert.Equal(BitConverter.DoubleToInt64Bits(b1), BitConverter.DoubleToInt64Bits(b2));
        Assert.Equal(BitConverter.DoubleToInt64Bits(u1), BitConverter.DoubleToInt64Bits(u2));
        Assert.Equal(o1, o2);
    }

    /// <summary>Párhuzamos vs szekvenciális: a sorrend nem befolyásol (I1/I2).</summary>
    [Fact]
    public void ParallelMatchesSequential()
    {
        const double timeMyr = 412.5;
        var seeds = PlateMotion.MovedSeeds(Seed, PlateGeneration.GenerateSeeds(Seed, PlateCount), timeMyr);
        var positions = new List<(double X, double Y, double Z)>(SamplePositions(9));

        var sequential = new double[positions.Count];
        for (int i = 0; i < positions.Count; i++)
            sequential[i] = ElevationAt(positions[i], seeds, timeMyr);

        var parallel = new double[positions.Count];
        Parallel.For(0, positions.Count, i => parallel[i] = ElevationAt(positions[i], seeds, timeMyr));

        for (int i = 0; i < positions.Count; i++)
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(sequential[i]),
                BitConverter.DoubleToInt64Bits(parallel[i]));
    }

    private static double ElevationAt((double X, double Y, double Z) p,
        (double X, double Y, double Z)[] seeds, double timeMyr)
    {
        DomainWarp.WarpPosition(Seed, p.X, p.Y, p.Z, out double wx, out double wy, out double wz);
        return PlateBoundaryEffect.ElevationWithBoundaryFromWarpedAtTime(
            Seed, 0, 0UL, p.X, p.Y, p.Z, wx, wy, wz, seeds, DeepTimeContext.AtPlateTime(timeMyr), out _);
    }

    /// <summary>Minden paraméter érdemben hat: timeMyr, worldSeed, lemez-azonosító.</summary>
    [Fact]
    public void EveryParameterAffectsTheResult()
    {
        var seeds0 = PlateGeneration.GenerateSeeds(Seed, PlateCount);
        TileGeometry.ToPosition(TileId.FromFaceLevelUV(1, Level, 20, 44), out double x, out double y, out double z);

        double atZero = ElevationAt((x, y, z), seeds0, 0.0);
        var seeds500 = PlateMotion.MovedSeeds(Seed, seeds0, 500.0);
        double at500 = ElevationAt((x, y, z), seeds500, 500.0);
        Assert.NotEqual(atZero, at500);

        // Ugyanazok a (mozgatott) magok, de t=0-s zaj: a KÜLÖNBSÉGNEK a zajból
        // kell jönnie, nem csak a lemezhatár elmozdulásából.
        double at500SeedsOnly = ElevationAt((x, y, z), seeds500, 0.0);
        Assert.NotEqual(at500SeedsOnly, at500);

        double otherSeed = 0.0;
        {
            DomainWarp.WarpPosition(Seed ^ 1UL, x, y, z, out double wx, out double wy, out double wz);
            otherSeed = PlateBoundaryEffect.ElevationWithBoundaryFromWarpedAtTime(
                Seed ^ 1UL, 0, 0UL, x, y, z, wx, wy, wz, seeds0, DeepTimeContext.Static, out _);
        }
        Assert.NotEqual(atZero, otherSeed);

        // Külön lemezek külön keretet adnak ugyanarra a pontra.
        CrustElevation.ComputeNoiseBasisInPlateFrame(Seed, 0, 300.0, x, y, z, out double a, out _, out _);
        CrustElevation.ComputeNoiseBasisInPlateFrame(Seed, 1, 300.0, x, y, z, out double b, out _, out _);
        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// A keverés a sáv szélén folytonos: gap = blendGap-nál pontosan a
    /// nyertes lemez értéke, épp alatta ahhoz tetszőlegesen közel — és ez
    /// MOST MÁR azonos kéregtípusnál is igaz (ND-136 óta minden határon
    /// keverünk, különben a lemez-keretes zaj varratot hagyna).
    /// </summary>
    [Fact]
    public void BlendIsContinuousAtTheBandEdgeForEveryBoundary()
    {
        const double gap = CrustElevation.DefaultBoundaryBlendGap;
        // Két SZÁNDÉKOSAN eltérő zaj-bázis, mintha két lemez keretéből jönne.
        const double bp = 0.7, bm = 0.6, bs = -0.3;
        const double sp = -0.5, sm = 0.2, ss = 0.9;

        for (int bestIndex = 0; bestIndex < 6; bestIndex++)
            for (int secondIndex = 0; secondIndex < 6; secondIndex++)
            {
                if (bestIndex == secondIndex) continue;

                double atEdge = CrustElevation.BlendedBaseElevationFromPlateFrameBases(
                    Seed, gap, 0.0, bestIndex, secondIndex, bp, bm, bs, sp, sm, ss, out _);
                double pureBest = CrustElevation.BaseElevationFromNoiseBasis(
                    Seed, bestIndex, bp, bm, bs, out _);
                Assert.Equal(BitConverter.DoubleToInt64Bits(pureBest), BitConverter.DoubleToInt64Bits(atEdge));

                double justInside = CrustElevation.BlendedBaseElevationFromPlateFrameBases(
                    Seed, gap * (1.0 - 1e-9), 0.0, bestIndex, secondIndex, bp, bm, bs, sp, sm, ss, out _);
                Assert.True(Math.Abs(justInside - pureBest) < 1e-3,
                    $"a sáv szélén folytonosnak kell lennie, ugrás={Math.Abs(justInside - pureBest)}");

                // A határon (gap=0) 50/50 keverés - MINDEN lemezpárnál, akkor
                // is, ha a két kéregtípus azonos.
                double atBoundary = CrustElevation.BlendedBaseElevationFromPlateFrameBases(
                    Seed, 0.0, 0.0, bestIndex, secondIndex, bp, bm, bs, sp, sm, ss, out _);
                double pureSecond = CrustElevation.BaseElevationFromNoiseBasis(
                    Seed, secondIndex, sp, sm, ss, out _);
                Assert.True(Math.Abs(atBoundary - 0.5 * (pureBest + pureSecond)) < 1e-9,
                    "a lemezhatáron 50/50 keverést várunk");
            }
    }

    /// <summary>
    /// Élesetek: t = 0, negatív idő (szimmetrikus visszaforgatás), nagyon
    /// nagy t, ismeretlen lemez.
    /// </summary>
    [Fact]
    public void EdgeCases()
    {
        const double x = 0.3, y = 0.4, z = 0.8660254037844386;

        PlateMotion.ToPlateFrame(Seed, -1, 750.0, x, y, z, out double ux, out double uy, out double uz);
        Assert.Equal(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(ux));
        Assert.Equal(BitConverter.DoubleToInt64Bits(y), BitConverter.DoubleToInt64Bits(uy));
        Assert.Equal(BitConverter.DoubleToInt64Bits(z), BitConverter.DoubleToInt64Bits(uz));

        // A visszaforgatás egységvektort ad vissza tetszőleges t-re.
        foreach (double t in new[] { -1000.0, 1e-9, 1000.0, 1_000_000.0 })
        {
            PlateMotion.ToPlateFrame(Seed, 4, t, x, y, z, out double px, out double py, out double pz);
            double len = Math.Sqrt(px * px + py * py + pz * pz);
            Assert.True(Math.Abs(len - 1.0) < 1e-12, $"t={t}: |p|={len}");
        }

        // A -t és +t visszaforgatás egymás inverze.
        PlateMotion.ToPlateFrame(Seed, 4, 137.0, x, y, z, out double ax, out double ay, out double az);
        PlateMotion.ToPlateFrame(Seed, 4, -137.0, ax, ay, az, out double rx, out double ry, out double rz);
        Assert.True(Math.Abs(rx - x) < 1e-12 && Math.Abs(ry - y) < 1e-12 && Math.Abs(rz - z) < 1e-12);
    }

    /// <summary>
    /// A cache-elt bázis idő-tudatos kiértékelése bitre ugyanazt adja, mint a
    /// cache nélküli út — a warp cache-elhető marad, csak a zaj számolódik újra.
    /// </summary>
    [Fact]
    public void CachedBasisEvaluateAtTimeMatchesDirectPath()
    {
        foreach (double timeMyr in new[] { 0.0, 42.0, 617.5 })
        {
            var seeds = PlateMotion.MovedSeeds(Seed, PlateGeneration.GenerateSeeds(Seed, PlateCount), timeMyr);
            foreach ((double x, double y, double z) in SamplePositions(11))
            {
                DomainWarp.WarpPosition(Seed, x, y, z, out double wx, out double wy, out double wz);
                PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime(
                    Seed, x, y, z, wx, wy, wz, seeds, DeepTimeContext.AtPlateTime(timeMyr),
                    out double expectedBase, out double expectedUplift, out bool expectedOceanic);

                TerrainPointBasis basis = TerrainPointBasis.Compute(Seed, x, y, z);
                basis.EvaluateAtTime(Seed, seeds, x, y, z, DeepTimeContext.AtPlateTime(timeMyr),
                    out double actualBase, out double actualUplift, out bool actualOceanic);

                Assert.Equal(expectedOceanic, actualOceanic);
                Assert.Equal(BitConverter.DoubleToInt64Bits(expectedBase), BitConverter.DoubleToInt64Bits(actualBase));
                Assert.Equal(BitConverter.DoubleToInt64Bits(expectedUplift), BitConverter.DoubleToInt64Bits(actualUplift));
            }
        }
    }

    /// <summary>
    /// Plauzibilitás: a lemez-keretes zaj nem torzítja el az elevációeloszlást
    /// (ugyanaz a zajfüggvény, csak elforgatott mintavételi ponton) — a
    /// szárazföld/óceán arány t &gt; 0-nál is a t = 0-hoz hasonló marad.
    /// </summary>
    [Fact]
    public void ElevationDistributionStaysPlausibleOverDeepTime()
    {
        var seeds0 = PlateGeneration.GenerateSeeds(Seed, PlateCount);
        var positions = new List<(double X, double Y, double Z)>(SamplePositions(2));

        double MeanAt(double timeMyr)
        {
            var seeds = PlateMotion.MovedSeeds(Seed, seeds0, timeMyr);
            double sum = 0.0;
            foreach (var p in positions) sum += ElevationAt(p, seeds, timeMyr);
            return sum / positions.Count;
        }

        double m0 = MeanAt(0.0);
        foreach (double t in new[] { 100.0, 500.0, 1000.0 })
        {
            double mt = MeanAt(t);
            Assert.True(Math.Abs(mt - m0) < 600.0,
                $"t={t}: az átlagos eleváció {mt:F1}m, t=0-nál {m0:F1}m - túl nagy eltolódás");
        }
    }
}
