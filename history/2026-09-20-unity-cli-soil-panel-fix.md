# 2026-09-20 — Unity CLI első éles használat: a talaj-panelsor hibája

## Mi volt a feladat

Egyszerű, bemelegítő feladat az újonnan bekötött Unity CLI-re (`unity-editor-mcp`,
151 tool). A todo.md #2 sorát vettem elő: „Talaj-termékenység panelsor — **nem
látom**" (felhasználói visszajelzés).

## Menet

1. `editor_status` + `get_serialized_fields` — a `PlanetView` jelenet nyitva,
   `adaptiveRenderBudget = 0` (a korábbi jelenet-szerkesztésem érvényes),
   `showLakesIce = true`, `showRivers = true`, konzol: **0 error**.
2. `editor_play` → Play mód. `eval`-lal kiolvastam a `_navPanelData.Regions`
   listát: **volt bennük talajérték** (0.1955–0.2786, sávnevekkel). Tehát a
   számítás jó.
3. Reflexióval lenavigáltam kontinens-szintre: a
   `GetRegionsForContinentCached` ugyanazokra a régiókra `soil=NULL`-t adott.
4. **Gyökérok:** a `RegionPanelData` **két külön helyen** épül:
   - `ComputePanelData` (~5805. sor) — a top-10 lista, ezt patcheltem ND-117-nél;
   - `BuildRegionPanelDataFromTiles` (~6000. sor) — a **lusta** építő, amit a
     navigációs menü használ. Ezt soha nem patcheltem.
   A lusta út jóval a panel-számítás után fut, tehát nem tudja újra elvégezni a
   statikus eróziós passzt.
5. Javítás: `ComputePanelData` elteszi az eredményt egy `_lastSoilErosion`
   mezőbe, a lusta út onnan olvassa.
6. `recompile` → **0 error**. Újra Play, `eval`:
   `Vermar Coast soil=0.2728 Exceptional / Rinum Coast soil=0.2786 Exceptional /
   Verhal Glacier soil=0.1967 Moderate`.
7. `capture_game_view` → a Régió-panelen látszik: **„Talaj-termékenység: 0.273
   (Exceptional)"**.

Commit: `4089f4f`. Tesztek: Core 478 / LodChunking 443 / App.Foundation 438,
mind zöld.

## Tanulságok

**A statikus olvasás nem találta meg.** Amikor ND-117-nél végigkerestem, hol
épül a `RegionPanelData`, megtaláltam az egyik helyet és megálltam. Az élő
Editor 3 `eval` hívás alatt megmutatta az ellentmondást (top-10 lista: van
érték; menü: null), ami azonnal kizárta a számítási hibát és a *megjelenítő*
útra mutatott. **Ha egy panelmező „nem látszik", először azt kell eldönteni,
hogy a számítás hiányzik-e vagy az adatút — erre az élő állapot lekérdezése a
leggyorsabb eszköz.**

**Második csapda: állott képernyőkép.** A Game view nem rajzol újra, amíg az
Editor háttérben van, így az első `capture_game_view` a Bolygó-szintet mutatta,
miközben az `eval` szerint a nav-állapot már Régió volt. `editor_focus` után
lett jó a kép. **Képernyőképet soha ne fogadj el bizonyítéknak `eval`-os
állapot-ellenőrzés nélkül.**

## Nyitva maradt (ugyanebből a körből)

- **#1 split-kvóta** — a 10–30 zoom sávban nem a budget a szűk keresztmetszet,
  hanem a `NewSplitsPerRequest = 1024` (10357 split elutasítva). Méréshez
  `private const` → `SerializeField` kell.
- **#9 szél-overlay fagyás** — a `WindSpeedColorAt` sarkonként **4 teljes**
  `ComputeElevationAtPoint`-ot hív; a budget-emelésem 5×-özte a sarokszámot.
- **#3 FlyTo forgáshiba** — a `NavigateToContinent/Region/Area` nyers
  `CenterDirection`-t (bolygó-LOKÁLIS) ad a világtérben dolgozó
  `FlyToDirection`-nek; `transform.TransformDirection` hiányzik 3 helyen.
