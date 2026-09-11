# ND-81 — Ismételt LOD-vetületmunka csökkentése

A felhasználó „csináld” jóváhagyására, az ND-80 két élő visszamérése után.
Branch `codex-handoff`, HEAD `195875e`; a meglévő dirty állapot megőrizve,
nincs commit/push, scene- vagy Core-módosítás ebben a lépésben.

Implementáció előtt új ND-81 döntés: pontos kamera- és proxyazonossághoz
kötött, legfeljebb 262 144 bejegyzéses metrika-cache a single-flight cut/emit
workerben. Nem tárol döntést, így kvóta/hiszterézis/trace/balance újrafut.
A geomorph is innen kérhet metrikát; a párhuzamos renderdiagnosztika nem.
Új log: selection/balance, metricHits/metricComputed/metricEntries és az
emit részidejei: resolveCheck/auxiliaryCopy/tileEmit.

Offline páros vizsgálat: 40 kör, minden cut és ellenőrzött trace azonos.
130 / 122,161 / 108,152 távolságnál a teljes kiválasztási lánc összideje
904,99→582,34 / 1072,38→679,77 / 1614,02→1170,09 ms. Egy offline futás,
nem Unity/FPS-benchmark. A záró állókamerás körök nulla metrikaszámítással
futnak. Mélyen a megmaradó balance ideje nagyobb lehet a selection idejénél.

Ellenőrzések:

- `dotnet build WorldGen.sln --no-restore -p:_EnableDefaultWindowsPlatform=false`: 0 hiba.
- Solution Debug tesztek: **593/593** = 381 Core + 205 LOD + 7 CLI.
- LOD Release: **205/205**, ebből 15 új ND-81 teszteset.
- Unity-forrásfordítás a meglévő `artifacts/lod-zoom-validation.targets`
  segítségével: **0 hiba, 83 meglévő figyelmeztetés**.
- `git diff --check`: rendben.
- Python KAT/vektor-regenerálás nem futott: nincs telepített Python.

[Részletes jelentés és próbamenet](../docs/reviews/lod-work-cache-nd81-2026-09-11.md).
A pixelcél változatlan; nincs élességjavítás vagy élő elfogadás. A felhasználó
próbája következik, közepes/mély zoomnál 8–10 s állással és visszazoommal.
A teljes sarok-/coverage-/pozícióellenőrzés és balance további csökkentése
nyitott; utána lehet a kisebb pixelcél költségét újra megvizsgálni.

M9 tartalmilag súlyozott becslés: **60–70%**. Durva, nem mért
ráfordítás-egyenérték: **2–4 óra**; további megjelenítés/zoom és validáció
**8–20 óra**. Élő Editor-visszajelzés nélkül nem tekintjük vizuálisan késznek.
