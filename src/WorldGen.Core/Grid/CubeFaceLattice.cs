using System;

namespace WorldGen.Core.Grid
{
    /// <summary>
    /// ATAN-MENTES, bitpontos térbeli hash-rács az egységgömbön.
    ///
    /// MIÉRT KELL (ND-186). A <see cref="TileGeometry.FromPosition"/> a
    /// tan-torzított kocka-lap koordinátát használja, tehát
    /// <see cref="Math.Atan"/>-t hív. Az ND-23b/ND-24 döntés szerint ez NEM
    /// bitpontos platformok között, és EZÉRT nem használható a szimuláció
    /// kritikus útján futásidőben. A folytonos folyó-nyomkövető viszont
    /// pontosan ezt tette: a hurok-védelem (`pathVisited`) és az összefolyás
    /// (`claimed`) MINDEN lépésben `FromPosition`-t hívott - azaz a kirajzolt
    /// folyóhálózat topológiája egy nem bitpontos függvényen múlt.
    ///
    /// A MEGOLDÁS. Térbeli hasheléshez NINCS szükség terület-kiegyenlített
    /// cellákra: a NYERS kocka-projekció (domináns tengely + két osztás)
    /// ugyanolyan jó bucket-rács, és KIZÁRÓLAG osztást, összeadást és
    /// összehasonlítást használ, tehát IEEE-754 szerint bitpontos. A cellák
    /// mérete a lapon belül változik (a lap közepén kb. kétszer nagyobb
    /// szögben, mint a lap szélén) - egy hash-rácsnak ez irreleváns, a
    /// GARANTÁLT ALSÓ korlátot (<see cref="MinCellMeters"/>) viszont
    /// pontosan ismerjük, és erre épül a <see cref="Hydrology.ClaimedRiverPoints"/>
    /// lefedettségi érvelése.
    ///
    /// A lap-konvenció (melyik tengely a normális/jobbra/felfele) SZÁNDÉKOSAN
    /// a <see cref="TileGeometry"/> tábláiból jön, hogy a két rács ugyanazt a
    /// hat lapot ugyanúgy számozza.
    ///
    /// Python-referencia: tools/reference/river_continuous_ref.py
    /// (`lattice_cell`).
    /// </summary>
    public static class CubeFaceLattice
    {
        /// <summary>
        /// A legnagyobb támogatott szint. A kulcs-csomagolás 3 + 21 + 21 bitet
        /// használ, tehát szint 20-ig (n = 1 048 576 &lt; 2^21) biztosan elfér,
        /// és a szomszéd-indexek (i ± 1) túlcsordulás nélkül ábrázolhatók.
        /// </summary>
        public const int MaxLevel = 20;

        private const int Bits = 21;
        private const long Mask = (1L << Bits) - 1L;

        /// <summary>
        /// A cellák GARANTÁLT alsó mérethatára méterben az adott szinten.
        ///
        /// A lap `a` koordinátája [-1, 1]-en fut, és a hozzá tartozó szög
        /// atan(a), amelynek deriváltja 1/(1 + a²) - ez |a| = 1-nél 1/2,
        /// ez a minimum. Egy cella `a`-ban 2/n széles, tehát a legkisebb
        /// szögmérete 1/n radián, azaz R/n méter.
        /// </summary>
        public static double MinCellMeters(int level) =>
            PlanetConstants.RadiusMeters / (1L << level);

        /// <summary>
        /// Egységvektor -> (lap, i, j) bucket-index. A `level` 0 és
        /// <see cref="MaxLevel"/> között lehet.
        /// </summary>
        public static void FromPosition(
            double x, double y, double z, int level, out int face, out int i, out int j)
        {
            if (level < 0 || level > MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));

            double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
            int dominant = 0;
            double dominantAbs = ax;
            if (ay > dominantAbs) { dominant = 1; dominantAbs = ay; }
            if (az > dominantAbs) { dominant = 2; dominantAbs = az; }

            double dominantValue = dominant == 0 ? x : dominant == 1 ? y : z;
            int dominantSign = dominantValue >= 0.0 ? 1 : -1;

            face = -1;
            for (int f = 0; f < 6; f++)
            {
                if (TileGeometry.NormalAxis[f] == dominant && TileGeometry.NormalSign[f] == dominantSign)
                {
                    face = f;
                    break;
                }
            }
            if (face < 0)
                throw new ArgumentException("Nem sikerült lapot azonosítani a pozícióból.");

            double scale = 1.0 / dominantAbs;
            double right = Component(x, y, z, TileGeometry.RightAxis[face]) * scale * TileGeometry.RightSign[face];
            double up = Component(x, y, z, TileGeometry.UpAxis[face]) * scale * TileGeometry.UpSign[face];

            long n = 1L << level;
            i = ClampIndex((long)((right + 1.0) * 0.5 * n), n);
            j = ClampIndex((long)((up + 1.0) * 0.5 * n), n);
        }

        /// <summary>
        /// (lap, i, j) -> egyetlen `long` szótár-kulcs. A `i`/`j` kívül eshet a
        /// [0, 2^level) tartományon (szomszéd-keresésnél) - a kulcs akkor is
        /// egyértelmű, csak nem fog létező cellára illeszkedni.
        /// </summary>
        public static long Key(int face, int i, int j) =>
            ((long)face << (2 * Bits)) | ((long)(i & Mask) << Bits) | (long)(j & Mask);

        /// <summary>Egységvektor -> szótár-kulcs egy lépésben.</summary>
        public static long KeyFromPosition(double x, double y, double z, int level)
        {
            FromPosition(x, y, z, level, out int face, out int i, out int j);
            return Key(face, i, j);
        }

        private static double Component(double x, double y, double z, int axis) =>
            axis == 0 ? x : axis == 1 ? y : z;

        private static int ClampIndex(long value, long n)
        {
            if (value < 0L) return 0;
            if (value > n - 1L) return (int)(n - 1L);
            return (int)value;
        }
    }
}
