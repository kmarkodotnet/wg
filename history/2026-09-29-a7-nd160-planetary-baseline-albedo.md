# A7 (d) / A24 (a) — a bázis bolygó-albedója: a szint rendben, a hideg vég nem

**Dátum:** 2026-09-29. **Ág:** `a19-plate-frame-noise`. **ND:** ND-160
(LEZÁRVA), ND-165 (ÚJ, nyitott). **Állapot:** KÉSZ, mérve; élő Unity-átvétel
hátra.

## A feladat és miért most

Az A7-ből három tétel maradt: (a) a jég vizuális átvétele, (c) a csapadék
szele (ND-163), (d) az ND-160 seed-törő bázis-albedó. Az ND-160 javaslata
explicit halasztási feltételt tartalmazott: „csak a 6. fázis fogyasztói
átállása ÉS annak vizuális átvétele UTÁN". Mindkettő megtörtént (ND-162,
ND-164; az ítélet: „kikapcsolva fest jobban"), és éppen ez az ítélet tette a
fizikai hitelességet a következő kör fő tételévé (todo2 A24). Az A24
javasolt sorrendjének első pontja is ez. Tehát (d) jött.

Egy körben EGY numerikus változás — hogy a hatás tisztán mérhető legyen.

## Amit a kód tesz

A bázis radiatív tagja egységesen `Temperature.AlbedoPlanet = 0,30`-cal
számol, felszíntípus-függetlenül. A FELSZÍNI albedó (`AlbedoOcean` 0,06 /
`AlbedoLand` 0,30) ott marad, ahová való: a solver tickenkénti
anomália-tagjában.

Két hely, és ez a kör egyetlen valódi kódolvasási lelete:

| hely | eddig | most |
|---|---|---|
| `ThermalBaseline` (cella-bázis + óceáni éves radiatív átlag) | `_parameters.BaselineAlbedoFor(...)`, felszíni visszaeséssel | ugyanaz, bolygó-albedós visszaeséssel |
| `ThermalWind.PointTemperature` (a bázis 4 offset-pontja) | **BEDRÓTOZOTT** `Temperature.AlbedoOcean/AlbedoLand` | `_parameters.BaselineAlbedoFor(...)` |

A második a lényeg: az ND-160 mérőkampó (`--baseline-albedo`) a SZELET nem
érte el, tehát a kampó egy olyan világot mért, amelyben a bázis és a
belőle differenciált szél KÜLÖNBÖZŐ albedóhoz tartozott. A szél a bázis
KÉPLETÉT differenciázza — a két helynek ugyanabból a forrásból kell jönnie.

Miért nem látszik ez az aggregátumokon: az albedó a gradienst csak
multiplikatívan skálázza (`((1−a)/(1−a'))^0,25 = 1,0765`), és mind a 4
offset-pont a cella saját típusát használja, tehát a szárazföldön a
változás NULLA, az óceánon egységes 7,1%-os skálázás. Ezért egyeznek a most
mért medián/egyezés-számok a kampó számaival — de a szélmező maga más.

## Mérés

`worldgen thermal-climate --seed A7C944210000 --plates 20 --level 5
--decompose true [--biome true] [--legacy-baseline-albedo true]`, Release,
12 mintanap, level 5 = 6144 cella (3993 óceáni), tengerszint 1623,9 m.

A „régi" oszlop NEM emlékezetből van: a `--legacy-baseline-albedo true`
kampóval, a MAI kódból mértem újra, és bitre reprodukálta a 2026-09-28-i
számokat (T_rad 264,86 K, medián 33,7 °C, egyezés 72,7%, küszöb 7,02 °C).

| mérték | régi (felszíni) | ÚJ (bolygó) |
|---|---:|---:|
| T_rad(f_eff) globális átlag | 264,86 K | **252,19 K** |
| bázis globális átlag | 302,89 K (+29,74 °C) | **290,22 K (+17,07 °C)** |
| éves átlag P50 | +33,7 °C | **+18,1 °C** |
| éves átlag P0 (MINIMUM) | −1,7 °C | **−1,7 °C — bitre ugyanaz** |
| éves átlag maximum | +45,9 °C | **+25,5 °C** |
| tartós jég | 430 (7,00%) | 430 (7,00%) — percentilis, azonos darabszám |
| szezonális hó | 1108 | **1943** |
| egyezés a régi, analitikus jégosztállyal | 72,7% | **86,4%** |
| tengeri jég (jégosztályos hidegvég) | 0 cella | **64 cella (1,6%)** |
| biome-egyezés a maival, szárazföldön | 78,3% | **80,1%** |
| futásidő (2 menet) | 29,1 s | 28,9 s |

## Amit a döntés megoldott — és amit nem

**Megoldott:** a SZINTET. A globális medián +33,7 → +18,1 °C, a Föld
~+15 °C-jához közel. A tengeri jég visszatért, a szezonális hó
megháromszorozódott, és a mai (analitikus) jégtakaróval való egyezés
72,7 → 86,4%-ra ugrott. Megszűnt a hiba VILÁGFÜGGŐSÉGE is: a bázis szintje
többé nem a szárazföld-arány függvénye.

**Nem oldotta meg:** a hideg végét. A minimum BITRE ugyanott maradt.
Az ok egy sorban: a minimum a szárazföldi póluson van, ahol a felszíni
szárazföld-albedó eddig is 0,30 volt — ott a bázis bitazonos. Ezt most
teszt kötözi ki (`PlanetaryBaselineCoolsTheOceanAndLeavesLandBitIdentical`):
a szárazföldi bázis bitre változatlan, az óceáni 13–21 K-nel hűl.

Következmény: az ND-159 (percentilis jégküszöb) és az ND-164 (jégosztályos
biome-hidegvég) **továbbra is kell**. A tartomány összenyomottságának oka
másban van — radiatív simítás (β = 0,5), hőkapacitás, a +8 K meridionális
konstans. Ez a todo2 A24 (b), a következő mérés.

## A csendes hiba, amit a bekötés közben fogtam meg

A `ThermalCheckpoint.ComputeModelIdentity` NEM tartalmazta a bázis
albedóját. Két futás, ami csak a bázis-albedó módjában különbözik, ugyanazt
a modell-azonosítót kapta volna — tehát a viewer lemez-gyorsítótára és a
checkpoint-visszaállítás NÉMÁN a másik mód eredményét adta volna vissza.
Pontosan az ND-162 csapdaosztálya. Javítva: az azonosítóba bekerült az
óceáni és a szárazföldi bázis-albedó (két érték mindhárom módot
szétválasztja: bolygó 0,30/0,30, legacy 0,06/0,30, explicit a/a), és a
`WorldInputsArePartOfIdentityAndForeignStateCannotContinue` teszt két új
esettel bizonyítja.

## Seed-törés és a referencia

- `WorldGeneratorVersion.Current` 5 → **6**
- `ThermalModelParameters.ModelVersion` 3 → **4**
- Python referencia ELŐBB: `thermal_field_ref.py` (`ALBEDO_PLANET_BASELINE`,
  MODEL_VERSION 4; az `ALBEDO_OCEAN_FULL/LAND_FULL` konstans a bázisból
  kikerült, ezért törölve), `thermal_checkpoint_ref.py` (generator „6",
  a két új albedó-mező).
- Újragenerált vektorok: `thermal_field_vectors.json`,
  `thermal_annual_vectors.json`, `thermal_checkpoint_vectors.json`,
  `thermal_feedback_vectors.json` — és átmásolva a
  `tests/WorldGen.Core.Tests/testdata/`-ba (a két hely KÜLÖN fájl; a
  regenerálás után a másolás nélkül a tesztek a régi vektorhoz mérnek —
  ebbe most bele is futottam, 11 bukó teszt formájában).

## Ellenőrzés

| kapu | eredmény |
|---|---|
| `dotnet test WorldGen.sln -c Debug` | **1962/1962** zöld (Core 824) |
| `dotnet test WorldGen.sln -c Release` | **1962/1962** zöld (Core 824) |
| `python tools/ci/check_burst_strict.py` | OK, 325 fájl |
| `dotnet build tests/WorldGen.Viewer.Compile` | 0 hiba |
| `dotnet build tests/WorldGen.App.UnityBinding.Compile` | 0 hiba |

A KAT-lánc érdemi része: a hőmodell minden referencia-tesztje az ÚJ
Python-vektorokhoz mérve zöld — tehát a C# és az orákulum bitre együtt
mozdult, nem csak a C# változott.

## Új nyitott döntés: ND-165

Ugyanaz az albedó-keverés ott van az ANALITIKUS hőmérséklet-úton is
(`Temperature.TemperatureKelvinFromAverageInsolation`,
`TemperatureKelvinFull`) — és azt a viewer ma is használja. Nem javítottam
csendben: külön ND, három opcióval. A javaslat (2): előbb a hideg vég
fizikája (A24 (b)), mert onnan jobban megítélhető, hogy egyáltalán kell-e.

## Ami hátra van

- **Élő Unity-átvétel.** A generátorverzió emelése miatt MINDEN hőmodell-
  gyorsítótár érvénytelen, tehát az első Play hideg futás lesz (level 5-on
  Editorban ~105 s a korábbi mérés szerint). A kérdés a felhasználóé:
  a hőmodellre állított biome/jég MOST jobban fest-e, mint az ND-164-kori
  ítélet idején.
- **A24 (b):** a hideg vég oka — β, hőkapacitás, meridionális transzport
  szétválasztása, méréssel.
- **A7 (c):** a csapadék szele (ND-163, 67,7%).
