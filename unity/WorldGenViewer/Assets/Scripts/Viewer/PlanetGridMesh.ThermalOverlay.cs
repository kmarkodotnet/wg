using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Debug = UnityEngine.Debug;

namespace WorldGen.Viewer
{
    /// <summary>
    /// Pillanatnyi hőmérséklet-overlay (ND-104).
    ///
    /// Adatút: a Build() utáni világból level-6 felszíntípus és eleváció →
    /// <see cref="SurfaceTemperatureField"/> háttérszálon, a kanonikus
    /// állapotig léptetve (<see cref="SimulationTime"/>, a <see cref="SunController"/>
    /// idejéből) → cellánkénti Ts és Ta → <see cref="ThermalOverlayPacking"/>
    /// fixpontos atlasz → két R16 textúra, legfeljebb <c>thermalSnapshotHz</c>
    /// gyakorisággal feltöltve → a <c>WorldGen/VertexColorUnlit</c> shader
    /// csak mintavételez és palettáz.
    ///
    /// A főszál csak kész, a célticket elért állapotot vesz át; félkész (spin-up
    /// közbeni) mező nem kerül képernyőre. Világ-újraépítés (Built) vagy a
    /// pálya/idő-forrás paramétereinek változása érvényteleníti a futó munkát.
    /// Diagnosztikai réteg (ND-103): nem ír vissza a világmodellbe.
    /// </summary>
    public partial class PlanetGridMesh
    {
        public enum ThermalOverlayMode
        {
            Off = 0,
            Surface = 1,
            Air = 2,
        }

        [Header("Pillanatnyi hőmérséklet (ND-104)")]
        [SerializeField]
        [Tooltip("Ki / felszíni hőmérséklet (Ts) / felszínközeli levegő (Ta, diagnosztikai).")]
        private ThermalOverlayMode thermalOverlayMode = ThermalOverlayMode.Off;

        [SerializeField]
        [Tooltip("A fix, abszolút színskála alsó végpontja °C-ban (nincs automatikus min/max).")]
        private float thermalScaleMinCelsius = -60f;

        [SerializeField]
        [Tooltip("A fix, abszolút színskála felső végpontja °C-ban.")]
        private float thermalScaleMaxCelsius = 50f;

        [SerializeField]
        [Range(1f, 30f)]
        [Tooltip("Legfeljebb ennyi textúra-feltöltés másodpercenként (ND-104: legalább 5 Hz).")]
        private float thermalSnapshotHz = 5f;

        [SerializeField]
        [Range(1, 2880)]
        [Tooltip("Egy háttérmunka legfeljebb ennyi tickkel lép előre (egy tick 900 s).")]
        private int thermalMaxTicksPerJob = 96;

        [SerializeField]
        [Tooltip("A solver akkor is halad, ha az overlay ki van kapcsolva (12. döntés), így bekapcsoláskor azonnal van kész állapot.")]
        private bool thermalSolverAlwaysOn = true;

        [SerializeField]
        [Tooltip("A kurzor alatti cella komponensbontása a Rétegek dobozban.")]
        private bool thermalShowDetails;

        [SerializeField]
        [Tooltip("Az idő forrása; üresen a jelenet első SunController-e. Ha nincs, a climateDayT áll.")]
        private SunController? thermalClockSource;

        private static readonly int ThermalSurfaceTexId = Shader.PropertyToID("_ThermalSurfaceTex");
        private static readonly int ThermalAirTexId = Shader.PropertyToID("_ThermalAirTex");
        private static readonly int ThermalModeId = Shader.PropertyToID("_ThermalMode");
        private static readonly int ThermalMinKId = Shader.PropertyToID("_ThermalMinK");
        private static readonly int ThermalMaxKId = Shader.PropertyToID("_ThermalMaxK");
        private static readonly int ThermalWorldToPlanetId = Shader.PropertyToID("_ThermalWorldToPlanet");

        private static readonly Lazy<DenseGridMetrics> ThermalGrid =
            new Lazy<DenseGridMetrics>(() => DenseGridMetrics.Build(ThermalOverlayPacking.Level), LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<int[]> ThermalTexelMap =
            new Lazy<int[]>(ThermalOverlayPacking.BuildTexelSourceMap, LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly struct ThermalWorldInputs
        {
            public readonly int Revision;
            public readonly ulong Seed;
            public readonly double SeaLevelM;
            public readonly double TYears;
            public readonly ThermalOrbit Orbit;

            public ThermalWorldInputs(int revision, ulong seed, double seaLevelM, double tYears, ThermalOrbit orbit)
            {
                Revision = revision;
                Seed = seed;
                SeaLevelM = seaLevelM;
                TYears = tYears;
                Orbit = orbit;
            }
        }

        /// <summary>A háttérmunka saját állapota; csak az éppen futó task írja.</summary>
        private sealed class ThermalWorker
        {
            public int Revision = -1;
            public SurfaceTemperatureField? Field;
            public ThermalSnapshot? State;
            public double[]? BaseK, SurfaceK, AirK;
            public int[] KindCounts = new int[4];
        }

        private sealed class ThermalJobResult
        {
            public int Revision;
            public long Tick, TargetTick;
            public bool Reached, Restarted;
            public int TicksAdvanced;
            public double InputMs, StepMs, PackMs;
            public ushort[]? SurfaceTexels, AirTexels;
            public int DiagnosticCell = -1;
            public ThermalCellDiagnostics Diagnostics;
            public double DiagnosticCellElevationM;
            public int[] KindCounts = new int[4];
        }

        private ThermalWorker? _thermalWorker;
        private Task<ThermalJobResult>? _thermalTask;
        private CancellationTokenSource? _thermalCancel;
        private int _thermalRevision;
        private bool _thermalBuiltListenerAdded;
        private ThermalOrbit _thermalCapturedOrbit;
        private bool _thermalHasCapturedOrbit;
        private Texture2D? _thermalSurfaceTexture, _thermalAirTexture;
        private ThermalJobResult? _thermalLatest;
        private ThermalJobResult? _thermalPendingUpload;
        private float _thermalLastUploadRealtime = -1000f;
        private int _thermalRequestedCell = -1;
        private double _thermalCursorElevationM = double.NaN;
        private string _thermalStatus = "hőmező: nincs adat";
        private bool _thermalFormatWarningShown, _thermalPhaseWarningShown;
        private Texture2D? _thermalLegendTexture;
        private float _thermalLegendMin = float.NaN, _thermalLegendMax = float.NaN;
        private SunController? _thermalClock;

        /// <summary>A Rétegek doboz ennyi sort foglal a hőmérséklet-blokknak.</summary>
        private int ThermalOverlayPanelRows => 2
            + (thermalOverlayMode != ThermalOverlayMode.Off ? 1 : 0)
            + (thermalShowDetails ? 9 : 0);

        private void UpdateThermalOverlay()
        {
            if (!_thermalBuiltListenerAdded)
            {
                Built.AddListener(InvalidateThermalWorld);
                _thermalBuiltListenerAdded = true;
            }

            PollThermalTask();
            UploadThermalSnapshotIfDue();
            UpdateThermalCursor();
            ApplyThermalShaderGlobals();

            bool wanted = thermalOverlayMode != ThermalOverlayMode.Off || thermalSolverAlwaysOn;
            if (!wanted || _thermalTask != null || _lastField == null)
                return;

            ThermalOrbit orbit = CurrentThermalOrbit(out double timeDays);
            if (!_thermalHasCapturedOrbit || !SameOrbit(orbit, _thermalCapturedOrbit))
            {
                if (_thermalHasCapturedOrbit)
                    InvalidateThermalWorld();
                _thermalCapturedOrbit = orbit;
                _thermalHasCapturedOrbit = true;
            }

            long target = SimulationTime.FromDaysFloor(timeDays).Tick;
            if (_thermalLatest != null && _thermalLatest.Revision == _thermalRevision && _thermalLatest.Tick == target)
                return;

            var inputs = new ThermalWorldInputs(_thermalRevision, _adaptiveSeed, _adaptiveSeaLevel, deepTimeMyr * 1.0e6, orbit);
            if (_thermalWorker == null || _thermalWorker.Revision != _thermalRevision)
                _thermalWorker = new ThermalWorker();

            _thermalCancel?.Dispose();
            _thermalCancel = new CancellationTokenSource();
            ThermalWorker worker = _thermalWorker;
            CancellationToken token = _thermalCancel.Token;
            int maxTicks = Mathf.Max(1, thermalMaxTicksPerJob);
            int requestedCell = _thermalRequestedCell;
            _thermalTask = Task.Run(() => RunThermalJob(worker, inputs, target, maxTicks, requestedCell, token), token);
        }

        private void InvalidateThermalWorld()
        {
            _thermalRevision++;
            _thermalCancel?.Cancel();
            _thermalStatus = "hőmező: új világ, újraszámítás";
        }

        private ThermalOrbit CurrentThermalOrbit(out double timeDays)
        {
            if (thermalClockSource != null)
                _thermalClock = thermalClockSource;
            else if (_thermalClock == null)
                _thermalClock = FindFirstObjectByType<SunController>();

            if (_thermalClock != null)
            {
                if (!_thermalPhaseWarningShown && (_thermalClock.OrbitalPhase0 != 0.0 || _thermalClock.RotationPhase0 != 0.0))
                {
                    Debug.LogWarning("PlanetGridMesh hőmodell: a SunController pálya-/forgásfázisa nem 0, a hőmodell 0 fázist feltételez (ND-101) - a fény és a hőtérkép eltérhet.");
                    _thermalPhaseWarningShown = true;
                }
                timeDays = _thermalClock.CurrentTimeDays;
                return new ThermalOrbit(_thermalClock.OrbitalPeriodDays, _thermalClock.RotationPeriodDays,
                    _thermalClock.AxialTiltDegrees * Math.PI / 180.0);
            }

            timeDays = climateDayT;
            return new ThermalOrbit(climateOrbitalPeriodDays, climateRotationPeriodDays, climateAxialTiltDegrees * Math.PI / 180.0);
        }

        private static bool SameOrbit(in ThermalOrbit a, in ThermalOrbit b)
            => a.OrbitalPeriodDays == b.OrbitalPeriodDays && a.RotationPeriodDays == b.RotationPeriodDays && a.AxialTiltRad == b.AxialTiltRad;

        /// <summary>Háttérszálon: bemenetek (szükség esetén), léptetés, csomagolás.</summary>
        private ThermalJobResult RunThermalJob(ThermalWorker worker, ThermalWorldInputs inputs, long target,
            int maxTicks, int requestedCell, CancellationToken token)
        {
            var result = new ThermalJobResult { Revision = inputs.Revision, TargetTick = target };
            var sw = Stopwatch.StartNew();
            DenseGridMetrics grid = ThermalGrid.Value;

            if (worker.Field == null || worker.Revision != inputs.Revision)
            {
                var kinds = new SurfaceThermalKind[grid.CellCount];
                var elevation = new double[grid.CellCount];
                var counts = new int[4];
                for (int c = 0; c < grid.CellCount; c++)
                {
                    if ((c & 1023) == 0) token.ThrowIfCancellationRequested();
                    double x = grid.CenterX[c], y = grid.CenterY[c], z = grid.CenterZ[c];
                    double h = ElevationAtDir(x, y, z);
                    elevation[c] = h;
                    TileId id = TileGeometry.FromPosition(x, y, z, ThermalOverlayPacking.Level);
                    SurfaceThermalKind kind;
                    if (h < inputs.SeaLevelM) kind = SurfaceThermalKind.Ocean;
                    else if (IsAdaptiveLakeTile(id)) kind = SurfaceThermalKind.Freshwater;
                    else if (IsAdaptiveIceTile(id)) kind = SurfaceThermalKind.Ice;
                    else kind = SurfaceThermalKind.Land;
                    kinds[c] = kind;
                    counts[(int)kind]++;
                }
                worker.Field = new SurfaceTemperatureField(grid, kinds, elevation, inputs.SeaLevelM, inputs.Seed,
                    inputs.TYears, inputs.Orbit) { UseParallelLocalStep = true };
                worker.State = new ThermalSnapshot(grid.CellCount);
                worker.BaseK = new double[grid.CellCount];
                worker.SurfaceK = new double[grid.CellCount];
                worker.AirK = new double[grid.CellCount];
                worker.KindCounts = counts;
                worker.Revision = inputs.Revision;
                result.InputMs = sw.Elapsed.TotalMilliseconds;
            }
            result.KindCounts = worker.KindCounts;

            SurfaceTemperatureField field = worker.Field;
            ThermalSnapshot state = worker.State!;
            sw.Restart();
            if (!SurfaceTemperatureField.CanContinue(state, target))
            {
                field.ResetCanonical(state, target);
                result.Restarted = true;
            }
            long stepTarget = Math.Min(target, state.Tick + maxTicks);
            while (state.Tick < stepTarget)
            {
                token.ThrowIfCancellationRequested();
                field.Step(state);
                result.TicksAdvanced++;
            }
            result.StepMs = sw.Elapsed.TotalMilliseconds;
            result.Tick = state.Tick;
            result.Reached = state.Tick == target;
            if (!result.Reached)
                return result;

            sw.Restart();
            double[] baseK = worker.BaseK!, surfaceK = worker.SurfaceK!, airK = worker.AirK!;
            field.BaselineAt(state.Tick, baseK);
            for (int c = 0; c < grid.CellCount; c++)
            {
                surfaceK[c] = baseK[c] + state.ThetaS[c];
                airK[c] = baseK[c] + state.ThetaA[c];
            }
            int[] map = ThermalTexelMap.Value;
            result.SurfaceTexels = new ushort[map.Length];
            result.AirTexels = new ushort[map.Length];
            ThermalOverlayPacking.Pack(map, surfaceK, result.SurfaceTexels);
            ThermalOverlayPacking.Pack(map, airK, result.AirTexels);
            if (requestedCell >= 0 && requestedCell < grid.CellCount)
            {
                result.DiagnosticCell = requestedCell;
                result.Diagnostics = field.Diagnose(state, requestedCell);
                result.DiagnosticCellElevationM = field.ElevationAt(requestedCell);
            }
            result.PackMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        private void PollThermalTask()
        {
            if (_thermalTask == null || !_thermalTask.IsCompleted)
                return;

            Task<ThermalJobResult> task = _thermalTask;
            _thermalTask = null;
            if (task.IsCanceled)
            {
                _thermalWorker = null;
                return;
            }
            if (task.IsFaulted)
            {
                Exception? error = task.Exception?.GetBaseException();
                if (error is OperationCanceledException)
                {
                    _thermalWorker = null;
                    return;
                }
                _thermalWorker = null;
                _thermalStatus = "hőmező: hiba, ld. Console";
                Debug.LogWarning($"PlanetGridMesh hőmodell: a háttérszámítás hibára futott: {error}");
                return;
            }

            ThermalJobResult result = task.Result;
            if (result.Revision != _thermalRevision)
            {
                _thermalWorker = null;
                return;
            }
            if (!result.Reached)
            {
                _thermalStatus = result.Restarted || result.Tick < result.TargetTick - thermalMaxTicksPerJob
                    ? $"hőmező számítása: {FormatThermalTime(result.Tick)} → {FormatThermalTime(result.TargetTick)}"
                    : _thermalStatus;
                return;
            }
            _thermalPendingUpload = result;
        }

        private void UploadThermalSnapshotIfDue()
        {
            ThermalJobResult? pending = _thermalPendingUpload;
            if (pending == null || pending.SurfaceTexels == null || pending.AirTexels == null)
                return;
            float interval = 1f / Mathf.Max(1f, thermalSnapshotHz);
            if (Time.unscaledTime - _thermalLastUploadRealtime < interval)
                return;

            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R16))
            {
                if (!_thermalFormatWarningShown)
                {
                    Debug.LogWarning("PlanetGridMesh hőmodell: az R16 textúraformátum nem támogatott, az overlay nem jeleníthető meg.");
                    _thermalFormatWarningShown = true;
                }
                _thermalStatus = "hőmező: R16 textúra nem támogatott";
                _thermalPendingUpload = null;
                return;
            }

            var sw = Stopwatch.StartNew();
            _thermalSurfaceTexture = EnsureThermalTexture(_thermalSurfaceTexture, "ThermalSurface");
            _thermalAirTexture = EnsureThermalTexture(_thermalAirTexture, "ThermalAir");
            _thermalSurfaceTexture.SetPixelData(pending.SurfaceTexels, 0);
            _thermalSurfaceTexture.Apply(false, false);
            _thermalAirTexture.SetPixelData(pending.AirTexels, 0);
            _thermalAirTexture.Apply(false, false);
            double uploadMs = sw.Elapsed.TotalMilliseconds;

            _thermalLatest = pending;
            _thermalPendingUpload = null;
            _thermalLastUploadRealtime = Time.unscaledTime;
            _thermalStatus = $"hőmező: {FormatThermalTime(pending.Tick)}";
            PerfLog(string.Format(CultureInfo.InvariantCulture,
                "[ND-104 thermal] tick={0} restarted={1} ticks={2} inputMs={3:F1} stepMs={4:F1} packMs={5:F1} uploadMs={6:F2} " +
                "mode={7} kinds=L{8}/O{9}/F{10}/I{11}",
                pending.Tick, pending.Restarted, pending.TicksAdvanced, pending.InputMs, pending.StepMs, pending.PackMs, uploadMs,
                thermalOverlayMode, pending.KindCounts[0], pending.KindCounts[1], pending.KindCounts[2], pending.KindCounts[3]));
        }

        private static Texture2D EnsureThermalTexture(Texture2D? texture, string name)
        {
            if (texture != null)
                return texture;
            return new Texture2D(ThermalOverlayPacking.AtlasWidth, ThermalOverlayPacking.AtlasHeight, TextureFormat.R16, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }

        private void ApplyThermalShaderGlobals()
        {
            bool visible = thermalOverlayMode != ThermalOverlayMode.Off
                && _thermalLatest != null
                && _thermalLatest.Revision == _thermalRevision
                && _thermalSurfaceTexture != null
                && _thermalAirTexture != null;
            Shader.SetGlobalFloat(ThermalModeId, visible ? (float)(int)thermalOverlayMode : 0f);
            if (!visible)
                return;
            Shader.SetGlobalTexture(ThermalSurfaceTexId, _thermalSurfaceTexture);
            Shader.SetGlobalTexture(ThermalAirTexId, _thermalAirTexture);
            Shader.SetGlobalFloat(ThermalMinKId, thermalScaleMinCelsius + 273.15f);
            Shader.SetGlobalFloat(ThermalMaxKId, thermalScaleMaxCelsius + 273.15f);
            Shader.SetGlobalMatrix(ThermalWorldToPlanetId, transform.worldToLocalMatrix);
        }

        private void UpdateThermalCursor()
        {
            if (!thermalShowDetails)
            {
                _thermalRequestedCell = -1;
                return;
            }
            Camera cam = GetAdaptiveCamera();
            if (cam == null)
                return;
            Vector3 mouse;
            try
            {
                mouse = Input.mousePosition;
            }
            catch (InvalidOperationException)
            {
                _thermalRequestedCell = -1;
                return;
            }

            Ray ray = cam.ScreenPointToRay(mouse);
            Vector3 center = transform.position;
            float scale = transform.lossyScale.x;
            double sphereRadius = (radius + _adaptiveSeaLevel * elevationScale) * scale;
            Vector3 oc = ray.origin - center;
            double b = Vector3.Dot(oc, ray.direction);
            double c = Vector3.Dot(oc, oc) - sphereRadius * sphereRadius;
            double disc = b * b - c;
            if (disc < 0.0)
            {
                _thermalRequestedCell = -1;
                return;
            }
            double t = -b - Math.Sqrt(disc);
            if (t < 0.0) t = -b + Math.Sqrt(disc);
            if (t < 0.0)
            {
                _thermalRequestedCell = -1;
                return;
            }
            Vector3 hit = ray.origin + ray.direction * (float)t;
            Vector3 local = transform.InverseTransformPoint(hit);
            if (local.sqrMagnitude < 1e-12f)
                return;
            BodyFrameConversion.ToCore(local.normalized, out double x, out double y, out double z);
            TileId id = TileGeometry.FromPosition(x, y, z, ThermalOverlayPacking.Level);
            id.GetUV(out uint u, out uint v);
            _thermalRequestedCell = DenseGridMetrics.Index(id.Face, (int)u, (int)v, ThermalOverlayPacking.Side);
            _thermalCursorElevationM = ElevationAtDir(x, y, z);
        }

        /// <summary>A Rétegek doboz végére rajzolt hőmérséklet-blokk (a wg-c6 által fenntartott hely).</summary>
        private void DrawThermalOverlayRows(float x, float y, float w, float rowH)
        {
            GUI.Label(new Rect(x, y + 2f, 95f, rowH), "Hőmérséklet:");
            float bx = x + 95f, bw = (w - 95f) / 3f;
            bool off = GUI.Toggle(new Rect(bx, y + 4f, bw, rowH), thermalOverlayMode == ThermalOverlayMode.Off, " Ki");
            bool surface = GUI.Toggle(new Rect(bx + bw, y + 4f, bw, rowH), thermalOverlayMode == ThermalOverlayMode.Surface, " Felszín (Ts)");
            bool air = GUI.Toggle(new Rect(bx + 2f * bw, y + 4f, bw, rowH), thermalOverlayMode == ThermalOverlayMode.Air, " Levegő (Ta)");
            if (off && thermalOverlayMode != ThermalOverlayMode.Off) thermalOverlayMode = ThermalOverlayMode.Off;
            else if (surface && thermalOverlayMode != ThermalOverlayMode.Surface) thermalOverlayMode = ThermalOverlayMode.Surface;
            else if (air && thermalOverlayMode != ThermalOverlayMode.Air) thermalOverlayMode = ThermalOverlayMode.Air;
            y += rowH;

            // A fix °C-skála csak bekapcsolt overlaynél látszik.
            if (thermalOverlayMode != ThermalOverlayMode.Off)
            {
                DrawThermalLegend(new Rect(x, y + 4f, w, rowH - 6f));
                y += rowH;
            }

            thermalShowDetails = GUI.Toggle(new Rect(x, y + 4f, 110f, rowH), thermalShowDetails, " Részletek");
            GUI.Label(new Rect(x + 115f, y + 2f, w - 115f, rowH), _thermalStatus);
            y += rowH;

            if (!thermalShowDetails)
                return;
            foreach (string line in ThermalDetailLines())
            {
                GUI.Label(new Rect(x, y, w, rowH), line);
                y += rowH;
            }
        }

        private string[] ThermalDetailLines()
        {
            var lines = new string[9];
            ThermalJobResult? latest = _thermalLatest;
            if (latest == null || latest.Revision != _thermalRevision || latest.DiagnosticCell < 0 || latest.DiagnosticCell != _thermalRequestedCell)
            {
                lines[0] = _thermalRequestedCell < 0 ? "Vidd a kurzort a bolygó fölé." : $"Cella {_thermalRequestedCell}: számítás a következő snapshotban.";
                for (int i = 1; i < lines.Length; i++) lines[i] = string.Empty;
                return lines;
            }

            ThermalCellDiagnostics d = latest.Diagnostics;
            CultureInfo ci = CultureInfo.InvariantCulture;
            double corrected = double.IsNaN(_thermalCursorElevationM)
                ? d.SurfaceK
                : SurfaceTemperatureField.AltitudeCorrectedK(d.SurfaceK, latest.DiagnosticCellElevationM, _thermalCursorElevationM, _adaptiveSeaLevel);
            lines[0] = string.Format(ci, "Cella {0} ({1}), {2}", latest.DiagnosticCell, KindLabel(d.Kind), FormatThermalTime(latest.Tick));
            lines[1] = string.Format(ci, "Ts {0:F1} °C   Ta {1:F1} °C   bázis {2:F1} °C", d.SurfaceK - 273.15, d.AirK - 273.15, d.BaselineK - 273.15);
            lines[2] = string.Format(ci, "Anomália: θs {0:+0.00;-0.00} K   θa {1:+0.00;-0.00} K", d.ThetaS, d.ThetaA);
            lines[3] = string.Format(ci, "Besugárzás: {0:F0} W/m² (napi átlagtól {1:+0;-0} W/m²)", d.AbsorbedSolarWm2, d.SolarAnomalyWm2);
            lines[4] = string.Format(ci, "Napszög: cos z = {0:F3}   napi faktor {1:F3}", d.CosZenith, d.DailyFactor);
            lines[5] = string.Format(ci, "Hőcsere felszín→levegő: {0:+0.0;-0.0} W/m²   szél {1:F1} m/s", d.SurfaceAirExchangeWm2, d.WindSpeedMs);
            lines[6] = string.Format(ci, "Advekció: {0:+0.0;-0.0} W/m²   keveredés: {1:F1} W/m²", d.AdvectionWm2, d.MixingWm2);
            lines[7] = string.Format(ci, "Magassági korrekció a kurzor pontjára: {0:+0.00;-0.00} K", corrected - d.SurfaceK);
            lines[8] = "Forrás: level-6 hőmező (ND-100–104), diagnosztikai réteg";
            return lines;
        }

        private static string KindLabel(SurfaceThermalKind kind) => kind switch
        {
            SurfaceThermalKind.Ocean => "óceán",
            SurfaceThermalKind.Freshwater => "édesvíz",
            SurfaceThermalKind.Ice => "jég",
            _ => "szárazföld",
        };

        private void DrawThermalLegend(Rect rect)
        {
            EnsureThermalLegendTexture();
            if (_thermalLegendTexture == null)
                return;
            const float labelWidth = 48f;
            var bar = new Rect(rect.x + labelWidth, rect.y, rect.width - 2f * labelWidth, rect.height);
            GUI.DrawTexture(bar, _thermalLegendTexture, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(rect.x, rect.y - 3f, labelWidth, rect.height + 6f), $"{thermalScaleMinCelsius:F0} °C");
            GUI.Label(new Rect(rect.xMax - labelWidth + 4f, rect.y - 3f, labelWidth, rect.height + 6f), $"{thermalScaleMaxCelsius:F0} °C");
            float span = thermalScaleMaxCelsius - thermalScaleMinCelsius;
            if (span > 0f && thermalScaleMinCelsius < 0f && thermalScaleMaxCelsius > 0f)
            {
                float zx = bar.x + bar.width * (-thermalScaleMinCelsius / span);
                GUI.DrawTexture(new Rect(zx - 1f, bar.y - 2f, 2f, bar.height + 4f), Texture2D.blackTexture);
                GUI.Label(new Rect(zx - 12f, bar.y + bar.height - 2f, 40f, 16f), "0 °C");
            }
        }

        private void EnsureThermalLegendTexture()
        {
            if (_thermalLegendTexture != null && _thermalLegendMin == thermalScaleMinCelsius && _thermalLegendMax == thermalScaleMaxCelsius)
                return;
            const int width = 256;
            if (_thermalLegendTexture == null)
            {
                _thermalLegendTexture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
                {
                    name = "ThermalLegend",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
            }
            double minK = thermalScaleMinCelsius + 273.15, maxK = thermalScaleMaxCelsius + 273.15;
            for (int i = 0; i < width; i++)
            {
                double k = minK + (maxK - minK) * (i + 0.5) / width;
                ThermalOverlayPacking.Palette(k, minK, maxK, out float r, out float g, out float b);
                _thermalLegendTexture.SetPixel(i, 0, new Color(r, g, b, 1f));
            }
            _thermalLegendTexture.Apply(false, false);
            _thermalLegendMin = thermalScaleMinCelsius;
            _thermalLegendMax = thermalScaleMaxCelsius;
        }

        private static string FormatThermalTime(long tick)
        {
            long seconds = tick * SimulationTime.TickSeconds;
            long day = SimulationTime.FloorDiv(seconds, SimulationTime.SecondsPerDay);
            long secondOfDay = seconds - day * SimulationTime.SecondsPerDay;
            return string.Format(CultureInfo.InvariantCulture, "nap {0}, {1:00}:{2:00}", day, secondOfDay / 3600, secondOfDay % 3600 / 60);
        }
    }
}
