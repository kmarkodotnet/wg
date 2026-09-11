# 2026-09-11 — ND-80 élő log kiértékelése

A felhasználó: „done, nézheted”. Vizsgált log:
`PerfLog_20260911_220452.txt`, összevetve a `214108` próbával.

- Mind a 48 alkalmazás ND80/L6/256 módban futott, a tényleges maximum
  256 levél. Nem jelzett sérült quadot vagy diagnosztikai hibát a napló.
- A nem üres alkalmazások feltöltési p90 értéke 25,44→5,29 ms,
  maximuma 27,77→6,63 ms. Eltérő kameraút, nem kontrollált FPS-benchmark.
- Teljes kérés mediánja 545,55→534,60 ms: a háttérmunka megmaradt.
  A jelenlegi állókamerás láncok 7–8 hullámban kb. 4,2–5,1 s-osak.
- Domináns a cut és az emit. A cache-hit előtt továbbra is teljes
  feloldottpozíció-bejárás fut; minden hullám új BuildCut-ot indít.
- A korai 10 px-es küszöb, a statikus víz és a proxy/fine eltérések
  változatlanok. A felhasználó nem adott kifejezett vizuális elfogadást.

Javasolt következő lépés: cut-fázisok mérése és az ismételt állókamerás
kiválasztás folytathatóvá tétele, majd a geometria-előkészítés csökkentése.
Runtime, scene, seed nem változott; teszteket nem futtattunk újra pusztán
a logelemzés miatt. Nincs commit/push; korábbi dirty állapot megőrizve.

[Részletes bizonyíték](../docs/reviews/lod-bounded-chunks-live-2026-09-11.md).
M9 60–70%; durva, nem mért ráfordítás-egyenérték 1–2 óra elemzés,
hátralévő megjelenítési/zoommunka és validáció 8–20 óra.
