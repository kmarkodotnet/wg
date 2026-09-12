# Fejlesztési ötlet #2 — Nem Föld-szerű bolygók

**Státusz:** koncepció, még nem elfogadott architekturális döntés

**Készült:** 2026-09-12

**Téma:** a WorldGen általánosítása vízalapú, Föld-szerű kőzetbolygóról
metán-/ammóniafolyadékos szilárd bolygókra, később külön gázóriás-modellre

## 1. Cél

A jelenlegi WorldGen egy determinisztikus, fizikailag összefüggő, de erősen
Föld-analóg világot generál. A cél olyan definíciós és modulhatárok kialakítása,
amelyek mellett ugyanaz a generátor többféle szilárd felszínű bolygót képes
kezelni, például:

- Föld-szerű vízbolygó;
- teljes vagy közel teljes óceánbolygó;
- hideg, metántengerekkel és vízjég-kéreggel rendelkező világ;
- ammónia-/vízkeverékes hideg világ;
- száraz vagy légkör nélküli kőzetbolygó.

A gázóriás támogatása hosszabb távú cél. Az nem a jelenlegi felszíni modell
egyszerű parametrizálása, hanem közös definíciós alapra épülő, külön
szimulációs modell.

## 2. Jelenlegi helyzet

A specifikáció már megnevez több szükséges fogalmat (`PlanetDefinition`,
`OrbitalElements`, atmoszféra-összetétel, belső hő és óceánkészlet), de a
megvalósításban még nincs egységes, menthető világdefiníció.

A `.worldpkg` jelenleg csak a következő lényegi bemeneteket tárolja:

- seed;
- lemezszám;
- referenciaszint;
- deep-time idő.

Néhány további érték a Unity Inspectorban már állítható — például a célzott
vízborítottság, a forgási és keringési periódus, valamint a tengelyferdeség —,
de ezek még nem alkotnak egységes Core-oldali definíciót, és nem mind részei a
perzisztált világazonosságnak.

A Core-ban összesen 151 publikus numerikus konstans található. Ezekből azonban
nem szabad 151 felhasználói paramétert készíteni: sok érték numerikus
felbontás, zajbeállítás, biztonsági korlát vagy univerzális fizikai állandó.

### 2.1 Fő Föld-/víz-specifikus feltételezések

- A fizikai bolygósugár globálisan fix: `PlanetConstants.RadiusMeters`.
- A kráterezés külön, fix 9,81 m/s² gravitációval számol.
- A hőmodell fix Föld-szerű csillagfluxust, víz-/szárazföld-albedót,
  lapse rate-et és 33 K üvegház-hatást használ.
- A párolgás vízhez kötött, 273,15 K körüli küszöbből indul.
- A tengeri jég küszöbe sós vízre van kalibrálva.
- A hidrológia alapfogalma az `isOcean`/`isOceanic` boolean; a tengerszint
  alatti terület automatikusan folyékony óceánnak számít.
- A felszíni kategóriák konkrétan `Ocean`, `SeaIce`, `IceSheet`, `Tundra`,
  `Temperate` és `Tropical` típusokra épülnek.
- Az óceáni és kontinentális kéreg magassága, zajamplitúdója és több
  tektonikai tulajdonsága fix Föld-analóg érték.

Ezek miatt egy metánvilág nem valósítható meg pusztán a víz színének és a
fagyáspontnak a megváltoztatásával.

### 2.2 A jelenlegi tile-típusok létrejöttének pontos feltételei

Ez a szakasz a 2026-09-12-i tényleges implementációt írja le, nem a későbbi,
általánosított bolygómodell célállapotát.

Fontos különbség van három fogalom között:

1. **Fizikai alaptulajdonság:** eleváció, tengerszint, hőmérséklet,
   csapadék, éves hőstatisztika és eseménytörténet.
2. **Core biome:** az `Ocean`, `SeaIce`, `IceSheet`, `Tundra`, `Temperate`
   vagy `Tropical` érték. Ezt jelenleg csak a hőmérséklet és az
   `isOceanic` boolean határozza meg.
3. **Látható renderkategória:** a Viewer a Core biome fölé kráter-, állandó
   jég- és tóréteget helyezhet, illetve a biome-határokat rendercélú,
   determinisztikus térbeli zajjal szabálytalanítja. A folyó ma már külön
   mesh-szalag, nem a tile alapkategóriája.

#### 2.2.1 A döntéshez használt hőmérséklet

A jelenlegi aktív Viewer-kódút az egyszerű `TemperatureKelvin` modellt
használja:

```text
albedo = isOceanic ? 0.06 : 0.30
absorbed = 1361 W/m² * dailyAverageInsolationFactor * (1 - albedo)
T_radiative = fourthRoot(absorbed / sigma)
T_altitude = 0.0065 K/m * max(0, elevation - seaLevel)
T = T_radiative + 33 K - T_altitude
```

A `dailyAverageInsolationFactor` egy teljes helyi nap 24 mintájának átlaga,
ezért függ:

- a tile gömbi helyétől és így a szélességi helyzettől;
- az év aktuális napjától;
- a keringési és forgási periódustól;
- a tengelyferdeségtől;
- a nappali megvilágítás geometriájától.

A magasság csak a tengerszint fölötti szárazföldet hűti. A csapadék,
talajnedvesség és növényzet jelenleg **nem vesz részt** a biome-besorolásban.
Emiatt a `Tropical` például nem jelent automatikusan esőerdőt, és a
`Temperate` sem garantál nedves, növényzettel borított felszínt.

#### 2.2.2 Tengerszint és óceáni állapot

Egy pont vagy tile akkor `isOceanic`, ha:

```text
elevation < seaLevel
```

A tengerszint kezdetben a `targetWaterFraction` célértékhez kalibrált
percentilis. Deep-time-ban a rendszer a kezdeti víztérfogat-proxyt tartja
meg, ezért a tényleges felszíni vízborítottság később eltérhet a célértéktől.

Az egyenlőség (`elevation == seaLevel`) nem óceán: a jelenlegi szigorú `<`
összehasonlítás szerint szárazföldnek számít.

#### 2.2.3 Core biome-ok

Az alábbi táblázat a zaj nélküli, Core-oldali besorolás. A Celsius-érték csak
a Kelvin-küszöb olvasható megfelelője.

| Core biome | Kötelező feltételek |
|---|---|
| `Ocean` | `elevation < seaLevel`, és `temperatureK >= 271.15 K` (kb. −2 °C) |
| `SeaIce` | `elevation < seaLevel`, és `temperatureK < 271.15 K` (kb. −2 °C) |
| `IceSheet` | `elevation >= seaLevel`, és `temperatureK < 263.15 K` (−10 °C) |
| `Tundra` | `elevation >= seaLevel`, és `263.15 K <= temperatureK < 278.15 K` (−10 °C-tól +5 °C-ig) |
| `Temperate` | `elevation >= seaLevel`, és `278.15 K <= temperatureK < 293.15 K` (+5 °C-tól +20 °C-ig) |
| `Tropical` | `elevation >= seaLevel`, és `temperatureK >= 293.15 K` (+20 °C) |

A küszöbérték maga mindig a melegebb kategóriába kerül. Például pontosan
278,15 K már `Temperate`, nem `Tundra`.

##### `Ocean`

Az `Ocean` létrejöttéhez két feltétel kell:

1. a felszín legyen a globális tengerszint alatt;
2. a pillanatnyi, napi átlagolt hőmérséklet ne legyen a sós víz jelenlegi
   −2 °C-os fagyási küszöbe alatt.

A folyékony víz rendelkezésre állását, a helyi nyomást és a forráspontot a
modell még nem ellenőrzi. A tengerszint alatti, elég meleg pontot automatikusan
vízóceánnak tekinti.

##### `SeaIce`

A `SeaIce` ugyanúgy tengerszint alatti tile, mint az `Ocean`, de a
hőmérséklete 271,15 K alatt van. Ez a kategória jelenleg nem jégvastagságot
vagy energiamérleget modellez, csak egy hőmérsékleti küszöböt.

A látható adaptív renderben a döntés:

```text
temperatureK + 4 K * spatialFbm(seed, position) < 271.15 K
```

Ez a determinisztikus zaj csak a látható partvonal szabályosságát töri meg;
a Core-ban eltárolt biome és hőmérséklet nem változik tőle.

##### `IceSheet`

A Core alap-biome szerint szárazföldi `IceSheet` keletkezik, ha az aktuális
hőmérséklet −10 °C alatt van.

Emellett a Viewer külön állandójég-döntést is végez. Ehhez:

1. a tile szárazföld legyen;
2. az év 12 időpontjából számolt éves átlaghőmérséklet készüljön el;
3. ehhez hozzáadódjon a deep-time globális eljegesedési ciklus eltérése;
4. az így kapott referenciaátlag és a helyi determinisztikus jitter összege
   legyen 258,15 K, azaz −15 °C alatt.

```text
annualMeanK + glaciationOffsetK
    + 4 K * spatialFbm(seed, position) < 258.15 K
```

Ha ez teljesül, az állandó jég renderkategória a normál biome-ot felülírja.
Ezért látható `IceSheet` két úton is létrejöhet: a pillanatnyi −10 °C-os Core
biome-ból vagy a szigorúbb, éves átlagos −15 °C-os állandójég-rétegből.

##### `Tundra`

Egy Core tile pontosan akkor tundra, ha:

1. nem óceáni, tehát `elevation >= seaLevel`;
2. az aktuális napi átlaghőmérséklete legalább −10 °C;
3. ugyanaz a hőmérséklet +5 °C alatt marad.

```text
!isOceanic
&& temperatureK >= 263.15
&& temperatureK < 278.15
```

A jelenlegi tundra-besorolás **nem követel meg** permafrosztot, fagyott
talajt, kevés csapadékot, rövid tenyészidőszakot vagy növényzetet. Fizikailag
ez ezért egyelőre „hideg szárazföldi hőmérsékleti sáv”, nem teljes tundra-
ökoszisztéma.

A látható renderben a hőmérséklethez ugyanaz a 4 K amplitúdójú térbeli jitter
adódik, mint a jéghatáron. Ez szabálytalanítja a tundra határát, de nem
módosítja a Core biome-statisztikát.

##### `Temperate`

Egy tile `Temperate`, ha szárazföld és az aktuális napi átlaghőmérséklete
+5 °C és +20 °C közé esik:

```text
!isOceanic
&& temperatureK >= 278.15
&& temperatureK < 293.15
```

Ez jelenleg csak mérsékelt hőmérsékletet jelent. Nem feltétel a megfelelő
csapadék, évszakosság, talaj vagy növényzet, ezért sivatagos és nedves tile is
ugyanebbe az alapkategóriába kerülhetne.

##### `Tropical`

Egy tile `Tropical`, ha szárazföld és az aktuális napi átlaghőmérséklete
legalább +20 °C:

```text
!isOceanic && temperatureK >= 293.15
```

Nincs felső hőmérsékleti határ. A kategória jelenleg nem jelent trópusi
esőerdőt: a csapadék és a növényzet nem része a döntésnek. Egy forró, teljesen
száraz sivatag is `Tropical` lenne.

#### 2.2.4 Hidrológiai és eseményalapú renderkategóriák

##### `Lake`

Először egy tile tójelölt, ha:

```text
!isOceanic && priorityFloodFilledElevation - elevation > 0.5 m
```

Ez azt jelenti, hogy a tile olyan zárt topográfiai mélyedésben van, amelyet a
priority-flood algoritmusnak legalább 0,5 méterrel fel kell töltenie a
kifolyási szint eléréséhez.

A Viewer alapbeállításai további komponensszintű szűrést végeznek. Egy
összefüggő tó csak akkor jelenik meg, ha:

- legalább 6 hidrológiai tile-ból áll; és
- a legmélyebb pontja legalább 40 méterrel van a számolt tófelszín alatt.

A tó ezért nem pusztán „tengerszint alatti víz”. Szárazföldi, lefolyástalan
vagy küszöbbel lezárt medence szükséges hozzá. A jelenlegi modell nem vizsgálja
külön, hogy a csapadék/párolgás mérlege ténylegesen képes-e feltölteni a
medencét.

##### `River`

A folyó ma már nem tile-alapkategória, hanem külön, a terep fölött futó
mesh-szalag. A folyó forráspontjához egyszerre kell teljesülnie:

- a tile szárazföld;
- az eleváció legalább 300 méterrel legyen a tengerszint fölött;
- a csapadék érje el a szárazföldi csapadékeloszlás 80. percentilisét;
- a jelölt bekerüljön a csapadék szerint rendezett első 12 forrás közé.

A kiválasztott forrásból a nyomvonal a lokális lejtést követi. A folyó
óceánnál, zárt mélyedésnél vagy egy másik folyóval való egyesülésnél ér véget.
A szalag szélessége a determinisztikusan számolt összefolyási/vízhozam-súllyal
nő.

A panelekhez létezik egy régebbi „river tile” statisztikai kiválasztás is:
szárazföldön a legnagyobb flow-accumulation értékű, alapból felső 3%-nyi
tile-t jelöli. Ez nem azonos a jelenleg kirajzolt, finomított folyó-mesh
feltételével.

##### `Crater`

Kráter csak akkor létezhet, ha a seed és az eltelt deep-time alapján valamely
10 000 éves eseménybucketben determinisztikusan megtörtént egy becsapódás.
Az esemény meghatározza:

- a becsapódás helyét;
- a becsapódó test 1–100 km közötti átmérőjét;
- a 15–25 km/s közötti sebességet;
- a becsapódási szöget;
- az ezekből származtatott kráterátmérőt és -mélységet.

Egy tile/pont akkor `Crater` renderkategória, ha a középpontja legalább egy
generált kráter szögsugarán belül van. A kráter egyidejűleg negatív elevációs
eltolást is ad: a peremen nulla, a középpont felé a krátermélységig nő.

#### 2.2.5 Szezonális hó

A Core külön `SeasonalSnow` állapotot is ismer:

```text
annualMeanK >= 258.15 K
&& annualMinimumK < 273.15 K
```

Az első feltétel azért implicit, mert 258,15 K alatti éves átlag esetén a
besorolás már `PermanentIce`. A szezonális hó jelenleg tesztelt Core-eredmény,
de nem önálló `RenderCategory`, ezért nem jelenik meg külön tile-típusként a
bolygón.

#### 2.2.6 Látható prioritási sorrend

Normál adaptív megjelenítésben a terep egyetlen alapkategóriáját a következő
sorrend választja ki:

```text
Crater
  > annual-mean Permanent Ice
  > Lake
  > SeaIce vagy Ocean
  > IceSheet / Tundra / Temperate / Tropical biome
```

Ez azt jelenti például, hogy egy tómedencében fekvő, egyébként tundra
hőmérsékletű tile `Lake` színnel jelenik meg, egy kráter pedig az állandó jeget
és a tavat is felülírja az alapterep kategóriájában.

A rétegek ettől még fizikailag egymás fölött létezhetnek. Az óceán
vízfelszíne külön geometria, ezért egy óceánfenéki `Crater` alapkategóriát a
vízfelszín eltakarhat. A folyók szintén külön meshként rajzolódnak, tehát nem
veszik el az alattuk lévő tundra/mérsékelt/trópusi biome-besorolást.

#### 2.2.7 Rövid döntési fa

```text
elevation < seaLevel?
├── igen
│   ├── T < 271.15 K? → SeaIce
│   └── különben      → Ocean
└── nem
    ├── T < 263.15 K? → IceSheet
    ├── T < 278.15 K? → Tundra
    ├── T < 293.15 K? → Temperate
    └── különben      → Tropical

Viewer-felülírások az alapkategória előtt:
Crater → Permanent Ice → Lake → fenti döntési fa

Külön rárajzolt réteg:
River
```

#### 2.2.8 Mit kell ezen megváltoztatni nem Föld-szerű világokhoz?

A későbbi általános modellben minden kategóriának a következő négy állapotból
kellene következnie:

```text
CrustKind
+ SurfaceMaterial
+ PhaseState(temperature, pressure)
+ Climate/Hydrology state
```

Így például a „tengerszint alatt” önmagában nem jelentene vizet, a „−2 °C
alatt” pedig nem jelentene automatikusan jeget. Metánvilágon ugyanaz a hely
lehetne vízjégből álló szilárd kéreg, fölötte folyékony metánnal; más nyomáson
és hőmérsékleten pedig száraz felszín metángőzzel vagy fagyott
metánlerakódással.

## 3. Javasolt architektúra

Az egyedi konstansok közvetlen Inspector-mezővé alakítása helyett egyetlen,
verziózott `WorldDefinition` legyen a világ minden autoritatív bemenetének
forrása.

```text
WorldDefinition
├── ModelVersion
├── Seed
├── BodyDefinition
├── StarSystemDefinition
├── OrbitalDefinition
├── AtmosphereDefinition
├── GeologyDefinition
├── VolatileDefinition
└── ClimateDefinition
```

Javasolt legfelső típuskülönbség:

```text
BodyKind
├── SolidSurface
└── GasGiant
```

Szilárd felszínű világoknál az elsődleges kondenzálódó anyag külön definíció:

```text
VolatileKind
├── None
├── Water
├── Methane
├── AmmoniaWater
└── Custom
```

A `Water`, `Methane` és más presetek csak alapértékeket töltenek be. A
szimuláció ugyanazt az általános `VolatileDefinition` adatot fogyasztja; nem
készülhet minden folyadékhoz külön, egymástól elsodródó hidrológiai kódút.

## 4. Javasolt paraméterkészlet

Egy fizikailag használható, metántengert is támogató szilárd bolygóhoz durván
**26 új, perzisztált modellparaméter** szükséges. Ebből körülbelül 18 már
valamilyen konstansként jelen van, 8 pedig jelenleg hiányzó modellfogalom.

Ez tervezési becslés, nem mechanikus konstansszámlálás. A pontos mezőlista az
implementáció előtti ND-döntés és Python referencia során változhat.

| Csoport | Becsült új paraméter | Példák |
|---|---:|---|
| Bolygótest | 4 | sugár, tömeg, belső hő, mágneses tér |
| Csillag és pálya | 4 | csillagluminozitás, effektív hőmérséklet, fél nagytengely, excentricitás |
| Atmoszféra | 6 | felszíni nyomás, összetétel, IR-optikai vastagság, fajhő, aeroszol, veszteségi faktor |
| Illóanyag/folyadék | 7 | anyagfajta, készlet, sűrűség, fázisgörbe, gőznyomásgörbe, fajhő, látens hő |
| Geológia | 3 | kéreganyag/sűrűség, tektonikai aktivitás, eróziós ellenállás |
| Klíma | 2 | horizontális hőtranszport, kondenzációs/csapadékképzési hatékonyság |
| **Összesen** | **26** | |

Ezen felül körülbelül **6 megjelenítési profilmező** szükséges, például a
folyadék abszorpciós/szórási tulajdonságai, a folyadék- és jégszín, az
atmoszférikus szórás és a felhő optikai karaktere. Ezek nem lehetnek a
szimuláció fizikai igazságának helyettesítői.

### 4.1 Származtatandó értékek

Az alábbi értékek ne legyenek egymástól független, szabadon állítható
paraméterek, mert könnyen fizikailag ellentmondó világot hoznának létre:

- gravitáció a tömegből és sugárból;
- átlagos sűrűség a tömegből és térfogatból;
- keringési periódus a csillagtömegből és fél nagytengelyből;
- pillanatnyi csillagfluxus a pálya és luminozitás alapján;
- atmoszféra átlagos molekulatömege az összetételből;
- légsűrűség és skálamagasság a nyomásból, hőmérsékletből, összetételből és
  gravitációból;
- fagyás, forrás és kondenzáció a helyi nyomásból és fázisgörbéből;
- párolgási sebesség a hőmérsékletből, szélből, gőznyomásból és elérhető
  folyadékkészletből.

## 5. A felszín általánosítása

Az `isOceanic` boolean egyszerre jelöl kéregtípust és vízzel fedett felszínt.
Ezt két külön fogalomra kell bontani:

```text
CrustKind
SurfaceMaterialState
```

Például egy metánvilágon teljesen érvényes lehet:

```text
CrustKind       = WaterIce
SurfacePhase    = Solid
SurfaceVolatile = Methane
LiquidCoverage  = true
```

A jelenlegi `Biome` enum helyett vagy fölött általános fizikai kategória kell:

```text
SurfaceRegime
├── ExposedSolid
├── LiquidCovered
├── VolatileIce
├── SeasonalDeposit
├── DrySediment
└── BiologicallyModified
```

A Föld-szerű tundra/mérsékelt/trópusi biome-ok csak akkor értelmezhetők, ha a
világon a biológiai modell engedélyezett és rendelkezik megfelelő
környezeti feltételekkel.

## 6. Metánvilág működési követelményei

Egy metánpreset nem tekinthető késznek attól, hogy a víz kék helyett más
színű. Legalább az alábbi modellkapcsolatoknak működniük kell:

1. A folyékony metán csak a helyi nyomás és hőmérséklet által engedett
   fázistartományban jelenhet meg.
2. A felszíni folyadék mennyiségét készletmegmaradás korlátozza; a célzott
   lefedettség nem hozhat létre korlátlan anyagot.
3. A párolgás, légköri szállítás, kondenzáció, csapadék, lefolyás és tavak
   ugyanazt a kiválasztott illóanyagot használják.
4. A folyadék és a szilárd fázis saját sűrűséggel, fajhővel, látens hővel,
   albedóval és eróziós hatással rendelkezik.
5. A panel minden felszíni anyagot és fázist a modellből mutat, megfelelő
   egységgel és számítási lánccal.
6. A renderelt folyadék, jég, köd és felhő minden tulajdonsága visszavezethető
   a generált világállapotra.

## 7. Gázóriások külön modellje

A gázóriás nem kezelhető `targetWaterFraction = 1` vagy `plateCount = 0`
beállítással. A következő jelenlegi modulok ott nem értelmezhetők:

- szilárd felszíni eleváció és tengerszint;
- óceáni/kontinentális kéreg;
- lemeztektonika;
- folyók és tavak;
- felszíni erózió;
- szárazföldi biome-ok.

A gázóriás saját állapottere várhatóan legalább a következőket igényli:

- nyomáskoordinátás függőleges rétegek;
- hőmérséklet- és sűrűségprofil;
- belső hőfluxus;
- összetétel mélység szerint;
- zonális szelek és sávok;
- konvekció;
- több kondenzálódó felhőanyag és felhőszint;
- örvények, viharok és hosszú élettartamú szerkezetek;
- látható „felszín” helyett optikai mélységből származó renderfelület.

Ehhez durván **20–25 további bemeneti szabadságfok** és egy külön
`GasGiantModel` szükséges. A rács, idő, random, csillag/pálya, perzisztencia és
megjelenítési interfész közös maradhat, a felszíni szimuláció azonban nem.

## 8. Javasolt fejlesztési szakaszok

### I. szakasz — Verziózott világdefiníció

- Új ND-döntés a definíciók tulajdonjogáról, mértékegységeiről és
  kompatibilitásáról.
- `WorldDefinition` és a fenti részdefiníciók bevezetése.
- A már létező Inspector-paraméterek átvezetése a definícióba.
- `.worldpkg` formátum- és modellverzió emelése.
- Validáció: tartományok, összegükben helyes gázfrakciók és fizikailag
  konzisztens származtatott értékek.
- Föld-analóg preset, amely a jelenlegi világot a lehető legszorosabban
  reprodukálja.

**Kapufeltétel:** a régi Föld-analóg preset kontrollált migráció után
determinista; az eltérő definíciók eltérő world hash-t adnak; ismeretlen
modellverzió explicit betöltési hibát okoz.

### II. szakasz — Általános illóanyag- és felszínmodell

- `VolatileDefinition`, fázisállapot és készletmegmaradás Python
  referencia-orákulummal.
- `isOceanic` szétválasztása kéregtípusra és felszíni anyagállapotra.
- Hidrológia, párolgás, csapadék, jég és tómodul átvezetése az általános
  illóanyag-definícióra.
- `SurfaceRegime` és opcionális Föld-biome réteg.
- Víz-, száraz-, metán- és ammónia/víz preset.
- Modellvezérelt renderprofilok és paneladatok.

**Kapufeltétel:** ugyanaz a hidrológiai lánc víz- és metánpreset mellett is
működik; az anyagmérleg zár; a fázisállapot megfelel a referencia-görbének;
nincs vízhez kötött rejtett konstans az aktív kódútban.

### III. szakasz — Külön gázóriás-prototípus

- Új ND-döntés a gázóriás állapotteréről és determinizmusáról.
- Nyomásszintes, egyszerű 1D oszlopreferencia.
- Később globális sáv-/szél-/felhőmező ugyanazon cubed-sphere rácson.
- Külön render- és panelréteg, közös csillag/pálya/idő interfésszel.

**Kapufeltétel:** a gázóriás nem futtat felszíni modult; energia- és
tömegprofilja stabil; azonos seed, definíció, verzió és idő azonos mezőket ad.

## 9. Presetek és felhasználói felület

A GUI ne mutassa egyszerre mind a 26–40 szakmai mezőt. Három szint javasolt:

1. **Preset:** Earth-like, Ocean World, Methane World, Dry World, Gas Giant.
2. **Egyszerű beállítások:** méret, hőmérsékleti karakter, légkör vastagsága,
   folyadékkészlet, geológiai aktivitás.
3. **Haladó fizika:** részletes összetétel, fázisgörbe, hőkapacitások,
   optikai és geológiai paraméterek.

A preset csak verziózott paramétereloszlás és alapértékkészlet legyen, ne külön
generátor. Mentéskor mindig a feloldott, konkrét paraméterek kerüljenek a
világdefinícióba, hogy egy preset későbbi módosítása ne változtassa meg a már
elmentett világot.

## 10. Determinizmus és verziózás

Ez a fejlesztés a numerikus szimuláció és a világazonosság nagy részét érinti,
ezért seed-/modellkompatibilitást törő módosításnak számít.

Kötelező:

- új, egyedi ND-döntések az implementáció előtt;
- Python referencia minden új fázis- és transzportképlethez;
- forrásolt anyagállandók, nem emlékezetből beírt értékek;
- determinisztikus tesztvektorok;
- paraméterhatás-tesztek minden autoritatív bemenetre;
- szekvenciális/párhuzamos és platformközi bitazonosság;
- world/model/package verzió emelése;
- régi csomagok explicit migrációja vagy világos elutasítása;
- a teljes definíció bevonása a world state hash-be.

## 11. Nem cél az első változatban

- tetszőleges vegyipari szimulátor;
- korlátlan számú illóanyag teljes kölcsönhatása;
- 3D atmoszférikus CFD;
- automatikus élet-/evolúciómodell nem Föld-szerű kémiára;
- gázóriások és szilárd bolygók egyetlen közös fizikai solverbe erőltetése;
- minden belső algoritmikus konstans megjelenítése a GUI-ban.

## 12. Kockázatok

| Kockázat | Következmény | Kezelés |
|---|---|---|
| Túl sok szabad paraméter | fizikailag lehetetlen világok | preset + validáció + származtatott értékek |
| Rejtett vízkonstans marad | látszólag metán-, valójában átszínezett vízvilág | aktív kódút audit + paraméterhatás-tesztek |
| Minden anyaghoz külön kód | gyorsan széteső implementáció | közös `VolatileDefinition` és interfész |
| Seed-kompatibilitás csendes törése | régi világok megváltoznak | modellverzió és explicit betöltési szabály |
| Pontatlan fizikai konstansok | hihetőnek látszó, hibás szimuláció | hiteles forrás + Python referencia + KAT |
| Gázóriás belekényszerítése a felszíni modellbe | sok speciális eset, értelmezhetetlen állapot | külön `GasGiantModel` |
| Túl nagy GUI | használhatatlan beállítási felület | preset/egyszerű/haladó rétegezés |

## 13. Kész-definíció a fejlesztési ötlet megvalósításához

- A világ minden autoritatív paramétere egy verziózott, mentett
  `WorldDefinition` része.
- A gravitáció, fluxus, légköri és fázisértékek dokumentált láncból
  származnak, nem egymásnak ellentmondó kézi mezők.
- Legalább egy víz-, egy metán- és egy folyadék nélküli szilárd bolygó ugyanazt
  az általános Core-adatutat használja.
- A metánpresetben a párolgás, csapadék, tavak, folyók és fagyott lerakódások
  metánkészletből származnak, nem vízkonstansokból.
- A Föld-biome-ok opcionális fogyasztók, nem az alapvető felszínállapot
  reprezentációja.
- Azonos seed + teljes definíció + modellverzió + idő minden támogatott
  platformon bitazonos világot ad.
- Minden panelérték és renderelt réteg a modellből következik.
- A gázóriás — amikor elkészül — közös infrastruktúrát, de külön fizikai
  modellt használ, és nem aktivál értelmetlen felszíni modulokat.

## 14. Durva ráfordítási becslés

Az alábbi számok tervezési nagyságrendek, nem mért munkaidők:

| Szakasz | Durva becslés |
|---|---:|
| Verziózott `WorldDefinition`, validáció, mentés, migráció | 40–70 óra |
| Általános anyag-/fázisreferencia és Core-adatmodell | 60–100 óra |
| Hidrológia, klíma, cryosphere és felszín átvezetése | 100–180 óra |
| Presetek, panelek, renderprofilok, Unity-validáció | 50–90 óra |
| **Metánképes szilárd bolygók összesen** | **250–440 óra** |
| Külön gázóriás MVP ezen felül | **180–320 óra** |

A legkisebb kockázatú első szállítható eredmény a verziózott
`WorldDefinition` és egy változatlan viselkedésű Föld-analóg preset. Ez még
nem ad metánbolygót, de megszünteti azt az architekturális akadályt, hogy a
világ fizikai identitása szétszórt konstansokból és Unity Inspector-mezőkből
álljon.
