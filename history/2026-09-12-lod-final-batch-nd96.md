# 2026-09-12 — Tile/zoom checkpoint és összevont ND-96 csomag

## Felhasználói kérés

A meglévő módosítások commitja után az összes fennmaradó tile/zoom lépés
folytatása, köztes kézi ellenőrzések nélkül. A végén teljes közös próbalista.
A korábbi korai-élesség/selection halasztását ez feloldotta.

## Végrehajtás

- `99b3ac4` checkpoint: viewer/tile munka és kapcsolódó tesztek/dokumentáció/
  scene; push nélkül. A külön Core/reference/CLI változások nem kerültek bele.
- ND-96: kamerafüggetlen, korlátos proxy-geometria cache, aktuális nézetű
  újravetítés; külön víz-worker-cache és forrás-/nézetazonosság.
- Kész CPU-mesh sarkok visszacsatolása explicit Unity → Core tengelycserével,
  ősi bounds és revízióvédelem; új finomítási hullám szükség esetén.
- Egy szomszédpasszos balance, páros regresszió a korábbi algoritmussal.
- A chunk-ellenőrzés már kiszámolt sarkait az emit újrahasználja.
- Végleges 8/7 px cél a kódban és scene-ben; érvényes merge-küszöb
  az Inspector 1-es/nem véges faktoránál is. Relief/magassági modell változatlan.
- Új részletes feedback/cache naplózás; változatlan ND-75 tényleges rajzmérés.

## Elvetett változatok és kockázat

A nyers tengerfenék-proxy a teljes kifutásban 200 ezer levelet és
13–16 s összes cut-időt is okozott: visszavonva, runtime proxy változatlan.
A 8/6 első beállítás helyett 8/7 maradt. Az új cache önmagában 22–26%-kal
csökkentette a páros mozgókamerás cut költségét; a sűrűbb végállapot
összesített elérése viszont több hullámot kér. Ezért nincs elfogadott
teljes zoom-/FPS-gyorsulás. Az ND-96 új munkája nincs újra commitolva.

## Ellenőrzés

Solution build 0 hiba/warning. Core 384, CLI 8, viewer 340 eset zöld,
összesen 732 részenként futtatva; viewer Debug és Release. Új 25 .NET-eset,
4 új, csak fordított Editor-eset. Unity runtime 0 hiba / 83 meglévő warning,
Editor-tesztprojekt 0 hiba / 4 package-reference warning.

A szigorú diagnosztikai 0-byte teszt egyszeri 7360-byte eltérése után
a mérés külön szkennelő szálra került, a 0-byte elvárás megmaradt. Nincs
renderer-algoritmus módosítás vagy toleranciaemelés ehhez kapcsolódóan.
Python KAT nem futott: nincs elérhető telepítés (`python`, `py -3`).
A külön munkaszál vektoraihoz nem nyúltunk. Natív Editor-test XML nem
keletkezett; az ideiglenes tesztindítást és kérését eltávolítottuk.

## Átadás

[Teljes implementációs összefoglaló, mérések és közös ellenőrzőlista](../docs/reviews/lod-final-batch-nd96-2026-09-12.md).
Nincs köztes kézi kapu; a natív/vizuális/performance próba egyben következik.
Súlyozott M9 61,25% (kb. 61%): csak a korai minőségi csoport 25 → 50
állapotpontja változik. A teljes cél nem elfogadott. Durva maradék
5–11 fejlesztői óra a végső ellenőrzésre és esetleges korrekcióira,
nem mért munkaidő és nem határidő.
