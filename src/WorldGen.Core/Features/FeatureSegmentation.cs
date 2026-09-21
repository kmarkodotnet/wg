using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Features
{
    /// <summary>
    /// M8 szegmentálás (§6.1, docs/01-architecture.md): kontinensek (a
    /// már meglévő <see cref="Tectonics.SeaLevelCalibration.CountContinents"/>
    /// M7-ből újrahasznosítva) + régiók (vízgyűjtő-alapú, a
    /// <see cref="Hydrology.FlowNetwork"/> szülő-fájából).
    ///
    /// HATÓKÖR: a régió EGYSZERŰEN a vízgyűjtő-csoport (minden szárazföld-
    /// tile ahhoz az óceán-"kifolyáshoz" tartozik, amihez végül lefolyik)
    /// — NEM a teljes vízgyűjtő ∪ biome-klaszter ∪ domborzati-törés
    /// hibrid (ND-05).
    /// </summary>
    public static class FeatureSegmentation
    {
        /// <summary>
        /// Minden szárazföld-tile-t az óceán-"kifolyás" (a szülő-láncon
        /// az első óceán-tile) szerint csoportosít.
        /// </summary>
        public static Dictionary<TileId, List<TileId>> FindWatershedRegions(
            Dictionary<TileId, TileId?> parent, Dictionary<TileId, bool> isOcean)
        {
            var regions = new Dictionary<TileId, List<TileId>>();

            foreach (var kv in isOcean)
            {
                if (kv.Value) continue; // csak szárazföld
                TileId tile = kv.Key;
                TileId current = tile;

                while (true)
                {
                    TileId? next = parent[current];
                    if (!next.HasValue) break; // nem várt (szárazföld-tile-nak mindig van szülője)
                    if (isOcean[next.Value])
                    {
                        if (!regions.TryGetValue(next.Value, out List<TileId> list))
                        {
                            list = new List<TileId>();
                            regions[next.Value] = list;
                        }
                        list.Add(tile);
                        break;
                    }
                    current = next.Value;
                }
            }

            return regions;
        }

        /// <summary>
        /// A leggyakoribb biome egy tile-halmazban.
        ///
        /// DÖNTETLEN: a KISEBB <see cref="Biome"/> enum-érték nyer. Ez nem
        /// kozmetika — 2026-09-21-ig NEM volt explicit döntetlen-feloldás, és
        /// az eredmény a `Dictionary&lt;Biome,int&gt;` BEJÁRÁSI SORRENDJÉN
        /// múlt (az `OrderByDescending` stabil, tehát az első bejárt
        /// maximumot adja vissza). Ez az I2 („nincs szótár-bejárási
        /// sorrendtől való függés") sértése volt, csak addig nem bukott ki,
        /// amíg kevés biome-osztály létezett. Az ND-126 (csapadék-alapú
        /// osztályozás) öt szárazföldi osztályt hozott a kettő helyett, és a
        /// Python-referencia azonnal más régiónevet adott, mint a C#
        /// („Sylthal Plains" vs „Sylthal Veld") — ugyanabból az adatból.
        /// </summary>
        public static Biome DominantBiome(IEnumerable<TileId> tiles, Dictionary<TileId, Biome> biomeOf)
        {
            var counts = new Dictionary<Biome, int>();
            foreach (TileId t in tiles)
            {
                Biome b = biomeOf[t];
                counts[b] = counts.TryGetValue(b, out int c) ? c + 1 : 1;
            }

            Biome best = default;
            int bestCount = -1;
            foreach (KeyValuePair<Biome, int> kv in counts)
            {
                if (kv.Value > bestCount || (kv.Value == bestCount && (int)kv.Key < (int)best))
                {
                    best = kv.Key;
                    bestCount = kv.Value;
                }
            }
            return best;
        }

        /// <summary>
        /// Egy régió (szárazföld-tile-halmaz) durva MORFOLÓGIAI típusa a
        /// régiónév-generáláshoz (§6.1 "morfológiai típusfelismerés"). TISZTA,
        /// determinisztikus geometriai/elevation-aggregáció (nincs új modell,
        /// nincs random) - a szomszédság-tábla (part-tile) és a per-tile
        /// eleváció alapján. HATÓKÖR: heurisztikus, NÉV-ízesítő közelítés (mint
        /// az "Area" tile-számlálás) - a küszöbök dokumentált MVP-értékek,
        /// hangolhatók. Nem geológiai osztályozás.
        /// </summary>
        public enum LandformType { Lowland, Plain, Plateau, Mountains, Basin, Island }

        public static string LandformTypeName(LandformType t) => t switch
        {
            LandformType.Mountains => "mountains",
            LandformType.Plateau => "plateau",
            LandformType.Basin => "basin",
            LandformType.Plain => "plain",
            LandformType.Island => "island",
            _ => "lowland",
        };

        /// <summary>
        /// A régió morfológiai típusa. A döntési sorrend fontos (első találat
        /// nyer): sziget (kicsi + túlnyomóan part) → hegyvidék (magas + tagolt)
        /// → fennsík (magas + lapos) → medence (alacsony belső + magas relief)
        /// → síkság (lapos) → alföld (alapértelmezés).
        /// </summary>
        public static LandformType ClassifyLandform(
            IReadOnlyCollection<TileId> tiles,
            Dictionary<TileId, double> elevationM, Dictionary<TileId, bool> isOcean, double seaLevelM,
            double mountainMeanAboveM = 1200.0, double mountainReliefM = 1500.0,
            double plateauMeanAboveM = 900.0, double plateauReliefMaxM = 700.0,
            double plainReliefMaxM = 400.0,
            double basinMeanAboveMaxM = 150.0, double basinReliefMinM = 600.0,
            int islandMaxTiles = 12, double islandCoastRatioMin = 0.6)
        {
            if (tiles == null || tiles.Count == 0)
                return LandformType.Lowland;

            double sum = 0.0, min = double.PositiveInfinity, max = double.NegativeInfinity;
            int coast = 0;
            foreach (TileId t in tiles)
            {
                double e = elevationM[t];
                sum += e;
                if (e < min) min = e;
                if (e > max) max = e;
                for (int d = 0; d < 4; d++)
                {
                    TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                    if (isOcean.TryGetValue(nb, out bool o) && o) { coast++; break; }
                }
            }

            double area = tiles.Count;
            double meanAbove = sum / area - seaLevelM;
            double relief = max - min;
            double coastRatio = coast / area;

            if (tiles.Count <= islandMaxTiles && coastRatio >= islandCoastRatioMin)
                return LandformType.Island;
            if (meanAbove >= mountainMeanAboveM && relief >= mountainReliefM)
                return LandformType.Mountains;
            if (meanAbove >= plateauMeanAboveM && relief <= plateauReliefMaxM)
                return LandformType.Plateau;
            if (relief >= basinReliefMinM && meanAbove <= basinMeanAboveMaxM)
                return LandformType.Basin;
            if (relief <= plainReliefMaxM)
                return LandformType.Plain;
            return LandformType.Lowland;
        }

        /// <summary>
        /// 2. probléma (docs/backlog.md "Continent és Island fogalmak
        /// szétválasztása", 2026-09-13) - a <see cref="Tectonics.SeaLevelCalibration.CountContinents"/>
        /// összefüggő szárazföld-komponensei ("landmass") közti SZEMANTIKAI
        /// kategorizálás. A flood-fill maga VÁLTOZATLAN (ez a lépés nem
        /// nyúl a domborzat-/tektonika-generáláshoz vagy a méreteloszláshoz -
        /// ld. a 3. problémát arra) - csak a méret alapján egy meglévő
        /// landmasst utólag ide-oda sorol.
        /// </summary>
        public enum LandmassClass { Continent, LargeIsland, Island, Islet }

        /// <summary>
        /// A küszöbök a landmass méretét a VILÁG TELJES SZÁRAZFÖLD-
        /// tile-számához (nem a bolygó teljes tile-számához) viszonyítják -
        /// ez a tengerszint-kalibráció víz-arányától (ND-37 target
        /// water fraction) FÜGGETLEN, stabil mértéket ad. Ellenőrizve a
        /// valós TestEarth001 világon (2026-09-13, 31 landmass, ~8600
        /// szárazföld-tile): az 5%-os küszöb PONTOSAN a két domináns
        /// szuperkontinenst (49.6% és 41.0%) választja el a harmadik
        /// legnagyobbtól (2.6%) - nem önkényes vágás egy folytonos
        /// eloszlás közepén. A 0.5%/0.05% küszöb a megmaradó hosszú farkat
        /// nagyjából egyenlő nagyságrendű sávokra bontja (log-skálán). A
        /// legalacsonyabb sáv (Islet) a JELENLEGI `CountContinents`
        /// `minSize=5` mellett gyakorlatilag üres marad (ez SZÁNDÉKOS - a
        /// 3. probléma foglalkozik azzal, hogy egyáltalán keletkezzenek-e
        /// ilyen apró töredékek).
        /// </summary>
        public const double ContinentLandShareThreshold = 0.05;
        public const double LargeIslandLandShareThreshold = 0.005;
        public const double IslandLandShareThreshold = 0.0005;

        /// <summary>Tiszta arányszámítás (osztás, összehasonlítás) - I1/I2-kompatibilis, nincs kerekítési bizonytalanság a küszöbök közelében (bitre reprodukálható IEEE-754 osztás).</summary>
        public static LandmassClass ClassifyLandmass(int landmassTileCount, int totalLandTiles)
        {
            if (totalLandTiles <= 0 || landmassTileCount <= 0)
                return LandmassClass.Islet;

            double share = landmassTileCount / (double)totalLandTiles;
            if (share > ContinentLandShareThreshold) return LandmassClass.Continent;
            if (share > LargeIslandLandShareThreshold) return LandmassClass.LargeIsland;
            if (share > IslandLandShareThreshold) return LandmassClass.Island;
            return LandmassClass.Islet;
        }

        public static string LandmassClassName(LandmassClass c) => c switch
        {
            LandmassClass.Continent => "continent",
            LandmassClass.LargeIsland => "large island",
            LandmassClass.Island => "island",
            _ => "islet",
        };

        /// <summary>
        /// ND-127: a panel-régió cél-mérete a SZÁRAZFÖLD százalékában, nem fix
        /// tile-számban. Ok: a viewer a referencia-szinten (alapértelmezés
        /// level 5) panelez, a tesztek/mérések level 6-on futnak, és fix
        /// tile-számmal a régiók SZÁMA szintenként többszörösére ugrana
        /// (ugyanaz a bolygó, más menü). Százalékkal a legnagyobb landmass
        /// 10 (level 5), illetve 11 (level 6) régiót kap - mérve.
        /// </summary>
        public const int RegionTargetLandSharePercent = 3;

        /// <summary>
        /// A cél-régióméret tile-ban. TISZTA EGÉSZ aritmetika (a +50 a
        /// felezőpont-kerekítés, nincs lebegőpontos kerekítési
        /// kétértelműség). Minimum 1, különben a "méret &lt; cél" feltétel
        /// sosem teljesülne és az összevonás azonnal megállna.
        /// </summary>
        public static int RecommendedRegionTileTarget(int totalLandTiles)
        {
            if (totalLandTiles <= 0) return 1;
            int target = (totalLandTiles * RegionTargetLandSharePercent + 50) / 100;
            return target < 1 ? 1 : target;
        }

        /// <summary>
        /// ND-127 - FÖLDRAJZILAG ÖSSZETARTOZÓ régiók a nyers vízgyűjtőkből.
        ///
        /// A PROBLÉMA, amit megold: a <see cref="FindWatershedRegions"/> a
        /// torkolat ÓCEÁN-tile-ja szerint kulcsol, ami a LEFOLYÁS azonosítója,
        /// nem egy földrajzi egységé. Mérve (seed 0xA7C944210000, 20 lemez,
        /// víz 0,65): level 5-ön 789 vízgyűjtő 2151 szárazföld-tile-ra, a
        /// panel legalább 5 tile-os szűrője után a szárazföld 50,1%-a
        /// SEMMILYEN régióba nem esett, a régiók 35,7%-a több, egymástól
        /// elszakadt földdarabból állt (ugyanabba az óceán-tile-ba folyó két
        /// félsziget), és a legnagyobb landmasson 65 menüpont keletkezett.
        ///
        /// AZ ALGORITMUS (tiszta egész-aritmetika: nincs lebegőpont, nincs
        /// random, nincs szótár-bejárási sorrendtől való függés - minden
        /// döntés explicit összehasonlítás, I1/I2-kompatibilis):
        /// 1. CELLÁK: minden vízgyűjtő önmagában összefüggő komponensekre
        ///    bontva. Innentől minden cella EGY összefüggő földdarab, és
        ///    egyetlen landmasson belül van (két landmass definíció szerint
        ///    nem szomszédos, tehát a landmass-határ átlépése kizárt).
        /// 2. SZOMSZÉDSÁGI GRÁF a cellák között, élsúly = a közös határ
        ///    hossza (hány tile-él érintkezik).
        /// 3. ÖSSZEVONÁS: amíg van cél-méret alatti cella, amelynek van
        ///    szomszédja, a legkisebbet (döntetlen: kisebb kanonikus
        ///    <see cref="TileId.Value"/>) beolvasztjuk abba a szomszédjába,
        ///    amelyik (a) maga is cél alatt van, ha van ilyen; (b) ezen belül
        ///    a LEGHOSSZABB közös határt osztja vele; (c) döntetlennél a
        ///    kisebb; (d) döntetlennél a kisebb <see cref="TileId.Value"/>-jú.
        ///
        /// A (b) szabály MÉRT különbség: a "legkisebb szomszédba olvad"
        /// változat a partvonal mentén elnyúló régiókat épít (a legnagyobb
        /// landmass régióinak átmérő/gyök-terület mutatója 2,5-3,5), a közös
        /// határ mentén 1,9-3,4 - egy kompakt folté ~2,0, magáé a
        /// landmassé 2,9.
        ///
        /// GARANCIÁK (a Core-tesztek ezeket ellenőrzik): a kimenet a bemeneti
        /// vízgyűjtők tile-jainak PARTÍCIÓJA (minden tile pontosan egy
        /// régióban), minden régió térben összefüggő, és a sorrend kanonikus
        /// (méret csökkenő, döntetlennél a legkisebb <see cref="TileId.Value"/>).
        /// A hívónak tehát NEM kell méret-szűrőt alkalmaznia - éppen az a
        /// szűrő volt az, ami a szárazföld felét kihagyta a navigációból.
        ///
        /// KÖLTSÉG: a cellaszámban (nem tile-számban) négyzetes - minden
        /// összevonás egy teljes cella-pásztázással választja ki a
        /// legkisebbet. MÉRVE: level 5-ön 19 ms (789 cella), level 6-on
        /// 43 ms (2467 cella). Ha valaha level 7-en kellene panelezni,
        /// ez a pásztázás a cserélendő rész (prioritási sor), nem az
        /// algoritmus.
        /// </summary>
        public static List<List<TileId>> MergeWatershedsIntoRegions(
            Dictionary<TileId, List<TileId>> watersheds, int targetRegionTileCount)
        {
            var result = new List<List<TileId>>();
            if (watersheds == null || watersheds.Count == 0) return result;

            // 1. cellak - a vizgyujtok kanonikus sorrendjeben (NEM a szotar
            // bejarasi sorrendjeben), minden vizgyujto osszefuggo komponensei.
            var roots = new List<TileId>(watersheds.Keys);
            roots.Sort((a, b) => a.Value.CompareTo(b.Value));
            var cells = new List<List<TileId>>();
            foreach (TileId root in roots)
                cells.AddRange(FindConnectedComponents(watersheds[root]));
            cells.Sort((a, b) => MinValue(a).CompareTo(MinValue(b)));

            int n = cells.Count;
            var cellOf = new Dictionary<TileId, int>();
            for (int i = 0; i < n; i++)
                foreach (TileId t in cells[i])
                    cellOf[t] = i;

            var parent = new int[n];
            var size = new int[n];
            var minTile = new ulong[n];
            var alive = new bool[n];
            var borders = new Dictionary<int, int>[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = i;
                size[i] = cells[i].Count;
                minTile[i] = MinValue(cells[i]);
                alive[i] = true;
                borders[i] = new Dictionary<int, int>();
            }

            // 2. szomszedsagi graf, elsuly = kozos hatar hossza. Minden elt a
            // SAJAT oldalarol szamoljuk, igy a suly szimmetrikus marad.
            for (int i = 0; i < n; i++)
            {
                foreach (TileId t in cells[i])
                {
                    for (int d = 0; d < 4; d++)
                    {
                        TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                        if (cellOf.TryGetValue(nb, out int j) && j != i)
                            borders[i][j] = borders[i].TryGetValue(j, out int w) ? w + 1 : 1;
                    }
                }
            }

            // 3. osszevonas
            while (true)
            {
                int g = -1;
                for (int i = 0; i < n; i++)
                {
                    if (!alive[i] || size[i] >= targetRegionTileCount || borders[i].Count == 0) continue;
                    if (g < 0 || size[i] < size[g] || (size[i] == size[g] && minTile[i] < minTile[g]))
                        g = i;
                }
                if (g < 0) break;

                var candidates = new List<int>(borders[g].Keys);
                candidates.Sort();
                int h = -1, hBorder = -1;
                bool hUnder = false;
                foreach (int cand in candidates)
                {
                    int border = borders[g][cand];
                    bool under = size[cand] < targetRegionTileCount;
                    bool better;
                    if (h < 0) better = true;
                    else if (under != hUnder) better = under;
                    else better = border > hBorder
                        || (border == hBorder && (size[cand] < size[h]
                            || (size[cand] == size[h] && minTile[cand] < minTile[h])));
                    if (better) { h = cand; hBorder = border; hUnder = under; }
                }

                parent[g] = h;
                alive[g] = false;
                size[h] += size[g];
                if (minTile[g] < minTile[h]) minTile[h] = minTile[g];
                foreach (KeyValuePair<int, int> kv in borders[g])
                {
                    int x = kv.Key;
                    if (x == h) continue;
                    borders[h][x] = borders[h].TryGetValue(x, out int wh) ? wh + kv.Value : kv.Value;
                    borders[x].Remove(g);
                    borders[x][h] = borders[x].TryGetValue(h, out int wx) ? wx + kv.Value : kv.Value;
                }
                borders[h].Remove(g);
                borders[g] = new Dictionary<int, int>();
            }

            // A cellak a sajat gyokerukhoz (union-find) csoportositva. A
            // csoport indexet a CELLA sorrendje adja (nem szotar-bejaras),
            // igy a kimenet sorrendje is fuggetlen a hash-tol.
            var groupIndexOfRoot = new Dictionary<int, int>();
            var groups = new List<List<TileId>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                if (!groupIndexOfRoot.TryGetValue(r, out int gi))
                {
                    gi = groups.Count;
                    groupIndexOfRoot[r] = gi;
                    groups.Add(new List<TileId>());
                }
                groups[gi].AddRange(cells[i]);
            }

            foreach (List<TileId> group in groups)
            {
                group.Sort((a, b) => a.Value.CompareTo(b.Value));
                result.Add(group);
            }
            result.Sort((a, b) =>
            {
                int bySize = b.Count.CompareTo(a.Count);
                return bySize != 0 ? bySize : a[0].Value.CompareTo(b[0].Value);
            });
            return result;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private static ulong MinValue(List<TileId> tiles)
        {
            ulong min = ulong.MaxValue;
            foreach (TileId t in tiles)
                if (t.Value < min) min = t.Value;
            return min;
        }

        /// <summary>
        /// Negyedik panelszint ("Terület" / Area, docs/backlog.md "Navigációs
        /// menü és panel-elrendezés" + docs/01-architecture.md §12): egy régió
        /// (szárazföld-tile-halmaz) felosztása kb. <paramref name="targetAreaTileCount"/>
        /// méretű, ÖSSZEFÜGGŐ darabokra.
        ///
        /// MÓDSZER (tiszta gráf-algoritmus, NINCS random, NINCS lebegőpontos
        /// transzcendens - I1/I2-kompatibilis minden szálszámon/sorrenden):
        /// 1. Először ÖSSZEFÜGGŐ KOMPONENSEKRE bont (4-szomszédsági BFS, a
        ///    régió tile-halmazán belül) - a vízgyűjtő-régió DEFINÍCIÓJA
        ///    (közös óceán-kifolyás) NEM garantálja a térbeli összefüggőséget
        ///    (két külön szárazföld-darab is folyhat ugyanabba az óceán-
        ///    tile-ba anélkül, hogy egymással szomszédosak lennének).
        /// 2. Minden komponenst KÜLÖN, többforrású BFS-sel oszt fel: a magok
        ///    kiválasztása "legtávolabbi pont" mintavétel (k-center jellegű,
        ///    a kanonikus <see cref="TileId.Value"/> a determinisztikus
        ///    döntetlen-eldöntő), majd minden tile a legközelebbi maghoz kerül
        ///    (szinkronizált BFS-rétegek, döntetlennél a kisebb kanonikus
        ///    <see cref="TileId.Value"/>-jú mag nyer) - ez egy Voronoi-jellegű,
        ///    összefüggő, kb. egyenletes méretű particionálás a tile-
        ///    szomszédsági gráfon.
        ///
        /// A visszaadott listák sorrendje kanonikus (komponens legkisebb
        /// tile-ja, majd a komponensen belüli mag <see cref="TileId.Value"/>
        /// sorrendje) - platform-/szálszámfüggetlen, mint minden más itteni
        /// szegmentálás.
        /// </summary>
        public static List<List<TileId>> PartitionRegionIntoAreas(
            IReadOnlyCollection<TileId> regionTiles, int targetAreaTileCount = 40)
        {
            if (regionTiles == null || regionTiles.Count == 0)
                return new List<List<TileId>>();

            var areas = new List<List<TileId>>();
            foreach (List<TileId> component in FindConnectedComponents(regionTiles))
                areas.AddRange(PartitionComponent(component, targetAreaTileCount));
            return areas;
        }

        private static List<List<TileId>> FindConnectedComponents(IReadOnlyCollection<TileId> tiles)
        {
            var tileSet = new HashSet<TileId>(tiles);
            var visited = new HashSet<TileId>();
            var ordered = new List<TileId>(tileSet);
            ordered.Sort((a, b) => a.Value.CompareTo(b.Value));

            var components = new List<List<TileId>>();
            foreach (TileId start in ordered)
            {
                if (visited.Contains(start)) continue;
                var component = new List<TileId>();
                var queue = new Queue<TileId>();
                queue.Enqueue(start);
                visited.Add(start);
                while (queue.Count > 0)
                {
                    TileId t = queue.Dequeue();
                    component.Add(t);
                    foreach (TileId nb in NeighborsInSet(t, tileSet))
                    {
                        if (visited.Add(nb)) queue.Enqueue(nb);
                    }
                }
                components.Add(component);
            }
            return components;
        }

        private static IEnumerable<TileId> NeighborsInSet(TileId t, HashSet<TileId> tileSet)
        {
            for (int d = 0; d < 4; d++)
            {
                TileId nb = TileNeighbors.Neighbor(t, (TileDirection)d);
                if (tileSet.Contains(nb)) yield return nb;
            }
        }

        /// <summary>Egyetlen (már összefüggő) komponens felosztása - ld. <see cref="PartitionRegionIntoAreas"/>.</summary>
        private static List<List<TileId>> PartitionComponent(List<TileId> component, int targetAreaTileCount)
        {
            var tileSet = new HashSet<TileId>(component);
            int n = tileSet.Count;
            if (n <= targetAreaTileCount || targetAreaTileCount <= 0)
                return new List<List<TileId>> { SortedByValue(tileSet) };

            int k = Math.Max(1, (int)Math.Round(n / (double)targetAreaTileCount));
            if (k <= 1)
                return new List<List<TileId>> { SortedByValue(tileSet) };

            // "Legtavolabbi pont" mag-mintavetel: a legkisebb kanonikus
            // tile-lal kezdve, mindig azt a meg ki nem valasztott tile-t
            // vesszuk fel, ami a MAR kivalasztott magoktol a legtavolabb van
            // (BFS-tavolsag), dontetlennel a kisebb TileId.Value nyer.
            var seeds = new List<TileId> { MinByValue(tileSet) };
            while (seeds.Count < k)
            {
                Dictionary<TileId, int> dist = BfsDistances(seeds, tileSet);
                TileId best = default;
                bool haveBest = false;
                int bestDist = -1;
                foreach (TileId t in tileSet)
                {
                    if (seeds.Contains(t)) continue;
                    int d = dist.TryGetValue(t, out int dv) ? dv : int.MaxValue;
                    if (!haveBest || d > bestDist || (d == bestDist && t.Value < best.Value))
                    {
                        best = t;
                        bestDist = d;
                        haveBest = true;
                    }
                }
                seeds.Add(best);
            }
            seeds.Sort((a, b) => a.Value.CompareTo(b.Value));

            // Tobbforrasu, retegenkent szinkronizalt BFS: minden tile a
            // legkozelebbi maghoz kerul, dontetlennel a kisebb TileId.Value-ju
            // mag nyer - FUGGETLENUL a bejarasi sorrendtol (minden jeloltet
            // explicit osszehasonlitunk, nem "elso nyer").
            var owner = new Dictionary<TileId, TileId>();
            foreach (TileId s in seeds) owner[s] = s;
            var distByTile = new Dictionary<TileId, int>();
            foreach (TileId s in seeds) distByTile[s] = 0;
            List<TileId> frontier = new List<TileId>(seeds);
            int depth = 0;
            while (frontier.Count > 0)
            {
                depth++;
                var proposals = new Dictionary<TileId, TileId>();
                foreach (TileId t in frontier)
                {
                    TileId o = owner[t];
                    foreach (TileId nb in NeighborsInSet(t, tileSet))
                    {
                        if (distByTile.ContainsKey(nb)) continue;
                        if (!proposals.TryGetValue(nb, out TileId currentOwner) || o.Value < currentOwner.Value)
                            proposals[nb] = o;
                    }
                }
                if (proposals.Count == 0) break;
                var nextFrontier = new List<TileId>();
                foreach (KeyValuePair<TileId, TileId> kv in proposals)
                {
                    distByTile[kv.Key] = depth;
                    owner[kv.Key] = kv.Value;
                    nextFrontier.Add(kv.Key);
                }
                nextFrontier.Sort((a, b) => a.Value.CompareTo(b.Value));
                frontier = nextFrontier;
            }

            var bySeed = new Dictionary<TileId, List<TileId>>();
            foreach (TileId s in seeds) bySeed[s] = new List<TileId>();
            var unassigned = new List<TileId>();
            foreach (TileId t in tileSet)
            {
                if (owner.TryGetValue(t, out TileId o)) bySeed[o].Add(t);
                else unassigned.Add(t);
            }

            var result = new List<List<TileId>>();
            foreach (TileId s in seeds) result.Add(SortedByValue(bySeed[s]));
            // Elvileg nem fordulhat elo (a komponens mar osszefuggo), de
            // biztonsagi halo: ha megis maradna hozzarendeletlen tile, sajat
            // teruletkent, kanonikus sorrendben hozzafuzzuk.
            if (unassigned.Count > 0)
                result.Add(SortedByValue(unassigned));
            return result;
        }

        private static Dictionary<TileId, int> BfsDistances(List<TileId> seeds, HashSet<TileId> tileSet)
        {
            var dist = new Dictionary<TileId, int>();
            var queue = new Queue<TileId>();
            foreach (TileId s in seeds)
            {
                dist[s] = 0;
                queue.Enqueue(s);
            }
            while (queue.Count > 0)
            {
                TileId t = queue.Dequeue();
                int d = dist[t];
                foreach (TileId nb in NeighborsInSet(t, tileSet))
                {
                    if (!dist.ContainsKey(nb))
                    {
                        dist[nb] = d + 1;
                        queue.Enqueue(nb);
                    }
                }
            }
            return dist;
        }

        private static TileId MinByValue(IEnumerable<TileId> tiles)
        {
            TileId best = default;
            bool have = false;
            foreach (TileId t in tiles)
            {
                if (!have || t.Value < best.Value) { best = t; have = true; }
            }
            return best;
        }

        private static List<TileId> SortedByValue(IEnumerable<TileId> tiles)
        {
            var list = new List<TileId>(tiles);
            list.Sort((a, b) => a.Value.CompareTo(b.Value));
            return list;
        }

        /// <summary>
        /// 3. probléma (docs/backlog.md "Extrém landmass méreteloszlás",
        /// 2026-09-13) - DIAGNOSZTIKAI statisztikák egy generált világ
        /// landmass-méreteloszlásához. Tiszta függvény a MÁR meglévő
        /// <see cref="Tectonics.SeaLevelCalibration.CountContinents"/>
        /// eredményén - nem változtat a generáláson, csak MÉRI azt.
        /// </summary>
        public readonly struct LandmassDistributionStats
        {
            public readonly int LandmassCount;
            public readonly int TotalLandTiles;
            public readonly double LargestLandmassShare;
            public readonly double Top2LandmassShare;
            public readonly double MedianLandmassSize;
            public readonly int P90LandmassSize;
            public readonly int TinyLandmassCount;
            public readonly double GiniCoefficient;

            public LandmassDistributionStats(
                int landmassCount, int totalLandTiles, double largestLandmassShare, double top2LandmassShare,
                double medianLandmassSize, int p90LandmassSize, int tinyLandmassCount, double giniCoefficient)
            {
                LandmassCount = landmassCount;
                TotalLandTiles = totalLandTiles;
                LargestLandmassShare = largestLandmassShare;
                Top2LandmassShare = top2LandmassShare;
                MedianLandmassSize = medianLandmassSize;
                P90LandmassSize = p90LandmassSize;
                TinyLandmassCount = tinyLandmassCount;
                GiniCoefficient = giniCoefficient;
            }
        }

        /// <summary>
        /// A "tiny" landmass méretküszöbe (tile-ban) a diagnosztikában - NEM
        /// azonos a régió/kontinens `minSize=5` szűréssel (ld. 1./2. probléma),
        /// csak a méreteloszlás-riport egyik sávhatára.
        /// </summary>
        public const int TinyLandmassTileThreshold = 20;

        /// <summary>
        /// 1:1 megfeleles a Python referencia `landmass_sweep`-mintájú
        /// statisztikájával - explicit, hordozható rendezés (méret szerint,
        /// nincs a nyelv gyűjtemény-bejárási sorrendjére támaszkodó lépés).
        /// </summary>
        public static LandmassDistributionStats ComputeLandmassDistributionStats(IReadOnlyList<IReadOnlyCollection<TileId>> landmasses)
        {
            int n = landmasses.Count;
            if (n == 0)
                return new LandmassDistributionStats(0, 0, 0.0, 0.0, 0.0, 0, 0, 0.0);

            var sizesDesc = new List<int>(n);
            foreach (IReadOnlyCollection<TileId> lm in landmasses)
                sizesDesc.Add(lm.Count);
            sizesDesc.Sort((a, b) => b.CompareTo(a));

            long total = 0;
            foreach (int s in sizesDesc) total += s;

            double largestShare = total > 0 ? sizesDesc[0] / (double)total : 0.0;
            double top2Share = total > 0
                ? (sizesDesc[0] + (n > 1 ? sizesDesc[1] : 0)) / (double)total
                : 0.0;

            double median;
            if (n % 2 == 1)
            {
                median = sizesDesc[n / 2];
            }
            else
            {
                // sizesDesc CSOKKENO sorrendben van - a ket kozepso elem
                // ugyanaz, mint novekvo sorrendben, csak forditott indexen.
                median = (sizesDesc[n / 2 - 1] + sizesDesc[n / 2]) / 2.0;
            }

            int p90Index = System.Math.Max(0, (int)(0.1 * n) - 1);
            int p90 = sizesDesc[p90Index];

            int tiny = 0;
            foreach (int s in sizesDesc)
                if (s < TinyLandmassTileThreshold) tiny++;

            // Gini egyutthato, diszkret kepletet - NOVEKVO sorrend kell hozza.
            var sizesAsc = new List<int>(sizesDesc);
            sizesAsc.Sort();
            double giniNumerator = 0.0;
            for (int i = 0; i < n; i++)
                giniNumerator += (2.0 * (i + 1) - n - 1) * sizesAsc[i];
            double gini = total > 0 ? giniNumerator / (n * (double)total) : 0.0;

            return new LandmassDistributionStats(n, (int)total, largestShare, top2Share, median, p90, tiny, gini);
        }
    }
}
