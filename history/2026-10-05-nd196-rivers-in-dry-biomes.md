# A folyók a száraz biome-okban gazdagodnak — mért anatómia (ND-196)

**Dátum:** 2026-10-05
**Ág:** `a19-plate-frame-noise` (a mérés a `fc44ce9` commit állapotán)
**Kiváltó:** felhasználói visszajelzés — „a folyók gyakran sárga biomon láthatóak, a zöld biomban nem vettem észre… azt kértem, hogy a csapadékos területeken legyen folyók inkább"
**Döntés:** `docs/04-decisions.md` ND-196 (NYITOTT, javaslat: (b))

## Rövid válasz

A megfigyelés helyes. A legszárazabb biome (`Desert`) a folyóhosszban
**2,45-szeresen** felülreprezentált, a legnedvesebb (`Rainforest`)
**0,51-szeresen** — azaz feleannyi folyó fut rajta, mint a területaránya
indokolná. Az ok nem a biome-oldalon van: a forrás-kiválasztásnak
**nincs globális csapadék-kapuja**, a csapadék csak medencén belüli
rendezési kulcs.

## Mérőpad

Friss `PlanetGridMesh` példány a jelenet Planetjéről `CopySerialized`-elt
beállításokkal (level 5, 6144 tile, 2151 szárazföldi). Build után a
`_riverSourceCache` (96 forrás) és — a háttérszálas finomítás bevárása után —
a `_riverRefinementTask.Result` (96 folytonos nyomvonal, 484 096 pont)
reflexióval kiolvasva, CSV-be mentve, Pythonban kiértékelve. A nyomvonal-pontok
a tartalmazó referencia-tile-ra képezve adják a hossz-súlyt (ez az, ami a
szalag alatt LÁTSZIK).

## 1. A nyomvonal

210 érintett tile, ebből 198 szárazföldi (12 óceáni a torkolatoknál);
468 619 szárazföldi pont-minta.

| biome | folyóhossz% | érintett tile% | szárazföldi alap% | gazdagodás |
|---|---|---|---|---|
| **Desert** (homokszín) | **28,9** | 28,3 | 11,8 | **2,45×** |
| Tundra (szürkebarna) | 44,1 | 43,4 | 41,2 | 1,07× |
| TemperateForest (zöld) | 7,3 | 6,6 | 9,5 | 0,77× |
| Savanna (olívzöld) | 4,8 | 4,5 | 8,1 | 0,59× |
| **Rainforest** (mélyzöld) | **7,5** | 6,6 | 14,7 | **0,51×** |
| Grassland | 7,5 | 10,6 | 14,7 | 0,51× |

## 2. A források

| biome | forrás | forrás% | szárazföldi alap% | gazdagodás |
|---|---|---|---|---|
| Tundra | 45 | 46,9 | 41,2 | 1,14× |
| **Desert** | 22 | 22,9 | 11,8 | **1,94×** |
| Grassland | 10 | 10,4 | 14,7 | 0,71× |
| **Rainforest** | 8 | 8,3 | 14,7 | **0,57×** |
| TemperateForest | 7 | 7,3 | 9,5 | 0,77× |
| Savanna | 4 | 4,2 | 8,1 | 0,51× |

A források globális szárazföldi csapadék-percentilise: `median = 52,3`,
`p25 = 0,0`, `max = 98,5`. A 80. percentilis alatt **82/96 (85%)**.

## 3. A gyökérok

A `SelectRiverSourcesPerBasin` a csapadékot KIZÁRÓLAG medencén belüli
rendezési kulcsként használja. A `DefaultPrecipPercentile = 0,80` **csak a
régi, globális `SelectRiverSources` paramétere** — a medencénkénti változat
soha nem olvassa. Így a 16 legnagyobb vízgyűjtő mindegyike megkapja a 6
forrását akkor is, ha a medencében egyetlen csapadékos tile sincs.

Medencénként a 6 forrás globális csapadék-percentilise:

```
medence 16:  98,5  98,3  96,7  95,9  93,3  91,2      <- nedves, helyes
medence  2:  93,4  92,9  91,9  91,7  91,1  87,8      <- nedves, helyes
medence  3:  81,5  79,6  77,9  77,8  77,7  77,3
medence 11:  82,1  78,0  77,1  68,9  64,9  64,7
medence 12:  70,6  68,7  56,9  53,4  53,2  52,7
medence 10:  65,5  61,3  58,0  53,1  52,9  52,3
medence  7:  62,7  62,3  59,8  58,8  57,4  55,8
medence 15:  60,7  56,6  55,9  52,8   0,0   0,0
medence  1:  52,6  52,5   0,0   0,0   0,0   0,0
medence  4:   0,0   0,0   0,0   0,0   0,0   0,0      <- MIND nulla csapadeku
medence  5:   0,0   0,0   0,0   0,0   0,0   0,0
medence  6:   0,0   0,0   0,0   0,0   0,0   0,0
medence  8:   0,0   0,0   0,0   0,0   0,0   0,0
medence  9:   0,0   0,0   0,0   0,0   0,0   0,0
medence 13:   0,0   0,0   0,0   0,0   0,0   0,0
medence 14:   0,0   0,0   0,0   0,0   0,0   0,0
```

**7 medence mind a 6 forrása nulla csapadékú**, kettő részben: összesen
**48 / 96 forrás (50%)** olyan tile-ról indul, ahol a csapadék PONTOSAN NULLA.

Nem a 150 km-es forrás-szeparáció és nem a választási rang a hibás: a
rang szerinti bontásban már az ELSŐ (a medence legnedvesebb) választott forrás
is 7/16 esetben nulla csapadékú.

```
1. valasztott: median percentilis=60,7  nulla csapadeku: 7/16
2. valasztott: median percentilis=56,6  nulla csapadeku: 7/16
3. valasztott: median percentilis=55,9  nulla csapadeku: 8/16
4. valasztott: median percentilis=52,8  nulla csapadeku: 8/16
5. valasztott: median percentilis= 0,0  nulla csapadeku: 9/16
6. valasztott: median percentilis= 0,0  nulla csapadeku: 9/16
```

## 4. A háttér, ami felerősíti: a szárazföld fele csapadékmentes

Ugyanaz a világ, szárazföldi tile-ok (2151):

| csapadék-mező | nulla csapadékú szárazföld | medián | maximum |
|---|---|---|---|
| analitikus (az előnézet) | 640 (**29,8%**) | 0,0957 | 30,87 |
| hőmodell-párolgással (a mai kép) | 1124 (**52,3%**) | **0,0000** | 67,61 |

A hőmodell párolgás-bemenetével a szárazföld több mint felén pontosan nulla a
csapadék, és a medián is 0. Ezért van ennyi teljesen száraz medence. Ez
ugyanaz a nyitott kalibrációs kérdés, amit az ND-195 is hagyott (A24 /
ND-165), most konkrét számmal.

Megjegyzés: a forrás a NYERS `precipField.Precipitation`-t látja, a biome az
INTERPOLÁLT `PrecipitationAtCore`-t — ezért lehet egy nulla-nyers-csapadékú
forrás-tile biome-ja mégis zöldes. A fenti 48/96 ettől független.

## 5. Mit javaslok

ND-196 (b): a 96 forrás szétosztása a medencék TELJES csapadékával arányosan,
a fix 6/medence helyett, egy abszolút alsó kapuval (nulla összcsapadékú
medence sosem kap forrást). Megtartja a forrás-számot és a kétszintű hálózatot
(az ND-180 mért nyeresége), és teljesíti a csapadék-preferenciát. Az (a)
változat — globális percentilis-kapu — ugyanazt a célt úgy érné el, hogy
7–9 medence forrás nélkül marad, azaz épp az összefolyás-arányt rontaná, amit
az ND-180 javítani akart.

**SEED-TÖRŐ:** új forráslista → minden folyóhálózat új → generátorverzió
10 → 11, az ND-189 mintájára.

## Kódváltozás

**Nincs.** Ez a kör mérés és döntés-előkészítés; a javítás seed-törő, ezért a
felhasználó jóváhagyására vár.
