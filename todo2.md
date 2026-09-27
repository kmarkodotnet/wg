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
| A4 ✅ | **~~Deep-time újraépítés: cél <1 s~~** | **LEZÁRVA (2026-09-22), felhasználói kérésre.** Az ND-132 második javítása utáni `PerfLog_20260922_163455.txt`: hideg **3,257 s**, 12 meleg Build **2,251–2,635 s**, átlag **2,479 s**; klasszifikáció **460–556 ms**. A 27 s-os regresszió ebben a menetben nem ismétlődött; az új folyómunka a teljes Build után indul. A lezárás az elért eredmény elfogadása: **a <1 s küszöböt a mérés nem igazolja**. További variancia-/allokációvizsgálat külön az A5-ben. | ND-132, [A5 mérési napló](history/2026-09-22-deep-time-allocation-profile.md) |
| A5 ✅ | **~~Deep-time variancia és allokációprofil~~** | **LEZÁRVA (2026-09-22, ND-133–135).** Főszálú Build-allokáció **~692 → ~515 MB (−25,6%)**, Editor-kontrollátlag **2375 → 2150 ms**, bitazonos mesh-hash. Részletes víz-/CPU-profil és worker-hívásláncok elkészültek; Development Player 10 kontrollja **2038 ± 33 ms**. Záró Editor-próba: 20 időváltásos Build **1,87–2,37 s**, azonos világidőhöz visszatérve legfeljebb **116 kB** követett memóriaingadozás; 0/100/500 Myr páronként azonos terep-/víz-/tó-hash. A rövid sorozat nem hosszú szivárgásteszt vagy vizuális átvétel. Az Editor belső GC-/allokátor-oka és natív eseményprofil opcionális további vizsgálat, nem elvégzett munka; a pontosított A5-határ az ND-133-ban. **A <1 s cél nincs teljesítve.** | ND-133–135, [első javítás](history/2026-09-22-deep-time-allocation-profile.md), [CPU-/Player-mérés és lezárás](history/2026-09-22-a5-water-cpu-player-profile.md) |
| A6 ✅ | **~~ND-108 — generátorverzió a mentéshez~~** | **LEZÁRVA (2026-09-22).** Közös Core `WorldGeneratorVersion` (A6 alapvonal: `"1"`, A7-től `"2"`), CLI `.worldpkg` v3, valódi Core-verzióra kötött app `CoreSavePolicy`; a helyőrző megszűnt. Hiányzó/eltérő generátorral nincs állapotbetöltés; appban a konfiguráció külön kiolvasható. A repository közvetlen Load-kapuja a szekcióadatok előtt újra ellenőriz. **1531/1531** Debug-teszt, app **451/451** + CLI **23/23** Release-ben is; élő Unity-fordítás és adapterpróba sikeres. A B2/C3/C5 verziózási előfeltétele teljesült, a seed-törő döntések és a teljes állapotmentés külön feladatok maradnak. | ND-108, [megvalósítás és ellenőrzés](history/2026-09-22-a6-generator-version.md) |
| A7 🟡 | **Hőmodell: 6. és 7. fázis + egységes overlay-enum** | **FOLYAMATBAN (2026-09-23).** Egységes `SurfaceOverlayMode`, egyszeri scene/prefab-migráció és élő Play-ellenőrzés kész (ND-141). A 7. fázis levegőanomália → szél → hőszállítás csatolása implementált, Python-vektorral és teljes rácsos kalibrációval (ND-142): a kezdeti 1,63 K előfutási eltérés **0,173 K**-re csökkent, a ≤0,18 K regressziós kapu megmaradt. Hőmodell v3, generátor `"2"`; az A6 kapuja tiltja a korábbi állapot betöltését. A 6. fázishoz világazonosított, hash-ellenőrzött Core-checkpoint (ND-143) és kanonikus, 96 tickes napi `Ts/Ta` átlag/min/max adatút (ND-144) készült, de **egyik fogyasztó sem állt át**. Nyitott: éves klímastatisztika és költségmérés, a jégmaszk körfüggésének feloldása, a biome/jég/párolgás átállítása és validációja. A korábbi 62% nem mért készültség; a három kimenetből kettő implementált, a fogyasztói átállás nyitott. | ND-100…104, ND-141…144; [napló](history/2026-09-23-a7-thermal-feedback-overlay.md) |
| A8 🟡 | **Folyóhálózat bitazonos gyorsítása** | A naiv körsorrend elvetve: a 43 → 32 összefolyást megváltoztatná. A8/3 Core 4-worker útja t=0 és t=22 mellett bitazonos, offline 2,0–2,2× gyorsabb. A8/4 viewerben az előnézet látható; egy kész t=0 Unity-menetben a 96 finom folyó 144,683 s alatt készült el, a végleges mesh után a Buildtől 166,156 s telt el. A régi kész szekvenciális menethez képest tájékoztató ~9,1× teljesidő-csökkenés, de az idő/step nem kontrollált. A mért 3,205/18,505 s főszálú mesh-akadásra ND-147 képkockákra bontott építés készült. Az első élő ND-147-menetben a 400 696 pontos finom mesh 19,217 s aktív munkája 47,748 s alatt futott le, 7,8 ms legnagyobb szelettel és 10,8 ms feltöltéssel. A Play Pause miatt a teljes folyóidő nem összevethető; szünet nélküli mérés, deep-time, memória és B3 látványítélet nyitott. | ND-124, ND-132, ND-145, ND-146, ND-147; [A8/1](docs/plans/a8/01-meresi-alap-es-dontesi-kapu-eredmény.md), [A8/2](docs/plans/a8/02-szemantika-es-algoritmusterv-eredmény.md), [A8/3](docs/plans/a8/03-core-megvalositas-es-differencialis-tesztek-eredmény.md), [A8/4](docs/plans/a8/04-viewer-bekotes-es-elo-atvetel-eredmény.md) |
| A9 ✅ | **~~Tó-blokkosság~~** | **KÉSZ (2026-09-22, ND-129).** MÉRT gyökérok: a vízfelszín a level-8 (~36 km) tó-tile-ok unióját rajzolta, és a tile-határon VÉGET ÉRT — a partvonal 90%-a nyers tile-él volt (csak a tó-sarkok 10,3%-ánál metszette a terep). Javítás: a vízfelszín 1 gyűrűvel TÚLNYÚLIK a valódi parton, és a már renderelt, adaptív terep vágja ki belőle a partot — **nulla új eleváció-kiértékelés**, a részletesség a terep-LOD-dal (level 20-ig) zoomra magától finomodik. Plusz N=4 al-osztás a part menti tile-okon a 25 m-es húr-behúrás ellen. Mérve: 18 307 rajzolt tile, 240 202 quad, 477 ms. | ND-129, ND-49 |
| A17 ✅ | **~~Biome-blokkosság: „nagy négyszög alakú régiók"~~** | **KÉSZ (2026-09-22, ND-130).** Felhasználói visszajelzés + kép alapján felvéve. MÉRT gyökérok: az ND-126 óta a biome-osztályozás bemenete a csapadék, az viszont a `level`=5 REFERENCIA-rács **diszkrét, ~288 km-es tile-értéke** volt (a hőmérséklet ezzel szemben pontszerű, folytonos) — egy küszöb-osztályozó bemeneteként ez a biome-határt pontosan a tile-élekre teszi. Javítás: a mező BILINEÁRIS interpolációja a tile-sarkok között (ugyanaz a minta, amit a felhő-réteg már használ), plusz a vágópontok és a csapadék-overlay ugyanerre a függvényre állítva. Core-kiegészítés: `TileGeometry.ToFaceUV` (a `FromPosition` első fele kiemelve) + tesztek. Mérve: a mintapontok **21,0%-a** vált csapadék-sávot. | ND-130, ND-126 |
| A18 ✅ | **~~Óceán-partvonal blokkosság~~** | **KÉSZ (2026-09-25, ND-149) — a vizuális átvétel hátra.** MÉRT gyökérok: a látható él a VÍZFELSZÍN pereme, nem a terep-szín váltása — a vízlap azoknak a level-8 tile-oknak az uniója volt, amelyeknek a KÖZÉPPONTJA óceáni, és opak quadként a tile-határon ért véget (A/B: vízrétegek elrejtve a színhatár SIMA). Javítás az ND-129 mintájára: a vízfelszín = óceáni tile-ok + **parti gyűrű**, a partot a már renderelt terep vágja ki a z-bufferrel — nulla új eleváció-kiértékelés. A terjesztés **ÉLENKÉNTI** (egy él akkor nyers perem, ha mindkét közös sarka víz alatt van), és az óceán globális, állandó vízszintje miatt KONVERGÁL (a tavaknál nem — ez a döntő eltérés). Al-osztás nem kell: a terep- és a víz-quad azonos UV-n és azonos háromszögeléssel fekszik, a ~25 m-es húr-behúrás kiesik a különbségképzésnél. **MÉRVE:** nyers vízperem **22,34% → 0,00%**, a terep által teljesen takart perem **24,28% → 94,73%**; +14 268 víz-tile (+5,57%), a maszk **4,2 ms**, a `BuildStaticBaseLayer` mediánja 1333 → 1394 ms (a fázisok futtatásonkénti szórása ENNÉL NAGYOBB). Inspector-kapcsoló: `coastalWaterRing` (A/B-hez). Nem seed-törő. | ND-149, ND-129, ND-82/ND-83; [diagnózis](history/2026-09-25-a18-ocean-shoreline-diagnosis.md), [megvalósítás](history/2026-09-25-nd149-ocean-coastal-water-ring.md) |
| A10 ✅ | **Régió-szegmentálás: az ND-05 hibrid maradék két tagja** | **Kész (ND-152, 2026-09-27).** A biome-klaszter és a domborzati törés az összevonás ÉLSÚLYÁBAN: alapsúly 4, azonos biome +3, domborzati törés −3 (a törés a cella-közi magasságkülönbségek p75-e, tehát relatív/szintfüggetlen). Mérve a legnagyobb landmasson: a régióhatár-élek 21,0% → **43,2%**-a ül törésen, 25,4% → **41,2%**-a biome-váltáson, a kompaktság nem romlik (2,42 → 2,39). `SoilFertilityThresholds` v3-ra kalibrálva. Az ND-05 ezzel LEZÁRVA. | ND-152, ND-127, ND-05 |
| A11 🟡 | **Kameramód: pálya menti (éves) követés** | Ellenőrizve: a `CameraViewMode.OrbitalFollow` **létezik, de nincs implementálva** — a tooltip maga mondja ki („egyelőre Szabad kamera-ként viselkedik"). A tengelyforgásos mód (`AxialRotation`) kész. Nyitott tervezési kérdések: a kameracélpont befagyasztása, fusson-e közben a napi ciklus, és a nem valós lépték miatti kalibráció (ND-19). | ND-62, backlog M3/M9 |
| A12/2 🟡 | **ND-19 — floating origin, geometria-eltolás** | Az 1. kör **kész** (2026-09-27): a technika eldöntve (kamera-illesztett, rács-illesztett origó, faktor-2 hiszterézis), a motorfüggetlen mag + 48 teszt megvan, a kamera követi az origót és naplózza az elért felbontást. Hátra: a geometria tényleges eltolása, ami a **test-keretben való renderelésre** váltást igényeli (a tengelyforgás különben nem kifejezhető) — csak az A11 közeli zoomjával együtt érdemes. | ND-19 |
| A13 ✅ | **ND-20 — Burst `FloatMode.Strict` CI-kikényszerítés** | **Lezárva (2026-09-27).** A kapu `tools/ci/check_burst_strict.py`, a CI-ben önálló `burst-strict` job. CI-szkript, nem Roslyn analyzer — az analyzer nem látná az `Assets/`-et (a CI-gépeken nincs Unity). A kapu előre, nulla találaton készült el, `--self-test` 28 fixture-on bizonyítja, hogy fog. | ND-20, CLAUDE.md |
| A14 ✅ | **~~ND-21 — HDRP volumetrikus felhő űrből~~** | **KÉSZ (2026-09-27, ND-21 lezárva: ELUTASÍTVA).** A prototípus lefutott élő Editorban (Unity 6000.0.77f1 + HDRP 17.0.1, `supportVolumetricClouds` ideiglenesen be, eldobható Volume, `planetRadius = 100`, `planetCenter = 0`), utána MINDEN visszaállt (`git status` tiszta, a flag újra `0` mind a NÉGY assetben — a `todo2` korábban hármat mondott). Négy független, mért blokkoló: (1) **az `altitudeRange` alsó korlátja 100 m, keményen vágva** — MÉRVE `kért 0,05 → tényleges 100`, azaz a mi léptékünkben (1 egység = 74,2 km) a legvékonyabb héj `r = 100,02…200,02`, **7420 km vastag** felhő; (2) a `ComputeNormalizationFactor` **a Föld sugarát drótozza be** (`k_EarthRadius = 6378100`), ezért a felhőtérkép UV-je elfajul — MÉRVE: `Advanced` módban NULLA felhő, kívülről, belülről, északról és délről is; (3) a felhőtérképes út — az EGYETLEN, amit a csapadék-mezőnk hajthatna — a shaderben kizárja a fél bolygót (`„we cannot support the full planet due to UV issues"`, `if (positionPS.y < 0.0f) return;`); (4) ami renderel, az a `Simple` preset, ahol a lefedettség **konstans a shaderben** (`float4(0.9, 0, 0.25, 1)`) + procedurális zaj — **I3-sértés**, és MÉRVE fekete pettyeket ad a felszínen, mert a méter-alapú zajfrekvencia nálunk ~74 km-es szemcse. Járulékos: a felhő behúzza a HDRP ég-/bolygó-modelljét, ami MÉRVE átrendezte a saját M3-as megvilágításunkat. **GPU-költség nem mérhető** a rendelkezésre álló műszerrel (3,4–6,1 ms szórás); egyetlen tiszta pár 2,71 → 3,16 ms, tájékoztató. Következmény: a Planet nézet felhői a saját úton maradnak, az A16/M13 volumetrikus tétele innentől saját, gömbi raymarch — nem „kapcsoljuk be a HDRP-t". | **ND-21** (lezárva), ND-01, ND-19; [napló](history/2026-09-27-a14-nd21-hdrp-volumetric-clouds.md); ld. A16 |
| A15 ✅ | **~~M13 Fázis 2–4~~** | **KÉSZ (2026-09-27, ND-151) — a vizuális átvétel hátra (B16).** A 2. és 3. fázis (GPU compute a teljes kiértékelésre, GPU-vezérelt mesh) **ELVETVE, nem halasztva**: arra a pipeline-ra épültek, amit az ND-128 MÉRÉS ALAPJÁN törölt (a `Gpu/` mappa nincs meg), és a hibaosztályuk azonos — a GPU-n számolt érték a MODELL BEMENETE lenne, ami az I1-et és a HLSL lebegőpontos szabadságát állítja szembe egymással. Amit a 3. fázis valójában akart (mesh-felbontás ALATTI felszíni részlet), azt a 4. fázis megadja per-pixel árnyalással, ahol a GPU-érték KIZÁRÓLAG kimenet. Megvalósítva: Core `SurfaceMicroDetail` (konstansok + válasz-leképezés + seedből a zaj-fázis, `RandomDomain.Decorative`), motorfüggetlen `MicroDetailBand` (nézetfüggő sávválasztás), `PlanetGridMesh.MicroDetail.cs` (uniformok), és a `VertexColorUnlit` per-pixel fBm-je. A létra két vége SZÁRMAZTATOTT: alul a modell saját másodlagos relief-zaja utáni következő oktáv (4,0744 ciklus/radián), felül a float-pontosságból adódó 2^17 (~49 m). A sávváltás folytonossága bizonyított (átúsztatott oktáv-súlyok, külön teszt). **MÉRVE:** helyi kontraszt bolygó-nézetben 7,18 → 10,27 (1,43×), közeli nézetben 3,20 → 18,46 (5,76×); GPU-időben nincs mérhető különbség (a képkocka-szórás nagyobb); memória-költség NULLA (se mesh-csatorna, se textúra). Menet közben két, méréssel talált hiba javítva: a 3 pixel/legfinomabb-oktáv homokpapír-szemcsét adott (→ 8,0), és a limbnél súroló nézetben villogott (→ `N·V`-halványítás ugyanabból a lábnyom-kritériumból). Overlay alatt és a vízfelszínen kikapcsol; kikapcsolva a kép BITRE a korábbi. **Nem seed-törő, bizonyítva:** mindhárom világ-hash az ND-150 óta dokumentált érték; `WorldGeneratorVersion` marad `"5"`. Tesztek: +20 Core, +13 viewer; 1761/1761 zöld, Unity 0 hiba, shader `msgCount=0`. | **ND-151** (lezárva), ND-128, ND-19; ld. B16, A16 |
| A16 🟡 | **M13 — volumetrikus felhő + AO, színkalibráció** | Nincs elkezdve. **Az ND-21 (A14) óta az út is rögzített:** a HDRP volumetrikus felhő ELUTASÍTVA (méret-clamp, Föld-sugár konstans, féltekére vágó felhőtérkép, I3-sértő `Simple` mód), tehát ez a tétel saját, gömbi raymarch a meglévő csapadék-/nedvesség-mezőből. A színkalibráció (spec §73) csak a többi látvány-tétel után értelmes. | backlog M13, **ND-21** |
| A22 ✅ | **Deep-time paraméterlista: kontextus-struct kellene (kódolvasás, 2026-09-26)** | **KÉSZ (2026-09-26).** Az ND-137 két köre után az `ElevationWithBoundaryFromWarpedAtTime` **16 paraméteres** volt, és ugyanez a hármas — `(plateTimeMyr, erosionTimeMyr, staticSeaLevelMeters)` — végigvonult a `BaseAndUpliftFromWarpedAtTime`, a `TerrainPointBasis.EvaluateAtTime`, a `SeaLevelCalibration` és a `RiverPathTracing` teljes láncán, plusz a viewer négy statikus segédfüggvényén. Mostantól egyetlen `readonly struct DeepTimeContext { PlateTimeMyr, ErosionTimeMyr, StaticSeaLevelMeters }` utazik (`src/WorldGen.Core/Tectonics/DeepTimeContext.cs`) — az elevációs belépő 16 → **12 paraméter**, és a következő deep-time tag hozzáadása egy struct-mező, nem ~25 call-site-nyi átvezetés. **A tengerszint SZÁNDÉKOSAN property, nem mező:** az abrázió konvenciója NaN = kikapcsolva, és nyers `double` mezővel a `default(DeepTimeContext)` 0,0 m-t adna, azaz CSENDBEN bekapcsolná az abráziót a 0 m-es szint körül — a struct ezért egy `_hasStaticSeaLevel` zászlóval a nullértéket helyesen NaN-ra képezi (külön teszt fogja). Gyárimetódusok: `Static`, `AtPlateTime(t)`, `Uniform(t)`, `WithStaticSeaLevel`, `WithErosionTime`. A viewer három `_adaptive…` mezője (`ErosionTimeMyr`/`PlateTimeMyr`/`StaticSeaLevel`) egyetlen `_adaptiveDeepTime`-ra olvadt, tehát a három érték már nem tud széttartani. **BIT-SEMLEGES, verifikálva:** `worldgen hash --seed 12345 --plates 20 --level 5` a refaktor előtt és után `--time 0` → `bf7ac2b2…de7252a7`, `--time 400` → `b35e3223…5aa50f12`, `--time 3000` → `d4526336…eca73a55` (HEAD-worktree összevetés). Nincs `WorldGeneratorVersion`-emelés (bitazonos optimalizálás). 673 + 5 új teszt zöld; Unity Editor újrafordítás 0 hiba; a két offline kapu (`WorldGen.Viewer.Compile`, `WorldGen.App.UnityBinding.Compile`) zöld. Menet közben a `tools/diagnostics/RiverBaseline` (nincs a sln-ben, tehát a CI nem fogta volna meg) is átvezetve. | ND-137, C# 9 / netstandard2.1 |
| A21 ✅ | **~~`DeterministicMath.Exp` alulcsordulása: szemét 0 helyett~~** | **KÉSZ (2026-09-26, ND-150).** Az ND-137 éleset-tesztje fogta meg; a vizsgálat SZÉLESEBB hibát talált, mint a felvételkor gondoltuk. MÉRT gyökérok: a `ScaleByPowerOfTwo` a `rawExponent + k` összeget közvetlenül az IEEE-754 exponens-mezőbe írja, és nem ellenőrizte, hogy bent marad-e a normál tartományban (`[1, 2046]`) — kicsúszáskor a bitek átfolynak lefelé az **előjelbitbe**, felfelé a NaN-mintákba. Mérve előtte: `exp(-710)` = `-1,4466e+308`, `exp(-750)` = `-6,1457e+290`, `exp(-4e6)` = `+4,4595e+145`, **`exp(710)` = `NaN`**, `exp(711)` = `-1,5331e-308`, `exp(1e6)` = `6,9566e+271`, `exp(NaN)` = **kivétel**; `pow(10, 400)` = `-3,0943e-217` (rossz ELŐJEL!), `pow(10, -400)` = `-3,2317e+216`; és a `Ln` is: `ln(+∞)` = `709,78`, `ln(NaN)` = `710,19`, `ln(5e-324)` = `-709,09` a helyes `-744,44` helyett (35 nagyságrend). Javítás a `DeterministicMath`-ban, NEM modulonként: új `ScaleByPowerOfTwoChecked` (alul `0,0`, felül `+∞`), `Exp` NaN-kapu + durva `±800` argumentum-kapu (a `double → long` konverzió biztonságához — a valódi határt a pontos exponens-ellenőrzés adja), `Ln` NaN/`+∞`/**subnormális** kezelés (exakt `× 2^54` felskálázás), `Pow` `y == 0 → 1,0` rövidzár (feloldja a `0 · ∞ = NaN` csapdát). Elvetve a **fokozatos alulcsordulás** (subnormális eredmény): a bit-eltolás nem tudja előállítani, a `0,0`-ra vágás dokumentált konvenció. Az ND-137 helyi `MaxDecayExponent`-je szándékosan MARAD (eltávolítása bit-változás lenne a `(-708,396; -700)` sávban). **NEM SEED-TÖRŐ, bizonyítva:** 3000 KAT-vektor újraszámolva 0 eltéréssel, a `deterministic_math_vectors.json` újragenerálva bitre azonos (`git diff` üres), és a világ-hash `git stash`-szel mérve **mind a három időpontban változatlan** (`t=0` `2b98af9a…6213738b`, `t=400` `14dc8ad2…59a7ea07`, `t=3000` `823e8ba0…cdcfe7ab`). `WorldGeneratorVersion` marad `"5"`. Tesztek: új `DeterministicMathEdgeCaseTests` (240 mintás monotonitás- és nemnegativitás-kapu az alulcsordulási átmeneten — ez fogja meg az előjelváltást); Release-ben **1675/1675** zöld (Core 673, Viewer 526, App 452, CLI 24), Unity `compilationFailed: false`, 0 konzol-hiba. | **ND-150** (lezárva), ND-27, ND-118, ND-137; [napló](history/2026-09-26-a21-nd150-deterministic-math-edge-cases.md) |
| A19 ✅ | **A domborzati zaj nem mozog a lemezzel (felhasználói elvárás, 2026-09-22)** | **KÉSZ (2026-09-26, ND-136 (C) opció).** A zaj a lemez saját vonatkoztatási rendszerében értékelődik ki: `PlateMotion.ToPlateFrame` a pontot `R(−ωt)`-vel visszaforgatja, `CrustElevation.ComputeNoiseBasisInPlateFrame` ott mintavételez. A határon a keverés kiterjed **minden** lemezpárra (`BlendedBaseElevationFromPlateFrameBases`) — a régi „csak eltérő kéregtípusnál” kikötés kikerült, különben az azonos típusú határ is varratos lenne. Az ND-35 uplift-maszk szintén a nyertes lemez keretéből jön. **A két előzetes aggály nem igazolódott:** (a) a második zaj-bázis CSAK a keverosávon belül (`gap < 0.005`) számolódik, tehát a költség ~1× + egy keskeny sávnyi extra, nem 2×; (b) a `TerrainPointBasis` WARP-része pozíció-függő maradt, tehát az ND-122/131 lemez-cache `t > 0`-nál is érvényes — csak a három zajtag számolódik újra (`EvaluateAtTime`). Ráadásul a `RiverPathTracing` folytonos nyomvonalkövetése is megkapta a `timeMyr`-t, különben a folyó más terepen futna, mint amit a felhasználó lát. `t = 0` bitre változatlan (külön regressziós teszt); `WorldGeneratorVersion` 2 → 3. | **ND-136** (lezárva), ND-63, ND-122/131, ND-108; ld. A20, C7 |
| A20 ✅ | **Deep-time erózió: ma csak uplift-relaxáció, nem erózió (felhasználói észrevétel, 2026-09-22)** | **KÉSZ (2026-09-26, ND-137 (B) opció).** A két domborzati relief-zajtag amplitúdója mostantól **külön időállandóval** kopik: `h(t) = base_c + T_p(t)·A_p + T_s(t)·A_s`, ahol `T_i` a tag saját gömbfelszíni átlaga felé relaxál. A hullámhossz-szelektivitás a lényeg — `tau_secondary/tau_primary = f_primary/f_secondary = 5π` a **meglévő zaj-frekvenciákból származtatva**, nem új szabad paraméter —, ettől néz ki egy öreg pajzs simának, de nem laposnak. Vezérlő: `t_eff = (W(|lat|) + 3·f_ice(|lat|))·t`, ahol `W` három-cellás, pontonként kiértékelhető, bit-egzakt csapadék-proxy, `f_ice` pedig a jeges időhányad **zárt alakja** (`0,5 + asin(u)/π`) — így a `GlaciationPeriodMyr`/`AmplitudeK` ciklus végre koptat is, nem csak a jégvonalat mozgatja. **Lerakódás van benne:** ahol a tag az átlaga alatt van (medence, völgytalp), a csillapítás FELEMELI — 500 Myr-nél 161 minta emelkedett, 127 süllyedt, a relief szórása −16,5%. **Átlagtartó** (implicit izosztázia): nullához relaxálva a kontinensek ~490 m-t süllyednének és az ND-38 elárasztaná a világot; mérve a globális eltolódás 3000 Myr-nél −13,8 m. Mérhetően érezhető: kontinentális átlagos elmozdulás 117 m (100 Myr) → 222 m (2 Gyr), max ~950 m. **Menet közben javítva egy pre-existing hiba:** az uplift-relaxációt eddig CSAK a viewer adta hozzá (külön mezőpassz), a `SeaLevelCalibration`, a `RiverPathTracing` és a `worldgen hash` CLI kihagyta — `t > 0`-nál a folyó relaxáció nélküli hegyeken keresett lejtőt, a determinizmus-eszköz meg olyan világot hashelt, amit a viewer nem is jelenít meg. A kopás mostantól TELJES EGÉSZÉBEN a Core-ban van (`ElevationWithBoundaryFromWarpedAtTime`), a viewer külön passza törölve, és a három elevációs út ugyanabban a műveleti sorrendben dolgozik. Az ND-04 sértetlen: zárt alakú, a láncolás max eltérése 1,5e-14. A modul átállt a `DeterministicMath` Exp/Sin/Cos/Asin-jára, tehát **most már bit-determinisztikus**. `t = 0` bitre változatlan — a teljes világ szintjén is igazolva: `worldgen hash --time 0` a változás előtt és után egyaránt `2b98af9a…6213738b` (`--time 400` változik, ahogy kell). `WorldGeneratorVersion` 3 → 4. **2. kör (2026-09-26, „fejezd be ND-137-t"):** megjött az erózió hiányzó FELE, a **folyóvízi bevágódás** — a völgy mélyebbre vágódik, a gerinc marad, tehát a relief NŐ: `D_primary = D_p(t) · (1 + 0,6·fluvialFraction·(1 − exp(−W·t/40 Myr)))`. Mért görbe az egyenlítőn: 1,0000 → **1,2441 (t ≈ 40 Myr, csúcs)** → 0,9252 (200) → 0,4800 (3000), azaz **relief-emelkedés, majd -hanyatlás** — eddig a csúszka csak lapítani tudott. A `fluvialFraction = W/(W+3·f_ice)` **származtatott** (nincs saját konstansa): a gleccser planál, nem szabdal, tehát a jég elnyomja a bevágódást (folyóvízi hányad 1,0000 → 0,0549 a 80. foknál). **Kulcs-belátás:** a (C) opció rács-alapú `FlowAccumulation`-je nem kell — egy procedurális világban a lefolyás-hálózatot maga a zaj HATÁROZZA MEG, tehát a `primary` zajérték nem proxyja, hanem OKA a vízgyűjtőnek. Plusz **parti abrázió**: a tengerszint körüli Gauss-sávban a felszín a STATIKUS (t=0) tengerszint felé planálódik (szirt vissza, self fel), jégtakaró alatt kikapcsol, és **soha nem lő túl** a tengerszinten (külön teszt — egy villogó partvonal lenne a legszembetűnőbb hibaosztály). Két éleset a tesztekből: a negatív idő ±végtelenbe csordult (mostantól „nulla előtt nincs erózió"), és a parti abrázió `progress` kitevője is alulcsordult (ugyanaz az A21). `WorldGeneratorVersion` 4 → 5; `t = 0` bitre változatlan MINDKÉT kör után (`worldgen hash --time 0` = `2b98af9a…6213738b` végig). **Nyitva marad — döntés-köteles:** hálózat menti hordalékszállítás / delta-építés (az ND-137 (C) opciója: `O(N × tile)` minden időlekérdezésnél, és MEGTÖRI a láncolhatóságot, tehát az ND-04 értelmezését módosítja), valamint a lemez vándorlási klímatörténetének integrálása (a `W` nem integrálható analitikusan → fix-`N` kvadratúra, ugyanaz az ND-04 kérdés). | **ND-137** (lezárva, 2 körben), ND-44, ND-04, ND-108, ND-124; ld. B14, A21, A22 |

---

## B. Nyitott — te kellesz hozzá (élő Unity Play / döntés)

| # | Feladat | Mit nézz / mit döntesz | Miért nem tudom én |
|---|---|---|---|
| B1 👁 | **Lemez-overlay újranézése** | Play → tektonikus overlay. Az ND-125 óta ELŐSZÖR a valódi, szabálytalan lemezhatárokat látod (eddig az overlay a warpolatlan Voronoit rajzolta, a tile-ok 19,5–26,4%-án más felosztást). Még mindig „négyszög/háromszög"? | A B2 (méret-eloszlás) döntés előfeltétele. |
| B2 👁🔴 | **Lemez MÉRET-eloszlás: kell-e beavatkozás?** | A mérés szerint a mi eloszlásunk **egyenletesebb**, mint a Földé (a legnagyobb lemezünk 8,7–12,1%, a Csendes-óceáni 20,4%). Kell-e szabálytalanabb? | **Seed-törő**: verzió-emelés + ND-09 ordinális kalibráció újrafuttatása + minden meglévő világ domborzata változik. Ezt nem hozom meg egyedül. Előfeltétele az A6 (ND-108). |
| B3 👁 | **Folyó- és tó-hálózat vizuális elfogadása** | A felhasználó 2026-09-23-án előbb eltűnt folyókat jelzett, majd az ND-145/A8/4 után megerősítette: az előnézeti folyók **megjelentek a felszínen**. A második friss PerfLogban a 96 finom folyó is elkészült (`river ready` 144,683 s, végleges mesh 18,505 s). Az ND-147 után a finom folyók is látszanak, de zoomoláskor a folyóvonalak lemaradását és az egész kép akadását jelezte. Az ND-148 scene-kiemelés javítása után mindkét tünetet javultnak látta; a PerfLogban egy 100,2 ms-os zoomrés még maradt. A felhasználónak nem kell a logjelölőket a képernyőn keresnie. A finom fa alakja/sűrűsége és a tópart szubjektív elfogadása még nyitott. | A korábbi metrikák (összefolyás 8 → 40, max vízhozam-súly 2 → 6, „súly ≥ 3" 0 → 13) és az ND-129 129 gyűrű-tile maradéka nem helyettesítik a vizuális ítéletet. [A8/4 átadás](docs/plans/a8/04-viewer-bekotes-es-elo-atvetel-eredmény.md). |
| B4 👁 | **Új biome-térkép megítélése** | Play → felszín. Öt szárazföldi biome (sivatag/sztyeppe/szavanna/mérsékelt erdő/esőerdő). (a) Jó-e az arányuk, (b) sok-e a jég, (c) jók-e a színek? | A 20/45/75 percentilis konstrukció szerint 20/25/30/25 arányt ad — hogy ez jó-e, arra nincs numerikus kritérium. A jégtúlsúly külön, MÉRT ok → A3. |
| B5 👁 | **Régió-lista megítélése (ND-127)** | Play → navigációs menü → nagy kontinens. Most ~10 régió van a korábbi 65 helyett, mindegyik összefüggő, a szárazföld 100%-a benne van. (a) Jó-e a felbontás (`RegionTargetLandSharePercent = 3`, egy szám), (b) illenek-e a nevek/alaktípusok? | A darabolás finomsága ízlés. A lefedettséget/összefüggőséget én mérem. **FIGYELEM: az ND-152 (2026-09-27) óta más a felosztás** — a legnagyobb landmasson 11 helyett 9 régió, más tile-halmazokkal és ezért más nevekkel; a B5-höz friss Play-menet kell. |
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
| B16 👁 | **Mikro-részlet látvány-ítélete (ND-151)** | Play → felszín, overlay nélkül, közeli zoom. A `microDetailStrength` (Inspector, 0-2) élőben hangolható, a `surfaceMicroDetail` pedig A/B-hez kapcsolható. Kérdés: az 1,0-s erősség domborzatnak látszik-e vagy zajnak; a súroló megvilágításnál (terminátor) nem túl erős-e; zoomolás közben nem "lélegzik"-e a minta. | A mérés (kontraszt 1,43× / 5,76×, nulla memória, nincs mérhető GPU-költség) azt mutatja, hogy a részlet OTT VAN és a modellt követi — hogy SZÉP-e, az felhasználói ítélet. A 3 → 8 pixel/oktáv javítás már egy körben megtörtént a saját mérésem alapján. |

---

## C. Blokkolt — nem ütemezhető, amíg a feltétel hiányzik

| # | Feladat | Mi hiányzik alóla |
|---|---|---|
| C1 🔒 | **Talaj: maradék 7 regolit-mező** (MineralDiversity, Phosphorus/Nitrogen, Iron, Sulfur, Salinity, pHProxy) | Ellenőrizve: a `RegolithProfile` ma 3 mezős (Depth, Porosity, WaterRetention). A többihez **kőzettípus-/litológia-modul** ÉS **perzisztált vulkáni hamu-/tefra-mező** kellene — egyik sem létezik. Az I4 szerint kitalálni tilos. |
| C2 🔒 | **A többi ordinális panel-mező** (Climate variability, Tectonic activity stb.) | Nincs alattuk folytonos metrika, amit kvantálni lehetne. Előbb a metrika, utána az ND-09 mintájú kalibráció. |
| C3 🔒 | **Mentés `simulation-state` szekciója** | Az ND-108 generátorverzió-kapu (A6) elkészült. Hátravan az állapottartalom és szerializálás eldöntése, majd a session-host bekötése (a Core ma tiszta függvény a paraméterekből — lehet, hogy az állapot újraszámolható). |
| C4 🔒 | **Hangrendszer** | A modell és a lejátszók készek (`MusicDirector`, `UiSoundPlayer`, AudioMixer-applier), de **maguk a hangfájlok és a licencük hiányoznak**, és az AudioMixer asset sincs meg. |
| C5 🔒 | **M11 rift + lemez-hasadás/egyesülés rendszer-integráció** | A Core kész (`PlateLifecycle`, idő-lekérdezhető topológia, bit-egzakt vektorok), de a globális `PlateId` → `ulong` váltás és a split/merge bekötése a fő elevation-láncba **seed-törő**. A6 (ND-108) elkészült; a B2-szintű döntés és a tényleges integráció még hátravan. |
| C6 🔒 | **Extrém landmass méret-eloszlás strukturális javítása** | A diagnózis lezárva: a sima part-átmenet és a kiegyensúlyozott kontinensméret a jelenlegi **bináris** lemez-kéreg modellben ütköző cél. Csak egy lemezen belüli kevert-kéreg modell oldaná fel — nagy munka, seed-törő. |
| C7 🔴 | **Sebesség-alapú határ-interakció (konvergens / divergens / transform)** | **Döntés: [ND-138](docs/04-decisions.md) — a záródási ráta képlete, a háromirányú besorolás és az opciók ott.** Felhasználói kérés, 2026-09-22. Az M10 óta megvan az ω és az Euler-pólus, de a `PlateBoundaryEffect` **nem használja** őket: az uplift kizárólag a határtól mért távolságból (a két legnagyobb dot-product különbsége) jön, kéregtípussal (ND-32) és `MountainMask`-kal (ND-35) modulálva. A modul saját doksija is ezt mondja ki: „nincs valódi konvergens/divergens/transform megkülönböztetés — az M10 adja majd hozzá a sebesség-adatot". **Következmény: egy széttartó (rift) határ ugyanazt a hegység-bónuszt kapja, mint egy ütköző.** A spec §14.3 divergens ága (rifting, új kéreg, medence-képződés) és a transform ág (vetődés, lokális relief) nincs implementálva. **Amit kell hozzá:** a relatív sebességvektor a két lemez Euler-forgásából a határponton, vetítve a határ normálisára → előjeles besorolás; ebből ágazik szét az uplift/rifting/vetődés. **Blokkoló:** seed-törő (minden meglévő világ domborzata változik) → ND-döntés + ND-108 generátorverzió-emelés, a B2-vel azonos súlyú felhasználói döntés. |
| C8 🔴 | **Lemez-lemez ütközés és kéreg-megmaradás** | **Döntés: [ND-139](docs/04-decisions.md) — a lagrange-i reprezentáció ára és a fél-analitikus torlódás-proxy ott.** Felhasználói kérés, 2026-09-22. A lemezek a Voronoi-hozzárendelés miatt **szabadon áthaladnak egymáson**: minden tile mindig a legközelebbi (elmozdított) maghoz tartozik, tehát nincs átfedés, nincs ütközés, nincs kéreg-megmaradás, nincs szubdukciós fogyasztás és nincs deformáció-visszacsatolás a mozgásra. A kontinensek „átúsznak" egymáson ahelyett, hogy összetorlódnának. **Amit kell hozzá:** a Voronoi-pillanatkép helyett kéreg-anyagot követő reprezentáció (tile-onkénti kéreg-kor/vastagság, ami a lemezzel utazik), plusz konvergens határon vastagodás/szubdukció. **Blokkoló:** ez a legnagyobb egyedi tektonikai átalakítás, erősen seed-törő, és a C5 (lemez-életciklus integráció) valamint a C6 (kevert kéreg) ugyanebbe a reprezentáció-váltásba fut bele — érdemes egy tervben kezelni őket. |
| C9 🟡 | **Előírt vs. számított mozgás (köpenyáramlás)** | **Döntés: [ND-140](docs/04-decisions.md) — az `omega`-tartomány táblázata és a zárt alakú `omega(t)` ott.** Felhasználói kérés, 2026-09-22. A mozgás ma **kinematikai**: minden lemez a seedből kapott fix Euler-pólust és fix ω-t kap (0,01–0,09 rad/Myr), és ez a világ teljes 1 milliárd évére **konstans**. Nincs köpenyáramlás, nincs hajtóerő, nincs ellenállás, nincs visszacsatolás a lemezek kölcsönhatásából. Megjegyzés a konstansokhoz: a 7420 km-es sugárral ω felső határa ~67 cm/év kerületi sebesség (földi maximum ~15 cm/év), az alsó határ ~7,4 cm/év reális — a felső vég külön felülvizsgálatot érdemel. **Blokkoló:** a C8 reprezentáció-váltása nélkül nincs mire visszacsatolni; sorrendben C7 → C8 → C9. |

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
| D4 🟠 | **Save/Load C-oldal** | → C3. A fejléc, CRC32-szekciók, rotáció, autosave, thumbnail F-ben kész; ND-108 verziókapu kész. A teljes állapotmentés/session-host nincs bekötve. |
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
4. ~~**A6** (ND-108)~~ — **kész (2026-09-22)**: B2, C3/D4 és C5 közös
   verziózási előfeltétele teljesült. Minden seed-törő változáskor emelendő.
5. Közben, amikor van Play-menetd: **B1 → B2**, **B3**, **B4**, **B5**, **B6**.
   Ez az öt visszajelzés dönti el az A8, A9 sorsát is. (Az A10 2026-09-27-én
   a B5 nélkül elkészült, felhasználói kérésre — a B5 tárgya ettől nem szűnt
   meg, csak az ítélet immár az ND-152 szerinti felosztásra vonatkozik.)
6. **Tektonika-blokk (2026-09-22, felhasználói kérés) — ND-136…ND-140.** A
   javasolt sorrend **~~A19 (ND-136)~~ → ~~A20 (ND-137)~~ → C7 (ND-138) → C8 (ND-139)
   → C9 (ND-140)**, és a döntő szempont a **seed-törés hatóköre**:

   | Tétel | ND | `t = 0` törik? | Kell ND-09 újrakalibrálás? |
   |---|---|---|---|
   | ~~A19 zaj a lemez keretében~~ **kész** | ND-136 | **nem** | nem |
   | ~~A20 deep-time erózió~~ **kész** | ND-137 | **nem** | nem |
   | C9 `omega`-tartomány / `omega(t)` | ND-140 | **nem** | nem |
   | C7 határ-besorolás | ND-138 | **igen** | **igen** |
   | C8 ütközés, kéreg-megmaradás | ND-139 | **igen** | **igen** |

   Az első három csoport egyetlen ND-108 verzióemelésbe összefogható, és a
   statikus (`t = 0`) világokat bitre érintetlenül hagyja — ezeket el tudom
   végezni. A C7/C8 a statikus világot is átrendezi, tehát a B2-vel azonos
   súlyú **felhasználói döntés**, és a C8-at a C5-tel és C6-tal egy tervbe
   kell vonni (mindhárom ugyanazt a tile-onkénti kéreg-állapotot kéri).

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
- `docs/04-decisions.md` ND-jei → A6 (108, azóta lezárva), A13 (20), A14 (21),
  A12 (19), B15 (110, 111), B2 (125 2. rész), A10 (05).
- **2026-09-22, felhasználói kérés (tektonika):** A19 (a domborzati zaj
  mozogjon együtt a lemezzel), A20 (deep-time erózió hiánya), C7–C9 (a
  sebesség-alapú határ-interakció, a lemez-ütközés és az előírt mozgás
  hiánya). A C7–C9 szövegét a felhasználó idézetként adta meg; a tényállítások
  kódból ellenőrizve (`PlateMotion.cs`, `PlateBoundaryEffect.cs`,
  `DeepTimeErosionGlaciation.cs`, `CrustElevation.ComputeNoiseBasis`).
  Mind az öthöz készült döntés-bejegyzés opciókkal és javaslattal:
  **ND-136** (A19), **ND-137** (A20), **ND-138** (C7), **ND-139** (C8),
  **ND-140** (C9) — `docs/04-decisions.md`. A felmérés naplója:
  [tektonikai hiányosságok](history/2026-09-22-tectonics-gap-nd136-140.md).
















----------------------------






-

Félbehagyott munka (ezek a legfontosabbak)

A7 🟡 — Hőmodell 6–7. fázis
A hőmodell visszacsatolása (levegő-anomália → szél → hőszállítás) már megvan, sőt kalibrálva is van (0,173 K előfutási eltérés). Elkészült két új adatút is: egy világazonosított, hash-ellenőrzött Core-checkpoint (ND-143) és egy 96 tickes napi Ts/Ta átlag/min/max kimenet (ND-144). Csak épp egyik fogyasztó sem használja őket — a biome-osztályozás, a jégmaszk és a párolgás továbbra is a régi, egyszerűbb hőmérsékletet olvassa. A feladat tehát nem új algoritmus, hanem átkötés + validáció: átállítani a három fogyasztót a napi statisztikára, feloldani a jégmaszk körfüggését (jelenleg körkörös a hivatkozás), és megmérni, mennyibe kerül mindez. A doksiban korábban szereplő „62%" nem mért szám volt.

A8 🟡 — Folyóhálózat gyorsítása
A Core oldali 4-workeres út bitazonos és offline 2,0–2,2× gyorsabb, a viewerben az előnézet is látszik, és a főszálú mesh-akadást (3,2 / 18,5 s!) az ND-147 képkockákra bontott építése megoldotta (7,8 ms a legnagyobb szelet). Ami hátra van: szünet nélküli, tiszta mérés (az eddigi menetekben Play Pause volt, így a teljes folyóidő nem összevethető), deep-time viselkedés, memóriaprofil, és a B3 látványítélet — ez utóbbi hozzád tartozik.

---

Helyességi tétel — ez a legalattomosabb

A21 🟠 — DeterministicMath.Exp alul-/túlcsordulása
A függvény bit-manipulációval skálázza a végeredményt (ScaleByPowerOfTwo), és nem figyeli, ha az exponens kifut a double tartományából. Kb. -710 alatt az exponens átcsordul az előjelbitbe, és az eredmény nem 0-hoz tart, hanem determinisztikus szemét: exp(-710) = -1,45e+308, exp(-4e6) = +4,46e+145. A túlcsordulási oldalon ugyanez.

Ma ez senkit nem ér el élesben, mert az ND-137 helyben védekezik (MaxDecayExponent = 700) — de ez a védelem modulonként megismétlendő, és pont ez az a fajta dolog, ami egyszer ki fog maradni. A javítás helye a DeterministicMath.Exp maga (alul 0.0, felül PositiveInfinity), és a Ln/Pow is átnézendő. Előtte végig kell ellenőrizni, hogy egyetlen meglévő KAT-vektor sem esik a ma szemetes tartományba — ha nem, a változás bitre semleges, és nem seed-törő.

---

Kozmetika, de gyűlik a kamat

A22 ✅ — Deep-time kontextus-struct
Az `ElevationWithBoundaryFromWarpedAtTime` 16 paraméteres volt, és ugyanaz a hármas — (plateTimeMyr, erosionTimeMyr, staticSeaLevelMeters) — vonult végig öt Core-osztályon és négy viewer-segédfüggvényen. Mostantól egy `readonly struct DeepTimeContext` utazik helyettük (12 paraméter), a viewer három `_adaptive…` mezője pedig egyetlen `_adaptiveDeepTime`-ra olvadt. A refaktor bit-semleges: a `worldgen hash` t = 0 / 400 / 3000-nél bitre ugyanaz, mint a HEAD-en (külön worktree-ben mérve), tehát nincs verzióemelés. A tengerszint property, nem mező — különben a `default(DeepTimeContext)` 0,0 m-t adna, ami csendben bekapcsolná a parti abráziót. Napló: `history/2026-09-26-a22-deep-time-context.md`.

---

Infrastruktúra / döntéshez kötött

A13 ✅ — ND-20, Burst FloatMode.Strict CI-kapu
Lezárva (2026-09-27). A repoban továbbra sincs egyetlen [BurstCompile] sem — épp ezért készült el a kapu ELŐRE, az első használat előtt: `tools/ci/check_burst_strict.py` + `burst-strict` CI-job. A szabály: minden [BurstCompile] explicit FloatMode = FloatMode.Strict-et kap, és a FloatPrecision nem lehet Low vagy Medium (azok approximációt engedélyeznek, ugyanabba az I1-osztályba tartoznak). CI-szkript lett, nem Roslyn analyzer, mert az analyzert csak a `dotnet build` futtatja — a Unity-oldali `Assets/`-et a CI-gépeken nincs mivel lefordítani; a szöveges scanner egy futással látja mind a 301 `.cs`-t. Mivel egy üres halmazon mindig zöld kapu értéktelen, a `--self-test` 28 fixture-on (13 átengedendő, 14 elutasítandó, 1 sorszám) bizonyítja, hogy valóban fog — és ez a self-test fogott is egy valódi hibát a kapuban fejlesztés közben (összevont attribútum-lista, `[StructLayout(...), BurstCompile]`: a visszafelé-olvasás megállt az előző attribútum záró zárójelén, és a sértés átment). Napló: `history/2026-09-27-a13-nd20-burst-strict-gate.md`.

A12 ✅ (1. kör) / A12/2 🟡 — ND-19, floating origin
Az 1. kör kész (2026-09-27). Mérve és megdőlt a régi ND-19 egyik állítása: a float32 felszíni hibája LÉPTÉK-INVARIÁNS (0,566 m radius=100-nál, 0,500 m a valós léptéknél, 0,885 m egység-gömbön), tehát a kis Unity-lépték nem védelem, és a méterszintű zoom semmilyen radius-értékkel nem érhető el — csak origó-eltolással. A választott technika: kamera-illesztett, 2-hatvány rácsra illesztett origó, 1,5×cella rebase-hiszterézissel, a cella felülről a bolygósugárhoz korlátozva. Kész: `Viewer/Lod/FloatingOrigin.cs` (motorfüggetlen, 48 teszt) + `PlanetOrbitCamera.FloatingOrigin.cs` (origó-követés + precízió-napló). Az élő Play-mérés egy valódi hibát fogott: korlát nélkül bolygó-nézeti magasságon az origó rosszabb volt az abszolútnál (gainFactor 0,3) — javítva, regressziós teszttel.
A12/2 (hátra): a geometria tényleges eltolása. Ez a **test-keretben (body frame) való renderelésre** váltást igényeli, mert origó-relatív csúcsok + identitás-transform mellett a bolygó tengelyforgása nem kifejezhető a transform-mal. Érintett: a sarok-cache Vector3 → double, radialBias, ToWaterVector3, StarField, SunController, és a `distance` float mező. Csak az A11 közeli zoomjával együtt van mérhető haszna — a most bekötött `gainFactor` napló mondja meg, mikor. Napló: `history/2026-09-27-a12-nd19-floating-origin.md`.

A11 🟡 — Pálya menti (éves) kameramód
A CameraViewMode.OrbitalFollow létezik az enumban, de nincs implementálva — a tooltip maga bevallja, hogy „egyelőre Szabad kameraként viselkedik". A tengelyforgásos mód kész. Nyitott tervezési kérdések: befagyasztjuk-e a kameracélpontot, fusson-e közben a napi ciklus, és a nem valós lépték miatti kalibráció → ez az A12-be fut bele.

---

Látvány (a hátsó sor)

A15 🟡 — M13 Fázis 2–4: GPU-vezérelt geometria, procedurális mikro-részlet textúra. Nincs elkezdve (az 1. fázis, a folytonos árnyalás, kész).

A16 🟡 — M13 volumetrikus felhő + AO, színkalibráció: nincs elkezdve; a színkalibráció csak a többi látvány-tétel után értelmes.

A14 ✅ — ND-21, HDRP volumetrikus felhő űrből: a prototípus 2026-09-27-én lefutott, a döntés ELUTASÍTÁS. Négy mért blokkoló: a rétegvastagság alsó korlátja (100 m, keményen vágva) a mi léptékünkben 7420 km vastag felhőhéjat ír elő; a sűrűség-normalizálás a Föld sugarát drótozza be, amitől a felhőtérkép UV-je elfajul (Advanced módban nulla felhő); a felhőtérképes út — az egyetlen, amit a csapadék-mezőnk hajthatna — a shaderben kizárja a déli féltekét; és ami renderel (Simple preset), annak a lefedettsége konstans a shaderben, tehát I3-sértés. A Planet nézet felhői a saját úton maradnak; az A16 volumetrikus tétele saját, gömbi raymarch. Napló: history/2026-09-27-a14-nd21-hdrp-volumetric-clouds.md.

A10 ✅ — ND-05 hibrid régió-szegmentálás: LEZÁRVA (ND-152, 2026-09-27). A vízgyűjtő maradt az atom (ND-127), a biome-klaszter és a domborzati törés az agglomeratív összevonás élsúlyaként jött be, ugyanazon a cella-gráfon. Mérve: a régióhatárok 21,0% → 43,2%-a ül domborzati törésen, 25,4% → 41,2%-a biome-váltáson, a kompaktság nem romlik. Nem seed-törő, de a régiónevek és a SoilFertility-küszöbök (v3) változtak. Napló: history/2026-09-27-a10-nd152-region-hybrid.md.
