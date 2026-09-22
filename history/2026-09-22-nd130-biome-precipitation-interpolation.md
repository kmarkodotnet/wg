# 2026-09-22 — ND-130: a biome-blokkosság gyökere a csapadék-mintavétel volt

Felhasználói visszajelzés képpel: *„a zöld színű biom a bolygó felszínén nagy
méretű négyszög alakú régiókban jelenik meg… mintha a biom determináltan
egy-egy nagyon nagy négyszög területre lenne kiszámolva"*. A `todo2.md`-ben
nem szerepelt; új sorként (A17) vettem fel.

## A gyökérok

Az ND-126 (2026-09-21) óta a biome-osztályozás kétdimenziós:
`Classify(temperatureK, isOceanic, precipitation, thresholds)`. A két bemenet
természete viszont gyökeresen különbözik:

- **hőmérséklet**: `TemperatureKelvinAt(x,y,z,…)` — pontszerű, folytonos,
  LOD-független;
- **csapadék**: `_adaptivePrecip[FromPosition(x,y,z, level)]` — a `level`=5
  referencia-rács **diszkrét tile-értéke**.

Level 5 = 6·4⁵ = **6144 tile** → egy tile **~288 km**. Egy küszöb-alapú
osztályozó ilyen bemenettel a biome-határt pontosan a tile-élekre teszi:
288 km-es, tengelypárhuzamos négyzetek. Ez volt a képen.

## A legtanulságosabb rész: a kód kimondta a téves feltevést

A `JitteredRenderBiome` ND-126-os kommentje szó szerint:

> „a jitter SZÁNDÉKOSAN csak a hőmérsékletre hat, a csapadékra nem […] a
> csapadék-határok már eleve szabálytalanok (a nedvesség-transzport a
> domborzatot követi), azokat nem kell rongyolni."

Az ÉRTÉKEK valóban a domborzatot követik. A MINTAVÉTEL viszont 288 km-es
lépcsős függvény, tehát a HATÁROK nem szabálytalanok, hanem négyzetesek. Egy
plauzibilis, de ellenőrizetlen indoklás pont azt a helyet zárta ki a
vizsgálatból, ahol a hiba volt.

## A javítás

A mező marad a jelenlegi (olcsó) szinten, de a **kiértékelés** lesz folytonos:
bilineáris interpoláció a tile-SARKOK között. Ez pontosan az a minta, amit a
**felhő-réteg 2026-09-06 óta már használ** (`PrecipAndOceanFractionAtCorner` +
GPU-interpoláció) — csak a biome-osztályozás sosem kapta meg.

1. **Core**: `TileGeometry.ToFaceUV(x,y,z, out face, out uc, out vc)` — a
   `PositionFromFaceUV` inverze, ami eddig a `FromPosition` törzsében volt
   elrejtve. A `FromPosition` innentől ezt hívja (nincs viselkedésváltozás).
   Azért kell, mert az interpolációhoz nem a tile-INDEX, hanem a tile-on
   BELÜLI pont kell — és a verifikált Core-matekot nem duplikáljuk a viewerbe.
   Tesztek: `TileGeometryFaceUvTests` (oda-vissza, index-egyezés a
   `FromPosition`-nel minden lapon/szinten, tisztaság, lap-normálisok).
2. **Sarok-tábla**: lap-lokális (n+1)² rács, sarkonként a sarkot osztó (max 4)
   tile átlaga, a lap-határra is helyes meglévő helperrel. Level 5-ön **6534
   bejegyzés**.
3. **`PrecipitationAtCore`**: `ToFaceUV` → tile-on belüli (s,t) → bilineáris.
4. **A vágópontok ugyanebből a függvényből** (a percentilisek különben más
   eloszlásra vonatkoznának, mint amit a biome-döntés lát) — ezért épül a
   sarok-tábla a vágópontok ELŐTT.
5. **A csapadék-overlay** is erre vált (eddig duplikálta a nyers lekérdezést).

## Mérés

98 304 level-7 mintapont a teljes gömbön, a vágópontokkal sávba sorolva:

| | érték |
|---|---|
| megváltozott csapadék-sávú mintapont | **21,0%** (20 597) |
| sáv-eloszlás előtte | [35 008, 9 920, 21 792, 31 584] |
| sáv-eloszlás utána | [27 347, 11 324, 23 051, 36 582] |

Vizuálisan (1079 km és 315 km magasság, biome-átmeneti zóna): a sivatag→erdő
átmenet szabálytalan, folytonos; négyszög sehol.

## Tanulságok

1. **Ha egy osztályozó bemenetei eltérő felbontásúak, a legdurvább szabja meg
   a kimenet geometriáját.** A folytonos hőmérséklet nem tudta „megmenteni" a
   lépcsős csapadékot — a határt a lépcsős bemenet rajzolta.
2. **Egy magyarázó komment nem bizonyíték.** Az ND-126 indoklása („a
   csapadék-határok eleve szabálytalanok") plauzibilis volt és téves; a
   kódból egy perc alatt ellenőrizhető lett volna.
3. **A megoldás már a repóban volt.** A felhő-réteg ugyanezt a problémát
   2026-09-06-án megoldotta ugyanezen a mezőn; a mintát csak át kellett vinni.

## Nyitva

- A bilineáris interpoláció C0, nem C1 — elvileg látszódhat rács-átlós „ránc"
  a szintvonalon. Élő ellenőrzésen nem látszik; ha mégis, a következő lépés a
  csapadékra is kiterjesztett zaj-perturbáció (az ND-57/ND-59 mintája).
- **Külön, még nyitott tétel:** az ÓCEÁN partvonala továbbra is blokkos
  (level-8, ~36 km lépcsők) — ez ugyanaz a hibaosztály, amit a tavaknál az
  ND-129 megoldott, csak a tengerre. A mostani képeken jól látszik.
