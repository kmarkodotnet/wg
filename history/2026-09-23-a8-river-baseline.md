# 2026-09-23 — A8/1: folyóhálózati mérési alap és döntési kapu

## Konfiguráció és hatókör

- Munkafa: `main`, kiinduló HEAD `e3713bf`; a mérés kezdetén más feladatból származó, nem commitolt A7/tektonikai módosítások voltak jelen. Az A8 nem váltott branchet, nem commitolt és nem pusholt.
- Gép: AMD Ryzen 7 5700X, 16 logikai processzor, Windows 10.0.26200 x64; `dotnet` SDK 10.0.301, a mérőprogram `net8.0` Release. A Unity Editor külön processzként futott; a Core-mérések a .NET futtatóban történtek, nem a Unity Mono alatt.
- Scene alapbeállítás: `worldSeed=0xA7C944210000`, 20 lemez, `level=5`, vízarány 0,65, `climateDayT=10`, keringési periódus 365,25 nap, forgási periódus 1 nap, tengelyferdeség 23,44°, `riverRefinementStepMeters=50`, `fineDepth=4`, 16 medence × 6 forrás. A t=0-ból számolt csapadékmezőből ugyanaz a 96 forrás kerül mindkét időállapotba. A t=22 Myr a lemezmagokat a `PlateMotion.MovedSeeds` útján mozgatja.
- A forrásválasztás `MoisturePrecipitation.Compute` → `FlowNetwork.PriorityFlood` → `SelectRiverSourcesPerBasin`. A nyomkövetés sorrendje a viewer szekvenciális `BuildContinuousRiverNetworkFromSources` sorrendje. A diagnosztika ugyanazt a `TraceRiverPathContinuous` hívást és ugyanazt a `claimed` bejegyzést végzi forrásonként; az opcionális `onPitEscape` számláló a spillway-keresés előtt hívódik, és nem módosít elágazást vagy numerikus értéket.
- Az első próba tévesen a Core alapértelmezett `dayT=0` értékét használta. A scene `climateDayT=10` értékét a viewer hívásláncában ellenőriztük, a próbát kijavítottuk, és a teljes hárommenetes sorozatot újrafuttattuk. A lent verziózott nyers adatok kizárólag a javított bemenetből származnak.

## Nyers állományok

- [sources.csv](a8-2026-09-23/sources.csv): mind a 96 forrásindex és `TileId.Value` hexadecimálisan. A két időállapot és a t=0 ismétlés forráslistája byte-azonos; a fájl SHA-256-a `379f70d7c4c438eac174b700f79e6165a14f518c4e91c3d616e0df5ff8826f92`.
- [t0-profile.csv](a8-2026-09-23/t0-profile.csv), [t0-repeat-profile.csv](a8-2026-09-23/t0-repeat-profile.csv), [t22-profile.csv](a8-2026-09-23/t22-profile.csv): forrásonként nyomkövetési és lefoglalási idő, pont-/lépésszám, pit-escape hívás, végállapot, befogadó index, csatlakozási pontindex és a kumulatív `claimed` elemszám. A `basinRound` oszlop az `index / 6` medenceblokk, 0–15.
- [basin-summary.csv](a8-2026-09-23/basin-summary.csv): a két teljes menet medencénként összesített nyomkövetési ideje, pontszáma, pit-escape hívása és beolvadása; a profilokból számítva.
- [t0-independent-tails.csv](a8-2026-09-23/t0-independent-tails.csv): a 17 beolvadó forrás `claimed` nélküli teljes útja és az eldobandó többletlépések.
- [t0-fingerprint.txt](a8-2026-09-23/t0-fingerprint.txt), [t0-repeat-fingerprint.txt](a8-2026-09-23/t0-repeat-fingerprint.txt), [t22-fingerprint.txt](a8-2026-09-23/t22-fingerprint.txt): hálózati SHA-256 és `ComputeDischargeWeights` teljes vektora.
- Unity nyers naplók: [2026-09-22 19:07](../unity/WorldGenViewer/Logs/PerfLog_20260922_190731.txt), [2026-09-23 08:51](../unity/WorldGenViewer/Logs/PerfLog_20260923_085121.txt), [2026-09-23 09:32](../unity/WorldGenViewer/Logs/PerfLog_20260923_093258.txt). Ezek meglévő Play-menetek, nem ehhez a diagnosztikai futtatóhoz készített kontrollkísérletek.

A hálózati lenyomat `BinaryWriter` little-endian bájtsorozatának SHA-256-a: `int32` folyószám; folyónként `uint64` forrás-`TileId.Value`, `int32` forrásindex, végállapot, befogadó index, vízhozamsúly, pontszám; majd minden pont X/Y/Z `double` értékének `Int64Bits` bitmintája. Így a koordináták összehasonlítása bitpontos. A renderelt képet ez a hash nem vizsgálja.

## Idők és topológia

| Menet | 96 folyó elkészülése | Pont | Lépés | Pit-escape | Beolvadás | Medencék közötti beolvadás | Lenyomat |
|---|---:|---:|---:|---:|---:|---:|---|
| t=0, első offline | 149,984 s | 400 698 | 388 012 | 398 | 17 | 3 | `3a7940c6b2b0faab…dd046d8` |
| t=0, második offline | 141,808 s | 400 698 | 388 012 | 398 | 17 | 3 | ugyanaz, teljes 64 hex jegyen |
| t=22 Myr, offline | 101,891 s | 284 834 | 276 588 | 276 | 17 | 2 | `c0c1a8ff3b6ad283…2a4293` |

A t=0 első mérésből a nyomkövetés 149 908,6 ms, a `claimed` bejegyzése 36,2 ms; a maradék a mérőprogram és hash költsége. A t=22 mérésből 101 834,1 és 31,1 ms. A hat első t=0 forrás diagnosztikai és eredeti szekvenciális hálózatépítőjének minden koordinátája és topológiai mezője bitre megegyezett (`canonicalMatch=True`). A két teljes t=0 futás lenyomata szintén egyezett.

A t=0 öt legdrágább forrása: 12 (5 951 ms, 11 948 lépés, Ocean), 14 (4 840 ms, 10 063, Ocean), 48 (4 823 ms, 6 744, Pit), 32 (4 232 ms, 4 755, Pit), 21 (3 724 ms, 7 173, Pit). A legdrágább medenceblokk a 2-es: 16 520 ms / 6 forrás. t=22-nél a legdrágább forrás a 12-es: 8 246 ms / 17 404 lépés / 21 pit-escape.

Az első kiválasztott források (0, 6, 12, …, 90) nyomkövetése t=0-nál összesen 33,732 s, t=22-nél 25,234 s. Ez a teljes munka 22,5%, illetve 24,8%-a; nem önállóan elérhető gyorsulás. t=0-nál a 17 beolvadó ág `claimed` nélküli befejezése további 38 797 lépés lenne, a jelenlegi 388 012 lépés 10,0%-a. Az önálló teljes futásuk összesen 47,919 s volt egy külön menetben; ez nem azonos terhelésű páros CPU-időmérés, így a különbségéből nem adunk megtakarítási százalékot.

## Unity PerfLog és élő bizonyíték határa

| Napló / esemény | `Build() TELJES` | Folyómunka | Mesh |
|---|---:|---:|---:|
| 2026-09-22 19:07, generáció 6 | 3 012,4 ms | `river ready` 1 477 626,3 ms az indítástól | 13 235,8 ms |
| 2026-09-23 08:51, generáció 1 | 3 384,7 ms | 244 984,8 ms után megszakadt | nincs |
| 2026-09-23 09:32, generáció 1 | 2 970,3 ms | 140 556,2 ms után megszakadt | nincs |

Az első sor az egyetlen talált kész, ND-132 szerinti 96 forrásos Unity-menet: a mesh naplózásáig a Build után legalább 1 490,9 s telt el. A napló nem tartalmaz megbízható, az eseménnyel együtt kiírt világidő/step paramétereket és vizuális megjelenésre vonatkozó képernyőképet. A megszakított meneteket nem számítjuk kész hálózatnak. Az offline t=22 és az 1 477,6 s-os Unity falióraidő eltérésének oka nem igazolt: a Mono/Editor, háttérterhelés és más világállapot hatása nincs kontrolláltan szétválasztva. Azonos paraméterű hideg/meleg Unity-kísérlet, deep-time váltás utáni kész mesh, valamint a B3 használhatósági ítélet hiányzik.

## Döntés

**A8/2, a források naiv round-major átütemezése: elvetés.** t=0 mellett a 7-es medence második forrása, a 43-as, a kanonikus sorban az 5-ös medence harmadik forrásába, a 32-esbe olvad. A minden medence második forrását a harmadik kör előtt feldolgozó sorrend a 43-ast a 32-es elé tenné; ekkor a szükséges `claimed` bejegyzés még nincs jelen, tehát a hálózat megváltozna. A korábbi ágaktól független teljes nyomvonal számítása lehetséges, de a kanonikus sorrendű csonkolás szükséges, és t=0-nál 38 797 eldobott lépéssel járna. Ezt az utat a meglévő offline párhuzamos API már megvalósítja. A viewerbe való visszahelyezése az ND-132 szálkészlet-versengési mérése miatt külön kontrollált teljesítményvizsgálatot igényel. Az élő B3-várakozási ítélet továbbra is nyitott.

## Újrafuttatás

```powershell
dotnet run --project tools/diagnostics/RiverBaseline/RiverBaseline.csproj -c Release -- 0 artifacts/a8-river-baseline-scene 96
dotnet run --no-build --project tools/diagnostics/RiverBaseline/RiverBaseline.csproj -c Release -- 0 artifacts/a8-river-baseline-scene/repeat 96
dotnet run --no-build --project tools/diagnostics/RiverBaseline/RiverBaseline.csproj -c Release -- 22 artifacts/a8-river-baseline-scene 96
dotnet run --no-build --project tools/diagnostics/RiverBaseline/RiverBaseline.csproj -c Release -- 0 artifacts/a8-river-baseline-scene independent
dotnet run --no-build --project tools/diagnostics/RiverBaseline/RiverBaseline.csproj -c Release -- 0 artifacts/a8-river-baseline-scene/verification 6
```

Az `independent` mód az előző t=0 profilra támaszkodik. A kész hálózat mérése alatt ne fusson más CPU-benchmark. A Unity Play-kontrollhoz azonos konfigurációval indíts hideg Buildet, várd meg a `river mesh` sort, ismételd meg ugyanazon világidőn, majd válts 22 Myr-ra és ott is várd meg a mesh-t; a `river cancel` sorokat jelöld külön. A logot és egy kész folyóhálózatról készült képernyőképet együtt értékeld.

Ellenőrzés: a teljes `WorldGen.sln` fordítás 0 hibával és 0 figyelmeztetéssel, a Core Release tesztcsomag 596/596 eredménnyel zárult. A viewer offline forrásfordítása 0 hibával és 122 már meglévő nullable figyelmeztetéssel zárult. Élő Unity fordítási/Play/vizuális ellenőrzés nem történt.

**B3 visszajelzés után:** a felhasználó nem lát folyókat Play módban. Az
új PerfLog hat megszakított folyómunkát mutat; a külön [B3/ND-145
napló](2026-09-23-a8-b3-river-preview.md) rögzíti a hiány okát és a
modellalapú előnézeti javítást. Az A8/2 round-major elvetése változatlan.
