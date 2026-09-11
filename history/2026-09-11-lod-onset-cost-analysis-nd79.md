# 2026-09-11 — Korai zoomküszöb és költség, ND-79

A felhasználó az ND-78 után az első közelítő görgetésekre alig reagáló
finomodást jelzett, logvizsgálatot és folytatást kért.

Forrás: `PerfLog_20260911_214108.txt`. Közepes zoomnál befejezett
állapotban 9,939 / 9,705 px-es statikus tereptile-ok maradnak. Proxyjuk
egyezik, megállásuk `below-threshold` a 10 pixeles első cél miatt.
Nem pusztán víz vagy késleltetés: a képközépen terep is 9,29 px marad.

A diagnosztikai program új `--quality` módja 2 logbeli irány × 13 állás
× 4 célbeállítás (104 állás) összehasonlítását végzi a meglévő ND-78
proxyval/óceáni kizárással. A runtime nem változott. A kisebb cél valóban
előrehoz finomodást, de 6/5 px mellett közepesen 2 930→96 969 dinamikus
levél és 728→23 640 L8-csoport is keletkezik. A globális csökkentést nem
adtuk át javításként, mert súlyos lassulást kockáztatna.

Az L6-csoportosítás ugyanitt 2 072 csoportot ad, maximum 151 levéllel,
de mélyen maximum 27 681 levelet is egyetlen chunkba gyűjtene. Nem
vezettük vissza a fix durva chunkolást. Javasolt következő munka: korlátos
méretű hierarchikus csomagolás, építési/upload-költség rendezése, majd
kisebb pixelcél együtt mért bevezetése. A felhasználótól irányt kértünk
ehhez az előfeltételhez vagy az azonnali, jelentősen lassabb minőséghez.

Ellenőrzés: Release offline próba sikeres, mind a 104 állás kiválasztási
budgeten belül; `git diff --check` tiszta. A próba a valódi linkelt LOD-
forrást használja, nem Unity-emissziót vagy GPU-mérést. Core-, runtime-
és scene-módosítás ebben a körben nincs, teljes tesztmátrixot nem
futtattunk újra. Élő Unity-validáció vagy elfogadott felbontásjavítás nincs.
Nincs commit/push, a korábbi dirty változások érintetlenek.

[Részletes elemzés](../docs/reviews/lod-onset-cost-analysis-nd79-2026-09-11.md).
M9 tartalmi becslés 60–70%, nem nőtt. Durva, nem mért ráfordítás-egyenérték:
2–4 óra elemzés; a javasolt megjelenítési átalakítás és zoomvalidáció
további 12–24 óra, a km-lépték és új Core-részletmodell nélkül.
