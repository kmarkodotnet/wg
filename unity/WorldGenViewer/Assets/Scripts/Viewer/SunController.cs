using System;
using UnityEngine;
using WorldGen.Core.Astronomy;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M3 vizuális cél: "megvilágított gömb, terminátorral - évszakok
    /// látszanak a terminátor mozgásán". A WorldGen.Core.Astronomy.
    /// OrbitalMechanics kimenetét (nap-irány a bolygó TEST-KERETÉBEN)
    /// fordítja a Directional Light forgatására.
    ///
    /// FONTOS KONVENCIÓ (a `Free`/alapértelmezett kameramódban): a Planet
    /// GameObject transform.rotation-ját IDENTITÁSON kell tartani (a
    /// PlanetGridMesh nem forgatja a mesh-et) - a nap-irány már a test-
    /// keretben van kiszámolva, tehát a mesh-nek NEM kell forognia a
    /// nap/éj ciklushoz, elég a fényt forgatni.
    ///
    /// KIVÉTEL (2026-09-10, felhasználói kérés): `AxialRotation`
    /// kameramódban EZ a komponens SZÁNDÉKOSAN elforgatja a Planet
    /// mesh-et (`PlanetGridMesh.CurrentCameraViewMode`-ból olvasva), hogy
    /// a bolygó saját tengely körüli forgása vizuálisan láthatóvá váljon
    /// (a Nap/csillagok ilyenkor a világtérben FIXEK maradnak - ld.
    /// ApplySunDirection). Ez az EGYETLEN hely, ahol a Planet-rotáció
    /// szándékosan eltér az identitástól.
    ///
    /// UGYANEZ A KIVÉTEL (ND-153, A11): `OrbitalFollow` (pálya menti,
    /// éves) módban is a pálya-keretben rendereljük a világot - a
    /// KÜLÖNBSÉG mindössze annyi, hogy a spin-szög BEFAGY (a módba lépés
    /// pillanatának szögén), az idő pedig éves ütemben fut
    /// (`orbitalFollowSecondsPerYear`). Így az éves jel (a terminátor
    /// észak-déli vándorlása, a sarki nappal/éjszaka) látható, a napi
    /// forgás pedig nem alias-ol. A modellidő TOVÁBBFUT: csak a kép
    /// spin-fázisa konstans, a panel/hőmodell a valódi órát olvassa.
    ///
    /// Ezt a komponenst a Directional Light GameObjectre kell tenni - a
    /// saját transform.rotation-ját írja át minden képkockán.
    /// </summary>
    [ExecuteAlways]
    public class SunController : MonoBehaviour
    {
        [Header("Pálya és forgás (M3 hatókör: kör pálya, ld. ND-26)")]
        [SerializeField] private double orbitalPeriodDays = 365.25;
        [SerializeField] private double rotationPeriodDays = 1.0;
        // NINCS [Range] itt szandekosan: a Unity RangeAttribute csak
        // (float,float) vagy (int,int) konstruktort fogad, a double->float
        // pedig NEM implicit konverzio C#-ban - [Range(0.0, 90.0)] egy
        // double mezon fordítási hibat adna.
        [SerializeField] private double axialTiltDegrees = 23.44;
        [SerializeField] private double orbitalPhase0 = 0.0;
        [SerializeField] private double rotationPhase0 = 0.0;

        [Header("Idő")]
        [Tooltip("Kézzel is állítható (Edit módban is azonnal alkalmazódik) - " +
                 "próbáld ki pl. 0 / 91.31 / 182.62 / 273.94 értékekkel a " +
                 "napéjegyenlőség/napforduló bejárásához.")]
        [SerializeField] private double currentTimeDays = 0.0;

        // FELHASZNALOI VISSZAJELZES (2026-09-06): "a bolygó tengely körüli
        // forgása nem látható... a nap mindig a bolygó egy adott pontja
        // irányában van" - GYOKEROK: ez a mezo alapbol `false` volt (a
        // korabbi, LATHATATLAN fenyforgatashoz keszult, ahol a manualis
        // csuszka-allitas volt a fo hasznalati mod), tehat `currentTimeDays`
        // SOHA nem haladt Play kozben, a Nap/csillagok fixen alltak. Most,
        // hogy van LATHATO Nap+csillag-rendszer, az alapertek `true`-ra
        // valtott - FONTOS: ez a MAR LETEZO SunController peldanyokra NEM
        // hat vissza (a scene-ben mar `false`-ra van mentve), ott kezzel
        // kell bepipalni.
        [SerializeField] private bool autoAdvance = true;
        [Tooltip("5.0 tul gyors volt (napi forgas < 1 mp alatt lezajlott) - " +
                 "0.05-tel egy teljes nap kb. 20 masodperc, jol kovetheto.")]
        [SerializeField] private double daysPerSecond = 0.05;

        [Header("Pálya menti (éves) mód - ND-153")]
        [SerializeField]
        [Tooltip("Ennyi valós másodperc alatt telik le EGY teljes pálya-kör, ha a " +
                 "kameramód 'Pálya mentén'. A napi mód daysPerSecond értékét NEM írja " +
                 "át - módváltáskor a napi ciklus ott folytatódik, ahol tartott.")]
        private double orbitalFollowSecondsPerYear = OrbitalFollowMath.DefaultSecondsPerYear;

        [SerializeField]
        [Tooltip("ND-153 alapdöntés: éves ütemben a napi forgás ALIAS-OL (60 s/év mellett " +
                 "~36 fok/képkocka), ezért a mesh a módba lépés spin-szögén megáll - a " +
                 "dőlés viszont él, a terminátor vándorol. Kikapcsolva maga az " +
                 "alias-jelenség is megvizsgálható (nem alapérték).")]
        private bool orbitalFollowFreezeSpin = true;

        [Header("Látható Nap + csillagos háttér (felhasználói kérés, 2026-09-06)")]
        [SerializeField]
        [Tooltip("A bolygó, aminek a pozíciója körül a látható Nap-korong elhelyezkedik " +
                 "(üresen hagyva a világ origója körül).")]
        private Transform planetTransform;

        [SerializeField]
        [Tooltip("Egy kis, önvilágító kvad/gömb (ld. StarUnlit anyag) - a Nap TÉNYLEGES " +
                 "csillagászati iránya szerint pozícionálva minden képkockán. Üresen " +
                 "hagyva csak a Directional Light forog, látható Nap-korong nélkül.")]
        private Transform sunVisual;

        [SerializeField] private float sunVisualDistance = 4000f;

        [SerializeField]
        [Tooltip("A csillagos háttér - a Nappal AZONOS forgási szöget kapja (ld. " +
                 "StarField.SetRotationAngleRadians doksi), hogy a bolygó tengelyforgása " +
                 "miatt a csillagok is láthatóan mozogjanak. Üresen hagyva nincs csillag-hatás.")]
        private StarField starField;

        [SerializeField]
        [Tooltip("A PlanetGridMesh.CurrentCameraViewMode-ot olvassuk innen - AxialRotation " +
                 "módban ez a komponens forgatja el TÉNYLEGESEN a Planet transform-ot " +
                 "(ld. ApplySunDirection). Üresen hagyva a mód-váltás nincs hatással " +
                 "(mindig a jelenlegi, 'Free' viselkedés fut).")]
        private PlanetGridMesh planetGridMesh;

        /// <summary>
        /// A megjelenített idő napokban. A pillanatnyi hőmodell (ND-104) ebből
        /// képzi az egész tickes Core-időt (<c>SimulationTime.FromDaysFloor</c>),
        /// így a fény és a hőtérkép ugyanazt az órát olvassa.
        /// </summary>
        public double CurrentTimeDays => currentTimeDays;
        public double OrbitalPeriodDays => orbitalPeriodDays;
        public double RotationPeriodDays => rotationPeriodDays;
        public double AxialTiltDegrees => axialTiltDegrees;
        public double OrbitalPhase0 => orbitalPhase0;
        public double RotationPhase0 => rotationPhase0;

        /// <summary>ND-153: hol tartunk a pályán, [0,1) - a panel éves kijelzéséhez.</summary>
        public double YearFraction =>
            OrbitalFollowMath.YearFraction(currentTimeDays, orbitalPeriodDays, orbitalPhase0);

        /// <summary>ND-153: az aktuális pálya-körön eltelt napok száma.</summary>
        public double DayOfYear =>
            OrbitalFollowMath.DayOfYear(currentTimeDays, orbitalPeriodDays, orbitalPhase0);

        /// <summary>
        /// ND-153: a szub-napponti pont SZÉLESSÉGE fokban. SZÁNDÉKOSAN csak a
        /// szélesség publikus: ez kizárólag a pálya-szögtől és a dőléstől függ,
        /// tehát a befagyasztott spin mellett is IGAZ (I4). A szub-napponti
        /// HOSSZÚSÁG a spin-fázisból jön, amit a pálya menti mód befagyaszt -
        /// azt ezért nem írjuk ki sehol.
        /// </summary>
        public double SubsolarLatitudeDegrees
        {
            get
            {
                if (orbitalPeriodDays <= 0.0 || rotationPeriodDays <= 0.0) return 0.0;
                OrbitalMechanics.SunDirectionBodyFrame(
                    currentTimeDays, orbitalPeriodDays, rotationPeriodDays,
                    axialTiltDegrees * Math.PI / 180.0, orbitalPhase0, rotationPhase0,
                    out double x, out double y, out double z);
                OrbitalMechanics.SubsolarPoint(x, y, z, out double latitude, out _);
                return latitude * 180.0 / Math.PI;
            }
        }

        private void OnEnable() => ApplySunDirection();
        private void OnValidate() => ApplySunDirection();

        private void Update()
        {
            if (autoAdvance && Application.isPlaying)
            {
                // ND-153: pálya menti módban NEM a napi ütem fut, hanem az
                // éves (orbitalFollowSecondsPerYear) - a napi `daysPerSecond`
                // érintetlen marad, hogy a módból visszalépve ott folytassa.
                if (CurrentMode == PlanetGridMesh.CameraViewMode.OrbitalFollow)
                {
                    currentTimeDays = OrbitalFollowMath.AdvanceDays(
                        currentTimeDays, Time.deltaTime,
                        OrbitalFollowMath.DaysPerSecond(orbitalPeriodDays, orbitalFollowSecondsPerYear),
                        orbitalPeriodDays);
                }
                else
                {
                    currentTimeDays += daysPerSecond * Time.deltaTime;
                }
            }

            ApplySunDirection();
        }

        /// <summary>
        /// Az aktív kameramód - bekötött <see cref="planetGridMesh"/> nélkül
        /// mindig `Free` (a mód-váltás ilyenkor hatástalan, ld. a mező tooltipje).
        /// </summary>
        private PlanetGridMesh.CameraViewMode CurrentMode =>
            planetGridMesh != null
                ? planetGridMesh.CurrentCameraViewMode
                : PlanetGridMesh.CameraViewMode.Free;

        // ND-153: a befagyasztott spin-szög (radián) és az a mód, amiben
        // utoljára voltunk. A befagyasztás a MÓDBA LÉPÉS pillanatának szögén
        // történik, hogy a felszín-rajzolat ne UGORJON a váltáskor.
        private double _frozenSpinAngle;
        private bool _hasFrozenSpinAngle;
        private PlanetGridMesh.CameraViewMode _lastMode = PlanetGridMesh.CameraViewMode.Free;

        private void ApplySunDirection()
        {
            if (orbitalPeriodDays <= 0.0 || rotationPeriodDays <= 0.0)
                return; // érvénytelen bemenet (pl. Inspectorban 0-ra állítva) - ne törjön el

            double axialTiltRad = axialTiltDegrees * Math.PI / 180.0;
            // Egyetlen igazságforrás a spin-szögre (ND-153): ugyanaz a képlet,
            // amit a Core SunDirectionBodyFrame és a befagyasztás is használ.
            double rotationAngle = OrbitalFollowMath.RotationAngle(
                currentTimeDays, rotationPeriodDays, rotationPhase0);

            // KAMERA-MÓD (felhasználói kérés, 2026-09-10): AxialRotation
            // módban a bolygó mesh-nek TÉNYLEGESEN kell forognia (a Nap/
            // csillagok maradjanak fixek), a Free (alapértelmezett) mód
            // VÁLTOZATLANUL a régi, "Planet mindig identitáson, csak a
            // fény forog a test-keretben" viselkedést adja. Ld.
            // docs/04-decisions.md a levezetésért (tengelycsere-konvenció,
            // a forgás-előjel a StarField MÁR élesben helyesnek bizonyult
            // ellentétes-forgatásából levezetve).
            // ND-153: a pálya menti (éves) mód UGYANEZEN az ágon fut - a
            // pálya-keretben rendereljük a világot, a Nap/csillagok fixek. A
            // KÜLÖNBSÉG kizárólag a felhasznált spin-szög: itt a módba lépés
            // pillanatában befagyasztott érték (különben éves ütemben a napi
            // forgás alias-olna, ld. a döntést).
            PlanetGridMesh.CameraViewMode mode = CurrentMode;
            bool orbitalFollowMode = mode == PlanetGridMesh.CameraViewMode.OrbitalFollow;
            bool axialRotationMode = mode == PlanetGridMesh.CameraViewMode.AxialRotation;
            bool orbitalFrameMode = axialRotationMode || orbitalFollowMode;

            if (mode != _lastMode)
            {
                _lastMode = mode;
                _hasFrozenSpinAngle = false;
            }
            if (orbitalFollowMode && orbitalFollowFreezeSpin && !_hasFrozenSpinAngle)
            {
                _frozenSpinAngle = rotationAngle;
                _hasFrozenSpinAngle = true;
            }

            double bodySpinAngle = orbitalFollowMode && orbitalFollowFreezeSpin
                ? _frozenSpinAngle
                : rotationAngle;

            Vector3 sunDirection;
            if (orbitalFrameMode)
            {
                // Pálya-keret: a Nap iránya CSAK az évszaktól függ, a
                // bolygó saját forgásától NEM - ez marad fix a világtérben.
                OrbitalMechanics.SunDirectionOrbitalFrame(
                    currentTimeDays, orbitalPeriodDays, orbitalPhase0,
                    out double ox, out double oy, out double oz);
                sunDirection = BodyFrameConversion.ToUnity(ox, oy, oz);

                if (planetGridMesh != null)
                {
                    // Test -> pálya forgatás (ld. OrbitalMechanics.
                    // BodyOrientationMatrix "Test -> pálya = R_tilt * R_spin"
                    // kommentje) Unity-megfelelője. A spin-előjel a
                    // StarField.SetRotationAngleRadians MÁR bevált,
                    // ELLENTÉTES forgatásából levezetve (ld. ott).
                    planetGridMesh.transform.rotation =
                        Quaternion.AngleAxis((float)axialTiltDegrees, Vector3.right) *
                        Quaternion.AngleAxis((float)(bodySpinAngle * Mathf.Rad2Deg), Vector3.up);
                }
            }
            else
            {
                if (planetGridMesh != null)
                    planetGridMesh.transform.rotation = Quaternion.identity;

                OrbitalMechanics.SunDirectionBodyFrame(
                    currentTimeDays, orbitalPeriodDays, rotationPeriodDays, axialTiltRad,
                    orbitalPhase0, rotationPhase0,
                    out double x, out double y, out double z);
                sunDirection = BodyFrameConversion.ToUnity(x, y, z);
            }

            // A fény a csillagtol a bolygo fele halad - a nap-irannyal
            // ELLENTETES iranyba "nez" (a Directional Light a sajat
            // +Z tengelye menten sugaroz).
            transform.rotation = Quaternion.LookRotation(-sunDirection);

            if (sunVisual != null)
            {
                Vector3 center = planetTransform != null ? planetTransform.position : Vector3.zero;
                sunVisual.position = center + sunDirection * sunVisualDistance;
                // A korong a bolygo (kozelitoleg a kamera) fele nez - mivel a
                // kamera mindig a bolygo KOZELEBEN van a csillag-tavolsaghoz
                // kepest, ez kamera-kovetes nelkul is kb. szembe-nezo marad.
                sunVisual.rotation = Quaternion.LookRotation(-sunDirection);
            }

            if (starField != null)
            {
                starField.EnsureBuilt();
                // A StarField a Planet gyereke, ezért AxialRotation módban
                // nem elég a lokális szögét nullázni: akkor örökölné a Planet
                // forgását. Itt valóban a világtérhez rögzítjük. Más módokban
                // a régi, ellentétes helyi forgatás szimulálja a bolygó
                // forgását az identitáson álló Planet mellett.
                if (orbitalFrameMode)
                    starField.KeepFixedInWorldSpace();
                else
                    starField.SetRotationAngleRadians(rotationAngle);
            }
        }

        [ContextMenu("Apply Now")]
        private void ApplyNow() => ApplySunDirection();
    }
}
