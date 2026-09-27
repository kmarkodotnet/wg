# A10 / ND-152 — az ND-05 hibrid régió-szegmentálás lezárása

**2026-09-27**, `a19-plate-frame-noise` ág.
Feladat: a `todo2.md` **A10** tétele — „Régió-szegmentálás: az ND-05 hibrid
maradék két tagja". Döntés: `docs/04-decisions.md` **ND-152**.

## Kiindulás

Az ND-127 (2026-09-21) a hibrid ELSŐ tagját tette rendbe: a torkolat
óceán-tile-ja szerint kulcsolt nyers vízgyűjtőket összefüggő komponensekre
bontotta (cellák), szomszédsági gráfot épített rájuk, és agglomeratívan
összevonta a szárazföld 3%-áig. A partner-választás viszont kizárólag
GEOMETRIAI volt: a leghosszabb közös határ döntött. Az ND-05 másik két
tagja — biome-klaszter és domborzati törés — hiányzott. Az ND-127 záró
mondata előre kimondta a megoldás alakját: „ugyanezen a cella-gráfon csak
más összevonási költségfüggvény lenne".

A todo2 megjegyzése szerint ez „csak a B5 vizuális ítélet után érdemes" —
a felhasználó most explicit kérte, így a B5 nélkül készült el. A B5 tárgya
(a felbontás és a nevek megítélése) ettől NEM szűnt meg; a régiók
tile-halmazai megváltoztak, tehát a nevek is.

## Amit csináltam

**Élsúly a darabszám helyett.** Az ND-127 (b) szabálya (leghosszabb közös
határ) marad, de a határ hossza most SÚLYOZOTT: minden érintkező tile-él
alapsúlya 4, az azonos biome-ú él +3, a domborzati törésen átmenő él −3.
Egy tile-él súlya 1…7, mindig pozitív. Minden más — a cél alatti szomszéd
előnye, a méret- és `TileId`-döntetlenek, a kanonikus kimeneti sorrend —
változatlan, tehát az ND-127 mindhárom garanciája (partíció,
összefüggőség, kanonikus sorrend) konstrukció szerint megmarad.

**A törés-küszöb relatív.** A `|Δ magasság|` eloszlás p75-e, a cella-közi
élek teljes populációján. Fix méter-küszöb szintfüggő lenne (level 5-ön a
tile négyszer nagyobb területű), az ND-127 kifejezett célja viszont a
szintfüggetlen régiószám volt.

**Sorrend: referencia előbb.** `tools/reference/features_ref.py`
(`merge_watersheds_into_regions` + `_elevation_meters`,
`_terrain_break_threshold`) → vektor-generálás → C# → C# a vektorokhoz
mérve. A `MatchesPythonReferenceMergedRegionsExactly` BITPONTOS (csak egész
aritmetika; az egyetlen lebegőpontos lépés a `Math.Floor(e + 0.5)`, ami
IEEE-754 szerint bitpontos).

**Hívók.** Viewer panel/navigáció (`PlanetGridMesh.cs`) és
`OrdinalCalibration` — mindkettő a hibridet hívja. A kalibráció a biome-
mezőt a regolit-lánc MÁR kiszámolt hőmérsékletéből és csapadékából építi
(`BuildLandBiomeField`), nem proxyból.

## Mért eredmény

Seed `0xA7C944210000`, 20 lemez, víz 0,65, level 6, cél 258 tile, a
legnagyobb landmasson (4171 tile). Törés-küszöb: **230 m** (13 318
cella-közi él). A „vaktában húzott határ" várható értéke a p75 definíciója
miatt ~25%:

| | ND-127 (csak geometria) | ND-152 (hibrid) |
|---|---|---|
| régió | 11 | 9 |
| régióhatár-él | 410 | 296 |
| domborzati TÖRÉSEN | **21,0%** | **43,2%** |
| biome-VÁLTÁSON | **25,4%** | **41,2%** |
| átmérő/√terület (átlag) | 2,42 | 2,39 |
| átmérő/√terület (min–max) | 1,91–3,48 | 1,72–2,94 |

Méret-illesztett kontroll (több cél-méret, ahol a régiószám azonos): a
törés-illeszkedés 17,6–27,9% → 35,7–39,5%, a biome-illeszkedés
18,1–22,8% → 30,2–37,2%, tehát a nyereség nem a méretkülönbségből jön.

**Amit a mérés NEM állít.** A régión BELÜLI domináns-biome tisztaság
gyakorlatilag nem mozdul (±0,01). Ez a lépték következménye: egy 250–700
tile-os régió több klímazónát fog át, akármi is a határa. A biome-klaszter
tag nem homogén régiót ígér, hanem azt, hogy a HATÁR ott legyen, ahol a
biome tényleg vált.

## Újrakalibrálás

Az `OrdinalCalibration` ugyanezt az összevonást mintázza, tehát a
`SoilFertilityThresholds` populációja megváltozott — **v3**: 500 világ,
23 555 régió-minta (v2: 23 492), p20/p80 0,157734/0,230240 →
**0,164778/0,228259**. A CoastalComplexity ugyanebben a futásban BITRE
ugyanazt adta (14 895 minta), ami visszaigazolja, hogy csak a
talaj-populáció mozdult.

**KÜLÖN TALÁLAT, nem ehhez a tételhez tartozik.** Ugyanez a futás a
`HabitabilityThresholds`-ot is újramérte, és az ELAVULT:
repo 0.702860 / 0.747966 / 0.786213 / 0.823878, mért 0.893281 / 0.917461 /
0.934899 / 0.953615. Ugyanaz a parancs, ugyanazok a paraméterek. Az
összevonás a Habitability-be nem szól bele (World-szintű metrika,
kontinens-/régió-felosztástól független), tehát ez egy KORÁBBI, 2026-09-19
óta bekövetkezett hőmérséklet-lánc-változás nyoma — pontosan az a fajta
csendes elavulás, amit az `OrdinalQuantization` doksija maga is „semmilyen
teszt nem fogja meg"-ként ír le. Szándékosan NEM nyúltam hozzá: kívül esik
az A10 hatókörén, és a panel ordinális sávjait tolná el. Külön tételként
kezelendő.

## Ellenőrzés

- `dotnet test WorldGen.sln` — **1768/1768 zöld** (Core 705, Viewer 587,
  App 452, CLI 24).
- Új tesztek (`WatershedMergeTests`): `HybridTermsChangeTheSegmentation`,
  `PartialHybridInputFallsBackToTheGeometricPath`,
  `HybridBoundariesFollowTerrainBreaksAndBiomeChanges`,
  `HybridDoesNotMakeRegionsStringy`,
  `TerrainBreakThresholdIsRelativeAtEveryLevel` (level 5 és 6),
  `EdgeWeightStaysPositiveInTheWorstCase`. A meglévő ND-127-es
  garancia-tesztek mind a HIBRID úton futnak.
- Python referencia: a `features_ref.py` saját assertjei (partíció,
  összefüggőség, tisztaság) a hibrid kimeneten zöldek.
- `python tools/ci/check_burst_strict.py` — OK (301 fájl).
- Offline Unity kapuk: `WorldGen.Viewer.Compile`,
  `WorldGen.App.UnityBinding.Compile` — 0 hiba.
- Élő Unity Editor: `recompile` → `compilationFailed: false`,
  konzol-hibák: **0**.

## Verziózás

NEM seed-törő az ND-108 értelmében: a domborzat, a hidrológia, a
biome-osztályozás és a `FindWatershedRegions` bitre változatlan; az
összevonás továbbra is tiszta, utólagos prezentációs réteg. A régiónevek
viszont megváltoznak.

## Ami nyitva maradt

- **B5** — a régió-lista vizuális megítélése (felbontás, nevek). A
  tile-halmazok most változtak, tehát a B5 friss Play-menetet kér.
- A `HabitabilityThresholds` elavulása (fent).
