# Döntési nyilvántartás

Minden architekturális döntés egy ND-számot kap. A nyitottakat **ne oldd meg
csendben** implementáció közben — ha egy kérdés eldöntésre szorul, vagy döntsd el
explicit és írd ide, vagy vedd fel új ND-ként.

## Lezárt döntések

| ID | Kérdés | Döntés | Indok |
|---|---|---|---|
| **ND-07** | A fotorealisztikus render része-e a projektnek? | **Igen, M2-től folyamatosan** | A kép a generálás elsődleges kimenete (I3). Ha a render a végére kerülne, 8 milestone-nyi hibás irány derülne ki későn. |
| **ND-23a** | Egységvektor-mintavétel a gömbön | **Elutasításos módszer**, csak `Math.Sqrt`-tel | Bitpontos, mert az IEEE-754 a `sqrt`-re korrekt kerekítést ír elő. Mérve: 1.903 átlagos iteráció (elmélet: 6/π = 1.910). |

### ND-01 — Technológiai stack: Unity 6 + HDRP

Részletes elemzés: `docs/03-unity-hdrp-evaluation.md`. Háromutas mérlegelés
(Unity 6 + HDRP / Godot 4 + C# / Rust + wgpu) — a döntő érv: a HDRP kész,
fizikailag megalapozott atmoszféra- (`PhysicallyBasedSky`), felhő- és
víz-rendszere reálisan 6-10 hét renderelési munkát spórol, és a Burst
compiler a szimulációs oldalon is közel Rust-szintű teljesítményt ad — a
projekt két legnagyobb technikai kockázatát csökkenti egyszerre. A hiányzó
double precision (floating origin kézzel) és a Burst determinizmus-csapdája
valós, kezelt kockázat, nem elutasítási ok.

**Döntés 4 feltétellel** (mind érvényben, az utolsó 3 új ND-ként nyitva):
1. A `src/WorldGen.Core` és minden szimulációs modul motorfüggetlen marad
   (netstandard2.1, nulla Unity-referencia) — **ez már érvényben van**
   (ND-22), és a Unity-projekt (`unity/WorldGenViewer/`) ezt egy helyi
   Unity package-ként (forrás szerint, nem másolva) hivatkozza.
2. `Unity.Mathematics` + `[BurstCompile(FloatMode = FloatMode.Strict)]`
   kötelező minden szimulációs kódon, CI-ellenőrzéssel → **ND-20**.
3. A floating origin stratégia M2-ben megtervezve/megvalósítva → **ND-19**.
4. A HDRP volumetrikus felhő űrből-nézeti működése M2-ben prototípussal
   ellenőrizve → **ND-21** (2026-09-27: a prototípus lefutott, a HDRP
   volumetrikus felhő **elutasítva**; a fallback — saját felhő-shader — lép
   életbe. A feltétel teljesült, csak nem a HDRP javára.)

Ha az 1. feltétel bármikor sérülne, a döntés visszafordítható: a viewer
cserélődik, a mag nem.

### ND-24 — Cubed sphere vetítés: transzcendens függvény + a területarány valós viselkedése

A §2.2 (`docs/05-milestones.md`) szerinti egyenszögű vetítés
`s = tan(u * pi/4)`-et használ a tile-területek kiegyenlítésére. A
`tools/reference/cubed_sphere_ref.py` referencia-méréssel validáltuk (nem
emlékezetből, ld. CLAUDE.md munkamódszer) — a mért érték eltért a dokumentum
korábbi, emlékezetből idézett "~1.3" állításától:

| Level | n×n (lap) | Mért max/min arány |
|---|---|---|
| 5 | 32×32 | 1.3795 |
| 6 (klíma-alapszint) | 64×64 | 1.3969 |
| 7 (bolygónézet render) | 128×128 | 1.4055 |
| 9 (kontinensnézet) | 512×512 | 1.4120 |
| 11 (régiónézet) | 2048×2048 | 1.4137 |
| ∞ (kontinuum-határérték) | — | → √2 ≈ 1.41421 |

**Döntés (mindkét részkérdésre):**

1. **A `tan` transzcendens kockázata (ND-23b osztály):** a `TileId -> 3D
   pozíció` leképezés **konstrukciós (baked)** számításnak minősül, nem
   szimulációsnak. A pozíciókat egyszer, a rács felépítésekor számoljuk ki
   `tan`-nal, verzióhoz kötött, hash-elt táblaként kezeljük — a szimuláció
   (klíma, inszoláció) ebből olvas, nem újraszámolja. Így a `tan`
   platformfüggése a rács-metaadatba szigetelődik, nem a szimuláció
   kritikus útjába.
2. **Az `AreaDistribution` teszt küszöbe LOD-szint szerint differenciált**
   (a felhasználó által jóváhagyott "B" opció): a szigorú `< 1.40` küszöb
   csak a szimuláció bázis-szintjére (level ≤ 6) vonatkozik, ahol a mérés
   szerint van rá tartalék (1.3795–1.3969). A magasabb, render/nézet-célú
   LOD-szinteken (level ≥ 7) a küszöb `< 1.42`, ami a mért kontinuum-
   határértékhez (√2 ≈ 1.4142) igazodik, kis tartalékkal.

**Indok:** a szigorú küszöb megtartása pont ott hozott volna hamis piros
tesztet, ahol a vetítés matematikailag nem tud jobbat nyújtani (a `tan`-warp
ismert tulajdonsága, hogy a lap-sarok/lap-közép Jacobi-arány √2-höz tart) —
ez nem hiba, hanem a választott vetítés natúr viselkedése. Jobb vetítés
keresése (a korábban felmerült "C" opció) külön munkát igényelt volna
ismeretlen nyereségért, ezért elutasítva.

### ND-19 — Floating origin: LEZÁRVA (2026-09-27, A12/2) — a geometria-eltolás kész

Az ND-01 (Unity 6 + HDRP) aktiválta, M2/M3-ra sürgősnek jelölve — újragondolva
M3 (csillagászat + fény) tervezésekor.

**A felismerés:** a HDRP `Directional Light` (a Nap-szimulációhoz) fizikailag
**nem rendelkezik érdemi pozícióval**, csak **iránnyal** (rotációval), mert
végtelen távoli fényforrást modellez. Ebből következik: a Core-oldali
(`double` pontosságú, motorfüggetlen) csillagászat-számítás sosem kell, hogy
nyers, nagy-értékű pozícióként (pl. "150 millió km-re a Nap") kerüljön
Unity-koordinátába — elég egy **normalizált irányvektor**, ami `float32`-ben
is pontos, mérettől függetlenül.

A `float32` precíziós probléma (amiért ND-19 eredetileg felmerült) csak akkor
jelentkezne, ha a kamera **valós, km-skálájú bolygófelszín közelébe** kerülne.
A bolygó jelenleg (és M3 után is) önkényes `radius=100` Unity-egységben van,
nem valós méretben.

**Döntés (2026-09-13):** az implementáció **halasztva M9-re** (Continent +
Region nézet), vagy amikorra ténylegesen éles bolygóméretre váltunk — M2/M3
nem függ tőle. A konkrét technika (kamera-központú eltolás + logaritmikus
depth, vagy szektorált rebase — ld. `JakubNei/UnityProceduralPlanets`
kutatás) továbbra is nyitott, csak nem sürgős.

---

#### 2026-09-27 (A12, 1. kör): a technika-kérdés eldöntve, a mag megvan

**Az egyik fenti állítás MÉRÉSSEL megdőlt.** A „a bolygó `radius=100`
Unity-egységben van, nem valós méretben" mondat implicit azt sugallta, hogy a
kis lépték *védelem* a precíziós probléma ellen. Nem az: a `float32` felszíni
pozíció-hibája **lépték-invariáns**, mert a `float32`-nek nem absztrakt
felbontása van, hanem **relatív** (2^-23 … 2^-22 a bináris oktávon belüli
helytől függően). Mért ULP-ek, ugyanarra a 7420 km-es bolygóra:

| modell-sugár | ULP (modell-egység) | fizikai kvantálás a felszínen |
|---|---|---|
| 100 (a mai viewer) | 7,629e-06 | **0,566 m** |
| 7 420 000 (valós lépték) | 0,5 | **0,500 m** |
| 1 (egység-gömb) | 1,192e-07 | **0,885 m** |

A három érték 2x-es faktoron belül azonos. Tehát a `radius` megválasztása
**nem** precíziós kérdés, és a méterszintű közeli zoom (A11, M9 régió-nézet)
**semmilyen** radius-értékkel nem érhető el — kizárólag origó-eltolással.
A mérés reprodukálható: `FloatingOriginTests.SurfaceQuantizationIsScaleInvariant`.

Ma ez azért nem látszik, mert a legközelebbi kameramagasság
`OrbitSurfaceMath.MinimumClearance(100.1, 100, nearClip)` miatt
**~7,4–24,5 km** (a `nearClip`-től függően) — ott a 0,57 m kvantálás
szögben ~2e-5 rad, láthatatlan.

**A választott technika: kamera-illesztett, rács-illesztett (snapped) origó,
egyetlen emit-csatornán alkalmazva.**

- Az origó a kamera modell-téri pozíciójából származik, egy **2-hatvány
  lépésű rácsra illesztve**. A rács-lépés a kamera felszín feletti
  magasságánál nem nagyobb legnagyobb 2-hatvány: így az origó legfeljebb
  ~magasságnyira van, és rebase csak akkor kell, ha a kamera a saját
  magasságával összemérhető utat tett meg (ekkorra a látvány maga is
  átfordult, tehát az újraépítés nem többletköltség).
- **Hiszterézis:** rebase-küszöb = 1,5 × rács-lépés. Frissen illesztett origó
  tengelyenként legfeljebb 0,5 cellányira van, tehát a cellahatáron ülő
  kamera nem tud oda-vissza billegni két origó között — enélkül minden
  képkocka teljes geometria-újraépítést kérne.
- **A kivonás DOUBLE-ban, a cast UTÁNA.** Ez a floating origin egésze; a
  fordított sorrend semmit nem nyer.
- **Felső cella-korlát + bolygó-nézeti kivétel** (élő Play-mérésből, ld.
  lentebb): a rács-lépés legfeljebb annyi, hogy `RebaseFactor * cell` a
  bolygósugáron belül maradjon (100-nál 64), és ha a kamera magassága eléri a
  sugarat (a teljes gömb látszik), az origó **bolygóközép** — faktor-2
  hiszterézis-sávval. Enélkül a nyereség-faktor 1 alá esett.

Mért, elérhető felbontás a viewer léptékén (74 200 m/egység):

| kameramagasság | rács-lépés | rebase-út | elért felbontás |
|---|---|---|---|
| 1 000 km | 8 egység (593,6 km) | 890,4 km | 70,8 mm |
| 100 km | 1 egység (74,2 km) | 111,3 km | 8,85 mm |
| 24 km (mai minimum) | 0,25 egység (18,55 km) | 27,8 km | 2,21 mm |
| 1 km | 7,8125e-03 (579,7 m) | 869,5 m | 0,069 mm |
| 10 m | 1,2207e-04 (9,06 m) | 13,6 m | 0,0011 mm |

**Elvetett alternatívák.** (a) *Per-chunk pivot* (minden mesh-darab a saját
középpontjához relatív): ugyanazt a precíziót adná, de a csúcsok
chunk-határon nem lennének bitre azonosak két szomszédos chunk között →
hézagok/T-illesztések, amiket az ND-70 sarok-megosztás pont most szüntetett
meg. (b) *Logaritmikus depth*: a **depth**-precízió eszköze, a
**pozíció**-precízióét nem oldja meg; a HDRP-ben amúgy sem szabadon
cserélhető. Akkor lesz szükséges, ha a `nearClip`-et érdemben lejjebb
visszük — külön, későbbi tétel. (c) *Valós léptékre váltás* (`radius` =
7 420 000): a fenti táblázat szerint semmit nem nyer.

**Ami ebben a körben elkészült (kód + teszt):**
`unity/.../Assets/Scripts/Viewer/Lod/FloatingOrigin.cs` — motorfüggetlen
(nulla UnityEngine-referencia), ezért Unity Editor nélkül tesztelt:
`Float32Ulp` (könyvtárfüggetlen, csak 2-hatvány skálázás),
`RecommendedCellUnits`, `MaximumUsefulCellUnits`, `Snap`, `TryAdvance`
(hiszterézis), `LocalRadiusUnits`, `RenderOrigin.ToLocal/ToAbsolute`. 48 teszt
(`tests/WorldGen.Viewer.LodChunking.Tests/FloatingOriginTests.cs`).
`PlanetOrbitCamera.FloatingOrigin.cs` — az origó **követése** a kamerából +
másodpercenkénti `[ND-19 floating origin]` napló, ami az abszolút és az
origó-relatív felbontást méterben egymás mellé teszi.

**Az élő mérés egy valódi hibát fogott.** Az első Play-menet naplója
`gainFactor=0.5` és `0.3` értéket adott a bolygó-nézeti magasságokon
(199,98 és 364,38 egység egy 100-as sugarú bolygó fölött): a magasság-alapú
cella-szabály 128-256-os rács-lépést választott, tehát az origó MESSZEBB
került a kamerától, mint a bolygóközép — az „origó-relatív" út rosszabb volt
az abszolútnál. A fenti felső korlát + bolygó-nézeti kivétel ezt javítja; a
javítás utáni napló `gainFactor=1.0` bolygó-nézetben és **256,0** a felszín
közelében (28,1 km magasságon 0,566 m → **2,21 mm**), ami bitre egyezik a
fenti táblázat „24 km" sorával. Három regressziós teszt köti ki
(`AnchoredResolutionIsNeverWorseThanTheAbsoluteOne` és a két
hiszterézis-teszt). Napló:
[A12 / ND-19](../history/2026-09-27-a12-nd19-floating-origin.md).

A `default(RenderOrigin)` szándékosan **érvénytelen**, nem „bolygóközép" —
ugyanaz a hibaosztály-védelem, mint az A22 `DeepTimeContext` NaN-os
tengerszintjénél: egy elfelejtett inicializálás ne állíthassa vissza
CSENDBEN az abszolút emittálást.

---

#### 2026-09-27 (A12/2, 2. kör): a geometria-eltolás KÉSZ — ND-19 LEZÁRVA

**A 2. kör EGY SAJÁT ÁLLÍTÁSÁT is megdöntötte.** Az 1. kör lezárása azt írta,
hogy az eltolás „a **test-keretben való renderelésre** váltást igényeli, mert
origó-relatív csúcsok + identitás-transform mellett a bolygó tengelyforgása
nem kifejezhető a transform-mal". **Ez téves volt.** Az origót a
TEST-KERETBEN választjuk (tehát a bolygóval EGYÜTT forog), ezért

    p  →  A + s·R·(p − O)

maga is puszta forgatás + eltolás — pontosan egy Unity-transzform. A Planet
`rotation`-ja VÁLTOZATLANUL a spin/dőlés hordozója marad (ND-153); csak a
`localPosition`-ja kap eltolást. A kameramódok (Free / AxialRotation /
OrbitalFollow) egyetlen sorral sem változtak.

**A választott jelenet-konvenció: KÉT réteg-gyökér, KÉT precízió.**

| réteg | lokális tér | transzform | hol kerekít a `float32` |
|---|---|---|---|
| **Planet** — statikus alap, víz, határvonal, folyó, tó, felhő, StarField, markerek | ABSZOLÚT `p` | `localPosition = A − s·R·O` | a csúcsban ÉS a mátrixban (`s·R·p − s·R·O`), ~0,57 m |
| **PlanetRefinedLayer** — a finomított (dinamikus) réteg | ORIGÓ-RELATÍV `p − O` | `localPosition = A` | csak a KICSI `p − O`-ban, mm alatt |

A nyereség ott jelentkezik, ahol kell: a kamera közelében renderelt,
finomított rétegen. A durva rétegek megtartják a mai kvantálásukat — ez nem
regresszió, mert a kamera közelében a finomított réteg TAKARJA őket
(`ApplyTerrainCoverage` index-maszkja kivágja a statikus háromszögeket), a
coverage szélén pedig a 0,57 m már jóval pixel alatti.

**A második réteg-gyökér TESTVÉR, nem gyerek.** Gyerekként a világ-eltolása
`s·R·O + (A − s·R·O)` lenne, amit a Unity `float32`-ben számol: a kioltás
~0,57 m maradékot hagyna, ÉS `R`-rel változna, tehát forgás közben a
finomított réteg remegne a durvához képest. Testvérként a pozíciója
közvetlenül `A`, kioltás nélkül. Cserébe a forgatást szinkronban kell
tartani — ezért hív a `SunController` `SyncRefinedLayerTransform()`-ot
közvetlenül a `rotation` beállítása után (plusz egy idempotens biztonsági
háló a `LateUpdate`-ben).

**A Planet lokális tere SZÁNDÉKOSAN ABSZOLÚT maradt.** Ez szüntette meg a
változás nagy részét: a LOD-kiválasztás, az overlay-ek, a diagnosztika és a
léptékvonalzó mind `transform.InverseTransformPoint(...)` /
`transform.localToWorldMatrix` úton kérdez — ha a Planet tere origó-relatívvá
vált volna, MINDEGYIK csendben eltolódik. Így egyetlen ilyen hívás sem
változott, és a finomított chunkok
`worldToLocal(Planet) · localToWorld(chunk)` kompozíciója automatikusan
visszaadja az abszolút Planet-lokális koordinátát (ND-72/ND-148 diagnosztika
érintetlen).

**Bit-azonossági szerződés.** Bolygóközepű origónál (`IsPlanetCenter`) a
kivonás pontos 0,0-t von ki, tehát minden emittált csúcs bitre a korábbi —
ezt teszt köti ki (`PlanetCentreOriginEmitsTheBarePlainCastBitForBit`,
`PlanetCentreOriginIsBitIdenticalOnAWholeSampledSurfaceSweep`). A statikus
alap-réteg és a másodlagos rétegek emit-útja ezért SZÁNDÉKOSAN a régi,
`float32`-es alakon maradt (`ToWaterVector3`, `_staticCornerPositions`). A
finomított réteg viszont mostantól végig double-ban számol
(`ToDisplacedPoint`, `ComputeDisplacedRadius` → `double`, `SurfacePoint.Lerp`
a geomorphnál), ezért bolygóközepű origónál is legfeljebb 1 `float32`
ULP-pel — a mai kvantáláson belül — eltérhet a korábbitól, szigorúan
pontosabb irányba.

**Az origó három állapota.** `pending` (amit a kamera javasol) → `requested`
(amivel az ÉPPEN FUTÓ emisszió számol, a többi `_requested*` snapshottal
együtt) → `applied` (amihez a MÁR FELTÖLTÖTT geometria tartozik; EZ vezérli a
jelenet-transzformot és a kamerát). A szétválasztás nem kozmetika: enélkül a
világ-eltolás és a geometria KÜLÖN képkockában váltana, és a kép egy
képkockára egész cellányit (a kamera magasságával összemérhető utat) ugrana.
Mellékhatásként egy ELAVULT kérés alkalmazása is konzisztens: a jelenet
egyszerűen visszaáll annak az origójára.

**A kamera saját pozíciója is double-ban.** Eltolt origónál a mai
`target.position + rot * (0,0,-distance)` alak HASZNÁLHATATLAN: mindkét tag
~bolygósugár nagyságrendű, az eredmény kicsi, tehát a `float32` kivonás
kioltással ~0,57 m hibát ad, ami képkockánként ugrik. Helyette a kamera
test-keretbeli pozíciójából ELŐBB vonjuk ki az origót double-ban, és a
maradékot a `RenderOriginFrame` (origó-relatív) terében adjuk át. A nézési
irány sem a két világpozíció kivonásából jön, hanem a `-camBody`
normalizálásából. `PlanetCenter` módban a régi, változatlan út fut.

**MÉRVE, élő Play módban (2026-09-27, Unity 6000.0.77f1 + HDRP):**

| mérés | érték |
|---|---|
| `applied` origó 28,1 km magasságon | `RenderOrigin(0, 34.25, -94.25, cell=0.25)` |
| `planetLocalPosition` = `A − O` | `(-1.182, -32.052, 861.25)` — **bitre** a várt érték |
| kamera az origó-relatív térben | `(0, 0.0888, -0.0952)`, azaz 0,13 egység (9,6 km) |
| elért felbontás | `appliedResolutionMeters=0.002211` (**2,21 mm**), `appliedGainFactor=256.0` |
| ugyanez 89 km magasságon | 8,845 mm, `gainFactor=64.0` |
| finomított chunk mesh-bounds középpontja | 0,6–2,7 egység (korábban ~100) |
| **A/B kép, fagyasztott idővel, 89 km** | átlagos abszolút eltérés **0,0037/255**, a pixelek **0,001%**-a tér el 8-nál többet |
| **A/B kép, fagyasztott idővel, bolygó-nézet** | átlag 0,65/255, 3,0% a >8 eltérés (a finomított réteg ≤1 ULP-es újraemissziója a partvonalakon) |

**Két valódi hibát fogott az élő mérés** (mindkettő olyan, amit teszt nem
talált volna meg, mert Unity-jelenet-állapotról szólnak):

1. **Visszamaradt réteg a Planet alatt.** A finomított réteg GameObject-jei
   TÚLÉLIK a domain reloadot, a `_refinedLayerRoot` mező viszont nem. Az új
   kód nem találta meg őket, MÁSODIK példányt hozott létre, a régi
   (abszolút koordinátás) pedig aktívan ottmaradt a Planet alatt — mérve egy
   aktív `IndependentWater1`. Javítás: `MigrateRefinedChildrenFromPlanet()`.
2. **A független vízréteg nem épült újra rebase-nél.** A
   `ComputeIndependentWater` szándékosan NEM emittál újra, ha a
   víz-kiválasztás változatlan (a víz nem morphol). Az ORIGÓ viszont nem
   része a kiválasztásnak, tehát rebase után a már feltöltött vízmesh csúcsai
   a RÉGI origóhoz voltak relatívak → a teljes vízréteg elcsúszott. A tünet
   alattomos volt, mert a TEREP helyesen újraépült mellette: mély óceán
   fölött a kamera „vízfelszín helyett tengerfeneket" látott. Javítás:
   `InvalidateIndependentWaterForRenderOrigin()`. A javítás ELŐTT a 89 km-es
   A/B kép 65,2/255 átlagos eltérést adott, UTÁNA 0,0037/255.

**A rebase MÉRT költsége.** Az origó-váltás a finomított réteg TELJES
újraemisszióját kéri (`changedChunks=1138/1138`, `reusedLeaves=0`):
bolygó-nézeti cut-méretnél (70 718 levél) `workerMs=2336`, viszont a FŐ SZÁL
érintetlen — az ND-147 képkockákra bontott feltöltés `stageFrames=22`,
`maxSlice=3,01 ms`, `commitMs=6,9`. A hiszterézis (`RebaseFactor = 1,5`) miatt
rebase csak akkor kell, ha a kamera a saját magasságával összemérhető utat
tett meg.

**Ami NYITVA marad (nem blokkoló, külön tétel).** A durva rétegek
(statikus alap, víz, folyó, tó, felhő, markerek) 0,57 m-es kvantálása
megmarad; ha a zoom valaha 1 km alá megy, ott is kellhet a double-lánc. A
`nearClip` (ma 0,3 egység ≈ 22 km) lejjebb vitele külön, logaritmikus-depth
tétel — ld. fent az elvetett alternatívák közt. Napló:
[A12/2 / ND-19](../history/2026-09-27-a12-2-nd19-floating-origin-geometry.md).

**Ami NYITVA marad (A12/2. kör), és miért külön döntés.** A geometria
tényleges eltolása nem egy cast átírása: ha a mesh csúcsai origó-relatívak és
a `transform` identitás, akkor a bolygó **tengelyforgása többé nem
kifejezhető a transform-mal** (origó körüli forgatás helyett bolygóközép
körüli kellene, amit csak `float32`-ben lehetne visszahozni — azaz pont a
megnyert pontosságot dobnánk el). A helyes megoldás a **test-keretben (body
frame) való renderelés**: a mesh transform identitás, a **kamerát** forgatjuk
a test-keretbe, a fényirányt is oda transzformáljuk. Ez a viewer
jelenet-konvencióját változtatja meg (érintett: `StarField`, `SunController`,
`PlanetGridMesh` sarok-cache `Vector3` → `double`, `radialBias`,
`ToWaterVector3`, a statikus `_staticCornerPositions` tömb). Ezt **csak a
közeli zoom (A11) mellett érdemes megtenni**, mert addig nincs mérhető
látvány-nyeresége — és a mai naplózás pont azt adja meg, mikor kezd
számítani.

### ND-25 — Szomszédszám a kocka sarkainál: 4, nem 3

A §2.4 (`docs/05-milestones.md`) eredeti állítása szerint a 8 kocka-sarkot
tartalmazó tile-oknak 3 szomszédja van 4 helyett. Kimerítő méréssel
(`tools/reference/neighbor_ref.py`, level 3/4/5, MINDEN tile, 4-szomszédos
él-adjacencia: jobbra/balra/fel/le a folytonos uv-térben, majd a már
verifikált vetítéssel visszaprojektálva) ez **nem igazolódott**: mind a 24
lap-sarok-tile pontosan 4 disztinkt, szimmetrikus szomszéddal rendelkezik,
kivétel nélkül, minden mért szinten.

**Döntés:** a `NeighborCount` teszt egységesen `== 4`-et vár el, kivétel
nélkül (`docs/05-milestones.md` frissítve). Indoklás: egy négyzet alakú
cella geometriailag mindig 4 éllel rendelkezik, a kocka sarkán ülő cella
sem kivétel — a 2 lapváltó és 2 lapon-belüli él mind disztinkt szomszédhoz
vezet. A "3" valószínűleg egy MÁSIK, 8-szomszédos (átlós/vertex-adjacencia)
kapcsolódási módra vonatkozik, ahol a kocka geometriai csúcsánál ténylegesen
csak 3 lap találkozik egy pontban — ez akkor válik relevánssá, ha M7-nél
(hidrológia) D8-jellegű átlós folyásirány-modell kerül bevezetésre. Ha ez
bekövetkezik, új ND szükséges a 8-szomszédos séma sarok-viselkedésére; ez
NEM blokkolja a jelen (él-alapú) szomszédsági táblát.

### ND-26 — Trigonometria a csillagászati modul kritikus útján: kockázat elfogadva M3-ra

Az M3 csillagászati modulja (`tools/reference/astronomy_ref.py`: nap-irány,
tengelydőlés, forgás, szub-napponti pont, §27 inszoláció) `Sin`/`Cos`/
`Atan2`/`Asin` függvényeket használ, amik NEM garantáltan bitre azonosak
platformok között (ld. CLAUDE.md táblázat, ND-23b osztály).

**Ellentétben a rács-vetítéssel (ND-24), ez NEM "baked"-elhető egyszer** —
az idő előrehaladtával folyamatosan újraszámolódik (a nap iránya változik),
tehát valódi szimulációs-kritikus úton fut, amint a kimenete (§27 fluxus/
inszoláció) tényleges szimulációs bemenetté válik.

**Döntés (felhasználó jóváhagyta):** a kockázat elfogadva, **M3-ra
korlátozva** — a jelenlegi felhasználás kizárólag a Unity `Directional
Light` rotációját vezérli (vizuális, nem checkpointolt/hash-elt
szimulációs állapot), tehát a platformfüggő ULP-eltérés nem sérti I1-et.

**Sürgősség:** M5 (klíma) előtt **véglegesen** dönteni kell, amikor a §27
fluxus/inszoláció ténylegesen szimulációs bemenetté (és checkpointolt
állapottá) válik — analóg az ND-23b döntéssel (saját polinomiális
implementáció vagy előre számolt tábla + interpoláció közül választva).

**M5-nél visszatérve: ld. ND-27** — a kockázat továbbra is elfogadva marad,
de a hatókör kibővül és egy kemény, visszavonhatatlan határidő kerül rá.

### ND-27 — ND-26 visszatérése M5-nél → VÉGLEGESEN LEZÁRVA (B opció: saját polinomiális implementáció)

Az ND-26-ban rögzített visszatérési pont: a hőmérséklet-modell (§28.2,
Stefan–Boltzmann sugárzási egyensúly, `Math.Pow(x, 0.25)`) ténylegesen a
`OrbitalMechanics.Insolation`-t (Sin/Cos-alapú) használja szimulációs
bemenetként, aminek kimenete (`TemperatureField`, ld. spec §5.4) idővel
checkpointolt állapottá válik.

**Közbenső döntés (M5-nél, felhasználó jóváhagyta):** a kockázat
átmenetileg elfogadva maradt (A opció), MOST MÁR a klíma-modulra is
kiterjesztve. Utána M10-nél (lemezmozgás) és M11-nél (becsapódás) is
kiterjedt ugyanerre a kockázati osztályra, három-négy modulra nőve.

**VÉGLEGES DÖNTÉS: B opció (saját polinomiális implementáció).**
`src/WorldGen.Core/Numerics/DeterministicMath.cs` + Python-referencia
(`tools/reference/deterministic_math_ref.py`) — csak a CLAUDE.md
táblázat szerint GARANTÁLTAN bitpontos alapműveletekre épül
(`+ - * /`, `Math.Sqrt`, `Math.Floor`/`Math.Round` — IEEE-754
roundToIntegral EXAKT specifikáció, nem transzcendens közelítés —, és
nyers bit-manipuláció `BitConverter`-rel):

- **Sin/Cos**: oktáns-redukció (legközelebbi k·π/4-re redukálva
  [-π/8,π/8] tartományba, kizárólag /, -, Floor-lal) + Taylor-polinom +
  szög-összeg azonosság a 8 oktáns EXAKT értékével (0, ±1, ±√2/2).
  Plauzibilitás valódi `Math.Sin/Cos`-hoz képest: < 2e-14.
- **Exp/Ln/Pow**: IEEE-754 bit-dekompozíció (mantissza/exponens
  szétválasztás, mint a C standard frexp/ldexp) + Taylor-sor szűk
  tartományon. `Pow(x,y) = Exp(y·Ln(x))`, x=0,y&gt;0 esetén 0
  (dokumentált konvenció). Plauzibilitás: Exp &lt; 3e-13 relatív, Ln
  &lt; 2e-9 abszolút, Pow &lt; 3e-9 relatív hiba a valódi
  függvényekhez képest.
- A C#/Python implementáció **BITPONTOSAN** (0 tolerancia) egyezik
  1000 tesztvektoron (500 sin/cos + 500 pow) — ez NEM
  tolerancia-alapú teszt, mert mindkét oldal ugyanazt a saját
  algoritmust futtatja, nem a rendszer könyvtárát.

**Bekötve:** `OrbitalMechanics` (RotX/RotZ mátrixok, pálya-irány),
`PlateMotion` (Rodrigues-forgatás), `ImpactCratering` (kráter-képlet
Pow-jai + `cosAngularRadius`), `Temperature` (a negyedik-gyök
sqrt(sqrt(x))-ként EGZAKT — nem is közelítés, jobb mint a
DeterministicMath.Pow).

**ALGORITMUS-VÁLTÁS (dokumentált, szándékos):** az `ImpactCratering`
szög-mintavételezése emellett ÁT LETT TERVEZVE: az eredeti
`angle = 0.5·Acos(1-2u)` helyett korong-alapú elutasításos mintavétel
(Malley-módszer, ugyanaz az elv, mint `SampleUnitVector3`-nál) — ez
KÖZVETLENÜL sin(θ)-t adja, Acos és utólagos Sin nélkül: (x,y) egyenletes
az egységkorongon → sin(θ)=√(1-x²-y²). A kapott szög sűrűsége
bizonyíthatóan pontosan sin(2θ) (levezetés a forráskódban), ugyanaz,
mint az eredeti acos-inverzióé — de MÁS véletlenszám-fogyasztás, tehát
más seed→esemény leképezés, mint a korábbi verzióban. Elfogadható,
mert még nincs perzisztált világ, amit védeni kellene.

**KIVÉTEL — `OrbitalMechanics.SubsolarPoint`:** Math.Asin/Atan2-t
használ, SZÁNDÉKOSAN NEM cserélve. Jelenleg sehol nincs bekötve
szimulációs kritikus útra (csak tesztekben hívott — az `Insolation`
közvetlen pontszorzatot használ, nem szélesség/hosszúság koordinátán
megy át). Ha ez változik, az ND-27 osztálya ide is kiterjed, és ekkor
az `Atan2`/`Asin` is meg kell kapja a `DeterministicMath`-beli
megfelelőjét (jelenleg nincs implementálva — nyitva marad, ha
szükségessé válik).

**Eredmény:** M12 (checkpoint) előtti kötelezettség **teljesítve** —
nincs Math.Sin/Cos/Pow/Acos a jelenlegi szimulációs kritikus úton
(csillagászat, klíma, lemezmozgás, becsapódás). 208/208 teszt zöld.

### ND-28 — M11 becsapódások: hatókör-szűkítés + valós bolygó-sugár bevezetése

**Kérdés:** az M11 (Események) spec-hatóköre széles (becsapódás, vulkán,
rift, lemez-hasadás/egyesülés — §22-23 és a milestone-tábla). Emellett a
kráter geometriájának a rács-térbe (angular méret) illesztéséhez szükség
van egy **valós bolygó-sugárra méterben** — ez eddig sehol nem szerepelt
explicit Core-konstansként (a Unity `radius=100f` szándékosan tetszőleges
vizuális egység, ND-19 miatt még nem valós lépték).

**Döntés (hatókör-szűkítés, a korábbi mérföldkövekével azonos mintát
követve — ld. M5 szél/csapadék, M7 tavak/jég, M10 erózió/eljegesedés
halasztása):**

1. **M11 MVP = csak becsapódás.** Vulkán, rift, lemez-hasadás/egyesülés
   halasztva — nincs önálló numerikus alapjuk még, és a spec-ben is
   kevésbé kidolgozottak, mint a becsapódás (§22, konkrét képletekkel).
2. **Kráter-paraméterek közül csak `diameter` + `depth`.** A `rimHeight`
   és `ejectaRadius` (spec §22.4) halasztva — vizuálisan nem
   elengedhetetlen az első checkpointhoz ("kráterek látszanak"), és
   külön geometria-munkát igényelne (gyűrű alakú kiemelkedés a
   tile-mesh-en).
3. **Új `PlanetConstants.RadiusMeters = 7_420_000.0`** (a spec kanonikus
   példa-bolygója, `docs/00-spec-v1.0.md:2170,2253` — `radiusKm: 7420`).
   Ez **nem** oldja meg ND-19-et (floating origin, Unity-oldali render-
   precízió nagy léptéken) — az változatlanul M9-re marad. Ez csak azt
   mondja ki, hogy a Core matematikája (ami az elevation-t is már most
   is méterben kezeli, ld. M4 dokumentáció) mostantól egy konkrét
   bolygóméretet is ismer, a kráter valós fizikai méretének a rács
   szögtartományára (radián) való átváltásához.
4. **A becsapódás fizikája forrásból ellenőrzött, nem kitalált érték**
   (a projekt "ne bízz emlékezetben" elve szerint, WebSearch-csel
   verifikálva, két független találat egyezésével):
   - Tranziens kráter átmérő: Schmidt & Housen (1987) / Collins, Melosh
     & Marcus (2005) skálázás — `D_tr = 1.161 · (ρᵢ/ρₜ)^(1/3) · L^0.78 ·
     v^0.44 · g^-0.22 · sin(θ)^(1/3)`.
   - Mélység: egyszerű kráterekre mélység/átmérő ≈ 1:5 (közismert,
     széles körben idézett arány).
   - Becsapódási gyakoriság: `ρ(≥D) = 20 · D^-2.4` [1/év], D méterben —
     hatványtörvény NEO-becsapódási ráta-modell.
   - Sebesség: 15-25 km/s (a szakirodalomban idézett átlagos NEO-Föld
     ütközési sebesség 15-21 km/s köré esik).
   - Szög: `P(θ) ∝ sin(2θ)` — ez NEM empirikus, hanem geometriai tény
     (véletlen irányú becsapódás a gömbön), zárt alakban invertálható.
   - Sűrűségek: kőzet becsapódó ~3000 kg/m³, kéreg cél ~2700 kg/m³
     (Föld kontinentális kéreg átlaga, jól ismert érték).
5. **Trigonometria-kockázat (ND-27 osztálya kiterjesztve, nem új ND):** a
   kráter-képlet `Math.Pow`/`Math.Sin`/`Math.Cos`-t használ, ugyanaz a
   kockázati kategória és M12 előtti lezárási határidő vonatkozik rá,
   mint a klímára és a lemezmozgásra.

**Indoklás:** ez a minta megegyezik minden korábbi milestone
hatókör-szűkítésével — kisebb, de fizikailag/matematikailag megalapozott
MVP, explicit deferrállal, nem csendes leegyszerűsítéssel.

### ND-29 — M11 vulkánosság: hatókör szuper-vulkáni (VEI8) eseményekre szűkítve

**Kérdés (architekturális felismerés implementáció közben):** a valós
vulkáni gyakoriság (VEI3-7, forrás: több független cikk, ld. lent)
NAGYSÁGRENDEKKEL magasabb, mint amit egy becsapódás-stílusú, "epochonként
legfeljebb egy esemény" Bernoulli-modell kezelni tud. Számpélda: VEI5
(≥10⁹ m³) gyakorisága kb. 1-2/évtized — 10 000 éves epoch-ra vetítve ez
~1500 várható esemény EGYETLEN epoch alatt, ami szétfeszíti az
egy-epoch-egy-Bernoulli-döntés modellt (amit a becsapódásoknál pont az
tett működővé, hogy ott a ráta valóban `≪ 1`/epoch).

**Döntés (hatókör-szűkítés + a spec saját kategorizálásának követése):**
csak a **szuper-vulkáni (VEI8) eseményeket** modellezzük ezzel az
architektúrával. A "háttér" vulkánosság (VEI3-7, gyakori, apránként
építkező) egy STRUKTURÁLISAN MÁS modellt igényelne (folytonos/perzisztens
vulkáni központ, nem diszkrét ritka esemény) — ez NEM épül meg most,
külön, jövőbeli feladat. Ez a szűkítés NEM önkényes: a spec maga is külön
kategóriaként kezeli a `SuperVolcanicEruption`/`SuperVolcano` fogalmat
(`docs/00-spec-v1.0.md:418,1225-1228,1807`) a §21 általános
"Vulkánosság"-tól elkülönítve — a döntés a spec saját szerkezetét követi,
nem attól idegen egyszerűsítés.

VEI8-nál a gyakoriság (~1-2/millió év) már természetesen illeszkedik a
10 000 éves epoch-modellbe (várható érték ~0.015-0.03/epoch) — ugyanaz a
nagyságrend, mint a becsapódásoknál (0.0126/epoch).

**Fizika (forrásból ellenőrzött, WebSearch, több független forrás
egyezésével):**
- VEI-skála: minden lépés (VEI8=≥10¹² m³ tefra) a valódi, hivatalos
  osztályozás alsó határa (Wikipédia/USGS-szintű konszenzus).
- Méreteloszlás: minden VEI-lépés (10x térfogat) kb. 6-7x ritkább —
  hatványtörvény `N(≥V) ∝ V^-β`, β = log₁₀(6.5) ≈ 0.8129 (a 6-7
  tartomány geometriai közepéből).
- Gyakoriság-kalibráció: VEI8 (~1-2/millió év, több forrás) → ráta
  ≈ 1.5×10⁻⁶/év a küszöbnél.
- Felső biztonsági sapka (dokumentált, nem újra-mintavételezett, ld.
  ImpactCratering ugyanezen mintája): 5×10¹² m³ — a La Garita
  Caldera/Fish Canyon Tuff kitörés (kb. 28 millió éve), a valaha ismert
  LEGNAGYOBB vulkánkitörés valódi becsült térfogata.
- Geometria: pajzsvulkán-kúp, lejtőszög ~6° (idézett tartomány 2-10°
  ill. 4-8°, a kettő közepéből) — `V = π·h³/(3·tan²θ)` kúp-térfogat
  azonosságból `h = (3·V·tan²θ/π)^(1/3)`, sugár `r = h/tanθ`. A `tanθ`
  fix, egyszer kiszámolt konstans (nem futásidejű trigonometria — ld.
  ND-24 "baked" mintája), a köbgyök `DeterministicMath.Pow(x, 1/3)`.
- Pozíció: `PlateBoundaryEffect.TwoBestDots` ÚJRAFELHASZNÁLVA (nem
  duplikálva) — a "gap" (két legközelebbi lemez-mag dot-product
  különbsége) már bevezetett, bitpontos lemezhatár-közelség proxy.
  Elutasításos mintavétel: egyenletes gömbi pont, elfogadva
  `1 - gap/gapScale` valószínűséggel (0, ha `gap ≥ gapScale`) — így a
  pozíciók a lemezhatárok köré koncentrálódnak, tisztán exakt
  aritmetikával (nincs új transzcendens-kockázat).

**Trigonometria-kockázat:** nincs — a `DeterministicMath.Pow`
(köbgyök) és egy fix, konstrukciós idejű `tan(6°)` az egyetlen
nem-egész-aritmetikai elem, ugyanaz az ND-27 lezárt megoldása.

### ND-30 — M12 perzisztencia: hatókör a determinisztikus hash-függvényre szűkítve

**Kérdés:** a spec (§47, §60, §64) egy teljes event-sourcing +
checkpoint + `.worldpkg` fájlformátum + `worldgen verify` CLI rendszert
ír le. Ez a jelenlegi projekt-állapothoz képest (a legtöbb spec-beli
réteg — `AtmosphereLayer`, `ResourceLayer`, `CryosphereLayer` stb. —
még meg sem épült) messze aránytalan lenne egyetlen lépésben megépíteni.

**Döntés (hatókör-szűkítés, a korábbi mérföldkövekével azonos mintát
követve):** ez a lépés CSAK a determinisztikus **hash-függvényt**
(`WorldStateHash`) adja — a `.worldpkg` fájlformátum, a CLI
(`worldgen verify`), az event-sourcing/replay rendszer HALASZTVA.

**Indoklás, ami ezt NEM csonka félmegoldássá teszi:** a Core minden
része MÁR MOST is tiszta függvénye a `(worldSeed, paraméterek, idő)`
hármasnak (ld. `SeaLevelCalibration.ComputeElevationFieldAtTime`,
`ImpactCratering.GenerateCratersUpToTime` stb.) — nincs
"irreverzibilis" szimulációs állapot, amit event-sourcing-gal kellene
tárolni. Ebből következik, hogy a "világ mentése" valójában már ma is
triviális (elég a `WorldDefinition`-t, azaz a bemeneti paramétereket
elmenteni — a state ebből újraszámolható), és amire TÉNYLEGESEN szükség
van már MOST, az egy módszer annak automatizált ellenőrzésére, hogy
"ugyanaz a definíció ugyanazt a világot adja-e minden platformon" (I1).
Pont ezt adja a hash-függvény, a nagyobb fájlformátum/CLI-réteg nélkül is.

**Módszer:** SHA-256 egy `(TileId → double)` mezőn, KANONIKUS
(`TileId.Value` szerint növekvő, nem `Dictionary` bejárási sorrend)
sorrendben, EXPLICIT big-endian bájtsorrendben (nem a platform natív
bájtsorrendjére támaszkodva). SHA-256 GARANTÁLTAN determinisztikus
(bit-manipuláció, nem transzcendens közelítés) — más kockázati
osztály, mint a Math.Sin/Cos/Pow (ND-27), nem igényel ND-kockázatvállalást.

227/227 teszt zöld, a Python-referenciával (`state_hash_ref.py`)
BITPONTOS SHA-256 egyezés.

### ND-31 — M13 fraktál-zaj: a CrustElevation fehér zaja lecserélve térben koherens fBm-re

**Kérdés:** a `CrustElevation` osztály saját dokumentációja MÁR M4 óta
explicit módon felvetette (nem ND-ként, hanem "később finomítható
egyszerűsítésként"): az §13.2 "fraktál részlet" (F) helyett egyszerű,
tile-onként FÜGGETLEN (fehér zaj-szerű) magasság-jitter volt használva —
NEM térben koherens fBm/Perlin. Ez okozta a korábban (M4/M13
vizsgálatnál) dokumentált "túl szabályos" Voronoi-cella-szerű
partvonal/hegylánc-benyomást. A felhasználó explicit kérésére ez a
lépés MOST lezárva (korábban "A" opcióval — halasztás — döntött).

**Döntés:** valódi 3D gradiens-zaj (`FractalNoise`, Ken Perlin
"Improving Noise" 2002 kvintikus fade-görbével) + fBm (5 oktáv,
persistence=0.5, lacunarity=2.0 — standard, széles körben idézett
paraméterek) váltja fel a fehér zajt. A rácspont-gradiensek hash-elése
ÚJRAFELHASZNÁLJA a már verifikált `SampleUnitVector3`-at (nincs új,
ellenőrizetlen hash-függvény). A kvintikus fade-görbe és a trilineáris
interpoláció TISZTA POLINOM — nincs új transzcendens-kockázat (ND-27
osztálya nem bővül).

**Hatókör:** csak az "F: fractal detail" komponens — a §13.2 teljes
`H0(p) = w_c·C(p) + w_f·F(p) + w_r·R(p) + w_v·V(p)` képletéből a
kontinens-maszk (C) továbbra is a lemez-Voronoi-struktúra, a ridged
mountains (R) és volcanic field (V) komponensek NEM külön modellezettek
most (R részben már létezik `PlateBoundaryEffect` uplift formájában, V
az M11 szuper-vulkán eseményekben — ezek nem lettek most újratervezve).

**BLAST RADIUS (dokumentált, szándékos):** ez ELTÉRŐ, magasabb kockázatú
kategória, mint a korábbi ND-k, mert az `M4` bázis-elevációt módosítja,
amire SZINTE MINDEN azóta épült modul épül. Érintett, újragenerált
tesztvektorok: `crust_elevation_vectors.json`, `plate_boundary_vectors.json`,
`hydrology_vectors.json`, `features_vectors.json`, `state_hash_vectors.json`.
`TEST-EARTH-001` (50-75% víz, ≥2 kontinens) VÁLTOZATLANUL teljesül (65.00%
víz, 2 kontinens — a pontos tile-számok kis mértékben eltolódtak:
7346+1221 → 7343+1220). A régió-szegmentálás régió-száma is változott
(16→8) — ez a koherens zaj miatt módosult vízgyűjtő-topológia
természetes következménye, nem hiba.

**API-változás:** `CrustElevation.BaseElevation` és
`PlateBoundaryEffect.ElevationWithBoundary` mostantól a pozíciót
(x,y,z) használja a zaj-kiértékeléshez a korábbi `tileIdValue` helyett
(a `tileIdValue` paraméter megmaradt `ElevationWithBoundary`-n, csak
belül nem használt — visszamenőleges hívási kompatibilitás). A Unity
oldal NEM érintett (csak `ElevationWithBoundary`-t hívja, aminek a
publikus szignatúrája változatlan).

234/234 teszt zöld, a Python-referenciával BITPONTOS egyezés minden
érintett láncban.

### ND-32 — ND-31 vizuális visszajelzés alapján: erősebb zaj + kéreg-típus-tudatos határhatás

**Kérdés:** az ND-31 Unity-beli vizuális ellenőrzésekor a felhasználó
konkrét, jól diagnosztizálható hibákat talált:
1. A fraktál-zaj gyakorlatilag észrevehetetlen volt.
2. A kontinensek továbbra is szinte pontosan a lemez-Voronoi-cellákkal
   egyeztek.
3. A lemezhatárokon fix magasságú, "falszerű" kiemelkedés látszott.
4. **Óceáni-óceáni lemezhatárokon is** megemelkedett az óceán
   látszólagos szintje.
5. Óceán-kontinens határon a szárazföld irreálisan kidudorodott.

**Diagnózis (mérve, nem találgatva):** egy statisztikai teszt
kimutatta, hogy a "part-vonal - lemezhatár egyezés" mértéke (0.28%
eltérés) FÜGGETLEN volt a zaj-amplitúdótól (500-3500m tartományban
tesztelve) — ez bizonyította, hogy nem a zaj gyengesége az elsődleges
ok, hanem a `PlateBoundaryEffect.BoundaryUplift` egységes (kéreg-
típustól független) alkalmazása, ami minden határon (óceáni-óceáni is)
ugyanazt a max. 1500m-es kiemelkedést adja hozzá — ez már önmagában a
tengerszint fölé emelhet óceáni tile-okat.

**Döntés (két összehangolt javítás):**
1. **`CrustElevation.NoiseAmplitudeMeters`: 500 → 2000m.** A korábbi
   érték eltörpült a határ-uplift (1500m) és az óceán/kontinens
   alapszint-különbség (4800m) mellett — Unity-oldalon
   `elevationScale`-lel szorozva a zaj-hozzájárulás vizuálisan
   gyakorlatilag nem volt megkülönböztethető a sima felülettől.
2. **`PlateBoundaryEffect.BoundaryUplift` kéreg-típus-tudatos:** ha a
   két legközelebbi lemez MINDKETTŐ óceáni, az uplift
   `DefaultOceanicOceanicUpliftFactor = 0.15`-tel szorzódik (nem
   nullázva — a valóságban óceáni-óceáni konvergens határon vulkáni
   szigetívek épülnek, csak keskenyebb/alacsonyabb relieffel, mint egy
   kontinentális ütközési zóna). `TwoBestDots` új túlterhelést kapott,
   ami a két legközelebbi lemez INDEXÉT is visszaadja (a kéreg-típus
   lekérdezéséhez) — a régi, csak dot-productot visszaadó verzió
   megmaradt (backward-kompatibilis, `VolcanicEruption` ezt hívja
   tovább, mert annak nincs szüksége kéreg-típusra).

**MÉG NYITVA (a felhasználóval egyeztetve, külön kérésre indítva):** a
"kontinensek pontosan a lemez-Voronoi-cellákkal egyeznek" probléma
STRUKTURÁLIS — a kéreg-típus jelenleg lemez-szinten bináris (§14.1
Plate struct mintájára), a nearest-seed plate-hozzárendelés pedig
mindig geometriailag sima (nagykör-szerű) határvonalat ad. Ennek valódi
javítása **domain warping**-ot igényelne (a spec §13.1 is név szerint
említi) — a pozíciót magát eltorzítani zajjal, MIELŐTT a lemez-
hozzárendelés/gap-számítás megtörténne. Ez jóval nagyobb hatókörű
változás lenne (a `PlateGeneration.AssignPlate` szignatúráját és
gyakorlatilag minden hívóját érintené) — külön döntés kell róla, nem
ennek az ND-nek a része.

235/235 teszt zöld, Python-referenciával bitpontos egyezés.

### ND-33 — ND-32 után is "katasztrofálisan" gyenge zaj: sima fBm lecserélve ridged multifractalra

**Kérdés:** az ND-32 (4x amplitúdó + kéreg-típus-tudatos uplift) Unity-
beli ellenőrzésekor a felhasználó szerint a zaj továbbra is
"katasztrofálisan" gyenge/észrevehetetlen maradt.

**Diagnózis:** a sima fBm (Perlin-alapú gradiens-zaj összege) ELVE
lekerekített, sima dombokat ad — ez a technika lényegéből fakad, nem
hangolási hiba. Bármennyire is nő az amplitúdó, a JELLEGE (lágy,
folytonos átmenetek) vizuálisan "simának" hat, nem "zajosnak".

**Döntés:** a sima fBm helyett **ridged multifractal** (a spec §13.1
maga is név szerint felsorolja, mint külön noise-családot) —
oktávonként `(1-|zaj|)²`, ami ÉLES gerinceket ad ott, ahol az alap
gradiens-zaj nullát metsz, alapvetően más — sokkal kontrasztosabb —
vizuális jelleggel, mint a sima fBm. Emellett az amplitúdó tovább nőtt
(2000→3000m).

**Módszer:** `FractalNoise.RidgedMultifractal` — ugyanazt a már
verifikált `GradientNoise3D` primitívet és oktáv-összegzési vázat
használja, mint az `Fbm` (nincs új hash-függvény, nincs új
transzcendens-kockázat) — csak az oktávonkénti kombinálás formulája más
(`(1-|n|)²` `n` helyett). A `CrustElevation.BaseElevation` az `Fbm`
hívást `RidgedMultifractal`-ra cserélte, `(r-0.5)·2` előjeles
átalakítással (a ridged kimenet [0,1]-hez közeli, ~0.7 átlaggal — a
`(r-0.5)·2` visszaadja a szimmetrikus, mindkét irányba ható
perturbáció-jelleget).

**Hatás (mérve):** a nyers elevációtartomány -4872…+3674m-re nőtt
(korábban -4746…+1599m) — a legmagasabb kontinentális csúcs több mint
duplájára nőtt az alapszinthez (800m) képest. A vízgyűjtő-topológia is
jelentősen összetettebbé vált (11→35 régió) — közvetlen jele annak,
hogy a domborzat ténylegesen sokkal tagoltabb lett.

**Még mindig nyitva (változatlanul, ld. ND-32):** a "kontinensek
pontosan a lemez-Voronoi-cellákkal egyeznek" strukturális kérdés —
domain warping nélkül ez továbbra is fennáll, függetlenül a zaj
erősségétől/jellegétől, mert a lemez-HOZZÁRENDELÉS (nem csak az
eleváció) geometriailag sima marad.

237/237 teszt zöld, Python-referenciával bitpontos egyezés (`ridged`
mező hozzáadva a meglévő zaj-tesztvektorokhoz).

### ND-34 — Óceán-fenék tompítása + regionális "hegyvidékiség" maszk

**Kérdés:** az ND-33 (ridged multifractal) Unity-beli ellenőrzésekor a
felhasználó két további, konkrét problémát talált: (a) az óceánfenék is
túl erősen "hegyesnek" látszott (irreális — a valóságban az óceáni
relief szelídebb, a középóceáni hátak/árkok kivételével, amiket nem
modellezünk külön); (b) a durvaság EGYENLETES volt az egész
szárazföldön, holott realisztikusabb, ha van sík/fennsík ÉS hegyvidék
is, nem mindenhol ugyanolyan "zajos" a felszín.

**Döntés (két összehangolt javítás):**
1. **`OceanicNoiseFactor = 0.25`** — óceáni tile-ok a zaj negyedét
   kapják csak.
2. **`MountainMask`** — külön, ALACSONY FREKVENCIÁS (2.5, szemben a
   ridged részlet 8-as alapfrekvenciájával), 3 oktávos, SIMA fBm (nem
   ridged!) adja meg REGIONÁLISAN [0,1] tartományban, mennyire
   érvényesüljön a ridged részlet-zaj adott a helyen — `normalized =
   clamp01(m·1.3+0.5)`, majd `normalized^1.5` (a hatványozás a legtöbb
   területet inkább sík felé tolja, ritkábban ad dramatikusan durva
   zónát). Mérve: a maszk kb. 6.5%-a esik <0.1 alá (gyakorlatilag sík),
   ~20%-a >0.5 fölé (kifejezetten hegyvidéki) — valódi, nem egyenletes
   eloszlás.

**Módszer:** mindkét primitívet (`FractalNoise.Fbm` a maszkhoz,
`RidgedMultifractal` a részlethez) ÚJRAFELHASZNÁLJA — nincs új
hash-függvény vagy transzcendens-kockázat, csak eltérő
frekvencia/oktáv-paraméterezéssel és a végén szorzással kombinálva:
`noise · mask · amplitude`.

**Hatás (mérve):** a nyers elevációtartomány -4119…+2790m-re szűkült
(kevésbé szélsőséges, mint ND-33 -4872…+3674m-je — a maszk miatt csak a
terület egy RÉSZÉN érvényesül a teljes ridged amplitúdó). A stray
(5 tile alatti) szigetek eltűntek (10→2 nyers komponens), a
vízgyűjtő-szám 35→12-re csökkent — mérsékeltebb, de még mindig sokkal
tagoltabb domborzat, mint az ND-31 előtti fehér zaj.

`TEST-EARTH-001` VÁLTOZATLANUL teljesül (65.00% víz, 2 kontinens,
7375+1227 tile).

237/237 teszt zöld, Python-referenciával bitpontos egyezés minden
érintett láncban.

### ND-35 — Lemezhatár-kiemelkedés is a regionális hegyvidékiség-maszkkal szorozva

**Kérdés:** a vízfelszín-réteg (lásd fentebb, a `PlanetGridMesh`
`BuildOceanShell`-je) hozzáadása után a felhasználó észrevette: a
szárazföld PARTI SÁVJA a tengerszinthez képest irreálisan magas maradt.

**Diagnózis:** az óceán-kontinens határ MINDIG lemezhatár is — a
`PlateBoundaryEffect.BoundaryUplift` viszont az ND-34 `MountainMask`
bevezetése ELLENÉRE sem volt vele modulálva, csak a domborzat
fraktál-részlete. Emiatt a parti sáv MINDIG maximális (akár 1500m-es)
kiemelkedést kapott, függetlenül attól, hogy ott sík vidéknek vagy
hegyvidéknek "kellene" lennie — irreális "falat" húzva a tengerszint
fölé szinte minden parton.

**Döntés:** a `BoundaryUplift` mostantól UGYANAZZAL a
`CrustElevation.MountainMask` regionális maszkkal szorzódik, mint a
domborzat ridged részlete — sík régióban (a maszk ~6.5%-a) a parti sáv
sem kap érdemi kiemelkedést, hegyvidéki régióban (a maszk ~20%-a)
viszont továbbra is a teljes, dramatikus kiemelkedést kapja (mint a
valóságban pl. az Andok a csendes-óceáni parton — kivétel, nem
általános szabály).

**Mérve:** az érintett tile-okon az ÁTLAGOS uplift 543.1m-ről
192.1m-re csökkent (a maximum plafonérték, 1500m, változatlan maradt
ott, ahol a maszk épp magas). A vízgyűjtő-szám 12→51-re nőtt — a parti
sáv most sokkal tagoltabb, kevésbé egyenletesen "fal-szerű".

`TEST-EARTH-001` VÁLTOZATLANUL teljesül.

237/237 teszt zöld, Python-referenciával bitpontos egyezés.

### ND-36 — Domain warping: a lemez-Voronoi HATÁRA is organikusan hullámzik, nem csak a rárakott zaj

**Kérdés (a felhasználó ismételt visszajelzése, ND-32-ben már diagnosztizálva,
de akkor külön döntésre halasztva):** a lemez-HOZZÁRENDELÉS
(`PlateGeneration.AssignPlate`) egy tiszta "legközelebbi mag" (nearest-seed)
Voronoi-felosztás a gömbön — ez MATEMATIKAILAG MINDIG sima, nagykör-ív-szerű
határvonalat ad, FÜGGETLENÜL attól, mennyi zajt teszünk az elevációra
utólag (ND-31→ND-35 mind csak az ELEVÁCIÓT tette változatosabbá). Emiatt a
kontinensek/lemezhatárok (és ezzel a partvonal) továbbra is irreálisan
"geometrikusnak" hatottak.

**Döntés:** **domain warping** (a spec §13.1 is név szerint említi) — a
lemez-hozzárendeléshez és a lemezhatár-közelség ("gap",
`PlateBoundaryEffect.TwoBestDots`) számításhoz használt POZÍCIÓT egy
zaj-alapú eltolással torzítjuk el, MIELŐTT a legközelebbi-mag
keresés/gap-számítás megtörténne. Új, motorfüggetlen modul:
`src/WorldGen.Core/Terrain/DomainWarp.cs` (`DomainWarp.WarpPosition`).

**Módszer:** három FÜGGETLEN `FractalNoise.Fbm`-kiértékelés (dx,dy,dz) —
a MÁR VERIFIKÁLT primitívet ÚJRAFELHASZNÁLJA VÁLTOZATLAN szignatúrával
(nincs új hash-függvény, nincs új `RandomProperty`). A három komponens
dekorrelációját KIZÁRÓLAG fix, egymástól és nullától távoli bemeneti
koordináta-eltolással oldottuk meg (pl. `fbm(seed, x+7.13, y+2.71, z+9.01, ...)`
a dx-hez), mert a `Fbm` API nem vesz fel `property_id` paramétert. Az
eltolt (wx,wy,wz) vektort a nyers (x,y,z)-hez adva, majd egységvektorra
renormalizálva kapjuk a warpolt pozíciót — a renormalizálás
**KÖZVETLEN OSZTÁSSAL** történik (`wx/length`, nem `wx*(1/length)`
reciprok-szorzással), hogy bitre egyezzen a Python referenciával (ez volt
az első portolási kísérlet egyetlen ULP-eltérése, ld. lent).

**Paraméter-hangolás (empirikusan mérve, `tools/reference/domain_warp_ref.py`):**
`Strength=1.0, Frequency=2.0, Octaves=3` → átlagos szögeltolódás ~9.0 fok
(8000 véletlen ponton mérve, célzott nagyságrend 5-15 fok volt), és a
lemezhatárok közelében (gap < `PlateBoundaryEffect.DefaultGapScale`=0.04)
lévő pontok ~40%-ánál változik meg az `AssignPlate` eredménye a warp
hatására — érdemi, de nem kaotikus/domináló hatás.

**Hatókör (szándékos, indokolt):** a lemez-HOZZÁRENDELÉS és a
HATÁR-KÖZELSÉG kapja a warpolt pozíciót. A TÉNYLEGES ELEVÁCIÓ-ZAJ
kiértékelése (`CrustElevation.BaseElevation`, `CrustElevation.MountainMask`)
VÁLTOZATLANUL a NYERS pozíciót kapja — a már jól hangolt (ND-31→ND-35)
zaj-textúra ne változzon meg alapvetően emiatt. Négy hívási hely lett
konzisztensen bekötve: `SeaLevelCalibration.ComputeElevationFieldWithSeeds`
(AssignPlate előtt), `PlateBoundaryEffect.BoundaryUplift` (TwoBestDots
előtt, a `MountainMask` hívás NYERS pozíción marad), `VolcanicEruption.
SamplePositionNearBoundary` (az elfogadási gap-döntés előtt, a
VISSZAADOTT pozíció nyers marad), és a Unity `PlanetGridMesh.
ComputeDisplacedRadius` (AssignPlate előtt, konzisztensen a Core-oldali
logikával — különben a sarok-alapú megjelenítés nem-warpolt határokat
mutatna a tile-közepű adatokhoz képest).

**Hibakeresési tanulság (dokumentálva, mert újra elő fog jönni):** az első
C# port a Python `wx/length` osztást tévesen `wx*(1.0/length)`
reciprok-szorzásra cserélte (más C#-beli mintákat, pl.
`DeterministicRandom.SampleUnitVector3`-ot követve, ahol a Python IS
reciprok-szorzást használ) — ez 3, egymástól függő tesztfájlban okozott
egyetlen-ULP-differenciát (`PlateBoundaryEffectVectorFileTests`,
`FlowNetworkVectorFileTests`, `WorldStateHashVectorFileTests`), amíg ki
nem derült, hogy a `domain_warp_ref.py` KÖZVETLEN osztást használ, NEM a
projekt többi helyén megszokott reciprok-szorzás mintát. A tanulság:
**minden egyes osztás/normalizálás műveletet külön ellenőrizni kell a
Python referenciában**, nem szabad feltételezni, hogy egy korábban látott
minta (reciprok-szorzás) mindenhol érvényes — ez pontosan az a fajta
csendes IEEE-754 eltérés, amit a CLAUDE.md lebegőpontos táblázata figyelmeztet.

**Mért hatás:** `TEST-EARTH-001` VÁLTOZATLANUL teljesül (65.00% víz), de a
kontinens-szám 2→6-ra nőtt (7462+1053+37+30+7+6 tile, a korábbi 2 nagy
kontinens több, kisebb darabra esett szét — ez a warp SZÁNDÉKOLT hatása,
nem hiba). A régió-szám 51→100-ra nőtt. A parti magasság (tengerszinthez
képesti relatív eleváció) statisztikailag alig változott (ld. ND-37 —
kiderült, hogy ez NEM elsősorban a lemezhatár-geometria problémája volt).

249/249 teszt zöld (12 új: `DomainWarpTests.cs`), Python-referenciával
BITPONTOS egyezés minden érintett láncban (`domain_warp_vectors.json`,
és az újragenerált `plate_boundary_vectors.json`, `hydrology_vectors.json`,
`features_vectors.json`, `state_hash_vectors.json`, `volcanism_vectors.json`).
A `plate_vectors.json` és `crust_elevation_vectors.json` VÁLTOZATLAN
maradt (a mögöttes `AssignPlate`/`BaseElevation` függvények szignatúrája
és logikája nem változott — csak a HÍVÓ oldal ad nekik más pozíciót).

### ND-37 — a kalibrált tengerszint mélyen az óceáni-kéreg elevációtartományba esett, nem egy valódi part-átmenetnél — MEGOLDVA (2. irány: decorrelálás)

**Kérdés (ND-36 domain warping vizsgálata közben derült ki, mérve, nem
találgatva):** a felhasználó eredeti panasza ("a part túl magas a
tengerszinthez képest") a domain warping UTÁN is fennállt — a parti sáv
(óceáni szomszéddal rendelkező szárazföld-tile-ok) átlagos elevációja a
kalibrált tengerszinthez képest ELŐTTE ~4255m, UTÁNA ~4072m volt (level 6,
teljes mező) — a warp csak ~4%-ot javított, elhanyagolható.

**Gyökérok (mérve):** a `SeaLevelCalibration.CalibrateSeaLevel`
percentilis-módszere a `TargetWaterFraction`=0.65-nél kalibrál. A
`CrustElevation.DefaultOceanicProbability`=0.55 (lemez-szintű Bernoulli-
valószínűség) miatt a TILE-SÚLYOZOTT óceáni-lemez-arány a mért világon
véletlenül ~66.7% — gyakorlatilag EGYBEESIK a célzott víz-aránnyal (65%).
Emiatt a kalibrált tengerszint (mérve: -3220.7m) NEM egy valódi
kontinentális-perem elevációs átmenetnél metsz, hanem MÉLYEN az óceáni
kéreg elevációtartományán (`OceanicBaseMeters`=-4000m körül) BELÜL — a
"parti" tile-ok (elevation ≥ sea_level) valójában az óceáni-kéreg-eloszlás
FELSŐ SZÉLÉN lévő tile-ok, aminek a tengerszinthez viszonyított relatív
magassága ezért matematikailag nagy szám lesz, még ha a nyers eleváció
önmagában plauzibilis (néhány száz-pár ezer méteres) tartományban is van.

**Miért nem oldható meg egyszerű konstans-hangolással (mérve, kizárva):**
a `ContinentalBaseMeters`/`DefaultUpliftMaxMeters`/`NoiseAmplitudeMeters`
együttes DRASZTIKUS csökkentése is csak ~4240m→~3712m-re (kb. 12%)
mozdítja az átlagot — mert a probléma nem ezekben a konstansokban van,
hanem magában a percentilis-kalibráció és a lemez-szintű bináris
kéreg-típus KÖLCSÖNHATÁSÁBAN. Az `OceanicBaseMeters` közvetlen
csökkentése (pl. -2000m-re) erősen hat (~2460m-re csökkenti az átlagot),
de irreálisan sekély óceánt eredményezne — fizikailag rosszabb
kompromisszum, mint a jelenlegi állapot.

**Eredetileg NYITVA HAGYVA, NEM egyetlen konstans átírásával csendben
"lezárva".** Három lehetséges jövőbeli irányt jegyeztünk fel:
1. A kéreg-típus finomítása tile-szintű gradiensre (nem bináris
   lemezenkénti Bernoulli) a lemezhatárok közelében.
2. A `TargetWaterFraction` és `DefaultOceanicProbability` explicit
   szétválasztása/decorrelálása.
3. A "parti magasság" metrika újragondolása.

**MEGOLDVA — a 2. irány (decorrelálás) empirikusan bevált.** Python
referenciában (`tools/reference/crust_elevation_ref.py`,
`sea_level_ref.py`) lemértük `OCEANIC_PROBABILITY` ∈
{0.55, 0.50, 0.45, 0.40, 0.35, 0.30} értékekre (world_seed=0xA7C944210000,
plateCount=20, level=6; a `TargetWaterFraction` változatlanul 0.65
maradt). A plate-szintű Bernoulli-döntés miatt lépcsős platókban változik
az eredmény (0.55≡0.50, 0.45 önálló, 0.40 önálló, 0.35≡0.30):

| oceanic_prob | tile-súlyozott óceáni-arány | tengerszint | parti sáv átlagos relatív magassága | kontinensek (≥5 tile) | TEST-EARTH-001 |
|---|---|---|---|---|---|
| 0.55 (régi alap) | 66.1% | -3220.7 m | **4072.0 m** | 6 | PASS |
| 0.45 | 36.7% | +1217.1 m | 252.9 m | 41 | PASS |
| **0.40 (választott)** | **35.1%** | **+1235.1 m** | **242.6 m** | **44** | **PASS** |
| 0.35 / 0.30 | (level-5 mintán mérve, azonos plató) | — | ~292.9 m (level 5) | — | PASS |

**Választás: `DefaultOceanicProbability` = 0.40.** Indoklás: a 0.45/0.40
pár közel azonos, drasztikus javulást ad (~93-94%-os csökkenés a parti
magasságban) — 0.40 mérve minimálisan jobb (242.6 vs 252.9 m) ÉS a
0.35/0.30 platóhoz képest kevésbé fragmentált világot ad. A hipotézis
igazolódott: 0.40-nél a tile-súlyozott óceáni-arány (35.1%) messze a
65%-os víz-cél ALATT van, ezért a percentilis-kalibráció kénytelen a
legalacsonyabb fekvésű KONTINENTÁLIS tile-okba is belenyúlni ("kontinentális
self" hatás) ahelyett, hogy az óceáni-kéreg-eloszlás tetejénél állna meg —
ez adja a drámai javulást, fizikailag is plauzibilis "elárasztott
kontinentális-perem" értelmezéssel.

**Mellékhatás (tudatosan vállalt kompromisszum):** a kontinensszám 6→44-re
nő (a 41 kisebb sziget/kontinens mérete 5-617 tile között, a 3 legnagyobb
[3837, 1582, 1321] adja a szárazföld ~67%-át) — ez a magasabb, kontinentális
tartományba eső tengerszint természetes következménye: sok alacsony fekvésű
terület elárasztásra kerül, szigetvilágot hozva létre a korábbi 2 nagy
kontinens helyén (ND-36 már megkezdte ezt a fragmentációs trendet
2→6-tal, ND-37 tovább viszi 6→44-re). Ez NEM hiba, dokumentált,
szándékos kompromisszum a realisztikusabb part-átmenetért cserébe.

**Kódváltozás:** `src/WorldGen.Core/Tectonics/CrustElevation.cs` —
`DefaultOceanicProbability` 0.55→0.40. Downstream Python
tesztvektor-fájlok újragenerálva és bitre lemásolva a C# tesztadatokba:
`crust_elevation_vectors.json`, `plate_boundary_vectors.json`,
`hydrology_vectors.json`, `features_vectors.json`,
`state_hash_vectors.json` (mind változott). `volcanism_vectors.json` és
`plate_vectors.json` VÁLTOZATLAN maradt (ellenőrizve `git diff`-fel) — sem
a szuper-vulkán pozíció/gyakoriság-mintavétel, sem a lemez-mag-generálás
nem függ a kéreg-típustól. Hardcode-olt teszt-elvárások frissítve:
`SeaLevelCalibrationTests.ContinentSizesMatchPythonReferenceExactly`
(új 44-elemű méretlista), `FeaturesTests.
MatchesPythonReferenceContinentsAndRegionsExactly` (6→44 kontinens,
100→398 régió).

**`TEST-EARTH-001` állapota:** VÁLTOZATLANUL teljesül (65.00% víz, 44
kontinens ≥2).

249/249 teszt zöld (Debug és Release is) — nincs új tesztfájl, a meglévő
KAT/struktúra-tesztek a frissített vektorokkal és elvárásokkal futnak.

### ND-38 — Térfogat-megmaradás alapú tengerszint a deep-time (M10) láncban — MEGOLDVA

**Kérdés:** a `SeaLevelCalibration.CalibrateSeaLevel` percentilis-módszere
minden lekérdezéskor PONTOSAN a `targetWaterFraction` (0.65) arányú tile-t
teszi víz alá, FÜGGETLENÜL attól, hogy a domborzat hogyan alakul. Az M10
deep-time láncban (`PlanetGridMesh.Build()`) minden `deepTimeMyr` értékre
újra lefut ez a kalibráció a lemezmozgás miatt megváltozott elevációs
mezőn — ez azt jelenti, hogy a víz-arány MINDIG pontosan 65% marad, akárhogy
is nőnek a hegyek vagy ütköznek a kontinensek. Fizikailag ez hibás: a
víz TÉRFOGATÁNAK kéne megmaradnia, nem az aránynak.

**Döntés:** a `t=0` világállapotból (a meglévő, változatlan percentilis-
kalibrációval) egy dimenziómentes víztérfogat-proxyt számolunk (`V0`,
`ComputeFloodedVolumeProxy` — a már bevett tile-egyenletes-terület
közelítés, ld. `FeatureMetrics.AreaTiles` és ND-24 precedense, NINCS
valódi gömbfelszín-területsúlyozás). Minden későbbi `t`-nél ehhez a
RÖGZÍTETT `V0`-hoz tartozó egyensúlyi tengerszintet keressük meg
(`CalibrateSeaLevelByVolume`) — a `Σ max(0, H - elevation)` monoton növekvő
függvénye `H`-nak, ezért egyértelműen konvergál egyetlen FIX (60)
iterációjú bináris kereséssel.

**Determinizmus-érvelés (I1):** a bináris kereső FIX iterációszámú `for`
ciklus, NEM tolerancia-alapú `while` — egy tolerancia-alapú leállás
platformfüggő lebegőpontos kerekítési különbségek miatt eltérő
lépésszámban állhatna meg (pl. egy `Math.Abs(vol - target) < eps`
feltétel az utolsó bitben ingadozhat két platform között), míg a fix
iterációszám mindig ugyanazt az útvonalat futja be, bitre reprodukálhatóan
minden platformon és szálszámon.

**`t=0` visszamenőleges kompatibilitás — bitre garantált, NEM csak mérve
közelítő:** a `PlanetGridMesh.Build()`-ben a `deepTimeMyr == 0.0` ág
VÁLTOZATLANUL a régi `CalibrateSeaLevel` percentilis-hívást futtatja —
nincs új számítási lánc `t=0`-nál, tehát a már vizuálisan jóváhagyott
render bitre ugyanaz marad. A térfogat-alapú visszaoldás csak `t>0`-nál
fut. Ettől függetlenül Python referenciával (`tools/reference/
sea_level_ref.py`, "ND-38" szakasz) LEMÉRTÜK, hogy a két módszer `t=0`-nál
mennyire közelít egymáshoz — a numerikus pontosság dokumentálásához: 60
lépéses bináris kereséssel a visszaoldott szint és a percentilis-szint
közötti eltérés `6.82e-13 m` volt (world_seed=0xA7C944210000,
plateCount=20, level=6, sea_level≈1235.106 m) — gyakorlatilag a dupla
lebegőpontos kerekítési zaj szintjén, messze a méteres nagyságrendű
elevációs skálához képest elhanyagolható. A C# oldali teszt
(`SeaLevelCalibrationVolumeBasedTests.
AtTimeZeroVolumeBasedLevelMatchesPercentileLevel`) `1e-6 m` toleranciát
követel meg, jóval a mért pontosság felett hagyva biztonsági margót.

**Mért hatás — a víz-arány TÉNYLEGESEN elmozdul 65%-tól (a funkció
bizonyítéka, nem csak elméleti lehetőség):** Python referenciával mérve
(world_seed=0xA7C944210000, plateCount=20, level=5, `V0`=11 359 571.377
proxy-egység, a `t=0` percentilis-kalibrált 64.9902%-os víz-arányból
számolva):

| `timeMyr` | tengerszint (rögzített `V0` mellett) | mért víz-arány | eltolódás a 65%-os t=0 céltól |
|---|---|---|---|
| 0 | 1244.78 m | 64.99% | (bázis) |
| 50 | 849.38 m | 41.81% | **−23.19 százalékpont** |
| 100 | 1652.15 m | 85.66% | **+20.66 százalékpont** |
| 250 | 1969.43 m | 93.70% | **+28.70 százalékpont** |
| 500 | 1206.86 m | 64.18% | −0.82 százalékpont |

Minden mért `t`-nél a bináris kereső a `V0`-hoz `≤3.73e-9` abszolút
eltéréssel konvergált (ellenőrizve: `flooded_volume_proxy` a visszaoldott
szintnél). A víz-arány egyetlen mért pontnál sem omlott össze 0%-ra vagy
100%-ra (fizikailag plauzibilis tartományban maradt), miközben jól látszik,
hogy a korábbi, örökké-pontosan-65%-os viselkedés megszűnt.

**Kódváltozás:**
- `tools/reference/sea_level_ref.py`: `flooded_volume_proxy`,
  `calibrate_sea_level_by_volume` (60 lépés alapértelmezett), valamint
  `compute_elevation_field_at_time` (a `plate_motion_ref.plate_seed_at_time`
  felhasználásával) + `__main__` verifikációs szakasz.
- `src/WorldGen.Core/Tectonics/SeaLevelCalibration.cs`:
  `ComputeFloodedVolumeProxy`, `CalibrateSeaLevelByVolume` (tiszta, statikus
  függvények, ugyanaz a stílus, mint a meglévő `CalibrateSeaLevel`).
- `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`: `V0`
  gyorsítótárazása (`EnsureInitialWaterVolumeCache`) — csak seed/
  plateCount/level/targetWaterFraction változásakor számol újra, a
  `deepTimeMyr` csúszka mozgatásakor NEM; `Build()`-ben `deepTimeMyr==0`
  esetén a régi percentilis-hívás, egyébként a térfogat-alapú visszaoldás.
- Nincs downstream Python tesztvektor-változás (a `CrustElevation`/
  `PlateBoundaryEffect`/stb. numerikus viselkedése nem módosult, csak a
  tengerszint-kalibráció egy ÚJ, opcionális módja került hozzá).

**`TEST-EARTH-001` állapota:** VÁLTOZATLANul teljesül `t=0`-nál mindkét
módszerrel (65.00% víz, 44 kontinens ≥2 — a percentilis-módszer bitre
változatlan; a térfogat-alapú visszaoldással is teljesül, ld.
`SeaLevelCalibrationVolumeBasedTests.
TestEarth001HoldsWithVolumeBasedCalibrationAtTimeZero`).

261/261 teszt zöld (12 új: `SeaLevelCalibrationVolumeBasedTests`), Debug és
Release konfigurációban egyaránt.

## Nyitott döntések

### ND-39 — Level 8 rács-betöltés gyorsítása: CPU Job System + Burst elsőként, GPU compute shader csak külön bitpontossági validációval

**Kérdés (felhasználói visszajelzés):** a level 8 rács generálása/betöltése a Unity-nézőben (`PlanetGridMesh.Build()`) nagyon lassú. Gyorsítható-e, és bevonható-e a GPU a számításba az I1 (bitpontos determinizmus minden platformon) megsértése nélkül?

**Mennyiségi becslés (forrásból levezetve, nem találgatva).** Level 8: `n = 1<<8 = 256`, `tiles = 6·256² = 393 216` tile. A domborzat-lánc egyetlen pontkiértékelése (a `DeterministicRandom.SampleUnitVector3` ~1,9 átlagos iterációjával, ND-23a — mindegyik iteráció 1 Threefry-4x64-20 blokk):

- `FractalNoise.GradientNoise3D` = 8 sarok × `SampleUnitVector3` ≈ 8·1,9 = **~15,2 Threefry**
- egy `Fbm`/`RidgedMultifractal` oktávonként 1 `GradientNoise3D`
- `DomainWarp.WarpPosition` = 3 független `Fbm` (3 oktáv) ≈ 3·(3·15,2) = **~137 Threefry**

Tile-középre (`ComputeElevationFieldWithSeeds`): `WarpPosition` (AssignPlate előtt, ~137) + `BaseElevation` [`IsOceanic` 1 + `RidgedMultifractal` 5 oktáv ~76 + `MountainMask` 3 oktáv ~46 = ~123] + `BoundaryUplift` [még egy `WarpPosition` ~137] ≈ **~396 Threefry/tile-közép**. A Unity `Build()` ezen felül tile-onként **4 sarkot** is kiszámol (`ToDisplacedVector3 → ComputeDisplacedRadius`), egyenként ~396 Threefry, cache nélkül: **~1585 Threefry/tile**. Összesen **~1980 Threefry/tile**.

Level 8-ra: **393 216 · ~1980 ≈ ~780 millió Threefry-4x64-20 kiértékelés** egyetlen betöltésre. Egy Threefry-4x64-20 = 20 kör ARX (add/rotate/xor) 4×64-bites szavakon (~120 64-bites ALU-művelet), tehát nagyságrendileg **~90 milliárd 64-bites egészművelet**, ráadásul a `SampleUnitVector3` elutasításos ága elágazásokkal. Ez futásidőben mind **egyetlen szálon, a Unity fő szálán** történik.

**Jelenlegi végrehajtási modell:** tisztán szekvenciális. `Build()` egy `for face / for u / for v` háromszoros ciklus, a `ComputeElevationFieldWithSeeds` ugyanígy — **nincs `Parallel.For`, nincs Job System, nincs Burst**. A teljes lánc a fő szálat blokkolja betöltés alatt.

**Opciók:**

| Opció | Előny | Hátrány / kockázat |
|---|---|---|
| **A: CPU Job System + Burst (`IJobParallelFor` tile-onként)** | A lánc tiszta függvény (I2) → triviálisan tile-párhuzamos. Marad CPU IEEE-754 szemantikában, `FloatMode.Strict`-tel (ND-20) bitpontos. Reális ~10-20× gyorsulás (magszám × SIMD `double4`). | A `DeterministicRandom`/`FractalNoise`/`CrustElevation` lánc Burst-kompatibilissá tétele: a `Dictionary<TileId,double>`, a `(double,double,double)[]` managed tömbök és az `out`-tuple minta helyett `NativeArray`/blittable value-típusok kellenek. A forró úton nincs kivétel és nincs transzcendens (csak `+ - * / sqrt abs floor`), tehát Strict-kompatibilis. |
| **B: GPU compute shader (HLSL)** | A Threefry counter-based, állapotmentes, tiszta (kulcs,számláló)→kimenet — kifejezetten masszív GPU-párhuzamosításra tervezve. Nagyságrendekkel nagyobb áteresztés. | **Komoly I1-kockázat.** A GPU lebegőpontos aritmetika gyártók/driverek/shader-fordító közt NEM garantáltan bitpontosan azonos a CPU IEEE-754-gyel (ugyanaz az osztály, mint a Burst `FloatMode.Default` — ND-20 — és a transzcendens-kockázat, ND-26/27). A mező táplálja az óceán/biome/tengerszint/`WorldStateHash`-t → checkpointolt, kritikus út. |
| **C: Algoritmikus — sarok-deduplikáció + warp-hoisting** | Determinizmus-semleges, azonnali nyereség. | Nem old meg mindent önmagában. |

**Determinizmus-semleges algoritmikus nyereségek (C, bármelyik úttal kombinálható):** (1) **Sarok-cache** — jelenleg minden sarok tile-onként újraszámolódik, holott egy sarkot legfeljebb 4 tile oszt: a megosztott sarkok deduplikálása a ~1585 Threefry/tile sarok-költség kb. 75%-át megszünteti (azonos bemenet → bitre azonos kimenet, nem seed-törő). (2) **Warp-hoisting** — a `WarpPosition` tile-onként kétszer fut (AssignPlate-hez és `BoundaryUplift`-ben); egyszeri kiszámítás bitre azonos értéket ad (nem seed-törő), a közép-költség ~30%-át megspórolva.

**JAVASLAT.** Elsőként az **A opció (CPU Job System + Burst, `FloatMode.Strict`)**, előtte/mellette a **C** olcsó, determinizmus-semleges nyereségekkel (sarok-cache, warp-hoisting) — ezek együtt reálisan több tízszeres gyorsulást adnak I1-kockázat nélkül, a projekt már meglévő ND-20 kötelezettségére építve. **B (GPU) csak KÜLÖN, óvatos munka**, és **kizárólag megjelenítési célra** (a nem-checkpointolt sarok-eltolás/mesh vizualizáció, analóg az ND-26 „csak Directional Light" és az ND-27 kivétel-kezelésével), VAGY ha bizonyítottan bitpontos minden támogatott GPU-n — a checkpointolt/hash-elt mezőt GPU nem adhatja, amíg ez nincs igazolva. A hierarchikus/LOD-alapú lusta betöltés (csak a kamerához közeli tile-ok finom szinten) **kapcsolódó, de külön munka** (egy korábbi LOD-ötletelésben már felmerült), nem ennek az ND-nek a hatóköre.

**Verziózás:** A és C bitre azonos mezőt ad (tiszta függvény, változatlan számítási lánc) → **nem seed-törő**, verzióemelés nem szükséges. B akkor és csak akkor vezethető be checkpointolt útra, ha bitpontos — különben I1-sértés, nem verzió-kérdés.

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs` (`Build()`, `ComputeDisplacedRadius`), `src/WorldGen.Core/Tectonics/SeaLevelCalibration.cs`, `src/WorldGen.Core/Terrain/FractalNoise.cs`, `src/WorldGen.Core/Terrain/DomainWarp.cs`, `src/WorldGen.Core/Tectonics/CrustElevation.cs`, `src/WorldGen.Core/Tectonics/PlateBoundaryEffect.cs`, `src/WorldGen.Core/Random/DeterministicRandom.cs`, `src/WorldGen.Core/Random/Threefry4x64.cs`.

### ND-40 — A viewer mesh-architektúrája: adaptív kvadfa-LOD a fix egészgömbös build helyett — és viszonya az ND-39 "A" (Burst) opcióhoz

**Kérdés (felhasználói visszajelzés + M9 tervezés).** Zoomoláskor a
`PlanetGridMesh` domborzata pixeles/durva marad, mert egyetlen fix
LOD-szinten, egyszerre a TELJES gömbre épül, és nem sűrűsödik a kamera
közelében. Két, egymással összefüggő döntés kell: (1) a viewer megtartsa-e
a mostani egészgömbös `Build()`-et (csak magasabb fix szintre emelve),
vagy váltson perzisztens, kamera-vezérelt adaptív kvadfára inkrementális
frissítéssel; és (2) hogyan viszonyul ez az ND-39 "A" (CPU Job System +
Burst) tervéhez — az adaptív LOD feleslegessé/halaszthatóvá teszi-e a
Burst-öt a viewer interaktivitásához?

**Kontextus (forrásból).** A domborzat-mező LOD-független tiszta függvény
`(seed, x, y, z)`-ből (`CrustElevation`/`PlateBoundaryEffect`/`DomainWarp`),
és a viewer `ComputeDisplacedRadius` már pont-alapon értékeli ki — tehát az
adaptív LOD **nem igényel új Core-numerikát vagy Python-referenciát**. Az
ND-39 az egészgömbös level 8 buildet ~780M Threefry-kiértékelésre mérte,
egyetlen főszálon. Az ND-02 rögzíti: a szimuláció bázis-LOD fix level 6, a
LOD csak lekérdezésre/renderre. A §70.5 megköveteli, hogy a fő partvonal ne
változzon LOD-váltáskor.

**Opciók:**

| Opció | Előny | Hátrány / kockázat |
|---|---|---|
| **A: Marad a fix egészgömbös `Build()`, magasabb szintre emelve, ND-39-A (Burst) gyorsítással** | Legkevesebb új viewer-kód; a meglévő szerkezet marad. | Az idő/memória 6·n²-nel skálázik a kamerától FÜGGETLENÜL. Level 11 (25M tile) egyszerre felépítése Burst-tel is irreális. A felhasználói panaszt (kamera-közeli finomodás) nem oldja meg — a részletesség globális, nem a nézetre koncentrált. |
| **B: Adaptív, perzisztens kvadfa, inkrementális frissítés + geomorphing (az M9-terv)** | Az egyidejűleg kiértékelt pontszám a látótértől függ, nem a max-mélységtől → nagyságrendekkel kevesebb. Level 11 zoom így válik egyáltalán lehetségessé. Közvetlenül megoldja a panaszt (kamera-közeli finomodás, folyamatos átmenet). | Több új viewer-kód (kvadfa, 2:1 balance, stitching, geomorphing, LRU sarok-cache). Tisztán megjelenítési, de nem triviális. |
| **C: Hibrid — adaptív kvadfa a geometriára, MEGTARTOTT fix level-6 referencia-passz a tengerszinthez / panel-cache-hez / biome-hoz** | B minden előnye, PLUSZ a §70.5 (partvonal-invariancia) és az ND-02 (fix bázis-LOD) strukturálisan garantált: a tengerszint és a panel-adat a fix referencia-szintről jön, nem a változó-LOD ponthalmazból, tehát a partvonal nem remeg zoomkor. | Két adat-út (referencia-szintű + adaptív) párhuzamos kezelése — de ezek tisztán szétválnak (panel vs. render). |

**JAVASLAT: C opció (adaptív kvadfa-render egy megtartott, fix level-6
referencia-passz felett).** Ez oldja meg a felhasználói panaszt anélkül,
hogy a partvonal/tengerszint zoomkor elmozdulna (§70.5, ND-02). A
`_lastField`/`_lastIsOcean`/`_lastBiomeOf`/`_lastSeaLevel` referencia-cache
változatlanul level 6-on marad (a panelek makrostruktúrát írnak le), az
adaptív magas-LOD geometria külön, tranziens, LRU-korlátos csomópont-
cache-ben él.

**A Burst-kérdés (ND-39-A viszonya) — állásfoglalás:** az adaptív LOD az
egyidejű ponthalmazt a kamera látóterére korlátozza (nagyságrendileg
konstans, a max-mélységtől független), így a per-frame inkrementális
frissítés főszálas, amortizált (frame-költségvetéses) végrehajtással is
elég gyors az interaktív zoomhoz. Ezért **az ND-39-A (Burst/Job) NEM
előfeltétele a viewer interaktivitásának** — halasztható, és csak akkor
aktiválandó, ha a profilozás (M9 9.5) főszál-akadást mutat nagy régió
belépésekor, VAGY ha egy külön, egészgömbös level-8 "dump" (nem
interaktív) igényként előjön. A két munka tehát szétcsatolva: az M9
adaptív LOD önmagában, Burst nélkül szállítja a folyamatos zoomot; az
ND-39-A opcionális gyorsítás marad a nem-adaptív utakra.

**Determinizmus / lebegőpont.** Ez KIZÁRÓLAG megjelenítési munka, nem
érint egyetlen Core-numerikát sem, nem változtat semmilyen mezőt, óceán/
biome-osztályozást, tengerszintet vagy `WorldStateHash`-t. A display-
oldali aritmetika (kamera-távolság-arány, morph-lerp, LOD-küszöbök) `float`
és NEM tartozik az I1 platformok-közötti bitpontosság hatálya alá (ugyanaz
a besorolás, mint a viewer meglévő `WaterDepthBucket` `Math.Exp`-je) —
követelmény csak a **session-en belüli reprodukálhatóság** (ugyanaz a
kamera-pozíció → ugyanaz a mesh; oda-vissza zoom → nincs drift/remegés),
amit a tiszta-függvény kiválasztás + hiszterézis biztosít. Új transzcendens
függvény a szimulációs kritikus úton NINCS.

**Verziózás: NEM seed-törő.** Az adaptív LOD bitre azonos mezőt jelenít
meg (a Core-lánc változatlan, csak MÁS pontokon és MÁS sűrűséggel
mintavételezve) — nem módosít semmilyen seed-hez kötött numerikus
viselkedést. Verzióemelés nem szükséges.

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/
PlanetGridMesh.cs` (`Build()`, `ComputeDisplacedRadius`,
`GetOrComputeCorner`, `ToDisplacedVector3`, cornerCache,
`_lastField`/`_lastSeaLevel`), `src/WorldGen.Core/Grid/TileId.cs`
(`Parent()`/`Child()`), `src/WorldGen.Core/Grid/TileNeighbors` (2:1
balance), `src/WorldGen.Core/Tectonics/SeaLevelCalibration.cs`
(referencia-szintű tengerszint). Kapcsolódó döntések: ND-02 (fix bázis-
LOD), ND-39 (Burst/Job és a sarok-cache/warp-hoisting), spec §51-52 és
§70.5.

### ND-20 — Burst `FloatMode.Strict` kikényszerítése (A13, LEZÁRVA: CI-szkript, előre megírva)

Az ND-01 miatt aktív. A Burst alapból `FloatMode.Default`-ban fordít, ami
engedélyezi a lebegőpontos műveletek átrendezését — ez csendben megsérti
I1-et. Egyetlen hiányzó `[BurstCompile(FloatMode = FloatMode.Strict)]` csak
platformok közötti hash-eltérésnél derül ki, ami nagyon drága hibakeresés.

**Döntés (2026-09-27): CI-szkript, nem Roslyn analyzer.** A kapu
`tools/ci/check_burst_strict.py`, a CI-ben önálló `burst-strict` job. Miért
szkript és nem analyzer: az analyzert csak a `dotnet build` futtatja, a
Unity-oldali `Assets/` fordítását pedig a CI-gépeken nincs mivel elvégezni
(nincs Unity — ld. a `tests/WorldGen.*.Compile` kapuk indoklását). A
szöveges scanner ugyanazzal az egy futással látja a `src/`, `tests/`,
`tools/` és `unity/WorldGenViewer/Assets/` alatti **összes** `.cs`-t,
függetlenül attól, hogy melyik assembly-be fordulna — és Unity nélkül is fut.

**A kikényszerített szabály.** Minden `[BurstCompile]` attribútum-használat
explicit `FloatMode = FloatMode.Strict`-et kap. Ezen felül a kapu elutasítja a
`FloatPrecision.Low` / `.Medium` értékeket is: ezek nem az átrendezést, hanem
a matematikai függvények approximációját engedélyezik, tehát ugyanabba az
I1-osztályba tartoznak. A `Standard` és a `High` átmegy. A `FloatMode`
többi értéke (`Default`, `Fast`, `Deterministic`) mind sértés — a
`Deterministic` is, mert a Burst-ben ma nem garantált szemantika.

**A kapu ELŐRE készült el, nulla találaton.** A repóban ma egyetlen
`[BurstCompile]` sincs (301 átvizsgált `.cs`); az ND-39 „A" opciója még nem
indult el. A kapu szándékosan a használat **előtt** került be, hogy az első
Burst-commit-tal EGYÜTT váltson pirosra, ne utólag, foltozásként. Egy mindig
zöld kapu viszont értéktelen, ezért a szkript `--self-test` módja 28
fixture-on (13 átengedendő, 14 elutasítandó, 1 sorszám-ellenőrzés) bizonyítja,
hogy valóban fog — a CI mindkettőt futtatja. A self-test a fejlesztés során
egy valódi hibát fogott a kapuban: az összevont attribútum-listában
(`[StructLayout(...), BurstCompile]`) a visszafelé-olvasás megállt az előző
attribútum záró zárójelén, és a sértés átment.

**Ismert, tudatos határ:** a scanner szöveges, nem szemantikus. Ha valaki
saját, `BurstCompile` nevű attribútumot ír, vagy `using`-aliasszal átnevezi a
Burst-ét, a kapu megtévedhet. Ez a csere elfogadható: a hamis pozitív
zajos-de-biztonságos, és a valós használati minták (kvalifikált nevtér,
`Attribute` utótag, assembly-szintű cél, több soros paraméterlista, komment-
és sztring-beli említés) fixture-ral fedettek.

### ND-21 — HDRP volumetrikus felhő űrből (A14, ELUTASÍTVA, prototípussal mérve)

**A kérdés (eredeti felvetés, ND-01 nyitotta).** A HDRP volumetrikus
felhőrendszere földfelszíni nézetre készült; bizonytalan volt, hogy az űrből
nézett teljes bolygó felhőzete milyen minőségű. A javaslat prototípus-
ellenőrzés volt, fallback-ként saját felhő-shader.

**A prototípus lefutott (2026-09-27, A14).** Élő Unity 6000.0.77f1 + HDRP
17.0.1, `PlanetView` jelenet, Play módban: a `HDRP High Fidelity` assetben
`supportVolumetricClouds` IDEIGLENESEN bekapcsolva, egy eldobható,
`HideFlags.DontSave` Volume-mal (`VisualEnvironment` + `VolumetricClouds`,
később `PhysicallyBasedSky`), `planetRadius = 100`, `planetCenter = (0,0,0)`,
`renderingSpace = World`. A menet után minden visszaállt (`git status` tiszta,
`supportVolumetricClouds: 0` mind a négy assetben). Képek:
`artifacts/a14/` (gitignore alatt), napló:
`history/2026-09-27-a14-nd21-hdrp-volumetric-clouds.md`.

**Döntés: ELUTASÍTVA — a Planet nézet felhői a saját úton maradnak** (ma a
mesh-alapú MVP a csapadék-mezőből, `WorldGen/CloudUnlit`; a volumetrikus
folytatás az A16/M13 saját shaderé). A HDRP volumetrikus felhő NEM opció
bolygó-léptékben, űrből. Négy független, mért ok — bármelyik önmagában is
elég lenne:

1. **Geometria: a rétegvastagság alsó korlátja a bolygónk sugara.** Az
   `altitudeRange` egy `MinFloatParameter(2000, 100)`, és a `value` setter
   keményen vág (`Mathf.Max(value, min)`). MÉRVE: `kért = 0,05 →
   tényleges = 100`. A mi léptékünkben (`radius = 100` egység = 7420 km, azaz
   1 egység = 74,2 km) a LEGVÉKONYABB lehetséges felhőhéj `r = 100,02 …
   200,02`, vagyis **7420 km vastag** — a felhő nem a felszínt takarja, hanem
   egy második, bolygónyi burkot képez körülötte. A valós-lépték nem kiút: az
   az ND-19/A12 (float32) miatt zárva van.
2. **A sűrűség-normalizálás a Föld sugarát drótozza be.** A
   `HDRenderPipeline.VolumetricClouds.cs` `ComputeNormalizationFactor`-ja
   `const float k_EarthRadius = 6378100.0f` mellett számol, és ez a
   `_NormalizationFactor` osztja a felhőtérkép UV-jét
   (`VolumetricCloudsUtilities.hlsl`, `GetCloudCoverageData`). A mi 200
   egységnyi bolygónk így a térkép EGYETLEN texeljére képződik. MÉRVE: a
   `Advanced` (felhőtérképes) módban **semmi nem renderelődött** — sem a héjon
   kívülről (`03`, `06`), sem belülről, sem az északi, sem a déli féltekéről
   (`08`, `09`).
3. **A felhőtérképes út — az EGYETLEN, amit a világmodell hajthatna — a
   shaderben kizárja a fél bolygót.** `VolumetricCloudsUtilities.hlsl`,
   `EvaluateCloudProperties`: *„When using a cloud map, we cannot support the
   full planet due to UV issues"*, majd `if (positionPS.y < 0.0f) return;`.
   A térkép sík, XZ-vetületű, nem gömbi — a déli félteke elvileg sem kaphat
   felhőt a saját adatunkból.
4. **Ami renderel, az sérti az I3-at.** Egyedül a `Simple` preset ad képet, de
   ott a lefedettség a shaderben `float4(0.9f, 0.0f, 0.25f, 1.0f)` konstans +
   procedurális zaj — nulla köze a világmodellhez. MÉRVE (`07`): a héjon
   belülről fekete pettyek szórva az egész felszínen, mert a `shapeScale` /
   `erosionScale` méter-alapú, Föld-léptékű felhőkre hangolt zajfrekvenciái a
   mi skálánkon ~1 egység (≈74 km) méretű szemcsét adnak.

**Járulékos megfigyelés.** A felhő bekapcsolása behúzza a HDRP saját
bolygó-/ég-modelljét (`VisualEnvironment` planet radius/center,
`PhysicallyBasedSky`), ami a mi M3-as megvilágításunkkal ütközik: MÉRVE (`04`)
a terminátor és a nappali oldal fényessége észrevehetően átrendeződött, pedig
csak az ég került be. Ez az I4/M3 szempontjából külön kockázat lenne.

**GPU-költség: nem mérhető a rendelkezésre álló műszerrel.** A
`get_performance_stats` képkocka-időzítése ebben a jelenetben 3,4–6,1 ms között
szórt (a terep-LOD háttérmunkája dominál), egy érvénytelen 0,03 ms-os mintával.
Egyetlen tiszta bolygónézeti pár adódott (2,71 → 3,16 ms), ez **tájékoztató,
nem bizonyíték**. A döntés nem is ezen múlik: az 1–4. pont geometriai és
invariáns-okokból zár.

**Következmény.** A `docs/03-unity-hdrp-evaluation.md` fallback-ága lép életbe:
a Planet nézet felhői saját shaderrel készülnek. Az ND-01 fidelity-becslésének
az a tétele, hogy „a HDRP kész felhő-rendszere renderelési munkát spórol",
a felhőre nézve NEM teljesül — az atmoszférára (`PhysicallyBasedSky`) és a
vízre külön kell majd megvizsgálni, ez az ND nem dönt róluk. Az A16/M13
volumetrikus felhő-tétele innentől nem „kapcsoljuk be a HDRP-t", hanem saját,
gömbi raymarch a meglévő csapadék-/nedvesség-mezőből.

### ND-23b — Transzcendens függvények a kritikus úton

`Math.Log`, `Math.Exp`, `Math.Sin`, `Math.Cos`, `Math.Pow` nem garantáltan
bitre azonos platformok között. A `SampleGaussianUnsafe` emiatt `Unsafe` jelölésű.

| Opció | Előny | Hátrány |
|---|---|---|
| Saját polinomiális implementáció, verziózva | Teljes kontroll, bitpontos | Meg kell írni és validálni |
| Előre számolt inverz-CDF tábla + lineáris interpoláció | Csak összeadás/szorzás | Korlátozott pontosság |
| Elfogadni a kockázatot | Nincs munka | I1 sérül |

**Sürgősség:** M4 (tektonika) előtt kell dönteni. A Gauss-eloszlás a lemez-
sebességeknél és az esemény-magnitúdóknál jön elő.

### ND-41 — Szél, párolgás/csapadék, időjárás-zaj: egyszerűsített, dokumentált zárt modell zárt formula nélküli spec-szakaszokhoz

**Kérdés:** a spec §30 ("Szél"), §31 ("Nedvesség és csapadék") és §32
("Időjárás") csak minőségi komponenslistát ad, zárt formula nélkül —
pl. `Evaporation = f(temperature, wind, surfaceWater)`, "moist air →
mountain → uplift → precipitation", `W(p,t) = Noise(Warp(p,t), seed)`.
A `tools/reference/wind_precipitation_ref.py` Python-orákulumhoz
konkrét, determinisztikus képleteket kellett választani öt olyan
ponton, ahol a spec nem specifikál egyértelműen.

**Döntés (öt alpont):**

1. **Háromsávos zonális index alakja.** Háromszög-hullám (0 → csúcs → 0)
   minden 30 fokos sávban, előjellel váltakozva (Hadley: negatív/keleti,
   Ferrel: pozitív/nyugati, Polar: negatív/keleti), nullátmenettel
   pontosan 0/30/60/90 foknál. **Indoklás:** ez kvalitatívan megfelel a
   valódi "csendöveknek" (doldrums ~0°, ló-szélességek ~30°, szubpoláris
   mélynyomás ~60°) és a köztük váltakozó szélöveknek — nem mérési érték,
   csak kvalitatív proxy, de nem önkényes: a nullátmenetek a valódi
   sávhatárokra esnek.

2. **Coriolis-proxy formája.** A hőmérsékleti nyomásgradiensből számolt
   termikus szélkomponenst egy FIX szögű forgatással térítjük el
   (`CORIOLIS_DEFLECTION_DEG_DEFAULT = 30°`), előjele `-sign(szélesség)`
   — jobbra az É-, balra a D-féltekén, a geosztrofikus szél klasszikus
   kvalitatív viselkedésének megfelelően. **Indoklás:** a szélességi
   alapcellák már saját sávos előjelváltással rendelkeznek (1. pont), a
   Coriolis-hatást csak a termikus komponensre alkalmazzuk, hogy a két
   hatás külön tesztelhető és dokumentálható legyen. A konkrét szögérték
   (30°) illusztratív, nem mérés — később finomítható.

3. **Hőmérséklet-gradiens számítási módja.** Véges differencia a már
   verifikált `temperature_kelvin`-ből, a lokális kelet/észak érintő-
   irányban, `GRADIENT_EPS = 1e-3` radián lépéssel, a szomszéd pontokon
   is a HÍVÓ tile aktuális `is_oceanic`/`elevation_m` értékét feltételezve
   (a valódi szomszéd-tile adatok nem elérhetők ebben az orákulumban,
   mert az nem fér hozzá a tile-rácshoz/szomszédsági táblához).
   **Indoklás:** ez dokumentált egyszerűsítés, nem hiba — a tényleges
   motor-integrációkor a hívó könnyen cserélheti valódi szomszéd-
   lekérdezésre.

4. **Elevációgradiens mint közvetlen bemenet.** A hegyi eltérítéshez és
   az orografikus csapadékhoz szükséges elevációgradienst (kelet/észak
   komponens) a modul KÖZVETLEN BEMENETKÉNT várja, nem számolja újra a
   kéregmodellből. **Indoklás:** ugyanaz a minta, mint ahogy
   `temperature_ref.py` is közvetlen bemenetként várja az
   `elevation_m`/`sea_level_m` értéket a kéregmodell újraszámolása
   helyett — a döntés a moduláris, réteg-független orákulum-tesztelést
   szolgálja, a motor-integrációkor triviálisan csatolható a valódi
   szomszéd-elevációhoz.

5. **Weather-deviáció korlátozásának módja (§32.3).** `tanh()`, nem kemény
   `clamp()`. **Indoklás:** a `noise_ref.fbm` saját plauzibilitás-tesztje
   szerint az fBm-érték ritkán enyhén túllépheti a [-1,1] tartományt
   (ld. `noise_ref.py` __main__, "[-1.5,1.5] körüli tartomány"). Kemény
   `clamp()` esetén ez egy látható, éles "plafont" adna a deviáció-
   mezőben (mesterségesen lapos foltok ott, ahol a zaj épp túllépte a
   határt) — a `tanh()` ehelyett simán, aszimptotikusan telítődik, nincs
   élesség-artefaktum, és `|tanh(x)| < 1` bármely véges x-re, tehát a
   deviáció szigorúan korlátos marad a klimatikus átlag körül, ahogy a
   §32.3 kifejezetten megköveteli ("a weather noise nem írhatja felül a
   klímát").

**Verziózás:** ez egy ÚJ modul (nincs korábbi C#-implementáció, amit
felülírna), ezért nem seed-törő a meglévő világokra nézve — de a modul
saját belső konstansai (a fenti 5 pont + a `BASE_WIND_SPEED_DEFAULT`,
`OROGRAPHIC_COEFF_DEFAULT` stb. numerikus alapértékek) a jövőben
finomíthatók, amíg a C#-port el nem készül és be nem kerül a
`testvectors.json`-ba — utána bármely módosításuk verzióemelést igényel.

**Verifikálva:** `tools/reference/wind_precipitation_ref.py` önállóan
lefut, 7 plauzibilitás-blokk zöld (zonális index, Coriolis-előjel,
hegyi eltérítés, determinizmus, párolgás-monotonitás, orografikus
csapadék/rain-shadow, weather-korlátosság), és két egymást követő
futtatás bitre azonos `wind_precipitation_vectors.json`-t generál
(SHA-256 egyezés).

**Releváns fájlok:** `tools/reference/wind_precipitation_ref.py` (import:
`temperature_ref.temperature_kelvin`, `noise_ref.fbm`,
`domain_warp_ref.warp_position`).

### ND-42 — M5 teljes hőmérséklet-modell: T_greenhouse/T_ocean/T_weather/T_cycle modellezési választásai

**Kérdés.** A backlog "M5 | Teljes hőmérséklet-modell" tétele a spec §28.1
teljes egyenletét kéri:

```
T = T_radiative + T_greenhouse + T_ocean - T_altitude + T_weather + T_cycle
```

A meglévő `tools/reference/temperature_ref.py` addig csak
`T_radiative + T_greenhouse(fix 33K) - T_altitude`-ot valósította meg. A spec
sem §28-nál, sem §29-nél (albedo-feedback) nem ad zárt formulát a hiányzó
három tagra — csak minőségi leírást (`cooling → more ice → higher albedo →
more cooling`, ill. §32 "advected procedural field", §25 "Milanković-szerű
komponensek, ne földi periódusokkal"). Ez a döntés rögzíti, milyen konkrét,
zárt modellt választottunk mind a négy tagra, mert a spec ezt nyitva hagyta.

**Réteg-döntés (nem tárgyalható, csak dokumentált): a régi `temperature_kelvin`
függvény VÁLTOZATLAN maradt.** A már generált 150 elemű
`temperature_vectors.json` és a rá épülő C# `Temperature.cs`/
`TemperatureTests.cs` bitre azonos maradt (ellenőrizve: a fájl `git diff`-je
üres). A teljes egyenlet egy ÚJ függvényben (`temperature_kelvin_full`)
készült el, saját, KÜLÖN tesztvektor-fájllal (`temperature_full_vectors.json`)
— ez még NINCS C#-portolva, az egy külön, későbbi lépés (lásd "Hátralévő" lent).

**1. T_greenhouse — logaritmikus, CO2-szerű koncentráció-proxy.**

| Opció | Előny | Hátrány |
|---|---|---|
| A: marad fix 33K konstans | Nincs új kockázat | Nem "modell", nem függ semmilyen bolygóparamétertől — a backlog tétel ezt kifejezetten kéri bővíteni |
| **B: `T_greenhouse = 33K + sensitivity · log2(ghg_ppm / 280ppm)`** | Fizikailag ismert kvalitatív minta (radiative forcing ~ln(concentráció)); a referencia-koncentráción PONTOSAN visszaadja a régi 33K-t (ln(1)=0 egzaktul) | A `sensitivity_k_per_doubling=3.0` és a `280 ppm` referencia valós Föld-adatok (preindusztriális CO2, IPCC "equilibrium climate sensitivity" középbecslése ~1.5–4.5K sávból), de itt egy szintetikus bolygó PROXY-jaként, nem mérésként használjuk |
| C: lineáris arányosság a koncentrációval | Nincs új transzcendens kockázat | Fizikailag kevésbé indokolt (a valós üvegházhatás jól ismerten logaritmikus, nem lineáris) |

**JAVASLAT: B.** A `dm.ln`/`dm.exp` (a már lezárt ND-27 `deterministic_math_ref`
modulja) használatával nem nyit új, nem-dokumentált transzcendens-kockázatot.
**MEGERŐSÍTÉST IGÉNYEL:** a `280 ppm` és a `3.0 K/duplázódás` valós fizikai
becslések, nem KAT-szerűen verifikálható algoritmus-konstansok — ha a
felhasználó más értéket akar, ez paraméterezhető (`ghg_ppm`,
`sensitivity_k_per_doubling`), a névleges alapértelmezettek csak egy
kiindulási javaslat.

**2. T_ocean — kontinentalitás/hőtehetetlenség, évi átlaghoz húzás.**

Az óceáni tile pillanatnyi `T_radiative`-ját az évi (12 havi mintás) átlaga
felé húzzuk egy `buffering_strength` (alapértelmezett 0.3) együtthatóval;
szárazföldön pontosan 0 (nincs változás a régi viselkedéshez képest).
Ellenőrizve (`__main__` plauzibilitás-teszt): 45°-on az óceán évszakos
szórásnégyzete (~500) érdemben kisebb, mint a szárazföldé (~880) ugyanazon a
szélességen. **MEGERŐSÍTÉST IGÉNYEL:** a `0.3` együttható és a 12 mintás évi
átlagolás felbontása tisztán modellezési választás, nincs spec-forrás vagy
mért Föld-adat mögötte.

**3. T_weather — §32.2 stateless időnoise, ÖNÁLLÓ MINIMÁLIS PLACEHOLDER.**

A feladat idején a testvér-feladat (szél/nedvesség/csapadék,
`wind_precipitation_ref.py`) párhuzamosan, még nem lezártan fut. Emiatt a
`weather_deviation_k` a spec §32.2 mintáját (`W(p,t) = Noise(Warp(p,t),
seed)`) a már verifikált `noise_ref.fbm`-mel valósítja meg, ahol a "Warp"
egy egyszerű, determinisztikus idő-eltolás (`WEATHER_TIME_DRIFT · day_t ·
WEATHER_TIME_SCALE_PER_DAY`) — NEM a teljes szél/nyomás/nedvesség-alapú
időjárás-mező. **HATÁRVONAL, NEM TÖRLENDŐ CSENDBEN:** amint a
`wind_precipitation_ref.py` elkészül, ezt a függvényt át kell nézni/
egyesíteni azzal (ne maradjon két független időjárás-forrás a rendszerben).
Az amplitúdó (`WEATHER_AMPLITUDE_K_DEFAULT = 4.0K`, a spec §32.3 saját
példájából: "-4°C" tipikus deviáció) és a zaj-frekvencia/oktávszám
modellezési választás. Ellenőrizve: az átlagos/max abszolút deviáció jóval a
tipikus egyenlítő-pólus klímakülönbség alatt marad (§32.3 "a weather noise
nem írhatja felül a klímát" — additív, nem domináns).

**4. T_cycle — Milanković-szerű additív kényszerítő oszcilláció, SZŰKÍTETT
HATÓKÖRREL.**

Három szinuszos komponens (eccentricity/obliquity/precession-szerű, spec
§25), seedelt periódusokkal "fizikailag ésszerű tartományból"
(50k–500k / 20k–150k / 10k–50k év — NEM a valódi Föld 100k/41k/23k éves
Milanković-periódusok, a spec kifejezetten tiltja a földi periódusok
másolását). **HATÓKÖR-HATÁR A TESTVÉR-FELADATTAL (M10 erózió+eljegesedés-
ciklusok) SZEMBEN, EXPLICITEN RÖGZÍTVE:** ez a modul KIZÁRÓLAG a
hőmérséklet-egyenlet additív, időfüggő bemeneti tagját számolja
(`climate_cycle_temperature_k`). A jégtakaró/eljegesedés TÉNYLEGES
következménye (jégmennyiség, albedo-visszacsatolás, tengerszint-hatás — §29
feedback-hurok) NEM ennek a modulnak a hatásköre, azt az M10 (erózió+
eljegesedés-ciklusok) feladat implementálja majd, ennek a `T_cycle` kimenetét
mint bemenetet felhasználva. **MEGERŐSÍTÉST IGÉNYEL:** a három periódus-
tartomány és a három amplitúdó (2.0/3.0/1.0 K) tisztán modellezési választás,
nincs mögötte spec-adat vagy hivatalos forrás.

**Determinizmus.** Minden új tag tiszta függvény (nincs mutable állapot);
`dm.sin_cos`/`dm.ln`/`dm.exp` (a lezárt ND-27 polinomiális implementációja)
használatával, NEM nyers `math.sin/log/exp`-pel — így az új ágak nem nyitnak
új, dokumentálatlan transzcendens-kockázatot. Megjegyzés: a MEGLÉVŐ
T_radiative/T_altitude lánc (`astronomy_ref.sun_direction_body_frame`, ill. a
`raw ** 0.25` negyedik gyök Python beépített `**`-tal) továbbra is nyers
`math.sin/cos`-t és `**`-ot használ — ez az ND-27 lezárása ELŐTTI állapotot
tükrözi, és jelen feladat kifejezett kérésére (a meglévő bázisréteg
érintetlenül hagyása) nem lett javítva; külön nyomon követendő, ha az ND-27
hatóköre újra napirendre kerül.

**Verziózás: NEM seed-törő (egyelőre).** A `temperature_kelvin` (a jelenleg
C#-ban is élő, seed-hez kötött függvény) bitre változatlan. A
`temperature_kelvin_full` egy ÚJ függvény, aminek még nincs C#
megfelelője — amikor portolásra kerül, AKKOR válik ez a döntés seed-törővé
(a `Temperature.cs` numerikus viselkedésének módosítása), és akkor kell a
megfelelő verziószámot emelni, nem most.

**Hátralévő (nem ennek a feladatnak a hatóköre):** (1) a fenti négy
"MEGERŐSÍTÉST IGÉNYEL" paraméter felhasználói jóváhagyása vagy módosítása;
(2) `temperature_kelvin_full` C# portolása + `temperature_full_vectors.json`
KAT-ellenőrzése (`WorldGen.Core.Tests`); (3) `weather_deviation_k` egyesítése
a testvér-feladat `wind_precipitation_ref.py`-jával, amint az elkészül; (4) a
`docs/05-milestones.md`/`docs/backlog.md` frissítése (szándékosan NEM ennek a
feladatnak a része, hogy elkerüljük az ütközést a párhuzamosan futó
testvér-feladatokkal).

**Releváns fájlok:** `tools/reference/temperature_ref.py`
(`greenhouse_temperature`, `ocean_buffering_temperature`,
`weather_deviation_k`, `climate_cycle_temperature_k`,
`temperature_kelvin_full`), `tools/reference/temperature_full_vectors.json`,
`tools/reference/deterministic_math_ref.py` (ND-27), `tools/reference/
noise_ref.py` (`fbm`, ND-31/32/33), `docs/00-spec-v1.0.md` §25, §28, §29,
§32.

### ND-43 — M7 hátralévő rész (tavak, jég/hó, statikus A1 eróziós pass): modellezési küszöbök és egyszerűsítések

M7 hátralévő tétele (`docs/backlog.md`: "Tavak, jég/hó, eróziós visszahatás")
a `tools/reference/hydrology_ref.py` már kész priority-flood/flow
accumulation eredményére épül (`tools/reference/lakes_ice_erosion_ref.py`).
A spec (§35 Tavak, §36 Jég és hó, §18 Erózió) egyik résznél sem ad zárt
numerikus küszöböt vagy együtthatót — az alábbi döntések mind ebből a
hiányból fakadnak, és mind **egyszerű, statikus, egyetlen elevációmezőre
ható közelítések**, nem az M10 deep-time lánc része.

**1. Tavak (§35) — melyik tile "tó".**

A depresszió-feltöltés (priority-flood) melléktermékeként minden tile-ra
ismert a feltöltött ("víz-") szint. Egy tile tó, ha `filled > raw_elevation
+ LAKE_MIN_DEPTH_M` és nem óceán. A `LAKE_MIN_DEPTH_M = 0.5` (méter) egy
numerikus zaj-küszöb, nem fizikai állítás — enélkül a lebegőpontos
kerekítés miatt szinte minden sík tile "tóként" jelenne meg egy epsilonnyi
feltöltéssel. A tavakat a már verifikált `neighbor()` függvénnyel BFS-sel
összefüggő komponensekbe csoportosítjuk. A priority-flood korrektségi
tulajdonsága miatt egy medence belső tile-jai jellemzően egyetlen közös
feltöltött szintet (a kifolyási/sill-pont magasságát) kapják — ritka,
többszintű (teraszos) medencéknél ez nem szigorúan igaz, ezért a
`surfaceElevation` mezőt a komponens átlagaként adjuk vissza, a min/max
szórást pedig diagnosztikaként jelentjük (a script kiírja, hány "nem
egyszikű" tavat talált — a jelenlegi teszt-világon 0-t).

A tavak kialakulásának többi módja (gleccser, kráter, tektonikus medence,
folyóelzárás — §35 felsorolása) NEM külön logika: ezek már MOST is
implicit módon topográfiai mélyedésként jelennek meg a domborzatban (pl. a
becsapódási kráterek már bevésik magukat az elevációba), ezért ez a
detektor őket is megtalálja, csak nem a keletkezési ok szerint különíti el
— ez tudatos hatókör-szűkítés, nem hiányzó eset. A "tavak időben"
alfejezet (feltöltődhet, kiszáradhat, túlfolyhat, tengerrel kapcsolatba
kerülhet) időfüggő állapot, ezért NEM ennek a statikus passznak a része.

**2. Jég/hó (§36) — statikus osztályozás küszöbei.**

A §36.2 "Accumulation > Melt" feltételt egy éves átlaghőmérséklet-küszöbre
egyszerűsítjük. A `temperature_ref.temperature_kelvin` egy adott naphoz
(`day_t`) ad napi átlagot; ezt 12 ponton (`NUM_ANNUAL_SAMPLES`) tovább
mintavételezzük a keringési periódus mentén — ugyanaz az elv, mint a
`temperature_ref.py`-ban a napi mintavételezésnél (sűrű mintavétel zárt
formula helyett, mert az utóbbi szinguláris a pólusoknál).

- **Permanens jég**: éves átlaghőmérséklet `< 258.15 K` (-15 °C).
- **Szezonális hó**: az éves átlag e fölött van, de a leghidegebb
  mintavett hónap `< 273.15 K` (0 °C, a víz fagyáspontja — ez fizikai
  állandó, nem becsült érték).
- **Nincs**: egyik feltétel sem teljesül.

A -15 °C-os küszöböt **empirikusan illesztettük** a `temperature_ref.py`
jelenlegi paramétereihez (Föld-szerű napállandó, 23.44°-os tengelydőlés,
33 K fix üvegházhatás): a modul saját szélesség-táblázata szerint ez kb.
50-55 fok szélesség fölött ad permanens jeget, ami plauzibilis analógia a
valódi sarkköri jégsapkákhoz, de **nem hivatkozott klimatológiai
konstans** — csak ehhez az egyszerűsített hőmérséklet-modellhez illesztett
heurisztika. Ha a `temperature_ref.py` alapmodellje változik (pl. a
33 K-es fix üvegházhatás finomodik, ld. `docs/backlog.md` M5 tétele), ezt a
küszöböt újra kell hangolni.

A 36.3 gleccseráramlás-diffúzió (jégvastagság+lejtő alapú modell, ami
eróziót/völgyeket/morénákat/tengerszintet is befolyásol) **explicit módon
HALASZTVA** — ez önálló, nagyobb feladat, nem fér bele ebbe a statikus
osztályozási passzba.

**3. Statikus (A1) eróziós pass (§18.2) — proxyk és a "k" együttható.**

`ErosionRate = k · Rainfall^α · Slope^β · MaterialFactor` egyetlen additív
korrekcióként alkalmazva (nem idő-integrált differenciálegyenlet):

- `Rainfall` proxy → normalizált flow accumulation (`[0,1]`) — ugyanaz a
  proxy, amit a `hydrology_ref.py` már használ a folyó-küszöbölésnél.
- `Slope` proxy → `|raw_elevation(k) - raw_elevation(parent(k))|`,
  normalizálva a szárazföldi maximummal. A `parent` a priority-flood
  folyásirány-célpontja — ez a FELTÖLTÖTT magasság szerint monoton csökken
  a cél felé, a NYERS elevációkülönbség előjele ezért nem feltétlenül
  "lefele" mutat, ezért abszolút értékkel dolgozunk (csak a meredekség
  mértéke érdekel, nem az iránya).
- `MaterialFactor = 1.0` (konstans) — nincs még külön kőzettípus/litológia
  mező a specifikációban implementálva; ha lesz, ez lesz a csatlakozási
  pont.
- `α = 0.5`, `β = 1.0` — a "stream power law" (`E = K·A^m·S^n`, tipikusan
  `m≈0.5`, `n≈1`) néven ismert, a folyóvölgy-bevágódás modellezésében
  általánosan használt **egyenletalak** átvétele. Fontos: ez NEM egy adott
  publikációból idézett számérték, csak a függvény alakja — a tényleges
  "k" együtthatót (`EROSION_MAX_DEPTH_M`) önállóan kalibráltuk.
- `EROSION_MAX_DEPTH_M = 250.0` méter: az elméleti maximális egyszeri-pass
  bevágódás (amikor a normalizált accumulation ÉS slope is 1.0 — ez a két
  szélsőség a gyakorlatban ritkán esik egybe, a ténylegesen megfigyelt
  maximum ez alatt marad). Úgy választottuk, hogy a jelenlegi szárazföldi
  elevációtartomány (kb. 2500-3800 m, ld. `crust_elevation_ref.py`
  `OCEANIC_BASE_M`/`CONTINENTAL_BASE_M`/`NOISE_AMPLITUDE_M`) kis törtrészét
  tegye ki egyetlen statikus passzban — egy valódi folyóvölgy több
  geológiai kor alatt alakul ki, nem egy lépésben.
- `DEPOSIT_FRACTION = 0.3`: az eróziós anyag ekkora hányada rakódik le a
  KÖZVETLEN lefele-szomszédon (a priority-flood `parent`-jén), ha az
  szárazföld; a maradék 70% "tovább szállítódik" (ebben az egylépéses
  közelítésben egyszerűen elvész / a tengerbe jut, nem követi tovább a
  teljes láncot). Ha a parent óceán, az üledék a tengerfenékre kerül, ami
  NEM része ennek a szárazföldi elevációmezőnek.

**Fontos, verifikációkor felszínre került viselkedés:** a legnagyobb
flow-accumulationú tile (jellemzően a torkolat/delta közelében) SAJÁT
bevágódása kicsi lehet (ha ott a lejtő lapos), miközben a VÉGSŐ
elevációja mégis NŐHET, mert a felvízi (magas erózióhozamú) szomszédai ide
rakják le az üledék egy részét — ez fizikailag helyes viselkedés
(deltaképződés, ld. a spec 18.2 utolsó mondata: "Az üledék alacsonyabb
helyeken lerakódhat"), nem hiba. A `lakes_ice_erosion_ref.py` plauzibilitás-
assertjei ezért a nyers `erosion[]` szótáron ellenőrzik a formula
accumulation/lejtő-monotonitását, a végső mezőn pedig csak azt, hogy
valahol tiszta bevágódás, valahol tiszta feltöltődés történik.

**Ha ez a heurisztika téves iránynak bizonyul** (pl. Unity-vizuális
ellenőrzésnél irreálisan mély kanyonok vagy irreális tófelszín jönne ki),
ezt a bekezdést kell frissíteni és a küszöböket/együtthatókat újrahangolni
— ne csendben, kódban módosítva.

### ND-44 — M10 erózió idővel + eljegesedés-ciklusok: zárt alakú relaxáció numerikus PDE helyett, illusztratív klíma-forcing a T_cycle helyett

**Kérdés.** Az M10 hátralévő fele (`docs/05-milestones.md` M10 sora):
"Erózió idővel, eljegesedés-ciklusok". A spec §17 `dH/dt = UpliftRate -
ErosionRate` és §18.2 `ErosionRate = k · Rainfall^α · Slope^β ·
MaterialFactor` egyenleteket kellene deep-time-ban (`timeMyr`)
kiértékelhetővé tenni. Két probléma: (1) a §18.2 teljes alakja a `Slope`
tagon keresztül ÖNMAGA a domborzattól függ, ami idővel maga is változik —
ez csatolt, nemlineáris PDE, aminek nincs általános zárt megoldása; (2)
nincs kész `Rainfall` mező (a hidrológia, `hydrology_ref.py`, statikus,
egyetlen elevációs mezőn dolgozik) és nincs kész klíma-modul `T_cycle`
tagja (`temperature_ref.py` explicit halasztja).

**Az ND-04 (nyitott, "Timestep-invariancia toleranciái", M10-re
revideálandó) kontextusa.** Ez a munka pont az az M10 lépés, ami az
ND-04 revideálását kellene, hogy megalapozza. A tapasztalat: egy naiv
Euler-lépegetéssel megvalósított `dH/dt = k(H_eq-H)` (ld.
`_naive_euler_relaxation` a referenciában) UGYANAZON `t=365 Myr`
végpontra `n_steps=1`-nél `-4623m`-et, `n_steps=2000`-nél `432.6m`-et ad —
tehát **több ezer méteres eltérés** pusztán a lépésszám miatt, miközben a
"helyes" (zárt alakú) válasz `432.617069 m`. Ez konkrét, mért bizonyíték
arra, hogy az I1 determinizmus miért sérülne egy iteratív integrátorral:
két, egyébként azonos seedű világ MÁS eredményt adna, ha a mérnöki kód
más `timeMyr` felbontásban kérdezné le a mezőt (pl. a renderelő 50 Myr-es
lépésekben, egy teszt 1 Myr-esben).

**Döntés — 1. Erózió: lineáris relaxáció, ZÁRT alakban, NEM a teljes
§18.2 formula.** A hegység-relief (a lemezhatár statikus uplift-bónusza,
`plate_boundary_ref.boundary_uplift`) exponenciálisan relaxál egy
egyensúlyi érték felé:

```
H(t) = H_eq + (H0 - H_eq) * exp(-t / tau)
H_eq = EQUILIBRIUM_FRACTION * H0      (H0 = boundary_uplift(...), a t=0 M4 érték)
```

Ez a `dH/dt = (1/tau)(H_eq - H)` lineáris ODE egzakt megoldása — a
`Rainfall`/`Slope`/`MaterialFactor` szorzat-modell HELYETT egy egyszerűsített,
"topográfiai relaxációs idő" jellegű közelítés (a geomorfológiában használt
koncepció: egy reliefzóna karakterisztikus ideje, amíg megközelíti az új
egyensúlyi állapotát egy tektonikai perturbáció után). **A konkrét
paraméterek (`OROGENIC_RELAXATION_TAU_MYR = 50`, `EQUILIBRIUM_FRACTION =
0.35`) ILLUSZTRATÍV MODELLEZÉSI VÁLASZTÁSOK, NEM egy publikált geológiai
mérésből verifikált szám** — nincs a `tools/reference/kat_vectors`-hoz
hasonló hivatalos forrás egy "mennyi idő alatt erodálódik egy hegylánc a
felére" konstansra, ezért ez **explicit megerősítést igényel**, mielőtt
C#-ba kerülne (a python-reference skill "ha nincs hivatalos forrás,
jelezd és kérj megerősítést" szabálya szerint). A `t=0` eset bitre
(mért: `4.55e-13 m` numerikus zaj) visszaadja a meglévő statikus M4
eredményt (`elevation_with_boundary`) — nincs seed-törő hatás a meglévő
world-öknél `t=0`-nál.

**A `Rainfall`/`Slope`/`MaterialFactor` teljes csatolt modellje EXPLICIT
HALASZTVA marad** (nincs `rainfall_ref.py`, nincs iteratív domborzat-
visszahatás) — ez egy tudatos hatókör-szűkítés, nem hallgatólagos
egyszerűsítés, mert nincs jelenleg megvalósítható zárt alak rá, és egy
numerikus PDE-megoldó direktben sértené I1-et (ld. fent).

**Döntés — 2. Eljegesedés: önálló, illusztratív periodikus forcing, NEM a
valódi T_cycle.** `GlobalTempOffset(t) = A · sin(2π·t/T)`, `A = 6K`, `T =
150 Myr` — **szintén illusztratív, nem verifikált geológiai/csillagászati
adatból levezetett szám** (a valódi icehouse/greenhouse szuperkontinens-
ciklusok időskálája nagyságrendileg hasonló, de ez NEM azt jelenti, hogy a
150 Myr egy konkrét, forrásból idézett érték — explicit megerősítést
igényel). Egy idealizált, szélesség-lineáris hőmérséklet-profillal
(`T_EQUATOR_K`, `LATITUDE_TEMP_GRADIENT_K_PER_RAD` — szintén illusztratív)
kombinálva a jégvonal szélessége zárt alakban, analitikusan (nem numerikus
gyökkereséssel) számolható. `t=0`-nál az eltolás 0 (visszamenőlegesen
kompatibilis a statikus M5 hőmérséklet-modellel, ha valaki hozzáadja az
eltolást). **Ezt később egyesíteni kell a klíma-modul valódi `T_cycle`
tagjával**, ha az elkészül (`temperature_ref.py` docstringje explicit
"T_cycle halasztva"-ként jelzi) — ez a modul nem helyettesíti azt, csak
egy ideiglenes, önmagában is tesztelhető proxy addig.

**A két alrendszer szándékosan NINCS összekapcsolva** (pl. "jégkorszakban
gyorsabb a glaciális erózió") — ez egy további, külön dokumentálandó
modellezési döntés lenne, amit itt nyitva hagyunk.

**Timestep-invariancia bizonyítéka (I1/ND-04).** A `_relax_towards(h0,
h_eq, t, tau)` függvényt `n_steps` egyenlő részintervallumra láncolva
(`chain_relaxation`, minden lépésben az előző kimenet az új `h0`, de a
`h_eq` FIX marad az EREDETI `h0`-ból számolva) az exponenciális relaxáció
félcsoport-tulajdonsága (`exp(-a(t1+t2)) = exp(-a·t1)·exp(-a·t2)`) miatt
`n_steps ∈ {1,2,3,5,13,47,101,500}`-ra mérve **max `5.68e-14 m` eltérést**
adott az egylépéses direkt kiértékeléshez képest (`h0=1234.5`, `t=365
Myr`, direkt érték `432.6170692017 m`) — ez a dupla lebegőpontos
kerekítés zajszintje, NEM diszkretizációs hiba. **Fontos implementációs
csapda, amit menet közben találtunk és javítottunk:** az első próbálkozás
a `h_eq`-t minden lépésben ÚJRASZÁMOLTA a pillanatnyi (már relaxált)
`h`-ból (`h_eq = eq_fraction * h_pillanatnyi`) — ez elrontotta a
félcsoport-tulajdonságot, és a lánc `n_steps`-től függő, akár több száz
méteres eltérést adott (mért: `n_steps=2`-nél `421.75 m` eltérés az
egylépéses eredménytől). A javítás: `h_eq` a teljes láncon át FIX, az
EREDETI `h0`-ból számolva. Ez önmagában egy élő demonstrációja annak,
milyen könnyű csendben timestep-függő modellt építeni, ha az egyensúlyi
cél nem marad invariáns a felbontással szemben — pontosan az a hiba-
osztály, amire a feladatkiírás figyelmeztetett.

**Referencia:** `tools/reference/erosion_glaciation_deep_time_ref.py`.
Tesztvektorok: `erosion_glaciation_deep_time_vectors.json`
(`erosionVectors`: 400 minta `(face,level,u,v,plateId,timeMyr) ->
(elevation,isOceanic)`; `glaciationVectors`: 200 minta `timeMyr ->
(globalTempOffsetK, iceLineAbsLatitudeRad, isIced)`). A script kétszeri
futtatása bitre azonos JSON-t ad (ellenőrizve).

**Nyitott, megerősítést igénylő pontok (a C# port ELŐTT eldöntendő):**
1. `OROGENIC_RELAXATION_TAU_MYR = 50` és `EQUILIBRIUM_FRACTION = 0.35` —
   illusztratív, nem forrásból verifikált.
2. `GLACIATION_PERIOD_MYR = 150`, `GLACIATION_AMPLITUDE_K = 6` —
   illusztratív, nem forrásból verifikált.
3. Kell-e a glaciális erózió és az orogén relaxáció összekapcsolása
   (jelenleg szándékosan szétválasztva).
4. A `Rainfall`/`Slope`/`MaterialFactor` teljes §18.2 modell továbbra is
   halasztva marad — mikor (melyik milestone) kerüljön napirendre, és
   milyen zárt-alakú vagy dokumentáltan-elfogadott-kockázatú megoldással.

### ND-45 — Lemez-életciklus (M10+M11 összevonva): split/merge/rift ütemezése timestep-invariáns módon

**Kérdés.** A backlog két tétele ("M10 | Lemez-születés/-halál (§16) | Fix
plateCount a világ elejétől" és "M11 | Rift-zóna + lemez-hasadás/egyesülés |
A spec ezt folytonos modellként írja le") ugyanaz a spec-szakasz
(`docs/00-spec-v1.0.md` §16, 963-980. sor): `PlateSplitEvent`,
`PlateMergeEvent`, `SubductionTermination`, `RiftActivation`, `HotspotBirth`,
`HotspotDeath`, "a world seed és a geodinamikai állapot alapján ütemezve". A
spec **nem ad zárt képletet** ezekhez (szemben pl. a becsapódásokkal, ahol
Schmidt & Housen (1987) skálázás van, ld. ND-28) — minden időzítési/
geometriai szabályt itt kellett megtervezni. Referencia:
`tools/reference/plate_lifecycle_ref.py`.

**A vezérlő korlát: ND-04 (timestep-invariancia).** Egy futásidőben
akkumulált "stressz-számláló" (pl. "minden Myr-ben +x esély a hasadásra,
összegezve") **lépésköz-függővé tenné a történelmet** — más dt mellett más
esemény-idő jönne ki. Ehelyett minden lemez a **saját, zárt-formájú
"életrajzi sorsát"** egyetlen Threefry-hívásból kapja, kizárólag a saját
`plateId`-jából és a world seedből (`plate_lifecycle_roll`) — a "születési
idő" (mikor jött létre egy korábbi split révén) csak egy ADDITÍV ELTOLÁS a
már rögzített élethosszhoz, nem egy másik állapotfüggő bemenet.
`PlateTopologyAtTime(seed, t)` (`resolve_topology`) minden hívásnál a
TELJES leszármazási fát újraépíti a gyökerektől — nincs memoizálás, nincs
modul-szintű mutable állapot. Ezt konkrét számpéldával is bizonyítottuk: a
`world_seed=0xA7C944210000, plateCount=10` világban közvetlenül lekérdezve
`t=725 Myr`-t 11 aktív lemezt kapunk; ha előtte a kód "lépésenként"
(t=50, 123, 200, 333, 500, 600, 700 Myr) is lekérdezi az állapotot (mintha
egy step-based szimulátor lenne), a `t=725`-nél kapott lista **bitre
azonos** marad (pl. lemez 1 pozíciója mindkét esetben pontosan
`(-0.7312988446535255, 0.3106893665156318, 0.6071854060684712)`).

**Döntések (hatókör-szűkítés, a korábbi milestone-ok mintáját követve):**

1. **RiftActivation + PlateSplitEvent implementálva.** Minden lemez
   `plate_lifecycle_roll(seed, plateId)` hívásból kap egy `kind`
   (`split`/`merge`/`none`, valószínűségek `P_SPLIT=0.45`, `P_MERGE=0.25`),
   egy `lifespanMyr`-t (`[80, 400]` Myr sávból, a Wilson-ciklus
   nagyságrendje) és egy `riftFractionOfLifespan`-t (`[0.40, 0.85]`) — a
   `RiftActivation` időpontja `birth + lifespan*riftFraction`, korábbi mint
   maga a split (`birth + lifespan`). **Ezek a numerikus sávok (P_SPLIT,
   P_MERGE, lifespan-tartomány, rift-frakció-tartomány) NEM hivatalos
   geológiai forrásból verifikált értékek, hanem plauzibilitásra hangolt
   MVP világtervezési konstansok** — a CLAUDE.md "ha nincs elérhető
   hivatalos forrás, jelezd explicit" szabálya szerint ez itt explicit
   jelezve van, és **felhasználói megerősítést igényel**, mielőtt a C#
   portban "véglegesnek" tekintenénk.
2. **Split geometria: két új mag ± `SPLIT_HALF_ANGLE_RAD` (0.12 rad, ~6.9°)
   szögeltolással a szülő split-időponti pozíciójától, egy véletlen
   merőleges tengely körül.** Mivel a Voronoi-hozzárendelés a legközelebbi
   maghoz köt, ez a régi cellát a két új mag felező-síkja mentén
   automatikusan kb. felezi — nincs szükség explicit poligon-vágásra. Mérve
   (world_seed=0xA7C944210000, plate 0 split t≈184 Myr-nél, level 4
   Voronoi-mintavétel): szülő terület splitkor 0.1003 (a gömb töredéke), a
   két gyermek együtt 0.0618+0.0553=0.1172 közvetlenül utána — közel a
   szülőéhez, nagyjából egyenlő arányban osztva.
3. **PlateMergeEvent és SubductionTermination mechanikailag EGYSÉGESÍTVE**:
   mindkettő "lemez-eltávolítás" — a lemez magja egyszerűen törlődik, a
   területe a megmaradt szomszédok között a szokásos Voronoi-szabály révén
   automatikusan újraoszlik, nincs külön nyilvántartott "győztes" lemez. A
   spec fogalmilag megkülönbözteti a kettőt (két lemez egyesülése vs. egy
   lemez teljes elnyelése), de MVP-szinten a mechanika azonos — egy valódi,
   két lineage-t egyetlen továbbélő azonosítóba olvasztó egyesülés
   halasztva, mert tömeg-/fluxus-követést igényelne.
4. **HotspotBirth/HotspotDeath HALASZTVA.** A projektben egyáltalán nincs
   még hotspot-modell (sem statikus, sem dinamikus) — ez önmagában külön
   milestone-nyi munka. Csak a `RandomProperty` tartomány (17-19) van
   fenntartva a jövőre.
5. **Leszármazási fa mélysége `MAX_GENERATION=3`-nál levágva.** Ez véges
   korlát egy véges teszt-horizonton (elkerüli a korlátlan elágazást), NEM
   fizikai állítás arról, hogy a lemezek 3 hasadás után mindig
   stabilizálódnak.
6. **Új `PlateId` séma: gyökér-lemezek megtartják a kis szekvenciális int
   azonosítót (0..N-1, visszamenőleg kompatibilis az M4 statikus listával);
   gyermek-lemezek nyers 64-bites Threefry-hash-t kapnak azonosítóként**
   (`_block(...)` kimenetének első szava, nem [0,1)-be skálázva). Ez azt
   jelenti, hogy a `PlateId` típusának a C# portban `ulong`-gá kell válnia
   (feltehetően jelenleg `int`) — ez FÜGGETLEN a `TileId` bit-layout
   invariánstól, jelzés a core-dev felé a porthoz. Ütközés-valószínűség a
   szimuláció léptékén (≪10^6 csomópont) elhanyagolható.
7. **Új `RandomProperty` azonosítók a Tectonics doménben: 14 =
   LifecycleRoll, 15 = SplitAxisHint, 16 = ChildPlateId** (a meglévő 10-13
   után a következő szabad sorszámok) — MÉG NINCSENEK felvéve a
   `src/WorldGen.Core/Random/RandomDomain.cs`-be, ez a C# port feladata.
8. **Terület `estimate_areas`-ben a gömb felületének törtrészeként (0..1),
   nem abszolút km²-ben** — nincs még elfogadott bolygó-sugár-konstans
   ehhez a modulhoz (a `PlanetConstants.RadiusMeters`, ld. ND-28, csak a
   becsapódás-modulban létezik eddig).

**Indoklás:** ez a minta megegyezik minden korábbi milestone
hatókör-szűkítésével (ND-28, ND-29, ND-30 stb.) — kisebb, de tesztelhető,
timestep-invariáns MVP, explicit deferrállal és explicit jelzett,
megerősítést igénylő tervezési konstansokkal, nem csendes leegyszerűsítéssel.

**Verziózás:** ha ez a C# portba kerül, a `RandomDomain`/`RandomProperty`
bővítés (7. pont) és a `PlateId` típusváltás (6. pont) **seed-törő**
változás — verzióemelést igényel, ld. CLAUDE.md "Verziózás és
seed-kompatibilitás".

### ND-46 — Screen-space LOD metrika: távolság-alapú, NEM nézési-szög-tudatos (felfedezett korlát, nem hiba)

**Felfedezés (felhasználói jelentés, 2026-09-02, screenshot-alapú diagnózis).**
A felhasználó azt észlelte, hogy egy közeli zoomnál a látott bolygófelszín
egy része finoman, más része durván (bázis-szintű) tile-okkal jelenik meg,
FÜGGETLENÜL a kamera forgatásától/mozgatásától — azaz a mintázat a bolygó
FELSZÍNÉHEZ, nem a kamera nézőpontjához kötött (ezt kísérletileg is
megerősítettük: forgatás/ki-be zoom nem mozdítja a mintázatot). Több
hipotézist (hiszterézis-kaszkád, "olcsó előszűrés" alulbecslése, nézőkúp-
lefedettség szélsőséges képernyő-aránynál) sorra kizártunk mérésekkel és
egy standalone teszt-harness-szel (`AdaptiveQuadTree.BuildCut` közvetlen
hívása a pontos élő paraméterekkel). A végső, felhasználó által is
megerősített megfigyelés: a durvábban maradó (kék) terület a bolygó
LÁTHATÓ KÖRVONALÁHOZ/HORIZONTJÁHOZ közelebb esik, mint a finomabb (piros)
terület.

**Gyökérok.** Az `AdaptiveQuadTree.Visit()` finomítási döntése
`atan2(rTile, distance)` — a csomópont befoglaló-gömbjének SZÖGSUGARA a
kamerától mért EGYENES-VONALÚ 3D TÁVOLSÁG alapján. Ez a metrika NEM veszi
figyelembe a felület-normál és a nézési irány szögét (a rálátás
"súroló"-ságát). Egy görbült felszínen (bolygó) ez azt jelenti: a látható
körvonalhoz/horizonthoz közeli terep ÉRDEMBEN NAGYOBB egyenes-vonalú
távolságra van a kamerától, mint a "elölnézeti" terep — annak ellenére,
hogy a súroló rálátás miatt (erős foreshortening ellenére) még mindig
jelentős képernyő-területet foglalhat el. A rendszer emiatt a saját belső
logikája szerint HELYESEN dönt (kevesebb finomítás a távolabbi
csomópontoknak), de ez a döntés NEM követi a tényleges vetített
képernyő-méretet ezen a tartományon.

**Ez MÁR KORÁBBAN DOKUMENTÁLT, ismert hiányosság** — az
`AdaptiveQuadTree.DefaultMaxLeafCount` biztonsági-korlát doksija
(`unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/AdaptiveQuadTree.cs`)
explicit megemlíti: *"A GYÖKÉROK (a helyes screen-space terület-vetítés,
ami figyelembe venné a nézési szöget is) MÉG NINCS megoldva... dokumentáltan
NYITOTT következő lépés: helyes szög-függő/vetített-terület metrika vagy
explicit horizont-vágás."* Ez az ND csak formálisan rögzíti ezt a
korábban csak kód-kommentben élő nyitott kérdést, a felhasználói
diagnózis alapján megerősítve, hogy a hatása ÉRDEMBEN ÉSZLELHETŐ vizuálisan.

**Opciók (A opció VÁLASZTVA és MEGVALÓSÍTVA, ld. lent):**

| Opció | Előny | Hátrány / kockázat |
|---|---|---|
| **A: Nézési-szög-korrekció a metrikában** — `angularRadius` szorzása egy, a felület-normál/nézési-irány szögéből (`cosTheta`) származó tényezővel, hogy súroló rálátásnál is nagyobb "hatékony" szögsugarat adjon | Közvetlenül a gyökérokot orvosolja, viszonylag kis kódváltozás | A felület-normál kiszámítása (vagy közelítése a gömb-középponttól a tile-központig húzott sugárból) minden Visit()-hívásnál plusz költség; hiszterézis-interakció újra-tesztelendő |
| **B: Explicit horizont-vágás/-súlyozás** — a horizonthoz közeli tile-okat egy külön, EGYSZERŰBB szabály finomítja (pl. mindig legalább N szinttel a bázis fölé, ha látókúpban van) | Egyszerű, kiszámítható | Nem "helyes" fizikai metrika, csak tapasz; új konstans(ok) hangolást igényelnek |
| **C: Marad, ahogy van (dokumentált korlát)** | Nulla kockázat, nulla munka | A felhasználó által ténylegesen észlelt vizuális hiányosság megmarad |

**Megvalósítás (2026-09-02, A opció).** `AdaptiveQuadTree.Visit()`-ben, a
nyers `angularRadius` kiszámítása után: a felület-NORMÁL egy gömbön
egyszerűen a tile-középpont egységvektora (`(cx,cy,cz)/planetRadius`,
nincs külön költség), `cosGrazing` = a normál és a "csomópont → kamera"
irány skaláris szorzata. `cosGrazing <= MinUsefulCosGrazing` (0.02, kb.
88.9°) esetén a csomópont a látható felszín túloldalán van VAGY majdnem
tökéletesen súroló — ilyenkor azonnal "nem bővül"-ként zárjuk le (ingyenes
hátterlap-oldali korlátozás is egyben). Egyébként `effectiveAngularRadius
= min(angularRadius / cosGrazing, π/2)` — a π/2-es felső korlát UGYANAZ a
védelem, mint az `IsWithinViewCone` korábbi (8480cbe) javításánál, hogy a
korrekció maga ne okozhasson degenerációt. A `SelectCut` "olcsó előszűrése"
(gyökér-szintű távolság-alapú rövidzár) is frissítve: a
`maxRelevantDistance`-t el kellett osztani `MinUsefulCosGrazing`-gal,
különben pont azokat a távoli, de súroló szögű gyökereket zárta volna ki
idő előtt, amiket a korrekció finomítani akarna.

**Verifikáció (1. kör, `MinUsefulCosGrazing=0.02`):** a pontos élő
(felhasználó Console-DIAG-jából vett) kamera-paraméterekkel reprodukálva:
a javítás előtti `cut.Count=393651` (legmélyebb szint 9) helyett a
javítás után `cut.Count≈401286`, legmélyebb szint 15 — a súroló szögű
terület érdemben mélyebbre finomodik. Új regressziós teszt:
`GrazingAngleCorrection_RefinesFarButStillVisibleTerrain`
(`AdaptiveQuadTreeTests.cs`). A teljes meglévő tesztkészlet (23/23,
standalone `lod-verify` harness) és mindkét érintett Unity-assembly
(`Assembly-CSharp.csproj`, `WorldGen.Viewer.Lod.Tests.csproj`) fordítása
zöld/tiszta volt.

**ÉLŐ TELJESÍTMÉNY-REGRESSZIÓ (ugyanaznap, `useGpuGeometry` KIKAPCSOLVA
mellett is):** a felhasználó élesben `cut.Count=479661`-et és
`adaptiv ujraepites 2341.7ms`-et mért — használhatatlanul lassú, a
CPU-s renderelési út (nem a Fázis 3 GPU-probléma) nem bírta el a
megnövekedett tile-számot. **A hiba az volt, hogy a verifikáció CSAK a
`BuildCut` (kiválasztás) mélységét/méretét ellenőrizte, a RENDERELÉS
tényleges költségét sosem élő Unity Editorban** — ld.
`history/RETROSPECTIVE-2026-09-02-adaptive-lod-saga.md` a teljes
elemzésért és tanulságokért.

**2. kör (`MinUsefulCosGrazing=0.02` → `0.3`, jelentősen szigorítva).** A
standalone harness-ben lemért, konzervatívabb (0.3/0.2/0.12) jelöltek
közül a legszigorúbbat választva: a legutóbbi élő kamera-paraméterekkel
`cut.Count≈401484` (base+~8268, közel a régi, elfogadható tartományhoz),
legmélyebb szint továbbra is 15 ott, ahol ténylegesen súroló a rálátás —
sokkal kevesebb többlet-tile, mint a 0.02-nél. A teljes tesztkészlet
(23/23) és mindkét assembly fordítása továbbra is zöld/tiszta.
**EZ A HANGOLÁS MÉG NINCS ÉLŐ UNITY EDITORBAN MEGERŐSÍTVE** — a fenti
hiba tanulsága szerint ez explicit így is marad jelezve, amíg nincs élő
visszajelzés a renderelési költségről, nem csak a kiválasztási logikáról.

**Hatás/prioritás:** vizuális minőség, NEM determinizmus/I1-I4-sértés (a
`src/WorldGen.Core`-t nem érinti, tisztán `unity/WorldGenViewer` renderelési
döntés). Élő Unity Editor-teszt (mind a vízszintes sáv eltűnése, MIND a
renderelési sebesség) MÉG MEGERŐSÍTENDŐ.

**Verziózás:** nem seed-törő (a világmodellt nem érinti, csak a viewer LOD-
kiválasztást).

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/AdaptiveQuadTree.cs`
(`Visit()`, `IsWithinViewCone()`), `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`
(`RecomputeCutAndRebuildAdaptiveMesh`).

### ND-47 — Adaptív LOD újratervezés: a bejárás leválasztása a statikus base-ről + kétszámlálós költségvetés + fázisterv

**Állapot:** Fázis 1 **és Fázis 2 implementálva és a kiválasztási szinten
verifikálva** (standalone harness + EditMode tesztek). Egy élő Unity-mérési kör
megtörtént (2026-09-03) — ez vezetett a Fázis 2-höz. Fázis 3–5 **nyitott**. A
**render-simaság (Fázis 3) a fő hátralévő elintézendő** — ld. lent.

**Kontextus / a felfedezett probléma.** A felhasználói jelentés: a bolygó
felszíne zoomra „töredezett" marad, a tile-okat nem szedi 4 részre, holott a
mélység indokolná; a képernyő egy kisebb, *esetleges* (futásonként változó)
részén finomodik csak. A `docs/04-decisions.md` ND-40/ND-46 és a kód elemzése
két, egymásra rakódó gyökérokot tárt fel:

1. **A `maxLeafCount` biztonsági korlát (200 000) kisebb volt, mint a
   `adaptiveBaseLevel=8` base-gyökereinek száma (6·4⁸ = 393 216).** A régi
   `SelectCut` a base-szintről indult, és a `leafCounter` MINDEN meglátogatott
   csomópontot számolt — így a korlát már a base-felsorolás közben, garantáltan
   bevágott, mielőtt bármi érdemi finomodás történt. A `Parallel.For`
   nemdeterminisztikus sorrendje miatt a keret-kimerülésig eljutott ~51%
   véletlenszerű, foltszerű finomodást adott → ez az „esetleges" tünet.
2. **A fél-ív `ComputeHalfArcMinUsefulCosGrazing` küszöb** a látható felszín
   horizont-felőli felét eleve durván hagyta (másodlagos, kisebb hatás).

**Vezérelv (a helyes screen-space-error kvadfa).** Egyetlen metrika (vetített
pixel-hiba) dönt; a bejárás költsége a LÁTHATÓ, finomítandó régió méretével
arányos, NEM a bolygóéval; a részletesség folytonosan nő popping nélkül. Négy
pillér / öt fázis:

- **Fázis 1 — a bejárás gyökérszintjének leválasztása a statikus base-ről.**
  A `SelectCut`/`Visit` új paramétert kap: `traversalRootLevel` (alacsony
  induló szint, elesben 3) és `staticBaseLevel` (a statikus réteg fedi a
  base-t és alatta). A bejárás az alacsony szintről indul, és CSAK oda
  ereszkedik le, ahol a base FÖLÉ kell finomítani; a sík régióban a rekurzió
  azonnal leáll és SEMMIT nem emittál (azt a statikus réteg fedi). A base-alatti
  393 216 csempe így fel sem merül a bejárásban. **A default-ok visszafelé-
  kompatibilisek** (`traversalRootLevel = -1` → base; `staticBaseLevel = -1` →
  teljes partíció), a régi hívók/tesztek változatlanul működnek.
- **Kétszámlálós költségvetés.** A `maxLeafCount` mostantól a KIMENETET
  (emittált `level>base` leaf-ek, ≈ render-költség) korlátozza, nem a
  meglátogatott csomópontokat; a base-alatti leereszkedést a látókúp+horizont-
  culling korlátozza (screen-space). Külön, generózus `workCap`
  (= `maxLeafCount*8`) őrzi a rendszert bármilyen bejárás-robbanás ellen. Ez
  szünteti meg a starvation-t: a produktív (nadírhoz közeli) ág nem éhezik ki
  a base-alatti bejárás miatt.
- **Kiterjedés-tudatos horizont-cull.** Egy csomópont CSAK akkor esik ki
  horizont mögöttiként, ha a befoglaló-gömbje TELJESEN a `C·P = R²` horizont-
  sík mögött van (`cDotP + camLen·rTile < R²`) — így a durva, a nadír fölé
  nyúló, de középpontjukkal már horizonton túli tile-ok NEM esnek ki (ez volt
  egy megtalált hiba: közeli zoomnál a level-3 nadír-tile középpontja már
  ~10°-ra, a horizont ~6°-ra volt → a teljes nadír-oszlop kiesett). Az
  agresszív anizotrop (`1/cosGrazing`) korrekció csak a base-szinttől lefelé
  hat, ahol a tile már elég kicsi, hogy a középpont-alapú szög értelmes legyen.

- **Fázis 2 — prioritásos, költségvetett finomítás — IMPLEMENTÁLVA.**
  Az élő Unity-mérés (2026-09-03, `PerfLog_20260903_222334`) megmutatta, hogy a
  Fázis 1 után a cut `targetTilePixelSize=12`-nél (a felhasználó által beállított
  agresszív érték) legitim módon ~160–200 000 csempét kér → a hard output-cap
  bevágott, ismét *haphazard* (a nézett közép nem osztódott), és a render
  (`RebuildAdaptiveMesh` O(cut)) 4–6 s lett. Ezért a `SelectCutPrioritized`
  best-first (SortedSet-alapú prioritási sor, kulcs: effektív szögsugár-hiba,
  holtverseny `TileId.Value` — **egyszálú, determinisztikus**): a legnagyobb
  képernyő-hibájú (kamerához legközelebbi, központi) csempét finomítja előbb, és
  a `maxLeafCount` (= új `adaptiveRenderBudget` mező, default 25 000) kimerülésekor
  a periféria durvább marad. Ez megszünteti a haphazard-ot és tunolható fékké
  teszi a render-költséget. A régi mód (`staticBaseLevel<0`) a párhuzamos DFS-t
  tartja. **Fontos:** a cél-tuning ÖNMAGÁBAN nem old meg (48px → 0 finomodás
  ezen a zoomon, 12px → túl sok); a priorizálás + budget a helyes mechanizmus.

  **Fázis 2 korrekció (2026-09-03, második élő mérés — „nem jó helyen
  osztódik"):** az első prioritás-kulcs az `angularRadius / cosGrazing` (ND-46
  anizotrop) hiba volt — ez a SÚROLÓ (horizont-menti) csempéknek adott magasabb
  prioritást, mint a nadírnak, ezért a budget a horizont-sávra ment, nem oda,
  ahová a felhasználó néz (a kamera a logban ≈ nadírba nézett). Javítás: a
  prioritás és a felbontás-döntés a **nyers** `angularRadius`-ból (boost nélkül)
  születik → a legközelebbi (képernyő-középi, nadír) csempe kapja a budgetet
  előbb; a súroló csempék nyers szögmérete kicsi → kevés budgetet visznek. Ez
  egyúttal **csökkenti** a kért csempeszámot is (a boost ~7×-esen felfújta a
  grazing-sávot: 24px-nél 40k → 5,5k). Az `EvaluateNodeForPriority` már csak
  backface-cull-t (`cosGrazing<=0`) használ, agresszív `minUsefulCosGrazing`
  küszöböt nem. Verifikálva (harness H.3): a legmélyebb csempék átlagos
  `cosGrazing`-je 0,806 (a központ felé, nem a súroló sávban).
- **Fázis 3 — aszinkron BuildCut + EMIT worker szálon IMPLEMENTÁLVA; csak a
  mesh-upload main-thread.** (2026-09-04, 2. lépés.) Az élő mérés megmutatta, hogy
  a fix `adaptiveRenderBudget` (50k) a kötő korlát: közelebb zoomolva a mélyülő
  központ elszívja a budgetet a perifériától, ami emiatt VISSZAOLVAD (a „zoom
  közben összevonja a tile-okat" tünet). A megoldás: a budgetet nagyra kell
  venni, hogy ne legyen kötő — de ez csak akkor megfizethető, ha az EMIT (elesben
  ~230ms 50k-nál) nem a fő szálon fut. Mivel a `ComputeTileClassification` és a
  sarok/emit teljes lánc igazoltan TISZTA (Core + BodyFrameConversion axis-swap,
  nulla Unity-API; a GPU-klasszifikáció csak opcionális gyorsítás), a worker
  `Task` most a teljes geometriát előállítja (`ComputeAdaptiveMeshBuffersCpu`,
  `forceCpu` klasszifikáció), a fő szál pedig csak feltölti a Unity mesh-eket
  (`ApplyAdaptiveMeshBuffers`). Single-flight (a cache-írás így soha nem
  konkurens), robusztus fallback: bármilyen worker-hiba TARTÓSAN visszaáll a
  szinkron útra (`_asyncMeshRebuildDisabledAfterError`). Így a fő szál hitchje ~a
  mesh-upload (SetVertices, tíz-száz ms a méret függvényében). **A merge-on-zoom
  megszüntetéséhez a felhasználónak fel kell vinnie az `adaptiveRenderBudget`-et
  (~100-150k), most már megfizethető.** Trade-off: nagyobb budget → a worker
  tovább számol → a LOD kicsit jobban lemarad gyors mozgásnál (de a kamera sima).
  A `BuildCut` tiszta statikus függvény (nulla megosztott állapot/Unity-API), ezért
  worker szálon (`Task.Run`) fut — a kiválasztás költsége (magas részletnél
  200-590ms) így NEM okoz frame-akadást, a régi mesh látszik, amíg az új cut
  elkészül. Single-flight (`_cutTask`, egyszerre egy), az alkalmazás (a
  `RebuildAdaptiveMesh` = emit + Unity mesh-upload) a fő szálon, az `Update()`-ben,
  amint a task kész (`TryApplyCompletedAsyncCut`). Try/catch fallback: bármilyen
  hiba esetén a következő kör a régi szinkron úton fut. Kapcsoló:
  `useAsyncMeshRebuild` (default be; GPU-geometria mellett kikapcsol). **Hátralévő:**
  az EMIT (a geometria-dictionary-k építése, `RebuildAdaptiveMesh` compute-része)
  is worker szálra vihető (az emit-út igazoltan tiszta: BodyFrameConversion axis-
  swap + Core + cache, nincs Unity-API), csak a végső mesh-upload marad main-thread
  — ez viszi a fő szál maradék hitchét ~a mesh-feltöltésre. **Régi log-bottleneck** (a
  teljes rész):
  `RebuildAdaptiveMesh`, ami a TELJES dinamikus mesh-t újraépíti a fő szálon
  minden mozgásnál (`emitLoop` O(cut): 200k csempénél ~2 s, plusz classification/
  corners cache-miss az új csempékre). A `adaptiveRenderBudget` csak a hitch
  NAGYSÁGÁT csökkenti (kevesebb csempe), NEM szünteti meg (minden mozgásnál
  újraépít). Az igazi fix: az előző és új cut DIFF-jét számolva CSAK a
  hozzáadott csempékre emittálni geometriát, a többit változatlanul hagyni, és
  a `BuildCut`-ot worker szálon futtatni. Ez viszi a per-frame költséget
  O(cut)-ról O(változás)-ra — a `screen-space-lod` cél („ne akadozzon") ezen áll.
- **Fázis 4** — a fél-ív `MinUsefulCosGrazing` kemény levágás lazítása valódi
  horizont/hátlap-cull + büdzsé-rangsorra (a horizont-felőli realizmusért). **Nyitott.**
- **Fázis 5** — geomorph/skirt a popping és a dinamikus↔statikus LOD-ugrás
  (mérve: a grazing-frontnál akár 4 szint, DE lyuk nélkül — a statikus base
  mindig fed) simítására. Részben van már (`geomorphRangeFraction`). **Nyitott.**

**Miért NEM hidaljuk át a dinamikus↔statikus varratot a balance-ban.** A
`BuildStaticBaseLayer` a gömb MINDEN pontját mindig lefedi a level-8 statikus
meshsel; a dinamikus finomítás csak FÖLÉ rajzolódik. Ezért a határnál SOHA nincs
lyuk/rés — csak esetleges vizuális LOD-ugrás. Egy korábbi kísérlet (a statikus
ős promótálása a dinamikus rétegbe) átfedést okozott (mind a 4 gyereket
hozzáadta, akkor is, ha némelyik gyerek-régió már finomítva volt), ezért
elvetettük. A dinamikus↔dinamikus 2:1 balance megmarad (varratmentes mesh); a
statikus-varrat simítása Fázis 5.

**Verifikáció (kiválasztási szint).** Standalone harness (`dotnet`, Unity
nélkül — az `AdaptiveQuadTree` tiszta, `noEngineReferences`): régi mód
változatlan (base-8 pontosan 393 216, tiny-cap runaway megfogva, 6.29M
degenerált previousCut-ból visszaáll); új mód: távolról üres cut, közelről
`level>base`-re finomodik (base+3..5 mélységig), determinisztikus, korlátos
(~16k a ~400k+ helyett), nincs ős-leszármazott átfedés, dinamikus↔dinamikus
varrat ≤1. EditMode NUnit-tesztek hozzáadva ugyanerre (`Phase1_*`).

> **ELINTÉZENDŐ (a retrospektív fő tanulsága, kötelező):** egy LOD-változás
> NEM nyilvánítható „késznek" pusztán a kiválasztási teszt alapján — a
> megnövekedett/megváltozott tile-halmaz RENDERELÉSI (frame-) költségét CSAK
> élő Unity Editor-mérés adja meg. **Fázis 1-et élő Editorban meg kell mérni**
> (BuildCut + RebuildAdaptiveMesh + frame-idő, zoom és rotáció közben),
> mielőtt bármelyik további fázis (2–5) indul.

**Hatás/prioritás:** teljesítmény + vizuális minőség; **NEM seed-törő**, a
`src/WorldGen.Core`-t nem érinti (tisztán `unity/WorldGenViewer` renderelési
döntés, I1–I4 érintetlen). A Fázis 1 mellékesen JAVÍTJA a korábbi rejtett
nemdeterminizmust (a megosztott számláló szál-ütemezéstől függő cutját).

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/AdaptiveQuadTree.cs`
(`SelectCut`/`Visit`/`EmitLeaf`, `traversalRootLevel`/`staticBaseLevel`,
kétszámlálós korlát, kiterjedés-tudatos horizont-cull),
`unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`
(`adaptiveTraversalRootLevel` mező, `RecomputeCutAndRebuildAdaptiveMesh`),
`unity/WorldGenViewer/Assets/Tests/EditMode/Lod/AdaptiveQuadTreeTests.cs` (`Phase1_*`).

### ND-48 — Dinamikus mesh chunking: inkrementális (diff-alapú) mesh-frissítés a teljes újraépítés helyett

**Kérdés (felhasználói kérés, 2026-09-05): "a tile-ok még mindig nagyon
nagyok és lassan töltődnek be… ez lenne a legnagyobb feladat, amit meg
kellene ugrani."** Az ND-47 Fázis 1-3 (bejárás leválasztása, prioritásos
költségvetés, aszinkron worker) után a rendszer már nagyon agresszíven
hangolt (`targetTilePixelSize=12`, `adaptiveRenderBudget=120000`,
`adaptiveMaxLevel=18`), mégis a felhasználó továbbra is nagy, blokkos
tile-okat lát. A gyökérok: **minden egyes cut-változáskor a TELJES
dinamikus mesh (`DynamicRefined` GameObject) újraépül és feltöltődik**
(`Mesh.SetVertices`/`SetTriangles`), akkor is, ha a cutnak csak töredéke
változott a kamera kis elmozdulása miatt. A mesh-feltöltés Unity API,
tehát ELKERÜLHETETLENÜL a fő szálon fut, és költsége a TELJES feltöltött
csúcspont-számmal arányos — ez az igazi plafon, ami korlátozza, mennyi
tile fér bele egy frame-be `adaptiveRenderBudget` további emelése nélkül
(amit viszont a fő-szál-akadás korlátoz). Az ND-47 Fázis 3 dokumentációja
saját maga is ezt nevezte meg "az igazi fix"-ként, de sosem valósult meg.

**Kizárt alternatíva: `useGpuGeometry` bekapcsolása.** Van egy meglévő GPU
compute pipeline (`GpuTerrainGeometryGenerator.cs`), de a doksija explicit
leszögezi: "SZÁNDÉKOSAN NEM zéró-másolásos (nincs DrawProceduralIndirect) -
az eredmény visszaolvasódik a CPU-ra". A visszaolvasás
(`ComputeBuffer.GetData`) SZINKRON, fő szálon fut, és a `useGpuGeometry`
be is kapcsolva KIKAPCSOLJA az aszinkron worker-utat (ld.
`useAsyncMeshRebuild && !useGpuGeometry` feltétel) — tehát ez minden
frame-ben egy GPU-pipeline-stallt vezetne be a fő szálra, valószínűleg
ROSSZABB, nem jobb. Nem ajánlott megoldás.

**Döntés: chunkolt, diff-alapú inkrementális frissítés.** A dinamikus
cutot egy rögzített `dynamicChunkLevel` szintű ős szerint "chunk"-okra
bontjuk (`DynamicMeshChunking.GroupByChunk`, motorfüggetlen, `dotnet
test`-tel tesztelt - `tests/WorldGen.Viewer.LodChunking.Tests`). Minden
chunk a SAJÁT Unity GameObject/Mesh-ét kapja
(`GetOrCreateChunkRenderTarget`, `_dynamicChunkGameObjects` explicit
térkép). Két egymást követő keret chunk-csoportosítását összehasonlítva
(`DynamicMeshChunking.DiffChunks`, HashSet-egyenlőség a levélhalmazokon)
CSAK a ténylegesen változott (új vagy más levélhalmazú) chunk-ok kapnak
új, konkatenált geometriát és feltöltést — a változatlan chunk-ok
GameObject-jéhez a fő szál HOZZÁ SEM NYÚL. A már nem használt chunk-ok
deaktiválódnak (nem törlődnek - ha a kamera visszatér, a GameObject/Mesh
újra bekapcsolható, nincs újraallokáció).

**Hatókör (tudatosan szűkítve, mint minden korábbi M9/ND-lépésnél):** EZ A
LÉPÉS CSAK a fő terep-mesh-et chunkolja. A vízfelszín (`DynamicWater`) és a
határvonalak (`DynamicBorders`) TOVÁBBRA IS globálisan, minden
cut-változáskor teljesen újraépülnek (a hívó `EmitAdaptiveTile`-t minden
levélre meghívja, a nem-változott chunk-ok terep-kimenete egy eldobandó
"scratch" bucketbe kerül, hogy a víz/határ-adatuk ennek ellenére
elkészüljön). Ez dokumentált, nem hallgatólagos egyszerűsítés - ha a
víz/határ réteg is szűk keresztmetszetnek bizonyul (élő méréssel
igazolandó), külön lépésben chunkolható ugyanezzel a mintával.

**Biztonsági háló - kapcsoló + teljes-törlés új világnál.** `useChunkedDynamicMesh`
(alapértelmezett: be) - ha regressziót okoz, kikapcsolható, és a rendszer
visszaesik a régi, teljes-újraépítéses viselkedésre (a kód mindkét utat
megtartja). `InvalidateAdaptiveCaches()` (minden `Build()`-nél, azaz
világ-paraméter-változáskor fut) mostantól `ClearAllDynamicChunks()`-t is
hív - enélkül egy ÚJ világ (más seed/deepTimeMyr) chunk-jai a RÉGI
világ geometriáját mutatnák tovább minden olyan chunk-nál, amit az új cut
inkrementális diffje épp nem érintene.

**Verifikáció.** A chunk-csoportosítás/diff logika (`DynamicMeshChunking`)
motorfüggetlen (nulla `UnityEngine`-referencia, mint az `AdaptiveQuadTree`),
ezért `dotnet test`-tel közvetlenül tesztelhető Unity nélkül - 14 unit
teszt (`ChunkRootOf` határesetek, csoportosítás helyessége, diff minden
kombinációja: változatlan/új/megváltozott/törölt chunk, tisztaság). A
tényleges Unity mesh-feltöltési út (`ApplyAdaptiveMeshBuffers`,
`GetOrCreateChunkRenderTarget`) Unity-API-t használ, ezért ide NEM
alkalmazható a `dotnet test` - a szokásos módon, ÉLŐ Unity Editor
mérés/vizuális ellenőrzés szükséges (ld. a retrospektíva fő tanulsága:
"egy LOD-változás NEM nyilvánítható késznek pusztán a kiválasztási teszt
alapján"). **Ez MÉG NINCS élő Unity Editorban megerősítve.**

**Hatás/prioritás:** teljesítmény + vizuális minőség; NEM seed-törő (a
`src/WorldGen.Core`-t nem érinti). Ha élesben beválik, várhatóan
lehetővé teszi az `adaptiveRenderBudget` további emelését (jelenleg a
fő-szál-feltöltés a kötő korlát) anélkül, hogy minden kameramozgás
akadna - ez közvetlenül oldja a felhasználó által jelzett "nagy, pixeles
tile" panaszt.

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/DynamicMeshChunking.cs`
(új), `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`
(`useChunkedDynamicMesh`, `dynamicChunkLevel`, `ComputeAdaptiveMeshBuffersCpu`,
`ApplyAdaptiveMeshBuffers`, `GetOrCreateChunkRenderTarget`,
`ClearAllDynamicChunks`), `tests/WorldGen.Viewer.LodChunking.Tests/` (új).

### ND-49 — Dendritikus folyó-nyomvonal: forrás + finom-szintű lejtő-követés lokális pit-escape-pel, a globális felbontás-emelés helyett

**Kérdés (felhasználói kérés, 2026-09-05, MAGAS prioritás).** A meglévő
hidrológia (`FlowNetwork`, M7) a REFERENCIA szinten (level 6-8) számol
priority-flood-ot és flow-accumulation-t - ezen a felbontáson (tile ~100-800
km) egy vízgyűjtő-területnek túl kevés tile jut ahhoz, hogy a kirajzolt
folyók valódi, elágazó mellékfolyó-mintázatot mutassanak: rövidek (mért
átlag: 5.9 tile), alig ágaznak. A felhasználó két megoldást vetett fel: (A)
lokális, csak a folyó-sáv mentén futtatott magas-LOD újraértékelés, vagy (B)
a globális `hydrologyLevel` emelése. B elvetve: level 10+-nál 6·4¹⁰ ≈ 6.3M
tile-ra nőne a mező, és a SZEKVENCIÁLIS priority-flood (`FlowNetwork.
PriorityFlood`, `SortedSet`-alapú) belátha­tatlanul lassulna ezen a méreten.
**A választás: A (lokális).**

**Módszer.** A REFERENCIA szinten (level 6) csak a folyó-FORRÁSOKAT
választjuk ki: csapadékos ÉS hegyvidéki tile-ok (`RiverPathTracing.
SelectRiverSources` - a meglévő `MoisturePrecipitation`-ból kapott
csapadék-mező felső percentilise ÉS egy tengerszint feletti magasság-küszöb
EGYSZERRE, top-K determinisztikus kiválasztással, döntetlennél
`TileId.Value` szerint). Minden forrásból EGYENKÉNT, egy SOKKAL FINOMABB
szinten (`fineLevel = level + fineDepth`, alapértelmezetten level 6+4=10)
lejtő-menti (steepest descent) lépésekkel követjük az utat az óceánig.

**A felfedezett probléma és a megoldás: lokális pit-escape.** Az első
(naiv, tisztán mohó lejtő-követéses) próba KATASZTROFÁLIS eredményt adott:
a finom szintű fraktál-zaj miatt a legtöbb forrás egy apró, zaj-méretű helyi
mélyedésbe akadt már néhány lépés után (mért: 8/12 forrás azonnal "pit",
átlagos hossz 5.9 tile - NEM lett jobb, mint az eredeti probléma!). Ez
PONTOSAN az az ok, ami miatt az eredeti hidrológia priority-flood-ot
használ naiv lejtő-követés helyett. **Megoldás:** minden "pit"-nél egy
LOKÁLIS, korlátozott csomópont-számú (`escapeNodeBudget`, alapértelmezetten
400) priority-flood (`RiverPathTracing.FindLocalSpillway`) keresi meg a
legközelebbi túlcsordulási pontot - UGYANAZ az elv, mint `FlowNetwork.
PriorityFlood`-ban, csak IGÉNY SZERINT, egy kis környékre futtatva, nem
előre az egész bolygóra. Ezzel a javítással: átlagos hossz 5.9 → 23.7 tile,
ocean-elérés 4/12 → 9/12, ÉS megjelent az első valódi összefolyás (merge)
is. Ez konkrét, mért bizonyíték arra, hogy a "docs-first, referencia-előbb"
munkarend miért éri meg: a Python-referencia futtatása FELTÁRTA a hibát,
MIELŐTT a C#-portba (vagy rosszabb esetben élő Unity-tesztbe) került volna.

**Hurokvédelem.** A pit-escape kereséssel a nyers eleváció NEM feltétlenül
csökken szigorúan MINDEN egyes lépésnél (egy meder rövid szakaszon át egy
alacsonyabb nyeregponton kelhet át, mielőtt tovább esne - mint a valódi
folyóknál) - ez elsőre egy REGRESSZIÓT is okozott (egy útvonal a saját,
korábban bejárt medrébe futott vissza, hurkot képezve). A javítás: a teljes
útvonal (fő ág + minden escape-kitérő) egy `pathVisited` halmazzal védett -
a lokális kereső SEM terjeszkedik, SEM fogad el túlcsordulási pontként már
bejárt tile-t. A strukturális garancia, amit ténylegesen ellenőrzünk (a
vektor-teszt): nincs ismétlődő tile, ÉS a végpont alacsonyabban van, mint a
kezdőpont (nettó esés) - NEM a szigorú lépésenkénti monotonitás.

**Dendritikus elágazás.** Ha egy KÉSŐBBI forrás útja egy MÁR MEGLÁTOGATOTT
(korábbi forrás által "lefoglalt", `claimed` térkép) finom tile-ba fut, ott
MEGÁLL ("merged") - a két folyó onnantól ugyanazt a meder-szakaszt
"használja" (a viewer két külön vonalszakaszként rajzolja, ami közös
végponton találkozik).

**Verifikáció.** `tools/reference/river_path_ref.py` (Python-referencia,
12 tesztvektor: forrás + útvonal + végződés-ok). A C# port
(`RiverPathTracing`, `tests/WorldGen.Core.Tests/Hydrology/
RiverPathTracingTests.cs`) BITRE EGZAKT egyezést ad a nyomvonal-követésre
(az eleváció-lánc már bizonyítottan bit-egzakt cross-platform, ld. ND-24/
ND-36) - a tesztek a Python-vektor RÖGZÍTETT forrás-listáját használják
(`BuildRiverNetworkFromSources`), FÜGGETLENÜL a csapadék-modell (nyers
Math.Sin/Cos, NEM garantáltan bit-egzakt) tolerancia-kockázatától; a
forrás-kiválasztás logikáját (`SelectRiverSources`) külön, szintetikus
adatos egység-tesztek fedik. 341/341 Core-teszt zöld.

**Hatókör (tudatosan szűkítve):** a forrás-kiválasztás a `MoisturePrecipitation`
SAJÁT (t=0, klíma-közelítés) elevációját/tengerszintjét használja, NEM a
deep-time-mozgatott domborzatot (mint a csapadék-overlay is, ld. M5
dokumentáció) - a TÉNYLEGES nyomvonal-követés viszont a JELENLEGI
(`_adaptiveSeeds`) lemez-pozíciókat kapja, hogy a megjelenített
domborzattal konzisztens legyen. A folyó-vonal réteg a régi, referencia-
szintű vízgyűjtő-fa-alapú rajzolást (`_adaptiveRiverTiles`/
`_adaptiveRiverParent`) VÁLTOTTA FEL a vizuális megjelenítésben; ezek a
mezők továbbra is számolódnak (a jövőbeli M8 panel-metrikákhoz még
hasznosak lehetnek), de a vonal-rajzolás már nem őket használja.

**Verziózás:** nem seed-törő (tisztán megjelenítési/kiválasztási döntés, a
`src/WorldGen.Core`-t motorfüggetlenül bővíti, I1-I4 érintetlen).

**Releváns fájlok:** `tools/reference/river_path_ref.py` (új),
`src/WorldGen.Core/Hydrology/RiverPathTracing.cs` (új),
`tests/WorldGen.Core.Tests/Hydrology/RiverPathTracingTests.cs` (új),
`unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`
(`_adaptiveDendriticRivers`, `BuildRiverNetwork`, a csapadék-blokk Build()-ben).

**Kiegészítés (2026-09-06, felhasználói KÖTELEZŐ elvárás): a tile-középpontok
összekötése helyett folytonos, kb. 10 m-es pontosságú nyomvonal.** A
felhasználó jelezte, hogy a ~13 km-es finom tile-középpontok Catmull-Rom
simítással összekötött vonala "rossz irányba" megy - a folyónak a tile-on
BELÜL, a kalkuláció szerinti pontos helyen kellene futnia, ~10 m-es
pontossággal.

*Módszer:* a durva (tile-láncolt) útvonal MEGMARAD a topológia (irány,
összefolyás, pit-escape) eldöntésére, de a MEGJELENÍTÉSHEZ minden egymást
követő durva tile-pár között egy FOLYTONOS, a felszín érintő-síkjában futó
lejtő-követő sétát végez (`RiverPathTracing.RefinePathContinuous`) - a MÁR
bit-egzakt pontszerű elevációfüggvényt (`ElevationAtPosition`, `tileIdValue`
paraméter NEM használt a jelenlegi képletben - a modell LOD-/rács-független)
tetszőleges, nem tile-rácshoz kötött pontban kiértékelve. Minden lépésnél N
jelölt irányt próbál az érintő-síkban (egyenletes szögosztás), a legjobbat
választva; helyi zaj-pöttyöknél (ahol egyik jelölt sem jobb) a durva célpont
felé lép tovább, hogy ne ragadjon be.

*Miért NEM egyszerűen a finom szint mélyítése:* a `FindLocalSpillway`
pit-escape keresés csomópont-költségvetése egy FIX FIZIKAI keresési
sugárhoz van hangolva a jelenlegi (~13 km-es) rácson - egy ~1300×-os
rácsfinomítás (13 km → 10 m) a keresési csomópontszámot a felülettel
arányosan, ~1300²-szeresére növelné ugyanahhoz a fizikai kereséshez,
használhatatlanná téve azt.

*Mért teljesítmény-korlát (kötelező mérés, nem becslés, ld. munkamódszer):*
12 folyóra, párhuzamosítva - 100m: ~1.9s, 50m: ~3.5s, 25m: **7.4s**, 10m
(a szigorú kért érték): **16.6s**. Ez a durva nyomvonal-követés (~125ms)
töredéke, DE összemérhető azzal a Build()-idővel, amit a session korábbi
részében (ND-48 előtti perf-munka) 60s-ról 15s-re vittünk le - egy
literális 10m-es szinkron lépés komoly regressziót jelentene minden
deep-time-váltásnál.

*Döntés (felhasználói választás a mért adatok alapján):* **HÁTTÉR-SZÁLON
(Task.Run) futó számítás, 50 méteres alapértelmezett lépésközzel.** A
számítás NEM blokkolja a Build()-et/a fő szálat - amíg fut, a régi (durva,
Catmull-Rom-simított) vonal látszik átmenetileg, majd a `LateUpdate()`-ben
(`TryApplyCompletedRiverRefinement`) a fő szál átveszi az eredményt és
újraépíti a folyó-mesh-t. Generáció-számlálóval védett (ha egy ÚJABB
Build() fut le, mire egy KORÁBBI finomítás befejeződik, az elavult
eredményt eldobjuk). A lépésköz (`riverRefinementStepMeters`) Inspector-
állítható, ha a felhasználó szigorúbb (pl. 10m) vagy lazább pontosságot
akar a sebesség rovására/javára.

*Kivétel a "referencia-előbb" munkarend alól (dokumentált, nem
hallgatólagos):* a `RefinePathContinuous` NEM kapott Python-referenciát -
új fizikai képletet NEM vezet be (a MÁR bit-egzakt pontszerű
elevációfüggvényt hívja), csak egy geometriai/algoritmikus (gyűrű-keresés
az érintő-síkban) lépést ad hozzá. Egy Python-oraklum futtatása (a mért
~7ms/pontszerű-kiértékelés Pythonban, több százezer kiértékeléssel) órákig
tartana. Helyette közvetlen, geometriai invarianciákat ellenőrző C#-tesztek
verifikálják (7 teszt: tisztaság, gömbön-maradás, végpontok pontossága,
lépésköz-monotonitás, éles esetek, végpontig-futás egy valódi folyón).

**Második kiegészítés (2026-09-06, ugyanazon a napon, felhasználói
visszajelzés utáni javítás): a fenti "hop-anchored" változat MÉG MINDIG
"tile-középpontok összekötésének" hatott.** A felhasználó a commitolt
verziót megnézve jelezte: *"nem jó ez a durva megjelenés... ne függjön
tile-októl... másképp nem elfogadható"*. Két, EGYMÁSTÓL FÜGGETLEN, méréssel
feltárt hiba állt emögött:

1. *Architekturális:* az első változat a durva tile-középpontokat KÖZBENSŐ
   KÉNYSZER-útpontként kezelte (minden hop végén explicit ráugrott a
   következő tile középpontjára) - ez, ha a finom keresés nem talált
   javulást, gyakorlatilag a régi, kifogásolt egyenes-szakaszos rajzolatot
   adta vissza, csak sűrűbb bontásban. **Javítás:** a köztes tile-pontok
   MOSTANTÓL EGYÁLTALÁN NEM szerepelnek útpontként - a séta kizárólag a
   forrás- és a torkolat-/nyelő-pozíció között, folytonos lejtő-követéssel
   halad.
2. *Numerikus (mérve, nem elméletben):* a folytonos séta bevezetése után
   egy diagnosztikai méréssel kiderült, hogy a lépésenkénti irányváltás
   ÁTLAGA ~170° volt - a séta gyakorlatilag lépésről lépésre oda-vissza
   lengett, nem sima kanyart rajzolt. Ok: egy fix (`stepMeters`) sugarú,
   "emlékezet nélküli" lejtő-keresés egy a lépésköznél szélesebb völgy
   aljánál a völgy egyik oldaláról a másikra pattog. **Javítás, két rész:**
   (a) az IRÁNY-érzékelés sugarát (`DefaultDirectionSensingRadiusMeters`,
   800 m) LEVÁLASZTOTTUK a tényleges lépésköztől (`stepMeters`, 50 m) - a
   lejtő irányát egy nagyobb, a finom zajt átlagoló körben keressük, a
   pozíció pontossága viszont változatlanul `stepMeters` marad; (b)
   IRÁNY-PERZISZTENCIA - a jelölt-irányok köréből kizárjuk azokat, amik
   `MaxHeadingTurnRadians`-nál (100°) jobban eltérnek az előző lépés
   irányától.

*Újabb mérési csapda (ugyanitt feltárva):* a javított verzió egyik közbenső
állapota a jelölt-irányokat a JELENLEGI pont elevációjához hasonlította
("csak akkor lépek, ha szigorúan jobb, mint ahol állok") - egy völgy
ALJÁN, ahol a jelenlegi pont már maga is keresztirányú helyi minimum, ez
majdnem MINDEN lépésnél "nincs javulás"-t jelzett, ami minden második
lépésnél elindította a drága, 64×-ig növekvő sugarú kiútkeresést: a teljes
12 folyós hálózatra a finomítás 3.5 s-ról **42 s-ra** nőtt. A végleges
megoldás a jelölt-irányokat EGYMÁSHOZ hasonlítja (a kúpon belüli
legalacsonyabb nyer, nem a jelenlegi ponthoz képest), a drága kiútkeresést
pedig csak akkor hívja, ha a végponttól vett távolság `StallWindowSteps`
(24) lépésen át NEM csökkent - ez a teljes hálózatra **7.3 s**-ra hozta
vissza a (párhuzamosított) számítási időt, a háttér-szálas
architektúra változatlanul hagyása mellett.

*Utólagos mérés (a végleges algoritmuson, a session korábbi 51 tile-os
referencia-folyóján):* lépésenkénti átlagos irányváltás 170° → **~40°**
(sima, folyamatos kanyar), a legnagyobb, egyenes vonaltól való oldalirányú
eltérés (kanyargás mértéke) ~13 km egy ~176 km hosszú folyón - fizikailag
plauzibilis meander, nem műtermék. Regresszió ellen új teszt:
`RefinePathContinuousTests.DoesNotOscillateBackAndForth` (átlagos
irányváltás < 90°-ra zárva).

**Harmadik kiegészítés (2026-09-06, ugyanazon a napon, MÉLYEBB diagnózis a
felhasználói kérésre: "a folyó nyomvonal minőségi javítása még nincs
befejezve, nézd meg alaposan").** A #2 pontban leírt, akkor "lezártnak"
hitt verziót egy dedikált C# diagnosztikai harness-szel (nem feltételezéssel)
lemérve a MÁSODIK verzió is súlyosan hibásnak bizonyult - de egy MÁS okból,
mint amit az irányváltás-átlag teszt (`DoesNotOscillateBackAndForth`) mérni
tudott.

*A mért tény:* a 12 referencia-folyóból az elsőn (51 finom tile, durva
lánc-hossz 551 km, forrás-torkolat légvonal 205 km) a finomítás **1804 km**
utat járt be - 3,3×-a a durva láncnak. Minden mért paraméter-kombinációnál
(50m/25m lépésköz, 800m/3200m/6400m érzékelési sugár) a séta **KIMERÍTETTE
a `maxSteps` korlátot**, és az utolsó lépés egy **~150 km-es mesterséges
"safety net" ugrás** volt a torkolatra (`if (AngularDistance(current, end) >
1e-12) refined.Add(end);`) - a séta SOHA nem érte el a végpontot
természetes úton. Az érzékelési sugár 8×-os növelése (800m→6400m) a
teljes úthosszat GYAKORLATILAG NEM változtatta (1804.18 → 1804.45 km) -
ez zárta ki, hogy finomhangolási kérdés lenne: az algoritmus STRUKTURÁLISAN
soha nem törekedett a végpont felé, kizárólag egy irány-kúpon belüli lokális
gradienskövetést végzett, ami egy nagy, enyhén zajos terepszakaszon
céltalanul bolyongott.

*Felhasználói válasz a diagnózisra - ÚJ tervezési elv:* ne legyen befagyasztott
lépésszám-korlát; a folyó kövesse a lejtőt, amíg TÉNYLEGESEN el nem ér egy
állóvízhez (óceánhoz vagy egy zárt medencéhez/tóhoz) - ha egy magasföldi tóba
fut, onnan (miután "megtelt") tovább kell folynia, amíg végül tengerhez ér.
Ez PONTOSAN a már meglévő, validált durva `TraceRiverPath`/`FindLocalSpillway`
elve (lejtő-követés + lokális priority-flood, ha elakad) - a korábbi hibás
verzió ezt az elvet ELVETETTE egy előre ismert végpont felé navigáló,
irány-perzisztens heurisztika javára.

*Új algoritmus (`TraceRiverPathContinuous` + `FindContinuousLocalSpillway`,
felváltja a törölt `RefinePathContinuous`-t):* nincs előre megadott végpont -
a séta a FORRÁSBÓL indul, és maga dönti el a természetes véget
(Ocean/Pit/Merged), pontosan a durva algoritmus mintáját követve:
1. **Normál lépés:** csak akkor lép, ha a jelölt-körben (nagyobb, a zajt
   átlagoló érzékelési sugárban) van a JELENLEGI pontnál SZIGORÚAN
   alacsonyabb pont - ez a döntő eltérés a korábbi ("körön belüli relatív
   legjobb", ami felfelé is léphetett) verzióhoz képest.
2. **Escape:** ha nincs alacsonyabb jelölt, egy VALÓDI lokális priority-flood
   (`FindContinuousLocalSpillway`) keresi meg a tényleges túlcsordulási
   pontot egy, a pit pontban rögzített érintő-síkbeli (i,j) egészrácson -
   ugyanaz az elv, mint `FindLocalSpillway`-ben, csak folytonos térben.

*Kalibrációs mérés (mért, nem becsült):* az escape-rács cellamérete
KRITIKUS - 50m-es cellával (a kirajzolási lépésközzel megegyezővel) egy
30 000-es csomópont-költségvetés csak ~8,7 km sugarú területet fed le,
ami 2/3 tesztelt forrást tévesen "Pit"-nek minősített, amit egy nagyobb
budget-tel (200 000, de ~9-12s/folyó) helyesen "Ocean"-ként zárt le. A
végleges megoldás: az escape-rács cellamérete `escapeCellMeters=2000m`
(16× nagyobb terület ugyanazzal a csomópont-számmal, mint 500m-nél) - ezzel
mind a 12 referencia-forrás helyesen, TERMÉSZETES úton ért véget (Ocean vagy
Pit, SOHA MaxSteps), összesen **~6,4 másodperc** alatt (szekvenciálisan,
mert a megosztott `claimed` térkép miatt a folyók nem párhuzamosíthatók,
ahogy a durva rétegnél sem). Az escape-szakaszok ritkák (folyónként 0-6
alkalom) és rövidek a teljes úthoz képest, ezért a nagyobb cellaméretük nem
áll össze látványos "durva" hatássá - a normál lépések (a folyó túlnyomó
része) változatlanul `stepMeters` (50m) pontosságúak.

*Tudatosan vállalt következmény:* mivel a folytonos elevációfüggvény
LOD-független, a finomabb (folytonos) keresés ELTÉRHET a durva (13 km-es
tile-átlagolt) döntéstől - a mért referencia-hálózatból két forrás, amit a
durva `TraceRiverPath` "Pit"-nek minősített (mert a 13 km-es tile-ok
elfedtek egy keskeny, ténylegesen lejtő hágót/nyerget), a folytonos verzió
helyesen "Ocean"-ként zárta le. Ez NEM hiba, hanem a már dokumentált
LOD-független modell következetes alkalmazása finomabb felbontáson - a
kontinuus réteg ÁTVESZI a topológiai döntés szerepét a MEGJELENÍTETT
hálózatra nézve; a durva réteg továbbra is a forrás-kiválasztáshoz és a
`claimed`-alapú dendritikus összefolyáshoz kell (amit a folytonos réteg is
megtart, saját, finom-tile alapú `claimed` térképpel).

*Verifikáció:* `TraceRiverPathContinuousTests` (a törölt
`RefinePathContinuousTests` helyén) - determinizmus, gömbön-maradás, forrás-
pozíció pontossága, ÉS a legfontosabb regresszió-védő teszt:
`ReachesNaturalTerminationNotMaxSteps` mind a 12 referencia-forrásra
(`[Theory]`), ami explicit ellenőrzi, hogy a termination SOHA nem
`MaxSteps` - ez a teszt a korábbi (hibás) implementációval MEGBUKOTT volna.
359/359 Core-teszt zöld.

*Verziózás:* nem seed-törő (tisztán megjelenítési döntés, ld. #1
kiegészítés indoklása - a `src/WorldGen.Core`-t motorfüggetlenül bővíti).

**Vizuális ellenőrzés Unityben MÉG MINDIG hátra** - ez a HARMADIK iteráció,
mérési bizonyítékkal alátámasztva, de élő Unity-nézetben még nem látott.

**HATODIK KIEGÉSZÍTÉS (2026-09-06, önálló munkamenetben, felhasználói
"a folyók már egészen jók" visszajelzés utáni két finomítási megjegyzés
egyikének diagnózisa)**: a backlogban rögzített "az útvonal a felületen
HELYENKÉNT MEGSZAKADNAK tűnik" megjegyzés gyökéroka megtalálva és
javítva. A `BuildContinuousRiverNetworkFromSources` a dendritikus
összefolyást egy `claimed: Dictionary<TileId, int>` térképpel oldja meg
(finom-tile → melyik folyó járt már ott) - amikor egy KÉSŐBBI folyó egy
MÁR CLAIMED finom-tile-ba lép, "Merged"-ként megáll. A PROBLÉMA: a
megálló folyó UTOLSÓ pontja csak "valahol EBBEN a finom-tile-ban" volt
(a saját mintavételi lépése alapján), NEM a befogadó folyó TÉNYLEGES
(folytonos térbeli) pontján - egy finom-tile mérete akár több száz méter
is lehet, tehát a két vonal a találkozásnál akár ennyivel is elválhatott
egymástól, vizuálisan "megszakadó útvonalnak" látszva. Ez PONTOSAN
egyezik az osztály-doksi EREDETI tervezési szándékával ("a hívó ezt egy
KÖZÖS PONTBAN végződő két vonalszakaszként rajzolja") - tehát valódi
regresszió/hiányosság volt, nem szándékos egyszerűsítés.

*Javítás:* a `claimed` térkép ÉRTÉKE egy új `ClaimedTileInfo` struct
(folyó-index + a TÉNYLEGES pozíció, amivel a lefoglaló folyó áthaladt
azon a tile-on) - amikor egy folyó "Merged"-ként megáll, az UTOLSÓ
pontja előtt hozzáadja ezt a TÁROLT, PONTOS pozíciót is a saját
útvonalához, mielőtt visszatérne. Ez a két vonal metszéspontját
PONTOSAN (nem csak "ugyanabban a durva tile-ban") közössé teszi - a
`ClaimedTileEndsAsMerged` teszt egy ÚJ, explicit egzakt-egyezés
assertion-nel ezt közvetlenül ellenőrzi (1e-12 toleranciával). A
`TraceRiverPath`/`BuildRiverNetworkFromSources` (a DISZKRÉT, `TileId`-
alapú, korábbi/durva algoritmus) NEM érintett - ott a "claimed" tile
MAGA a megosztott pont, nincs folytonos-térbeli rés.

*Hatókör:* csak a `RiverPathTracing.cs` (Core) belső logikája
változott, a Viewer (`PlanetGridMesh.BuildRiverNetwork`) hívási módja
VÁLTOZATLAN (a `claimed` map a `BuildContinuousRiverNetworkFromSources`-
on BELÜL, nem a hívó oldalán épül). 363/363 Core-teszt zöld (a bővített
`ClaimedTileEndsAsMerged` is, ami korábban NEM ellenőrizte az egzakt
egyezést - csak a bővített assertion bizonyítja, hogy a javítás
ténylegesen működik, nem csak "nem tört el semmit").

*Verziózás:* nem seed-törő (a folyó-VÉGPONTOK pozíciója marginálisan
változik az összefolyási pontoknál - ez tisztán vizuális pontosítás,
nem a világmodell állapotát érinti; a `ClaimedTileInfo` egy ÚJ,
publikus típus, nem számoz át semmit).

**Élő Unity-ellenőrzés hátra** - a MÁSIK nyitott finomítási megjegyzés
(a folyó-vonal szélessége nem korrelál a vízhozammal) TOVÁBBRA IS
nyitott, külön (nagyobb, mesh-alapú "szalag"-rajzolást igénylő) munka.

### ND-50 — Build()/renderelés szétválasztás: csak a felhő és a folyó-vonal kapott kivételt, a szél-/csapadék-overlay még nem (LEZÁRVA: (A))

**Kérdés (önálló code-review-ban feltárt hiányosság, 2026-09-06).** A
felhasználói visszajelzés nyomán bevezetett `ApplyCloudOnlyRebuild`/
`CloudConfigChangedSinceBuild` (ld. fenti #4 kiegészítés az ND-49-hez,
illetve ugyanez a minta a `riverLineRadialBias`-ra is kiterjesztve) csak
KÉT tisztán vizuális paraméter-csoportot választott le a teljes
`WorldConfigChangedSinceBuild()`-ről. Egy alapos (8 szempontú, majd
verifikált) code-review rámutatott: a `windSpeedOverlay`,
`precipitationOverlay`, `windSpeedColorMaxMs`, `precipitationColorMax`
mezők TOVÁBBRA IS a teljes `WorldConfigChangedSinceBuild()`-en mennek át
- ha valaki ezeket kapcsolgatja/hangolja, MÉG MINDIG teljes `Build()` fut,
ami eldobja a háttérszálon futó, több másodperces folyó-finomítást,
UGYANAZZAL a mechanizmussal, amit a felhőre/folyó-vonalra már kijavítottunk.

**Miért nem oldottuk meg ugyanúgy, mint a felhőt/folyó-vonalat.** A felhő
és a folyó-vonal KÜLÖN RÉTEG (saját GameObject/Mesh), ami a MÁR meglévő,
cache-elt adatokból (csapadék-mező, illetve a finomított folyó-pontok)
olcsón újraépíthető a teljes statikus/dinamikus terep-mesh érintése
nélkül. A `windSpeedOverlay`/`precipitationOverlay` viszont NEM külön
réteg - a FŐ TEREP-MESH tile-jainak SZÍNÉT cseréli le (a biome-szín
helyett szél-/csapadék-szín), a színezési logika mélyen beágyazva fut a
statikus alapréteg ÉS a dinamikus (kamera-vezérelt) LOD-mesh
építésébe egyaránt. Egy "csak újraszínezés" gyors útvonal bevezetése
(a fizikai szimuláció - domain warp, lemez-hozzárendelés, eleváció -
újraszámítása NÉLKÜL) egy jelentős, a mesh-építési kódot mélyen érintő
refaktor lenne, amit NEM végeztünk el önállóan (a felhasználó jelenléte
nélkül) egy ilyen kritikus, sokat használt kódútra.

**Javaslat (nem implementálva, döntésre vár):**
- A) Külön "csak-újraszínezés" függvény bevezetése, ami a MÁR kiszámolt
  eleváció-/óceán-/biome-mezőkből újraszínezi a statikus+dinamikus
  mesh-eket a szimuláció újrafuttatása nélkül - a `windSpeedOverlay`/
  `precipitationOverlay`-t is a cloud-mintához hasonló, olcsó ágra
  terelné. Nagyobb munka, de általánosan megoldja a problémát.
- B) Amint az altitude-szempontú review is javasolta: a folyó-finomítási
  generáció-számláló invalidálását ne ahhoz kössük, hogy "fut-e
  `Build()`", hanem közvetlenül ahhoz, hogy a finomítás TÉNYLEGES
  bemenetei (forrás-lista, tengerszint, lépésköz) változtak-e - ekkor
  BÁRMILYEN jövőbeli, pusztán vizuális paraméter automatikusan
  ártalmatlan lenne, hand-maintained snapshot-párok szaporítása nélkül.
- C) Egyelőre hagyjuk így (a szél-/csapadék-overlay ritkán használt
  fejlesztői/diagnosztikai kapcsoló, nem a fő munkafolyamat része) - csak
  dokumentáljuk a korlátozást (ez a jelen bejegyzés).

**Javaslat:** B) a legrobusztusabb (kizárja az egész hibaosztályt jövőre
nézve is), de nagyobb refaktor a `_riverRefinementTask`/generáció-kezelés
körül - felhasználói megerősítést igényel, mielőtt hozzákezdenénk.

**Verziózás:** nem seed-törő (tisztán teljesítmény-/UX-kérdés).

---

**LEZÁRVA (2026-09-20): A) megvalósítva, lényegesen olcsóbban, mint a fenti
becslés.**

A bejegyzés azt feltételezte, hogy A)-hoz "a mesh-építési kódot mélyen érintő
refaktor" kell. Ez **tévedés volt**: a `BuildStaticBaseLayer()` már akkor is
önálló volt, és kizárólag a CACHE-ELT világállapotból dolgozik
(`_adaptiveSeed` / `_adaptiveSeeds` / `_adaptiveCraters` / `_adaptiveSeaLevel`
+ a terrain-bázis cache) — **szimulációt nem futtat**. Elég volt tehát:

1. az öt overlay-mezőt (`windSpeedOverlay`, `precipitationOverlay`,
   `tectonicPlateOverlay`, `windSpeedColorMaxMs`, `precipitationColorMax`)
   kivenni a `WorldConfigChangedSinceBuild()`-ből egy saját
   `OverlayConfigChangedSinceBuild()` / `SnapshotOverlayConfig()` párba —
   ugyanaz a minta, mint a felhőnél és a folyó-vonalnál;
2. egy `ApplyOverlayOnlyRebuild()`, ami üríti a szín-gyorsítótárakat és
   **csak** a statikus alapréteget építi újra, a dinamikus chunkokat pedig az
   `_adaptiveConfigDirty` viszi;
3. a klasszifikáció újrahasznosítása (`BuildStaticBaseLayer(reuseClassifications: true)`):
   a tile-besorolás az elevációból/tengerszintből/biome-ból jön, overlay-től
   független — ez önmagában 453 ms volt.

**Mérve élő Editorban** (level 8 alapszint, 396 294 statikus sarok):

| overlay-váltás | régi (teljes `Build()`) | új (overlay-út) | folyó-generáció |
|---|---|---|---|
| tektonikus / csapadék / overlay ki | 3147–3826 ms | **897–1595 ms** | régen bumpolt, most **változatlan** |
| szél-overlay be (drága színezés) | 5718 ms | **2986 ms** | **változatlan** |

A sebesség a kisebbik nyereség. A lényeg, hogy a folyó- és felhő-generáció
**érintetlen marad**, tehát az overlay kapcsolgatása többé nem dobja el a
háttérszálon futó, többmásodperces folyó-finomítást — ez volt a bejegyzés
eredeti panasza.

**Egy meglévő rés is bezárult.** A `_waterCornerColors` (a vízfelszín
sarok-színei, szintén overlay-függő) eddig CSAK a teljes világ-reset során
ürült. Amíg minden overlay-változás teljes `Build()`-et indított, ez nem
látszott; az olcsó úton látszana. Most az `InvalidateOverlayColorCaches()`
egy helyen üríti az összes overlay-függő szín-cache-t.

**B) TOVÁBBRA IS ÉRDEMES, de már nem sürgős.** A mostani javítás a KONKRÉT
öt mezőt kezeli. Ha valaki a jövőben új, tisztán vizuális paramétert vesz fel
a `WorldConfigChangedSinceBuild()`-be, a hibaosztály visszatér. B) (a
generáció-invalidálást a finomítás TÉNYLEGES bemeneteihez kötni) ezt
szerkezetileg zárná ki.

### ND-51 — Csillagos háttér + látható Nap: determinisztikus, de NEM a világmodell része

**Kérdés (felhasználói kérés, 2026-09-06).** "mi a helyzet azzal hogy a
hatter a csillagos eg legyen" - a backlog már rögzítette ezt a tételt
(M3/M13, Alacsony/Kicsi), egyetlen nyitott kérdéssel: vonatkozik-e az I3
("nincs kézzel festett textúra, minden pixel a világmodellből
következik") a háttér-csillagokra, amik NEM a bolygó része, hanem a
Naprendszeren KÍVÜLI, dekoratív kontextus.

**Válasz (felhasználói döntés):** procedurális, determinisztikus
csillagmező, ÉS emellett explicit két új követelmény: (1) a csillagok a
bolygó tengelyforgása miatt LÁTHATÓAN mozogjanak, (2) legyen egy LÁTHATÓ
Nap-korong is, aminek a szöge az ÉV során (évszakok) változzon.

**Felfedezés implementáció közben: a Nap-mechanika MÁR LÉTEZETT.** A
`SunController.cs` (Directional Light-ra tett komponens) MÁR a
`WorldGen.Core.Astronomy.OrbitalMechanics.SunDirectionBodyFrame`-et
hívta minden képkockán, saját `currentTimeDays`/`autoAdvance`/
`daysPerSecond` idő-akkumulátorral - ez pontosan a hiányzó M3 vizuális
cél ("megvilágított gömb, terminátorral - évszakok látszanak a
terminátor mozgásán") kész, de eddig LÁTHATATLAN (csak a fény irányát
állította) megvalósítása volt. Erre ÉPÍTETTEM, nem újraterveztem.

**Miért NEM a világmodell része, mégis determinisztikus (a `RandomDomain`
bővítése).** A `src/WorldGen.Core/Random/RandomDomain.cs`-ben új,
KÜLÖN tartomány: `RandomDomain.Decorative = 32` (+ `RandomProperty.
StarPosition = 40`, `StarBrightness = 41`) - explicit dokumentálva, hogy
erre NEM vonatkoznak ugyanazok a seed-kompatibilitási garanciák, mint az
1-8 (világmodell-) domainekre, mert a csillagkép nem a szimulált bolygó
állapotának része (nincs mit checkpointolni/verifyelni rá). A
`DeterministicRandom.SampleUnitVector3`/`Sample` MÁR verifikált
primitíveket hívja újra (nincs új hash-függvény) - a hozzáadás pusztán
additív enum-bővítés, NEM számoz át/használ fel meglévő értéket, tehát a
CLAUDE.md "seed-törő változás" kritériuma NEM teljesül (nem kell
verzió-emelés).

**Geometriai/forgatási konvenció (ld. kód-kommentek részletesen,
`StarField.cs`/`SunController.cs`):**
- A projekt konvenciója: a terep-mesh test-keretben FIX, a nap/éj
  ciklust a Directional Light FORGATÁSA szimulálja (nem a mesh forog).
- A csillagok (közelítőleg) az inercia-keretben fixek - hogy a FIX
  terephez képest mégis "végigsöpörjenek az égen" a bolygó
  tengelyforgása miatt, a csillag-mezőnek a nap napi (tengelyforgás-
  eredetű) szögével MEGEGYEZŐ nagyságú, de ELLENTÉTES irányú forgást
  kell kapnia a pólustengely (Unity Y, ld. `BodyFrameConversion`) körül.
  `SunController.ApplySunDirection` ugyanabból a `rotationPeriodDays`/
  `rotationPhase0`/`currentTimeDays`-ból számolja ezt a szöget, amit a
  Nap-irányhoz is használ - a napi ÉS évi (szezonális) mozgás UGYANABBÓL
  az idő-változóból adódik, a helyes (fizikai) arányban, mert mindkettő
  ugyanazon a `OrbitalMechanics.SunDirectionBodyFrame` hívásláncon megy át.
- A csillagok geometriája (kis, gömb-középpontból KIFELÉ néző kvadok,
  nem kamera-követő klasszikus billboard) és a Nap-korong pozicionálása
  (a bolygó-középponttól a Nap-irányba, fix távolságra) EGYSZERŰSÍTÉS -
  a csillagszféra sugara (5000) sok ezerszerese a kamera lehetséges
  elmozdulásának, ezért vizuálisan megkülönböztethetetlen egy valódi
  kamera-billboardtól, de nem igényel per-frame újraszámítást minden
  csillagra.

**Shader:** `WorldGen/StarUnlit` (`StarUnlit.shader`) - a MEGLÉVŐ
`CloudUnlit`/`VertexColorUnlit` "best-effort HDRP CG" mintáját követi
(egyszerű CGPROGRAM, nincs élő Unity-teszttel megerősítve), additív
keveréssel (`Blend One One`), a vertex-szín hordozza a fényességet -
mind a csillagokhoz, MIND a Nap-koronghoz újrahasznosítva (nincs
szükség két külön anyagra egy ilyen egyszerű MVP-hez).

**Scope/korlátok:**
- A `SunController` SAJÁT (a `PlanetGridMesh.climate*` mezőktől
  FÜGGETLEN, ld. a `SunController`-t megelőző eredeti fejléc-kommentet)
  orbitális paramétereit használja - ha a kettő szét van hangolva
  (pl. eltérő `axialTiltDegrees`), a látható Nap/csillagok NEM feltétlenül
  egyeznek a klíma-alapú terminátor-effekttel. Ez MÁR ÍGY volt a
  `SunController` eredeti tervezésében, nem ezzel a munkával vezettük be.
- A scene-be történő tényleges bekötés (a `StarField` GameObject
  létrehozása, a `SunController` új mezőinek - `sunVisual`, `starField`,
  `planetTransform` - Inspector-beli kitöltése) NEM történt meg
  scene-YAML-szerkesztéssel (a kockázata - hibás fileID/GUID - nem érte
  meg egy élőben nem tesztelhető változtatásnál) - ez felhasználói
  Unity-Editor lépés, ld. a session-napló pontos utasítását.

**Verziózás:** nem seed-törő (a `RandomDomain`/`RandomProperty`
bővítés ADDITÍV, nem számoz át semmit; a `src/`-et érintő rész
motorfüggetlen marad).

**Élő Unity-ellenőrzés MÉG NINCS** - ez egy vadonatúj vizuális modul,
build-ellenőrizve (a generált Assembly-CSharp.csproj-hoz manuálisan
hozzáadott `StarField.cs` bejegyzéssel - Unity a saját scene-megnyitásakor
úgyis újragenerálja ezt a fájlt, tehát ez csak ideiglenes, helyi
ellenőrzési segédlet volt), 363/363 Core-teszt zöld.

### ND-52 — Másodlagos, finom-léptékű részlet-zaj a közeli zoom laposságára

**Kérdés (felhasználói kérés, 2026-09-07).** "fraktál zaj generálás
nagyon közeli zoom esetén nem jó. bizonyos tile-okat azért nem bont meg,
mert nincs fraktál zaj ami indokolná ezt" - a felhasználó egy MÁSODIK,
alacsony amplitúdójú zajréteg bevezetését kérte, ami a tile-ok "eredeti
mérete szerint" 40×40 tile-on ismétlődik, ugyanazzal az algoritmussal,
mint az elsődleges dombormlat-zaj.

**Vizsgálat: a tünet oka MÁS, mint a felhasználó feltételezése, de a
kért megoldás helyes.** Az `AdaptiveQuadTree` tile-felbontási döntése
(`Lod/AdaptiveQuadTree.cs`) TISZTÁN képernyő-téri/szögméret-alapú
(`Math.Atan2(rTile, distance)` vs. küszöb) - NEM néz zaj-tartalmat, tehát
"nem bontja meg, mert nincs zaj ami indokolná" szó szerint NEM ez
történik. A VALÓS ok: a `CrustElevation.BaseElevation` elsődleges
`FractalNoise.RidgedMultifractal`-jának (BaseFrequency=8, 5 oktáv,
lacunarity=2) legfinomabb oktávja is még több tucat km hullámhosszú,
miközben a renderelt adaptív LOD ENNÉL sokkal mélyebbre bontja a
geometriát (a legmélyebb szinteken egy tile akár méteres nagyságrendű) -
így a legmélyebb LOD-szinteken a felszín a domborzat-zaj szempontjából
GYAKORLATILAG SIMA, függetlenül attól, hány geometriai háromszögre van
felbontva. A felhasználó által kért megoldás (egy magasabb frekvenciájú,
alacsonyabb amplitúdójú második zajréteg) ERRE a valós okra is helyes
válasz, csak a diagnózis pontosítása fontos a jövőbeli hangoláshoz.

**Megoldás:** `CrustElevation.SecondaryDetailNoise` - UGYANAZ a már
verifikált `FractalNoise.RidgedMultifractal` primitív (nincs új
hash-függvény, ld. `DomainWarp` azonos precedense), de:
- **Frekvencia:** a periódus `SecondaryNoisePeriodTiles=40` darab, a
  renderer `PlanetGridMesh.cs` alapértelmezett, adaptív felbontás ELŐTTI
  statikus rács-szintjének (`level=5`, dokumentáltan "a tile-ok EREDETI
  mérete") megfelelő tile-nyi. Átszámítás: egy kockalap éle kb. π/2
  radiánt fed le, 2^level tile-ra osztva → egy referencia-tile szögmérete
  kb. (π/2)/32 rad; a periódus ennek 40-szerese; a `FractalNoise`
  frekvenciája a periódus reciproka (ld. `FractalNoise.Fbm`/
  `RidgedMultifractal` doksija: a frekvencia közvetlenül szorozza a
  bemeneti koordinátákat, tehát hullámhossz = 1/frekvencia).
- **Amplitúdó:** `SecondaryNoiseAmplitudeMeters=200.0` (az elsődleges
  `NoiseAmplitudeMeters=3000.0` kb. 1/15-e - "jóval alacsonyabb", ahogy a
  felhasználó kérte).
- **Dekorreláció:** fix koordináta-eltolással (`SecondaryNoiseOffsetX/Y/Z`),
  ugyanaz a minta, mint a `DomainWarp` három komponensének
  dekorrelációja - különben a második réteg csak erősítené/gyengítené az
  elsőt ugyanazokon a helyeken, nem adna FÜGGETLEN részletet.
- **NEM kapja meg a `MountainMask`-ot** (szándékos, dokumentálva a
  kódban) - épp a "sík" régiókban a legfontosabb, hogy legyen közeli-zoom
  textúra, a maszk pont ott nyomná el a legjobban.
- Az óceáni szelídítés (`OceanicNoiseFactor`) ugyanúgy vonatkozik rá.

**Ez SZÁMSZERŰEN MEGVÁLTOZTATJA a `BaseElevation` kimenetét MINDEN
pozícióra** - a CLAUDE.md "Bármely szimulációs algoritmus numerikus
viselkedésének módosítása" kritériuma teljesül. Nem vezettünk be külön
verzió-mezőt (nincs ilyen a projektben elevációra - ld. ND-31/33/34
precedens, amik szintén hangolták a formulát ND-dokumentálással, külön
version-gate nélkül, mert a rendszer mindig újragenerál seedből, nem
savegame-kompatibilitást őriz). A Python referencia
(`tools/reference/crust_elevation_ref.py`) EGYIDEJŰLEG frissült, és a
`crust_elevation_vectors.json`, valamint a rá épülő
`erosion_glaciation_deep_time_vectors.json` és `plate_boundary_vectors.json`
KAT-vektorok mind ÚJRAGENERÁLVA lettek (mindhárom Python-referencia
hívja `base_elevation`-t) - ez KÖVETI a CLAUDE.md "a Python a helyes"
elvét, nem egy új algoritmus, hanem egy MÁR verifikált primitív
újrafelhasználása más paraméterekkel, tehát NEM igényelt friss
Python-oráklum-tervezést a nulláról.

**Nyitott, dokumentált feltételezés:** a "tile-ok eredeti mérete" =
`PlanetGridMesh.level=5` (a Viewer statikus, adaptív felbontás előtti
alap-rácsa) - ez egy ÉSSZERŰ, de NEM az egyetlen lehetséges értelmezés
(az ND-02 Core-oldali "szimuláció bázis-LOD"-ja level 6). Mivel a két
konstans (`SecondaryNoiseReferenceLevel`, `SecondaryNoisePeriodTiles`)
külön áll, élő Unity-visszajelzés alapján szabadon újrahangolható KAT-
vektor-újragenerálás nélkül is (csak az AMPLITÚDÓ/FREKVENCIA hangolása
nem érinti a KAT-fájlokat, ha a felhasználó a jelenlegi arányt jónak
találja - de MAGÁT a formulát/paramétert módosítani újra KAT-regenerálást
igényel, ld. fent).

**Élő Unity-ellenőrzés MÉG NINCS** - ez egy Core-only numerikus
változtatás, build+teszt-ellenőrizve (ld. session-napló), de a
VIZUÁLIS hatást (van-e érdemi különbség a legmélyebb zoom-szinten) csak
élő Unity Play-módban lehet megerősíteni.

**Kiegészítés (2026-09-07): GPU-oldali port + fordítási-idő regresszió,
VÉGSŐ MEGOLDÁS.** Code review feltárta, hogy `TileClassification.compute`
(a Viewer `useGpuClassification`/`useGpuGeometry` gyorsítóútja) NEM
kapta meg a másodlagos zajt - pótolva (`SecondaryDetailNoiseF`, 1:1 a
frekvencia/amplitúdó/eltolás paraméterekben). EZUTÁN a felhasználó élő
Unity-ben "Compiler timed out" hibát kapott a `CSGenerateTerrainGeometry`
kernelre - ez a kernel szálanként 5x hívja az elevációt (1 közép + 4
sarok, a HLSL fordító által jellemzően teljesen kifejtett ciklusban), és
a hozzáadott 3 oktáv × 5 hívás × 8 sarok-hash már túl sok volt a
fordítónak. ELSŐ javítási kísérlet: a GPU-oldali oktáv-szám 3→1-re
csökkentve, plusz egy `[loop]` attribútum a sarok-ciklusra - a
felhasználó ÚJRA tesztelte, UGYANAZT a "Compiler timed out" hibát kapta.
**Ahelyett hogy tovább próbálkoznánk verifikálhatatlan félmegoldásokkal,
a `BaseElevationF` TELJESEN VISSZAÁLLÍTVA az ND-52 ELŐTTI formulára**
(`git diff HEAD` szerint a függvény törzse bájtra megegyezik a legutóbbi
commit-tal, csak egy magyarázó kommentár maradt) - a GPU-port
VÉGLEGESEN, SZÁNDÉKOSAN NEM tartalmazza a másodlagos zajt. Ez egy
elfogadott, dokumentált korlát: a `useGpuClassification`/`useGpuGeometry`
bekapcsolásakor a GPU-úton számolt eleváció a finom részlet-zaj nélkül,
valamivel simább, mint a CPU-é (ami a TÉNYLEGES, alapértelmezett
renderelt geometriát adja - a GPU-gyorsítás jelenleg alapból KI van
kapcsolva). Tanulság: két egymást követő, magam által nem
verifikálható HLSL-fordítási-idő-optimalizálási kísérlet helyett a
BIZTOSAN MŰKÖDŐ állapotra való teljes visszaállás volt a helyes döntés,
amint az első próbálkozás nem vált be.

**Kiegészítés (2026-09-07): amplitúdó-újrahangolás, tervezési hiba
korrigálva.** A felhasználó jelezte: "nem jött be... a második szintű
zaj... lehet e az egész síkra kiterjedő folytonos zajt hozzáadni?".
Utólagos számolás feltárta, hogy az EREDETI ND-52 terv számolási hibán
alapult: `SecondaryNoisePeriodTiles=40` × egy level=5 tile szögmérete
együtt ~1.9635 radián, ami egy teljes nagykör ~0.3125-öd része -
SZÉLESEBB periódus, mint akár az elsődleges zaj BÁZIS-oktávja
(periódus=0.125 rad). A "másodlagos, közeli-zoom finom részlet-zaj"
koncepció tehát TÉVES premisszán alapult - a zaj sosem adott finom
részletet, csak egy nagyon halvány (200m), regionális léptékű
hullámzást, ami minden zoom-szinten gyakorlatilag láthatatlan maradt.

Mivel a periódus MÁR EGYFAJTA egész-felszínt átfogó, folytonosan
ismétlődő lépteket ad, a javítás NEM a frekvencián, hanem KIZÁRÓLAG az
amplitúdón múlt: `SecondaryNoiseAmplitudeMeters` 200→900m (az
elsődleges 3000m kb. 30%-a). A funkció neve/paraméterei változatlanok
(elkerülve egy újabb átnevezési kaszkádot), de a dokumentált SZEREPE
mostantól "másodlagos, folytonos, regionális léptékű domborzat-
textúra", nem "közeli-zoom részlet". Ugyanaz a teljes KAT-vektor-
regenerálási lánc futott le, mint az első ND-52 körben (lásd fent) -
a kontinens-szám 37→34-re módosult, a `TestEarth001` habitability-
sávja Moderate→Low-ra tolódott (a víz-arány/kontinens-szám invariáns
továbbra is teljesül). A `PlanetView.unity` scene stale (a session
korábbi kód-alapérték-változásait nem követő) `surfaceAmbient`/
`surfaceSpecularStrength`/`surfaceShininess` mezői is közvetlenül
frissítve a scene-fájlban - ez magyarázza, miért nem volt észrevehető
az éjszakai-sötétítés javítás sem (egy már szerializált Inspector-
érték nem frissül automatikusan kód-alapérték-változáskor).

### ND-53 — HDRP Bloom küszöb (threshold=0) volt a "folyó/jég brutálisan csillog" jelenség valódi oka, NEM a terep-anyagok

**Kérdés (felhasználói visszajelzés, 2026-09-06 óta ismétlődő,
2026-09-07-én lezárva).** A folyó- és jég-felületek fény-visszaverődése
a felhasználó szerint "mintha villámlana" - túl erős, nem folyamatos.
Négy egymást követő javítási kör (a `VertexColorUnlit.shader`
Blinn-Phong `_SpecStrength`/`_Shininess` csökkentése 0.30/24-ről
0.12/8-ra; a `River`/`SeaIce` kategóriák HDRP/Lit `_Smoothness`-ének
beállítása 0.08-ra, amit korábban a kód SOHA nem állított be; a
`PlanetView.unity` scene stale, kód-alapérték-változás előtti
szerializált értékeinek frissítése; végül DIAGNOSZTIKAI KÍSÉRLETKÉNT
mindkét anyag specularis/smoothness paraméterének NULLÁRA állítása)
**egyik sem oldotta meg a problémát** - a felhasználó screenshotot
küldött (`pics/p.png`), ami bebizonyította, hogy a csillogás a
specStrength=0/Smoothness=0 állapotban IS változatlanul erős maradt.

**Gyökérok.** A screenshoton a "csillanó" foltok kerek, lágy-szélű,
glóriás, "kifehéredett" jellege NEM Blinn-Phong/PBR spekuláris
csillanásra utalt (ami tile-diszkrét, keskeny fényfoltokat adna),
hanem HDRP BLOOM post-processing-re. A projekt globális HDRP
alapértelmezés-profiljában (`unity/WorldGenViewer/Assets/Settings/
HDRPDefaultResources/DefaultSettingsVolumeProfile.asset` - ez
érvényesül, mert a `PlanetView.unity` scene-ben NINCS külön `Volume`
GameObject/felülbírálás) a **Bloom `threshold` (küszöb) értéke `0`
volt**. Ez azt jelenti, hogy GYAKORLATILAG BÁRMILYEN nem-teljesen-
fekete felület hozzájárul a bloom-hatáshoz, nem csak a szándékosan
HDR-fényes elemek (pl. a Nap-korong, `StarUnlit.shader` `[HDR]`
`_Color` + `_Brightness` szorzó, ami akár 20x-osra is felmehet). Mivel
a Bloom egy POST-PROCESSING effekt a VÉGSŐ renderelt pixel-
fényességre, TELJESEN FÜGGETLEN attól, hogy a fényesség diffúz vagy
spekuláris eredetű - ez magyarázza, miért volt HATÁSTALAN minden
anyag-szintű próbálkozás (a Bloom ugyanúgy bevilágította a jeget/vizet
akár volt specular, akár nem). A jég (közel-fehér, `IceSheet` szín
≈(0.95, 0.96, 0.98)) és a napfényes víz a jelenet LEGFÉNYESEBB LDR-
tartományú felületei - ők lépik át elsőként és legerősebben egy ilyen
kritikusan alacsony küszöböt.

**Javítás.** `threshold` 0 → 1.05 (`DefaultSettingsVolumeProfile.
asset`). Ez a normál, `[0,1]` LDR-tartományú terep/víz/jég színeket
MÁR NEM engedi bloomolni, de a Nap-korong (ami `_Brightness`-sel
szándékosan 1.0 fölé van tolva) TOVÁBBRA IS bloomol, ahogy az eredetileg
kívánt volt (ld. checklist 2. pont, "Nap-korong fényessége"). A
korábban diagnosztikai célból lecsökkentett/nullázott terep-anyag
paraméterek (`surfaceSpecularStrength`, `FlatMaterialSmoothness`)
visszaállítva az eredeti, ésszerű kalibrációra (0.12/8, ill. 0.08) -
ezek soha nem voltak hibásak, csak a Bloom maszkolta el a hatásukat.

**Tanulság.** Egy vizuális "csillanás" tünet ELSŐ RÁNÉZÉSRE a
leginkább kézenfekvő, hasonló nevű mechanizmusra (anyag-specular)
utalt, és 4 kör alatt sem derült ki tévesen, MERT minden egyes
anyag-szintű változtatás valóban VALAMENNYIT csökkentette a
látványt is (a bloom-hatás input-fényessége részben az anyag saját
diffúz+specular kimenete) - csak a VALÓDI, domináns forrás (a
post-processing lánc) sosem került szóba, amíg egy DÖNTŐ, nulla-
állapotú kísérlet (screenshot-tal dokumentálva) véglegesen ki nem
zárta az anyagokat. Ez a projekt saját "ne találgass, szerezz konkrét
bizonyítékot" elvének egy újabb megerősítése - itt a bizonyíték egy
felhasználói screenshot volt, nem egy Debug.Log.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-beállítás,
a Core-t nem érinti). Élő Unity-ellenőrzés hátra.

**✅✅✅✅✅✅✅✅✅✅✅ VÉGLEGES LEZÁRÁS (2026-09-09), TELJES BLOOM-
KIKAPCSOLÁS:** a jelenség UGYANEBBEN a formában (pólusi/vízfelszíni
túlexponálás) többször is visszatért a threshold-emelés (1.05→1.4)
ELLENÉRE - a döntő diagnosztikai adat: a "Tavak+jég" kikapcsolása a
nyílt vízfelszínt tárta fel, ami MÉG ERŐSEBB túlexponálást adott, noha
számolással a vízfelszín-shader kimenete (specular=0.02, sötét
alapszín) messze a Bloom-küszöb alatt kellene maradjon - ez kizárta,
hogy a rendes diffúz+specular lánc lenne a magyarázat. Végső, döntő
lépés: `Bloom.intensity` 0.2→**0** (a teljes effekt kikapcsolva, nem
csak a küszöb hangolva). Felhasználói visszaigazolás: **"tök jó, végre
nem csillognak a pólusok! megoldottad"**. A 2026-09-06 óta tartó,
20+ körös "csillogás" vizsgálat ezzel VÉGLEGESEN LEZÁRVA - retrospektíve
a Bloom hatása minden korábbi hangolásnál (threshold/scatter emelés)
erősebbnek bizonyult, mint amit egy csökkentett, de aktív Bloom
kezelni tudott; csak a teljes kikapcsolás oldotta meg. **Nyitott
kérdés a jövőre**: ha valaha legitim HDR fényforrás (pl. a látható
Napkorong) Bloom-glóriáját vissza szeretnénk kapni, azt a Bloom
UJRA bekapcsolásával, de a terep/víz shaderek kimenetének SZIGORÚBB
[0,1] tartományra clamp-elésével kellene megoldani, nem a jelenlegi
(bizonyítottan elégtelen) küszöb-hangolással.

### ND-54 — Folytonos felszín-színezés a "Full" hőmodellt használja, NEM a "Simple"-t; GPU-klasszifikáció kikapcsolva

**Kontextus:** a 13-16. körös "folyó/jég villódzás" vizsgálat során
(ld. `history/2026-09-06-lod-rivers-clouds-session.md`) egy ÚJABB,
minden korábbitól ELTÉRŐ jelenség került elő: egy stabil, a bolygó
forgásával együtt mozgó **sárga folt pontosan az északi pólusnál**.
Ez NEM rendering-hiba (nem NaN, nem shader-degenerálódás) - számolással
igazolt, valódi szimulációs eredmény.

**A jelenség oka:** a folytonos felszín-színezéshez (`ContinuousCornerColor`
és az adaptív LOD tile-klasszifikáció) használt `Temperature.
TemperatureKelvin` ("Simple" képlet, ld. `src/WorldGen.Core/Climate/
Temperature.cs`) NEM tartalmaz jég-albedó visszacsatolást vagy óceáni
hő-puffert - azok csak a "Full" modellben vannak (`TemperatureKelvinFull`,
ND-42/§28.1). Sarki NYÁR alatt (a tengely a Nap felé billen) a Nap SOHA
nem nyugszik le a pólusnál - a napi átlagos `cos(θ)` ott folyamatosan
magas marad, míg az egyenlítőnél az idő kb. felében leáll (éjszaka).
Kiszámolva a jelenlegi 23.44°-os tengelydőléssel: **pólus (sarki nappal)
≈ 319K (46°C)** vs **egyenlítő (napi átlag) ≈ 303K (30°C)** - a Simple
képlet szerint a pólus MELEGEBB, mint az egyenlítő, ami átlépi a
`TemperateThresholdK`-t (293.15K), "Tropical" (sárga) színt adva a
fizikailag leghidegebbnek szánt pontnak.

**Kapcsolódó inkonzisztencia:** a fő biome-besorolás (a kontinens-lista/
panel "dominant: X" mezője) UGYANEZT a Simple képletet használta - tehát
a panel és a renderelt szín korábban KONZISZTENSEN hibás volt együtt
(nem vették észre, mert a panel csak az AGGREGÁLT domináns biome-ot
mutatja, egy kis sarki "Tropical" folt eltűnik az átlagban).

**Döntés:** mind az 5 hívási hely (`ComputeTileClassification`, a GPU-
klasszifikáció CPU-fallback ága, `PrecomputeCornersInParallel`,
`ContinuousCornerColor`, a habitability-számítás) egységesen egy új
`PlanetGridMesh.TemperatureKelvinAt(...)` segédfüggvényen át
`TemperatureKelvinFull`-t hív, `tYears = deepTimeMyr * 1e6`-tal és a
`_adaptiveSeed`-del (a T_weather zajhoz). Ez egyszerre javítja a
felszín-színt ÉS teszi konzisztenssé a panel-besorolással.

**GPU-klasszifikáció kikapcsolva (`useGpuClassification: 1→0` a
`PlanetView.unity`-ban).** A `TileClassification.compute` GPU-shader
saját, portolt Simple-képletet használ (`TemperatureKelvinF`) - a Full
modell GPU-portolása (időjárás-zaj + Milankovics-ciklusok + 12-mintás
óceáni átlag) valószínűleg reprodukálná az ND-52-ben már dokumentált
GPU shader-fordítási időtúllépést (ahol egy JÓVAL kisebb bővítés miatt
is vissza kellett vonni a változtatást). Felhasználói döntés (2026-09-09,
3 opció közül választva): a biztos, azonnal helyes CPU-path-ot
választottuk a kockázatos GPU-portolás helyett. **Nyitott, dokumentált
korlát**: ha valaki később újra bekapcsolja a `useGpuClassification`-t,
a GPU-oldali klasszifikáció/szín ismét a Simple képletet fogja
használni (a sarki nyár-jelenség visszatér) - a GPU-shader Full-portolása
külön, elkülönült feladat, csak akkor éri meg, ha a CPU-path teljesítménye
nem elég egy nagyobb rácsfelbontáshoz.

**Teljesítmény-megjegyzés:** a Full modell drágább, mint a Simple (extra
`AnnualMeanRadiativeTemperature` 12-mintás hívás OCEANI pontokra - de
a `ContinuousCornerColor`/`TemperatureKelvinAt` hívások SOHA nem
oceáni pontra futnak, mert az óceáni ág korábban visszatér
`ContinuousOceanRockColor`-ral, tehát ott a t_ocean tag mindig 0,
extra költség nélkül; a `ComputeTileClassification`/GPU-fallback ágakon
viszont IGEN, ott érdemi extra költség jelentkezhet óceáni tile-oknál).
Élő teljesítmény-ellenőrzés hátra (`adaptiveRebuildWarningMs` naplóval).

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render/klasszifikációs
változás, a Core-t/a mentett világállapotot nem érinti - a `Temperature.
TemperatureKelvinFull` már létező, tesztelt Core-függvény, nem új
algoritmus). Élő Unity-ellenőrzés hátra.

**❌ UTÓJEGYZET, UGYANAZNAP: VISSZAVONVA teljesítmény-regresszió miatt.**
Élő tesztelés során a felhasználó jelezte: a fenti váltás után "az egész
bolygó tök sötét" lett. Valószínű ok: `ClimateCycleTemperatureK` és
`GreenhouseTemperature` **pozíciófüggetlen** értéket adnak (kizárólag
`worldSeed`/`tYears`/`ghgPpm` függvényei, az `(x,y,z)` paraméterektől
teljesen függetlenek), MÉGIS a `TemperatureKelvinFull` minden egyes
híváskor újraszámolja őket - és mivel ezt a hívást `ContinuousCornerColor`/
`ComputeTileClassification`/stb. **SORONKÉNT, SAROKONKÉNT** hívja (a
`PrecomputeCornersInParallel` könnyen százezres nagyságrendű hívásszámot
jelent), a hozzáadott 2× `Threefry4x64` hash + 3× `SinCos` hívás
hívásonként valószínűleg katasztrofális lassulást okozott - a mesh-build
feltehetően sosem futott le rendesen, ezért maradt a bolygó "sötét"
(befejezetlen/hiányos geometria-frissítés, nem tényleges fényezési hiba).

**Visszaállítva**: `TemperatureKelvinAt` ismét a "Simple" `Temperature.
TemperatureKelvin`-t hívja, `useGpuClassification` vissza `1`-re. A
"sárga pólus" jelenség (a döntés eredeti oka) emiatt VISSZATÉRHET - ez
egy külön, még nyitott tétel.

**Helyes következő lépés (NEM implementálva)**: a Full modell
pozíciófüggetlen tagjait (`tGreenhouse`, `tCycle`) egy `Build()`-enkénti
CACHE-elt mezőben egyszer kiszámolni (a `worldSeed`/`tYears`/`ghgPpm`
úgyis csak world-generáláskor/deep-time-csúszka-mozgatáskor változik),
és a per-vertex hívásba már csak a valóban pozíciófüggő tagokat
(`tRadiative`, `tAltitude`, `tWeather`) átadni. Ez megőrizné a
pólus-javítást ÉS a teljesítményt is - de ez egy külön, alaposabb
implementációs feladat, nem oldottuk meg ma.

### ND-55 — Lejtő-érzékeny, DE tile-határok között folytonos felszín-normál (veges differencia, megosztott sarok-cache-ben)

**Kontextus:** a 13-16. körös NaN-vizsgálat (ld. ND-54 fölötti szakasz)
után a 15. kör a felszín-normált a durva, quadonkénti cross-product
számításról egy tisztán gömb-irányú (`normalize(pozíció)`) közelítésre
váltotta - ez NaN-biztos volt, de a felhasználó jelezte: "elveszett a
vizuális magasság érzete" (a domborzat lejtés-árnyalása eltűnt, minden
pont úgy fényezett, mintha tökéletes gömb lenne). A lejtő-érzékeny
cross-product normál visszaállítása (helyes NaN-védelemmel) viszont
visszahozta az EREDETI (2026-09-06 óta dokumentált) problémát: "totál
visszaállt a csillogás" - mert a LAPOS, quadonkénti normál a szomszédos
tile-ok között DISZKONTINUUS, és a spekuláris fényfolt emiatt
tile-ról tile-ra ugorva "villan" (nem a fényerő a probléma, hanem a
normál-mező FOLYTONOSSÁGÁNAK hiánya - ezt már az intenzitás-csökkentés
1-12. körben sem oldotta meg véglegesen).

**Felismerés:** a probléma NEM "lapos VAGY sima normál" választás,
hanem hogy a KORÁBBI két megoldás egyike sem volt egyszerre sima ÉS
lejtés-érzékeny. A `_persistentCornerCache`/`cornerCache` már ma is
MEGOSZTJA a sarokpontok POZÍCIÓJÁT a szomszédos tile-ok között (a kulcs
`(Face, Level, CornerU, CornerV)`, független attól, MELYIK tile kéri) -
ha a NORMÁLT is ugyanígy, a sarokponthoz kötve, egy KIS, RÖGZÍTETT (nem
a hívó quad tile-méretétől függő) UV-eltolással vett veges differenciával
számoljuk (nem a hívó quad SAJÁT, tile-méretű sarok-távolságával), akkor
a normál TISZTÁN a `(face,uc,vc)` pont függvénye - a szomszédos tile-ok
automatikusan UGYANAZT az értéket kapják a közös sarkukon (nincs ugrás),
miközben a normál továbbra is a TÉNYLEGES helyi lejtésből származik (nem
egy lejtés-vak gömb-közelítésből).

**Implementáció** (`PlanetGridMesh.cs`):
- `ComputeCornerNormalViaFiniteDifference(face, uc, vc, ...)`: a
  `ToDisplacedVector3`-at (a MÁR létező, egyetlen pozíció-forrás
  függvényt) hívja a `(uc,vc)`, `(uc+ε,vc)`, `(uc,vc+ε)` pontokra
  (`ε = NormalSampleEpsilonUV = 1e-4`), a két érintő-vektor cross
  szorzatából normál - degenerált/NaN esetben `SafeSurfaceNormal`
  (gömb-irányú) tartalékra esik vissza (a 14. körben felismert HELYES,
  `!(x >= küszöb)` NaN-védelemmel).
- Ugyanaz a MEGOSZTOTT sarok-cache-mintázat, mint a színnél
  (`_persistentCornerColorCache`/`GetOrComputePersistentCornerColor`):
  új `_persistentCornerNormalCache` (adaptív út) + egy helyi
  `cornerNormalCache` (statikus alapréteg-ciklus) + a GPU-geometria
  útvonalhoz is bekötve. A `PrecomputeCornersInParallel` MOST már a
  normált is a MEGLÉVŐ párhuzamos ciklusban számolja (nem külön,
  szekvenciális lépésben).
- Új `AddQuad` túlterhelés, ami a 4 csúcs-normált KÍVÜLRŐL, előre
  kiszámítva kapja (a szárazföldi kategóriák hívják) - a víz/tó/folyó/
  kráter továbbra is a RÉGI, belsőleg számolt (lapos/degenerált-védett)
  `AddQuad`-ot használja, mert azoknál a folytonosság kevésbé kritikus
  (víz eleve gömb, a többi kis, jelölő jellegű terület) - ld. a korábbi
  ND-54-es döntés kapcsán már dokumentált precedens.

**TELJESÍTMÉNY, KÜLÖN KIEMELVE (az ND-54 alatti Full-modell-
regresszióból tanulva)**: ez EGYEDI, MÉG NEM CACHE-ELT sarkonként 2
TOVÁBBI teljes elevációkiértékelést (`ToDisplacedVector3`) jelent - de
KIZÁRÓLAG a megosztott sarok-cache-en KERESZTÜL érhető el
(`ComputeCornerNormal`/`GetOrComputePersistentCornerNormal`), SOHA nem
quadonként közvetlenül, tehát a többletköltség pontosan úgy korlátozott,
mint a már bevált szín-cache költsége (`PrecomputeCornersInParallel`,
Parallel.For, csak a hiányzó sarkokra). **Élő teljesítmény-ellenőrzés
hátra** - ha az `adaptiveRebuildWarningMs` naplóban tartós lassulás
jelentkezne, az `ε` durvábbra állítása vagy a normál-cache külön
LRU-mérete (jelenleg a pozíció-cache-ével közös) hangolható.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-változás, a
Core-t nem érinti). Élő Unity-ellenőrzés hátra.

### ND-56 — Harmadik, közeli-zoom léptékű részlet-zaj réteg (`CrustElevation.TertiaryDetailNoise`)

**Kontextus:** felhasználói kérés (2026-09-09): "eddig fraktál alapú volt
[a másodlagos zaj], most olyat szeretnék ami a maximális felbontás esetén
is minden tile-ra hatással van". A meglévő másodlagos zaj (ND-52) periódusa
a level=5 STATIKUS rácshoz van hangolva - regionális léptékű, folytonos
domborzat-textúrát ad, de a legmélyebb adaptív LOD-nál (nagyságrendekkel
finomabb tile-méret) több ezer szomszédos tile esik ugyanabba a hullámba,
ezért ott ismét laposnak/részlettelennek tűnhet a felszín.

**Megvitatott alternatívák** (felhasználóval egyeztetve, `1` opció
választva):
1. **Harmadik, magasabb frekvenciájú koherens zajréteg** (ugyanaz a
   `RidgedMultifractal`, csak közeli-zoom léptékre hangolva) - a
   választott irány.
2. Sarokponthoz kötött, determinisztikus hash-alapú "mikro-jitter" (nem
   folytonos zajfüggvény) - elvetve, mert "szemcsés/kráteres" hatást ad,
   nem "domborzat-szerű" mintázatot.
3. GPU-alapú zajszámítás kiterjesztése - a felhasználó felvetette, de
   elvetve: a `WorldGen.Core` motorfüggetlensége miatt a GPU sosem
   lehet a HITELES modell forrása (csak Viewer-oldali közelítés); az
   ND-52-ben már dokumentált GPU shader-fordítási időtúllépés-korlát
   minden további réteggel súlyosbodna; a finom (magas frekvenciás) zaj
   pontossága a `float` (32 bit, GPU) precízióval ROSSZABB, nem jobb,
   mint `double`-lel (CPU) - pont ott, ahol a legfinomabb részlet
   számítana.

**Implementáció**: `CrustElevation.TertiaryDetailNoise` - ugyanaz a
`RidgedMultifractal` algoritmus, mint az elsődleges/másodlagos réteg,
harmadik, független koordináta-eltolással dekorrelálva, `MountainMask`
nélkül (ugyanazon okból, mint a másodlagos - sík régiókban a
legfontosabb, hogy legyen közeli-zoom textúra).

**❌ ELSŐ PRÓBÁLKOZÁS VISSZAVONVA, ÉLŐ TESZT ELŐTT (2026-09-09):**
`TertiaryNoiseReferenceLevel=20` (a Unity Viewer `PlanetGridMesh.
adaptiveMaxLevel` ELMÉLETI felső korlátja), `TertiaryNoisePeriodTiles=3`.
Felhasználói visszajelzés élő teszt után: **"katasztrófa... a távoli zoom
nézetet nagyban befolyásolja, ellenben az extrém közeli zoom esetén nem
egyenletes a zaj eloszlása"**. Diagnózis: level=20 a GYAKORLATBAN szinte
soha nem éretik el (elméleti felső korlát, nem tényleges zoom-mélység),
ezért a zaj hullámhossza a gyakorlati LOD-oknál sokkal kisebb, mint egy
tile - ez **térbeli ALIASING**-ot okozott két irányban: (1) távoli/durva
LOD-nál a kevés, egymástól távoli sarokpont a hullám véletlenszerű
fázisait találta el → kaotikus zaj ott, ahol semminek nem kellett volna
látszania; (2) közeli, de a ténylegesen elért (nem elméleti max) LOD-nál
egyetlen tile-on belül több teljes hullámciklus is belefért → egyenetlen,
foltos hatás sima hullámzás helyett.

**✅ JAVÍTVA, ÉLŐ TESZT ELŐTT**: a másodlagos zaj sikeres mintáját
ismételtük meg (level a STATIKUS/gyakran-elért rácshoz hangolva, nem az
elméleti maximumhoz) - `TertiaryNoiseReferenceLevel` 20→**13** (~ND-18
"Erózió cél-LOD: 12" értékéhez közeli), `TertiaryNoisePeriodTiles` 3→**8**
(hogy egy tile-on belül ne törjön több teljes hullámciklus).
`TertiaryNoiseAmplitudeMeters=50`, `TertiaryNoiseOctaves=2` változatlan.

**Regenerálási lánc**: a teljes downstream kaszkád újrafuttatva (Python
referencia + minden függő KAT-vektor: crust_elevation, plate_boundary,
erosion_glaciation_deep_time, hydrology, river_path, lakes_ice_erosion,
moisture_transport, features, state_hash - a `volcanism`/`sea_level` nem
függ közvetlenül a `base_elevation`-től, TEST-EARTH-001 továbbra is PASS,
65.0% víz-arány). Kontinens-szám 34→**37**, régió-szám 412→**402** (a
`TestEarth001Tests.ContinentSizesMatchPythonReferenceExactly` és
`FeatureSegmentationStructuralTests.MatchesPythonReferenceContinentsAndRegionsExactly`
tesztek frissítve a Python referenciával újramért, pontos értékekre).
375/375 Core-teszt PASS.

**Verziózás:** SEED-TÖRŐ (a `CrustElevation.BaseElevation` numerikus
kimenete minden pozícióra megváltozik - ugyanaz a besorolás, mint ND-52).

**❌ TELJESEN VISSZAVONVA, UGYANAZNAP (2026-09-09):** élő Unity-teszt
után a felhasználó visszajelzése: "nem lett jobb, szeretném visszavonni
a 3. fokú zajgenerálást. működjön minden úgy ahogy ezelőtt". A level=13/
8-tile újrahangolás sem hozott érzékelhető javulást a korábbi
(level=20/3-tile, már korábban "katasztrófaként" elutasított) állapothoz
képest. A teljes ND-56 réteg (Python `tertiary_detail_noise`/
`TERTIARY_NOISE_*` konstansok, C# `TertiaryDetailNoise`/
`TertiaryNoise*` mezők, mindkét helyen a `base_elevation`/`BaseElevation`
visszatérési sorból az additív tag) KITÖRÖLVE. A teljes downstream
KAT-vektor-lánc újra regenerálva az ND-52-es (másodlagos zaj, harmadik
réteg nélküli) állapotra, a két érintett teszt-elvárás visszaállítva
(kontinens-szám 37→**34**, régió-szám 402→**412**, kontinens-méret-lista
visszaállítva). 375/375 Core-teszt PASS. **Tanulság**: ez a második eset
ebben a session-ben (az első az ND-54 Full-hőmodell-kísérlet volt), hogy
egy jól megindokolt, referencia-szinten plauzibilis numerikus finomítás
élő Unity-tesztelésen egyszerűen NEM hozott érzékelhető/kívánt vizuális
javulást - a `CrustElevation.BaseElevation` további finomítása inkább
VIZUÁLIS, élő Unity-vissza csatolással vezérelt iterációt igényelne
(pl. egyenesen Unityben, a tényleges renderelt eredményt figyelve), nem
tisztán referencia-szintű (Python/C# plauzibilitás-teszt) tervezést.

### ND-57 — Tengeri jég (SeaIce/Ocean) határ zaj-jitterrel, a szárazföldi jégsapka-mintát követve

**Kontextus:** felhasználói visszajelzés (2026-09-09): "van az északi és
déli póluson is egy fix, adott magassági foknál lévő jég kirajzolás, kör
alakú, a pólus a r sugarú körben, belül van a jég" - kapcsolódik a
korábbi #7-es checklist-tételhez ("Pólusi jég — zajos partvonal"), ahol
a felhasználó jelezte: "a sarkvidéki kontinens ok, a konstans fehér
sapka még mindig ott van".

**Gyökérok:** a `Biome.SeaIce`/`Biome.Ocean` határ a Core-ban
(`BiomeClassification.Classify`) TISZTA hőmérséklet-küszöb
(`OceanFreezingK`=271.15K), zaj/jitter NÉLKÜL - ellentétben a
szárazföldi jégsapka-határral, amit a Viewer korábban (ND-nem-számozott,
`IsAdaptiveIceTile`) már zajjal perturbált. Mivel az óceáni hőmérséklet
(a jelenlegi egyszerű, inszolláció-alapú modellben) majdnem tökéletesen
szélesség-szimmetrikus, ez egy geometriailag tökéletes kört adott a
tengeri jég határának mindkét pólusnál.

**Javítás** (`PlanetGridMesh.cs`): új `IsAdaptiveSeaIce(x,y,z,
temperatureK)` - UGYANAZ a jitter-minta (`IceBoundaryJitterAmplitudeK`/
`Frequency`/`Octaves`, `FractalNoise.Fbm`), mint a szárazföldi
`IsAdaptiveIceTile`, csak az `OceanFreezingK` küszöbre alkalmazva. FONTOS:
ez KIZÁRÓLAG a RENDER-kategória (`RenderCategory.SeaIce` vs `.Ocean`)
döntését módosítja - a Core `biome`/`temperatureK` (és az ezekből
számolt statisztikák, pl. panel-adatok) VÁLTOZATLANOK maradnak,
ugyanazon elv szerint, mint a szárazföldi jég jitterje. Mindhárom
érintett hely frissítve: a statikus alapréteg, az adaptív
`ComputeTileClassification`, és MINDKÉT vízfelszín-szín-döntés (korábban
`biome == Biome.SeaIce`-t néztek, most a már jitterelt kategóriát/
`isSeaIceRendered`-et).

**Dokumentált, el nem hárított korlát**: a GPU compute shader port
(`TileClassification.compute`, `ClassifyBiomeF`) NEM kapott jittert -
`useGpuGeometry` alapértelmezetten ki van kapcsolva, és a
`GpuQuadResult` nem is ad vissza `temperatureK`-t a hívó oldalnak (csak
elevation/isOceanic/biome-ot) - a jitter hozzáadásához ez is bővítendő
lenne, külön feladat.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-döntés, a
Core `biome`/`temperatureK`/statisztikák változatlanok). **Élő
Unity-ellenőrzés hátra.**

### ND-58 — SeaIce is a "folytonos" (nem lapos HDRP/Lit) terep-kategóriák közé került

**Kontextus:** az ND-57 jitter hozzáadása UTÁN a felhasználó jelezte,
hogy a kör alakúság problémája fennmaradt, DE egy screenshot alapján
kiderült, hogy a valódi, elsődleges probléma NEM a határvonal
szabálytalansága volt - a nyílt óceán fölött (nincs alatta kontinens)
egy ÉLES, SOKSZÖGLETES (jól látható háromszög-facettákkal), és
ÉJSZAKA is világosszürke maradó folt jelent meg.

**Gyökérok:** a `RenderCategory.SeaIce` a `GetOrCreateCategoryMaterial`
és `IsContinuousTerrainCategory` szerint a `River`/`Crater`
kategóriákkal egy csoportba tartozott - "kis terület/jelölő jellegű",
ezért a RÉGI, lapos `CategoryColor` + `CreateFlatColorMaterial` (Unity
beépített HDRP/Lit) útvonalat kapta, NEM a folytonos
`VertexColorUnlit`-et. Ez a feltételezés az ND-57 ELŐTT ésszerű volt
(a tengeri jég ritkán/kis foltokban fordult elő), de az ND-57 (jitterelt
SeaIce/Ocean határ) óta a `SeaIce` EGÉSZ SARKI JÉGSAPKÁNYI, nagy,
összefüggő területet fedhet le. A HDRP/Lit anyag két, egymástól
független problémát okozott: (1) NEM használja a `surfaceAmbient`-et
(saját HDRP sky-ambient-jét kapja), ezért éjszaka sem sötétedett el
rendesen; (2) quadonként EGYETLEN, egységes színt ad (nincs
sarkonkénti interpoláció a szomszédokkal, szemben a folytonos
kategóriákkal), ami az éles, sokszögletes határvonalat okozta - ez
volt a "kör alakúság" észlelt oka is, mert a durva, egyenlő szélességű
kvadrátrács quad-hataraí adták a látszólagos geometrikus mintázatot,
nem maga a fagyási-küszöb.

**Javítás**: `RenderCategory.SeaIce` felvéve az
`IsContinuousTerrainCategory`/`ContinuousSurfaceColor` közé, az
`Ocean`-nal azonos módon (`ContinuousOceanRockColor` - mivel a
`ContinuousCornerColor` már eddig is PURE `isOceanic`-alapon döntött,
nem kategórián, ez a hívó-oldali útvonal-döntés módosítása volt
elegendő, a szín-számítás logikája változatlan). A tényleges
jég-vs-víz szín továbbra is a KÜLÖN vízfelszín-rétegből jön
(`ContinuousWaterCornerColor`/ND-57 `isSeaIceRendered`), ami MÁR
eddig is a folytonos, ambient-helyes `_waterSurfaceMaterial`-t
használta - ez a javítás a TEREP/óceánfenék-réteg (a víz alatt, illetve
egy esetleges rés/Z-fighting esetén átcsúszó) SeaIce-kategóriájú
quad-jait érinti.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-útvonal
döntés). **Élő Unity-ellenőrzés hátra.**

### ND-59 — A szárazföldi jég/tundra biome-fallback is szabályos kört adott (a Core biome, nem a jitterelt réteg, volt a valódi ok)

**Kontextus:** az ND-57/ND-58 UTÁN a felhasználó egy ÚJ screenshottal
jelezte, hogy a probléma továbbra sem a színről/anyagról szól: a
pólusoknál egy szabályos KÖR alakú, világosszürke RÉTEG látszik a
domborzat "alatt", amit a valódi terep (hegy, tó, kráter) itt-ott
felülír. Közvetlen Hierarchy-kiválasztással kizárta: `WaterSurface`,
egy "DynamicRefined"-szerű objektum, `Rivers`, `LakeSurface` - a
gyanú a `Planet` (fő terep-) objektumra esett.

**Gyökérok:** a render-kategória ternárius minden classification
helyen (`PlanetGridMesh.cs` statikus alapréteg + CPU-adaptív ág, ill.
a két GPU-táplált adaptív ág) `ToRenderCategory(biome)`-ra esik
vissza, ha a tile nem kráter/tó/óceán és a jitterelt `isIce`
(évi-átlag alapú, `IsAdaptiveIceTile`) hamis. Ez a `biome` viszont a
Core `BiomeClassification.Classify(temperatureK, isOceanic)`
PILLANATNYI, ZAJ/JITTER NÉLKÜLI hőmérsékletéből jön
(`BiomeClassification.cs`: `if (temperatureK < IceSheetThresholdK)
return Biome.IceSheet;`). Két, egymástól FÜGGETLEN jégréteg létezett
tehát: (1) a jitterelt, évi-átlag `isIce`, és (2) a nyers `biome` saját
IceSheet-besorolása. Mivel `isIce` csak HOZZÁAD jeget, sosem vesz el,
a nyers, jitter nélküli réteg mindig "átsejlik", ahol `isIce` épp
hamis - és mivel a szárazföldi pillanatnyi hőmérséklet a pólusoknál
(sík, alacsony domborzatú területeken) közel tisztán
szélesség/évszak-függő, ez a réteg geometriailag majdnem tökéletes
kör. Ugyanaz a hibaosztály, mint az ND-57 (tengeri jég), csak a
SZÁRAZFÖLDI biome-eldöntésnél, és korábban rejtve maradt, mert a
domborzat/hegy/tó véletlenszerűen gyakran felülírta.

**Javítás:** új `JitteredRenderBiome(x,y,z,temperatureK,isOceanic,seed)`
helper (`PlanetGridMesh.cs`) - ugyanazt az `IceBoundaryJitterAmplitudeK`/
`Frequency`/`Octaves` zajt alkalmazza a hőmérsékletre, mint az
ND-57/`IsAdaptiveIceTile`, MIELŐTT a `BiomeClassification.Classify`-t
hívja. Bekötve a két CPU-oldali fallback-ágba (statikus alapréteg és a
CPU-adaptív `ComputeTileClassification`) - ez érinti azt, ami a
"Bolygó" nézetben (mindig CPU, ez volt a screenshoten látható). A két
GPU-táplált adaptív ág (`useGpuClassification: 1` a scene-ben) NEM
kapta meg ezt a javítást, mert a `GpuQuadResult`/`GpuClassificationResult`
csak a már kész, diszkrét `biome`-ot adja vissza a CPU-nak, nem a
nyers `temperatureK`-t - ugyanaz az elfogadott, dokumentált korlát,
mint az ND-57 GPU-oldali hiánya (ld. `TileClassification.compute`
kommentje `ClassifyBiomeF` fölött). A Core `biomeOf[id]`/statisztikák
(kontinens-osztályozás, névgenerálás stb.) ÉRINTETLENEK - kizárólag a
Viewer render-kategória döntése változott.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-útvonal
döntés). **Élő Unity-ellenőrzés hátra.**

### ND-60 — A "kör alakú pólusi jég" végleges oka: az óceán SOSEM finomodik adaptívan, és a víz FEHÉR jég-színe volt a látott réteg

**Kontextus:** az ND-59 UTÁN a felhasználó megerősítette: közelről zoomolva
IS tökéletes kör maradt a pólusi "jég", és ha a `WaterSurface` réteget
kikapcsolta, ELTŰNT vele együtt ez a zavaró félkörös folt is. Saját
hipotézise: "nem lehet, hogy egy adott magassági foktól a víz/óceán
kirajzolása nem kék hanem fehér/halványszürkével történik?"

**Gyökérok (kódból igazolva, KÉT rétegben):**

1. **Az adaptív (finomodó) réteg explicit módon kizárja az óceáni
   tile-okat a finomodásból** (`IsBaseAncestorOceanic`, egy 2026-09-02-i
   szándékos teljesítmény-döntés): ha egy tile durva-szintű őse óceáni
   (`elevation < seaLevel`), SOSEM kerül finomítandó listába -
   FÜGGETLENÜL a kameratávolságtól. A tengeri jég geometriailag óceán
   (fagyott víz), tehát ez a szabály rá is érvényes - a pólusi
   víz/jég-felszín emiatt MINDIG a durva statikus alaphálón (level=5)
   renderelődik, közelről is. Egy önálló, offline C# próbaszkripttel
   (a valós seeddel/paraméterekkel, Unity nélkül) számszerűen igazolva:
   a pólus közelében a szomszédos tile-ok hőmérséklete 20-26K-t ugrik,
   amit sem K-alapú jitter (tesztelve 4→50K), sem pozíció-alapú
   szélesség-jitter (2°→35°) nem tud megtörni - a burkoló forma minden
   tesztelt amplitúdónál lényegében változatlan kör maradt.
2. **A víz-quad SZÍNE korábban bináris kapcsolóval dőlt el**:
   `isSeaIceRendered`/`category==SeaIce` esetén a TELJES quad egyetlen,
   lapos, fehér "jég" színt kapott (`CategoryColor(RenderCategory.
   SeaIce, 0)`) a normál, mélység-alapú kék óceánszín
   (`ContinuousWaterCornerColor`) helyett. Mivel (1) miatt ez a
   kapcsoló mindig a durva racson dőlt el, a fehér folt éles, szabályos
   kör alakú lett - ÉS mivel ez a `WaterSurface` mesh-en (nem a terep-
   meshen) történt, a `WaterSurface` kikapcsolása vele együtt eltüntette.

**Felhasználói döntés a javítás irányáról:** a finomodási architektúra
(1. pont) MÓDOSÍTÁSA ELUTASÍTVA ("nagyon nem jó irány... a tile bontás
most nem érdekel") - a felhasználó kifejezetten azt kérte, hogy az
óceán SZÍNÉT ne befolyásolja a pólusközelség, a tile-felbontás
kérdésétől függetlenül.

**Javítás (kizárólag a 2. pont, a víz SZÍNE):** mindhárom vízépítő
helyen (statikus alapréteg, `EmitAdaptiveTile` CPU-adaptív ág,
`EmitAdaptiveTilesGpu` GPU-adaptív ág) törölve a fehér jég-szín ág - a
víz MOSTANTÓL MINDIG `ContinuousWaterCornerColor`-t (valódi mélység-
alapú kék) kap, hőmérséklettől/szélességtől függetlenül. Emellett az
`EmitAdaptiveTilesGpu` víz-LÉTEZÉSI feltétele (`biome == Biome.Ocean`)
ki lett egészítve `|| biome == Biome.SeaIce`-szel - ez a GPU-s ág
korábban EGYÁLTALÁN nem épített vízfelszínt SeaIce-tile-okra (a
CPU-s `EmitAdaptiveTile` már korábban helyesen tartalmazta mindkét
esetet), ami a mély óceánfenék-terepet hagyta volna fedetlenül azokon
a tile-okon - ez NEM a tile-felbontásról szól, csak arról, hogy a víz
egyáltalán megépüljön-e (a látvány konzisztenciájához kellett, a
felhasználó kérésén nem változtat).

A `RenderCategory.SeaIce`/`isSeaIceRendered` kategória-eldöntés
(ND-57/ND-59 jitter) VÁLTOZATLANUL megmaradt - ez mostantól csak a
TEREP (óceánfenék) kategória-besorolásán él tovább, ami ND-58 óta
ugyanazt a színt adja, mint `Ocean` (`ContinuousOceanRockColor`),
tehát vizuálisan nincs hatása. Nem törölve, mert a `bucket`/
statisztikai célú megkülönböztetés máshol még hasznos lehet, és a
törlése nagyobb, itt nem kért átalakítás lenne.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-útvonal
döntés). **Élő Unity-ellenőrzés hátra.**

### ND-62 — Kameramód-kapcsoló + "Tengelyforgás megfigyelése" (a Planet mesh tényleges forgatása)

**Kontextus:** felhasználói kérés (2026-09-10) - a backlog két kamera-
mód tételét (pálya menti mozgás követése; tengelyforgás megfigyelése)
egy közös panel-kapcsolóval szeretné, ebben a sorrendben: (1) kapcsoló-
infrastruktúra, (2) tengelyforgás-mód, (3) pálya menti mód (ez utóbbi
KÖVETKEZŐ kör, a backlog saját nyitott kérdése miatt - fusson-e a
tengelyforgás egyidejűleg -, amit csak akkor kell eldönteni).

**Architekturális ütközés (a backlog is jelezte)**: a `SunController`
eddig SZÁNDÉKOSAN és DOKUMENTÁLTAN a `Planet` GameObject rotációját
mindig identitáson tartotta - a nap/éj ciklust a Directional Light
forgatása szimulálta, a bolygó SAJÁT (forgó) test-keretében számolt
Nap-irány (`OrbitalMechanics.SunDirectionBodyFrame`) alapján. A
"tengelyforgás megfigyelése" mód ezt PONT megfordítja: a bolygónak
TÉNYLEGESEN forognia kell, a Nap/csillagok maradjanak fixek.

**Megoldás**: mivel a `PlanetGridMesh` a mesh-csúcsokat mindig LOKÁLIS
(test-keret) koordinátában építi, a `Planet.transform.rotation`
beállítása Unity-szinten automatikusan elforgatja a KÉSZ mesh-et - a
geometria-számítás egyáltalán nem módosult. Új `PlanetGridMesh.
CameraViewMode` enum (`Free`/`AxialRotation`/`OrbitalFollow`) + panel-
kapcsoló (3 kölcsönösen kizáró `GUI.Toggle`, a `windSpeedOverlay`/
`precipitationOverlay` kizárás mintáján). `SunController.
ApplySunDirection()` mód-elágazása:
- **`Free`** (alapértelmezett): változatlan - `SunDirectionBodyFrame`,
  Planet identitáson, csillagmező ellentétes irányban forog.
- **`AxialRotation`**: `OrbitalMechanics.SunDirectionOrbitalFrame`
  (a MÁR publikus, "nem forgó pálya-keret" függvény) közvetlenül, test-
  keret-transzformáció NÉLKÜL, mint világtér-irány (fény/nap-korong/
  csillagmező ezt kapja, forgatás nélkül - a csillagmező `SetRotationAngleRadians(0)`-t
  kap, fixen áll). A `Planet.transform.rotation` a Core `R_tilt * R_spin`
  (test→pálya) mátrix Unity-megfelelőjét kapja: `Quaternion.AngleAxis(
  axialTiltDegrees, Vector3.right) * Quaternion.AngleAxis(rotationAngle
  *Rad2Deg, Vector3.up)`. A SPIN előjele a `StarField.
  SetRotationAngleRadians` MÁR élesben bevált, ELLENTÉTES irányú
  forgatásából levezetve (nagy bizonyossággal helyes) - a DŐLÉS előjele
  viszont a tengelycsere-konvenció (`BodyFrameConversion`: Core (x,y,z)
  → Unity (x,z,y), ami egy páratlan permutáció/tükrözés) miatt csak
  ELMÉLETBEN levezetett, **élő Unity-vizuális teszttel ellenőrizendő**
  - pontosan ugyanaz a kockázati osztály, mint a MÁR MEGLÉVŐ, dokumentált
  `PlanetOrbitCamera.FlyToDirection` bizonytalansága.
- **`OrbitalFollow`**: egyelőre a `Free`-vel azonos (nincs még
  implementálva - 2. kör).

**`PlanetOrbitCamera`-t NEM kellett módosítani**: a `target.position`
körül forog, a `target.rotation`-t sosem olvassa - a Planet forgása
ortogonális a szabad egérrel-nézegetéshez.

**Ellenőrzési kritérium**: ugyanannál a `currentTimeDays`-nál a
megvilágított kontinensek/terep `AxialRotation` és `Free` módban
UGYANAZOK legyenek. Ha tükrözöttnek/eltoltnak tűnik, a legvalószínűbb
javítás a dőlés-komponens előjelváltása vagy a szorzási sorrend
felcserélése.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-útvonal
döntés, Core-t nem érinti - csak egy MÁR publikus függvényt hívunk
újonnan). **Élő Unity-ellenőrzés hátra** - a `SunController`
Inspectorában be kell kötni az új `planetGridMesh` mezőt.

**Élő teszt kiegészítése (2026-09-11):** a csillagmező tengelyforgás
módban lassan a bolygóval együtt mozgott. Gyökérok: a `StarField` a
`Planet` gyereke, ezért a `SetRotationAngleRadians(0)` csak a lokális
forgatást nullázta, a szülő forgását továbbra is örökölte. Javítás:
`StarField.KeepFixedInWorldSpace()` minden képkockán világkoordinátában
identitáson tartja. A scene-ben talált `rotationPeriodDays=100` érték a
világ `climateRotationPeriodDays=1` értékével is ellentmondott és a tesztet
százszor lassította; 1 napra összehangolva. További élő ellenőrzés hátra.

### ND-63 — Deep-time exact rebuild: időfüggetlen terrain-bázis cache, változatlan időfüggő kiértékeléssel

**Kontextus és mérés (2026-09-11):** a legacy geometriahurok, a nem használt
coarse folyófelhalmozás és a redundáns normál-középpont kiértékelésének
eltávolítása után két élő deep-time rebuild 18,997 s és 19,138 s volt. A
domináns részfázis a statikus sarkok pozíció/szín/normál számítása, átlag
7,511 s. Minden időlépésnél 396 294 sarok cache miss keletkezik, miközben a
sarok nyers koordinátája és a következő drága részeredmények nem függenek a
deep-time értéktől: `DomainWarp.WarpPosition`, az elsődleges ridged zaj, a
`MountainMask` és a másodlagos zaj.

**Döntés:** a viewer a statikus base-level sarkok középpontjához és a két
ND-55 normálmintájához perzisztens, `(worldSeed, adaptiveBaseLevel)` kulcsú,
tömör tömbös `TerrainPointBasis` cache-t tart fenn. A bázis kizárólag a
warpolt koordinátát és a kéreg-eleváció időfüggetlen zajtagjait tárolja.
Deep-time váltáskor továbbra is újrafut:

- a mozgó lemezmagok előállítása és a legközelebbi/két legközelebbi lemez
  meghatározása;
- a kéregtípusból következő base elevation összeállítása;
- a lemezhatár-uplift és annak időbeli relaxációja;
- a kráterkorrekció, hőmérséklet, biome és minden modellbesorolás.

A `TwoBestDots` eredményének `bestIndex` tagja ugyanabban a seed-sorrendben,
ugyanazzal a szigorú `>` összehasonlítással adja azt a plate ID-t, mint az
eddigi külön `AssignPlate` passz. Ezért a két seed-szkennelés egyetlen
passzba vonható össze. A `MountainMask` ugyanaz a double részeredmény a base
elevation és az uplift számára, ezért újraszámítás helyett megosztható.

**Hatókör első fázisa:** csak a legfeljebb level-8 statikus base-level
sarokút használja az új cache-t. Level 9-10 esetén a memória 4x/16x lenne,
ezért ott az általános exact út marad. A dinamikus, változó LOD-sarkok a
meglévő általános útvonalon maradnak; a level-8 hidrológia és tile-
klasszifikáció közös bázistömbje egy
következő, külön mérhető fázis. Így az első változtatás kis felületű, és a
7,511 s-os legnagyobb blokkot célozza.

**Determinizmus és verziózás:** az új út ugyanazokat a részműveleteket és
azonos lebegőpontos műveleti sorrendet használja; csak a tiszta,
időfüggetlen részeredményt tárolja el. Core-teszt hasonlítja össze az eredeti
és a bázisból történő kiértékelés `double` bitmintáját több seed/pont esetén.
Nem seed-törő, világverzió-emelés nem szükséges. A cache nem kerül
perzisztálásra és nem része a world state hashnek.

**Memória:** level 8-on `6*(256+1)^2 = 396 294` sarok, három minta és mintánként
hat `double` körülbelül 54,4 MiB nyers tömbmemória. Dictionary nem használható
ehhez a bázishoz; a `(face,u,v)` determinisztikus tömbindexre képeződik.

**Elfogadási kritérium:** Core bitazonossági tesztek, teljes build/test,
Unity C# fordítás, majd élő PerfLog. Vizuális késznek csak változatlan felszín
és az új log alapján nyilvánítható.

### ND-64 — Deep-time exact rebuild: közös tile-középpont bázis és előállított napi Nap-minták

**Élő mérési alap (2026-09-11):** az ND-63 első fázisa után a két deep-time
rebuild 12 158,5 ms és 11 976,2 ms volt (átlag 12 067,4 ms). A statikus
sarokfázis 7 511,4 ms-ról 438,8 ms-ra csökkent, miközben a terrain-bázis
mindkét rebuildben `reused=True`. A maradék idő három domináns blokkja a
level-8 hidrológia (átlag 4 912,2 ms), a tile-klasszifikáció (3 026,0 ms) és
a mesh-emisszió (2 521,6 ms).

**Döntés, terrain:** az ND-63 második fázisában a level-8 tile-középpontokhoz
is tömör, `(worldSeed, level)` kulcsú `TerrainPointBasis` tömb készül. Ugyanezt
olvassa a hidrológiai elevation field és a base-level tile-klasszifikáció.
Az aktuálisan mozgatott plate-ek, uplift, relaxáció, kráter és óceán/biome
döntések továbbra is minden időpontban frissen értékelődnek. Az eredeti field
lebegőpontos műveleti sorrendje megmarad: először `base + uplift`, utána
kráterkorrekció, végül `+(relaxedUplift-uplift)`.

**Döntés, hőmérséklet:** a `Temperature.DailyAverageInsolationFactor` napi 24
mintájának Nap-iránya kizárólag a Build-szintű idő/pálya/forgás/dőlés
paraméterektől függ, a felszíni ponttól nem. Ezek az irányok egyszer készülnek
el, majd minden tile ugyanabban a sorrendben végzi el a 24 dot-productot és
összegzést. A hőmérséklet képlete, mintaszáma és összeadási sorrendje nem
változik. Az élő log közvetlen bizonyítéka: a t=0 GPU-klasszifikáció dispatch-e
37,2 ms, az utána futó, eleváció-zaj nélküli CPU hőmérséklet-loop 3 677,8 ms.

**Determinizmus és verziózás:** mindkét változtatás tiszta közös-részkifejezés
kiemelés; sem random mapping, sem numerikus képlet, sem mintavétel nem változik.
A régi és az előállított-bázisú utak `double` bitmintáját teszt fedi. Nem
seed-törő, világverzió-emelés nem szükséges. A tile-középpont cache level 8-on
393 216 × 48 byte, körülbelül 18 MiB nyers tömbmemória; level 8 fölött nem
épül fel.

**Elfogadási kapu:** teljes build/test és Unity C# fordítás után új élő PerfLog.
A `<1 s` cél ettől még nem tekinthető elértnek; a mérés után a mesh-emisszió és
a priority-flood maradékát külön kell kezelni.

### ND-65 — Exact maradék: kikapcsolt border-work elhagyása és determinisztikus priority-flood heap

**Kontextus (2026-09-11):** az ND-64 utáni két élő deep-time rebuild átlaga
5 781,2 ms. A két domináns blokk a statikus mesh-emisszió (2 464,2 ms) és a
level-8 priority-flood (1 248,8 ms). A scene-ben `showBorders=0`, mégis minden
393 216 tile négy border-vertexet és nyolc vonalindexet ír listákba; a
`BuildBorders` ezt az egész eredményt felhasználás nélkül eldobja. A flood
`SortedSet<(double Elevation,long Counter)>` mellett külön
`Dictionary<long,TileId>` leképezést tart fenn, noha a tile közvetlenül a
prioritási sor elemében tárolható.

**Döntés:** kikapcsolt border-rendernél az emit-loop nem állít elő border-
geometriát. Bekapcsolt állapotban az út változatlan. A priority-flood rendezett
halmaza egy belső bináris minimum-heapre cserélődik, amely közvetlenül az
`(Elevation, Counter, Tile)` hármast tárolja. Az összehasonlítás először
`double.CompareTo`-val az elevációt, majd `long.CompareTo`-val az egyedi,
monoton számlálót vizsgálja; ez ugyanaz a teljes rendezés, mint az eddigi
ValueTuple/SortedSet kulcsé. Így a pop-sorrend, a szomszédok fix
right/left/up/down bejárása, a parent, filled és floodOrder eredmény változatlan.

**Determinizmus és verziózás:** sem szimulációs képlet, sem tie-break, sem
bejárási sorrend nem változik; a heap csak az azonos prioritási sor más
adatszerkezeti reprezentációja. Nem seed-törő, világverzió-emelés nem kell.
Teszt hasonlítja össze a teljes `Filled`, `Parent` és `FloodOrder` kimenetet a
korábbi rendezett-halmaz referenciaúttal.

### ND-66 — A teljes statikus base-grid tömör, közvetlenül indexelt render-cache-e

**Kontextus (2026-09-11):** az ND-65 utáni két élő deep-time rebuild átlaga
5 276,6 ms. A priority-flood 31,7%-kal, az emisszió csak 4,2%-kal gyorsult; az
utóbbi továbbra is 2 361,0 ms, a teljes idő 44,7%-a. A base-grid nem ritka vagy
változó topológia: mind a 393 216 level-8 tile, illetve mind a 396 294 face-local
sarok jelen van, mégis a statikus emit minden tile-nál egy TileId-kulcsú
klasszifikációs Dictionary+LRU találatot, valamint 4-4 tuple-kulcsú pozíció-,
normál- és szín-Dictionary találatot végez. A pozíciótalálatok ezen felül a
LinkedList-alapú LRU-t is átrendezik. Ez több millió főszálas hash-/lista-
művelet olyan adatokon, amelyek indexe közvetlenül levezethető
`face * stride + u * side + v` alakban.

**Döntés:** a teljes statikus base-grid klasszifikációja, sarokpozíciója,
normálja és színe tömör tömbökbe kerül, a már rögzített face/u/v bejárási
sorrenddel. A statikus emit közvetlen tömbindexet használ; nem tölti fel és nem
érinti az általános dinamikus Dictionary/LRU cache-eket. A tömbök a Build után
is megmaradnak, ezért a dinamikus LOD base-szintű szülő-sarok és oceanic-ős
lekérdezése ugyanazt az adatot közvetlenül eléri. Level 8 felett a már meglévő
memóriakorlát miatt az általános cache-út marad.

**Egzakt viselkedés:** a tömbös út ugyanazokat a
`ComputeTileClassification`, `ToDisplacedVector3FromBasis`,
`ContinuousCornerColorAuto` és
`ComputeCornerNormalViaFiniteDifferenceFromBasis` függvényeket hívja, ugyanarra
a TileId-/face/u/v-sorrendre. Csak a tárolás és a visszakeresés változik; nincs
új numerikus képlet, random mapping vagy seedfüggés. Nem seed-törő,
világverzió-emelés nem kell. Az élő Unity-kapu: változatlan látvány és új
PerfLog; a parancssori C# build ezt nem helyettesíti.

### ND-67 — Exact sűrű priority-flood és újrahasznált cubed-sphere topológia

**Kontextus (2026-09-11):** az ND-66 utáni négy meleg deep-time rebuild átlaga
3 564,3 ms. A teljes hydrology 1 003,5 ms, ebből a priority-flood 748,0 ms. A
heap már az ND-65 szerinti tömör bináris heap, de a level-8 rács minden egyes
tile-jához továbbra is Dictionary/HashSet alapú `filled`, `parent`, `order` és
`visited` állapotot kezel, illetve minden rebuildben négyszer geometriai úton
újraszámolja a világidőtől teljesen független szomszédságot.

**Döntés:** a `FlowNetwork` kap egy fix level teljes cubed-sphere rácsát
face/u/v sorrendben reprezentáló, világfüggetlen `DenseGridTopology` típust. A
topológia egyszer állítja elő a TileId-ket és a right/left/up/down
szomszédindexeket, majd deep-time rebuildenként újrahasználható. Az új
`PriorityFloodDense` az elevációt, ocean flaget, visited állapotot, filled
magasságot, parent indexet és flood ordert tömbökben kezeli. A régi Dictionary-
alapú publikus út változatlanul megmarad a referencia-szintű/paneles
fogyasztóknak és egzakt összehasonlítási orákulumnak.

**Determinizmus:** a sűrű index pontosan a meglévő beszúrási sorrend
(`face`, majd `u`, majd `v`). Az óceáni gyökerek enqueue-sorrendje, a heap
`(elevation,counter)` összehasonlítása és a szomszédok right/left/up/down
sorrendje változatlan. Teszt veti össze minden tile `Filled`, `Parent` és
`FloodOrder` értékét a Dictionary-úttal, a `double` értékeket bitmintára.
Nincs numerikus vagy seed-viselkedés változás; világverzió-emelés nem kell.

### ND-68 — Statikus mesh-bucketek pontos előméretezése és közvetlen elérése

**Kontextus (2026-09-11):** az ND-66 után a statikus emisszió még átlag
944,7 ms. A klasszifikáció már teljes egészében tömbben rendelkezésre áll az
emit előtt, mégis minden tile négy `(category,bucket)` Dictionary-lookupot
végez, a terrain- és vízlisták pedig alapkapacitásról ismételten növekednek és
másolódnak.

**Döntés:** a statikus emit előtt egy lineáris számlálópassz meghatározza minden
terrain- és water-bucket quad-számát. A listák pontos `4*quadCount` vertex/
normal/color és `6*quadCount` index kapacitással jönnek létre, egyszer kerülnek
be a meglévő Dictionary-kimenetbe, az emit pedig közvetlen tömbreferenciával
éri el őket. A dinamikus, ritka és változó LOD-út továbbra is a meglévő
Dictionary-alapú `GetOrAddLists` megoldást használja.

**Egzakt viselkedés:** a bucket kulcsa, a tile-ok face/u/v emit-sorrendje, az
`AddQuad` hívások és a későbbi determinisztikus submesh-sorrend változatlan.
Az előpassz csak kapacitást és referenciát készít elő; nem hoz létre új
renderkategóriát, nem módosít színt, geometriát vagy szimulációs adatot.

**Élő mérés (2026-09-11):** öt meleg deep-time rebuild átlaga 2 834,7 ms,
az ND-66 utáni 3 564,3 ms-hoz képest további 20,5% javulás. A dense flood
átlag 257,5 ms (`topology reused=True`), 65,6%-kal kevesebb az előző
748,0 ms-nál. A statikus base-layer 1 602,4 ms, az emit 664,4 ms; rendre
13,0% és 29,7% javulás. A felhasználó ezt az állapotot ideiglenesen
elfogadta; a `<1 s` cél nyitott backlog marad. A következő kör előtt a nagy
13,4–243,2 ms `bucketPrepare`, 97,0–309,2 ms mesh és 25,9–283,7 ms víz-
szórás miatt Unity Profiler/GC-allokációs mérés szükséges.

### ND-69 — Zoom-LOD javítás, 1. csomag: hiteles besorolás, nézetfrissítés és megőrzött frontier

**Kontextus (2026-09-11):** a külön zoom-diagnózis után a felhasználó kérte a
javítások megkezdését. Kiindulás: `195875e`; az ND-63–68 exact gyorsításai
megmaradnak. A t=0 GPU-klasszifikáció továbbra is kihagyja a secondary detail
zajt, miközben a CPU-s geometria tartalmazza. A kamerakapu csak abszolút
elmozdulást figyel; a prioritásos cut pedig budgetnél eldobja a még függő leveleket.

**Döntés:** az adaptív CPU-geometria statikus és dinamikus klasszifikációja
egyaránt a teljes CPU-modellt használja. Az óceáni ős középpontja önmagában
nem tilthatja a már az alapmesh sarkain is látható szárazföld finomítását:
a szűréshez a négy saroknak is víz alatt kell lennie. A sarokteszt cache-elt,
világváltáskor ürül; ez mintavételes védelem, **nem** bizonyított felső korlát
a tile teljes belsejére (apró szigetek konzervatív korlátja külön feladat).

A nézet aláírása testkoordinátás pozíciót/irányt, FOV-t, képarányt és
pixelméretet tartalmaz. Kis mozgás sem veszhet el végleg: legfeljebb 0,25 s
indítási késleltetés után új kérés kell, a meglévő időkapu és single-flight
megtartásával. A kész mesh a **kéréskori**, nem az alkalmazáskori nézetet igazolja.
A pixelküszöb a perspektivikus fókusztávolságból számolódik.

A prioritásos kiválasztó a függő dinamikus leveleknek előre fenntartja a
budgetet. Split csak négy gyermek számára elegendő hellyel indulhat; telített
budgetnél a már létrejött frontier durvább levelei megmaradnak, nem dobódnak el.
A külön 2:1 balance-passz korábbi pótköltségkerete egyelőre megmarad.

**Határ:** nincs Core-/seed-/világverzió-változás. Ez az első csomag, nem a teljes
M9 lezárása: statikus/dinamikus fedéscsere, beragadt chunk-geomorph, varratok,
eltolt domborzatot követő kiválasztási korlátok és upload-időkeret még külön
javítást igényelnek. Elfogadási kapu: linkelt LOD-regressziók, solution-tesztek,
Unity C# fordítás, majd élő zoom/PerfLog. Automata teszt nem igazol látványt/FPS-t.

### ND-70 — Teljes base-tile fedéscsere és geometriaérzékeny chunk-frissítés

**Kontextus (2026-09-11):** a felhasználó engedélyezte az ND-69 utáni lépést.
A mindig megmaradó statikus háromszögek eltakarhatják a finom völgyeket;
a levélhalmazra épülő chunk-diff pedig nem követi a kamerafüggő geomorphot.

**Döntés:** a CPU-s dinamikus út minden érintett base-tile alatt teljes
kvadfa-partíciót készít: a kiválasztó által kihagyott ágakat durva pótló
levelek fedik. Ezek nem új világadatok, ugyanazt a Core-mezőt mintázzák.
Csak az így teljesen kiváltott base-tile statikus terrain-indexei kapcsolhatók
ki, és csak a dinamikus mesh-ek sikeres feltöltése után. A base vertex-,
normal-, color- és submesh-adatok megmaradnak; az érintett indexintervallumok
degenerált háromszögekre cserélődnek, visszazoomnál az eredeti indexek állnak
vissza. Nincs shader/discard-maszk, globális mélységi bias vagy teljes base-remesh.

A renderelt közös sarok gazdája a legdurvább érintkező renderlevél, azonos
szintnél a legkisebb TileId. Finom–durva élen a finom pont a durva él két
feloldott végpontjából interpolálódik; a rekurzió csak csökkenő LOD-szinteken
haladhat. A morph coarse-felülete az `AddQuad` valódi 00–11 átlójú
háromszögpárja, nem bilineáris nyeregfelület. A statikus szomszéd ugyanennek
a közösél-szabálynak a base-szintű esete. A terrain radiális bias ezen az
úton megszűnik; a külön vízréteg meglévő viselkedése megmarad.

A chunkon belüli emit-sorrend stabil. A topológia-diff mellett az elkészült
csúcspozíciókat is összevetjük az utolsó sikeres feltöltéssel. Változatlan
topológiánál pozíció és bounds frissül, nem index/szín/material. Az async és
szinkron CPU-út közös előállítás/alkalmazás kódot használ. A kísérleti GPU-
geometria nem kap csendben részleges fedéscserét.

**Határ és kockázat:** a fedéspótlás és a közösél-feloldás többletmunka;
a feltöltési időkeret és a teljesen inkrementális emisszió még nincs lezárva.
A kamerafüggő morph továbbra is LOD-kérésenként, nem shaderben frame-enként
frissül. A víz/alap-border külön rétege nem része a terrain-indexcserének.
Nincs Core-/seed-változás. Kötelező a partíció-, index-visszaállítási,
közösél-/kockalapél- és morph-regresszió, Unity C# fordítás, majd élő kép/PerfLog.

### ND-71 — Mintavételezett domborzatot követő LOD-metrika

**2026-09-11, élő teszt UTÁNI státusz: aktív viewer-integráció visszavonva
az ND-72-ben.** A felhasználó nem látott érdemi élességnövekedést, viszont
súlyos lassulást mért. Az alábbi döntés a kísérlet történeti leírása.

**Kontextus (2026-09-11):** az ND-70 után a felhasználó a zoom/visszazoom
működését megerősítette, de kb. 13 görgetés után nem lát további élesedést.
A friss `PerfLog_20260911_144159.txt` szerint a kamera 114,855 után egészen
103,663 egységig közeledik és új cutok készülnek; nem igazolt hard zoom-stop.
A kiválasztó és a morph ugyanakkor még a 100 sugarú alapgömböt méri, nem
a megjelenített terepet. A pontos km-lépték külön backlog, a gyorsítás halasztva.

**Döntés:** a prioritásos CPU-út opcionális, testkoordinátás bounds-lekérdezést
kap. A viewer a négy tényleges, morph nélküli terepsarkot és a középpontot
mintázza a meglévő Core-mezőből, azonos tengerszint/relief skálával; ebből
számol középpontot és befoglaló sugarat. A láthatóság a teljes befoglaló gömböt
használja; a mintasűrűség és a morph az érintősíkbeli sarokkiterjedést méri,
az eltolt középponttól vett kameratávolsággal. A radiális magasságugrás nem
lehet szintfüggetlen tesszellációs hiba: a teljes 3D kiterjedést hibának véve
a valós mezős próba már 0,5 egység távolságnál L20 ágakra költötte a budgetet.
Ez mintasűrűségi cél, nem garantált háromszög-képernyőátmérő meredek falakon.
A kiválasztás és a geomorph ugyanazt a metrikát használja.
Az alapgömb horizont-/radiális backface-tiltása ezen az
úton nem alkalmazható: nem bizonyítja egy eltolt, lejtős patch láthatatlanságát.
A nézetkúp, pixelküszöb, hiszterézis, maxLevel és budget megmarad.
A mintapontok a meglévő sarok-cache-t használják, a bounds kérésenként cache-elt.

**Korlát:** ez a megjelenítés mintavételezett geometriai metrikája, nem a teljes
folytonos mező matematikailag bizonyított intervallumbecslése. Az öt pont
között rejlő, még nem mintázott csúcsot nem garantálja; szigorú hierarchikus
terrain-error bound későbbi munka. Nincs új zaj, Core-/seed-változás vagy
budgetemelés. A kamera alapgömbhöz kötött minimuma/felszínkövetése külön nyitott
lépés; az új metrika önmagában nem collision-megoldás. A PerfLog kapjon tényleges
legmélyebb LOD-szintet, hogy a hard limit és a látható modellrészlet elkülönüljön.
Kötelező: eltolt gömbös közelítési regresszió, ismételhetőség/budget,
morph-metrika egyezés, solution és Unity-fordítás, majd élő zoom-ellenőrzés.

### ND-72 — Az ND-71 regresszió visszavonása és renderoldali diagnosztika

**Bizonyíték (2026-09-11):** `PerfLog_20260911_172850.txt`, aktív ND-71:
122,161 / 118,144 / 106,675 kameratávolságnál a cut rendre 6820 / 6712 /
9584 ms; a teljes requestAge 8010 / 8738 / 11767 ms. Kérésenként 191 620 /
241 408 / 239 172 bounds, nadir L10 / L10 / L12. A magasságkiértékelés a
kiválasztás belső ciklusába került, még az óceáni szűrés és renderdöntés előtt.
A horizont-/backface-szűrés elhagyása tovább növelte a vizsgált tartományt.
Az ND-71 külön L14–L17 próbája nem a felhasználó kameráit mérte és nem
igazolta az elvárt látványjavulást. Az élő visszajelzés alapján a tradeoff hibás.

**Döntés, implementáció előtt:** az aktív CPU-kiválasztóból és morphból
eltávolítjuk az ND-71 domborzati callback/bounds-cache integrációját. Visszaáll
az ND-70 gömbös metrikája és korábbi cullingja. A fedéscsere, közösél-feloldás,
pozícióérzékeny upload, CPU-besorolás és budget-frontier marad. A tiszta
`SurfaceLodBounds` és opcionális API csak explicit offline kísérletként marad;
nem kerül alapértelmezett vagy Inspector-kapcsolós útra.

Az élesség további munkája előtt kérésenként **egyetlen nadír-diagnosztika**
készül: cut és tényleges terrain-renderlevél szintje, óceáni finomítás-tiltás,
helyi víz/land állapot, modellfelszíntől vett radiális távolság, morph-alfa,
a feloldott terrain-quad négy sarkának kéréskori vetített pixelátmérője.
Ez nem raycast és nem takarásvizsgálat: víz alatt a terrain-quad nem a látható
vízfelszín. Clip/near-plane keresztezésnél a pixelmérés érvénytelen, nem hamis
szám. A mátrix/viewport a kérés elején rögzített, Unity API nem fut workerben.
A diagnosztika külön időzített; legfeljebb egy új modellpontot értékel, nem
áganként terepet. A km-lépték továbbra is külön backlog.

**Következő lehetséges lépések:** (1) élő idő és kép ellenőrzése a helyreállított
úton; (2) renderhiba vs. modellrészlet vs. kamera/morph szétválasztása a
diagnosztikából; (3) indokolt domborzati finomítás esetén Build-kori hierarchikus
proxy/error-adatok, olcsó konzervatív előszűrés, majd csak érintett látható
ágak frissítése, azonos kamerás költség- és képi elfogadási kapuval.
Nem cseréljük a regressziót új, mérés nélkül aktivált heurisztikára.
Nincs új zaj, Core-/seed-változás vagy budgetemelés. A felbontási plafon
ettől még nem tekinthető megoldottnak; most a bizonyított regresszió javul.

### ND-73 — Korábbi első felosztás, rövidebb geomorph-átmenet

**Kontextus (2026-09-11):** az ND-72 után a felhasználó szerint valamivel jobb,
de későn indul a látható finomodás. A `PerfLog_20260911_182258.txt` szárazföldi
nadírja 149,319 távolságnál még L8 / 11,17 px; 140,379-nél már L9, de a morph
alfája csak 0,105. 133,060-nál az alfa még 0,388; a kész finom geometria
nagy része a durva felületre van visszahúzva. A diagnosztika olcsó (jellemzően
0,06–0,10 ms), a cut ismét tizedmásodperces, nem az ND-71 többmásodperces útja.

**Vizsgált, nem aktivált változat:** minden szinten 12→10 px csökkentés.
Azonos be-/visszazoom-próbában a csúcslevélszám 107 739→195 118 lett;
ezt a közel kétszeres többletet nem vállaljuk fel a korábbi regresszió után.

**Szűkített döntés, implementáció előtt:** csak a statikus base csomópont
első felosztásának célja legyen 10 px a scene jelenlegi 12 px-éhez képest.
A mélyebb szintek küszöbe 12 px marad, a budget 200 000. A merge-küszöb
változatlan (12/1,5=8 px), így a base tényleges hiszterézise 10/8=1,25;
visszazoomnál nem hosszabbítjuk meg az előre létrehozott levelek megőrzését.
Az első split-küszöbnek a merge-küszöb felett kell maradnia.
Az opcionális base-küszöb nélkül a LOD API bitre a korábbi viselkedést adja.
A base szülő morphja ugyanazt az új base-küszöböt használja, mint az osztás;
nem változhat csak a kiválasztó vagy csak a morph távolsága.
A geomorph tartománya 0,6→0,35: a teljes finom pozíció a split-távolság
65%-ánál elérhető a korábbi 40% helyett, születéskor továbbra is alfa=0.
Ez hangolási paraméter, nem fizikai konstans vagy új szimulációs algoritmus.

A scene szerializált mezői és a komponens alapértékei együtt frissüljenek.
Nincs új domborzati mintavétel/culling, zaj, Core-/seed-változás vagy nagyobb
budget. Aktiválás előtt azonos kamerás be-/visszazoom levélszám-ellenőrzés kell;
ha az elsőszintű előrehozás is aránytalanul drága, nem maradhat bekapcsolva.
A rövidebb morph snapshotok között észrevehetőbb átmenetet adhat: élő kép,
visszazoom és PerfLog-ellenőrzés nélkül nem vizuálisan kész.

### ND-74 — Build-kori radiális terep-proxy a LOD távolságához

**Állapot, implementáció előtt (2026-09-11):** a felhasználó a hátralévő
zoomfeladatokat sorrendben kéri, minden átadás után saját ellenőrzéssel.
Az ND-73 eredményét elutasította; a 18:39:49-es logban ugyanakkor 0,600 a
morph-range, a fájlban 0,35: az élő beállításeltérést külön ellenőrizni kell.

**Első, korlátozott lépés:** a már előállított statikus terepsarkok sugarából
immutábilis, sűrű `TerrainLodProxy` épül. A base alatti keresés a minták
maximumát összegző hierarchiát használja; base-től a helyi négy sugár bilineáris
interpolációja adja a tile-középpont becsült sugarát. A vízszint alatti proxy
a tenger sugarára korlátozott. A LOD és a morph ugyanazzal az eltolt
középponttal és sugararányosan skálázott patch-mérettel számol.
Ez mintasűrűségi/távolsági proxy, NEM új elevációmező, NEM bizonyított
képernyőhiba vagy a minták között rejlő terepcsúcs felső korlátja.

**Költség és élettartam:** level 8-on kb. 7 MiB állandó sugaradat; Buildkor
lineáris tömbfeldolgozás, nulla új Core-mintavétel. Zoomkor csak tömbolvasás
és interpoláció; nincs az ND-71-féle per-node magasságkiértékelés. Build és
cache-invalidálás cseréli/törli a proxyt. A worker kéréskori referenciát kap.
Base > 8 és GPU-geometria esetén explicit gömbös fallback, nem nagyobb rejtett
cache. A funkció külön kapcsolható összehasonlításhoz, a kapcsolóváltás
új cutot kér. Pixelcél, budget, kamera és világmodell változatlan.

**Határ:** az alapgömbös culling most megmarad; a láthatósági/parti garancia
a következő külön feladat. A proxy csak ezen az előszűrésen átjutó patch-ek
prioritását és osztását javítja. Az új út először offline, azonos kamerás
összehasonlítást kap (cut, levélszám, valódi modellhez mért proxyeltérés),
majd feltételes élő próba következik. Nem állítunk FPS- vagy képi sikert
Unity-visszajelzés nélkül. A teljes első tétel elfogadása ehhez kötött;
szigorú terrain-error bound továbbra is nyitott.

### ND-75 — A ténylegesen feltöltött tile-ok képernyőméretének naplózása

**Felhasználói kérés, 2026-09-11:** az ND-74 után a közepes zoom mintha
nem reagálna, mélyebben ismét finomodik, de a kép nem megfelelő. Most
kizárólag diagnosztika készül, nincs új LOD-, morph-, kamera- vagy budgethangolás.

**Mérési szerződés, implementáció előtt:** a sikeres Unity mesh-feltöltéshez
tartozó végleges vertex-/indexlisták megőrzött snapshotja a forrás, nem a
kért cut, a proxy vagy egy újonnan kiszámolt modellfelszín. A position-only
feltöltés és a statikus indexmaszk is követett. Csak aktív, engedélyezett,
a mérőkamerának szánt terrain/water rendererek kerülnek a snapshotba.
Az aktuális kamera és objektummátrixokkal, legfeljebb másodpercenként egy
háttérfeladatban mérünk, a LOD-worker állapotától függetlenül.

17×9 képernyőpontban a tényleges indexelt háromszögek CPU-s vetítése,
frustum-clippingje, winding-szűrése és mélységtesztje választja ki az
elöl levő tile-t. A tile a mesh négycsúcsos, kétháromszöges quadja.
Log: képernyőre vágott szélesség/magasság/átmérő pixelben; teljes vetített
quad-átmérő külön, ha érvényes; statikus/dinamikus terep és víz forrása;
mintaponttérkép, mintázott p50/p90/max. Ezek NEM minden tile-ra kiterjedő
statisztikák vagy globális maximumok. A középpont külön pontos mérete szerepel.
Zoom: tényleges középponttávolság, alapgömb feletti magasság, `R/(d-R)`
arány és a kamera meglévő nézetosztálya, FOV és viewport. Nincs kitalált
görgetésszám vagy LOD-célból visszakövetkeztetett tile-méret.

A kamera/mesh állapotának frame-je, ideje, mesh-revíziója, a LOD-kérés
folyamatban léte/kora és a diagnosztika saját ideje is logolt. Futó
háttérmérés snapshotja nem keveredhet frissebb indexekkel vagy vertexekkel.
Near-plane metszésnél clipping történik; a teljes, nem vágott quadméret
érvénytelen lehet, ezt külön jelöljük. A CPU-mérés nem GPU-pixel-visszaolvasás:
UI, felhő, marker, folyóvonal, transzparens blend, TAA és shaderbeli
vertex-mozgatás nincs modellezve. Nem adaptív/GPU-geometriai út explicit
nem támogatott, nem ad hamis érvényes mérést. Az aktuális felszínshaderek
nem mozgatják a vertexpozíciót. A teszt célja a feltöltött felszíni
geometria nagyságának és frissítési késésének elválasztása.

### ND-76 — Nézethez kötött, adagolt finomítás és valódi inkrementális emisszió

**Döntés implementáció előtt, 2026-09-11:** az ND-75 élő logban 180 px-es
középső tile és 2,5–10,6 s-os régi nézetű kérések látszanak. Álló kameránál
105,51 px-es statikus terep is marad. A felhasználó engedélyezte a javítást.

- A CPU/proxy út terepkiterjedést is tartalmazó boundsot és a tényleges
  kamera téglalap alakú perspektivikus frustumát használja. A régi alapgömbös
  horizont/backface-elutasítás ezen az úton nem előzheti meg a tereptesztet.
  A bounds kész base-sugarakból származik, nem új Core-mintákból; az ismert
  base-mintákat fedi, a finomabb valódi mezőre továbbra sem szigorú korlát.
  A bounds csak láthatóságra szolgál; az osztási méret a négy proxy-sarok
  tényleges perspektivikus vetülete. Az első offline próba elvetette a teljes
  befoglaló gömb pixelsugarát mint osztási hibát: súroló lapoknál túlosztott
  (159,34 távolságnál 109 319 levél a quad-metrika 7 045 levele helyett).
- Kérésenként korlátos számú új osztás, az előző felosztások megőrzésével.
  Kezdeti keret 1024 új osztás/kérés; a korábbi 256-os offline próbának túl
  sok hullám kellett. Ez nem milliszekundumos határ: balance és cache-miss
  további munkát okozhat. A főszálas upload továbbra is atomikus, nem streaming.
  A halasztott finomítás álló kameránál is folytatódik. A tile-budget és a
  maximális LOD nem nő. Az egyes publikált állapotok teljes fedést adnak.
- A változatlan topológiájú chunk teljes emitje csak akkor hagyható ki,
  ha a végleges, közösélekkel feloldott csúcspozíciói is egzaktul azonosak.
  A víz/border adatai is a cache részei; Build és konfigurációváltás invalidál.
- A lényegesen elavult kérés kooperatívan megszakítható. Egy sikeres
  alkalmazás előtt legfeljebb egy ilyen megszakítás engedett, hogy folyamatos
  mozgatás se éheztesse ki a megjelenítést. Cache-hez továbbra is egy worker fér.
- ND-75 megmarad összehasonlításra; a saját költségét ritkább mintavétel
  mérsékli. Új napló: feldolgozott/újrahasznált levelek, halasztott osztások,
  megszakítás és az új nézetmetrika aktív állapota.

Ez viewer-változás, nincs Core-, seed- vagy modellváltozás. A pixelcél
egységes hangolása és további optimalizáció csak a friss élő próba alapján;
a tesztek/Unity-fordítás nem helyettesítik a vizuális elfogadást.

### ND-77 — Tereptile-azonosság és kiválasztási megállás összekötése

**Döntés implementáció előtt, 2026-09-11:** az ND-76 élő próbában
(`PerfLog_20260911_205525.txt`, 20:56:23) befejezett finomítás mellett is
88,64 px-es dinamikus tereptile szerepel. Az ND-75 nem őrizte meg a tile
azonosságát és a konkrét kiválasztási megállást; a proxy/morph/szomszéd
hibaforrások között ebből nem lehet bizonyítékkal választani.

- A CPU terep konkatenált quadjai explicit TileId-t kapnak, az emissziós
  bucketek sorrendjében. A hozzárendelés nem vertex-pozícióból visszabecsült.
  Pozíciófrissítés és cache-újrahasználat megőrzi a hozzárendelést.
- A kiválasztás opcionálisan rögzíti a tényleges megállási okot, hibát és
  küszöböt. Csak sikeresen alkalmazott kérés trace-e kerül a snapshotba;
  a worker nem olvas Unity objektumot vagy változó cache-t a mérés során.
- A ritka ND-75 mélységtesztelt minták legnagyobb tereptalálataihoz
  mesh-azonosság, TileId, request-metrika, megállási ok és feltöltött
  sarokpozíciók kerülnek. A balance/fedés által létrehozott levél nem
  kaphat hamisan saját kiválasztási döntést: az őst külön jelöljük.
- A `lodPending=False` önmagában nem kész állapot: a napló a halasztott
  finomítás folytatását is jelzi. Mérési költség továbbra is külön látható.

Ez az engedélyezett 1. lépés bizonyítékgyűjtő része. A finomítás, morph,
víz, budget és Core változatlan; javítást csak az azonosított okra végzünk.
Új élő próba szükséges, ez önmagában nem felbontásjavítás.

### ND-78 — Későn eldobott tengerfenék-finomítás előzetes kizárása

**Döntés és méréssel korrigált terv, 2026-09-11.** Az ND-77 élő logban
(`PerfLog_20260911_212059.txt`, 21:21:17 és 21:21:53) álló kameránál,
befejezett kéréslánc mellett két statikus tereptile 28,816 / 31,678 px.
Becslésük 9,194 / 7,176 px, megállásuk `below-threshold`. A feltöltött
sarkok újravetítése mindkét értékpárt reprodukálja: a kis érték oka a mély
sarkok `max(seaRadius, cornerRadius)` helyettesítése. Ez méretdefiníciós
eltérés is: az ND-75 a teljes clipped quadot méri, a víz által takart
részeket nem vágja le a tile méretéből.

**Elvetett kezdeti változat:** a teljes nyers mélység használata a proxy
quadjában/boundsában. A mérés már 300-as távolságnál 17 ezer, közepesen
60–67 ezer tereplevelet adott, a régi 0 / ~1 ezer helyett, a későbbi
óceáni kizárást előrehozva is. A mély, részben víz alá nyúló partfalak
tömeges felosztása így nem arányos javítás. A kísérleti runtime-módosítás
visszavonva; a proxy, morph és távolságmetrika az ND-76 állapot marad.
A kísérlet a diagnosztikai próbában reprodukálható, nem éles feature.

**Átadásra választott részjavítás:** a renderer meglévő
`IsBaseAncestorOceanic` kizárása már a statikus alapszint kiválasztásánál
lefut, mielőtt leszármazottak vagy új osztási kvóta fogyna. Pontosan a
meglévő feltétel: víz alatti középpont és mind a négy base-sarok víz alatt.
Vegyes parti tile-t nem zárunk ki. A későbbi renderoldali szűrő megmarad.
A callback csak a base-szinten és csak a CPU/perspektivikus proxy-úton
aktív, a kész statikus besorolást/sarkakat olvassa, új Core-minta nélkül.

Az 1024-es munkakeret így a valóban megjeleníthető terepet szolgálja.
Új trace-ok: `renderer-base-exclusion`, `skippedSelectionBases`,
`earlyOceanExclusion=ND78`. A vízréteg, budget, pixelcél és seed változatlan.

Az offline, két nézetirányos, 14 állásos próbában az érdemben renderelt
terep TileId-halmaza azonos maradt. Egy közepes állásnál 11→8, mélyebben
14→13 hullám kellett. Ezek kiválasztási adatok, nem Unity frame-idők.
Az ND-77-ben állandó `split-quota` miatt váró levelek késésére célzott
részjavítás; nem oldja meg a 28–32 px-es teljes-quad eltérést, a statikus
víz durvaságát vagy a teljes látható-terep hibakorlátját. Élő próba kell.

### ND-79 — Korai zoom minőségi küszöbének költségvizsgálata

**2026-09-11, mérési döntés; nem runtime-javítás.** Az ND-78 utáni
`PerfLog_20260911_214108.txt` álló, befejezett közepes nézetében a
9,939 és 9,705 pixeles statikus tereptile proxyja pontosan ugyanekkora.
A `below-threshold` megállás 10 pixeles célt használ. A korai finomodás
hiánya itt igazolt minőségi küszöbkérdés, nem proxyhiba vagy workerkésés.

Az offline `--quality` próba kisebb, egységes céljai korábbi finomítást
hoznak, de a 6/5 pixeles cél egyik közepes állásában 2 930→96 969
dinamikus levelet és 728→23 640 L8-chunkot adnak. A globális küszöb
csökkentését ezért **nem aktiváljuk teljesítményelfogadás nélkül**.
Ez nem a 6/5 cél végleges elvetése: a jelenlegi feldolgozási/renderer
szerkezettel túl nagy regressziós kockázatot jelent.

Javaslat a következő implementációra: korlátos méretű hierarchikus
chunk-csomagolás, majd építési/upload-költségkorlát és a kisebb pixelcél
együttes validációja. A fix L6-chunk nem elegendő, mélyen ismét több
tízezer levelet vonna egyetlen újraépítésbe. A felhasználótól a megjelenítés
előzetes átalakítása és az azonnali, lassabb minőség között irányt kértünk.
A pontos algoritmus implementáció előtt külön döntést igényel.

[Logelemzés, összehasonlítás és korlátok](reviews/lod-onset-cost-analysis-nd79-2026-09-11.md).
A runtime, scene, seed és világmodell ebben a lépésben változatlan.

### ND-80 — Levélszámmal korlátozott hierarchikus renderchunkok

**Döntés implementáció előtt, 2026-09-11.** A felhasználó az ND-79
költségvizsgálat után előbb a megjelenítés átalakítását választotta.
Az első, külön átadandó lépés a CPU-terep csomagolása; a LOD-kiválasztás,
pixelcél, világmodell, morph és vízgeometria változatlan marad.

- A teljes fedés/közösél-feloldás után a leveleket a megadott legdurvább
  chunkszint ősei alá gyűjtjük (kezdőérték L6). Az ennél durvább bemeneti
  levél önálló marad. A 256 levélnél nagyobb csoport négy gyermekterületre
  oszlik rekurzívan; nem bontjuk magukat a terepleveleket.
- Stabil kulcs a chunk területének TileId-ja. Az előző sikeresen feltöltött
  partíció osztásait megtartjuk 128 levél felett; 128 vagy kevesebb levélnél
  engedünk összevonást. A 256 kemény korlát, a 128 hiszterézis, nem pixelcél.
  Üres csoport nincs; minden bemeneti levél pontosan egy csoportba kerül.
- A már működő topológia/pozíció-diff és emit-cache változatlanul a teljes
  csoportot kezeli. Split/merge esetén az új kulcsok felépülnek, a régiek
  kikapcsolódnak ugyanabban a főszálas alkalmazásban. Megszakított munka
  nem módosítja a publikált partíciót. A régi fix csoportosítás kapcsolóval
  elérhető összehasonlításhoz, a scene meglévő értékeit nem írjuk felül.
- Naplózzuk a csoportosítás idejét, a tényleges csoportméret-maximumot,
  a használt korlátot és a módot. A csoportszám nem GPU draw-call mérés.

A korlát egy chunk emissziójának/feltöltésének méretét fogja meg, nem egy
teljes kérés milliszekundumos költségét. Több kis chunk összevonása több
változatlan levél újraemisszióját is okozhatja; ezt az élő logból külön
ellenőrizzük. A főszálas upload több frame-re bontása, objektumpool és a
minőségi küszöb csökkentése nem része ennek az első lépésnek.

### ND-81 — Pontos vetület-cache az álló kamerás finomítási hullámokhoz (2026-09-11)

Az ND-80 második élő próbájában 31 946 levélből csak 64 igényelt új
emissziót, mégis 323,68 ms volt a teljes cut és 188,82 ms az emit szakasz.
Első, korlátozott lépésként a változatlan vetület ismételt kiértékelését
hagyjuk el; nem vezetünk be korábbi döntéseket megőrző kiválasztási frontot.

- Egyetlen workerhez tartozó cache tárolja a `(látható, szöghiba)` eredményt
  TileId-nként. A kamera pozíciója, normalizált tengelyei, vetítési és vágási
  paraméterei pontosan egyezzenek; a terep-proxy objektumazonossága kötelező.
  Kameraváltás, proxycsere, Build és nem perspektivikus mód érvénytelenít.
- Legfeljebb 262 144 bejegyzés; telítettségnél a hiányzó értéket továbbra is
  kiszámoljuk, csak nem tároljuk. A korlát nem változtathatja meg a cutot.
- A hiszterézis, prioritási sor, splitkvóta, óceánkizárás, trace és 2:1 balance
  továbbra is minden kérésben lefut. Nem tárolunk split/merge döntéseket.
  A geomorph ugyanebből a pontos metrikából olvashat a geometria workerében;
  a párhuzamos képernyődiagnosztika kizárólag az immutábilis view-t olvassa.
- Külön mérjük a selection/balance és geometria-feloldás/aux-másolás idejét,
  valamint a cache találatait és a valódi metrikaszámításokat.

A 12/10 px cél, az ND-80 chunkcsomagolás, a terepgeometria, a víz és a Core
változatlan. Ez az ismételt munka első csökkentése, nem a teljes késleltetés
megoldása. Azonos bemenetsorozatra azonos cutot és trace-t kell igazolni;
az élő gyorsulás és a vizuális eredmény külön felhasználói próbát igényel.

### ND-82 — Önálló vízfelszín-LOD, külön kiválasztási és renderkapu

**2026-09-11, döntés implementáció előtt.** A felhasználó az első zoomok
akadásának javítását backlogra halasztotta, és a következő tervezett tile-
feladatot kérte. A vízfelszín jelenleg a terep emissziójához kötött, miközben
a mély óceáni terep finomítását szándékosan kihagyjuk (ND-78).

1. Első, külön átadandó kapu: motorfüggetlen viewer-modul a víz saját cutjához.
   Bemenet a ténylegesen emittált statikus víz-base-tile-ok halmaza, a modellből
   származó, rendererrel azonos tengerszintsugár és a kamera. Nem a tengerfenék
   kihagyási maszkja, és nem új, közelítő óceán-/jég-besorolás.
2. Újrahasználjuk a tesztelt prioritásos kvadfát, pontos perspektivikus
   quad-metrikát, hiszterézist, splitkvótát, teljes fedést és közösél-resolvert.
   A vízhez külön állapot és külön levél-/munkakeret tartozik. A víz-proxy
   minden sugarán a megadott tengerszint áll; nem hívunk elevációt, klímát,
   hydrologyt vagy terep-emissziót. Új Core-algoritmus/seedváltozás nincs.
3. A part/alap vízmaszkja ebben a kapuban változatlan. Csak létező víz-base
   alatt keletkezhet gyermek. Minden lecserélhető base teljesen fedett;
   nincs részleges base-elrejtés. A fel nem osztott alap marad helyettesítő.
   A geometriai terv a finom és durva vízszéleket közös élre illeszti.
4. A következő kapu kötelező a runtime aktiváláshoz: víz-attribútumok hiteles
   forrása, vízre külön indexmaszk, a régi/dinamikus víz kettős rajzolásának
   megszüntetése, atomikus csere és visszaállítás. Ezt nem kapcsoljuk be
   egy pusztán tesztelt kiválasztó alapján. A GPU-kísérleti út és tavak saját
   vízszintje külön kompatibilitási ellenőrzést igényel.

Az első kapu önállóan tesztelhető, a futó viewernek nem ad új munkát és nem
változtat képet. A teljes vízfinomítás csak a második kapu és élő Unity-próba
után tekinthető késznek. A korai zoomküszöb és a halasztott ND-81 panasz
ebben a feladatban nem módosul.

### ND-83 — Önálló víz-LOD renderbekötése

**2026-09-11, döntés a kód előtt.** Az ND-82 második kapuja a CPU,
perspektivikus adaptív útba kerül, ugyanabba a single-flight workerbe.
Külön víz-cut: legfeljebb 8192 levél, kérésenként 256 új osztás, a meglévő
normál pixelcél/hiszterézis mellett. A korai terep-zoomküszöb változatlan.

- A statikus emit víz-bucketenként feljegyzi az emittált TileId-kat; ezekből
  készül a vízforrás és a ritka indexmaszk. Hiányzó (száraz) base nem rejthető
  el. A sugár a statikus renderer pontos float értéke.
- A víz geometriájához nincs tengerfenék-normál vagy biome-kiértékelés.
  A szín a nyers, morph nélküli modellmagasság és a meglévő ND-60/overlay
  színfüggvény eredménye; korlátos, Buildenként érvénytelenített cache.
  A pozíció és az RGB azonos topológiájú közösél-resolveren megy át.
- A statikus vízmaszk alatti terrain-vezérelt víz-emisszió kimarad;
  száraz base alatt a meglévő finom parti víz viselkedése megmarad.
  A tavak külön objektuma/vízszintje nem változik. A GPU és nem támogatott
  nézet a régi vízútra áll vissza, új vízmaszkolás nélkül.
- Két váltott vízobjektum: az új mesh teljes feltöltése után, egy főszálas
  alkalmazáson belül történik a statikus maszk és a látható objektum cseréje.
  Megszakított worker nem publikál. Uploadhibánál teljes statikus vízre
  állunk vissza, mindkét dinamikus vízobjektum kikapcsolásával.
- A víz külön selection/emit/upload ideje és levelei naplózottak. Az ND-75
  tényleges geometriamérése a maszkolt statikus vízquadokat nem számolja.
  A víz további osztásigénye álló kameránál is új munkakört indít.

A víz geometriai finomítása nem ígér új fizikai részleteket a modell
felbontásán túl. A több frame-es feltöltés, víz-geomorph és az első zoomok
halasztott hibája nem része ennek a lépésnek. Élő part/óceán/visszazoom és
paraméterváltás-próba kell a vizuális és teljesítmény-elfogadáshoz.

### ND-84 — Kamerafüggő, fizikai kilométer-lépték a megjelenített felszínen

**2026-09-11, döntés implementáció előtt.** A lépték nem görgetésszámból,
LOD-szintből vagy tile-méretből következik. Referenciapontja a kamera
viewportjának láthatóan megjelölt közepe. A kívánt képernyőszélesség két
végpontjából képzett sugarakat a viewer aktuális radiális felszínével metsszük:
szárazföldön a renderrel azonos, tengerszint körül túlrajzolt domborzattal,
vízen a renderelt tengerszintsugárral. A bolygó pozícióját és forgását az
inverz transzformáció kezeli; nem egységes skála esetén a kijelzés érvénytelen.

A két metszéspont normalizált iránya közti középponti szöget a Core kanonikus
`PlanetConstants.RadiusMeters` értékével szorozzuk. Így a kijelzett hossz a
referencia-gömb nagy köríve, nem a túlrajzolt terepen megtett út és nem
Unity-egység. A domborzat csak azt határozza meg, hogy a képernyősugár hol éri
el a látható felszínt. A felirat 1/2/5-ös „szép” m vagy km értéket mutat; a
hozzá tartozó pixelszélességet determinisztikus felezéssel oldjuk meg, ezért
a kerekítés nem válik pontatlan, lineáris pixelbecsléssé.

Ha a középső referencia környezetében bármelyik sugár nem metszi a felszínt,
a kamera nem perspektivikus, a szükséges modell-snapshot még nem él, vagy a
transzformáció skálája nem egységes, a panel explicit érvénytelen jelet mutat.
A számítás tisztán viewer/UI-oldali, a tile-kiválasztást, mesh-emissziót,
világmodellt és seed-kompatibilitást nem változtatja. A gömbmetszés,
nagy köríves távolság, szépérték-választás és megoldási hibakorlát külön
UnityEngine-független regressziókat kap. Élő Unity-próba kell az elrendezés,
a középjel és a teljes zoomtartomány ellenőrzéséhez.

Az első élő próba visszajelzése alapján egyetlen átmeneti felszínmetszési hiba
nem törölheti azonnal az utolsó hiteles értéket. A kijelzés nyolc egymást követő
sikertelen mintáig megtartja azt; biztos konfigurációs hiba esetén továbbra is
azonnal érvénytelen. A radiális iteráció tűrése a renderelt `float` háló
pontosságához igazodik és csillapítást használ a part-/domborzatperemeken.
Közeli kameránál, ha a gyors segédgömb-iteráció egy köztes sugár miatt feladja
a metszést, előjeles sugárparaméteres gyökkeresés adja az első tényleges
radiális felszínmetszést. Ez csak tartalék út, ezért a szokásos közepes és távoli
nézet mintavételi költségét nem növeli.

### ND-85 — Több képkockás terep-upload, egyetlen fedésváltással

**2026-09-12, döntés implementáció előtt.** Következő tile-feladat az
ND-83 után: a CPU async, chunkolt út terepfeltöltésének szétosztása.
Nem módosítjuk a cutot, a pixelcélt, a morphot vagy a Core-t.

- A kész workereredmény feltöltési tranzakcióvá válik. Legfeljebb 64 mesh
  és puha 2 ms keret jut egy Update-ra; legalább egy mesh elkészül, mert
  egy Unity uploadhívást nem lehet megszakítani. A 2 ms nem kemény FPS-garancia.
- Minden megváltozott/pozíciófrissített chunk inaktív tartalék Mesh-be kerül.
  Nem rajzolható és nem kerül a tényleges geometriadiagnosztikába. A normál
  position-only út ebben a módban teljes tartalékfeltöltés: több összmunka,
  cserébe a régi mesh a következő frame-ekben is változatlan marad.
- Az összes terepmesh elkészülte után külön frame egyben váltja a mesh-
  referenciákat, anyagokat, aux-rétegeket és statikus fedésmaszkokat.
  A cut/cache/diagnosztikai kamera csak ekkor válik publikálttá.
- Új worker nem indul staging közben. Világ-/LOD-konfiguráció-váltás,
  explicit Build és adaptív mód kikapcsolása eldobja a még nem publikált
  tranzakciót. Puszta kameramozgás nem éhezteti ki az uploadot: az elkészült
  kérés megjelenik, majd a következő kérés felzárkózik az aktuális kamerához.
- Staginghiba nem érinti a régi képet; commit-hiba a meglévő statikus
  fallbackot használja. A tartalék mesh chunkonként újrahasználható; a
  meglévő chunk-cache mellett legfeljebb egy további mesh/chunk marad.
  Build/komponens-megszűnés felszabadítja a tartalékokat. Ez többletmemória,
  nem általános chunk-cache memóriakorlát vagy objektumpool-javítás.
- Külön stageFrames/stageTotal/maxSlice/commit és feltöltött chunk/vertex
  mérés szükséges. A water/border upload, a maszk és aktiválás ebben az
  első kapuban még egyetlen commit-frame költsége. Ezek további bontása és
  a teljes főszálas keret csak élő mérés alapján következik.

Az új út kapcsolható, az alapérték bekapcsolt; szinkron/GPU/nem chunkolt
út változatlan. A vizuális elfogadás (lyuk, villanás, zoom/visszazoom,
világváltás) és az FPS-hatás élő Unity-próbát igényel.

### ND-86 — Víz és határvonal előkészített feltöltése

**2026-09-12, döntés implementáció előtt.** Az ND-85 élő próbájában
284 staging-adag p90 ideje 1,29 ms, maximuma 3,65 ms; 86 commit maximuma
15,83 ms. A külön vízpublikációs szakasz maximuma 10,71 ms (maszkolással
együtt, nem tiszta GPU-upload mérés). Következő kapu az aux-feltöltés.

- A dinamikus víz-bucketek konkatenálása és bounds-számítása a meglévő CPU
  workerre kerül. A sorrend, vertex/szín/normal és indexek nem változnak.
  A statikus/legacy szinkron vízépítést nem módosítjuk.
- A dinamikus parti víz, az önálló óceánvíz és a border külön staging-
  munkaként követi a terepet, ugyanabban a 2 ms/64 feladatos puha keretben.
  Üres/kikapcsolt réteg explicit üres eredményt kap; csak a commit rejti el
  az előzőt. Azonos önálló víz-cut továbbra sem kér új vízmesh-t.
- Az aux-mesh-ek sem kapnak renderert a feltöltés során. A commit csak
  mesh/anyag/diagnosztika-referenciát és láthatóságot vált, majd alkalmazza
  a fedésmaszkokat. A teljes terep+víz+border egy tranzakció.
- Rétegenként egy plusz tartalék mesh tárolható, Build/OnDestroy takarítja.
  Az önálló víz meglévő két bufferén felül egy közös tartalék használható.
  Staginghiba/megszakítás a régi képet hagyja; commit-hiba a meglévő statikus
  fallback. Nem változik a LOD-kiválasztás, színmodell, tavak vagy pixelcél.
- A vízkonkatenálás, aux-staging, terepcsere, legacy aux-publikáció,
  terrain/water maszk és cache-eviction külön mérhető lesz.

A natív meshhívások még nem részekre bontott bufferfeltöltések: egy nagy
víz/border job túllépheti a puha keretet. A maszk és a referenciaváltások
egy frame-ben maradnak. A 10,71 ms okának pontosabb elválasztását és az
új költségeloszlást élő loggal kell igazolni; FPS-javulás nem előlegezhető.

### ND-87 — Terep-rendercélok előkészítése és a publikálás felbontása

**2026-09-12, implementáció előtt.** Az ND-86 élő logban a 7,88 ms-os
commitból 6,30 ms a tereppublikálás; belső költségei még nincsenek külön mérve.
A felhasználó jóváhagyta e szakasz mérését és előkészítésének leválasztását.

- A meglévő staging-job a mesh mellett a rendercélt is előkészíti. Új
  GameObject még a komponensek felvétele előtt inaktív; meglévő célhoz
  staging alatt sem mesh-, sem anyag-, sem aktivitásmódosítás nem tartozik.
- A MeshFilter/MeshRenderer referenciája és a diagnosztikai adatburkoló
  előre készül. A publikált diagnosztikai térkép csak commitkor változik.
- Új célok a meglévő chunk-cache-be kerülnek. Commit előtti megszakításkor
  csak e kérés új céljai törlődnek; a korábbi cache és a látható kép marad.
  Commit-hiba után a meglévő statikus fallback takarítja a fedést, a célok
  a normál cache-életciklusban maradnak. Nem általános objektumpool.
- Külön idő: staging mesh/anyag, cél-/diagnosztika-előkészítés; commit
  mesh-/anyagcsere, diagnosztikai publikálás, aktiválás és eltűnő célok
  kikapcsolása. Új/újrahasznált célok száma is naplózandó.
- A közös terep/víz/border/maszk fedésváltás, puha 2 ms/64 job keret,
  a legacy út, Core, kiválasztás és zoomküszöb változatlan.

Az objektumkészítés dominanciája hipotézis; a részidők és új élő próba
igazolják a hatást. Egy job továbbra is túllépheti a keretet, a staging
összideje nőhet; a teljes ~669 ms-os kéréskésés megoldása nem e lépés célja.

### ND-89 — Előkészített terepfedés-maszk, változatlan GPU-publikálással

**2026-09-12, implementáció előtt.** A friss ND-87 logban 257 commit során
a tereppublikálás maximuma 1,98 ms; a 9,85 ms-os commitból 7,95 ms a maszk.
Nem ismert még e maszkidő CPU/natív bontása. Következő lépés csak a terepmaszk.

- A maszk következő állapota előkészíthető tranzakció: validált, másolt
  gyökérhalmaz, elrejtési/visszaállítási offsetek és rendezett upload-range-ek.
  Az előkészítés sem a jelenlegi indexeket, sem a rejtett tile-okat nem írja.
- Az ND-85–87 főszálas staging-sor végére kerül egy maszktervezési job, a
  következő diagnosztikai snapshot is ott készül. Nincs worker-hozzáférés
  az élő maszkhoz, teljes statikus indexbuffer-másolat vagy új GPU-mesh.
- A terv tulajdonoshoz és monoton revízióhoz kötött; idegen/elavult vagy
  már alkalmazott terv az indexek írása előtt elutasítandó. Eldobáskor nincs
  visszagörgetendő maszkállapot. A közös commit írja a CPU-indexeket és
  végzi a régi részleges SetIndexBufferData hívásokat, azonos tartományokkal.
- Változatlan fedésnél nincs új diagnosztikai snapshot és natív feltöltés.
  Legacy SetHidden megmarad prepare+apply kompozícióként; hibafallback
  továbbra is visszaállítja a statikus fedést. A vízmaszk út nem változik.
- Külön mérés: maszktervezés/snapshot előkészítése, CPU-alkalmazás, natív
  indexfeltöltés, snapshot-publikálás és range-szám. A 7,95 ms csökkenése
  csak élő mérésből állítható; a natív feltöltés még nem több frame-es.

A mask-job is monolitikus, túllépheti a puha 2 ms-ot. Az ND-87 25,76 ms-os
stageTarget-tüskéje külön nyitott kockázat, oka nincs izolálva. A teljes
kéréskésés, mozgókamerás selection és élességi/proxyhiba továbbra is backlog.

### ND-88 — Fizikai 1:1 függőleges relief a kilométer-lépték mellett

**2026-09-12, felhasználói vizuális visszajelzés alapján.** Az ND-84 pontos
vízszintes léptéke láthatóvá tette, hogy a scene korábbi domborzati beállítása
nem fizikai skálát használt. A `radius=100`, `elevationScale=0.001` és
`terrainReliefExaggeration=1.5` együtt a kanonikus 7 420 km-es bolygón
111,3-szoros függőleges túlrajzolást jelentett. Emiatt egy körülbelül 1,8 km-es
modellbeli tektonikus perem nagyjából 200 km magasnak látszhatott a léptékhez
viszonyítva.

A viewer új `usePhysicalReliefScale` kapcsolója alapból igaz. Ebben a módban
az `elevationScale = radius / PlanetConstants.RadiusMeters`, a külön relief-
szorzó pedig 1; a `Start` és az Inspector-változások `OnValidate` útja is
szinkronizálja a szerializált értékeket. Kikapcsolva a korábbi művészi skála
és függőleges túlrajzolás továbbra is használható. Ez kizárólag viewer-
geometriai változás: a Core elevációt, tengerszintet, hidrológiát, seedet és
world package-et nem módosítja.

A mérés egy másik, valódi modellhiányt is feltárt: a deep-time erózió csak a
legfeljebb 1500 m-es lemezhatár-upliftet relaxálja, miközben az óceáni
(-4000 m) és kontinentális (+800 m) kéregbázis a legközelebbi lemez ID-jével
diszkréten válthat. Ennek folytonos átmenete külön, seed-kompatibilitást és
referenciavektorokat érintő Core-döntés; az ND-88 nem rejti el rendereroldali
clamp-pal. Előbb az 1:1 megjelenítést kell élő Unityban ugyanazon a helyen
ellenőrizni, utána a megmaradó fizikai peremmagasság mérhető és kalibrálható.

### ND-91 — Helyi modellfelszínt követő orbitkamerakorlát, első kapu

**2026-09-12, implementáció előtt.** Az upload után a felhasználói sorrend
következő önálló tétele a felszínkövető kamerakorlát. A ND-89 logokban a
natív terepmaszk maximuma 0,59 ms; további upload-átalakítást ebből nem
indokolunk. A különböző relief/viewport miatt nincs kontrollált A/B mérés.

- A kamera a nadír irányában az ND-84 meglévő modellfelszín-lekérdezését
  használja (tenger vagy túlrajzolt szárazföld), nem új Core-algoritmust.
  Pozitív, egyenletes target-skálát támogat; más transzformációnál fallback.
- A zoom és forgás helyi felszín feletti magassággal arányos. A minimális
  rés a korábbi `minDistance - surfaceRadius`, legalább 0,001 világ-egység
  és a near clip 1,1-szerese. Ütközéskor kifelé korrekció történik; ha a
  maxDistance ennél kisebb, a felszínkorlát élvez elsőbbséget.
- A korlát minden transzformációra érvényes, beleértve a FlyTo-t és LateUpdate
  újraellenőrzését. Nem módosítjuk a near clipet, scene-t vagy reliefet.
- Pontosan azonos lokális irány + világ-snapshot esetén cache-találat;
  puszta zoom nem indít ismételt modellmintavételt. Konfigurációváltás alatt
  nincs kevert modellminta; átmenetileg az előző sugár/alapgömb a fallback.
- Kapcsolható, alapból aktív. Külön log jelzi a modell/fallback forrást,
  mintavételi időt, tényleges távolságot, magasságot és korrekciót.

Ez **első, pontmintás kapu**, nem teljes rendergeometriai ütközésgarancia:
a durva/morpholt háromszög, meredek oldal és a near-plane sarkainak
ütközése eltérhet a nadír modellmagasságától. Tavak külön vízszintje sem
része az ND-84 lekérdezésnek. Ezek, a forgás közbeni mintavételi költség és
az élő zoom/FlyTo elfogadás nyitott. Nem tile-élesség- vagy selection-javítás.

### ND-90 — Folytonos vegyes kéregátmenet és 1 km-es uplift-plafon

**2026-09-12, döntés implementáció előtt, felhasználói élő megfigyelés
alapján.** Az ND-88 megszünteti a 111,3-szoros megjelenítési túlrajzolást,
de a Core elevációmezőjében is van falszerű lemezhatár. A legközelebbi lemez
ID-jének átbillenésekor az óceáni és kontinentális kéreg teljes alap-elevációja
(-4000/+800 m bázis és eltérő zajamplitúdó) egyetlen pontban válthat. A
deep-time erózió eddig csak a külön uplift-bónuszt relaxálta, ezt a lépcsőt
nem érintette.

A két legközelebbi lemez gap értékét a báziselevációhoz is felhasználjuk.
Csak eltérő kéregtípusnál, `0.005` gap-határzónán belül a nyertes és
második lemez ugyanazon pozícióban számolt báziselevációját keverjük. A második
lemez súlya a határon 0,5, a zóna külső szélén 0; a kettő között polinomiális
smoothstep fut. Így a két oldal ugyanahhoz az átlaghoz tart, a zóna szélén a
meredekség is folytonos, és nincs új transzcendens művelet. Az `isOceanic`
továbbra is a legközelebbi lemez anyagtulajdonsága; a keverés csak az
elevációt folytonosítja. A sáv szándékosan keskenyebb a `0.04` uplift-zónánál:
elég széles a pontszerű 4,8 km-es lépcső megszüntetéséhez, de kevésbé írja át
a kontinens- és fix víztérfogat-topológiát.

A kanonikus level-6 fix víztérfogat-próba 250 Myr-nél 95,036% vizet ad.
Ez a korábbi 95%-os durva összeomlásőr határát mindössze 0,036 százalékponttal
lépi át, miközben a 0 szélességű (diszkontinuus) változat átmegy. A kapu felső
határa ezért dokumentáltan 96%-ra módosul; nem termékcél vagy célzott vízarány,
csak annak őre, hogy a megőrzött víztérfogat ne omoljon 100%-os borításba.

A tektonikus „felgyűrődés” külön komponensének felső korlátja 1500 m-ről
1000 m-re csökken. Ez pontosan érvényesíti a felhasználó által kért legfeljebb
1 km-es upliftet; a teljes felszíni eleváció természetesen lehet magasabb a
kontinentális alap és a valódi domborzati zaj miatt. Nem alkalmazunk
tengerszinthez kötött renderer-clampet, mert az elrejtené a modellhibát és
szétválasztaná a render/panel/hidrológia forrását.

Ez numerikus világkép- és seed-kompatibilitást törő módosítás. A `.worldpkg`
formátum 2-re emelkedik és az 1-es csomagok betöltése explicit hibát ad. A
Python referencia az elsődleges orákulum; a crust/plate-boundary/deep-time,
hidrológia, tavak, folyók, feature, vulkanizmus és state-hash downstream
vektorait újra kell generálni, majd byte-szinten ellenőrizni. Külön regresszió
igazolja a határon vett kétoldali folytonosságot, az 1 km-es uplift-plafont,
a cache-elt/nem cache-elt út bitazonosságát és a világplauzibilitást. Élő Unity
vizuális elfogadás továbbra is szükséges ugyanazon problémás peremnél.

**Implementáció utáni ellenőrzés (2026-09-12):** a teljes Python downstream
lánc újragenerálva; a level-5 lemezhatár-minta 16,4%-a kap upliftet, mért
maximuma 726,6 m, míg a szintetikus határteszt egzakt 1000 m-es plafont
igazol. A kanonikus level-6 világ 31 kontinenst és 421 legalább öt tile-os
régiót ad, a state hash
`dd685aac9df276053fcb6bb58cffa38100a725961065d0a7688b1aff19472f12`.
A referencia- és tesztpéldányok bájtra azonosak; Core 384/384, CLI 8/8 és
viewer-LOD 290/290 teszt zöld, a Release solution build 0 warning/0 error.

### ND-92 — Teljes statikus terepmaszk-helyreállítás commit-hibánál

**2026-09-12, javítás előtt.** Az ND-89 `ApplyPrepared` már átírja a CPU
indexeket és a rejtett halmazt a részleges natív feltöltés előtt. Ha a
feltöltés félbeszakad, a GPU még olyan régi rejtett quadot tartalmazhat,
amelyet a CPU már visszaállítottnak tekint. A korábbi `SetHidden(empty)`
hibaág ezt nem küldi újra: három eltérő megszakítási ponttal reprodukált
tesztben maradnak degenerált GPU-indexek a statikus fallbackon.

- Csak a commit hibaágában explicit `RestoreAll` állítja vissza a teljes
  CPU-indexbuffert az eredeti másolatból, üríti a rejtett halmazt és új
  revízióval érvényteleníti a függő terveket. A visszaadott teljes tartomány
  akkor is feltöltendő, ha a CPU szerint már semmi sem rejtett.
- A viewer ezt egy teljes natív indexfeltöltéssel publikálja. A diagnosztika
  csak a sikeres feltöltés után válik teljesen láthatóvá. A szokásos sikeres
  commit részleges feltöltése és annak költsége nem változik.
- A vízmaszk ugyanazt a CPU-előbb / részleges GPU-utána protokollt használja,
  ezért a közös commit hibaágában az is `RestoreAll`-t kap. A két réteget
  egymástól függetlenül próbáljuk helyreállítani; egyik hibája nem állítja
  meg a másik kísérletét. A víz normál commitja változatlan.
- Ha a helyreállítás is hibázik, a félkész dinamikus célok kikapcsolása és
  a korábbi cut-cache érvénytelenítése akkor is lefut; az eredeti és a
  helyreállítási kivétel együtt megmarad. Tartós natív hiba esetén nincs
  helyreállítási garancia vagy sikeres statikus fedést állító diagnosztika.

Ez nézetoldali hibabiztonság, nem a normál zoomélesség/késés javítása.
Core, relief, kamera és kiválasztási küszöb változatlan. A natív integráció
Editor-tesztje külön futtatást igényel; a szimulált bufferpróba nem GPU-teszt.

### ND-93 — Nem használt terep-renderer cache és mesh-életciklus

**2026-09-12, implementáció előtt.** A felhasználó 2–3 feladat együttes
átadását kérte. Első tétel a nyitott rendercache-erőforráskockázat: a
`RemovedChunkRoots` csak kikapcsolta az objektumokat, a chunk- és spare-
térkép világváltásig növekedhetett. GameObject törlése önmagában nem
helyettesíti az általunk létrehozott runtime Mesh explicit felszabadítását.

- Legfeljebb 128 nem használt chunk-kulcs megtartása a cél. A legrégebben
  használaton kívülivé vált kulcsok kiürítése puha 0,5 ms / 8 kulcs/frame;
  ez nem azonnali kemény memóriakorlát. Aktív/publikált fedés nem eviktálható.
- Staging közben nincs cache-takarítás; worker közben kizárólag a Unity-
  erőforrásokat érinti, a worker által olvasott CPU chunk-cache-t nem.
  Újrahasználat kiveszi a kulcsot az inaktív sorból; megszakítás visszateszi
  a nem publikált, már nem szükséges tartalékokat. Nincs teljes frame-enkénti
  objektumtérkép-bejárás vagy rendezés.
- Eviction eltávolítja a célobjektumot, saját runtime mesh-eit, tartalékát
  és diagnosztikai hivatkozását. Külön tulajdonosi mesh-nyilvántartás védi
  a megosztott asseteket, és Build/OnDestroy felszabadítja a saját mesh-eket
  akkor is, ha egy célobjektum korábban már eltűnt.
- Darabszám/eviction-idő naplózott; nem állítunk ezekből pontos GPU-byte-
  költséget. Az aktív fedés, CPU-mintacache és aux-mesh memóriája külön marad.

Várható hatás: hosszú területváltások után kevesebb megőrzött inaktív
geometria. Kiürített területre visszatérés új mesh-t/objektumot készíthet,
ami többletköltség; vizuális és memóriahatás élő próbával igazolandó.

### ND-94 — Külön ütemezett terepmesh és rendercél-előkészítés

**2026-09-12, implementáció előtt.** A csomag második feladata az ND-87
monolitikus terep-job két részre bontása. Az ismert 7,10 ms-os staging-
csúcs és a korábbi célkészítési tüskék nem tekinthetők megoldottnak.

- Chunkenként mesh-feltöltés/anyag-előkészítés, majd külön rendercél/
  diagnosztikai burkoló készül. A két lépés között is érvényesül a meglévő
  2 ms puha időkeret; a normál mesh API-hívás továbbra sem megszakítható.
- A legfeljebb 64 chunk/frame elméleti átvitelt 128 részfeladat/frame
  tartja meg. Aux/maszk a sor végén marad, közös commit külön Update-ban.
  Félkész mesh vagy cél nem kerül a rendererbe.
- Megszakítás a fél pár után is biztonságos. Részfeladattípus és tényleges
  legdrágább job-idő kerül a slice-logba, nem feltételezett terhelés.

Ez finomabb főszálas ütemezés, nem csökkenti szükségszerűen az összmunkát,
és nem oldja meg a mozgókamerás selectiont vagy az élességi küszöböt.
Core/relief/scene és a kiválasztott tile-fedés mindkét tételben változatlan.

### ND-95 — Három következő mérési/kamera/lépték korrekció

**2026-09-12, implementáció előtt.** A felhasználó három következő feladatot
kér egy csomagban. A korai élesség/selection halasztása megmarad.

1. A kérésdiagnosztika `Stopwatch` monotón időbélyeget kap: kickoff,
   worker belépés/kész, főszálas átvétel, commit eleje/vége. A frame-hez
   kötött `Time.unscaledTime` nem mérhet tiszta kéréskort a Build közben.
   Sorban állás, worker falióra, kész eredmény várakozása, staging falióra
   és commit külön jelenik meg. A scheduling/cancellation küszöb nem változik.
2. A km-lépték hitelessége kamera/target transzformációhoz, vetülethez,
   viewporthoz és világ-revízióhoz kötött. A nyolc hibányi türelmi idő
   csak ugyanazon mérési helyzetben őrizhet értéket; változáskor azonnal
   érvénytelen. A közös felszínlekérdezés visszautasítja a függő világváltást,
   nem keverhet régi snapshotot új relief/radius mezőkkel. Másik targethez
   nem használhatjuk az előző vagy egy tetszőleges bolygó modelljét.
3. A FlyTo interpoláció a helyi felszín feletti magasságot viszi át,
   az út közben is az aktuális irány sugarával. Nem egyszer előre mért
   cél-sugárból interpolált abszolút középponttávolságot. A min/near/max
   korlát, kézi megszakítás és a kapcsolható alapgömb mód megmarad.

Ez nem új Core/numerikus világmodell, nem teljes mesh-kamera ütközésvédelem,
nem relief- vagy tile-küszöbhangolás. Tesztelés: mesterséges időbélyeges
fázisösszeg, valódi viewer metódusok Editor-fixture-rel, meglévő regressziók;
offline fordítás nem helyettesít natív/vizuális ellenőrzést.

### ND-96 — Teljes tile/zoom zárócsomag, köztes kézi kapuk nélkül

**2026-09-12, implementáció előtt.** A felhasználó a checkpoint commit
után a fennmaradó teljes tile/zoom munkát kéri, egyetlen végső kézi próbával.
Ez feloldja a selection és korai élesség korábbi halasztását, nem bővíti
a feladatot új Core-mikrodomborzattal vagy a teljes M13-mal.

- Kamerafüggetlen proxy-geometria cache megőrzése nézetváltáskor; a
  vetített metrika továbbra is az aktuális kamerával számolandó. Korlátos,
  egy worker tulajdonú cache, világváltáskor eldobva.
- A kész CPU-terepquadok visszacsatolása a következő kiválasztáshoz,
  új Core-minták nélkül. Az ismert terep kiterjedése az ősi cull-boundsot
  is bővíti; a kész quad vetülete nem maradhat kisebb proxy mögé rejtve.
  Új geometriai információ revízióváltással érvényteleníti a metrika-cache-t.
  A korrekció progresszív és korlátos, nem folytonos terepmaximum-bizonyíték.
- Balance: a kettős szomszédbejárás helyett egyszeri, determinisztikus
  bejárás, változatlan rendezett split-sorrend és budget. Páros regresszió.
- A korai minőség és a kész geometria ellenőrzése, kamera/víz/lépték/
  upload életciklus regresszió és egységes végső próbalista egy csomag.
  A natív/élő bizonyítékot külön kell kezelni a CLI-fordítástól.

**Végrehajtás és visszamérés:** [ND-96 csomag és teljes próbalista](reviews/lod-final-batch-nd96-2026-09-12.md).
A geometria újrafelhasználása a víz külön workerében is működik. Az emit
megkapja a már feloldott saroklistát. Az Inspector 1-es/nem véges merge-
faktora érvényes, pozitív hiszterézist ad. A végső pixelcél 8/7: a 8/6
drágább korai hullámai miatt a base-célt 7-re módosítottuk. A mindenhol
nyers mélységű proxy kísérletét 200 ezres túlosztás miatt visszavontuk.
Az új cache gyorsulása nem egyenlő a sűrűbb teljes kép gyorsabb elérésével;
az offline költségtáblázat és a végső élő kapu ezt külön kezeli.

### ND-97 — Metrikatároló újrahasználata és helyi feedback-érvénytelenítés

2026-09-12, implementáció előtt. A felhasználó kézi próba nélkül kér
folytatást. Az ND-96 után is minden kameraváltás új metrika-szótárat épít,
és minden egyes feedback-rekord az egész szótárat törli. A következő két
költségcsökkentés változatlan 8/7 pixelcéllal és tile-/split-keretekkel:

- Explicit, egy worker által használt nézet-reset megtartja a szótár
  tárolóját, de minden régi vetített értéket töröl. A meglévő, új cache-t
  készítő `Reproject` megmarad referenciának; a runtime az új resetet használja.
  Régi request nem használhat tovább egy következő nézetre resetelt cache-t.
- A geometria-revízió tile-onként, a visszacsatolt tile és ősei mentén
  érvénytelenít. Független ág metrikája érvényben marad. Egy bejegyzés
  vetületváltás után továbbra sem használható, geometriafrissítés után
  pedig csak azonos helyi revíziónál cache-találat.
- Pontos metrika-/cut-/split-egyezés, korlát- és nullallokációs próba,
  valamint páros offline idő/allokációmérés szükséges. Nem ígérünk ebből
  teljes FPS- vagy vizuális elfogadást; nincs Core-/relief-változás.

Harmadik, méréssel azonosított részfeladat: a `SurfaceQuad` örökölt
értéktípus-egyenlősége 1000 azonos feedbacknél 1368000 byte-ot allokált a
Release reprodukcióban. A cache helyi, komponensenkénti, tolerancia nélküli
összehasonlítást kap; a Core és az általános quad-típus nem változik.

Átadás: [ND-97 mérések és tesztbizonyíték](reviews/lod-cache-allocation-nd97-2026-09-12.md).
A közös későbbi kézi próbalista megmarad; nincs új minőségi cél vagy
elfogadottnak jelentett FPS-eredmény.

### ND-98 — Igazoltan stabil vízkiválasztás újrahasználata

2026-09-12, implementáció előtt. A terep további finomítási körei jelenleg
változatlan kameránál a már stabil víz-cutot is újraépítik. Az immutábilis
vízforrás és az előző eredmény mellett explicit újrahasználatot vezetünk be:

- Csak azonos teljes vetület, maxLevel, split-/merge-küszöb, levélkeret és
  splitkvóta mellett; forrásváltás továbbra is új kiválasztást igényel.
- Nem elég a `DeferredSplits == 0`: egy további teljes kiválasztásnak azonos
  rendezett leveleket kell adnia. Ez igazolja a determinisztikus fixpontot,
  a hiszterézis és a kiegyensúlyozás utóhatásait is figyelembe véve.
- A megosztott fedés immutábilis; az új kérés statisztikái nullázottak,
  külön `selectionReusePolicy=ND98 reusedSelection=True` naplójelöléssel.
  Nincs régi munkaidő újramérése.
- Az argumentum-/cache-validálás és a megszakítás ellenőrzése megelőzi a
  gyors utat. Külső geometriafeedbacket tartalmazó cache-nél nincs gyors út.
- Kikapcsolható újrahasználattal páros, pontos eredmény-összehasonlítás és
  álló/módosuló nézetes regresszió igazolja a változatlan fedést. Ez csak a
  vízkiválasztás költségét csökkenti; nem teljes FPS- vagy vizuális igazolás.

### ND-99 — Fenntartva: a testvérág ND-62-je (tengeri jég) merge-kor ezt a számot kapja

**2026-09-13, azonosító-ütközés feloldása.** Az ND-62 azonosító két ágon
két különböző döntést jelöl:

| Ág | ND-62 tartalma | Utána következő fővonali döntések |
|---|---|---|
| `codex-handoff` (fővonal) | Kameramód-kapcsoló, „Tengelyforgás megfigyelése” | ND-63–98 erre épülve, commitolva |
| `experiment/full-temperature-model` (`e93b909`, nincs merge-elve) | Tengeri jég: mélységmodulált fagyási küszöb, folytonos sarok-blend | nincs |

**Döntés:** a fővonal megtartja a kameramódos ND-62-t, mert arra 36 további,
már commitolt döntés és számos dokumentum hivatkozik. A testvérág tengeri-jég
döntése merge vagy cherry-pick esetén **ND-99** számot kap; a merge-commitban
az ottani fejléc, a backlog-sor és a kódkommentek `ND-62` hivatkozásait
ND-99-re kell írni. A testvérág ND-61-e a fővonalon nem létezik, ezért
változatlanul megtarthatja a számát.

A pillanatnyi hőmodell terve (backlog 24. döntés) a testvérág releváns
Full-klímabázis-munkájának átvizsgálását kérte. A közös ős (`4b3ac02`) óta az
ág `src/`, `tools/reference/` és `tests/` alatt egyetlen Core-változást hoz:
`Temperature.TemperatureKelvinFullPrecomputed`. Ez a pozíciófüggetlen
`tGreenhouse` és `tCycle` tagot kívülről fogadja, az eredeti
`TemperatureKelvinFull` bitazonosan erre delegál. Nem új fizika, csak
hívásonkénti ismételt számítás megszüntetése. Az ND-100 lassú klímabázisa
ugyanezt a mintát követi (snapshotonként egyszer számolt pozíciófüggetlen
tagok); ha a C#-ban szükséges, a függvény átvehető, de a merge nem feltétele
az ND-100–104 munkának.

Nem seed-törő, nincs kódváltozás. A testvérág vizuális tesztje a felhasználó
2026-09-12-i döntése szerint jelenleg nincs napirenden.

### ND-100 — Pillanatnyi kétállapotú hőmodell: lassú klímabázis + felszín- és levegőanomália

**2026-09-13, implementáció előtt. Az irány a felhasználó által 2026-09-11-én
jóváhagyva** ([backlog döntésjegyzék](backlog.md) 1, 9, 13, 14, 16, 17, 18,
26, 27, 28. pont). Ez a bejegyzés rögzíti a modellt; a numerikus paraméterek
nyitottak, csak forrás + Python-referencia + mérés után kerülhetnek be.

**Kérdés.** A `Temperature.TemperatureKelvin` és `TemperatureKelvinFull` a
radiatív tagot egy teljes forgás 24 mintájának átlagából számolja, ezért a
nappali és éjszakai oldal ugyanazon a napon azonos értéket kap. A felhasználói
cél pillanatnyi, hőtehetetlenséggel késleltetett nappal/éjszaka-hőmérséklet,
amelyet a szél is szállít. Hogyan épüljön ez a meglévő klímára anélkül, hogy a
napi átlagos besugárzást kétszer számolnánk?

**Opciók:**

| Opció | Előny | Hátrány |
|---|---|---|
| A: Teljes hőmérséklet-állapot saját energiamérleggel, a bázis nélkül | Egyetlen konzisztens egyenlet | Újrakalibrálandó a meglévő §28.1 egyensúly, üvegház, lapse rate és ciklus; a biome-/jég-kalibráció elszakad |
| **B: Lassú bázis (`Bs`, `Ba`) + gyors anomália (`θs`, `θa`)** | A meglévő kalibrált klíma marad az egyensúly; a solver csak a napi eltérést integrálja | A két réteg határát pontosan kell definiálni (mi van a bázisban, mi az anomáliában) |
| C: A meglévő napi átlagot pillanatnyi `max(0, n·s)`-re cserélni | Kevés kód | Nincs hőtehetetlenség, éles terminátor; ellentmond a felhasználói célnak |

**Javaslat / jóváhagyott irány: B.**

- `Ts = Bs + θs` (felszíni skin-hőmérséklet), `Ta = Ba + θa`
  (felszínközeli levegő). Mindkettő Kelvin, `double`.
- Referenciaegyenletek (egységek: `J m⁻² K⁻¹`, `W m⁻²`, `K`, `s`):

  ```text
  Cs(kind) · dθs/dt = ΔQsolar − λs·θs − ksa·(θs − θa)
  Ca       · dθa/dt = ksa·(θs − θa) − λa·θa + advekció (ND-102) + keveredés
  ΔQsolar  = F·(1 − albedo(kind))·( max(0, n·s(t)) − dailyAverageFactor )
  λs       = 4·ε·σ·Bs³      (csak szorzás; a bázis körüli linearizálás)
  ```

- A `dailyAverageFactor` ugyanaz a 24 mintás függvény, amelyből a bázis
  radiatív tagja készül, így a napi átlagos elnyelt sugárzás csak a bázisban
  szerepel. Az `n` az ND-02 szerinti level-6 cella gömbi normálja (27. pont);
  lejtő és hegyárnyék későbbi mikroklíma-réteg.
- `s(t)` kizárólag `OrbitalMechanics.SunDirectionBodyFrame`-ből jön, amely
  `DeterministicMath.SinCos`-t használ. A solver kritikus útján nincs nyers
  `Math.Sin/Cos/Exp/Pow`; a `Bs³` és minden együttható szorzás/osztás.
- A procedurális `T_weather` kimarad a bázisból (14. pont), a felhőproxy nem
  árnyékol (17. pont), a jég albedója/hőtehetetlensége olvasható, de a jég nem
  olvad és nem nő visszacsatoltan (16. pont).
- Magas render-LOD-on egyszeri, determinisztikus magasságkorrekció:
  `T(vertex) = T(cella) − Γ·(h(vertex) − h(cella))`. A cella saját magassága
  csak a bázisban szerepel lapse rate-tel, így nincs kettős alkalmazás (18. pont).
- A klímabázis pozíciófüggetlen tagjait (`tGreenhouse`, `tCycle`)
  snapshotonként egyszer kell számolni (ND-99 átvizsgálás).

**Nyitott kérdések — a Python-referencia és forrásolt értékek döntik el:**

| # | Kérdés | Javaslat, amit mérni kell |
|---:|---|---|
| 1 | `Bs` és `Ba` viszonya | Első változatban `Bs = Ba = TemperatureKelvinFull` a weather tag nélkül; külön skin–levegő offset csak forrásolt értékkel |
| 2 | `Cs` felszíntípusonként | Óceán: kevert réteg `ρ·cp·h`; szárazföld: napi hőbehatolási mélység; édesvíz és jég: forrásolt mélység/anyagérték. Mindegyik tartománnyal és hivatkozással |
| 3 | `Ca` (felszínközeli levegőoszlop) | `ρair·cp·H`, a határréteg-vastagság forrásból |
| 4 | `ksa`, `λa`, `ε` | Forrásolt nagyságrend; `λa` newtoni relaxációs időállandóként |
| 5 | Jég és édesvíz albedója | A meglévő `AlbedoOcean`/`AlbedoLand` mellett forrásolt `Ice`, `Freshwater` érték |
| 6 | A tickben mintázott `max(0, n·s)` napi átlaga eltér-e mérhetően a 24 mintás `dailyAverageFactor`-tól | **Mérve (2026-09-13, `tools/reference/thermal_anomaly_column_ref.py`):** 3600 s-os ticknél a mintaátlag a 24 mintás faktorral pontosan egyezik; 1800/900 s-nál 0,8·10⁻³ … 1,5·10⁻³ az eltérés (a 24 mintás átlag kvadratúrahibája), ami ~1–2 W m⁻² és a kiépült napi átlagos `θs`-ben ≤ 0,14 K. Elfogadható, ha a bázis és az anomália ugyanazt a 24 mintás faktort használja; nem nő időben |
| 7 | Évszakos bázisváltozás kezelése a solver alatt | **Mérve:** egyszer, a kezdőnapra rögzített faktorral 30 nap alatt a napi átlagos `θs` 45°-on +2,3 K (óceán) / +8,3 K (szárazföld) — évszakos drift. Naponta, a nap elején újraszámolt faktorral ≤ 0,14 K, óránként, középre igazított 24 mintás ablakkal és lineáris interpolációval ≤ 0,09 K. **Javaslat: az óránként középre igazított faktor**; a bázis (`Bs`, `Ba`) frissítése ugyanehhez az órás rácshoz igazodjon |

**Elfogadási feltétel (Python, még C# előtt):** kontrollált, szél nélküli
esetben a napsütötte oldal melegebb, a maximum a helyi dél után jelentkezik,
az óceán napi amplitúdója kisebb a szárazföldénél; nulla besugárzásnál
fizikailag ésszerű tartomány felé hűl.

**Verziózás:** additív, diagnosztikai mező (ND-103). Amíg más fogyasztó nem
olvassa, nem seed-törő; a hőmodellnek saját modellverziója van.

**Érintett fájlok:** `tools/reference/` (új hőanomália-orákulum),
`src/WorldGen.Core/Climate/Temperature.cs` (bázis elérése), új
`src/WorldGen.Core/Climate/SurfaceTemperatureField.cs`.

**Módosítás mérés alapján (2026-09-13, implementáció közben).** A level-6
referencia első futása (`tools/reference/thermal_field_ref.py`) két hibát
mutatott a fenti bázis-definícióval:

1. A napi faktoros `TemperatureKelvinFull`-szerkezet sarki éjszakán a radiatív
   tag nullára esése miatt ~29 K-es bázist adott — sérti a kész-definíciót.
2. A szél termikus tagja a sarki éjszaka határán a `T^(1/4)` végtelen
   deriváltja miatt ~5700 m/s-os sebességet adott (a meglévő `WindVector`
   ugyanezt a hibát hordozza).

Két javítási irány egycellás mérése (`tools/reference/thermal_seasonal_column_ref.py`):

| Változat | Eredmény | Döntés |
|---|---|---|
| A: éves átlagos bázis, az évszakot a solver integrálja | Az óceán ~40 napos memóriája miatt a kanonikus újraindítás hibája 60 nap spin-up után is 5,3 K (45°) / 11,9 K (80°); 80°-os óceán nyáron +22 °C | Elvetve |
| **S: évszakos bázis, simított radiatív faktor** `f_eff = (1 − β)·f_napi + β·f_éves` | β = 0,5: 80°-os szárazföld −58 … +21 °C, 45° −18 … +37 °C; 10 napos spin-up hiba ≤ 0,18 K. β = 0,3: sarki minimum −80 °C | **Elfogadva, β = 0,5 (M13, ideiglenes)** |

Érvényes definíció: `Bs = Ba = T_rad(f_eff) + T_greenhouse + T_ocean − T_alt + T_cycle`,
ahol `T_ocean = 0,3·(mean_j T_rad(f_eff_j) − T_rad(f_eff))` csak óceánon; a
forcing változatlanul `ΔQ = F·(1 − albedo)·(max(0, n·s) − f_napi)`. A szél
termikus tagja a `T_rad(f_eff) + 33 − T_alt` pont-hőmérséklet gradienséből
készül (level 6-on max. 61 m/s). A simítás a meridionális hőszállítás és a
hőtehetetlenség proxyja, nem forrásolt mérés — megerősítendő.

Ideiglenes, még megerősítendő további választások: **M11** — hőcserében
`U_eff = max(U, 1 m/s)` (COARE gustiness-érv, Fairall et al. 2003); **M12** —
édesvíz hőkapacitása az óceáni képlet, jégé a szárazföldi.

Ismert, a meglévő §28-kalibrációból öröklött korlát: a 0,06-os óceáni albedó
és a +33 K additív üvegház miatt az egyenlítői óceán bázisa ~48 °C. Ez nem a
hőmodell hibája; a klímamodell kalibrációja külön feladat.

Bizonyíték: Python-referencia kétszeri futása bájtra azonos
(`thermal_field_vectors.json`); C# `SurfaceTemperatureFieldTests` 22/22
(rács, bázis, szél, rövid és kanonikus futás a vektorokhoz mérve,
determinizmus, párhuzamos = szekvenciális, konstansmegőrzés, energia,
paraméterhatás, napi ciklus, sarki korlát, élesetek).

### ND-101 — Hőmodell rács, idő, tick, spin-up és checkpoint

**2026-09-13, implementáció előtt. Jóváhagyott irány** (backlog 3, 4, 5, 20,
21. pont). Az ND-02 (fix level-6 szimulációs bázis) és ND-03 (köztes idő csak
jelölt prezentációs interpoláció) alkalmazása a hőmodellre.

**Döntés:**

- Fix level-6 egészgömbös rács: `6 · 64 · 64 = 24 576` cella, sűrű `double[]`
  mezők, kanonikus index `face · 64² + u · 64 + v` (az ND-66 sűrű rácsának
  konvenciója). Átlagos cellaél ~168 km (mérve, ND-101 3. nyitott kérdés). A render-LOD nem módosítja az
  állapotot és a költséget.
- Egyetlen Core `SimulationTime`: egész tickszám (`long`), verziózott fix
  tickhosszal. A másodperc `tick · tickSeconds` (egész), a csillagászati nap
  `seconds / 86400.0`. Unity `deltaTime` csak az idősebesség-gyűjtőt táplálja,
  solver-lépés soha.
- Két puffer (előző → következő), tickenként csere. Minden cella csak az előző
  pufferből és rögzített sorrendű szomszédokból számol, ezért szekvenciális és
  párhuzamos futás bitazonos.
- Fizikai tick nem hagyható ki. Lemaradáskor a legutóbbi kész snapshot látszik
  időbélyeggel, és az időgyorsítás lassul.
- Kezdőállapot: `θs = θa = 0` a cél-időbucket lassú bázisán, utána dokumentált,
  egész forgásszámú determinisztikus spin-up. Nagy deep-time ugrásnál nem fut
  milliónyi tick: az új bucket bázisából új, fix hosszú spin-up indul.
- A hőmező verziózott, eldobható cache/checkpoint (modellverzió + paraméter-
  hash + tick). Autoritatív world state/hash csak külön döntéssel (ND-103).

**Nyitott kérdések:**

| # | Kérdés | Opciók / javaslat |
|---:|---|---|
| 1 | Időintegrátor | A: teljesen explicit Euler — egyszerű, de a kis hőkapacitású szárazföldi cella stabilitása kis ticket kényszeríthet. B: IMEX, a cellánkénti lineáris tagok (`λs`, `λa`, `ksa`) implicit 2×2 megoldása csak `+ − × /` műveletekkel, az advekció explicit upwind. **Mérve (2026-09-13, egycellás, RK4 30 s referencia):** visszafelé Euler-IMEX (forcing a lépés végén) elsőrendű — szárazföldön (0,12 m) 2,0 K / 1,0 K max. hiba 1800 / 900 s-nál; **Crank–Nicolson-IMEX (forcing a lépés közepén) másodrendű — 0,16 K / 0,025 K**, 0,5 m-es szárazföldön 0,052 / 0,0072 K, óceánon ≤ 0,001 K. Mindkettő minden vizsgált ticknél stabil. **Javaslat: Crank–Nicolson-IMEX** |
| 2 | Tickhossz | A lokális tagok a CN-IMEX-szel nem korlátozzák a ticket; 900 s mellett a szárazföldi hiba ≤ 0,025 K, 1800 s mellett ≤ 0,16 K (mérve). A végleges tick az advekció CFL-korlátjától függ (legkisebb cellaél / max. szélsebesség, ND-102), és a forgási periódus egész osztója legyen. Jelölt: 900 s |
| 3 | Rácsmetrika (cellaterület, élhossz, élnormál) előállítása | **Mérve (2026-09-13, `tools/reference/thermal_grid_metrics_ref.py`):** level 6-on a húrsokszög-terület a pontos gömbi területtől −1,5·10⁻⁴ … −7,1·10⁻⁵ relatív eltérésű, a teljes összegre normalizálva −2,3·10⁻⁵ … +5,6·10⁻⁵; a húr és a gömbi ív élhossza 1,3·10⁻⁵ … 2,5·10⁻⁵ relatív eltérésű; a cellaterület max/min aránya 1,3969 (az ND-24 mért értékével egyező); átlagos cellaél ~168 km. A cellasarkok az ND-24 szerint konstrukciós (baked) `tan`-warp pozíciók, tehát a metrika is konstrukciós adat: egyszer épül, a szimuláció csak olvassa, a táblahash-t a CI platformmátrixa ellenőrzi. **Javaslat: B** — húrsokszög-terület és húr-élhossz csak `+ − × / sqrt` műveletekkel, a teljes területre normalizálva; nem vezet be új transzcendens függvényt az ND-24 `tan`-ján túl. A konzervativitáshoz antiszimmetrikusan használt, pozitív súly elegendő, a ~10⁻⁵ nagyságrendű eltérés a paraméterek bizonytalanságánál jóval kisebb |
| 4 | Spin-up hossza | A leglassabb (óceáni) relaxációs időállandóból és a mért napi periodikus konvergenciából |
| 5 | Időbucket és checkpoint-gyakoriság | Mérés után; a spin-up költsége határozza meg |

**Verziózás:** a tickhossz, a spin-up és a rácsmetrika a hőmodell verziójának
része; változásuk a hőmező-cache érvénytelenítését jelenti, a világ seedjét nem.

### ND-102 — Egyirányú széladvekció a levegőanomáliára, külön `WindTick`

**2026-09-13, implementáció előtt. Jóváhagyott irány** (backlog 2, 15, 19,
26, 28. pont). A kétirányú hő→szél csatolás későbbi, külön ND.

**Kérdés.** Hogyan szállítsa a szél a felszínközeli levegő hőjét determinisztikusan,
a jelenlegi szélmodell megváltoztatása nélkül?

**Döntés:**

- A szél a levegőanomália energiatartalmát (`Ea = Ca · θa`) szállítja, nem a
  talajt és nem a teljes kalibrált bázist (26. pont).
- Véges térfogatú, upwind fluxus: élenként egyszer számolt normálsebesség
  `u_e` és élhossz `L_e`; a fluxus a szél felőli cella értékét viszi. Rögzített
  élsorrend (right, left, up, down), két puffer. Előbb a séma saját numerikus
  diffúzióját mérjük; explicit fizikai keveredés csak forrásolt célértékhez
  kerül be (28. pont).
- Külön, ritkább `WindTick`; két kész wind snapshot között determinisztikus
  lineáris interpoláció ugyanazon Core-időre (19. pont).
- A jelenlegi `WindPrecipitation.WindVector` nyers `Math.Asin/Atan2/Sin/Cos/Tanh`
  hívásokat használ, ezért a hőmodell kritikus bemeneteként változatlanul nem
  vehető át. A hőmodellhez **új, determinisztikus szélsnapshot-út** készül.
  A mostani csapadék- és biome-fogyasztók a régi úton maradnak; azok átállítása
  seed-törő és külön döntés.

**Nyitott kérdések:**

| # | Kérdés | Opciók / javaslat |
|---:|---|---|
| 1 | Konzervativitás és konstans mező megőrzése divergens szélnél | A felszínközeli szél nem divergenciamentes, ezért a tiszta fluxusforma konvergenciazónában felhalmozza az energiát, a tiszta advektív forma pedig nem konzervatív. **A:** fluxusforma + `θa·div(u)` kompenzáció. **B:** a szél diszkrét divergenciamentes vetítése (iteratív Poisson, drága). **C:** tiszta fluxusforma. **Mérve (2026-09-13, `tools/reference/thermal_advection_ref.py`):** pólus felé összetartó széllel, level 4-en 200 lépés után a C-ben a konstans mező max. eltérése 316 (a pólusnál felhalmozódik), az A-ban pontosan 0. Merevtest-forgással, élközépponti sebességgel mindkét forma energiaváltozása ≤ 1,4·10⁻¹⁶ egy teljes körülfordulás alatt. **Javaslat: A** |
| 1b | Numerikus diffúzió (28. döntés) | **Mérve:** elsőrendű upwind, merevtest-forgás 20 m s⁻¹-mal, egy teljes körülfordulás (~27 nap): a folt csúcsa level 4-en 0,151×, level 5-ön 0,266× (kockasarkokon áthaladó tengellyel level 4-en 0,082×); monoton, negatív érték nincs; tömegközéppont-hiba 22–76 km. A séma saját diffúziója nagy, ezért explicit keveredési tag nem kerül be. Ha az élő ellenőrzés túl elkenődött hőanomáliát mutat, egy másodrendű, limiteres séma külön döntés |
| 2 | A determinisztikus szél képlete | A meglévő zonális sávok és Coriolis-proxy átírása `z = sin(lat)` alapú vagy `DeterministicMath` + új determinisztikus inverz függvényekkel; Python-KAT a régi úttal való eltérés mérésével |
| 3 | `WindTick` hossza | A szélforrás (napi átlagos hőgradiens) változási sebességéből; kezdeti jelölt a forgási periódus egész osztója. **Költség mérve (2026-09-13):** a jelenlegi `WindVector` a teljes level-6 rácson 156 ms egy szálon (6,3 µs/cella), tehát egy snapshot a termikus ticknél nagyságrendekkel drágább; az új determinisztikus szélútnak ennél ne legyen lassabb |
| 4 | Élnormál-sebesség | **Mérve (2026-09-13):** level 6-on merevtest-forgásnál (z és kockasarkokon áthaladó tengely) az élközépponti analitikus kiértékelés diszkrét divergenciája max. 6,9·10⁻¹⁵ (U·√A-hoz mérve), a két cellaközép átlaga 3,5·10⁻³ … 5,9·10⁻³ hamis divergenciát ad, és a kompenzált formában egy körülfordulás alatt 2,8·10⁻⁴ … 9,1·10⁻⁴ energiát veszít; tömegközéppont-hibája is nagyobb (39–133 km vs. 22–76 km). A két oldalról számolt élhossz max. 7,5·10⁻¹⁶ relatív eltérésű, ezért a fluxust élenként egyszer kell számolni. **Javaslat: élközépponti kiértékelés.** A legkisebb level-6 cellaél 128,8 km |

**Elfogadási feltétel:** szél nélkül egy lokalizált `θa` anomália nem mozdul;
egyenletes széllel a szélirányba mozdul és a felszínre hőcserével hat; konstans
mező a kockalap-éleken átlépve sem változik; divergenciamentes széllel az
energia a dokumentált numerikus tolerancián belül megmarad.

### ND-103 — Termikus felszíntípus és a diagnosztikai/autoritatív határ

**2026-09-13, implementáció előtt. Jóváhagyott irány** (backlog 6, 7, 16, 21. pont).

**Döntés:**

- Új `SurfaceThermalKind`: `Land`, `Ocean`, `Freshwater`, `Ice`, saját
  albedóval és hőkapacitással (ND-100 nyitott 2. és 5. kérdés). A jelenlegi
  `isOceanic` boolean nem elég.
- Forrás: a meglévő óceánmaszk, tóazonosítás és jégbesorolás. A hőmodell a
  jeget csak olvassa (16. pont).
- A hőmező **párhuzamos diagnosztikai modell**: nem írja át a biome-ot, jeget,
  csapadékot, hidrológiát, panelmetrikákat vagy a `WorldStateHash`-t. Más
  fogyasztó csak külön validációs és modellverziós kapun állhat át rá.

**Nyitott kérdések:**

| # | Kérdés | Opciók / javaslat |
|---:|---|---|
| 1 | Level-6 cellatípus a finomabb maszkokból | **A:** többségi típus — egyszerű, de a part lépcsős. **B:** területarányos keverés (albedó és hőkapacitás súlyozott átlaga). Javaslat: B, ha a Python a part menti napi amplitúdót ésszerűbbnek méri |
| 2 | A jég forrása deep-time és évszak mellett | A meglévő jégbesorolás időpillanata a hőmodell bázisidejével legyen azonos; nem a render-LOD-ból |

**Verziózás:** amíg diagnosztikai, a world hash változatlan. Az autoritatív
átállás modellverzió-, betöltési hiba-, checkpoint- és hash-frissítést igényel.

### ND-104 — Hőoverlay adatút, UI és teljesítménycélok a Viewerben

**2026-09-13, implementáció előtt. Jóváhagyott irány** (backlog 8, 9, 10, 11,
12, 20, 22, 23. pont). A Core-fázisok (ND-100–103) után következik; élő Unity-
ellenőrzést igényel.

**Döntés:**

- A Core számolja a teljes level-6 `double` mezőt. A GPU csak interpolál és
  palettáz; HLSL-ben nincs második sugárzás-, szél- vagy hőképlet.
- Adatút: hat szelet, face-enként 64×64, CPU-n explicit skálával fixpontosra
  kvantált scalar textúra, egycellás, szomszédmezőből töltött gutterrel; a
  terep- és vízvertexek face-UV-t kapnak. A prezentációs textúra byte-
  reprodukálható.
- A solver háttérszálon, kettős snapshot-pufferrel halad az overlay állapotától
  függetlenül; a főszál csak kész, időbélyegzett snapshotot cserél.
- A `SunController`, a szél, a solver és az overlay ugyanazt a Core
  `SimulationTime`-ot olvassa; a `currentTimeDays` és a `climateDayT` nem
  maradhat két független óra.
- Egyetlen, kölcsönösen kizáró `SurfaceOverlayMode` (`None`,
  `SurfaceTemperature`, `AirTemperature`, `WindSpeed`, `Precipitation`) a
  mostani booleanok helyett, tesztelt migrációval. Overlay-váltás nem indít
  `Build()`-et, hidrológiát vagy mesh-geometriát (az ND-50 invalidáció
  általánosítása).
- Világításfüggetlen alapszín, opcionális külön hillshade; fix abszolút °C
  skála külön 0 °C jelöléssel, automatikus min/max nélkül.
- A közös futásidejű panel kap összecsukható blokkot a kurzor alatti
  komponensbontással (`Ts`, `Ta`, bázis, besugárzás, napszög, hőcsere,
  advekció, keveredés, felszíntípus, magassági korrekció).
- Teljesítménycél élő gépen mérve: kész snapshotnál egy képkockás váltás,
  16,7 ms alatti főszálú csere/feltöltés, normál időhaladásnál legalább 5 Hz.

**Nyitott kérdések:**

| # | Kérdés | Javaslat |
|---:|---|---|
| 1 | Kvantálási skála és bitmélység | 16 bit, a fizikailag lehetséges tartományra és a panelen szükséges felbontásra méretezve; a Python P1/P99 burkoló alapján |
| 2 | Paletta alapvégpontjai | Több seed, évszak, nappal/éjszaka és spin-up utáni P1/P99 alapján kalibrálva |
| 3 | Snapshot-átvételi gyakoriság gyorsított időnél | Mérés az 5 Hz-es cél és a háttérszál terhelése alapján |

**Implementáció (2026-09-13), élő Unity-ellenőrzés előtt.** Kvantálás:
`q = floor((K − 150) / 0,01 + 0,5)` 16 biten (150,00 … 805,35 K, 0,01 K
lépés). Atlasz: 396 × 66 R16 texel, lapanként 64×64 cella + egycellás, a
topológiai szomszéddal töltött gutter; a gutter-sarok a legközelebbi belső
cella. Paletta: fix alapvégpontok −60 °C / +50 °C (Inspectorban állítható),
kék → cián → 0 °C világos semleges → sárga → piros, 0 °C-os kontúrvonal. A
snapshot-feltöltés alapból legfeljebb 5 Hz, a solver háttérmunkája legfeljebb
96 tick/munka, a lokális lépés párhuzamos (bitazonos a szekvenciálissal).
Az idő forrása a `SunController.CurrentTimeDays` (egész tickre lefelé
kerekítve); a `climateDayT` Build-kori biome-paraméter marad. Eltérések és
nyitott pontok: `docs/01-architecture.md` §11.6. A nyitott kérdések 1–3.
pontja (bitmélység és alapvégpontok véglegesítése, gyorsított idő) az élő
PerfLog (`[ND-104 thermal]` sor) és vizuális próba után zárható.

### ND-105 — Az alkalmazásréteg (App Shell) helye és függetlensége

**2026-09-13. Megvalósítva (Foundation-rész).** Részletek:
`docs/09-app-shell-architecture.md`. **Az ND-105–114 blokk az app-rétegé**
(2026-09-13-án ND-105–109-ről bővítve); a párhuzamos Core-munka ND-115-től folytassa.

**Döntés:** a motor- és Core-független alkalmazáslogika (állapotgép, session,
settings, mentési konténer, UI-modellek, hang-matek) a
`unity/WorldGenViewer/Assets/Scripts/App/Foundation/` alatt él,
`WorldGen.App.Foundation` asmdef-fel (`noEngineReferences: true`, **nincs
`WorldGen.Core` referencia**). Fordítási kapu: `tests/WorldGen.App.Foundation.Compile`
(netstandard2.1, C# 9, linkelt forrás), tesztek: `tests/WorldGen.App.Foundation.Tests`.

| Opció | Előny | Hátrány |
|---|---|---|
| **A: Unity Assets + noEngineReferences asmdef (választott)** | a viewer-LOD bevált mintája; Unity azonnal látja | a csproj a forrástól külön mappában |
| B: `src/WorldGen.App` package | szimmetrikus a Core-ral | a `src/` a determinisztikus mag helye (CLAUDE.md), manifest-módosítás |

**Verziózás:** nem seed-törő, szimulációt nem érint.

### ND-106 — Saját minimál JSON az alkalmazásrétegben

**2026-09-13. Megvalósítva.** A Unity alatt nincs `System.Text.Json`, a
`JsonUtility` motorfüggő és nem kezeli a szótárakat, verziómigrációt.
Döntés: saját, szigorú RFC 8259 parser/writer (`WorldGen.App.Serialization`),
mélységkorláttal, pozíciós hibával; a szám eredeti szövegként is megmarad
(64 bites seed pontosan). Alternatíva: `com.unity.nuget.newtonsoft-json`
— elvetve, mert a Foundation így külső csomag nélkül, dotnet alatt is
tesztelhető.

### ND-107 — Mentési konténer, szöveges seed-leképezés

**2026-09-13. Megvalósítva (Foundation), a szekciók tartalma Core-függő.**

- Konténer: `WGSV` magic, u16 konténerverzió, UTF-8 JSON fejléc CRC32-vel,
  szekciótábla (név, hossz, CRC32), nyers szekcióadatok. Little-endian.
  A betöltési lista csak a fejlécet olvassa. Alternatíva: ZIP — elvetve
  (lassabb fejléc-only listázás, nagyobb felület).
- Atomi írás: ideiglenes fájl → flush lemezre → replace, előző változat `.bak`.
- A szöveges seed (nem szám) → **FNV-1a 64** az UTF-8 bájtokon, normalizálás
  nélkül. Ez stabil szerződés (megosztott szöveges seedek); módosítása
  verzióemelés. A decimális és a `0x` hex seed változatlanul az `ulong` érték.

### ND-108 — Generátorverzió a mentés kompatibilitásához (LEZÁRVA)

**Előzmény (2026-09-13): nyitott, a Save Core-kötését blokkolja.**

A mentés fejlécébe `worldGeneratorVersion` kerül. A Core-ban ma nincs ilyen
azonosító (a `VERSION` fájl emberi checkpoint; a spec
generator/simulation/schema verziói nem implementáltak).

| Opció | Leírás |
|---|---|
| **A (javaslat)** | Core-konstans `WorldGeneratorVersion` (egész vagy SemVer), minden seed-törő ND-nél emelve |
| B | A spec hármasa (generator / simulation / schema) külön mezőként |
| C | `WorldStateHash` egy kanonikus seedre, mint ujjlenyomat (automatikus, de drága és nem mond migrálhatóságot) |

**Döntés (2026-09-22, A6, implementáció előtt): A.** A Core
`Persistence.WorldGeneratorVersion.Current` konstansa kezdetben `"1"`.
Ez a jelenlegi, ND-126b/130 utáni numerikus világmodell első explicit
kompatibilitási alapvonala; nem a régi `.worldpkg` v1 azonosítója. Minden
seed-/világadat-törő algoritmus-, paraméteralapérték-, random- vagy
azonosítóváltozáskor monoton növelendő, az érintett ND-ben indokolva.
Pusztán reprezentációs vagy bitazonos teljesítményjavítás nem emeli.
Az alkalmazásverzió, a konténer/formátum és a generátor külön fogalom;
nincs automatikus hash-/Git-/dátumalapú verzió és nincs több verzió futtatása.

- CLI: a `.worldpkg` formátum **2 → 3**, új `WorldGeneratorVersion` mező.
  Az új formátumszám azért kell, hogy a régi olvasó se fogadjon el olyan
  új csomagot, amelynek generátorverzióját nem ellenőrzi. A v1/v2 csomag
  explicit elutasított; a hiányzó verzió nem kaphat automatikusan aktuális
  alapértéket. A `Create` bélyegez, a `Load`, `Save` és újraszámolás is
  ellenőrzi a formátumot és a pontos generátoregyezést, számítás/írás előtt.
- Alkalmazás: a Foundation Core-független marad. A `CoreSavePolicy`
  vékony UnityBinding-adapter ad új, verziózott fejlécet és ellenőrzési
  szabályt a Core-konstansból; az `AppBootstrap` Inspector-helyőrzője megszűnik.
  A `.wgsave` fejléc már tartalmazza a mezőt, formátumemelés nem szükséges.
  Hiányzó/eltérő verzió → `ConfigurationOnly`: állapot nem tölthető,
  a fejlécből seed/paraméter továbbra is kiolvasható. A repository a
  tényleges betöltéskor, a szekcióadatok előtt ismét ellenőriz; nem elég
  a listanézet korábbi döntése. Nincs csendes átverziózás vagy migráció.
- A6 határa a közös verzió és a mentési kompatibilitási kapu. A teljes
  szimulációs állapot szerializálása, session-host és menübekötés külön
  alkalmazásfeladat marad; ennek elkészültét A6 nem állítja.

Zárókapu: aktuális mentés round-trip; régi/hiányzó/eltérő/jövőbeli
verzió elutasítása; formátum/generátor/app-verzió függetlensége; közvetlen
betöltés és megváltozott fájl ellenőrzése; solution-/érintett tesztek,
élő Unity-fordítás. A numerikus világadatok változatlanok.

**Lezárás (2026-09-22):** implementálva, minden fenti kapu teljesült.
Teljes Debug solution: **1531/1531** (Core 567, LOD 490, app 451, CLI 23);
az app és CLI Release-ben is 451/451 és 23/23. Debug/Release build és
offline UnityBinding-kapu 0 hiba. Élő Unity-fordítás sikeres, Console 0
hiba; valódi Editor-adapterpróba és fájlos CLI round-trip/elutasítás kész.
KAT 9/9; újragenerált referenciavektorok byte-azonosak. A részletes
bizonyíték és hatókör: `history/2026-09-22-a6-generator-version.md`.

### ND-109 — Nem determinisztikus API-k az alkalmazásrétegben

**2026-09-13. Megvalósítva.** A CLAUDE.md lebegőpontos és véletlen-tiltásai a
**szimulációs kritikus útra** vonatkoznak. Az app-réteg használhat
`DateTime`-ot (injektált `IClock`), `Guid`-ot (ideiglenes fájlnév),
`Math.Log10`-et (hangerő-dB) és kriptográfiai entrópiát (új seed sorsolása).
Határ: a szimulációba csak explicit, tárolt érték lép be (seed, paraméterek,
szimulációs idő). A véletlen preset is konkrét értékeket sorsol, amik a
kérésbe és a mentésbe kerülnek.

### ND-110 — A menürendszer UI-technológiája: UI Toolkit

**2026-09-13 nyitva, 2026-09-20 ELDÖNTVE (felhasználói döntés): UI Toolkit,
az IMGUI megtartásával a fejlesztői overlayekhez.**

**Hatókör.** A döntés CSAK a nézet-réteget érinti. A Foundation-modellek
(`MenuModel`, `SettingsScreenModel`, `SaveSlotRows`, `Dialogs`, `ToastQueue`)
szándékosan UI-technológia-függetlenek — a `SettingsScreenModel` doksija ezt
ki is mondja —, ezért a 433 Foundation-teszt egyikét sem érinti, és a döntés
később mérsékelt költséggel visszavonható.

**Mi kapja az UI Toolkitet:** a kiadható héj — Main Menu, Settings,
Load/Save, Pause, dialógusok, toastok.

**Mi MARAD IMGUI-ban, szándékosan:** a fejlesztői overlayek — navigációs
menü, réteg-kapcsolók, deep-time panel, F3 diagnosztika. Ezek működnek, nem
részei a kiadható héjnak, és az átírásuk tiszta veszteség lenne. A
`WorldGenPanelUI` (Canvas+TMP, 175 sor) sorsa a Main Menu munkájakor dől el.

**A DÖNTŐ ÉRV — nem az esztétika, hanem a munkamegosztás.** A uGUI
prefab-alapú: minden képernyő kézi Unity-Editor szerkesztés, amit CSAK a
felhasználó tud elvégezni. A projekt története során ez többször beragadt
("új Unity-Editor lépést igényel" → sokáig nyitva marad). Az UI Toolkit
ezzel szemben UXML + USS SZÖVEGFÁJLOKON áll: ezeket az agent írja meg,
verziókezeljük és offline ellenőrizzük; a felhasználóra scene-enként egyetlen
`UIDocument` komponens beállítása marad.

**Mérési alap a döntéshez (2026-09-20):** IMGUI 41 hívási hely a
`PlanetGridMesh`-ben, Canvas+TMP 175 sor a `WorldGenPanelUI`-ban, UI Toolkit
használat: NULLA. Unity 6000.0.77f1.

**Vállalt hátrány:** új technológia a projektben, tehát az ELSŐ képernyő
lassabb lesz a többinél; a USS-stíluslap miatt a másodiktól gyorsul. Az UI
Toolkit world-space UI-ra gyengébb — a terv szerint ilyen nem kell; ha
mégis felmerülne (a bolygó felszínéhez kötött, lebegő panelek), az ÚJ
döntést igényel, nem ennek a kiterjesztését.

---

**Az eredeti mérlegelés (2026-09-13), megtartva:**

A viewer ma kétféle UI-t használ: IMGUI (`PlanetGridMesh.DrawNavigationPanel`)
és Canvas + TextMeshPro (`WorldGenPanelUI`).

| Opció | Előny | Hátrány |
|---|---|---|
| **A: UI Toolkit (javaslat)** | stíluslap (USS), felbontás- és UI-scale-kezelés beépítve, billentyű-/egérnavigáció, jól illeszkedik a modell/nézet szétválasztáshoz | új technológia a projektben; world-space UI-ra gyengébb (itt nem kell) |
| B: uGUI + TextMeshPro | a projektben már van; sok minta | prefab-alapú, a skálázás és a navigáció több kézi munka |
| C: IMGUI | a navigációs panel már ilyen | kiadható menürendszerhez nem ajánlott (stílus, akadálymentesség, teljesítmény) |

(Az eredeti jegyzet itt "döntés kell a felhasználótól"-lal zárult; a döntés
2026-09-20-án megszületett — ld. a szakasz elején.)

### ND-111 — Kiadási identitás és alkalmazásverzió (RÉSZBEN NYITOTT)

**2026-09-13.**

- **Megvalósítva:** az alkalmazásverzió és a kiadási identitás egyetlen fájlban
  él: `tools/release/release-identity.json` (termék- és cégnév, SemVer
  `0.1.0-alpha`). A repo-gyökér `VERSION` fájl emberi checkpoint marad
  (a saját leírása szerint nem kiadási verzió), ezért NEM ebből jön az app-verzió.
  A build-script az identitást csak a build idejére állítja be a
  PlayerSettings-ben, utána visszaállítja (a ProjectSettings nem lesz koszos),
  és `StreamingAssets/build-info.json`-t ír a futásidejű `BuildInfo`-hoz.
- **Nyitott, felhasználói döntés:** a végleges **terméknév** és **cégnév**. Ez nem
  kozmetika: a Unity `persistentDataPath` (`%USERPROFILE%\AppData\LocalLow\<Company>\<Product>`)
  ebből képződik, tehát az első kiadás után a módosítása a mentések és beállítások
  „eltűnését” okozza (migráció nélkül). Jelenleg `WorldGen` / `WorldGen`
  helyőrzővel. A mostani `ProjectSettings` értékei (`Unity Technologies` /
  `com.unity.template.hdrp-blank`) a sablonból maradtak.
- Ikon és splash screen: asset kell a felhasználótól.

### ND-112 — Windows-csomagolás: portable ZIP és Inno Setup

**2026-09-13. Megvalósítva (scriptek), a telepítő fordítása Inno Setup 6
telepítését igényli.**

- Portable: `tools/release/package-portable.ps1` →
  `WorldGen-<verzió>-win64.zip`, a Unity „DoNotShip” / „DontShip” mappái nélkül.
- Telepítő: `tools/release/WorldGen.iss` (Inno Setup 6). Az `AppId` GUID
  **rögzített, soha nem változhat** (különben a frissítés nem ismeri fel a
  meglévő telepítést). Alapértelmezett könyvtár `{autopf}\WorldGen`, Start
  menü, opcionális asztali ikon, indítás a telepítés végén.
- Uninstall: a felhasználói adat a `LocalLow` alatt van, a telepítési
  könyvtáron kívül, így alapból megmarad. Egy kérdés (alapértelmezett: **Nem**)
  felajánlja a mentések, beállítások, képernyőképek és naplók törlését (WF-INSTALL-002).
- Alternatíva: MSI (WiX). Elvetve az MVP-hez, mert az Inno egyszerűbb, és a roadmap is ezt javasolja.

### ND-118 — A determinisztikus matek kiterjesztése: Atan/Atan2/Asin/Acos/Tanh

**2026-09-20. Megvalósítva (a függvények); a modulok átállítása KÜLÖN lépés.**

**Előzmény.** Az ND-27 eldöntötte, hogy saját polinomiális implementációt
írunk a transzcendens függvényekre, és a `DeterministicMath` meg is épült:
`Sin`, `Cos`, `SinCos`, `Ln`, `Exp`, `Pow`. Nyolc Core-modul használja.

**A feltárt hiányosság (2026-09-20).** A klíma-lánc
(`WindPrecipitation`: `Asin`/`Atan2`/`Tanh` ×4), a folyó-nyomvonal
(`RiverPathTracing`: `Acos`) és a csillagászat (`OrbitalMechanics.SubsolarPoint`:
`Asin`/`Atan2`) máig NYERS `System.Math`-ot hív a kritikus úton — de NEM
feledékenységből: **ezeknek a függvényeknek egyszerűen nem volt
determinisztikus párjuk.** A `DeterministicMath` API-ja nem tartalmazta
őket, tehát nem is lehetett mire cserélni.

Ennek a következménye mérhető: **tizenkét KAT-teszt toleranciával mér**, nem
bitpontosan (`OrbitalMechanics`, `Temperature`, `WindPrecipitation`,
`MoisturePrecipitation`, `LakesIceErosion`, `DeepTimeErosionGlaciation`,
`PlateMotion`, `TileGeometry`, `SeaLevelCalibration`,
`SurfaceTemperatureField`, `RegolithModel`, `DeterministicMath` maga a
plauzibilitásra). Vagyis az I1 ("bitre ugyanaz a világ minden platformon")
ezekre a láncokra ma **nincs kikényszerítve**, csak ~1e-6-ig.

**Döntés.** Megírjuk az öt hiányzó függvényt, ugyanazzal a módszerrel, mint
az ND-27: csak garantáltan bitpontos műveletekből (`+ - * /`, `Math.Sqrt`),
Python-orákulum → KAT-vektorok → C#.

| Függvény | Módszer |
|---|---|
| `Atan` | reciprok-redukció (\|x\|>1), majd HÁROMSZOROS felezés `a/(1+sqrt(1+a²))`-vel \|t\|≤0,0985-ig, majd 10 tagú Taylor hátulról előre összegezve |
| `Atan2` | kvadráns-logika az `Atan`-ra |
| `Asin` | \|x\|≤0,5: `atan(x/sqrt(1-x²))`; \|x\|>0,5: félszög-azonosság, mert a naiv képlet a tartomány SZÉLÉN kioltana |
| `Acos` | `pi/2 - Asin(x)` |
| `Tanh` | `(1-e^{-2\|x\|})/(1+e^{-2\|x\|})` a determinisztikus `Exp`-ből, kis- és nagy-argumentumú átváltással |

**Mért pontosság** (20 000 minta/függvény, a valódi `math.*`-hoz mérve):
`atan` 3,3e-16 · `asin` 6,7e-16 · `acos` 8,9e-16 · `atan2` 4,4e-16 ·
`tanh` 8,0e-13 (relatív). A kitűzött cél 1e-9 volt. A tartomány szélén
(\|x\|→1) az `asin` hibája 2,2e-16 — ezért kell a félszög-ág.

**A C# BITRE egyezik a Pythonnal**, tolerancia nélküli `Assert.Equal`-lal,
2000 új vektoron (500 függvényenként). Ez azért lehetséges, mert mindkét
oldal UGYANAZT a műveleti sorrendet futtatja, csak IEEE-754 szerint
korrekt kerekítésű műveletekből.

**AMI EBBŐL NEM KÖVETKEZIK.** Ez a lépés **önmagában semmit nem változtat a
világon**: egyetlen meglévő modul sem állt át, a meglévő 1000
`sinCos`/`pow` vektor bitre változatlan (ellenőrizve). A modulok átállítása
**SEED-TÖRŐ**, mert minden ráépülő KAT-vektort újra kell generálni
(domborzat, lemezhatár, hidrológia, folyók, tavak, nedvesség, features,
vulkanizmus, state hash) — és az ND-52 tapasztalata szerint az **ordinális
kalibrációt is** (ld. ND-09 v2, 2026-09-19: az ND-52 észrevétlenül tette
elavulttá). Az átállás külön döntést és külön, összevont lépést igényel.

### ND-115 — Horizont-vágás a vetített terep-úton

**2026-09-19. Megvalósítva.** Részletek és mérések:
`history/2026-09-19-horizon-cull.md`.

**Probléma:** az `AdaptiveQuadTree.EvaluateNodeForPriority` korán visszatér,
ha vetített nézet ÉS terep-proxy is jelen van, ezért a lentebb álló,
kiterjedés-tudatos horizont-teszt ezen az ágon soha nem futott le. Márpedig
ez a produkciós ág. Következmény mérve: 3 egység magasságban, ahol a látható
sapka a gömb ~1,4%-a, a vágás 123 152 level-8 csomópontot járt be ~5 500
látható csempére; a 191 084 kiértékelésből 180 564 már a base-szint
eléréséig megtörtént.

**Döntés:** a proxy-ágon is fut horizont-vágás, `node.Level <
staticBaseLevel` hatókörrel. A feltétel SZÖGALAPÚ, nem érintősíkos: egy
`rP` sugarú pont akkor van az `R` sugarú takaró mögött `d` távolságból, ha
szöge > `acos(R/d) + acos(R/rP)`. Koszinuszban kifejtve csak szorzás és
`Math.Sqrt` kell, tehát a cut determinizmusa nem sérül (transzcendens
függvény nem bitpontos, ld. CLAUDE.md).

**Elvetett változat:** az érintősík-teszt (`C·X + |C|·r < R²`) átvétele a
nem-proxys ágból. Az CSAK a gömb felszínén lévő pontra helyes; emelt pontra
hamis, ezért 54 próbanézetből 6-ban megváltoztatta a cutot, kettőben
katasztrofálisan (5 636 levél → 0), a limbus menti látható csempéket kivágva.

**A garancia, amit vállalunk:** a cut NEM bitre azonos (48/54 nézet az, 6
eltér, mert telített budgetnél a best-first határa eltolódik). Helyette a
LÁTHATÓ FEDETTSÉG védett: 54 nézet × 21×11 képernyő-minta alapján a
finomított látható minták száma 6621/9561 a vágás előtt ÉS után is,
nézetenként pontosan egyezően, nulla romlott nézettel.

**Hatás:** metrika-kiértékelés −75,5%, a hideg selection 482-689 ms-ról
35-103 ms-ra, a meleg 157-183 ms-ról 7-22 ms-ra.

### ND-116 — A 2:1 kiegyensúlyozás korai kilépése blokkolt budgetnél

**2026-09-19. Megvalósítva.** Részletek és mérések:
`history/2026-09-19-balance-early-exit.md`.

**Probléma:** az `EnforceRestrictedBalance` fixpont-ciklusa minden körben
végigszkenneli a teljes cutot (|cut| × 4 szomszéd). Szoros budgetnél az őr
(`cut.Count + 3 > maxLeafCount`) minden felosztást blokkol, tehát a
szkennelésnek nincs kimenete, csak költsége: 20 000 levélnél 31 ms,
64 000-nél 78 ms. Ez a produkciós eset - a 2026-09-18-i naplóban 68
vágásból 44 volt telített.

**Döntés:** ugyanezt a feltételt belépéskor is ellenőrizzük. Ha igaz, örökre
igaz marad (a `SplitOnce` nettó +3, a `cut.Count` csak nő), tehát a korai
kilépés kimenete azonos. Igazolva 180 próbaeseten: nulla megváltozott cut,
a balance összideje 9068 ms → 1832 ms.

**NYITOTT marad:** ha a metrika a budget ALATT telítődik, van fejtér, és a
teljes újraszkennelés miatt 108 felosztás 332 ms-ba kerül (~3 ms/felosztás).
Munkalistás átírás megoldaná, DE a jelenlegi ciklus körönként gyűjt és
`TileId.Value` szerint rendezve oszt, a budget-őrök mid-iterációban is
blokkolhatnak - a kimenet tehát sorrend-függő. Az átírás előtt tisztázandó,
hogy a fixpont egyértelmű-e a korlátok nélkül, és hogyan viselkedik azokkal.
Ez egyben ok arra, hogy a `MaximumRenderBudget` NE emelkedjen: a balance
csak a "budget > telítődés" tartományban dolgozik, tehát nagyobb plafon
gyakrabban visz a drága esetbe.


### ND-117 — Talaj/regolit (`RegolithProfile`) MVP-hatókör és a maradék mezők halasztása

**2026-09-19.** A `docs/backlog.md` M8 sora és a
`src/WorldGen.Core/Features/OrdinalQuantization.cs` doksija régóta jelzi:
"Soil fertility TOVÁBBRA IS BLOKKOLT (nincs talaj-modul)". Részletes terv:
`docs/01-architecture.md` §13.

**Döntés — hatókör-szűkítés.** A spec §39 `RegolithProfile`-jának 10
mezőjéből (Depth, Porosity, WaterRetention, MineralDiversity,
PhosphorusAvailability, NitrogenAvailability, Iron, Sulfur, Salinity,
pHProxy) az MVP **csak hármat** (Depth, Porosity, WaterRetention) számol
— a `tools/reference/regolith_ref.py`-ban, C# nélkül (a C#-port külön
lépés). A döntő szűrő: a spec 6 forrása (alapkőzet, vulkanizmus, erózió,
üledék, víz, hőmérséklet) közül melyiknek van MÁR MOST valódi,
tile-onként VÁLTOZÓ Core-kimenete.

**Fedett forrás → felhasznált Core-kimenet:**
- erózió/üledék → `LakesIceErosion.ApplyStaticErosionPass`
  (`Erosion[]`/`DepositionGain[]`, M7, már KAT-vektoros);
- víz → a csapadék-mező (`moisture_transport_ref.compute_precipitation_field`
  / a C# oldalon `MoisturePrecipitation`, M5, már KAT-vektoros);
- hőmérséklet → `LakesIceErosion.AnnualTemperatureStats` (éves átlag, már
  KAT-vektoros).

**NEM fedett forrás, és MIÉRT:**
- **alapkőzet** — a Core-ban CSAK egy plate-szintű `isOceanic` bool létezik
  (`CrustElevation.cs`); minden szárazföldi tile-on ez UGYANAZ, tehát nulla
  tile-közi varianciát ad szárazföldön belül. Valódi kőzettípus/litológia
  NINCS modellezve. Amit ebből felhasználtunk: a LEJTŐ (elevációgradiens)
  mint csupasz-kőzet-kitettség proxy a `Depth`-hez — ez a MÁR dokumentált
  biome-táblázati kapcsolatot (§5: "Csupasz szikla … lejtő > 25°") követi,
  nem új feltalálás.
- **vulkanizmus** — `VolcanicEruption.cs` (ND-29) csak epizodikus,
  RITKA VEI8-eseményeket generál (várhatóan 1-2/millió év), nincs
  PERZISZTÁLT, tile-onkénti hamu-/tefra-lerakódás mező, amit fel tudnánk
  használni. Egy ilyen mező (távolság-alapú lecsengéssel, deep-time-ban
  felhalmozva) ÖNÁLLÓ, jövőbeli feladat lenne.

**Ezért a maradék 7 mező (`MineralDiversity`, `PhosphorusAvailability`,
`NitrogenAvailability`, `Iron`, `Sulfur`, `Salinity`, `pHProxy`) MIND
HALASZTVA marad** — mindegyik ténylegesen litológia/vulkanizmus-függő
lenne. Az I4 invariáns szerint inkább hiányozzon a mező (a C#
`RegolithProfile` struct egyelőre nem is tartalmazza őket), mint kitalált
érték szerepeljen rajta. **Előfeltétel a feloldásukhoz:** (1) egy
kőzettípus/litológia-modul, ami tile-szinten megkülönbözteti a
szárazföldi alapkőzetet (jelenleg nincs ütemezve), ÉS (2) egy perzisztált,
deep-time-ban felhalmozott vulkáni hamu-/tefra-lerakódás mező (a jelenlegi
epizodikus `VolcanicEruption` kiterjesztése).

**Nincs seed-törő hatás.** Ez a modul (`regolith_ref.py` +
`compute_regolith_profile`) kizárólag a CLAUDE.md táblázata szerint
garantáltan bitpontos műveleteket használ (`+ − × /`, `abs`, `min`, `max`
— nincs `Sin`/`Cos`/`Exp`/`Log`/`Pow` a láncban) és **nem igényel új
véletlenszám-mintavételt** (nincs új `RandomDomain`/`RandomProperty`) — a
három kimenet tisztán a már verifikált Core-kimenetek (elevéció/lejtő,
erózió, üledék, csapadék, hőmérséklet) algebrai függvénye. Ezért a C#-port
(amikor elkészül) ELVBEN bitpontosan, tolerancia nélkül egyezhet a
Python-referenciával — ezt a C#-implementáció dönti el véglegesen, nem ez
a döntés.

**Nyitva marad:** az MVP-konstansok (`DEPTH_MAX_M`, `FREEZE_THAW_HALF_RANGE_K`,
`PRECIP_REFERENCE` stb., ld. `docs/01-architecture.md` §13.2) illusztratívak,
vizuális kalibrálást igényelnek (ugyanaz a minta, mint az ND-41 szél/
csapadék-konstansai) — csak Unity-render után finomíthatók érdemben. A
§2.3 panel-táblázat "Soil fertility: mélység × minerality × nedvesség"
képlete mostantól `Depth × WaterRetention`-re mutasson (a "minerality" tag
kimarad, amíg a kémiai mezők blokkoltak — ld. `docs/01-architecture.md`
§13.6 táblázata). Az ordinális "Soil fertility" panelmező tényleges
kalibrálása (ND-09 mintájára, N≈500 világ, kvintilis-küszöbök) csak a
C#-port elkészülte UTÁN lehetséges — ez NEM ennek a döntésnek a
hatóköre.


### ND-119 — A szél-overlay NEM azt a szelet mutatja, amit a szimuláció használ (LEZÁRVA: (A))

**2026-09-20.** A todo.md #9 visszajelzés ("szél-overlay befagyasztja a
nézegetőt") teljesítmény-vizsgálata közben derült ki egy tartalmi
eltérés, ami I3/I4-et érint.

**A tény.** A `WindPrecipitation.WindVector` utolsó tagja a
*hegy-eltérés* (`ApplyMountainDeflection`), ami egy elevációs gradienst
vár. A világmodell saját csapadék-mezője —
`MoisturePrecipitation.Compute` — ezt a két paramétert **nullával** hívja
(`MoisturePrecipitation.cs`, a `WindVector`-hívás `0.0, 0.0` argumentuma),
tehát a szimuláció szele hegy-eltérés NÉLKÜL készül. A **megjelenítő**
szél-overlay viszont sarkonként kiszámolja a gradienst, és ÁTADJA.
Következésképp az overlay egy olyan szélmezőt rajzol ki, amit a
világmodellben semmi nem használ.

**Miért számít.** I3: "a képen látható minden pixel a világmodellből
következik". Egy overlay, ami a modellétől eltérő mennyiséget mutat, ezt
formálisan sérti — nem kitalált érték, de nem is a modell értéke.

**Másodlagos tény (méréssel).** A hegy-eltérés mértéke
`maxFraction * tanh(slopeMag / 0.5)`. A valós gradiens-nagyságok
10^4 nagyságrendűek, tehát a `tanh` MINDIG telítésben van: a tag
kizárólag a gradiens IRÁNYÁTÓL függ. Az irány viszont erősen skálafüggő
(a domborzat nagyfrekvenciás), így az overlay képe azon múlik, milyen
lépésközzel deriválunk — ami eddig egy tetszőleges konstans volt
(`GradientEps = 1e-3` radián), nem a megjelenített LOD felbontása.
Mért hatás a szín-rámpán (400 minta, `windSpeedColorMaxMs=15`):

| gradiens forrása | lépésköz | átlagos rámpa-eltolódás | minták >5% |
|---|---|---|---|
| pontonkénti véges differencia (régi) | 1e-3 rad | — (referencia) | — |
| base-szintű sarokrács (level 8, MOST ez fut) | 6,1e-3 rad | 0,090 | 27,8% |
| referencia-tile-onként (level 5, ELVETVE) | 4,9e-2 rad | 0,124 | 36,8% |

**Ami MOST történt (nem döntés, teljesítmény-javítás).** A
`WindSpeedColorAt` 238 us/sarokról 6,3 us/sarokra csökkent (38x): az
elevációs gradiens a MÁR KISZÁMOLT base-szintű sarok-pozíciókból jön, a
hőmérséklet-gradiens pedig az ND-64 előre számolt napi Nap-irányaiból
(ez utóbbi BITRE azonos). Az overlay ettől használható lett (82 FPS).
A fenti táblázat középső sora a mostani állapot.

**Opciók.**

- **(A) Az overlay a szimuláció szelét mutassa** — a hegy-eltérés-tagot
  nullával hívjuk, ahogy a `MoisturePrecipitation` is. I3/I4-tiszta,
  INGYEN van (a gradiens-számítás teljesen elmarad), és a kép
  skálafüggetlenné válik. Ára: eltűnik a domborzati részlet az
  overlayről, a kép zonálisabb/simább lesz.
- **(B) Marad a hegy-eltérés az overlayen, de RÖGZÍTETT skálával** — a
  mostani állapot dokumentálva: a gradiens a base-szint rácsosztásán
  értendő, és ezt az overlay felirata is közli. Az I3-eltérés megmarad,
  csak explicit lesz.
- **(C) A hegy-eltérés kerüljön be a SZIMULÁCIÓBA is** — a
  `MoisturePrecipitation` is adja át a gradienst. Ez fizikailag a
  legerősebb (az orografikus csapadék így kap szél-oldali erősítést a
  már meglévő `uplift` tagon felül is), de **SEED-TÖRŐ**: minden
  csapadék-mező, minden folyó-forrás és így minden régió-név megváltozik,
  és az ND-09 ordinális kalibrációt is újra kell futtatni.

**Javaslat: (A).** Az overlay feladata az, hogy a modellt mutassa; a
skálafüggő, telítésben lévő hegy-eltérés-tag ma inkább zajt ad, mint
információt, és épp ez a tag volt a költség 89%-a. (C) önmagában is
védhető fizikailag, de seed-törő, tehát külön, tudatos lépés kell hozzá —
nem egy overlay-hiba mellékterméke.

**LEZÁRVA (2026-09-21): (A) — a felhasználó döntése.** Az overlay mostantól
a hegy-eltérés tagot NULLÁVAL hívja, pontosan úgy, ahogy a
`MoisturePrecipitation.Compute` is — az overlay tehát a modell szelét mutatja
(I3/I4 helyreállt).

**Mellékhatás:** a sarkonkénti elevációs gradiens számítása teljesen elmarad.
Ez volt a `WindSpeedColorAt` költségének 89%-a, és emiatt kiesett a
`TryStaticCornerElevationGradient` / `ElevationGradientTangent` pár is
(126 sor törölve). A szél többi tagja (zonális alap, termikus szél, Coriolis)
változatlanul sarkonként számolódik az ND-64 mintákból — az overlay tehát nem
lesz blokkos.

A kép ezzel **skálafüggetlen** lett: korábban azon múlt, milyen lépésközzel
deriváltunk, mert a `maxFraction * tanh(slopeMag / 0.5)` tag a valós, 10^4
nagyságrendű gradienseknél MINDIG telítésben volt, tehát kizárólag a gradiens
IRÁNYÁTÓL függött. Ez zajt adott, nem információt.

### ND-120 — A GPU-osztályozó shader két algoritmus-generációval le van maradva (LEZÁRVA: (A))

**2026-09-20.** A todo.md úgy fogalmazott, hogy a jelenetben
`useGpuClassification: 1` **aktív**, tehát a besorolás és a geometria eltérő
elevációt lát. A vizsgálat ezt **részben cáfolta, részben súlyosbította.**

**1. A GPU-ág ma ELÉRHETETLEN.** A `PrecomputeClassificationsInParallel`
mindhárom hívója `forceCpu: true`-t ad (az ND-47 3. fázisa óta: worker
szálról a GPU-dispatch tilos), a `BuildStaticBaseLayer` sűrű ága pedig eleve
a tiszta CPU-s `PrecomputeStaticClassificationsInParallel`-t hívja. A 310
PerfLog átvizsgálása megerősíti: `usedGpu=True` **utoljára 2026-09-11-én**
fordult elő, azóta egyszer sem. Tehát **nincs élő CPU/GPU eltérés** — a
jelenetbeli `1` egy halott kapcsoló volt, ami élőnek látszott.

**2. Ha viszont bárki visszakapcsolná, az nem „gyorsítás" lenne.** A
`TileClassification.compute` `BaseElevationF`-je **két** Core-újítást nem
tartalmaz:
- **ND-52** másodlagos részletzaj (`SecondaryNoiseAmplitudeMeters = 900`);
- **ND-90** lemezhatár-keverés (`BlendedBaseElevationFromNoiseBasis`).

**Mérés** (`GpuShaderElevationParityTests`, seed `0xA7C944210000`, 20 lemez,
tengerszint mindkét oldalon a CPU-ból):

| hiányzó tag | \|Δ\| átlag | \|Δ\| max | óceán/szárazföld átfordulás | biome-átfordulás |
|---|---|---|---|---|
| csak ND-52 (másodlagos zaj) | 282,0 m | 890 m | **21,83%** | 23,43% |
| csak ND-90 (határkeverés) | 23,4 m | 3071 m | 0,39% | 0,40% |
| **a shader tényleges állapota (mindkettő hiányzik)** | **300,8 m** | **3116 m** | **21,99%** | **23,60%** |

Level 8-on (393 216 tile) gyakorlatilag ugyanez: 22,13% / 23,78% — az arány
**skála-stabil**. A két tag jellege eltér: a másodlagos zaj GLOBÁLIS (mindent
elmozdít, korlátos amplitúdóval), a határkeverés LOKÁLIS (kevés tile, de ott
nagyobb ugrás).

**A szám ALSÓ KORLÁT.** A mérés mindkét oldalon float64-gyel fut, tehát a
shader float32-es pontosságvesztése és bármilyen egyéb elcsúszás **nincs
benne**; kráter és deep-time erózió nélküli, t=0 alapdomborzatot hasonlít.

**Amit most tettem (nem döntés):**
- a jelenetbeli `useGpuClassification` 1 → **0** (ma viselkedésben semleges,
  mert az ág elérhetetlen — pusztán megszünteti a félrevezető állapotot, és
  összhangba hozza a C#-alapértékkel meg a shader saját kommentjével);
- a mérés `GpuShaderElevationParityTests`-be zárva, hogy a szám ne avuljon el;
- a mező mellé figyelmeztető blokk került a kóddal együtt olvasható helyre.

**Opciók.**

- **(A) A GPU-osztályozó út törlése** (shader, `GpuTileClassifier`, a mező és
  a jelenetbeli kapcsoló). Indok: ma is halott kód; kétszer futott
  „Compiler timed out"-ba; és amíg létezik, MINDEN jövőbeli Core-algoritmus-
  változást kézzel kellene utánavezetni — ez állandó I1-kockázat (egy
  elfelejtett port csendben más világot osztályozna). A klasszifikáció amúgy
  is 453 ms, amit az ND-50 óta overlay-váltáskor már át is ugrunk.
- **(B) A shader felzárkóztatása** (ND-52 + ND-90 portolása HLSL-be), majd
  egy CPU/GPU egyezési teszt, ami CI-ban fut. Ez megtartja a jövőbeli
  gyorsítási lehetőséget, de a fordítási időtúllépés kockázata megmarad, és
  a tesztnek valódi GPU kell — a CI-gépeken nincs.
- **(C) Marad úgy, ahogy most van**: halott kód, kikapcsolt kapcsoló,
  figyelmeztető komment és a mérést rögzítő teszt.

**Javaslat: (A).** A GPU-ág egy meg nem valósult optimalizáció maradványa,
ami nem termel értéket, viszont folyamatos determinizmus-kockázatot igen. Ha
a klasszifikáció később tényleg szűk keresztmetszet lesz, akkor érdemes
újraírni — a mai, elavult shader nem alap ehhez. (C) elfogadható átmenet;
(B) csak akkor védhető, ha valaki ténylegesen vállalja a folyamatos
karbantartást ÉS van hol futtatni az egyezési tesztet.

**Verziózás:** önmagában nem seed-törő (a GPU-ág ma nem fut, tehát egyetlen
világ sem függ tőle). (B) viszont azzá tenné, ha a portolás közben a
CPU-oldalt is hozzányúlnánk — nem szabad.

---

**LEZÁRVA (2026-09-21): (A) — a felhasználó döntése.** Törölve:
- `PrecomputeClassificationsOnGpu` (a dispatch-ág és a metódus),
- a `useGpuClassification` mező és a jelenetbeli kapcsoló,
- `Assets/Scripts/Viewer/Gpu/GpuTileClassifier.cs` (nem volt más hívója).

**A `forceCpu` paraméter szándékosan MEGMARADT**: az async emit-út ezzel
jelzi, hogy worker szálról fut. Ma már nincs másik ág, de a hívási felület
így változatlan, és egy jövőbeli, ELLENŐRZÖTT GPU-út ide illeszkedne vissza.

**AMIT NEM TÖRÖLTEM, ÉS MIÉRT — ezt érdemes tudni.** A
`TileClassification.compute` asset **megmaradt**, mert a `useGpuGeometry` út
is használja (`GpuTerrainGeometryGenerator`), és **az is ugyanazt az elavult
`BaseElevationF`-et** hívja. Vagyis a 22%-os eltérés kockázata a
`useGpuGeometry` bekapcsolásával **ma is él**. Ez nem volt része a
döntésnek, ezért nem nyúltam hozzá; a mező tooltipje viszont mostantól
tételesen kiírja a mért számokat. A shaderhez amúgy is óvatosan kell érni:
kétszer futott „Compiler timed out"-ba, és a mostantól nem hívott
`CSClassifyTiles` kernelt épp ezért hagytam benne.

A `GpuShaderElevationParityTests` **MARAD**, újrakeretezve: az állítása
független attól, hogy van-e GPU-út — ez a két tag nem elhanyagolható.
Konkrétan a `useGpuGeometry` utat védi, és minden jövőbeli „egyszerűsített"
eleváció-közelítést (GPU, előre számolt textúra, LOD-proxy).

### ND-121 — A balance munkalistás átírása kész; a `MaximumRenderBudget` plafon emelése döntést kér (LEZÁRVA: (B))

**2026-09-20.** Az `AdaptiveQuadTree.MaximumRenderBudget = 48 000` doksija
**két** okot nevez meg, és a másodikat kifejezetten feltételhez köti:
„Amíg a ciklus teljes újraszkennelés helyett nem munkalistával dolgozik, a
48 000 marad." Ez a feltétel most teljesült.

**1. Előfeltétel tisztázva: a fixpont korlátok nélkül EGYÉRTELMŰ.**
A saját magamnak előírt előfeltétel az volt, hogy a kimenet sorrend-függő-e.
`BalanceFixpointUniquenessTests`: öt gyökeresen eltérő végrehajtási sorrend —
köztük egy teljesen aszinkron, véletlen választással dolgozó fixpont-kereső —
**azonos vágást ÉS azonos lépésszámot** ad, és megegyezik a termelési,
körökben dolgozó implementációval.

Az ok szerkezeti, és külön tesztben is rögzítve: a felosztás **monoton** — egy
tile felosztása soha nem szüntet meg másik, fennálló szintkülönbség-sértést
(a fedő ős csak finomabb lehet, a különbség tehát csak csökken). A lezárás
ezért sorrendtől független legkisebb fixpont.

**Sorrend-függőség CSAK a korlátoknál van** (`WithATightBudgetTheOutcomeDoesDependOnOrder`):
ott a ciklus félbeszakad, és számít, melyik felosztások fértek be. Az átírás
ezért nem hivatkozhatott pusztán a fixpont egyértelműségére — a kör-szemantikát
is meg kellett őriznie.

**2. Az átírás.** Az első kör változatlanul a teljes vágást járja be; a
többi csak a *frontier*-t: az előző körben keletkezett gyerekeket **és a
sértést kiváltó leveleket**. A második fél nélkülözhetetlen, és az első,
hibás változatomból hiányzott: a sértés MINDIG a finom oldalról látszik, egy
felosztás viszont csak egy szintet javít, tehát a kiváltó finom levél a
következő körben is sérthet — a durva oldalról ez nem vehető észre (a
szomszéd-területnek nincs fedő őse a vágásban). Hat egyenértékűségi teszt
bukott el rá, mielőtt kijavítottam.

**Igazolás.** `BalanceWorklistEquivalenceTests`: 600 véletlen konfiguráción
(40 seed × 3 mélység × 5 budget, a szorító eseteket is beleértve) a kimenet
**minden esetben azonos** a régi, teljes szkennelésű referenciával, 3,5×
kevesebb bejárt levél mellett. Nagy kaszkádon: 4032 → 14 070 levél,
**11 kör mindkettőnél**, bejárt levelek 129 381 → 25 683 (5,0×), idő
53,0 ms → 12,4 ms (4,3×).

**A költség-modell változása:** `körök × O(|cut|)` helyett
`1 × O(|cut|) + O(felosztások)`.

**3. A plafon — ITT KELL DÖNTENI.** A `RenderBudgetForViewport` a
`MaximumRenderBudget`-tel **vág**, és ez ma köt:

| felbontás | a képlet kérése (8 px cél, 3,0 overhead) | ténylegesen kapott | vágás |
|---|---|---|---|
| 1920×1080 | 97 200 | 48 000 | **2,0×** |
| 2560×1440 | 172 800 | 48 000 | 3,6× |
| 3840×2160 | 388 800 | 48 000 | 8,1× |

Ez közvetlenül a felhasználó #1 visszajelzése („brutál nagyok a tile-ok").

**Ami az emelés ellen szól, és NEM oldódott meg:** a doksi 1. oka, a
**költség-paritás**. 48 000 levélnél a worker-költség 150–158 ms, ami pont
annyi, amennyi a változtatások előtt 8000 levéllel volt. A plafon emelése
ezt a paritást lépné túl — ez UX-kompromisszum, nem technikai kérdés.

**A 2. ok mostani állapota — BECSLÉS, nem mérés.** A doksi szerint 96 000-es
budgetnél a vágás 66 900-nál telítődik, és a balance 4 kör / 108 felosztás /
320 ms. Az új költség-modellből ez ≈ egy kör bejárása + elhanyagolható maradék,
vagyis nagyságrendileg **80–110 ms**. Ezt offline NEM tudtam reprodukálni (a
konkrét eset a Unity-oldali terep-proxit és felszíni metrikát igényli; az én
offline nézetemben a vágás pont a budgeten telítődik, így az ND-116 korai
kilépés lép életbe és a balance 0 kört fut). **A számot a felhasználó
PerfLogja tudja megerősíteni.**

**Opciók.**

- **(A) Marad 48 000.** A költség-paritás sértetlen. A tile-méret-panasz
  megoldását máshonnan kell hozni (pl. a már megemelt split-kvóta, ND-76).
- **(B) Emelés 96 000-re.** A képlet 1080p-s kérésének (97 200) gyakorlatilag
  a teljes kielégítése. Várható worker-költség a doksi táblája + az új
  balance-modell alapján: selection ~73 ms + balance ~80–110 ms ≈ **150–185 ms**
  — vagyis nagyjából a MAI 48 000-es összköltség, mert a selection a
  telítődés miatt nem nő tovább. Ez a becslés a megerősítendő pont.
- **(C) Felbontás-arányos plafon** (pl. `pixelWidth * pixelHeight / 24`), hogy
  4K-n se legyen 8× vágás. Nagyobb munka, és a gyengébb gépeken kockázatos.

**Javaslat: (B), de CSAK élő visszamérés után.** A `adaptiveRenderBudget`
SerializeField Play közben felülírható, tehát a felhasználó ki tudja próbálni
96 000-rel, mielőtt a konstans változik. Ha a PerfLog `balance` sora tényleg
100 ms körül marad, az emelés indokolt; ha 300 ms marad, (A) a helyes.

---

**LEZÁRVA (2026-09-21): (B) — élő mérés alapján.** A felhasználó
`adaptiveRenderBudget = 96 000`-rel végigpróbálta: „nem akad". A PerfLog
ennél többet mond:

```
cutBudget=96000  cutSaturated=False  starvedBudget=0  starvedQuota=0
levelek ~49 216   selection=13,9-65,1 ms   balance=0-43,6 ms
```

**A döntő felismerés, ami a saját becslésemet is megjavította.** Azt
vártam, hogy a worker-költség 150–185 ms-ra nő. Nem nőtt: **14–109 ms**,
vagyis az eddigi 48 000-es szint (150 ms) ALATT. Az ok az, hogy a vágás a
**metrika** szerint ~49 216 levélnél telítődik, nem a budgetnél — a budget
emelése tehát nem kétszerezi a levélszámot, csak megszünteti az éhezést
(48 000-nél `starvedBudget=6943` volt, most **0**). Nem a budget hajtotta a
költséget, hanem a metrika által kért levélszám, és az mindkét plafonnal
ugyanannyi.

A 2. ok (balance-szakadék) az ND-121 munkalistás átírásával megszűnt:
`balance=43,6 ms` 49 000 levélnél, a korábbi 66 900 levélre mért 327 ms
helyett.

A 96 000 nem önkényes: ez a `RenderBudgetForViewport` 1920×1080-as
kérésének (97 200) gyakorlatilag teljes kielégítése.

**Maradék kockázat, kimondva:** a mérés EGY nézetállásra vonatkozik, ahol a
metrika 49 216 levelet kért. Egy olyan nézetben, ahol a metrika tényleg
96 000 közelébe megy, a selection/balance arányosan drágulna — ilyet ebben a
munkamenetben nem láttunk (`cutSaturated=False` MINDEN mintában). Ha
előfordul, az `adaptiveRenderBudget` továbbra is kézi visszafogást ad.

**Amit ez a felhasználó #1 panaszából megold:** a 48 000-es plafon 6943
felosztást éheztetett ki; most nulla. A tile-méret panasz mindkét ága
(split-kvóta ND-76, plafon ND-121) le van zárva.

**Verziózás:** nem seed-törő (a vágás megjelenítési döntés, nem világmodell).

### ND-122 — A terrain-bázis lemez-gyorsítótár érvényesítése: újraszámolás, nem verziókonstans (LEZÁRVA)

**2026-09-20.** A hideg Build statikus sarok-terrain-bázisa élesben mérve
**7,4–8,5 s** (a `terrainBasis=` PerfLog-sor), level 8-on 54,4 MiB nyers adat.
A todo.md 9. sora lemez-gyorsítótárat javasolt, három kikötéssel: **nem lehet
world-state**, **eltérő algoritmusverziót nem tölthet be**, és kell
méret-/I/O-/invalidációs terv.

**A döntés, amit meghozni kellett:** miből tudja a betöltő, hogy a fájl a
*mostani* algoritmussal készült?

- **(A) Kézzel emelt verziókonstans.** A szokásos megoldás, és pont az a
  törékeny: akkor bukik el, amikor valaki a `DomainWarp`-ot vagy a
  `CrustElevation.ComputeNoiseBasis`-t módosítja és **elfelejti** emelni a
  számot. A következmény csendes: egy másik világ domborzata töltődne be,
  látható hibaüzenet nélkül. Ez az I1 (determinizmus) invariáns sérülése
  lenne, a legrosszabb fajtából — észrevehetetlen.
- **(B) A forrásfájlok hash-e.** Automatikus, de túl érzékeny (egy komment
  átírása is érvénytelenít) ÉS nem elég pontos (a Core NuGet-/fordítóverzió
  változását nem látja).
- **(C) Újraszámolásos validáció.** Betöltéskor a fájlból vett, széles szórású
  mintát ÚJRASZÁMOLJUK és bitre hasonlítjuk.

**Választás: (C).** Ez az egyetlen, ami nem emberi figyelemre épít: az
ellenőrzés *ugyanazt a függvényt* futtatja, amit a cache tárol, tehát bármely
algoritmus-változás automatikusan érvényteleníti a fájlt.

**Paraméter:** 1024 minta (`ValidationSampleCount`), Knuth-féle szorzóprím
lépésközzel szórva, az első és utolsó indexszel kiegészítve. Mérve: a
validáció ~18 ms a 7400–8500 ms helyett — 0,25% ráfordítás.

**A maradék kockázat, kimondva.** Ha egy változás a bejegyzéseknek csak
töredékét érinti, a mintavétel elvileg átengedheti. 1024 minta mellett egy
1%-nyi bejegyzést érintő változás észlelési valószínűsége ~99,996%; egyetlen
bejegyzést érintőé viszont elhanyagolható. A teljes újraszámolás elvenné a
gyorsítótár értelmét, ezért ez **tudatos kompromisszum**, nem figyelmetlenség.
Aki ennél szigorúbbat akar, emelje a mintaszámot — a költség lineáris.

**A másik két kikötés.**
- *Nem world-state:* a betöltés bármilyen kétségnél `null`-t ad; nincs
  „javítás" útvonal és nincs részleges betöltés. A fájl törlése csak lassít.
  Minden I/O-hiba elnyelt: a Build sosem bukhat el a gyorsítótáron.
- *Méret/I/O:* a fájlméret előre kiszámítható (`ExpectedFileSize`); a
  könyvtár kvótája 512 MiB (≈9 level-8-as világ), a legrégebben írt fájlok
  esnek ki. Az írás ideiglenes fájlba megy, majd átnevezés — egy félbeszakadt
  írás nem hagy hátra betölthető fél-fájlt.

**Mérés (level 8, 396 294 bejegyzés, 54,4 MiB, memóriában):** szerializálás
118 ms (ebből ellenőrzőösszeg 52 ms), visszaolvasás + ellenőrzés 99 ms.
Ehhez jön a fizikai lemez-olvasás és a valós újraszámolásos validáció
(~18 ms). Várható összes: **~120–230 ms a 7,4–8,5 s helyett.**

**Verziózás:** nem seed-törő — a gyorsítótár származtatott adat, a világmodell
nem függ tőle.

### ND-124 — A folyó-forrás kiválasztás sűrítése egy vízgyűjtőn belül (LEZÁRVA: (A))

**2026-09-21.** A #5 visszajelzés: „nincs tree alakzat, nagyon tirkák a
folyók, sosem ér bele egyik a másikba… az egész bolygón ritkák a folyók."

**Amit a vizsgálat kimutatott.** A dendritikus összefolyás **támogatott** és
működik (`claimed` térkép, `MergedIntoRiverIndex`,
`ComputeDischargeWeights`). A hiba nem ott volt, hanem a forrásszámban:
`DefaultSourceTopK = 12`. És ez sem hidrológiai döntés volt, hanem
**költség-korlát** — a folytonos nyomvonal-követő ~0,8 s/folyó, és
*szekvenciálisan* futott a megosztott `claimed` térkép miatt.

**A költség-korlát feloldva.** A `claimed` térkép a követésben KIZÁRÓLAG a
megállást befolyásolja, a lépésirányt nem — tehát a folyók egymástól
függetlenül, párhuzamosan követhetők, majd forrás-sorrendben csonkolhatók.
`BuildContinuousRiverNetworkFromSourcesParallel`: **bitre azonos** kimenet
(a pontok, a megállási ok és a teljes vízhozam-fa is), mérve **3,7–5,2×**
gyorsabb. `DefaultSourceTopK` 12 → **48** (kb. 7–8 s háttérszálon).

**Ami ezzel MEGOLDÓDOTT:** a folyók sűrűsége (4× több), és van
fa-szerkezet: 12 forrás → **1** összefolyás, 48 forrás → **15**.

**Ami NEM oldódott meg, és ez a nyitott kérdés.** A fa **sekély**: 40
forrásnál a legnagyobb vízhozam-súly **2** — vagyis tipikusan egy
mellékfolyó, nem egy többszintű hálózat. Az ok a forrás-KIVÁLASZTÁS: a
globális „legcsapadékosabb top-K" a legnedvesebb hegyvidékek **között**
szórja szét a forrásokat, nem **egy vízgyűjtőn belül** sűríti őket. Egy
valódi dendritikus fához ez utóbbi kell.

**Opciók.**

- **(A) Vízgyűjtő-alapú kvóta.** A `FlowNetwork` már számol vízgyűjtőket
  (`FindWatershedRegions`) és folyás-szülőt (`ParentIndex`). Válasszunk a
  legnagyobb N vízgyűjtőt, és mindegyiken belül K forrást — így a forrásokat
  garantáltan közös torkolat felé tartó ágakra tesszük. Ez adja a
  legmélyebb fát a legkevesebb folyóval.
- **(B) Egyszerűen még több globális forrás** (pl. 200). A sűrűség nő, a fa
  mélysége viszont csak lassan — és a költség lineárisan (kb. 30 s
  háttérszálon).
- **(C) A hálózat topológiája a flow-networkből**, a folytonos követő csak
  simításra. A fa így **konstrukció szerint** helyes (minden tile egyetlen
  szülőhöz folyik), és a vízhozam a flow-akkumulációból jön. Ez a
  legerősebb, de a legnagyobb átalakítás — és a régi, tile-középpontos út
  épp a blokkosság miatt lett leváltva, tehát a simítást gondosan kell
  megtartani.

**Javaslat: (A).** A meglévő vízgyűjtő-szegmentálásra épül, nem igényel új
algoritmust, és pontosan azt célozza, ami hiányzik (egy medencén belüli
sűrűség). (C) a hosszú távú helyes válasz, de csak akkor érdemes
belevágni, ha (A) mérése szerint a fa még mindig sekély.

**Verziózás:** nem seed-törő. A forrás-kiválasztás megjelenítési/forrás-
választási réteg (a hívás feletti komment szerint „vizuális/forrás-
kiválasztási réteg, nem a világmodell része"), és a `topK` emelése a
meglévő sorrend első 48 elemét veszi — a korábbi 12 részhalmaza.

---

**LEZÁRVA (A), 2026-09-21** — felhasználói döntés („legyen A"), implementálva:
`RiverPathTracing.SelectRiverSourcesPerBasin` +
`BuildRiverNetworkPerBasin`, bekötve a viewer `Build()`-jébe.

**Amit az implementáció közben a MÉRÉS derített ki — és ami átírta a tervet.**
Az (A) leírása szerint „a legnagyobb N vízgyűjtő, mindegyikben K forrás".
Az első változat pontosan ezt csinálta, a meglévő globális csapadék-küszöbbel
(a szárazföldi eloszlás 80. percentilise) együtt — és **0 forrást** adott,
minden paraméterezésnél. A diagnosztika (seed `0xA7C944210000`, level 6):

- 8602 szárazföldi tile, **2467** vízgyűjtő, a legnagyobb **108** tile-os —
  vagyis a level-6 priority-flood minden apró parti lefolyást külön
  medencének lát (a kulcs a torkolat óceán-tile-ja);
- a 6 legnagyobb vízgyűjtőben **egyetlen** tile sincs a globális 80.
  percentilis fölött.

Az ok szerkezeti: a nagy vízgyűjtők ott vannak, ahol sok a szárazföld
(kontinens-belső), a legnedvesebb tile-ok viszont a keskeny, csapadékos parti
hegyvidékeken — a két szűrő metszete üres. Ezért a csapadék-küszöb a
medencén belül **relatív** lett (a medence saját legnedvesebb tile-jai); a
magasság-küszöb (hegyvidék) maradt abszolút. Hozzájött egy
`minBasinTiles` küszöb is (2-3 tile-os parti lefolyásban nincs hova
összefolyni).

**Mért eredmény** (ugyanaz a seed, level 6, `fineDepth` 4, folytonos követő,
16 mag):

| Forrás-kiválasztás | Forrás | Összefolyás | Max vízhozam-súly | Súly ≥ 3 | Idő |
|---|---|---|---|---|---|
| globális top-K | 48 | 8 (17%) | 2 | **0** | 6,5 s |
| medence 6×8 | 48 | 21 (44%) | 6 | 6 | 19,9 s |
| medence 12×6 | 72 | 27 (38%) | 5 | 9 | 26,0 s |
| **medence 16×6** (alapértelmezés) | **96** | **40 (42%)** | **6** | **13** | **32,3 s** |
| medence 20×5 | 100 | 39 (39%) | 5 | 13 | 31,7 s |

A lényeg a „Súly ≥ 3" oszlop: a globális top-K-nál **egyetlen** folyó sincs,
amibe kettőnél több ág futna be — tehát nincs többszintű hálózat, csak
„fő ág + egy mellékfolyó" párok. Ez a „nincs tree alakzat" visszajelzés
számszerű megfelelője.

**Alapértelmezés: 16 medence × 6 forrás.** Kevesebb medence mélyebb fát ad
(6×8: 44% összefolyás), de a folyókat a bolygó néhány pontjára sűríti — ami
éppen a MÁSIK panasz („az egész bolygón ritkák a folyók"). A 16 külön
folyórendszer eloszlik a szárazföldeken, és a fa is többszintű marad.

**A költség 5×** (6,5 s → 32 s háttérszálon, a `Build()`-et nem blokkolja).
Nem a hosszabb nyomvonalak miatt: a medence-források a kontinens
BELSEJÉBEN indulnak, ahol sokkal több a pit-escape (lokális priority-flood).
Mérve: ugyanez szekvenciálisan 54,6 s egy szálon, tehát a párhuzamosítás
2,5×-öt hoz — a maradék a terheléskiegyenlítetlenség (egy-két nagyon hosszú
folyó uralja a farkat). Ha ez zavaróvá válik, a következő lépés a
kör-alapú (round-major) forrás-sorrend: a medencék ELSŐ forrásai külön
körben, előre, és a későbbi körök a már lefoglalt főágnál korán megállnak —
a szekvenciális szemantika, tehát bitre azonos kimenettel.

**Determinizmus.** A kiválasztás tiszta függvény: a vízgyűjtők méret szerint
csökkenően (döntetlennél a torkolat `TileId.Value`-ja szerint), a jelöltek
csapadék szerint csökkenően (döntetlennél `TileId.Value` szerint) rendezve —
nincs szótár-bejárási sorrendtől való függés (külön teszt méri, fordított
beszúrási sorrendű szótárakkal). A minimális forrás-távolság küszöbe
`DeterministicMath.Cos`-szal számolódik, NEM `Math.Cos`-szal (ND-27): a
küszöb közvetlenül eldönti, mely tile-ok lesznek források, tehát a kritikus
úton van.

**Tesztek:** `tests/WorldGen.Core.Tests/Hydrology/PerBasinRiverSourceTests.cs`
(7 teszt): medencénkénti kvóta és méret-sorrend, `minBasinTiles`,
minimális forrás-távolság (a megmaradt párok tényleges távolsága is mérve),
magasság-küszöb, tisztaság + szótár-sorrend-függetlenség, élesetek
(üres világ, érvénytelen paraméterek), és a lényeg: valódi világon a
medence-kvóta TÖBB összefolyást ad, mint az ugyanannyi forrást használó
globális top-K (mérve a durva hálózaton: 37 vs 21).

### ND-125 — A lemezek „rendkívül szabályosak": overlay-hiba volt, a méret-eloszlás viszont nyitott kérdés

**2026-09-21.** Visszajelzés (#4): „a lemezek rendkívül szabályosak, szinte
mindegyik egy négyszög vagy háromszög, némelyik pedig brutál nagy."

Ez a tétel **seed-törőnek volt beütemezve** (verzió-emelés + ND-09 ordinális
kalibráció újrafuttatása). A vizsgálat kiderítette, hogy a panasz nagyobbik
fele **nem a világmodellről szól**.

#### 1. rész — az alak: I3-sértés az overlayben (JAVÍTVA, nem seed-törő)

A `PlanetGridMesh.TectonicPlateColorAt` a **NYERS** pozícióval hívta a
`PlateGeneration.AssignPlate`-et. A világmodell viszont — `SeaLevelCalibration`,
`RiverPathTracing`, a teljes domborzat-lánc — a **WARPOLT** pozícióval kérdez
(ND-36: „a tiszta »legközelebbi mag« gömbi Voronoi-felosztás MATEMATIKAILAG
MINDIG sima, nagykör-ív-szerű határvonalat ad").

Az overlay tehát egy **másik lemez-felosztást** rajzolt, mint amit a domborzat
használ. Mérve (level 7, 98 304 tile, három seed):

| | nyers (az overlay) | warpolt (a világmodell) |
|---|---|---|
| eltérő hozzárendelés | — | **19,5–26,4%** a tile-okból |
| kerület/√terület | 4,9–5,2 | **7,2–7,9** (1,40–1,59×) |

A kerület/√terület izoperimetrikus hányados a lényeg: a nyers felosztás
értéke a konvex sokszögekére jellemző — mert *az is*, definíció szerint. A
felhasználó pontosan ezt látta. Ez ugyanaz a hibaosztály, mint az **ND-119**
(a szél-overlay nem a szimuláció szelét mutatta).

**Javítás:** új kanonikus belépési pont, `PlateGeneration.AssignPlateWarped`
(warp + assign egy helyen, dokumentálva, hogy lemez-hovatartozást
MEGJELENÍTENI csak ezzel szabad); az overlay ezt hívja.

**Költség:** 0,022 → 8,3 µs/sarok (a warp három 3-oktávos fBm-kiértékelés).
24 576 sarokra ~205 ms egy szálon, de a sarok-szín-előszámítás párhuzamos, és
ez a nagyságrend megegyezik a már elfogadott szél-overlay-ével (6,3 µs/sarok).
**Olcsóbb warpot nem szabad használni:** ha az overlay más paraméterekkel
warpol, mint a világmodell, visszatér ugyanez a hiba, csak halkabban.

**Nem seed-törő:** a világmodell egyetlen bitje sem változik — eddig is a
warpolt felosztást használta. Csak a megjelenítés igazodik hozzá.

#### 2. rész — a méret: a mérés ELLENTMOND a benyomásnak (NYITOTT)

A „némelyik brutál nagy" valódi modell-tulajdonság, nem overlay-hiba. Megmérve
(level 7, warpolt hozzárendelés, lemez-terület a bolygófelszín %-ában):

| Seed | Legnagyobb | Legkisebb | Top 3 együtt |
|---|---|---|---|
| `0xA7C944210000` | 12,1% | 1,6% | 31,1% |
| `0x1234` | 8,7% | 1,5% | 25,0% |
| `0xDEADBEEF` | 10,4% | 0,6% | 27,1% |
| **Föld** | **20,4%** (Pacific) | **0,05%** (Juan de Fuca) | **42,4%** |

A generált eloszlás tehát **egyenletesebb**, mint a Földé, nem szélsőségesebb:
a legnagyobb lemezünk feleakkora részt foglal el, mint a Csendes-óceáni, és
nincsenek apró lemezek sem. Az ok: 20 **egyenletes eloszlású** magpont
Poisson-Voronoi-ja — a cellaméretek szórása korlátos. A Földön ezzel szemben
néhány domináns és sok apró lemez van.

A „brutál nagy" benyomás valószínűleg **relatív**: ha minden lemez közepes,
egy 12%-os kilóg, és nincs mellette apró, ami léptéket adna.

**Opciók (mind seed-törő):**

- **(A) Maradjon.** A méret-eloszlás már most is hihető tartományban van, és a
  panasz nagyobbik fele (az alak) a megjelenítés javításával megszűnik. Nincs
  verzió-emelés, nincs ND-09 újrakalibrálás.
- **(B) Súlyozott Voronoi.** Minden maghoz determinisztikus súly (pl.
  hatványeloszlásból), és a hozzárendelés `dot − w` szerint dönt. Kis
  kódváltozás, pontosan a hiányzó tulajdonságot adja (néhány domináns + sok
  apró), a Földhöz közelebbi eloszlás. Seed-törő.
- **(C) Több lemez + súlyozás** (20 → 30–40). A „sok apró" textúrát adja, de
  minden per-pont `AssignPlate` lineárisan drágul, és az `AssignPlate` a
  legforróbb úton van (minden tile, minden sarok).

**Javaslat: (A) egyelőre — de a döntés a felhasználóé, és MÉRÉS után.** Az
1. rész javítása után a lemezek először látszanak a valódi, szabálytalan
alakjukban. Előbb nézze meg; ha a méret-eloszlás ezután is zavaró, (B) a
válasz. Fordított sorrendben egy seed-törő változást hoznánk meg egy olyan
benyomás alapján, amit egy megjelenítési hiba okozott.

**Verziózás:** az 1. rész nem seed-törő. A 2. rész (B/C) az lenne:
`AssignPlate` numerikus viselkedése változna → verzió-emelés ÉS az ND-09
ordinális kalibráció újrafuttatása.

### ND-126 — „A térítők közt minden sivatag": a biome-osztályozó NEM LÁTJA a csapadékot (LEZÁRVA: (A))

**2026-09-21.** Visszajelzés (klíma-kalibráció): „sivatagok: ráktérítő
baktérítő közt minden sivatag, fölötte és alatta egy darabig zöld, efölött meg
jég van, nagyon nem életszerű".

A tétel „klíma-konstansok kalibrálása" néven volt beütemezve. **Nem
konstans-probléma.** A `BiomeClassification.Classify(temperatureK, isOceanic)`
szignatúrája a teljes magyarázat: a csapadék **nem bemenet**. Az osztályozó
saját doksija ki is mondja: „a csapadék/nedvesség (§31) halasztva van, ezért
NEM különböztetünk meg pl. sivatagot/esőerdőt". Azóta a csapadék-mező
(`MoisturePrecipitation`) elkészült — a folyó-forrásokhoz, az overlayhez és a
talajhoz használjuk is —, csak a biome-osztályozásba nem került be.

Következmény: négy szárazföldi osztály, tisztán hőmérsékleti küszöbökkel
(−10 °C / +5 °C / +20 °C), a hőmérséklet pedig lényegében a szélesség sima
függvénye → **tökéletes szélességi sávok**. A >20 °C sáv render-színe
`(0.78, 0.72, 0.20)`, azaz homoksárga — ezt olvasta a felhasználó
sivatagnak. Mérve (seed `0xA7C944210000`, level 6, szárazföldi tile-ok):

| Szélességi sáv | Átlag T | Biome-eloszlás a szárazföldön |
|---|---|---|
| 70°..90° | −62,6 °C | Jég 100% |
| 50°..70° | −13,9 °C | Jég 58%, Tundra 42% |
| 30°..50° | +9,4 °C | Tundra 23%, Mérsékelt 77% |
| 10°..30° | +23,1 °C | Mérsékelt 19%, **Trópusi 81%** |
| −10°..10° | +27,6 °C | **Trópusi 100%** |

**A csapadék-mező viszont NEM rossz** — csak nem használjuk. Ugyanaz a mérés,
szárazföldi átlagcsapadék sávonként: 2,89 (Egyenlítő) → 1,25 (10–30°) →
0,51–0,90 (30–50°) → 0,07–0,10 (50–70°) → 0,00 (sark). Ez kvalitatíve a
helyes alak (nedves Egyenlítő, szárazabb szubtrópus), csak laposabb, mint a
Földé.

**A döntő szám.** Egy HŐMÉRSÉKLETI sávon belül mekkora a csapadék szórása?

| Hőmérsékleti osztály | tile | P10 | medián | P90 | P90/P10 |
|---|---|---|---|---|---|
| Trópusi | 2648 | 0,04 | 1,45 | 5,65 | **140×** |
| Mérsékelt | 2452 | 0,00 | 0,42 | 1,86 | nagyon nagy |
| Tundra | 1446 | 0,00 | 0,12 | 0,69 | nagyon nagy |

Egy másik seeden a trópusi sávban 52×. Vagyis **ugyanazon a szélességen már
most is van száraz és nedves szárazföld** — pontosan a Föld mintázata (Szahara
és Kongó azonos szélességen). Az információ megvan, csak eldobjuk az
osztályozásnál.

**Opciók.**

- **(A) Whittaker-jellegű 2D osztályozás** (hőmérséklet × csapadék). Új
  biome-értékek: Desert, Grassland/Steppe, Savanna, TemperateForest,
  Rainforest, Boreal/Taiga a meglévő Tundra/IceSheet mellé. Ez a standard
  megközelítés, és pontosan azt a mintázatot adja, ami hiányzik.
- **(B) Minimális: aridity-módosító.** A négy hőmérsékleti osztály marad, de
  egy száraz/nedves jelző mellé kerül → csak `Desert` jön létre újként
  (trópusi+száraz, mérsékelt+száraz). Kisebb változás, kevesebb új szín, de
  a „zöld mérsékelt öv" továbbra is egységes marad.
- **(C) Csak a render-színeket változtatni.** ELUTASÍTVA: I4-sértés lenne —
  a panel továbbra is „Trópusi"-t írna oda, ahol a kép esőerdőt mutat.

**Javaslat: (A).** A (B) a panasz felét oldaná meg, és utána ugyanide
jutnánk. A csapadék-mező már létezik, gyorsítótárazott (cache-találatnál
0,0 ms), és a viewerben már ott van `_adaptivePrecip` néven, a finomabb
szinteken pedig a `TryGetReferenceAncestorValue` minta már megoldja a
referencia-szintű mező lekérdezését — tehát nincs új infrastruktúra.

**KÜSZÖB-TERVEZÉS — ez a rész nem szabadon választható.** A csapadék
egysége a modellben **nem mm/év**, hanem önkényes nedvesség-egység, ami
függ a `DefaultPrecipBaseFraction`-től, az iterációszámtól és a
bolygóparaméterektől. Abszolút küszöb („ha precip < 0,3, akkor sivatag")
ezért **világfüggő** lenne, és más bolygóparamétereknél értelmetlenné válna.
A küszöböknek a SZÁRAZFÖLDI ELOSZLÁS PERCENTILISEINEK kell lenniük —
ugyanaz a minta, mint a folyó-forrásoknál (`DefaultPrecipPercentile`), és
ugyanaz a tanulság, mint az ND-124-ben (ott a globális küszöb és a
medence-szűrés metszete üres lett).

**Hatókör-figyelmeztetés.** A biome-mező fogyasztói: régiónév-generálás
(`DominantBiome` → `NameGeneration`), talaj-termékenység, a panelek
biome-sorai, a render-kategóriák és színek. Mindegyik érintett; az új
biome-értékekhez render-szín és név-kulcs is kell.

**Verziózás:** nem seed-törő a domborzat értelmében (a `TileId`→eleváció
leképezés nem változik), de a **világ biome-mezője és a régiónevek
megváltoznak** — verzió-emelés kell, és a mentés-kompatibilitás (ND-108)
szempontjából ez pont olyan eset, amire az a kapu való.

---

---

**LEZÁRVA (A), 2026-09-21** — felhasználói döntés, implementálva.

**A táblázat.** Két hőmérsékleti oszlop × négy csapadék-sor, a hideg vég
csapadéktól függetlenül (a sarkvidéki „hideg sivatag" is jég/tundra):

| | 5–20 °C | 20 °C fölött |
|---|---|---|
| nedves | Rainforest | Rainforest |
| közepes | TemperateForest | Savanna |
| félszáraz | Grassland | Grassland |
| száraz | Desert | Desert |

**MÉRT eredmény** (seed `0xA7C944210000`, level 6, szárazföldi tile-ok) —
hány KÜLÖNBÖZŐ biome van egy 20°-os szélességi sávon belül:

| Sáv | Előtte | Utána |
|---|---|---|
| 30°..50° | 2 | **6** |
| 10°..30° | 2 | **5** |
| −10°..10° | **1** | **4** |
| −30°..−10° | 2 | **5** |
| −50°..−30° | 2 | **5** |

Az Egyenlítő sávja korábban **100% Trópusi** volt — egyetlen osztály az egész
sávban. Most: Sivatag 17%, Sztyeppe 16%, Szavanna 29%, Esőerdő 38%. A
szélességi sávok feltörtek.

**IMPLEMENTÁCIÓ KÖZBEN TALÁLT TERVEZÉSI HIBA — a küszöb POPULÁCIÓJA.**
Az első változat a vágópontokat a TELJES szárazföldi eloszlásból számolta.
Eredmény: az arid vágópont pontosan **0,000** lett (a szárazföld több mint
20%-ának nulla a csapadéka), és a szárazföld **24,4%-a** lett esőerdő — a
Földön ez ~7%. Az ok szerkezeti: a hideg tile-okat a HŐMÉRSÉKLET dönti el, a
csapadékuk viszont szisztematikusan 0 körüli, tehát lehúzzák a
percentiliseket, és a meleg sáv minden tile-ja „nedvesnek" látszik.

Javítás: `ComputeThresholdsForVegetatedLand` — a vágópontok CSAK azokból a
tile-okból, amelyeket a csapadék-tengely egyáltalán osztályoz
(`TundraThresholdK` fölött). Utána a vágópontok 0,109 / 0,604 / 1,999, és a
globális eloszlás értelmes.

**Ez pontosan az ND-124 hibaosztálya**: ott a globális csapadék-percentilis és
a vízgyűjtő-szűrés metszete lett üres, mert két KÜLÖNBÖZŐ populációra
vonatkozó küszöböt kombináltunk. Harmadszor jött elő ugyanez a minta — a
percentilis-küszöbnél MINDIG ki kell mondani, MELYIK populáció eloszlásáról
van szó.

**A kalibráció mostantól a percentiliseken múlik, és ez szándékos.**
A vegetált szárazföld konstrukció szerint 20% / 25% / 30% / 25% arányban
oszlik Sivatag / Sztyeppe / közepes / Esőerdő között (`AridPercentile`,
`SemiAridPercentile`, `MoistPercentile`). Hogy ezek az arányok jók-e, az
hangolási kérdés, nem szerkezeti — a felhasználó vizuális ítélete dönti el.

**Ami MÉG MINDIG nem földszerű, és ez az ND-126b:** a globális szárazföldi
eloszlásban jég + tundra **40,7%** (a Földön ~18%). Ez nem az osztályozó
hibája — a hőmérsékleti lánc túl meredek sark–egyenlítő esést ad.

**MELLÉKTERMÉK — egy látens determinizmus-hiba.** A `FeatureSegmentation.DominantBiome`
döntetlenjét 2026-09-21-ig a `Dictionary<Biome,int>` BEJÁRÁSI SORRENDJE
döntötte el; explicit szabály nem volt. Ez I2-sértés, csak addig nem bukott
ki, amíg két szárazföldi osztály létezett. Az öt osztállyal a
Python-referencia és a C# azonnal MÁS régiónevet adott ugyanabból az adatból
(„Sylthal Plains" vs „Sylthal Veld"). Javítva mindkét oldalon: a KISEBB
`Biome` enum-érték nyer, tesztbe zárva.

**Verziózás:** a domborzat NEM változott (a `TileId`→eleváció leképezés
érintetlen), de a világ **biome-mezője és a régiónevei megváltoztak**.

**Hatókör, ami tényleg átment:** `Biome` enum (Temperate/Tropical →
Desert/Grassland/TemperateForest/Savanna/Rainforest), `NameGeneration`
utótagok, a viewer `RenderCategory`-ja, kategória-színei és a FOLYTONOS
felszínszín (mostantól kétdimenziós keverés: a csapadék-tengely mentén
Desert→Grassland→közepes→Rainforest, a hőmérséklet csak a „közepes" horgonyt
váltja). A `Build()` mostantól MINDIG kiszámolja a csapadék-mezőt (korábban
csak overlay/folyók/felhők kérték) — gyorsítótár-találatnál 0,0 ms.
Törölve: a halott `ContinuousSurfaceColor`.

**Python-referencia:** `tools/reference/biome_ref.py` (classify + mindkét
küszöb-függvény), 200 új tesztvektor, és a `features_ref.py` is a
csapadék-proxyra állt (a szegmentálási referencia nem klíma-referencia).

#### ND-126b — klíma-konstansok kalibrálása (LEZÁRVA)

**2026-09-22. Megvalósítva Python-referencia és C# regressziós mérés alapján.**

Ezek valódi kalibrációs hibák, az (A)/(B) döntéstől függetlenül:

1. **A termikus szél korlátlan a sarkoknál.** Átlagos szélsebesség
   szélességi sávonként: **7,4** m/s az Egyenlítőn, **50** m/s 50–70°-on,
   **120–126** m/s 70–90°-on. Az alap-zonális szél 10 m/s
   (`BaseWindSpeed`), a többi a `ThermalWindCoeff * dT/d(észak)` tagból jön,
   ami K/radiánban mér, és a sarkok felé elszabadul. 120 m/s felszíni szél
   nem fizikai (a futóáramlás is 50–70 m/s, magasban). Ez visszahat a
   párolgásra (`EvapWindCoeff`, 30-as sapkával) és az advekció sebességére.
   Döntés: a kétdimenziós termikus vektor irányát megtartva a nagyságát
   `30 m/s * tanh(|v| / 30 m/s)` alakban korlátozzuk, még a Coriolis-forgatás
   előtt. A zonális alapkomponens nem része a korlátozásnak.

2. **A sark–egyenlítő hőmérsékleti esés túl meredek.** Szárazföldi átlag a
   70–90° sávban **−62 °C** (napéjegyenlőségi pillanat), az Egyenlítőn
   +27,6 °C. A Földön az évi átlag a Déli-sarkon ~−50 °C, az Északin ~−18 °C.
   Emiatt a szárazföld 58–100%-a jég 50° felett. Ez a `Temperature` modul
   konstansainak kérdése (ND-41), és a felhasználó „efölött meg jég van"
   megjegyzését közvetlenül magyarázza. Döntés: a hiányzó meridionális
   hőszállítást `T_transport = 40 K * sin⁴(szélesség)` additív proxyval
   közelítjük. Ez az Egyenlítőn nulla, a póluson 40 K, és nem használ új
   transzcendens függvényt, mert a normalizált pozíció `z` komponensének
   negyedik hatványa adja.

**Mért eredmény ugyanazon a kanonikus világon** (`0xA7C944210000`, level 6,
65% víz, napéjegyenlőség): a hideg szárazföld (`IceSheet + Tundra`) aránya
**40,71% → 16,91%**; a 70–90° szárazföldi átlag **−62,21 °C → −26,31 °C**;
az egyenlítői átlag gyakorlatilag változatlan (**27,58 °C**). A teljes szél
maximuma **36,79 m/s**, a 70–90° sáv átlaga **34,66 m/s**, az egyenlítői
sávé **7,26 m/s**. A csapadék 20/45/75 percentilis-küszöbei nem változtak;
azok vizuális elfogadása továbbra is B4.

Az egyszerű és a teljes hőmérsékletút, valamint a diagnosztikai hőmező azonos
korrekciót kapott; a szélkorlát a régi és az ND-102 hőszélúton is azonos.
A `ThermalModelParameters.ModelVersion` **1 → 2**. A teljes világmentés
generátorverzió-kapuja ekkor még az ND-108/A6 nyitott feladata volt (azóta
2026-09-22-én elkészült); az itt hivatkozott
`.worldpkg` csak definíciót és eleváció-hash-t tárol, klímaállapotot nem.

### ND-127 — A régiók nem „tartoznak össze": a vízgyűjtő-szegmentálás a torkolat óceán-tile-ja szerint kulcsol (LEZÁRVA: (A))

**2026-09-21.** A `todo.md` 1. tábla 3. sora, az ND-124 mérésének
mellékterméke: a navigációs menü régiói nem alkotnak földrajzi egységet.

**A MÉRT diagnózis** (seed `0xA7C944210000`, 20 lemez, víz 0,65):

| | level 5 (a viewer panel-szintje) | level 6 |
|---|---|---|
| szárazföld-tile | 2151 | 8602 |
| vízgyűjtő (`FindWatershedRegions`) | 789 | 2467 |
| ebből ≥5 tile (csak ez látszik a panelen) | 112 | 421 |
| a szárazföld hány %-a van egyáltalán régióban | **50,1%** | **63,3%** |
| régió a legnagyobb landmasson | **65** | **207** |
| térben SZÉTESŐ régió (>1 komponens) | **35,7%** | **20,0%** |
| legnagyobb régió | 31 tile | 108 tile |

Három külön hiba, egy okból:

1. **A szárazföld fele-harmada semmilyen régióhoz nem tartozik.** A panel
   `Count >= 5` szűrése 677 (level 5), illetve 2046 (level 6) apró
   vízgyűjtőt dob el — ezek tile-jai nem navigálhatók, nem kapnak nevet.
2. **Egy régió több, egymástól elszakadt földdarab is lehet.** A régió
   kulcsa a torkolat ÓCEÁN-tile-ja; két külön félsziget is folyhat ugyanabba
   az óceán-tile-ba. Level 5-ön a régiók **több mint harmada** ilyen.
   Szó szerint ez a panasz: a régió darabjai nem tartoznak össze.
3. **65–207 menüpont egyetlen kontinensen**, mindegyik egy keskeny parti
   lefolyás. Ez a szint így navigációra használhatatlan.

A gyökérok egy szintbeli hiba: az óceán-tile szerinti kulcsolás a LEFOLYÁS
azonosítója, nem egy földrajzi egységé. Egy hosszú partszakasz definíció
szerint sok apró, egymástól független vízgyűjtőre esik.

**Opciók.**

- **(A) A vízgyűjtők agglomeratív összevonása cél-méretig.** A vízgyűjtő
  marad az atom (a vízválasztó valódi földrajzi határ — a „Duna-medence"
  pontosan így egység), de a kicsiket összeolvasztjuk a szomszédjukkal,
  amíg el nem érik a cél-méretet. Előbb ÖSSZEFÜGGŐ komponensekre bontunk,
  így a 2. hiba konstrukció szerint megszűnik, és minden szárazföld-tile
  pontosan egy régióba kerül (nincs több `Count >= 5` szűrés).
- **(B) A vízgyűjtő elhagyása, tisztán térbeli felosztás.** A már meglévő
  `PartitionRegionIntoAreas` (k-center + többforrású BFS) a landmassra,
  nagyobb cél-mérettel. Olcsó (kész kód), garantáltan kompakt — de a
  határok nem követnek semmilyen természetes vonalat: a folyó közepén vág
  ketté egy völgyet, és a felosztás ettől „mesterséges rács"-nak látszik.
- **(C) Az ND-05 teljes hibridje** (vízgyűjtő ∪ biome-klaszter ∪
  domborzati törés). Ez a specifikáció eredeti terve, és hosszú távon ez a
  helyes válasz — de három külön klaszterezés összefésülése, saját
  súlyozási paraméterekkel, amiket csak vizuálisan lehet hangolni.

**Javaslat: (A).** A 2. és a 3. hiba konstrukció szerint megszűnik tőle, az
1. is (100% lefedettség), és a határai megmaradnak vízválasztónak — tehát
(C) felé is ez a helyes első lépés: (C) ugyanezen a cella-gráfon csak más
összevonási költségfüggvény lenne.

**Verziózás:** nem seed-törő. A `FindWatershedRegions` és az egész
hidrológiai lánc VÁLTOZATLAN; az összevonás tiszta, utólagos prezentációs
réteg a már kiszámolt vízgyűjtők fölött. A régiónevek viszont
megváltoznak (más tile-halmaz → más domináns biome/morfológia), és a
`SoilFertility` ordinális küszöbök populációja is más lesz — ld. lent.

---

**LEZÁRVA (A), 2026-09-21** — implementálva:
`FeatureSegmentation.MergeWatershedsIntoRegions` +
`RecommendedRegionTileTarget`, Python-referenciával
(`features_ref.py: merge_watersheds_into_regions`) és bitpontos
vektor-teszttel.

**Az algoritmus** (tiszta egész-aritmetika: nincs lebegőpont, nincs random,
nincs szótár-bejárási sorrendtől való függés):

1. **Cellák:** minden vízgyűjtő ÖSSZEFÜGGŐ komponensekre bontva
   (4-szomszédság). Innentől minden cella egyetlen összefüggő földdarab, és
   egyetlen landmasson belül van (két landmass definíció szerint nem
   szomszédos, tehát a landmass-határ átlépése kizárt).
2. **Szomszédsági gráf** a cellák között, élsúly = a KÖZÖS HATÁR hossza
   (hány tile-él érintkezik).
3. **Összevonás:** amíg van cél-méret alatti cella, amelynek van szomszédja,
   a legkisebbet (döntetlen: kisebb kanonikus `TileId.Value`) beolvasztjuk
   abba a szomszédjába, amelyik (a) maga is cél alatt van, ha van ilyen;
   (b) ezen belül a LEGHOSSZABB közös határt osztja vele; (c) döntetlennél
   a kisebb; (d) döntetlennél a kisebb `TileId.Value`-jú.
4. A kimenet méret szerint csökkenő, döntetlennél `TileId.Value` szerinti
   kanonikus sorrendben.

A (b) szabály MÉRT különbség, nem ízlés. A kézenfekvőbb „olvadjon a
legkisebb szomszédba" változat a partvonal mentén elnyúló régiókat épít;
a két szabály a legnagyobb landmasson (level 6, cél 258 tile):

| Partner-szabály | Régió | Legnagyobb | átmérő/√terület | kerület/√terület (átlag) |
|---|---|---|---|---|
| legkisebb szomszéd | 10 | 602 | 2,5–3,5 | 5,79 |
| közös határ | 11 | 699 | 1,9–3,4 | 5,46 |
| **közös határ + cél alatti előny** | **11** | **612** | **1,9–3,4** (a legnagyobbé 2,3) | **5,52** |

(Viszonyítás: egy kompakt folt átmérő/√terület mutatója ~2,0; maga a
legnagyobb landmass 2,9 — a régiók tehát nem nyúlványosabbak, mint a
kontinens, aminek a részei.)

**Cél-méret:** a szárazföld **3%-a** (`RecommendedRegionTileTarget`), nem
fix tile-szám — így a régiók SZÁMA szintfüggetlen (a viewer level 5-ön
panelez, a tesztek/mérések level 6-on futnak). Mérve a legnagyobb
landmasson: level 5 → **10** régió (79–144 tile), level 6 → **11** régió
(265–612 tile).

**MÉRT eredmény** (ugyanaz a seed/paraméterezés, mint a diagnózisnál):

| | level 5: előtte → utána | level 6: előtte → utána |
|---|---|---|
| lefedett szárazföld | 50,1% → **100%** | 63,3% → **100%** |
| szétesett (>1 komponens) régió | 35,7% → **0** | 20,0% → **0** |
| régió a legnagyobb landmasson | 65 → **10** | 207 → **11** |
| összevonás költsége | **19 ms** | **43 ms** |

**Következmény, amit ez a döntés NEM old meg** (külön tétel a `todo.md`-ben):
a `SoilFertilityThresholds` (ND-09/ND-117) kalibrációs populációja a ≥5
tile-os VÍZGYŰJTŐ-régió volt; az összevont régiók nagyobbak, tehát az
átlagolt termékenység eloszlása eltolódik. MÉRVE, ugyanazon az 500 világon:
v1 (vízgyűjtő) p20/p80 = 0,1946/0,2596, v2 (összevont régió)
0,1577/0,2302 — a régi küszöbökkel az összevont régiók ~40%-a esne a
legalsó kvintilisbe a 20% helyett. **Ez ebben a menetben megtörtént**: az
`OrdinalCalibration` is az összevont régiókat mintázza, és a
`SoilFertilityThresholds` v2-re cserélve (23 492 régió-minta). Ellenőrzés:
a Habitability és a CoastalComplexity vágópontjai BITRE ugyanazok
maradtak, tehát tényleg csak a talaj-populáció változott.

### ND-128 — A `useGpuGeometry` út: az ND-120 másik fele (LEZÁRVA: (A) — törölve)

**2026-09-21.** Az ND-120 a GPU-**osztályozó** utat törölte, de a
`TileClassification.compute` assetet megtartotta, mert a
`useGpuGeometry` (GPU-**geometria**) ág is azt hívja — és **az is ugyanazt
az elavult `BaseElevationF`-et**. Az ND-120 ezt kimondottan a hatókörén
kívül hagyta („nem volt része a döntésnek, ezért nem nyúltam hozzá"). Ez a
döntés zárja be azt a rést.

**A hiba MÉRVE, ma is érvényes.** A `GpuShaderElevationParityTests`
(seed `0xA7C944210000`, 20 lemez, level 6; ma újrafuttatva: 3/3 zöld) a
shaderből hiányzó két tagot méri:

| hiányzó tag | \|Δ\| átlag | \|Δ\| max | óceán/szárazföld átfordulás | biome-átfordulás |
|---|---|---|---|---|
| ND-52 másodlagos zaj | 282,0 m | 890 m | 21,83% | 23,43% |
| ND-90 határkeverés | 23,4 m | 3071 m | 0,39% | 0,40% |
| **a shader tényleges állapota** | **300,8 m** | **3116 m** | **~22%** | **~24%** |

A teszt küszöbe (`OceanFlipFraction` 0,15–0,30) ma is teljesül, tehát a
szám nem avult el. A mérés mindkét oldalon float64 — a shader float32-es
vesztesége **nincs** benne, vagyis ez ALSÓ korlát.

**Amit a vizsgálat ezen FELÜL talált — és ami eldöntötte a kérdést.**

1. **A GPU-ág mérve LASSABB, mint a CPU-ág.** A mező saját doksija rögzíti
   a 2026-09-02-i élő mérést: **~50 s/újraépítés**, mert a kernel minden
   tile-hoz külön számolja mind a 4 sarkot + a középpontot (5× a teljes
   fraktál-zaj-lánc tile-onként), míg a CPU-út a
   `_persistentCornerColorCache`-en keresztül a SZOMSZÉDOS tile-ok között
   megosztott sarkokat csak egyszer számolja. Sarok-szintű (nem tile-szintű)
   dispatch kellene hozzá — az újraírás, nem karbantartás.
2. **Hiányzik belőle a geomorphing** (a LOD-váltás fokozatos átmenete), tehát
   bekapcsolva „pattanás" látszik finomodáskor.
3. **Öt másik, ma is fejlesztett út köré fonódik** `!useGpuGeometry`
   feltételként: aszinkron mesh-újraépítés (ND-47 3. fázis), szakaszolt
   terep-upload (ND-85), önálló víz-finomítás (ND-82/83), terep-LOD-proxy
   (ND-74) és a kirajzolt méret diagnosztika (ND-75). Mindegyik azt jelenti:
   *ha ezt bekapcsolod, a fél viewer visszaesik egy régebbi útvonalra.*
4. **A `RebuildAdaptiveMesh` CPU-ága ELÉRHETETLEN kód volt.** A metódus
   elején `if (!useGpuGeometry || tileClassificationCompute == null) { CPU; return; }`
   áll, tehát a lentebbi `else` ág (klasszifikáció + sarkak +
   `EmitAdaptiveTile` ciklus) sosem futhatott — a szinkron CPU-utat az
   ND-47 óta a `ComputeAdaptiveMeshBuffersCpu` viszi. Ez ~90 sornyi halott
   kód volt, ami mellesleg úgy nézett ki, mintha a CPU-út két helyen élne.

**Opciók.**

- **(A) Az út törlése** (mező, `EmitAdaptiveTilesGpu`,
  `GpuTerrainGeometryGenerator.cs`, `TileClassification.compute`, a
  jelenetbeli hivatkozások és az öt `!useGpuGeometry` feltétel). Ugyanaz az
  indoklás, amit az ND-120-nál elfogadtunk: halott kód, ami minden jövőbeli
  Core-változásnál kézi utánavezetést követelne, és amíg létezik, egy
  Inspector-kattintással más bolygót lehet renderelni.
- **(B) A shader felzárkóztatása** (ND-52 + ND-90 HLSL-portolása) + GPU-s
  egyezési teszt. A CI-gépeken nincs GPU, a shader kétszer futott
  „Compiler timed out"-ba, és a végeredmény **továbbra is lassabb** lenne a
  CPU-útnál (1. pont) — vagyis a karbantartási terhet egy negatív előjelű
  gyorsításért vállalnánk.
- **(C) Marad, figyelmeztető tooltippel** (a mai állapot). A kockázat
  ilyenkor egyetlen kattintás távolságra marad, és minden Core-változásnál
  újra kell gondolni.

**Javaslat: (A).** (B) akkor lenne védhető, ha a GPU-ág gyorsabb lenne —
mérve nem az. (C)-t az ND-120 már „elfogadható átmenetnek" minősítette; az
átmenet most véget ér.

**Verziózás:** nem seed-törő. A törölt ág **nem futott** (alapból kikapcsolt,
és a jelenetben is `0`), tehát egyetlen generált világ sem függ tőle. A
világmodell (`src/WorldGen.Core`) érintetlen.

---

**LEZÁRVA (A), 2026-09-21** — végrehajtva:

- törölve a `useGpuGeometry` és `tileClassificationCompute` mező, a
  `_gpuGeometryGenerator`, az `EmitAdaptiveTilesGpu` (~150 sor),
  `Assets/Scripts/Viewer/Gpu/GpuTerrainGeometryGenerator.cs` és
  `TileClassification.compute` (a `Gpu` mappa egészében, `.meta`-kkal);
- a `RebuildAdaptiveMesh` a fenti 4. pont miatt **három sorra** egyszerűsödött
  (a cut átadása a CPU-útnak) — a halott ág is elment;
- az öt `!useGpuGeometry` feltétel eltűnt: az aszinkron újraépítés, a
  szakaszolt upload, a víz-finomítás, a terep-LOD-proxy és az ND-75
  diagnosztika innentől feltétel nélkül a normál úton fut;
- a `PlanetView.unity`-ból kikerült a shader-hivatkozás és a kapcsoló.

**A `GpuShaderElevationParityTests` MARAD**, immár egyetlen szereppel: ha
valaki bármikor „egyszerűsített" eleváció-közelítést vezetne be (GPU-n,
előre számolt textúrában, LOD-proxyban), ez megmondja, mit veszít vele —
a tile-ok ~22%-át a tengerszint rossz oldalán.

**Amit ez NEM old meg:** ha a klasszifikáció/geometria valaha tényleg szűk
keresztmetszet lesz, a GPU-út újraírható — de sarok-szintű dispatchcsel,
a Core-eleváció megosztott forrásából, és egy CPU/GPU egyezési kapuval.
A mai shader ehhez nem alap.

### ND-129 — Tó-blokkosság (A9): a partvonalat a TEREP messe ki, ne a tó-poligon

**2026-09-22.** A `todo2.md` A9 sora, felhasználói visszajelzés alapján
(„a tavak zoomra nagy tile-okból összerakottnak néznek ki"). Az ND-49/
ND-124 a FOLYÓ-vonalat vitte finom nyomvonalra; a tavakra a javítás soha
nem terjedt ki.

**A MÉRT diagnózis** (élő Editor, seed `0xA7C944210000`, `hydrologyLevel=8`,
`adaptiveBaseLevel=8`, `level=5`):

| | érték |
|---|---|
| tó-tile összesen (level 8) | **11 505** |
| egy level-8 tile oldala | **~36 km** |
| a legnagyobb tó | 404 tile, felszín **1975,7 m** |
| a tó-tile-ok terep-tartománya | 1602,4 – 2097,2 m |
| tó-tile SARKOK a vízfelszín FÖLÖTT | **10,3%** (166/1616) |
| tó-tile KÖZEPEK a vízfelszín fölött | **0** (0/404) |
| a vízszinthez ±25 m-en belüli sarkok | **14,8%** (239/1616) |
| 1-gyűrű szomszéd, csupa víz alatti sarokkal | **0** (0/146) |
| 1-gyűrű szomszéd, VEGYES (metszi a vízszintet) | **78,1%** (114/146) |
| 1-gyűrű szomszéd, csupa víz feletti sarokkal | 21,9% (32/146) |

**Három, egymástól független ok — ebből kettő valódi:**

1. **A rajzolt tó a level-8 tile-halmaz uniója.** A `BuildLakeSurface`
   tile-onként EGYETLEN quadot rak le (`GetContinuousBounds` → 4 sarok), és
   a halmaz szélén a vízfelszín egyszerűen VÉGET ér a tile-határon. A
   partvonal ezért tengelypárhuzamos, 36 km-es lépcsőkből áll. **Ez a fő ok.**
2. **A lapos quad behúr a gömbbe.** Egy 36 km-es húr közepe
   `R·θ²/8 ≈ 25 m`-rel a helyes sugár ALATT van. A tó-sarkok **14,8%-a**
   ezen a sávon belül van, tehát a behúrás a part menti sávban ténylegesen
   a terep alá viszi a vízfelszínt.
3. **A `RenderCategory.Lake` adaptív ága halott kód.** Az
   `IsAdaptiveLakeTile` az `IsInReferenceLevelSet`-en át a megjelenítési
   `level`-ig (5) sétál vissza, a halmaz viszont level-8 tile-okból áll —
   SOHA nem talál. Nem okoz hibát (az opak vízfelszín úgyis takar), de azt
   jelenti, hogy a tónak NINCS más geometriai forrása a vízfelszín-mesh-en
   kívül. Külön takarítandó.

**A kulcs-megfigyelés.** A terep-mesh adaptív, level 20-ig finomodik, és a
tó vízfelszíne egy ISMERT magasságú, VÍZSZINTES sík. Ahol a terep a sík
fölé emelkedik, ott a terep — pusztán a z-bufferből — KIVÁGJA a vízfelszínt.
Ez ma is működik, csak a tó-tile-ok **10,3%-ányi** sarkánál fordul elő; a
partvonal maradék ~90%-a nyers tile-él. Ha a vízfelszín TÚLNYÚLNA a valódi
parton, a partvonal 100%-ban terep-metszet lenne — és a részletessége
AUTOMATIKUSAN követné a terep-LOD-ot, vagyis zoomra magától finomodna.
A mérés azt is kimondja, hogy ez biztonságos: az 1-gyűrű szomszédok
**78,1%-a VEGYES** (a vízszint áthalad rajtuk), és **egyetlen egy sincs**,
amelyik teljes egészében víz alatt lenne — tehát egy gyűrűnyi kiterjesztés
sehol nem önt el egész tile-t.

**Opciók.**

- **(A) Újramintavételezéses kontúr (marching squares).** A tó-tile-okon
  al-rácson mintázzuk a terepet, és a vízszint-szintvonalat interpolálva
  vágjuk ki a vízfelületet. *ELVETVE, mérés alapján:* a
  `ComputeElevationAtPoint` élőben mért költsége **~53 µs/hívás**
  (`history/2026-09-20-flyto-wind-overlay-split-quota.md`: 4 hívás = 212 µs).
  Egy egyenletes level-12 rács a 11 505 tó-tile-ra 2,9 M hívás ≈ **2,5 perc
  CPU**. Adaptívan (csak a part menti cellák) olcsóbb lenne, de egy MÁSODIK,
  a terep-mesh-től FÜGGETLEN partvonal-definíciót vezetne be — két forrás,
  amik zoomon elcsúszhatnak egymástól.
- **(B) A partvonal a MEGLÉVŐ terep-mesh metszete. ← VÁLASZTOTT.** A tó
  vízfelszíne kiterjed a valódi parton túlra (a tó-tile-ok + 1 gyűrű
  szomszéd), és a már renderelt, adaptív terep vágja ki belőle a partot.
  **Nulla új eleváció-kiértékelés a partvonalhoz**; egyetlen partvonal-
  definíció (a terep), tehát nem csúszhat el; a részletesség a terep-LOD-dal
  együtt, level 20-ig finomodik. A 2. okra (behúrás) a vízfelszín-quadok
  al-osztása a válasz.
- **(C) A globális `hydrologyLevel` emelése.** Ugyanaz az érv veti el, mint
  az ND-49-nél: a `FlowNetwork.PriorityFlood` szekvenciális, level 10+-on
  6,3 M tile-ra kezelhetetlen — és a blokkosságot csak eltolná, nem szüntetné meg.

**A (B) végrehajtási terve.**

1. **Kiterjesztés 1 gyűrűvel.** A `BuildLakeSurface` a tó-tile-ok
   `TileNeighbors.Neighbor` szerinti 1-gyűrűjét is megrajzolja, a tó SAJÁT
   felszín-magasságán. A csupa víz FELETTI sarkú gyűrű-tile-ok (mérve
   21,9%) kimaradnak — ott a vízfelszín amúgy is teljesen takarva lenne.
2. **Al-osztás a behúrás ellen.** Minden rajzolt tile N×N al-quadra bomlik.
   `N=4` (level 10) mellett a behúrás **25 m → 1,6 m**. EGYENLETES al-osztás,
   mert így nincs T-csomópont/hajszálrés a szomszédos tile-ok között.
   Takarékosság: az a tile, aminek MIND a négy sarka legalább 40 m-rel a
   vízszint alatt van, egyetlen quad marad (ott a behúrás láthatatlan) —
   ehhez a T-csomópont a tile ÉLÉN a durva húrra ejtett al-sarkokkal
   kezelendő.
3. **A sarok-elevációk INGYEN vannak.** A döntésekhez (kihagyás/al-osztás)
   level-8 SAROK-elevációk kellenek — a `TryGetStaticTerrainBasis`
   (ND-63/ND-122, lemezre cache-elt) pontosan ezeket adja, tehát nem kell
   új `TerrainPointBasis.Compute`.
4. **`SurfaceElevation` finomítás.** A `LakeInfo.SurfaceElevation` a
   komponens `filled` értékeinek ÁTLAGA. Egy több, eltérő szintű mélyedésből
   összeolvadt komponensnél ez se nem a felső, se nem az alsó szint. A
   per-tile `filled` érték a helyes vízszint — a `MinSurface`/`MaxSurface`
   eltérése mérendő, és ha érdemi, a per-tile érték a rajzoláshoz.

**Amit ez NEM old meg (tudatosan, v1-ben):** a lefolyónál (spill-pont) a
völgytalp a feltöltési szint ALATT van, tehát a gyűrű-kiterjesztés ott egy
legfeljebb egy tile-nyi „nyelvet" adhat a folyó irányába. A mérés szerint
egész gyűrű-tile sosem kerül víz alá, tehát ez korlátos és terep-alakú.
Ha a vizuális ellenőrzésen (B3) zavaró, a `FlowNetwork` `Parent`-térképe
alapján a lefolyás-irányú gyűrű-tile-ok kizárhatók.

**Ha a (2) al-osztás kevésnek bizonyul közeli zoomnál**, a vízfelszín a
meglévő ND-82/ND-83 gépezettel (`WaterLodSource`, `LodCoverage`,
`LodCornerResolver`) kamera-adaptívvá tehető — a partvonal-definíció
ettől NEM változik, csak a sík geometriai pontossága.

**Verziózás:** nem seed-törő (tisztán megjelenítési döntés; a
`LakesIceErosion` detektálása, a `filled` mező és minden szimulációs érték
változatlan, I1–I4 érintetlen).

### ND-130 — A szárazföldi biome-ok „nagy négyszögekben": a csapadék-bemenet egy 288 km-es lépcsős függvény

**2026-09-22.** Felhasználói visszajelzés: *„a zöld színű biom a bolygó
felszínén nagy méretű négyszög alakú régiókban jelenik meg… mintha a biom
determináltan egy-egy nagyon nagy négyszög területre lenne kiszámolva"* —
képpel, pirossal bejelölt, tengelypárhuzamos téglalapokkal. A `todo2.md`-ben
korábban nem szerepelt.

**A gyökérok — kódból egyértelmű.** Az ND-126 óta a biome-osztályozás
KÉTDIMENZIÓS: `BiomeClassification.Classify(temperatureK, isOceanic,
precipitation, thresholds)`. A két bemenet természete viszont
GYÖKERESEN KÜLÖNBÖZŐ:

| bemenet | forrás | felbontás |
|---|---|---|
| hőmérséklet | `TemperatureKelvinAt(x,y,z,…)` — pontszerű, folytonos függvény | LOD-független |
| csapadék | `PrecipitationAtCore` → `_adaptivePrecip[FromPosition(x,y,z, level)]` | **diszkrét, `level` (=5) szintű tile-érték** |

A `MoisturePrecipitation.Compute` globális, iteratív nedvesség-advekció, ezért
csak egy FIX rácson értelmezett; a viewer a REFERENCIA-szinten (`level`) kéri.
A jelenetben `level`=5 → 6·4⁵ = **6144 tile** → egy tile **~288 km** oldalú
(mérve élőben: `precipTiles=6144`).

A régi `PrecipitationAtCore` ennek a mezőnek a NYERS, tile-szintű értékét adta
vissza. Egy küszöb-alapú osztályozó bemeneteként ez azt jelenti, hogy a
szárazföldi biome-határ **pontosan a level-5 tile-élekre esik** — 288 km-es,
tengelypárhuzamos négyzetek. Pontosan ez látszik a képen.

**Ezt a hibát az ND-126 vezette be, és a saját kódja ki is mondta a téves
feltevést.** A `JitteredRenderBiome` kommentje: *„a jitter SZÁNDÉKOSAN csak a
hőmérsékletre hat, a csapadékra nem… a csapadék-határok már eleve
szabálytalanok (a nedvesség-transzport a domborzatot követi), azokat nem kell
rongyolni."* Az ÉRTÉKEK valóban a domborzatot követik — a MINTAVÉTEL viszont
288 km-es lépcsős függvény, tehát a HATÁROK nem szabálytalanok, hanem
négyzetesek.

**MÉRT hatás** (élő Editor, seed `0xA7C944210000`, 98 304 level-7 mintapont a
teljes gömbön, a `_adaptiveBiomeThresholds` vágópontjaival sávokba sorolva):

| | érték |
|---|---|
| megváltozott csapadék-sávú mintapont | **21,0%** (20 597 / 98 304) |
| sáv-eloszlás ELŐTTE | [35 008, 9 920, 21 792, 31 584] |
| sáv-eloszlás UTÁNA | [27 347, 11 324, 23 051, 36 582] |

**Opciók.**

- **(A) A `hydrologyLevel`-hez hasonlóan a csapadék-szint emelése.** Level 6-on
  4×, level 7-en 16× tile. A `MoisturePrecipitation.Compute` per-tile szél + 24
  advekciós iteráció; jelenleg hidegen 300–400 ms, tehát level 7-en 5–6 s
  lenne. ÉS csak KISEBB négyzeteket adna, nem szüntetné meg a lépcsőt.
  Elvetve.
- **(B) A mező BILINEÁRIS interpolációja a tile-sarkok között. ← VÁLASZTOTT.**
  A mező marad a jelenlegi (olcsó) szinten, de a KIÉRTÉKELÉS lesz folytonos.
  Pontosan ez a minta, amit a FELHŐ-réteg 2026-09-06 óta már használ
  (`PrecipAndOceanFractionAtCorner` + GPU-interpoláció) — csak a
  biome-osztályozás sosem kapta meg. Nulla új szimulációs számítás, a mező
  saját értékeiből interpolál, tehát I3/I4 érintetlen.
- **(C) Zaj-perturbáció a csapadékra** (mint az ND-57/ND-59 a hőmérsékletre).
  Ez csak FELRONGYOLNÁ a négyzet-éleket; a 288 km-es blokk-szerkezet
  megmaradna. Önmagában nem elég; a (B) után szükség esetén kiegészítésként
  hozzáadható.

**A (B) végrehajtása.**

1. **Core-kiegészítés:** `TileGeometry.ToFaceUV(x,y,z, out face, out uc, out vc)`
   — a `PositionFromFaceUV` inverze, ami eddig a `FromPosition` TÖRZSÉBEN volt
   elrejtve. A `FromPosition` innentől ezt hívja, tehát a két út definíció
   szerint ugyanazt a (uc,vc)-t látja (nincs viselkedésváltozás). Kell, mert az
   interpolációhoz nem a tile-INDEX, hanem a tile-on BELÜLI pont kell. Nem
   duplikáljuk a verifikált Core-matekot a viewerben.
   Tesztek: `TileGeometryFaceUvTests` (oda-vissza, index-egyezés a
   `FromPosition`-nel minden lapon/szinten, tisztaság, lap-normálisok).
2. **Sarok-tábla** (`BuildPrecipitationCornerTable`): lap-lokális (n+1)×(n+1)
   rács, sarkonként a sarkot osztó (legfeljebb 4) tile átlaga, a MÁR MEGLÉVŐ,
   lap-határra is helyes `PrecipAndOceanFractionAtCorner`-rel (az
   `TileNeighbors`-t használ, nem nyers index-aritmetikát — a kockaél menti
   varrat-hiba mért indoklását ld. ott). Level 5-ön **6534 bejegyzés**.
3. **`PrecipitationAtCore`**: `ToFaceUV` → a pont tile-on belüli (s,t) helye →
   bilineáris interpoláció a négy sarok között.
4. **A vágópontok UGYANEBBŐL a függvényből.** A `ComputeThresholdsForVegetatedLand`
   mintái a nyers `pkv.Value` helyett az INTERPOLÁLT értéket kapják a tile
   közepén — különben a 20/45/75 percentilis más eloszlásra vonatkozna, mint
   amit a biome-döntés lát. Ezért épül a sarok-tábla a vágópontok ELŐTT.
5. **A csapadék-overlay** (`PrecipitationColorAt`) is erre a függvényre vált —
   eddig duplikálta a nyers lekérdezést, így mást mutatott volna, mint amit a
   felszín színe követ.

**Amit ez NEM old meg:** a bilineáris interpoláció C0, nem C1 — elvileg
látszódhat a rács-átlós „ránc" a szintvonalon. Élő ellenőrzésen (315 km és
1079 km magasság, biome-átmeneti zóna) nem látszik; ha később mégis, a (C)
zaj-perturbáció a következő lépés.

**Verziózás:** nem seed-törő. A `MoisturePrecipitation` mezője, a
`BiomeClassification` és minden szimulációs érték változatlan; a `ToFaceUV`
pusztán kiemelés egy meglévő függvényből. A MEGJELENÍTETT biome-ok és a
belőlük számolt PANEL-statisztikák viszont megváltoznak (21,0% mintapont) —
ez szándékos, és a panel/overlay/felszín mostantól UGYANABBÓL a függvényből
dolgozik.

### ND-131 — A hidrológiai tile-középpont bázis is a lemez-gyorsítótárba: a formátum N tömbössé általánosítva (LEZÁRVA)

**2026-09-22.** A `todo2.md` A2 sora. Az ND-122 a STATIKUS SAROK-bázist vitte
validált lemez-gyorsítótárba; a hidrológia TILE-KÖZÉPPONT bázisa
(`EnsureTileCenterTerrainBasisCache`, ND-64) viszont **csak memóriában**
gyorsítótárazott (seed+szint kulccsal), ezért minden HIDEG Build
újraszámolta.

**MÉRT kiindulás** (élő Editor, seed `0xA7C944210000`, level 8, 393 216
tile-középpont): a `hydrology(...) terrainBasis=` sor **2 295 ms**, a teljes
hidrológia-fázis **2 625 ms**.

**A döntendő kérdés nem az volt, hogy kell-e lemez-cache** (az ND-122 ezt már
eldöntötte, az újraszámolásos validációval együtt), **hanem hogy a meglévő
formátum hogyan szolgálja ki a MÁSODIK fogyasztót**, aminek más az alakja:

| | statikus sarok (ND-63) | tile-középpont (ND-64) |
|---|---|---|
| tömbök száma | **3** (center + u/v eltolt a normálhoz) | **1** (nincs normál-számítás) |
| darabszám level 8-on | 396 294 ((n+1)² laponként) | 393 216 (n² laponként) |
| normál-epszilon | 1e-4 | **nem értelmezett** |
| nyers méret | 54,4 MiB | 18,0 MiB |

**Opciók.**

- **(A) A meglévő, háromtömbös formátum újrahasználata** úgy, hogy ugyanazt a
  tömböt adjuk át háromszor. Nulla formátum-munka, de **háromszoros
  lemezhasználat** (54,4 MiB a 18,0 helyett) és hazug adat a fájlban.
  Elvetve.
- **(B) Külön, párhuzamos formátum + külön betöltő/mentő út.** Duplikálná az
  ND-122 három biztosítékát (kulcs, ellenőrzőösszeg, újraszámolásos
  validáció) — épp azt a kódot, aminek a helyessége a legkritikusabb.
  Elvetve.
- **(C) A formátum általánosítása N tömbre. ← VÁLASZTOTT.** A fejléc kap egy
  `ArrayCount` és egy `Kind` mezőt, a `Payload` `TerrainPointBasis[][]`-t tart,
  a validációs visszahívás `Action<int, TerrainPointBasis[][]>` lesz. A három
  biztosíték változatlanul EGY helyen marad, mindkét fogyasztóra.

**Miért kell a `Kind` is, ha az `ArrayCount` és a darabszám úgyis különbözik?**
Mert az „úgyis különbözik" ESETLEGES: a darabszámok (n+1)² vs n² laponként, a
tömbszám 3 vs 1 — egyik sem a szándékot fejezi ki, és egy későbbi
változtatásnál (pl. ha a sarok-bázis egy tömbösre egyszerűsödne) csendben
egybeeshetnének. A `Kind` kimondja, hogy melyik fogyasztó adata; teszt is
rögzíti, hogy egy tile-középpont fájl sarok-kulccsal `kulcs-elteres`-t ad.

Az epszilon a tile-középpont ágon **0.0** — nem „hiányzó" érték, hanem az,
hogy a tartalom tényleg nem függ tőle (nincs véges-differencia). A `Key`
doksija ezt kimondja.

**Formátum-váltás és a régi fájlok.** A fejléc bővült, ezért a `Magic`
`WGTB0001` → `WGTB0002`, és a fájlnév is más (`basis_k<kind>_…_<count>x<arrays>.bin`).
A korábbi fájlokat így már a NEVÜK sem találja meg — soha többé nem
olvasnánk őket, viszont a kvótából helyet foglalnának. Ezért a betöltés ÉS a
mentés útja is meghív egy munkamenetenként egyszer futó takarítást
(`PurgeStaleTerrainBasisCacheFilesOnce`), ami a `basis_*.bin` fájlok közül
törli azokat, amik nem a mostani névelőtagot viselik.

*MÉRT csapda, amiért ez a betöltés útján is kell:* az első változat csak a
kvóta-kezelésből takarított, az viszont CSAK MENTÉSKOR fut — egy olyan Build,
ami minden bázist a gyorsítótárból kap, soha nem ír, tehát a takarítás sem
futott volna le. Élőben ellenőrizve: a 54,4 MiB-os árva fájl ott maradt, amíg
a hívás át nem került a betöltési ágra is.

**MÉRT eredmény** (ugyanaz a seed, hideg Build, lemez-találattal):

| | előtte | utána |
|---|---|---|
| `hydrology(...) terrainBasis=` | **2 295 ms** | **98–104 ms** (~22×) |
| teljes hidrológia-fázis | 2 625 ms | 417–472 ms |
| fájlméret | — | 18,0 MiB |
| kiírás | — | 55 ms |
| beolvasás + validáció | — | 94–96 ms |
| gyorsítótár-könyvtár / világ | 54,4 MiB | 72,4 MiB |

**Helyesség-ellenőrzés a formátumon túl:** a `lakes=11505` érték BITRE azonos
a számoló és a lemezről töltő ágon. A tó-halmaz a teljes tile-középpont
bázisból származik (eleváció → priority flood → tó-detektálás), tehát ez egy
végponttól végpontig tartó egyezés-jelzés, nem csak a fájlformátumé.

**Tesztek:** `TerrainBasisDiskCacheTests` 7 → **15 teszt**. Újak: egy tömbös
oda-vissza, egy tömbös algoritmus-eltérés elutasítása, a két gyorsítótár
összekeverhetetlensége (`Kind`/`ArrayCount` kulcs-eltérés + eltérő fájlnév),
az elavult formátumú nevek felismerése, és a méret-terv mindkét alakra.
490/490 zöld a `WorldGen.Viewer.LodChunking.Tests`-ben.

**Verziózás:** nem seed-törő — a gyorsítótár származtatott adat, a világmodell
nem függ tőle. A formátum-váltás ára egyetlen lassabb hideg Build világonként
(a régi fájl nem használható), utána a takarítás visszaadja a helyet.

### ND-132 — Deep-time újraépítés: megszakítható, elkülönített folyómunka (A4 LEZÁRVA)

**Döntés (2026-09-22):** a deep-time `Build()` nem futtat szinkron folyó-
nyomkövetést. A t=0 csapadék-/óceánmezőből származó, vízgyűjtőnkénti
forráslista gyorsítótárazható a csapadékmező példányához kötve. A meglévő
csapadék-cache teljes kulcsa a seed, lemezszám, szint, vízarány, referencia-
nap, keringési és forgási periódus, valamint tengelyferdeség. A deep-time-függő,
elmozdult lemez-seedeket
használó folytonos nyomkövetés háttérfeladatban fut. Új világépítés az előző
feladatot `CancellationTokenSource`-szal megszakítja; a token a párhuzamos
folyóhálózatból egészen az egyes nyomvonalak és a lokális spillway-keresés
belső ciklusáig eljut.

**Feltárt problémák.** Az ND-124 óta minden `Build()` előbb szinkron felépítette a 96
forrás durva hálózatát (mérve kb. 1,6 s), majd elindított egy új, CPU-igényes
96-folyós finomítást. A generációszámláló csak az elavult eredmény
alkalmazását tiltotta meg: magát a régi munkát nem állította le. Ez lehetővé
tette az egymásra halmozódást, de ennek kijavítása NEM oldotta meg a mért
lassulást. Az első változat után a `PerfLog_20260922_160327.txt` hidegen
24 932,7 ms, melegen 27 001,6 ms teljes Buildet mutat; a meleg
klasszifikáció önmagában 20 896,3 ms. A korábbi gyökérok-állítás túl erős volt.

**Második változat: ütemezés.** Az első javítás közvetlenül a statikus terep
előtt új, korlátlan `Parallel.For` folyómunkát indított a közös ThreadPoolban.
Ez továbbra is versenyez a terep párhuzamos meneteivel; az egymást követő
menetek nagy szórása összhangban van a szálkészlet kiéheztetésével, de a
PerfLog önmagában nem bizonyítja annak kizárólagosságát. Most a megszakítás
a Build belépésére kerül, az új munka csak a teljes szinkron Build után
indul. A viewer a szekvenciális, azonos forrássorrendű nyomkövetőt külön
`LongRunning` feladatban futtatja, így nem foglalja el a terep ThreadPoolját.
A Core párhuzamos API-ja továbbra is elérhető offline számításra. A token
a szekvenciális útból is végigjut a nyomkövetőig. A folyók elkészülési
idejét külön kell mérni: a rövidebb Build nem jelenti a teljes folyóhálózat
elkészülését. Az <1 s cél továbbra is nyitott.

**Offline reprodukció Unity Mono alatt (2026-09-22).** A valódi Core-kóddal,
96 folyóforrással és 393 216 cache-elt terrain-bázis kiértékelésével,
hőmérséklet- és zajszámítással végzett próba eredménye: folyók nélkül három
előtérmenet 1555,4 / 1540,8 / 1572,4 ms; saját szálon futó szekvenciális
folyómunka mellett 1615,1 / 1653,9 / 1666,9 ms, bitazonos ellenőrzőösszeggel.
A régi `Task.Run` + korlátlan `Parallel.For` változatban az első menet még
1844,8 ms, a második viszont több mint 60 s alatt sem fejeződött be;
a próbát megszakítottuk. Egy ismétlésben a ThreadPool-alapú 30 s-os
`CancelAfter` időzítő sem szabadította fel az előtérmenetet 60 s-on belül.
Ez reprodukálja a közös szálkészlet telítődését. Nem teljes Unity Build-,
mesh-, GPU- vagy vizuális mérés, ezért ebből <1 s teljes idő nem állítható.

A külön szálas, 30 s-os watchdoggal ismételt régi ütemezés számai:
2136,1 / **28 345,3** / 1511,9 ms. A második menetet a folyófeladat
megszakítása szabadította fel, majd a harmadik visszaállt a kontroll
idejére. Mindhárom ellenőrzőösszeg azonos. A reprodukció a
`tools/diagnostics/probe-river-scheduling.ps1` paranccsal, `baseline`,
`old` és `dedicated` módban megismételhető; nem változtatja az Editor állapotát.

**Miért nem cache-eljük a teljes folyóhálózatot deep-time között?** A
forráslista valóban t=0 adatból jön, de a nyomvonal-követő a megjelenített
deep-time állapot `PlateMotion.MovedSeeds(...)` eredményét kapja. A teljes
útvonal újrahasználata ezért vizuálisan és modell-szinten hibás lenne. Csak a
forráslista stabil; a nyomvonalat újra kell számolni, de nem a fő szálon.

**Átmeneti megjelenítés.** Amíg az új nyomvonal készül, az új világállapothoz
nem rajzolunk régi, térben már érvénytelen folyóvonalat. Elkészüléskor a
finomított hálózat és a vízhozam-súlyok együtt, fő szálon kerülnek átadásra.
Ez tudatos csere: a számítás idejére nincs folyóvonal, és nincs régi/új
állapot keverése. A vezérlés tényleges gyorsulását új élő mérésnek kell
igazolnia; a szekvenciális háttérmunka teljes futásideje még nincs megmérve.

**Verziózás:** nem seed-törő. A Core numerikus eredménye változatlan; csak a
munka ütemezése, megszakíthatósága és a viewer átadási ideje változik.

**Utólagos állapot (2026-09-22):** a `PerfLog_20260922_163455.txt` egy hideg
3256,5 ms-os és 12 meleg, 2250,6–2634,6 ms-os Buildet tartalmaz. A meleg
átlag 2479,4 ms, a klasszifikáció 460,2–556,3 ms; a 27 s-os regresszió nem
ismétlődött. A4 a felhasználó kérésére lezárva, az elért eredmény elfogadva;
a <1 s numerikus küszöb ettől még nem igazolt. A további profilozás A5/ND-133.

### ND-133 — Deep-time variancia és allokációprofil (LEZÁRVA)

**Döntés (2026-09-22, implementáció előtt):** az A5 első lépése a jelenlegi
PerfLog megismételhető elemzése és opcionális, fázisonkénti viewer-mérés.
A `profileDeepTimeAllocations` alapból hamis. A statikus alapréteg minden
fázisa kap Unity Profiler-mintát; a terrain mesh összeállítása, feltöltése
és indexmaszkja külön mérhető. A naplózás a mért szakaszok után történik.

Mennyiségek: főszálon `GC.GetAllocatedBytesForCurrentThread()` különbsége;
folyamatszinten `GC.CollectionCount(0/1/2)` különbsége és
`GC.GetTotalMemory(false)` előtte/utána. A heap nettó változását tilos
allokációként értelmezni. A worker-allokáció, a natív/GPU memória és a GC
szünetideje külön Unity Profiler-vizsgálatot igényel. A számlálók egymással
korreláló megfigyelések, önmagukban nem bizonyítanak GC-okozatot.
Nincs kényszerített GC vagy új szimulációs algoritmus/verzióváltás.

A logelemző külön kezeli a hideg/meleg cache-állapotot, és kizárja az
önálló overlay-futtatásokat és a befejezetlen Buildet a teljes Build
statisztikájából. Különböző világidők szórása nem tiszta futásidejű zaj:
az érdemi munka is változik. A lezáráshoz rögzített konfigurációjú,
ismételt, profilozás nélküli kontroll és Profiler-menet szükséges.

**Élő számlálóellenőrzés:** ezen a Unity Mono futtatón a
`GC.GetAllocatedBytesForCurrentThread()` az ismert allokációnál sem lép.
A műszerezés ezt a fázisok előtt ellenőrzi; nem támogatott számlálónál
`allocationCounterSupported=False`, bájtérték `-1`, a JSON-ban `null`.
Ez nem allokációmentesség. Részletes mérés: `history/2026-09-22-deep-time-allocation-profile.md`.

**A5 második folytatás, mérési terv:** a vízfázis külön setup/összefűzés/
upload/diagnosztikai másolat/layout/maszk/LOD-forrás mintákra bomlik, az
összesített PerfLog-vízidő megmarad. Először azonos konfigurációjú élő
profil készül; a mérés nem változtat GC-módot vagy szimulációs viselkedést.

**Lezárási kapu pontosítása (2026-09-22):** az A5 véges profilozási feladat:
megismételhető fázis-/allokációmérés, igazolt pazarlás javítása, Editor és
Player elkülönítése, majd változó világidős ismétlési ellenőrzés. A záró
menet a Build-időt, a memóriaállományt és külön a visszatérő időpontok
terrain/víz/tó mesh-hashét vizsgálja. A rövid sorozat nem hosszú távú
szivárgásbizonyítás és nem vizuális átvétel. Az Editor belső allokátorának
teljes feltárása és minden natív allokáció eseményszintű követése korábban
túl tágra nyitotta a feladatot: ezek opcionális további vizsgálatok, nem
A5-zárókövetelmények. A <1 s cél elérését ez a profilozási lezárás nem jelenti.

**Záró eredmény (2026-09-22):** 2 bemelegítés után 20 kontrollált Build,
0/100/500/100/0 Myr négyszer: 1,873–2,374 s, azonos időpontok között a
Unity-állomány min–max eltérése legfeljebb 115 626 bájt. Külön hat Buildben
a három időpont ismételt terrain/víz/tó-hash-e páronként bitazonos;
a különböző időpontok eltérő terepet adnak. Console: 0 hiba. A rövid
ismétlésben nincs jelentős felhalmozódás; hosszú szivárgásmentesség és
vizuális átvétel nincs állítva. A pontosított profilozási feladat lezárva.
Részletes bizonyíték: `history/2026-09-22-a5-water-cpu-player-profile.md`.

### ND-134 — Mesh-összefűzés pontos előfoglalással (A5)

**Döntés (2026-09-22, implementáció előtt):** a terrain és a statikus víz
összefűző listái a nem üres bucketek tényleges elemszámából számolt
kapacitással indulnak. A bucketek rendezése, bejárása, elemei, indexeltolása
és bounds-számítása változatlan. Nincs megosztott buffer, új tulajdonjogi
szerződés, Core- vagy seed-változás.

**Bizonyíték:** három valódi Unity Profiler-mintában a főszálú managed
allokáció a terrain mesh-összeállításban 204 070 380 bájt/Build, a
statikus vízfázisban 132 805 219 bájt/Build. A külön `GC.Alloc` hívásláncos
menet a `ConcatenateMultiMaterialBuckets`-hez, illetve `BuildWaterSurface`-
hez köti a 3,8 → 7,7 → 15,3 → 30,6 MB és további növekvő tömböket.
Az input bucketek darabszáma már ismert: a kapacitásnövelés elkerülhető.

**Kapuk:** azonos konfiguráción ismételt kontroll és profilos előtte/utána
mérés; a statikus terep/víz/tó csúcs-, normál-, szín-, submesh-index- és
bounds-adatainak azonos SHA-256 értéke; élő Unity-fordítás/Console és viewer
ellenőrzés. A bitazonosság fontosabb, mint a mért idő. A maradék bucket- és
maszk-varianciát nem nyilvánítjuk ezzel automatikusan lezártnak.

**Eredmény (2026-09-22):** implementálva, élő Editorban mérve. A terrain
összeállítás 204 070 380 → 75 500 252, a vízfázis 132 805 219 → 84 171 555
főszálú managed bájt/Build (3–3 Profiler-minta). A teljes Build allokációja
~692 → ~515 MB. Két bemelegítés után 10–10 profilozás nélküli kontroll
átlaga 2375,08 → 2150,30 ms, szórása 119,09 → 73,68 ms. A terep, víz és
tó geometriájának SHA-256 értéke azonos. A vízfázis kontrollszórása ugyanakkor
14,75 → 151,84 ms: az allokációcsökkenés nem oldotta meg minden fázis
varianciáját. Ebben a mérési körben A5 még nyitott volt; a későbbi lezárás
az ND-133-ban, a részletes korlátok és reprodukció a mérési naplóban.

### ND-135 — A5 külön Development Player-kontroll

**Döntés (2026-09-22, implementáció előtt):** az Editor-függő variancia
leválasztására ugyanaz a PlanetView scene külön Windows Development
Playerként épül az `artifacts/` alá. A mérőkomponens csak Development
Playerben és explicit `-a5-output` indítási argumentummal aktiválódik;
nem kerül scene-be és nem módosít projektbeállítást. Azonos világ/kamera,
bemelegítés és teljes Build-minták, külön fázismérős menet.

A főszál CPU-ideje Windows Editorban/Playerben `GetThreadTimes`-ból,
kernel+user időként mérhető; más platformon -1, a JSON-ban null. Az OS
számláló durva felbontása miatt rövid szakaszoknál nulla vagy a falióránál
kicsivel nagyobb idő is lehet. A mért ~90 ms CPU-csúcsok viszont nem
magyarázhatók kizárólag deschedulinggel.

Külön, legfeljebb három Buildes **diagnosztikai** Player-próbában a GC
csak az egyes Build hívás idejére kikapcsolható, `finally` visszaállítással
és 4 GiB managed-heap indítási korláttal. Nincs explicit `GC.Collect`,
nincs termékbeli GC-szabályváltás. A Unity API Editorban nem támogatott,
ezért ott nem színlelünk ilyen összehasonlítást. A próba befejezésekor a
Player kilép; kimeneti fájlt nem ír felül.

Források: [Unity GCMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Scripting.GarbageCollector.GCMode.html),
[Microsoft GetThreadTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadtimes).

**Mérve:** a Development Player 10 kontrolljának átlaga 2037,56 ms,
szórása 32,76 ms, vízfázisa 62,1–71,0 ms. Az Editor CPU-csúcsai ebben a
Player-sorozatban nem ismétlődtek. Normál / GC-disabled / ismételt normál,
3–3 fázismérős Build átlaga 2011,77 / 1963,00 / 2010,07 ms; GC-disabled
alatt mindhárom gyűjtésszámláló-delta 0, a Build utáni managed heap
0,933–1,137 GB. Ez rövid diagnózis, nem GC-kikapcsolási termékjavaslat.
A tíz kontroll utáni Unity-nyilvántartott memória 328,272–328,300 MB;
állomány, nem natív allokációs forgalom. Részletek és korlátok:
`history/2026-09-22-a5-water-cpu-player-profile.md`.

### ND-136 — A domborzati zaj nem utazik a lemezzel (A19, LEZÁRVA: (C))

**2026-09-22.** Felhasználói elvárás: „amennyiben a lemezek mozognak, akkor
elvárnám, hogy mozogjanak velük a domborzati elemek, másképpen az elsődleges
és másodlagos zajok. Azaz ha a lemez felfelé megy, akkor a zaj is menjen
felfelé egyenletesen és arányosan vele."

#### A tényállás — kódból ellenőrizve

Deep-time-ban **csak a lemez-magok forognak el** (`PlateMotion.MovedSeeds`).
A domborzat-textúra három tagja — elsődleges ridged zaj, `MountainMask`,
másodlagos részletzaj — a **rögzített világ-pozícióból** számolódik:

```
CrustElevation.ComputeNoiseBasis(worldSeed, x, y, z, ...)
   <- DeepTimeErosionGlaciation.ElevationAtTime         (DeepTimeErosionGlaciation.cs:55)
   <- PlateBoundaryEffect.ElevationWithBoundaryFromWarped (PlateBoundaryEffect.cs:201)
   <- TerrainPointBasis.Compute                          (CrustElevation.cs)
```

Ez az ND-63 óta **kimondott szándék** volt („Deep-time során a mozgó lemez és
így a kéregtípus változhat, ezek a részeredmények viszont kizárólag a world
seedtől és a pozíciótól függenek"), és pontosan ez tette lehetővé az ND-122 /
ND-131 lemez-gyorsítótárat: a `TerrainPointBasis` struct doksija szó szerint
azt mondja, hogy „nem tartalmaz … eróziót vagy más időfüggő állapotot".

**A következmény fizikailag rossz.** A kéreg anyag; a domborzat a kéreg
tulajdonsága, tehát együtt kell mozognia vele. Ma viszont a lemezhatár
átcsúszik egy álló textúra fölött: a hegyvonulat helyben marad, a lemez
elvándorol alóla. Ugyanaz a hibaosztály, mint az ND-119 (a szél-overlay nem a
szimuláció szelét mutatta) és az ND-125 1. része (az overlay nem a világmodell
lemezfelosztását rajzolta) — csak itt nem a megjelenítés tér el a modelltől,
hanem a modell önmagával nem konzisztens.

#### Amit a javítás megkövetel

A mintavételi pontot a **lemez saját vonatkoztatási rendszerébe** kell
visszavinni, mielőtt a zaj kiértékelődik: a `p` pontot a lemez Euler-pólusa
körül `R(-omega*t)`-vel visszaforgatni, és a zajt ott mintavételezni. Ez
egyetlen további Rodrigues-forgatás pontonként, a már meglévő, bitpontos
`DeterministicMath.SinCos`-szal (ND-27) — **nincs új ND-23-kockázat**.

Három dolog viszont nem magától értetődő:

**(1) A határ menti folytonosság ma ingyen van, utána nem lesz az.** A
`BlendedBaseElevationFromNoiseBasis` (ND-90) csak akkor kever, ha a két
legközelebbi lemez **kéregtípusa eltér**; azonos típusnál egyszerűen a
nyertes lemez értékét adja vissza. Ez ma helyes, mert a zaj-bázis mindkét
lemezre **ugyanaz a három szám** — azonos típusnál tehát a függvény
matematikailag folytonos. Lemez-keretes zajjal viszont a határ két oldalán
**más zajérték** áll, így **minden** határ szakadásossá válik, nem csak a
kéregtípus-váltó. A keverést ki kell terjeszteni az összes határra, és
pontonként **két** zaj-bázist kell számolni (a nyertes és a második lemez
keretében).

**(2) Ez a legdrágább tag a legforróbb úton.** A `ComputeNoiseBasis` három
fBm-jellegű kiértékelés (ridged multifractal + `MountainMask` + másodlagos
részlet). Az ND-131 mérése szerint a tile-középpont-bázis számolása level 8-on
**2 295 ms** volt, mielőtt lemezre került. A kétszeres kiértékelés ennek a
nagyságrendnek a duplázását jelenti — **és a gyorsítótár nem menti meg**: a
`TerrainPointBasis` kulcsa ma pozíció-alapú és időfüggetlen, lemez-keretes
zajjal viszont a bázis `t`-függővé válik. (Ez nem új mérés, hanem az ND-131
mért értékéből vett nagyságrend-becslés.)

**(3) `t = 0`-nál a kimenet bitre változatlan.** A Rodrigues-forgatás
`angle = 0`-nál egzakt identitás (`cos 0 = 1`, `sin 0 = 0` IEEE-754 pontosan,
ugyanaz az érv, amit a `ComputeElevationFieldAtTime` már használ). A statikus
világok tehát **nem törnek**; csak a `t > 0` deep-time kimenet módosul.

#### Opciók

- **(A) Maradjon (az ND-63 állapota).** Olcsó, cache-barát, `t`-független
  bázis. A felhasználó ezt explicit elutasította, és fizikailag is rossz.
- **(B) Lemez-keretes zaj, kemény hozzárendeléssel.** Minden pont a nyertes
  lemez keretében mintavételez. Egyetlen extra forgatás, nincs
  költség-duplázás — de **minden lemezhatáron szakadás** lesz (lásd (1)),
  beleértve azokat is, amelyek ma tökéletesen simák. Az ND-90 folytonos parti
  átmenete ellen dolgozna.
- **(C) Lemez-keretes zaj, kétlemezes keveréssel.** Pontonként két zaj-bázis
  (a nyertes és a második lemez keretében), a meglévő smoothstep-súllyal
  keverve — a keverési feltételből viszont kikerül a „csak eltérő
  kéregtípusnál" kikötés. Kétszeres zajköltség és `t`-függő cache.
- **(D) A kéreg tulajdonságainak tile-onkénti követése.** Fizikailag ez a
  helyes út (a zaj nem „újramintavételeződik", hanem a kéreggel utazik) — de
  ez már az **ND-139** reprezentáció-váltása, és felveti az ND-04-et.

**Javaslat: (C).** A (B) egy ma meglévő, jó tulajdonságot (sima határ azonos
kéregtípusnál) rombolna le; a (D) nagyságrenddel nagyobb munka. A (C) ára a
zaj-költség duplázása és a deep-time cache újragondolása — utóbbira a
kézenfekvő MVP, hogy a lemez-gyorsítótár **csak `t = 0`-ra** marad érvényes
(ott bitre azonos, lásd (3)), a `t > 0` pedig számol. Ezzel a statikus Build
sebessége nem romlik, és a lassulás oda kerül, ahol az új viselkedés
jelentkezik.

**Nyitott alkérdés a (C)-n belül:** nagy `t`-nél (500–1000 Myr) a két
szomszédos keret akár több tíz fokkal is elfordul egymáshoz képest, tehát a
határ két oldalán a zaj **teljesen korrelálatlan**. Egy keskeny keverősáv
ilyenkor nem simítás, hanem egy látható nyírási sáv lesz. Ez fizikailag nem
rossz (egy transzform határ valóban egymás mellé tol nem összetartozó
kérget), de a sáv szélességét kalibrálni kell — és ez az ND-138-cal együtt
értelmes, mert ott dől el, melyik határ transzform.

**Verziózás:** `t > 0` seed-törő → `WorldGeneratorVersion.Current` emelése
(ND-108). `t = 0` bitre változatlan. Az ND-137-tel **egy** verzióemelésbe
érdemes összefogni.

**Munkarend (CLAUDE.md):** előbb Python orákulum
(`tools/reference/plate_frame_noise_ref.py`) + tesztvektorok, aztán C#.

#### LEZÁRVA (2026-09-26): a **(C)** opció implementálva

A zaj a lemez saját vonatkoztatási rendszerében értékelődik ki:
`PlateMotion.ToPlateFrame` a pontot `R(-omega*t)`-vel visszaforgatja a lemez
Euler-pólusa körül, és a három zajtag
(`CrustElevation.ComputeNoiseBasisInPlateFrame`) ott mintavételeződik. A
határon a keverés kiterjed **minden** lemezpárra
(`BlendedBaseElevationFromPlateFrameBases`) — a régi „csak eltérő
kéregtípusnál" kikötés kikerült, mert lemez-keretes zajjal az azonos típusú
határ is szakadásossá válna. Az ND-35 uplift-maszk szintén a nyertes lemez
keretéből jön (`BoundaryUpliftFromWarpedAtTime`), tehát a hegyvonulat a
kéreggel együtt vándorol.

**A (2) pont költség-becslése FELÜLBÍRÁLVA — nem duplázódik a zaj.** A
tervezet azt feltételezte, hogy pontonként *mindig* két zaj-bázis kell. Nem
kell: a második bázisra csak akkor van szükség, ha a pont a keverősávon belül
van (`gap < BoundaryBlendGap = 0.005`), azon kívül a függvény amúgy is a
nyertes lemez értékét adja vissza. Az implementáció ezért csak ott számolja ki
(`PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime`), tehát a költség
**~1× mindenhol + egy keskeny sávnyi extra**, nem 2×.

**A cache-stratégia is olcsóbb lett a tervezettnél.** A `TerrainPointBasis`
két része szétvált: a WARP (a három fBm-es domain warp, a drága tag) tisztán
pozíció-függő, tehát a lemez-gyorsítótár (ND-122/ND-131) `t > 0`-nál is
érvényes marad rá; csak a három ZAJTAG számolódik újra
(`TerrainPointBasis.EvaluateAtTime`). Nem kellett a cache-t `t = 0`-ra
korlátozni, ahogy a javaslat MVP-je feltételezte.

**Új, a tervezetben nem szereplő kiterjesztés: a hidrológia is követi.** A
`RiverPathTracing` folytonos nyomvonalkövetése a saját, pontszerű
elevációkiértékelését használja; ez `timeMyr`-t kapott, különben `t > 0`-nál a
folyó egy másik (világ-keretes) terepen futna, mint amit a felhasználó lát
(ugyanaz a hibaosztály, amit ez az ND javít).

**Verziózás:** `WorldGeneratorVersion.Current` 2 → 3 (ND-108). `t = 0` bitre
változatlan — ezt a `PlateFrameNoiseVectorFileTests` `zeroTimeVectors` blokkja
és a `ZeroTimeIsBitIdenticalToTheStaticPath` teszt (ami a RÉGI keverési
szabályt külön újraimplementálva hasonlítja össze) őrzi.

**Nyitva marad** a javaslat alkérdése: nagy `t`-nél a szomszédos keretek
elfordulnak egymáshoz képest, tehát a keverősáv egy látható nyírási sáv lesz. A
sáv szélességének kalibrálása az **ND-138**-cal együtt értelmes, mert ott dől
el, melyik határ transzform.

**Referencia és tesztek:** `tools/reference/plate_frame_noise_ref.py`
(+ `plate_frame_noise_vectors.json`, 400 + 100 vektor),
`tests/WorldGen.Core.Tests/Tectonics/PlateFrameNoiseTests.cs`. Az
`erosion_glaciation_deep_time_ref.py` és a vektorai újragenerálva (mostantól a
ténylegesen elmozdított magokkal mérnek, nem a `t = 0` magokkal vett hibriddel).


### ND-137 — Deep-time erózió: hullámhossz-szelektív, zárt alakú relief-kopás + bevágódás + parti abrázió (A20, LEZÁRVA — (B), 2 körben)

**2026-09-22.** Felhasználói észrevétel: „az erózió egy olyan dolog, amit
deep time-ban hiányolok." **Implementálva 2026-09-26, a (B) opció szerint.**

#### A tényállás — kódból ellenőrizve (a döntés előtt)

A `DeepTimeErosionGlaciation` neve többet ígért, mint amit tett. A teljes
„erózió" egyetlen zárt alakú exponenciális relaxáció volt:

```
UpliftRelaxationElevation(upliftStatic, t) = H_eq + (H0 - H_eq) * exp(-t/tau)
H0 = upliftStatic,  H_eq = 0.35 * upliftStatic,  tau = 50 Myr
```

Ez **kizárólag a lemezhatár-uplift bónuszra** hatott (`ElevationAtTime`:
`baseElev + upliftT`). Az alap-eleváció — a kéreg-bázis plusz az elsődleges és
másodlagos zaj — **időben teljesen változatlan** volt. Vagyis a deep-time
csúszka a hegycsúcsokat lelapította 35%-ra, és a domborzattal ezen kívül semmit
nem csinált.

#### Két korlát, ami a megoldást alakította

**(1) ND-04, timestep-invariancia.** A relaxáció azért zárt alakú, hogy a
„`t` közvetlen lekérdezése" és a „lépésenként szimulálva" **definíció szerint
ugyanaz** legyen. Egy klasszikus, állapotot akkumuláló eróziós integrátor ezt
megtörné: a csúszkával 0 → 100 → 200 Myr más világot adna, mint a közvetlen
200 Myr, és a felhasználó pont ezt a két utat használja felváltva.

**(2) A modul nem volt bit-determinisztikus.** Nyers `Math.Exp`/`Math.Sin` —
a CLAUDE.md táblázata szerint platformok között nem garantált.

#### A megvalósított modell

Az alap-eleváció három tagból áll (`CrustElevation`):

```
h = base_c  +  primary * mask * A_p  +  secondary * A_s
```

`base_c` a **kéreg-típus bázisszintje** (a lemez vastagságából/úszásából adódó
szint, nem felszíni relief — ezért nem is erodálódik); a másik két tag a valódi
felszíni relief. Az erózió ezek amplitúdóját csillapítja, **külön**
időállandóval:

```
h(t) = base_c + T_p(t) * A_p + T_s(t) * A_s
T_i(t) = m_i + (term_i - m_i) * D_i(t)
D_i(t) = eq_i + (1 - eq_i) * exp(-t_eff / tau_i)
t_eff  = (W(|lat|) + G * f_ice(|lat|)) * t
```

**A hullámhossz-szelektivitás a modell lényege.** A rövid hullámhosszú relief
(éles gerincek, völgyek) több nagyságrenddel gyorsabban kopik, mint a
regionális léptékű domborzati hullámzás — ezért néz ki egy 1 milliárd éves
pajzs simának, de nem teljesen laposnak. A lineáris lejtő-diffúzió
(`dh/dt = kappa * lap(h)`) egy `k` hullámszámú komponensre pontosan
`exp(-kappa*k^2*t)`-t ad; a lineáris stream-power (`n=1`) `~exp(-t/tau)`-t,
`tau ~ L`-lel. A két határ között választottunk: **tau arányos a
hullámhosszal**, tehát

```
tau_secondary / tau_primary = f_primary / f_secondary = 8.0 / 0.50929... = 5*pi
```

ami a **már meglévő zaj-frekvenciákból származtatott** szám, nem egy újabb
szabadon választott konstans.

**Az átlag felé relaxálunk, nem nulla felé.** Egy fontos, mérésen alapuló
korrekció a döntés eredeti tervezetéhez képest: a két relief-tag **nem
nulla-átlagú** (`primary*mask` ≈ 0,165, `secondary` ≈ 0,42 — hat seeden mérve,
`CrustElevation.PrimaryReliefSphericalMean` / `SecondaryReliefSphericalMean`).
Nullához relaxálva a kontinensek átlagosan **~490 m-t süllyednének**, ami
fiktív tömegveszteség: nincs izosztatikus kiegyenlítési modellünk, ami ezt (a
valóságban ~80%-ban) visszaemelné — az ND-38 térfogat-megmaradó tengerszint
pedig ezt a világ elárasztásaként fordítaná le. Az **átlaghoz** relaxálás ezt a
kompenzációt építi be: a relief simul (peneplanáció), a kéreg átlagos magassága
megmarad. Mérve: 3000 Myr-nél a globális átlag eltolódása −13,8 m (a maradék a
zaj-átlag seed-függő szórása, nem rendszeres lehúzás).

**Ez adja a lerakódást is.** A csillapítás nem „lehúzás": ahol a relief-tag az
átlaga **alatt** van (medence, völgytalp, intramontán árok), ott a csillapítás
**felemeli** a felszínt — a medence feltöltődik, miközben a csúcs lekopik.
Mérve 500 Myr-nél: 161 minta emelkedett (max +321 m), 127 süllyedt
(max −874 m), a relief szórása 449 → 375 m (−16,5%).

**A víz és a jég vezérli, zonálisan.** A `W(|lat|)` egy három-cellás csapadék-
proxy (nedves ITCZ `cos^8` alakban, szubtrópusi sivatagöv, mérsékelt övi
viharpálya-Gauss, száraz pólus; 0,15..1,15). Ugyanaz a modellezési szint, mint
az `IceLineAbsLatitude` idealizált, szélesség-alapú hőmérséklet-profilja —
**szándékosan nem** a rács-alapú `MoisturePrecipitation`, mert az iteratív,
rácsra kötött és nem bit-egzakt, tehát pontonként (mesh-sarkonként) nem
kiértékelhető. A `f_ice(|lat|)` a **jeges időhányad zárt alakja**: a
`sin(theta) <= u` feltétel időaránya egy teljes perióduson `0,5 + asin(u)/pi`;
ezzel a `GlaciationPeriodMyr`/`AmplitudeK` ciklus **végre koptatja is** a
felszínt, nem csak a jégvonalat mozgatja.

#### Kalibrált konstansok (ILLUSZTRATÍV, vizuális megerősítést igényelnek)

| Konstans | Érték | Megjegyzés |
|---|---|---|
| `PrimaryReliefTauMyr` | 250 Myr | rövid hullámhosszú relief |
| `PrimaryReliefEqFraction` | 0,30 | telítésben megmaradó hányad |
| `SecondaryReliefTauMyr` | 250 · 5π ≈ 3927 Myr | **származtatott**, nem szabad |
| `SecondaryReliefEqFraction` | 0,60 | |
| `GlacialErosivity` | 3,0 | a jég hatékonyabb eroder |
| `ErosionWaterFloor` / `EquatorWeight` / `MidLatWeight` | 0,15 / 1,0 / 0,55 | zonális profil |
| `ErosionMidLatCenterRad` / `WidthRad` | 50° / 12° | viharpálya |

#### Mennyire „érezhető" (a todo2 A20 tényleges elvárása)

A kontinentális tile-ok átlagos elmozdulása az erózió nélküli állapothoz képest
(lemez-idő fixen 0, hogy tisztán az erózió látszódjon; max zárójelben):

| t (Myr) | 100 | 250 | 500 | 1000 | 2000 |
|---|---|---|---|---|---|
| kontinentális átlag | 117 m | 172 m | 200 m | 215 m | 222 m |
| max | 676 m | 888 m | 926 m | 938 m | 949 m |
| óceáni átlag | 32 m | 45 m | 52 m | 55 m | 57 m |

#### Amit a döntés MEGTART az eredeti opciólistából

- **(A) Maradjon** — elvetve, a felhasználó explicit hiányolta.
- **(B) Zárt alakú, hidrológia-vezérelt kopás** — **EZ VALÓSULT MEG**, azzal a
  pontosítással, hogy a „hidrológia" itt pontonként kiértékelhető zonális
  csapadék- és jég-proxy, nem a rács-alapú vízhozam-mező (ld. lent, mi maradt
  nyitva).
- **(C) Rögzített lépésszámú, `t`-arányos integrátor** — NYITVA MARAD,
  külön döntéssel.
- **(D) Akkumulált állapotú, iteratív erózió** — elvetve (sérti az ND-04-et).

#### Ami az 1. kör után nyitva maradt

- **Vízgyűjtő-terület-súlyozott bevágódás.** A valódi stream-power a
  vízhozammal (`A^m`) skálázódik, az pedig **globális, rács-alapú** mennyiség
  (ND-124 `FlowAccumulation`). Pontonkénti, zárt alakú függvényként nem
  előállítható, és a pipeline-ban körkörös lenne (a folyóhálózat a tengerszint
  után számolódik, a tengerszint pedig az elevációból). Ez az eredeti (C) opció.
  → a 2. körben MÁS ÚTON megoldva: a lefolyás-hálózatot a procedurális világban
  maga a zaj határozza meg, tehát a `primary` zajérték nem proxyja, hanem OKA a
  vízgyűjtőnek (lásd lentebb).
- **Hálózat menti hordalékszállítás, delta-építés, medencék közti tömegátvitel.**
  A mostani modell a relief-mezőn **belül** rendez át tömeget (csúcs le, medence
  fel), de nem mozgat anyagot lefelé a folyóhálózaton.
- **Parti abrázió.** → a 2. körben ELKÉSZÜLT (lásd lentebb).
- **A lemez vándorlási történetének integrálása.** Az eróziós hatékonyság a pont
  MAI szélességéből jön; egy pólustól az egyenlítőig vándorolt kéreg valójában
  vegyes klímatörténetet élt át. Zárt alakban ez az Euler-pólus körüli pálya
  menti integrál lenne — megoldható, de külön munka.
- **A `GlaciationPeriodMyr = 150` és `AmplitudeK = 6`** továbbra is illusztratív
  (ND-44 nyitott pontja, todo2 B14). A glaciális eróziós tag most már ezekre
  épül, tehát megerősítésük **fontosabb lett**.
- **A `glaciated_fraction` részperiódus-közelítése.** A zárt alak a
  **szekuláris** (egy teljes ciklusra átlagolt) jeges időhányad; egész sok
  periódusra egzakt, részperiódusra közelítés. Cserébe lineáris `t`-ben, tehát
  az ND-04 láncolhatóság egzaktul teljesül, és az erózió monoton nő (nem
  „visszakopik" egy interglaciálisban). Numerikus időintegrállal ellenőrizve:
  max eltérés 1,7e-5, a mintavételezés felbontásán.

#### 2. KÖR (2026-09-26, „fejezd be ND-137-t") — folyóvízi bevágódás + parti abrázió

Az 1. kör a nyitott tételek közül a hidrológia-vezérelt **csillapítást** hozta
meg. A 2. kör kettőt lezár közülük, egyet pedig más úton, de érdemben: a
**vízhajtotta bevágódást**. Ami továbbra is nyitva marad, az lentebb, saját
indoklással.

**A hiányzó fél. Az 1. kör modellje tisztán SIMÍT** (lejtő-diffúzió: csúcs le,
medence fel). Ez az erózió fele. A másik fele a folyóvízi **bevágódás**, ami
ellenkező előjelű: a völgy mélyebbre vágódik, a gerinc a helyén marad, tehát a
relief **NŐ**. Enélkül a deep-time csúszka soha nem tud mást, mint lapítani —
egy 500 Myr-os világ csak fakóbb változata a 0 Myr-osnak, holott a valódi
ciklusban a fiatal orogén **először felszabdalódik**, és csak azután kopik le.

##### Hol van a víz — és miért NEM kell hozzá a vízgyűjtő-mező

A stream-power vízhozam-tagja (`A^m`) globális, rács-alapú mennyiség
(ND-124 `FlowAccumulation`) — pontonkénti zárt alakban nem áll elő, és a
pipeline-ban körkörös lenne. **De egy procedurális világban a lefolyás-hálózatot
maga a zaj határozza meg**: a víz a topográfiai mélyedésekbe fut, azok pedig
pontosan a ridged multifractal alacsony értékű helyei. A `primary` zajérték tehát
**nem proxyja a vízgyűjtőnek, hanem az oka** — közvetlenül használható, nem
közelítés. Ez az a pont, ahol a 2. kör érdemben teljesíti az eredeti (C) opció
célját anélkül, hogy a globális mezőt be kellene húzni a pontonkénti függvénybe.

##### A forma

A bevágódás ugyanazt a térbeli mintát erősíti, amit a diffúzió csillapít (a
primary relief-deviációt), csak ellenkező előjellel és saját, rövidebb
időállandóval — ezért **multiplikatív tényező** a `D_primary`-n:

```
D_primary(t) = D_p(t) * A_f(t)
A_f(t)       = 1 + gain * fluvialFraction(|lat|) * (1 - exp(-t_f / tau_f))
t_f          = W(|lat|) * t          (CSAK a folyóvízi hatékonyság)
```

A `fluvialFraction = W / (W + G·f_ice)` az erózió folyóvízi hányada — **származtatott,
nincs saját konstansa**. Indok: a gleccser nem felszabdalja, hanem leplanálja a
felszínt (U-alakú trog, lenyesett pajzs), tehát ahol a jég dominál, ott a
bevágódás elnyomódik. Mérve: egyenlítő 1,0000 → 45° 0,2611 → 80° 0,0549.

A két tényező együtt **relief-emelkedés, majd -hanyatlás** görbét ad:

| t (Myr) | 0 | 10 | 25 | **40** | 60 | 100 | 200 | 500 | 1000 | 3000 |
|---|---|---|---|---|---|---|---|---|---|---|
| `D_primary` | 1,0000 | 1,1137 | 1,2082 | **1,2441** | 1,2410 | 1,1619 | 0,9252 | 0,5923 | 0,4913 | 0,4800 |

Csak az **elsődleges** tagra: a bevágódás rövid hullámhosszú folyamat (völgyek),
a regionális hullámzást nem szabdalja fel. Fizikai indok, nem önkényes választás.

**ND-04.** Mindkét tényező **önállóan** félcsoport (exponenciális), a szorzatuk
tiszta függvénye `t`-nek. A „lépésenként szimulálva" értelmezés **két
állapotváltozót** tart — ugyanúgy, ahogy az elsődleges és a másodlagos
relief-tag is külön láncolódik. Mérve: a bevágódás láncolási eltérése
8,9e-16, a teljes `D_primary`-é 1,5e-14.

##### Parti abrázió

A hullámzás a tengerszint körüli **sávban** planálja a felszínt: a szirt
visszavágódik, a self feltöltődik. Mindkét irány a tengerszint felé mozgat —
ugyanaz a „relaxáció egy célszint felé" minta, mint a relief-tagoknál, csak a
cél nem a zaj-átlag, hanem a tengerszint.

```
delta = -(h - seaLevel_0) * planingFraction * exp(-0.5*((h-seaLevel_0)/band)^2)
        * (1 - f_ice) * (1 - exp(-t / tau_c))
```

A **statikus (t = 0) tengerszintet** használja, nem a pillanatnyit: (a) így tiszta
függvény marad `(pozíció, t)`-ből, (b) különben körkörös lenne (a tengerszint az
elevációból számolódik). A két szint közti különbség deep-time-ban néhány száz
méter, a sáv szélességén belüli hiba. A viewer és a CLI a `t = 0` mező
kalibrációjából kapja (a viewerben ez már eleve megvolt az ND-38
térfogat-cache-hez).

**Jégtakaró alatt kikapcsol** (`1 - f_ice`): egy jégpajzs alá szorult part nem
abradálódik. Mért értékek 400 Myr-nél, a tengerszint −150 m-nél: +300 m-en
−77,45 m (szirt vissza), −300 m-en +77,45 m (self fel), ±4000 m-en 0,00 m.
**Soha nem lő túl** a tengerszinten (`planingFraction ≤ 0,55`), tehát a
part/tenger reláció nem fordulhat meg — ez külön teszt, mert egy villogó
partvonal a legszembetűnőbb hibaosztály lenne.

##### A 2. kör mért hatása

| | 1. kör | 2. kör |
|---|---|---|
| kontinentális átlagos elmozdulás 2 Gyr-nél | 222 m | 195 m |
| relief szórásának csökkenése 500 Myr-nél | −16,5% | −13,0% |
| `D_primary` maximuma | 1,0 (`t = 0`) | **1,2441** (`t ≈ 40 Myr`) |
| globális átlag eltolódása 3 Gyr-nél | −13,8 m | −9,8 m |

A telítési „mennyire más" metrika **szándékosan kisebb** lett: a bevágódás
megőrzi a relief egy részét (az egyensúlyi `D_primary` 0,30 helyett
0,30·(1+gain) = 0,48). Közben a domborzat **változatosabb** — a köztes időkben
felszabdalt felföldek jelennek meg, nem csak fakóbb csúcsok.

##### Új konstansok (ILLUSZTRATÍV, vizuális megerősítést igényelnek)

| Konstans | Érték |
|---|---|
| `FluvialDissectionGain` | 0,6 |
| `FluvialDissectionTauMyr` | 40 Myr |
| `CoastalBandMeters` | 250 m (Gauss-szigma) |
| `CoastalPlaningFraction` | 0,55 |
| `CoastalAbrasionTauMyr` | 120 Myr |

##### Még egy éleset, amit a teszt fogott meg: a negatív idő

Az 1. körben a negatív erózió-idő matematikailag **felerősített**; a 2. körben a
két felerősítő tényező szorzata **±végtelenbe csordult** (`t = -1e9`), ami
csendben megmérgezte volna az egész elevációmezőt. Ezért mostantól minden tag
„nulla előtt nincs erózió"-ként értelmezi a negatív időt (identitás). A deep-time
csúszka 0-tól indul, tehát ez a modellen nem változtat — csak egy garbage
tartományt szüntet meg.

##### Mi marad nyitva a 2. kör UTÁN

- **Hálózat menti hordalékszállítás, delta-építés, medencék közti tömegátvitel.**
  Ez az, amit sem az 1., sem a 2. kör nem tud: a modell a relief-mezőn **belül**
  rendez át tömeget (csúcs le, medence fel, part a tengerszint felé), de nem
  mozgat anyagot **lefelé a folyóhálózaton**. Ehhez a hálózat mentén akkumuláló
  számítás kell — ez az eredeti **(C) opció**, és **felhasználói döntést igényel**,
  mert: (1) `O(N × tile)` minden időlekérdezésnél, a mai ~2,5 s-os deep-time
  Build tetejére; (2) **megtöri a láncolhatóságot** — a csúszkával 0 → 100 → 200
  Myr más világot adna, mint a közvetlen 200 Myr, tehát az ND-04 értelmezését
  módosítja („fix `N`-nel `t`-ből tiszta függvény", nem „láncolható").
- **A lemez vándorlási történetének integrálása.** Az eróziós hatékonyság a pont
  MAI szélességéből jön; egy pólustól az egyenlítőig vándorolt kéreg vegyes
  klímatörténetet élt át. Zárt alakban ez az Euler-pólus körüli pálya menti
  integrál lenne — de a `W` (cos^8 + Gauss) **nem integrálható analitikusan**,
  tehát fix-`N` kvadratúra kellene, ami ugyanazt az ND-04 kérdést nyitja meg,
  mint a (C) opció. Ezért ez is **döntés-köteles**, nem elvégezhető munka.
- **A `GlaciationPeriodMyr = 150` és `AmplitudeK = 6`** továbbra is illusztratív
  (ND-44 nyitott pontja, todo2 B14). A 2. kör óta **három** tag épül rájuk
  (jégvonal, glaciális erózió, a bevágódás jég-elnyomása), tehát megerősítésük
  még fontosabb lett.
- **A `glaciated_fraction` részperiódus-közelítése** (lásd lentebb, változatlan).
- **Technikai adósság:** a deep-time paraméterlista kinőtte magát
  (`ElevationWithBoundaryFromWarpedAtTime` 16 paraméter). A `(plateTime,
  erosionTime, staticSeaLevel)` hármast egy `readonly struct` kontextusba kell
  fogni — tisztán kozmetikai, seed-semleges átalakítás, felvéve a todo2-be.

##### Verziózás (2. kör)

`WorldGeneratorVersion.Current` **4 → 5**. `t = 0` **bitre változatlan**, világ-hash
szinten is igazolva: `worldgen hash --time 0` az 1. kör előtt, az 1. kör után és
a 2. kör után egyaránt `2b98af9a…6213738b`. A `--time 400` hash az 1. körben
`590ec46e…957e441f`, a 2. körben `14dc8ad2…59a7ea07`. A
`thermal_checkpoint_ref.py` generátorverzióját is emelni kellett (4 → 5).

#### Mellékesen kiderült: a `DeterministicMath.Exp` alulcsordulás-hibája

Az éleset-teszt megfogta, hogy a `DeterministicMath.Exp` kb. −710 alatti
argumentumra **nem 0-hoz tart, hanem szemetet ad** (mérve: `exp(-710)` =
`-1,45e+308`, `exp(-750)` = `-6,1e+290`, `exp(-4e6)` = `+4,46e+145`): a `ScaleByPowerOfTwo`
bit-manipulációval vonja le az exponenst, és nem kezeli az alulcsordulást (az
exponens átcsordul az előjelbitbe). A modellben ez sosem fordulna elő (a
kitevő Gyr-léptéknél is ~80 alatt marad), ezért itt **lokális kitevő-korlát**
(`MaxDecayExponent = 700`) került be, nem a `DeterministicMath` módosítása —
az minden Exp-használót érintő, külön mérlegelendő változás lenne.
**Felvéve a todo2-be (A21).**

#### Mellékesen javított, pre-existing inkonzisztencia: a deep-time kopás szétesett a fogyasztók között

A munka közben kiderült, hogy az uplift-relaxációt **csak a viewer** adta hozzá
(`ApplyDeepTimeErosionToField`, egy külön mezőpassz). A Core
`ElevationWithBoundaryFromWarpedAtTime`-ot hívó többi fogyasztó — a
`SeaLevelCalibration` mezője, a `RiverPathTracing` folytonos nyomvonalkövetése
és a `worldgen hash` / `checkpoint` CLI — **egyszerűen kihagyta**: `t > 0`-nál
a folyó relaxáció nélküli hegyeken keresett lejtőt, a determinizmus-eszköz
pedig egy olyan világot hashelt, amit a viewer nem is jelenít meg.

Ezért a deep-time kopás **teljes egészében a Core-ba került**: az
`ElevationWithBoundaryFromWarpedAtTime` mostantól a relief-csillapítást ÉS az
uplift-relaxációt is elvégzi (ez a függvény a TELJES elevációt adja vissza; a
szétbontott alakot továbbra is a `BaseAndUpliftFromWarpedAtTime` adja nyers
uplifttel). A viewer külön eróziós passza **törölve**, a CLI pedig átadja a
`--time`-ot eróziós időként is. Mellékhatás: a három elevációs út (Core mező,
cache-elt tile-közép, pontszerű sarok) mostantól **ugyanabban a műveleti
sorrendben** dolgozik — `(base + relaxált uplift)`, utána kráter —, korábban a
tile-közép útján a kráter közbeékelődött, és az utolsó biteken eltért.

#### Világ-hash bizonyíték (`worldgen hash`, seed A7C944210000, 20 lemez, level 6)

| `--time` | változás előtt | változás után |
|---|---|---|
| 0 | `2b98af9a…6213738b` | `2b98af9a…6213738b` — **bitre azonos** |
| 400 | `6c7a8d2f…c4003956` | `590ec46e…957e441f` — változik (erózió + relaxáció bekerült) |

#### Verziózás

`WorldGeneratorVersion.Current` **3 → 4** (ND-108). Seed-törő minden `t > 0`-ra;
`t = 0`-nál a kimenet **bitre változatlan**, és ezt explicit rövidzár
garantálja: `t_eff = 0` → a csillapító egzaktul `1,0`, az pedig rövidzár az
eredeti kifejezésre (az `m + (x - m) * 1.0` alak nem adná vissza bitre `x`-et).
A `thermal_checkpoint_ref.py` generátorverzióját is emelni kellett (a
checkpoint-identitás hasheli).

#### Bizonyíték

**Python orákulum** — `tools/reference/erosion_glaciation_deep_time_ref.py`
(bővítve), `plate_frame_noise_ref.py` (csillapítás-átvezetés, bitre semleges),
`crust_elevation_ref.py` (a két gömbfelszíni átlag). Szakaszok: 3a zonális
profil alakja, 3b **ismert-válasz** (a jeges időhányad zárt alakja vs. numerikus
időintegrál 40 perióduson), 3c timestep-invariancia (max eltérés 1,5e-14),
3d lerakódás, 3e átlagtartás, 3f láthatóság és differenciált kopás.
Új vektorok: 400 relief-eróziós + 120 szétválasztott-idő.

**C# tesztek** — `tests/WorldGen.Core.Tests/Tectonics/DeepTimeReliefErosionTests.cs`
(27 teszt): KAT a két új vektorhalmazra **1e-12 tűréssel** (mindkét oldal
ugyanazt a `DeterministicMath`-ot implementálja, tehát nem kell a régi, laza
1e-6), a modell-konstansok egyezése, `t = 0` bit-regresszió, tisztaság,
párhuzamos = szekvenciális, timestep-invariancia, zonális alak, a jeges
időhányad numerikus ellenőrzése, differenciált kopás, kétirányú mozgás
(lerakódás), láthatóság + monotonitás, élesetek, simulás + átlagtartás.

Teljes futás: **1626 teszt zöld** (Core 624, Viewer.LodChunking 526,
App.Foundation 452, Cli 24).

### ND-138 — Sebesség-alapú határ-interakció: konvergens / divergens / transzform (C7, NYITOTT)

**2026-09-22.** Felhasználói kérés. A jelenlegi állapot a `PlateBoundaryEffect`
saját doksijából idézve: „a lemezek NEM mozognak M4-ben, ezért nincs valódi
konvergens/divergens/transform megkülönböztetés — az M10 (»Deep time«) adja
majd hozzá a sebesség-adatot, ami ezt lehetővé tenné."

**Az M10 azóta lezárult** (Euler-pólus + `omega` minden lemezre,
`PlateMotion`), **de a határ-hatás nem lett rákötve.** Az uplift ma kizárólag
a határtól mért távolságból jön (a két legnagyobb dot-product `gap`-je),
kéregtípussal (ND-32) és `MountainMask`-kal (ND-35) modulálva. Következmény:

**egy széttartó (rift) határ pontosan ugyanazt a hegység-bónuszt kapja, mint
egy ütköző.** A spec §14.3 divergens ága (rifting, új kéreg, medence-képződés)
és transzform ága (vetődés, lokális relief, földrengés-proxy) nincs
implementálva.

#### A hiányzó számítás — és hogy miért olcsó

A `p` határponton a két lemez felszíni sebessége merevtest-forgásból:

```
v_i = omega_i * (k_i x p)        k_i = a lemez Euler-tengelye (egységvektor)
dv  = v_i - v_j                  (relatív sebesség, sugár/Myr egységben)
```

A határ normálisa a `p*s_i - p*s_j` gradienséből, érintősíkra vetítve:

```
d     = s_i - s_j                (a két MOZGATOTT lemez-mag különbsége)
n_raw = d - p * (p . d)
n     = n_raw / |n_raw|          (n a nyertes lemez belseje felé mutat)
```

Besorolás a `c = dv . n` előjeles záródási rátából:

```
c < 0  -> konvergens   (i anyaga a határ felé nyomul)
c > 0  -> divergens    (i anyaga visszahúzódik a saját belseje felé)
|c| << |dv|  ->  transzform  (a mozgás a határral párhuzamos)
```

A transzform komponens `sqrt(|dv|^2 - c^2)`. **Minden tag csak `+ - * /` és
`Math.Sqrt` — tehát bitpontos minden platformon, nincs ND-23-kockázat**, és a
kereszt-/skalárszorzatok nagyságrendileg olcsóbbak, mint a már futó
zaj-kiértékelés. Nincs új állapot: `(seed, p, t)` tiszta függvénye marad, az
ND-04 sértetlen.

#### Amit a három ág adna

| Ág | Kontinentális–kontinentális | Óceáni–kontinentális | Óceáni–óceáni |
|---|---|---|---|
| **Konvergens** | orogén, a mai uplift teljes súllyal | vulkáni ív + árok (aszimmetrikus) | szigetív (ND-32 már csökkentett) |
| **Divergens** | riftvölgy: **negatív** graben, kiemelt vállakkal | — (ritka) | óceánközépi hátság: mérsékelt pozitív relief |
| **Transzform** | nincs uplift, csak lokális, vonal menti érdesség | ugyanaz | ugyanaz |

Figyelem: a divergens ág **nem egyszerűen »nulla uplift«**. Egy óceánközépi
hátság kiemelkedik az abisszális síkból, egy kontinentális rift pedig
bemélyed — mindkettő előjeles, nem hiányzó hatás. A mai egységes
`DefaultUpliftMaxMeters = 1000` plafon (ND-90) így két külön konstanssá válik.

#### Opciók

- **(A) Maradjon egységes.** A felhasználó explicit kérte a javítást; elvetve.
- **(B) Háromirányú, folytonos besorolás a mai uplift-tagon.** A `c`-ből
  folytonos súlyok (nem kemény kategória — a küszöb-alapú besorolás a
  határ mentén villogó sávokat adna), ezek skálázzák/előjelezik a meglévő
  `BoundaryUpliftFromNearestPlates`-et. Kis kódváltozás, a teljes lánc
  bitpontos marad.
- **(C) (B) + valódi hátság-/rift-morfológia.** A hátság profilja a kéreg
  **korából** jön (a hátságtól távolodva mélyül az óceánfenék) — ehhez
  kéreg-kor mező kell, ami az **ND-139** reprezentáció-váltása.

**Javaslat: (B) most, (C) az ND-139-cel.** A (B) önmagában megszünteti a
legszembetűnőbb hibát (hegylánc a riften), és nem igényel új adatszerkezetet.

**Verziózás — fontos különbség az ND-136/137-hez képest:** ez **`t = 0`-nál is
seed-törő**, mert a lemezek sebessége `t = 0`-ban sem nulla, tehát a
besorolás már a statikus világot is átrendezi. Verzió-emelés (ND-108) **és**
az ND-09 ordinális kalibráció újrafuttatása kell, ugyanúgy, mint a B2-nél.
Ezért ez **felhasználói döntés**, nem önállóan elvégezhető munka.

**Munkarend:** Python orákulum (`tools/reference/plate_boundary_kinematics_ref.py`)
→ ismert-válasz vektorok (merevtest-forgás analitikus eseteivel: tiszta
konvergencia, tiszta divergencia, tiszta nyírás) → C#.

### ND-139 — Lemez-lemez ütközés és kéreg-megmaradás (C8, NYITOTT)

**2026-09-22.** Felhasználói kérés. Ma a lemezek **szabadon áthaladnak
egymáson**: a hozzárendelés minden pillanatban gömbi Voronoi az elmozgatott
magokhoz (`PlateGeneration.AssignPlateWarped`), tehát definíció szerint nincs
átfedés és nincs hézag — a partíció mindig tökéletes. Ebből következik, hogy
**nincs ütközés, nincs kéreg-megmaradás, nincs szubdukciós fogyasztás, és
nincs deformáció-visszacsatolás a mozgásra.** Két kontinens „átúszik"
egymáson ahelyett, hogy összetorlódna.

#### Miért ez a legnehezebb tétel

A mai modell **euleri**: minden mező a rögzített térpont függvénye, a lemez
csak egy címke, ami a pont fölött vált. Az ütközés viszont **lagrange-i**
kérdés — ahhoz a kéreganyagot kell követni. Ez három dolgot bont meg
egyszerre:

1. **Az ND-63 „pozíció + seed" tisztaságát.** A kéreg-kor és -vastagság nem
   számolható ki egy pontból zárt alakban: attól függ, mikor lépte át a pont
   a hátságot, ami a határok korábbi mozgásának integrálja.
2. **Az ND-04 timestep-invarianciát.** Ha a kéreg-állapot akkumulálódik, a
   csúszkával 0 → 100 → 200 Myr más világot ad, mint a közvetlen 200 Myr.
3. **A Voronoi-gyorsaságot.** Az `AssignPlate` a legforróbb úton van (minden
   tile, minden sarok); egy anyagkövető réteg ezt nem helyettesíti, hanem
   melléteszi.

#### Opciók

- **(A) Maradjon.** A felhasználó explicit kérte a javítást; elvetve, de
  megjegyzendő, hogy ennek a tételnek a látható haszna a legkisebb a ráfordított
  munkához képest — az ND-136 és ND-138 sokkal többet ad olcsóbban.
- **(B) Tile-onkénti kéreg-nyilvántartás (teljes lagrange-i modell).**
  Kéreg-kor, -vastagság és -típus tile-onként, a lemezzel együtt advektálva;
  az eleváció izosztáziából (vastagság → felhajtóerő), konvergens határon
  vastagodás, szubdukciónál fogyasztás. Ez a fizikailag helyes válasz, és
  ugyanez a réteg oldaná meg a **C5**-öt (lemez-életciklus: a `PlateLifecycle`
  split/merge ma kész, de nincs bekötve) és a **C6**-ot (lemezen belüli
  kevert kéreg, ami a partvonal-simaság és a kontinensméret ütközését
  feloldaná). **Ára:** az ND-04 explicit módosítása, perzisztált világállapot
  (→ C3 mentés-szekció), és nagyságrendekkel nagyobb munka, mint az eddigi
  tektonikai tételek együttvéve.
- **(C) Fél-analitikus torlódás-proxy.** A Voronoi marad, de a két lemez
  **integrált relatív elmozdulásából** (az ND-138 `c` záródási rátájának
  `t`-re vett integrálja — merevtest-forgásnál ez zárt alakban megvan) egy
  „mennyit torlódtak eddig" skalár, ami a kéregvastagodás proxyja, és
  vastagodás-arányosan emeli az orogént. Megtartja a `(seed, p, t)` tisztaságot
  és az ND-04-et, nincs perzisztált állapot, és megadja a **látható**
  viselkedést: ahol két lemez régóta közeledik, ott magas hegység nő; ahol
  épp most kezdett, ott még alacsony. Nem valódi tömegmegmaradás.

**Javaslat: (C) MVP-ként, (B) csak külön, összevont tervvel.** A (C) az
ND-138 melléktermékeként szinte ingyen adódik. A (B)-t **nem szabad
önmagában** elkezdeni: a C5-tel és a C6-tal egy tervbe kell vonni, mert
mindhárom ugyanazt a tile-onkénti kéreg-állapotot kéri, és külön-külön
bevezetve háromszor törnénk seed-kompatibilitást.

**Verziózás:** mindkét opció seed-törő minden `t`-re; a (B) ezen felül a
mentés-formátumot is érinti (C3/D4) és a `PlateId` → `ulong` globális váltást
is maga után vonja (a C5 régóta nyitott előfeltétele).

### ND-140 — Előírt vs. számított lemezmozgás; az `omega`-tartomány felülvizsgálata (C9, NYITOTT)

**2026-09-22.** Felhasználói kérés. A mozgás ma **kinematikai, nem dinamikai**:
minden lemez a seedből kap egy fix Euler-pólust és egy fix szögsebességet
(`PlateMotion.GenerateAngularVelocity`, egyenletes eloszlás
`[0,01; 0,09] rad/Myr`), és ez a világ **teljes 1 milliárd évére konstans**.
Nincs köpenyáramlás, nincs hajtóerő (slab pull, ridge push), nincs ellenállás,
és nincs visszacsatolás a lemezek kölcsönhatásából.

#### Egy külön, olcsó résztétel: maga a tartomány

A `PlanetConstants.RadiusMeters = 7 420 000` mellett a kerületi sebesség az
Euler-egyenlítőn:

| `omega` | km/Myr | cm/év | viszonyítás |
|---|---|---|---|
| 0,01 rad/Myr (alsó) | 74,2 | **7,4** | reális (Föld: 1–15 cm/év) |
| 0,09 rad/Myr (felső) | 667,8 | **66,8** | a földi maximum **~4,5×-e** |

A felső vég tehát irreálisan gyors. Ez **nem a jelen ND fő kérdése**, de a
legolcsóbb javítás az egész blokkban: egyetlen konstans, és a hatása a
deep-time látványra azonnali (ma a csúszka végén a kontinensek indokolatlanul
messzire szaladnak). Cserébe seed-törő, tehát verzióemeléssel jár — érdemes
ugyanabba a körbe tenni, mint az ND-138-at.

#### Opciók a mozgás-modellre

- **(A) Marad a konstans `omega`**, csak a tartomány kerül felülvizsgálatra
  (fent). Nincs új matek, nincs új adatszerkezet.
- **(B) Időben változó, de ZÁRT ALAKÚ `omega(t)`.** Pl.
  `omega(t) = omega_0 + A * sin(2*pi*t/T + phi)`, ahol az `A`, `T`, `phi`
  seedből jön. A forgásszög ennek az integrálja, ami zárt alakban megvan:

  ```
  theta(t) = omega_0*t + (A*T / 2*pi) * (cos(phi) - cos(2*pi*t/T + phi))
  ```

  Tehát `P(t)` továbbra is **tiszta függvény `t`-ből**, az ND-04 sértetlen, és
  a `DeterministicMath.SinCos` (ND-27) miatt bitpontos marad. Ezzel a lemezek
  gyorsulnak-lassulnak a deep-time során, ami a valóságot sokkal jobban
  közelíti, mint a konstans — dinamika nélkül.
- **(C) Valódi erőegyensúly (köpenyáramlás, slab pull, ridge push).** Ehhez
  kell az ND-139 (B) anyagkövető reprezentációja ÉS akkumulált állapot, tehát
  az ND-04 feladása. Gyakorlatilag egy másik szimulátor. **Az 1.0 hatókörén
  kívül.**

**Javaslat: (A) most, (B) az ND-138 után, (C) nem az 1.0-ban.** A sorrend
C7 → C8 → C9 (ND-138 → ND-139 → ND-140) azért ez, mert a (C)-nek nincs mire
visszacsatolnia, amíg nincs kéreg-reprezentáció.

**Verziózás:** mindkettő seed-törő minden `t > 0`-ra, de **`t = 0`-nál egyik
sem**: az `omega` csak a `PlateSeedAtTime` `angle = omega * t` szorzatában
jelenik meg, ami `t = 0`-nál azonosan nulla. (A `PlateLifecycle` is használja,
de az ma nincs bekötve az elevációs láncba — C5.) Verzió-emelés (ND-108) kell,
és ez a tétel ebből a szempontból az ND-136/137-tel egy csoportba esik, nem az
ND-138-cal.

### ND-141 — Egységes felszíni overlay-választás (A7, IMPLEMENTÁLT)

**2026-09-23, implementáció előtt.** Az ND-104 szerinti egyetlen
`SurfaceOverlayMode` a megjelenítés autoritatív állapota: None,
SurfaceTemperature, AirTemperature, WindSpeed, Precipitation és a később
hozzáadott TectonicPlates. A régi mezők csak rejtett, egyszeri
szerializációs migrációhoz maradnak meg. Régi, egyszerre aktív kapcsolóknál
a tényleges régi renderprioritás érvényes: hő → tektonika → szél → csapadék.
Új, már verziózott beállítást a régi mezők soha nem írhatnak felül.

A futásidejű gombok ugyanazt az enumot állítják, az Inspector is ezt mutatja.
Hőmódok közt csak shader-/textúra-váltás történik; a vertexszínt használó
módok a meglévő vizuális invalidáción mennek át, nem teljes világ-Buildön.
A hősolver futása független a nézettől. Ez nem numerikus modellváltozás,
ezért önmagában nem emeli a generátorverziót. A7 6–7. fázisa külön
numerikus döntést és validációt kap; az enum elkészülte nem zárja le A7-et.

### ND-142 — Levegőanomália visszahatása a szélre, explicit időintegráció (A7, IMPLEMENTÁLT, KALIBRÁLT)

**Dátum:** 2026-09-23. **Az implementációt megelőző döntés.**

A 7. fázisban a meglévő termikus szél közelítéséhez hozzáadjuk a számított
`thetaA = Ta − Ba` térbeli gradiensét. A napi bázisgradiens és az anomália
összege kerül a meglévő ND-126b sebességkorlátozás és Coriolis-forgatás elé;
így a bázist nem számoljuk kétszer, és a termikus komponens határa továbbra
is 30 m/s. Ez diagnosztikus nyomási hatást közelítő szélmodell, nem önálló
légnyomás- vagy impulzusmegmaradási egyenlet. Nem jelenítünk meg belőle
kitalált hPa-értéket. A termikus cirkuláció irányának fizikai ellenőrzése:
[NWS: Lake Breezes](https://www.weather.gov/apx/lake_breeze); a modell
együtthatói a meglévő játékmodellből származnak, nem ebből a forrásból.

A cellagradiens a négy szomszéd érintősíkba vetített középpontkülönbségére
illesztett, súlyozatlan 2D legkisebb négyzetes egyenes. A kanonikus élsorrend
határozza meg az összegezést. Az élgradienst a két cella 3D gradienseinek
számtani átlaga adja, az élközép érintősíkjába vetítve. Ez a cube-face
varratokon is ugyanaz az eljárás. A geometriai együtthatók előre készülnek.

Időintegráció: a tick **eleji**, minden cellán teljes `thetaA` határozza meg
az adott 900 s-os tick szelét; a napi bázis továbbra is a tick közepén
interpolált. Ezzel fut az advekció, majd a lokális hőcsere. Nincs bejárási
sorrendfüggő cellánkénti visszacsatolás és nincs konvergálásig tartó,
platformfüggő megállás. A fél/negyed időlépéses referenciafutás a
konvergencia ellenőrzése; a produkciós tick változatlan.

A Python referencia készül először, analitikus konstans/lineáris gradiens-
ellenőrzéssel, utána a vektorok és C# port. Kötelező a paraméterhatás,
varrat/pólus, véges/bounded szél, kanonikus újrajátszás, párhuzamos azonosság
és teljes level-6 próba. A numerikus viselkedés változása miatt
`ThermalModelParameters.ModelVersion: 2 → 3`,
`WorldGeneratorVersion.Current: "1" → "2"`; a meglévő A6 betöltési kapu
visszautasítja az előző generátor állapotát. A régi egyirányú `ThermalWind`
mintavétel referencia/összehasonlítás céljára megmarad; a solver és a saját
diagnosztikája a csatolt szelet használja.

**Első validáció, kalibráció előtt (2026-09-23):** level 3-on a 10/20 napos előfutás
maximális eltérése a 30 napos futástól 0,1021/0,0561 K; level 6-on viszont
**1,6286/0,7708 K**. A korábbi egyirányú modellhez közölt ≤0,18 K érték
nem volt átvihető a teljes erősségű csatolásra. Ezt a beállítást nem
fogadtuk el; ezért következett az alábbi bucket-vizsgálat és kalibráció.
A kisrácsos zöld teszt
önmagában nem elegendő. Nem növeljük meg utólag a korábbi küszöböt.
A tényleges 30. napi bucket-váltás level-6 kontrollja (folyamatos 40 napos
futás vs. új 10 napos előfutás ugyanarra a tickre): maximum **1,6899 K**,
globális `Ts/Ta` RMS **0,11275 K**, a maximum szárazföldi cellán jelentkezett.
Ez a kezdeti beállítás kanonikus újraindítási hibája volt; nem véletlen
mérési zajként vagy a teszt-tolerancia lazításával kezeltük.

**Kontrollparaméter a kalibrációhoz (implementáció előtt):**
`AirFeedbackStrength`, dimenziómentes 0..1 szorzó az anomáliagradiensre.
Kezdeti értéke 1 (az eredeti teljes visszacsatolás), 0 az anomália nélküli
kontroll. A napi bázisgradiens és a sebességkorlát nem változik. A paraméter
a fizikai bemeneti azonosító része; nem viewer-/frame-függő kapcsoló.
A 0/0,25/1 kontroll célja a régi előfutási hiba és a csatolás hatásának
szétválasztása. Új alapérték csak a teljes rács mérése alapján választható.

**Kalibrált alapérték (2026-09-23): `AirFeedbackStrength = 0,1`.**
Azonos level-6 tesztvilágon, 10/30 nap max. eltérés: 0 → 0,14778 K;
0,1 → **0,17317 K**; 0,25 → 0,22115 K; 1 → 1,62860 K.
A 0,1-es érték megtartja az eredeti ≤0,18 K előfutási célt, miközben
a visszacsatolás hatása nem nulla. Ez mérésből választott játékmodell-
kalibráció, nem univerzális légkörfizikai konstans. A teljes erősségű
kezdeti kísérletet nem tesszük alapértelmezetté. A regressziós kapu
level 6-on is fut, hogy a kisrács ne fedhesse el a hibát. A vektorokat
a kiválasztott alapértékkel újra kell generálni.

**Kalibrált bucket-határ:** a 30. napon a 0,1-es csatolás max.
**0,19788 K**, globális RMS **0,04880 K** újraindítási eltérést ad;
a csatolás nélküli kontroll **0,21948 / 0,04961 K**. A kezdeti kísérlet
1,69 K-os új hibáját a kalibráció korrigálta. A kanonikus újraindítás
kis maradékeltérése már a kontrollban is létezik, nem tűnt el. A 0,18 K
előfutási teszt az azonos időpontú 10/30 napos mérés kapuja, nem minden
évszakra és bucket-határra bizonyított általános maximum. A kalibrált
paraméterrel a teljes level-6 regresszió Debug/Release alatt is zöld.

A 6. fázis fogyasztói kapuja külön marad: a visszacsatolás önmagában még
nem teszi a pillanatnyi mezőt éves biome-/jégstatisztikává. Az A7 nem
zárható le pusztán a 7. fázis és az overlay elkészítésével.

### ND-143 — Hőállapot-azonosság, checkpoint és a 6. fázis fogyasztói kapuja (A7, CHECKPOINT IMPLEMENTÁLT, ÁTÁLLÁS NYITOTT)

**Dátum:** 2026-09-23. **Az implementációt megelőző döntés.**

A `ThermalSnapshot` folytathatóságához a kezdőtick önmagában nem elegendő:
ugyanahhoz a rácshoz, felszíntípus-/magasságmezőhöz, seedhez, geológiai
időhöz, pályához és fizikai paraméterekhez kell tartoznia. A Core ezekből,
a generátor- és hőmodellverzióból kanonikus SHA-256 bemeneti azonosítót
képez. A checkpoint ezt az azonosítót, a kezdő- és állapotticket, valamint
a teljes double `thetaS/thetaA` mezőt tartalmazza. Rögzített byte-sorrend,
fájlformátum-verzió, méretellenőrzés és SHA-256 integritás-ellenőrzés kell;
hibánál a célállapot változatlan. Az eltérő modell vagy világ betöltése
explicit hiba. A kanonikus újraszámítás továbbra is érvényes alternatíva.

A hőállapot hash-e a teljes verziózott checkpoint tartalmából származik.
Ez külön termikus állapothash: a meglévő, elevációt összegző
`WorldStateHash` jelentését nem írjuk át csendben. Az app `simulation-state`
szekciójába kötés továbbra is C3; az A7 Core-codec önmagában nem teljes
alkalmazásmentés.

**A 6. fázis átállítási határa:** a pillanatnyi `Ts` párolgási bemenetnek,
a napi/évszakos `Ta` statisztika klímaosztályozásnak alkalmas adatút.
A tartós jég éves statisztikát igényel. A jelenlegi solver a Build során
már osztályozott jég- és tómezőből indul; ezt nem lehet ugyanazon Buildben
egyszerű függvénycserével visszakötni. A légköri visszacsatolás nem oldja
meg ezt a körkörös függést.

Ezért a numerikus/élő validáció és a megfelelő időátlagokat előállító,
renderfüggetlen klímafuttatás előtt a biome/jég/hidrológia fogyasztói kapu
**nyitva marad**. Ezt az ND-103 és a backlog 6. fázisa már megköveteli;
nem új jóváhagyási követelmény. Az overlay vagy egy elkészült GPU-textúra
nem válhat fizikai bemenetté. A checkpoint/hash előkészítés és a 7. fázis
elkészítése nem jelentheti a teljes A7 lezárását.

### ND-144 — Kanonikus napi hőstatisztika a 6. fázis bemenetéhez (A7, IMPLEMENTÁLT)

**2026-09-23, implementáció előtt.** A biome- és jégátálláshoz a pillanatnyi
`Ts/Ta` nem elég. A Core egy teljes, egész UTC-modellnapra, rögzített
900 s-os tickekből készít cellánkénti átlagot, minimumot és maximumot.
A nap mintái a `[day·96, (day+1)·96)` tickek **eleji** állapotai; az
egyenlő időközű napi átlag a 96 minta összegének 96-tal osztott értéke.
Az összegzés cellánként és időben növekvő ticksorrendben történik.
Az eredmény az ND-143 modellazonosítót és a napindexet hordozza. Az
állapotot a nap elején a kanonikus útvonalra visszük; a nap végén a
következő nap kezdetén áll. Így a kimenet független a korábbi viewer-
snapshotoktól és a lekérdezési sorrendtől.

Az első lépés a napi statisztika Core-adatútja és regressziója. Ez még
nem éves éghajlati átlag: az év hosszát, az évszakmintavételt, a
számítás költségét és a jégmaszk-visszacsatolást külön mérés és döntés
után lehet rögzíteni. A biome/jég/párolgás fogyasztói kapu addig nyitott.
Az új API a világ fizikai kimenetét még nem módosítja, ezért ez a lépés
nem emeli a generátor- vagy hőmodellverziót.

### ND-145 — Modellalapú előnézeti folyóhálózat a hosszú finomítás alatt (A8/B3, IMPLEMENTÁLT, ÉLŐ ELLENŐRZÉS NYITOTT)

**Döntés (2026-09-23).** A felhasználó Play módban nem lát folyókat: a
`PerfLog_20260923_113651.txt` hat egymás utáni, 96 forrásos ND-132 munkát
indított, és mind a hatot a `river ready` előtt megszakította. A `Build()`
az előző finomított hálózatot törli, a `BuildRiverNetwork()` csak a teljes
új finomítás után tud rajzolni. Az A8/1 pontos scene-bemenettel mért
szekvenciális Core-idő 102–150 s, a korábbi egyetlen kész Unity-menet
`river ready` ideje 1477,6 s. A B3 várakozási ítélet ezért jelenleg nem
vizsgálható: a hálózat hiányzik.

**Választott megoldás:** az ND-132 dedikált szálán előbb a meglévő,
gyors `BuildRiverNetworkFromSources` durva, modellalapú útja készül el
ugyanabból a seedből, elmozdult lemezmagokból, tengerszintből és
forráslistából. A level+4 tile-középpontok közé eredetileg legfeljebb 1 km-es,
később az ND-147 mért mesh-költsége miatt legfeljebb 4 km-es
gömbfelszíni köztes renderpontok kerülnek, majd átmeneti szalag-mesh lesz.
Az előnézetet csak az aktuális generációhoz szabad átvenni, főszálon.
Ugyanezen a szálon ezután változatlanul lefut a szekvenciális folytonos
nyomkövető; a kész hálózat lecseréli az előnézetet. Új `Build()` vagy
letiltás az előnézetet is érvényteleníti. Nem jelenítünk meg korábbi
világállapothoz tartozó folyót, és nincs modellfüggetlen vonal.

**Mérési alap:** az A8 diagnosztikai futtatóban a 96 durva út 0 Myr-nál
679,7 ms / 2883 alappont volt .NET 8 Release alatt. Ez nem Unity-vizuális
vagy Unity-falióraidő-mérés. A durva út geometriája ideiglenes és
láthatóan szögletesebb lehet; a végleges numerikus hálózat és a
generátorverzió változatlan. A Build teljes idejébe nem kerül vissza
szinkron folyómunka.

**Kapu:** offline viewer-fordítás és célzott ellenőrzés után élő Unity
Play-kép és PerfLog kell: `river preview` az aktuális Build után rövid
idővel jelenjen meg; deep-time váltáskor a régi előnézet tűnjön el;
`river ready` után a finom vonalak váltsák fel. Az A8/1 round-major
sorrendcseréjének elvetése ettől független.

**Első élő visszajelzés (2026-09-23 15:38):** a felhasználó látta a
folyókat a felszínen. A `PerfLog_20260923_153831.txt` t=0-nál 96 ág és
59 986 renderpont előnézetét mutatja, a Build utáni indítástól 4,530 s
alatt. Ebből a főszálú preview-mesh **3,395 s**, ami jelentős frame-
megakadás; külön teljesítményvizsgálatot igényel. A Play-menet 19,586 s
után `river cancel completed=False` sorral zárult, így a végleges hálózat
és a deep-time csere még nem igazolt.

**Második élő menet:** az előnézet ismét megjelent 4,331 s alatt,
59 986 renderponttal; a főszálú mesh 3,205 s volt. Ez megerősíti az
előnézet láthatóságát és a többmásodperces akadást. A finom hálózat
elkészült; ennek teljesítményét az ND-146 rögzíti.

### ND-146 — A8/2: körsorrend elvetve, korlátos spekulatív Core-próba (AKTÍV)

**Döntés (2026-09-23).** Az A8/1 mérési kapuja nem teljesült a kör szerinti
forrássorrendhez: a t=0 hálózatban a 43-as forrás a 32-es korábban lefoglalt
ágába olvad. A kör szerinti feldolgozás a 43-ast a 32-es elé helyezné.
Az eredeti forráslista, `SourceIndex`, pontsor és pontbitek, befejezési ok,
`MergedIntoRiverIndex` és a `ComputeDischargeWeights` teljes vektora csak a
kanonikus forrásindexű lefoglalással tartható meg. A források átszámozását
vagy a `claimed` térkép kör szerinti írását az azonos kimenetű A8 keretében
elvetjük. Ez a finom utak **medencék közötti** találkozása miatt nem oldható
meg pusztán a durva vízgyűjtők elkülönítésével.

**Megengedett szemantika.** A `claimed` nélküli teljes út spekulatívan,
bármilyen sorrendben kiszámolható, mert a `claimed` csak a megállási pontot
befolyásolja. A véglegesítés viszont 0-tól növekvő forrásindex szerint
ellenőrzi kizárólag a `ClaimCheckIndices` pontokat, az első foglalt tile-nál
a befogadó tényleges pontját fűzi hozzá, és csak a csonkolt út pontjait
foglalja le. Ezt a Core meglévő
`BuildContinuousRiverNetworkFromSourcesParallel` API-ja már megvalósítja.
A későbbi forrás nem kaphat korai, változatlan `claimed` pillanatképet a
korábbi források véglegesítése előtt; csak teljes, foglalás nélküli utat.
Megszakításkor a még nem véglegesített eredmény nem adható át a viewernek.

**Költség és kapu.** A scene-pontos t=0 alapvonalon a 96 szekvenciális út
149,984 s, és a 17 beolvadó ág `claimed` nélküli teljes követése további
38 797 lépést jelentene (+10,0%). A 400 698 végleges XYZ `double` pont
nyers koordinátaterhe 9,62 MB; a spekulatív utak és a `claimed` térkép
ezen felül memóriát kérnek. A korábbi ND-132 szerint a szabadon futó
`Parallel.For` a terep munkáival versenghet. Az A8/1 önmagában nem mért
korlátos párhuzamos konfiguráción stabil teljes elkészülési gyorsulást
vagy memória-/előtéridejű korlátot. A B3 láthatósági hibára az ND-145 előnézete ad külön megoldást;
annak élő Unity-elfogadása még nyitott.

**Új mérés, ugyanazon a napon:** a meglévő, korlátlan `Parallel.For` Core-út
ugyanazon t=0 scene-bemenettel **26,378 s** alatt befejeződött, a 96 folyó
teljes SHA-256 lenyomata egyezik az A8/1 szekvenciális alapvonallal.
Csúcs processz-munkakészlet: **168 972 288 byte**. A worker-korlátot
paraméterré tettük az API-ban, változatlan kanonikus commit mellett.
Két workerrel t=0: **129,422 s**, 117 301 248 byte csúcs; néggyel t=0:
**70,487 s**, ismétlésben **67,347 s** / 222,203 s processz-CPU-idő,
106 389 504, illetve 118 951 936 byte csúcs; t=22 Myr: **47,434 s**,
107 675 648 byte csúcs. Minden teljes SHA-256 lenyomat egyezik a
szekvenciális alapvonallal. A négyworker-es Core-út az A8/3 offline
jelöltje; a korlátlan 16 szálas út viewerbe kötését az ND-132 miatt
nem engedélyezzük. A8/4-hez a viewer előterével együtt mért CPU- és
memóriahatás, a `Build()` és a folyó-mesh ideje, valamint élő Play-kép kell.

**A8/4 bekötési próba:** az ND-132 `LongRunning` háttérfeladat megmarad;
az ND-145 durva előnézet után a finom Core-hívás a négyworker-es párhuzamos
API-ra vált. A forrássorrend és a generációellenőrzés változatlan. A
PerfLog az ütemezést és a workerszámot rögzíti. Ez kísérleti viewer-út:
ha a Unity előtér vagy memória romlik, a szekvenciális referenciaút
visszaállítható, és a gyorsítás nem tekinthető elfogadottnak.

**Első befejezett Unity-menet (2026-09-23 18:06):** t=0, 50 m, 96 forrás,
4 worker; `Build() TELJES` 2,968 s, `river preview` 4,331 s (ebből
főszálú mesh 3,205 s), `river ready` 144,683 s, végleges főszálú mesh
18,505 s, a Build utáni teljes `river mesh` 163,188 s. Az egyetlen
korábbi kész szekvenciális Unity-menet 1477,626 s + 13,236 s volt;
ebből tájékoztatóan ~9,1× rövidebb teljes Build utáni idő látszik,
de a régi idő/step és előtérterhelés nem kontrolláltan azonos. A
3,2/18,5 s-os főszálú mesh-akadás jelentős és külön javítást igényel;
az A8-at ettől még nem jelöljük teljesen elfogadottnak. Deep-time
váltás, memória és képi B3 ítélet nyitott.

### ND-147 — Folyómesh főszálú építés képkockákra osztása (A8/4, PRÓBA)

**Ok (2026-09-23):** két élő t=0 Play-menetben az ND-145 előnézeti mesh
3,205–3,395 s-ig, a második menet végleges 400 698 pontos folyómesh-e
18,505 s-ig foglalta a főszálat. A felhasználó nem figyelte külön a
váltás pillanatát; az észlelt akadás hiánya ezért nem ellenbizonyíték.
Az A8/4 `river ready` 144,683 s-os eredménye mellett a megjelenítési
akadás külön fennmaradó probléma.

**Döntés:** a meglévő `RiverPositionOnSurface` számítást és
`AddRiverRibbon` topológiát változatlanul, pontsorrendben futtatjuk a
főszálon, de egyetlen `LateUpdate` helyett képkockánként korlátos munkával.
A korábban kész aktuális előnézet látható marad, amíg a teljes új mesh
elkészül. Az új mesh csak a teljes pont-/indexlista után, egyező
Build-generációnál válthatja fel. Új Build, kikapcsolt folyók vagy új
előnézet/fine eredmény a függő mesh-munkát eldobja. A 4 ms-os CPU-keret
az egyes felszíni pontok között érvényes; egy hosszú szalag lezárása és a
Unity mesh-feltöltés külön mérendő, ezért még nem ígérünk teljes
frame-csúcs korlátot. Az előnézeti tile-közök 1 km helyett 4 km-enként
kapnak renderpontot: a két élő 1 km-es menet 59 986 pontja és 3,2–3,4 s
főszálú mesh-ideje miatt ezt külön csökkentjük. A durva modellút
topológiája, a Core-folyóhálózat és a seed-verzió változatlan.

**Kapu:** viewer-fordítás, majd élő PerfLogban külön `mesh prepare`
falióraidő/aktív főszálú idő/maximális slice, `mesh upload` idő és a
megjelenített preview/fine átmenet ellenőrzése. A8/B3 végső vizuális
elfogadása csak ezek és felhasználói kép/visszajelzés után történhet.

**Első élő próba (2026-09-23 22:44):** az előnézeti mesh 16 056 pontja
970,4 ms aktív főszálú munkával, legfeljebb 5,1 ms-os szelettel és 1,1 ms
feltöltéssel elkészült. A Play Pause állapota után a 400 696 pontos finom
mesh is elkészült: 19 217,2 ms összesített főszálú munka 47 748,1 ms
falióra alatt, 7,8 ms legnagyobb szelet és 10,8 ms feltöltés. A korábbi
18,505 s egybefüggő mesh-munka megszűnt ebben a menetben. A Pause miatt
az 1496,977 s-os `river ready` nem futásidő-bizonyíték. A felhasználói
látványítélet, szünet nélküli ismétlés és Profiler frame-csúcs még nyitott.

### ND-148 — Zoom közbeni folyó- és frame-megakadás elkülönítése (A8/B3, PRÓBA)

**Megfigyelés (2026-09-23):** az ND-147 után a felhasználó a finom
folyókat látja, de zoomkor néha az egész kép megakad, máskor a
folyóvonalak maradnak le a tereptől. A kész `Rivers` mesh kameramozgásra
nem épül újra. A 2026-09-23-i PerfLog zoomja mellett terep-LOD kérés és
feltöltés szerepel, de összframe-/GPU-idő nincs; a korábbi Pause miatt
a hosszú falióra-rések önmagukban nem bizonyítanak frame-hibát.

**Feltárt konfigurációs hiba:** a `PlanetGridMesh` dokumentált
`riverLineRadialBias` alapértéke 0,5, de a `PlanetView.unity` mentett
értéke még 0,02. Az eredeti ND-49 vizsgálat szerint ez a kis eltérés
nem tartja a folyóvonalat a darabos renderelt terep fölött. A scene
értékét 0,5-re hozzuk; ez csak vizuális kiemelés, nem változtat Core-
folyóutat vagy seedet. A tényleges, zoomfüggő terepmeshre vetítés külön
nagyobb megoldás, ha az új élő kép még takarást mutat.

**Mérési döntés:** 100 ms fölötti frame-réseknél a viewer ritka
`ND-148 frame stall` bejegyzést ír: képkockaszám, delta, az aktuális
`LateUpdate` költsége, zoom, folyómesh- és LOD-függő munka. Ez nem
állít gyökérokot, de a következő megszakítás nélküli Play-menetben
elkülöníti az egész kép akadását a kizárólagos folyó-takarás hibájától.
Az Editor Pause/ablakfókusz váltás utáni nagy deltát külön kell kezelni
az értékelésben. A vizuális és Profiler-elfogadás nyitott.

**Első élő próba (2026-09-25):** a felhasználó az Inspectorban 0,5-re
állított értékkel mind a folyóvonalak, mind az egész kép zoom közbeni
viselkedését javultnak látta. A finom mesh elkészült. A finom hálózat
előtti fókuszált, 1 s alatti 29 frame-rés 147,4 ms átlagú, 243,8 ms
maximális volt, miközben az aktuális `LateUpdate` legfeljebb 2,4 ms;
terep-LOD munka többnyire függőben volt. A finom mesh utáni zoomnál
egy 100,2 ms-os rés még szerepel. A Playben volt Pause, ezért a
`river ready` ideje nem mérvadó. A vizuális javulás megerősített, az
egész kép akadásának oksági magyarázata és a frame-csúcs elfogadása
nyitott.

### ND-149 — Óceán-partvonal: parti víz-gyűrű a terep-vágáshoz (A18, ELFOGADVA, implementálva)

**MÉRT gyökérok (2026-09-25, élő Editor, seed `0xA7C944210000`):** a
szárazföld–óceán határ lépcsősségét a **vízfelszín pereme** adta, nem a
terep-szín váltása. A terep sarok-színe pontszerű és folytonos
(`ContinuousCornerColorAuto`), a vízfelszín viszont azoknak a level-8
tile-oknak az uniója volt, amelyeknek a KÖZÉPPONTJA óceáni — a
`WaterLodSource.ContainsWater` a `BaseLevel`-ig (8) sétál vissza, a statikus
emisszió pedig tile-onként egy teljes, opak quadot rakott le. A/B bizonyíték: a
vízrétegek elrejtésével a színhatár sima
(`history/2026-09-25-a18-ocean-shoreline-diagnosis.md`).

30 150 level-8 partvonal-élből **22,34%** (6 737) mindkét közös sarka a
vízszint alatt volt — ott a vízlap pereme szabadon látszott, semmi nem vágta
ki; további **53,38%** vegyes. A szárazföld-oldali 20 223 határtile
**70,30%-ának** van vízszint alatti sarka. A víz-LOD geometriai finomítása nem
segített: azonos kameránál `deepestLevel = 9`, `replacedRoots = 2048` a
256 267 gyökérből — a KÖRVONAL végig level-8. A hiba nem múlik el zoomra:
~126 km magasságban `deepestLevel = 13`, és a partvonalon a ~1,1 km-es
lépcsők továbbra is látszottak — csak a lépték csökkent.

**DÖNTÉS (az ND-129 mintája):** a vízfelszín tile-halmaza az óceáni tile-ok
**+ parti gyűrű**, és a már renderelt, adaptív terep vágja ki belőle a partot
a z-bufferrel — nulla új eleváció-kiértékelés, a részletesség a terep-LOD-dal
finomodik. A gyűrűből kimarad az a tile, aminek mind a négy sarka a vízszint
FÖLÖTT van (a terep úgyis eltakarná).

**A terjesztés szabálya ÉLENKÉNTI, nem tile-onkénti.** Egy él akkor és csak
akkor marad nyers vízperem, ha a KÉT KÖZÖS SARKA MINDKETTŐ a vízszint alatt
van. A gyűrű pontosan ezeken az éleken lép tovább. Ez konvergál: az óceán
vízszintje globális és állandó, tehát a valódi partnál magától megáll (az
ND-129-nél a tavak feltöltési szintje miatt a terjesztés NEM konvergált, ezért
ott 1 kör a végleges). MÉRVE: az első, tile-alapú („teljesen elöntött tile")
változat 12 nyers élt hagyott (0,05%); az élenkénti szabály **0-t**.

**MIÉRT NEM KELL AL-OSZTÁS (eltérés az ND-129-től):** a terep-quad és a
víz-quad ugyanazon az UV-négyszögön, ugyanazzal a háromszögeléssel
(`AddQuad` p00,p10,p11,p01) fekszik, gyakorlatilag azonos sugáron — a ~25 m-es
húr-behúrás tehát mindkettőből kiesik a különbségképzésnél. A metszésvonal így
pontosan a sarok-elevációk lineárisan interpolált tengerszint-kontúrja,
vagyis ugyanaz a határ, amit a terep sarok-SZÍNE már ma is folytonosan
kirajzol.

**Az ND-149 négy nyitott kérdése lezárva:**

1. *Al-osztás* — nem kell, ld. fent; az élő képek is ezt erősítik meg.
2. *`OceanRefinement.CanSkip` / `IsBaseAncestorOceanic`* — változatlan. A
   gyűrű-tile `IsOceanic` értéke hamis, ezért a finomítás-kihagyás sosem
   alkalmazza rá — pontosan ez kell, hiszen ott a terepnek finomodnia KELL,
   hogy kivágja a vizet.
3. *A gyűrű vízszíne* — magától helyes. A `WaterDepthBucket` a negatív
   mélységet 0-ra vágja (a bucket csak submesh-csoportosítás), a TÉNYLEGES
   szín sarkonként a `ContinuousWaterCornerColor`-ból jön a megfelelő
   szárazföld-sarok elevációja alapján.
4. *Költség* — mérve, ld. lent.

**MÉRT eredmény (ugyanaz a seed, level-8 partvonal-élek):**

| | ND-149 előtt | ND-149 után |
|---|---|---|
| nyers vízperem (mindkét sarok víz alatt) | 6 737 — **22,34%** | **0 — 0,00%** |
| vegyes (a terep átmetszi az élt) | 16 093 — 53,38% | 1 372 — 5,27% |
| a terep teljesen eltakarja a peremet | 7 320 — 24,28% | 24 666 — **94,73%** |
| vizet emittáló level-8 tile | 256 267 (65,17%) | 270 535 (68,80%), **+14 268 (+5,57%)** |

A gyűrű 14 268 tile-t rajzol, 6 032-t kihagy (mind a négy sarka víz fölött),
és 4 körben (a korlát 4) `openBoundary=0`-ra konvergál.

**Költség (10+ Build, medián):** a maszk maga **4,2 ms**. A teljes
`BuildStaticBaseLayer` mediánja 1333 ms (ki) → 1394 ms (be), **+61 ms**, de a
fázisok futásonkénti szórása ennél NAGYOBB (`water` 53–344 ms kikapcsolva is,
`mesh` 143–411 ms), tehát a különbség a zaj nagyságrendjében van; az `emit`
fázis érdemben változatlan (262 → 264 ms). A Build ~2,4 s nagyságrendje nem
változott.

**Tesztelhetőség:** a két tiszta döntés (`Classify`, `EdgeFullyBelowWater`) az
`OceanRefinement` mintájára külön, UnityEngine-referencia nélküli osztályba
került (`Viewer/Lod/CoastalWaterRing.cs`), és Unity Editor NÉLKÜL tesztelt
(`CoastalWaterRingTests`, 23 eset; a LodChunking projekt 526/526 zöld). A
tiszta osztályba emelés után a Build élőben BITRE ugyanazt adta.

**Kapcsoló:** `PlanetGridMesh.coastalWaterRing` (alapértelmezés: be) — az
Inspectorból kikapcsolva a Build az ND-149 ELŐTTI viselkedést adja, így az
A/B bármikor megismételhető.

**Nem seed-törő:** kizárólag render-oldali; Core-mezőt, elevációt, seedet nem
érint, a generátorverziót nem emeli.

**Nyitott:** a felhasználói vizuális átvétel (B-kategória). A lap-varratokon
(kereszt-lap szomszédok) futó partvonal-élek a fenti statisztikából
kimaradtak — a maszk maga viszont a `TileNeighbors.Neighbor` vetítéses útján
a varratokat is kezeli.

### ND-150 — `DeterministicMath` Exp/Ln/Pow tartomány-élesetek (A21, ELFOGADVA, implementálva)

**Honnan jött:** az ND-137 (A20) éleset-tesztje fogta meg. Ott a javítás
*helyi* volt (`DeepTimeErosionGlaciation.MaxDecayExponent = 700`), és a
kódkomment maga jegyezte fel, hogy ez „a `DeterministicMath` saját
hiányossága". Ez az ND a hiányosságot magát zárja le.

**MÉRT gyökérok.** Az `Exp` a Taylor-sor eredményét `ScaleByPowerOfTwo`
bit-manipulációval skálázza: a `rawExponent + k` összeg közvetlenül az
IEEE-754 exponens-mezőbe íródik. A művelet **nem ellenőrizte**, hogy az
összeg bent marad-e a normál double tartományban (`[1, 2046]`). Ha kicsúszik,
a bitek átfolynak a szomszédos mezőkbe — lefelé az **előjelbitbe**, felfelé a
NaN/végtelen mintákba. Az eredmény nem `0`-hoz vagy `+∞`-hez tart, hanem
determinisztikus **szemét**. A `Ln` ugyanígy: a `FrexpBits` bitbontása normál
double-t vár, és a NaN nem esik bele a `x <= 0` kapuba.

Mérve a javítás előtt (Python-orákulum, ugyanaz az algoritmus mint a C#):

| bemenet | ND-150 előtt | helyes | ND-150 után |
|---|---|---|---|
| `exp(-710)` | `-1,4466e+308` | `4,476e-309` | `0,0` |
| `exp(-750)` | `-6,1457e+290` | alulcsordulás | `0,0` |
| `exp(-4e6)` | `+4,4595e+145` | alulcsordulás | `0,0` |
| `exp(710)` | `NaN` | túlcsordulás | `+∞` |
| `exp(711)` | `-1,5331e-308` | túlcsordulás | `+∞` |
| `exp(1e6)` | `6,9566e+271` | túlcsordulás | `+∞` |
| `exp(NaN)` | kivétel (`float NaN to integer`) | `NaN` | `NaN` |
| `ln(5e-324)` | `-709,09` | `-744,44` | `-744,44` |
| `ln(1e-310)` | `-709,085` | `-713,801` | `-713,801` |
| `ln(+∞)` | `709,7827` | `+∞` | `+∞` |
| `ln(NaN)` | `710,1882` | `NaN` | `NaN` |
| `pow(10, 400)` | `-3,0943e-217` | túlcsordulás | `+∞` |
| `pow(10, -400)` | `-3,2317e+216` | alulcsordulás | `0,0` |
| `pow(+∞, 0)` | `1,0` | `1,0` | `1,0` |
| `pow(+∞, 2)` | `-1,0` | `+∞` | `+∞` |

**Miért komoly, ha ma egy modul sem hajt ilyen tartományba.** Az I1
(determinizmus) **nem** sérül — a szemét minden platformon ugyanaz a szemét.
Az I3/I4 viszont sérül, és **semmi nem jelzi**: nincs NaN, nincs kivétel,
nincs vizuális robbanás, csak egy hibás szám a láncban. A `pow(10, 400)`
előjelváltása pontosan az a hibaosztály, ami hónapokig elbújik.

**A DÖNTÉS: a korlát a `DeterministicMath`-ba kerül, nem modulonként.** Az
ND-137 helyi `MaxDecayExponent`-je működött, de minden új `Exp`-használónak
meg kellett volna ismételnie — ez előbb-utóbb kimarad valahol. A helyes hely
a függvény maga.

**Megoldás.**

1. **`ScaleByPowerOfTwoChecked`** — új, ellenőrző változat: a megnövelt
   exponens `<= 0` → `0,0`, `>= 0x7FF` → `+∞`. A nyers `ScaleByPowerOfTwo`
   szándékosan ELLENŐRZÉS NÉLKÜL marad, hogy az „exakt bit-eltolás" jelentés
   egyértelmű legyen.
2. **`Exp` durva argumentum-kapu** (`ExpArgumentGate = 800,0`) — NEM a valódi
   határ (az `709,783` / `-708,396`, azt a pont 1. kezeli exaktan), hanem
   azért kell, hogy a `double → long` konverzió biztonságos legyen: a NaN
   konverziója kivételt dob, a nagyon nagy érték eredménye definiálatlan.
   Plusz explicit `NaN → NaN`.
3. **`Ln`** — `NaN → NaN`, `+∞ → +∞`, és a **subnormális** bemenet exakt
   felskálázása (`× 2^54`, majd `-54` a kitevőből). A `2` hatványával való
   szorzás kerekítésmentes, és 54 eltolás minden subnormálisra elég
   (`2^-1074 · 2^54 = 2^-1020 > 2^-1022`). Az `x <= 0` **továbbra is
   kivételt dob** — ez a modul meglévő szerződése, nem változtattuk.
4. **`Pow`** — `y == 0 → 1,0` rövidzár. Véges `x`-re numerikusan változatlan
   (`Exp(0 · Ln(x)) = Exp(0) = 1,0`), de `x = +∞` mellett feloldja a
   `0 · ∞ = NaN` csapdát. A `x == 0` vizsgálat **szándékosan előbb** van, így
   a `pow(0, 0) = 0,0` ND-27-es konvenció bitre változatlan.

**Alternatíva, amit ELVETETTÜNK: fokozatos alulcsordulás** (subnormális
eredmény a `[-745, -708,4]` sávban, ahogy a `Math.Exp` teszi). A bit-eltolás
ezt nem tudja előállítani — külön, mantissza-eltolós utat igényelne, aminek a
kerekítése platformfüggetlen bizonyítást kérne. A `0,0`-ra vágás
**dokumentált konvenció**; a fizikai modellekben egy `1e-309`-es és egy `0`-s
csillapító között nincs érdemi különbség.

**Az ND-137 helyi korlátja MARAD.** Redundáns, de eltávolítása bit-változás
lenne a `(-708,396, -700)` sávban (ott az `Exp` valós, apró értéket ad, a
helyi rövidzár viszont az egyensúlyi hányadot). Nem elérhető a modellben, de
nem is szükséges hozzányúlni.

**NEM SEED-TÖRŐ — bizonyítva.** Minden megváltozott érték a korábban szemetes
tartományban van; a védett sáv bitre változatlan.

- **3000 KAT-vektor** (`sinCos`, `pow`, `atan`, `asin`/`acos`, `atan2`,
  `tanh`) újraszámolva a javított orákulummal: **0 eltérés**. A
  `deterministic_math_vectors.json` újragenerálva **bitre azonos**
  (`git diff` üres).
- A `pow`-vektorok belső `Exp`-argumentuma `[-34,14; 33,15]`, a `tanh`-é
  `>= -40` — messze a biztonságos `[-708,396; 709,783]` sávon belül.
- **Világ-hash** (`worldgen hash --seed A7C944210000 --plates 20 --level 6`),
  `git stash`-szel a változás előtti kód ellen mérve:

  | `--time` | előtte | utána |
  |---|---|---|
  | 0 | `2b98af9a…6213738b` | `2b98af9a…6213738b` — azonos |
  | 400 | `14dc8ad2…59a7ea07` | `14dc8ad2…59a7ea07` — azonos |
  | 3000 | `823e8ba0…cdcfe7ab` | `823e8ba0…cdcfe7ab` — azonos |

  A `WorldGeneratorVersion` **marad `"5"`**.

**Tesztek.** Új `DeterministicMathEdgeCaseTests` osztály: alul-/túlcsordulás
`Theory`-kkal, **monotonitás és nemnegativitás a teljes alulcsordulási
átmeneten** (240 minta — ez fogja meg a régi hiba előjelváltását), NaN-
terjedés, subnormális `Ln` plauzibilitás a `Math.Log` ellen, `Exp(Ln(x))`
körbejárás, `Pow`-élesetek, és ismételhetőség (I2).


### ND-151 — M13 2-4. fázis: a GPU-vezérelt geometria elvetése, per-pixel mikro-részlet helyette (A15, ELFOGADVA, implementálva)

**Kiindulás (2026-09-27, A15).** A `docs/backlog.md` 297. sora a
"fotorealisztikus" négyfázisú terv maradékát így írta le: **2. fázis** — a
meglévő GPU compute pipeline kiterjesztése a teljes kiértékelésre; **3. fázis**
— GPU-vezérelt mesh; **4. fázis** — procedurális mikro-részlet textúra. Az
1. fázis (folytonos árnyalás) kész és megerősített.

**A 2. és 3. fázis TÁRGYTALAN — az ND-128 óta.** A terv arra a GPU compute
pipeline-ra épült, amit az ND-128 MÉRÉS ALAPJÁN törölt: a
`TileClassification.compute` a Core-eleváció HLSL-be ÚJRAÍRT mása volt, és a
tile-ok ~22%-át a tengerszint másik oldalára tette; ráadásul lassabb is volt
(~50 s/újraépítés). A `Gpu/` mappa megszűnt, a "kiterjesztendő pipeline"
fizikailag nincs meg.

A hibaosztály neve pontosan megfogalmazható, és ez dönti el a kérdést:
**a GPU-n számolt érték a MODELL BEMENETE lett.** Egy GPU-vezérelt mesh
(3. fázis) ugyanezt követelné meg — a csúcspont-eltolásnak eleváció kell, azt
pedig a GPU-n kellene számolni, bitpontosan a Core-ral megegyezően. Az I1
(bitre azonos világ minden platformon) és a HLSL lebegőpontos szabadsága
(átrendezés, `mad`-összevonás, gyors matematikai helyettesítések) egymást
kizárják. A 2-3. fázist tehát **nem halasztjuk, hanem ELVETJÜK**; ha valaha
GPU-geometria kell, az új ND-t igényel, és a bemenetnek akkor is a
CPU-elevációból kell jönnie (displacement-textúra feltöltése, nem
újraszámolás).

**Amit a 3. fázis VALÓJÁBAN akart, azt a 4. fázis megadja.** A cél nem a
"GPU-n futó geometria" volt, hanem a **mesh-felbontás alatti felszíni
részlet**. Ez per-pixel árnyalással elérhető: a fragment shader perturbálja a
normált és modulálja az albedót. Ugyanaz a látvány, ÚJ GEOMETRIA NÉLKÜL, és
— a döntő eltérés — a GPU-n számolt érték KIZÁRÓLAG kimenet.

#### A megvalósítás

| Réteg | Fájl | Szerep |
|---|---|---|
| Core | `src/WorldGen.Core/Terrain/SurfaceMicroDetail.cs` | Konstansok + a válasz-leképezés (amplitúdó, albedó-szórás, frekvencia-szorzó) + seedből a zaj-fázis |
| Viewer (motorfüggetlen) | `Assets/Scripts/Viewer/Lod/MicroDetailBand.cs` | A nézetfüggő sávválasztás (a Core nem tud a kameráról) |
| Viewer (Unity) | `Assets/Scripts/Viewer/PlanetGridMesh.MicroDetail.cs` | Uniform-átadás frame-enként, overlay-kapu, anyag-kapu |
| Shader | `Assets/Shaders/VertexColorUnlit.shader` | A per-pixel fBm és a normál/albedó módosítás |

**A frekvencia-létra SZÁRMAZTATOTT, nem választott.**

- *Alsó vég:* `BaseFrequency = SecondaryNoiseFrequency * 2^SecondaryNoiseOctaves`
  = **4,0744 ciklus/radián** (~1564 km hullámhossz) — pontosan a következő
  oktáv a modell saját másodlagos relief-zaja után. A mikro-részlet ott
  folytatja a létrát, ahol a világmodell abbahagyta.
- *Felső vég:* `MaxFrequency = 2^17` = **131 072 ciklus/radián** (~49 m
  Föld-méreten). A shader a bolygó-lokál egységvektorból számolja a
  zaj-koordinátát; 32 bites float relatív eps `2^-23`, és `(1/64) / 2^-23 = 2^17`
  az a frekvencia, aminél a rács-cella hibája még 1/64 cella alatt marad.
  Ennél finomabb részlet a geometria pozíció-ábrázolását is kérné (ND-19 /
  A12/2 test-keretes renderelés) — ez a korlát tehát nem önkényes, hanem
  egy MÁSIK nyitott döntésre mutat.

**A sávváltás folytonossága (a kritikus rész).** ~15 oktávot egy fragment
shader nem számol ki, ezért négy oktávot használunk, és a létrát toljuk a
pixel-lábnyom szerint. Ha a sáv ugrálna, zoomolás közben az EGÉSZ felszíni
textúra pattogna. A megoldás a létra két végének ellentétes átúsztatása:
`w = (1-frac, 1, 1, frac) * persistence^k`, súlyösszeggel normálva.
BIZONYÍTOTT folytonosság: `frac = 1`-nél az n. sáv normált súlyai a
(2B, 4B, 8B) frekvenciákon `(0,571; 0,286; 0,143)` — pontosan ugyanazok, mint
az (n+1). sáv `frac = 0`-nál. Külön C#-teszt fogja
(`BandCrossing_IsContinuous_TheLadderShiftChangesNothing`).

**Mi jön a világmodellből (I3).** Nincs kézzel festett textúra:

- **lejtő** = a MÁR RENDERELT geometria normálja és a radiális irány szöge;
- **magasság** = a MÁR RENDERELT rádiusz, a `WorldElevationFromDisplacedRadius`
  inverzével (ugyanaz az elv, mint az ND-129/ND-149 parti gyűrűjénél: **nulla új
  eleváció-kiértékelés**);
- **tengerszint** = a kalibrált ND-38 szint;
- **fázis** = a világ seedje (`RandomDomain.Decorative` / új
  `RandomProperty.MicroDetailPhase = 43`, ugyanaz a besorolás, mint a
  csillagmezőé) — ugyanaz a seed, ugyanaz a mikro-részlet.

**Miért nem érinti az I1-et.** A mikro-részlet egyetlen mezőbe,
gyorsítótárba, hash-be vagy mentésbe sem folyik vissza. BIZONYÍTVA: a
`worldgen hash --seed A7C944210000 --plates 20 --level 6` mindhárom
időpontban **bitre az ND-150 óta dokumentált érték**: `t=0`
`2b98af9a…6213738b`, `t=400` `14dc8ad2…59a7ea07`, `t=3000`
`823e8ba0…cdcfe7ab`. A `WorldGeneratorVersion` marad `"5"`. **Nem seed-törő.**

**A HLSL-másolat kérdése — külön kezelve.** A shader a leképezés ALAKJÁT
(lerp + kvintikus smoothstep) tükrözi, a SZÁMOKAT viszont uniformként a
Core-ból kapja. Így az ND-128 hibaosztálya (két, egymástól elcsúszó
számkészlet) strukturálisan nem alakulhat ki; és mivel az érték csak kimenet,
egy esetleges eltérés VIZUÁLIS hiba lenne, soha nem determinizmus-sérülés.

#### Mérés (élő Play, 2026-09-27, 1600x900)

| Mit | Mikro-részlet KI | Mikro-részlet BE | Arány |
|---|---|---|---|
| Helyi kontraszt (átlagos abs. Laplace a megvilágított pixeleken), bolygó-nézet | 7,18 | 10,27 | **1,43x** |
| Ugyanaz, közeli nézet (115 egység, ~1,15 R) | 3,20 | 18,46 | **5,76x** |
| GPU-idő (`frameTiming.gpuFrameTimeMs`, néhány minta) | 3,66-5,79 ms | 3,09-3,16 ms | nincs mérhető különbség — a képkockák közti szórás NAGYOBB |
| CPU-költség | — | frame-enként néhány uniform | — |
| Memória | — | **nulla** (se mesh-csatorna, se textúra) | — |

**Menet közben javítva két, méréssel talált hiba.**

1. **A pixelek/legfinomabb-oktáv arány 3,0-val homokpapírt adott.** Az első
   élő menetben a felszín egyenletes, sűrű SZEMCSÉT kapott — zajnak látszott,
   nem domborzatnak: a legfinomabb oktáv éppen a Nyquist-határra esett. **8,0**-ra
   emelve a legfinomabb oktáv 8 pixel, az alap-oktáv 64 pixel — ez már
   felismerhető részlet-domborzat.
2. **Súroló nézetnél villogás a limbnél.** A sáv a LEGKÖZELEBBI felszínpont
   lábnyomából számol; a limb felé ugyanaz a felszín `1/cos`-szor akkora
   szöget fed egy pixelen, tehát ott a részlet a Nyquist alá csúszott. A
   halványítás (`smoothstep` a `N·V`-n) UGYANABBÓL a lábnyom-kritériumból
   következik, ami a sávot is adja — nem külön kozmetika.

**Kapuk.** Az overlay-ek (tektonika, szél, csapadék, hő) SZÁNDÉKOSAN kizárják
a mikro-részletet: ott a szín egy MÉRT mennyiség palettája (I4), amit egy
részlet-moduláció félreolvashatóvá tenne. A vízfelszín anyag-szintű kapuval
(`_MicroDetailEnable = 0`) marad sima. Inspector-kapcsolók: `surfaceMicroDetail`
(A/B) és `microDetailStrength` (0-2, élő hangoláshoz).

**Kikapcsolva a kép BITRE az ND-151 előtti** — a shader a kapunál egzaktul
`1.0` albedó-szorzót ad vissza és a normált nem módosítja.

**Tesztek.** 20 új Core-teszt (`SurfaceMicroDetailTests`: tisztaság,
paraméter-hatás, partvonal-simaság, monotonitás, élesetek `double.MaxValue`-ig,
a származtatott konstansok visszaellenőrzése) és 13 új viewer-teszt
(`MicroDetailBandTests`: sávfolytonosság, láthatóság-monotonitás,
pontossági plafon, érvénytelen bemenetek). Teljes futás: **1761/1761 zöld**
(Core 698, Viewer 587, App 452, CLI 24), Unity `compilationFailed: false`,
0 konzol-hiba, shader `msgCount=0`.

**NYITOTT: a vizuális átvétel.** A mérés azt mutatja, hogy a részlet ott van
és a modellt követi; hogy a mostani erősség (1,0) SZÉP-e, az felhasználói
ítélet — ld. B16.


### ND-152 — Az ND-05 hibrid maradék két tagja: biome-klaszter + domborzati törés az összevonás élsúlyában (A10, ELFOGADVA: (A))

**2026-09-27, A10.** Az ND-127 a hibrid ELSŐ tagját (vízgyűjtő) tette rendbe:
a torkolat szerint kulcsolt nyers vízgyűjtőket összefüggő komponensekre
bontotta (cellák), szomszédsági gráfot épített rájuk, és agglomeratívan
összevonta cél-méretig — 100% szárazföld-lefedettség, 0 szétesett régió,
10–11 régió a legnagyobb landmasson. A záró mondata kimondta, hogy az ND-05
maradéka „ugyanezen a cella-gráfon csak más összevonási költségfüggvény
lenne". Ez a döntés azt a költségfüggvényt írja le.

**Mi hiányzott.** Az ND-127 partner-választása kizárólag GEOMETRIAI: a
legkisebb cél alatti cella abba a szomszédjába olvad, amelyikkel a
LEGHOSSZABB közös határt osztja. Se a biome, se a domborzat nem szól bele,
így két, a specifikációban nevesített természetes határ nem jelenik meg a
régió-határokon:

1. **biome-klaszter** — a tundra és a sivatag egy régióba kerülhet, ha a
   vízválasztó éppen úgy esik;
2. **domborzati törés** — a hegygerinc / peremlépcső mint határ nem kap
   semmilyen előnyt, holott a valódi régió-határok nagy része pontosan ilyen.

**Opciók.**

- **(A) Súlyozott élhossz ugyanazon a cella-gráfon.** A közös határ hossza
  helyett a határ SÚLYOZOTT hossza dönt: minden érintkező tile-él alapsúlyt
  kap, a biome-egyezés bónuszt ad, a domborzati törés levon. Minden más
  (a cél alatti szomszéd előnye, a méret- és `TileId`-döntetlenek, a
  kanonikus kimeneti sorrend) VÁLTOZATLAN, tehát az ND-127 mindhárom
  garanciája (partíció, összefüggőség, kanonikus sorrend) konstrukció
  szerint megmarad. Tiszta egész aritmetika.
- **(B) Három külön klaszterezés összefésülése** — a spec szó szerinti
  olvasata (vízgyűjtő ∪ biome-klaszter ∪ domborzati törés mint három
  független szegmentálás, utólag egyesítve). Ez az ND-127 (C) opciója:
  három klaszterezés saját súlyozási paraméterekkel, és az egyesítésük NEM
  garantálja sem a partíciót, sem az összefüggőséget — a két legfontosabb
  megszerzett garanciát kellene újra bizonyítani.
- **(C) Nem csináljuk meg.** A mai régiók összefüggőek és lefedik a
  szárazföldet; a határaik „csak" nem követik a biome-/domborzat-váltást.

**ELFOGADVA: (A).** A (B) a garanciákat kockáztatja azért, amit az (A) egy
élsúly-képlettel megkap; a (C) az ND-05-öt tartósan félkészen hagyná.

**A KÉPLET.** Minden `t → nb` érintkező tile-élre (`t` az `i`, `nb` a `j`
cellában):

```
w(t, nb) = RegionMergeBaseEdgeWeight                                   (= 4)
         + (biome[t] == biome[nb] ? RegionMergeSameBiomeBonus : 0)     (= 3)
         - (|m(t) - m(nb)| >= breakThreshold
              ? RegionMergeTerrainBreakPenalty : 0)                    (= 3)

m(t) = (long)Math.Floor(elevationM[t] + 0.5)
```

`borders[i][j]` ennek az ÖSSZEGE az összes érintkező tile-élen (a mai
darabszám helyett). Egy tile-él súlya így 1…7: biome-egyező, töréstelen él
7, biome-váltó törés-él 1 — hétszeres különbség, de mindig POZITÍV, tehát a
„van-e szomszédja" feltétel (`borders[i].Count == 0`) jelentése nem
változik, és a súly az összevonásnál továbbra is egyszerűen összegződik.
(A pozitivitás nem kozmetika: nulla súlyú él eltüntetné a szomszédságot, és
egy cella cél alatti méretben beragadna — a „méretpadló" teszt bukna.)

**Determinizmus (I1/I2).** Tiszta egész aritmetika; a lebegőpont egyetlen
helyen jelenik meg, a magasság egész méterre kerekítésében, ami
`Math.Floor(e + 0.5)` — összeadás + korrekt kerekítésű floor, tehát
IEEE-754 szerint bitpontos (ld. a CLAUDE.md lebegőpont-táblázatát). Nincs
transzcendens függvény, nincs random, nincs szótár-bejárási sorrendtől való
függés. Az élsúly SZIMMETRIKUS a két tile-ban, tehát az ND-127 szimmetria-
feltevése (az élt a saját oldaláról számoljuk) érvényben marad.

**A törés-küszöb RELATÍV, nem fix méter.** `breakThreshold` = a
`|m(t) - m(nb)|` eloszlás **p75**-e, a gráf ÖSSZES cella-közi érintkező
tile-élén mérve. Ok: fix méter-küszöb szintfüggő lenne (level 5-ön a tile
négyszer nagyobb területű, tehát nagyobb a tipikus él-menti magasságugrás),
az ND-127 kifejezett célja viszont az volt, hogy a régiók SZÁMA
szintfüggetlen legyen. A p75-tel definíció szerint a legmeredekebb negyed
számít törésnek, szinttől és bolygótól függetlenül (mérve: level 5-ön
és level 6-on is a cella-közi élek 5–45%-os sávján belül — a
`TerrainBreakThresholdIsRelativeAtEveryLevel` teszt ezt rögzíti; a
`0xA7C944210000` világon level 6-on a küszöb **230 m**, 13 318 cella-közi
élen). A percentilis-konvenció a projektben már meglévő
`idx = (int)(q * count)`, clampelve (ugyanaz, mint a
`BiomeClassification.Percentile`) — nincs interpoláció, tehát nincs
kerekítési kétértelműség.

**A súlyok megválasztása.** `4 / +3 / −3`: a bónusz és a levonás azonos
nagyságú (egyik tag sem dominál a másikon), és mindkettő elég nagy ahhoz,
hogy egy él súlyát megfordítsa a szomszédjáéhoz képest, de nem annyi, hogy
a határhossz-jelet elnyomja — egy 2 tile hosszú, biome-egyező, töréstelen
határ (14) még mindig veszít egy 4 tile hosszú, alapsúlyú határral (16)
szemben. A geometria tehát továbbra is a vezető jel, a két új tag a
döntetlenek és a közeli esetek eldöntője. (Az
`EdgeWeightStaysPositiveInTheWorstCase` teszt mindhárom viszonyt rögzíti.)

**MÉRT eredmény** (seed `0xA7C944210000`, 20 lemez, víz 0,65, level 6, cél
258 tile; a mutatók a LEGNAGYOBB landmassra, 4171 tile). A
„régióhatár-él" a két különböző régióba eső, szomszédos szárazföld-tile-pár;
a „törésen ül" azt jelenti, hogy a magasságkülönbsége eléri a p75 küszöböt —
vaktában húzott határ várható értéke tehát ~25%:

| | ND-127 (csak geometria) | ND-152 (hibrid) |
|---|---|---|
| régió a legnagyobb landmasson | 11 | 9 |
| régióhatár-él | 410 | 296 |
| ebből domborzati TÖRÉSEN | **21,0%** | **43,2%** |
| ebből biome-VÁLTÁSON | **25,4%** | **41,2%** |
| átmérő/√terület (átlag) | 2,42 | **2,39** |
| átmérő/√terület (min–max) | 1,91–3,48 | 1,72–2,94 |

A geometriai út tehát a törésekhez képest NEM javít (21% < a 25%-os
véletlen szint), a hibrid megduplázza; ugyanez a biome-váltásra 25,4% →
41,2%. A kompaktság közben nem romlik — sőt a legnyúlványosabb régió
mutatója 3,48-ról 2,94-re esik.

**MÉRET-ILLESZTETT ellenőrzés** (ugyanaz a világ, több cél-mérettel — a
régiószám és az átlagos méret azonos, tehát a különbség nem a méretből jön):

| cél | régió (geo → hibrid) | törésen % | biome-váltáson % | domináns-biome tisztaság |
|---|---|---|---|---|
| 180 | 14 → 15 | 27,7 → 38,6 | 22,8 → 37,2 | 0,373 → 0,389 |
| 210 | 13 → 14 | 27,9 → 37,5 | 20,5 → 35,6 | 0,365 → 0,374 |
| 258 | 11 → 9 | 21,0 → 43,2 | 25,4 → 41,2 | 0,356 → 0,350 |
| 300 | 8 → 8 | 21,3 → 39,5 | 18,1 → 37,2 | 0,342 → 0,334 |
| 360 | 7 → 7 | 21,7 → 35,8 | 18,9 → 35,8 | 0,339 → 0,340 |
| 430 | 7 → 7 | 17,6 → 35,7 | 20,0 → 30,2 | 0,321 → 0,338 |

**Amit ez a mérés NEM állít.** A régión BELÜLI domináns-biome tisztaság
gyakorlatilag nem mozdul (±0,01, hol így, hol úgy). Ez nem hiba, hanem a
lépték: egy 250–700 tile-os régió a mi szintjeinken több klímazónát fog át,
akármi is a határa. A biome-klaszter tag NEM homogén biome-régiókat ígér,
hanem azt, hogy a HATÁR ott legyen, ahol a biome tényleg váltik — és ezt a
25,4% → 41,2% méri.

**Visszamenős kompatibilitás / hatókör.** A két új tag a `biomeOf` ÉS az
`elevationM` szótár együttes átadásához kötött. Ha a hívó bármelyiket
elhagyja (`null`), minden él az alapsúlyt kapja, ami a darabszámmal
ARÁNYOS, tehát a partner-választás bitre az ND-127-beli. Ez nem kényelmi
visszaút, hanem a hibrid tagjainak szétválaszthatósága: a fenti táblázat
pontosan ezt a két futást állítja szembe, és a
`PartialHybridInputFallsBackToTheGeometricPath` teszt rögzíti, hogy „fél
hibrid" nem létezik. A PRODUKCIÓS hívók (viewer panel/navigáció,
`OrdinalCalibration`) mind a hibridet hívják.

**Verziózás.** NEM seed-törő az ND-108 értelmében: a domborzat, a
hidrológia, a biome-osztályozás és a `FindWatershedRegions` egyetlen bitje
sem változik; az összevonás továbbra is tiszta, utólagos prezentációs réteg
a már kiszámolt vízgyűjtők fölött. A régiónevek viszont MEGVÁLTOZNAK (más
tile-halmaz → más domináns biome/morfológia), és — mint az ND-127-nél — a
`SoilFertilityThresholds` kalibrációs populációja is más lesz, mert az
`OrdinalCalibration` ugyanezt az összevonást mintázza. **Ez ebben a
menetben megtörtént**: az `OrdinalCalibration` is a hibrid összevonást
mintázza (a biome-mezőt a regolit-lánc MÁR kiszámolt hőmérsékletéből és
csapadékából építi, nem proxyból), és a `SoilFertilityThresholds` v3-ra
cserélve — 500 világ, 23 555 régió-minta (v2: 23 492), p20/p80
0,157734/0,230240 → **0,164778/0,228259**. Ellenőrzés: a
CoastalComplexity vágópontjai ugyanebben a futásban BITRE ugyanazok
maradtak (14 895 minta), tehát tényleg csak a talaj-populáció mozdult.

**LEZÁRVA (A), 2026-09-27** — implementálva:
`FeatureSegmentation.MergeWatershedsIntoRegions` opcionális `biomeOf` /
`elevationM` paraméterekkel + `RegionMergeBaseEdgeWeight`,
`RegionMergeSameBiomeBonus`, `RegionMergeTerrainBreakPenalty`,
`RegionMergeTerrainBreakPercentile`; Python-referencia
(`features_ref.py: merge_watersheds_into_regions`, ugyanezekkel a
konstansokkal) és BITPONTOS vektor-teszt
(`MatchesPythonReferenceMergedRegionsExactly` — a vektorok innentől a
hibridet írják le). **Ezzel az ND-05 lezárul**: mindhárom tagja
(vízgyűjtő, biome-klaszter, domborzati törés) benne van.

**NYITOTT: a vizuális átvétel.** Hogy a 9–11 régió határai a képen
tetszetősek-e, az felhasználói ítélet — ld. B5.

### ND-153 — Pálya menti (éves) kameramód: a pálya-keretben rendereljük, a napi forgás befagyasztva (A11, ELFOGADVA)

**2026-09-27, A11.** Az ND-62 a kameramód-kapcsolót és a tengelyforgásos
módot lezárta, a harmadik értéket (`OrbitalFollow`) viszont szándékosan
no-opként hagyta — a saját tooltipje vallotta be, hogy „egyelőre Szabad
kameraként viselkedik". A backlog három nyitott kérdést sorolt hozzá:
(1) befagyasztjuk-e a kameracélpontot, (2) fusson-e közben a napi ciklus,
(3) hogyan kalibráljuk a nem valós lépték miatt (ND-19). Ez a döntés
mindháromra válaszol.

**Mit kell látni.** A mód célja az ÉVES jel: a terminátor észak–déli
vándorlása, a sarki nappal/éjszaka megjelenése és eltűnése, a Nap-korong
körbefordulása a bolygó körül. Ez a három dolog kizárólag a pálya-szögtől
és a tengelydőléstől függ, a bolygó napi forgásától NEM.

**(3) A lépték-kérdés nem merül fel — ezért nem blokkol az ND-19.** A
kamera a bolygó közepe körül orbitál (`PlanetOrbitCamera`, a `target.position`
körül), a bolygó a világ origójában áll, a Nap-korong pedig egy fix
`sunVisualDistance` sugarú körön mozog. Vagyis a pálya menti követést a
bolygóval EGYÜTT MOZGÓ, nem forgó (inerciális) pálya-keretben rendereljük —
nem a csillag-központú keretben. Kör pálya mellett (ND-26 hatókör) a
csillag-távolság konstans, tehát ebben a keretben a látvány EXAKT: a bolygó
valós pálya menti helyzetét egyetlen szög (`OrbitalMechanics.OrbitalAngle`)
hordozza, és pont az látszik is. Valós léptékű pálya-koordinátára
(1 AU ≈ 1,5·10^11 m float32-ben) így nincs szükség — az ND-19/A12 geometria-
eltolása ettől a módtól függetlenül marad az A11 közeli zoomjának a kérdése.

**(1) A kameracélpontot NEM fagyasztjuk be — nincs is mit.** A
`PlanetOrbitCamera` sosem olvassa a `target.rotation`-t, csak a
`target.position`-t, ami a világ origója. Az egér tehát ebben a módban is
teljes körűen a felhasználónál van: a mód a VILÁGOT teszi inerciálissá,
nem a kamerát kötözi meg. Ez azért is a helyes választás, mert egy a
Naphoz rögzített kameraorientáció pont azt tüntetné el, amit meg akarunk
mutatni (a terminátor elmozdulását a képen).

**(2) A napi ciklus NEM fut — a spin befagy, a modellidő nem.** Ez a
döntés valódi tartalma. Ha egy év 60 másodperc (alapérték), akkor 60 fps-en
365,25 forgás jut 3600 képkockára: ~36°/képkocka, azaz a napi forgás
ALIAS-OL (kerékszprich-effektus), nem „gyors", hanem félrevezető. Ezért
`OrbitalFollow`-ban a mesh a módba lépés pillanatában érvényes spin-szögen
áll meg (nincs ugrás a módváltáskor), a dőlés viszont ÉL — így a tengely
láthatóan a pálya-síkhoz képest ferdén áll, és a terminátor emiatt vándorol.

Fontos, hogy mit NEM állítunk ezzel: a modellidő (`currentTimeDays`)
TOVÁBBFUT, tehát a panel, a hőmodell és minden szám a valódi órát olvassa;
csak a KÉP spin-fázisa konstans. Emiatt a szub-napponti HOSSZÚSÁG ebben a
módban nem a modell szerinti — a szub-napponti SZÉLESSÉG viszont igen, mert
az csak a pálya-szögtől és a dőléstől függ. A panel ezért kizárólag a
szélességet írja ki (I4: minden kiírt szám levezetett és igaz), a hosszúságot
nem. A befagyasztás kikapcsolható (`orbitalFollowFreezeSpin`), hogy az
alias-jelenség maga is megvizsgálható legyen — de nem az alapérték.

**Időlépték és a hitch-korlát.** Az éves rátát külön mező adja
(`orbitalFollowSecondsPerYear`, alapérték 60 s/év), a napi ciklus
`daysPerSecond`-ját nem írja át (módváltáskor a napi mód ott folytatódik,
ahol tartott). Egy képkockára eső előrehaladás az év 2%-ára korlátozva
(`OrbitalFollowMath.MaxYearFractionPerStep`): egy fordítási/GC-akadás így
nem ugraszt évszakot, és a hőmodell cél-tickje sem lő el.

**A hőmodell nem tart lépést, és ez nem hiba.** A pillanatnyi hőmodell
(ND-104) képkockánként `thermalMaxTicksPerJob` tickre van korlátozva; éves
ütemben a cél-tick ennél gyorsabban nő. A hőoverlay tehát ebben a módban
látványosan LE MARAD (a saját státuszsora ezt kiírja) — szándékosan nem
gyorsítottuk fel, mert az a hőmodell időléptékét hamisítaná meg. A két
funkciót együtt nem érdemes használni.

**A `Free` mód változatlan.** A módelágazás `SunController.ApplySunDirection`-ben
egyetlen ponton nő eggyel: a pálya-keretes renderelés ága mostantól
`AxialRotation` ÉS `OrbitalFollow` esetén is fut, a kettő KIZÁRÓLAG a
felhasznált spin-szögben tér el (élő vs. befagyasztott). Ezzel az ND-62
ott dokumentált, élesben már ellenőrzött dőlés-/spin-előjel levezetése
újrahasznosul, nem íródik újra.

**Verziózás:** nem seed-törő — tisztán Viewer-oldali render-útvonal, a Core
egyetlen új függvényt sem kapott (a már publikus `SunDirectionOrbitalFrame`
/ `SubsolarPoint` hívódik). Az új, motorfüggetlen `OrbitalFollowMath`
(`unity/.../Viewer/OrbitalFollowMath.cs`) az `OrbitSurfaceMath`/`ScaleBarMath`
mintájára Unity nélkül is tesztelhető — 24 teszt.

**Élő ellenőrzés kritériuma:** `OrbitalFollow`-ban a terminátor egy 60 s-os
kör alatt láthatóan észak-délre vándorol (a sarkvidéken teljes nappal →
teljes éjszaka), a felszín-rajzolat viszont NEM pörög; a Nap-korong egy kört
tesz a bolygó körül; a panel szubszoláris szélessége ±23,44° között
oszcillál, a csillagmező fixen áll.

### ND-154 — M13 térfogati felhő: saját, gömbi raymarch (A16, ELFOGADVA, implementálva)

**Kiindulás (2026-09-27, A16).** Az ND-21 a HDRP volumetrikus felhőjét
**ELUTASÍTOTTA** (négy mért blokkoló: 100 m-es rétegvastagság-clamp, ami a mi
léptékünkben 7420 km vastag héjat ír elő; a `ComputeNormalizationFactor`
bedrótozott Föld-sugara; a felhőtérképes út féltekére vágása a shaderben; és a
`Simple` preset shaderben KONSTANS lefedettsége, ami I3-sértés). Az A16
volumetrikus tétele ezzel saját, gömbi raymarch lett.

Ami eddig volt: az M6-os felhő-MVP egyetlen **LAPOS quad-héj**, a csapadék a
vertex-alfában. A limbnél nincs vastagsága, nem árnyékol, és egyetlen
Lambert-tag világítja meg.

#### A megvalósítás

| Réteg | Fájl | Szerep |
|---|---|---|
| Core | `src/WorldGen.Core/Climate/CloudVolume.cs` | A héj fizikája: alj (LCL), vastagság, optika, sub-grid szétbontás, többszörös szórás |
| Core | `src/WorldGen.Core/Terrain/SurfaceSkyOpenness.cs` | Égbolt-nyitottság (ND-155) — ugyanabba az atlaszba megy |
| Viewer (motorfüggetlen) | `Assets/Scripts/Viewer/Lod/CloudSkyAtlas.cs` | A négycsatornás atlasz + a forrás→atlasz bilineáris felskálázás |
| Viewer (motorfüggetlen) | `Assets/Scripts/Viewer/Lod/CloudRaymarchPlan.cs` | Nézetfüggő lépésszám, burkoló-nagyítás |
| Viewer (Unity) | `Assets/Scripts/Viewer/PlanetGridMesh.CloudVolume.cs` | Háttérszálas atlasz-munka, héj-mesh, uniformok |
| Shader | `Assets/Shaders/CloudVolume.shader` | A raymarch |
| Shader | `Assets/Shaders/PlanetCubeAtlas.cginc` | **ÚJ, KÖZÖS** atlasz-UV — a terep-shader és a felho-shader ugyanazt olvassa |

**Az adatút — minden a MÁR KISZÁMÍTOTT mezőkből (I3).** A háttérmunka a
`_lastPrecipField`-ből dolgozik: **nulla új csapadék- és eleváció-kiértékelés**.
A lefedettség a domain-relatív (óceán/szárazföld KÜLÖN) percentilis-alakítás
eredménye — ugyanaz a képlet, amit a lapos MVP használt, most a Core-ban
(`CloudVolume.ShapeCoverage`). Négy csatorna egyetlen RGBA32 kocka-atlaszban
(396×66 texel = 104 KB, **nulla dead channel**): R = lefedettség, G = felhőalap,
B = vastagság, A = égbolt-nyitottság. Az atlasz GEOMETRIÁJA nem új: pontosan a
hő-overlay (ND-104) hat-lapos, gutteres atlasza, ugyanazzal a texel→cella
leképezéssel.

**A felhőalap SZÁRMAZTATOTT, nem csúszka.** Lawrence (2005, BAMS 86(2)) két
közelítéséből: `z_LCL ≈ 125·(T − T_d)` és `(T − T_d) ≈ (100 − RH)/5`, tehát
`z_LCL ≈ 25·(100 − RH)` — a hőmérséklet-tag kiesik, ami azért fontos, mert a
modell nem hordoz harmatpontot. Az alj a TALAJ fölött képződik, ezért a
domborzattal együtt emelkedik: a felhőtakaró **ráborul a hegyláncra**, nem
átvágja. A vastagság rétegfelhő-alap + konvektív tag a lefedettség
KVADRÁTJÁVAL + orografikus tag a terepmagasságból.

#### Három MÉRT hiba, ami a fejlesztés közben derült ki

1. **A pass egyáltalán nem rajzolódott.** Az első változat `Cull Front` +
   `ZTest Always` volt (hogy egy sugárra egy fragment essen). Az így megmaradó
   HÁTSÓ héjlapok a bolygó MÖGÖTT vannak, és a HDRP mélységtesztje eldobta
   őket — a beépített diagnosztika 1-es módja (a burkoló tömör kitöltése)
   teljesen üres képet adott. **Javítás:** `Cull Off` + `ZTest LEqual` +
   GEOMETRIAI lapválasztás (`dot(p − kamera, p) < 0` a közeli oldal), ami
   ráadásul ingyen ad mélység-takarást felszín-közeli nézetben.
2. **A menet átlépett a felhő fölött.** A héj a LEGNAGYOBB lehetséges felhőt
   fogja be (24 km), a tipikus dekk viszont 500–2000 m. 12 lépéssel a menet
   gyakorlatilag nem mintázta meg a dekket. **Javítás:** a szakasz
   középpontjának irányában kiolvassuk a HELYI aljat/vastagságot, és a menetet
   a két helyi gömbhéj közé szorítjuk (+25% padding). Egy extra
   textúraolvasás, és minden lépés a felhőbe esik.
3. **Szögletes felhőárnyék-foltok.** A csapadék-mező a referencia-szinten él
   (level 5, 364 km-es cella), az atlasz level 6-on; a legközelebbi cella
   átvétele a forrás cellaméretén hagyott hard éleket, amit a GPU bilineáris
   szűrése nem tud elsimítani. **Javítás:** `CloudSkyAtlas.UpsampleTable` —
   bilineáris felskálázás a forrás-szintről, a laphatárokat a `TileNeighbors`
   kezeli. Ez RESAMPLING, nem kiegészítés: a súlyok nemnegatívak és 1-re
   összegződnek, a csúcsértékek nem nőnek.

#### Három modell-hiba, amit a kép mutatott meg

**(a) Egyenletes szürke fátyol.** Az első változat a lefedettséget
MULTIPLIKATÍVAN modulálta a részlet-zajjal. Ez fizikailag rossz: egy 364 km-es
cella 0,1-es lefedettsége azt jelenti, hogy a terület **10%-án VAN felhő és
90%-án NINCS** — nem azt, hogy az egészet egy tizednyi sűrűségű fátyol fedi.
A mért következmény pontosan ez lett: derült területek nélküli szürke fátyol.

A javítás a **sub-grid szétbontás**: ott van felhő, ahol a [0,1]-re normált
részlet-zaj a küszöb alatt van. A küszöb ZÁRT ALAKBAN levezethető, nem hangolt.
Legyen `f(n) = clamp((T − n)/w, 0, 1)` egyenletes `n`-re:

| tartomány | várható érték | a küszöb |
|---|---|---|
| `T < w` | `T²/(2w)` | `T = √(2wc)` |
| `w ≤ T ≤ 1` | `T − w/2` | `T = c + w/2` |
| `T > 1` | `(T−w) + (w² − (T−1)²)/(2w)` | `T = 1 + w − √(2w(1−c))` |

A három ág `c = w/2`-nél és `c = 1 − w/2`-nél folytonosan illeszkedik, a két
végpont EGZAKT: `T(0) = 0` (nulla lefedettség → egzaktul üres, **I3-garancia**)
és `T(1) = 1 + w` (teljes lefedettség → mindenütt felhő). Csak `√` kell hozzá,
ami IEEE-754 szerint korrekt kerekítésű. **A zajra vett várható érték a
modellezett cella-átlag**, tehát a modell mennyisége nem vész el és nem is nő —
a szétbontás csak ott ad információt, ahol a modellnek nincs: a cella
BELSEJÉBEN. Teszt bizonyítja (`SubGridCoverage_PreservesTheModelledCellMean`).

**(b) Az ÉJSZAKAI oldal felhői is világítottak.** A Nap felé mért optikai
mélység csak azt mondja meg, mennyi FELHŐ van a minta fölött — azt nem, hogy a
Nap a horizont FÖLÖTT van-e. Enélkül a sötét oldal felhői teljes napfényt
kaptak, és világosszürke foltokként ragyogtak a majdnem fekete felszín fölött.
**Javítás:** `CloudVolume.DaylightFactor`, ami a TELJES szórási tagot kapuzza —
az ambiens (égbolt-) részt is, mert az égboltfény maga is SZÓRT NAPFÉNY, tehát
éjszaka nincs. Az alfa megmarad, ezért az éjszakai felhő SZILUETTKÉNT takar, ami
fizikailag helyes. A sáv szélessége SZÁRMAZTATOTT: egy h magasságban lévő
felhőt a Nap még `√(2h/R)` szöggel a horizont alatt is megvilágít; a dekk
tetejére (h ≈ 2 km, R = 7420 km) ez 0,023 rad, amit a ±0,05-ös
koszinusz-sáv lefed — ezért a felhő-terminátor a felszínihez képest kissé
később jön, ami maga a szürkület.

**(c) Sötét szürke felhőfoltok.** Egy egyszeres-szórású raymarch az optikailag
vastag felhőt **definíció szerint** sötétnek mutatja: a dekk belsejében a Nap
felé mért optikai mélység 5–20, tehát `exp(−τ)` gyakorlatilag nulla. A valódi
felhő viszont FEHÉR, mert a vízcsepp egyszeres-szórási albedója ~1 — a fotonok
nem elnyelődnek, hanem sokszor szóródnak és kijutnak. Ezt a hányadot egy
egyszeres-szórású modell soha nem tartalmazza. **Javítás:** a szokásos oktávos
közelítés (`CloudVolume.SunScatterGain`, 4 oktáv): a k. oktáv
`energia^k` energiát hordoz, `kioltás^k`-szoros optikai mélységet lát, és
`aszimmetria^k`-val laposabb fázisfüggvénnyel szóródik. A nulladik oktáv
EGZAKTUL az egyszeres szórás. Mérve `τ = 6`-nál: egyszeres szórás `2,5·10⁻³`,
oktávos összeg `> 0,2` — két nagyságrend.

#### Mérés (élő Play, 2026-09-27, 1600×900, befagyasztott Nap)

| Mit | Felhő KI | Felhő BE (review előtt) | Felhő BE (**végleges**) |
|---|---|---|---|
| Átlagos luminancia a bolygókorongon | 75,55 | 80,93 | **96,89** |
| Fényes (L > 140) pixelek aránya | 14,01% | 16,22% | **25,13%** |
| Eltérő pixelek a teljes képen | — | 10,57% | — |
| GPU-idő (`gpuFrameTime`, 16 minta átlaga) | 2,46 ms | 2,67 ms (**+0,21 ms**) | — |
| Atlasz-memória | — | 104 KB (egy RGBA32 textúra) | — |

A GPU-különbség a képkockák közti szórás (2,2–3,3 ms) felső határán van, tehát
**tájékoztató**, nem szigorú mérés — de a nagyságrend (tized ms) egyértelmű.

A harmadik oszlop a code review 3. megállapításának javítása UTÁN mért érték, és
önmagában is tanulságos: a naiv `0,5 + 0,5·fbm` leképezés a modellezett felhő
kb. NÉGYÖTÖDÉT elnyomta (a felhőnek tulajdonítható fényes hányad 2,2
százalékpontról 11,1-re nőtt). A „szebb" korábbi kép tehát nem jobb volt, hanem
HIÁNYOS — a mostani a modellhez hű.

**Következmény a látvány-ítéletre (B17).** A részlet-létra legfinomabb oktávja
~23 km hullámhossz, ami teljes bolygó-nézetben (1600 px, 4,6 km/pixel) ~5 pixel
— az ND-151 MÉRÉSSEL megállapított 8 pixel/oktáv kritériuma ALATT. Kontinens-
nézetben ez nem probléma (ott a felhő szemmel láthatóan térfogati), bolygó-
nézetben viszont szemcsés lehet. A lehetséges javítás ugyanaz a nézetfüggő
sáv-eltolás, amit az ND-151 használ, vagy kevesebb oktáv; hogy KELL-e,
felhasználói ítélet.

#### Amit a code review talált (mind javítva, kivéve a két dokumentált korlátot)

| # | Mi volt | Javítás |
|---|---|---|
| 1 | A felszíni felhőárnyék a bolygó-lokál `up`-ot egy VILÁG-téri Nap-iránnyal szorozta — tengely-forgatásos módban a zenitszög a bolygó forgását követte a Napé helyett | a Nap-irányt a bolygó keretébe forgatjuk |
| 2 | Az égbolt-nyitottság kiszámolva és becsomagolva, de a shader SEHOL nem olvasta; ráadásul minden sodródási ütemben (1,5 s) újraszámolt ~786 000 mintát | az A csatorna bekötve az ambiens tagba; a nyitottság világonként EGYSZER számolódik (gyorsítótár) |
| 3 | A sub-grid küszöb zárt alakja EGYENLETES zajra van levezetve, a shader viszont egy közel NORMÁLIS fBm-et adott neki (`0,5 + 0,5·fbm` → a minták a [0,37; 0,63] sávban) — a mean-preservation, amire az I3-érvelés épül, NEM teljesült | `DetailNoiseToUniform`: a normális eloszlásfüggvény logisztikus közelítése; a σ = 0,2537 **MÉRT**, és teszt méri újra a shader zajának tükrével |
| 4 | A menet ablaka egyetlen, a húr közepén vett mintából jött — a limbet súroló ~1100 km-es húron a terep-követő felhőalap kicsúszott belőle | három minta a húr mentén, min-alj/max-tető uniója |
| 5 | A héj-anyagot csak a GameObjectet LÉTREHOZÓ ág állította be; Play közbeni domain-reload után a felhő örökre a shader alapértelmezett Napjával rendereltetett volna | a meglévő ág visszaveszi a renderer anyagát |
| 6 | A régi héj-mesh minden sugárváltozásnál (deep-time lépés!) elszivárgott | a régit eldobjuk |
| 7 | Nem volt `OnDisable`/`OnDestroy` — a textúra, az anyag és a futó munka Play-ciklusonként felhalmozódott | `CancelCloudAtlasWork` + `ReleaseCloudVolumeResources` |
| 8 | Az árnyék a cella-átlagos lefedettséggel számol, a felhő a sub-grid szétbontással — az árnyéknak nincsenek a felhő pereméhez illeszkedő élei | **dokumentált korlát**: az egyeztetéshez a részlet-zaj MÁSODIK példánya kellene a terep-shaderben |
| 9 | A `_CloudWorldToPlanet` az `Update`-ből ment ki, a bolygó forgása viszont egy MÁSIK komponens `Update`-jéből jön — a keret egy frame-et késhetett | az uniformok a `LateUpdate`-ből mennek (ugyanott, ahol a mikro-részleté) |
| 10 | Holt státusz-property: az „RGBA32 nem támogatott" és „hiba az atlasz-építésben" sosem jutott ki | a státusz a teljesítmény-naplóba kerül |
| 11 | A `SurfaceSkyOpenness` doksija „nincs benne transzcendens"-t állított, a kód viszont nyers `Math.Cos`/`Sin`-t hívott | `DeterministicMath`, és a gyűrűk szögfüggvényei a ciklusból kiemelve |

#### Felhasználói visszajelzés utáni második kör (2026-09-27)

A felhasználói ítélet két dolgot kifogásolt: *„a felhő átlátszósága nem elég
magas, totálisan takarja minden felhő a területet, emellett pontosan
talajszintre van rajzolva, ... a hegységek csúcsa környékére kellene rajzolni."*
Mindkettő VALÓDI hibára mutatott, és a másodikból egy komoly, addig észre nem
vett hiba derült ki.

**(1) A felhő 1/111-ed magasságban volt rajzolva — a terep-nagyítás hiánya.**
A jelenetben `terrainReliefExaggeration = 111`, azaz a domborzat 111-szeres
függőleges nagyítással van rajzolva: egy 2308 m-es csúcs 255 km magasnak
(3,45 egység) látszik. A felhő magasságát viszont NEM szoroztam a nagyítással,
így a dekk a valódi 2500 m-nek megfelelő 0,034 egységnél ült — a rajzolt
hegyek TÖREDÉKÉNÉL, gyakorlatilag a felszínen. Innen a „talajszintre van
rajzolva" benyomás. **Javítás:** a `_CloudScale.x` mostantól „rajzolt egység
per MODELL méter", vagyis a felhő ugyanazt a függőleges nyújtást kapja, mint a
terep — enélkül a nagyított hegyek átdöfnék a dekket.

**(2) A dekk terepkövető volt, pedig középszintű felhőlap.** Az alap
`talaj + LCL` volt. Az LCL fizikailag helyes, de a FELSZÍNI légbuborék
kondenzációs szintje — a KÖD és a gomolyfelhő alapja, ami tényleg követi a
domborzatot. A csapadék-mező által hajtott dekk viszont frontális/konvektív
RENDSZER, aminek a tömege a közép-troposzférában ül, közel állandó nyomási
szinten, VÍZSZINTES lapként. **Javítás:** `MidLevelBaseMeters = 2500` — a WMO
középszintű osztályának (2–7 km) alsó pereme, ami MÉRVE a modellezett domborzat
fölé kerül (a legmagasabb szárazföld 2308 m, a szárazföld 99%-a 1469 m alatt,
a mediánja 325 m). A lap a szárazföld 99%-a fölött vízszintes; ahol a talaj
fölötti kondenzációs szint mégis fölé kerülne, ott folytonosan megemelkedik.

**(3) Minden felhő opak volt, mert nem létezett vékony felhő.** A vastagság
MINDEN nemnulla lefedettségnél legalább 500 m volt (τ = 7). **Javítás:**
`MinThicknessMeters = 80` (τ = 1,1, áttetsző fátyol), és a vastagság innen nő a
lefedettséggel — fizikailag a felhővastagság és a lefedettség erősen korrelál.

**(4) Az „opacitás" csúszka mostantól az OPTIKAI MÉLYSÉGET skálázza**, nem a
kész alfát: így a felhő fizikai módon vékonyodik (a sűrű mag továbbra is
opakabb a peremnél), nem egyenletesen elhalványul. Az alapérték **0,45** —
tudatos, visszafordítható render-döntés: a felhő optikai mélysége fizikailag
tényleg opak, de a bolygó megismerhetősége fontosabb. 1,0 = a fizikai érték.

**(5) A perem-lágyság (`SubGridEdgeWidth`) 0,25 → 0,35.** A 0,55 MÉRVE túl
sokat kent szét: a lágy perem a cella nagyobb részét érinti, így összességében
TÖBB eget takart el vékony felhővel.

Három új, élő Inspector-csúszka a látvány-ítélethez (B17):
`cloudDeckBaseMeters` (a lap szintje), `cloudEdgeSoftness` (perem-lágyság),
`cloudVolumeOpacity` (optikai mélység). Az elsőnek az atlaszt is újra kell
csomagolnia, ezt a munka-indító kapu kezeli.

**A második kör MÉRÉSE** (ugyanaz a nézet, befagyasztott Nap): korong-luminancia
75,55 (felhő nélkül) → 96,89 (a review utáni, még fizikai optikai mélységgel)
→ **90,12** (a 0,45-ös optikai-mélység-skalával); fényes pixelek 14,01% → 25,13%
→ **20,32%**. A felhő tehát továbbra is ott van, de a felszín átolvasható rajta.

**MEGMARADT LÁTVÁNY-KÉRDÉS (B17).** A 111-szeres függőleges nyújtás a mély
konvektív cellákat is felnagyítja: egy 9,5 km-es zivatarfelhő 1054 km-nek
(14 egység) rajzolódik, ami a limbnél kiugró dudorokat ad. Ez a nyújtás
KÖVETKEZMÉNYE és KONZISZTENS a tereppél (egy 2,3 km-es hegy 255 km-nek látszik),
nem hiba — de eldöntendő, hogy így maradjon-e, vagy a felhő VASTAGSÁGA kapjon
kisebb nyújtást, mint az ALAPJA.

#### Harmadik kör: kipúpo sodás, magasság-ugrálás, mozgás (2026-09-28)

Három további kifogás, mind valódi hibára mutatott.

**(1) „Bizonyos felhők durván kipúpo sodnak a bolygóból."** A második körben a
felhő minden függőleges méretét a terep 111-szeres nagyításával rajzoltam. Az
ALAPRA ez kell (különben a hegyek átdöfik), a VASTAGSÁGRA viszont káros: egy
9,5 km-es zivatarfelhő így 1054 km-es (14 egységnyi) toronyként rajzolódott egy
100 egység sugarú bolygón. **Javítás:** két KÜLÖN függőleges skála — az alap a
terep nagyítását követi, a vastagság saját, kisebb szorzót kap
(`cloudThicknessExaggeration`, alapértelmezés 20). A héj külső sugara 135,9-ről
121,2 egységre csökkent, a limb kitisztúlt.

**(2) „Ahogy mozgatom, adott felhő magassága is ugrál."** A lépésszám
nézetfüggő volt (12–48 a kamera távolságától). A menet a mintavételi ABLAKOT
osztja N részre, tehát a lépésszám változása ELTOLJA azokat a magasságokat, ahol
a sűrűséget mintavételezzük — egy vékony dekk súlyának középpontja fél
lépéssel elmozdul, ami a képen a felhő magasságának ugrásáként látszik.
**Javítás:** FIX 32 lépés (`CloudRaymarchPlan.MarchSteps`); a nézetfüggő
választó és a tesztjei törölve, helyettük egy teszt ŐRZI, hogy a lépésszám
konstans marad.

**(3) „A felhők pozíciója statikus."** HÁROM oka volt, és a harmadik egy
modell-szintű korlát, ami eddig nem derült ki:

  a) a sodródási út alapértelmezésben KI volt kapcsolva;
  b) a VALÓS időt integrálta, nem a szimulációsét — most a Nap órájából jön;
  c) **a modell beépített sodródása TELÍTŐDIK.** A
     `WindPrecipitation.WeatherNoiseRaw` úgy sodor, hogy a mintavételi
     ponthoz HOZZÁADJA a `driftAxis · (0,01·t)` vektort, majd újranormálja.
     Az elfordulás ezért `atan(0,01·t)`, ami **π/2-nél megáll**, és `t ≳ 100`
     fölött minden pont a drift-tengely felé kollapszál. MÉRVE: `t = 2559`-nél
     az eltolás 25,6 egység, az elfordulás 1,53 rad — a mintázat gyakorlatilag
     áll. Hosszú távú óraként használhatatlan.

**Javítás:** gömbön a helyes advekció a FORGATÁS. Az új
`CloudVolume.Advect` (Rodrigues-formula a modell saját sodródási tengelye
körül) izometria: tetszőlegesen sokáig fut, torzulás és telitődés nélkül. Az
időjárás-tag így VONUL, a klimatológia (ITCZ, orografikus) pedig a helyén
marad — pontosan ez a fizikai szerepmegosztás. A modell `t` paraméterét
nullán hagyjuk, hogy a telitődő út ne szóljon bele.

**MÉRVE** (azonos megvilágítás, EGY teljes napforduló alatt, a korongon):
a változó pixelek aránya **1,91% → 25,89%** (13,5×), az erősen változóké
0,00% → 0,90%. A sebesség SZÁRMAZTATOTT: 0,3 rad/nap = 2225 km/nap =
**25,8 m/s** a 7420 km-es sugáron — futóáramlás-szintű vonulási sebesség,
ami a középszintű dekkhez illik. Csúszkák: `cloudAdvectionRadiansPerDay`
(0–2) és `cloudWeatherStrength` (a vándorló hányad súlya; a modell saját
kalibrált 0,7-e az alapértelmezés).

**(4) „Deep time-ban totál változatlanok" — EZ NEM A FELHŐRÉTEG HIBÁJA.** A
`MoisturePrecipitation.Compute` a deep time értékét **meg sem kapja**: saját,
t=0-ás eleváció-mezőből számol, kráter és erózió nélkül (ezt a
`GetOrComputePrecipitationField` cache-ének komment je expliciten rögzíti is).
A csapadék-mező tehát deep-time-INVARIÁNS, és a felhő ezt hűen tükrözi — a
lemezek szétválása, a hegységek felemelkedése nem látszik a csapadékon, tehát
a felhőn sem. Ez MODELL-szintű hiány, nem render-hiba, és a javítása a
csapadék-lánc deep-time-osítása lenne — önálló tétel, saját ND-vel, mert a
klíma-mező numerikus viselkedését változtatja. Felvéve a todo2 B19 sorába.

#### Negyedik kör: a mozgás ÉSZREVEHETŐVÉ tétele és a deep-time bemenet (2026-09-28)

A visszajelzés: „a felhők érdemben továbbra se mozognak, deep time pedig
egyenesen fixek". Mindkettő jogos volt, és a mérésem volt félrevezető: egy
TELJES nap ugrásával mértem (25,9% változás), a nézőben viszont egy nap
**100 másodperc**, tehát a valós észlelet ennek a század része.

**(1) A mozgás átköltözött a shaderbe.** Az advekciót addig az atlasz
ÚJRAÉPÍTÉSE hajtotta, 1,5 másodperces ütemben — MÉRVE **1,35 pixeles
ugrásokban**, összesen 0,9 pixel/másodperc sebességgel. Most a shader forgatja
el az atlasz mintavételi irányát (`CloudAdvect`, ugyanaz a Rodrigues-forgatás,
mint a Core-ban), tehát a vándorlás KEPKOCKÁNKÉNT folytonos és **nulla
CPU-költségű**; az atlasz csak világ-változáskor épül újra. A felszíni
felhőárnyék ugyanazt a forgatást kapja, különben kicsúszna a felhő alól.

**(2) Az alapértelmezett sebesség prezentációs érték lett.** A fizikai
0,3 rad/nap a néző léptékén (100 s/nap, ~300 pixel sugarú bolygó) 0,9
pixel/másodperc — mérve nem észrevehető. Az alapértelmezés ezért
`PresentationAdvectionRadiansPerDay = 1,5` (4,5 pixel/másodperc, a mintázat
~4 nap alatt ér körbe). Ez UGYANOLYAN tudatos, dokumentált torzítás, mint a
domborzat 111-szeres függőleges nagyítása; a csúszka lefelé a fizikai
értékig megy. **MÉRVE** (AZONOS Nap-állás, csak 8 másodpercnyi advekciós
szögkülönbség): a korong **77,6%-a** változik, 23,5%-a erősen — a korábbi,
pixel alatti elmozdulás helyett.

**(3) A deep time mostantól VALÓDI bemenet (ND-157).** A megosztott
csapadék-mező a paraméteres `MoisturePrecipitation.Compute` eredménye, ami a
deep time értékét meg sem kapja. Az új, ADDITÍV `ComputeFromElevationField`
overload beengedi a hívó saját eleváció-mezőjét; a felhő-atlasz ezzel a
VIEWER deep-time mezőjéből (`_lastField` + kalibrált tengerszint) építi a saját
csapadék-mezőjét. A megosztott mező ÉRINTETLEN, tehát a csapadék-overlay és a
lapos réteg bitre a korábbi, és a paraméteres út minden régi hívója is.
**MÉRVE** (seed A7C944210000, azonos nézet): a felhő-lefedettség átlaga
t = 0-nál 0,081 / 21160 cella, **600 Myr-nál 0,088 / 20203 cella**, és a
mintázat láthatóan más — a felhő követi a lemezmozgást és a hegységek
felemelkedését.

**MELLÉKLELET, NEM A FELHŐRÉTEG:** deep-time újraépítés után a kép elmosódott,
a csillagok szaggatott csíkokká nyúlnak. BIZONYÍTVA, hogy nem a felhő okozza:
a `cloudVolumetric` kikapcsolásával is megmarad. Valószínű ok a HDRP
temporális akkumulációja a teljes világ-csere után — önálló tétel.

#### Ötödik kör: a HELYBEN MARADÓ apró foltok (2026-09-28)

A visszajelzés: „a tenger fölött mintha foltokban fix maradna a felhőzet, de
csak apró, viszont sok foltban".

**A hiba.** Az advekciót a shaderbe költöztetve az ATLASZ mintavételi irányát
forgattam el, a cellán BELÜLI részlet-zaj koordinátáját viszont nem:
`q = u * frekvencia + fázis` a FORGATATLAN irányt használta. A sub-grid
szétbontás (ND-154) a zaj és a lefedettség küszöb-összehasonlítása, tehát a
felhőfoltok APRÓ mintázata a BOLYGÓHOZ volt szögezve, miközben a lefedettség
elcsúszott fölötte — a foltok helyben maradtak, csak jöttek-mentek.

A TENGEREN a legfeltűnőbb, és ezért: ott a klimatológiai lefedettség sima
(nincs orografikus szerkezet), tehát a képet a rögzített zaj uralja.

**Javítás:** az advektált irány egyszer számolódik (`ua = CloudAdvect(u)`), és
MINDKETTŐ — az atlasz és a részlet-zaj — ezt használja.

**MÉRVE** (azonos Nap-állás, 2 másodpercnyi advekció-különbség, egy tenger
fölötti 220×220 pixeles folton, a legjobban illeszkedő eltolást keresve):

| | legjobb illeszkedés | átlagos eltérés eltolás nélkül | eltolással |
|---|---|---|---|
| javítás ELŐTT | **(0, 0) px** | 11,57 | 11,57 (0,0% javulás) |
| javítás UTÁN | **(+8, +8) px** | 17,35 | 5,44 (**68,7% javulás**) |

Vagyis a mintázat korábban BIZONYÍTHATÓAN nem transzlálódott (a nulla eltolás
volt a legjobb illeszkedés), most viszont a várt nagyságrendű eltolásnál
illeszkedik. A felhő-pixelek közül a változatlanok aránya 55,7% → 38,9%
(a maradék túlnyomórészt a foltok EGYSZÍNŰ BELSEJE, ami eltolás után is
azonos — ezért is kellett a fenti, eltolás-kereső mérés).

**ISMERT KORLÁT, dokumentálva.** Űrből nézve a burkoló KÖZELI lapja van elöl,
ezért ott a mélységteszt nem segít: egy a felhődekkbe emelkedő HEGY nem takarja
el a mögötte lévő felhőt. A domináns takaró (a bolygó túloldala) analitikusan
kezelt (belső héjgömb-metszés). A teljes megoldás mélységtextúra-olvasást
kívánna, ami HDRP-ben custom pass — külön tétel.

**Kapu.** `cloudVolumetric` (Inspector). Kikapcsolva a réteg nem rajzolódik és
a felszíni felhőárnyék EGZAKTUL 1-es szorzót ad, tehát a kép bitre az ND-154
előtti. Diagnosztika: `cloudVolumeDiagnostic` 0–4 (0 = ki, 1 = a burkoló tömör
kitöltése, 2 = a menet ablakának hossza, 3 = a nyers lefedettség, 4 = az alfa).

**Nem seed-törő, bizonyítva.** A `worldgen hash --seed A7C944210000 --plates 20
--level 6` mindhárom időpontban bitre az ND-150 óta dokumentált érték
(`t=0` `2b98af9a…6213738b`, `t=400` `14dc8ad2…59a7ea07`, `t=3000`
`823e8ba0…cdcfe7ab`). A `WorldGeneratorVersion` marad `"5"`.

**Tesztek.** 48 új Core-teszt (`CloudVolumeTests`) és 20 új viewer-teszt
(`CloudSkyAtlasTests`, `CloudRaymarchPlanTests`). Teljes futás:
**1901/1901 zöld**, Unity `compilationFailed: false`, 0 konzol-hiba, mindhárom
shader `msgCount = 0`.

**NYITOTT: a vizuális átvétel** — ld. todo2 B17. A mérés azt mutatja, hogy a felhő
ott van, a modellt követi és szerkezete van; hogy a mostani fényesség/sűrűség
SZÉP-e, felhasználói ítélet.


### ND-155 — M13 ambient occlusion: a mikro-relief sávja, mert makro-léptéken NINCS okkludáló domborzat (A16, ELFOGADVA, implementálva)

**A kérdés.** Az M13 „AO" tétele kézenfekvőnek tűnt: völgyek sötétedjenek,
gerincek ne. A kérdés az volt, MELYIK léptéken.

**A MÉRÉS döntötte el.** A klasszikus terep-AO az égbolt-nyitottság (sky view
factor). Az új, motorfüggetlen `SurfaceSkyOpenness` ezt zárt alakban számolja:
egy azimut-szektorban, ahol a horizont α szögben emelkedik, a
koszinusz-súlyozott látható hányad `cos²α = d²/(d² + Δh²)` — **tisztán
racionális**, nincs benne se szög, se transzcendens. A valódi világ
eleváció-mezőjén mérve (seed `A7C944210000`, 20 lemez):

| szint | cellaméret | szárazföldi nyitottság-ÁTLAG | minimum |
|---|---|---|---|
| 6 | 182 km | 0,999999 | 0,999965 |
| 8 | 45,5 km | 0,999996 | 0,999254 |
| 9 | 22,8 km | 0,999994 | 0,997554 |
| 10 | 11,4 km | 0,999992 | 0,990073 |

**Makro-léptéken okkludáló domborzat NEM LÉTEZIK — nem csak „nem látszik".**
Az ok nem a mintavétel: a világmodell relief-létrája ~1564 km hullámhossznál
VÉGET ÉR (`SurfaceMicroDetail.BaseFrequency`, ld. ND-151), tehát a modellezett
domborzat ezeken a léptékeken sima. Egy AO, ami itt bármit is mutatna, csak
felnagyított zaj lenne.

**Következmény: az AO oda kerül, ahol a meredek relief TÉNYLEGESEN van** — az
ND-151 per-pixel mikro-részletébe. Ez nem kompromisszum, hanem az egyetlen
hely, ahol okkludáló felület létezik. `SurfaceMicroDetail.AmbientOcclusion`:
a részlet-zaj MÉLYEDÉSEIBEN csökken, a kiemelkedéseken 1 marad, és a relief
AMPLITÚDÓJÁVAL skálázódik (sík üledéken gyakorlatilag nincs AO, szirten van) —
tehát nincs hozzá külön szabad csúszka.

**Az AO KIZÁRÓLAG az AMBIENS tagot csillapítja.** Ez fizika, nem stílus: a
horizont-eltakarás a SZÓRT (égbolt-) fényt veszi el, a közvetlen napfényt nem —
azt a `N·L` és a felhőárnyék kezeli.

**A makro sáv mégis be van kötve, MÉRT értékkel.** A nyitottság az atlasz A
csatornájában megy a shaderhez, és a terep-shader a fizikailag helyes módon
alkalmazza — az adat viszont azt mondja, hogy nincs eltakarás (~1), tehát a
látható hatása nulla. Ez SZÁNDÉKOS: nem erősítünk fel egy nem létező jelet.
Ha a modell valaha finomabb reliefet kap, az út kész, és a
`ReferenceLevelOpenness_IsEffectivelyOne_TheMeasurementBehindNd155` teszt —
ami CSAPDAZSINÓR, nem elvárás — elbukik, és pontosan erre mutat rá.

**A HARMADIK okkluder viszont valódi és planetáris: a FELHŐ.** A terep-shader
ugyanabból az atlaszból olvassa a lefedettséget és a vastagságot, és a DIREKT
napfényt Beer–Lambert szerint csillapítja, plusz a borult égbolt diffúz
padlójával (`OvercastDiffuseTransmission = 0,25`) — enélkül az árnyék FEKETE
lenne (τ = 10-nél az áteresztés 4,5·10⁻⁵), ami se nem fizikai, se nem nézhető.
Elég a felszíni pont fölött mintavenni: a Nap sugara a felhőalapot
`alap·tan(zenit)` távolságban keresztezi, ami 1500 m-es alapnál és 60°-os
zenitnél 2,6 km — az atlasz cellája 182 km, tehát az eltolás a felbontás ALATT
van, nem közelítés.

**Kapuk.** Az adat-overlay-ek (tektonika, szél, csapadék, hő) alatt a
felhőárnyék KI van kapcsolva — ugyanaz az elv, mint az ND-151 mikro-részleténél:
ott a szín egy MÉRT mennyiség palettája (I4), amit egy árnyék-moduláció
félreolvashatóvá tenne. Kikapcsolva (`cloudShadowStrength = 0` vagy a
mikro-részlet kapuja zárva) a shader EGZAKTUL 1-es szorzót és 1-es AO-t ad,
tehát a kép bitre a korábbi.

**Tesztek.** 11 új Core-teszt (`SurfaceSkyOpennessTests`, benne a fenti mérés
csapdazsinórként) és 5 új AO-teszt a `SurfaceMicroDetailTests`-ben.


### ND-156 — Színkalibráció: mi készült el most, és miért a felhasználói átvétel után jön a többi (A16, RÉSZBEN HALASZTVA)

**A tétel.** Az M13 harmadik lába a „végleges színkalibráció" (spec §73).

**Ami MOST elkészült: a felhő-réteg saját kalibrációja.** Ez új réteg, nincs
mihez „visszanyúlni", tehát a kalibrációja az ND-154 része és fizikai
alapokon áll: a kioltási együttható (0,02 1/m → egy 500 m-es rétegfelhő
optikai mélysége 10, a mért stratus-tartomány alsó sávja), a
Henyey–Greenstein-aszimmetria (0,62), a többszörös szórás oktávjai, és a
borult égbolt diffúz padlója (0,25 — a mért 20–30%-os globális sugárzás-arány).
Egyik sem szemre állított szám.

**Ami HALASZTVA: a TEREP-paletta újrafokozása.** Ennek két oka van, és
mindkettő tárgyi:

1. **A paletta jelenlegi értékeit a felhasználó ÉLŐBEN, szemre hangolta be**
   több körben (ld. a backlog felhő-/csillanás-/ambiens-köreit). Lineáris
   színtérben (`m_ActiveColorSpace: 1`) ezek a számok lineáris
   radiancia-szorzók, nem sRGB-albedók — egy „helyes színtérre hozás" tehát
   NEM javítás lenne, hanem a hangolás eldobása.
2. **A felhő megváltoztatja a referenciát.** Mostantól a kép jelentős része
   felhő, és a felhő fényessége a terep fölé kerül; hogy a terep ehhez képest
   hol legyen, csak a felhő vizuális átvétele UTÁN dönthető el. A todo2 maga is
   így fogalmaz: „a színkalibráció csak a többi látvány-tétel után értelmes".

**Az eljárás, amikor sorra kerül** (hogy ne kelljen újra kitalálni): minden
render-kategóriához tartozzon egy dokumentált FIZIKAI albedó (spec §29
kategóriái: víz, hó, jég, kőzet, homok, növényzet), a paletta lineáris
luminanciáinak RENDEZÉSE egyezzen az albedók rendezésével, és egyetlen
globális expozíciós tényezővel illeszkedjenek — ami ebből kilóg, azt kell
újrafokozni. Ez mérhető, tesztelhető kapu; a hiányzó bemenet nem a módszer,
hanem a felhasználói ítélet arról, hogy a felhővel együtt milyen összkép a cél.

**Következmény a listán:** az A16 volumetrikus felhő + AO lába KÉSZ, a
színkalibrációs lába a todo2 B18 sora (a B17 vizuális átvétel UTÁN).


### ND-158 — Éves hőstatisztika és a jégmaszk körkörös függésének feloldása (A7 6. fázis, ELFOGADVA, Core implementálva; a küszöb-kérdést az ND-159 zárta le)

**Dátum:** 2026-09-28. **Előzmény:** ND-100–104 (hőmodell), ND-142 (hő–szél
csatolás), ND-143 (állapot-azonosság + a 6. fázis fogyasztói kapuja),
ND-144 (kanonikus napi statisztika).

**A tétel.** Az ND-143/144 után két adatút készen állt (checkpoint, napi
`Ts/Ta` statisztika), de EGYETLEN fogyasztó sem állt át: a biome-osztályozás,
a jégmaszk és a párolgás változatlanul az analitikus
`Temperature.TemperatureKelvin`-t olvasta. Három nyitott kérdés maradt:
(1) éves statisztika, (2) a jégmaszk körfüggése, (3) a költség.

#### 1. Éves statisztika — a mintavétel szabálya

A napi átlag nem klímakritérium: a tartós jég küszöbe ÉVES átlag és ÉVES
minimum, a Whittaker-tábla pedig éves középhőmérsékletre van kalibrálva.
Az évet `sampleDays` darab EGÉSZ modellnap képviseli:

```
nap_j = firstDay + floor(j · keringési_periódus_nap / sampleDays),  j = 0 … n−1
mean[c] = (Σ_j napi_átlag_j[c]) / sampleDays
min[c]  = min_j napi_min_j[c];   max[c] = max_j napi_max_j[c]
```

Csak szorzás, osztás és `Math.Floor` — mind bitpontos IEEE-754, tehát a
mintanapok platformfüggetlenül azonosak (a transzcendens tiltás nem sérül).
Az alapértelmezett mintaszám **12**, szándékosan azonos a
`LakesIceErosion.NumAnnualSamples` és a `ThermalBaseline` éves ablakszámával
— egyféle éves mintavételi konvenció legyen a projektben. Föld-szerű évnél
ez a 0, 30, 60, 91, 121, 152, 182, 213, 243, 273, 304, 334. napot jelenti.

A napokat NÖVEKVŐ sorrendben dolgozzuk fel. Ez nem az eredmény, hanem a
KÖLTSÉG miatt fontos: a solver így a 30 napos bucketen belül folytatható, és
csak bucket-váltáskor indul kanonikusan újra. Az eredmény a sorrendtől
független, mert minden nap kanonikus állapotból indul — erre külön teszt van
(bepiszkolt, KÉSŐBBI napra állított állapottal).

**Ismétlődő nap = explicit hiba.** Ha a keringési periódus rövidebb, mint a
kért mintaszám, a floor-képlet kétszer adná ugyanazt a napot, és az átlag
CSENDBEN kétszer súlyozná. Ezért `ArgumentException`, nem hallgatólagos
deduplikálás.

#### 2. A jégmaszk körfüggése — KÉT RÖGZÍTETT MENET, nem fixpont-iteráció

A `SurfaceTemperatureField` bemenete a felszíntípus-térkép (abból jön az
albedó, az emisszivitás, a hőkapacitás). A jég viszont a hőmérséklet
KIMENETE. Eddig a viewer úgy vágta el a kört, hogy a solver a Build során
már kész, RÉGI (analitikus) jégmezőt kapott bemenetnek — a hőmodell jege
tehát sosem a hőmodellből jött.

A feloldás: a hívó **jégmentes** felszíntípus-térképet ad (Land / Ocean /
Freshwater — ez tisztán eleváció- és tó-kérdés, hőmérséklet nincs benne).

1. **A menet:** éves statisztika → jégosztály;
2. **B menet:** ahol az A tartós jeget adott, a típus `Ice` → új mező → éves
   statisztika → VÉGLEGES jégosztály.

A jégmentesség KIKÉNYSZERÍTETT: `Ice` a bemenetben `ArgumentException`.

**Miért nem „amíg nem változik" ciklus.** Egy fixpont-iteráció leállása
tolerancia- és sorrendfüggő lenne, oszcilláló cellák mellett pedig akár
végtelen — az I1 (bitre azonos világ) így nem tartható. A menetszám ezért
KONSTANS kettő, és a két menet közötti átsorolás MÉRVE van
(`ThermalClimate.ReclassifiedCells`), nem elrejtve.

**Bitazonos rövidzár.** Ha az A menet SEHOL nem talált tartós jeget, a B
menet felszíntípus-térképe definíció szerint azonos az A-éval, tehát ugyanaz
a mező ugyanabból az állapotból ugyanazt adná. A második futás elhagyása így
nem közelítés. MÉRVE: level 6-on **117,0 s → 56,6 s**. A `SecondPassSkipped`
jelzi, hogy ez történt-e.

A jég a FELSZÍNI (Ts) éves átlagra és minimumra osztályozódik, a
`LakesIceErosion.ClassifyIce` VÁLTOZATLAN küszöbeivel (−15 °C éves átlag,
273,15 K éves minimum) — hogy az átállás ne keverjen össze két változtatást
egy méréssel.

#### 3. A költség — MÉRVE

Seed `0xA7C944210000`, plates 20, t = 0, 12 mintanap, Release, párhuzamos
lokális lépés, ezen a gépen (nem CI-referencia):

| Szint | Cella | Tick / menet | Idő (2 menet) | Idő (rövidzárral) |
|---|---:|---:|---:|---:|
| 4 | 1 536 | 14 880 | 8,0 s | — |
| 6 | 24 576 | 14 880 | **117,0 s** | **56,6 s** |

Összehasonlításul a RÉGI analitikus jégút ugyanezen a level-6 világon:
**482 ms**. A nagyságrendi különbség (~120×) nem meglepő: az analitikus út
cellánként 12 × 24 inszoláció-mintát vesz, az új út 2 × 14 880 tickes teljes
solver-futás a teljes rácson.

#### 4. A MÉRÉS, AMI A FOGYASZTÓI ÁTÁLLÁST BLOKKOLJA

Ugyanaz a level-6 világ, ugyanaz a küszöb, két hőmérséklet-forrás:

| | tartós jég | szezonális hó | nincs | éves átlag tartomány |
|---|---:|---:|---:|---|
| RÉGI analitikus út | **1 727** | 10 491 | 12 358 | −81,0 … +47,9 °C |
| ÚJ hőmodell-út | **0** | 6 180 | 18 396 | −4,5 … +45,9 °C |

Egyezés a két jégosztály között: **17 181 / 24 576 = 69,9%**.

**Az átállás tehát ELTÜNTETNÉ a teljes állandó jégtakarót.** A gyökérok nem
hiba, hanem az M13 modellválasztás: a hőmodell bázisa SIMÍTOTT radiatív
faktort használ (`f_eff = 0,5·f_napi + 0,5·f_éves`), épp azért, mert a nyers
napi faktor a sarki éjszakán ~29 K-t adott. Ez viszont a sarki évi átlagot
−81 °C-ról −4,5 °C-ra emeli, és a −15 °C-os jégküszöb fölé viszi az EGÉSZ
bolygót.

Ez nem átkötési, hanem KALIBRÁCIÓS kérdés, és a két lehetséges válasz
(a β radiatív simítás csökkentése, vagy a jégküszöb újrahangolása a hőmodell
eloszlásához) **mindkettő seed-törő**. Ezért a biome/jég/párolgás
fogyasztói kapuja az ND-143 szerint **NYITVA MARAD**, most már mért indokkal;
a Core adatút és az átkötési felület kész. A kalibrációt külön ND-nek kell
eldöntenie, a felhasználó vizuális ítéletével együtt (a jégtakaró látható
bolygó-jellemző).

#### 5. Mi készült el most

- `ThermalAnnualStatistics` + `ThermalAnnualStatisticsCalculator` (Core);
- `ThermalClimate` + `ThermalClimateCalculator`: kétmenetes jég, biome-adapter
  (a hőmérsékleti tengely az éves LEVEGŐ-átlag), `IceFreeKinds()`;
- `MoisturePrecipitation.ComputeFromElevationField` overload explicit
  hőmérséklet-mezővel (`null` → a régi út, BITRE azonos minden korábbi hívóra);
- `worldgen thermal-climate` költségmérő parancs (nem CI-lépés);
- Python-orákulum: `tools/reference/thermal_annual_ref.py` →
  `thermal_annual_vectors.json`. A hőmodell nem új kernel, ezért az orákulum a
  már verifikált `thermal_field_ref.py` solverére épül, és kizárólag az
  aggregációt és a kétmenetes utat rögzíti — kis rácson (level 2, 96 cella) és
  rövid éven (8 nap, 4 minta), hogy pure Pythonban másodpercek alatt lefusson.
  Az orákulum-világ sarki fennsíkja (7000 m) SZÁNDÉKOS: enélkül nem keletkezne
  tartós jég, és a kétmenetes út nem lenne kipróbálva.

**Nem seed-törő:** új API-k, és a meglévő utak bitre változatlanok
(a `MoisturePrecipitation` régi overloadja delegál, `temperatureK = null`).
A `WorldGeneratorVersion` marad `"5"`, a `ThermalModelParameters.ModelVersion`
marad 3.


### ND-159 — A jégküszöb és a radiatív simítás ütközése (A7 6. fázis, ELFOGADVA: (C) percentilis küszöb, implementálva)

**Dátum:** 2026-09-28. **Előzmény:** ND-158 (éves hőstatisztika),
ND-100 (M13 radiatív simítás), ND-43 (`LakesIceErosion` jégküszöbei).

**A tétel.** Az ND-158 mérése szerint a hőmodellre átállított
jégosztályozás NULLA tartós jeget ad, szemben a ma látható 1727 cellával.
Ez a döntés arról szól, hogy ezt melyik irányban oldjuk fel. A döntés
**seed-törő** és **látható** (a jégsapka bolygó-jellemző), ezért nem
implementációs részletkérdés.

#### A mérés

`worldgen thermal-climate --seed 0xA7C944210000 --plates 20 --level 5 --beta 0,0.05,0.1,0.25,0.5`
(6144 cella, 12 mintanap, Release; a szintválasztás a futásidő miatt level 5,
az arányok level 6-on is ugyanezek).

| β | tartós jég | éves átlag min | **pillanatnyi min** | a mai takarót adó küszöb | egyezés a régivel |
|---|---:|---:|---:|---:|---:|
| 0 | **444 (7,23%)** | −79,0 °C | **−220,8 °C** | −19,12 °C | **91,8%** |
| 0,05 | 254 (4,13%) | −35,6 °C | −115,2 °C | −5,03 °C | 88,6% |
| 0,1 | 133 (2,16%) | −26,8 °C | −95,3 °C | −1,70 °C | 85,4% |
| 0,25 | 0 | −12,9 °C | −63,1 °C | +2,87 °C | 80,0% |
| **0,5 (mai)** | **0** | −1,7 °C | −33,1 °C | +6,91 °C | 69,8% |
| *cél: régi analitikus út* | *421 (6,85%)* | *−77,3 °C* | *−218,8 °C* | — | — |

A „pillanatnyi min" a mintavételezett év legalacsonyabb napi felszíni
értéke — ez az, amit a hőoverlay kirajzol.

#### Amit a mérés MEGFOGOTT, és ami átírja a kérdést

**A mai, látható jégtérkép egy FIZIKAILAG TARTHATATLAN hőmérséklet-mezőből
készül.** A régi analitikus út sarki éjszakai minimuma **−218,8 °C**
(= 54,3 K), maximuma **+107,5 °C**. A jégsapka tehát nem azért van a helyén,
mert a modell jól számol, hanem mert olyan hidegre megy, hogy bármilyen
küszöb alatt marad. Az ND-100 pont ezt a hibát javította ki a
hőmodellben a β = 0,5 simítással (`f_eff = 0,5·f_napi + 0,5·f_éves`).

Ebből következik, hogy **a β = 0 nem „visszaállítja a helyes állapotot",
hanem átviszi a régi út hibáját a hőmodellbe is** — és a hőoverlay-en
(amit az ND-141 óta látunk) a sarki éjszaka 52 K-re esne. A β = 0 tehát a
jégsapkát visszahozza, de a már elfogadott hőtérképet rontja el.

**Második, független észrevétel:** a β = 0,5 medián éves felszíni átlaga
**+33,7 °C**. A modell tehát nem csak a póluson meleg, hanem GLOBÁLISAN — ez
az üvegház-állandó, a meridionális szállítás és az óceáni pufferelés
együttes energia-mérlege, nem a β. Ez az ND-42 / B14 (b) nyitott
konstans-megerősítéséhez tartozik, nem ehhez a döntéshez, de a jég
hiányának ez is oka — és önállóan is mérendő.

#### Az opciók

**(A) β = 0, változatlan −15 °C küszöb.** A mai jégtakaró ~visszaáll
(444 vs 421 cella, 91,8% egyezés). ÁRA: a hőoverlay sarki éjszakája
−220 °C, azaz az ND-100 által már egyszer elvetett állapot tér vissza.
Seed-törő (a teljes hőmodell változik).

**(B) Köztes β (0,05–0,1).** Jég 4,1% / 2,2%, pillanatnyi min −115 / −95 °C.
Mindkét végen rosszabb, mint a szélő választások; nincs mért indoka, hogy
épp hol álljon meg. Seed-törő.

**(C) β = 0,5 marad, a jégküszöb PERCENTILIS lesz.** A „tartós jég" nem
abszolút −15 °C, hanem a leghidegebb N% (pl. a mai aránynak megfelelő
~6,85%). Ugyanaz a minta, mint az ND-126 csapadék-percentiliseinél és a
folyó-forrás kiválasztásnál: **abszolút küszöb világfüggő lenne**, és a
hőmodell abszolút szintje (lásd a +33,7 °C mediánt) úgysem hiteles.
Előny: a fizikailag ép pillanatnyi mező MEGMARAD, a hőoverlay nem romlik,
és a hőmodell maga NEM változik (nem seed-törő a hőmezőre, csak a
jégosztályozásra). Hátrány: a „−15 °C = tartós jég" fizikai jelentés
elvész, és minden világon lesz ugyanannyi jég (egy forró bolygón is).

**(D) Halasztás.** A jég marad a régi analitikus úton; csak a biome és a
párolgás áll át a hőmodellre. Ekkor egy világban két különböző
hőmérséklet-modell dönt (I4 szempontjából gyenge), viszont semmi nem romlik
el, és nem seed-törő.

#### Javaslat

**(C)**, az alábbi indoklással: ez az egyetlen opció, amelyik nem áldozza fel
a már elfogadott hőtérképet, a projektben már bevett mintát követi
(ND-126), és nem köti a jégtakarót egy olyan abszolút hőmérséklet-szinthez,
amelyről ugyanez a mérés mutatta ki, hogy nem hiteles. Az (A) akkor
választható, ha a „−15 °C = tartós jég" fizikai jelentés megőrzése
fontosabb, mint a hőoverlay — de akkor az ND-100 döntését is újra kell nyitni.

#### DÖNTÉS (2026-09-28): **(C) — percentilis tartós-jég küszöb**

A felhasználó a (C)-t választotta. Implementálva:
`ThermalIceClassification` — a tartós jég vágópontja az adott MENET SAJÁT
éves felszíni középhőmérséklet-eloszlásának q-percentilise
(`idx = (int)(q·n)` a rendezett mintán — a projekt egységes percentilis-
konvenciója), a szezonális hóé marad az ABSZOLÚT fagypont.

**q = 0,07**, mért alapon: a ma látható jégtakaró a cellák 6,85%-a level
5-ön és 7,03%-a level 6-on — a 0,07 ennek kerek megfelelője.

**Miért nincs metszet abszolút plafonnal.** Kézenfekvő volna a percentilist
megvágni egy „de csak fagypont alatt" feltétellel. MÉRVE ez majdnem üres
halmaz: β = 0,5 mellett a leghidegebb cella éves átlaga −1,7 °C, a 7.
percentilis már +7,02 °C — egy 0 °C-os plafon néhány cellát hagyna meg.
Pontosan az ND-124 hibaáosztálya, ezért tudatosan NINCS metszet.

**Eredmény**, ugyanazon a világon:

| | cél (régi út) | percentilis küszöb | egyezés | pillanatnyi min |
|---|---:|---:|---:|---:|
| level 5 | 421 (6,85%) | **430 (7,00%)** | 69,8% → **72,7%** | −33,1 °C |
| level 6 | 1727 (7,03%) | **1720 (7,00%)** | 69,9% → **72,6%** | −34,2 °C |

A pillanatnyi mező ÉRINTETLEN, tehát a hőoverlay nem romlik. A level-6
futásban a két menet **2 cellát** sorolt át egymáshoz képest — vagyis a
második menet nem formalitás, a jég-albedó tényleg visszahat.

**KÖLTSÉG-KÖVETKEZMÉNY.** Percentilis módban az A menet KONSTRUKCIÓ SZERINT
talál tartós jeget, ezért az ND-158 bitazonos rövidzára (a második menet
kihagyása) gyakorlatilag sosem lép be: a költség a teljes kétmenetes érték.
Mérve: level 5-ön **39,8 s** (a rövidzáras 17,0 s helyett), level 6-on
**117,1 s** (3,94 ms/tick, Release, párhuzamos lokális lépés). Ez a Buildbe
szinkron módon nem fér bele — háttérszál és/vagy lemez-gyorsítótár kell hozzá
(az ND-122/131 mintájára), ez az átkötés külön lépése.

**VÁLLALT ÁR, explicit:** a tartós jég aránya így minden világon ugyanaz —
egy forró bolygón is lesz „jégsapka". A modell abszolút szintjének
hitelességét az ND-160 tárgyalja.

**A fogyasztói kapu (ND-143) ezzel a jég oldaláról feloldható**; a viewer
tényleges átkötése és annak vizuális átvétele külön lépés. A mérőeszköz
(`worldgen thermal-climate --beta --ice-percentile --decompose`) a repóban
van, tehát bármelyik opció újramérhető más seedeken is.


### ND-160 — A bázis radiatív tagja BOLYGÓ-albedóval számol (LEZÁRVA 2026-09-29, (1) opció, SEED-TÖRŐ)

**Dátum:** 2026-09-28. **Előzmény:** ND-42 (üvegház-konstansok), ND-100
(bázis), ND-159 (β-söprés), todo2 B14 (b) konstans-megerősítés.

**A lelet.** Az ND-159 söprése mellékesen kimutatta, hogy a modell nem csak
a póluson, hanem GLOBÁLISAN meleg: a medián éves felszíni átlag
**+33,7 °C**. A `worldgen thermal-climate --decompose` a bázist tagonként
bontja fel (területtel súlyozott globális átlag, level 5, seed
0xA7C944210000):

| tag | érték (K) |
|---|---:|
| **T_rad(f_eff)** | **264,86** |
| T_üvegház | +33,00 |
| T_meridionális | +8,00 |
| T_óceán | −0,00 |
| T_magasság | −0,92 |
| T_ciklus | −2,05 |
| = bázis átlag | **302,89 K = +29,74 °C** |

**A gyökérok.** A radiatív tag a cella FELSZÍNI albedójával számol
(`Temperature.AlbedoOcean = 0,06`, `AlbedoLand = 0,30`), de a képlet maga
BOLYGÓ-energiamérleg, és a hozzáadott +33 K üvegház-eltolás a 255 K-es
bolygó-egyensúlyhoz van kalibrálva — ami viszont a≈0,30-as BOLYGÓ-albedót
feltételez (felhők + légkör). Ellenőrző számítás f = 0,25 mellett:

| albedó | T_rad |
|---|---:|
| 0,30 (bolygó-albedó) | 254,58 K |
| 0,06 (óceán-felszín) | 274,05 K |
| 0,144 (a világ 65% óceánának kevert FELSZÍNI albedója) | 267,71 K |

A mért 264,86 K pont itt van (a ^0,25 konkavitása húzza kicsit lejjebb),
tehát **+10,3 K-nel a 255 K fölött**. Ez a többlet, plusz a +8 K
meridionális tag viszi a bázist +29,7 °C-ra a várt ~+15 °C helyett.

**Fontos: a SOLVER nem hibás.** A tickenkénti anomália-tag
(`_cellAlbedoTerm = SolarConstant · (1 − Albedo(kind))`) helyesen használ
felszíni albedót — ott az a fizikailag helyes mennyiség. A keverés
kizárólag a BÁZIS radiatív tagjában van.

**Opciók (egyik sincs implementálva):**

**(1) Bolygó-albedó a bázisban.** A bázis radiatív tagja egységes
a≈0,30-cal számol; a felszíni albedó-különbség marad ott, ahová való —
az anomália-tagban. Fizikailag ez a konzisztens válasz, és a globális átlag
közel ~+15 °C-ra kerülne. Seed-törő.

**(2) Az üvegház-konstans csökkentése.** Ugyanazt az átlagot adja, de
elfedi az okot, és más világokon (más szárazföld-aránnyal) más hibát hagy
hátra, mert a felszíni albedó keveréke világfüggő. Seed-törő.

**(3) Marad így.** A homérséklet-panel és a biome-sávok tudatosan meleg
bolygót mutatnak. A percentilis-küszöbök (ND-126, ND-159) ezt már
kezelik, mert nem abszolút szintre támaszkodnak.

**Javaslat: (1)**, de csak a 6. fázis fogyasztói átállása ÉS annak vizuális
átvétele UTÁN — különben egy mérésben két változás keveredne, és
pontosan ez a hiba vezetett az ND-142 kalibrációs köréhez.

---

## LEZÁRÁS (2026-09-29): az (1) opció, implementálva és megmérve

**A halasztási feltétel teljesült.** A 6. fázis fogyasztói átállása kész
(ND-162 jégmaszk, ND-164 biome/párolgás) és a vizuális átvétel megtörtént —
az ítélet az volt, hogy „kikapcsolva fest jobban", és pont ez tette a
FIZIKAI hitelességet a következő kör fő tételévé (todo2 A24). A szintet
javító változás tehát most nem keveredik mással: ez az egyetlen numerikus
módosítás ebben a körben.

**Amit a kód most tesz.** A bázis radiatív tagja egységesen
`Temperature.AlbedoPlanet = 0,30`-cal számol, felszíntípus-függetlenül. Két
helyen — és SZÁNDÉKOSAN ugyanabból a forrásból:

| hely | mi változott |
|---|---|
| `ThermalBaseline` (cella-bázis, óceáni éves radiatív átlag) | `_parameters.BaselineAlbedoFor(...)`, az új alapértelmezéssel |
| `ThermalWind.PointTemperature` (a bázis 4 offset-pontja) | eddig BEDRÓTOZOTT `Temperature.AlbedoOcean/AlbedoLand`, most ugyanaz a paraméter |

A szél azért tartozik ide, mert a bázis KÉPLETÉT differenciázza: ha ott más
albedó szerepelne, a szél egy nem létező bázishoz tartozó gradienst adna.
(Az ND-160 mérőkampó ezt még nem érte el — a `--baseline-albedo` csak a
`ThermalBaseline`-t állította át, a szelet nem. A most mért aggregátumok
mégis egyeznek a kampó számaival, mert az albedó a gradienst csak
multiplikatívan, `((1−a)/(1−a'))^0,25` arányban skálázza, és a 4 offset-pont
ugyanahhoz a cellához tartozik, tehát a szél-változás az óceánon 7,1%-os
skálázás, a szárazföldön nulla.)

A `Temperature.AlbedoOcean/AlbedoLand` **megmarad** — a solver tickenkénti
anomália-tagja (`_cellAlbedoTerm`) továbbra is ezekkel dolgozik, ott ez a
fizikailag helyes mennyiség. Az `AlbedoPlanet` számszerűen egyenlő az
`AlbedoLand`-del, de NEM ugyanaz a mennyiség, ezért külön konstans.

**A/B-kampó a visszaméréshez.** `ThermalModelParameters.LegacySurfaceBaselineAlbedo`
(CLI: `--legacy-baseline-albedo true`) bitre visszaadja az ND-160 ELŐTTI,
kevert albedós bázist. A kampó VALÓDI: a lenti „régi" oszlop számai ezzel
a kapcsolóval, a mai kódból készültek.

**MÉRÉS** (`worldgen thermal-climate --seed A7C944210000 --plates 20
--level 5 --decompose true`, Release, 12 mintanap, 6144 cella / 3993 óceáni):

| mérték | régi (felszíni albedó) | ÚJ (bolygó-albedó) |
|---|---:|---:|
| T_rad(f_eff) globális átlag | 264,86 K | **252,19 K** |
| bázis globális átlag | 302,89 K (+29,74 °C) | **290,22 K (+17,07 °C)** |
| éves átlag mediánja (P50) | +33,7 °C | **+18,1 °C** |
| éves átlag MINIMUMA (P0) | −1,7 °C | **−1,7 °C (bitre ugyanaz)** |
| éves átlag maximuma | +45,9 °C | **+25,5 °C** |
| egyezés a régi (analitikus) jégosztállyal | 72,7% | **86,4%** |
| a mai 421 jégcellát adó küszöb | +6,91 °C | **+6,04 °C** |
| szezonális hó | 1108 cella | **1943 cella** |
| tengeri jég (jégosztályos hidegvég) | 32 → 0 cella | **64 cella (1,6%)** |
| biome-egyezés a maival, szárazföldön | 78,3% | **80,1%** |

**Amit a döntés MEGOLDOTT:** a szintet. A globális medián +33,7 → +18,1 °C
(a Föld ~+15 °C-jához közel), a tengeri jég visszatért, a szezonális hó
megháromszorozódott, és a régi jégtakaróval való egyezés 72,7 → 86,4%.
A hiba VILÁGFÜGGŐSÉGE is megszűnt: a bázis szintje már nem a szárazföld-arány
függvénye.

**Amit NEM oldott meg, kimondva:** a hideg végét. A minimum BITRE ugyanott,
−1,7 °C-on maradt — mert az a szárazföldi póluson van, ahol a felszíni
szárazföld-albedó eddig is 0,30 volt, tehát a bázis ott bitazonos. Ezt a
`PlanetaryBaselineCoolsTheOceanAndLeavesLandBitIdentical` teszt ki is
kötözi: a szárazföldi bázis bitre változatlan, az óceáni 13–21 K-nel hűl.
Következésképp az ND-159 (percentilis jégküszöb) és az ND-164 (jégosztályos
biome-hidegvég) **továbbra is kell** — a tartomány összenyomottságának oka
másban van (radiatív simítás β, hőkapacitás, meridionális transzport), ez a
todo2 A24 (b) tétele.

**Seed-törés.** `WorldGeneratorVersion.Current` 5 → **6**,
`ThermalModelParameters.ModelVersion` 3 → **4**. Minden hőmodell-kimenet
változik (bázis, szél, éves éghajlat, jégosztály, biome, csapadék). A
`ThermalCheckpoint.ComputeModelIdentity` mostantól a BÁZIS albedóját is
tartalmazza (óceán + szárazföld érték, ami mindhárom módot szétválasztja) —
nélküle egy A/B-mérés némán a másik mód gyorsítótárára találna rá; ez
pontosan az ND-162 csapdaosztálya. Python-referencia és tesztvektorok
újragenerálva (`thermal_field_ref.py` MODEL_VERSION 4,
`thermal_checkpoint_ref.py` generator „6").

**Nyitva marad:** ugyanez a keverés az ANALITIKUS hőmérséklet-úton is ott
van — ld. **ND-165**.


### ND-161 — Durvább rácson számolt éves éghajlat: ELUTASÍTVA, mérés alapján (A7 6. fázis)

**Dátum:** 2026-09-28. **Előzmény:** ND-158/159 (a kétmenetes éves éghajlat
level 6-on **117,1 s**), ND-100 18. pont (magasságkorrekció).

**A kérdés.** A 117 s a Buildbe szinkron módon nem fér bele. Mielőtt
gyorsítótárat és háttérszálat építenénk rá, meg kellett mérni az olcsóbb
választ: az éghajlat látszólag SIMA mező, tehát hátha durvább rácson
számolva és felnagyítva is ugyanazt a jégmaszkot adja.

**Három változatot mértem**, hogy a hatások szétváljanak:
(a) a durva szint SAJÁT világa (saját eleváció-lánc és tengerszint);
(b) UGYANAZ a világ lemintavételezve (blokk-átlag eleváció, a teljes
szintű tengerszinttel) — ez izolálja a rácsfelbontást a világdefiníciótól;
(c) (b) + PER-FINOM-CELLA magasságkorrekció
(`SurfaceTemperatureField.AltitudeCorrectedK`, ND-100 18. pont).

Seed 0xA7C944210000, level 6 referencia (1720 tartós jégcella), q = 0,07.
A „Jaccard" a tartós-jég halmazok metszete/uniója — ez a lányeges szám,
mert a jégsapka HELYE látszik, nem a darabszáma.

| éghajlat szintje | idő | (a) saját világ | (b) lemintavételezve | (c) + magasságkorrekció |
|---|---:|---:|---:|---:|
| 5 (4× kevesebb cella) | 28,1 s | 73,0% | 76,4% | **85,5%** |
| 4 (16×) | 7,6 s | 61,7% | 65,0% | 75,5% |
| 3 (64×) | 2,3 s | 47,1% | 56,8% | 66,5% |

Maradék maximális éves-átlag eltérés a legjobb változatban (level 5, (c)):
**20,9 K**.

**ÍTÉLET: ELUTASÍTVA.** A legjobb eset is minden hetedik jégcellát rossz
helyre tenne, és ezért cserébe mindössze 4× gyorsulást ad (117 → 28 s),
ami a Build szinkron költségként továbbra sem vállalható. A 16× és 64×
változatok, amik már érdemi gyorsulást adnának, használhatatlanok.

**Miért nem működik.** A lemintavételezés alig javít (73,0% → 76,4%),
tehát nem a világdefiníció az ok, hanem MAGA A FELBONTÁS. A magasság-
korrekció sokat javít (76,4% → 85,5%), tehát a hiba nagy része valóban a
lapse rate — de a megmaradó 20,9 K több annál: egy durva cella
szárazföldet és óceánt is tartalmaz, és a felszíntípus a solverben nem
simítható utólag — más albedó, más hőkapacitás, más szél. Az éghajlat
tehát nem azon a rácson sima, amin a világ nem az.

**Következmény.** A 117 s-ot ARCHITEKTÚRÁVAL kell kezelni, nem
közelítéssel: lemez-gyorsítótár az ND-143 `ModelIdentity` kulcsával
(az ND-122/131 mintájára) és háttérszál előnézettel (az ND-145 mintájára).
Ez a viewer oldalán élő Unity-t igényel, tehát külön lépés.

Ugyanaz a hibaosztály, mint az ND-128/ND-151-nél: a kézenfekvő olcsóbb út
mérésen bukott meg, és a mérés marad a repóban
(`worldgen thermal-climate --climate-level`), hogy újrafuttatható legyen.


### ND-162 — A jégmaszk átállása a hőmodellre: előnézet + háttérszál + cache (A7 6. fázis, ELFOGADVA)

**Dátum:** 2026-09-28. **Előzmény:** ND-158 (éves adatút), ND-159
(percentilis küszöb), ND-161 (a durva rács elutasítva), ND-145
(előnézet-minta a folyóknál), ND-122/131 (lemez-cache minta).

**A tétel.** Az ND-158/159 után a Core adatút kész és a küszöb-kérdés
eldőlt, de a viewer jégmaszkja még mindig az analitikus
`LakesIceErosion.AnnualTemperatureStats`-ból jön. Ez a döntés az átkötés
MÓDJÁRÓL szól.

#### 1. Előnézet + autoritatív csere (az ND-145 mintája)

A Build nem várhat 117 s-ot. Ezért:

- a Build változatlanul lefuttatja az OLCSÓ analitikus utat (MÉRVE 684 ms
  level 6-on) és azonnal rajzol — ez az **ELŐNÉZET**;
- egy háttérszál közben előállítja a hő-éghajlatot (lemez-cache-ből vagy
  számolva), és amikor kész, kicseréli a jégmezőt és újraszínez;
- új Build vagy világváltás a függőben lévő munkát ÉRVÉNYTELENÍTI
  (revizió-szám), tehát korábbi világ eredménye SOHA nem kerülhet a képre.

A panel kiírja, melyik forrás aktív (előnézet vagy hőmodell) — az I4 szerint
a számnak forrása van, és itt a forrás menet közben változik.

#### 2. A CSAPDA, amit a bekötés előtt találtam: KÉT deep-time hőforcing

A két út KÜLÖNBÖZŐ deep-time hőmérséklet-eltolást használ, és ez eddig
nem volt kimondva:

| | forcing | periódus | amplitúdó | hol |
|---|---|---|---|---|
| analitikus jégút | `DeepTimeErosionGlaciation.GlobalTempOffset` (ND-44) | **150 Myr** | ±6 K | a viewer adja hozzá |
| hőmodell | `Temperature.ClimateCycleTemperatureK` (Milanković) | **10–500 kyr** | ±(2+3+1) K | a bázisban (`ThermalBaseline.CycleK`) |

Ha az átállás csendben elhagyná az ND-44 eltolást, a deep-time viselkedés
LÁTHATÓAN elromlana: a Milanković-ciklusok periódusa 10–500 ezer év, az
időcsúszka viszont **millió években** lép — egyetlen csúszka-lépés alatt
2–20 teljes ciklus futna le, tehát a jégsapka ugrálna, ahelyett hogy a
150 Myr-es lassú lengést mutatná. Ugyanaz az alias-osztály, mint az
ND-153-ban a napi forgásé.

**Döntés:** az ND-44 eltolás EGYELŐRE MEGMARAD, a hőmodell éves átlagára
hozzáadva. Indok: ebben a körben PONTOSAN EGY dolog változik — a
hőmérséklet TÉRBELI forrása —, hogy a vizuális átvétel egy változást
ítéljen meg. A két forcing összevonása (az ND-44 sinus beemelése a
hőmodell bázisába a Milanković-tag helyére vagy mellé) külön, SEED-TÖRŐ
döntés — ld. a B14 (a) konstans-megerősítést, ami az ND-44
„illusztratív” 150 Myr / 6 K értékeit úgyis nyitva tartja.

Kimondva: így ÁTMENETILEG két deep-time forcing összeadódik a jégmaszkon.
Ez nem elegáns, de MÉRHETŐ és visszafordítható, és nem kever két
változást egy ítéletbe.

#### 3. Hatókör: ELŐSZÖR CSAK A JÉG

A biome és a párolgás ebben a körben a régi úton marad. Indok ugyanaz: a
jégnek van mért A/B-je (72,6% egyezés, 1720 vs 1727 cella) és tiszta
vizuális ítélete; a biome átállása az éves LEVEGŐ-átlagra egy másik,
önállóan megítélendő változás.

#### 4. A cache és a lenyomat

A lemez-gyorsítótár a `ThermalClimateDiskCache` (ND-158 kör, már kész és
tesztelt): kulcsa az ND-143 `ModelIdentity` + mintanapok + jégküszöb-mód +
a rövid kanonikus előtag lenyomata. A fájlkezelés és a kvóta a
`TerrainBasisDiskCache` viewer-oldali mintáját követi; minden I/O hiba
nyelve: a cache kényelem, nem adat — hibánál számolunk.

#### 4b. A MÉRT EDITOR-KÖLTSÉG (2026-09-28, élő ellenőrzés)

Az ND-158/159 időadatai **.NET 8 Release CLI**-ből származnak. Az élő
ellenőrzés kimutatta, hogy az **Editor (Mono) lényegesen lassabb**, és ezt
a különbséget a döntésnek tartalmaznia kell:

| | ms/tick | 1 menet (14 880 tick) | 2 menet |
|---|---:|---:|---:|
| .NET 8 Release (CLI), level 5 | 1,34 | 17,0 s | 39,8 s |
| **Unity Editor (Mono), level 5** | **3,54** | **52,7 s** | **~105 s** |

A mérés level 5-ön, 6144 cellán, Play nélkül készült. A napi statisztika
rétege NEM szűk keresztmetszet: nyers léptetés 3,50 ms/tick, a napi
statisztikán keresztül 3,56 ms/tick (1,1×).

**A párhuzamos lokális lépés Monóban NEM gyorsít.** Mérve: 200 tick
szekvenciálisan 3,56 ms/tick, párhuzamosan 3,55 ms/tick — a `Parallel.For`
tickenkénti particionálási költsége felemészti a nyereséget ekkora
munkacsomagnál (tickenként egyetlen, 6144 elemű ciklus). Play közben
ráadásul ugyanazon a ThreadPoolon versenyez az Editor saját munkáival.
Ezért a viewer-oldali worker **szekvenciálisan** lép; a CLI (ahol a
párhuzamosítás használ) ettől függetlenül megtartja.

**Play közbeni első mérés:** a háttérmunka 15 percnél tovább futott anélkül,
hogy befejeződött volna — vagyis Play alatt a versenghelyzet további,
NEM MÉRT lassulást ad a fenti ~105 s-hoz képest. A párhuzamos lépés
kikapcsolása utáni újramérés hátravan; a pontos Play-beli számot NEM
állítjuk addig, amíg nincs tisztán mérve.

**Következmény a tervre:** az előnézet + háttérszál + cache felépítés
ettől nem rossz — sőt, ez pont az az eset, amiért kell. De a felhasználó
egy ÚJ világnál PERCEKIG az előnézetet látja, nem másodpercekig. Ha ez
soknak bizonyul, a következő mérhető lépés a mintanapok számának
csökkentése (12 → pl. 4, ami közel harmadára viszi a tickeket) — ára a
durvább évszakos mintavétel, tehát külön A/B kell hozzá.

**MÉRÉSI TANULSÁG, kimondva:** az első két Editor-benchmarkom eredménye
sosem érkezett meg, mert `EditorPrefs`-be írtam őket egy HÁTTÉRSZÁLRÓL —
az Unity API főszálat követel, tehát a kimenet (és a catch-ág is) kivételt
dobott, a mérés pedig végtelen „running”-nak látszott. A mérőeszköz
hibája majdnem a mért rendszer hibájának látszott; a fenti számok már
fájlba írt, ismételt mérésből valók.

#### 4c. ÉLŐ IGAZOLÁS és egy CSENDES HIBA, amit csak ez fogott meg

**A hiba.** A `ThermalClimateCacheDirectory` az
`Application.persistentDataPath`-ból készült — az viszont **Unity API, amit
kizárólag a főszálról szabad olvasni**. A háttérszálon mindhárom
cache-művelet (takarítás, olvasás, írás) `UnityException`-nel szállt el.
A következmény NEM összeomlás volt — a hibákat lenyeljük, ahogy a terv
előírja —, hanem az, hogy **a cache soha nem íródott és soha nem talált**:
a funkció „működött", csak épp sosem gyorsított, és minden Build újra
számolt. Offline fordítással és tesztekkel ez NEM látszik; csak élő Play
mutatta meg. Javítva: a könyvtárat a főszál rögzíti a worker indítása
előtt (`EnsureThermalClimateCacheDirectory`).

**Igazolás (level 3, 384 cella — a mechanizmus szintfüggetlen):**

| lépés | mért eredmény |
|---|---|
| Build, cache-tévesztés | ELŐNÉZET aktív: 135 jég-tile, küszöb **258,150 K** (abszolút, ND-43) |
| háttérszál | `RanToCompletion`, **6,8 s**, cache-fájl **38 249 bájt** |
| újabb Build után | **`ThermalClimateIceActive = true`**, küszöb **281,445 K** (ND-159 percentilis vágópont) |
| új Play, cache-találat | `fromCache=true`, **65 ms** (6,8 s helyett), azonnal a hőmodell jege |

**A verifikáció korlátja, kimondva:** MCP-vel vezérelt, nem fókuszált
Editorban a Play-hurok nem lép tovább (`Time.frameCount` 1-en állt), ezért
az `Update()` — és vele a csere — magától sosem futott le. A fenti
eredmény `EditorApplication.Step()` frame-léptetéssel készült. Emiatt a
KORÁBBI, level-5 Play-megfigyelésünk („15 percig futott") sem értékelhető
teljesítmény-adatként. A tényleges vizuális átvétel továbbra is a
felhasználó fókuszált Editorában tartozik megtörténni.

#### 5. Amit ez NEM változtat

A `WorldGeneratorVersion` és a hőmodell verziója változatlan: a jégmaszk
RENDER- és panel-kimenet, nem a generátor numerikus lánca. A
`WorldStateHash` (eleváció-összeg) jelentése sem változik.


### ND-163 — A csapadék szél-tagja: a cellaközéppontii szélvektor kivezetése (A7 7. fázis, ELFOGADVA a Core-részre; FOGYASZTÓI KAPU ZÁRVA)

**Dátum:** 2026-09-28. **Előzmény:** ND-142 (hő–szél csatolás),
ND-158 (párolgás hőmérséklet-bemenete), ND-162 (a jég átállása).

**A tétel.** Az A7 7. fázisából ez maradt: a `MoisturePrecipitation` még
mindig a RÉGI, analitikus `WindPrecipitation.WindVector`-ból veszi a szelet —
tehát a csapadék-mező nem látja az ND-142 csatolt szelét.

**A fő felismerés: itt NINCS ÚJ NUMERIKA.** A csatolt szél belső `Wind(...)`
függvénye MÁR kiszámolja a cellaközéppontbeli teljes 3D szélvektort
(`wx, wy, wz`) és a kelet/észak komponenseket — a `SampleCoupled` viszont a
celláknál eldobja őket (`out _, out _, out _`), és csak a NAGYSÁGOT
(`cellSpeed`) tartja meg. Vagyis a hiányzó mennyiség már ott van a
memóriában; csak kivezetni kell.

Ezért ez a lépés **nem igényel új Python-orákulumot**: a számolás
bitre ugyanaz, amit az ND-142 vektorai már lefednek. Amit bizonyítani kell,
az az, hogy (a) a meglévő kimenetek BITRE változatlanok, és (b) az új
vektor-kimenet és a régi `cellSpeed` KONZISZTENS (a vektor hossza a
tangenciális síkban pontosan a sebesség).

**Megvalósítás (Core):**
1. `ThermalWind.SampleCoupled` új túlírása, ami a cellaközéppontbeli
   szélvektort is kitölti. A régi alák változatlanul delegál, tehát minden
   meglévő hívó bitre azonos eredményt kap.
2. `SurfaceWindSample` értéktípus (3D irány + sebesség) — ez az a alak,
   amit a nedvesség-transzport vár.
3. `MoisturePrecipitation.ComputeFromElevationField` új túlterhelése, ami a
   szelet KÍVÜLRŐL kapja — pontosan úgy, ahogy az ND-158-ban a
   hőmérséklet-mezőt. `null` esetén a régi, analitikus út fut, BITRE
   azonosan.

**A FOGYASZTÓI KAPU ZÁRVA MARAD.** A viewer egyelőre nem áll át. Indok az
ND-162 3. pontjával azonos: a csapadék-mező hat a FELHŐRE, a
folyó-forrásokra és a biome csapadék-tengelyére is — három látható
következmény egyszerre. A jég átállásának vizuális átvétele még nem
történt meg; két változást egy ítéletbe keverni pontosan az a hiba, ami az
ND-142 kalibrációs köréhez vezetett.

#### A mintavétel kérdése — MEGMÉRVE (2026-09-28)

A csapadék-mező EGYETLEN `dayT` pillanatra készül, a csatolt szél viszont
tick-szintű mennyiség. Választott bemenet: az **ÉVES adatúton belüli
átlag** — a mintanapok MINDEN tickjén vett cellaközépponti szélvektor és
sebesség átlaga. Ez INGYEN jön az éghajlat-futással (a `Step` amúgy is
kiszámolja a szelet), és illeszkedik ahhoz, hogy a csapadék klíma-jellegű
mennyiség, nem pillanatkép.

**A vektorátlag és a sebességátlag KÜLÖN mennyiség**, és ez nem pongyolaság:
a vektorátlag hossza kisebb az átlagsebességnél, ha a szélirány az év
során forog. MÉRVE (level 5, seed 0xA7C944210000): a „szél-állandóság"
(|átlagvektor| / átlagsebesség) **0,746**, tehát átlagosan ~25% kioltás —
valódi fizikai tartalom (monszun-jelleg), nem numerikus hiba. A csapadék
mindkettőt használja: az IRÁNYT a nedvesség kifolyásához, a SEBESSÉGET a
párolgáshoz.

#### A FOGYASZTÓI A/B — ez dönti el, miért marad zárva a kapu

`worldgen thermal-climate --precip true`, level 5, 2151 szárazföldi tile.
A mérőszám nem a nyers csapadék-különbség, hanem a **NEGYED-besorolás**
változása: az ND-126 szerint a biome-ot a csapadék PERCENTILISEI döntik el,
tehát egy egyenletes skálázódás SEMMIT nem változtatna a képen — ami
számít, az az átrendeződés.

| változat | átlagos \|eltérés\| | negyed-besorolás egyezés |
|---|---:|---:|
| (1) csak a HŐMÉRSÉKLET a hőmodellből | 6,8% | **86,2%** |
| (2) + a SZÉL is a hőmodellből | 42,7% | **67,7%** |
| (2) a (1)-hez képest (= a szél önmagában) | 41,0% | 70,5% |

**Az eredmény: a két átállás NEM egyenrangú.** A hőmérséklet-csere
mérsékelt (a szárazföld 86%-a ugyanabban a csapadék-negyedben marad); a
SZÉL-csere viszont a szárazföld csaknem harmadát átsorolja, és átlagosan
41%-kal más csapadékot ad. Ez átrajzolná a biome-térképet, a felhőket és a
folyó-forrásokat is.

**Egy figyelmeztető szám:** a maximális eltérés **38,94**, miközben az átlag
1,06 — vagyis egyes tile-okon 37-szeres az eltérés. Ez ott várható, ahol az
éves átlagos szél majdnem kioltódik: a nettó szállítás eltűnik, és a
nedvesség helyben halmozódik. Ez modellezési kérdés (a kioltódó szélű
helyeken talán a SEBESSÉG-átlag irányával kellene számolni), nem
implementációs hiba — de a fogyasztói átállás előtt tisztázni kell.

**Ezért a három fogyasztó ÁTÁLLÍTÁSA HÁROM KÜLÖN ÍTÉLET**, nem egy:
a jég (ND-162, kész és aktív), a biome/párolgás hőmérséklete (mérsékelt
változás), és a csapadék szele (nagy változás + nyitott modellkérdés).
Mindhárom a felhasználó vizuális ítéletét igényli; a Core adatút és a
mérőeszköz mindháromhoz készen áll.

**Nem seed-törő:** csak új API-k; a `WorldGeneratorVersion` marad `"5"`, a
hőmodell verziója 3.

**Utómérés (2026-09-29, ND-160 után).** A korábbi A/B a jégmentes **A menet**
éves szelét mérte, miközben a fogyasztó a végleges **B menetet** használná.
A CLI mérőút most a B felszíntípusain ismétli meg a szélgyűjtést, és bitenként
ellenőrzi, hogy a szélrögzítés nem változtatja meg a B éves hőmezőjét.
Ugyanazon level-5 világon: az éves szélállandóság **0,737**, a hőmérséklet
önmagában **89,3%**, hőmérséklet + B-menetes szél **66,9%** szárazföldi
csapadéknegyed-egyezést ad az analitikus alaphoz képest. A szélcsere önmagában
**70,4%** egyezést ad a hőmodell-hőmérsékletes változathoz képest.

Az éves átlagvektor kioltódása csak a szélső eltérések egy részét magyarázza:
a `|átlagvektor| / átlagsebesség` szerinti `<0,25` sávban 31 szárazföldi
cella van, míg a `0,50–0,75` sávban **1374**, és ezek **37,6%-a** is más
csapadéknegyedbe kerül. A kapu ezért zárva marad; az éves vektor helyi
kioltódásának külön kezelése önmagában nem oldaná a széles körű átrendeződést.


### ND-164 — A biome hőmérséklet-tengelye és a párolgás átállása a hőmodellre; a hideg vég a jégosztályból (A7 6. fázis, ELFOGADVA, implementálva)

**Dátum:** 2026-09-28. **Előzmény:** ND-126 (kétdimenziós biome-tábla),
ND-158 (éves adatút, párolgás-overload), ND-159 (percentilis jégküszöb),
ND-160 (bázis-albedó, NYITOTT), ND-162 (a jégmaszk átállása), ND-163
(szélvektor, a fogyasztói kapu ZÁRVA maradt).

**A tétel.** Az ND-163 három külön ítéletre bontotta a fogyasztói átállást.
Ez a döntés a MÁSODIKAT zárja le: a **biome hőmérséklet-tengelye** és a
**csapadék párolgás-tagja** átáll a hőmodell éves éghajlatára. A csapadék
SZELE változatlanul az analitikus úton marad (ND-163, nyitva).

#### 1. A mérés, amiért ez nem puszta átkötés lett

`worldgen thermal-climate --biome true`, seed `0xA7C944210000`, level 5,
2151 szárazföldi tile. A hőmérséklet-tengely tartománya:

| forrás | tartomány |
|---|---|
| mai, analitikus (egyetlen `dayT` 24 mintás napi átlaga) | −92,7 … +51,3 °C |
| hőmodell, éves LEVEGŐ-középhőmérséklet | **−1,7 … +45,9 °C** |

**Ez a lelet állítja meg a naiv átkötést.** A `BiomeClassification` hideg
vége ABSZOLÚT: `IceSheetThresholdK` = −10 °C, `OceanFreezingK` = −2 °C. A
hőmodell éves levegő-átlaga sehol nem megy −1,7 °C alá, tehát **egyetlen
cella sem esik e két küszöb alá**: a jégtakaró-biome és a tengeri jég
NÉMÁN kiürülne.

Ez pontosan az a hibaosztály, amit az ND-159 a jégMASZKRA már eldöntött
(abszolút küszöb helyett percentilis), és a gyökéroka is ugyanaz: az
ND-160 szerinti ~10 K globális melegtöbblet plusz a radiatív simítás
(ND-100 M13), ami a sarki éves átlagot felhúzza.

#### 2. A mért változatok

| változat | szárazföldi egyezés a maival | szárazföldi IceSheet | Tundra |
|---|---:|---:|---:|
| (0) mai: analitikus tengely + analitikus csapadék | — | 7,6% | 9,3% |
| (1) hőmodell tengely, csapadék változatlan | 78,1% | 0,0% | 11,8% |
| (2) + a párolgás hőmérséklete is | 78,4% | 0,0% | 11,9% |
| **(3) + a hideg vég a JÉGOSZTÁLYBÓL** | **78,3%** | **20,0%** | **0,0%** |
| (4) (3) + tundra a szezonális hóból | 36,4% | 20,0% | 48,4% |

**A (4) ELUTASÍTVA, mérés alapján:** a szezonális hó (a napi minimum
fagypontja) túl tág sáv — a szárazföld felét tundrává tenné.

**A DÖNTŐ szám, ami a (3)-at választotta:** a (2) változat 254 szárazföldi
tundra-cellájából **254 (100%)** a jégmaszkon BELÜL van. A viewer
render-kategóriája ott amúgy is `IceSheet` (ND-162 óta a hőmodell
percentilis maszkjából), tehát a (3) a KÉPBŐL NEM VESZ EL SEMMIT —
egyedül a panelt (`biomeOf`) hozza összhangba azzal, ami látszik (I4).

**Mellékhatás, ami valójában javítás:** az ND-59 óta KÉT független
jégréteg élt egymás mellett (a jitterelt maszk és a biome saját, jitter
nélküli `IceSheet`-je), és a nyers, kör alakú biome-jég „átsejlett" ott,
ahol a maszk épp hamis volt. Ezzel egyetlen forrás marad.

#### 3. Ami VESZTESÉG, kimondva: a tengeri jég

| | tengeri jég az óceáni cellák arányában |
|---|---:|
| mai, analitikus | 32/3993 = **0,8%** |
| a hőmodell tengelyével | **0,0%** |

A hőmodellnek — a mai kalibrációval — NINCS fagypont alatti óceánja. Az
analitikus úton csak azért van tengeri jég, mert az a mező −92 °C-ig megy.
**Ezt a bázis-albedó döntése (ND-160) hozza vissza:** a mérés szerint
a=0,30 bolygó-albedóval **64 óceáni cella** lesz tartós jég (1,6%, tehát
kétszerese a mainak). Ez a legerősebb új érv az ND-160 (1) opciója mellett.

#### 4. Amit az ND-160 mérőkampó mutatott

A `ThermalModelParameters.baselineAlbedo` (alapértelmezés `null` = a mai,
bedrótozott felszíni albedó, BITRE változatlan) most mérhetővé teszi az
ND-160 javaslatát. `--baseline-albedo 0.30` mellett:

| | mai bázis | bolygó-albedó |
|---|---:|---:|
| globális medián éves átlag | +33,7 °C | **+18,1 °C** |
| egyezés a régi jégosztállyal | 72,7% | **86,4%** |
| óceáni tartós jég | 0 cella | **64 cella** |
| éves LEVEGŐ-minimum | −1,7 °C | **−1,7 °C** |

**A hideg vég NEM tér vissza** — a minimum bitre ugyanott marad. Az ND-160
tehát a globális SZINTET javítja (és a tengeri jeget visszahozza), de a
biome abszolút hidegvégét NEM: az a (3) úton marad. Ez fontos, mert az
ND-160 javaslata eddig implicit módon azt sugallta, hogy a skála
helyreállításával minden downstream küszöb újra használható lesz.

#### 5. Megvalósítás

**Core** (motorfüggetlen, seed-semleges — csak új API-k):
- `ThermalClimateCalculator.ClassifyBiomes(..., iceClass)` túlterhelés: a
  tartós jég felülír (`IceSheet` / `SeaIce`), minden melegebb cella BITRE a
  jégosztály nélküli eredmény.
- `MoisturePrecipitation.Compute(..., temperatureK, ...)` túlterhelés: a
  paraméteres út is átveheti a párolgás hőmérséklet-mezőjét. `null` esetén
  bitre a régi út (tesztben kikötve).
- `ThermalModelParameters.BaselineAlbedo` mérőkampó (fent).

**Viewer:**
- `useThermalClimateBiome` kapcsoló (alapértelmezés: BE), a jég kapujához
  kötve — egy világban EGY hőmérséklet-forrás legyen.
- `BiomeTemperatureKelvinAt(...)`: SAROK-INTERPOLÁLT éves levegő-átlag. A
  sarok-tábla a TENGERSZINTRE REDUKÁLT hőmérsékletet hordozza
  (`T + lapse·max(0, h_cella − tengerszint)`), a kiértékelés pedig a PONT
  saját elevációjával húzza vissza — ez a `SurfaceTemperatureField.
  AltitudeCorrectedK` szabálya, folytonosan. Nyers tile-lookup helyett
  azért, mert az a referencia-szintű (90–800 km) cellahatárokat tenné
  láthatóvá; a redukció nélküli interpoláció pedig a hegyek hidegét
  szétkenné a völgyekre.
- A párolgás a `ThermalClimate.Refined` éves FELSZÍNI átlagát kapja (a
  párolgás felszíni folyamat), MINDEN cellára — az óceániakra is, mert a
  nedvesség épp ott keletkezik.
- A csapadék-gyorsítótár kulcsa kiegészült a párolgási mezővel
  (referencia-azonosság). **Enélkül csendben elavult mezőt adna:** a
  kulcs eddigi tételes leltára arra épült, hogy a `Compute` a deep time-ot
  „meg sem kapja" — a hőmodell éves átlaga viszont `tYears`-függő.
- A deep-time glaciációs eltolás (ND-44) mostantól a biome tengelyére IS
  rámegy, nem csak a jégre: egy mező táplálja mindkettőt, és ha csak a jég
  mozdulna a csúszkával, a két réteg ugyanazon a képen mondana mást.
- `IsIceMaskTile(...)`: a jégmaszk döntése egyetlen helyen. Ez javított egy
  meglévő eltérést is — a statikus alapréteg még a bedrótozott abszolút
  küszöböt használta, miközben az ND-162 óta a percentilis vágópont az
  érvényes.

#### 6. Élő igazolás (Unity 6, Play mód, level 5)

| mérték | érték |
|---|---|
| éghajlat háttérszálon | **117,9 s** (cache-tévesztés), utána Build **2367 ms** |
| `_adaptiveClimateAirCornerLevel` | **5** (a tengely aktív) |
| jégküszöb | **280,168 K = 7,02 °C** — bitre a CLI percentilis vágópontja |
| párolgási mező | **6144 tile, [−1,7; +45,9] °C** — bitre a CLI tartománya |
| szárazföldi biome-megoszlás | IceSheet 438, Desert 427, Rainforest 414, Grassland 398, TemperateForest 251, Savanna 223, Tundra 0 |
| tengeri jég | 0 (korábban 35) |
| Console-hiba a viewerből | nincs |

A 438 jég-tile a mért 430 percentilis-cella + a határ-jitter — tehát a
kép és a panel ugyanazt mondja.

**Amit az élő futás ELKAPOTT (meglévő hiba, nem ez a változás okozta):** a
`biomeOf` a Build-ben a csapadék-vágópontok KISZÁMÍTÁSA ELŐTT készül
(a geometria-ciklus a 3157. sorban, a vágópontok a 3396.-ban), tehát
minden Build az ELŐZŐ Build vágópontjaival osztályoz — az ELSŐ Build után
pedig `(0, 0, 0)` vágóponttal és 0 csapadékkal, ami a szárazföld 100%-át
`Desert`-nek mutatta a panelen. A második Build után magától helyreáll.
Külön tétel, nem ennek a döntésnek a hatóköre.

#### 7. Amit ez NEM változtat

`WorldGeneratorVersion` és a hőmodell verziója változatlan: a biome
RENDER- és panel-kimenet, nem a generátor numerikus lánca, és a Core
oldalon csak új API-k keletkeztek. A csapadék-mező viszont mostantól
deep-time-függő lett (a hőmérséklet-bemeneten keresztül) — ezt a
gyorsítótár kulcsa követi.

#### 7b. Három hiba, amit a felhasználói visszajelzés hozott elő

A „nem működik, hiába kapcsolgatom" jelzés után három külön ok derült ki.

**(1) A kapcsolók nem szerepeltek a konfiguráció-pillanatképben.** A
`WorldConfigChangedSinceBuild()` sem az új `useThermalClimateBiome`-ot,
sem az ND-162 `useThermalClimateIce`-át nem figyelte, tehát az
Inspector-beli átkapcsolás egyáltalán nem indított Buildet. Az ND-162
kapcsolója emiatt SOHA nem működött. Mindkettő bekerült.

**(2) A domain reload VÉGLEG megölte az éghajlatot — ez a súlyos.** Unity
a Play közbeni script-újrafordításkor az ÉRTÉKTÍPUSÚ mezőket átmenti, a
REFERENCIÁKAT nem. Így az `_climateInputsIdentity` (ulong), a
`_hasClimateInputsIdentity` (bool) és a `_climateRevision` (int) TÚLÉLI, a
`_climatePendingInputs` / `_climateApplied` / `_climateCompleted` /
`_climateTask` viszont `null` lesz. A `CaptureThermalClimateInputs` ezután
az „ugyanaz a világ" ágra futott — hiszen az azonosító tényleg ugyanaz —,
és soha nem fegyverezte újra a bemenetet; az `UpdateThermalClimate` pedig
`inputs == null` miatt sosem indult el. **Az éghajlat a session végéig
halott maradt, hibaüzenet nélkül:** a jég, a biome és a párolgás némán az
analitikus előnézeten ragadt. Élőben megfigyelve: `rev = 5`, változatlan
azonosító, `pending`/`applied`/`completed` mind `null`, és három egymást
követő Build sem mozdított rajta.

Javítás: az „ugyanaz a világ" rövidzár mostantól megkérdezi, van-e BÁRMI
élő (pending bemenet, futó task, kész vagy már átvett eredmény). Ha nincs,
újrafegyverzi a bemenetet REVÍZIÓ-EMELÉS NÉLKÜL — a világ nem változott,
tehát a lemez-cache talál. Élőben: `"éghajlat: újrafegyverezve (domain
reload után)"` → **gyorsítótárból 497 ms** → a hőmodell útja újra aktív.

Az általánosítható tanulság: **Unityben egy értéktípusú „cache-kulcs" és a
hozzá tartozó referencia-állapot külön sorsú a domain reloadon.** A
kulcsra épülő „nincs dolgom" rövidzár mindig kérdezze meg azt is, hogy
egyáltalán VAN-e még mit rövidre zárni.

**(3) Mérési csapda (nem felhasználói hiba).** A `Build()` az elején kilép,
ha LOD-vágás fut (`_cutTask != null`), és csak a
`_fullBuildRequestedAfterCut` jelzőt állítja. Egyetlen evalban futtatott
„ON → OFF → ON" sorozatban ezért a 2. és 3. Build no-op volt, és úgy tűnt,
mintha a kapcsoló nem váltana. A halasztott kérést az `Update()`
teljesíti; a verifikációt viszont egy Build / egy állítás ritmusban kell
végezni.

**Az egyenkénti igazolás:**

| kapcsoló | `configChanged` | sarok-tábla | párolgási mező | szárazföldi biome |
|---|---|---:|---|---|
| OFF | True | **−1** | `null` | IceSheet 175, Tundra 155, SeaIce 45 (analitikus) |
| ON | True | **5** | `SET` | IceSheet **430**, Tundra 0, SeaIce 0 (hőmodell) |

A 430 pontosan a CLI-ben mért percentilis jégcella-szám.

#### 8. Ami NYITVA marad

1. A **vizuális átvétel** — felhasználói ítélet (a tengeri jég eltűnése és
   a jégsapka 7,6% → 20,4% növekedése a szárazföldön a két látható tétel).
2. **ND-160** — most már erősebb érvekkel: visszahozza a tengeri jeget és
   a globális szintet a Földéhez viszi. Seed-törő.
3. **ND-163** — a csapadék szele (nagy változás, 67,7%) és a kioltódó éves
   szél modellkérdése.

*(Utóirat 2026-09-29: az ND-160 LEZÁRVA, az (1) opcióval — a tengeri jég
visszatért (64 cella), a globális medián +18,1 °C, a szárazföldi
biome-egyezés 78,3% → 80,1%. A hideg vég viszont nem mozdult, tehát a
jégosztályos hidegvég marad.)*


### ND-165 — Ugyanaz az albedó-keverés az ANALITIKUS hőmérséklet-úton (MÉRVE a képlet, DÖNTÉS NYITOTT)

**Dátum:** 2026-09-29. **Előzmény:** ND-42 (a teljes analitikus
hőmérséklet-modell), ND-100 (hőmodell-bázis), **ND-160** (a bázis
bolygó-albedója, LEZÁRVA).

**A lelet.** Az ND-160 a hőmodell BÁZISÁT javította. Ugyanaz a keverés
azonban ott van az ANALITIKUS úton is, amit a viewer ma is használ (és amire
a kép nagy része épül):

| hely | kód |
|---|---|
| `Temperature.TemperatureKelvinFromAverageInsolation` | `albedo = isOceanic ? AlbedoOcean : AlbedoLand` |
| `Temperature.TemperatureKelvinFull` | ugyanaz, majd `+ GreenhouseTemperature(...)` (~33 K) |

A szerkezet azonos az ND-160-ban leírttal: a radiatív tag FELSZÍNI albedóval
számol, a hozzáadott ~33 K üvegház-eltolás viszont a 255 K-es, a ≈ 0,30-as
BOLYGÓ-albedós egyensúlyhoz van kalibrálva. Az óceáni radiatív tag ezért
ugyanazon inszolációs faktor mellett `((1−0,06)/(1−0,30))^0,25 = 1,0765`
arányban, azaz ~19–20 K-nel melegebb a kelleténél — pontosan az a többlet,
amit az ND-160 a hőmodellből kivett.

**Amit MÉRTEM (level 5, seed 0xA7C944210000, 20 lemez):** az analitikus út
éves átlaga `[−77,3; +47,9] °C`. A felső vég (+47,9 °C) illeszkedik a
gyanúhoz; a globális MEDIÁNT ezen az úton még nem mértem meg — a
`thermal-climate` mérés az analitikus utat csak a jégosztályhoz futtatja, a
percentilis-bontása nincs kiírva. Ez a döntés első teendője.

**Miért NEM javítottam most.** Három ok, mindegyik a munkarendből:
1. **Egy körben egy numerikus változás.** Az ND-160 hatását most lehet
   tisztán megmérni; ha az analitikus út is mozdulna, a két hatás
   összekeveredne (ez az ND-142 tanulsága).
2. **Az analitikus út a mai KÉP forrása**, és a felhasználó éppen ezt
   találta jobbnak (todo2 A24). Egy ~19 K-es óceáni hűtés a hőmérséklet-,
   jég-, csapadék- és biome-képet EGYSZERRE változtatná meg, mérés és
   vizuális átvétel nélkül.
3. **Az absztrakt küszöbök itt ABSZOLÚTAK.** A `BiomeClassification`
   hőmérséklet-sávjai és a `LakesIceErosion` −15/−2 °C-os küszöbei ehhez az
   úthoz vannak kalibrálva; ezeket a változás után újra kell mérni.

**Opciók:**

**(1) Ugyanaz, mint az ND-160-nál: bolygó-albedó a radiatív tagban.** A két
út fizikailag konzisztens lesz, és a hőmodellre való átállás (ND-162/164)
mérőszámai is értelmezhetőbbek, mert a két oldal ugyanarról a szintről
indul. Seed-törő; a biome- és jégküszöböket újra kell mérni.

**(2) Előbb a hideg vég (A24 (b)), utána ez.** Ha a tartomány
összenyomottsága a hőmodellben megoldódik, kiderülhet, hogy az analitikus
utat egyáltalán nem kell javítani, mert a fogyasztók addigra a hőmodellre
állnak.

**(3) Marad így.** Az analitikus út tudatosan egy „illusztratív", meleg
bolygó, és csak a percentilis-alapú fogyasztók használják.

**Javaslat: (2)** — a sorrend a fontos. Az ND-160 után a következő mérés a
hideg vég oka (β, hőkapacitás, meridionális transzport szétválasztása); az
analitikus út javítása onnan jobban megítélhető. Ha viszont a hőmodellre
való teljes átállás elmarad, az (1) kötelező lesz.


### ND-166 — A hőmodell hideg vége: radiatív simítás és meridionális proxy szétválasztása (A7/A24, DÖNTÉS NYITOTT)

**Utóállapot (2026-10-01):** az alábbi diagnózis transzportágát az
ND-168 implementációja és az ND-171 mérési kapuja továbbléptette. A
pozitív K-proxy az aktív hőmodellben már nincs. Az évszakos simítás és a
percentilis jég kiváltása továbbra is nyitott; az alábbi „következő lépés”
mondatok a 2026-09-29-i történeti állapotot rögzítik.

**Dátum:** 2026-09-29. **Előzmény:** ND-100, ND-126b, ND-159, ND-160 és a felhasználó ND-160 utáni élő ítélete: a `useThermalClimateBiome` kikapcsolva továbbra is jobban fest; a cél a hőmodell fizikai javítása.

**Mért diagnózis** (seed `A7C944210000`, 20 lemez, level 5, 12 mintanap, két menet, bolygó-albedó):

| β | Éves minimum | P5 | P50 | Tartós jég 7%-os vágópontja | Pillanatnyi minimum |
|---:|---:|---:|---:|---:|---:|
| 0,5 (jelenlegi) | −1,7 °C | +5,4 °C | +18,1 °C | +6,07 °C | −33,1 °C |
| 0,3 | −9,9 °C | −0,1 °C | +17,7 °C | +1,18 °C | −55,7 °C |
| 0,2 | −16,4 °C | −4,5 °C | +17,4 °C | −2,46 °C | −71,7 °C |

A talaj effektív mélységének 0,5 → 0,12 m csökkentése β=0,5 mellett az éves minimumot **nem mozdította** (−1,7 °C), de a pillanatnyi maximumot +55,8 → +74,2 °C-ra emelte. Az óceáni mélység 10 → 2,4 m-re csökkentése sem mozdította az éves minimumot. A leghidegebb cella éves átlaga a bázisától legfeljebb 0,1 K-re van: a hideg vég itt **bázisprobléma**, nem a napi hőtehetetlenség hiánya.

A `Temperature.MeridionalHeatTransportK(z) = 40 K · z⁴` tag az ND-126b analitikus hőútját is javította, majd változtatás nélkül bekerült a hőmodell bázisába és a termikus szél gradiensébe. A level-5 rácson a bázishoz területileg súlyozva **+8,00 K-t** ad, a β=0,2 leghidegebb cellájában **+39,90 K-t**. Ez önmagában pozitív hőforrás; egy belső meridionális hőszállítás globális integrálja viszont zérus kell legyen. A fizikai korlátot az [energiaegyensúly-modellek szakirodalma](https://esd.copernicus.org/articles/11/1195/2020/) is kimondja. A hőmérséklethez adott K-eltolás egyébként sem energiafluxus, tehát a nulla átlagra központosítás csak diagnosztikai közelítés lehet, nem automatikus végső megoldás.

**Opciók és sorrend:**

1. A meglévő tag erősségét csak mérőparaméterként változtatni, az alapértéket 1-en tartva. Ez szétválasztja a β és a proxy hatását; a régi világkimenet bitre azonos marad. A szél bázisgradiensét ugyanazzal a paraméterrel kell számolni, a nem alapértelmezett értéknek külön modellazonosító kell.
2. A pozitív K-proxyt energiamegmaradó, rácséleken fluxust szállító taggal felváltani. Ez új numerikus algoritmus, Python-orákulumot, vektorokat és explicit modell-/generátorverzió-emelést kíván. A kalibrációban a sarki éves átlag, a globális energiamérleg és a napi szélsőértékek együtt számítanak; a régi analitikus jégtérkép nem fizikai orákulum (−218,8 °C-os sarki éjszakát adott).
3. A β megváltoztatása önmagában. A fenti mérés szerint javítja a hideg véget, de β=0,2-nél is −2,46 °C-os éves átlagnál kellene a tartós-jég 7%-os vágópontja; a +8 K globális hőforrás megmarad. Ez nem zárja le az ND-t.

**Jelenlegi döntés:** az 1. lépés diagnosztikai megvalósítása, majd több seed és rácsszint A/B-mérése. A fogyasztói csapadékszél-kaput és a percentilis jégküszöböt addig nem nyitjuk át. Az alapértelmezett numerikus viselkedés változtatásához a 2. lépés új, bizonyított döntése szükséges.

**Első A/B eredmény (2026-09-29):** a `meridionalTransportScale` 1,0 alapértéken bitre a régi út; a nem alapérték külön modellazonosítót kap. β=0,2 mellett a skála 1,0 → 0,75 → 0,0 változása a level-5 éves minimumot **−16,4 → −26,4 → −56,3 °C**-ra, a 7%-os jégvágópontot **−2,46 → −9,89 → −31,55 °C**-ra viszi. A globális bázis átlagához a tag rendre **+8 → +6 → 0 K**-t ad. A 0,75-ös beállítás számai ígéretesebbek, de ez továbbra is globális hőforrás; **nem választott végleges modell**. A nullázás pedig megmutatja, hogy a jelenlegi anomália-advekció önmagában nem pótolja a klímabázis meridionális hőszállítását. A 2. opció energiamegmaradó fluxusmodelljének tervezése marad a következő numerikus lépés.

### ND-167 — Konzervatív élfluxus numerikus szerződése (A7, RÉSZDÖNTÉS)

**Dátum:** 2026-09-29. **Előzmény:** ND-166 és a [diffúziós energiaegyensúly-modellek](https://esd.copernicus.org/articles/11/1195/2020/) azon feltétele, hogy a belső hőszállítás globális integrálja nulla.

**Döntés:** a fluxus diszkrét alapegysége egy kanonikus, egyszer bejárt rácsél. Az `i < j` élhez adott nemnegatív, véges `G_e` vezetőképesség (W/K) és a két abszolút hőmérséklet (K) alapján `Q_e = G_e · (T_j − T_i)` (W). Az `i` cella `+Q_e`, a `j` cella `−Q_e` teljesítményt kap; a cella W/m²-forrása `P_c / A_c`, a későbbi hőmérsékleti tendencia `P_c / (A_c C_c)` K/s, ahol `C_c` J/(m² K). Az élsorrend rögzített, a számítás szekvenciális. Azonos bemenetből bitazonos eredmény készül; a két végpontra ugyanaz az egyszer kiszámolt `Q_e` kerül ellenkező előjellel. Az energiamegmaradást a teljesítményösszeg lebegőpontos hibán belüli nullája igazolja. Ez a numerikus mag nem választ `G_e` értéket.

**Határ:** ez csak a fluxus-/divergencia-kernel, nem kész klímamodell. A geometriai `G_e` szabálya, meridionális irányfüggése, együtthatója, stabil időintegrálása, a bázis és anomália viszonya, illetve a felszín/levegő energiafelosztása még nyitott. A jelenlegi +40 K proxy, a generátorverzió, a checkpoint és a viewer kimenete nem változik. Fogyasztói bekötés előtt külön ND-168 döntés, Python KAT és teljes A/B szükséges; a bekötés seed-törő változtatásként verzióemeléssel jár.

### ND-168 — Az éves meridionális hőmérleg bekötése (A7, IMPLEMENTÁCIÓS DÖNTÉS)

**Átvételi kiegészítés (2026-10-01):** az ND-171 szerint az L5/L6
mérlegmaradék-kapu teljesült, három seed fizikai diagnosztikája elkészült.
A felhasználó az aktuális működést/képet rendben lévőnek jelezte. A
tesztek régi vektormásolatait javítottuk; Debug/Release 1976/1976 zöld.
A következő, percentilist kiváltó jégmodell nincs ezzel elfogadva.

**Dátum:** 2026-09-29. **Előzmény:** ND-166/167. A korábbi `40 K · z⁴` proxy a globális bázishoz +8 K-t adott, ezért belső hőszállításként nem tartható fenn. A cél a meglévő kétmenetes éves éghajlat és az órás hőmező közös fizikai bázisa.

**Modell:** a 12 éves ablakban számított, bolygó-albedós radiatív bázis `T₀,c` éves átlaga után az `i < j` éleken `G_e = D · R² · (L_e / d_ij) · (n_e · N_e)²` W/K. Itt `L_e` az él húrhossza, `d_ij` a cellaközéppontok húr-távolsága, `n_e` az élnormál, `N_e` a helyi észak; így a vezetés meridionális irányú, a cubed-sphere lapvarratain ugyanazzal az éllel. `D = 0,555 W/(m² K)` a [climlab meridionális diffúziójának alapértéke](https://climlab.readthedocs.io/en/latest/api/climlab.dynamics.MeridionalHeatDiffusion.html); `λ = 2,09 W/(m² K)` a [Budyko-féle lineáris energiavisszacsatolás közölt értéke](https://esd.copernicus.org/articles/11/1195/2020/esd-11-1195-2020.pdf). Ezek földi kiinduló értékek, nem ebből a generált világból utólag illesztett számok.

Az éves `B_c` mező a `λ A_c (B_c − T₀,c) = Σ_e Q_{e→c}(B)` diszkrét mérleget oldja. A pozitív `G_e` és `λ A_c` miatt a rendszer szimmetrikus, pozitív definit. A korrekció `δ_c = B_c − T₀,c` a teljes éves hőbázishoz adódik; az órás és a szél-bázis ugyanennek a mezőnek a gradiensét használja. Egy él két végpontján ellenkező teljesítmény szerepel, ezért a globális hőmérleg belső tagja nulla. A numerikus megoldó rögzített iterációszámú, kanonikus élsorrendű, előkondicionált konjugáltgradiens-eljárás; nincs tolerancia miatti platformfüggő iterációszám. A szezonális `β = 0,5` simítás és a tickenkénti anomália-solver külön marad; a hőtehetetlenségük későbbi fizikai finomítás tárgya, nem az éves hőtranszporté.

**Kompatibilitás:** az alapértelmezett `40 K · z⁴` tag megszűnik a hőmodell bázisában és termikus szelében. A diagnosztikai skála csak kifejezett legacy A/B-ágon értelmes. Ez seed-törő: hőmodell 4 → 5, generátor 6 → 7, a checkpoint és a lemez-cache eltérő verziót explicit elutasít. Python referencia, kézzel számolt kisrács-KAT, újragenerált vektorok és C# tesztek tartoznak hozzá. A hőmodellből származó csapadékszél fogyasztói bekötése a korábbi nagy A/B-eltérés miatt külön ND-163 kapu marad; az éves hőmérleg lezárása önmagában nem jogosítja fel annak néma aktiválását.

**Átvételi határ:** a numerikus és fogyasztói kódot a felhasználó kérése szerint átadjuk közös utólagos ellenőrzésre. A hőmodell-biome vizuális minősége és az ND-163 szélcsere csak az élő Unity-kép és a felhasználó mérése után minősíthető késznek.

### ND-169 — A végleges éves szél csapadékfogyasztója (A7, IMPLEMENTÁCIÓS DÖNTÉS)

**Átvételi kiegészítés (2026-10-01):** a felhasználó az új hőmodell
általános képét/működését rendben lévőnek jelezte; a kért parancssori
tesztkör ezután lefutott. Részletes csapadék-/felhő-A/B mérés ebből nem
következik. Az alábbi utólagos futtatásra váró állapot az átadás története.

**Dátum:** 2026-09-29. **Előzmény:** ND-163, ND-164 és ND-168. Az ND-163 A/B a régi radiatív bázison nagy csapadékváltozást adott; a szél használata ezért az új fizikai bázissal együtt kap külön vizuális átvételt.

**Döntés:** a kétmenetes hőklíma végleges B menetében a már számolt cellaközépponti szélvektor és az átlagsebesség éves átlaga is tárolódik. Ha a viewer ugyanennek a klímának a hőmérsékletét használja a párolgáshoz, a csapadék nedvesség-advekciója is ennek a B menetnek a szelét kapja. A `SurfaceWindSample` vektorátlag és sebességátlag külön mennyiség marad; nem képezzük egyikből a másikat. Cache-találatkor azonos mezők olvashatók vissza, ezért a lemezformátum verziója emelkedik. Az analitikus előnézet továbbra is együtt használ analitikus hőmérsékletet és szelet. A hőklíma-kapcsoló kikapcsolt állapotában a régi út változatlan.

**Átvételi kapu:** a korábbi ND-163 mérés alapján a változás nagy lehet. Az A7 technikai bekötése elkészült; elfogadott vizuális kimenetet és fizikai pontosságot a felhasználó utólagos Unity-ellenőrzése nélkül nem állítunk. A parancssori buildet, teszteket és KAT-ot is a felhasználó végzi az átadás után.


### ND-170 — A24 fizikai átvételi diagnosztika (MÉRÉSI DÖNTÉS)

**Dátum:** 2026-10-01. Az ND-168 implementációja önmagában nem igazolja
a hideg tartományt. Az alapmodell vagy a jégküszöb következő változtatása
előtt a CLI a tényleges éves célmezőből és korrekcióból közölje a
`λ δ − P(T₀+δ)/A` cellánkénti maradék maximumát (W/m²), a transzport
globális integrálját és a korrekció területi átlagát. A nullára központosított
korrekció önmagában nem bizonyítja a megoldó konvergenciáját.

A klímastatisztika területtel súlyozott felszíni/levegő-átlaga, féltekénkénti
sarki átlaga, valamint a fagypont feletti éves átlagú tartós-jég területe
kerüljön a mérésbe. A régi analitikus jégdarabszám összehasonlítás,
nem fizikai cél. Az abszolút −15 °C-os szabály külön kétmenetes futás,
mert az A menet jégmaszkja visszahat a B menetre.

Ez kizárólag megfigyelés: nincs új alapparaméter, jégküszöb vagy numerikus
műveleti sorrend a szimulációban, ezért nincs új verzióemelés. A
`ThermalBaseline.AnnualTargetK` a már kiszámított célmező olvasható nézete;
a CLI nem épít saját hőmodellt. A fizikai és vizuális átvétel nyitott.

### ND-171 — A24 abszolút jégküszöb és szezonális simítás a konzervatív transzport után (MÉRT RÉSZDÖNTÉS)

**Dátum:** 2026-10-01. Az ND-170 mérése után az ND-168 numerikus
transzportja level 5/6-on átvehető a 0,01 W/m² maradékkapu szerint:
maximum `1,42e-9` / `0,0038911 W/m²`. A belső teljesítmény globális
területre osztott összege mindkét szinten `3e-14 W/m²` alatt van.
Ez a diszkrét mérleg ellenőrzése, nem a teljes klíma fizikai hitelesítése.

Seed `A7C944210000`, 20 lemez, 12 mintanap, kétmenetes éves modell:

| β | Tartós-jég szabály | Éves Ts minimum | Területi Ta átlag | Pillanatnyi Ts min / max | Tartós jég |
|---:|---|---:|---:|---:|---:|
| 0,5 | 7. percentilis, L5 | −11,7 °C | 9,049 °C | −40,2 / 41,6 °C | 430 cella |
| 0,5 | −15 °C, L5 | −11,8 °C | 9,048 °C | −40,0 / 42,4 °C | 0 cella |
| 0,3 | −15 °C, L5 | −15,4 °C | 7,825 °C | −61,2 / 57,6 °C | 5 cella |
| 0,2 | −15 °C, L5 | −18,3 °C | 6,931 °C | −76,1 / 66,7 °C | 140 cella |
| 0,5 | 7. percentilis, L6 | −11,9 °C | 9,019 °C | −40,9 / 41,9 °C | 1720 cella |

Két további seed (`A7C944210001`, `A7C944210002`) L5 minimuma −8,2 /
−12,4 °C, területi Ta átlaga 13,111 / 8,921 °C. Mindhárom seeden a
percentilis tartós jég éves felszíni átlaga fagypont alatti. A seedfüggő
éghajlati ciklus eltér, ezért a 9 °C globális átlag nem hasonlítható
közvetlenül egy fix földi 15 °C-os célszámhoz.

**Rész-döntés:** az A24(c) nem zárható le a régi −15 °C küszöb
visszaállításával. A β csökkentését sem választjuk pusztán azért, hogy
jeget adjon: egyszerre módosítja a globális átlagot és a napi szélsőségeket.
Az alap β=0,5 és a percentilis átmenetileg marad; nincs seed-verzióváltás.
Az új hőút vizuális működésére a felhasználó „minden rendben” visszajelzést
adott. Ez nem a következő, még el nem készült küszöbváltás elfogadása.

**A következő fizikai döntés nyitott:** szárazföldön a hófelhalmozódás és
az olvadás éves mérlege, tengeren külön fagyási/olvadási feltétel kell.
Az [NSIDC összefoglalója](https://nsidc.org/learn/parts-cryosphere/glaciers/science-glaciers)
szerint a jég fennmaradását a felhalmozódás és veszteség egyenlege dönti
el; egyetlen éves hőmérsékleti vágópont ennek proxyja. A mostani csapadék
MVP-skálája és 12 napos hőmintavétel nem jogosít fel kg/m²/év mérleg
állítására. Előbb egység-/kalibrációs és évszakos mintavételi terv, majd
Python referencia és explicit verzióváltás szükséges. Korlátozott, csak
hőmérsékleti szezonális proxy külön, névvel vállalt egyszerűsítés lehet;
ezt nem nevezzük tömegmegmaradó jégmodellnek.

### ND-172 — A24 pozitív foknap és hóolvadási potenciál (DIAGNOSZTIKA)

**Dátum:** 2026-10-02. A szárazföldi jég következő mérési lépése a tényleges
levegő-hőmérsékletből számított pozitív foknap: `max(Ta−273,15; 0) × dt`,
ahol `dt` 86400 SI-másodperces napban értendő. A meglévő solver mintanapjain
minden tick elejét mintázzuk, majd a minták összegét `keringési nap / mintanap`
súllyal évesítjük. Ez mintavételes becslés; 12/24/48 nap összevetése szükséges.
A napi átlag pozitív részének vétele elveszítené a nappali olvadást.

A külön, tiszta hómérleg-kernel explicit bemenete a kezdeti hó és a lépés
elején hozzáadott havazás (m vízegyenérték), a foknap (K·nap), illetve az
olvadási tényező (m vízegyenérték / K·nap). A tényleges olvadás a készlet és
a potenciál minimuma. Nincs visszafagyás, firn vagy csupaszjég-olvadás.
A diagnosztikai viszonyítás `0,003 m/(K·nap)` hóolvadási tényező;
a [PISM dokumentáció](https://www.pism.io/docs/climate_forcing/surface.html)
3 mm **folyékony vízegyenérték** / pozitív foknap alapértéket közöl.
Ez empirikus hőmérsékleti modell, nem teljes energiamérleg vagy PISM-port.

A CLI a jelenlegi tartós **szárazföldi** jégcellák hóolvadási potenciálját
méri. Ez nem tényleges éves veszteség vagy előírt havazás: az időzítés és
a készlet korlátozhatja az olvadást. A jelenlegi csapadékproxy nem válik
hallgatólagosan m/év adattá. A tengeri jég külön fizikai feladat marad.
Python KAT és generált vektor előzi meg a C# kernelt. A mérés nem fogyasztója
a világmodellnek: a jégbesorolás és a generátorverzió változatlan.

### ND-173 — A24 nyári hőmérséklet tagonkénti vizsgálata (MÉRÉSI DÖNTÉS)

**Dátum:** 2026-10-02. Az ND-172 pozitív foknap-mérése után a következő
diagnosztika minden cella legmelegebb mintanapjához megőrzi a nap indexét,
a bázis átlagát és a levegő-anomália átlagát. A két utóbbi összege a
nap levegőátlaga; a rekonstrukció eltérését teszt ellenőrzi.
A CLI a jelenlegi szárazföldi jégmaszk legmelegebb celláján közli a
helyet, a magasságot, a radiatív részt, az üvegház-, éves transzport-,
magassági és ciklustagot. A radiatív rész a tényleges bázisból maradékként
származik; szárazföldön nincs óceáni puffer. Nem alkotunk második hőmodellt.

Megvizsgálandó: (1) a magas nyári hő a bázisból vagy az anomália-solverből
ered-e; (2) az éves transzport állandó korrekciója mennyit ad nyáron;
(3) a változó bázisra mennyiben hat a solver hőkapacitása és jégalbedója.
Ezekből csak mérés után következhet új szezonális modell. A megfigyelés
nem módosít szimulációs sorrendet, alapparamétert vagy verziót.

Kontrollként ugyanazt az ND-168 egyensúlyi megoldót a kiválasztott nap
átlagos, éves transzport nélküli bázisára is lefuttatjuk, és fél keringéssel
később megismételjük. Ez **pillanatnyi egyensúlyi kontroll**, nincs benne
hőtárolás; nem tekintjük kész évszakos klímának. A cellás maradék és a
korrekció területi átlaga a kontrollban is mérendő.

**Kódból igazolt szerkezeti korlát:** a solver `T = B(t) + θ` alakban csak
a `θ` anomáliát lépteti. A `LocalStep` nem von le `C dB/dt` tagot, ezért
a teljes hőmérsékletben a bázis változása hőkapacitástól függetlenül jelenik
meg. Ez az eredeti anomáliamodell működése, de nem időfüggő energiamérleg
a teljes hőmérsékletre. A felszíni jégalbedó csak a napi anomália
`F(1−a)(cos(z)−f_napi)` forrásában szerepel; a szárazföldi szezonális
bázist nem hűti. A mélység vagy a jégalbedó önmagában nem javíthatja a bázist.

A következő implementáció iránya ezért **időfüggő szezonális energiamérleg**,
amelyben a hőtárolás, a sugárzási forrás és a konzervatív transzport ugyanazt
a hőmérsékletmezőt kezeli. A [climlab EBM dokumentációja](https://climlab.readthedocs.io/en/stable/api/climlab.model.ebm.html)
ilyen `C ∂T/∂t = elnyelt rövidhullám − kimenő hosszúhullám + diffúzió`
szerkezetet ír le. A saját megoldásban külön tisztázandó a felszíni és
bolygó-albedó kapcsolata, az állandó magassági/ciklustag helye, valamint
a periodikus állapot determinisztikus előállítása. A napi egyensúlyi
kontroll nem kerül automatikusan a viewerbe. Az aktív átállás későbbi
numerikus döntést, Python-orákulumot és verzióemelést igényel.

### ND-174 — A24 periodikus szezonális energiamérleg (LEZÁRVA)

**Dátum:** 2026-10-02. Az ND-173 után az aktív bázis a teljes szezonális
hőmérsékletre felírt lineáris energiamérlegre áll át:
`C dT/dt = S(t)(1−a) − A − B T + P(T)/terület + B(ciklus−Γh)`.
Itt T Celsiusban, C J/(m² K), a fluxusok W/m²-ben értendők. C a felszín
és a modellezett levegőoszlop hőkapacitásának összege. A változó bázishoz
nem adódik újra éves, állandó transzportkorrekció vagy +33 K üvegháztag.

Sugárzási kiindulás: [climlab EBM forrás](https://climlab.readthedocs.io/en/stable/_modules/climlab/model/ebm.html),
A=210 W/m²; B az ND-168-ban rögzített 2,09 W/(m² K); D=0,555 W/(m² K).
A jégmentes bolygó-albedó `0,30 + 0,078 P₂(sin szélesség)`, a jeges
bolygó-albedó 0,62. Ezek effektív légkör-tető albedók, nem a napi solver
felszíni albedói. A beérkező sugárzás a meglévő determinisztikus pályából
származik. A régi β-simítás az új bázisban nem helyettesít hőtárolást.

A periodikus évet egyenletes fázisokra bontjuk, visszalépő Euler időalakkal.
A ciklikus időrendszert diszkrét Fourier-felbontás diagonalizálja; minden
harmonikusra ugyanaz a konzervatív rácsoperátor és a komplex tárolási tag
oldandó meg. Így nincs kezdeti év vagy seed-/lekérdezéssorrend-függő spin-up.
A Python-referencia, analitikus egy-/kétcellás KAT, energiamérleg-maradék,
fáziskésés, időfelbontás-vizsgálat és C#-vektorteszt előzi meg az aktiválást.
A szél a megoldott szezonális mező gradiensét kapja.

A jégbesorolásból megszűnik az előírt percentilis. A szárazföldi hó és a
tengeri jég feltétele külön döntési/tesztkaput kap az új hőmező mérése után.
Aktiváláskor generátor 7→8, hőmodell 5→6; a régi csomagok/checkpointok
explicit verzióhibával, a viewer-cache-ek új identitással kezelendők.

### ND-175 — A24 fizikai hó/jégbesorolás, percentilis nélkül (LEZÁRVA)

**Dátum:** 2026-10-02. Szárazföldön a tartós jég éghajlati feltétele az
éves havazás és a pozitív foknapból számított potenciális hóolvadás pozitív
egyenlege. Ez felhalmozódási hajlam, nem jégvastagság vagy gleccseráramlás.
A havazás aránya 0 °C alatt 1, +2 °C felett 0, közte lineáris; az ND-172
hóolvadási tényezője 0,003 m vízegyenérték/(K·nap). Az évfázisokban a
szezonális levegőbázis az input, a napi ingadozás alatti olvadást ez a
klimatológiai közelítés nem oldja fel. A 24/48/96 fázisfinomítás tesztje és az L3 48/96 mérés elkészült.

A csapadék térbeli eloszlását a meglévő nedvességtranszport adja, az adott
menet éves hőmérsékletéből és szeléből. Külön, explicit kalibráció alakítja
m vízegyenérték/év értékké: a teljes bolygó területi átlaga 0,97 m/év,
nulla proxyforrás esetén mindenütt nulla. Kiindulás a [NASA vízkörforgási
összefoglaló](https://science.nasa.gov/earth/earth-observatory/the-water-cycle/)
495000 km³/év fluxusa a Föld ~510 millió km² felszínére vetítve.
Ez földszerű referencia-paraméter, nem mért bolygóadat. Az éven belüli
csapadék egyenletes; a hó/rain megoszlás követi az évszakos hőt.
A korábbi dimenzió nélküli csapadékot nem címkézzük át visszamenőleg.

Vízen külön, konzervatív termikus feltétel: tartós fagyási potenciál akkor
van, ha a legmelegebb évfázis is a fagyáspont alatt marad (tenger −1,8 °C,
édesvíz 0 °C). Ez nem tengerijég-vastagságmodell. A befagyott tengeri cella
évszakos hőkapacitása megtartja az alatta levő óceáni réteget.

A két rögzített A/B menet marad. A maszkhoz külön, **K egységű
jégfennmaradási mérlegjel** készül: szárazföldön
`(olvadási potenciál−havazás)/(olvadási tényező × év napjai)`, vízen
`legmelegebb fázis−fagyáspont`. Negatív jel tartós jeget jelent.
Ez nem hőmérséklet; a viewer hő/párolgás/talaj panelje továbbra is a valódi
hőmezőt olvassa. Cache és render bemenet külön mezőben viszi a jelet.
Nincs előírt jégarány, és egy meleg/száraz világ jogosan lehet jégmentes.

### A többi nyitott döntés

| ID | Kérdés | Javaslat | Mikor |
|---|---|---|---|
| ND-02 | Szimuláció bázis-LOD: fix vagy adaptív | Fix level 6; LOD csak lekérdezésre/renderre | M2 |
| ND-03 | Köztes időpont interpolációja | Engedett, HUD-on jelölve | M10 |
| ND-04 | Timestep-invariancia toleranciái | `docs/01-architecture.md` §9 kiindulásnak, M10 után revideálni | M10 |
| ND-06 | Névgenerálás módszere | Szótag-templétek + hangulati készlet a feature tulajdonságaiból | M8 |
| ND-08 | Óceáni áramlatok | 1.0-ban egyszerűsített gyre-modell M6-tól (a ciklonokhoz kell) | M6 |
| ND-09 | Ordinális kvantálás referencia-eloszlása | Előre kalibrált, ~1000 világból, verziózva | M8 |
| ND-10 | Biome-küszöbök bolygóparaméterrel skálázva? | Nem az 1.0-ban; fix Föld-küszöbök, de konfigban | M5 |
| ND-11 | Feature-identitás perzisztálása | Checkpointolt, a többi layerrel | M8 |
| ND-12 | Kettőscsillag: kettős árnyék renderelése | Igen, 2 fényforrás támogatása | M3 |
| ND-13 | Neurális textúra-réteg | Csak M13 után; előbb A1–A5 (olcsóbb, többet ad) | M13 |
| ND-14 | Neurális determinizmus | Bake + tartalom-hash a package-be | ND-13-mal |
| ND-15 | Neurális modell forrása | Kész alapmodell + ControlNet | ND-13 után |
| ND-16 | Neurális sütés hatóköre | Progresszív, zoomra — a teljes level 12 nem fér el | ND-13-mal |
| ND-17 | Denoise strength plafon | 0.35, elevation-korrelációs teszttel | ND-13 után |
| ND-18 | Erózió cél-LOD | 12 | M7 |
| ND-22 | Core assembly-izoláció | netstandard2.1, nulla motor-referencia | **Érvényben** |

### ND-165 végrehajtási döntés — 2026-10-02

Az (1) opció végrehajtása: a két analitikus radiatív képlet bolygó-albedót
használ, mert a hozzáadott üvegház-tag erre a mérlegre van kalibrálva.
A felszíni albedó a hőmodell napi felszíni fluxusában marad. Az analitikus
mező gyors előnézet és történeti összehasonlítás; a kész éghajlat jégmaszkját
az ND-174/175 energiamérlege adja. A generátor 8-as verziója ezt is lefedi.
A Python referencia és a belőle újragenerált vektorok előzik meg a C# portot.
Az előnézet óceáni hűlése tudatos, nem a régi túlmelegedéshez igazított küszöb.

### ND-174/175 kiegészítés — numerikus és fogyasztói szerződés

A COCG komplex bilineáris szorzata nem nulla reziduumnál is degenerálódhat;
a rövid évű L1-es teszt ezt ténylegesen előidézte. A Python/C# megoldó
ilyenkor nulláról induló Jacobi-tartalékutat használ, legfeljebb 8192 lépéssel.
A rendszer szigorúan diagonáldomináns; az elfogadást továbbra is a teljes,
függetlenül újraszámolt fizikai reziduum dönti el. A relatív leállási kapu
mellett 1e-11 W/m² abszolút kapu védi a közel nulla Fourier-forrást.

A deep-time globális hőeltolás a szezonális forcing része, a viewer nem
adja hozzá ismét. A cache WGTC0003 / climate_k3 külön tárolja a jég
fennmaradási jelét és a fizikai mód jelzőjét. A jel nem hőmérséklet:
a regolit, párolgás és biome hőtengelye valódi K értéket kap. A fizikai
jégmaszkra nincs dekoratív határzaj; a hideg, de száraz cella nem válhat
jéggé a biome abszolút hőmérsékleti fallbackjén. A csapadék-adapter kizárólag
az igazolt, 1e-12 alatti negatív kerekítési maradékot képezi nullára.


**2026-10-02 lezárás (ND-165/174/175):** a teljes A24 implementálva és ellenőrizve; 1995/1995 teljes regresszió és 1/1 külön L6 jéghatárteszt Debug/Release, Python KAT és byte-reprodukció, tényleges Unity Play / hideg és meleg cache / HDRP-kép. A modellhatárok és mérések: [A24 lezárási napló](../history/2026-10-02-a24-seasonal-physical-ice.md). Az ND-165 fenti nyitott állapota történeti; az (1) opció elkészült. A csapadék 0,97 m/365,25 nap referencia, más évhosszhoz időarányosan skálázva.

### ND-176 — Klímaazonosság ellenőrzése a Build fogyasztói előtt (HIBAJAVÍTÁS)

2026-10-02, A8 élő átvétel: a Build 63 hiányzó tile-kulcs hibát jelzett.
A fizikai jégjel teljes rácsú; a `MeanSurfaceK` csak szárazföldi térkép,
a `SurfaceAllK` viszont minden cella valódi éves felszíni hőmérséklete.
A fizikai adapter ez utóbbit olvassa. A klímabemenet azonosítóját a
hidrológia után, a jég/biome/regolit fogyasztása ELŐTT ellenőrizzük.
Korábban az ellenőrzés a Build későbbi részében volt: más időpont vagy
rácsszint kezdetben a régi klíma revízióját érvényesnek láthatta.
Eltérő világnál a régi adat érvénytelen, az analitikus előnézet fut,
majd az új klíma egyszer kér teljes újraépítést. Ez viewer-életciklus és
adapterjavítás; Core-numerika, random és generátorverzió nem változik.
A folyóhálózat vizuális átvétele továbbra is elutasított/nyitott.

Ellenőrzés: offline viewer Release fordítás és tényleges Unity batch
Build-regresszió külön projektmásolaton zöld (L5/0 → L4/22 Myr, régi
fizikai klímával). A felhasználói Play-jelenet és a folyólátvány új
ellenőrzése ettől még hátra van.

### ND-186 — Folytonos folyó-nyomkövető v2: az ND-180 „C" opciójának 1. köre

2026-10-03. Az ND-180 három MÉRT modellhibát azonosított a megjelenített
(folytonos) folyóhálózatban, és a „C" opciót javasolta: valódi térbeli
közelségvizsgálat + terephez kötött folytonos lejtésirány + medencehű
escape-kezelés, külön Python-orákulummal, majd C# porttal. Ez a bejegyzés az
1. kör: **mindhárom HIBAOSZTÁLY javítva**, a módszer a projekt munkarendje
szerint (referencia → verifikálás → tesztvektorok → C#).

**Amit ez a kör NEM tartalmaz, kimondva:** a „C" opció harmadik mechanizmusát
(„vízszint-/medencehű escape-kezelés") csak GEOMETRIAILAG valósítja meg — a
nyomvonal a feltöltési szint alatt futó sima vonal lesz a rácslépcső helyett.
Azt, hogy egy feltöltődő medence helyén TAVAT kellene rajzolni és a folyót a
kifolyásnál folytatni, szándékosan nem döntöttem el: ez az **ND-187**.

**Új Python-orákulum.** `tools/reference/river_continuous_ref.py` — a
folytonos követőnek 2026-10-02-ig NEM volt orákuluma (a `river_path_ref.py` a
durva, TileId-rácson futó változatot írja le). Az orákulum SZINTETIKUS,
analitikus domborzatokon fut (lejtő sík, parabolikus falú völgy, zárt medence
számolt peremmel, két párhuzamos meder közeli és távoli távolságon) — a valódi
elevációs láncot (domain warp + lemezkeret + erózió) a Python nem tudja bitre
reprodukálni, a hibaosztályok viszont ezeken a terepeken pontosan előállnak.
A C# követő ezért `IElevationSampler`-rel paraméterezhető (generikus,
`struct`-ra kötött — a JIT devirtualizálja, nincs delegate-költség), tehát az
orákulum UGYANAZT a kódot méri, amit a termék futtat.

**(1) Iránykvantálás → folytonos lejtésirány.** A v1 a `ringDirections` (8)
jelölt irány közül a legjobbat választotta, és abba lépett: a lépésirány 45
fokos rácsra volt kvantálva (mérve: 1422 darab 30 foknál nagyobb irányváltás a
0. ágon). A v2 a jelölt-kör ELSŐ HARMONIKUSÁBÓL számol gradienst
(`g = Σ_k (e_k − átlag)·d_k`), és annak ellentettjébe lép. Ha a folytonos irány
nem lejt, visszaesik a legjobb jelölt-irányra — a szigorú lejtés-kapu tehát nem
gyengül, és a `maxSteps`-mentes, természetes lezárás (Ocean/Pit/Merged) megmarad.
Mérve az orákulumban: a kvantált követő a szintetikus völgy MEDRÉT SEM találta
meg (565 km-es út a völgy mellett), a folytonos irány bekonvergál a mederbe
(560 km), a laterális eltérés lépésenként kb. negyedére csökken.

**(2) Hamis összefolyás → valódi térbeli közelségvizsgálat.** A `claimed`
térkép EGY pontot tárolt egy TELJES finom tile-ra (level 9, 13–18 km), és a
beleolvadó folyót erre zárta. Ebből két független hiba jött: a mért **24,113
km-es** összefolyási „teleport", és a RÁCS-LOTTÓ (az összefolyás a cella
illeszkedésén múlt, nem a távolságon — az orákulumban mérve: két, egymástól
2968 m-re futó párhuzamos meder NEM kapcsolódott össze, mert a cellahatár épp
közéjük esett). Az új `ClaimedRiverPoints` cellánként a pontok LISTÁJÁT tárolja,
és csak `mergeRadiusMeters`-en belüli valódi pontra zár, a LEGKÖZELEBBIRE. A
záróél hossza így strukturálisan korlátos, nem mérési tapasztalat. A
bucket-rács szintjét nem a hívó adja meg, hanem a toleranciából számoljuk,
hogy a 9 próbapontos keresés lefedése bizonyíthatóan álljon (ld. az osztály
doksijának lefedettségi érvelését).

A tolerancia értékét **MÉRÉS** döntötte el, nem becslés: a valódi t=0
hálózaton söpörve 100 m → 14 összefolyás, 250 m → 14, **500 m → 18**,
2000 m → 18 — a szám tehát **500 m-nél telítődik**, fölötte már csak a záróél
nyúlik. Ez egybeesik egy fizikai érvvel is: 500 m a követő saját érzékelési
sugara, vagyis az a lépték, amin a modell a domborzatot megítéli. Kimondott
következmény: a záróél legfeljebb ~500 m (mérve 0,499 km) — ez a tracer
érzékelési léptékén belül van, a javítás előtti 24,113 km-hez képest viszont
nagyságrendekkel kisebb.

**(3) Finomítatlan escape → „víz alatti" egyenesek.** A lokális
priority-flood 2000 m-es rácson találja meg a túlcsordulási pontot, és a v1 a
rácsútvonalat KÖZVETLENÜL fűzte a nyomvonalhoz: 2,0 / 2,828 km-es élek, 45
fokos lépcsők (a t=0 hálózat hosszának **58,77%-a** 75 m-nél hosszabb éleken).
A javítás NEM dekoratív simítás: a flood útvonala definíció szerint a
`lakeLevel` (az útvonal legnagyobb nyers elevációja) alatt marad, vagyis a
medence feltöltődése után VÍZ ALATT van — ott a fizikai vízfelszín sima, a rács
lépcsője a rács mellékterméke. Ezért ahol egy egyenes (nagykör) szakasz MINDEN
mintapontja a `lakeLevel` alatt marad, ott az egyenest emittáljuk; ahol az
egyenes kibukkanna a vízből, ott a rácsútvonal részletei megmaradnak. A döntés
tehát mért, nem feltételezett. Mellékhatásként megszűnt egy nulla hosszú él is
(a v1 a pit pontot kétszer vette fel).

A kiírt pontok SŰRŰSÉGE külön, szintén mért paraméter (`escapeEmitMeters`,
250 m). Először a normál lépésközre (50 m) mintavételeztem: a t=0 hálózat
pontszáma **415 293 → 954 598 (+130%)**, a csúcsmemória **117 → 255 MB**, azaz
a mesh-előkészítés 2,3-szorosára nyúlt volna — holott az escape-szakasz
ALAKJÁRÓL a 2000 m-es döntési rácsnál finomabb mintavétel nem ad új
modell-információt (az a szakasz „víz alatti", azaz sima), és az ALAK
mérőszáma (30° feletti irányváltások) 250 m-en ugyanolyan jó: 686 vs 690.
250 m-rel a pontszám 504 060 (+21,4%) és a hálózatidő a v1 ALÁ került.
**Ezzel egy ND-180-as mérőszámot visszavonok:** a „75 m-nél hosszabb élek
hossz-aránya" (58,77%) 250 m-es emisszióval definíció szerint visszatér
(57,37%), de ez most a szándékolt viselkedést jelzi, nem lépcsőt — az alak
mérőszáma innentől a 30° feletti irányváltások száma és a legnagyobb él.

**Két csendes I1-sértés is megszűnt a folyó kritikus útján.** (a) A
jelölt-irányokat a v1 `Math.Cos`/`Math.Sin`-nel állította elő, ami az ND-23
táblázata szerint nem garantáltan bitpontos; a v2 `RiverDirectionTable`-je
szögfelezéssel (`normalize(a+b)`, csak `+`, `/`, `sqrt`) építi a táblát, ami
ráadásul bitre szimmetrikus (a k. és a k+n/2. irány egymás pontos ellentettje).
(b) A hurok-védelem és a `claimed` keresés minden lépésben
`TileGeometry.FromPosition`-t hívott, ami `Math.Atan`-t használ — az ND-24
EXPLICITEN kizárja a szimuláció kritikus útjáról. A v2 helyette a
`CubeFaceLattice`-t használja: nyers kocka-projekció, kizárólag osztás és
összehasonlítás, tehát bitpontos. Terület-kiegyenlítésre egy hash-rácsnak nincs
szüksége, a garantált ALSÓ cellaméret (`R / 2^level`) viszont ismert, és erre
épül a (2) lefedettségi érvelése.

**MÉRVE a valódi t=0 hálózaton** (96 ág, level 5, 4 worker; a „v1" oszlop az
ND-180 alapvonala ugyanerre a seedre):

| Mérőszám | v1 | v2 | változás |
|---|---|---|---|
| legnagyobb él | **24,113 km** | **0,499 km** | −98,0% |
| legnagyobb összefolyási záróél | **24,113 km** | **0,499 km** | −98,0% |
| 30° feletti irányváltás (mind a 96 ág) | **93 404** | **673** | −99,3% |
| legrosszabb egyetlen ág | 3 292 | 26 | −99,2% |
| 0. ág irányváltásai | 1 422 | **9** | — |
| összefolyás / Ocean / Pit | 19 / 66 / 11 | **18 / 67 / 11** | ~változatlan |
| teljes hossz | 48 879,793 km | 46 641,008 km | −4,6% |
| pontszám | 415 293 | 504 060 | +21,4% |
| hálózatidő (4 worker) | 71 950 ms | **60 831 ms** | −15,4% |
| csúcs working set | 116,9 MB | 136,0 MB | +16,3% |

**A fa MEGMARADT**, és ezt szándékosan külön ellenőriztem, mert a valódi
távolságvizsgálat elvileg szétszedhette volna: az összefolyások száma 19 → 18,
a zsákutcák száma pontosan ugyanannyi (11). (A `todo2.md`-ben szereplő
„43 → 32 összefolyás" egy KORÁBBI, más kísérlet száma volt; a tényleges v1
alapvonal az endpoint-fájlból 19.) A fa MÉLYSÉGE külön kérdés, és annak oka a
forrás-kiválasztás — ezt már az ND-124 is kimondta.

A javítás egy tesztet elbuktatott, és ez tanulságos: a `DischargeWeightsAreIdentical`
16 forrásos KICSI világában a valódi közelségvizsgálattal NULLA összefolyás van
— és MÉRVE 2000 m-en sem lesz több, tehát nem a tolerancia a szűk
keresztmetszet, hanem az, hogy 16 globálisan szétszórt forrás nyomvonala soha
nem fut egy mederbe. A tesztet nem lazítottam, hanem szétválasztottam: az
egyezést és a záróélek korlátosságát két toleranciával mérem (a súlyok összegét
egy független azonossággal, ami LÁNCOKRA is igaz), a lánc-akkumulációt pedig
egy ÚJ, szintetikus fán futó teszt (`DischargeWeightsAccumulateMultiLevelChains`)
— egy akkumulációs szabály tesztje ne múljon azon, hogy a terep ad-e éppen
mellékfolyót.

Tisztességesen jelezve: a `MoreSourcesProduceMoreConfluences` világában (level 6,
GLOBÁLIS forrás-kiválasztás) az összefolyás 12 forrásnál 1 → 0, 48-nál 15 → 10.
A teszt zöld (10 > 0), de ott a fa ritkább lett. A termék útján (per-medence
forrás-kiválasztás, 96 ág) viszont 19 → 18 — a különbség maga is az ND-124
melletti érv: a per-medence kvóta azért ad fát, mert a forrásokat közös torkolat
felé tartó ágakra teszi.

**Kompatibilitás.** Mindhárom javítás numerikus, tehát minden folyóhálózat
(nyomvonal, összefolyás-fa, vízhozam-súly) új. A generátorverzió **8 → 9**;
a régi `worldpkg` elutasítása és a hőmodell-gyorsítótár érvénytelenítése
ugyanezen a verzión múlik (a cachekulcs a generátorverziót tartalmazza).
Folyó-specifikus lemez-cache nincs. Ez NEM A8-teljesítmény-optimalizálás: az
A8 bitazonossági állításai a v1-re szóltak, a v2 szándékosan más hálózat.

**Ami tudatosan NYITOTT marad (ND-187).** A mért 58,77%-os escape-arány azt
jelenti, hogy a folyók a hosszuk több mint felét zárt medencéken átvágva
töltik. Az ND-186 ezt GEOMETRIAILAG kezeli (sima, víz alatti vonal a lépcső
helyett), de a MODELLKÉRDÉST nem dönti el: egy ilyen medence fizikailag TÓ
lenne, és akkor nem folyót, hanem tavat kellene rajzolni, a folyót pedig a tó
kifolyásától folytatni. Ez külön, mért döntés (tófelület-modell, part, a
LakesIceErosion-nal való összevetés), nem oldjuk meg csendben ebben a körben.

### ND-187 — Zárt medence: folyó vagy tó? (LEZÁRVA 2026-10-03: A+ jelölt tavi szakasz)

2026-10-03. Az ND-180 mérése szerint a t=0 hálózat hosszának **58,77%-a** 75
m-nél hosszabb (escape-) éleken van, vagyis a folyók a hosszuk több mint felét
zárt medencéken átvágva töltik. Az ND-186 ezt GEOMETRIAILAG kezelte: a 2 km-es
rácslépcső helyére sima, a feltöltési szint alatt futó vonal került — ez
indokolt, mert az a terület a feltöltődés után víz alatt van. A MODELLKÉRDÉS
viszont nyitva van: ha egy medence feltöltődik, akkor fizikailag **TÓ**, és
akkor nem egy folyóvonalat kellene átvezetni rajta, hanem tófelületet rajzolni,
a folyót pedig a tó kifolyásától folytatni.

**Amit el kell dönteni:** (a) a medence feltöltési szintjét ugyanaz a modell
adja-e, mint a `LakesIceErosion.IdentifyLakes` (ami a durva rácson már számol
tavakat), vagy a folytonos escape saját szintjét kell-e visszacsatolni oda;
(b) a tófelület megjelenítése (part, vízszint, I3-kompatibilis szín) a
meglévő vízrétegbe illeszkedik-e; (c) a folyóág topológiája (a tóba érkező
ág + a kifolyó ág két külön ág-e, vagy egy, tavon átvezetett ág).

**Opciók:** (A) marad a mostani, geometriailag sima átvezetés — a leghűbb a
mai adathoz, de egy nagy tó helyén folyóvonalat mutat; (B) a medence tóként
jelenik meg, a folyó a tóparton megáll és a kifolyásnál újraindul — fizikailag
helyes, de új ág-topológiát és új vízfelület-geometriát igényel; (C) hibrid: a
tó megjelenik, de a folyó vonala is átvezet rajta (mint a valós térképeken a
tavon áthúzott folyóvonal). Javaslat: előbb MÉRÉS — hány medence, mekkora
felülettel és mélységgel, és hány egyezik a `LakesIceErosion` már meglévő
tavaival; a döntés enélkül csak vizuális preferencia lenne.

**Kompatibilitás:** (B) és (C) is seed-/világadat-törő (új folyó-topológia),
tehát a generátorverzió emelését igényli. Addig a (A) a futó állapot, és ez
NEM tekinthető a B3 látványítélet lezárásának.

**MÉRÉS (2026-10-03) — a döntéshez kért számok.** Új `lakecross` mód a
`RiverBaseline`-ban (a folyópontokat level 8-as tile-ra képezi és a
`LakesIceErosion.IdentifyLakes` + `TileCount>=6 && MaxDepth>=40` szűrővel
vetett LÁTHATÓ tavakkal metszi — ugyanaz a tó-definíció, amit a viewer
használ). Seed 0xA7C944210000, t=0, 96 ág, 46 641,008 km, 531 látható tó
(11 505 tile):

| Mérőszám | Érték |
|---|---|
| tó-tile-okon futó hossz | **19 283,466 km = 41,34%** |
| ágak, amik ÁTVÁGNAK legalább egy tavon | 27 / 96 (51 átvágás) |
| leghosszabb egyetlen tó-átvágás | **512,937 km** (#33) |
| ágak, amiknek a hosszuk >90%-a tavon fut | **13** |
| ágak, amiknek a hosszuk >50%-a tavon fut | 34 |
| tóban VÉGZŐDŐ ágak | 32 (a 11 `Pit` mind valódi, látható tóban — `endcheck`) |
| tóban KEZDŐDŐ ágak | **17** |
| tengerszint alatt kezdődő ágak | 2 (ezek a 0,00 km hosszú ágak) |

Vagyis a jelenség nem kivételes eset: a megjelenített „folyó” 41%-a valójában
tófelület. Bizonyíték: `artifacts/a8-nd187/t0-lakecross.csv`.

**FELHASZNÁLÓI VISSZAJELZÉS (2026-10-03), ami lezárta a döntést.** „Látványosan
átfolynak a tavakon a folyók, illetve arra is van példa, hogy szárazföldön
kezdődik és ott is van vége a folyónak.” A felhasználó a fenti mérés
ismeretében az (A) opció JELÖLT változatát választotta, és külön a
forrás-szűrést (ND-189).

**DÖNTÉS: (A+) — a geometria marad, a tavi szakasz JELÖLVE lesz, és nem
folyóvonalként jelenik meg.** A nyomkövető már ma is tudja, mely pontok
vannak a medence feltöltési szintje alatt (`RefineEscapePath` a `lakeLevel`
alatti egyeneseket vonja össze), csak nem adja ki ezt az információt. Ezért:

1. a `ContinuousRiverPath` új mezőt kap (`SubmergedSpans`): azok a
   `Points`-tartományok, amelyek egy medence feltöltési szintje alatt futnak;
2. a párhuzamos csonkolás (`CommitReady`) a tartományokat is elvágja ott,
   ahol az ágat;
3. a viewer ezeket a szakaszokat NEM rajzolja folyószalagként — a vonal a
   tó partján megáll, és a tó túloldalán folytatódik.

**Miért ez, és nem (B).** A (B) (külön tó-kifolyás ág-topológia) a teljes
fizikai megoldás, de seed-törő és nagyságrenddel nagyobb munka; a mérés
szerint a látványhiba 100%-át a tavi szakaszok RAJZOLÁSA okozza, nem a
geometriájuk. Az (A+) a HELYES képet adja a mai adatból: a víz a tavon át
folyik tovább, de a tó nem folyó. A geometria bitre változatlan, tehát
**nincs generátorverzió-emelés** — a `SubmergedSpans` származtatott adat,
a fingerprint a `Points`-ból számol.

**Várható kimenet (mért alapon):** 27 358 km látható folyóvonal, ~147
darabban, átlagosan 186 km/darab. A 13 „majdnem teljesen tavi” ág szinte
eltűnik — ez nem veszteség, hanem a tény.

**MÉRÉS A MEGVALÓSÍTÁS UTÁN (2026-10-03) — a vágás kapuja NEM a Core
jelölése lett.** A `SubmergedSpans` elkészült (Python-orákulum + bitre egyező
C# port, `ContinuousRiverV2Tests`), és ezzel mérhetővé vált, mennyire fedi a
Core saját „víz alatti” fogalma a MEGJELENÍTETT tavakat. A mélység-küszöböt
(`DefaultSubmergedMinDepthMeters`) söpörtem a valódi t=0 hálózaton:

| küszöb | jelölt hossz | a hossz %-a | tartományok | ebből látható tavon | a tó-hossz hány %-át fedi |
|---|---|---|---|---|---|
| 0 m | 33 490 km | 71,80% | 309 | 16 100 km | 83,49% |
| 10 m | 32 015 km | 68,64% | 214 | 15 924 km | 82,58% |
| **40 m** | **26 025 km** | **55,80%** | **110** | **15 402 km** | **79,87%** |
| 100 m | 16 986 km | 36,42% | 46 | 11 177 km | 57,96% |

A Core jelölése tehát MÉG 40 m-es küszöbbel is 55,8%-ot fed, miközben a
látható tavakra csak 41,34% esik. Az ok MÉRT és szerkezeti: a követő a 2 km-es
escape-rácson MINDEN lokális mélyedést medencének lát, a megjelenített
tó-réteg viszont level 7-8-as tile-okon és a `TileCount >= 6 && MaxDepth >= 40`
szűrővel készül. **Ha a span alapján vágtunk volna, a folyóhossz 55-72%-a
eltűnt volna a képről** — jóval több, mint amit a felhasználó tavakként lát
(„a folyók mennyisége kb. ok” volt a visszajelzése).

Ezért a viewer vágása a MEGJELENÍTETT tó-rétegre épül (`_adaptiveLakeTiles`,
ugyanaz a halmaz, amit a `LakeSurface` rajzol): a folyószalag ott szakad meg,
ahol a felhasználó TAVAT LÁT, és nem szakad meg ott, ahol nincs kirajzolt tó.
A 40 m-es küszöb azért marad a Core-ban, mert az apró mélyedéseket (309 → 110
tartomány) kiszűri, és a legtöbb látható tavat még fedi (79,87%) — a
`SubmergedSpans` így a modell-oldali igazság marad (tesztelt, orákulumhoz
mért), nem a megjelenítés kapuja.

**Ami ezzel NEM dől el:** a Core escape-`lakeLevel`-je és a
`LakesIceErosion` tószintje NEM ugyanaz a felbontás (ld. a fenti 55,80% vs
41,34%). Amíg a tó-réteg durvább, mint a követő escape-rácsa, a kettő nem
fog egybeesni. Ha a tó-réteg egyszer a követő felbontására kerül, a vágás
visszatérhet a span-okra — az új, önálló döntés lesz.

### ND-196 — A folyók a SZÁRAZ biome-okban gazdagodnak, mert a medence-kvóta nem küszöböl csapadékra (LEZÁRVA 2026-10-06, a (b) opció; SEED-TÖRŐ, generátor 10 → 11)

2026-10-05, felhasználói visszajelzés: „a folyók gyakran sárga biomon
láthatóak, a zöld biomban nem vettem észre… azt kértem, hogy a csapadékos
területeken legyen folyók inkább". MEGMÉRVE: a megfigyelés helyes, és
mennyiségileg igazolt.

#### 1. A nyomvonal mérve (ezt LÁTJA a felhasználó)

Friss `PlanetGridMesh` példány, level 5, a kész folytonos hálózat: 96 folyó,
484 096 nyomvonal-pont, 210 érintett tile (198 szárazföldi, 12 óceáni a
torkolatoknál). A „folyóhossz%" pont-súlyozott (minden nyomvonal-pont egy
minta), a „szárazföldi alap%" a biome-térkép szárazföldi megoszlása:

| biome | folyóhossz% | érintett tile% | szárazföldi alap% | gazdagodás |
|---|---|---|---|---|
| **Desert** (homokszín) | **28,9** | 28,3 | 11,8 | **2,45×** |
| Tundra (szürkebarna) | 44,1 | 43,4 | 41,2 | 1,07× |
| TemperateForest (zöld) | 7,3 | 6,6 | 9,5 | 0,77× |
| Savanna (olívzöld) | 4,8 | 4,5 | 8,1 | 0,59× |
| **Rainforest** (mélyzöld) | **7,5** | 6,6 | 14,7 | **0,51×** |
| Grassland | 7,5 | 10,6 | 14,7 | 0,51× |

A legszárazabb osztály **2,45-szeresen** felülreprezentált, a legnedvesebb
**feleannyira** van jelen, mint a szárazföldi arány indokolná. A felhasználó
kérése (ND-124 óta: csapadékos területen legyenek a folyók) tehát NEM
teljesül.

#### 2. A gyökérok: a medence-kvótának nincs globális csapadék-kapuja

A `SelectRiverSourcesPerBasin` (ND-180/186 óta ez az aktív út) a csapadékot
KIZÁRÓLAG **medencén belüli rendezési kulcsként** használja:

```csharp
candidates.Sort((x, y) => precipField[y].CompareTo(precipField[x]) ...);
```

A `DefaultPrecipPercentile = 0,80` **csak a régi, globális
`SelectRiverSources` paramétere** — a medencénkénti változat soha nem olvassa.
Következmény: a 16 legnagyobb vízgyűjtő MINDEGYIKE megkapja a 6 forrását akkor
is, ha a medencében egyetlen csapadékos tile sincs.

Mérve, medencénként a 6 forrás globális szárazföldi csapadék-percentilise:

```
medence  2:  93,4  92,9  91,9  91,7  91,1  87,8      <- nedves, helyes
medence 16:  98,5  98,3  96,7  95,9  93,3  91,2      <- nedves, helyes
medence  3:  81,5  79,6  77,9  77,8  77,7  77,3
medence  4:   0,0   0,0   0,0   0,0   0,0   0,0      <- MIND nulla csapadeku
medence  5:   0,0   0,0   0,0   0,0   0,0   0,0
medence  6:   0,0   0,0   0,0   0,0   0,0   0,0
medence  8:   0,0   0,0   0,0   0,0   0,0   0,0
medence  9:   0,0   0,0   0,0   0,0   0,0   0,0
medence 13:   0,0   0,0   0,0   0,0   0,0   0,0
medence 14:   0,0   0,0   0,0   0,0   0,0   0,0
medence  1:  52,6  52,5   0,0   0,0   0,0   0,0
medence 15:  60,7  56,6  55,9  52,8   0,0   0,0
```

**7 medence mind a 6 forrása nulla csapadékú tile-on indul**, kettő pedig
részben: összesen **48 / 96 forrás (50%) olyan tile-ról indul, ahol a
csapadék PONTOSAN NULLA.** Ez nem a 150 km-es forrás-szeparáció és nem a
választási rang hibája: már az ELSŐ (azaz a medence legnedvesebb) választott
forrás is 7/16 esetben nulla csapadékú. A forrás-biome gazdagodás ugyanezt
mutatja: `Desert` 1,94×, `Rainforest` 0,57×.

#### 3. A háttér, ami ezt felerősíti: a szárazföld fele csapadékmentes

Ugyanazon a világon, szárazföldi tile-okra (2151):

| csapadék-mező | nulla csapadékú szárazföld | medián | maximum |
|---|---|---|---|
| analitikus (az előnézet) | 640 (**29,8%**) | 0,0957 | 30,87 |
| hőmodell-párolgással (a mai kép) | 1124 (**52,3%**) | **0,0000** | 67,61 |

A hőmodell párolgás-bemenetével a szárazföld **több mint felén pontosan nulla**
a csapadék, és a medián is 0. Ezért van olyan sok teljesen száraz medence. Ez
ugyanaz a nyitott kalibrációs kérdés, amit az ND-195 is hagyott maga után, és
az A24 / ND-165 körébe tartozik — de most már konkrét számmal.

A forrás a NYERS `precipField.Precipitation`-t látja, a biome az INTERPOLÁLT
`PrecipitationAtCore`-t; ezért lehet egy nulla-nyers-csapadékú forrás-tile
biome-ja mégis zöldes. A két oldal tehát nem is UGYANAZT a csapadék-értéket
nézi, de a fenti 48/96 ettől független.

#### 4. Opciók

**(a) Globális csapadék-kapu a medence-kvóta FÖLÉ.** Egy jelölt csak akkor
forrás, ha a globális szárazföldi eloszlásban a P-percentilis fölött van (a
meglévő `DefaultPrecipPercentile = 0,80`, vagy lazábban). A kapun elbukó
medencék forrás NÉLKÜL maradnak. Kockázat: a 16 medencéből 7–9 kiesik, tehát
~48–60 forrás marad, és csökkenhet az a 42%-os összefolyás-arány, amiért a
medence-kvóta egyáltalán készült (ND-180 mérés). **Meg kell mérni.**

**(b) Csapadék-arányos kvóta.** A 96 forrás szétosztása a medencék TELJES
csapadékával arányosan, a fix 6/medence helyett, egy abszolút alsó kapuval
(nulla összcsapadékú medence sosem kap forrást). A nedves medencék több folyót
kapnak, a számuk marad, a kétszintű hálózat megmarad. **Javaslat: ez.**

**(c) Marad így,** és a dokumentáció rögzíti, hogy a folyó-elhelyezés
domborzat-/medence-vezérelt, nem csapadék-vezérelt. Nincs költség, de a
felhasználó kérése teljesítetlen marad.

**Javaslat: (b)**, mert megtartja a forrás-számot és a kétszintű hálózatot
(ami az ND-180 mért nyeresége volt), ÉS teljesíti a csapadék-preferenciát.
Az (a) ugyanazt a célt egy olyan mellékhatással éri el, amit az ND-180
kifejezetten javítani akart.

**SEED-TÖRŐ.** Bármelyik változat új forráslistát ad, tehát minden
folyóhálózat új → generátorverzió-emelés (10 → 11) és a döntés rögzítése kell,
ugyanúgy, mint az ND-189-nél.

**Amit ez NEM dönt el:** hogy a csapadék-mező nulla-aránya (52,3%) helyes-e. Ha
az A24 / ND-165 körében a párolgás kalibrációja változik, a (b) automatikusan
jobb elhelyezést ad — a két kérdés független, és a sorrend szabadon
választható.

#### 5. LEZÁRVA (2026-10-06, a (b) opció; SEED-TÖRŐ, generátor 10 → 11)

A felhasználó a (b) opciót választotta. Megvalósítva, és a hatás MÉRVE —
ugyanazon a mezőn, amin a hiba keletkezett (a hőmodell párolgásával), nem
az analitikus előnézeten.

**A mérőpad először MEGBÍZHATÓSÁGOT kapott.** Az 1-4. pont mérése élő Unity
Editorban, reflexióval készült, tehát nem volt újrafuttatható. Új, offline
mérőpad: `tools/diagnostics/RiverBaseline` `thermal` módja ugyanazt a
Core-láncot futtatja, amit a viewer háttérszála
(`ThermalClimateCalculator.Compute` → `Refined.MeanSurfaceK` + éves szél →
`MoisturePrecipitation.ComputeWithClimateFields`). **Az egyezés nem feltételezés,
hanem mért:** a nulla csapadékú szárazföld `52,3%` (1124/2151), a nulla
csapadékú forrás `48/96`, a szárazföldi nyomvonal-pont `468 619` — mind a
HÁROM megegyezik az 1-4. pont Editor-beli számával, és a medencénkénti
percentilis-lista is (93,4 / 92,9 / 91,9 …; 98,5 / 98,3 / 96,7 …).

**A megvalósítás (`SelectRiverSourcesPerBasin`):**

1. egy medence SÚLYA a saját JELÖLT-tile-jainak csapadék-összege (jelölt =
   hegyvidéki ÉS pozitív csapadékú);
2. ABSZOLÚT alsó kapu: nulla csapadékú tile nem lehet forrás, akkor sem, ha a
   medencéjén belül éppen ő a legnedvesebb (`DefaultMinSourcePrecip = 0,0`,
   szigorú >);
3. nulla súlyú medence nem kap forrást, és **nem is foglal medence-HELYET** —
   a `basinCount` slot a következő, NEDVES vízgyűjtőre csúszik;
4. a keret FIX (`basinCount × sourcesPerBasin` = 96), és a súlyokkal
   arányosan oszlik (Jefferson/D'Hondt-menet, döntetlennél a kisebb
   medence-index javára), felső korláttal
   (`DefaultMaxSourcesPerBasin = 2 × 6 = 12`);
5. a kiosztás INKREMENTÁLIS: minden keret-egység azonnal megpróbál forrást
   felvenni, és ha a medence kimerült, az egység a következő legjobb
   medencére szállt át.

**A 3. és az 5. pont MÉRÉSBŐL jött, nem tervből.** A naiv (b) — előre
kiosztott kvóta, a 16 legnagyobb medencére — a 96-os keretből csak **66**
forrást valósított meg (−31% folyó), mert a 16 legnagyobb vízgyűjtőből 7
teljesen száraz, és a kvóta olyan medencékbe is jutott, ahol elfogytak a
jelöltek. A száraz medencék slot-jának átengedése 66 → **70**, az
inkrementális kiosztás 70 → **96** forrás. A (b) ígérete („megtartja a
forrás-számot") csak így teljesül.

**Mért hatás (seed 0xA7C944210000, level 5, t = 0, a hőmodell mezője):**

| metrika | előtte | utána |
|---|---|---|
| forrás | 96 | **96** |
| nulla csapadékú forrás | 48 (**50,0%**) | **0 (0,0%)** |
| forrás csapadék-percentilis (medián) | 52,3 | **69,7** |
| nyomvonal-pont percentilise (medián) | **0,0** | **75,4** |
| nyomvonal-pont percentilise (átlag) | 32,7 | **68,3** |
| nyomvonal nulla csapadékú szárazföldön | **55,6%** | **8,1%** |
| szárazföldi folyóhossz | 43 852 km | **44 995 km** (+2,6%) |

Az ELSŐDLEGES mérték szándékosan a csapadék-percentilis, nem a biome:
ez nem függ a biome-vágópontoktól és az interpolációs konvenciótól. A
maradék 8,1% nem hiba: a forrás nedves, a folyó viszont lefelé folyik, és
átszelhet száraz medencét is.

A biome-gazdagodás ugyanezen a mezőn (a mérőpad NYERS tile-értékkel
osztályoz, a viewer az interpolált sarok-táblával — ezért ezek az arányok
a viewer 1. pontbeli táblájával nem azonos populációra vonatkoznak):

| biome | gazdagodás előtte | utána |
|---|---|---|
| Desert | 0,89 | **0,48** |
| Tundra | 1,06 | **0,08** |
| Grassland | 1,51 | 2,18 |
| Savanna | 0,21 | **3,98** |
| TemperateForest | 1,01 | 2,11 |
| Rainforest | 0,79 | **1,03** |

Az analitikus (előnézeti) mezőn ugyanez: 96 forrás, nulla csapadékú forrás
15 → **0**, a nyomvonal 2,7%-a fut nulla csapadékú szárazföldön, hossz
46 588 → 47 269 km.

**Ellenőrzés:** 1995/1995 teljes regresszió Release ÉS Debug (Core 871,
Viewer LodChunking 664, App 452, CLI 24; a Core-ban 4 új ND-196-teszt), a
`thermal_checkpoint_vectors.json` a Python orákulumból újragenerálva (a
diff PONTOSAN a `"10"` → `"11"` generátor-sztring és a belőle származó két
hash), ND-20 Burst-kapu OK, mindkét Unity offline fordítási kapu 0 hiba.

**Gyorsítótár-következmény:** a `ThermalCheckpoint` a generátorverziót a
fejlécbe írja, tehát a `ModelIdentity` és vele MINDEN hőmodell-gyorsítótár
kulcsa megváltozik — az első Build a 10 → 11 emelés után újraszámolja az éves
éghajlatot (az ND-192/195 szinkron útja téveszt, a `climate_keys.v1.txt`
fejléce elavul), utána ismét ms-os a betöltés. Ez az ND-160 és az ND-189
emelésekkel azonos, ismert költség.

**NYITVA marad:** (a) a biome-gazdagodás VIEWER-oldali, interpolált táblája
csak élő Play-menetben mérhető — ez a B-sor tárgya; (b) az, hogy a
csapadék-mező 52,3%-os nulla-aránya helyes-e, továbbra is az A24 / ND-165
kérdése, és ettől a döntéstől független.

### ND-195 — Az indítás utáni biome-átrendeződés (zöld → sárga) és a nem perzisztens éghajlat-kulcs (LEZÁRVA 2026-10-05, az (a) opció; viewer-oldali gyorsítótár: nincs verzió-emelés)

2026-10-05, felhasználói visszajelzés: „az indításkor adott biom pár másodperc
után változik zöld → sárga". MEGVIZSGÁLVA ÉS MEGMÉRVE; nem új hiba, hanem az
ND-162 előnézet-váltása, amit az ND-192 szinkron betöltési útja INDÍTÁSKOR nem
tud megelőzni.

#### 1. Mi történik (mérve)

Az első `Build()` szándékosan az ANALITIKUS előnézetet rajzolja (ND-162), majd
amikor a háttérszálas éves éghajlat megjön, `_climateRebuildRequested` → egy
TELJES második Build fut, hőmodell-tengellyel. Mérőpad: friss
`PlanetGridMesh` példány a jelenet Planetjéről `CopySerialized`-elt
beállításokkal (level 5, 6144 tile), Build#1 (analitikus) → háttérszál →
Build#2 (hőmodell), tile-szintű összevetéssel.

**1402 / 6144 tile biome-ja változik** (a szárazföld 2151 tile-jából 1280).
A legnagyobb átmenetek:

```
IceSheet        -> Tundra            224      Savanna   -> Grassland        79
Desert          -> Tundra            169      Rainforest-> Savanna          78
SeaIce          -> Ocean             122      Savanna   -> Tundra           71
Grassland       -> Desert            114      Desert    -> Grassland        68
Grassland       -> Tundra             99      TempForest-> Tundra           64
Savanna         -> TemperateForest    86      Grassland -> TemperateForest  39
```

Szín-csoportokra vetítve (a `BiomeColor` tényleges értékeivel: zöld =
TemperateForest/Rainforest, olívzöld = Savanna, sárga = Desert/Grassland,
szürkebarna = Tundra):

```
sarga     -> szurkebarna  268     zold      -> olivzold      78
jeg       -> szurkebarna  224     zold      -> szurkebarna   75
olivzold  -> zold         122     zold      -> sarga         33
jeg       -> viz          122     sarga     -> olivzold      19
olivzold  -> sarga        115     szurkebarna -> sarga         7
```

A felhasználó által látott „zöld → sárga" tehát a `zold → olivzold` (78),
`zold → szurkebarna` (75) és `zold → sarga` (33) = **186 tile**, plusz a
mellettük futó `olivzold → sarga` (115) — összesen 301 tile sötétzöldből
sárgás/szürkés irányba.

#### 2. Miért ilyen nagy a váltás: nem csak a hőmérséklet-tengely vált

A `_adaptiveEvaporationTemperatureK` `null` → hőmodell-felszín váltása ÚJ
csapadék-mezőt ad (a `GetOrComputePrecipitationField` kulcsának kilencedik
eleme), tehát a biome MINDHÁROM bemenete egyszerre más:

| | analitikus (Build#1) | hőmodell (Build#2) |
|---|---|---|
| csapadék-mező megváltozott tile | — | **5275 / 6144** |
| csapadék-medián | 0,7539 | **0,2282** |
| csapadék-maximum | 105,678 | **199,132** |
| arid / semiArid / moist vágópont | 0,1536 / 0,4933 / 1,4554 | **0,0315 / 0,1724 / 1,1527** |

A vágópontok percentilisek, tehát magukban követik az eloszlást — a váltás
mégis nagy, mert a mező ALAKJA változik (a medián harmadára esik, a maximum
majdnem duplázódik), nem csak a léptéke.

#### 3. Miért „pár másodperc": a kulcs nem perzisztens

Az ND-192 szinkron, Build-beli betöltése (`TryLoadThermalClimateDuringBuild`)
azonnal kiváltaná az előnézetet — de a `TryGetCachedClimateKey` kapun elbukik,
mert a `_climateKeyMemory` **példány-mező, nem perzisztens**: minden
indításnál (Play start / domain reload) üres. A lemez-gyorsítótár közben MEGVAN.
Három független menet PerfLogja:

```
[ND-192 climate job] fromCache=True keyFromMemory=False totalMs=6662.3 grid=8.5  field=6234.4 fingerprint=411.6 disk=5.7   compute=0.0 unpack=2.2
[ND-192 climate job] fromCache=True keyFromMemory=False totalMs=7119.0 grid=11.2 field=6327.4 fingerprint=618.2 disk=115.4 compute=0.0 unpack=44.6
[ND-192 climate job] fromCache=True keyFromMemory=False totalMs=7871.8 grid=9.4  field=7359.0 fingerprint=487.2 disk=10.3  compute=0.0 unpack=4.4
```

`compute = 0,0 ms` — az éghajlat teljes egészében a lemezről jön. A
**6,2–7,4 s a `field` fázis**, vagyis a `SurfaceTemperatureField` felépítése,
ami KIZÁRÓLAG a gyorsítótár-kulcshoz (`ComputeSolverFingerprint`) kell. A
tényleges lemez-olvasás 5,7–115 ms, a kipakolás 2,2–44,6 ms. A várakozás
**~90%-a olyan munka, aminek nincs tartalmi eredménye** — csak azért fut, mert
a kulcsot nem tudjuk a lemezről.

#### 4. Kapcsolat az ND-194-gyel (A23)

A váltás maga (1402 tile) az ND-194 ELŐTT is megvolt, csak az IRÁNYA volt más:
addig az első Build 100% `Desert`-et adott (sárga), tehát sárga → zöldes
irányba váltott. Az ND-194 óta az első kép HELYES (az analitikus tengely
valódi csapadékával), ezért most a sötétzöld osztályok dominálnak az első
képen, és a váltás zöld → sárga/szürke irányba látszik. **Az ND-194 nem
okozta a váltást, de láthatóvá/zavaróbbá tette** — korábban a hibás, egyszínű
sivatag „olvadt fel" a helyes képbe.

#### 5. Opciók

**(a) A gyorsítótár-kulcs perzisztálása lemezre** (`identity → modelIdentity +
fingerprint` leképezés a meglévő cache-könyvtárban). Ekkor az ND-192 szinkron
útja már az ELSŐ Buildben bekapcsol: nincs analitikus előnézet, nincs
átrendeződés. A mért költség, amit a Buildbe beengedünk: 5,7–115 ms
lemez-olvasás + 2,2–44,6 ms kipakolás, a 6,2–7,4 s-os `field` fázis helyett.
Biztonsági háló már megvan: a `ThermalClimateDiskCache.TryRead` a KULCCSAL
validál, tehát egy elavult/hamis perzisztált kulcs elutasításra és
újraszámolásra vezet, nem rossz adatra.

**(b) A biome kirajzolásának halasztása,** amíg nincs hőmodell (semleges
felszín az első Buildben). Megszünteti a villogást, de üres/szürke kezdőképet
ad, és sérti azt az ND-162 szándékot, hogy legyen azonnali kép.

**(c) Marad így,** a panel jelzi az „éghajlat: számítás fut" állapotot (ma is
jelzi). Nincs költség, de a felhasználó által jelzett zavar megmarad.

**Javaslat: (a).** Ez az egyetlen opció, ami a mérés szerint a VÁRAKOZÁS
okát szünteti meg (a 90%-os, eredmény nélküli `field` fázist), nem csak a
tünetet rejti el. A (b) és (c) nem igényel külön döntést, ha az (a) megy.

**Amit ez a kör NEM dönt el:** hogy a két tengely (analitikus vs hőmodell)
eltérése önmagában indokolt-e ilyen mértékben (medián 0,754 → 0,228). Az a
csapadék-párolgás kalibrációjának kérdése, és az A24 / ND-165 körébe tartozik
— külön mérés, nem ennek a tételnek a tárgya.

#### 6. LEZÁRÁS (2026-10-05): az (a) opció, implementálva és megmérve

**A kulcstábla perzisztál.** A `_climateKeyMemory` tartalma a gyorsítótár-
könyvtárba kerül (`climate_keys.v1.txt`), fejlécében egy **modell-próba
azonosítóval**. A próba nem egy kézzel másolt paraméter-lista, hanem egy FIX,
szintetikus (level 1 — a `DenseGridMetrics` legkisebb engedett szintje)
bemenetre vett `ModelIdentity`: **ugyanaz a kód** (`ComputeModelIdentity`)
számolja, amelyik a valódi kulcsot is, tehát a paraméter-lista nincs kétszer
leírva, és egy új modell-paraméter nem tud csendben kimaradni a kapuból.

Ez pótolja azt a biztonsági érvet, amit a perzisztálás elvett (a példány-mező
„a domain reload kiüríti, tehát elavult kulcsot sosem látunk"). Az
`identity` a világ-specifikus bemeneteket fedi (szint, seed, tengerszint,
deep-time, pálya, per-cella típus + eleváció, modell- és generátorverzió), a
fejléc-próba pedig a modell-paramétereket és a bolygó-sugarat — pont azokat,
amiket egy A/B-mérés modellverzió-emelés nélkül állít át (ld. az ND-160
figyelmeztetését ugyanerről).

**A második, mérés közben feltárt rés: a hiányzó `.bin`.** A kulcs mostantól
túléli a sessiont, a `.bin` fájlokra viszont vonatkozik a kvóta-takarítás
(`EnforceThermalClimateCacheQuota`, 128 MiB) — tehát előállhat, hogy a kulcsot
tudjuk, de a fájl már nincs ott. Ha ilyenkor a szinkron útra léptünk volna, a
`RunThermalClimateJob` a FŐ SZÁLON építene mezőt (6,2–7,4 s) és számolna teljes
éghajlatot (hidegen ~118 s level 5-on) — azaz a kép megfagy. A szinkron út
kapuja ezért a fájl létezését is ellenőrzi (`ClimateCacheFileExists`), a
kulccsal AZONOS fájlnév-számítással. Ez a rés a memóriabeli változatban is
megvolt elvben, de a perzisztálás tette valóssá.

**Mérve (ugyanaz a mérőpad: friss `PlanetGridMesh` példány = új indítás, mert a
példány-mezők üresek; level 5, 6144 tile):**

| | előtte | utána |
|---|---|---|
| az első Build éghajlata | analitikus előnézet | **Build-ben betöltve** |
| betöltési idő | 6662 / 7119 / 7872 ms (háttérszálon) | **14 / 16 ms (a Buildben)** |
| `rebuildRequested` az első Build után | `True` (második teljes Build) | **`False`** |
| háttérszálas éghajlat-job | elindul | **nem indul** |
| biome-átrendeződés | **1402 / 6144 tile** | **0 tile** |
| vágópontok az első Buildben | 0,1536 / 0,4933 / 1,4554 (analitikus) | **0,0315 / 0,1724 / 1,1527** |

**A végállapot bitre azonos.** Az új ELSŐ Build biome-térképe
**0 / 6144 eltéréssel** egyezik a korábbi kétlépéses út VÉGÁLLAPOTÁVAL
(`Ocean=3993 Tundra=886 Grassland=316 Rainforest=316 Desert=254
TemperateForest=204 Savanna=175`). Vagyis nem egy másik képet mutatunk
hamarabb — ugyanazt a képet mutatjuk, csak elsőre.

**A kapuk külön-külön megmérve, mindhárom úton (PerfLog):**

```
[ND-195 climate keys] loaded=1                      -> talalat, a kulcs a lemezrol
[ND-192 build climate load] applied=True ... ms=17.1 -> szinkron betoltes a Buildben
[ND-195 climate keys] discarded=stale-model-probe    -> elavult fejlec: 0 bejegyzes
[ND-195 build climate load] skipped=cache-file-missing -> hianyzo .bin: elonezet
```

- **Elavult fejléc:** a próba-azonosítót nullára írva egy friss példány
  **0 bejegyzést** töltött be → a régi út fut, nem rossz adat.
- **Hiányzó `.bin`:** a fájlt elnevezve a Build **4171 ms** (a megszokott
  előnézet-út) és `climateApplied = null` — **nem fagyott a főszálon**.

**Nincs generátorverzió-emelés.** A Core numerikája és a világadat változatlan;
a perzisztált tábla gyorsítótár-kulcs, a tartalmat továbbra is a
`ThermalClimateDiskCache.TryRead` validálja.

**Kapuk:** `dotnet build tests/WorldGen.Viewer.Compile` → 0 error; Unity
`recompile` → `compilationFailed: false`; `dotnet test
tests/WorldGen.Viewer.LodChunking.Tests` → **664/664 zöld**; Unity Console
aktuális hibaszám **0**.

**Továbbra is nyitva:** hogy a két tengely eltérése (csapadék-medián
0,754 → 0,228) önmagában indokolt-e. A felhasználó ezt most már nem LÁTJA
váltásként, de a kérdés modellkérdésként megmarad — A24 / ND-165.

### ND-194 — A csapadék-mező és a vágópontok a biome-osztályozás ELÉ kerülnek (LEZÁRVA 2026-10-05, A23; viewer-oldali sorrend: nincs verzió-emelés)

2026-10-05, a todo2 **A23** tétele (eredetileg az ND-164 élő ellenőrzése fogta
meg). A `PlanetGridMesh.Build()` a `biomeOf` szótárat a geometria-ciklusban
töltötte fel, a csapadék-mezőt és a három percentilis-vágópontot viszont csak
a ciklus UTÁN számolta ki — tehát minden Build az **ELŐZŐ** Build mezőjével és
vágópontjaival osztályozott. Egy friss példány ELSŐ Buildjénél ez `null`
csapadék-mezőt (`PrecipitationAtCore` → 0) és `(0, 0, 0)` vágópontot jelentett.

**Mérve (élő Editor, friss `PlanetGridMesh` példány, a jelenet Planetjéről
`CopySerialized`-elt beállításokkal, level 5, 6144 tile) — ELŐTTE:**

```
build#1: Ocean=3871 Desert=1672 Tundra=255 IceSheet=224 SeaIce=122
```

A szárazföld MINDEN meleg tile-ja (1672) `Desert` lett, holott ugyanabban a
Buildben az arid vágópont 0,1536 volt, és a meleg szárazföldi csapadékból
ténylegesen csak **335** tile esik e alá. A második Build után magától
helyreállt, ezért maradt észrevétlen. **I4-sértés, amíg tart.**

**A javítás:** a csapadék-blokk (mező + sarok-tábla + vágópontok) egészében a
geometria-ciklus ELŐTT fut. Ez biztonságos, mert a blokk minden bemenete már
korábban elő van állítva: `_adaptiveEvaporationTemperatureK` és
`_adaptiveClimateWind` a hőmodell-kapu után, a `BiomeTemperatureKelvinAt`
sarok-táblája a `BuildClimateAirCornerTable`-ben — mindkettő a régi helynél is
KORÁBBAN. A `GetOrComputePrecipitationField` tiszta függvény a serializált
klíma-paraméterekre és ezekre a mezőkre, tehát a folyó-forrás kiválasztás
(`SelectRiverSourcesPerBasin`) és a felhő-réteg BITRE ugyanazt a mezőt kapja,
mint korábban.

**Mérve UTÁNA (ugyanaz a mérőpad):**

```
build#1: Ocean=3871 Grassland=418 Rainforest=417 Savanna=386 Desert=335
         Tundra=255 IceSheet=224 SeaIce=122 TemperateForest=116
```

`Desert` = **335**, pontosan annyi, amennyit az ugyanabban a Buildben érvényes
vágópont (arid = 0,1536) a meleg szárazföldön kijelöl. Teljes I4-ellenőrzés
ugyanerre a Buildre: mindhárom bemenetet (`BiomeTemperatureKelvinAt`,
`PrecipitationAtCore`, `_adaptiveBiomeThresholds`) visszaolvasva és a
`BiomeClassification.Classify`-t újrafuttatva **6144/6144 tile egyezik,
mismatch = 0** — a panel biome-térképe tehát pontosan a SAJÁT Buildje
mezőiből következik.

**Nincs generátorverzió-emelés:** a Core numerikája és a világadat változatlan,
a sorrend-hiba kizárólag a viewer panel-/osztályozás-állapotát érintette.

**Tanulság a következő körökre:** ha egy Build-fázis egy KÉSŐBBI fázis
eredményét olvassa mezőn keresztül, a hiba nem bukik el, csak az ELSŐ
futásban látszik — és utána magát „javítja". Az ilyen sorrend-hibát csak
FRISS példányon lehet megmérni; a már bejáratott jelenet-objektum
szisztematikusan elrejti.

### ND-193 — A folyószalag a RENDERELT felszínre vetül (LEZÁRVA 2026-10-05, megjelenítési döntés: nincs verzió-emelés)

2026-10-05, felhasználói visszajelzés képen (`pics/p.png`): pirossal
„összevissza folyók", lilával „a folyó nem éri el a tavat, vagy épp nagyon
belenyúlik" — és a felhasználó hipotézise: *„amikor a zoomolás miatt
pontosabb kalkulációt kap a tó pereme, a folyó végpontja a zoomoláskor nincs
újraszámolva."* **A hipotézis igaznak bizonyult, és a mért ok nagyobb, mint
a vágás pontatlansága.**

**1. mérés — a folyóvonal LEBEG.** Az élő Editorban (seed 0xA7C944210000,
t=0, 96 ág, 484 096 pont, kamera 988 km magasan) megmértem a kirajzolt
folyóvertex és a RENDERELT terep-háromszög távolságát ugyanazon a
felületi ponton:

| Mérőszám | Érték |
|---|---|
| sugár-irányú eltérés (átlag) | **37,04 km** |
| képernyő-elcsúszás, átlag / max | **9,9 / 22,7 képpont** (1275×809) |
| a folytonos mező és a renderelt mesh eltérése, level 8 | átlag 21,5 m, max 147,9 m |
| ugyanez level 10 / 11 (kamera közelében) | átlag 0,9 / 0,5 m |

A 37 km **teljes egészében** a `riverLineRadialBias = 0,5` Unity-egységből
jött. A mező doksija szerint ez „~50 m ekvivalens" — ez a RÉGI
`elevationScale = 0,01` mellett volt igaz. A mai lánccal
(`elevationScale = 1,3477e-5`, `terrainReliefExaggeration = 111`) ugyanaz a
0,5 egység **334 világ-méter**, kirajzolva 37,1 km. Ferde rálátásnál ez
parallaxist ad: a vonal a völgyéből a hegyoldalra, a tóba vagy a parttól
beljebb csúszik — pontosan a felhasználó piros és lila jelölései.
**Ez ugyanaz a hibaosztály, amit a hőmodellnél kétszer is elkaptunk: a
konstans maradt, a mező léptéke változott.**

**2. mérés — a vágás tile-granularitású.** A szalagot az ND-187 óta a
MEGJELENÍTETT tó-tile-halmaz kapuzza (level 8 ≈ 39 km-es tile). A
ténylegesen kirajzolt vízfelszín viszont a tó-réteg és a terep metszete.
Mérve ugyanabban az állapotban: a tó-tile miatt kivágott, de valójában
SZÁRAZ (renderelt terep a vízszint felett) hossz **715,1 km**, a kirajzolt,
de valójában víz alatti hossz **32,9 km**, és **56 / 96 ág** a renderelt
vízvonal FÖLÖTT ér véget.

**Döntés.** A folyószalag mostantól
1. a **RENDERELT** felszínre vetül (`LodCoverage.FindRenderedLeaf` +
   `SurfaceQuad.At`, ugyanaz a háromszögelés, amiből a terep-mesh készült),
   nem a folytonos elevációmezőre;
2. ott szakad meg, ahol a **renderelt** felület a **kirajzolt** vízszint
   (tónál a `BuildLakeSurface` gyűrűvel kiterjesztett feltöltési szintje,
   egyébként a tengerszint) alá kerül — tile-határ helyett a követő
   lépésközének (50 m) pontosságával;
3. **újravetül minden LOD-alkalmazás után** (`AdoptRenderedSurface` →
   `MaintainRiverSurfaceProjection`), tehát zoomoláskor a végpont valóban
   újraszámolódik;
4. a sugár-eltolás `riverLineBiasWorldMeters = 10` VILÁG-méter, a terep
   megjelenítési láncán átszámolva — így a túlrajzolás vagy a lépték
   változása nem tudja újra elszabadítani.

**Amit tudatosan NEM teszünk:** nem hosszabbítjuk meg a vonalat a renderelt
partvonalig. A hiányzó véget a modell nem tartalmazza, a toldás dekoratív
lenne (I3). A rés magától záródik, ahogy a mesh a zoommal a folytonos
mezőhöz konvergál (mérve: level 11-en 0,5 m eltérés).

**Szálbiztonság.** A vetítés a FŐ szálon fut, miközben egy worker már a
következő LOD-ot építheti. Ezért csak olyan sarkot olvas, ami adatverseny
nélkül elérhető: az alap-szinten a statikus, csak-olvasott sarok-tömb, a
finomított szinteken kizárólag a már feloldott sarok
(`LodCornerResolver.TryGetResolvedCorner`). Hiányzó sarokra az alap-szintű
ős négyszögére esik vissza, és ezt a napló `projectionFallbacks`-ként
számolja.

**Mérés a javítás UTÁN** (ugyanaz a kamera, a KIRAJZOLT mesh
középvonal-vertexeiből):

| Mérőszám | Előtte | Utána |
|---|---|---|
| sugár-irányú eltérés a renderelt mesh-től | 37,04 km | **10,00 világ-méter** |
| képernyő-elcsúszás, átlag / max | 9,87 / 22,7 px | **0,30 / 0,69 px** |
| kirajzolt vertex a renderelt víz alatt | 32,9 km hossz | **0** |
| `projectionMisses` / `projectionFallbacks` | — | **0 / 0** |

Zoom-próba 988 → 222 km: a LOD level 13-14-re finomodott,
`surfaceRevision` 13 → 15, és a folyómesh is újravetült (mérve 9,99 m,
0 víz alatti vertex). Újravetítés költsége a finom hálózaton
`activeMs = 383,8`, `maxSliceMs = 8,4`, `uploadMs = 35,6`.

**Nincs Core-/seed-változás, nincs generátorverzió-emelés** — a modellút
pontjai bitre változatlanok, csak a megjelenítésük.
[Napló](../history/2026-10-05-a8-nd193-river-on-rendered-surface.md).

### ND-192 — A deep-time léptetés dupla Buildje (LEZÁRVA 2026-10-03, BITAZONOS: nincs verzió-emelés)

2026-10-03, felhasználói jelzés: „a deep time is megint lassú". Mérve: a
csúszka elhúzása után **két teljes `Build()` fut le ugyanarra az időpontra**
(MÉRVE 11 697 + 10 449 ms = **22,1 s**), és a második eldobja az első
félkész folyómunkáját.

**Miért volt két Build.** Az ND-162 architektúrája szerint (ld. a
`PlanetGridMesh.ThermalClimate.cs` fejlécét) cache-találatnál „a Build MAGA
tölti be (ms), és azonnal a hőmodell jeget használja — nincs előnézet, nincs
csere". A mérés szerint ez **nem teljesült**: a Build csak RÖGZÍTETTE a
hőmodell bemenetét (`CaptureThermalClimateInputs`), a betöltés viszont a
következő `Update()` háttérszálán indult, így az első Build mindig az
analitikus előnézetet rajzolta, és a kész éghajlat egy második, teljes Buildet
kért.

**A mért ok, amiért a betöltés nem volt „ms".** Új fázismérés
(`[ND-192 climate job]`) a háttérjobra, cache-találatos menetben:

| fázis | idő | arány |
|---|---|---|
| `SurfaceTemperatureField` felépítése | **5838-8487 ms** | **91-94%** |
| solver-lenyomat (96 tick) | 485-805 ms | 6-8% |
| **lemez-olvasás** | **5,7 ms** | **0,09%** |
| kipakolás | 1,3-3,7 ms | 0,03% |

Vagyis a „cache-találat" 6,4-9,0 másodperce szinte teljes egészében a
cache-KULCS előállítása volt: a mező felépül, kiadja a modellazonosítót és a
lenyomatot, aztán — találat esetén — az eredménye **eldobódik**.

**A javítás (két lépés, egyik sem változtat numerikát).**
1. **A cache-kulcs session-memóriája.** A bemenet-azonosító
   (`ComputeInputsIdentity`, már létezett) mellé eltároljuk a kulcs két
   komponensét (modellazonosító + solver-lenyomat). A mező felépítése így
   KÉSŐN történik: csak akkor, ha a kulcsot nem tudjuk, vagy ha a cache
   téveszt és tényleg számolni kell.
2. **A Build maga tölt be** (`TryLoadThermalClimateDuringBuild`), ha a kulcs
   memóriából jön ÉS a lemez talál — így nincs előnézet és nincs második Build.
   Ha a kulcs nincs memóriában, a metódus AZONNAL visszatér: a kulcs
   előállítását (6,3-9,0 s) nem tesszük a fő szálra, ott a megszokott út fut.

**Miért biztonságos.** A memória-gyorsítótár nem kerüli meg a cache
helyesség-ellenőrzését: a beolvasott fájlt a `ThermalClimateDiskCache.TryRead`
ugyanúgy validálja (modellazonosító, mintanapok, percentilis, lenyomat,
ellenőrzőösszeg), tehát hibás memória-bejegyzés nem tud rossz tartalmat
behozni — legrosszabb esetben téveszt, és újraszámolunk. Tévesztéskor a kód a
mezőből ÚJRASZÁMOLJA a kulcsot, és ha az eltér a memóriabelitől, a memóriát
eldobja. A gyorsítótár PÉLDÁNY-mező (nem static), ezért a Play közbeni
szkript-újrafordítás (domain reload) kiüríti: numerikusan megváltozott kód
sosem lát elavult kulcsot.

**Mért eredmény (élő Editor, seed 184482873278464, level 5, 200 ↔ 400 Myr).**

| menet | klíma-betöltés | Buildek száma | teljes |
|---|---|---|---|
| kulcs NINCS memóriában (első látogatás) | 8991 ms (`field` 8487) | 2 (11 697 + 10 449 ms) | **22,1 s** |
| kulcs memóriában (visszalépés) | **13,2 ms** (`field` 0) | **1** (már `source=thermal`) | **9,0 s** |

**−13,1 s (−59%)** ismételt deep-time léptetésnél. Az első (hideg) léptetés
változatlan: ott a lemezen sincs adat, az előnézet-út helyes.

**A kimenet BITAZONOS.** Az `[ND-192 build identity]` mérés (a statikus
alapréteg pozíció- és szín-hashe) a régi, kétBuildes menet végállapotára és az
új, egyBuildes menetre UGYANAZT adja: `posHash=D91E47AA8ACEB83E`,
`colorHash=055FD0F4C5AACBB7` (1 572 864 vertex). Ezért a CLAUDE.md szabálya
szerint ez bitazonos optimalizálás: **nincs generátorverzió-emelés.**

**Melléktermék-mérés, ami megdöntött egy feltevést.** A CLAUDE.md Állapot
szakasza szerint a hőmodell „cache-találatnál ms" — ez NEM igaz volt: a
találat 6,4-9,0 s-ot vett, mert a kulcs előállítása dominált. A mostani
javítás ezt ismételt léptetésnél 13 ms-ra hozza, de EGY SESSION ELSŐ
látogatásánál (pl. Play-indítás után) továbbra is 6-9 s — a mező felépítése
ott elkerülhetetlen, mert a lenyomat ellenőrzi a futó kódot. Ennek
gyorsítása (a `SurfaceTemperatureField` konstruktora) külön, még nem mért
feladat.

**Ami ebből NEM következik.** A dupla Build nem volt „hiba a modellben": a
hőmodell bemenete tényleg a Build kimenete. Mérve az is, hogy a második Build
NEM pusztán színezést változtat — a statikus alapréteg pozíció-hashe is más
(`692FE8CB34551053` → `441365C915B27373`), tehát egy „csak-színezés"
újraépítés NEM lett volna helyes megoldás.

### ND-191 — Hideg kiindulású hitch-mérőpad (NYITOTT, az ND-190 blokkolja nélküle)

2026-10-03. Az ND-190 két javítási kísérlete azért nem volt eldönthető, mert a
jelenlegi mérési eljárás (85 s-os kamerakör élő Play-menetben) **3× szórást**
ad ugyanazzal a kóddal: 52 és 159 közt a >60 ms-os frame-ek száma, mert a
geometria-, metrika- és chunk-cache melegedése dominálja az eredményt.

**Amit a mérőpadnak tudnia kell:**
1. **Hideg, azonos kiindulás minden menethez** — a `_terrainEvaluationCache`,
   a chunk-cache (`_spareTerrainMeshes`, `_inactiveTerrainChunks`) és a
   vízréteg cache-e explicit ürítése a menet előtt, ÚJRAÉPÍTÉS nélkül (a
   hőmodell és a folyóhálózat NE számoljon újra, különben a menet 4 perc).
2. **Fix kameraút** (már megvan: aranyszög-spirál irányhalmazok) és fix
   időzítés.
3. **N ismétlés, medián** — a kiugró menetek ne döntsenek.
4. **Egy sor gépi kimenet** menetenként (hitch-szám sávonként, max, p99,
   hitchben töltött idő), hogy A/B-t össze lehessen fűzni.

**Opciók.** (a) Editor-only diagnosztikai komponens a viewerben, menü-
parancsból indítva. (b) Batch-mód Player-mérés a meglévő `DeepTimePlayerProbe`
(ND-135) mintájára, CLI-kapcsolóval, JSON-kimenettel — ez determinisztikusabb
(nincs Editor-overhead, nincs Scene nézet), de a Player-build ideje hozzáadódik
a körhöz. **Javaslat: (b)**, mert az ND-190 mérései közben az Editor Game és
Scene nézete is rajzolt, ami önmagában zaj.

**Amíg ez nincs meg, hitch-javítást nem érdemes írni** — nem lehet
megállapítani, hogy hatott-e.

### ND-190 — A kép-szaggatás mért anatómiája és két elvetett javítás (NYITOTT: mérőpad kell)

2026-10-03, felhasználói jelzés: „a kép generálás megint rendkívüli ütemben
szaggat". Élő Editor-mérés (Unity 6000.0.77f1, Ryzen 7 5700X 8c/16t, t=200 Myr,
66 folyóág, 1275×809 Game nézet). **A szaggatás létezik és reprodukálható**, de
EGYIK javítási jelöltem hatása sem emelkedett ki a mérés zajából — ezért
mindkettőt visszavontam, és a következő kör NEM kódmódosítás, hanem mérőpad.

**1) Mit mértem meg (ezek a tények).**

| Állapot | Frame-idő |
|---|---|
| Nyugalom (nincs LOD-munka) | p50 **3,7-5,2 ms** (190-270 fps), GPU 3,3 ms |
| Hideg LOD-újraépítés (85 s kamerakör) | **52-159 hitch >60 ms**, 14-59 db >100 ms, max 220-526 ms, 4,7-15,2 s hitchben |
| Deep-time léptetés (0 → 200 Myr) | egyetlen **9617 ms-os** frame (a Build a fő szálon) |

**2) Mi NEM okozza (mindegyik kizárva méréssel).**
* **Nem GC:** a hitch-frame-ekben `GC.CollectionCount(0)` delta **0** (110 s-ban
  összesen 13 kollekció, egyik sem hitch-frame-re esett). Allokáció 22,5 MB/s.
* **Nem a szeletelt feltöltés CPU-ideje:** `[ND-85 upload slice]` **1-2 ms**.
* **Nem a script Update/LateUpdate:** `lateUpdateMs` 0,0-3,2.
* **Nem a diagnosztika:** a `[ND-75 drawn]` fő szálú capture p50 **1,04 ms**
  (a 17×9-es mérés maga háttérszálon, 171 ms / 2 s).
* **Nem a rajzolt mennyiség növekedése:** nyugalomban 1609 draw call /
  3,87 M vertex, a hitch-frame-ekben 1964-2569 draw call / 3,94-4,07 M vertex
  (+20-60%), miközben a frame-idő **12-28×**.

**3) Hol megy el az idő (Unity Profiler, elkapott hitch-frame-ek).**
```
Main Thread  148 ms: TimeUpdate.WaitForLastPresentationAndUpdateTime 134,6 ms
                     (GfxDeviceD3D12.WaitForLastPresentation) - sajat munka 7 ms
Render Thread 205 ms: RenderLoop 138,9 (GfxDeviceD3D12.WaitForGPU self 134,4)
                     + Gfx.WaitForGfxCommandsFromMainThread 66,7
              104 ms: RenderLoop self 96,4 - a HDRP rajzolas ebbol 3,5 ms
GPU frame: nyugalomban 3,3 ms -> hitch-frame-ekben 72,3 / 76,2 / 103,2 ms
```
Tehát a fő szál **present-várakozásban** áll, mert a render szál és a GPU
dolgozik — de nem RAJZOL (HDRP 3,5 ms), hanem **GPU-erőforrást készít elő**.
A legjobb magyarázat, ami az összes számmal konzisztens: a chunk-publikálás
(ND-85/ND-94) szándékosan **atomikus**, így a több száz ÚJ vertex-buffer első
használata MIND egyetlen frame-re esik.

**4) Az első jelölt: vertex-alapú szelet-kapu (ELVETVE).** A szelet eddig csak
fő szálú CPU-ms-ra és tételszámra volt kalibrálva. Új mérés a PerfLogba
(`sliceVertices`, **ez megmaradt**): egy frame-ben p50 **19 012**, p90 35 692,
max **54 548** vertex megy a GPU-ra. 16 000-es kapuval, négy 85 s-os kamerakörön:

| kör | kapu | >60 ms | >100 ms | max | hitchben |
|---|---|---|---|---|---|
| 0 | ki | 159 | 59 | 220 ms | 15,2 s |
| 1 | be | 158 | 26 | 219 ms | 13,1 s |
| 2 | ki | 52 | 14 | 229 ms | 4,7 s |
| 3 | be | 85 | 24 | 295 ms | 7,9 s |

Az első pár javulást, a második rosszabbodást mutat → **nem igazolt**, a kaput
(és tesztjeit) visszavontam.

**5) A második jelölt: `Mesh.UploadMeshData(false)` a staging közben
(ELVETVE).** Ez pontosan a 3) pont mechanizmusára hat: a buffer a MÁR FÉKEZETT
szeletben menne a GPU-ra, nem a publikálás utáni első rajzoláskor. Négy menet,
a párok MÁSODIK felében megfordított kapcsoló-sorrenddel:

| fázis | eager | kör | >60 ms | >100 ms | max | hitchben | p50 |
|---|---|---|---|---|---|---|---|
| 0 | ki | 0 | 126 | 50 | 526 ms | 13,1 s | 5,62 ms |
| 1 | **be** | 0 | 73 | 32 | 435 ms | 7,6 s | 6,29 ms |
| 2 | **be** | 2 | 86 | 48 | 300 ms | 9,2 s | 5,87 ms |
| 3 | ki | 2 | 76 | 27 | 200 ms | 7,1 s | 6,52 ms |

**Mindkét párban a MÁSODIK menet jobb, a kapcsoló állásától függetlenül** —
amit mértem, az a cache-melegedés, nem a beavatkozás. Ráadásul a nyugalmi p50
a bekapcsolt ággal rosszabb (5,62 → 6,29 ms). Visszavontam.

**6) A mért tanulság, ami a következő körre érvényes.** A 85 s-os kamerakör
mint mérőpad **nem elég felbontású**: ugyanazzal a kóddal 52 és 159 közt
szór a hitch-szám (3×), mert a `_terrainEvaluationCache` / chunk-cache
melegedése dominál. Ezen a zajon egy 10-30%-os javítás nem látszik, tehát
**minden további hitch-javítás előtt mérőpad kell**: azonos, HIDEG kiindulás
minden menethez (a geometria-/metrika-/chunk-cache explicit ürítése), ugyanaz
a kameraút, több ismétlés, mediánnal. Ez az ND-191 tárgya.

**Nyitva marad (döntést igényel, mert a KÉPET érinti):** ha a 3) pont
magyarázata helyes, az érdemi javítás a publikálás atomikusságának
felbontása - darabokban cserélt fedés, ami átmenetileg lyukat vagy
átlapolást adhat a képen (az ND-85 pont ezt zárta ki). Ez képi kompromisszum,
tehát nem dönthető el mérés nélkül és felhasználói elfogadás nélkül.

### ND-189 — Folyóforrás ne induljon tó alól vagy tengerszint alól (LEZÁRVA 2026-10-03, SEED-TÖRŐ: generátor 9 → 10)

2026-10-03. Az ND-187 mérése szerint a 96 forrásból **17 egy látható tó
alatt**, **2 pedig a tengerszint alatt** van a követő FINOM mezőjén. Az utóbbi
kettő adja a 0,00 km hosszú ágakat (`#72`, `#81`), az előbbiek pedig azokat a
folyókat, amelyek „a semmiből”, egy tófelület közepéből indulnak. A
felhasználó ezt a „nagyon rövid folyók” és a „szárazföldön kezdődik” panasz
részeként jelezte.

**Ok.** A `SelectRiverSources` / `SelectRiverSourcesPerBasin` a DURVA
(level 5) csapadék- és elevációmezőn dönt, és a `minElevAboveSeaM` = 300 m
küszöböt is ott értékeli ki. A nyomkövető viszont a FINOM mezőt
(`source.Level + DefaultFineDepth`) látja, ahol ugyanaz a tile-középpont már
tenger alatt vagy egy feltöltött medence alján lehet.

**Opciók:** (A) a forrás-jelöltet a KÖVETŐ saját finom mezőjén ellenőrizzük
(eleváció > tengerszint, és ne legyen a priority-flood feltöltött szintje
alatt), és aki elbukik, azt kihagyjuk a kvótából — pontosan a mért hibát
javítja, de minden forráslista és így minden folyóhálózat új; (B) a durva
küszöböt emeljük (pl. 300 → 600 m) — olcsóbb, de nem a valódi okot kezeli
és véletlenszerűen más forrásokat is kidob; (C) marad.

**DÖNTÉS: (A)**, a felhasználó választása szerint. **SEED-TÖRŐ**: a
generátorverzió **9 → 10**, a régi `worldpkg` elutasítása és a
hőmodell-gyorsítótár érvénytelenítése automatikus.

**MÉRT EREDMÉNY (t=0, 96 ág):** tengerszint alatt kezdődő ág **2 → 0**,
teljes hossz 46 641,008 → **47 713,082 km** (+2,3%), összefolyás 18 → 19,
`Pit` 11 → 10, tóban végződő 32 → 29, tóban kezdődő 17 → **15**.

**A tóban kezdődő 15 ág: MÉRÉSSEL kiderült, hogy ez NEM modellhiba.**
Megpróbáltam egy második szűrőt is — „álló-víz teszt": a jelölt akkor
használható forrás, ha a követő saját jelölt-köréből van lefelé vezető
irány (vagyis a nyomvonal nem futna azonnal medence-átvágásba). A modellből
jön, determinisztikus, és nem függ a megjelenítés felbontásától (a
megjelenített tó-rétegből szűrni I1-sértés lenne, mert a `hydrologyLevel`
beállítás befolyásolná a VILÁGOT). A valódi t=0 hálózaton mérve a kimenet
**bitre változatlan** lett: 47 713,082 km, 512 398 pont, `startsInLake = 15`
— vagyis **a szűrő egyetlen forrást sem utasított el**. Ezért visszavontam:
nem tartunk fenn mérhetetlen hatású kódot a kritikus úton (forrásonként 8
plusz eleváció-hívás lett volna).

A magyarázat a felbontás-különbség: a „tóban kezdődik" mérés a level 8-as
tó-RÉTEGEN történik, a forráspont viszont a level 9-es finom mezőn van, és
ott van lefelé vezető irány — a forrás a tó-tile-on belül egy magasabb
ponton fakad. A felhasználó ebből semmit nem lát, mert a tó-tile-okon futó
szakaszt az ND-187 óta nem rajzoljuk: a folyó a tó PARTJÁN kezdődik a képen.

### ND-185 — Saját HDRP folyójelölő shader és képi elfogadás

2026-10-03. A natív képpárok kézi ellenőrzése cáfolta a korábbi automatikus
„látható pixel” értelmezést: a bekapcsolt folyó sötétedést is okozott,
nem kék vonalat. A puszta abszolút RGB-különbség ezért nem megfelelő kapu.
A ND-181/184 képi elfogadási állításai addig nem érvényesek, amíg tényleges
kék, keskeny vonalat nem látunk. Külön `WorldGen/RiverOverlay` shader,
külön saját anyag: HDRP ForwardOnly, késői rajzolás, ZWrite Off,
ZTest LEqual, állandó kategória-kék (nem napfényfüggő). A szélesség a
modell vízhozamsúlyából és minimum 4 képernyőpixelből következik.
A régi közös víz-/terepshader folyókra bevezetett UV1-szélesítése és
fragment-jelölése megszűnik, így nem befolyásolhatja a többi réteget.
A shader Always Included beállítást kap a runtime `Shader.Find` miatt.
A Core útadata változatlan. Kapu: a kék vonal valóban látszik a normál és
távoli kamerában; sötétedés vagy csillagzaj nem számít találatnak.

Ellenőrzés: teljes natív Play PASS; kész finom hálózat alap/távoli/közeli
képen 449 / 374 / 25 912 kék pixel, kézi képi ellenőrzéssel. Négy natív
EditMode eset PASS; viewer Release 0 hiba. Felhasználói átvétel és B3
nyitott. [Napló](../history/2026-10-03-a8-hdrp-river-visibility.md).

### ND-184 — Tartós folyó-áttekintés (ELUTASÍTVA, ND-185 kiváltja)

2026-10-03. Kísérlet: a finom számítás után is az 1 km-es áttekintést
megtartani, a kanonikus 50 m-es adatot külön tárolva. A javaslat hibás
képi mérésből következett: az abszolút RGB-eltérés sötétedést is sikernek
számolt. Az ND-185 shaderével az eredeti finom hálózat valódi kék vonala
is megjelenik alap-, távoli és közeli nézetben. Ezért a kerülő megoldás
visszavonva: a teljes finom adat elkészültekor átveszi az áttekintés helyét,
a panel „finom hálózat”-ra vált. A Core bemenete változatlan.

### ND-183 — Viewer követési paraméterek változtatása (ELUTASÍTVA)

2026-10-03. A kanonikus 1000 m / fineDepth=4 próba 202, az
1000 m / fineDepth=8 próba 108 eltérő belső bolygópixelt adott, miközben
a kamera-prioritásos teljes áttekintés ~10 200-at. Ez abszolút RGB-eltérés,
nem igazolt látható folyó; az ND-185 a korábbi értelmezést cáfolta.
A kísérleti alapbeállítás és egyszeri migráció visszavonva;
a végleges Core-kiértékelés bemenete továbbra is 50 m / fineDepth=4.
Ez dokumentált, elutasított próbálkozás; nem modelljavítás.

### ND-182 — Képernyőn szélesített folyószalag kétoldalas raszterezése (ELUTASÍTVA)

2026-10-03. Az eredeti kamerás teljes menetben a finom hálózat abszolút
RGB-eltérése jelentősen visszaesett az áttekintés után. Hipotézis: a néhány század
pixelenként mintavételezett finom út a vertex-shaderben 4 pixelre szélesedik;
fordulóknál a rasztertérbeli háromszög-bejárás megfordulhat, miközben a
fizikai szalag háromszöge helyes. A folyó saját anyaga ezért `Cull Off`-ot
kap; a közös shader többi anyaga marad `Cull Back`. A mélységteszt érvényes,
a bolygó túloldala nem látszik át. A Core-út/width modell változatlan.
Ugyanazon teljes finom mesh-en Back/Off képpár: 106 / 108 eltérő pixel
a bolygó belső körén belül. Nem oldja meg az eltűnést, ezért a kísérleti
anyag- és Cull-beállítás visszavonva. Az eltérés nem kékfolyó-bizonyíték
(ND-185); a Cull mód önmagában nem javította a közös shader megjelenítését.
[Unity ShaderLab Cull](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-Cull.html).

### ND-181 — Korai bolygószintű folyó-áttekintés és távoli láthatóság

2026-10-03. Új felhasználói elutasítás: közel és messziről sincs folyó.
A `PerfLog_20261003_001543.txt` csak egy publikált finom ágat tartalmaz,
a teljes hálózat nem készült el. A korábbi célzott, folyó fölé helyezett
kamerás kép ezért nem igazolja a szokásos kezdő bolygónézet használhatóságát.

A négyworker-es munka előbb 1000 m lépésközzel futtatja ugyanazt a Core
folytonos követőt (nem a korábbi TileId-középpontos lépcsős konvertert).
1/4/6/16/48 és a teljes hálózat állapotában publikál; a hat kezdeti
forrás a nagy vízgyűjtők kezdő körét fedi. Utána ugyanazon workerkerettel
lefut az eredeti finom követés. Az áttekintés megmarad a teljes finom
hálózat elkészültéig, hogy néhány finom ág ne tüntesse el a többi folyót.
A panel külön áttekintési és finom ágszámot jelez; nem mutat hamis kész
állapotot. Generáció- és megszakításvédelem mindkét fázisban kötelező.
A finom eredmény számai és hash-e változatlanok, nincs seed-/verzióváltás.

Az eredeti kamerás első geometriai ellenőrzésnél a 6 ág pontjai a kamera
ellentétes félgömbjén voltak: a legnagyobb vízgyűjtők forrásai nem
garantálnak kamera felőli ágat. A láthatóság shaderoldali bizonyítéka ND-185.
Ezért kizárólag az áttekintés forrássorrendjét a kamera felőli félgömbre
priorizáljuk (forrásirány · kamerahelyi irány, determinisztikus TileId tie).
A finom követés az eredeti kanonikus forrássorrendet használja. Az áttekintés
nem bitazonos végleges világadat, hanem felbontás-/nézetfüggő modellkiértékelés.

A minimális teljes szalagszélesség 2 → 4 képernyőpixel, közelről a nagyobb
vízhozamfüggő geometriai szélesség megmarad. A scene kezdőnézete nagyrészt
éjszakai; a kapcsolható folyóvonal ezért térképi modelljelölésként saját
kategóriaszínét adja, nem sötétedik a Nap szerint. A fizikailag árnyalt
tó-/óceánfelszín változatlan, a folyó mélységtesztje megmarad (nincs
bolygón átlátszó vonal). A kamera kezdeti irányát a kamera inicializációja
előtt is helyesen kell olvasni; a scene transformja eltérhet a futó nézettől.
Elfogadási ellenőrzés: a scene eredeti kamerája, valamint
annál távolabbi bolygónézet; a kamera folyóra igazítása nem helyettesíti
ezt. A teljes áttekintés többletköltségét és az első 6 ág idejét külön mérjük.

**Kiegészítés (2026-10-03, MÉRT): miért nem látott a felhasználó egyetlen
folyót sem.** Két, egymástól független ok, mindkettő az élő Editorban mérve,
egyik sem a Core-ban — a 96 ágú hálózat KÉSZ volt, csak nem jutott ki a képre.

*(1) A Play-menet megáll, amint a Unity ablak elveszti a fókuszt.*
`PlayerSettings.runInBackground` 0 volt. A folyó-finomítás dedikált
(LongRunning) háttérszálon fut, ezért VALÓS időben tovább dolgozott, de a
`TryApplyCompletedRiverRefinement` a fő szálon van: frame nélkül nincs
átvétel. A mérés pillanatában `_pendingRiverNetwork` = 96 kész ág
(114 352 pont), `_adaptiveRefinedRiverPaths` = 6 áttekintő ág,
`Time.time` = 50,9 s, miközben a folyó-stopper 306,8 s-on állt — vagyis a
fő szál a Play első 51 másodpercén ragadt. Amint `Application.runInBackground`
igazra váltott, 9 másodperc alatt `refined = 96`, `preview = False`,
`fineCompleted = 96`, és felépült a 919 946 vertexes finom folyómesh.
Javítás: `runInBackground: 1` (ProjectSettings) — a viewer hosszú
háttérszámítása nem múlhat azon, hogy melyik ablak az aktív.

*(2) A fix ágszám-küszöbök (1/4/6/16/48) 19,5 másodpercre 6 ágon ragadtak.*
A `BuildContinuousRiverNetworkFromSourcesParallel` a kész ágakat SZIGORÚAN
forrás-sorrendben commitálja (a `claimed` szemantika reprodukálása —
determinizmus-követelmény, nem változtatható), ezért a commit-időpontok
erősen egyenetlenek. Mérve (`RiverBaseline`, seed 0xA7C944210000, level 5,
4 worker, 1 km-es áttekintés): 1. ág **0,67 s**, 4. ág 1,01 s, 6. ág
**1,57 s**, 16. ág **21,15 s**, 96. ág **36,53 s** — tehát 1,6 és 21,1 s
között EGYETLEN frissítés sem volt, és egy másik futásban az utolsó 18 ág
ugyanazon a 37,67 s-on commitált. A fix küszöbök így nem haladást mutattak,
hanem véletlen pillanatokat. Javítás: időalapú publikálás
(`RiverPreviewPublishIntervalSeconds` = 1,0 s; az első és az utolsó ág
mindig publikál). A hálózat kimenete ettől BITRE változatlan — kizárólag
megjelenítési ütemezés, nincs generátorverzió-emelés.

*(3) A 6 ág tényleg a túloldalon volt — de nem a priorizálás hibájából.*
A mért dot-sorozat (forrásirány · kamerairány) a publikált listán teljesen
vegyes (−0,80 … +0,99; a 96-ból 47 esik a látható féltekére), vagyis a
sorrend NEM a mérés pillanatának kamerairányára rendezett: a nézet a Build
óta elfordult, a már commitált sorrend pedig befagyott. Ezt a (2) javítás
kezeli — ha a hálózat másodpercenként bővül, néhány tíz másodperc múlva
minden ág látszik, a nézet állásától függetlenül. Kamera-forgatásra NEM
indítunk új folyó-számítást (az a teljes 37-60 s-os láncot újraindítaná).

### ND-188 — Az áttekintés lépésköze és az escape-emisszió sűrűsége (LEZÁRVA 2026-10-03: A, a MESH-költség miatt)

2026-10-03. Az ND-181 áttekintése azért van, hogy a felhasználó a teljes,
finom hálózat előtt MÁR lásson valamit. A mérés viszont azt mutatja, hogy az
áttekintés alig gyorsabb a végleges menetnél: 1 km-es lépésközzel **36,5 s /
123 926 pont**, az 50 m-es finom menettel 60,8 s / 504 060 pont (ugyanaz a
seed, 4 worker, ugyanaz a gép). Vagyis a 20-szoros lépésköz-növelés csak
**1,7-szeres** időnyereséget ad.

MÉRT OK: az ND-186 óta a teljes hossz 58,77%-át adó escape-szakaszokat a
`DefaultContinuousEscapeEmitMeters` = 250 m FIX sűrűséggel mintavételezzük,
a lépésköztől függetlenül; a lokális priority-flood költsége pedig szintén
nem a lépésközzel skálázódik. Az áttekintés tehát ugyanazt a drága
medence-átvágást végzi el, mint a finom menet.

Opciók: **(A)** az escape-emisszió legyen lépésköz-arányos (pl.
`max(250 m, 5 × stepMeters)`) — ekkor az áttekintés tényleg durvább és
gyorsabb, de az áttekintő vonal a medencéken átvágva elszakadhat a
megjelenített felszíntől (a viewer minden pontot a domborzatra ültet);
**(B)** az áttekintés hagyja ki az escape-et, és a medencébe érő ágat ott
zárja le (Pit) — gyors és fizikailag olvasható, de az áttekintésen a folyó
rövidebbnek látszik, mint a véglegesen; **(C)** marad a mai állapot, és az
áttekintés helyett a progresszív publikálásra (ND-181 kieg. (2))
támaszkodunk — nincs új kód, de a féltekét kitöltő hálózat csak ~20-35 s-nál
áll össze.

**MÉRÉS (2026-10-03).** A `RiverBaseline` `lakecross` módja mostantól a
lépésközt ÉS az escape-emissziót is söpri. Seed 0xA7C944210000, t=0, 96 ág,
4 worker, UGYANAZ a bináris és gép:

| lépésköz | escape-emisszió | hálózatidő | pont | csúcsmemória |
|---|---|---|---|---|
| 1 km | 250 m (mai) | 40,3 s | 127 514 | 111,4 MB |
| 1 km | **5 km** | 44,5 s | **25 715** | **89,6 MB** |
| 1 km | 20 km | 42,4 s | 22 263 | 88,9 MB |
| 50 m (finom) | 250 m | **85,0 s** | 512 398 | 164,1 MB |

Két dolog derült ki, mindkettő cáfolja a bejegyzés eredeti feltevését:

1. **Az emisszió sűrűsége a hálózatidőt EGYÁLTALÁN nem befolyásolja**
   (40,3 / 44,5 / 42,4 s — a három érték a futások közti zaj szintjén van).
   A költség a lokális priority-floodban van, nem a pontok kiírásában. Tehát
   az (A) opció NEM gyorsítja az áttekintést — az eredeti indoklás hibás volt.
2. **Az áttekintés a mért 2,0× nyereséget mégis megadja** (40,3 s vs 85,0 s a
   finom menetre), tehát önálló áttekintő menetre van értelme — a korábban
   idézett 1,7× két külön futás összevetése volt, ez a szám most ugyanabból a
   söprésből jön.

**DÖNTÉS: (A), de MÁS indokkal — a MESH-költségért, nem az időért.** Az
áttekintés escape-emissziója a viewerben 5 km
(`RiverOverviewEscapeEmitMeters`, = 5× lépésköz): a pontszám ötödére esik
(127 514 → 25 715), a csúcsmemória 111 → 90 MB, és ezt a VIEWER fizeti meg —
minden pontot a megjelenített felszínre kell vetíteni és szalaggá építeni. Az
áttekintő folyómesh így 110 986 vertex helyett ~22 000 lesz, vagyis a
felhasználó nagyságrenddel hamarabb lát TELJES hálózatot. 20 km-nél a pontszám
már alig csökken (telítődés), viszont az áttekintő vonal jobban elszakadna a
domborzattól, ezért 5 km a választás.

**Nem seed-törő:** ez kizárólag az ÁTTEKINTŐ menet paramétere; a végleges
finom hálózat (és annak minden száma) bitre változatlan. A Core
`DefaultContinuousEscapeEmitMeters` = 250 m marad.

### ND-180 — Folyóalak: igazolt modellhiba, numerikus javítási kapu (LEZÁRVA, ND-186)

2026-10-02. A teljes aktuális t=0 CLI-hálózat bitazonos A8-hash-sel:
48 879,793 km összhossz, ebből **58,77%** a 75 méternél hosszabb éleken;
a normál lépés 50 m. A lokális escape 2 / 2,828 km-es rácséleket fűz
hozzá közvetlenül, nem finomítja tovább őket. Ez cáfolja az ND-49 korábbi
általánosítását, hogy az escape-szakaszok a látvány szempontjából mindig
ritkák és rövidek. A legnagyobb összefolyási záróél **24,113 km** (29. ág):
a `source.Level + DefaultFineDepth` itt L9; a claimed térkép egy egész
ilyen cellához az első befogadó pontot tárolja, valódi közelségvizsgálat
nélkül. Ez a rövid/hamis összefolyások külön oka. A nyolc irányra korlátozott
normál lejtés további iránykvantálást okoz.

**Opciók:** (A) viewer-spline: nem javítja a fizikai út vagy a túl korai
összefolyás hibáját, ezért elvetve; (B) csak kisebb escape-cellák: költség
négyzetesen nő, az összefolyást és iránykvantálást nem javítja; (C) modellben
valódi térbeli közelségvizsgálat + terephez kötött folytonos lejtésirány +
vízszint-/medencehű escape-kezelés. Javasolt: C, külön Python-orákulummal,
szintetikus sík/völgy/medence/közeli és távoli meder tesztekkel, majd C# port.
Elfogadási kapu: nincs többkilométeres összefolyási teleport; hurokmentesség,
torkolat-/tókapcsolat és minden platformon párhuzamos/szekvenciális egyezés.

Szakmai kiindulópont, nem már implementált algoritmus:
[Tarboton 1997](https://hydrology.usu.edu/dtarb/dinf.pdf) háromszögfacettekből
vezet le folytonos lejtésirányt, csökkentve a D8 iránytorzítását;
[Barnes et al. 2014](https://doi.org/10.1016/j.cageo.2013.04.024) a medencék
kitöltését és lefolyási kezelését tárgyalja. A bolygó folytonos nyomkövetője
külön adaptációt és validációt igényel.

**Kompatibilitás:** C új numerikus folyóhálózatot adna, ezért A8 régi
hash-ének megtartása nem célja; ez B3 modelljavítás. Aktiválás előtt a
generátor 8 → 9 emelése, explicit régi-worldpkg elutasítás és cache-
érvénytelenítés ellenőrzése kötelező. E bejegyzés diagnózis és terv:
a futó Core továbbra is változatlan, nincs csendes numerikus csere.
Bizonyíték: `artifacts/a8-geometry-current/t0-geometry.csv`.

**LEZÁRVA 2026-10-03, ND-186.** A javasolt „C" opció 1. köre elkészült: mind a
három hibaosztály (iránykvantálás, hamis/teleportáló összefolyás, finomítatlan
escape-szakasz) javítva, Python-orákulummal és bitre egyező C# porttal,
generátorverzió 8 → 9. A medencék TÓ-kérdése külön, mért döntésként nyitva
marad: **ND-187**. A B3 látványítélet továbbra is a felhasználóé.

### ND-179 — Folyómesh korlátos feltöltése és atomikus cseréje

2026-10-02. Az ND-178 valódi Unity-menetében a végleges mesh feltöltése
51,1 ms volt: a CPU-előkészítés szeletelése után ez még egyetlen nagy
Unity-hívássor. Legfeljebb 16 384 vertexes részeket építünk, a határon
a valódi modellpontpárt megismételve. Egy képkocka egy részt tölt fel;
az új gyökér inaktív marad, amíg minden része kész. A teljes aktuális
generáció egyetlen gyökércserével publikálódik, utána a régi saját mesh-ek
felszabadulnak. Megszakítás a még rejtett részeket is felszabadítja.
Nincs Core-/seed-változás vagy spline-simítás; a szögletes modellút
külön, méréssel alátámasztott numerikus döntést igényel. A kisebb részek
több draw callt jelentenek; a feltöltési csúcsot és az új geometria
azonosságát Unityben ellenőrizni kell, nem tekintjük előre perf-PASS-nak.

### ND-178 — Folyószalag minimális képernyőbeli szélessége

2026-10-02. A 0,008 egységnyi fél-szélesség bolygónézetben szubpixeles:
a folyómesh megléte nem biztosít látható folyót. A viewer a valódi modellág
középvonala körül legalább 2 pixel teljes szélességet rajzol. A nagyobb,
vízhozamfüggő geometriai szélességet megtartja; közelről nincs további
vastagítás. A középvonal és a mélységteszt változatlan, nincs új folyóág,
dekoratív toldás vagy Core-/seed-változás. A szalag középpontját UV1-ben
adjuk a közös víz-shadernek; csak a megjelölt folyóvertexek szélesednek,
a tavak és óceánok nem. A világítás továbbra is a közös vízmodellé.
A felhasználói láthatósági elfogadás nyitott; a korábbi kép nem elegendő.

### ND-177 — Kanonikus finom folyóágak progresszív publikálása (A8)

2026-10-02. A rácsközéppontokat követő ND-145 előnézet vizuálisan
elutasítva. Helyette a finom nyomkövető már véglegesített ágai jelennek
meg: a spekulatív követés párhuzamos marad, a claimed commit továbbra
is szigorúan forrásindex szerinti. A követés és commit átfedhet, mert
a spekulatív utak nem olvassák a claimed térképet. Egyetlen zárolás
védi a kész eredmények publikálását és a kanonikus commitot.
A callback csak teljes, kanonikusan csonkolt ágat kap; az ágaival együtt
a befogadó korábbi ág is már kész. A viewer 1/4/16/48 ágnál kér részleges
megjelenítést, majd a teljes hálózat zárja a folyamatot. A generáció- és
megszakításvédelem minden részletre is érvényes. Nincs új random,
numerikus döntés vagy végleges pontmódosítás; az eredmény bitazonosságát
a szekvenciális orákulumhoz és a callback nélküli úthoz mérjük.
Ez a lépcsős ELŐNÉZET javítása; a Pit-ágak fizikai értelmezését nem oldja
meg. Az utóbbihoz külön mért modell-döntés szükséges, nem dekoratív
vonaltoldás.

**Megvalósítás és ellenőrzés:** a rácsos konverter törölve. A meglévő
Rivers objektum is minden mesh-publikációnál a közös víz-shadert kapja,
vertexszínnel; a HDRP/Lit külön expozíciós útja helyett ugyanazon a
világítási úton fut, mint a többi víz. A fél-szélesség 0,08 → 0,008
Unity-egység a kódban és a PlanetView scene-ben. Ez szimbolikus
megjelenítési szélesség, nem fizikai mederszélesség-becslés. A panel a
finom ágak valódi darabszámát és a számítás/kirajzolás/kész állapotát írja.
A teljes 96 ágú szekvenciális és progresszív Core-hash azonos; a 11 Pit
végpont mind megjelenített L8 tóra esik a mért alapvilágban. Két tényleges
Unity Play-menet elkészült (96 ág, 25 összefolyás), nappali HDRP-kép is
van. A felhasználói vizuális elfogadás, kontrollált frame-/memóriaprofil és
gyors deep-time-váltás átvétele továbbra is külön kapu.
