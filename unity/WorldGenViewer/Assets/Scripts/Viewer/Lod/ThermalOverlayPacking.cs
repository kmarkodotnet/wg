using System;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// A pillanatnyi hőmező megjelenítési adatútja (ND-104), UnityEngine-függés
    /// nélkül, hogy .NET-ből tesztelhető legyen.
    ///
    /// <list type="bullet">
    /// <item>Atlasz: hat lap egymás mellett, lapanként 66×66 texel (64×64 cella
    /// + egycellás gutter). A gutter a szomszédos cella értékét kapja
    /// (<see cref="TileNeighbors"/>), így a bilineáris mintavétel a
    /// kockalap-éleken sem kever idegen értéket; a négy gutter-sarok a legközelebbi
    /// belső cellát kapja.</item>
    /// <item>Kvantálás: <c>q = floor((K − 150) / 0,01 + 0,5)</c>, 0…65535-re
    /// vágva — a prezentációs textúra byte-reprodukálható; a GPU csak
    /// interpolál és palettáz.</item>
    /// <item><see cref="AtlasCoordinate"/> a shader <c>ThermalAtlasUv</c>
    /// függvényének CPU-tükre (prezentáció, nem szimuláció).</item>
    /// </list>
    /// </summary>
    public static class ThermalOverlayPacking
    {
        public const int Level = 6;
        public const int Side = 1 << Level;
        public const int FaceTexels = Side + 2;
        public const int AtlasWidth = 6 * FaceTexels;
        public const int AtlasHeight = FaceTexels;
        public const double QuantOffsetK = 150.0;
        public const double QuantStepK = 0.01;
        public const double ZeroCelsiusK = 273.15;

        /// <summary>Minden atlasz-texelhez a forrás cella kanonikus indexe (sorfolytonosan, alulról).</summary>
        public static int[] BuildTexelSourceMap()
        {
            var map = new int[AtlasWidth * AtlasHeight];
            for (int face = 0; face < 6; face++)
                for (int ty = 0; ty < FaceTexels; ty++)
                    for (int tx = 0; tx < FaceTexels; tx++)
                        map[ty * AtlasWidth + face * FaceTexels + tx] = SourceCell(face, tx - 1, ty - 1);
            return map;
        }

        private static int SourceCell(int face, int u, int v)
        {
            bool uOut = u < 0 || u >= Side;
            bool vOut = v < 0 || v >= Side;
            int cu = u < 0 ? 0 : (u >= Side ? Side - 1 : u);
            int cv = v < 0 ? 0 : (v >= Side ? Side - 1 : v);
            if (!uOut && !vOut)
                return DenseGridMetrics.Index(face, u, v, Side);
            if (uOut && vOut)
                return DenseGridMetrics.Index(face, cu, cv, Side);

            TileDirection direction = u < 0 ? TileDirection.Left
                : u >= Side ? TileDirection.Right
                : v < 0 ? TileDirection.Down
                : TileDirection.Up;
            TileId neighbor = TileNeighbors.Neighbor(TileId.FromFaceLevelUV(face, Level, (uint)cu, (uint)cv), direction);
            neighbor.GetUV(out uint nu, out uint nv);
            return DenseGridMetrics.Index(neighbor.Face, (int)nu, (int)nv, Side);
        }

        public static ushort Quantize(double kelvin)
        {
            if (double.IsNaN(kelvin))
                return 0;
            double q = Math.Floor((kelvin - QuantOffsetK) / QuantStepK + 0.5);
            if (q <= 0.0) return 0;
            if (q >= ushort.MaxValue) return ushort.MaxValue;
            return (ushort)q;
        }

        public static double Decode(ushort quantized) => QuantOffsetK + quantized * QuantStepK;

        /// <summary>Az atlasz kitöltése a cellánkénti Kelvin-értékekből.</summary>
        public static void Pack(int[] texelSourceMap, double[] valuesK, ushort[] target)
        {
            if (texelSourceMap == null || valuesK == null || target == null)
                throw new ArgumentNullException(texelSourceMap == null ? nameof(texelSourceMap) : valuesK == null ? nameof(valuesK) : nameof(target));
            if (texelSourceMap.Length != AtlasWidth * AtlasHeight || target.Length != texelSourceMap.Length)
                throw new ArgumentException("Az atlasz mérete 396×66 texel.");
            for (int i = 0; i < texelSourceMap.Length; i++)
                target[i] = Quantize(valuesK[texelSourceMap[i]]);
        }

        /// <summary>
        /// Core-irány → lap és normalizált atlasz-koordináta (0..1), a shader
        /// <c>ThermalAtlasUv</c> képletével. A cellaközép a texelközépre esik.
        /// </summary>
        public static void AtlasCoordinate(double x, double y, double z, out int face, out double atlasU, out double atlasV)
        {
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (!(len > 0.0))
                throw new ArgumentException("Nullvektorhoz nincs atlasz-koordináta.");
            x /= len; y /= len; z /= len;
            double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
            int axis = 0;
            if (ay > ax) axis = 1;
            if (az > (axis == 0 ? ax : ay)) axis = 2;
            double dominant = axis == 0 ? x : (axis == 1 ? y : z);
            face = axis * 2 + (dominant >= 0.0 ? 0 : 1);
            double inv = 1.0 / Math.Abs(dominant);
            double wx, wy;
            switch (face)
            {
                case 0: wx = -z; wy = y; break;
                case 1: wx = z; wy = y; break;
                case 2: wx = x; wy = z; break;
                case 3: wx = x; wy = -z; break;
                case 4: wx = x; wy = y; break;
                default: wx = -x; wy = y; break;
            }
            double uc = Math.Atan(wx * inv) * 4.0 / Math.PI;
            double vc = Math.Atan(wy * inv) * 4.0 / Math.PI;
            atlasU = (FaceTexels * face + 1.0 + (uc + 1.0) * 0.5 * Side) / AtlasWidth;
            atlasV = (1.0 + (vc + 1.0) * 0.5 * Side) / AtlasHeight;
        }

        /// <summary>A shader <c>ThermalPalette</c> tükre (jelmagyarázathoz).</summary>
        public static void Palette(double kelvin, double minK, double maxK, out float r, out float g, out float b)
        {
            double[] cold0 = { 0.08, 0.16, 0.62 }, cold1 = { 0.20, 0.72, 0.92 }, neutral = { 0.93, 0.93, 0.89 };
            double[] warm1 = { 0.98, 0.80, 0.24 }, warm0 = { 0.78, 0.12, 0.08 };
            double[] from, to;
            double t;
            if (kelvin <= ZeroCelsiusK)
            {
                double s = Saturate((kelvin - minK) / Math.Max(ZeroCelsiusK - minK, 1e-3));
                if (s < 0.5) { from = cold0; to = cold1; t = s * 2.0; }
                else { from = cold1; to = neutral; t = s * 2.0 - 1.0; }
            }
            else
            {
                double w = Saturate((kelvin - ZeroCelsiusK) / Math.Max(maxK - ZeroCelsiusK, 1e-3));
                if (w < 0.5) { from = neutral; to = warm1; t = w * 2.0; }
                else { from = warm1; to = warm0; t = w * 2.0 - 1.0; }
            }
            r = (float)(from[0] + (to[0] - from[0]) * t);
            g = (float)(from[1] + (to[1] - from[1]) * t);
            b = (float)(from[2] + (to[2] - from[2]) * t);
        }

        private static double Saturate(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
