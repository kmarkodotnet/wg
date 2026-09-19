# Tile-méret mintavételező napló és kiértékelő (2026-09-18)

## Kérés

"Csinálj egy olyan naplót, hogy az épp látható tile-ok közül mintavételezz
tile méretet és pozíciót az égitesten, és mindig mentsd el a zoom aktuális
értékét. Egyenletes eloszlásban vegyél mintát. A logot később felolvasod, és
azt vizsgálod, hogy adott zoom mellett mekkora adott képernyő-pozíción a
tile mérete."

## Amit NEM kellett megírni (már megvolt)

Fontos előzmény-ellenőrzés: az **egyenletes képernyő-raszteres mintavétel a
tile pixelméretével MÁR LÉTEZETT** (`RenderedTileDiagnostics`, ND-75):
17×9-es raszter, minden mintaponthoz megkeresi a fedő, MÁR FELTÖLTÖTT
quadot, és mér `WidthPx`/`HeightPx`/`DiameterPx`-et. A zoom is benne volt a
napló fejlécében (`altitudeAboveBaseUnits`, `zoomRatioRoverAltitude`,
`viewClass`). Ez 2 másodpercenként, háttérszálon fut.

Ami hiányzott:
- a minta **égitest-beli pozíciója** (lat/lon) — egyáltalán nem volt,
- a minta **LOD-szintje** — csak a legnagyobb 8 kiugró értékre
  (`[ND-77 terrain]`),
- **gépi feldolgozhatóság**: a `[ND-75 row]` térkép ember-olvasásra jó, de a
  zoomot külön fejlécből kellene hozzá illeszteni, és a képernyő-pozíció is
  csak implicit (oszlop/sor index),
- a tile **fizikai mérete** (km).

## Amit hozzáadtam

**`[tilesample]` napló — soronként EGY minta**, a meglévő 17×9-es raszter
minden találatáról (`PlanetGridMesh.RenderDiagnostics.cs`,
`logUniformTileSamples` kapcsoló, alapból bekapcsolva):

```
[tilesample] frame=.. altitudeUnits=.. zoomRatio=.. viewClass=.. fov=..
             deepTimeMyr=.. col=.. row=.. sx=.. sy=.. px=.. py=..
             source=D clippedDiameterPx=.. clippedWidthPx=..
             clippedHeightPx=.. fullQuadDiameterPx=..
             tile=.. face=.. level=.. u=.. v=..
             lat=.. lon=.. tileEdgeKm=.. tileEdgeUnits=..
```

Minden sor **önmagában** hordozza a zoomot ÉS a képernyő-pozíciót, tehát
utólag fejléc-összeillesztés nélkül kiértékelhető.

Két méret-forrás, szándékosan:
- **pixel**: a már feltöltött quad vetítéséből (a `targetTilePixelSize=8`
  LOD-cél közvetlen mértéke),
- **kilométer**: UGYANANNAK a quadnak a **rajzolt** élhosszából, a bolygó
  fizikai sugarával átskálázva (`PlanetConstants.RadiusMeters / radius`) —
  tehát a tényleges geometriát méri, nem egy idealizált szögméret-formulát
  (a cubed-sphere tile-területek ~1,3-1,4× arányban változnak, ND-24).

A lat/lon a Core test-keretében számolódik, ahol **a pólus a Z tengely**
(`BodyFrameConversion` doksija szerint): `lat=asin(z)`, `lon=atan2(y,x)`.
Ez diagnosztika, nem a világmodell számítási útja, ezért a trigonometria itt
nem esik az I1/ND-23 determinizmus-hatókörbe.

A tile-azonosítás a MÁR MEGLÉVŐ úton megy (`surface.TileIds[hit.Quad]`,
ugyanaz, amit az `[ND-77 terrain]` kiugró-elemzés használ) — nincs új
geometria-számítás, a mérés továbbra is háttérszálon fut.

## Kiértékelő: `tools/analyze_tile_samples.py`

Függőség nélküli (stdlib) elemző, három nézettel:

1. **Zoom-sáv × képernyő-zóna** tábla: zóna = közép / középső gyűrű /
   periféria (a képernyő-középtől mért relatív távolság alapján — a
   best-first LOD épp a perifériát hagyja durvábban, ezért ez a
   legárulkodóbb bontás). Oszlopok: minta-szám, pixel p50/p90, **a
   LOD-célhoz viszonyított arány** (pl. `2.99x` = háromszor durvább a
   célnál), LOD-szint p50, km p50.
2. **Rács-térkép zoom-sávonként**: a 17×9-es raszter mediánja pozícióként —
   ezen közvetlenül látszik, hol és mennyivel durvul el a kép.
3. **LOD-szint → tényleges fizikai élhossz (km)** összegzés.

Használat:
```
python tools/analyze_tile_samples.py unity/WorldGenViewer/Logs/PerfLog_*.txt
python tools/analyze_tile_samples.py --target-px 8 --source D <fájlok>
```

## Ellenőrzés

- Unity `Assembly-CSharp.csproj` fordítás: 180 hiba, mind a 8 MÁR ISMERT,
  elfogadott osztályban — **nulla új hibaosztály, nulla hiba a módosított
  fájlban**. (Két saját hibát javítottam közben: hiányzó `using
  WorldGen.Core` a `PlanetConstants`-hoz, és `FormattableString.Invariant`
  `+`-szal összefűzött interpolált stringeken — utóbbi már `string`-et ad.
  Most minden szám invariánsan formázott stringként kerül a sorba.)
- `python tools/analyze_tile_samples.py --self-test`: PASS (parser +
  zoom-sávok + zónák).
- **Végponttól végpontig próba szintetikus naplón** (919 sor, két
  zoom-sávban, szándékosan középen finom / szélen durva adattal): az elemző
  helyesen adta vissza a radiális elkoszolódást (közép 1,44× → periféria
  2,99× a célhoz képest), a rács-térkép és a szint→km tábla is rendben.
  Ez azt bizonyítja, hogy a kiértékelő működik, MÉG MIELŐTT valódi
  Unity-naplót generálnál.

## Következő lépés (te)

Egy Play-munkamenet, amiben végigmész a zoom-skálán (bolygó-nézettől a
legközelebbi közelítésig), közben forgatsz is. A napló 2 másodpercenként
153 mintát ír. Aztán add ide a PerfLog nevét, és felolvasom.

Ha túl nagy lesz a napló, a `logUniformTileSamples` kikapcsolható — a
`[ND-75 ...]` összefoglaló attól függetlenül megy tovább.

---

# Az első valódi mérés kiértékelése

Napló: `unity/WorldGenViewer/Logs/PerfLog_20260918_215749.txt` (1,57 MB),
**3210 `[tilesample]` minta, 35 különböző magasságról**, viewport
1196×710, 68 LOD-vágás-számítás.

## Mit mér a kérdés (zoom × képernyő-pozíció → tile-méret)

| zoom-sáv (magasság) | zóna | n | px p50 | px p90 | cél-arány | szint p50 | km p50 |
|---|---|---|---|---|---|---|---|
| 3-10 | közép | 102 | 30,07 | 38,34 | **3,76×** | 10 | 11,07 |
| 3-10 | középső gyűrű | 348 | 31,52 | 72,69 | **3,94×** | 9 | 21,39 |
| 3-10 | periféria | 468 | 32,64 | 101,58 | **4,08×** | 9 | 21,54 |
| 10-30 | közép | 85 | 18,55 | 21,93 | 2,32× | 9 | 22,23 |
| 10-30 | középső gyűrű | 290 | 17,52 | 22,48 | 2,19× | 9 | 21,88 |
| 10-30 | periféria | 390 | 17,05 | 24,03 | 2,13× | 9 | 21,91 |
| 30-100 | közép | 170 | 13,52 | 16,34 | 1,69× | 8 | 43,19 |
| 30-100 | középső gyűrű | 562 | 12,63 | 15,90 | 1,58× | 8 | 42,87 |
| 30-100 | periféria | 649 | 11,18 | 15,88 | 1,40× | 8 | 42,48 |
| 100+ | közép | 68 | 2,54 | 4,85 | **0,32×** | 3,5 | 42,87 |
| 100+ | középső gyűrű | 72 | 3,38 | 4,46 | 0,42× | – | 42,90 |
| 100+ | periféria | 6 | 3,15 | 3,50 | 0,39× | – | 44,22 |

A 0-0,3 / 0,3-1 / 1-3 sáv üres: ez a munkamenet nem ment 3 egység
magasság alá.

**A fő anomália: befelé zoomolva a tile-ok NAGYOBBAK lesznek a képernyőn**
(11 → 18 → 31 px), pedig a LOD-célja pont az, hogy állandó ~8 px legyen.
A finomítás nem tart lépést a közelítéssel.

## Pozíció-függés: nem radiális, hanem bal-jobb aszimmetria

A rács-térképek szerint a radiális bontás majdnem lapos (a periféria még
egy hajszállal *kisebb* px-ben, mert a súroló szög összenyomja a
vetületet). Ami viszont látszik: **szisztematikus bal-jobb dőlés** —
3-10 sávban a bal szél ~25-30 px, a jobb szél 36-47 px; 30-100 sávban
bal oszlop ~10-11 px, jobb szél 13-16 px. Ez nem szórás, hanem a
best-first bejárás sorrendje: a budget elfogy, mielőtt a kép egyik
oldaláig eljutna.

## Gyökérok A — a budget aritmetikailag kizárja a célt

- 68 vágásból **44 pontosan 7998-8000 leafnél áll meg** (`adaptiveRenderBudget = 8000`).
- A kiugró tile-ok megállási oka: **79/95 `stop=leaf-budget`** (mellette
  8 `outside-view`, 5 `below-threshold`, 1 `split-quota`).
- `deferredSplits` maximuma **13 095** — a szelekció ennyi további
  felosztást szeretett volna.

> **JAVÍTÁS (2026-09-19).** A fenti harmadik pont értelmezése téves volt: a
> `deferredSplits` NEM a leaf-budget éhezését számolja, hanem a
> **kérésenkénti új-felosztás kvótát** (`MaxNewSplits`, `stop=split-quota`).
> A leaf-budget miatti megállást akkor **semmi nem számolta**, csak a trace
> rögzítette tile-onként. A konklúzió (a budget a szűk keresztmetszet) állva
> marad — a `stop=leaf-budget` 79/95-ös aránya és a 7998-8000-es telítés
> önmagában bizonyítja —, de a 13 095 más mennyiség, mint amit írtam. Ezért
> került be a külön `starvedBudget` számláló, ld. alább.

Ellenőrző számítás: 1196×710 = 849 160 px², 8×8 px-es tile-onként
64 px² → **≥13 268 leaf kell** ahhoz, hogy a kitöltött képernyőn a cél
teljesüljön, a restricted-balance ráhagyás előtt. A budget 8000. Tehát
a 8 px-es cél a jelenlegi budgettel **nem elérhető**, bármilyen jó is a
prioritási metrika.

## Gyökérok B — a budget elosztása is hibás

- `nadirCutL` (a kamera alatti tile szintje, azaz amit épp nézünk):
  **68 vágásból 59-ben 8 vagy 9** — alapszint vagy egy fölötte.
- `deepestCutL`: **19 vágásban eléri a 20-at**, a `adaptiveMaxLevel`
  maximumát.

A vágás tehát valahol ~85 m-es tile-okig lefúr, miközben közvetlenül a
kamera alatt 43/22 km-es tile marad. A 3210 mintából level 12-re 14,
level 13-ra 6 találat esik — a mély szintek gyakorlatilag nem a látott
felszínen vannak. Ha a prioritási metrika helyes lenne, a nadír lenne a
legmélyebb pont.

## Felelősség: ez a saját regresszióm

Az `adaptiveRenderBudget` 200000 → 8000 változást a **df59d39**
(`perf(viewer): bound the adaptive LOD cut with a realistic render budget`)
commit vitte be, a szaggatás-vizsgálat **téves első kísérletében**. A
szaggatás valódi oka utóbb a léptékcsík főszálon futó újraszámítása volt
(0552bb1) — a budget-csökkentés a szaggatáson nem segített, viszont most
közvetlenül ez fojtja el az M9 felszíni részletességet.

## De a 200000 visszaállítása sem a válasz — mért költségek

| mérőszám | érték cut=8000-nél | hol fut |
|---|---|---|
| `mesh-feltoltes` | 0,57-2,67 ms | **főszál** |
| `maxSlice` | ≤2,0 ms | főszál |
| `cut` (szelekció) | 98-272 ms (1 kiugró: 608) | háttérszál |
| `requestAge` | 300-1190 ms | latencia |
| `metricComputed` | 177 000-209 000 / vágás | háttérszál |

A főszálon tehát van tartalék (ezért nem szaggat), de a szelekció már
most 100-270 ms, és a kép 0,3-1,2 s késéssel követi a kamerát. 16×
budgettel ez több másodperces latencia lenne — vagyis a puszta
szám-visszaemelés a szaggatást csúszásra váltaná.

## Javaslat a következő lépésre (M9)

1. **Budget a viewporthoz kötve, ne fix szám**: `ceil(szélesség × magasság
   / targetTilePixelSize²) × ráhagyás`. 1196×710 + 8 px → ~13 300 × ~1,5
   ≈ 20 000. Ez mérhető cél, nem tipp.
2. **Előbb a B gyökérok**, mert ingyen van: ha a nadír kapja a mély
   szinteket a level-20-as lefúrás helyett, ugyanabból a 8000-ből
   érdemben jobb kép lesz. A `deepestCutL=20` / `nadirCutL=8` pár az a
   konkrét jelenség, amit reprodukálni és tesztelni kell.
3. A szelekció 177-209 ezer metrika-kiértékelése a latencia forrása —
   ez a budget-emelés előfeltétele, nem utólagos optimalizáció.

Mindhárom lépés csak a viewert érinti, **nem seed-törő**.

---

# Munka-könyvelés a naplóban (2026-09-19)

## Kérés

"Az egyes mentett adatok mellett kiírod az aktuális összes tile számosságot,
plusz azt, hogy az aktuális zoom vagy rotáció miatt hány új tile került
kiszámolásra és hány tile kalkulálása lett abbahagyva, mert szükségtelen."

## A kulcsfelismerés: a kért szám nem is létezett

A vágásnak volt `newSplits` és `deferredSplits` számlálója, de a
**`stop=leaf-budget` megállást semmi nem számolta** — csak a
`LodSelectionTrace` rögzítette tile-onként, és a napló abból is csak a
legnagyobb 8 kiugró tile-t írta ki. Épp ezért tudtam félreolvasni tegnap a
`deferredSplits`-et (az a *kérésenkénti kvóta* számlálója).

## Amit hozzáadtam

**`LodSelectionWork`** (`Lod/ProjectedLodView.cs`) — négy új számláló, és a
lényeg a **szétválasztásuk**, mert összevonva a szám félrevezető:

| kategória | számláló | jelentés |
|---|---|---|
| **ELVONT** munka | `BudgetStops` | a leaf-budget blokkolta a kért felosztást (**ÚJ**) |
| **ELVONT** munka | `DeferredSplits` | a kérésenkénti kvóta fogyott el (már megvolt) |
| szükségtelen | `BelowThresholdStops` | a tile MÁR elég finom |
| szükségtelen | `MaxLevelStops` | elérte az `adaptiveMaxLevel`-t |
| szükségtelen | `InvisibleStops` | látókúpon kívül / súroló szög |
| szükségtelen | `SkippedStaticBases` | ND-78 óceán-előszűrés (már megvolt) |

Az "elvont" ennyivel rosszabb a kép, mint amit a metrika kért; a
"szükségtelen" helyesen maradt abba. A felhasználói kérdés a kettőt
egybevette — a napló szándékosan külön mutatja.

**`AdaptiveQuadTree`** — a számlálók ott íródnak, ahol a `stopReason` már
eldőlt, tehát a trace és a számláló nem tud elcsúszni egymástól.

**`[tilesample]` sorok** — minden minta mellé bekerül mindhárom blokk:
```
cutLeaves=.. dynLeaves=.. waterLeaves=.. cutBudget=.. cutSaturated=..
newSplits=.. reusedLeaves=.. metricComputed=.. metricReused=..
starvedBudget=.. starvedQuota=..
skipSufficient=.. skipMaxLevel=.. skipInvisible=.. skipOceanBase=.. skipOceanLeaf=..
```
Szándékosan ismétlődik soronként: a napló egész tervezési elve, hogy egy sor
önmagában kiértékelhető legyen. A `cutSaturated` a leggyorsabb jelzés arra,
hogy a látott durva tile a budget miatt durva-e. Az `[async apply ND-76]`
összefoglaló sor is megkapta ugyanezeket a neveket.

## Teszt: `tests/.../CutWorkAccountingTests.cs` (11 eset, mind zöld)

A számok jelentését ELŐBB igazoljuk, csak utána következtetünk belőlük —
különben megismételném a tegnapi hibát. A teszt nevesítve fogja meg a
félreolvasást (`SplitQuotaIsAccountedSeparatelyFromLeafBudget`), valamint:
`LeafBudgetStarvationIsCounted`, `AmpleBudgetLeavesOnlySufficientStops`,
`NewSplitsNeverExceedQuota` (3 eset), `EveryDynamicLeafHasARecordedStopReason`
(teljességi invariáns: minden kirajzolt dinamikus tile-hoz tartozik rögzített
ok), `CountersAreRepeatable` (tisztaság),
`PlanetViewFitsTheBudgetWhileSurfaceProximityStarvesIt`,
`UnconstrainedCutIsManyTimesLargerThanTheTightBudget`.

**Méretezés:** durva küszöbbel (`PixelHeight=135`) és 600/40 000-es
budgettel dolgozik, hogy a "bőséges" eset is pár ezer tile maradjon. Egy
korábbi, 200 000-es budgettel dolgozó próbám több százezer TileId-t
allokált, és elhasította a párhuzamosan futó, allokációt mérő
`TerrainEvaluationCacheTests`-t — ez a tanulság van beépítve.

## Mérés közben megdőlt feltevés

Első nekifutásra azt tesztelnem, hogy "közelebb zoomolva több felosztás
vonódik el". **Megbukott.** A tényleges mérés (1080p küszöb, budget 8000):

| kamera-távolság | magasság | levelek | elvont (budget) |
|---|---|---|---|
| 400 | 300 | 0 | 0 |
| 200 | 100 | 0 | 0 |
| 140 | 40 | 8000 | 24 594 |
| **120** | **20** | **8000** | **37 864** |
| 110 | 10 | 8000 | 18 829 |
| 103 | 3 | 8000 | 7 665 |
| 100,6 | 0,6 | 8000 | 7 201 |

Az éhezés **középtávon a legnagyobb** (nagy a látott felület ÉS nagy a kért
részletesség), a legközelebbi zoomnál visszaesik, mert a látott felület
összezsugorodik. Bolygó-nézetben (≥200) a budget bőven elég, dinamikus levél
sem keletkezik. A teszt ezért a két szélső esetet hasonlítja, nem
monotonitást állít.

Ugyanez a mérés adja a **budget-plafon mértékét**: korlátlan budgettel
ugyanez a nézet 164 000-268 000 levelet kér, a 8000 helyett.

## Kiértékelő

`tools/analyze_tile_samples.py` új szakasza: "Munka-könyvelés" zoom-sávonként,
**vágásonként deduplikálva** (`frame` szerint) — mintaszám szerint súlyozva a
sok mintát adó zoom-sávok felülreprezentáltak lennének. Két tábla: a
tile-számosság/újraszámolás, illetve az abbahagyott számítások OK szerint,
az "ELVONT" és az "ok:" oszlopok elkülönítve.

## Ellenőrzés

- `dotnet test` LOD-csomag: **402/402 zöld**, 18 s (a korábban elhasított
  `TerrainEvaluationCacheTests`-szel együtt).
- Unity `Assembly-CSharp` fordítás: **180 hiba, mind a 8 ISMERT, elfogadott
  osztályban** — nulla új hibaosztály; a módosított fájlokban
  (`RenderDiagnostics`, `AdaptiveQuadTree`, `ProjectedLodView`) **nulla**
  hiba, a `PlanetGridMesh.cs`-ben a szerkesztett sorok környékén sem új
  (a listázott CS8618-ak pre-existing referencia-mezők, az én új mezőim `int`-ek).
- `python tools/analyze_tile_samples.py --self-test`: PASS, az új mezőkre is.
- **Végponttól végpontig próba szintetikus naplón**: 306 mintasor / 2 vágás →
  helyesen 2 vágásra deduplikált, és helyesen különítette el a nem telített
  bolygó-nézetet (0 elvont, 9803 "elég finom") a telített felszín-közeltől
  (1/1 telített, 7201 elvont).
- A régi, mezők nélküli napló továbbra is olvasható (a szakasz jelzi, hogy
  nincs `cutWork` adat).

## Következő lépés (te)

Egy Play-munkamenet a zoom-skálán végig, forgatással. Utána a napló
megmutatja, hogy adott zoomon és képernyő-pozíción a durva tile a budget
miatt durva-e (`cutSaturated`, `starvedBudget`), vagy azért, mert a metrika
szerint már elég finom (`skipSufficient`).

---

# Korrekció: a "B gyökérok" nem létezik (2026-09-19)

## Mit állítottam

A 2026-09-18-i kiértékelésben ezt írtam: *"Gyökérok B — a budget elosztása
is hibás. A vágás valahol ~85 m-es tile-okig lefúr, miközben közvetlenül a
kamera alatt 43/22 km-es tile marad."* Alapja a naplóbeli `nadirCutL=8` és
`deepestCutL=20` **marginális** eloszlása volt, és ezt javasoltam első
javítandónak, mert "ingyen van".

## Mit mutat a mérés

**Frissen számolt vágásban a nadír MINDIG a legmélyebb szintet kapja.**
Offline, a LOD-tesztkörnyezetben (`AdaptiveQuadTree.BuildCut`) minden
konfigurációban `nadirCutL == deepestCutL`:

| próba | eredmény |
|---|---|
| tiszta gömb, 120 / 103 / 100,6 távolság | nadír = legmélyebb (9 / 11 / 13) |
| terep-proxy 0% és 2% domborzattal | nadír = legmélyebb (9 / 12 / 14) |
| `previousCut` hiszterézis, 0,05-5° forgatás után | nadír = legmélyebb (13) |
| `previousCut` hiszterézis, zoom 100,3 → 100,02 | nadír = legmélyebb (14/16/18) |

Három kézenfekvő magyarázatot így **cáfoltam**: nem a terep-proxy hibás
metrikája, nem a hiszterézis, és nem is a kérés késése (a napló a
`_pendingCutCamX`-et használja, vagyis azt a kamerát, amire a vágás
KÉSZÜLT — ellenőriztem a hívási helyen, `PlanetGridMesh.cs:3373`).

## A valódi ok: két HELYES viselkedés

Amikor reprodukáltam a mintát, mindkétszer szándékos működés adta:

1. **ND-78 óceán-előszűrés.** Ha a nadír base-tile-ját a `SkipStaticBase`
   kihagyja, az eredmény pontosan `nadirCutL=8`, `deepestCutL=13`. A vizet
   a saját víz-LOD-ja fedi, a terep-réteg helyesen nem finomít alatta.
2. **Ferde kameraállás.** 75°-os dőlésnél `nadirCutL=8`, `deepestCutL=13`:
   a nadír — a kamera ALATTI pont, **nem a képernyő közepe** — kiesik a
   látókúpból, ahol helyesen nem finomodik.

A valódi naplóban mind a **11** `nadirOceanBlocked=True` eset `nadirCutL=8`
volt (hibátlan egyirányú implikáció, 68 vágásból). A maradék 21 sekély eset
a ferde nézet számlájára írható — a pálya menti kamera alacsonyan épp
ferdén néz.

## Következmény

`nadirCutL` **nem azt méri, amit nézünk**. Prioritás-hiba nincs; ha ezt
"megjavítottam" volna, egy nem létező hibát írtam volna át, és közben
elrontottam volna az ND-78-at. A `ShallowNadirIsCorrectWhenExcludedOrOffScreen`
és a `FreshCutAlwaysRefinesTheNadirDeepest` teszt ezt rögzíti, hogy legközelebb
se induljon el senki ezen az úton.

**Az "A gyökérok" viszont ÁLL**, és közvetlen bizonyítékon nyugszik, nem
marginális eloszláson: `stop=leaf-budget` a kiugró tile-ok 79/95 arányában,
telítés 68 vágásból 44-nél pontosan 7998-8000 leafnél, és a mért
tile-méret 1,4-4,1× a 8 px-es célhoz képest. A teendő tehát **kizárólag**
a budget-plafon és a szelekció költsége — nem a prioritás.
