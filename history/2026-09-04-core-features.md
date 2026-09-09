# 2026-09-04 — halasztott Core-featureök C#-portja (branch: core-deferred-features)

Felhasználói kérés: a backlog #5-10 feladatai egyben, a végén ellenőrzés.
Ezek a halasztott, de a Python-referenciában MÁR KÉSZ Core-modulok — tehát
tiszta „port + verifikálás" (Python = igazság → C# a vektorokhoz mérve).
Külön branch (`core-deferred-features`), modulonként külön commit.

## Elvégezve (mind vektor-verifikált teszttel)

- **#6 M5 teljes hőmérséklet (ND-42)** — `Temperature.TemperatureKelvinFull`
  (T_radiative+greenhouse+ocean-altitude+weather+cycle). 200 vektor, 1e-6 tol
  (a radiatív sqrt(sqrt) vs Python pow ~1 ULP, mint a meglévő TemperatureKelvin).
- **#5 M5 szél/nedvesség/csapadék (ND-41)** — `WindPrecipitation` (zonális+Coriolis+
  termikus szél, párolgás, orografikus csapadék, stateless időjárás-zaj). 3×300
  vektor, 1e-6 tol (nyers Math.Sin/Cos/Tanh). `Math.CopySign` helyett kézi
  előjel (netstandard2.1).
- **#7 M7 tavak/jég/erózió (ND-43)** — `LakesIceErosion` (topografiai tó-detektálás,
  jég-osztályozás, statikus stream-power erózió). TEST-EARTH-001 500 vektor;
  a tengerszint egzakt, a lakeId/deposition a Python (face,u,v) sorrendjére
  igazítva, float 1e-6.
- **#8 M10 deep-time erózió+eljegesedés (ND-44)** — `DeepTimeErosionGlaciation`
  (exp relaxáció — TIMESTEP-INVARIÁNS zárt formula; szinuszos jégvonal-forcing).
  600 vektor, 1e-6 tol (Math.Exp/Sin).
- **#9 M11 lemez-életciklus (ND-45)** — `PlateLifecycle` (split/merge/rift,
  leszármazási fa, timestep-invariáns). Új RandomProperty 14-16; PlateMotion
  ulong-overload. 92+15 vektor: a fa-struktúra BIT-EGZAKT, a kompozit Rodrigues-
  pozíció 1e-9.
- **#10 M8 panel-metrikák** — `FeatureMetrics`: CoastTileCount, CoastalComplexity
  (geometriai), HabitabilityFraction (a most portolt hőmérsékletből, dokumentált
  sáv). **Soil fertility BLOKKOLT** (nincs talaj-modul) — dokumentálva.

## Állapot
Teljes Core teszt: **290/290 zöld** (a ~261 baseline + az új modulok). Nincs
regresszió. A klíma/deep-time modulok nyers Math.Sin/Cos/Exp-et használnak (a
referenciát követve) → cross-platform szigorú bit-determinizmus MÉG NINCS
garantálva ezekre (dokumentált, mint a meglévő Temperature-lánc); a
DeterministicMath-ra váltás + vektor-regenerálás későbbi lépés.

## Viewer-bekötés (folytatás, ugyanezen a branchen)
- **Tavak + állandó jég a viewerbe** (`PlanetGridMesh`, commit `ccf8dc3`) —
  a felhasználó kérésére (#5-10 Core-modulok addig NEM láttak a bolygón). A
  folyó-integráció mintájára: a referencia-szinten EGYSZER (Build) számoljuk a
  tavakat (`IdentifyLakes` a KÖZÖS priority-flood `filled`-jéből) és az állandó
  jeget (`AnnualTemperatureStats`+`ClassifyIce`, csak szárazföld), tároljuk
  (`_adaptiveLakeTiles`/`_adaptiveIceTiles`), a finomabb LOD-tile-ok az ős-tile
  állapotát öröklik (`IsAdaptiveLakeTile`/`IsAdaptiveIceTile`). Új
  `RenderCategory.Lake` (kék), az állandó jég `IceSheet` (fehér). Mind a 4
  klasszifikációs helyen (statikus Build, GPU-quad, CPU/GPU adaptív). Kapcsoló:
  `showLakesIce`. Unity-oldal, offline nem fordítható → felhasználói élő
  ellenőrzés. Prioritás: kráter > jég > tó > folyó > biome.

- **Szél-sebesség overlay** (`PlanetGridMesh`, commit `46df931`) — a felhasználó
  a „csapadék/szél" opciót választotta. FONTOS megállapítás: a `Precipitation`
  csak per-tile fv. külső `incomingMoisture`-rel; a Core-ban (és a Python-refben)
  NINCS nedvesség-advekció → a csapadéktérkép szárazföldön ~0 lenne. Ezért a
  felhasználó a szél-réteget választotta (a csapadék-advekciót külön, ref-first
  Core-munkaként hagyjuk — potenciális ND). A szél a verifikált `WindPrecipitation.
  WindVector`-ból jön; az elevation-gradiens a `ComputeElevationAtPoint` véges
  differenciája (nem új sim-matek). Egyetlen csomópont: `ContinuousCornerColor`
  (statikus + adaptív/párhuzamos út) + `ContinuousWaterCornerColor` (hogy az óceáni
  szélsávok ne tűnjenek el a víz alatt). `windSpeedOverlay` kapcsoló (alap: ki),
  `windSpeedColorMaxMs` rámpa-tető. Szín-cache módváltáskor ürül. Unity-oldal →
  élő ellenőrzés.

- **Deep-time erózió a csúszkán** (`PlanetGridMesh`, commit `38eb7c1`) — a
  `DeepTimeErosionGlaciation.UpliftRelaxationElevation` bekötése a meglévő
  `deepTimeMyr` csúszkára: a lemezhatár-hegységek uplift-bónusza idővel a maradvány-
  értékére (~35%, tau≈50 Myr) relaxál, az alap-eleváció változatlan. KONZISZTENSEN
  a geometriára (ComputeElevationAtPoint: base+uplift szétbontás, t=0-nál bit-
  azonos) ÉS a mezőre (field += relaxedUplift − uplift, ugyanazokkal a
  seedekkel/warppal → egzakt kivonás), a tengerszint-kalibráció ELŐTT, hogy a
  partvonal/biome egyezzen. Erózió-aktív állapotban (t≠0) a GPU-osztályozás
  helyett CPU (a shader nem eróziózik). Kapcsoló: `showDeepTimeErosion` (alap: be).
  Nincs Core-változás (nem seed-törő). Unity-oldal → élő ellenőrzés.
  Lehetséges folytatás: időfüggő jégvonal (`GlobalTempOffset`/`IsIced`) a
  jég-réteghez kötve.
- **Húzható idő-csúszka + világ-újraépítés config-változásra** (commit `9f3e0e7`)
  — kiderült, hogy NEM volt csúszka (a `deepTimeMyr` double, a Unity `[Range]`
  csak float/int), ÉS a `deepTimeMyr` változása addig csak a dinamikus LOD-ot
  építette újra a RÉGI `_adaptive*` állapottal → a világ sosem számolódott újra
  az új időre. Javítás: (1) `[Range(0,1000)]` float proxy (`deepTimeSliderMyr`),
  kétirányú szinkron OnValidate-ben; (2) `WorldConfigChangedSinceBuild()` (a
  Build()-kori snapshothoz mérve) → teljes `Build()`, fékezve
  (`minSecondsBetweenAdaptiveRebuilds`). Ez egyben javítja, hogy a
  windSpeedOverlay/showLakesIce kapcsolók a STATIKUS alapréteget (a felszín
  többsége) is frissítsék, amit az adaptív-only út nem tett.

## Nyitott (felhasználói megerősítést igénylő) MVP-konstansok
ND-42/43/44/45 illusztratív konstansai (greenhouse-érzékenység, jég-küszöbök,
erózió-együtthatók, lemez P_SPLIT/P_MERGE/élettartam) — a referenciák
explicit jelzik, hogy ezek megerősítendők, mielőtt „véglegesnek" tekintjük.
