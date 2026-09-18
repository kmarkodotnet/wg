using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

/// <summary>
/// REGRESSZIÓS TESZT egy VISSZATÉRŐ hibaosztályra (2026-09-18).
///
/// Tünet (a felhasználó többször jelezte, legutóbb "nagyon visszajött"):
/// a rotáció és a zoom szaggat, a munkamenet előrehaladtával rosszabbodva.
/// Mért gyökérok (`history/2026-09-18-lod-render-budget.md`, illetve a
/// `history/2026-09-13-rotation-zoom-stutter-lod-cut-growth.md` élő
/// PerfLog-elemzése): a `SelectCutPrioritized` a felszín közelében MINDEN
/// gyakorlati kameramagasságon jóval több dinamikus levelet KÉRNE, mint
/// amennyit a gép interaktív sebességgel fel tud építeni; az egyetlen
/// tényleges korlát az `adaptiveRenderBudget`, ami 200 000-en állt, tehát
/// gyakorlatilag SOSEM kötött. Élőben mérve: ~33 000 levélnél a kérés-idő
/// 1300-1900ms (a szaggatás), induláskor 100-350ms.
///
/// Ez a teszt a hiszterézis visszacsatolási hurkot szimulálja (minden lépés
/// az ELŐZŐ cutot kapja meg, miközben a kamera körbefordul), és azt
/// rögzíti, hogy a költségvetés TÉNYLEGESEN felső korlát marad - erre
/// támaszkodik a javítás (budget 200000 -> 8000). Ha egy jövőbeli változás
/// kiiktatná a budget-korlátot, ez a teszt megfogja.
///
/// SZÁNDÉKOSAN KIS KÖLTSÉGVETÉSEKKEL fut: egy 200 000-es próba több
/// százezer elemű halmazt allokál, ami a párhuzamosan futó, ALLOKÁCIÓT MÉRŐ
/// tesztet (TerrainEvaluationCacheTests) elbuktatja - a nagy költségvetésű
/// mérés eredménye ezért a history-dokumentumban van, nem itt.
/// </summary>
public class CutGrowthRegressionTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public CutGrowthRegressionTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private const double Radius = 100;
    private const double Fov = Math.PI / 3;
    private const double Aspect = 16.0 / 9;

    /// <summary>
    /// A `EnforceRestrictedBalance` a NEM-strict (work == null, azaz teszt-)
    /// úton `maxLeafCount * 3`-ig engedi a kiegyensúlyozó kiegészítést
    /// (ld. ott: balanceSizeCap) - a felső korlát tehát a budget
    /// háromszorosa, nem a budget maga. Élesben (work != null) a strict ág
    /// a budgetnél vág.
    /// </summary>
    private const int BalanceSlackFactor = 3;

    private static HashSet<TileId> CutAtAngle(double angleRadians, double distance, int budget,
        IReadOnlyCollection<TileId>? previous)
    {
        // Kamera a planéta körül, az XZ síkban körbefordulva.
        double cx = Math.Cos(angleRadians) * distance;
        double cz = Math.Sin(angleRadians) * distance;
        double len = Math.Sqrt(cx * cx + cz * cz);
        double fwdX = -cx / len, fwdZ = -cz / len;

        double split = 6 * Fov / 1080;
        double half = Math.Atan(Math.Tan(Fov / 2) * Math.Sqrt(1 + Aspect * Aspect)) * 1.3;
        return AdaptiveQuadTree.BuildCut(cx, 0, cz, Radius, previous!, 8, 20,
            split, split / 1.5, fwdX, 0, fwdZ, half, budget,
            traversalRootLevel: 3, staticBaseLevel: 8);
    }

    /// <summary>
    /// A LÉNYEGI állítás: bármennyi egymásra épülő (hiszterézises) lépés
    /// után sem lépheti túl a cut a költségvetést (a kiegyensúlyozó
    /// ráhagyással) - sem forgatás, sem az előző cut visszacsatolása nem
    /// tudja "felpumpálni".
    /// </summary>
    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    [InlineData(8000)]
    public void RotatingCameraWithHysteresisNeverExceedsBudget(int budget)
    {
        HashSet<TileId>? previous = null;
        int first = -1, max = 0;
        for (int step = 0; step < 24; step++)
        {
            HashSet<TileId> cut = CutAtAngle(step * (Math.PI / 12), 100.6, budget, previous);
            Assert.InRange(cut.Count, 0, budget * BalanceSlackFactor);
            if (first < 0) first = cut.Count;
            if (cut.Count > max) max = cut.Count;
            previous = cut;
        }
        _output.WriteLine($"budget={budget}: elso cut={first}, maximum 24 lepes alatt={max}, " +
                          $"felso korlat={budget * BalanceSlackFactor}");
    }

    /// <summary>
    /// A költségvetés a felszín közelében MINDEN vizsgált magasságon kötő
    /// korlát (nem csak néha aktiváló védőháló): a kiválasztás mindig
    /// pontosan a budgetig finomít. Ez az állítás az, ami miatt a budget
    /// értéke KÖZVETLENÜL a szaggatás mértékét szabályozza.
    /// </summary>
    [Theory]
    [InlineData(100.1)]
    [InlineData(100.6)]
    [InlineData(102.0)]
    [InlineData(110.0)]
    public void BudgetIsTheBindingConstraintNearTheSurface(double distance)
    {
        const int budget = 1500;
        HashSet<TileId> cut = CutAtAngle(0, distance, budget, null);
        _output.WriteLine($"magassag={distance - Radius:F1} egyseg -> cut={cut.Count} (budget={budget})");
        // A budget-ellenorzes egy NEGYES csaladot nez elore
        // (`result.Count + pendingDynamicLeaves + 4 > budget`), ezert a cut a
        // budget alatt legfeljebb egy csaladdal all meg - de a hataron, nem a
        // hibakuszob miatt. Ez a "kötő korlát" allitas lenyege.
        Assert.InRange(cut.Count, budget - 4, budget);
    }
}
