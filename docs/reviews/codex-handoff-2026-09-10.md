# Code review — `codex-handoff` (2026-09-10)

**Összehasonlítási alap:** `core-deferred-features...codex-handoff`

**Állapot:** CHANGES REQUESTED

A review kizárólag olvasási/ellenőrzési munka volt; az alkalmazáskódhoz és a
vizsgált dokumentációhoz nem nyúltam.

## Megállapítások

### [P1] A „Pálya mentén” mód kiválasztható, miközben semmit nem implementál

**Rendezve (2026-09-10):** a még nem implementált mód letiltott,
„Pálya mentén (hamarosan)” feliratú vezérlőként látszik, és nem állítható be.

**Hely:** `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs:392`,
`unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs:1426`,
`unity/WorldGenViewer/Assets/Scripts/Viewer/SunController.cs:119-153`

A publikus vezérlőfelület három egyenrangú módot kínál, és a felhasználó
aktiválhatja a „Pálya mentén” opciót. A `SunController` azonban csak az
`AxialRotation` értéket kezeli külön; az `OrbitalFollow` ugyanabba az `else`
ágba esik, mint a `Free`, tehát a kapcsoló látható állapotváltozást mutat,
funkcionális változás nélkül. Az Inspector-tooltip és a döntésnapló ugyan
leírja, hogy ez még nincs implementálva, de a futó UI nem jelzi ezt.

Ez félrevezető felhasználói szerződés és hibás tesztélményt ad: könnyű arra
következtetni, hogy a mód implementálva van, de rosszul működik. Amíg nincs
kész, a gomb legyen letiltva/„hamarosan” jelölve, vagy ne legyen választható.

### [P2] A kibővített IMGUI panel kilóg a saját háttérdobozából

**Rendezve (2026-09-10):** a háttérmagasság a tényleges tízsoros layoutot
követő közös konstansból számolódik.

**Hely:** `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs:1346`,
`unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs:1423-1430`

A háttérdoboz magassága változatlanul `rowH * 8 + 16`, miközben az új
kameravezérlők két további sort és két további `y += rowH` lépést adtak a
panelhez. Következésképp a kameragombok a doboz alsó részén kívül rajzolódnak,
és kisebb Game View esetén könnyebben ütközhetnek más UI-val vagy a képernyő
szélével. A doboz magasságát a tényleges sorszámhoz kell igazítani, ideális
esetben egyetlen közös layout-konstansból.

### [P2] A teljesítménynapló az optimalizálás után is „eldobott” munkát jelent

**Rendezve (2026-09-10):** adaptív módban a szakasz neve most
`biome-only adaptive preparation`; a `legacy geometry loop` név csak a valóban
legacy geometriaágon jelenik meg.

**Hely:** `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs:1719-1731`,
`unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs:1878-1880`

`useAdaptiveLod=true` esetén az új `continue` már kihagyja a legacy geometria
előállítását, de a PerfLog változatlanul hozzáfűzi az
`[ELDOBVA - useAdaptiveLod felulirja]` címkét. A mért szakaszban ekkor már a
szükséges biome-statisztikai ciklus és az üres legacy struktúrák kezelése van,
nem az a geometriai munka, amelynek eredményét eldobnánk. Ez a következő
teljesítményvizsgálatot félrevezetheti, különösen mert a handover kifejezetten
friss PerfLog alapján kéri megítélni a nyereséget. A címke és lehetőleg a
szakasz neve különítse el a „biome-only adaptive path” és a valódi legacy
geometria útját.

## Validációs hiányok és kockázatok

- A `SunController` Unity-függő koordinátatranszformációjára nincs automatizált
  teszt. A Core csillagászati tesztjei nem ellenőrzik a Unity quaternion-,
  tengelycsere- és world/local-space kompozíciót.
- A döntésnapló helyesen jelzi, hogy az axiális dőlés előjele és a szorzási
  sorrend élő ellenőrzést igényel. Review alapján ezt nem lehet késznek
  minősíteni.
- A szükséges `planetGridMesh` scene-hivatkozás a vizsgált
  `PlanetView.unity` fájlban ténylegesen be van kötve (`fileID: 283293246`),
  tehát a handover általános „be kell kötni” figyelmeztetése ezen a konkrét
  scene-en már nem aktuális. Más scene/prefab ettől még lehet érintett.
- Az optimalizálás valós nyereségét és a vizuális rétegek változatlanságát csak
  friss Unity PerfLog és élő összehasonlítás igazolja.

## Ellenőrzési bizonyíték

- `git diff --check core-deferred-features...HEAD`: tiszta.
- `dotnet build WorldGen.sln`: sikeres, 0 warning, 0 error.
- `WorldGen.Core.Tests`: 375/375 sikeres.
- `WorldGen.Viewer.LodChunking.Tests`: 14/14 sikeres.
- `WorldGen.Cli.Tests`: 7/7 sikeres.
- Python KAT nem futott: ezen a gépen nincs telepített Python interpreter.
- Unity Editor-kompiláció és élő vizuális/performance teszt nem történt.

**CI/solution utókövetés (2026-09-11):** a korábban kimaradó
`WorldGen.Viewer.LodChunking.Tests`, `WorldGen.Cli` és `WorldGen.Cli.Tests`
projektek bekerültek a gyökér `WorldGen.sln`-be. A CI meglévő solution-szintű
`dotnet build`/`dotnet test` lépése így mindhárom tesztassemblyt futtatja.
Ellenőrzés: teljes build 0 warning/0 error; Core 375/375, LOD 14/14, CLI 7/7.

## Összegző vélemény

A legacy-geometria kihagyása lokálisan ésszerű és kis kockázatú változtatásnak
tűnik. A tengelyforgás mód felépítése követhető, a world/local LOD-kezelés
figyelembe veszi a Planet transformját, és a scene-referencia is be van kötve.
Ettől függetlenül a változtatás élő Unity-validáció nélkül még nem elfogadható
késznek, a választatható, de no-op `OrbitalFollow` módot pedig release előtt
rendezni kell.

---

## Kódméret-, boilerplate- és teljesítmény-audit

### Rövid diagnózis

A fő szerkezeti probléma a viewerben koncentrálódik. A
`PlanetGridMesh.cs` **6208 soros**, miközben a következő legnagyobb viewer-fájl
1192 sor. Egyetlen MonoBehaviour egyszerre felel a build orchestrationért,
config-invalidationért, statikus és adaptív geometriáért, CPU/GPU
klasszifikációért, cache-ekért, minden render-rétegért, paneladatért, async
életciklusért, debug UI-ért és naplózásért. Ez már god object; a boilerplate
ennek tünete.

### Legjobb kódmennyiség-csökkentő absztrakciók

#### 1. `MeshBuffers` és `BucketedMeshBuffers<TKey>`

**Terület:** nagyjából `PlanetGridMesh.cs:2371-3000` és `3936-5160`.

Sok helyen ismétlődik a `vertices`/`normals`/`triangles`/`colors` négyes,
annak bucketenkénti dictionary-változata, több `AddQuad` overload, a
`GetOrAddLists`, konkatenálás és feltöltés. Egy típusos `MeshBuffers` saját
`AddQuad` és `ClearAndEnsureCapacity` műveletekkel várhatóan több száz sort
venne ki. A `BucketedMeshBuffers<TKey>` egyetlen lookupból adná vissza a négy
listát, négy párhuzamos dictionary helyett. Ez kódot és hash-lookupot is
csökkenthet. A forró útvonalon ne használjon LINQ-ot vagy delegate-et.

#### 2. Változtathatatlan `WorldBuildContext`

A build után sok külön `_adaptive*` mező őrzi ugyanannak a világnak a seedjeit,
mezőit, tengeri szintjét, hidrológiáját és renderküszöbeit. Javasolt egy
immutable `WorldBuildContext`, benne `SimulationSnapshot` és
`RenderSettingsSnapshot`. Minden builder ugyanazt a snapshotot kapná. Ez
csökkenti a mezőket/paramétereket, biztonságosabbá teszi a háttérszálas
feldolgozást, és lehetővé teszi a korrekt cache-kulcsokat.

#### 3. Értékobjektumok a config-snapshot boilerplate helyett

**Terület:** `PlanetGridMesh.cs:1114-1266`.

A `SnapshotWorldConfig`/`WorldConfigChangedSinceBuild` és river/cloud párjaik
manuális mezőmásolást és összehasonlítást ismételnek. Egy új Inspector-mezőt
könnyű csak az egyik oldalon felvenni. Három allokációmentes értéktípus
(`WorldConfigKey`, `RiverRenderConfigKey`, `CloudRenderConfigKey`) explicit
`Equals` implementációval körülbelül száz sor boilerplate-et és egy tipikus
stale-rebuild hibaforrást szüntetne meg.

#### 4. Rétegépítők leválasztása

Javasolt felelősségi határok:

```text
PlanetGridMesh              Unity lifecycle és orchestration
PlanetBuildPipeline         dependency graph és invalidation
TerrainMeshBuilder          statikus/adaptív terrain + corner cache
WaterMeshBuilder            ocean/lake surface
HydrologyRenderBuilder      river ribbon + lake render
CloudMeshBuilder            cloud számítás és mesh
SurfaceColorEvaluator       szín- és overlay-döntések
PlanetPanelProjector        WorldGenPanelData előállítása
MeshTargetPool              GameObject/Mesh/Material újrahasználat
```

Nem minden kis metódushoz kell osztály. A határ ott indokolt, ahol eltér az
invalidation oka, az adatfüggőség vagy a végrehajtás helye (main thread,
worker, GPU).

### Legnagyobb valószínű teljesítménynyereségek

#### 1. Sűrű fix-LOD adatokhoz tömb, ne `Dictionary<TileId,T>`

A level 8 rács pontosan `6 * 256 * 256 = 393216` elemes és teljesen sűrű.
Itt a dictionary hashinget, bucketmemóriát és rosszabb cache-lokalitást ad.
A build belső reprezentációja lehet `T[]` vagy hat lapos `T[][]`, determinista
`face/u/v -> index` leképezéssel, miközben a publikus API maradhat `TileId`.
Ez egyszerre gyorsíthatja a `BuildStaticBaseLayer`-t és a hidrológiát, valamint
csökkentheti a memóriát/GC-t. Konkrét nyereséget csak prototípusmérés után
szabad állítani.

#### 2. Függőségi gráfos részleges rebuild

A <1 s cél puszta mikrooptimalizálással valószínűtlen. A teljes `Build()` az
átadás szerint kb. 24 s, ebből ~72% statikus base mesh és ~18% hidrológia.
A tartós megoldás explicit adatfüggőség és rétegenkénti invalidation:

```text
seed/plates -> elevation -> sea level -> climate -> hydrology -> render layers
                         \-> panel metrics
```

UI-, kamera-, overlay- és material-változás ne indítson szimulációt.
Deep-time változásnál külön kell meghatározni, mi számolandó újra, mi
cache-elhető és mi interpolálható. A jelenlegi bool/snapshot rendszer további
kézi bővítése rosszul skálázódik.

#### 3. Statikus base mesh chunkolt és adagolt frissítése

A `BuildStaticBaseLayer()` minden alkalommal 393216 tile-t emitál. A dinamikus
LOD-nál már van chunking/incrementális infrastruktúra; ennek mintájára a base
layer is épülhet worker-chunkokban, újrahasznált bufferekkel és frame-ek között
adagolt feltöltéssel. Ez nem feltétlenül csökkenti önmagában az összidőt <1 s
alá, de megszüntetheti a hosszú blokkolást és előkészíti a dirty-region
frissítést.

#### 4. Közös tile-számítások egyszeri előállítása

Elevation, ocean flag, temperature, biome, kategória, sarokpozíció és szín
több renderútvonalon újra előáll. Egy kompakt `TileRenderSample` vagy SoA cache
csak a profilerben drágának bizonyult értékeket tárolja. Külön mérendő a zaj-,
hőmérséklet-, klasszifikáció-, buffer-append és mesh-upload költség. Vakon
mindent cache-elni memória szempontból rosszabb lehet.

#### 5. Buffer- és mesh-életciklus központosítása

A meglévő Mesh-újrahasználat jó irány, de a listák/dictionaryk/tömbök kezelése
széttagolt. Egységes `MeshTargetPool` és újrahasználható `MeshBuffers` mellett
központilag kezelhető a `Capacity`, `Clear()`, material-cache és indokolt
worker-tömböknél az `ArrayPool<T>`. Minden lépés előtt/után GC.Alloc és idő
mérés kell.

### Amit nem érdemes túlabsztrahálni

- A Core numerikus algoritmusai maradjanak explicit és auditálhatók; ne
  kerüljenek általános reflection/delegate „pipeline step” mögé.
- A `TileId`, random domain/property és determinisztikus rendezés maradjon
  látható.
- Ne kerüljön LINQ a tile- és vertex-ciklusokba csak a rövidebb kódért.
- Ne cache-eljünk mérés nélkül mindent.
- A 6208 sor puszta `partial class` fájlokra bontása csak navigációt javít;
  kódot, függőséget és futásidőt nem csökkent. Átmeneti lépésnek elfogadható,
  végső architektúrának nem.

### Javasolt optimalizálási sorrend

1. Unity Profiler markerek a `BuildStaticBaseLayer` belsejébe: klasszifikáció,
   corner/elevation, szín, buffer-append, mesh upload.
2. Sűrű tömbös prototípus egyetlen fix-level mezőre, azonos output
   összevetésével.
3. `MeshBuffers` absztrakció és buffer-reuse; előtte/utána GC.Alloc és idő.
4. Config key + `WorldBuildContext`, invalidation regressziós tesztekkel.
5. Rétegépítők fokozatos kivonása a `PlanetGridMesh`-ből.
6. Dependency graph és részleges/chunkolt base rebuild.

Nagy egyszeri rewrite nem javasolt. Minden kivonás előtt és után azonos
seedből azonos mesh-adatot, paneladatot és képet kell ellenőrizni. Az első
három lépés mérhető sebességnyereséget adhat; a későbbiek tartósan csökkentik
a kódmennyiséget és a változtatási kockázatot.
