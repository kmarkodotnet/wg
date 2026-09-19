# A LOD levél-költségvetés a viewportból (2026-09-19)

## Kiindulás

A feladat az volt, hogy csökkentsem a szelekció költségét, mert tegnap ezt
írtam: *"a szelekció már most 100-270 ms, és a kép 0,3-1,2 s késéssel követi
a kamerát. 16× budgettel ez több másodperces latencia lenne."*

**Ez az állítás téves volt** — lineárisan extrapoláltam mérés nélkül.
Ugyanaz a hiba, amiből a fix 8000-es budget is született (a mező tooltipje
szó szerint rögzíti: *"a mert (levelszam, ido) parokbol linearisan
extrapolalva"*).

## Mérés 1 — hová megy a selection-idő

A `PerfLog_20260918_215749.txt` 68 vágásából, a kumulatív és a per-vágás
számlálókat szétválasztva (`geometryComputed`, `ViewResets`,
`FeedbackRefreshes` a cache élettartamára összegez, csak a `metric*`
per-vágás):

| | vágás | metrikaszámítás p50 | selection p50 |
|---|---|---|---|
| hideg metrika-cache | 24 | 114 187 | 182 ms |
| meleg cache | 44 | 813 | **109 ms** |

A hideg vágások a teljes selection-idő **48%-át** viszik. A hidegség oka
megvan: `LodTerrainEvaluationCache.ResetView` **bitpontos**
`SameProjection` egyezést vár, tehát a kamera bármilyen elmozdulása törli
az egész metrika-cache-t.

De a meglepetés a második sor: **meleg cache-sel, NULLA kiértékeléssel is
109 ms.** Laborban reprodukálva (szintetikus terep-proxy, azonos nézet):

```
hideg: 191 000-213 000 kiértékelés, 482-689 ms
meleg:          0 kiértékelés,      157-183 ms
```

Vagyis a vágás **~200 000 tile-t látogat meg 8 000 levélért** — 25 látogatás
levelenként —, és ez a padló a cache-től függetlenül. A látogatásszámot a
LÁTOTT FELÜLET hajtja, nem a budget.

## Megdőlt hipotézisek (mind méréssel)

1. *"A level-3-ról induló leszállás pazarol, kezdjük a base-szinten."*
   **Nem.** `traversalRootLevel=3` → 26 ms; `=8` → 263-305 ms, mert ott a
   6×256×256 = 393 216 base-gyökeret mind fel kell sorolni. A hierarchikus
   leszállás MAGA az optimalizáció.
2. *"A budget emelése drága."* **Nem** — lásd a következő szakaszt.

## Mérés 2 — a döntő tábla

Meleg metrika-cache, azonos nézet, csak a budget változik (D=103):

| budget | levelek | selection | balance | összesen |
|---|---|---|---|---|
| 8 000 | 7 998 | 146 ms | 9 ms | **155 ms** |
| 12 000 | 12 000 | 162 ms | 13 ms | 175 ms |
| 16 000 | 15 999 | 169 ms | 19 ms | 188 ms |
| 24 000 | 24 000 | 172 ms | 26 ms | **199 ms** |
| 32 000 | 31 998 | 197 ms | 35 ms | 232 ms |
| 48 000 | 48 000 | 245 ms | 57 ms | 303 ms |
| 64 000 | 63 999 | 275 ms | 71 ms | 345 ms |

**3× budget = +28% worker-idő.** Sima skálázás, nincs törés. A 128 000-es
próbában a levélszám ~58 000-nél telítődik: ennyit kér a metrika ebben a
nézetben, tehát a 8 000 a kért részletesség **14%-a**.

## Miért nem szaggat ettől

A főszálas feltöltés az ND-85 szerint keretenként
`TerrainUploadSliceMs = 2` ms-ra szeletelt (`LodUploadBatch.StageSlice`).
Nagyobb mesh tehát **több staging-frame, nem nagyobb akadás** — a napló
`maxSlice` értékei (0,13-2,83 ms) ezt meg is erősítik. A nagyobb budget ára
latencia, nem akadás. Ez azért számít, mert a budget eredetileg épp egy
szaggatás-vadászat téves első kísérletében esett 200 000-ről 8 000-re
(df59d39); a szaggatás valódi oka a léptékcsík volt (0552bb1).

## A változtatás

`AdaptiveQuadTree.RenderBudgetForViewport(w, h, targetTilePixelSize, ...)` —
tiszta függvény: `ceil(w*h / targetPx²) * 1,5`, alul 8 000-re (a korábbi
kézi érték), felül 48 000-re vágva (a mérési tábla szerint efölött a
balance kezd dominálni).

`PlanetGridMesh.adaptiveRenderBudget` szemantikája:
- **0 (új alapérték)** = automatikus, a viewportból,
- **pozitív** = kézi felülbírálás, a régi viselkedés (Play közben
  sweepelhető marad, ahogy eddig).

A felhasználó 1196×710-es ablakában ez **8 000 → 19 903** (2,49×).

### Várt hatás a mért tile-méretekre

A levélszám 2,49×-e a lineáris tile-méretet √2,49 = 1,58-cal osztja:

| zoom-sáv | mért (8 000) | várt (19 903) |
|---|---|---|
| 30-100 | 1,40-1,69× | **0,89-1,07×** (célon) |
| 10-30 | 2,13-2,32× | 1,35-1,47× |
| 3-10 | 3,76-4,08× | 2,38-2,58× |

A legközelebbi zoom ezzel sem éri el a célt — ott ~16× levél kellene. Ez a
felső korlát tudatos ára, nem elfeledett maradék.

## Ellenőrzés

- `RenderBudgetForViewportTests`: 18 eset (a konkrét 1196×710-es munkamenet,
  pixelszám- és négyzetes skálázás, alsó/felső korlát, monotonitás,
  élesetek). Két saját számolási hibámat fogta meg: a `ceil` 13 269-et ad,
  nem 13 268-at, és a négyzetes tesztet a 8 000-es minimum elfedte volna.
- LOD-tesztcsomag: **422/422 zöld**, 14 s.
- Unity `Assembly-CSharp`: 180 hiba, mind a 8 ismert, elfogadott osztályban;
  **nulla** a módosított fájlokban.

## Ami NEM ellenőrizhető offline (rád vár)

Az emit/corners/resolveCheck és a mesh-feltöltés levélszámmal skálázódik, és
ezek Unity-függők. A 2 ms-os szelet-korlát elvben leveszi a szaggatás-
kockázatot, de ezt egy Play-menet mondja meg. A napló mostantól kiírja a
`cutBudget`-et és a `budgetMode=viewport|manual`-t minden `[tilesample]`
sorban, tehát utólag pontosan visszamérhető.

## Nyitva maradt, önállóan folytatható

1. **Hideg metrika-cache (a selection 48%-a).** A bitpontos
   `SameProjection` minden kameramozdulatnál ürít. Tűrésalapú
   újrahasználat elvben megoldaná, de az a vágást a kamera ÚTJÁTÓL tenné
   függővé — ez sérti a `SelectCutPrioritized` dokumentált garanciáját
   ("ugyanaz a (kamera, előző cut) pár MINDIG bitre ugyanazt a cutot adja"),
   tehát ND-döntést igényel, nem csendes változtatást.
2. **A 25 látogatás/levél arány.** Ez a valódi strukturális tartalék; a
   budget-emelés csak megkerüli.
