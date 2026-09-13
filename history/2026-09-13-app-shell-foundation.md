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

**Nem ellenőrzött / hátra:** Unity-ben még nem volt megnyitva (a `.meta`
fájlokat a Unity generálja, commitolni kell). U réteg (scene-ek, nézetek,
AudioMixer, Screen API) és C réteg (valós paraméterséma, generálás, szimulációs
állapot a mentésben) nincs kész. Blokkoló nyitott döntések: ND-108
(generátorverzió a Core-ban), a szimulációs állapot szerializálása, a
UI-technológia (WF-UI-009, még ND nélkül).
