# WorldGen – Desktop Release Roadmap

A jelenlegi WorldGen projektben a core szimuláció már működik: a bolygó forog, bizonyos paraméterek állíthatók, vannak overlayek, Deep Time ugrás, vizuális zoom és egyéb szimulációs funkciók.

A következő cél az, hogy a projektből telepíthető, kiadható Windows alkalmazás / játék készüljön. Ehhez a szimuláció köré fel kell építeni a teljes alkalmazásréteget: főmenü, beállítások, hang, mentés-betöltés, loading, hibakezelés, build, installer és release QA.

A roadmapet úgy érdemes végrehajtani, hogy Claude Sonnet egymásra épülő, jól körülhatárolt fejlesztési taskokat kapjon.

---

## Phase 1 — Application Foundation

### WF-APP-001 – Application State Architecture

Állapotok:

- Main Menu
- World Creation
- Loading
- Simulation
- Pause / Menu
- visszatérés Main Menu-be
- Quit

#### Acceptance Criteria

- ugyanabban a processben több világ egymás után elindítható
- nincs scene/resource leak
- ESC konzisztensen működik
- állapotváltások centralizáltak

---

### WF-APP-002 – Scene Structure Refactor

Javasolt struktúra:

```text
Bootstrap
MainMenu
WorldSimulation
```

Alternatíva: persistent Bootstrap scene + additív scene-ek.

#### Acceptance Criteria

- globális service-ek nem duplikálódnak
- új simulation session indításakor az előző teljesen takarítható
- nincs `FindObjectOfType`-okra épülő kaotikus lifecycle

---

### WF-APP-003 – Global Service Layer

Legalább:

- SettingsService
- SaveGameService
- AudioService
- Scene/ApplicationStateService
- ScreenshotService
- LoggingService

Fontos: ne készüljön mindenből Singleton csak azért, mert egyszerűbb.

---

# Phase 2 — Main Menu

### WF-UI-001 – Main Menu

Menüpontok:

```text
WORLDGEN

New World
Continue
Load World
Settings
Help
Credits
Quit

v0.x.x
```

#### Acceptance Criteria

- billentyűzet + egér
- hover / selected / disabled állapot
- Continue csak akkor aktív, ha létezik mentés
- verzió megjelenik
- Quit Windows buildben ténylegesen kilép

---

### WF-UI-002 – New World Screen

A jelenlegi generálási paraméterek kerüljenek egy egységes New World képernyőre.

Plusz:

- World Name
- Seed
- Random Seed
- Reset Defaults
- Presetek
- Advanced Settings
- Start Simulation

Első presetek:

```text
Earth-like
Ocean World
Dry World
High Gravity
Low Gravity
Geologically Active
Random
```

#### Acceptance Criteria

- seed másolható
- ugyanaz a seed + paraméterek reprodukálható világot adnak
- hibás paraméter nem indíthat generálást
- Advanced rész összecsukható

---

# Phase 3 — Unified UI Framework

### WF-UI-003 – Common Dialog System

Egyetlen újrahasználható rendszer:

```text
Confirm
Warning
Error
Information
```

Példa:

> Unsaved simulation progress will be lost. Continue?

Ne legyen mindenhol külön prefab és külön logika.

---

### WF-UI-004 – Toast / Notification System

Példák:

```text
World saved
Screenshot saved
Settings applied
Simulation paused
Autosave completed
```

A rendszer támogassa:

- info
- success
- warning
- error

---

### WF-UI-005 – Tooltip System

Különösen fontos a WorldGen paramétereihez és overlayeihez.

A tooltip tudjon:

- címet
- rövid leírást
- opcionális részletes magyarázatot

Példa:

```text
Tectonic Activity

Controls the average rate of lithospheric plate motion.
Higher values generally result in increased mountain building,
volcanism and crustal recycling.
```

---

# Phase 4 — Pause / In-game Menu

### WF-UI-006 – Pause Menu

ESC:

```text
Resume
Save World
Load World
Settings
Help
Return to Main Menu
Quit to Desktop
```

#### Acceptance Criteria

- simulation pause-ol
- kamera/input nem működik a háttérben
- Settings módosítható játék közben
- Return to Main Menu takarítja a session state-et

---

# Phase 5 — Settings System

Ez önmagában is egy kisebb subsystem.

## WF-SET-001 – Persistent Settings Architecture

Kategóriák:

```text
Graphics
Audio
Controls
Simulation
Interface
```

Mentési hely:

`Application.persistentDataPath`

Ne `PlayerPrefs` legyen az egész rendszer alapja.

Példa:

```json
{
  "version": 1,
  "graphics": {},
  "audio": {},
  "controls": {},
  "simulation": {},
  "interface": {}
}
```

#### Acceptance Criteria

- restart után megmarad
- hibás config esetén default
- verziózott schema
- Restore Defaults

---

### WF-SET-002 – Graphics Settings

MVP:

- Display Mode
- Resolution
- VSync
- FPS Limit
- Graphics Quality

Presetek:

```text
Low
Medium
High
Ultra
```

Később külön:

- Atmosphere
- Cloud Quality
- Shadow Quality
- Planet LOD
- Render Scale
- HDR opcionálisan
- monitor választás
- video setting rollback

---

### WF-SET-003 – Simulation Quality

A WorldGen miatt ezt külön kell választani a grafikai qualitytől.

Példa:

```text
Simulation Quality:
Fast
Balanced
Accurate
```

Később ez szabályozhat:

- sampling density
- climate iterations
- erosion resolution
- tectonic solver detail
- Deep Time pontosság

Ez hosszú távon fontos architekturális döntés.

---

### WF-SET-004 – Audio Settings

Sliderek:

```text
Master
Music
Ambient
Effects
UI
```

Plusz:

- mute
- Restore Defaults

---

### WF-SET-005 – Control Settings

Első körben:

- mouse sensitivity
- zoom sensitivity
- camera speed

Később:

- teljes key rebinding
- input conflict detection
- Restore defaults

---

# Phase 6 — Audio System

### WF-AUDIO-001 – Audio Mixer Architecture

Unity AudioMixer groupok:

```text
Master
├── Music
├── Ambient
├── SFX
└── UI
```

A Settings közvetlenül ezt kezelje.

---

### WF-AUDIO-002 – Main Menu Music

- loop
- fade-in
- fade-out
- seamless átmenet simulation zenére

---

### WF-AUDIO-003 – Simulation Ambient Audio

Első körben egyszerű:

- space ambience
- atmosphere ambience
- ocean ambience

Később dinamikussá tehető.

---

### WF-AUDIO-004 – UI Sounds

Legalább:

- hover
- click
- back
- confirm
- error

Ne legyen túl agresszív.

---

### WF-AUDIO-005 – Dynamic Music Foundation

Érdemes architekturálisan előkészíteni, de nem kell rögtön teljesen implementálni.

A későbbi rendszer állapotokat kaphat:

```text
Space
Geological
Ocean
Life
ComplexLife
Catastrophe
```

És ezek között crossfade-elhet.

---

# Phase 7 — Save / Load

Ez kritikusabb subsystem, mint elsőre látszik.

### WF-SAVE-001 – Save Game Format

A mentés minimum tartalmazza:

- save format version
- world name
- seed
- generation parameters
- simulation time
- simulation state
- overlay / user state, ahol szükséges
- camera állapot opcionálisan
- thumbnail metadata

---

### WF-SAVE-002 – Save World

Pause menüből:

```text
Save
Save As
```

#### Acceptance Criteria

- mentés közben ne sérülhessen az előző save
- temporary file → atomic replacement
- exception logolva
- user-friendly hiba

---

### WF-SAVE-003 – Load World

Lista:

```text
Planet Name
Age
Created
Last Played
Seed
```

Később thumbnail.

---

### WF-SAVE-004 – Autosave

Például:

- 5 / 10 / 20 perc
- kikapcsolható

Bizonyos eseményekkor:

- Deep Time jump előtt/után opcionálisan

---

### WF-SAVE-005 – Save Versioning

Minden save tartalmazza:

```text
SaveFormatVersion
ApplicationVersion
WorldGeneratorVersion
```

Így később eldönthető:

- kompatibilis
- migrálható
- inkompatibilis

---

# Phase 8 — Loading Experience

### WF-LOAD-001 – Loading Screen

Példa:

```text
Generating planetary crust...
Building tectonic plates...
Calculating climate...
Generating oceans...
Preparing renderer...
```

Progress bar.

Nem szükséges, hogy minden százalék fizikailag tökéletesen pontos legyen, de ne legyen félrevezető.

---

### WF-LOAD-002 – Responsive Generation

Amennyire a jelenlegi architecture engedi:

- ne álljon meg a UI
- loading animation fusson
- lehetőség szerint async / coroutine / job alapú folyamat
- generálás megszakítása, ha technikailag lehetséges

---

# Phase 9 — World Information

### WF-WORLD-001 – Planet Profile

Példa:

```text
Planet: Gaia-8214

Age                  2.73 Ga
Radius                7,021 km
Mass                  1.12 Earth
Surface Gravity       1.06 g
Ocean Coverage        63%
Mean Temperature      16.3 °C
Atmospheric Pressure  1.14 atm

Continents             5
Tectonic Plates        11
Tectonic Activity      High
```

Az értékek mindig abból jöjjenek, amit ténylegesen tud a motor.

---

### WF-WORLD-002 – Seed & World Export

Legalább:

```text
Copy Seed
Copy World Parameters
```

Később:

```text
Export World Configuration
Import World Configuration
```

Lehet például JSON.

Ez kutatási / szimulációs jellegű használatnál nagyon értékes.

---

# Phase 10 — Help System

### WF-HELP-001 – Controls

Help → Controls.

---

### WF-HELP-002 – Overlay Help

Minden overlayhez:

- mit mutat
- mit jelent a skála
- hogyan interpretálandó

---

### WF-HELP-003 – Deep Time Help

Magyarázza:

- mit jelent a Deep Time
- milyen folyamatokat gyorsítunk
- mely dolgok determinisztikusak
- hol történhet approximation

---

# Phase 11 — Screenshot System

### WF-SHOT-001 – Screenshot

Hotkey például:

```text
F12
```

Screenshot mentése:

```text
WorldGen/Screenshots/
```

Toast:

```text
Screenshot saved
```

---

### WF-SHOT-002 – Clean Screenshot

Külön hotkey vagy opció:

```text
Capture without UI
```

A projektnél ez különösen hasznos.

---

# Phase 12 — Logging & Diagnostics

### WF-DIAG-001 – File Logging

Példa:

```text
Logs/worldgen-yyyyMMdd-HHmmss.log
```

Tartalmazza:

- application version
- Unity version
- OS
- CPU
- GPU
- RAM
- VRAM, ha lekérdezhető
- startup
- world creation
- save / load
- exceptionök

---

### WF-DIAG-002 – Debug Overlay

Development buildben például:

```text
FPS
Frame Time
Simulation Step Time
GPU Memory
Current LOD
Active Chunks
```

Release-ben default OFF.

---

# Phase 13 — Release Identity

### WF-REL-001 – Product Identity

Beállítani:

- Product Name: `WorldGen` vagy végleges név
- Company
- Version
- icon
- splash screen
- executable metadata

---

### WF-REL-002 – Versioning

Indulásnak:

```text
0.1.0-alpha
```

Semantic versioning:

```text
MAJOR.MINOR.PATCH
```

Példák:

```text
0.1.0
0.1.1
0.2.0
```

---

# Phase 14 — Windows Build

### WF-BUILD-001 – Release Build Profile

Cél:

```text
Windows x86-64
Release
Development Build = OFF
```

Fix build pipeline.

---

### WF-BUILD-002 – Portable Distribution

Első kiadásként legyen:

```text
WorldGen-0.1.0-win64.zip
```

Kicsomagol → `WorldGen.exe`.

Ez telepítő előtt nagyon hasznos.

---

# Phase 15 — Installer

### WF-INSTALL-001 – Windows Installer

Javasolt technológia:

**Inno Setup**

Installer:

```text
WorldGen Setup
```

Opciók:

- install directory
- Start Menu
- Desktop shortcut
- launch after install

---

### WF-INSTALL-002 – Uninstaller

Uninstall ne törölje automatikusan:

```text
Saves
Settings
Screenshots
```

Ha valaha törölni akarjuk, külön checkbox legyen.

---

# Phase 16 — First Run Experience

### WF-FIRST-001 – Welcome Screen

Első indításkor:

```text
Welcome to WorldGen
```

Röviden:

- Create a world
- Navigate
- Use overlays
- Explore Deep Time

Checkbox:

```text
Don't show this again
```

---

# Phase 17 — Release Candidate QA

### WF-QA-001 – Clean Installation

Tiszta Windows környezet:

```text
Install
Launch
Create world
Play
Save
Exit
Restart
Load
```

---

### WF-QA-002 – Session Lifecycle

Teszt:

```text
Main Menu
→ World A
→ Main Menu
→ World B
→ Main Menu
→ World C
```

Nem maradhat:

- memory leak
- duplikált object
- beragadt event subscription
- előző sessionből maradt state

---

### WF-QA-003 – Deep Time Regression

Világ:

```text
Seed = X
```

Futtatás:

```text
0 → 1 Ma → 100 Ma → 1 Ga
```

Restart után ugyanazzal a seed/state-tel ellenőrizni a determinisztikusságot.

---

# WorldGen-specifikus extra feature-csoportok

## 1. Simulation Reproducibility

A UI-ból egyértelműen lehessen látni és másolni:

- seed
- world parameters
- world generator version

Később:

- export
- import
- reprodukálható világmegosztás

---

## 2. Simulation vs. Rendering különválasztása

Fontos, hogy a simulation quality ne legyen azonos a graphics qualityvel.

Például:

```text
Graphics Quality:
Low
Medium
High
Ultra

Simulation Quality:
Fast
Balanced
Accurate
```

Így egy gyengébb GPU-val rendelkező user is futtathat pontosabb szimulációt alacsonyabb vizuális részletességgel.

---

## 3. World Summary / Planet Profile

A WorldGen egyik legfontosabb információs UI-ja lehet.

Mutathatja:

- világ neve
- seed
- simulation age
- radius
- mass
- gravity
- water coverage
- temperature
- atmosphere
- continents
- tectonic plates
- tectonic activity
- egyéb későbbi evolúciós / klimatikus adatok

---

# WorldGen 0.1.0 Desktop MVP

Az első kiadható buildbe javasolt:

```text
✓ Main Menu
✓ New World
✓ Pause Menu
✓ Settings
✓ Graphics Settings
✓ Audio Settings
✓ Basic Audio
✓ Save / Load
✓ Autosave
✓ Loading Screen
✓ Planet Profile
✓ Seed Copy
✓ Help / Controls
✓ Screenshot
✓ Logging
✓ Versioning
✓ Windows x64 Build
✓ Portable ZIP
✓ Installer
```

---

# Amit nem szükséges erőltetni 0.1.0-ra

```text
- Steam
- auto updater
- achievements
- controller támogatás
- teljes key rebinding
- többnyelvűség
- elaborate tutorial
- cloud saves
- telemetry
- crash upload service
- komplex adaptív soundtrack
```

---

# Claude Sonnet fejlesztési feladatok struktúrája

Nem érdemes a teljes roadmapet egyszerre implementáltatni.

Javasolt task-méret:

```text
TASK WG-001
Implement the application state and scene lifecycle architecture.

TASK WG-002
Implement the main menu using the existing UI visual language.

TASK WG-003
Implement persistent versioned settings storage.

TASK WG-004
Implement graphics settings.

TASK WG-005
Implement AudioMixer-based audio settings.
```

Minden Claude task tartalmazza:

```text
Context
Goal
Existing behavior that must not break
Technical constraints
Implementation requirements
Files/components likely affected
Acceptance criteria
Manual test procedure
Out of scope
```

Kiemelten fontos instrukció:

> Do not rewrite or alter the existing simulation logic unless explicitly required by this task.

A core WorldGen már működik, ezért minden új feature-nek a meglévő szimuláció köré kell épülnie, nem pedig újraimplementálnia azt.

---

# Javasolt első Claude Sonnet fejlesztési batch

Az első batch tartalma:

```text
Application Foundation
Main Menu
Pause Menu
Settings Architecture
```

Ebből kb. 8–12 egymásra épülő task készíthető.

Javasolt sorrend:

1. Application state / lifecycle architecture
2. Bootstrap and scene structure
3. Global service layer
4. Main Menu
5. New World screen integration
6. Pause Menu
7. Common dialog system
8. Persistent settings architecture
9. Graphics settings
10. Basic audio settings
11. Navigation / back handling
12. Session cleanup regression testing

Ez stabil alapot ad minden további desktop-release feature számára.
