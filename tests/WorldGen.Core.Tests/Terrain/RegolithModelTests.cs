using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Terrain;

/// <summary>
/// Talaj/regolit MVP (ND-117) - a Python referencia (regolith_ref.py) 500
/// tiszta-fuggveny + 300 teljes-racs vektora.
///
/// A <see cref="RegolithModel.ComputeProfile"/> SAJAT lanca KIZAROLAG
/// +-*//abs/min/max muveleteket hasznal (nincs Sin/Cos/Exp/Log/Pow a
/// kritikus uton), ezert - a Temperature/WindPrecipitation/DeepTimeErosion
/// mintajatol elteroen - BITPONTOSAN, tolerancia nelkul mérünk (ld. lent,
/// RegolithModelUnitVectorFileTests, 500/500 egzakt egyezes). A teljes-racs
/// driver (<see cref="RegolithModel.ComputeField"/>) viszont MAR MEGLEVO,
/// nem-bitpontos upstream lancokra epul (LakesIceErosion homerseklet/erozio,
/// MoisturePrecipitation szel) - ott 1e-6 tolerancia, ugyanaz a konvencio,
/// mint a LakesIceErosionVectorFileTests/MoisturePrecipitationVectorFileTests-ben.
/// </summary>
public class RegolithModelUnitVectorFileTests
{
    [Fact]
    public void ComputeProfileMatchesPythonReferenceExactly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "regolith_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int n = 0;
        foreach (JsonElement v in root.GetProperty("unitVectors").EnumerateArray())
        {
            bool isOcean = v.GetProperty("isOcean").GetBoolean();
            double slopeNorm = v.GetProperty("slopeNorm").GetDouble();
            double erosionDepthM = v.GetProperty("erosionDepthM").GetDouble();
            double depositionGainM = v.GetProperty("depositionGainM").GetDouble();
            double meanT = v.GetProperty("annualMeanTemperatureK").GetDouble();
            double precip = v.GetProperty("precipitation").GetDouble();

            RegolithProfile p = RegolithModel.ComputeProfile(isOcean, slopeNorm, erosionDepthM, depositionGainM, meanT, precip);

            Assert.Equal(v.GetProperty("depthM").GetDouble(), p.DepthMeters);
            Assert.Equal(v.GetProperty("porosity").GetDouble(), p.Porosity);
            Assert.Equal(v.GetProperty("waterRetention").GetDouble(), p.WaterRetention);
            n++;
        }
        Assert.Equal(500, n);
    }
}

public class RegolithModelFieldVectorFileTests
{
    // A RegolithModel SAJAT lancanak (ComputeProfile, ComputeSlopeNorm) minden
    // muvelete + - * / abs min max, ezert BITPONTOSAN egyezik a Pythonnal -
    // ezt a RegolithModelUnitVectorFileTests fent 500/500 bitpontosan, azaz
    // tolerancia NELKULI Assert.Equal-lal igazolja. A teljes-racs driver
    // viszont MAR MEGLEVO, ettol a modultol fuggetlen upstream lancokra epul,
    // amik NEM igernek bitpontossagot a CLAUDE.md tablazata szerint, es a
    // sajat KAT-tesztjuk is toleranciaval mer:
    //   - LakesIceErosion.AnnualTemperatureStats: nyers Math.Sin/Cos/Exp a
    //     Temperature-lancban (ld. LakesIceErosionVectorFileTests, Tol=1e-6);
    //   - LakesIceErosion.ApplyStaticErosionPass: Math.Pow az eroziós
    //     melysegben (ugyanaz a teszt, ugyanaz a Tol);
    //   - MoisturePrecipitation.Compute: nyers Math.Sin/Cos a szel-lancban
    //     (ld. MoisturePrecipitationVectorFileTests, 1e-6).
    // Emiatt a VEGPONTTOL-VEGPONTIG integracios teszt ugyanazt az 1e-6
    // tolerancia-konvenciot koveti, mint ez a ket mar meglevo teszt - ez NEM
    // a RegolithModel sajat lancanak pontatlansaga, hanem az orokolt upstream
    // pontatlansag athuzodasa a Depth/Porosity/WaterRetention-be (a Depth az
    // Erosion/DepositionGain-en at, a Porosity a homersekleten at, a
    // WaterRetention a csapadekon at). Csak az isOcean (diszkret) marad
    // egzakt.
    //
    // A TENYLEGES ELTERES MERVE (2026-09-19, a toleranciat 1e-300-ra allitva):
    // a legnagyobb elteres 3,410605e-13, az `annualMeanTemperatureK`-ban egy
    // ~261,53 K-es ertekre - vagyis nehany ULP, relativ ~1,3e-15. Tehat az
    // 1e-6-os konvencio itt ~7 nagysagrend tartalekkal all. Szandekosan NEM
    // szorítjuk szukebbre: ez a szam EGY platformon (Windows x64) mert, a CI
    // viszont Linux x64/ARM64-en es macOS-en is fut, ahol a Sin/Cos/Exp
    // implementacio elterhet. A meglevo LakesIceErosionTests ugyanezt az
    // 1e-6-ot hasznalja, es az mar atment mind a negy platformon.
    private const double Tol = 1e-6;

    [Fact]
    public void ComputeFieldMatchesPythonReferenceWithinEstablishedTolerance()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "regolith_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        ulong worldSeed = root.GetProperty("worldSeed").GetUInt64();
        int plateCount = root.GetProperty("plateCount").GetInt32();
        int level = root.GetProperty("level").GetInt32();
        double expectedSeaLevel = root.GetProperty("seaLevel").GetDouble();

        RegolithModel.RegolithField world = RegolithModel.ComputeField(worldSeed, plateCount, level);
        Assert.True(Math.Abs(expectedSeaLevel - world.SeaLevel) < Tol);

        int n = 0;
        double maxAbsDiff = 0.0;
        void Track(double expected, double actual, string label, int face, uint u, uint w)
        {
            double diff = Math.Abs(expected - actual);
            if (diff > maxAbsDiff) maxAbsDiff = diff;
            Assert.True(diff < Tol, $"{label} eltér tile ({face},{u},{w}): várt {expected}, kapott {actual}, diff {diff}");
        }

        foreach (JsonElement v in root.GetProperty("fieldVectors").EnumerateArray())
        {
            int face = v.GetProperty("face").GetInt32();
            uint u = v.GetProperty("u").GetUInt32();
            uint w = v.GetProperty("v").GetUInt32();
            TileId key = TileId.FromFaceLevelUV(face, level, u, w);

            bool isOcean = v.GetProperty("isOcean").GetBoolean();
            Assert.Equal(isOcean, world.IsOcean[key]);
            Track(v.GetProperty("rawElevation").GetDouble(), world.Elevation[key], "rawElevation", face, u, w);
            Track(v.GetProperty("erosionDepthM").GetDouble(), world.Erosion[key], "erosionDepthM", face, u, w);
            Track(v.GetProperty("depositionGainM").GetDouble(), world.DepositionGain[key], "depositionGainM", face, u, w);
            Track(v.GetProperty("precipitation").GetDouble(), world.Precipitation[key], "precipitation", face, u, w);
            Track(v.GetProperty("slopeNorm").GetDouble(), world.SlopeNorm[key], "slopeNorm", face, u, w);

            JsonElement meanTEl = v.GetProperty("annualMeanTemperatureK");
            if (meanTEl.ValueKind == JsonValueKind.Null)
                Assert.Null(world.MeanTemperatureK[key]);
            else
                Track(meanTEl.GetDouble(), world.MeanTemperatureK[key]!.Value, "annualMeanTemperatureK", face, u, w);

            Track(v.GetProperty("depthM").GetDouble(), world.DepthMeters[key], "depthM", face, u, w);
            Track(v.GetProperty("porosity").GetDouble(), world.Porosity[key], "porosity", face, u, w);
            Track(v.GetProperty("waterRetention").GetDouble(), world.WaterRetention[key], "waterRetention", face, u, w);
            n++;
        }
        Assert.Equal(300, n);
        Assert.True(maxAbsDiff < Tol, $"max abs diff = {maxAbsDiff}");
    }
}

public class RegolithModelPropertyTests
{
    [Fact]
    public void ComputeProfileIsPure()
    {
        RegolithProfile a = RegolithModel.ComputeProfile(false, 0.3, 12.0, 0.8, 280.0, 1.1);
        RegolithProfile b = RegolithModel.ComputeProfile(false, 0.3, 12.0, 0.8, 280.0, 1.1);
        Assert.Equal(a.DepthMeters, b.DepthMeters);
        Assert.Equal(a.Porosity, b.Porosity);
        Assert.Equal(a.WaterRetention, b.WaterRetention);
    }

    [Fact]
    public void ComputeFieldIsDeterministicAcrossRepeatedRuns()
    {
        RegolithModel.RegolithField a = RegolithModel.ComputeField(0xA7C944210000UL, 20, 4);
        RegolithModel.RegolithField b = RegolithModel.ComputeField(0xA7C944210000UL, 20, 4);
        foreach (TileId k in a.Elevation.Keys)
        {
            Assert.Equal(a.DepthMeters[k], b.DepthMeters[k]);
            Assert.Equal(a.Porosity[k], b.Porosity[k]);
            Assert.Equal(a.WaterRetention[k], b.WaterRetention[k]);
        }
    }

    [Fact]
    public void OceanTileAlwaysZeroProfileEvenWithExtremeInputs()
    {
        (bool, double, double, double, double, double)[] extremeCases =
        {
            (true, 0.0, 0.0, 0.0, 273.15, 0.0),
            (true, 1.0, 1e18, 1e18, 1e18, 1e18),
            (true, -5.0, -5.0, -5.0, -100.0, -5.0),
            (true, 0.0, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue),
        };
        foreach ((bool isOcean, double slope, double ero, double dep, double t, double precip) in extremeCases)
        {
            RegolithProfile p = RegolithModel.ComputeProfile(isOcean, slope, ero, dep, t, precip);
            Assert.Equal(0.0, p.DepthMeters);
            Assert.Equal(0.0, p.Porosity);
            Assert.Equal(0.0, p.WaterRetention);
        }
    }

    [Fact]
    public void SlopeNormStrictlyDecreasesDepth()
    {
        RegolithProfile flat = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, 288.0, 1.0);
        RegolithProfile mid = RegolithModel.ComputeProfile(false, 0.5, 0.0, 0.0, 288.0, 1.0);
        RegolithProfile steep = RegolithModel.ComputeProfile(false, 1.0, 0.0, 0.0, 288.0, 1.0);
        Assert.True(flat.DepthMeters > mid.DepthMeters);
        Assert.True(mid.DepthMeters > steep.DepthMeters);
        Assert.Equal(0.0, steep.DepthMeters);
        Assert.Equal(RegolithModel.DepthMaxM, flat.DepthMeters);
    }

    [Fact]
    public void DepositionAndErosionHaveOppositeEffectOnDepth()
    {
        RegolithProfile noDep = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, 288.0, 1.0);
        RegolithProfile highDep = RegolithModel.ComputeProfile(false, 0.0, 0.0, 3.0, 288.0, 1.0);
        RegolithProfile highEro = RegolithModel.ComputeProfile(false, 0.0, 250.0, 0.0, 288.0, 1.0);
        Assert.True(highDep.DepthMeters > noDep.DepthMeters);
        Assert.True(highEro.DepthMeters < noDep.DepthMeters);
        Assert.Equal(RegolithModel.DepthAbsoluteCapM, highDep.DepthMeters);
    }

    [Fact]
    public void PorosityFreezeThawTentPeaksAtFreezingPoint()
    {
        double freezing = LakesIceErosion.FreezingPointK;
        RegolithProfile atFreeze = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, freezing, 0.0);
        RegolithProfile nearCold = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, freezing - 5.0, 0.0);
        RegolithProfile farCold = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, freezing - 100.0, 0.0);
        RegolithProfile farWarm = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, freezing + 100.0, 0.0);

        Assert.Equal(RegolithModel.PorosityBase + RegolithModel.PorosityFreezeThawCoeff, atFreeze.Porosity);
        Assert.True(atFreeze.Porosity > nearCold.Porosity);
        Assert.True(nearCold.Porosity > farCold.Porosity);
        Assert.True(atFreeze.Porosity > farWarm.Porosity);
        Assert.Equal(RegolithModel.PorosityBase, farCold.Porosity);
        Assert.Equal(RegolithModel.PorosityBase, farWarm.Porosity);
    }

    [Fact]
    public void PorosityCompactionReducesPorosity()
    {
        RegolithProfile noDep = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, LakesIceErosion.FreezingPointK, 0.0);
        RegolithProfile fullDep = RegolithModel.ComputeProfile(false, 0.0, 0.0, RegolithModel.DepthMaxM, LakesIceErosion.FreezingPointK, 0.0);
        Assert.True(fullDep.Porosity < noDep.Porosity);
        Assert.True(Math.Abs((noDep.Porosity - fullDep.Porosity) - RegolithModel.PorosityCompactionCoeff) < 1e-12);
    }

    [Fact]
    public void PrecipitationSaturatesWaterRetentionAndIgnoresNegative()
    {
        RegolithProfile dry = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, 288.0, 0.0);
        RegolithProfile wet = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, 288.0, RegolithModel.PrecipReference);
        RegolithProfile extreme = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, 288.0, 1e18);
        RegolithProfile negative = RegolithModel.ComputeProfile(false, 0.0, 0.0, 0.0, 288.0, -5.0);

        Assert.True(wet.WaterRetention > dry.WaterRetention);
        Assert.Equal(wet.WaterRetention, extreme.WaterRetention);
        Assert.Equal(dry.WaterRetention, negative.WaterRetention);
        Assert.InRange(dry.WaterRetention, 0.0, 1.0);
        Assert.InRange(wet.WaterRetention, 0.0, 1.0);
    }

    [Fact]
    public void AllOutputsStayInDocumentedRangeWithExtremeInputs()
    {
        (bool, double, double, double, double, double)[] cases =
        {
            (false, 0.0, 0.0, 0.0, 0.0, 0.0),
            (false, 1.0, 1e18, 1e18, 1e18, 1e18),
            (false, 0.5, 1e18, 0.0, -1e18, 0.0),
            (false, 0.5, 0.0, 1e18, 1e18, -1e18),
            (false, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue),
            (false, -double.MaxValue, 0.0, 0.0, double.MaxValue, 0.0),
        };
        foreach ((bool isOcean, double slope, double ero, double dep, double t, double precip) in cases)
        {
            RegolithProfile p = RegolithModel.ComputeProfile(isOcean, slope, ero, dep, t, precip);
            Assert.InRange(p.DepthMeters, 0.0, RegolithModel.DepthAbsoluteCapM);
            Assert.InRange(p.Porosity, 0.0, 1.0);
            Assert.InRange(p.WaterRetention, 0.0, 1.0);
        }
    }

    [Fact]
    public void EveryParameterHasAnEffectOnAtLeastOneOutput()
    {
        RegolithProfile baseline = RegolithModel.ComputeProfile(false, 0.4, 10.0, 0.5, 285.0, 0.8);

        Assert.NotEqual(baseline.DepthMeters, RegolithModel.ComputeProfile(false, 0.9, 10.0, 0.5, 285.0, 0.8).DepthMeters);
        Assert.NotEqual(baseline.DepthMeters, RegolithModel.ComputeProfile(false, 0.4, 200.0, 0.5, 285.0, 0.8).DepthMeters);
        Assert.NotEqual(baseline.DepthMeters, RegolithModel.ComputeProfile(false, 0.4, 10.0, 3.0, 285.0, 0.8).DepthMeters);
        Assert.NotEqual(baseline.Porosity, RegolithModel.ComputeProfile(false, 0.4, 10.0, 0.5, 273.15, 0.8).Porosity);
        Assert.NotEqual(baseline.Porosity, RegolithModel.ComputeProfile(false, 0.4, 10.0, 2.0, 285.0, 0.8).Porosity);
        Assert.NotEqual(baseline.WaterRetention, RegolithModel.ComputeProfile(false, 0.4, 10.0, 0.5, 285.0, 5.0).WaterRetention);
        Assert.NotEqual(baseline.DepthMeters, RegolithModel.ComputeProfile(true, 0.4, 10.0, 0.5, 285.0, 0.8).DepthMeters);
    }

    [Fact]
    public void ParallelComputationMatchesSequential()
    {
        var inputs = new (bool isOcean, double slope, double ero, double dep, double t, double precip)[400];
        for (int i = 0; i < inputs.Length; i++)
        {
            double f = i / (double)inputs.Length;
            inputs[i] = (i % 7 == 0, f, f * 300.0, f * 4.0, 200.0 + f * 150.0, f * 8.0);
        }

        var sequential = new RegolithProfile[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            var (isOcean, slope, ero, dep, t, precip) = inputs[i];
            sequential[i] = RegolithModel.ComputeProfile(isOcean, slope, ero, dep, t, precip);
        }

        var parallel = new RegolithProfile[inputs.Length];
        Parallel.For(0, inputs.Length, i =>
        {
            var (isOcean, slope, ero, dep, t, precip) = inputs[i];
            parallel[i] = RegolithModel.ComputeProfile(isOcean, slope, ero, dep, t, precip);
        });

        for (int i = 0; i < inputs.Length; i++)
        {
            Assert.Equal(sequential[i].DepthMeters, parallel[i].DepthMeters);
            Assert.Equal(sequential[i].Porosity, parallel[i].Porosity);
            Assert.Equal(sequential[i].WaterRetention, parallel[i].WaterRetention);
        }
    }

    [Fact]
    public void ComputeFieldParallelTileLoopMatchesSequentialDriver()
    {
        // A teljes-racs driver maga szekvencialis (face/u/v sorrend nem
        // szamit, ld. ComputeSlopeNorm doksija) - itt azt igazoljuk, hogy a
        // per-tile ComputeProfile-hivas parhuzamositva (Parallel.ForEach a
        // mar kiszamolt bemenet-mezokon) BITRE ugyanazt adja, mint a driver
        // szekvencialis beagyazott ciklusa.
        RegolithModel.RegolithField world = RegolithModel.ComputeField(0xA7C944210000UL, 20, 3);
        var keys = new List<TileId>(world.Elevation.Keys);

        var parallelDepth = new Dictionary<TileId, double>();
        var parallelPorosity = new Dictionary<TileId, double>();
        var parallelRetention = new Dictionary<TileId, double>();
        var locks = new object();

        Parallel.ForEach(keys, k =>
        {
            RegolithProfile p = RegolithModel.ComputeProfile(
                world.IsOcean[k], world.SlopeNorm[k], world.Erosion[k], world.DepositionGain[k],
                world.MeanTemperatureK[k] ?? 0.0, world.Precipitation[k]);
            lock (locks)
            {
                parallelDepth[k] = p.DepthMeters;
                parallelPorosity[k] = p.Porosity;
                parallelRetention[k] = p.WaterRetention;
            }
        });

        foreach (TileId k in keys)
        {
            Assert.Equal(world.DepthMeters[k], parallelDepth[k]);
            Assert.Equal(world.Porosity[k], parallelPorosity[k]);
            Assert.Equal(world.WaterRetention[k], parallelRetention[k]);
        }
    }

    [Fact]
    public void ComputeSlopeNormEmptyLandFieldGivesAllZeros()
    {
        // Eles eset: minden tile ocean (ures szarazfold-tartomany) - a
        // max(..., 1e-9) korlat miatt nincs 0-osztas, minden slopeNorm 0.
        var field = new Dictionary<TileId, double>();
        var parent = new Dictionary<TileId, TileId?>();
        var isOcean = new Dictionary<TileId, bool>();
        for (int i = 0; i < 4; i++)
        {
            TileId t = TileId.FromFaceLevelUV(0, 1, (uint)(i / 2), (uint)(i % 2));
            field[t] = 0.0;
            parent[t] = null;
            isOcean[t] = true;
        }

        Dictionary<TileId, double> slopeNorm = RegolithModel.ComputeSlopeNorm(field, parent, isOcean);
        foreach (double v in slopeNorm.Values)
            Assert.Equal(0.0, v);
    }
}
