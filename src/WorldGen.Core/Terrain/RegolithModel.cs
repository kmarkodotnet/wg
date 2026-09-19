using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;

namespace WorldGen.Core.Terrain
{
    /// <summary>
    /// Talaj/regolit MVP (spec §39, docs/01-architecture.md §13, ND-117). A
    /// három kimenet (Depth, Porosity, WaterRetention) KIZÁRÓLAG a CLAUDE.md
    /// táblázata szerint garantáltan bitpontos műveleteket használ
    /// (<c>+ − × /</c>, <see cref="Math.Abs(double)"/>, <see cref="Math.Min(double, double)"/>,
    /// <see cref="Math.Max(double, double)"/> — nincs Sin/Cos/Exp/Log/Pow a
    /// láncban), és nem igényel új véletlenszám-mintavételt — mindhárom
    /// kimenet tisztán a már verifikált Core-kimenetek (elevéció/lejtő,
    /// erózió, üledék, csapadék, hőmérséklet) algebrai függvénye. Python
    /// referencia: tools/reference/regolith_ref.py
    /// (<c>compute_regolith_profile</c> / <c>compute_regolith_field</c>) —
    /// ez a modul 1:1 portja, ezért ELVBEN bitpontosan (tolerancia nélkül)
    /// egyezhet vele.
    /// </summary>
    public static class RegolithModel
    {
        // --- 1. Depth ---------------------------------------------------
        public const double DepthMaxM = 2.0;
        public const double DepthDepositionGainCoeff = 1.0;
        public const double DepthErosionStripCoeff = 0.02;
        public const double DepthAbsoluteCapM = 5.0;

        // --- 2. Porosity -------------------------------------------------
        public const double PorosityBase = 0.35;
        public const double PorosityFreezeThawCoeff = 0.25;
        public const double PorosityCompactionCoeff = 0.15;
        public const double FreezeThawHalfRangeK = 15.0;

        // --- 3. Water retention -------------------------------------------
        public const double RetentionPorosityCoeff = 0.5;
        public const double RetentionDepthCoeff = 0.3;
        public const double RetentionPrecipCoeff = 0.2;
        public const double PrecipReference = 2.0;

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>
        /// A <c>RegolithProfile</c> MVP három mezőjének (Depth[m], Porosity[0..1],
        /// WaterRetention[0..1]) TISZTA függvénye. Óceáni tile-ra mindhárom
        /// kimenet 0.0 (a regolit ebben az MVP-ben szárazföldi fogalom).
        /// 1:1 port: tools/reference/regolith_ref.py: compute_regolith_profile.
        /// </summary>
        public static RegolithProfile ComputeProfile(
            bool isOcean, double slopeNorm, double erosionDepthM, double depositionGainM,
            double annualMeanTemperatureK, double precipitation,
            double depthMaxM = DepthMaxM,
            double depositionGainCoeff = DepthDepositionGainCoeff,
            double erosionStripCoeff = DepthErosionStripCoeff,
            double depthAbsoluteCapM = DepthAbsoluteCapM,
            double porosityBase = PorosityBase,
            double freezeThawCoeff = PorosityFreezeThawCoeff,
            double compactionCoeff = PorosityCompactionCoeff,
            double freezeThawHalfRangeK = FreezeThawHalfRangeK,
            double retentionPorosityCoeff = RetentionPorosityCoeff,
            double retentionDepthCoeff = RetentionDepthCoeff,
            double retentionPrecipCoeff = RetentionPrecipCoeff,
            double precipReference = PrecipReference)
        {
            if (isOcean) return new RegolithProfile(0.0, 0.0, 0.0);

            double slopeNormC = Clamp(slopeNorm, 0.0, 1.0);
            double slopeFactor = (1.0 - slopeNormC) * (1.0 - slopeNormC);
            double baseDepth = depthMaxM * slopeFactor;
            double depth = baseDepth + depositionGainCoeff * Math.Max(0.0, depositionGainM)
                - erosionStripCoeff * Math.Max(0.0, erosionDepthM);
            depth = Clamp(depth, 0.0, depthAbsoluteCapM);

            double freezeThawDelta = Math.Abs(annualMeanTemperatureK - LakesIceErosion.FreezingPointK);
            double freezeThawActivity = Math.Max(0.0, 1.0 - freezeThawDelta / freezeThawHalfRangeK);
            double depositionFraction = Math.Min(1.0, Math.Max(0.0, depositionGainM) / depthMaxM);
            double porosity = porosityBase + freezeThawCoeff * freezeThawActivity
                - compactionCoeff * depositionFraction;
            porosity = Clamp(porosity, 0.0, 1.0);

            double precipFactor = Math.Min(1.0, Math.Max(0.0, precipitation) / precipReference);
            double depthFraction = Math.Min(1.0, depth / depthMaxM);
            double waterRetention = retentionPorosityCoeff * porosity
                + retentionDepthCoeff * depthFraction
                + retentionPrecipCoeff * precipFactor;
            waterRetention = Clamp(waterRetention, 0.0, 1.0);

            return new RegolithProfile(depth, porosity, waterRetention);
        }

        /// <summary>
        /// A normalizált lejtő (elevációgradiens) minden tile-ra: |elevation[k] -
        /// elevation[parent[k]]| / max(összes szárazföldi ilyen érték, 1e-9). A
        /// <see cref="LakesIceErosion.ApplyStaticErosionPass"/>-ban MÁR meglévő
        /// <c>parent</c>/<c>field</c> bejárási mintát követi (nem épít külön
        /// szomszéd-gráf bejárást) — az eredmény minden kulcsra (kulcs, érték)
        /// pár, óceáni tile-ra 0.0. A max-vétel és a per-kulcs osztás
        /// kommutatív/asszociatív műveletek, ezért az iterációs sorrend nem
        /// befolyásolja a kimenetet (ellentétben pl. a tó-komponens
        /// felfedezéssel vagy az üledék-összegzéssel a LakesIceErosion-ban).
        /// 1:1 megfelelő: regolith_ref.py: compute_regolith_field belső
        /// slope_raw/max_slope/slope_norm számítása.
        /// </summary>
        public static Dictionary<TileId, double> ComputeSlopeNorm(
            Dictionary<TileId, double> field, Dictionary<TileId, TileId?> parent, Dictionary<TileId, bool> isOcean)
        {
            var slopeRaw = new Dictionary<TileId, double>();
            double maxSlope = 0.0;
            foreach (TileId k in field.Keys)
            {
                if (isOcean[k]) continue;
                TileId? p = parent[k];
                double s = p.HasValue ? Math.Abs(field[k] - field[p.Value]) : 0.0;
                slopeRaw[k] = s;
                if (s > maxSlope) maxSlope = s;
            }
            maxSlope = Math.Max(maxSlope, 1e-9);

            var slopeNorm = new Dictionary<TileId, double>(field.Count);
            foreach (TileId k in field.Keys)
                slopeNorm[k] = isOcean[k] ? 0.0 : slopeRaw[k] / maxSlope;
            return slopeNorm;
        }

        /// <summary>Teljes-rács kimenet: bemenet-mezők + a három RegolithProfile-mező minden tile-ra.</summary>
        public sealed class RegolithField
        {
            public Dictionary<TileId, double> Elevation = new Dictionary<TileId, double>();
            public double SeaLevel;
            public Dictionary<TileId, bool> IsOcean = new Dictionary<TileId, bool>();
            public Dictionary<TileId, double> Erosion = new Dictionary<TileId, double>();
            public Dictionary<TileId, double> DepositionGain = new Dictionary<TileId, double>();
            public Dictionary<TileId, double> Precipitation = new Dictionary<TileId, double>();
            public Dictionary<TileId, double> SlopeNorm = new Dictionary<TileId, double>();
            public Dictionary<TileId, double?> MeanTemperatureK = new Dictionary<TileId, double?>();
            public Dictionary<TileId, double> DepthMeters = new Dictionary<TileId, double>();
            public Dictionary<TileId, double> Porosity = new Dictionary<TileId, double>();
            public Dictionary<TileId, double> WaterRetention = new Dictionary<TileId, double>();
        }

        /// <summary>
        /// Teljes-rács driver: elevéció/lejtő + statikus eroziós pass + csapadék +
        /// éves hőmérséklet-statisztika -&gt; RegolithProfile MVP minden tile-ra.
        /// 1:1 megfelelő: regolith_ref.py: compute_regolith_field. Minden
        /// bemenet MÁR meglévő, verifikált Core-kimenet (SeaLevelCalibration,
        /// FlowNetwork, LakesIceErosion, MoisturePrecipitation) — ez a driver
        /// nem vezet be új alap-adatforrást, csak összeköti a meglévőket.
        /// </summary>
        public static RegolithField ComputeField(
            ulong worldSeed, int plateCount, int level,
            double orbitalPeriodDays = 365.25, double rotationPeriodDays = 1.0, double axialTiltDegrees = 23.44)
        {
            Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(worldSeed, plateCount, level);
            double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.65);
            Dictionary<TileId, bool> isOcean = FlowNetwork.ComputeOceanField(field, seaLevel);
            FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(field, isOcean);
            Dictionary<TileId, long> accumulation = FlowNetwork.FlowAccumulation(field, flood.Parent, flood.FloodOrder);
            LakesIceErosion.ErosionResult erosionResult =
                LakesIceErosion.ApplyStaticErosionPass(field, flood.Parent, isOcean, accumulation);
            MoisturePrecipitation.PrecipitationField precipField = MoisturePrecipitation.Compute(
                worldSeed, plateCount, level,
                orbitalPeriodDays: orbitalPeriodDays, rotationPeriodDays: rotationPeriodDays,
                axialTiltDegrees: axialTiltDegrees);

            double axialTilt = axialTiltDegrees * Math.PI / 180.0;
            Dictionary<TileId, double> slopeNorm = ComputeSlopeNorm(field, flood.Parent, isOcean);

            var result = new RegolithField
            {
                Elevation = field,
                SeaLevel = seaLevel,
                IsOcean = isOcean,
                Erosion = erosionResult.Erosion,
                DepositionGain = erosionResult.DepositionGain,
                Precipitation = precipField.Precipitation,
                SlopeNorm = slopeNorm,
            };

            foreach (TileId k in field.Keys)
            {
                if (isOcean[k])
                {
                    result.MeanTemperatureK[k] = null;
                    result.DepthMeters[k] = 0.0;
                    result.Porosity[k] = 0.0;
                    result.WaterRetention[k] = 0.0;
                    continue;
                }

                TileGeometry.ToPosition(k, out double x, out double y, out double z);
                LakesIceErosion.AnnualTemperatureStats(
                    x, y, z, orbitalPeriodDays, rotationPeriodDays, axialTilt,
                    isOcean[k], field[k], seaLevel, out double meanT, out _, out _);

                RegolithProfile profile = ComputeProfile(
                    false, slopeNorm[k], erosionResult.Erosion[k], erosionResult.DepositionGain[k],
                    meanT, precipField.Precipitation[k]);

                result.MeanTemperatureK[k] = meanT;
                result.DepthMeters[k] = profile.DepthMeters;
                result.Porosity[k] = profile.Porosity;
                result.WaterRetention[k] = profile.WaterRetention;
            }

            return result;
        }
    }
}
