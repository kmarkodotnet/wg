# A12/2 / ND-19 — floating origin, 2. kör: a geometria tényleges eltolása

**Dátum:** 2026-09-27
**Ág:** `a19-plate-frame-noise`
**Kiindulás:** `todo2.md` → „A12/2 🟡 — ND-19, floating origin, geometria-eltolás"
**Döntés:** `docs/04-decisions.md` → **ND-19** (új alszakasz; az ND-19 ezzel
**lezárva**)
**Előzmény:** [A12 1. kör](2026-09-27-a12-nd19-floating-origin.md) — a
technika eldöntve, a matematikai mag + 48 teszt kész, az origó követve és
mérve, de a geometria még abszolút koordinátákban íródott.

---

## Az 1. kör EGY SAJÁT ÁLLÍTÁSA MEGDŐLT

Az 1. kör lezárása ezt írta a hátralévő munkáról:

> Ha a mesh csúcsai origó-relatívak és a `transform` identitás, akkor **a
> bolygó tengelyforgása többé nem kifejezhető a transform-mal**: origó körüli
> forgatás helyett bolygóközép körüli kellene […] A helyes megoldás a
> **test-keretben (body frame) való renderelés**.

**Ez téves volt.** A hiba a „`transform` identitás" hallgatólagos
feltevésében volt. Az origót a TEST-KERETBEN választjuk — vagyis `O` a
bolygóval EGYÜTT forog —, ezért a teljes leképezés

    p  →  A + s·R·(p − O)

(ahol `A` a jelenetbeli Planet-pozíció, `s` a skála, `R` a test → szülő-tér
forgatás) **maga is puszta forgatás + eltolás, azaz pontosan egy
Unity-transzform**. A Planet `rotation`-ja változatlanul a spin/dőlés
hordozója marad (ND-153), csak a `localPosition`-ja kap eltolást. A
kameramódok (Free / AxialRotation / OrbitalFollow) **egyetlen sorral sem
változtak**, és a „test-keretes renderelésre váltás" — ami a jelenet minden
fogyasztóját érintette volna — elmaradt.

## A választott jelenet-konvenció: két réteg-gyökér, két precízió

| réteg | lokális tér | transzform | hol kerekít a `float32` |
|---|---|---|---|
| **Planet** — statikus alap, víz, határvonal, folyó, tó, felhő, StarField, markerek | ABSZOLÚT `p` | `localPosition = A − s·R·O` | a csúcsban ÉS a mátrixban (`s·R·p − s·R·O`), ~0,57 m |
| **PlanetRefinedLayer** (ÚJ) — a finomított (dinamikus) réteg | ORIGÓ-RELATÍV `p − O` | `localPosition = A` | csak a KICSI `p − O`-ban, mm alatt |

**Miért elég ennyi.** A nyereség ott jelentkezik, ahol kell: a kamera
közelében renderelt, finomított rétegen. A durva rétegek megtartják a mai
kvantálásukat — ez nem regresszió, mert a kamera közelében a finomított réteg
TAKARJA őket (`ApplyTerrainCoverage` index-maszkja kivágja a statikus
háromszögeket), a coverage szélén pedig a 0,57 m már jóval pixel alatti.

**Miért nem tolható el MINDEN réteg.** A statikus alapréteget rebase-enként
újra kellene emittálni. Mérve (`docs/backlog.md`, ND-67/68 sor):
`BuildStaticBaseLayer` ~1,3–1,6 s, ebből az emit ~0,66 s. Egy közeli
pásztázásnál ez néhány másodpercenként ismétlődne — vállalhatatlan. Az
eltolást ezért a TRANSZFORM hordozza ott, ahol a pontosság úgysem számít.

**Miért TESTVÉR a második gyökér, nem gyerek.** Gyerekként a világ-eltolása
`s·R·O + (A − s·R·O)` lenne, amit a Unity `float32`-ben számol: a kioltás
~0,57 m maradékot hagyna, ÉS `R`-rel változna, tehát forgás közben a
finomított réteg REMEGNE a durvához képest. Testvérként a pozíciója
közvetlenül `A` — nincs kioltás. Cserébe a forgatást szinkronban kell
tartani: a `SunController` közvetlenül a `rotation` beállítása után hívja a
`SyncRefinedLayerTransform()`-ot, plusz van egy idempotens biztonsági háló a
`PlanetGridMesh.LateUpdate()`-ben.

**A Planet lokális tere SZÁNDÉKOSAN ABSZOLÚT maradt.** Ez szüntette meg a
változás nagy részét. A LOD-kiválasztás, az overlay-ek, a diagnosztika és a
léptékvonalzó mind `transform.InverseTransformPoint(...)` /
`transform.localToWorldMatrix` úton kérdez — ha a Planet tere
origó-relatívvá vált volna, MINDEGYIK csendben eltolódik (nyolc hívási hely,
mind némán rossz eredményt adna). Így egyetlen ilyen hívás sem változott, és
a finomított chunkok `worldToLocal(Planet) · localToWorld(chunk)`
kompozíciója automatikusan visszaadja az abszolút Planet-lokális
koordinátát.

## Bit-azonossági szerződés

**Bolygóközepű origónál minden emittált csúcs BITRE a korábbi**, mert a
kivonás pontos 0,0-t von ki. Ezt két teszt köti ki
(`PlanetCentreOriginEmitsTheBarePlainCastBitForBit` és a 2000 mintás
`…OnAWholeSampledSurfaceSweep`).

Hogy ez akkor is igaz maradjon, ha a finomított réteg double-lánca később
változik, a statikus és másodlagos rétegek emit-útja SZÁNDÉKOSAN a régi,
`float32`-es alakon maradt (`ToWaterVector3`, `_staticCornerPositions`
Vector3-ként). A finomított réteg viszont mostantól végig double-ban számol:

- `ToDisplacedPoint` / `ToDisplacedPointFromBasis` → `SurfacePoint`;
- `ComputeDisplacedRadius` → `double` (**ez volt a rejtett szűk keresztmetszet**:
  korábban `radius + (float)(…)`, tehát maga a SUGÁR kerekedett float32-re,
  100-as léptéken 0,57 m — a pozíció-lánc pontossága már ITT elveszett);
- a perzisztens sarok-cache `SurfacePoint`-ot tárol (korábban a kerekítés MÁR
  a cache ÍRÁSAKOR megtörtént, tehát a későbbi double-kivonásnak nem lett
  volna mit nyernie) — és így **origó-független**, rebase-nél NEM kell eldobni;
- a geomorph-blend `SurfacePoint.Lerp` (korábban `Vector3.Lerp`);
- a közös-sarok feloldó (`LodCornerResolver`) már double volt, csak egy
  fölösleges `ToUnityPoint`-cast dobta el a pontosságát.

Emiatt a finomított réteg bolygóközepű origónál is legfeljebb **1 `float32`
ULP-pel** — a mai kvantáláson belül — eltérhet a korábbitól, szigorúan
pontosabb irányba.

## Az origó három állapota — és miért nem kozmetika

`pending` (amit a kamera javasol) → `requested` (amivel az ÉPPEN FUTÓ
emisszió számol, a többi `_requested*` snapshottal egy helyen) → `applied`
(amihez a MÁR FELTÖLTÖTT geometria tartozik; EZ vezérli a
jelenet-transzformot és a kamerát).

Enélkül a világ-eltolás és a geometria KÜLÖN képkockában váltana, és a kép
egy képkockára egész cellányit — a kamera magasságával összemérhető utat —
ugrana. Mellékhatásként egy ELAVULT kérés alkalmazása is konzisztens marad: a
jelenet egyszerűen visszaáll annak az origójára, mert a buffer a SAJÁT
origóját hordozza (`AdaptiveMeshBuffers.RenderOrigin`).

## A kamera saját pozíciója

Eltolt origónál a mai `target.position + rot * (0,0,-distance)` alak
HASZNÁLHATATLAN: mindkét tag ~bolygósugár nagyságrendű, az eredmény kicsi,
tehát a `float32` kivonás kioltással ~0,57 m hibát ad — és az képkockánként
UGRIK. Helyette: a kamera test-keretbeli pozíciójából ELŐBB vonjuk ki az
origót double-ban, és a (legfeljebb ~1,5 cellányi) maradékot a
`RenderOriginFrame` origó-relatív terében adjuk át. A nézési irány sem a két
világpozíció kivonásából jön, hanem a `-camBody` normalizálásából.
`PlanetCenter` módban a régi, változatlan út fut, tehát a kamerapozíció bitre
a korábbi.

## Mérés (élő Play, Unity 6000.0.77f1 + HDRP)

| mérés | érték |
|---|---|
| `applied` origó 28,1 km magasságon | `RenderOrigin(0, 34.25, -94.25, cell=0.25)` |
| `planetLocalPosition` = `A − O` | `(-1.182, -32.052, 861.25)` — **bitre** a várt érték |
| kamera az origó-relatív térben | `(0, 0.0888, -0.0952)` → 0,13 egység (9,6 km) |
| elért felbontás | `appliedResolutionMeters=0.002211` (**2,21 mm**), `appliedGainFactor=256.0` |
| ugyanez 89 km magasságon | 8,845 mm, `gainFactor=64.0` |
| abszolút (mai) felbontás | 0,5661 m |
| finomított chunk mesh-bounds középpontja | 0,6–2,7 egység (korábban ~100) |
| A/B kép, **fagyasztott idővel**, 89 km | átlagos abszolút eltérés **0,0037/255**; a pixelek **0,001%**-a (9 px) tér el 8-nál többet |
| A/B kép, **fagyasztott idővel**, bolygó-nézet | átlag 0,65/255; 3,0% a >8 eltérés — a finomított réteg ≤1 ULP-es újraemissziója a partvonalakon |

**Rebase-költség, mérve.** Az origó-váltás a finomított réteg TELJES
újraemisszióját kéri (`changedChunks=1138/1138`, `reusedLeaves=0`):
bolygó-nézeti cut-méretnél (70 718 levél) `workerMs=2336`, viszont a **fő szál
érintetlen** — az ND-147 képkockákra bontott feltöltés `stageFrames=22`,
`maxSlice=3,01 ms`, `commitMs=6,9 ms`. A hiszterézis (`RebaseFactor = 1,5`)
miatt rebase csak akkor kell, ha a kamera a saját magasságával összemérhető
utat tett meg.

## Az élő mérés KÉT VALÓDI HIBÁT fogott

Mindkettő olyan, amit teszt nem talált volna meg, mert Unity-jelenet-állapotról
szól.

**1. Visszamaradt réteg a Planet alatt.** A finomított réteg GameObject-jei
TÚLÉLIK a domain reloadot, a `_refinedLayerRoot` mező viszont nem. Az új kód
nem találta meg őket (`LayerRoot(name).Find(name)`), MÁSODIK példányt hozott
létre a gyökér alatt, a régi (abszolút koordinátás) pedig aktívan ottmaradt a
Planet alatt — mérve egy aktív `IndependentWater1`. Javítás:
`MigrateRefinedChildrenFromPlanet()`, ami a névkonvenció alapján (a
`Dynamic*`/`IndependentWater*` nevek és a `Chunk_` előtag) átköltözteti őket.
A naplóban látszik: `[ND-19 layer migration] moved=2`.

**2. A független vízréteg nem épült újra rebase-nél.** A
`ComputeIndependentWater` SZÁNDÉKOSAN nem emittál újra, ha a víz-kiválasztás
változatlan (`ReusedSelection`, illetve azonos `Leaves`) — a víz nem morphol,
tehát azonos cut mellett azonos attribútumok. Az ORIGÓ viszont nem része a
kiválasztásnak: rebase után a már feltöltött vízmesh csúcsai a RÉGI origóhoz
voltak relatívak, tehát a teljes vízréteg elcsúszott (a mért esetben ~100
egységgel, azaz gyakorlatilag eltűnt). A tünet **alattomos** volt, mert a
TEREP helyesen újraépült mellette: mély óceán fölött a kamera „vízfelszín
helyett tengerfeneket" látott, ami simán elmehetett volna „ez a mély óceán
így néz ki"-ként. Javítás: `InvalidateIndependentWaterForRenderOrigin()`
(`_appliedWaterSelection = null`, ami friss kiválasztást ÉS emissziót
kényszerít). **A javítás ELŐTT a 89 km-es A/B kép 65,2/255 átlagos eltérést
adott, UTÁNA 0,0037/255.**

## Egy MÉRÉSI MÓDSZERTANI csapda (nem kódhiba, de órákat vitt el)

Az első A/B képpárok drámai fényesség-különbséget mutattak (átlag 20 vs 113),
miközben a GEOMETRIA pixelre egyezett. Az ok nem a változtatás volt, hanem a
**futó idő**: a `SunController.daysPerSecond = 0.05`, azaz egy teljes nap 20
másodperc alatt telik le — a mód-váltás és a két felvétel között 8–20
másodperc telt el, tehát a terminátor átsöpört a képen. Free módban a Planet
identitáson áll és CSAK a fény forog, ezért a geometria helyben maradt, a
világítás viszont teljesen átfordult — pont az a kombináció, ami
„geometria-rendben, árnyalás-elromlott" hibának látszik.

**Tanulság a következő élő vizuális A/B-hez:** `daysPerSecond = 0`, fix
`currentTimeDays`, `followLocalSurface = false` és fix `distance` KELL, mielőtt
bármit összehasonlítunk. Enélkül a `ConstrainSurfaceDistance` is elmozdítja a
kamerát két felvétel között (ez is megtörtént: ugyanaz a póz egyszer vizet,
egyszer terepet mutatott).

## Ellenőrzés

| kapu | eredmény |
|---|---|
| `dotnet test WorldGen.sln` | **1817/1817 zöld** (Core 705, Viewer.LodChunking 636, App.Foundation 452, Cli 24) |
| ebből új: `FloatingOriginGeometryTests` | **19 teszt** |
| `dotnet build tests/WorldGen.Viewer.Compile` | 0 error (122 warning, mind a meglévő CS8632) |
| `dotnet build tests/WorldGen.App.UnityBinding.Compile` | 0 error |
| Élő Unity `recompile` | `compilationFailed: false`, **0 console error** |
| `AssetDatabase.Refresh` + `.meta` | az új `PlanetGridMesh.FloatingOrigin.cs` importálva, a `.meta` commitolva |
| Élő Play A/B | ld. a mérés-táblát |
| Jelenet-fájl / ProjectSettings | **érintetlen** (`git status` tiszta rájuk) |

### Az új tesztek (19), és mit fognak meg

- **Bit-azonosság** bolygóközepű origónál (`BitConverter.SingleToInt32Bits`
  összehasonlítással, plusz egy 2000 mintás felszín-söprés).
- **Mért nyereség**: két, 10 cm-re levő felszíni pont bolygóközepű origóval
  UGYANARRA a `float32` csúcsra kerekedik, kamera-illesztettel KÜLÖN marad.
- **Mért kvantálási lépés** bináris kereséssel, a zárt formulához és az ND-19
  táblázathoz mérve (28,1 km → ≥64×, 1 km → ≥1024× nyereség).
- **A rács-illesztett origó kivonása EGZAKT** double-ban (4 cellaméret ×
  500 minta; a maradék tengelyenként ≤ fél cella).
- **A két réteg-út ekvivalenciája**: `A + s·R·(p−O)` ugyanaz Planet-úton
  (abszolút csúcs + eltolt transzform) és finomított úton (relatív csúcs +
  eredeti transzform).
- **A radiális bias csak ABSZOLÚT ponton értelmes** — a lokális maradék iránya
  semmilyen használható kapcsolatban nincs a radiálissal (ezért kell a bias a
  float32-re váltás ELŐTT).
- `SurfacePoint` kivonás/sugár/irány/egyenlőség + a `SamePositions`
  chunk-újrahasznosítási út.
- Geomorph-blend: 10 cm-es részlet a morph FELÉNÉL is megmarad.

**Egy hamis zöldet a teszt maga fogott meg:** az első változat a mintapontot
a `(1,0,0)` tengelyre tette, ahol két komponens NULLA — a 0 körül a `float32`
ULP denormálisan kicsi, tehát a bolygóközepű origó is „felbontotta" a
centimétert. Az `(1,1,1)` irányra váltva (egyetlen komponens sem esik 0
közelébe) a teszt a valódi viselkedést méri; a doksi-komment ezt rögzíti is,
hogy ne lehessen visszarontani.

## Mi maradt nyitva (nem blokkoló)

- A durva rétegek (statikus alap, víz, folyó, tó, felhő, markerek) 0,57 m-es
  kvantálása megmarad. Ha a zoom valaha 1 km alá megy, ott is kellhet a
  double-lánc — de az a statikus réteg rebase-enkénti újraépítését jelentené,
  tehát előbb inkrementális re-emit kell hozzá.
- A `nearClip` (ma 0,3 egység ≈ 22 km) lejjebb vitele külön, logaritmikus-depth
  tétel — ld. az ND-19 elvetett alternatíváit.
- A felhasználói vizuális átvétel.
