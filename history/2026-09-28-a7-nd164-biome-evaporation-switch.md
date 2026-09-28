# A7 6. fázis — a biome és a párolgás átállása, és a mérés, ami a hideg véget átírta

**Dátum:** 2026-09-28. **Ág:** `a19-plate-frame-noise`. **ND:** ND-164.
**Állapot:** KÉSZ és élőben igazolva; a vizuális átvétel a felhasználóé.

## A feladat

A felhasználó az A7 három zárt kapuja közül a **(b)**-t nyitotta ki: a
biome hőmérséklet-tengelye és a csapadék párolgás-tagja álljon át az
ND-158 éves éghajlatára. A csapadék SZELE (ND-163) és az ND-160
seed-törő kalibráció szándékosan kimaradt.

Az előzetes becslés szerint ez „mérsékelt változás" volt (86,2% egyezés) —
de az a szám a CSAPADÉKRA vonatkozott, nem a biome-ra. A biome-ot előbb
meg kellett mérni.

## A mérés, ami megállította a naiv átkötést

Új CLI-mérés: `worldgen thermal-climate --biome true`. Az első futás
azonnal kiadta a bajt:

| forrás | hőmérséklet-tartomány |
|---|---|
| mai, analitikus | −92,7 … +51,3 °C |
| hőmodell, éves LEVEGŐ-átlag | **−1,7 … +45,9 °C** |

A `BiomeClassification` hideg vége ABSZOLÚT (`IceSheet` −10 °C, `SeaIce`
−2 °C). A hőmodell **sehol nem megy −1,7 °C alá**, tehát a puszta
tengely-csere NÉMÁN kiürítette volna a jégtakaró-biome-ot és a tengeri
jeget. Ugyanaz a hibaosztály, amit az ND-159 a jégMASZKRA már eldöntött:
abszolút küszöb helyett percentilis.

## A négy mért változat

| változat | szárazföldi egyezés | IceSheet | Tundra |
|---|---:|---:|---:|
| (0) mai | — | 7,6% | 9,3% |
| (1) hőmodell tengely | 78,1% | 0,0% | 11,8% |
| (2) + párolgás hőmérséklete | 78,4% | 0,0% | 11,9% |
| **(3) + hideg vég a jégosztályból** | **78,3%** | **20,0%** | **0,0%** |
| (4) (3) + tundra a szezonális hóból | 36,4% | 20,0% | 48,4% |

A (4)-et a mérés utasította el: a szezonális hó (a napi minimum
fagypontja) túl tág sáv, a szárazföld felét tundrává tenné.

**A (3)-at egyetlen szám döntötte el:** a (2) változat 254 szárazföldi
tundra-cellájából **254 (100%)** a jégmaszkon BELÜL van. A viewer ott
amúgy is `IceSheet`-et rajzol (ND-162). Tehát a (3) a KÉPBŐL nem vesz el
semmit — a panelt hozza összhangba azzal, ami látszik (I4). Ráadásul
megszünteti az ND-59 óta élő KÉT független jégréteget.

## A veszteség, kimondva

A tengeri jég **0,8% → 0,0%**. A hőmodellnek a mai kalibrációval nincs
fagypont alatti óceánja; az analitikus úton csak azért van tengeri jég,
mert az a mező −92 °C-ig megy.

## Az ND-160 mérőkampó — és a meglepetés

Hogy a döntés ne találgatásra épüljön, a bázis-albedó MÉRHETŐVÉ vált
(`ThermalModelParameters.BaselineAlbedo`, alapértelmezés `null` = a mai,
bedrótozott konstansok, bitre változatlan). `--baseline-albedo 0.30`:

| | mai bázis | bolygó-albedó |
|---|---:|---:|
| globális medián | +33,7 °C | **+18,1 °C** |
| egyezés a régi jégosztállyal | 72,7% | **86,4%** |
| óceáni tartós jég | 0 | **64 cella** |
| éves levegő-MINIMUM | −1,7 °C | **−1,7 °C** |

Két tanulság. (1) Az ND-160 visszahozza a tengeri jeget, sőt a mainál
többet — ez a legerősebb új érv az (1) opciója mellett. (2) A hideg vég
NEM tér vissza: a minimum ugyanott marad, tehát a biome abszolút
hidegvége az ND-160 UTÁN SEM lenne használható. Ezt eddig implicit módon
az ellenkezőjét feltételeztük.

## Megvalósítás

**Core** (csak új API-k, seed-semleges):
- `ClassifyBiomes(..., iceClass)` — a tartós jég felülír, minden melegebb
  cella BITRE a jégosztály nélküli eredmény (tesztben kikötve).
- `MoisturePrecipitation.Compute(..., temperatureK, ...)` — a paraméteres
  út is átveheti a párolgás mezőjét; `null` esetén bitre a régi.
- `ThermalModelParameters.BaselineAlbedo` mérőkampó.

**Viewer:**
- `useThermalClimateBiome` kapcsoló (BE), a jég kapujához kötve.
- `BiomeTemperatureKelvinAt` — SAROK-INTERPOLÁLT éves levegő-átlag. A
  tábla a TENGERSZINTRE REDUKÁLT hőmérsékletet hordozza, a kiértékelés a
  pont saját elevációjával húzza vissza (`AltitudeCorrectedK`,
  folytonosan). Nyers tile-lookup a 90–800 km-es cellahatárokat tenné
  láthatóvá; redukció nélküli interpoláció a hegyek hidegét kenné szét.
- A párolgás az éves FELSZÍNI átlagot kapja, MINDEN cellára (az óceániakra
  is — a nedvesség ott keletkezik).
- A csapadék-cache kulcsa kiegészült a párolgási mezővel. **Enélkül
  csendben elavult mezőt adna:** a kulcs leltára arra épült, hogy a
  `Compute` a deep time-ot „meg sem kapja" — a hőmodell éves átlaga
  viszont `tYears`-függő.
- `IsIceMaskTile(...)` egyetlen helyen. Ez javított egy meglévő eltérést
  is: a statikus alapréteg még a bedrótozott abszolút küszöböt használta,
  pedig az ND-162 óta a percentilis vágópont az érvényes.

## Ellenőrzések

- Core **822/822**, LodChunking **662/662**, solution 0 hiba / 0 warning.
- ND-20 Burst-kapu OK (325 fájl).
- Offline Unity-kapuk (Viewer.Compile, App.UnityBinding.Compile): 0 hiba.
- Új tesztek: a jégosztályos hidegvég (felülírás + a meleg vég
  bitazonossága + tisztaság + élesetek), a bázis-albedó kampó
  (alapértelmezés a `Temperature` konstansaira esik vissza, NEM a
  paraméterezett felszíni albedóra; érdemben hat; tartomány-ellenőrzés),
  a csapadék paraméteres túlterhelése (`null` = bitre a régi; hat;
  hiányzó tile explicit hiba).
- A `PrecipitationCacheKeyTests` reflexiós kapuja frissítve: két
  túlterhelést vár (12 és 13 paraméter), és rögzíti, hogy a 13. elem a
  viewer cache-kulcsában BENNE van.

## Élő igazolás (Unity 6, Play, level 5)

| mérték | érték |
|---|---|
| éghajlat háttérszálon | 117,9 s (cache-tévesztés), utána Build 2367 ms |
| `_adaptiveClimateAirCornerLevel` | **5** |
| jégküszöb | **280,168 K = 7,02 °C** — bitre a CLI percentilis vágópontja |
| párolgási mező | **6144 tile, [−1,7; +45,9] °C** — bitre a CLI tartománya |
| biome (szárazföld) | IceSheet 438, Desert 427, Rainforest 414, Grassland 398, TemperateForest 251, Savanna 223, Tundra 0 |
| tengeri jég | 0 (korábban 35) |
| Console-hiba a viewerből | nincs |

A 438 jég-tile a mért 430 percentilis-cella + a határ-jitter.

**Mérési csapda, rögzítve:** nem fókuszált, MCP-vel vezérelt Editorban az
`Update()` nem lép tovább, ezért a kész éghajlat magától sosem került
képre (`status` percekig „számítás fut", miközben a task már
`RanToCompletion` volt). Az `UpdateThermalClimate()` + `Build()` explicit
meghívása kellett — ugyanaz a korlát, amit az ND-162 is rögzített.

## Amit az élő futás mellékesen elkapott (MEGLÉVŐ hiba, külön tétel)

A `biomeOf` a Build-ben a csapadék-vágópontok KISZÁMÍTÁSA ELŐTT készül
(geometria-ciklus: 3157. sor, vágópontok: 3396.), tehát minden Build az
ELŐZŐ Build vágópontjaival osztályoz. Az ELSŐ Build után ez `(0, 0, 0)`
vágópont és 0 csapadék, ami a szárazföld **100%-át `Desert`-nek** mutatta
a panelen (mérve: Desert=1672, miközben a tényleges csapadék-medián 1,64 a
0,41-es arid vágópont mellett). A második Build után magától helyreáll.
Nem ez a változás okozta, és nem is ennek a hatóköre — de rögzítve, mert
pont a panel-számokat rontja, amiket most tettünk igazzá.

## Becslés

A7 funkcionális súlyozással kb. **95%** — a maradék a csapadék szele
(ND-163), az ND-160 döntés és a vizuális átvétel. Erre a körre durván
**3 munkaóra** ment rá; durva becslés, nincs valós idő-naplózás.

---

## Utóirat (ugyanaznap): „nem működik, hiába kapcsolgatom"

A felhasználó jelezte, hogy a kapcsoló nem csinál semmit. **Három külön ok
volt, és a második a súlyos.**

### 1. A kapcsolók nem szerepeltek a konfiguráció-pillanatképben

A `WorldConfigChangedSinceBuild()` nem figyelte sem az új
`useThermalClimateBiome`-ot, sem az ND-162 `useThermalClimateIce`-át —
tehát az Inspector-beli átkapcsolás **egyáltalán nem indított Buildet**,
amíg valami más nem változott. Az ND-162 kapcsolója így SOHA nem működött.
Javítás: mindkettő bekerült a `SnapshotWorldConfig`/összehasonlítás párba.
Élőben igazolva: `configChanged` mindkét irányban `True`.

### 2. A domain reload VÉGLEG megölte az éghajlatot

Ez a súlyos. Unity a Play közbeni script-újrafordításkor (és a reload-dal
járó Play-be lépéskor) az **értéktípusú** mezőket átmenti, a
**referenciákat** nem. Következmény:

| mező | típus | domain reload után |
|---|---|---|
| `_climateInputsIdentity` | `ulong` | **túléli** |
| `_hasClimateInputsIdentity` | `bool` | **túléli** |
| `_climateRevision` | `int` | **túléli** |
| `_climatePendingInputs` | referencia | `null` |
| `_climateApplied` / `_climateCompleted` | referencia | `null` |
| `_climateTask` | referencia | `null` |

Ezután a `CaptureThermalClimateInputs` az „ugyanaz a világ" ágra futott
(az azonosító ugyanaz!), és **soha nem fegyverezte újra** a bemenetet; az
`UpdateThermalClimate` pedig `inputs == null` miatt sosem indult el. Az
éghajlat a session végéig HALOTT maradt — a jég, a biome és a párolgás
némán az analitikus előnézeten ragadt, hibaüzenet nélkül.

Élőben megfigyelve: `rev=5`, `hasId=True`, `pending/applied/completed`
mind `null`, változatlan azonosító mellett; három egymás utáni Build sem
mozdított rajta semmit.

Javítás: az „ugyanaz a világ" ág mostantól megkérdezi, van-e **bármi élő**
(pending / futó task / kész / átvett eredmény). Ha nincs, újrafegyverzi a
bemenetet **revízió-emelés NÉLKÜL** — a világ nem változott, tehát a
lemez-cache találni fog. Élőben igazolva: reload után
`status = "éghajlat: újrafegyverezve (domain reload után)"`, majd
**gyorsítótárból 497 ms**, és a hőmodell útja újra aktív.

Ez a hibaosztály általános tanulság: **Unity-ben egy értéktípusú
„cache-kulcs" és a hozzá tartozó referencia-állapot külön sorsú.** A
kulcsra épülő „nincs dolgom" rövidzár mindig kérdezze meg azt is, hogy
egyáltalán VAN-e még mit rövidre zárni.

### 3. Mérési csapda, ami engem vezetett félre

A `Build()` az elején kilép, ha LOD-vágás fut (`_cutTask != null`), és csak
a `_fullBuildRequestedAfterCut` jelzőt állítja. Emiatt az egyetlen
eval-ban futtatott „ON → OFF → ON" sorozatban a 2. és 3. Build **no-op**
volt, és úgy tűnt, mintha a kapcsoló nem váltana. Ez NEM felhasználói hiba
(a halasztott kérést az `Update()` teljesíti) — de a verifikációt
egyenként, egy Build / egy állítás ritmusban kell végezni.

### Az egyenkénti, tiszta igazolás

| kapcsoló | `configChanged` | sarok-tábla | párolgási mező | szárazföldi biome |
|---|---|---:|---|---|
| **OFF** | True | **−1** | `null` | IceSheet 175, Tundra 155, SeaIce 45 (analitikus) |
| **ON** | True | **5** | `SET` | IceSheet **430**, Tundra 0, SeaIce 0 (hőmodell) |

A 430 pontosan a CLI-ben mért percentilis jégcella-szám.

Kapuk a javítások után: solution 0 hiba / 0 warning, LodChunking
**662/662**, ND-20 kapu OK, mindkét offline Unity-kapu tiszta, Unity
újrafordítás 0 hibával.
