# 2026-09-11 — Korábbi első finomodás, ND-73

**Utólagos visszajelzés:** az eredmény nem jó, elutasítva. Az ND-74 indulásakor
olvasott 18:39:49-es log még 0,600 morph-range-et tartalmazott a scene fájl
0,35 értéke helyett. Az alábbi „ellenőrzés még kell” a korábbi átadás állapota.

A felhasználó az ND-72 után valamivel jobbnak látja a zoomot, de későinek
érzi a finomodás kezdetét. A 18:22:58-as PerfLog szárazföldi nadírján a base
11,17 px-nél még nem osztódott; az első L9 megjelenésekor a morph csak 0,105.
A diagnózist és a költségkockázatot a módosítás előtt ismertettük.

A globális 12→10 px profil elvetve: a kontrollált roundtrip csúcslevélszáma
107 739→195 118. Ehelyett csak a base első splitje kap 10 px célt, a mélyebb
szintek 12 px értéke és a merge-küszöb marad. A base morphja ugyanazt a
küszöböt használja. Morph-range 0,6→0,35, kéréskori snapshotból. Scene és
komponens alapérték együtt frissült; az offline ND-71 nem aktiválódott.

A kontrollált sorban egy görgetési lépéssel korábbi első split, ugyanott
0,105→0,626 morph-alfa. A mély közeli cutok halmazként azonosak, a teljes
roundtrip csúcslevélszáma +25,7%. A korai kérésenkénti költség ennél nagyobb
arányban is nőhet, ezért nem állítottunk ingyenes vagy FPS-validált javulást.

[Részletes mérés és elfogadási kapu](../docs/reviews/lod-zoom-onset-nd73-2026-09-11.md).
Core 381/381, CLI 7/7, LOD Debug/Release 97/97 (összesen 485 teszt);
solution build 0 hiba, Unity assembly fordítás 0 hiba /
83 figyelmeztetés. Élő ND-73 ellenőrzés még kell. Core-/seed-változás,
budgetemelés, commit/push nincs. A pontos km-lépték a backlogban marad.
Python nincs, KAT/regenerálás nem futott; Core és tesztvektor változatlan.

Durva M9: 60–70%; jelen munka 2–4 óra, további zoommunka 14–30 óra,
nem mért idő, km-lépték UI nélkül.
