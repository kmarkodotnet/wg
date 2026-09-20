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

        /// <summary>
        /// A <see cref="IdentifyLakes"/> TOMBINDEXELT valtozatanak eredmenye.
        /// A per-tile mezok TOMBOK (nem Dictionary): a suru topologia indexei
        /// szerint, ahol a `LakeId` -1 a nem-to tile-okon.
        /// </summary>
        public sealed class DenseLakeResult
        {
            public bool[] IsLake = Array.Empty<bool>();
            public double[] Depth = Array.Empty<double>();
            public int[] LakeId = Array.Empty<int>();
            public List<LakeInfo> Lakes = new List<LakeInfo>();

            /// <summary>
            /// A Dictionary-alapu <see cref="LakeResult"/>-ta konvertalja - a
            /// referencia-orakulummal valo osszehasonlitashoz, illetve azoknak a
            /// hivoknak, akik meg a regi alakot varjak. A `LakeId` szotarba -
            /// a Dictionary-valtozattal EGYEZOEN - csak a to-tile-ok kerulnek be.
            /// </summary>
            public LakeResult ToLakeResult(FlowNetwork.DenseGridTopology topology)
            {
                if (topology == null) throw new ArgumentNullException(nameof(topology));
                if (topology.Count != IsLake.Length)
                    throw new ArgumentException("A topologia es a to-eredmeny merete elter.", nameof(topology));

                var result = new LakeResult { Lakes = Lakes };
                for (int i = 0; i < IsLake.Length; i++)
                {
                    TileId tile = topology.TileAt(i);
                    result.IsLake[tile] = IsLake[i];
                    result.Depth[tile] = Depth[i];
                    if (LakeId[i] >= 0) result.LakeId[tile] = LakeId[i];
                }
                return result;
            }
        }

        /// <summary>
        /// A <see cref="IdentifyLakes"/> TOMBINDEXELT valtozata: a suru
        /// topologiat es a mar meglevo `double[]`/`bool[]` mezoket olvassa,
        /// Dictionary nelkul.
        ///
        /// MIERT BITRE AZONOS a kimenete a Dictionary-alapu uttal:
        ///  - a KULSO bejaras sorrendje azonos. A Dictionary-valtozat a
        ///    kulcsokat (face, u, v) lexikografikusan RENDEZI
        ///    (FieldKeysInFaceUvOrder); a suru index viszont definicio szerint
        ///    `face * n * n + u * n + v`, tehat a 0..Count-1 bejaras PONTOSAN
        ///    ugyanez a sorrend - a rendezes elhagyhato, nem helyettesitheto.
        ///  - a komponens-bejaras (DFS) azonos: ugyanaz a verem (LIFO), a
        ///    szomszedok ugyanabban a TileDirection-sorrendben (0..3) kerulnek
        ///    ra, es a `visited` jeloles ugyanugy a BETEVESKOR tortenik, nem a
        ///    kivetelkor. Ezert a `LakeInfo.Tiles` lista elemsorrendje is azonos.
        ///  - ebbol kovetkezoen a statisztikak osszegzesi SORRENDJE is azonos,
        ///    tehat a lebegopontos osszeadas nem-asszociativitasa sem okozhat
        ///    elterest.
        ///
        /// ELOFELTETEL: a mezok a topologia TELJES szintjere vonatkoznak
        /// (Length == topology.Count). A Dictionary-valtozat megengedne reszleges
        /// mezot (a hianyzo szomszedot kihagyja); itt minden szomszed letezik,
        /// ezert a ket ut csak TELJES mezore egyezik - a viewer mindig ilyet ad.
        /// </summary>
        public static DenseLakeResult IdentifyLakesDense(
            FlowNetwork.DenseGridTopology topology, double[] field, double[] filled, bool[] isOcean,
            double minDepth = LakeMinDepthM)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (filled == null) throw new ArgumentNullException(nameof(filled));
            if (isOcean == null) throw new ArgumentNullException(nameof(isOcean));
            int count = topology.Count;
            if (field.Length != count) throw new ArgumentException("A mezo merete nem egyezik a topologiaval.", nameof(field));
            if (filled.Length != count) throw new ArgumentException("A feltoltott mezo merete nem egyezik a topologiaval.", nameof(filled));
            if (isOcean.Length != count) throw new ArgumentException("Az ocean-maszk merete nem egyezik a topologiaval.", nameof(isOcean));

            var r = new DenseLakeResult
            {
                IsLake = new bool[count],
                Depth = new double[count],
                LakeId = new int[count],
            };
            for (int i = 0; i < count; i++)
            {
                double d = filled[i] - field[i];
                bool lake = !isOcean[i] && d > minDepth;
                r.IsLake[i] = lake;
                r.Depth[i] = lake ? d : 0.0;
                r.LakeId[i] = -1;
            }

            var visited = new bool[count];
            var stack = new List<int>();
            var component = new List<int>();
            for (int start = 0; start < count; start++)
            {
                if (!r.IsLake[start] || visited[start]) continue;

                component.Clear();
                stack.Clear();
                stack.Add(start);
                visited[start] = true;
                while (stack.Count > 0)
                {
                    int cur = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    component.Add(cur);
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = topology.NeighborIndex(cur, (TileDirection)d);
                        if (r.IsLake[nb] && !visited[nb])
                        {
                            visited[nb] = true;
                            stack.Add(nb);
                        }
                    }
                }

                int thisId = r.Lakes.Count;
                double minSurf = double.PositiveInfinity, maxSurf = double.NegativeInfinity, sumSurf = 0;
                double maxDepth = double.NegativeInfinity, sumDepth = 0;
                var tiles = new List<TileId>(component.Count);
                foreach (int index in component)
                {
                    r.LakeId[index] = thisId;
                    tiles.Add(topology.TileAt(index));
                    double surf = filled[index];
                    if (surf < minSurf) minSurf = surf; if (surf > maxSurf) maxSurf = surf; sumSurf += surf;
                    double dp = r.Depth[index];
                    if (dp > maxDepth) maxDepth = dp; sumDepth += dp;
                }
                r.Lakes.Add(new LakeInfo
                {
                    Id = thisId, Tiles = tiles, TileCount = tiles.Count,
                    SurfaceElevation = sumSurf / tiles.Count, MinSurface = minSurf, MaxSurface = maxSurf,
                    MaxDepth = maxDepth, MeanDepth = sumDepth / tiles.Count,
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
