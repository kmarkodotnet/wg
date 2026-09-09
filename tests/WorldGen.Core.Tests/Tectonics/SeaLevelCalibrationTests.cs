using System;
using System.Collections.Generic;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// A `TEST-EARTH-001` közvetlen kódbeli kifejezése (docs/05-milestones.md
/// M4 sora): 50-75% víz, legalább 2 kontinens. A referencia (tools/reference/
/// sea_level_ref.py) ugyanezekkel a paraméterekkel (world_seed, plateCount=20,
/// level=6) mérve pontosan 65.00% vizet és 44 kontinenst adott (ND-37 -
/// `CrustElevation.DefaultOceanicProbability` 0.55→0.40-re csökkentve,
/// szándékosan a `TargetWaterFraction`=0.65 alá decorrelálva, hogy a
/// percentilis-kalibráció ne az óceáni-kéreg tartomány tetejére, hanem a
/// legalacsonyabb fekvésű kontinentális tile-okba is belenyúljon - ez a
/// parti sáv relatív magasságát ~4072m-ről ~242.6m-re csökkentette, cserébe
/// a korábbi 6 helyett 44, jellemzően kisebb kontinenst/szigetet ad).
///
/// ND-52 (2026-09-07): a `CrustElevation.BaseElevation` másodlagos,
/// finom-léptékű részlet-zajának bevezetése MEGVÁLTOZTATTA a pontos
/// elevációt minden pozícióra, tehát a kontinens-partíciót is - a Python
/// referenciával újramérve 37 kontinenst ad (65%-os víz-arány cél
/// változatlan, a WaterFractionIsBetween50And75Percent/HasAtLeastTwoContinents
/// tesztek továbbra is PASS-olnak, csak az EGZAKT méret-lista frissült).
///
/// ND-52 UJRAHANGOLÁS (2026-09-07, ugyanaznap, második kör: a felhasználó
/// jelezte, hogy a másodlagos zaj hatása "nem jött be" - a periódus
/// utólagos számolással kb. 0.3125-szöröse egy teljes nagykörnek, tehát
/// SOSEM adott "közeli-zoom részletet", csak egy alig észrevehető, 200m-es
/// regionális hullámzást). Az amplitúdó 200→900m-re emelve, hogy ez a
/// már eleve folytonos, egész-felszínes hullámzás láthatóvá váljon -
/// 37→34 kontinensre módosítva (Python referenciával újramérve, 65%-os
/// víz-arány továbbra is változatlan).
///
/// ND-56 (2026-09-09): harmadik, közeli-zoom léptékű részlet-zaj réteg
/// (`CrustElevation.TertiaryDetailNoise`) bevezetve, majd MÉG UGYANAZNAP
/// TELJESEN VISSZAVONVA (élő teszt után a felhasználó jelezte: "nem lett
/// jobb... működjön minden úgy ahogy ezelőtt") - a réteg és minden
/// paramétere törölve a Core-ból, a lenti értékek ismét a ND-52
/// második körének (level=5/40 tile, 900m amplitúdó) eredményei.
/// </summary>
public class TestEarth001Tests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;
    private const double TargetWaterFraction = 0.65;

    [Fact]
    public void WaterFractionIsBetween50And75Percent()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);

        int water = 0;
        foreach (double e in field.Values)
            if (e < seaLevel) water++;
        double waterFraction = water / (double)field.Count;

        Assert.InRange(waterFraction, 0.50, 0.75);
    }

    [Fact]
    public void HasAtLeastTwoContinents()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var continents = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 5);

        Assert.True(continents.Count >= 2, $"Legalább 2 kontinens várt, kaptunk: {continents.Count}");
    }

    /// <summary>Egzakt egyezés a Python referenciával mért kontinens-méretekkel.</summary>
    [Fact]
    public void ContinentSizesMatchPythonReferenceExactly()
    {
        var field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        var continents = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 5);

        var sizes = new List<int>();
        foreach (var c in continents)
            sizes.Add(c.Count);
        sizes.Sort((a, b) => b.CompareTo(a));

        Assert.Equal(new List<int> {
            4254, 2960, 289, 220, 152, 87, 53, 38, 38, 35, 33, 30, 28, 19,
            18, 16, 15, 15, 14, 12, 12, 11, 9, 8, 8, 7, 7, 6, 6, 6,
            6, 5, 5, 5,
        }, sizes);
    }
}

public class SeaLevelCalibrationUnitTests
{
    [Fact]
    public void CalibrateSeaLevelPicksExpectedPercentile()
    {
        var elevations = new List<double> { 5.0, 1.0, 3.0, 2.0, 4.0 }; // sorolatlan, [1,2,3,4,5]-re rendezodik
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(elevations, targetWaterFraction: 0.5);
        // idx = (int)(0.5*5) = 2 -> a rendezett tomb 3. eleme (0-indexeles): 3.0
        Assert.Equal(3.0, seaLevel);
    }

    [Fact]
    public void CalibrateSeaLevelAtZeroPicksMinimum()
    {
        var elevations = new List<double> { 5.0, 1.0, 3.0 };
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(elevations, targetWaterFraction: 0.0);
        Assert.Equal(1.0, seaLevel);
    }

    [Fact]
    public void CalibrateSeaLevelIsPure()
    {
        var elevations = new List<double> { 5.0, 1.0, 3.0, 2.0, 4.0 };
        double a = SeaLevelCalibration.CalibrateSeaLevel(elevations, 0.3);
        double b = SeaLevelCalibration.CalibrateSeaLevel(elevations, 0.3);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ThrowsOnEmptyElevations()
    {
        Assert.Throws<ArgumentException>(() =>
            SeaLevelCalibration.CalibrateSeaLevel(new List<double>(), 0.5));
    }

    [Fact]
    public void CountContinentsOnSmallGridIsDeterministic()
    {
        // Kisebb, olcsobb racs (level 3, 384 tile) a tisztasag/determinizmus
        // ellenorzesehez - nem kell a teljes level 6 minden edge case tesztnel.
        var field = SeaLevelCalibration.ComputeElevationField(1UL, plateCount: 6, level: 3);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.5);

        var a = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 1);
        var b = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 1);

        Assert.Equal(a.Count, b.Count);
    }

    [Fact]
    public void MinSizeFilterRemovesSmallComponents()
    {
        var field = SeaLevelCalibration.ComputeElevationField(1UL, plateCount: 6, level: 3);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.5);

        var unfiltered = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 1);
        var filtered = SeaLevelCalibration.CountContinents(field, seaLevel, minSize: 1000);

        Assert.True(filtered.Count <= unfiltered.Count);
    }
}
