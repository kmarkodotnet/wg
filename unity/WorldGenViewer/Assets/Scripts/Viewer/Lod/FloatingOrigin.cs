using System;
using System.Globalization;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// ND-19 (A12) — a floating origin MATEMATIKAI magja: melyik pont legyen
    /// a render-origó, mikor kell újra-alapozni (rebase), és mennyi
    /// pozíció-pontosságot ad ez `float32`-ben.
    ///
    /// MOTORFÜGGETLEN (nulla UnityEngine-referencia, ld. az asmdef
    /// `noEngineReferences` beállítását), ezért Unity Editor nélkül,
    /// közvetlenül tesztelhető — ugyanaz a minta, mint a többi Lod-modulnál.
    /// Tisztán vizuális/precíziós segédszámítás, NEM szimulációs logika: az
    /// I1 determinizmus-invariáns nem érintett (a világmodell egyetlen bitje
    /// sem függ tőle). A számítás ennek ellenére szándékosan transzcendens-
    /// mentes (csak `+ - * /` és 2-hatvány skálázás), hogy platformok között
    /// is ugyanazt az origót válassza — így a diagnosztikai naplók
    /// összevethetők maradnak.
    ///
    /// A MÉRT FELISMERÉS, amiért ez egyáltalán kell (ld. `FloatingOriginTests`):
    /// a `float32` felszíni pozíció-hibája LÉPTÉK-INVARIÁNS. Akár
    /// `radius = 100` Unity-egység a bolygó, akár a valós 7 420 000, az
    /// ULP/sugár arány ugyanaz (2^-23 … 2^-22), tehát a felszínen ugyanaz a
    /// néhány deciméteres kvantálás jön ki MINDKÉT léptéknél. Ebből
    /// következik, hogy a "kis Unity-lépték" NEM védelem a precíziós
    /// probléma ellen, és hogy a méterszintű közeli zoom (A11/M9) CSAK
    /// origó-eltolással érhető el — nem másik radius-értékkel.
    ///
    /// Térkonvenció: "modell-tér" = a viewer Unity-egységű TEST-KERETE
    /// (body frame), a bolygó középpontja a 0 pontban, a felszín sugara
    /// `surfaceRadius` egység. Minden itteni bemenet/kimenet ebben a térben
    /// értendő; a Unity-tengelycserét (ld. `BodyFrameConversion`) a hívó
    /// végzi, mert az irreleváns a precízió szempontjából.
    /// </summary>
    public static class FloatingOrigin
    {
        /// <summary>
        /// A `float32` szignifikancia-lépése az [1,2) bináris oktávban:
        /// 2^-23 = 1/8388608. A 24 bites szignifikandusból (23 tárolt bit +
        /// rejtett vezető 1) következik; pontosan ábrázolható double-ként.
        /// </summary>
        public const double Float32MantissaStep = 1.0 / 8388608.0;

        /// <summary>A legkisebb pozitív (denormál) float32 lépés, 2^-149 — a 0 körüli ULP.</summary>
        public const double Float32DenormalStep = 1.401298464324817E-45;

        /// <summary>
        /// A legkisebb origó-rács-lépés, amit még megengedünk (Unity-egység).
        /// Ennél finomabb rács alatt a rebase-ek sűrűbbek lennének, mint
        /// amennyi pontosságot még nyerni lehet: 2^-30 egység egy 100-as
        /// sugarú bolygónál ~0,07 mm fizikai lépés — jóval a látható alatt.
        /// </summary>
        public const double MinimumCellUnits = 1.0 / 1073741824.0; // 2^-30

        /// <summary>
        /// A rebase-küszöb a rács-lépés ennyiszerese. Egy frissen illesztett
        /// origó tengelyenként legfeljebb 0,5 cellányira van a kamerától,
        /// tehát az 1,5-es faktor TISZTA HISZTERÉZIST ad: a cellahatáron ülő
        /// kamera nem tud oda-vissza billegni két origó között (ez lenne a
        /// klasszikus hiba: rebase-enként teljes geometria-újraépítés).
        /// </summary>
        public const double RebaseFactor = 1.5;

        /// <summary>
        /// A `float32` ULP (két szomszédos ábrázolható érték távolsága) a
        /// megadott nagyságrendnél, double-ban számolva. Csak 2-hatvány
        /// skálázás, tehát BITPONTOS és könyvtárfüggetlen (nincs `ILogB` /
        /// `ScaleB`, amik netstandard2.1 alatt nem érhetők el mindenhol).
        /// </summary>
        public static double Float32Ulp(double magnitude)
        {
            double m = Math.Abs(magnitude);
            if (double.IsNaN(m) || double.IsInfinity(m))
                throw new ArgumentOutOfRangeException(nameof(magnitude));
            if (m <= 0.0) return Float32DenormalStep;

            double ulp = 1.0;
            // A ciklusok legfeljebb ~1100 lépést tesznek (a double kitevő-
            // tartománya), tehát végesek. A `m` csak 2-hatvánnyal skálázódik,
            // ezért maga a skálázás nem kerekít.
            while (m >= 2.0) { m *= 0.5; ulp *= 2.0; }
            while (m < 1.0) { m *= 2.0; ulp *= 0.5; }
            return ulp * Float32MantissaStep;
        }

        /// <summary>
        /// Az adott lokális (origótól mért) sugárban elérhető pozíció-
        /// felbontás MÉTERBEN. `metersPerUnit` = fizikai bolygósugár /
        /// modell-sugár (pl. 7 420 000 / 100 = 74 200 m/egység).
        /// </summary>
        public static double ResolutionMeters(double localRadiusUnits, double metersPerUnit)
        {
            if (!(metersPerUnit > 0.0) || double.IsInfinity(metersPerUnit))
                throw new ArgumentOutOfRangeException(nameof(metersPerUnit));
            return Float32Ulp(localRadiusUnits) * metersPerUnit;
        }

        /// <summary>
        /// A javasolt origó-rács-lépés a kamera FELSZÍN FELETTI magasságából:
        /// a magasságnál nem nagyobb legnagyobb 2-hatvány, `MinimumCellUnits`
        /// padlóval. Két oka van, hogy a magasság a vezérlő paraméter:
        ///
        /// 1. Az origó így legfeljebb ~magasságnyira van a kamerától, tehát a
        ///    képernyőn releváns geometria lokális koordinátái a magasság
        ///    nagyságrendjébe esnek — pont ott lesz finom az ULP, ahol a
        ///    felbontás számít.
        /// 2. Rebase csak akkor kell, ha a kamera a saját magasságával
        ///    összemérhető utat tett meg — ekkorra a látvány maga is érdemben
        ///    átfordult, tehát az újraépítés nem "extra" költség.
        ///
        /// 2-hatvány rácsot használunk, mert így az origó minden tagja
        /// pontosan ábrázolható, és a `p - origó` kivonás nem visz be
        /// kerekítési hibát a double-tartományon belül.
        /// </summary>
        public static double RecommendedCellUnits(double altitudeUnits)
        {
            double a = Math.Abs(altitudeUnits);
            if (double.IsNaN(a) || double.IsInfinity(a))
                throw new ArgumentOutOfRangeException(nameof(altitudeUnits));
            if (a <= MinimumCellUnits) return MinimumCellUnits;

            double cell = 1.0;
            while (cell * 2.0 <= a) cell *= 2.0;
            while (cell > a) cell *= 0.5;
            return Math.Max(MinimumCellUnits, cell);
        }

        /// <summary>
        /// A rács-lépés FELSŐ korlátja: a legnagyobb 2-hatvány, amire még
        /// `RebaseFactor * cell` legfeljebb `surfaceRadius`.
        ///
        /// MIÉRT KELL (élő Play-mérésből, 2026-09-27). A korlát nélkül a
        /// bolygó-nézeti magasságokon (a mai jelenetben 200-364 egység egy
        /// 100-as sugarú bolygó fölött) a javasolt rács-lépés 128-256 lett,
        /// azaz az origó MESSZEBB került a kamerától, mint maga a bolygó
        /// középpontja — a naplózott `gainFactor` 0,5 és 0,3 volt, tehát az
        /// "origó-relatív" út ROSSZABB volt az abszolútnál. A korláttal a
        /// nyereség-faktor soha nem eshet 1 alá: a legrosszabb eset az, hogy
        /// az origó-relatív felbontás egyenlő az abszolúttal.
        /// </summary>
        public static double MaximumUsefulCellUnits(double surfaceRadiusUnits)
        {
            double r = Math.Abs(surfaceRadiusUnits);
            if (double.IsNaN(r) || double.IsInfinity(r))
                throw new ArgumentOutOfRangeException(nameof(surfaceRadiusUnits));
            return RecommendedCellUnits(r / RebaseFactor);
        }

        /// <summary>
        /// A kamerapozíció illesztése a `cellUnits` lépésű rácsra (tengelyenként
        /// a legközelebbi rácspontra). A `Math.Floor(x/c + 0.5)` alak
        /// SZÁNDÉKOS: a `Math.Round` bankári kerekítése a pontos feleknél
        /// kétféle választ adna ugyanarra a helyre attól függően, melyik
        /// oldalról érkezik a kamera — a floor-alak monoton, tehát az origó a
        /// kamera folytonos mozgása mentén sem tud visszaugrani.
        /// </summary>
        public static RenderOrigin Snap(double camX, double camY, double camZ, double cellUnits)
        {
            if (!(cellUnits > 0.0) || double.IsInfinity(cellUnits))
                throw new ArgumentOutOfRangeException(nameof(cellUnits));
            return new RenderOrigin(
                Math.Floor(camX / cellUnits + 0.5) * cellUnits,
                Math.Floor(camY / cellUnits + 0.5) * cellUnits,
                Math.Floor(camZ / cellUnits + 0.5) * cellUnits,
                cellUnits);
        }

        /// <summary>
        /// A teljes origó-vezérlés egy lépése. `true`, ha ÚJ origóra kell
        /// váltani (a hívónak ilyenkor újra kell alapoznia a geometriát);
        /// `false` esetén `next` az érvényben lévő origó, változatlanul.
        ///
        /// Rebase akkor kell, ha (a) még nincs érvényes origó, (b) a rács-lépés
        /// megváltozott (a zoom másik bináris oktávba lépett), vagy (c) a
        /// kamera tengelyenként több mint `RebaseFactor * cell`-re került.
        ///
        /// A rács-lépés `MaximumUsefulCellUnits`-szal felülről korlátozott, és
        /// bolygó-nézeti magasságon (a teljes gömb látszik) az origó a
        /// BOLYGÓKÖZÉP: ott az eltolás definíció szerint nem nyer semmit,
        /// viszont fölösleges rebase-eket indítana. A két üzemmód között
        /// FAKTOR-2 HISZTERÉZIS-SÁV van (belépés a teljes sugárnál, kilépés
        /// csak a fél sugár alatt), hogy a küszöb körül
        /// lebegő kamera ne billegjen képkockánként.
        /// </summary>
        public static bool TryAdvance(
            in RenderOrigin current, double camX, double camY, double camZ,
            double altitudeUnits, double surfaceRadiusUnits, out RenderOrigin next)
        {
            double radius = Math.Abs(surfaceRadiusUnits);
            double altitude = Math.Abs(altitudeUnits);
            double cellCap = MaximumUsefulCellUnits(radius);

            // Az `IsPlanetCenter` itt ÜZEMMÓD-jelző, és ez megbízható: mivel
            // cell <= radius/1.5, egy kamera-illesztett origó tengelyenként
            // legfeljebb 0,5 cellát tér el a kamerától, tehát a hossza
            // legalább radius - 0,87*cell > 0 - azaz SOSEM eshet pontosan a
            // bolygóközépre, amíg a kamera a gömbön kívül van.
            bool planetCenter = current.IsValid && current.IsPlanetCenter
                ? altitude >= radius * 0.5   // benne vagyunk: csak a fél sugár alatt lépünk ki
                : altitude >= radius;        // kívül vagyunk: a teljes sugárnál lépünk be

            // Bolygóközepű módban a rács-lépésnek nincs jelentése (az origó
            // fix), ezért a felső korláton PINNELJÜK - különben a magasság
            // változása oktávonként "rebase-t" jelentene, miközben az origó
            // számértéke egyáltalán nem változik.
            double cell = planetCenter ? cellCap : Math.Min(RecommendedCellUnits(altitudeUnits), cellCap);

            bool stale = !current.IsValid
                || current.CellUnits != cell
                || (planetCenter
                    ? !current.IsPlanetCenter
                    : current.ChebyshevDistanceTo(camX, camY, camZ) > RebaseFactor * cell);
            if (!stale)
            {
                next = current;
                return false;
            }
            next = planetCenter ? RenderOrigin.PlanetCenter(cell) : Snap(camX, camY, camZ, cell);
            return true;
        }

        /// <summary>
        /// Az a lokális sugár, ami az elérhető felbontást MEGHATÁROZZA: a
        /// kamera közelében levő geometria legnagyobb origó-relatív távolsága.
        /// Bolygóközepű origónál ez a bolygó sugara (a geometria |p| ≈ R-en
        /// van), kamera-illesztett origónál a rebase-küszöb.
        /// </summary>
        public static double LocalRadiusUnits(in RenderOrigin origin, double surfaceRadiusUnits)
        {
            if (!origin.IsValid) throw new ArgumentOutOfRangeException(nameof(origin));
            return origin.IsPlanetCenter
                ? Math.Abs(surfaceRadiusUnits)
                : RebaseFactor * origin.CellUnits;
        }
    }

    /// <summary>
    /// Egy érvényben lévő render-origó: a modell-tér (test-keret, Unity-egység)
    /// azon pontja, amihez képest a geometria `float32`-be íródik.
    /// `default(RenderOrigin)` = ÉRVÉNYTELEN (nem "a bolygó középpontja"),
    /// ld. <see cref="IsValid"/> — ez szándékos, ugyanaz a hibaosztály-védelem,
    /// mint a `DeepTimeContext` NaN-os tengerszintjénél (A22): egy
    /// alapértelmezett struct ne tűnhessen legális, bolygóközepű origónak,
    /// mert az CSENDBEN visszaállítaná a régi, precíziót vesztő viselkedést.
    /// A bolygóközepű (azaz gyakorlatilag kikapcsolt) origót a
    /// <see cref="PlanetCenter"/> adja, explicit rács-léptékkel.
    /// </summary>
    public readonly struct RenderOrigin
    {
        public readonly double X, Y, Z;

        /// <summary>A rács-lépés, amivel ez az origó illesztve lett (Unity-egység). 0 = érvénytelen.</summary>
        public readonly double CellUnits;

        public RenderOrigin(double x, double y, double z, double cellUnits)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z))
                throw new ArgumentOutOfRangeException(nameof(x));
            if (!(cellUnits > 0.0) || double.IsInfinity(cellUnits))
                throw new ArgumentOutOfRangeException(nameof(cellUnits));
            X = x; Y = y; Z = z; CellUnits = cellUnits;
        }

        public bool IsValid => CellUnits > 0.0;

        /// <summary>A bolygó középpontjára illesztett origó — a floating origin ELŐTTI, abszolút viselkedés.</summary>
        public static RenderOrigin PlanetCenter(double cellUnits)
        {
            return new RenderOrigin(0.0, 0.0, 0.0, cellUnits);
        }

        public bool IsPlanetCenter => X == 0.0 && Y == 0.0 && Z == 0.0;

        /// <summary>Tengelyenkénti (Chebyshev) távolság — a rács-illesztéshez tartozó természetes metrika.</summary>
        public double ChebyshevDistanceTo(double x, double y, double z)
        {
            return Math.Max(Math.Abs(x - X), Math.Max(Math.Abs(y - Y), Math.Abs(z - Z)));
        }

        /// <summary>
        /// Modell-téri (abszolút) pont → origó-relatív `float32` hármas. A
        /// kivonás DOUBLE-ban történik, és csak UTÁNA jön a `float` cast — ez
        /// a floating origin lényege; a fordított sorrend (cast, majd kivonás)
        /// semmit nem nyerne, mert a pontosság már a castnál elveszett.
        /// </summary>
        public void ToLocal(double x, double y, double z, out float lx, out float ly, out float lz)
        {
            lx = (float)(x - X);
            ly = (float)(y - Y);
            lz = (float)(z - Z);
        }

        /// <summary>
        /// A <see cref="ToLocal(double,double,double,out float,out float,out float)"/>
        /// alakja a viewer emit-láncának pozíció-típusához (A12/2). A bemenet
        /// ABSZOLÚT test-keretbeli pont, a kimenet az origó-relatív,
        /// `float32`-be MÁR átváltott hármas - pontosan az, ami a mesh
        /// vertex-bufferébe kerül.
        /// </summary>
        public void ToLocalVertex(in SurfacePoint absolute, out float lx, out float ly, out float lz)
        {
            ToLocal(absolute.X, absolute.Y, absolute.Z, out lx, out ly, out lz);
        }

        /// <summary>Origó-relatív pont DOUBLE-ban (a köztes számításokhoz, pl. radiális bias).</summary>
        public SurfacePoint ToLocalPoint(in SurfacePoint absolute)
        {
            return new SurfacePoint(absolute.X - X, absolute.Y - Y, absolute.Z - Z);
        }

        /// <summary>Az origó-relatív koordináta visszaalakítása abszolút modell-térbe (double-ban).</summary>
        public void ToAbsolute(double lx, double ly, double lz, out double x, out double y, out double z)
        {
            x = lx + X; y = ly + Y; z = lz + Z;
        }

        public override string ToString()
        {
            if (!IsValid) return "RenderOrigin(invalid)";
            return "RenderOrigin(" + R(X) + ", " + R(Y) + ", " + R(Z) + ", cell=" + R(CellUnits) + ")";
        }

        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
