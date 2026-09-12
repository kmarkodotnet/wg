# 2026-09-12 — ND-85, több frame-es terepfeltöltés

## Kérés és scope

A felhasználó a fordítási hiba ellenőrzése után a következő tile-division
feladatot kérte. A backlog szerinti következő lépés a több frame-es upload.
Ennek első kapuja készült el: a CPU async, chunkolt terep feltöltésének
ütemezése, teljes fedésváltással. A víz/border és commit további bontása
még nincs kész. A korai zoompanasz, selection-költség, pixelcél és Core
változatlan; a másik szál lépték/deep-time módosításait megőriztük.
Kiindulás: `codex-handoff`, `0f9cd4b`, a korábbi munkák dirty állapotával.
Commit/push nem történt, scene-t nem szerkesztettünk.

## Megvalósítás

- Kód előtti ND-85 döntés, architektúra 3.18 és backlog/M9 frissítés.
- `LodUploadBatch<T>`: másolt munkasor, 2 ms puha/64 elem keret, legalább egy
  munka garantált előrehaladása. A teljes staging előtt nincs commit;
  commit legfeljebb egyszer, hibás/megszakított sor nem folytatható.
- `PlanetGridMesh.Upload` partial: rendererhez nem kötött tartalék mesh-ek,
  több Update-os staging, külön commit-frame. Pozíciófrissítés is teljes
  tartalékfeltöltés, ezért a régi mesh érintetlen marad, de nőhet az összmunka.
- A staging alatt nincs új worker; világ-/konfigurációváltás, Build,
  kikapcsolás eldobja. Kameramozgás önmagában nem éhezteti ki.
- A régi mesh újrahasználható tartalék lesz, chunkonként legfeljebb egy.
  A meglévő chunk-cache általános memóriakorlátja továbbra sincs megoldva.
  Build/OnDestroy felszabadítja a tartalékokat.
- Sikeres alkalmazás után publikálódik a cut/cache/kamera/diagnosztika.
  Staginghiba régi képet hagy, commit-hiba meglévő statikus fallback;
  a következő kérés egylépéses feltöltésre tér vissza.
- `useStagedTerrainUpload` alapból igaz. Szinkron/GPU/nem chunkolt út régi.
- ND-85 begin/slice/discard és ND-76 stageFrames/stageTotal/maxSlice/
  stagedVertices; ND-75 uploadPending/uploadStaged, csak publikált geometria.

## Ellenőrzés

- Solution build: **0 hiba, 0 warning**.
- Teljes solution teszt: **652/652 PASS** = 381 Core + 264 viewer-LOD + 7 CLI.
- Viewer-LOD Release: **264/264 PASS**.
- Új célzott ütemezési tesztek: **14/14 PASS**. Idő-/darabkeret, túl drága
  egyetlen mesh, részeredmény láthatatlansága, megszakítás, hibás staging,
  egyszeri/reentráns/hibás commit, bemenetmásolás, paramétervalidáció.
- `Assembly-CSharp` teljes generált projekt + új fájlokat felvevő ignored
  validációs target: **0 hiba, 83 meglévő nullable warning**. A generált
  projekt a víz-LOD és a másik szál lépték-partial fájljait is tartalmazza.
- Editor LOD-tesztassembly: **0 hiba, 4 csomag/assembly-verzió warning**.
  Új valódi MeshFilter-teszt készült: a staging nem változtatja a korábbi
  meshreferenciát/vertexet; a teljes sor után a commit cseréli. **Nem futott
  Editorban**, a parancssori fordítás nem helyettesíti a futtatását.
- `git diff --check`: tiszta. Core/Python referencia változatlan, KAT és
  vektor-regenerálás nem futott (ebben a környezetben nincs telepített Python).

Nincs élő Unity-vizuális/FPS-elfogadás vagy runtime hibainjektálási bizonyíték.
A C# tesztek a munkasor állapotgépét igazolják, nem a teljes Unity-integrációt.

## Átadás

[Próbalista és mérési korlátok](../docs/reviews/lod-staged-upload-nd85-2026-09-12.md).
Kontinens/part zoom → megállás → visszazoom, oldalirányú mozgás, staging
közbeni világváltás. Az elsődleges nyitott költség a végső commit:
víz/border upload, aktiválás/új renderobjektum, maszk és eviction továbbra
is ott történik. A 2 ms csak puha staging-keret, nem teljes frame-garancia.

M9 tartalmilag durván 60–70%. E lépés ráfordítás-egyenértéke 3–5 óra,
élő validáció/korrekció további 1–3 óra, a teljes fennmaradó zoom/render
munka 8–20 óra. Durva becslések, nem mért munkaidő.
