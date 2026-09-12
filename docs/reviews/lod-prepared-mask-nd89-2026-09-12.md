# ND-87 visszamérés és ND-89 — Előkészített terepmaszk

## Mit mutat az új próba?

Forrás: `unity/WorldGenViewer/Logs/PerfLog_20260912_023354.txt`,
összehasonlítás: `PerfLog_20260912_015131.txt`. Az új mintában 257/257 commit
ND87, 511 feltöltési szelet; nincs single-fallback vagy naplózott
exception/diagnosztikai hiba. 27 elavult workerkérés és két staging-sor
eldobódott. Ez működési bizonyíték, nem teljes vizuális/FPS-elfogadás.

Kvantilisek: rendezett értékek, `floor((n-1)*p)` index. A kameraút és a
terhelés erősen eltér: a dinamikus levelek mediánja 10703→300, maximuma
32466→49359. A táblázat ezért **nem kontrollált A/B gyorsulásmérés**.

| Mért szakasz | ND-86 medián / p90 / max | ND-87 medián / p90 / max |
|---|---:|---:|
| Terep publikálása | 0,63 / 3,01 / 6,30 ms | 0,08 / 1,15 / 1,98 ms |
| Teljes commit | 2,01 / 4,97 / 7,88 ms | 0,94 / 3,21 / 9,85 ms |
| Terepmaszk | 0,28 / 1,02 / 2,05 ms | 0,05 / 1,00 / 7,95 ms |
| Staging-szelet | 0,85 / 1,19 / 4,02 ms | 0,90 / 1,85 / 25,76 ms |
| Teljes kéréskésés | 669,3 / 823,2 / 1404,7 ms | 289,3 / 784,7 / 3427,1 ms |

Az új rendercél-előkészítés működik, a terep publikálási csúcsa alacsonyabb.
A leglassabb commitban azonban már **7,95 ms a terepmaszk**, miközben a
terep publikálása 0,92 ms, a külső vízszakasz 0,48 ms; 8878 rejtett
base-tile, 11820 feltöltött index. Nem ismert még a maszkidő CPU/natív aránya.

**A teljes főszálas akadás nem tekinthető megoldottnak:** frame 13050-ben
egy staging-szelet 25,76 ms (`jobs=26`, `done=114/183`). Az érintett kérés
cél-előkészítése összesen 28,40 ms, 121 új/59 újrahasznált céllal; staging
összesen 32,42 ms, commit 2,82 ms. Az ok nem izolált (objektumkészítés,
allokáció/GC vagy más megakadás); ebből nem állítható, hogy egyetlen konkrét
Unity-hívás volt 25 ms. Ez külön nyitott kockázat, nem fedjük el jobb mediánnal.

## Elkészült következő lépés

A `TerrainIndexMask` most módosítás nélkül készít elő egy következő állapotot:
validált és másolt gyökérhalmaz, visszaállítandó/elrejtendő quad-offsetek,
rendezett és a régi szabállyal összevont feltöltési tartományok. A terv nem
írja az élő CPU-indexeket és nem változtatja meg a látható fedést.

A főszálas staging-sor végén készül el ez a terv, és változott fedésnél a
következő rajzdiagnosztikai snapshot. A commit csak alkalmazza az offseteket,
elvégzi a meglévő részleges indexfeltöltést és publikálja a snapshotot.
Változatlan fedésnél nincs snapshot-újraépítés vagy natív feltöltés. A terv
tulajdonos-/revízióellenőrzése az első írás előtt kiszűri az idegen, elavult
és ismételten alkalmazott eredményt. El nem fogadott terv eldobható anélkül,
hogy az élő maszkot vissza kellene állítani.

Nincs új GPU-mesh, teljes statikus indexbuffer-másolat vagy workerből írt
Unity-állapot. A CPU-terv plusz tárhelye az elrejtett gyökerekhez és a változott
quad-offsetekhez arányos. A legacy `SetHidden` API prepare+apply kompozíció
lett; ugyanazokat az indexeket/range-eket adja, némi terv-allokációs többlettel.
A vízmaszk hívási útja és a meglévő commit-hibafallback nem változott.

**Várható hatás:** a gyökérvalidálás, halmazkülönbség, rendezés/range-képzés
és a diagnosztikai snapshot kikerül a commitból. Hogy ez mennyit nyer a
7,95 ms-ból, még nem bizonyított. A natív indexfeltöltés és a CPU-indexek
átírása marad egy commit-frame-ben. A maszktervezés is egy monolitikus
staging-job, túllépheti a 2 ms-os puha keretet; a staging késése nőhet.
Ez nem a teljes háttérmunka vagy az élességi/proxyhiba megoldása.

## Új log és próbamenet

A begin/slice sorok `pipeline=ND89` jelölésűek. Az async apply továbbra is
`terrainPipeline=ND87`, `auxPipeline=ND86`, emellett:

- `terrainMaskMode=ND89`: valóban előkészített terepmaszk alkalmazása;
- `maskPlan`: stagingbeli terv és diagnosztikai snapshot együtt;
- `maskApply`: CPU-indexek írása; single módban a tervezést is tartalmazza;
- `maskUpload`: natív indexfeltöltés és a mesh lekérése;
- `maskSnapshot`: kész snapshot publikálása; single módban készítése is;
- `maskRanges`: natív feltöltési tartományok száma, a `maskIndices` a mennyiség.

A `maskPlan` a `stageTotal` része, nem része a commit `terrainMask` idejének.
A többi maszk-részidő a `terrainMask` részhalmaza; nem összeadandó még egyszer.
A staging során naplózott rajzméret továbbra is a régi publikált fedést méri.

Kérjük a szokásos zoom/visszazoomot, oldalirányú mozgást új területre, partot
és vizet. Ellenőrizendő: a statikus/fine váltásnál nincs idő előtt eltűnő
terep, lyuk vagy villanás, visszazoomkor a régi base-indexek visszaállnak.
Világváltás/Play leállítás félkész staging mellett se publikáljon régi tervet.
A következő logból a commitot, maskPlan/maskApply/maskUpload időt, range-
számot, legnagyobb staging-szeletet és teljes requestAge-et együtt értékeljük.

**Párhuzamos változás:** a másik munkaszál ND-88 fizikai relief-skálázása és
scene-módosítása megmaradt. A saját maszkdöntést ezért ND-89-re neveztük át;
az ott hiányzó `WorldGen.Core` importot a sikeres fordításhoz pótoltuk.
A most elemzett log még `elevationScale=0.001`, `reliefExaggeration=1.5`;
eltérő következő skálázás más geometriát és terhelést jelent, ezért azt
nem szabad tiszta ND-89 teljesítmény-összehasonlításként kezelni.

## Ellenőrzés és készültség

Hét új célzott .NET-eset sikeres: előkészítés láthatatlansága, pontos
visszaállítás, idegen/elavult/ismételt terv elutasítása, bemenet/snapshot
elkülönítése, invalid/missing/null bemenet, valamint 20 lépéses, két
bejárási sorrendű indexellenőrzés függetlenül előállított elvárt tömbbel.
Teljes .NET solution **666/666 PASS** (381 Core + 278 viewer-LOD + 7 CLI),
solution build 0 hiba/0 warning. Viewer-LOD Release **278/278 PASS**.
Unity-forrásfordítás 0 hiba/83 korábbi
warning; Editor LOD-tesztassembly 0 hiba/4 korábbi warning. Új valódi,
két submesh-es indexbuffer-teszt fordított, **Editorban nem futott**.
A Unity-kompilációhoz a korábbi ignored validációs target és a parancssori
TreatWarningsAsErrors=false beállítás kellett; a repo szigorát nem enyhítettük.

Core/seed/referencia változatlan. Élő ND-89 vizuális/FPS-elfogadás nincs.
M9 tartalmilag durván 60–70%. Ráfordítás-egyenérték erre a lépésre 2–4 óra,
élő validáció/korrekció 1–3 óra, fennmaradó zoom/render munka 8–20 óra;
ezek durva, nem mért becslések.
