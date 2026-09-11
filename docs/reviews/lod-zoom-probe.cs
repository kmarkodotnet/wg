using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using WorldGen.Viewer.Lod;

// Diagnosztikai próba, nem szimulációs modul és nem Unity-renderteszt.
// A cut/chunk kód linkelt; a geometriai mintavétel double referencia-
// számítás. A hiányzó zajréteg hatását külön mérjük, GPU-futtatás nélkül.
internal static class Program
{
    private const double R = 100;
    private const double Fov = Math.PI / 3;
    private const double Aspect = 16.0 / 9;
    private const ulong Seed = 184482873278464;
    private static readonly (double X, double Y, double Z)[] Seeds = PlateMotion.MovedSeeds(Seed, PlateGeneration.GenerateSeeds(Seed, 20), 0);
    private static readonly double Sea = SeaLevelCalibration.CalibrateSeaLevel(SeaLevelCalibration.ComputeElevationFieldAtTime(Seed, 20, 5, 0).Values, 0.65);
    private static readonly V Axis = new V(0.017, -0.937, 0.349).Unit;

    private static HashSet<TileId> Cut(double d, double height = 1080, int budget = 200000, HashSet<TileId> previous = null, double pixels = 12)
    {
        V c = Axis * d;
        double split = pixels / 2 * Fov / height;
        double half = Math.Atan(Math.Tan(Fov / 2) * Math.Sqrt(1 + Aspect * Aspect)) * 1.3;
        return AdaptiveQuadTree.BuildCut(c.X, c.Y, c.Z, R, previous, 8, 20, split, split / 1.5,
            -Axis.X, -Axis.Y, -Axis.Z, half, budget, traversalRootLevel: 3, staticBaseLevel: 8);
    }

    private static int Cover(V p, HashSet<TileId> cut)
    {
        TileId tile = TileGeometry.FromPosition(p.X, p.Y, p.Z, 20);
        while (tile.Level > 8) { if (cut.Contains(tile)) return tile.Level; tile = tile.Parent(); }
        return 8;
    }

    private static List<V> ScreenPoints(double d)
    {
        V camera = Axis * d, forward = Axis * -1;
        V right = V.Cross(forward, new V(0, 0, 1)).Unit;
        V up = V.Cross(right, forward).Unit;
        var points = new List<V>();
        for (int y = 0; y < 23; y++) for (int x = 0; x < 41; x++)
        {
            V dir = (forward + right * ((2 * (x + 0.5) / 41 - 1) * Aspect * Math.Tan(Fov / 2))
                + up * ((2 * (y + 0.5) / 23 - 1) * Math.Tan(Fov / 2))).Unit;
            double b = V.Dot(camera, dir), disc = b * b - (d * d - R * R);
            if (disc >= 0) points.Add((camera + dir * (-b - Math.Sqrt(disc))).Unit);
        }
        return points;
    }

    private static double Elevation(V p)
    {
        DomainWarp.WarpPosition(Seed, p.X, p.Y, p.Z, out double wx, out double wy, out double wz);
        int plate = PlateGeneration.AssignPlate(wx, wy, wz, Seeds);
        return CrustElevation.BaseElevation(Seed, plate, p.X, p.Y, p.Z, out _)
            + PlateBoundaryEffect.BoundaryUpliftFromWarped(Seed, p.X, p.Y, p.Z, wx, wy, wz, Seeds);
    }
    private static V Point(int face, double u, double v)
    { TileGeometry.PositionFromFaceUV(face, u, v, out double x, out double y, out double z); return new V(x, y, z); }
    private static V Displace(V p) => p * (R + (Sea + (Elevation(p) - Sea) * 1.5) * 0.001);

    private static double RayTriangle(V dir, V a, V b, V c)
    {
        V e1 = b - a, e2 = c - a, h = V.Cross(dir, e2);
        double det = V.Dot(e1, h);
        if (Math.Abs(det) < 1e-12) return double.NaN;
        double f = 1 / det;
        V s = a * -1;
        double u = f * V.Dot(s, h);
        V q = V.Cross(s, e1);
        double v = f * V.Dot(dir, q);
        if (u < -1e-8 || v < -1e-8 || u + v > 1 + 1e-8) return double.NaN;
        return f * V.Dot(e2, q);
    }

    private static void Main()
    {
        Console.WriteLine($"PROBE .NET {Environment.Version}; ideal sphere camera, aspect={Aspect:F6}; scene seed={Seed}; sea={Sea:F9}");
        Cut(300);
        foreach (double height in new[] { 688.0, 1080.0, 2160.0 })
        foreach (double d in new[] { 300.0, 160, 145, 130, 110, 103, 101.1, 100.1 })
        {
            var sw = Stopwatch.StartNew(); var cut = Cut(d, height); sw.Stop();
            var points = ScreenPoints(d);
            Console.WriteLine($"CUT H={height} d={d:F3} count={cut.Count} maxL={(cut.Count == 0 ? 8 : cut.Max(t => t.Level))} sphereScreenBase={points.Count(p => Cover(p, cut) == 8)}/{points.Count} dotnetCutMs={sw.Elapsed.TotalMilliseconds:F1}");
        }
        var full = Cut(100.6);
        foreach (int budget in new[] { 4000, 25000, 50000, 200000 })
        {
            var limited = Cut(100.6, budget: budget);
            var points = ScreenPoints(100.6);
            int dropped = points.Count(p => Cover(p, full) > 8 && Cover(p, limited) == 8);
            Console.WriteLine($"BUDGET {budget} count={limited.Count} droppedToBase={dropped}/{points.Count} nadirL={Cover(Axis, limited)}");
        }
        var prev = Cut(101.1);
        var next = Cut(100.9, previous: prev);
        foreach (int level in new[] { 8, 11, 13 })
        {
            var a = DynamicMeshChunking.GroupByChunk(prev, level);
            var b = DynamicMeshChunking.GroupByChunk(next, level);
            var diff = DynamicMeshChunking.DiffChunks(a, b);
            int changedLeaves = diff.ChangedOrNewChunks.Sum(t => b[t].Count);
            Console.WriteLine($"CHUNK L={level} groups={b.Count} maxLeaves={b.Values.Max(t => t.Count)} changedGroups={diff.ChangedOrNewChunks.Count} unchanged={diff.UnchangedChunkCount} changedLeaves={changedLeaves}/{next.Count}");
            var changed = new HashSet<TileId>(diff.ChangedOrNewChunks);
            double maxAlphaDelta = 0;
            int stale = 0;
            foreach (var group in b.Where(g => !changed.Contains(g.Key))) foreach (var tile in group.Value)
            {
                double delta = Math.Abs(Alpha(tile, 101.1) - Alpha(tile, 100.9));
                if (delta > 0.000001) stale++;
                maxAlphaDelta = Math.Max(maxAlphaDelta, delta);
            }
            Console.WriteLine($"MORPH L={level} unchangedChunkLeavesWithChangedAlpha={stale} maxAlphaDelta={maxAlphaDelta:F6}");
        }
        int land = 0, hidden = 0, oceanParent = 0, mixed = 0, lostLand = 0, gainedLand = 0;
        var deficits = new List<double>();
        string example = "";
        for (int face = 0; face < 6; face++) for (uint u = 0; u < 256; u += 8) for (uint v = 0; v < 256; v += 8)
        {
            TileId tile = TileId.FromFaceLevelUV(face, 8, u, v);
            TileGeometry.GetContinuousBounds(tile, out double u0, out double u1, out double v0, out double v1);
            V center = Point(face, (u0 + u1) / 2, (v0 + v1) / 2);
            V[] dirs = { Point(face,u0,v0), Point(face,u1,v0), Point(face,u1,v1), Point(face,u0,v1) };
            double ec = Elevation(center);
            DomainWarp.WarpPosition(Seed, center.X, center.Y, center.Z, out double wx, out double wy, out double wz);
            int plate = PlateGeneration.AssignPlate(wx, wy, wz, Seeds);
            double amplitude = CrustElevation.SecondaryNoiseAmplitudeMeters * (CrustElevation.IsOceanic(Seed, plate) ? CrustElevation.OceanicNoiseFactor : 1);
            double withoutSecondary = ec - CrustElevation.SecondaryDetailNoise(Seed, center.X, center.Y, center.Z) * amplitude;
            if (ec >= Sea && withoutSecondary < Sea) lostLand++;
            if (ec < Sea && withoutSecondary >= Sea) gainedLand++;
            if (ec < Sea) { oceanParent++; if (dirs.Any(p => Elevation(p) >= Sea)) mixed++; continue; }
            land++;
            V[] corners = dirs.Select(Displace).ToArray();
            double rb = RayTriangle(center, corners[0], corners[1], corners[2]);
            if (double.IsNaN(rb)) rb = RayTriangle(center, corners[0], corners[2], corners[3]);
            double rf = Displace(center).Length + 0.002;
            if (rb > rf)
            {
                hidden++; deficits.Add(rb - rf);
                if (example == "" && rb - rf > 0.02) example = $"face={face} L8 u={u} v={v} baseRadius={rb:F9} fineRadiusWithBias={rf:F9} delta={rb-rf:F9}";
            }
        }
        deficits.Sort();
        Console.WriteLine($"OCCLUSION sampledBase=6144 landCenters={land} fineCenterBelowBase={hidden} fraction={(double)hidden/land:P2} excessP50={deficits[deficits.Count/2]:F9} excessMax={deficits.Last():F9}");
        Console.WriteLine($"OCCLUSION EXAMPLE {example}");
        Console.WriteLine($"COAST oceanBaseCenters={oceanParent} withLandCorner={mixed}");
        Console.WriteLine($"MODEL_MISMATCH CPU secondary omitted only (NOT GPU execution): lostLand={lostLand}/{land} gainedLand={gainedLand}/{oceanParent}");
    }

    private static double Alpha(TileId tile, double distance)
    {
        AdaptiveQuadTree.GetCenterAndBoundingRadius(tile.Parent(), R, out double x, out double y, out double z, out double r);
        double splitDistance = r / Math.Tan(6 * Fov / 1080);
        return Math.Clamp((splitDistance - (Axis * distance - new V(x,y,z)).Length) / (0.6 * splitDistance), 0, 1);
    }

    private readonly record struct V(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt(Dot(this, this));
        public V Unit => this * (1 / Length);
        public static V operator +(V a, V b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static V operator -(V a, V b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static V operator *(V a, double s) => new(a.X*s,a.Y*s,a.Z*s);
        public static double Dot(V a, V b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        public static V Cross(V a, V b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    }
}
