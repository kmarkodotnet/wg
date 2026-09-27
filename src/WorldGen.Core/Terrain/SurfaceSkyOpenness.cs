using System;
using WorldGen.Core.Grid;
using WorldGen.Core.Numerics;

namespace WorldGen.Core.Terrain
{
    /// <summary>
    /// M13 (ND-155): ÉGBOLT-NYITOTTSÁG (sky view factor) a domborzatból — az
    /// ambient occlusion MAKRO sávja.
    ///
    /// MI EZ. Egy felszínpont az égbolt egy RÉSZÉT látja csak: egy völgy
    /// alján a környező gerincek elvágják a horizont alatti égboltot. Ez a
    /// mennyiség a szórt (égbolt-) megvilágítás lokális csillapítása —
    /// klasszikus terep-AO, de NEM képernyőtér-trükk: a MÁR KISZÁMÍTOTT
    /// eleváció-mezőből számolt geometriai láthatóság.
    ///
    /// A FORMULA NEM ÖNKÉNYES. Egy azimut-szektorban, ahol a horizont α
    /// szögben emelkedik, a koszinusz-súlyozott (lambert-i) égbolt-látható
    /// hányad zárt alakban <c>cos²α</c>:
    /// <c>∫₀^{π/2−α} 2·cos z·sin z dz = sin²(π/2−α) = cos²α</c>.
    /// Mivel <c>tan α = Δh/d</c>, ez <c>cos²α = d²/(d² + Δh²)</c> —
    /// a LÁTHATÓSÁG maga TISZTÁN RACIONÁLIS: nincs benne se szög, se
    /// transzcendens függvény. A nyitottság az azimutokra vett átlag.
    /// (A mintavételi IRÁNYOK előállításához kell szinusz/koszinusz; az a
    /// <see cref="Numerics.DeterministicMath"/>-ból jön, nem a nyers
    /// <c>Math</c>-ból — render-oldali mező ugyan, de nincs okunk feladni a
    /// platformfüggetlenséget ott, ahol ingyen megvan.)
    ///
    /// MIÉRT A RENDERELT (NAGYÍTOTT) DOMBORZATBÓL. A látható árnyalásnak a
    /// LÁTHATÓ geometriához kell tartoznia, ezért a
    /// <c>reliefExaggeration</c> paraméter ugyanaz a szorzó, amivel a mesh
    /// is készül (<c>terrainReliefExaggeration</c>). 1,0-nál ez a fizikai
    /// domborzat.
    ///
    /// MÉRT KORLÁT — EZÉRT VAN KÉT SÁV. A globálisan rendelkezésre álló
    /// eleváció-mező a referencia-szint (level 6): egy cella ~156 km, ahol egy
    /// 3000 m-es gerinc <c>tan α = 0,019</c>, azaz <c>cos²α = 0,99963</c> —
    /// a nyitottság-eltérés 4·10⁻⁴, LÁTHATATLAN. A makro-AO tehát csak akkor
    /// ad képet, ha a mintavétel a CELLA-MÉRETTEL együtt finomodik; ezért a
    /// <see cref="Evaluate"/> bármely szinten fut, és ezért kíséri a
    /// render-oldalon egy MIKRO sáv (az ND-151 részlet-relief önárnyékolása).
    /// A pontos mért számokat ld. ND-155.
    /// </summary>
    public static class SurfaceSkyOpenness
    {
        /// <summary>
        /// Azimut-irányok száma. 8 a legkisebb szám, ami egy völgy két
        /// oldalát és a gerinc-irányokat is elkapja (a négy tengely + a négy
        /// átló); páros, tehát a szembenálló irányok párban vannak, ami egy
        /// hosszanti völgyet szimmetrikusan mér.
        /// </summary>
        public const int AzimuthCount = 8;

        /// <summary>
        /// Mintavételi gyűrűk száma. A gyűrűk a CELLA szögméretének 1…N
        /// szeresében vannak, tehát a horizont-keresés hatóköre a rács
        /// felbontásával együtt skálázódik — nincs benne fix km-es lépték,
        /// amit egy szintváltás elrontana.
        /// </summary>
        public const int RingCount = 4;

        /// <summary>
        /// Az égbolt-láthatóság egyetlen azimut-szektorra: <c>cos²α</c>, ahol
        /// <c>tan α = Δh/d</c>. Csak a FÖLÉ emelkedő terep árnyékol
        /// (<c>Δh ≤ 0</c> → 1, azaz a lejtő lefelé nem takar égboltot).
        /// </summary>
        public static double SectorVisibility(double deltaHeightMeters, double distanceMeters)
        {
            if (deltaHeightMeters <= 0.0) return 1.0;
            if (distanceMeters <= 0.0) return 0.0;
            double d2 = distanceMeters * distanceMeters;
            return d2 / (d2 + deltaHeightMeters * deltaHeightMeters);
        }

        /// <summary>
        /// Égbolt-nyitottság minden cellára [0,1]. 1 = a teljes égbolt
        /// látszik (sík felszín), kisebb érték = a környezet elvágja.
        ///
        /// A tenger alatti cellák EGZAKTUL 1-et kapnak: a vízfelszín sík és
        /// opak, a tengerfenék domborzata nem árnyékolja az égboltot ott, ahol
        /// a képen a víz van (ugyanaz az elv, mint az ND-151 víz-alatti
        /// csillapításánál).
        /// </summary>
        /// <param name="grid">A cella-középpontokat adó sűrű rács.</param>
        /// <param name="elevationMeters">Cellánkénti eleváció (m), <paramref name="grid"/> indexelésével.</param>
        /// <param name="seaLevelMeters">A kalibrált tengerszint (m).</param>
        /// <param name="reliefExaggeration">A render domborzat-nagyítása (1 = fizikai).</param>
        public static double[] Evaluate(
            DenseGridMetrics grid, double[] elevationMeters, double seaLevelMeters, double reliefExaggeration = 1.0)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (elevationMeters == null) throw new ArgumentNullException(nameof(elevationMeters));
            if (elevationMeters.Length != grid.CellCount)
                throw new ArgumentException("Az eleváció-tömb hossza nem egyezik a rács cellaszámával.", nameof(elevationMeters));

            int side = grid.Side;
            double cellAngle = (Math.PI * 0.5) / side;
            var openness = new double[grid.CellCount];

            // A gyűrűk szögtávolsága és a hozzá tartozó METRIKUS távolság -
            // ciklus elott egyszer, mert minden cellara azonos.
            var ringDistance = new double[RingCount];
            var ringCos = new double[RingCount];
            var ringSin = new double[RingCount];
            for (int r = 0; r < RingCount; r++)
            {
                double angle = cellAngle * (r + 1);
                ringDistance[r] = angle * grid.RadiusMeters;
                ringCos[r] = DeterministicMath.Cos(angle);
                ringSin[r] = DeterministicMath.Sin(angle);
            }

            // Az azimut-irányok szinusza/koszinusza - szinten egyszer.
            var azCos = new double[AzimuthCount];
            var azSin = new double[AzimuthCount];
            for (int a = 0; a < AzimuthCount; a++)
            {
                double phi = 2.0 * Math.PI * a / AzimuthCount;
                azCos[a] = DeterministicMath.Cos(phi);
                azSin[a] = DeterministicMath.Sin(phi);
            }

            for (int c = 0; c < grid.CellCount; c++)
            {
                double h0 = elevationMeters[c];
                if (h0 < seaLevelMeters)
                {
                    openness[c] = 1.0;
                    continue;
                }

                double ux = grid.CenterX[c], uy = grid.CenterY[c], uz = grid.CenterZ[c];
                TangentBasis(ux, uy, uz, out double tx, out double ty, out double tz, out double bx, out double by, out double bz);

                double sum = 0.0;
                for (int a = 0; a < AzimuthCount; a++)
                {
                    double dx = tx * azCos[a] + bx * azSin[a];
                    double dy = ty * azCos[a] + by * azSin[a];
                    double dz = tz * azCos[a] + bz * azSin[a];

                    // A szektor lathatosaga a LEGMEREDEKEBB horizont szerint,
                    // azaz a gyuruk kozti MINIMUM (a legnagyobb alfa).
                    double visibility = 1.0;
                    for (int r = 0; r < RingCount; r++)
                    {
                        double ca = ringCos[r], sa = ringSin[r];
                        double px = ux * ca + dx * sa;
                        double py = uy * ca + dy * sa;
                        double pz = uz * ca + dz * sa;
                        int sample = CellAt(px, py, pz, grid.Level, side);
                        double dh = (elevationMeters[sample] - h0) * reliefExaggeration;
                        double v = SectorVisibility(dh, ringDistance[r]);
                        if (v < visibility) visibility = v;
                    }
                    sum += visibility;
                }
                openness[c] = sum / AzimuthCount;
            }
            return openness;
        }

        /// <summary>
        /// Stabil tangens-bázis egy egységvektorhoz: a LEGKISEBB komponens
        /// tengelyét választva a kereszt-szorzat soha nem degenerálódik
        /// (ugyanaz a minta, mint a shader <c>MicroTangentBasis</c>-ában).
        /// </summary>
        public static void TangentBasis(
            double ux, double uy, double uz,
            out double tx, out double ty, out double tz,
            out double bx, out double by, out double bz)
        {
            double ax = Math.Abs(ux), ay = Math.Abs(uy), az = Math.Abs(uz);
            double sx, sy, sz;
            if (ax <= ay && ax <= az) { sx = 1.0; sy = 0.0; sz = 0.0; }
            else if (ay <= az) { sx = 0.0; sy = 1.0; sz = 0.0; }
            else { sx = 0.0; sy = 0.0; sz = 1.0; }

            tx = uy * sz - uz * sy;
            ty = uz * sx - ux * sz;
            tz = ux * sy - uy * sx;
            double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (len < 1e-12) { tx = 1.0; ty = 0.0; tz = 0.0; }
            else { tx /= len; ty /= len; tz /= len; }

            bx = uy * tz - uz * ty;
            by = uz * tx - ux * tz;
            bz = ux * ty - uy * tx;
        }

        private static int CellAt(double px, double py, double pz, int level, int side)
        {
            TileId tile = TileGeometry.FromPosition(px, py, pz, level);
            tile.GetUV(out uint u, out uint v);
            return DenseGridMetrics.Index(tile.Face, (int)u, (int)v, side);
        }
    }
}
