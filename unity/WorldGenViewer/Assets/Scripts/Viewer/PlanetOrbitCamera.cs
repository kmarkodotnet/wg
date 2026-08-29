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
        [SerializeField] private float minDistance = 120f;
        [SerializeField] private float maxDistance = 800f;

        [SerializeField] private float rotationSpeed = 120f;
        [SerializeField] private float zoomSpeed = 150f;

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
                _yaw += dx * rotationSpeed * Time.deltaTime;
                _pitch -= dy * rotationSpeed * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                distance -= scroll * zoomSpeed;
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
