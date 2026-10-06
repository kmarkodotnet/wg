using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Hydrology
{
    /// <summary>
    /// ND-198 (2026-10-06): a tavak VÍZMÉRLEGE.
    ///
    /// A PROBLÉMA, amit megold (felhasználói észrevétel + mérés). A tó eddig
    /// TISZTÁN TOPOGRÁFIAI volt: a <see cref="FlowNetwork.PriorityFlood"/>
    /// minden zárt mélyedést feltölt a kifolyási szintig, és a
    /// <see cref="LakesIceErosion.IdentifyLakes"/> ezt a feltöltést nevezi
    /// tónak — vízmérleg nélkül. MÉRVE (seed 0xA7C944210000, level 8, a
    /// hőmodell csapadékával): 531 látható tóból 227 (42,7%) vízgyűjtőjében
    /// PONTOSAN nulla a csapadék, 474-et (89,3%) semmilyen folyó nem ér el, és
    /// a teljes tó-térfogat 1 967 814 km³ — a Föld tavainak több mint
    /// tízszerese. Egy 3000 m mély, csapadékmentes medence ugyanúgy színültig
    /// telt, mint egy esős hegyvidéki katlan.
    ///
    /// A MODELL. Egy zárt (lefolyástalan) tó szintje ott áll be, ahol a
    /// BEÁRAMLÁS egyenlő a PÁROLGÁSSAL:
    ///
    ///   beáramlás = Σ(vízgyűjtő csapadéka × terület) × lefolyási hányad
    ///               + Σ(tófelszínre hulló csapadék × terület)
    ///   párolgás  = Σ(nyílt vízfelszín párolgási üteme × terület) a TÓ felszínén
    ///
    /// Mindkét oldal ugyanabban a nedvesség-egységben van
    /// (<see cref="Climate.WindPrecipitation.Evaporation"/> adja a párolgást,
    /// és ugyanez a tag táplálja a csapadékot is), ezért a mérleg
    /// dimenziótlan hányadosként értelmes — pontosan úgy, ahogy a csapadék-
    /// küszöbök is percentilisek, nem mm/év (ND-126).
    ///
    /// Mivel a tófelszín a szinttel MONOTON nő, a párolgás is monoton nő, a
    /// beáramlás viszont (a vízgyűjtő adott) jó közelítéssel állandó — tehát
    /// EGYETLEN, jól definiált egyensúlyi szint van. Azt keressük meg: a tó
    /// saját tile-jainak terep-magasságai közül a LEGMAGASABBAT, amelynél a
    /// párolgás még nem haladja meg a beáramlást. Ha már a legalacsonyabb
    /// szint párolgása is több, a tó ELTŰNIK (száraz medence, sós lapos).
    ///
    /// DETERMINIZMUS (I1/I2): csak összeadás és összehasonlítás, nincs
    /// transzcendens függvény, nincs állapot. Minden összegzés
    /// DETERMINISZTIKUSAN RENDEZETT listán fut (a tó tile-jai magasság,
    /// döntetlennél <c>TileId.Value</c> szerint; a vízgyűjtő tile-jai
    /// <c>TileId.Value</c> szerint), tehát a bitpontos eredmény nem függ a
    /// szótárak bejárási sorrendjétől.
    ///
    /// AMIT EZ NEM MODELLEZ: a szezonalitást (a tó egész évben egyensúlyban
    /// van), a felszín alatti beszivárgást és a tó alatti vízrétegek
    /// tárolását. Ezek az ND-198 (c) hatókörébe tartoznak.
    /// </summary>
    public static class LakeWaterBalance
    {
        /// <summary>
        /// A csapadék mekkora hányada jut el LEFOLYÁSKÉNT a tóig (a többit a
        /// szárazföld elpárologtatja, illetve beszivárog). MÉRT kalibráció:
        /// ld. ND-198 — a cél a bolygó tó-térfogatának valószerű nagyságrendje.
        /// </summary>
        public const double DefaultRunoffCoefficient = 0.30;

        /// <summary>
        /// Az eredmény egy tóra: a vízmérleg szerinti felszín-magasság, és
        /// hogy a tó egyáltalán megmarad-e.
        /// </summary>
        public readonly struct BalancedLake
        {
            /// <summary>A <see cref="LakesIceErosion.LakeInfo.Id"/> azonosítója.</summary>
            public readonly int Id;

            /// <summary>A vízmérleg szerinti felszín (≤ a topográfiai kifolyási szint).</summary>
            public readonly double SurfaceElevation;

            /// <summary>Hány tile marad víz alatt ezen a szinten.</summary>
            public readonly int TileCount;

            /// <summary>A számított beáramlás (nedvesség-egység × m²).</summary>
            public readonly double Inflow;

            /// <summary>A számított párolgás a megmaradó felszínen (ugyanabban az egységben).</summary>
            public readonly double Evaporation;

            public BalancedLake(int id, double surfaceElevation, int tileCount, double inflow, double evaporation)
            {
                Id = id;
                SurfaceElevation = surfaceElevation;
                TileCount = tileCount;
                Inflow = inflow;
                Evaporation = evaporation;
            }

            /// <summary>Igaz, ha a mérleg szerint marad tó (legalább egy víz alatti tile).</summary>
            public bool Exists => TileCount > 0;
        }

        /// <summary>
        /// Minden tóra kiszámolja a vízmérleg szerinti szintet.
        ///
        /// A <paramref name="precipitation"/> és az <paramref name="openWaterEvaporation"/>
        /// kulcsai DURVÁBB szinten is lehetnek, mint a terep (a klíma-mező
        /// jellemzően level 5, a terep level 7-8): ilyenkor a tile ősét
        /// keressük meg. Ez tudatos közelítés — a klíma felbontása kisebb,
        /// mint a domborzaté —, és a <paramref name="climateLevel"/> teszi
        /// explicitté.
        /// </summary>
        /// <param name="lakes">A topográfiai tó-detektálás eredménye.</param>
        /// <param name="terrain">A NYERS terep (a feltöltés nélkül), a tavak szintjén.</param>
        /// <param name="drainageParent">A lefolyás-fa (<see cref="FlowNetwork.PriorityFlood"/>), a vízgyűjtő bejárásához.</param>
        /// <param name="precipitation">Csapadék-mező (klíma-szint).</param>
        /// <param name="openWaterEvaporation">Nyílt vízfelszín párolgása (klíma-szint).</param>
        /// <param name="climateLevel">A két klíma-mező tile-szintje.</param>
        /// <param name="runoffCoefficient">A szárazföldi csapadék lefolyó hányada.</param>
        public static List<BalancedLake> Balance(
            LakesIceErosion.LakeResult lakes,
            Dictionary<TileId, double> terrain,
            Dictionary<TileId, TileId?> drainageParent,
            IReadOnlyDictionary<TileId, double> precipitation,
            IReadOnlyDictionary<TileId, double> openWaterEvaporation,
            int climateLevel,
            double runoffCoefficient = DefaultRunoffCoefficient)
        {
            if (lakes == null) throw new ArgumentNullException(nameof(lakes));
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (drainageParent == null) throw new ArgumentNullException(nameof(drainageParent));
            if (precipitation == null) throw new ArgumentNullException(nameof(precipitation));
            if (openWaterEvaporation == null) throw new ArgumentNullException(nameof(openWaterEvaporation));
            if (runoffCoefficient < 0.0) throw new ArgumentOutOfRangeException(nameof(runoffCoefficient));

            var result = new List<BalancedLake>(lakes.Lakes.Count);
            if (lakes.Lakes.Count == 0) return result;

            // Gyerek-lista a lefolyas-szulokbol: ezzel jarjuk be a vizgyujtot
            // FELFELE. A listak TileId szerint rendezve - az osszegzes
            // sorrendje igy nem fugg a szotar bejarasatol.
            var children = new Dictionary<TileId, List<TileId>>();
            foreach (KeyValuePair<TileId, TileId?> kv in drainageParent)
            {
                if (kv.Value == null) continue;
                TileId parent = kv.Value.Value;
                if (!children.TryGetValue(parent, out List<TileId> list))
                {
                    list = new List<TileId>();
                    children[parent] = list;
                }
                list.Add(kv.Key);
            }
            foreach (KeyValuePair<TileId, List<TileId>> kv in children)
                kv.Value.Sort((a, b) => a.Value.CompareTo(b.Value));

            // ND-198: MINDEN tó-tile elnyelő — a vízgyűjtő bejárása egy
            // FELJEBB fekvő tónál megáll. Ez az endorheikus feltevés: a felső
            // tó a saját mérlege szerint tartja a vizét, és nem adja tovább.
            // (A túlfolyás továbbadása — „a felső tó megtelik és a többlet
            // lefolyik" — az ND-198 következő köre; ehhez a tavakat lefolyási
            // sorrendben kellene feldolgozni.) A sűrű út UGYANEZT a szabályt
            // követi, és egy teszt méri, hogy a kettő egyezik.
            var allLakeTiles = new HashSet<TileId>();
            foreach (LakesIceErosion.LakeInfo lake in lakes.Lakes)
                foreach (TileId t in lake.Tiles) allLakeTiles.Add(t);

            foreach (LakesIceErosion.LakeInfo lake in lakes.Lakes)
            {
                result.Add(BalanceOne(lake, terrain, children, allLakeTiles, precipitation,
                    openWaterEvaporation, climateLevel, runoffCoefficient));
            }
            return result;
        }

        private static BalancedLake BalanceOne(
            LakesIceErosion.LakeInfo lake,
            Dictionary<TileId, double> terrain,
            Dictionary<TileId, List<TileId>> children,
            HashSet<TileId> allLakeTiles,
            IReadOnlyDictionary<TileId, double> precipitation,
            IReadOnlyDictionary<TileId, double> openWaterEvaporation,
            int climateLevel,
            double runoffCoefficient)
        {
            // A to tile-jai MAGASSAG szerint novekvoen (dontetlennel TileId):
            // ez adja a lehetseges vizmerleg-szinteket is.
            var tiles = new List<TileId>(lake.Tiles);
            tiles.Sort((a, b) =>
            {
                double ea = terrain.TryGetValue(a, out double va) ? va : 0.0;
                double eb = terrain.TryGetValue(b, out double vb) ? vb : 0.0;
                int byElev = ea.CompareTo(eb);
                return byElev != 0 ? byElev : a.Value.CompareTo(b.Value);
            });

            var lakeSet = new HashSet<TileId>(tiles);

            // VIZGYUJTO: a to fole folyo osszes tile (a lefolyas-fan felfele),
            // a to sajat tile-jai nelkul. A bejaras sorrendje nem szamit, mert
            // az osszeget utana RENDEZETT listan kepezzuk.
            var catchment = new List<TileId>();
            var seen = new HashSet<TileId>();
            var stack = new Stack<TileId>();
            foreach (TileId t in tiles) stack.Push(t);
            while (stack.Count > 0)
            {
                TileId current = stack.Pop();
                if (!children.TryGetValue(current, out List<TileId> kids)) continue;
                for (int i = 0; i < kids.Count; i++)
                {
                    TileId kid = kids[i];
                    if (lakeSet.Contains(kid) || !seen.Add(kid)) continue;
                    // Egy MASIK to elnyeli a sajat vizgyujtojet ES a sajat
                    // csapadekat is - sem o, sem a folotte levo terulet nem
                    // szamit bele ENNEK a tonak a bearamlasaba (ld. a Balance
                    // doksijat; a suru ut ugyanezt teszi).
                    if (allLakeTiles.Contains(kid)) continue;
                    catchment.Add(kid);
                    stack.Push(kid);
                }
            }
            catchment.Sort((a, b) => a.Value.CompareTo(b.Value));

            // BEARAMLAS: a vizgyujto lefolyasa + a to felszinere hullo csapadek.
            // A tile-terulet a SZINTBOL szarmazik, es minden tile ugyanakkora
            // sulyt kap - a gombi tile-terulet level-en belul nem azonos, de a
            // merleg mindket oldalan UGYANAZ a kozelites all, tehat a hanyados
            // ertelmes marad (es a sulyok kiesnek).
            double inflow = 0.0;
            for (int i = 0; i < catchment.Count; i++)
                inflow += ClimateValue(precipitation, catchment[i], climateLevel) * runoffCoefficient;
            for (int i = 0; i < tiles.Count; i++)
                inflow += ClimateValue(precipitation, tiles[i], climateLevel);

            // A LEGMAGASABB szint, ahol a parolgas meg nem haladja a bearamlast.
            // A tiles lista magassag szerint no, tehat az i. szintnel az elso
            // (i+1) tile van viz alatt.
            double evaporation = 0.0;
            int keptTiles = 0;
            double keptEvaporation = 0.0;
            for (int i = 0; i < tiles.Count; i++)
            {
                evaporation += ClimateValue(openWaterEvaporation, tiles[i], climateLevel);
                if (evaporation > inflow) break;
                keptTiles = i + 1;
                keptEvaporation = evaporation;
            }

            if (keptTiles == 0)
                return new BalancedLake(lake.Id, double.NaN, 0, inflow, evaporation);

            // A megmaradó felszín: a topográfiai szint, ha MINDEN tile elfér,
            // különben az első víz FELETTI tile terepszintje (addig telik).
            double surface = keptTiles >= tiles.Count
                ? lake.SurfaceElevation
                : (terrain.TryGetValue(tiles[keptTiles], out double nextElev)
                    ? nextElev : lake.SurfaceElevation);
            if (surface > lake.SurfaceElevation) surface = lake.SurfaceElevation;
            return new BalancedLake(lake.Id, surface, keptTiles, inflow, keptEvaporation);
        }

        // MEGJEGYZES (ND-198, 2026-10-06): keszult egy SURU (tombindexelt),
        // lefolyas-akkumulacios valtozat is - a viewer hidrologiaja azon az
        // uton dolgozik. A ket implementacio egy VALODI vilagon MERVE nem
        // egyezett (362 tobol 80-nal mas tile-szam, a szotaras ut
        // kovetkezetesen nagyobb bearamlassal), ezert NEM szallitjuk: egy
        // nem bizonyitottan azonos masodik ut a kritikus uton rosszabb, mint
        // a kis tobbletkoltseg. A viewer ezert szotarakat epit a suru
        // allapotbol. Ha a suru ut visszajon, ELOBB kell a bitazonossagot
        // igazolo teszt (ugyanaz a minta, mint a DenseLakeEquivalenceTests).

        /// <summary>A klíma-mező értéke egy (esetleg finomabb) tile-ra: az ősének az értéke.</summary>
        private static double ClimateValue(
            IReadOnlyDictionary<TileId, double> field, TileId tile, int climateLevel)
        {
            TileId current = tile;
            while (current.Level > climateLevel) current = current.Parent();
            return field.TryGetValue(current, out double value) ? value : 0.0;
        }
    }
}
