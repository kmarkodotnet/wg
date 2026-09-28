using System;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// A térfogati felhő (ND-154) raymarch-terve — UnityEngine nélkül, hogy
    /// .NET-ből tesztelhető legyen.
    ///
    /// A LÉPÉSSZÁM SZÁNDÉKOSAN NEM NÉZETFÜGGŐ. Egy korábbi változat a héj
    /// képernyőre vetített vastagságából számolta; ez MÉRT hibát okozott
    /// (a felhő magassága ugrált kameramozgásra), ld. <see cref="MarchSteps"/>.
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
        /// A menet lépésszáma — FIX, nem nézetfüggő.
        ///
        /// MIÉRT FIX (felhasználói visszajelzés, 2026-09-28: „ahogy mozgatom,
        /// adott felhő magassága is ugrál"). Az első változat a lépésszámot a
        /// héj képernyőre vetített vastagságából számolta, tehát a kamera
        /// távolságával 12 és 48 között változott. A menet a MINTAVÉTELI
        /// ABLAKOT osztja fel N részre, így a lépésszám megváltozása
        /// ELTOLJA azokat a magasságokat, ahol a sűrűséget mintavételezzük:
        /// egy vékony dekk súlyának középpontja fél lépéssel elmozdul, ami a
        /// képen a felhő MAGASSÁGÁNAK UGRÁSÁKÉNT látszik. Egy képen
        /// állandó lépésszám ezt a szabadsági fokot megszünteti.
        ///
        /// 32 azt jelenti, hogy a dekk és a hozzá tartozó padding ~32 mintát
        /// kap; a függőleges profil két átmeneti sávja (0,18 és 0,42 a
        /// vastagság arányában) így 4, illetve 9 mintából áll elő.
        /// </summary>
        public const int MarchSteps = 32;

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
    }
}
