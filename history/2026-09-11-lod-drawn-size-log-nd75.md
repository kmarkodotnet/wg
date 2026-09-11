# 2026-09-11 — Zoom és tényleges kirajzolt tile-méret, ND-75

A felhasználó ND-74 után közepes zoomnál elmaradó finomodást, mélyen
ismét frissülő, de elégtelen részletességet jelez. Kifejezetten a valódi
kirajzolt tile-méret naplózását kérte, új javítás helyett; a következő
döntés az ő próbája és a log közös értelmezése után következik.

ND-75 és architektúra §3.12 előzte meg az új diagnosztikát.
`PlanetGridMesh.RenderDiagnostics.cs` partial: sikeresen feltöltött
végleges vertex-/indexlisták követése (statikus/dinamikus terrain/water),
position-only frissítéssel és másolt statikus maszk-snapshottal.
Az aktuális kamera vetítése és aktív rendererállapota a méréskori snapshot;
nem a kért cut, a terrain-proxy vagy új modellminták adják a méretet.

`RenderedTileDiagnostics`: Unity-független 17×9-es ritka raster/mélységteszt,
hat frustumsík szerinti clipping, tényleges indexwinding és cull-mód.
Középső szélesség/magasság/átmérő, mintapontos p50/p90/max és 9 soros
mérettérkép; terrain és water elkülönítve. A képernyőponthoz elöl álló tile
vetített kiterjedését mérjük, nem minden látható tile globális maximumát.
Nincs UI/felhő/marker/TAA/blend vagy GPU-pixel-visszaolvasás.

Log: aktuális távolság, alapgömb feletti magasság, R/(d−R) zoomarány,
meglévő nézetosztály, FOV/viewport, frame/idő/revízió, LOD-worker állapota,
alkalmazott cut kamerájának eltérése és a diagnosztika saját ideje.
Legfeljebb egy mérőworker, maximum 1 Hz; LOD-kérés közben is mér.
A main-thread/worker diagnosztikai hiba explicit loggal letiltja a mérést,
nem befolyásolja a finomítási döntést. A logkapcsoló kódban és scene-ben be.

[Mérési definíció, mezőjegyzék és felhasználói próba](../docs/reviews/lod-drawn-size-log-nd75-2026-09-11.md).

Ellenőrzés:

- Solution build: 0 hiba / 0 figyelmeztetés.
- Core 381/381, CLI 7/7, LOD Debug 125/125 a solution-futásban;
  LOD Release 125/125 külön. Összesen 513 külön teszteset, 15 új diagnosztikai eset.
- Unity Assembly-CSharp + LOD fordítás: 0 hiba / 83 meglévő figyelmeztetés.
  A két új forrásnak saját meta van, az ignorált validation targets a
  generált projektek fordításához kiegészült. Élő Editor-teszt nem történt.
- Önálló `--drawn` próba: 409 600 quad, 153 találatos pont, 64,79 / 56,40 /
  57,16 ms. A ciklus allokációja 40 byte (Stopwatch), nincs per-quad allokáció.
  Ez csak a tiszta mérőmag, nem a Unity capture/vetítés/fájlírás teljes költsége.
- `git diff --check` tiszta. Nincs Core-, seed-, referencia-, tesztvektor-,
  LOD-, kamera-, morph- vagy budgetváltoztatás; Python referencia/KAT nem futott
  (nincs telepített Python). Commit/push nem történt.

Készültség: a log implementált, valós futása a felhasználó próbájára vár;
a zoomhiba nem megoldott. M9 tartalmilag súlyozott becslése 60–70%,
a diagnosztika durva fejlesztői ráfordítás-egyenértéke 3–5 óra.
A korábbi 12–28 órás hátralévő zoommunka az új mérésig bizonytalan;
ezek nem mért idők, és nem tartalmaznak km-UI-t/új Core-részletmodellt.
