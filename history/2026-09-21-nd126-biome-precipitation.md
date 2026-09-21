# 2026-09-21 — ND-126: a biome-osztályozó megkapta a csapadékot

**Feladat:** „klíma-konstansok kalibrálása". Visszajelzés: „a ráktérítő és
baktérítő közt minden sivatag, fölötte és alatta egy darabig zöld, efölött meg
jég van, nagyon nem életszerű".

**Nem konstans-probléma volt.** A `BiomeClassification.Classify(temperatureK,
isOceanic)` szignatúrája a teljes magyarázat: a csapadék nem bemenet. Az
osztályozó saját doksija ki is mondta, hogy „a csapadék/nedvesség (§31)
halasztva van, ezért NEM különböztetünk meg pl. sivatagot/esőerdőt". A
csapadék-mező azóta elkészült (folyók, overlay, talaj használja), csak ide nem
került be.

## A diagnózis számai

Négy osztály, tisztán hőmérsékleti küszöbökkel → tökéletes szélességi sávok.
A >20 °C sáv render-színe `(0.78, 0.72, 0.20)`, homoksárga — ezt olvasta a
felhasználó sivatagnak.

A döntő szám a beavatkozás előtt: egy HŐMÉRSÉKLETI osztályon belül a csapadék
P10→P90 szórása **52–140×**. Az információ megvolt, az osztályozás dobta el.

## Az eredmény

Hány különböző biome egy 20°-os sávban:

| Sáv | Előtte | Utána |
|---|---|---|
| 30°..50° | 2 | 6 |
| 10°..30° | 2 | 5 |
| −10°..10° | **1** | 4 |
| −30°..−10° | 2 | 5 |

Az Egyenlítő sávja korábban 100% Trópusi volt. Most: Sivatag 17%, Sztyeppe
16%, Szavanna 29%, Esőerdő 38%.

## Két dolog, amit a munka közben a mérés fogott meg

**1. A percentilis-küszöb POPULÁCIÓJA.** Az első változat a vágópontokat a
teljes szárazföldi eloszlásból számolta. Az arid vágópont pontosan **0,000**
lett, és a szárazföld 24,4%-a esőerdő (a Földön ~7%). Ok: a hideg tile-okat a
hőmérséklet dönti el, a csapadékuk viszont 0 körüli — lehúzzák a
percentiliseket. Javítás: `ComputeThresholdsForVegetatedLand`, csak a
`TundraThresholdK` fölötti tile-okból. Utána 0,109 / 0,604 / 1,999.

**Ez harmadszor ugyanaz a minta** (ND-124: globális küszöb ∩ vízgyűjtő-szűrés
= üres; ND-126 első kör; most). **Percentilis-küszöbnél mindig ki kell
mondani, MELYIK populáció eloszlásáról van szó.** Ezt érdemes szabályként
kezelni, nem esetenként újra felfedezni.

**2. Látens determinizmus-hiba a `DominantBiome`-ban.** A döntetlent a
`Dictionary<Biome,int>` bejárási sorrendje döntötte el — explicit szabály nem
volt. I2-sértés, ami addig nem bukott ki, amíg két szárazföldi osztály
létezett. Öt osztállyal a Python-referencia és a C# azonnal más régiónevet
adott ugyanabból az adatból („Sylthal Plains" vs „Sylthal Veld"). Javítva
mindkét oldalon (a kisebb enum-érték nyer), tesztbe zárva.

A második pont tanulsága módszertani: **a Python-referencia nem csak
algoritmus-hibát fog meg, hanem a két implementáció közti NEM DETERMINISZTIKUS
pontokat is** — pont azért, mert a szótár-szemantikája más. Enélkül ez a hiba
akkor derült volna ki, amikor a CI egy másik platformon más régiónevet ad.

## Ami nyitva maradt

A globális szárazföldi eloszlásban jég + tundra **40,7%** (a Földön ~18%). Ez
nem az osztályozó hibája, hanem a hőmérsékleti lánc túl meredek sark–egyenlítő
esése — **ND-126b**, a termikus szél sarki elszabadulásával együtt (7,4 m/s az
Egyenlítőn, 120–126 m/s 70–90°-on).

Ezeket szándékosan az osztályozó UTÁNRA hagytam: a biome-küszöbök
percentilis-alapúak, tehát a hőmérséklet/szél hangolása automatikusan
átrendezi őket — fordított sorrendben kétszer kalibrálnánk.

## Állapot

- Core tesztek: **534 ✓** (516 → +18), offline kapuk 0 hiba, Unity Editor
  fordítás hibátlan.
- Python-referencia frissítve és újrafuttatva (`biome_ref.py`,
  `features_ref.py`), 200 + 300 tesztvektor újragenerálva.
- Vizuális elfogadás hátra; a kalibráció (percentilisek, ND-126b) utána jön.
