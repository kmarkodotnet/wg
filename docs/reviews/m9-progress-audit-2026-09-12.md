# M9 — Haladási és ráfordítás-becslés újraalapozása

## Miért nem változott a jelentett szám?

A felhasználó jogosan jelezte, hogy sok egymást követő átadás ugyanazt a
„60–70%; 8–20 óra” sort kapta. A milestone-napló ND-80–91 bejegyzései
ezt ténylegesen ismétlik. Nem készült hozzájuk új súlyozás, feladatbontás
vagy tényleges időnyilvántartás. Ez jelentési hiba volt.

Másrészt az elvégzett munka nagy része az eredeti cél előfeltétele:
olcsóbb chunkok, víz-LOD, leválasztott feltöltés/publikáció, mérés és
kamerabiztonság. A fő felhasználói minőségi cél — korai, minden szükséges
helyen jelentkező részletesség, sima átmenet — nem lett elfogadva, sőt
a felhasználó kifejezetten halasztotta. Ez indokolja, hogy az elfogadottság
kevésbé nőtt, mint az implementált funkciók száma; nem indokolja az
ellenőrizetlen százalék/óraszám másolását.

**A régi sávot aktuális előrejelzésként visszavonjuk.** Nem helyettesítjük
automatikus, promptonkénti százaléknöveléssel. Az alábbi új bázis nem
hasonlítható közvetlenül a korábbi becsléshez: kisebb értéke nem új regresszió.

## Ellenőrzött hatókör és pontozás

Forrás: `docs/05-milestones.md` M9 cél, szűkített hatókör és 9.1–9.5;
az aktív backlog, ND-69–91 átadások és a tényleges viewer-források/tesztek.
Branch `codex-handoff`, HEAD `0f9cd4b`, további nem commitolt fejlesztések.
A teljes M13 látvány, új mikro-domborzati frekvenciák és a Core ND-90
nem része ennek az M9-mutatónak. A jóváhagyott kamera/lépték-kiegészítések igen.

Súlyok a cél fontosságát jelzik, nem fájl-, teszt- vagy ND-darabszámot.
Pontozás: 0 = nincs megkezdve; 25 = diagnózis/prototípus, lényegi célhiány;
50 = részben implementált/tesztelt, lényegi hiány marad; 75 = implementált
és részleges működési bizonyíték, teljes elfogadás nélkül; 100 = a csoport
kritériumai igazoltak és az élő cél elfogadott. Csoporton belül nem a
legkészebb részfeladatot vetítjük ki az egészre. Ez szakmai becslési rubrika,
nem objektív fizikai mérés; a súlyok változtatását külön dokumentálni kell.

| Munkacsoport | Súly | Állapotpont | Súlyozott pont | Bizonyíték / hiány |
|---|---:|---:|---:|---|
| Terep-kiválasztás, fedés, közös élek | 25% | 75 | 18,75 | Kvadfa/budget/coverage/stitch tesztek, ND-70 pozitív zoom-visszazoom; teljes szélsőhelyzet-elfogadás nincs |
| Korai részletesség és folyamatos átmenet | 25% | 25 | 6,25 | Ismert késői osztás és proxy/geometria eltérés, több elutasított próba; halasztott fő minőségi cél |
| Reakcióidő, főszálköltség és erőforráskeret | 20% | 50 | 10,00 | ND-80/81/85–94 implementált; ND-93 ürítés és ND-94 staging élő loggal igazolt. Teljes reakcióidő, memória/FPS és puha keret túllépése nyitott |
| Víz és kapcsolódó rétegek LOD-együttállása | 15% | 75 | 11,25 | ND-82/83/86 kód, tesztek és aktív víz a logban; part/világváltás teljes vizuális elfogadása hiányzik |
| Navigáció, FlyTo és felszínközeli kamerabiztonság | 10% | 50 | 5,00 | ND-91 pontminta és ND-95 teljes útvonalú FlyTo magasság implementált; near-plane oldalak, tavak, közeli élő próba hátra |
| Tényleges rajzdiagnosztika és km-lépték | 5% | 75 | 3,75 | ND-75 mesh-mérés, ND-84 lépték és ND-95 kontextusvédelem/monotón kérésóra; teljes élő pontossági ellenőrzés hiányzik |
| **Összesen** | **100%** | | **55,00** | **Kb. 55%, durva bizonytalansági sáv: 50–60%** |

Az „implementált” nem „elfogadott”, de nem is nulla előrelépés. A következő
jelentésben a megváltozott csoportot és bizonyítékot kell megnevezni.
Ha egyik csoport állapotpontja sem változik, azt kell írni: „új részfeladat
elkészült, elfogadási kapu még nem zárult”, nem új sávot kitalálni.

## Új élő bizonyíték az auditkor

`unity/WorldGenViewer/Logs/PerfLog_20260912_125145.txt`, 39 commit,
113 staging-szelet; kvantilisindex `floor((n-1)*p)`:

- Commit medián/p90/max: 0,92 / 2,18 / 4,25 ms.
- Teljes kérés: 336,0 / 594,6 / 890,3 ms.
- Staging-szelet: 0,85 / 1,67 / 7,10 ms — tehát a 2 ms nem kemény korlát.
- ND-91: 213 modellforrású kameranapló, nincs fallback ezekben. Minimum
  modellfelszín feletti magasság 25,29264 egység; minden naplózott kifelé
  korrekció nulla. **Nem történt a 0,33-as kamerakorlátot elérő próba.**
- Kamera-lekérdezések ablakonként összesített ideje: medián 0,216,
  p90 0,279, max 1,710 ms. Nem egyedi mintaköltség vagy frame-idő.
- Utolsó ND-75 méretminta: p50 7,72 / p90 8,82 / max 11,03 px. Egyetlen
  állapot nem bizonyítja a teljes zoomtartomány egyenletes részletességét.

Ez részleges működési bizonyíték, nem a felhasználó helyetti vizuális elfogadás.
Nem kontrollált A/B teljesítményteszt és nem indokolja a minőségi csoport
automatikus felpontozását. A korábbi 9 Core-vektorhiba másik munkaszál
köztes állapotában jelentkezett; az ND-90 history már zöld újrafutást rögzít,
az auditban külön friss Core-futtatás is **384/384 PASS** eredményt adott
(Debug, 1 perc 57 másodperc). A régi kilenc hiba már nem aktuális blokkoló.
Ez ellenőrzés, nem ebben a munkaszálban elvégzett Core-javítás vagy M9-munka.

## Hátralévő munka — tételes új alapbecslés

**14:02-es próba visszamérése:** [ND-93/94 élő eredmény](lod-resource-staging-live-2026-09-12.md).
Az erőforráscsoport új bizonyítéka 1568 ürített kulcs és a túllépés
lecsengése; az upload 256 szeletben működött. A csoport pontja 50 marad,
mert a teljes reakcióidő és memória/FPS-elfogadás nem teljesült. Az
upload/cache sorból a számlálós visszamérés kész, natív memória-/főszál-
ellenőrzés és esetleges korrekció marad; a durva 1–2 órás keret még
indokolt. A teljes 12–24 órás sáv ezért ebben az elemzési körben nem
változik. A 26,975 px / 9,635 px eltérés a minőségi csoport nyitottságát
is megerősíti; nem indokol pontemelést.

Fejlesztői ráfordítás-egyenérték, **nem mért munkaidő**, nem naptári határidő.
A felhasználói próbák közötti várakozás nincs benne. A már elvégzett órák
utólag hitelesen nem rekonstruálhatók; a régi részbecsléseket nem összegezzük
tényként. A következő tartományok a jelenleg ismert hiányokat fedik le:

| Hátralévő csomag | Durva ráfordítás | Lezárási feltétel |
|---|---:|---|
| Maszkhiba utáni terep/víz-helyreállítás (ND-92) | Implementáció elkészült; kivezetve a maradékból | Reprodukció és szűk javítás kész; Editor-futtatás a végső regressziós kapuban marad |
| Víz/kamera/lépték célzott élő ellenőrzése és kisebb korrekciói | 1–3 óra | ND-95 FlyTo/lépték korrekció implementált; part, minimumzoom, világváltás és lépték élő elfogadása hátra; nagyobb geometriavédelem külön újrabecslendő |
| Mozgókamerás selection/balance/emisszió késésének csökkentése | 4–8 óra | Azonos útvonalon mért kérés- és felzárkózási idők, elfogadható reakció |
| Proxy/geometria hiba és korai finomodás | 3–6 óra | Tényleges tile-méret + felhasználói élesség/átmenet-elfogadás |
| Upload/cél-cache visszamérés és korrekció (ND-93/94 implementált) | 1–2 óra | Időkeretes inaktív cache és kétfázisú staging élő memória-/főszálvizsgálata; nagy natív job továbbra is kockázat |
| Összevont regresszió és végső vizuális/performance kapu | 2–4 óra | Több tereptípus, oda-vissza zoom, nincs lyuk/pattanás; elfogadott teljes út |
| **Aktuális összesen az ND-95 korrekciói után** | **11–23 óra** | Nem tartalmaz új Core/mikroterep/M13 fejlesztést |

**ND-95 frissítés:** [három korrekció és közös ellenőrzés](lod-navigation-measurement-batch-nd95-2026-09-12.md).
Monotón kérésóra/fázisok, lépték-kontextusvédelem és teljes úton helyi
magasságú FlyTo került kódba. 11 új .NET-eset futott, 17 új Editor-eset
csak fordított. A navigáció/diagnosztika csoportok pontjai változatlanok,
mert élő elfogadási szintet nem léptünk. A kisebb kamera/lépték korrekciók
egy része elkészült: e sor 2–4 → 1–3 óra, összes maradék 12–24 → 11–23.
Ez friss szakmai durva becslés, nem mért idő kivonása vagy garantált határidő.

**Következő csomag frissítése (ND-93/94):** az inaktív cache kezelése és
mesh-életciklusa, valamint a külön ütemezett mesh/rendercél implementált.
[Kilenc új .NET-eset és közös próbamenet](lod-resource-staging-batch-nd93-94-2026-09-12.md).
Az upload/cache sor 2–4 órás implementációs/korrekciós keretből 1–2 órás
visszamérési/korrekciós maradékra változott; a teljes maradék 13–26-ról
12–24 órára csökkent. Nem mért időt vontunk le. A reakcióidő csoport
50-es pontja még nem emelhető: a mozgókamerás selection és a teljes
élő elfogadás hiányzik. Súlyozott M9 kb. 55%, a két új részfeladat külön kész.

**Korábbi, ND-92-es frissítés:** az audit kezdetén 14–28 óra volt a
tételes alapbecslés, ebből az ND-92 1–2 órás ráfordítás-egyenértékű
implementációs csomagja elkészült. Ezért a változatlan többi sor összege
most 13–26 óra. Ez nem mért idő levonása. Az Editor-futtatás nincs késznek
számolva; a végső regressziós keret része. A hibabiztonsági részjavítás
nem zárta le az általános teljesítmény- vagy vizuális kaput, ezért az új
súlyozott bázis továbbra is kb. 55%; az elvégzett részfeladat külön látható.

A két korábban halasztott élességi/selection-csomag az összes hátralévőben
szerepel, de e becslés nem aktiválja őket. További felhasználói döntésig
nem módosítjuk küszöbeiket. Ha az elfogadáshoz új algoritmus vagy teljes
mesh-kameravédelem szükséges, annak feltárásakor új becslés kell, nem a sáv
csendes kitolása. A táblázat az aktuális csomag lezárásakor frissítendő.
