# ND-81 — Ismételt LOD-metrikaszámítás csökkentése

## Kiindulás és hatókör

A felhasználó az ND-80 második élő logelemzése után engedélyezte a következő
javítást. A `PerfLog_20260911_223119.txt` kiemelt, 130-as kameraállásában
31 946 levélből 31 882 újrahasznosult, de a cut 323,68 ms, az emit szakasz
188,82 ms volt. Az emit teljes előkészítést/ellenőrzést is tartalmazott.

Ez a lépés a változatlan kamera alatt újra és újra futó vetületmérést
csökkenti. Nem változik a 12/10 px cél, az 1024 új split/kérés, a 200 000-es
budget, a chunkcsomagolás, a terep/víz geometriája vagy a Core/seed.
A teljes finomítás késleltetése és a késői élesedés nincs megoldottnak jelölve.

## Implementáció

- `LodTerrainEvaluationCache`: TileId-hoz a ténylegesen kiszámolt láthatóságot
  és szöghibát tárolja. Pontosan azonos nézet és ugyanaz az immutábilis proxy
  esetén használható újra, legfeljebb 262 144 bejegyzéssel. Telített cache
  mellett a számítás folytatódik, az eredmény nem csonkul.
- Kamera-/vetületváltozás, proxycsere és Build érvénytelenít. Nem közelítő
  kameraküszöbbel dolgozik. A viewport pixelcéljának változása nem teszi
  hibássá a szögmetrikát: az új küszöb szerinti döntést minden kérés újrafuttatja.
- A prioritási sor, hiszterézis, kvóta, kizárás, trace és balance nem kerül
  cache-be. A geomorph ugyanazt a metrikát használja a workerben; a külön
  képernyődiagnosztika továbbra is a megosztható, immutábilis view-t olvassa.
- Nem tárolunk kérésközi közösél-feloldást: a coverage változását még az
  azonos levelű/pozíciójú chunkoknál is ellenőrizni kell.

## Offline páros mérés

Parancs: `dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj
--no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --work-cache`.

A diagnosztika a meglévő referencia-világból előállított L8 proxyval és
óceánkizárással fut, `(52.815,-79.658,88.120)` irányban, 1238×688-as vetülettel.
Mindkét út pontosan ugyanazt az előző cutot kapja. A futtatási sorrend
körönként váltakozik. Mindhárom távolság üres cutból indul, majd a befejezés
után még egy változatlan kört végez: ez **nem az élő kameraút visszajátszása**.

| Távolság | Körök, záró ismétléssel | Összes régi cut-idő | Cache-es cut-idő | Csökkenés | Kihagyott / elvégzett metrikaszámítás |
|---|---:|---:|---:|---:|---:|
| 130,000 | 9 | 904,99 ms | 582,34 ms | 35,7% | 871 472 / 122 976 |
| 122,161 | 14 | 1072,38 ms | 679,77 ms | 36,6% | 1 371 543 / 129 651 |
| 108,152 | 17 | 1614,02 ms | 1170,09 ms | 27,5% | 1 595 256 / 129 471 |

Mind a **40 párban azonos** a kiegyensúlyozott tile-készlet, az új/halasztott
osztások száma, a trace mérete és a végső levelekhez tartozó trace.
A záró állókamerás körökben nulla új metrikaszámítás maradt. A próbában
a cache nem érte el a korlátját. Egy futás időadatai, JIT/GC/ütemezés
befolyásolhatja őket; nincs render, geometria-emit vagy Unity-upload a mérésben.
Ebből nem következik 27–37%-os FPS- vagy teljeskérés-gyorsulás.

Mélyen a megmaradó balance-költség jelentős: a 108,152-es utolsó körben a
cache-es selection 34,72 ms, balance 80,98 ms. A cache ennek az algoritmusnak
a munkáját nem csökkenti. A teljes saroklista, coverage/resolver és aux-rétegek
kezelése is külön további optimalizálási feladat.

## Az új élő log értelmezése

Az `[async apply ND-76]` sorban a `workCache=ND81` jelzi az új verziót.

- `selection` + `balance`: a `cut` két belső részideje; ne adjuk még egyszer
  a teljes cut-időhöz.
- `metricHits` / `metricComputed`: kizárólag a **kiválasztásban** cache-ből
  olvasott / újonnan kiszámolt metrikák. Nem tile-célok vagy renderelt méretek.
- `metricEntries`: a cache elemszáma az emit után, tehát a geomorph által
  esetleg hozzáadott szülőket is tartalmazza.
- `resolveCheck`: rendezés, közösél-feloldás, pozíció- és cache-ellenőrzés;
  `auxiliaryCopy`: víz/border bufferek összefűzése;
  `tileEmit`: az új chunk buffereinek létrehozása, emissziója és konkatenálása.
  Ezek a chunkolt CPU-út `emit` idejének részhalmazai, nem külön hozzáadandó
  idők. A nem chunkolt út ezeket nullán hagyja. A `corners` továbbra is külön,
  emit előtti előkészítés. A főszálas feltöltés külön mérés marad.

A korábbi `[drawn ...]` mérések továbbra is a feltöltött, kirajzolásra használt
geometriából származnak; ezen a diagnosztikán nem változtattunk.

## Ellenőrzés és következő próba

15 új teszteset: pontos metrika hideg/meleg/telített cache-ben, nyolc
kameraparaméter-változás, proxyazonosság, változó küszöb/kvóta és zoom melletti
cut/trace egyezés, megszakítás után megmaradó tiszta metrikák. A trace-egyezés
unit tesztje a teljes L8 lekérdezési rácson ellenőrzi a megálló ősöket.

- LOD: **205/205 Debug és Release**.
- Solution build: **0 hiba, 0 figyelmeztetés**.
- Unity-forrásfordítás: **0 hiba, 83 meglévő figyelmeztetés**; ez nem élő
  Editor/scene/Inspector vagy vizuális/performance validáció.
- Teljes solution regresszió: **593/593** (381 Core + 205 LOD + 7 CLI).
- Python KAT és vektor-regenerálás nem futtatható telepített Python nélkül;
  Core- és numerikus viselkedést nem módosítottunk.

Próba: távolról közepesre, majd mélyre zoom; mindkét helyen legalább
8–10 másodperc állás, végül visszazoom. Azonos kamera mellett magas
`metricHits` és a korábbinál kisebb selection-idő várható. Mozgáskor a cache
újraépül, ott nem ígérünk ugyanekkora nyereséget. Figyelendő a teljes
`requestAge`, a balance/resolveCheck/corners dominancia, valamint a változatlan
kirajzolt tile-méret és a varratmentesség. Új log alapján választunk további
egy lépést; a kisebb pixelcél még nincs bekapcsolva.

M9 tartalmi becslés: **60–70%**. Durva, nem mért ráfordítás-egyenérték erre a
lépésre **2–4 óra**; további megjelenítés/zoommunka és validáció **8–20 óra**.
