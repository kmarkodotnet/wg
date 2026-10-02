# A24 — a nyári sarki meleg eredete (ND-173)

Az ND-172 lezárt részfeladata külön commit: `92f4169`. A felhasználó
folytatást kért; ebben a körben a nyári bázis és anomália szétválasztása,
majd az éves/napi transzport kontrollja készült el.

## Implementáció és mérés

A `ThermalMeltExposure` a legmelegebb mintanaphoz a nap indexét,
a bázis és a levegő-anomália napi átlagát is megőrzi. Ezek ugyanazokból
a tick-eleji mintákból származnak, mint a korábbi foknap és napi átlag.
A CLI a legmelegebb jelenlegi szárazföldi jégcellán közli a tagokat.

```text
dotnet artifacts/bin/WorldGen.Cli/Release/net8.0/worldgen.dll thermal-climate --seed A7C944210000 --plates 20 --level 5 --days 12 --melt-exposure-days 48 --parallel true
```

Alapparaméterek; a jégmaszk 12, a kitettség 48 mintanapos. A kiválasztott
cella: **5616**, szélesség **−88,012°**, magasság **96,7 m** a tengerszint
felett; a legmelegebb mintanap **273**.

| Tag | Érték |
|---|---:|
| Napi átlagos levegő-hőmérséklet | +39,8539 °C |
| Napi átlagos bázis | +39,8694 °C |
| Napi átlagos levegő-anomália | −0,015473 K |
| Radiatív bázisrész | 257,4536 K |
| Üvegháztag | +33,0000 K |
| Éves transzport korrekciója | +25,2418 K |
| Magasságtag | −0,6285 K |
| Klímaciklus | −2,0475 K |

A radiatív rész a tényleges bázisból visszafejtett maradék; szárazföldön
nincs óceáni puffer. A nyári meleg szinte teljes egészében a bázisból jön.

## Napi transzportkontroll

Ugyanazon B-menet típusmezővel és azonos diffúziós együtthatóval az ND-168
megoldóját a kiválasztott nap átlagos, éves korrekció nélküli bázisára
futtattuk. A teljes rács új egyensúlyi korrekciójából ugyanazt a cellát
olvassuk ki. Ez nem időben léptetett évszakos modell: nincs hőtárolás.

| Nap | Transzport előtti bázis | Napi korrekció | Kontroll eredménye | Max. egyenletmaradék |
|---:|---:|---:|---:|---:|
| 273, déli nyár | +14,6276 °C | +0,6717 K | +15,2992 °C | 2,300e−9 W/m² |
| 90, déli tél | −62,6506 °C | +31,8939 K | −30,7568 °C | 2,413e−9 W/m² |

A korrekció területi átlaga +8,653e−15 / −1,184e−14 K.
A kontroll numerikusan konvergált. Nyáron az éves korrekcióhoz képest
**24,5701 K-rel kisebb** a korrekció. Az éves konzervativitás tehát nem
igazolja a korrekció minden évszakban változatlan alkalmazását.
A kontroll nyara még így is +15,30 °C: ez nem kész tartósjég-megoldás.

Nyers helyi log: `artifacts/a24-daily-transport-control-l5.txt`, 70,3 s.
Korábbi, csak tagfelbontásos log: `artifacts/a24-summer-budget-l5.txt`.
Egy seed és egy rácsszint vizsgálata; nem általános bolygókalibráció.

## Szerkezeti ok és következő implementáció

`SurfaceTemperatureField.LocalStep` az anomáliát lépteti. A teljes
`T=B(t)+θ` hőmérséklet deriváltjában ezért a változó bázis automatikusan
megjelenik, a solver pedig nem kompenzálja `−C dB/dt` forrással. A
hőkapacitás növelése nem fékezi a bázis évszakos változását. Ez az
eredeti anomáliamodell tudatos felbontásának korlátja.

A jégalbedó a `F(1−a)(cos(z)−f_napi)` napi anomália-forrást szorozza.
A bázis a bolygó-albedót használja; a szárazföldi jégalbedó átállítása
nem hűti ezt a nyári bázist. A felszíni albedó közvetlen behelyettesítése
viszont visszahozná az ND-160-ban már kimért bolygó/felszín keverést.

Az ND-173 az időfüggő szezonális energiamérleg irányát rögzíti:
hőtárolás + elnyelt sugárzás − kimenő sugárzás + konzervatív transzport.
Szakmai kiindulás: [climlab EBM](https://climlab.readthedocs.io/en/stable/api/climlab.model.ebm.html).
A következő numerikus részfeladat a periodikus szezonális állapot
Python-referenciája, a sugárzási/albedó-paraméterek és a nap/év időlépés
kalibrációjával. A kétcellás energiamérleg-KAT, évvégi periodicitás,
időlépés-konvergencia és fáziskésés kapui után következhet C#-port,
explicit modell-/generátorverzió-váltás, majd a hó/jég fogyasztó.

## Ellenőrzés és állapot

- Release build: 0 hiba / 0 figyelmeztetés.
- Teljes Release solution: **1987/1987** teszt zöld (Core 847,
  viewer-LOD 664, CLI 24, App 452). Random123 KAT 9/9; 512 újragenerált
  random tesztvektor byte-egyező a verziózott készlettel.
- A kibővített diagnosztika 11 célzott tesztje Debug/Release zöld:
  Python-egyezés, bázis+anomália rekonstrukció, helyes kiválasztott nap,
  párhuzamos/szekvenciális egyezés és a korábbi hókernel tesztjei.
- A Python-vektorok második generálása byte-egyező. A HEAD-hez képest
  a régi `days`, `pdd`, `warmest` mezők minden eleme változatlan.
- A két napi transzportkontroll maradéka és területi integrálja fent.
- Az aktív szimuláció, a jégmaszk és a viewer nem változott; nem történt
  új Unity-vizuális átvétel.

Az **A24 becslése ~75% marad**: az ok most mért, de a szezonális
modell javítása még hátravan. E kör durva ráfordításbecslése **2–3
munkaóra**; a korábbi **12–24 munkaóra** hátralévő becslés továbbra is
bizonytalan, és a szezonális modell kalibrációjától függ.
