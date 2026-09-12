# 2026-09-12 — ND-91 kamerakorlát, első kapu

A felhasználó kérte a tile-division lépések folytatását. A kötelező
projektforrások és a friss ND-89 logok ellenőrzése után a backlogban
következő önálló felszínkövető kamerakorlátot implementáltuk.

Az ND-84 modellfelszín-lekérdezését pontos irány/snapshot cache közvetíti.
A zoom, forgás, FlyTo és ApplyTransform helyi magasságot használ, a
near clipet is figyelembe vevő minimális réssel; LateUpdate újra ellenőriz.
Kapcsolható, alapból aktív. Nincs Core-, relief-, scene- vagy pixelcél-edit.
A párhuzamos ND-90 módosításokat megőriztük; commit/push nem történt.

[Mérés, korlátok és próbalépések](../docs/reviews/lod-camera-clearance-nd91-2026-09-12.md).
12 új .NET-eset; LOD Debug/Release 290/290, CLI 8/8. Solution build zöld,
Unity-forrás 0 hiba/83 warning, Editor-assembly 0 hiba/4 warning.
Három új Editor kamera/transform/cache-teszt csak fordított. A teljes
Core-tesztfutás 375/384 sikeres, 9 referenciaeltérést jelez, külön numerikus munkaszálat
érint; e lépésben a referenciákat nem írtuk át.

Nem teljes mesh-ütközésvédelem vagy élességjavítás. Élő vizuális/performance
próba szükséges. M9 durván 60–70%; e kapu 2–4 óra ráfordítás-egyenérték,
próba/korrekció 1–3 óra, zoom/render hátralévő 8–20 óra: durva, nem mért becslés.
