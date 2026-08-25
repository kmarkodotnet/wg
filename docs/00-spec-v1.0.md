# DYNAMIC PLANET WORLD GENERATOR
## Teljes műszaki és tervezési specifikáció – v1.0

**Projekt célja:** önálló, determinisztikus, seed-alapú bolygó- és világ-generátor létrehozása, amely később az evolúciós játék/szimuláció alapvilágát szolgáltatja.

**Elsődleges prioritás:** először a World Generator működjön önálló programként.  
**Másodlagos prioritás:** a generált világ alkalmas legyen arra, hogy később változtatás nélkül vagy minimális bővítéssel ráépüljön az evolúciós motor.

---

# 0. Vezetői összefoglaló

A World Generator nem egyszerűen egy random heightmap-generátor.

A program feladata egy teljes, időben változó bolygórendszer létrehozása:

```text
CSILLAGRENDSZER
        ↓
BOLYGÓPÁLYA + FIZIKAI PARAMÉTEREK
        ↓
GEOLOGIA / TEKTONIKA
        ↓
DOMBORZAT + ÓCEÁNOK
        ↓
LÉGKÖR
        ↓
KLÍMA + IDŐJÁRÁS
        ↓
HIDROLÓGIA + JÉG
        ↓
ABIOTIKUS ERŐFORRÁSOK
        ↓
DINAMIKUS VILÁGÁLLAPOT
        ↓
KÉSŐBB: ÉLET / EVOLÚCIÓ
```

A kulcskövetelmény:

> **Azonos seed + azonos időpont + azonos verzió esetén mindig pontosan ugyanazt a világállapotot kell kapni.**

Ugyanakkor két különböző seed kellően különböző bolygót és történelmet eredményezzen.

A világ nem statikus:

- másodpercek / órák alatt változik a megvilágítás és időjárás;
- évszakosan változik a hőmérséklet, csapadék, hó és jég;
- száz–tízezer éves skálán klímaciklusok működnek;
- millió éves skálán folyók, hegységek, partvonalak és jégsapkák változnak;
- több tíz–százmillió év alatt kontinensek vándorolnak, ütköznek, szétszakadnak;
- ritkán becsapódások, szupervulkáni események és más katasztrófák történnek;
- milliárd éves skálán a csillag és a bolygó állapota is változhat.

A rendszernek **nem szükséges minden köztes másodpercet kiszámolnia**.  
A világ állapotát analitikus/ciklikus komponensek, determinisztikusan ütemezett események, numerikus szimulációk és checkpointok együtt adják.

---

# 1. A program határai

## 1.1 A World Generator 1.0 feladata

A program generálja és szimulálja:

- csillagrendszert;
- bolygófizikát;
- bolygópályát;
- megvilágítást;
- gravitációt;
- légkört;
- geológiát;
- tektonikai lemezeket;
- kontinenseket;
- óceánokat;
- hegységeket;
- vulkanizmust;
- időjárást;
- klímát;
- szeleket;
- csapadékot;
- folyókat;
- tavakat;
- gleccsereket;
- tengeri jeget;
- talaj/regolit jellegű mezőket;
- abiogén erőforrásokat;
- kozmikus környezetet;
- meteor- és üstökösbecsapódásokat;
- hosszú távú determinisztikus világfejlődést.

## 1.2 Ami NEM szükséges az első World Generator verzióban

- élőlények;
- evolúció;
- növényzet;
- tápláléklánc;
- populációk;
- civilizáció;
- részletes kémiai reakcióháló;
- teljes fluid-dinamikai légköri szimuláció;
- teljes köpenykonvekciós geofizika;
- pontos N-body csillagászati szimulátor.

Ezekhez a World Generator adatot biztosít, de nem kell őket első körben megvalósítani.

---

# 2. Fő tervezési követelmények

## 2.1 Determinisztikusság

Kötelező:

```text
Generate(seed=S, time=T)
```

mindig ugyanazt adja.

Továbbá:

```text
Generate(S, 0)
AdvanceTo(1_000_000 years)
```

és

```text
Generate(S, 1_000_000 years)
```

azonos vagy numerikus tolerancián belül azonos világot kell adjon.

## 2.2 Időlépés-függetlenség

Nem elfogadható, hogy:

```text
1000 × 1 év
```

lényegesen más bolygót eredményezzen, mint:

```text
1 × 1000 év
```

A gyors és lassú szimuláció között legyen definiált hibahatár.

## 2.3 Párhuzamos futtatás melletti determinisztikusság

A világ ne változzon attól, hogy:

- 1 CPU thread;
- 8 CPU thread;
- GPU compute;
- eltérő tile feldolgozási sorrend

használatával számoljuk.

Ezért nem ajánlott egyetlen globális szekvenciális `Random` objektum.

## 2.4 Reprodukálhatóság

Minden world save tartalmazza:

```text
worldSeed
generatorVersion
simulationVersion
parameterSetVersion
currentTime
eventHistoryHash
```

## 2.5 Skálázhatóság

Támogatandó:

```text
bolygó nézet
kontinens nézet
régió nézet
lokális tile nézet
```

ugyanazon geometriai rendszer felett.

---

# 3. Determinisztikus random architektúra

Ez az egész rendszer egyik legfontosabb része.

## 3.1 Hierarchikus seed

Alap:

```text
WorldSeed
```

Ebből alrendszer-seedek:

```text
StarSeed
OrbitSeed
PlanetSeed
TerrainSeed
PlateSeed
ClimateSeed
HydrologySeed
ResourceSeed
ImpactSeed
VolcanismSeed
WeatherSeed
FutureEventSeed
```

Például:

```text
StarSeed = Hash(WorldSeed, "STAR")
TerrainSeed = Hash(WorldSeed, "TERRAIN")
```

## 3.2 Ne használjunk feldolgozási sorrendtől függő randomot

Rossz:

```text
rng.Next()
rng.Next()
rng.Next()
```

ha párhuzamos feldolgozás történik.

Jobb:

```text
RandomValue =
DeterministicRandom(
    WorldSeed,
    SubsystemId,
    SpatialId,
    TimeBucket,
    PropertyId,
    SampleIndex
)
```

Ez lényegében counter-based random.

## 3.3 Példa

Egy adott tile 12 500. évi csapadék-noise értéke:

```text
R = Random(
    seed,
    "CLIMATE_RAIN",
    tileId,
    year = 12500
)
```

Nem számít, milyen sorrendben kérjük le a tile-okat.

## 3.4 Ajánlott PRNG stratégia

Preferált:

- counter-based PRNG;
- Philox / Threefry jellegű algoritmus;
- vagy saját stabil hash-alapú stateless random layer.

Követelmény:

- platformfüggetlen;
- verziózott;
- dokumentált;
- tesztvektorokkal ellenőrzött.

---

# 4. Időmodell

A világnak több egymásra épülő időskálája van.

## 4.1 Időegység

Belső reprezentáció:

```text
SimulationTime
```

lehetőleg nagy pontosságú, de ne floating point év legyen az egyetlen forrás.

Ajánlott:

```text
int64 seconds
```

vagy külön:

```text
Epoch + fractionalTime
```

Nagyon mély időnél praktikus:

```text
long ticks
```

definiált fix tick hosszal.

## 4.2 Időtartományok

### A. Instant / local

```text
másodperc – óra
```

- nappal-éjszaka;
- lokális időjárás;
- szél;
- hőmérsékleti napi ciklus.

### B. Seasonal

```text
nap – év
```

- évszakok;
- hó;
- jég;
- monszun;
- ciklikus csapadék.

### C. Climate

```text
10 – 100 000 év
```

- hosszú klímaciklusok;
- eljegesedés;
- száraz/nedves periódusok;
- tengerszintváltozás.

### D. Geological

```text
10 000 – 100 000 000 év
```

- erózió;
- hegységképződés;
- lemezmozgás;
- kontinensvándorlás;
- óceánok nyílása/záródása.

### E. Deep Time

```text
100 millió – több milliárd év
```

- csillag fényességének változása;
- bolygó belső energiájának csökkenése;
- nagy tektonikai átrendeződések;
- ritka kozmikus katasztrófák.

---

# 5. Világállapot: statikus, ciklikus és történeti komponensek

A világállapot ne egyetlen nagy mutable objektum legyen.

## 5.1 Immutable / lassan változó alap

```text
WorldDefinition
StarSystemDefinition
PlanetDefinition
InitialPlateDefinition
MaterialConstants
```

## 5.2 Analitikusan számolható időfüggések

Például:

```text
dayNight(t)
season(t)
orbitalPosition(t)
axialCycle(t)
stellarLuminosityTrend(t)
```

## 5.3 Történeti események

Például:

```text
MeteorImpact
PlateSplit
PlateMerge
SuperVolcanicEruption
MajorRift
OceanClosure
AtmosphericShock
```

## 5.4 Állapotot módosító numerikus mezők

Például:

```text
ElevationField
CrustThicknessField
TemperatureField
IceField
SoilField
```

Ezekhez checkpoint rendszer kell.

---

# 6. Csillagrendszer-generátor

A program ne csak bolygót, hanem csillagkörnyezetet generáljon.

## 6.1 Fő paraméterek

```text
starCount
```

Ajánlott 1.0 tartomány:

```text
1–3
```

### Minden csillaghoz

```text
Star
{
    Mass
    Radius
    Luminosity
    EffectiveTemperature
    SpectralClass
    Age
    Metallicity
    ActivityLevel
    FlareRate
    RadiationOutput
}
```

## 6.2 Csillagok száma

### Egycsillagos rendszer

Egyszerűbb, stabilabb.

### Kettőscsillag

Lehet:

```text
circumstellar orbit
```

vagy

```text
circumbinary orbit
```

Ez jelentős periodikus besugárzásváltozást adhat.

### Három csillag

Ritkább és dinamikailag komplexebb.

Első verzióban engedhető, de korlátozott, stabil konfigurációkkal.

---

# 7. Bolygó távolsága a csillagtól

Kötelező paraméter:

```text
semiMajorAxis
```

például AU-ban.

További pályaelemek:

```text
eccentricity
inclination
longitudeOfAscendingNode
argumentOfPeriapsis
meanAnomalyAtEpoch
```

## 7.1 Csillagenergia

A csillagtól kapott fluxus:

\[
F =
\frac{L}{4\pi r^2}
\]

Több csillagnál:

\[
F_{total}
=
\sum_i
\frac{L_i}{4\pi r_i^2}
\]

Ez közvetlen input a klímamodellhez.

## 7.2 Excentricitás

Nagy eccentricity:

```text
periapsis → nagyon meleg
apoapsis → hidegebb
```

Ez erős évszakos/éves klímahatást adhat még alacsony tengelyferdeségnél is.

---

# 8. Kozmikus környezet – mennyire „zűrös” az űr

A bolygó környezete kapjon egy külön:

```text
CosmicEnvironment
```

modult.

## 8.1 Paraméterek

```text
stellarDensity
asteroidFlux
cometFlux
interstellarRadiation
cosmicRayIntensity
nearbySupernovaRisk
rogueBodyEncounterRate
debrisDensity
giantPlanetShielding
magnetosphereProtection
galacticEnvironmentActivity
```

## 8.2 Egyszerű játékos paraméter

A GUI-ban lehet:

```text
Cosmic Hazard Level:
Very Low
Low
Moderate
High
Extreme
```

de mögötte több tényleges paraméter dolgozik.

## 8.3 Következmények

Magas hazard:

- gyakoribb meteor;
- gyakoribb üstökös;
- ritka extrém becsapódás;
- nagyobb sugárzási esemény;
- csillagkitörés;
- légkörvesztési események esélye.

Később az evolúciós motor számára:

- kihalási nyomás;
- mutációs környezet;
- izoláció;
- gyors ökológiai reset.

---

# 9. Bolygófizikai paraméterek

```text
PlanetDefinition
{
    Radius
    Mass
    Gravity
    MeanDensity

    RotationPeriod
    AxialTilt

    OrbitalElements

    CoreFraction
    MantleFraction
    CrustFraction

    InternalHeat
    MagneticFieldStrength

    SurfacePressure
    AtmosphereComposition

    OceanWaterInventory
}
```

## 9.1 Gravitáció

\[
g=
\frac{GM}{R^2}
\]

A játékos közvetlenül is állíthatja, vagy tömeg/sugár alapján származtatható.

Evolúciós motor számára később közvetlen input:

- testtartás;
- végtagterhelés;
- ugrás;
- repülés;
- maximális testméret.

---

# 10. Légkör

## 10.1 Fő komponensek

```text
N2
O2
CO2
H2O
CH4
Ar
other
```

A World Generator esetén az O₂ lehet abiogén vagy később élet által módosítható.

## 10.2 Fő derived értékek

```text
surfacePressure
meanMolecularWeight
greenhouseStrength
heatCapacity
radiativeTransferApproximation
soundSpeed
airDensity
```

## 10.3 Atmoszféra veszteség

Nagyon hosszú időskálán függhet:

```text
stellarWind
gravity
magneticField
upperAtmosphericTemperature
```

---

# 11. Térbeli reprezentáció

## 11.1 Ajánlott belső modell: cubed sphere

A bolygó ne belsőleg equirectangular grid legyen.

Ajánlott:

```text
6 cube face
+
quadtree LOD
+
sphere projection
```

Előny:

- nincs erős pólustorzítás;
- könnyű LOD;
- könnyű tile-alapú feldolgozás;
- globális gömb reprezentáció.

## 11.2 Export

A program ettől függetlenül tudjon exportálni:

- equirectangular PNG;
- heightmap;
- cubemap;
- raw float texture;
- GeoJSON-szerű régióhatárok;
- JSON metadata.

---

# 12. Planet Tile

Minden tile/cell minimálisan:

```text
WorldTile
{
    Id
    PositionOnSphere

    Elevation
    CrustType
    CrustAge
    PlateId

    SurfaceType
    WaterDepth

    Temperature
    Pressure
    Humidity

    Precipitation
    WindVector

    IceThickness
    SnowDepth

    SoilDepth
    SoilMinerality

    ResourceProfile
}
```

Az időfüggő értékek külön state layerbe tehetők.

---

# 13. Alapgeometria és kontinensmaszk

A kezdeti világ kialakításához több matematikai mezőt kombináljunk.

## 13.1 Noise családok

- Simplex/OpenSimplex;
- Perlin;
- fBm;
- ridged multifractal;
- Worley/Voronoi;
- domain warping;
- spherical harmonic jellegű alacsony frekvenciás mezők.

## 13.2 Alapképlet

\[
H_0(p)=
w_cC(p)
+
w_fF(p)
+
w_rR(p)
+
w_vV(p)
\]

ahol:

- \(C\): continent-scale mask;
- \(F\): fractal detail;
- \(R\): ridged mountains;
- \(V\): volcanic field.

Ez csak kezdeti állapot.  
A végleges domborzatot a tektonika és erózió módosítja.

---

# 14. Tektonikai modell

Ez a hosszú távú világfejlődés egyik központi eleme.

## 14.1 Lemezek létrehozása

A bolygófelszínt gömbi Voronoi-régiókra bontjuk.

```text
plateCount
```

tipikus tartomány:

```text
6–30
```

Minden plate:

```text
Plate
{
    Id
    CrustType
    MeanCrustThickness
    Density
    EulerPole
    AngularVelocity
    Buoyancy
    Age
}
```

## 14.2 Lemezmozgás

Gömbön egy lemez mozgása:

```text
Euler rotation
```

alapú.

A lemez pozíciója idővel:

\[
P(t)=R(\omega t)P_0
\]

ahol:

- \(R\): gömbi rotáció;
- \(\omega\): angular velocity.

## 14.3 Határinterakciók

Két lemez határán:

### Konvergens

```text
uplift
subduction
mountain building
volcanism
```

### Divergens

```text
rifting
new crust
basin formation
```

### Transform

```text
faulting
local relief
earthquake proxy
```

---

# 15. Kontinensvándorlás

A kontinensek nem külön mozgatott sprite-ok.

A szárazföld a:

```text
crust
+
uplift
+
erosion
+
seaLevel
```

eredménye.

Ezért egy kontinens:

- kettészakadhat;
- összeütközhet egy másikkal;
- elmerülhet;
- új magasföld keletkezhet;
- új szigetív alakulhat ki.

## 15.1 Új kontinens létrejötte

Nincs külön:

```text
CreateNewContinent()
```

szükségszerűen.

A következő folyamatból emergensen kijöhet:

```text
rift
→ új óceánmedence
→ két szárazföld szétválik
→ önálló kontinensek
```

vagy:

```text
plate collision
→ uplift
→ korábban víz alatti kérgrész kiemelkedik
→ új szárazföld
```

---

# 16. Lemez-születés és lemez-halál

Több százmillió éves skálán nem elég fix lemezekkel dolgozni.

Determinált deep-time események:

```text
PlateSplitEvent
PlateMergeEvent
SubductionTermination
RiftActivation
HotspotBirth
HotspotDeath
```

Ezeket a world seed és a geodinamikai állapot alapján ütemezzük.

---

# 17. Hegységek

A hegységek fő forrásai:

- lemezütközés;
- szubdukció;
- vulkanikus ívek;
- hotspotok;
- régi hegységek maradványai.

Hegységi uplift:

\[
\frac{dH}{dt}
=
UpliftRate
-
ErosionRate
\]

---

# 18. Erózió

## 18.1 Fő komponensek

- vízerózió;
- folyóbevágódás;
- lejtőomlás;
- glaciális erózió;
- parti erózió;
- hőmérsékleti aprózódás.

## 18.2 Egyszerű modell

\[
ErosionRate
=
k
\cdot
Rainfall^\alpha
\cdot
Slope^\beta
\cdot
MaterialFactor
\]

Az üledék alacsonyabb helyeken lerakódhat.

---

# 19. Tengerszint

A tengerszint dinamikus.

Függhet:

```text
globalWaterInventory
iceVolume
oceanBasinVolume
thermalExpansion
tectonicState
```

## 19.1 Jégolvadás

\[
\Delta SeaLevel
\propto
-\Delta IceVolume
\]

## 19.2 Tektonikai hatás

Az óceánmedencék térfogatváltozása több millió éves skálán megváltoztathatja a globális tengerszintet.

---

# 20. Domborzat végső modell

\[
Elevation =
BaseCrust
+
TectonicUplift
+
Volcanism
-
Erosion
+
Sedimentation
+
ImpactTopography
\]

Ez időfüggő.

---

# 21. Vulkánosság

## 21.1 Források

- lemezhatárok;
- hotspotok;
- rift zónák;
- mantle plume proxy.

## 21.2 Vulkanikus esemény

```text
EruptionEvent
{
    Position
    Magnitude
    Duration
    AshMass
    GasRelease
    LavaVolume
}
```

## 21.3 Hatások

Rövid táv:

- aeroszol;
- lehűlés;
- lokális pusztítás.

Hosszú táv:

- új föld;
- hegység/sziget;
- CO₂ felszabadítás;
- termékeny ásványi felszín.

---

# 22. Meteor- és üstökösbecsapódások

A becsapódások ne kézzel scripteltek legyenek.

## 22.1 Hazard rate

\[
\lambda_{impact}
=
f(
cosmicEnvironment,
asteroidBelts,
cometReservoir,
giantPlanetShielding,
planetCrossSection
)
\]

## 22.2 Determinisztikus eseménygenerálás

Időt osszuk pl. időablakokra:

```text
ImpactEpoch = 10 000 years
```

Minden epoch:

```text
u = Random(
    WorldSeed,
    "IMPACT",
    epochIndex
)
```

A hazard alapján eldöntjük, történik-e esemény.

Ha igen:

```text
position
mass
velocity
angle
composition
```

mind determinisztikusan generált.

## 22.3 Nagy események

Ritka heavy-tail eloszlás.

Így:

- sok kicsi becsapódás;
- kevés közepes;
- nagyon ritka globális esemény.

## 22.4 Kráter

Kráter-paraméter:

```text
diameter
depth
rimHeight
ejectaRadius
```

A height field tartósan módosul.

---

# 23. Determinisztikus „random történelem”

Ez a rendszer magja.

A világ története nem előre tárolt több milliárd évnyi lista.

Helyette:

```text
WorldSeed
+
State
+
Time
+
Deterministic hazard functions
```

generálja.

## 23.1 Példák

### Meteorit

```text
ImpactSchedule(seed, cosmicEnvironment)
```

### Supervolcano

```text
VolcanicEventSchedule(seed, tectonicState)
```

### Rift

```text
RiftProbability(seed, plateStress, time)
```

### Jégkorszak

```text
OrbitalCycles
+
CO2
+
albedo
+
ocean state
```

együttes következménye.

---

# 24. Ciklikus maszkok

A világ periodikus rendszereit matematikai komponensek adják.

## 24.1 Általános ciklus

\[
X(t)
=
A\sin(
2\pi t/P+\phi
)
\]

ahol:

- \(A\): amplitúdó;
- \(P\): periódus;
- \(\phi\): seedelt fázis.

## 24.2 Több ciklus összege

\[
X(t)
=
\sum_i
A_i
\sin(
2\pi t/P_i+\phi_i
)
\]

Ez kiváló:

- klímaciklusokra;
- tengelyváltozásra;
- csillagaktivitásra;
- óceáni oszcillációkra.

## 24.3 Bolygónként eltérő

A `P`, `A`, `φ` értékeket a seed generálja a fizikailag megengedett intervallumokon belül.

Így minden bolygó más, de teljesen determinisztikus.

---

# 25. Pálya- és tengelyciklusok

Milanković-szerű komponensek:

```text
eccentricityCycle
obliquityCycle
precessionCycle
```

Nem kell földi periódusokat használni.

A periódusok függjenek a rendszer paramétereitől és legyenek seedelt, fizikailag ésszerű tartományban.

Ezek több tízezer–millió éves klímaciklusokat adnak.

---

# 26. Csillag hosszú távú változása

A csillag fényessége ne legyen örökké konstans.

\[
L(t)=L_0\cdot StellarEvolutionFactor(t)
\]

Első verzióban elég lassú, sima trend.

Aktív csillagnál külön:

```text
stellarCycle
flareEvents
```

---

# 27. Nappal és éjszaka

Minden tile lokális besugárzása:

\[
Insolation
=
F_{star}
\cdot
max(0,\cos\theta)
\]

ahol \(\theta\) a nap beesési szöge.

Több csillagnál összegezni kell.

A rendszerből kijön:

- nappal;
- éjszaka;
- szürkület;
- poláris nappal;
- poláris éjszaka.

---

# 28. Klímamodell

A cél nem CFD, hanem stabil, konzisztens globális klíma.

## 28.1 Hőmérséklet komponensek

\[
T =
T_{radiative}
+
T_{greenhouse}
+
T_{ocean}
-
T_{altitude}
+
T_{weather}
+
T_{cycle}
\]

## 28.2 Radiatív alap

Egyszerű egyensúly:

\[
T_{eq}
\propto
\left(
\frac{F(1-A)}{4\sigma}
\right)^{1/4}
\]

ahol:

- \(F\): bejövő fluxus;
- \(A\): albedo;
- \(\sigma\): Stefan–Boltzmann állandó.

## 28.3 Lapse rate

\[
T_{altitude}
=
\Gamma h
\]

---

# 29. Albedo

Tile albedo függ:

- víz;
- hó;
- jég;
- kőzet;
- homok;
- felhő;
- később növényzet.

Globális feedback:

```text
cooling
→ more ice
→ higher albedo
→ more cooling
```

Ez lehetővé tesz valódi klímaváltásokat.

---

# 30. Szél

Első verzióban:

- szélességi alap cellák;
- Coriolis-proxy;
- hőmérsékleti nyomásgradiens;
- hegyek eltérítő hatása.

Tile:

```text
WindVector
```

A szél később:

- időjárás;
- párolgás;
- csapadék;
- szagterjedés;
- repülés

alapja.

---

# 31. Nedvesség és csapadék

Nedvesség forrás:

- óceán;
- tó;
- nedves felszín.

Párolgás:

\[
Evaporation
=
f(
temperature,
wind,
surfaceWater
)
\]

Légköri moisture transport:

```text
advection along wind
```

Orografikus csapadék:

```text
moist air
→ mountain
→ uplift
→ precipitation
```

Leeward oldalon:

```text
rain shadow
```

---

# 32. Időjárás

A rövid távú időjárás legyen determinisztikus, de kaotikus hatású.

## 32.1 Weather field

Használható:

```text
advected procedural field
+
pressure cells
+
temperature gradient
+
humidity
```

## 32.2 Stateless időnoise

Például:

\[
W(p,t)
=
Noise(
Warp(p,t),
seed
)
\]

így tetszőleges időpontra lekérdezhető.

## 32.3 Fontos

A weather noise nem írhatja felül a klímát.

Példa:

```text
ClimateMeanTemperature = 18°C
WeatherDeviation = -4°C
CurrentTemperature = 14°C
```

---

# 33. Folyók

A domborzat alapján:

1. depressziók kezelése;
2. flow direction;
3. flow accumulation;
4. river threshold;
5. river width;
6. erosion.

Tile-hoz:

```text
FlowDirection
FlowAccumulation
RiverDischarge
```

---

# 34. Folyók időbeli változása

Kis időskálán:

- vízhozam;
- árvíz.

Nagy időskálán:

- medervándorlás;
- bevágódás;
- deltaépülés;
- folyóelfogás;
- vízgyűjtő átrendeződés.

A folyóhálózatot geológiai checkpointok között újra lehet számolni.

---

# 35. Tavak

Kialakulás:

- topográfiai mélyedés;
- gleccser;
- kráter;
- tektonikus medence;
- folyóelzárás.

Időben:

- feltöltődhet;
- kiszáradhat;
- túlfolyhat;
- tengerrel kapcsolatba kerülhet.

---

# 36. Jég és hó

## 36.1 Seasonal snow

Függ:

```text
temperature
precipitation
sunlight
```

## 36.2 Permanent ice

Ha az éves jégmérleg pozitív:

\[
Accumulation > Melt
\]

jégtakaró növekszik.

## 36.3 Gleccseráramlás

Egyszerűsített:

```text
ice thickness
+
slope
```

alapú diffúziós modell.

Hatása:

- erózió;
- völgyek;
- morénák;
- tengerszint.

---

# 37. Óceán

Első verzióhoz nem kell teljes ocean circulation CFD.

Elég:

```text
surfaceCurrentField
deepOceanMixingProxy
heatCapacity
coastalUpwelling
```

A felszíni áramlás függjön:

- széltől;
- Coriolis-tól;
- partvonaltól;
- hőmérséklettől.

---

# 38. Biome még élet nélkül

A World Generator ne definiáljon biológiai biomot véglegesen.

Inkább generáljon:

```text
AbioticHabitatClass
```

Például:

- polar ice;
- alpine;
- cold dry plain;
- temperate humid lowland;
- hot wet lowland;
- hot arid basin;
- shallow sea;
- deep sea;
- tidal coast;
- river floodplain;
- geothermal field.

Később az evolúciós motor ebből saját biológiai biome-okat alakíthat.

---

# 39. Talaj / regolit

Élet előtt is fontos:

```text
RegolithProfile
{
    Depth
    Porosity
    WaterRetention
    MineralDiversity
    PhosphorusAvailability
    NitrogenAvailability
    Iron
    Sulfur
    Salinity
    pHProxy
}
```

Forrás:

- alapkőzet;
- vulkanizmus;
- erózió;
- üledék;
- víz;
- hőmérséklet.

---

# 40. Erőforrások

A későbbi élet számára szükséges mezők:

```text
WaterAvailability
LightAvailability
CarbonAvailability
NitrogenAvailability
PhosphorusAvailability
SulfurAvailability
IronAvailability
MineralEnergyPotential
OrganicMatter = 0 initially
```

A World Generator csak abiogén készleteket ad.

---

# 41. Geotermikus energia

Tile:

```text
GeothermalFlux
```

Magas lehet:

- vulkáni régió;
- rift;
- hotspot;
- óceáni hátság.

Később kemotróf élet számára fontos.

---

# 42. Mágneses tér

Globális paraméter:

```text
MagneticFieldStrength
```

Idővel lassan változhat.

Hat:

- légkörvesztésre;
- sugárzási környezetre;
- később magnetorecepció értékére.

---

# 43. Nagy világ-események katalógusa

## Geológiai

- `PlateSplit`
- `PlateMerge`
- `MajorCollision`
- `RiftOpening`
- `OceanOpening`
- `OceanClosure`
- `MountainPulse`
- `HotspotBirth`
- `HotspotDeath`
- `SuperVolcano`

## Klimatikus

- `GlaciationOnset`
- `GlaciationRetreat`
- `HyperthermalEvent`
- `MegaDrought`
- `OceanAnoxiaProxy`
- `SeaLevelPulse`

## Kozmikus

- `MeteorImpact`
- `CometImpact`
- `StellarFlare`
- `RadiationPulse`
- `NearbySupernova`
- `RogueBodyPerturbation`

## Atmoszférikus

- `AtmosphericLossPulse`
- `GreenhouseShift`
- `AerosolCooling`

---

# 44. Esemény-súlyosság

Minden esemény:

```text
EventSeverity
{
    Local
    Regional
    Continental
    Global
}
```

és:

```text
Magnitude
Duration
AffectedArea
RecoveryTimescale
```

---

# 45. Események determinisztikus időzítése

## 45.1 Time-bucket módszer

Példa:

```text
epochLength = 1000 years
epochIndex = floor(time / epochLength)
```

Random:

```text
u =
Random(seed, eventType, epochIndex)
```

Hazard:

```text
P(event) =
1 - exp(-lambda * epochLength)
```

Ha:

```text
u < P(event)
```

esemény történik.

## 45.2 Pontos eseményidő epochon belül

Új determinisztikus random:

```text
v =
Random(seed, eventType, epochIndex, "TIME")
```

\[
t_{event}
=
t_{epochStart}
+
v\cdot epochLength
\]

---

# 46. Hazard rate állapotfüggően

A λ ne mindig konstans legyen.

Például:

\[
\lambda_{volcano}
=
f(
plateStress,
subductionLength,
hotspots,
internalHeat
)
\]

\[
\lambda_{impact}
=
f(
cosmicEnvironment,
planetCrossSection,
shielding
)
\]

Így a világ belső állapota alakítja a jövőjét.

---

# 47. Event sourcing + checkpoint stratégia

A teljes világot nem kell minden évre elmenteni.

## 47.1 Tároljuk

```text
InitialWorld
+
MajorEvents
+
PeriodicCheckpoints
```

## 47.2 Checkpoint

Például:

```text
every 100k / 1M years
```

a geológiai modelltől függően.

## 47.3 Lekérdezés

Ha kérjük:

```text
StateAt(73.4 Myr)
```

akkor:

1. legközelebbi korábbi checkpoint;
2. replay események;
3. numerikus integráció;
4. analitikus fast-time mezők kiértékelése.

---

# 48. Timestep rendszer

Ne legyen globális fix timestep.

## 48.1 Fast fields

Analitikus:

```text
day/night
season
stellar cycles
weather procedural component
```

## 48.2 Medium dynamics

Adaptív:

```text
snow
ice
soil moisture
river discharge
```

## 48.3 Geological

Nagy lépések:

```text
1k – 100k years
```

de esemény közelében kisebb.

---

# 49. Adaptív timestep

Egy mező karakterisztikus ideje:

\[
\tau_X=
\frac{|X|}
{|dX/dt|+\epsilon}
\]

Timestep:

\[
\Delta t
=
safety
\cdot
\min_X\tau_X
\]

Korlát:

```text
minStep ≤ Δt ≤ maxStep
```

---

# 50. Determinisztikus time travel

A program támogassa:

```text
SetTime(0)
SetTime(100 Myr)
SetTime(20 Myr)
SetTime(100 Myr)
```

A két 100 Myr állapot azonos legyen.

Ez kritikus az evolúciós játék timeline-jához.

---

# 51. LOD és idő

A részletesség térben és időben eltérhet.

Példa:

```text
planet view:
global 1024² equivalent

continent:
higher terrain LOD

region:
river + local terrain detail

local:
micro-height + weather detail
```

A magasabb LOD olyan részletet adjon hozzá, amely a seedből determinisztikusan következik, és nem módosítja a nagy léptékű földrajzot.

---

# 52. Hierarchikus noise

A terrain detail:

\[
H =
H_{L0}
+
H_{L1}
+
H_{L2}
+
...
\]

A magasabb LOD csak újabb frekvenciatartományt ad hozzá.

Ez megakadályozza, hogy zoomoláskor „más világ” jelenjen meg.

---

# 53. World Generator API

## 53.1 Generate

```text
GenerateWorld(WorldGenerationRequest request)
    -> WorldPackage
```

## 53.2 Query

```text
GetWorldMetadata()
GetStarSystem()
GetPlanetState(time)
GetTile(tileId, time, lod)
GetRegion(bounds, time, lod)
GetClimate(tileId, time)
GetEventHistory(from, to)
GetNextMajorEvents(afterTime)
```

## 53.3 Advance

```text
AdvanceTo(time)
AdvanceBy(delta)
```

## 53.4 Export

```text
ExportHeightMap()
ExportClimateMap()
ExportWaterMap()
ExportPlateMap()
ExportResourceMap()
ExportSnapshot()
```

---

# 54. WorldGenerationRequest

Példa:

```json
{
  "seed": "A7C9-4421",

  "starSystem": {
    "starCount": 2,
    "primaryStarMassSolar": 0.96,
    "secondaryStarMassSolar": 0.42,
    "binarySeparationAu": 18.0
  },

  "planet": {
    "radiusKm": 7420,
    "gravityG": 0.93,
    "rotationHours": 27.8,
    "axialTiltDeg": 19.5,
    "semiMajorAxisAu": 1.08,
    "eccentricity": 0.034,
    "oceanCoverageTarget": 0.61
  },

  "geology": {
    "plateCount": 14,
    "tectonicActivity": 0.72,
    "volcanicActivity": 0.51,
    "internalHeat": 0.67
  },

  "cosmicEnvironment": {
    "hazardLevel": 0.58,
    "asteroidFlux": 0.61,
    "cometFlux": 0.43,
    "radiationLevel": 0.35
  }
}
```

Minden mező opcionálisan:

```text
fixed
range
auto
```

lehet.

---

# 55. Random generációs profilok

Hasznos presetek:

```text
EarthLike
OceanWorld
ArchipelagoWorld
SupercontinentWorld
HighGravityWorld
LowGravityWorld
IceWorld
HotWetWorld
TectonicallyViolent
CosmicallyDangerous
ChaoticBinary
StableOldWorld
```

A preset csak paramétereloszlás.

Nem külön generátor.

---

# 56. Példa random világ

## Nereida-7

```text
Seed: A7C9-4421

Stars: 2

Primary:
0.96 M☉
0.89 L☉
moderate activity

Secondary:
0.42 M☉
wide companion

Planet orbital distance:
1.08 AU

Radius:
7420 km

Gravity:
0.93 g

Rotation:
27.8 h

Axial tilt:
19.5°

Orbit:
388 local days

Ocean coverage:
61%

Atmosphere:
1.08 atm

Plate count:
14

Tectonic activity:
High

Volcanism:
Moderate

Cosmic hazard:
Moderate-high

Mean climate:
temperate

Long-term climate variability:
moderate
```

Ez csak tesztvilág.

---

# 57. Példa determinisztikus deep-time történet

Ugyanazon seed esetén mindig ugyanaz:

```text
0 Myr
Initial world state

12.8 Myr
Major continental rift begins

18.2 Myr
New narrow ocean basin appears

36.4 Myr
Large volcanic island arc forms

51.9 Myr
1.7 km asteroid impact

77.1 Myr
Northern continental collision

92.6 Myr
Major mountain uplift pulse

114.0 Myr
Glaciation begins

128.4 Myr
Sea level -82 m

161.7 Myr
Plate merger closes inland sea

217.3 Myr
Large hotspot province develops

284.5 Myr
Supercontinent configuration

301.2 Myr
Supercontinent rifting begins
```

Más seed → teljesen más történelem.

---

# 58. Rövid idő vs mély idő

## 1 nap

Változik:

- fény;
- hőmérséklet;
- szél;
- felhő;
- csapadék.

Nem változik érzékelhetően:

- kontinens;
- hegység.

## 100 év

Változhat:

- gleccser;
- tó;
- partvonal kisebb mértékben;
- folyók;
- klímaátlag.

## 100 000 év

Jelentős:

- glaciális ciklus;
- tengerszint;
- folyóvölgy;
- erózió.

## 10 millió év

Jelentős:

- kontinenspozíció;
- hegység;
- óceán;
- tektonika;
- klímazónák.

## 500 millió év

A bolygó térképe akár teljesen más lehet.

---

# 59. Változási sebesség skálázása

Minden alrendszernek:

```text
CharacteristicTimescale
```

értéke van.

Példa:

```text
weather              hours
season               months
ice                  years–millennia
river morphology     centuries–millennia
erosion              kyr–Myr
plate motion          Myr
stellar evolution     100 Myr–Gyr
```

Ez alapján a scheduler eldöntheti, mit kell újraszámolni.

---

# 60. World State hash

Minden snapshothoz számolható:

```text
StateHash
```

Tartalma:

- version;
- seed;
- time;
- layer hashes;
- event history hash.

Ez automatizált determinisztikussági teszthez kulcsfontosságú.

---

# 61. Javasolt programarchitektúra

```text
WorldGenerator.App
WorldGenerator.Core
WorldGenerator.Random
WorldGenerator.Astronomy
WorldGenerator.Planetology
WorldGenerator.Tectonics
WorldGenerator.Terrain
WorldGenerator.Atmosphere
WorldGenerator.Climate
WorldGenerator.Hydrology
WorldGenerator.Cryosphere
WorldGenerator.Resources
WorldGenerator.Events
WorldGenerator.Time
WorldGenerator.Persistence
WorldGenerator.Export
WorldGenerator.Tests
```

A konkrét nyelv lehet tetszőleges; a modulhatárok fontosabbak.

---

# 62. Fő domain objektumok

```text
WorldDefinition
StarSystem
Star
PlanetDefinition
OrbitalElements
CosmicEnvironment

Plate
PlateBoundary
CrustCell

WorldTile
TerrainLayer
OceanLayer
AtmosphereLayer
ClimateLayer
HydrologyLayer
IceLayer
ResourceLayer

WorldEvent
WorldCheckpoint
WorldSnapshot
```

---

# 63. Réteges state

Ne legyen minden egy `WorldTile` mutable objektumban.

Jobb:

```text
StaticGeometryLayer
GeologyLayer
TerrainLayer
AtmosphereLayer
ClimateLayer
WaterLayer
IceLayer
ResourceLayer
```

Ez:

- cache-elhető;
- külön frissíthető;
- GPU-barát;
- evolúciós motor számára külön lekérdezhető.

---

# 64. Mentési formátum

## World package

```text
/world.json
/stars.json
/planet.json
/events.bin
/checkpoints/
/layers/
/metadata.json
```

Exportálható egyetlen:

```text
.worldpkg
```

konténerbe.

---

# 65. Verziózás

Minden generált world package:

```text
GeneratorVersion
SimulationVersion
SchemaVersion
RandomAlgorithmVersion
```

Az algoritmus verzióváltás seed mellett más világot eredményezhet, ezért verzió nélkül tilos reprodukciót ígérni.

---

# 66. Parancssori program – minimum

```text
worldgen generate --seed A7C9-4421
worldgen info world.worldpkg
worldgen snapshot world.worldpkg --time 10Myr
worldgen snapshot world.worldpkg --time 100Myr
worldgen export world.worldpkg --layer height --time 50Myr
worldgen events world.worldpkg --from 0 --to 500Myr
worldgen verify world.worldpkg
```

---

# 67. Opcionális World Viewer

A World Generatorhoz már az evolúciós játék előtt érdemes egy egyszerű viewer.

Nézetek:

```text
Planet
Elevation
Plates
Temperature
Rainfall
Wind
Water
Ice
Resources
Events
```

Idő slider:

```text
0 → 1 Gyr
```

A slider húzásakor látható:

- kontinensvándorlás;
- jég változása;
- tengerszint;
- becsapódási kráter;
- hegységképződés.

---

# 68. Teljesítmény

## Alapelv

A program ne számoljon olyat, amit analitikusan le lehet kérdezni.

### Példa

Nappal/éjszaka:

nem kell minden percet eltárolni.

### Példa

Meteor:

nem kell minden nap ellenőrizni.

Event epochból generálható.

### Példa

Tektonika:

nem kell másodperces timestep.

---

# 69. Cache stratégia

Cache:

- tile geometry;
- climate normals;
- checkpoint;
- event schedule chunk;
- regional derived data.

Kulcs:

```text
(seed, timeBucket, layer, tile, lod, version)
```

---

# 70. Tesztelési stratégia

## 70.1 Determinism test

```text
Generate(seed)
hash == expectedHash
```

## 70.2 Parallel determinism

```text
1 thread hash
==
16 thread hash
```

## 70.3 Time travel

```text
StateAt(100Myr)
StateAt(20Myr)
StateAt(100Myr)
```

első és második 100 Myr hash azonos.

## 70.4 Timestep invariance

```text
Advance 1000 years in one step
```

vs.

```text
1000 × 1 year
```

különbség tolerancián belül.

## 70.5 LOD invariance

A kontinens fő partvonala ne változzon azért, mert magasabb LOD-ra váltunk.

---

# 71. Property-based tesztek

Random seedek ezrein:

- gravity > 0;
- ocean fraction 0–1;
- water downhill folyik;
- folyó nem mászik fel hegyre;
- hőmérséklet fizikailag ésszerű;
- kontinensek nem NaN;
- event time monoton;
- azonos seed reprodukálható.

---

# 72. Statisztikai validáció

10 000 generált világon:

- kontinensszám eloszlás;
- óceánborítás;
- átlagos relief;
- hegységek;
- csapadék;
- jég;
- impact rate;
- plate count;
- climate variability.

Ez kiszűri, ha a generator minden világot túl hasonlóra gyárt.

---

# 73. Vizualizációs acceptance criteria

Egy világ akkor tekinthető vizuálisan használhatónak, ha:

- kontinensek természetesnek hatnak;
- hegységek lemezlogikát követnek;
- folyók hegyből tenger felé futnak;
- száraz térségek klímailag indokolhatók;
- jég a hőmérséklettel konzisztens;
- partvonal változatos;
- nagy időugrásnál látható geológiai változás.

---

# 74. MVP – World Generator 0.1

Tartalom:

1. single-star system;
2. planet physical parameters;
3. cubed-sphere grid;
4. deterministic random framework;
5. static plate generation;
6. initial terrain;
7. sea level;
8. temperature;
9. basic wind;
10. precipitation;
11. rivers;
12. seasonal cycle;
13. snapshot/export;
14. simple viewer.

Még nincs deep-time plate evolution.

---

# 75. World Generator 0.2

Hozzáad:

- moving tectonic plates;
- erosion;
- mountain evolution;
- long-term climate;
- ice;
- dynamic sea level;
- checkpoints.

---

# 76. World Generator 0.3

Hozzáad:

- meteor/comet events;
- supervolcano events;
- plate split/merge;
- hotspot;
- cosmic hazard;
- star activity;
- deep-time event scheduler.

---

# 77. World Generator 0.4

Hozzáad:

- multiple stars;
- complex orbital forcing;
- advanced ocean currents;
- atmospheric loss;
- richer resource/geochemistry;
- high-LOD regional detail.

---

# 78. World Generator 1.0 kritériuma

A program képes:

```text
Generate(seed)
```

és megjeleníteni ugyanazt a bolygót:

```text
t = 0
t = 1 year
t = 10 kyr
t = 1 Myr
t = 100 Myr
t = 1 Gyr
```

úgy, hogy:

- rövid időn belül finom változások;
- hosszú időn belül drasztikus változások;
- minden reprodukálható;
- a világ geológiailag és klimatikusan belsőleg konzisztens.

---

# 79. Claude számára ajánlott fejlesztési sorrend

## Phase 1 – Foundation

- solution/project structure;
- stable deterministic random;
- units;
- time representation;
- vector/spherical math;
- WorldDefinition.

## Phase 2 – Astronomy

- stars;
- orbit;
- rotation;
- axial tilt;
- insolation.

## Phase 3 – Spatial model

- cubed sphere;
- tile IDs;
- LOD;
- coordinate conversion.

## Phase 4 – Initial geology

- plates;
- crust;
- initial elevation;
- sea level.

## Phase 5 – Climate

- temperature;
- wind;
- moisture;
- rainfall;
- seasons.

## Phase 6 – Hydrology

- rivers;
- lakes;
- soil moisture;
- snow.

## Phase 7 – Deep time

- plate movement;
- uplift;
- erosion;
- glaciation;
- sea level.

## Phase 8 – Events

- impacts;
- volcano;
- rifts;
- plate split/merge;
- stellar events.

## Phase 9 – Persistence

- checkpoints;
- event sourcing;
- world package;
- state hashing.

## Phase 10 – Viewer

- map layers;
- planet view;
- timeline;
- events.

---

# 80. Claude implementációs alaputasítás

A fejlesztés során az alábbi invariánsokat tilos megsérteni:

1. Azonos `(seed, version, time)` → azonos világ.
2. Feldolgozási sorrend nem befolyásolhatja a random eredményt.
3. A világ nem statikus snapshot, hanem időfüggő rendszer.
4. Rövid időskála kis, hosszú időskála nagy változást eredményezzen.
5. A deep-time változás ne egyszerű textúra-morph legyen, hanem tektonikai/klímafolyamatokból következzen.
6. Meteor, vulkán és hasonló esemény seedelt determinisztikus történelem része legyen.
7. A csillagrendszer paraméterei ténylegesen hassanak a klímára.
8. A gravitáció és légkör később közvetlenül használható legyen az evolúciós motorban.
9. LOD-váltás ne változtassa meg a világ makrostruktúráját.
10. Minden fizikai mennyiségnek legyen definiált mértékegysége.
11. A World Generator ne függjön az evolúciós motortól.
12. Az evolúciós motor később csak consumer legyen.

---

# 81. Későbbi evolúciós motorhoz szükséges world interface

A World Generator később a következő inputokat szolgáltassa az élőlények számára.

## 81.1 Fizikai környezet

```text
Gravity
AtmosphericDensity
Pressure
Temperature
Wind
WaterDepth
CurrentVelocity
TerrainSlope
SurfaceType
```

## 81.2 Energiaforrások

```text
SolarFlux
GeothermalFlux
ChemicalEnergyPotential
```

## 81.3 Anyagi erőforrások

```text
Water
Carbon
Nitrogen
Phosphorus
Sulfur
Iron
Minerals
```

## 81.4 Környezeti veszély

```text
UVRadiation
IonizingRadiation
TemperatureStress
FloodRisk
VolcanicRisk
ImpactRisk
StormRisk
```

## 81.5 Térbeli kapcsolat

```text
NeighborTiles
MovementCost
WaterConnectivity
LandConnectivity
Altitude
BiomeProxy
```

---

# 82. Az evolúciós motor és a World Generator határa

Nagyon fontos architekturális szabály:

```text
WORLD GENERATOR
    |
    | EnvironmentSnapshot / EnvironmentQuery
    v
EVOLUTION ENGINE
```

A World Generator:

```text
nem tud fajokról
nem tud genomról
nem tud fitnessről
```

Az Evolution Engine:

```text
nem generál kontinenseket
nem számol tektonikát
nem hoz létre időjárást
```

Ezek API-n keresztül kapcsolódnak.

---

# 83. EnvironmentSnapshot

Javasolt későbbi szerződés:

```text
EnvironmentSnapshot
{
    Time
    TileId

    Gravity

    Temperature
    Pressure
    Humidity

    LightFlux
    UVFlux

    WindVector

    WaterDepth
    WaterTemperature
    WaterSalinity
    WaterCurrent

    TerrainSlope
    SurfaceHardness

    GeothermalFlux

    Resources

    HazardProfile
}
```

Ez elég lehet ahhoz, hogy az evolúciós motor számolja:

- túlélést;
- mozgást;
- fotoszintézist;
- hőháztartást;
- légzést;
- táplálék-termelést.

---

# 84. Világ-események átadása az evolúciónak

A World Generator publikál:

```text
WorldEventOccurred
```

például:

```text
MeteorImpact
Glaciation
Flood
VolcanicWinter
SeaLevelChange
HabitatFragmentation
NewIslandEmergence
LandBridgeFormation
```

Az evolúciós motor ezekből módosítja:

- populációkat;
- migrációt;
- kihalást;
- izolációt;
- szelekciós nyomást.

---

# 85. Evolúciós jelentőségű geológiai események

Különösen fontos később:

## Új sziget

→ founder effect  
→ izoláció  
→ gyors fajképződés

## Földhíd

→ korábban izolált fajok találkoznak

## Hegység

→ populációk szétválása

## Folyó létrejötte

→ barrier vagy migrációs folyosó

## Jégkorszak

→ habitat shift

## Meteorit

→ tömeges kihalás

A World Generator csak a fizikai eseményt adja.  
Az evolúciós következményt az evolúciós motor számolja.

---

# 86. Jövőbeli faj-kép megjelenítés alapja

A World Generator később a faj képéhez háttér-contextet adhat:

```text
habitat
light
humidity
terrain
water
climate
gravity
```

A Genome/Phenotype motor pedig:

```text
body plan
size
limbs
eyes
surface
color
weapons
```

A kettő együtt adhat determinisztikus AI image promptot.

Példa:

```text
SpeciesVisualSeed =
Hash(
    WorldSeed,
    SpeciesGenomeId,
    PhenotypeVersion
)
```

Így a faj képe reprodukálható.

---

# 87. Nem-funkcionális követelmények

## Determinizmus

kritikus.

## Teljesítmény

fontos.

## Modularitás

kritikus.

## Numerikus stabilitás

kritikus.

## Debugolhatóság

kritikus.

Minden derived layernél lehessen megmondani:

```text
miért ilyen az érték?
```

Például:

```text
Temperature:
Radiative base  21.4
Altitude         -6.1
Ocean             +1.8
Season            -2.2
Weather           +0.7
----------------------
Current           15.6 °C
```

---

# 88. Debug layer-ek

Viewerben legyen:

- plate velocity;
- plate boundaries;
- uplift;
- erosion;
- rainfall;
- moisture transport;
- albedo;
- stellar flux;
- sea level;
- event influence;
- random field IDs.

Ez Claude fejlesztés közben rendkívül fontos.

---

# 89. Acceptance scenario A – statikus ellenőrzés

Seed:

```text
TEST-EARTH-001
```

Elvárt:

- 1 csillag;
- 0.9–1.1 g;
- 50–75% víz;
- több kontinens;
- poláris jég;
- működő folyók;
- éghajlati övek.

---

# 90. Acceptance scenario B – deep time

Seed:

```text
TEST-DEEPTIME-001
```

Követelmény:

0 Myr és 200 Myr között:

- legalább egy jelentős plate collision;
- legalább egy rift;
- mérhető kontinensvándorlás;
- megváltozott hegységek;
- megváltozott partvonal.

Ugyanazon seednél minden futás azonos.

---

# 91. Acceptance scenario C – cosmic hazard

Seed:

```text
TEST-IMPACT-001
```

Extrém cosmic hazard.

100 Myr időablakban:

- több kisebb impact;
- legalább egy nagyobb;
- kráterek tartósan megjelennek;
- klimatikus transient válasz látszik.

---

# 92. Acceptance scenario D – binary star

Seed:

```text
TEST-BINARY-001
```

- két csillag;
- stabil bolygópálya;
- periodikus fluxusmoduláció;
- ebből kimutatható klímajel.

---

# 93. Acceptance scenario E – timestep invariance

Két futás:

```text
A:
0 → 10 Myr egyben

B:
0 → 10 Myr 10 000 kisebb lépéssel
```

A makro layer-ek eltérése:

```text
< definiált tolerancia
```

---

# 94. Első tényleges fejlesztési cél

Még NE az evolúciós játék készüljön.

Első cél:

> **Egy seed alapján létrejön egy bolygó, a viewerben megjeleníthető, és az időcsúszkát 0–100 millió év között mozgatva látható, hogy a bolygó földrajza és klímája determinisztikusan fejlődik.**

Ha ez stabilan működik, csak utána:

- részletesebb lokális nézet;
- abiogén resource layer;
- evolution engine integration.

---

# 95. Definition of Done – World Generator 1.0

A World Generator 1.0 késznek tekinthető, ha:

- [ ] seed alapján világ generálható;
- [ ] csillagok száma paraméterezhető;
- [ ] csillagok tulajdonságai generálhatók;
- [ ] bolygó csillagtávolsága paraméterezhető;
- [ ] pálya paraméterezhető;
- [ ] gravitáció paraméterezhető;
- [ ] forgás és tengelyferdeség működik;
- [ ] nappal/éjszaka működik;
- [ ] évszakok működnek;
- [ ] légkör paraméterezhető;
- [ ] kontinensek generálódnak;
- [ ] tektonikai lemezek léteznek;
- [ ] lemezek időben mozognak;
- [ ] hegységek tektonikából képződnek;
- [ ] erózió működik;
- [ ] óceán és tengerszint működik;
- [ ] folyók és tavak működnek;
- [ ] klíma és csapadék működik;
- [ ] jég/hó működik;
- [ ] cosmic hazard paraméterezhető;
- [ ] meteorit események determinisztikusak;
- [ ] vulkáni események determinisztikusak;
- [ ] deep-time plate események determinisztikusak;
- [ ] world snapshot lekérhető tetszőleges időpontra;
- [ ] vissza- és előreugrás reprodukálható;
- [ ] LOD rendszer működik;
- [ ] viewerben layer-ek megjelennek;
- [ ] export működik;
- [ ] determinism tests zöldek;
- [ ] time-step invariance tests zöldek;
- [ ] world package menthető/tölthető;
- [ ] evolution engine számára EnvironmentSnapshot API rendelkezésre áll.

---

# 96. Claude-nak átadható rövid főprompt

## FELADAT

Készíts önálló, moduláris Dynamic Planet World Generator programot a jelen dokumentum specifikációja alapján.

A program elsődleges célja nem játék, hanem egy fizikailag koherens, seedelt, determinisztikus, időben változó bolygómodell.

A rendszer támogassa:

- csillagrendszert;
- 1–3 csillagot;
- bolygó-csillag távolságot;
- pályaelemeket;
- gravitációt;
- légkört;
- cubed-sphere térbeli modellt;
- tektonikai lemezeket;
- kontinensvándorlást;
- hegységképződést;
- eróziót;
- óceánt;
- klímát;
- időjárást;
- hidrológiát;
- jeget;
- kozmikus környezetet;
- determinisztikus meteor- és geológiai eseményeket;
- millió–milliárd éves deep-time változást;
- event sourcingot és checkpointokat;
- tetszőleges időpontra történő determinisztikus lekérdezést;
- layer exportot;
- egyszerű world viewert.

A program ne tartalmazzon evolúciós logikát.

Az Evolution Engine később kizárólag az EnvironmentSnapshot / WorldEvent API-n keresztül kapcsolódik.

### Kritikus invariáns

```text
WorldState =
F(
    WorldSeed,
    GeneratorVersion,
    Parameters,
    SimulationTime
)
```

Ugyanazon inputra mindig ugyanaz az output kell.

### Implementációs elv

Ne implementálj olyan random rendszert, amely a hívási sorrendtől függ.

Ne köss mindent globális fix timestephez.

Használj:

- stateless/counter-based deterministic randomot;
- analitikus ciklusokat;
- adaptív timesteppet;
- event schedule-t;
- checkpointokat;
- layer-alapú state-et;
- reprodukálható state hasht.

Először készíts architekturális tervet, domain modellt és milestone-listát. Utána implementáld fázisonként, minden fázis mellé automatizált tesztekkel.

---

# 97. Végső tervezési alapelv

A World Generator feladata nem az, hogy „szép random térképet” készítsen.

A cél:

> **egy olyan matematikailag reprodukálható bolygót létrehozni, amelynek saját geológiai, klimatikus és kozmikus története van, és amely több százmillió éves időskálán is következetesen változik.**

Erre a világra épül majd az evolúciós rendszer.

Az evolúció így nem egy statikus pályán történik, hanem egy olyan bolygón, amely maga is folyamatosan változik:

```text
élet alkalmazkodik a világhoz
        ↕
világ tovább változik
        ↕
új szelekciós nyomás
        ↕
új evolúció
```

Ez adja majd a teljes projekt legfontosabb hosszú távú emergens tulajdonságát.
