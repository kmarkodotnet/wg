using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// ND-120: a `TileClassification.compute` GPU-shader `BaseElevationF`-je két
/// Core-újítást NEM tartalmaz — az ND-52 MÁSODLAGOS ZAJT és az ND-90
/// lemezhatár-KEVERÉST. A shader maga nem hívható C#-ból, ezért itt azt
/// mérjük, MEKKORA következménye van ennek a két hiánynak: a Core-utat
/// futtatjuk le úgy, hogy egyszer minden benne van, egyszer pedig a hiányzó
/// tagokat kivesszük — a két eredményt ugyanazzal a tengerszinttel
/// osztályozzuk (a shader is konstansként kapja meg a CPU-tól).
///
/// MIÉRT TESZT ÉS NEM EGYSZERI MÉRÉS. A számok döntés-minőségűek (ld. ND-120:
/// a shader javítása vs. a GPU-út törlése). Ha valaki később azt gondolná,
/// hogy „ez a tag elhanyagolható, a GPU-n kihagyható", ez a teszt megmondja,
/// hogy nem az. A küszöbök szándékosan LAZÁK (nagyságrendet rögzítenek, nem
/// pontos darabszámot), hogy a jövőbeli hangolások ne törjék el őket.
///
/// A HATÓKÖR PONTOSAN: alap-domborzat t=0-ban, kráterek és deep-time erózió
/// NÉLKÜL, MINDKÉT oldalon float64-gyel. Tehát NEM tartalmazza a shader
/// float32-es pontosságvesztését és semmilyen egyéb shader-elcsúszást — a
/// mért érték ALSÓ KORLÁT a tényleges GPU/CPU eltérésre.
/// </summary>
public class GpuShaderElevationParityTests
{
    private const ulong Seed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWater = 0.65;
    private static readonly double AxialTilt = 23.44 * Math.PI / 180.0;

    private enum Variant
    {
        /// <summary>A Core teljes, aktuális útja.</summary>
        Truth,
        /// <summary>ND-52 másodlagos zaj nélkül (ez hiányzik a shaderből).</summary>
        NoSecondaryNoise,
        /// <summary>ND-90 lemezhatár-keverés nélkül (ez is hiányzik).</summary>
        NoBoundaryBlend,
        /// <summary>A shader TÉNYLEGES állapota: egyik sincs benne.</summary>
        ShaderLike,
    }

    private static double Elevation(
        Variant variant, (double X, double Y, double Z)[] seeds,
        double x, double y, double z, out bool isOceanic)
    {
        DomainWarp.WarpPosition(Seed, x, y, z, out double wx, out double wy, out double wz);
        PlateBoundaryEffect.TwoBestDots(
            wx, wy, wz, seeds, out double best, out double second, out int bestIndex, out int secondIndex);
        CrustElevation.ComputeNoiseBasis(
            Seed, x, y, z, out double primary, out double mask, out double secondary);

        double usedSecondary =
            variant == Variant.NoSecondaryNoise || variant == Variant.ShaderLike ? 0.0 : secondary;
        bool blend = variant == Variant.Truth || variant == Variant.NoSecondaryNoise;

        double baseElevation = blend
            ? CrustElevation.BlendedBaseElevationFromNoiseBasis(
                Seed, best, second, bestIndex, secondIndex, primary, mask, usedSecondary, out isOceanic)
            : CrustElevation.BaseElevationFromNoiseBasis(
                Seed, bestIndex, primary, mask, usedSecondary, out isOceanic);

        return baseElevation + PlateBoundaryEffect.BoundaryUpliftFromNearestPlates(
            Seed, best, second, bestIndex, secondIndex, mask);
    }

    private sealed class Divergence
    {
        public double MeanAbsMeters;
        public double MaxAbsMeters;
        public double OceanFlipFraction;
        public double BiomeFlipFraction;
    }

    private static Divergence Measure(Variant variant)
    {
        var seeds = PlateGeneration.GenerateSeeds(Seed, PlateCount);
        uint n = 1u << Level;
        int tileCount = checked((int)(6L * n * n));
        var ids = new TileId[tileCount];
        int idx = 0;
        for (int face = 0; face <= 5; face++)
            for (uint u = 0; u < n; u++)
                for (uint v = 0; v < n; v++)
                    ids[idx++] = TileId.FromFaceLevelUV(face, Level, u, v);

        var truth = new double[tileCount];
        Parallel.For(0, tileCount, i =>
        {
            TileGeometry.ToPosition(ids[i], out double x, out double y, out double z);
            truth[i] = Elevation(Variant.Truth, seeds, x, y, z, out _);
        });

        // A tengerszintet a CPU Core kalibrálja, és a shader KONSTANSKÉNT kapja
        // meg - tehát mindkét oldal ugyanazt használja.
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(new List<double>(truth), TargetWater);

        var absDiff = new double[tileCount];
        var oceanFlip = new int[tileCount];
        var biomeFlip = new int[tileCount];
        Parallel.For(0, tileCount, i =>
        {
            TileGeometry.ToPosition(ids[i], out double x, out double y, out double z);
            double e = Elevation(variant, seeds, x, y, z, out _);
            absDiff[i] = Math.Abs(e - truth[i]);

            bool oceanTruth = truth[i] < seaLevel;
            bool oceanVariant = e < seaLevel;
            oceanFlip[i] = oceanTruth != oceanVariant ? 1 : 0;

            Biome bT = BiomeClassification.Classify(
                Temperature.TemperatureKelvin(x, y, z, 0.0, 365.25, 1.0, AxialTilt, oceanTruth, truth[i], seaLevel),
                oceanTruth);
            Biome bV = BiomeClassification.Classify(
                Temperature.TemperatureKelvin(x, y, z, 0.0, 365.25, 1.0, AxialTilt, oceanVariant, e, seaLevel),
                oceanVariant);
            biomeFlip[i] = bT != bV ? 1 : 0;
        });

        var result = new Divergence();
        double sum = 0;
        int oceanFlips = 0, biomeFlips = 0;
        for (int i = 0; i < tileCount; i++)
        {
            sum += absDiff[i];
            if (absDiff[i] > result.MaxAbsMeters) result.MaxAbsMeters = absDiff[i];
            oceanFlips += oceanFlip[i];
            biomeFlips += biomeFlip[i];
        }
        result.MeanAbsMeters = sum / tileCount;
        result.OceanFlipFraction = (double)oceanFlips / tileCount;
        result.BiomeFlipFraction = (double)biomeFlips / tileCount;
        return result;
    }

    /// <summary>
    /// A shader tényleges állapota. MÉRVE 2026-09-20-án (seed 0xA7C944210000,
    /// 20 lemez, level 6, 24 576 tile): |delta| átlag 300,8 m, max 3116 m,
    /// óceán/szárazföld átfordulás 21,99%, biome-átfordulás 23,60%. Level 8-on
    /// (393 216 tile) gyakorlatilag ugyanez: 22,13% / 23,78% - az arány
    /// skála-stabil.
    /// </summary>
    [Fact]
    public void ShaderOmissionsWouldReclassifyAboutAQuarterOfThePlanet()
    {
        Divergence d = Measure(Variant.ShaderLike);

        Assert.InRange(d.MeanAbsMeters, 200.0, 400.0);
        Assert.InRange(d.OceanFlipFraction, 0.15, 0.30);
        Assert.InRange(d.BiomeFlipFraction, 0.15, 0.35);
    }

    /// <summary>
    /// A két hiányzó tag NEM egyforma súlyú, és ezt érdemes külön rögzíteni:
    /// a másodlagos zaj GLOBÁLIS (minden tile-t elmozdít), a határkeverés
    /// LOKÁLIS (csak lemezhatárok mentén hat, ott viszont nagyot). Aki a
    /// shadert javítani akarja, ebből tudja, melyik tagot kell előbb.
    /// </summary>
    [Fact]
    public void SecondaryNoiseDominatesWhileBoundaryBlendIsLocalButLarge()
    {
        Divergence secondary = Measure(Variant.NoSecondaryNoise);
        Divergence blend = Measure(Variant.NoBoundaryBlend);

        // A masodlagos zaj: mindenhol hat, ezert sok atfordulas, de korlatos elteres.
        Assert.True(secondary.OceanFlipFraction > 0.15,
            $"A masodlagos zaj kihagyasa csak {secondary.OceanFlipFraction:P2} atfordulast adott.");
        Assert.InRange(secondary.MaxAbsMeters, 500.0, 1200.0);

        // A hatarkeveres: kevés tile, de a maximum NAGYOBB, mint a masodlagos zaje.
        Assert.True(blend.OceanFlipFraction < 0.02,
            $"A hatarkeveres kihagyasa {blend.OceanFlipFraction:P2} atfordulast adott - ez mar nem lokalis.");
        Assert.True(blend.MaxAbsMeters > secondary.MaxAbsMeters,
            "A hatarkeveres maximalis elterese nem haladja meg a masodlagos zajet.");
    }

    /// <summary>
    /// Épelméjűségi horgony: a teljes Core-út önmagával összevetve nulla
    /// eltérést ad. Enélkül a fenti két teszt akkor is zöld maradhatna, ha a
    /// mérő-keret elromlana (pl. mindig ugyanazt a változatot számolná).
    /// </summary>
    [Fact]
    public void TruthAgainstItselfIsExactlyZero()
    {
        Divergence d = Measure(Variant.Truth);

        Assert.Equal(0.0, d.MeanAbsMeters);
        Assert.Equal(0.0, d.MaxAbsMeters);
        Assert.Equal(0.0, d.OceanFlipFraction);
        Assert.Equal(0.0, d.BiomeFlipFraction);
    }
}
