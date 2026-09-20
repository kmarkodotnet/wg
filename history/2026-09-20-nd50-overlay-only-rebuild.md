# 2026-09-20 — ND-50 lezárva: overlay ≠ teljes újraépítés

## A panasz

A `windSpeedOverlay` / `precipitationOverlay` / `tectonicPlateOverlay`
kapcsolgatása **teljes `Build()`-et** indított: újraszámolta az
elevációmezőt, a tengerszintet, a hidrológiát, az eróziót és a csapadékot,
majd bumpolta a folyó-finomítási generációt — vagyis **eldobta a
háttérszálon futó, többmásodperces folyó-finomítást**. Ugyanaz a hibaosztály,
amit a felhőre (`ApplyCloudOnlyRebuild`) és a folyó-vonalra már kijavítottunk.

## Amit az ND-50 tévesen feltételezett

A bejegyzés szerint a megoldáshoz „a mesh-építési kódot mélyen érintő
refaktor" kellett volna, mert az overlay nem külön réteg, hanem a fő
terep-mesh színét cseréli. **Ez tévedés volt.** A `BuildStaticBaseLayer()`
már akkor is önálló volt, és kizárólag a CACHE-ELT világállapotból dolgozik
(`_adaptiveSeed`, `_adaptiveSeeds`, `_adaptiveCraters`, `_adaptiveSeaLevel`
+ a terrain-bázis cache) — szimulációt nem futtat. A „mély refaktor" tehát
már el volt végezve, csak senki nem hívta be erre a célra.

## A javítás

1. Az öt overlay-mező kikerült a `WorldConfigChangedSinceBuild()`-ből egy
   saját `OverlayConfigChangedSinceBuild()` / `SnapshotOverlayConfig()`
   párba — ugyanaz a minta, mint a felhőnél és a folyó-vonalnál.
2. `ApplyOverlayOnlyRebuild()`: üríti a szín-gyorsítótárakat, újraépíti a
   statikus alapréteget, és `_adaptiveConfigDirty = true`-val a következő
   `Update()`-re bízza a dinamikus chunkok újraszínezését.
3. `BuildStaticBaseLayer(reuseClassifications: true)`: a tile-besorolás az
   elevációból/tengerszintből/biome-ból jön, overlay-től független — 453 ms
   megspórolva. A paraméter méret-/szint-ellenőrzéssel BIZTONSÁGOSAN
   visszaesik újraszámolásra, és csak az overlay-út adja true-val.

**Két degenerált eset marad teljes `Build()`:** ha még sosem futott Build,
illetve ha a csapadék-overlayt kapcsolják be, de csapadék-mező még nem
számolódott (`_adaptivePrecip == null`). Ez utóbbi nélkül az overlay
CSENDBEN nulla csapadékot rajzolna mindenhova — ugyanaz a csapda, amit az
`ApplyCloudOnlyRebuild` is kezel.

## Mérés (élő Editor, level 8 alapszint, 396 294 statikus sarok)

| overlay-váltás | régi (teljes `Build()`) | új (overlay-út) | folyó-generáció |
|---|---|---|---|
| tektonikus / csapadék / overlay ki | 3147–3826 ms | **897–1595 ms** | régen bumpolt → most **változatlan** |
| szél-overlay be (drága színezés) | 5718 ms | **2986 ms** | **változatlan** |

A `BuildStaticBaseLayer` bontása az overlay-úton (PerfLog): klasszifikáció
**0 ms** (`classificationsReused=True`, korábban 453 ms), sarkok 77 ms,
bucketPrepare 326 ms, emit 749 ms, mesh 144 ms, víz 84 ms.

A sebesség a kisebbik nyereség. A lényeg a `riverGen 1→1`: a háttérfinomítás
túléli az overlay-kapcsolgatást.

## Egy meglévő rés is bezárult

A `_waterCornerColors` (a vízfelszín sarok-színei, szintén overlay-függő)
eddig CSAK a teljes világ-reset során ürült — az
`InvalidateColorCacheIfModeChanged` nem érintette. Amíg minden
overlay-változás teljes `Build()`-et indított, ez nem látszott; az olcsó
úton viszont elavult vízszíneket hagyott volna. Most az
`InvalidateOverlayColorCaches()` egy helyen üríti az összeset.

## Igazolás

- **Diszpécser-ellenőrzés:** nyugalomban egyik ág sem billen; `deepTimeMyr`
  változásra `world=True, overlay=False` (teljes Build, a generáció
  jogosan bumpol); overlay-változásra `world=False, overlay=True`.
- **Végponttól végpontig:** csak a mezőt állítottam be, az `Update()`
  választotta az utat — a snapshot frissült, a statikus színek a
  lemez-palettára váltottak, `riverGen` 1 maradt.
- **Vizuálisan:** a tektonikus overlay a legendával együtt megjelenik.
- Offline kapu 0 hiba; Unity `recompile` 0 hiba; Core 480 / LodChunking 447 /
  App.Foundation 438 zöld.

## Ami nyitva marad

Az ND-50 B) opciója (a finomítási generáció invalidálását a finomítás
TÉNYLEGES bemeneteihez kötni) továbbra is érdemes, de már nem sürgős. A
mostani javítás a KONKRÉT öt mezőt kezeli; ha valaki új, tisztán vizuális
paramétert vesz fel a `WorldConfigChangedSinceBuild()`-be, a hibaosztály
visszatér. B) ezt szerkezetileg zárná ki.

## Tanulság

**Egy régi ND „miért nehéz" indoklását érdemes újraolvasni a mai kód
ismeretében.** Az ND-50 2026-09-06-i feltevése („mély mesh-refaktor kell")
azóta elavult: a `BuildStaticBaseLayer` időközben önállóvá és
cache-vezéreltté vált. A feladat nem lett könnyebb attól, hogy
halogattuk — már rég könnyű volt, csak senki nem nézett rá újra.
