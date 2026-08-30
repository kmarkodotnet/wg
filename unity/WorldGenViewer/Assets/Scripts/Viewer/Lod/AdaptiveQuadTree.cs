using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// M9 (docs/05-milestones.md §9.1) adaptiv, kamera-vezerelt kvadfa-LOD
    /// kivalasztasa a MAR letezo cubed-sphere racson (TileId.Parent()/
    /// Child(), TileNeighbors) - nincs uj koordinata-rendszer, a kvadfa a
    /// meglevo Morton-hierarchiara ul ra (ld. TileId doksi).
    ///
    /// TISZTA, MOTORFUGGETLEN logika: nincs UnityEngine referencia (ld. az
    /// asmdef noEngineReferences=true beallitasat), csak a WorldGen.Core
    /// Grid-modulra epul - ezert plain NUnit/dotnet alol is tesztelheto,
    /// Unity Editor futtatasa nelkul is (bar a projektben ELERHETO a Unity
    /// Editor, es EditMode teszt is keszult hozza, ld. Assets/Tests/EditMode/Lod).
    ///
    /// KOORDINATA-KONVENCIO: minden pozicio-parameter a WorldGen.Core sajat
    /// "test-keret" konvenciojaban ertendo (ugyanaz, mint a TileGeometry-e),
    /// NEM a Unity-re csavart (BodyFrameConversion utani) konvencioban - a
    /// hivo (PlanetGridMesh) felelossege az atvaltas. Igy ez a modul semmit
    /// nem tud a tengelycserelrol, csak sima haromdimenzios tavolsagokkal
    /// szamol - a hivo altal atadott `planetRadius`-szal skalazva.
    ///
    /// HISZTEREZIS ES REPRODUKALHATOSAG - NYITOTT TERVEZESI DONTES,
    /// EXPLICIT DOKUMENTALVA (mert a ket kovetelmeny latszolag ellentmond
    /// egymasnak, ld. docs/05-milestones.md §9.1 "Reprodukalhatosag" sora):
    ///
    /// A hiszterezishez (K_merge &gt; K_split, hogy egy csomopont a kuszob
    /// ket oldalan ne pattogjon frame-rol frame-re) SZUKSEG van az ELOZO
    /// keret aktiv-level allapotara. Ez elsore ellentetesnek tunik azzal,
    /// hogy "ugyanaz a kamera-pozicio -&gt; mindig ugyanaz a cut", FUGGETLENUL
    /// az odajutas utjatol. A feloldas: az elozo keret cut-ja EXPLICIT, a
    /// hivo altal atadott BEMENETI PARAMETER (nem rejtett, modul-szintu
    /// mutable allapot, ld. I2 - a fuggveny maga innentol is tiszta: nincs
    /// static mezo, determinisztikus a KET bemenetbol). A "reprodukalhatosag"
    /// property emiatt pontosan igy ertendo es tesztelt (ld.
    /// AdaptiveQuadTreeTests):
    ///   1. (kamera-pozicio, elozo cut) par -&gt; MINDIG bitre ugyanaz az uj cut.
    ///   2. HIDEG inditas (elozo cut = ures/null) egy adott poziciora is
    ///      MINDIG bitre ugyanazt a cut-ot adja.
    /// Egy adott vegso poziciora, KULONBOZO elozmennyel (pl. felulrol vagy
    /// alulrol kozelitve a hiszterezis-savot), a fuggveny elvben KETFELE, de
    /// mindket iranyban STABIL (tovabb NEM oszcillalo) cut-ot adhat a
    /// K_split..K_merge savon belul - ez SZANDEKOS, maga a hiszterezis
    /// celja, es a milestone "monoton kamera-ut" tesztje pontosan ezt a
    /// stabilitast varja el, NEM az elozmenytol fuggetlen egyertekuseget.
    /// </summary>
    public static class AdaptiveQuadTree
    {
        public const int DefaultBaseLevel = 2;
        public const int DefaultMaxLevel = 11;
        public const double DefaultSplitFactor = 2.5;
        public const double DefaultMergeFactor = DefaultSplitFactor * 1.5;

        /// <summary>
        /// Nincs latokup-szures (a teljes gomb "lathatonak" szamit) - ez az
        /// alapertelmezes a visszafele-kompatibilitashoz (pl. a meglevo
        /// egyseg-tesztek, amik nem adnak meg kamera-iranyt).
        /// </summary>
        public const double NoCullingHalfFovRadians = Math.PI;

        /// <summary>
        /// A teljes M9 lepes: nyers kivalasztas (<see cref="SelectCut"/>),
        /// majd 2:1 kiegyensulyozas (<see cref="EnforceRestrictedBalance"/>).
        /// Ezt hivja a viewer - a ket reszlepes kulon-kulon is publikus a
        /// celzott tesztelhetoseg miatt.
        /// </summary>
        public static HashSet<TileId> BuildCut(
            double cameraX, double cameraY, double cameraZ,
            double planetRadius,
            IReadOnlyCollection<TileId> previousCut,
            int baseLevel = DefaultBaseLevel,
            int maxLevel = DefaultMaxLevel,
            double splitFactor = DefaultSplitFactor,
            double mergeFactor = DefaultMergeFactor,
            double forwardX = 0.0, double forwardY = 0.0, double forwardZ = 1.0,
            double halfFovRadians = NoCullingHalfFovRadians)
        {
            HashSet<TileId> cut = SelectCut(
                cameraX, cameraY, cameraZ, planetRadius, previousCut,
                baseLevel, maxLevel, splitFactor, mergeFactor,
                forwardX, forwardY, forwardZ, halfFovRadians);
            EnforceRestrictedBalance(cut, baseLevel);
            return cut;
        }

        /// <summary>
        /// Top-down kvadfa-bejaras a base-level gyokerektol: minden
        /// csomopontnal a kamera-tavolsag es a csomopont befoglalo-sugara
        /// alapjan dont a felbontasrol, hiszterezissel (ld. osztaly-doc).
        /// NEM vegzi el a 2:1 kiegyensulyozast - ld. <see cref="BuildCut"/>.
        ///
        /// LATOKUP-SZURES (forwardX/Y/Z + halfFovRadians): a BASE LEVEL
        /// partíció MINDIG teljes (a gomb minden pontjan van legalabb egy
        /// leaf - ld. a §2.4 NoGaps-elvet, ez itt is garantalt), de a
        /// FINOMODAS (base level feletti felbontas) csak azokra a
        /// csomopontokra tortenik, amik a kamera latokupjaban (vagy annak
        /// kozeleben) vannak. Enelkul egy a felszinhez nagyon kozeli
        /// kamera (kis magassag) a KOROTTE levo TELJES korlapot finomitana
        /// - elore, hatra, oldalra egyarant -, holott a hatra/oldalra eso
        /// resz sosem jelenik meg a kepernyon. Ez okozott korabban
        /// robbanasszeru, de haszontalan tile-szamot.
        /// </summary>
        public static HashSet<TileId> SelectCut(
            double cameraX, double cameraY, double cameraZ,
            double planetRadius,
            IReadOnlyCollection<TileId> previousCut,
            int baseLevel = DefaultBaseLevel,
            int maxLevel = DefaultMaxLevel,
            double splitFactor = DefaultSplitFactor,
            double mergeFactor = DefaultMergeFactor,
            double forwardX = 0.0, double forwardY = 0.0, double forwardZ = 1.0,
            double halfFovRadians = NoCullingHalfFovRadians)
        {
            if (baseLevel < 0 || baseLevel > maxLevel)
                throw new ArgumentOutOfRangeException(nameof(baseLevel), "A baseLevel 0..maxLevel tartomanyban lehet.");
            if (maxLevel > TileId.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(maxLevel), $"A maxLevel legfeljebb {TileId.MaxLevel} lehet.");
            if (splitFactor <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(splitFactor), "A splitFactor-nak pozitivnak kell lennie.");
            if (mergeFactor < splitFactor)
                throw new ArgumentOutOfRangeException(nameof(mergeFactor), "A mergeFactor nem lehet kisebb a splitFactor-nal (hiszterezis).");

            double fwdLen = Math.Sqrt(forwardX * forwardX + forwardY * forwardY + forwardZ * forwardZ);
            if (fwdLen < 1e-9)
                throw new ArgumentException("A forward iranyvektor nem lehet nulla hosszu.");
            forwardX /= fwdLen; forwardY /= fwdLen; forwardZ /= fwdLen;

            HashSet<TileId> previousExpanded = BuildExpandedAncestorSet(previousCut, baseLevel);

            // Parhuzamositas (gpu-calc): a base-level gyokerek EGYMASTOL
            // FUGGETLENUL bejarhatok (a Visit() rekurzio csak OLVASSA a
            // `previousExpanded`-et, es csak HOZZAAD a kimenethez - nincs
            // megosztott mutable allapot, amit at kellene rendezni). Egy
            // ConcurrentBag-be gyujtunk (szalbiztos Add), a vegen egyszer
            // alakitjuk HashSet-te. Mert szukseges: nagy `splitFactor`
            // mellett a SelectCut onmagaban is tobb-tíz-milliszekundumos
            // koltseg lehet (merve), es a base-level gyokerek szama
            // (6*4^baseLevel) tobbnyire jocskan meghaladja a magok szamat,
            // tehat joul skalazodik.
            uint baseN = baseLevel == 0 ? 1u : (1u << baseLevel);
            int rootsPerFace = (int)(baseN * baseN);
            int totalRoots = 6 * rootsPerFace;
            var bag = new System.Collections.Concurrent.ConcurrentBag<TileId>();

            // OLCSO ELOSZURES (kritikus nagy baseLevel-nel, pl. 8 = 393216
            // gyoker): a `Visit()` MAGA draga (GetCenterAndBoundingRadius 4
            // tan/atan-hasznalo TileGeometry-hivassal + latokup-teszt), es
            // baseLevel=8-nal ezt mind a 393216 gyokerre lefuttatni akkor
            // is, ha a VEGEREDMENY "marad alap szinten" - mert 80-320ms-et
            // vett igenybe MINDEN egyes ujraepitesnel (merve), fuggetlenul
            // attol, hogy a mesh-epites mar csak a finomitott reszt erinti.
            // A javitas: egy OLCSO also-becsles (csak TileGeometry.ToPosition,
            // NEM a teljes befoglalo-sugar-szamitas) kiszamitja, hogy egy
            // adott base-level gyoker LEHETSEGES-e egyaltalan hogy finomodjon
            // (a tavolsaga a kamera-tol < mergeFactor * a legnagyobb
            // lehetseges rTile ezen a szinten) - ha nem, a DRAGA Visit()
            // teljesen kihagyhato, a gyoker egyszeruen "marad alap" (a bag-be
            // kerul modositas nelkul). A hatarertek KONZERVATIV (a tenyleges
            // max/min terulet-arany felulrol korlatos ND-24 szerint, itt egy
            // biztonsagos 2x szorzoval), tehat SOSEM zar ki egy olyan
            // gyokeret, aminek ténylegesen finomodnia kellene - csak a
            // egyertelmuen tavoli, semmikepp nem finomodo gyokereknel sporol.
            GetCenterAndBoundingRadius(
                TileId.FromFaceLevelUV(0, baseLevel, 0, 0), planetRadius,
                out _, out _, out _, out double sampleRTile);
            double conservativeMaxRTile = sampleRTile * 2.0;
            double maxRelevantDistance = mergeFactor * conservativeMaxRTile;
            double maxRelevantDistanceSq = maxRelevantDistance * maxRelevantDistance;

            System.Threading.Tasks.Parallel.For(0, totalRoots, rootIndex =>
            {
                int face = rootIndex / rootsPerFace;
                int withinFace = rootIndex % rootsPerFace;
                uint u = (uint)(withinFace / (int)baseN);
                uint v = (uint)(withinFace % (int)baseN);

                TileId root = TileId.FromFaceLevelUV(face, baseLevel, u, v);

                TileGeometry.ToPosition(root, out double rx, out double ry, out double rz);
                double ccx = rx * planetRadius, ccy = ry * planetRadius, ccz = rz * planetRadius;
                double ddx = cameraX - ccx, ddy = cameraY - ccy, ddz = cameraZ - ccz;
                double distSq = ddx * ddx + ddy * ddy + ddz * ddz;

                if (distSq > maxRelevantDistanceSq)
                {
                    bag.Add(root); // garantaltan tul messze van barmilyen finomodashoz
                    return;
                }

                Visit(root, cameraX, cameraY, cameraZ, planetRadius,
                    previousExpanded, maxLevel, splitFactor, mergeFactor,
                    forwardX, forwardY, forwardZ, halfFovRadians, bag);
            });

            return new HashSet<TileId>(bag);
        }

        private static void Visit(
            TileId node,
            double cameraX, double cameraY, double cameraZ, double planetRadius,
            HashSet<TileId> previousExpanded, int maxLevel, double splitFactor, double mergeFactor,
            double forwardX, double forwardY, double forwardZ, double halfFovRadians,
            System.Collections.Concurrent.ConcurrentBag<TileId> cut)
        {
            if (node.Level >= maxLevel)
            {
                cut.Add(node);
                return;
            }

            GetCenterAndBoundingRadius(node, planetRadius, out double cx, out double cy, out double cz, out double rTile);
            double dx = cameraX - cx, dy = cameraY - cy, dz = cameraZ - cz;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // Hiszterezis: ha ez a csomopont az ELOZO keretben mar fel volt
            // bontva (valamelyik leszarmazottja aktiv volt), a nagyobb
            // K_merge kuszob kell az OSSZEVONASHOZ; ha nem volt felbontva
            // (leaf volt vagy meg nem letezett a bejarasban), a kisebb
            // K_split eleg a felbontashoz. Ld. osztaly-doc a "miert nem
            // sertei ez a tisztasagot" targyalasahoz.
            double threshold = previousExpanded.Contains(node) ? mergeFactor : splitFactor;
            bool distanceWantsExpand = distance < threshold * rTile;

            // Latokup-szures: a tavolsag-kriterium onmagaban NEM eleg -
            // finomitas csak akkor tortenik, ha a csomopont (a SAJAT
            // szogmeretevel bovitett kuszobbel) ténylegesen a kamera
            // latokupjaban van. A sajat szogmeret hozzaadasa azert kell,
            // hogy egy nagy, meg a kup szelen levo csomopont ne essen ki
            // tul korán (a kozeppontja mar kicsit kivul lehet, mig a
            // teste meg reszben belul).
            bool expand = distanceWantsExpand
                && IsWithinViewCone(cx, cy, cz, rTile, cameraX, cameraY, cameraZ, forwardX, forwardY, forwardZ, halfFovRadians);

            if (!expand)
            {
                cut.Add(node);
                return;
            }

            for (int i = 0; i < 4; i++)
                Visit(node.Child(i), cameraX, cameraY, cameraZ, planetRadius,
                    previousExpanded, maxLevel, splitFactor, mergeFactor,
                    forwardX, forwardY, forwardZ, halfFovRadians, cut);
        }

        /// <summary>
        /// Egy csomopont (gomb-kozeppont + befoglalo sugar) a kamera
        /// latokupjaban van-e - kup-teszt, a csomopont SAJAT szogmeretevel
        /// (angularRadius) bovitve, hogy a kup szelen levo, meg reszben
        /// lathato csomopontok ne essenek ki tul korán. `halfFovRadians`
        /// tartalmazza a hivo altal mar hozzaadott biztonsagi ratartast
        /// (pl. keplet-oldali FOV/aspect + margo) - ez a fuggveny mar csak
        /// a nyers geometriai osszehasonlitast vegzi.
        /// </summary>
        internal static bool IsWithinViewCone(
            double nodeX, double nodeY, double nodeZ, double rTile,
            double cameraX, double cameraY, double cameraZ,
            double forwardX, double forwardY, double forwardZ,
            double halfFovRadians)
        {
            if (halfFovRadians >= Math.PI)
                return true; // nincs szures (a hivo explicit "teljes gomb lathato"-t kert)

            double dx = nodeX - cameraX, dy = nodeY - cameraY, dz = nodeZ - cameraZ;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < 1e-9)
                return true; // a kamera a csomopont belsejeben van - trivialisan "lathato"

            double cosAngle = (dx * forwardX + dy * forwardY + dz * forwardZ) / distance;
            cosAngle = cosAngle < -1.0 ? -1.0 : (cosAngle > 1.0 ? 1.0 : cosAngle);
            double angleToCenter = Math.Acos(cosAngle);

            double angularRadius = Math.Atan2(rTile, distance);
            return angleToCenter <= halfFovRadians + angularRadius;
        }

        /// <summary>
        /// Az elozo cut minden levelenek OSSZES ose (a base level-ig) - ez
        /// azoknak a csomopontoknak a halmaza, amik az ELOZO keretben "fel
        /// voltak bontva" (nem maguk voltak a leaf, hanem valamelyik
        /// leszarmazottjuk). Egyszeri, olcso elokeszites (leaf-szam * melyseg),
        /// hogy a bejaras soran O(1) legyen a "wasExpanded" lekerdezes.
        /// </summary>
        private static HashSet<TileId> BuildExpandedAncestorSet(IReadOnlyCollection<TileId> previousCut, int baseLevel)
        {
            var expanded = new HashSet<TileId>();
            if (previousCut == null)
                return expanded;

            foreach (TileId leaf in previousCut)
            {
                TileId current = leaf;
                while (current.Level > baseLevel)
                {
                    current = current.Parent();
                    expanded.Add(current);
                }
            }
            return expanded;
        }

        /// <summary>
        /// A csomopont KOZEPE (TileGeometry.ToPosition szerinti egysegvektor
        /// * planetRadius) es befoglalo sugara (a negy SAROK tavolsaganak
        /// maximuma a kozepponttol) - UGYANAZ a geometria, amit a tenyleges
        /// mesh-epites is hasznal (ld. PlanetGridMesh), tehat a LOD-dontes
        /// es a megjelenitett geometria sosem ter el egymastol.
        /// </summary>
        public static void GetCenterAndBoundingRadius(
            TileId node, double planetRadius,
            out double centerX, out double centerY, out double centerZ, out double boundingRadius)
        {
            TileGeometry.ToPosition(node, out double cx, out double cy, out double cz);
            centerX = cx * planetRadius;
            centerY = cy * planetRadius;
            centerZ = cz * planetRadius;

            TileGeometry.GetContinuousBounds(node, out double uMin, out double uMax, out double vMin, out double vMax);

            double maxCornerDistSq = 0.0;
            for (int ui = 0; ui < 2; ui++)
            {
                double uc = ui == 0 ? uMin : uMax;
                for (int vi = 0; vi < 2; vi++)
                {
                    double vc = vi == 0 ? vMin : vMax;
                    TileGeometry.PositionFromFaceUV(node.Face, uc, vc, out double px, out double py, out double pz);
                    double dx = px * planetRadius - centerX;
                    double dy = py * planetRadius - centerY;
                    double dz = pz * planetRadius - centerZ;
                    double distSq = dx * dx + dy * dy + dz * dz;
                    if (distSq > maxCornerDistSq) maxCornerDistSq = distSq;
                }
            }
            boundingRadius = Math.Sqrt(maxCornerDistSq);
        }

        /// <summary>
        /// 2:1 kiegyensulyozott (restricted) kvadfa kikenyszeritese a
        /// varratmentes LOD-hatarokhoz (docs/05-milestones.md §9.1): ha egy
        /// aktiv level el-szomszedja TOBB MINT 1 szinttel durvabb, azt a
        /// durvabb szulot kenyszer-felbontjuk (a 4 gyereket teve a cut-ba
        /// helyette) - fixpontig iteralva, mert egy felbontas ujabb, MASIK
        /// szomszednal okozhat egyensulytalansagot. A MAR verifikalt
        /// TileNeighbors tablat hasznalja (M2.4) - nincs uj szomszedsag-
        /// matek. Csak a DURVA oldalt bontjuk fel, a finomat SOSEM vonjuk
        /// ossze - ez garantalja a fixpontig-terminalast (a felbontasok
        /// szama felulrol korlatos: minden csomopont legfeljebb maxLevel-ig
        /// bonthato).
        /// </summary>
        internal static void EnforceRestrictedBalance(HashSet<TileId> cut, int baseLevel)
        {
            bool changed;
            do
            {
                changed = false;

                // OLCSO ELOSZURES (kritikus nagy baseLevel-nel, pl. 8-nal a
                // cut 393k+ elemet is tartalmazhat, de a finomitott resz
                // csak nehany ezer): a TAVOLI, tisztan base-szintu tile-ok
                // MINDIG egyensulyban vannak egymassal (0 a level-kulonbseg),
                // tehat csak a level>baseLevel (finomitott) tile-okat ES az
                // O SAME-LEVEL SZOMSZEDJAIKAT (a hatar, ahol egyensulytalansag
                // egyaltalan felmerulhet) erdemes a draga TileNeighbors.
                // Neighbor (tan/atan) hivasokkal ellenorizni. A `cut`
                // teljes bejarasa itt megmarad (kell a level-szures miatt),
                // de EZ csak egy OLCSO level-osszehasonlitas HashSet-be
                // gyujtessel - a DRAGA resz (szomszed-keresés) mar csak a
                // sokkal kisebb jelolt-halmazon fut.
                var candidates = new HashSet<TileId>();
                foreach (TileId t in cut)
                {
                    if (t.Level <= baseLevel)
                        continue;
                    candidates.Add(t);
                    for (int d = 0; d < 4; d++)
                    {
                        TileId neighbor = TileNeighbors.Neighbor(t, (TileDirection)d);
                        if (TryFindCoveringAncestor(neighbor, cut, out TileId covering))
                            candidates.Add(covering);
                    }
                }

                // Parhuzamositas (gpu-calc): a SZOMSZED-KERESES (TileNeighbors.
                // Neighbor, ami a ND-24 szerint dokumentaltan draga tan/atan
                // hivasokat hasznal) es a fedo-os keresese TISZTAN OLVASSA a
                // `cut`-ot ebben a fazisban (nincs meg mutacio) - ezert
                // biztonsagosan parhuzamosithato. Csak a TENYLEGES felbontast
                // (SplitOnce, ami ir a `cut`-ba) vegezzuk egyszalon, utana,
                // mert a HashSet<T> nem szalbiztos irasra. Merve: ez a fazis
                // volt a legdragabb resz (akar 80+ ms egy nagy cut-nal),
                // dominalva a teljes adaptiv ujraepites koltseget.
                var toSplit = new System.Collections.Concurrent.ConcurrentDictionary<TileId, byte>();
                System.Threading.Tasks.Parallel.ForEach(candidates, leaf =>
                {
                    if (!cut.Contains(leaf))
                        return; // korabbi iteracios lepesben mar kicserelodott (a szulo felbomlott)

                    for (int dirIndex = 0; dirIndex < 4; dirIndex++)
                    {
                        TileId sameLevelNeighbor = TileNeighbors.Neighbor(leaf, (TileDirection)dirIndex);
                        if (TryFindCoveringAncestor(sameLevelNeighbor, cut, out TileId coveringAncestor)
                            && leaf.Level - coveringAncestor.Level > 1)
                        {
                            toSplit.TryAdd(coveringAncestor, 0);
                        }
                    }
                });

                foreach (TileId ancestor in toSplit.Keys)
                {
                    if (cut.Contains(ancestor))
                    {
                        SplitOnce(cut, ancestor);
                        changed = true;
                    }
                }
            } while (changed);
        }

        /// <summary>
        /// Megkeresi azt a csomopontot a cut-ban, ami LEFEDI a megadott
        /// (barmilyen szintu) TileId-t - azaz maga a TileId, vagy annak
        /// valamelyik ose. Ha a cut-beli fedes egy LESZARMAZOTTJA (a
        /// szomszed oldala FINOMABB, nem durvabb), a fuggveny false-t ad
        /// vissza - ez a szandekolt viselkedes, ld. az EnforceRestrictedBalance
        /// dokumentaciojat (a finomabb oldal a masik iranybol kerul
        /// ellenorzesre, szimmetrikusan).
        /// </summary>
        private static bool TryFindCoveringAncestor(TileId sameLevelId, HashSet<TileId> cut, out TileId covering)
        {
            TileId current = sameLevelId;
            while (true)
            {
                if (cut.Contains(current))
                {
                    covering = current;
                    return true;
                }
                if (current.Level == 0)
                {
                    covering = default;
                    return false;
                }
                current = current.Parent();
            }
        }

        private static void SplitOnce(HashSet<TileId> cut, TileId node)
        {
            cut.Remove(node);
            for (int i = 0; i < 4; i++)
                cut.Add(node.Child(i));
        }
    }
}
