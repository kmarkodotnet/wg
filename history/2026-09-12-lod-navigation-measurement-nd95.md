# 2026-09-12 — Három ND-95 korrekció

A felhasználó három következő feladatot kért; a bizonytalan korábbi
9,9 s-os várakozás miatt nem kértünk további reprodukciót.
[Közös átadás és próbamenet](../docs/reviews/lod-navigation-measurement-batch-nd95-2026-09-12.md).

1. Monotón kickoff/worker/átvétel/commit óra, fázisonkénti falióra-log.
   A Build-frame nem torzítja a requestAge/pendingMs értéket; scheduling marad.
2. Lépték nézet-/világkontextushoz kötve, nincs régi modell új paraméterrel,
   másik targettel vagy megváltozott vetülettel. Türelem csak azonos nézetben.
3. FlyTo helyi felszín feletti magasságot interpolál az egész úton,
   megtartott korlátokkal és kézi megszakítással.

707/707 .NET PASS, viewer Release 315/315. Solution 0 hiba/0 warning;
Unity-forrás 0 hiba/83 meglévő warning, Editor-tesztek 0 hiba/4 meglévő
warning. 11 új .NET-időfáziseset futott; 17 új Editor-bekötési eset csak
fordított. Élő kép/FlyTo/lépték/minimumkamera-acceptance továbbra is kell.
Nincs Core/relief/scene/küszöbváltozás, commit vagy push.

M9 súlyozott állapot kb. 55%; víz/kamera/lépték korrekciós maradék
2–4-ről 1–3 órára, összes maradék 12–24-ről 11–23 órára becsülve.
E csomag durva ráfordítás-egyenértéke 1–3 óra; egyik sem mért munkaidő.
