# WorldGen – Desktop Release Roadmap (átrendezett változat)

Forrás: [`WorldGen_Desktop_Release_Roadmap.md`](WorldGen_Desktop_Release_Roadmap.md) (változatlanul megmarad).
Architektúra: [`../09-app-shell-architecture.md`](../09-app-shell-architecture.md).
Dátum: 2026-09-13.

## Mi változott az eredetihez képest, és miért

1. **Minden feladat három részre bomlik**, mert a Core-t közben tovább fejleszted:
   - **F** — Foundation: motor- és Core-független C#, dotnet-tel tesztelhető. *Most elkészíthető.*
   - **U** — Unity-kötés: scene, prefab, UI-nézet, AudioMixer, Screen API. Csak a Foundationt és Unityt hivatkozza.
   - **C** — Core-kötés: valódi paraméterséma, generálás, szimulációs állapot, bolygóprofil-értékek. *A Core-API stabilizálódására vár.*
2. **Naplózás és verziózás előrekerült a Phase 1-be** (eredetileg Phase 12/13). Az állapotváltások hibáit már az elejétől naplózni kell, a mentés verziózása pedig a `SemanticVersion`-re épül.
3. **Új alapfeladatok a Phase 1-ben:** user-data tárolás (`UserDataLayout`, atomi írás) és JSON-szerializálás. Unity alatt nincs `System.Text.Json`, a `JsonUtility` pedig motorfüggő; mindkettő előfeltétele a Settingsnek és a Save-nek.
4. **A Settings-architektúra a Main Menu elé került.** A menü a Continue-hoz mentést, a UI-hoz beállítást olvas (tooltip-késleltetés, megerősítések).
5. **A közös UI-keretrendszer (dialog, toast, tooltip, menümodell, lokalizációs tábla, vissza-navigáció) a menük elé került.** Így a menük nem építenek saját, egyedi logikát.
6. **A Loading a New World és a Pause közé került**, mert a New World → Start rögtön ezt használja.
7. **A Save/Load F része előrehozható**, a C része blokkolt (ND-108 generátorverzió, szimulációs állapot szerializálása).
8. **Új, a base features listából átvett tételek:** lokalizációs tábla, fókuszvesztés és háttér-FPS, felbontáslista-kezelés, sérült mentés felismerése, mentésrotáció.

Jelölés: ✅ kész (F) · ⏳ következő · 🔒 Core-függő, blokkolt · ○ később.

---

## Phase 1 — Application Foundation

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-APP-001 | Állapotgép (Boot, MainMenu, WorldCreation, Loading, Simulation, Paused, Quitting), sorba állított átmenetek | ✅ `AppStateMachine` | ⏳ `AppBootstrap` bekötés | — |
| WF-APP-002 | Scene-szerkezet: persistent Bootstrap + additív MainMenu / WorldSimulation | ✅ `SessionScope`, `SessionManager` | ⏳ scene-ek, a `PlanetView.unity` átemelése WorldSimulationbe | 🔒 a világ építése sessionbe kötve |
| WF-APP-003 | Szolgáltatásréteg singleton nélkül | ✅ `ServiceRegistry`, `IAppService` | ⏳ kompozíciós gyökér | — |
| WF-APP-004 *(új)* | User-data tárolás: Saves / Settings / Screenshots / Logs / Cache / Temp, atomi írás | ✅ `UserDataLayout`, `AtomicFileWriter`, `FileNameSanitizer` | ⏳ `persistentDataPath` átadása | — |
| WF-APP-005 *(új)* | JSON-szerializálás (Unity-kompatibilis, pontos 64 bites seed) | ✅ `JsonParser`, `JsonWriter`, `JsonValue` | — | — |
| WF-DIAG-001 *(előrehozva)* | Fájlnaplózás, rendszerinfó, kivételek | ✅ `AppLogger`, `FileLogSink`, `SystemInfoReport` | ⏳ `Application.logMessageReceived` → logger, `SystemInfo` | — |
| WF-REL-002 *(előrehozva)* | Szemantikus verzió, build-szám, csatorna | ✅ `SemanticVersion`, `BuildInfo` | ⏳ `bundleVersion` szinkron a `VERSION`-nel | — |

## Phase 2 — Settings Architecture *(korábban Phase 5)*

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-SET-001 | Verziózott, tűrő, perzisztens settings; Restore Defaults; sérült fájl megőrzése | ✅ `SettingsSerializer`, `SettingsStore`, `SettingsEditSession` | ⏳ | — |
| WF-SET-002 | Graphics: Display Mode, Resolution, VSync, FPS limit, Quality preset; felbontás-rollback | ✅ `GraphicsSettings`, `ResolutionCatalog`, `FrameRatePolicy`, `VideoModeConfirmation` | ⏳ `Screen`, `QualitySettings`, HDRP asset-presetek | — |
| WF-SET-003 | Simulation Quality: Fast / Balanced / Accurate | ✅ tárolás + mentésfejléc | — | 🔒 jelentése a Core-ban (seed-törő lehet) |
| WF-SET-004 | Audio: Master / Music / Ambient / Effects / UI, Mute | ✅ `AudioSettings`, `AudioMixModel` | ⏳ AudioMixer asset + exposed paraméterek | — |
| WF-SET-005 | Controls: mouse / zoom sensitivity, camera speed, invert | ✅ `ControlSettings` | ⏳ bekötés a `PlanetOrbitCamera`-ba *(meglévő viewer-kód, óvatos, kis diff)* | — |
| WF-SET-006 *(új)* | Fókuszvesztés: pause / háttér-FPS | ✅ `FrameRatePolicy` | ⏳ `OnApplicationFocus` | — |

## Phase 3 — Unified UI Framework

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-UI-003 | Közös dialogrendszer (Confirm / Warning / Error / Information) | ✅ `DialogService` | ⏳ egy prefab / nézet | — |
| WF-UI-004 | Toast (info / success / warning / error) | ✅ `ToastQueue` | ⏳ | — |
| WF-UI-005 | Tooltip (cím, rövid, részletes) | ✅ `TooltipCatalog`, `TooltipHoverTracker` | ⏳ | 🔒 paraméter- és overlay-szövegek |
| WF-UI-007 *(új)* | Lokalizációs tábla (első kiadás: angol) | ✅ `LocalizationTable` | ⏳ `Resources`/`StreamingAssets` betöltés | — |
| WF-UI-008 *(új)* | Vissza-navigáció / ESC egységesen | ✅ `BackNavigationRouter`, `ScreenStack` | ⏳ input-bekötés; a kamera-input tiltása modális UI alatt | — |
| WF-UI-009 *(új)* | UI-technológia eldöntése: UI Toolkit vs uGUI vs a mostani IMGUI | — | ⏳ **döntés kell (ND)** a U réteg előtt | — |

## Phase 4 — Main Menu

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-UI-001 | Main Menu (New World, Continue, Load, Settings, Help, Credits, Quit, verzió) | ✅ `MenuModel`, `MainMenuDefinition` | ⏳ nézet, háttér (a bolygó maga, I3!) | — |

## Phase 5 — New World Screen

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-UI-002 | Név, seed, random seed, presetek, Advanced, validáció, Reset Defaults | ✅ `WorldCreationForm`, `ParameterSchema`, `WorldPreset`, `SeedCodec` | ⏳ | 🔒 a valódi séma a Core-paraméterekből; presetek fizikai értékei |
| WF-WORLD-002 | Copy Seed / Copy Parameters, Export / Import JSON | ✅ `WorldConfigurationCodec` | ⏳ vágólap | — |

## Phase 6 — Loading Experience *(korábban Phase 8)*

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-LOAD-001 | Loading screen, súlyozott szakaszok, üzenetek | ✅ `LoadingProgressTracker`, `ProgressDisplaySmoother` | ⏳ | 🔒 a valódi szakaszok a `PlanetGridMesh.Build`-ből |
| WF-LOAD-002 | Reszponzív generálás, megszakítás | ✅ szálbiztos jelentés, `IsCancellable` | ⏳ | 🔒 háttérszálas build, megszakítási pontok |

## Phase 7 — Pause Menu

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-UI-006 | ESC → Resume, Save, Load, Settings, Help, Main Menu, Quit; szimuláció és kamera áll | ✅ `PauseMenuDefinition`, állapotgép | ⏳ | 🔒 a szimulációs idő megállítása (`SunController` / `SimulationTime`) |

## Phase 8 — Save / Load

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-SAVE-001 | Formátum: fejléc + CRC32-szekciók | ✅ `SaveContainer`, `SaveHeader` | — | 🔒 `simulation-state` szekció tartalma |
| WF-SAVE-002 | Save / Save As, atomi csere, naplózott és felhasználóbarát hiba | ✅ `SaveRepository`, `SaveErrorClassifier` | ⏳ | 🔒 |
| WF-SAVE-003 | Load lista (név, kor, létrehozva, utoljára, seed), törlés, sérült mentés jelölése | ✅ fejléc-only listázás | ⏳ | — |
| WF-SAVE-004 | Autosave (0 / 5 / 10 / 20 perc, Deep Time előtt), rotáció | ✅ `AutosaveScheduler` | ⏳ | 🔒 Deep Time esemény |
| WF-SAVE-005 | Verziózás: formátum / app / generátor → Compatible / Migratable / ConfigurationOnly / Incompatible | ✅ `SaveCompatibility` | — | 🔒 **ND-108: a Core generátorverziója** |
| WF-SAVE-006 *(új)* | Thumbnail | ✅ szekció + metaadat | ⏳ RenderTexture → PNG | — |

## Phase 9 — Audio System *(korábban Phase 6)*

A hangbeállítás (Phase 2) már kész modell. Maguk a hangfájlok **asset-beszerzést és licencet** igényelnek, ezért ez hátrébb került.

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-AUDIO-001 | Mixer-hierarchia | ✅ `AudioMixModel`, `VolumeMath` | ⏳ | — |
| WF-AUDIO-002 | Menüzene: loop, fade, átmenet | ✅ `FadeEnvelope`, `MusicDirector` | ⏳ | — |
| WF-AUDIO-003 | Ambient: space / atmosphere / ocean | ✅ csatorna | ⏳ | 🔒 dinamikusan a kamera-magasságból és a felszíntípusból |
| WF-AUDIO-004 | UI-hangok, nem agresszívan | ✅ `UiSoundThrottle` | ⏳ | — |
| WF-AUDIO-005 | Dinamikus zene alapjai | ✅ `MusicDirector` állapotai | — | ○ |

## Phase 10 — World Information

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-WORLD-001 | Planet Profile; csak valós modellértékek, hiányzónak nincs sora (I4) | ✅ `PlanetProfile`, `QuantityFormatter` | ⏳ | 🔒 a `WorldGenPanelData` / Core-metrikák leképezése |

## Phase 11 — Help

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-HELP-001…003 | Controls, Overlay, Deep Time help | ✅ lokalizációs tábla és tooltip-katalógus a hordozó | ⏳ | 🔒 szövegtartalom a Core valós viselkedéséből |

## Phase 12 — Screenshot

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-SHOT-001/002 | F12, UI nélküli kép, mappa megnyitása, toast | ✅ `ScreenshotNaming` | ⏳ `ScreenCapture`, UI-kamera tiltása | — |

## Phase 13 — Diagnostics

| ID | Feladat | F | U | C |
|---|---|---|---|---|
| WF-DIAG-002 | Debug overlay (FPS, frame time, szim. lépésidő, LOD, chunkok) | ✅ `FrameTimeStats` | ⏳ | 🔒 a meglévő PerfLog / LOD-diagnosztika átvezetése |

## Phase 14–18 — Release

Változatlan sorrendben: **Release Identity** (WF-REL-001) → **Windows Build** (WF-BUILD-001/002) → **Installer** (WF-INSTALL-001/002) → **First Run** (WF-FIRST-001; a Foundation-rész, a `showWelcomeOnStartup` és a `SettingsLoadResult.IsFirstRun` ✅) → **RC QA** (WF-QA-001…003; a QA-002-höz a `SessionManager.LiveSessionCount` ✅).

- WF-BUILD-001 javasolt kiegészítése: egy Editor build-script (`BuildPipeline.BuildPlayer`), ami a `VERSION` fájlból írja a `bundleVersion`-t, és ugyanazt a verziót teszi a `BuildInfo`-ba.
- WF-QA-003 (Deep Time regresszió) már részben megvan: `WorldStateHash` a Core-ban. A QA-script ezt hasonlítsa össze újraindítás után.

---

## Javasolt következő lépések (U réteg, a Core-tól függetlenül)

1. **ND a UI-technológiáról** (WF-UI-009). Ma IMGUI és Canvas/TMP is van a viewerben; egy kiadható menürendszerhez javaslat: UI Toolkit.
2. `Bootstrap` scene + `AppBootstrap` kompozíciós gyökér: registry, logger, settings betöltése, állapotgép.
3. Settings képernyő + Graphics / Audio alkalmazása.
4. Main Menu nézet és a meglévő `PlanetView` átemelése `WorldSimulation` scene-be. **Ez az első olyan lépés, ami meglévő viewer-kódhoz ér**, ezért egyeztessük a Core-munkáddal, hogy ne ütközzön.

---

## Állapotfrissítés — 2. kör (2026-09-13)

Részletek: `docs/09-app-shell-architecture.md` §15.

| Terület | Új állapot |
|---|---|
| WF-APP-001/002/003 | U-kód kész (`AppBootstrap`, `SceneFlow`), **scene-ek még nincsenek**, nincs bekötve |
| WF-UI-001/006, WF-SAVE-002/004 flow | F kész: `AppFlowController` (433 teszt a Foundationben) |
| WF-UI-008 ESC | F + U kész (`KeyBindingInput`, Bootstrap) |
| WF-UI-009 | **ND-110, felhasználói döntés** |
| WF-SET-002/004/006 alkalmazás | U kész (`UnityGraphicsApplier`, `UnityAudioApplier`, fókusz) — az AudioMixer asset hiányzik |
| WF-SET-005 kiosztás | F kész (`KeyBindingMap`); rebinding UI: ND-110 után |
| WF-AUDIO-002/003/004 | U kész (`MusicPlayer`, `UiSoundPlayer`) — **hangfájlok és licencük hiányzik** |
| WF-HELP-001/003 | F kész; a Deep Time szöveg felhasználói átnézést igényel |
| WF-SHOT-001/002 | U kész; a UI nélküli kép a viewer UI `IUiVisibility`-bekötésére vár |
| WF-DIAG-001/002 | U kész (napló, log-híd, hibaritkítás, F3 overlay) |
| WF-REL-001/002 | identitásfájl + build-info kész; **végleges név/cég, ikon, splash: ND-111, felhasználó** |
| WF-BUILD-001 | build-script kész, élő Unity-build még nem futott |
| WF-BUILD-002 | portable ZIP-script kész, kamu buildön kipróbálva |
| WF-INSTALL-001/002 | Inno-script kész, **Inno Setup telepítése kell a fordításhoz** |
| WF-QA-001…003 | ellenőrzőlista kész: `release-qa-checklist.md` |
