using System;
using WorldGen.Core.Grid;
using WorldGen.Core.Numerics;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// Determinisztikus szélsnapshot a pillanatnyi hőmodell advekciójához
    /// (ND-102; referencia: tools/reference/thermal_field_ref.py, <c>WindField</c>).
    /// A <see cref="WindPrecipitation.WindVector"/> zonális és termikus
    /// komponensének átírása nyers transzcendens függvények nélkül:
    ///
    /// <list type="bullet">
    /// <item>Zonális sáv: a meglévő háromsávos index minden sávban
    /// <c>−sin(6·|lat|)</c>; ez <c>z = sin(lat)</c> és <c>c = sqrt(1 − z²)</c>
    /// polinomja: <c>−2·(3|z| − 4|z|³)·(4c³ − 3c)</c>.</item>
    /// <item>Kelet = normalize(−y, x, 0), észak = p × kelet (a póluson a
    /// meglévő konvenció szerint kelet = (0, 1, 0)).</item>
    /// <item>Termikus komponens: a <c>T_rad(f_eff) + 33 − T_alt</c>
    /// pont-hőmérséklet véges differenciája (a simított faktor miatt a sarki
    /// éjszaka határán is véges; a nyers napi faktorral ~5700 m/s-os kiugrás
    /// adódott), 30°-os Coriolis-forgatás <see cref="DeterministicMath.SinCos"/>-szal.</item>
    /// <item>Orográfiai eltérítés nincs (első modellverzió).</item>
    /// </list>
    ///
    /// Napi snapshot a <c>k − 0,5</c> napos ablakkal, két snapshot között
    /// lineáris interpoláció. Élsebesség az élközépponti szél normálkomponense
    /// (az él alacsonyabb indexű cellájának óceán/eleváció adatával), a cella
    /// szélsebessége a cellaközépben. Nem szálbiztos.
    /// </summary>
    public sealed class ThermalWind
    {
        private readonly DenseGridMetrics _grid;
        private readonly SurfaceThermalKind[] _kinds;
        private readonly double[] _elevationM;
        private readonly double _seaLevelM;
        private readonly ThermalOrbit _orbit;
        private readonly ThermalModelParameters _parameters;
        private readonly double _coriolisSin, _coriolisCos;

        // Pontonként (élközép, majd cellaközép): kelet, észak, a négy
        // eltolt pont és azok éves faktora. Világon belül állandó.
        private readonly double[] _edgeGeometry, _cellGeometry;
        private const int GeometryStride = 3 + 3 + 3 + 12 + 4;

        private long _dayA = long.MinValue, _dayB = long.MinValue;
        private readonly double[] _edgeA, _speedA, _edgeB, _speedB;

        public ThermalWind(DenseGridMetrics grid, SurfaceThermalKind[] kinds, double[] elevationM,
            double seaLevelM, ThermalOrbit orbit, ThermalModelParameters? parameters = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
            _elevationM = elevationM ?? throw new ArgumentNullException(nameof(elevationM));
            if (kinds.Length != grid.CellCount || elevationM.Length != grid.CellCount)
                throw new ArgumentException("A felszíntípus- és elevációtömb mérete a cellaszámmal egyezzen.");
            _seaLevelM = seaLevelM;
            _orbit = orbit;
            _parameters = parameters ?? ThermalModelParameters.Default;
            DeterministicMath.SinCos(WindPrecipitation.CoriolisDeflectionDeg * Math.PI / 180.0, out _coriolisSin, out _coriolisCos);
            _edgeA = new double[grid.EdgeCount];
            _edgeB = new double[grid.EdgeCount];
            _speedA = new double[grid.CellCount];
            _speedB = new double[grid.CellCount];

            DailyInsolationSampleDirections[] windows = ThermalBaseline.CreateAnnualWindows(orbit);
            _edgeGeometry = new double[grid.EdgeCount * GeometryStride];
            for (int e = 0; e < grid.EdgeCount; e++)
                FillGeometry(_edgeGeometry, e * GeometryStride, grid.EdgeMidX[e], grid.EdgeMidY[e], grid.EdgeMidZ[e], windows);
            _cellGeometry = new double[grid.CellCount * GeometryStride];
            for (int c = 0; c < grid.CellCount; c++)
                FillGeometry(_cellGeometry, c * GeometryStride, grid.CenterX[c], grid.CenterY[c], grid.CenterZ[c], windows);
        }

        /// <summary>A meglévő sávindex transzcendens függvény nélkül, <c>z = sin(lat)</c>-ból.</summary>
        public static double ZonalBandIndex(double z)
        {
            double az = z >= 0.0 ? z : -z;
            double c2 = 1.0 - z * z;
            double c = c2 > 0.0 ? Math.Sqrt(c2) : 0.0;
            double sin3 = 3.0 * az - 4.0 * az * az * az;
            double cos3 = 4.0 * c * c * c - 3.0 * c;
            return -(2.0 * sin3 * cos3);
        }

        /// <summary>A kelet- és északi érintő-egységvektor transzcendens függvény nélkül.</summary>
        public static void EastNorth(double x, double y, double z,
            out double eastX, out double eastY, out double eastZ,
            out double northX, out double northY, out double northZ)
        {
            double rho2 = x * x + y * y;
            if (rho2 < 1.0e-24)
            {
                eastX = 0.0; eastY = 1.0; eastZ = 0.0;
            }
            else
            {
                double inv = 1.0 / Math.Sqrt(rho2);
                eastX = -y * inv; eastY = x * inv; eastZ = 0.0;
            }
            northX = y * eastZ - z * eastY;
            northY = z * eastX - x * eastZ;
            northZ = x * eastY - y * eastX;
        }

        private static void FillGeometry(double[] g, int o, double x, double y, double z, DailyInsolationSampleDirections[] windows)
        {
            EastNorth(x, y, z, out double ex, out double ey, out double ez, out double nx, out double ny, out double nz);
            g[o] = ex; g[o + 1] = ey; g[o + 2] = ez;
            g[o + 3] = nx; g[o + 4] = ny; g[o + 5] = nz;
            g[o + 6] = x; g[o + 7] = y; g[o + 8] = z;
            const double e = WindPrecipitation.GradientEps;
            OffsetPoint(x, y, z, ex * e, ey * e, ez * e, g, o + 9);
            OffsetPoint(x, y, z, -ex * e, -ey * e, -ez * e, g, o + 12);
            OffsetPoint(x, y, z, nx * e, ny * e, nz * e, g, o + 15);
            OffsetPoint(x, y, z, -nx * e, -ny * e, -nz * e, g, o + 18);
            for (int t = 0; t < 4; t++)
            {
                int p = o + 9 + t * 3;
                g[o + 21 + t] = ThermalBaseline.AnnualFactorAt(windows, g[p], g[p + 1], g[p + 2]);
            }
        }

        private static void OffsetPoint(double x, double y, double z, double dx, double dy, double dz, double[] g, int o)
        {
            double px = x + dx, py = y + dy, pz = z + dz;
            double len = Math.Sqrt(px * px + py * py + pz * pz);
            if (len < 1e-12) { px = 0.0; py = 0.0; pz = 0.0; }
            else { px /= len; py /= len; pz /= len; }
            g[o] = px; g[o + 1] = py; g[o + 2] = pz;
        }

        private double PointTemperature(double[] g, int t, in DailyInsolationSampleDirections samples,
            double annual, bool isOceanic, double elevationM)
        {
            double daily = samples.AverageFactor(g[t], g[t + 1], g[t + 2]);
            double albedo = isOceanic ? Temperature.AlbedoOcean : Temperature.AlbedoLand;
            double tRad = ThermalBaseline.RadiativeTemperature(_parameters.EffectiveFactor(daily, annual), albedo);
            return tRad + Temperature.DefaultGreenhouseK - Temperature.LapseRateKPerM * Math.Max(0.0, elevationM - _seaLevelM);
        }

        private void WindFromGeometry(double[] g, int o, in DailyInsolationSampleDirections samples, int owner,
            out double windEast, out double windNorth, out double wind3dX, out double wind3dY, out double wind3dZ)
        {
            bool oceanic = _kinds[owner] == SurfaceThermalKind.Ocean;
            double elevation = _elevationM[owner];
            double tEp = PointTemperature(g, o + 9, samples, g[o + 21], oceanic, elevation);
            double tEm = PointTemperature(g, o + 12, samples, g[o + 22], oceanic, elevation);
            double tNp = PointTemperature(g, o + 15, samples, g[o + 23], oceanic, elevation);
            double tNm = PointTemperature(g, o + 18, samples, g[o + 24], oceanic, elevation);

            double z = g[o + 8];
            double baseEast = WindPrecipitation.BaseWindSpeed * ZonalBandIndex(z);
            const double e = WindPrecipitation.GradientEps;
            double gradE = (tEp - tEm) / (2.0 * e);
            double gradN = (tNp - tNm) / (2.0 * e);
            double rawE = gradE * WindPrecipitation.ThermalWindCoeff;
            double rawN = gradN * WindPrecipitation.ThermalWindCoeff;

            double s = z >= 0.0 ? -_coriolisSin : _coriolisSin;
            double thermalE = rawE * _coriolisCos - rawN * s;
            double thermalN = rawE * s + rawN * _coriolisCos;

            windEast = baseEast + thermalE;
            windNorth = 0.0 + thermalN;
            wind3dX = windEast * g[o] + windNorth * g[o + 3];
            wind3dY = windEast * g[o + 1] + windNorth * g[o + 4];
            wind3dZ = windEast * g[o + 2] + windNorth * g[o + 5];
        }

        /// <summary>A <paramref name="day"/> napi snapshot: élnormál-sebesség és cella-szélsebesség.</summary>
        public void EvaluateDay(long day, double[] edgeVelocity, double[] cellSpeed)
        {
            if (edgeVelocity == null || cellSpeed == null
                || edgeVelocity.Length != _grid.EdgeCount || cellSpeed.Length != _grid.CellCount)
                throw new ArgumentException("A kimeneti tömbök mérete az él- és cellaszámmal egyezzen.");

            var samples = DailyInsolationSampleDirections.Create(
                day - 0.5, _orbit.OrbitalPeriodDays, _orbit.RotationPeriodDays, _orbit.AxialTiltRad);
            for (int e = 0; e < _grid.EdgeCount; e++)
            {
                WindFromGeometry(_edgeGeometry, e * GeometryStride, samples, _grid.EdgeI[e],
                    out _, out _, out double wx, out double wy, out double wz);
                edgeVelocity[e] = wx * _grid.EdgeNormalX[e] + wy * _grid.EdgeNormalY[e] + wz * _grid.EdgeNormalZ[e];
            }
            for (int c = 0; c < _grid.CellCount; c++)
            {
                WindFromGeometry(_cellGeometry, c * GeometryStride, samples, c,
                    out double we, out double wn, out _, out _, out _);
                cellSpeed[c] = Math.Sqrt(we * we + wn * wn);
            }
        }

        /// <summary>Élsebesség és cella-szélsebesség a két szomszédos napi snapshot között interpolálva.</summary>
        public void Sample(long seconds, double[] edgeVelocity, double[] cellSpeed)
        {
            if (edgeVelocity == null || cellSpeed == null
                || edgeVelocity.Length != _grid.EdgeCount || cellSpeed.Length != _grid.CellCount)
                throw new ArgumentException("A kimeneti tömbök mérete az él- és cellaszámmal egyezzen.");

            long day = SimulationTime.FloorDiv(seconds, SimulationTime.SecondsPerDay);
            double w = (seconds - day * SimulationTime.SecondsPerDay) / (double)SimulationTime.SecondsPerDay;
            EnsureDays(day);
            for (int e = 0; e < _grid.EdgeCount; e++)
                edgeVelocity[e] = _edgeA[e] + (_edgeB[e] - _edgeA[e]) * w;
            for (int c = 0; c < _grid.CellCount; c++)
                cellSpeed[c] = _speedA[c] + (_speedB[c] - _speedA[c]) * w;
        }

        private void EnsureDays(long day)
        {
            if (_dayA == day && _dayB == day + 1)
                return;
            if (_dayB == day)
            {
                Array.Copy(_edgeB, _edgeA, _edgeA.Length);
                Array.Copy(_speedB, _speedA, _speedA.Length);
                _dayA = day;
            }
            else
            {
                EvaluateDay(day, _edgeA, _speedA);
                _dayA = day;
            }
            EvaluateDay(day + 1, _edgeB, _speedB);
            _dayB = day + 1;
        }
    }
}
