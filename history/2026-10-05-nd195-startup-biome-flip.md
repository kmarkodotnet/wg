# Az indítás utáni biome-átrendeződés (zöld → sárga) mért anatómiája — ND-195

**Dátum:** 2026-10-05
**Ág:** `a19-plate-frame-noise`
**Kiváltó:** felhasználói visszajelzés — „az indításkor adott biom pár másodperc után változik zöld → sárga"
**Döntés:** `docs/04-decisions.md` ND-195 (NYITOTT, javaslat: (a))

## Rövid válasz

Nem új hiba. Az első Build szándékosan az analitikus előnézetet rajzolja
(ND-162); amikor a háttérszálas éves éghajlat megjön, egy TELJES második Build
fut hőmodell-tengellyel, és a biome átrendeződik. A „pár másodperc" oka az,
hogy a lemez-gyorsítótár MEGVAN és a számítás 0 ms, de a gyorsítótár-KULCSOT
minden indításkor újra elő kell állítani — és ez 6,2–7,4 s.

## Mérőpad

Élő Unity Editor, Edit mode, `eval`. Friss `GameObject` + `PlanetGridMesh`, a
jelenet `Planet` objektumáról `EditorUtility.CopySerialized`-elt beállításokkal
(level 5 → 6144 tile). Build#1 (analitikus) → `UpdateThermalClimate()` a
háttérszál indítására → várakozás a `_climateTask` befejezésére →
`UpdateThermalClimate()` az alkalmazáshoz → `TryApplyCompletedAsyncCut()` →
Build#2 (hőmodell). Mindkét Build után a `_lastBiomeOf`, `_adaptivePrecip` és
`_adaptiveBiomeThresholds` CSV-be mentve, és tile-szinten összevetve.

## 1. Mennyi vált

**1402 / 6144 tile** (a 2151 szárazföldi tile-ból 1280).

```
IceSheet        -> Tundra            224      Savanna   -> Grassland        79
Desert          -> Tundra            169      Rainforest-> Savanna          78
SeaIce          -> Ocean             122      Savanna   -> Tundra           71
Grassland       -> Desert            114      Desert    -> Grassland        68
Grassland       -> Tundra             99      TempForest-> Tundra           64
Savanna         -> TemperateForest    86      Grassland -> TemperateForest  39
```

Szín-csoportokra (a `BiomeColor` tényleges értékeiből: zöld =
TemperateForest/Rainforest, olívzöld = Savanna, sárga = Desert/Grassland,
szürkebarna = Tundra):

```
sarga     -> szurkebarna  268     zold      -> olivzold      78
jeg       -> szurkebarna  224     zold      -> szurkebarna   75
olivzold  -> zold         122     zold      -> sarga         33
jeg       -> viz          122     sarga     -> olivzold      19
olivzold  -> sarga        115     szurkebarna -> sarga         7
```

A jelzett „zöld → sárga": `zold → olivzold/szurkebarna/sarga` = **186 tile**,
a mellette futó `olivzold → sarga` további 115 → **301 tile** sötétzöldből
sárgás/szürkés irányba.

## 2. Miért ilyen nagy: mindhárom bemenet egyszerre vált

A `_adaptiveEvaporationTemperatureK` `null` → hőmodell-felszín váltása új
csapadék-mezőt ad (a csapadék-cache kulcsának kilencedik eleme), tehát nem csak
a hőmérséklet-tengely változik:

| | analitikus | hőmodell |
|---|---|---|
| megváltozott csapadék-tile | — | **5275 / 6144** |
| csapadék-medián | 0,7539 | **0,2282** |
| csapadék-maximum | 105,678 | **199,132** |
| arid / semiArid / moist | 0,1536 / 0,4933 / 1,4554 | **0,0315 / 0,1724 / 1,1527** |

A vágópontok percentilisek, tehát követik az eloszlást — a váltás mégis nagy,
mert a mező ALAKJA változik (medián harmadára, maximum majdnem duplájára).

## 3. Miért „pár másodperc"

`TryLoadThermalClimateDuringBuild` (ND-192) azonnal kiváltaná az előnézetet, de
`TryGetCachedClimateKey` elbukik: a `_climateKeyMemory` **példány-mező, nem
perzisztens**, tehát minden indításnál üres. Három független menet PerfLogja:

```
[ND-192 climate job] fromCache=True keyFromMemory=False totalMs=6662.3 grid=8.5  field=6234.4 fingerprint=411.6 disk=5.7   compute=0.0 unpack=2.2
[ND-192 climate job] fromCache=True keyFromMemory=False totalMs=7119.0 grid=11.2 field=6327.4 fingerprint=618.2 disk=115.4 compute=0.0 unpack=44.6
[ND-192 climate job] fromCache=True keyFromMemory=False totalMs=7871.8 grid=9.4  field=7359.0 fingerprint=487.2 disk=10.3  compute=0.0 unpack=4.4
```

`compute = 0,0 ms` — az éghajlat teljes egészében a lemezről jön. A 6,2–7,4 s a
`field` fázis: a `SurfaceTemperatureField` felépítése, ami KIZÁRÓLAG a
kulcshoz (`ComputeSolverFingerprint`) kell. A lemez-olvasás 5,7–115 ms, a
kipakolás 2,2–44,6 ms. A várakozás **~90%-a tartalmi eredmény nélküli munka**.

## 4. Kapcsolat az ND-194-gyel (A23)

A váltás az ND-194 előtt is megvolt, csak az IRÁNYA volt más: addig az első
Build 100% `Desert` (sárga) volt, tehát sárga → zöldes irányba váltott. Az
ND-194 óta az első kép helyes, ezért a váltás zöld → sárga/szürke irányban
látszik. Az ND-194 nem okozta, de **láthatóvá tette** — korábban a hibás,
egyszínű sivatag „olvadt fel" a helyes képbe.

## 5. Mit javaslok

ND-195 (a): a gyorsítótár-kulcs (`identity → modelIdentity + fingerprint`)
perzisztálása a meglévő cache-könyvtárba, hogy az ND-192 szinkron útja már az
ELSŐ Buildben bekapcsoljon. A Buildbe beengedett mért költség 5,7–115 ms
olvasás + 2,2–44,6 ms kipakolás a 6,2–7,4 s helyett. A
`ThermalClimateDiskCache.TryRead` a kulccsal validál, tehát elavult perzisztált
kulcs elutasításra és újraszámolásra vezet, nem rossz adatra.

## Nem dől el ezzel

Hogy a két tengely eltérése (csapadék-medián 0,754 → 0,228) önmagában
indokolt-e. Az a párolgás-kalibráció kérdése — A24 / ND-165, külön mérés.

## A javítás (az (a) opció, a felhasználó elfogadta)

### 1. A kulcstábla perzisztál

`_climateKeyMemory` → `climate_keys.v1.txt` a gyorsítótár-könyvtárban. Formátum:

```
WGTCLIMKEY1 <modell-proba-azonosito>
<identity:x16> <modelIdentity> <fingerprint:x16>
...
```

A fejléc **modell-próba azonosítója** nem kézzel másolt paraméter-lista, hanem
egy FIX, szintetikus (level 1 — a `DenseGridMetrics` legkisebb engedett szintje;
a level 0 `ArgumentOutOfRangeException`-t dob, ezt a mérés fogta meg) bemenetre
vett `ModelIdentity`. **Ugyanaz a kód** (`ThermalCheckpoint.ComputeModelIdentity`)
számolja, amelyik a valódi kulcsot is — tehát a modell-paraméterek listája nincs
kétszer leírva, és egy új paraméter nem tud csendben kimaradni a kapuból.

Ez pótolja azt a biztonsági érvet, amit a perzisztálás elvett. A fedés:
az `identity` a világ-specifikus bemeneteket (szint, seed, tengerszint,
deep-time, pálya, per-cella típus + eleváció, modell- és generátorverzió), a
fejléc-próba a modell-paramétereket és a bolygó-sugarat — pont azokat, amiket
egy A/B-mérés modellverzió-emelés nélkül állít át.

### 2. A mérés közben feltárt második rés: a hiányzó `.bin`

A kulcs mostantól túléli a sessiont, a `.bin` fájlokra viszont vonatkozik a
kvóta-takarítás (128 MiB) — tehát előállhat, hogy a kulcsot tudjuk, de a fájl
már nincs ott. Ha ilyenkor a szinkron útra léptünk volna, a
`RunThermalClimateJob` a **fő szálon** építene mezőt (6,2–7,4 s) és számolna
teljes éghajlatot (hidegen ~118 s) — a kép megfagy. A szinkron út kapuja ezért
a fájl létezését is ellenőrzi (`ClimateCacheFileExists`), a kulccsal azonos
fájlnév-számítással. Ez a rés elvben a memóriabeli változatban is megvolt; a
perzisztálás tette valóssá.

### 3. Sorrend-javítás

`TryLoadThermalClimateDuringBuild`-ben az `EnsureThermalClimateCacheDirectory()`
a kulcs-keresés ELÉ került: a tábla ebből a könyvtárból tölt be, és a
`persistentDataPath` Unity API, amit csak a főszálról szabad olvasni. Enélkül az
első Build sosem látná a lemezen levő táblát — épp az indítás maradna a régi úton.

## Mérés a javítás után

Ugyanaz a mérőpad (friss példány = új indítás, level 5, 6144 tile):

| | előtte | utána |
|---|---|---|
| az első Build éghajlata | analitikus előnézet | **Build-ben betöltve** |
| betöltési idő | 6662 / 7119 / 7872 ms | **14 / 16 ms** |
| `rebuildRequested` az első Build után | `True` | **`False`** |
| háttérszálas job | elindul | **nem indul** |
| biome-átrendeződés | **1402 / 6144 tile** | **0 tile** |
| vágópontok az első Buildben | 0,1536 / 0,4933 / 1,4554 | **0,0315 / 0,1724 / 1,1527** |

**A végállapot azonos:** az új első Build biome-térképe **0 / 6144 eltéréssel**
egyezik a korábbi kétlépéses út végállapotával (`Ocean=3993 Tundra=886
Grassland=316 Rainforest=316 Desert=254 TemperateForest=204 Savanna=175`). Nem
más képet mutatunk hamarabb — ugyanazt a képet mutatjuk elsőre.

### A kapuk külön-külön

```
[ND-195 climate keys] loaded=1                         -> talalat, kulcs a lemezrol
[ND-192 build climate load] applied=True ... ms=17.1    -> szinkron betoltes a Buildben
[ND-195 climate keys] discarded=stale-model-probe       -> elavult fejlec: 0 bejegyzes
[ND-195 build climate load] skipped=cache-file-missing  -> hianyzo .bin: elonezet
```

- **Elavult fejléc:** a próba-azonosítót nullára írva egy friss példány
  **0 bejegyzést** töltött be → a régi út fut, nem rossz adat. (A fájl utána
  visszaállítva.)
- **Hiányzó `.bin`:** a fájlt elnevezve a Build **4171 ms** (a megszokott
  előnézet-út), `climateApplied = null` — **nem fagyott a főszálon**. (A fájl
  utána visszaállítva; 5 `.bin` megvan, 0 elrejtve maradt.)

## Kapuk

- `dotnet build tests/WorldGen.Viewer.Compile` → **0 error** (133 warning, mind előzetesen is fennálló).
- Unity `recompile` → `compilationFailed: false`.
- `dotnet test tests/WorldGen.Viewer.LodChunking.Tests` → **664/664 zöld**.
- Unity Console aktuális hibaszám **0**. A próba-objektumok törölve, a jelenet
  nem módosult (`isDirty=False`).
- Nincs generátorverzió-emelés: a Core numerikája és a világadat változatlan.

## Egy hiba, amit a mérés fogott meg

Az első próba-azonosító `DenseGridMetrics.Build(0)`-t használt, ami
`ArgumentOutOfRangeException`-t dob („A sűrű rácsmetrika 1 és 12 közötti szintre
épül"). A hibakezelés jól viselkedett — csendes figyelmeztetés, a gyorsítótár
nem perzisztált, a Build ment tovább —, de a funkció NEM működött. Enélkül a
mérés zöldnek látszott volna a fájl meglétének ellenőrzése nélkül: **a
gyorsítótárnál a „nem dőlt el semmi" nem bizonyíték, a fájlt meg kell nézni.**
