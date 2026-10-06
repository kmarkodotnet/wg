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
        /// <summary>
        /// ND-197 (b) (2026-10-06): a forrás MINIMÁLIS magassága a tengerszint
        /// fölött. 300 → 150 m, MÉRÉS alapján: a 300 m-es küszöb a nedves
        /// szárazföld 61,5%-át zárta ki a forrás-jelöltségből (514 nedves
        /// tile-ból csak 198 volt 300 m fölött), és a két szűrő (ez + a
        /// medence-méret) együtt a nedves föld 91,8%-át — ez volt a mért oka
        /// annak, hogy „sok zöld területen nincs folyó".
        /// </summary>
        public const double DefaultMinElevAboveSeaM = 150.0;
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
        /// <summary>
        /// ND-197 (b) (2026-10-06): hány vízgyűjtő VERSENYEZHET a forrás-keretért.
        /// Az alapértelmezés MINDEGYIK (a méretküszöb fölött, pozitív csapadékkal) —
        /// a korábbi „16 legnagyobb" vágás MÉRT hibát okozott: a nedves szárazföld
        /// 51,8%-a (105 összefüggő nedves foltból 92, a legnagyobb 66 tile) EGYETLEN
        /// folyót sem kapott, mert a vízgyűjtője nem fért be a legnagyobb 16-ba.
        /// A keret maga a csapadékból származik, ld. <see cref="DefaultSourcesPerCandidate"/>.
        /// </summary>
        public const int DefaultSourceBasinCount = int.MaxValue;

        /// <summary>
        /// ND-197 (b): hány forrás jut egy JELÖLT tile-ra. A forrás-keret
        /// ebből származik (keret = jelöltek száma × ez az érték), tehát a
        /// folyók SZÁMA a bolygó forrásképes, nedves hegyvidékének MÉRETÉT
        /// követi — nem egy fix 96-os szám, ami egyetlen Föld-szerű esetre
        /// volt hangolva.
        ///
        /// MIÉRT A JELÖLTSZÁM, ÉS NEM A CSAPADÉK-ÖSSZEG. Az első változat a
        /// szárazföldi csapadék ÖSSZEGÉBŐL számolta a keretet, és ez MÉRÉSSEL
        /// megbukott: a csapadék egysége önkényes (ND-126), ezért ugyanaz a
        /// konstans a hőmodell mezőjén 320, az analitikus előnézeten viszont
        /// 512 (plafonos) forrást adott — a nézet váltásakor megugrott volna a
        /// folyók száma. A jelöltszám GEOMETRIAI mennyiség, tehát a mező
        /// skálájától független. Ugyanaz a hibaosztály, mint az ND-159/164
        /// abszolút hőmérséklet-küszöbeinél.
        ///
        /// MÉRT KALIBRÁCIÓ (seed 0xA7C944210000, level 5, hőmodell-csapadék):
        /// ~320 forrásnál áll be a jó lefedettség/költség arány — a nedves
        /// szárazföld 1 szomszédon belüli lefedettsége 25,5% → 76,8%, a
        /// hálózat-építés 4 workerrel 91 s → 165 s. 512 forrásnál a
        /// lefedettség már csak 78,6% (+1,8 százalékpont), a költség viszont
        /// 279 s.
        /// </summary>
        public const double DefaultSourcesPerCandidate = 0.52;

        /// <summary>Biztonsági plafon: a keret ennél több forrást sosem ad (védelem egy elszálló csapadék-mező ellen).</summary>
        public const int DefaultMaxSources = 512;

        /// <summary>
        /// Hány forrás egy vízgyűjtőn belül. A 6 MÉRT érték: 4-nél a
        /// legnagyobb vízhozam-súly 4 és 5-6 folyó éri el a 3-as súlyt,
        /// 6-nál a súly 6 és 13 folyó - vagyis a fa ettől lesz TÖBBSZINTŰ,
        /// nem csak "egy mellékfolyó". Ld. <see cref="BuildRiverNetworkPerBasin"/>.
        /// </summary>
        public const int DefaultSourcesPerBasin = 6;

        /// <summary>
        /// Ennél kevesebb tile-os vízgyűjtőbe nem teszünk forrást.
        ///
        /// ND-197 (b) (2026-10-06): 12 → 2, MÉRÉS alapján. Az eredeti indok
        /// („egy 2-3 tile-os parti lefolyásban nincs hova összefolyni") a
        /// FA-MÉLYSÉGRE szólt, de a mellékhatása sokkal nagyobb volt: a
        /// referencia-szinten (level 5) EGY tile ~313 km, tehát a 12-es
        /// küszöb ~1,2 millió km²-nél kisebb vízgyűjtőket zárt ki — a nedves
        /// szárazföld 80,4%-át. MÉRVE: a jelölt-kínálat 120 → 376 forrás, a
        /// nedves szárazföld folyó-lefedettsége (1 szomszédon belül)
        /// 25,5% → 76,8%. A mély, dendritikus fát továbbra is a NAGY
        /// vízgyűjtők adják (oda megy a kvóta nagy része), a kicsik rövid,
        /// egyágú patakokat kapnak — ahogy a valóságban is.
        /// </summary>
        public const int DefaultMinBasinTiles = 2;

        /// <summary>
        /// Két forrás MINIMÁLIS távolsága egy vízgyűjtőn belül. Enélkül a
        /// legcsapadékosabb tile-ok egymás szomszédjai lennének, a második
        /// folyó egy-két lépés után beleolvadna az elsőbe, és nem keletkezne
        /// valódi mellékfolyó - csak egy elágazás a forrás mellett.
        /// </summary>
        public const double DefaultSourceSeparationMeters = 150_000.0;

        /// <summary>
        /// ND-196 (b): egy vízgyűjtő FELSŐ forrás-korlátja a csapadék-arányos
        /// kvótában. A 2 × <see cref="DefaultSourcesPerBasin"/> MÉRT
        /// kompromisszum: a 16 medencéből 7 teljesen csapadékmentes volt,
        /// tehát a maradék ~9 nedves medencének kell felszívnia a teljes
        /// 96-os keretet (átlag 10,7/medence). Korlát nélkül a legnedvesebb
        /// medence elvinné a keret nagy részét, és visszatérne a MÁSIK
        /// felhasználói panasz ("az egész bolygón ritkák a folyók"), amiért
        /// az ND-124 a medencénkénti kvótát egyáltalán bevezette.
        /// </summary>
        public const int DefaultMaxSourcesPerBasin = 2 * DefaultSourcesPerBasin;

        /// <summary>
        /// ND-196 (b): egy jelölt csak akkor lehet forrás, ha a csapadéka
        /// ENNÉL SZIGORÚAN NAGYOBB. A 0,0 nem kozmetika: a hőmodell
        /// párolgás-bemenetével a szárazföld 52,3%-ának PONTOSAN nulla a
        /// csapadéka, és a medencén belüli rendezés egy teljesen száraz
        /// medencében is kiadta a 6 forrást - mérve 48/96 forrás (50%)
        /// indult nulla csapadékú tile-ról. Ez az ABSZOLÚT alsó kapu, amit
        /// az ND-196 (b) a csapadék-arányos kvóta mellé ír elő.
        /// </summary>
        public const double DefaultMinSourcePrecip = 0.0;

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
        /// a méretküszöb fölötti vízgyűjtőket veszi (legfeljebb
        /// <paramref name="basinCount"/> darabot), és szétosztja közöttük a
        /// forrás-keretet.
        ///
        /// ND-196 (b) (SEED-TÖRŐ, 2026-10-06): a kvóta NEM fix forrás/medence,
        /// hanem a medencék CSAPADÉK-ÖSSZEGÉVEL arányos, abszolút alsó kapuval.
        /// ND-197 (b) (SEED-TÖRŐ, 2026-10-06): a KERET is a csapadékból jön
        /// (<paramref name="sourcesPerCandidate"/>), és MINDEN méretküszöb fölötti
        /// vízgyűjtő versenyez érte — nem csak a legnagyobb 16.
        ///
        /// MIÉRT. A csapadék eddig KIZÁRÓLAG medencén belüli rendezési kulcs
        /// volt, globális kapu nélkül - így mind a 16 legnagyobb vízgyűjtő
        /// megkapta a 6 forrását akkor is, ha egyetlen csapadékos tile sem
        /// volt benne. MÉRVE (seed 0xA7C944210000, level 5, a hőmodell
        /// párolgásával): 7 medence MIND a 6 forrása nulla csapadékú tile-on
        /// indult, összesen 48/96 forrás (50%), és a folyóhosszban a
        /// `Desert` 2,45×, a `Rainforest` 0,51× volt a szárazföldi
        /// arányához képest - azaz a felhasználói kérés (csapadékos
        /// területen legyenek a folyók) nem teljesült.
        ///
        /// A KVÓTA-KÉPZÉS (determinisztikus, nincs benne lebegőpontos
        /// rendezés-érzékenység):
        /// 1. egy medence SÚLYA a saját JELÖLT-tile-jainak csapadék-összege
        ///    (jelölt = hegyvidéki ÉS pozitív csapadékú) - a súly tehát azt
        ///    méri, van-e egyáltalán nedves forrásvidék a medencében, nem
        ///    pedig azt, mekkora a medence;
        /// 2. nulla súlyú medence SOHA nem kap forrást, és nem is foglal
        ///    medence-HELYET: a <paramref name="basinCount"/> slot a következő,
        ///    NEDVES vízgyűjtőre csúszik. MÉRVE: enélkül a 16 legnagyobb
        ///    vízgyűjtő közül 7 száraz volt, és a 96-os keretből csak 66
        ///    forrás valósult meg (-31% folyó);
        /// 3. minden megmaradt (pozitív súlyú) medence kap egyet — ez tartja
        ///    meg a folyórendszerek bolygó-léptékű szétszórtságát;
        /// 4. a maradék keret a `súly / (eddigi forrás + 1)` legnagyobb
        ///    hányadosa szerint oszlik (Jefferson/D'Hondt-menet), döntetlennél
        ///    a kisebb medence-index javára, legfeljebb
        ///    <paramref name="maxSourcesPerBasin"/>-ig;
        /// 5. a kiosztás INKREMENTÁLIS: minden keret-egység azonnal megpróbál
        ///    forrást felvenni, és ha a medence kimerült (nincs több használható
        ///    jelölt, vagy a szeparáció nem enged többet), az egység a következő
        ///    legjobb medencére szállt át. MÉRVE: előre kiosztott kvótával a
        ///    96-os keretből csak 70 forrás valósult meg.
        /// A súly-összegzés a MÁR RENDEZETT jelölt-listán fut, tehát a
        /// szótár-bejárási sorrend nem befolyásolja a bitpontos összeget.
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
            int? sourceBudget = null,
            double minElevAboveSeaM = DefaultMinElevAboveSeaM,
            double minSeparationMeters = DefaultSourceSeparationMeters,
            int minBasinTiles = DefaultMinBasinTiles,
            ulong worldSeed = 0UL,
            (double X, double Y, double Z)[]? seeds = null,
            DeepTimeContext context = default,
            int fineDepth = DefaultFineDepth,
            Dictionary<TileId, double>? floodFilled = null,
            double lakeDepthMeters = DefaultSubmergedMinDepthMeters,
            int maxSourcesPerBasin = DefaultMaxSourcesPerBasin,
            double minSourcePrecip = DefaultMinSourcePrecip,
            double sourcesPerCandidate = DefaultSourcesPerCandidate,
            int maxSources = DefaultMaxSources)
        {
            if (elevField == null) throw new ArgumentNullException(nameof(elevField));
            if (precipField == null) throw new ArgumentNullException(nameof(precipField));
            if (isOcean == null) throw new ArgumentNullException(nameof(isOcean));
            if (floodParent == null) throw new ArgumentNullException(nameof(floodParent));
            if (basinCount < 1) throw new ArgumentOutOfRangeException(nameof(basinCount));
            if (sourceBudget.HasValue && sourceBudget.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(sourceBudget));
            if (sourcesPerCandidate <= 0.0) throw new ArgumentOutOfRangeException(nameof(sourcesPerCandidate));
            if (maxSourcesPerBasin < 1) throw new ArgumentOutOfRangeException(nameof(maxSourcesPerBasin));

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

            // 1. MENET: jelolt-listak es medence-sulyok. A sulyozas miatt a
            // TELJES jelolt-halmaz kell, mielott barmit valasztunk - ezert
            // van ket menet (korabban egy volt, fix kvotaval).
            var basinCandidates = new List<List<TileId>>();
            var basinWeights = new List<double>();
            int basinsUsed = 0;
            for (int b = 0; b < outlets.Count && basinsUsed < basinCount; b++)
            {
                List<TileId> basin = basins[outlets[b]];
                if (basin.Count < minBasinTiles) break; // meret szerint rendezve: innentol mind kisebb

                var candidates = new List<TileId>();
                foreach (TileId t in basin)
                {
                    if (!elevField.TryGetValue(t, out double elevation)) continue;
                    if (elevation < seaLevel + minElevAboveSeaM) continue;
                    if (!precipField.TryGetValue(t, out double precipHere)) continue;
                    // ND-196 (b): ABSZOLUT also kapu - nulla (vagy negativ)
                    // csapadeku tile nem forrasvidek, akkor sem, ha a sajat
                    // medencejen belul eppen o a legnedvesebb.
                    if (precipHere <= minSourcePrecip) continue;
                    candidates.Add(t);
                }
                candidates.Sort((x, y) =>
                {
                    int byPrecip = precipField[y].CompareTo(precipField[x]);
                    return byPrecip != 0 ? byPrecip : x.Value.CompareTo(y.Value);
                });

                // A suly a RENDEZETT listan ossszegzodik, tehat a szotar-
                // bejarasi sorrend nem valtoztatja meg a bitpontos osszeget.
                double weight = 0.0;
                for (int c = 0; c < candidates.Count; c++) weight += precipField[candidates[c]];

                // ND-196 (b): a TELJESEN SZARAZ medence nem foglal medence-
                // HELYET sem. MERVE: a 16 legnagyobb vizgyujtobol 7-nek
                // pontosan nulla a jelolt-csapadeka, tehat a "16 medence"
                // valojaban 9-et jelentett, es a 96-os keretbol csak 66
                // forras valosult meg (-31% folyo). Igy viszont a kovetkezo,
                // NEDVES medence lep a helyere - a keret betoltheto marad.
                if (weight <= 0.0) continue;
                basinsUsed++;

                basinCandidates.Add(candidates);
                basinWeights.Add(weight);
            }

            // 2. MENET: a keret szetosztasa csapadek-aranyosan, INKREMENTALISAN.
            //
            // A keret az ND-196-ban meg FIX volt (basinCount x sourcesPerBasin);
            // az ND-197 (b) ota a SZARAZFOLDI VIZHOZAMBOL szarmazik, ha a hivo
            // nem ad explicit keretet - igy a folyok SZAMA a bolygo
            // csapadekat koveti, es nem egy fix szam.
            //
            // MIERT INKREMENTALIS (es nem elore kiszamolt kvota). MERVE: az
            // elore kiosztott kvotabol a 96-os keretnek csak 70 forrasa
            // valosult meg, mert a kvota olyan medencekbe is jutott, ahol
            // elfogytak a jeloltek (kevés nedves hegyvidek, vagy a 150 km-es
            // forras-szeparacio nem enged tobbet). Igy viszont minden
            // keret-egyseg oda kerul, ahol tenylegesen van hova: a soron levo
            // medence AZONNAL megprobalja a valasztast, es ha nem megy, a
            // keret-egyseg a kovetkezo legjobb medencere szall at.
            var basinChosen = new List<List<TileId>>(basinCandidates.Count);
            var basinChosenPositions = new List<List<(double X, double Y, double Z)>>(basinCandidates.Count);
            var basinCursor = new int[basinCandidates.Count];
            var basinExhausted = new bool[basinCandidates.Count];
            for (int b = 0; b < basinCandidates.Count; b++)
            {
                basinChosen.Add(new List<TileId>());
                basinChosenPositions.Add(new List<(double X, double Y, double Z)>());
            }

            // Egy forras felvetele a(z) `b` medencebe, a jelolt-sorrendben
            // elorehaladva. Hamis, ha a medence KIMERULT (nincs tobb
            // hasznalhato jelolt) - ekkor a keret masik medencere szall at.
            bool TryAddSource(int b)
            {
                List<TileId> candidates = basinCandidates[b];
                List<(double X, double Y, double Z)> chosen = basinChosenPositions[b];
                for (int c = basinCursor[b]; c < candidates.Count; c++)
                {
                    // ND-189 (SEED-TORO, 2026-10-03): a jelolt a DURVA mezon
                    // felelt meg, de a nyomkoveto a FINOM mezot latja. MERVE a
                    // t=0 halozaton: 96 forrasbol 17 egy LATHATO to alatt, 2
                    // pedig a tengerszint ALATT volt - az utobbi ketto adta a
                    // 0,00 km hosszu agakat (#72, #81). Itt ugyanazt a pontot
                    // ellenorizzuk, amibol a koveto INDUL.
                    if (seeds != null && !FineSourceIsUsable(
                            worldSeed, seeds, context, fineDepth, candidates[c], seaLevel))
                        continue;
                    // A durva mezon latszo TO sem lehet forras (ott allovíz
                    // van, nem forrasvidek).
                    if (floodFilled != null
                        && floodFilled.TryGetValue(candidates[c], out double filledHere)
                        && elevField.TryGetValue(candidates[c], out double rawHere)
                        && filledHere - rawHere >= lakeDepthMeters)
                        continue;
                    TileGeometry.ToPosition(candidates[c], out double x, out double y, out double z);
                    bool tooClose = false;
                    for (int k = 0; k < chosen.Count; k++)
                    {
                        double dot = x * chosen[k].X + y * chosen[k].Y + z * chosen[k].Z;
                        if (dot > minSeparationCos) { tooClose = true; break; }
                    }
                    if (tooClose) continue;
                    chosen.Add((x, y, z));
                    basinChosen[b].Add(candidates[c]);
                    basinCursor[b] = c + 1;
                    return true;
                }
                basinCursor[b] = candidates.Count;
                basinExhausted[b] = true;
                return false;
            }

            int budget;
            if (sourceBudget.HasValue)
            {
                budget = sourceBudget.Value;
            }
            else
            {
                // A jelolteket a mar eloallitott, DETERMINISZTIKUS sorrendu
                // medence-listakbol szamoljuk, nem a szotarbol.
                int candidateCount = 0;
                for (int b = 0; b < basinCandidates.Count; b++) candidateCount += basinCandidates[b].Count;
                double raw = candidateCount * sourcesPerCandidate;
                budget = raw >= maxSources ? maxSources : (int)raw;
                if (budget < 1) budget = 1;
            }
            int placed = 0;

            // ELSO KOR: minden megmaradt (pozitiv sulyu) medence kap egyet -
            // ez tartja meg a folyorendszerek bolygo-leptéku szetszortsagat.
            for (int b = 0; b < basinCandidates.Count && placed < budget; b++)
                if (TryAddSource(b)) placed++;

            // UTANA: a maradek keret a `suly / (eddigi forras + 1)` legnagyobb
            // hanyadosa szerint (Jefferson/D'Hondt-menet), dontetlennel a
            // kisebb medence-index javara, legfeljebb maxSourcesPerBasin-ig.
            while (placed < budget)
            {
                int best = -1;
                double bestQuotient = 0.0;
                for (int b = 0; b < basinCandidates.Count; b++)
                {
                    if (basinExhausted[b]) continue;
                    int count = basinChosen[b].Count;
                    if (count >= maxSourcesPerBasin) continue;
                    double quotient = basinWeights[b] / (count + 1);
                    if (quotient > bestQuotient) { bestQuotient = quotient; best = b; }
                }
                if (best < 0) break; // nincs tobb hely: a keret maradeka elesik
                if (TryAddSource(best)) placed++;
            }

            // 3. MENET: a kimenet MEDENCE SZERINT csoportositva - ugyanaz a
            // sorrend-konvencio, mint a fix kvotanal (a diagnosztika es a
            // nyomkoveto sorrend-fuggo allapota erre epul).
            var sources = new List<TileId>(placed);
            for (int b = 0; b < basinChosen.Count; b++)
                sources.AddRange(basinChosen[b]);
            return sources;
        }

        /// <summary>
        /// ND-189: hasznalhato-e a jelolt FORRASKENT azon a FINOM ponton,
        /// ahonnan a <see cref="TraceRiverPathContinuous"/> ténylegesen indul?
        /// A szamitas BITRE ugyanaz a lanc, mint ott (ugyanaz a finom TileId,
        /// ugyanaz a sampler) - kulonben a szures masik pontot ellenorizne,
        /// mint amibol a koveto indul.
        ///
        /// A feltetel: a forraspont a TENGERSZINT FELETT legyen. Enelkul a
        /// koveto az elso lepesnel `Ocean` terminaciot ad, es nulla hosszu
        /// "folyot" kapunk (merve: 2 ilyen ag a 96-bol).
        /// </summary>
        private static bool FineSourceIsUsable(
            ulong worldSeed, (double X, double Y, double Z)[] seeds, in DeepTimeContext context,
            int fineDepth, TileId source, double seaLevel)
        {
            int fineLevel = source.Level + fineDepth;
            source.GetUV(out uint su, out uint sv);
            TileId fineSource = TileId.FromFaceLevelUV(
                source.Face, fineLevel, su << fineDepth, sv << fineDepth);
            TileGeometry.ToPosition(fineSource, out double x, out double y, out double z);
            var sampler = new WorldElevationSampler(worldSeed, seeds, context);
            return sampler.Sample(x, y, z) >= seaLevel;
        }

        /// <summary>Memoizált pontszerű elevációkiértékelés - a nyomvonalkövetés és a pit-escape keresés gyakran ugyanazokat a finom tile-okat kérdezi le.</summary>
        private sealed class ElevationCache
        {
            private readonly ulong _worldSeed;
            private readonly (double X, double Y, double Z)[] _seeds;
            private readonly DeepTimeContext _context;
            private readonly Dictionary<TileId, double> _cache = new Dictionary<TileId, double>();

            public ElevationCache(
                ulong worldSeed, (double X, double Y, double Z)[] seeds, in DeepTimeContext context)
            {
                _worldSeed = worldSeed;
                _seeds = seeds;
                _context = context;
            }

            public double Get(TileId id)
            {
                if (_cache.TryGetValue(id, out double cached))
                    return cached;
                TileGeometry.ToPosition(id, out double x, out double y, out double z);
                double elev = ElevationAtPosition(_worldSeed, x, y, z, _seeds, _context);
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
            ulong worldSeed, double x, double y, double z, (double X, double Y, double Z)[] seeds,
            in DeepTimeContext context)
        {
            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
            // ND-136 (A19): a folytonos nyomvonal UGYANAZT a lemez-keretes
            // domborzatot lassa, mint a tile-kozepu mezo - kulonben t>0-nal a
            // folyo egy masik (vilag-keretes) terepen futna, mint amit a
            // felhasznalo lat.
            // ND-137 (A20): ugyanez all az EROZIORA is - a folyo a MAR
            // lekopott/feltoltodott terepen fut, kulonben a nyomvonal egy
            // masik domborzaton keresne a lejtot, mint amit a felhasznalo lat.
            return PlateBoundaryEffect.ElevationWithBoundaryFromWarpedAtTime(
                worldSeed, plateId, 0UL, x, y, z, wx, wy, wz, seeds, context, out _);
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
            Dictionary<TileId, int> claimed, int maxSteps, int escapeNodeBudget,
            DeepTimeContext context = default)
        {
            var elevCache = new ElevationCache(
                worldSeed, seeds, context);
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
        /// A hurok-vedelem bucket-racsanak szintje a BITPONTOS
        /// <see cref="CubeFaceLattice"/>-en (ND-186). Level 20-on a cella
        /// 7,1-14,2 m - finomabb, mint az alapertelmezett 50 m-es lepeskoz,
        /// tehat ket KULONBOZO, kozeli lepes nem olvad ossze, a folyo SAJAT
        /// medret viszont megbizhatoan felismeri.
        ///
        /// MIERT NEM <see cref="TileGeometry.FromPosition"/> (a korabbi
        /// megoldas): az `Math.Atan`-t hiv, ami az ND-23b/ND-24 szerint NEM
        /// bitpontos platformok kozott - azaz a kirajzolt folyohalozat
        /// topologiaja egy nem bitpontos fuggvenyen mult (I1-serules).
        /// </summary>
        public const int DefaultVisitedLatticeLevel = 20;

        /// <summary>
        /// Az osszefolyas terbeli toleranciaja - ld.
        /// <see cref="ClaimedRiverPoints.DefaultMergeRadiusMeters"/>.
        /// </summary>
        public const double DefaultMergeRadiusMeters = ClaimedRiverPoints.DefaultMergeRadiusMeters;

        /// <summary>
        /// Az escape-szakasz "viz alatti" egyenes-osszevonasat legfeljebb ennyi
        /// durva cellan at probaljuk. Tisztan KOLTSEGKORLAT (a probalkozas
        /// kvadratikus a szakasz hosszaban), a DONTEST nem befolyasolja:
        /// nagyobb ertek csak hosszabb egyenes szakaszokat engedne.
        /// </summary>
        public const int DefaultEscapeShortcutCells = 64;

        /// <summary>
        /// Az escape-szakasz PONTSURUSEGE meterben.
        ///
        /// MERT INDOK (ND-186). Az escape-szakaszt eloszor a normal lepeskozre
        /// (50 m) mintaveteleztuk, es a t=0 halozat pontszama 415 293 -> 954 598
        /// lett (+130%), a csucs-memoria 117 -> 255 MB - vagyis a mesh-elokeszites
        /// 2,3-szorosara nyult. Holott az escape-szakasz ALAKJAROL a 2000 m-es
        /// dontesi racsnal finomabb mintavetel NEM ad uj modell-informaciot: az a
        /// szakasz "viz alatti", azaz SIMA. 250 m meg mindig 8x finomabb, mint a
        /// dontesi racs, es eleg suru ahhoz, hogy a kirajzolt vonal a
        /// megjelenitett felszinen maradjon (a viewer minden pontot a domborzatra
        /// ultet, tehat a tul hosszu szakasz bevagna a reliefbe).
        /// </summary>
        public const double DefaultContinuousEscapeEmitMeters = 250.0;

        /// <summary>
        /// ND-187: egy zart medence MILYEN MELY legyen ahhoz, hogy a benne
        /// futo szakaszt TO-nak (viz alattinak) jeloljuk - ld.
        /// <see cref="ContinuousRiverPath.SubmergedSpans"/>.
        ///
        /// MIERT KELL KUSZOB (MERVE, 2026-10-03). Kuszob nelkul a jeloles a
        /// t=0 halozat hosszanak 71,80%-at (33 490 km) fedte, holott a
        /// LATHATO tavakra (LakesIceErosion + TileCount &gt;= 6 és MaxDepth &gt;= 40)
        /// csak 41,34% (19 283 km) esik. A kulonbseget apro, nehany meteres
        /// lokalis melyedesek adjak: azokon a viz valoban ATFOLYIK, ott
        /// FOLYO van, nem to. A kuszob ezeket kizarja.
        ///
        /// Az ertek a megjelenitett to-reteg melyseg-kuszobevel egyezik
        /// (`minLakeDepthMeters` a viewerben, `MaxDepth &gt;= 40` az
        /// `endcheck`-ben), hogy a ket reteg ugyanazt a vilagot mutassa.
        /// </summary>
        public const double DefaultSubmergedMinDepthMeters = 40.0;

        public sealed class ContinuousRiverPath
        {
            public int SourceIndex;
            public List<(double X, double Y, double Z)> Points = new List<(double X, double Y, double Z)>();
            public TerminationReason Termination;

            /// <summary>
            /// Ha <see cref="Termination"/> == Merged, annak a folyónak a
            /// SourceIndex-e, amibe ez a folyó beleolvadt (ld.
            /// <see cref="ClaimedRiverPoint.RiverIndex"/>) - egyébként -1.
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

            /// <summary>
            /// ND-187: azok a <see cref="Points"/>-tartomanyok (zart
            /// [Start, End] indexparok), amelyek egy ZART MEDENCE feltoltesi
            /// szintje ALATT futnak - vagyis fizikailag TO-felszin alatt,
            /// nem folyomeder.
            ///
            /// MIERT KELL. A 2026-10-03-i meres szerint a t=0 halozat
            /// hosszanak 41,34%-a (19 283 km a 46 641-bol) latható tavakon
            /// fut, a leghosszabb egyetlen atvagas 512,9 km. A nyomvonal
            /// GEOMETRIAJA helyes (a viz tenylegesen atfolyik a tavon), de
            /// FOLYOKENT megjelenitve hamis kepet ad - a felhasznaloi
            /// visszajelzes is ezt jelezte. A viewer ezeket a szakaszokat
            /// nem rajzolja folyoszalagkent.
            ///
            /// A tartomany a BEERESZKEDESSEL kezdodik (az a pont, ahonnan a
            /// nyomvonal mar vegig a feltoltesi szint alatt maradt) es az
            /// escape-szakasz utolso pontjaval zarul. A tartomanyok
            /// DISZJUNKTAK es NOVEKVOK; egymasba ero medencek osszevonodnak.
            ///
            /// Ez SZARMAZTATOTT adat: a <see cref="Points"/> geometriajat nem
            /// befolyasolja, ezert nem seed-toro es nem valtoztat
            /// generatorverziot.
            /// </summary>
            public List<(int Start, int End)> SubmergedSpans = new List<(int Start, int End)>();
        }

        /// <summary>
        /// A folytonos dendritikus osszefolyas lefoglalt pontjai MOSTANTOL a
        /// <see cref="ClaimedRiverPoints"/> terbeli indexben vannak (ND-186).
        /// A korabbi `ClaimedTileInfo` + `Dictionary&lt;TileId, ...&gt;` par
        /// szandekosan megszunt: egy TELJES finom tile-ra tarolt EGYETLEN pont
        /// adta az ND-180-ban mert 24,113 km-es osszefolyasi "teleportot" es a
        /// racs-illeszkedestol fuggo, hamis/kimarado osszefolyasokat.
        /// </summary>

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
            ClaimedRiverPoints? claimed,
            double stepMeters = DefaultContinuousStepMeters,
            double sensingRadiusMeters = DefaultContinuousSensingRadiusMeters,
            int ringDirections = DefaultContinuousRingDirections,
            double escapeCellMeters = DefaultContinuousEscapeCellMeters,
            int escapeNodeBudget = DefaultContinuousEscapeNodeBudget,
            long maxSteps = DefaultContinuousMaxSteps,
            int visitedLatticeLevel = DefaultVisitedLatticeLevel,
            System.Threading.CancellationToken cancellation = default,
            Action? onPitEscape = null,
            DeepTimeContext context = default,
            int escapeShortcutCells = DefaultEscapeShortcutCells,
            double escapeEmitMeters = DefaultContinuousEscapeEmitMeters,
            double submergedMinDepthMeters = DefaultSubmergedMinDepthMeters)
        {
            int fineLevel = source.Level + fineDepth;
            source.GetUV(out uint su, out uint sv);
            TileId fineSource = TileId.FromFaceLevelUV(source.Face, fineLevel, su << fineDepth, sv << fineDepth);
            TileGeometry.ToPosition(fineSource, out double sx, out double sy, out double sz);
            var sampler = new WorldElevationSampler(worldSeed, seeds, context);
            return TraceContinuousFrom(sampler, (sx, sy, sz), sourceIndex, seaLevel, claimed,
                stepMeters, sensingRadiusMeters, ringDirections, escapeCellMeters,
                escapeNodeBudget, maxSteps, visitedLatticeLevel, escapeShortcutCells,
                escapeEmitMeters, submergedMinDepthMeters, cancellation, onPitEscape);
        }

        /// <summary>
        /// Pontszeru elevacio-kiertekelo. A kovetot EZEN keresztul
        /// parameterezzuk, hogy a Python-orakulum SZINTETIKUS, analitikus
        /// domborzatai (sik / volgy / medence / ket meder) UGYANAZT a kodot
        /// merjek, amit a termek futtat. Generikus, `struct`-ra kotott
        /// megvalositas: a JIT devirtualizalja, tehat a lepesenkent tobbszori
        /// hivas nem jar delegate-koltseggel.
        /// </summary>
        public interface IElevationSampler
        {
            double Sample(double x, double y, double z);
        }

        /// <summary>A termek elevacio-kiertekeloje (ld. <see cref="ElevationAtPosition"/>).</summary>
        public readonly struct WorldElevationSampler : IElevationSampler
        {
            private readonly ulong _worldSeed;
            private readonly (double X, double Y, double Z)[] _seeds;
            private readonly DeepTimeContext _context;

            public WorldElevationSampler(
                ulong worldSeed, (double X, double Y, double Z)[] seeds, in DeepTimeContext context)
            {
                _worldSeed = worldSeed;
                _seeds = seeds;
                _context = context;
            }

            public double Sample(double x, double y, double z) =>
                ElevationAtPosition(_worldSeed, x, y, z, _seeds, _context);
        }

        /// <summary>
        /// A v2 folytonos koveto MAGJA, barmilyen elevacio-kiertekelovel.
        /// A harom ND-186 javitas itt el:
        ///   (1) a lepesirany a jelolt-kor ELSO HARMONIKUSABOL szamolt
        ///       FOLYTONOS lejtesirany (<see cref="DescentDirection"/>);
        ///   (2) az osszefolyas VALODI terbeli kozelsegvizsgalat
        ///       (<see cref="ClaimedRiverPoints"/>);
        ///   (3) az escape-szakasz "viz alatti" egyenesekre van osszevonva es
        ///       `stepMeters` surusegre mintavetelezve
        ///       (<see cref="RefineEscapePath{TSampler}"/>).
        /// </summary>
        public static ContinuousRiverPath TraceContinuousFrom<TSampler>(
            TSampler sampler, (double X, double Y, double Z) sourcePosition, int sourceIndex,
            double seaLevel, ClaimedRiverPoints? claimed,
            double stepMeters = DefaultContinuousStepMeters,
            double sensingRadiusMeters = DefaultContinuousSensingRadiusMeters,
            int ringDirections = DefaultContinuousRingDirections,
            double escapeCellMeters = DefaultContinuousEscapeCellMeters,
            int escapeNodeBudget = DefaultContinuousEscapeNodeBudget,
            long maxSteps = DefaultContinuousMaxSteps,
            int visitedLatticeLevel = DefaultVisitedLatticeLevel,
            int escapeShortcutCells = DefaultEscapeShortcutCells,
            double escapeEmitMeters = DefaultContinuousEscapeEmitMeters,
            double submergedMinDepthMeters = DefaultSubmergedMinDepthMeters,
            System.Threading.CancellationToken cancellation = default,
            Action? onPitEscape = null)
            where TSampler : struct, IElevationSampler
        {
            cancellation.ThrowIfCancellationRequested();
            (double X, double Y)[] directions = RiverDirectionTable.Build(ringDirections);

            var result = new ContinuousRiverPath { SourceIndex = sourceIndex };
            (double X, double Y, double Z) current = sourcePosition;
            result.Points.Add(current);

            // ND-187: pontonkenti elevacio - KIZAROLAG a viz alatti
            // tartomanyok visszamenoleges megjelolesehez (ld. az escape-ag
            // span-szamitasat). Egyetlen lepes-dontest sem befolyasol, es a
            // nyomvonal befejezesevel eldobodik.
            var pointElevations = new List<double>
            {
                sampler.Sample(current.X, current.Y, current.Z),
            };

            // HUROK-VEDELEM: minden bejart pontot egy FINOM (kb. 7-14 m)
            // bucket-racsra kepezunk. ND-186: ez a racs a BITPONTOS
            // CubeFaceLattice, nem a Math.Atan-t hivo TileGeometry.
            var pathVisited = new HashSet<long>
            {
                CubeFaceLattice.KeyFromPosition(current.X, current.Y, current.Z, visitedLatticeLevel),
            };

            double stepAngular = stepMeters / PlanetConstants.RadiusMeters;
            double sensingAngular = Math.Max(stepAngular, sensingRadiusMeters / PlanetConstants.RadiusMeters);
            double elev = pointElevations[0];
            var sensed = new double[ringDirections];

            for (long step = 0; step < maxSteps; step++)
            {
                cancellation.ThrowIfCancellationRequested();
                result.ClaimCheckIndices.Add(result.Points.Count - 1);

                if (elev < seaLevel)
                {
                    result.Termination = TerminationReason.Ocean;
                    return result;
                }

                if (claimed != null && step > 0
                    && claimed.TryFindNearest(current, out ClaimedRiverPoint owner))
                {
                    // A megszakado folyo utolso pontjat PONTOSAN a befogado
                    // folyo LEGKOZELEBBI valodi pontjara zarjuk - a zaroel
                    // igy strukturalisan <= a tolerancia (ND-186).
                    result.Points.Add(owner.Position);
                    pointElevations.Add(sampler.Sample(
                        owner.Position.X, owner.Position.Y, owner.Position.Z));
                    result.MergedIntoRiverIndex = owner.RiverIndex;
                    result.Termination = TerminationReason.Merged;
                    return result;
                }

                SphereWalk.GetTangentBasis(current, out (double X, double Y, double Z) t1, out (double X, double Y, double Z) t2);

                // A LEPES-KAPU valtozatlan: csak akkor lepunk, ha a
                // jelolt-korben van SZIGORUAN alacsonyabb pont.
                double bestElev = elev;
                int bestIndex = -1;
                for (int k = 0; k < ringDirections; k++)
                {
                    (double X, double Y, double Z) probe = SphereWalk.StepInTangentDirection(
                        current, t1, t2, directions[k].X, directions[k].Y, sensingAngular);
                    double probeElev = sampler.Sample(probe.X, probe.Y, probe.Z);
                    sensed[k] = probeElev;
                    if (probeElev < bestElev)
                    {
                        bestElev = probeElev;
                        bestIndex = k;
                    }
                }

                if (bestIndex >= 0)
                {
                    (double X, double Y) direction = DescentDirection(sensed, directions, ringDirections, bestIndex);
                    (double X, double Y, double Z) candidate = SphereWalk.StepInTangentDirection(
                        current, t1, t2, direction.X, direction.Y, stepAngular);
                    double candidateElev = sampler.Sample(candidate.X, candidate.Y, candidate.Z);
                    if (candidateElev >= elev
                        && (direction.X != directions[bestIndex].X || direction.Y != directions[bestIndex].Y))
                    {
                        // A folytonos irany nem lejt: visszaesunk a legjobb
                        // jelolt-iranyra (a v1 viselkedese), hogy a szigoru
                        // lejtes-kapu ne gyenguljon.
                        direction = directions[bestIndex];
                        candidate = SphereWalk.StepInTangentDirection(
                            current, t1, t2, direction.X, direction.Y, stepAngular);
                        candidateElev = sampler.Sample(candidate.X, candidate.Y, candidate.Z);
                    }
                    current = candidate;
                    elev = candidateElev;
                    result.Points.Add(current);
                    pointElevations.Add(candidateElev);
                    pathVisited.Add(CubeFaceLattice.KeyFromPosition(current.X, current.Y, current.Z, visitedLatticeLevel));
                    continue;
                }

                // Diagnosztikai szamlalo; a lejto- es osszefolyas-dontest nem modositja.
                onPitEscape?.Invoke();
                var escape = FindContinuousLocalSpillway(
                    sampler, current, elev, escapeCellMeters,
                    escapeNodeBudget, pathVisited, visitedLatticeLevel, cancellation);
                if (escape == null)
                {
                    result.Termination = TerminationReason.Pit;
                    return result;
                }

                List<(double X, double Y, double Z)> coarsePath = escape.Value.Path;
                double lakeLevel = elev;
                for (int i = 0; i < coarsePath.Count; i++)
                {
                    double cellElev = sampler.Sample(coarsePath[i].X, coarsePath[i].Y, coarsePath[i].Z);
                    if (cellElev > lakeLevel) lakeLevel = cellElev;
                }
                List<(double X, double Y, double Z)> refined = RefineEscapePath(
                    sampler, coarsePath, lakeLevel, escapeEmitMeters, escapeCellMeters * 0.5, escapeShortcutCells);
                // ND-187: a span a BEERESZKEDESSEL kezdodik - a mar felvett
                // pontokon visszafele addig, amig a pont a feltoltesi szint
                // (`lakeLevel`) alatt van. Ez az a szakasz, ami a feltoltodes
                // utan TO-felszin alatt marad.
                int spanStart = result.Points.Count - 1;
                while (spanStart > 0 && pointElevations[spanStart - 1] < lakeLevel) spanStart--;
                for (int i = 0; i < refined.Count; i++)
                {
                    result.Points.Add(refined[i]);
                    pointElevations.Add(lakeLevel);
                    pathVisited.Add(CubeFaceLattice.KeyFromPosition(refined[i].X, refined[i].Y, refined[i].Z, visitedLatticeLevel));
                }
                int spanEnd = result.Points.Count - 1;
                // ND-187 melyseg-kapu: csak a VALODI medencet jeloljuk tonak.
                // A melyseg a feltoltesi szint es a medence melypontja (a pit
                // aktualis elevacioja) kozti kulonbseg.
                double basinDepth = lakeLevel - elev;
                if (spanEnd > spanStart && basinDepth >= submergedMinDepthMeters)
                {
                    int last = result.SubmergedSpans.Count - 1;
                    if (last >= 0 && result.SubmergedSpans[last].End >= spanStart)
                    {
                        // Egymasba ero medencek: osszevonjuk, hogy a
                        // tartomanyok diszjunktak es novekvok maradjanak.
                        result.SubmergedSpans[last] = (result.SubmergedSpans[last].Start, spanEnd);
                    }
                    else
                    {
                        result.SubmergedSpans.Add((spanStart, spanEnd));
                    }
                }
                current = coarsePath[coarsePath.Count - 1];
                elev = escape.Value.SpillwayElevation;
            }

            result.Termination = TerminationReason.MaxSteps;
            return result;
        }

        /// <summary>
        /// FOLYTONOS lejtesirany a jelolt-kor ELSO HARMONIKUSABOL (ND-186, az
        /// ND-180 (1) hibaosztaly javitasa).
        ///
        /// A HIBA. A v1 a `ringDirections` (8) jelolt irany KOZUL a legjobbat
        /// valasztotta, es ABBA lepett - azaz a lepesirany 45 fokos racsra
        /// volt kvantalva. Merve: a 0. agon 1422 darab 30 foknal nagyobb
        /// iranyvaltas, es a Python-orakulum szintetikus volgyeben a kvantalt
        /// koveto a MEDRET SEM talalta meg (vegig a volgy mellett futott, 565
        /// km-es uton, a folytonos irany 560 km-es utjaval szemben).
        ///
        /// A JAVITAS. Egy `r` sugaru koron egyenletesen mintavett `e_k`
        /// magassagokra a legkisebb-negyzetes sikillesztes gradiense aranyos a
        /// `g = sum_k (e_k - atlag) * d_k` vektorral (a kor elso harmonikusa).
        /// A lejtesirany ennek az ellentettje, normalizalva - ez FOLYTONOS
        /// fuggvenye a domborzatnak, nincs benne tablara kerekites. Teljesen
        /// sima kornel (g = 0) a legjobb jelolt-irany marad, mert akkor nincs
        /// ertelmes gradiens.
        ///
        /// Az atlag kivonasa numerikus okokbol van: a szogfelezett tabla
        /// EGZAKTUL szimmetrikus (ld. <see cref="RiverDirectionTable"/>), tehat
        /// egzakt aritmetikaban a konstans tag kiesne - lebegopontban viszont a
        /// nagy, kozel egyenlo magassagok kivonasa jelentosen javitja a
        /// kondicionalast.
        /// </summary>
        public static (double X, double Y) DescentDirection(
            double[] sensed, (double X, double Y)[] directions, int count, int bestIndex)
        {
            double total = 0.0;
            for (int k = 0; k < count; k++) total += sensed[k];
            double mean = total / count;
            double gx = 0.0, gy = 0.0;
            for (int k = 0; k < count; k++)
            {
                double weight = sensed[k] - mean;
                gx += weight * directions[k].X;
                gy += weight * directions[k].Y;
            }
            double length = Math.Sqrt(gx * gx + gy * gy);
            if (length == 0.0) return directions[bestIndex];
            return (-gx / length, -gy / length);
        }

        /// <summary>
        /// Az escape-szakasz finomitasa (ND-186, az ND-180 (3) hibaosztaly
        /// javitasa).
        ///
        /// A HIBA. A lokalis priority-flood a `escapeCellMeters` (2000 m)
        /// cellameretu racson talalja meg a tulcsordulasi pontot, es a v1 a
        /// racsutvonalat KOZVETLENUL fuzte a nyomvonalhoz - igy 2,0 / 2,828
        /// km-es elek kerultek bele (a normal lepes 50 m). Merve: a t=0 halozat
        /// teljes hosszanak 58,77%-a 75 m-nel hosszabb eleken van, es a
        /// 8-szomszedos racs 45 fokos lepcsoje adta a "szogletes utat".
        ///
        /// MIERT EZ A HELYES JAVITAS (es miert nem dekorativ simitas). A
        /// priority-flood utvonala definicio szerint a `lakeLevel` (az utvonal
        /// legnagyobb nyers elevacioja) ALATT marad, vagyis a medence
        /// feltoltodese utan VIZ ALATT van. Egy ilyen teruleten a fizikai
        /// vizfelszin SIMA: a racs lepcsoje NEM modell-tartalom, hanem a racs
        /// mellekterméke. Ezert ahol egy EGYENES (nagykor) szakasz MINDEN
        /// mintapontja `lakeLevel` alatt marad, ott az egyenes a HELYESEBB
        /// nyomvonal. Ahol az egyenes KIBUKKANNA a vizbol, ott a racsutvonal
        /// reszletei megmaradnak - a dontes tehat MERT, nem feltetelezett.
        ///
        /// A mintavetel surusege `probeMeters` (a durva cellameret fele): a
        /// domborzati dontes maga is a durva racson keszult, ennel finomabb
        /// probalgatas nem ad uj informaciot, viszont kvadratikusan draga. A
        /// KIIRT pontok surusege kulon parameter (`emitMeters`, ld.
        /// <see cref="DefaultContinuousEscapeEmitMeters"/> meresi indoklasat).
        ///
        /// A kimenet a `coarsePath` ELSO pontjat (a pit-et) KIHAGYJA - azt a
        /// hivo mar felvette. (A v1 ezt duplan vette fel, nulla hosszu ellel.)
        /// </summary>
        public static List<(double X, double Y, double Z)> RefineEscapePath<TSampler>(
            TSampler sampler, List<(double X, double Y, double Z)> coarsePath,
            double lakeLevel, double emitMeters, double probeMeters, int maxShortcutCells)
            where TSampler : struct, IElevationSampler
        {
            var output = new List<(double X, double Y, double Z)>();
            int count = coarsePath.Count;
            int anchorIndex = 0;
            while (anchorIndex < count - 1)
            {
                (double X, double Y, double Z) anchor = coarsePath[anchorIndex];
                int best = anchorIndex + 1;
                int limit = Math.Min(count - 1, anchorIndex + maxShortcutCells);
                for (int candidate = anchorIndex + 2; candidate <= limit; candidate++)
                {
                    if (!IsSegmentSubmerged(sampler, anchor, coarsePath[candidate], lakeLevel, probeMeters))
                        break;
                    best = candidate;
                }
                (double X, double Y, double Z) target = coarsePath[best];
                double span = SphereWalk.ChordMeters(anchor, target);
                int pieces = Math.Max(1, (int)(span / emitMeters));
                for (int k = 1; k <= pieces; k++)
                    output.Add(SphereWalk.GeodesicPoint(anchor, target, (double)k / pieces));
                anchorIndex = best;
            }
            return output;
        }

        private static bool IsSegmentSubmerged<TSampler>(
            TSampler sampler, (double X, double Y, double Z) a, (double X, double Y, double Z) b,
            double lakeLevel, double probeMeters)
            where TSampler : struct, IElevationSampler
        {
            double span = SphereWalk.ChordMeters(a, b);
            int probes = Math.Max(1, (int)(span / probeMeters));
            for (int k = 1; k < probes; k++)
            {
                (double X, double Y, double Z) p = SphereWalk.GeodesicPoint(a, b, (double)k / probes);
                if (sampler.Sample(p.X, p.Y, p.Z) > lakeLevel) return false;
            }
            return true;
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
        private static (List<(double X, double Y, double Z)> Path, double SpillwayElevation)? FindContinuousLocalSpillway<TSampler>(
            TSampler sampler,
            (double X, double Y, double Z) pit, double pitElevation,
            double cellMeters, int nodeBudget,
            HashSet<long> pathVisited, int visitedLatticeLevel,
            System.Threading.CancellationToken cancellation)
            where TSampler : struct, IElevationSampler
        {
            cancellation.ThrowIfCancellationRequested();
            SphereWalk.GetTangentBasis(pit, out (double X, double Y, double Z) t1, out (double X, double Y, double Z) t2);
            double cellAngular = cellMeters / PlanetConstants.RadiusMeters;

            (double X, double Y, double Z) CellPos(int i, int j) => SphereWalk.StepInTangentDirection(pit, t1, t2, i, j, cellAngular);
            double CellElev(int i, int j)
            {
                (double X, double Y, double Z) p = CellPos(i, j);
                return sampler.Sample(p.X, p.Y, p.Z);
            }
            bool IsPathVisited(int i, int j)
            {
                (double X, double Y, double Z) p = CellPos(i, j);
                return pathVisited.Contains(CubeFaceLattice.KeyFromPosition(p.X, p.Y, p.Z, visitedLatticeLevel));
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
            System.Threading.CancellationToken cancellation = default,
            DeepTimeContext context = default,
            double mergeRadiusMeters = DefaultMergeRadiusMeters)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            cancellation.ThrowIfCancellationRequested();
            var claimed = new ClaimedRiverPoints(mergeRadiusMeters);
            var rivers = new List<ContinuousRiverPath>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                ContinuousRiverPath river = TraceRiverPathContinuous(
                    worldSeed, seeds, seaLevel, sources[i], i, fineDepth, claimed,
                    stepMeters, sensingRadiusMeters, ringDirections, escapeCellMeters, escapeNodeBudget, maxSteps,
                    cancellation: cancellation, context: context);

                foreach ((double X, double Y, double Z) p in river.Points)
                    claimed.Add(p, i);
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
            System.Threading.CancellationToken cancellation = default,
            int maxDegreeOfParallelism = -1,
            DeepTimeContext context = default,
            Action<ContinuousRiverPath>? onRiverCompleted = null,
            double mergeRadiusMeters = DefaultMergeRadiusMeters,
            double submergedMinDepthMeters = DefaultSubmergedMinDepthMeters,
            double escapeEmitMeters = DefaultContinuousEscapeEmitMeters)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (maxDegreeOfParallelism == 0 || maxDegreeOfParallelism < -1)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
            cancellation.ThrowIfCancellationRequested();

            // 1. FÁZIS: minden folyó ÖNÁLLÓAN, `claimed` nélkül. Tiszta
            // függvények, megosztott állapot nélkül - a sorrend nem számít.
            var traced = new ContinuousRiverPath[sources.Count];
            var parallelOptions = new System.Threading.Tasks.ParallelOptions
            {
                CancellationToken = cancellation,
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
            };
            // 2. FÁZIS: csonkolás FORRÁS-SORRENDBEN - ez reprodukálja a
            // szekvenciális `claimed` szemantikát.
            var claimed = new ClaimedRiverPoints(mergeRadiusMeters);
            var rivers = new List<ContinuousRiverPath>(sources.Count);
            void CommitReady()
            {
                while (rivers.Count < sources.Count && traced[rivers.Count] != null)
                {
                    int i = rivers.Count;
                    cancellation.ThrowIfCancellationRequested();
                    ContinuousRiverPath full = traced[i];

                    var river = new ContinuousRiverPath { SourceIndex = i };
                    int mergeAt = -1;
                    ClaimedRiverPoint mergeOwner = default;
                    // CSAK azokon a pontokon ellenorzunk, ahol a szekvencialis
                    // koveto is ellenorzott volna (ld. ClaimCheckIndices) - az
                    // elso (step == 0) kihagyva, ahogy ott is.
                    for (int c = 1; c < full.ClaimCheckIndices.Count; c++)
                    {
                        int index = full.ClaimCheckIndices[c];
                        if ((uint)index >= (uint)full.Points.Count) break;
                        (double X, double Y, double Z) point = full.Points[index];
                        if (claimed.TryFindNearest(point, out ClaimedRiverPoint owner))
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
                        // ND-187: a viz alatti tartomanyokat UGYANOTT vagjuk el,
                        // ahol az agat. A hozzafuzott befogado pont (mergeAt + 1)
                        // mar a MASIK folyo pontja - azt nem jeloljuk.
                        for (int k = 0; k < full.SubmergedSpans.Count; k++)
                        {
                            (int Start, int End) span = full.SubmergedSpans[k];
                            if (span.Start > mergeAt) break;
                            int end = span.End < mergeAt ? span.End : mergeAt;
                            if (end > span.Start) river.SubmergedSpans.Add((span.Start, end));
                        }
                    }
                    else
                    {
                        river.Points.AddRange(full.Points);
                        river.ClaimCheckIndices.AddRange(full.ClaimCheckIndices);
                        river.SubmergedSpans.AddRange(full.SubmergedSpans);
                        river.Termination = full.Termination;
                    }

                    foreach ((double X, double Y, double Z) p in river.Points)
                        claimed.Add(p, i);
                    rivers.Add(river);
                    traced[i] = null!;
                    onRiverCompleted?.Invoke(river);
                }
            }
            var commitGate = new object();
            System.Threading.Tasks.Parallel.For(0, sources.Count, parallelOptions, i =>
            {
                ContinuousRiverPath full = TraceRiverPathContinuous(
                    worldSeed, seeds, seaLevel, sources[i], i, fineDepth,
                    claimed: null,
                    stepMeters, sensingRadiusMeters, ringDirections,
                    escapeCellMeters, escapeNodeBudget, maxSteps,
                    DefaultVisitedLatticeLevel, cancellation, context: context,
                    escapeEmitMeters: escapeEmitMeters,
                    submergedMinDepthMeters: submergedMinDepthMeters);
                // ND-177: kesz utak atadasa es commit kizárólag e kapu alatt.
                // A koveto nem olvas claimed-et, igy a ket fazis atfedhet.
                lock (commitGate)
                {
                    traced[i] = full;
                    CommitReady();
                }
            });
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

        // A gombi erintobazis es a lepes-kepletek atkerultek a
        // `SphereWalk` osztalyba (ND-186), BIT-SEMLEGESEN - igy az
        // osszefolyasi terbeli index es az escape-finomitas is UGYANAZT a
        // kepletet hasznalja.

        /// <summary>
        /// A teljes lánc: forrás-kiválasztás -> nyomvonal-követés minden
        /// forrásra, dendritikus egyesüléssel (a `claimed` térkép megosztott
        /// az összes forrás között). A hívó (pl. a viewer) az elevációt/
        /// csapadékot/óceán-mezőt/tengerszintet a MÁR meglévő
        /// <c>Climate.MoisturePrecipitation.Compute</c>-ból adja (nincs
        /// duplikált számítás).
        /// </summary>
        public static List<RiverPath> BuildRiverNetwork(
            ulong worldSeed, (double X, double Y, double Z)[] seeds,
            Dictionary<TileId, double> elevField, Dictionary<TileId, double> precipField,
            Dictionary<TileId, bool> isOcean, double seaLevel,
            int fineDepth = DefaultFineDepth, int topK = DefaultSourceTopK,
            double minElevAboveSeaM = DefaultMinElevAboveSeaM,
            double precipPercentile = DefaultPrecipPercentile,
            int maxSteps = DefaultMaxSteps, int escapeNodeBudget = DefaultEscapeNodeBudget,
            DeepTimeContext context = default)
        {
            List<TileId> sources = SelectRiverSources(
                elevField, precipField, isOcean, seaLevel, topK, minElevAboveSeaM, precipPercentile);
            return BuildRiverNetworkFromSources(worldSeed, seeds, seaLevel, sources, fineDepth, maxSteps, escapeNodeBudget, context);
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
            int? sourceBudget = null,
            double minElevAboveSeaM = DefaultMinElevAboveSeaM,
            double minSeparationMeters = DefaultSourceSeparationMeters,
            int maxSteps = DefaultMaxSteps, int escapeNodeBudget = DefaultEscapeNodeBudget,
            DeepTimeContext context = default)
        {
            FlowNetwork.FloodResult flood = FlowNetwork.PriorityFlood(elevField, isOcean);
            List<TileId> sources = SelectRiverSourcesPerBasin(
                elevField, precipField, isOcean, flood.Parent, seaLevel,
                basinCount, sourceBudget, minElevAboveSeaM, minSeparationMeters);
            return BuildRiverNetworkFromSources(worldSeed, seeds, seaLevel, sources, fineDepth, maxSteps, escapeNodeBudget, context);
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
            int maxSteps = DefaultMaxSteps, int escapeNodeBudget = DefaultEscapeNodeBudget,
            DeepTimeContext context = default)
        {
            var elevCache = new ElevationCache(
                worldSeed, seeds, context);
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
