using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;

namespace WorldGen.Core.Climate
{
    /// <summary>ND-175: explicit éves hóegyenleg és külön vízi fagyási feltétel.</summary>
    public sealed class PhysicalIceBudget
    {
        public const double ReferencePrecipitationMPerYear = 0.97;
        public const double SeaFreezingK = 271.35;
        public ReadOnlyCollection<double> PrecipitationM { get; }
        public ReadOnlyCollection<double> SnowfallM { get; }
        public ReadOnlyCollection<double> MeltPotentialM { get; }
        public ReadOnlyCollection<double> PersistenceMarginK { get; }
        public ReadOnlyCollection<LakesIceErosion.IceClass> Classes { get; }
        public double MaxSeasonalResidualWm2 { get; }

        private PhysicalIceBudget(double[] precip, double[] snow, double[] melt, double[] margin,
            LakesIceErosion.IceClass[] classes, double residual)
        {
            PrecipitationM = Array.AsReadOnly(precip); SnowfallM = Array.AsReadOnly(snow);
            MeltPotentialM = Array.AsReadOnly(melt); PersistenceMarginK = Array.AsReadOnly(margin);
            Classes = Array.AsReadOnly(classes);
            MaxSeasonalResidualWm2 = residual;
        }

        public static double[] CalibratePrecipitation(double[] area, double[] proxy,
            double referenceMPerYear = ReferencePrecipitationMPerYear)
        {
            if (area == null || proxy == null) throw new ArgumentNullException();
            if (area.Length != proxy.Length) throw new ArgumentException("Eltérő csapadékrács.");
            if (double.IsNaN(referenceMPerYear) || double.IsInfinity(referenceMPerYear) || referenceMPerYear < 0)
                throw new ArgumentOutOfRangeException(nameof(referenceMPerYear));
            double total = 0, weighted = 0;
            for (int c = 0; c < area.Length; c++)
            {
                if (!(area[c] > 0) || double.IsInfinity(area[c]) || !(proxy[c] >= 0) || double.IsInfinity(proxy[c]))
                    throw new ArgumentException($"Érvénytelen csapadék vagy cellaterület: cella={c}, proxy={proxy[c]:R}, terület={area[c]:R}.");
                total += area[c]; weighted += area[c] * proxy[c];
            }
            var result = new double[area.Length];
            if (double.IsInfinity(total) || double.IsInfinity(weighted))
                throw new OverflowException("A csapadék területi összege túlcsordult.");
            if (weighted == 0) return result;
            double scale = referenceMPerYear / (weighted / total);
            for (int c = 0; c < result.Length; c++)
            {
                result[c] = proxy[c] * scale;
                if (double.IsNaN(result[c]) || double.IsInfinity(result[c]))
                    throw new OverflowException("A kalibrált csapadék nem véges.");
            }
            return result;
        }

        public static PhysicalIceBudget Compute(DenseGridMetrics grid, SurfaceThermalKind[] iceFreeKinds,
            double[] elevation, double seaLevel, ulong seed, ThermalOrbit orbit,
            SeasonalEnergyBalance seasonal, ThermalAnnualStatistics annual)
        {
            if (grid == null || iceFreeKinds == null || elevation == null || seasonal == null || annual == null)
                throw new ArgumentNullException();
            int count = grid.CellCount, side = grid.Side;
            if (iceFreeKinds.Length != count || elevation.Length != count || annual.MeanAirK.Count != count)
                throw new ArgumentException("Eltérő hómérleg-rács.");
            if (annual.MeanWindX == null || annual.MeanWindY == null || annual.MeanWindZ == null || annual.MeanWindSpeedMs == null)
                throw new ArgumentException("A hómérleg csapadékához éves szél kell.", nameof(annual));
            var heights = new Dictionary<TileId, double>(count);
            var temperatures = new Dictionary<TileId, double>(count);
            var wind = new Dictionary<TileId, SurfaceWindSample>(count);
            var ids = new TileId[count];
            for (int face = 0; face < 6; face++) for (int u = 0; u < side; u++) for (int v = 0; v < side; v++)
            {
                int c = DenseGridMetrics.Index(face, u, v, side);
                TileId id = TileId.FromFaceLevelUV(face, grid.Level, (uint)u, (uint)v); ids[c] = id;
                heights[id] = elevation[c]; temperatures[id] = annual.MeanAirK[c];
                wind[id] = new SurfaceWindSample(annual.MeanWindX[c], annual.MeanWindY[c], annual.MeanWindZ[c], annual.MeanWindSpeedMs[c]);
            }
            var raw = MoisturePrecipitation.ComputeFromFields(heights, seaLevel, seed, grid.Level, temperatures, wind,
                orbitalPeriodDays: orbit.OrbitalPeriodDays, rotationPeriodDays: orbit.RotationPeriodDays,
                axialTiltDegrees: orbit.AxialTiltRad * 180.0 / Math.PI);
            var proxy = new double[count];
            for (int c = 0; c < count; c++)
            {
                double value = raw.Precipitation[ids[c]];
                // A proxy fluxuselosztásában m−Σmw kerekítésből ~−1e−17 maradhat.
                proxy[c] = value < 0 && value >= -1e-12 ? 0 : value;
            }
            // A 0,97 m referencia 365,25 napra szól; a mérleg egy bolygóévet integrál.
            double[] precip = CalibratePrecipitation(grid.Area, proxy,
                ReferencePrecipitationMPerYear * orbit.OrbitalPeriodDays / 365.25);
            var snow = new double[count]; var melt = new double[count]; var margin = new double[count];
            var classes = new LakesIceErosion.IceClass[count];
            var temperaturesK = new double[seasonal.PhaseCount];
            for (int c = 0; c < count; c++)
            {
                for (int j = 0; j < seasonal.PhaseCount; j++)
                    temperaturesK[j] = seasonal.TemperatureK(c, j * orbit.OrbitalPeriodDays / seasonal.PhaseCount);
                var budget = Evaluate(temperaturesK, orbit.OrbitalPeriodDays, precip[c], iceFreeKinds[c]);
                snow[c] = budget.SnowfallM; melt[c] = budget.MeltPotentialM;
                margin[c] = budget.PersistenceMarginK; classes[c] = budget.Classification;
            }
            return new PhysicalIceBudget(precip, snow, melt, margin, classes, seasonal.MaxResidualWm2);
        }

        public static IceBudgetCell Evaluate(IReadOnlyList<double> temperatureK, double periodDays,
            double precipitationM, SurfaceThermalKind iceFreeKind)
        {
            if (temperatureK == null) throw new ArgumentNullException(nameof(temperatureK));
            if (temperatureK.Count == 0 || !(periodDays > 0) || double.IsInfinity(periodDays)
                || !(precipitationM >= 0) || double.IsInfinity(precipitationM)
                || (int)iceFreeKind < 0 || (int)iceFreeKind > (int)SurfaceThermalKind.Freshwater)
                throw new ArgumentException("Érvénytelen éves jégmérleg-bemenet.");
            double min = double.PositiveInfinity, max = double.NegativeInfinity, pdd = 0, snow = 0;
            for (int j = 0; j < temperatureK.Count; j++)
            {
                double t = temperatureK[j];
                pdd += TemperatureIndexSnow.PositiveDegreeDays(t, periodDays / temperatureK.Count);
                min = Math.Min(min, t); max = Math.Max(max, t);
                snow += precipitationM / temperatureK.Count * Math.Max(0, Math.Min(1, (275.15 - t) / 2.0));
            }
            double melt = TemperatureIndexSnow.MeltPotential(pdd, TemperatureIndexSnow.ReferenceSnowMeltFactor);
            bool water = iceFreeKind != SurfaceThermalKind.Land;
            double freezing = iceFreeKind == SurfaceThermalKind.Ocean ? SeaFreezingK : 273.15;
            double margin = water ? max - freezing : (melt - snow) / (TemperatureIndexSnow.ReferenceSnowMeltFactor * periodDays);
            if (double.IsNaN(margin) || double.IsInfinity(margin))
                throw new OverflowException("A jégmérleg nem véges.");
            LakesIceErosion.IceClass classification = margin < 0 ? LakesIceErosion.IceClass.PermanentIce
                : min < freezing && (water || snow > 0) ? LakesIceErosion.IceClass.SeasonalSnow : LakesIceErosion.IceClass.None;
            return new IceBudgetCell(snow, melt, margin, classification);
        }
    }

    public readonly struct IceBudgetCell
    {
        public double SnowfallM { get; }
        public double MeltPotentialM { get; }
        public double PersistenceMarginK { get; }
        public LakesIceErosion.IceClass Classification { get; }
        internal IceBudgetCell(double snow, double melt, double margin, LakesIceErosion.IceClass classification)
        {
            SnowfallM = snow; MeltPotentialM = melt; PersistenceMarginK = margin; Classification = classification;
        }
    }
}
