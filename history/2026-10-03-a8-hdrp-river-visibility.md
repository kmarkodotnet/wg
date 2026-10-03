# A8 — HDRP folyóláthatóság javítása, 2026-10-03

## Kiindulás és a korábbi bizonyíték korrekciója

A felhasználó újra futtatta a viewert: közel és távol sem látott folyót,
és távolabbi bolygónézetben is szeretné olvasni a hálózatot. A korábbi
teszthalasztás ezért már nem aktuális; a látvány ismét elutasított.

A natív képpárok kézi átnézése kimutatta, hogy az abszolút RGB-eltérés
sötétedést és zajt is „látható folyópixel”-nek számolt. Ez a korábbi
automatikus képi PASS-állításokat nem igazolja. A geometria-, erőforrás-
és Core-regressziók eredménye ettől független; a látványkapu újra nyitott.

## Végleges megjelenítési változás

- ND-181: ugyanazon Core-követő előbb 1 km-es áttekintést készít négy
  workerrel, 1/4/6/16/48/96 ágú publikációval. Csak ennek forrássorrendje
  kamera felőli; a finom hálózat eredeti kanonikus sorrendben számolódik.
  Az áttekintés a teljes finom eredmény elkészültéig megmarad, a panel
  külön számolja a finom ágakat. A kamera kezdőiránya Start előtt is érvényes.
- ND-185: saját `WorldGen/RiverOverlay` shader és felszabadított saját anyag,
  HDRP ForwardOnly passz, ZWrite Off, ZTest LEqual, Cull Off. A kategória-kék
  térképi jelölés éjjel is látszik; a bolygó túloldalát a mélységteszt takarja.
  A szélesség a modell vízhozamsúlyát követi, legalább 4 képernyőpixel.
  A közös terep-/vízshader folyóspecifikus módosításait visszavontuk.
  A shader az Always Included listába került, hogy a runtime `Shader.Find`
  hívás miatt ne maradjon ki a player csomagból.
- A kész finom hálózat átveszi az áttekintés helyét, a panel átnevezése
  követi az adatot. Az eredeti 50 m / fineDepth=4, 0,008 fél-szélesség és
  Core modellút megmarad. Nincs e javítás miatt generátorverzió-váltás.
- ND-182: a közös shader Cull módjának önálló változtatása nem javított.
  ND-183: 1 km-es végleges követés / nagyobb fineDepth próbája visszavonva.
  ND-184: tartós áttekintés mint kerülő megoldás visszavonva, mert a saját
  shaderrel az eredeti finom hálózat is látható. Ezek nem aktív beállítások.

## Bizonyíték és korlátai

Elkülönített Unity 6000.0.77f1 / HDRP projektmásolat, a felhasználó futó
Editorának módosítása nélkül. A korábban kiszámolt eredeti 50 m-es natív
adat visszajátszása 96 ággal, 1280×720 képpárokon: alapnézet 450, távoli
nézet 447, közeli nézet 25 445 valódi kék folyópixel. A távoli kamera
300 helyett 600 egységre van. Kézzel is ellenőrzött távoli és közeli kép.
Ez renderer-bizonyíték, önmagában nem a teljes háttérmunka validációja.

Az új képi kapu csak a bolygó vetített korongján belül számol:
`after.b > after.g + 15`, `after.g > after.r + 15`,
`after.b > before.b + 20`. Sötétedés és csillagzaj nem siker. A vizsgálat
folyóréteg ki/be képpárt használ, és kézi képellenőrzéssel egészül ki.

Aktuális offline viewer Release-fordítás Unityvel egyező beállításokkal:
0 hiba, 113 meglévő figyelmeztetés. Solution-fordítás: 0 hiba / 0 warning;
viewer LOD regresszió: 664/664 PASS. Ezek nem helyettesítik a natív próbát.
Teljes, újraszámoló natív Play-menet: **PASS**, piros script-/shaderhiba
nélkül. Első látható ág: 22,2 s a harness indításától; 93 kék pixel az
alapnézetben, 57 a kétszeres távolságon. Teljes áttekintés: 79,8 s,
554 kék pixel. Teljes kanonikus finom hálózat: 231,5 s, 449 kék pixel;
távolról 374, közelről 25 912. A panel mögötti `preview=False` mindhárom
végleges nézetben. A futó fizikai klíma egy köztes új Buildet váltott ki;
ezért ezek nem kontrollált optimalizálási összehasonlítások.

Az utolsó generáció 96 ága 198,020 s alatt számolódott ki; a 383 035
pontos finom mesh előkészítése 18,662 s falióra, 17,459 s aktív munka,
legfeljebb 7,6 ms-os geometriai szeletekkel. A 47 mesh-rész összes
feltöltése 42,7 ms, legnagyobb upload 1,1 ms, publikáció 0,1 ms.

Négy natív EditMode eset: **4/4 PASS** — kamera Start előtti iránya;
finom progresszió alatt megmaradó áttekintés, elavult generáció eldobása és
végleges adatcsere; bitazonos háromszög-/normálgeometria részekre bontva,
atomikus publikáció és erőforrástakarítás 1, illetve 20 megszakítás után.
A teszt a saját, támogatott shader közös anyagát is ellenőrzi.

Lokális bizonyítékok (az `artifacts/` nincs verziózva):

- `artifacts/a8-visibility-result.txt`, `a8-visibility-final-oct3.log`;
- `artifacts/a8-visible-final-canonical.png`, `a8-visible-final-far.png`,
  `a8-visible-final-close.png`;
- `artifacts/a24-unity-validation/Logs/PerfLog_20261003_102033.txt`;
- `artifacts/a8-river-mesh-tests-oct3.xml`, `a8-oct3-viewer-build-final.log`.

A felhasználó saját Editorában történő új átvétel még hátra van; a natív
ellenőrzés alapján nem jelentjük késznek a B3 hálózatalakot.

## Nyitott munka és durva becslés

A közeli képen már kék folyó látszik, de a szögletes utak, megszakadások,
hamis közelségi összefolyások nem javultak ettől. ND-180 / B3 numerikus
modelljavítás külön feladat, Python-orákulummal, majd C# porttal és a
szükséges kompatibilitási döntéssel. Nem dekoratív spline-nal fedjük el.

A8 tartalmilag súlyozott, durva állapotbecslés: **~80%**, nem lezárt.
E kör durván **4–6 emberóra**; kézi átvétel, kontrollált memória/GPU és
deep-time teljesítménykapuk **4–8 óra**, új hibák nélkül. A B3 modelljavítás
további **8–24 óra durva becslés**, az orákulum eredményétől függően.
Új felhasználói Play-próba még szükséges. Commit és push nem történt.
