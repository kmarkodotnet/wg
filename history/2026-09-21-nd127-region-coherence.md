# 2026-09-21 — ND-127 lezárva: a régiók földrajzi összetartozása

**Feladat:** `todo.md` 1. tábla 3. sor — „a navigációs menü régiói nem
tartoznak össze". Az ND-124 mérése számszerűsítette a tünetet (2467 medence
8602 szárazföldi tile-ra), de a régió-oldali következményét nem javította.

## Mi derült ki a mérésből

A gyökérok egy SZINTBELI hiba: a `FindWatershedRegions` a torkolat
**óceán-tile-ja** szerint kulcsol — az a LEFOLYÁS azonosítója, nem egy
földrajzi egységé. Ebből három külön tünet jött (seed `0xA7C944210000`,
20 lemez, víz 0,65):

| | level 5 (a viewer panel-szintje) | level 6 |
|---|---|---|
| vízgyűjtő / szárazföld-tile | 789 / 2151 | 2467 / 8602 |
| a szárazföld hány %-a van egyáltalán régióban | 50,1% | 63,3% |
| régió a legnagyobb landmasson | 65 | 207 |
| térben szétesett régió (>1 komponens) | 35,7% | 20,0% |

A 2. sor a legfontosabb: a panel `Count >= 5` szűrője a szárazföld felét
(level 5) kihagyta a navigációból. A 3. sor szó szerint a panasz: egy
régiónév alatt több, egymástól elszakadt földdarab.

## A megoldás (ND-127 (A))

`FeatureSegmentation.MergeWatershedsIntoRegions` — a vízgyűjtők agglomeratív
összevonása. Cella = egy vízgyűjtő egy összefüggő komponense; szomszédsági
gráf a cellák között, élsúly = a közös határ hossza; amíg van cél alatti
cella, a legkisebbet beolvasztjuk abba a szomszédjába, amelyik (a) maga is
cél alatt van, (b) a leghosszabb közös határt osztja vele. Tiszta egész
aritmetika, nincs szótár-bejárási függés.

Eredmény (előtte → utána): lefedettség **50,1% → 100%** (level 5) és
**63,3% → 100%** (level 6), szétesett régió **35,7% → 0** és **20,0% → 0**,
régió a legnagyobb landmasson **65 → 10** és **207 → 11**. Költség 19, ill. 43 ms (Release, egy szálon).

## Amit érdemes megjegyezni

1. **Az összevonási PARTNER-szabály mérhetően számít.** Az első,
   kézenfekvőbb változat („olvadjon a legkisebb szomszédba") a partvonal
   mentén kígyózó régiókat épített: a legnagyobb landmasson az
   átmérő/√terület 2,5–3,5 volt, a közös-határ szabállyal 1,9–3,4 (a
   legnagyobb régióé 3,3 → 2,3). Viszonyítás: kompakt folt ~2,0, maga a
   landmass 2,9. A „cél alatti szomszéd előnyben" kiegészítés a legnagyobb
   régiót 699 → 612 tile-ra vitte. Ezt mérni kellett, mert a kerület/√terület
   mutató önmagában NEM mutatta meg (a partvonal fraktál, a landmassé is
   22–26).
2. **A cél-méret nem lehet fix tile-szám.** A viewer level 5-ön panelez, a
   tesztek level 6-on futnak; fix 65 tile-lal a level 6-os menü 3-4-szer
   annyi régiót adna ugyanarra a bolygóra. A szárazföld 3%-a (
   `RecommendedRegionTileTarget`) mindkét szinten 10–11 régiót ad a
   legnagyobb landmasson.
3. **A régió-definíció csere ÁTGYŰRŰZIK az ordinális küszöbökre.** A
   `SoilFertilityThresholds` (ND-09/ND-117) kalibrációs populációja a ≥5
   tile-os vízgyűjtő volt. Egy 8 világos gyorsmérés mutatta meg, hogy az
   eloszlás eltolódik; az újrakalibrálás (500 világ, level 6, ugyanaz a
   `calibrate-ordinals --soil true` parancs) megerősítette: v1
   p20/p80 = 0,1946/0,2596, **v2 = 0,1577/0,2302** (23 492 régió-minta).
   A v2-populáció p40-e (0,1940) épp a v1 p20 alatt van — a régi
   küszöbökkel az összevont régiók ~40%-a esett volna a legalsó
   kvintilisbe a 20% helyett. Kontroll: a Habitability és a
   CoastalComplexity vágópontjai BITRE változatlanok maradtak, tehát
   tényleg csak a talaj-populáció mozdult.
4. **A viewer fallback-ága (egész landmass, ha nincs régió) biztonsági
   hálóvá vált.** Nem töröltem: nulla költségű, és egy jövőbeli
   szegmentálás-csere megint kihagyhat tile-okat.

## Állapot

- Core tesztek: **550 ✓** (534 → +16, ebből 1 bitpontos vektor-teszt a
  Python-orákulum ellen), Cli 8 ✓, App.Foundation 438 ✓, LodChunking 482 ✓.
- Offline Unity fordítási kapuk: 0 hiba. Unity Editor recompile: 0 console
  error.
- **Vizuális elfogadás hátravan** (`todo.md` 2. tábla 5. sor): a felbontás
  (`RegionTargetLandSharePercent = 3`) ízlés kérdése, a számok nem döntik el.
- Nyitva marad az ND-05 hibridjének másik két tagja (biome-klaszter,
  domborzati törés) — ugyanezen a cella-gráfon más összevonási
  költségfüggvényként jönnének be.
