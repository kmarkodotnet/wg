using System;
using UnityEngine;
using WorldGen.Core;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    /// <summary>
    /// ND-19 (A12) — a render-origó KÖVETÉSE, a precízió MÉRÉSE, és (A12/2)
    /// a kamera SAJÁT pozíciójának eltolása.
    ///
    /// Az 1. kör csak követett és mért. A 2. kör (A12/2) óta ez a rész
    /// - JAVASLATOT ad a <see cref="PlanetGridMesh"/>-nek
    ///   (<see cref="PlanetGridMesh.RequestRenderOrigin"/>), és
    /// - a MESH ÁLTAL MÁR ALKALMAZOTT origóhoz igazítja a kamerát
    ///   (<see cref="TryApplyShiftedTransform"/>).
    ///
    /// A két lépés szándékosan szét van választva: az origó csak akkor válik
    /// érvényessé, amikor az ahhoz emittált geometria fel is töltődött —
    /// különben a kamera egy képkockára akár egész cellányit (a magasságával
    /// összemérhető utat) elcsúszna a mesh-től. Ld. `docs/04-decisions.md`
    /// ND-19.
    ///
    /// A "modell-tér" itt a `target` (PlanetGridMesh) LOKÁLIS, Unity-tengelyű,
    /// SKÁLÁZATLAN tere. Ez a tér az A12/2 után is ABSZOLÚT test-keret marad
    /// (ld. `PlanetGridMesh.FloatingOrigin.cs` jelenet-konvenció táblája) —
    /// pontosan ezért nem kellett egyetlen `InverseTransformPoint`-hívást sem
    /// átírni a LOD-ban, az overlay-ekben és a léptékvonalzóban.
    /// </summary>
    public partial class PlanetOrbitCamera
    {
        /// <summary>ND-19: mihez képest íródik a geometria float32-be.</summary>
        public enum RenderOriginMode
        {
            /// <summary>Bolygóközép — a floating origin ELŐTTI, abszolút viselkedés (mai alapértelmezés). A geometria bitre a korábbi.</summary>
            PlanetCenter,

            /// <summary>Kamera-illesztett, rács-illesztett origó — A12/2 óta a geometriát IS eltolja (a finomított rétegen).</summary>
            CameraAnchored,
        }

        [Header("ND-19 (A12): floating origin")]
        [SerializeField]
        [Tooltip("PlanetCenter = abszolút emittálás (a geometria a bolygó " +
                 "középpontjához képest íródik float32-be) - bitre a floating " +
                 "origin ELŐTTI kép. CameraAnchored = a kamera közelébe " +
                 "illesztett origó: a FINOMÍTOTT réteg csúcsai origó-relatívak, " +
                 "a világ -s*R*O-val eltolódik, a kamera ide igazodik. " +
                 "Ld. docs/04-decisions.md ND-19 (A12/2).")]
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
                PlanetGridMesh planet = OriginPlanet;
                if (planet != null) return planet.AppliedRenderOrigin;
                if (!_cameraAnchoredOrigin.IsValid) return default;
                return originMode == RenderOriginMode.CameraAnchored
                    ? _cameraAnchoredOrigin
                    : RenderOrigin.PlanetCenter(_cameraAnchoredOrigin.CellUnits);
            }
        }

        private PlanetGridMesh _originPlanet;

        /// <summary>
        /// A target PlanetGridMesh-e. Szándékosan NEM a ND-91 felszínkövetés
        /// `_surfacePlanet` mezője: az csak `followLocalSurface` mellett él, az
        /// origó viszont attól függetlenül kell.
        /// </summary>
        private PlanetGridMesh OriginPlanet
        {
            get
            {
                if (target == null) return null;
                if (_originPlanet == null || _originPlanet.transform != target)
                    _originPlanet = target.GetComponent<PlanetGridMesh>();
                return _originPlanet;
            }
        }

        /// <summary>
        /// A12/2 — a kamera elhelyezése ELTOLT render-origó mellett. `false`, ha
        /// nincs eltolás (a hívó ilyenkor a változatlan, régi utat járja, tehát
        /// `PlanetCenter` módban a kamerapozíció BITRE a korábbi).
        ///
        /// A lényeg: a kamera test-keretbeli pozíciójából ELŐBB vonjuk ki az
        /// origót DOUBLE-ban, és csak a (legfeljebb ~1,5 cellányi) MARADÉKOT
        /// castoljuk `float32`-re. A `RenderOriginFrame` lokális tere pontosan
        /// ez az origó-relatív tér, tehát a `TransformPoint` már csak forgat és
        /// skáláz — nincs nagy számok közötti kivonás.
        ///
        /// A nézési irány SEM a két világpozíció kivonásából jön (az ugyanaz a
        /// kioltás lenne), hanem a test-keretbeli `-camBody` normalizálásából.
        /// </summary>
        private bool TryApplyShiftedTransform()
        {
            if (target == null) return false;
            PlanetGridMesh planet = OriginPlanet;
            if (planet == null) return false;
            RenderOrigin origin = planet.AppliedRenderOrigin;
            if (!origin.IsValid || origin.IsPlanetCenter) return false;
            Transform frame = planet.RenderOriginFrame;
            if (frame == null) return false;

            float uniformScale = target.lossyScale.x;
            if (!(uniformScale > 0f)) uniformScale = 1f;
            Vector3 dirBody = target.InverseTransformDirection(CurrentViewDirection);
            double modelDistance = (double)distance / uniformScale;
            double bx = dirBody.x * modelDistance;
            double by = dirBody.y * modelDistance;
            double bz = dirBody.z * modelDistance;

            var local = new Vector3(
                (float)(bx - origin.X), (float)(by - origin.Y), (float)(bz - origin.Z));
            transform.position = frame.TransformPoint(local);

            double length = Math.Sqrt(bx * bx + by * by + bz * bz);
            if (length > 0.0)
            {
                var towardCentre = new Vector3(
                    (float)(-bx / length), (float)(-by / length), (float)(-bz / length));
                transform.rotation = Quaternion.LookRotation(
                    frame.TransformDirection(towardCentre), Vector3.up);
            }
            return true;
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

            // A12/2: a JAVASLAT atadasa. A mesh dont arrol, mikor lep at ra (a
            // geometria ujra-emittalasaval EGY lepesben) - a kamera csak azt
            // olvassa vissza, ami MAR alkalmazva van.
            PlanetGridMesh planet = OriginPlanet;
            if (planet != null)
            {
                planet.RequestRenderOrigin(originMode == RenderOriginMode.CameraAnchored
                    ? _cameraAnchoredOrigin
                    : RenderOrigin.PlanetCenter(_cameraAnchoredOrigin.CellUnits));
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
            RenderOrigin applied = CurrentRenderOrigin;
            double appliedRadius = applied.IsValid
                ? FloatingOrigin.LocalRadiusUnits(applied, surfaceRadius) : surfaceRadius;
            double appliedResolution = FloatingOrigin.ResolutionMeters(appliedRadius, metersPerUnit);
            return $"[ND-19 floating origin] mode={originMode} applied={applied} " +
                   $"appliedResolutionMeters={appliedResolution:F6} " +
                   $"appliedGainFactor={(appliedResolution > 0 ? absolute / appliedResolution : 0):F1} " +
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
