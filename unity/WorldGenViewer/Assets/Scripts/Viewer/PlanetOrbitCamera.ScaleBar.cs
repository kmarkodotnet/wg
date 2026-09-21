using System;
using System.Threading.Tasks;
using UnityEngine;
using WorldGen.Core;

namespace WorldGen.Viewer
{
    public partial class PlanetOrbitCamera
    {
        private const int ScaleBarFailureGraceCount = 8;

        [Header("M9: Fizikai lépték (ND-84)")]
        [SerializeField] private bool showPhysicalScaleBar = true;
        [SerializeField, Range(80f, 320f)] private float scaleBarTargetWidthPixels = 180f;
        [SerializeField, Range(12f, 100f)] private float scaleBarBottomMarginPixels = 36f;
        [SerializeField, Range(0.05f, 1f)] private float scaleBarRefreshIntervalSeconds = 0.15f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("MERT JAVITAS (2026-09-18): a leptekcsik ujraszamolasa a FOSZALON " +
                 "30-180ms-ot vitt el, masodpercenkent 6-7-szer (658-3393 " +
                 "ComputeElevationAtPoint hivas kerdesenkent - ld. a " +
                 "[ND-84 scale diag] sorokat a PerfLog_20260918_170408-ban). Ez volt a " +
                 "felhasznalo altal jelzett rotacio/zoom szaggatas tenyleges oka - NEM a " +
                 "LOD-cut merete (a budget 8000-re csokkentese nem segitett). Mozgas " +
                 "kozben ez a munka amugy is KARBA MEGY: az IsScaleBarContextCurrent " +
                 "minden kepkockan ervenyteleniti az eredmenyt (ezert latszik 'Leptek: —' " +
                 "forgatas alatt). Ezert most csak akkor szamolunk ujra, ha a kamera " +
                 "ennyi ideig MOZDULATLAN volt. 0 = a regi (mindig szamol) viselkedes.")]
        private float scaleBarSettleSeconds = 0.12f;

        [SerializeField, Range(0.2f, 3f)]
        [Tooltip("GARANCIA (2026-09-18, masodik kor): ha a nezet ennyi ideig NEM " +
                 "nyugszik meg (pl. tengelyforgas-mod, vagy a felszinkoveto " +
                 "distance-korrekcio folyamatos apro mozgasa), akkor is lefut EGY " +
                 "szamitas, hogy a leptekcsik ne tunjon el vegleg. Igy a legrosszabb " +
                 "eset ~1 szamitas/masodperc a korabbi 6-7 helyett.")]
        private float scaleBarMaxWaitSeconds = 1.0f;

        private Camera? _scaleBarCamera;
        private PlanetGridMesh? _scaleBarPlanet;
        private GUIStyle? _scaleBarLabelStyle;
        private bool _scaleBarValid;
        private int _scaleBarConsecutiveFailures;
        private float _scaleBarPixelWidth;
        private double _scaleBarDistanceMeters;
        private float _scaleReferenceCenterX;
        private float _scaleReferenceCenterY;
        private float _scaleViewportBottomGuiY;
        private float _nextScaleBarRefreshTime;
        private Transform? _scaleSampleTarget;
        private float _scaleSampleYaw, _scaleSamplePitch, _scaleSampleDistance, _scaleSampleFov;
        private Matrix4x4 _scaleSampleTargetMatrix;
        private Rect _scaleSampleViewport;
        private int _scaleSampleWorldRevision, _scaleSampleScreenHeight;

        private void OnGUI()
        {
            if (!showPhysicalScaleBar)
            {
                InvalidateScaleBar();
                return;
            }

            // A frissítési időköz alatt sem rajzolhatunk régi nézethez tartozó számot.
            if (_scaleBarValid && !IsScaleBarContextCurrent()) InvalidateScaleBar();

            // MERT JAVITAS (2026-09-18), ket kapu:
            //  (1) egy VALTOZATLAN nezethez pontosan EGY szamitas tartozik
            //      (akar sikerult, akar nem) - korabban a puszta 0.15s-es
            //      idozito miatt allo kameraval is ujrafutott a 30-180ms-os
            //      szamitas, orokke. A "sikertelen" esetet is le kell fedni
            //      (pl. az eg fele nezve a sugar nem talal felszint), kulonben
            //      ott maradna a masodpercenkenti ujraprobalkozas.
            //  (2) IsScaleBarViewSettled - mozgas kozben egyaltalan nem
            //      szamolunk, mert az eredmenyt a kovetkezo kepkocka amugy is
            //      eldobja (ezert latszik ilyenkor "Leptek: —").
            // Igy a koltseg ~kameramegallasonkent EGY szamitas, a korabbi
            // masodpercenkenti 6-7 helyett.
            if (Event.current.type == EventType.Repaint)
            {
                // #8 (2026-09-21): a KESZ hatterszalas eredmeny atvetele - ez
                // a fo szalon csak ket double ertekadas.
                TryAdoptCompletedScaleBar();

                if (Time.unscaledTime >= _nextScaleBarRefreshTime && ShouldRecomputeScaleBarNow())
                {
                    _nextScaleBarRefreshTime = Time.unscaledTime
                        + Mathf.Max(0.05f, scaleBarRefreshIntervalSeconds);
                    _scaleBarComputedForCurrentView = true;
                    _lastScaleBarComputeTime = Time.unscaledTime;
                    StartScaleBarComputation();
                }
            }

            DrawPhysicalScaleBar();
        }

        private const double ScaleBarPerfLogThresholdMs = 1.0;

        // ====================================================================
        // #8 (2026-09-21, felhasznaloi keres): "a kalkulacio hatterszalban
        // fusson, de NE blokkolja a fo szalat".
        //
        // A MERT PROBLEMA. A szamitas 30-180 ms a FO SZALON, keresenkent
        // 658-3393 ComputeElevationAtPoint hivassal. Tengelyforgas-modban a
        // nezet SOHA nem nyugszik meg (a bolygo transformja minden kepkockaban
        // valtozik), ezert az eredmeny azonnal ervenytelenne valt, a
        // garancia-ag pedig masodpercenkent ujraszamolt - innen a
        // "tengelyforgas eseten folyamatosan akad" visszajelzes.
        //
        // AMI A FO SZALON MARAD (mikroszekundumok): a kamera bazisanak
        // osszeallitasa. A Camera.ScreenPointToRay es a Transform-hivasok CSAK
        // a fo szalrol engedelyezettek, ezert a sugarat NEM azokkal allitjuk
        // elo a hatterben, hanem a ScaleBarMath.CameraRayBasis tiszta
        // aritmetikajaval (offline tesztelve: CameraRayBasisTests - a szogek
        // 1e-12-ig egyeznek a perspektiv projekcio definiciojaval).
        //
        // AMI HATTERSZALRA KERUL: a teljes kereses (TryFindMeasurableWidth +
        // TrySolveScale) es benne minden felszin-mintavetel. Ezek tiszta
        // olvasasok a Build ota valtozatlan mezokbol.
        //
        // EGYSZERRE EGY szamitas fut (single-flight), es az eredmenyt csak
        // akkor vesszuk at, ha a nezet-pillanatkep MEG ervenyes - kulonben
        // eldobjuk, ES a korabbi ertek marad kirajzolva (nem ugrik "—"-re).
        // A hatterszal SOSEM ir kozvetlenul a kirajzolt ertekekbe.
        // ====================================================================

        private Task _scaleBarTask;
        private int _scaleBarTaskGeneration;
        private volatile bool _scaleBarTaskDone;
        private volatile bool _scaleBarTaskSucceeded;
        private double _scaleBarTaskWidth;
        private double _scaleBarTaskDistance;
        private double _scaleBarTaskMilliseconds;
        private int _scaleBarTaskEvaluations;

        /// <summary>
        /// Osszeallitja a kamera lokalis bazisat es elinditja a hatterszalas
        /// kereseset. A fo szalon CSAK nehany Transform-transzformacio fut, a
        /// draga resz nem.
        /// </summary>
        private void StartScaleBarComputation()
        {
            if (_scaleBarTask != null && !_scaleBarTaskDone)
                return; // MAR fut egy szamitas - a kovetkezo tick ujra probalja.

            if (_scaleBarCamera == null) _scaleBarCamera = GetComponent<Camera>();
            if (target == null) _scaleBarPlanet = null;
            else if (_scaleBarPlanet == null || _scaleBarPlanet.transform != target)
                _scaleBarPlanet = target.GetComponent<PlanetGridMesh>();
            if (_scaleBarCamera == null || _scaleBarCamera.orthographic
                || _scaleBarCamera.fieldOfView <= 0f || _scaleBarPlanet == null || target == null
                || !_scaleBarPlanet.IsSurfaceMeasurementCurrent)
            {
                InvalidateScaleBar();
                return;
            }

            Vector3 scale = target.lossyScale;
            float scaleMagnitude = Mathf.Max(1f, Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (!(scale.x > 0f) || float.IsInfinity(scaleMagnitude)
                || Mathf.Abs(scale.x - scale.y) > scaleMagnitude * 1e-5f
                || Mathf.Abs(scale.x - scale.z) > scaleMagnitude * 1e-5f)
            {
                InvalidateScaleBar();
                return;
            }

            Rect viewport = _scaleBarCamera.pixelRect;
            if (viewport.width < 20f || viewport.height <= 0f)
            {
                InvalidateScaleBar();
                return;
            }

            _scaleReferenceCenterX = viewport.x + viewport.width * 0.5f;
            _scaleReferenceCenterY = viewport.y + viewport.height * 0.5f;
            _scaleViewportBottomGuiY = Screen.height - viewport.y;

            // A kamera bazisa a BOLYGO LOKALIS tereben.
            Transform cameraTransform = _scaleBarCamera.transform;
            Vector3 localOrigin = target.InverseTransformPoint(cameraTransform.position);
            Vector3 localForward = target.InverseTransformDirection(cameraTransform.forward);
            Vector3 localRight = target.InverseTransformDirection(cameraTransform.right);
            Vector3 localUp = target.InverseTransformDirection(cameraTransform.up);
            double tanHalfVertical = Math.Tan(_scaleBarCamera.fieldOfView * Math.PI / 360.0);
            double aspect = viewport.width / (double)viewport.height;

            var basis = new ScaleBarMath.CameraRayBasis(
                new ScaleBarMath.Vector3d(localOrigin.x, localOrigin.y, localOrigin.z),
                new ScaleBarMath.Vector3d(localForward.x, localForward.y, localForward.z),
                new ScaleBarMath.Vector3d(localRight.x, localRight.y, localRight.z),
                new ScaleBarMath.Vector3d(localUp.x, localUp.y, localUp.z),
                tanHalfVertical, aspect);
            if (!basis.IsUsable)
            {
                InvalidateScaleBar();
                return;
            }

            double viewportWidth = viewport.width;
            double preferredWidth = Math.Min(
                Math.Max(20.0, scaleBarTargetWidthPixels),
                Math.Max(20.0, viewportWidth * 0.45));
            PlanetGridMesh planet = _scaleBarPlanet;
            double baseRadius = planet.SurfaceMeasurementBaseRadius;

            CaptureScaleBarContext();
            _scaleBarTaskGeneration++;
            int generation = _scaleBarTaskGeneration;
            _scaleBarTaskDone = false;
            planet.ResetScaleSurfaceEvaluationCount();

            _scaleBarTask = Task.Run(() =>
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                bool ok = false;
                double solvedWidth = 0.0;
                double roundDistance = 0.0;
                try
                {
                    ok = SolveScaleBarOffThread(
                        basis, viewportWidth, preferredWidth, baseRadius, planet,
                        out solvedWidth, out roundDistance);
                }
                catch (Exception)
                {
                    // A hatterszal SOHA nem dobhat a Unityre: ha a vilag
                    // kozben ujraepult (a planet mezoi lecserelodtek), az
                    // eredmenyt egyszeruen eldobjuk - a generacio-ellenorzes
                    // es az IsScaleBarContextCurrent ugyanezt tenne.
                    ok = false;
                }
                timer.Stop();
                if (generation != _scaleBarTaskGeneration)
                {
                    _scaleBarTaskDone = true;
                    return;
                }
                _scaleBarTaskWidth = solvedWidth;
                _scaleBarTaskDistance = roundDistance;
                _scaleBarTaskMilliseconds = timer.Elapsed.TotalMilliseconds;
                _scaleBarTaskEvaluations = planet.ScaleSurfaceEvaluationCount;
                _scaleBarTaskSucceeded = ok;
                _scaleBarTaskDone = true;
            });
        }

        /// <summary>
        /// A teljes kereses, TISZTA szamitassal - ez fut a hatterszalon.
        /// Unity API-t nem hiv: a sugarat a CameraRayBasis adja, a felszint a
        /// planet Build ota valtozatlan mezoi.
        /// </summary>
        private static bool SolveScaleBarOffThread(
            ScaleBarMath.CameraRayBasis basis, double viewportWidth, double preferredWidth,
            double baseRadius, PlanetGridMesh planet,
            out double solvedWidth, out double roundDistance)
        {
            solvedWidth = 0.0;
            roundDistance = 0.0;

            bool RadiusAt(ScaleBarMath.Vector3d direction, out double displayedRadius)
                => planet.TryGetScaleSurfaceRadius(
                    new Vector3((float)direction.X, (float)direction.Y, (float)direction.Z),
                    out displayedRadius);

            bool Measure(double pixelWidth, out double distanceMeters)
            {
                distanceMeters = 0.0;
                if (!(pixelWidth > 0.0) || !(viewportWidth > 0.0)) return false;
                // A ket mintapont a viewport KOZEPETOL +-pixelWidth/2-re van;
                // NDC-ben ez +-(pixelWidth / viewportWidth), mert az NDC a FEL
                // szelessegre normalizal.
                double ndcHalf = pixelWidth / viewportWidth;
                ScaleBarMath.Vector3d leftDirection = ScaleBarMath.RayDirectionAtNdc(basis, -ndcHalf, 0.0);
                ScaleBarMath.Vector3d rightDirection = ScaleBarMath.RayDirectionAtNdc(basis, ndcHalf, 0.0);
                if (!ScaleBarMath.TryIntersectRadialSurface(
                        basis.Origin, leftDirection, baseRadius, RadiusAt, out ScaleBarMath.Vector3d left)
                    || !ScaleBarMath.TryIntersectRadialSurface(
                        basis.Origin, rightDirection, baseRadius, RadiusAt, out ScaleBarMath.Vector3d right))
                {
                    return false;
                }
                distanceMeters = ScaleBarMath.GreatCircleDistanceMeters(
                    left, right, PlanetConstants.RadiusMeters);
                return distanceMeters > 0.0
                    && !double.IsNaN(distanceMeters) && !double.IsInfinity(distanceMeters);
            }

            if (!ScaleBarMath.TryFindMeasurableWidth(
                    Measure, preferredWidth, 20.0, out double maximumWidth, out double maximumDistance))
            {
                return false;
            }

            return ScaleBarMath.TrySolveScale(
                Measure, maximumWidth, maximumDistance, 4,
                out solvedWidth, out roundDistance, out _, out _, out _);
        }

        /// <summary>
        /// A kesz hatterszalas eredmeny atvetele a FO SZALON - ket
        /// ertekadas, itt nincs szamitas. Ha a nezet kozben elmozdult, az
        /// eredmenyt eldobjuk, es a korabbi ertek marad kirajzolva.
        /// </summary>
        private void TryAdoptCompletedScaleBar()
        {
            if (_scaleBarTask == null || !_scaleBarTaskDone) return;
            _scaleBarTask = null;

            if (_scaleBarPlanet != null && _scaleBarTaskMilliseconds >= ScaleBarPerfLogThresholdMs)
            {
                _scaleBarPlanet.LogCameraSurface(FormattableString.Invariant(
                    $"[ND-84 scale diag] hatterszal={_scaleBarTaskMilliseconds:F2}ms evalCount={_scaleBarTaskEvaluations} ok={_scaleBarTaskSucceeded} frame={Time.frameCount} foSzalonMs=0"));
            }

            if (!_scaleBarTaskSucceeded)
            {
                RegisterTransientScaleBarFailure();
                return;
            }
            if (!IsScaleBarContextCurrent())
                return;

            _scaleBarPixelWidth = (float)_scaleBarTaskWidth;
            _scaleBarDistanceMeters = _scaleBarTaskDistance;
            _scaleBarConsecutiveFailures = 0;
            _scaleBarValid = true;
        }

        // MOZGAS-DETEKTALAS (2026-09-18) a fenti kapuhoz. SZANDEKOSAN kulon
        // mezokben tarolt pillanatkep: a _scaleSample* mezok a MAR KISZAMOLT
        // eredmeny ervenyesseget kotik a nezethez (IsScaleBarContextCurrent),
        // ez viszont csak azt figyeli, valtozott-e a nezet az ELOZO KEPKOCKA
        // ota. A ketto elkulonitese nelkul a recompute sajat kontextus-
        // mentese (CaptureScaleBarContext) azonnal "mozdulatlannak" jelentene
        // a kamerat, es a kapu sose fogna.
        private bool _hasScaleMotionSnapshot;
        private float _scaleMotionYaw, _scaleMotionPitch, _scaleMotionDistance, _scaleMotionFov;
        private Matrix4x4 _scaleMotionTarget;
        private Rect _scaleMotionViewport;
        private int _scaleMotionScreenHeight;
        private float _scaleBarStillSince;
        private float _lastScaleBarComputeTime = float.NegativeInfinity;

        /// <summary>
        /// Lefutott-e mar a szamitas a JELENLEGI (valtozatlan) nezethez. A
        /// nezet legkisebb valtozasakor <see cref="IsScaleBarViewSettled"/>
        /// nullazza, igy kameramegallasonkent pontosan egy szamitas fut -
        /// sikertelen meres eseten is (nincs masodpercenkenti ujraprobalkozas).
        /// </summary>
        private bool _scaleBarComputedForCurrentView;

        /// <summary>
        /// Igaz, ha a nezet (kamera + bolygo-transzform + viewport) legalabb
        /// <see cref="scaleBarSettleSeconds"/> ideig valtozatlan. Csak
        /// matrix-osszehasonlitas, nincs benne felszin-kiertekeles - a draga
        /// resz (RecomputePhysicalScaleBar) igy egyaltalan nem fut forgatas/
        /// zoom kozben, amikor az eredmenyet amugy is eldobnank.
        /// </summary>
        /// <summary>
        /// Kell-e MOST ujraszamolni. Harom eset engedi:
        ///  - a nezet megnyugodott es ehhez a nyugalmi allapothoz meg nem
        ///    szamoltunk (a tipikus ut: a felhasznalo elengedi az egeret),
        ///  - nincs ervenyes ertek (a csik "—"-t mutat) es a nezet nyugodt
        ///    (pl. az elozo meres sikertelen volt, erdemes ujraprobalni),
        ///  - GARANCIA: <see cref="scaleBarMaxWaitSeconds"/> ideje nem futott
        ///    szamitas es nincs ervenyes ertek - igy a csik akkor sem tunhet
        ///    el, ha a nezet SOHA nem nyugszik meg (tengelyforgas-mod, vagy a
        ///    felszinkoveto distance-korrekcio apro, folyamatos mozgasa).
        /// </summary>
        private bool ShouldRecomputeScaleBarNow()
        {
            // AMIG A FELHASZNALO TENYLEGESEN INTERAKTAL (lenyomott bal gomb
            // vagy gorgetes), SEMMIKEPP nem szamolunk - ez az az interakcio,
            // amit a 30-180ms-os szamitas lathatoan szaggatott. A garancia-ag
            // is ki van zarva itt, kulonben hosszu huzas kozben
            // masodpercenkent visszajonne egy hitch.
            if (Input.GetMouseButton(0)
                || Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
            {
                return false;
            }

            bool settled = IsScaleBarViewSettled();
            if (settled && (!_scaleBarComputedForCurrentView || !_scaleBarValid)) return true;

            // GARANCIA: ha a nezet sosem nyugszik meg (tengelyforgas-mod,
            // FlyTo-animacio, felszinkoveto korrekcio), akkor is legyen
            // ertek - de csak akkor, ha epp NINCS ervenyes szam kirajzolva.
            return !_scaleBarValid
                && Time.unscaledTime - _lastScaleBarComputeTime
                   >= Mathf.Max(0.2f, scaleBarMaxWaitSeconds);
        }

        /// <summary>
        /// Igaz, ha a nezet legalabb <see cref="scaleBarSettleSeconds"/> ideje
        /// nem valtozott ERDEMBEN. TOLERANCIAVAL hasonlit, nem bitpontosan:
        /// az elso valtozat matrix-egyenloseget vizsgalt, es ezert SOHA nem
        /// nyilt ki (a felhasznaloi visszajelzes: "a leptekcsik eltunt"). Ok:
        /// a PlanetOrbitCamera.Update() MINDEN kepkockaban lefuttatja a
        /// RefreshLocalSurface + ConstrainSurfaceDistance part, ami a terep
        /// alapjan aprot allit a `distance`-en, tehat a kameramatrix ket
        /// kepkocka kozott sosem bitazonos. A kerekitett feliratnak
        /// ("10 km") ez a nagysagrend amugy is erdektelen.
        /// </summary>
        private bool IsScaleBarViewSettled()
        {
            if (_scaleBarCamera == null) _scaleBarCamera = GetComponent<Camera>();
            if (_scaleBarCamera == null || target == null) return false;

            float fov = _scaleBarCamera.fieldOfView;
            Matrix4x4 targetMatrix = target.localToWorldMatrix;
            Rect viewport = _scaleBarCamera.pixelRect;
            int screenHeight = Screen.height;

            // UGYANAZ a tures-osszehasonlitas, mint az ervenyesseg-
            // ellenorzesnel (IsScaleBarContextCurrent) - szandekosan a KOZOS
            // segedfuggvennyel, hogy a ketto ne tudjon elcsuszni egymastol.
            bool unchanged = _hasScaleMotionSnapshot
                && IsSameScaleBarView(_scaleMotionYaw, _scaleMotionPitch, _scaleMotionDistance,
                    _scaleMotionFov, _scaleMotionTarget, _scaleMotionViewport, _scaleMotionScreenHeight);

            if (!unchanged)
            {
                _hasScaleMotionSnapshot = true;
                _scaleMotionYaw = _yaw;
                _scaleMotionPitch = _pitch;
                _scaleMotionDistance = distance;
                _scaleMotionFov = fov;
                _scaleMotionTarget = targetMatrix;
                _scaleMotionViewport = viewport;
                _scaleMotionScreenHeight = screenHeight;
                _scaleBarStillSince = Time.unscaledTime;
                _scaleBarComputedForCurrentView = false;
                return false;
            }

            return Time.unscaledTime - _scaleBarStillSince >= Mathf.Max(0f, scaleBarSettleSeconds);
        }

        private void CaptureScaleBarContext()
        {
            _scaleSampleTarget = target;
            _scaleSampleYaw = _yaw;
            _scaleSamplePitch = _pitch;
            _scaleSampleDistance = distance;
            _scaleSampleFov = _scaleBarCamera!.fieldOfView;
            _scaleSampleTargetMatrix = target.localToWorldMatrix;
            _scaleSampleViewport = _scaleBarCamera.pixelRect;
            _scaleSampleWorldRevision = _scaleBarPlanet!.SurfaceMeasurementRevision;
            _scaleSampleScreenHeight = Screen.height;
        }

        /// <summary>
        /// Ervenyes-e MEG a kiszamolt ertek a jelenlegi nezethez.
        ///
        /// 2026-09-18: itt is TOLERANCIAVAL hasonlitunk, ugyanazzal a
        /// segedfuggvennyel, mint a <see cref="IsScaleBarViewSettled"/> - a
        /// korabbi bitpontos matrix-egyenloseg miatt a frissen kiszamolt
        /// erteket MAR A KOVETKEZO kepkocka eldobta (a felszinkoveto
        /// distance-korrekcio minden kepkockaban aprot mozdit), igy a csik
        /// egyetlen kepkockara latszott, majd "—"-re valtott. A ket
        /// osszehasonlitas SZANDEKOSAN ugyanazt a fuggvenyt hasznalja: amikor
        /// kulon logikaval mentek, el tudtak csuszni egymastol, es a csik
        /// veglegesen befagyott "—"-en.
        ///
        /// A vilag-ujraepitesre vonatkozo ellenorzesek (revizio,
        /// IsSurfaceMeasurementCurrent) EGZAKTAK maradnak: azoknal nincs
        /// "kicsi valtozas", uj vilag = uj meres.
        /// </summary>
        private bool IsScaleBarContextCurrent()
        {
            return target != null && _scaleSampleTarget == target && _scaleBarCamera != null
                && _scaleBarPlanet != null && _scaleBarPlanet.transform == target
                && _scaleBarPlanet.IsSurfaceMeasurementCurrent
                && _scaleBarPlanet.SurfaceMeasurementRevision == _scaleSampleWorldRevision
                && IsSameScaleBarView(
                    _scaleSampleYaw, _scaleSamplePitch, _scaleSampleDistance, _scaleSampleFov,
                    _scaleSampleTargetMatrix, _scaleSampleViewport, _scaleSampleScreenHeight);
        }

        /// <summary>
        /// A kamera allapota (szog + tavolsag + fov) es a rajzolasi kontextus
        /// erdemben egyezik-e a megadott pillanatkeppel. A `distance`-nel
        /// RELATIV tures, mert a felszinkoveto korrekcio aranyos nagysagu
        /// aprosagokat mozdit; a szogeknel 0.01 fok. Ezek a nagysagrendek a
        /// KEREKITETT feliratot ("10 km") nem befolyasoljak.
        ///
        /// #8 (2026-09-21): A BOLYGO FORGATASA SZANDEKOSAN NEM SZAMIT.
        ///
        /// Korabban itt `targetMatrix.Equals(target.localToWorldMatrix)` allt,
        /// ami tengelyforgas-modban MINDEN kepkockaban hamis (a bolygo
        /// transformja folyamatosan valtozik). Emiatt a frissen kiszamolt
        /// erteket azonnal eldobtuk, es a csik VEGLEG "—"-en maradt - ez volt
        /// a felhasznaloi "ugral" visszajelzes masik fele.
        ///
        /// MIERT HELYES a forgatast kihagyni: a kiirt lepteket a KAMERA-
        /// GEOMETRIA hatarozza meg (magassag, latoszog, viewport) - a bolygo
        /// elfordulasa csak azt valtoztatja meg, MELYIK domborzat van a ket
        /// mintapont alatt. Az ebbol szarmazo elteres a domborzati relief
        /// nagysagrendje, a felirat viszont 1-2 ertekes jegyre KEREKITETT
        /// ("10 km", "43 km") - ezt a valtozas nem tudja elmozditani.
        ///
        /// AMI VISZONT SZAMIT es ellenorzott marad: a bolygo SKALAJA es
        /// POZICIOJA (ezek tenylegesen atskalaznak/athelyeznek mindent), a
        /// kamera szoge/tavolsaga/latoszoge, a viewport, es EGZAKTAN a
        /// vilag-revizio (uj vilag = uj meres).
        /// </summary>
        private bool IsSameScaleBarView(
            float yaw, float pitch, float sampleDistance, float fov,
            Matrix4x4 targetMatrix, Rect viewport, int screenHeight)
        {
            if (_scaleBarCamera == null || target == null) return false;
            float distanceEpsilon = Mathf.Max(1e-4f, Mathf.Abs(distance) * 1e-4f);

            // A pillanatkep matrixabol CSAK a skalat es a poziciot vesszuk -
            // a forgatast nem (ld. a doksit).
            Vector3 sampledPosition = targetMatrix.GetColumn(3);
            Vector3 sampledScale = targetMatrix.lossyScale;
            Vector3 currentPosition = target.position;
            Vector3 currentScale = target.lossyScale;
            float scaleEpsilon = Mathf.Max(1e-5f, currentScale.magnitude * 1e-4f);
            float positionEpsilon = Mathf.Max(1e-4f, Mathf.Abs(distance) * 1e-4f);

            return Mathf.Abs(Mathf.DeltaAngle(yaw, _yaw)) < 0.01f
                && Mathf.Abs(Mathf.DeltaAngle(pitch, _pitch)) < 0.01f
                && Mathf.Abs(sampleDistance - distance) < distanceEpsilon
                && Mathf.Abs(fov - _scaleBarCamera.fieldOfView) < 1e-3f
                && (sampledScale - currentScale).magnitude < scaleEpsilon
                && (sampledPosition - currentPosition).magnitude < positionEpsilon
                && viewport.Equals(_scaleBarCamera.pixelRect)
                && screenHeight == Screen.height;
        }

        private void InvalidateScaleBar()
        {
            _scaleBarConsecutiveFailures = ScaleBarFailureGraceCount;
            _scaleBarValid = false;
        }

        private void RegisterTransientScaleBarFailure()
        {
            if (!IsScaleBarContextCurrent())
            {
                InvalidateScaleBar();
                return;
            }
            _scaleBarConsecutiveFailures++;
            if (_scaleBarConsecutiveFailures >= ScaleBarFailureGraceCount)
                _scaleBarValid = false;
        }

        private bool TryMeasureCenteredSurfaceDistance(double pixelWidth, out double distanceMeters)
        {
            distanceMeters = 0.0;
            if (!(pixelWidth > 0.0)
                || !TrySurfaceDirectionAtScreenPoint(
                    _scaleReferenceCenterX - (float)pixelWidth * 0.5f,
                    _scaleReferenceCenterY,
                    out ScaleBarMath.Vector3d left)
                || !TrySurfaceDirectionAtScreenPoint(
                    _scaleReferenceCenterX + (float)pixelWidth * 0.5f,
                    _scaleReferenceCenterY,
                    out ScaleBarMath.Vector3d right))
            {
                return false;
            }

            distanceMeters = ScaleBarMath.GreatCircleDistanceMeters(
                left, right, PlanetConstants.RadiusMeters);
            return distanceMeters > 0.0
                && !double.IsNaN(distanceMeters) && !double.IsInfinity(distanceMeters);
        }

        private bool TrySurfaceDirectionAtScreenPoint(float screenX, float screenY, out ScaleBarMath.Vector3d direction)
        {
            direction = default(ScaleBarMath.Vector3d);
            Ray worldRay = _scaleBarCamera!.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
            Vector3 localOrigin = target.InverseTransformPoint(worldRay.origin);
            Vector3 localNext = target.InverseTransformPoint(worldRay.origin + worldRay.direction);
            Vector3 localRayDirection = localNext - localOrigin;
            var origin = new ScaleBarMath.Vector3d(localOrigin.x, localOrigin.y, localOrigin.z);
            var rayDirection = new ScaleBarMath.Vector3d(
                localRayDirection.x, localRayDirection.y, localRayDirection.z);
            return ScaleBarMath.TryIntersectRadialSurface(
                origin, rayDirection, _scaleBarPlanet!.SurfaceMeasurementBaseRadius, TryGetDisplayedRadius, out direction);
        }

        private bool TryGetDisplayedRadius(ScaleBarMath.Vector3d direction, out double displayedRadius)
        {
            return _scaleBarPlanet!.TryGetScaleSurfaceRadius(
                new Vector3((float)direction.X, (float)direction.Y, (float)direction.Z),
                out displayedRadius);
        }

        private void DrawPhysicalScaleBar()
        {
            EnsureScaleBarStyle();
            float centerX = _scaleReferenceCenterX > 0f ? _scaleReferenceCenterX : Screen.width * 0.5f;
            float viewportBottom = _scaleViewportBottomGuiY > 0f ? _scaleViewportBottomGuiY : Screen.height;
            float baselineY = viewportBottom - scaleBarBottomMarginPixels;
            string label = _scaleBarValid
                ? FormatScaleDistance(_scaleBarDistanceMeters) + "  (képközép)"
                : "Lépték: —";
            float width = _scaleBarValid ? _scaleBarPixelWidth : scaleBarTargetWidthPixels;
            float left = centerX - width * 0.5f;

            DrawScaleRect(
                new Rect(centerX - 126f, baselineY - 30f, 252f, 51f),
                new Color(0f, 0f, 0f, 0.58f));
            GUI.Label(new Rect(centerX - 120f, baselineY - 28f, 240f, 22f), label, _scaleBarLabelStyle!);
            if (_scaleBarValid)
            {
                DrawScaleRect(new Rect(left - 1f, baselineY - 1f, width + 2f, 3f), Color.black);
                DrawScaleRect(new Rect(left, baselineY, width, 1f), Color.white);
                DrawScaleRect(new Rect(left - 1f, baselineY - 7f, 3f, 15f), Color.black);
                DrawScaleRect(new Rect(left, baselineY - 6f, 1f, 13f), Color.white);
                DrawScaleRect(new Rect(left + width - 1f, baselineY - 7f, 3f, 15f), Color.black);
                DrawScaleRect(new Rect(left + width, baselineY - 6f, 1f, 13f), Color.white);

                // A perspektivikus meres konkret referenciapontja, nem globalis
                // kepernyoskala: egy visszafogott kereszt jeloli a kep kozepet.
                float markerY = Screen.height - _scaleReferenceCenterY;
                DrawScaleRect(new Rect(_scaleReferenceCenterX - 5f, markerY, 11f, 1f), new Color(1f, 1f, 1f, 0.65f));
                DrawScaleRect(new Rect(_scaleReferenceCenterX, markerY - 5f, 1f, 11f), new Color(1f, 1f, 1f, 0.65f));
            }
        }

        private void EnsureScaleBarStyle()
        {
            if (_scaleBarLabelStyle != null)
                return;

            _scaleBarLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }

        private static void DrawScaleRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static string FormatScaleDistance(double meters)
        {
            if (meters < 1000.0)
                return meters >= 10.0 ? meters.ToString("0") + " m" : meters.ToString("0.#") + " m";

            double kilometers = meters / 1000.0;
            return kilometers >= 10.0
                ? kilometers.ToString("0") + " km"
                : kilometers.ToString("0.#") + " km";
        }
    }
}
