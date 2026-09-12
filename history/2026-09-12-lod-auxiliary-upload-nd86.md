# 2026-09-12 — ND-86, víz-/határvonal-staging

## Kérés és kiindulás

A felhasználó folytatást kért a következő tile-division lépésen. A friss
`PerfLog_20260912_013133.txt` ND-85 adatai: 86 commit, 284 szelet,
szelet p90 1,29 ms/max 3,65 ms; commit p90 5,62 ms/max 15,83 ms.
A leglassabb commit vízszakasza 10,71 ms, de ez maszkot is tartalmazott.
Ennek következő kapuja a víz-/border-upload előkészítése és külön mérés.
A korai zoompanasz/küszöb/selection halasztva maradt.

Kiindulás: `codex-handoff`, `0f9cd4b`, meglévő dirty ND-82–85, lépték- és
deep-time módosításokkal. Ezeket és az új bolygóötlet-dokumentumot megőriztük.
Nem készült commit/push, scene-módosítás vagy Core-/seed-változtatás.

## Megvalósítás

Kód előtt ND-86 döntés. Új `PlanetGridMesh.AuxiliaryUpload` partial: rendezett
vízbucket-csomagolás és bounds a workerben, leválasztott mesh-tartalékok,
víz/border stage/publish. Az ND-85 sor immár `Action` munkákat kezel;
terep után a parti víz, szükség esetén az önálló víz, majd a border következik.
Üres réteg csak a közös commitkor rejt el; változatlan önálló víz nem töltődik.
Meglévő megszakítás/fallback megmarad. Egy-egy aux-spare újrahasználható,
Build/OnDestroy felszabadítja; a fallback border-anyag OnDestroyig él.

Új aux-pack/stage és commit-részidők, külön vízmaszk-idő; a régi `upload`
mező továbbra is teljes publikálás, nem tisztán GPU-másolás. Egy nagy aux-mesh
monolitikus marad: nincs 2 ms-os teljes frame-garancia. Referenciaváltás,
maszk, aktiválás és eviction továbbra is közös commitban történik.

## Ellenőrzések

- Solution build: 0 hiba, 0 warning.
- Teljes solution teszt: **659/659 PASS** = 381 Core + 271 viewer-LOD + 7 CLI.
- Viewer-LOD Release: **271/271 PASS**.
- Célzott `LodUploadBatchTests`: **19/19 PASS**, öt új vegyesréteg-eset.
- Unity `Assembly-CSharp` offline fordítás, ignored validációs targettel:
  0 hiba, 83 meglévő nullable warning. Az új partial a targetbe bekerült.
- Editor LOD-tesztassembly: 0 hiba, 4 meglévő csomag/assembly warning.
  Új, reflectionnel a tényleges worker-csomagolást hívó teszt: bucket-sorrend,
  üres bucket, attribútumok, indexeltolás, bounds, bemenet érintetlensége.
  **Csak fordított, Editorban nem futott.**
- `git diff --check`: tiszta. Python KAT/vektor-regenerálás nem futott:
  nincs telepített Python; a Core és a referencia változatlan.

## Átadás

Architektúra 3.19, backlog, M9 és az ND-85 review frissítve.
[Részletes mérés és próbamenet](../docs/reviews/lod-auxiliary-upload-nd86-2026-09-12.md).
Új élő Play-próba kell vízzel, parttal, borderrel, zoom/visszazoommal és
staging közbeni világváltással. Az offline fordítás nem Unity-vizuális vagy
FPS-elfogadás; az ND-85 régi logja nem igazolja az ND-86 gyorsulását.

M9 tartalmilag durván 60–70%; e lépés ráfordítás-egyenértéke 3–5 óra,
élő validáció/korrekció további 1–3 óra, fennmaradó zoom/render munka
8–20 óra. Durva becslések, nem mért munkaidők.
