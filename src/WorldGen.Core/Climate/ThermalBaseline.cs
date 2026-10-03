using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// A pillanatnyi hőmodell lassú klímabázisa (ND-100; referencia:
    /// tools/reference/thermal_field_ref.py, <c>Baseline</c>).
    ///
    /// A <see cref="Temperature.TemperatureKelvinFull"/> szerkezete a weather tag
    /// nélkül, de a radiatív tag simított faktort kap (M13, 2026-09-13):
    /// <c>f_eff = (1 − β)·f_napi + β·f_éves</c>. A napi faktoros változat sarki
    /// éjszakán ~29 K-t adott; a simítás a szállítás és a hőtehetetlenség
    /// proxyja. Az ND-168 a korábbi pozitív sarki K-tag helyére az éves
    /// célmező energiamegmaradó meridionális egyensúlyi korrekcióját teszi.
    /// <c>f_napi</c> óránként, középre igazított 24 mintás ablakból
    /// (<c>dayT = h/24 − 0,5</c>), <c>f_éves</c> 12 éves ablakból; az óceáni
    /// kontinentalitás-tag a simított radiatív hőmérséklet éves átlagához húz.
    /// Órán belül a napi faktor és a bázis lineárisan interpolált.
    ///
    /// Nem szálbiztos: egy példányt egy solver-szál használ.
    /// </summary>
    public sealed class ThermalBaseline
    {
        private readonly DenseGridMetrics _grid;
        private readonly SurfaceThermalKind[] _kinds;
        private readonly double[] _elevationM;
        private readonly double _seaLevelM;
        private readonly ThermalOrbit _orbit;
        private readonly ThermalModelParameters _parameters;

        private long _hourA = long.MinValue, _hourB = long.MinValue;
        private readonly double[] _factorA, _baseA, _factorB, _baseB;

        public double GreenhouseK { get; }
        public double CycleK { get; }

        /// <summary>Az éves napi faktor cellánként (12 ablak × 24 minta).</summary>
        public double[] AnnualFactor { get; }

        /// <summary>Óceáni cellák simított radiatív hőmérsékletének éves átlaga; más cellán 0.</summary>
        public double[] AnnualMeanRadiativeK { get; }

        /// <summary>ND-168: az éves energiamegmaradó meridionális korrekció, K.</summary>
        public double[] AnnualTransportCorrectionK { get; }

        /// <summary>ND-170: a mérlegegyenlet tényleges éves célmezője, K; csak diagnosztikai olvasásra.</summary>
        public IReadOnlyList<double> AnnualTargetK { get; }
        public SeasonalEnergyBalance? Seasonal { get; }

        public ThermalBaseline(DenseGridMetrics grid, SurfaceThermalKind[] kinds, double[] elevationM,
            double seaLevelM, ulong worldSeed, double tYears, ThermalOrbit orbit,
            ThermalModelParameters? parameters = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
            _elevationM = elevationM ?? throw new ArgumentNullException(nameof(elevationM));
            if (kinds.Length != grid.CellCount || elevationM.Length != grid.CellCount)
                throw new ArgumentException("A felszíntípus- és elevációtömb mérete a cellaszámmal egyezzen.");
            _seaLevelM = seaLevelM;
            _orbit = orbit;
            _parameters = parameters ?? ThermalModelParameters.Default;

            GreenhouseK = Temperature.GreenhouseTemperature();
            CycleK = Temperature.ClimateCycleTemperatureK(worldSeed, tYears);
            if (_parameters.UseSeasonalEnergyBalance)
                CycleK += WorldGen.Core.Tectonics.DeepTimeErosionGlaciation.GlobalTempOffset(tYears / 1.0e6);

            int count = grid.CellCount;
            _factorA = new double[count];
            _baseA = new double[count];
            _factorB = new double[count];
            _baseB = new double[count];

            AnnualFactor = new double[count];
            AnnualMeanRadiativeK = new double[count];
            var annualTargetK = new double[count];
            DailyInsolationSampleDirections[] windows = CreateAnnualWindows(orbit);
            var windowFactors = new double[windows.Length];
            for (int c = 0; c < count; c++)
            {
                double total = 0.0;
                for (int j = 0; j < windows.Length; j++)
                {
                    windowFactors[j] = windows[j].AverageFactor(grid.CenterX[c], grid.CenterY[c], grid.CenterZ[c]);
                    total += windowFactors[j];
                }
                double annual = total / Temperature.OceanAnnualSamples;
                AnnualFactor[c] = annual;
                total = 0.0;
                for (int j = 0; j < windows.Length; j++)
                    total += RadiativeTemperature(_parameters.EffectiveFactor(windowFactors[j], annual),
                        _parameters.BaselineAlbedoFor(kinds[c] == SurfaceThermalKind.Ocean
                            ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land));
                double meanRadiative = total / Temperature.OceanAnnualSamples;
                if (kinds[c] == SurfaceThermalKind.Ocean)
                    AnnualMeanRadiativeK[c] = meanRadiative;
                double altitudeK = Temperature.LapseRateKPerM * Math.Max(0.0, elevationM[c] - seaLevelM);
                annualTargetK[c] = meanRadiative + GreenhouseK - altitudeK + CycleK;
            }
            if (_parameters.UseSeasonalEnergyBalance)
            {
                Seasonal = new SeasonalEnergyBalance(grid, kinds, elevationM, seaLevelM, CycleK, orbit,
                    _parameters, _parameters.SeasonalPhases);
                annualTargetK = (double[])Seasonal.AnnualTargetK.Clone();
                AnnualTransportCorrectionK = new double[count];
                for (int c = 0; c < count; c++)
                    AnnualTransportCorrectionK[c] = _parameters.MeridionalTransportScale == 0 ? 0
                        : Seasonal.AnnualMeanK[c] - annualTargetK[c];
            }
            else AnnualTransportCorrectionK = MeridionalEnergyBalance.SolveCorrection(
                grid, annualTargetK, MeridionalEnergyBalance.DiffusionWm2K * _parameters.MeridionalTransportScale);
            AnnualTargetK = Array.AsReadOnly(annualTargetK);
        }

        public DenseGridMetrics Grid => _grid;
        public ThermalOrbit Orbit => _orbit;

        internal static DailyInsolationSampleDirections[] CreateAnnualWindows(ThermalOrbit orbit)
        {
            var windows = new DailyInsolationSampleDirections[Temperature.OceanAnnualSamples];
            for (int j = 0; j < windows.Length; j++)
            {
                windows[j] = DailyInsolationSampleDirections.Create(
                    j * (orbit.OrbitalPeriodDays / Temperature.OceanAnnualSamples),
                    orbit.OrbitalPeriodDays, orbit.RotationPeriodDays, orbit.AxialTiltRad);
            }
            return windows;
        }

        internal static double AnnualFactorAt(DailyInsolationSampleDirections[] windows, double x, double y, double z)
        {
            double total = 0.0;
            for (int j = 0; j < windows.Length; j++)
                total += windows[j].AverageFactor(x, y, z);
            return total / Temperature.OceanAnnualSamples;
        }

        /// <summary>Az óránként kiértékelt napi faktor és bázis (nem interpolált).</summary>
        public void EvaluateHour(long hour, double[] dailyFactor, double[] baseK)
        {
            if (dailyFactor == null || baseK == null || dailyFactor.Length != _grid.CellCount || baseK.Length != _grid.CellCount)
                throw new ArgumentException("A kimeneti tömbök mérete a cellaszámmal egyezzen.");

            var samples = DailyInsolationSampleDirections.Create(
                hour / 24.0 - 0.5, _orbit.OrbitalPeriodDays, _orbit.RotationPeriodDays, _orbit.AxialTiltRad);
            for (int c = 0; c < _grid.CellCount; c++)
            {
                double f = samples.AverageFactor(_grid.CenterX[c], _grid.CenterY[c], _grid.CenterZ[c]);
                bool oceanic = _kinds[c] == SurfaceThermalKind.Ocean;
                double tRad = RadiativeTemperature(_parameters.EffectiveFactor(f, AnnualFactor[c]),
                    _parameters.BaselineAlbedoFor(oceanic ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land));
                double tOcean = oceanic ? Temperature.OceanBufferingStrength * (AnnualMeanRadiativeK[c] - tRad) : 0.0;
                double tAlt = Temperature.LapseRateKPerM * Math.Max(0.0, _elevationM[c] - _seaLevelM);
                dailyFactor[c] = f;
                baseK[c] = tRad + GreenhouseK + tOcean
                    + AnnualTransportCorrectionK[c] - tAlt + CycleK;
                if (Seasonal != null) baseK[c] = Seasonal.TemperatureK(c, hour / 24.0);
            }
        }

        /// <summary>
        /// Napi faktor és bázis a <paramref name="seconds"/> időpontban, a két
        /// szomszédos óra között lineárisan interpolálva.
        /// </summary>
        public void Sample(long seconds, double[] dailyFactor, double[] baseK)
        {
            if (dailyFactor == null || baseK == null || dailyFactor.Length != _grid.CellCount || baseK.Length != _grid.CellCount)
                throw new ArgumentException("A kimeneti tömbök mérete a cellaszámmal egyezzen.");

            long hour = SimulationTime.FloorDiv(seconds, SimulationTime.SecondsPerHour);
            double w = (seconds - hour * SimulationTime.SecondsPerHour) / (double)SimulationTime.SecondsPerHour;
            EnsureHours(hour);
            for (int c = 0; c < _grid.CellCount; c++)
            {
                dailyFactor[c] = _factorA[c] + (_factorB[c] - _factorA[c]) * w;
                baseK[c] = _baseA[c] + (_baseB[c] - _baseA[c]) * w;
            }
        }

        private void EnsureHours(long hour)
        {
            if (_hourA == hour && _hourB == hour + 1)
                return;
            if (_hourB == hour)
            {
                Array.Copy(_factorB, _factorA, _factorA.Length);
                Array.Copy(_baseB, _baseA, _baseA.Length);
                _hourA = hour;
            }
            else
            {
                EvaluateHour(hour, _factorA, _baseA);
                _hourA = hour;
            }
            EvaluateHour(hour + 1, _factorB, _baseB);
            _hourB = hour + 1;
        }

        internal static double RadiativeTemperature(double factor, double albedo)
        {
            double absorbed = Temperature.DefaultFPeak * factor * (1.0 - albedo);
            double raw = absorbed > 0.0 ? absorbed / Temperature.Sigma : 0.0;
            return raw > 0.0 ? Math.Sqrt(Math.Sqrt(raw)) : 0.0;
        }
    }
}
