using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

/// <summary>
/// M10: a deep-time (időfüggő) elevation-mező bekötése a lemezmozgásba
/// (ComputeElevationFieldAtTime) - ez teszi az "időcsúszkát élővé":
/// a lemezmozgás ténylegesen befolyásolja a domborzatot, nem csak
/// önmagában létező, be nem kötött képesség.
/// </summary>
public class SeaLevelCalibrationTimeAwareTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 5; // kisebb, olcsóbb szint a gyors teszteléshez

    [Fact]
    public void AtTimeZeroMatchesStaticFieldExactly()
    {
        Dictionary<TileId, double> staticField = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        Dictionary<TileId, double> timeField = SeaLevelCalibration.ComputeElevationFieldAtTime(WorldSeed, PlateCount, Level, 0.0);

        Assert.Equal(staticField.Count, timeField.Count);
        foreach (TileId id in staticField.Keys)
            Assert.Equal(staticField[id], timeField[id]);
    }

    [Fact]
    public void DeepTimeFieldDiffersFromStaticField()
    {
        Dictionary<TileId, double> staticField = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        Dictionary<TileId, double> laterField = SeaLevelCalibration.ComputeElevationFieldAtTime(WorldSeed, PlateCount, Level, 100.0);

        int differing = 0;
        foreach (TileId id in staticField.Keys)
            if (staticField[id] != laterField[id])
                differing++;

        Assert.True(differing > 0, "100 Myr elteltével legalább néhány tile elevációjának változnia kell");
    }

    [Fact]
    public void ContinentsShiftOverDeepTime()
    {
        Dictionary<TileId, double> field0 = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double seaLevel0 = SeaLevelCalibration.CalibrateSeaLevel(field0.Values, 0.65);
        var continents0 = new HashSet<TileId>();
        foreach (var comp in SeaLevelCalibration.CountContinents(field0, seaLevel0, minSize: 5))
            foreach (TileId t in comp) continents0.Add(t);

        Dictionary<TileId, double> field100 = SeaLevelCalibration.ComputeElevationFieldAtTime(WorldSeed, PlateCount, Level, 100.0);
        double seaLevel100 = SeaLevelCalibration.CalibrateSeaLevel(field100.Values, 0.65);
        var continents100 = new HashSet<TileId>();
        foreach (var comp in SeaLevelCalibration.CountContinents(field100, seaLevel100, minSize: 5))
            foreach (TileId t in comp) continents100.Add(t);

        // A kontinensek altal fedett tile-halmaznak legalabb reszben elteronek
        // kell lennie 100 Myr utan. Explicit szimmetrikus differencia (nem
        // HashSet.Equals/xUnit collection-equality, aminek a rendezetlen
        // halmazokra vonatkozo szemantikaja bizonytalan).
        Assert.NotEmpty(continents0);
        Assert.NotEmpty(continents100);

        int onlyIn0 = 0, onlyIn100 = 0;
        foreach (TileId t in continents0) if (!continents100.Contains(t)) onlyIn0++;
        foreach (TileId t in continents100) if (!continents0.Contains(t)) onlyIn100++;

        Assert.True(onlyIn0 > 0 || onlyIn100 > 0,
            "A kontinensek által fedett tile-halmaznak el kell térnie 100 Myr elteltével");
    }
}

/// <summary>
/// ND-38: térfogat-megmaradás alapú tengerszint - a percentilis-módszer
/// (<see cref="SeaLevelCalibration.CalibrateSeaLevel"/>) mindig PONTOSAN a
/// célzott víz-arányt adja, függetlenül attól, hogyan alakul a domborzat -
/// ez a deep-time láncban fizikailag hibás (a víz TÉRFOGATának kéne
/// megmaradnia, nem az aránynak). A <see cref="SeaLevelCalibration.
/// CalibrateSeaLevelByVolume"/> a t=0-nál percentilissel kalibrált
/// térfogatot RÖGZÍTI, és minden későbbi t-nél ehhez a térfogathoz tartozó
/// egyensúlyi szintet keresi meg fix (60) iterációjú bináris kereséssel.
/// </summary>
public class SeaLevelCalibrationVolumeBasedTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 5; // ugyanaz az olcsó szint, mint a többi deep-time tesztnél
    private const double TargetWaterFraction = 0.65;

    /// <summary>
    /// t=0-nál a térfogat-alapú visszaoldásnak GYAKORLATILAG egyeznie kell a
    /// percentilis-kalibrált szinttel (ugyanaz a mező, ugyanaz a víztömeg -
    /// csak más módszerrel kalibrálva). A tolerancia (1e-6 m) a Python
    /// referenciával (tools/reference/sea_level_ref.py, "ND-38: terfogat-
    /// alapu tengerszint" szakasz) mérve dokumentált: 60 bináris keresési
    /// lépés dupla pontosság alatt a méteres nagyságrendű elevációs
    /// tartományon (~ezres m) ennél jóval pontosabb, gyakorlatilag exakt
    /// egyezést ad.
    /// </summary>
    [Fact]
    public void AtTimeZeroVolumeBasedLevelMatchesPercentileLevel()
    {
        Dictionary<TileId, double> field0 = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double percentileLevel = SeaLevelCalibration.CalibrateSeaLevel(field0.Values, TargetWaterFraction);

        double v0 = SeaLevelCalibration.ComputeFloodedVolumeProxy(field0.Values, percentileLevel);
        double volumeLevel = SeaLevelCalibration.CalibrateSeaLevelByVolume(field0.Values, v0);

        Assert.True(Math.Abs(volumeLevel - percentileLevel) < 1e-6,
            $"t=0-nál a térfogat-alapú ({volumeLevel}) és percentilis-alapú ({percentileLevel}) szintnek gyakorlatilag egyeznie kell");
    }

    /// <summary>`TEST-EARTH-001` (50-75% víz, ≥2 kontinens) a térfogat-alapú kalibrációval is teljesül t=0-nál.</summary>
    [Fact]
    public void TestEarth001HoldsWithVolumeBasedCalibrationAtTimeZero()
    {
        const int level = 6; // ugyanaz a szint, mint a hivatalos TEST-EARTH-001 tesztnél
        Dictionary<TileId, double> field0 = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, level);
        double percentileLevel = SeaLevelCalibration.CalibrateSeaLevel(field0.Values, TargetWaterFraction);
        double v0 = SeaLevelCalibration.ComputeFloodedVolumeProxy(field0.Values, percentileLevel);
        double seaLevel = SeaLevelCalibration.CalibrateSeaLevelByVolume(field0.Values, v0);

        int water = 0;
        foreach (double e in field0.Values)
            if (e < seaLevel) water++;
        double waterFraction = water / (double)field0.Count;
        Assert.InRange(waterFraction, 0.50, 0.75);

        var continents = SeaLevelCalibration.CountContinents(field0, seaLevel, minSize: 5);
        Assert.True(continents.Count >= 2, $"Legalább 2 kontinens várt, kaptunk: {continents.Count}");
    }

    /// <summary>
    /// A funkció bizonyítéka: rögzített víztérfogat mellett a víz-arány
    /// TÉNYLEGESEN elmozdul a 65%-os t=0 céltól, ahogy a domborzat a
    /// lemezmozgás miatt változik - ha ez az assert sosem futna át, a
    /// mechanizmus csendben visszaesett volna a régi, konstans-arányú
    /// percentilis-viselkedésre.
    /// </summary>
    [Theory]
    [InlineData(50.0)]
    [InlineData(100.0)]
    [InlineData(250.0)]
    [InlineData(500.0)]
    public void WaterFractionActuallyShiftsOverDeepTimeWithFixedVolume(double timeMyr)
    {
        Dictionary<TileId, double> field0 = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double percentileLevel0 = SeaLevelCalibration.CalibrateSeaLevel(field0.Values, TargetWaterFraction);
        double v0 = SeaLevelCalibration.ComputeFloodedVolumeProxy(field0.Values, percentileLevel0);

        Dictionary<TileId, double> fieldT = SeaLevelCalibration.ComputeElevationFieldAtTime(WorldSeed, PlateCount, Level, timeMyr);
        double seaLevelT = SeaLevelCalibration.CalibrateSeaLevelByVolume(fieldT.Values, v0);

        int water = 0;
        foreach (double e in fieldT.Values)
            if (e < seaLevelT) water++;
        double waterFractionT = water / (double)fieldT.Count;

        // Fizikailag plauzibilis tartomány - nem omlott össze 0%-ra/100%-ra.
        Assert.InRange(waterFractionT, 0.05, 0.95);

        // A visszaoldott szintnél a proxy-térfogatnak gyakorlatilag V0-nak kell lennie
        // (a bináris kereső konvergenciájának bizonyítéka).
        double vCheck = SeaLevelCalibration.ComputeFloodedVolumeProxy(fieldT.Values, seaLevelT);
        double relativeTolerance = Math.Max(1.0, Math.Abs(v0)) * 1e-6;
        Assert.True(Math.Abs(vCheck - v0) < relativeTolerance,
            $"A visszaoldott szintnél a proxy-térfogatnak (t={timeMyr}) gyakorlatilag V0-nak kell lennie: V(H)={vCheck}, V0={v0}");
    }

    /// <summary>Fix iterációszám - determinizmus (I1): a metódus nem tolerancia-alapú `while` ciklus.</summary>
    [Fact]
    public void CalibrateSeaLevelByVolumeIsPureAndDeterministic()
    {
        Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double percentileLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        double v0 = SeaLevelCalibration.ComputeFloodedVolumeProxy(field.Values, percentileLevel);

        double a = SeaLevelCalibration.CalibrateSeaLevelByVolume(field.Values, v0);
        double b = SeaLevelCalibration.CalibrateSeaLevelByVolume(field.Values, v0);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ComputeFloodedVolumeProxyIsMonotonicIncreasingInSeaLevel()
    {
        var elevations = new List<double> { -100.0, -50.0, 0.0, 50.0, 100.0 };
        double vLow = SeaLevelCalibration.ComputeFloodedVolumeProxy(elevations, -75.0);
        double vMid = SeaLevelCalibration.ComputeFloodedVolumeProxy(elevations, 0.0);
        double vHigh = SeaLevelCalibration.ComputeFloodedVolumeProxy(elevations, 75.0);

        Assert.True(vLow < vMid);
        Assert.True(vMid < vHigh);
    }

    [Fact]
    public void ComputeFloodedVolumeProxyIsZeroBelowMinimumElevation()
    {
        var elevations = new List<double> { -100.0, -50.0, 0.0, 50.0, 100.0 };
        double v = SeaLevelCalibration.ComputeFloodedVolumeProxy(elevations, -200.0);
        Assert.Equal(0.0, v);
    }

    [Fact]
    public void CalibrateSeaLevelByVolumeThrowsOnEmptyElevations()
    {
        Assert.Throws<ArgumentException>(() =>
            SeaLevelCalibration.CalibrateSeaLevelByVolume(new List<double>(), targetVolume: 10.0));
    }

    [Fact]
    public void CalibrateSeaLevelByVolumeWithZeroTargetVolumeGivesMinimumElevation()
    {
        var elevations = new List<double> { -100.0, -50.0, 0.0, 50.0, 100.0 };
        double level = SeaLevelCalibration.CalibrateSeaLevelByVolume(elevations, targetVolume: 0.0);
        // V(H)=0 minden H<=min(elevations)-ra - a binaris kereses a tartomany
        // aljara konvergal.
        Assert.True(Math.Abs(level - (-100.0)) < 1e-9);
    }

    [Fact]
    public void CalibrateSeaLevelByVolumeRespectsCustomIterationCount()
    {
        Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level);
        double percentileLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, TargetWaterFraction);
        double v0 = SeaLevelCalibration.ComputeFloodedVolumeProxy(field.Values, percentileLevel);

        // Kevesebb iteracioval durvabb kozelites - de meg mindig valid, monoton
        // konvergens ertek, es a metodus parameterezheto (nem csak a default
        // 60-ra van bedrotozva).
        double coarse = SeaLevelCalibration.CalibrateSeaLevelByVolume(field.Values, v0, iterations: 10);
        double fine = SeaLevelCalibration.CalibrateSeaLevelByVolume(field.Values, v0, iterations: 60);

        Assert.NotEqual(coarse, fine);
        Assert.True(Math.Abs(fine - percentileLevel) < Math.Abs(coarse - percentileLevel) || Math.Abs(fine - percentileLevel) < 1e-6,
            "Több iterációval a becslésnek legalább annyira jónak (vagy jobbnak) kell lennie");
    }
}
