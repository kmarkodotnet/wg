using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Features
{
    /// <summary>
    /// M8 aggregált panel-metrikák (§6, docs/01-architecture.md §2, I4):
    /// minden mezőnek egyértelmű, visszakövethető számítási lánca van.
    ///
    /// HATÓKÖR: "Area" tile-számlálás, NEM valódi km² (dokumentált
    /// közelítés, ld. tools/reference/features_ref.py fejléce és ND-24 -
    /// a cubed-sphere tile-területek ~1.3-1.4x arányban változnak).
    ///
    /// TOVABBI PANEL-MEZOK ALLAPOTA (backlog M8):
    ///   - Coastal complexity: TISZTA GEOMETRIAI aggregacio (part-tile-szam
    ///     a szomszedsag-tablabol) -> MOST szamolhato (ld. lent). Determinisztikus,
    ///     nem igenyel uj modellt, ezert Python-referencia nelkul (mint a tobbi
    ///     itteni aggregacio), kozvetlen unit-teszttel verifikalt.
    ///   - Habitability: a MOST portolt homerseklet-modellbol (Temperature) egy
    ///     DOKUMENTALT SAV-HEURISZTIKA (folyekony-viz-barat homerseklet-sav) -
    ///     ld. lent; a sav-hatarok dokumentalt egyszerusites (mint az "Area").
    ///   - Soil fertility: BLOKKOLT - nincs talaj-modul a projektben (sem
    ///     kozettipus/litologia, sem talajkepzodes). Ez onallo milestone-nyi
    ///     munka; amint lesz talaj-mezo, ide kapcsolodik. NEM implementalt.
    /// </summary>
    public static class FeatureMetrics
    {
        /// <summary>Egy tile-halmaz "területe" tile-számlálásban.</summary>
        public static int AreaTiles(ICollection<TileId> tiles) => tiles.Count;

        /// <summary>Egy tile-halmazban előforduló egyedi biome-ok száma.</summary>
        public static int BiomeDiversity(IEnumerable<TileId> tiles, Dictionary<TileId, Climate.Biome> biomeOf)
        {
            var present = new HashSet<Climate.Biome>();
            foreach (TileId t in tiles)
                present.Add(biomeOf[t]);
            return present.Count;
        }

        /// <summary>
        /// Hány folyó-tile folyik KÖZVETLENÜL óceánba egy adott tile-halmazon
        /// (kontinens vagy régió) belül - a "folyó-torkolatok száma" panel-mező
        /// (§8.3) forrása.
        /// </summary>
        public static int RiverMouthCount(
            IEnumerable<TileId> tiles, Dictionary<TileId, TileId?> parent,
            Dictionary<TileId, bool> isOcean, HashSet<TileId> riverTiles)
        {
            int count = 0;
            foreach (TileId t in tiles)
            {
                if (!riverTiles.Contains(t))
                    continue;
                TileId? p = parent[t];
                if (p.HasValue && isOcean[p.Value])
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Az óceán aránya a mezőben (0..1) - a "World Overview" panel
        /// "Ocean coverage" mezőjének forrása (docs/01-architecture.md §2.1).
        /// </summary>
        public static double OceanCoverageFraction(Dictionary<TileId, bool> isOcean)
        {
            if (isOcean.Count == 0)
                return 0.0;
            int oceanCount = 0;
            foreach (bool v in isOcean.Values)
                if (v) oceanCount++;
            return (double)oceanCount / isOcean.Count;
        }

        /// <summary>
        /// Hány DISTINCT vízgyűjtő-régió (a <see cref="FeatureSegmentation.
        /// FindWatershedRegions"/> eredménye, kifolyás-tile szerint
        /// azonosítva) metsz bele egy adott tile-halmazba (kontinensbe) -
        /// a "River basins" panel-mező (§2.2) forrása. Egy régió akkor
        /// számít bele, ha LEGALÁBB EGY tile-ja a halmazban van.
        /// </summary>
        public static int RiverBasinCount(
            HashSet<TileId> tiles, Dictionary<TileId, List<TileId>> regions)
        {
            int count = 0;
            foreach (KeyValuePair<TileId, List<TileId>> region in regions)
            {
                foreach (TileId t in region.Value)
                {
                    if (tiles.Contains(t))
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Part-tile-ok szama a halmazban: azok a SZARAZFOLD-tile-ok, amelyeknek
        /// legalabb egy (a 4 szomszed kozul) OCEAN-szomszedja van. A "Coastal
        /// complexity" panel-mezo alapja (§8). Tiszta geometriai aggregacio a
        /// mar verifikalt TileNeighbors tablabol.
        /// </summary>
        public static int CoastTileCount(IEnumerable<TileId> tiles, Dictionary<TileId, bool> isOcean)
        {
            int count = 0;
            foreach (TileId t in tiles)
            {
                if (isOcean.TryGetValue(t, out bool ocean) && ocean)
                    continue; // csak szarazfold-tile lehet "part"
                for (int d = 0; d < 4; d++)
                {
                    TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                    if (isOcean.TryGetValue(nb, out bool nbOcean) && nbOcean)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// "Coastal complexity" (§8) - dimenziomentes alak-heurisztika: a part-
        /// tile-ok szama a szarazfold-terulet negyzetgyokehez viszonyitva. Egy
        /// tokeletes korlaphoz (minimalis kerulet/terulet) kozeli, egy erosen
        /// tagolt (fjordos/szigetes) parthoz magas. HATOKOR: heurisztikus alak-
        /// mutato (nem valodi fraktal-dimenzio); a normalizalo sqrt(terulet) a
        /// meret-fuggetlenseget adja. Determinisztikus, tile-szam-alapu (mint az "Area").
        /// </summary>
        public static double CoastalComplexity(IReadOnlyCollection<TileId> tiles, Dictionary<TileId, bool> isOcean)
        {
            int land = 0;
            foreach (TileId t in tiles)
                if (!(isOcean.TryGetValue(t, out bool ocean) && ocean))
                    land++;
            if (land == 0)
                return 0.0;
            return CoastTileCount(tiles, isOcean) / Math.Sqrt(land);
        }

        /// <summary>
        /// "Habitability" (§8) - a SZARAZFOLD-tile-ok azon hanyada (0..1),
        /// amelyeknek homerseklete a folyekony-viz-barat savba esik. A sav-
        /// hatarok (alap: 273.15..313.15 K, azaz 0..40 °C) DOKUMENTALT
        /// egyszerusites/heurisztika (mint az "Area" tile-szamlalasa), NEM
        /// verifikalt eletfeltetel-modell - a viz/tapanyag/legkor tobbi
        /// tenyezoje (pl. Soil fertility) meg hianyzik (ld. osztaly-doc).
        /// A hivo a mar verifikalt Temperature-bol (pl. AnnualTemperatureStats
        /// evi atlaga) adja a per-tile homersekletet.
        /// </summary>
        public static double HabitabilityFraction(
            IEnumerable<TileId> tiles, Dictionary<TileId, double> temperatureK, Dictionary<TileId, bool> isOcean,
            double minHabitableK = 273.15, double maxHabitableK = 313.15)
        {
            int land = 0, habitable = 0;
            foreach (TileId t in tiles)
            {
                if (isOcean.TryGetValue(t, out bool ocean) && ocean)
                    continue;
                land++;
                if (temperatureK.TryGetValue(t, out double tk) && tk >= minHabitableK && tk <= maxHabitableK)
                    habitable++;
            }
            return land == 0 ? 0.0 : (double)habitable / land;
        }
    }
}
