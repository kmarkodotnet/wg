using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests;

/// <summary>
/// ND-149 (A18): a parti víz-gyűrű tiszta döntései. A gyűrű azért létezik,
/// hogy a vízfelszín TÚLNYÚLJON a valódi parton, és a renderelt terep vágja
/// ki belőle a partvonalat - ezért a két kérdés, amit itt rögzítünk:
/// rajzoljuk-e a tile-t egyáltalán, és melyik élen kell TOVÁBB terjedni.
/// </summary>
public class CoastalWaterRingTests
{
    [Fact]
    public void AllCornersAboveWaterIsSkipped()
        => Assert.Equal(CoastalRingTileState.Skipped,
            CoastalWaterRing.Classify(false, false, false, false));

    [Fact]
    public void AllCornersBelowWaterIsSubmerged()
        => Assert.Equal(CoastalRingTileState.Submerged,
            CoastalWaterRing.Classify(true, true, true, true));

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    public void AnyMixedCornerIsCoast(bool b00, bool b10, bool b11, bool b01)
        => Assert.Equal(CoastalRingTileState.Coast, CoastalWaterRing.Classify(b00, b10, b11, b01));

    /// <summary>
    /// A NYERS vízperem feltétele élenkénti: mindkét KÖZÖS sarok víz alatt.
    /// A sarok-sorrend a quad-emisszióé (00, 10, 11, 01), tehát a jobb él a
    /// 10-11 pár, a bal a 00-01, a felső a 01-11, az alsó a 00-10.
    /// </summary>
    [Theory]
    [InlineData(TileDirection.Right, false, true, true, false)]
    [InlineData(TileDirection.Left, true, false, false, true)]
    [InlineData(TileDirection.Up, false, false, true, true)]
    [InlineData(TileDirection.Down, true, true, false, false)]
    public void EdgeIsOpenWhenBothSharedCornersAreBelowWater(
        TileDirection direction, bool b00, bool b10, bool b11, bool b01)
        => Assert.True(CoastalWaterRing.EdgeFullyBelowWater(direction, b00, b10, b11, b01));

    /// <summary>
    /// Ugyanazok a sarok-mintázatok a TÖBBI irányra nem nyitottak - ez fogja
    /// meg, ha az él -> sarokpár leképezés elcsúszna.
    /// </summary>
    [Theory]
    [InlineData(TileDirection.Left, false, true, true, false)]
    [InlineData(TileDirection.Up, false, true, true, false)]
    [InlineData(TileDirection.Down, false, true, true, false)]
    [InlineData(TileDirection.Right, true, false, false, true)]
    [InlineData(TileDirection.Right, false, false, true, true)]
    [InlineData(TileDirection.Right, true, true, false, false)]
    public void OtherDirectionsStayClosedForTheSameCorners(
        TileDirection direction, bool b00, bool b10, bool b11, bool b01)
        => Assert.False(CoastalWaterRing.EdgeFullyBelowWater(direction, b00, b10, b11, b01));

    /// <summary>
    /// Élesetek: teljesen víz alatti tile-on MIND a négy él nyitott, teljesen
    /// víz fölöttin EGYIK SEM. Az előbbi az, ami a gyűrűt továbbviszi.
    /// </summary>
    [Theory]
    [InlineData(TileDirection.Right)]
    [InlineData(TileDirection.Left)]
    [InlineData(TileDirection.Up)]
    [InlineData(TileDirection.Down)]
    public void SubmergedTileOpensEveryEdgeAndDryTileOpensNone(TileDirection direction)
    {
        Assert.True(CoastalWaterRing.EdgeFullyBelowWater(direction, true, true, true, true));
        Assert.False(CoastalWaterRing.EdgeFullyBelowWater(direction, false, false, false, false));
    }

    /// <summary>
    /// A kihagyott (mind a négy sarka víz fölötti) tile SOHA nem nyit élt -
    /// vagyis a gyűrű nem tud száraz tile-on át terjedni.
    /// </summary>
    [Fact]
    public void SkippedTileNeverPropagates()
    {
        Assert.Equal(CoastalRingTileState.Skipped, CoastalWaterRing.Classify(false, false, false, false));
        for (int d = 0; d < 4; d++)
            Assert.False(CoastalWaterRing.EdgeFullyBelowWater((TileDirection)d, false, false, false, false));
    }
}
