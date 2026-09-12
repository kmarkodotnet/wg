# ND-95 — Három korrekció közös átadása

## Hatókör és eredmény

A felhasználó a bizonytalan, 9,9 másodperces korábbi várakozás további
kikérdezése helyett három következő feladat megoldását kérte. A meglévő
mérési/kamera/lépték ág három korrekciója került kódba. **Nem tértünk
vissza a halasztott korai élességhez vagy selection-optimalizáláshoz.**
Core, seed, relief, scene és tile-küszöbök változatlanok ebben a körben.

### 1. Valós kérésóra és várakozási fázisok

A korábbi `requestAge` és ND-75 `pendingMs` frame-hez kötött Unity-időt
használt. A Build végén indított kérés ezért a Build-frame kezdetének
idejét kaphatta, másodpercekkel túlbecsülve a kickoff óta eltelt időt.
Most monotón `Stopwatch` időbélyegek mérik a tényleges eseményeket.

Új `[ND-95 request timing]` sor minden sikeres async publikációnál:

| Mező | Mért intervallum |
|---|---|
| `queueMs` | Kérésindítás → worker belépése; a Task.Run előtti rövid főszálas előkészítést is tartalmazza |
| `workerMs` | Worker belépése → kész buffer; falióra, nem tiszta processzoridő |
| `readyWaitMs` | Kész buffer → főszál észleli az eredményt |
| `stagingWallMs` | Átvétel → commit kezdete; staging-frame-ek közti várakozás is |
| `commitMs` | Commit kezdete → kész publikáció |
| `totalMs` | A teljes fenti lánc, a részek összege |

`requestTicks` az adott indítás monotón azonosítója, nem UTC-idő.
`focused` csak a publikációkori fókuszállapot, nem a teljes intervallum
fókusztörténete. A staging tényleges munkaideje továbbra is külön
`stageTotal`, nem azonos a `stagingWallMs` értékkel.

A régi mezőnév megmarad, de `requestAgeClock=ND95` / `pendingClock=ND95`
jelöli az új jelentést. A korábbi loggal különösen Build után nem szabad
egyszerű sebességnyereségként összevetni. A cancellation/supersede
szabály régi időalapja és küszöbei változatlanok. A korábbi 9,9 s-os eset
okát nem találtuk ki; a következő hasonló esemény már fázisokra bontható.

### 2. Lépték: csak az aktuális mérési helyzethez tartozó érték

A közös radiális felszínlekérdezés korábban csak snapshot-létezést
ellenőrzött, a még építésre váró új paramétereket nem zárta ki. Most
függő világváltozás esetén nem keveri a régi modelladatot az új relief/
radius beállítással. A lépték a saját target modelljét használja; másik
targetre váltás nem tarthatja meg az előző bolygót vagy választhat tetszőlegeset.
Sugármetszéshez a bolygó tényleges alap-radiusát használja, nem a kamera
külön beállított fallback-sugarát. Nulla/negatív skálát is elutasít.

A sikeres minta megőrzi a kamera- és targetmátrixot, vetületet,
viewportot, képernyőmagasságot és világ-revíziót. Bármelyik változásakor
a régi szám azonnal érvénytelen. A meglévő nyolc sikertelen mintás
türelem csak változatlan mérési helyzetre vonatkozik.

**Látható kompromisszum:** mozgás/világváltás közben a következő sikeres,
időkorlátosan ütemezett mintáig `Lépték: —` jelenhet meg. Az alap
frissítési időköz 0,15 s marad; nem erőltetünk minden frame-ben új,
drága modellmetszést. Mozgás közben ezért lehet szakaszos a kijelzés;
ez hitelességi javítás, nem folyamatos kijelzési teljesítménygarancia.
A nagy köríves, képközépre vonatkozó ND-84 távolságdefiníció nem változik.
Ez továbbra sem a renderháromszögeken megtett, relief-túlrajzolt úthossz.

### 3. FlyTo: helyi magasság az egész átmenet alatt

A régi út egyszer mintázta a célhely sugarát, majd a középponttól mért
abszolút távolságot interpolálta. Az út közbeni hegy/víz magassága és
egy közben módosuló világ így csak utólagos ütközési korrekcióként hatott.

Most a kezdő és kért végső **felszín feletti magasság** interpolálódik.
Minden lépés az aktuális irányban mintáz, ehhez adja a kért magasságot,
majd a meglévő közös near/min/max korlátot alkalmazza. Az ismételt azonos
irányú ApplyTransform a meglévő egybejegyzéses cache-t használja.
A kapcsoló kikapcsolásakor megmarad az alapgömb mód. A kézi megszakítás,
szöginterpoláció és smoothstep változatlan. A végpont nulla időtartamú
repülésnél is az új utat használja.

Ez pontmintás magasságtartás, nem teljes hegyoldali/near-plane sarokvédelem.
Gyorsan változó, erősen túlrajzolt terepen a sugárkövetés látható mozgást
okozhat; ezt élőben kell ellenőrizni. A teljes mesh-kameravédelem nem kész.

## Fejlesztői ellenőrzés

- Teljes .NET: **707/707 PASS** (384 Core + 315 viewer + 8 CLI).
- Viewer Release: **315/315 PASS**.
- Solution build: 0 hiba, 0 warning.
- Unity Assembly-CSharp offline fordítás: 0 hiba, 83 meglévő warning.
- Editor-tesztassembly offline fordítás: 0 hiba, 4 meglévő warning.
- 11 új .NET-eset az időfázisokra: korábbi Build-től független időeltolás,
  hosszú főszálas várakozás, single út, frekvencia és mind az öt időrendi él.
- 17 új natív Editor-eset a valódi viewer metódusokra: kontextusváltás,
  azonos nézetű türelem, modell nélküli target, nulla/tükrözött skála,
  FlyTo helyi magasság/skála/új világ/legacy mód/korlátok és coroutine-végpont.
  **Ezek csak fordítottak, nem futottak az élő Editor Test Runnerben.**
  A cache-fixture a bekötést vizsgálja, nem új, teljes világot épít.
- Nincs új Core-algoritmus vagy vektormódosítás; Python-vektorgenerálást
  e viewer-korrekciók miatt nem futtattunk. A másik munkaszál változásai
  megmaradtak. Nincs commit/push.

## Egy közös felhasználói próba

1. Play újraindítása és fordítás után távoli → közeli → távoli zoom,
   majd megállás. A terrain/víz nem tűnhet el; a lépték megállás után
   térjen vissza. Mozgás közbeni rövid `—` most szándékos.
2. A kontinens/régió panel egyik kattintható célpontjára indíts FlyTo-t;
   ismételd eltérő, hegyes/parti helyre. Nézd, követi-e a helyi magasságot,
   nincs-e új erős radiális ugrás. Repülés közben görgővel/húzással vedd
   át a vezérlést: a repülésnek meg kell szakadnia.
3. Deep-time váltás után ismét zoom/FlyTo, majd megállás. A régi lépték
   ne maradjon érvényes az új világra; elkészülés után legyen új érték.
4. Tenger és hegység felett közelíts valóban a minimumig, majd távolodj.
   Ez a korábbi ND-91 nyitott kapuja; az előző log minimuma még 2,88
   egység volt, nem érte el a 0,33-as korlátot. Hegyek melletti levágás
   esetén képernyőkép és lépésleírás szükséges.

Én a friss logban az ND-95 időfázisokat, a Build utáni valódi kéréskort,
az ND-91 magasságot és az ND-75/93 geometria/cache folytonosságát ellenőrzöm.
Az Editor Test Runnerben külön a `NavigationMeasurementTests`,
`TerrainIndexMaskMeshTests`, `ChunkUploadResourceTests` futtatása szükséges.

## Haladás és következő kapu

A három korrekció implementált és fordított; élő vizuális elfogadást
nem jelentünk. A víz/kamera/lépték sor korrekciós része csökkent, de a
célzott élő próbák maradtak: durva maradék **2–4 → 1–3 óra**, az audit
teljes maradéka **12–24 → 11–23 munkaóra**. Ráfordítás-egyenérték,
nem mért idő és nem határidő; e csomag durván 1–3 óra munkának felel meg.
M9 súlyozott állapot kb. **55%**: a kamera és mérés csoportjának teljes
elfogadási szintje még nem változott. A következő aktív kapu a fenti élő
ellenőrzés és a natív regresszió, nem újabb önkényes upload-átalakítás.
Az eredeti élesség/proxy és mozgókamerás selection két halasztott csomag
továbbra is szerepel a maradékban, de nem módosult.
