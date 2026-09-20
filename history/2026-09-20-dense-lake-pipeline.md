# 2026-09-20 — Sűrű (tömbindexelt) tó-pipeline

## A feladat

A todo.md 1. tábla 4. sora: tömbindexelt `IdentifyLakes`, ami a már meglévő
dense field/fill/ocean tömböket és az újrahasznált topológiát olvassa; a
Dictionary-út marad referencia-orákulumnak. Becsült nyereség: ~190 ms.

**A becslés pontos volt.** A felhasználó naplóiban a hidrológia-sor:
`lakes=180,2–189,8 ms`, plusz `conversion=12,0–14,1 ms`.

## Mit talált a vizsgálat

Két dolog derült ki, mielőtt egy sort is írtam:

**1. A viewer a `LakeResult`-ból CSAK a `Lakes` listát használja.** Az
`IsLake` / `Depth` / `LakeId` dictionary-k — egyenként 393 216 bejegyzéssel —
felépülnek, és senki nem olvassa őket.

**2. A `ToFilledDictionary` konverziónak egyetlen fogyasztója volt**: maga az
`IdentifyLakes`. A sűrű flood eredménye már `double[]`, aztán 393 216
dictionary-beszúrással átalakult, hogy a Dictionary-alapú tó-detektálás
megehesse.

Vagyis a költség nagy része nem a tó-keresés, hanem a körítése.

## A bitre azonosság kulcsa

A Dictionary-változat a kulcsokat **(face, u, v) lexikografikusan rendezi**
(`FieldKeysInFaceUvOrder`) — ez level 8-on 393 216 elem rendezése, hasonlításonként
két Morton-dekódolással. A sűrű index viszont definíció szerint
`face * n² + u * n + v`, tehát a `0..Count-1` bejárás **pontosan ugyanez a
sorrend**. A rendezés nem helyettesítendő, hanem **elhagyható**.

Ezen felül gondosan replikáltam:
- ugyanaz a verem (LIFO) és ugyanaz a szomszéd-sorrend (`TileDirection` 0..3);
- a `visited` jelölés a **betevéskor** történik, nem a kivételkor (ez
  befolyásolja a bejárási sorrendet);
- így a `LakeInfo.Tiles` **elemsorrendje** is azonos — és ezért az összegzési
  sorrend is, tehát a lebegőpontos összeadás nem-asszociativitása sem okozhat
  eltérést a `SurfaceElevation` / `MeanDepth` értékekben.

Ez utóbbi nem elméleti óvatosság: ha a komponens-bejárás sorrendje eltérne, a
tó **alakja változatlan** maradna, csak a felszín-elevációja csúszna el pár
ULP-pel — csendben, vizuálisan észrevehetetlenül.

## Igazolás

`DenseLakeEquivalenceTests` (12 teszt), a Dictionary-úttal mint orákulummal:

- **5 valódi világ** (különböző seed, lemezszám, szint, víz-arány): 120–659 tó,
  minden tó minden statisztikája **bitre** egyezik, a tile-listák
  elemsorrendjéig;
- **4 különböző `minDepth` küszöb** (0 / 1 / 25 / 500 m) — enélkül az egyezés
  csak az alapértéken lenne igazolva;
- **élesetek**: csupa óceán (0 tó) és csupa tó (1 tó);
- a méret-ellenőrzések tényleg dobnak.

**Sebesség** (level 7, 98 304 tile, 1294 tó): Dictionary **17,9 ms** →
tömbindexelt **1,0 ms** = **18,3×**.

## A viewer-oldali változás

A sűrű ágon a `ToFilledDictionary` teljesen kimarad, és az
`IdentifyLakesDense` fut. A Dictionary-út tartalék marad arra az esetre, ha
nincs sűrű adat (`hydroLevel == level`). A PerfLog `lakes=` sora mostantól
kiírja, melyik út futott (`dense=True/False`).

**Futásidejű igazolás hátravan**: a Unity Editor főszála ebben a
munkamenetben leállt válaszolni, így Play módban nem tudtam újramérni. Az
Editor újraindítása után a `lakes=` és `conversion=` sorok mutatják meg a
tényleges nyereséget.

## Tanulság

**Mielőtt optimalizálsz, nézd meg, mit fogyaszt a hívó.** A feladat úgy szólt,
hogy „a tó-detektálás legyen tömbindexelt" — és az volt a kisebbik fele. A
nagyobbik fele három soha nem olvasott dictionary és egy fölösleges konverzió
volt, amit csak az hozott elő, hogy megnéztem a hívási helyet.
