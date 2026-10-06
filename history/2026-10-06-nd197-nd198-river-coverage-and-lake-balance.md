# ND-197 + ND-198 — folyó-lefedettség és tó-vízmérleg (SEED-TÖRŐ, generátor 11 → 12)

**Dátum:** 2026-10-06
**Ág:** `a19-plate-frame-noise`
**Kiváltó:** felhasználói visszajelzés az ND-196 átvétele közben —
„a bolygón nem egyenletes az eloszlása, sok zöld terület van most is, ahol
nincs folyó", és „vannak olyan szárazföldi tektonikus lemezen lévő tavak,
amiket sem csapadék, sem pedig folyó nem tölt, ezek mégis sok vizet tárolnak,
ez életszerűtlen. Tó akkor legyen, ha van elegendő csapadék és tölti is
valamilyen folyó."
**Döntések:** `docs/04-decisions.md` ND-197 és ND-198 (mindkettő LEZÁRVA, a (b)
opcióval, a felhasználó választása szerint).

## Rövid válasz

Mindkét észrevétel MÉRVE igazolódott, és a tavaknál nagyobb volt a baj, mint a
megfogalmazás sejtette.

| | előtte | utána |
|---|---|---|
| folyó-forrás | 96 | **321** |
| nedves tile, amiben FUT folyó | 15,6% | **56,4%** |
| nedves tile 1 szomszédon belül | 25,5% | **76,8%** |
| folyó nélküli nedves szárazföld | **51,2%** | **19,5%** |
| legnagyobb folyó nélküli nedves folt | 66 tile | **10 tile** |
| látható tó | 531 | **190** |
| tó-térfogat | 1 967 814 km³ | **513 262 km³** |

## 1. ND-197 — a folyók eloszlása

### A mérés

Új `coverage` mérőpad a `RiverBaseline`-ban (a hőmodell csapadék-mezőjén, ami a
képernyőn is van): a nedves szárazföld = a pozitív csapadékú tile-ok felső fele
(514 tile). A folyós tile-októl szélességi kereséssel számolt „hány szomszédnyi
lépés a legközelebbi folyó".

Kiindulás: a nedves tile-ok **13,8%**-ában fut folyó, és a 105 összefüggő
nedves foltból **92-ben EGYETLEN folyó sincs** — ez a nedves szárazföld
51,8%-a, a legnagyobb ilyen folt 66 tile.

### A szűk keresztmetszet — két vakvágány után

**(1) A keret NEM volt az.** Söprés 96 → 192 → 288 kerettel: a forrás-szám
**117-nél elakadt**, a lefedettség 51,2% → 50,8%.

**(2) A szeparáció sem.** A 150 km-es minimális forrás-távolság kikapcsolva
UGYANANNYI (120) forrást adott.

A szűrő-anatómia mutatta meg a valódi okot (514 nedves tile):

| szűrő | átmegy |
|---|---|
| magasság ≥ tengerszint + 300 m | 198 (38,5%) |
| vízgyűjtő ≥ 12 tile | 101 (19,6%) |
| **mindkettő** | **42 (8,2%)** |

A level 5-ös tile ~313 km, tehát a „12 tile" küszöb ~1,2 millió km²-nél kisebb
vízgyűjtőket zárt ki — a nedves szárazföld 80,4%-át.

### A megoldás és a választott pont

A `basinCount` „16 legnagyobb" vágása megszűnt, `minBasinTiles` 12 → **2**,
`minElevAboveSeaM` 300 → **150 m**, és a keret a FORRÁSKÉPES TILE-OK SZÁMÁBÓL
jön (`DefaultSourcesPerCandidate = 0,52`).

| konfiguráció | forrás | nedves tile-ban | ≤1 szomszéd | folyó nélküli nedves föld | hálózat |
|---|---|---|---|---|---|
| előtte (12 / 300 m / 96) | 96 | 15,6% | 25,5% | 51,2% | 91 s |
| 4 / 150 m / 250 | 250 | 46,3% | 68,3% | 23,7% | 127 s |
| **2 / 150 m / 321 (ez lett)** | 321 | 56,4% | 76,8% | 19,5% | 165 s |
| 2 / 0 m / 400 | 400 | 65,4% | 83,1% | 16,7% | 175 s |
| 2 / 150 m / 512 | 512 | 58,2% | 78,6% | 19,5% | 279 s |

### Egy hiba, amit a MÉRÉS fogott meg menet közben

A keret első változata a szárazföldi csapadék ÖSSZEGÉBŐL számolt. Ez megbukott:
a csapadék egysége önkényes (ND-126), ezért ugyanaz a konstans a hőmodell
mezőjén 320, az ANALITIKUS előnézeten viszont 512 (plafonos) forrást adott
volna — a nézet váltásakor megugrott volna a folyók száma. A jelöltszám
geometriai mennyiség, tehát nézetfüggetlen. Ugyanaz a hibaosztály, mint az
ND-159/164 abszolút hőmérséklet-küszöbeinél.

### Amit elront, és miért vállaljuk

Az összefolyó ágak ARÁNYA 29,2% → 15,6%. ABSZOLÚT értékben viszont TÖBB
összefolyás van (28 → 50), és a leghosszabb ág is nőtt (17 520 → 18 350 pont):
a sok új, kicsi vízgyűjtő egyágú patakot kap, ezek hígítják az arányt. Az
ND-124 mért nyeresége (a nagy vízgyűjtők mély fája) megmarad. Emiatt egy teszt
kritériuma érvényét vesztette, és átírtam arra, amit a modell MOST garantál
(lényegesen több érintett vízgyűjtő + keletkezik összefolyás).

## 2. ND-198 — a tavak vízmérlege

### A mérés

A látható tavak (level 8, ≥ 6 tile, ≥ 40 m) vízgyűjtőjét a lefolyás-fa felfelé
bejárásával, a csapadékot a hőmodell mezőjéből:

- **531 tó, összesen 1 967 814 km³** — a Föld tavainak több mint tízszerese;
- **227 (42,7%)** vízgyűjtőjében PONTOSAN nulla a csapadék (a térfogat 41,3%-a);
- **474 (89,3%)** tavat semmilyen folyó nem ér el (a térfogat 83,7%-a).

A gyökérok: a tó TISZTÁN TOPOGRÁFIAI volt — a priority-flood minden zárt
mélyedést a kifolyási szintig tölt, vízmérleg nélkül.

### A modell

Új `LakeWaterBalance` a Core-ban: a tó addig telik, amíg a BEÁRAMLÁS
(vízgyűjtő csapadéka × lefolyási hányad + a tóra hulló csapadék) fedezi a
PÁROLGÁST (a víz alatti tile-ok nyílt vízfelszíni párolgása). A két oldal
ugyanabban a nedvesség-egységben van, mert a párolgás a modell saját
`WindPrecipitation.Evaporation` képletéből jön — ehhez a `PrecipitationField`
kapott egy `OpenWaterEvaporation` mezőt (a csapadék-számításra bitre semleges).

Mérve (lefolyási hányad 0,30): **531 → 190 tó**, **1 967 814 → 513 262 km³**
(26,1%), a számítás **78 ms** a teljes bolygóra level 8-on.

A lefolyási hányad nem rejtett hangoló csavar: 0,10 → 25,3%, 0,30 → 26,1%,
0,60 → 27,0% megtartott térfogat. A mérleget a vízgyűjtő/tófelszín arány és a
csapadék nulla-aránya dönti el.

### Egy SŰRŰ változat, amit a mérés VISSZADOBOTT

A viewer hidrológiája tömbindexelt úton dolgozik, ezért készült egy sűrű,
lefolyás-akkumulációs változat is. Egy valódi világon mérve a két út **nem
egyezett**: 362 tóból 80-nál más tile-szám, a szótáras út következetesen
nagyobb beáramlással. Nem szállítok nem bizonyítottan azonos második utat a
kritikus úton — a sűrű változat törölve, a viewer a verifikált szótáras úton
számol (a két szótárat a sűrű állapotból építi). Visszatéréshez ELŐBB kell a
bitazonosságot igazoló teszt (ugyanaz a minta, mint a `DenseLakeEquivalenceTests`).

### Tudatos egyszerűsítés

Minden tó ELNYELŐ: a felette fekvő tó vize nem folyik tovább az alsóba (a
túlfolyás továbbadásához a tavakat lefolyási sorrendben kellene feldolgozni —
következő kör). Nincs szezonalitás és beszivárgás.

## 3. Ellenőrzés

- **2017/2017 teszt zöld Release-ben** (Core 877, Viewer LodChunking 664,
  App 452, CLI 24). Új: 6 `LakeWaterBalanceTests`; átírva 1 folyó-teszt.
- `thermal_checkpoint_vectors.json` újragenerálva a Python orákulumból
  (a diff a `"11"` → `"12"` generátor-sztring és a két belőle származó hash).
- Unity offline fordítási kapu: 0 hiba. Élő Editor: újrafordítás
  `compilationFailed: false`, **0 konzol-hiba**.
- **Élő Play-menet:** `_riverSourceCache = 321` (pontosan a CLI-mérés), a
  tó-felszín mesh **960 808 → 337 656 vertex**, az éghajlat a Buildben
  betöltve (42 ms).

## 4. Nyitva marad

1. A 19,5% folyó nélküli nedves föld nagy része olyan kis folt, ahol a
   forrás-feltételek nem teljesülnek; a további csökkentés a csapadék-mező
   kalibrációján (A24 / ND-165) múlik.
2. A tavak túlfolyásának továbbadása (lefolyási sorrendű feldolgozás).
3. A teljes folyóhálózat felépítése arányosan lassabb lett (CLI-ben 91 s →
   165 s négy workerrel); a viewerben ez hosszabb várakozás a VÉGLEGES
   hálózatra, az áttekintő réteg előbb látszik.

Mérési kimenetek: `artifacts/nd197-*.txt`, `artifacts/nd198-lb-*.txt`,
`artifacts/nd196/` (medence-, lefedettség- és tó-CSV-k).
