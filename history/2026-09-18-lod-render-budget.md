# Rotáció/zoom szaggatás: az adaptív render-költségvetés valós értékre állítása (2026-09-18)

## Kérés

A felhasználó a 09-13-i diagnózis után élőben újra ellenőrizte:
"szaggat a rotáció és a zoom is. nagyon visszajött a jelenség". A
navigációs menü / tektonikus overlay viszont rendben van.

## Kiindulás: a 09-13-i diagnózis két javaslata

A `history/2026-09-13-rotation-zoom-stutter-lod-cut-growth.md` élő
PerfLog-ból (`PerfLog_20260913_184215.txt`) már kimérte a tünetet:
a dinamikus levélszám 52 → 32 795, a kérés-idő 100-350ms → 1300-1900ms.
Két irányt javasolt, implementáció nélkül:

- (a) `adaptiveRenderBudget` csökkentése,
- (b) a `newGroups` bejárásban a változatlan chunkok kiszűrése MÉG a
  `CaptureResolvedPositions`/`SamePositions` ellenőrzés előtt.

## A (b) irány ELLENŐRZÉS UTÁN ELVETVE — forrásból igazolva

A (b) javaslat **nem biztonságos**, és ezt kódolvasás igazolta, nem
feltételezés:

`CaptureResolvedPositions` → `GetAdaptiveCorners` → `_activeCornerResolver.Corner(...)`
(`PlanetGridMesh.cs:4483`). A `LodCornerResolver` a **teljes aktuális cut**
`coverage`-éből épül minden újraépítésnél, és pontosan a T-csomópontok
összefűzését (varratmentes él) végzi. Ezért egy chunk feloldott
csúcspozíciói AKKOR IS megváltoznak, ha a saját levélhalmaza változatlan,
de egy SZOMSZÉDOS chunk finomodott. A `!changed && SamePositions(...)`
ellenőrzés tehát **teherhordó**: pont ezt az esetet fogja meg. Kihagyása
visszahozná a varratokat/repedéseket a chunk-határokon — egy olyan
hibaosztályt, amivel ez a projekt már többször megküzdött.

## A (a) irány: mérés a döntéshez

Ideiglenes (a végleges tesztből eltávolított) próbákkal megmértem, mennyi
dinamikus levelet **kérne** a kiválasztás, ha a budget nem korlátozná
(`AdaptiveQuadTree.BuildCut`, teszt-parametrizálás, level 8 base / 20 max):

| kamera magasság (egység) | kért dinamikus levél (budget=200 000) |
|---|---|
| 0.1 | 176 292 |
| 0.6 | 161 912 |
| 2.0 | 192 856 |
| 10.0 | 199 999 |
| 30.0 | 162 660 |
| 100.0 | 0 (a statikus base fed) |

**A kulcs-felismerés:** a felszín közelében MINDEN gyakorlati
kameramagasságon a kérés a költségvetés közelébe/telítésébe fut. Az
`adaptiveRenderBudget` tehát **nem egy néha aktiváló védőháló, hanem a
cut méretét MINDEN esetben meghatározó, kötő korlát** — 200 000-en
állítva pedig gyakorlatilag "korlát nélkül" engedi a rendszert.
(A teszt-küszöb agresszívebb a produkciósnál, ezért az abszolút számok
nem a produkciós értékek — az élő log ~33 000-et mért. A szerkezeti
következtetés viszont ugyanaz: 200 000 soha nem kötött, a 8 000 keményen
köt.)

Forgatás + hiszterézis-visszacsatolás (24 lépés, minden lépés az előző
cutot kapja) mellett a cut a budgetnél megáll és ott is marad
(2000 → 2000, 8000 → 8000), tehát a korlát megbízható, nem közelítő.

## Változtatás

- `PlanetGridMesh.adaptiveRenderBudget`: **200 000 → 8 000** (kód
  alapérték + tooltip a mérési háttérrel).
- `PlanetView.unity` `adaptiveRenderBudget`: **200 000 → 8 000**.
  **Ez a lépés kritikus**: a projekt dokumentált csapdája, hogy a
  scene-ben szerializált Inspector-érték FELÜLÍRJA a kód-alapértéket,
  ezért a puszta kód-módosítás "nem hatna" (ld. a korábbi
  `surfaceAmbient`/`surfaceSpecularStrength` esetet).

Az érték az élő log (levélszám, kérés-idő) pároiból lineárisan
extrapolálva ~400-500ms kérés-időnek felel meg az 1300-1900ms helyett.

### Miért nem kellett a (b) optimalizáció

Mivel a cut mérete most keményen korlátozott, a chunk-bejárás
(`CaptureResolvedPositions`/`SamePositions`) költsége is ezzel együtt
korlátozott — ugyanaz a korlát fogja meg a geometria mennyiségét ÉS a
könyvelés költségét. A varratmentességet veszélyeztető (b) irányra
így nincs is szükség.

## Új regressziós teszt

`tests/WorldGen.Viewer.LodChunking.Tests/CutGrowthRegressionTests.cs`
(4+3 eset): a forgó kamera + hiszterézis-visszacsatolás semmilyen
lépésszám mellett sem tudja a cutot a budget (+ a `EnforceRestrictedBalance`
nem-strict ráhagyása) fölé vinni, és a budget a felszín közelében minden
vizsgált magasságon kötő korlát. Ez a visszatérő hibaosztály automatizált
védőhálója: ha valaki kiiktatja a budget-korlátot, ez elbukik.

**Megjegyzés a teszt kialakításához:** az első változat 200 000-es
költségvetéssel mért, és ezzel elbuktatta a párhuzamosan futó,
ALLOKÁCIÓT MÉRŐ `TerrainEvaluationCacheTests`-et (xUnit a
teszt-kollekciókat párhuzamosan futtatja, a több százezer elemű halmaz
GC-t indított a másik teszt mérési ablakában). A végleges teszt ezért
kis költségvetésekkel fut, a nagy mérés pedig itt, a naplóban van.

## Ellenőrzés

- `dotnet test tests/WorldGen.Viewer.LodChunking.Tests`: **391/391 PASS**
  (7 új eset; a korábban interferáló allokációs teszt is zöld).
- Unity `Assembly-CSharp.csproj` fordítás: 180 hiba, mind a 8 MÁR ISMERT,
  elfogadott hibaosztályban (CS0618 elavult `FindObjectOfType` + CS8600-8625
  nullable-warning-as-error) — **nulla új hibaosztály**, semmi a
  módosításomból.
- Core nem érintett (nincs Core-változás), nem seed-törő.

## ÉLŐ ELLENŐRZÉS ÉS HANGOLÁS — a felhasználóra vár

1. **Ha a Unity Editor épp nyitva van a `PlanetView.unity`-vel**, a
   memóriában lévő scene NEM tudja a fájl-szintű módosításomat, és
   mentéskor FELÜL IS ÍRHATJA. Ilyenkor vagy scene-újratöltés kell, vagy
   egyszerűen az Inspectorban kell 8000-re állítani az
   `adaptiveRenderBudget`-et.
2. A mező **Play közben is állítható**: az `OnValidate` beállítja az
   `_adaptiveConfigDirty`-t, ami a következő adaptív újraépítésnél
   kiüríti a chunk-cache-t (`PlanetGridMesh.cs:1092`) — tehát a hatás
   azonnali, újraindítás nélkül.
3. Javasolt sweep egy munkamenetben: 8000 → 4000 → 2000 (ha még szaggat),
   illetve 8000 → 16000 → 32000 (ha sima, de túl darabos a kép). A
   PerfLogban a `cut.Count`/`requestAge` sorok mutatják a hatást.
   A kompromisszum iránya: kisebb budget = simább mozgás, de a periférián
   durvább csempék (a legnagyobb képernyő-hibájú, központi rész marad
   finom — best-first kiválasztás).
