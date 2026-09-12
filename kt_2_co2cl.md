# Tudásátadás 2 — Codex → Claude (Sonnet / Opus)

Átadási pillanatkép: **2026-09-12**, `F:\Claude\wg`.
Előzmény: [kt_1_cl2co.md](kt_1_cl2co.md), az eredeti
`docs/07-handover-2026-09-10.md` változatlan szöveggel átnevezve.
Ez a dokumentum az átvétel óta végzett munkát, a jelenlegi kód működését,
a bizonyítékokat és a folytatási tervet adja át. Nem új implementációs döntés.

## 1. Vezetői összefoglaló — ezzel kezdd

A Codex a `0d993f4` állapotból vette át a fejlesztést. A legfontosabb eredmények:

1. A teljes, egzakt deep-time rebuild meleg idejét az élő mérésekben
   kb. **22,57 s-ról 2,83 s-ra** csökkentettük, a szimuláció numerikus
   eredményét megtartó cache-/adatszerkezeti optimalizálással (ND-63–68).
   A felhasználó ezt ideiglenesen elfogadta; az eredeti **<1 s cél nincs kész**.
2. A zoomhibáról részletes diagnózis készült, majd a terep kiválasztását,
   teljes fedéscseréjét, közös éleit, geomorph-frissítését, inkrementális
   emisszióját és mérését átalakítottuk (ND-69–81). Egy drága kísérletet
   visszavontunk; ezt nem szabad kész javításként visszahozni.
3. Önálló, korlátos víz-LOD, több frame-es terep/víz/border-feltöltés,
   előkészített publikáció, revízióvédett maszk, hibafallback és inaktív
   renderobjektumok takarítása készült (ND-82/83/85–87/89/92–94).
4. Precíz értelmű fizikai km-lépték, helyi felszínkövető kamerakorlát,
   FlyTo-korrekció és hitelesebb kérésidőmérés került be (ND-84/91/95).
5. A legújabb munkafabeli csomag: kameraváltást túlélő geometria-cache,
   kész meshből visszacsatolt LOD-hiba, egyszeres balance-passz,
   8/7 px minőségi cél, metrikatároló-reuse/helyi érvénytelenítés,
   stabil víz-cut reuse (ND-96–98).

**A felhasználó fő célja még nincs lezárva:** már az első/közepes zoomoknál
egyenletesen részletes, gyorsan felzárkózó, sima kép. A kisebb uploadcsúcs
vagy gyorsabb részfolyamat nem bizonyítja ezt. A legújabb élő naplóban a
háttérmunka még domináns, a napló vége még finomítás közbeni állapot.

Utolsó teljes .NET-ellenőrzés az ND-98 végén: **765/765 PASS**
(384 Core + 373 viewer-LOD + 8 CLI); viewer Release 373/373.
Unity runtime forrásfordítás 0 hiba / 83 meglévő warning, Editor-tesztprojekt
0 hiba / 4 warning. **Ez nem natív Editor-tesztfuttatás vagy vizuális elfogadás.**

Az átadás készítésekor találtam egy új `PerfLog_20260912_202309.txt` fájlt:
ND-96/97/98 már fut benne. Ennek friss értékelése a 8. fejezetben felülírja
a korábbi doksik „nincs új ND-96 log” megjegyzését. Felhasználói képi
értékelés ettől még nem érkezett ebben a beszélgetésben.

## 2. A felhasználóval való együttműködés

- Magyarul kommunikálj; kód/azonosító/commit angol, dokumentáció/komment magyar.
- A felhasználó műszakilag tájékozott, képet, logot és Frame Debuggert is
  használ. A megfigyelése elsődleges bizonyíték, nem félreértésből eredő panasz.
- Többször elégedetlen volt: a magabiztos magyarázat után a kép nem javult,
  néha jelentősen lassult. **Ne jelentsd „megoldva”-nak a zoomot offline tesztből.**
- Korábban egyesével, majd 2–3 feladatonként ellenőrzött. Legutóbb a teljes
  implementációs csomag végén, **egyben** szeretne ellenőrizni. Ne kérj új
  kézi tesztet minden apró optimalizálás után. A közös lista már létezik.
- Az első zoomok akadását átmenetileg backlogra tette, később az összes
  fennmaradó tile/zoom feladat folytatásával ezt a halasztást feloldotta.
  A backlog lentebbi „halasztva, csak a víz a következő” sorai történetiek.
- Nem kért új, dekoratív zajt: a sűrűbb mesh a modell meglévő részletét tegye
  láthatóvá. Új mikroterep külön Core-/verziózási döntés lenne.
- Hegységeket szeretne látni természetes partokkal. Egy globális magassági
  szorzó nem oldja meg mindkettőt; ne lapítsd le újra csendben a bolygót.
- A sokszor ismételt „60–70%, 8–20 óra” jogos kritika volt. Tételes audit
  készült; ne számold a készültséget ND-k vagy promptok darabszámából.
- Commit/push csak kérésre. A kért checkpoint elkészült, a későbbi munkát
  nem commitoltuk újra. A távoli repo publikus.

## 3. Repo, branchek és a munkafa biztonsága

### 3.1 Ellenőrzött commitállapot

| Hivatkozás | Átadáskori érték / jelentés |
|---|---|
| Aktív branch | `codex-handoff` |
| HEAD | `99b3ac4895e415f210551770d81e790a36aea31c` |
| Lokális remote-tracking ref | `origin/codex-handoff = 0f9cd4b`; a branch 1 committal előtte |
| Claude-átvétel alapja | `0d993f4`, a `feature/camera-view-modes` jelenlegi vége |
| `195875e` | Exact deep-time optimalizálás, review/solution-integráció és diagnózis |
| `0f9cd4b` (`tile division`) | A korai tile/zoom csomag, nagyjából ND-69–82 és kapcsolódó dokumentáció |
| `99b3ac4` | Víz renderbekötés, upload/navigáció/erőforrás checkpoint, ND-95-ig tartó csomag |
| ND-96–98 | **Munkafában, nincs benne a HEAD-ben** |

Távoli hálózati fetch nem történt a tudásátadáskor: az origin adata a lokális
tracking ref, nem frissen lekért távoli bizonyíték. A korábbi átadás azon
mondata, hogy a kameraág és a handoff ág azonos, már nem igaz.

`experiment/full-temperature-model = e93b909` továbbra is külön testvérág.
Nem merge-eltük a jelenlegi ágba. Az ottani tengeri-jég/hőmodell ND-62
azonosító ütközik a kameraág ND-62-jével. Merge előtt oldd fel; ne hozz át
válogatás nélkül egy régi `PlanetGridMesh.cs`-t. `core-deferred-features`
`4b3ac02`-n áll, a lokális tracking szerint 2 committal előzi az originjét.
A többi kísérleti branch nem a folytatás kiindulópontja.

### 3.2 Két külön változáscsoport van ugyanabban a munkafában

**A tile/zoom munkaszál ND-96–98 fájljai:**

- `unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/`: `AdaptiveQuadTree.cs`,
  `AdaptiveViewState.cs`, `ProjectedLodView.cs`, `SurfaceLodBounds.cs`,
  `WaterLodSource.cs`, új `LodGeometryCache.cs` és `.meta`.
- `PlanetGridMesh.cs`, `.Refinement.cs`, `.WaterRefinement.cs`, `PlanetView.unity`.
- Viewer-tesztek/projekt: `RenderedTileDiagnosticsTests`,
  `TerrainEvaluationCacheTests`, `WaterLodSourceTests`, új
  `BalanceEquivalenceTests`, `LodGeometryCacheTests`.
- Új Unity Editor `GeometryFeedbackTests.cs` és `.meta`.
- Architektúra/döntések/milestone/backlog, diagnosztikai probe/projekt,
  M9-audit, ND-96/97/98 review és history dokumentumok.

**Másik munkaszálból származó, megőrzendő módosítások:**

- Core `CrustElevation.cs`, `DeepTimeErosionGlaciation.cs`,
  `PlateBoundaryEffect.cs`; hozzájuk tartozó Features/Tectonics tesztek.
- Python `crust_elevation_ref.py`, `erosion_glaciation_deep_time_ref.py`,
  `plate_boundary_ref.py` és downstream referencia-/tesztvektorok:
  erosion, features, hydrology, lakes/ice, moisture, plate boundary,
  river path, state hash.
- `tools/WorldGen.Cli/WorldPackage.cs` és CLI-tesztje.
- `history/2026-09-12-tectonic-boundary-transition-nd90.md`;
  `history/2026-09-11-deep-time-step-buttons.md` utólagos bővítése.
- Új `docs/08-development-idea-2-non-earthlike-planets.md`, `docs/base_models/`.

A külön munkaszál ND-90 numerikus változását nem tulajdonítom a tile-
javításnak. A közös munkafán futó zöld Core-tesztek annak aktuális
állapotát is ellenőrizték, de a Python-újragenerálás történeti bizonyítéka
a másik szál naplójában van. Tilos egyszerűen `git add .`-tal egyetlen
„zoomfix” commitba összemosni mindent, resetelni vagy eldobni az eltéréseket.

Az átadás maga további dokumentációs diff: `kt_2_co2cl.md`, a régi átadás
átnevezése, startup-hivatkozások és history/státuszfrissítés. Nem alkalmazáskód.

## 4. Projektarchitektúra és invariánsok

Olvasási sorrend: `CLAUDE.md`, `AGENTS.md`, ez a fájl, a régi átadás személyes
üzenete, majd az aktív backlog/M9 és a feladathoz tartozó ND/architektúra.

| Terület | Szerződés |
|---|---|
| `src/WorldGen.Core` | `netstandard2.1`, C# 9, motorfüggetlen; fizikai értékek double |
| `tools/reference` | Python-orákulum és determinisztikus tesztvektorok forrása |
| `tests/WorldGen.Core.Tests` | Core xUnit- és vektortesztek |
| `unity/WorldGenViewer` | Unity 6 / HDRP; Core helyi package-ként, forrásból, nem másolatból |
| `Assets/Scripts/Viewer/Lod` | UnityEngine-független viewer-algoritmusok, nem Core-szimuláció |
| `tests/WorldGen.Viewer.LodChunking.Tests` | Ugyanezek a források linkelve, .NET 8 xUnit; nem második implementáció |
| `tools/WorldGen.Cli` + tesztje | .NET 8, perzisztencia/CLI |

Azonos seed + verzió minden platformon/szálszámon/sorrendben bitazonos világ.
Állapotmentes random; nincs rejtett seed/időfüggés a Core-ban. Minden pixel
és paneladat modellforrású. Core-ban nincs `System.Text.Json`, file-scoped
namespace, `record`, `required` vagy primary constructor. Kritikus numerikus
úton a transzcendenseket az ND-23b és `DeterministicMath` szerint kezeld.
A viewer időmérője/naplójának órája nem kerül a szimulációba.

Új numerikus viselkedés: előbb döntés, Python/KAT, generált vektor, C# port,
teszt; seed-töréskor kompatibilitási verzió és explicit betöltési hiba.
Az ND-63–68 exact cache-ek nem seed-törők. Az ND-90 **igen**, lásd 10. fejezet.

A legmagasabb jelenlegi főági ND **98**. A következő új számot friss keresés
után foglald; ND-88/89 és ND-90/91 nem numerikus sorrendben szerepelnek a
doksiban. A külön ág ND-62 ütközése ettől még nincs rendezve.

## 5. Mit végeztem el az átvétel óta?

### 5.1 Induló audit, infrastruktúra és kis korrekciók

Forrás: [átvételi review](docs/reviews/codex-handoff-2026-09-10.md).

- A nem implementált „Pálya mentén” kameramód már nem választható no-op:
  letiltott, „hamarosan” jelölésű vezérlő. A teljes orbit-follow nincs kész.
- A kamera/deep-time IMGUI panel háttere követi a sorok számát.
- A PerfLog adaptív ágban `biome-only adaptive preparation` nevet használ,
  nem félrevezető „eldobott legacy geometry” címkét.
- AxialRotation módban a Planet gyermekeként élő csillagmező valóban
  világtérhez rögzül: `StarField.KeepFixedInWorldSpace()` és SunController-
  bekötés; puszta `localRotation=identity` örökölte volna a bolygó forgását.
- A PlanetView `SunController.planetGridMesh` referenciája ténylegesen be
  van kötve (`fileID: 283293246`); a régi általános handover-figyelmeztetés
  ezen a scene-en már nem aktuális. Más scene/prefab külön ellenőrizendő.
- A gyökér solutionbe bekerült a CLI és a Unity-független viewer-LOD
  tesztprojektje is. A meglévő solution-szintű CI így mindhárom assemblyt
  futtatja. Nem állítunk ehhez friss, lefutott távoli CI-t.
- `AGENTS.md` tartós projektutasítás és bizonyítékalapú átadási/review doksik.

A `PlanetGridMesh` nagy orchestration-fájl maradt. A későbbi partialok
navigálhatóbbá tették, de ez nem teljes rétegépítő-/dependency-graph refaktor.
Az eredeti review által javasolt általános `WorldBuildContext`, egységes
`MeshBuffers`, rétegenkénti invalidálás nem jelenthető késznek.

### 5.2 Exact deep-time gyorsítás (ND-63–68)

Részletek és élő mérési sor:
[history](history/2026-09-11-deep-time-exact-performance.md),
[döntések](docs/04-decisions.md).

- Elhagytuk a nem használt coarse folyófelhalmozás/-kiválasztás munkáját;
  a megjelenített dendritikus folyók és a panel saját hidrológiája maradt.
- A véges differenciás normál újrahasználja a már számolt középpontot.
- `TerrainPointBasis`: a seedfüggő, deep-time-független warp/zaj/mountain
  részeredmények egyszer készülnek a statikus sarkokra és két normálmintára.
  L8-on kb. 54,4 MiB nyers adat, L8 felett nem négyszerezzük tovább.
- Külön középpont-bázis (~18 MiB L8-on) közösen szolgálja a hidrológiai
  fieldet és klasszifikációt. A mozgó lemez-hozzárendelés, uplift, erózió,
  kráter, klíma, színek továbbra is az aktuális világidőből készülnek.
- Napi hőmérséklet 24 Nap-iránya Buildenként egyszer; tile-onként az eredeti
  dot-product/összegzési sorrend marad. Nem kisebb mintaszámú közelítés.
- Priority-flood: determinisztikus `(elevation,counter,tile)` min-heap;
  majd `DenseGridTopology` és `PriorityFloodDense`, tömbös állapot és
  újrahasznált right/left/up/down szomszédindexek.
- Az eredeti dictionary-út kontrollként megmarad; `Filled`, `Parent`,
  `FloodOrder` és double-bitminták egyezését teszteljük. A tie-break és a
  bejárási/lebegőpontos sorrend nem optimalizálható át szabadon.
- Teljes statikus grid: pozíció/normál/szín/besorolás sűrű tömbben, direkt
  `face/u/v` index. Nincs többmilliós dictionary/LRU-mozgatás az emitben.
- Pontosan előméretezett terrain/water bucketek, közvetlen listareferenciák;
  kikapcsolt border mellett annak geometriája egyáltalán nem épül.

Meleg élő Build-átlagok a javítási láncban: **22,57 → 19,07 → 12,07 →
5,78 → 5,28 → 3,56 → 2,83 s**. Nem kontrollált laborbenchmark, hanem
egymást követő PerfLog-próbák. Utolsó ND-68 sor: flood ~257,5 ms,
statikus base ~1602,4 ms, emit ~664,4 ms. A későbbi 14:02-es logban
10 deep-time Build 2,757–3,043 s. A hideg cache-építés ennél drágább.

### 5.3 A zoomhiba diagnózisa, fedés és varratok (ND-69–74)

[Eredeti diagnózis](docs/reviews/lod-zoom-diagnosis-2026-09-11.md),
[1. csomag](docs/reviews/lod-zoom-fixes-2026-09-11.md),
[2. csomag](docs/reviews/lod-zoom-fixes-phase2-2026-09-11.md).

- CPU-geometria mellett a klasszifikáció is a teljes CPU-modellt használja.
  A t=0 GPU-besorolásból hiányzó secondary detail korábban rossz óceáni
  tiltással elrejthette a valódi CPU-terep finomítását.
- Nem csak az óceáni base-középpont számít: a négy sarok szárazföldje is
  megakadályozza a teljes tile finomításának letiltását. Ez mintavételes
  védelem, nem minden apró belső szigetet bizonyító felső korlát.
- Nézetkulcs: pozíció/irány/FOV/aspect/pixelméret, kis mozgásra is frissítés.
  A pixelküszöb perspektivikus fókusztávolságból jön, nem puszta távolság.
- Budgetnél megmarad a már létrehozott frontier: nincs véletlenszerűen
  eldobott dinamikus levél. A determinisztikus prioritás és a teljes fedés fontos.
- `LodCoverage`: a kiváltott base-tile alatt teljes renderpartíció. Csak
  sikeres finom mesh-publikáció után rejthető el a durva statikus terrain.
- `TerrainIndexMask`: eredeti indexek megőrzése, érintett quadok degenerált
  háromszögekre cserélése, visszazoomnál helyreállítás. Nem shaderes mélységbias.
- `LodCornerResolver`: legdurvább érintkező levél a sarokgazda, tie-break
  legkisebb TileId; illesztés a valódi durva élhez, kockalap-éleken is.
- Morph a tényleges `AddQuad` **00–11 átlójú két háromszögére**, nem bilineáris
  nyeregre történik. A topológiaegyezés nem jelenti a geometria egyezését:
  a kamera/coverage/morph által változó pozíciókat is ellenőrizzük.

A felhasználó az ND-70 után a zoom/visszazoom működését pozitívan jelezte,
de kb. 13 görgetés után nem érzett további élesedést. Ez nem igazolt hard
kamerastop volt; az alábbi metrika- és költségproblémák külön maradtak.

**ND-71 rossz tradeoff, ND-72-ben visszavonva:** a kiválasztás minden
vizsgált csomópontjára 4 sarok + középpont valós elevációt számoltunk.
~192–241 ezer bounds/kérés, 6,7–9,6 s cut, 8–11,8 s request, érdemi
képi javulás nélkül. Ne aktiváld újra a `surfaceBounds` callbacket a
runtime-ban. A tiszta API/teszt történeti kísérleti eszközként megmaradt.

ND-72 olcsó nadírdiagnosztika, ND-73 korábbi első split/rövidebb morph,
ND-74 Build-kori `TerrainLodProxy` készült. Utóbbi a már kiszámolt statikus
saroksugarakból épül, L8-on kb. 7 MiB; zoomkor nincs új Core-hívás a
metrikához. Tenger alatti proxy a vízsugárra korlátozott. A fine sugár
bilineáris becslés, **nem szigorú képernyőhiba-garancia**.

### 5.4 Valóban kirajzolt tile-ok, progresszív munka (ND-75–81)

- ND-75 a sikeresen feltöltött vertex/index adatot, aktív renderereket és
  alkalmazott maszkokat méri. 17×9 képernyőminta, clipping és mintánként
  mesh-mélységteszt. Külön worker, legfeljebb 1 Hz; nem Core-mintavétel.
- Ez a tényleges mesh megfigyelése, **nem az elvárt tile-méret**. Nem GPU
  readback: shader, átlátszóság, teljes képernyőmaximum nem garantált.
- ND-76 `ProjectedLodView` és progresszív kiválasztás: 1024 új terepsplit/
  request, további hullám álló kameránál is; frustum/proxy-quad vetület.
  Elavult számítás megszakítható, félkész mesh nem publikálódik.
- Valódi chunk-emissziós cache: változatlan feloldott geometria mellett
  újrahasznált terrain/aux adatok. Nem csak a GPU-upload marad ki.
- ND-77 material-bucket sorrenddel egyező TileId-tömb; a kirajzolt quadhoz
  LOD-megállási ok, proxybecslés, szint és morph kapcsolható.
- ND-78 a később biztosan eldobandó base-tengerfeneket már a splitkvóta
  előtt kizárja. Nem új óceánmodell vagy teljes láthatósági bizonyítás.
- ND-79 kisebb küszöb költségpróba: 6/5 px közepes zoomon ~33× levelet,
  23640 apró chunkot is okozott; nem kapcsoltuk be.
- ND-80 rendercsomagolás külön a tile-kiválasztástól: max 256 levél/chunk,
  visszaösszevonás 128-nál, stabil hierarchikus partíció. Változatlan fedés.
- ND-81 pontos vetület-cache: azonos kamera/proxy mellett nincs ismételt
  metrikaszámítás; külön selection/balance/resolve/emit részidők.

Élő részfunkció-bizonyíték: ND-80 upload p90 25,44→5,29 ms két eltérő
kameraútban, de request medián még ~0,54 s és 4–5 s felzárkózás.
ND-81 állókamerás cache működött; mozgáskor 285–374 ms selection és
nulla cache-találat maradt. Nem lett ettől sima az első zoom.

### 5.5 Víz-LOD, publikáció, hibabiztonság (ND-82–87/89/92–94)

- Önálló `WaterLodSource`: ténylegesen emittált base-vízmaszk és
  tengerszintsugár, külön előző cut/kvóta/budget; nem a tengerfenék-tiltás.
- 8192 vízlevél, 256 új split/request; víz-közösélek és teljes base-fedés.
  Saját RGB/sarokfeloldás, nyers modellmagasság/meglevő színfüggvény,
  max 65536 szín-cache bejegyzés. Nincs víz-morph.
- Két váltott víz-rendercél és külön statikus vízmaszk; világváltás
  invalidálja a forrást/cache-t. Azonos víz-cutnál nincs új emisszió/upload.
- ND-85: leválasztott spare mesh-ekbe több frame-es staging, külön végső
  commit; a régi kép addig érintetlen. `useStagedTerrainUpload` kapcsoló.
- ND-86: víz/border előkészítés a közös sorban, konkatenálás a workerben.
  Egy nagy víz/border mesh továbbra is monolitikus natív job lehet.
- ND-87: inaktív rendercél/komponensek/diagnosztikai burkoló előkészítése
  a stagingben, nem a látható csere pillanatában.
- ND-89: maszkterv élő indexírás nélkül, tulajdonos- és revízióvédelem.
  Elavult terv nem alkalmazható másik világ/mesh maszkjára.
- ND-92: részlegesen sikertelen natív indexfeltöltés után a teljes eredeti
  statikus terrain/víz indexbuffer visszatöltése. Egy CPU-reset önmagában
  nem igazol GPU-helyreállítást; a teljes visszatöltés csak a hibaág része.
- ND-93: legfeljebb 128 inaktív kulcs célérték, max 8 ürítés / 0,5 ms
  puha takarítási keret. Csak saját runtime mesh-eket szabad Destroy-olni.
- ND-94: külön `terrainMesh` és `terrainTarget` job, közös **2 ms puha /
  128 job/frame** keret. Commit csak a teljes sor után, külön Update-ban.

A single-flight a stagingre is kiterjed. Kamera mozgásakor a már zajló
staging nem dobódik el: előbb konzisztensen publikál, utána jön frissebb
kérés. Világ/konfiguráció/Build/disable megszakíthatja. Ez fontos késleltetési
tradeoff, nem hiba nélkül megváltoztatható részlet. Egy natív Mesh API-hívás
nem preemptálható, ezért a 2 ms **nem kemény maximum**.

### 5.6 Lépték, relief, kamera és időmérés (ND-84/88/91/95)

- `ScaleBarMath`, `PlanetOrbitCamera.ScaleBar`, `PlanetGridMesh.ScaleSurface`:
  a csík végpontjaiból a modellből származó radiális felszíni találatok,
  majd fizikai gömbi távolság `PlanetConstants.RadiusMeters` alapján.
  Nem a 111× relief hegy-völgy útvonalhossza, nem állandó km/pixel.
- Érvénytelen találatnál nem kitalált szám jelenik meg. ND-95 óta pontos
  kamera/target-mátrix, vetület, pixelRect és világrevízió a kontextuskulcs;
  rövid hibakiegyenlítés csak azonos kontextusban. Frissítés ~0,15 s.
- ND-88 fizikai relief: `radius / 7420000` Unity-egység/méter, túlrajzolás 1.
  Ez magyarázta a „eltűntek a hegyek” élményt: a régi `0.001 × 1.5`
  valójában ~111,3× vertikális túlzás volt. Nem a tile-cache lapította le.
- A `usePhysicalReliefScale` bekapcsolva a `SynchronizePhysicalReliefScale`
  életciklusban 1-re állítja a reliefet; emiatt tűnhetett nem szerkeszthetőnek.
  Egyéni túlrajzoláshoz előbb ezt kell kikapcsolni. Az átadott scene-ben
  **ki van kapcsolva**, fizikai alap elevationScale mellett relief=111.
- ND-91 `PlanetOrbitCamera.Surface` és `OrbitSurfaceMath`: helyi modell-
  felszíntől számolt radiális kameratávolság, near-plane/legacy minimum
  figyelembevételével. A tesztelt scene közeli minimuma ~0,33 egység.
  **Nem teljes mesh-ütközés, oldalirányú hegyfal- vagy near-plane védelem.**
- ND-95 FlyTo a teljes úton helyi magasságot interpolál, nem csak a végén
  korrigál; kézi beavatkozás megszakítja. Kamera/lépték közös modellminta
  elutasítja az új Inspector-konfig és régi Build adatainak keverését.
- `LodRequestTiming`: monotón Stopwatch timestampből queue, worker,
  ready-wait, staging-wall, commit, total. A korábbi frame-órás requestAge
  Build-időt vagy Editor-szünetet is félrevezetően tartalmazhatott.
  Az ND-95 a mérés óráját javította, nem írta át az egész schedulinget.

### 5.7 Legutóbbi optimalizálások (ND-96–98, még nem commitolt)

Részletes bizonyíték:
[ND-96](docs/reviews/lod-final-batch-nd96-2026-09-12.md),
[ND-97](docs/reviews/lod-cache-allocation-nd97-2026-09-12.md),
[ND-98](docs/reviews/lod-water-reuse-nd98-2026-09-12.md).

**ND-96:**

- `LodGeometryCache` a kamerafüggetlen proxy-bounds/quad geometriát megtartja
  nézetváltások között. Az új kamera mindig újravetít; régi pixelhiba nem reuse-olható.
- `CaptureGeometryFeedback` a már elkészült emissziós terrain mesh quadjait
  méri; az alulbecslő proxyhoz tényleges sarokgeometriát és ősboundsot ad.
  Nincs új Core-minta, nincs GPU-readback. Ez még **előkészített emisszió**,
  nem feltétlenül már képernyőre publikált mesh; az ND-75 külön megfigyelés.
- A nagyobb gyermekgeometriát egy régi lapos szülőquad nem takarhatja el:
  leszármazotti feedbacknél összesített bounds kell, nem csak saját quad.
- A 2:1 balance egy fine-leaf szomszédpasszra egyszerűsült, ugyanazzal a
  rendezett split-sorrenddel/kerettel; legacy-egyezési tesztek védik.
- A feloldott saroklista közvetlenül átmegy az emitbe: nincs kétszeri
  `GetAdaptiveCorners` ugyanazon chunk emissziójához.
- Scene és kód pixelcél **8 normál / 7 első split**. A kipróbált 8/6
  korai cél túl drága volt. A globálisan nyers tengerfenék-proxy is
  visszavonva: 200 ezres keret és 13–16 s cut lett belőle.

Az izolált mozgókamerás geometria-cache próba 22–26% cut-időcsökkenést
adott azonos cuttal. **A sűrűbb teljes kép mégis több munka:** egy 0 Myr-es
offline pontnál, 160 távolságon 12/10 cél: 3379 levél / 1 hullám / 191 ms;
8/7 cél: 70051 levél / 18 hullám / 1166 ms csak cut. 127-nél 91046 levél /
22 hullám / 1499 ms, 105-nél 79397 / 26 / 2547 ms. Emit/upload és új
feedback-hullámok nincsenek ezekben. Ne ígérj ezekből teljes zoomgyorsulást.

**ND-97:**

- `LodTerrainEvaluationCache.ResetView` törli a nézetfüggő értékeket, de
  megtartja a dictionary kapacitását. A `Reproject` új tárolót adó kontroll marad.
- Feedback-revízió tile-onként és az ősláncon: csak az érintett metrika
  avul el, nem ürül az egész szótár minden Record után.
- Pontos quad-egyenlőség komponensenként, nem boxinggal járó
  `ValueType.Equals`. 1000 azonos Record korábban 1368000 B, utána 0 B.
- Páros mozgókamerás tárolópróba: 52–61% kevesebb cut-allokáció,
  5–9% kevesebb cut-idő. Nem peak RAM: a kapacitás megtartása rezidens
  memóriát tart életben. Világváltás új cache-t igényel.

**ND-98:**

- A már stabil víz-kiválasztás újrahasználható további terephullámokban.
- Nem elég `DeferredSplits==0`: azonos teljes vetület és LOD-paraméterek
  mellett egy új teljes selectionnek pontosan ugyanazokat a leveleket kell
  adnia. Csak ez igazol determinisztikus fixpontot hiszterézis/balance után.
- Új eredményobjektum megosztott immutábilis fedéssel, friss nullázott
  munkastatisztikával. Nincs előzménylánc vagy új, növekvő globális cache.
- Bemenet-/cache-/forrásvalidálás és cancellation a gyors út előtt is;
  külső geometriafeedbackes cache-nél nincs reuse.
- Kontroll: `reuseStableSelection:false`. Szintetikus teljes vízgömbön
  120 állókamerás kérésből 96 reuse: 3622,61→959,74 ms víz-selection,
  815256856→283173504 B allokáció. 60 mozgó nézetben 0 reuse és azonos
  allokáció. Ez **74% részfolyamat-gyorsulás**, nem 74%-kal gyorsabb app.

## 6. Aktuális adatfolyam és kódtérkép

```text
Build / új világ
  → exact statikus bázis, klasszifikáció, sarkok, hidrológia
  → immutábilis TerrainLodProxy + WaterLodSource

főszál: kamera/config snapshot, single-flight request
  → worker: projected cut + balance
  → óceánszűrés + teljes coverage + közösél/morph feloldás
  → bounded chunkok + geometria-diff + változott emisszió
  → kész terrain quad feedback a következő cut számára
  → saját víz-selection/emit + aux-pack + maszkterv
  → főszál: leválasztott mesh/rendercél staging több Update-ban
  → külön commit: referenciák, aktív rétegek, maszkok, applied állapot
  → ND-75: a publikált mesh aktuális kamerás megfigyelése
```

Az alábbi fájlnevek az `unity/WorldGenViewer/Assets/Scripts/Viewer/` alatt értendők:

| Fájl / szimbólum | Mire használd |
|---|---|
| `PlanetGridMesh.cs` | `Build`, config-snapshot, `RecomputeCutAndRebuildAdaptiveMesh`, worker/emit/commit orchestration, statikus bázis és UI |
| `PlanetGridMesh.Refinement.cs` | request elavulás, metrika-cache, feloldott pozíciók, ND-96 feedback |
| `PlanetGridMesh.WaterRefinement.cs` | önálló vízforrás, kiválasztás, emit, maszk és publikáció |
| `PlanetGridMesh.Upload.cs` | pending staging, jobok, prepare/publish/discard |
| `PlanetGridMesh.AuxiliaryUpload.cs` | víz/border konkatenálás, leválasztott mesh és közös publikáció |
| `PlanetGridMesh.ChunkResources.cs` | inaktív sor és saját Mesh/GameObject élettartam |
| `PlanetGridMesh.RenderDiagnostics.cs` | kizárólag alkalmazott geometria-snapshot, ND-75/77 |
| `PlanetGridMesh.ScaleSurface.cs` | érvényes világfüggő közös felszínminta, revízió |
| `PlanetOrbitCamera.cs`, `.Surface.cs`, `.ScaleBar.cs` | orbit/zoom/FlyTo, helyi clearance, km-lépték |
| `Lod/AdaptiveQuadTree.cs` | budget/frontier, cut, 2:1 balance |
| `Lod/ProjectedLodView.cs` | perspektivikus metrika, `LodSelectionWork`, pontos metrika-cache |
| `Lod/LodGeometryCache.cs` | ND-96 geometriatárolás/feedback és ND-97 helyi revízió |
| `Lod/LodCoverage.cs`, `LodCornerResolver.cs` | teljes fedés és valódi közösél-geometria |
| `Lod/DynamicMeshChunking.cs` | rendercsomagolás, nem új tile-LOD |
| `Lod/TerrainIndexMask.cs` | eredeti indexállapot, előkészített terv, restore |
| `Lod/LodUploadBatch.cs`, `InactiveChunkQueue.cs`, `LodRequestTiming.cs` | motorfüggetlen ütemezési/élettartam-/időmérési szerződések |

### Kritikus invariánsok a viewerben

1. **Koordináta:** Core `(x,y,z)` ↔ Unity-local `(x,z,y)` a
   `BodyFrameConversion` szerint. A kamerát előbb Planet-local térbe kell
   hozni (`InverseTransformPoint/Direction`), utána tengelyt cserélni.
   A feedback `Vector3` sarkainál is kell az explicit csere. Egy sima
   `ToSurfacePoint` csomagolás nem feltétlenül végez koordinátakonverziót.
2. A worker nem hívhat Unity natív API-t. Kamera/mátrix/konfig snapshotot
   használ; nem olvashat új világból paramétert a régi világbufferek mellé.
3. A mutable cache-ek **nem szálbiztosak**. A normál cut/emit worker
   egyedüli tulajdonos. A diagnosztikai worker nem olvassa őket.
   `ResetView` csak a korábbi használat befejezése után történhet.
4. Build futó worker mellett nem ürítheti annak cache-eit; megszakítás/
   elhalasztás és kész eredmény eldobása kell. Single-flight ≠ korlátlan taskindítás.
5. Chunk/cut/water **applied** állapot csak sikeres publikáció után válthat.
   Staging nem kapcsolhatja le a régi képet, és nem rejthet el base quadot.
6. Egyenlő levélhalmaz nem elég terrain-emisszió reuse-hoz: változhat a
   morph és a szomszédos coverage közösél-pozíciója. A víz külön eset,
   nincs morph; az ND-98 csak teljesen azonos kiválasztási kulccsal működik.
7. Élettartam: csak saját runtime Mesh pusztítható, megosztott/scene asset nem.
   Hibaágban a CPU- és natív indexállapot együttes helyreállítása szükséges.
8. A metrika/geometry cache 262144-es darabkorlátja nem teljes byte-budget.
   Feedback teljes ősláncának kell hely: telítettségkor Record elutasítható.
   A proxy-cache telítettsége után kiértékelés marad, csak tárolás nem.
9. Az ismert geometriák union bounds-a világon belül nem szűkül; a cache
   nem bizonyít minden még nem mintázott mikrocsúcsot, és nem occlusion-rendszer.

## 7. A tényleges scene-beállítások

Átadáskor a `unity/WorldGenViewer/Assets/PlanetView.unity` alapján:

| Mező | Érték |
|---|---:|
| radius | 100 |
| adaptiveBaseLevel / adaptiveMaxLevel | 8 / 20 |
| targetTilePixelSize / initialRefinementPixelSize | 8 / 7 |
| useAdaptiveLod / useTerrainLodProxy | 1 / 1 |
| useGpuGeometry | 0 |
| useBoundedDynamicChunks / useStagedTerrainUpload | 1 / 1 |
| geomorphRangeFraction | **0,36** |
| usePhysicalReliefScale | **0** |
| elevationScale | 0,000013477088948787062 |
| terrainReliefExaggeration | **111** |
| showPhysicalScaleBar / followLocalSurface | 1 / 1 |

A 0,35-ös morph korábbi dokumentációs pillanatkép; a mostani scene és
20:23-as log 0,36. Ne írd vissza automatikusan. A komponens alapértéke és
a már szerializált scene eltérhet, mindig a futó log/Inspector számít.
`useGpuClassification=True` a fejlécben nem jelenti, hogy a CPU terrain
helyett ténylegesen a hiányos GPU-besorolás fut: az ND-69 a CPU-geometria
útját külön a teljes CPU-modellre kényszeríti. Ne kapcsold be a GPU-
geometriát a lassulás gyors megkerüléseként, az eltérő kísérleti út.

## 8. Mérések, naplók és a legújabb bizonyíték

### 8.1 Korábbi, a javításokat motiváló konkrét hiba

`unity/WorldGenViewer/Logs/PerfLog_20260912_140221.txt`, 14:05:40.291,
frame 19990, már nyugodt állapot:

- seed `184482873278464`, idő `585.06399658203122 Myr`, sea
  `1591.1231991793884`, relief 111, viewport 957×583;
- cameraCore `(52.594116,-84.707642,78.634064)`, d=126.983627;
- terrain TileId **`8900000000015424`**, face 4, L9, u482/v4;
- tényleges quad **26,975 px**, proxy **9,635 px**, küszöb 12 px,
  stop=`below-threshold`, morph=1.

Ez nem feltöltési késés volt: a proxy alulbecsülte a kész geometriát.
Az ND-96 konkrét quad-adatos regressziója innen ered, de a hiányzó teljes
kameratengelyek miatt az offline teszt nem pixelpontos teljes log-replay.

Ugyanebben a logban 104 commit / 256 staging slice, szelet p50/p90/max
1,34/2,01/3,18 ms, commit 0,89/1,93/4,55 ms. 1568 inaktív kulcs ürült,
337 átmeneti többlet a következő mintában 128-ra csökkent. Teljes request
385,2/671,1/9905,6 ms, még a régi requestAge problémájával. A 9,9 s-os
eset Editor-szünet/fókusz oka nem tisztázott; a felhasználó nem emlékszik,
ne kérdezd ugyanazt újra. A közeli 0,33-as kamerakorlátot ez sem érte el.

### 8.2 Friss log az átadás készítésekor — 20:23-as futás

Fájl: `unity/WorldGenViewer/Logs/PerfLog_20260912_202309.txt`
(328907 byte a vizsgálatkor; utolsó sor 20:24:20.945).
Ez az **aktuális legújabb megtalált log**, nem a 14:02-es.
Világ: ugyanaz a seed, **0 Myr**, sea=1623,9039902756563,
viewport **1197×878**, 8/7 px cél, relief 111. Tehát a két log eltérő
világidő/nézet/felbontás, közvetlen teljesítmény-A/B nem állítható.

64 ND-95 időzített/alkalmazott kérés. Kvantilis: rendezett minták
`floor((n-1)*p)` indexe; minden idő ms:

| Fázis | p50 | p90 | max |
|---|---:|---:|---:|
| Teljes kérés | 746,319 | 1073,420 | 3888,386 |
| Worker | 713,008 | 1021,003 | 1405,996 |
| Queue | 0,039 | 0,046 | 0,074 |
| Kész worker → átvétel várakozás | 2,139 | 21,980 | 2835,353 |
| Staging falióra | 19,058 | 42,107 | 81,572 |
| Commit | 0,901 | 1,277 | 5,717 |
| Selection (worker részideje) | 161,26 | 282,41 | 479,34 |
| Balance (worker részideje) | 76,83 | 163,79 | 247,59 |
| Resolve/check (emit részideje) | 75,12 | 196,45 | 385,79 |
| Corners | 110,92 | 138,21 | 161,10 |
| Geometry feedback | 19,95 | 33,75 | 43,56 |

Az oszlopok kvantilisei nem ugyanazon kérést jelentik, ne add össze őket.
A részidők közül több másik fázis részhalmaza, szintén nem összeadandó.

Tények:

- `geometryCache=ND96`, `metricStorage=ND97`, ND-95 óra és ND-94 staging aktív.
- 64 water apply-ból **27 `reusedSelection=True`**; az ND-98 gyors út élőben
  is működik. Nem ugyanazon futás ki/be A/B gyorsulási bizonyítéka.
- A végén a geometria-cache **262144** bejegyzésen telített. Feedbackből
  58850 tényleges quad, 66817 bounds, `feedbackRejected=0`.
  `feedbackUnidentified=0`; a forrásazonosítás működik ezekben a kérésekben.
- Utolsó alkalmazás: d=108,173, **64220** terrain levél, 351 chunk,
  max 256 levél/chunk, deepest L15, nadír L12, **1024 új / 3770 halasztott
  split**, feedbackAdded=56, feedbackPending=True. A következő request elindul.
- Utolsó ND-75 minta: 153/153 találat, **p50 6,44 / p90 10,12 / max 10,39 px**,
  dinamikus terep 109, víz 44 találat; invalid/malformed=0. Ez vegyes rétegű,
  mintapontos érték, nem teljes képernyős vagy csak-terep maximum.
- A mintában `lodPending=True`, `refinementPending=True`, kamera-delta=0:
  álló kamera mellett a kép még felzárkózik. **Nem lezárt végállapot.**
- A diagnosztikai scan utolsó mintája ~165 ms külön workerben. Nem 165 ms
  főszálblokk, de nem ingyenes CPU-munka; Profilerben a versengése is mérendő.
- A 3888 ms-os kérésből 2835 ms a kész-eredmény átvételére várás;
  `focused=True` a naplózáskor. Ebből nem állapítható meg a várakozás oka,
  és nem tulajdonítható mind a kiválasztónak vagy a GC-nek bizonyíték nélkül.
- Egyszerű `exception|error` szövegkeresés nem talált találatot ebben a
  PerfLogban. Ez nem helyettesíti a Unity Console/Editor.log ellenőrzését.

**Következtetés a folytatáshoz:** a fő maradék költség normál kérésnél a
workerben van, nem a ~1 ms commitban. Selection/balance, sarok-előkészítés,
coverage-függő resolve/check és ismételt feedback-scan a következő mért
jelöltek. A cache telítettsége és a sok hullám külön vizsgálandó.
Az ND-98 víz-reuse ténye nem jelenti, hogy a terep gyorsan készül el.

### 8.3 Naplóolvasási szabályok

- `ND-75 drawn/size/row`: publikált mesh aktuális kamerán, S=statikus terrain,
  D=dinamikus terrain, W=statikus víz, w=dinamikus víz.
- `ND-72 render`: kéréskori **nadír terrain** quad; víz alatt nem a látható víz.
- ND-77: TileId + a publikált kérés megállási oka; ezt vesd össze a valódi quaddal.
- `workCache=ND81`: metricHits/Computed kérésköltség; ND-96 geometry és ND-97
  reset/feedback számlálók cache-életcikluson belül összesítettek.
- `feedbackMeasured/Added/Ms/Pending/Unidentified`: kérésenként; az
  Entries/Bounds/Rejected felhalmozott cache-adat.
- `ND-95 request timing`: total falióra és felbontása. `stageTotal` aktív
  staging CPU-munka, `stagingWallMs` a közben eltelt frame-ekkel együtt.
- `ND-83 water apply`: `reusedSelection` nem ugyanaz, mint `reusedMesh`.
- ND-93 ownedMeshes/inactiveKeys/spares darabszám, nem mért RAM/VRAM byte.

## 9. Tesztelés, reprodukció és helyi környezet

### 9.1 Utolsó lefutott ellenőrzések

Az ND-98 kódállapoton, közvetlenül e dokumentációs kör előtt:

- solution build: 0 hiba / 0 warning;
- teljes Debug solution: **765/765**, Core 384, viewer 373, CLI 8;
- viewer Release: **373/373**;
- Unity `Assembly-CSharp` offline: 0 hiba / 83 warning;
- Unity `WorldGen.Viewer.Lod.Tests` offline: 0 hiba / 4 warning;
- `git diff --check`: tiszta, külön CRLF figyelmeztetés lehet a másik
  munkaszál vektorfájljaira.

ND-96 +25, ND-97 +15, ND-98 +18 új .NET-esetet hozott a viewerbe.
Kiemelt tesztek: budget/frontier, coverage/stitch/cubeface, pontos metrika-
és balance-kontroll, cache-élettartam és kapacitás, cancellation, maszk-
revízió/restore, víz-cut, fizikai lépték, kamera, időmérés és nullallokáció.
A szigorú allocation-assert dedikált mérőszálon fut; ne lazíts rajta csak
azért, hogy egy hibás allokációs eredmény zöld legyen.

**Natív EditMode kapu még nyitott:** `GeometryFeedbackTests`,
`NavigationMeasurementTests`, `ChunkUploadResourceTests`,
`TerrainIndexMaskMeshTests` és a teljes `WorldGen.Viewer.Lod.Tests` csoport.
Korábbi ideiglenes Editor-tesztindító híd/kérés el lett távolítva; nincs
bent hagyott automatikusan később lefutó művelet. Tesztfordítás ≠ tesztfutás.

### 9.2 Használt parancsok

PowerShell, repo gyökér:

```powershell
git status --short
git log -8 --oneline --decorate
dotnet build WorldGen.sln --no-restore -p:_EnableDefaultWindowsPlatform=false -v:quiet
dotnet test WorldGen.sln --no-restore -p:_EnableDefaultWindowsPlatform=false -v:minimal
dotnet test tests/WorldGen.Viewer.LodChunking.Tests/WorldGen.Viewer.LodChunking.Tests.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -v:minimal
git diff --check
```

Új checkoutnál előbb restore kell. A `_EnableDefaultWindowsPlatform=false`
a helyi SDK/projekt környezethez használt kapcsoló, nem algoritmikus javítás.
`Directory.Build.props` az outputot **artifacts/** alá irányítja: a Core
package forráskönyvtárában létrehozott bin/obj DLL-ek Unity duplikált
assembly-nevet okoznának. Ezt ne állítsd vissza.

Offline Unity-forrásellenőrzés a jelenlegi gépen:

```powershell
dotnet build unity/WorldGenViewer/Assembly-CSharp.csproj --no-restore -p:_EnableDefaultWindowsPlatform=false -p:TreatWarningsAsErrors=false -p:CustomAfterMicrosoftCommonTargets=F:\Claude\wg\artifacts\lod-zoom-validation.targets -v:quiet
dotnet build unity/WorldGenViewer/WorldGen.Viewer.Lod.Tests.csproj --no-restore -p:_EnableDefaultWindowsPlatform=false -p:TreatWarningsAsErrors=false -p:CustomAfterMicrosoftCommonTargets=F:\Claude\wg\artifacts\lod-zoom-validation.targets -v:quiet
```

Az `artifacts/lod-zoom-validation.targets` **lokális, ignorált segéd**, nem
portable buildrendszer. A Unity által generált csproj néha nem tartalmazta
az új partial/LOD/Editor-fájlokat importig; ez a fájl projektfüggő
`Compile Include` sorokat ad hozzá `Exclude="@(Compile)"` védelemmel.
Friss Unity projektgenerálás az elsődleges. Más gépen ne vakon használd
az abszolút pathot; új fájlnál ellenőrizd a tényleges Compile-listát.
A 83 warning elnyomása nem megoldás a valódi compile errorokra; a fentiek
csak a meglévő warning-as-error öröklődést oldják fel ebben az offline kapuban.

Új LOD-forrás három helyen lehet releváns: Unity asmdef/import,
linkelt .NET tesztcsproj, önálló diagnosztikai csproj. `internal` teszt-/runtime
láthatóság és Unity/.NET nyelvi metszet ellenőrzendő. Párhuzamos munkánál
közös `PlanetGridMesh.cs` írása korábban átmeneti buildhibát okozott; külön
fájlfelelősség és integráció utáni teljes fordítás kell.

### 9.3 Diagnosztikai probe

`docs/reviews/ViewerLodDiagnostic.csproj`, `lod-zoom-probe.cs`:

```powershell
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --moving-cache
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --storage-cache
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --closure-tuned
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --water-reuse
```

További régi módok a `Main` elején: `--proxy`, `--onset`, `--drawn`,
`--progressive`, `--shoreline`, `--work-cache`, `--closure` stb.
Mindig olvasd a konkrét módot: ezek **nem** a teljes élő pipeline vagy
aktuális világ általános benchmarkjai. A terrain próbák 0 Myr-es, régi
relief-approx alapú mintája nem a 585 Myr-es log hű visszajátszása.
Az ND-97 kontroll az új kódon csak a tárolópolitikát váltja, nem egy régi
teljes bináris A/B. Az ND-98 teljes vízgömb, nem partvonalas világ.

Python a Codex-környezetben nem volt telepítve/elérhető (`python` hiányzott,
`py -3` nem talált interpretert). Emiatt nem állítunk friss Python/KAT-
futtatást a viewer-csomagokra. Másik szál ND-90 történetileg dokumentált
Python/KAT-eredményét ettől külön kezeld. Numerikus folytatáshoz:
`verify_kat.py`, generátorok újrafuttatása és byte-összevetés a verziózott
vektorokkal szükséges; kézzel ne szerkeszd a JSON-orákulumot.

A meglévő CI Debug/Release Linux x64/ARM64, Windows és macOS mátrix,
`fail-fast:false`; a reference-oracle KAT után újragenerál és összevet.
Ne minősíts platformeltérést flakynek. A mostani fejlesztői branch távoli
CI-futása nincs e dokumentummal igazolva.

## 10. Kapcsolódó, másik szálból származó munkák és nyitott termékigények

### ND-90 — modellbeli természetesebb kéregátmenet

[Külön munkaszál naplója](history/2026-09-12-tectonic-boundary-transition-nd90.md):
vegyes óceáni/kontinentális két legközelebbi lemez bázisa `0.005` gap-sávban
smoothstep-pel keveredik; külön uplift-plafon 1500→1000 m. Az `isOceanic`
diszkrét anyagtulajdonság marad. A teljes eleváció ettől még nem ≤1000 m.
Ez numerikus világváltozás, nem viewer-cache. CLI `CurrentFormatVersion=2`,
v1 `.worldpkg` explicit inkompatibilis. Gyökér `VERSION` továbbra is
`0.11.0-dev` checkpointjelölő, nem teljes generator/simulation/schema
verziórendszer. Ezt külön kell továbbtervezni, nem a VERSION átírásával elfedni.

A külön history Python/KAT/downstream újragenerálást rögzít; a Core/CLI
aktuális .NET-tesztje zöld. A természetes part **vizuális elfogadása nincs**.
A teljes „hegységek érzékelhetőek, part alacsony” célhoz M13 normál/
világítás/árnyalás munka is kell. Nem javítandó globális renderer-clamppal.

### Deep-time léptetőgombok

A közös kódban megvannak a ±`1y,10y,100y,1ky,10ky,1my,10my,100my`
lépések, 600 px panel, pozitív/negatív sorok, double Myr és Gyr-szövegtükör.
[Saját napló](history/2026-09-11-deep-time-step-buttons.md), külön szálhoz
kapcsolódó utólagos diff is van. Nem a tile-cache eredménye; élő UI-kapu külön.

### Pillanatnyi hőmodell és overlay — kidolgozott, nem implementált terv

[Backlog](docs/backlog.md) „Pillanatnyi hőmérsékletmező és overlay”:
29 felhasználói döntés rögzítve. **Nem napi átlagos hőszínezés** a végső igény:
aktuális nappal/éjjel, széllel hideg/meleg szállítás; `Ts/Ta` két hőanomália,
fix L6 teljes bolygó, egész tickes közös `SimulationTime`, külön `WindTick`,
spin-up/checkpoint, konzervatív upwind advekció és felszíntípusonkénti tehetetlenség.
Hatlapos fixpontos scalar GPU-megjelenítés, fix °C-paletta, közös UI,
kurzor-debug. Öt külön ND jóváhagyott, számozás előtt ND-62 ütközés feloldandó.
Core-solver/renderer **nem készült**; a `SunController.currentTimeDays` és
`PlanetGridMesh.climateDayT` szétválása ismert integrációs feladat.
Ne kezdd újra a már lezárt 29 kérdést, és ne implementálj helyette simple daily overlayt.

`docs/08-development-idea-2-non-earthlike-planets.md`: koncepció, nem
elfogadott architektúra/implementáció. `docs/base_models/`: fizikai modellek
állapotleírása, nem új solver. ND-20 Burst strict CI, ND-21 HDRP felhő és
több M13/deferred feature továbbra is nyitott. Nem minden milestone-név
jelent teljes spec-lefedettséget.

## 11. Javasolt folytatás Sonnet/Opus számára

### Első munkamenet — ne indulj újabb vak küszöbhangolással

1. Ellenőrizd a munkafát/brancheket, olvasd el az ND-96/97/98-at és a
   releváns partialokat. Őrizd meg a másik szál módosításait. Az új napló
   és az átadott kód marker-/scene-egyezését ellenőrizd.
2. Futtasd a .NET regressziót és a natív Editor-teszteket, ha az Editor
   elérhető. Ha nem, ezt hagyd explicit nyitva; nem a felhasználó helyetti
   vizuális teszt. Ne várakozz végtelenül néma teszthídra.
3. A 20:23-as logot teljes kérésláncok szerint bontsd: az első 1–6 zoom,
   majd álló közepes/mély nézet. Mennyi idő a kamera megállásától a stabil
   képig? A fájl vége még nem fixpont; ebből nincs teljes felzárkózási idő.
4. Első mérésalapú teljesítményjelöltek: **balance + resolve/check**, a
   teljes corner-előkészítés, majd a cache telítettsége/feedback-scan.
   Utolsó kérésben balance ~182 ms, resolve/check ~381 ms, corners ~138 ms,
   feedback ~44 ms: nem újabb commit-mikrooptimalizálás az első jelölt.
5. Egyetlen izolált változtatást mérj azonos cut/pozíció/kameraút mellett;
   régi út kontrollként, cancellation/budget/world-change tesztekkel.
   Utána csak indokolt esetben nyúlj a kvótához vagy minőségi célhoz.

Lehetséges következő kódfeladatok, **nem előre eldöntött implementációk**:

- Ismételt közösél-feloldás csökkentése pontos coverage-/morph-függőség-
  kulccsal. `CaptureResolvedPositions` még teljes listát épít a meglévő
  chunkra is. Puszta azonos levélhalmazra reuse regressziót hozna vissza.
- Tiszta TileId-topológia/szomszédműveletek korlátos reuse-ja a balance-ban;
  kockalap-szélek, sorrend és budget pontos egyezése kötelező.
- A 262144-es geometry cache telítettség utáni költségének külön mérése.
  Ne emeld vakon a korlátot: mi a rezidens byte-költség, mi járna jó
  evictionnel, milyen feedback szükséges a helyes korai döntéshez?
- Feedback ismételt mérésének kihagyása csak bizonyítottan azonos
  view/mesh/cél/geometria-állapotnál; ne veszíts el szükséges új hullámot.
- Diagnosztikai worker CPU-költségének Profiler-mérése külön a rendertől.
  Ki/be összevetés hasznos, de a mérés kikapcsolása nem élességjavítás.

### Minőségi hiba esetén döntési sorrend

1. A nagy quad melyik réteg? Valódi ND-75 adat vagy csak proxy/nadír?
2. Pending vagy kész? Régi kamera vagy már nulla kamera-delta?
3. Stop oka: küszöb, splitkvóta, leafbudget, maxLOD, ocean exclusion,
   culling, hiányzó feedback-azonosító vagy telített feedback?
4. Actual quad vs proxy és morph. Ha kész geometriát becsül alá, ez
   metrikahiba; ha a jó célhoz sok hullám kell, ez felzárkózási költség.
5. Ha a geometria megfelelően finom, de nincs új látható modellrészlet,
   ne oszd tovább vaktában. Modellfrekvencia/relief/árnyalás külön kérdés.

### Tiltott/rizikós rövidítések

- Ne hozd vissza az ND-71 per-node teljes terrain-mintázást.
- Ne kapcsold vissza a mindenhol nyers tengerfenék-proxyt.
- Ne emeld vakon a 200000-es renderbudgetet/maxLOD-t, és ne csökkentsd
  globálisan a pixelt a teljes felzárkózási költség mérése nélkül.
- Ne kapcsold ki a hiszterézist, stitch-et, teljes coverage-t vagy a
  pozícióérzékeny invalidálást a gyorsulási szám kedvéért.
- Ne tekintsd a 2 ms staginget hard frame-garanciának; az aux mesh monolitikus lehet.
- Ne merge-elj régi kísérleti viewer-fájlt a mostani partial/caching rendszerre.
- Ha feladatot delegálsz, külön fájlok és explicit tulajdonosok legyenek;
  ugyanazt a `PlanetGridMesh.cs`-t ne írja két munkaszál egyszerre.

Sonnet számára a szűk kód-/tesztfeladatok pontos kontrollúttal átvehetők.
Opus bevonása a metrika/coverage/ütemezés új architekturális döntésénél lehet
hasznos, de egyik modell neve sem helyettesíti a páros tesztet és az élő
visszajelzést. Az átadás nem igényel párhuzamos agenteket vagy teljes rewrite-ot.

## 12. Végső felhasználói ellenőrzés és kész-definíció

A teljes, már összeállított lista:
[ND-96 — Teljes végső ellenőrzési lista](docs/reviews/lod-final-batch-nd96-2026-09-12.md).
ND-97/98 kiegészítései szerepelnek az elején. Ne gyárts mellé egy eltérő
feltételrendszerű második listát. A kézi rész durván 15–25 perc; natív
teszt/Profiler ettől külön. Fő csoportok:

- Import/Console/EditMode, majd távoli indulás.
- Első scrollok, közepes zoom megállással, mély zoom, gyors be/ki fordítás,
  oldalirányú mozgás, stabilizálódás és morph.
- Hegy, síkság, meredek part, nyílt víz/jég, pólus/kockalapél, border.
- Minimumkamera és near-plane oldalak, FlyTo oda/vissza/megszakítás.
- Fizikai lépték, viewport/aspect/FOV és érvénytelen találat kezelése.
- Deep-time világváltás folyamatban lévő zoom közben, 5–10 távoli terület,
  Play stop/start, memória/mesh/GC/frame-csúcsok.

Élő logban működő részfunkció ≠ felhasználó által elfogadott teljes kép.
Az aktuális log nem bizonyítja a 0,33-as közeli kamerakorlát elérését,
a teljes élességi végállapotot vagy a lépték teljes pontossági elfogadását.

## 13. Haladás és ráfordítás — becslés, nem mérés

[Rögzített súlyozási audit](docs/reviews/m9-progress-audit-2026-09-12.md):

| M9 munkacsoport | Súly | Állapotpont |
|---|---:|---:|
| Terepkiválasztás/fedés/közös élek | 25% | 75/100 |
| Korai részletesség/folyamatos átmenet | 25% | 50/100 |
| Reakcióidő/főszál/erőforrás | 20% | 50/100 |
| Víz/rétegek együttállása | 15% | 75/100 |
| Navigáció/FlyTo/kamerabiztonság | 10% | 50/100 |
| Rajzdiagnosztika/km-lépték | 5% | 75/100 |

Összesen **61,25%, kb. 61%**, durva bizonytalansági sáv 55–65%.
A 20:23-as új log működési bizonyítékot ad az ND-96–98-hoz, de a teljes
reakcióidő/vizuális kapu nem zárult: most nem indokol új pontszintet.
Ez a szűk M9 + jóváhagyott zoom/navigáció kiegészítések mutatója, nem az
egész generátor vagy teljes M13 százaléka.

Aktuális durva maradék: **5–11 fejlesztői óra**:

- natív Editor, víz/kamera/FlyTo/lépték integráció: 1–3 óra;
- teljes felzárkózás mérése és célzott korrekció: 3–6 óra;
- hosszú erőforrás/FPS és végső regresszió: 1–2 óra.

Ez nem garantált határidő. A most talált ~1 s worker/hullám és a még
folyamatban lévő nagy cut miatt a performance-sor kockázatos; ha új
renderarchitektúra, teljes ütközésvédelem vagy Core-mikroterep kell,
**újrabecslés szükséges**. A felhasználói várakozás nincs az órákban.

A teljes Codex-időszak utólagos tényleges munkaórája nem rekonstruálható:
nincs hiteles időnapló, átfedő munkaszálak és egymást felülíró részbecslések
voltak. Ne add össze őket és ne találj ki összesített „eltöltött órát”.
Legutóbbi csomagok szakmai ráfordítás-egyenértéke: ND-96 6–12,
ND-97 1–3, ND-98 1–2 óra; ezek sem mért idők.

## 14. Rövid indítóüzenet a következő Claude-sessionhöz

> Olvasd el a CLAUDE.md, AGENTS.md és kt_2_co2cl.md fájlokat. Az aktív ág
> codex-handoff, HEAD 99b3ac4, ND-96–98 még munkafában; külön ND-90 Core/
> referencia/CLI változások is vannak, ezeket őrizd meg. A legújabb log a
> PerfLog_20260912_202309.txt. A zoom célja még nem elfogadott: a log végén
> álló kameránál 64220 levéllel további finomítás fut, a worker ~1 s/kérés.
> Előbb ellenőrizd a kód és log egyezését, majd a balance/resolve/corner
> költséget vizsgáld pontos kimeneti kontrollal. Ne módosítsd csendben a
> reliefet vagy a pixelcélt. A felhasználó később egyben ellenőriz a meglévő
> teljes próbalista alapján. Commit/push csak új kérésre.

## 15. Háttérben elkészült, a felhasználó által még nem ellenőrzött fejlesztések

Összeállítva 2026-09-12-én, a Claude-átvétel első munkamenetében, a history-
naplók, a review-dokumentumok és a `docs/06-user-verification-checklist.md`
alapján. Ide csak az került, amihez **kód készült**, de a felhasználó
élő, vizuális vagy működési elfogadása hiányzik.

Két állapotot különböztetünk meg:

- **Élő log látta:** egy PerfLog igazolta, hogy a kód fut. A kép, a
  FPS és a memória elfogadása ettől még hiányzik.
- **Semmi élő bizonyíték:** csak .NET-teszt és offline fordítás van mögötte.

A zoom/tile tételeknél az elvárások **azonosak** a
[ND-96 végső listával](docs/reviews/lod-final-batch-nd96-2026-09-12.md);
a hivatkozott „L1–L5” pontok annak 1–5. fejezetei. Ez nem egy második,
eltérő feltételrendszer, hanem fejlesztésenkénti nézet ugyanarra.

**Javasolt sorrend:** 15.1 → 15.2 → 15.3 → 15.4 → 15.5. Az A–C blokk
együtt kb. 30–45 perc, a D–E blokk további kb. 20–30 perc. Ez durva becslés.
Hibánál elég a pont száma, egy kép a `pics/p.png` helyre és a friss PerfLog.

### 15.1 Előkészítés és natív Editor-tesztek

**1. Unity-fordítás és EditMode tesztek (ND-91/92/95/96 Editor-esetek)**

- *Mit csináltunk:* 5 Unity EditMode tesztfájl készült vagy bővült
  (`Assets/Tests/EditMode/Lod/`): `GeometryFeedbackTests` (4 paraméteres
  eset), `NavigationMeasurementTests`, `ChunkUploadResourceTests`,
  `TerrainIndexMaskMeshTests`, `AdaptiveQuadTreeTests`.
- *Állapot:* **semmi élő bizonyíték.** Csak offline fordítás volt:
  0 hiba / 83 warning a runtime-on, 0 hiba / 4 warning a tesztprojekten.
  Egyik teszt sem futott natívan.
- *Hogyan ellenőrizd:*
  - [ ] Nyisd meg a projektet, várd ki az importot. A Console-ban ne legyen piros hiba.
  - [ ] Window → General → Test Runner → **EditMode** → `WorldGen.Viewer.Lod.Tests` → Run All.
  - [ ] Inspectorban a PlanetView `PlanetGridMesh` mezői: Target Tile Pixel
    Size = 8, Initial Refinement Pixel Size = 7. CPU-geometria
    (`useGpuGeometry` ki), terrain proxy, bounded chunks és staged upload be.
- *Elvárt:* minden teszt zöld. Egy piros tesztet nem vált ki a vizuális próba.

### 15.2 Tile/zoom — ND-96–98, munkafában, nincs commitolva

**2. ND-96 — geometria-cache, mesh-visszacsatolás, egyszerűsített balance, 8/7 px cél**

- *Mit csináltunk:*
  - A kamerától független proxy-geometria megmarad nézetváltáskor (`LodGeometryCache`).
  - A kész terepmesh valódi sarkai visszajelzik, ha a proxy alulbecsült egy quadot.
    Ez volt a 14:05-ös `8900000000015424` tile hibája: 26,975 px volt a valós méret, 9,635 px a proxy becslése.
  - A 2:1 balance egy szomszédbejárással fut.
  - Az emit nem oldja fel kétszer a sarkokat.
  - A pixelcél 12/10-ről **8/7**-re változott a kódban és a scene-ben is.
- *Állapot:* **élő log látta** (`PerfLog_20260912_202309.txt`:
  `geometryCache=ND96`, `feedbackUnidentified=0`). A log végén álló
  kamera mellett még finomodott, vizuális elfogadás nincs.
- *Hogyan ellenőrizd:* L2 teljes egésze.
  - [ ] Az első 1–6 görgetés szárazföld fölött, egyesével, rövid megállásokkal.
  - [ ] Közepes zoomnál 5–10 s megállás. Mérd, nagyjából mennyi idő után áll meg a finomodás.
  - [ ] Mély zoom, gyors be/ki irányváltás, oldalirányú forgatás.
- *Elvárt:* a finomodás korábban kezdődik, a teljes látható terület
  felzárkózik, nincs alakpattanás vagy pulzáló rács. **Külön jelezd, ha a
  8/7 cél miatt érezhetően lassabb vagy akadósabb lett**, mint korábban. Ez
  ismert kockázat: közepes zoomnál 3379 helyett kb. 70000 levél kell.

**3. ND-97 — cache-allokáció csökkentése**

- *Mit csináltunk:* a nézetfüggő metrikatár nem allokál újra minden
  nézetnél, a feedback helyben érvénytelenít, és a quad-összehasonlítás boxing nélküli.
- *Állapot:* **élő log látta** (`metricStorage=ND97`). GC- és memóriamérés nincs.
- *Hogyan ellenőrizd:*
  - [ ] Unity Profiler (CPU + Memory), 30–60 s folyamatos zoom és forgatás, majd megállás.
- *Elvárt:* a GC.Alloc csúcsok nem rendszeresek. A rezidens memória egy
  plató után nem nő tovább, ugyanazon a világon belül.

**4. ND-98 — stabil vízkiválasztás újrahasználata**

- *Mit csináltunk:* ha a vízkiválasztás bizonyítottan fixpontban van, a
  további terephullámok nem számolják újra.
- *Állapot:* **élő log látta**: 64 vízkérésből 27 `reusedSelection=True`.
  Vizuális próba nincs.
- *Hogyan ellenőrizd:* L3.
  - [ ] Nyílt víz, meredek part és tengeri jég fölött közepes → mély → távoli út, mindkét irányban.
- *Elvárt:* a víz követi a nézetet, nincs beszakadás, kettős villogó felület
  vagy szárazföldre kerülő víz. Megállás után a vízfelszín nem „ragad” régi állapotban.

### 15.3 Korábban commitolt tile/upload munka (`0f9cd4b`, `99b3ac4`)

**5. ND-69–74 — teljes fedés, közös élek, morph, nadír-diagnosztika, terrain proxy**

- *Mit csináltunk:*
  - A CPU-geometriához teljes CPU-klasszifikáció tartozik.
  - Base-tile alatt teljes partíció van, és csak sikeres publikálás után rejtődik el a durva terep.
  - A sarokgazda egyértelmű, a kockalap-élek is illeszkednek.
  - A morph a tényleges háromszög-átlón fut.
  - A metrika a Build-kori `TerrainLodProxy`-ból jön.
- *Állapot:* a felhasználó ND-70 után pozitívan jelezte a zoom/visszazoom
  működését. A későbbi ND-73 (korábbi split, rövidebb morph) és ND-74 (proxy)
  csak élő logban látszott, **kifejezett vizuális elfogadás nincs**.
- *Hogyan ellenőrizd:* L3.
  - [ ] Forgasd a bolygót egy pólusra és egy kockalap-élre, közepes és mély zoomnál.
  - [ ] Zoomolj ki-be ugyanazon a helyen.
- *Elvárt:* nincs tartós repedés, lebegő perem, hiányzó tile, és a split/merge
  pillanatában nincs feltűnő alakugrás.

**6. ND-75–81 — kirajzolt tile-mérés, progresszív kiválasztás, korlátos chunkok, munkacache**

- *Mit csináltunk:*
  - A PerfLog a ténylegesen feltöltött mesh-quadok pixelméretét méri (17×9 minta).
  - A kiválasztás kérésenként 1024 split hullámokban halad.
  - A tengerfenék korán kizáródik.
  - Egy chunk legfeljebb 256 levél.
  - Álló kameránál a metrikák cache-ből jönnek.
- *Állapot:* **élő log látta** (ND-80: upload p90 25→5 ms). A felhasználó a
  ND-81 után még az első zoomok beragadását jelezte; ezt az ND-96 célozta.
  Nincs vizuális elfogadás.
- *Hogyan ellenőrizd:* a 2. pont próbája ezt is lefedi. Külön lépés nem kell.
- *Elvárt:* ugyanaz, mint a 2. pontnál.

**7. ND-82/83 — önálló víz-LOD**

- *Mit csináltunk:* saját vízforrás és kiválasztás (max. 8192 levél),
  saját közös élek, két váltott víz-rendercél, változatlan kiválasztásnál nincs új upload.
- *Állapot:* **élő log látta** (`ND-83 water apply` sorok). A part és a víz vizuális elfogadása nincs.
- *Hogyan ellenőrizd:* a 4. pont próbája lefedi. Plusz:
  - [ ] Közeli zoom egy tó fölött.
- *Elvárt:* a tó felszíne finomodik, és nem lóg ki a partra.

**8. ND-85/86/87/89/93/94 — több frame-es staging, aux-upload, inaktív objektumok takarítása**

- *Mit csináltunk:*
  - A terep, víz és határvonal leválasztott mesh-ekbe több frame alatt töltődik fel, és egy külön lépésben vált át.
  - A maszkterv revízióvédett.
  - Inaktív objektumokból legfeljebb 128 marad, frame-enként max. 8 ürül.
  - 2 ms-os puha staging-keret van.
- *Állapot:* **élő log látta** (ND-86: `PerfLog_20260912_015131`; ND-93/94:
  `PerfLog_20260912_140221`, 1568 kiürített kulcs). FPS, memória és Play
  stop/start elfogadás nincs.
- *Hogyan ellenőrizd:* L5.
  - [ ] Járj be 5–10 egymástól távoli területet mély zoomban, majd állj meg 10 s-ra.
  - [ ] Hierarchy-ben számold az inaktív terep-GameObjecteket az elején és a végén.
  - [ ] Play Stop → Play újra.
  - [ ] Zoom közben kapcsold ki/be a határvonalakat.
- *Elvárt:*
  - Az inaktív objektumok száma egy átmeneti csúcs után visszaáll (~128 körülre).
  - Újraindítás után nincs hiányzó víz, beragadt maszk vagy új Console-hiba.
  - A határvonal a terepet követi.
  - Profilerben a frame-csúcsok ritkák. Egy-egy natív mesh-job túllépheti a 2 ms-ot, ez ismert.

**9. ND-92 — maszkfeltöltési hibafallback**

- *Mit csináltunk:* ha a GPU-indexfeltöltés részben elbukik, a teljes eredeti
  terep- és vízindexbuffer visszatöltődik, és a dinamikus réteg biztonságosan kikapcsol.
- *Állapot:* **semmi élő bizonyíték**. 4 Editor-eset csak fordított.
- *Hogyan ellenőrizd:* kézzel nem kényszeríthető ki. Elég az 1. pontban a
  `TerrainIndexMaskMeshTests` és a `ChunkUploadResourceTests` zöld futása.
- *Elvárt:* zöld tesztek. Normál használatban nincs látható hatása.

### 15.4 Kamera, lépték, relief

**10. ND-84 + ND-95 — fizikai km-léptékcsík**

- *Mit csináltunk:*
  - A csík két végpontja a modellfelszínt metszi, a távolság a
    `7 420 000 m` sugarú gömbön mért ív.
  - Az első élő próba után átmeneti hibánál megmarad az utolsó hiteles érték.
  - A második élő próba után közeli zoomnál gyökkeresés van.
  - ND-95 óta a kamera, a vetület és a világrevízió a kontextuskulcs.
- *Állapot:* két élő próba hibát talált, **a két javítás és az ND-95 élőben ellenőrizetlen**.
- *Hogyan ellenőrizd:* L4 utolsó három pontja.
  - [ ] Zoomolj távolról a minimumig, és közben mozogj oldalra.
  - [ ] Változtasd a Game view méretét és képarányát, majd a kamera FOV-ját.
  - [ ] Deep-time lépés után nézd meg újra.
- *Elvárt:*
  - A felirat közeli zoomnál sem tűnik el tartósan, és azonos nézetben nem villog.
  - Érvénytelen mérésnél `Lépték: —` látszik, nem egy másik helyhez tartozó régi szám.
  - A szám a vízszintes fizikai távolság, nem a 111× relief hegy-völgy hossza.

**11. ND-91 — helyi felszínkövető kamerakorlát**

- *Mit csináltunk:* zoom, forgatás, FlyTo és transzformáció a kamera alatti
  modellmagassághoz és a near clip-hez igazított minimális réssel (`followLocalSurface`, alapból be).
- *Állapot:* **semmi élő bizonyíték**. 3 Editor-eset csak fordított.
- *Hogyan ellenőrizd:* L4 első pontja.
  - [ ] Minimum zoom síkságon, hegycsúcson, meredek parton és tó fölött.
  - [ ] A minimumon forgasd a kamerát, hogy hegyoldal kerüljön a kép szélére.
- *Elvárt:* a kamera nem megy a felszín alá. A kép szélén vagy a near-plane
  mentén látható bevágást jelezd. Ez ismert korlát: pontmintás védelem, nem mesh-ütközés.

**12. ND-95 — FlyTo helyi magassággal, megszakítás, új időmérés**

- *Mit csináltunk:* a FlyTo az egész úton a helyi felszín feletti magasságot
  interpolálja, kézi beavatkozásra megszakad, és a PerfLog kérésidői monotón órából jönnek.
- *Állapot:* az időmérés **élő log látta** (`ND-95 request timing`). A FlyTo
  és a megszakítás **semmi élő bizonyíték**.
- *Hogyan ellenőrizd:*
  - [ ] Kattints egy síkvidéki kontinens/régió nevére, majd egy magas hegységére, végül vissza.
  - [ ] Egy FlyTo közben görgess vagy húzd a kamerát.
- *Elvárt:* az út közben sem süllyed a hegybe, nem a végén ugrik fel.
  Megszakítás után azonnal a tiéd az irányítás, nincs utólagos rántás.

**13. ND-88 — fizikai relief-skála kapcsoló**

- *Mit csináltunk:* `usePhysicalReliefScale`. Bekapcsolva
  `elevationScale = radius / 7 420 000` és relief = 1, azaz 1:1. Kiderült,
  hogy a régi beállítás valójában ~111× függőleges túlzás volt.
- *Állapot:* az átadott scene-ben **ki van kapcsolva**, relief = 111. Az 1:1
  mód és a kapcsoló viselkedése **élőben nincs elfogadva**.
- *Hogyan ellenőrizd:*
  - [ ] Inspectorban kapcsold be: a relief mező 1-re áll és nem szerkeszthető érdemben. Nézd meg a hegyeket.
  - [ ] Kapcsold ki: a relief újra szabadon állítható, és a korábbi értéked visszaállítható.
- *Elvárt:* a kapcsoló kiszámíthatóan viselkedik. Az 1:1 kép valószínűleg
  laposnak hat; ez **fizikailag helyes**, a látható hegységek kérdése külön M13-feladat (14. pont).

### 15.5 Core és UI — másik munkaszálból vagy korábbról

**14. ND-90 — folytonos óceáni/kontinentális kéregátmenet (seed-törő)**

- *Mit csináltunk:*
  - Eltérő kéregtípusú lemezek határán `0.005` széles smoothstep-sáv keveri
    a -4000 m-es és +800 m-es kéregbázist.
  - A tektonikai uplift plafonja 1500 → 1000 m.
  - A Python-referencia és az összes downstream vektor újragenerálva.
  - A `.worldpkg` formátum 2-es lett.
- *Állapot:* Core 384/384 zöld. **Semmi vizuális elfogadás**. Friss Python
  KAT-futás a jelenlegi gépen nem igazolt, mert nincs telepített Python.
- *Hogyan ellenőrizd:*
  - [ ] Menj vissza arra a lemezperemre, ahol korábban kb. 200 km-es falat láttál. Nézd meg 0, kb. 250 és 585 Myr-nél.
  - [ ] Nézd meg relief = 111 és 1:1 (13. pont) mellett is.
  - [ ] Ha van régi, 1-es verziójú `.worldpkg` fájlod, töltsd be a CLI-vel.
  - [ ] Opcionálisan, ha van Python: `python tools/reference/verify_kat.py`.
- *Elvárt:*
  - A perem nem függőleges fal, hanem átmenet. A hegységek nem tűnnek el.
  - A v1 csomagnál explicit hiba jön („…az 1-es világok az ND-90 kéregátmenet miatt numerikusan inkompatibilisek”).
  - A KAT hibátlan.
  - A „hegyek látszanak, a part alacsony” végső cél ettől még nem teljes; az M13 árnyalási munka külön van.

**15. Deep-time léptetőgombok (8 lépés × 2 irány)**

- *Mit csináltunk:* ±`1y, 10y, 100y, 1ky, 10ky, 1my, 10my, 100my` gombok,
  pozitívak a felső, negatívak az alsó sorban. 600 px széles panel, a Gyr
  mező 9 tizedest mutat.
- *Állapot:* az első, szűkebb változatra jött visszajelzés (szélesítés,
  köztes lépések). **A kibővített 600 px-es változat élőben nincs ellenőrizve.**
- *Hogyan ellenőrizd:*
  - [ ] Play módban nyomd meg mind a 16 gombot.
  - [ ] 0 Myr-en nyomd a `-1y`-t, a felső határon (alapból 1000 Myr) a `+100my`-t.
- *Elvárt:*
  - Egyik felirat sem csonkolódik, és a csúszka és a Gyr mező együtt frissül.
  - `+1y` után `0.000000001 Gyr` látszik.
  - A határokon nem megy 0 alá vagy a maximum fölé.

**16. Kamera- és panel-apróságok (átvételi review, 2026-09-10)**

- *Mit csináltunk:*
  - A „Pálya mentén” mód letiltott, „(hamarosan)” feliratú vezérlő lett.
  - Az IMGUI panel háttere a tényleges sorszámot követi.
  - A PerfLog adaptív ágban `biome-only adaptive preparation` címkét ír.
- *Állapot:* **semmi élő bizonyíték.**
- *Hogyan ellenőrizd:*
  - [ ] Kattints a „Pálya mentén (hamarosan)” vezérlőre.
  - [ ] Nézd meg a panel alját.
- *Elvárt:* a mód nem vált át, és a háttérdoboz minden sort, a kameragombokat is, lefed.

**17. Tengelyforgás mód — csillagmező világtérben, dőlés előjele**

- *Mit csináltunk:* AxialRotation módban a bolygó forog, a Planet alatti
  csillagmező a `StarField.KeepFixedInWorldSpace()` révén világtérben áll.
  A `SunController.planetGridMesh` a PlanetView scene-ben be van kötve.
- *Állapot:* **semmi élő elfogadás** a javítás után. Az `axialTilt` Unity-beli
  előjele csak levezetett (kt_1, ND-62 a kameraágon).
- *Hogyan ellenőrizd* (`06-user-verification-checklist.md` 14. pont):
  - [ ] `SunController` → Days Per Second = 0.1, Auto Advance be.
  - [ ] Play, váltás „Tengelyforgás” módra.
  - [ ] Egy jól felismerhető kontinenst kövess kb. 10 s-ig.
  - [ ] Ugyanannál a `currentTimeDays` értéknél hasonlítsd össze a „Szabad kamera” móddal, mely kontinensek vannak megvilágítva.
- *Elvárt:* a kontinens áthalad a terminátoron, a csillagok nem forognak
  vele, és mindkét módban ugyanazok a területek vannak megvilágítva. Ha
  tükrözött vagy eltolt, az valószínűleg a dőlés előjele: szólj.

**18. Exact deep-time gyorsítás (ND-63–68) — látvány változatlansága**

- *Mit csináltunk:*
  - Időfüggetlen terepbázis-cache.
  - Sűrű tömbös statikus rács és priority-flood.
  - A napi Nap-irányok Buildenként egyszer készülnek.
  - Előméretezett bucketek.
  - A numerikus eredmény bitre azonos (tesztekkel igazolva).
- *Állapot:* a felhasználó a **2,83 s-os meleg időt ideiglenesen elfogadta**.
  Nem ellenőrizte külön, hogy a kép pixelre ugyanaz-e. A hideg első Build ~13 s maradt.
- *Hogyan ellenőrizd:*
  - [ ] Két-három deep-time lépés oda-vissza ugyanarra az időre.
  - [ ] Kapcsold be a határvonalakat egy deep-time váltás után.
- *Elvárt:* ugyanarra az időre visszalépve a kontinensek, folyók, tavak és
  színek azonosak. A bekapcsolt határvonal megjelenik, nem marad üres.

### 15.6 Nem ebben a munkafában — külön ágon, csak commit után

**19. `experiment/full-temperature-model` — tengeri jég mélységfüggő átmenete (az ottani ND-62)**

- *Mit csináltunk (Claude, kt_1):* a fagyási küszöböt a helyi mélység modulálja,
  a szín sarkonként folytonosan keveredik jég és víz között.
- *Állapot:* **semmi élő bizonyíték.** Az ág nincs merge-elve, és az ND-62
  azonosítója ütközik a kameraágéval.
- *Hogyan ellenőrizd:* **csak a jelenlegi munkafa commitolása után** váltsd
  az ágat, különben a nem commitolt ND-90–98 munka veszélybe kerül. Play
  teljes újraindítással, mindkét pólus közelről és távolról.
- *Elvárt:* a tengeri jég határa nem szabályos kör, és a jég/víz átmenet folytonos.

**Régebbi, Claude-korszakból nyitott tételek** (folyóvonal-szakadás és
-szélesség „nok”, render budget 200000, morfológiai nevek, tavak/jég,
dinamikus tengerszint, eljegesedési ciklus): a
`docs/06-user-verification-checklist.md` 4., 5., 13. pontja és a „Régebbi,
még nyitott tételek” rész. Ezeket a Codex-munka nem célozta, részben felül is
írhatta őket (például az ND-78/82 víz- és tengerfenék-kezelés). Csak a fenti
csomag után érdemes újranézni.
