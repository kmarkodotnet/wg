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
