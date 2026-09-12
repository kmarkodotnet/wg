# ND-82 — Vízfelszín önálló tile-kiválasztása, első kapu

**Későbbi folytatás:** az [ND-83 renderbekötés](water-lod-render-nd83-2026-09-11.md)
elkészült és élő próbára vár. Az alábbi szöveg az első, még inaktív kapu
történeti állapotát rögzíti.

## Felhasználói döntés és eredmény

2026-09-11: az első zoomok beragadása maradjon backlog, folytassuk a következő
tervezett tile-feladattal. A kiválasztott önálló feladat a vízfelszín LOD-ja.

**Elkészült a külön víz-kiválasztó és a geometriai fedési terv. Nem készült
még el a teljes vízfinomítás:** a viewer nem hívja az új modult, így a mostani
kép, a zoomküszöb és a runtime számítási terhelése változatlan. Ez az első
ellenőrzési kapu, a következő a tényleges renderbekötés.

## Miért külön víz-cut?

A jelenlegi `EmitAdaptiveTile` a vízquadot a tereptile emissziója közben
építi. A víz feltétele `isOceanic && (biome == Ocean || biome == SeaIce)`;
régebbi kommentekkel ellentétben a tengeri jég is ide tartozik. A geometria
tengerszintű gömbhéj, a szín viszont a terepsarkok mélységéből jön:
`ContinuousWaterCornerColor(p00...p01, seaLevel)`.

Az ND-78 jogosan kihagyja a mély, fedett tengerfenék drága finomítását,
emiatt azonban a felette lévő víznek nincs saját sűrűsödése. A cél: a víz
saját képernyőmérete és munkakerete alapján osztódjon, a tengerfenék
besorolásának/mesh-ének felépítése nélkül. Ez nem dekoratív vízhéj:
a létező vízmaszkot és modellből kapott tengerszintet kell megőrizni.

## Új modul

`unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/WaterLodSource.cs`:

- Bemenet: base-szint (0–8), pozitív véges tengerszintsugár és a tényleges
  statikus víz-emisszió TileId-halmaza. Saját másolat készül, duplikátumok
  nélkül; nem kér eleváció-, hydrology-, klíma- vagy biome-callbacket.
- A víz-proxy a meglévő `TerrainLodProxy` állandó sugarú esete. Új szimulációs
  képlet nincs. A víz minden pontja a modell tengerszintsugarából következik.
- A `Select` az immutábilis kamera pozícióját a `ProjectedLodView.Camera`
  mezőből veszi, saját korábbi vízeredményt és saját szög-/levél-/splitkeretet
  kap. Más világ-/víz-snapshotból származó előzményt elutasít.
- Újrahasznált algoritmusok: prioritásos kvadfa, perspektivikus quad-metrika,
  hiszterézis, kvóta, balance és `LodCoverage`. A teljes vízfedés sem lépheti
  túl a levélkeretet. Nem keletkezik gyermek száraz base alatt.
- Eredmény: rendezett, csak olvasható levelek és kiváltandó víz-base-gyökerek,
  új/halasztott osztásszám, selection/balance részidő. A halasztás nem
  elveszett kérés; a hívó új hullámot indíthat. Visszazoomkor az üres dinamikus
  eredmény a megmaradó statikus víz használatát jelenti.
- A geometriai terv külön `LodCornerResolver`-t ad minden workernek. A finom
  határ a durva víz élhúrjához illeszkedik; az ilyen pont a gömb belsejében
  lehet, ezért nem szabad utólag automatikusan visszavetíteni a gömbre.
- A `TryFindRenderedLeaf` lekérdezés legalább `DeepestLevel` szintű
  mintacellát vár, így nem állítja egy már felosztott durva szülőről, hogy
  az lenne a ténylegesen renderelendő levél.

L8-nál a maszk nyers tömbje 393 216 bájt, a proxy tömbjei 7 364 640 bájt.
Ez együtt ~7,4 MiB hasznos tömbadat, **nem teljes heap-/csúcsmemória-mérés**:
a konstrukció átmeneti tömböt és a kiválasztás további kollekciókat is használ.
Az állandó sugarú proxy sűrű tárolásának tömörítése későbbi optimalizálás.

## Ellenőrzések

24 új teszteset, többek között:

- vízfinomítás terep-cut/modellezési callback nélkül;
- üres vízmaszk, másolt bemenet, duplikátumok, hibás szintek/sugarak;
- 1, 4, 17, 100 és 4000 leveles keret: teljes fedés, nincs szülő/gyermek
  átfedés, nincs száraz base alá terjeszkedés;
- állókamerás konvergencia, visszazoom, maximális szint;
- vízszint és képi küszöb érdemi hatása;
- megszakított kérés nem módosítja a korábbi eredményt;
- párhuzamos/szekvenciális és fordított bemeneti sorrend azonos cutot ad;
- véges, bejárási sorrendtől független feloldott sarkak; durva statikus
  vízélhez illeszkedés; cubed-sphere lapok közös pontjai;
- tényleges L8 méretű szintetikus maszk 300/130/105 távolságnál,
  legfeljebb 8192 levél és 256 új split. Ezek **tesztkeretek**, nem aktivált
  Inspector-alapértékek vagy élő teljesítményígéretek.

A teljes build/teszt eredménye a [munkanaplóban](../../history/2026-09-11-water-lod-selection-nd82.md).
A tesztek nem igazolják a Unity-kép partvonalát, színeit vagy teljesítményét.

## Következő kapu — még hátra

1. A **tényleges statikus víz-emisszió** azonosítóit és indexoffsetjeit átadni.
   Nem szabad a tengerfenék-kizárási maszkot vízmaszkként használni.
2. A víz attribútumait leválasztani a morpholt terepsarkokról, hiteles
   mélység-/modellforrással. Új minták költségét külön mérni/cache-elni.
3. Külön statikus víz-indexmaszk, dinamikus vízmesh és a régi terrain-vezérelt
   víz-emisszióval való kizárólagosság. A megadott tengerszintsugár pontosan
   egyezzen a rendererével (annak float kerekítésével együtt).
4. Teljes base-vízfoltok atomikus cseréje, hiba/megszakítás esetén régi fedés;
   közös élek, partok és a tavak külön vízszintjének megőrzése.
5. Valódi ND-75 vízméret-mérés és élő Unity-próba: mély óceán, part,
   zoom/visszazoom, világparaméter-változás. Addig nincs vizuális készjelentés.

Egyelőre **nem szükséges új Play-próba**: nincs aktivált runtime-változás.
Az első zoomok akadását, küszöbét és az ND-81 logban talált geometria/proxy
eltérést nem javítottuk és nem zártuk le.

M9 tartalmilag durván 60–70%. Nem mért, durva ráfordítás-egyenérték erre a
kapura 2–4 óra; a víz renderbekötése és élő validációja további 4–8 óra,
a teljes fennmaradó zoom-/megjelenítési munka becslése 8–20 óra.
