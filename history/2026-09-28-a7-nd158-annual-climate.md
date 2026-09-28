# A7 6. fázis — éves hőstatisztika, kétmenetes jégmaszk, és a mérés, ami a fogyasztói átállást megállította

**Dátum:** 2026-09-28. **Ág:** `a19-plate-frame-noise`. **ND:** ND-158.
**Állapot:** a Core adatút KÉSZ; a fogyasztói átállás MÉRT indokkal blokkolva.

## Mi volt a feladat

A todo2 A7 sora szerint a 6–7. fázisból ez maradt: „nem új algoritmus, hanem
átkötés + validáció: átállítani a három fogyasztót (biome, jég, párolgás) a
napi statisztikára, feloldani a jégmaszk körfüggését, és megmérni, mennyibe
kerül mindez."

## Mi készült el

**Python-orákulum előbb.** `tools/reference/thermal_annual_ref.py`. A
hőmodell nem új kernel, ezért az orákulum a már verifikált
`thermal_field_ref.py` solverére épül, és kizárólag az aggregációt és a
kétmenetes utat rögzíti. Kis rácson (level 2, 96 cella) és rövid éven
(8 nap, 4 mintanap) fut, hogy pure Pythonban másodpercek alatt kész legyen.
Az orákulum-világ sarki fennsíkja (7000 m) szándékos: enélkül a világ nem
termelne tartós jeget, és a kétmenetes út nem lenne kipróbálva — a futás
szerint 32 cella lesz `PermanentIce`, tehát a B menet tényleg más mezőt lát.

**Core.**
- `ThermalAnnualStatistics` + `ThermalAnnualStatisticsCalculator`:
  `nap_j = firstDay + floor(j · periódus / n)`, egyenlő súlyú napi átlagok,
  napi szélsőértékek szélsőértéke. Ismétlődő mintanap explicit hiba.
- `ThermalClimate` + `ThermalClimateCalculator`: kétmenetes jégmaszk
  (jégmentes bemenet kikényszerítve), biome-adapter az éves LEVEGŐ-átlagra,
  `IceFreeKinds()`.
- `MoisturePrecipitation.ComputeFromElevationField` új overloadja explicit
  hőmérséklet-mezővel; `null` bemenetnél a régi, analitikus út fut, tehát
  minden korábbi hívó BITRE azonos eredményt kap.

**Mérőeszköz.** `worldgen thermal-climate` CLI-parancs. Nem CI-lépés: a
level-6 futás percekben mérhető, egy CI-teszt vagy lassú lenne, vagy
gépfüggő időkaput állítana.

## Mérések

Seed `0xA7C944210000`, plates 20, t = 0, 12 mintanap, Release, párhuzamos
lokális lépés, ezen a gépen (nem CI-referencia).

| Szint | Cella | Tick/menet | Idő (2 menet) | Idő (bitazonos rövidzárral) |
|---|---:|---:|---:|---:|
| 4 | 1 536 | 14 880 | 8,0 s | — |
| 6 | 24 576 | 14 880 | **117,0 s** | **56,6 s** |

A rövidzár: ha az A menet sehol nem talált tartós jeget, a B menet
felszíntípus-térképe AZONOS az A-éval, tehát a második futás elhagyása nem
közelítés. A régi analitikus jégút ugyanezen a világon **482 ms**.

## A megtalált akadály — ezért nem állt át egyetlen fogyasztó sem

Ugyanaz a level-6 világ, ugyanaz a küszöb, két hőmérséklet-forrás:

| | tartós jég | szezonális hó | nincs | éves átlag |
|---|---:|---:|---:|---|
| RÉGI analitikus út | **1 727** | 10 491 | 12 358 | −81,0 … +47,9 °C |
| ÚJ hőmodell-út | **0** | 6 180 | 18 396 | −4,5 … +45,9 °C |

Egyezés: **69,9%** (17 181 / 24 576 cella).

Az átállás tehát ELTÜNTETNÉ a teljes állandó jégtakarót. A gyökérok nem
hiba, hanem az M13 modellválasztás: a hőmodell bázisa simított radiatív
faktort használ (`f_eff = 0,5·f_napi + 0,5·f_éves`), épp azért, mert a nyers
napi faktor a sarki éjszakán ~29 K-t adott. Ez viszont a sarki évi átlagot
−81 °C-ról −4,5 °C-ra emeli, a −15 °C-os jégküszöb fölé.

Ez KALIBRÁCIÓS kérdés, nem átkötési, és mindkét lehetséges válasz (a β
csökkentése vagy a jégküszöb újrahangolása) **seed-törő**. Csendben nem
dönthető el — a jégtakaró látható bolygó-jellemző, a felhasználó vizuális
ítélete is kell hozzá. Az ND-143 fogyasztói kapuja ezért nyitva marad, most
már mért indokkal.

## Ellenőrzések

- Új Core-tesztek: **16/16** (Debug). Közte a Python-orákulum ismert-válasz
  tesztje: a kétmenetes klíma minden cellájának mind a hat éves sorozata, a
  jégosztályok, a finomított felszíntípusok és az átsorolt cellák száma.
  A dokumentált kapu 1e-9 (az ND-142 szélvektor-konvenciója); a tényleges
  egyezés **szorosabb 1e-13-nál** — próbaképpen 1e-13-mal is zöld volt.
- Tisztaság: ismételt hívás bitre azonos; bepiszkolt, KÉSŐBBI napra állított
  állapottal is azonos (a kanonikus út kijavítja).
- Párhuzamos vs szekvenciális lokális lépés: bitre azonos éves statisztika.
- Paraméterhatás: a mintaszám és az évkezdet eltolása is megváltoztatja a
  kimenetet; minden mintanap számít.
- Élesetek: 0 mintanap, 0/∞ keringési periódus, ismétlődő nap, `long`
  túlcsordulás, méret-eltérés, `null` — mind explicit hiba.
- Teljes solution-build: 0 hiba, 0 warning.

## Ami hátravan az A7-ből

1. **A jégküszöb / β kalibráció** — új ND, seed-törő, felhasználói ítélettel.
   Ez a blokkoló; enélkül a fogyasztói átállás rosszabb képet adna, mint a mai.
2. A viewer `IsAdaptiveIceTile` / biome / párolgás tényleges átkötése a
   `ThermalClimate`-ra — a Core felület kész, a kapcsoló a kalibráció után
   fordítható.
3. A 7. fázis maradéka: a csapadék szél-tagja még a régi
   `WindPrecipitation.WindVector` úton van (a párolgás hőmérséklet-tagja
   viszont már átköthető).
4. Költség-beépítés: 56,6 s a Build-ben nem vállalható szinkron módon; ha az
   átállás megtörténik, ez háttérszálra és/vagy gyorsítótárba kell kerüljön
   (az ND-122/131 lemez-cache mintájára).

## Becslés

A7 funkcionális súlyozással: overlay 15% (kész) + csatolás 35% (kész) +
fogyasztói átállás 50%, amiből az adatút és a mérés kész, a kalibráció és a
tényleges átkötés nem → **kb. 65%**. Ez durva projektbecslés, nem mért
készültség. Erre a körre durván **2–3 munkaóra** ment rá, a maradék durván
**8–16 munkaóra** — a kalibrációs kör hossza a felhasználói visszajelzéstől
függ, nincs valós idő-naplózás a projektben.
