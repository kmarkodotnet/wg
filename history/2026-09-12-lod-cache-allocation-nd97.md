# 2026-09-12 — ND-97, további három tile/zoom költségcsökkentés

A felhasználó a következő feladatokat kérte, kézi próbát csak később végez.
Branch `codex-handoff`, HEAD `99b3ac4`; az ND-96 munkafabeli változásokra
építünk. Új commit/push nem történt, a külön Core/reference/CLI munka megmaradt.

## Implementáció

1. A terrain/víz worker metrika-cache-e explicit nézet-resetkor megőrzi
   a szótár kapacitását. Minden megváltozott vetület régi értéke törlődik;
   az azonos vetület cache-találatai megmaradnak. Single-flight: reset
   csak az előző worker/staging befejezése után történik.
2. A geometria-feedback helyi revíziója csak a tile és ősei eltárolt
   metrikáját érvényteleníti; nincs teljes Dictionary.Clear minden
   feljegyzés után. Telített cache is helyben frissíti az elavult elemet.
3. A cache azonos-quad ellenőrzése az örökölt ValueType.Equals helyett
   komponensenkénti double.Equals. Tolerancia nélküli, allokációmentes.

Új log: `metricStorage=ND97`, `metricViewResets`, `metricFeedbackRefreshes`.
A számlálók cache-életciklusonként összesítettek. Pixelcél 8/7, relief,
világmodell és tile-/split-budget változatlan.

## Bizonyíték

- 15 új eset; solution build 0 hiba/warning, teljes .NET **747/747**
  (384 Core, 355 viewer, 8 CLI), viewer Release **355/355**.
- Unity runtime forrásfordítás 0 hiba / 83 meglévő warning, Editor-tesztek
  fordítása 0 hiba / 4 package-reference warning; nincs natív tesztfuttatás.
- 1000 azonos feedback reprodukciója javítás előtt 1368000 byte, utána
  0 byte; a szigorú 0-byte regresszió zöld, a geometria/revízió nem változik.
- 26×2 páros mozgókamerás állásban azonos cut/split/halasztás. A tároló
  megőrzésének izolált próbája 52–61%-kal kevesebb cut-allokációt és
  5–9%-kal kisebb cut-időt adott. Nem a teljes renderer vagy peak memória.
- Python-orákulumot nem futtattunk újra, Core/vektormódosítás nincs ebből
  a csomagból. Az előző körben Python-telepítés nem volt elérhető.

[Részletes mérés és korlátok](../docs/reviews/lod-cache-allocation-nd97-2026-09-12.md).
A [korábbi teljes kézi lista](../docs/reviews/lod-final-batch-nd96-2026-09-12.md)
továbbra is közös, későbbi kapu. Nem kérünk most újabb kézi próbát.

## Haladás

M9 **61,25%, kb. 61% marad**: a reakcióidő/erőforrás csoporthoz új
offline bizonyíték van, de teljes élő elfogadás nincs. E csomag durva
ráfordítás-egyenértéke 1–3 óra; az újraértékelt maradék 5–11 óra,
főként teljes felzárkózás, natív/vizuális és memória/FPS kapu. Nem mért
munkaidő és nem automatikusan levont fejlesztési idő.
