# 3. probléma — extrém landmass méreteloszlás: vizsgálat + mérés (2026-09-13)

## Kérés

A felhasználó három, egymástól független problémát azonosított (eltűnő
Region/Area kis landmass esetén; Continent/Island fogalmak szétválasztása;
extrém landmass méreteloszlás), és explicit sorrendben, mérésalapú
munkamódszert kért mindegyikhez. Ez a napló a **3. probléma** vizsgálati
részét dokumentálja — az 1. és 2. probléma implementálva és lezárva (ld. a
`docs/backlog.md`-ben a saját soraikat és a
`history/2026-09-13-navigation-menu-area-level.md`-t megelőző beszélgetést).

## 1. lépés — a jelenlegi implementáció vizsgálata

**Plate generation** (`src/WorldGen.Core/Tectonics/CrustElevation.cs`,
osztály-doksi): a HATÓKÖR szakasz EXPLICIT dokumentálja, hogy "Kéreg-típus
PLATE-szinten" — egy `plateId` **egyetlen bináris** `IsOceanic` értéket kap
(`DeterministicRandom.Chance` Bernoulli-próba, `RandomProperty.CrustType`),
"a spec §14.1 Plate struct-ja is így modellezi". **A felhasználó
architekturális hipotézise (plate != continent, egy plate NEM hordozhat
kevert crustot) A JELENLEGI KÓDBAN PONTOSAN ÍGY MŰKÖDIK — megerősítve.**

**Crust elevation**: `OceanicBaseMeters=-4000`, `ContinentalBaseMeters=800`
— **4800 m réselválasztás**. `NoiseAmplitudeMeters=3000` (elsődleges) +
`SecondaryNoiseAmplitudeMeters=900` (ND-52), óceáni tile-oknál
`OceanicNoiseFactor=0.25`-tel szorozva (ND-34, szándékos — "az óceánfenék
NE legyen olyan hegyes, mint a szárazföld"). **Egy óceáni tile MAXIMÁLIS
elérhető elevációja -4000+0.25×(3000+900)=-3025 m — a zaj SOHA nem tudja
átbillenteni egy óceáni tile-t szárazfölddé**, mert az amplitúdó (max
~3900 m szárazföldön, ~975 m óceánban) kisebb, mint a 4800 m-es réselválasztás.
`DefaultOceanicProbability=0.40` (azaz 60% kontinentális lemez -
ND-37 SZÁNDÉKOSAN alacsonyra állította, hogy a tengerszint-kalibráció ne a
kéregtípus-résbe, hanem a kontinentális zaj-tartományba nyúljon be).

**Sea level calibration**: percentilis-alapú, **kizárólag a globális
víz-arányt optimalizálja** (`ComputeFloodedVolumeProxy` binary search) —
NINCS komponens-méret vagy topológia-visszacsatolás.

## 2. lépés — mérés

**Diagnosztika hozzáadva**: `FeatureSegmentation.ComputeLandmassDistributionStats`
(Core, tesztelve — ld. `tests/WorldGen.Core.Tests/Features/
LandmassDistributionStatsTests.cs`, Python-vektor-egyezéssel).

**Plate-tulajdonság diagnózis** (ad-hoc Python, nem commitolt): a
TestEarth001 seeden (20 lemez) 13 kontinentális / 7 óceáni lemez van. A
két szuperkontinens **12 a 13 kontinentális lemez közül fuz össze**
(8, ill. 4 lemez egyesül egyetlen összefüggő landmass-szá) — csak 1
kontinentális lemez marad ki (túlnyomórészt víz alá süllyedt). Ez
**gráf-percolation** jellegű viselkedés: 20 csomópontú, ~60%-os
"pozitív" valószínűségű véletlen gráfon (a lemez-szomszédsági gráf) a
klasszikus percolációs küszöb (~0,5 tipikus véletlen gráfokon) fölött
vagyunk, ezért egy vagy két ÓRIÁS összefüggő komponens dominál, a maradék
pedig apró töredék.

**Empirikus szweep** (5 seed, `plate_count=20`, alap `oceanicProbability=0.40`):

| seed | landmass | legnagyobb% | top2% | Gini | major(&gt;5%) |
|---|---|---|---|---|---|
| A7C9… | 31 | 49.6% | 90.6% | 0.892 | 2 |
| 1234 | 42 | 43.7% | 74.1% | 0.877 | 4 |
| DEADBEEF | 22 | 86.9% | 92.1% | 0.916 | 2 |
| 9E37… | 23 | 83.8% | 95.5% | 0.916 | 2 |
| 100000001 | 52 | 75.7% | 83.2% | 0.908 | 2 |

**Felhasználói cél**: legnagyobb 20-45%, top2 40-70%, major 4-8. **Egyetlen
mért seed sem éri el a célt** — a jelenség KONZISZTENS, nem egyetlen seed
véletlene.

**Kísérlet A — `oceanicProbability` emelése 0.40→0.50** (a percolációs
küszöb közelébe): **ROSSZABB** lett, nem jobb — a küszöb KÖZELÉBEN a
percoláció klasszikusan MAGASABB varianciát és gyakran MÉG dominánsabb
óriás-komponenst ad (kritikus pont jelenség). Mért: legnagyobb% 41.3-93.1%
(volt 43.7-86.9%), landmass-szám 7-41 (volt 22-52, nagyobb szórás).
**Ez a legkisebb, egyparaméteres javítás EMPIRIKUSAN NEM MŰKÖDIK** — nem
feltételezés, hanem mérés.

**Kísérlet B — `plate_count` emelése 20→60** (finomabb percolációs gráf,
`oceanicProbability` változatlan 0.40): enyhe javulás (legnagyobb% minimum
33.0%-ra esett, volt 43.7%), de **továbbra is messze a céltartományon
kívül** (legnagyobb% max 85.4%, Gini továbbra is 0.87-0.92, major 1-4).
A percolációs elmélet ezt megjósolja: nagy N határesetben a domináns
komponens ARÁNYA elsősorban `p`-től függ, nem `N`-től — több, kisebb lemez
csak simábbá teszi a partvonalat, nem oldja meg az alap-aránytalanságot.

## 3. lépés — technikai javaslat (implementáció ELŐTT, jóváhagyásra vár)

**A mért eredmény**: sem az `oceanicProbability`, sem a `plateCount`
önmagi hangolása NEM elég a célzott eloszláshoz — ez a felhasználó saját
architekturális hipotézisét (plate-szintű bináris crust túl durva
egység) erősíti meg, MÉRÉSSEL, nem feltételezéssel.

**A legkisebb, MŰKÖDNI VALÓSZÍNŰSÍTHETŐ következő lépés**: nem új
adatmodell ("Plate → több crust area"), hanem a MEGLÉVŐ zaj és a
kéregtípus-bázis közti RÉS szűkítése úgy, hogy a zaj ÉRDEMBEN felül tudja
írni a plate bináris típusát néhol (pl. `OceanicBaseMeters` közelítése a
kontinentális bázishoz és/vagy `OceanicNoiseFactor` emelése) — ez land/sea
határt a diszkrét 20-csomópontú lemez-gráf helyett egyre inkább a
folytonos, térben koherens zajmező alakítaná, ami természetesebb,
kevésbé "mind vagy semmi" eloszlást adna.

**Ütközés két KORÁBBI, felhasználó által jóváhagyott döntéssel**, amit
NEM tudok élő Unity-vizsgálat nélkül újra-validálni:
- **ND-34**: `OceanicNoiseFactor=0.25` kifejezetten azért lett bevezetve,
  mert a felhasználó szerint a teljes amplitúdójú óceánfenék "túl hegyes"
  volt vizuálisan. Az emelése ezt a korábban javított problémát
  megnyithatja újra.
- **ND-37**: `DefaultOceanicProbability=0.40` kifejezetten azért lett
  ilyen alacsonyra állítva, hogy a tengerszint NE a kéregtípus-résbe
  ("falszerű part") essen. A rés szűkítése (`OceanicBaseMeters` emelése)
  hasonló irányban hat, mint amit ND-37 elkerülni próbált - újra
  meg kell mérni, nem tér-e vissza a "falszerű part" jelenség.

**Ezért itt megállok, mielőtt implementálnék** — ez már nem egyértelmű,
kockázatmentes paraméter-hangolás, hanem két korábbi vizuális döntéssel
való kompromisszum, amit a felhasználónak kell eldöntenie.

## Utólagos, kritikus forrás-ellenőrzés: ND-37 MÁR MEGVÁLASZOLTA ezt

A felhasználó jóváhagyta a rés-szűkítési kísérletet - mielőtt elindítottam,
**forrásból ellenőriztem** (`docs/04-decisions.md` ND-37, "CLAUDE.md: ne
bízz az emlékezetedben, verifikálj forrásból" elve szerint), és két
kritikus, a döntést megváltoztató tényre bukkantam:

1. **Pontosan ezt a kísérletet már elvégezték és elutasították.** ND-37
   szövege szerint: "Az `OceanicBaseMeters` közvetlen csökkentése (pl.
   -2000m-re) erősen hat... de **irreálisan sekély óceánt eredményezne -
   fizikailag rosszabb kompromisszum, mint a jelenlegi állapot.**"
2. **A jelenlegi extrém eloszlás MAGA ND-37 tudatosan vállalt
   mellékhatása**, nem véletlen hiba: ND-37 az `oceanicProbability`
   0.55→0.40 váltásakor MÉRVE dokumentálta, hogy a kontinensszám 6→44-re
   nő, és explicit leírta: "Ez NEM hiba, dokumentált, szándékos
   kompromisszum a realisztikusabb part-átmenetért cserébe." A 0.55-ös
   RÉGI értéknél csak 6 kontinens volt (közel a mostani felhasználói
   célhoz: 4-8 major), de az adta a "falszerű part" (ND-37 eredeti)
   hibát.

**Következtetés**: a "sima part-átmenet" (ND-37 eredeti célja) és a
"kiegyensúlyozott kontinensméret-eloszlás" (a mostani 3. probléma célja) a
JELENLEGI bináris lemez-kéreg modell mellett **strukturálisan ütköző
célok** - ezt most KÉT FÜGGETLEN forrás támasztja alá: a saját mai mérésem
(0.40 rossz, 0.50 még rosszabb a cél szempontjából) és ND-37 réges-régi,
más célra végzett mérése (0.55 jó a kontinensszámra, rossz a partra; 0.40
jó a partra, rossz a kontinensszámra).

**Felhasználói döntés (2026-09-13)**: a numerikus finomhangolást NEM
folytatjuk tovább ebben a körben - sem a rés-szűkítést (amit ND-37 már
kipróbált és elvetett), sem a strukturális "plate-en belüli kevert crust"
irányt. **A 3. probléma lezárva: diagnózis + mérés + a fenti ND-37
ütközés dokumentálva, implementáció nélkül. Follow-up feladatként
nyitva marad.**
