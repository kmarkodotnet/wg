# A7 — egységes overlay, hő–szél csatolás és checkpoint

**Dátum:** 2026-09-23. **Állapot: folyamatban, nem lezárt A7.**

A munka a `main` ág `e3713bf` (`generator version`) commitjára épült.
Commit/push nem készült. A munkakezdéskor már meglévő ND-136–140 és a
tektonikai hiányfelmérés módosításait megőriztem.

## Megvalósítás

**ND-141, overlay:** egyetlen `SurfaceOverlayMode` váltotta a négy külön
szerializált állapotot. Hat mód: kikapcsolt, felszínhő, léghő, szél,
csapadék, lemezek. A régi mezők `FormerlySerializedAs` átvezetést és egyszeri
migrációt kapnak, megőrzött láthatósági prioritással. Az Inspector és a
Rétegek panel ugyanazt az enumot írja. Hőnézetek között nincs vertexszín-
újraépítés; a többi nézet a meglévő színinvalidációt használja, teljes
világ-Build nélkül. Világváltás után elavult, feltöltésre váró hő-snapshot
sem publikálható.

**ND-142, hő–szél:** a tick eleji levegőanomália négy szomszédos cellából
számított tangenciális LS-gradiense a napi bázisgradienshez adódik.
A két cella gradiense az élközéppontban közösen hat a fluxusra. A teljes
termikus gradiens a meglévő sebességkorláton és Coriolis-forgatáson megy át.
Az anomália dimenziómentes `AirFeedbackStrength` alapértéke **0,1**:
ezt a teljes rácsos előfutási kontroll választotta ki. A bázisszél erősségét
nem csökkenti. A paraméter a checkpoint bemeneti azonosítójának része.
Ezután következik az advekció és a lokális hőcsere. A saját diagnosztika
is ezt a csatolt szélutat olvassa. Nincs iterációs megállási toleranciától
függő eredmény. A modell nyomási hatást közelít, külön hPa-mezőt nem számol.
A régi szél-overlay továbbra is a korábbi `WindPrecipitation` klímautat
mutatja; annak átállítása a fogyasztói integráció része.

A numerikus szerződés előbb Pythonban készült. A teljes level-6 referencia
újragenerálta a vektorokat, majd ezek kerültek a C# tesztadatok közé;
a verziózott vektorok nem kézi számok. Hőmodellverzió **3**, közös
generátorverzió **"2"**. Az A6 app-/CLI-betöltési kapu az előző `"1"`
generátor állapotát elutasítja; erre külön regressziós eset került.

**ND-143, állapot:** `ThermalSnapshot.ModelIdentity` azonosítja a teljes
fizikai bemenetet (seed, geológiai idő, pálya, rácsszint/sugár, tengerszint,
felszíntípusok, magasságok és paraméterek), valamint a modellverziókat.
A közvetlen léptetés idegen világ állapotát elutasítja; a `StateAt`
ilyenkor kanonikusan újraszámol. A `ThermalCheckpoint` explicit big-endian
formátumot, teljes double mezőt és SHA-256 ellenőrzést ad. Sérült,
csonka, nem véges, nem kanonikus vagy idegen állapot nem tölthető; a cél
hiba esetén változatlan. A külön termikus állapothash nem írja át a régi
elevációhash jelentését. Az alkalmazás teljes `simulation-state` mentése
továbbra is C3.

## Ellenőrzések és a megtalált numerikus korlát

- Végleges, kalibrált modell teljes Debug solution-futása: **1572/1572**,
  ebből Core **593**, viewer-LOD **503**, app **452**, CLI **24**.
  Core idő: **7 perc 55 másodperc**.
- A végleges kiegészítő A7 Core-csomag **26/26 Debug és 26/26 Release**,
  immár a teljes level-6 ≤0,18 K előfutási regresszióval, a csatolás
  paraméterhatásával és a független bináris checkpoint-KAT-tal.
- Debug és Release solution-build: 0 hiba, 0 warning.
- Random123 KAT **9/9**. Az 512 random vektor újragenerált és verziózott
  fájljának SHA-256-ja egyaránt
  `2CB4EF00BB52934DFE682AA0D810A4850744567EFC496839B1BB956F93C74990`.
- A végleges teljes hővektorfájl és a C# másolat SHA-256-ja egyaránt
  `20EBCE4CBBE6913D67479DA1FDA7A4617B2646DF0A8DADDB8ECF82A7B76D2746`.
  A független checkpoint-vektor és a C# másolat hash-e:
  `F8C89C47F96C246E13D04721C08C723F32A8A9E6A16EB18FA5659B83100214B3`.
- A csatolt szél minden cellára/élre egyezik a külön Python-orákulummal.
  Konstans anomália nem okoz szelet; nem konstans mező hatása kimérhető;
  az összesített sebesség ≤40 m/s. A napi cache múltja nem hat az eredményre.
- Level-3 referencia, 24 óra: a 225 s-os futáshoz képest a 900 s-os RMS
  hiba **0,00162101 K**, a 450 s-os **0,00039335 K**. A lépésfinomítás
  csökkenti a hibát. A referencia a kockalap-varratokat is ellenőrzi.
- A teljes level-6 kanonikus referencia 1060 tickje véges eredményt adott;
  a tesztvilág felszíne **−23,7…+47,1 °C**. A C# kanonikus, párhuzamos,
  paraméterhatás- és plauzibilitási tesztek is átmentek.
- **A kalibrációt kiváltó eltérés:** teljes erősségű csatolásnál a 10 és 20 napos előfutás különbsége a
  30 naposhoz képest level 3-on max. **0,1021 / 0,0561 K**, level 6-on
  viszont **1,6286 / 0,7708 K**. A korábbi ≤0,18 K egyirányú eredmény
  erre a kezdeti beállításra nem volt igazolt. A kisrácsos zöld teszt
  nem helyettesítette a teljes rács numerikus átvételét.
- A **kezdeti, teljes erősségű 30. napi bucket-határ** level-6 ellenőrzése: folyamatos
  40 napos futás és a kanonikus új 10 napos előfutás ugyanarra a tickre
  maximum **1,6899 K**, globális `Ts/Ta` RMS **0,11275 K** eltérést adott.
  A maximum szárazföldön, a 15525. cellában jelentkezett. Ezt az új
  hibát a kalibráció korrigálta; nem maradt alapértelmezett beállítás.

A teljes level-6 kontroll azonos tesztvilággal:

| Csatolási erősség | 10/30 nap max. eltérés | 20/30 nap max. eltérés |
|---|---:|---:|
| 0 (kontroll) | 0,14778 K | 0,05288 K |
| **0,1 (végleges)** | **0,17317 K** | **0,06465 K** |
| 0,25 | 0,22115 K | 0,08528 K |
| 1 (kezdeti kísérlet) | 1,62860 K | 0,77083 K |

A ≤0,18 K előfutási kaput megtartottuk; a megfelelő alapérték a mért
0,1 lett. Ez játékmodell-kalibráció, nem univerzális fizikai állandó.
A visszacsatolás integrált hőmérsékletre gyakorolt hatása külön tesztben
nem nulla. A **végleges havi határ** maximuma **0,19788 K**, globális
RMS-e **0,04880 K**; a csatolás nélküli kontroll **0,21948 / 0,04961 K**.
A kis kanonikus újraindítási maradékeltérés tehát a kontrollban is
megvan, és a kalibrált csatolás ezt nem rontotta. A 0,18 K mérési kapu
nem minden évszakra/bucketre bizonyított általános maximum.

Az új CI-lépés futtatja a csatolás referencia-ellenőrzését és a checkpoint
független `struct/hashlib` KAT-ját. A random vektorok összevetése továbbra
is byte-egzakt; a termikus szélvektorok ND-24 szerinti konstrukciós
tan-warpja miatt az összevetés 1e-9 toleranciájú. A checkpoint-KAT sík,
konstans bemenetből készül, nem függ a natív tan-warp sütésétől, ezért
annak fájlja byte-egzakt. A CI platformmátrix `fail-fast: false` maradt.
A távoli platformmátrixot ebben a körben nem futtattam.

## Élő Unity

Az Editor lefordította a módosításokat, Console-hiba nélkül. A migrációt
izolált preview scene-ben, valódi régi prefab-YAML mezőkkel ellenőriztem:
**11/11** migrációs/Inspector/invalidációs próba sikeres. A JSON-próba
nem kezelte a régi mezőneveket; ezután a tényleges asset-szerializálási
utat vizsgáltam. Az ideiglenes prefab eltávolítva, a scene nincs átmentve.

Saját Editor Play-próba készült, majd a végleges 0,1-es csatolással
megismételtem. A kész level-6 mező ekkor a **19306. ticken
(201. nap 02:30)** állt; mind a hat overlay-mód shaderállapota helyes.
A léghő-overlayről készült és ellenőrzött Game-view kép. A saját Playt
leállítottam, standalone Player nem indult. Ez rövid működési próba,
nem több seedes/évszakos vizuális és teljesítmény-átvétel.

Lokális mérési mellékletek az ignorált `artifacts/a7/` alatt:
`OverlayProbe.cs`, `PlayProbe.cs`, `SpinUpProbe.cs`,
`air-overlay-calibrated.png`, `spinup-level6.txt`, `spinup-controls.txt`,
`bucket-boundary.txt`, `bucket-boundary-calibrated.txt`, `bucket-boundary-control.txt`.

A hordozható mérőeszköz verziózott forrása:
`tools/diagnostics/ThermalStabilityProbe.cs`. Élő Editorban
`unity command run_script --file <repo>/tools/diagnostics/ThermalStabilityProbe.cs --entry ThermalStabilityProbe.Start --project-path <repo>/unity/WorldGenViewer`
indítja a négy level-6 kontrollt; a kimenet `artifacts/a7/thermal-stability-level6.csv`.
A `StartSmall` belépési pont rövid level-3 próbája mind a négy sort sikeresen
előállította. Futás közben domain reloadot ne indítsunk, mert a háttérfeladat
az Editor folyamatához tartozik.

## ND-144 folytatás: napi Core-statisztika

A `ThermalDailyStatisticsCalculator` a kanonikus állapotból induló teljes
modellnap mind a 96 tickjének elején mintáz. Cellánként `Ts/Ta` átlagot,
minimumot és maximumot ad; a minták fix ticksorrendben adódnak össze.
Az eredmény az ND-143 modellazonosítót és a napindexet hordozza. A solver
állapota a következő nap kezdetére lép, így egymást követő napoknál
folytatható, más sorrendben pedig kanonikusan újraszámolható. A Core API
nem függ a renderertől; a régi fogyasztói láncot még nem módosítja.

A célzott teszt a 96 mintát kézi Core-végigléptetéssel veti össze, és
ellenőrzi a lekérdezési sorrendtől független bitazonosságot, a
modellazonosítót és a túlcsordulási határt. Az éves ablak és annak teljes
level-6 költsége továbbra is mérendő; a napi API nem teszi automatikusan
használhatóvá az éves jégklasszifikációt.

ND-144 ellenőrzés: célzott tesztek **3/3 Debug és Release**; teljes Core
**596/596**; önálló Viewer-LOD **503/503**, app **452/452**, CLI **24/24**.
Release solution-build 0 hiba és 0 warning. A Viewer offline fordítás
0 hibával, 122 meglévő nullability warninggal végződött. A párhuzamos
solution-tesztben ugyanaz a Viewer allokációs eset 7984 bájt miatt egyszer
elbukott, az önálló újrafuttatás zöld. A Python KAT ezen a gépen nem
futtatható (`py -3`: nincs telepített Python). Élő Unity-próba az ND-144
Core-bővítéséhez nem készült; az A7 korábbi overlay-próbája fent szerepel.

## Ami az A7-ből még hátravan

1. Szélesebb seed-/évszak-/paraméter-elfogadás. A kalibráció konkrét
   teljes rácsos kontrollon teljesült, nem minden lehetséges világra
   bizonyított hibatétel; a kanonikus újraindítás kis maradéka ismert.
2. A fizikai bemenetek renderfüggetlen, immutábilis előállítása. Jelenleg
   a viewer a Build után létrejött tó/jég-mezőből adja a solver maszkjait.
3. Éves `Ts/Ta` klímastatisztika, megfelelő időátlaggal és
   teljesítményméréssel. A napi adatút kész, de a pillanatnyi és a napi
   mező sem éves jégkritérium.
4. Fogyasztónként az autoritatív átállás: párolgás, biome, jég és a
   hozzájuk tartozó megjelenített klímaadatok. A jelenlegi
   `Temperature`-út még aktív. A jégmaszk visszacsatolási körét is rendezni
   kell; ezt nem helyettesíti egy hőoverlay vagy checkpoint.

**Korábbi súlyozott A7-becslés az ND-144 után: kb. 62% (visszavonva).** Súlyok: overlay 15%, coupling 35%,
fogyasztói átállás 50%. Az első kész, a második implementált és kalibrált,
a harmadiknál részben kész infrastruktúra van. Durva ráfordítás
erre a körre **1–2 munkaóra**, hátralévő **12–24 munkaóra**; nem vállalás.
A teljes M5/M12 százalékát ebből nem következtetjük ki.

Az ND-144 folytatás után a durva további ráfordításbecslés **1–2 óra**,
a maradék **11–23 óra**; az éves futtatás méréséig ez különösen bizonytalan.

**2026-09-23-i becslés-korrekció:** a 62% a checkpointot és a napi
adatutat a fogyasztói fázis részleges teljesüléseként számolta el, noha
egyetlen fogyasztó sem állt át. Funkcionális súlyozással az overlay és a
csatolás 15% + 35%, azaz **kb. 50%**; ez is durva projektbecslés. A
11–23 óra nem tételes, mért munkaidőbecslés, és a felső határa nem
védhető az éves level-6 futás teljesítménymérése nélkül. A következő
döntési kapu durván **1–3 munkaóra**: éves futtatási költségmérés,
jégmaszk-körfüggés és fogyasztói adatút megtervezése. A teljes maradékot
ezután kell újrabecsülni.
