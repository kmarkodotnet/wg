# Alkalmazásréteg (App Shell) — architektúra-terv

Állapot: **2026-09-13, első terv + motor- és Core-független alap implementálva.**
Forrásfeladatok: [`app_base_features/application_base_features.md`](app_base_features/application_base_features.md),
[`app_base_features/WorldGen_Desktop_Release_Roadmap.md`](app_base_features/WorldGen_Desktop_Release_Roadmap.md).
Átrendezett, bontott feladatsor: [`app_base_features/WorldGen_Desktop_Release_Roadmap_revised.md`](app_base_features/WorldGen_Desktop_Release_Roadmap_revised.md).
Döntések: ND-105–ND-109 (`docs/04-decisions.md`).

## 0. Alapelv

A szimuláció (Core) és a viewer (LOD, render) **érintetlen**. Az alkalmazásréteg
köréjük épül, és három rétegre bomlik:

| Réteg | Hely | Hivatkozhat | Tesztelés |
|---|---|---|---|
| **F — Foundation** | `unity/WorldGenViewer/Assets/Scripts/App/Foundation/` | csak BCL (netstandard2.1) | `tests/WorldGen.App.Foundation.Tests` (dotnet, Unity nélkül) |
| **U — Unity-kötés** | `unity/WorldGenViewer/Assets/Scripts/App/Unity/` (később) | Foundation + UnityEngine | Unity EditMode/PlayMode + kézi |
| **C — Core-kötés** | `unity/WorldGenViewer/Assets/Scripts/App/WorldBinding/` (később) | Foundation + WorldGen.Core + Viewer | Unity + kézi |

A Foundation **nem hivatkozza a `WorldGen.Core`-t** (ND-105). Így a Core
párhuzamos fejlesztése nem töri, és fordítva: a Core-t sem köti az app-réteg.
Minden Core-függő adat (paraméterséma, szimulációs állapot, generátorverzió,
bolygóprofil-értékek) a Foundationben absztrakt, adatvezérelt formában
jelenik meg; a C réteg tölti fel.

### 0.1 Determinizmus-határ (ND-109)

A Foundation **nincs a determinisztikus úton**. Használhat `DateTime`-ot
(injektált `IClock`-on át), `Math.Log10`-et (hangerő-dB), kriptográfiai
entrópiát (új seed sorsolása). A határ szabálya:

- ami a szimulációba belép, az **csak explicit, tárolt érték**: seed (`ulong`),
  paraméterkészlet, szimulációs idő. Ezek bitre visszaírhatók és mentésbe
  kerülnek;
- a Foundation soha nem számol a szimuláció helyett, és nem ír vissza
  generált mezőt;
- véletlen preset (`Random`) is előbb konkrét értékeket sorsol, azok kerülnek a
  kérésbe és a mentésbe — a reprodukálhatóság így megmarad.

## 1. Modultérkép (Foundation)

```
App/Foundation/
  Versioning/      SemanticVersion, BuildInfo, ReleaseChannel
  Lifecycle/       AppState, AppStateMachine, SessionScope, SessionManager
  Services/        IAppService, ServiceRegistry
  Navigation/      BackAction, IBackHandler, BackNavigationRouter, ScreenStack
  Diagnostics/     AppLogger, ILogSink, MemoryLogSink, FileLogSink,
                   LogFileNaming, SystemInfoReport, FrameTimeStats
  Storage/         IClock, IFileSystem, PhysicalFileSystem, UserDataLayout,
                   AtomicFileWriter, FileNameSanitizer
  Serialization/   JsonValue, JsonParser, JsonWriter, Crc32
  Settings/        AppSettings (+5 kategória), SettingsSerializer,
                   SettingsStore, SettingsEditSession, VideoModeConfirmation,
                   ResolutionCatalog, FrameRatePolicy
  Localization/    LocalizationTable
  UI/              DialogService, ToastQueue, TooltipCatalog,
                   TooltipHoverTracker, MenuModel, MainMenuDefinition,
                   PauseMenuDefinition
  WorldSetup/      ParameterSchema, ParameterSet, WorldPreset, SeedCodec,
                   IEntropySource, WorldCreationForm, WorldConfigurationCodec
  Saves/           SaveHeader, SaveContainer, SaveCompatibility,
                   SaveRepository, AutosaveScheduler, SaveErrorClassifier
  Loading/         LoadingStage, LoadingProgressTracker, ProgressDisplaySmoother
  Audio/           VolumeMath, AudioMixModel, FadeEnvelope, MusicDirector,
                   UiSoundThrottle
  WorldInfo/       QuantityFormatter, PlanetProfile
  Screenshots/     ScreenshotNaming
```

## 2. Alkalmazás-állapotgép (WF-APP-001)

```
Boot ──► MainMenu ──► WorldCreation ──► Loading ──► Simulation ◄──► Paused
            ▲  │            │              │  │                       │
            │  └────────────┼──────────────┘  │                       │
            └───────────────┴─────────────────┴───────────────────────┘
   bármely állapot ──► Quitting
```

| Honnan | Hová engedett |
|---|---|
| Boot | MainMenu |
| MainMenu | WorldCreation, Loading (Continue / Load) |
| WorldCreation | MainMenu, Loading |
| Loading | Simulation, MainMenu (megszakítás / hiba) |
| Simulation | Paused, Loading (Deep Time újraépítés, ha a C réteg így kéri) |
| Paused | Simulation, MainMenu, Loading (Load World) |
| * | Quitting |

- Egyetlen belépési pont: `AppStateMachine.RequestTransition`. Nem engedett
  átmenet `false` + ok, nem kivétel — a UI-hibák ne döntsék le az appot.
- Átmenet-kérés `StateChanged` eseménykezelőn belül **sorba áll**, az aktuális
  értesítés végén fut le. Így nincs újrabelépő állapotváltás.
- Session-határ: a `Loading → Simulation` nyit sessiont (a C réteg
  `SessionManager.BeginSession`-nel), minden `MainMenu`/`Quitting`/új `Loading`
  zár. A `SessionScope` minden regisztrált `IDisposable`-t és leiratkozást
  **fordított sorrendben** futtat, a kivételeket gyűjti és naplózza.
  A `SessionManager.LiveSessionCount` a QA-002 szivárgásteszt mérőszáma.

## 3. Szolgáltatásréteg (WF-APP-003)

- **Nincs statikus singleton.** Egyetlen kompozíciós gyökér (Unity:
  `AppBootstrap` MonoBehaviour a persistent Bootstrap scene-ben) építi a
  `ServiceRegistry`-t, és explicit adja tovább.
- `IAppService.Initialize()` regisztrációs sorrendben, `Shutdown()` fordítottban.
- Duplikált regisztráció kivétel (a „globális service-ek nem duplikálódnak”
  kritérium kódszinten).
- Scene-szerkezet (WF-APP-002, U réteg): `Bootstrap` (persistent) +
  additív `MainMenu` és `WorldSimulation`. A Foundation ebből csak az
  állapotgépet és a session-takarítást adja.

## 4. ESC / vissza-navigáció

`BackNavigationRouter` sorrendje:

1. regisztrált `IBackHandler`-ek, **utolsóként regisztrált elöl** (aktív dialógus,
   nyitott tooltip-részlet, legördülő);
2. `ScreenStack` (pl. Settings → Graphics → vissza Settingsbe);
3. állapot-alapértelmezés:

| Állapot | ESC |
|---|---|
| Simulation | `OpenPauseMenu` |
| Paused | `ResumeSimulation` |
| WorldCreation | `ReturnToMainMenu` |
| Loading | `CancelLoading` |
| MainMenu, Boot, Quitting | `None` |

## 5. Tárolás, JSON, naplózás

- `UserDataLayout` a Unity `Application.persistentDataPath`-ból kapja a gyökeret:
  `Saves/`, `Settings/settings.json`, `Screenshots/`, `Logs/`, `Cache/`, `Temp/`.
  Semmi nem kerül a telepítési könyvtárba.
- `AtomicFileWriter`: `cél.tmp-<guid>` → flush lemezre → `Replace` (előző
  változat `.bak`-ba) vagy `Move`. Olvasáskor, ha a cél hiányzik, de van `.bak`,
  az helyreáll. Így mentés közben nem sérülhet az előző save (WF-SAVE-002).
- JSON (ND-106): saját, szigorú RFC 8259 parser, mélységkorláttal és
  pozíciós hibaüzenettel; a számokat **eredeti szövegként** is megőrzi, így a
  64 bites seed nem veszít pontosságot. Invariáns kultúra, `R` formátumú double.
- Naplózás: `AppLogger` szálbiztos, sinkekre ír; `FileLogSink` a
  `Logs/worldgen-yyyyMMdd-HHmmss.log` fájlba, megőrzés: legújabb N fájl.
  `SystemInfoReport` a Unity-adatokból (OS, CPU, GPU, RAM, VRAM) formáz.

## 6. Beállítások (WF-SET-001…005)

```json
{
  "version": 1,
  "graphics":   { "displayMode": "Borderless", "resolution": {...}, "vSync": true,
                  "fpsLimit": 0, "backgroundFpsLimit": 30, "quality": "High" },
  "audio":      { "master": 1.0, "music": 0.7, "ambient": 0.8, "effects": 0.8,
                  "ui": 0.6, "muted": false },
  "controls":   { "mouseSensitivity": 1.0, "zoomSensitivity": 1.0,
                  "cameraSpeed": 1.0, "invertOrbitY": false },
  "simulation": { "quality": "Balanced", "autosaveIntervalMinutes": 10,
                  "autosaveBeforeDeepTimeJump": false, "pauseWhenUnfocused": true },
  "interface":  { "language": "en", "uiScale": 1.0, "tooltipDelaySeconds": 0.5,
                  "showConfirmations": true, "showWelcomeOnStartup": true,
                  "showDebugOverlay": false, "reduceMotion": false }
}
```

- **Mezőszintű tűrés:** hiányzó vagy hibás mező → az adott mező alapértéke +
  `SettingsLoadIssue`. Teljesen olvashatatlan fájl → minden alapérték, az eredeti
  fájl `settings.corrupt-<időbélyeg>.json` néven megmarad (nem vész el csendben).
- **Verzió:** migrációs lánc v→v+1 JSON-objektumon. Újabb verziójú fájl
  (app-visszalépés) → tűrő olvasás, figyelmeztetés.
- A `SettingsEditSession` adja az Apply / Cancel / Restore Defaults (kategóriánként)
  mintát; a `VideoModeConfirmation` a felbontás-visszaállítást (alap 15 s).
- A **Simulation Quality** külön kategória a Graphics Quality-től. Jelentését a
  Core adja meg később (mintasűrűség, iterációk); ez **seed-törő lehet**, ezért a
  mentés fejléce is tárolja — ld. ND-108.

## 7. UI-modellek (WF-UI-001…006)

Minden UI-komponens **modell + nézet** szétválasztású. A Foundation a modellt adja
(állapot, szabály, esemény), a U réteg a vizuált.

- `DialogService`: egyetlen aktív modális, a többi sorban áll. Típusok:
  Confirm / Warning / Error / Information. Az ESC a `Cancel` szerepű gombot
  választja. `IsSkippable` kérés automatikusan elfogadódik, ha a felhasználó
  kikapcsolta a megerősítéseket.
- `ToastQueue`: Info / Success / Warning / Error; legfeljebb N látható, azonos
  üzenet összevonva (számlálóval), unscaled idővel lejár.
- `TooltipCatalog` + `TooltipHoverTracker`: cím, rövid leírás, opcionális
  részletes magyarázat; késleltetés a beállításból, részletes szöveg hosszabb
  hover után.
- `MenuModel`: billentyű (fel/le, körbeforduló, letiltott elemek átugrása) és
  egér (hover kiválaszt) egységesen. `MainMenuDefinition`: a Continue és a Load
  csak meglévő érvényes mentésnél aktív.
- `LocalizationTable`: kulcs → szöveg, fallback nyelv, hiányzó kulcsok gyűjtése.
  Első kiadás csak angol, de nincs beégetett UI-szöveg.

## 8. Világ létrehozása (WF-UI-002, WF-WORLD-002)

- `ParameterSchema`: a C réteg tölti fel a Core valós paramétereiből
  (kulcs, típus, min/max, alapérték, egység, advanced-e, tooltip-kulcs).
- `WorldPreset`: felülírások; a `Random` preset a sémahatárokon belül sorsol.
- `SeedCodec`: decimális `ulong`, `0x` hex, vagy tetszőleges szöveg →
  FNV-1a 64 (UTF-8). A szöveg→seed leképezés **stabil szerződés** (a megosztott
  szöveges seedek miatt), változtatása verzióemelés — ND-107.
- `WorldCreationForm.Validate()` → hibalista; érvénytelen űrlapból nem épül
  `WorldCreationRequest` (a „hibás paraméter nem indíthat generálást” kritérium).
- `WorldConfigurationCodec`: export / import JSON (seed szövegként).

## 9. Mentés (WF-SAVE-001…005) — ND-107

Konténer (little-endian):

```
"WGSV"            4 byte magic
u16 containerVer  (1)
u16 reserved      (0)
u32 headerLen
header            UTF-8 JSON (SaveHeader)
u32 headerCrc32
u32 sectionCount
section[] { u16 nameLen, name UTF-8, u64 length, u32 crc32 }
data[]            a szekciók nyers bájtjai, a táblázat sorrendjében
```

- A betöltési lista **csak a fejlécet** olvassa (gyors, a szekciókat nem).
- A szekciók (pl. `simulation-state`, `thumbnail`, `camera`) nyers bájtok; a
  tartalmukat a C / U réteg adja. Minden szekció CRC32-vel ellenőrzött →
  sérült mentés felismerése.
- Fejléc: `saveFormatVersion`, `applicationVersion`, `worldGeneratorVersion`,
  `worldName`, `seed` (szöveg), `parameterSchemaId`, `parameters`,
  `simulationQuality`, `simulationAgeYears`, `createdUtc`, `lastPlayedUtc`,
  `playTimeSeconds`, `kind` (Manual / Auto / Quick), `thumbnail` metaadat.
- Kompatibilitás:

| Szint | Mikor | Mit tehet a UI |
|---|---|---|
| Compatible | formátum és generátor egyezik | betölt |
| Migratable | régebbi formátum, van migráció, generátor egyezik | migrál + betölt |
| ConfigurationOnly | a generátorverzió eltér | „Új világ ezekkel a paraméterekkel” (a seed + paraméterek megmaradnak, az állapot nem) |
| Incompatible | újabb formátum / nincs migráció / sérült | letiltva, ok kiírva |

- `AutosaveScheduler`: 0 / 5 / 10 / 20 perc, csak `Simulation` állapotban
  számol, rotáció világonként legfeljebb N autosave.
- `SaveErrorClassifier`: kivétel → felhasználóbarát kategória (lemez megtelt,
  hozzáférés megtagadva, túl hosszú út, sérült, ismeretlen); a kivétel naplóba.

## 10. Betöltés (WF-LOAD-001/002)

- `LoadingProgressTracker`: súlyozott szakaszok, szálbiztos jelentés a
  generáló szálról, **monoton** összesített arány, aktuális üzenetkulcs.
- `ProgressDisplaySmoother`: a kijelzett érték sosem előzi meg a valódit és
  sosem megy vissza — „ne legyen félrevezető”.
- Megszakítás: `CancellationToken` a C rétegben; ha a Core adott szakasza nem
  megszakítható, a tracker ezt jelzi (`IsCancellable`).

## 11. Hang (WF-AUDIO-001…005)

- Mixer-hierarchia: Master → Music / Ambient / SFX / UI. Az `AudioMixModel` a
  beállításokból **csoportonkénti dB-t** ad (a Unity mixer maga szoroz a
  hierarchiában), valamint effektív lineáris szintet más fogyasztóknak.
  Néma → −80 dB.
- `FadeEnvelope`: lineáris és equal-power átmenet.
- `MusicDirector`: zenei állapotok (MainMenu, Space, Geological, Ocean, Life,
  ComplexLife, Catastrophe), két rétegű crossfade, lejátszási lista állapotonként.
  A dinamikus állapotváltást később a C réteg kéri (WF-AUDIO-005).
- `UiSoundThrottle`: eseményenkénti minimális időköz („ne legyen agresszív”).

## 12. Egyéb

- `QuantityFormatter` + `PlanetProfile` (WF-WORLD-001): csak a C réteg által
  ténylegesen megadott értékekből épít sort; hiányzó érték **sort nem kap**
  (I4: nincs kitalált szám, nincs placeholder).
- `ScreenshotNaming` (WF-SHOT): `worldgen-yyyyMMdd-HHmmss[-clean].png`, ütközésnél
  sorszám.
- `FrameTimeStats` (WF-DIAG-002): gördülő ablak, FPS, átlag, p95, max.
- `FrameRatePolicy`: VSync / FPS-limit / háttér-limit → Unity `vSyncCount` és
  `targetFrameRate`.
- `ResolutionCatalog`: Unity felbontáslista deduplikálása, mentett érték
  legközelebbi elérhető párja.

## 13. Nyitott kérdések

| # | Kérdés | Opciók / javaslat | Blokkol? |
|---:|---|---|---|
| 1 | Hol éljen a Foundation (ND-105) | **A:** Unity Assets alatt, `noEngineReferences` asmdef, linkelt tesztprojekt (a Lod-minta). **B:** `src/WorldGen.App` külön package. Javaslat és megvalósítás: A | nem |
| 2 | JSON-könyvtár (ND-106) | **A:** saját minimál JSON. **B:** `com.unity.nuget.newtonsoft-json`. Javaslat és megvalósítás: A | nem |
| 3 | Mentési konténer (ND-107) | bináris konténer JSON-fejléccel és CRC32-szekciókkal; alternatíva ZIP. Javaslat: saját konténer | nem |
| 4 | Generátorverzió a Core-ban (ND-108) | a Core-nak kell egy `WorldGeneratorVersion` (vagy generator/simulation/schema hármas, ld. spec). Amíg nincs, a mentés `ConfigurationOnly`-nál szigorúbb nem lehet | **igen, a Save C réteghez** |
| 5 | Szimulációs állapot szerializálása | Core checkpoint API (ND-101 említi) vagy „seed + paraméter + idő újraszámolás”. Javaslat: először az utóbbi (olcsó, a determinizmus miatt helyes), checkpoint később gyorsításnak | **igen, a Save C réteghez** |
| 6 | Simulation Quality jelentése | Core-döntés, seed-törő lehet; a mentés tárolja | nem (Foundation kész) |
| 7 | Deep Time és a Loading állapot | teljes rebuild → Loading állapot vagy háttérfolyamat a Simulationben toasttal. Javaslat: 2,8 s-os rebuildnél Simulationben marad, progress-sávval | nem |
| 8 | UI-technológia (WF-UI-009) | UI Toolkit / uGUI+TMP / a mostani IMGUI. Javaslat: UI Toolkit a menürendszerhez; új ND a U réteg előtt | **igen, a U réteghez** |

## 14. Megvalósított állapot (2026-09-13)

A Foundation (F réteg) teljes a fenti §1 modultérkép szerint; **378 xUnit-teszt**
fut Unity nélkül (`tests/WorldGen.App.Foundation.Tests`), a netstandard2.1 / C# 9
fordítási kapu 0 warninggal. Unity-ben még nem volt megnyitva: a `.meta` fájlokat
a Unity az első importkor generálja, és azokat is commitolni kell.

Eltérések / pontosítások a fenti tervhez képest:

- `SaveHeader` kapott egy tartós `WorldId`-t (az autosave-rotáció kulcsa, Save As
  után is azonos) és egy szabad `extensions` objektumot (kamera, overlay-állapot).
- `SeedCodec`: a negatív decimális bemenet (`SignedDecimal`) bitre azonos `ulong`-ra
  képződik, mert a viewer ma `long worldSeed`-et használ. A 64 bitnél nagyobb
  decimális szám szövegként hash-elődik (nem hiba).
- A betöltött settings-fájl újabb verziónál az első felülírás előtt
  `settings.v{N}.json` másolatot kap; sérült fájl `settings.corrupt-<idő>.json`-ként marad meg.
- A konfiguráció-import a tartományon kívüli értéket **nem** igazítja: az űrlap
  validációja mutatja meg (nincs csendes módosítás).
- Referencia-ellenőrzés: CRC-32 a Python `zlib.crc32` ellen; az FNV-1a 64
  prímje és offset basise a definícióból (2^40 + 2^8 + 0xb3; FNV-0 az aláírás-stringen)
  Pythonban levezetve, a tesztvektorok onnan.

Következő (U réteg, a Core-tól függetlenül): ND a UI-technológiáról → Bootstrap
scene + `AppBootstrap` kompozíciós gyökér → Settings képernyő (Graphics/Audio
alkalmazása) → Main Menu. A `PlanetView` átemelése a WorldSimulation scene-be
az első olyan lépés, ami meglévő viewer-kódhoz ér.

## 15. Megvalósított állapot — 2. kör (2026-09-13)

**Foundation-bővítés** (433 teszt összesen):

- `Flow/AppFlowController`: a menüakciók, megerősítések, állapotváltások,
  session-határok, mentéskérések, autosave és kilépés egyetlen helye. Maga nem
  generál és nem ment: eseményt küld (`NewWorldRequested`, `LoadRequested`,
  `SaveRequested`, `QuitRequested`), és `Notify…` hívásokkal kapja vissza az
  eredményt. Az ablak bezárása (`Application.wantsToQuit`) csak nem mentett
  haladásnál kérdez. `WorldSessionHostSlot`: ide köti be később a Core-kötés a
  valódi világot; cél nélkül nincs menthető állapot.
- `Controls/KeyBindingMap`: alapkiosztás (Esc fix, F12, Shift+F12, F5, F9, F1, F3),
  ütközésmentes átállítás, tűrő JSON (`Settings/keybindings.json`).
- `Diagnostics/ExceptionThrottle`, `ErrorReport`, `DebugOverlayText`.
- `Help/HelpPages`: Controls (a viewer tényleges egérvezérléséből és az aktuális
  kiosztásból) és Deep Time. **A Deep Time szövegét a felhasználónak át kell néznie**
  (a spec és a döntések alapján írtam, nem futó modellből mérve).
- `Settings/QualityLevelMapper`: a HDRP-sablon 3 szintje („High Fidelity”,
  „Balanced”, „Performant”, csökkenő sorrendben) ↔ 4 fokozat, név alapján.
- `Versioning/BuildInfoCodec`, `ReleaseIdentity` (ND-111).

**Unity-kötés** (`Assets/Scripts/App/UnityBinding/`, asmdef: `WorldGen.App.UnityBinding`),
**scene-be kötés nélkül**:

| Fájl | Tartalom |
|---|---|
| `AppBootstrap` | kompozíciós gyökér: user-data, napló (fájl + memória, 10 fájl megőrzése), rendszerinfó, Unity log-híd, settings betöltése és alkalmazása, állapotgép, dialog, toast, ESC-router, autosave, mentéslista, flow, billentyűk, videó-rollback dialógussal, screenshot, scene-flow, fókusz, kilépés, F3 debug overlay (IMGUI, fejlesztői eszköz) |
| `UnityLogBridge` | `logMessageReceivedThreaded` → `AppLogger`, kivétel-ritkítás, egyszeri értesítés |
| `UnityEnvironment` | `SystemInfo` → riport; `StreamingAssets/build-info.json` vagy Development-verzió |
| `SettingsAppliers` | Screen / QualitySettings / vSync / targetFrameRate; AudioMixer exponált paraméterek (mixer nélkül `AudioListener.volume`) |
| `UnityScreenshotService` | frame végi capture, atomi PNG; `IUiVisibility` a UI nélküli képhez (a viewer UI-ja még nem valósítja meg) |
| `SceneFlow` | additív tartalom-scene csere a persistent Bootstrap mellett |
| `AudioPlayers` | `AudioClipLibrary` ScriptableObject, `MusicPlayer` (két réteg), `UiSoundPlayer` |
| `KeyBindingInput` | `KeyChord` → legacy Input, pontos módosító-egyezéssel |

**Editor** (`Assets/Scripts/App/EditorTools/`): `WorldGenBuild` — menü és
`-executeMethod` Windows x64 Release build; az identitás csak a build idejére kerül a
PlayerSettings-be. **Kiadás** (`tools/release/`): `release-identity.json`,
`package-portable.ps1` (egy kamu build-mappán kipróbálva: ZIP + SHA-256, a DoNotShip
mappa kimarad), `WorldGen.iss` + `build-installer.ps1` (Inno Setup nincs telepítve,
**nem fordítva**, csak a hibaágak ellenőrizve), `README.md`. QA-lista:
`docs/app_base_features/release-qa-checklist.md`.

**Ellenőrzés:** `tests/WorldGen.App.UnityBinding.Compile` a helyi Unity 6000.0.77f1
DLL-jei ellen hibák és warningok nélkül fordul. Ez nem a Unity saját fordítása.

Megfigyelések a Unity-projektről, amikhez a kiadás előtt döntés kell:

- `EditorBuildSettings` scene-listája üres → a build a `fallbackScenes`-t használja.
- `ProjectSettings`: `companyName: Unity Technologies`, `productName: com.unity.template.hdrp-blank`,
  `runInBackground: 0` (a Bootstrap futásidőben igazra állítja), `resizableWindow: 0`.
