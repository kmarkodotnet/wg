# 2026-09-13 — Rotáció/zoom szaggatás: az adaptív LOD-cut korlátlan növekedése

## Kérés

A felhasználó jelezte: "megint elképesztően lassú a grafika, a rotációra és a
zoomra is szaggat", és kérte az ok kivizsgálását, szükség esetén logolással.

## Vizsgálat menete

1. Átnéztem a `history/RETROSPECTIVE-2026-09-02-adaptive-lod-saga.md`-t —
   ugyanez a tünetosztály (rotáció/zoom szaggatás) korábban már 4 diagnózis-
   kört igényelt, és a #1 tanulság explicit: **élő mérés kell, ne csak
   kódelemzés**, mielőtt bármit "megoldottnak" nyilvánítunk.
2. Első hipotézis (később másodlagosnak bizonyult): a friss (ma reggeli,
   ND-84) fizikai léptékcsík (`PlanetOrbitCamera.ScaleBar.cs`) minden
   `scaleBarRefreshIntervalSeconds` (0.15s) alkalommal akár ~100-200
   `ComputeElevationAtPoint`-hívást futtat (`TryFindMeasurableWidth` +
   `TrySolveScale` beágyazott felezései). Ehhez ideiglenes diagnosztikai
   naplózást adtam (`[ND-84 scale diag] recomputeMs=... evalCount=...`,
   `PlanetOrbitCamera.ScaleBar.cs` + `PlanetGridMesh.ScaleSurface.cs`,
   `ScaleSurfaceEvaluationCount` számláló). Ez a naplózás a helyén maradt,
   de a MÉRÉS (lásd lent) nem ezt igazolta fő okként.
3. A `unity/WorldGenViewer/Logs/PerfLog_20260913_184215.txt` (a felhasználó
   AKTUÁLIS, a panasz idejéből származó munkamenete) már tartalmazott elég
   adatot a valódi ok azonosításához, ÉLŐ mérésből, új session nélkül:
   - `[ND-95 request timing]`: a `totalMs` a munkamenet elején 100-350ms,
     majd folyamatosan nő 1300-1900ms-ig.
   - `[ND-93 terrain cache]`: `targets`/`ownedMeshes` 0-ról ~20-ra, majd
     folyamatosan ~1200-1300-ra nő a munkamenet során (`limit=128` csak az
     INAKTÍV kulcsokat korlátozza, az aktív halmazt nem).
   - `cut.Count=`/`dynLeaves=` (grep az egész fájlon): **52 → 154 → 571 →
     ... → 30 448 → 32 795** - a dinamikus LOD-levélszám két nagyságrendet
     nő a munkamenet alatt, alkalmankénti visszaeséssel, de az ÖSSZKÉP
     folyamatos növekedés.

## Gyökérok

A `AdaptiveQuadTree.SelectCutPrioritized` (`Lod/AdaptiveQuadTree.cs:323`)
hiszterézist használ: egy csempe, ami az ELŐZŐ cutban már finomítva volt
(`previousExpanded`), alacsonyabb `mergeThresholdRadians` küszöbbel marad
finom, mint amivel egy ÚJ csempe split-elődne (`splitThresholdRadians`) -
ez szándékos, a villogás elleni védelem. A tényleges korlát a
`adaptiveRenderBudget` (`PlanetGridMesh.cs:534`, alapérték **200 000**) -
ez csak akkor kezdi durvábban hagyni a periférikus részt, ha a cut MÁR
elérte ezt a méretet.

A `PlanetGridMesh.cs:3500` körüli chunk-csoportosító ciklus (`newGroups`
bejárása `ComputeAdaptiveMeshBuffersCpu`-ban) MINDEN csoportra lefuttatja a
`CaptureResolvedPositions` + `SamePositions` ellenőrzést - ÉS a VÁLTOZATLAN
csoportokra is -, tehát a rebuild ára a TELJES cut méretével arányos, nem a
ténylegesen megváltozott résszel. Ahogy a felhasználó forgatja/zoomolja a
nézetet, egyre több, korábban belátott régió marad "ragadósan" finom
(hiszterézis), a cut mérete lépésről lépésre nő, és minden EGYES következő
frissítés egyre TÖBB munkát végez - ez magyarázza, hogy a szaggatás a
munkamenet előrehaladtával rosszabbodik ("megint" - ismerős minta).

A `adaptiveRenderBudget=200000` messze a jelenleg megfigyelt 30-33 ezres
tartomány fölött van, tehát a beépített védőháló ezen a munkameneten
GYAKORLATILAG NEM lépett működésbe - a cut szabadon nőhetett a
megfigyelt méretig.

## Mellékes, MÁSODLAGOS lelet (nem a fő ok, de valós felesleges költség)

A fizikai léptékcsík (ND-84, 2026-09-13 reggeli munka) minden 0.15
másodpercben lefuttatja a felezéses keresést, KAMERAMOZGÁSTÓL FÜGGETLENÜL -
ez a mai napon hozzáadott, viszonylag friss kód, önmagában is felesleges
CPU-terhelés, de a mértéke (max néhány száz felszín-mintavétel/hívás) a
mostani mérésben nem magyarázza a megfigyelt másodperces késéseket. Az
ideiglenes `[ND-84 scale diag]` naplózás a helyén maradt egy következő
körhöz, ha külön is látni akarjuk a tényleges költségét.

## Mit NEM csináltam

Nem nyúltam az `adaptiveRenderBudget` értékéhez vagy a hiszterézis
küszöbökhöz - ez a projekt saját, ismételten dokumentált tanulsága szerint
(retrospektíva #1) csak ÉLŐ Unity Editor-méréssel verifikálható javítás, és
egy ilyen paraméterváltoztatás vizuális/teljesítmény kompromisszumot érint,
amit a felhasználónak érdemes jóváhagynia. A kérdést visszaadtam neki.

## Következő lépés (felhasználói döntésre vár)

- `adaptiveRenderBudget` csökkentése (pl. 200 000 → 15-20 000) kísérletként,
  élő teszttel, VAGY
- a `newGroups` bejárásban a változatlan csoportok kiszűrése MÉG a
  `CaptureResolvedPositions`/`SamePositions` ellenőrzés előtt (a
  `DynamicMeshChunking.DiffChunks` már tudja, mi változott -
  `diff.ChangedOrNewChunks` -, ez elvben olcsóbbá tehetné a nem-változó
  többséget anélkül, hogy a hiszterézist/részletességet csökkentenénk).

Egyik irány sem lett implementálva ebben a körben - csak diagnosztizálva.
Nincs commit.
