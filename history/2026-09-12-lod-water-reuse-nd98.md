# 2026-09-12 — ND-98, a stabil víz-cut ismételt munkájának elhagyása

A felhasználó folytatást kér, kézi ellenőrzése később lesz. Branch
`codex-handoff`, HEAD `99b3ac4`. Az ND-96/97 munkafabeli változásokra
építünk. Nincs új commit/push; a külön Core/reference/CLI munka megmaradt.

## Implementáció

A terepfinomítás álló kamerás hullámai eddig újraszámolták a már stabil
víz-cutot, és csak ezután hagyták ki a víz-emissziót. Az ND-98 előzetes
döntés szerint azonos teljes vetület/LOD-paraméterek és egy teljes
újraszámolással igazolt fixpont után megosztható a víz fedése és rendezett
levélsnapshotja. Az új kérés munkastatisztikái nullázottak. Változó
nézet/paraméter, eltérő forrás/cache, cancellation vagy geometriafeedback
nem bújhat át a gyors úton. A korábbi kiválasztás immutábilis marad.

Új log: `selectionReusePolicy=ND98 reusedSelection=True/False`.
Az emisszió/feltöltés `reusedMesh` mezője ettől külön marad.
Pixelcél 8/7, relief, világmodell és munka-/levélkeretek változatlanok.

## Ellenőrzés

- 18 új regressziós eset; teljes solution **765/765** (384 Core,
  373 viewer, 8 CLI), viewer Release **373/373**.
- Solution build 0 hiba / 0 warning. Unity runtime forrásfordítás
  0 hiba / 83 meglévő warning; Editor-tesztfordítás 0 hiba / 4 warning.
  Ez nem natív Editor-tesztfuttatás. `git diff --check` tiszta.
- Páros Release-próba szintetikus teljes vízgömbön: 120 álló kamerás
  kérésből 96 újrahasznált; összes kiválasztási idő 3622,61→959,74 ms,
  allokáció 815256856→283173504 byte. Pontos fedés-/pending-egyezés.
- 60 mozgókamerás kérésből 0 újrahasznált; azonos 626263208 byte,
  idő 4047,71→4060,85 ms. Nincs elavult nézet újrahasználata.
- Python/KAT nem futott, nincs telepített Python; Core/vektorfájlhoz
  e csomagban nem nyúltunk.

[Részletes mérés és korlátok](../docs/reviews/lod-water-reuse-nd98-2026-09-12.md).
A [közös végső próbalista](../docs/reviews/lod-final-batch-nd96-2026-09-12.md)
frissült a logmezővel. Nem kérünk új köztes kézi kaput. Teljes FPS-,
reakcióidő- és vizuális elfogadást nem állítunk ebből a részfolyamatból.

## Haladás

Súlyozott M9 **61,25%, kb. 61%**: a reakcióidő/erőforrás csoport
offline bizonyítéka bővült, teljes élő kapuja még nincs. A csomag durva
ráfordítás-egyenértéke 1–2 óra; újraértékelt maradék **5–11 óra** a
natív/vizuális mérésekre, teljes felzárkózás vizsgálatára, szükséges
korrekciókra és hosszú erőforráspróbára. Nem mért munkaidő.
