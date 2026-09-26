using System;

namespace WorldGen.Core.Climate
{
    public sealed partial class ThermalWind
    {
        private FeedbackData? _feedback;

        /// <summary>
        /// ND-142: a tick eleji levegőanomália gradiense visszahat a szélre.
        /// A napi bázis és az anomália együtt, a korlátozás előtt szerepel.
        /// A bemenetet nem módosítja; az összes cella ugyanazt az állapotot olvassa.
        /// </summary>
        public void SampleCoupled(long seconds, double[] airAnomaly, double[] edgeVelocity, double[] cellSpeed)
        {
            if (airAnomaly == null || airAnomaly.Length != _grid.CellCount
                || edgeVelocity == null || edgeVelocity.Length != _grid.EdgeCount
                || cellSpeed == null || cellSpeed.Length != _grid.CellCount)
                throw new ArgumentException("A hő- és széltömbök mérete nem egyezik a ráccsal.");
            if (ReferenceEquals(airAnomaly, cellSpeed))
                throw new ArgumentException("A bemenet és kimenet nem lehet ugyanaz a tömb.");
            for (int c = 0; c < airAnomaly.Length; c++)
                if (double.IsNaN(airAnomaly[c]) || double.IsInfinity(airAnomaly[c]))
                    throw new ArgumentException("Nem véges levegőanomália.", nameof(airAnomaly));
            if (_feedback == null) _feedback = new FeedbackData(this);
            _feedback.Sample(seconds, airAnomaly, edgeVelocity, cellSpeed);
        }

        private sealed class FeedbackData
        {
            private readonly ThermalWind _owner;
            private readonly int[] _neighbors;
            private readonly double[] _deltaE, _deltaN, _invEE, _invEN, _invNN;
            private readonly double[] _gx, _gy, _gz;
            private readonly double[] _baseEA, _baseNA, _baseEB, _baseNB;
            private long _dayA = long.MinValue, _dayB = long.MinValue;

            public FeedbackData(ThermalWind owner)
            {
                _owner = owner;
                int cells = owner._grid.CellCount, count = cells + owner._grid.EdgeCount;
                _neighbors = new int[cells * 4];
                _deltaE = new double[cells * 4]; _deltaN = new double[cells * 4];
                _invEE = new double[cells]; _invEN = new double[cells]; _invNN = new double[cells];
                _gx = new double[cells]; _gy = new double[cells]; _gz = new double[cells];
                _baseEA = new double[count]; _baseNA = new double[count];
                _baseEB = new double[count]; _baseNB = new double[count];
                var offsets = new int[cells];
                for (int e = 0; e < owner._grid.EdgeCount; e++)
                {
                    int i = owner._grid.EdgeI[e], j = owner._grid.EdgeJ[e];
                    AddNeighbor(i, j, offsets[i]++);
                    AddNeighbor(j, i, offsets[j]++);
                }
                for (int c = 0; c < cells; c++)
                {
                    double ee = 0.0, en = 0.0, nn = 0.0;
                    for (int d = 0; d < 4; d++)
                    {
                        int k = c * 4 + d;
                        double de = _deltaE[k], dn = _deltaN[k];
                        ee += de * de; en += de * dn; nn += dn * dn;
                    }
                    double det = ee * nn - en * en;
                    if (!(det > 0.0)) throw new InvalidOperationException("Szinguláris hőrács-gradiens.");
                    _invEE[c] = nn / det; _invEN[c] = -en / det; _invNN[c] = ee / det;
                }
            }

            private void AddNeighbor(int c, int neighbor, int offset)
            {
                var grid = _owner._grid;
                double dx = grid.CenterX[neighbor] - grid.CenterX[c];
                double dy = grid.CenterY[neighbor] - grid.CenterY[c];
                double dz = grid.CenterZ[neighbor] - grid.CenterZ[c];
                double[] g = _owner._cellGeometry;
                int o = c * GeometryStride, k = c * 4 + offset;
                _neighbors[k] = neighbor;
                _deltaE[k] = dx * g[o] + dy * g[o + 1] + dz * g[o + 2];
                _deltaN[k] = dx * g[o + 3] + dy * g[o + 4] + dz * g[o + 5];
            }

            private void Gradient(double[] theta)
            {
                double[] g = _owner._cellGeometry;
                for (int c = 0; c < theta.Length; c++)
                {
                    double re = 0.0, rn = 0.0;
                    for (int d = 0; d < 4; d++)
                    {
                        int k = c * 4 + d;
                        double delta = theta[_neighbors[k]] - theta[c];
                        re += _deltaE[k] * delta; rn += _deltaN[k] * delta;
                    }
                    double ge = _invEE[c] * re + _invEN[c] * rn;
                    double gn = _invEN[c] * re + _invNN[c] * rn;
                    int o = c * GeometryStride;
                    _gx[c] = ge * g[o] + gn * g[o + 3];
                    _gy[c] = ge * g[o + 1] + gn * g[o + 4];
                    _gz[c] = ge * g[o + 2] + gn * g[o + 5];
                }
            }

            private void BaselineDay(long day, double[] east, double[] north)
            {
                var samples = DailyInsolationSampleDirections.Create(day - 0.5,
                    _owner._orbit.OrbitalPeriodDays, _owner._orbit.RotationPeriodDays, _owner._orbit.AxialTiltRad);
                int edges = _owner._grid.EdgeCount;
                for (int k = 0; k < east.Length; k++)
                {
                    bool edge = k < edges;
                    int index = edge ? k : k - edges;
                    int cell = edge ? _owner._grid.EdgeI[index] : index;
                    double[] g = edge ? _owner._edgeGeometry : _owner._cellGeometry;
                    int o = index * GeometryStride;
                    bool ocean = _owner._kinds[cell] == SurfaceThermalKind.Ocean;
                    double elevation = _owner._elevationM[cell];
                    double ep = _owner.PointTemperature(g, o + 9, samples, g[o + 21], ocean, elevation);
                    double em = _owner.PointTemperature(g, o + 12, samples, g[o + 22], ocean, elevation);
                    double np = _owner.PointTemperature(g, o + 15, samples, g[o + 23], ocean, elevation);
                    double nm = _owner.PointTemperature(g, o + 18, samples, g[o + 24], ocean, elevation);
                    east[k] = (ep - em) / (2.0 * WindPrecipitation.GradientEps);
                    north[k] = (np - nm) / (2.0 * WindPrecipitation.GradientEps);
                }
            }

            private void EnsureDays(long day)
            {
                if (_dayA == day && _dayB == day + 1) return;
                if (_dayB == day)
                {
                    Array.Copy(_baseEB, _baseEA, _baseEA.Length);
                    Array.Copy(_baseNB, _baseNA, _baseNA.Length);
                }
                else BaselineDay(day, _baseEA, _baseNA);
                _dayA = day;
                BaselineDay(day + 1, _baseEB, _baseNB);
                _dayB = day + 1;
            }

            public void Sample(long seconds, double[] theta, double[] edgeVelocity, double[] cellSpeed)
            {
                long day = SimulationTime.FloorDiv(seconds, SimulationTime.SecondsPerDay);
                double w = (seconds - day * SimulationTime.SecondsPerDay) / (double)SimulationTime.SecondsPerDay;
                EnsureDays(day);
                Gradient(theta);
                var grid = _owner._grid;
                for (int e = 0; e < grid.EdgeCount; e++)
                {
                    int i = grid.EdgeI[e], j = grid.EdgeJ[e];
                    Wind(_owner._edgeGeometry, e * GeometryStride, e, w,
                        0.5 * (_gx[i] + _gx[j]), 0.5 * (_gy[i] + _gy[j]), 0.5 * (_gz[i] + _gz[j]),
                        out _, out double wx, out double wy, out double wz);
                    edgeVelocity[e] = wx * grid.EdgeNormalX[e] + wy * grid.EdgeNormalY[e] + wz * grid.EdgeNormalZ[e];
                }
                for (int c = 0; c < grid.CellCount; c++)
                    Wind(_owner._cellGeometry, c * GeometryStride, grid.EdgeCount + c, w,
                        _gx[c], _gy[c], _gz[c], out cellSpeed[c], out _, out _, out _);
            }

            private void Wind(double[] g, int o, int k, double w, double gx, double gy, double gz,
                out double speed, out double wx, out double wy, out double wz)
            {
                // A 3D anomáliagradiens tangenciális komponense a helyi bázisban.
                double ge = _baseEA[k] + (_baseEB[k] - _baseEA[k]) * w
                    + (gx * g[o] + gy * g[o + 1] + gz * g[o + 2]) * _owner._parameters.AirFeedbackStrength;
                double gn = _baseNA[k] + (_baseNB[k] - _baseNA[k]) * w
                    + (gx * g[o + 3] + gy * g[o + 4] + gz * g[o + 5]) * _owner._parameters.AirFeedbackStrength;
                double rawE = ge * WindPrecipitation.ThermalWindCoeff;
                double rawN = gn * WindPrecipitation.ThermalWindCoeff;
                WindPrecipitation.LimitThermalWind(rawE, rawN, out rawE, out rawN);
                double z = g[o + 8], s = z >= 0.0 ? -_owner._coriolisSin : _owner._coriolisSin;
                double we = WindPrecipitation.BaseWindSpeed * ZonalBandIndex(z)
                    + (rawE * _owner._coriolisCos - rawN * s);
                double wn = 0.0 + (rawE * s + rawN * _owner._coriolisCos);
                speed = Math.Sqrt(we * we + wn * wn);
                wx = we * g[o] + wn * g[o + 3];
                wy = we * g[o + 1] + wn * g[o + 4];
                wz = we * g[o + 2] + wn * g[o + 5];
            }
        }
    }
}
