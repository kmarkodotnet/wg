using UnityEngine;

namespace WorldGen.Viewer
{
    /// <summary>
    /// A WorldGen.Core "test-keret" (Grid/Astronomy modulok saját, belső
    /// koordinátarendszere - ld. TileGeometry, OrbitalMechanics doksik) és a
    /// Unity világkoordináta közötti ÁTVÁLTÁS. EZT a konverziót MINDEN
    /// komponensnek (PlanetGridMesh, SunController) UGYANÚGY kell
    /// alkalmaznia - különben a felszín és a megvilágítás elcsúszik
    /// egymáshoz képest.
    ///
    /// A Core-modulok saját belső konvenciója szerint a "pólus" (spin-
    /// tengely, ld. OrbitalMechanics RotZ) a Z tengely. Unityben viszont a
    /// megszokott "glóbusz oldalról nézve" élményhez a pólusnak a Y
    /// tengelyen (világ "fel") kell lennie - enélkül a kamera alapból a
    /// pólusra néz rá felülről, nem az egyenlítőre oldalról.
    ///
    /// Tengelycsere: Core (x,y,z) -> Unity (x, z, y). Ez pusztán egy
    /// koordináta-relabeling, nem befolyásolja a már meglévő "konzisztens
    /// háromszög-bejárás" logikát (az mindig az ÉPPEN AKTUÁLIS csúcs-
    /// pozíciókból számol normált, tehát automatikusan alkalmazkodik).
    /// </summary>
    internal static class BodyFrameConversion
    {
        public static Vector3 ToUnity(double x, double y, double z)
        {
            return new Vector3((float)x, (float)z, (float)y);
        }

        /// <summary>
        /// A <see cref="ToUnity"/> inverze - Unity-terbeli (mar tengely-
        /// cserelt) koordinatabol vissza a Core "test-keret" konvenciojaba.
        /// A tengelycsere onmaga inverze (csak y/z felcsereles), tehat ez
        /// UGYANAZ a keplet, csak elnevezesben explicit az irany - az M9
        /// adaptiv LOD (WorldGen.Viewer.Lod.AdaptiveQuadTree) ezt hasznalja
        /// a kamera Unity-vilagpoziciojanak a Core-keretbe valo
        /// visszaalakitasahoz, mert az a modul motorfuggetlen es semmit
        /// nem tud a Unity-tengelykonvenciorol.
        /// </summary>
        public static void ToCore(Vector3 unityPosition, out double x, out double y, out double z)
        {
            x = unityPosition.x;
            y = unityPosition.z;
            z = unityPosition.y;
        }
    }
}
