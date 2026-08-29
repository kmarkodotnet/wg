namespace WorldGen.Core.Terrain
{
    /// <summary>
    /// Domain warp (docs/00-spec-v1.0.md Sz.13.1, docs/04-decisions.md ND-36):
    /// a lemez-Voronoi HOZZÁRENDELÉSHEZ (<see cref="Tectonics.PlateGeneration.AssignPlate"/>)
    /// és a HATÁR-KÖZELSÉG ("gap", <c>Tectonics.PlateBoundaryEffect.TwoBestDots</c>)
    /// számításhoz használt POZÍCIÓT torzítja el egy zaj-alapú eltolással,
    /// MIELŐTT a legközelebbi-mag keresés/gap-számítás megtörténne.
    ///
    /// OK (felhasználói visszajelzés): a tiszta "legközelebbi mag" gömbi
    /// Voronoi-felosztás MATEMATIKAILAG MINDIG sima, nagykör-ív-szerű
    /// határvonalat ad, függetlenül attól, mennyi zajt teszünk az
    /// elevációra utólag — a zaj csak azt módosítja, MILYEN MAGAS egy
    /// pont, nem azt, MELYIK LEMEZHEZ tartozik. A domain warp a DÖNTÉSHEZ
    /// használt pozíciót torzítja, így maga a határvonal is organikusan
    /// hullámzik.
    ///
    /// MÓDSZER: három FÜGGETLEN <see cref="FractalNoise.Fbm"/>-kiértékelés
    /// (dx,dy,dz) — a MÁR VERIFIKÁLT primitívet ÚJRAFELHASZNÁLJA
    /// VÁLTOZATLAN szignatúrával (nincs új hash-függvény, nincs új
    /// <see cref="Random.RandomProperty"/>). A három komponens
    /// dekorrelációját KIZÁRÓLAG fix, egymástól és nullától távoli
    /// bemeneti koordináta-eltolással oldjuk meg (lásd a lenti
    /// <c>OffsetDx0..OffsetDz2</c> konstansokat) — így a három kiértékelés
    /// különböző rácspontokra esik,
    /// statisztikailag függetlennek tekinthető, annak ellenére, hogy
    /// ugyanazt a hash-láncot használják. Az eltolás-konstansok tetszőlegesen
    /// választottak, nincs mélyebb jelentésük — csak annyi a cél, hogy a
    /// három (dx,dy,dz) kiértékelés lattice-cellái ne essenek egybe
    /// semmilyen egész-többszörösnél.
    ///
    /// PARAMÉTER-HANGOLÁS (Python-referenciával mérve,
    /// <c>tools/reference/domain_warp_ref.py</c>): Strength=1.0,
    /// Frequency=2.0, Octaves=3 → átlagos szögeltolódás kb. 9.0 fok
    /// (8000 véletlen ponton mérve, célzott nagyságrend: 5-15 fok), és a
    /// lemezhatárok közelében (gap &lt; <see cref="Tectonics.PlateBoundaryEffect.DefaultGapScale"/>)
    /// lévő pontok kb. 40%-ánál változik meg az <c>AssignPlate</c>
    /// eredménye a warp hatására — érdemi, de nem kaotikus/domináló hatás.
    ///
    /// HATÓKÖR: a lemez-HOZZÁRENDELÉS és a HATÁR-KÖZELSÉG kapja a warpolt
    /// pozíciót. A TÉNYLEGES ELEVÁCIÓ-ZAJ kiértékelése
    /// (<see cref="Tectonics.CrustElevation.BaseElevation"/>,
    /// <see cref="Tectonics.CrustElevation.MountainMask"/>) VÁLTOZATLANUL a
    /// NYERS pozíciót kapja — a warp csak a lemez-topológia döntéséhez
    /// használt, a domborzat-textúra nem (szándékos döntés, ND-36
    /// indokolja: a már jól hangolt zaj-textúra ne változzon meg
    /// alapvetően emiatt).
    /// </summary>
    public static class DomainWarp
    {
        public const double DefaultStrength = 1.0;
        public const double DefaultFrequency = 2.0;
        public const int DefaultOctaves = 3;

        // Fix, egymástól és nullától "elég távol" koordináta-eltolások a
        // három (dx,dy,dz) fBm-kiértékeléshez - ld. osztály-doc. 1:1 a
        // Python referenciával (tools/reference/domain_warp_ref.py
        // OFFSET_DX/DY/DZ).
        private const double OffsetDx0 = 7.13, OffsetDx1 = 2.71, OffsetDx2 = 9.01;
        private const double OffsetDy0 = 13.37, OffsetDy1 = 5.59, OffsetDy2 = 1.91;
        private const double OffsetDz0 = 4.67, OffsetDz1 = 11.23, OffsetDz2 = 8.05;

        /// <summary>
        /// Eltorzítja az (x,y,z) egységvektor-pozíciót egy koherens
        /// fBm-alapú eltolással, majd visszaállítja egységvektorra. Tiszta
        /// függvény - nincs mutable állapot, ismételt hívás ugyanarra a
        /// bemenetre bitre azonos kimenetet ad.
        /// </summary>
        public static void WarpPosition(
            ulong worldSeed, double x, double y, double z,
            out double warpedX, out double warpedY, out double warpedZ,
            double strength = DefaultStrength, double frequency = DefaultFrequency, int octaves = DefaultOctaves)
        {
            double dx = FractalNoise.Fbm(worldSeed, x + OffsetDx0, y + OffsetDx1, z + OffsetDx2, frequency, octaves);
            double dy = FractalNoise.Fbm(worldSeed, x + OffsetDy0, y + OffsetDy1, z + OffsetDy2, frequency, octaves);
            double dz = FractalNoise.Fbm(worldSeed, x + OffsetDz0, y + OffsetDz1, z + OffsetDz2, frequency, octaves);

            double wx = x + strength * dx;
            double wy = y + strength * dy;
            double wz = z + strength * dz;
            double length = System.Math.Sqrt(wx * wx + wy * wy + wz * wz);

            if (length < 1e-9)
            {
                // Dokumentált él-eset védelem: ha a torzítás véletlenül
                // (közel) pontosan az origóba tolná a pontot (elméletileg
                // lehetséges, de rendkívül valószínűtlen |strength*d| < 1
                // miatt), essünk vissza a torzítatlan pozícióra - jobb egy
                // enyhe determinisztikus torzítás-kihagyás, mint egy
                // NaN/végtelen irányvektor.
                warpedX = x; warpedY = y; warpedZ = z;
                return;
            }

            // FONTOS: KÖZVETLEN OSZTÁS (nem reciprok-szorzás), hogy
            // BITPONTOSAN egyezzen a Python referenciával
            // (tools/reference/domain_warp_ref.py: "wx / length"), ami itt
            // NEM az 1/x-es-szorzás mintát követi (ellentétben pl.
            // DeterministicRandom.SampleUnitVector3-mal, ahol a Python is
            // reciprok-szorzást használ) - a két művelet IEEE-754-ben nem
            // feltétlenül ad bitre azonos eredményt.
            warpedX = wx / length;
            warpedY = wy / length;
            warpedZ = wz / length;
        }
    }
}
