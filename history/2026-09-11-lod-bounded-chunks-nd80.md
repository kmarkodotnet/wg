# 2026-09-11 — Korlátos renderchunkok, ND-80

A felhasználó az ND-79 után a megjelenítés átalakítását választotta a
kisebb pixelcél azonnali, jelentős költségnövelése helyett. Az első,
külön ellenőrizhető lépés elkészült; nem léptünk tovább a minőségi célra.

Implementáció előtt ND-80 döntés és architektúra §3.15 készült.

- `DynamicMeshChunking.GroupByLeafBudget`: területi kvadfa-csoportosítás,
  256 levél/chunk kemény korlát, korábbi osztások összevonása 128-nál.
  Változatlan cut-levelek; stabil területi TileId-kulcsok, megőrzött
  előző partíció, kooperatív megszakítás. A minimumszint kezdőértéke L6.
- Bekötve a CPU chunk-emisszióba. A régi diff, teljes feloldottpozíció-
  cache, segédrétegek és atomikus alkalmazás megmaradtak. Új kapcsoló:
  `useBoundedDynamicChunks`, alapból igaz; a fix út összehasonlítható.
- Új log: `chunkPacking=ND80`, `chunkMinLevel`, `chunkLeafLimit`, a kész
  csoportokból mért `maxChunkLeaves`, `grouping` idő (az emit részhalmaza).
- A scene, Core, seed, pixelcél, kvóta, morph és vízgeometria nem változott.
  Meglévő Inspector-értékeket nem írtunk felül; új komponensnél L6 az alap.

Bizonyíték:

- 20 új egységteszt. LOD Debug/Release 190/190; Core 381/381, CLI 7/7,
  **578 külön teszteset**, minden sikeres. Az első tesztfutásban egy
  split-teszt tévesen már 256 levélnél osztást várt; javítva valóban
  korlát feletti bemenetre, az algoritmust nem lazítottuk a teszthez.
- Solution build 0 hiba/0 figyelmeztetés. Unity-forrásfordítás 0 hiba,
  83 meglévő figyelmeztetés. Nem élő Editor-vizuális ellenőrzés.
- `--quality` próba 104 állásában pontosan azonos TileId-fedés és
  maximum 256 levél/chunk. Közepes példák: jelenlegi céllal 728→215,
  5 149→475 chunk; sűrűbb stresszbeállítással 23 640→2 072.
- Mély példában 141→278 chunk is előfordul: a túl nagy régi egységet
  darabolja fel a korlát. A cél nem mindenhol a legkisebb objektumszám.
- A tile-onkénti közösél-feloldott geometria azonosságát külön teszt
  ellenőrzi eltérő csomagolás és feldolgozási sorrend mellett.
- `git diff --check` tiszta; Core/referencia/Core-teszt diff üres.
  Python nincs telepítve (`py --list`); KAT/vektorregenerálás nem futott.

Korlátok: nincs mért FPS/draw-call gyorsulás. A csoportosításnak saját
költsége van, a nagyobb csoport több változatlan tile újraemisszióját
okozhatja. A főszálas upload továbbra is egyben fut, a régi inaktív
GameObjectek cache-e nem kapott méretkorlátot. Ezek, a több Core-minta,
a víz és a pixelcél a következő lépések témái maradnak.

Új Play-próbát kérünk azonos zoomúttal, megállásokkal, visszazoommal és
elfordítással. Az ND-80 logban a tényleges chunkméretet, darabszámot,
emit/reuse arányt és teljes upload/kérésidőt vizsgáljuk. Felbontásjavulást
most nem állítunk és nem várunk: ez annak költségoldali előfeltétele.

[Teljes leírás és próbamenet](../docs/reviews/lod-bounded-chunks-nd80-2026-09-11.md).
Nincs commit/push, a korábbi dirty módosítások és `.claude/` megőrizve.
M9 tartalmi becslés továbbra is 60–70%. Durva, nem mért fejlesztői
ráfordítás-egyenérték e lépésre 3–6 óra; további megjelenítési/zoommunka
és validáció 8–20 óra, km-lépték és új Core-részletmodell nélkül.
