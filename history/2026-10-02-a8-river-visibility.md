# A8 — eltűnő folyók, ND-178

A felhasználó az ND-177 javítást elutasította: a folyók nem láthatók.
A saját korábbi halvány képet tévesen kezeltük elegendő vizuális bizonyítéknak.

## Ok és javítás

A felhasználói `PerfLog_20261002_210602.txt` szerint az aktuális generáció
1, majd 4 finom ágának mesh-e valóban publikálódott; a teljes 96 ág még
nem készült el a napló végéig. A 0,008 geometriai fél-szélesség távolról
szubpixeles, ezért a mesh jelenléte nem jelenti a vonal láthatóságát.

A folyómesh UV1 csatornája tartalmazza a bal/jobb vertexpár középvonalát.
A shader legalább 1 pixelre növeli a vetített fél-szélességet. A nagyobb
geometriai szélesség megmarad; a mélységteszt és a világítás változatlan.
A tavak/óceánok nem kapnak jelzőt, ezért nem szélesednek. A Core, a
szimuláció, az ágak és a seed/verzió változatlanok. Inspector-átállítás
nem szükséges a minimumszélességhez.

## Ellenőrzés

- Offline viewer Release fordítás: 0 hiba, 0 figyelmeztetés.
- Elkülönített Unity 6000.0.77f1/HDRP Play: 96 ág, 60 Ocean, 11 Pit,
  25 Merged, 0 MaxSteps; részleges és teljes mesh elkészült.
- Finom hálózat kész: 128,608 s; mesh 18,975 s falidő / 17,758 s aktív,
  legnagyobb szelet 7,9 ms, feltöltés 51,1 ms. Ez a feltöltés még nem
  teljesíti a frame-budget célt; a láthatósági ellenőrzés nem perf-PASS.
- Nincs C#-fordítási hiba, shaderhiba vagy futásidejű kivétel a menetben.
- Ugyanazon nappali modellállapot, kamera és mesh előtte–utána képe:
  UV1 jelző nélkül a régi szélesség; jelzővel az új. 1280×720 felbontás,
  közeli kamera 8, távoli kamera 74 egységnyi magasságban.
- Kézzel megtekintett képek: távolról korábban eltűnő vonalak most kék
  folyóvonalként kivehetők, közelről is láthatók. A nagyobb RGB-eltérésű
  pixelek száma 1023 / 2033; ez képdifferencia, nem önmagában elfogadás.

Bizonyíték: `artifacts/a8-width-play.log`, `artifacts/a8-play-result.txt`,
`artifacts/a8-width-before-8.png`, `artifacts/a8-width-after-8.png`,
`artifacts/a8-width-before-74.png`, `artifacts/a8-width-after-74.png`.
A közeli képnél 10 másodperc LOD-beállási időt hagytunk; a távoli képpár
azonos terepállapoton készült, a teljes távoli LOD-konvergencia nem állítás.

## Nyitott

A felhasználó saját jelenetének új látványítélete kell. A most jól látható
nyomvonal még szögletes, a hálózat természetessége nem megoldott: ezt nem
rejti el a láthatósági javítás. A8 továbbra is nyitott, durván ~80%; a
kontrollált teljesítmény-/memória-/deep-time kapuk hátra vannak. E kör
durva ráfordításbecslése 1–2 emberóra; a további látvány-/átvételi munka
4–8 óra durva becslés, a nyomvonalmodell esetleges áttervezése nélkül.
Commit és push nem történt.
