using UnityEngine;
using WorldGen.Core;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    /// <summary>
    /// ND-19 (A12) 1. kör — a render-origó KÖVETÉSE és a precízió MÉRÉSE.
    ///
    /// Ez a rész még NEM tolja el a geometriát (a mesh továbbra is abszolút,
    /// bolygóközepű koordinátákban íródik): azt az `origoMode` mező
    /// `PlanetCenter` alapértéke mondja ki explicit módon, hogy ne lehessen
    /// félreérteni. Amit MOST ad: a kamera modell-téri pozíciójából
    /// determinisztikusan levezetett, hiszterézises origó + rebase-számláló +
    /// egy másodpercenkénti napló, ami a TÉNYLEGESEN elért és az abszolút
    /// (mai) felbontást méterben egymás mellé teszi. Ez a napló az, ami a
    /// 2. kör (geometria-eltolás, test-keretes renderelés) előtt/után
    /// összevethető — ld. `docs/04-decisions.md` ND-19.
    ///
    /// A "modell-tér" itt a `target` (PlanetGridMesh) LOKÁLIS, Unity-tengelyű,
    /// SKÁLÁZATLAN tere — pontosan az a tér, ahol a mesh csúcsai vannak
    /// (`PlanetGridMesh.ToUnityPoint` kimenete), tehát ahol az origó-kivonás
    /// majd történni fog.
    /// </summary>
    public partial class PlanetOrbitCamera
    {
        /// <summary>ND-19: mihez képest íródik a geometria float32-be.</summary>
        public enum RenderOriginMode
        {
            /// <summary>Bolygóközép — a floating origin ELŐTTI, abszolút viselkedés (mai alapértelmezés).</summary>
            PlanetCenter,

            /// <summary>Kamera-illesztett, rács-illesztett origó. A követés/mérés MINDIG fut, ez csak azt mondja meg, melyik origót jelentjük érvényesnek.</summary>
            CameraAnchored,
        }

        [Header("ND-19 (A12): floating origin")]
        [SerializeField]
        [Tooltip("PlanetCenter = a mai, abszolút emittálás (a geometria a bolygó " +
                 "középpontjához képest íródik float32-be). CameraAnchored = a " +
                 "kamera közelébe illesztett origó. A 2. kör (geometria-eltolás) " +
                 "előtt a CameraAnchored CSAK a jelentett origót változtatja meg, " +
                 "a renderelt mesh-t nem - ld. docs/04-decisions.md ND-19.")]
        private RenderOriginMode originMode = RenderOriginMode.PlanetCenter;

        [SerializeField]
        [Tooltip("Az ND-19 precízió-napló ki/be (másodpercenként egy sor a " +
                 "Unity Console-ba, az ND-91 kamera-napló mellé).")]
        private bool logRenderOrigin = true;

        private RenderOrigin _cameraAnchoredOrigin;
        private int _originRebaseCount;
        private double _lastCameraModelX, _lastCameraModelY, _lastCameraModelZ;
        private double _lastAltitudeModelUnits;
        private bool _hasCameraModelPosition;

        /// <summary>
        /// Az ÉRVÉNYBEN lévő render-origó. `PlanetCenter` módban a bolygóközép
        /// (a mai viselkedés), `CameraAnchored` módban a kamera-illesztett.
        /// Érvénytelen (`IsValid == false`) addig, amíg a target ismeretlen.
        /// </summary>
        public RenderOrigin CurrentRenderOrigin
        {
            get
            {
                if (!_cameraAnchoredOrigin.IsValid) return default;
                return originMode == RenderOriginMode.CameraAnchored
                    ? _cameraAnchoredOrigin
                    : RenderOrigin.PlanetCenter(_cameraAnchoredOrigin.CellUnits);
            }
        }

        /// <summary>Hányszor kellett új origóra váltani a komponens élete során — a hiszterézis hatásának mérőszáma.</summary>
        public int RenderOriginRebaseCount => _originRebaseCount;

        /// <summary>
        /// Méter / modell-egység a jelenlegi léptéknél. A viewer `radius`-a
        /// tetszőleges vizuális egység (ND-19/ND-28), a fizikai sugár
        /// `PlanetConstants.RadiusMeters` — az átváltás EBBŐL a két számból
        /// származik, nem beállított konstansból.
        /// </summary>
        public double MetersPerModelUnit => PlanetConstants.RadiusMeters / Mathf.Max(1e-9f, surfaceRadius);

        /// <summary>
        /// Az origó-követés egy lépése. A kamera modell-téri pozícióját a
        /// `target` lokális terében számolja (skálázatlanul), mert a mesh
        /// csúcsai is ott vannak.
        /// </summary>
        private void AdvanceRenderOrigin()
        {
            if (target == null)
            {
                _hasCameraModelPosition = false;
                return;
            }

            Vector3 scale = target.lossyScale;
            float uniformScale = scale.x;
            if (!(uniformScale > 0f)) uniformScale = 1f;

            // A kamera iránya a target LOKÁLIS terében (ugyanaz a hívás, amit
            // az ND-91 felszínmintázás is használ) - a bolygó tengelyforgása
            // így nem mozgatja az origót a felszínhez képest.
            Vector3 localDirection = target.InverseTransformDirection(CurrentViewDirection);
            double modelDistance = (double)distance / uniformScale;
            _lastCameraModelX = (double)localDirection.x * modelDistance;
            _lastCameraModelY = (double)localDirection.y * modelDistance;
            _lastCameraModelZ = (double)localDirection.z * modelDistance;
            _lastAltitudeModelUnits = (double)AltitudeAboveSurface / uniformScale;
            _hasCameraModelPosition = true;

            // A MODELL-téri felszínsugár (nem a világtéri): az origó
            // ugyanabban a skálázatlan térben él, mint a mesh csúcsai.
            double modelSurfaceRadius = (double)EffectiveSurfaceRadius / uniformScale;
            if (FloatingOrigin.TryAdvance(_cameraAnchoredOrigin,
                    _lastCameraModelX, _lastCameraModelY, _lastCameraModelZ,
                    _lastAltitudeModelUnits, modelSurfaceRadius, out RenderOrigin next))
            {
                _cameraAnchoredOrigin = next;
                _originRebaseCount++;
            }
        }

        /// <summary>
        /// Az egy másodpercenkénti precízió-napló sora. A "mért" jelző itt
        /// szó szerint értendő: mindkét felbontás a `FloatingOrigin` IEEE-754
        /// ULP-számításából jön, nem becslésből.
        /// </summary>
        private string BuildRenderOriginLog()
        {
            if (!_hasCameraModelPosition || !_cameraAnchoredOrigin.IsValid)
                return "[ND-19 floating origin] target ismeretlen, origo meg nem kovetheto";

            double metersPerUnit = MetersPerModelUnit;
            double localRadius = FloatingOrigin.LocalRadiusUnits(_cameraAnchoredOrigin, surfaceRadius);
            double anchored = FloatingOrigin.ResolutionMeters(localRadius, metersPerUnit);
            double absolute = FloatingOrigin.ResolutionMeters(surfaceRadius, metersPerUnit);

            // MINDKÉT origót kiírjuk: az `origin` az ÉRVÉNYBEN lévő (PlanetCenter
            // módban a bolygóközép), az `anchored` az, amire a kamera-illesztés
            // állna - és az `anchoredResolutionMeters`/`gainFactor` EZ UTÓBBIRA
            // vonatkozik, tehát a 2. kör várható haszna. Enélkül a napló
            // félreérthető: `origin=(0,0,0)` mellett állna egy 256x-os nyereség.
            return $"[ND-19 floating origin] mode={originMode} origin={CurrentRenderOrigin} " +
                   $"anchored={_cameraAnchoredOrigin} " +
                   $"rebases={_originRebaseCount} altitudeUnits={_lastAltitudeModelUnits:F6} " +
                   $"altitudeMeters={_lastAltitudeModelUnits * metersPerUnit:F1} " +
                   $"cellUnits={_cameraAnchoredOrigin.CellUnits:R} " +
                   $"cellMeters={_cameraAnchoredOrigin.CellUnits * metersPerUnit:F2} " +
                   $"absoluteResolutionMeters={absolute:F4} anchoredResolutionMeters={anchored:F6} " +
                   $"gainFactor={(anchored > 0 ? absolute / anchored : 0):F1}";
        }
    }
}
