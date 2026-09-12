# 2026-09-12 — ND-93/94 élő logelemzés

A felhasználó lefuttatta a próbát. [Részletes elemzés](../docs/reviews/lod-resource-staging-live-2026-09-12.md)
a `PerfLog_20260912_140221.txt` alapján: 104 commit, 256 staging-szelet,
1568 kiürített inaktív kulcs. A 337-es átmeneti inaktív csúcs a következő
mintára 128-ra csökkent. Szeletmax 3,18 ms, commitmax 4,55 ms.
Nem kontrollált A/B gyorsulásmérés; tíz világ-újraépítés is volt.

Nyitott minőség: kész állapotban a `8900000000015424` tile 26,975 px,
proxyja csak 9,635 px, ezért `below-threshold`. A halasztott geometria/
proxy javítás új reprodukciós bizonyítéka. A 9,906 s-os egyszeri kérés
nem a mért uploadköltségből következik, szünet/fókusz hatása tisztázandó.
A Build-frame időbélyege torzíthatja a Build utáni `requestAge`-et.

Nincs kód-, scene- vagy Core-változás és commit/push. A cache/staging
implementációhoz élő működési bizonyíték került; memória/vizuális/Editor-
elfogadás nincs automatikusan lezárva. M9 újraértékelve kb. 55%; az audit
azonos hatókörű, durva maradéka 12–24 óra, nem mért idő.
