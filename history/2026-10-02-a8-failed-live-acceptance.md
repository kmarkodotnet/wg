# A8 — sikertelen élő átvétel és klímaadapter-javítás

A felhasználó `pics/p.png` képe alapján a folyók nem pusztán pixelesek:
téglalapos/lépcsős nyomvonalak. A hálózat közelről nem életszerű, rövid,
nem összefüggő, torkolatok nem láthatók. A folyók megjelennek és zoomkor
a felszínen maradnak. Console: 63 `KeyNotFoundException`,
`PlanetGridMesh.Build`, a fizikai klíma regolit-adapterének szótárolvasása.

## Bizonyíték és határ

A friss `PerfLog_20261002_202422.txt` két t=0 folyófeladatot indít.
Az első 5,566 s után megszakad a klíma-cache átvételével járó új Buildnél.
A második előnézete 14 619 ponttal 2,763 s alatt elkészül (max slice
4,1 ms, upload 1,3 ms), a feladat 119,760 s után megszakad. Nincs
`river ready` vagy finom `river mesh staged` sor. Ez az előnézet élő
elutasítása, nem a kész finom hálózat alakjának bizonyítéka. A teljes
hálózatról jelzett problémákat ettől nem tekintjük megoldottnak.

A durva előnézet a level+4 rács négyirányú útját rajzolja; a köztes
gömbi renderpontok nem szüntetik meg a rács lépcsős alakját. A
`riverBaseHalfWidth=0,08`, radius=100, fizikai sugár=7420 km mellett
a legkisebb ág megjelenített teljes szélessége kb. 11,9 km. Ez meglévő
vizualizációs paraméter, nem fizikai mederszélesség-mérés. Önkényes
szélességcsökkentés vagy spline nem igazolná a vízrajz helyességét.

## Javítás (ND-176)

- A `CaptureThermalClimateInputs` a hidrológia után, a klíma első
  fogyasztása elé került. Idő-/seed-/rácsszintváltáskor a régi klíma
  nem használható az új Buildben.
- A fizikai adapter regolit-hőmérséklete `SurfaceAllK`, a teljes valódi
  éves felszíni mező; a jég fennmaradási jele külön marad.
- Core-algoritmus és seed-kompatibilitás nem változik.

Offline viewer Release fordítás: 0 hiba (a warningok elnyomva).
Külön projektmásolatos Unity Build-regresszió **PASS**: régi fizikai klíma
mellett L5/0 Myr → L4/22 Myr váltás két tényleges Builddel, a régi klíma
fogyasztás előtt érvénytelen, nincs hiányzó kulcs és az új Build analitikus
előnézetet használ. Eredmény: `artifacts/a8-unity-regression.txt` és `.log`.
Ez batch/CPU fogyasztói ellenőrzés, nem a felhasználói jelenet Play-próbája
vagy folyólátvány-átvétel. Az első tesztharnessben
a második Buildet a függő LOD sorba állította, ezért nem volt érvényes
fogyasztói próba; a javított harness kikapcsolja a háttér-LOD-t.

A8/B3 nincs lezárva. Hátra: kész finom hálózat elkészülése a javított
életciklusban, végállapot-/csatlakozásdiagnózis és új képi ellenőrzés;
továbbra is kontrollált teljesítmény-, megszakítás- és memóriapróba.
Commit/push nem készült. Az előző A24 munkafabeli változásokat megőriztük.
