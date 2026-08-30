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
        [Tooltip("A bolygó sugara (PlanetGridMesh alapértéke 100) - a forgási " +
                 "sebesség a FELSZÍNTŐL mért magassággal (distance - surfaceRadius) " +
                 "skálázódik, nem a középponttól mért nyers távolsággal. Enélkül a " +
                 "sebesség minDistance-nél (120) is 'csak' 20%-kal csökkenne a " +
                 "defaulthoz képest, holott a kamera valójában a felszín közvetlen " +
                 "közelében van (100 sugár + 20 magasság).")]
        private float surfaceRadius = 100f;

        [SerializeField]
        [Tooltip("Fok/pixel/másodperc, EGYSÉGNYI felszín-feletti magasságra vetítve - " +
                 "a tényleges forgási sebesség a jelenlegi magassággal (distance - " +
                 "surfaceRadius) SZORZÓDIK, úgyhogy a felszín közelében arányosan " +
                 "sokkal lassabb, mint messziről nézve. Csökkentve 5-ről 1-re - az " +
                 "5-ös érték tipikus (~200 egységnyi) magasságban ~1000 fok/mp " +
                 "effektív sebességet adott egyetlen egérmozdulatra, ami " +
                 "eltúlzottan érzékenynek hatott.")]
        private float rotationSpeedPerAltitude = 1f;

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
                 "zoom scroll-egysegenkent. Tovabb emelve 0.4-rol 0.8-ra - a " +
                 "felhasznaloi visszajelzes szerint 0.4 meg mindig lassunak " +
                 "erzodott a teljes 100.1-800 tavolsag-tartomany bejarasahoz.")]
        private float zoomSensitivity = 0.8f;

        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;

        [SerializeField] private float initialYaw = 0f;
        [SerializeField] private float initialPitch = 20f;

        private float _yaw;
        private float _pitch;

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
            if (Input.GetMouseButton(0))
            {
                float dx = Input.GetAxis("Mouse X");
                float dy = Input.GetAxis("Mouse Y");
                // A padlo 1f-rol 0.001f-re csokkentve: a regi minDistance=120
                // mellett a magassag sosem ment 20 ala, tehat a Max(1f,...)
                // padlo sose lepett eletbe. Most (minDistance=100.1) a
                // magassag akar 0.1-ig is csokkenhet - az 1f-es padlo ott
                // MESTERSEGESEN MEGALLITOTTA a magassag-aranyos skalazodast
                // (a forgas sebessege konstans maradt 1 egysegnyi magassag
                // alatt), ami eppen a legkozelebbi zoom-tartomanyban tunt
                // "elveszettnek" - ez volt a hiba oka.
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
