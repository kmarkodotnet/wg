# 2026-09-22 — A4 lezárás és A5 variancia-/allokációprofil

## Hatókör

A felhasználó kérésére a `todo2.md` A4 tétele lezárva. Ez az elért eredmény
elfogadása, nem a címben szereplő <1 s küszöb teljesítésének állítása.
A5 önálló diagnosztikai tételként folyamatban (ND-133). A kiinduló munkafa
már tartalmazta az ND-132 Core/viewer/teszt/dokumentáció változásait; ezeket
megőriztük. Branch: `main`, HEAD `3f0ad76`. Commit/push nincs.

## Friss baseline, nem a régi öt mérés

Forrás: `unity/WorldGenViewer/Logs/PerfLog_20260922_163455.txt`.
13 teljes Build: egy hideg (memóriabeli bázis nem újrahasznált, lemez-cache
lehetséges), 12 meleg. A hideg 3256,5 ms. A 27 s-os ND-132 regresszió ebben
a menetben nem ismétlődik: a folyóindítások a teljes Build után szerepelnek.

| Fázis | Minimum–maximum (ms) | Átlag (ms) | Populációs szórás (ms) |
|---|---:|---:|---:|
| Teljes Build | 2250,6–2634,6 | 2479,4 | 121,2 |
| Statikus alapréteg | 1111,1–1570,5 | 1375,4 | 115,1 |
| Klasszifikáció | 460,2–556,3 | 487,9 | 25,1 |
| Bucket-előkészítés | 18,7–315,2 | 85,1 | 101,8 |
| Emit | 247,3–276,9 | 261,8 | 9,7 |
| Terrain mesh összesen | 159,1–580,1 | 350,5 | 139,0 |
| Statikus víz | 52,0–110,5 | 83,6 | 13,4 |
| Hidrológia | 271,8–486,9 | 350,0 | 83,7 |
| Folyómesh + tófelszín | 192,8–616,9 | 353,8 | 118,5 |

A teljes Build mediánja 2528,0 ms. A különböző deep-time állapotokban a
tó- és jégmennyiség is változik: ez a minta **nem tiszta futtatózaj-mérés**.
A részidők szórásait nem lehet összeadni. A mesh-fázis eddig együtt mérte
az összefűzést, a feltöltést és az indexmaszkot; önmagában nem GPU-idő.

## Elkészült mérőeszközök

`tools/diagnostics/analyze-deep-time.ps1`: csak teljes `Build()` párokat
értékel; hideg/meleg/azonosítatlan csoport, részidők, medián, átlag,
populációs szórás, nearest-rank p95. Kizárja az önálló overlay-futást és
a félbemaradt Buildet, hiányzó részidőből nem csinál nullát. JSON-kimenete
tartalmazza a forrás SHA-256 értékét és a nyers Build-mintákat.

```powershell
./tools/diagnostics/analyze-deep-time.ps1 `
  -LogPath unity/WorldGenViewer/Logs/PerfLog_20260922_163455.txt `
  -OutputPath artifacts/deep-time-baseline.json
./tools/diagnostics/test-deep-time-analysis.ps1
```

`PlanetGridMesh.profileDeepTimeAllocations`: alapból kikapcsolt Inspector-
kapcsoló. Bekapcsolva 14 statikus fázisnak saját `WorldGen.StaticBase.*`
Profiler-mintája és `[A5 static phase]` PerfLog-sora van. Az összefűzés,
mesh-upload és maszk külön mérhető. A mért fázisok alatt nincs új log-I/O;
a diagnosztika előre foglalt mintatömbbe ír, utána naplóz. Kivételnél is
lezárja a Profiler-mintát, a fejléc `complete=False` állapotot jelez.

**Mért adatok jelentése:**

- `mainThreadBytes`: csak a hívó főszál kumulatív managed allokációja.
  A számlálót ismert 1024 bájtos allokációval ellenőrizzük a fázisokon kívül.
  Nem működő számlálónál `allocationCounterSupported=False`, érték `-1`,
  a JSON-ban `null`. Nem nulla allokáció!
- `gc0/1/2`: folyamatszintű gyűjtésszámláló-különbségek; a Mono esetében
  együtt is léphetnek, nem három független GC-esemény.
- `heapDeltaBytes`: a teljes managed heap nettó változása, negatív is lehet;
  nem az allokált bájtok összege, és háttérmunkát is tartalmazhat.
- A worker-, natív/GPU allokáció és a GC-szünet hossza külön Profiler-
  mérést igényel. Nincs kényszerített GC vagy seed-/modellváltozás.

## Első élő ellenőrzés és korlát

Az Editor kezdetben Edit módban volt. Rövid saját Play-menetben azonos
seed, t=0 és L8 mellett készült kontroll, majd profilos Build. A fókuszon
kívüli futás kezdetben tiltott volt, emiatt több kérés a LOD mögött várt és
összeolvadt. **A kiküldött parancsok száma nem a mérések száma.** A mérés
idejére a háttérfutást engedélyeztük, végül visszaállítottuk.

Az első prototípus logja: `PerfLog_20260922_164835.txt`: kontroll 2226,5 ms,
profilos Build 2779,5 ms. Ez egyetlen páros próba, nem overhead-becslés.
A 361,645 ms bucket-előkészítés alatt a GC-számlálók nőttek, a nettó heap
446 623 744 bájttal csökkent. A mesh-összeállításnál és a vízfázisnál is
volt gyűjtés. **GC-egybeesés igazolt, a szünetidő és kizárólagos okozat nem.**

A prototípus minden fázisban nulla főszálú allokációt kapott az API-tól,
a biztos tömballokációknál is. Emiatt készült a fenti támogatottság-próba;
ennek a prototípuslognak a `mainThreadBytes=0` értékei érvénytelenek.

**Végleges műszerezés élő ellenőrzése:** `PerfLog_20260922_165242.txt`:
hideg 3132,6 ms, egy meleg profilos Build 2234,7 ms; 14 teljes fázisminta,
`allocationCounterSupported=False`, minden bájtérték `-1`, a feldolgozott
JSON-ban `null`. A támogatottság-próba tehát élőben is működik.
A bucket itt 19,526 ms, gyűjtésszámláló-növekedés nélkül; a mesh-maszk
262,208 ms szintén számlálónövekedés nélkül. Emiatt a teljes varianciát
továbbra sem indokolt kizárólag a GC-nek tulajdonítani. A maszkon belüli
CPU-munka, esetleges inkrementális GC és ütemezés Timeline-vizsgálatra vár.
A mesh-összeállítás nettó heap-növekedése 204 120 064 bájt, a bucketé
121 769 984 bájt; ezek folyamatszintű nettó értékek, nem allokációmérések.

Az Editor visszaállt Edit módba, `Application.runInBackground` az eredeti
`false` értékre; scene-t nem mentettünk. A mérőkód alapból kikapcsolt marad.

## Következő mérési kapu

1. Rögzített seed, idő, LOD, kamera, felbontás, overlay/rétegek és azonos
   háttérfutás. Két bemelegítés után legalább 10 ténylegesen befejezett
   kontroll-Build; a LOD mögé halasztott kérést ne számoljuk új mintának.
2. Ugyanezen állapot mellett kapcsolt diagnosztika és Unity CPU Profiler
   Timeline, Deep Profile nélkül. `GC.Alloc` call stack külön rövid menetben,
   mert maga is torzíthat. Editor-/JIT-hatást Player-kontrollal lehet leválasztani.
3. A bucket és mesh-összeállítás allokációs forrásait és GC-szünetét a
   jelölt tartományokban vizsgálni; a worker- és natív memória külön adat.
4. A bizonyított domináns allokációra kis javítás, azonos munkaterhelésű
   előtte/utána mérés. A5 csak ezután zárható; most nincs találgatáson alapuló
   buffer-/cache-átalakítás.

## Részfeladat-készültség (durva becslés)

Az A5 M9/M10-diagnosztikai részfeladata kb. **40%**: baseline és eszközök
elkészültek, első élő GC-megfigyelés van; a kontrollált sorozat, worker/native
profil és az ok elkülönítése hátra. Durva ráfordítás **1–2 munkaóra**,
hátralévő **2–4 munkaóra**, nem mért időnapló. Ez nem a teljes M9/M10 vagy
a projekt újra-auditált százaléka.

## Ellenőrzések

- Logelemző regressziós próbája: zöld (Build-határok, overlay/befejezetlen
  kizárás, cache-csoport, tizedesvessző, ismert statisztika, hiányzó fázis,
  negatív nettó heap és nem támogatott számláló).
- `dotnet build WorldGen.sln --no-restore`: 0 hiba, 0 figyelmeztetés.
- Viewer offline kapu: 0 hiba; 122 meglévő nullable-annotációs figyelmeztetés.
- Élő Unity újrafordítás: `completed`, `compilationFailed=false`,
  Console ground truth: 0 hiba. Két rövid Play-menetben fázismérés történt;
  vizuális acceptance-t ez a feladat nem végzett.
- Python `verify_kat.py`: 9/9. Numerikus algoritmus/vektor nem változott.
- Teljes solution tesztfutás: **1503/1503 zöld** (Core 567, viewer-LOD 490,
  CLI 8, app Foundation 438). A Core-futás kb. 7 perc; ez a meglévő,
  ND-132 módosításait is tartalmazó munkafát ellenőrzi.
- `git diff --check`: tiszta; nincs új scene-/asset-módosítás.

Baseline SHA-256: `196EC2DC4A48391AA10F28DAA83785828C08B8C7E783535ED9E25AD7434E1EAB`.

**A5 folytatás, mérési terv (2026-09-22):** a következő mérés a Unity
`RawFrameDataView` valódi `GC.Alloc` metaadatait használja, nem a nem működő
Mono-számlálót. Két bemelegítés után tíz azonos konfigurációjú kontroll-
Build; külön rövid Profiler-menet, Deep Profile nélkül. A runner csak üres
LOD-feladatsornál indít, `Built` eseménnyel igazolja a tényleges Buildet,
fagyasztja a napi időléptetést, és végül visszaállítja a kapcsolókat.
A teljes Build és a statikus fázisok main-thread GC-bájtjai elkülönülnek;
a worker-szálak allokációja időablakos megfigyelés, nem automatikus
hozzárendelés a Buildhez. A Profiler-menet idejét nem keverjük a kontrollal.
A script a `tools/diagnostics/` alatt él, nem importálunk diagnosztikai
komponenst a termék scene-jébe. Optimalizálás csak a mért eredmény után.

## A5 folytatás — kontrollált profil és első javítás (ND-134)

Az új munkamenet ellenőrzött alapja `main`, `07318cd` (`deep time fixes`),
amely már tartalmazza a fenti első lépést. Ebben a folytatásban nem készült
commit vagy push, Core-/numerikus módosítás sem történt.

### Terhelés és módszer

Unity Editor, Mono, inkrementális GC bekapcsolva. Azonos seed
`184482873278464`, deep-time **t=0**, statikus L8/hidrológia L8, napi t=10,
level=5, 1297×760 viewport, bolygónézet; folyók és tó/jég bekapcsolva,
felhő/határvonal/overlay kikapcsolva. A háttérben a meglévő termikus és
folyómunka futhat; a sorozat ezért a termék tényleges konkurens terhelését
tartalmazza. A világbeállítások JSON-ja előtte/utána karakterre azonos.
Nem történt kameramozgatás; a runner új változata külön kamera-pózt is ment.

Két bemelegítés után **10–10 befejezett, profilozás nélküli Build**.
A közös időalap az eddigi PerfLog teljes Build-idő; a runner Stopwatch ideje
a `Build()` visszatéréséig tart, így a végén induló háttérmunka ütemezését
is tartalmazhatja. Ezeket nem keverjük. A meleg kontrollok kiválasztása:
`-Instrumentation control -Cache warm -SkipBuilds 2 -MaxBuilds 10`.

| Kontroll, ms | Előtte | Utána |
|---|---:|---:|
| Teljes Build átlag | 2375,08 | 2150,30 |
| Teljes Build medián | 2394,10 | 2171,75 |
| Minimum–maximum | 2214,50–2522,70 | 2060,00–2231,00 |
| Nearest-rank p95 (10 mintán maximum) | 2522,70 | 2231,00 |
| Populációs szórás | 119,09 | 73,68 |
| Terrain mesh összesen, átlag / szórás | 314,78 / 131,09 | 137,94 / 13,36 |
| Bucket-előkészítés, átlag / szórás | 61,57 / 25,22 | 33,96 / 11,66 |
| Statikus víz, átlag / szórás | 96,64 / 14,75 | 212,96 / 151,84 |

A teljes átlag ebben a menetben **9,46%-kal**, a szórás **38,13%-kal**
csökkent. Ez egy konfiguráció két egymást követő Editor-sorozata, nem
platformfüggetlen teljesítményígéret. A vízfázis kifejezetten romlott:
a teljes Build javulásából nem következik minden részfázis stabilizálódása.

### Allokációs bizonyíték és módosítás

Külön 3–3 rövid Profiler-minta, Deep Profile és hívásláncok nélkül.
A Unity `RawFrameDataView` tényleges `GC.Alloc` méretmetaadatait összegeztük
a jelölt főszálú tartományokon belül; ez megkerüli a nem működő Mono API-t.
A teljes marker és az alatta levő fázisok egymásba ágyazott összegek,
ezért nem adhatók össze.

| Főszálú managed allokáció, bájt/Build | Előtte | Utána |
|---|---:|---:|
| Teljes Build, 3 minta minimum–maximum | 691 878 323–691 880 003 | 514 674 689–514 678 335 |
| Mesh-összeállítás (mindhárom mintában) | 204 070 380 | 75 500 252 |
| Statikus víz (mindhárom mintában) | 132 805 219 | 84 171 555 |
| Bucket-előkészítés | 121 582 120 | 121 582 120 |
| Terrain indexmaszk | 31 458 600 | 31 458 600 |

A két javított fázis **177 203 792 bájt (~169 MiB)** átmeneti allokációját
szünteti meg Buildenként. A teljes főszálú allokáció csökkenése kb. **25,6%**.
A `GC.Alloc` hívásláncokat külön egyetlen menetben gyűjtöttük: a növekvő
tömböket a `ConcatenateMultiMaterialBuckets` és `BuildWaterSurface`
listakapacitás-növelése hozta létre. Az ND-134 javítása pontos kapacitást
foglal a kész bucketek elemszáma alapján. A sorrend, indexeltolás, bounds és
elemek változatlanok; nincs pool, közös buffer vagy új cache.

Az előtte mérés Build-időablakában a többi megfigyelt szál összes
`GC.Alloc` értéke **104,1–131,1 MB** volt. Ez háttérfeladatokat is tartalmaz,
nem mind rendelhető ehhez a Buildhez. A Unity által nyilvántartott memória
nettó változását és a mesh pillanatnyi méretét is mentjük, de ezek nem
allokációs összegek és nem különítik el a natív költséget.

### Bitazonosság és ellenőrzések

A statikus terep, víz és tó csúcs-, normál-, szín-, submesh-index- és
bounds-adataiból SHA-256 készült; az előtte/utána értékek rendre azonosak:

```text
Terrain:      CD1F5D261639BF179B5752463BA0277B146C628702B8482EF96B48D09E2E1ADB
WaterSurface: 5E9D187F599FF323CB4851F786649FEDFF0C79817E69A7E7B04425073055A34E
LakeSurface:  96C57F0485A3405CFBD2AAF5973AA629322AC864572F53918F13336EA3E2B640
```

Ez a mért konfiguráció tartalmi egyezése, nem minden seed/LOD vizuális
elfogadása. Élő Play-mérés és Unity-fordítás történt, külön képi acceptance nem.

- Friss solution build: 0 hiba, 0 figyelmeztetés.
- Friss viewer-LOD tesztek: **490/490 zöld**; logelemző próbája zöld,
  immár profil/kontroll-, cache-, bemelegítés- és darabszámszűréssel is.
- Viewer offline fordítás: 0 hiba, 122 meglévő figyelmeztetés.
- Élő Unity: újrafordítás `completed`, `compilationFailed=false`, Console
  ground truth 0 hiba. A végleges külső runner dry-run fordítása is sikeres.
- A korábbi teljes 1503/1503 teszt és KAT 9/9 ebben a naplóban fent szerepel;
  a változatlan Core hosszú tesztjeit ebben a folytatásban nem ismételtük.
- Végül Edit mód, háttérfutás `false`, Profiler és Deep Profile `false`.
  Scene-t nem mentettünk; a napi időléptetés és profilkapcsolók visszaálltak.

### Reprodukció és helyi eredmények

A `tools/diagnostics/DeepTimeProfileProbe.cs` külső Unity Pipeline-script.
Használat előtt aktív, nem szünetelő Play mód kell, a Profiler legyen
kikapcsolva. A gyökér `artifacts/a5-run-config.json` konfiguráció például:

```json
{"output":"artifacts/a5-control-new.json","warmups":2,"runs":10,"profile":false,"callStacks":false,"hashGeometry":false}
```

```powershell
& 'C:/Users/Krisz/AppData/Local/Unity/bin/unity.exe' command run_script `
  --file F:/Claude/wg/tools/diagnostics/DeepTimeProfileProbe.cs `
  --entry DeepTimeProfileProbe.Start `
  --project-path F:/Claude/wg/unity/WorldGenViewer --timeout 20
```

A parancs a menetet elindítja; **csak a JSON `status=complete` jelent kész
mérést**. Hiba esetén `failed` és `error`; 240 s határidő. A runner megőrzi
és végül visszaállítja a kapcsolókat, nem indít/leállít Play módot, nem ment
scene-t, nem ír felül eredményfájlt. Két menetet ne indíts párhuzamosan.

Profilhoz új kimeneti név, `warmups:0,runs:3,profile:true`; külön hívásláncos
menethez `runs:1,callStacks:true`. Hashhez külön menet:
`warmups:0,runs:1,profile:false,hashGeometry:true`. A hash+profil együtt
tiltott: a hash sok Profiler-mintát generál. Az első ilyen diagnosztikai
kísérlet nagy `.data` fájlját töröltük, a hash-t tartalmazó JSON-t megtartottuk.

A `.data` fájl a Profiler végén még tárolt frame-eket őrzi; nem garantált,
hogy a teljes menet belefér. A JSON minden kiválasztott Build összegét menti.
A runner világ-/kamerakonfigurációt, viewportot és kamera-pózt is rögzít.
Az első kontroll-runner serializerhibája miatt az előtte-kontroll JSON
nem tartalmaz runlistát; az érvényes 10 minta a PerfLogban van. A végleges
runner .NET szerializálót használ, az utána JSON runlistája már teljes.

Helyi, nem verziózott mérési fájlok:

- Kontroll: `PerfLog_20260922_171453.txt` és `PerfLog_20260922_172226.txt`
  a viewer `Logs/` mappájában; feldolgozva `artifacts/a5-before-selected.json`
  és `artifacts/a5-after-selected.json`.
- Profil: `artifacts/a5-before-profile.json`, `a5-after-profile.json` és
  megfelelő `.data`; híváslánc: `artifacts/a5-before-stacks.json` / `.data`.
- Tartalmi hash: `artifacts/a5-before-detail.json`, `a5-after-geometry.json`.

A végleges kontrollforrások SHA-256 értékei:

```text
171453: F0E352C3FBA102F4C93694957911F42049230CDCB5733E1B63180AC72AA25E4C
172226: 7B8AF224A5299FC66A4547397D4A680CDF023EE057E898BB6FEC1182E3CD1AD0
```

### Ami még nyitott

Az utána-profil vízfázisa **351,16 / 60,88 / 381,97 ms**. A látható
`GC.Collect` rész-események néhány ms-osak, nem magyarázzák önmagukban a
~300 ms eltérést. A gyűjtésszámláló és az allokáció idejének együttjárása
továbbra sem elég kizárólagos GC-okozatot állítani. Következő lépés a
vízfázis belső összefűzés/upload/maszk szakaszainak külön mérése és a
CPU-futás/szálütemezés/inkrementális GC elkülönítése. A worker/native
tulajdonítás és az Editor-/JIT-hatás Player-kontrollja is hátra van.

Tartalmilag súlyozott **A5-részfeladat: kb. 70%**; a mérőeszköz, kontroll,
igazolt allokációs forrás és első bitazonos javítás kész. Nem a teljes
M9/M10 százaléka. Durva összes ráfordítás **2–4 munkaóra**, hátralévő
**2–4 munkaóra**, nem mért időnapló. A5 ezért folyamatban marad.

**Újabb folytatás:** a vízfázis szétbontása, Windows CPU-idő, worker-
hívásláncok és Development Player-/GC-kontroll elkészült; a jelenlegi
állapotot a [második A5 napló](2026-09-22-a5-water-cpu-player-profile.md)
írja le. A fenti ~70% a korábbi mérési kör állapota.

**Zárás:** a második napló végén szereplő 20 időváltásos kontroll és
6 külön hash-Build sikeres. A5 a pontosított ND-133 hatókörében lezárva;
a belső allokátor teljes feltárása opcionális további vizsgálat marad.
A korábbi százalékok és becslések történeti állapotok, nem aktuális kapuk.
