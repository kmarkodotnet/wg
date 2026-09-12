# Precíz fizikai léptékcsík — ND-84 (2026-09-11)

## Kérés és párhuzamos hatókör

A kamerafüggő km-lépték a másik Codex-threadben futó tile-/víz-LOD munka
mellett készült. A megoldás nem olvas tile-számot vagy LOD-szintet, nem
módosít kiválasztást és nem igényel scene-változtatást. A kamera meglévő
komponense kapott külön partial UI-fájlt, a `PlanetGridMesh` pedig külön
partial fájlban csak a renderrel egyező radiális felszínsugarat adja át.

## Mérési definíció

- A látható referencia a viewport közepe, egy kis kereszttel jelölve.
- A két képernyősugár a túlrajzolt szárazföldet vagy a renderelt vízszintet
  metszi.
- A metszési irányok közti nagy körív a Core `7 420 000 m` fizikai sugarát
  használja; a Unity-egység és a vertikális túlrajzolás nem kerül a km-be.
- Az 1/2/5 sorozatból választott kerek m/km érték pixelszélességét felezés
  oldja meg, nem lineáris közelítés.
- Hiányzó metszés, modell-snapshot, perspektíva vagy egységes bolygóskála
  esetén explicit `Lépték: —` jelenik meg.
- A domborzati mintázás 0,15 másodperces minimális időközzel fut, hogy ne
  terhelje minden frame-ben a tile-worker mellett a főszálat.

## Ellenőrzés

- ND-84 tiszta geometriai tesztek: gömbmetszés, ég/inside érvénytelenség,
  radiális felszín, nagy körív, 1/2/5 kerekítés, nemlineáris pixelszélesség,
  több FOV/felbontás/képarány/kameratávolság/fizikai sugár.
- A teljes viewer parancssori tesztprojekt a kibővített mátrixszal
  266/266 PASS; ebből az ND-84 célzott tesztjei 16/16 PASS.
- A teljes gyökér solution build PASS, 0 warning, 0 error.
- Offline `Assembly-CSharp` ellenőrzésben az ND-84 fájlok saját diagnosztika
  nélkül fordultak; a generált projekt 77 meglévő nullable/obsolete baseline
  hibája ettől függetlenül megmaradt.
- Élő Unity Game view ellenőrzés szükséges az elhelyezéshez, a teljes
  zoomtartományhoz és a frissítési költséghez.

## 2026-09-12 – kijelzési stabilitás

Az első élő próba során a numerikus érték csak ritkán maradt látható. Minden
mintavétel elején elveszett az előző jó mérés, a radiális felszínmetszés pedig a
megjelenített, `float` pontosságú hálónál indokolatlanul szűk tűréssel dolgozott.
A kijelzés most nyolc egymást követő átmeneti mérési hibáig megtartja az utolsó
hiteles értéket; a biztosan érvénytelen konfigurációt továbbra is azonnal jelzi.
A felszíniteráció csillapítást és a renderpontossághoz igazított tűrést kapott,
a felirat és a vonal pedig áttetsző sötét hátteret használ.

A második élő próba közeli zoomnál még talált kiesést. Ilyenkor egy iterációs
köztes gömb sugara nagyobb lehetett a kamera középponttól mért távolságánál,
miközben a tényleges irányfüggő felszín előtt továbbra is volt érvényes metszés.
A gyors iteráció sikertelensége esetén ezért közvetlen, előjeles sugárparaméteres
gyökkeresés keresi meg az első radiális felszínmetszést. Külön regresszió fedi a
felszínhez 0,01 megjelenítési egységre lévő kamerát és a meredek közeli reliefet.
