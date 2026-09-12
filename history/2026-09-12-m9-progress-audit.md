# 2026-09-12 — M9 haladásjelentés korrekciója

A felhasználó számon kérte a kb. tíz átadáson keresztül ismételt
„60–70%; 8–20 óra” becslést, és közben subagenten kérte a következő
tile-feladatot. A jelentési hibát elismertük: a tartományok nem újraértékelt
állapotot, hanem továbbmásolt korábbi becslést jelentettek.

[Audit](../docs/reviews/m9-progress-audit-2026-09-12.md): hat explicit,
tartalmilag súlyozott csoport és az implementáció/elfogadás elválasztása.
Új állapotbázis kb. 55% (durva 50–60%): nem visszafejlődés, nem összevethető
a korábbi levezetetlen számmal. A régi becsléseket történeti bejegyzésként
megőriztük, aktuálisként visszavontuk. Javult a milestone főtáblája is:
a FlyTo már nem szerepel benne meg nem kezdettként.

Új, tételes maradék alapbecslés 14–28 fejlesztői munkaóra-egyenérték a
most indított helyreállítási csomaggal együtt. Nem mért munkaidő vagy
határidő; felhasználói várakozás, új Core/mikroterep/M13 nincs benne.
A lezárások utáni aktuális érték az audit táblájában követendő.
Az ND-92 implementációját e körben lezártuk: a maradék sorok összege
13–26 óra lett. A teljes vizuális kapu és az 55%-os új állapotbázis ettől
nem változott; a natív Editor-próba a regressziós keretben marad.

A 12:51:45 logban az upload és ND-91 aktív, de a legközelebbi
modellmagasság 25,29 egység, tehát a közeli kameravédelmet nem próbálta ki.
Külön friss Core-ellenőrzés 384/384 sikeres: a párhuzamos ND-90 munka
köztes állapotában tapasztalt 9 vektorhiba már nem aktuális.

Explicit felhasználói kérésre a `tile_mask_recovery` subagent kapta az
ND-89 részleges natív maszkfeltöltés utáni hibafallback reprodukcióját és
szűk javítását (ND-92). A főszál a milestone/backlog/audit fájlokat kezelte;
kód/architektúra/döntés fájlokat az agent, felülvizsgálattal. Nincs commit/push.
