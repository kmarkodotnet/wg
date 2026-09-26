# 2026-09-23 — A8/4 viewer-bekötés, élő átvétel nyitott

A [teljes A8/4 eredmény](../docs/plans/a8/04-viewer-bekotes-es-elo-atvetel-eredmény.md)
rögzíti a módosítást, az offline ellenőrzést és az élő átvételi kaput.

A `PlanetGridMesh.StartRiverRefinement` az ND-145 durva, modellalapú
előnézet után a Core `BuildContinuousRiverNetworkFromSourcesParallel`
útját hívja `maxDegreeOfParallelism: 4` értékkel. Az ND-132 dedikált
háttérfeladat, a megszakítás, a generációellenőrzés és a főszálú mesh-
átvétel megmaradt. A PerfLog a forrásszámot, workerszámot, világidőt,
lépésközt, preview-időt/pontszámot/mesh-időt és a végleges hálózat
teljes elkészülési és mesh-idejét külön jelzi.

Az offline Viewer.Compile fordítás 0 hibával zárult. A Core A8/3
bitazonos 96 forrásos t=0/t=22 lenyomatai és 600/600 Release tesztje
az előző részfeladat eredménye. A Unity Editor-processz elérhető, de a
naplója régebbi a friss forrásnál, új Play PerfLog nincs. A Windows
computer-use segéd két inicializálási kísérletben azonos kernel-assets
útvonalhibával leállt, így a Play-menetet nem lehetett vezérelni. A
felhasználót friss PerfLog és képernyőkép beküldésére kértük. Az élő
gyorsulás, előtér/frame- és memóriahatás, valamint a folyók tényleges
láthatósága továbbra is nyitott. A8/B3 nincs vizuálisan késznek jelölve.

**Későbbi élő menet, 15:38:** a felhasználó megerősítette, hogy a folyók
megjelentek a felszínen. A [friss PerfLog](../unity/WorldGenViewer/Logs/PerfLog_20260923_153831.txt)
`workers=4`, t=0, 50 m, 96 forrás mellett 2993,1 ms Buildet,
4529,6 ms előnézeti elkészülést, 59 986 renderpontot és 3394,9 ms
főszálú preview-mesh időt mért. A Play 19 586,2 ms-nál megszakította a
finom feladatot; `river ready` és végleges mesh nem keletkezett.
Az előnézet láthatósága így igazolt felhasználói megfigyelés, de a
3,395 s-os frame-megakadás és a finom hálózat teljes ideje nyitott.
Az eredményfájl ezekkel a számokkal frissült.

**Második élő menet, 18:06:** a felhasználó hosszabb Play-menetet futtatott;
a [PerfLog_20260923_180606.txt](../unity/WorldGenViewer/Logs/PerfLog_20260923_180606.txt)
egy t=0, 50 m-es, 96 forrásos Buildet és kész négyworker-es finom
hálózatot mutat. Build 2968,1 ms; előnézet 4330,6 ms (mesh 3205,3 ms);
finom `river ready` 144 682,8 ms; végleges mesh 18 504,6 ms, így a
folyómunka indításától a végleges mesh-ig 163 188,0 ms. Ez a Build
kezdetétől 166,156 s. A régi kész szekvenciális menet 1477,626 s
`river ready` + 13,236 s mesh volt, de ismeretlen világidővel és
terheléssel; a ~9,1× teljesidő-arány tájékoztató, nem kontrollált
gyorsulás. A 18,5 s-os főszálú mesh-akadás külön probléma. A felhasználó
végleges hálózatról szóló látvány-/akadási ítélete még hiányzik.

Branch `main`, kiinduló HEAD `e3713bf`, előzetesen is módosított munkafa.
Commit és push nem készült.
