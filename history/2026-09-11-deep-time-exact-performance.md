# Deep-time exact rebuild teljesítményvizsgálat — 2026-09-11

## Cél és döntött irány

A cél a teljes, pontos világ-újraszámítás gyorsítása 1 másodperc alá. A preview
nem az elsődleges megoldás: csak akkor marad tartalék út, ha az exact rebuild a
szükséges optimalizálások után is 1 másodpercnél lassabb.

## Élő Unity baseline

A legacy geometriahurok kihagyása utáni hét PerfLog alapján:

- teljes `Build()`: átlag 22 569,5 ms, tartomány 21 744,9–24 744,9 ms;
- `BuildStaticBaseLayer`: átlag 17 517,3 ms, tartomány
  16 844,5–19 325,1 ms, a teljes idő 77,6%-a;
- hydrology: átlag 4 190,0 ms, tartomány 4 019,3–4 539,2 ms, 18,6%;
- minden más együtt: átlag körülbelül 862 ms, 3,8%.

Ez megerősíti, hogy az első javítás valóban eltüntette a korábbi körülbelül
1,5 másodperces, eldobott geometriamunkát. A következő kör két domináns célja a
statikus alapréteg és a nagyfelbontású hidrológia.

## Külön Core-mérőpróba

Az aktuális scene-konfigurációt közelítő, JIT-bemelegített .NET 8 futás harmadik
mérésében a level-8 numerikus lánc 2 811,7 ms volt:

| Fázis | Idő |
|---|---:|
| elevation field | 1 124,0 ms |
| kráterek | 56,9 ms |
| erózió | 565,9 ms |
| referencia-tengerszint + ocean field | 30,7 ms |
| priority flood | 594,3 ms |
| flow accumulation | 282,0 ms |
| river selection | 19,5 ms |
| lake identification | 134,1 ms |

A próba nem Unity benchmark, ezért az abszolút ideje nem vihető át a viewerre.
Arra alkalmas, hogy igazolja: a render által már nem használt coarse
`FlowAccumulation` + `SelectRiverTiles` elhagyása valós, körülbelül 301,5 ms-os
hot .NET munka megszüntetését jelenti, és nem a tavakhoz szükséges floodot
távolítja el.

## Implementált exact optimalizálások

1. A level-8 coarse folyóhalmaz és parent-fa számítása kimarad. A megjelenített
   folyók a külön dendritikus hálózatból származnak, a panelek pedig saját
   level-5 referencia-hidrológiát számolnak, ezért ezek a mezők csak íródtak,
   de sehol nem olvasódtak.
2. A statikus alapréteg normálvektora újrahasználja a már kiszámolt sarokpontot.
   A korábbi út ugyanazzal a bemenettel még egyszer végigszámolta a teljes
   magasságláncot. Level 8-on ez nagyjából 396 ezer redundáns elevation-
   kiértékelést szüntet meg; az eredmény és a kiértékelési sorrend változatlan.
3. A PerfLog most külön méri a hidrológia field/flood/lake, illetve a statikus
   alapréteg enumeration/classification/corners/emit/mesh/borders/water/cache-
   eviction al-fázisait. A következő optimalizációt már ezek alapján lehet
   kiválasztani, találgatás nélkül.

## Ellenőrzés és nyitott kapu

- `dotnet build WorldGen.sln`: sikeres, 0 warning, 0 error.
- `dotnet test WorldGen.sln --no-build`: LOD 14/14, CLI 7/7 sikeres.
- külön Core-futás: 375/375 sikeres.
- Python KAT: ezen a gépen nincs telepített Python, ezért most nem futott; Core
  numerikus algoritmus vagy tesztvektor nem változott.
- A Unity által generált `Assembly-CSharp.csproj` fordítása a generált projekt
  warning-as-error beállítását felülírva sikeres: 0 error, 85 ismert Unity- és
  nullable warning. Élő Editor-fordítás és friss PerfLog ettől még szükséges,
  ezért az optimalizálás tényleges Unity-gyorsulása nincs késznek nyilvánítva.

## Következő lépés

Az új log alapján a legnagyobb maradék al-fázist kell folytatni. Valószínű
jelöltek: időfüggetlen terrain-bázis cache-elése, az elevation+erózió ismételt
plate/domain-warp munkájának összevonása, valamint a változatlan mesh-topológia
újrahasználata. Ezek közül csak a mérés után választunk, mert a <1s célhoz
strukturális, de továbbra is exact megoldás szükséges.

## ND-63 — időfüggetlen terrain-bázis cache, első fázis

A két élő deep-time változtatás az előző optimalizálások után 18 996,7 ms és
19 137,6 ms volt (átlag 19 067,2 ms). A részletes log szerint a statikus
sarokfázis önmagában átlag 7 511,4 ms, és mind a 396 294 sarok cache miss volt.

Az ND-63 első fázisa a statikus base-level sarkok középpontjához és a két
véges-differencia normálmintához eltárolja a deep-time-független
`TerrainPointBasis` adatot: warpolt koordináta, elsődleges zaj, MountainMask és
másodlagos zaj. A mozgó plate-hozzárendelés, uplift, erózió, kráter és
hőmérséklet továbbra is minden időpontban frissen számolódik. A külön
`AssignPlate` és `TwoBestDots` passz egyetlen, azonos sorrendű `TwoBestDots`
seed-szkenneléssé egyesült; annak `bestIndex` eredménye bitre azonos plate ID.

Level 8-on a három tömb nyers mérete körülbelül 54,4 MiB. Level 9-10 esetén a
cache szándékosan kikapcsol, mert a memória 4x/16x nőne; ott az általános exact
út marad.

Külön Release .NET 8 kontrollmérés, 396 294 sarok × 3 minta:

- egyszeri terrain-bázis felépítés: 3 489,0 ms;
- teljes base/uplift újraértékelés 400/500/600 Myr-nél: 54,2 / 47,5 / 47,5 ms.

Ez nem Unity-idő és nem tartalmazza a sarokszínt, Vector3-műveleteket vagy a
viewer cache-feltöltését, de igazolja, hogy a drága zajlánc helyett deep-time
váltáskor már csak a kis időfüggő maradék fut.

Ellenőrzés: 377 Core + 14 LOD + 7 CLI teszt sikeres; ebből 2 új teszt több
seed/pont/idő esetén bitmintára ellenőrzi a cache-elt és az eredeti
base/uplift komponensek azonosságát. Solution build 0 warning/0 error, Unity
C# build 0 error (85 meglévő warning). A tényleges Unity-gyorsuláshoz és a
vizuális azonossághoz új élő PerfLog szükséges.

## ND-63 élő Unity-eredmény és a következő exact cél

Az új `PerfLog_20260911_123713.txt` három teljes Buildet tartalmaz. Az első
feltölti a terrain-bázist (`terrainBasis=6718,8ms, reused=False`), a két
deep-time váltás már újrahasználja (`0,0ms, reused=True`):

- teljes rebuild: 12 158,5 / 11 976,2 ms, átlag 12 067,4 ms;
- statikus alaprész: 6 423,9 / 6 589,1 ms, átlag 6 506,5 ms;
- sarkok: 440,1 / 437,4 ms, átlag 438,8 ms;
- hidrológia: 5 031,0 / 4 793,4 ms, átlag 4 912,2 ms;
- klasszifikáció: 2 989,3 / 3 062,6 ms, átlag 3 026,0 ms;
- emisszió: 2 451,3 / 2 591,8 ms, átlag 2 521,6 ms.

Az előző kétméréses 19 067,2 ms átlaghoz képest ez 7 000 ms, azaz 36,7%
gyorsulás. A legacy-fix utáni 22 569,5 ms hétnaplós baseline-hoz képest a
teljes javulás 10 502,1 ms, azaz 46,5%. A sarokfázis önmagában 94,2%-kal
csökkent, ezért az ND-63 célzott hipotézise élőben igazolódott.

A következő exact lépés (ND-64) a hidrológia és klasszifikáció közös level-8
tile-középpont `TerrainPointBasis` tömbje, valamint a napi 24, tile-tól
független Nap-irány Buildenként egyszeri előállítása. Utóbbit a log különösen
erősen indokolja: az induló GPU dispatch 37,2 ms volt, de az eleváció-zaj
nélküli CPU hőmérséklet-loop 3 677,8 ms.

## ND-64 implementáció

- A level-8 tile-középpontokhoz külön, tömör `TerrainPointBasis` + `TileId`
  tömb épül. Deep-time váltáskor ugyanazt a tömböt olvassa a hidrológiai field
  és a base-level klasszifikáció.
- A hidrológiai elevation + kráter + erózió három külön nagy tömb-/Dictionary-
  passza egyetlen párhuzamos passz lett, az eredeti lebegőpontos műveleti
  sorrend megtartásával.
- A napi átlaghőmérséklet 24, tile-tól független Nap-iránya Buildenként egyszer
  készül el. Tile-onként csak az eredeti sorrendű 24 dot-product és a változatlan
  hőmérséklet-képlet fut.
- A PerfLog külön jelzi a hydrology `terrainBasis` és a statikus
  `tileCenterBasis` idejét/újrahasználatát.

Ellenőrzés: 379 Core + 14 LOD + 7 CLI teszt sikeres. Az új tesztek a teljes
`base+uplift → kráter → eróziós korrekció` láncot és az előállított Nap-mintás
hőmérsékletet `double` bitmintára vetik össze a korábbi úttal. Solution build:
0 warning/0 error; Unity C# build: 0 error, 85 meglévő warning. Python továbbra
sem érhető el ezen a gépen. Az ND-64 tényleges gyorsulása új élő Unity PerfLogra
vár.

## ND-64 élő Unity-eredmény és ND-65

Az új `PerfLog_20260911_125604.txt` két deep-time váltása 5 608,7 ms és
5 953,7 ms volt, átlag 5 781,2 ms. Mind a hydrology `terrainBasis`, mind a
statikus `tileCenterBasis` mindkét rebuildben `reused=True`:

- hydrology field: 75,5 / 237,1 ms, átlag 156,3 ms;
- teljes hydrology: 1 498,2 / 1 555,0 ms, átlag 1 526,6 ms;
- klasszifikáció: 462,4 / 507,2 ms, átlag 484,8 ms;
- sarkok: 372,1 / 337,0 ms, átlag 354,6 ms;
- mesh-emisszió: 2 430,5 / 2 497,8 ms, átlag 2 464,2 ms;
- priority-flood: 1 302,4 / 1 195,1 ms, átlag 1 248,8 ms.

Az ND-63 utáni 12 067,4 ms átlaghoz képest ez újabb 52,1% gyorsulás. A
22 569,5 ms legacy-fix utáni baseline-hoz képest az összesített javulás 74,4%.
A hydrology field 95,6%-kal, a klasszifikáció 84,0%-kal rövidült. A következő
domináns célok már egyértelműen az emisszió (42,6%) és a flood (21,6%).

Az ND-65 első exact lépései:

- `showBorders=0` mellett az emit-loop nem építi fel a korábban felhasználás
  nélkül eldobott 1 572 864 border-vertexet és 3 145 728 vonalindexet;
- a priority-flood `SortedSet + counterToTile Dictionary` párosa közvetlen
  TileId-t tároló determinisztikus bináris minimum-heap lett.

A régi rendezett-halmaz referencia és az új heap teljes `Filled`, `Parent`,
`FloodOrder` eredménye egyezik. Ellenőrzés: 380 Core + 14 LOD + 7 CLI teszt,
összesen 401/401 sikeres; solution build 0 warning/0 error; Unity C# build
0 error, 85 meglévő warning; `git diff --check` tiszta. Élő ND-65 PerfLog kell
a tényleges nyereség megállapításához.

## ND-65 élő Unity-eredmény és ND-66

Az új `PerfLog_20260911_130513.txt` két deep-time váltása 5 181,1 ms és
5 372,0 ms volt, átlag 5 276,6 ms. Az ND-64-es 5 781,2 ms átlaghoz képest ez
504,7 ms, azaz további 8,7% gyorsulás. A 22 569,5 ms legacy-fix utáni
baseline-hoz képest a teljes javulás 76,6%.

- priority-flood: 924,1 / 783,0 ms, átlag 853,6 ms; az előző 1 248,8 ms-hoz
  képest 31,7% javulás;
- statikus emisszió: 2 386,1 / 2 335,9 ms, átlag 2 361,0 ms; 4,2% javulás;
- teljes statikus base-layer: 3 471,5 / 3 462,9 ms, átlag 3 467,2 ms;
- klasszifikáció: átlag 478,7 ms; sarkok: átlag 397,8 ms;
- teljes hydrology: átlag 1 185,1 ms; ebből field 68,7 ms, flood 853,6 ms,
  tavak 262,8 ms (a második futásban 409,3 ms-os szórással).

Az emisszió így a teljes rebuild 44,7%-a, ezért az ND-66 ezt célozza. A teljes
base-grid szabályos, sűrű topológiáját közvetlen tömbindexre képezi:
`face * stride + u * side + v`. A klasszifikáció és a 396 294 face-local sarok
pozíció/normál/szín eredménye ugyanazokkal a függvényekkel, ugyanazon a
face/u/v sorrenden készül, de a statikus emit nem tölt és nem érint
Dictionary-/LinkedList-LRU cache-eket. A tömbök Build után megmaradnak, hogy a
dinamikus LOD base-szintű szülő-sarok és oceanic-ős lekérdezéseit is egzaktul
kiszolgálják. Level 8 fölött az általános út marad.

Ellenőrzés: 380 Core + 14 LOD + 7 CLI teszt, összesen 401/401 sikeres; solution
build 0 warning/0 error; Unity C# build 0 error, 85 meglévő warning;
`git diff --check` tiszta. A PerfLog új `denseStatic=True` mezője igazolja majd,
hogy az élő mérés az ND-66 útvonalon futott. Vizuális és teljesítmény-
elfogadáshoz új élő Unity-futtatás szükséges.

## ND-66 élő Unity-eredmény és ND-67/68

Az új `PerfLog_20260911_131720.txt` négy meleg deep-time váltása 3 339,3 ms,
3 506,9 ms, 3 729,4 ms és 3 681,6 ms volt, átlag 3 564,3 ms. Az ND-65-ös
5 276,6 ms átlaghoz képest ez további 32,5% gyorsulás. A 22 569,5 ms
legacy-fix utáni baseline-hoz képest a teljes javulás 84,2%, azaz 6,3-szoros
gyorsulás. Mind a négy mérés `denseStatic=True` útvonalon futott.

- teljes statikus base-layer: átlag 1 842,2 ms;
- klasszifikáció: átlag 465,9 ms;
- sarkok: átlag 99,1 ms;
- statikus emisszió: átlag 944,7 ms;
- terrain mesh: átlag 175,2 ms; víz: átlag 151,2 ms;
- teljes hydrology: átlag 1 003,5 ms; ebből field 48,4 ms,
  priority-flood 748,0 ms és tavak 207,2 ms.

Az ND-67 a még domináns priority-floodot sűrű, face/u/v szerint indexelt
tömbökre vitte. A világtól független cubed-sphere szomszédtopológia egyszer
épül fel, majd a következő deep-time váltások újrahasználják; a heap tie-break
és a right/left/up/down sorrend változatlan. Az új teszt minden tile `Filled`,
`Parent`, `FloodOrder` és szomszéd eredményét összeveti a Dictionary-alapú
referenciaúttal, a lebegőpontos értékeket bitmintára.

Az ND-68 a statikus klasszifikációból előre megszámolja a terrain- és
water-bucketek quadjait. A listák pontos kapacitással jönnek létre, az emit
pedig közvetlen tömbreferenciát használ; a dinamikus LOD-út változatlan.
A PerfLog külön jelzi a sűrű flood használatát, a topológia építési/
újrahasználati állapotát, a flood-kernel és Dictionary-konverzió idejét,
valamint a bucket-előkészítést.

Ellenőrzés: 381 Core + 14 LOD + 7 CLI teszt, összesen 402/402 sikeres; solution
build 0 warning/0 error; Unity C# build 0 error, 85 meglévő warning. Az ND-67/68
tényleges gyorsulása és a változatlan látvány új élő Unity-futtatásra vár.

## ND-67/68 élő eredmény és ideiglenes lezárás

A `PerfLog_20260911_133442.txt` egy hideg és öt meleg teljes Buildet tartalmaz.
Az öt meleg deep-time rebuild 2 656,3 / 2 790,2 / 2 606,5 / 2 935,2 /
3 185,4 ms, átlag 2 834,7 ms (minimum 2 606,5, maximum 3 185,4 ms). Ez az
ND-66 utáni 3 564,3 ms átlaghoz képest további 20,5% gyorsulás; a 22 569,5 ms
legacy-fix utáni baseline-hoz képest 87,4%, azaz közel 8,0-szoros javulás.

Az ND-67 célzott hipotézise igazolódott: minden meleg futás
`denseFlood=True`, `topology reused=True` állapotú. A priority-flood átlaga
257,5 ms (235,6–284,8 ms), ebből a dense kernel 247,9 ms, a szükséges ideiglenes
Dictionary-konverzió 9,6 ms. Az ND-66-os 748,0 ms flood átlaghoz képest ez
65,6% javulás. A teljes hydrology átlaga 550,1 ms-ra csökkent.

Az ND-68 után a statikus base-layer átlaga 1 602,4 ms, az emit 664,4 ms. Az
ND-66-os 1 842,2 / 944,7 ms értékekhez képest ez rendre 13,0% és 29,7%
gyorsulás; a `bucketPrepare` költséggel együtt az előméretezett emit út átlaga
774,4 ms. A fennmaradó statikus rész átlagos bontása: klasszifikáció 482,6 ms,
bucket-előkészítés 110,0 ms, terrain mesh 145,4 ms és vízfeltöltés 106,4 ms.

A szórás nem elhanyagolható: bucket-előkészítés 13,4–243,2 ms, terrain mesh
97,0–309,2 ms, víz 25,9–283,7 ms és klasszifikáció 418,7–669,3 ms. Ezért a
következő körben előbb Unity Profiler/GC-allokációs mérés indokolt. További
backlogtételek: dense lake pipeline, a bizonyítottan deep-time-invariáns
csapadékmező cache-e, determinisztikus direct-array/párhuzamos mesh-emisszió,
valamint a hideg terrain-bázis előállításának kezelése.

A hideg első Build 13 029,1 ms maradt; ebből a statikus sarok terrain-bázis
6 866,6 ms, a tile-középpont bázis 2 189,2 ms. Ez külön hidegindítási feladat,
nem a meleg deep-time mérés része.

A felhasználó a 2,835 s-os jelenlegi meleg eredményt 2026-09-11-én
ideiglenesen elfogadta. A `<1 s` cél nyitva marad, de a munka most másik
feladatra vált; új optimalizálás ebben a lezáró körben nem készült.
