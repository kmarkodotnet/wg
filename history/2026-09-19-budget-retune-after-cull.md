# A budget újrahangolása a horizont-vágás után (2026-09-19)

## Miért kellett hozzányúlni

A viewport-budget szorzóját (1,5) és felső korlátját (48 000) a horizont-vágás
ELŐTTI költségekkel méreteztem. A vágás után a selection 7-8×-szal olcsóbb
lett, tehát a mérési alap, amin a két konstans állt, elavult.

Először azt ellenőriztem, maradt-e egyáltalán tennivaló a korábban nyitva
hagyott tételen ("hideg metrika-cache, a selection 48%-a, ND-döntést igényel").

## A nyitott tétel magától megszűnt

| | vágás előtt | vágás után |
|---|---|---|
| hideg selection | 482-689 ms | **35-103 ms** |
| meleg selection | 157-183 ms | **7-22 ms** |
| látogatás / levél | ~25 | **~3,4** |

A hideg cache büntetése továbbra is 4-5× a meleghez képest, de egy
nagyságrenddel kisebb számon. **Nem kell ND-döntés**: a tűrésalapú
cache-újrahasználat (ami a vágást a kamera útjától tenné függővé, sértve a
`SelectCutPrioritized` dokumentált determinizmus-garanciáját) egy már nem
létező problémát oldana meg. A tétel lezárva.

## A szorzó: 1,5 → 3,0

Az értéket NEM elméletből vettem, hanem a felhasználó 2026-09-18-i naplójából
számoltam vissza. A levélszám a lineáris tile-méret NÉGYZETÉVEL skálázódik:

| zoom-sáv | mért tile-méret (8 000 levéllel) | szükséges levél |
|---|---|---|
| 30-100 | 1,40-1,69× | 16 000 - 23 000 |
| 10-30 | 2,13-2,32× | 36 000 - 43 000 |
| 3-10 | 3,76-4,08× | 112 000 - 136 000 |

A csupasz raszter-igény 1196×710 és 8 px mellett 13 269. A 3,0-es szorzó
**39 807** levelet ad, ami a két leggyakrabban használt sávot (kontinens- és
régió-nézet) célra viszi. A legközelebbi zoom ~8-10× szorzót kérne; azt a
felső korlát tudatosan nem engedi.

## A felső korlát marad 48 000 — de más okból

Két, egymástól független MÉRT ok tartja ott:

1. **Költség-paritás.** 48 000 levélnél a worker-költség (selection+balance,
   meleg cache) 150-158 ms — pontosan annyi, amennyi a felhasználónak a
   változtatások ELŐTT volt 8 000 levéllel (naplóban selection p50 = 147 ms).
   Hatszoros részletesség a korábbi költségen; efölött már drágább lenne,
   mint eddig.
2. **Balance-szakadék.** Az `EnforceRestrictedBalance` nem szigorú korlátja
   `maxLeafCount*3`, és a költsége EZZEL nő, nem a tényleges levélszámmal.
   Mérve (D=103): 48 000-nél 56 ms, 96 000-nél **327 ms**. A 48 000 biztos
   távolságban marad ettől.

A második pont egy külön, most nem javított gyengeség: a balance ára a
*korláttól* függ, nem a munkától.

## Mérési táblák

Budget-söprés a horizont-vágás után (D=103, meleg cache):

| budget | levelek | selection | balance | összesen | hideg |
|---|---|---|---|---|---|
| 8 000 | 7 998 | 8 ms | 9 ms | 17 ms | 34 ms |
| 20 000 | 19 998 | 51 ms | 22 ms | 73 ms | 88 ms |
| 32 000 | 31 998 | 72 ms | 36 ms | 107 ms | 143 ms |
| 48 000 | 48 000 | 94 ms | 56 ms | 150 ms | 224 ms |
| 64 000 | 63 999 | 116 ms | 77 ms | 193 ms | 315 ms |
| 96 000 | 66 900* | 73 ms | **327 ms** | 400 ms | 558 ms |

(*) itt telítődik a levélszám — ennyit kér összesen a metrika ebben a nézetben.

A felhasználó tényleges budgetjével (39 807):

| távolság | levelek | meleg | hideg |
|---|---|---|---|
| 150 (legtöbb látható felület) | 39 804 | 194 ms | 347 ms |
| 120 | 39 806 | 90 ms | 188 ms |
| 110 | 39 807 | 82 ms | 177 ms |
| 103 | 39 807 | 81 ms | 177 ms |
| 101 | 39 807 | 82 ms | 178 ms |

Vagyis ötszörös levélszámnál a közeli nézetek **olcsóbbak**, mint a korábbi
8 000-es alapállapot (147 ms), és a középtávú legrosszabb eset +32%.

## Ellenőrzés

- LOD-tesztcsomag: **434/434 zöld**.
- Unity `Assembly-CSharp`: 180 hiba, mind a 8 ismert, elfogadott osztályban.
- `RenderBudgetForViewportTests` frissítve: a várt érték 19 903 → 39 807, és a
  minimum-teszt viewportjai kisebbre, mert a 3,0-es szorzóval a 640×480 MÁR
  14 400-at kap (a minimum 170 667 képpontnál fordul át, 16:9-ben ~551×310).

## Ami továbbra is rád vár

Az emit/corners/feltöltés levélszámmal skálázódik, és Unity-függő. A
2 ms-os szelet-korlát elvben leveszi a szaggatás-kockázatot (a nagyobb mesh
több staging-frame, nem nagyobb akadás), de ezt egy Play-menet mondja meg.
A napló minden `[tilesample]` sorban kiírja a `cutBudget`-et és a
`budgetMode`-ot, tehát utólag pontosan visszamérhető.
