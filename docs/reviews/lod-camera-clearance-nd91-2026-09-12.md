# ND-91 — Helyi felszínkövető kamerakorlát, első kapu

## Előző feltöltési kapu visszamérése

A 2026-09-12-i PerfLogokban aktív az ND-89. Kvantilis: rendezett minta,
`floor((n-1)*p)`. Nem kontrollált A/B: viewport és relief is változott.

| Log időpontja | Commitok | Commit medián / p90 / max (ms) | Natív maszk max (ms) | Maszkterv max (ms) |
|---|---:|---:|---:|---:|
| 10:40:03 | 143 | 0,50 / 1,53 / 5,21 | 0,59 | 5,24 |
| 10:49:35 | 25 | 0,71 / 1,25 / 4,32 | 0,52 | 2,76 |
| 11:20:23 | 29 | 0,50 / 1,50 / 4,58 | 0,55 | 1,41 |

A 11:22:38 logban csak egy commit volt, nulla dinamikus tereplevéllel;
nem bizonyítja a mélyzoom-upload működését. A két korai log fizikai 1×,
a 11:20-as 10×, a 11:22-es 111× reliefet jelöl; a korábbi ND-87 próba
más magassági skálával futott. Nem igazolt egységes gyorsulási arány.
A natív maszk nem mutat indokot újabb nagy upload-átalakításra. A terv
5,24 ms-os csúcsa és a teljes kérés akár 3700,4 ms-os késése nyitott.

## Mi változott?

A korábbi fix `surfaceRadius` nem követte a helyi hegységet vagy tengert.
A kamera ezért a magasabb felszínbe közelíthetett, és a zoom érzékenysége
nem a tényleges helyi modellmagassághoz igazodott.

Most a kamera alatti meglévő modellpont-lekérdezés adja a sugarat, az
aktuális relief-skálázással. A görgő és forgás e fölötti magassággal arányos.
A minimális rés a korábbi `minDistance - surfaceRadius`, legalább 0,001
világ-egység és 1,1 × near clip. A jelenetben a near clip 0,3, ezért a
legkisebb rés most 0,33 egység a korábbi 0,1 helyett: a kamera nem mehet
olyan közel, ahol a középső felületet már levágná a near plane.
Alacsony maxDistance nem írja felül a
felszínvédelmet. A FlyTo célmagassága a célpont felszínéhez igazodik, az út
közbeni transzformációkon is érvényes a korlát. Emelkedő terep kifelé
korrigálhat; nincs új simító animáció, amely átmenetileg a felszínbe engedne.

Pontosan azonos lokális irány és világ-snapshot esetén nincs új modellminta.
A target forgása érvényteleníti az irányegyezést; a világ újraépítése a
snapshotrevíziót. Konfigurációváltás közben átmeneti fallback marad.
Nincs mesh-építés, LOD-választás vagy új Core-szimulációs algoritmus.

**Korlát:** ez nadír-pontmintás védelem, nem teljes rendermesh-ütközésvizsgálat.
Durva vagy morpholt háromszög, meredek hegyoldal, near-plane sarok és külön
tóvízszint eltérhet a modellponttól. Az oldalak védelme következő kapu lehet,
ha az élő próba indokolja. A forgás közbeni modellmintavétel költsége mérendő.
Küszöböt, reliefet, scene-beállítást nem módosítottunk; az első zoomok
beragadása továbbra is halasztott backlog.

## Próbamenet

1. Unity fordítás után Play: a kamerán `Follow Local Surface` alapból aktív.
2. Óceán fölött közelíts a minimumig, majd távolodj: ne akadjon ott a görgő.
3. Hegység fölött ismételd, majd kis magasságban húzd oldalra a kamerát.
   Figyeld a felszínbe jutást, near-plane vágást és a kifelé korrekciót.
4. Panelről FlyTo: a köztes út és a célhely se menjen a helyi modell alá.
5. Álló kamerán világidő/relief-váltás után ellenőrizd az új magasságot.
6. Ha rossz az eredmény, `Follow Local Surface` kikapcsolásával a korábbi
   alapgömbös kameramód visszakapcsolható; scene-et automatikusan nem mentünk.

Új log: `[ND-91 camera surface]`, másodpercenként. `distanceUnits` a
tényleges kameratávolság; `surfaceRadiusUnits` a modell/fallback sugár;
`altitudeUnits` a kettő különbsége. `outwardCorrectionUnits` az időablak
legnagyobb kifelé korrekciója; `sampleTotalMs` az ablak összes lekérdezési
ideje, nem egyetlen frame vagy egyetlen modellminta. `source=model|fallback`
nem állít renderháromszög-metszést. Az ND-75 tényleges tile-mérése változatlan.

## Ellenőrzés

- Solution build: 0 hiba / 0 warning.
- Viewer-LOD: **290/290 Debug és Release**, ebből 12 új kameramatematikai eset.
- CLI: **8/8 sikeres** a teljes futásban.
- Unity-forrás: 0 hiba / 83 korábbi warning; Editor LOD-assembly:
  0 hiba / 4 korábbi warning. Három új valódi kamera/transform/cache
  integrációs eset fordítva, **Editorban nem futtatva**. Ezek előállított
  cache-fixture-t használnak, nem teljes világépítést vagy FlyTo-korutintesztet.
- A teljes solution Core-eredménye **375 sikeres / 9 hibás / 384 összesen**.
  A hibák referenciaeltérések (plate boundary, deep-time erosion, precipitation,
  river path, state hash, flow network, lakes/ice, TestEarth kontinensméretek,
  feature-szegmentáció); a munkafában
  párhuzamos ND-90 Core-/vektormódosítások vannak. Ez a lépés nem írta azokat,
  és nem állít teljes projekt-szintű zöld teszteredményt. Numerikus referencia-
  újragenerálás e viewer-feladatban nem történt.

Élő Unity/FPS-elfogadás még nincs. M9 tartalmilag durván **60–70%**.
Durva, nem mért ráfordítás-egyenérték: e kapu 2–4 óra; próba/korrekció
1–3 óra; fennmaradó zoom/render munka 8–20 óra.
