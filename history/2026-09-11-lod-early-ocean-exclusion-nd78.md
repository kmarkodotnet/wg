# 2026-09-11 — ND-77 kiértékelése, ND-78 részjavítás

A felhasználó elvégezte az ND-77 próbát: „done. te jössz”. Folytattuk az
engedélyezett 1. lépést. Az új log `PerfLog_20260911_212059.txt`.

Bizonyított:

- Álló kameránál, befejezett finomítás mellett két statikus tile teljes
  clipped mérete 28,816 / 31,678 px, a kiválasztás becslése 9,194 / 7,176 px.
- A feltöltött sarkok független újravetítése pontosan visszaadja mindkét
  párt: az eltérést a vízszintre emelt mély proxy-sarkok okozzák.
- A mérés teljes quadot mér, nem takaratlan fragmentumkiterjedést;
  a víz alatti rész is beleszámíthat, ha a tile egy ponton látható.
- Egy másik tereptile több állókamerás mintában `split-quota` okból vár.
  A cut felosztott tengerfeneket is, amit utána a renderer eldobott.

Elvetett runtime-kísérlet: nyers terepsugarak a proxy quad/boundsában.
Már távol ~17 ezer, közepesen 60–67 ezer tereplevelet hozott az eredeti
0 / ~1 ezer helyett. A korai óceáni kizárással együtt is túl sok volt.
**Visszavonva**; az éles `TerrainLodProxy`/morph változatlan. A döntésnapló
őrzi a kezdeti tervet és annak mérés alapján történt korrekcióját.

Átadott részjavítás:

- A CPU/perspektivikus proxy-út már a base-szintnél meghívja a renderer
  meglévő `IsBaseAncestorOceanic` kizárását, mielőtt kvóta fogyna.
- A feltétel ugyanaz, a vegyes part nem kap új kizárást. Csak kész statikus
  besorolás/sarokadat olvasása történik, nincs új Core-minta.
- Az osztási keretből nem készül később eldobott dinamikus tengerfenék.
- `earlyOceanExclusion=ND78`, `skippedSelectionBases` és a döntési trace-ben
  `renderer-base-exclusion` jelzi az új utat.
- Pixelcél, budget, maximális LOD, vízmesh, scene, seed változatlan.

Offline próba (`--shoreline`): 2 irány × 7 állás, a renderer szűrése utáni
TileId-halmaz mind a 14 esetben azonos a régi és az átadott út között.
Közepes állásban 11→8, mélyebben 14→13 adag kell; kiválasztási mérés,
nem Unity FPS vagy teljes mesh-idő. Az éles callback első cache-feltöltése
és a Unity emisszió/upload költsége nem része a próbának.

Ellenőrzés:

- Solution build: 0 hiba, 0 figyelmeztetés.
- Core 381/381, CLI 7/7, LOD Debug/Release 170/170: **558 külön eset**,
  9 új. Teszt: logbeli méretkülönbség, kizárás kvóta nélkül, változatlan
  nem kizárt part, fedés, visszazoom, előzmény védelme, megszakítás, budget.
- Unity-forrásfordítás: 0 hiba, 83 korábbi figyelmeztetés. Élő vizuális
  vagy performance validáció nincs; a felhasználó következő próbája kell.
- Python nincs telepítve (`py --list`), KAT/vektorregenerálás nem futott.
  Core/referencia/Core-teszt diff üres; seed-kompatibilitás nem változott.
- `git diff --check` tiszta. Nincs commit/push; korábbi felhasználói
  módosítások és `.claude` megőrizve. Az elvetett kód saját kísérlet volt,
  annak visszavonása nem érintett felhasználói adatot.

[Teljes elemzés, reprodukció és próbamenet](../docs/reviews/lod-early-ocean-exclusion-nd78-2026-09-11.md).

Az 1. lépés teljes élességi célja **nem lezárt**: a tartós teljes-quad
méretkülönbség maradhat, a valóban takaratlan terep hibamértéke és a
mély proxy/morph eltérései nyitottak. Most a kimutatott kvótapazarlás és
ebből eredő késés részjavítását adjuk át. Vízfinomítás és általános
upload-optimalizálás nem indult el.

M9 tartalmi becslés 60–70%. Durva, nem mért ráfordítás-egyenérték e lépésre
3–6 óra; további zoomminőség/validáció 8–20 óra, km-UI és új Core-modell nélkül.
