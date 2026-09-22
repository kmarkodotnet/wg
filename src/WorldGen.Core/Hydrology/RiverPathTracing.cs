using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Core.Terrain;
using WorldGen.Core.Tectonics;

namespace WorldGen.Core.Hydrology
{
    /// <summary>
    /// M9/M7 dendritikus folyó-nyomvonal (backlog 2026-09-05, felhasználói
    /// kérés, MAGAS prioritás): a MEGLÉVŐ <see cref="FlowNetwork"/> a
    /// REFERENCIA szinten (pl. level 6-8) számol priority-flood-ot - ezen a
    /// felbontáson egy vízgyűjtő-területnek túl kevés tile jut ahhoz, hogy a
    /// kirajzolt folyók valódi, elágazó mellékfolyó-mintázatot mutassanak
    /// (rövidek, alig ágaznak).
    ///
    /// MÓDSZER (a felhasználónak felajánlott 2 opció közül a LOKÁLIS
    /// választva - globális `hydrologyLevel` emelése level 10+-ra milliókra
    /// növelné a tile-számot és belátha­tatlanul lassítaná a szekvenciális
    /// priority-flood-ot): a REFERENCIA szinten csak a folyó-FORRÁSOKAT
    /// választjuk ki (csapadékos + hegyvidéki tile-ok, <see
    /// cref="Climate.MoisturePrecipitation"/>-ból), majd EGYENKÉNT, egy
    /// SOKKAL FINOMABB szinten (fineLevel = level + fineDepth) követjük a
    /// lejtőt lefelé (steepest descent) az óceánig.
    ///
    /// A finom szintű fraktál-zaj miatt a nyers lejtő-követés sok, apró
    /// (zaj-méretű) helyi mélyedésbe akadna bele, mielőtt bármilyen valós
    /// vízgyűjtő-kijáratot elérne - ez pontosan az az ok, ami miatt az
    /// EREDETI (globális) hidrológia priority-flood-ot használ naiv
    /// lejtő-követés helyett. Ezért minden "pit"-nél egy LOKÁLIS,
    /// korlátozott csomópont-számú priority-flood (<see
    /// cref="FindLocalSpillway"/>) keresi meg a legközelebbi tulcsordulási
    /// pontot - ugyanaz az elv, mint <see cref="FlowNetwork.PriorityFlood"/>,
    /// csak IGÉNY SZERINT, lokálisan futtatva.
    ///
    /// DENDRITIKUS ELÁGAZÁS: ha egy KÉSŐBBI forrás útja egy MÁR MEGLÁTOGATOTT
    /// (korábbi forrás által "lefoglalt") finom tile-ba fut, ott MEGÁLL -
    /// összefolyás (a hívó, a viewer, ezt egy közös pontban végződő két
    /// vonalszakaszként rajzolja).
    ///
    /// DETERMINIZMUS: minden lépésnél a 4 szomszéd közül a SZIGORÚAN
    /// legalacsonyabb nyer; döntetlen esetén a rögzített szomszéd-sorrend
    /// (Right,Left,Up,Down) első találata. A teljes útvonal (fő ág + minden
    /// escape-kitérő) egy `visited` halmazzal védett - SOSEM lép vissza már
    /// bejárt tile-ra, ez strukturálisan zárja ki a hurkot.
    ///
    /// Python-referencia: tools/reference/river_path_ref.py.
    /// </summary>
    public static class RiverPathTracing
    {
        public const int DefaultFineDepth = 4;
        /// <summary>
        /// A folyo-forrasok szama. 12 -> 48 (2026-09-21, #5).
        ///
        /// A 12-es ertek NEM hidrologiai dontes volt, hanem KOLTSEG-korlat: a
        /// folytonos nyomvonal-koveto ~0,8 s/folyo, es szekvencialisan futott
        /// (a megosztott `claimed` terkep miatt), tehat 12 folyo 6-10 masodperc.
        /// Ebbol kovetkezett a felhasznaloi visszajelzes: "ritkak a folyok" es
        /// "nincs tree alakzat, sosem er bele egyik a masikba" - 12 egymastol
        /// tavoli forras nyomvonalai gyakorlatilag soha nem talalkoznak.
        ///
        /// A <see cref="BuildContinuousRiverNetworkFromSourcesParallel"/> ezt a
        /// korlatot feloldotta (BITRE azonos kimenet, merve 3,7-5,2x). MERT
        /// koltseg 48 forrasnal: kb. 7-8 s HATTERSZALON (a Buildet nem
        /// blokkolja), a szekvencialis ~35 s helyett.
        ///
        /// MERT HATAS (ParallelRiverNetworkTests): 12 forras -> 1 osszefolyas,
        /// 48 forras -> 15. A halozat tehat SURUBB es van benne fa-szerkezet,
        /// DE meg mindig SEKELY (a legnagyobb vizhozam-suly 40 forrasnal is
        /// csak 2). Ennek oka a forras-KIVALASZTAS: a globalis "legcsapadekosabb
        /// top-K" a legnedvesebb hegyvidekek kozott szetszorja a forrasokat,
        /// nem egy vizgyujton belul suriti oket - egy valodi dendritikus fahoz
        /// az kell. Ez kulon lepes, ld. ND-124.
        /// </summary>
        public const int DefaultSourceTopK = 48;
        public const double DefaultMinElevAboveSeaM = 300.0;
        public const double DefaultPrecipPercentile = 0.80;
        public const int DefaultMaxSteps = 2000;
        public const int DefaultEscapeNodeBudget = 400;

        public enum TerminationReason { Ocean, Pit, Merged, MaxSteps }

        public sealed class RiverPath
        {
            public int SourceIndex;
            public TileId Source;
            public List<TileId> Path = new List<TileId>();
            public TerminationReason Termination;
        }

        /// <summary>
        /// Csapadékos hegyvidéki tile-ok kiválasztása forrásként - MINDKÉT
        /// küszöbnek (magasság ÉS csapadék) egyszerre kell teljesülnie. A
        /// csapadék-küszöb a SZÁRAZFÖLDI eloszlás percentilise (nem
        /// abszolút érték), hogy világfüggetlenül értelmes maradjon.
        /// Determinisztikus rendezés: csökkenő csapadék, döntetlennel
        /// növekvő TileId.Value szerint.
        /// </summary>
        public static List<TileId> SelectRiverSources(
            Dictionary<TileId, double> elevField, Dictionary<TileId, double> precipField,
            Dictionary<TileId, bool> isOcean, double seaLevel,
            int topK = DefaultSourceTopK,
            double minElevAboveSeaM = DefaultMinElevAboveSeaM,
            double precipPercentile = DefaultPrecipPercentile)
        {
            var landPrecip = new List<double>();
            foreach (KeyValuePair<TileId, double> kv in precipField)
                if (!isOcean[kv.Key]) landPrecip.Add(kv.Value);

            if (landPrecip.Count == 0)
                return new List<TileId>();

            landPrecip.Sort();
            int idx = Math.Max(0, Math.Min(landPrecip.Count - 1, (int)(precipPercentile * landPrecip.Count)));
            double precipThreshold = landPrecip[idx];

            var candidates = new List<TileId>();
            foreach (KeyValuePair<TileId, double> kv in elevField)
            {
                TileId t = kv.Key;
                if (isOcean[t]) continue;
                if (kv.Value < seaLevel + minElevAboveSeaM) continue;
                if (precipField[t] < precipThreshold) continue;
                candidates.Add(t);
            }

            candidates.Sort((a, b) =>
            {
                int byPrecip = precipField[b].CompareTo(precipField[a]);
                return byPrecip != 0 ? byPrecip : a.Value.CompareTo(b.Value);
            });

            if (candidates.Count > topK)
                candidates.RemoveRange(topK, candidates.Count - topK);
            return candidates;
        }

        /// <summary>
        /// Hány vízgyűjtőből válasszunk forrást (a legnagyobbaktól kezdve).
        /// A 16 KOMPROMISSZUM: kevesebb medence mélyebb fát ad (6 medencénél
        /// 44% összefolyás 42% helyett), de a folyókat a bolygó néhány
        /// pontjára sűríti - ami éppen a MÁSIK felhasználói panasz
        /// ("az egész bolygón ritkák a folyók"). 16 külön folyórendszer
        /// eloszlik a szárazföldeken, és a fa is többszintű marad.
        /// </summary>
        public const int DefaultSourceBasinCount = 16;

        /// <summary>
        /// Hány forrás egy vízgyűjtőn belül. A 6 MÉRT érték: 4-nél a
        /// legnagyobb vízhozam-súly 4 és 5-6 folyó éri el a 3-as súlyt,
        /// 6-nál a súly 6 és 13 folyó - vagyis a fa ettől lesz TÖBBSZINTŰ,
        /// nem csak "egy mellékfolyó". Ld. <see cref="BuildRiverNetworkPerBasin"/>.
        /// </summary>
        public const int DefaultSourcesPerBasin = 6;

        /// <summary>
        /// Ennél kevesebb tile-os vízgyűjtőbe nem teszünk forrást - egy
        /// 2-3 tile-os parti lefolyásban nincs hova összefolyni.
        /// </summary>
        public const int DefaultMinBasinTiles = 12;

        /// <summary>
        /// Két forrás MINIMÁLIS távolsága egy vízgyűjtőn belül. Enélkül a
        /// legcsapadékosabb tile-ok egymás szomszédjai lennének, a második
        /// folyó egy-két lépés után beleolvadna az elsőbe, és nem keletkezne
        /// valódi mellékfolyó - csak egy elágazás a forrás mellett.
        /// </summary>
        public const double DefaultSourceSeparationMeters = 150_000.0;

        /// <summary>
        /// ND-124 (A): forrás-kiválasztás VÍZGYŰJTŐNKÉNT, nem globális
        /// top-K-val.
        ///
        /// MIÉRT. A <see cref="SelectRiverSources"/> a legcsapadékosabb
        /// tile-okat veszi az EGÉSZ bolygóról. Ez a legnedvesebb hegyvidékek
        /// KÖZÖTT szórja szét a forrásokat, tehát a nyomvonalak külön
        /// medencékben futnak a tengerig, és ritkán találkoznak - mérve:
        /// 48 globális forrásból 8 összefolyás, a legnagyobb vízhozam-súly 2.
        /// A felhasználói visszajelzés ("nincs tree alakzat, sosem ér bele
        /// egyik a másikba") pontosan ez.
        ///
        /// Dendritikus fához a forrásoknak EGY vízgyűjtőn belül kell lenniük -
        /// akkor közös torkolat felé tartanak, és összefolynak. Ez a függvény
        /// a legnagyobb <paramref name="basinCount"/> vízgyűjtőt veszi, és
        /// mindegyikben <paramref name="sourcesPerBasin"/> forrást választ.
        ///
        /// A CSAPADÉK-KÜSZÖB MEDENCÉN BELÜL RELATÍV - és ez nem kozmetika.
        /// Mérve (seed 0xA7C944210000, level 6): a 6 legnagyobb vízgyűjtőben
        /// EGYETLEN tile sincs a szárazföldi csapadék-eloszlás 80.
        /// percentilise fölött, tehát a globális küszöbbel a metszet ÜRES -
        /// pontosan 0 forrás. A nagy vízgyűjtők ugyanis ott vannak, ahol sok
        /// a szárazföld (kontinens-belső), a legnedvesebb tile-ok viszont a
        /// keskeny, csapadékos parti hegyvidékeken. Ezért itt a medence SAJÁT
        /// legnedvesebb tile-jait vesszük; a magasság-küszöb (hegyvidék)
        /// marad abszolút.
        ///
        /// DETERMINIZMUS: a vízgyűjtők méret szerint csökkenően, döntetlennél
        /// a torkolat `TileId.Value`-ja szerint növekvően rendezve; a
        /// jelölteken belül csapadék szerint csökkenően, döntetlennél
        /// `TileId.Value` szerint növekvően. Nincs `System.Random`, nincs
        /// szótár-bejárási sorrendtől való függés.
        ///
        /// A minimális forrás-távolság (<paramref name="minSeparationMeters"/>)
        /// mohó szűréssel érvényesül: a sorrendben előrébb álló jelölt
        /// "elnyeli" a hozzá közelieket. Ez is determinisztikus.
        /// </summary>
        public static List<TileId> SelectRiverSourcesPerBasin(
            Dictionary<TileId, double> elevField, Dictionary<TileId, double> precipField,
            Dictionary<TileId, bool> isOcean, Dictionary<TileId, TileId?> floodParent,
            double seaLevel,
            int basinCount = DefaultSourceBasinCount,
            int sourcesPerBasin = DefaultSourcesPerBasin,
            double minElevAboveSeaM = DefaultMinElevAboveSeaM,
            double minSeparationMeters = DefaultSourceSeparationMeters,
            int minBasinTiles = DefaultMinBasinTiles)
        {
            if (elevField == null) throw new ArgumentNullException(nameof(elevField));
            if (precipField == null) throw new ArgumentNullException(nameof(precipField));
            if (isOcean == null) throw new ArgumentNullException(nameof(isOcean));
            if (floodParent == null) throw new ArgumentNullException(nameof(floodParent));
            if (basinCount < 1) throw new ArgumentOutOfRangeException(nameof(basinCount));
            if (sourcesPerBasin < 1) throw new ArgumentOutOfRangeException(nameof(sourcesPerBasin));

            Dictionary<TileId, List<TileId>> basins =
                Features.FeatureSegmentation.FindWatershedRegions(floodParent, isOcean);

            var outlets = new List<TileId>(basins.Keys);
            outlets.Sort((a, b) =>
            {
                int bySize = basins[b].Count.CompareTo(basins[a].Count);
                return bySize != 0 ? bySize : a.Value.CompareTo(b.Value);
            });

            // ND-27: NEM Math.Cos - az nem garantaltan bitpontos platformok
            // kozott, es ez a kuszob kozvetlenul befolyasolja, MELY tile-ok
            // lesznek folyo-forrasok (tehat a kritikus uton van).
            double minSeparationCos = Numerics.DeterministicMath.Cos(
                Math.Max(0.0, minSeparationMeters) / PlanetConstants.RadiusMeters);

            var sources = new List<TileId>();
            int basinsUsed = 0;
            for (int b = 0; b < outlets.Count && basinsUsed < basinCount; b++)
            {
                List<TileId> basin = basins[outlets[b]];
                if (basin.Count < minBasinTiles) break; // meret szerint rendezve: innentol mind kisebb
                basinsUsed++;

                var candidates = new List<TileId>();
                foreach (TileId t in basin)
                {
                    if (!elevField.TryGetValue(t, out double elevation)) continue;
                    if (elevation < seaLevel + minElevAboveSeaM) continue;
                    if (!precipField.ContainsKey(t)) continue;
                    candidates.Add(t);
                }
                candidates.Sort((x, y) =>
                {
                    int byPrecip = precipField[y].CompareTo(precipField[x]);
                    return byPrecip != 0 ? byPrecip : x.Value.CompareTo(y.Value);
                });

                // Moho, minimalis-tavolsagu valasztas a medencen belul.
                var chosen = new List<(double X, double Y, double Z)>(sourcesPerBasin);
                for (int c = 0; c < candidates.Count && chosen.Count < sourcesPerBasin; c++)
                {
                    TileGeometry.ToPosition(candidates[c], out double x, out double y, out double z);
                    bool tooClose = false;
                    for (int k = 0; k < chosen.Count; k++)
                    {
                        double dot = x * chosen[k].X + y * chosen[k].Y + z * chosen[k].Z;
                        if (dot > minSeparationCos) { tooClose = true; break; }
                    }
                    if (tooClose) continue;
                    chosen.Add((x, y, z));
                    sources.Add(candidates[c]);
                }
            }
            return sources;
        }

        /// <summary>Memoizált pontszerű elevációkiértékelés - a nyomvonalkövetés és a pit-escape keresés gyakran ugyanazokat a finom tile-okat kérdezi le.</summary>
        private sealed class ElevationCache
        {
            private readonly ulong _worldSeed;
            private readonly (double X, double Y, double Z)[] _seeds;
            private readonly Dictionary<TileId, double> _cache = new Dictionary<TileId, double>();

            public ElevationCache(ulong worldSeed, (double X, double Y, double Z)[] seeds)
            {
                _worldSeed = worldSeed;
                _seeds = seeds;
            }

            public double Get(TileId id)
            {
                if (_cache.TryGetValue(id, out double cached))
                    return cached;
                TileGeometry.ToPosition(id, out double x, out double y, out double z);
                double elev = ElevationAtPosition(_worldSeed, x, y, z, _seeds);
                _cache[id] = elev;
                return elev;
            }
        }

        /// <summary>
        /// Pontszerű (nem tile-hoz kötött) elevációkiértékelés - UGYANAZ a
        /// képlet, mint a tile-alapú <see cref="ElevationCache.Get"/>, csak
        /// TETSZŐLEGES, folytonos (x,y,z) egységgömb-pozícióra. A `tileIdValue`
        /// paraméter (<see cref="PlateBoundaryEffect.ElevationWithBoundaryFromWarped"/>)
        /// a jelenlegi képletben NEM használt (a modell LOD-/rács-független -
        /// ugyanaz a fizikai pont ugyanazt az elevációt adja bármilyen
        /// felbontáson kérdezve, ld. M9 architektúra), ezért itt egy
        /// tetszőleges konstans (0) adható át - ez teszi lehetővé a
        /// <see cref="TraceRiverPathContinuous"/> tile-rácstól független,
        /// folytonos nyomvonal-követését.
        /// </summary>
        private static double ElevationAtPosition(
            ulong worldSeed, double x, double y, double z, (double X, double Y, double Z)[] seeds)
        {
            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
            return PlateBoundaryEffect.ElevationWithBoundaryFromWarped(
                worldSeed, plateId, 0UL, x, y, z, wx, wy, wz, seeds, out _);
        }

        private static TileId Neighbor(TileId id, TileDirection direction) => TileNeighbors.Neighbor(id, direction);

        private static readonly TileDirection[] DirectionOrder =
            { TileDirection.Right, TileDirection.Left, TileDirection.Up, TileDirection.Down };

        /// <summary>
        /// Lokális, korlátozott csomópont-számú priority-flood a `pit`-ből
        /// kiindulva - ugyanaz az elv, mint <see cref="FlowNetwork.
        /// PriorityFlood"/>, csak IGÉNY SZERINT futtatva. Visszaadja az utat
        /// a pit-től a tulcsordulási pontig (a pit-et is beleértve elsőként)
        /// és a tulcsordulási pont elevációját - vagy nullt, ha a budgeten
        /// belül nem talált kijáratot. `pathVisited` a hívó eddigi teljes
        /// útvonala - a keresés ELKERÜLI ezeket (se nem terjeszkedik beléjük,
        /// se nem fogadja el őket tulcsordulási pontként), különben a folyó a
        /// SAJÁT már bejárt medrét metszhetné újra (hurok).
        /// </summary>
        private static (List<TileId> Path, double SpillwayElevation)? FindLocalSpillway(
            ElevationCache elevCache, TileId pit, double pitElevation,
            HashSet<TileId> pathVisited, int nodeBudget)
        {
            // Kulcs = (feltoltott elevacio, egyedi szamlalo) - UGYANAZ a
            // mintazat, mint FlowNetwork.PriorityFlood-ban: a SortedSet-nek
            // igy sosem kell TileId-t osszehasonlitania (a szamlalo garantalt
            // egyedi), azt csak a kulon `counterToNode` dictionary-bol nezzuk ki.
            var visited = new HashSet<TileId> { pit };
            var parent = new Dictionary<TileId, TileId?> { [pit] = null };
            var queue = new SortedSet<(double FilledElevation, long Counter)>();
            var counterToNode = new Dictionary<long, TileId>();
            long counter = 0;

            queue.Add((pitElevation, counter));
            counterToNode[counter] = pit;
            counter++;

            int expanded = 0;
            while (queue.Count > 0 && expanded < nodeBudget)
            {
                var top = queue.Min;
                queue.Remove(top);
                expanded++;
                TileId node = counterToNode[top.Counter];
                double rawElev = elevCache.Get(node);

                if (!node.Equals(pit) && rawElev < pitElevation && !pathVisited.Contains(node))
                {
                    var path = new List<TileId>();
                    TileId? cur = node;
                    while (cur.HasValue)
                    {
                        path.Add(cur.Value);
                        cur = parent[cur.Value];
                    }
                    path.Reverse();
                    return (path, rawElev);
                }

                foreach (TileDirection d in DirectionOrder)
                {
                    TileId nb = Neighbor(node, d);
                    if (visited.Contains(nb) || pathVisited.Contains(nb))
                        continue;
                    visited.Add(nb);
                    double nbRaw = elevCache.Get(nb);
                    double nbFilled = Math.Max(nbRaw, top.FilledElevation);
                    parent[nb] = node;
                    queue.Add((nbFilled, counter));
                    counterToNode[counter] = nb;
                    counter++;
                }
            }

            return null;
        }

        /// <summary>
        /// Egy forrásból induló nyomvonal a FINE szinten: lejtő-menti
        /// (steepest descent) lépések, lokális priority-flood-dal (ld.
        /// <see cref="FindLocalSpillway"/>) minden apró, zaj-méretű
        /// mélyedésnél áthidalva - csak akkor áll meg véglegesen "pit"-ként,
        /// ha a lokális kereséstem sem talál kijáratot a csomópont-budgeten
        /// belül (valódi, nagy medence/tó).
        /// </summary>
        public static RiverPath TraceRiverPath(
            ulong worldSeed, (double X, double Y, double Z)[] seeds, double seaLevel,
            TileId source, int sourceIndex, int fineDepth,
            Dictionary<TileId, int> claimed, int maxSteps, int escapeNodeBudget)
        {
            var elevCache = new ElevationCache(worldSeed, seeds);
            return TraceRiverPath(worldSeed, seeds, seaLevel, source, sourceIndex, fineDepth, claimed, maxSteps, escapeNodeBudget, elevCache);
        }

        private static RiverPath TraceRiverPath(
            ulong worldSeed, (double X, double Y, double Z)[] seeds, double seaLevel,
            TileId source, int sourceIndex, int fineDepth,
            Dictionary<TileId, int> claimed, int maxSteps, int escapeNodeBudget,
            ElevationCache elevCache)
        {
            int fineLevel = source.Level + fineDepth;
            source.GetUV(out uint su, out uint sv);
            TileId current = TileId.FromFaceLevelUV(source.Face, fineLevel, su << fineDepth, sv << fineDepth);

            var result = new RiverPath { SourceIndex = sourceIndex, Source = source };
            result.Path.Add(current);
            var pathVisited = new HashSet<TileId> { current };
            double elev = elevCache.Get(current);

            for (int step = 0; step < maxSteps; step++)
            {
                if (elev < seaLevel)
                {
                    result.Termination = TerminationReason.Ocean;
                    return result;
                }
                if (claimed != null && step > 0 && claimed.ContainsKey(current))
                {
                    result.Termination = TerminationReason.Merged;
                    return result;
                }

                TileId? bestNode = null;
                double bestElev = 0.0;
                foreach (TileDirection d in DirectionOrder)
                {
                    TileId nb = Neighbor(current, d);
                    if (pathVisited.Contains(nb))
                        continue;
                    double nbElev = elevCache.Get(nb);
                    if (nbElev < elev && (bestNode == null || nbElev < bestElev))
                    {
                        bestNode = nb;
                        bestElev = nbElev;
                    }
                }

                if (bestNode.HasValue)
                {
                    current = bestNode.Value;
                    elev = bestElev;
                    result.Path.Add(current);
                    pathVisited.Add(current);
                    continue;
                }

                var escape = FindLocalSpillway(elevCache, current, elev, pathVisited, escapeNodeBudget);
                if (escape == null)
                {
                    result.Termination = TerminationReason.Pit;
                    return result;
                }
                List<TileId> escapePath = escape.Value.Path;
                for (int i = 1; i < escapePath.Count; i++)
                {
                    result.Path.Add(escapePath[i]);
                    pathVisited.Add(escapePath[i]);
                }
                current = escapePath[escapePath.Count - 1];
                elev = escape.Value.SpillwayElevation;
            }

            result.Termination = TerminationReason.MaxSteps;
            return result;
        }

        public const double DefaultContinuousStepMeters = 50.0;
        /// <summary>
        /// Az IRANY-ERZEKELES sugara METERBEN, a tenyleges lepeskoztol
        /// (`stepMeters`) fuggetlenul - nagyobb, mint a lepeskoz, hogy
        /// atlasson a lepeskoz-lepteku fraktal-zajon, de a tenyleges lepes
        /// merete valtozatlanul `stepMeters` marad (a kirajzolt pont
        /// pontossaga).
        /// </summary>
        public const double DefaultContinuousSensingRadiusMeters = 500.0;
        public const int DefaultContinuousRingDirections = 8;
        /// <summary>A lokalis priority-flood escape-racs cellamerete - NAGYOBB, mint `stepMeters`, hogy ugyanakkora csomopont-koltsegvetessel sokkal nagyobb fizikai teruletet fedjen le (ld. TraceRiverPathContinuous doksi).</summary>
        public const double DefaultContinuousEscapeCellMeters = 2000.0;
        public const int DefaultContinuousEscapeNodeBudget = 30_000;
        /// <summary>Tisztan biztonsagi felso korlat (vegtelen ciklus ellen) - a normal mukodesben SOSEM er el ide, ld. doksi.</summary>
        public const long DefaultContinuousMaxSteps = 500_000;
        /// <summary>
        /// A HUROK-VEDELEM racsfelbontasa (ld. TraceRiverPathContinuous
        /// "PATHVISITED" megjegyzes) - egy tile ezen a szinten kb 14 m,
        /// finomabb mint a alapertelmezett 50 m-es lepeskoz, hogy ket
        /// KULONBOZO, kozeli (de nem azonos) lepest ne kezeljen tevesen
        /// ugyanannak a helynek.
        /// </summary>
        public const int DefaultVisitedGridLevel = 20;

        public sealed class ContinuousRiverPath
        {
            public int SourceIndex;
            public List<(double X, double Y, double Z)> Points = new List<(double X, double Y, double Z)>();
            public TerminationReason Termination;

            /// <summary>
            /// Ha <see cref="Termination"/> == Merged, annak a folyónak a
            /// SourceIndex-e, amibe ez a folyó beleolvadt (ld.
            /// <see cref="ClaimedTileInfo.RiverIndex"/>) - egyébként -1.
            /// Ez adja a dendritikus "kinél folyik bele" fát, amiből a
            /// vízhozam-arányos vonal-szélesség (ld. RiverDischargeWeights)
            /// levezethető - I3-kompatibilis (a szélesség a TÉNYLEGES
            /// összefolyás-struktúrából jön, nem dekoratív becslés).
            /// </summary>
            public int MergedIntoRiverIndex = -1;

            /// <summary>
            /// Azok a <see cref="Points"/>-indexek, amelyeken a
            /// nyomvonal-követés a `claimed` összefolyás-ellenőrzést
            /// ELVÉGZI - vagyis a követő-ciklus iterációinak teteje.
            ///
            /// MIÉRT KELL EZ KÜLÖN LISTA. A pit-escape útvonal EGYSZERRE
            /// több pontot fűz a `Points`-hoz, és azokat a követés NEM
            /// ellenőrzi összefolyásra - csak a következő iteráció tetején
            /// lévő pontot. A `Points` indexei tehát önmagukban NEM
            /// mondják meg, hol történt ellenőrzés. Ezt a
            /// <see cref="BuildContinuousRiverNetworkFromSourcesParallel"/>
            /// használja: az első, `claimed` NÉLKÜL felvett nyomvonalat
            /// utólag PONTOSAN ott vágja el, ahol a szekvenciális követés is
            /// elvágta volna - enélkül SZIGORÚBB lenne (az escape-útvonal
            /// belső pontjain is összefolyást találna), és más folyóhálózatot
            /// adna.
            /// </summary>
            public List<int> ClaimCheckIndices = new List<int>();
        }

        /// <summary>
        /// Egy finom-tile "lefoglalásának" adatai a folytonos dendritikus
        /// összefolyáshoz (ld. <see cref="TraceRiverPathContinuous"/>
        /// "Merged" ága) - a folyó-INDEXEN kívül a TÉNYLEGES (folytonos
        /// térbeli) pontot is tárolja, amivel a lefoglaló folyó áthaladt
        /// ezen a tile-on. FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06, backlog:
        /// "az útvonal a felületen helyenként megszakadni tűnik"):
        /// korábban csak a folyó-INDEX volt tárolva, ezért egy összefolyásnál
        /// a MEGSZAKADÓ folyó egyszerűen megállt "valahol EBBEN a
        /// fine-tile-ban" (akár több száz méterre a befogadó folyó
        /// TÉNYLEGES vonalától) - ez adta a vizuálisan megszakadó
        /// összefolyási pontokat. A pozíció tárolásával a megszakadó folyó
        /// utolsó pontja PONTOSAN a befogadó folyó egyik valódi pontjára
        /// zárható (ld. lent), a két vonal ténylegesen összeér.
        /// </summary>
        public readonly struct ClaimedTileInfo
        {
            public readonly int RiverIndex;
            public readonly (double X, double Y, double Z) Position;

            public ClaimedTileInfo(int riverIndex, (double X, double Y, double Z) position)
            {
                RiverIndex = riverIndex;
                Position = position;
            }
        }

        /// <summary>
        /// Felhasznaloi visszajelzes utan HARMADSZOR ujragondolt finomitas
        /// (2026-09-06): az elozo ("hop-anchored", majd "iranykupos +
        /// stagnalas-eszleles") valtozatok MERESSEL bizonyitottan SOSEM
        /// ertek celba termeszetes uton - a durva ut ket VEGPONTJA kozott
        /// egy fix `maxSteps`-ig probalkoztak, majd egy ~150 KM-es
        /// MESTERSEGES "safety net" teleport-tal zartak a torkolatra (ld.
        /// docs/04-decisions.md ND-49 3. kiegeszites a teljes meresi
        /// lancert). A felhasznaloi elvaras egyertelmu volt: NE legyen
        /// befegetett lepesszam-korlat - a folyo kovesse a lejtot, amig
        /// VALODIAN allovizhez (oceanhoz vagy egy zart medencehez/tohoz)
        /// nem er, PONTOSAN ugyanugy, ahogy a durva `TraceRiverPath` is
        /// teszi tile-racson - csak folytonos terben.
        ///
        /// MODSZER: nincs elore megadott vegpont - a seta a FORRASBOL indul,
        /// es MAGA donti el a termeszetes veget (Ocean/Pit/Merged), pontosan
        /// a durva algoritmus mintajat kovetve:
        /// 1. NORMAL LEPES: `ringDirections` jelolt irany a
        ///    `sensingRadiusMeters` sugaru korben (ez a nagyobb sugar
        ///    atlagolja a lepeskoz-lepteku zajt) - csak akkor lepunk, ha a
        ///    LEGJOBB jelolt SZIGORUAN alacsonyabb, mint a JELENLEGI pont
        ///    (nem csak a koron beluli relative legalacsonyabb - ez a
        ///    donto kulonbseg a korabbi, hibas valtozathoz kepest, ld. lent).
        ///    A lepes MERETE `stepMeters` (fuggetlen az erzekelesi sugartol).
        /// 2. ESCAPE: ha egyik jelolt sem alacsonyabb (helyi minimum/
        ///    medence), egy VALODI lokalis priority-flood (<see
        ///    cref="FindContinuousLocalSpillway"/>) keresi meg a tenyleges
        ///    tulcsordulasi pontot - ugyanaz az elv, mint <see
        ///    cref="FindLocalSpillway"/>-ben, csak egy folytonos (i,j)
        ///    erinto-sik racson, NEM a globalis TileId-racson.
        ///
        /// MIERT NEM MUKODOTT A KORABBI ("iranykupos") VALTOZAT: az egy
        /// ELORE ISMERT vegpont (a durva torkolat pozicioja) fele probalt
        /// haladni PUSZTAN lokalis gradienskovetessel, iranypersziszten-
        /// ciaval - MERESSEL BIZONYITVA (ld. ND-49 3. kieg.), hogy egy nagy,
        /// enyhen zajos teruleten ez egy hosszu, celtalan bolyongast adott
        /// (a mert referencia-folyon 1804 km bejart ut egy 551 km-es durva
        /// lanchoz es 205 km-es legvonalhoz kepest), mert a lepes-valasztas
        /// SOHA nem kovetelte meg a SZIGORU csokkenest - mindig lepett
        /// valamerre, meg felfele is, ha az volt "a kupon beluli legjobb".
        /// Az UJ valtozat ELVETI az elore ismert vegpontot ES az iranykupot;
        /// helyette PONTOSAN a mar validalt durva mintat (szigoru csokkenes
        /// + escape) alkalmazza, igy STRUKTURALISAN kizart a celtalan
        /// bolyongas - minden lepes VAGY szigoruan lejt, VAGY egy explicit,
        /// verifikalt tulcsordulasi pontra ugrik.
        ///
        /// MIERT NAGYOBB AZ ESCAPE-RACS CELLAMERETE (`escapeCellMeters`),
        /// MINT A KIRAJZOLASI LEPESKOZ (`stepMeters`): MERT (ld. ND-49 3.
        /// kieg.) egy azonos FIZIKAI keresesi sugarhoz a csomopont-szam a
        /// cellameret NEGYZETEVEL fordanyan aranyos - 500 m-es cellaval egy
        /// valos, tobb szaz km²-es fennsik-szeru terulet atszeleseset a
        /// rendelkezesre allo csomopont-koltsegvetes (`escapeNodeBudget`)
        /// mar nem tudta athidalni (a folyo tevesen "Pit"-kent zarult, holott
        /// a durva referencia-vektorok szerint "Ocean"-nal kellett volna).
        /// 2000 m-es cellamerettel (16x nagyobb terulet UGYANAKKORA
        /// csomopont-koltsegvetessel) mind a 12 referencia-forras
        /// termeszetesen elerte a sajat (durva szinten ismert) vegallapotat,
        /// 12 folyora osszesen kb. 6.4 masodperc alatt (merve, hatterszalon
        /// futtatva). Az escape-szakaszok RITKAK (folyononkent tipikusan
        /// 0-6 alkalom) es RÖVIDEK a teljes utvonalhoz kepest, ezert a
        /// nagyobb cellameretuk nem all ossze latvanyos "durva" hatassa - a
        /// NORMAL lepesek (a folyo tulnyomo resze) valtozatlanul
        /// `stepMeters` pontossaguak.
        ///
        /// FONTOS KOVETKEZMENY (tudatosan vallalt, dokumentalt): mivel a
        /// folytonos elevaciofuggveny LOD-fuggetlen (ugyanaz a fizikai pont
        /// ugyanazt az erteket adja barmilyen felbontason), a FINOMABB
        /// (folytonos) kereses ELTERHET a durva (13 km-es tile-atlagolt)
        /// dontestol - PELDAUL a durva `TraceRiverPath` "Pit"-nek minositett
        /// ket forrast (mert a 13 km-es tile-ok elfedtek egy keskeny,
        /// tenylegesen lejto hagot/nyerget), a folytonos verzio helyesen
        /// "Ocean"-kent zarta le mindkettot. Ez NEM hiba, hanem a mar
        /// dokumentalt LOD-fuggetlen modell KOVETKEZETES alkalmazasa
        /// finomabb felbontason - a kontinuus reteg ATVESZI a topologiai
        /// dontes szerepet a MEGJELENITETT halozatra nezve, a durva reteg
        /// tovabbra is a FORRAS-kivalasztashoz es a claimed-alapu dendritikus
        /// osszefolyashoz kell.
        ///
        /// TISZTASAG/DETERMINIZMUS: az eredmeny kizarolag a forras-
        /// poziciotol, a `claimed` bemeneti allapottol es a (bit-egzakt)
        /// elevaciofuggvenytol fugg - nincs rejtett allapot; a jelolt-iranyok
        /// es a flood-fill szomszedsagi sorrendje rogzitett, dontetlennel a
        /// determinisztikus (Counter-alapu beszurasi sorrend) `SortedSet`
        /// dont, pontosan mint `FindLocalSpillway`-ben.
        /// </summary>
        public static ContinuousRiverPath TraceRiverPathContinuous(
            ulong worldSeed, (double X, double Y, double Z)[] seeds, double seaLevel,
            TileId source, int sourceIndex, int fineDepth,
            Dictionary<TileId, ClaimedTileInfo> claimed,
            double stepMeters = DefaultContinuousStepMeters,
            double sensingRadiusMeters = DefaultContinuousSensingRadiusMeters,
            int ringDirections = DefaultContinuousRingDirections,
            double escapeCellMeters = DefaultContinuousEscapeCellMeters,
            int escapeNodeBudget = DefaultContinuousEscapeNodeBudget,
            long maxSteps = DefaultContinuousMaxSteps,
            int visitedGridLevel = DefaultVisitedGridLevel,
            System.Threading.CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            int fineLevel = source.Level + fineDepth;
            source.GetUV(out uint su, out uint sv);
            TileId fineSource = TileId.FromFaceLevelUV(source.Face, fineLevel, su << fineDepth, sv << fineDepth);
            TileGeometry.ToPosition(fineSource, out double sx, out double sy, out double sz);

            var result = new ContinuousRiverPath { SourceIndex = sourceIndex };
            (double X, double Y, double Z) current = (sx, sy, sz);
            result.Points.Add(current);

            // HUROK-VEDELEM (code-review-ban feltart hianyossag, 2026-09-06):
            // a durva `TraceRiverPath`/`FindLocalSpillway` egy `pathVisited`
            // halmazzal strukturalisan kizarja, hogy a folyo visszalepjen
            // sajat korabban bejart utjara - a folytonos valtozat ELSO
            // verzioja ezt NEM oroktolte (a FindContinuousLocalSpillway
            // sajat `visited` halmaza csak az EGY escape-hivas LOKALIS
            // racsara vonatkozott, nem a folyo teljes utjara). Mivel
            // folytonos terben nincs egzakt tile-egyenloseg, egy FINOM
            // (level=`visitedGridLevel`, alapertelmezetten kb. 14 m/tile)
            // racsra kepezzuk le minden bejart pontot - ez eleg finom ahhoz,
            // hogy ket KULONBOZO, kozeli lepest ne kezeljen tevesen
            // azonosnak, de eleg durva ahhoz, hogy a folyo SAJAT medret
            // (amit korabban mar bejart) megbizhatoan felismerje.
            var pathVisited = new HashSet<TileId> { TileGeometry.FromPosition(current.X, current.Y, current.Z, visitedGridLevel) };

            double stepAngular = stepMeters / PlanetConstants.RadiusMeters;
            // Az erzekelesi sugar SOSEM lehet kisebb, mint a tenyleges
            // lepeskoz - kulonben az irany-erzekeles a lepeskoznel kisebb
            // hullamhosszu zajra is erzekeny lenne, ami PONTOSAN az a
            // lengeshiba, amit az erzekelesi sugar bevezetese eredetileg
            // megoldott (ld. osztaly-doksi, "LATOTAV A ZAJTOL FUGGETLENUL").
            // `sensingRadiusMeters` nincs kulon Inspector-mezokent
            // exponalva (csak `stepMeters`), de MERT `stepMeters` IGEN, egy
            // felhasznaloi ertek (pl. 1000m) `DefaultContinuousSensingRadiusMeters`
            // (500m) fole vihetne a lepeskozt e nelkul a Math.Max nelkul.
            double sensingAngular = Math.Max(stepAngular, sensingRadiusMeters / PlanetConstants.RadiusMeters);
            double elev = ElevationAtPosition(worldSeed, current.X, current.Y, current.Z, seeds);

            for (long step = 0; step < maxSteps; step++)
            {
                cancellation.ThrowIfCancellationRequested();
                // A ciklus teteje: EZ az a pozicio, amit a `claimed`
                // ellenorzes lat (ld. ClaimCheckIndices doksija). A
                // rogzites a tengerszint-ellenorzes ELOTT tortenik, hogy az
                // "Ocean"-nal zarult nyomvonalnal is teljes legyen a lista.
                result.ClaimCheckIndices.Add(result.Points.Count - 1);

                if (elev < seaLevel)
                {
                    result.Termination = TerminationReason.Ocean;
                    return result;
                }

                if (claimed != null && step > 0)
                {
                    TileId fineTile = TileGeometry.FromPosition(current.X, current.Y, current.Z, fineLevel);
                    if (claimed.TryGetValue(fineTile, out ClaimedTileInfo owner))
                    {
                        // A megszakado folyo utolso pontjat PONTOSAN a
                        // befogado folyo tenyleges pontjara zarjuk (ld.
                        // ClaimedTileInfo doksi) - enelkul a ket vonal csak
                        // "ugyanabban a durva fine-tile-ban" erne veget,
                        // vizualisan rest hagyva a talalkozasnal.
                        result.Points.Add(owner.Position);
                        result.MergedIntoRiverIndex = owner.RiverIndex;
                        result.Termination = TerminationReason.Merged;
                        return result;
                    }
                }

                GetTangentBasis(current, out (double X, double Y, double Z) t1, out (double X, double Y, double Z) t2);

                // Csak akkor lepunk, ha a jelolt-korben van SZIGORUAN a
                // jelenlegi pontnal alacsonyabb pont - ez a donto elteres a
                // korabbi ("korozon beluli relative legjobb") valtozathoz
                // kepest, ld. osztaly-doksi.
                double bestElev = elev;
                double bestDx = 0.0, bestDy = 0.0;
                bool found = false;
                for (int k = 0; k < ringDirections; k++)
                {
                    double angle = 2.0 * Math.PI * k / ringDirections;
                    double dx = Math.Cos(angle), dy = Math.Sin(angle);
                    (double X, double Y, double Z) sensed = StepInTangentDirection(current, t1, t2, dx, dy, sensingAngular);
                    double sensedElev = ElevationAtPosition(worldSeed, sensed.X, sensed.Y, sensed.Z, seeds);
                    if (sensedElev < bestElev)
                    {
                        bestElev = sensedElev;
                        bestDx = dx;
                        bestDy = dy;
                        found = true;
                    }
                }

                if (found)
                {
                    current = StepInTangentDirection(current, t1, t2, bestDx, bestDy, stepAngular);
                    elev = ElevationAtPosition(worldSeed, current.X, current.Y, current.Z, seeds);
                    result.Points.Add(current);
                    pathVisited.Add(TileGeometry.FromPosition(current.X, current.Y, current.Z, visitedGridLevel));
                    continue;
                }

                var escape = FindContinuousLocalSpillway(
                    worldSeed, seeds, current, elev, escapeCellMeters,
                    escapeNodeBudget, pathVisited, visitedGridLevel, cancellation);
                if (escape == null)
                {
                    result.Termination = TerminationReason.Pit;
                    return result;
                }

                foreach ((double X, double Y, double Z) p in escape.Value.Path)
                {
                    result.Points.Add(p);
                    pathVisited.Add(TileGeometry.FromPosition(p.X, p.Y, p.Z, visitedGridLevel));
                }
                current = escape.Value.Path[escape.Value.Path.Count - 1];
                elev = escape.Value.SpillwayElevation;
            }

            result.Termination = TerminationReason.MaxSteps;
            return result;
        }

        /// <summary>
        /// Kontinuus (nem TileId-racshoz kotott) valtozata a <see
        /// cref="FindLocalSpillway"/>-nek: lokalis, korlatozott csomopont-
        /// szamu priority-flood a `pit` korul, egy IDEIGLENES, a `pit`
        /// pontban rogzitett erinto-sik (i,j) egeszrbacsan (8-szomszedos).
        /// A racs cellamerete `cellMeters` (NAGYOBB, mint a kirajzolasi
        /// lepeskoz - ld. TraceRiverPathContinuous doksi, miert). A racs
        /// origoja es tangens-bazisa a PIT pontjahoz kotott es rogzitett a
        /// teljes flood-fill alatt - lokalis (tipikusan legfeljebb nehany
        /// szaz km-es) teruletre ez a sik kozelites elhanyagolhato torzitast
        /// ad, a visszaadott pontok pedig MINDIG egzaktul a gombre vannak
        /// normalizalva (ld. StepInTangentDirection).
        ///
        /// HUROK-VEDELEM (code-review-ban feltart hianyossag, potlva
        /// 2026-09-06): `pathVisited` a HIVO teljes eddigi utja (finom
        /// racsra kepezve, ld. TraceRiverPathContinuous) - a kereses SEM
        /// nem terjeszkedik bele MAR bejart cellaba, SEM nem fogadja el
        /// tulcsordulasi pontkent, PONTOSAN ugyanaz a mintaz, mint a durva
        /// `FindLocalSpillway`-ben. Enelkul a folyo elmeletileg visszater-
        /// hetne egy korabban mar bejart, KESOBB ismet elerheto (alacsonyabb
        /// mint az AKTUALIS pit, de nem alacsonyabb mint amikor eloszor
        /// bejartak) pontra, hurkot/ismetlodest okozva a kirajzolt vonalban,
        /// vagy szelso esetben a `maxSteps` biztonsagi korlatig futva
        /// termeszetes Ocean/Pit helyett.
        /// </summary>
        private static (List<(double X, double Y, double Z)> Path, double SpillwayElevation)? FindContinuousLocalSpillway(
            ulong worldSeed, (double X, double Y, double Z)[] seeds,
            (double X, double Y, double Z) pit, double pitElevation,
            double cellMeters, int nodeBudget,
            HashSet<TileId> pathVisited, int visitedGridLevel,
            System.Threading.CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            GetTangentBasis(pit, out (double X, double Y, double Z) t1, out (double X, double Y, double Z) t2);
            double cellAngular = cellMeters / PlanetConstants.RadiusMeters;

            (double X, double Y, double Z) CellPos(int i, int j) => StepInTangentDirection(pit, t1, t2, i, j, cellAngular);
            double CellElev(int i, int j)
            {
                (double X, double Y, double Z) p = CellPos(i, j);
                return ElevationAtPosition(worldSeed, p.X, p.Y, p.Z, seeds);
            }
            bool IsPathVisited(int i, int j)
            {
                (double X, double Y, double Z) p = CellPos(i, j);
                return pathVisited.Contains(TileGeometry.FromPosition(p.X, p.Y, p.Z, visitedGridLevel));
            }

            var visited = new HashSet<(int I, int J)> { (0, 0) };
            var parent = new Dictionary<(int I, int J), (int I, int J)?> { [(0, 0)] = null };
            var queue = new SortedSet<(double FilledElevation, long Counter)>();
            var counterToNode = new Dictionary<long, (int I, int J)>();
            long counter = 0;
            queue.Add((pitElevation, counter));
            counterToNode[counter] = (0, 0);
            counter++;

            (int Di, int Dj)[] neighbors8 =
            {
                (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
            };

            int expanded = 0;
            while (queue.Count > 0 && expanded < nodeBudget)
            {
                cancellation.ThrowIfCancellationRequested();
                var top = queue.Min;
                queue.Remove(top);
                expanded++;
                (int I, int J) node = counterToNode[top.Counter];
                double rawElev = node == (0, 0) ? pitElevation : CellElev(node.I, node.J);

                if (node != (0, 0) && rawElev < pitElevation && !IsPathVisited(node.I, node.J))
                {
                    var idxPath = new List<(int I, int J)>();
                    (int I, int J)? cur = node;
                    while (cur.HasValue) { idxPath.Add(cur.Value); cur = parent[cur.Value]; }
                    idxPath.Reverse();
                    var posPath = new List<(double X, double Y, double Z)>(idxPath.Count);
                    foreach ((int I, int J) ij in idxPath) posPath.Add(CellPos(ij.I, ij.J));
                    return (posPath, rawElev);
                }

                foreach ((int Di, int Dj) in neighbors8)
                {
                    (int I, int J) nb = (node.I + Di, node.J + Dj);
                    if (visited.Contains(nb) || IsPathVisited(nb.I, nb.J)) continue;
                    visited.Add(nb);
                    double nbRaw = CellElev(nb.I, nb.J);
                    double nbFilled = Math.Max(nbRaw, top.FilledElevation);
                    parent[nb] = node;
                    queue.Add((nbFilled, counter));
                    counterToNode[counter] = nb;
                    counter++;
                }
            }
            return null;
        }

        /// <summary>
        /// A teljes kontinuus halozat: minden forrasra <see
        /// cref="TraceRiverPathContinuous"/>, MEGOSZTOTT `claimed` finom-
        /// tile terkeppel (mint <see cref="BuildRiverNetworkFromSources"/>),
        /// hogy a dendritikus osszefolyas (ld. osztaly-doksi) a folytonos
        /// rétegen is megmaradjon. SZEKVENCIALISAN fut folyononkent (nem
        /// parhuzamosan), MERT a claimed map megosztott irasa/olvasasa
        /// parhuzamositva versenyhelyzetet (es ezzel determinizmus-serulest)
        /// okozna - a hivo felelossege, hogy hatterszalon (nem a fo szalon)
        /// inditsa.
        /// </summary>
        public static List<ContinuousRiverPath> BuildContinuousRiverNetworkFromSources(
            ulong worldSeed, (double X, double Y, double Z)[] seeds, double seaLevel,
            IReadOnlyList<TileId> sources, int fineDepth,
            double stepMeters = DefaultContinuousStepMeters,
            double sensingRadiusMeters = DefaultContinuousSensingRadiusMeters,
            int ringDirections = DefaultContinuousRingDirections,
            double escapeCellMeters = DefaultContinuousEscapeCellMeters,
            int escapeNodeBudget = DefaultContinuousEscapeNodeBudget,
            long maxSteps = DefaultContinuousMaxSteps,
            System.Threading.CancellationToken cancellation = default)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            cancellation.ThrowIfCancellationRequested();
            var claimed = new Dictionary<TileId, ClaimedTileInfo>();
            var rivers = new List<ContinuousRiverPath>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                ContinuousRiverPath river = TraceRiverPathContinuous(
                    worldSeed, seeds, seaLevel, sources[i], i, fineDepth, claimed,
                    stepMeters, sensingRadiusMeters, ringDirections, escapeCellMeters, escapeNodeBudget, maxSteps,
                    cancellation: cancellation);

                int fineLevel = sources[i].Level + fineDepth;
                foreach ((double X, double Y, double Z) p in river.Points)
                {
                    TileId t = TileGeometry.FromPosition(p.X, p.Y, p.Z, fineLevel);
                    if (!claimed.ContainsKey(t)) claimed[t] = new ClaimedTileInfo(i, p);
                }
                rivers.Add(river);
            }
            return rivers;
        }

        /// <summary>
        /// A <see cref="BuildContinuousRiverNetworkFromSources"/> PÁRHUZAMOS
        /// változata, BITRE AZONOS kimenettel.
        ///
        /// MIÉRT LEHETSÉGES. A `claimed` térkép a nyomvonal-követésben
        /// KIZÁRÓLAG a MEGÁLLÁST befolyásolja (ld.
        /// <see cref="TraceRiverPathContinuous"/> "Merged" ága): a lépésirányt
        /// sosem. Egy folyó útvonala tehát a saját forrásából, a domborzatból
        /// és a tengerszintből egyértelműen következik - a többi folyótól
        /// FÜGGETLENÜL. Ezért:
        ///
        ///   1. minden folyót PÁRHUZAMOSAN, `claimed` NÉLKÜL végigkövetünk;
        ///   2. majd FORRÁS-SORRENDBEN (növekvő index) végigmegyünk rajtuk, és
        ///      mindegyiket az első olyan pontnál elvágjuk, ahol egy KISEBB
        ///      indexű folyó már lefoglalta a finom tile-t - ugyanazt a pontot
        ///      és ugyanazt a `MergedIntoRiverIndex`-et adva, mint a
        ///      szekvenciális változat.
        ///
        /// A 2. fázis szigorúan sorrendben fut, tehát a lefoglalási sorrend -
        /// és így a teljes dendritikus fa - VÁLTOZATLAN. Az 1. fázisban a
        /// nyomvonal a beolvadási ponton TÚL is folytatódik; azokat a pontokat
        /// a csonkolás eldobja, mielőtt bármit lefoglalnának.
        ///
        /// MIÉRT KELL. A szekvenciális változat 12 folyóra 6-10 másodperc, és
        /// EZ tartotta a forrásszámot (`DefaultSourceTopK`) 12-n - amiből a
        /// felhasználói visszajelzés szerint "ritkák a folyók" és "nincs tree
        /// alakzat, sosem ér bele egyik a másikba" következett: 12 egymástól
        /// távoli forrás nyomvonalai gyakorlatilag soha nem találkoznak.
        /// </summary>
        public static List<ContinuousRiverPath> BuildContinuousRiverNetworkFromSourcesParallel(
            ulong worldSeed, (double X, double Y, double Z)[] seeds, double seaLevel,
            IReadOnlyList<TileId> sources, int fineDepth,
            double stepMeters = DefaultContinuousStepMeters,
            double sensingRadiusMeters = DefaultContinuousSensingRadiusMeters,
            int ringDirections = DefaultContinuousRingDirections,
            double escapeCellMeters = DefaultContinuousEscapeCellMeters,
            int escapeNodeBudget = DefaultContinuousEscapeNodeBudget,
            long maxSteps = DefaultContinuousMaxSteps,
            System.Threading.CancellationToken cancellation = default)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            cancellation.ThrowIfCancellationRequested();

            // 1. FÁZIS: minden folyó ÖNÁLLÓAN, `claimed` nélkül. Tiszta
            // függvények, megosztott állapot nélkül - a sorrend nem számít.
            var traced = new ContinuousRiverPath[sources.Count];
            var parallelOptions = new System.Threading.Tasks.ParallelOptions
            {
                CancellationToken = cancellation,
            };
            System.Threading.Tasks.Parallel.For(0, sources.Count, parallelOptions, i =>
            {
                // URES `claimed`: a kovetes ilyenkor sosem all meg
                // "Merged"-kent, tehat a teljes nyomvonalat megkapjuk. (A
                // parameter nem nullable, es a nyomvonal-koveto CSAK OLVASSA
                // ezt a szotarat - a lefoglalas a 2. fazisban tortenik.)
                traced[i] = TraceRiverPathContinuous(
                    worldSeed, seeds, seaLevel, sources[i], i, fineDepth,
                    new Dictionary<TileId, ClaimedTileInfo>(),
                    stepMeters, sensingRadiusMeters, ringDirections,
                    escapeCellMeters, escapeNodeBudget, maxSteps,
                    DefaultVisitedGridLevel, cancellation);
            });

            // 2. FÁZIS: csonkolás FORRÁS-SORRENDBEN - ez reprodukálja a
            // szekvenciális `claimed` szemantikát.
            var claimed = new Dictionary<TileId, ClaimedTileInfo>();
            var rivers = new List<ContinuousRiverPath>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                ContinuousRiverPath full = traced[i];
                int fineLevel = sources[i].Level + fineDepth;

                var river = new ContinuousRiverPath { SourceIndex = i };
                int mergeAt = -1;
                ClaimedTileInfo mergeOwner = default;
                // CSAK azokon a pontokon ellenorzunk, ahol a szekvencialis
                // koveto is ellenorzott volna (ld. ClaimCheckIndices) - az
                // elso (step == 0) kihagyva, ahogy ott is.
                for (int c = 1; c < full.ClaimCheckIndices.Count; c++)
                {
                    int index = full.ClaimCheckIndices[c];
                    if ((uint)index >= (uint)full.Points.Count) break;
                    (double X, double Y, double Z) point = full.Points[index];
                    TileId fineTile = TileGeometry.FromPosition(point.X, point.Y, point.Z, fineLevel);
                    if (claimed.TryGetValue(fineTile, out ClaimedTileInfo owner))
                    {
                        mergeAt = index;
                        mergeOwner = owner;
                        break;
                    }
                }

                if (mergeAt >= 0)
                {
                    // A szekvencialis ag a MAR FELVETT pontokat megtartja
                    // (Points[0..mergeAt]), majd a befogado folyo TENYLEGES
                    // pontjat fuzi a vegere - igy nem marad res a
                    // talalkozasnal.
                    for (int k = 0; k <= mergeAt; k++) river.Points.Add(full.Points[k]);
                    river.Points.Add(mergeOwner.Position);
                    river.MergedIntoRiverIndex = mergeOwner.RiverIndex;
                    river.Termination = TerminationReason.Merged;
                }
                else
                {
                    river.Points.AddRange(full.Points);
                    river.ClaimCheckIndices.AddRange(full.ClaimCheckIndices);
                    river.Termination = full.Termination;
                }

                foreach ((double X, double Y, double Z) p in river.Points)
                {
                    TileId t = TileGeometry.FromPosition(p.X, p.Y, p.Z, fineLevel);
                    if (!claimed.ContainsKey(t)) claimed[t] = new ClaimedTileInfo(i, p);
                }
                rivers.Add(river);
            }
            return rivers;
        }

        /// <summary>
        /// M9/M7 "vonal-szélesség nem korrelál a vízhozammal" (backlog,
        /// 2026-09-06): minden folyóra egy "vízhozam-súlyt" számol a
        /// dendritikus összefolyás-fa (<see
        /// cref="ContinuousRiverPath.MergedIntoRiverIndex"/>) alapján - egy
        /// forrás önmagában 1 egységet ér, egy összefolyásnál a beleolvadó
        /// folyó TELJES (már saját maga is felhalmozott) súlya hozzáadódik
        /// a befogadóéhoz. A torkolathoz közeli, sok tributary-t összegyűjtő
        /// szakaszok így arányosan nagyobb súlyt kapnak, mint egy elszigetelt
        /// forrás-ág - a hívó (Viewer) ezt tipikusan sqrt(súly)-lyal
        /// arányos vonal-szélességre fordítja (a valós hidrológiában is
        /// megfigyelt szélesség~vízhozam^0.5 durva közelítése, Leopold-
        /// Maddock "at-a-station hydraulic geometry"). I3-kompatibilis: a
        /// súly a TÉNYLEGES összefolyás-struktúrából jön, nem dekoratív
        /// becslés.
        ///
        /// FONTOS: a `rivers` listát PONTOSAN abban a sorrendben kell
        /// megadni, ahogy a <see cref="BuildContinuousRiverNetworkFromSources"/>
        /// visszaadta (SourceIndex szerint növekvő) - a
        /// `MergedIntoRiverIndex` MINDIG egy KISEBB indexű folyóra mutat
        /// (mert a `claimed` map csak MÁR bejárt folyóktól fogad el
        /// bejegyzést), ezért a FORDÍTOTT (utolsótól-elsőig) bejárás
        /// garantáltan helyesen összegzi a többszintű (lánc-szerű,
        /// A&lt;-B&lt;-C) összefolyásokat is - ha előre haladnánk, egy
        /// korábban feldolgozott szülő nem kapná meg a később feldolgozott
        /// unoka-ág súlyát.
        /// </summary>
        public static int[] ComputeDischargeWeights(IReadOnlyList<ContinuousRiverPath> rivers)
        {
            var weights = new int[rivers.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = 1;
            for (int i = rivers.Count - 1; i >= 0; i--)
            {
                int parent = rivers[i].MergedIntoRiverIndex;
                if (parent >= 0 && parent < weights.Length)
                    weights[parent] += weights[i];
            }
            return weights;
        }

        private static double AngularDistance((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
        }

        /// <summary>Két, a `p` egységvektorra merőleges, egymásra is merőleges érintő-irány (Gram-Schmidt egy tetszőleges referenciából).</summary>
        private static void GetTangentBasis(
            (double X, double Y, double Z) p, out (double X, double Y, double Z) t1, out (double X, double Y, double Z) t2)
        {
            (double X, double Y, double Z) reference = Math.Abs(p.Z) < 0.9 ? (0.0, 0.0, 1.0) : (0.0, 1.0, 0.0);
            double dot = p.X * reference.X + p.Y * reference.Y + p.Z * reference.Z;
            double rx = reference.X - dot * p.X, ry = reference.Y - dot * p.Y, rz = reference.Z - dot * p.Z;
            double len = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            t1 = (rx / len, ry / len, rz / len);
            t2 = (p.Y * t1.Z - p.Z * t1.Y, p.Z * t1.X - p.X * t1.Z, p.X * t1.Y - p.Y * t1.X); // p x t1
        }

        /// <summary>Kis szögű lépés a `p` egységgömb-ponttól a (t1,t2) érintő-bázisban a (dx,dy) irányba, `angularStep` radián nagyságban - utána egzakt vissza-normalizálás a gömbre.</summary>
        private static (double X, double Y, double Z) StepInTangentDirection(
            (double X, double Y, double Z) p, (double X, double Y, double Z) t1, (double X, double Y, double Z) t2,
            double dx, double dy, double angularStep)
        {
            double nx = p.X + angularStep * (dx * t1.X + dy * t2.X);
            double ny = p.Y + angularStep * (dx * t1.Y + dy * t2.Y);
            double nz = p.Z + angularStep * (dx * t1.Z + dy * t2.Z);
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return (nx / len, ny / len, nz / len);
        }

        /// <summary>
        /// A teljes lánc: forrás-kiválasztás -> nyomvonal-követés minden
        /// forrásra, dendritikus egyesüléssel (a `claimed` térkép megosztott
        /// az összes forrás között). A hívó (pl. a viewer) az elevációt/
        /// csapadékot/óceán-mezőt/tengerszintet a MÁR meglévő <see
        /// cref="Climate.MoisturePrecipitation.Compute"/>-ból adja (nincs
        /// duplikált számítás).
        /// </summary>
        public static List<RiverPath> BuildRiverNetwork(
            ulong worldSeed, (double X, double Y, double Z)[] seeds,
            Dictionary<TileId, double> elevField, Dictionary<TileId, double> precipField,
            Dictionary<TileId, bool> isOcean, double seaLevel,
            int fineDepth = DefaultFineDepth, int topK = DefaultSourceTopK,
            double minElevAboveSeaM = DefaultMinElevAboveSeaM,
            double precipPercentile = DefaultPrecipPercentile,
            int maxSteps = DefaultMaxSteps, int escapeNodeBudget = DefaultEscapeNodeBudget)
        {
            List<TileId> sources = SelectRiverSources(
                elevField, precipField, isOcean, seaLevel, topK, minElevAboveSeaM, precipPercentile);
            return BuildRiverNetworkFromSources(worldSeed, seeds, seaLevel, sources, fineDepth, maxSteps, escapeNodeBudget);
        }

        /// <summary>
        /// ND-124 (A) - ugyanaz, mint <see cref="BuildRiverNetwork"/>, de a
        /// forrásokat <see cref="SelectRiverSourcesPerBasin"/> választja:
        /// vízgyűjtőnkénti kvótával, nem globális top-K-val. A
        /// priority-flood-ot (a vízgyűjtő-szegmentáláshoz) MAGA számolja
        /// ugyanabból az elevációs/óceán-mezőből, amit kapott - így a
        /// vízgyűjtők garantáltan ugyanahhoz a világállapothoz tartoznak,
        /// mint a források.
        ///
        /// MÉRVE (seed 0xA7C944210000, level 6, fineDepth 4, folytonos
        /// követő) - a globális top-K-hoz képest:
        ///
        ///   globális top-K 48 :  8 összefolyás (17%), max vízhozam-súly 2,   6,5 s
        ///   medence  6×8 (48) : 21 összefolyás (44%), max vízhozam-súly 6,  19,9 s
        ///   medence 12×6 (72) : 27 összefolyás (38%), max vízhozam-súly 5,  26,0 s
        ///   medence 16×6 (96) : 40 összefolyás (42%), max vízhozam-súly 6,  32,3 s
        ///
        /// A globális top-K-nál EGYETLEN folyó sincs 3-as vagy nagyobb
        /// vízhozam-súllyal (nincs kétszintű hálózat); a 16×6-nál 13 van.
        /// A költség ~5× - a medence-források a kontinens BELSEJÉBEN
        /// indulnak, ahol sokkal több a pit-escape. Háttérszálon fut, a
        /// Build()-et nem blokkolja.
        /// </summary>
        public static List<RiverPath> BuildRiverNetworkPerBasin(
            ulong worldSeed, (double X, double Y, double Z)[] seeds,
            Dictionary<TileId, double> elevField, Dictionary<TileId, double> precipField,
            Dictionary<TileId, bool> isOcean, double seaLevel,
            int fineDepth = DefaultFineDepth,
            int basinCount = DefaultSourceBasinCount,
            int sourcesPerBasin = DefaultSourcesPerBasin,
            double minElevAboveSeaM = DefaultMinElevAboveSeaM,
            double minSeparationMeters = DefaultSourceSeparationMeters,
            int maxSteps = DefaultMaxSteps, int escapeNodeBudget = DefaultEscapeNodeBudget)
        {
            FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(elevField, isOcean);
            List<TileId> sources = SelectRiverSourcesPerBasin(
                elevField, precipField, isOcean, flood.Parent, seaLevel,
                basinCount, sourcesPerBasin, minElevAboveSeaM, minSeparationMeters);
            return BuildRiverNetworkFromSources(worldSeed, seeds, seaLevel, sources, fineDepth, maxSteps, escapeNodeBudget);
        }

        /// <summary>
        /// Ugyanaz, mint <see cref="BuildRiverNetwork"/>, de a forrás-listát
        /// KÉSZEN kapja (nincs <see cref="SelectRiverSources"/>-hívás) - a
        /// hívó tesztelhetőség/rugalmasság miatt maga adhatja meg a
        /// forrásokat (pl. a Python-referencia rögzített forrás-listájával
        /// bitre egyező eredmény ellenőrzéséhez, a csapadék-alapú
        /// kiválasztás cross-platform toleranciájától FÜGGETLENÜL - ld.
        /// osztály-doksi).
        /// </summary>
        public static List<RiverPath> BuildRiverNetworkFromSources(
            ulong worldSeed, (double X, double Y, double Z)[] seeds, double seaLevel,
            IReadOnlyList<TileId> sources,
            int fineDepth = DefaultFineDepth,
            int maxSteps = DefaultMaxSteps, int escapeNodeBudget = DefaultEscapeNodeBudget)
        {
            var elevCache = new ElevationCache(worldSeed, seeds);
            var claimed = new Dictionary<TileId, int>();
            var rivers = new List<RiverPath>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                RiverPath river = TraceRiverPath(
                    worldSeed, seeds, seaLevel, sources[i], i, fineDepth, claimed, maxSteps, escapeNodeBudget, elevCache);
                foreach (TileId t in river.Path)
                    if (!claimed.ContainsKey(t)) claimed[t] = i;
                rivers.Add(river);
            }
            return rivers;
        }
    }
}
