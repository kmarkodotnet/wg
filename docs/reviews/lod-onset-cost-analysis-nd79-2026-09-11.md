# ND-79 — A korai zoom finomítási küszöbe és annak költsége

2026-09-11. Friss felhasználói próba: az első néhány közelítő görgetésre
alig reagál a felbontás. **A panasz igazolt. Ez elemzés és offline kísérlet,
nem átadott vizuális javítás.**

## Mit bizonyít az élő log?

Forrás: `unity/WorldGenViewer/Logs/PerfLog_20260911_214108.txt`.
Az ND-78 aktív (`earlyOceanExclusion=ND78`), a pixelcél továbbra is 12,
az első base-osztás célja 10. Viewport 1238×688, FOV 60°, morph-range 0,360.

| Idő / állapot | Tényleges terep vagy közép | A kiválasztás állapota |
|---|---|---|
| 21:41:24, távolság 300 | Legnagyobb mintázott tereptile 3,368 px | Nincs dinamikus terep |
| Első kérés 21:41:25.673, távolság 263,746 | 20 dinamikus levél / 5 új osztás | 110,7 ms kéréskor; nincs halasztás |
| 21:41:28–30, távolság 160,239 | Statikus tereptile 9,939 px, víz a képközépen 9,00 px | Kameraeltérés 0, nincs futó kérés vagy további hullám |
| 21:41:32, elfordítás után, távolság 160,239 | Középső **statikus terep 9,29 px**, legnagyobb mintázott terep 9,705 px | Ugyancsak teljesen befejezett finomítás |
| 21:41:38, távolság 127,067 | Középső dinamikus terep 10,93 px | Befejezett; legnagyobb terepminta 12,281 px |
| 21:41:50, távolság 105,465 | Középső dinamikus terep 11,46 px | Befejezett; legnagyobb terepminta 13,259 px |

A 9,939 px-es statikus tile `68000000000054D0`, F3/L8/u236/v8.
Tényleges megállása `below-threshold`, a kéréskori proxy is 9,939 px,
küszöbe 10 px. A 9,705 px-es tile `6800000000007BA3`, F3/L8/u209/v125;
annak proxyja is egyezik a kirajzolt mérettel. **Itt nem proxy-alulbecslés,
nem morph, nem elakadt worker és nem max-LOD a korai részlettelenség oka.**
A szabály tudatosan hagyja a távol kb. 3 pixeles rácsot 10 pixel közelébe
nőni; később 12 pixeles célra áll be. Ez nem őrzi a kezdeti élességet.

A középső víz külön hiba: statikus marad. Viszont 21:41:32-nél már terep
van középen, ezért a teljes panaszt nem lehet a vízrétegre fogni.
Görgetésszám nincs a logban; a konkrét görgetések számát nem állítjuk.
A 17×9-es mérőháló nem teljes képernyős maximum és nem végső GPU-mérés.

Mélyen továbbra is vannak kvótára váró hullámok: 21:41:42–48 között
20–26 px-es tereptalálatok, `split-quota` megállással. A teszt végén,
21:42:00-kor visszazoom után még folyik finomítás; az nem végállapot.

## A kipróbált kisebb küszöbök

Reprodukció:

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj -c Release --no-restore -p:_EnableDefaultWindowsPlatform=false -- --quality
```

A meglévő ND-78 próba kész base-mintáit és óceáni kizárását használja.
Két logbeli nézetirány, mindegyikre 13 távolság, külön előzmény minden
beállításhoz. Célok 12/10, 9/7,5, 8/6,667 és 6/5 px (mély/base).
A renderelt világmodell és a proxy változatlan. Teljes kiválasztási
eredményeket hasonlítunk, nem az első 1024 osztás után félbemaradt állást.
Az előkészítés nincs a cut időmérésében; ez nem Unity FPS-benchmark.

Az első irány `(82.747,-129.398,45.666)` normalizálva:

| Távolság | 12/10 px levelek | 9/7,5 px | 8/6,667 px | 6/5 px |
|---|---:|---:|---:|---:|
| 300 | 0 | 68 | 252 | 1 120 |
| 209,762 | 384 | 1 509 | 2 379 | 15 873 |
| 160,239 | 2 930 | 42 865 | 60 942 | 96 969 |
| 127,067 | 22 411 | 54 039 | 71 428 | 99 171 |

A második irányon `(72.764,-132.848,52.281)` a középső terep a 6/5
beállítással már 209,762-nél L9, az eredetivel még 160,239-nél is L8.
Tehát a változás valóban előrehozza az osztást. Ugyanakkor 160,239-nél
2 912→96 989 levél, 103,663-nál 48 426→198 485 levél keletkezik.
A budget-ellenőrzés minden kísérleti állásban teljesül, de a 200 ezres
korlát közelében levő eredmény önmagában nem jelent elfogadható sebességet.

**A kisebb célokat nem aktiváltuk az alkalmazásban.** A 6/5 minőség
közepesen ~33-szoros dinamikus levélszámot jelenthet, nem csupán egy
olcsó konstansmódosítás. Ez az egész bolygó geometriájának nem 33-szorozása:
a statikus alap változatlan, a dinamikus többlet nő. A 4×-es osztások és
a korábbi, szinte üres dinamikus réteg miatt a növekedés ugrásszerű.

## Miért kell a megjelenítési oldalt is rendezni?

A scene `dynamicChunkLevel=6` beállítását a runtime legalább a base-szintre,
8-ra emeli. A jelenlegi út így közepes zoomnál tipikusan 4 levélből készít
egy külön terepchunkot. Az első irány 160,239 állásában:

- Régi minőség: 2 930 levél, 728 darab L8-csoport.
- 6/5 minőség: 96 969 levél, **23 640 darab L8-csoport**.
- Ugyanez L6-os csoportosítással 2 072 csoport, maximum 151 levél/chunk.

Ezek csoportszámok, **nem mért GPU draw call-ok**; anyagbontás további
rajzolásokat adhat. A fix L6-ra visszaállítás mélyen viszont ismét óriási
chunkokat adna: ugyanazon irány 103,663-as állásában 27 681 levél is
kerülhetne egy chunkba. A régi fix-chunk hibát nem szabad visszahozni.

Javasolt folytatás, külön ellenőrizhető lépésekben:

1. Korlátos méretű, hierarchikus chunk-csomagolás: a sekély levelek ne
   hozzanak létre több tízezer apró renderert, a mély rész pedig ne kerüljön
   egyetlen óriási chunkba. Kötelező fedés-, diff-, visszazoom- és méretkorlát-teszt.
2. Az adagolt építés és főszálas upload tényleges költségének korlátozása;
   a változatlan geometria/adat megőrzése. A csomagolás önmagában nem
   szünteti meg a több modellminta vagy több osztási hullám költségét.
3. Kisebb, egységes tereppixelcél, mérhető korai osztással és élességgel;
   csak az összköltség újramérése után. A vízfelszín külön finomítási munka.

Az alternatíva a kisebb pixelcél azonnali bekapcsolása a jelentős
lassulás vállalásával. Ehhez felhasználói irányválasztást kértünk.
Nincs késznek jelzett zoomjavítás, és az aktuális runtime/scene nem változott.
