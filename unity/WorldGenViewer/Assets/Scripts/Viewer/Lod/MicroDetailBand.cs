using System;
using WorldGen.Core.Terrain;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// ND-151 (M13 4. fázis): a felszíni mikro-részlet fBm-létrájának
    /// NÉZETFÜGGŐ sávválasztása. Ez a rész szándékosan NEM a Core-ban van: a
    /// <see cref="SurfaceMicroDetail"/> a világból származó választ adja
    /// (amplitúdó/szórás/frekvencia-szorzó), a KAMERÁRÓL viszont a Core nem
    /// tud — és nem is szabad tudnia.
    ///
    /// A PROBLÉMA. A mikro-részlet leghasznosabb hullámhossza ~50 m és
    /// ~1500 km között van, ez ~15 oktáv. Ennyit egy fragment shader nem
    /// számol ki per pixel, és nem is kell: a pixelnél finomabb oktáv csak
    /// villogást (aliasingot) ad, a képernyő teljes szélességénél nagyobb
    /// oktáv pedig nem látszik. Ezért négy oktávot használunk, és a LÉTRÁT
    /// toljuk el a pixel-lábnyom szerint — ez a "sáv".
    ///
    /// A SÁVVÁLTÁS FOLYTONOSSÁGA (a kritikus rész). Ha a sáv csak ugrálna,
    /// zoomolás közben a teljes felszíni textúra láthatóan pattogna. A
    /// megoldás a létra két végének ellentétes irányú átúsztatása: az
    /// oktáv-súlyok
    /// <c>w0 = (1 - frac)</c>, <c>w1 = w2 = 1</c>, <c>w3 = frac</c>
    /// (mindegyik a saját <c>persistence^k</c> amplitúdóján), majd a
    /// súlyösszeggel normálva. BIZONYÍTHATÓAN folytonos: <c>frac = 1</c>-nél
    /// az <c>n</c>-edik sáv súlyai <c>(0, a1, a2, a3)</c> a
    /// <c>(B, 2B, 4B, 8B)</c> frekvenciákon, ami NORMÁLÁS UTÁN ugyanaz, mint
    /// az <c>n+1</c>-edik sáv <c>frac = 0</c>-nál: <c>(a0, a1, a2, 0)</c> a
    /// <c>(2B, 4B, 8B, 16B)</c> frekvenciákon — az arányok
    /// <c>(0,571; 0,286; 0,143)</c> mindkét oldalon. Tehát a sávváltás
    /// pillanatában a kép NEM változik.
    /// </summary>
    public static class MicroDetailBand
    {
        /// <summary>
        /// Hány pixelt fedjen a LEGFINOMABB oktáv egy hullámhossza.
        ///
        /// MÉRVE (2026-09-27, élő Play, 1600x900): 3,0-val a legfinomabb oktáv
        /// éppen a Nyquist-határra esik, és a felszín egyenletes, homokpapír-
        /// szerű SZEMCSÉT kap - nem domborzatnak, hanem zajnak látszik. 8,0-val
        /// a legfinomabb oktáv 8 pixel, az alap-oktáv 64 pixel: ez már
        /// felismerhető részlet-domborzat. A Nyquist-korlát 2; a 8,0 két
        /// oktávnyi ráhagyás, mert a normál-perturbáció a legérzékenyebb az
        /// aliasingra (a spekuláris kiemeli).
        /// </summary>
        public const double PixelsPerFinestFeature = 8.0;

        /// <summary>
        /// A legfinomabb oktáv frekvencia-szorzója a létra alapjához képest.
        /// SORREND: ez a mező a <see cref="MaxBandLevel"/> ELŐTT van, mert a
        /// statikus inicializálók deklarációs sorrendben futnak.
        /// </summary>
        private static readonly double FinestOctaveFactor =
            Math.Pow(SurfaceMicroDetail.Lacunarity, SurfaceMicroDetail.Octaves - 1);

        /// <summary>
        /// A legfelső elérhető sáv indexe: itt a legfinomabb oktáv éppen a
        /// <see cref="SurfaceMicroDetail.MaxFrequency"/>-n van. Fölötte a
        /// részlet nem lesz finomabb (a float-pontosság a korlát), csak a
        /// geometria nagyítódik tovább.
        /// </summary>
        public static readonly double MaxBandLevel = Log2(
            SurfaceMicroDetail.MaxFrequency
            / FinestOctaveFactor
            / SurfaceMicroDetail.BaseFrequency);

        /// <summary>A kiválasztott sáv: a shadernek átadott három szám.</summary>
        public readonly struct Band
        {
            /// <summary>A létra ALAP-frekvenciája (ciklus/radián).</summary>
            public readonly double BaseFrequency;

            /// <summary>A sávon belüli tört rész [0,1): az átúsztatás vezérlője.</summary>
            public readonly double Fraction;

            /// <summary>
            /// [0,1] láthatóság. Nulla, amíg a bolygó annyira messze van, hogy
            /// már a létra legalsó oktávja is pixel alatti lenne — ott a
            /// részlet csak zajt adna a bolygó-nézetre.
            /// </summary>
            public readonly double Visibility;

            public Band(double baseFrequency, double fraction, double visibility)
            {
                BaseFrequency = baseFrequency;
                Fraction = fraction;
                Visibility = visibility;
            }
        }

        /// <summary>
        /// Sávválasztás a kamera-geometriából. Minden hossz UGYANABBAN a
        /// jelenet-egységben; a visszaadott frekvencia ciklus/radián a
        /// gömbfelszínen, tehát a jelenet léptékétől FÜGGETLEN (ez fontos:
        /// az ND-19 floating origin változtathatja a léptéket).
        /// </summary>
        /// <param name="cameraDistanceUnits">Kamera - bolygóközéppont távolság.</param>
        /// <param name="planetRadiusUnits">A bolygó rajzolt (tengerszinti) rádiusza.</param>
        /// <param name="verticalFovRad">A kamera vertikális látószöge radiánban.</param>
        /// <param name="pixelHeight">A célfelület magassága pixelben.</param>
        public static Band Select(
            double cameraDistanceUnits, double planetRadiusUnits, double verticalFovRad, double pixelHeight)
        {
            if (!(planetRadiusUnits > 0.0) || !(pixelHeight >= 1.0)
                || !(verticalFovRad > 0.0) || verticalFovRad >= Math.PI
                || double.IsNaN(cameraDistanceUnits))
                return new Band(SurfaceMicroDetail.BaseFrequency, 0.0, 0.0);

            // A kamera a felszín alatt/azon: a lehető legfinomabb sáv.
            double surfaceDistance = cameraDistanceUnits - planetRadiusUnits;
            if (!(surfaceDistance > 0.0))
                return BandAtLevel(MaxBandLevel, 1.0);

            // Egy pixel világ-hossza a legközelebbi felszínponton, majd
            // ugyanez a gömbfelszínen radiánban.
            double worldPerPixel = 2.0 * Math.Tan(verticalFovRad * 0.5) * surfaceDistance / pixelHeight;
            double radiansPerPixel = worldPerPixel / planetRadiusUnits;
            if (!(radiansPerPixel > 0.0))
                return BandAtLevel(MaxBandLevel, 1.0);

            double finestWavelengthRad = PixelsPerFinestFeature * radiansPerPixel;
            double neededBaseFrequency = 1.0 / (finestWavelengthRad * FinestOctaveFactor);
            double level = Log2(neededBaseFrequency / SurfaceMicroDetail.BaseFrequency);

            // A 0 alatti sáv azt jelenti, hogy már a létra alja is pixel
            // alatti: ilyenkor a részlet elhalványul, nem "elcsúszik".
            double visibility = SurfaceMicroDetail.Smoothstep01(level);
            if (level <= 0.0)
                return new Band(SurfaceMicroDetail.BaseFrequency, 0.0, visibility);
            return BandAtLevel(level, visibility);
        }

        private static Band BandAtLevel(double level, double visibility)
        {
            if (level >= MaxBandLevel)
                return new Band(SurfaceMicroDetail.BaseFrequency * Exp2(MaxBandLevel), 0.0, visibility);
            double whole = Math.Floor(level);
            return new Band(
                SurfaceMicroDetail.BaseFrequency * Exp2(whole),
                level - whole,
                visibility);
        }

        /// <summary>
        /// A négy oktáv NORMÁLT súlya egy sáv-tört értékre — ugyanaz a
        /// képlet, amit a shader is számol. Itt azért van kiemelve, hogy a
        /// sávváltás folytonossága TESZTELHETŐ legyen (a HLSL nem az).
        /// </summary>
        public static double[] OctaveWeights(double fraction)
        {
            double frac = fraction < 0.0 ? 0.0 : (fraction > 1.0 ? 1.0 : fraction);
            var weights = new double[SurfaceMicroDetail.Octaves];
            double amplitude = 1.0;
            double sum = 0.0;
            for (int k = 0; k < weights.Length; k++)
            {
                double fade = k == 0 ? 1.0 - frac : (k == weights.Length - 1 ? frac : 1.0);
                weights[k] = amplitude * fade;
                sum += weights[k];
                amplitude *= SurfaceMicroDetail.Persistence;
            }
            if (sum > 0.0)
                for (int k = 0; k < weights.Length; k++)
                    weights[k] /= sum;
            return weights;
        }

        // Math.Log/Math.Pow itt MEGENGEDETT (a CLAUDE.md tilalma a szimulációs
        // KRITIKUS ÚTRA vonatkozik, ND-23): ez nézetfüggő render-paraméter, ami
        // semmilyen mezőbe, hashbe vagy mentésbe nem folyik vissza, és
        // frame-enként egyszer, nem pontonként fut.
        private static double Log2(double x) => Math.Log(x) / Math.Log(2.0);

        private static double Exp2(double x) => Math.Pow(2.0, x);
    }
}
