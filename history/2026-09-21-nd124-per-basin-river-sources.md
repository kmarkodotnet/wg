# 2026-09-21 — ND-124 lezárva: vízgyűjtő-alapú folyó-forrás kiválasztás

**Feladat:** #5 — folyók. Felhasználói visszajelzés: „nincs tree alakzat…
sosem ér bele egyik a másikba", „az egész bolygón ritkák a folyók".

**Előzmény (ugyanaznap, `60121aa`):** a dendritikus összefolyás *működik*, a
hiba a forrásszám volt (`DefaultSourceTopK = 12`), és az sem hidrológiai
döntés, hanem költség-korlát. A folytonos nyomvonal-követő párhuzamosítva
(bitre azonos kimenet, 3,7–5,2×), topK 12 → 48. Ez a sűrűséget javította, a
fa mélységét nem — ezért nyílt az ND-124, három opcióval. A felhasználó
választása: **(A) vízgyűjtő-alapú kvóta**.

## Mi történt ebben a menetben

1. **Az (A) első változata 0 forrást adott.** A terv szerinti „legnagyobb N
   vízgyűjtő × K forrás" a meglévő *globális* csapadék-küszöbbel (szárazföldi
   80. percentilis) kombinálva minden paraméterezésnél üres listát adott.
2. **A diagnosztika (nem tippelés) mutatta meg, miért.** seed
   `0xA7C944210000`, level 6: 8602 szárazföldi tile, **2467** vízgyűjtő, a
   legnagyobb **108** tile-os; és a 6 legnagyobb vízgyűjtőben **egyetlen**
   tile sincs a globális 80. percentilis fölött. A nagy vízgyűjtők a
   kontinens-belsőben vannak, a legnedvesebb tile-ok a keskeny parti
   hegyvidékeken — a két szűrő metszete üres.
3. **Javítás:** a csapadék-küszöb medencén belül *relatív* (a medence saját
   legnedvesebb tile-jai), a magasság-küszöb marad abszolút; új
   `minBasinTiles` küszöb (2-3 tile-os parti lefolyásban nincs hova
   összefolyni).
4. **Paraméter-mérés** (level 6, `fineDepth` 4, folytonos követő, 16 mag):

   | Kiválasztás | Forrás | Összefolyás | Max súly | Súly ≥ 3 | Idő |
   |---|---|---|---|---|---|
   | globális top-K | 48 | 8 (17%) | 2 | **0** | 6,5 s |
   | medence 6×8 | 48 | 21 (44%) | 6 | 6 | 19,9 s |
   | medence 12×6 | 72 | 27 (38%) | 5 | 9 | 26,0 s |
   | **medence 16×6** | **96** | **40 (42%)** | **6** | **13** | **32,3 s** |
   | medence 20×5 | 100 | 39 (39%) | 5 | 13 | 31,7 s |

   Alapértelmezés: **16×6**. Kevesebb medence mélyebb fát ad, de a folyókat a
   bolygó néhány pontjára sűríti — ami éppen a másik panasz.
5. **`Math.Cos` → `DeterministicMath.Cos`** a minimális forrás-távolság
   küszöbénél (ND-27): a küszöb eldönti, mely tile-ok lesznek források, tehát
   a kritikus úton van.

## Amit érdemes megjegyezni

1. **A `fineDepth` NEM a fő tényező az összefolyásban.** Külön mértem (2/3/4):
   a medence 6×8 esetén 52% → 48% → 44%. Érdemes volt megmérni, mert a viewer
   `fineDepth` 4-gyel fut (~9 km-es claim-cella) a bench 2-jével (~36 km)
   szemben, és elsőre ezt gyanítottam fő oknak. Nem az volt.
2. **A párhuzamos követés ára a medence-forrásoknál nagyobb, mint gondoltam.**
   Ugyanaz a 48 forrás szekvenciálisan 54,6 s (1 szál), párhuzamosan 19,9 s
   (16 mag) — csak 2,5×. A különbség nem a nyomvonalhossz (201k vs 159k pont),
   hanem a pit-escape a kontinens-belsőben, plusz a
   terheléskiegyenlítetlenség. A következő lépés, ha zavaróvá válik:
   **kör-alapú (round-major) forrás-sorrend** — a medencék ELSŐ forrásai külön
   körben előre, a későbbi körök a már lefoglalt főágnál korán megállnak. Ez a
   szekvenciális szemantika, tehát bitre azonos kimenetet ad, de a merged
   folyók nem követik végig a főágat.
3. **A „legnagyobb vízgyűjtő" level 6-on nem kontinentális vízgyűjtő.**
   2467 medence 8602 szárazföldi tile-ra, a legnagyobb 108 tile — a
   szegmentálás a torkolat óceán-tile-ja szerint kulcsol, tehát egy hosszú
   partszakasz sok apró medencére esik. Ez korlátozza, mennyire mély fa
   építhető; ha ennél mélyebb kell, az az ND-124 (C) iránya (a topológia a
   flow-networkből).

## Állapot

- Core tesztek: **511 ✓** (504 → +7), offline Unity-kapuk 0 hiba, Unity
  Editor fordítás 0 console error.
- Vizuális megerősítés a felhasználótól **még hátravan** — a mért metrikák
  (összefolyás-arány, vízhozam-súly eloszlás) javultak, de a képernyőn
  látható eredményt nem én ítélem meg.
- Következő a sorban: **#4 — lemez-szabálytalanság** (seed-törő: verzió-emelés
  és az ND-09 ordinális kalibráció újrafuttatása kell hozzá, tehát előbb terv,
  aztán kód).
