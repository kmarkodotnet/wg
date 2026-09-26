# 2026-09-25 — ND-148 folyó-zoom diagnózis

A felhasználó szerint a finom folyók látszanak, de zoomkor néha az egész
kép megakad, néha a folyóvonalak maradnak le a tereptől. A korábbi
[PerfLog](../unity/WorldGenViewer/Logs/PerfLog_20260923_224446.txt)
zoom idején terep-LOD kérést és feltöltést mutat, de összframe-/GPU-időt
nem. Az Editor Pause miatt a nagy falióra-rések nem értelmezhetők
önmagukban frame-akadásként. A kész folyómesh nem épül újra zoomra.

A scene-ben mentett `riverLineRadialBias: 0.02` eltért a kód 0,5-ös
alapértékétől és a korábbi ND-49 vizsgálat által megállapított szükséges
kiemeléstől. A `PlanetView.unity` értékét 0,5-re változtattuk. Az új
`[ND-148 frame stall]` PerfLog-sor 100 ms fölötti frame-résnél a
képkockadeltát, aktuális `LateUpdate` költséget, zoomot és folyó-/LOD-
függő munkát rögzíti. Ez diagnosztika, nem állítja, hogy a teljes kép
megakadása már megoldódott.

Az offline `WorldGen.Viewer.Compile` Release build 0 hibával és 122
warninggal zárult. Ezen a ponton az élő Unity-zoompróba még hiányzott.
A már nyitott scene-ben az Inspector értékét
külön ellenőrizni kell, mert a külső `.unity` módosítás nem mindig
töltődik vissza automatikusan. Commit és push nem készült.

Az új [22:25-ös PerfLog](../unity/WorldGenViewer/Logs/PerfLog_20260925_222539.txt)
alapján az előnézet 3242,3 ms alatt, a Pause utáni finom mesh 400 696
ponttal elkészült. A finom mesh 45 943,1 ms falióra alatt 18 744,3 ms
aktív főszálú munkát végzett, 8,2 ms legnagyobb szelettel és 12,3 ms
feltöltéssel. A Pause miatt a 270 438,8 ms-os `river ready` nem
teljesítmény-összevetés. A finom mesh előtti 29 fókuszált, 1 s alatti
frame-rés átlaga 147,4 ms, maximuma 243,8 ms, az aktuális LateUpdate
maximuma 2,4 ms; a finom hálózat utáni zoomnál egy 100,2 ms-os rés
maradt. A felhasználó az Inspectorban 0,5-re állított értékkel mindkét
zoomtünetet javultnak látta. A vizuális javulás megerősített, de a
teljes képakadás oka és végső Profiler-elfogadása nyitott.
