using System;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

/// <summary>
/// A viewportból számolt levél-költségvetés (2026-09-19). A korábbi fix 8000
/// egy LINEÁRIS extrapolációból jött, és a mintavételező napló szerint
/// 1,4-4,1-szeresére hizlalta a rajzolt tile-okat a 8 px-es célhoz képest.
/// A mérési tábla és az indoklás:
/// <see cref="AdaptiveQuadTree.RenderBudgetForViewport"/>.
/// </summary>
public class RenderBudgetForViewportTests
{
    private const double Target = 8.0;

    /// <summary>
    /// A konkrét eset, amiből a diagnózis született: a felhasználó 1196x710-es
    /// ablakában 849 160 / 64 = 13 269 levél a csupasz raszter-igény (felfelé
    /// kerekítve), a 3,0-es ráhagyással 39 807 - szemben a korábbi fix 8000-rel.
    ///
    /// A szorzó 1,5-ről 3,0-ra nőtt, miután a horizont-vágás a selectiont 7-8x
    /// olcsóbbá tette; az érték a naplóból VISSZASZÁMOLT levélszükséglet
    /// (ld. AdaptiveQuadTree.DefaultRenderBudgetOverhead).
    /// </summary>
    [Fact]
    public void MeasuredSessionViewportAsksForFiveTimesTheOldBudget()
    {
        int budget = AdaptiveQuadTree.RenderBudgetForViewport(1196, 710, Target);

        Assert.Equal(39807, budget);
        Assert.True(budget > 4 * AdaptiveQuadTree.MinimumRenderBudget);
    }

    /// <summary>A képlet lényege: a képernyő befér-e cél-méretű tile-okkal.</summary>
    [Theory]
    [InlineData(1920, 1080, 1.0, 32400)]   // 2 073 600 / 64
    [InlineData(1280, 720, 1.0, 14400)]
    [InlineData(1280, 720, 2.0, 28800)]    // a ráhagyás lineárisan hat
    public void BudgetTracksPixelCountAndOverhead(int width, int height,
        double overhead, int expected)
        => Assert.Equal(expected, AdaptiveQuadTree.RenderBudgetForViewport(
            width, height, Target, overhead));

    /// <summary>
    /// Négyzetesen hat: fele akkora cél-tile négyszer annyi levél. Az alsó/felső
    /// korlátot kikapcsolva mérjük, különben a clamp elfedné a képletet -
    /// 1600x900 @16 px ugyanis 5625, ami a 8000-es minimum ALATT van.
    /// </summary>
    [Fact]
    public void HalvingTheTargetTileQuadruplesTheBudget()
    {
        int coarse = AdaptiveQuadTree.RenderBudgetForViewport(1600, 900, 16.0, 1.0, 1, int.MaxValue);
        int fine = AdaptiveQuadTree.RenderBudgetForViewport(1600, 900, 8.0, 1.0, 1, int.MaxValue);

        Assert.Equal(5625, coarse);
        Assert.Equal(4 * coarse, fine);
    }

    /// <summary>
    /// Kis ablaknál sem megyünk a korábbi kézi érték alá. A 3,0-es szorzóval a
    /// minimum 8000/3 * 64 = 170 667 képpontnál fordul át (16:9-ben ~551x310),
    /// ezért itt ennél kisebb ablakok szerepelnek - 640x480 MÁR 14 400-at kap.
    /// </summary>
    [Theory]
    [InlineData(320, 240)]
    [InlineData(480, 270)]
    [InlineData(1, 1)]
    public void SmallViewportsKeepTheHistoricMinimum(int width, int height)
        => Assert.Equal(AdaptiveQuadTree.MinimumRenderBudget,
            AdaptiveQuadTree.RenderBudgetForViewport(width, height, Target));

    /// <summary>
    /// 4K-nál a nyers képlet 194 400-at kérne, tehát a felső korlát ott
    /// továbbra is vág. A korlát ÉRTÉKE viszont 2026-09-21-én 48 000-ről
    /// 96 000-re emelkedett (ND-121): a korábbi indok részben az
    /// EnforceRestrictedBalance költsége volt (48 000-nél 57 ms), amit a
    /// munkalistás átírás megszüntetett, részben a költség-paritás, ami az
    /// élő mérés szerint nem következett be (a vágás a metrika szerint
    /// ~49 216 levélnél telítődik, nem a budgetnél). Ez a teszt
    /// SZÁNDÉKOSAN a konstansra hivatkozik, nem egy beírt számra - így a
    /// plafon jövőbeli hangolása nem törik el rajta, csak a vágás TÉNYE
    /// marad rögzítve.
    /// </summary>
    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(7680, 4320)]
    public void LargeViewportsAreCappedAtTheMeasuredCostCeiling(int width, int height)
        => Assert.Equal(AdaptiveQuadTree.MaximumRenderBudget,
            AdaptiveQuadTree.RenderBudgetForViewport(width, height, Target));

    /// <summary>Monoton: nagyobb ablak soha nem kap kevesebb levelet.</summary>
    [Fact]
    public void BudgetIsMonotonicInViewportArea()
    {
        int previous = 0;
        foreach (int width in new[] { 640, 1024, 1196, 1600, 1920, 2560, 3840 })
        {
            int budget = AdaptiveQuadTree.RenderBudgetForViewport(width, width * 9 / 16, Target);
            Assert.True(budget >= previous, $"{width} szélességnél csökkent: {budget} < {previous}.");
            previous = budget;
        }
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void InvalidViewportIsRejected(int width, int height)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => AdaptiveQuadTree.RenderBudgetForViewport(width, height, Target));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-8.0)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTargetTileSizeIsRejected(double target)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => AdaptiveQuadTree.RenderBudgetForViewport(1196, 710, target));

    /// <summary>A ráhagyás nem csökkenthet: 1 alatt hiba, nem csendes vágás.</summary>
    [Fact]
    public void OverheadBelowOneIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => AdaptiveQuadTree.RenderBudgetForViewport(1196, 710, Target, 0.5));
}
