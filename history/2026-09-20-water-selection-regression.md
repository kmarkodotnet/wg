# 2026-09-20 — Regresszió az ND-50 overlay-úton: elavult víz-kiválasztás

## Mi történt

Az ND-50 javítás (`4a1cfe5`) után a Unity-konzolban öt hiba jelent meg,
pontosan a saját tesztelési ablakomban (15:50–15:57 UTC):

```
ArgumentException: Új víz-snapshothoz új kiválasztás kell.
Parameter name: previous
  at WaterLodSource.Select (WaterLodSource.cs:101)
  at PlanetGridMesh.ComputeIndependentWater (WaterRefinement.cs:86)
  at PlanetGridMesh.ComputeAdaptiveMeshBuffersCpu → RebuildAdaptiveMesh → Update
```

majd: *„az async BuildCut+emit hibával zárult – TARTÓSAN visszaállok a
szinkron (fő szálú) útra"*. Vagyis a nézegető teljesen abbahagyta a
worker-szál használatát — a tünet nem csak egy naplósor, hanem a teljes
aszinkron LOD-út kiesése.

## Az ok

Az `InitializeIndependentWater()` — amit a `BuildStaticBaseLayer()` a végén
hív — **új `WaterLodSource`-t** hoz létre, de a `_appliedWaterSelection` a
RÉGIRE mutatott. A `WaterLodSource.Select` pedig `ReferenceEquals`-szel
ellenőrzi, hogy a kapott `previous` ugyanahhoz a forráshoz tartozik-e.

Eddig ez azért nem látszott, mert a `Build()` a saját
`InvalidateAdaptiveCaches()`-ében **már nullázta** a mezőt, mielőtt a
statikus réteget újraépítette. **Az invariánst tehát a HÍVÓ tartotta
fenn, nem az a kód, amelyik az elavulást okozza.** Amint az ND-50
`ApplyOverlayOnlyRebuild` a `BuildStaticBaseLayer()`-t a teljes `Build()`
nélkül hívta meg, a kivétel azonnal jött.

## A javítás

A nullázás oda került, **ahol az elavulás keletkezik**: az
`InitializeIndependentWater` maga ejti el a `_appliedWaterSelection`-t (és a
`_drawnHiddenStaticWaterQuads`-ot, mert a statikus víz-mesh épp
újraíródott — ugyanaz az indok, amiért az `InvalidateAdaptiveCaches`
`restoreStaticIndices: false`-szal hív). Így minden jelenlegi és jövőbeli
hívó biztonságos, nem csak az, amelyikre most gondoltam.

**A `WaterLodSource.Select` őrzése szándékosan maradt.** Kézenfekvő lett
volna a `ComputeIndependentWater`-ben csendben elejteni a nem illeszkedő
`previous`-t — az viszont elrejtette volna ezt a hibaosztályt ahelyett,
hogy hangosan jelzi.

## Igazolás állapota

Offline kapu 0 hiba, Core 483 / LodChunking 455 / App.Foundation 438 zöld.
**Futásidejű igazolás HÁTRAVAN:** a Unity Editor főszála közben leállt
válaszolni (az `exec` és a `recompile` is időtúllépéssel elszáll, a
`groundTruth` mintavétel 466 másodperce nem frissült), ezért Play módba
nem tudtam visszalépni. Az Editor újraindítása után néhány
overlay-kapcsolgatásnak hibamentesnek kell lennie.

## Tanulságok

**Az élő konzol átnézése nem opcionális, és nem is elég a végén.** A
commit előtt lefuttattam a teszteket és a fordítási kaput, és mindkettő
zöld volt — a hibát egyetlen automatizált ellenőrzés sem fogta meg, mert
runtime-hiba egy MonoBehaviour-úton. A CLAUDE.md előírja, hogy „Check
Unity Console errors before considering a task complete"; én az ND-50-nél
a mérésekre koncentráltam, és a konzolt csak két feladattal később néztem
meg. Ekkor már három feladat commitja volt fölötte.

**A `console` tool szűrője működik — én hívtam rosszul.** Korábban
felírtam a naplóba, hogy a `types` szűrő „nem szűr". Valójában a
paraméter neve `level` (minimum-súlyosság) és `tail`, nem `types`/`count`.
A rossz paraméterekkel a szerver az alapértelmezést adta vissza: 95
warningot és 55 KB-ot. `level: "error", tail: 10` pontosan a hat hibát adta
vissza. **Ha egy eszköz „nem működik", előbb a hívást ellenőrizd, mielőtt
az eszközre fogod** — és javítsd a korábbi, téves feljegyzést, mert az
tévútra visz.

**Amit a jövőben másképp csinálok:** ha egy változtatás egy addig
egyetlen hívóval rendelkező függvényt új helyről hív meg, végig kell nézni,
mit tart fenn a RÉGI hívó a függvény körül. Itt a `BuildStaticBaseLayer`
három ilyet is örökölt a `Build()`-től (víz-kiválasztás, rejtett quadok,
chunk-cache); kettőt magamtól kezeltem, egyet nem.
