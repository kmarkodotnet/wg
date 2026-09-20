using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

/// <summary>
/// A kérésenkénti felosztás-kvóta (`PlanetGridMesh.NewSplitsPerRequest`, ND-76)
/// KÉSLELTETÉS-szabályzó, nem minőségi: a vágás ugyanoda konvergál, csak több
/// vagy kevesebb kérésből. Ezt a tesztet a #1 felhasználói visszajelzés
/// ("közepes közelítésnél brutál nagyok a tile-ok") kivizsgálása hozta létre.
///
/// MIÉRT KELL. A 2026-09-20-i mérés alapján emeltük a kvótát 1024-ről
/// 8192-re. Az emelés CSAK akkor védhető, ha a végeredmény nem változik —
/// különben egy teljesítmény-hangoló konstans csendben a KÉPET is átírná, és
/// egy jövőbeli visszaállítás/finomhangolás észrevétlenül más világot
/// rajzolna. Ez a teszt pontosan ezt az invariánst rögzíti.
/// </summary>
public class SplitQuotaConvergenceTests
{
    private const double Radius = 100;
    private const double Fov = Math.PI / 3;
    private const double Aspect = 16.0 / 9;
    private const int BaseLevel = 8;
    private const int MaxLevel = 20;

    // Szándékosan szerény felbontás/budget: a konvergencia-ciklus így is
    // többször lefut, de nem allokál százezres nagyságrendű TileId-t (ld. a
    // CutWorkAccountingTests méretezési megjegyzését).
    private const int PixelHeight = 270;

    /// <summary>Az a méret, ahol a 256-os kvóta BIZONYÍTOTTAN köt (ld. QuotaActuallyBinds).</summary>
    private const int BindingBudget = 20000;

    private static readonly double AxisLength = Math.Sqrt(.017 * .017 + .937 * .937 + .349 * .349);
    private static readonly double Ax = .017 / AxisLength, Ay = -.937 / AxisLength, Az = .349 / AxisLength;

    private static HashSet<TileId> Cut(double distance, TileId[] previous, int maxNewSplits,
        int budget, out LodSelectionWork work)
    {
        work = new LodSelectionWork(null, maxNewSplits);
        double split = 6 * Fov / PixelHeight;
        double half = Math.Atan(Math.Tan(Fov / 2) * Math.Sqrt(1 + Aspect * Aspect)) * 1.3;
        return AdaptiveQuadTree.BuildCut(Ax * distance, Ay * distance, Az * distance, Radius,
            previous, BaseLevel, MaxLevel, split, split / 1.5,
            -Ax, -Ay, -Az, half, budget,
            traversalRootLevel: 3, staticBaseLevel: BaseLevel, work: work);
    }

    /// <summary>Ismételt kéréseket ad ki, amíg a szelekció már nem oszt fel semmit.</summary>
    private static HashSet<TileId> Converge(double distance, int quota, int budget, out int requests)
    {
        TileId[] previous = Array.Empty<TileId>();
        HashSet<TileId> cut = Cut(distance, previous, quota, budget, out LodSelectionWork work);
        requests = 1;
        while (work.NewSplits > 0 && requests < 200)
        {
            previous = cut.ToArray();
            cut = Cut(distance, previous, quota, budget, out work);
            requests++;
        }
        return cut;
    }

    /// <summary>
    /// A kvóta NEM változtatja meg a végeredményt - csak azt, hány kérésből
    /// jutunk el oda.
    /// </summary>
    /// <remarks>
    /// Csak olyan távolságok, ahol egyáltalán VAN dinamikus vágás. 120-nál
    /// (20 egység magasság) ezen a 270 px-es felbontáson a level 8-as statikus
    /// alapszint MÁR teljesíti a 8 px-es célt, tehát a vágás üres - helyes
    /// viselkedés, de a kvótáról semmit nem mondana.
    /// </remarks>
    [Theory]
    [InlineData(110.0)]
    [InlineData(103.0)]
    public void ConvergedCutDoesNotDependOnTheSplitQuota(double distance)
    {
        HashSet<TileId> slow = Converge(distance, 256, BindingBudget, out int slowRequests);
        HashSet<TileId> fast = Converge(distance, 8192, BindingBudget, out int fastRequests);

        Assert.True(slow.SetEquals(fast),
            $"A konvergalt vagas elter: {slow.Count} levél 256-os kvótával, "
            + $"{fast.Count} levél 8192-essel, szimmetrikus különbség "
            + $"{slow.Except(fast).Count() + fast.Except(slow).Count()}.");
        // "Nem rosszabb" - a szigorú javulást a QuotaActuallyBinds méri. Nem
        // minden távolságon köt a kvóta (szerényebb budgeten a 120-asnál
        // mindkettő EGY kérésből konvergál, sőt a vágás üres is lehet, ha a
        // statikus alapszint fedi a képet), és egy ott kikényszerített szigorú
        // egyenlőtlenség üres állítás lenne.
        Assert.True(fastRequests <= slowRequests,
            $"A nagyobb kvóta TÖBB kérést igényelt ({fastRequests} vs {slowRequests}).");
        Assert.NotEmpty(fast);
    }

    /// <summary>
    /// A kvóta felső korlát marad: egyetlen kérés sem oszthat fel többet.
    /// (A <see cref="CutWorkAccountingTests"/> ezt kis kvótákra már rögzíti;
    /// itt a MOST beállított 8192-es nagyságrendet is lefedjük.)
    /// </summary>
    [Fact]
    public void RaisedQuotaIsStillAnUpperBound()
    {
        Cut(110.0, Array.Empty<TileId>(), 8192, BindingBudget, out LodSelectionWork work);
        Assert.InRange(work.NewSplits, 0, 8192);
    }

    /// <summary>
    /// A tényleges haszon: ahol a kvóta KÖT, ott a nagyobb kvóta érdemben
    /// kevesebb kérésből konvergál UGYANODA. Enélkül az előző teszt
    /// elfogadna egy olyan kvótát is, ami sosem lép életbe.
    /// </summary>
    [Fact]
    public void QuotaActuallyBindsAndTheLargerOneConvergesInFewerRequests()
    {
        HashSet<TileId> slow = Converge(110.0, 256, BindingBudget, out int slowRequests);
        HashSet<TileId> fast = Converge(110.0, 8192, BindingBudget, out int fastRequests);

        Assert.True(slowRequests > 3,
            $"A 256-os kvóta nem kötött ({slowRequests} kérés) - a teszt így semmit nem mér.");
        Assert.True(fastRequests < slowRequests,
            $"A 8192-es kvóta nem gyorsított ({fastRequests} vs {slowRequests} kérés).");
        Assert.True(slow.SetEquals(fast), "A két kvóta más vágáshoz konvergált.");
    }
}
