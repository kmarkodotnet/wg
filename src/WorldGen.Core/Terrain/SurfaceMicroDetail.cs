using WorldGen.Core.Random;
using WorldGen.Core.Tectonics;

namespace WorldGen.Core.Terrain
{
    /// <summary>
    /// M13 4. fázis (ND-151): a felszíni MIKRO-RÉSZLET render-paraméterei.
    ///
    /// MI EZ. A látható mesh legfinomabb szintje is ~10-100 m-es quadokból áll,
    /// a képernyőn viszont közeli nézetben egy quad sok pixelt fed — ott a
    /// felszín MA teljesen sima, mert a vertex-szín és a vertex-normál
    /// lineárisan interpolálódik. A mikro-részlet ezt a hézagot tölti be:
    /// PER-PIXEL, a fragment shaderben számolt fBm-mel perturbálja a normált
    /// (részlet-domborzat árnyalása) és modulálja az albedót — ÚJ GEOMETRIA
    /// NÉLKÜL, nulla új eleváció-kiértékeléssel.
    ///
    /// MIÉRT NEM SÉRTI AZ I3-at. Nincs kézzel festett textúra: a részlet
    /// AMPLITÚDÓJA, FREKVENCIÁJA és SZÍN-SZÓRÁSA a világmodellből számított
    /// mezőkből jön (lejtő = a már renderelt geometria normálja, magasság =
    /// a már renderelt rádiusz, tengerszint = a kalibrált ND-38 szint), a
    /// FÁZISA pedig a világ seedjéből (ld. <see cref="PhaseOffset"/>), tehát
    /// ugyanaz a seed ugyanazt a mikro-részletet adja.
    ///
    /// MIÉRT NEM ÉRINTI AZ I1-et. A mikro-részlet KIZÁRÓLAG render-oldali:
    /// egyetlen mezőbe, gyorsítótárba, hash-be vagy mentésbe sem folyik
    /// vissza. A GPU-n futó fBm bitpontossága platformfüggő — ÉPPEN EZÉRT nem
    /// szabad semminek visszacsatolnia belőle (ez a döntő eltérés az ND-128-ban
    /// TÖRÖLT GPU-elevációtól, ami a klasszifikáció BEMENETE volt).
    ///
    /// A HLSL-MÁSOLAT KÉRDÉSE. A shader a lenti leképezés ALAKJÁT (lerp +
    /// smoothstep) tükrözi, a SZÁMOKAT viszont uniformként INNEN kapja — így
    /// az ND-128 hibaosztálya (két, egymástól elcsúszó számkészlet) nem tud
    /// kialakulni: egyetlen igazságforrás van, ez az osztály.
    /// </summary>
    public static class SurfaceMicroDetail
    {
        /// <summary>
        /// A mikro-részlet fBm-létrájának ALSÓ vége (ciklus/radián a
        /// gömbfelszínen): pontosan a következő oktáv a modell saját
        /// másodlagos relief-zajának LEGFINOMABB oktávja után. NEM új szabad
        /// paraméter — a <see cref="CrustElevation.SecondaryNoiseFrequency"/>
        /// és a <see cref="CrustElevation.SecondaryNoiseOctaves"/> határozza
        /// meg: a létra ott folytatódik, ahol a világmodell abbahagyta.
        /// </summary>
        public static readonly double BaseFrequency =
            CrustElevation.SecondaryNoiseFrequency * (1 << CrustElevation.SecondaryNoiseOctaves);

        /// <summary>
        /// A mikro-részlet fBm-létrájának FELSŐ vége (ciklus/radián), a
        /// float-pontosságból SZÁMÍTVA, nem szemre választva. A shader a
        /// bolygó-lokál egységvektorból (32 bites float, relatív eps
        /// 2^-23 ≈ 1,19e-7) állítja elő a zaj-koordinátát; ha a rács-cella
        /// hibája 1/64 cellánál nagyobb lenne, a minta láthatóan lépcsőzne.
        /// (1/64) / 2^-23 = 2^17, ami Föld-méreten ~49 m hullámhossz.
        /// Ennél finomabb részlethez a geometria is pontosabb pozíció-
        /// ábrázolást kér (ND-19 / A12/2 test-keretes renderelés).
        /// </summary>
        public const double MaxFrequency = 131072.0;

        /// <summary>A létra oktávszáma. A negyedik oktáv a sávváltás átmenete (ld. a viewer MicroDetailBand doksiját).</summary>
        public const int Octaves = 4;

        /// <summary>fBm-persistence: a modell saját zajával azonos (<see cref="FractalNoise"/>).</summary>
        public const double Persistence = FractalNoise.DefaultPersistence;

        /// <summary>fBm-lacunarity: a modell saját zajával azonos (<see cref="FractalNoise"/>).</summary>
        public const double Lacunarity = FractalNoise.DefaultLacunarity;

        /// <summary>
        /// Az a lejtő-szinusz, ami körül a felszín "sík üledék"-ből
        /// "szálkás kőzet"-be vált (0,30 ≈ 17,5°).
        /// </summary>
        public const double SlopeReferenceSin = 0.30;

        /// <summary>
        /// Az a tengerszint fölötti magasság, ami fölött a felszín lejtő
        /// NÉLKÜL is kőzet-jellegű részletet kap (fennsíkok, magashegyi
        /// platók) — különben egy 4000 m-es plató ugyanolyan sima lenne, mint
        /// egy parti síkság.
        /// </summary>
        public const double RockAltitudeMeters = 2500.0;

        /// <summary>Sík felszín normál-perturbációs amplitúdója (a normál tangenciális eltolása, dimenziótlan).</summary>
        public const double PlainsNormalAmplitude = 0.06;

        /// <summary>Meredek/kőzetes felszín normál-perturbációs amplitúdója.</summary>
        public const double RockNormalAmplitude = 0.50;

        /// <summary>Sík felszín albedó-szórása (relatív, ±).</summary>
        public const double PlainsAlbedoJitter = 0.05;

        /// <summary>Meredek/kőzetes felszín albedó-szórása (relatív, ±).</summary>
        public const double RockAlbedoJitter = 0.14;

        /// <summary>Kőzetes felszín frekvencia-szorzója: a szirt textúrája finomabb szemcsés, mint a síkságé.</summary>
        public const double RockFrequencyScale = 2.0;

        /// <summary>
        /// A víz alatti felszín részlet-csillapítása. A tengerfenék üledékkel
        /// fedett és a vízen át nézve amúgy is szórt — de NEM nulla, mert a
        /// parti sekélyeken átlátszik.
        /// </summary>
        public const double SubmergedNormalScale = 0.30;

        /// <summary>Ld. <see cref="SubmergedNormalScale"/>.</summary>
        public const double SubmergedAlbedoScale = 0.40;

        /// <summary>
        /// A víz alatti csillapítás átmeneti sávja (m). SZÁNDÉKOSAN nem
        /// éles: egy ugrás a tengerszintnél pontosan a partvonalon adna
        /// látható gyűrűt — ez az a hibaosztály, amit az ND-129/ND-149 két
        /// körben javított, nem hozzuk vissza.
        /// </summary>
        public const double SubmergenceBandMeters = 200.0;

        /// <summary>Egy pont mikro-részlet-válasza (mind dimenziótlan, render-oldali).</summary>
        public readonly struct MicroDetailResponse
        {
            /// <summary>A normál tangenciális perturbációjának amplitúdója.</summary>
            public readonly double NormalAmplitude;

            /// <summary>Az albedó relatív modulációja (±).</summary>
            public readonly double AlbedoJitter;

            /// <summary>A zaj-frekvencia helyi szorzója.</summary>
            public readonly double FrequencyScale;

            public MicroDetailResponse(double normalAmplitude, double albedoJitter, double frequencyScale)
            {
                NormalAmplitude = normalAmplitude;
                AlbedoJitter = albedoJitter;
                FrequencyScale = frequencyScale;
            }
        }

        /// <summary>Kvintikus simítás a [0,1] sávra — ugyanaz a fade-görbe, mint a <see cref="FractalNoise"/>-ban (nincs új transzcendens).</summary>
        public static double Smoothstep01(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        /// <summary>
        /// A "kőzetesség" [0,1]: a lejtő ÉS a magasság közül a nagyobb hatás
        /// dönt (egy meredek parti szirt és egy magashegyi plató egyaránt
        /// kőzet). A lejtőt a felszíni normál és a radiális irány szöge adja:
        /// <paramref name="slopeSin"/> = sin(lejtőszög).
        /// </summary>
        public static double Rockiness(double slopeSin, double elevationMeters, double seaLevelMeters)
        {
            double bySlope = Smoothstep01(slopeSin / SlopeReferenceSin);
            double byAltitude = Smoothstep01((elevationMeters - seaLevelMeters) / RockAltitudeMeters);
            return bySlope > byAltitude ? bySlope : byAltitude;
        }

        /// <summary>
        /// [0,1] víz-alattiság: 0 a szárazföldön, 1 a
        /// <see cref="SubmergenceBandMeters"/>-nél mélyebb víz alatt.
        /// </summary>
        public static double Submergence(double elevationMeters, double seaLevelMeters)
            => Smoothstep01((seaLevelMeters - elevationMeters) / SubmergenceBandMeters);

        /// <summary>
        /// A mikro-részlet válasza egy pontban. TISZTA függvény: csak
        /// <c>+ - * /</c> és polinom (nincs transzcendens, nincs állapot).
        /// A shader ugyanezt számolja, de a konstansokat uniformként innen
        /// kapja.
        /// </summary>
        public static MicroDetailResponse Evaluate(double slopeSin, double elevationMeters, double seaLevelMeters)
        {
            double rock = Rockiness(slopeSin, elevationMeters, seaLevelMeters);
            double wet = Submergence(elevationMeters, seaLevelMeters);
            double normal = PlainsNormalAmplitude + (RockNormalAmplitude - PlainsNormalAmplitude) * rock;
            double albedo = PlainsAlbedoJitter + (RockAlbedoJitter - PlainsAlbedoJitter) * rock;
            double normalScale = 1.0 + (SubmergedNormalScale - 1.0) * wet;
            double albedoScale = 1.0 + (SubmergedAlbedoScale - 1.0) * wet;
            return new MicroDetailResponse(
                normal * normalScale,
                albedo * albedoScale,
                1.0 + (RockFrequencyScale - 1.0) * rock);
        }

        /// <summary>
        /// A mikro-részlet zaj-fázisa: seedenként más minta, de ugyanaz a seed
        /// mindig ugyanazt adja. A <see cref="RandomDomain.Decorative"/>
        /// domainben van, mert NEM a világmodell része (ugyanaz a besorolás,
        /// mint a csillagmezőé és a lemez-overlay színárnyalatáé) — az
        /// I1 garanciái nem terjednek ki rá, a determinizmus mégis megmarad.
        /// A [0, 1024) tartomány elég nagy ahhoz, hogy a különböző seedek
        /// látványosan más rács-régióban mintavételezzenek.
        /// </summary>
        public static void PhaseOffset(ulong worldSeed, out double x, out double y, out double z)
        {
            DeterministicRandom.Sample4(
                worldSeed, RandomDomain.Decorative, 0, 0,
                out double a, out double b, out double c, out double _,
                RandomProperty.MicroDetailPhase);
            x = a * 1024.0;
            y = b * 1024.0;
            z = c * 1024.0;
        }
    }
}
