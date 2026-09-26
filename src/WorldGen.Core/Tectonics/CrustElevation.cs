using WorldGen.Core.Random;
using WorldGen.Core.Terrain;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M4 kéreg-típus és alap-eleváció (§4.2, docs/05-milestones.md).
    ///
    /// HATÓKÖR (dokumentált egyszerűsítés, NEM ND — később finomíthető):
    ///   - Kéreg-típus PLATE-szinten (a spec §14.1 Plate struct-ja is így
    ///     modellezi: egy plate egyetlen CrustType mezővel rendelkezik).
    ///
    /// ND-31 (docs/04-decisions.md): az §13.2 "fraktál részlet" (F)
    /// korábban egyszerű, tile-onként FÜGGETLEN (fehér zaj-szerű)
    /// magasság-jitter volt — NEM térben koherens fBm/Perlin, dokumentált,
    /// ismert hiányosság ("túl szabályos" Voronoi-cella határok).
    ///
    /// ND-33 (docs/04-decisions.md): a sima fBm (ND-31) továbbra is túl
    /// simának/észrevehetetlennek bizonyult vizuálisan, még 4x
    /// amplitúdóval is (felhasználói visszajelzés). MOST "ridged
    /// multifractal" (<see cref="FractalNoise.RidgedMultifractal"/>, spec
    /// §13.1 nevesítve is említi) — éles gerinceket ad, jóval nagyobb
    /// vizuális kontraszttal, mint a sima fBm lekerekített dombjai.
    ///
    /// ND-34 (docs/04-decisions.md): további felhasználói visszajelzés —
    /// (a) az óceánfenék is túl erősen "hegyes" lett, holott a valóságban
    /// az óceáni relief szelídebb a kontinentálisnál; (b) a durvaság
    /// egyenletes volt a szárazföldön, holott realisztikusabb, ha van
    /// sík/fennsík ÉS hegyvidék is. Ezért: (1) óceáni tile-ok a zaj
    /// <see cref="OceanicNoiseFactor"/>-szorosát kapják csak; (2) egy
    /// külön, ALACSONY FREKVENCIÁS "hegyvidékiség" maszk (sima fBm, nem
    /// ridged) határozza meg REGIONÁLISAN, mennyire érvényesüljön a
    /// ridged részlet — nagy területek maradhatnak simák, mások
    /// dramatikusan durvák.
    /// </summary>
    public static class CrustElevation
    {
        public const double OceanicBaseMeters = -4000.0;
        public const double ContinentalBaseMeters = 800.0;
        // ND-33: tovabb emelve (2000->3000), ridged multifractalra valtva
        // a sima fBm helyett a nagyobb vizualis kontraszt erdekeben.
        public const double NoiseAmplitudeMeters = 3000.0;

        // ND-37 (docs/04-decisions.md): SZANDEKOSAN a SeaLevelCalibration
        // TargetWaterFraction-je (0.65) ALATT, attol decorrelalva. Korabban
        // 0.55 volt (kb. Fold-szeru arany), de a tile-sulyozott oceani-lemez-
        // arany (~66.7%) majdnem egybeesett a celzott viz-arannyal, ezert a
        // percentilis-alapu tengerszint-kalibracio a tengerszintet melyen az
        // oceani kereg elevaciotartomanyaba (OceanicBaseMeters korul) tuzte
        // ki - nem egy valodi kontinentalis-peremi atmenetnel. Ez adta a
        // felhasznaloi panaszt: a part "falszeruen" magasan logott a
        // tengerszint folott. 0.40-nel merve (level 6, 20 lemez,
        // world_seed=0xA7C944210000): a tile-sulyozott oceani-arany ~35.1%-ra
        // esik (messze a 65%-os viz-cel alatt), igy a percentilis-kalibracio
        // a legalacsonyabb fekvesu KONTINENTALIS tile-okba is belenyul
        // ("kontinentalis self" hatas) - a parti sav atlagos relativ
        // magassaga ~4072m-rol ~242.6m-re csokkent (94%). TEST-EARTH-001
        // valtozatlanul teljesul (65.0% viz, tobb kontinens).
        public const double DefaultOceanicProbability = 0.40;

        // ND-90: az óceáni/kontinentális kéreg teljes báziselevációja ezen a
        // lemezhatár-gap tartományon belül folytonosan keveredik. Azonos a
        // tektonikus uplift zónájával, de itt marad saját konstansként, hogy a
        // kéregmodell ne függjön visszafelé a PlateBoundaryEffect osztálytól.
        public const double DefaultBoundaryBlendGap = 0.005;

        // ND-34: az ocean-fenek szelidebb, mint a szarazfold.
        public const double OceanicNoiseFactor = 0.25;

        // ND-34: a "hegyvidekiseg" maszk parameterei - alacsony frekvencia
        // -> nagy, regionalis zonak; a gain a maszk fBm nyers tartomanyat
        // [0,1]-hez kozelebb nyujtja; a bias-power (>1) tobbnyire sik,
        // ritkabban dramatikusan durva teruleteket ad.
        public const double MountainMaskFrequency = 2.5;
        public const int MountainMaskOctaves = 3;
        public const double MountainMaskGain = 1.3;
        public const double MountainMaskBiasPower = 1.5;

        // ND-52 (docs/04-decisions.md), UJRAHANGOLVA (2026-09-07,
        // masodik felhasznaloi kor: "nem jott be... a masodik szintu
        // zaj... lehet e az egesz sikra kiterjedo folytonos zajt
        // hozzaadni?"): az EREDETI szandek "finom-lepteku, csak a
        // legkozelebbi zoomnal eszrevehetó reszlet-zaj" volt, DE
        // UTOLAGOS SZAMOLAS FELTARTA A TERVEZESI HIBAT: a
        // SecondaryNoisePeriodTiles=40 * egy level=5 referencia-tile
        // szogmerete egyutt kb. 0.3125-szorose a teljes nagykor
        // korulbelululeg, azaz a masodlagos zaj periodusa JOVAL
        // SZELESEBB, mint akar az ELSODLEGES zaj BAZIS-oktavja
        // (frequency=8 -> periodus 1/8=0.125, a masodlagosnak meg
        // frequency~0.51 -> periodus~1.96) - tehat ez SOHA nem is
        // adott "finom reszletet", hanem egy nagyon halvany (200m/3000m
        // = 1/15 amplitudoju), regionalis lepteku hullamzast, ami
        // gyakorlatilag eszrevehetetlen maradt BARMELY zoom-szinten.
        //
        // UJ CEL: mivel a periodus MAR EGYFAJTA "az egesz felszinre
        // kiterjedo, folytonosan ismetlodo" lepteket ad (kb. 3
        // hullamhossznyi egy nagykorön), a JAVITAS nem a frekvencian,
        // hanem az AMPLITUDON mult - jelentosen megemelve (200->900m,
        // az elsodleges kb. 30%-ara), hogy ez a mar eleve folytonos,
        // egesz-felszines hullamzas VEGRE lathatova valjon minden
        // zoom-szinten, ne csak egy soha-el-nem-ert extrem kozeli
        // nezetnel. A nev ("SecondaryDetailNoise") es a parameterek
        // neve valtozatlan maradt (elkerulve egy meg nagyobb
        // atnevezesi-kaszkadot), de a SZEREPE mostantol "masodlagos,
        // folytonos, regionalis lepteku dombormlat-textura", NEM
        // "kozeli-zoom reszlet".
        private const int SecondaryNoiseReferenceLevel = 5;
        private const int SecondaryNoisePeriodTiles = 40;
        public const double SecondaryNoiseAmplitudeMeters = 900.0;
        public const int SecondaryNoiseOctaves = 3;

        /// <summary>
        /// A két relief-TAG (már összeszorozva, de még amplitúdó nélkül)
        /// gömbfelszíni átlaga. Egyik sem nulla-átlagú: a ridged multifractal
        /// felfelé torzított, a másodlagos fBm szintén. Mérés (Python
        /// orákulum, 1014 minta level 6-on, hat különböző world seeden):
        /// <c>primary * mask</c> 0,1609..0,1727 → 0,165; <c>secondary</c>
        /// 0,3351..0,4882 → 0,42.
        ///
        /// HOL KELL (ND-137 / A20): a deep-time erózió EZEKHEZ az átlagokhoz
        /// relaxálja a relief-tagokat, nem nullához. Nullához relaxálva a
        /// kontinensek átlagosan ~490 m-t süllyednének — fiktív
        /// tömegveszteség, mert nincs izosztatikus kiegyenlítési modellünk,
        /// ami ezt (a valóságban ~80%-ban) visszaemelné. Az átlaghoz
        /// relaxálás ezt a kompenzációt építi be: a relief SIMUL
        /// (peneplanáció), de a kéreg átlagos magassága megmarad.
        /// </summary>
        public const double PrimaryReliefSphericalMean = 0.165;

        /// <summary>Ld. <see cref="PrimaryReliefSphericalMean"/>.</summary>
        public const double SecondaryReliefSphericalMean = 0.42;

        /// <summary>
        /// ND-137 (A20): egy dimenziótlan relief-tag csillapított értéke — a
        /// gömbfelszíni átlaga felé relaxál. <paramref name="decay"/> = 1,0
        /// esetén EGZAKT rövidzár (az <c>m + (x - m) * 1.0</c> alak nem adná
        /// vissza bitre x-et).
        /// </summary>
        public static double ErodedReliefTerm(double term, double sphericalMean, double decay)
            => decay == 1.0 ? term : sphericalMean + (term - sphericalMean) * decay;

        // Fix, a primer zaj racspontjaitol "elegge tavoli" koordinata-
        // eltolas (ld. DomainWarp.cs azonos mintaja), hogy a masodlagos
        // zaj NE korrelaljon az elsodlegessel (kulonben csak
        // felerositene/gyengitene azt ugyanazokon a helyeken, nem adna
        // FUGGETLEN reszletet).
        private const double SecondaryNoiseOffsetX = 41.19, SecondaryNoiseOffsetY = 17.83, SecondaryNoiseOffsetZ = 29.61;

        /// <summary>
        /// Egy <see cref="SecondaryNoiseReferenceLevel"/>-szintu referencia-
        /// tile KOZELITO szogmerete radianban (kockalap-el / 2^level).
        /// </summary>
        private static readonly double SecondaryNoiseReferenceTileRadians =
            (System.Math.PI / 2.0) / (1 << SecondaryNoiseReferenceLevel);

        /// <summary>
        /// A masodlagos zaj FractalNoise-frekvenciaja: a periodusa
        /// <see cref="SecondaryNoisePeriodTiles"/> darab referencia-tile-nyi.
        /// </summary>
        public static readonly double SecondaryNoiseFrequency =
            1.0 / (SecondaryNoisePeriodTiles * SecondaryNoiseReferenceTileRadians);

        /// <summary>Kéreg-típus lemezenként — determinisztikus Bernoulli-próba.</summary>
        public static bool IsOceanic(ulong worldSeed, int plateId, double oceanicProbability = DefaultOceanicProbability)
        {
            return DeterministicRandom.Chance(
                worldSeed, RandomDomain.Tectonics, (ulong)plateId, 0,
                oceanicProbability, RandomProperty.CrustType);
        }

        /// <summary>
        /// [0,1] regionális "hegyvidékiség" - alacsony frekvenciás, sima
        /// fBm (NEM ridged), hogy nagy, összefüggő zónákat adjon sík/durva
        /// területekre (ND-34).
        /// </summary>
        public static double MountainMask(ulong worldSeed, double x, double y, double z)
        {
            double m = FractalNoise.Fbm(worldSeed, x, y, z, MountainMaskFrequency, MountainMaskOctaves);
            double normalized = Clamp01(m * MountainMaskGain + 0.5);
            return System.Math.Pow(normalized, MountainMaskBiasPower);
        }

        /// <summary>
        /// ND-136 (A19): a <see cref="MountainMask"/> a lemez SAJÁT keretében.
        /// Külön van a <see cref="ComputeNoiseBasisInPlateFrame"/>-től, mert az
        /// uplift-út (ND-35) csak a maszkot kéri — nincs értelme mellé a két
        /// drága ridged-multifractal kiértékelést is elvégezni.
        /// </summary>
        public static double MountainMaskInPlateFrame(
            ulong worldSeed, int plateId, double timeMyr, double x, double y, double z)
        {
            PlateMotion.ToPlateFrame(
                worldSeed, plateId, timeMyr, x, y, z,
                out double px, out double py, out double pz);
            return MountainMask(worldSeed, px, py, pz);
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        /// <summary>
        /// ND-52: masodlagos, finom-lepteku dombormlat-zaj [-1,1]-hez
        /// kozeli tartomanyban - UGYANAZ a <see cref="FractalNoise.RidgedMultifractal"/>
        /// algoritmus, mint az elsodleges, csak <see cref="SecondaryNoiseFrequency"/>-n
        /// (ld. osztaly-doksi) es kulon koordinata-eltolassal (dekorrelacio).
        /// </summary>
        public static double SecondaryDetailNoise(ulong worldSeed, double x, double y, double z)
        {
            double r = FractalNoise.RidgedMultifractal(
                worldSeed,
                x + SecondaryNoiseOffsetX, y + SecondaryNoiseOffsetY, z + SecondaryNoiseOffsetZ,
                SecondaryNoiseFrequency, SecondaryNoiseOctaves);
            return (r - 0.5) * 2.0;
        }

        /// <summary>
        /// Az eleváció zajtagjai egy adott nyers, VILÁG-keretes pozíción.
        ///
        /// ND-63 eredetileg azt rögzítette, hogy ezek a részeredmények
        /// kizárólag a world seedtől és a pozíciótól függenek. ND-136 (A19)
        /// óta ez már csak a <c>t = 0</c> útra igaz: deep-time-ban a zajt a
        /// lemez SAJÁT keretében kell mintavételezni
        /// (<see cref="ComputeNoiseBasisInPlateFrame"/>), különben a
        /// lemezhatár átcsúszik egy álló textúra felett. Ez a túlterhelés
        /// tehát a <c>t = 0</c> (és a cache-elt bázis) útja marad.
        /// </summary>
        public static void ComputeNoiseBasis(
            ulong worldSeed, double x, double y, double z,
            out double primaryNoise, out double mountainMask, out double secondaryNoise)
        {
            // ridged_multifractal kb. [0,1]-hez kozeli, atlagosan ~0.7
            // korul - (r-0.5)*2-vel [-1,1]-hez kozeli, ELOJELES
            // modositova alakitva, hogy ne csak felfele toljon.
            double r = FractalNoise.RidgedMultifractal(worldSeed, x, y, z);
            primaryNoise = (r - 0.5) * 2.0;
            mountainMask = MountainMask(worldSeed, x, y, z);
            // ND-52: a masodlagos zaj SZANDEKOSAN nem kap MountainMask-ot;
            // az oceani amplitudo-csokkentes viszont az osszeallitasban erre
            // a tagra is ervenyes.
            secondaryNoise = SecondaryDetailNoise(worldSeed, x, y, z);
        }

        /// <summary>
        /// ND-136 (A19): ugyanaz a három zajtag, de a
        /// <paramref name="plateId"/> lemez SAJÁT vonatkoztatási rendszerében
        /// mintavételezve — a világ-pozíciót előbb
        /// <see cref="PlateMotion.ToPlateFrame"/> forgatja vissza
        /// <c>R(-ωt)</c>-vel. Így a domborzat-textúra a kéreggel együtt
        /// vándorol; a korábbi, rögzített világ-pozíciós mintavétel mellett a
        /// hegyvonulat helyben maradt, miközben a lemez elvándorolt alóla.
        ///
        /// <paramref name="timeMyr"/> = 0-nál bitre azonos a
        /// <see cref="ComputeNoiseBasis"/> eredményével (a visszaforgatás
        /// egzakt azonosság, ld. <see cref="PlateMotion.ToPlateFrame"/>).
        /// </summary>
        public static void ComputeNoiseBasisInPlateFrame(
            ulong worldSeed, int plateId, double timeMyr, double x, double y, double z,
            out double primaryNoise, out double mountainMask, out double secondaryNoise)
        {
            PlateMotion.ToPlateFrame(
                worldSeed, plateId, timeMyr, x, y, z,
                out double px, out double py, out double pz);
            ComputeNoiseBasis(worldSeed, px, py, pz, out primaryNoise, out mountainMask, out secondaryNoise);
        }

        /// <summary>
        /// A <see cref="ComputeNoiseBasis"/> által előállított, időfüggetlen
        /// tagokból állítja össze a plate-függő alap-elevációt. A műveleti
        /// sorrend megegyezik a korábbi <see cref="BaseElevation"/> útéval.
        /// </summary>
        public static double BaseElevationFromNoiseBasis(
            ulong worldSeed, int plateId,
            double primaryNoise, double mountainMask, double secondaryNoise,
            out bool isOceanic,
            double oceanicProbability = DefaultOceanicProbability,
            double primaryDecay = 1.0, double secondaryDecay = 1.0)
        {
            isOceanic = IsOceanic(worldSeed, plateId, oceanicProbability);
            double baseValue = isOceanic ? OceanicBaseMeters : ContinentalBaseMeters;
            double amplitude = NoiseAmplitudeMeters * (isOceanic ? OceanicNoiseFactor : 1.0);
            double secondaryAmplitude = SecondaryNoiseAmplitudeMeters * (isOceanic ? OceanicNoiseFactor : 1.0);
            // ND-137 (A20): a deep-time erózió a két relief-tag amplitúdóját
            // KÜLÖN csillapítja (hullámhossz-szelektív kopás). Az 1,0/1,0
            // rövidzár garantálja, hogy az erózió NÉLKÜLI út (és így a t = 0
            // világ) bitre változatlan maradjon.
            if (primaryDecay == 1.0 && secondaryDecay == 1.0)
                return baseValue + primaryNoise * mountainMask * amplitude + secondaryNoise * secondaryAmplitude;

            // FONTOS: az elsődleges tagnál a MASZKKAL MÁR ÖSSZESZOROZVA
            // relaxálunk, nem a nyers zajértékre — különben az
            // `átlag * mask * amplitúdó` rész csillapítatlan maradna, és a
            // hegységöv a végtelenségig őrizne egy ~1300 m-es, mask-alakú
            // reliefet.
            double primaryTerm = ErodedReliefTerm(
                primaryNoise * mountainMask, PrimaryReliefSphericalMean, primaryDecay);
            double secondaryTerm = ErodedReliefTerm(
                secondaryNoise, SecondaryReliefSphericalMean, secondaryDecay);
            return baseValue + primaryTerm * amplitude + secondaryTerm * secondaryAmplitude;
        }

        /// <summary>
        /// ND-90: a két legközelebbi lemez között folytonos báziseleváció.
        /// A lemezhatáron 50/50 keverést, a zóna külső szélén tisztán a
        /// legközelebbi lemez értékét adja. A smoothstep polinom miatt mindkét
        /// végponton nulla a súly deriváltja; nincs új transzcendens művelet.
        /// Az isOceanic továbbra is a nyertes lemez anyagtulajdonsága, csak az
        /// eleváció válik folytonossá.
        ///
        /// Ez a túlterhelés MINDKÉT lemezre UGYANAZT a zaj-bázist használja —
        /// a <c>t = 0</c> (világ-keretes) út. A deep-time út a
        /// <see cref="BlendedBaseElevationFromPlateFrameBases"/>-t hívja, ahol
        /// a két lemez zaja már eltér.
        /// </summary>
        public static double BlendedBaseElevationFromNoiseBasis(
            ulong worldSeed,
            double best, double second, int bestIndex, int secondIndex,
            double primaryNoise, double mountainMask, double secondaryNoise,
            out bool isOceanic,
            double blendGap = DefaultBoundaryBlendGap)
        {
            return BlendedBaseElevationFromPlateFrameBases(
                worldSeed, best, second, bestIndex, secondIndex,
                primaryNoise, mountainMask, secondaryNoise,
                primaryNoise, mountainMask, secondaryNoise,
                out isOceanic, blendGap);
        }

        /// <summary>
        /// ND-136 (A19): ugyanaz az ND-90 keverés, de a két lemez zaj-bázisa
        /// KÜLÖN érkezik — mindegyik a saját lemezének keretében
        /// mintavételezve (<see cref="ComputeNoiseBasisInPlateFrame"/>).
        ///
        /// FONTOS KÜLÖNBSÉG az ND-90 eredeti alakjához képest: onnan kikerült
        /// a „csak eltérő kéregtípusnál keverünk" kikötés. Az korábban helyes
        /// volt, mert azonos kéregtípusnál a két lemez UGYANABBÓL a három
        /// számból számolt, tehát a függvény amúgy is folytonos volt.
        /// Lemez-keretes zajjal viszont a határ két oldalán MÁS zajérték áll,
        /// így keverés nélkül MINDEN határ szakadásossá válna — nem csak a
        /// kéregtípus-váltó.
        ///
        /// A <c>t = 0</c> kimenet ettől BITRE változatlan: ott a két bázis
        /// azonos, tehát azonos kéregtípusnál a két eleváció is bitre egyenlő,
        /// és <c>best + (best - best) * w == best</c>.
        /// </summary>
        public static double BlendedBaseElevationFromPlateFrameBases(
            ulong worldSeed,
            double best, double second, int bestIndex, int secondIndex,
            double bestPrimaryNoise, double bestMountainMask, double bestSecondaryNoise,
            double secondPrimaryNoise, double secondMountainMask, double secondSecondaryNoise,
            out bool isOceanic,
            double blendGap = DefaultBoundaryBlendGap,
            double primaryDecay = 1.0, double secondaryDecay = 1.0)
        {
            // ND-137 (A20): UGYANAZ a két csillapító tényező mindkét lemezre —
            // az erózió a FELSZÍNEN hat, nem lemezenként (a tényezők tisztán
            // pozíció- és időfüggők, ld. DeepTimeErosionGlaciation).
            double bestElevation = BaseElevationFromNoiseBasis(
                worldSeed, bestIndex, bestPrimaryNoise, bestMountainMask, bestSecondaryNoise,
                out isOceanic, DefaultOceanicProbability, primaryDecay, secondaryDecay);

            double gap = best - second;
            if (secondIndex < 0 || gap >= blendGap || !(blendGap > 0.0))
                return bestElevation;

            double secondElevation = BaseElevationFromNoiseBasis(
                worldSeed, secondIndex, secondPrimaryNoise, secondMountainMask, secondSecondaryNoise,
                out _, DefaultOceanicProbability, primaryDecay, secondaryDecay);

            double normalizedGap = gap / blendGap;
            double smoothGap = normalizedGap * normalizedGap * (3.0 - 2.0 * normalizedGap);
            double secondWeight = 0.5 * (1.0 - smoothGap);
            return bestElevation + (secondElevation - bestElevation) * secondWeight;
        }

        /// <summary>A tile alap-magassága méterben: kéreg-típus bázis + térben koherens, maszkolt ridged zaj.</summary>
        public static double BaseElevation(
            ulong worldSeed, int plateId, double x, double y, double z, out bool isOceanic,
            double oceanicProbability = DefaultOceanicProbability)
        {
            ComputeNoiseBasis(
                worldSeed, x, y, z,
                out double primaryNoise, out double mountainMask, out double secondaryNoise);
            return BaseElevationFromNoiseBasis(
                worldSeed, plateId, primaryNoise, mountainMask, secondaryNoise,
                out isOceanic, oceanicProbability);
        }
    }

    /// <summary>
    /// Egy nyers gömbfelszíni pozíció world-seed-függő, de deep-time-
    /// független részeredményei (ND-63). Nem tartalmaz plate ID-t, kéregtípust,
    /// upliftet, eróziót vagy más időfüggő állapotot.
    ///
    /// ND-136 (A19) ÓTA PONTOSÍTVA. A <see cref="WarpedX"/>/<see cref="WarpedY"/>/
    /// <see cref="WarpedZ"/> továbbra is tisztán pozíció-függő (a lemez-topológia
    /// a világ keretében dől el), tehát a lemez-gyorsítótár (ND-122/ND-131)
    /// RÁJUK deep-time-ban is érvényes marad — és épp ez a drága, három fBm-es
    /// domain warp. A három ZAJTAG viszont már csak <c>t = 0</c>-ra érvényes:
    /// <c>t &gt; 0</c>-nál a zaj a lemez saját keretében értékelődik ki, ezért
    /// az <see cref="EvaluateAtTime"/> újraszámolja őket (a cache-elteket csak
    /// <c>t = 0</c>-nál használja).
    /// </summary>
    public readonly struct TerrainPointBasis
    {
        public readonly double WarpedX;
        public readonly double WarpedY;
        public readonly double WarpedZ;
        public readonly double PrimaryNoise;
        public readonly double MountainMask;
        public readonly double SecondaryNoise;

        public TerrainPointBasis(
            double warpedX, double warpedY, double warpedZ,
            double primaryNoise, double mountainMask, double secondaryNoise)
        {
            WarpedX = warpedX;
            WarpedY = warpedY;
            WarpedZ = warpedZ;
            PrimaryNoise = primaryNoise;
            MountainMask = mountainMask;
            SecondaryNoise = secondaryNoise;
        }

        public static TerrainPointBasis Compute(ulong worldSeed, double x, double y, double z)
        {
            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            CrustElevation.ComputeNoiseBasis(
                worldSeed, x, y, z,
                out double primaryNoise, out double mountainMask, out double secondaryNoise);
            return new TerrainPointBasis(wx, wy, wz, primaryNoise, mountainMask, secondaryNoise);
        }

        /// <summary>
        /// A cache-elt bázisból és az aktuálisan mozgatott lemezmagokból adja
        /// vissza az időbeli relaxáció ELŐTTI base/uplift komponenseket.
        ///
        /// Ez a <c>t = 0</c> út: a cache-elt, VILÁG-keretes zajtagokat
        /// használja. Deep-time-ban az <see cref="EvaluateAtTime"/> kell
        /// (ND-136), különben a domborzat nem mozog a lemezzel.
        /// </summary>
        public void Evaluate(
            ulong worldSeed, (double X, double Y, double Z)[] seeds,
            out double baseElevation, out double uplift, out bool isOceanic)
        {
            PlateBoundaryEffect.TwoBestDots(
                WarpedX, WarpedY, WarpedZ, seeds,
                out double best, out double second, out int bestIndex, out int secondIndex);
            baseElevation = CrustElevation.BlendedBaseElevationFromNoiseBasis(
                worldSeed, best, second, bestIndex, secondIndex,
                PrimaryNoise, MountainMask, SecondaryNoise, out isOceanic);
            uplift = PlateBoundaryEffect.BoundaryUpliftFromNearestPlates(
                worldSeed, best, second, bestIndex, secondIndex, MountainMask);
        }

        /// <summary>
        /// ND-136 (A19): ugyanaz, de a zaj a lemez saját keretében
        /// értékelődik ki <paramref name="timeMyr"/>-nél.
        ///
        /// A cache-elt WARP (a drága, három fBm-es domain warp) itt is
        /// hasznosul — csak a zajtagokat kell újraszámolni. A nyers
        /// <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/>
        /// ezért kell: a cache a warpot tárolja, a nyers pozíciót nem.
        ///
        /// <paramref name="timeMyr"/> = 0-nál bitre azonos az
        /// <see cref="Evaluate"/> eredményével, és el is kerüli a
        /// zaj-újraszámolást (a cache-elt tagokat használja).
        /// </summary>
        public void EvaluateAtTime(
            ulong worldSeed, (double X, double Y, double Z)[] seeds,
            double x, double y, double z, double timeMyr,
            out double baseElevation, out double uplift, out bool isOceanic,
            double erosionTimeMyr = 0.0)
        {
            if (timeMyr == 0.0 && erosionTimeMyr == 0.0)
            {
                Evaluate(worldSeed, seeds, out baseElevation, out uplift, out isOceanic);
                return;
            }

            // ND-137 (A20): a relief-csillapitas tisztan pozicio- es
            // ido-fuggo, tehat a cache-elt (t=0) zajtagokra is alkalmazhato -
            // ezert `timeMyr == 0 && erosionTimeMyr > 0` eseten is helyes
            // eredmenyt ad az alabbi (ujraszamolo) ag: a ToPlateFrame t=0-ra
            // egzakt azonossag.
            DeepTimeErosionGlaciation.ReliefDecayFactors(
                z, erosionTimeMyr, out double primaryDecay, out double secondaryDecay);

            PlateBoundaryEffect.TwoBestDots(
                WarpedX, WarpedY, WarpedZ, seeds,
                out double best, out double second, out int bestIndex, out int secondIndex);
            CrustElevation.ComputeNoiseBasisInPlateFrame(
                worldSeed, bestIndex, timeMyr, x, y, z,
                out double bestPrimary, out double bestMask, out double bestSecondary);

            double secondPrimary = bestPrimary, secondMask = bestMask, secondSecondary = bestSecondary;
            if (secondIndex >= 0 && best - second < CrustElevation.DefaultBoundaryBlendGap)
            {
                CrustElevation.ComputeNoiseBasisInPlateFrame(
                    worldSeed, secondIndex, timeMyr, x, y, z,
                    out secondPrimary, out secondMask, out secondSecondary);
            }

            baseElevation = CrustElevation.BlendedBaseElevationFromPlateFrameBases(
                worldSeed, best, second, bestIndex, secondIndex,
                bestPrimary, bestMask, bestSecondary,
                secondPrimary, secondMask, secondSecondary,
                out isOceanic, CrustElevation.DefaultBoundaryBlendGap,
                primaryDecay, secondaryDecay);
            uplift = PlateBoundaryEffect.BoundaryUpliftFromNearestPlates(
                worldSeed, best, second, bestIndex, secondIndex, bestMask);
        }
    }
}
