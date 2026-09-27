using System;
using WorldGen.Core.Numerics;
using WorldGen.Core.Random;
using WorldGen.Core.Terrain;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// M13 (ND-154): a TÉRFOGATI felhő render-paraméterei — a felhőhéj
    /// geometriája, optikája és részlet-létrája.
    ///
    /// MI EZ. A felhő-MVP (M6) egyetlen, LAPOS quad-héj volt: a csapadék-mező
    /// a vertex-alfába került, és a réteg egy végtelenül vékony, kétdimenziós
    /// lap maradt — a limbnél nem volt vastagsága, nem árnyékolta a felszínt,
    /// és a megvilágítása egyetlen Lambert-tag volt. Ez az osztály annak a
    /// hézagnak a modell-oldala: a felhőnek ALJA, TETEJE, SŰRŰSÉGE és OPTIKAI
    /// MÉLYSÉGE van, a shader pedig ebben a héjban raymarchol
    /// (ld. <c>Assets/Shaders/CloudVolume.shader</c>).
    ///
    /// MIÉRT NEM A HDRP VOLUMETRIKUS FELHŐJE. Ld. ND-21 (ELUTASÍTVA, négy
    /// MÉRT blokkolóval): a rétegvastagság alsó korlátja a mi léptékünkben
    /// 7420 km vastag héjat írt elő, a sűrűség-normalizálás a Föld sugarát
    /// drótozza be, a felhőtérképes út a shaderben kizárja a fél bolygót, és
    /// ami renderel (<c>Simple</c> preset), annak a lefedettsége KONSTANS a
    /// shaderben — I3-sértés. Ezért saját, gömbi raymarch.
    ///
    /// MIÉRT NEM SÉRTI AZ I3-at. A felhő LEFEDETTSÉGE a világmodell MÉRT
    /// mennyisége: a <see cref="MoisturePrecipitation.PrecipitationField"/>
    /// csapadéka, ugyanazzal a domain-relatív (óceán/szárazföld külön)
    /// percentilis-alakítással, amit a lapos MVP is használt (ld.
    /// <see cref="ShapeCoverage"/>). Az ALJA az eleváció-mezőből és a
    /// lefedettségből SZÁRMAZIK (<see cref="CloudBaseAboveSeaLevelMeters"/>),
    /// a VASTAGSÁGA a lefedettségből és az orografikus emelésből. A shaderben
    /// csak (a) a függőleges profil ALAKJA és (b) a részlet-oktávok vannak —
    /// utóbbi a modell saját zaj-létrájának folytatása a lefedettség-mező
    /// felbontása ALATT (ugyanaz a szerkezet, mint az ND-151 mikro-részleténél),
    /// és MULTIPLIKATÍV: ahol a lefedettség nulla, ott a részlet is egzaktul
    /// nullát ad, tehát felhő SOHA nem keletkezik a modelltől függetlenül.
    ///
    /// MIÉRT NEM ÉRINTI AZ I1-et. Minden itteni érték KIZÁRÓLAG render-kimenet:
    /// egyetlen mezőbe, gyorsítótárba, hash-be vagy mentésbe sem folyik vissza.
    /// A GPU-oldali raymarch bitpontossága platformfüggő — éppen ezért nem
    /// szabad semminek visszacsatolnia belőle (ez a döntő eltérés az ND-128-ban
    /// TÖRÖLT GPU-elevációtól, ami a klasszifikáció BEMENETE volt).
    ///
    /// A HLSL-MÁSOLAT KÉRDÉSE. A shader a leképezések ALAKJÁT tükrözi, a
    /// SZÁMOKAT viszont uniformként INNEN kapja — egyetlen igazságforrás van,
    /// ez az osztály (ND-151 óta bevált minta, így az ND-128 hibaosztálya,
    /// a két egymástól elcsúszó számkészlet, strukturálisan nem alakulhat ki).
    /// </summary>
    public static class CloudVolume
    {
        // -------------------------------------------------------------------
        // 1. LEFEDETTSÉG — a csapadék-mezőből
        // -------------------------------------------------------------------

        /// <summary>
        /// A felső percentilis, ahol a lefedettség 1-et ér el. A lapos MVP-vel
        /// AZONOS érték (a <c>ComputeCloudMeshData</c> <c>CeilingPercentile</c>-je),
        /// hogy a térfogati réteg ugyanazt a csapadék→lefedettség skálát lássa.
        /// </summary>
        public const double CeilingPercentile = 0.97;

        /// <summary>
        /// A nyers csapadék alakítása [0,1] lefedettségre egy domain (óceán
        /// VAGY szárazföld) saját padló/plafon-értékével. A padló alatt
        /// EGZAKTUL nulla — ez az a garancia, amire a shader multiplikatív
        /// részlete épül (nulla lefedettség → nincs felhő).
        /// </summary>
        public static double ShapeCoverage(double precip, double floor, double ceiling, double gamma)
        {
            // A NaN VÉDELEM nem elméleti: egy NaN lefedettség a GPU-n a teljes
            // raymarchot megfertőzné (a NaN minden műveleten átterjed), és a
            // hiba egyetlen fehér/cián pixelblokként jelenne meg, nem
            // adathibaként. A csapadék-mező nem ad NaN-t - de a vágás ITT van.
            if (double.IsNaN(precip)) return 0.0;
            if (precip <= floor) return 0.0;
            double span = ceiling - floor;
            if (span <= 0.0) return 1.0;
            double t = (precip - floor) / span;
            if (t >= 1.0) return 1.0;
            return DeterministicMath.Pow(t, gamma);
        }

        /// <summary>
        /// A KÉSZ (0..1) lefedettségek keverése a rácspont óceán-aránya
        /// szerint. FONTOS, MÉRÉSSEL FELTÁRT CSAPDA (M6, felhasználói
        /// visszajelzés: „a szárazföld felett továbbra is alig van felhő”): a
        /// KÜSZÖBÖT interpolálni HIBA, mert a legtöbb szárazföld part közelében
        /// van, tehát pozitív óceán-arányú, és a küszöb így az óceáni (magasabb)
        /// érték felé tolódott. A nyers csapadékot KÜLÖN kell alakítani mindkét
        /// domain saját padlójával, és a KÉSZ értéket keverni — ezt teszi ez.
        /// </summary>
        public static double BlendByOceanFraction(double landCoverage, double oceanCoverage, double oceanFraction)
        {
            double f = oceanFraction <= 0.0 ? 0.0 : (oceanFraction >= 1.0 ? 1.0 : oceanFraction);
            return landCoverage + (oceanCoverage - landCoverage) * f;
        }

        // -------------------------------------------------------------------
        // 2. FÜGGŐLEGES SZERKEZET — alj és vastagság a modellből
        // -------------------------------------------------------------------

        /// <summary>Relatív páratartalom a lefedettség nulla végén (%).</summary>
        public const double RelativeHumidityDryPercent = 40.0;

        /// <summary>Relatív páratartalom a teljes lefedettségnél (%).</summary>
        public const double RelativeHumidityWetPercent = 95.0;

        /// <summary>
        /// Az emelési kondenzációs szint (LCL) meredeksége a
        /// páratartalom-hiány szerint, m/%. Lawrence (2005, BAMS 86(2):
        /// „The Relationship between Relative Humidity and the Dewpoint
        /// Temperature in Moist Air”) két közelítéséből SZÁRMAZTATVA:
        /// z_LCL ≈ 125 m·(T − T_d) és (T − T_d) ≈ (100 − RH)/5 →
        /// z_LCL ≈ 25 m·(100 − RH). A modell nem hordoz harmatpontot, ezért a
        /// páratartalom-only alak kell; a hőmérséklet-tagot a Lawrence-féle
        /// közelítés éppen kiejti, tehát ez NEM elhanyagolás.
        /// </summary>
        public const double LclMetersPerHumidityPercent = 25.0;

        /// <summary>
        /// A felhőalap legkisebb TALAJ FÖLÖTTI magassága (m). A tiszta LCL
        /// teljes telítésnél 125 m-t adna — az már köd, nem felhő, és a
        /// raymarch szempontjából a felszíni mesh-be vágna.
        /// </summary>
        public const double MinBaseAboveGroundMeters = 250.0;

        /// <summary>A csomagolás (atlasz) felső korlátja a felhőalapra, m a tengerszint fölött.</summary>
        public const double MaxBaseAboveSeaLevelMeters = 12000.0;

        /// <summary>Rétegfelhő-vastagság a lefedettség alsó végén (m) — stratus.</summary>
        public const double StratusThicknessMeters = 500.0;

        /// <summary>
        /// A vastagság konvektív tartaléka teljes lefedettségnél (m). A
        /// stratus-vastagsághoz ADÓDIK, tehát teljes lefedettségnél
        /// 500 + 9000 = 9500 m — zivatarfelhő-lépték.
        /// </summary>
        public const double ConvectiveThicknessMeters = 9000.0;

        /// <summary>
        /// Orografikus vastagság-tartalék: a tengerszint fölötti TALAJ
        /// magasságának ennyiszerese adódik a vastagsághoz (dimenziótlan).
        /// Egy 3000 m-es hegylánc fölött +1050 m felhő teljes lefedettségnél —
        /// ez a „felhőbe burkolt hegygerinc”, valódi orografikus jelenség, és
        /// a MÁR KISZÁMÍTOTT eleváció-mezőből jön, ÚJ kiértékelés nélkül.
        /// </summary>
        public const double OrographicThicknessFactor = 0.35;

        /// <summary>A csomagolás (atlasz) felső korlátja a vastagságra (m).</summary>
        public const double MaxThicknessMeters = 12000.0;

        /// <summary>
        /// A héj KÜLSŐ sugara a tengerszint fölött (m) — a geometria
        /// konzervatív burkolójához. A két csomagolási plafon összege, tehát
        /// SZÁRMAZTATOTT: az atlaszban ábrázolható legmagasabb felhőtető.
        /// </summary>
        public const double ShellOuterAboveSeaLevelMeters = MaxBaseAboveSeaLevelMeters + MaxThicknessMeters;

        /// <summary>Relatív páratartalom (%) a lefedettségből — lineáris, dokumentált végpontokkal.</summary>
        public static double RelativeHumidityPercent(double coverage)
        {
            double c = coverage <= 0.0 ? 0.0 : (coverage >= 1.0 ? 1.0 : coverage);
            return RelativeHumidityDryPercent + (RelativeHumidityWetPercent - RelativeHumidityDryPercent) * c;
        }

        /// <summary>Az emelési kondenzációs szint a TALAJ fölött (m).</summary>
        public static double LiftingCondensationLevelMeters(double coverage)
        {
            double lcl = LclMetersPerHumidityPercent * (100.0 - RelativeHumidityPercent(coverage));
            return lcl < MinBaseAboveGroundMeters ? MinBaseAboveGroundMeters : lcl;
        }

        /// <summary>
        /// A felhőalap a TENGERSZINT fölött (m). A felhőalap a TALAJ fölött
        /// képződik, ezért a domborzat fölött megemelkedik — így a felhőtakaró
        /// ráborul a hegyláncra, nem átvágja azt.
        /// </summary>
        public static double CloudBaseAboveSeaLevelMeters(double coverage, double groundAboveSeaLevelMeters)
        {
            double ground = groundAboveSeaLevelMeters > 0.0 ? groundAboveSeaLevelMeters : 0.0;
            double b = ground + LiftingCondensationLevelMeters(coverage);
            return b > MaxBaseAboveSeaLevelMeters ? MaxBaseAboveSeaLevelMeters : b;
        }

        /// <summary>
        /// A felhő vastagsága (m): rétegfelhő-alap + konvektív tag a
        /// lefedettség KVADRÁTJÁVAL (a mély konvekció a legcsapadékosabb
        /// sávokra koncentrálódik, nem lineárisan) + orografikus tag.
        /// </summary>
        public static double CloudThicknessMeters(double coverage, double groundAboveSeaLevelMeters)
        {
            double c = coverage <= 0.0 ? 0.0 : (coverage >= 1.0 ? 1.0 : coverage);
            double ground = groundAboveSeaLevelMeters > 0.0 ? groundAboveSeaLevelMeters : 0.0;
            double t = StratusThicknessMeters
                + ConvectiveThicknessMeters * c * c
                + OrographicThicknessFactor * ground * c;
            return t > MaxThicknessMeters ? MaxThicknessMeters : t;
        }

        // -------------------------------------------------------------------
        // 3. OPTIKA
        // -------------------------------------------------------------------

        /// <summary>
        /// Kioltási együttható teljes lefedettségnél (1/m). Egy 500 m-es,
        /// teljes lefedettségű rétegfelhő optikai mélysége ezzel 10 — a mért
        /// stratus-optikai mélység (10…40) alsó sávja; egy 9500 m-es
        /// zivatarfelhő 190-et ad, azaz teljesen opak. A felhő tehát NEM
        /// „félig átlátszó festék”, hanem optikai mélységben viselkedik.
        /// </summary>
        public const double ExtinctionPerMeter = 0.02;

        /// <summary>
        /// A Henyey–Greenstein-szórás előreszórási aszimmetriája. 0,62 a
        /// vízcsepp-felhőkre jellemző effektív érték nagyságrendje (a Mie-szórás
        /// erősen előreszóró) — ez adja a Nap felé nézett felhő ragyogó peremét.
        /// </summary>
        public const double ForwardScatterG = 0.62;

        /// <summary>
        /// Egy TELJESEN beborult égbolt diffúz áteresztése: a felhő alatti
        /// felszínt ennyi (a direkt napfényhez mért) SZÓRT fény éri, amikor a
        /// direkt sugárzást a felhő teljesen elnyelte. A mért érték
        /// borult réteg alatt a globális sugárzás ~20-30%-a — enélkül a
        /// felhőárnyék FEKETE lenne (τ = 10 mellett a Beer–Lambert-áteresztés
        /// 4,5·10⁻⁵), ami se nem fizikai, se nem nézhető. A többszörös
        /// szórás pótlása, nem kozmetika.
        /// </summary>
        public const double OvercastDiffuseTransmission = 0.25;

        /// <summary>
        /// A felhőárnyék optikai mélységéhez használt ÁTLAGOS profil-érték: a
        /// <see cref="VerticalProfile"/> integrálja a [0,1] sávon. A Nap
        /// sugara a teljes rétegen áthalad, tehát nem a pontbeli, hanem az
        /// átlagolt sűrűség számít. SZÁRMAZTATOTT érték: a két kvintikus fade
        /// szorzatának integrálja a [0,1] sávon PONTOSAN 1 − (0,18 + 0,42)/2
        /// = 0,7, mert a kvintikus fade szimmetrikus, tehát mindkét sáv
        /// épp a felét veszi el. Teszt (<c>VerticalProfileMean_MatchesTheNumericIntegral</c>)
        /// numerikusan visszaellenőrzi.
        /// </summary>
        public const double VerticalProfileMean = 0.7;

        /// <summary>
        /// A felhőárnyék optikai mélysége a felszínen: a teljes rétegen át,
        /// a Nap zenitszögével megnyújtott úton.
        /// <paramref name="sunCosZenith"/> a felszíni normális és a Nap-irány
        /// koszinusza; kis értékeknél vágva, mert a lapos beesésnél a
        /// sík-párhuzamos közelítés elveszti az értelmét.
        /// </summary>
        public static double ShadowOpticalDepth(double coverage, double thicknessMeters, double sunCosZenith)
        {
            if (double.IsNaN(coverage) || coverage <= 0.0 || thicknessMeters <= 0.0) return 0.0;
            double cov = coverage > 1.0 ? 1.0 : coverage;
            double thick = thicknessMeters > MaxThicknessMeters ? MaxThicknessMeters : thicknessMeters;
            double cos = sunCosZenith < 0.15 || double.IsNaN(sunCosZenith) ? 0.15 : (sunCosZenith > 1.0 ? 1.0 : sunCosZenith);
            return ExtinctionPerMeter * cov * VerticalProfileMean * thick / cos;
        }

        /// <summary>
        /// A DIREKT napfény szorzója a felszínen a felhő alatt: a
        /// Beer–Lambert-áteresztés, plusz a borult égbolt diffúz padlója
        /// (<see cref="OvercastDiffuseTransmission"/>) a felhővel takart
        /// hányadra. Nulla lefedettségnél EGZAKTUL 1 — a kép a felhő nélkül
        /// bitre a korábbi.
        /// </summary>
        public static double SurfaceSunlightFactor(double coverage, double thicknessMeters, double sunCosZenith)
        {
            if (double.IsNaN(coverage) || coverage <= 0.0) return 1.0;
            double cov = coverage > 1.0 ? 1.0 : coverage;
            double transmitted = Transmittance(ShadowOpticalDepth(cov, thicknessMeters, sunCosZenith));
            double shadowed = transmitted + (1.0 - transmitted) * OvercastDiffuseTransmission;
            return 1.0 + (shadowed - 1.0) * cov;
        }

        /// <summary>
        /// A normált (izotróp = 1) fázisfüggvény csúcsának VÁGÁSA. A
        /// szingle-scatter Henyey–Greenstein-csúcs g = 0,62-nél 11,2× —
        /// fizikailag ennyi, de a MEGFIGYELT felhőperem-felfényesedés ennél
        /// lapítottabb, mert a TÖBBSZÖRÖS szórás elmossa a csúcsot (amit egy
        /// egyszeres szórású modell definíció szerint nem tartalmaz). A vágás
        /// tehát a hiányzó többszörös szórás pótlása, nem kozmetika; 4×-nél a
        /// Nap felé nézett felhőperem még jól látható fényes szegélyt kap.
        /// </summary>
        public const double PhasePeakCap = 4.0;

        /// <summary>
        /// A felhő TERMINÁTORÁNAK félsávja a Nap-zenitszög koszinuszában.
        ///
        /// MIÉRT KELL (MÉRT HIBA JAVÍTÁSA). A Nap felé mért optikai mélység
        /// csak azt mondja meg, mennyi FELHŐ van a minta fölött — azt nem,
        /// hogy a Nap egyáltalán a horizont FÖLÖTT van-e. Enélkül az
        /// ÉJSZAKAI oldal felhői is teljes napfényt kaptak, és fehéren
        /// világítottak a sötét felszín fölött.
        ///
        /// A SÁV SZÉLESSÉGE SZÁRMAZTATOTT: egy h magasságban lévő felhőt a Nap
        /// még akkor is megvilágít, amikor az a helyi horizont alatt van
        /// √(2h/R) szöggel. A dekk tetejére (h ≈ 2 km, R = 7420 km) ez
        /// 0,023 rad; a ±0,05-ös koszinusz-sáv ezt lefedi, és egyben lágy
        /// átmenetet ad — ezért látszik a felhő-terminátor a felszínihez
        /// képest kissé KÉSŐBB, ami fizikailag helyes (ez a szürkület).
        /// </summary>
        public const double TwilightBandCos = 0.05;

        /// <summary>
        /// Nappali tényező a felhő-megvilágításhoz: 0 az éjszakai oldalon,
        /// 1 a nappalin, a <see cref="TwilightBandCos"/> sávban simán átmenve.
        /// <paramref name="sunCosZenith"/> a helyi függőleges és a Nap-irány
        /// koszinusza.
        /// </summary>
        public static double DaylightFactor(double sunCosZenith)
        {
            if (double.IsNaN(sunCosZenith)) return 0.0;
            return SurfaceMicroDetail.Smoothstep01(
                (sunCosZenith + TwilightBandCos) / (2.0 * TwilightBandCos));
        }

        /// <summary>
        /// A felhőalapnál a felfelé futó fade sávja a teljes vastagság
        /// arányában (dimenziótlan). Az alj élesebb, mint a tető — a
        /// kondenzációs szint fizikailag éles határ.
        /// </summary>
        public const double ProfileBaseFadeFraction = 0.18;

        /// <summary>A felhőtető fade-sávja — szélesebb, mert a tető beszáradó, foszladozó.</summary>
        public const double ProfileTopFadeFraction = 0.42;

        /// <summary>
        /// Függőleges sűrűség-profil a héjon belül: <paramref name="t"/> = 0
        /// az alj, 1 a tető. Két kvintikus fade szorzata — tisztán polinom,
        /// nincs benne transzcendens.
        /// </summary>
        public static double VerticalProfile(double t)
        {
            if (t <= 0.0 || t >= 1.0) return 0.0;
            double rise = SurfaceMicroDetail.Smoothstep01(t / ProfileBaseFadeFraction);
            double fall = SurfaceMicroDetail.Smoothstep01((1.0 - t) / ProfileTopFadeFraction);
            return rise * fall;
        }

        /// <summary>Beer–Lambert-áteresztés egy adott optikai mélységre.</summary>
        public static double Transmittance(double opticalDepth)
        {
            if (opticalDepth <= 0.0) return 1.0;
            return DeterministicMath.Exp(-opticalDepth);
        }

        /// <summary>
        /// Henyey–Greenstein fázisfüggvény. <paramref name="cosTheta"/> = 1 a
        /// TISZTA előreszórás (a sugár a Nap felé tart), −1 a visszaszórás.
        /// Integrálja a teljes térszögre 1.
        /// </summary>
        public static double PhaseFunction(double cosTheta)
        {
            double g = ForwardScatterG;
            double g2 = g * g;
            double denom = 1.0 + g2 - 2.0 * g * cosTheta;
            if (denom < 1e-9) denom = 1e-9;
            return (1.0 - g2) / (4.0 * Math.PI * denom * Math.Sqrt(denom));
        }

        /// <summary>
        /// A fázisfüggvény IZOTRÓP = 1-re normálva (<c>4π·HG</c>), a csúcs
        /// <see cref="PhasePeakCap"/>-re vágva. A shader ezt az alakot
        /// használja: render-oldali ERŐSÍTÉS, ahol az izotróp szórás az
        /// egységérték, tehát az ambiens taggal összemérhető.
        /// </summary>
        public static double PhaseFunctionNormalized(double cosTheta)
        {
            double p = 4.0 * Math.PI * PhaseFunction(cosTheta);
            return p > PhasePeakCap ? PhasePeakCap : p;
        }

        /// <summary>
        /// A TÖBBSZÖRÖS SZÓRÁS oktávjainak száma.
        ///
        /// MIÉRT KELL EGYÁLTALÁN (MÉRT HIBA JAVÍTÁSA). Egy egyszeres-szórású
        /// raymarch az optikailag vastag felhőt definíció szerint SÖTÉTNEK
        /// mutatja: a Nap felé mért optikai mélység a dekk belsejében 5–20,
        /// tehát <c>exp(−τ)</c> gyakorlatilag nulla. MÉRVE pontosan ez történt:
        /// a felhőfoltok szürke, a terepnél is sötétebb foltokként jelentek meg.
        /// A valódi felhő viszont FEHÉR, mert a vízcsepp egyszeres-szórási
        /// albedója ~1: a fotonok nem elnyelődnek, hanem sokszor szóródnak és
        /// kijutnak. Ezt a hányadot egy egyszeres-szórású modell SOHA nem
        /// tartalmazza, ezért külön tagként kell hozzáadni.
        ///
        /// A KÖZELÍTÉS a szokásos oktávos alak: a k. oktáv kevesebb energiát
        /// hordoz (<see cref="OctaveEnergy"/>^k), CSÖKKENTETT kioltást lát
        /// (<see cref="OctaveExtinction"/>^k — a sokszorosan szórt fény
        /// „átlátja” a felhőt), és LAPOSABB fázisfüggvénnyel szóródik
        /// (<see cref="OctaveEccentricity"/>^k — a szórások elmossák az
        /// irányfüggést). A nulladik oktáv EGZAKTUL az egyszeres szórás.
        /// </summary>
        public const int MultiScatterOctaves = 4;

        /// <summary>Energia-arány oktávonként.</summary>
        public const double OctaveEnergy = 0.5;

        /// <summary>Kioltás-arány oktávonként.</summary>
        public const double OctaveExtinction = 0.25;

        /// <summary>Fázis-aszimmetria aránya oktávonként.</summary>
        public const double OctaveEccentricity = 0.5;

        /// <summary>
        /// Henyey–Greenstein tetszőleges aszimmetriával, IZOTRÓP = 1-re
        /// normálva (<c>4π·HG</c>), a csúcs <see cref="PhasePeakCap"/>-re vágva.
        /// </summary>
        public static double PhaseNormalized(double cosTheta, double g)
        {
            double g2 = g * g;
            double denom = 1.0 + g2 - 2.0 * g * cosTheta;
            if (denom < 1e-9) denom = 1e-9;
            double p = (1.0 - g2) / (denom * Math.Sqrt(denom));
            return p > PhasePeakCap ? PhasePeakCap : p;
        }

        /// <summary>
        /// A Nap felől érkező szórt fény erősítése egy felhő-mintában: az
        /// oktávok összege. <paramref name="sunOpticalDepth"/> a mintától a
        /// felhőtetőig mért optikai mélység a Nap irányában.
        /// Nulla optikai mélységnél az összeg a fázisfüggvény oktávos
        /// átlaga (a felhő teteje), nagy mélységnél a magasabb oktávok
        /// tartják fenn a fényességet — ez adja a fehér tetőt és a sötétebb
        /// aljat.
        /// </summary>
        public static double SunScatterGain(double sunOpticalDepth, double cosTheta)
        {
            double tau = sunOpticalDepth > 0.0 ? sunOpticalDepth : 0.0;
            double energy = 1.0;
            double extinction = 1.0;
            double eccentricity = 1.0;
            double total = 0.0;
            for (int k = 0; k < MultiScatterOctaves; k++)
            {
                total += energy * PhaseNormalized(cosTheta, ForwardScatterG * eccentricity)
                    * Transmittance(tau * extinction);
                energy *= OctaveEnergy;
                extinction *= OctaveExtinction;
                eccentricity *= OctaveEccentricity;
            }
            return total;
        }

        // -------------------------------------------------------------------
        // 4. RÉSZLET-LÉTRA — a lefedettség-mező felbontása ALATT
        // -------------------------------------------------------------------

        /// <summary>
        /// A részlet-fBm alap-frekvenciája (ciklus/radián) egy adott
        /// atlasz-szinthez. SZÁRMAZTATOTT, nem választott: a lefedettség-mező
        /// egy kockalap-élen <c>2^level</c> cellát hordoz, a lap éle π/2
        /// radián, tehát <c>2^level·2/π</c> cella esik egy radiánra — ez
        /// egyben az „egy ciklus egy atlasz-cellán” frekvencia, azaz PONTOSAN
        /// ott van, ahol a lefedettség-mező információja véget ér (a cellánkénti
        /// Nyquist-határ fölötti első oktáv). A részlet ott folytatja, ahol a
        /// modell abbahagyta — ugyanaz az elv, mint az ND-151 mikro-részleténél.
        /// </summary>
        public static double DetailBaseFrequency(int atlasLevel)
        {
            if (atlasLevel < 0) atlasLevel = 0;
            return (1 << atlasLevel) * 2.0 / Math.PI;
        }

        /// <summary>A részlet-létra oktávszáma.</summary>
        public const int DetailOctaves = 4;

        /// <summary>fBm-persistence: a modell saját zajával azonos.</summary>
        public const double DetailPersistence = FractalNoise.DefaultPersistence;

        /// <summary>fBm-lacunarity: a modell saját zajával azonos.</summary>
        public const double DetailLacunarity = FractalNoise.DefaultLacunarity;

        /// <summary>
        /// A részlet-zaj FÜGGŐLEGES megnyújtása: a felhők vízszintesen sokkal
        /// nagyobb léptékben szerveződnek, mint függőlegesen — a héj 24 km
        /// vastag, a felhőcella 50–180 km széles. A radiális irányban ennyivel
        /// SŰRŰBB a zaj, mint a tangenciálisban.
        /// </summary>
        public const double DetailVerticalStretch = 3.0;

        /// <summary>
        /// A CELLÁN BELÜLI (sub-grid) felhő-eloszlás átmeneti sávjának
        /// szélessége a lefedettség skáláján. A felhőfolt SZÉLE nem éles: ez
        /// a sáv adja a foszladozó peremet.
        /// </summary>
        public const double SubGridEdgeWidth = 0.25;

        /// <summary>
        /// A render-oldali részlet-fBm SZÓRÁSA (4 oktáv érték-zaj,
        /// persistence 0,5, a súlyösszeggel normálva). **MÉRT érték**: a
        /// shader saját zajának 30 000 mintáján 0,2537 (átlag 0,0009,
        /// tartomány [−0,89; 0,86]). A
        /// <c>DetailNoiseIsApproximatelyNormalWithTheDocumentedSigma</c> teszt
        /// a shader zajának tükrével újraméri — ha a shader zaja változik, a
        /// teszt elbukik és pontosan erre mutat rá.
        /// </summary>
        public const double DetailNoiseStdDev = 0.2537;

        /// <summary>
        /// A logisztikus-normális közelítés meredeksége: <c>Φ(x) ≈ σ(1,702·x)</c>
        /// a normális eloszlásfüggvény klasszikus logisztikus közelítése.
        /// </summary>
        public const double LogisticNormalSlope = 1.702;

        /// <summary>
        /// A nulla átlagú részlet-fBm leképezése ~EGYENLETES [0,1] változóvá.
        ///
        /// MIÉRT KELL (a mean-preservation ezen áll vagy bukik). A
        /// <see cref="SubGridThreshold"/> zárt alakja EGYENLETES zajra van
        /// levezetve. A shader tényleges zaja viszont négy oktáv összege,
        /// tehát közel NORMÁLIS és 0,5 köré tömörül: a naiv
        /// <c>0,5 + 0,5·fbm</c> leképezéssel a minták gyakorlatilag a
        /// [0,37; 0,63] sávba esnek, és a küszöbözés SZISZTEMATIKUS
        /// kontraszt-nyújtást okoz — derült ott, ahol a modell szerint
        /// gyengén felhős, és borult ott, ahol szerint többnyire felhős.
        /// A modell mennyisége tehát NEM maradna meg, pedig az I3-érvelés
        /// éppen erre épül.
        ///
        /// A leképezés a normális eloszlásfüggvény logisztikus közelítése.
        /// MÉRVE: az így kapott változó eloszlásfüggvénye a tényleges
        /// shader-zajon legfeljebb **0,018**-dal tér el az egyenletestől.
        /// </summary>
        public static double DetailNoiseToUniform(double noise)
        {
            if (double.IsNaN(noise)) return 0.5;
            double x = LogisticNormalSlope * noise / DetailNoiseStdDev;
            if (x <= -60.0) return 0.0;
            if (x >= 60.0) return 1.0;
            return 1.0 / (1.0 + DeterministicMath.Exp(-x));
        }

        /// <summary>
        /// A modellezett cella-ÁTLAGOS lefedettség szétbontása a cellán
        /// belüli felhő/derült mintázatra — a <see cref="SubGridCoverage"/>
        /// KÜSZÖBE.
        ///
        /// MIÉRT NEM SZORZÓ (MÉRT HIBA JAVÍTÁSA). Az első változat a
        /// lefedettséget MULTIPLIKATÍVAN modulálta a részlet-zajjal. Ez
        /// fizikailag rossz: egy 364 km-es cella 0,1-es lefedettsége azt
        /// jelenti, hogy a terület 10%-án VAN felhő és 90%-án NINCS — nem
        /// azt, hogy az egészet egy tizednyi sűrűségű fátyol fedi. A mért
        /// következmény pontosan ez lett: a bolygót EGYENLETES SZÜRKE FÁTYOL
        /// borította, derült területek nélkül.
        ///
        /// A KÜSZÖB ZÁRT ALAKBAN LEVEZETHETŐ, nem hangolt. Legyen a helyi
        /// sűrűség <c>f(n) = clamp((T − n)/w, 0, 1)</c> a [0,1]-en egyenletes
        /// <c>n</c> zajra. A várható érték:
        /// <list type="bullet">
        /// <item><c>T &lt; w</c>: <c>E = T²/(2w)</c> → <c>T = √(2wc)</c>;</item>
        /// <item><c>w ≤ T ≤ 1</c>: <c>E = T − w/2</c> → <c>T = c + w/2</c>;</item>
        /// <item><c>T &gt; 1</c>: <c>E = (T−w) + (w² − (T−1)²)/(2w)</c> →
        /// <c>T = 1 + w − √(2w(1−c))</c>.</item>
        /// </list>
        /// A három ág a <c>c = w/2</c> és <c>c = 1 − w/2</c> pontokban
        /// folytonosan illeszkedik, és a két végpont EGZAKT:
        /// <c>T(0) = 0</c> (nulla lefedettség → egzaktul üres, I3-garancia) és
        /// <c>T(1) = 1 + w</c> (teljes lefedettség → mindenütt felhő). Csak
        /// <c>√</c> kell hozzá, ami IEEE-754 szerint korrekt kerekítésű.
        /// </summary>
        public static double SubGridThreshold(double coverage)
        {
            double w = SubGridEdgeWidth;
            if (double.IsNaN(coverage) || coverage <= 0.0) return 0.0;
            if (coverage >= 1.0) return 1.0 + w;
            if (coverage < 0.5 * w) return Math.Sqrt(2.0 * w * coverage);
            if (coverage > 1.0 - 0.5 * w) return 1.0 + w - Math.Sqrt(2.0 * w * (1.0 - coverage));
            return coverage + 0.5 * w;
        }

        /// <summary>
        /// A cellán belüli felhő/derült szétbontás: ott van felhő, ahol a
        /// [0,1]-re normált részlet-zaj a <see cref="SubGridThreshold"/> alatt
        /// van, <see cref="SubGridEdgeWidth"/> széles lágy peremmel.
        ///
        /// A zajra vett VÁRHATÓ ÉRTÉKE a modellezett cella-átlag (ld. a
        /// küszöb levezetését), tehát a modell mennyisége megmarad; a
        /// szétbontás csak ott ad információt, ahol a modellnek nincs — a
        /// cella BELSEJÉBEN.
        /// </summary>
        public static double SubGridCoverage(double coverage, double noise01)
        {
            if (double.IsNaN(coverage) || double.IsNaN(noise01) || coverage <= 0.0) return 0.0;
            double n = noise01 <= 0.0 ? 0.0 : (noise01 >= 1.0 ? 1.0 : noise01);
            double local = (SubGridThreshold(coverage) - n) / SubGridEdgeWidth;
            return local <= 0.0 ? 0.0 : (local >= 1.0 ? 1.0 : local);
        }

        /// <summary>
        /// A részlet-zaj FÁZISA a világ seedjéből. A
        /// <see cref="RandomDomain.Decorative"/> domainben van — ugyanaz a
        /// besorolás, mint a csillagmezőé és a felszíni mikro-részletéé
        /// (<see cref="RandomProperty.MicroDetailPhase"/>): NEM a világmodell
        /// része, az I1 garanciái nem terjednek ki rá, de „ugyanaz a seed →
        /// ugyanaz a látvány” teljesül.
        /// </summary>
        public static void DetailPhase(ulong worldSeed, out double x, out double y, out double z)
        {
            DeterministicRandom.Sample4(
                worldSeed, RandomDomain.Decorative, 0, 0,
                out double a, out double b, out double c, out double _,
                RandomProperty.CloudDetailPhase);
            x = a * 1024.0;
            y = b * 1024.0;
            z = c * 1024.0;
        }
    }
}
