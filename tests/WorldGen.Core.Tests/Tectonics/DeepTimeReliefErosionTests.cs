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
/// ND-137 (A20) — deep-time RELIEF-erózió: a domborzati zajtagok
/// hullámhossz-szelektív, hidrológia-vezérelt kopása.
///
/// A Python orákulum: tools/reference/erosion_glaciation_deep_time_ref.py
/// (`relief_decay_factors`, `zonal_water_factor`, `glaciated_fraction`).
/// Mivel MINDKÉT oldal ugyanazt a <c>DeterministicMath</c> Exp/Cos/Asin-t
/// implementálja, a tűrés szoros (1e-12) — nem a laza 1e-6, mint a régi,
/// nyers <c>Math.Exp</c>-es úton.
/// </summary>
public class DeepTimeReliefErosionVectorFileTests
{
    private const double Tol = 1e-12;

    private static JsonDocument LoadVectors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "erosion_glaciation_deep_time_vectors.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void ReliefDecayMatchesPythonReference()
    {
        using JsonDocument doc = LoadVectors();
        JsonElement root = doc.RootElement;
        int level = root.GetProperty("level").GetInt32();

        // A modell-konstansoknak is egyezniük kell — egy elcsúszott konstans
        // különben csak a vektorok zaján keresztül, félreérthetően látszana.
        void SameConstant(string jsonKey, double csharpValue, double tolerance = 0.0)
        {
            double referenceValue = root.GetProperty(jsonKey).GetDouble();
            Assert.True(Math.Abs(referenceValue - csharpValue) <= tolerance,
                $"{jsonKey}: referencia {referenceValue} vs C# {csharpValue}");
        }

        SameConstant("erosionWaterFloor", DeepTimeErosionGlaciation.ErosionWaterFloor);
        SameConstant("erosionEquatorWeight", DeepTimeErosionGlaciation.ErosionEquatorWeight);
        SameConstant("erosionMidLatWeight", DeepTimeErosionGlaciation.ErosionMidLatWeight);
        SameConstant("erosionMidLatCenterRad", DeepTimeErosionGlaciation.ErosionMidLatCenterRad, 1e-15);
        SameConstant("erosionMidLatWidthRad", DeepTimeErosionGlaciation.ErosionMidLatWidthRad, 1e-15);
        SameConstant("glacialErosivity", DeepTimeErosionGlaciation.GlacialErosivity);
        SameConstant("primaryReliefTauMyr", DeepTimeErosionGlaciation.PrimaryReliefTauMyr);
        SameConstant("primaryReliefEqFraction", DeepTimeErosionGlaciation.PrimaryReliefEqFraction);
        SameConstant("secondaryReliefTauMyr", DeepTimeErosionGlaciation.SecondaryReliefTauMyr, 1e-9);
        SameConstant("secondaryReliefEqFraction", DeepTimeErosionGlaciation.SecondaryReliefEqFraction);
        SameConstant("fluvialDissectionGain", DeepTimeErosionGlaciation.FluvialDissectionGain);
        SameConstant("fluvialDissectionTauMyr", DeepTimeErosionGlaciation.FluvialDissectionTauMyr);
        SameConstant("coastalBandMeters", DeepTimeErosionGlaciation.CoastalBandMeters);
        SameConstant("coastalPlaningFraction", DeepTimeErosionGlaciation.CoastalPlaningFraction);
        SameConstant("coastalAbrasionTauMyr", DeepTimeErosionGlaciation.CoastalAbrasionTauMyr);

        int n = 0;
        foreach (JsonElement v in root.GetProperty("reliefErosionVectors").EnumerateArray())
        {
            TileId id = TileId.FromFaceLevelUV(v.GetProperty("face").GetInt32(), level,
                v.GetProperty("u").GetUInt32(), v.GetProperty("v").GetUInt32());
            TileGeometry.ToPosition(id, out _, out _, out double z);
            double erosionT = v.GetProperty("erosionTimeMyr").GetDouble();

            double lat = DeepTimeErosionGlaciation.AbsLatitudeRad(z);
            Assert.True(Math.Abs(lat - v.GetProperty("absLatitudeRad").GetDouble()) < Tol, $"lat: {lat}");
            Assert.True(Math.Abs(DeepTimeErosionGlaciation.ZonalWaterFactor(lat)
                - v.GetProperty("zonalWaterFactor").GetDouble()) < Tol);
            Assert.True(Math.Abs(DeepTimeErosionGlaciation.GlaciatedFraction(lat)
                - v.GetProperty("glaciatedFraction").GetDouble()) < Tol);
            Assert.True(Math.Abs(DeepTimeErosionGlaciation.EffectiveErosionTimeMyr(z, erosionT)
                - v.GetProperty("effectiveErosionTimeMyr").GetDouble()) < 1e-9);

            DeepTimeErosionGlaciation.ReliefDecayFactors(z, erosionT, out double dp, out double ds);
            Assert.True(Math.Abs(dp - v.GetProperty("primaryReliefDecay").GetDouble()) < Tol, $"D_p: {dp}");
            Assert.True(Math.Abs(ds - v.GetProperty("secondaryReliefDecay").GetDouble()) < Tol, $"D_s: {ds}");
            n++;
        }
        Assert.Equal(400, n);
    }

    [Fact]
    public void SplitPlateAndErosionTimeMatchesPythonReference()
    {
        using JsonDocument doc = LoadVectors();
        JsonElement root = doc.RootElement;
        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        int level = root.GetProperty("level").GetInt32();
        (double X, double Y, double Z)[] seeds0 = PlateGeneration.GenerateSeeds(worldSeed, plateCount);

        int n = 0;
        foreach (JsonElement v in root.GetProperty("splitTimeVectors").EnumerateArray())
        {
            TileId id = TileId.FromFaceLevelUV(v.GetProperty("face").GetInt32(), level,
                v.GetProperty("u").GetUInt32(), v.GetProperty("v").GetUInt32());
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            double plateT = v.GetProperty("plateTimeMyr").GetDouble();
            double erosionT = v.GetProperty("erosionTimeMyr").GetDouble();

            (double X, double Y, double Z)[] seeds = PlateMotion.MovedSeeds(worldSeed, seeds0, plateT);
            int plateId = PlateGeneration.AssignPlate(x, y, z, seeds);
            Assert.Equal(v.GetProperty("plateId").GetInt32(), plateId);

            double staticSea = v.GetProperty("hasStaticSeaLevel").GetBoolean()
                ? v.GetProperty("staticSeaLevelMeters").GetDouble()
                : double.NaN;
            double elev = DeepTimeErosionGlaciation.ElevationAtTime(
                worldSeed, plateId, x, y, z, seeds, plateT, erosionT, out bool oceanic,
                DeepTimeErosionGlaciation.OrogenicRelaxationTauMyr,
                DeepTimeErosionGlaciation.EquilibriumFraction, staticSea);
            Assert.True(Math.Abs(elev - v.GetProperty("elevation").GetDouble()) < 1e-9, $"elev: {elev}");
            Assert.Equal(v.GetProperty("isOceanic").GetBoolean(), oceanic);
            n++;
        }
        Assert.Equal(120, n);
    }
}

public class DeepTimeReliefErosionPropertyTests
{
    private const ulong Seed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;

    private static (double X, double Y, double Z)[] Seeds() =>
        PlateGeneration.GenerateSeeds(Seed, PlateCount);

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
    /// A t = 0 világ BITRE változatlan: az erózió nulla effektív idővel
    /// pontosan 1,0 csillapítót ad, az pedig rövidzár az eredeti kifejezésre.
    /// Ez a legfontosabb regresszió — ezen múlik, hogy a már vizuálisan
    /// jóváhagyott statikus világ nem mozdul el.
    /// </summary>
    [Fact]
    public void ZeroErosionTimeIsBitIdenticalToTheStaticPath()
    {
        var seeds = Seeds();
        foreach (TileId id in SampleTiles())
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            DeepTimeErosionGlaciation.ReliefDecayFactors(z, 0.0, out double dp, out double ds);
            Assert.Equal(1.0, dp);
            Assert.Equal(1.0, ds);

            int plateId = PlateGeneration.AssignPlate(x, y, z, seeds);
            double withoutErosion = PlateBoundaryEffect.ElevationWithBoundary(
                Seed, plateId, id.Value, x, y, z, seeds, out bool oc0);
            double withZeroErosion = DeepTimeErosionGlaciation.ElevationAtTime(
                Seed, plateId, x, y, z, seeds, 0.0, 0.0, out bool oc1);
            Assert.Equal(oc0, oc1);
            // A maradék kizárólag az uplift-relaxáció `hEq + (h0 - hEq)`
            // átrendezéséből jön (ND-44 óta így van), NEM az új eróziós ágból.
            Assert.True(Math.Abs(withoutErosion - withZeroErosion) < 1e-9);
        }
    }

    /// <summary>Tisztaság: ismételt hívás bitre ugyanaz (nincs rejtett állapot, I2).</summary>
    [Fact]
    public void ReliefDecayIsPure()
    {
        foreach (TileId id in SampleTiles(13))
        {
            TileGeometry.ToPosition(id, out _, out _, out double z);
            foreach (double t in new[] { 0.0, 7.5, 250.0, 1000.0, 5000.0 })
            {
                DeepTimeErosionGlaciation.ReliefDecayFactors(z, t, out double a1, out double b1);
                DeepTimeErosionGlaciation.ReliefDecayFactors(z, t, out double a2, out double b2);
                Assert.Equal(a1, a2);
                Assert.Equal(b1, b2);
            }
        }
    }

    /// <summary>Párhuzamos == szekvenciális (sorrendfüggetlenség, I1).</summary>
    [Fact]
    public void ParallelMatchesSequential()
    {
        var seeds = PlateMotion.MovedSeeds(Seed, Seeds(), 600.0);
        List<TileId> tiles = SampleTiles(5);
        var sequential = new double[tiles.Count];
        for (int i = 0; i < tiles.Count; i++)
        {
            TileGeometry.ToPosition(tiles[i], out double x, out double y, out double z);
            int pid = PlateGeneration.AssignPlate(x, y, z, seeds);
            sequential[i] = DeepTimeErosionGlaciation.ElevationAtTime(
                Seed, pid, x, y, z, seeds, 600.0, 600.0, out _);
        }

        var parallel = new double[tiles.Count];
        Parallel.For(0, tiles.Count, i =>
        {
            TileGeometry.ToPosition(tiles[i], out double x, out double y, out double z);
            int pid = PlateGeneration.AssignPlate(x, y, z, seeds);
            parallel[i] = DeepTimeErosionGlaciation.ElevationAtTime(
                Seed, pid, x, y, z, seeds, 600.0, 600.0, out _);
        });

        for (int i = 0; i < tiles.Count; i++)
            Assert.Equal(sequential[i], parallel[i]);
    }

    /// <summary>
    /// Timestep-invariancia (ND-04): a csillapítás akárhány részidőközre
    /// láncolva ugyanazt adja, mint a közvetlen lekérdezés. Ez a garancia,
    /// hogy a csúszka 0 → 500 → 1000 Myr útja és a közvetlen 1000 Myr
    /// UGYANAZT a világot adja.
    /// </summary>
    [Fact]
    public void ReliefDecayIsTimestepInvariant()
    {
        TileGeometry.ToPosition(TileId.FromFaceLevelUV(2, Level, 17, 41), out _, out _, out double z);
        const double total = 640.0;
        DeepTimeErosionGlaciation.ReliefDecayFactors(z, total, out double dp, out double ds);
        // ND-137 2. kör: a D_primary MOST két önálló exponenciális tényező
        // SZORZATA (diffúziós csillapítás × folyóvízi bevágódás), ezért a
        // láncolás KÉT állapotváltozót tart — ugyanúgy, ahogy az elsődleges és
        // a másodlagos relief-tag is külön láncolódik. Mindkettő önállóan
        // félcsoport, tehát a szorzat láncolása is egzakt.
        double chainLat = DeepTimeErosionGlaciation.AbsLatitudeRad(z);
        foreach (int steps in new[] { 1, 2, 3, 7, 29, 113, 1000 })
        {
            double chainedP = DeepTimeErosionGlaciation.ChainReliefDecay(
                                  z, total, steps, DeepTimeErosionGlaciation.PrimaryReliefTauMyr,
                                  DeepTimeErosionGlaciation.PrimaryReliefEqFraction)
                              * DeepTimeErosionGlaciation.ChainFluvialDissection(chainLat, total, steps);
            double chainedS = DeepTimeErosionGlaciation.ChainReliefDecay(
                z, total, steps, DeepTimeErosionGlaciation.SecondaryReliefTauMyr,
                DeepTimeErosionGlaciation.SecondaryReliefEqFraction);
            Assert.True(Math.Abs(chainedP - dp) < 1e-12, $"steps={steps}: {chainedP} vs {dp}");
            Assert.True(Math.Abs(chainedS - ds) < 1e-12, $"steps={steps}: {chainedS} vs {ds}");
        }
    }

    /// <summary>
    /// A zonális profil három-cellás: nedves egyenlítő, szubtrópusi
    /// sivatagöv, nedves viharpálya, száraz pólus.
    /// </summary>
    [Fact]
    public void ZonalWaterFactorHasThreeCellShape()
    {
        double Deg(double d) => DeepTimeErosionGlaciation.ZonalWaterFactor(d * Math.PI / 180.0);
        Assert.True(Deg(0) > Deg(28));
        Assert.True(Deg(50) > Deg(28));
        Assert.True(Deg(50) > Deg(90));
        foreach (double d in new[] { 0.0, 15.0, 28.0, 45.0, 60.0, 90.0 })
            Assert.True(Deg(d) >= DeepTimeErosionGlaciation.ErosionWaterFloor);
    }

    /// <summary>
    /// A jeges időhányad zárt alakja MEGEGYEZIK a numerikus időintegrállal
    /// (ez a modul saját ismert-válasz tesztje — az <see cref="DeepTimeErosionGlaciation.IsIced"/>
    /// mintavételezése a független referencia).
    /// </summary>
    [Fact]
    public void GlaciatedFractionMatchesNumericalIntegral()
    {
        const int samples = 200_000;
        const int periods = 40;
        double total = periods * DeepTimeErosionGlaciation.GlaciationPeriodMyr;
        foreach (double deg in new[] { 20.0, 32.0, 35.0, 40.0, 45.0, 48.0, 55.0, 80.0 })
        {
            double lat = deg * Math.PI / 180.0;
            int hits = 0;
            for (int i = 0; i < samples; i++)
            {
                double t = (i + 0.5) * total / samples;
                if (DeepTimeErosionGlaciation.IsIced(lat, t)) hits++;
            }
            double numeric = (double)hits / samples;
            double closed = DeepTimeErosionGlaciation.GlaciatedFraction(lat);
            // A tűrést a MINTAVÉTELEZÉS korlátozza (~80 jéghatár-keresztezés
            // 200k mintán), nem a zárt alak.
            Assert.True(Math.Abs(numeric - closed) < 1e-3, $"{deg} fok: {closed} vs {numeric}");
        }
    }

    /// <summary>
    /// Differenciált erózió: a nedves öv és a jégtakaró jobban kopik, mint a
    /// szubtrópusi sivatag — és a regionális (hosszú hullámhosszú) relief
    /// lassabban, mint a rövid hullámhosszú.
    /// </summary>
    [Fact]
    public void ErosionIsDifferentiatedByLatitudeAndWavelength()
    {
        double Z(double deg) => Math.Sin(deg * Math.PI / 180.0);
        const double t = 200.0;
        DeepTimeErosionGlaciation.ReliefDecayFactors(Z(0), t, out double wetP, out double wetS);
        DeepTimeErosionGlaciation.ReliefDecayFactors(Z(28), t, out double dryP, out _);
        DeepTimeErosionGlaciation.ReliefDecayFactors(Z(80), t, out double polarP, out _);

        Assert.True(wetP < dryP, $"nedves {wetP} vs száraz {dryP}");
        Assert.True(polarP < dryP, $"poláris {polarP} vs száraz {dryP}");
        Assert.True(wetS > wetP, $"regionális {wetS} vs rövid hullámhossz {wetP}");
    }

    /// <summary>
    /// A csúcs lekopik, a medence FELTÖLTŐDIK: a relief-tag a saját
    /// gömbfelszíni átlaga felé mozog, és nem lő túl rajta. Ez a modell
    /// „lerakódás" tagja — tömeg-ÁTRENDEZÉS, nem egyirányú lehúzás.
    /// </summary>
    [Fact]
    public void ErosionMovesReliefTowardItsMeanInBothDirections()
    {
        const double mean = CrustElevation.PrimaryReliefSphericalMean;
        foreach (double decay in new[] { 0.9, 0.5, 0.31, 0.3 })
        {
            double above = CrustElevation.ErodedReliefTerm(mean + 0.4, mean, decay);
            double below = CrustElevation.ErodedReliefTerm(mean - 0.4, mean, decay);
            Assert.True(above < mean + 0.4 && above > mean, "A csúcsnak LE kell jönnie, de nem az átlag alá");
            Assert.True(below > mean - 0.4 && below < mean, "A medencének FEL kell jönnie, de nem az átlag fölé");
        }
        Assert.Equal(mean + 0.4, CrustElevation.ErodedReliefTerm(mean + 0.4, mean, 1.0));
    }

    /// <summary>
    /// Minden paraméter érdemben hat, és az erózió LÁTHATÓ a domborzaton
    /// (todo2 A20 elvárása: „a deep-time csúszka legyen érezhető a
    /// domborzaton is"). A lemez-idő fixen 0, hogy a különbség tisztán az
    /// erózió műve legyen.
    /// </summary>
    [Fact]
    public void ErosionVisiblyChangesTheTerrainAndGrowsWithTime()
    {
        var seeds = Seeds();
        List<TileId> tiles = SampleTiles();
        var means = new List<double>();
        foreach (double t in new[] { 100.0, 250.0, 500.0, 1000.0, 2000.0 })
        {
            var landDiffs = new List<double>();
            foreach (TileId id in tiles)
            {
                TileGeometry.ToPosition(id, out double x, out double y, out double z);
                int pid = PlateGeneration.AssignPlate(x, y, z, seeds);
                double noErosion = DeepTimeErosionGlaciation.ElevationAtTime(
                    Seed, pid, x, y, z, seeds, 0.0, 0.0, out bool oceanic);
                double eroded = DeepTimeErosionGlaciation.ElevationAtTime(
                    Seed, pid, x, y, z, seeds, 0.0, t, out _);
                if (!oceanic) landDiffs.Add(Math.Abs(eroded - noErosion));
            }
            means.Add(landDiffs.Average());
        }

        for (int i = 1; i < means.Count; i++)
            Assert.True(means[i] > means[i - 1], $"a hatásnak monoton nőnie kell: {means[i - 1]} -> {means[i]}");
        // A 2. kör (folyóvízi bevágódás) SZÁNDÉKOSAN visszavesz a telítési
        // elmozdulásból: a bevágódás megőrzi a relief egy részét (az egyensúlyi
        // D_primary 0,30 helyett 0,30·(1+gain)), tehát a „mennyire más" metrika
        // kisebb, mint az 1. körben — közben a domborzat VÁLTOZATOSABB lett.
        Assert.True(means[^1] > 150.0, $"a kontinentális átlagos elmozdulás túl kicsi: {means[^1]} m");
    }

    /// <summary>
    /// Élesetek: 0, nagyon nagy idő, pólus és egyenlítő, tartományon kívüli
    /// z. A csillapító mindig a [eq, 1] intervallumban marad, sosem NaN.
    /// </summary>
    [Fact]
    public void EdgeCasesStayInRange()
    {
        foreach (double z in new[] { -1.5, -1.0, -0.5, 0.0, 0.5, 1.0, 1.5 })
        {
            foreach (double t in new[] { 0.0, 1e-9, 1.0, 1e5, 1e9 })
            {
                DeepTimeErosionGlaciation.ReliefDecayFactors(z, t, out double dp, out double ds);
                Assert.False(double.IsNaN(dp) || double.IsNaN(ds));
                // ND-137 2. kör: a D_primary a bevágódás miatt 1,0 FÖLÉ is mehet,
                // legfeljebb (1 + gain)-szeresére; alulról az egyensúlyi hányad köti.
                Assert.InRange(dp, DeepTimeErosionGlaciation.PrimaryReliefEqFraction,
                    1.0 + DeepTimeErosionGlaciation.FluvialDissectionGain);
                Assert.InRange(ds, DeepTimeErosionGlaciation.SecondaryReliefEqFraction, 1.0);
            }
        }
        // Negatív erózió-idő nincs a modellben (a csúszka 0-tól indul); ha
        // mégis érkezne, a formula matematikailag felerősít — de véges,
        // nem NaN/végtelen értéket kell adnia (a kitevő-korlát miatt).
        // ND-137 2. kör: a negatív idő „nulla előtt nincs erózió"-ként
        // értelmeződik (identitás). Korábban matematikailag FELERŐSÍTETT, és a
        // két felerősítő tényező szorzata ±végtelenbe csordult — ami csendben
        // megmérgezte volna az egész elevációmezőt.
        foreach (double t in new[] { -1e-9, -1.0, -100.0, -1e9 })
        {
            DeepTimeErosionGlaciation.ReliefDecayFactors(0.0, t, out double negP, out double negS);
            Assert.Equal(1.0, negP);
            Assert.Equal(1.0, negS);
        }
    }

    /// <summary>
    /// Eloszlás-plauzibilitás: telítésben a relief SZÓRÁSA érdemben csökken
    /// (simulás), a globális ÁTLAG viszont gyakorlatilag változatlan marad
    /// (implicit izosztatikus kompenzáció — különben az ND-38
    /// térfogat-megmaradó tengerszint elárasztaná a világot).
    /// </summary>
    [Fact]
    public void ReliefSmoothsWhileGlobalMeanIsPreserved()
    {
        var seeds = Seeds();
        List<TileId> tiles = SampleTiles(5);
        var before = new List<double>();
        var after = new List<double>();
        foreach (TileId id in tiles)
        {
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            int pid = PlateGeneration.AssignPlate(x, y, z, seeds);
            before.Add(DeepTimeErosionGlaciation.ElevationAtTime(Seed, pid, x, y, z, seeds, 0.0, 0.0, out _));
            after.Add(DeepTimeErosionGlaciation.ElevationAtTime(Seed, pid, x, y, z, seeds, 0.0, 3000.0, out _));
        }

        double Std(List<double> xs)
        {
            double m = xs.Average();
            return Math.Sqrt(xs.Sum(v => (v - m) * (v - m)) / xs.Count);
        }

        Assert.True(Std(after) < Std(before), $"a reliefnek simulnia kell: {Std(before)} -> {Std(after)}");
        Assert.True(Math.Abs(after.Average() - before.Average()) < 80.0,
            $"a globális átlag nem tolódhat el érdemben: {before.Average()} -> {after.Average()}");
    }
}
