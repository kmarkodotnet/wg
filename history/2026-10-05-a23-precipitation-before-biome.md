# A23 — A csapadék-mező és a vágópontok a biome-osztályozás elé (ND-194)

**Dátum:** 2026-10-05
**Ág:** `a19-plate-frame-noise`
**Tétel:** todo2 A23 (I4-sértés), döntés: `docs/04-decisions.md` ND-194
**Előzmény:** `history/2026-09-28-a7-nd164-biome-evaporation-switch.md`

## A hiba

A `PlanetGridMesh.Build()` a `biomeOf` szótárat a geometria-ciklusban töltötte
fel, a csapadék-mezőt (`GetOrComputePrecipitationField`), a sarok-táblát
(`BuildPrecipitationCornerTable`) és a három percentilis-vágópontot
(`ComputeThresholdsForVegetatedLand`) viszont csak a ciklus UTÁN számolta ki.
Minden Build tehát az **előző** Build mezőjével és vágópontjaival osztályozott.
Friss példány első Buildjénél `_adaptivePrecip == null` (→ `PrecipitationAtCore`
0-t ad) és a vágópont `(0, 0, 0)`.

## Mérőpad

Élő Unity Editor, Edit mode, `eval`. Friss `GameObject` + `PlanetGridMesh`,
a jelenet `Planet` objektumáról `EditorUtility.CopySerialized`-elt
beállításokkal (level 5 → 6144 tile), majd egyetlen `Build()`, és a privát
`_lastBiomeOf` / `_adaptiveBiomeThresholds` / `_lastField` / `_lastIsOcean`
visszaolvasása reflexióval. A friss példány azért kell, mert a jelenet
bejáratott objektuma szisztematikusan elrejti a sorrend-hibát (a második
Build után magától helyreáll).

Megjegyzés: friss példányon a hőmodell-cache hideg, ezért a hőmérséklet-tengely
az analitikus előnézet (ND-162) — ez a mérést nem érinti, mert a vizsgált
tengely a csapadék.

## Előtte

```
build#1: Ocean=3871 Desert=1672 Tundra=255 IceSheet=224 SeaIce=122
```

A meleg szárazföld **100%-a** (1672 tile) `Desert`. Ugyanakkor a Build végén
érvényes vágópontok: `arid=0,1536 semiArid=0,4933 moist=1,4554`, és a meleg
szárazföld interpolált csapadékából (`min=0,0000 p20=0,1536 med=0,5754
max=18,9739`) ténylegesen csak **335** tile esik az arid vágópont alá.
Globális csapadék: `n=6144 min=0,000 med=0,754 max=105,678`.

## A javítás

A teljes csapadék-blokk (mező + sarok-tábla + vágópontok + `_lastPrecipField`
+ PerfLog) a geometria-ciklus ELÉ költözött, közvetlenül a hőmodell-kapu
(`BuildClimateAirCornerTable`, `_adaptiveEvaporationTemperatureK`,
`_adaptiveClimateWind`) utáni PerfLog mögé. A blokk minden bemenete ott már
elő van állítva, a `GetOrComputePrecipitationField` pedig tiszta függvény a
serializált klíma-paraméterekre és ezekre a mezőkre — a folyó-forrás
kiválasztás (`SelectRiverSourcesPerBasin`) és a felhő-réteg így bitre ugyanazt
a mezőt kapja, mint korábban.

A régi helyre figyelmeztető komment került a blokk fölé: ha ide új fázis
kerül, a csapadék és a vágópontok MARADJANAK a biome-osztályozás előtt.

## Utána

```
build#1: Ocean=3871 Grassland=418 Rainforest=417 Savanna=386 Desert=335
         Tundra=255 IceSheet=224 SeaIce=122 TemperateForest=116
```

`Desert = 335` — pontosan annyi, amennyit az ugyanabban a Buildben érvényes
arid vágópont kijelöl (a fenti 335-tel egyező szám, nem közelítés).

**Teljes I4-ellenőrzés ugyanerre a Buildre.** Minden tile-ra visszaolvasva a
három bemenetet (`BiomeTemperatureKelvinAt`, `PrecipitationAtCore`,
`_adaptiveBiomeThresholds`) és újrafuttatva a
`BiomeClassification.Classify`-t: **mismatch = 0 / 6144 tile**. A panel
biome-térképe tehát pontosan a saját Buildje mezőiből következik.

## Fordítási / konzol-ellenőrzés

- `dotnet build tests/WorldGen.Viewer.Compile` → **0 Error** (133 warning, mind előzetesen is fennálló CS8632/CS0414).
- Unity `recompile` → `compilationFailed: false`, nulla hiba.
- Unity Console a mérések után: egyetlen error a saját `eval` 5 s-os
  főszál-időkorlátja (a cold Build 4249 ms + reflexiós mérés), nincs
  `Build()`-ből származó kivétel. A próba-objektumok törölve, a jelenet
  nem lett módosítva (`isDirty=False`).

## Nem dől el ezzel

- A `(0,0,0)` vágópont mint konvenció (üres eloszlásnál) változatlan marad —
  most már csak akkor fordulhat elő, ha tényleg nincs vegetált szárazföld.
- A hőmodell hideg vége (A24) és az ND-165 továbbra is nyitott.
