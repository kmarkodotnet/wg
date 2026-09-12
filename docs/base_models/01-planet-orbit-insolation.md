# Bolygótest, rács, pálya és besugárzás

## 1. Térbeli alapmodell

**Státusz:** aktív.

A bolygó felszíne hat lapból álló cubed-sphere rács. Egy `level = L` szinten
lapoldalanként `2^L` tile található, ezért a teljes tileszám:

```text
tileCount = 6 * 4^L
```

Minden tile stabil `TileId`-t, négy topológiai szomszédot és a gömbön egy
egységvektoros pozíciót kap. A fizikai mezők — eleváció, hőmérséklet,
csapadék, óceáni állapot — ezen a rácson vagy annak valamely rögzített
referenciaszintjén élnek.

A render-LOD nem változtathatja meg a világ fizikai állapotát. A jelenlegi
Viewer külön referenciaszintet használ a tengerszinthez, paneladatokhoz,
biome-okhoz és több hidrológiai művelethez.

## 2. Bolygósugár és gravitáció

### Fizikai sugár

**Státusz:** aktív, de fix.

```text
PlanetConstants.RadiusMeters = 7 420 000 m
```

Ezt jelenleg többek között a kráter fizikai méretének gömbi szögsugárrá
alakítása és a folyó méterben megadott lépéseinek szögértékre váltása használja.

### Unity megjelenítési sugár

**Státusz:** aktív megjelenítési paraméter.

A Viewer alapgömbje külön, alapból `radius = 100` Unity-egység. Ez nem a
7 420 km közvetlen leképezése. A méterben számolt domborzat külön skálán kerül
a gömbre, és a Viewer képes függőleges túlrajzolást alkalmazni.

### Gravitáció

**Státusz:** nincs egységes bolygómodell.

Nincs tömeg–sugár kapcsolatból származtatott globális gravitáció. A
krátermodell külön, fix értéket használ:

```text
g = 9.81 m/s²
```

A gravitáció jelenleg nem hat a légkörre, folyadékokra, erózióra,
hegymaximális magasságra vagy szelekre.

## 3. Pályamodell

**Státusz:** aktív, tudatosan szűkített.

A jelenlegi csillagászati modell feltételei:

- egyetlen csillag;
- körpálya, tehát excentricitás nulla;
- állandó keringési periódus;
- állandó forgási periódus;
- állandó tengelyferdeség;
- külön kezdő pálya- és forgási fázis.

A pályaszög:

```text
orbitalAngle(t) = phase0 + 2*pi*t/orbitalPeriod
```

A csillag iránya a pályakeretben:

```text
sunDirectionOrbital = (-cos(theta), -sin(theta), 0)
```

Ezt tengelyferdeségi és sajátforgási mátrix alakítja a bolygó testhez kötött
koordinátarendszerébe.

Alap Viewer-paraméterek:

| Paraméter | Alapérték |
|---|---:|
| Keringési periódus | 365,25 nap |
| Forgási periódus | 1 nap |
| Tengelyferdeség | 23,44° |
| Pályafázis | 0 |
| Forgási fázis | 0 |

Az időegységek itt konzisztensen használhatók, amennyiben `t`, a keringési és
a forgási periódus ugyanabban az egységben van. A Viewer napot használ.

## 4. Lokális besugárzás

**Státusz:** aktív.

Egy felszíni normál és a csillag iránya alapján:

```text
cosTheta = dot(surfaceNormal, sunDirection)
insolation = flux * max(0, cosTheta)
```

Az éjszakai oldal közvetlen besugárzása így nulla.

A hőmodell azonban jelenleg nem a pillanatnyi értéket használja, hanem egy
teljes forgás 24 mintájából számolt napi átlagot:

```text
dailyAverageFactor = mean(max(0, dot(normal, sunDirection(sample))))
```

Következmény: az aktív hőmérsékletmező nem mutat nappal–éjszaka különbséget,
csak szélességi, évszakos és magassági különbségeket.

## 5. Csillagfluxus

**Státusz:** segédfüggvény elérhető, az aktív hőmodellbe nincs teljesen bekötve.

A Core ismeri:

```text
stellarFlux = luminosity / (4*pi*distance²)
```

Az aktív hőmodell mégsem csillagluminozitásból és pályatávolságból számol,
hanem közvetlenül a fix Föld-szerű értéket kapja:

```text
F_peak = 1361 W/m²
```

## 6. Látható Nap és világítás

**Státusz:** aktív megjelenítés.

A `SunController` ugyanabból a Core napirányból forgatja a Directional Lightot
és helyezi el a látható Nap-korongot. Szabad kamera módban a bolygó áll, a fény
forog a testkeretben. Tengelyforgás módban a Nap világtérben marad, a bolygó
mesh fordul el.

Ez vizuális koordinátakezelés; nem ad új csillagászati fizikát.

## 7. Hiányzó fizikai kapcsolatok

- elliptikus pálya és időben változó csillagtávolság;
- többcsillagos rendszer;
- csillagtömeg, -sugár, -spektrum és -fejlődés;
- bolygótömeg és átlagos sűrűség;
- tömegből és sugárból származtatott gravitáció;
- árapály, precesszió és spin–orbit rezonancia;
- egységes Core-idő a Viewer fénye és minden klímamodul számára.

