# ND-93/94 — Két feladat, közös ellenőrzési csomag

## Elkészült változtatások

**1. Nem használt renderchunkok és saját mesh-ek életciklusa (ND-93).**
A korábbi út a már nem szükséges chunkokat csak kikapcsolta, a teljes
GameObject-/spare-cache világváltásig megmaradhatott. Most az inaktív
kulcsok sorrendben nyilvántartottak, alapból 128 maradhat a cache-ben.
A többletet frame-enként legfeljebb 8 kulccsal, puha 0,5 ms keretben üríti.
Aktív és a publikált cut által használt chunk nem törölhető. Staging alatt
a takarítás szünetel; a tiszta worker számítása közben futhat, CPU-adatát
nem változtatja. Újrahasználat kiveszi a kulcsot az inaktív sorból.

A saját runtime Mesh-ek létrehozáskor tulajdonosi nyilvántartásba kerülnek.
Eviction, Build és OnDestroy explicit felszabadítja őket: nem csak az
őket használó GameObject tűnik el. Külső sharedMesh nem válik saját
tulajdonná; a legacy chunk-upload is külön mesh-t készít helyette.
Az aktív, statikus, aux és managed CPU-cache nem része a 128-as limitnek.
Ez darabszámú célkorlát, **nem kemény teljes VRAM/RAM-korlát**.

Várható nyereség: hosszú területváltás után kevesebb feleslegesen megtartott
inaktív geometria. Ár: a kiürített területre visszatérés új objektumot/mesh-t
igényelhet. A Destroy natív költsége később is jelentkezhet; a memória/FPS
nyeresége még nem igazolt élőben.

**2. Külön ütemezhető terepmesh és rendercél (ND-94).**
Korábban egy jobba tartozott a mesh feltöltése, az anyag és a célobjektum
előkészítése. Most külön `terrainMesh` és `terrainTarget` job; közöttük
is megállhat a 2 ms puha keret. A korábbi 64 chunk/frame elméleti átvitelt
128 részfeladat/frame tartja meg. Az aux/maszk a sor végén marad, a teljes
fedéscsere továbbra is külön Update. A fél pár után is biztonságosan
megszakítható a kérés; nem jelenhet meg félkész mesh.

Ez nem feltétlenül kevesebb összmunka: több ütemezési pont és kis managed
burkoló kerül a sorba. Egyetlen natív hívás továbbra sem megszakítható.
A korábbi 7,10 ms-os staging-tüske megszűnését nem állítjuk; az új log a
legdrágább job típusát és idejét külön méri.

**Változatlan:** Core, seed, relief, scene, tengerszint, tile-cut, pixelcél,
korai finomodási küszöb és mozgókamerás selection. Ezért ettől a csomagtól
önmagában nem várunk látványos élességnövekedést. Az ND-91 kamera és
ND-92 hibafallback korábbi változtatásai megmaradnak.

## Egyben elvégzendő felhasználói próba

### Előkészítés

- Állítsd le a Play módot, várd meg a Unity fordítás végét. Új piros
  Console-hiba esetén még ne kezdj vizuális tesztet: küldd el a hibát.
- A szokásos jelenetet használd, a reliefet és a viewportot ne változtasd
  a próba közben. Így a teljesítményváltozás jobban összevethető.
- A `PlanetGridMesh` új `Inactive Terrain Chunk Limit` mezője alapból 128.
  A `Use Staged Terrain Upload` legyen bekapcsolva. Scene-mentés nem kell.

### Kötelező, közös próbasor

| Lépés | Mit csinálj? | Mit figyelj a képen? |
|---|---|---|
| 1. Induló kép | Play, várd meg az első világépítést | A felszín, hegyek, part és víz megmarad; nincs új hiányzó réteg |
| 2. Zoom oda-vissza | Távoli → közepes → közeli zoom, majd vissza; kétszer | Feltöltés közben maradhat a régi részlet, de nem lehet lyuk, eltűnő chunk vagy félkész villanás |
| 3. Új területek | Közepes/közeli zoomon járj be legalább 3–4 eltérő területet, köztük partot és óceánt | A terep/víz együtt frissül; tartós hiány vagy új erős akadás nem jelenhet meg |
| 4. Visszatérés | Menj vissza egy korábbi helyre, várd meg a finomítást | A már kiürített chunkok is újra megjelennek; nincs üres folt vagy elveszett felszín |
| 5. Megállás | Állj meg és hagyd befejeződni a kérést/feltöltést, majd várj néhány másodpercet | Nem tűnik el aktív terep a háttértakarítás miatt; a cache lecsengését a logból ellenőrzöm |
| 6. Világváltás | Használd a szokásos deep-time léptetést/újraépítést, majd ismét zoomolj | Az előző világ chunkjai nem maradnak a képen; a hosszú teljes Build gyorsítása nem e csomag célja |
| 7. Play-életciklus | Zoomolás közben állítsd le a Playt, majd indítsd újra | Nincs új Destroyed/MissingReference/NullReference hiba, az új világ teljesen felépül |

A régi első-zoomos késést külön jegyezd fel, de annak változatlansága
nem ennek a két részfeladatnak a regressziója: a javítását korábban halasztottuk.

### Két hasznos kiegészítő próba

1. **Cache stressz:** ha a bejárt út nem termel 128 inaktív chunkot,
   Play módban ideiglenesen állítsd az `Inactive Terrain Chunk Limit` mezőt
   8-ra, majd ismételd a területváltást és visszatérést. A mező módosítása
   megszakíthat egy staginget, de a régi fedésnek meg kell maradnia. Ezután
   állítsd vissza 128-ra; a beállítást ne mentsd el a scene-be.
2. **Korábbi kamerakapu:** tenger és hegység fölött közelíts valóban a
   minimumig, ott mozdulj oldalra, majd távolodj. Ellenőrizd a felszínbe
   jutást/levágást és a görgő működését. Az ND-91 pontmintás védelme nem
   teljes hegyoldali/near-plane sarokvédelmi garancia; ha ilyet látsz,
   képernyőképpel és a próba lépésével jelezd. A legutóbbi 12:51-es log
   még nem ért el a 0,33-as minimum közelébe.

## Mit ellenőrzök én a logból?

- `[ND-93 terrain cache]`: `targets`, `ownedMeshes`, `spares`, `inactiveKeys`
  valódi nyilvántartási darabszámok, nem kívánt tile-méretek. Az `inactiveKeys`
  stabil állapotban legfeljebb `limit`, ha nincs staging; nagy inaktiválás
  után a többlet fokozatosan ürülhet. A `targets`/`ownedMeshes` az aktív
  fedés miatt természetesen lehet több 128-nál. Nem szabad ezeket byte-
  pontos memóriafoglalásnak tekinteni.
- `evicted` az előző cache-log óta kiürített kulcsok száma;
  `evictionTotalMs` az időablak Update-beli takarítási/ellenőrzési összideje.
  Nem tartalmazza a később végrehajtott natív Destroy teljes költségét.
- Feltöltés: `pipeline=ND94`, `terrainPipeline=ND94`; `maxJobType`,
  `maxJobMs`, `maxJobKey` a slice tényleges legdrágább részfeladata.
  Aux-jobnál a kulcs nulla. A `maxJobMs` az `elapsed` része, nem plusz idő.
- A teljes `requestAge`, `stageFrames`, `maxSlice` és commit-idő együtt
  értékelendő: rövidebb szelet mellett sem rejthetjük el a hosszabb kérést.
- ND-75 továbbra is a ténylegesen kirajzolt quadokat méri. Nem nőhet
  hibás/hiányos geometria az eviction vagy szétválasztott staging miatt.
- Normál próba közben az ND-92 recovery-log vagy Unity-kivétel új hiba
  jele; a hibafallback teszteléséhez ne okozz szándékosan GPU-hibát.

**Amit kérünk vissza:** elég a friss PerfLog és röviden a fenti lépések
szerinti észrevétel (például „4: visszatéréskor folt maradt”). Hibánál
képernyőkép/Console-szöveg, cache-stressznél az alkalmazott limit is kell.

## Fejlesztői ellenőrzés és korlátok

- Solution build: 0 hiba / 0 warning.
- Teljes .NET futás: **696/696 PASS** (384 Core + 304 viewer + 8 CLI).
- Viewer Release: **304/304 PASS**.
- Kilenc új .NET-eset: hét inaktív sor-/újrahasználat-/ismétlési eset és
  két mesh/cél közötti időkeret/megszakítás eset.
- Unity Assembly-CSharp fordítás: 0 hiba / 83 korábbi warning.
- Editor tesztassembly fordítás: 0 hiba / 4 korábbi warning. Kilenc új
  integrációs eset: saját/idegen mesh életciklus, árva/kettősen hivatkozott
  saját mesh, publikált kulcs védelme, legacy upload-hiba, tényleges pending
  sor megszakítása a két fázis között. Négy korábbi staging-eset is az új
  kétfázisú út szerint ellenőriz. **Editorban nem futottak.**
- Nincs élő ND-93/94 FPS-, memória- vagy vizuális elfogadás. A CUA/Editor
  helyett az offline fordítást nem tekintjük kész vizuális ellenőrzésnek.
- Nincs Core-, referencia-, relief-, scene-edit vagy commit/push e körben.
  A korábban módosított munkafájljaitokat megőriztük.

Az [M9-audit](m9-progress-audit-2026-09-12.md) upload/cache sora most
implementációról visszamérésre/korrekcióra vált; a teljes minőségi kapu
nem zárult. E két tétel ráfordítás-egyenértéke durván 2–4 óra, nem mért idő.
A friss hátralévő összeg 12–24 óra; a súlyozott M9-bázis kb. 55% marad,
mert a korai minőség és teljes reakcióidő még nyitott. A változás a két
konkrét implementáció és a szűkült maradék, nem új vizuális elfogadás.
