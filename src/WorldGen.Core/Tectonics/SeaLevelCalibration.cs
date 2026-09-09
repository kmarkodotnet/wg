using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Core.Terrain;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M4 tengerszint-kalibráció + kontinens-számlálás (§4.4,
    /// docs/05-milestones.md) — a `TEST-EARTH-001` elfogadási kritérium
    /// (50-75% víz, több kontinens) numerikus alapja.
    ///
    /// MÓDSZER: a tengerszintet a magasság-eloszlás PERCENTILISE határozza
    /// meg (nem fix méter-érték) — ez automatikusan biztosítja a célzott
    /// víz-arányt, függetlenül attól, hogy a nyers elevációeloszlás éppen
    /// hogyan alakul.
    /// </summary>
    public static class SeaLevelCalibration
    {
        /// <summary>Minden tile elevációja egy adott (worldSeed, plateCount, level) világon, t=0-nál (M4, statikus).</summary>
        public static Dictionary<TileId, double> ComputeElevationField(ulong worldSeed, int plateCount, int level)
        {
            var seeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);
            return ComputeElevationFieldWithSeeds(worldSeed, seeds, level);
        }

        /// <summary>
        /// Minden tile elevációja <paramref name="timeMyr"/> deep-time időpontban
        /// (M10, §14.2) — a lemez-magok <see cref="PlateMotion"/> szerint elmozdulva.
        /// <paramref name="timeMyr"/>=0 esetén bitre megegyezik a statikus
        /// <see cref="ComputeElevationField"/> eredményével (a Rodrigues-forgatás
        /// angle=0-nál egzaktul identitás: cos(0)=1, sin(0)=0 IEEE-754 pontosan).
        /// </summary>
        public static Dictionary<TileId, double> ComputeElevationFieldAtTime(
            ulong worldSeed, int plateCount, int level, double timeMyr)
        {
            var seeds0 = PlateGeneration.GenerateSeeds(worldSeed, plateCount);
            var movedSeeds = PlateMotion.MovedSeeds(worldSeed, seeds0, timeMyr);
            return ComputeElevationFieldWithSeeds(worldSeed, movedSeeds, level);
        }

        private static Dictionary<TileId, double> ComputeElevationFieldWithSeeds(
            ulong worldSeed, (double X, double Y, double Z)[] seeds, int level)
        {
            uint n = level == 0 ? 1u : (1u << level);
            int tileCount = checked((int)(6L * n * n));
            var ids = new TileId[tileCount];

            int idx = 0;
            for (int face = 0; face <= 5; face++)
                for (uint u = 0; u < n; u++)
                    for (uint v = 0; v < n; v++)
                        ids[idx++] = TileId.FromFaceLevelUV(face, level, u, v);

            // TELJESITMENY: minden tile elevacioja FUGGETLEN, tiszta fuggveny a
            // sajat (worldSeed, seeds, id) bemenetebol - nincs tile-ok kozotti
            // megosztott allapot, tehat a kiertekeles sorrendje NEM befolyasolja
            // az eredmenyt (I2). Parhuzamositva (Parallel.For, kulon celtombbe
            // irva, majd egyszalu Dictionary-epitessel) nagy szinteken (level 7-8,
            // ahol ezt a fuggvenyt a hidrologia/BuildStaticBaseLayer eleri) sokszoros
            // gyorsulast ad a magszam aranyaban, BITRE valtozatlan eredmennyel -
            // ugyanaz a keplet fut tile-onkent, csak nem szekvencialisan.
            var elevations = new double[tileCount];
            Parallel.For(0, tileCount, i =>
            {
                TileId id = ids[i];
                TileGeometry.ToPosition(id, out double x, out double y, out double z);

                // ND-36: a lemez-HOZZARENDELES a WARPOLT poziciot
                // kapja (DomainWarp) - ez tori meg a nyers legkozelebbi-mag
                // Voronoi-hatarok tul geometrikus, nagykor-iv-szeru
                // jelleget. Az ElevationWithBoundary (es a benne levo
                // BaseElevation zaj-kiertekeles) VALTOZATLANUL a NYERS
                // (x,y,z)-t kapja - a warp csak a lemez-topologia
                // dontesehez hasznalt, a domborzat-textura nem.
                //
                // ND-39 "C" opio (warp-hoisting): a warp CSAK EGYSZER
                // fut ide pontonkent - az AssignPlate ES a
                // BoundaryUplift (ElevationWithBoundaryFromWarped-en
                // keresztul) UGYANAZT a mar kiszamitott (wx,wy,wz)-t
                // hasznalja, ahelyett hogy a BoundaryUplift sajat maga
                // ujraszamolna a WarpPosition-t ugyanarra a pontra.
                // Tiszta fuggveny -> bitre azonos eredmeny, mint
                // korabban, csak a redundans szamitas tunt el.
                DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
                int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
                double elevation = PlateBoundaryEffect.ElevationWithBoundaryFromWarped(
                    worldSeed, plateId, id.Value, x, y, z, wx, wy, wz, seeds, out _);
                elevations[i] = elevation;
            });

            var field = new Dictionary<TileId, double>(tileCount);
            for (int i = 0; i < tileCount; i++)
                field[ids[i]] = elevations[i];
            return field;
        }

        /// <summary>A percentilis-érték, ami a célzott víz-arányt adja.</summary>
        public static double CalibrateSeaLevel(IEnumerable<double> elevations, double targetWaterFraction)
        {
            var sorted = new List<double>(elevations);
            sorted.Sort();
            if (sorted.Count == 0)
                throw new ArgumentException("Üres eleváció-mező.", nameof(elevations));

            int idx = (int)(targetWaterFraction * sorted.Count);
            idx = Math.Max(0, Math.Min(sorted.Count - 1, idx));
            return sorted[idx];
        }

        /// <summary>
        /// ND-38: dimenziómentes víz-térfogat PROXY egy adott tengerszinthez -
        /// minden tile terület-súly nélkül (egyenletes tile-terület-közelítés,
        /// ld. a már bevett <see cref="Features.FeatureMetrics.AreaTiles"/>
        /// precedenst, ND-24) <c>max(0, seaLevel - elevation)</c>-vel járul
        /// hozzá. Monoton NÖVEKVŐ függvénye <paramref name="seaLevel"/>-nek,
        /// ez teszi lehetővé a <see cref="CalibrateSeaLevelByVolume"/> bináris
        /// keresését.
        /// </summary>
        public static double ComputeFloodedVolumeProxy(IEnumerable<double> elevations, double seaLevel)
        {
            double total = 0.0;
            foreach (double e in elevations)
            {
                double depth = seaLevel - e;
                if (depth > 0.0)
                    total += depth;
            }
            return total;
        }

        /// <summary>
        /// ND-38: a tengerszint, amire <see cref="ComputeFloodedVolumeProxy"/>
        /// gyakorlatilag egyenlő <paramref name="targetVolume"/>-mal, FIX
        /// iterációszámú bináris kereséssel (nem tolerancia-alapú leállás -
        /// CLAUDE.md I1, determinizmus platformok között: egy fix
        /// iterációszámú ciklus mindig ugyanannyi lépést fut, bitre
        /// reprodukálhatóan). A <c>[min(elevations), max(elevations)]</c>
        /// tartomány dupla lebegőpontos pontosság alatt 60 lépésben bőven
        /// belefér.
        /// </summary>
        public static double CalibrateSeaLevelByVolume(IEnumerable<double> elevations, double targetVolume, int iterations = 60)
        {
            var values = new List<double>(elevations);
            if (values.Count == 0)
                throw new ArgumentException("Üres eleváció-mező.", nameof(elevations));

            double lo = values[0];
            double hi = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                double e = values[i];
                if (e < lo) lo = e;
                if (e > hi) hi = e;
            }

            for (int i = 0; i < iterations; i++)
            {
                double mid = (lo + hi) / 2.0;
                double vol = ComputeFloodedVolumeProxy(values, mid);
                if (vol < targetVolume)
                    lo = mid;
                else
                    hi = mid;
            }
            return (lo + hi) / 2.0;
        }

        /// <summary>Összefüggő szárazföld-komponensek (szélességi bejárás), min. méret szerint szűrve.</summary>
        public static List<List<TileId>> CountContinents(Dictionary<TileId, double> field, double seaLevel, int minSize)
        {
            var land = new HashSet<TileId>();
            foreach (var kv in field)
                if (kv.Value >= seaLevel)
                    land.Add(kv.Key);

            var visited = new HashSet<TileId>();
            var components = new List<List<TileId>>();

            foreach (TileId start in land)
            {
                if (visited.Contains(start))
                    continue;

                var component = new List<TileId>();
                var queue = new Queue<TileId>();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    TileId current = queue.Dequeue();
                    component.Add(current);

                    TileNeighbors.GetAll(current, out TileId right, out TileId left, out TileId up, out TileId down);
                    CheckNeighbor(right, land, visited, queue);
                    CheckNeighbor(left, land, visited, queue);
                    CheckNeighbor(up, land, visited, queue);
                    CheckNeighbor(down, land, visited, queue);
                }
                components.Add(component);
            }

            var sized = new List<List<TileId>>();
            foreach (var c in components)
                if (c.Count >= minSize)
                    sized.Add(c);
            return sized;
        }

        private static void CheckNeighbor(TileId candidate, HashSet<TileId> land, HashSet<TileId> visited, Queue<TileId> queue)
        {
            if (land.Contains(candidate) && !visited.Contains(candidate))
            {
                visited.Add(candidate);
                queue.Enqueue(candidate);
            }
        }
    }
}
