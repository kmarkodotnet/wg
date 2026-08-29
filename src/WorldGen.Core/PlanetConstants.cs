namespace WorldGen.Core
{
    /// <summary>
    /// Globális, egy bolygóra vonatkozó fizikai állandók, amiket több modul
    /// (pl. becsapódás-kráterezés, ND-28) is ismer.
    ///
    /// FONTOS: ez NEM oldja meg ND-19-et (floating origin, Unity-oldali
    /// render-precízió nagy léptéken) — az M9-re marad. Ez a konstans csak
    /// azt teszi lehetővé, hogy a Core valós fizikai mennyiségeket (pl.
    /// kráter-átmérő méterben) át tudjon váltani a rács szögtartományára.
    /// </summary>
    public static class PlanetConstants
    {
        /// <summary>
        /// A spec kanonikus példa-bolygójának sugara méterben
        /// (docs/00-spec-v1.0.md:2170,2253 — radiusKm: 7420). Ld. ND-28.
        /// </summary>
        public const double RadiusMeters = 7_420_000.0;
    }
}
