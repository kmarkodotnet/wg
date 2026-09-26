# A12 / ND-19 — floating origin, 1. kör: a technika eldöntve, a mag kész

**Dátum:** 2026-09-27
**Ág:** `a19-plate-frame-noise`
**Kiindulás:** `todo2.md` → „A12 🟡 — ND-19, floating origin"
**Döntés:** `docs/04-decisions.md` → **ND-19** (nem új ND-szám; a meglévő
nyitott kérdés lezárása + új alszakasz)

---

## Amit a régi ND-19 állított — és ami megdőlt

Az ND-19 2026-09-13-as szövege szerint a `float32` precíziós probléma „csak
akkor jelentkezne, ha a kamera valós, km-skálájú bolygófelszín közelébe
kerülne", és külön kiemelte, hogy „a bolygó jelenleg önkényes `radius=100`
Unity-egységben van, nem valós méretben". Ez utóbbi mondat implicit azt
sugallta, hogy a kis lépték *védelem*.

**Nem az.** A `float32`-nek nincs absztrakt felbontása, csak **relatív**
(2^-23 … 2^-22, a bináris oktávon belüli helytől függően). Ugyanarra a
7420 km-es bolygóra mérve:

| modell-sugár | ULP (modell-egység) | fizikai kvantálás a felszínen |
|---|---|---|
| 100 (a mai viewer) | 7,629e-06 | **0,566 m** |
| 7 420 000 (valós lépték) | 0,5 | **0,500 m** |
| 1 (egység-gömb) | 1,192e-07 | **0,885 m** |

Három nagyságrendileg teljesen eltérő lépték, és a fizikai hiba 2x-es
faktoron belül azonos. Következmény: a `radius` megválasztása **nem
precíziós kérdés**, és a méterszintű közeli zoom (A11, M9 régió-nézet)
**semmilyen** radius-értékkel nem érhető el — kizárólag origó-eltolással.
(Teszt: `FloatingOriginTests.SurfaceQuantizationIsScaleInvariant`.)

Az „egyelőre nem látszik" rész viszont igaz, és most már számmal:
`OrbitSurfaceMath.MinimumClearance(100.1, 100, nearClip)` miatt a
legközelebbi kameramagasság **~7,4–24,5 km** (a `nearClip`-től függően),
ahol 0,57 m szögben ~2e-5 rad.

## A választott technika

**Kamera-illesztett, rács-illesztett (snapped) origó.** Kulcspontok:

- A rács-lépés a kamera felszín feletti magasságánál nem nagyobb legnagyobb
  **2-hatvány**. Így (a) az origó legfeljebb ~magasságnyira van, tehát a
  képernyőn releváns geometria lokális koordinátái a magasság
  nagyságrendjébe esnek, és (b) rebase csak akkor kell, ha a kamera a saját
  magasságával összemérhető utat tett meg — ekkorra a látvány is átfordult,
  tehát az újraépítés nem többletköltség. A 2-hatvány azért kell, hogy az
  origó tagjai pontosan ábrázolhatók legyenek.
- **Hiszterézis: 1,5 × rács-lépés.** Frissen illesztett origó tengelyenként
  ≤ 0,5 cellányira van, tehát a cellahatáron ülő kamera nem billeghet két
  origó között. Enélkül minden képkocka teljes geometria-újraépítést kérne —
  ez az a hibaosztály, ami csak élő Play-ben, teljesítmény-összeomlásként
  jelentkezne. Teszt: `CameraJitterAtACellBoundaryDoesNotRebase` (200
  képkocka billegés → **0** rebase), és a párja
  `SustainedTranslationEventuallyRebases` (10 egység út → 3–12 rebase, nem
  nulla és nem száz).
- **A kivonás DOUBLE-ban, a cast UTÁNA.** Ez a floating origin egésze.

Mért, elérhető felbontás a viewer léptékén (74 200 m/egység):

| kameramagasság | rács-lépés | rebase-út | elért felbontás |
|---|---|---|---|
| 1 000 km | 8 egység (593,6 km) | 890,4 km | 70,8 mm |
| 100 km | 1 egység (74,2 km) | 111,3 km | 8,85 mm |
| 24 km (mai minimum) | 0,25 egység (18,55 km) | 27,8 km | 2,21 mm |
| 1 km | 7,8125e-03 (579,7 m) | 869,5 m | 0,069 mm |
| 10 m | 1,2207e-04 (9,06 m) | 13,6 m | 0,0011 mm |

## Elvetett alternatívák

| alternatíva | miért nem |
|---|---|
| **Per-chunk pivot** (minden mesh-darab a saját középpontjához relatív) | Ugyanezt a precíziót adná, de a csúcsok chunk-határon nem lennének **bitre azonosak** két szomszédos chunk között → hézagok/T-illesztések, amiket az ND-70 sarok-megosztás pont most szüntetett meg. |
| **Logaritmikus depth** | A **depth**-precízió eszköze, a **pozíció**-precízióét nem oldja meg; HDRP-ben amúgy sem szabadon cserélhető. Akkor lesz szükséges, ha a `nearClip`-et érdemben lejjebb visszük — külön, későbbi tétel. |
| **Valós léptékre váltás** (`radius` = 7 420 000) | A fenti lépték-invariancia-mérés szerint semmit nem nyer. |

## Amit csináltunk

**`unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/FloatingOrigin.cs`** —
motorfüggetlen (a `Lod/` asmdef `noEngineReferences=true`), ezért Unity
Editor nélkül, `dotnet test`-tel tesztelt:

| tag | mit ad |
|---|---|
| `Float32Ulp(magnitude)` | IEEE-754 float32 ULP double-ban. Csak 2-hatvány skálázás, tehát bitpontos és könyvtárfüggetlen — nincs `ILogB`/`ScaleB`, amik netstandard2.1 alatt nem érhetők el mindenhol. |
| `ResolutionMeters(localRadius, metersPerUnit)` | a fenti táblázatok forrása |
| `RecommendedCellUnits(altitude)` | magasság → 2-hatvány rács-lépés |
| `Snap(cam, cell)` | rács-illesztés `Math.Floor(x/c + 0.5)`-tel |
| `TryAdvance(current, cam, altitude, out next)` | a teljes vezérlés, hiszterézissel |
| `RenderOrigin.ToLocal/ToAbsolute` | a double-kivonás-majd-cast út |

A számítás szándékosan **transzcendens-mentes** (csak `+ - * /` és 2-hatvány
skálázás), hogy platformok között ugyanazt az origót válassza — így a
diagnosztikai naplók összevethetők maradnak. Az I1 amúgy sem érintett: a
világmodell egyetlen bitje sem függ tőle.

**`PlanetOrbitCamera.FloatingOrigin.cs`** — az origó **követése** a kamerából.
A modell-téri kamerapozíció a `target` **lokális, Unity-tengelyű,
skálázatlan** terében számolódik (pontosan ott, ahol a mesh csúcsai vannak),
az `AdvanceRenderOrigin` a `LateUpdate` végén fut, a végleges kamerapozíció
után. Másodpercenként egy `[ND-19 floating origin]` sor a perf-naplóba, ami
az abszolút és az origó-relatív felbontást méterben egymás mellé teszi
(`gainFactor`) — tehát a 2. kör haszna előre és utólag is mérhető.

## Az egy valódi hibalehetőség — és ahogy kizártuk

A `default(RenderOrigin)` szándékosan **érvénytelen** (`CellUnits == 0`), nem
„bolygóközép". Ugyanaz a hibaosztály-védelem, mint az A22
`DeepTimeContext` NaN-os tengerszintjénél: ha a nullérték legális,
bolygóközepű origónak tűnne, egy elfelejtett inicializálás **csendben**
visszaállítaná az abszolút, precíziót vesztő emittálást — pont az, amit
senki nem keresne. Az érvénytelen origó ezért mindig rebase-t kér
(`DefaultOriginIsInvalidWhileTheExplicitPlanetCenterIsNot`), a bolygóközepű
origót pedig explicit `RenderOrigin.PlanetCenter(cell)` adja.

## Ellenőrzés

| kapu | eredmény |
|---|---|
| `dotnet test tests/WorldGen.Viewer.LodChunking.Tests` | **560/560 zöld** (34 új `FloatingOriginTests`, 3 egymást követő futásban) |
| `dotnet build tests/WorldGen.Viewer.Compile` | **0 error** (122 warning, mind a meglévő CS8632) |
| Élő Unity `recompile` | `compilationFailed: false`, **0 console error** |
| `AssetDatabase.Refresh` + `.meta` | mindkét új `.cs` importálva, a `.meta`-k commitolva |
| Élő Play, `[ND-19 floating origin]` napló | `gainFactor=1.0` bolygó-nézetben, `256.0` a felszín közelében — ld. lent |

### Az élő mérés EGY VALÓDI HIBÁT fogott (és ezért érdemes volt bekötni)

Az első Play-menetben (`PerfLog_20260927_001916.txt`) ez állt:

```
mode=PlanetCenter origin=RenderOrigin(0,0,0, cell=128) rebases=1 altitudeUnits=199.98
  absoluteResolutionMeters=0.5661 anchoredResolutionMeters=1.132202 gainFactor=0.5
mode=PlanetCenter origin=RenderOrigin(0,0,0, cell=256) rebases=2 altitudeUnits=364.38
  absoluteResolutionMeters=0.5661 anchoredResolutionMeters=2.264404 gainFactor=0.3
```

**`gainFactor < 1`:** a jelenet bolygó-nézeti magasságai (200-364 egység egy
100-as sugarú bolygó fölött) 128-256-os rács-lépést adtak, azaz az origó
MESSZEBB került a kamerától, mint maga a bolygóközép — az „origó-relatív" út
rosszabb volt az abszolútnál. Elméletből ezt nem vettem észre, mert a
magasság-alapú cella-szabály hallgatólagosan feltette, hogy a magasság a
bolygósugár alatt van.

**Javítás:** `MaximumUsefulCellUnits(radius)` felső korlát (a legnagyobb
2-hatvány, amire `RebaseFactor * cell` még a sugáron belül van — 100-nál
**64**), plusz bolygó-nézeti magasságon (a teljes gömb látszik) az origó
explicit **bolygóközép**, faktor-2 hiszterézis-sávval a küszöb körül, és a
cella ilyenkor a felső korláton pinnelve (különben a zoom oktávonként
fölösleges „rebase-t" jelentene egy amúgy nem változó origón).

**A javítás utáni mérés** (`PerfLog_20260927_002754.txt`):

```
mode=PlanetCenter origin=RenderOrigin(0,0,0, cell=64) rebases=1 altitudeUnits=199.98
  absoluteResolutionMeters=0.5661 anchoredResolutionMeters=0.566101 gainFactor=1.0
mode=PlanetCenter origin=RenderOrigin(0,0,0, cell=0.25) rebases=2 altitudeUnits=0.378113
  altitudeMeters=28056.0 cellMeters=18550.00
  absoluteResolutionMeters=0.5661 anchoredResolutionMeters=0.002211 gainFactor=256.0
```

A második sor a felszín közelébe (28,1 km) állított kamerával készült
(`distance = 100.4`, reflexióval Play közben): **0,566 m → 2,21 mm, 256x** —
és ez **bitre egyezik** a fenti offline táblázat „24 km" sorával (2,21 mm),
tehát a modul és a mérés egymást hitelesíti.

Három új regressziós teszt köti ki a javítást:
`AnchoredResolutionIsNeverWorseThanTheAbsoluteOne` (8 magasságon, a naplóban
mért 364,4 és 199,98 is köztük), `HoveringAtTheModeThresholdDoesNotFlipModes`
`EveryFrame` (200 képkocka → 0 rebase) és
`ZoomingWithinPlanetViewDoesNotRebaseThePlanetCentreOrigin`.

**Mellékesen kiderült** (nem a mi hibánk, de tudni kell a következő élő
méréshez): `Application.runInBackground` a projektben **false**, ezért fókusz
nélküli Editorban a Play mód gyakorlatilag megáll (`Time.frameCount` 2-n
ragadt). Az MCP-n keresztüli élő mérésekhez `runInBackground = true` kell.

**Egy megfigyelt, NEM elhallgatott eltérés.** A 34 új teszt hozzáadása utáni
*első* teljes futásban a `TerrainEvaluationCacheTests.WarmViewResetAnd`
`EvaluationReuseStorageWithoutAllocating` elbukott (7984 bájt allokáció a
várt 0 helyett). Azóta 3 egymást követő teljes futás zöld, és a teszt
egyedül futva is zöld. A teszt egy dedikált `Thread`-en
`GC.GetAllocatedBytesForCurrentThread()`-et mér, ami érzékeny a tiered-JIT
átmenetre a mért ablakban — a bukás a *tesztelő*, nem a tesztelt kód
sajátja, és a floating origin egyetlen kódútat sem érint belőle. Nem
javítottam (más tétel hatóköre), de rögzítem: ha visszatér, a `Scan()`
bemelegítési körszámát kell növelni a mérés előtt.

## Ami NYITVA marad — A12/2. kör, és miért külön döntés

A geometria tényleges eltolása nem egy cast átírása. Ha a mesh csúcsai
origó-relatívak és a `transform` identitás, akkor **a bolygó tengelyforgása
többé nem kifejezhető a transform-mal**: origó körüli forgatás helyett
bolygóközép körüli kellene, amit csak `float32`-ben lehetne visszahozni —
azaz pont a megnyert pontosságot dobnánk el. A helyes megoldás a
**test-keretben (body frame) való renderelés**: a mesh transform identitás, a
**kamerát** forgatjuk a test-keretbe, a fényirányt is oda transzformáljuk.

Ez a viewer jelenet-konvencióját változtatja meg. Érintett:

- `PlanetGridMesh` sarok-cache `Vector3` → `double` (`_persistentCornerCache`,
  `_staticCornerPositions`), mert a precízió ma már a cache **írásakor**
  elveszik;
- `radialBias` (`p.normalized`) és `ToWaterVector3` — origó-relatív térben az
  abszolút irány külön kell;
- `StarField`, `SunController` — a jelenet-konvenció közös fogyasztói;
- a `PlanetOrbitCamera.distance` maga is `float` szerializált mező (ULP
  ~0,57 m a 100-as léptéken), tehát a kamerapozíciót is double-ra kell vinni.

Ezt **csak a közeli zoom (A11) mellett érdemes megtenni**, mert addig nincs
mérhető látvány-nyeresége — és a most bekötött naplózás pont azt adja meg,
mikor kezd számítani (`gainFactor`).
