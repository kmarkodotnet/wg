# Backlog — hátralévő feladatok mérföldkövenként

## Aktív sorrend — felhasználói döntés, 2026-09-11

**Claude-visszaadás, 2026-09-12:** [teljes aktuális tudásátadás](../kt_2_co2cl.md).
Az átadáskor újonnan megtalált `PerfLog_20260912_202309.txt` már aktív
ND-96/97/98-at mutat: 64 kérés, 27 stabil víz-reuse, total p50/p90
746/1073 ms. A végén 64220 terrain levél, 3770 halasztott split és új
kérés: nincs kész minőségi végállapot. A fő maradék a worker selection/
balance/corner/resolve munkája; ez nem kontrollált összevetés a régi loggal.
A korábbi „nincs új ND-96 log” sorok történetiek, vizuális és natív
elfogadás továbbra is nyitott. Részletek, fájlfelelősségek, folytatás: átadás §8–11.

**ND-98, stabil víz-cut:** [ismételt kiválasztás kihagyása](reviews/lod-water-reuse-nd98-2026-09-12.md)
implementált, 18 új regressziós esettel. Pontos fixpont-igazolás után a
változatlan víz fedése újrahasználható; nézet/paraméter változáskor teljes
számítás. A páros álló kamerás vízpróbában 74% idő- és 65% allokációcsökkenés,
azonos fedés; mozgó kameránál nincs kihagyás. Nem teljes zoom/FPS-mérés.
M9 61,25% és durva 5–11 óra maradék: az élő kapuk változatlanul nyitottak.

**ND-97, további munka kézi kapu nélkül:** [három allokáció-/cache-javítás](reviews/lod-cache-allocation-nd97-2026-09-12.md).
Metrikatároló újrahasználata terrain/víz nézetváltáskor, helyi őslánc-
érvénytelenítés feedback után, allokációmentes azonos-quad összehasonlítás.
15 új .NET-eset; 8/7 cél és keretek változatlanok. Az izolált tárolópróba
52–61% kevesebb cut-allokációt és 5–9% kisebb cut-időt adott, azonos
kiválasztással. A végső kézi lista megmarad, most nem kérünk ellenőrzést.
M9 kb. 61%, durva maradék 5–11 óra: teljes reakcióidő/élő kapu még nincs.

**Aktuális ND-96 átadás:** [összevont implementáció, mérések és teljes végső ellenőrzőlista](reviews/lod-final-batch-nd96-2026-09-12.md).
A nézetváltásos geometria-cache, a kész mesh visszacsatolása, egyszeres
balance, ismételt sarokfeloldás elhagyása, víz-cache/hiszterézis és a 8/7 px
korai minőségi beállítás implementált. 732 .NET-eset részenként zöld,
340 viewer-eset Debug/Release; 4 új Editor-eset csak fordított. A nyers
mélység-proxy túl drága változata visszavonva. A sűrűbb teljes képnek
kimért többletköltsége van, teljes zoomgyorsulást nem állítunk.
**Most egyetlen összevont kézi/natív kapu marad**, és az ott igazolt hibák
korrekciója: korai/közepes élesség és átmenet, víz/part, minimumkamera/
FlyTo/lépték, világváltás, hosszú memória-/FPS-próba. Súlyozott M9 kb. 61%;
durva maradék 5–11 óra, nem mért idő. Core/M13/natural-coast modellezés
nem része ennek a lezárásnak. A korábbi „halasztva” állapotok történetiek.

**2026-09-12, teljes folytatás engedélyezve:** checkpoint `99b3ac4`;
a felhasználó egyetlen végső kézi próbát kér. A korai élesség/selection
halasztását feloldotta. Aktív: nézetváltásos geometria-cache, valódi
quad-visszacsatolás, balance/emisszió, korai finomodás, majd a teljes
víz/kamera/lépték/upload regressziós és kézi átadás (ND-96). A lentebbi
„halasztva/próbánként várunk” sorok történetiek. Core/M13 nem új hatókör.

**ND-95, három korrekció közös átadása:** [részletek és próbasor](reviews/lod-navigation-measurement-batch-nd95-2026-09-12.md).
Monotón kérés-/worker-/várakozási időmérés; kamera/világkontextushoz kötött,
kevert modelladatot elutasító km-lépték; teljes útján helyi magasságot
interpoláló FlyTo. 707/707 .NET PASS, viewer Release 315/315; 17 új Editor-
teszt csak fordított. Élő minimumkamera/FlyTo/lépték és natív regresszió
következik. A korai élesség/selection halasztása változatlan. A tételes
durva maradék 11–23 óra, nem mért idő; M9 kb. 55%, teljes kapu nem zárult.

**ND-93/94 élő visszamérés (14:02-es próba):** [eredmény](reviews/lod-resource-staging-live-2026-09-12.md).
1568 inaktív kulcs ürült; a 337-es átmeneti többlet a következő mintára
128-ra csökkent. 104 commit / 256 szelet, szeletmax 3,18 ms, commitmax
4,55 ms. A két részfunkció loggal igazolt; teljes memória/FPS/vizuális
elfogadás még nincs. A kész kép 26,975 px-es terepquadját a proxy csak
9,635 px-nek méri: a halasztott minőségi hiba új reprodukciója, nem upload-
késés. Egy 9,906 s-os kérésnél Editor-szünet/fókusz tisztázandó; a Build
utáni requestAge a frame-időbélyeg miatt Build-időt is tartalmazhat.
Következő aktív kapu a víz/kamera/lépték és Editor-regresszió ellenőrzése;
ez az elemzés nem aktiválja újra a halasztott selection/élesség feladatot.

**ND-93/94 közös átadás (2026-09-12):** a felhasználó 2–3 feladat után,
egyben ellenőriz. Két upload/cache tétel implementált: korlátos inaktív
chunk-megtartás és saját mesh-életciklus; külön ütemezhető mesh/rendercél
előkészítés. [Egyesített próbamenet és logmezők](reviews/lod-resource-staging-batch-nd93-94-2026-09-12.md).
Viewer 304/304 Debug/Release; teljes .NET 696/696, Unity-forrás fordított.
Élő memória/FPS/vizuális és natív Editor-teszt még szükséges. A korai
élesség/selection továbbra is halasztott; ez nem új felbontási küszöb.

**2026-09-12, státuszaudit:** a korábbi 60–70% / 8–20 óra ismétlése nem
volt újrabecslés. Az [aktuális, tételes M9-állapot](reviews/m9-progress-audit-2026-09-12.md)
felülírja a lentebbi történeti becsléseket. Kódba került funkció, loggal
igazolt működés és felhasználó által elfogadott cél külön állapot.
A felhasználó most egy subagentet is kért: a következő szűk upload-feladat
a részlegesen sikertelen terep-/vízmaszk-feltöltés utáni teljes helyreállítás
([ND-92, implementált](reviews/lod-mask-recovery-nd92-2026-09-12.md), natív
Editor-próba még hátra). A korai élesség/selection halasztását ez nem oldja fel.
A víz-LOD, több frame-es upload, km-lépték és pontmintás kamerakorlát már
implementált; ezek lent szereplő „ezután” sorai történeti sorrendet írnak le.

**ND-91, következő kapu implementált (2026-09-12):** a helyi modellfelszínhez
igazodó orbitkamerakorlát, arányos zoom/forgás és FlyTo-bekötés elkészült.
[Részletek, ND-89 visszamérés és próbamenet](reviews/lod-camera-clearance-nd91-2026-09-12.md).
Azonos irány és világ esetén cache, külön `[ND-91 camera surface]` log;
nincs relief-, scene- vagy élességküszöb-módosítás. **Élő próbára vár.**
Ez nadírmodell-pontminta, nem teljes mesh-/near-plane-ütközésvédelem;
a meredek oldalak, tavak, morph és forgás közbeni költség még ellenőrizendő.
Az ND-89 aktív a friss logokban (natív maszk max 0,59 ms); az upload teljes
vizuális/FPS-elfogadása és az első zoomok beragadása továbbra is nyitott.

**ND-87 visszamérve, ND-89 próbára vár (2026-09-12):** az új logban a
terep-publikálás maximuma 1,98 ms; a leglassabb, 9,85 ms-os commitból
7,95 ms a terepmaszk. [Mérés és következő kapu](reviews/lod-prepared-mask-nd89-2026-09-12.md):
a maszkterv/snapshot előkészítése a stagingbe került, revízióvédett
alkalmazással és külön CPU/natív részidőkkel. A natív indexfeltöltés még
egy commit, élő ND-89 nyereség nem igazolt. Az ND-87 25,76 ms-os staging-
tüskéje is nyitott (stageTarget összköltség 28,40 ms az érintett kérésben).
A teljes zoomkésés és a korábbi élességi/proxyhiba továbbra is backlog.

**ND-87 implementált, próbára vár (2026-09-12):** a felhasználó jóváhagyta
a tereppublikálás következő kapuját. [Új rendercélok előkészítése és részidők](reviews/lod-terrain-publication-nd87-2026-09-12.md):
inaktív objektumkészítés és komponens-/diagnosztikai előkészítés a stagingben,
csak a teljes sor után képcsere. Külön mesh/anyag, diagnosztika, aktiválás,
kikapcsolás mérés; commit előtti megszakítás takarítja a kérés új céljait.
Élő teljesítmény-/vizuális mérés még kell; a teljes késés és a korábbi
élességi/proxyhiba nem megoldott, küszöbmódosítás nem történt.

**ND-86 élő log, 2026-09-12:** [mérés és következtetés](reviews/lod-auxiliary-upload-live-2026-09-12.md).
Az aux-staging aktív: a vízpublikálás maximuma 10,71→0,50 ms, a commité
15,83→7,88 ms két eltérő kameraútban; nem kontrollált FPS-benchmark.
A teljes kérés mediánja 659→669 ms, tehát a zoomreakció nem oldódott meg.
A commit maradék csúcsa tereppublikálás (6,30 ms); ennek részletes mérése
és előkészítése a következő javasolt upload-kapu. A kész képben 20,804 px
terepquad / 11,804 px proxy is marad: a korábban halasztott minőségi tétel
nyitott. Ebben a körben nincs kódváltozás vagy teljes vizuális elfogadás.

**2026-09-12, folytatás:** a felhasználó a víz-LOD után a következő tile-
feladatot kérte. Az [ND-85 több frame-es terep-upload](reviews/lod-staged-upload-nd85-2026-09-12.md)
első kapujáról megérkezett az élő log: 284 feltöltési szelet p90 értéke
1,29 ms, de a végső commit maximuma 15,83 ms. Az erre épülő
[ND-86 víz-/határvonal-upload](reviews/lod-auxiliary-upload-nd86-2026-09-12.md)
implementált, új élő próbára vár. A víz összefűzése workerre, a mesh-ek
feltöltése a közös staging-sorba került. A maszk és referencia-/aktivitásváltás
még egy commit-frame; egy nagy vízmesh is túllépheti a puha keretet.
A víz korábbi vizuális ellenőrzési pontjai sem tekinthetők automatikusan lezártnak.

- **Halasztva, nem megoldva:** az első zoomok beragadása, a mozgókamerás
  selection gyorsítása, a korai élesedés küszöbe és a sima időbeli átmenet.
  Az ND-81 élő log és a geometria/proxy eltérés bizonyítékai megmaradnak.
- **Élő próbára vár: vízfelszín saját LOD-ja (ND-82/83).** Első kapu
  [implementált, 24 célzott tesztesettel](reviews/water-lod-selection-nd82-2026-09-11.md):
  a ténylegesen létező víz-base-tile-okból külön, korlátos tile-kiválasztás
  és teljes fedés, tengerfenék-/klíma-mintavétel nélkül. A második kapu
  [renderbe kötve](reviews/water-lod-render-nd83-2026-09-11.md): külön vízszín,
  statikus vízmaszk, váltott mesh-publikáció és tényleges vízquad-diagnosztika.
  Következő ellenőrzés: élő part-/víz-/visszazoom- és világváltás-próba.
  Ez nem zárja le a teljesítmény- vagy vizuális elfogadást.
- Ezután külön lépések: több frame-es upload; felszínkövető kamerakorlát;
  precíz km-lépték. Az általános teljesítmény- és minőségi hibák nem zárulnak
  le attól, hogy most másik feladatra lépünk.

**ND-81 élő eredmény (2026-09-11):** az [új logelemzés](reviews/lod-work-cache-live-2026-09-11.md)
szerint az állókamerás cache működik, de az első mozgások selectionje
285–374 ms, nulla cache-találattal. A felzárkózás 3–4,2 s; a kész állapotú
9,881 px-es statikus tile küszöb miatt nem osztódik. Következő javasolt
teljesítményfókusz a kameramozgás közbeni kiválasztás, majd balance/feloldás.
Külön reprodukálandó a visszazoomkor mért 34,969 px geometria / 6,931 px
proxy eltérés. A felhasználó az átmenetet továbbra sem fogadta el.

**ND-81 átadva próbára (2026-09-11):** az ismételt háttérmunka első
csökkentése a [pontos vetület-cache](reviews/lod-work-cache-nd81-2026-09-11.md).
Azonos kamera és proxy mellett nincs ismételt tile-metrika számítás; a
geomorph is újrahasználja. Offline 40 páros cut/trace ellenőrzés egyezik,
a teljes kiválasztási lánc ideje 27–37%-kal csökkent ebben a próbában.
Ez nem Unity/FPS-mérés. A 12/10 px cél változatlan, nincs élességjavítás.
Élő ellenőrzés következik: `workCache=ND81`, selection/balance, metricHits/
metricComputed, resolveCheck/auxiliaryCopy/tileEmit. A teljes sarok-előkészítés,
coverage-függő geometriaellenőrzés és balance további optimalizálása nyitott;
ezek után lehet biztonságosan visszatérni a kisebb pixelcélra.

**ND-80 élő visszamérés (2026-09-11):** [a friss log](reviews/lod-bounded-chunks-live-2026-09-11.md)
szerint aktív az új csomagolás, maximum 256 levél/chunk. A feltöltés p90
25,44→5,29 ms, maximum 27,77→6,63 ms a két eltérő kameraút próbájában;
nem kontrollált FPS-benchmark. A teljes kérés mediánja továbbra is ~0,54 s,
állókamerás lánc 4–5 s. Következő javasolt prioritás az ismételt cut- és
geometria-előkészítési munka csökkentése, nem az upload további bontása.
A pixelcél és a vizuális minőség nyitott; ebben a körben nincs kódváltozás.

**ND-80, aktuális átadás (2026-09-11):** a felhasználó a megjelenítés
előzetes átalakítását választotta. Az első [korlátos chunk-csomagolás](reviews/lod-bounded-chunks-nd80-2026-09-11.md)
implementált: maximum 256 levél, összevonás 128-nál, változatlan terep-LOD.
Offline 104 állásban azonos tile-fedés; közepes példában 728→215 chunk.
LOD 190/190 Debug/Release, Unity-forrásfordítás 0 hiba. **Élő vizuális és
teljesítmény-ellenőrzés következik**; nincs még elfogadott gyorsulás.
Inkrementális emisszió továbbfejlesztése, upload-időkeret, objektumpool,
kisebb pixelcél és vízfinomítás továbbra is nyitott.

**ND-79, aktuális mérés (2026-09-11):** az ND-78 utáni próba igazolja,
hogy a 10 px-es első és 12 px-es mély cél nem őrzi a távoli ~3 px-es
rács élességét. Kisebb célokra készült [offline költségvizsgálat](reviews/lod-onset-cost-analysis-nd79-2026-09-11.md):
a 6/5 cél közepesen ~33× dinamikus levélszámot és 23 640 apró chunkot
is okoz. Nem aktiváltuk, nincs új vizuális javítás. A következő javasolt
előfeltétel a korlátos méretű hierarchikus chunk-csomagolás és az építési/
upload-költség rendezése; irányválasztást kértünk. Az első finomodás,
a vízréteg és a teljes zoomminőség továbbra is nyitott.

**ND-78 frissítés (2026-09-11):** az új ND-77 log alapján a teljes quad
és a vízszintre emelt proxy méretkülönbsége reprodukált. A nyers mélységre
finomító kísérlet súlyos túlosztása miatt visszavonva. A megvalósított
részjavítás a renderer által később kizárt tengerfeneket már az osztási
keret előtt szűri: a `split-quota` miatti terepkésést csökkenti.
Az 1. lépés teljes minőségi célja továbbra is nyitott, a durva vízréteg
nem változott. [Bizonyítékok és átadás](reviews/lod-early-ocean-exclusion-nd78-2026-09-11.md).

**ND-77 aktuális állapot (2026-09-11):** az ND-76 élő próbában a víz mellett
terepen is marad kiugró méret (88,64 px befejezett finomításnál). Az első
engedélyezett következő lépés célzott azonosító/megállási naplója elkészült:
[részletek és próbamenet](reviews/lod-terrain-decision-trace-nd77-2026-09-11.md).
**A terephiba javítása új logig nyitott**, nem kész felbontásjavítás.
A vízfelszín önálló finomítása és az ismételt kiválasztás optimalizálása
külön következő lépés; most nem változtak.

**ND-76 frissítés (2026-09-11):** az ND-75 élő log igazolta a régi kamerához
készülő 2,5–10,6 s-os kéréseket és az állva is megmaradó nagy tile-okat.
Implementált a [képernyő-quad alapú, adagolt finomítás és emit-cache](reviews/lod-progressive-projected-nd76-2026-09-11.md),
megszakítható elavult kéréssel. Élő elfogadásra vár; az alábbi korábbi
„szünetel / csak diagnosztika” állapotokat ez felülírja. Upload-időkeret,
szigorú mélyterep-bounds, kamerakorlát és km-lépték továbbra is nyitott.

**M9 diagnosztikai frissítés (2026-09-11):** a zoom közben későn és foltosan
finomodó felszínről [külön, mérésekkel alátámasztott diagnózis](reviews/lod-zoom-diagnosis-2026-09-11.md)
készült. A fő új tételek: GPU/CPU modellkülönbségből eredő finomítás-tiltás,
durva/fine felület takarása, abszolút kameramozgás-kapu, budget miatti
fedésvesztés a dinamikus rétegben, chunk-diff és geomorph eltérése.
Az [ND-69 első javítási csomagja](reviews/lod-zoom-fixes-2026-09-11.md)
elkészült: CPU-besorolás, parti sarokvédelem, nézetfrissítés és a budgetnél
megőrzött frontier; 431/431 teszt sikeres. Élő Unity-acceptance még nincs.
Az [ND-70 második csomagja](reviews/lod-zoom-fixes-phase2-2026-09-11.md) is
implementált: teljes base-tile fedéscsere, geometriai közösél-feloldás és
pozícióérzékeny chunk-frissítés. 445/445 .NET-teszt sikeres, az új két Unity
Mesh API-teszt még csak fordított. **Új élő visszajelzés:** a zoom és visszazoom
szépen működik; kb. 13 görgetés után a további élesedés nem érzékelhető.
A lassulás ismert, a felhasználó most kifejezetten halasztja a gyorsítást.
Teljes zoomtartományú látvány/FPS-acceptance, upload-időkeret,
inkrementális emisszió továbbra is nyitott. Az
[ND-71 harmadik csomag](reviews/lod-zoom-fixes-phase3-2026-09-11.md)
**élőben elutasítva:** nem hozott érdemi látványjavulást, a cut 6,7–9,6 s lett.
Az [ND-72 regressziójavítás](reviews/lod-zoom-regression-nd72-2026-09-11.md)
kivette a drága mintavételt az aktív kiválasztásból/morphból; az ND-70
működő fedés- és zoomjavításai megmaradnak. Az új, kérésenként egy pontot
mérő renderdiagnosztika a további élességvizsgálatot szolgálja. **Friss élő
visszajelzés:** valamivel jobb, de későn indul a finomodás. Az
[ND-73 hangolás](reviews/lod-zoom-onset-nd73-2026-09-11.md) csak az első
base-osztást hozza előre és rövidíti a morphot; a mélyebb 12 px cél és a
200 000 budget marad. A próba csúcslevélszáma +25,7%, nem költségmentes;
**élő eredményét a felhasználó elutasította.** A friss log még 0,600 morphot
mutatott, a scene fájl 0,35 értékétől eltérően. Az
[ND-74](reviews/lod-terrain-proxy-nd74-2026-09-11.md) Build-kori radiális
terep-proxyt vezet be közös cut/morph metrikával; implementált, élő próbára
átadva, nem elfogadott. Nincs per-node új Core-minta, de több tile készülhet.
A felhasználó most a hátralévő feladatokat sorban kéri, minden lépés után
saját ellenőrzéssel: (1) terephez igazított LOD — jelen lépés tesztje;
(2) láthatóság/part; (3) részleges emit és upload-időkeret;
(4) felszínkövető kamera; (5) szükség esetén sűrű felületi adat;
(6) teljes tartományú ellenőrzés és külön precíz km-lépték.
Szigorú terrain-error bound továbbra sincs; a radiális proxy csak közelítés.
**ND-75, új diagnosztikai kapu:** a felhasználó ND-74 után közepes zoomnál
megálló finomodást, mélyen elégtelen javulást jelez. Kérésére most csak a
[tényleges kirajzolt mesh pixelméretének naplózása](reviews/lod-drawn-size-log-nd75-2026-09-11.md)
készül, függetlenül a LOD-kérés elkészültétől. A további javítási sorrend
az új próba + log közös értékeléséig szünetel; a beállítások változatlanok.
Az alábbi történeti „működő/kész” megjelölések
nem jelentik a jelenlegi zoomminőség és teljesítmény elfogadását.

Állapotfelmérés: 2026-09-01 (frissítve). Az M0-M5, M7-M8, M10 (deep-time
alap) tartalmilag kész (halasztott al-tételekkel), az M4 lezárva
(ND-36/37/38), az M9 adaptív LOD + M13 Fázis 1 (folytonos árnyalás) most
lett felhasználó által megerősítve működőnek. A tábla a MÉG HÁTRALÉVŐ
tételeket sorolja fel — a kész mérföldkövek nem szerepelnek itt, ld.
`docs/05-milestones.md` a teljes státuszért.

Oszlopok: **Prio** (Magas/Közepes/Alacsony), **Komplexitás** (Kicsi/Közepes/
Nagy — durva becslés, nem munkaóra), **Kellek?** (kell-e felhasználói
vizuális ellenőrzés/döntés a megvalósításhoz: Igen/Részben/Nem).

| Mérföldkő | Megnevezés | Leírás | Prio | Komplexitás | Kellek? |
|---|---|---|---|---|---|
| M9/UI | Precíz, kamerafüggő kilométeres léptékcsík | Felhasználói igény (2026-09-11), **ND-84 szerint kódban implementálva; élő Unity-validáció hátra**: az adott képernyőszélességű csík mellett a neki megfelelő valós felszíni távolság jelenjen meg km-ben (kis léptéknél m-ben). Nem görgetésszám- vagy zoomtáblázat: az aktuális kamera vetítése, viewportja, bolygótranszformációja és a világmodell fizikai sugara alapján kell számolni. Implementáció előtt egyértelmű mérési definíció kell: ajánlott a megjelölt referenciaponton át húzott képernyőszakasz két felszíni végpontja közti referencia-gömbi geodetikus távolság; ez nem az egyenetlen terepen megtett út hossza. A sugarak metszésénél a megjelenített domborzatot/tengert és a vertikális túlrajzolást figyelembe kell venni, a kijelzett fizikai hosszba viszont nem szabad a túlrajzolást vagy a Unity-egységeket kilométerként bekeverni. A csík helye/referenciapontja legyen látható; perspektívában nem állítható, hogy ugyanaz a lépték az egész képernyőre érvényes. Ég/horizont/metszéshiány esetén egyértelműen érvénytelen állapot, nem becsült hamis szám. Rögzített hibakeret és független geometriai regressziók szükségesek: több zoom/FOV/felbontás/képarány, bolygósugár, forgatás, pólus, lapél, part és túlrajzolás; a felirat kerekítése is a hibakeret része. | Közepes | Közepes | Igen (definíció + élő ellenőrzés) |
| M9/M10 | Deep-time újraépítés sebessége — cél <1s | Felhasználói kérés (2026-09-10): a `deepTimeMyr` csúszka (vagy bármi más, ami teljes `Build()`-et vált ki) mozgatása jelenleg ~24 másodperces újraépítést okoz - a cél <1s. **Friss, legacy-fix UTÁNI mérés hét `Logs/PerfLog_*.txt`-ből** (level=5 statikus alap, `adaptiveBaseLevel=8`, hydroLevel=8): `TELJES átlag 22.57s` (21.74-24.74s), ebből **`BuildStaticBaseLayer` átlag 17.52s (77.6%)**, **`hydrology` átlag 4.19s (18.6%)**, minden más együtt átlag ~0.86s. ✅ **1. LÉPÉS MÉRVE (2026-09-11)**: az eldobott legacy geometria kihagyása a korábbi 24.1-24.5s-ról jellemzően 21.7-23.2s-ra vitte a teljes időt. ✅ **2. EXACT LÉPÉS MÉRVE (2026-09-11)**: a coarse folyómunka kihagyása és a normál-középpont újrahasználata után két deep-time rebuild 18.997s és 19.138s (átlag 19.067s), az előző 22.57s átlaghoz képest 15.5% gyorsulás. ✅ **3. EXACT LÉPÉS MÉRVE — ND-63:** a statikus level-8 sarkok cache-e után két deep-time rebuild 12.159s és 11.976s (átlag 12.067s), további 36.7% gyorsulás; a sarokfázis 7.511s-ról 0.439s-ra esett, `terrainBasis reused=True`. ✅ **4. EXACT LÉPÉS MÉRVE — ND-64:** a közös tile-középpont terrain-bázis és az egyszer előállított napi Nap-minták után két rebuild 5.609s és 5.954s (átlag 5.781s), újabb 52.1% gyorsulás; a hydrology field 0.156s-ra, a klasszifikáció 0.485s-ra esett. ✅ **5. EXACT LÉPÉS MÉRVE — ND-65:** két rebuild 5.181s és 5.372s (átlag 5.277s), további 8.7% gyorsulás; a priority-flood 1.249s-ról 0.854s-ra (-31.7%), az emisszió 2.464s-ról 2.361s-ra (-4.2%) esett. ✅ **6. EXACT LÉPÉS MÉRVE — ND-66:** négy meleg rebuild 3.340s / 3.507s / 3.729s / 3.682s, átlag 3.564s; ez az ND-65-höz képest további 32.5%, a 22.570s baseline-hoz képest 84.2% gyorsulás (6.3×). `denseStatic=True`; a statikus emisszió 0.945s-ra, a sarokfázis 0.099s-ra esett. ✅ **7–8. EXACT LÉPÉS MÉRVE — ND-67/68:** öt meleg rebuild 2.656s / 2.790s / 2.607s / 2.935s / 3.185s, átlag 2.835s; ez az ND-66-hoz képest további 20.5%, a 22.570s baseline-hoz képest 87.4% gyorsulás (8.0×). `denseFlood=True`, a topológia minden meleg futásban `reused=True`; a flood 0.748s-ról 0.258s-ra (-65.6%), a statikus base-layer 1.602s-ra, az emit 0.664s-ra esett. A hideg első Build továbbra is 13.029s, főleg a terrain-bázisok első előállítása miatt. **2026-09-11: a felhasználó a jelenlegi eredményt ideiglenesen elfogadta; a `<1 s` cél és az alábbi exact folytatások backlogban maradnak, a munka most másik feladatra vált.** A preview továbbra is csak tartalék, ha az exact út végül nem vihető 1s alá. | Magas | Nagy | Igen (élő Unity PerfLog) |
| M9/M10 | Deep-time exact folytatás — sűrű lake pipeline | Az ND-67 utáni öt meleg futásban a lake-fázis átlag 190.7ms (120.3–341.5ms), a dense flood eredményének Dictionary-konverziója további 9.6ms. Következő exact irány: `IdentifyLakes` tömbindexelt változata, amely közvetlenül a dense field/fill/ocean tömböket és az újrahasznált topológiát olvassa, majd csak a ténylegesen továbbadott tóeredményt materializálja TileId-ként. A Dictionary-út maradjon referencia-orákulum; teljes eredmény-egyezési teszt és élő PerfLog kell. | Közepes | Közepes | Igen (PerfLog) |
| M9/M10 | Deep-time invariant csapadékmező cache | A csapadékfázis stabilan átlag 295.1ms minden deep-time Buildben, miközben a jelenlegi hívás saját t=0 mezőt számol, és a deep-time értéket nem kapja bemenetként. Implementáció előtt tételesen rögzítendő a cache-kulcs és minden invalidáló Inspector-paraméter; csak a bizonyítottan változatlan `MoisturePrecipitation` snapshot használható újra. A dendritikus folyóhálózat nem cache-elhető vele vakon, mert a jelenlegi deep-time lemezmagokat is megkapja. | Közepes | Közepes | Igen (PerfLog) |
| M9/M10 | Statikus mesh direct-array / determinisztikus párhuzamos emit | A meleg statikus base-layer még átlag 1.602s: klasszifikáció 482.6ms, bucket-előkészítés 110.0ms, emit 664.4ms, terrain mesh 145.4ms, víz 106.4ms. Következő nagy exact irány: a klasszifikációval együtt számolt routing/bucket darabszámok és prefix offsetek alapján közvetlen végső tömbírás, stabil face/u/v és submesh-sorrenddel; így a serial listás emit és az utólagos listakonkatenálás megszüntethető. Unity MeshData/job irány csak élő Editor- és látványvalidációval fogadható el. | Közepes | Nagy | Igen (Unity + PerfLog) |
| M9/M10 | Deep-time variancia és allokációprofil | Az öt meleg Build 2.607–3.185s között szórt. Különösen a bucket-előkészítés 13.4–243.2ms, a terrain mesh 97.0–309.2ms, a vízfeltöltés 25.9–283.7ms és a klasszifikáció 418.7–669.3ms között változott. Következő optimalizálás előtt Unity Profiler/GC-allokáció és főszál-idővonal mérés kell, hogy a valódi munka és a GC/JIT/Editor-zaj szétváljon. | Közepes | Kicsi | Igen (Unity Profiler) |
| M9/M10 | Hideg Build terrain-bázis költsége | A friss log első Buildje 13.029s; ebből a statikus sarok terrain-bázis 6.867s, a tile-középpont bázis 2.189s. Vizsgálandó egy algoritmus-/seed-/level-kulcsú, validált lemezcache vagy gyorsabb egyszeri előállítás. A cache nem lehet world-state és eltérő algoritmusverziót nem tölthet be; méret-, I/O- és invalidációs terv szükséges. | Alacsony | Nagy | Igen (hidegindítás-mérés) |
| M5/M9 | Gyors, egész bolygós pillanatnyi hőmérséklet-overlay és dinamikus hőmező (kék=hideg, piros=meleg) | A tétel már szerepelt az `experiment/full-temperature-model` testvérág backlogjában, de csak napi átlagos színnézetként. **2026-09-11: újratervezve és a 29 pontos felhasználói döntéssor lezárva; implementáció nincs.** A kívánt nézethez új, időfüggő Core-mező kell: a pillanatnyi napsütés melegítse a nappali oldalt, a felszín hőtehetetlensége tartsa meg a hőt, a determinisztikus szél pedig a felszínközeli hideg/meleg levegőt szállítsa. Az overlay ennek gyors, teljes bolygós megjelenítése, nem önálló hőképlet. A részletes modell-, idő-, render-, teljesítmény- és tesztterv, valamint a választások tételes jegyzéke a tábla alatti „Pillanatnyi hőmérsékletmező és overlay — kidolgozott terv” szakaszban van. | Közepes | Nagy | Igen |
| M3/M9 | Kameramód: felszíni pontra rögzítve, pálya menti (éves) mozgás követése | Felhasználói kérés (2026-09-09): panel-kapcsoló, ami a kamerát a felszín egy adott pontjához "ragasztja", és a bolygó Nap körüli pályáján haladva is ugyanazt a pontot mutatja — eközben a háttérben a csillagok és a Nap látszólag mozognak (a pálya-pozíció/évszak változása miatt). Jelenlegi alap: a `WorldGen.Core.Astronomy.OrbitalMechanics` már kiszámolja a Nap-irányt a bolygó test-keretében `currentTimeDays` függvényében (`SunController` ezt használja a fényforgatáshoz) - a pálya-pozíció (nem csak az irány) számítása és a kamera erre való rárögzítése ÚJ munka, a `PlanetOrbitCamera`-nak jelenleg semmilyen pálya-/idő-tudatossága nincs. **Nyitott kérdések (tisztázandó implementáció előtt)**: (1) a felszíni "pont" hogyan azonosítható — kattintással kiválasztott TileId/lat-lon, vagy csak az aktuális kameracélpont "befagyasztása"? (2) ebben a módban a tengelyforgás (napi ciklus) is fusson-e egyszerre, vagy a mód kifejezetten csak a pálya-menti (éves) mozgást izolálja (tengelyforgás szüneteltetve)? (3) a jelenlegi renderelés nem valós léptékű (ND-19 floating origin M9-re halasztva) - a pálya menti kameramozgás vizuális "sebessége"/amplitúdója kalibrálandó, nem fizikai lépték. | Alacsony | Közepes | Igen |
| M3/M9 | Kameramód: csillagokhoz/Naphoz rögzítve, tengelyforgás megfigyelése | Felhasználói kérés (2026-09-09): második panel-kapcsoló - a kamera a csillagmezőhöz/Naphoz képest ÁLLÓ (inerciarendszerbeli) pozícióban marad, és a bolygó SAJÁT TENGELYE körüli forgása válik láthatóvá (a csillagok és a Nap a háttérben MOZDULATLANOK). **ARCHITEKTURÁLIS ÜTKÖZÉS a meglévő rendszerrel, ND-döntést igényel a megvalósítás előtt**: a `SunController.cs` jelenleg SZÁNDÉKOSAN és DOKUMENTÁLTAN úgy van felépítve, hogy a `Planet` GameObject `transform.rotation`-ja MINDIG identitáson marad (a mesh sosem forog) - a nap/éj ciklust a Directional Light forgatása szimulálja, a bolygó test-keretében már kiszámolt Nap-irány alapján. Ez a kért módhoz (látszólag álló ég, ténylegesen forgó bolygó) PONT FORDÍTOTT viselkedést igényelne: vagy (a) a Planet mesh-t kell ténylegesen elforgatni a tengelyforgásnak megfelelően, és a fényforgatás-logikát átdolgozni, hogy ezzel konzisztens maradjon (nagyobb refaktor, érinti a dokumentált "identitás-forgás" invariánst), vagy (b) valamilyen alternatív megoldás (pl. a kamera ellentétes irányú forgatása a csillagmezővel együtt mozgatva) - ez utóbbi viszont NEM adná a kért "csillagok állnak" hatást, ha a csillagmező a kamerához van rögzítve, tehát valószínűleg (a) az egyetlen helyes irány. **Közös elfogadási kritérium mindkét módhoz**: a bolygónak MINDIG a ténylegesen a Nap felé néző fele legyen megvilágítva (fizikailag konzisztens terminátor) - ez a jelenlegi szabad kameránál már működik (`SunController` helyesen számolja a Nap-irányt), de itt kifejezetten ellenőrizendő, hogy mindkét ÚJ módban is megmarad-e, különös tekintettel a fenti (a) opcióra, ahol a mesh-forgatás és a fény-logika közötti konzisztenciát újra kell biztosítani. | Alacsony | Nagy | Igen |
| M9 | Felszín-részletesség / nagy, "pixeles" tile-ok + lassú betöltés (ND-46, ND-48) | 2026-09-05, felhasználói kérés: "ez lenne a legnagyobb feladat" — a tile-ok még mindig nagyok, csúnya blokkos hatást keltenek. Diagnózis: a rendszer már agresszíven hangolt (12px cél, 120k budget, level 18 max), a VALÓDI plafon az, hogy MINDEN cut-változáskor a TELJES dinamikus mesh újraépül/feltöltődik (fő-szálú `Mesh.SetVertices`, a teljes lathato reszlettel aranyos koltseg, nem a tenyleges valtozassal). ✅ **KÓDBAN MEGVALÓSÍTVA (ND-48, chunkolt inkrementális mesh-frissítés)**: a dinamikus réteg rögzített méretű "chunk"-okra bomlik, mindegyik saját GameObject/Mesh-sel; kameramozgáskor CSAK a ténylegesen változott chunk-ok épülnek/töltődnek fel újra (`DynamicMeshChunking`, 14 dotnet-teszttel verifikálva a diff-logikára). `useChunkedDynamicMesh` kapcsolóval visszakapcsolható a régi viselkedésre, ha regressziót okozna. HATÓKÖR: csak a terep-mesh chunkolt, a víz/határ-réteg egyelőre globális marad. A `useGpuGeometry` bekapcsolása KIZÁRVA (szinkron GPU-readback stallt okozna) — ld. ND-48 indoklás. **ÉLŐ UNITY MÉRÉS/VIZUÁLIS ELLENŐRZÉS MÉG NINCS** — a projekt saját szabálya szerint ("egy LOD-változás nem nyilvánítható késznek a kiválasztási teszt alapján") ez a következő lépés, utána (ha beválik) az `adaptiveRenderBudget` tovább emelhető lehet. ✅ **MÉRVE ÉS ÁTHANGOLVA (2026-09-18/19)** — ld. `history/2026-09-18-tile-sample-log.md`, `2026-09-19-horizon-cull.md`, `2026-09-19-viewport-render-budget.md`, `2026-09-19-budget-retune-after-cull.md`. A `[tilesample]` napló megmérte a tényleges hibát: a rajzolt tile-ok **1,4-4,1×** nagyobbak a 8 px-es célnál, 68 vágásból 44 pontosan a 8000-es budgetnél telítődött, a kiugró tile-ok **79/95** arányban `stop=leaf-budget` okkal álltak meg. Két gyökérok, mindkettő javítva: (1) a fix 8000-es budget egy LINEÁRIS extrapolációból származott — a valóságban a bejárás költségét a LÁTOTT FELÜLET hajtja, nem a budget, ezért a budget mostantól a viewportból számolódik (`AdaptiveQuadTree.RenderBudgetForViewport`, 1196×710-nél **8000 → 39 807**); (2) a vetített terep-ágon **egyáltalán nem futott horizont-vágás** (korai `return` a `EvaluateNodeForPriority`-ben), ezért a bolygó túlsó felét is végigjártuk — a vágás a metrika-kiértékelést **−75,5%**-kal csökkentette úgy, hogy a látható fedettség 54 nézeten × 21×11 mintán **pontosan változatlan** (0 romlott nézet). Eredmény: ötszörös levélszám a közeli nézetekben a korábbinál OLCSÓBBAN (81-90 ms vs 147 ms), a középtávú legrosszabb eset +32%. Egy téves saját következtetést is visszavontam: a `nadirCutL=8` / `deepestCutL=20` minta NEM prioritás-hiba, hanem az ND-78 óceán-kizárás és a ferde kameraállás helyes viselkedése (tesztben rögzítve). **ÉLŐ ELLENŐRZÉS TOVÁBBRA IS HÁTRA**: az emit/feltöltés levélszámmal skálázódik és Unity-függő; a 2 ms-os ND-85 szelet-korlát elvben leveszi a szaggatás-kockázatot (több staging-frame, nem nagyobb akadás), de ezt Play-menet dönti el. A napló minden `[tilesample]` sorban kiírja a `cutBudget`-et és a `budgetMode`-ot a visszaméréshez. | **Magas** | Nagy | Igen |
| M9/M7 | Folyó- (és tó-) blokkosodás | `IsAdaptiveRiverTile`/`IsAdaptiveLakeTile` a referencia-szinten (level=5/6) dönt, ezért a statikus alapréteg (level=8) szintjén egy folyó/tó-tile teljes leszármazott-blokkja egységesen "folyó"/"tó"-nak számít — nagy, blokkos **kék téglalapok** vékony folyó-vonal / valósághű tópart helyett. 2026-09-05: felhasználó ismét jelezte ("még mindig nagy kék téglalapok a kontinenseken"); a most bekötött tavak UGYANEZT a mechanizmust öröklik. Kísérlet (opció A, 2026-09-05): vonal-hálózat a flow-gráf mentén - ❌ nem oldotta meg (a blokkosodás fennmaradt). ✅✅ **A FOLYÓ-RÉSZ VÁRHATÓAN MEGOLDVA** (ND-49, 2026-09-06): a folyó-vonalak MOST MÁR a dendritikus, finom-szintű (level+4) nyomvonalból rajzolódnak, nem a referencia-szintű vízgyűjtő-fából - ez strukturálisan sokkal finomabb geometriát ad. A TÓ-blokkosodás továbbra is a régi, referencia-szintű mechanizmust használja, arra NEM vonatkozik ez a javítás. **Vizuális ellenőrzés hátra** (Unity) - ha a folyó-vonal még mindig blokkosnak tűnik, az egy ÚJ, önálló diagnózist igényel (nem ugyanaz a gyökérok, mint eddig). | **Magas** | Közepes | Igen |
| M7/M13 | Északi sarki jég-/víz-artefakt | 2026-09-05, felhasználó: az északi pólusnál a jég mélyen "beszakadtnak" látszik, a tenger NEM tölti ki a mélyedést, és ott marad egy folt a RÉGI tengerből, amin megcsillan a fény. Valószínű ok: a pólus-környéki vízfelszín-réteg (`BuildWaterSurface`) és/vagy a jég-geometria nem záródik a pólus/cubed-sphere sarok-varratnál. ✅✅ **MEGOLDVA, felhasználó megerősítette** (2026-09-05): minden óceáni tile (nyílt víz + tengeri jég) tengerszintű, opak felszínt kap (jég = fehér), a gödör-artefakt eltűnt. | Kész | — | — |
| M13 | Vertex-szín árnyalás-regresszió | 2026-09-05, felhasználó: a folytonos, sarkonként interpolált vertex-szín (`CreateVertexColorMaterial`) a többi területen MEGSZÜNTETTE a fény-visszaverődést (spekuláris/csillanás), és a színátmenetek "elég bénák" lettek (lapos/bandinges). ✅✅ **MEGOLDVA, felhasználó megerősítette** (2026-09-05): a `VertexColorUnlit` shader valós idejű Lambert + Blinn-Phong világítást számol (explicit Nap-uniform a C#-ból). | Kész | — | — |
| M13 | Éjszakai oldal — sötétítés, nem homályosítás | 2026-09-05, felhasználói visszajelzés: amikor a felszínt nem éri közvetlen napfény (éjszaka van rajta), a jelenlegi megjelenítés homályossá/ködössé teszi a felszínt, ahelyett hogy ténylegesen sötétítené. Elvárás: a `VertexColorUnlit` valós idejű Lambert+Blinn-Phong láncát úgy módosítani, hogy az éjszakai oldalon a felszín valódi sötét (alacsony/nulla megvilágítású árnyékolás) legyen, ne alfa-/köd-szerű elhomályosítás. ✅ **KÓDBAN MEGVALÓSÍTVA (2026-09-06, önálló munkamenetben)**: gyökérok azonosítva - a `surfaceAmbient` egy SZÁNDÉKOS "padló" volt (alapérték 0.35), ami miatt az éjszakai oldal SOHA nem ment 35% megvilágítás alá (a `diffuse = _Ambient + (1-_Ambient)*ndotl*_SunColor` képletben) - ez adta a "homályos, nem igazán sötét" hatást (a domborzat kontrasztja is elmosódott, mert minden felület egyformán "padló-szintre" világítódott ki árnyékban). Javítás: `surfaceAmbient` alapértéke 0.35→0.04 (`PlanetGridMesh.cs` mező + a `VertexColorUnlit.shader` Properties-fallback konzisztensen), így az éjszakai oldal ténylegesen sötét, csak egy halvány csillag-/légköri fény szintjén marad látható (nem tiszta fekete). A mező TOVÁBBRA IS élőben hangolható Inspector-csúszka (0..1) - ez a DEFAULT-ot javítja, nem veszi el a finomhangolás lehetőségét. Build-ellenőrizve (nincs új build-hiba a szokásos zajszinten felül - tisztán numerikus konstans-változtatás). **Élő Unity-ellenőrzés hátra** - ha 0.04 túl sötétnek/túl világosnak bizonyul, egyszerű csúszka-hangolás. | Közepes | Kicsi | Igen |
| M9/M7 | Folyók: fa-szerű (dendritikus) hálózat, hosszabb folyók, csapadék-forrás + szuperfinom nyomvonal-kiértékelés | ✅✅ **KÓDBAN MEGVALÓSÍTVA (ND-49, 2026-09-06)**: a lokális megközelítés választva. Referencia-szinten csapadékos+hegyvidéki forrás-kiválasztás, majd forrásonként finom szintű (level+4) lejtő-követés lokális pit-escape-pel. Python-referenciával bitre egzakt C#-port. ✅✅✅ **HARMADSZOR ÚJRATERVEZVE (ugyanaznap, felhasználói kérésre: "a minőségi javítás még nincs befejezve, nézd meg alaposan")**: dedikált C# diagnosztikai harness-szel bebizonyosodott, hogy a MÁSODIK verzió (irány-kúpos, stagnálás-alapú) egy referencia-folyón 1804 km utat járt be egy 551 km-es durva lánchoz képest, és MINDEN mért beállításnál a lépésszám-korlátba futott, egy rejtett ~150 km-es "safety net" teleporttal a torkolatra - SOHA nem ért célba természetesen. Felhasználói irányelv: nincs befagyasztott lépésszám-korlát, a folyó kövesse a lejtőt a TERMÉSZETES végállapotig (óceán/zárt medence). Új algoritmus (`TraceRiverPathContinuous`): a durva `TraceRiverPath`/`FindLocalSpillway` mintáját követi (szigorú lejtő-csökkenés + lokális priority-flood escape), de folytonos térben, forrástól a természetes Ocean/Pit/Merged végállapotig. Mind a 12 referencia-forrás helyesen konvergál, ~6.4s alatt összesen. Regresszió-védő teszt (`ReachesNaturalTerminationNotMaxSteps`, mind a 12 forrásra) zárja ki a MaxSteps-be futást. 359/359 Core-teszt zöld. Ld. ND-49 3. kiegészítés a teljes méréssorozatért. ✅✅✅✅ **NEGYEDIK JAVÍTÁS (2026-09-06, önálló munkamenetben, felhasználói jelenlét nélkül talált gyökérok)**: egy független vizsgálat feltárta, hogy a Core-oldali algoritmus HELYES volta ELLENÉRE a finomítás a GYAKORLATBAN valószínűleg SOHA nem futott le teljesen az élő Unity-nézetben - a `WorldConfigChangedSinceBuild()` a felhő-réteg TISZTÁN VIZUÁLIS paramétereit (showClouds, magasság, átlátszatlanság, küszöb/gamma) is figyelte, és BÁRMELYIK változása TELJES `Build()`-et indított, ami eldobta a háttérszálon futó, több másodperces folyó-finomítást (új generáció-szám). Ha valaki bármelyik csúszkát (akár a felhő-kalibrációt) folyamatosan mozgatta, a finomítás sosem tudott lefutni - mindig a régi, durva fallback-vonal látszott. JAVÍTVA: a felhő-paraméterek KÜLÖN figyelve (`CloudConfigChangedSinceBuild`/`ApplyCloudOnlyRebuild`), a MÁR kiszámolt, cache-elt csapadék-mezőből (`_lastPrecipField`) csak a felhő-réteget építve újra, a többi réteget (és a folyó-finomítási generációt) érintetlenül hagyva. ✅✅✅✅✅ **ÖTÖDIK JAVÍTÁS (ugyanaznap, önálló 8-szempontú code-review, felhasználói jelenlét nélkül)**: a review 5 további, kóddal/méréssel megerősített hibát tárt fel a Core-algoritmusban, mind javítva: (1) hurok-védelem HIÁNYZOTT a folytonos pit-escape keresésből (a durva `FindLocalSpillway` `pathVisited`-je nem lett átörökítve) - pótolva egy finom (level 20, ~14 m/tile) racsra képezett `pathVisited` halmazzal, teljesítmény-hatás nélkül (12 folyóra 5.86s, változatlan); (2) `sensingRadius>=stepMeters` biztonsági clamp HIÁNYZOTT (a törölt korábbi verzió `Math.Max`-ot használt, ez volt a kulcs a ~170°-os lengés-hiba korábbi javításához) - visszapótolva; (3) `MaxSteps` (lépésszám-biztonsági korlát) néma elnyelése - most `Debug.LogWarning` jelzi; (4) a NEGYEDIK javítás (fent) MAGA is hiányos volt: `riverLineRadialBias` MÉG MINDIG teljes `Build()`-et indított, eldobva a finomítást - leválasztva, saját olcsó rebuild-ággal; (5) egy saját, menet közbeni regressziót (kettős fékezés egy debounce-fixben) is elkapott és javított, mielőtt élesbe került volna. 363/363 Core-teszt zöld. ✅ **ÉLŐ UNITY-VISSZAJELZÉS, VÉGRE (2026-09-06): "a folyók már egészen jók"** - az ötödik javítási kör alapvetően működik. KÉT ÚJ, FINOMÍTÁSI IGÉNYŰ MEGJEGYZÉS: (a) a folyó-vonal SZÉLESSÉGE nem korrelál azzal, mennyi vizet szállít (jelenleg a `BuildRiverNetwork` egységes vastagságú `MeshTopology.Lines`-t rajzol - a szélességet a flow-accumulation/vízgyűjtő-méretből kellene levezetni, ami egy MESH-alapú, változó szélességű "szalag" rajzolást igényelne vonal-topológia helyett, nem dekoratív, hanem adatból vezetett, tehát I3-kompatibilis); (b) az útvonal a felületen HELYENKÉNT MEGSZAKADNAK tűnik - valószínű okok (nem diagnosztizálva): a dendritikus összefolyásoknál (merge) a vonal-szegmensek nem kapcsolódnak folytonosan, VAGY a radiális bias/Z-fighting miatt egyes szakaszok belesüllyednek a terepbe és emiatt tűnnek hiányzónak (nem feltétlenül valódi geometriai hiány), VAGY egy mesh-index hiba az `AddQuad`/vertex-hozzáadásban. Mindkettő ÚJ, önálló diagnózist igényel. ✅ **AZ ÚTVONAL-MEGSZAKADÁS DIAGNOSZTIZÁLVA ÉS JAVÍTVA (2026-09-06, önálló munkamenetben, ND-49 hatodik kiegészítés)**: gyökérok - a dendritikus összefolyásnál egy megálló ("Merged") folyó utolsó pontja csak "ugyanabban a durva finom-tile-ban" volt, mint a befogadó folyó, NEM annak tényleges pontján - a tile mérete (akár több száz méter) pont akkora vizuális rést hagyott, amennyit a felhasználó észlelt. Javítva: a `claimed` térkép mostantól a befogadó folyó TÉNYLEGES pozícióját is tárolja (`ClaimedTileInfo`), és az összefolyásnál ezt a pontos pozíciót zárja a megálló folyó útvonalába - a két vonal PONTOSAN összeér. Új, egzakt-egyezést ellenőrző teszt-assertion (1e-12 tolerancia) bizonyítja a javítást. 363/363 Core-teszt zöld, Viewer build-ellenőrizve (nincs új hiba). A MÁSIK finomítási igény (vonal-szélesség ~ vízhozam) TOVÁBBRA IS nyitott, külön munka. ⚠️ **FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06, ugyanaznap): a merge-pont-javítás UTÁN is szaggatottak a folyók** - a felhasználó szerint nem a több-folyós összefolyás az ok, hanem "kirajzolási probléma". Újra megvizsgálva: valószínűbb (és a felhasználói megérzéssel egyező) gyökérok azonosítva - a folyó-vonal minden pontja a FOLYTONOS (finom, ~50 m lépésközű) elevációmezőt olvassa ki, de a TÉNYLEGESEN renderelt terep egy darabosan lineáris (két háromszögből álló) mesh, aminek csúcsai a sokkal durvább adaptív LOD-tile-határokon vannak - a `CrustElevation.NoiseAmplitudeMeters` (3000 m) domborzat-zaj mellett ez a két felület könnyen eltérhet a korábbi `riverLineRadialBias` (0.02 Unity-egység, `elevationScale=0.01` mellett mindössze ~2 méteres) sugár-eltolásnál nagyobb mértékben, ezért a folyó-vonal helyenként a terep-mesh ALÁ süllyed (Z-fighting/takarás) - ez szaggatottnak LÁTSZIK, pedig a nyomvonal-ADAT (a korábbi javítás óta) folytonos. ✅ **RÉSZLEGES JAVÍTÁS**: `riverLineRadialBias` 0.02→0.5-re emelve (~50 m ekvivalens) - ez a leggyakoribb esetben elegendő, de a LEGDURVÁBB LOD-szinteken (távoli kamera) még mindig előfordulhat, mert a bias FIX érték, nem igazodik a helyi mesh-durvasághoz. **A TELJES megoldás** (nem implementálva, külön munka): a folyó-pont sugarát a HELYI terep-mesh TÉNYLEGES (nem a folytonos mezőből számolt) magasságára kellene vetíteni. Build-ellenőrizve (nincs új hibaosztály). **Élő Unity-ellenőrzés hátra.** ✅ **A VONAL-SZÉLESSÉG ~ VÍZHOZAM TÉTEL IS MEGVALÓSÍTVA (2026-09-06, önálló munkamenetben, felhasználó kérésére: "folytasd a következő feladattal... válaszd ki te és kódolj")**: Core - `RiverPathTracing.ContinuousRiverPath` új `MergedIntoRiverIndex` mezővel (melyik folyóba olvadt bele, -1 ha nem), és egy új `ComputeDischargeWeights` függvénnyel, ami a dendritikus összefolyás-fából (nem becslésből, tehát I3-kompatibilis) egy "vízhozam-súlyt" számol folyónként: egy forrás önmagában 1, egy összefolyásnál a beleolvadó folyó teljes súlya hozzáadódik a befogadóéhoz (a fordított bejárás garantálja a többszintű láncok helyes összegzését). Viewer - a `BuildRiverNetwork`/`BuildRivers` a korábbi vékony `MeshTopology.Lines` helyett egy `MeshTopology.Triangles`-alapú, változó szélességű mesh-SZALAGOT épít (`AddRiverRibbon`, középső-differenciás érintő + helyi felszín-normális alapú "oldal"-vektor, hogy a szegmensek folytonosan illeszkedjenek), a fél-szélesség `riverBaseHalfWidth * sqrt(vízhozam-súly)` (a valós hidrológiában is megfigyelt szélesség~vízhozam^0.5 durva közelítése). A durva (nem-finomított) fallback-vonal egységes (súly=1) szélességet kap, mert ott nincs összefolyás-fa adat. 3 új Core-teszt (lánc-összefolyás, nincs-összefolyás, több-mellékfolyó-egy-főágba) + 366/366 Core-teszt zöld összesen. Build-ellenőrizve (egyetlen új, elfogadott mintájú nullable-figyelmeztetés, nincs új hibaosztály). **KALIBRÁLATLAN, élő Unity-ellenőrzés hátra**: `riverBaseHalfWidth=0.08` becsült érték. | **Magas** | Nagy | Igen (csak megjelenítés-ellenőrzés) |
| M7/M13 | Pólusi jég — klímamodell-vezérelt zajos partvonal | 2026-09-05, felhasználói visszajelzés: a pólusok körüli jég jelenleg fixen, egyenletesen ("túl tisztán") van megrajzolva. Elvárás: a jégsapka kiterjedését és a partvonal szabálytalanságát/zaját a klímamodellből (hőmérséklet-mező, ld. `Temperature`/`LakesIceErosion.ClassifyIce`) kellene levezetni, ne egy fix geometriai/vizuális sablon adja — csak ott legyen jég (a pólusokon is), ahol a klímamodell szerint ténylegesen fagypont alatti/jeges a terület. ✅ **KÓDBAN MEGVALÓSÍTVA (2026-09-06, önálló munkamenetben)**: gyökérok-diagnózis megerősítette, hogy ez UGYANAZ a hibaosztály, mint a folyó-/tó-blokkosodás (ND-49) — az éves hőmérséklet-alapú jég-klasszifikáció (`AnnualTemperatureStats`+`ClassifyIce`, drága, 12 mintavétel/tile) CSAK a referencia-szinten fut, minden finomabb leaf tile az ős referencia-tile AZONOS booleanjét örökölte, ezért a partvonal blokkos/mesterségesen szabályos volt, nem a klímamező hiányzott mögüle. Javítás: a referencia-szintű `meanK`-t (olcsó dictionary-lookup, NINCS újra teljes `Temperature`-lánc leaf-enként) egy leaf-pozíciófüggő, térben koherens zajjal (`FractalNoise.Fbm` — MÁR verifikált primitív, a `DomainWarp`/ND-36 mintája szerint újrafelhasználva, nincs új hash-függvény/`RandomProperty`) perturbáljuk a küszöb-összevetés előtt (`PlanetGridMesh.IsAdaptiveIceTile`). I3-kompatibilis: a zaj maga is a worldSeedből determinisztikusan generált mező. A Core réteg (`LakesIceErosion`, `Temperature`) VÁLTOZATLAN — tisztán Viewer-oldali render-logika, 363/363 Core-teszt zöld. **KALIBRÁLATLAN, élő Unity-ellenőrzés hátra**: `IceBoundaryJitterAmplitudeK=4.0`, `IceBoundaryJitterFrequency=24.0`, `IceBoundaryJitterOctaves=3` (a `PlanetGridMesh` konstansai) becsült, nem mért értékek — ha a partvonal még mindig túl sima vagy túl kaotikus, ezek a hangolandó paraméterek. ✅ **ND-57 (2026-09-09)**: a tengeri jég (`SeaIce`/`Ocean`) határa a Core-ban zaj nélküli, tiszta hőküszöb volt (ellentétben a szárazfölddel) - jitter hozzáadva (`IsAdaptiveSeaIce`), csak render-kategória szinten. ❌ **"ez továbbra se oldotta meg"** - a valódi, ELSŐDLEGES probléma más volt: a `SeaIce` a `River`/`Crater`-hez hasonlóan a régi, lapos HDRP/Lit anyagot használta (nem `VertexColorUnlit`-et), ami (1) nem követte a `surfaceAmbient`-et (éjszaka is világos maradt), (2) quadonként egységes színt adott (éles, sokszögletes határ, nem a küszöb-szabálytalanság hiánya volt a fő látvány). ✅ **ND-58 (2026-09-09)**: `SeaIce` felvéve a folytonos kategóriák közé (`Ocean`-nal azonos módon) - most már ambient-helyesen sötétedik és sarkonként interpolált, sima színátmenetet kap. Ld. `docs/04-decisions.md` ND-57/ND-58. **Élő Unity-ellenőrzés hátra.** | Közepes | Közepes | Igen |
| M9 | Kontinens/régió kamera-átmenetek | Zoom közben folyamatos átmenet Planet → Continent → Region nézetek között. Az orbit/zoom kamera-alap + adaptív LOD kész, de a nézetszint-váltás/fly-to logika nincs. ✅ **NÉZETSZINT-VÁLTÁS MVP KÓDBAN MEGVALÓSÍTVA (2026-09-06, önálló munkamenetben)**: a `PlanetOrbitCamera` új `CurrentViewLevel` (Planet/Continent/Region, tisztán a felszín-feletti magasságból, két kalibrálatlan küszöbbel) + `CurrentViewDirection`/`AltitudeAboveSurface` publikus property-je; a `WorldGenPanelUI` minden képkockán megjeleníti (egy opcionális `viewLevelText` TMP mezőn) az aktuális nézetszintet + a kamera nézet-irányához LEGKÖZELEBBI kontinens/régió nevét (dot-szorzat a `CenterDirection`-ök ellen). **HATÓKÖR-SZŰKÍTÉS, TUDATOSAN**: ez a "folyamatos átmenet" célnak a LEGKISEBB, élő Unity-teszt nélkül biztonságosan megvalósítható szelete - a kamera FORGÁSI KÖZÉPPONTJÁNAK tényleges áthelyezése a kontinens/régió felszíni pontjára (hogy zoom közben valóban "bele lehessen repülni" egy régióba, ne csak a teljes bolygó körül forogva közelebb menni hozzá) egy jóval nagyobb, a `PlanetOrbitCamera` forgás/zoom-matematikáját mélyen érintő átalakítás lenne - KÜLÖN, később elvégzendő munka. Build-ellenőrizve (csak már ismert, elfogadott nullable-figyelmeztetés-kategóriák, nincs új hibaosztály). **Új Unity-Editor lépést igényel** (egy `TMP_Text` UI-elem létrehozása és a `viewLevelText` mezőbe kötése) - ld. docs/06-user-verification-checklist.md. ⚠️ **ÉLŐ VISSZAJELZÉS (2026-09-07): "nok, nem ír ki semmit, pedig felvettem a komponenst és beállítottam"** - a néma early-return (`viewLevelText`/`orbitCamera`/`_lastData` null-ellenőrzés) eddig semmilyen diagnosztikát nem adott. Egyszeri (nem per-frame) `Debug.Log` hozzáadva `UpdateViewLevelDisplay`-hez, ami pontosan megmondja, melyik feltétel `null` - a felhasználó következő Console-visszajelzése kell a valódi gyökérokhoz. ✅ **VALÓDI GYÖKÉROK MEGTALÁLVA ÉS JAVÍTVA (2026-09-07, három diagnosztikai kör után)**: a naplózott adatok bizonyították, hogy a szöveg helyesen íródik be (aktív, teljesen látható fehér szín, ésszerű méret), és a felhasználó megerősítette, hogy a RectTransform kerete a látható vásznon BELÜL van - tehát Z-SORRENDI TAKARÁS volt az ok (egy másik, opak panel-háttér a Canvas-hierarchiában KÉSŐBBI testvérként eltakarta). Javítva: `OnEnable()` mostantól `viewLevelText.transform.SetAsLastSibling()`-et hív, ami mindig a látható rétegre kényszeríti. Build-ellenőrizve (78 hiba, baseline). ⚠️ **NEGYEDIK KÖR (2026-09-07): a SetAsLastSibling() SEM segített** - "nem látszik". Áthelyezve `OnEnable()`-ből az `Update()`-ből hívott `UpdateViewLevelDisplay()`-be (hot-reload-biztos), plusz bővített diagnosztika: `RectMask2D`/`Mask`/`CanvasGroup` szülők keresése (ezek maszkolnák/rejtenék el a szöveget, anélkül hogy a GameObject saját aktív állapota jelezné). ⚠️ **ÖTÖDIK KÖR (2026-09-07): a bővített napló szerint MINDEN rendben** (legfelső testvér, nincs maszk/CanvasGroup), mégis "egyszerűen nem látszik". Két új diagnosztikai irány: (a) font/anyag/shader NÉV-összehasonlítás egy MŰKÖDŐ panellel (`worldPanelText`) - HDRP-shader-kompatibilitási hibára gyanakodva; (b) a jelenet ÖSSZES Canvas-ának felsorolása (renderMode/sortingOrder/worldCamera) - hátha egy másik, magasabb sorting-order-ű Canvas fedi le ugyanazt a területet. ✅ **VALÓSZÍNŰLEG A VALÓDI OK MEGTALÁLVA (2026-09-07, 6. kör)**: font/anyag/shader/Canvas mind rendben - de a naplóban `fontSize=36` vs `rect height=30` (a betűméret nagyobb, mint a doboz!) - a TMP saját, beépített (nem külön-komponenses) függőleges túlcsordulás-vágása ezt teljesen láthatatlanná tehette. Javítva: kódból kényszerítve az `Overflow` (nem vágott) mód, függetlenül a RectTransform méretétől. ✅✅ **MEGOLDVA, felhasználó megerősítette (2026-09-07): "most jó"** - a felhasználó a `worldPanelText`-et (a helyesen méretezett RectTransformjával) másolta le és kötötte be `viewLevelText`-ként, ami megerősíti a diagnózist (az eredeti, frissen létrehozott szöveg-doboz `fontSize`-nál kisebb magasságú volt). 6 diagnosztikai kör után megtalálva - a kódos `Overflow`-védelem is bent maradt extra biztonságként. | Közepes | Nagy | Nem |
| M8/M9 | Kattintható panel-nevek → kamera-ugrás | Felhasználói kérés (2026-09-06): "a canvason megjelenített infó (kontinensek/régiók nevére) kattintva odaugrik a kamera?" - ez a fenti tétel EGYSZERŰBB, konkrétabb szelete (nem teljes nézetszint-váltás, csak fókusz-ugrás ugyanazon a nézeten belül). ✅ **KÓDBAN MEGVALÓSÍTVA (ugyanaznap)**: (1) `ContinentPanelData`/`RegionPanelData` bővítve egy `CenterDirection` mezővel (a tile-halmaz egységvektor-átlaga, `ComputePanelData()`-ban számolva); (2) `WorldGenPanelUI` a nevet TMP `<link>` tag-be csomagolja (aláhúzva/kiemelve), és `TMP_TextUtilities.FindIntersectingLink`-kel azonosítja a kattintott linket - NEM kellett Button-onkénti UI-átalakítás; (3) `PlanetOrbitCamera.FlyToDirection` egy animált (smoothstep, kb. 1s) átmenetet indít a célirány felé, `LerpAngle`-lel a legrövidebb köríven. **KOCKÁZATI PONT, MÉG NEM UNITY-BEN TESZTELT**: a pitch/yaw inverz képlet (irányvektorból kamera-szögek) egy LEVEZETETT, a `Quaternion.Euler(pitch,yaw,0)*Vector3.back` konvencióra épülő formula - egy Unity-független matematikai önkonzisztencia-teszttel (2000 véletlen szög-pár oda-vissza) ellenőrizve (hibamentes), DE ez NEM bizonyítja a tényleges Unity-viselkedést; ha a kamera-ugrás iránya tükrözöttnek/fordítottnak tűnik, egy előjelváltás (pitch vagy yaw negálása) a valószínű javítás. ✅ **HIBAJAVÍTÁS (2026-09-06, ugyanaznap)**: felhasználói jelzés — "megjelentek a linkek, kékek, de kattintásra nem reagál, console üzenet nincs". Gyökérok: az `orbitCamera` mező a scene-ben üresen maradt, és a `WorldGenPanelUI.OnEnable()`-beli `FindObjectOfType` fallback Play közbeni szkript-hot-reload után nem futott le újra (ugyanaz a jelenség, mint amit a `PlanetGridMesh`-nél "ÖNGYÓGYÍTÁS" kommenttel már korábban dokumentáltunk) — az `Update()` ezért csendben, log nélkül kilépett minden kattintásnál. Javítva: az `orbitCamera`-keresés mostantól minden `Update()`-ben megismétlődik, ha még null; kattintáskor explicit `Debug.LogWarning`/`Debug.Log` jelzi, ha `_lastData`/`orbitCamera` hiányzik, vagy ha nem talál linket a kattintás pozíciójában — a pitch/yaw irány-kockázat továbbra is ÉLŐ UNITY-ELLENŐRZÉST igényel. ✅ **HANGOLÁS (2026-09-06, ugyanaznap)**: felhasználói visszajelzés — "jobb lenne ha... inkább egy gomb lenne, mert ha nem a karakterre kattintok, nem történik semmi" (a `FindIntersectingLink` a glyph-tinta pixel-pontos négyszögén belülre szorította a találatot). VALÓDI Unity `Button`-ra váltás NEM lehetséges kockázatmentesen: a scene `EventSystem` GameObject-je `m_IsActive: 0` (kikapcsolva), egyik Canvas-on sincs `GraphicRaycaster` - ezek élő bekapcsolása Unity-teszt nélkül kockázatos lenne (az orbit-kamera drag-kezelése is `Input.GetMouseButton(0)`-ra épül). Ehelyett a kattintás-detektálás megtartotta a bevált utat, de a link bounding boxát kibővítettem: vízszintesen a glyph-szélesség+8px, függőlegesen a SOR ascender/descender-je (nem a glyph-tinta, hogy egy leszáró szár nélküli név, pl. "MOUNTAINS", ne kapjon alacsonyabb kattint-magasságot)+6px padding — gyakorlatilag Button-szerű, megbocsátó kattint-terület, scene-infrastruktúra érintése nélkül. Build-ellenőrizve (126 hiba, baseline). Élő ellenőrzés hátra, a padding (`LinkClickPaddingX/Y`) egyszerű konstans-hangolás, ha még mindig szűk. ✅ **MÁSODIK HANGOLÁSI KÖR (2026-09-06)**: felhasználói visszajelzés — "a link nevek továbbra se jól kattinthatók, megküzdök hogy adott régióhoz kattintással eljussak" (a név-glyphek köré paddelt terület még mindig túl szűk volt). Javítva: mivel egy kontinens/régió gyakorlatilag mindig EGYETLEN sorra fér, a vízszintes kattint-teszt a SZÖVEG TELJES SZÉLESSÉGÉRE bővült (nem csak a név karaktereire) - a sor BÁRMELY pontjára kattintva (a statisztika-szövegre is) aktiválja a linket, függőlegesen továbbra is a sor ascender/descender+padding. Build-ellenőrizve, nincs új hiba. Élő ellenőrzés hátra. ✅✅✅ **VALÓDI GYÖKÉROK MEGTALÁLVA ÉS JAVÍTVA (2026-09-06, harmadik "full nem működik" visszajelzés)**: diagnosztikai naplózással kiderült, hogy a hitbox-detektálás VALÓJÁBAN MINDIG helyesen működött (a napló `contains=True`-t mutatott minden alkalommal) - a hiba egy TELJESEN MÁS helyen volt: a `PlanetOrbitCamera.Update()`-ben a "vedd át az irányítást egy folyamatban lévő FlyTo-animációtól" logika `Input.GetMouseButton(0)`-t (a gomb LENYOMVA VAN, nem csak az első frame-ben) ellenőrizte önmagában - ez UGYANARRA a kattintásra IS igaz volt, ami a `FlyToDirection`-t elindította (amíg a felhasználó fizikailag nyomva tartja az egérgombot, ami egy kattintás során több frame-en át is tart), ezért a frissen elindított animáció MÁR A KÖVETKEZŐ képkockán megszakadt, mielőtt a kamera akár egyetlen frame-nyit is mozdulhatott volna - innen a "kattintás regisztrálódik, de semmi nem történik" élmény. Javítva: a megszakítás mostantól TÉNYLEGES egérhúzáshoz (dx/dy ≠ 0) van kötve, nem pusztán ahhoz hogy a gomb lenyomva van-e - egy álló kattintás, ami épp a FlyTo-t indította, többé nem szakítja meg saját magát. Build-ellenőrizve (156 hiba, csak sor-eltolódásos, már ismert mintájú figyelmeztetések), 369/369 Core-teszt zöld (Core-t nem érintette). A pitch/yaw irány-konvenció továbbra sincs élőben leellenőrizve - ez a KÖVETKEZŐ élő teszt tétje. ⚠️✅ **NEGYEDIK GYÖKÉROK, CODE REVIEW-BAN FELTÁRVA ÉS JAVÍTVA (2026-09-07)**: a kattintás-detektálás az ELSŐ egyező linket választotta, nem a legközelebbit - a padded sorsávok szomszédos kontinens/régió-sorok között (mért font-metrikákkal, 10pt LiberationSans SDF) kb. KÉTSZERESEN átfednek egymással (a sáv magassága ~23.2 egység a ~11.5 egységes sormagassághoz képest), tehát egy sor-2-höz szánt kattintás gyakran sor-1-et talált el elsőként - ez valószínűleg a session egész hosszában visszatérő "rossz régióhoz repül" panaszok egyik VALÓDI, eddig fel nem tárt oka volt (a korábbi 3 kör mindegyike a hitbox MÉRETÉT nagyobbította, nem a TÖBB-EGYEZÉS-KÖZÜLI-VÁLASZTÁS logikáján javított). Javítva: az összes egyező linket összegyűjtve, a klikk Y-koordinátájához (nem a dokumentum-sorrendhez) LEGKÖZELEBBI sor közepét választja. 375/375 Core-teszt zöld (Core-t nem érintette), Unity build-ellenőrizve (78 hiba, baseline). **Élő Unity-ellenőrzés hátra** - ez most már a NEGYEDIK, remélhetőleg végleges javítási kör. ❌ **A FUNKCIÓ MEGSZŰNT (2026-09-13, felhasználói kérés)**: a kattintható Canvas/TMP kontinens-/régió-lista (és a `TryGetClickedLinkIndex`/`FlyToRegion` kód) törölve - a navigációt az új navigációs menü (breadcrumb + lista + "Vissza", ld. lentebb az M8/M9/UI sort) váltotta ki. Ez a sor innentől TÖRTÉNETI napló, nem élő funkció-leírás; ld. `history/2026-09-13-legacy-panel-removal.md`. | Közepes | Közepes | Igen |
| M13 | Fázis 2-4: GPU-vezérelt geometria, mikro-részlet textúra | A 4 fázisú "fotorealisztikus" terv 1. fázisa (folytonos árnyalás) KÉSZ és megerősítve. 2. fázis (a meglévő GPU compute pipeline kiterjesztése a teljes kiértékelésre) és 3-4. fázis (GPU-vezérelt mesh, procedurális mikro-textúra) nincs elkezdve. | Alacsony | Nagy | Igen |
| M6 | Atmoszféra-render | Rayleigh-szórás, ciklonok — HDRP-prototípus (ND-21) még el sem kezdődött. ✅ **Felhő-réteg MVP KÓDBAN MEGVALÓSÍTVA** (2026-09-06): `showClouds` kapcsoló, a felhő-sűrűséget a MÁR meglévő `MoisturePrecipitation` csapadék-mezőből vezeti le (I3 - nincs kézzel festett textúra), egy második, átlátszó gömbhéjként (`WorldGen/CloudUnlit` egyedi shader, a `VertexColorUnlit` mintájára). ✅✅ **KALIBRÁCIÓ JAVÍTVA, felhasználói visszajelzés alapján, ugyanaznap**: az ELSŐ verzió "katasztrófa... lehangoló" volt (blokkos tile-határok, hatalmas blokk-felhők) - javítva sarok-interpolált sűrűséggel (mint a terep/víz szín, `PrecipAndOceanFractionAtCorner`). MÁSODIK visszajelzés: "elsősorban óceánok felett van, kontinenseket elkerüli... óceánokat teljesen befedi... köd-szerű". MÉRVE: a nyers csapadék-skála óceán fölött ~4× a szárazföldinek (átlag 4.67 vs 1.27) - javítva domain-relatív percentilis-küszöbbel (külön az óceáni/szárazföldi eloszláson, part menti lineáris átmenettel), és a magasság csökkentve (8000→3000 m). ✅✅✅ **HARMADIK JAVÍTÁS (ugyanaznap, felhasználói visszajelzés: "egyetlen egy függőleges vonalban... a rendszer eleje és vége nem match-el... továbbra is túl magasan van, a szárazföld felett alig van felhő")**: a varrat gyökéroka a MÁSODIK javítás sarok-átlagolása volt - a lap-határon nyers index-aritmetikával kereste a szomszédokat. MÉRVE: 192/6144 lap-szélen lévő tile-ból 48-nál (25%) >0.5, egy helyen 12.7-es eltérés a hibás/helyes átlag között. Javítva `TileNeighbors.Neighbor`-ral (majd a MARADÉK, 4. átlós szomszédre egy `DiagonalNeighbor` kísérlet IS hibásnak bizonyult a kocka éle/csúcsa közelében - mérve: 768/24576 oda-vissza teszt sikertelen -, végül lap-határ-átlépésnél egyszerűen kihagyva a bizonytalan 4. mintát). A szárazföld-hiányra: a küszöböt (nem a sűrűséget) interpoláltam óceán-arány szerint, ami a part menti szárazföldet (ahol a legtöbb szárazföld van) az óceáni küszöb felé tolta - javítva, most a kész sűrűséget interpolálom. Magasság tovább csökkentve (3000→1500 m), opacity/küszöb/gamma visszahangolva. NEM a teljes volumetrikus HDRP-modell, egyszerű MVP-közelítés. **Vizuális ellenőrzés MÉG MINDIG hátra** (Unity) - ez már a HARMADIK kalibrációs kör, mind csak számítással/méréssel ellenőrzött. | Közepes | Nagy | Igen |
| M6/M13 | Felhő-mozgás | Felhasználói visszajelzés (2026-09-06): a felhőréteg jelenleg TELJESEN STATIKUS - csak akkor frissül, ha egy `Build()` lefut (pl. deep-time csúszka mozgatásakor), nincs semmilyen folyamatos animáció, ezért "olyan mintha nem mozogna". Megoldás-jelölt (I3-kompatibilis, NEM dekoratív): a MÁR meglévő szél-mezőből (`WindPrecipitation.WindVector`) vezérelt, idővel eltolt UV/pozíció-animáció - ez nagyobb tervezést igényel (a jelenlegi felhő-mesh csak Build()-kor épül, egy folyamatos animációhoz vagy Update()-ben futó UV-eltolás, vagy periodikus, olcsó pozíció-frissítés kell). Nem egyszerű kalibrációs hangolás, külön tervezést igényel. ✅ **KÓDBAN MEGVALÓSÍTVA (2026-09-06, önálló munkamenetben)**: a felhő-mesh vertex-színbe (nem textúrába) sütött sűrűséget használja, tehát egy hagyományos UV-textúra-csúsztatás NEM alkalmazható rá. Helyette felfedeztem, hogy a Core-ban MÁR LÉTEZIK egy pontosan erre a célra épített, eddig sehonnan nem hívott mechanizmus: `WindPrecipitation.WeatherPrecipitationMultiplier(worldSeed,x,y,z,t)` — egy idő-koherens (a `DomainWarp`/`FractalNoise`-ra épülő, tehát a projekt más zaj-felhasználásaival AZONOS, verifikált primitíveket használó), a klimatológiai csapadék-átlagot egy `t` idő-paraméterrel súlyozó szorzó (§32 "időjárás-zaj"). A `PlanetGridMesh.BuildClouds` most minden SAROK saját (face,u,v)-pozíciójában megszorozza a nyers csapadékot ezzel a szorzóval, MIELŐTT a küszöb/gamma-alakítás lefutna, és egy ÚJ, saját fékezésű `Update()`-ág (`cloudDriftEnabled`/`cloudDriftTimeScale`/`cloudDriftRebuildIntervalSeconds`) periodikusan (alapból 1.5s-enként) újraépíti CSAK a felhő-réteget a MÁR meglévő `ApplyCloudOnlyRebuild`/`_lastPrecipField` cache-en keresztül (nincs teljes `Build()`, nincs a háttérszálon futó folyó-finomítás eldobása). I3-kompatibilis: a mozgás forrása egy valódi, seedből generált, idővel koherensen sodródó mező, nem dekoratív UV-trükk. A Core réteg VÁLTOZATLAN (csak egy eddig nem hívott, már létező függvényt kötöttem be), 363/363 Core-teszt zöld, build-ellenőrizve (126 hiba, megegyezik a baseline-nal). **KALIBRÁLATLAN, élő Unity-ellenőrzés hátra**: `cloudDriftTimeScale=3.0` becsült érték — ha a sodródás túl gyors/lassú vagy túl kaotikus (a `WeatherWarpFrequency`/`WeatherNoiseFrequency` konstansok a Core-ban kontinens-léptékűre vannak hangolva §32-höz, nem felhő-léptékre), ez a hangolandó paraméter, esetleg egy kiegészítő frekvencia-skálázás is szükséges lehet. ✅ **HIBAJAVÍTÁS (2026-09-06, ugyanaznap)**: felhasználói visszajelzés — a csillagmező forgása pár másodpercenként akadt. Gyökérok: a periodikus felhő-sodródás újraépítés a FŐ SZÁLON, szinkron módon futtatta a drága `DomainWarp`/`Fbm`-alapú `WeatherPrecipitationMultiplier`-t minden csapadék-sarokra minden alkalommal - ez frame-hitchet okozott, ami egy folyamatosan mozgó elemen (a forgó csillagégen) volt a legfeltűnőbb. Javítva: a `BuildClouds` szétválasztva egy TISZTA (nincs Unity API-hívás) `ComputeCloudMeshData` + egy fő-szálú `ApplyCloudMeshData`-ra; az `ApplyCloudOnlyRebuild` most `Task.Run`-nal háttérszálon számol (single-flight véd a párhuzamos indítástól, generáció-számláló az elavult eredmény ellen) - UGYANAZ a minta, mint a folyó-finomításnál (`_riverRefinementTask`). Az összes bemenet explicit paraméterként megy át (nincs élő mező-olvasás a háttérszálról - race-mentes, ha közben egy csúszkát mozgatnak). Build-ellenőrizve (6 új nullable-warning, UGYANAZ az elfogadott minta, mint a `_cutTask`/`_riverRefinementTask`-nál), 363/363 Core-teszt zöld. **Élő Unity-ellenőrzés hátra** - a felhasználónak kell megerősítenie, hogy az akadás valóban megszűnt. ⚠️✅ **CODE REVIEW-BAN FELTÁRT VERSENYHELYZET, JAVÍTVA (2026-09-06)**: a fenti háttérszálas javítás HIÁNYOS volt - a `Build()` sosem növelte a `_cloudRebuildGeneration`-t (a folyó-mintától eltérően), ezért egy még futó háttérszálas felhő-újraszámítás egy `Build()` UTÁN is "aktuálisnak" tűnt, és csendben felülírhatta a frissen épített, korrekt felhő-réteget egy elavult eredménnyel. Két független review-szempont is megtalálta. Javítva: `Build()` mostantól `_cloudRebuildGeneration++`+`_cloudRebuildTask=null`-t végez. Emellett egy MÁSIK, review során feltárt hiba is javítva: a mai éjszakai-sötétítés javítás (`surfaceAmbient` 0.35→0.04) észrevétlenül a FELHŐ-anyagra is rákerült, majdnem feketére sötétítve a felhőket éjszaka - most külön `cloudAmbient` mező (0.55, a shader eredeti értéke). 369/369 Core-teszt zöld, build-ellenőrizve. | Közepes | Közepes | Igen |
| M3/M13 | Csillagos háttér | Felhasználói kérés (2026-09-06): a bolygó jelenleg háttér nélkül áll a Game nézetben (üres/fekete tér) - egy csillagos égbolt (skybox vagy háttér-gömb) sokkal jobban nézne ki. Még nincs elkezdve - nincs meglévő csillag-mező/star-catalog a projektben, tervezést igényel (procedurálisan generált, determinisztikus csillag-elhelyezés, vagy egy egyszerű, nem-interaktív skybox-textúra - ez utóbbi kérdéses az I3 "nincs kézzel festett textúra" elve alól, mert a háttér-csillagok NEM a világmodell része, hanem a Naprendszeren KÍVÜLI, dekoratív kontextus - ezt tisztázni kell, mielőtt implementálnánk). ✅ **KÓDBAN MEGVALÓSÍTVA (2026-09-06)**: felhasználói döntés (ND-51) - procedurális, determinisztikus csillagmező (`RandomDomain.Decorative`, additív enum-bővítés, NEM seed-törő), a bolygó tengelyforgásával láthatóan mozgó (a Nap napi mozgásával ellentétes irányú, azonos szögű forgás), PLUSZ egy látható Nap-korong is, aminek szöge az évszakok szerint valóban változik. Kiderült, hogy a Nap-mechanika (`SunController.cs`) MÁR LÉTEZETT és MÁR helyesen hívta az `OrbitalMechanics.SunDirectionBodyFrame`-et - csak láthatatlan volt (csak a fényt forgatta). Új: `StarField.cs` (determinisztikus csillag-geometria) + `WorldGen/StarUnlit` shader (additív, a `CloudUnlit` mintájára) + `SunController` kibővítve a csillagmező-forgatással és a Nap-korong pozicionálásával. Build-ellenőrizve, 363/363 Core-teszt zöld. **A scene-be kötés (GameObject létrehozása, mezők kitöltése) felhasználói Unity-Editor lépés** - ld. ND-51 a pontos instrukciókért -, élő vizuális ellenőrzés még nincs. ✅ **ELSŐ ÉLŐ VISSZAJELZÉSI KÖR (2026-09-06, ugyanaznap)**: felhasználó bekötötte és tesztelte - "a csillagok jók", de három hiba/kérés jött: (1) a Nap-korong "négyszög" volt (a shader nem lágyította a kvad szélét) - javítva UV-alapú kör-lágyítással (`_SoftEdge` smoothstep), amihez a `StarField.Build()` mostantól UV-koordinátákat is generál minden csillag-kvadnak; (2) a Nap túl nagy/közel/gyors volt - Inspector-érték hangolási javaslat adva (scale/distance/daysPerSecond), felhasználói Editor-lépés; (3) a Nap legyen fényesebb + a háttér legyen fekete (nem a default HDRP ég) - a shader `_Brightness` tartománya 4→20-ra nőtt, `_Color` HDR-t is enged (Bloom-glow), a fekete háttér Camera "Background Type"-beállítás, felhasználói Editor-lépés. ✅✅ **MEGOLDVA, felhasználó megerősítette** (2026-09-06): a fekete háttér oka egy Volume "Sky" override volt (nem a Camera Background Type) - kikapcsolva, fekete lett. ✅ **NEGYEDIK VISSZAJELZÉS (ugyanaznap)**: "a bolygó tengely körüli forgása nem látható... a nap mindig a bolygó egy adott pontja irányában van". Gyökérok: a `SunController.autoAdvance` mező alapból `false` volt (a régi, LÁTHATATLAN fény-forgatáshoz készült, ahol a kézi csúszka-állítás volt a fő használati mód) - enélkül `currentTimeDays` sosem haladt Play közben. Javítva: alapérték `true`-ra váltva (a MÁR LÉTEZŐ, felhasználó által beállított komponensre ez NEM hat vissza - a scene-ben már `false`-ra van mentve, ott kézzel kell bepipálni). Build-ellenőrizve. ⚠️✅ **CODE REVIEW-BAN FELTÁRT HIBA, JAVÍTVA (2026-09-06)**: a `SunController` `[ExecuteAlways]`, és az `OnValidate()`-je (Edit módban, Play NÉLKÜL is lefut - pl. amikor az Inspectorban a `starField` mezőt beállítjuk) meghívta a `StarField.EnsureBuilt()`-et, ami ELSŐ alkalommal `AddComponent`/`new Material`-t hívott volna - ezt Unity NEM engedi `OnValidate` belsejéből (hibát logolna a konzolra). Javítva: `EnsureBuilt()` mostantól `Application.isPlaying`-hoz kötött - a "Rebuild Stars" context-menu (explicit felhasználói akció) ettől függetlenül Edit módban is működik. | Kész | — | — |
| M11 | Rift-zóna + lemez-hasadás/egyesülés | ✅ **C#-port kész** (2026-09-04, `PlateLifecycle`, 92+15 vektor: fa-struktúra bit-egzakt, pozíció 1e-9). A lemez-topológia tetszőleges `t`-re lekérdezhető (`ResolveTopology`), timestep-invariáns. HÁTRA: a teljes rendszer-integráció (a globális `PlateId` `ulong`-ra váltása = seed-törő, split/merge a fő elevation-láncban) + a lemezhatárok Unity-vizualizációja; a split/merge-konstansok felhasználói megerősítést igényelnek. | Alacsony | Nagy | Részben |
| M11 | Kráter tartós hatásának ellenőrzése | ✅ **KÉSZ** (2026-09-05): igazolva, hogy a kráter-elevation a Core-mezőben (`ApplyToField`) és a viewer pont-kiértékelésében (`ComputeElevationAtPoint`) UGYANAZ a függvény (`ElevationDelta`); új `ApplyToField(field, craters)` overload + a viewer arra állítva (nincs duplikált sim-matek), 2 új teszt. | Közepes | Kicsi | Nem |
| M12 | Checkpoint-rendszer + `.worldpkg` formátum | ✅ **KÉSZ** (2026-09-05, `tools/WorldGen.Cli/WorldPackage.cs`): a `.worldpkg` a VILÁG-DEFINÍCIÓT (seed/plateCount/level/deepTimeMyr) menti JSON-ban + a mentéskori `WorldStateHash`-t, ND-30 indoklása szerint (a Core tiszta függvénye a paramétereknek, nincs mit event-sourcing-olni). `worldgen checkpoint save/verify` parancsok; 7 unit teszt (round-trip, tamper-detekció, ismeretlen formátumverzió, hiányzó fájl). | Kész | — | — |
| M12 | `worldgen verify` CLI | ✅ **KÉSZ** (2026-09-05, `tools/WorldGen.Cli/Program.cs`): `worldgen hash`/`worldgen verify` parancsok (seed+paraméterek → SHA-256 hash, ill. összevetés egy elvárt hash-sel, exit code 0/1) - pontosan az I1 ("ugyanaz a definíció → ugyanaz a világ minden platformon") automatizált ellenőrzése, amit ND-30 célként megjelölt. | Kész | — | — |
| M13 | Volumetrikus felhő + AO | Semmi nincs elkezdve belőlük. | Alacsony | Nagy | Igen |
| M13 | Színkalibráció | A teljes látvány egységes színvilágának finomhangolása (spec §73). | Alacsony | Közepes | Igen |
| M8 | Habitability / Coastal complexity / Soil fertility mezők | ✅✅ **PANELRE KÖTVE** (2026-09-05): a `WorldGenPanelData`/`ComputePanelData`/`WorldGenPanelUI` mostantól kiírja a World-szintű Habitability%-ot és a Continent-szintű Coastal complexity-t, MINDKETTŐ az ND-09 kalibrált ordinális sávjával együtt (ld. lent). Soil fertility: a blokkoló **talaj-modul MVP-je KÉSZ** (2026-09-19, ND-117 - `tools/reference/regolith_ref.py` + `src/WorldGen.Core/Terrain/RegolithModel.cs`, 800 KAT-vektor, a tiszta függvény 500/500 BITPONTOSAN egyezik a Python orákulummal). A spec 10 `RegolithProfile`-mezőjéből három van meg (Depth, Porosity, WaterRetention) - csak azok, amiknek van valódi, tile-onként változó Core-forrásuk; a maradék hét litológia-/vulkanizmus-függő mező az I4 szerint HALASZTVA, nem kitalálva. **HÁTRA:** (1) az ND-09 mintájú ordinális kalibráció (N≈500 világ) a panel-sávhoz, (2) a `WorldGenPanelData`/`ComputePanelData` bekötés, (3) vizuális ellenőrzés (Unity). A Habitability/Coastal complexity mezők vizuális ellenőrzése továbbra is hátra. | Kész | — | Igen (csak megjelenítés-ellenőrzés) |
| M8 | Morfológiai típusfelismerés | ✅✅ **NÉVBE + PANELBE KÖTVE** (2026-09-05): `NameGeneration.GenerateName` új, landform-tudatos overloadja (nem seed-törő - ugyanazok a RandomProperty-k, a régi 3-paraméteres hívók bitre változatlanok, 300/300 Python-vektor továbbra is zöld) a régiónév utótagját a felismert morfológiai típusra cseréli (pl. "... Range" hegyvidéknél, "... Basin" medencénél) - pontosan a docs/01-architecture.md §2.3 "Northwatch Range" mintája. A `RegionPanelData.LandformType` a panelen is megjelenik. 7 új unit-teszt. **Vizuális ellenőrzés hátra** (Unity). | Kész | — | Igen (csak megjelenítés-ellenőrzés) |
| M8 | Ordinális kvantálás kalibrálása (ND-09) | ✅ **v1 KALIBRÁLVA** (2026-09-05, `src/WorldGen.Core/Features/OrdinalQuantization.cs` + `tools/WorldGen.Cli/OrdinalCalibration.cs`): N=500 világ (seed=1..500, plateCount=20, level=6, targetWaterFraction=0.65, Föld-analóg klíma) kvintilis-eloszlásából 4-4 küszöb a World-szintű Habitability-re és a Continent-szintű Coastal complexity-re (`worldgen calibrate-ordinals` paranccsal reprodukálható). HATÓKÖR-SZŰKÍTÉS: csak ez a 2 mező kalibrált - a spec többi ordinális mezője (Climate variability, Tectonic activity stb.) TOVÁBBRA IS BLOKKOLT, mert nincs alattuk folytonos metrika. 22 unit/integrációs teszt (határeset-viselkedés, monotonitás, végponttól-végpontig regresszió egy valódi világon). | Kész | — | Nem |
| M5 | Szél, nedvesség, csapadék | ✅ **Szél C#-kész + viewer szél-overlay bekötve** (2026-09-04/05, `WindPrecipitation`, 3×300 vektor). ✅ **A nedvesség-transzport (advekció) is KÉSZ** (2026-09-05, `MoisturePrecipitation`, reference-first: Python-ref + 140 vektor, C# 1e-6-on belül; óceán-forrás → szél menti advekció → orografikus lecsapódás) + **viewer csapadék-overlay bekötve** (aridtól csapadékosig). A modell-konstansok (iteráció, csapadék-hányad, orografia) vizuális kalibrálást igényelnek. ⚠️ **ND-50 (2026-09-06, nyitott)**: a `windSpeedOverlay`/`precipitationOverlay` kapcsolgatása/hangolása MÉG MINDIG teljes `Build()`-et indít, eldobva a háttérszálon futó folyó-finomítást (ugyanaz a hibaosztály, amit a felhőre/folyó-vonalra már megoldottunk) - itt NEM oldható meg ugyanolyan olcsó "csak-réteg-újraépítés" trükkel, mert ez a FŐ terep-mesh SZÍNÉT cseréli, nem egy külön réteget; a megoldás (egy külön "csak újraszínezés" gyors útvonal) egy nagyobb, a mesh-építést mélyen érintő refaktor - szándékosan NEM végeztük el felügyelet nélkül, ld. decisions.md ND-50 a 3 felvázolt opcióért. | Közepes | Nagy | Részben |
| M5 | Teljes hőmérséklet-modell | ✅ **C#-port kész** (2026-09-04, `Temperature.TemperatureKelvinFull`, 200 vektor, 1e-6 tol). Felhasználói megerősítés a greenhouse-konstansokra hátra. | Alacsony | Közepes | Részben |
| M7 | Tavak, jég/hó, eróziós visszahatás | ✅ **C#-port + viewer-bekötés kész** (2026-09-04/05, `LakesIceErosion`; tavak kék + állandó jég fehér a viewerben). Unity vizuális ellenőrzés hátra (felhasználó). | Közepes | Közepes | Igen |
| M10 | Erózió idővel, eljegesedés-ciklusok | ✅ **C#-port + viewer idő-csúszka bekötés kész** (2026-09-04/05, `DeepTimeErosionGlaciation`; a hegyek kopnak a `deepTimeMyr` csúszkán). ✅✅ **Eljegesedés-ciklus is bekötve** (2026-09-05): a szárazföldi állandó jégtakaró-besorolás mostantól `GlobalTempOffset(deepTimeMyr)`-t ad a hőmérsékleti mintákhoz (ND-44 dokumentált mintája szerint - `t=0`-nál az eltolás PONTOSAN 0, bizonyítva teszttel, tehát a jelenlegi renderelés bitre változatlan marad). HATÓKÖR: csak a szárazföldi jégtakaró mozdul a ciklussal - a tengeri jég/biome-határok NEM (külön munka lenne). A `GLACIATION_PERIOD_MYR=150`/`AMPLITUDE_K=6` konstansok TOVÁBBRA IS illusztratívak (ND-44 nyitott pontja) - **vizuális ellenőrzés + a konstansok megerősítése hátra**. | Alacsony | Kicsi | Igen (csak megjelenítés-ellenőrzés) |
| M10 | Dinamikus (térfogat-alapú) tengerszint — Unity vizuális ellenőrzés | A Core-oldali mechanizmus (ND-38) kész/tesztelt, de Unityben (időcsúszkával) MÉG NINCS élőben kipróbálva. | Közepes | Kicsi | Igen |
| M10 | Lemez-születés/-halál | ✅ **C#-port kész** (2026-09-04, `PlateLifecycle` — ld. az M11 rift sort; a topológia idő-lekérdezhető). Rendszer-integráció + vizualizáció hátra. | Alacsony | Nagy | Részben |
| M13 | Folyó/jég spekuláris csillanás — túl erős, "villámlás"-szerű | Felhasználói visszajelzés (2026-09-06): a folyó- és jég-felületek fény-visszaverődése (spekuláris csillanás, `VertexColorUnlit` Blinn-Phong lánc, ld. `surfaceSpecularStrength`/`surfaceShininess`) jelenleg TÚL ERŐS - a hatás úgy néz ki, "mintha villámlana", nem folyamatos, természetes csillanásnak. Valószínű ok (NEM diagnosztizálva): a spekuláris tag a durva/adaptív LOD-mesh diszkrét vertex-normálisain számol, ezért kamera-/Nap-mozgás közben a csillanás nem simán vándorol a felületen, hanem tile-ról tile-ra ugorva villan fel-le. MÁSODIK, kapcsolódó megjegyzés a felhasználótól: "azon a területen ahol épp villámlik, ott legyen ugyanez a villódzás" - ÉRTELMEZÉS (tisztázásra szorul): a csillanás jelenleg valószínűleg egy-egy izolált tile-on/pillanatban villan fel, ahelyett hogy a ténylegesen megvilágított/csillanó felület EGÉSZÉN egységesen, folytonosan jelentkezne - tehát a kért javítás nem csak az intenzitás csökkentése, hanem a csillanás TÉRBELI KITERJEDÉSÉNEK/folytonosságának javítása is (ne pattogjon tile-onként). Megoldás-jelöltek (nem eldöntve): (a) `surfaceSpecularStrength`/`surfaceShininess` csökkentése (gyors kalibráció); (b) a folyó/jég kategóriák (`RenderCategory.River`/vízfelszín) saját, tompább spekuláris-paraméterrel renderelése a szárazföldtől elkülönítve; (c) simább (interpolált, nem per-tile-diszkrét) normál-számítás a csillanás foltosságának megszüntetésére. ✅ **(a) KÓDBAN MEGVALÓSÍTVA (2026-09-07, önálló feladatválasztás)**: `surfaceSpecularStrength` 0.30→0.12 (nyers intenzitás csökkentve), `surfaceShininess` 24→8 (a Blinn-Phong `pow(ndotH, shininess)` tag SZÉLESEBB, lágyabb fényfoltot ad ugyanazon a diszkrét, per-tile normál-mezőn - ez a "pattogás" ellen is hat, nem csak az intenzitás ellen, mert egy keskeny/magas-shininess-ű fényfolt hirtelen tud eltűnni két szomszédos, eltérő normálú tile között, egy széles/alacsony-shininess-ű fokozatosabban). A `VertexColorUnlit.shader` Properties-fallback értéke is szinkronban frissítve. (b) és (c) NEM implementálva - mindkettő nagyobb refaktor (a víz/jég MOST ugyanazt az egyetlen megosztott `_vertexColorMaterial`-t használja, mint a szárazföld; a mesh-normálok per-tile diszkrétek), csak akkor érdemes belevágni, ha az (a) kalibráció élő teszten nem bizonyul elégségesnek. Build-ellenőrizve (78 hiba, ugyanaz a már ismert nullable-baseline, nincs új hibaosztály). **Élő Unity-ellenőrzés hátra.** ⚠️✅ **MÁSODIK, VALÓDI GYÖKÉROK (2026-09-07, élő visszajelzés: "a folyó és a jég még mindig roppant mód csillog az űrből, távolról")**: kiderült, hogy a korábbi diagnózis RÉSZBEN téves volt - a `River`/`SeaIce` kategóriák NEM a hangolt `_vertexColorMaterial`-t használják (az csak Ocean/IceSheet/Tundra/Temperate/Tropical/Lake-re vonatkozik), hanem Unity beépített `HDRP/Lit`-jét (`CreateFlatColorMaterial`), aminek Smoothness/Metallic tulajdonságait a kód SOHA nem állította be - a HDRP alapértelmezett (~0.5) Smoothness-e a HDRP fizikailag-korrekt, erős Nap-fényerősség mellett adta az "izzást", teljesen függetlenül az (a) javítástól. Javítva: `CreateFlatColorMaterial` mostantól alacsony (0.08) Smoothness-t és 0 Metallic-ot állít be minden ezzel létrehozott anyagra (River, SeaIce, Crater, határvonal, tartalék-anyagok). Build-ellenőrizve (78 hiba, baseline). ⚠️ **HARMADIK KÖR**: kiderült, hogy a scene-ben a `surfaceSpecularStrength`/`surfaceShininess`/`surfaceAmbient` régi, kód-alapérték-változás előtti értékeken voltak szerializálva - scene YAML-ban is javítva. ⚠️ **NEGYEDIK KÖR (2026-09-07): "továbbra is fennáll"** - ellenőrizve: mindhárom hely (scene/shader/kód) a helyes csökkentett értéken állt, semmi nem állt vissza. DIAGNOSZTIKAI KÍSÉRLETKÉNT mindkét specular-forrás NULLÁRA állítva (`surfaceSpecularStrength`: 0.12→0 mindhárom helyen, `FlatMaterialSmoothness`: 0.08→0) - ha ez SEM tünteti el a csillanást, az kizárja mindkét jelenlegi anyagot, és egy mélyebb okra (HDRP Reflection Probe/SSR, vagy a futásidőben létrehozott HDRP/Lit anyagok hiányos shader-kulcsszó-inicializálása) terelné a vizsgálatot. Build-ellenőrizve (78 hiba, baseline). ✅✅✅ **ÖTÖDIK KÖR, VALÓDI GYÖKÉROK MEGTALÁLVA (2026-09-07, felhasználói screenshot alapján)**: a felhasználó `pics/p.png`-ben megjelölte a csillogó foltokat - a specStrength=0/Smoothness=0 állapot ELLENÉRE a csillogás VÁLTOZATLAN maradt, ami kizárta mindkét terep-anyagot. A foltok kerek, glóriás jellege HDRP Bloom post-processingre utalt, nem anyag-specularra. Megtalálva: a projekt globális `DefaultSettingsVolumeProfile.asset`-jében a Bloom `threshold`=0 volt (gyakorlatilag bármi bloomol, nem csak a szándékosan HDR-fényes Nap) - ez egy POST-PROCESSING effekt, teljesen független attól, hogy a fényesség diffúz vagy spekuláris eredetű, ezért volt hatástalan minden anyag-szintű próbálkozás. Javítva: `threshold` 0→1.05 (ld. ND-53, docs/04-decisions.md). A diagnosztikai célból nullázott anyag-paraméterek visszaállítva az eredeti, ésszerű kalibrációra (0.12/8/0.08) - sosem voltak hibásak. ⚠️ **HATODIK KÖR: a Bloom-javítás SEM segített ("ugyanaz a látvány")**. A meglévő Inspector-kapcsolókkal (Felhők, Kráterek, Tavak+jég, StarField, Gizmos, SunVisual, Szél-/Csapadék-overlay, Erózió) MIND a 7 réteget egyenként kizárva élő teszttel - a foltok minden esetben megmaradtak, bizonyítva, hogy az alap terep/óceán-mesh része, mindig renderelődik. ÚJ HIPOTÉZIS: a `RenderCategory.IceSheet`/`SeaIce` NYERS ALAPSZÍNE `(0.95,0.96,0.98)`/`(0.80,0.88,0.93)` volt - majdnem tiszta fehér, VALÓDI fényezés nélkül is "izzó" hatást kelthet, ami megmagyarázná, hogy semelyik fényezési/post-processing változtatás miért nem hatott (egyik sem érinti a nyers alapszínt). Tompítva `(0.80,0.83,0.87)`/`(0.72,0.78,0.84)`-re. ✅ **HETEDIK KÖR, A DÖNTŐ NYOM (2026-09-07)**: friss screenshot után a felhasználó észrevette: "Tavak+jég kikapcsolva → MÉG FÉNYESEBB, szinte vakító" - amikor a jég ki van kapcsolva, az adott terület LIKVID vízfelszínre (`BuildWaterSurface`) vált, ami egy TÖKÉLETESEN SIMA gömbhéj - a screenshoton látható foltok jellege (koncentrált, éles, glóriás) valódi műholdképek "napcsillanás" (sun glint) jelenségére hasonlít, ami sima felületen sokkal koncentráltabb/fényesebb, mint a durva terepen, UGYANOLYAN specular-paraméterek mellett (koherens normál nagy területen). Gyökérok: a vízfelszín EDDIG a szárazfölddel megosztott `_vertexColorMaterial`-t használta. Javítva: a vízfelszín MOST KÜLÖN anyagot kap (`_waterSurfaceMaterial`), saját, jóval alacsonyabb specular-paraméterekkel (`waterSpecularStrength=0.02`, Inspector-mezők). Build-ellenőrizve (79 hiba - +1 az ismert CS8618-mintából egy új Material-mező miatt, nem új hibaosztály). ⚠️ **NYOLCADIK KÖR, DÖNTŐ FORDULAT (2026-09-07)**: a felhasználó kikapcsolta a Directional Light-ot élőben - SEMMI nem változott. Ez kizár MINDEN fényezési hipotézist. Valószínű ok: a `VertexColorUnlit` shader nem Unity beépített fény-rendszerét használja, a C# kézzel olvassa ki a Light tulajdonságait, amik egy kikapcsolt GameObject-en NEM nullázódnak. Tiszta diagnosztikai teszt hozzáadva: `diagForceZeroLighting` (alapból BE) explicit (0,0,0) Nap-színt/0 ambienst kényszerít kódból. Build-ellenőrizve (79 hiba, baseline). ✅ **KILENCEDIK KÖR (2026-09-07)**: friss screenshot szerint a Föld nagy része elsötétült (a diagnosztika működött), DE 2-3 szorosan csoportosuló, éles fényfolt maradt a pólusoknál/szigeteknél, "Felhők (MVP)" bekapcsolt állapotban. Hiányosság találva: a `cloudAmbient` (0.55, külön mező) NEM lett nullázva a diagnosztikai tesztben - egy sűrű/fehér felhő-csomó a "fekete Nap" alatt is fényes maradhatott. A foltok diszkrét, csoportosuló jellege illik a "lokálisan sűrű csapadék" képhez. Javítva: `cloudAmbient` is 0-ra kényszerítve a teszt alatt. Build-ellenőrizve (79 hiba, baseline). ⚠️ **TIZEDIK KÖR: a felhők kikapcsolása SEM segített.** Mivel a saját shader BIZONYÍTOTTAN elsötétült a diagnosztika alatt, és minden más réteget kizártunk, az egyetlen SOHA nem érintett rendszer a `River`/`SeaIce`/`Crater` HDRP/Lit anyaga - ez Unity valódi PBR fény-csővezetékén megy át, ami az ÉGBOLT (Sky) ambient-hozzájárulását is tartalmazza, teljesen függetlenül a Directional Light-tól. Megtalálva: a globális `DefaultSettingsVolumeProfile.asset`-ben a `HDRISky.exposure` értéke `11` volt (a másik két sky-típus mindkettő 0-n áll) - egy kirívóan túlexponált égbolt, ami óriási ambient-fényforrást ad minden HDRP/Lit anyagnak, amit a fehér SeaIce sokkal látványosabban ver vissza, mint a sötét Crater. Javítva: `exposure` 11→0. A `diagForceZeroLighting` diagnosztikai kapcsoló alapértéke is visszaállítva `false`-ra (a normál, végleges megjelenés teszteléséhez). Build-ellenőrizve (79 hiba, baseline). ✅ **TIZENEGYEDIK KÖR (2026-09-07): "picit jobb, de még mindig nagyon fénylik"** - valós, mérhető javulás, de nem elég. `IndirectLightingController` ellenőrizve (semleges, rendben). A `River`/`SeaIce` HDRP/Lit anyaga még mindig `Smoothness=0.08`-on állt - végleg nullázva (a HDRP BRDF-je Smoothness=0-nál elméletileg semmilyen specularis/tükröző választ nem ad), plusz explicit fekete Emissive Color biztonsági beállítás. Build-ellenőrizve (79 hiba, baseline). ✅ **TIZENKETTEDIK KÖR, TELJES SZISZTEMATIKUS HDRP VOLUME-AUDIT (2026-09-07)**: felhasználói kérésre ("szeretném, ha most szisztematikusan utánanéznél... professzionális munkát várok el") egy közeli, felülnézeti screenshot (teljesen kiégett, hatalmas fehér gömb a pólusnál) alapján VÉGIGMENTEM a teljes HDRP Volume-profil MINDEN komponensén: Bloom (már javítva), HDRISky (már javítva), GradientSky/PhysicallyBasedSky (rendben), IndirectLightingController (rendben), **Exposure (ÚJ TALÁLAT: `mode=Automatic`, `limitMax=14`, kamerakeret-függő dinamikus expozíció - elvi hiba is az I3 invariáns szempontjából, ha a kamera sok sötét űrrel körülvett fényes felszínre néz, drasztikusan túlexponálhat)**, Tonemapping (ACES, nem hard-clip, rendben), WhiteBalance/ColorAdjustments (rendben). Javítva: `Exposure.mode` Automatic→Fixed, `fixedExposure=0`. Dokumentált, de nem javított mellékes megfigyelés: a kráter-markerek (`CreatePrimitive(Sphere)`, 1-6 egység átmérő) extrém közeli zoomnál betölthetik a képernyőt - a színük nem indokolná fehér megjelenést, de érdemes tisztázni, éppen egy krátert mutatott-e a screenshot. ✅✅✅ **TIZENHARMADIK KÖR, VALÓDI GYÖKÉROK MEGTALÁLVA DIREKT AZONOSÍTÁSSAL (2026-09-07)**: a felhasználó élesen jelezte a folyamat elfogadhatatlanságát - leálltunk a találgatással, direkt GameObject-azonosítást kértem (Play→Pause→Scene-kattintás). Válasz: **"WaterSurface"**. Átvizsgálva az `AddQuad` mesh-építő függvényt: a felszín-normál `Vector3.Cross(...).normalized` képlettel készül - degenerált quadnál (4 sarokpont majdnem egybeesik, ami a kockás-gömb geometria SARKAIN/PÓLUSAIN fordulhat elő) ez NaN-t eredményezhet. A NaN a shaderben mindent megfertőz, a GPU tipikusan tiszta fehérként jeleníti meg - ez megmagyarázza, miért volt hatástalan MINDEN korábbi javítás (NaN×0=NaN, nem 0, tehát még a kényszerített-fekete-fény teszt sem segíthetett). Javítva: védelmi küszöb az `AddQuad`-ban, degenerált quadnál a sarokpontok sugárirány-átlagára esik vissza. Build-ellenőrizve (79 hiba, baseline). ❌ **A TIZENHARMADIK KÖR NEM SEGÍTETT ("totál semmi nem változott")** - teljes Play-újraindítás után is ("nem stale build"). Újranézve a feltételt: `rawNormal.sqrMagnitude < 1e-12f` NaN esetén IEEE-754 szerint MINDIG `false` (bármilyen `<`/`>` NaN-nal false), tehát ha a bemenő SAROKPONTOK már NaN-t tartalmaztak, a védelmi ág soha nem futott le. ✅✅✅✅ **TIZENNEGYEDIK KÖR (2026-09-09)**: 3 javítás egyszerre - (1) feltétel `!(rawNormal.sqrMagnitude >= 1e-12f)`-re cserélve, ez NaN esetén is igaz, tehát a fallback mindig lefut; a fallback-ág is védve (ha `avgPos.normalized` sem ésszerű, `Vector3.up`); (2) új `SanitizeVertexColor` - a sarok-SZÍNEKET (nem csak a normált) is ellenőrzi NaN/Infinity-re, talált esetben Debug.LogWarning + MAGENTA-ra cserél; (3) `VertexColorUnlit.shader` végén `isnan`/`isinf` ellenőrzés a végső `rgb`-n - ha a GPU maga produkál NaN-t, CIÁN-ra cserél. A két diagnosztikai szín (magenta=C#-oldali szín-NaN, cián=GPU-oldali számítás-NaN) egyértelműen megkülönbözteti a maradék hiba forrását, ha a fehér folt továbbra sem tűnne el teljesen. `dotnet build src/WorldGen.Core` 0 hiba; a Unity-specifikus fájlokat manuális átolvasással ellenőriztem (nincs elérhető Unity batch build ebben a munkamenetben). **Élő Unity-ellenőrzés hátra.** ✅✅✅✅✅ **TIZENÖTÖDIK KÖR, STRUKTURÁLIS ÚJRATERVEZÉS (2026-09-09, felhasználói kérésre: "keress jobb megoldást")**: `git log` alapján megtaláltam a pontos eredet-kommitot - `1efd5fa` ("restore specular", 2026-09-05) vezette be, hogy a shader egyáltalán kiolvassa a per-vertex normált; előtte a hibás `Vector3.Cross(...).normalized` már a kódban volt, csak láthatatlan. A 13-14. körös PATCH-elés (védelmi küszöbök egy törékeny számítás köré) helyett a teljes cross-product-alapú lapos normál-számítást KIIKTATTAM: mivel a felszín gyakorlatilag egy gömb, minden csúcspont saját normálja `normalize(saját pozíció)` (`SafeSurfaceNormal`) - STRUKTURÁLISAN soha nem lehet NaN/nulla, függetlenül a négy sarokpont egybeesésétől. Mellékhatásként ez SIMA, csúcsonkénti normálokat ad a régi LAPOS, quadonkénti helyett, ami az első körben jelzett "tile-ról tile-ra pattogó" csillanást is megoldja. A 14. körös diagnosztikai védőháló (magenta/cián NaN-jelzés) megmaradt további biztonsági hálóként. `dotnet build src/WorldGen.Core` 0 hiba. **Élő Unity-ellenőrzés hátra.** ✅✅✅✅✅✅ **TIZENHATODIK KÖR (2026-09-09): "most jobb de még mindig van egy pont"** - screenshot alapján a maradék folt CIÁN színű volt, ami a 14. körös GPU-oldali NaN-diagnosztika (`isnan`→cián) saját jelzése - tehát a mesh-normál javítás (15. kör) jó volt, de van egy MÁSIK, független NaN-forrás: a Blinn-Phong `H = normalize(L+V)` fél-vektor, ha a Nap- és kamera-irány közel ellentétes (limb/sarok-közeli, kamera-szög-függő eset), `L+V≈0` → `normalize` NaN-t ad. Javítva: `SafeNormalize` segédfüggvény + degenerált esetben spec=0 a `VertexColorUnlit.shader`-ben. `dotnet build src/WorldGen.Core` 0 hiba. **Élő Unity-ellenőrzés hátra.** ✅✅✅✅✅✅✅ **TIZENHETEDIK KÖR (2026-09-09), ND-54: a sárga pólus-folt NEM rendering-hiba** - számolással igazolt, valós jelenség: a "Simple" `Temperature.TemperatureKelvin` (jég-albedó/óceán-puffer nélkül) a sarki nyár alatt a pólust melegebbnek számolja, mint az egyenlítőt (a Nap sosem nyugszik le ott) - kiszámolva ~319K a pólusnál vs ~303K az egyenlítőnél a jelenlegi 23.44°-os tengelydőléssel, ami átlépi a "Tropical" küszöböt. Felhasználói döntés: mind az 5 hívási hely átállítva a "Full" hőmodellre (`TemperatureKelvinFull`, közös `TemperatureKelvinAt` segédfüggvényen át) - ez konzisztenssé is teszi a panel biome-besorolásával. Emellett `useGpuClassification: 1→0` a scene-ben, mert a GPU compute shader saját Simple-portja felülírta volna a CPU-javítást (a Full modell GPU-portolása kockázatos lenne, ND-52 precedens). Ld. `docs/04-decisions.md` ND-54. `dotnet build src/WorldGen.Core` 0 hiba. ❌ **VISSZAVONVA (2026-09-09): "az egész bolygó tök sötét"** - a Full-modell váltás valószínűleg súlyos teljesítmény-regressziót okozott (a pozíciófüggetlen `ClimateCycleTemperatureK`/`GreenhouseTemperature` feleslegesen újraszámolódott minden egyes sarokra/tile-ra, 2x Threefry hash + 3x SinCos hívással, százezres nagyságrendben) - a mesh-build valószínűleg sosem fejeződött be. Visszaállítva Simple képletre + `useGpuClassification: 1`-re. A sárga pólus-folt emiatt visszatérhet - a Full modell megfelelő, cache-elt bevezetése külön feladat marad. ✅ **TIZENNYOLCADIK KÖR (2026-09-09): "elveszett a vizuális magasság érzete"** - a 15. kör mellékhatása: a lejtő-érzékeny cross-product normált teljesen lecserélte gömb-irányú normálra, ami megszüntette a domborzat-árnyalást. Javítva: `AddQuad` visszaáll a lejtő-érzékeny normálra elsődlegesként, a 14. körben felismert HELYES NaN-védelemmel (`!(sqrMagnitude >= küszöb)`) csak a valóban degenerált esetekre tartalékként. `dotnet build src/WorldGen.Core` 0 hiba. ❌ **"totál visszaállt a csillogás"** - a lapos normál visszaállítása visszahozta az eredeti (2026-09-06 óta ismert) diszkontinuitás-problémát. ✅✅✅✅✅✅✅✅ **TIZENKILENCEDIK KÖR, ND-55 (2026-09-09)**: harmadik módszer - a normált kis, rögzített UV-eltolású veges differenciával, a MEGLÉVŐ, szomszédos tile-ok közötti megosztott sarok-cache mintázatán (mint pozíció/szín) keresztül számoljuk (`ComputeCornerNormalViaFiniteDifference` + `_persistentCornerNormalCache`/`cornerNormalCache`) - ez EGYSZERRE lejtés-érzékeny (magasság-érzet megmarad) ÉS folytonos a tile-határokon (nincs ugrás/villódzás). Új `AddQuad` túlterhelés kívülről kapott normálokkal, a szárazföldi hívási helyeken bekötve (víz/tó/folyó/kráter változatlan marad). Teljesítmény-tudatosan: csak a meglévő párhuzamos sarok-cache-en keresztül, sosem quadonként közvetlenül. Ld. `docs/04-decisions.md` ND-55. ✅✅✅✅✅✅✅✅✅ **MEGERŐSÍTVE (2026-09-09): "magasságérzet ok, most eltűntek a tile-ok is, jónak tűnik... nem rossz"** - funkcionálisan ÉS teljesítmény-szempontból is LEZÁRVA. Jövőbeli, NEM sürgős finomhangolási lehetőség, ha később mégis teljesítmény-problémát észlelnénk nagyobb rácsfelbontásnál: a `NormalSampleEpsilonUV` (1e-4) durvábbra állítható, vagy a normál-cache-nek adható saját (a pozíció-cache-től független) LRU-méret. ⚠️ **HUSZADIK KÖR (2026-09-09): "a tükröződés még mindig durva"** - az ND-55 mellékhatása: a spekuláris csillanás most nagy, összefüggő területen koherensen jelentkezik (korábban a diszkontinuus normál szétszórta sok kis foltra). Kiszámolva: fényes, közel fehér jégfelület alapszín+diffúz önmagában ~1.0-1.1 fényerőt ér el - határeset volt a Bloom `threshold=1.05` körül. Javítva: `threshold` 1.05→1.4, `scatter` 0.7→0.5 (`DefaultSettingsVolumeProfile.asset`). ❌ **"a csillogás megmaradt", NEM Bloom-küszöb probléma** - friss screenshot alapján a folt több különálló, kerek fényfolt csoportja egy sötét háttér előtt - ÚJ HIPOTÉZIS: napfényes felhőgomolyok, nem terep-specular. Egybeesés: a felhasználó ugyanekkor kérte a felhők alapértelmezett kikapcsolását (`showClouds`/`cloudDriftEnabled` true→false, kód+scene). ⚠️ **ÚJ HIBAOSZTÁLY (2026-09-09): "nincs alapból kikapcsolva egyik sem"** - kizárva Project Settings/kód/prefab; a valódi ok: a Unity Editor a scene-t MÁR MEMÓRIÁBAN tartotta a fájlon-kívüli (szövegszerkesztős) módosításom ELŐTTI állapotban - egy `.unity` fájl külső szerkesztése NEM tölti újra automatikusan egy már nyitva lévő scene-t. Felhasználó kézzel kikapcsolja Edit módban + menti. ✅✅✅✅✅✅✅✅✅✅ **MEGERŐSÍTVE (2026-09-09): "ez most jó"** - a felhők (`showClouds`/`cloudDriftEnabled`) kikapcsolása után a fényfolt eltűnt. A 21 körös vizsgálat lezárva: a "villódzás"/"csillogás" jelenségnek TÖBB, egymástól független forrása volt egyszerre (specular-hangolás, HDRP Smoothness, Bloom threshold, HDRISky exposure, Exposure mode, degenerált quad NaN normál, Blinn-Phong H-vektor NaN, sárga pólus hőmodell-hiányosság, ÉS végül a felhőréteg) - mindegyiket sorban ki kellett zárni/javítani, mielőtt a teljes kép tiszta lett. ⚠️ **ÚJRA ELŐKERÜLT (2026-09-09)**: pólusi túlexponálás ismét jelentkezett. Döntő teszt: "Tavak+jég" kikapcsolása a nyílt vízfelszínt erősebben túlexponálta (nem gyengítette) - számolással kizárva, hogy ez a rendes fényezési lánc eredménye lenne. ✅✅✅✅✅✅✅✅✅✅✅ **VÉGLEGESEN MEGOLDVA**: `Bloom.intensity` 0.2→**0** (teljes kikapcsolás, nem csak küszöb-hangolás). Felhasználói visszaigazolás: **"tök jó, végre nem csillognak a pólusok! megoldottad"**. Ld. `docs/04-decisions.md` ND-53 kiegészítése a teljes indoklásért. | Közepes | Közepes | Nem |
| M13/M9 | Fraktál domborzat-zaj nagyon közeli zoomnál lapos | Felhasználói kérés (2026-09-07): "fraktál zaj generálás nagyon közeli zoom esetén nem jó... vezess be egy másodlagos zajt, ami a tile-ok eredeti mérete szerint 40x40 tile-on ismétlődik... jóval alacsonyabb amplitúdóval". ✅ **KÓDBAN MEGVALÓSÍTVA (ND-52, ugyanaznap)**: diagnózis - az `AdaptiveQuadTree` subdivíziója TISZTÁN képernyő-téri/szögméret-alapú, NEM zaj-tartalom-érzékeny; a VALÓS ok, hogy az elsődleges `RidgedMultifractal` legfinomabb oktávja is tucat-km hullámhosszú, míg a renderelt LOD ennél sokkal mélyebbre bont, ezért a legmélyebb szinteken a domborzat gyakorlatilag sima. `CrustElevation.SecondaryDetailNoise` - UGYANAZ a verifikált `RidgedMultifractal` primitív, `SecondaryNoiseFrequency`-n (periódus = 40 × a `PlanetGridMesh.level=5` "eredeti" statikus rács-tile szögmérete), `SecondaryNoiseAmplitudeMeters=200` (az elsődleges ~1/15-e), külön koordináta-eltolással dekorrelálva, a `MountainMask`-ot SZÁNDÉKOSAN kihagyva (a sík régiókban a legfontosabb a közeli-zoom textúra). Ez SZÁMSZERŰEN megváltoztatja `BaseElevation` kimenetét minden pozícióra - a Python referencia (`crust_elevation_ref.py`) egyidejűleg frissült, és MINDEN rá épülő KAT-vektor (`crust_elevation`/`plate_boundary`/`erosion_glaciation_deep_time`/`hydrology`/`river_path`/`lakes_ice_erosion`/`moisture_transport`/`features`/`volcanism`/`state_hash`) újragenerálva, a `TestEarth001` kontinens-méret-listája frissítve (44→37 kontinens, víz-arány cél változatlan). Ld. ND-52 a teljes indoklásért. 6 új Core-teszt (tisztaság, paraméter-érzékenység, periodicitás, amplitúdó-arány). **Nyitott, dokumentált feltételezés**: "tile-ok eredeti mérete" = `PlanetGridMesh.level=5` (NEM az ND-02 Core-oldali level 6) - élő Unity-visszajelzés alapján a két konstans (`SecondaryNoiseReferenceLevel`, `SecondaryNoisePeriodTiles`) újrahangolható. **Élő Unity-ellenőrzés hátra** - ez a vizuális hatás csak Play-módban, legmélyebb zoomnál ellenőrizhető. ⚠️✅ **CODE REVIEW-BAN FELTÁRT 3 HIBA, JAVÍTVA (2026-09-07)**: (1) a GPU compute shader oldali `BaseElevationF` (`TileClassification.compute`) NEM kapta meg a másodlagos zajt - ha a `useGpuClassification`/`useGpuGeometry` kapcsolók bármelyike be van kapcsolva (a scene-ben `useGpuClassification=1` ÉPPEN aktív), a klasszifikáció és a geometria csendben eltérő elevációt használt volna a tengerszint/jég-küszöbök közelében - pótolva, 1:1 tükrözve a CPU-formulát; (2) a kontinens/régió-panel link-kattintás detektálása az ELSŐ egyező (nem a legközelebbi) sort választotta, és a padded kattint-sávok szomszédos sorok között ténylegesen kb. 2x-esen átfednek (mért font-metrikákkal) - ez valószínűleg a session egész hosszában visszatérő "rossz régióhoz repül" panaszok egyik valódi maradék oka volt; javítva: mostantól a klikk Y-koordinátájához LEGKÖZELEBBI sor közepét választja az összes egyező jelölt közül; (3) a tengerfenék-árnyalás normalizációja (`OceanRockBucket`/`ContinuousOceanRockColor`) nem számolt a másodlagos zaj amplitúdójával - pótolva. 375/375 Core-teszt zöld, Unity build-ellenőrizve (78 hiba, ugyanaz a baseline). ⚠️ **ÉLŐ UNITY-HIBA (2026-09-07)**: az (1) GPU shader javítás UTÁN a felhasználó "Compiler timed out" hibát kapott a `CSGenerateTerrainGeometry` kernelre. ELSŐ javítási kísérlet (3→1 oktáv + `[loop]` attribútum) NEM oldotta meg - a felhasználó UGYANAZT a hibát kapta újra. ✅ **VÉGLEGESEN JAVÍTVA, TELJES VISSZAVONÁSSAL**: mivel két egymást követő próbálkozás (3 oktáv, majd 1 oktáv+`[loop]`) is ugyanabba a fordítási-idő korlátba ütközött, ahelyett hogy tovább találgatnánk (amit nem tudok élőben ellenőrizni), a GPU-oldali `BaseElevationF` VISSZAÁLLÍTVA bájtra pontosan az ND-52 ELŐTTI, bizonyítottan működő formulára (nincs másodlagos zaj a GPU-porton) - ez a `git diff HEAD`-del ellenőrizve, a `BaseElevationF` függvény törzse változatlan, csak egy magyarázó kommentár maradt. **SZÁNDÉKOS, DOKUMENTÁLT KORLÁT**: a `useGpuClassification`/`useGpuGeometry` kapcsolók bekapcsolásakor a GPU-úton számolt eleváció a finom részlet-zaj NÉLKÜL, valamivel simább, mint a CPU-é - ez egy elfogadott tradeoff, amíg a shader-fordítási-idő költségvetés nem enged többet (pl. egy jövőbeli, a shader teljes szerkezetét érintő optimalizálással). ⚠️✅ **UJRAHANGOLÁS (2026-09-07, második felhasználói kör: "nem jött be... a másodlagos zaj... lehet e az egész síkra kiterjedő folytonos zajt hozzáadni?")**: utólagos számolás feltárta a tervezési hibát - a `SecondaryNoisePeriodTiles=40` × egy level=5 tile szögmérete együtt kb. 0.3125-szöröse egy teljes nagykörnek, tehát a "másodlagos zaj" SOHA nem is adott közeli-zoom finom részletet (a periódusa SZÉLESEBB, mint az elsődleges zaj bázis-oktávja is), csak egy alig észrevehető (200m amplitúdójú) regionális hullámzást - gyakorlatilag láthatatlan maradt minden zoom-szinten. Javítva: `SecondaryNoiseAmplitudeMeters` 200→900m-re emelve (az elsődleges 3000m kb. 30%-a), hogy ez a már eleve folytonos, egész-felszínes hullámzás láthatóvá váljon minden zoom-szinten (nem csak elméletileg a legmélyebb LOD-nál) - ez KÖZVETLENÜL válasz a "folytonos zaj az egész síkra" kérésre, mivel a jelenség már eleve ilyen jellegű volt, csak túl halvány. Emellett a `PlanetView.unity` scene-ben talált stale (a kód-alapértelmezés-változásokat nem követő, korábban szerializált) `surfaceAmbient`=0.35/`surfaceSpecularStrength`=0.3/`surfaceShininess`=24 értékek is közvetlenül 0.04/0.12/8-ra frissítve a scene-fájlban (ez magyarázza, miért "nem jött be" az éjszakai-sötétítés fix sem - a kód-alapérték-változás nem ír felül egy már létező, szerializált Inspector-értéket). Python referencia + MINDEN kapcsolódó KAT-vektor újragenerálva, kontinens-szám 37→34-re frissítve (Python-mérve), habitability-sáv Moderate→Low. 375/375 Core-teszt zöld, Unity build-ellenőrizve (78 hiba, baseline). ✅ **ND-56, HARMADIK RÉTEG (2026-09-09, felhasználói kérés: "olyat szeretnék ami a maximális felbontás esetén is minden tile-ra hatással van")**: `CrustElevation.TertiaryDetailNoise` - ugyanaz a `RidgedMultifractal`, harmadik koordináta-eltolással. GPU-ra tolás elvetve (Core motorfüggetlensége, ND-52 GPU-fordítási-időtúllépés-precedens, float32 pontosság finom zajnál rosszabb, nem jobb). ❌ **ELSŐ PRÓBÁLKOZÁS VISSZAVONVA ÉLŐ TESZT ELŐTT**: level=20 (elméleti max LOD)/3 tile periódus - felhasználói visszajelzés: "katasztrófa... a távoli zoom nézetet nagyban befolyásolja, ellenben az extrém közeli zoom esetén nem egyenletes a zaj eloszlása" - térbeli ALIASING (level=20 gyakorlatban szinte soha nem érhető el). ✅ JAVÍTVA: level 20→13 (~ND-18 cél-LOD-hoz közeli), periódus 3→8 tile (ugyanaz a minta, mint a másodlagos zajnál). Python referencia + teljes downstream KAT-vektor-lánc újragenerálva, kontinens-szám 34→37, régió-szám 412→402 frissítve, 375/375 Core-teszt zöld. Ld. ND-56 (docs/04-decisions.md) a teljes indoklásért. ❌ **TELJESEN VISSZAVONVA (2026-09-09): "nem lett jobb... működjön minden úgy ahogy ezelőtt"** - az újrahangolt verzió sem hozott érzékelhető javulást. A teljes ND-56 réteg törölve Python-ból és C#-ból, downstream KAT-vektor-lánc visszaregenerálva az ND-52 állapotra (kontinens-szám vissza 34, régió-szám vissza 412), 375/375 Core-teszt PASS. A `CrustElevation.BaseElevation` most a mai session ELŐTTI (ND-52 utáni) állapotot tükrözi. | Közepes | Közepes | Nem |

| M4/M10 | Lemezhatár kéregátmenetének folytonosítása és fizikai peremkorlát | ✅ **Kódban megvalósítva (ND-88/ND-90, 2026-09-12):** a viewer alapból 1:1 fizikai függőleges skálát használ a korábbi 111,3× túlrajzolás helyett. A Core eltérő kéregtípusú lemezeinek -4000/+800 m bázisa a két legközelebbi lemez `0.005`-ös gap-sávjában folytonosan, smoothstep súllyal keveredik; a külön tektonikus uplift plafonja 1500→1000 m. A statikus, deep-time és cache-elt lekérdezési út közös formulát használ; rendereroldali clamp nincs. Numerikus világkép-változásként `.worldpkg` v2 és teljes downstream Python/KAT-frissítés tartozik hozzá. **Nyitott:** élő Unityban ugyanazon problémás hely, több időpont és zoom ellenőrzése; a teljes eleváció a kéregbázis és domborzati zaj miatt továbbra is lehet 1 km fölött, az 1 km-es korlát az uplift-komponensre vonatkozik. | Közepes | Kicsi (élő validáció) | Igen |
| M4/M8/UI | Eltűnő Region/Area kis landmass esetén | ✅ **KÓDBAN MEGVALÓSÍTVA (2026-09-13)** — felhasználói jelentés: kis (≤19 tile-os) landmass-oknak nem volt megjelenő régiójuk/területük, mert a régió-szűrés (`Count >= 5`) minden helyi vízgyűjtőjükre külön-külön alkalmazódott. Javítás: ha egy landmass normál (méretszűrt) régió-listája üres, egyetlen fallback régióként a landmass TELJES tile-halmazát adja a Viewer (`ComputeRegionPanelDataForContinent`, negatív index-kódolás, disjunkt featureId-tartomány 900000+/950000+) — Core-szemantika (`FindWatershedRegions`) változatlan. Invariáns: minden nem-üres landmass-nak ≥1 régiója, minden régiónak ≥1 területe van (`PartitionRegionIntoAreas` már eleve garantálja ezt). 1 új Core-teszt (`EveryLandmassHasAtLeastOneRegionAfterFallback`, valódi világon gyakorolja a fallback-ágat). **Nem seed-törő.** Ld. `history/2026-09-13-navigation-menu-area-level.md` és a beszélgetés. | Magas | Kicsi | Nem |
| M4/M8/UI | Continent és Island fogalmak szétválasztása | ✅ **KÓDBAN MEGVALÓSÍTVA (2026-09-13)** — minden összefüggő szárazföld-komponens azonos "kontinens" hierarchiaszinten jelent meg, 4000+ tile-os szuperkontinenstől 5 tile-os szigetig. Új `FeatureSegmentation.LandmassClass` (`Continent`/`LargeIsland`/`Island`/`Islet`), a landmass méretét a világ TELJES szárazföld-tile-számához viszonyítva (nem fix abszolút vagy bolygó-tile-arányos küszöb — a víz-arány kalibrációjától független, stabil mérték). Küszöbök a valós TestEarth001 eloszlásán ellenőrizve (5%-os határ pontosan a 2 szuperkontinenst választja el a 3. legnagyobbtól). Eredmény ezen a világon: 2 Continent, 4 LargeIsland, 25 Island, 0 Islet (az Islet sáv a jelenlegi `minSize=5` mellett szerkezetileg üres — ld. a 3. probléma sorát). `ContinentPanelData.LandmassClass` megjelenik a navigációs listában és a "Kiválasztott elem" panelen. A flood-fill komponens/hierarchia VÁLTOZATLAN, csak utólagos címkézés. 9 új Core-teszt, Python-vektor-egyezéssel. **Nem seed-törő.** | Magas | Kicsi | Nem |
| M4/M10 | Extrém landmass méreteloszlás — diagnózis lezárva, javítás elhalasztva | 2026-09-13, felhasználói jelentés: néhány szuperkontinens dominál (a mért világon 2 db adja a szárazföld ~90%-át), miközben rengeteg apró sziget is keletkezik. **GYÖKÉROK (mérve, forrásból ellenőrizve, ld. `history/2026-09-13-landmass-distribution-investigation.md`):** a `CrustElevation` HATÓKÖRE dokumentáltan lemez-szintű BINÁRIS kéreg-típust ad (`IsOceanic` egy Bernoulli-próba plate-enként, "a spec §14.1 Plate struct-ja is így modellezi") — ez gráf-percolation viselkedést okoz: a TestEarth001 20 lemezéből 13 kontinentális, és ezek 12-je csak 2 óriás klaszterré olvad össze (mért, plate-hozzárendelési diagnózissal igazolva). **Két új diagnosztikai eszköz készült**: `FeatureSegmentation.ComputeLandmassDistributionStats` (LandmassCount/TotalLandTiles/LargestShare/Top2Share/Median/P90/TinyCount/Gini, Core, tesztelt, Python-vektor-egyezéssel) + 5 seedes empirikus sweep. **Két kísérlet lefuttatva és ELUTASÍTVA (mérve, nem találgatva):** (A) `oceanicProbability` 0.40→0.50 (a percolációs küszöb felé) — ROSSZABB lett (legnagyobb% variancia nőtt, néhol 93%-ig). (B) `plateCount` 20→60 (finomabb percolációs gráf) — csak enyhe javulás, továbbra is céltartományon kívül. **KRITIKUS forrás-ellenőrzés**: a felhasználó jóváhagyott egy harmadik kísérletet (a kéreg-típus elevációs résének szűkítése), de a `docs/04-decisions.md` ND-37 bejegyzése kiderítette, hogy ezt MÁR kipróbálták és elutasították ("irreálisan sekély óceánt eredményezne"), ÉS hogy a jelenlegi extrém eloszlás MAGA ND-37 2026-09-0x-i, tudatosan vállalt mellékhatása (a "falszerű part" hiba javításáért cserébe, akkor is mérve/dokumentálva). **Következtetés: a sima part-átmenet és a kiegyensúlyozott kontinensméret-eloszlás a jelenlegi bináris lemez-kéreg modell mellett strukturálisan ütköző célok** — csak egy nagyobb, plate-en belüli kevert-crust modell oldaná fel mindkettőt egyszerre. **Felhasználói döntés: a numerikus javítás elhalasztva, follow-up feladat.** Diagnosztika/mérőeszköz megmarad. | Alacsony (számítási) | Nagy (ha struktúrálisan javítjuk) | Igen |
| M4/UI | Tektonikus lemez overlay (deep time mozgás, lemezenkénti szín+név) | ✅ **KÓDBAN MEGVALÓSÍTVA (2026-09-13)** — felhasználói kérés. Új Core `PlatePresentation` (determinisztikus szín arany-arány hue-elosztással, `RandomDomain.Decorative`/`PlateColorHue=42`; név a meglévő `NameGeneration`-t újrahasználva `OceanicCrust`/`ContinentalCrust` utótag-kulcsokkal) - NINCS új szimuláció, a lemez-hovatartozás és -mozgás a MÁR MEGLÉVŐ `PlateGeneration.AssignPlate`+`PlateMotion.MovedSeeds`/`_adaptiveSeeds`-ből jön. Viewer: `PlanetGridMesh.TectonicOverlay.cs` (új partial fájl), `tectonicPlateOverlay` kapcsoló a `DrawLayersPanel`-ben, a MEGLÉVŐ szél-/csapadék-/hő-overlay-mintát követve (`ContinuousCornerColor`/`ContinuousWaterCornerColor` hook), kölcsönösen kizárva a többi overlay-vel MINDKÉT irányban (a korábbi wind/precip pár egyirányú hézagát is bezárva), gördíthető jelmagyarázat-doboz (szín+név+kéregtípus lemezenként). **Nem seed-törő** (a szín/név csak renderelési tulajdonság, a Decorative domain szándékosan a világmodell-garanciákon kívül van). 9 új Core-teszt, 441/441 zöld. Ld. `history/2026-09-13-tectonic-plate-overlay.md`. **Élő Unity Play-teszt hátra.** | Közepes | Közepes | Igen (élő ellenőrzés) |
| M8/M9/UI | Hierarchikus navigációs menü + breadcrumb (bolygó → kontinens → régió → terület) | Felhasználói kérés (2026-09-13). Play módban egy, a jelenlegi futásidejű panelekével (ld. "Deep time" doboz) egyező kinézetű navigációs menü: 1. szinten a kontinensek listája; kontinensre kattintva a kamera rázoomol, a menüben "Vissza" gomb jelenik meg, alatta a kontinens régiói listázva; régióra kattintva tovább zoom, "Vissza" gomb, alatta a régió kisebb területei (ÚJ, negyedik hierarchiaszint - jelenleg nincs Core-fogalma, névvel sem rendelkezik). Felül breadcrumb mutatja az útvonalat. Emellett a felhasználó kért egy javaslatot a MEGLÉVŐ futásidejű beállítás-panel (deep-time, erózió/szél/csapadék/felhő-overlay kapcsolók) csoportosítására/elhelyezésére, mert az új navigációs menü ugyanoda kerül vizuálisan. Részletes terv, nyitott kérdések és a panel-elrendezési javaslat a tábla alatti "Navigációs menü és panel-elrendezés — kidolgozott terv" szakaszban. **ÜTKÖZÉSVESZÉLY**: a párhuzamosan futó hőmérsékletmező-munka (ld. fenti sor, ND-100-104, 11/22. jóváhagyott döntés) SZINTÉN erre a közös panelre tervez új blokkot - a két munka layout-javaslatát össze kell hangolni, mielőtt a scene-fájl ténylegesen módosul. ✅ **Mockup jóváhagyva (2026-09-13)** - a felhasználó a Planet Navigator artifactot elfogadta 2 kiegészítéssel, mindkettő KÓDBAN MEGVALÓSÍTVA (ld. `history/2026-09-13-deep-time-step-and-camera-state.md`): (1) a Deep time léptetőgombokból hiányzó `100ky` lépés pótolva (teljes tizes-lépéskű sor `1y`-tól `100my`-ig, 9 gomb); (2) új "Kamera állása" doboz (nézetszint/magasság/nézetirány, a meglévő `PlanetOrbitCamera` állapotából) a Deep time doboz fölé, jobb felül. ✅✅ **Negyedik szint + teljes navigációs menü KÓDBAN MEGVALÓSÍTVA (2026-09-13)** - ld. `history/2026-09-13-navigation-menu-area-level.md` és `docs/01-architecture.md` §12 a teljes tervért/indoklásért. Core: `FeatureSegmentation.PartitionRegionIntoAreas` (tiszta BFS-particionálás, Python-referenciával bitpontosan egyező, 9 új Core-teszt, 393/393 zöld). Viewer: teljes `NavigationLevel`-állapotgép (Planet/Continent/Region/Area), valódi `GUI.Button`-breadcrumb + gördíthető lista + "Vissza" gomb, a jóváhagyott elrendezés szerint átrendezve (bal: Navigáció/Deep time/Rétegek; jobb: Kamera állása/Kiválasztott elem) - a `PlanetOrbitCamera.SuggestedAltitude` új segédfüggvénnyel konzisztens zoom-célmagasság minden szinten. **Tudatosan NEM módosítva**: a régi Canvas/TMP `WorldGenPanelUI` szöveglisták (most redundánsak) inaktiválása élő Unity-ellenőrzés nélkül kockázatos lett volna - ez azóta MEGTÖRTÉNT (2026-09-13: a kattintható listák és a hozzájuk tartozó kód törölve, a scene-beli szövegobjektumok inaktívak, ld. `history/2026-09-13-legacy-panel-removal.md`). **Élő Unity Game view ellenőrzés MÉG HÁTRA** (kattinthatóság mind a 4 szinten, lista-görgetés, panel-átfedésmentesség keskeny nézetnél) - kódból nem nyilvánítható késznek. | Közepes | Nagy | Igen (élő Unity jóváhagyás hátra) |

## Látható hegységek, természetes partok — M4/M10 és M13 (2026-09-12)

**Felhasználói igény, backlog; megvalósítás még nincs.** A hegységeknek
érzékelhető térbeli formával kell megjelenniük, miközben a partok alacsonyak
és természetesek maradnak, nem több száz kilométer magasnak látszó falak.
Az ND-88 fizikai skálája mellett tapasztalt lapos, pusztán színátmenetes
látvány sem elfogadott végeredmény. Egyetlen globális magassági szorzó
nem tudja külön szabályozni a hegyeket és a part menti magasságugrásokat.

Két összekapcsolt, de külön rétegben kezelendő feladat:

1. **Modellbeli part-/kéregátmenet (M4/M10):** az ND-90 a diszkrét
   óceáni/kontinentális bázisváltást folytonosította és 1 km-re korlátozta az
   uplift-komponenst. Élő ellenőrzés után csak akkor nyílik új Core-hangolás,
   ha a tényleges modellmagasság vagy a sáv szélessége továbbra sem természetes;
   rendereroldali magasságlevágás továbbra sem elfogadható.
2. **Domborzat érzékelhetősége árnyalással (M13):** a modellből származó
   lejtők/normálok és a világítás megjelenítésének vizsgálata, hogy fizikai
   vagy mérsékelt, explicit túlrajzolás mellett is felismerhetők legyenek
   a hegyvonulatok és völgyek. Nem kerülhet bele modellfüggetlen dekoratív
   domborzat; kerülendő a korábbi csillogási, tile-határ- és villanási regresszió.

**Elfogadás:** ugyanazon helyeken, több zoomszinten és megvilágításnál
ellenőrzött hegységek, völgyek és természetes partok; a helyi modellmagasság,
a tényleges kirajzolt magasság és az esetleges túlrajzolás külön azonosítható.
Ne változzon észrevétlenül a vízszintes km-lépték jelentése. Élő Unity-
vizuális és teljesítménymérés szükséges, a globális szorzó emelése önmagában
nem zárja le ezt az igényt. A partmagasság pontos mérőszámát és határértékét
a kapcsolódó Core-tételben kell tisztázni; nem minden part egységes levágása a cél.

Az igény felvétele nem módosítja automatikusan az aktuális tile/upload
feladatok sorrendjét, és most nem jár kód- vagy scene-változtatással.

## Pillanatnyi hőmérsékletmező és overlay — kidolgozott terv (2026-09-11)

**Állapot:** a hatókör és a 29 architekturális/termékdöntés felhasználó által
jóváhagyva; numerikus paraméterkalibráció és implementáció még nincs.
**2026-09-13:** az ND-62 ütközés rendezve (ND-99 fenntartva a testvérág
tengeri-jég döntésének), az öt döntés rögzítve: ND-100 hőmodell, ND-101
rács/idő/checkpoint, ND-102 széladvekció, ND-103 felszíntípus és autoritatív
határ, ND-104 Viewer-adatút/UI. Architektúra: `01-architecture.md` §11.
Baseline mérve (level 6: napi átlag gyorsúton 1,5 ms, Full 382 ms, szél
156 ms / teljes rács). 2. fázis elkezdve: level-6 rácsmetrika mérve,
paraméterforrások gyűjtve (`docs/reviews/thermal-parameters-sources-2026-09-13.md`,
10 modellválasztás megerősítésre vár), egycellás mérés: Crank–Nicolson-IMEX
javasolt, óránként középre igazított napi faktor. Gömbi advekció mérve:
kompenzált upwind fluxusforma élközépponti széllel. M1–M10 jóváhagyva.
**Implementálva (2026-09-13):** level-6 Python-referencia és vektorok (kétszeri
futás bájtra azonos), Core `SurfaceTemperatureField` és társai (22 teszt),
bázis-módosítás mérés alapján (M13, β = 0,5 radiatív simítás — a napi faktoros
bázis sarki éjszakán ~29 K-t adott), Viewer-overlay (Rétegek doboz: Ki /
Felszín / Levegő, jelmagyarázat, kurzor-bontás; R16 atlasz, shader-paletta).
Élő Unity-ellenőrzés hátra: `docs/06-user-verification-checklist.md` 15. pont.
Megerősítendő: M11–M13; öröklött korlát: egyenlítői óceán ~48 °C (§28 kalibráció).
Hátralévő fázis: autoritatív átállás (6.) és kétirányú szélcsatolás (7.) — külön döntés. Az
eredeti, rövid backlog-tétel az `experiment/full-temperature-model` testvérágon
jelent meg napi átlagos felszíni színnézetként. A felhasználói pontosítás ennél
nagyobb feladatot határoz meg: egy új, időben fejlődő hőmodell kell, amelynek az
overlay csak a gyors megjelenítő rétege.

### Jóváhagyott döntések — tételes jegyzék

Az interaktív döntéssor 2026-09-11-én lezárult. Az alábbi választások kötelező
tervezési bemenetek; megváltoztatásuk új döntésnapló-bejegyzést vagy a majdani
ND kifejezett újranyitását igényli.

| # | Jóváhagyott választás |
|---:|---|
| 1 | Két állapot készül: pillanatnyi felszínhőmérséklet (`Ts`) és felszínközeli levegőhőmérséklet (`Ta`); az alap overlay `Ts`, `Ta` külön diagnosztikai mód. |
| 2 | Első változatban egyirányú csatolás: a determinisztikus szél szállítja a levegő hőjét, a pillanatnyi hőmező még nem írja vissza a szelet. |
| 3 | Fix, egész bolygós level-6 szimulációs rács (`24 576` cella); a render-LOD nem módosíthatja az állapotot. |
| 4 | Egyetlen, egész tickes Core `SimulationTime`; a tickhossz CFL-/stabilitásmérésből jön, Unity `deltaTime` nem solver-lépés. |
| 5 | Lassú klímabázisból induló, determinisztikus spin-up és verziózott checkpoint/időbucket stratégia. |
| 6 | Legalább négy termikus felszíntípus: `Land`, `Ocean`, `Freshwater`, `Ice`, külön albedóval és hőtehetetlenséggel. |
| 7 | Az új hőmező először párhuzamos diagnosztikai modell; más fogyasztók csak külön validációs és modellverziós kapun állhatnak át rá. |
| 8 | Core-oldali `double` fizika, CPU-n determinisztikusan csomagolt hatlapos fixpontos hőtextúra; a GPU csak interpolál és palettáz. |
| 9 | Világításfüggetlen alaphőszín; domborzati hillshade csak külön opcionális kapcsoló. |
| 10 | Fix, abszolút, méréssel kalibrált °C-skála, kézi felülbírálással és külön 0 °C jelöléssel; nincs automatikus frame-min/max. |
| 11 | Az overlay azon a közös futásidejű panelen kap külön blokkot, ahol a deep-time, mozgás és tengelyforgási módok állíthatók. |
| 12 | A hősolver az overlay állapotától függetlenül, a közös idővel halad háttérben; a Viewer korlátozott frekvenciával vesz át kész snapshotot. |
| 13 | Forrásolt Föld-szerű alapparaméterek, verziózott és bolygónként felülírható konfigurációban; később átvezethetők generált atmoszféra-/anyagadatokra. |
| 14 | A jelenlegi procedurális `T_weather` kimarad az első dinamikus modellből; csak későbbi, külön validált perturbációként térhet vissza. |
| 15 | Konzervatív, upwind véges térfogatú levegő-hőadvekció, két sűrű pufferrel és CFL-alapú fix tickkel. |
| 16 | Az első modell olvassa a meglévő jég albedóját/hőtehetetlenségét, de még nem olvasztja vagy növeszti visszacsatoltan. |
| 17 | A jelenlegi felhő-/csapadékproxy nem árnyékolja a felszínt; sugárzási felhőhatás csak későbbi, valódi felhőoptikai mezőből jöhet. |
| 18 | A level-6 dinamika fölött determinisztikus magas-LOD magasságkülönbség-korrekció készül, a lapse-rate kettős alkalmazása nélkül. |
| 19 | Külön, ritkább determinisztikus `WindTick`; két kész wind snapshot között determinisztikus interpoláció megengedett. |
| 20 | Fizikai tick nem hagyható ki. Lemaradáskor az utolsó kész snapshot látszik időbélyeggel, és szükség esetén az időgyorsítás lassul; a főszál nem blokkol. |
| 21 | A diagnosztikai hőmező verziózott, eldobható cache/checkpoint; csak későbbi döntéssel válik autoritatív world state/hash részévé. |
| 22 | A közös panelen összecsukható, kurzorpont-alapú komponensbontás: `Ts`, `Ta`, bázis, besugárzás, napszög, hőcsere, advekció, keveredés, felszíntípus és magassági korrekció. |
| 23 | Cél: kész snapshotnál egy képkockás kapcsolás, 16,7 ms alatti főszálú csere/feltöltés és normál futásnál legalább 5 Hz kijelzési frissítés, élő gépen mérve. |
| 24 | A dinamikus modell előtt tételesen átvizsgáljuk és külön integráljuk a testvérág releváns Full-klímabázisát/cache-ét, az ND-62 ütközés rendezésével. |
| 25 | Kötelező Python referencia + KAT/tulajdonság-/determinizmustesztek + élő Unity vizuális és PerfLog-validáció. |
| 26 | A szél a lassú klímabázistól való levegő-hőanomália energiatartalmát szállítja, nem a teljes kalibrált klímabázist. |
| 27 | Az első modell a level-6 cella gömbi normálját használja a napsugárzáshoz; lejtőirány és hegyárnyék későbbi mikroklíma-réteg. |
| 28 | Előbb az upwind séma saját numerikus diffúzióját mérjük; explicit fizikai keveredés csak forrásolt célértékhez, szükséges mértékben kerül be. |
| 29 | Nem egy ernyő-ND készül, hanem öt egymásra hivatkozó döntés: hőmodell; rács/idő/checkpoint; széladvekció; felszíntípus/autoritatív határ; Viewer/adatút/UI/teljesítmény. Az azonosítók az ND-62 konfliktus feloldása után adhatók ki. |

A konkrét tickhossz, spin-up forgásszám, hőkapacitások, hőcsere-/keveredési
együtthatók, wind cadence és színskála-végpontok **nem maradtak termékdöntési
kérdések**: ezeket hiteles forrás, Python referencia, stabilitásvizsgálat és
mérés alapján kell meghatározni, majd a megfelelő ND-ben rögzíteni.

### Rögzített felhasználói cél

- Az alkalmazás futásidőben, teljes `Build()` nélkül, gyorsan kapcsolható
  `Temperature` nézetet adjon. A mező mind a hat cube-face-re, tehát a nappali
  és az éjszakai oldalra is készüljön el, ne csak az aktuálisan látható
  kamerakivágásra.
- Az alapnézet a **pillanatnyi felszínhőmérsékletet** mutassa `°C`-ban:
  kékes a hideg, pirosas a meleg tartomány; a színek ne legyenek puszta
  képernyőeffektek, minden mintának visszakövethető modellértéke legyen.
- A pillanatnyilag napsütötte oldal kapjon közvetlen besugárzást, az éjszakai
  oldal ne. A felszín hőtehetetlensége miatt a terminátor nem lehet azonnali,
  éles „meleg/hideg kapcsoló”: a délutáni maximum késhet, és az óceánnak
  lassabban kell melegednie/hűlnie, mint a szárazföldnek.
- A szél a **felszínközeli levegő** hideg/meleg állapotát szállítsa. Nem
  fizikailag helyes a talaj vagy a vízfelszín hőmérsékletét közvetlenül a
  széllel odébb tolni; a szállított levegő és a felszín determinisztikus
  hőcserével hasson egymásra.
- Ez globális, stabil klíma-/időjárás-proxy, nem teljes 3D CFD. Az első
  változat nem modellez függőleges légköri rétegeket, részletes frontokat,
  felhőárnyékot, óceáni áramlatokat vagy hegységek lokális önárnyékát. Ezek
  csak valódi modellbemenettel, külön későbbi fázisban kerülhetnek be.

### Miért nem elég a jelenlegi hőképlet

- A `Temperature.TemperatureKelvin` és a Full változat radiatív tagja 24
  mintából **egy teljes forgás napi átlagát** veszi. Emiatt ugyanazon a napon a
  nappali és éjszakai oldal azonos besugárzási átlagot lát; a kért hatást ebből
  sem új színskála, sem gyakoribb újrarajzolás nem tudja létrehozni.
- A `SunController.currentTimeDays` képkockánként halad és a fényt vezérli, míg
  a `PlanetGridMesh.climateDayT` külön szerializált mező és csak teljes rebuild
  során jut el a klímahívásokhoz. E két idő jelenleg elszakadhat, így a látható
  Nap és egy hőoverlay külön napszakot mutatna.
- A meglévő `WindPrecipitation.WindVector` napi átlagos hőgradiensből készül,
  és saját dokumentációja szerint még nyers `Math.Sin/Cos/Asin/Atan2/Tanh`
  függvényeket használ. Egy checkpointolt hőmező kritikus bemeneteként ez az
  I1 invariáns miatt nem vehető át változtatás nélkül.
- A jelenlegi `isOceanic` boolean nem tud különbséget tenni szárazföld,
  óceán, édesvíz és jég között. Egy egész bolygós felszínhőmodellhez legalább
  dokumentált `SurfaceThermalKind` kell, eltérő albedóval és hőtehetetlenséggel.

### Jóváhagyott modell: lassú klíma + két gyors hőanomália

Az új mező ne cserélje le vakon a már meglévő klímát. A lassú klímabázis adja
az évszakos/deep-time egyensúlyt; a solver az ettől való gyors eltérést
integrálja. Ez elkerüli, hogy a napi átlagos napsugárzást egyszer a bázisban,
majd még egyszer a pillanatnyi modellben is hozzáadjuk:

```text
Surface/Air climate baseline (Bs, Ba)
              +-----------------------------------+
solar anomaly>| surface anomaly θs               |<-- land/ocean/lake/ice hőtehetetlenség
              |             ^  hőcsere  v         |
wind -------->| near-surface air anomaly θa       |--> advekció + keveredés
              +-----------------------------------+
                     |                    |
                  Ts=Bs+θs             Ta=Ba+θa
                     |                    |
                     +---- temperature overlay ---+
```

- **`Bs`/`Ba` klímabázis:** a jóváhagyott Full modell napi/évszakos,
  magassági, üvegház- és deep-time komponenseiből származó lassú felszín- és
  levegő-célállapot. A magassági lapse-rate pontosan egy helyen szerepeljen.
  A meglévő procedurális `T_weather` nem adható még egyszer a dinamikus
  hőmezőhöz: az első változatból kimarad, majd később csak külön validált,
  dokumentált perturbációként vezethető vissza.
- **`Ts = Bs + θs`:** a talaj, óceán, tó vagy jég pillanatnyi „skin”
  hőmérséklete. A hajtás a pillanatnyi és a bázisban már elszámolt napi átlagos
  elnyelt sugárzás különbsége:
  `ΔQsolar = F*(1-albedo)*(max(0,n·s(t)) - dailyAverageFactor)`.
  A `SurfaceThermalKind` adja a felszíni fajhőt/hőtehetetlenséget.
- **`Ta = Ba + θa`:** a felszínközeli levegő pillanatnyi hőmérséklete. A
  felszínnel `Hsa = ksa*(θs-θa)` hőt cserél, a szél pedig első körben a
  levegőanomália energiatartalmát (`Ea = Ca*θa`) advektálja, nem a talajt és
  nem a már kalibrált teljes klímabázist. Előbb az upwind séma saját numerikus
  diffúzióját kell mérni; explicit, korlátos fizikai keveredési tag csak a
  forrásolt célértékhez szükséges mértékben kerülhet be.
- **Első referenciaegyenletek:**
  `Cs*dθs/dt = ΔQsolar - λs*θs - Hsa`, illetve
  `dEa/dt = Hsa - λa*θa - div(wind*Ea) + div(Ka*grad(θa))`.
  A `λs` hosszúhullámú visszaállító tag fizikailag levezethető a bázis körüli
  linearizálásból (`4*epsilon*sigma*Bs^3`), kizárólag szorzásokkal. A Python
  referencia rögzítse az energiamérleget és minden egységet (`J m⁻² K⁻¹`,
  `W m⁻²`, `m s⁻¹`, `K`). A pontos diszkretizáció és paraméter nem kerülhet
  emlékezetből a C#-ba: referenciaforrás, dimenzióellenőrzés, stabilitásmérés
  és KAT kell.

### Szélkapcsolat: két külön fázis

1. **Első szállítható változat — egyirányú csatolás (jóváhagyva):** a már létező,
   de előbb determinisztikussá tett és fix időpillanatra cache-elt szélmező
   szállítja `Ta`-t. A hőmező még nem írja vissza a szelet. Így már látható,
   mérhető meleg-/hideglevegő-transzport készül körkörös solver nélkül.
2. **Későbbi, külön ND — kétirányú csatolás:** `Ta` gradienséből nyomás-/termikus
   szélkorrekció készül, majd a szél ismét advektálja `Ta`-t. Ehhez iteráció,
   konvergenciakritérium és stabilitási bizonyítás kell; nem része az első
   implementációnak. A jelenlegi `WindVector -> napi átlag T-gradiens`
   összefüggést nem szabad csendben önmagába visszacsatolni.
3. **Szél időfelbontása:** a szél külön, a termikus ticknél ritkább,
   determinisztikus `WindTick` szerint frissül. Két teljes wind snapshot között
   azonos Core-időre számolt determinisztikus interpoláció engedett.

### Térbeli rács, idő és determinizmus

- Az első modell az ND-02 szerinti fix level-6 egészgömbös szimulációs rácson
  fusson: `6 * 64 * 64 = 24 576` cella, sűrű, kanonikus
  `(face,u,v)`/`TileId` sorrendű `double[]` mezőkön. A render-LOD nem
  változtathatja meg a hőállapotot vagy annak költségét.
- Ez nagyjából globális/szinoptikus, ~100–200 km-es lépték. Magasabb render-
  LOD-on csak modellből következő lokális korrekció (például finomabb
  domborzatból származó, egyszer alkalmazott lapse-rate) adható hozzá. Ha valódi
  mezoskálás advekcióhoz level 7/8 kell, az ND-02 tudatos újranyitása és külön
  memória-/teljesítménymérés szükséges.
- A jelenlegi négyirányú `TileNeighbors` jó topológiai alap, de fizikai
  transzporthoz új, verziózott rácsmetrika-cache kell: cellaterület, közös
  élhossz, középponttávolság és élirány/tangens. Egyenlő súlyok a cubed sphere
  torzulása miatt nem elfogadhatók.
- Az advekció konzervatív, szélirány szerinti upwind véges térfogatú fluxus
  legyen. Minden cella az előző, változatlan pufferből és a négy szomszédból,
  rögzített iránysorrendben számolja a következő értékét; nincs párhuzamos,
  sorrendfüggő `Dictionary +=` redukció. Két puffer cserélődik tickenként.
- Egyetlen Core-oldali, egész másodperces/tickes `SimulationTime` legyen az
  időforrás. A `SunController`, a szél, a hősolver,
  a hőoverlay és később az időcsúszka ugyanennek csak fogyasztói legyenek.
  Unity `Time.deltaTime` kizárólag a kívánt idősebességet gyűjtheti; közvetlenül
  nem lehet numerikus solver-lépés.
- A termikus tick hossza nem találomra rögzítendő. A Python modell számolja ki
  a legrosszabb cella/szél szerinti CFL-korlátot, a hőcsere/diffúzió stabilitási
  korlátját, majd ebből választ verziózott fix tick-et. `StateAt(t)` pontosan a
  szükséges kanonikus tickeket futtatja, két állapot között csak az ND-03
  szerint, HUD-on jelölt prezentációs interpoláció engedett.
- Kezdőállapotként a lassú klímabázisból származó `Ts/Ta` és dokumentált,
  egész forgásszámú determinisztikus spin-up készül. Nagy deep-time ugrásnál
  nem futhat le milliónyi napi tick: a cél-időbucket lassú klímabázisából új,
  fix hosszúságú spin-up készül, illetve kompatibilis checkpoint esetén abból
  folytatódik. Analitikus periodikus közelítés nem része az első változatnak.

### Gyors megjelenítés — a modell és a shader határa

- A Core számolja a teljes level-6 `double` hőmezőt. A Viewer csak a kész
  snapshotot veszi át; HLSL-ben nem lehet második napsugárzás-, szél- vagy
  hőképlet. A GPU kizárólag interpolálhat és Kelvin/°C értéket palettára
  képezhet, tehát megjelenít, nem szimulál.
- Jóváhagyott adatút: a 24 576 érték egy hat szeletes, face-enként 64×64-es,
  CPU-n explicit skálával fixpontosra kvantált scalar texture/bufferbe kerül,
  cube-face-enként egycellás, szomszédmezőből töltött gutterrel. Így a
  prezentációs bemeneti textúra is byte-reprodukálható; a GPU csak ezt
  interpolálja és színezi. A statikus/dinamikus terep és a víz mesh vertexei
  face-UV koordinátát kapnak; ugyanazt a snapshotot mintavételezik. Ez
  lényegesen olcsóbb, mint minden termikus ticknél több százezer magas-LOD
  vertex színét CPU-ról újratölteni.
- A solver háttérszálon, kettős snapshot-pufferrel fusson. A Viewer csak kész,
  időbélyegzett állapotot cserél be a főszálon; gyorsított időnél a köztes
  kijelzéseket összevonhatja, de a Core tickeket nem hagyhatja el. Félkész mező
  soha nem kerülhet a képernyőre.
- A hőmérséklet-overlay kapcsolása és a paletta változtatása nem indíthat
  `Build()`-et, hidrológiát, mesh-geometriát vagy folyó-finomítást. Az ND-50
  vizuális invalidációját általánosítani kell, a jelenlegi
  `_colorCacheWindMode` boolean helyett teljes overlay-/snapshot-/paletta-
  verziókulccsal.
- Jóváhagyott teljesítménycélok: kész snapshotnál a váltás egy renderelt
  képkockán belül történik; a snapshotcsere és GPU-feltöltés nem okozhat
  16,7 ms feletti főszálú képkockát; normál időhaladásnál a kijelzés legalább
  5 Hz-cel frissül. Hideg cache/deep-time ugrás közben időbélyegzett utolsó
  snapshot és „hőmező számítása” állapot látszik teljes `Build()` és főszálú
  blokkolás nélkül. A fizikai tick nem hagyható ki; tartós lemaradáskor az
  időgyorsítás lassul.

### Overlay és jelmagyarázat

- A mostani booleanok helyett egyetlen, kölcsönösen kizáró
  `SurfaceOverlayMode`: `None`, `SurfaceTemperature`, `AirTemperature`
  (diagnosztikai), `WindSpeed`, `Precipitation`. A régi szerializált booleanok
  migrációját tesztelni kell.
- Az alap paletta kék → cián → világos semleges → sárga → piros, fix abszolút
  `°C` skálával és külön 0 °C jelöléssel. Automatikus frame-enkénti min/max
  tilos, mert eltüntetné a valódi napszakos/évszakos változást. A default
  végpontokat több seed, évszak, nappal/éjszaka és spin-up utáni P1/P99 burkoló
  alapján kell kalibrálni; az Inspector-tartomány clampelhető.
- Az adatnézet alapból világításfüggetlen legyen. A jelenlegi
  `VertexColorUnlit` a neve ellenére Lambert + Blinn–Phong shadert használna,
  ezért a hideg éjszakai értéket még egyszer elsötétítené és meghamisítaná a
  jelmagyarázatot. Opcionális hillshade külön kapcsoló lehet.
- A panel mutassa a snapshot idejét/korát és a kurzor alatti bontást:
  `Ts`, `Ta`, klímabázis, pillanatnyi besugárzás, felszín–levegő hőcsere,
  advekció, keveredés, felszíntípus és magassági korrekció. Ez teljesíti a spec
  §87 „miért ilyen az érték?” követelményét.
- A kapcsoló, skála és összecsukható részletblokk azon a közös futásidejű
  panelen legyen, ahol a deep-time, a mozgás és a tengelyforgási módok is
  állíthatók; nem készül külön ideiglenes overlay-ablak.
- A terep, nyílt óceán, tó és jég ugyanabból a Core snapshotból kapjon színt.
  Folyó-, kráter-, felhő- és határvonal-rétegek nem írhatnak be saját
  hőértéket; tiszta diagnosztikai módban külön elrejthetők.

### Érintett helyek implementációkor

| Hely | Tervezett módosítás |
|---|---|
| `docs/04-decisions.md` | Az áganként duplikált ND-62 feloldása után új ND-k: kétállapotú hőmodell és paraméterforrások; fix tick/CFL és spin-up/checkpoint; egyirányú szélcsatolás; `SurfaceThermalKind`; render-adatút és skála. |
| `tools/reference/` + `testdata/` | Új Python hőtranszport-orákulum, mért paraméterkalibráció, stabilitás-/CFL-vizsgálat és determinisztikus vektorok. A verziózott JSON kézzel nem szerkeszthető. |
| `src/WorldGen.Core/Climate/Temperature.cs` | A lassú klímabázis komponenseinek egyértelmű, nem duplázó elérése; a testvérág cache-optimalizálását az ND-62 konfliktus rendezése után kell összevezetni. |
| új `src/WorldGen.Core/Climate/SurfaceTemperatureField.cs` (javasolt) | `Ts/Ta` dense snapshot, kétpufferes fix-tick solver, sugárzási/hőcsere-/advekciós/diffúziós tagok és magyarázó komponensek. |
| új Core rácsmetrika/idő helper (javasolt) | Verziózott area/edge/tangent táblák, kanonikus indexelés és egész tickes `SimulationTime`; Unity-referencia nélkül. |
| `src/WorldGen.Core/Climate/WindPrecipitation.cs` | Determinisztikus, cache-elhető szélbemenet; nyers transzcendens út megszüntetése vagy verziózott fix-rácsos előszámítása. Kétirányú coupling még nem. |
| `tests/WorldGen.Core.Tests` | KAT, energia-/uniformitás-/varrat-/stabilitás-, szekvenciális=párhuzamos-, fix-tick-, checkpoint- és paraméterhatás-tesztek. |
| új viewer időkoordinátor + `SunController.cs` | A fény, bolygóforgás és hőmező ugyanazt a Core-időt olvassa; `currentTimeDays` nem maradhat második autoritatív óra. |
| `PlanetGridMesh.cs` + külön hőoverlay shader/helper | `SurfaceOverlayMode`, UI/legend/debug értékek, snapshotkezelés, face-UV/scalar-buffer bekötés és `Build()` nélküli invalidáció. Shaderben csak mintavétel/interpoláció/paletta. |
| `PlanetView.unity`, `docs/06-user-verification-checklist.md` | Scene-bekötés, stale Inspector-értékek, vizuális és PerfLog acceptance. |
| CLI/perzisztencia/hash | Amikor a mező autoritatív világállapottá válik: modellverzió, checkpoint és state hash. Additív kísérleti mezőként előbb párhuzamosan futhat, de biome/jég/hidrológia bemenetét még nem írhatja át. |

### Implementációs fázisok és kapuk

1. **Döntés és baseline:** ND-62 ütközés rendezése; a két hőállapot,
   felszíntípusok, egyirányú szél, idő/tick és kezdeti állapot új ND-jei. A
   jelenlegi Simple/Full hő- és wind-path teljesítmény-/eloszlásmérése.
2. **Python referencia:** először forrásolt paraméterekkel kétcellás, 1D, majd
   teljes level-6 gömbteszt. CFL/stabilitás, energiaegyenleg, napi fáziskésés,
   szárazföld/óceán amplitúdó és széllel mozgó hőanomália mérése. Csak ezután
   készülhet tesztvektor.
3. **Core, még fogyasztók átírása nélkül:** dense `Ts/Ta` snapshot és fix-tick
   solver a régi napi átlagmező mellett. Ez a fázis még ne változtassa meg a
   biome-ot, jeget, csapadékot vagy world hash-t; előbb bizonyítani kell a
   stabilitást és a költséget.
4. **Közös idő és egyirányú szél:** a determinisztikussá tett wind snapshot és
   a Core `SimulationTime` bekötése; SunController ugyanazt az időt mutatja.
5. **Gyors overlay:** scalar buffer/texture, varratmentes mintavétel, unlit
   paletta, jelmagyarázat és komponens-debug. ND-50 szerinti külön vizuális
   invalidáció, teljes `Build()` nélkül.
6. **Autoritatív átállás külön kapun:** csak automata és élő validáció után
   dönthető el, hogy `Ts/Ta` mely biome-/jég-/párolgás-fogyasztókat váltja le.
   Ez seed-/modellverzió-, betöltési hiba-, checkpoint- és hash-frissítést
   igényelhet; nem része az overlay MVP csendes mellékhatásának.
7. **Későbbi coupling:** pillanatnyi `Ta` → nyomás/szél → `Ta` fixpont vagy
   időintegráció, külön ND-vel és konvergenciatesztekkel.

### Kész-definíció

- Kontrollált, szél nélküli esetben a napsütötte oldal melegebb az éjszakainál,
  a maximum a hőtehetetlenség miatt a helyi dél után jelentkezik, és az óceán
  napi amplitúdója kisebb a szárazföldénél. Nulla besugárzásnál a rendszer
  fizikailag ésszerű alsó tartomány felé hűl, nem ugrik 0 K-re.
- Széllel egy lokalizált `Ta` anomália a szélirányba mozdul és a felszínre
  hőcserével hat; nulla széllel nem mozdul. Forrás/süllyesztő nélküli
  transzportnál az energia a dokumentált numerikus tolerancián belül megmarad;
  konstans mezőt sem az advekció, sem a cube-face átlépés nem változtat meg.
- Azonos seed, modellverzió és Core-idő ugyanazt a `Ts/Ta` snapshot-hash-t adja
  eltérő frameszám, render-LOD, kamera, tile-bejárási sorrend, illetve
  szekvenciális/párhuzamos futás mellett. NaN/Infinity nem jelenhet meg.
- A Nap fénye és a hőoverlay terminátora ugyanabból a
  `SunDirectionBodyFrame(time)` eredményből következik. A napi ciklus láthatóan
  halad; pause/scrub után nincs időszétcsúszás.
- A hőtérkép szárazföldön, óceánon, tavon és jégen teljes, mind a hat face-en,
  planet/continent/region zoomon varrat- és LOD-pop nélkül. A shader által
  mintázott érték a Core snapshot/interpoláció eredménye; nincs külön HLSL
  fizika.
- `None`/`SurfaceTemperature`/`AirTemperature`/`WindSpeed`/`Precipitation`
  közül pontosan egy aktív. A skála, °C és 0 °C látszik; a paletta nem skálázódik
  újra kamera- vagy időmozgáskor, és a világítás nem hamisítja meg az adatszínt.
- Overlay-váltáskor nincs `Build()`/hidrológia/mesh-geometria/folyó-finomítás.
  A teljesítménykapu a fenti baseline-nal mérve teljesül, az aszinkron mezőcsere
  nem mutat félkész állapotot vagy főszálú akadást.
- Python referencia + újragenerált, byte-egyező vektorok, teljes solution és
  mindhárom tesztprojekt zöld. Élő Unity Editor képernyőkép, vizuális napszak-
  és szélteszt, valamint friss PerfLog nélkül a tétel nem jelölhető késznek.

## Navigációs menü és panel-elrendezés — kidolgozott terv (2026-09-13)

**Állapot:** felhasználói kérés rögzítve, tervezés folyamatban, implementáció
még nincs. ND-szám szándékosan **nincs** kiosztva ebben a lépésben: a
`docs/04-decisions.md` fájlt ugyanebben az időpontban egy másik feladat (a
pillanatnyi hőmérsékletmező, ND-99–104) aktívan szerkeszti a közös
munkafában - a konfliktus elkerülése érdekében az ND-kiosztás az
implementáció megkezdésekor, friss fájlállapot mellett történik.

### Cél

Play módban egy, a meglévő "Deep time" panellel **azonos vizuális stílusú**
navigációs menü, ami a bolygó → kontinens → régió → terület hierarchiában
enged lefelé zoomolni kattintással, felfelé pedig "Vissza" gombbal és egy
breadcrumb-sorral. A kattintás-a-névre → kamera-repülés mechanizmus MÁR
LÉTEZIK (a terv írásakor a `WorldGenPanelUI` kattintható listája - az a
panel 2026-09-13-án megszűnt, helyette a navigációs menü -,
`PlanetOrbitCamera.FlyToDirection`,
`ContinentPanelData`/`RegionPanelData.CenterDirection`) - ez az alap, amit a
menü kiterjeszt egy explicit szintállapot-géppel (jelenleg csak egy opcionális
`viewLevelText` mutatja a legközelebbi nevet, nincs "vissza" navigáció és
nincs negyedik szint).

### Hierarchiaszintek és a hiányzó negyedik szint

| Szint | Meglévő Core-forrás | Állapot |
|---|---|---|
| Bolygó | `WorldPanelData` | Kész (M8) |
| Kontinens | `ContinentPanelData`, `NameGeneration.GenerateName` | Kész (M8), névvel |
| Régió | `RegionPanelData`, `FeatureSegmentation.ClassifyLandform` | Kész (M8), névvel |
| **Terület** (a régió "kisebb területei") | **Nincs** | Új Core-fogalom kell |

A negyedik szint ("terület") jelenleg NEM létezik sem adatmodellben, sem
névben - ezt kérte a felhasználó explicit módon ("a régió egyes kisebb
területei is... ezeknek is kell külön megnevezést adni"). Ez tehát nem tiszta
Viewer-feladat: a `FeatureSegmentation`-t egy harmadik, finomabb
particionálási szinttel kell kiegészíteni (pl. a régió tile-jainak
tovább-klaszterezése, hasonló módszerrel, mint kontinens→régió), és a
`NameGeneration.GenerateName`-t (már paraméteres `featureId`-re, ld.
`src/WorldGen.Core/Features/NameGeneration.cs`) erre a szintre is meg kell
hívni, gondoskodva arról, hogy a terület-`featureId`-k ne ütközzenek a
kontinens-/régió-szint azonosítóival ugyanabban a `RandomDomain.Naming`
hívásban. **Ez docs-first Core-munka**: előbb `docs/01-architecture.md` §2
bővítése egy "Terület" panelszinttel, utána Python-referencia
(`features_ref.py`) egyeztetés, majd C#. Nem seed-törő, ha új, eddig nem
használt featureId-tartományt kap (a meglévő kontinens/régió nevek bitre
változatlanok maradnak).

### UI-állapotgép

- Egy explicit `NavigationLevel` enum (`Planet`/`Continent`/`Region`/`Area`)
  és egy "kijelölt lánc" (aktuális kontinens-/régió-/terület-index), NEM csak
  a legközelebbi név kitalálása kameratávolságból (a jelenlegi
  `viewLevelText` heurisztikája erre nem elég pontos egy explicit menühöz).
- Lista-elem kattintás → `PlanetOrbitCamera.FlyToDirection` a gyerek
  `CenterDirection`-jére, ÉS a `NavigationLevel` eggyel lejjebb lép.
- "Vissza" gomb → `FlyToDirection` a szülő (vagy a bolygó-nézet) felé, a
  szint eggyel feljebb lép. A kamera-repülés célpontja/magassága ugyanaz a
  mechanizmus, ami már működik kattintásra - nem kell új kamera-logika, csak
  a hívás iránya fordul meg.
- Breadcrumb: `Bolygó / <kontinens neve> / <régió neve> / <terület neve>`,
  minden korábbi szegmens is kattintható (egyenes ugrás arra a szintre).
  MEGVALÓSÍTÁSKOR ETTŐL ELTÉRTÜNK: a menü OnGUI-ban valódi `GUI.Button`-okra
  épült, nem a `WorldGenPanelUI` padded-link mintájára (az a kattintható
  panel 2026-09-13-án megszűnt) - így sem új
  `EventSystem`/`GraphicRaycaster`-függés, sem padded-link kompromisszum nem
  kellett.

### Panel-elrendezési javaslat (a kért "hova kerüljön a képernyőn")

A jelenlegi "Deep time" doboz (bal felső sarok, sötét félig-átlátszó háttér,
egyszerű checkbox-lista + egy szám-input/Alkalmaz gomb) és a kontinens/régió
szöveglista (jelenleg háttér nélkül, közvetlenül a 3D nézet fölé úsztatva)
KÉT KÜLÖNÁLLÓ vizuális elem - az új navigációs menü ne öntsön mindent egy
dobozba, hanem különítse el funkció szerint, azonos stílussal (sötét doboz,
fehér szöveg, checkbox-sor, kis betűméret):

| Zóna | Tartalom | Indoklás |
|---|---|---|
| **Bal felső, 1. doboz — Navigáció** | Breadcrumb sor + kattintható lista (kontinensek/régiók/területek) + "Vissza" gomb | Ez az elsődleges, gyakran használt interakció - a jelenlegi kontinens/régió szöveglista helyére kerül, de DOBOZOLVA (a jelenlegi háttér nélküli, a 3D nézet fölé úsztatott szöveg olvashatósági probléma is egyúttal - sötét doboz alatta javítja). |
| **Bal felső, 2. doboz — Deep time** | Változatlan (Idő, Gyors kézi beállítás, Alkalmaz) | Ne mozgassuk el egy már megszokott, működő elemet feleslegesen. |
| **Bal felső, 3. doboz — Rétegek/overlay-ek** | A jelenlegi 8 checkbox (Erózió, Szél-overlay, Tavak+jég, Kráterek, Csapadék-overlay, Felhők, Felhő-sodródás) + **fenntartott hely a jövőbeli hőmérséklet-overlay blokknak** (ld. a hőmodell-terv 11./22. jóváhagyott döntése - ugyanerre a panelre tervez saját, összecsukható blokkot) | Ma egy dobozban van a deep-time-mal keverve; szét kell választani "időlépték" és "mit lássak a felszínen" fogalmilag, hogy a hőmérséklet-blokk később ide illeszkedjen anélkül, hogy a deep-time dobozt kelljen újra átrendezni. |
| **Jobb felső, kiválasztott elem panelje (World/Continent/Region adatok)** | A meglévő `WorldGenPanelData` számok (terület, biome, folyók stb.) | Ez MARADJON adatpanel, ne navigáció - a navigáció (bal oldal) és az adatmegjelenítés (jobb oldal) elválasztása egyértelműbbé teszi, hogy melyik dobozra kell kattintani navigáláshoz. |

A három bal oldali doboz egymás ALATT, függőlegesen rendezve (nem egymás
mellett) - ez illeszkedik a jelenlegi képernyő-elrendezéshez (a "Deep time"
doboz ma is bal felül van, a checkbox-sor alatta). A Navigáció doboz kerüljön
LEGFELÜLRE (leggyakoribb interakció), utána Deep time, legalul a Rétegek.

**Nyitott kérdés (felhasználói jóváhagyást igényel implementáció előtt):**
maradjon-e mindhárom doboz egyszerre látható (jelenlegi minta), vagy legyen
a Navigáció doboz összecsukható/lebegő, hogy mélyebb hierarchiaszinteken
(hosszú terület-lista) ne nyomja le a képernyőt? Javaslat: a lista görgethető
legyen egy max-magasság fölött, doboz-összecsukás nélkül (kevesebb új UI-
állapot, konzisztens a meglévő egyszerű stílussal).

### Fázisterv

1. **Terület-szint Core-tervezés** (`docs/01-architecture.md` §2 bővítés,
   Python-referencia, C# `FeatureSegmentation`/`NameGeneration` kiterjesztés,
   KAT-vektorok) - a döntés a hőmérséklet-munka `docs/04-decisions.md`
   szerkesztésének lezárása UTÁN kap ND-számot.
2. **Panel-adatmodell bővítés**: `AreaPanelData` (a `RegionPanelData` mintáján),
   `WorldGenPanelData.Areas` lista.
3. **Navigációs állapotgép + UI**: `NavigationLevel`, lista/vissza-gomb/
   breadcrumb rendering a meglévő padded-link kattintás-mintával, a fenti
   panel-elrendezés szerint dobozolva.
4. **Élő Unity vizuális ellenőrzés**: kattintás minden szinten, vissza-gomb,
   breadcrumb-ugrás, hosszú lista görgetése, stílus-egyezés a Deep time
   dobozzal.

### Kész-definíció

- Mind a négy szint (bolygó/kontinens/régió/terület) neve a Core-modellből jön
  (I4), kattintható, és a kamera a megfelelő `CenterDirection`-re repül.
- "Vissza" gomb minden nem-bolygó szinten működik, a breadcrumb minden
  szegmense kattintható.
- A navigációs doboz vizuálisan a Deep time dobozzal egyező stílust követ
  (szín, betűméret, elhelyezkedés-logika).
- A meglévő Deep time/overlay-checkbox funkciók VÁLTOZATLANOK maradnak -
  csak a dobozolás/csoportosítás változik.
- Élő Unity Play-teszt: mind a négy szint közti oda-vissza navigáció
  vizuálisan megerősítve, nincs regresszió a meglévő kattints-a-névre
  funkción.

## Ebben a munkamenetben lezárt tételek (referenciaként)

**M4 (geológia):** ND-36 domain warping, ND-37 part-magasság, ND-38
térfogat-tengerszint.

**M13 Fázis 1 (folytonos árnyalás) + teljesítmény, MA véglegesítve,
felhasználó megerősítette — "a zoom és a rotáció is szuper":**
- Folytonos, sarkonként interpolált, varrat nélküli felszín-/víz-szín
  (nem tile-egészre-egyenletes).
- Ideiglenes, fix irányú Lambert-árnyékolás (a domborzat láthatóvá tétele).
- Mesh-objektumok újrahasznosítása új példány helyett (GPU-akadás javítása).
- Cache-méret ellenőrzése a statikus alapréteg lábnyoma ellen
  (cache-thrashing javítása).
- Sarok-szín áthelyezve a párhuzamos előkészítő menetbe (egyszálú
  költség kiküszöbölése).
- **KRITIKUS:** biztonsági levélszám-korlát (`DefaultMaxLeafCount=200_000`)
  a kvadfában — egy éles esetben 6 349 914 tile-t termelt egy cut,
  30-60+ másodperces lefagyást okozva; a korlát strukturálisan
  kizárja ennek megismétlődését (a gyökér-geometriai ok, ld. fenti
  M9 sor, még nyitva van, de a KATASZTRÓFA már nem fordulhat elő).
