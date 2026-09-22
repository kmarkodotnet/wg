# 2026-09-22 — ND-131: a hidrológiai tile-középpont bázis lemez-gyorsítótára (A2)

A `todo2.md` A2 sora. Az ND-122 a statikus SAROK-bázist vitte validált
lemez-gyorsítótárba; a hidrológia TILE-KÖZÉPPONT bázisa (ND-64) viszont csak
memóriában cache-elt, ezért minden hideg Build újraszámolta — élőben mérve
**2 295 ms** (`hydrology(...) terrainBasis=`).

## A tényleges döntés

Nem az volt a kérdés, hogy kell-e lemez-cache (azt az ND-122 eldöntötte,
újraszámolásos validációval együtt), hanem hogy a meglévő formátum hogyan
szolgálja ki a MÁSODIK fogyasztót, aminek más az alakja:

| | statikus sarok | tile-középpont |
|---|---|---|
| tömbök | **3** (center + u/v a normálhoz) | **1** (nincs normál) |
| darabszám (level 8) | 396 294 | 393 216 |
| epszilon | 1e-4 | nem értelmezett |
| nyers méret | 54,4 MiB | 18,0 MiB |

- **Háromszor ugyanaz a tömb** → 3× lemez (54,4 MiB a 18,0 helyett) + hazug
  tartalom. Elvetve.
- **Külön, párhuzamos formátum** → duplikálná az ND-122 három biztosítékát,
  épp azt a kódot, aminek a helyessége a legkritikusabb. Elvetve.
- **N tömbös általánosítás** (`ArrayCount` + `Kind` a kulcsban). Ez ment.

A `Kind` azért kell az `ArrayCount` mellé is, mert a két gyorsítótár
elkülönítése különben ESETLEGES lenne (a darabszám (n+1)² vs n², a tömbszám
3 vs 1) — egy későbbi változtatásnál csendben egybeeshetnének. Teszt rögzíti,
hogy egy tile-középpont fájl sarok-kulccsal `kulcs-elteres`-t ad.

## A mért csapda: a takarítás nem futott le

A fejléc bővült, ezért a fájlnév is más → a korábbi fájlokat már a nevük sem
találja meg, viszont a kvótából helyet foglalnának. Az első változat a
takarítást a **kvóta-kezelésbe** tette — az viszont CSAK MENTÉSKOR fut. Egy
Build, ami minden bázist a gyorsítótárból kap, soha nem ír, tehát a takarítás
sem futott le: élőben ellenőrizve a 54,4 MiB-os árva fájl ott maradt.

Javítás: munkamenetenként egyszer futó `PurgeStaleTerrainBasisCacheFilesOnce`,
amit a BETÖLTÉS útja is meghív. Újraellenőrizve: a fájl törlődött, a könyvtár
pontosan egy világnyi (72,4 MiB).

## Mérés

| | előtte | utána |
|---|---|---|
| `hydrology(...) terrainBasis=` | **2 295 ms** | **98–104 ms** (~22×) |
| teljes hidrológia-fázis | 2 625 ms | 417–472 ms |
| fájl / kiírás / beolvasás+validáció | — | 18,0 MiB / 55 ms / 94–96 ms |
| gyorsítótár-könyvtár / világ | 54,4 MiB | 72,4 MiB (kvóta 512 MiB) |

**Helyesség a formátumon túl:** a `lakes=11505` bitre azonos a számoló és a
lemezről töltő ágon. A tó-halmaz a teljes tile-középpont bázisból származik
(eleváció → priority flood → tó-detektálás), tehát ez végponttól végpontig
tartó egyezés-jelzés, nem csak a fájlformátumé.

Tesztek: `TerrainBasisDiskCacheTests` 7 → 15; a teljes
`WorldGen.Viewer.LodChunking.Tests` **490/490 zöld**.

## Tanulságok

1. **Egy takarító/karbantartó lépést oda kell tenni, ahol a RITKA út is
   áthalad rajta.** A kvóta-kezelés a mentés útján ült — pont azon, amit egy
   jól működő gyorsítótár soha nem használ.
2. **A „úgyis különbözik" nem elkülönítés.** Az `ArrayCount` és a darabszám
   ma véletlenül megkülönbözteti a két cache-t; a `Kind` kimondja. Egy
   kulcsnak a SZÁNDÉKOT kell kódolnia, nem a jelenlegi számokat.
3. **A formátum-általánosítás olcsóbb volt, mint a duplikálás.** Az ND-122
   három biztosítéka egy helyen maradt, és mindkét fogyasztó ugyanazt a
   verifikált utat kapja.

## Megjegyzés a méréshez

A `BuildStaticBaseLayer` 23–26 s és a teljes Build 28–33 s az Editor HIDEG
menetében — ez VÁLTOZATLAN, minden mai naplóban ott van, a változtatás
előttiekben is (a `classification=19,7 s` viszi). Nem ennek a tételnek a
hatóköre; a todo2 A4 külön kezeli.
