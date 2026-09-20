# 2026-09-20 — #3 FlyTo, #9 szél-overlay, #1 split-kvóta

A todo.md második táblázatának három visszajelzése, egy menetben. Mindhárom
mérésre épül; a tanulságok a végén.

## #3 — „tengelyforgásnál rossz helyre visz" (31b1d7d)

**Ok.** A `NavigateToContinent/Region/Area` a panel `CenterDirection`-jét
adta át a `PlanetOrbitCamera.FlyToDirection`-nek. A `CenterDirection` a
bolygó SAJÁT (forgatás előtti) terében van, a `FlyToDirection` viszont
VILÁGTERI irányból számol pitch/yaw-ot. A `PlanetOrbitCamera.
CurrentViewDirection` doksija maga mondja ki, hogy egy `CenterDirection`-t
előbb „világtérbe forgatva" kell összehasonlítani — ezt a lépést sehol nem
végeztük el.

`CameraViewMode.Free`-ben a két keret EGYBEESIK (a `SunController` identitáson
tartja a bolygó transformját), ezért maradt lappangó. `AxialRotation`-ben a
`SunController.cs:151` TÉNYLEGESEN forgatja a mesh-t (tengelydőlés × spin).

**Mérve élő Editorban** (bolygó euler 341,5/219,5/345,4):
`local=(0,1844, 0,9336, −0,3071)` vs `world=(0,0380, 0,7149, 0,6982)` →
**62,61°**. A javítás után a kamera pontosan a világteri vektorra állt
(0,00°); visszavetítve a bolygó lokális terébe 0,00° a célkontinens
középpontjához, míg a következő kettőhöz 171,4° és 112,4°.

**Nem javítva, tudatosan:** `AxialRotation`-ben a bolygó a repülés UTÁN is
forog tovább, tehát a célpont lassan kicsúszik. Ehhez a kamerának
folyamatosan követnie kellene a bolygó keretét — külön feladat.

## #9 — „szél-overlay percekre megállítja a képet" (5bb843a)

**Mérve** (5000 minta, élő Editor):

| művelet | idő |
|---|---|
| `ElevationGradientTangent` (4× `ComputeElevationAtPoint`) | 212 µs |
| `TemperatureGradientTangent` (4× `TemperatureKelvin`) | 28 µs |
| **teljes `WindSpeedColorAt`** | **238 µs/sarok** |
| `PrecipitationColorAt` (dictionary-lookup) | 1,10 µs |
| normál sarok-szín | 1,06 µs |

225× a normál út, ~400 000 statikus sarokra — innen a „fagyás".

**Két javítás, mindkettő MUNKÁT VESZ EL, nem közelít durvábban:**

1. Az elevációs gradiens az ND-66 base-szintű statikus sarok-pozíciókból jön
   (a sugaruk már tartalmazza a domborzatot) — legkisebb négyzetes illesztés
   a 4 rács-szomszédra, nulla új Core-kiértékelés.
2. A hőmérséklet-gradiens az ND-64 előre számolt napi Nap-irányaiból
   (`TemperatureKelvinFromSamples`). A `WindVector` közös farkát kiemeltem
   `WindVectorFromTemperatureGradient`-be; teszt igazolja, hogy **bitre
   azonos** — ez a fél tehát vizuálisan semmit nem változtat.

**Külön hiba, amit ez hozott elő:** a `PrecomputeStaticCornersInParallel`
UGYANABBAN a `Parallel.For`-ban töltötte a pozíciót ÉS a színt, így a szín
nem láthatta a szomszédok pozícióját → minden sarok a drága ágra esett
(Build 15,8 s). Két fázisra bontva: 5,7 s (overlay nélkül 3,4 s).

**Eredmény:** 238 → 6,3 µs (38×), 82 FPS, a kép sima (nincs tile-blokkosodás).

**Nyitva hagyott döntés → ND-119.** A `MoisturePrecipitation.Compute` a
`WindVector`-t `elevationGradient = 0,0`-val hívja: a SZIMULÁCIÓ szelében
nincs hegy-eltérés. Az overlay viszont rajzol egyet — tehát nem a modell
szelét mutatja (I3/I4). Opciók és javaslat az ND-ben; a felhasználó dönt.

## #1 — „közepes közelítésnél brutál nagyok a tile-ok" (1d177d5)

**Mérve offline** (1920×1080, 8 px cél → 48000 leaf-budget, level 8 alapszint,
üres előzményből konvergenciáig, JIT-bemelegítés után):

| kvóta | kérés | összes | egy kérés |
|---|---|---|---|
| 1024 | 13–17 | 326–600 ms | 2,6–57 ms |
| 4096 | 4–5 | 96–164 ms | 9,4–30 ms |
| 8192 | 3 | 58–115 ms | 17–40 ms |
| végtelen | 2 | 40–75 ms | 19–35 ms |

**A döntő tény:** a konvergált vágás MINDEN kvótánál azonos volt (48000
levél, azonos `BudgetStops`). A kvóta tehát tisztán KÉSLELTETÉS-szabályzó,
nem minőségi — az emelés mindkét irányban nyer. 1024 → **8192**; nem
16384/végtelen, mert a szelekció worker-szálon fut és mozgó kameránál
megszakad, ott a kérésenkénti idő a korlát (40 ms alatt tartva).

`SplitQuotaConvergenceTests` rögzíti az invariánst, amin az emelés áll —
enélkül ennek a konstansnak a hangolása csendben a KÉPET is átírhatná.

## Tanulságok

**A `runInBackground` nélkül a Play mód nem tickel.** Az Editor háttérben
volt, `Time.frameCount` 78 másodpercen át 2 maradt, miközben az `eval`
hívások működtek — tehát nem fagyás volt, hanem a player loop állt.
`UnityEngine.Application.runInBackground = true` után azonnal futott.
Ez magyarázza az előző kör „állott képernyőképeit" is.

**A `console get` `types` szűrője nem szűr.** Hibákat kérve 95 warningot adott
vissza és 55 KB-ot írt a kontextusba. Használj helyette `eval`-t célzott
lekérdezéssel.

**Az első mérés a JIT-et méri.** A split-kvóta első sora 103 ms-ot mutatott,
bemelegítés után 57 ms — és a többi távolságnál 2,6–16 ms. Bemelegítés
nélkül azt hittem volna, hogy a kis kvóta a drágább.

**Amit majdnem elrontottam.** A szél-overlay elevációs gradiensét először a
REFERENCIA-szintű (level 5) elevációmezőből vettem — 2,76 µs/sarok, még
gyorsabb. Mielőtt leszállítottam volna, megmértem a KÉP változását: a
szín-rámpa átlagosan 0,124-del tolódott el, a minták 36,8%-a 5% fölött.
Ez már nem „ugyanaz a kép gyorsabban", hanem más kép. A base-szintű
(level 8) változat 0,090 / 27,8%-ra jött le, és ez ment be — a különbséget
az ND-119 rögzíti. **Teljesítmény-javításnál a gyorsulás mellé a
KIMENET változását is meg kell mérni, különben a „gyorsítás" észrevétlenül
minőségi döntés lesz.**
