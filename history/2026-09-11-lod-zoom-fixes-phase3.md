# 2026-09-11 — Zoomjavítás, harmadik csomag (ND-71)

A felhasználó megerősítette: az ND-70 után a zoom/visszazoom szépen működik.
A lassulást most halasztja; kb. 13 görgetés után nem érzékel további élesedést.
Kérésére pontos, kamerafüggő km-léptékcsík került a backlogba, implementáció nélkül.

A friss 14:41:59-es PerfLog további kamera-közelítést és új cutokat igazolt,
nem teljes zoom-stopot. Az alapgömbtől mért távolság hibájára ND-71 született:
öt tényleges terepminta, külön láthatósági bounds és érintősíkbeli
mintasűrűség-metrika, azonos split/morph számítás. A régi gömb-horizonttiltás
nem érvényes az eltolt terepre. A callback opcionális, GPU/legacy viselkedés
nem kap csendben új modellt. Core-/seed-változás nincs.

A teljes 3D sugárral végzett kezdeti próba magasságugrásokon túlfinomított;
a végleges változat mintasűrűséghez tangenciális kiterjedést használ.
A pozíciót előre betöltő bounds-kód miatt a render-cache szín/normál
teljességvizsgálata és LRU-kezelése is javult. A statikus ősrács mintái
újrahasználhatók, koordináta-bitazonosságuk tesztelt.

A célzott szárazföldi próbában 0,5 → 0,05 render-egység közelítésnél a régi
nadir L12 → L12 helyett L14 → L17; konfigurált budget végig 25 000.
A részletes mérés, költség és korlátok:
[harmadik csomag](../docs/reviews/lod-zoom-fixes-phase3-2026-09-11.md).

Ellenőrzés: Core 381, CLI 7, LOD 72 sikeres teszt; LOD Release is 72/72.
Solution build 0 hiba; Unity assembly fordítás 0 hiba / 83 figyelmeztetés.
Python nincs, KAT/regenerálás nem futott; Core nem változott. Élő ND-71
vizuális/FPS-ellenőrzés még nincs. Új logjel: `[async apply ND-71]`,
`deepestCutL` és `nadirCutL`. Commit/push nem készült.

Nyitott: felszínkövető kamera, bizonyított terrain-bounds/occlusion,
upload és emisszió gyorsítása, km-lépték UI. Durva, súlyozott M9: 65–75%;
csomag 4–8 óra nagyságrend, további zoommunka 14–28 óra (nem mért idő,
lépték-UI nélkül).
