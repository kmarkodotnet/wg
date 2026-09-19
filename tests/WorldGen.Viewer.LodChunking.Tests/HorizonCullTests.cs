using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.Lod.Tests;

/// <summary>
/// Horizont-vágás a vetített terep-úton (2026-09-19).
///
/// ELŐZMÉNY: ezen az ágon SEMMILYEN horizont-vágás nem futott - az
/// <c>EvaluateNodeForPriority</c> korai <c>return</c>-je miatt a meglévő,
/// kiterjedés-tudatos teszt soha nem hajtódott végre, ha a vetített nézet és
/// a terep-proxy is jelen van. Márpedig ez a PRODUKCIÓS ág (a PerfLog
/// `terrainProxy=True projectedTerrain=True` sora). Mérve: 3 egység
/// magasságban, ahol a látható sapka a gömb ~1,4%-a, a vágás 123 152 level-8
/// csomópontot járt be és 120 846-ot tett kupacra. A teljes 191 084
/// kiértékelésből 180 564 már a base-szint eléréséig megtörtént: 96% olyan
/// munka, ami egyetlen levelet sem termel.
///
/// A vágás NEM ad bitre azonos cutot: telített budgetnél eltolja a best-first
/// határát, mert a frontierről eltűnnek a láthatatlan csomópontok (54
/// próbanézetből 48 bitre azonos, 6 eltér). Ezért NEM halmaz-azonosságot
/// védünk, hanem azt, ami a felhasználónak számít: a LÁTHATÓ FEDETTSÉGET.
/// Az alábbi alapértékek a vágás ELŐTTI kódból mértek, és a vágás után
/// mindegyik PONTOSAN ugyanannyi maradt (54 nézeten összesen 6621/9561
/// finomított látható minta, változatlanul).
/// </summary>
public class HorizonCullTests
{
    private const double Radius = 100;
    private const double Sea = 100;
    private const double Peak = 102;
    private const double Fov = Math.PI / 3;
    private const double Aspect = 16.0 / 9;
    private const int BaseLevel = 8;
    private const int Columns = 21, Rows = 11;
    private static readonly double Split = 6 * Fov / 1080;
    private static readonly double Half =
        Math.Atan(Math.Tan(Fov / 2) * Math.Sqrt(1 + Aspect * Aspect)) * 1.3;

    /// <summary>Domborzatos proxy: a csúcsok átkukucskálhatnak a horizonton.</summary>
    private static TerrainLodProxy Proxy()
    {
        int n = 1 << BaseLevel, side = n + 1;
        var radii = new double[6 * side * side];
        for (int face = 0; face < 6; face++)
            for (int u = 0; u < side; u++)
                for (int v = 0; v < side; v++)
                {
                    double x = u / (double)n * 2 - 1, y = v / (double)n * 2 - 1;
                    double h = Math.Sin(7.3 * x + 1.1 * face) * Math.Cos(5.7 * y + 0.7 * face);
                    radii[face * side * side + u * side + v] = Sea + (Peak - Sea) * Math.Max(0, h);
                }
        return new TerrainLodProxy(BaseLevel, radii, Sea);
    }

    private readonly struct View
    {
        public readonly double Ax, Ay, Az, Distance;
        public readonly double Fx, Fy, Fz, Rx, Ry, Rz, Ux, Uy, Uz;

        public View(double distance, double tiltDegrees)
        {
            const double Angle1 = 0.3, Angle2 = 0.7;
            double ax = Math.Cos(Angle1) * Math.Cos(Angle2);
            double ay = Math.Sin(Angle1) * Math.Cos(Angle2);
            double az = Math.Sin(Angle2);
            double len = Math.Sqrt(ax * ax + ay * ay + az * az);
            Ax = ax / len; Ay = ay / len; Az = az / len; Distance = distance;

            double rx = -Ay, ry = Ax, rl = Math.Sqrt(rx * rx + ry * ry);
            if (rl < 1e-9) { rx = 1; ry = 0; rl = 1; }
            Rx = rx / rl; Ry = ry / rl; Rz = 0;
            Ux = Ry * Az; Uy = -Rx * Az; Uz = Rx * Ay - Ry * Ax;

            double c = Math.Cos(tiltDegrees * Math.PI / 180);
            double s = Math.Sin(tiltDegrees * Math.PI / 180);
            double fx = -Ax * c + Rx * s, fy = -Ay * c + Ry * s, fz = -Az * c;
            double fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            Fx = fx / fl; Fy = fy / fl; Fz = fz / fl;
        }

        public ProjectedLodView Projected() => new ProjectedLodView(
            new SurfacePoint(Ax * Distance, Ay * Distance, Az * Distance),
            new SurfacePoint(Rx, Ry, Rz), new SurfacePoint(Ux, Uy, Uz),
            new SurfacePoint(Fx, Fy, Fz), Fov, Aspect, .01, 10000);
    }

    private static HashSet<TileId> Cut(in View view, TerrainLodProxy proxy,
        out LodSelectionWork work, int budget = 20000)
    {
        work = new LodSelectionWork(view.Projected());
        return AdaptiveQuadTree.BuildCut(view.Ax * view.Distance, view.Ay * view.Distance,
            view.Az * view.Distance, Radius, Array.Empty<TileId>(), BaseLevel, 20,
            Split, Split / 1.5, view.Fx, view.Fy, view.Fz, Half, budget,
            traversalRootLevel: 3, staticBaseLevel: BaseLevel, terrainProxy: proxy, work: work);
    }

    /// <summary>
    /// AMI SZÁMÍT: a látható képernyőből ugyanannyi minta marad az alapszint
    /// FÖLÖTT finomítva, mint a vágás előtt. A várt értékek a vágás előtti
    /// kódból MÉRTEK; a "legalább" reláció azért kell, mert a felszabadult
    /// budget a látható részre kerülhet (egy nézetben ez meg is történt).
    ///
    /// A 100% nem elvárás és sosem volt az: 20 000 levél nem elég a teljes
    /// képernyő 8 px-es lefedéséhez, a periféria alapszinten marad.
    /// </summary>
    [Theory]
    [InlineData(103.0, 0.0, 223)]
    [InlineData(103.0, 25.0, 216)]
    [InlineData(110.0, 0.0, 228)]
    [InlineData(110.0, 25.0, 220)]
    [InlineData(120.0, 0.0, 230)]
    public void VisibleCoverageIsNoWorseThanBeforeCulling(
        double distance, double tilt, int refinedBefore)
    {
        var proxy = Proxy();
        var view = new View(distance, tilt);
        HashSet<TileId> cut = Cut(view, proxy, out _);

        int visible = 0, refined = 0;
        ForEachSurfaceSample(view, (x, y, z) =>
        {
            visible++;
            if (CoverageLevel(x, y, z, cut) > BaseLevel) refined++;
        });

        Assert.True(visible > 50, "Csak " + visible + " minta ert felszint - gyenge proba.");
        Assert.True(refined >= refinedBefore,
            "A lathato fedettseg romlott: " + refined + " finomitott minta a vagas elotti "
            + refinedBefore + " helyett (" + visible + " lathato mintabol).");
    }

    /// <summary>
    /// A vágás haszna, regressziós korláttal. Ugyanezek a nézetek a vágás
    /// ELŐTT 180 000-205 000 metrika-kiértékelést igényeltek, utána
    /// 26 900-28 300-at. Ha ez a szám visszakúszik, a horizont-szűrés megint
    /// kiesett a produkciós ágból - pontosan az a hiba, ami eddig fennállt.
    /// </summary>
    [Theory]
    [InlineData(103.0)]
    [InlineData(110.0)]
    [InlineData(120.0)]
    public void CullingRemovesMostOfTheDescentWork(double distance)
    {
        var proxy = Proxy();
        var view = new View(distance, 0.0);
        HashSet<TileId> cut = Cut(view, proxy, out LodSelectionWork work);

        Assert.NotEmpty(cut);
        Assert.True(work.MetricEvaluations < 60000,
            work.MetricEvaluations + " kiertekeles - a horizont-szures nem fut "
            + "(vagas elott 180 000-205 000, utana 26 900-28 300 volt).");
    }

    /// <summary>
    /// A vágás nem lehet mohó: a nadír környéke a legfinomabb rész, azt
    /// semmiképp nem szabad kivágni. (Az első, érintősíkos változatom pont
    /// ezt rontotta el a limbus közelében.)
    /// </summary>
    [Theory]
    [InlineData(103.0)]
    [InlineData(110.0)]
    [InlineData(120.0)]
    public void NadirStaysRefined(double distance)
    {
        var proxy = Proxy();
        var view = new View(distance, 0.0);
        HashSet<TileId> cut = Cut(view, proxy, out _);

        Assert.True(CoverageLevel(view.Ax, view.Ay, view.Az, cut) > BaseLevel,
            "A kamera alatti pont alapszinten maradt.");
    }

    /// <summary>Determinizmus: a vágás nem visz futásfüggő eltérést a cutba.</summary>
    [Fact]
    public void CullingIsDeterministic()
    {
        var proxy = Proxy();
        var view = new View(110.0, 25.0);
        HashSet<TileId> first = Cut(view, proxy, out _);
        HashSet<TileId> second = Cut(view, proxy, out _);

        Assert.True(first.SetEquals(second));
    }

    /// <summary>Egyenletes képernyő-raszter, a gömbre visszametszve.</summary>
    private static void ForEachSurfaceSample(in View view, Action<double, double, double> sample)
    {
        double tanY = Math.Tan(Fov / 2), tanX = tanY * Aspect;
        double sx = view.Ry * view.Fz - view.Rz * view.Fy;
        double sy = view.Rz * view.Fx - view.Rx * view.Fz;
        double sz = view.Rx * view.Fy - view.Ry * view.Fx;
        double cx = view.Ax * view.Distance, cy = view.Ay * view.Distance, cz = view.Az * view.Distance;

        for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                double u = (2 * (column + .5) / Columns - 1) * tanX;
                double v = (2 * (row + .5) / Rows - 1) * tanY;
                double dx = view.Fx + view.Rx * u + sx * v;
                double dy = view.Fy + view.Ry * u + sy * v;
                double dz = view.Fz + view.Rz * u + sz * v;
                double dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                dx /= dl; dy /= dl; dz /= dl;

                double b = cx * dx + cy * dy + cz * dz;
                double disc = b * b - (view.Distance * view.Distance - Radius * Radius);
                if (disc < 0) continue;                       // az eget nezi
                double t = -b - Math.Sqrt(disc);
                if (t < 0) continue;

                double hx = cx + dx * t, hy = cy + dy * t, hz = cz + dz * t;
                double hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                sample(hx / hl, hy / hl, hz / hl);
            }
    }

    private static int CoverageLevel(double x, double y, double z, HashSet<TileId> cut)
    {
        TileId tile = TileGeometry.FromPosition(x, y, z, 20);
        while (tile.Level > BaseLevel)
        {
            if (cut.Contains(tile)) return tile.Level;
            tile = tile.Parent();
        }
        return BaseLevel;
    }
}
