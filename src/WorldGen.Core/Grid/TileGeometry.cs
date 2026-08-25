using System;
using System.Runtime.CompilerServices;

namespace WorldGen.Core.Grid
{
    /// <summary>
    /// TileId és a gömb felszínén levő 3D pozíció közötti leképezés.
    ///
    /// A vetítés "érintő" (tan) warp (ND-24): a lapon belüli folytonos
    /// [-1,1] koordinátát tan(t*pi/4)-gyel torzítjuk a tile-területek
    /// kiegyenlítéséhez (ld. tools/reference/cubed_sphere_ref.py).
    ///
    /// FIGYELEM (ND-23b/ND-24): a Math.Tan/Math.Atan NEM garantáltan
    /// bitpontos platformok között. Az ND-24 döntése szerint ez a
    /// leképezés KONSTRUKCIÓS (baked): egyszer számítandó ki, verzióhoz
    /// kötve/hash-elve tárolandó, és a szimuláció ebből OLVAS, nem
    /// újraszámolja. Ne használd a szimuláció kritikus útján futásidőben,
    /// ismételt/platformfüggő újraszámításban.
    /// </summary>
    public static class TileGeometry
    {
        // (normalAxis, normalSign, rightAxis, rightSign, upAxis, upSign) - lapanként,
        // 0=X, 1=Y, 2=Z. A konvenció a projekt saját, önkényes de következetes
        // választása - nem külső szabványhoz igazodik.
        private static readonly int[] NormalAxis = { 0, 0, 1, 1, 2, 2 };
        private static readonly int[] NormalSign = { 1, -1, 1, -1, 1, -1 };
        private static readonly int[] RightAxis = { 2, 2, 0, 0, 0, 0 };
        private static readonly int[] RightSign = { -1, 1, 1, 1, 1, -1 };
        private static readonly int[] UpAxis = { 1, 1, 2, 2, 1, 1 };
        private static readonly int[] UpSign = { 1, 1, 1, -1, 1, 1 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double WarpTan(double t) => Math.Tan(t * Math.PI / 4.0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double UnwarpTan(double s) => Math.Atan(s) * 4.0 / Math.PI;

        /// <summary>A tile ID -> a tile KÖZEPÉNEK egységvektora a gömbön.</summary>
        public static void ToPosition(TileId id, out double x, out double y, out double z)
        {
            id.GetUV(out uint u, out uint v);
            int level = id.Level;
            long n = 1L << level;

            double uc = (u + 0.5) / n * 2.0 - 1.0;
            double vc = (v + 0.5) / n * 2.0 - 1.0;

            int face = id.Face;
            double wx = WarpTan(uc);
            double wy = WarpTan(vc);

            Span<double> p = stackalloc double[3];
            p[NormalAxis[face]] = NormalSign[face];
            p[RightAxis[face]] += wx * RightSign[face];
            p[UpAxis[face]] += wy * UpSign[face];

            double length = Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
            x = p[0] / length;
            y = p[1] / length;
            z = p[2] / length;
        }

        /// <summary>Egységvektor -> a hozzá tartozó TileId adott LOD-szinten.</summary>
        public static TileId FromPosition(double x, double y, double z, int level)
        {
            Span<double> p = stackalloc double[3] { x, y, z };

            int dominant = 0;
            if (Math.Abs(p[1]) > Math.Abs(p[dominant])) dominant = 1;
            if (Math.Abs(p[2]) > Math.Abs(p[dominant])) dominant = 2;
            int dominantSign = p[dominant] >= 0 ? 1 : -1;

            int face = -1;
            for (int f = 0; f < 6; f++)
            {
                if (NormalAxis[f] == dominant && NormalSign[f] == dominantSign)
                {
                    face = f;
                    break;
                }
            }
            if (face < 0)
                throw new ArgumentException("Nem sikerült lapot azonosítani a pozícióból.");

            double scale = 1.0 / Math.Abs(p[NormalAxis[face]]);
            double pnRight = p[RightAxis[face]] * scale;
            double pnUp = p[UpAxis[face]] * scale;

            double warpedX = pnRight * RightSign[face];
            double warpedY = pnUp * UpSign[face];
            double uc = UnwarpTan(warpedX);
            double vc = UnwarpTan(warpedY);

            long n = 1L << level;
            long u = (long)((uc + 1.0) / 2.0 * n);
            long v = (long)((vc + 1.0) / 2.0 * n);
            u = Math.Max(0, Math.Min(n - 1, u));
            v = Math.Max(0, Math.Min(n - 1, v));

            return TileId.FromFaceLevelUV(face, level, (uint)u, (uint)v);
        }
    }
}
