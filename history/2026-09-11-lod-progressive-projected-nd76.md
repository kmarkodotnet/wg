# 2026-09-11 — ND-75 kiértékelése és ND-76 javítás

A felhasználó jóváhagyta a kimért zoomhiba javítását. A
`PerfLog_20260911_195929.txt` régi kamerához készülő 2,5–10,6 s-os kéréseket,
180,40 px-es középső tile-t és álló kameránál 105,51 px-es statikus terepet
mutatott. A log elemzése után ND-76 és architektúra §3.13 előzte meg a kódot.

Implementált:

- ProjectedLodView: tényleges kamera téglalap alakú frustuma, terep-proxy
  kiterjedésű bounds, osztáshoz a proxy négy sarkának perspektivikus mérete.
  A proxy-úton nincs előzetes alapgömb-horizont/backface-elutasítás.
- 1024 új osztás/kérés, halasztás után folytatás álló kameránál is;
  strict cut-budget a balance fázisban, rendezett split-sorrend.
- Kooperatív megszakítás lényegesen elavult kameránál, legfeljebb egyszer
  két sikeres publikálás között. Nem fut párhuzamosan két cache-író worker.
- Teljes terrain/víz/border chunk emit-cache egzakt végleges pozíció-
  összehasonlítással; konfiguráció/Build/színmód invalidálás és sikeres
  alkalmazáshoz kötött cache-generáció. A szomszéd/morph változása sem vész el.
- Új ND-76 munkaszámlálók; ND-75 változatlan mérési definíció, 2 s-os indítás.

Az első gömbméretű osztási kísérlet túlosztott, ezért nem maradt aktív;
a quad-vetület lényegesen jobb. A 256-os munkaadag túl sok hullámot okozott,
a végleges 1024. A két logirány offline próbájában mélyen ~75–76%-kal
kevesebb tile kell változatlan középső végső LOD mellett. Közepes zoomnál
44–47% többlet is lehet. Az 1. mély irány első hulláma L13, végül L14;
ezek kiválasztási adatok, nem Unity-látvány/FPS-eredmények.

[Részletes mérés, határok és próbamenet](../docs/reviews/lod-progressive-projected-nd76-2026-09-11.md).

Ellenőrzés:

- Solution build: 0 hiba, 0 figyelmeztetés.
- Core 381/381 és CLI 7/7 a solution-futásban. Végleges LOD Debug 144/144,
  Release 144/144; 19 új ND-76 eset. Összesen 532 külön teszteset sikeres.
- Unity Assembly-CSharp + LOD fordítás: 0 hiba, 83 meglévő figyelmeztetés.
  Élő Editor/scene/performance ellenőrzés nem történt ebben a lépésben.
- Python KAT és vektorregenerálás nem futott: nincs telepített Python;
  Core-, modell-, seed-, referencia- és tesztvektorváltozás nincs.
- Új források saját meta-fájllal; az ignorált validation targets a generált
  Unity-csproj ellenőrzését segíti. Commit/push nincs.
- `git diff --check` tiszta; a Core/referencia/tesztvektorok diffje üres.

Az upload továbbra is hullámonként atomikus, nem több frame-es streaming;
a pozíciók feloldása és a teljes cut bejárása még megmarad. A base-proxy
nem szigorú mélyterep-bound. A vizuális kész-definíciót csak az új élő próba
igazolhatja. M9 tartalmilag továbbra is 60–70%. Durva, nem mért fejlesztői
ráfordítás-egyenérték 6–10 óra, hátralévő zoom/validáció 8–20 óra
(km-UI és új Core-részletmodell nélkül).
