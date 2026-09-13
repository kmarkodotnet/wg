# 2026-09-13 — Alkalmazásréteg (App Shell) — Foundation

Ág: `codex-handoff`. Párhuzamosan a munkafában nem commitolt Core-munka
(hőmodell, ND-99–104) volt; ehhez ez a munkamenet nem nyúlt, csak saját
fájlokat commitolt. A `docs/04-decisions.md`-ből kizárólag az ND-105–109 blokk
került indexbe (a HEAD-változatra illesztve), a munkafa szövege változatlan.

## [1] — A desktop-kiadás alkalmazásrétegének elindítása

**Felhasználói kérés:** „olvasd fel a \docs\app_base_features-ben lévő leírást
és feladatokat. ez alapján szeretnék lassan belekezdeni a core alkalmazás
körüli fő alkalmazás kiépítésének, menü, zenék, mentés, beállítások, stb. …
úgy, hogy közben még fejleszteni fogom a core alkalmazást. írd meg azokat a c#
kódokat, amiket a core-tól függetlenül meg lehet írni. … ha szükséges, módosíts
rajta, de ennek eredményét egy másik fájlba írd.” — majd: „folytasd”.

**Történt:**

- Dokumentáció előbb: `docs/09-app-shell-architecture.md` (F / U / C
  rétegfelosztás, állapotgép-tábla, ESC-szabály, settings-séma, mentési
  konténer, kompatibilitási szintek, nyitott kérdések), átrendezett roadmap
  külön fájlban: `docs/app_base_features/WorldGen_Desktop_Release_Roadmap_revised.md`
  (az eredeti változatlan). Döntések: ND-105–109 (a blokk az app-rétegé,
  a Core-munka ND-110-től folytassa).
- Fő architekturális döntés (ND-105): a Foundation a
  `unity/WorldGenViewer/Assets/Scripts/App/Foundation/` alatt, `noEngineReferences`
  asmdef-fel, **Core-referencia nélkül** — így a Core párhuzamos fejlesztése nem
  töri. Fordítási kapu: `tests/WorldGen.App.Foundation.Compile` (netstandard2.1,
  C# 9, linkelt forrás), tesztek: `tests/WorldGen.App.Foundation.Tests`.
- Saját JSON (ND-106), mert Unity alatt nincs `System.Text.Json`, és a 64 bites
  seednek pontosan kell túlélnie.
- Referencia-ellenőrzés a CLAUDE.md szerint, nem emlékezetből: CRC-32 a Python
  `zlib.crc32` ellen; FNV-1a 64 prím és offset basis a definícióból levezetve.
- Menet közben talált és javított saját hibák: JSON-hibapozíció tesztvárakozás,
  nem pozitív felbontás csendes elvesztése a settings-riportból, fejben számolt
  hexa→decimális tesztérték, `FormatAge` 999,6 év → „1000 yr” határeset,
  xUnit1031 blokkoló `Wait()`.

**Eredmény:**

| Commit | Tartalom |
|---|---|
| `308227d` | állapotgép, session, service registry, ESC-router, tárolás + atomi írás, JSON, CRC-32, naplózás, SemVer; docs + ND-105–109 |
| `1e6afbb` | settings (séma v1, tűrő olvasás, store, edit session, video-rollback, felbontás/FPS-policy), lokalizáció, dialog/toast/tooltip/menü-modellek |
| `3d27361` | paraméterséma, presetek, SeedCodec, New World űrlap, konfiguráció export/import, mentési konténer, mentéslista, autosave, betöltési progress |
| `ee23bf0` | hang-mix modell, fade + zenei crossfade, bolygóprofil-formázás (I4), képernyőkép-nevezés |

Tesztek (Release, teljes solution): App Foundation **378/378**, Core 432/432,
viewer-LOD 384/384, CLI 8/8; build 0 warning / 0 error.

**Nem ellenőrzött / hátra (az [1]. bejegyzés idején):** Unity-ben még nem volt megnyitva (a `.meta`
fájlokat a Unity generálja, commitolni kell). U réteg (scene-ek, nézetek,
AudioMixer, Screen API) és C réteg (valós paraméterséma, generálás, szimulációs
állapot a mentésben) nincs kész. Blokkoló nyitott döntések: ND-108
(generátorverzió a Core-ban), a szimulációs állapot szerializálása, a
UI-technológia (WF-UI-009, még ND nélkül).

## [2] — Minden tőlem független desktop-munka

**Felhasználói kérés:** „desktop kapcsán van e még olyan munka amit tőlem
függetlenül végezhetsz? ha van, akkor állj neki. csak akkor szólj, hogy megvagy,
amikor már más munka nincs, csak olyan amihez én kellek” — majd: „folytasd”.

**Történt:**

- Döntések: az app-blokk ND-105–114-re bővült (a Core ND-115-től). ND-110
  UI-technológia (nyitott), ND-111 kiadási identitás (részben nyitott), ND-112
  Windows-csomagolás.
- Foundation: `AppFlowController` (a teljes menü/megerősítés/állapot/session/
  mentés/kilépés flow), `KeyBindingMap`, `ExceptionThrottle` + `ErrorReport`,
  `DebugOverlayText`, `HelpPages`, `QualityLevelMapper` (a HDRP 3 szintje ↔ 4
  fokozat), `BuildInfoCodec`, `ReleaseIdentity`, `WorldSessionHostSlot`,
  `SettingsScreenModel`, `SaveSlotRows`.
- Unity-kötés (scene-be kötés nélkül): `AppBootstrap` és a hozzá tartozó
  alkalmazók, szolgáltatások. Editor: `WorldGenBuild`. Kiadás: identitásfájl,
  portable ZIP (kamu buildön kipróbálva), Inno Setup script + wrapper (Inno nincs
  telepítve, csak a hibaágak ellenőrizve), README, QA-lista, licenc-vázlat.
- Offline fordítási kapu a helyi Unity DLL-ek ellen.
- **Hiba és javítás:** a futó Editor (a felhasználóé vagy a wg-c6 sessioné)
  importálta az új kódot, és CS0103 hibát adott, mert a projektben nincs
  ScreenCapture modul, az offline kapu viszont hivatkozta. Javítás: `abdfa7a`.
  A wg-c6 session értesítve (ne nyúljon hozzá, elég egy refresh). A kapu azóta
  csak a Unity `.rsp` szerint elérhető modulokat hivatkozza.
- A futó Editor miatt nem indítottam Unity batchmode-ot, és nem adtam hozzá
  Unity EditMode teszt-asmdef-et: egy esetleges fordítási hiba a Play módot tiltaná.
- wg-c6 (tektonikus overlay) egyeztetés: nincs átfedés. Figyelmeztetés az ND-104
  közös `SurfaceOverlayMode`-jára és az ND-számozásra.

**Eredmény:** commitok `b4a1b42`, `80b90ba`, `abdfa7a`, `6a9d6a5` + ez a docs-commit.
App Foundation **438/438**, a Unity-kötés offline kapuja 0 hiba / 0 warning.
Élő Unity-újrafordítás a javítás után: még nem igazolt (az Editor nem frissített).
