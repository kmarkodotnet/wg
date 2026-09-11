# 2026-09-11 — ND-80 második élő próba

A felhasználó újabb „done, nézheted” üzenetére a friss
`PerfLog_20260911_223119.txt` logot elemeztük. Nincs új runtime-változás.

57 sikeres alkalmazás, ND80/L6/256; minden chunk betartja a korlátot.
A nem üres kérések feltöltési mediánja 3,41 ms, p90 értéke 5,68 ms,
maximuma 8,60 ms. Teljes kérés mediánja továbbra is 506,50 ms.

Kiemelt bizonyíték: 22:32:06.115-nél csak 6 új osztás és 64 emittált
levél marad, 31 882 újrahasznosul; a kérés mégis 616,9 ms, ebből cut
323,68 ms, emit 188,82 ms, feltöltés 2,48 ms. Az emit teljes előkészítést
és cache-ellenőrzést is tartalmaz, nem csak a 64 levél geometriaírását.

A két 130-as állókamerás lánc kb. 4,32 és 4,00 s; kész állapotban a
középső tereptile 10,18 px. Következő javasolt cél változatlanul az
ismételt teljes cut- és geometria-előkészítés, belső fázisméréssel.

[Az élő elemzés frissítve](../docs/reviews/lod-bounded-chunks-live-2026-09-11.md).
Nincs kódmódosítás, tesztújrafuttatás, commit/push vagy vizuális lezárás.
M9 60–70%. Durva, nem mért ráfordítás-egyenérték: 0,5–1 óra elemzés;
hátralévő megjelenítési/zoommunka és validáció 8–20 óra.
