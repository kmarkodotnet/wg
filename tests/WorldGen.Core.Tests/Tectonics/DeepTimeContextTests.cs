using System;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// A22 — a deep-time kontextus-struct. A refaktor BIT-SEMLEGES, tehát a
/// lényegi tesztek nem új viselkedést írnak le, hanem azt kötik ki, hogy
/// a struct EGY-EGY tagja pontosan ugyanoda ér el, ahová a korábbi három
/// külön paraméter — és hogy a <c>default</c> érték NEM kapcsol be csendben
/// semmit (ez a struct-osítás egyetlen valódi hibalehetősége).
/// </summary>
public class DeepTimeContextTests
{
    private const ulong Seed = 0xA22D_EEF7_0C01_1234UL;
    private const int PlateCount = 20;
    private const int Level = 5;

    /// <summary>
    /// Éleset: a <c>default(DeepTimeContext)</c> tengerszintje NaN, nem 0,0.
    /// Nyers <c>double</c> mezővel a nullérték 0 m-es tengerszintet jelentene,
    /// ami CSENDBEN bekapcsolná a parti abráziót a 0 m-es szint körül.
    /// </summary>
    [Fact]
    public void DefaultContextDisablesCoastalAbrasion()
    {
        DeepTimeContext ctx = default;
        Assert.Equal(0.0, ctx.PlateTimeMyr);
        Assert.Equal(0.0, ctx.ErosionTimeMyr);
        Assert.True(double.IsNaN(ctx.StaticSeaLevelMeters));
        Assert.True(ctx.IsStatic);

        // A Static gyárimetódus ugyanez.
        Assert.True(double.IsNaN(DeepTimeContext.Static.StaticSeaLevelMeters));
        Assert.True(DeepTimeContext.Static.IsStatic);
    }

    [Fact]
    public void FactoriesSetExactlyTheIntendedFields()
    {
        DeepTimeContext plateOnly = DeepTimeContext.AtPlateTime(250.0);
        Assert.Equal(250.0, plateOnly.PlateTimeMyr);
        Assert.Equal(0.0, plateOnly.ErosionTimeMyr);
        Assert.True(double.IsNaN(plateOnly.StaticSeaLevelMeters));
        Assert.False(plateOnly.IsStatic);

        DeepTimeContext uniform = DeepTimeContext.Uniform(250.0);
        Assert.Equal(250.0, uniform.PlateTimeMyr);
        Assert.Equal(250.0, uniform.ErosionTimeMyr);
        Assert.True(double.IsNaN(uniform.StaticSeaLevelMeters));

        DeepTimeContext withSea = uniform.WithStaticSeaLevel(-137.5);
        Assert.Equal(250.0, withSea.PlateTimeMyr);
        Assert.Equal(250.0, withSea.ErosionTimeMyr);
        Assert.Equal(-137.5, withSea.StaticSeaLevelMeters);

        // WithErosionTime megtartja a tengerszintet, ha volt...
        DeepTimeContext reErodedWithSea = withSea.WithErosionTime(10.0);
        Assert.Equal(250.0, reErodedWithSea.PlateTimeMyr);
        Assert.Equal(10.0, reErodedWithSea.ErosionTimeMyr);
        Assert.Equal(-137.5, reErodedWithSea.StaticSeaLevelMeters);

        // ...és NEM talál ki 0,0-t, ha nem volt.
        DeepTimeContext reErodedNoSea = uniform.WithErosionTime(10.0);
        Assert.True(double.IsNaN(reErodedNoSea.StaticSeaLevelMeters));
    }

    /// <summary>
    /// Minden tag érdemben hat a kimenetre — külön-külön. A hármas eddig három
    /// paraméter volt; a struct akkor helyes, ha egyik tag sem "esik ki" a
    /// leképezésből (pl. mert a mezőt nem másoltuk át egy <c>With…</c>-ben).
    /// </summary>
    [Fact]
    public void EveryContextFieldAffectsTheElevation()
    {
        var seeds0 = PlateGeneration.GenerateSeeds(Seed, PlateCount);

        // Olyan pont, ami szárazföldi és a (t=0) tengerszint közelében van -
        // ott mindhárom tag hat: a lemez-keret, a kopás és az abrázió is.
        var field0 = SeaLevelCalibration.ComputeElevationField(Seed, PlateCount, Level);
        double seaLevel0 = SeaLevelCalibration.CalibrateSeaLevel(field0.Values, 0.65);

        TileId probe = default;
        bool found = false;
        foreach (var kv in field0)
        {
            double above = kv.Value - seaLevel0;
            if (above > 30.0 && above < 200.0) { probe = kv.Key; found = true; break; }
        }
        Assert.True(found, "Nem találtam parthoz közeli szárazföldi mintapontot.");
        TileGeometry.ToPosition(probe, out double x, out double y, out double z);

        double Eval(DeepTimeContext ctx)
        {
            var seeds = PlateMotion.MovedSeeds(Seed, seeds0, ctx.PlateTimeMyr);
            DomainWarp.WarpPosition(Seed, x, y, z, out double wx, out double wy, out double wz);
            int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
            return PlateBoundaryEffect.ElevationWithBoundaryFromWarpedAtTime(
                Seed, plateId, probe.Value, x, y, z, wx, wy, wz, seeds, ctx, out _);
        }

        double atStatic = Eval(DeepTimeContext.Static);

        // 1. PlateTimeMyr: a lemez-keretes zaj elmozdul.
        double plateOnly = Eval(DeepTimeContext.AtPlateTime(400.0));
        Assert.NotEqual(atStatic, plateOnly);

        // 2. ErosionTimeMyr: ugyanaz a lemez-idő, de kopással.
        double eroded = Eval(DeepTimeContext.AtPlateTime(400.0).WithErosionTime(400.0));
        Assert.NotEqual(plateOnly, eroded);

        // 3. StaticSeaLevelMeters: ugyanaz a két idő, de bekapcsolt abrázióval.
        double abraded = Eval(
            DeepTimeContext.Uniform(400.0).WithStaticSeaLevel(seaLevel0));
        Assert.NotEqual(eroded, abraded);
    }

    /// <summary>
    /// Tisztaság (I2): ugyanaz a kontextus ismételt hívásra BITRE ugyanazt adja,
    /// és a struct értékszemantikája miatt egy másolat sem tér el az eredetitől.
    /// </summary>
    [Fact]
    public void IsPureAndCopySafe()
    {
        var seeds0 = PlateGeneration.GenerateSeeds(Seed, PlateCount);
        var seeds = PlateMotion.MovedSeeds(Seed, seeds0, 777.0);
        TileGeometry.ToPosition(TileId.FromFaceLevelUV(3, Level, 7, 19),
            out double x, out double y, out double z);

        var ctx = new DeepTimeContext(777.0, 777.0, -120.0);
        DeepTimeContext copy = ctx;

        double a = DeepTimeErosionGlaciation.ElevationAtTime(
            Seed, 0, x, y, z, seeds, ctx, out bool o1);
        double b = DeepTimeErosionGlaciation.ElevationAtTime(
            Seed, 0, x, y, z, seeds, ctx, out bool o2);
        double c = DeepTimeErosionGlaciation.ElevationAtTime(
            Seed, 0, x, y, z, seeds, copy, out bool o3);

        Assert.Equal(BitConverter.DoubleToInt64Bits(a), BitConverter.DoubleToInt64Bits(b));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a), BitConverter.DoubleToInt64Bits(c));
        Assert.Equal(o1, o2);
        Assert.Equal(o1, o3);
    }

    /// <summary>
    /// A22 REGRESSZIÓS KAPU: a <c>ComputeElevationFieldAtTime</c> régi,
    /// csak-lemez-idős túlterhelése bitre ugyanazt adja, mint az
    /// <see cref="DeepTimeContext.AtPlateTime"/>-os új út — a kettő ugyanaz a
    /// kód, de a szerződést ki kell kötni, mert a CLI és a diagnosztikai
    /// eszközök a rövid alakot hívják.
    /// </summary>
    [Fact]
    public void PlateTimeOnlyOverloadMatchesContextPath()
    {
        const int level = 3;
        foreach (double timeMyr in new[] { 0.0, 120.0 })
        {
            var legacy = SeaLevelCalibration.ComputeElevationFieldAtTime(
                Seed, PlateCount, level, timeMyr);
            var viaContext = SeaLevelCalibration.ComputeElevationFieldAtTime(
                Seed, PlateCount, level, DeepTimeContext.AtPlateTime(timeMyr));

            Assert.Equal(legacy.Count, viaContext.Count);
            foreach (var kv in legacy)
                Assert.Equal(
                    BitConverter.DoubleToInt64Bits(kv.Value),
                    BitConverter.DoubleToInt64Bits(viaContext[kv.Key]));
        }
    }
}
