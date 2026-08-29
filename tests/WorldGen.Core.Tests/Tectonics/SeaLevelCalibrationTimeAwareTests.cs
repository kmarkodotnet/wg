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
