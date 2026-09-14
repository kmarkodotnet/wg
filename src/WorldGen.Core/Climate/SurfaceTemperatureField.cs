using System;
using System.Threading.Tasks;
using WorldGen.Core.Astronomy;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Climate
{
    /// <summary>A hőmező egy kész állapota egy egész ticken.</summary>
    public sealed class ThermalSnapshot
    {
        public long Tick { get; internal set; }

        /// <summary>
        /// A kanonikus spin-up kezdőtickje, amelyből ez az állapot léptetéssel
        /// született; <c>null</c>, ha az állapot nem kanonikus úton készült
        /// (pl. vizsgálati nullázás).
        /// </summary>
        public long? CanonicalStartTick { get; internal set; }

        /// <summary>Felszíni anomália θs (K), cellánként.</summary>
        public double[] ThetaS { get; }

        /// <summary>Levegőanomália θa (K), cellánként.</summary>
        public double[] ThetaA { get; }

        public ThermalSnapshot(int cellCount)
        {
            ThetaS = new double[cellCount];
            ThetaA = new double[cellCount];
        }

        /// <summary>Nullázott, nem kanonikus anomália a megadott ticken (vizsgálati kezdőállapot).</summary>
        public void Reset(long tick)
        {
            Tick = tick;
            CanonicalStartTick = null;
            Array.Clear(ThetaS, 0, ThetaS.Length);
            Array.Clear(ThetaA, 0, ThetaA.Length);
        }

        public void CopyFrom(ThermalSnapshot other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other.ThetaS.Length != ThetaS.Length) throw new ArgumentException("Eltérő cellaszám.");
            Tick = other.Tick;
            CanonicalStartTick = other.CanonicalStartTick;
            Array.Copy(other.ThetaS, ThetaS, ThetaS.Length);
            Array.Copy(other.ThetaA, ThetaA, ThetaA.Length);
        }
    }

    /// <summary>Egy cella hőmérsékletének komponensbontása a panelhez (ND-104).</summary>
    public struct ThermalCellDiagnostics
    {
        public SurfaceThermalKind Kind;
        public double BaselineK;
        public double SurfaceK;
        public double AirK;
        public double ThetaS;
        public double ThetaA;
        public double CosZenith;
        public double DailyFactor;
        public double AbsorbedSolarWm2;
        public double SolarAnomalyWm2;
        public double SurfaceAirExchangeWm2;
        public double AdvectionWm2;
        public double MixingWm2;
        public double WindSpeedMs;
    }

    /// <summary>
    /// Pillanatnyi felszín- és levegőhőmérséklet a teljes rácsszinten
    /// (ND-100–103; referencia: tools/reference/thermal_field_ref.py).
    ///
    /// Tickenként (t → t + dt, tm = t + dt/2):
    /// <list type="number">
    /// <item>θa kompenzált upwind advekciója az élközépponti széllel, a
    /// beáramlási Courant-számból determinisztikusan választott részlépésekkel;</item>
    /// <item>lokális tagok Crank–Nicolson-IMEX-szel, a forcing, a bázis és a
    /// szél tm-ben; cellánként független, ezért párhuzamosan is bitazonos.</item>
    /// </list>
    /// A kanonikus állapot: <see cref="SimulationTime.CanonicalStartTick"/>-ben
    /// θ = 0, onnan léptetve. Egy kanonikus snapshot csak akkor folytatható,
    /// ha ugyanabból a kezdőtickből indult, mint amit a cél megkövetel.
    ///
    /// Diagnosztikai modell (ND-103): nem írja át a biome-ot, jeget, csapadékot
    /// vagy a world state hash-t. Nem szálbiztos; egy példányt egy szál használ.
    /// </summary>
    public sealed class SurfaceTemperatureField
    {
        public const double MaxInflowCourant = 0.5;

        private readonly DenseGridMetrics _grid;
        private readonly SurfaceThermalKind[] _kinds;
        private readonly double[] _elevationM;
        private readonly double _seaLevelM;
        private readonly ThermalOrbit _orbit;
        private readonly ThermalModelParameters _parameters;
        private readonly ThermalBaseline _baseline;
        private readonly ThermalWind _wind;

        private readonly double[] _edgeVelocity, _cellSpeed, _factor, _baseK;
        private readonly double[] _inflow, _divergence, _flux, _advectScratch;
        private readonly double[] _cellAlbedoTerm, _cellEmissivity, _cellHeatCapacity;

        public SurfaceTemperatureField(DenseGridMetrics grid, SurfaceThermalKind[] kinds, double[] elevationM,
            double seaLevelM, ulong worldSeed, double tYears, ThermalOrbit orbit,
            ThermalModelParameters? parameters = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _kinds = (SurfaceThermalKind[])(kinds ?? throw new ArgumentNullException(nameof(kinds))).Clone();
            _elevationM = (double[])(elevationM ?? throw new ArgumentNullException(nameof(elevationM))).Clone();
            if (_kinds.Length != grid.CellCount || _elevationM.Length != grid.CellCount)
                throw new ArgumentException("A felszíntípus- és elevációtömb mérete a cellaszámmal egyezzen.");
            for (int c = 0; c < _kinds.Length; c++)
            {
                if ((int)_kinds[c] > (int)SurfaceThermalKind.Ice)
                    throw new ArgumentException($"Ismeretlen felszíntípus a(z) {c}. cellán.", nameof(kinds));
                if (double.IsNaN(_elevationM[c]) || double.IsInfinity(_elevationM[c]))
                    throw new ArgumentException($"Nem véges eleváció a(z) {c}. cellán.", nameof(elevationM));
            }

            _seaLevelM = seaLevelM;
            _orbit = orbit;
            _parameters = parameters ?? ThermalModelParameters.Default;
            _baseline = new ThermalBaseline(grid, _kinds, _elevationM, seaLevelM, worldSeed, tYears, orbit, _parameters);
            _wind = new ThermalWind(grid, _kinds, _elevationM, seaLevelM, orbit, _parameters);

            int count = grid.CellCount;
            _edgeVelocity = new double[grid.EdgeCount];
            _cellSpeed = new double[count];
            _factor = new double[count];
            _baseK = new double[count];
            _inflow = new double[count];
            _divergence = new double[count];
            _flux = new double[count];
            _advectScratch = new double[count];
            _cellAlbedoTerm = new double[count];
            _cellEmissivity = new double[count];
            _cellHeatCapacity = new double[count];
            for (int c = 0; c < count; c++)
            {
                _cellAlbedoTerm[c] = _parameters.SolarConstant * (1.0 - _parameters.Albedo(_kinds[c]));
                _cellEmissivity[c] = _parameters.Emissivity(_kinds[c]);
                _cellHeatCapacity[c] = _parameters.SurfaceHeatCapacity(_kinds[c]);
            }
        }

        public DenseGridMetrics Grid => _grid;
        public ThermalBaseline Baseline => _baseline;
        public ThermalWind Wind => _wind;
        public ThermalModelParameters Parameters => _parameters;
        public ThermalOrbit Orbit => _orbit;
        public double SeaLevelM => _seaLevelM;
        public SurfaceThermalKind KindAt(int cell) => _kinds[cell];
        public double ElevationAt(int cell) => _elevationM[cell];

        /// <summary>Az utolsó lépés advekciós részlépésszáma.</summary>
        public int LastSubsteps { get; private set; }

        public bool UseParallelLocalStep { get; set; }

        /// <summary>Egy tick: <paramref name="state"/> helyben <c>Tick + 1</c>-re lép.</summary>
        public void Step(ThermalSnapshot state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.ThetaS.Length != _grid.CellCount) throw new ArgumentException("Eltérő cellaszám.", nameof(state));

            long tick = state.Tick;
            double dt = SimulationTime.TickSeconds;
            long tm = tick * SimulationTime.TickSeconds + SimulationTime.TickSeconds / 2;

            _wind.Sample(tm, _edgeVelocity, _cellSpeed);
            LastSubsteps = AdvectCompensatedUpwind(_grid, _edgeVelocity, state.ThetaA, dt,
                _inflow, _divergence, _flux, _advectScratch);

            _baseline.Sample(tm, _factor, _baseK);
            OrbitalMechanics.SunDirectionBodyFrame(tm / (double)SimulationTime.SecondsPerDay,
                _orbit.OrbitalPeriodDays, _orbit.RotationPeriodDays, _orbit.AxialTiltRad, 0.0, 0.0,
                out double sx, out double sy, out double sz);

            double[] thetaS = state.ThetaS, thetaA = state.ThetaA;
            if (UseParallelLocalStep)
                Parallel.For(0, _grid.CellCount, c => LocalStep(c, sx, sy, sz, dt, thetaS, thetaA));
            else
                for (int c = 0; c < _grid.CellCount; c++)
                    LocalStep(c, sx, sy, sz, dt, thetaS, thetaA);

            state.Tick = tick + 1;
        }

        private void LocalStep(int c, double sx, double sy, double sz, double dt, double[] thetaS, double[] thetaA)
        {
            double cosz = Math.Max(0.0, _grid.CenterX[c] * sx + _grid.CenterY[c] * sy + _grid.CenterZ[c] * sz);
            double q = _cellAlbedoTerm[c] * (cosz - _factor[c]);
            double bs = _baseK[c];
            double ls = 4.0 * _cellEmissivity[c] * Temperature.Sigma * bs * bs * bs;
            double speed = _cellSpeed[c];
            double uEff = speed > _parameters.MinExchangeWindMs ? speed : _parameters.MinExchangeWindMs;
            double k = _parameters.ExchangePerMetrePerSecond * uEff;
            double cs = _cellHeatCapacity[c];
            double ca = _parameters.AirHeatCapacity;
            double la = _parameters.AirRelaxation;
            double ts = thetaS[c], ta = thetaA[c];

            double rs = cs / dt * ts + q - 0.5 * (ls * ts + k * (ts - ta));
            double ra = ca / dt * ta + 0.5 * (k * (ts - ta) - la * ta);
            double a11 = cs / dt + 0.5 * (ls + k);
            double a12 = -0.5 * k;
            double a21 = -0.5 * k;
            double a22 = ca / dt + 0.5 * (k + la);
            double det = a11 * a22 - a12 * a21;
            thetaS[c] = (rs * a22 - a12 * ra) / det;
            thetaA[c] = (a11 * ra - a21 * rs) / det;
        }

        /// <summary>
        /// Kompenzált upwind fluxusforma (ND-102): <c>θ' = θ − dts/A·(Σ±F − θ·Σ±u·L)</c>.
        /// A részlépésszám a legnagyobb beáramlási Courant-számból
        /// (<c>dt·Σ_be |u|·L / A</c>) úgy, hogy részlépésenként ≤ 0,5 legyen.
        /// A <paramref name="theta"/> helyben frissül; a visszatérési érték a részlépésszám.
        /// </summary>
        public static int AdvectCompensatedUpwind(DenseGridMetrics grid, double[] edgeVelocity, double[] theta, double dt,
            double[] inflow, double[] divergence, double[] flux, double[] scratch)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            int cells = grid.CellCount, edges = grid.EdgeCount;
            if (edgeVelocity == null || edgeVelocity.Length != edges) throw new ArgumentException("Élsebesség-tömb mérete.");
            if (theta == null || theta.Length != cells) throw new ArgumentException("Állapottömb mérete.");
            if (inflow == null || divergence == null || flux == null || scratch == null
                || inflow.Length != cells || divergence.Length != cells || flux.Length != cells || scratch.Length != cells)
                throw new ArgumentException("Munkatömbök mérete.");

            Array.Clear(inflow, 0, cells);
            Array.Clear(divergence, 0, cells);
            for (int e = 0; e < edges; e++)
            {
                double ul = edgeVelocity[e] * grid.EdgeLength[e];
                int i = grid.EdgeI[e], j = grid.EdgeJ[e];
                divergence[i] += ul;
                divergence[j] -= ul;
                if (ul > 0.0) inflow[j] += ul;
                else inflow[i] -= ul;
            }

            double courant = 0.0;
            for (int c = 0; c < cells; c++)
            {
                double v = inflow[c] * dt / grid.Area[c];
                if (v > courant) courant = v;
            }
            if (double.IsNaN(courant) || double.IsInfinity(courant))
                throw new ArgumentException("Nem véges élsebesség.", nameof(edgeVelocity));

            int substeps = courant <= MaxInflowCourant ? 1 : (int)Math.Ceiling(courant / MaxInflowCourant);
            double dts = dt / substeps;
            for (int s = 0; s < substeps; s++)
            {
                Array.Clear(flux, 0, cells);
                for (int e = 0; e < edges; e++)
                {
                    int i = grid.EdgeI[e], j = grid.EdgeJ[e];
                    double ul = edgeVelocity[e] * grid.EdgeLength[e];
                    double f = ul * (ul > 0.0 ? theta[i] : theta[j]);
                    flux[i] += f;
                    flux[j] -= f;
                }
                for (int c = 0; c < cells; c++)
                    scratch[c] = theta[c] - dts / grid.Area[c] * (flux[c] - theta[c] * divergence[c]);
                Array.Copy(scratch, theta, cells);
            }
            return substeps;
        }

        /// <summary><paramref name="state"/> léptetése a <paramref name="targetTick"/>-ig (előre).</summary>
        public void RunTo(ThermalSnapshot state, long targetTick)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (targetTick < state.Tick) throw new ArgumentOutOfRangeException(nameof(targetTick), "Csak előre léptethető.");
            while (state.Tick < targetTick)
                Step(state);
        }

        /// <summary>Kanonikus kezdőállapot (θ = 0) a cél bucketjének spin-up kezdetén.</summary>
        public void ResetCanonical(ThermalSnapshot state, long targetTick)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            long start = SimulationTime.CanonicalStartTick(targetTick);
            state.Reset(start);
            state.CanonicalStartTick = start;
        }

        /// <summary>
        /// Igaz, ha <paramref name="state"/> a <paramref name="targetTick"/>
        /// kanonikus útjának része: ugyanabból a kanonikus kezdőtickből indult,
        /// és nem későbbi a célnál.
        /// </summary>
        public static bool CanContinue(ThermalSnapshot? state, long targetTick)
        {
            if (state == null || state.CanonicalStartTick == null)
                return false;
            return state.CanonicalStartTick.Value == SimulationTime.CanonicalStartTick(targetTick)
                && state.Tick <= targetTick;
        }

        /// <summary>Kanonikus állapot a <paramref name="targetTick"/>-en; ha lehet, a meglévő állapotból folytatva.</summary>
        public void StateAt(ThermalSnapshot state, long targetTick)
        {
            if (!CanContinue(state, targetTick))
                ResetCanonical(state, targetTick);
            RunTo(state, targetTick);
        }

        /// <summary>Bázis (K) cellánként egy egész tick időpontjában.</summary>
        public void BaselineAt(long tick, double[] baseK)
        {
            if (baseK == null || baseK.Length != _grid.CellCount) throw new ArgumentException("Kimeneti tömb mérete.");
            _baseline.Sample(tick * SimulationTime.TickSeconds, _factor, baseK);
        }

        /// <summary>
        /// Egy cella komponensbontása a <paramref name="state"/> időpontjában.
        /// Az advekciós tag a jelenlegi θa és szél kompenzált upwind tendenciája
        /// (W m⁻²); explicit keveredés a modellben nincs, értéke 0.
        /// </summary>
        public ThermalCellDiagnostics Diagnose(ThermalSnapshot state, int cell)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (cell < 0 || cell >= _grid.CellCount) throw new ArgumentOutOfRangeException(nameof(cell));

            long seconds = state.Tick * SimulationTime.TickSeconds;
            _baseline.Sample(seconds, _factor, _baseK);
            _wind.Sample(seconds, _edgeVelocity, _cellSpeed);
            OrbitalMechanics.SunDirectionBodyFrame(seconds / (double)SimulationTime.SecondsPerDay,
                _orbit.OrbitalPeriodDays, _orbit.RotationPeriodDays, _orbit.AxialTiltRad, 0.0, 0.0,
                out double sx, out double sy, out double sz);

            double cosz = Math.Max(0.0, _grid.CenterX[cell] * sx + _grid.CenterY[cell] * sy + _grid.CenterZ[cell] * sz);
            double speed = _cellSpeed[cell];
            double uEff = speed > _parameters.MinExchangeWindMs ? speed : _parameters.MinExchangeWindMs;
            double ts = state.ThetaS[cell], ta = state.ThetaA[cell];

            double advection = 0.0;
            for (int e = 0; e < _grid.EdgeCount; e++)
            {
                int i = _grid.EdgeI[e], j = _grid.EdgeJ[e];
                if (i != cell && j != cell) continue;
                double ul = _edgeVelocity[e] * _grid.EdgeLength[e];
                double f = ul * (ul > 0.0 ? state.ThetaA[i] : state.ThetaA[j]);
                double sign = i == cell ? 1.0 : -1.0;
                advection -= sign * (f - ta * ul);
            }
            advection = advection / _grid.Area[cell] * _parameters.AirHeatCapacity;

            return new ThermalCellDiagnostics
            {
                Kind = _kinds[cell],
                BaselineK = _baseK[cell],
                SurfaceK = _baseK[cell] + ts,
                AirK = _baseK[cell] + ta,
                ThetaS = ts,
                ThetaA = ta,
                CosZenith = cosz,
                DailyFactor = _factor[cell],
                AbsorbedSolarWm2 = _cellAlbedoTerm[cell] * cosz,
                SolarAnomalyWm2 = _cellAlbedoTerm[cell] * (cosz - _factor[cell]),
                SurfaceAirExchangeWm2 = _parameters.ExchangePerMetrePerSecond * uEff * (ts - ta),
                AdvectionWm2 = advection,
                MixingWm2 = 0.0,
                WindSpeedMs = speed,
            };
        }

        /// <summary>
        /// Magas render-LOD-on egyszer alkalmazott magasságkorrekció (ND-100, 18. pont):
        /// a cella saját magassága a bázisban már szerepel, csak a különbség kerül le.
        /// </summary>
        public static double AltitudeCorrectedK(double cellTemperatureK, double cellElevationM, double pointElevationM, double seaLevelM)
        {
            double cellHeight = Math.Max(0.0, cellElevationM - seaLevelM);
            double pointHeight = Math.Max(0.0, pointElevationM - seaLevelM);
            return cellTemperatureK - Temperature.LapseRateKPerM * (pointHeight - cellHeight);
        }
    }
}
