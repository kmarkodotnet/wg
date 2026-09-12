# ND-92 — Terepmaszk-helyreállítás részleges commit-hibánál

## Bizonyított hiba, szűk cél

A felhasználó a státuszbecslés auditja mellett külön subagenten kérte a
következő tile-feladatot. Ez az ND-89 upload-hibafallback egyik hiányzó
biztonsági kapuja, **nem a normál zoomélesség vagy sebesség javítása**.

A CPU `ApplyPrepared` az új rejtett halmazt már a natív indexfeltöltés
előtt publikálja. Példa: a régi GPU-maszk a 0. és 80. quadot rejti, az új
CPU-maszk a 40.-et. Három külön range töltené vissza a 0.-at, rejtené a
40.-et és állítaná vissza a 80.-at. Ha 0, 1 vagy 2 range után megszakad az
upload, a CPU-ból számolt `SetHidden(empty)` csak a 40.-et állítja vissza:
a régi GPU-maszkból marad lyuk. A **javítás előtti teszt 3/3 esetben elbukott**.
Ez szimulált natív buffer, nem élő GPU-hibainjektálás; a mechanizmust igazolja.

## Változás és várható hatás

- `TerrainIndexMask.RestoreAll` eredetiből állítja vissza az összes indexet,
  üríti a rejtett halmazt, érvényteleníti a régi terveket. Akkor is a teljes
  tartományt adja, ha a CPU szerint már semmi sem rejtett. Újabb fallback
  így ismételhető, és nincs új állandó indexbuffer.
- Kizárólag a commit hibaágában a teljes terep-indexbuffer feltöltődik.
  A normál sikeres részleges upload, staging és küszöb nem változott.
  Hibánál ez szándékosan drágább, a teljes statikus fedés az elsődleges.
- Ugyanez a hiba a vízmaszkot is érinti, ezért annak teljes helyreállítása
  is bekerült a közös commit hibaágába. A két réteg külön kísérlet: egyik
  hibája nem akadályozza a másik helyreállítását. A normál vízút változatlan.
- A diagnosztikai snapshot csak sikeres natív restore után ürül. Külön
  `[ND-92 terrain recovery]` és `[ND-92 water recovery]` sorban az
  `indices=... restored=True` jelzi a rétegenkénti sikert.
- Ha a restore is hibázik, a félkész dinamikus terep/víz/border akkor is
  kikapcsolódik, a cut-cache érvénytelenedik. Az eredeti és a recovery-hibák
  egy `AggregateException` alatt megmarad; nincs hamis sikerjelentés.
- A puszta vízkikapcsolás nem üríti a statikus vízmaszk diagnosztikáját.
  Új világ Build-kori invalidálása viszont továbbra is külön üríti, mert
  ott már új statikus vízmesh készült.

Tartós GPU-/mesh-hiba mellett nem ígérünk visszaállított képet. A korai
zoomok beragadása, a proxyeltérés és az általános
FPS-/minőségi elfogadás ettől nem oldódik meg. Core, scene, relief, kamera
és tile-kiválasztás nem változott e csomagban.

## Ellenőrzés

- Öt új .NET-eset: három részleges upload-megszakítás, ismételt restore és
  tervrevízió/snapshot-védelem, valamint üres ritka felület.
- Viewer-LOD **295/295 Debug és Release PASS**.
- Solution build: **0 hiba, 0 warning**.
- Unity Assembly-CSharp offline: **0 hiba, 83 korábbi warning**.
- Editor LOD-tesztassembly offline: **0 hiba, 4 korábbi warning**.
- Négy új Editor-eset a valódi viewer catch ágba lép: a CPU/GPU-eltérést
  előállított fixture adja, a hibás commit-bemenet aktiválja a fallbackot.
  Sikeres restore-nál a terep két submesh-e, pozíciói és bounds-a megmarad;
  a vízindexek is teljesen visszaállnak. Mindkét hiányzó mesh-kombináció
  esetén rétegenként nincs hamis siker-snapshot, a kivételek megmaradnak és a
  dinamikus réteg kikapcsolódik. **Ezek csak fordítottak, Editorban nem futottak.**

A főszál külön ellenőrizte a másik ND-90 munka utáni Core-teszteket:
384/384 Debug PASS, tehát a korábbi kilenc referenciahiba nem aktuális
blokkoló. A nézetoldali javítás nem írta át a Core-t vagy vektorait.

## Következő ellenőrzési kapu és státusz

Editor Test Runnerben a `CommitFailureRestoresFullStaticIndicesAndCleansUpEvenWhenRecoveryFails`
négy esete futtatandó. Normál zoom/visszazoom alatt ne jelenjen meg ND-92
recovery-log vagy új vizuális regresszió. A hibafallback természetéből
adódóan rendes próbában **nem várható élesebb kép vagy gyorsabb zoom**.

Az új, feladattételekre bontott becslés forrása a
[M9-státuszaudit](m9-progress-audit-2026-09-12.md), nem a korábbi változatlanul
ismételt százalék/órasáv. E szűk csomag ráfordítás-egyenértéke durván 1–2 óra,
nem mért munkaidő. A hibabiztonsági kód és offline kapu kész; a natív Editor-
futtatás még nyitott, ezért teljes vizuális elfogadásnak nem számít.
