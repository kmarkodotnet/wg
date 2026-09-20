# 2026-09-20 — GPU/CPU eleváció-eltérés számszerűsítve (ND-120)

## A feladat

A todo.md 2. sora: „a scene-ben `useGpuClassification: 1` **aktív**, de a
`TileClassification.compute` `BaseElevationF`-je az ND-52 előtti képletre lett
visszaállítva… offline meg tudom számolni, hány tile osztályozása fordul át".

## Amit a vizsgálat talált — a premissza részben hibás volt

**A jelenetbeli kapcsoló tényleg 1 volt** (a C# alapérték `false`, a shader
saját kommentje szerint „jelenleg KIKAPCSOLVA" — mégis 1). **De a GPU-ág ma
elérhetetlen:** a `PrecomputeClassificationsInParallel` mindhárom hívója
`forceCpu: true`-t ad (az ND-47 3. fázisa óta: worker szálról a GPU-dispatch
tilos), a `BuildStaticBaseLayer` sűrű ága pedig eleve a tiszta CPU-s
`PrecomputeStaticClassificationsInParallel`-t hívja.

Ezt nem a kódolvasásra bíztam: a **310 PerfLog** átvizsgálása szerint
`usedGpu=True` **utoljára 2026-09-11-én** fordult elő, azóta egyszer sem
(1966 régi előfordulás, 0 az utóbbi 9 napban).

Vagyis: **nincs élő CPU/GPU eltérés.** A jelenetbeli `1` halott kapcsoló volt,
ami élőnek látszott — ez maga a veszély.

## A szám, amit a feladat kért

Az összehasonlítást a Core publikus API-jával építettem fel: ugyanaz a
`WarpPosition` → `TwoBestDots` → `ComputeNoiseBasis` → bázis + uplift lánc,
egyszer teljesen, egyszer a shaderből hiányzó tagok nélkül. A tengerszint
mindkét oldalon a CPU-ból jön (a shader is konstansként kapja).

| hiányzó tag | \|Δ\| átlag | \|Δ\| max | óceán/szárazföld átfordulás | biome-átfordulás |
|---|---|---|---|---|
| csak ND-52 (másodlagos zaj) | 282,0 m | 890 m | **21,83%** | 23,43% |
| csak ND-90 (határkeverés) | 23,4 m | 3071 m | 0,39% | 0,40% |
| **a shader tényleges állapota** | **300,8 m** | **3116 m** | **21,99%** | **23,60%** |

Level 8-on (393 216 tile) gyakorlatilag ugyanez: 22,13% / 23,78% — az arány
**skála-stabil**.

A két tag jellege eltér, és ez a javítási sorrend szempontjából fontos: a
másodlagos zaj **globális** (mindent elmozdít, korlátos amplitúdóval, ezért
sok átfordulás), a határkeverés **lokális** (kevés tile, de ott nagyobb ugrás
— innen a 3071 m-es maximum 0,39% átfordulás mellett).

Épelméjűségi ellenőrzés: a `SecondaryNoiseAmplitudeMeters = 900`, tehát a
282 m-es átlag és a 890 m-es maximum pontosan az elvárt nagyságrend.

**A szám ALSÓ KORLÁT.** A mérés mindkét oldalon float64-gyel fut, kráter és
deep-time erózió nélkül, t=0-ban — a shader float32-es pontosságvesztése és
bármilyen egyéb elcsúszás nincs benne.

## Amit tettem

- `useGpuClassification` a jelenetben **1 → 0**. Ma viselkedésben semleges
  (az ág elérhetetlen), pusztán megszünteti a félrevezető állapotot, és
  összhangba hozza a C#-alapértékkel meg a shader saját kommentjével.
- A mérés `tests/WorldGen.Core.Tests/Tectonics/GpuShaderElevationParityTests.cs`-be
  zárva — laza küszöbökkel (nagyságrend, nem darabszám), plusz egy
  „a teljes út önmagával 0 eltérés" horgonnyal, ami a mérő-keret elromlását
  fogná meg.
- Figyelmeztető blokk a `useGpuClassification` mező mellé, a mért számokkal:
  a bekapcsolás nem gyorsítás, hanem egy **másik bolygó** osztályozása a
  mostani geometria alatt.
- **ND-120** nyitva: törlés (A) / shader felzárkóztatása (B) / marad így (C).
  Javaslat: **(A)**.

## Tanulság

**A „nem fut" és a „nem futhat" két különböző állítás, és mindkettőt mérni
kell.** A statikus olvasás azt mondta, a GPU-ág elérhetetlen (3 hívó, mind
`forceCpu: true`) — de a naplókban 1966 `usedGpu=True` volt. A kettő
feloldása (régi futások, a viselkedés 2026-09-11-én változott) csak a naplók
időrendi átnézéséből jött ki. Ha megállok a kódolvasásnál, vagy azt hiszem,
hogy „sosem futott", vagy azt, hogy „most is fut" — mindkettő téves lett
volna.

**Egy halott kapcsoló, ami élőnek látszik, rosszabb, mint egy hiányzó
funkció.** A jelenetbeli `1` senkit nem zavart, amíg nem nézett oda — de ha
valaki egy `forceCpu: true`-t eltávolít teljesítmény-okból, a világ egynegyede
csendben átsorolódik. A veszély nem a kódban volt, hanem a kód és a
konfiguráció **eltérésében**.
