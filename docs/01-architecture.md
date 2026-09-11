# Dynamic Planet World Generator — Architektúra-terv

**Verzió:** v0.2 — a renderelt kép a generálás elsődleges kimenete
**Bemenet:** `dynamic_planet_world_generator_spec_v1_0.md` (v1.0), 4 db UI referenciakép
**Változás v0.1-hez:** a vizuális réteg nem külön projekt, hanem első osztályú kimenet. A panelek adatai kizárólag generált értékek.

---

## 0. Az alapelv

```
seed  →  világmodell  →  render layer stack  →  a kép
                     └→  panel-metrikák      → a kép melletti adatok
```

**Két invariáns, ami mostantól kötelező:**

1. **A képen látható minden pixel a világmodellből következik.** Nincs kézzel festett textúra, nincs dekoratív felhőréteg, nincs „hangulati" színkorrekció, ami nem valamelyik generált mezőből származik.
2. **A panelen látható minden szám a világmodellből olvasható ki.** Nincs kitalált érték, nincs placeholder. Ha egy mező szerepel a panelen, van mögötte generátor-forrás, egység és számítási lánc.

Ez a második invariáns erősebb, mint amilyennek látszik. A §87 debugolhatósági követelmény (`miért ilyen az érték?`) most a UI-ra is vonatkozik: minden panelmezőnek lekérdezhető a bontása.

---

## 1. A referenciaképek dekódolása

A 4 kép három zoom-szintet mutat ugyanabból a világból (`Nereida-7`, seed `A7C9-4421`). Ez pontosan a spec §2.5 skálázhatósági követelménye, konkrét UI-ban.

| Kép | Nézetszint | Panel | Layer-tabok |
|---|---|---|---|
| 2 | **Planet** — teljes gömb, űrből | World Overview | Surface / Climate / Water / Vegetation / Tectonics / Resources |
| 1, 4 | **Continent** — Aurelion, ferde légköri perspektíva | Continent Overview | Surface / Climate / Rivers / Habitats / Tectonics / Resources |
| 3 | **Region** — Silvertide Delta, alacsony magasság | Region Overview | ugyanaz, Habitats aktív |

A layer-tabok zoom-szintenként változnak (`Water`→`Rivers`, `Vegetation`→`Habitats`). Ez nem következetlenség, hanem helyes design: a releváns absztrakció más léptékben. Átveszem.

### 1.1 Figyelmeztetés: életre utaló mezők

A `Vegetation`, `Habitats`, `Population support`, `Biodiversity potential` mezők **életet sugallnak**, amit a spec §1.2 explicit kizár a WorldGen 1.0-ból.

Feloldás: ezek **abiotikus potenciál-metrikák**, nem tényleges élet.

| Panel-mező | Amit NEM jelent | Amit jelent |
|---|---|---|
| Population support | Hány élőlény él ott | Eltartóképesség-index abiotikus tényezőkből: hőmérséklet-stabilitás, vízelérhetőség, talajtermékenység, napenergia |
| Biodiversity potential | Hány faj él ott | Élőhely-heterogenitás: mikroklíma-változatosság + domborzati tagoltság + vízhatár-sűrűség |
| Vegetation / Habitats layer | Növényzettérkép | Potenciális biome-osztályozás (Whittaker-szerű), ami *ha lenne élet*, ott mi lenne |

Így a UI konzisztens marad az evolúciós motorral is: amikor az rákerül, ezek a mezők **nem tűnnek el**, hanem mellettük megjelenik a tényleges populáció. A potenciál és a valóság eltérése önmagában érdekes játékadat lesz.

---

## 2. Panel-mezők teljes leképezése

Ez a projekt szerződése: minden mező, forrás, egység, számítási lánc.

### 2.1 World Overview (Planet nézet)

| Mező | Példaérték | Forrás | Típus | Egység |
|---|---|---|---|---|
| Név | Nereida-7 | `Features.NameGen(seed, "PLANET")` | generált | — |
| Seed | A7C9-4421 | `WorldDefinition.Seed` | bemenet | — |
| Radius | 7 420 km | `PlanetDefinition.RadiusKm` | **bemeneti paraméter** | km |
| Gravity | 0.93 g | `g = GM/r²`, tömeg a sugárból + sűrűségből | derived | g |
| Axial tilt | 19.5° | `PlanetDefinition.AxialTiltDeg` | bemeneti paraméter | fok |
| Rotation period | 27.8 h | `PlanetDefinition.RotationHours` | bemeneti paraméter | óra |
| Orbital period | 388 d | Kepler III: `T = 2π√(a³/GM★)`, helyi napokban | derived | helyi nap |
| Ocean coverage | 61% | `count(elevation < seaLevel) / total`, level 7-en | **derived a domborzatból** | % |
| Atmosphere density | 1.08 atm | `AtmosphereLayer.SurfacePressure` | bemeneti paraméter | atm |
| Climate variability | Moderate | Globális átlaghőmérséklet szórása 100 kyr ablakon | derived, kvantált | ordinális |
| Seasonality | Medium | Tengelyferdeség + excentricitás → insolációs amplitúdó | derived, kvantált | ordinális |
| Tectonic activity | High | `Geology.TectonicActivity` (bemenet) + tényleges átlagos lemezsebesség | hibrid | ordinális |
| Volcanism | Moderate | `Geology.VolcanicActivity` + vulkáni események rátája | hibrid | ordinális |
| Habitability | High + sáv | Kompozit index (lásd 2.4) | derived | 0–1 → ordinális |

**Fontos megkülönböztetés:** az `Ocean coverage` a specben (§54) *bemeneti célérték* (`oceanCoverageTarget: 0.61`), a panelen viszont *tényleges mért érték*. A kettő nem azonos — a tengerszint-megoldó a célértéket közelíti, de a tektonika idővel elmozdítja. **A panelnek a ténylegeset kell mutatnia**, és deep-time-ban változnia kell. Ez jó teszt: ha a szám 500 Myr alatt sem mozdul, a tektonika nem hat a hipszometriára.

### 2.2 Continent Overview

| Mező | Példaérték | Forrás | Egység |
|---|---|---|---|
| Név | Aurelion | `Features.NameGen(seed, "CONTINENT", continentId)` | — |
| Szülő | Nereida-7 | hierarchia | — |
| Area | 24.8 M km² | Σ tile-terület a kontinens-komponensben | km² |
| Biomes | 9 | distinct biome-osztályok száma a területen | db |
| Major mountain systems | 3 | összefüggő komponensek, ahol elevation > küszöb ÉS uplift-eredetű | db |
| River basins | 11 | distinct vízgyűjtők, ahol lefolyás > küszöb | db |
| Coastal complexity | High | fraktáldimenzió (box-counting) a partvonalon | ordinális |
| Dominant climate | Temperate / Subtropical | 2 leggyakoribb Köppen-szerű zóna területarány szerint | — |
| Population support | Very High | területre súlyozott eltartóképesség-index | ordinális |

A `Coastal complexity` fraktáldimenziója szép, mert **közvetlenül validálja a §73 vizuális acceptance-t** („partvonal változatos"). Ha az érték 1.05 körül van, a partvonal sima és unalmas — a generátor rossz, nem a metrika.

### 2.3 Region Overview

| Mező | Példaérték | Forrás | Egység |
|---|---|---|---|
| Név | Silvertide Delta | `Features.NameGen(..., regionId)` + feature-típus utótag | — |
| Biome mix | Wetlands / Coastal Forest / Floodplain | top-3 biome területarány szerint | — |
| Mean temperature | 18.7 °C | éves átlag `TemperatureField` a régióban | °C |
| Seasonal rainfall | High | csapadék évszakos amplitúdója | ordinális |
| River channels | 17 | `RiverGraph` élszám a régióban | db |
| Flood frequency | Moderate | csapadék-csúcs / mederkapacitás arány | ordinális |
| Soil fertility | Very High | `SoilLayer`: mélység × minerality × nedvesség | ordinális |
| Biodiversity potential | Exceptional | élőhely-heterogenitás index | ordinális |

A név `Delta` utótagja **nem véletlen** — a `Features` modul felismeri a morfológiai típust (delta, öböl, hegylánc, tundra, fennsík) és a névgenerátor ezt használja. Ezért lesz „Silvertide **Delta**", „Northwatch **Range**", „Halcyon **Basin**", „Frosthold **Tundra**" — pontosan úgy, ahogy a képeken.

### 2.4 Ordinális kvantálás

Sok mező szöveges skálán jelenik meg (Low/Moderate/High/Very High/Exceptional). Ez UI-döntés, de **a mögöttes érték mindig folytonos** és lekérdezhető.

| Szint | Percentilis-sáv |
|---|---|
| Very Low | 0–10% |
| Low | 10–30% |
| Moderate | 30–70% |
| High | 70–90% |
| Very High | 90–98% |
| Exceptional | 98–100% |

> **ND-09 — nyitott:** a percentilis **mihez** képest? Opciók: (a) a bolygón belüli eloszlás, (b) globális referencia-eloszlás sok generált világból, (c) Föld-normalizált. **Javaslatom: (b)** — előre kalibrált, verziózott referencia-eloszlás, amit ~1000 generált világból számolunk és a build részeként szállítunk. Enélkül egy jégvilágon minden régió „Exceptional" termékenységű lenne, ami félrevezető. Az (a) opció önreferenciális, a (c) antropocentrikus.

### 2.5 Amit a HUD mutat, de NEM a WorldGen dolga

A képek fejlécében erőforrás-számlálók (`1.24K +18`, `2.07K +21`…) és a `LIVE — This is a live game` felirat szerepel. Ezek a **játékréteghez** tartoznak, nem a generátorhoz. A WorldGen ezeket nem szolgáltatja. A dátum (`2350.04.17`) és a sebességvezérlés viszont igen — az a `SimulationTime` UI-vetülete.

---

## 3. Render-architektúra

### 3.1 A fidelity-kérdés, őszintén

A referenciaképek AI-generált koncepciórajzok. Egy valós idejű, procedurális adatból dolgozó renderer **nem fogja pixelre reprodukálni** őket — a koncepciórajz olyan részletgazdagságot tartalmaz, ami nem következik semmilyen adatmodellből.

Amit viszont el lehet érni, és amit célként tűzök ki:

| Vizuális jellemző | Elérhető? | Hogyan |
|---|---|---|
| Gömb alakú bolygó, terminátorral | Igen, könnyen | Sphere mesh + nap-irány az `Astronomy`-ból |
| Atmoszférikus perem-fény (kék halo) | Igen | Rayleigh-szórás, analitikus közelítés |
| Óceán-csillanás, mélységfüggő szín | Igen | Fresnel + mélység-alapú abszorpció a `WaterDepth`-ből |
| Változatos szárazföld-színek | Igen | Biome → albedo LUT, nedvességgel modulálva |
| Hegyek árnyékolása, gerincek | Igen | Normal map az `ElevationField`-ből + AO |
| Hó a magas hegyeken | Igen | `SnowDepth` + `IceField` maszk |
| **Ciklonok, spirális felhőörvények** | Részben | Kell valódi ciklonelhelyezés a szélmezőből — nem dekoráció (lásd 3.4) |
| Felhők ott, ahol csapadék van | Igen | `Precipitation` + `Humidity` → felhősűrűség |
| Volumetrikus felhővetés a felszínre | Igen, drágán | Cloud shadow map |
| A koncepciórajz festői mikrodetálja | **Nem** | Nem következik adatból |

**Reális várakozás: 80–85% vizuális közelség**, és ami hiányzik, az nem az adatmodell hibája. Ha ezt előre elfogadjuk, nem lesz csalódás az M6 környékén.

### 3.2 Render-adatszerződés

A generátor és a renderer közötti határ egy explicit textúra-készlet. **Ez a szerződés a projekt legfontosabb interfésze** — ha jól definiált, a renderer és a szimuláció függetlenül fejleszthető.

```
RenderLayerStack(time, lod, bounds)
{
    // Geometria
    Elevation        R32F    méter, tengerszinthez képest
    WaterDepth       R32F    méter, 0 = szárazföld

    // Felszín-megjelenés
    BiomeId          R8U     enum → albedo LUT
    Wetness          R8       0–1, talajnedvesség (sötétíti a felszínt)
    SnowCover        R8       0–1
    IceThickness     R16F     méter (tengeri jég + gleccser)
    Roughness        R8       felszíni érdesség (kőzet vs homok vs víz)

    // Atmoszféra
    CloudDensity     R8       0–1
    CloudTopHeight   R16F     méter
    Humidity         R8       0–1

    // Világítás (analitikus, nem textúra)
    SunDirection     vec3
    SunIrradiance    float    W/m²
    SecondarySun*    opcionális, kettőscsillagnál

    // Származtatott, GPU-n számolt
    Normal           ← Elevation-ből, sobel
    AmbientOcclusion ← Elevation-ből, horizon mapping
    CloudShadow      ← CloudDensity + SunDirection
}
```

**Fontos:** a `Normal` és az `AO` **nem** a generátor kimenete — GPU-n számolódik az elevationből. Ha a CPU-oldal állítaná elő, a memóriaforgalom megháromszorozódna, feleslegesen.

### 3.3 Három renderer, egy adatforrás

| Nézet | Geometria | LOD | Kamera | Sajátosság |
|---|---|---|---|---|
| **Planet** | Teljes gömb, kvadtree-vel LOD-olt | 6–8 | Orbitális, ~2–3 bolygósugár távolság | Atmoszférikus szórás dominál, terminátor, felhőréteg külön sphere shell |
| **Continent** | Gömbfelület-patch, displacement mapping | 9–11 | Ferde, ~50–200 km magasság | Görbület még látszik (a képeken is!), légköri perspektíva a távolban |
| **Region** | Tesszellált terep-patch | 12–14 | Alacsony, ~5–20 km | Víz-felszín shader, köd, a görbület elhanyagolható |

A képeken a **kontinensnézet megőrzi a bolygógörbületet** — a horizont ívelt, és a bal felső sarokban látszik az űr. Ez nem sík terepmegjelenítés. Architekturálisan ez azt jelenti, hogy a continent renderer ugyanazt a gömbi geometriát használja, mint a planet, csak közelebbi kamerával és magasabb LOD-dal. **Egyetlen geometriai pipeline, három kamerabeállítás** — ez lényegesen egyszerűbb, mint három külön renderer.

### 3.4 Felhők — ahol a legkönnyebb csalni, és nem szabad

A képeken jól kivehető spirális ciklonok vannak. A kísértés az, hogy ezeket dekoratív noise-szal rakjuk oda. Ez sértené a 0. szakasz 1. invariánsát.

Helyes megoldás:

```
1. Szélmező a klímamodellből (Coriolis + nyomásgradiens)
2. Ciklogenezis-alkalmasság = f(tengerfelszín-hőmérséklet, szélnyírás, szélesség)
   → trópusi óceánok fölött magas, egyenlítőn (Coriolis≈0) és pólusokon alacsony
3. Determinisztikus ciklon-elhelyezés: Sample(seed,"CYCLONE",tileId,timeBucket)
   < alkalmasság → ciklonmag keletkezik
4. A ciklon pályát követ (nyugatra sodródik, majd pólus felé kanyarodik)
5. A felhősűrűség-mező spirális perturbációt kap a ciklonmag körül
```

Így a ciklonok **ott lesznek, ahol fizikailag lenniük kell**, mozognak, és évszakosan sűrűsödnek. Ez a kép hitelességének nagy része — és ingyen adja a §84 `Flood` / vihar-eseményeket is az evolúciós motornak.

### 3.5 Perf-célok

| Nézet | Cél | Kritikus költség |
|---|---|---|
| Planet, statikus idő | 60 fps | Atmoszférikus szórás (raymarch) |
| Planet, idő-scrub | 30 fps | Layer-újraszámítás, nem a render |
| Continent | 60 fps | Displacement + magas LOD streaming |
| Region | 60 fps | Tesszelláció + vízshader |

Az idő-scrub a szűk keresztmetszet, nem a render. A megoldás a v0.1-ben leírt checkpoint + interpoláció (ND-03).

### 3.6 Sűrű teljes-rács gyorsutak

A teljes, fix levelű cubed-sphere mezők nem ritka adatszerkezetek. A
teljesítménykritikus deep-time út ezért használhat `face/u/v` szerint
közvetlenül indexelt tömböket és világfüggetlen, újrahasznált
szomszédindex-topológiát. Ez kizárólag reprezentációs optimalizálás: a
bejárási sorrendnek és minden numerikus eredménynek egyeznie kell az általános,
TileId/Dictionary alapú Core-úttal. Az általános API marad az orákulum és a
ritka vagy változó LOD-adatok útja; részletek: ND-66 és ND-67.

---

### 3.7 Adaptív terrain-fedés tulajdonosa (ND-70)

A kiválasztott cut nem közvetlenül renderlista: előbb minden érintett
base-tile alatt teljes renderpartícióvá egészül ki (`LodCoverage`).
A `LodCornerResolver` a legdurvább érintkező levélhez igazítja a közös
csúcsot/élt, a mező és a nyers morpholt quad a viewer callbackjéből jön.
E modulok UnityEngine nélkül tesztelhetők, nem kerülnek a Core-ba.

A sikeresen feltöltött dinamikus partícióhoz a `TerrainIndexMask` kapcsolja
ki a kiváltott statikus terrain-háromszögeket; üres partíció visszaállítja az
eredeti indexeket. A `PlanetGridMesh` kizárólag főszálon tölti a Unity mesh-eket.
Azonos chunk-topológia mellett is külön vizsgálja a csúcspozíció változását,
hogy a CPU-s geomorph ne ragadhasson be. A részleges indexfrissítés API-ja:
[Unity Mesh.SetIndexBufferData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetIndexBufferData.html).

### 3.8 Domborzati LOD-metrika (ND-71)

**Aktuális státusz, ND-72:** az alábbi kísérleti integráció az élő
teljesítményregresszió miatt visszavonva. Az aktív `PlanetGridMesh` ismét
az ND-70 gömbös kiválasztását és morphját használja, nincs domborzati
bounds-callback vagy bounds-cache a belső ciklusban. A tiszta opcionális
LOD API csak offline próbákhoz maradt meg. Az alábbi leírás történeti.

A CPU-kiválasztó a viewer `GetSurfaceLodBounds` callbackjén át a tényleges,
morph nélküli terepsarkokat és középpontot kapja, testkoordinátákban.
A nézetkúp a teljes mintaboundsot, a mintasűrűség és a morph az érintősíkbeli
kiterjedést és az eltolt középpont távolságát használja (`SurfaceLodBounds`).
Az alapgömb horizont-/backface-tesztje itt nem érvényes; nincs helyette
szigorú terrain-occlusion garancia. A minták közti rejtett terepmaximum és a
meredek falak vetített hibakorlátja szintén nyitott. Részletek: ND-71.

A bounds-cache egyetlen kérés kiválasztását és emisszióját szolgálja ki.
A minták a meglévő pozíció-cache-be kerülnek; a render-előkészítés ezért
külön vizsgálja a szín és normál meglétét is, duplikált LRU-bejegyzés nélkül.
A Core-modell, a kamera kezelése, a maxLOD és a budget nem változik.

### 3.9 Olcsó renderdiagnosztika (ND-72)

Az óceáni szűrés és fedéspótlás UTÁNI `LodCoverage.FindRenderedLeaf` adja
a nadír terrain-levelét. A viewer az emitben használt morpholt/közösélhez
illesztett négy sarkot méri; kérésenként egy új modellpontból víz/land állapot
és radiális felszíntávolság is készül. Ez nem része a kiválasztásnak.
A kérés elején rögzített kamera-/vetítési mátrix és viewport adja a quad
pixelátmérőjét; a homogén osztás és négy sarok átmérője Unity nélkül tesztelt.
Near-plane vagy kamera mögötti sarok érvénytelen mérést ad. Ez nem raycast,
nem láthatóságvizsgálat, nem a víz felszínének és nem a teljes viewportnak
a hibamérése. A diagnosztika saját ideje külön szerepel a logban.

### 3.10 Első felosztás és morph hangolása (ND-73)

A prioritásos cut opcionális `baseSplitScale` paramétere csak a még nem
felosztott statikus base csomópont split-küszöbét csökkenti. A mélyebb szintek
és a merge-küszöb változatlanok; az új base-splitnek a merge felett kell
maradnia. Az alapértelmezett 1-es skála a korábbi API-viselkedés.
A viewer 10 px első / 12 px további célt használ; a base-szülő morphja a
base-küszöböt kapja, a többi a rendes küszöböt. A morph-range 0,35.
A küszöbök és a morph-range kéréskori értékei rögzítettek a worker számára.
Nincs domborzati callback a kiválasztásban. A GPU-kísérleti út nem kap
előrehozott base-splitet. Részletek és mérési korlátok: ND-73.

### 3.11 Build-kori tereptávolság-proxy (ND-74)

`BuildStaticBaseLayer → statikus saroksugarak → TerrainLodProxy → cut + morph`.
A proxy saját másolatot tart a hatlapos sugarakról, valamint base alatti
maximum-piramist. Az adat snapshotként olvasható, nincs benne Core-hívás,
UnityEngine vagy módosítható publikus tömb. Új Build invalidálja; a kiválasztás
és emisszió ugyanazt a kéréskori példányt olvassa. Base > 8 és GPU-geometria
esetén a gömbös út marad. A meglévő culling nem változik ebben a lépésben.
A finom középpont sugarát bilineárisan interpoláljuk: ez a LOD-döntés
közelítése, nem a renderelt geometria lecserélése. A valódi mesh továbbra is
a teljes világmodellből készül. A nadírdiagnosztika külön méri a proxy
sugárhibáját a valódi modellponthoz képest.

### 3.12 Kirajzolt felszín mintapontos diagnosztikája (ND-75)

A `PlanetGridMesh.RenderDiagnostics` partial kizárólag sikeres mesh-uploadok
végleges vertex-/indexadatait jegyzi meg. A maszk elrejtett quad-indexei
immutábilis snapshotot kapnak; position-only frissítés a régi indexek mellé
az új pozíciólistát rögzíti. Az inaktív chunk nem mérhető láthatóként.
A `RenderedTileDiagnostics` Unity-független clipping/mélységtesztelő,
17×9 képernyőmintával. A kameravetítés a méréskori, nem a LOD-kéréskori.
A főszál csak snapshot-referenciát/mátrixot ad át és kész szöveget naplóz;
egyetlen mérőworker dolgozik, legfeljebb 1 Hz-en. Nincs Core-mintavétel,
mesh-collider, új rajzolás vagy a finomítást befolyásoló visszacsatolás.
A képterületi mintavétel és a GPU-képhez képesti korlátok az ND-75-ben élnek.

### 3.13 Adagolt képernyő-LOD (ND-76)

Kamera-snapshot + Build-kori terep-proxy → frustum/terepbounds alapú,
új osztásokra korlátozott cut → teljes fedés/közösél-feloldás →
chunk-cache összevetés → csak változott geometria emissziója → alkalmazás.
A halasztott cut új kérést indít álló kameránál is. A cache csak sikeres
alkalmazáskor vált generációt; a megszakított kérés nem publikál félkész mesht.
A numerikus világmodell változatlan; a becslés és az ütemezés viewer-logika.

### 3.14 Tereptile-döntés diagnosztika (ND-77)

CPU-emissziós tile-sorrend → material-bucket szerint konkatenált TileId-tömb
→ sikeres mesh-upload → ND-75 képernyőtalálat → pontos tile és a sikeres
kérés megállási trace-e. A megállási trace megfigyelés, nem LOD-visszacsatolás.
A mintázó az immutábilis, alkalmazott generációt kapja, nem a futó kérését.
A proxy-becslés és a feltöltött geometria mérete külön mező; az eltérést
nem szabad automatikusan egyetlen hibaforrásnak tulajdonítani.

Az ND-78 a renderer meglévő teljes-base kizárását a prioritásos kiválasztás
elé hozza: a biztosan később eldobott tengerfenék nem fogyaszt új osztási
keretet. A callback csak a base-en, a kész statikus adatokból dolgozik.
A nyers mélységű proxy-kísérlet túlosztása miatt visszavonva; az éles proxy
és morph metrikája változatlan. A látható fragmentum és a teljes quad
méretkülönbsége külön megoldandó kérdés marad.

### 3.15 Korlátos renderchunk-partíció (ND-80)

A CPU kiválasztás → óceáni szűrés → teljes base-fedés → globális
közösél-resolver lánc után külön csomagolás fut. A `DynamicMeshChunking`
a leveleket legdurvább területi gyökerek alá gyűjti, és a 256 levelet
meghaladó csoportokat rekurzívan bontja. Az előző sikeres partíció alapján
128 levélig nem vonja vissza az osztást. A TileId-k és a geometria nem
változnak; csak a renderer/diff/cache munkacsomagjai.

A worker az előző partíciót csak olvassa, az új csoportokat külön építi.
Sikeres alkalmazás vált generációt; a meglévő diff az eltűnt szülő- vagy
gyermekchunkokat ugyanabban az alkalmazásban kikapcsolja. A korlát per
chunk levélszám, nem frame-időkeret vagy összes memória-korlát.

### 3.16 Pontos vetület-cache (ND-81)

A `PlanetGridMesh.Refinement` egy `LodTerrainEvaluationCache` példányt tart
a cut/emit single-flight worker számára. Új kérés előtt pontos kamera-/
vetületegyezés és proxy-referenciaazonosság alapján megtartja vagy lecseréli;
Build és nem perspektivikus mód eldobja. A 262 144 bejegyzéses felső korlát
után is teljes kiértékelés történik, csak új tárolás nem.

A cache TileId → láthatóság/szöghiba leképezés, nem kiválasztási állapot.
A `LodSelectionWork` továbbra is friss prioritási sort, kvótát és trace-t
használ, és külön méri a selection/balance szakaszt. A geometria workerének
geomorph számítása is olvashat/írhat ide; a külön diagnosztikai task nem.
A `ProjectedLodView` változatlanul immutábilis. A coverage-függő közösél-
feloldást és a pozícióazonosság-ellenőrzést nem hagyjuk ki cache-találatkor.
Az emit belső részidői a feloldást/ellenőrzést, segédréteg-másolást és az
új chunk emisszióját külön mutatják; nem hozzáadandók az emit összegéhez.

### 3.17 Önálló víz-LOD előkészítés (ND-82, első kapu)

A `WaterLodSource` saját, másolt base-vízmaszkból és tengerszintsugárból
készít immutábilis kiválasztási forrást. A maszk a valóban emittált víz
azonosítóit jelenti, nem a tengerfenék-kizárást. Állandó sugarú
`TerrainLodProxy` használja újra a meglévő geometriai metrikát; nincs
magasság-/klímamintavétel vagy UnityEngine-függés. A `Select` saját előző
víz-cutot, szögküszöböt, levélkeretet és splitkvótát kap. Más forrásból
származó előzményt elutasít (új Build új snapshotot igényel).

A `WaterLodSelection` rendezett, csak olvasható leveleket és teljesen
lefedett kiváltandó base-gyökereket ad. A saját `LodCornerResolver` a finom
vízszélt a durva szomszéd/alap vízhúrjához illeszti. A kihagyott alap
érintetlenül fed, száraz base alá nem kerül új víz. A geometriai fedést
tesztek ellenőrzik; a vízszínek, vízmaszk és mesh-csere még nincsenek bekötve.

**A viewer most nem hívja ezt a modult.** Runtime-aktiválás csak a következő
kapuban: pontos attribútumforrás, statikus víz indexmaszkja, kizárólagos
durva/finom rajzolás, atomikus publikáció és hibánál visszaállás. A régi
víz-emisszió és a felhasználó által halasztott elsőzoom-probléma változatlan.

## 4. Modultérkép (frissítve)

```
┌──────────────────────────────────────────────────────────────┐
│                      WorldGen.Viewer                          │
│  ┌────────────┬────────────┬────────────┬─────────────────┐  │
│  │  Planet    │ Continent  │  Region    │  UI: panelek,   │  │
│  │  Renderer  │  Renderer  │  Renderer  │  timeline,      │  │
│  │            │            │            │  layer-tabok    │  │
│  └────────────┴────────────┴────────────┴─────────────────┘  │
└───────────────────┬──────────────────────┬───────────────────┘
                    │ RenderLayerStack     │ PanelMetrics
┌───────────────────▼──────────────────────▼───────────────────┐
│                    WorldGen.Presentation                      │
│   Biome→albedo LUT, ordinális kvantálás, felhő-szintézis,    │
│   ciklon-elhelyezés, textúra-csomagolás                       │
└───────────────────────────┬──────────────────────────────────┘
                            │
┌───────────────────────────▼──────────────────────────────────┐
│                      WorldGen.Features                        │
│   kontinens/régió szegmentálás, névgenerálás, aggregált       │
│   metrikák, morfológiai típusfelismerés, identitáskövetés     │
└───────────────────────────┬──────────────────────────────────┘
                            │
┌───────────────────────────▼──────────────────────────────────┐
│                    WorldGen.Simulation                        │
│      időlépés-ütemező, checkpoint, layer-invalidálás          │
└──┬────────┬────────┬────────┬────────┬────────┬──────────────┘
   │        │        │        │        │        │
┌──▼──┐ ┌───▼───┐ ┌──▼────┐ ┌─▼─────┐ ┌▼──────┐ ┌▼────────┐
│Astro│ │Tecto- │ │Terrain│ │Climate│ │Hydro  │ │Events   │
│nomy │ │nics   │ │Erosion│ │Atmos  │ │Cryo   │ │Scheduler│
└──┬──┘ └───┬───┘ └──┬────┘ └─┬─────┘ └┬──────┘ └┬────────┘
   └────────┴────────┴────────┴────────┴─────────┘
                            │
┌───────────────────────────▼──────────────────────────────────┐
│  WorldGen.Core — Grid, Time, Units, Layers, Math, Random     │
└──────────────────────────────────────────────────────────────┘
```

**Két új modul a v0.1-hez képest:**

| Modul | Felelősség | Miért kritikus most |
|---|---|---|
| `Features` | Szegmentálás, névadás, aggregált metrikák | A panelek adatai ebből jönnek |
| `Presentation` | Szimulációs mező → render-textúra | Ez a határ, ami a renderert leválasztja a szimulációtól |

A `Presentation` réteg **tiszta függvény**: `(szimulációs mezők, idő, LOD) → textúrák`. Nincs saját állapota, nincs saját randomja a determinisztikus rétegen kívül. Ezért tesztelhető: ugyanaz a világállapot ugyanazokat a textúrákat adja, bitre.

---

## 5. Biome-osztályozás — a kép színeinek forrása

A képek vizuális gazdagságának nagy része a biome-változatosságból jön (sötétzöld erdő, homokos szavanna, szürke szikla, fehér hó, türkiz sekély víz). Ez a `Presentation` réteg feladata.

**Bemenet (mind abiotikus, spec-konform):**

```
éves átlaghőmérséklet
éves csapadék
csapadék évszakossága
talajnedvesség
tengerszint feletti magasság
lejtő
talajmélység
hótakaró tartóssága
```

**Kimenet: biome-osztály**, Whittaker-diagram alapú, magasság/lejtő korrekcióval.

| Biome | T (°C) | Csapadék (mm/év) | Egyéb | Albedo (közelítő) |
|---|---|---|---|---|
| Poláris jég | < −10 | bármi | — | 0.75 |
| Tundra | −10…0 | < 400 | — | 0.20 |
| Boreális erdő | 0…7 | > 350 | — | 0.10 |
| Mérsékelt lombhullató | 7…18 | > 700 | — | 0.15 |
| Mérsékelt füves | 7…18 | 250–700 | — | 0.22 |
| Mediterrán cserjés | 12…20 | 300–800 | erős évszakosság | 0.20 |
| Sivatag | > 12 | < 250 | — | 0.35 |
| Szavanna | > 20 | 400–1300 | erős évszakosság | 0.25 |
| Trópusi esőerdő | > 20 | > 2000 | — | 0.12 |
| Vizes élőhely / delta | bármi | — | talajnedvesség > 0.8, lejtő < 1° | 0.14 |
| Csupasz szikla | bármi | — | talajmélység < 0.1 m, lejtő > 25° | 0.28 |
| Gleccser | bármi | — | jégvastagság > 10 m | 0.80 |
| Sekély tenger | — | — | mélység < 50 m | 0.08 |
| Mély óceán | — | — | mélység ≥ 50 m | 0.06 |

**Visszacsatolás:** az albedo bemenet a `Climate` modulnak (§29). Tehát a biome nem csak megjelenítés — hatással van a hőmérsékletre. Ez a körkörös függés a fixpont-iterációval oldódik (v0.1 §6).

A „Silvertide Delta" biome mix-e (`Wetlands / Coastal Forest / Floodplain`) pontosan ebből a táblázatból származik, területarány szerint rendezve.

> **ND-10 — nyitott:** a biome-osztályok Föld-alapúak. Egy 0.93 g gravitációjú, 1.08 atm nyomású, 27.8 órás forgású bolygón a küszöbök eltolódnának. Opciók: (a) fix Föld-küszöbök, (b) bolygóparaméterekkel skálázott küszöbök. **Javaslat: (a) az 1.0-ban**, mert a skálázás fizikája bizonytalan és a hibája nem lenne látható — de a küszöbök konfigurációba, ne kódba kerüljenek.

---

## 6. A `Features` modul — nevek és identitás

### 6.1 Szegmentálás

```
Kontinens = összefüggő szárazföldi komponens (elevation > seaLevel),
            területküszöb fölött (pl. > 1 M km²)

Régió     = kontinensen belüli szegmens, hibrid kritérium:
            vízgyűjtő-határok ∪ biome-klaszterek ∪ domborzati törések
```

### 6.2 Morfológiai típusfelismerés

A név utótagja a felismert alaktípusból jön:

| Típus | Felismerési kritérium | Utótag-készlet |
|---|---|---|
| Delta | folyótorkolat + lapos + sok ág | Delta, Mouth, Fan |
| Hegylánc | uplift-eredetű, elnyúlt, magas | Range, Mountains, Peaks, Spine |
| Medence | zárt mélyedés, körülvett | Basin, Hollow, Vale |
| Tundra | biome-alapú | Tundra, Barrens, Waste |
| Erdő | biome-alapú, kiterjedt | Forest, Woods, Wilds |
| Part | partvonal mentén, keskeny | Coast, Shore, Strand |
| Tóvidék | sok állóvíz | Lakes, Meres |
| Mocsár | wetlands + lapos | Fen, Marsh, Moor, Mire |
| Dombvidék | mérsékelt relief | Foothills, Downs, Rise |
| Szigetcsoport | sok kis komponens | Isles, Archipelago, Skerries |

A referenciaképek nevei mind beleillenek: `Frosthold **Tundra**`, `Northwatch **Range**`, `Halcyon **Basin**`, `Veldrin **Forests**`, `Isleward **Coast**`, `Azure **Lakes**`, `Marshlight **Fen**`, `Southwatch **Foothills**`, `Wavecradle **Isles**`.

### 6.3 Névgenerálás

Kéttagú: `<tő> + <utótag>`, ahol a tő szótag-templétekből épül, seedelten:

```
Tő = Sample(seed, "NAME", featureId) → szótagválasztás
     + klimatikus/morfológiai hangulati készlet
```

A „hangulati készlet" a feature tulajdonságaiból jön: hideg helyek `Frost-`, `Rime-`, `Cold-` előtagot kapnak nagyobb valószínűséggel, vizes helyek `Silver-`, `Tide-`, `Marsh-` elemeket. Így lesz `Frosthold Tundra` és `Silvertide Delta` — a név információt hordoz, nem véletlen zaj.

### 6.4 Identitáskövetés deep-time-ban

**Ez a legnehezebb rész, és a spec egyáltalán nem érinti.**

Ha Aurelion 40 Myr múlva kettészakad, melyik fél marad Aurelion?

| Esemény | Szabály |
|---|---|
| Szétszakadás | A nagyobb területű örökli a nevet. A kisebb új nevet kap, de megjegyzi: `derivedFrom: Aurelion` |
| Egyesülés | A nagyobb neve győz. A kisebb neve régiónévként túlélhet |
| Elmerülés | A név archiválódik, `submergedAt: t` |
| Új sziget | Teljesen új név, `originEvent: <eventId>` |
| Fokozatos zsugorodás | Név marad, amíg a terület > az eredeti 20%-a |

Ez **eseménynaplót igényel a Features rétegben is** — nem elég a szegmentálást minden időpontban újrafuttatni, mert akkor a nevek ugrálnának. A feature-identitás így maga is event-sourced állapot.

> **ND-11 — nyitott:** a feature-identitás naplója checkpointolandó-e, vagy determinisztikusan újrajátszható 0-tól? Az újrajátszás tisztább, de egy 1 Gyr-es lekérdezésnél a teljes feature-történet lepörgetését jelenti. **Javaslat: checkpointolt, a többi layerrel együtt.**

---

## 7. Milestone-terv (átrendezve)

A kulcsváltozás: **a render nem a végén van**. Az M2-től kezdve minden fázisnak van vizuális kimenete, mert enélkül nem látszik, hogy jó-e.

| M | Név | Tartalom | Vizuális kimenet | Kész, ha |
|---|---|---|---|---|
| **M0** | Döntések | ND-01 stack, repo, CI | — | Stack eldöntve, CI zöld |
| **M1** | Determinisztikus alap | PRNG + tesztvektorok, Time, Units, SphericalMath | — | Tesztvektorok 3 platformon egyeznek |
| **M2** | Grid + első render | Cubed sphere, TileId, LOD, **nyers gömb-render** | Szürke gömb, tile-határokkal | Forgatható gömb, LOD működik |
| **M3** | Csillagászat + világítás | Csillagok, pálya, rotáció, insoláció | **Megvilágított gömb, terminátorral** | Évszakok láthatók a terminátor mozgásán |
| **M4** | Geológia + domborzat | Lemezek, kéreg, elevation, tengerszint | **Kontinensek, óceánok, hegyek árnyékolva** | `TEST-EARTH-001`: 50–75% víz, több kontinens |
| **M5** | Klíma | Hőmérséklet, szél, nedvesség, csapadék | **Biome-színek, hó, jégsapkák** | Éghajlati övek felismerhetők |
| **M6** | Atmoszféra-render | Rayleigh-szórás, felhőréteg, ciklonok | **A Planet nézet lényegében kész** | Kép 2 szintjén: 80%-os közelség |
| **M7** | Hidrológia | Folyók, tavak, hó, gleccser | Folyók láthatók a kontinensnézeten | Folyók hegyből tengerbe futnak |
| **M8** | Features + panelek | Szegmentálás, névadás, aggregált metrikák | **World/Continent/Region panelek élesben** | Minden panelmezőnek valós forrása van |
| **M9** | Continent + Region nézet | Magas LOD, displacement, kamera-átmenetek | **Kép 1, 3, 4 szintje** | Zoom-átmenet folyamatos |
| **M10** | Deep time | Lemezmozgás, erózió, eljegesedés, dinamikus tengerszint | **Az időcsúszka él** | TimeTravel + TimestepInvariance zöld |
| **M11** | Események | Becsapódás, vulkán, rift, split/merge | Kráterek, vulkánkitörések láthatók | Acceptance A–E zöld |
| **M12** | Perzisztencia + CLI | Checkpoint, .worldpkg, state hash | — | `worldgen verify` reprodukál |
| **M13** | Polish | Volumetrikus felhők, AO, víz-shader, színkalibráció | **Végleges látvány** | Vizuális acceptance §73 |

**Miért került a render ilyen korán:** ha M2-ben van egy forgatható gömb, akkor M4-ben azonnal látszik, ha a kontinensek „foltosak" vagy a hegyek nem követik a lemezhatárokat. A v0.1 sorrendjében ez csak M10-ben derült volna ki — 8 milestone-nyi rossz irányba fejlesztés után.

---

## 8. Nyitott döntések

| ID | Kérdés | Javaslatom | Mikor |
|---|---|---|---|
| **ND-01** | Technológiai stack | **Most erősebb az érv a Godot 4 mellett** — a render első osztályú lett, és a Godot kész sphere/shader/kamera infrastruktúrát ad. C# core + Godot viewer. | **M0** |
| **ND-02** | Szimuláció bázis-LOD | Fix level 6, LOD csak lekérdezésre/renderre | M2 |
| **ND-03** | Köztes idő interpolációja | Engedett, HUD-on jelölve | M10 |
| **ND-04** | Timestep-tolerancia | v0.1 táblázat kiindulásnak, M10 után revideálni | M10 |
| **ND-05** | Régiószegmentálás | Hibrid: vízgyűjtő + biome-klaszter + domborzati törés | M8 |
| **ND-06** | Névgenerálás | Szótag-templétek + hangulati készlet a feature tulajdonságaiból | M8 |
| **ND-07** | ~~Render scope~~ | **Lezárva: a render a projekt része, M2-től folyamatosan** | — |
| **ND-08** | Óceáni áramlatok | 1.0-ban nincs; a felszíni hőmérséklet-mezőt egyszerűsített gyre-modell adja M6-tól, mert a ciklonok kellenek hozzá | M6 |
| **ND-09** | Ordinális kvantálás referenciája | Előre kalibrált, ~1000 világból számolt, verziózott referencia-eloszlás | M8 |
| **ND-10** | Biome-küszöbök bolygóparaméterekkel skálázva? | Nem az 1.0-ban; fix Föld-küszöbök, de konfigban | M5 |
| **ND-11** | Feature-identitás perzisztálása | Checkpointolt, a többi layerrel | M8 |
| **ND-12** | Kettőscsillagnál kettős árnyék? | Igen, a renderer támogassa 2 fényforrást — a `TEST-BINARY-001` acceptance vizuálisan is ellenőrizhető lesz | M3 |

---

## 9. Kockázatok, amiket a képek hoztak be

1. **Elvárás-kockázat.** A koncepciórajz szebb, mint amit egy valós renderer első 6 hónapban ad. Ha M6-nál a Planet nézet „csak" jó, nem lélegzetelállító, az normális — a különbség a polish (M13), nem az architektúra.

2. **A biome-albedo visszacsatolás láthatóvá teszi a klímahibákat.** Ez valójában előny: ha a sivatagok rossz szélességen vannak, az azonnal látszik a képen. A vizuális réteg így *tesztté* válik. Érdemes ezt kihasználni: M5-től screenshot-alapú regressziós teszt (percepciós hash az ismert seedek renderjén).

3. **A panelmetrikák számítási költsége.** A `Coastal complexity` fraktáldimenzió és a `Biodiversity potential` heterogenitás-index globális pásztázást igényel. Ezeket **nem szabad frame-enként számolni** — checkpointonként egyszer, és interpolálni.

4. **A ciklonmodell scope-csúszása.** A 3.4 pont könnyen kifut egy fél-időjárás-szimulációvá. Kemény korlát: a ciklon egy *jelölt a felhőmezőn*, nem szimulált örvény. Ha valódi vorticitás-számítás kerül bele, az M6-ból M9 lesz.

---

## 10. Következő lépés

ND-01 továbbra is blokkoló, de a döntési helyzet változott: a render első osztályú kimenetté válásával a **Godot 4 + C#** kombináció mérlege érzékelhetően javult a nyers Rust ellen — a kész gömb-geometria, shader-pipeline, kamera- és UI-rendszer több hónapnyi munkát spórol, és a determinizmus a C# core-ban továbbra is megoldható.

Ha ezt jóváhagyod, az első kódszállítás **M1 + M2 együtt**: determinisztikus PRNG tesztvektorokkal, cubed-sphere grid, és egy forgatható, LOD-olt gömb a képernyőn. Ez már mutat valamit, és minden további rá épül.
