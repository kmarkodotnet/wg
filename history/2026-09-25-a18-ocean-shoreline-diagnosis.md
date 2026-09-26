# 2026-09-25 — A18: az óceán-partvonal blokkosságának gyökérok-diagnózisa

A `todo2.md` A18 sora MEGFIGYELÉSKÉNT volt felvéve: „a SZÁRAZFÖLD–ÓCEÁN határ
lépcsős, ~level-8 (36 km) léptékben", a gyanú az ND-129 (tavak) hibaosztálya,
de a **tényleges gyökérok-diagnózis hátravolt**: melyik réteg éle látszik — a
vízfelszín pereme vagy a terep-szín váltása?

**Ez a napló csak a diagnózist rögzíti; a mérés idején kód NEM változott.**
A javítás azóta elkészült: [ND-149 megvalósítás](2026-09-25-nd149-ocean-coastal-water-ring.md)
(nyers vízperem 22,34% → 0,00%).

## A válasz: a vízfelszín pereme

A terep-szín NEM tud tile-éles lépcsőt adni: a sarok-színt a
`ContinuousCornerColorAuto` a SAROK saját elevációjából vezeti le
(`isOceanic = elevation < _adaptiveSeaLevel`), a `RenderCategory.Ocean` és a
szárazföldi biome-ok is `IsContinuousTerrainCategory`-k, tehát a quad négy
sarka külön színt kap és a GPU interpolál.

A vízfelszín viszont level-8 tile-ok uniója: a `WaterLodSource.ContainsWater`
a `BaseLevel`-ig (8) sétál vissza, a statikus víz-emisszió pedig tile-onként
EGY teljes quadot rak le minden olyan tile fölé, aminek a **középpontja**
óceáni (`EmitAdaptiveTile`, `isOceanic && (biome == Ocean || SeaIce)`). A
vízlap opak, a pereme pontosan a tile-határon ér véget.

### Élő A/B (Play mód, seed `0xA7C944210000`, azonos kamera)

| kép | tartalom |
|---|---|
| `pics/a18_A_water_on.png` | ~1274 km magasság, VÍZ BE — a partvonalon tengelypárhuzamos lépcsők |
| `pics/a18_B_water_off.png` | ugyanaz a kamera, VÍZ KI — a zöld/szürke (szárazföld/óceáni kőzet) színhatár **sima, organikus**, lépcső SEHOL |
| `pics/a18_C_water_on_far.png` | ~2548 km (kontinens-nézet), VÍZ BE — a lépcsők a legfeltűnőbbek |
| `pics/a18_D_water_off_far.png` | ugyanaz, VÍZ KI |
| `pics/a18_E_water_on_close.png` | ~126 km, VÍZ BE — a lépcső MEGMARAD, csak finomabb (ld. lentebb) |

A vízrétegek (`WaterSurface`, `IndependentWater0/1`, `DynamicWater`,
`LakeSurface`) elrejtésével a lépcsők eltűnnek. Ez zárja le a kérdést: **a
látható él a vízfelszín pereme, nem a terep-szín váltása.**

## A mérés

Élő Editor, Play mód, `seaLevel = 1623,90 m`, `adaptiveBaseLevel = 8`,
`adaptiveMaxLevel = 20`. Forrás: a Build által feltöltött
`_staticTileClassifications` (393 216 level-8 tile) és `_staticCornerPositions`
(6 × 257², a sarok-eleváció a megjelenített sugárból visszaszámolva).

| | érték |
|---|---|
| level-8 tile összesen | 393 216 |
| óceáni (= vizet emittáló) tile | 256 267 (65,17%) |
| a víz-LOD forrás kimért `BaseLevel` / `WaterRootCount` | 8 / 256 267 |

**Partvonal-élek** (lapon belüli level-8 tile-élek, ahol a víz/nem-víz
besorolás vált; a lap-varratokat a statisztika nem tartalmazza):

| | darab | arány |
|---|---|---|
| összes partvonal-él | 30 150 | 100% |
| **mindkét közös sarok a vízszint ALATT** — a vízlap pereme szabadon látszik, nincs mi kivágja | **6 737** | **22,34%** |
| VEGYES (a vízszint átmetszi az élt) — a part egy része jó, a többi nyers tile-él | 16 093 | 53,38% |
| mindkét sarok a vízszint FÖLÖTT — a terep eltakarja a peremet | 7 320 | 24,28% |

**Határtile-ok:**

| | érték |
|---|---|
| szárazföld-oldali határtile | 20 223 |
| ebből ≥1 sarka a vízszint ALATT (oda a víznek be kellene érnie) | 14 217 (**70,30%**) |
| ebből MIND a 4 sarka víz alatt (egy gyűrű sem lenne elég) | **133 (0,66%)** |
| víz-oldali határtile | 19 709 |
| ebből ≥1 sarka a vízszint FÖLÖTT (ott a terep már most vág) | 14 003 (71,05%) |

## Miért nem oldja meg a meglévő víz-LOD

A `WaterLodSource.Select` GEOMETRIAILAG finomítja a vízlapot (a húr-behúrás
és a pixelcél miatt), de a KÖRVONALAT nem: a `skipStaticBase` és a
`ContainsWater` is a level-8 gyökérhalmazra néz. Ugyanennél a kameránál
(~1274 km) mérve: `leaves = 8192`, `deepestLevel = 9`, `replacedRoots = 2048`
a 256 267 gyökérből — vagyis a körvonal végig level-8 maradt.

A **finomított** terep-chunkokban a víz sub-level-8 tile-onként újra
emittálódik (`EmitAdaptiveTile` víz-ága a `!ContainsWater(id)` esetben is
lefut), és a víz-LOD is mélyebbre megy: ~126 km magasságban mérve
`deepestLevel = 13`, `replacedRoots = 43`. Ezért a lépcső a zoommal
ARÁNYOSAN ZSUGORODIK — de **nem tűnik el**: a `pics/a18_E_water_on_close.png`
partvonalán a level-13 (~1,1 km) lépcsők továbbra is látszanak. A hiba tehát
nem zoomfüggő hibaosztály, csak a lépték változik; a 36 km-es, feltűnő
változata a bolygó- és kontinens-nézet level-8 alapadatából jön, és a fenti
22,34% / 70,30% pontosan erre a szintre vonatkozik.

## Az ND-129-cel való egyezés

A tavaknál mért döntő szám ott a **0** volt (az 1-gyűrű szomszédok közül
egyetlen sem került teljesen víz alá). Az óceánnál ennek a megfelelője
**133 / 20 223 = 0,66%** — ugyanaz a kép: egy gyűrűnyi kiterjesztés az
esetek 99,34%-ában sehol nem önt el egész tile-t, tehát a valódi partvonal a
gyűrűn BELÜL van, és a már renderelt terep ki tudja vágni. A javaslat az
ND-149-ben.

## Amit a mérés NEM mond meg

- A lap-varratokon (`TileNeighbors` kereszt-lap szomszédok) futó partvonal-élek
  kimaradtak a 30 150-ből; arányuk kicsi, de nincs megmérve.
- A sarok-eleváció a level-8 rács lineáris közelítése; egy finomabb terep az
  „mindkét sarok víz alatt" élek egy részén még átmetszhetné a vízszintet.
- A javítás költsége (tile-szám, ms) nincs megmérve — az az implementáció
  feladata.

## Mérési módszer megjegyzés

A Unity Editor háttérben NEM léptette a player loopot (`Time.frameCount` 2-n
állt), ezért a kamera és a LOD nem frissült, a `capture_game_view` viszont
így is renderelt — egy ideig „a kamera nem mozdul" tünetet adott.
`Application.runInBackground = true` oldotta meg. Élő, LOD-függő vizuális
méréshez ezt a jövőben ELSŐ lépésként érdemes beállítani.
