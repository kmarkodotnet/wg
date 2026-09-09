# 2026-08-30 — REKONSTRUÁLVA git-előzményből (az eredeti beszélgetés elveszett)

Ez a fájl NEM éles beszélgetés-napló — a `git log`, a commit-üzenetek és a
`docs/04-decisions.md` alapján lett utólag összeállítva, mert egy 2026-08-31-i
munkamenet nem látta ezt a munkát a saját kontextusában. Az időbélyegek
git commit-időbélyegek (helyi idő).

## 00:17 — `519e80f` docs: M9 adaptív LOD terv + ND-40

Döntés: a `PlanetGridMesh` fix egészgömbös build helyett adaptív,
kamera-vezérelt kvadfa-LOD-ra vált (ND-40, "C" opció — adaptív geometria +
megtartott fix level-6 referencia-passz a tengerszinthez/panelekhez, hogy a
partvonal ne remegjen zoomkor). Indoklás: a felhasználó panaszolta, hogy
zoomoláskor a domborzat pixeles/durva marad, mert a mesh nem sűrűsödik a
kamera közelében.

## 12:21 — `4a61fbd` M9: adaptív kvadfa-LOD implementáció + 5 Python-referencia (ND-41..45)

Ez a legnagyobb egyetlen commit ezen a napon (534 sor `04-decisions.md`,
78 sor `05-milestones.md`, ~44000 sor új tesztvektor+referencia). Két,
egymástól független munka egy commitba összefésülve:

1. **Adaptív kvadfa-LOD C#-implementáció**: `WorldGen.Viewer.Lod.AdaptiveQuadTree`
   (motorfüggetlen, unit-tesztelt), hiszterézises split/merge, 2:1
   kiegyensúlyozás, geomorphing, frustum culling, perzisztens LRU sarok- és
   klasszifikáció-cache. `PlanetOrbitCamera` zoom exponenciálissá alakítva.
2. **5 párhuzamos `reference-dev` munkamenetből (Python-referencia, C#-port
   NÉLKÜL)**:
   - ND-41 — szél/párolgás/csapadék (`tools/reference/wind_precipitation_ref.py`)
   - ND-42 — teljes hőmérséklet-modell, `T_greenhouse`/`T_ocean`/`T_weather`/`T_cycle` (`temperature_ref.py::temperature_kelvin_full`)
   - ND-43 — tavak/jég/hó/A1 eróziós pass (`lakes_ice_erosion_ref.py`)
   - ND-44 — erózió idővel + eljegesedés-ciklusok, zárt alakú relaxáció (`erosion_glaciation_deep_time_ref.py`)
   - ND-45 — lemez-életciklus: split/merge/rift, `PlateId` `ulong`-ra váltás (seed-törő) (`plate_lifecycle_ref.py`)

**FONTOS NYITOTT TÉTEL:** mind az 5 (ND-41–45) csak Python-referencia szinten
kész — a C#-port egyikhez sem történt meg (ld. `docs/backlog.md`).

## 12:29–13:32 — `33fc186`, `d6f1406` gpu-calc: párhuzamosítás

`Parallel.For` a per-tile Core-kiértékelésre + a kvadfa-kiválasztás/balance
párhuzamosítása (a tényleges szűk keresztmetszet) + kamera-érzékenység
hangolás.

## 13:36–14:04 — `a498aff`, `4f1d002`, `b1ad99e` PlanetOrbitCamera forgás-sebesség, 3 kör

Iteráció: magasság-padló eltávolítva → állandó (nem magasság-függő) sebesség
+ gyorsabb zoom → **explicit felhasználói kérésre visszaállítva** magasság-
arányos sebességre. (Ez a felhasználó saját korábbi, "minél közelebb annál
lassabb" kérésének megfelelő beállás — ld. a 2026-08-31-i napló elején
említett korábbi kontextust is, ami ugyanezt a döntést írja le egy MÁSIK,
korábbi körből — a két kör valószínűleg ugyanazt a beállítást járta be
kétszer, különböző munkamenetekben.)

## 14:47 — `48e3fbb` incremental-mesh-buffers

Statikus alapréteg (egyszer épül) + dinamikus finomított réteg
szétválasztása — teljesítmény-optimalizálás, hogy ne kelljen a teljes mesh-t
újraépíteni minden LOD-váltásnál.

## 16:12 — `1e9691b` "setup"

Vélhetően egy branch-váltás/scaffolding commit a következő (GPU compute
pipeline) munkához — a commit-üzenet nem informatív.

## 21:41 — `0f907f5` gpu-compute-pipeline: TileClassification.compute + GpuTileClassifier dispatch (jelen HEAD a `screen-space-lod` ágon)

GPU-oldali (float32, HLSL) port a determinisztikus lánc egy részéről
(Threefry4x64 → DeterministicRandom → FractalNoise/DomainWarp →
PlateGeneration/PlateBoundaryEffect/CrustElevation → OrbitalMechanics →
Temperature → BiomeClassification), **kizárólag** a Unity-viewer
tile-klasszifikációjához (megjelenítés, NEM a perzisztált világmodell —
I1/I2 a `src/WorldGen.Core`-ra vonatkozik, ez tisztán render-oldali
reprodukció, dokumentált CPU/GPU eltérésekkel). A Threefry4x64 uint2-alapú
64-bit-emulációja C#-ban 9/9 hivatalos Random123 KAT-vektorral ellenőrizve,
mielőtt HLSL-be került. `useGpuClassification` alapból KIKAPCSOLVA. A
`.compute` fájl élő Unity-tesztelést igényel (ebből a környezetből nem
futtatható).

**Ez a commit után a munka NEM ért véget** — 2026-08-31-én a munkafában
további, COMMITOLATLAN módosítások voltak ugyanezeken a fájlokon
(`GpuTileClassifier.cs`, `TileClassification.compute`, `AdaptiveQuadTree.cs`,
`PlanetGridMesh.cs`, `AdaptiveQuadTreeTests.cs` + nagy `PlanetView.unity`
diff) — ez volt a folytatás kiindulópontja.
