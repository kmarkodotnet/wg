# A8/4 — Viewer-bekötés és élő átvétel: részleges eredmény

**Dátum:** 2026-09-23. **Állapot:** a viewer-bekötés és fordítás kész;
az **előnézeti folyók láthatóságát a felhasználó Play-ben megerősítette**.
A finom hálózat elkészülése és az élő teljesítménykapu nyitott.

## Bekötés

A `PlanetGridMesh.StartRiverRefinement` a teljes `Build()` után ugyanazon
ND-132 `LongRunning` háttérfeladaton elindítja az ND-145 modellalapú durva
előnézetet. Utána a finom hálózat a Core
`BuildContinuousRiverNetworkFromSourcesParallel` API-jával, **legfeljebb
4 workerrel** készül el. A forráslista és a kanonikus `claimed` commit a
Core-ban változatlan; az A8/3 teljes 96 forrásos t=0 és t=22 Myr SHA-256
lenyomata a szekvenciális alapvonallal bitazonos.

Új Buildnél a régi feladat megszakad, a függő előnézet törlődik; a
generációellenőrzés miatt elavult finom hálózat nem kerül a főszálra. A
`TryApplyCompletedRiverRefinement` továbbra is a kész listából számolja a
`ComputeDischargeWeights` súlyokat és építi a szalag-mesht. A preview és
a végleges mesh egyaránt csak a megfelelő generációnál alkalmazható.

Az új PerfLog kulcssorok:

```text
[ND-132 river start] ... sources=96 scheduler=dedicated-bounded-parallel workers=4 timeMyr=... stepMeters=... afterBuild=True
[ND-145 river preview] ... elapsedMs=... rivers=... points=... meshMs=...
[ND-132 river ready] ... elapsedMs=... rivers=96 workers=4
[ND-147 river mesh staged] ... kind=preview|fine prepareWallMs=... activeMs=... maxSliceMs=... uploadMs=...
[ND-132 river mesh] ... elapsedMs=... totalElapsedMs=...
```

A `Build() TELJES` a főszálú Build ideje; a `river ready` a Build utáni
háttérmunka teljes ideje; a `river mesh` főszálú idő és `totalElapsedMs`
együtt mutatja, mikor lett a végleges szalag kész. A `river cancel`
generációt, befejezettséget és addigi időt rögzít.
Az ND-147 előtti naplóban a mesh egyetlen főszálú hívás volt; az
ND-147 utáni `river mesh elapsedMs` a több képkockára szétosztott aktív
főszálú munka összege, a `totalElapsedMs` pedig továbbra is falióraidő.

## Ellenőrzés és hiányzó élő bizonyíték

- A [Core A8/3 differenciális és mérési kapu](03-core-megvalositas-es-differencialis-tesztek-eredmény.md)
  zöld: célzott 13/13, teljes Core Release 600/600, solution build 0 hiba.
- `dotnet build tests/WorldGen.Viewer.Compile --no-restore`: **0 hiba**;
  a figyelmeztetéseket külön kapcsolóval elnyomtuk a rövid kimenethez.
- Az első ellenőrzéskor az Editor.log és a PerfLog még régebbi volt a
  módosított forrásnál. A későbbi felhasználói Play-menet már az új
  `scheduler=dedicated-bounded-parallel workers=4` sort írta. Az
  Editor.logban nincs `error CS` vagy `Compilation failed` sor; a
  releváns `LogAssemblyErrors` értékek 0 ms-ok.
- A Windows computer-use segéd az ablak kiválasztása előtt kétszer
  `failed to write kernel assets: The system cannot find the path specified`
  hibával leállt. Saját képernyőkép így nem készült; a felhasználó
  később maga indított Play-menetet.

## Első élő Play-menet: az előnézet látható, a finomítás megszakadt

A felhasználó visszajelzése: „folyók megjelentek felületen”. Az új
[PerfLog_20260923_153831.txt](../../../unity/WorldGenViewer/Logs/PerfLog_20260923_153831.txt)
0 Myr, 50 m lépés, 96 forrás, négy worker adatait rögzíti:

| Esemény | Idő | Megjegyzés |
|---|---:|---|
| `Build() TELJES` | 2 993,1 ms | Főszálú Build |
| `river preview` | 4 529,6 ms a háttérmunka indításától | 96 ág, 59 986 renderpont |
| előnézeti mesh | 3 394,9 ms | Főszálú, jelentős egyszeri megakadás |
| `river cancel` | 19 586,2 ms | `completed=False`; nincs `river ready` vagy végleges `river mesh` |

A Build kezdetétől az előnézet naplózásáig a két mért szakasz összege
**7,523 s**. A korábbi menetekben még semmilyen folyó nem jelent meg;
ebben a menetben az előnézet megjelent. A 3,395 s-os főszálú mesh-idő
teljesítménykockázat. A felhasználói észlelésből a képminőség, a
folyófa sűrűsége és az akadás szubjektív hatása nem állapítható meg.
A finom négyworker-es út Unity-idejét ebből a **megszakított** futásból
nem számoljuk ki.

## Megszakítás nélküli élő Play-menet: a finom hálózat is elkészült

A felhasználó hosszabb Play-menetet futtatott; a naplósorokat nekünk kellett
kikeresni. A [PerfLog_20260923_180606.txt](../../../unity/WorldGenViewer/Logs/PerfLog_20260923_180606.txt)
0 Myr, 50 m lépés, 96 forrás és négy worker mellett egyetlen Buildet és
**megszakítás nélküli, kész hálózatot** mutat:

| Esemény | Mért idő |
|---|---:|
| Főszálú `Build() TELJES` | 2 968,1 ms |
| Durva előnézet a folyómunka indításától | 4 330,6 ms; 96 ág, 59 986 renderpont |
| Ebből előnézeti főszálú mesh | 3 205,3 ms |
| `[ND-132 river ready]` a folyómunka indításától | 144 682,8 ms; 96 folyó |
| Végleges főszálú mesh | 18 504,6 ms |
| Végleges mesh a folyómunka indításától | 163 188,0 ms |

A Build kezdetétől a végleges mesh naplózásáig **166,156 s (2 perc 46 s)**
telt el. A korábbi egyetlen kész ND-132 Unity-menetben a szekvenciális
`river ready` 1 477,626 s, a mesh 13,236 s volt. A jelenlegi teljes,
Build utáni 163,188 s körülbelül **9,1× rövidebb** a korábbi
1 490,862 s-nál, de a régi menet világideje és step-paramétere nem
szerepel a naplóban, valamint a terhelés nem kontrolláltan azonos;
ezért ez **tájékoztató összevetés, nem igazolt azonos körülményű
gyorsulás**. A `Build()` továbbra is kb. 3 s.

A végleges mesh 18,505 s-ig foglalta a főszálat, az előnézeti mesh
3,205 s-ig. Mindkettő jelentős frame-megakadás. A korábbi kész menet
végleges mesh-ideje 13,236 s volt; a mostani hosszabb, de a két
körülmény eltérhetett, és a mesh-kód számítási útját az A8/4 nem
módosította. A felhasználó korábban látta az előnézeti folyókat; arról,
hogy a véglegesre váltáskor mit látott és hogyan érzékelte az akadást,
még nincs külön visszajelzés. Deep-time váltás, hideg/meleg ismétlés,
Profiler-csúcsmemória és részletes B3 látványítélet továbbra is nyitott.

## ND-147: a mért főszálú akadás kezelése

A felhasználó jelezte, hogy a végleges mesh-váltás pillanatát nem
figyelte; az élő napló 18,505 s-os főszálú mérése ettől függetlenül
valós teljesítményhiba. Az [ND-147](../../04-decisions.md) szerint a
viewer most ugyanazt a `RiverPositionOnSurface` és `AddRiverRibbon`
számítást képkockák között, pontsorrendben, **4 ms-os felszínre illesztési
kerettel** végzi. Az előző, aktuális generációjú előnézet a teljes új
mesh elkészültéig látható; csak a kész hálózat válthatja fel. Az
előnézeti renderpont-köz 1 km-ről 4 km-re nőtt, mert az előző két
menet 59 986 pontja 3,2–3,4 s főszálú munkát igényelt. A durva Core-út
és a finom folyóhálózat numerikus eredménye változatlan.

Az offline `WorldGen.Viewer.Compile` fordítás a módosítás után 0 hibával
zárult. Az első új t=0 Unity Play-menet
([PerfLog 22:44:46](../../../unity/WorldGenViewer/Logs/PerfLog_20260923_224446.txt))
`Build()` ideje 3129,5 ms volt. A 96 folyós előnézet 3537,5 ms-mal a
folyómunka indítása után készült el, 16 056 renderponttal. A szakaszos
mesh-előkészítés falióraideje 2409,0 ms, aktív főszálú ideje 970,4 ms,
legnagyobb szelete 5,1 ms, egyszeri Unity-feltöltése 1,1 ms volt.
A Play ezután Pause állapotba került: a napló 22:45:10-kor megállt, a
felhasználó a szünetet 23:10 körül oldotta fel. A `river ready`
1496976,8 ms-os és az összesített 1544725,6 ms-os falióraideje ezért
**nem használható gyorsulásmérésre**.

A Pause feloldása után a 96 folyós finom hálózat is elkészült. A
`[ND-147 river mesh staged]` finom sora 400 696 renderpontot, 47 748,1 ms
előkészítési falióraidőt, 19 217,2 ms összesített aktív főszálú időt,
7,8 ms legnagyobb szeletet és 10,8 ms egyszeri Unity mesh-feltöltést mér.
Az `[ND-132 river mesh]` 19 228,0 ms értéke az aktív építés és feltöltés
összege, **nem egyetlen frame akadása**. Az előző menet 18 504,6 ms-os
egybefüggő főszálú mesh-munkájához képest a munka képkockákra oszlott;
azonos scene-paraméterű, szünet nélküli menet és Unity Profiler nélkül
a tényleges frame-csúcs még nem igazolt. A PerfLogban az
`[ND-147 river mesh staged]` sor külön mutatja a teljes előkészítési
falióraidőt, az aktív főszálú időt, a legnagyobb slice-ot és az egyszeri
Unity mesh-feltöltést. A mért 7,8 ms-os szelet és 10,8 ms-os feltöltés
meghaladta a 4 ms-os pontfeldolgozási keretet; ezek összege sem teljes
frame-idő. Az A8/B3 végső teljesítmény- és vizuális átvétele továbbra is nyitott.

**Felhasználói visszajelzés az új menet után:** a finom folyók látszanak,
de zoomoláskor „megakadnak”. A `Rivers` mesh ebben a változatban a
`BuildRiverNetwork` után statikus; a kamera- és terep-LOD mozgása nem
indít új folyómesh-építést. A napló a zoom közben `ND-95 request timing`
és `ND-85 upload slice` terep-LOD munkát mutat, de nem tartalmaz
összframe-/GPU-időt. A jelzés ezért vizuális vagy frame-teljesítményhiba
lehet; az érintett réteg és pontos tünet tisztázása nyitott. Nem tekintjük
az ND-147 teljesítmény- és B3 látványkapuját elfogadottnak.

**ND-148 folytatás (2026-09-25):** a felhasználó pontosította, hogy
zoomkor az egész kép fagyása és a folyóvonalak tereptől való lemaradása
egyaránt előfordul. A `PlanetView.unity` ellenőrzésekor kiderült: a
mentett `riverLineRadialBias` még 0,02, miközben a kód alapértéke és az
ND-49 magyarázata 0,5. A scene értékét 0,5-re javítottuk. Ez a
terepmesh mögé kerülő vonalszakaszok korábban igazolt kockázatát
csökkenti, de a mostani zoomtünet okát és megszűnését nem bizonyítja.
100 ms fölötti frame-résnél új `[ND-148 frame stall]` sor rögzíti a
delta-, az aktuális `LateUpdate`-időt, a zoomot és a függő folyó-/LOD-
munkát. Az offline `WorldGen.Viewer.Compile` Release build 0 hibával,
122 warninggal sikerült; élő Unity-kompiláció, szünet nélküli zoom-
mérés és képi elfogadás még nincs. Nyitott Editorban a külső scene-
fájlmódosítás nem feltétlenül frissül automatikusan: a `Planet Grid
Mesh` Inspectorban a River Line Radial Bias mezőnek ténylegesen 0,5-öt
kell mutatnia.

**ND-148 élő próba (2026-09-25, 22:25):** az új
[PerfLog](../../../unity/WorldGenViewer/Logs/PerfLog_20260925_222539.txt)
`Build()` ideje 2928,6 ms. A 96 folyós, 16 056 pontos előnézet a
folyómunka indulásától 3242,3 ms alatt készült el (5,0 ms legnagyobb
szelet, 1,1 ms feltöltés). Pause feloldása után a 400 696 pontos finom
mesh is elkészült: 45 943,1 ms előkészítési falióra, 18 744,3 ms
összesített aktív főszálú munka, 8,2 ms legnagyobb szelet, 12,3 ms
feltöltés. A `river ready` 270 438,8 ms és a teljes 316 382,7 ms a
Pause miatt nem alkalmas szünet nélküli sebesség-összevetésre.

A finom mesh elkészülése előtti 29 darab, fókuszált és 1 s alatti
`ND-148 frame stall` átlaga 147,4 ms, maximuma 243,8 ms; az aktuális
`LateUpdate` átlaga csak 0,14 ms, maximuma 2,4 ms. E rések többségénél
terep-LOD munka volt függőben, folyómesh viszont nem. A finom mesh
utáni, zoomarány 2,331-es szakaszban egy 100,2 ms-os, függő LOD-os
rés maradt; egy 17,2 s-os rés Pause/Editor-szünet lehet, ezért nem
számoljuk zoom-frame bizonyítéknak. **A felhasználó szerint mind a
folyóvonalak lemaradása, mind az egész kép megakadása javult** a 0,5-ös
Inspector-beállítású menetben. Az egész kép akadásának okát a bias-
változás nem bizonyítja; rövid, mérhető rés még maradt. A képi javulás
felhasználói visszajelzéssel igazolt, a teljes zoom-teljesítménykapu
nem zárható le Profiler és szünet nélküli ismétlés nélkül.

**Átvételi próba:** Unity fordítás után Play, egy t=0 Build; ellenőrizni
kell a `river preview` sort és a ténylegesen látható durva folyókat, majd
megvárni a `river ready` + `river mesh` sorokat és a finom vonalakra
váltást. Ugyanígy t=22 Myr; gyors egymás utáni deep-time váltásnál a régi
generáció `river cancel` után ne jelenjen meg. A PerfLogból hideg/meleg
Build-, preview-, ready- és mesh-időt, valamint frame-csúcsot és Unity
Profilerből csúcsmemóriát kell összevetni az A8/1 alapvonallal. A
képernyőképen az összefolyásokat, szélességeket és tavak/óceán
csatlakozását is ellenőrizni kell.

**Döntési állapot:** az offline 4-worker teljes Core-gyorsulás
2,0–2,2×; az előnézet Unityben látható, és egy 96 folyós finom Unity-menet
elkészült. Az azonos körülményű Unity-gyorsulás, az ND-145/B3 részletes
látványítélet, a hosszabb menet memóriahatása és az ND-147 mesh-akadás
élő újramérésének szünet nélküli megerősítése **nyitott**. Az ND-147
szakaszos finom mesh-e egyszer elkészült Unityben; a felhasználói
látvány- és megakadás-visszajelzés még hiányzik. A viewer-út emiatt
kísérleti marad. Ha egy későbbi
menetben `river preview` napló van, de folyó nincs, a `Rivers` GameObject,
mesh, anyag és takarás vizsgálata szükséges. Ha a `river ready` késik vagy
az előtér romlik, a szekvenciális referenciaút visszaállítása indokolt.

Branch `main`, kiinduló HEAD `e3713bf`, előzetesen is módosított munkafa.
Commit és push nem készült.
