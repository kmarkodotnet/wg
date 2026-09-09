using UnityEngine;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M9 első lépése: egyszerű orbit+zoom kamera a Planet nézethez -
    /// bal egérgomb húzása körbeforgatja a célpont körül, a görgő
    /// közelít/távolít. Ez a kamera-átmenetek (kontinens/régió zoom)
    /// előfeltétele - anélkül nincs mihez "átmenetet" csinálni.
    ///
    /// Tisztán vizuális/UX kód, NEM szimulációs logika - nincs I1/I2
    /// determinizmus-érintettség (float, System.Math sem kritikus úton).
    /// </summary>
    public class PlanetOrbitCamera : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("A célpont, ami körül a kamera forog. Üresen hagyva automatikusan " +
                 "megkeresi a jelenetben lévő PlanetGridMesh-t.")]
        private Transform target;

        [SerializeField] private float distance = 300f;

        [SerializeField]
        [Tooltip("M9: az eredeti 120-as ertek (surfaceRadius=100 mellett csak " +
                 "~20 egysegnyi magassagot engedett) SOHA nem ert le a kvadfa-LOD " +
                 "finomabb szintjeihez (level 6-hoz mar ~5 egysegnyi magassag " +
                 "kell, level 9-hez ~0.6 - ld. docs/05-milestones.md §9, " +
                 "AdaptiveQuadTree.GetCenterAndBoundingRadius szamitasa alapjan " +
                 "mert). Enelkul a leszallasi-zoom finomodasa soha nem valt " +
                 "lathatova, fuggetlenul attol, hogy a mesh-oldal helyes-e. " +
                 "FONTOS: ha a jelenetben (PlanetView.unity) ez az ertek mar " +
                 "felul van irva az Inspectorban, a szerkesztett scene-beli ertek " +
                 "eloz - ott is csokkentsd kezzel, ha meg mindig 120 all.")]
        private float minDistance = 100.1f;

        [SerializeField] private float maxDistance = 800f;

        [SerializeField]
        [Tooltip("A bolygó sugara (PlanetGridMesh alapértéke 100) - MIND a " +
                 "forgás, MIND a zoom a FELSZÍNTŐL mért magassággal " +
                 "(distance - surfaceRadius) skálázódik, nem a középponttól " +
                 "mért nyers távolsággal.")]
        private float surfaceRadius = 100f;

        [SerializeField]
        [Tooltip("Fok/pixel/másodperc, EGYSÉGNYI felszín-feletti magasságra " +
                 "vetítve - a tényleges forgási sebesség a jelenlegi " +
                 "magassággal (distance - surfaceRadius) SZORZÓDIK, hogy a " +
                 "felszín közelében a forgás ÉRDEMBEN lassabb legyen, mint " +
                 "messziről nézve - így a felszín-részletek változása " +
                 "kamera-mozgás közben is követhető marad. EXPLICIT " +
                 "FELHASZNALOI KERES: a magassaggal aranyos lassitas a CEL, " +
                 "nem opcionalis finomhangolas. FONTOS ELOZMENY: egy korabbi " +
                 "verzioban itt egy `Mathf.Max(1f, magassag)` padlo volt, ami " +
                 "a regi (nagyobb) minDistance mellett sose lepett eletbe, de " +
                 "a mostani, felszinhez nagyon kozeli minDistance-nel (100.1) " +
                 "MESTERSEGESEN MEGALLITOTTA a skalazodast 1 egysegnyi " +
                 "magassag alatt - ezt a padlot vegleg 0.001-re csokkentettuk " +
                 "(ld. Update()), hogy a skalazas a legkozelebbi zoomig is " +
                 "ervenyben maradjon.")]
        private float rotationSpeedPerAltitude = 0.3f;

        [SerializeField]
        [Tooltip("M9: aranyos (nem additiv) zoom - minden scroll-egyseg " +
                 "UGYANAKKORA RELATIV valtozast okoz a felszin-feletti " +
                 "magassagban, fuggetlenul attol, hogy tavol vagy kozel vagyunk. " +
                 "Ez azert kell, mert a melyebb LOD-szintek (ld. PlanetGridMesh " +
                 "adaptiveMaxLevel) csak a felszin NAGYON keskeny savjaban " +
                 "aktivalodnak (pl. level 8-hoz mar ~1.25 egysegnyi magassagon " +
                 "belul kell lenni egy 100-as sugaru bolygonal) - egy regi, " +
                 "additiv zoom (150 egyseg/kattintas) gyakorlatilag athuzna " +
                 "ezen a savon, sose lehetne belelonni. Nagyobb ertek = gyorsabb " +
                 "zoom scroll-egysegenkent. Tobbszori emeles utan (0.15->0.4->0.8) " +
                 "a felhasznaloi visszajelzes szerint MEG MINDIG lassu volt - " +
                 "most 2.0-ra emelve (kb. 25x az eredeti erzekenyseghez kepest " +
                 "osszesen), hogy a teljes 100.1-800 tavolsag-tartomanyt " +
                 "hatarozottan kevesebb gorgo-kattintassal lehessen bejarni.")]
        private float zoomSensitivity = 2.0f;

        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;

        [SerializeField] private float initialYaw = 0f;
        [SerializeField] private float initialPitch = 20f;

        [Header("M9: Nézetszint (kontinens/régió kamera-átmenetek)")]
        [SerializeField]
        [Tooltip("E FELETT az altitude (felszín feletti magasság) felett Planet nézetszint " +
                 "(a teljes glóbusz relevánsabb, mint egyetlen kontinens/régió). KALIBRÁLATLAN " +
                 "becslés - élő Unity-ellenőrzés hátra, ld. docs/06-user-verification-checklist.md.")]
        private float continentViewAltitude = 60f;

        [SerializeField]
        [Tooltip("E ALATT az altitude Region nézetszint (Continent felette, Planet e felett is). " +
                 "KALIBRÁLATLAN becslés.")]
        private float regionViewAltitude = 15f;

        /// <summary>
        /// M9 "nézetszint-váltás" (docs/backlog.md "Kontinens/régió kamera-
        /// átmenetek"): melyik panel-szint a relevánsabb a JELENLEGI zoom-
        /// mélységnél. Tisztán a kamera-magasságból származtatott, NEM
        /// szimulációs állapot - a WorldGenPanelUI ezt olvassa ki minden
        /// képkockán, hogy a legközelebbi kontinens/régió nevét
        /// kiemelje/megjelenítse.
        /// </summary>
        public enum ViewLevel { Planet, Continent, Region }

        public ViewLevel CurrentViewLevel
        {
            get
            {
                float altitude = AltitudeAboveSurface;
                if (altitude > continentViewAltitude) return ViewLevel.Planet;
                return altitude > regionViewAltitude ? ViewLevel.Continent : ViewLevel.Region;
            }
        }

        /// <summary>A kamera felszín feletti magassága (nem a nyers, középponttól mért `distance`) - ld. `distance` mező doksija.</summary>
        public float AltitudeAboveSurface => distance - surfaceRadius;

        /// <summary>
        /// A target-tól a kamera fele mutató, Unity-vilagteri, normalizalt
        /// irany - UGYANAZ a konvencio, amit a <see cref="FlyToDirection"/>
        /// bemenetkent var (ld. ott a doksit) - tehat egy `WorldGenPanelData.
        /// CenterDirection`-bol vilagterbe forgatott irannyal kozvetlenul
        /// osszehasonlithato (pl. dot-szorzattal a "legkozelebbi kontinens"
        /// meghatarozasahoz).
        /// </summary>
        public Vector3 CurrentViewDirection => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back;

        private float _yaw;
        private float _pitch;

        // FELHASZNALOI KERES (2026-09-06): "a canvason megjelenitett info
        // (kontinensek/regiok neveire) kattintva odaugrik-e a kamera" - ld.
        // WorldGenPanelUI (kattinthato link) + WorldGenPanelData.CenterDirection
        // (a celpont-irany forrasa). Amig fut egy FlyTo-animacio, a kezi
        // egerbevitel (Update()) FELULIRJA a repulest (a felhasznalo barmikor
        // atveheti az iranyitast) - ld. Update() elejen a `_flyToCoroutine != null` ag.
        private Coroutine _flyToCoroutine;

        /// <summary>
        /// Animaltan a target korul UGY forgatja a kamerat, hogy vegul a
        /// megadott `direction` (a target-tol a celpont fele mutato, MAR
        /// Unity-terbeli, NORMALIZALT irany - ld. WorldGenPanelData.CenterDirection)
        /// iranyaban legyen, opcionalisan uj FELSZIN FELETTI MAGASSAGRA
        /// (NEM a kozepponttol mert nyers `distance`-re - ld. `distance`
        /// mezo doksija: a forgas/zoom sebesseg is a felszin feletti
        /// magassaggal aranyos MINDENUTT ebben az osztalyban, ez az API
        /// KONZISZTENS ezzel). MATEMATIKAI ELOFELTETEL, MEG NEM UNITY-BEN
        /// TESZTELT: az inverz keplet feltetelezi az `ApplyTransform`-ban
        /// hasznalt `Quaternion.Euler(pitch,yaw,0) * Vector3.back` forgatasi
        /// konvenciot - ha az iranyeredmeny Unityben tukrozottnek/forditottnak
        /// tunik, egy elojelvaltas (pitch vagy yaw negalasa) a valoszinu javitas.
        /// </summary>
        public void FlyToDirection(Vector3 direction, float? newAltitudeAboveSurface = null, float durationSeconds = 1.0f)
        {
            if (direction.sqrMagnitude < 1e-12f)
            {
                Debug.Log($"PlanetOrbitCamera [diag]: FlyToDirection - direction majdnem nullvektor " +
                    $"(sqrMagnitude={direction.sqrMagnitude}), NEMA early-return.");
                return;
            }
            direction = direction.normalized;

            // Inverz keplet az ApplyTransform() `Quaternion.Euler(pitch,yaw,0) *
            // Vector3.back` kepletehez (levezetve, NEM Unity-ben leellenorizve):
            // dir = (-cos(pitch)*sin(yaw), sin(pitch), -cos(pitch)*cos(yaw))
            // => pitch = asin(dir.y), yaw = atan2(-dir.x, -dir.z)
            float targetPitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
            float targetYaw = Mathf.Atan2(-direction.x, -direction.z) * Mathf.Rad2Deg;
            float currentAltitude = distance - surfaceRadius;
            float targetDistance = Mathf.Clamp(surfaceRadius + (newAltitudeAboveSurface ?? currentAltitude), minDistance, maxDistance);

            // IDEIGLENES DIAGNOSZTIKA (2026-09-06): ld. WorldGenPanelUI [diag]
            // - ez megmutatja, hogy a szamolt cel-szogek/tavolsag ertelmesek-e,
            // es hogy a korutin ENGEDELYEZVE van-e (StartCoroutine csendben
            // nem csinal semmit, ha a GameObject/komponens INAKTIV).
            Debug.Log($"PlanetOrbitCamera [diag]: FlyToDirection - direction={direction}, " +
                $"currentPitch={_pitch}, currentYaw={_yaw}, currentDistance={distance} -> " +
                $"targetPitch={targetPitch}, targetYaw={targetYaw}, targetDistance={targetDistance}, " +
                $"gameObject.activeInHierarchy={gameObject.activeInHierarchy}, enabled={enabled}.");

            if (_flyToCoroutine != null) StopCoroutine(_flyToCoroutine);
            _flyToCoroutine = StartCoroutine(FlyToCoroutine(targetPitch, targetYaw, targetDistance, durationSeconds));
        }

        private System.Collections.IEnumerator FlyToCoroutine(float targetPitch, float targetYaw, float targetDistance, float durationSeconds)
        {
            float startPitch = _pitch;
            float startYaw = _yaw;
            float startDistance = distance;
            float t = 0f;
            while (t < durationSeconds)
            {
                t += Time.deltaTime;
                float f = durationSeconds > 0f ? Mathf.Clamp01(t / durationSeconds) : 1f;
                float eased = f * f * (3f - 2f * f); // smoothstep - gyorsul, majd lassul
                _pitch = Mathf.Lerp(startPitch, targetPitch, eased);
                // LerpAngle: a legrovidebb koriranyban forog (nem a hosszabb,
                // 360 fok koruli uton), mert `_yaw` korkoros mennyiseg.
                _yaw = Mathf.LerpAngle(startYaw, targetYaw, eased);
                distance = Mathf.Lerp(startDistance, targetDistance, eased);
                ApplyTransform();
                yield return null;
            }
            _pitch = targetPitch;
            _yaw = targetYaw;
            distance = targetDistance;
            ApplyTransform();
            _flyToCoroutine = null;
        }

        private void Start()
        {
            if (target == null)
            {
                var planet = FindObjectOfType<PlanetGridMesh>();
                if (planet != null) target = planet.transform;
            }

            _yaw = initialYaw;
            _pitch = initialPitch;
            ApplyTransform();
        }

        private void Update()
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            // GYOKEROK-JAVITAS (2026-09-06, "kattintás detektálva, de a
            // kamera nem mozdul" - diagnosztikai naplózással kiderítve):
            // az EREDETI feltetel `Input.GetMouseButton(0)` volt onmagaban -
            // ez az a kattintas SORAN, amig az egergomb LENYOMVA marad (nem
            // csak `GetMouseButtonDown` az elso frame-ben), MINDEN kovetkezo
            // frame-ben IGAZ - tehat UGYANAZ a kattintas, ami a
            // WorldGenPanelUI-n keresztul elinditotta a FlyToDirection-t,
            // MAR A KOVETKEZO Update()-jeben (meg mielott az egergombot
            // felengednenk) megszakitotta a MEG EL SEM INDULT animaciot -
            // a kamera gyakorlatilag SOHA nem mozdulhatott, meg egyetlen
            // frame-nyit sem. JAVITAS: csak akkor szamit "a felhasznalo
            // atvette az iranyitast"-nak, ha TENYLEGESEN HUZZA is az egeret
            // (dx/dy nem nulla), nem pusztan azert, mert a gomb (meg mindig)
            // le van nyomva.
            bool isDragging = Input.GetMouseButton(0) && (Mathf.Abs(dx) > 0.0001f || Mathf.Abs(dy) > 0.0001f);

            // A felhasznalo BARMIKOR atveheti az iranyitast egy folyamatban
            // levo FlyTo-animaciotol - VALODI egerhuzas vagy gorgetes
            // megszakitja (egy allo kattintas, ami epp a FlyTo-t inditotta,
            // NEM).
            if (_flyToCoroutine != null && (isDragging || Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f))
            {
                StopCoroutine(_flyToCoroutine);
                _flyToCoroutine = null;
            }

            if (Input.GetMouseButton(0))
            {
                // Padlo 0.001-en (NEM 1f-en, ld. rotationSpeedPerAltitude
                // doksija) - igy a magassag-aranyos lassitas a legkozelebbi
                // zoomig (minDistance=100.1, magassag~0.1) is ervenyben marad.
                float altitude = Mathf.Max(0.001f, distance - surfaceRadius);
                float effectiveRotationSpeed = rotationSpeedPerAltitude * altitude;
                _yaw += dx * effectiveRotationSpeed * Time.deltaTime;
                _pitch -= dy * effectiveRotationSpeed * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                // Aranyos zoom a felszin-feletti magassagon (nem a nyers
                // kozeppont-tavolsagon) - igy a felszin kozeleben (ahol a
                // finom LOD-savok vannak) is ugyanolyan HASZNALHATO marad a
                // zoom, mint messziről, csak kisebb abszolut lepesekkel.
                float altitude = Mathf.Max(0.001f, distance - surfaceRadius);
                float newAltitude = altitude * Mathf.Exp(-scroll * zoomSensitivity);
                distance = surfaceRadius + newAltitude;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }

            ApplyTransform();
        }

        private void ApplyTransform()
        {
            Vector3 targetPos = target != null ? target.position : Vector3.zero;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = targetPos + rot * new Vector3(0f, 0f, -distance);
            transform.LookAt(targetPos);
        }
    }
}
