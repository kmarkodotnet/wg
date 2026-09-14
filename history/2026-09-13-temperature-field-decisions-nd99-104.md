# 2026-09-13 — Pillanatnyi hőmérsékletmező: döntések rögzítése (ND-99–104)

## Előzmény

A felhasználó a zoomtól független, önállóan végezhető feladatok közül a
pillanatnyi hőmérsékletmező Core-részét választotta, előtte a munkafa
szétválasztott commitolását kérte.

## Commitok a munka előtt

A nem commitolt munkafa négy commitba került, `git add .` nélkül:

| Commit | Tartalom |
|---|---|
| `af70665` | ND-90 Core, Python-referencia, vektorok, CLI `.worldpkg` v2 |
| `c191083` | ND-96–98 viewer, tesztek, probe, architektúra/döntések |
| `6f7d4ba` | `docs/base_models`, nem Föld-szerű bolygó koncepció, léptetőgomb-napló |
| `3ba18bd` | kt_2 átadás, kt_1 átnevezés, startup-hivatkozások |

A backlog, a milestone-dokumentum és az M9-audit kt_2-bekezdései soronként
váltak szét: a viewer-commit köztes változatot kapott, az átadási commit a
véglegeset. Ellenőrzés a commitok előtt: `dotnet test WorldGen.sln` 765/765
(Core 384, viewer 373, CLI 8), Threefry KAT 9/9, a nyolc referencia/testdata
vektorpár SHA-256 szerint azonos. A `claude_sessions.md` a felhasználó fájlja,
nem került commitba.

## Döntésdokumentáció

- **ND-99:** a fővonal megtartja a kameramódos ND-62-t; a testvérág tengeri-jég
  ND-62-je merge-kor ND-99 lesz. A testvérág Core-változása csak a bitazonos
  `TemperatureKelvinFullPrecomputed` — nem feltétele a hőmodell-munkának.
- **ND-100:** lassú bázis + `θs`/`θa` anomália, referenciaegyenletek, a napi
  átlag csak a bázisban; paraméterek nyitottak.
- **ND-101:** level-6 sűrű rács, egész tickes `SimulationTime`, két puffer,
  spin-up, eldobható checkpoint; nyitott az integrátor (javaslat IMEX), a
  tickhossz és a rácsmetrika determinisztikus előállítása.
- **ND-102:** upwind élfluxusos, egyirányú széladvekció, külön `WindTick`,
  új determinisztikus szélút; nyitott a divergens szél kezelése.
- **ND-103:** `SurfaceThermalKind`, diagnosztikai határ.
- **ND-104:** Viewer adatút, overlay, UI, teljesítménycélok.

Architektúra: `docs/01-architecture.md` §11 (adatfolyam, javasolt interfészek,
kötelező tesztek, nyitott kérdések). Backlog-állapot frissítve.

Csak dokumentáció változott; új commit nincs (a felhasználó csak a korábbi
munkafa commitolását kérte).

## Commitok utólagos ellenőrzése

- Az `af70665` (csak ND-90) külön worktree-ben: Core 384/384, viewer 315/315,
  CLI 8/8 — a köztes commit önmagában is zöld.
- Az ND-90 Python-referenciája scratch-másolatban újragenerálva (mind a
  kilenc érintett generátor, rc=0): crust elevation, plate boundary,
  erosion/glaciation, hydrology, river path, lakes/ice, features és state hash
  bájtra egyezik a commitolt vektorral; a moisture transport
  tartalma CR-eltávolítás után egyezik — a `moisture_transport_ref.py` nem
  ad `newline="\n"`-t, ezért Windows alatt CRLF-et ír (régi, a munkától
  független sorvég-eltérés).

## 1. fázis: baseline

Scratchpad .NET 8 Release mérés, level 6, egy szál, ismételt futás bitazonos:
`TemperatureKelvin` 79 ms, közös Nap-mintás gyorsút 1,5 ms,
`TemperatureKelvinFull` 382 ms, `WindVector` 156 ms a teljes rácson.

## 2. fázis: első Python-mérések

- `tools/reference/thermal_grid_metrics_ref.py`: normalizált húrsokszög-terület
  ≤ 5,6·10⁻⁵ relatív eltérés a gömbi területtől, átlagos cellaél ~168 km
  (a doksiban korábban becsült ~180 km javítva).
- `docs/reviews/thermal-parameters-sources-2026-09-13.md`: letöltött és
  kivonatos források tételesen; 10 modellválasztás megerősítésre vár.
- `tools/reference/thermal_anomaly_column_ref.py`: Crank–Nicolson-IMEX
  másodrendű (szárazföldön 900 s-nál 0,025 K hiba), óránként középre
  igazított napi faktorral az évszakos drift ≤ 0,09 K; 0,12 m-es szárazföldi
  réteggel a napi amplitúdó túl nagy, M4 kiindulás 0,5 m-re módosítva.
- `tools/reference/thermal_advection_ref.py`: kompenzált upwind fluxusforma
  élközépponti széllel; konstans mező pontosan megmarad, divergenciamentes
  szélnél energia gépi pontossággal; az upwind diffúzió nagy.

Eredmények beírva: ND-100 6–7., ND-101 1–3., ND-102 1., 1b., 3. nyitott
kérdés; architektúra §11.1 és §11.5; backlog-állapot.

## Megvalósítás (a felhasználó jóváhagyta M1–M10-et és a javasolt sorrendet)

- **Python-referencia** `tools/reference/thermal_field_ref.py`: level-6 rács,
  bázis, determinisztikus szél, CN-IMEX solver, kanonikus spin-up; vektorok
  `thermal_field_vectors.json`, kétszeri futás bájtra azonos.
- **Első futás két hibát mutatott:** a napi faktoros bázis sarki éjszakán
  ~29 K, a termikus szél ~5700 m/s. Mérés (`thermal_seasonal_column_ref.py`):
  az éves bázisú változat óceáni memóriája miatt 60 nap spin-up után is
  5–12 K hibát ad (elvetve); a simított radiatív faktor (β = 0,5, M13)
  sarki szárazföldön −58 … +21 °C, 10 napos spin-up hiba ≤ 0,18 K
  (elfogadva, ND-100 módosítás).
- **Core** (`src/WorldGen.Core`): `Grid/DenseGridMetrics`,
  `Climate/{SimulationTime, SurfaceThermalKind, ThermalModelParameters,
  ThermalBaseline, ThermalWind, SurfaceTemperatureField}`. Tesztek:
  `SurfaceTemperatureFieldTests` 22/22. Egy valódi hibát a tesztek fogtak meg:
  a kanonikus folytatás nullázott, nem kanonikus snapshotot is elfogadott —
  a snapshot most rögzíti a kanonikus kezdőtickjét.
- **Viewer**: `Lod/ThermalOverlayPacking.cs` (.NET-teszt 6/6, viewer-suite
  379/379), `PlanetGridMesh.ThermalOverlay.cs`, `VertexColorUnlit.shader`
  overlay-ág, `SunController` publikus idő; hookok a `PlanetGridMesh.Update`
  elején és a Rétegek doboz végén (a `wg-c6` munkamenettel egyeztetve).
- Ideiglenes, megerősítendő: M11 (szélcsend-alsóhatár 1 m/s), M12 (édesvíz/jég
  hőkapacitása), M13 (β = 0,5). Öröklött kalibrációs korlát: egyenlítői óceán
  ~48 °C.
- Élő Unity-ellenőrzés: `docs/06-user-verification-checklist.md` 15. pont.

## Párhuzamos munkamenet

Ugyanebben a munkafában egy másik interaktív munkamenet (`wg-19`) dolgozik:
navigációs menü terve a backlogban, `PlanetGridMesh.cs` és egy saját history-
fájl. A közös dokumentumokat csak célzott szerkesztéssel módosítottuk, az ő
fájljaikhoz nem nyúltunk.
