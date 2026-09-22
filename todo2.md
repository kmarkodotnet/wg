# TODO2 — aktuális, ellenőrzött feladatlista

**Készült: 2026-09-21**, az ND-127 lezárása után. Ez a lap váltja a `todo.md`-t
és a `docs/backlog.md` feladat-tábláját mint *élő* lista. Az a két fájl
**történeti napló marad** (a hosszú diagnózisok, mérési sorozatok és a
lezárt tételek indoklása ott van) — de „mi van hátra" kérdésre innentől **ez**
a válasz.

**Hogyan készült.** Nem átmásoltam a két régi listát: minden tételt
visszaellenőriztem a kódban, a `docs/04-decisions.md` ND-állapotában, a
tesztekben és a mért naplókban. Ahol a régi lista mást állított, mint a kód,
ott a KÓD az igazság, és külön jelzem. Ellenőrzött források: `todo.md`,
`docs/backlog.md` (47 táblasor + 3 kidolgozott terv), `docs/04-decisions.md`
(ND-01…ND-127), `docs/app_base_features/WorldGen_Desktop_Release_Roadmap_revised.md`,
`docs/11-play-check.md`, `history/` naplók, `src/`, `unity/…/Assets/Scripts/`.

> **Elavult forrás, ne használd:** `docs/06-user-verification-checklist.md`
> (2026-09-07-es állapot, 1545 sor). Amit még élő belőle, az itt szerepel.

**Jelölés.** 🔴 kritikus/helyességi · 🟠 fontos · 🟡 ráér · 🔒 blokkolt (más
modul vagy döntés hiányzik) · 👁 élő Unity Play kell hozzá.

---

## A. Nyitott — ezeket önállóan el tudom végezni

| # | Feladat | Mi a tényleges állapot (ellenőrizve) | Hivatkozás |
|---|---|---|---|
| A1 ✅ | **~~`useGpuGeometry` eleváció-eltérése~~** | **KÉSZ (2026-09-21, ND-128): az út törölve.** A `TileClassification.compute` a Core-eleváció ÚJRAÍRT (HLSL) mása volt, az ND-52 és ND-90 előtti állapotban — mérve a tile-ok ~22%-a került volna a tengerszint másik oldalára. A törlés mellett döntött az is, hogy a GPU-ág élő mérésben LASSABB volt (~50 s/újraépítés, sarok-dedup nélkül), geomorphing nélkül futott, és öt másik utat (aszinkron újraépítés, szakaszolt upload, víz-finomítás, terep-LOD-proxy, ND-75 diagnosztika) `!useGpuGeometry` feltétellel béklyózott. Ráadásul a `RebuildAdaptiveMesh` CPU-ága ELÉRHETETLEN (halott) kód volt. −398 sor a viewerben, a `Gpu/` mappa megszűnt. | ND-128, ND-120 |
| A2 ✅ | **~~Hidrológia tile-középpont terrain-bázisa (hideg Build)~~** | **KÉSZ (2026-09-22, ND-131).** A tile-középpont bázis is a validált LEMEZ-gyorsítótárba került. A formátum N TÖMBÖSSÉ általánosítva (`ArrayCount` + `Kind` a kulcsban), mert ennek a bázisnak EGY tömbje van a sarok-bázis HÁROM-ja helyett — így 18,0 MiB a fájl 54,4 helyett, és az ND-122 három biztosítéka (kulcs, ellenőrzőösszeg, 1024 bejegyzés újraszámolása) egy helyen marad. MÉRVE: `hydrology(...) terrainBasis=` **2 295 ms → 98–104 ms** (~22×), a teljes hidrológia-fázis 2 625 → 417 ms; a `lakes=11505` bitre azonos a számoló és a lemezről töltő ágon. A formátum-váltás miatt árván maradó régi fájlokat munkamenetenként egyszeri takarítás törli (a BETÖLTÉS útjáról is — mérve: csak a mentés útjáról nem futott le). Tesztek: 7 → 15, 490/490 zöld. | ND-131, ND-122, ND-64 |
| A3 ✅ | **~~Klíma-konstansok hangolása (ND-126b)~~** | **KÉSZ (2026-09-22).** A hőmérsékletutak `40 K * sin⁴(szélesség)` meridionális hőszállítás-proxyt kaptak; a termikus szél iránytartó `30 m/s * tanh(|v| / 30 m/s)` korlátot. Kanonikus mérés: hideg szárazföld **40,71% → 16,91%**, 70–90° szárazföldi átlag **−62,21 → −26,31 °C**, egyenlítő **27,58 °C**; szélmaximum **36,79 m/s**, sarki átlag **34,66 m/s**. Python-orákulum és 9 érintett vektorfájl újragenerálva; a csapadék-percentilisek változatlanok, vizuális ítéletük továbbra is B4. | ND-126b, ND-41, ND-118 |
| A4 🟠 | **Deep-time újraépítés: cél <1 s** | Jelenleg ~2,8 s meleg (22,57 s baseline-ról, 8,0×), hideg teljes Build 5271 ms. A maradék, mért tételek: statikus base-layer emit (ND-123 után párhuzamos, de a **bucketen BELÜLI, prefix-offsetes darabolás** még nincs meg — ez a `PlanetGridMesh.StaticEmit.cs` fejlécében kimondott következő lépés), klasszifikáció 418–669 ms, terrain mesh 97–309 ms. Ideiglenesen elfogadtad; a cél megmarad. | backlog M9/M10, ND-123 |
| A5 🟠 | **Deep-time variancia és allokációprofil** | Öt meleg Build 2,607–3,185 s között szórt (bucket-előkészítés 13–243 ms, vízfeltöltés 26–284 ms). Unity Profiler/GC-mérés kell, hogy a valódi munka és a GC/JIT/Editor-zaj szétváljon — ez az A4 előfeltétele, nem utólagos ellenőrzése. 👁 (Profiler-menet) | backlog M9/M10 |
| A6 🟠 | **ND-108 — generátorverzió a mentéshez** | Nyitott (a döntés fejléce is annak jelöli). Két dolgot blokkol: a mentés-kompatibilitási kaput (`SaveCompatibility`, app-réteg WF-SAVE-005) ÉS minden seed-törő változást (lemez-méreteloszlás, `PlateId`→`ulong`): épp ilyenkor kellene működnie. | ND-108 |
| A7 🟡 | **Hőmodell: 6. és 7. fázis + egységes overlay-enum** | A Core (ND-100–103) és az overlay (ND-104) **elkészült** (`SurfaceTemperatureField`, `ThermalBaseline`, `ThermalWind`, `SimulationTime`, `PlanetGridMesh.ThermalOverlay.cs`). Ami NINCS meg: (a) a 6. fázis — a `Ts/Ta` ma **diagnosztikai**, a biome/jég/párolgás továbbra is a napi átlagú `Temperature`-t használja; (b) a 7. fázis (kétirányú csatolás); (c) az ND-104 egységes `SurfaceOverlayMode` enumja — ellenőrizve: ma is külön `windSpeedOverlay`/`precipitationOverlay`/`tectonicPlateOverlay` bool + külön `thermalOverlayMode` van. | ND-100…104 |
| A8 🟡 | **Kör-alapú (round-major) folyó-forrás sorrend** | Opcionális: a 16×6 = 96 forrás ~30 s háttérszálon (a `Build()`-et nem blokkolja). A medencék ELSŐ forrásai előre futnának, a későbbi körök a már lefoglalt főágnál korán megállnának — bitre azonos kimenet. Csak akkor érdemes, ha a várakozás zavar (→ B3). | ND-124 |
| A9 ✅ | **~~Tó-blokkosság~~** | **KÉSZ (2026-09-22, ND-129).** MÉRT gyökérok: a vízfelszín a level-8 (~36 km) tó-tile-ok unióját rajzolta, és a tile-határon VÉGET ÉRT — a partvonal 90%-a nyers tile-él volt (csak a tó-sarkok 10,3%-ánál metszette a terep). Javítás: a vízfelszín 1 gyűrűvel TÚLNYÚLIK a valódi parton, és a már renderelt, adaptív terep vágja ki belőle a partot — **nulla új eleváció-kiértékelés**, a részletesség a terep-LOD-dal (level 20-ig) zoomra magától finomodik. Plusz N=4 al-osztás a part menti tile-okon a 25 m-es húr-behúrás ellen. Mérve: 18 307 rajzolt tile, 240 202 quad, 477 ms. | ND-129, ND-49 |
| A17 ✅ | **~~Biome-blokkosság: „nagy négyszög alakú régiók"~~** | **KÉSZ (2026-09-22, ND-130).** Felhasználói visszajelzés + kép alapján felvéve. MÉRT gyökérok: az ND-126 óta a biome-osztályozás bemenete a csapadék, az viszont a `level`=5 REFERENCIA-rács **diszkrét, ~288 km-es tile-értéke** volt (a hőmérséklet ezzel szemben pontszerű, folytonos) — egy küszöb-osztályozó bemeneteként ez a biome-határt pontosan a tile-élekre teszi. Javítás: a mező BILINEÁRIS interpolációja a tile-sarkok között (ugyanaz a minta, amit a felhő-réteg már használ), plusz a vágópontok és a csapadék-overlay ugyanerre a függvényre állítva. Core-kiegészítés: `TileGeometry.ToFaceUV` (a `FromPosition` első fele kiemelve) + tesztek. Mérve: a mintapontok **21,0%-a** vált csapadék-sávot. | ND-130, ND-126 |
| A18 🟠 | **Óceán-partvonal blokkosság (MEGFIGYELÉS, nem diagnosztizált)** | Az ND-129/ND-130 vizuális ellenőrzésénél a képeken a SZÁRAZFÖLD–ÓCEÁN határ továbbra is lépcsős, ~level-8 (36 km) léptékben. Gyanú: ugyanaz a hibaosztály, amit a tavaknál az ND-129 megoldott — a `WaterLodSource.ContainsWater` a `BaseLevel`-ig (8) sétál vissza, tehát az óceán vízfelszíne level-8 tile-halmaz uniója, míg a terep színe (`ComputeTileClassification`: `isOceanic = elevation < seaLevel`) PONTSZERŰ, folytonos. **Ez még nincs végigmérve** — a tényleges gyökérok-diagnózis hátravan (melyik réteg éle látszik: a vízfelszín pereme vagy a terep-szín váltása). | ND-129, ND-82/ND-83 |
| A10 🟡 | **Régió-szegmentálás: az ND-05 hibrid maradék két tagja** | Az ND-127 a vízgyűjtő-tagot hozta rendbe (100% lefedettség, 0 szétesett régió). A biome-klaszter és a domborzati törés továbbra is hiányzik — ugyanazon a cella-gráfon más összevonási költségfüggvényként jönne be. Csak a B5 vizuális ítélet után érdemes. | ND-127, ND-05 |
| A11 🟡 | **Kameramód: pálya menti (éves) követés** | Ellenőrizve: a `CameraViewMode.OrbitalFollow` **létezik, de nincs implementálva** — a tooltip maga mondja ki („egyelőre Szabad kamera-ként viselkedik"). A tengelyforgásos mód (`AxialRotation`) kész. Nyitott tervezési kérdések: a kameracélpont befagyasztása, fusson-e közben a napi ciklus, és a nem valós lépték miatti kalibráció (ND-19). | ND-62, backlog M3/M9 |
| A12 🟡 | **ND-19 — floating origin** | Nem létezik implementáció; M9-re halasztva. A közeli zoomnál és a valós léptékű koordinátáknál fog számítani (az A11 is ebbe fut bele). | ND-19 |
| A13 🟡 | **ND-20 — Burst `FloatMode.Strict` CI-kikényszerítés** | **Állapot-pontosítás:** ellenőrizve, hogy a repóban **egyetlen `[BurstCompile]` sincs**, és a CI-ben sincs rá kapu. A tétel tehát ma tárgytalan — de nyitva kell maradnia: az első Burst-használat pillanatában kötelezővé válik, különben az I1 csendben sérül. Javaslat: a CI-kapu megírása ELŐRE (üres halmazon is fut), nem a használat után. | ND-20, CLAUDE.md |
| A14 🟡 | **ND-21 — HDRP volumetrikus felhő űrből** | Ellenőrizve: mindhárom HDRP-asset `supportVolumetricClouds: 0`; a jelenlegi felhő a saját, mesh-alapú MVP-réteg. A prototípus-ellenőrzés (működik-e a HDRP volumetrikus felhő bolygó-léptékben, űrből) nem történt meg. | ND-21 |
| A15 🟡 | **M13 Fázis 2–4** | Nincs elkezdve: GPU-vezérelt geometria, procedurális mikro-részlet textúra. Az 1. fázis (folytonos árnyalás) kész és megerősített. | backlog M13 |
| A16 🟡 | **M13 — volumetrikus felhő + AO, színkalibráció** | Nincs elkezdve. A színkalibráció (spec §73) csak a többi látvány-tétel után értelmes. | backlog M13 |

---

## B. Nyitott — te kellesz hozzá (élő Unity Play / döntés)

| # | Feladat | Mit nézz / mit döntesz | Miért nem tudom én |
|---|---|---|---|
| B1 👁 | **Lemez-overlay újranézése** | Play → tektonikus overlay. Az ND-125 óta ELŐSZÖR a valódi, szabálytalan lemezhatárokat látod (eddig az overlay a warpolatlan Voronoit rajzolta, a tile-ok 19,5–26,4%-án más felosztást). Még mindig „négyszög/háromszög"? | A B2 (méret-eloszlás) döntés előfeltétele. |
| B2 👁🔴 | **Lemez MÉRET-eloszlás: kell-e beavatkozás?** | A mérés szerint a mi eloszlásunk **egyenletesebb**, mint a Földé (a legnagyobb lemezünk 8,7–12,1%, a Csendes-óceáni 20,4%). Kell-e szabálytalanabb? | **Seed-törő**: verzió-emelés + ND-09 ordinális kalibráció újrafuttatása + minden meglévő világ domborzata változik. Ezt nem hozom meg egyedül. Előfeltétele az A6 (ND-108). |
| B3 👁 | **Folyó- és tó-hálózat vizuális elfogadása** | Play → zoom egy nagy kontinens folyóhálózatára. A finomított vonalak háttérszálon készülnek (~30 s, 96 forrás). Fának látszik-e? Elég sűrű-e? A tavak partvonala az ND-129 után rendben van-e (nem tolakodik-e be a víz a lefolyó-völgyekbe)? | A metrikák javultak (összefolyás 8 → 40, max vízhozam-súly 2 → 6, „súly ≥ 3" 0 → 13), de hogy a képernyőn fa-e, azt nem én ítélem meg. Válasz → A8; az ND-129 mért maradéka 129 gyűrű-tile (1,4%), ami még nyers tile-él. |
| B4 👁 | **Új biome-térkép megítélése** | Play → felszín. Öt szárazföldi biome (sivatag/sztyeppe/szavanna/mérsékelt erdő/esőerdő). (a) Jó-e az arányuk, (b) sok-e a jég, (c) jók-e a színek? | A 20/45/75 percentilis konstrukció szerint 20/25/30/25 arányt ad — hogy ez jó-e, arra nincs numerikus kritérium. A jégtúlsúly külön, MÉRT ok → A3. |
| B5 👁 | **Régió-lista megítélése (ND-127)** | Play → navigációs menü → nagy kontinens. Most ~10 régió van a korábbi 65 helyett, mindegyik összefüggő, a szárazföld 100%-a benne van. (a) Jó-e a felbontás (`RegionTargetLandSharePercent = 3`, egy szám), (b) illenek-e a nevek/alaktípusok? | A darabolás finomsága ízlés. A lefedettséget/összefüggőséget én mérem. Válasz → A10. |
| B6 👁 | **Navigációs menü teljes élő elfogadása** | Mind a négy szint (bolygó → kontinens → régió → terület): kattintás, „Vissza", breadcrumb-ugrás, hosszú lista görgetése, panel-átfedés keskeny nézetnél, stílus-egyezés a Deep time dobozzal. | A menü kódból kész (`NavigationLevel`, `AreaPanelData`), de **élő Play-ellenőrzés soha nem futott rajta**. Kódból nem nyilvánítható késznek. |
| B7 👁 | **Kilométer-léptékcsík (ND-84) élő validációja** | Több zoom/FOV/felbontás/képarány, pólus, lapél, part; ég/horizont esetén érvénytelen állapotot kell mutatnia, nem hamis számot. | Implementálva (`PlanetOrbitCamera.ScaleBar.cs`, `ScaleBarMath.cs`), de élőben nem igazolt; a hibakeret csak méréssel rögzíthető. |
| B8 👁 | **Látható hegységek, természetes partok** | Ugyanazon a helyen, több zoomszinten és megvilágításnál: felismerhetők-e a hegyvonulatok és völgyek, és a part alacsony/természetes marad-e? | Az ND-88 (1:1 relief) + ND-90 (folytonos kéregátmenet, 1 km uplift-plafon) kódban kész, élőben nincs igazolva. Ha lapos marad, az M13-árnyalás-kérdés; ha falszerű, Core-hangolás. Egyetlen globális szorzó nem oldja meg mindkettőt. |
| B9 👁 | **Hőoverlay élő elfogadása** | Play → hőoverlay: halad-e a napi ciklus, a maximum a helyi dél UTÁN van-e, kisebb-e az óceán napi amplitúdója, mozog-e a szélirányba a meleg anomália; overlay-váltáskor nincs-e akadás. | A „Kész-definíció" kimondja: élő képernyőkép, napszak-/szélteszt és friss PerfLog nélkül a tétel nem jelölhető késznek. |
| B10 👁 | **Atmoszféra- és felhő-MVP megítélése** | Play → a bolygó pereme (Rayleigh-közelítés) és a felhőréteg mozgása. | Három kalibrációs kör futott, **mindegyik csak számítással** ellenőrizve. Ez nem a teljes HDRP-modell, tudatos MVP. |
| B11 👁 | **Panel-mezők vizuális ellenőrzése** | Habitability, Coastal complexity, **Soil fertility (ma v2 küszöbökkel)**, morfológiai alaktípus-nevek („… Range", „… Basin"). | A számok forrása igazolt (I4), de a panel-sávok („Kiemelkedő"/„Alacsony") értelmessége csak élőben ítélhető meg. |
| B12 👁 | **Tavak, jég, erózió + dinamikus tengerszint** | Play → tavak (kék) és állandó jég (fehér); majd a deep-time csúszkával: mozdul-e a tengerszint (ND-38, térfogat-megmaradás). | A Core-oldal tesztelt, de a tengerszint-mechanizmus **élőben, időcsúszkával soha nem futott**. |
| B13 👁 | **Pólusi jég partvonala, éjszakai oldal sötétsége** | ND-57/58 (tengeri jég folytonos kategória, zajos partvonal) és a `surfaceAmbient = 0.04` éjszakai érték. | Mindkettő numerikusan kész, csak szemmel dönthető el (túl sötét / túl világos, elég szabálytalan-e a jégperem). |
| B14 | **Konstans-megerősítések** | (a) Eljegesedési ciklus: `GLACIATION_PERIOD_MYR = 150`, `AMPLITUDE_K = 6` — illusztratív értékek (ND-44 nyitott pontja). (b) Üvegházhatás-konstansok a teljes hőmodellben (ND-42). | Ezek modellezési ízlés-döntések, nem levezethetők — a spec sem rögzíti őket. |
| B15 | **Kiadási identitás és UI-technológia** | ND-111: végleges név, cég, ikon, splash. ND-110 döntés megvan (UI Toolkit), de a nézetek megépítésének sorrendje a te prioritásod. Plusz: Inno Setup telepítése a telepítő fordításához, és a `tools/release/THIRD-PARTY-NOTICES.md` jogi átnézése (Random123-attribúció). | Névadás/jogi/telepítői döntés. |

---

## C. Blokkolt — nem ütemezhető, amíg a feltétel hiányzik

| # | Feladat | Mi hiányzik alóla |
|---|---|---|
| C1 🔒 | **Talaj: maradék 7 regolit-mező** (MineralDiversity, Phosphorus/Nitrogen, Iron, Sulfur, Salinity, pHProxy) | Ellenőrizve: a `RegolithProfile` ma 3 mezős (Depth, Porosity, WaterRetention). A többihez **kőzettípus-/litológia-modul** ÉS **perzisztált vulkáni hamu-/tefra-mező** kellene — egyik sem létezik. Az I4 szerint kitalálni tilos. |
| C2 🔒 | **A többi ordinális panel-mező** (Climate variability, Tectonic activity stb.) | Nincs alattuk folytonos metrika, amit kvantálni lehetne. Előbb a metrika, utána az ND-09 mintájú kalibráció. |
| C3 🔒 | **Mentés `simulation-state` szekciója** | ND-108 (generátorverzió, → A6) és annak eldöntése, mi a szimulációs állapot egyáltalán (a Core ma tiszta függvény a paraméterekből — lehet, hogy nincs is mit menteni). |
| C4 🔒 | **Hangrendszer** | A modell és a lejátszók készek (`MusicDirector`, `UiSoundPlayer`, AudioMixer-applier), de **maguk a hangfájlok és a licencük hiányoznak**, és az AudioMixer asset sincs meg. |
| C5 🔒 | **M11 rift + lemez-hasadás/egyesülés rendszer-integráció** | A Core kész (`PlateLifecycle`, idő-lekérdezhető topológia, bit-egzakt vektorok), de a globális `PlateId` → `ulong` váltás és a split/merge bekötése a fő elevation-láncba **seed-törő** → előbb A6 (ND-108) és B2-szintű döntés. |
| C6 🔒 | **Extrém landmass méret-eloszlás strukturális javítása** | A diagnózis lezárva: a sima part-átmenet és a kiegyensúlyozott kontinensméret a jelenlegi **bináris** lemez-kéreg modellben ütköző cél. Csak egy lemezen belüli kevert-kéreg modell oldaná fel — nagy munka, seed-törő. |

---

## D. Alkalmazásréteg (asztali kiadás) — külön sáv

Forrás: `docs/app_base_features/WorldGen_Desktop_Release_Roadmap_revised.md`,
`docs/09-app-shell-architecture.md` §15. Jelölés: **F** = motorfüggetlen C#
(dotnet-tel tesztelt), **U** = Unity-kötés, **C** = Core-kötés.

**Ahol tartunk:** az F réteg gyakorlatilag kész (`WorldGen.App.Foundation`,
438 zöld teszt), az U rétegből megvannak az *osztályok*
(`AppBootstrap`, `SceneFlow`, `SettingsAppliers`, `UnityLogBridge`,
`UnityScreenshotService`, `KeyBindingInput`, `AudioPlayers`), a C réteg
blokkolt.

| # | Nyitott tétel | Állapot |
|---|---|---|
| D1 🟠 | **Scene-szerkezet** | Ellenőrizve: az `Assets` alatt **egyetlen scene van** (`PlanetView.unity`). A Bootstrap / MainMenu / WorldSimulation scene-ek nem léteznek, tehát az `AppBootstrap` nincs bekötve. Ez az egész app-sáv első lépése. |
| D2 🟠 | **Menü-, Settings-, Load World-nézetek** | A nézetmodellek készek (`SettingsScreenModel`, `SaveSlotRows`, `MenuModel`); maguk a nézetek az ND-110 (UI Toolkit) szerint megépítendők. Ide tartozik a kamera-input tiltása modális UI alatt. |
| D3 🟠 | **Loading / Pause bekötése a valódi Buildhez** | A súlyozott szakaszok és a megszakítás F-ben kész; a valódi szakaszok a `PlanetGridMesh.Build`-ből jönnének, és kellene hozzá háttérszálas build + megszakítási pontok. 🔒 részben Core-függő. |
| D4 🟠 | **Save/Load C-oldal** | → C3 (ND-108). A fejléc, CRC32-szekciók, rotáció, autosave, thumbnail F-ben kész. |
| D5 🟡 | **Planet Profile / Help szövegek** | A `WorldGenPanelData` → `PlanetProfile` leképezés hiányzik; a Deep Time help-szöveg felhasználói átnézést kér. |
| D6 🟡 | **Debug overlay (F3) tartalma** | A keret kész, a meglévő PerfLog/LOD-diagnosztika átvezetése hiányzik. |
| D7 🟡 | **Build és telepítő** | A build-script és a portable ZIP-script kész (kamu buildön kipróbálva), **élő Unity-build még nem futott**; az Inno-script kész, de **Inno Setup nincs telepítve** → B15. |
| D8 🟡 | **Release-QA** | `docs/app_base_features/release-qa-checklist.md` kész; az A/B/F szakasz futtatható ma, a többi a D1–D4 után. |

---

## E. Lezárt tételek — ellenőrizve (hogy semmi ne vesszen el)

Ezek a `todo.md`-ből és a backlogból **kikerülnek**; a részletes indoklás a
`docs/04-decisions.md`-ben és a `history/` naplókban marad.

**2026-09-21 — reggel, az előző napi munka élő igazolása:** ND-119 szél-overlay
(a látott szél ≠ a szimulált szél) · ND-120 GPU-osztályozó út törlése · ND-121
`MaximumRenderBudget` 48 000 → **96 000** (`starvedBudget` 6943 → 0, a
tile-méret-panasz mindkét ága lezárva) · ND-123 párhuzamos statikus emit
ön-ellenőrzése EGYEZÉST adott (a kapcsoló kikapcsolva).

**2026-09-21 — a mai fejlesztés:** ND-128 a `useGpuGeometry` út törlése (az
ND-120 másik fele; a GPU-oldali, újraírt világmodell-matek megszűnt) · ND-124 vízgyűjtő-alapú folyó-forrás (fa-alak:
összefolyás 17% → 42%, max vízhozam-súly 2 → 6) · ND-125 1. rész: a
lemez-overlay warpolatlan pozícióval rajzolt (javítva) · ND-126 kétdimenziós
biome-osztályozás (az Egyenlítő sávjában 1 → 4 biome) · **ND-127 régió-összevonás**
(lefedettség 50,1% → 100%, szétesett régió 35,7% → 0, menüpont 65 → 10) ·
`SoilFertilityThresholds` v2 újrakalibrálva (500 világ, 23 492 minta).

**2026-09-20:** ND-122 terrain-bázis LEMEZ-gyorsítótár újraszámolásos
validációval (7,4–8,5 s → ~0,2 s) · ND-50 overlay-only újraépítés (overlay ≠
teljes Build) · sűrű tó-pipeline (180–190 → 6,2–9,5 ms) · csapadék-cache
(322–410 → 0,0 ms).

**2026-09-19:** ND-115 horizont-vágás a vetített terep-úton · ND-116 a 2:1
kiegyensúlyozás korai kilépése blokkolt budgetnél · budget-újrahangolás a
vágás után.

**Korábban, felhasználó által megerősítve:** északi sarki jég-artefakt ·
vertex-szín árnyalás-regresszió · folyó/jég spekuláris „villámlás" (ND-53,
Bloom=0) · kontinens/régió kamera-átmenetek · csillagos háttér · kráter tartós
hatása · `.worldpkg` + `worldgen hash`/`verify` CLI (M12) · Continent/Island
szétválasztás · eltűnő Region/Area kis landmasson (az ND-127 óta strukturálisan
sem fordulhat elő) · tektonikus lemez-overlay · hierarchikus navigációs menü
kódja · M1–M5, M7, M10 alap, M8 numerikus rész.

**Visszavont / megszűnt:** ND-56 harmadik zaj-réteg (felhasználói döntés:
„működjön minden úgy, ahogy ezelőtt") · ND-71 mintavételezett LOD-metrika
(regresszió) · kattintható panel-nevek (a navigációs menü váltotta ki,
2026-09-13).

---

## F. Sorrend-javaslat

1. ~~**A1** (`useGpuGeometry`)~~ — **kész** (ND-128, 2026-09-21): az út
   törölve, a helyességi kockázat megszűnt.
2. **A3** (jég-túlsúly + sarki szél lefogása) — MÉRT hibák, a te ítéleted
   nélkül is javíthatók, és a B4 megítélését is megkönnyítik. Érdemes
   egyszerre a csapadék-percentilisekkel, különben kétszer kalibrálunk.
3. **A2** (hidrológia-bázis lemez-cache) — a hideg Build legnagyobb egyedi
   maradék tétele, kész mintát másol (ND-122).
4. **A6** (ND-108) — nem sürgős önmagában, de **három** dolog kapuja: B2,
   C3/D4 és C5. Minden seed-törő változás ELŐTT kell.
5. Közben, amikor van Play-menetd: **B1 → B2**, **B3**, **B4**, **B5**, **B6**.
   Ez az öt visszajelzés dönti el az A8, A9, A10 sorsát is.

---

## G. Mi került át honnan

- `todo.md` 1. tábla 13 sora → A1 (4.), A2 (5.), A3 (2.), A4 (8.), A6 (9.),
  A8 (7.), A9 (6.), A12 (11.), A13/A14 (10.), B15 (12.), C1 (13.). Az 1. sor
  (lemez-ALAK) LEZÁRVA, a maradéka B2; a 3. sor (régiók) LEZÁRVA (ND-127), a
  maradéka A10 + B5.
- `todo.md` 2. tábla 6 sora → B1, B2, B3, B4 és B5 (a 4. sor, „blokkosság",
  beolvadt B3-ba).
- `docs/backlog.md` 47 táblasora → E (28 lezárt/visszavont), A4/A5/A11/A15/A16,
  B7/B8/B9/B10/B11/B12/B13/B14, C2/C5/C6.
- `docs/backlog.md` három kidolgozott terve → hőmérséklet-overlay: A7 + B9;
  navigációs menü: B6; „látható hegységek, természetes partok": B8.
- `docs/app_base_features/…Roadmap_revised.md` + `release-qa-checklist.md` → D.
- `docs/04-decisions.md` nyitott ND-jei → A6 (108), A13 (20), A14 (21),
  A12 (19), B15 (110, 111), B2 (125 2. rész), A10 (05).
