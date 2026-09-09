using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Hydrology
{
    /// <summary>
    /// M7 tavak (spec §35), jeg/ho (§36) es statikus (A1) eroziós visszahatas
    /// (§18.2) - a STATIKUS domborzat-allapotra epulve (nem a deep-time lanc,
    /// az M10/ND-44). Python referencia: tools/reference/lakes_ice_erosion_ref.py,
    /// minden modellezesi dontes docs/04-decisions.md ND-43 alatt.
    ///
    /// SORREND-FUGGOSEG: a Python `for k in field` a mezo (face,u,v) beszurasi
    /// sorrendjeben jar. A `lakeId` (komponens-cimkezes felfedezesi sorrendje)
    /// es a `depositionGain` (lebegopontos osszegzes sorrendje) ettol fugg,
    /// ezert a C# EXPLICIT (face,u,v) sorrendben iteral (FieldKeysInFaceUvOrder).
    /// </summary>
    public static class LakesIceErosion
    {
        // --- 1. Tavak (§35) ---
        public const double LakeMinDepthM = 0.5;

        // --- 2. Jeg/ho (§36) ---
        public const int NumAnnualSamples = 12;
        public const double FreezingPointK = 273.15;
        public const double PermanentIceMeanThresholdK = 258.15; // -15 C
        public const double SeasonalSnowMinThresholdK = FreezingPointK;

        // --- 3. Statikus eroziós pass (§18.2) ---
        public const double ErosionAlpha = 0.5;
        public const double ErosionBeta = 1.0;
        public const double ErosionMaxDepthM = 250.0;
        public const double MaterialFactor = 1.0;
        public const double DepositFraction = 0.3;

        public enum IceClass { None, SeasonalSnow, PermanentIce }

        public sealed class LakeInfo
        {
            public int Id;
            public List<TileId> Tiles = new List<TileId>();
            public int TileCount;
            public double SurfaceElevation, MinSurface, MaxSurface, MaxDepth, MeanDepth;
        }

        public sealed class LakeResult
        {
            public Dictionary<TileId, bool> IsLake = new Dictionary<TileId, bool>();
            public Dictionary<TileId, double> Depth = new Dictionary<TileId, double>();
            public Dictionary<TileId, int> LakeId = new Dictionary<TileId, int>();
            public List<LakeInfo> Lakes = new List<LakeInfo>();
        }

        public sealed class ErosionResult
        {
            public Dictionary<TileId, double> NewField = new Dictionary<TileId, double>();
            public Dictionary<TileId, double> Erosion = new Dictionary<TileId, double>();
            public Dictionary<TileId, double> DepositionGain = new Dictionary<TileId, double>();
        }

        /// <summary>A mezo kulcsai (face, u, v) lexikografikus sorrendben - a Python dict beszurasi sorrendjenek megfeleloen.</summary>
        private static List<TileId> FieldKeysInFaceUvOrder<T>(Dictionary<TileId, T> field)
        {
            var keys = new List<TileId>(field.Keys);
            keys.Sort((a, b) =>
            {
                if (a.Face != b.Face) return a.Face.CompareTo(b.Face);
                a.GetUV(out uint au, out uint av);
                b.GetUV(out uint bu, out uint bv);
                if (au != bu) return au.CompareTo(bu);
                return av.CompareTo(bv);
            });
            return keys;
        }

        /// <summary>To-detektalas topografiai melyedes (priority-flood feltoltott szint &gt; nyers elevacio) alapjan (§35).</summary>
        public static LakeResult IdentifyLakes(
            Dictionary<TileId, double> field, Dictionary<TileId, double> filled,
            Dictionary<TileId, bool> isOcean, double minDepth = LakeMinDepthM)
        {
            var r = new LakeResult();
            foreach (TileId k in field.Keys)
            {
                double d = filled[k] - field[k];
                if (!isOcean[k] && d > minDepth) { r.IsLake[k] = true; r.Depth[k] = d; }
                else { r.IsLake[k] = false; r.Depth[k] = 0.0; }
            }

            var visited = new HashSet<TileId>();
            foreach (TileId k in FieldKeysInFaceUvOrder(field))
            {
                if (!r.IsLake[k] || visited.Contains(k)) continue;
                var comp = new List<TileId>();
                var stack = new Stack<TileId>();
                stack.Push(k);
                visited.Add(k);
                while (stack.Count > 0)
                {
                    TileId cur = stack.Pop();
                    comp.Add(cur);
                    for (int d = 0; d < 4; d++)
                    {
                        TileId nb = TileNeighbors.Neighbor(cur, (TileDirection)d);
                        if (r.IsLake.TryGetValue(nb, out bool nbIsLake) && nbIsLake && !visited.Contains(nb))
                        {
                            visited.Add(nb);
                            stack.Push(nb);
                        }
                    }
                }

                int thisId = r.Lakes.Count;
                double minSurf = double.PositiveInfinity, maxSurf = double.NegativeInfinity, sumSurf = 0;
                double maxDepth = double.NegativeInfinity, sumDepth = 0;
                foreach (TileId t in comp)
                {
                    r.LakeId[t] = thisId;
                    double surf = filled[t];
                    if (surf < minSurf) minSurf = surf; if (surf > maxSurf) maxSurf = surf; sumSurf += surf;
                    double dp = r.Depth[t];
                    if (dp > maxDepth) maxDepth = dp; sumDepth += dp;
                }
                r.Lakes.Add(new LakeInfo
                {
                    Id = thisId, Tiles = comp, TileCount = comp.Count,
                    SurfaceElevation = sumSurf / comp.Count, MinSurface = minSurf, MaxSurface = maxSurf,
                    MaxDepth = maxDepth, MeanDepth = sumDepth / comp.Count,
                });
            }
            return r;
        }

        /// <summary>Eves (mean, min, max) napi-atlag homerseklet, suru mintavetellel a keringesi periodus alatt.</summary>
        public static void AnnualTemperatureStats(
            double x, double y, double z, double orbitalPeriod, double rotationPeriod, double axialTilt,
            bool isOceanic, double elevationM, double seaLevelM,
            out double mean, out double min, out double max,
            double orbitalPhase0 = 0.0, double rotationPhase0 = 0.0, int numSamples = NumAnnualSamples)
        {
            double total = 0.0;
            min = double.PositiveInfinity; max = double.NegativeInfinity;
            for (int i = 0; i < numSamples; i++)
            {
                double dayT = i * (orbitalPeriod / numSamples);
                double t = Temperature.TemperatureKelvin(x, y, z, dayT, orbitalPeriod, rotationPeriod, axialTilt,
                    isOceanic, elevationM, seaLevelM, orbitalPhase0, rotationPhase0);
                total += t;
                if (t < min) min = t; if (t > max) max = t;
            }
            mean = total / numSamples;
        }

        public static IceClass ClassifyIce(double meanAnnualK, double minAnnualK)
        {
            if (meanAnnualK < PermanentIceMeanThresholdK) return IceClass.PermanentIce;
            if (minAnnualK < SeasonalSnowMinThresholdK) return IceClass.SeasonalSnow;
            return IceClass.None;
        }

        public static string IceClassName(IceClass c) =>
            c == IceClass.PermanentIce ? "permanent_ice" : c == IceClass.SeasonalSnow ? "seasonal_snow" : "none";

        /// <summary>Egyetlen additiv eroziós korrekcio (§18.2): new = field - erosion + deposition. Csak szarazfoldre.</summary>
        public static ErosionResult ApplyStaticErosionPass(
            Dictionary<TileId, double> field, Dictionary<TileId, TileId?> parent,
            Dictionary<TileId, bool> isOcean, Dictionary<TileId, long> accumulation,
            double alpha = ErosionAlpha, double beta = ErosionBeta, double maxDepthM = ErosionMaxDepthM,
            double materialFactor = MaterialFactor, double depositFraction = DepositFraction)
        {
            var r = new ErosionResult();
            foreach (TileId k in field.Keys) { r.Erosion[k] = 0.0; r.DepositionGain[k] = 0.0; r.NewField[k] = field[k]; }

            List<TileId> ordered = FieldKeysInFaceUvOrder(field);
            var land = new List<TileId>();
            foreach (TileId k in ordered) if (!isOcean[k]) land.Add(k);
            if (land.Count == 0) return r;

            long maxAcc = 1;
            foreach (TileId k in land) if (accumulation[k] > maxAcc) maxAcc = accumulation[k];

            var slopeRaw = new Dictionary<TileId, double>();
            double maxSlope = 1e-9;
            foreach (TileId k in land)
            {
                TileId? p = parent[k];
                double s = p.HasValue ? Math.Abs(field[k] - field[p.Value]) : 0.0;
                slopeRaw[k] = s;
                if (s > maxSlope) maxSlope = s;
            }

            foreach (TileId k in land)
            {
                double accNorm = (double)accumulation[k] / maxAcc;
                double slopeNorm = slopeRaw[k] / maxSlope;
                r.Erosion[k] = maxDepthM * Math.Pow(accNorm, alpha) * Math.Pow(slopeNorm, beta) * materialFactor;
            }

            foreach (TileId k in land)
            {
                TileId? p = parent[k];
                if (p.HasValue && !isOcean[p.Value])
                    r.DepositionGain[p.Value] += r.Erosion[k] * depositFraction;
            }

            foreach (TileId k in land)
                r.NewField[k] = field[k] - r.Erosion[k] + r.DepositionGain[k];

            return r;
        }
    }
}
