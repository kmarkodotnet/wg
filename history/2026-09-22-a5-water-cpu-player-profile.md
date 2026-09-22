# 2026-09-22 — A5: vízfázis, CPU-idő és Player-kontroll

Előzmény: [első allokációs javítás és kontroll](2026-09-22-deep-time-allocation-profile.md).
Kiindulás `main`, `07318cd`; az előző A5 diffje megőrizve. Core-/seed-
változás nincs. A mérés célja a még nyitott vízfázis-variancia elkülönítése.

## Editor: a kiugrás nem a mesh-feltöltésben van

ND-133 kiegészítés: a korábbi vízmarker hét egymást követő részre bomlik:
setup, assemble, upload, diagnostics, layout, mask, source. A meglévő
összesített PerfLog-vízidő megmarad. A diagnosztikai nyilvántartás csak
referenciákat jegyez fel, nem másolja a nagy listákat (56 bájt/minta).
A jelentős másolatok a layout- és indexmaszk-építésben vannak.

Első részletes profil: `artifacts/a5-water-detail.json`, 2 bemelegítés +
8 minta. Az upload 24–27 ms körül marad, a managed allokációt végző
szakaszok több tíz/száz ms-ra nőnek. A vízmaszk 13 873 904 bájt,
összefűzés 47 154 336, layout 12 205 440, LOD-forrás 10 937 691 bájt/Build;
az összes vízallokáció az ND-134 utáni 84 171 555 bájt marad.

A Windows-specifikus, opcionális `GetThreadTimes` mérés a kernel+user
CPU-időt adja; a fázislog új `threadCpuMs` mezője. Más platformon vagy
hibánál -1, feldolgozva null. A rövid szakaszoknál a 15,625 ms körüli
kvantálás miatt nulla és a falióraidőnél nagyobb érték is előfordul;
nem szabad egyetlen rövid mintából CPU-kihasználtságot számolni.

`artifacts/a5-water-cpu-v2.json`: 2 bemelegítés + 8 sikeres minta.
Az alábbiak a bemelegítés nélküli nyolc fázislog statisztikái:

| Vízfázis | Falióra min–max (ms) | Átlag (ms) | Főszál CPU-átlag (ms) |
|---|---:|---:|---:|
| Összefűzés | 8,88–315,59 | 57,09 | 56,64 |
| Feltöltés | 23,16–26,46 | 24,95 | 27,34 |
| Layout | 3,97–82,29 | 19,99 | 17,58 |
| Maszk | 3,02–95,02 | 25,71 | 25,39 |
| LOD-forrás | 14,66–92,53 | 33,93 | 33,20 |

Példa: mask 91,693 ms falióra / 93,750 ms CPU. Következtetés: a lassú
szakaszok főszálú CPU-munkát végeznek, nem elsősorban a szál futásra
várakozása nyújtja meg őket. Ez még nem azonosítja a Mono allokátor/GC
belső rutinját; a látható GC.Collect minták csak néhány ms-osak.

A CPU-profil első próbája név nélküli natív Profiler-mintán elbukott;
`artifacts/a5-water-cpu.json` ezért **érvénytelen sorozat**, a runner
helyreállította a kapcsolókat. A javított változat kihagyja a név nélküli
GC-eseményt, új kimeneti fájlba futott. A közös PerfLogban az érvénytelen
próba egy befejezett Buildje megmaradt: a v2 nyolc méréséhez a meleg
profilos sorok közül összesen hármat kell kihagyni (1 próba + 2 bemelegítés).
Feldolgozás: `artifacts/a5-water-cpu-selected.json`.

## Worker-hívásláncok

A runner már szálanként összesít, és külön menetben a nagy worker-
allokációk hívásláncát is menti. A CPU-sorozat nyolc mért Build-időablakában
**112,9–131,1 MB** worker-allokáció látszott. Ez időbeli átfedés, nem
automatikusan a statikus Build munkájának része.

Egy külön hívásláncos minta (`artifacts/a5-workers-stacks.json`) nagy,
legalább 1 MiB-os allokációinak tulajdonosai:

| Legfelső projektbeli hívás | Minták | Bájt összesen |
|---|---:|---:|
| LodGeometryCache.Evaluate | 9 | 69 101 248 |
| LodTerrainEvaluationCache.EvaluateTerrain | 5 | 15 200 360 |
| LodSelectionTrace.Record | 2 | 4 471 424 |
| AdaptiveQuadTree.HeapPush | 2 | 2 097 216 |

A láncok a terep- és víz-LOD kiválasztásához vezetnek. A teljes Build
még a tófelszín elkészítése előtt elindítja az új adaptív cutot, tehát
valós átfedés lehetséges; ezekből a bájtokból nem következik, hogy a
korábban lefutó statikus vízfázis CPU-kiugrását a LOD-worker okozta.
Nincs bizonyíték alapján el nem döntött LOD-cache-átalakítás.

## Player-próba protokollja (ND-135)

`DeepTimePlayerProbe` csak Development Playerben fordul és csak explicit
`-a5-output` kapcsolóval indul. Az eredeti PlanetView scene külön buildje
`artifacts/a5-player/WorldGenA5.exe`; nincs diagnosztikai scene-módosítás.
A runtime kapcsolók végül visszaállnak, a mérés végén a Player kilép.

Normál kontroll: 2 bemelegítés + 10 mérés, `-a5-warmups 2 -a5-runs 10`.
Fázisméréshez `-a5-phases`. Külön GC-kontrollhoz legfeljebb 3 teljes Build,
`-a5-warmups 0 -a5-runs 3 -a5-phases -a5-no-gc`: a GC csak a hívás
idejére áll Disabled módba, minden hívás `finally` ága visszaállítja.
4 GiB feletti induló managed heapnél a próba nem folytatható. Nincs
explicit gyűjtés és nincs javaslat a termék GC-jének kikapcsolására.

Források: [Windows CPU-idő](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadtimes),
[Unity GCMode Editor-korlát](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Scripting.GarbageCollector.GCMode.html),
[Profiler közös monotón időalap](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.RawFrameDataView.GetSampleStartTimeMs.html).

## Player-eredmények

A build sikeres, 0 hiba, 128 figyelmeztetés; az első HDRP shaderfordítás
kb. 395 s. A mérések már a befejezett shaderfordítás után, külön Player-
folyamatokban futottak, az Editor Edit módban maradt. Minden Player
magától kilépett. Rejtett ablakos D3D12 futás RTX 5070 Ti-n, 1297×760;
ez Build-/memóriamérés, nem FPS-, prezentációs vagy vizuális acceptance.
Az Editor- és Player-riport valamennyi világparamétere, induló kamerája
és viewportja egyezik. Az induló scene kamera-simítását külön nem mértük;
a tízes kontrollból két bemelegítő Build kimaradt.

Példa indítás (minden menethez új output/log név):

```powershell
Start-Process -FilePath F:/Claude/wg/artifacts/a5-player/WorldGenA5.exe `
  -WindowStyle Hidden -ArgumentList '-screen-fullscreen 0 -screen-width 1297 -screen-height 760 -logFile F:/Claude/wg/artifacts/a5-player-control.log -a5-output F:/Claude/wg/artifacts/a5-player-control.json -a5-warmups 2 -a5-runs 10'
```

Az első hideg scene-Build nem része a runnernek. A 2 bemelegítés utáni
10 kontroll (`a5-player-control.json`, `a5-player-control-selected.json`):

| PerfLog-idő (ms) | Átlag | Szórás | Minimum–maximum |
|---|---:|---:|---:|
| Teljes Build | 2037,56 | 32,76 | 2007,9–2105,3 |
| Statikus víz | 67,95 | 2,68 | 62,1–71,0 |

A korábbi Editor-kontroll 2150 ± 74 ms volt, de ebből nem állítunk általános
Player-gyorsulási tényezőt. Ebben a Player-menetben az Editor 300 ms körüli
vízcsúcsai nem jelentkeztek. A főszál allokációszámlálója Playerben is
nem támogatott maradt; az allokációs bájtokat továbbra is a korábbi valódi
Profiler `GC.Alloc` metaadatok bizonyítják.

Külön rövid, 3–3 fázismérős Build, normál → GC-disabled → normál sorrendben:

| Mód | Build-átlag (ms) | Vízátlag (ms) | GC.CollectionCount(0) delta / Build |
|---|---:|---:|---|
| Normál (`a5-player-phases.json`) | 2011,77 | 67,20 | 32 / 31 / 34 |
| GC-disabled (`a5-player-no-gc.json`) | 1963,00 | 62,03 | 0 / 0 / 0 |
| Normál ismétlés (`a5-player-phases-repeat.json`) | 2010,07 | 66,87 | 32 / 31 / 32 |

A rövid sorozatban a GC nélküli Build átlagosan kb. **2,4%-kal** rövidebb;
három minta és a normál ismétlés 53,93 ms szórása mellett ez nem erős
sebességígéret. A használt managed heap a GC-disabled hívások után
**932 966 400 / 1 136 812 032 / 1 076 318 208 bájt** lett. A következő
Build előtt már helyreállított GC ismét dolgozhatott. A visszakapcsolás és
a frame-ek közti gyűjtés nem része a mért Buildnek: a munka egy része
csak a mérési ablakon kívülre tolódik. **Termékbeli GC-kikapcsolás nincs.**

## Memória és megmaradt korlát

A tíz normál kontroll után a `Profiler.GetTotalAllocatedMemoryLong()`
**328 272 160–328 300 344 bájt** között volt (28 184 bájt különbség).
Ugyanakkor a `GetMonoUsedSizeLong()` 346–518 MB között változott.
Ezek külön számlálók: a Unity által nyilvántartott állomány és a managed
heap nem összeadható/egymásból kivonható allokációs forgalom.

A stabil követett állomány és a stabil natív mesh-upload idő csökkenti
annak valószínűségét, hogy a most látott Editor-csúcs egy tartósan növekvő
natív mesh-tároló problémája. Ez következtetés, nem hosszú memóriaszivárgás-
teszt. A natív allokáció/felszabadítás **eseményenkénti** byte-profilja és az
Editor managed allokátor/GC belső CPU-rutinjának mintavételes azonosítása
még hiányzik. A profil nem bizonyít kizárólagos GC-okozatot az Editorban.

## Ellenőrzés és állapot

- Solution build: 0 hiba / 0 warning; viewer-LOD: **490/490 zöld**.
- A Development Player feltételes forrása külön offline kapun is lefordult.
  Ehhez a meglévő Unity JSONSerializeModule bekerült a kapu referenciái közé.
- Élő Editor-/Player-fordítás sikeres, Console ground truth 0 hiba.
  A build meglévő figyelmeztetéseket is naplózott; nem állítunk warningmentességet.
- Logelemző: sikeres regressziós próba, külön teszt a mért nulla,
  a hiányzó és a nem támogatott CPU-idő kezelésére.
- A build automatikusan átírta a HDRP runtime-settings listát és a
  QualitySettings szerializálását; kizárólag ezeket az igazolt build-diffeket
  visszaállítottuk. Scene és projektbeállítás nem maradt módosítva.
- Az Editor Edit módban, Profiler kikapcsolva; a négy saját Player kilépett.
  Commit/push nem készült, a korábbi ND-134 javítást megőriztük.

**A5 tartalmilag súlyozott készültsége kb. 85%.** A fázisok elkülönítése,
főszálú CPU, worker-allokációs tulajdonosok és Player-/GC-kontroll készen van;
a fenti két részletes belső memória-/CPU-vizsgálat maradt. Ez nem a teljes
M9/M10 újra-auditált százaléka. Durva összes ráfordítás **4–6 munkaóra**,
hátralévő **1–3 munkaóra**, nem mért időnapló.

## Záró időváltásos ellenőrzés és a feladat határa

A felhasználó kérte az A5 céljának és alfeladatainak közérthető tisztázását.
A korábbi 85% túl tág feladathatárt tükrözött: az Editor futtatókörnyezete
belső működésének teljes felderítése nem szükséges egy használható
variancia-/allokációprofilhoz. Az ND-133 zárási kapuja ezért pontosítva:
ismételhető mérés, igazolt pazarlás javítása, külön Editor/Player-kontroll,
majd változó világidős teljesítmény-, memória- és tartalmi ellenőrzés.
Az opcionális belső GC-/natív eseményprofil nincs elvégzettnek nyilvánítva.

A külső Editor-runner opcionális `deepTimesMyr` sorozatot kapott. A
bemelegítést a kiinduló időn végzi, majd a sorozatot ciklikusan ismétli;
utolsó eleme kötelezően az eredeti világidő. Minden Build után külön rögzíti
a világidőt, kamera-pózt és az abszolút Unity-/Mono-memóriaszámlálókat.
A három időmezőt együtt állítja, így az Inspector szinkronja nem indít
észrevétlen további Buildet. A hash külön, Profiler nélküli menetben készül.
Hiba esetén a menet sikertelen; a saját Play-menet leállítása állítja
vissza a scene futás előtti állapotát. Scene-mentés nem történik.

**Teljesítmény:** `artifacts/a5-time-cycles.json`, 2 bemelegítés + 20 minta,
`[0, 100, 500, 100, 0]` Myr négyszer; profil és hash nélkül. Ugyanaz a
kamera-póz mind a húsz mintában. A runner a teljes `Build()` hívást méri,
nem kizárólag a PerfLog belső időablakát; a háttérfolyók befejezése nem
része a szinkron hívásnak.

| Világidő (Myr) | Minták | Átlag (ms) | Minimum–maximum (ms) | Unity-állomány min–max eltérése |
|---|---:|---:|---:|---:|
| 0 | 8 | 2234,73 | 2104,49–2335,29 | 115 098 bájt |
| 100 | 8 | 1970,27 | 1873,18–2048,42 | 115 626 bájt |
| 500 | 4 | 2337,09 | 2302,34–2373,79 | 59 872 bájt |

A 0 Myr első/utolsó mért Buildje 2295,68 / 2283,33 ms; a követett
Unity-memória különbsége +46 352 bájt, és a sorozat nem monoton növekvő.
Az abszolút Editor-számláló 1,606–1,626 GB; ez az egész Editor állománya,
nem a világ önálló mérete, ezért nem közvetlen Player-összehasonlítás.
A Mono használt állománya 1,128–1,403 GB között ingadozik. Ezek nem az
újonnan lefoglalt összes bájtok, nem adhatók össze. A rövid próba nem mutat
jelentős felhalmozódást vagy ismétlésszám szerinti lassulást; órákon át
tartó szivárgásmentességet nem bizonyít.

**Tartalmi kapu:** `artifacts/a5-time-geometry.json`, hat külön Build,
`[0, 100, 500, 100, 500, 0]` Myr. Mindhárom időponthoz két minta tartozik,
mindháromban a terrain/WaterSurface/LakeSurface SHA-256 értékei páronként
azonosak (9/9 egyezés). A három különböző időpont terephash-e különböző,
tehát a teszt valóban eltérő világállapotokat épített. A visszaállított
0 Myr hash-ei az ND-134 előtti és utáni korábbi ellenőrzéssel is egyeznek.
A hash a vertex/normal/color, submesh-index és bounds adatokat foglalja
magába; nem shader-/képernyőkép-ellenőrzés, és nem a háttérfolyók tesztje.

A két helyi, nem verziózott eredmény SHA-256 azonosítója:

```text
a5-time-cycles.json:   D3AB4E90E6957935AE897F6127CB49EE82E8D0A46CFB21B9EFE38E2BAF9B2904
a5-time-geometry.json: 7549D25E7226F508FF655C71EADB280B4640855CCDE2A2CA5FCB9EC62F5D84C5
```

Mindkét jelentés `complete`, hibamezőjük üres. Közös részletes napló:
`unity/WorldGenViewer/Logs/PerfLog_20260922_184217.txt`. A hash-menet
memóriaadatait és a megelőző scene-Buildet nem keverjük a húsz kontrollba.
A kibővített runner élő Unity-fordítása sikeres; a logelemző regressziós
próbája ismét sikeres. A korábban rögzített solution-/LOD-kapuk óta ebben
a zárókörben termékkód nem változott. Console ground truth a teljes végén
0 hiba, 0 warning. A Profiler és a háttérfutás visszaállítva kikapcsoltra,
a saját Play-menet leállítva; scene és projektbeállítás nem lett mentve.
Új Player nem indult, a zárókörnek nem kellett hálózati engedély.

**A5 lezárva, tartalmilag súlyozott készültség 100% a pontosított
profilozási hatókörben.** Nem a teljes M9/M10 százaléka, és **nem <1 s**
újraépítés. Durva összes ráfordítás **5–7 munkaóra**, ebben a feladatban
hátralévő **0 óra**; nem mért időnapló. Az Editor belső allokátor/GC pontos
magyarázata, natív eseményszintű byte-profil és hosszú terheléses teszt
nincs igazolva. Ezek szükség esetén külön feladatként vehetők elő.

Felhasználói Play-ellenőrzés nem blokkolta a profilozás lezárását.
Opcionális vizuális próba: előre/hátra időváltás, majd ugyanarra az időre
visszatérés; hiányzó terep/víz/tó, piros Console-hiba vagy fokozatos
lassulás megfigyelése. Az aszinkron folyók elkészülését külön meg kell
várni. Élő képi átvételt a hash-kapu nem helyettesít.
