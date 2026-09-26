# A8/1 — Mérési alap és döntési kapu

## Cél

Megállapítani, hogy a folyóvonalakra várakozás ténylegesen zavaró-e, és a kör-alapú feldolgozásnak van-e bizonyítható gyorsulási lehetősége a jelenlegi, bitazonos hálózat megtartásával. Ez önálló diagnosztikai session; még nem változtatja meg a folyóalgoritmust.

## Kiindulás és olvasnivaló

- `AGENTS.md`, `CLAUDE.md`, `kt_2_co2cl.md`, `kt_1_cl2co.md`; ellenőrizd a branch-et és a munkafát.
- `todo2.md` A8 és B3; `docs/04-decisions.md` ND-124 és ND-132; `history/2026-09-22-nd132-river-scheduling.md`.
- `src/WorldGen.Core/Hydrology/RiverPathTracing.cs`: `SelectRiverSourcesPerBasin`, a szekvenciális és párhuzamos folytonos hálózatépítő.
- `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`: `StartRiverRefinement`, `TryApplyCompletedRiverRefinement`.

Az A8 szövegében szereplő ~30 s az ND-124 idején mért párhuzamos 16×6 próba, nem a jelenlegi viewer szekvenciális háttérfeladatának igazolt ideje. Az ND-132 óta a viewer `LongRunning` feladaton, a `Build()` után futtatja a szekvenciális Core-utat; a régi párhuzamos API csak offline elérhető.

## Teendők

1. Reprodukálható, aktuális alapvonal: azonos seed, világidő, `riverRefinementStepMeters`, 16×6 forrás és hardver mellett mérd külön a `Build() TELJES`, `[ND-132 river start]`, `[ND-132 river ready]` és `[ND-132 river mesh]` időt. Hideg és meleg futás, valamint egy deep-time váltás legyen benne. Jelöld a megszakított meneteket; ne keverd őket a kész hálózat idejébe.
2. Rögzítsd a pontos forráslistát és a hálózat összehasonlítható lenyomatát: forrásindex, befejezési ok, `MergedIntoRiverIndex`, pontok koordinátái és `ComputeDischargeWeights`. Az összehasonlítás numerikus része bitpontos legyen; a megjelenést külön kezeld.
3. Profilozd a költséget forrásonként és medencénként: nyomkövetési idő, pontok/lépések, pit-escape hívások és az a pont, ahol az adott folyó a korábban lefoglalt főághoz csatlakozik. A mérés ne változtassa meg a számítás sorrendjét vagy eredményét.
4. Vizsgáld meg a B3 élő megfigyelését: mennyi idő után látható a kész hálózat, és a felhasználó számára ez valóban várakozási probléma-e. Ha nincs élő Unity hozzáférés, a hiányzó bizonyítékot nevezd meg, és az offline mérést külön jelöld.
5. Készíts döntési jegyzőkönyvet: mekkora a számítás ideje, mennyi munka végezhető el korábbi források `claimed` térképe nélkül, és érdemes-e folytatni. Ha azonos kimenet mellett nincs értelmes gyorsulási út, A8 lezárható indokoltan implementáció nélkül.

## Kimenet és kész feltétel

- Dátumozott mérési napló a `history/` alatt, pontos konfigurációval és nyers PerfLog-hivatkozással.
- Rövid, számszerű **folytatás / elvetés** döntés. A 2. rész csak folytatás esetén indul.
- Nem állítunk gyorsulást a `Build()` idejének csökkenéséből: itt a folyóhálózat teljes elkészülési ideje a fő mérőszám.

## Átadás a következő sessionnek

Add át a baseline lenyomatot és a mérési parancsokat, az érintett commit/branch állapotot, valamint a legdrágább források listáját. Commit és push csak külön felhasználói kérésre.
