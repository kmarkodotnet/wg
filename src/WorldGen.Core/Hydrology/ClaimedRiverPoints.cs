using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Hydrology
{
    /// <summary>Egy már lefoglalt folyópont: melyik folyóé, és hol van pontosan.</summary>
    public readonly struct ClaimedRiverPoint
    {
        public readonly int RiverIndex;
        public readonly (double X, double Y, double Z) Position;

        public ClaimedRiverPoint(int riverIndex, (double X, double Y, double Z) position)
        {
            RiverIndex = riverIndex;
            Position = position;
        }
    }

    /// <summary>
    /// A dendritikus összefolyás térbeli indexe - VALÓDI távolságvizsgálattal
    /// (ND-186, az ND-180 (2) hibaosztály javítása).
    ///
    /// A RÉGI SZEMANTIKA ÉS A HIBÁJA. Korábban egy
    /// `Dictionary&lt;TileId, ClaimedTileInfo&gt;` tárolta EGY pontot egy TELJES
    /// finom tile-ra (`source.Level + fineDepth`, tipikusan level 9 =
    /// 13-18 km), és a beleolvadó folyót erre az egy pontra zártuk. Ebből két
    /// független hiba következett, MINDKETTŐ mérve:
    ///
    ///   - ÖSSZEFOLYÁSI TELEPORT: a záróél akár a teljes cellaátmérő lehetett.
    ///     A t=0 hálózatban a legnagyobb mért záróél **24,113 km** (29. ág) -
    ///     a képen ez egy több kilométeres, terepet átvágó ugrás.
    ///   - RÁCS-LOTTÓ: az összefolyás nem a TÁVOLSÁGON múlt, hanem azon, hogy
    ///     a két nyomvonal ugyanabba a cellába esik-e. A Python-orákulumban
    ///     mérve: két, egymástól 2968 m-re futó PÁRHUZAMOS meder NEM
    ///     kapcsolódott össze, mert a cellahatár épp közéjük esett.
    ///
    /// AZ ÚJ SZEMANTIKA. Cellánként a lefoglalt pontok LISTÁJA egy
    /// <see cref="CubeFaceLattice"/> bucket-rácson, és az összefolyás CSAK
    /// akkor történik meg, ha van `mergeRadiusMeters`-en BELÜLI valódi
    /// lefoglalt pont; a zárás a LEGKÖZELEBBIRE történik. A záróél hossza
    /// így STRUKTURÁLISAN korlátos (&lt;= `mergeRadiusMeters`), nem mérési
    /// tapasztalat.
    ///
    /// LEFEDETTSÉGI ÉRVELÉS (miért elég 9 próbapont). A keresés a kérdezett
    /// pont körül 9 pozíciót vetít cellára: a pontot magát, a négy
    /// tengely-irányú és a négy átlós eltolást `mergeRadiusMeters`
    /// távolságban. Minden `r` sugáron belüli pont a 2r x 2r-es érintősíki
    /// négyzetben van, a 9 próbapont pedig r osztású 3x3-as rács ezen a
    /// négyzeten. Ha a cella mérete legalább `r`, akkor minden olyan cella,
    /// ami a négyzetet metszi, tartalmazza a 9 próbapont valamelyikét (egy
    /// &gt;= r hosszú intervallum, ami [-r, r]-t metszi, tartalmazza a
    /// {-r, 0, r} valamelyikét). Ezért a rácsszintet NEM a hívó adja meg,
    /// hanem a toleranciából SZÁMOLJUK (<see cref="LatticeLevelFor"/>): a
    /// legfinomabb olyan szintet, aminek a GARANTÁLT legkisebb cellája még
    /// legalább `mergeRadiusMeters` - így a lefedés a lap minden pontján áll.
    /// A próbapontokat VALÓDI 3D pozícióból vetítjük, ezért a lap-határokon
    /// át is helyesen működik (i ± 1 index-aritmetika ott elhasalt volna).
    ///
    /// DETERMINIZMUS. A találatok közül a legkisebb (távolság, beszúrási
    /// sorszám) pár nyer - a sorszám miatt a döntetlen is egyértelmű, a
    /// szótár-bejárási sorrendtől függetlenül. A példány NEM szálbiztos: a
    /// hívó (szekvenciális hálózatépítés, illetve a párhuzamos változat
    /// commit-kapuja) felelős a kizárólagos hozzáférésért.
    ///
    /// Python-referencia: tools/reference/river_continuous_ref.py
    /// (`ClaimedRiverPoints`).
    /// </summary>
    public sealed class ClaimedRiverPoints
    {
        /// <summary>
        /// Az összefolyás térbeli toleranciája méterben.
        ///
        /// MÉRÉSSEL eldöntve (ND-186), nem becsülve. A valódi t=0 hálózaton
        /// (96 ág, level 5) söpörve: **100 m → 14** összefolyás, **250 m → 14**,
        /// **500 m → 18**, **2000 m → 18**. A szám tehát **500 m-nél telítődik**:
        /// fölötte már csak a záróél nyúlik, új összefolyás nem keletkezik.
        ///
        /// Ez egybeesik egy FIZIKAI érvvel is: 500 m a követő saját
        /// érzékelési sugara (<see cref="RiverPathTracing.DefaultContinuousSensingRadiusMeters"/>),
        /// vagyis az a lépték, amin a modell a domborzatot megítéli - két, ennél
        /// közelebb futó nyomvonal a modell saját felbontásán UGYANABBAN a
        /// mederben van. Kimondott következmény: az összefolyási záróél így
        /// legfeljebb 500 m (mérve: 0,499 km), nem 100 m - de ez a tracer
        /// érzékelési léptékén belül van, és a javítás előtti 24,113 km-hez
        /// képest nagyságrendekkel kisebb.
        /// </summary>
        public const double DefaultMergeRadiusMeters = 500.0;

        private readonly int _level;
        private readonly Dictionary<long, List<Entry>> _cells = new Dictionary<long, List<Entry>>();
        private long _order;

        private readonly struct Entry
        {
            public readonly (double X, double Y, double Z) Position;
            public readonly int RiverIndex;
            public readonly long Order;

            public Entry((double X, double Y, double Z) position, int riverIndex, long order)
            {
                Position = position;
                RiverIndex = riverIndex;
                Order = order;
            }
        }

        /// <summary>
        /// A bucket-rács szintje a toleranciából SZÁMOLVA: a legfinomabb olyan
        /// szint, aminek a GARANTÁLT legkisebb cellája még legalább
        /// `mergeRadiusMeters` - ez pontosan a 9 próbapontos lefedés
        /// feltétele (ld. osztály-doksi). Kettőzéssel számol, logaritmus
        /// nélkül: a `Math.Log` nem bitpontos (CLAUDE.md táblázat), és egy
        /// RÁCSSZINT elcsúszása itt más folyóhálózatot adna.
        /// </summary>
        public static int LatticeLevelFor(double mergeRadiusMeters)
        {
            if (!(mergeRadiusMeters > 0.0))
                throw new ArgumentOutOfRangeException(nameof(mergeRadiusMeters));
            // A lefedesi ervelés feltetele `mergeRadiusMeters <= legkisebb cella`.
            // Level 1-nel a garantalt legkisebb cella 3 710 km - ennel nagyobb
            // tolerancia mellett a 9 probapontos kereses CSENDBEN hagyna ki
            // talalatokat, ezert inkabb dobunk.
            if (mergeRadiusMeters > CubeFaceLattice.MinCellMeters(1))
                throw new ArgumentOutOfRangeException(
                    nameof(mergeRadiusMeters),
                    "Az osszefolyasi tolerancia nem lehet nagyobb a legdurvabb bucket-cellanal.");
            int level = 1;
            while (level < CubeFaceLattice.MaxLevel
                   && CubeFaceLattice.MinCellMeters(level + 1) >= mergeRadiusMeters)
            {
                level++;
            }
            return level;
        }

        public ClaimedRiverPoints(double mergeRadiusMeters = DefaultMergeRadiusMeters)
        {
            _level = LatticeLevelFor(mergeRadiusMeters);
            MergeRadiusMeters = mergeRadiusMeters;
        }

        /// <summary>A konstruktorban megadott térbeli összefolyási tolerancia.</summary>
        public double MergeRadiusMeters { get; }

        /// <summary>A használt bucket-rács szintje (diagnosztika).</summary>
        public int LatticeLevel => _level;

        /// <summary>Hány lefoglalt pontot tartalmaz az index (diagnosztika).</summary>
        public long Count => _order;

        /// <summary>Egy folyópont lefoglalása.</summary>
        public void Add((double X, double Y, double Z) position, int riverIndex)
        {
            long key = CubeFaceLattice.KeyFromPosition(position.X, position.Y, position.Z, _level);
            if (!_cells.TryGetValue(key, out List<Entry> bucket))
            {
                bucket = new List<Entry>(4);
                _cells[key] = bucket;
            }
            bucket.Add(new Entry(position, riverIndex, _order));
            _order++;
        }

        /// <summary>
        /// A legközelebbi lefoglalt pont a <see cref="MergeRadiusMeters"/>
        /// sugáron belül. Döntetlennél a KORÁBBI beszúrás nyer.
        /// </summary>
        public bool TryFindNearest(
            (double X, double Y, double Z) position, out ClaimedRiverPoint nearest)
        {
            nearest = default;
            if (_cells.Count == 0) return false;

            double limit = MergeRadiusMeters / PlanetConstants.RadiusMeters;
            SphereWalk.GetTangentBasis(position, out (double X, double Y, double Z) t1, out (double X, double Y, double Z) t2);

            // 9 próbapont: a pont maga, négy tengely-irányú és négy átlós
            // eltolás a tolerancia-sugárban (ld. osztály-doksi lefedési érvelés).
            Span<long> keys = stackalloc long[9];
            int keyCount = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    (double X, double Y, double Z) probe = dx == 0 && dy == 0
                        ? position
                        : SphereWalk.StepInTangentDirection(position, t1, t2, dx, dy, limit);
                    long key = CubeFaceLattice.KeyFromPosition(probe.X, probe.Y, probe.Z, _level);
                    bool seen = false;
                    for (int k = 0; k < keyCount; k++)
                    {
                        if (keys[k] == key) { seen = true; break; }
                    }
                    if (!seen) keys[keyCount++] = key;
                }
            }

            bool found = false;
            double bestDistance = 0.0;
            long bestOrder = 0;
            for (int k = 0; k < keyCount; k++)
            {
                if (!_cells.TryGetValue(keys[k], out List<Entry> bucket)) continue;
                for (int e = 0; e < bucket.Count; e++)
                {
                    Entry entry = bucket[e];
                    double dx = entry.Position.X - position.X;
                    double dy = entry.Position.Y - position.Y;
                    double dz = entry.Position.Z - position.Z;
                    double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (distance > limit) continue;
                    if (!found || distance < bestDistance || (distance == bestDistance && entry.Order < bestOrder))
                    {
                        found = true;
                        bestDistance = distance;
                        bestOrder = entry.Order;
                        nearest = new ClaimedRiverPoint(entry.RiverIndex, entry.Position);
                    }
                }
            }
            return found;
        }
    }
}
