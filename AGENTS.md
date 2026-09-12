# AGENTS.md

Ez a fájl a Codex tartós projektutasítása. A részletes szakmai háttér továbbra
is a meglévő dokumentációban él; a `.claude/` tartalmát meg kell őrizni, mert
fontos történeti és munkafolyamat-kontextus.

## Session-indítás és források

Érdemi munka előtt olvasd el, ebben a sorrendben:

1. `AGENTS.md` és `CLAUDE.md`;
2. `kt_2_co2cl.md` (aktuális átadás), majd `kt_1_cl2co.md` (korábbi
   Claude → Codex átadás, beleértve a személyes Codex-üzenetet);
3. `docs/05-milestones.md` és `docs/backlog.md` aktuális részei;
4. a feladathoz tartozó részek a `docs/04-decisions.md` döntésnaplóból és a
   `docs/01-architecture.md` architektúrából;
5. szükség szerint a `docs/00-spec-v1.0.md`, a referenciaanyagok és a legutóbbi
   releváns `history/` bejegyzés.

A kézi állapotleírásokat mindig ellenőrizd a tényleges branch, commit-gráf,
kód, projektfájlok és tesztek alapján. A `README.md`, a `CLAUDE.md` Állapot
szakasza és a `docs/05-milestones.md` egyes korai sorai elavultak lehetnek.

## Architektúra és határok

- `src/WorldGen.Core`: determinisztikus, motorfüggetlen szimulációs mag;
  `netstandard2.1`, C# 9, nulla Unity/Godot referencia.
- `tools/reference`: Python referencia-orákulum és a C# tesztvektorok forrása.
- `tests/WorldGen.Core.Tests`: xUnit Core- és vektortesztjei.
- `unity/WorldGenViewer`: Unity 6 / HDRP viewer. A Core-t helyi Unity package-ként,
  forrásból hivatkozza; ne készíts másolatot róla.
- `tests/WorldGen.Viewer.LodChunking.Tests`: a UnityEngine-független viewer-LOD
  forrás linkelt, parancssori tesztje.
- `tools/WorldGen.Cli` és `tools/WorldGen.Cli.Tests`: .NET 8 CLI/perzisztencia és
  tesztjei. A gyökér `WorldGen.sln` a Core, CLI és Unity-független viewer-LOD
  projekteket, valamint mindhárom tesztprojektet tartalmazza.
- A világmodell táplálja a render-rétegeket és a panelmetrikákat. Viewer/UI
  logika nem kerülhet a Core-ba, szimulációs logika nem kerülhet a viewerbe.
- A Core-ban ne használj `System.Text.Json`-t (tesztprojektben szabad), és ne
  vezess be a támogatott Unity/C# metszeten kívüli nyelvi elemet: file-scoped
  namespace, `record`, `required` tag vagy primary constructor nem megengedett.

## Nem sérthető invariánsok

1. Azonos seed + verzió minden támogatott platformon, szálszámon és
   kiértékelési sorrendben bitazonos világot ad.
2. A random réteg tiszta és állapotmentes. Tilos a `System.Random`,
   `Guid.NewGuid`, `DateTime.Now`, `Environment.TickCount` és minden rejtett
   seed-/sorrendfüggés.
3. Minden renderelt pixel a világmodellből következik; nincs dekoratív,
   modellfüggetlen tartalom.
4. Minden panelértéknek valós modellforrása, egysége és számítási lánca van;
   nincs placeholder.

A Core fizikai értékei `double` típusúak. A kritikus úton a platformok között
nem garantált transzcendens műveletek (`Math.Log/Exp/Sin/Cos/Pow`) csak az
érintett ND-döntés explicit engedélyével használhatók; lásd ND-23b és a saját
`DeterministicMath` implementációt.

## Fejlesztési munkarend

- Dokumentáció és döntés előzze meg az új modult vagy seed-kompatibilitást
  érintő implementációt. Új architekturális kérdés kapjon új, egyedi ND-számot;
  ne szülessen csendes döntés.
- Új numerikus algoritmus sorrendje: Python referencia -> hiteles forrás/KAT
  ellenőrzés -> determinisztikus tesztvektor -> C# port -> vektorteszt.
- A Python referencia az alapértelmezett orákulum; eltérésnél előbb annak
  hitelességét ellenőrizd, és csak bizonyítékkal térj el tőle. Konstanst és
  tesztvektort ne emlékezetből adj meg.
- Seed-törő módosítás (random mapping/domain/property, Threefry, `TileId`
  layout vagy numerikus szimulációs viselkedés) verzióemelést, explicit
  betöltési hibát és döntésnapló-frissítést igényel.
- Kis, áttekinthető változtatásokat készíts. Commitot vagy push-t csak akkor
  végezz, ha a felhasználó kéri vagy jóváhagyja. A távoli repo publikus.
- A kód, az azonosítók és a commitüzenetek angolul; a dokumentáció, kommentek
  és felhasználói kommunikáció magyarul készül.
- Jelentős munka után frissítsd a releváns dokumentációt és a `history/`
  naplót. Záráskor adj tartalmilag súlyozott milestone-százalékot és világosan
  durva becslésként jelölt ráfordítás/hátralévő munkaóra-becslést, ha a feladat
  projekt-haladásról szólt.

## Tesztelés és kész-definíció

Alap ellenőrzések:

```text
dotnet build WorldGen.sln
dotnet test tests/WorldGen.Core.Tests/WorldGen.Core.Tests.csproj
dotnet test tests/WorldGen.Viewer.LodChunking.Tests/WorldGen.Viewer.LodChunking.Tests.csproj
dotnet test tools/WorldGen.Cli.Tests/WorldGen.Cli.Tests.csproj
cd tools/reference && python verify_kat.py
```

A gyökér solution a Core-, CLI- és viewer-LOD tesztassemblyt egyaránt futtatja.
Új numerikus modulnál legyen KAT (ha van referencia), ismételhetőségi,
párhuzamos/szekvenciális, paraméterhatás-, plauzibilitási és élesteszt.
A CI Debug/Release módban fut Linux x64/ARM64, Windows és macOS alatt; egy
platformeltérés determinizmushiba, nem flaky teszt.

A referencia-orákulum CI-ellenőrzése ne álljon meg a `verify_kat.py` futtatásánál:
a tesztvektorokat újra kell generálni, majd byte-szinten összevetni a repóban
verziózott `testdata/testvectors.json` fájllal. A verziózott tesztvektort kézzel
ne szerkeszd. A platformmátrixban maradjon `fail-fast: false`, hogy egyetlen
hiba ne takarja el a többi platform eredményét.

Unity-specifikus módosítást ne jelents vizuálisan késznek élő Unity Editor
ellenőrzés nélkül. A felhasználó képernyőképe, Frame Debugger eredménye és
mérése elsődleges bizonyíték. Ha nincs élő visszajelzés, ezt mondd ki. A
parancssori Core/LOD tesztek nem helyettesítik a Unity-kompilációt, scene- és
Inspector-bekötést vagy a vizuális/performance validációt.

## Aktuális átadási kockázatok (2026-09-10)

- Az aktuális `codex-handoff` és a `feature/camera-view-modes` ugyanarra a
  commitra mutat. A kamera-/tengelyforgás-mód és az első Build-optimalizálás
  még élő Unity-validációra vár; a `SunController` új `Planet Grid Mesh`
  Inspector mezőjét be kell kötni.
- Az `experiment/full-temperature-model` testvérág, nem része a jelenlegi
  ágnak. A depth-modulált, folytonos tengeri-jég blend még élő Unity-tesztre
  vár. Összefésüléskor az áganként külön létrejött két ND-62 azonosítót előbb
  fel kell oldani.
- A deep-time teljes `Build()` átadási mérés szerint kb. 24 s. A legacy
  geometriahurok kihagyása implementálva van, de új PerfLog kell; a domináns
  `BuildStaticBaseLayer` és hydrology költség, valamint a <1 s cél nyitott.
- A `core-deferred-features` két lokális committal előzi az origint; a jelenlegi
  handover/camera ág öt committal épül rá. A feature branchek nincsenek az
  originre pusholva. Branchművelet előtt mindig ellenőrizd újra a gráfot.
- Az ND-20 (Burst strict CI), ND-21 (HDRP volumetrikus felhő) és több későbbi
  ND/backlog tétel nyitott. Ne tekintsd a milestone-nevet teljes
  spec-lefedettségnek: több feature tudatosan halasztott.
