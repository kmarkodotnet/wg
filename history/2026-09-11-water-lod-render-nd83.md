# 2026-09-11 — ND-83, vízfelszín-LOD renderbe kötése

## Kérés és határ

A felhasználó az ND-82 kiválasztási kapu után folytatást kért. Ebben a
lépésben a víz saját finomítása kerül a rendererbe. Az első zoomok
beragadása/küszöbe és a több frame-es upload marad a korábbi backlogban.
Induláskor `codex-handoff`, HEAD `0f9cd4b` (`tile division`), tiszta munkafa.
Munka közben másik feladatból deep-time gomb- és ND-84 léptékmódosítások
jelentek meg; ezeket megőriztük, nem ennek a lépésnek az eredményei.
Commit/push nem történt.

## Implementáció

- Kód előtti ND-83 döntés, architektúra 3.17 frissítve.
- `PlanetGridMesh.WaterRefinement.cs`: statikus víz-emisszióból forrás és
  ritka indexmaszk; külön 8192 levél/256 split keret, meglévő single-flight.
- Modellmagasságból jövő nyers vízszín, 65 536 elemig cache, azonos cutnál
  teljes vízmesh-reuse. Pozíció/RGB közösél-illesztés ugyanazzal a resolverrel.
- Terrain-vezérelt víz kizárása a statikus vízmaszk alatt, száraz base
  finom parti vizének és külön tavaknak megtartása.
- Két váltott vízobjektum és külön statikus maszk; hiba esetén statikus
  fallback. Build nem másolja a régi layout indexeit az új vízmesh-be;
  a korábbi dinamikus vízobjektumok a világváltáskor kikapcsolódnak.
- CPU szinkron/async finomítás újraindul a víz halasztott osztásaihoz is.
  GPU/nem támogatott nézet visszaáll a régi vízútra.
- ND-83 részidők/levélszám/cache és ND-75 tényleges geometriamérés frissítve.
  A maszkolt statikus vízquad nem szerepel a kirajzolt méretek között.
- Nincs Core-, seed-, scene- vagy küszöbmódosítás ebből a feladatból.

## Ellenőrzés

- `dotnet build WorldGen.sln --no-restore -p:_EnableDefaultWindowsPlatform=false`:
  0 hiba, 0 warning.
- Teljes `dotnet test WorldGen.sln --no-restore ...`: **633/633 sikeres**
  (381 Core, 245 viewer-LOD, 7 CLI). A LOD-szám már tartalmazza a párhuzamos
  másik feladat 9 léptéktesztjét; e feladat saját növekménye **7 teszteset**.
- LOD Release: **245/245 sikeres**; külön új víz-szerződéstesztek: **7/7**.
- `Assembly-CSharp` forrásfordítás az ignored validációs targettel:
  0 hiba, 83 meglévő nullable warning.
- `WorldGen.Viewer.Lod.Tests` Editor-tesztassembly fordítása: 0 hiba,
  4 csomag/assembly-verzió warning. Az új valódi Mesh-maszkteszt **nem futott**,
  ahhoz Unity Editor Test Runner kell.
- `git diff --check`: tiszta. Python KAT/vektor-regenerálás nem futott:
  `py -0p` szerint nincs telepített Python. Core numerika nem változott.

Nincs élő Play-/képernyőkép-/FPS-bizonyíték. Az uploadhibás visszaállítás
vezérlési útja kódellenőrzött, nem Unityben hibainjektálással mért eredmény.
Ez implementált, próbára átadott lépés, nem vizuálisan elfogadott víz-LOD.

## Következő kapu

[Próbaútvonal és logmezők](../docs/reviews/water-lod-render-nd83-2026-09-11.md):
óceán/part/tó, zoom/visszazoom, világ- és overlay-váltás. Először ezek élő
visszajelzése; nem indul újabb feladat automatikusan.

M9 tartalmilag durván 60–70%; e lépés ráfordítás-egyenértéke 3–5 óra,
víz-validáció/korrekció még 1–3 óra, fennmaradó zoom/render munka 8–20 óra.
Durva becslések, nem mért időadatok.
