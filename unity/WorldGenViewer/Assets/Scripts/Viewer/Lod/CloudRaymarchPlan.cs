using System;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// A térfogati felhő (ND-154) NÉZETFÜGGŐ raymarch-terve — UnityEngine
    /// nélkül, hogy .NET-ből tesztelhető legyen. A Core nem tud a kameráról,
    /// ezért a lépésszám és a burkoló-geometria mérete ide tartozik (ugyanaz a
    /// szereposztás, mint a <see cref="MicroDetailBand"/>-nál).
    ///
    /// A LÉPÉSSZÁM NEM SZEMRE VÁLASZTOTT. A raymarch lépéshossza
    /// <c>útvonal / lépésszám</c>; a lépésszám akkor „elég”, ha a héj a
    /// KÉPERNYŐN nincs több pixel magas, mint amennyi lépés van benne —
    /// finomabb felosztás definíció szerint láthatatlan. Ezért a terv a héj
    /// KÉPERNYŐRE VETÍTETT vastagságából számol
    /// (<see cref="PixelsPerStep"/> pixel/lépés), és a két végén vág.
    ///
    /// MIÉRT KELL BURKOLÓ-GEOMETRIA. A raymarch egy fragment shaderben fut,
    /// tehát kell egy felület, ami a héjat a képernyőn LEFEDI. Egy sokszög-gömb
    /// lapjai a beírt gömbön belül vannak, ezért a nyers héj-sugárral rajzolt
    /// mesh a limbnél LEVÁGNÁ a felhőt; a <see cref="ShellPaddingFactor"/>
    /// éppen annyival nagyít, hogy a lapok a héjat kívülről érintsék. A
    /// „túllógó” terület nem baj: ott a sugár ANALITIKUSAN nem metszi a héjat,
    /// és a shader eldobja a fragmentet.
    /// </summary>
    public static class CloudRaymarchPlan
    {
        /// <summary>
        /// A legkisebb lépésszám. Ennél kevesebb a felhőalap/felhőtető
        /// átmeneti sávjait (0,18 és 0,42 a vastagság arányában) sem mintázná
        /// meg, tehát a függőleges profil lépcsőzne.
        /// </summary>
        public const int MinViewSteps = 12;

        /// <summary>
        /// A legnagyobb lépésszám. A felhő a látóhatárt súrolva optikailag
        /// vastag (a korai kilépés amúgy is levágja a marchot), ezért ennél
        /// több lépés már csak GPU-időt fogyaszt.
        /// </summary>
        public const int MaxViewSteps = 48;

        /// <summary>
        /// Pixel / lépés. 2,0 azt jelenti: a héj képernyőre vetített
        /// vastagságának minden második pixelére esik egy minta — a
        /// Nyquist-határ a KÉPEN, nem a térben (a térbeli határt a
        /// <see cref="MinViewSteps"/> tartja).
        /// </summary>
        public const double PixelsPerStep = 2.0;

        /// <summary>
        /// A Nap felé futó (árnyék-) menet lépésszáma. Ez nem KÉP, hanem egy
        /// áteresztés-BECSLÉS, amit a Beer–Lambert-kitevő simít; 4 lépés a
        /// felhő függőleges profiljának (egy emelkedő + egy eső fade)
        /// elkülönítéséhez elég, és fixen 4, mert minden sűrűségi mintánál
        /// lefut — itt a költség négyzetesen jelenik meg.
        /// </summary>
        public const int LightSteps = 4;

        /// <summary>
        /// Korai kilépés: ha az áteresztés ez alá esik, a maradék minta már
        /// nem látszik (1% — a 8 bites kimeneten 2,5 kvantálási szint).
        /// </summary>
        public const double TransmittanceCutoff = 0.01;

        /// <summary>
        /// A burkoló gömb-mesh nagyítási tényezője: a kockagömb-quad
        /// szög-átmérőjének feléhez tartozó <c>1/cos</c>. Így a LAPOK
        /// érintik a héjat, tehát a mesh TARTALMAZZA a héjgömböt.
        /// </summary>
        public static double ShellPaddingFactor(int shellMeshLevel)
        {
            if (shellMeshLevel < 0) shellMeshLevel = 0;
            double cellAngle = (Math.PI * 0.5) / (1 << shellMeshLevel);
            double halfDiagonal = cellAngle * 0.5 * Math.Sqrt(2.0);
            double c = Math.Cos(halfDiagonal);
            return c <= 1e-6 ? 1.0 : 1.0 / c;
        }

        /// <summary>
        /// A héj képernyőre vetített vastagsága pixelben. A kamera és a héj
        /// LEGKÖZELEBBI pontja közti távolságból számol — a legszigorúbb eset,
        /// mert ott a legnagyobb a nagyítás.
        /// </summary>
        public static double ShellPixels(
            double cameraDistanceFromCenter, double shellOuterRadius, double shellThickness,
            double verticalFovRadians, int pixelHeight)
        {
            if (shellThickness <= 0.0 || pixelHeight <= 0 || verticalFovRadians <= 0.0) return 0.0;
            double toShell = cameraDistanceFromCenter - shellOuterRadius;
            // A héjon BELÜL (vagy közvetlenül fölötte) a nagyítás elszaladna;
            // a padló a héj saját vastagsága, ami a "belül vagyok" eset
            // természetes hossz-léptéke.
            if (toShell < shellThickness) toShell = shellThickness;
            double halfExtent = Math.Tan(verticalFovRadians * 0.5) * toShell;
            if (halfExtent <= 0.0) return 0.0;
            double pixelsPerUnit = pixelHeight / (2.0 * halfExtent);
            return shellThickness * pixelsPerUnit;
        }

        /// <summary>A nézetfüggő lépésszám a héj pixel-vastagságából.</summary>
        public static int ViewSteps(
            double cameraDistanceFromCenter, double shellOuterRadius, double shellThickness,
            double verticalFovRadians, int pixelHeight)
        {
            double pixels = ShellPixels(
                cameraDistanceFromCenter, shellOuterRadius, shellThickness, verticalFovRadians, pixelHeight);
            double steps = pixels / PixelsPerStep;
            if (double.IsNaN(steps) || steps <= MinViewSteps) return MinViewSteps;
            if (steps >= MaxViewSteps) return MaxViewSteps;
            return (int)Math.Floor(steps + 0.5);
        }
    }
}
