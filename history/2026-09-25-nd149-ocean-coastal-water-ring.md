# 2026-09-25 — ND-149: az óceán-partvonal blokkosságának megszüntetése (A18)

A diagnózis külön naplóban:
[`2026-09-25-a18-ocean-shoreline-diagnosis.md`](2026-09-25-a18-ocean-shoreline-diagnosis.md).
Röviden: a látható lépcsőt a **vízfelszín pereme** adta, nem a terep-szín
váltása — a vízlap azoknak a level-8 tile-oknak az uniója volt, amelyeknek a
KÖZÉPPONTJA óceáni, és opak quadként pontosan a tile-határon ért véget.

## Amit csinál

A vízfelszín tile-halmaza mostantól **óceáni tile-ok + parti gyűrű**, és a már
renderelt, adaptív terep vágja ki belőle a partvonalat a z-bufferrel. Nulla új
eleváció-kiértékelés: a maszk kizárólag a Build által MÁR kiszámolt level-8
sarok-pozíciók sugarát olvassa.

1. **1. kör (párhuzamos):** minden nem-vizes base-tile, aminek van vizet
   emittáló 4-szomszédja. Kimarad az, aminek MIND a négy sarka a vízszint
   FÖLÖTT van (ott a terep úgyis eltakarná a vízlapot).
2. **További körök ÉLENKÉNT:** egy él akkor és csak akkor marad nyers
   vízperem, ha a KÉT KÖZÖS SARKA MINDKETTŐ a vízszint alatt van — a gyűrű
   pontosan ezeken lép tovább. Korlát: `CoastalWaterRingMaxExpansions = 4`.
3. A szomszéd-keresés a lap belsejében tiszta index-aritmetika, és CSAK a
   lap-éleken (a tile-ok ~1,6%-a) hívja a vetítéses `TileNeighbors.Neighbor`-t.

### Miért konvergál — és miért nem konvergált az ND-129-nél

Az óceán vízszintje **globális és állandó**, ezért a terjesztés a valódi
partvonalnál magától megáll. A tavaknál (ND-129) a feltöltési szint tavanként
más, és a LEFOLYÁSNÁL a völgytalp hosszan a szint alatt marad — ott a
terjesztés nem konvergált, ezért maradt 1 kör. Ez a döntő különbség a két
javítás között.

MÉRVE: az első, tile-alapú változat („csak a TELJESEN elöntött tile-ok körül
terjedj") 12 nyers élt hagyott (0,05%). Az élenkénti szabály **0-t**.

### Miért nem kell al-osztás (a másik eltérés az ND-129-től)

A terep-quad és a víz-quad **ugyanazon az UV-négyszögön, ugyanazzal a
háromszögeléssel** (`AddQuad` p00,p10,p11,p01) fekszik, gyakorlatilag azonos
sugáron. A level-8 lapos quad ~25 m-es húr-behúrása tehát MINDKETTŐBŐL kiesik
a különbségképzésnél: a metszésvonal pontosan a sarok-elevációk lineárisan
interpolált tengerszint-kontúrja — vagyis ugyanaz a határ, amit a terep
sarok-SZÍNE (`ContinuousCornerColorAuto`) már ma is folytonosan kirajzol.
A tavaknál a vízszint tavanként külön sugár, ezért ott kellett az N=4.

## Mérés

Seed `0xA7C944210000`, `adaptiveBaseLevel = 8`, `seaLevel = 1623,90 m`.
Ugyanaz a level-8 partvonal-él statisztika, mint a diagnózisban (lapon belüli
élek; a lap-varratok kimaradtak).

| | ND-149 előtt | ND-149 után |
|---|---|---|
| partvonal-él összesen | 30 150 | 26 038 |
| **nyers vízperem** (mindkét közös sarok víz alatt) | 6 737 — **22,34%** | **0 — 0,00%** |
| vegyes (a terep átmetszi az élt) | 16 093 — 53,38% | 1 372 — 5,27% |
| a terep teljesen eltakarja a peremet | 7 320 — 24,28% | 24 666 — **94,73%** |
| vizet emittáló level-8 tile | 256 267 (65,17%) | 270 535 (68,80%) |

`[ND-149 coastal ring] enabled=True drawn=14268 skippedAboveWater=6032
rounds=4 openBoundary=0 maxExpansions=4 mask=3,7–15,5ms`

### Vizuális A/B (kontrollált: azonos kamera, azonos fény, kapcsoló → `Build()` → azonos várakozás)

| kép | tartalom |
|---|---|
| `pics/a18_FIX_ring_off_far.png` | ~2548 km, gyűrű KI — lépcsős part |
| `pics/a18_FIX_ring_on_far.png` | ~2548 km, gyűrű BE — sima part |
| `pics/a18_FIX_ring_off.png` | ~1274 km, gyűrű KI |
| `pics/a18_FIX_ring_on.png` | ~1274 km, gyűrű BE |
| `pics/a18_FIX_ring_on_close.png` | ~126 km, gyűrű BE (a ~1,1 km-es lépcsők is eltűntek) |

Pixel-eltérés a kontrollált párokon: **2,59%** (2548 km) és **1,06%**
(1274 km) — vagyis egy vékony partvonal-sáv az egész látható féltekén, nem
globális színváltozás. A korábbi, ND-149 ELŐTTI közeli kép
(`a18_E_water_on_close.png`) és az új közeli kép között 0,94%.

## Költség

10+ Build, mediánok:

| | gyűrű KI | gyűrű BE |
|---|---|---|
| maszk (`ComputeCoastalWaterRing`) | 0 ms | **4,2 ms** |
| `emit` fázis | 262 ms | 264 ms |
| `BuildStaticBaseLayer` összesen | 1333 ms | 1394 ms (**+61 ms**) |

**KIMONDVA: a +61 ms a zaj nagyságrendjében van.** Ugyanazzal a beállítással a
`water` fázis 53–344 ms, a `mesh` 143–411 ms között ingadozott — a fázismérés
szórása nagyobb, mint a mért különbség. Ami biztosan a gyűrűé: a 4,2 ms-os
maszk és a víz-mesh +5,57%-nyi quadja. A teljes Build ~2,4 s nagyságrendje nem
változott.

## Érintett kód

- **ÚJ:** `unity/.../Viewer/Lod/CoastalWaterRing.cs` — a két TISZTA döntés
  (`Classify`, `EdgeFullyBelowWater`), az `OceanRefinement` mintájára: nulla
  UnityEngine-referencia, tehát Unity Editor NÉLKÜL is tesztelhető. A `.meta`
  az `AssetDatabase.Refresh` után elkészült és commitolandó.
- **ÚJ:** `tests/WorldGen.Viewer.LodChunking.Tests/CoastalWaterRingTests.cs` —
  23 esetet fed: besorolás (száraz / vegyes / teljesen elöntött), az
  él → sarokpár leképezés mind a négy irányra, a többi irány NEGATÍV
  ellenőrzése ugyanazokkal a sarok-mintázatokkal (ez fogná meg, ha az él →
  sarokpár leképezés elcsúszna), és az élesetek (teljesen elöntött tile mind a
  négy éle nyitott; száraz tile egyiké sem, tehát a gyűrű nem tud száraz
  tile-on át terjedni). **526/526 zöld** a LodChunking tesztprojektben.
- `PlanetGridMesh.cs` — `coastalWaterRing` kapcsoló (Inspector, alapból BE),
  `_staticCoastalWaterRing` maszk, `ComputeCoastalWaterRing`,
  `ClassifyCoastalRingTile`, `SharedEdgeFullyBelowWater`,
  `BaseNeighborDenseIndex`, `StaticTileEmitsWater`, `StaticWaterDepthBucket`;
  `CreateStaticMeshBuckets` és `EmitAdaptiveTile` víz-feltétele.
- `PlanetGridMesh.StaticEmit.cs` — a párhuzamos emit víz-particionálása
  ugyanarra a predikátumra állítva (különben a bucket-kapacitás és az emit
  elcsúszna).

A gyűrű CSAK a statikus base-rétegre (level 8) hat: a finomított chunkok
`staticDenseIndex`-e −1, ott a víz tile-onként újra emittálódik, illetve a
független vízréteg (`WaterLodSource`, aminek a gyökerei most a gyűrűt is
tartalmazzák) fedi le.

## Ellenőrzés

- `dotnet test tests/WorldGen.Viewer.LodChunking.Tests` — **526/526 zöld**.
- `dotnet build tests/WorldGen.Viewer.Compile` és
  `tests/WorldGen.App.UnityBinding.Compile` — 0 hiba.
- Unity `recompile` — 0 hiba; Play módban a Console 0 hibát adott
  (a naplóban csak a korábbi CS8632/CS0618 figyelmeztetések és egy MCP
  `/api/exec` időtúllépés a hosszú Build alatt).
- Élő Editor, `coastalWaterRing` ki/be kapcsolással 5, illetve 6 Build.
- A tiszta-osztályba emelés UTÁN újra mérve, BITRE ugyanaz: `drawn=14268
  skipped=6032 rounds=4 open=0`, `bothBelow=0 mixed=1372 bothAbove=24666`.

## Ami NYITOTT

- **A felhasználói vizuális átvétel** (B-kategória) — a mérés bizonyítja, hogy
  a nyers vízperem megszűnt, de a látvány elfogadása a felhasználóé.
- A lap-varratokon futó partvonal-élek nincsenek benne a statisztikában (a
  maszk maga kezeli őket a `TileNeighbors.Neighbor` vetítéses útján).
- Z-fighting a part menti sávban, ahol a terep épp a vízszinten fut: nem
  kerestük külön, az élő képeken nem látszott.
- Az `adaptiveBaseLevel > 8` eset: ott nincs sűrű statikus adat, a gyűrű nem
  fut le (a viselkedés az ND-149 előtti marad). Dokumentált korlát.
