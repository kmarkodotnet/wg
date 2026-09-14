using WorldGen.Core.Features;
using WorldGen.Core.Random;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// Felhasználói kérés (2026-09-13, docs/backlog.md): tektonikuslemez-
    /// overlay Play módban, deep time során a MÁR MEGLÉVŐ lemez-mozgást
    /// mutatva (<see cref="PlateMotion.MovedSeeds"/> + <see cref="PlateGeneration.AssignPlate"/> -
    /// nincs új szimuláció). Ez az osztály KIZÁRÓLAG a megjelenítéshez kell:
    /// lemezenkénti szín és név - egyik sem fizikai állapot, mindkettő
    /// tiszta, determinisztikus függvénye a (worldSeed, plateId)-nek.
    /// </summary>
    public static class PlatePresentation
    {
        /// <summary>
        /// A lemez-nevek featureId-tartománya - disjunkt a kontinens (0..N),
        /// régió (10000+i), terület (20000+..., fallback 950000+) és
        /// fallback-régió (900000+i) tartományoktól (ld.
        /// docs/01-architecture.md §12.3 és a navigációs menü doksija).
        /// </summary>
        public const ulong PlateFeatureIdBase = 800000;

        public static ulong PlateFeatureId(int plateId) => PlateFeatureIdBase + (ulong)plateId;

        /// <summary>
        /// Egy lemez neve - a MÁR MEGLÉVŐ <see cref="NameGeneration.GenerateName(ulong, ulong, string)"/>-t
        /// hívja, a kéregtípus szerint választott (nem biome-, hanem
        /// kéreg-alapú) utótaggal, hogy "Craton"/"Trench" jellegű, a
        /// lemez fizikai jellegéhez illő nevet adjon, ne biome-alapút.
        /// </summary>
        public static string PlateName(ulong worldSeed, int plateId, bool isOceanic)
        {
            string crustKey = isOceanic ? "OceanicCrust" : "ContinentalCrust";
            return NameGeneration.GenerateName(worldSeed, PlateFeatureId(plateId), crustKey);
        }

        /// <summary>
        /// [0,1) színárnyalat egy lemezhez - arany-arány léptékű elosztás
        /// (jól elkülönülő árnyalatok, nincs két szomszédos index-nek közeli
        /// színe), egy világonként egyszer mintavételezett kezdő-eltolással
        /// (<see cref="RandomProperty.PlateColorHue"/>, Decorative domain -
        /// tisztán renderelési tulajdonság, ld. RandomDomain.Decorative doksi).
        /// </summary>
        public static double PlateHue(ulong worldSeed, int plateId)
        {
            const double goldenRatioConjugate = 0.6180339887498949;
            double hueOffset = DeterministicRandom.SampleRange(
                worldSeed, RandomDomain.Decorative, 0, 0, 0.0, 1.0, RandomProperty.PlateColorHue);
            double hue = hueOffset + plateId * goldenRatioConjugate;
            return Frac01(hue);
        }

        private static double Frac01(double x)
        {
            double f = x - System.Math.Floor(x);
            // Vedelmi hatar: IEEE-754 kerekitesi hiba miatt f elvben [0,1)-ben
            // van, de a hatarra (1.0) sose csusszon ki.
            return f >= 1.0 ? 0.0 : (f < 0.0 ? 0.0 : f);
        }

        /// <summary>
        /// Egy lemez RGB színe [0,1] tartományban - rögzített, kellemes
        /// telítettség/fényesség, csak az árnyalat változik lemezenként.
        /// </summary>
        public static void PlateColorRgb(ulong worldSeed, int plateId, out double r, out double g, out double b)
        {
            const double saturation = 0.62;
            const double value = 0.88;
            HsvToRgb(PlateHue(worldSeed, plateId), saturation, value, out r, out g, out b);
        }

        /// <summary>
        /// Standard HSV -&gt; RGB, csak `+ - * / Math.Floor`-ral (I1/I2-kompatibilis,
        /// nincs benne transzcendens függvény, tehát nincs ND-23 kockázat).
        /// h∈[0,1), s,v∈[0,1].
        /// </summary>
        public static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
        {
            double hh = Frac01(h) * 6.0;
            int i = (int)System.Math.Floor(hh) % 6;
            if (i < 0) i += 6;
            double f = hh - System.Math.Floor(hh);
            double p = v * (1.0 - s);
            double q = v * (1.0 - s * f);
            double t = v * (1.0 - s * (1.0 - f));

            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }
    }
}
