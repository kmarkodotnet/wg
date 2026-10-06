# A26 / ND-196 — csapadék-arányos folyó-forrás kvóta (SEED-TÖRŐ, generátor 10 → 11)

**Dátum:** 2026-10-06
**Ág:** `a19-plate-frame-noise`
**Kiváltó:** a felhasználó a 2026-10-05-i ND-196 mérés után a **(b) opciót**
választotta („csapadék-arányos kvóta abszolút alsó kapuval").
**Döntés:** `docs/04-decisions.md` ND-196 (LEZÁRVA), 5. pont.

## Rövid válasz

A folyó-forrás kvóta mostantól a vízgyűjtők **csapadék-összegével arányos**.
A mért hatás azon a mezőn, ahol a hiba keletkezett (a hőmodell párolgásával):
a **nulla csapadékú forrás 48/96 (50,0%) → 0 (0,0%)**, a nyomvonal-pontok
**55,6% → 8,1%**-a fut nulla csapadékú szárazföldön, a medián
csapadék-percentilis **0,0 → 75,4**, miközben a forrás-szám (96) és a
szárazföldi folyóhossz (43 852 → 44 995 km) **megmaradt**.

## 1. Először a mérőpad lett újrafuttathatóvá

Az ND-196 felvételi mérése élő Unity Editorban, reflexióval készült — tehát
nem volt megismételhető, és egy javítás hatását nem lehetett volna rajta
mérni. Ezért a `tools/diagnostics/RiverBaseline` két új módot kapott:

- `biome` — az ANALITIKUS csapadék/hőmérséklet-lánc (a viewer előnézete);
- `thermal` — a HŐMODELL éves éghajlata, ugyanazzal a Core-lánccal, amit a
  viewer háttérszála futtat: `ThermalClimateCalculator.Compute` (level 5,
  `includeRefinedWind: true`) → `Refined.MeanSurfaceK` + éves szél →
  `MoisturePrecipitation.ComputeWithClimateFields`.

**Az egyezés mért, nem feltételezett.** A `thermal` út ugyanazt a három
számot adja, mint az Editor-beli felvétel:

| ellenőrzött szám | ND-196 (Editor) | mérőpad (CLI) |
|---|---|---|
| nulla csapadékú szárazföld | 1124 / 2151 (52,3%) | 1124 / 2151 (52,3%) |
| nulla csapadékú forrás | 48 / 96 | 48 / 96 |
| szárazföldi nyomvonal-pont | 468 619 | 468 619 |

A medencénkénti forrás-percentilis listák is egyeznek (93,4 / 92,9 / 91,9 …,
98,5 / 98,3 / 96,7 …). A hőmodell a CLI-ben (.NET 8) 17,1 s, a teljes
folyóhálózat 4 workerrel 71-73 s — a teljes mérés ~95 s, tehát
előtte/utána összevethető.

## 2. A megvalósítás

`RiverPathTracing.SelectRiverSourcesPerBasin`, öt pont:

1. egy medence SÚLYA a saját JELÖLT-tile-jainak csapadék-összege
   (jelölt = hegyvidéki ÉS pozitív csapadékú), a MÁR RENDEZETT listán
   összegezve (nincs szótár-bejárási sorrend-függés);
2. **abszolút alsó kapu** (`DefaultMinSourcePrecip = 0,0`, szigorú >): nulla
   csapadékú tile nem lehet forrás, akkor sem, ha a medencéjén belül éppen ő
   a legnedvesebb;
3. nulla súlyú medence nem kap forrást, és **nem is foglal medence-HELYET**;
4. a keret FIX (`basinCount × sourcesPerBasin` = 96), és a súlyokkal
   arányosan oszlik (Jefferson/D'Hondt, döntetlennél a kisebb medence-index
   javára), `DefaultMaxSourcesPerBasin = 2 × 6 = 12` felső korláttal;
5. a kiosztás **inkrementális**: minden keret-egység azonnal megpróbál
   forrást felvenni, és ha a medence kimerült, az egység a következő legjobb
   medencére szállt át.

## 3. A 3. és 5. pont MÉRÉSBŐL jött, nem tervből

A naiv (b) — előre kiosztott kvóta a 16 legnagyobb medencére — **rontotta** a
folyók számát:

| változat | forrás | szárazföldi hossz | nulla csapadékú forrás | nyomvonal nulla csapadékon |
|---|---|---|---|---|
| előtte (fix 6/medence) | 96 | 43 852 km | 48 (50,0%) | 55,6% |
| naiv (b), előre kiosztott kvóta | **66** | 31 485 km | 0 | 11,1% |
| + a száraz medence nem foglal slotot | **70** | 31 710 km | 0 | 5,2% |
| + inkrementális kiosztás (ez a végleges) | **96** | 44 995 km | **0** | **8,1%** |

Ok: a 16 legnagyobb vízgyűjtőből 7-nek PONTOSAN nulla a jelölt-csapadéka,
a kvóta pedig olyan medencékbe is jutott, ahol elfogytak a jelöltek (kevés
nedves hegyvidék, vagy a 150 km-es forrás-szeparáció nem engedett többet).
A (b) ígérete („megtartja a forrás-számot") csak az 5. ponttal teljesül.

## 4. Mért végállapot (seed 0xA7C944210000, level 5, t = 0)

Hőmodell-mező (ez van a felhasználó képernyőjén):

| metrika | előtte | utána |
|---|---|---|
| forrás | 96 | 96 |
| nulla csapadékú forrás | 48 (50,0%) | **0 (0,0%)** |
| forrás csapadék-percentilis (medián) | 52,3 | **69,7** |
| nyomvonal-pont percentilise (medián) | 0,0 | **75,4** |
| nyomvonal-pont percentilise (átlag) | 32,7 | **68,3** |
| nyomvonal nulla csapadékú szárazföldön | 55,6% | **8,1%** |
| szárazföldi folyóhossz | 43 852 km | 44 995 km |

Biome-gazdagodás ugyanitt (a mérőpad NYERS tile-értékkel osztályoz, a viewer
az interpolált sarok-táblával — **ezek az arányok tehát nem ugyanarra a
populációra vonatkoznak, mint az ND-196 1. pontjának viewer-táblája**):
`Desert` 0,89 → **0,48**, `Tundra` 1,06 → **0,08**, `Savanna` 0,21 → 3,98,
`TemperateForest` 1,01 → 2,11, `Grassland` 1,51 → 2,18, `Rainforest`
0,79 → **1,03**.

Analitikus (előnézeti) mező: 96 forrás, nulla csapadékú forrás 15 → **0**, a
nyomvonal 2,7%-a fut nulla csapadékú szárazföldön, hossz 46 588 → 47 269 km.

## 5. Ellenőrzés

- **1995/1995 teljes regresszió Release ÉS Debug** (Core 871, Viewer
  LodChunking 664, App 452, CLI 24). A Core-ban 4 új ND-196-teszt: csapadék-
  arányos kvóta, nulla csapadékú medence, nulla csapadékú tile, felső korlát,
  plusz a „nedvesebb, de kisebb medence több forrást kap" kapu.
- **Két meglévő teszt tudatosan átírva**: a kvóta-megoszlás (3+3 → 5+1,
  csapadék-arányos) és a méret-küszöb tesztje (a FIX keret miatt nem a
  darabszám, hanem a kis medence részvétele a mérték).
- **`thermal_checkpoint_vectors.json` újragenerálva** a Python orákulumból; a
  diff pontosan a `"10"` → `"11"` generátor-sztring és a belőle származó két
  hash (a `ThermalCheckpoint` a generátorverziót a fejlécbe írja).
- ND-20 Burst-kapu OK (343 fájl), mindkét Unity offline fordítási kapu
  0 hiba.

**Gyorsítótár-következmény:** a `ThermalCheckpoint` a generátorverziót a
fejlécbe írja, tehát a `ModelIdentity` és vele MINDEN hőmodell-gyorsítótár
kulcsa megváltozik — az első Build a 10 → 11 emelés után újraszámolja az éves
éghajlatot (az ND-192/195 szinkron útja téveszt, a `climate_keys.v1.txt`
fejléce elavul), utána ismét ms-os a betöltés. Ez az ND-160 és az ND-189
emelésekkel azonos, ismert költség.

Ellenőrizve az élő Editorban: újrafordítás után `compilationFailed: false`,
**0 konzol-hiba** (188 warning, mind korábbi).

## 6. Nyitva marad

1. A biome-gazdagodás **viewer-oldali**, interpolált táblája (az ND-196 1.
   pontjának 2,45×-ös `Desert` száma) csak élő Play-menetben mérhető — ez a
   felhasználói átvétel tárgya.
2. A csapadék-mező 52,3%-os nulla-aránya továbbra is az **A24 / ND-165**
   kalibrációs kérdése, és ettől a döntéstől független. Ha az javul, a (b)
   automatikusan jobb elhelyezést ad.

Mérési kimenetek: `artifacts/nd196-before-thermal.txt`,
`artifacts/nd196-after3-thermal.txt`, `artifacts/nd196-after3-analytic.txt`,
`artifacts/nd196/` (medence- és gazdagodás-CSV-k).
