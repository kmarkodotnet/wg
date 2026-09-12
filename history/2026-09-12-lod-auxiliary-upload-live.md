# 2026-09-12 — ND-86 élő logelemzés

A felhasználó futtatott és logellenőrzést kért. Az új forrás
`PerfLog_20260912_015131.txt`, az alap `PerfLog_20260912_013133.txt`.
71/71 commit ND-86, 206 szelet; vízpublikálás max 0,50 ms, commit max
7,88 ms, legnagyobb tereppublikálás 6,30 ms. Teljes kérés medián 669,3 ms,
max 1404,7 ms: a háttérmunka és a finomítási késés továbbra is domináns.
A legnagyobb szelet 4,02 ms, az első terepjob, nem az aux-feltöltés.

Kész, állókamerás mintában a terepquad 20,804 px, kiválasztási proxyja
11,804 px a 12 px cél alatt. A korai zoom/proxy/minőség továbbra is backlog.
Nincs PerfLogban error/exception/single-fallback; 21 elavult workerkérés
és a futás végén egy nem publikált staging eldobódott. Világváltás és teljes
vizuális/FPS-elfogadás nem igazolható ebből a logból.

[Részletes táblázat, módszer és továbblépés](../docs/reviews/lod-auxiliary-upload-live-2026-09-12.md).
Backlog és M9 frissítve. Javaslat az upload folytatására: tereppublikálás
felbontott mérése és a drága előkészítés leválasztása a végső váltásról;
a víz további bontása jelenleg nem elsődleges. A kameraút eltér, így a
régi/új idők különbsége nem tiszta ND-86 gyorsulásmérés.

Csak dokumentáció változott; kód, scene, Core, seed és a többi dirty munka
érintetlen. Nem volt build/teszt, commit/push vagy új implementáció.
M9 tartalmilag durván 60–70%; a vizsgálat ráfordítás-egyenértéke 0,5–1,5 óra,
fennmaradó zoom/render munka 8–20 óra, durva és nem mért becslések.
