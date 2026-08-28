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
    /// FONTOS KONVENCIÓ: a Planet GameObject transform.rotation-ját
    /// IDENTITÁSON kell tartani (a PlanetGridMesh nem forgatja a mesh-et) -
    /// a nap-irány már a test-keretben van kiszámolva, tehát a mesh-nek
    /// NEM kell forognia a nap/éj ciklushoz, elég a fényt forgatni. Ha a
    /// Planet objektum elforog, a megvilágítás hibás lesz.
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

        [SerializeField] private bool autoAdvance = false;
        [SerializeField] private double daysPerSecond = 5.0;

        private void OnEnable() => ApplySunDirection();
        private void OnValidate() => ApplySunDirection();

        private void Update()
        {
            if (autoAdvance && Application.isPlaying)
                currentTimeDays += daysPerSecond * Time.deltaTime;

            ApplySunDirection();
        }

        private void ApplySunDirection()
        {
            if (orbitalPeriodDays <= 0.0 || rotationPeriodDays <= 0.0)
                return; // érvénytelen bemenet (pl. Inspectorban 0-ra állítva) - ne törjön el

            double axialTiltRad = axialTiltDegrees * Math.PI / 180.0;
            OrbitalMechanics.SunDirectionBodyFrame(
                currentTimeDays, orbitalPeriodDays, rotationPeriodDays, axialTiltRad,
                orbitalPhase0, rotationPhase0,
                out double x, out double y, out double z);

            var sunDirection = new Vector3((float)x, (float)y, (float)z);
            // A fény a csillagtol a bolygo fele halad - a nap-irannyal
            // ELLENTETES iranyba "nez" (a Directional Light a sajat
            // +Z tengelye menten sugaroz).
            transform.rotation = Quaternion.LookRotation(-sunDirection);
        }

        [ContextMenu("Apply Now")]
        private void ApplyNow() => ApplySunDirection();
    }
}
