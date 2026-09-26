# 2026-09-23 — A8/2 szemantikai és algoritmuskapu

A felhasználó az A8/1 után a következő részfeladat folytatását kérte.
Az A8/2 terv előfeltétele a folytatást támogató mérési kapu volt; az
[A8/1](2026-09-23-a8-river-baseline.md) ezzel ellentétes eredményt adott.
Ezért a részfeladatot a bitazonossági szerződés, a lehetséges ütemezés és
a megvalósítási kapu dokumentálásával zártuk le. Teljes eredmény:
[A8/2 eredmény](../docs/plans/a8/02-szemantika-es-algoritmusterv-eredmény.md);
döntés: [ND-146](../docs/04-decisions.md).

Az ellenpélda a t=0, 96 forrásos scene-mérés 43 → 32 összefolyása.
A medencénkénti körsorrend ezt a két forrást felcseréli. A `claimed`
nélküli teljes követés és az utána következő növekvő indexű commit
bitazonos lehet, és a Core-ban már van ilyen párhuzamos API. A további
38 797 spekulatív lépés és az ND-132 korábbi ThreadPool-terhelése mellett
az aktuális viewerre igazolt teljes gyorsulás nincs. Új Core-út és viewer-
bekötés ezért nem készült; A8/3 és A8/4 előfeltétele hiányzik.

A folyók jelenlegi Play-beli hiányára az [ND-145](2026-09-23-a8-b3-river-preview.md)
előnézeti módosítás van a munkafában. A legutóbbi elérhető Unity PerfLog
2026-09-23 11:38-kor zárult, a módosítás előtti állapotot mutatja.
Új `river preview` sor, Unity-fordítás vagy képernyőkép még nincs;
az offline viewer-forrásfordítás az előző részfeladat végén sikeres volt.

Branch: `main`, kiinduló HEAD `e3713bf`, már előzetesen is módosított
munkafa. Commit és push nem készült.
