# 2026-09-23 — ND-147 szakaszos folyómesh-építés

A felhasználó jelezte, hogy a kész finom folyóhálózatra váltás
pillanatát nem figyelte, így az akadását nem tudja megítélni. A
[18:06-os élő PerfLog](../unity/WorldGenViewer/Logs/PerfLog_20260923_180606.txt)
ennek ellenére 18 504,6 ms főszálú végleges mesh-építést mér, a durva
előnézet korábbi két menete 3205,3 és 3394,9 ms volt. Ezt a
[ND-147](../docs/04-decisions.md) külön render-ütemezési hibaként kezeli.

A viewerben a `BuildRiverNetwork` most generációhoz kötött, teljes
mesh-munkát ütemez. A `LateUpdate` egyenként felszínre illeszti a
pontokat 4 ms-os képkockánkénti keretben, folyónként ugyanazzal az
`AddRiverRibbon` geometriával. A teljes mesh egyben váltja le a látható
aktuális előnézetet. Új Build vagy újabb hálózateredmény eldobja az
elavult függő munkát. A durva tile-út köztes renderpontjai 4 km-enként
kerülnek fel, az eredeti 1 km helyett; a Core-hálózat és a finom pontsor
nem változott. A következő PerfLog `ND-147 river mesh staged` sorában
előállítási falióraidő, aktív főszálú idő, maximális slice és Unity
mesh-feltöltés külön szerepel.

Az offline viewer-forrásfordítás 0 hibával sikerült. Az új
[22:44:46-os PerfLog](../unity/WorldGenViewer/Logs/PerfLog_20260923_224446.txt)
az előnézetre 16 056 pontot, 970,4 ms aktív főszálú munkát, 5,1 ms
legnagyobb szeletet és 1,1 ms Unity-feltöltést mért. A 96 folyós
előnézet 3537,5 ms-mal a folyómunka indulása után lett kész. A napló
22:45:10-kor a Play Pause miatt állt meg; a felhasználó 23:10 körül
feloldotta. Ezután elkészült a 96 folyós, 400 696 pontú finom mesh:
47 748,1 ms falióra, 19 217,2 ms összesített aktív főszálú munka,
7,8 ms legnagyobb szelet és 10,8 ms egyszeri feltöltés. A korábbi
18 504,6 ms egybefüggő munka helyett a számítás több frame-re oszlott.
A `river ready` 1496976,8 ms-os ideje a Pause-t is tartalmazza, így
gyorsulás összevetésére alkalmatlan. Felhasználói látványvisszajelzés
és szünet nélküli frame-/futásidő-mérés még szükséges. Részletes állapot:
[A8/4 eredmény](../docs/plans/a8/04-viewer-bekotes-es-elo-atvetel-eredmény.md).

Commit és push nem készült.
