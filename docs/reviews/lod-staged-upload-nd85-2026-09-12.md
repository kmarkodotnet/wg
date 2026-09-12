# ND-85 — Több frame-es terep-upload, első kapu

**Folytatás:** az első élő log kiértékelése és az implementált víz-/határvonal-
staging az [ND-86 átadásban](lod-auxiliary-upload-nd86-2026-09-12.md) található.
Az alábbi leírás az első kapu történeti állapota.

## Elkészült változás

A CPU async, chunkolt terepút most több Update-ban töltheti fel a kész
workereredményt. A munkasor puha időkerete 2 ms és legfeljebb 64 mesh/frame.
Minden frame legalább egy feltöltést elvégez, mert egy natív Unity-hívást
nem lehet közben felfüggeszteni. A 2 ms tehát nem garantált felső korlát.

A feltöltések láthatatlan, rendererhez nem kötött tartalék Mesh-ekbe mennek.
A ténylegesen rajzolt pozíciók, színek, maszk és a diagnosztika közben a
korábbi konzisztens felszínt mutatják. Az utolsó mesh után külön frame
váltja át a kész terepet és aux-rétegeket. Az aktuális cut és cache csak
sikeres alkalmazás után változik. Nincs részleges finomításból eredő
szándékos durva/finom keverés a staging-frame-ekben.

Világ-/Inspector-konfigurációváltás, explicit Build vagy kikapcsolás
eldobja a félkész munkát. A kamera mozgása önmagában nem dobja el: ezzel
elkerüljük a soha be nem fejeződő feltöltést; utána az új kamera kérésére
frissítünk. Új worker a staging alatt nem indul. Staginghiba esetén a régi
kép megmarad, commit-hibánál a meglévő statikus fallback lép életbe.

Az új kapcsoló `Use Staged Terrain Upload`, alapból bekapcsolt. Nem írtuk
át a scene-t. Szinkron, GPU és nem chunkolt módban a régi upload működik.
A víz-LOD, a tile-kiválasztás, a geomorph és a pixelküszöb nem változott.

## Várható hatás és ár

Csökkenhet a sok terepchunk egyszerre történő feltöltésének frame-tüskéje.
Ez **nem mért FPS-javulás**, hanem a szerkezet célja. Nem csökkenti a
selection/balance/emit hátterezett számítását, és nem élesít korábban.

Több frame késés és több memória az ára: a meglévő chunk-cache minden
érintett kulcsához még egy Mesh kerülhet. A korábbi mesh a következő kérés
tartaléka lesz; Build és megszűnés felszabadítja a tartalékokat. Ez nem oldja
meg a meglévő chunk-cache általános memóriakorlátját. Position-only változás
is teljes tartalékfeltöltést kér, így az összes másolt adat mennyisége nőhet.

**Még egyetlen commit-frame-ben marad:** víz/border feltöltés, meshreferenciák
és anyagok cseréje, aktiválás, statikus indexmaszkok és cache-eviction.
Ezért az új út nem garantálja a teljes commit vagy frame 2 ms alatti idejét.
Ezek további bontását az élő részidők alapján kell kiválasztani.

## Próba és log

1. Új Play: kontinens/part fölött zoom → megállás → visszazoom; oldalirányú
   mozgatás is. Várakozás közben maradjon összefüggő a régi felszín.
2. A kész finomítás váltásakor ne legyen lyuk vagy villanó dupla réteg.
3. Feltöltés közben deep-time-váltás/Build, majd új zoom: ne jelenjen meg a
   régi világ félkész geometriája.
4. Opcionális A/B: azonos nézet, az új kapcsoló ki/be; ne az összes indulási
   költséget hasonlítsuk egyetlen frame tüskéjéhez.

Új mezők:

- `[ND-85 upload begin]`: a feltöltendő terepmesh-ek száma és kerete.
- `[ND-85 upload slice]`: adott frame meshes/done/elapsed; `published=False`.
- `[async apply ND-76]`: `uploadMode=ND85`, `stageFrames`, `stageTotal`,
  `maxSlice`, `stagedVertices`; a `mesh-feltoltes` immár csak a commit ideje.
- `[ND-75 drawn]`: `uploadPending`, `uploadStaged`; a méretstatisztika továbbra
  is kizárólag a ténylegesen publikált, látható mesh-ekből készül.
- `[ND-85 upload discarded]`: félbehagyott előkészítés vagy külön jelzett
  commit-kísérlet; a kettő nem ugyanaz a hibafázis.

14 új parancssori ütemezési teszteset és egy új, valódi MeshFiltert használó
Editor-teszt készült. Az Editor-teszt futtatása és a Play-eredmény még nyitott.
Részletes build/regresszió: [munkanapló](../../history/2026-09-12-lod-staged-upload-nd85.md).

M9 durván 60–70%. E lépés ráfordítás-egyenértéke 3–5 óra, élő validáció és
korrekció további 1–3 óra; teljes fennmaradó zoom/render munka 8–20 óra.
Durva becslések, nem mért időadatok. Az első zoomok halasztott panasza nyitott.
