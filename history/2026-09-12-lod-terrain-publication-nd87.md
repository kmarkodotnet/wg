# 2026-09-12 — ND-87, terep-rendercél előkészítés

## Kérés és megoldás

A felhasználó jóváhagyta az ND-86 log után javasolt következő upload-lépést.
A 6,30 ms-os tereppublikálás belső okát még nem mértük külön: célja az
előkészíthető munka leválasztása és a maradék részletes naplózása.
Kód előtt ND-87 döntés készült; Core/seed/küszöb/selection nem változott.

A terep staging-job új cél esetén inaktív GameObjectet és komponenseket
készít, meglévő esetén csak referenciát vesz át. Az előkészített rekord
MeshFilter/MeshRenderer és DrawnSurface referenciát is tárol. A staging
nem írja a régi mesh-/anyagreferenciát, aktivitást vagy diagnosztikai térképet.
Commitkor a kész referenciák váltanak, a diagnosztika publikálódik és a
szükséges célok aktiválódnak. Az új célok a meglévő cache tulajdonába kerülnek;
commit előtti eldobás csak az adott kérés új céljait törli, a régi cache marad.
Commit-hiba továbbra is statikus fedésre áll vissza.

Új mezők: terrainPipeline=ND87, stageMesh/stageTarget, newTargets/reusedTargets,
terrainSwap/terrainDiagnostic/terrainActivate/terrainDeactivate. A korábbi
auxPipeline=ND86 és összesített időmérések megmaradnak. A begin/slice pipeline
ND87, a darabkorlát naplóneve maxJobsPerFrame. A maszk és teljes fedésváltás
továbbra is egy közös commit. Ez nem általános pool vagy teljes frame-keret.

## Ellenőrzés

- Solution build: 0 hiba, 0 warning.
- .NET solution: **659/659 PASS** (381 Core + 271 viewer-LOD + 7 CLI).
- Viewer-LOD Release: **271/271 PASS**.
- Unity Assembly-CSharp offline fordítás: 0 hiba, 83 meglévő warning.
- Editor LOD-tesztassembly: 0 hiba, 4 meglévő warning. Négy új eset a valódi
  stage/publish/discard metódusokat hívja: új/meglévő cél × commit/eldobás.
  Régi mesh/vertex/aktivitás megőrzése, korai diagnosztikapublikálás tilalma,
  új objektum inaktivitása és megszakításos takarítása. **Editorban nem
  futottak**, a fordítás nem bizonyítja a Unity runtime-integrációt.
- Az első Unity offline parancsból hiányzott a korábban is szükséges
  TreatWarningsAsErrors=false felülírás: a meglévő nullable figyelmeztetések
  hibává váltak. A megfelelő parancssori kapcsolóval fordítva a korábbi
  warningszám maradt. Repo-projektbeállítást nem enyhítettünk.
- `git diff --check` tiszta. Python KAT/regenerálás nem futott, a Python-
  környezet korábban hiányzott; referencia/numerikus mag nem változott.

## Átadás

[Próbamenet és mérési korlátok](../docs/reviews/lod-terrain-publication-nd87-2026-09-12.md).
Új élő zoom/visszazoom és új területre forgatás, visszatérés, világváltás
kell. Objektumkészítés mennyisége, commit, staging-csúcs és teljes requestAge
együtt értékelendő. A korai élesedés és a proxy-/geometriaeltérés backlogon
marad; a ~669 ms-os teljes kéréskésést ez a lépés nem célozza közvetlenül.

Branch/HEAD változatlan: codex-handoff / 0f9cd4b. A meglévő dirty ND-82–86,
lépték-, deep-time és bolygóötlet-munkát megőriztük. Nincs commit/push,
scene-szerkesztés vagy élő Unity/FPS-elfogadás. Architektúra 3.20, backlog
és milestone frissítve. M9 tartalmilag durván 60–70%; e lépés 2–4 óra
ráfordítás-egyenérték, élő ellenőrzés/korrekció 1–3 óra, hátralévő zoom/render
8–20 óra: durva, nem mért becslések.
