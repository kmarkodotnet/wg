# 2026-09-05 — teljesítmény-optimalizálás + 5 backlog-tétel (branch: core-deferred-features)

Interaktív session (nem autonóm menet) — a felhasználó folyamatosan jelen volt,
konkrét visszajelzéseket adott lépésről lépésre.

## 1. Deep-time Gyr kézi bevitel (viewer UX)

A felhasználó kérte, hogy a deep-time csúszka mellé kerüljön egy szöveges
mező, ahova milliárd évben (Gyr) be lehet írni az időt. Két iteráció:
- Első verzió: élőben alkalmazta a beírt szöveget minden gépelésre (rossz -
  minden részprefixnél újraépítette a világot).
- Javítva: a mező csak Enter lenyomására VAGY egy "Alkalmaz" gombra
  alkalmazza az értéket (`GUI.SetNextControlName` + `Event.current.keyCode`).

## 2. Build() teljesítmény: 60s → 15s (felhasználó mérte)

A felhasználó jelezte, hogy egy deep-time (Gyr) újraszámolás ~1 percig tart.
Kód-olvasással (nem feltételezésekkel) azonosítottam a valós szűk
keresztmetszeteket:

- **`SeaLevelCalibration.ComputeElevationFieldWithSeeds`** (Core) - a fő
  elevációmező-számítás `level`-en ÉS a `hydrologyLevel`-en (a scene-ben
  hydrologyLevel=8, azaz 393k tile!) EGYSZÁLÚ volt. `Parallel.For`-ral
  párhuzamosítva: mért gyorsulás level=7-nél 4054ms→269ms (15×), level
  8-nál 6947ms→1019ms - és egy külön harness-szel igazolva, hogy BITRE
  AZONOS eredményt ad (determinizmus megőrizve).
- **`ApplyDeepTimeErosionToField`** (viewer) - ugyanez a minta, a scene-ben
  `showDeepTimeErosion=1`, tehát ez is 393k tile-on futott egyszálúan.
- **`ImpactCratering.ApplyToField`** (Core) - O(tile × kráterszám), szintén
  párhuzamosítva.
- **Redundáns dupla kráter-ciklus megszüntetve**: a tile-klasszifikáció
  KÉTSZER járta végig a kráter-listát ugyanarra a pontra (`ElevationDelta`
  ÉS külön `IsInsideAnyCrater`). Az `ElevationDelta`-nak lett egy
  `out bool insideAny` overloadja, egy menetben adja mindkettőt.
- Részletes `PerfLog`/`Stopwatch` instrumentáció a `Build()` MINDEN
  fázisára (seeds+craters, elevation+erózió, tengerszint, hidrológia, jég,
  [eldobott] legacy geometria, csapadék, `BuildStaticBaseLayer`, adaptív
  cut, folyó+tó, TELJES) - a `Logs/PerfLog_*.txt`-ben mostantól pontosan
  látszik, melyik fázis mennyit visz el.

**Felhasználói visszajelzés a menet közben**: 60s → 15s (mérve). A
felhasználó jelezte, hogy ezen még bőven kell optimalizálni - a
`PriorityFlood` gráf-algoritmus (393k tile-on, `SortedSet`-alapú, inherensen
szekvenciális) a fő gyanúsított a maradék időre, de ehhez a következő
PerfLog-kimenet kell a pontos diagnózishoz (nem vak további optimalizálás).

**Ellenőrzés**: `dotnet test` a Core-suite-on minden lépés után zöld (a
végén 332/332); egy külön throwaway harness igazolta szekvenciális vs.
párhuzamos BITPONTOS egyezést a `ComputeElevationFieldAtTime`-ra.

## 3. Öt backlog-tétel, amihez a felhasználó nem kellett (Kellek=Nem)

A felhasználó kérte: "kezdj öt olyan feladatot a backlogból, amihez nekem
nem kell jelen lennem". Az öt, `Kellek?=Nem` jelölésű tétel:

1. **M12 checkpoint-rendszer + `.worldpkg`** - `tools/WorldGen.Cli/
   WorldPackage.cs`: a fájl a VILÁG-DEFINÍCIÓT (seed/plateCount/level/
   deepTimeMyr) menti JSON-ban + a mentéskori `WorldStateHash`-t (ND-30
   indoklása szerint: a Core tiszta függvénye a paramétereknek, nincs mit
   event-sourcing-olni). `worldgen checkpoint save/verify` parancsok.
2. **M12 `worldgen verify` CLI** - `tools/WorldGen.Cli/Program.cs`:
   `worldgen hash`/`worldgen verify` - seed+paraméterek → SHA-256, ill.
   összevetés egy elvárt hash-sel (I1 automatizált ellenőrzése).
3. **M8 Habitability/Coastal complexity panelre kötés** - a
   `WorldGenPanelData`/`ComputePanelData`/`WorldGenPanelUI` kiegészítve;
   MINDKETTŐ az ND-09 kalibrált ordinális sávjával együtt jelenik meg.
4. **M8 morfológiai típus névbe+panelbe kötése** - `NameGeneration.
   GenerateName` új, landform-tudatos overloadja (NEM seed-törő - a régi
   3-paraméteres hívók bitre változatlanok, 300/300 Python-vektor zöld
   marad) a régiónév utótagját a felismert típusra cseréli ("... Range"
   hegyvidéknél stb., docs/01-architecture.md §2.3 mintája szerint).
5. **M8 ND-09 ordinális kalibráció v1** - `src/WorldGen.Core/Features/
   OrdinalQuantization.cs` + `tools/WorldGen.Cli/OrdinalCalibration.cs`:
   N=500 világ (seed=1..500, plateCount=20, level=6, water=0.65,
   Föld-analóg klíma) kvintilis-eloszlásából kalibrált 4-4 küszöb a
   Habitability-re és Coastal complexity-re. Csak ez a 2 mező - a spec
   többi ordinális mezője továbbra is blokkolt (nincs alattuk metrika).

Mind az 5 tétel Python-referencia NÉLKÜL készült (mint a többi M8
aggregáció) - tiszta statisztikai/geometriai számítás a MÁR verifikált
Core-metrikákon, nem új szimulációs algoritmus.

## Állapot

Core-suite: **332/332 zöld** (290 baseline + 13 korábbi session + 22 ND-09 +
7 landform). CLI-suite (`tools/WorldGen.Cli.Tests`): **7/7 zöld**. A
viewer-oldali `dotnet build` ellenőrzés egy ISMERT KORLÁTBA ütközött: a
Unity-generált `unity/WorldGenViewer/WorldGen.Core.csproj` STALE (explicit
fájllistás, nem globol) - az ÚJ `OrdinalQuantization.cs`-t nem látja, amíg
a Unity Editor újra nem szinkronizálja a projekt-fájlokat (ami automatikus,
amint a felhasználó megnyitja) - ez NEM valódi hiba, csak egy offline
ellenőrzési vakfolt.

**Commitolatlan** - a felhasználó explicit jóváhagyása nélkül nem
commitoltunk (ld. munkamódszer). Backlog-higiénia elvégezve (`docs/
backlog.md`), a téves 2026-09-06 dátumjelölések 2026-09-05-re javítva.

## Hátra

- PriorityFlood/FlowAccumulation optimalizálása a friss PerfLog-adatok
  alapján (felhasználói visszajelzés kell a pontos bontáshoz).
- A most elkészült 3 panel-mező (Habitability, Coastal complexity,
  Landform) + a Gyr-mező vizuális ellenőrzése Unityben.
- Commit, ha a felhasználó jóváhagyja.
