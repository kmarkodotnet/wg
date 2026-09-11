# ND-80 — Korlátos méretű renderchunkok, első átalakítás

2026-09-11. **Implementált; az első élő próba logja kiértékelve.**
[Élő eredmény](lod-bounded-chunks-live-2026-09-11.md): a 256-os korlát
teljesül, a feltöltési csúcsok kedvezőbbek; a több másodperces háttér-
finomítás megmaradt. Kifejezett vizuális acceptance még nincs.
A felhasználó a megjelenítés előzetes átalakítását választotta, nem az
azonnali kisebb pixelcélt a nagyobb költség vállalásával.

## Mi változott?

A CPU-terep teljes fedése és globális közösél-feloldása után a levelek
nem kizárólag egy fix L8-as őshöz kerülnek. A `dynamicChunkLevel` most
a legdurvább engedett csoportgyökér (a scene meglévő értéke 6).
256 levél felett a csoport a kvadfa gyermekterületeire oszlik, szükség
esetén több szinten. Egyetlen tereplevelet sem oszt a csomagoló.

A korábban osztott csoport csak 128 levélnél vagy az alatt vonódhat
össze. Ez csökkenti a küszöb körüli ismételt chunkcserét. A chunk kulcsa
a területi TileId: a meglévő diff, geometriai cache és feltöltés tovább
használható. Minden cut-levél pontosan egy csoportban szerepel.

Az új partíció külön épül, az előzőt a worker csak olvassa. A szülő/
gyermek chunkok cseréje a meglévő egy-frame-es alkalmazásban történik;
az eltűnt kulcsok kikapcsolódnak. Megszakított kérés nem publikál új
partíciót. A feloldott csúcspozíciók továbbra is részei a cache-egyezésnek.

Az új `useBoundedDynamicChunks` kapcsoló alapból igaz. Kikapcsolva
megmarad a korábbi fix csoportosítás az eredeti base-szintű alsó korláttal.
Az új komponensek `dynamicChunkLevel` alapja 6; a korábban szerializált
Inspector-értékeket nem írjuk át. A scene fájl ebben a lépésben nem változott.

**Nem változott:** 12 px-es mély és 10 px-es első osztási cél, kvóta,
cut-levélhalmaz, modellek, seed, morph, közösél-feloldás, vízgeometria.
Ez a sűrűbb terep előfeltétele, önmagában nem korábbi zoomélesedés.

## Mérési bizonyíték

Az ND-79 `--quality` próbáját kiegészítettük a tényleges teljes fedés
régi és új csoportosításával. 2 logbeli irány × 13 állás × 4 pixelcél:
mind a **104 esetben azonos TileId-halmaz, egyszeres fedés, maximum
256 levél/chunk**. Minden cél/irány saját partíció-előzményt visz tovább.

| Nézet / távolság | Pixelcél mély/base | Tereplevelek | Régi → új chunkok | Új maximum |
|---|---|---:|---:|---:|
| 1 / 160,239 | 12/10, aktív minőség | 2 930 | 728 → 215 | 64 |
| 1 / 127,067 | 12/10, aktív minőség | 22 411 | 5 149 → 475 | 166 |
| 1 / 160,239 | 6/5, csak stresszpróba | 96 969 | 23 640 → 2 072 | 151 |
| 1 / 103,663 | 6/5, csak stresszpróba | 57 115 | 3 208 → 734 | 256 |
| 2 / 103,663 | 12/10, aktív minőség | 48 426 | 141 → 278 | 256 |
| 2 / 103,663 | 6/5, csak stresszpróba | 198 485 | 3 209 → 1 354 | 256 |

Mélyen tehát a chunkok száma **nőhet is**: a korábbi túlméretes csoportot
fel kell darabolni. Nem állítunk minden nézetben kevesebb objektumot.
A kisebb pixeles stresszbeállítások továbbra sincsenek bekapcsolva a viewerben.

A csomagolás ideje függ a levélszámtól, mélységtől, előzménytől és a
futási környezettől; a mérésekben ~0,2–39 ms is előfordult. Tesztek is
futottak párhuzamosan, ezért ezek nem kontrollált sebességígéretek.
A stabil eredmény a csoportszám, fedés és méretkorlát. **Nincs mért FPS-
vagy GPU draw-call nyereség**, nincs ebben Unity-emisszió és feltöltés.

Futtatás:

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj -c Release --no-restore -p:_EnableDefaultWindowsPlatform=false -- --quality
```

## Tesztek és korlátok

20 új parancssori eset: üres/szekély/mély cut, 1/4/64/256-os korlát,
split/merge-hiszterézis, diffben eltűnő szülő/gyermek, érintetlen másik
terület, bemeneti sorrend/párhuzamosság, korábbi állapot megőrzése,
visszadurvuló cut, lapok/max LOD, duplikátum, megszakítás, hibás paraméter,
minimumszint hatása, teljes fedés és tile-onként azonos feloldott geometria.

LOD Debug/Release: 190/190. Unity-forrásfordítás: 0 hiba, 83 meglévő
figyelmeztetés. Ez nem Editorban végzett vizuális vagy scene-validáció.

Nyitott költségek:

- Az összevont chunkban egy helyi változás több változatlan levél újraemisszióját
  okozhatja; az `emittedLeaves/reusedLeaves` és az összidő dönt majd.
- A 256 egy chunk leveleinek korlátja, nem egy kérésé, milliszekundumé vagy
  teljes memóriahasználatáé. Anyagbontás miatt több draw call is tartozhat hozzá.
- A főszálas upload még egyben fut. A régi, inaktív GameObjectek cache-e
  továbbra is megmarad; korlátos objektumpool nincs ebben a lépésben.
- A több modellminta, osztási hullám, vízfinomítás és korábbi élesedés
  problémáját nem oldja meg önmagában a csomagolás.

## Következő felhasználói próba

Új Play, ugyanaz a zoomút, közepes és mély nézetben néhány másodperces
megállással, majd visszazoom és elfordítás. A kép részletessége most
nem kell hogy nőjön; eltűnő folt, rés vagy duplán rajzolt terület nem jelenhet meg.

Az apply-log új mezői:

```text
chunkPacking=ND80 chunkMinLevel=6 chunkLeafLimit=256 maxChunkLeaves=... grouping=...ms
```

A `maxChunkLeaves` a kész csoportokból számolt maximum, nem a beállított
cél visszamondása. A `grouping` az `emit` idő részhalmaza, nem adandó hozzá
még egyszer. A `changedChunks` nevezője a teljes új csoportszám.
Ha `chunkMinLevel` nagyobb 6-nál, az Inspectorban megőrzött egyéni beállítás
aktív; ezt figyelembe kell venni az összehasonlításkor.

Az új log alapján döntünk az emisszió/upload következő lépéséről. A
pixelcélhoz csak a költség újramérése után nyúlunk.
