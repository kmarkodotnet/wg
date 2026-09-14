using System;

namespace WorldGen.Core.Grid
{
    /// <summary>
    /// Egy teljes, sűrű cubed-sphere rácsszint véges térfogatú metrikája
    /// (ND-101, 3. nyitott kérdés, B javaslat; referencia:
    /// tools/reference/thermal_field_ref.py, <c>Grid</c>).
    ///
    /// Kanonikus cellaindex: <c>face · side² + u · side + v</c>. A cellaközép a
    /// <see cref="TileGeometry.ToPosition"/>, a sarkok a tan-warp vetület
    /// (ND-24) pontjai. A terület a két síkháromszögből álló húrsokszög,
    /// a teljes gömbre normalizálva; az élhossz húrhossz. A mért eltérés a
    /// pontos gömbi értéktől level 6-on ≤ 5,6·10⁻⁵ (terület) és
    /// ≤ 2,5·10⁻⁵ (élhossz). A sarkok az ND-24 szerint konstrukciós adatok:
    /// a metrika egyszer épül, a szimuláció csak olvassa.
    ///
    /// Élek: cellánként Right, Left, Up, Down sorrendben, csak ha a szomszéd
    /// indexe nagyobb; a sarokpontok az alacsonyabb indexű cella oldaláról
    /// jönnek, a normál a nagyobb indexű cella felé mutat.
    /// </summary>
    public sealed class DenseGridMetrics
    {
        public int Level { get; }
        public int Side { get; }
        public int CellCount { get; }
        public int EdgeCount { get; }
        public double RadiusMeters { get; }

        /// <summary>Cellaközép egységvektora (Core-koordináta).</summary>
        public double[] CenterX { get; }
        public double[] CenterY { get; }
        public double[] CenterZ { get; }

        /// <summary>Normalizált cellaterület m²-ben.</summary>
        public double[] Area { get; }

        /// <summary>Az él két cellája, <c>EdgeI[e] &lt; EdgeJ[e]</c>.</summary>
        public int[] EdgeI { get; }
        public int[] EdgeJ { get; }

        /// <summary>Húr-élhossz méterben.</summary>
        public double[] EdgeLength { get; }

        /// <summary>Élközéppont egységvektora.</summary>
        public double[] EdgeMidX { get; }
        public double[] EdgeMidY { get; }
        public double[] EdgeMidZ { get; }

        /// <summary>Egységnyi élnormál az I cellától a J cella felé.</summary>
        public double[] EdgeNormalX { get; }
        public double[] EdgeNormalY { get; }
        public double[] EdgeNormalZ { get; }

        private DenseGridMetrics(int level, double radiusMeters, int cellCount, int edgeCount)
        {
            Level = level;
            Side = 1 << level;
            CellCount = cellCount;
            EdgeCount = edgeCount;
            RadiusMeters = radiusMeters;
            CenterX = new double[cellCount];
            CenterY = new double[cellCount];
            CenterZ = new double[cellCount];
            Area = new double[cellCount];
            EdgeI = new int[edgeCount];
            EdgeJ = new int[edgeCount];
            EdgeLength = new double[edgeCount];
            EdgeMidX = new double[edgeCount];
            EdgeMidY = new double[edgeCount];
            EdgeMidZ = new double[edgeCount];
            EdgeNormalX = new double[edgeCount];
            EdgeNormalY = new double[edgeCount];
            EdgeNormalZ = new double[edgeCount];
        }

        public static int Index(int face, int u, int v, int side) => face * side * side + u * side + v;

        public static DenseGridMetrics Build(int level, double radiusMeters = PlanetConstants.RadiusMeters)
        {
            if (level < 1 || level > 12)
                throw new ArgumentOutOfRangeException(nameof(level), "A sűrű rácsmetrika 1 és 12 közötti szintre épül.");
            if (!(radiusMeters > 0.0))
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));

            int n = 1 << level;
            int cellCount = 6 * n * n;
            // Minden cellának 4 szomszédja van, minden él pontosan két cellához tartozik.
            var grid = new DenseGridMetrics(level, radiusMeters, cellCount, 2 * cellCount);

            var sideA = new double[grid.EdgeCount * 3];
            var sideB = new double[grid.EdgeCount * 3];
            int edge = 0;
            double rawTotal = 0.0;
            Span<double> corner = stackalloc double[12];

            for (int face = 0; face < 6; face++)
            {
                for (int u = 0; u < n; u++)
                {
                    for (int v = 0; v < n; v++)
                    {
                        int idx = Index(face, u, v, n);
                        Corner(face, n, u, v, corner, 0);
                        Corner(face, n, u + 1, v, corner, 3);
                        Corner(face, n, u + 1, v + 1, corner, 6);
                        Corner(face, n, u, v + 1, corner, 9);

                        var id = TileId.FromFaceLevelUV(face, level, (uint)u, (uint)v);
                        TileGeometry.ToPosition(id, out grid.CenterX[idx], out grid.CenterY[idx], out grid.CenterZ[idx]);

                        double area = 0.5 * (TriangleDoubleArea(corner, 0, 3, 6) + TriangleDoubleArea(corner, 0, 6, 9));
                        grid.Area[idx] = area;
                        rawTotal += area;

                        for (int d = 0; d < 4; d++)
                        {
                            TileId nb = TileNeighbors.Neighbor(id, (TileDirection)d);
                            nb.GetUV(out uint nu, out uint nv);
                            int j = Index(nb.Face, (int)nu, (int)nv, n);
                            if (idx >= j)
                                continue;
                            if (edge >= grid.EdgeCount)
                                throw new InvalidOperationException("A szomszédsági gráf több élt adott a vártnál.");

                            // right: (p10, p11), left: (p00, p01), up: (p01, p11), down: (p00, p10)
                            int a, b;
                            switch (d)
                            {
                                case 0: a = 3; b = 6; break;
                                case 1: a = 0; b = 9; break;
                                case 2: a = 9; b = 6; break;
                                default: a = 0; b = 3; break;
                            }
                            for (int k = 0; k < 3; k++)
                            {
                                sideA[edge * 3 + k] = corner[a + k];
                                sideB[edge * 3 + k] = corner[b + k];
                            }
                            grid.EdgeI[edge] = idx;
                            grid.EdgeJ[edge] = j;
                            edge++;
                        }
                    }
                }
            }

            if (edge != grid.EdgeCount)
                throw new InvalidOperationException($"A szomszédsági gráf {edge} élt adott, {grid.EdgeCount} helyett.");

            double areaScale = 4.0 * Math.PI / rawTotal * radiusMeters * radiusMeters;
            for (int c = 0; c < cellCount; c++)
                grid.Area[c] = grid.Area[c] * areaScale;

            for (int e = 0; e < grid.EdgeCount; e++)
            {
                double ax = sideA[e * 3], ay = sideA[e * 3 + 1], az = sideA[e * 3 + 2];
                double bx = sideB[e * 3], by = sideB[e * 3 + 1], bz = sideB[e * 3 + 2];

                Normalize((ax + bx) * 0.5, (ay + by) * 0.5, (az + bz) * 0.5, out double mx, out double my, out double mz);

                double dx = bx - ax, dy = by - ay, dz = bz - az;
                Normalize(dy * mz - dz * my, dz * mx - dx * mz, dx * my - dy * mx, out double nx, out double ny, out double nz);

                int i = grid.EdgeI[e], j = grid.EdgeJ[e];
                double tx = grid.CenterX[j] - grid.CenterX[i];
                double ty = grid.CenterY[j] - grid.CenterY[i];
                double tz = grid.CenterZ[j] - grid.CenterZ[i];
                if (nx * tx + ny * ty + nz * tz < 0.0)
                {
                    nx = -nx; ny = -ny; nz = -nz;
                }

                grid.EdgeLength[e] = Math.Sqrt(dx * dx + dy * dy + dz * dz) * radiusMeters;
                grid.EdgeMidX[e] = mx; grid.EdgeMidY[e] = my; grid.EdgeMidZ[e] = mz;
                grid.EdgeNormalX[e] = nx; grid.EdgeNormalY[e] = ny; grid.EdgeNormalZ[e] = nz;
            }

            return grid;
        }

        private static void Corner(int face, int n, int i, int j, Span<double> target, int offset)
        {
            TileGeometry.PositionFromFaceUV(face, (double)i / n * 2.0 - 1.0, (double)j / n * 2.0 - 1.0,
                out target[offset], out target[offset + 1], out target[offset + 2]);
        }

        /// <summary>|(p1 − p0) × (p2 − p0)|, a Python-referenciával azonos műveleti sorrendben.</summary>
        private static double TriangleDoubleArea(Span<double> p, int i0, int i1, int i2)
        {
            double ax = p[i1] - p[i0], ay = p[i1 + 1] - p[i0 + 1], az = p[i1 + 2] - p[i0 + 2];
            double bx = p[i2] - p[i0], by = p[i2 + 1] - p[i0 + 1], bz = p[i2 + 2] - p[i0 + 2];
            double cx = ay * bz - az * by;
            double cy = az * bx - ax * bz;
            double cz = ax * by - ay * bx;
            return Math.Sqrt(cx * cx + cy * cy + cz * cz);
        }

        private static void Normalize(double x, double y, double z, out double nx, out double ny, out double nz)
        {
            double inv = 1.0 / Math.Sqrt(x * x + y * y + z * z);
            nx = x * inv;
            ny = y * inv;
            nz = z * inv;
        }
    }
}
