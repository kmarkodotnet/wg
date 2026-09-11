# ND-76 — Nézetkövető finomítás az ND-75 élő log után

2026-09-11. Állapot: implementált, élő Unity-visszaellenőrzésre átadva.
Nem tekinthető vizuálisan elfogadottnak.

## Bizonyíték és a változás határa

Az `unity/WorldGenViewer/Logs/PerfLog_20260911_195929.txt` alapján:

- 20:13:10-kor a kamera középponttávolsága 105,456727, a kirajzolt cut
  kamerája még 127,027420. Középen 112,96 × 142,48 px-es tile,
  180,40 px átmérő; a találatos minták mediánja 130,68 px.
- A nehéz kérés 10,60 s korú eredményt alkalmazott. Sarokfázis 4,90 s,
  emit 2,94 s, classification 1,52 s, cut 1,09 s. Nem csak upload-lassulás.
- 20:13:52–54: álló kamera, nulla alkalmazott kameraeltérés, nincs függő
  kérés; mégis 105,51 px-es statikus terep a mintákban, középen 7,32 px.
- A diagnosztika főszálas költségének mediánja 9,96 ms, maximuma 138,56 ms;
  háttérmérés mediánja 210,56 ms. Ez sem volt ingyenes.

A log eleji közel 13 perces időrés néhány frame közé esik, ezért nem
számítási benchmark; a működő zoomszakasz fázisidőit használtuk.
Az ND-75 mintázott feltöltött geometriát mér, nem teljes GPU-képet.

## Implementáció

1. **Téglalap alakú kamerafrustum + terepkiterjedés.** A kész radiális proxy
   a négy sarok közti magasságváltozást is figyelembe veszi. Az aktív CPU/
   perspektivikus/proxy út nem használja előtte az alapgömb régi horizont-
   és backface-kapuját. Osztáshoz a négy proxy-sarok vetített átmérője kell;
   a gömbbounds kizárólag a láthatósági előszűrésé. A morph azonos metrikát kap.
2. **1024 új osztás/kérés.** A már meglévő felosztások nem fogyasztják ezt a
   keretet; a halasztott finomítás álló kameránál is folytatódik. A prioritás
   továbbra is a nagy képernyőhibáé. Minden hullám teljes base-fedést és
   közösél-feloldást kap. A balance az új úton a 200 000-es cut-budgetet sem
   lépheti át; a budgetnél megmaradó szintkülönbséget a resolver illeszti.
3. **Elavult kérés megszakítható.** 120 ms után, a kért helyi felszínmagasság
   20%-ánál nagyobb kameramozgás, érdemi irány- vagy vetítésváltozás esetén.
   Legfeljebb egy megszakítás két sikeres alkalmazás között: folyamatos
   mozgás nem tarthatja örökké a régi képet. A worker kooperatívan áll le,
   nem fut vele párhuzamos másik LOD-worker, félkész mesh nem publikálódik.
4. **Valódi emit-cache.** Azonos levélhalmaz ÉS egzaktul azonos, közöséllel
   feloldott csúcsok esetén a chunk terrain/víz/border emissziója kimarad.
   Csak topológiai egyezés nem elég: szomszédváltozás és morph is módosíthatja
   a geometriát. A vízszín a terrain-pozíciótól is függhet, ezért együtt
   cache-elt. Build, konfiguráció- és színmódváltás invalidál; cache-generáció
   csak sikeres alkalmazáskor vált. Hiányzó cache teljes feltöltést kér,
   így azonos pozíció mellett változó szín/anyag sem ragad be.
5. **Diagnosztika megmarad.** Az ND-75 snapshot most 2 másodpercenként indul.
   Az új `[async apply ND-76]` sor `emittedLeaves`, `reusedLeaves`,
   `newSplits`, `deferredSplits`, `projectedTerrain` mezőket is tartalmaz;
   `[ND-76 request]` jelzi a megszakítást/eldobást.

Az új proxy- és kameraosztály engine-független. Core, seed, világmodell,
tile-layout, maximális LOD, pixelcél és scene-beállítás ezen a lépésen belül
nem változott. A működés a kódban aktív, nincs új Inspector-bekötés.

## Offline ellenőrzés és elvetett változatok

`dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj -c Release
--no-restore -p:_EnableDefaultWindowsPlatform=false -- --progressive`

A két irány az élő logból származik. A proxy a próba meglévő Core-alapú
terrain-orákulumából kapja a base-sugarakat; a mintavétel külön előkészítés,
nem része a kiválasztásnak. Az egyes régi/új összevetések ugyanazt az előző
cutot kapják; a haladó pálya az új, beállt eredményt viszi tovább. Ez nem
frame-pontos Unity-replay, nincs benne teljes render, vízszűrés vagy upload.

| Irány / középponttávolság | Régi cut | Új beállt cut | Régi → új középső LOD | Új hullámok |
|---|---:|---:|---:|---:|
| 1 / 139,777 | 42 732 | 61 693 | 9 → 9 | 14 |
| 1 / 126,663 | 79 494 | 36 502 | 10 → 10 | 3 |
| 1 / 114,833 | 132 001 | 41 383 | 11 → 11 | 11 |
| 1 / 105,457 | 199 998 | 48 156 | 14 → 14 | 15 |
| 2 / 139,777 | 38 540 | 56 571 | 9 → 9 | 14 |
| 2 / 105,457 | 170 915 | 43 117 | 13 → 13 | 14 |

Mélyen ~75–76%-kal kevesebb levél marad azonos nadír-LOD mellett. **Közepes
zoomnál viszont 44–47%-kal több is lehet**: a korábban alábecsült meredek/
perifériás tile-ok helyes finomítása többletmunka. Nem állítunk mindenütt
gyorsulást. A végső 300-as távolságnál mindkét irány ismét üres dinamikus cut.

Az 1. mély irány első hulláma 5087 levéllel már L13, a végső L14-hez 15
hullám kell. A tiszta cut ideje első hullámnál 44,8 ms, a leglassabb itt
159,6 ms; az összes hullám cutja együtt 1454,5 ms. **Nem teljes mesh-idők**,
és nem állítjuk, hogy 44,8 ms után Unityben már megjelenik a kép.

A gömb teljes vetített méretét használó első változatot elvetettük:
159,34-nél 109 319 levélhez vezetett a quad-metrika 7045 levele helyett.
A 256-os adagolást is elvetettük: túl sok hullám és ismételt cut-munka.

## Nyitott határok és élő elfogadás

Ellenőrzés: solution build 0 hiba/0 figyelmeztetés; Core 381/381, CLI 7/7,
LOD Debug és Release 144/144 (532 külön teszteset, 19 új). Unity assemblyk
fordítása 0 hiba/83 meglévő figyelmeztetés. `git diff --check` tiszta.
Python KAT/vektorregenerálás nem futott (nincs Python); a Core és az
orákulum nem változott. Élő Unity-vizuális ellenőrzés továbbra is hiányzik.

- A proxy a base-minták közti interpoláció: a valódi mély terep eltérésére
  nem bizonyított felső korlát. Az eredeti 105,51 px-es konkrét tile javulását
  az új ND-75 mérésnek kell igazolnia, nem a szintetikus tesztnek.
- A 1024-es keret nem főszálas milliszekundum-budget. Balance, cache-hiány,
  régi chunk morphja és szomszédváltozása további munkát okozhat. A teljes
  cut és a feloldott pozíciók összevetése még hullámonként megmarad.
- Egy hullám feltöltése atomikus. A víz/border globális konkatenálása és
  feltöltése megmarad; több frame-re osztott upload nincs ebben a csomagban.
- A cache többletmemóriát köt le a jelenlegi renderelt chunkokhoz; nem őrzi
  korlátlanul az elhagyott nézetek mesh-generációit. Kamera felszínkorlátja,
  szigorú óceáni belsőmintás láthatóság, precíz km-lépték külön nyitott.

**Próba:** állítsd le a Playt, várd meg az újrafordulást, indíts új Playt.
Ugyanaz a távoli → közepes → mély → vissza út, közepes/mély nézetnél
10–15 s megállással. A régi logban `terrainProxy=True`, `morphRange=0.360`
volt; most ne hangolj párhuzamosan Inspector-értékeket. Az új kickoffban
`projectedTerrain=True newSplitLimit=1024` legyen. Figyeld a gyors első
finomodást, az állva tovább élesedést, a durva foltokat és a varratokat.
Ezután az új PerfLogból összehasonlítható a tényleges pixelméret és késés.

M9 tartalmi becslés élő elfogadásig továbbra is 60–70%. E csomag durva
fejlesztői ráfordítás-egyenértéke 6–10 óra; további zoom/validáció 8–20 óra,
új km-UI és Core-részletmodell nélkül. Ezek nem mért munkaidők.
