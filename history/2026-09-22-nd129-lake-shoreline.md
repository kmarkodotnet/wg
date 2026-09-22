# 2026-09-22 — ND-129: a tó-partvonal blokkosságának megszüntetése (A9)

Felhasználói kérés: *„szeretném, hogy a tavak, ha zoomolok, ne úgy nézzenek ki
mint ahogy most, nagy tile-okból összerakottak, hanem szeretném ha zoomra
részletesebb lenne a partvonaluk"*. A `todo2.md` A9 sora.

## A diagnózis — és ami menet közben megfordította a tervet

Az első terv **marching squares** volt: a tó-tile-okon al-rácson mintavételezni
a terepet, és a vízszint-szintvonalat interpolálva kivágni a vízfelületet.
Ezt két mérés söpörte el:

1. **A `ComputeElevationAtPoint` ~53 µs/hívás** (a `history/2026-09-20-flyto-
   wind-overlay-split-quota.md` korábbi élő mérése: 4 hívás = 212 µs). A
   11 505 tó-tile egyenletes level-12 rácsa 2,9 M hívás ≈ **2,5 perc CPU**.
2. **A partvonal MÁR most is terep-metszet — csak ritkán.** Élő Editorban
   mérve a legnagyobb tó (404 tile) sarkainak **10,3%-a** a vízfelszín FÖLÖTT
   van, tehát ott a terep a z-bufferrel kivágja a vizet. A maradék ~90% azért
   nyers tile-él, mert a vízfelszín a tile-határon egyszerűen VÉGET ÉR.

Innen adódott a jóval olcsóbb és pontosabb megoldás: **ne új kontúrt
számoljunk, hanem hagyjuk, hogy a már renderelt terep vágja ki a partot** — a
vízfelszínt kell túlnyújtani a valódi parton.

## A mérések, amikre a döntés épül

Seed `0xA7C944210000`, `hydrologyLevel=8`, `adaptiveBaseLevel=8`, `level=5`.

| | érték |
|---|---|
| tó-tile összesen (level 8, ~36 km oldal) | 11 505 |
| legnagyobb tó | 404 tile, felszín 1975,7 m |
| tó-tile sarkok a vízfelszín FÖLÖTT | 10,3% (166/1616) |
| tó-tile KÖZEPEK a vízfelszín fölött | 0 |
| a vízszinthez ±25 m-en belüli sarkok | 14,8% (239/1616) |
| 1-gyűrű szomszéd, csupa víz alatti sarokkal | **0** (0/146) |
| 1-gyűrű szomszéd, VEGYES | 78,1% (114/146) |

A döntő sor a **0**: egy gyűrűnyi kiterjesztés sehol nem önt el egész tile-t,
tehát a valódi partvonal a gyűrűn BELÜL van.

## Amit csinál (`PlanetGridMesh.BuildLakeSurface`)

1. **1 gyűrű kiterjesztés** a tó-tile-ok köré, a tó saját vízszintjén
   (több tóhoz érő gyűrű-tile a LEGALACSONYABBAT kapja — sorrendfüggetlen).
2. **Kihagyás**: az a tile, aminek mind a négy sarka a vízszint fölött van,
   ki sem kerül (a terep úgyis eltakarná). Mérve 2479 tile (11,9%).
3. **N=4 al-osztás** a part menti tile-okon. Indok: egy level-8 lapos quad a
   gömb húrja, a közepe `R·θ²/8 ≈ 25 m`-rel a helyes sugár alatt van, és a
   tó-sarkok 14,8%-a ezen a sávon belül esik. N=4-nél a behúrás **1,6 m**.
   Ahol mind a négy sarok legalább 40 m-rel a vízszint alatt van, a tile
   egyetlen quad marad.
4. **T-csomópont-kezelés**: al-osztott tile durva szomszéd felé eső élének
   belső pontjai a húrra esnek (`SnapEdgeToChord`), így nincs hajszálrés.
5. A sarok-elevációk a **ND-63/ND-122 lemez-cache-elt level-8 sarok-bázisból**
   jönnek — mérve **28 844 mintából 28 844 cache-találat, 0 tévesztés**, tehát
   nincs új `TerrainPointBasis.Compute`.

## Amit MEGMÉRTÜNK és ELVETETTÜNK

A gyűrű **iteratív** terjesztése (csak a teljesen víz alá került tile-ok
körül, 4 körig) élőben: +528 tile (+2,5%), idő 724 → 1054 ms, **de a nyitott
határ nem fogyott, hanem nőtt (129 → 154)**. Oka: a LEFOLYÁSNÁL a völgytalp
hosszan a feltöltési szint alatt marad, tehát a tó a folyó medrében
„elfolyik". Ezért a konstans **1** maradt; a maradék 129 gyűrű-tile (az első
gyűrű 1,4%-a) továbbra is nyers tile-él — ez a vizuális ítélet (B3) tárgya.

## Perf

| | |
|---|---|
| `BuildLakeSurface` (1 gyűrű, lista-előfoglalás nélkül) | 724 ms |
| ugyanaz **pontos kapacitás-előfoglalással** | **477 ms** |
| rajzolt tile / quad / csúcs | 18 307 / 240 202 / 960 808 |

A csúcsszám a besorolás után pontosan ismert, ezért a négy `List<T>` előre
foglal — egy ~1 M csúcsos mesh-nél a duplázgatás önmagában tíz MB-os
újramásolás volt.

## Tanulságok

1. **Mielőtt új számítást tervezel, nézd meg, mi van MÁR kiszámolva.** A
   partvonalhoz nem kellett egyetlen új eleváció-hívás sem: a terep-mesh
   pontosan ezt az információt hordozza, és level 20-ig finomodik — vagyis a
   „zoomra részletesebb" követelmény *ingyen* teljesül.
2. **Egy régi mérés (53 µs/hívás) egy másik feladatból döntötte el ezt a
   tervet.** A `history/` napló emiatt fizet ki magát.
3. **A „terjesszük tovább, amíg elfogy" ötletet meg kell mérni, nem
   elképzelni** — itt nem konvergált, és ezt csak a futtatás mutatta meg.

## Nyitott, ND-129-ben dokumentálva

- A `RenderCategory.Lake` adaptív ága **halott kód**: az `IsAdaptiveLakeTile`
  a megjelenítési `level`-ig (5) sétál vissza, a halmaz level-8-as → sosem
  talál. Ártalmatlan (az opak vízfelszín takar), de takarítandó.
- A `LakeInfo.SurfaceElevation` a komponens `filled` értékeinek ÁTLAGA; több,
  eltérő szintű mélyedésből összeolvadt tónál a per-tile `filled` lenne a
  helyes vízszint. A `MinSurface`/`MaxSurface` szórása mérendő.
