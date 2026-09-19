using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

/// <summary>
/// A vágás MUNKA-KÖNYVELÉSE (felhasználói kérés, 2026-09-19): "hány új tile
/// került kiszámolásra és hány tile kalkulálása lett abbahagyva, mert
/// szükségtelen". A `[tilesample]` napló ezeket a számokat írja ki minden
/// minta mellé, ezért ELŐBB itt igazoljuk a jelentésüket — különben a
/// naplóból levont következtetés is hibás lenne.
///
/// Ez a teszt egy KONKRÉT, MÁR MEGTÖRTÉNT félreértést fog meg: a
/// 2026-09-18-i kiértékelésben a `deferredSplits`-et a leaf-budget
/// éhezésének olvastam, holott az a KÉRÉSENKÉNTI kvóta számlálója. A
/// leaf-budget megállást addig SEMMI nem számolta
/// (<see cref="LodSelectionWork.BudgetStops"/>), csak a trace rögzítette
/// tile-onként.
///
/// MÉRETEZÉS: szándékosan durva küszöbbel (<see cref="PixelHeight"/>=135)
/// dolgozunk, hogy a "bőséges budget" eset is pár ezer tile maradjon. Egy
/// korábbi, 200 000-es budgettel dolgozó próbám több százezer TileId-t
/// allokált, és ezzel elhasította a párhuzamosan futó, allokációt mérő
/// TerrainEvaluationCacheTests-t.
/// </summary>
public class CutWorkAccountingTests
{
    private const double Radius = 100;
    private const double Fov = Math.PI / 3;
    private const double Aspect = 16.0 / 9;
    private const int BaseLevel = 8;
    private const int MaxLevel = 20;
    private const int PixelHeight = 135;

    /// <summary>Bolygó-nézet: a 8 szintű statikus alap fedi, nincs éhezés.</summary>
    private const double FarDistance = 140.0;

    /// <summary>Felszín-közel: itt telítődik a budget.</summary>
    private const double NearDistance = 103.0;

    private const int TightBudget = 600;
    private const int AmpleBudget = 40000;

    // Ugyanaz a nézet-tengely, mint az AdaptiveQuadTreeBudgetTests-ben, hogy
    // a két teszt ugyanarról a vágásról beszéljen.
    private static readonly double AxisLength = Math.Sqrt(.017 * .017 + .937 * .937 + .349 * .349);
    private static readonly double Ax = .017 / AxisLength, Ay = -.937 / AxisLength, Az = .349 / AxisLength;

    private static HashSet<TileId> Cut(double distance, int budget, out LodSelectionWork work,
        int maxNewSplits = int.MaxValue)
    {
        work = new LodSelectionWork(null, maxNewSplits);
        double split = 6 * Fov / PixelHeight;
        double half = Math.Atan(Math.Tan(Fov / 2) * Math.Sqrt(1 + Aspect * Aspect)) * 1.3;
        return AdaptiveQuadTree.BuildCut(Ax * distance, Ay * distance, Az * distance, Radius,
            Array.Empty<TileId>(), BaseLevel, MaxLevel, split, split / 1.5,
            -Ax, -Ay, -Az, half, budget,
            traversalRootLevel: 3, staticBaseLevel: BaseLevel, work: work);
    }

    /// <summary>
    /// A kért szám: szűk budgetnél a szelekció AKART finomítani, de nem
    /// engedték. Ez ELVONT munka, nem szükségtelen — eddig sehol nem látszott.
    /// </summary>
    [Fact]
    public void LeafBudgetStarvationIsCounted()
    {
        HashSet<TileId> cut = Cut(NearDistance, TightBudget, out LodSelectionWork work);

        Assert.InRange(cut.Count, 1, TightBudget);
        Assert.True(work.BudgetStops > 0,
            $"A budget {TightBudget}-nál telítődött, de BudgetStops=0 maradt.");
        // A kvóta itt végtelen, tehát a két ok NEM keveredhet.
        Assert.Equal(0, work.DeferredSplits);
    }

    /// <summary>
    /// Bőséges budgetnél nincs éhezés: minden megállás azért történt, mert a
    /// tile MÁR elég finom volt — vagyis a munka valóban szükségtelen volt.
    /// </summary>
    [Fact]
    public void AmpleBudgetLeavesOnlySufficientStops()
    {
        HashSet<TileId> cut = Cut(NearDistance, AmpleBudget, out LodSelectionWork work);

        Assert.Equal(0, work.BudgetStops);
        Assert.Equal(0, work.DeferredSplits);
        Assert.True(work.BelowThresholdStops > 0, "Egyetlen 'elég finom' megállás sincs.");
        Assert.NotEmpty(cut);
    }

    /// <summary>
    /// A 2026-09-18-i félreolvasás ellen: a kérésenkénti kvóta és a
    /// leaf-budget KÜLÖN ok, és külön is látszik.
    /// </summary>
    [Fact]
    public void SplitQuotaIsAccountedSeparatelyFromLeafBudget()
    {
        HashSet<TileId> cut = Cut(NearDistance, AmpleBudget, out LodSelectionWork work,
            maxNewSplits: 128);

        Assert.True(work.DeferredSplits > 0, "A 128-as kvóta nem fogyott el.");
        Assert.Equal(0, work.BudgetStops);
        Assert.InRange(work.NewSplits, 1, 128);
        Assert.NotEmpty(cut);
    }

    /// <summary>
    /// "Hány új tile került kiszámolásra": a kvóta felső korlát, amit a
    /// szelekció soha nem lép túl.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(1024)]
    public void NewSplitsNeverExceedQuota(int quota)
    {
        Cut(NearDistance, AmpleBudget, out LodSelectionWork work, maxNewSplits: quota);
        Assert.InRange(work.NewSplits, 0, quota);
    }

    /// <summary>
    /// Teljességi invariáns: MINDEN kirajzolt dinamikus tile-hoz tartozik
    /// rögzített megállási ok. A "&lt;=" azért kell, mert az alapszinten vagy
    /// alatta történt megállás nem ad dinamikus levelet.
    /// </summary>
    [Theory]
    [InlineData(TightBudget)]
    [InlineData(AmpleBudget)]
    public void EveryDynamicLeafHasARecordedStopReason(int budget)
    {
        HashSet<TileId> cut = Cut(NearDistance, budget, out LodSelectionWork work);

        int stops = work.BudgetStops + work.DeferredSplits + work.BelowThresholdStops
            + work.MaxLevelStops + work.InvisibleStops;
        Assert.True(cut.Count <= stops,
            $"{cut.Count} dinamikus levél, de csak {stops} könyvelt megállás.");
    }

    /// <summary>Tisztaság: ugyanaz a kérés ugyanazokat a számokat adja.</summary>
    [Fact]
    public void CountersAreRepeatable()
    {
        Cut(NearDistance, TightBudget, out LodSelectionWork a);
        Cut(NearDistance, TightBudget, out LodSelectionWork b);

        Assert.Equal(a.BudgetStops, b.BudgetStops);
        Assert.Equal(a.NewSplits, b.NewSplits);
        Assert.Equal(a.BelowThresholdStops, b.BelowThresholdStops);
        Assert.Equal(a.MaxLevelStops, b.MaxLevelStops);
        Assert.Equal(a.InvisibleStops, b.InvisibleStops);
    }

    /// <summary>
    /// A kérés "az aktuális zoom vagy rotáció miatt" része, MÉRVE: bolygó-
    /// nézetben ugyanaz a budget bőven elég (a statikus alap fedi a képet),
    /// felszín-közelben viszont éhezik.
    ///
    /// FIGYELEM, ez NEM monoton: a méréseim szerint az elvont felosztások
    /// száma KÖZÉPTÁVON a legnagyobb (ott nagy a látott felület ÉS nagy a
    /// kért részletesség), a legközelebbi zoomnál pedig visszaesik, mert a
    /// látott felület összezsugorodik. Ezért a teszt a két SZÉLSŐ esetet
    /// hasonlítja, nem monotonitást állít.
    /// </summary>
    [Fact]
    public void PlanetViewFitsTheBudgetWhileSurfaceProximityStarvesIt()
    {
        HashSet<TileId> far = Cut(FarDistance, TightBudget, out LodSelectionWork farWork);
        Cut(NearDistance, TightBudget, out LodSelectionWork nearWork);

        Assert.Empty(far); // a statikus alapszint fedi
        Assert.Equal(0, farWork.BudgetStops);
        Assert.True(nearWork.BudgetStops > 0,
            $"Felszín-közelben nincs éhezés (BudgetStops={nearWork.BudgetStops}).");
    }

    /// <summary>
    /// A budget-plafon MÉRTÉKE: ugyanahhoz a nézethez a metrika sokszor annyi
    /// levelet kér, mint amennyit a szűk budget megenged. Ez az M9 munka
    /// kvantitatív kiindulópontja — ha a jövőbeli javítás ezt az arányt
    /// lenyomja, az itt látszik.
    /// </summary>
    [Fact]
    public void UnconstrainedCutIsManyTimesLargerThanTheTightBudget()
    {
        HashSet<TileId> tight = Cut(NearDistance, TightBudget, out _);
        HashSet<TileId> ample = Cut(NearDistance, AmpleBudget, out _);

        Assert.True(ample.Count > 4 * tight.Count,
            $"Szűk budget {tight.Count}, korlátlan {ample.Count} levél.");
    }
}
