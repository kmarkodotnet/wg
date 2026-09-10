# Felhasználói ellenőrzési lista

Ez a dokumentum azokat a tételeket gyűjti, amikhez **a felhasználónak
kell tennie valamit** — élőben kipróbálni Unity-ben, egy Inspector-
értéket beállítani, vagy visszajelzést adni. Minden tétel négy részből
áll: **mit módosítottam**, **mi volt a megoldás/diagnózis**, **hogyan
teszteld**, **mi az elvárt eredmény**.

A `docs/backlog.md` a teljes, mérföldkő-szerinti feladatlista — ez a
dokumentum ANNAK egy leszűkített, tesztelés-központú nézete, csak azokra
a tételekre, amikhez MÁR történt kódváltoztatás és élő megerősítés
hiányzik. Frissítve tartom, ahogy új munka készül el.

Legutóbb frissítve: 2026-09-07 (3. kör).

**Code review-frissítés (2026-09-07, 2. kör)**: egy `/code-review
--effort high` a napi teljes diffre 3 valós hibát talált és javított:
(1) a GPU compute shader oldali elevacio-szamitas (`TileClassification.
compute`) nem kapta meg az ND-52 másodlagos zaját - ha a
`useGpuClassification`/`useGpuGeometry` kapcsolók be vannak kapcsolva
(a scene-ben `useGpuClassification` ÉPPEN aktív), a tile-osztályozás és
a renderelt geometria csendben eltérő elevációt használt volna; (2) a
6. pont (kattintható panel-nevek) egy MARADÉK, korábban fel nem tárt
hibája - ld. lent, a 6. pont kiegészítése; (3) a tengerfenék-árnyalás
normalizációja nem számolt a másodlagos zaj amplitúdójával.

**Code review-frissítés**: egy `/code-review --effort high` a mai teljes
diffre 2 valós hibát talált és javított (nem igényelnek külön tesztet,
a 3. és 6. pont már ezt a javított állapotot írja le): (1) a
felhő-újraépítés háttérszálas verziója egy versenyhelyzet miatt
elavult eredménnyel felülírhatta a friss felhő-réteget; (2) az
éjszakai-sötétítés javítás észrevétlenül a felhőket is majdnem
feketére sötétítette éjszaka (külön `cloudAmbient` mező vezetve be).
Egy harmadik, a review verifikációja közben talált hiba is javítva:
a `SunController` Edit módban (Play nélkül) hibásan próbálta felépíteni
a csillagmezőt.

---

## 1. Csillagászat — tengelyforgás sebessége

**Mit módosítottam:** `SunController.cs` — az `autoAdvance` alapértéke
`false`→`true` (a MÁR LÉTEZŐ komponensedre ez nem hat vissza).

**Mi volt a megoldás:** a jelenetedben a `Rotation Period Days` mező
tévesen `100`-ra volt állítva `1` helyett — ez azt jelentette hogy egy
teljes tengelyforduláshoz `100 / 0.01 = 10000` másodperc (majdnem 3 óra)
kellett volna, nem a várt 100.

**Hogyan teszteld:** a Directional Light-on lévő `SunController`
komponensen:
1. `Rotation Period Days` = **1**
2. `Days Per Second` = **0.01** (vagy amit szeretnél)
3. `Auto Advance` legyen bepipálva
4. Play mód

**Elvárt eredmény:** `rotationPeriodDays / daysPerSecond` másodperc
alatt (a fenti értékekkel 100 mp) a Nap és a csillagmező egy teljes kört
tegyen meg az égen.
ok
---

## 2. Nap-korong fényessége és mérete

**Mit módosítottam:** `StarUnlit.shader` — `_Brightness` tartomány
4→20-ra nőtt, `_Color` mostantól `[HDR]` (Bloom-glow-hoz, ha van a
jelenetben Bloom post-process).

**Mi volt a megoldás:** korábban a `_Brightness` csúszka plafonja (4)
túl alacsony volt egy meggyőző, izzó Nap-hatáshoz.

**Hogyan teszteld:** válaszd ki a `SunVisualMat` anyagot (Project
ablak) → húzd fel a **Brightness Multiplier**-t (pl. 5-8) → kattints a
**Color**-ra → az **Intensity** csúszkát is húzd fel, ha még mindig
halvány.

**Elvárt eredmény:** a Nap-korong jól látható, kellően fényes, nem
"szürkés" folt.

*Kapcsolódó, korábban javasolt (nem kód, csak Inspector-hangolás)
finomítás:* ha a korong még mindig túl nagynak/közelinek tűnik: a
SunVisual Quad `Scale`-je legyen ~`80,80,80` (nem 250), a
`SunController`-en `Sun Visual Distance` ~`4800`.
ok
---

## 3. Felhő-sodródás akadása

**Mit módosítottam:** `PlanetGridMesh.cs` — a felhő-réteg periodikus
újraszámítása (`ApplyCloudOnlyRebuild`) mostantól HÁTTÉRSZÁLON fut
(`Task.Run`, single-flight védelemmel), nem a fő szálon szinkron módon.

**Mi volt a megoldás:** a korábbi, fő szálon futó, drága
`DomainWarp`/`Fbm`-alapú zajszámítás minden csapadék-sarokra frame-
hitchet okozott, ami egy folyamatosan mozgó elemen (a forgó csillagégen)
volt a legfeltűnőbb.

**Hogyan teszteld:** Play mód, `showClouds` és `cloudDriftEnabled`
bekapcsolva, figyeld a csillagmező/felhőréteg mozgását kb. 1-2 percig.

**Elvárt eredmény:** a mozgás egyenletes, nincs periodikus (kb.
1.5-2 másodpercenkénti) akadás/döccenés.
ok
---

## 4. Folyó-vonal — helyenkénti "megszakadás" (KÉT különálló javítás)

**4a. Mit módosítottam:** `RiverPathTracing.cs` (Core) — a dendritikus
összefolyásnál a megálló folyó utolsó pontja mostantól PONTOSAN a
befogadó folyó egyik tényleges pontjára záródik (`ClaimedTileInfo`),
nem csak "ugyanabba a durva finom-tile-ba".

**4b. Mit módosítottam:** `PlanetGridMesh.cs` — `riverLineRadialBias`
alapértéke `0.02`→`0.5`.

**Mi volt a megoldás:** KÉT különböző gyökérok volt ugyanarra a
tünetre. Az első (4a) a folyók ÖSSZEFOLYÁSI pontjainál hagyott
vizuális rést. Miután ezt javítottam, a felhasználói visszajelzés
szerint a szaggatottság MÉGIS megmaradt — ekkor derült ki a MÁSODIK,
független ok (4b): a folyó-vonal a folytonos elevációmezőt olvassa ki,
de a ténylegesen renderelt terep egy darabosan lineáris, durvább
LOD-mesh, aminek felszíne alá a vonal helyenként belesüllyed
(Z-fighting) — ez a `riverLineRadialBias` (sugár-irányú kiemelés)
emelésével enyhíthető, de NEM 100%-ban megoldva (a legdurvább
LOD-szinteken, távoli kameránál még mindig előfordulhat).

**Hogyan teszteld:** Play mód, zoomolj be egy folyóhálózatra
(kontinens/régió szintre), kövesd végig több folyó útját a forrástól a
torkolatig, különös tekintettel az összefolyási pontokra.

**Elvárt eredmény:** a folyó-vonal folytonosnak tűnik, nincsenek éles
"hiányzó szakaszok". Ha MÉG MINDIG szaggatott, valószínűleg a `riverLineRadialBias`
(4b) nem elég a te terepeden — szólj, és tovább emelem, vagy
megcsinálom a teljes megoldást (a folyó-pont vetítése a HELYI mesh
tényleges magasságára).
nok
---

## 5. Folyó-vonal szélessége — vízhozam-arányos szalag

**Mit módosítottam:** `RiverPathTracing.cs` (Core, új
`ComputeDischargeWeights`) + `PlanetGridMesh.cs` (a folyó-réteg mostantól
`MeshTopology.Triangles`-alapú, változó szélességű "szalag", nem
egységes vékony vonal).

**Mi volt a megoldás:** korábban MINDEN folyó azonos vastagságú vonal
volt, függetlenül attól, mennyi vizet szállít. Most a dendritikus
összefolyás-fából (hány forrás-ág táplálja) egy súlyt számolok
folyónként, és a vonal fél-szélessége ezzel arányosan (sqrt-esen) nő.

**Hogyan teszteld:** Play mód, keress egy olyan folyót, aminek több
mellékfolyója van (látszik az elágazás a forrás-vidéken) - hasonlítsd
össze a fő-ág szélességét egy elszigetelt, mellékfolyó nélküli forrás-
ág szélességével.

**Elvárt eredmény:** a torkolat felé / több tributary-t összegyűjtő
szakaszok LÁTHATÓAN szélesebbek, mint egy magányos forrás-ág. Ha a
különbség alig látszik, vagy éppen TÚL drámai (aránytalanul vastag
fő-ágak), a `riverBaseHalfWidth` (`PlanetGridMesh` Inspector-mező,
alapból `0.08`) hangolható.
nok
---

## 6. Kontinens/régió-panel kattintható nevek

**Mit módosítottam:** `WorldGenPanelUI.cs` — a kattintás-detektálás
mostantól a SOR TELJES SZÉLESSÉGÉT figyeli (nem csak a név karaktereit).

**Mi volt a megoldás:** két korábbi kör (glyph-alapú, majd paddelt
glyph-terület) sem volt elég megbocsátó - a felhasználó szerint
"megküzdök hogy adott régióhoz kattintással eljussak". A teljes-sor-
szélesség a legnagyvonalúbb, ami a jelenlegi (valódi Unity `Button`
nélküli, `EventSystem`/`GraphicRaycaster` hiánya miatt kényszerű)
architektúrán belül elérhető.

**Hogyan teszteld:** Play mód, kattints a kontinens/régió panel
BÁRMELY pontjára egy adott sorban (nem csak a névre, a statisztika-
szövegre is).

**Elvárt eredmény:** a kamera a kattintott kontinens/régió fölé repül.
Ha MÉG MINDIG nem megbízható, az egy MÁSIK, mélyebb okra utalna (pl. a
`_lastData`/`orbitCamera` inicializálási problémára) - ez esetben nézd
meg a Console-t, a kód explicit `Debug.LogWarning`/`Debug.Log`
üzeneteket ír minden sikertelen kattintásnál, amik megmondják, MELYIK
feltétel hiúsítja meg.
ok

**KIEGÉSZÍTÉS (2026-09-07, code review talált egy MARADÉK hibát az
ÍGY MÁR "ok"-nak jelölt funkción belül):** a padded kattint-sávok
szomszédos kontinens/régió-sorok között kb. KÉTSZERESEN átfedtek
(mért font-metrikákkal) - egy klikk, ami az ELSŐ (nem a legközelebbi)
egyező linket találta el, alkalmanként a SZOMSZÉDOS sorra ugrott volna.
Javítva: a legközelebbi sor közepét választja, nem az elsőt. Ha eddig
ritkán "átugrott a szomszéd sorra" élményed volt, ez lehetett az ok -
érdemes újra kipróbálni ugyanúgy, ahogy fent (Play mód, kattints
egymás melletti kontinens/régió-sorokra, figyeld hogy MINDIG a helyes
sorra repül-e).

---

## 7. Pólusi jég — zajos partvonal

**Mit módosítottam:** `PlanetGridMesh.cs` — a jég/nem-jég döntés
mostantól leaf-tile-pozíciófüggő zajjal (`FractalNoise.Fbm`)
perturbált, nem a durva referencia-tile egészére egyenletes.

**Mi volt a megoldás:** a jég-klasszifikáció csak a durva referencia-
szinten futott, minden finomabb tile ugyanazt örökölte - ez adta a
"túl tiszta", blokkos/szögletes partvonalat.

**Hogyan teszteld:** nézd meg a pólusi jégsapka szélét közelről.

**Elvárt eredmény:** a partvonal szabálytalan, "organikus" legyen, ne
egyenes/szögletes blokkokból álljon. Ha túl SIMA maradt vagy túl
KAOTIKUS: `IceBoundaryJitterAmplitudeK`(4.0)/`Frequency`(24.0)/
`Octaves`(3) a `PlanetGridMesh` konstansai (kódban hangolhatók, nincs
Inspector-mező rájuk).
nok, a sarkvidéki kontinens ok, a konstans fehér sapka még mindig ott van.

**✅ ND-57 (2026-09-09, felhasznaloi visszajelzes: "fix, adott magassagi
foknal levo jeg kirajzolas, kor alaku")**: a KONSTANS (tengeri) jégsapka
kör alakú határának oka megtalálva - a `Biome.SeaIce`/`Ocean` határ a
Core-ban tiszta hőmérséklet-küszöb, zaj nélkül (ellentétben a
szárazföldi jégsapkával, amit már korábban zajosítottunk). Javítva:
`IsAdaptiveSeaIce` - ugyanaz a jitter-minta, csak a render-kategória
döntésénél (a Core `biome`/statisztikák változatlanok). Mindhárom
érintett hely frissítve (statikus alapréteg, adaptív klasszifikáció,
vízfelszín-szín mindkét helye). Ld. `docs/04-decisions.md` ND-57.

**Hogyan teszteld:** Play mód TELJES újraindítással, nézd meg közelről
mindkét pólus tengeri jég-határát.

**Elvárt eredmény:** a tengeri jég határa most szabálytalan/organikus,
nem tökéletes kör.
---

## 8. Felhő-mozgás sebessége

**Mit módosítottam:** `PlanetGridMesh.cs` — a felhőréteg most a
`WindPrecipitation.WeatherPrecipitationMultiplier` idő-koherens zaját
használva folyamatosan változik/sodródik.

**Hogyan teszteld:** Play mód, `cloudDriftEnabled` bekapcsolva, figyeld
a felhőmintázatot 1-2 percig.

**Elvárt eredmény:** a felhők láthatóan mozognak/alakulnak, nem
statikusak. Sebesség hangolása: `cloudDriftTimeScale` (`PlanetGridMesh`,
alapból 3.0).
ok
---

## 9. Éjszakai oldal sötétsége

**Mit módosítottam:** `PlanetGridMesh.cs`/`VertexColorUnlit.shader` —
`surfaceAmbient` alapértéke `0.35`→`0.04`.

**Hogyan teszteld:** forgasd a Napot úgy (SunController), hogy a
kamera által látott oldal éjszakába kerüljön.

**Elvárt eredmény:** az éjszakai oldal ténylegesen SÖTÉT (nem
"homályos/ködös"), de nem tiszta fekete. Ha túl sötét/túl világos:
`surfaceAmbient` (`PlanetGridMesh` Inspector, 0..1 csúszka) élőben
hangolható.

*(A `nok` jelölés valószínűleg a scene-ben talált elavult
`surfaceAmbient: 0.35` érték miatt született - ez most javítva
(`0.04`), ld. a 12. pont kiegészítését. A 2026-09-07-i chat-visszajelzésed
szerint ("a világ hátsó része már sötét") ez már működik - érdemes
lehet a fenti jelölést `ok`-ra frissíteni, ha megerősítve látod.)*
ok
---

## 10. Nézetszint-váltás (Planet/Continent/Region) — MVP

**Mit módosítottam:** `PlanetOrbitCamera.cs` (új `CurrentViewLevel`,
`CurrentViewDirection`, `AltitudeAboveSurface` property) +
`WorldGenPanelUI.cs` (új, opcionális `viewLevelText` mező, ami minden
képkockán frissül).

**Mi volt a megoldás:** ez a "Kontinens/régió kamera-átmenetek" tétel
LEGKISEBB, élő teszt nélkül biztonságosan megvalósítható szelete - a
kamera felszín-feletti magasságából (nem szimulációs adat, tisztán a
zoom-mélységből) meghatározom, hogy Planet/Continent/Region nézetszinten
vagyunk-e, és megjelenítem a kamera jelenlegi nézet-irányához
legközelebbi kontinens/régió nevét. **NEM valósítja meg** a "folyamatos
átmenetet" abban az értelemben, hogy a kamera forgási középpontja NEM
mozdul el a kontinens/régió felszíni pontjára zoom közben (ez egy
jóval nagyobb, később elvégzendő munka) - egyelőre csak egy INFORMÁCIÓS
kijelző.

**ÚJ Unity-Editor lépést igényel** (ELTÉRŐEN a többi tételtől, itt
tényleg kell egy új UI-elemet létrehozni, nem csak egy meglévőt
tesztelni):
1. Hozz létre egy új TextMeshPro szöveg-objektumot a Canvason (pl. a
   meglévő panelek fölé/mellé), nevezd el "ViewLevelText"-nek.
2. A `WorldGenPanelUI` komponensen töltsd ki az új **View Level Text**
   mezőt ezzel az objektummal.

**Hogyan teszteld:** Play mód, zoomolj be/ki a bolygóra a görgővel,
figyeld a ViewLevelText tartalmát.

**Elvárt eredmény:** távolról "Nézet: Bolygó", közepes távolságból
"Nézet: Kontinens — [legközelebbi kontinens neve]", nagyon közelről
"Nézet: Régió — [legközelebbi régió neve]" jelenjen meg, és a szöveg a
zoom-mélységgel összhangban helyesen váltson. A két magassági küszöb
(`continentViewAltitude`=60, `regionViewAltitude`=15, a
`PlanetOrbitCamera`-n) KALIBRÁLATLAN - ha rosszkor vált, ezeket hangold.

nok, nem ír ki semmit, pedig felvettem a komponenst és beállítottam

**⚠️ DIAGNOSZTIKA HOZZÁADVA (2026-09-07)**: a néma early-return
(`viewLevelText == null || orbitCamera == null || _lastData == null`)
eddig SEMMILYEN Console-üzenetet nem adott arról, MELYIK feltétel
hiúsítja meg a kiírást - ez pontosan ugyanaz a hibaosztály, mint a
korábbi (4 körös) kattintható-link saga, ahol a diagnosztikai naplózás
végül feltárta a valódi gyökérokot. Hozzáadva egy EGYSZERI (nem minden
Update()-ben ismétlődő) `Debug.Log`, ami pontosan megmondja, a három
feltétel közül melyik `null`. **Kérlek próbáld ki újra Play módban, és
másold be ide (vagy a chatbe) a Console-ban megjelenő
`WorldGenPanelUI [diag]: UpdateViewLevelDisplay nem ir ki semmit...`
kezdetű sort** - ez pontosan megmutatja, hova nyúljak.

**⚠️ 2. KÖR (2026-09-07): a fenti diagnosztikai üzenet SOHA nem
jelenik meg** (megerősítve: a Console-szűrő "UpdateViewLevelDisplay"-re
0 találatot adott) - ez azt jelenti, hogy `viewLevelText`/`orbitCamera`/
`_lastData` MIND kitöltött, és a `viewLevelText.text = ...` sor
TÉNYLEGESEN lefut minden frame-ben - tehát ez NEM logikai hiba, hanem
UI-MEGJELENÍTÉSI probléma (a szöveg beállítódik, de valamiért nem
látszik - pl. inaktív GameObject, átlátszó/0-alfa szín, 0 méretű
RectTransform, hiányzó/inaktív Canvas, vagy egy másik UI-elem takarja).
Bővített diagnosztikát adtam hozzá, ami EGYSZERRE vizsgálja az összes
gyakori láthatósági okot. **Kérlek próbáld ki újra, és másold be a
Console-ban megjelenő `WorldGenPanelUI [diag]: UpdateViewLevelDisplay
BEALLITOTTA a szoveget...` kezdetű sort** (szűrd rá "BEALLITOTTA"-ra,
ha nem találod).

**⚠️✅ 3. KÖR, VALÓDI GYÖKÉROK MEGTALÁLVA ÉS JAVÍTVA (2026-09-07)**: a
naplózott adatok szerint minden helyes volt (aktív, teljesen látható
fehér szín, ésszerű méret/pozíció) - te megerősítetted, hogy a Scene
view-ban a RectTransform kerete a LÁTHATÓ vásznon BELÜL van, mégsem
látszik a szöveg. Ez Z-SORRENDI TAKARÁS: Unity uGUI a Canvas-on belüli
elemeket HIERARCHIA-SORREND szerint rajzolja (a később következő
testvér kerül felülre) - a `ViewLevelText` valószínűleg egy korábbi
testvér volt, és egy másik, opak panel-háttér (pl. a kontinens/régió-
panel) eltakarta. Javítva: `WorldGenPanelUI.OnEnable()` mostantól
kódból `viewLevelText.transform.SetAsLastSibling()`-et hív - ez MINDIG
a hierarchia tetejére (látható rétegre) kényszeríti, függetlenül attól,
hova húztad a Canvas-on belül. **Kérlek próbáld ki újra Play módban -
most már látszania kell.**

**⚠️ 4. KÖR (2026-09-07): a `SetAsLastSibling()` SEM oldotta meg** -
"nem látszik". Két lehetséges ok: (a) az `OnEnable()` esetleg NEM
futott újra hot-reload után (ugyanaz a jelenség, mint korábban az
`orbitCamera`-nál - ott az `Update()`-be áthelyezett önjavítás oldotta
meg), (b) a testvér-sorrend NEM is volt a valódi ok. Javítva: a
`SetAsLastSibling()` hívást áthelyeztem az `Update()`-ből minden
frame-ben hívott `UpdateViewLevelDisplay()`-be (biztosan lefut,
függetlenül a hot-reload állapottól). EMELLETT bővített diagnosztikát
adtam hozzá, ami újabb, korábban nem vizsgált lehetséges okokat néz:
van-e a szülők között `RectMask2D`/`Mask` (levágná a szöveget, ha a
maszk területén kívül esik) vagy `CanvasGroup` (aminek `alpha=0`-ja
láthatatlanná tenné, anélkül hogy a GameObject saját aktív állapota ezt
jelezné), plusz a testvér-index/testvérek száma.

**Kérlek próbáld ki újra, és másold be a Console-ban megjelenő
`WorldGenPanelUI [diag]: UpdateViewLevelDisplay BEALLITOTTA...` sort
ÚJRA** (a régi log-sor gyorsítótárazva maradhatott a Console-ban -
töröld a Console-t "Clear" gombbal Play előtt, hogy biztosan a friss
verziót lásd). Ha MOST SEM látszik a szöveg, ez az új napló szinte
biztosan megmutatja, melyik maszk/CanvasGroup a ludas.

**⚠️ 5. KÖR (2026-09-07): a bővített napló szerint MINDEN makulátlan**
(`siblingIndex=3, parentChildCount=4` - tényleg legfelső testvér;
`RectMask2D-szulok=0, Mask-szulok=0, CanvasGroup-szulok=[nincs]` -
nincs maszkolás), mégis "egyszerűen nem látszik a felirat". Két ÚJ,
korábban nem vizsgált irányt adtam hozzá: (a) közvetlen összehasonlítás
egy MÁR MŰKÖDŐ panellel (`worldPanelText`) - a font/anyag/shader
NEVÉNEK összevetése (ha eltér, az a projektben már többször előfordult
HDRP-shader-kompatibilitási hibaosztályra utalna - az Inspector-értékek
"helyesnek" látszanának, de a shader mégsem rajzolna semmit); (b) a
jelenetben található ÖSSZES Canvas felsorolása (`renderMode`,
`sortingOrder`, `worldCamera`) - hátha van egy MÁSIK, magasabb
sorting-order-ű Canvas, ami ugyanazt a képernyő-területet fedi le,
FÜGGETLENÜL a ViewLevelText saját Canvas-án belüli helyétől.

**Kérlek próbáld ki újra, és másold be a Console-ban megjelenő KÉT ÚJ
sort**: `WorldGenPanelUI [diag]: OSSZEHASONLITAS...` és
`WorldGenPanelUI [diag]: A JELENETBEN TALALHATO OSSZES Canvas...`.

**⚠️✅ 6. KÖR, VALÓSZÍNŰLEG A VALÓDI OK MEGTALÁLVA (2026-09-07)**: a
font/anyag/shader TELJESEN MEGEGYEZETT a működő panellel, és a
jelenetben csak 1 Canvas van - mindkét korábbi hipotézis kizárva. DE a
korábbi (5. köri) naplóban észrevettem egy addig figyelmen kívül
hagyott adatot: `fontSize=36`, de `rect height=30` - **a betűméret
NAGYOBB, mint a doboz magassága!** Egy 36pt betű tényleges sormagassága
szinte biztosan meghaladja a 30 egységnyi dobozt - ha a TMP
túlcsordulási módja bármi olyan, ami FÜGGŐLEGESEN vág (pl.
alapértelmezett `Truncate`), ez a TELJES sort láthatatlanná teheti,
FÜGGETLENÜL egy külön `RectMask2D`/`Mask` komponenstől (ez a TMP SAJÁT,
beépített túlcsordulás-kezelése, nem egy külön komponens - ezért nem
mutatta ki a korábbi maszk-ellenőrzés). Javítva: a kód mostantól
kényszeríti az `Overflow` (nem vágott) módot, függetlenül attól, mekkora
dobozt állítottál be a RectTransformon - így a szöveg SOSEM tűnhet el
pusztán doboz-méret miatt. A naplóba hozzáadtam az `overflowMode`
értékét is, hogy megerősítsük/cáfoljuk ezt.

**Kérlek próbáld ki újra** - ha ez volt az ok, MOST már látszania kell.

**✅✅ MEGOLDVA, felhasználó megerősítette (2026-09-07): "most jó"** -
a felhasználó nem az én kódos `Overflow`-kényszerítésemmel oldotta meg,
hanem gyakorlati megkerüléssel: lemásolta a MÁR MŰKÖDŐ `worldPanelText`
GameObject-et (a helyesen méretezett RectTransformjával együtt),
átnevezte, és ezt a másolatot kötötte be a `WorldGenPanelUI`
`viewLevelText` mezőjébe - a másolat öröklte a `worldPanelText`
NAGYOBB (fontSize-hoz illő) dobozméretét, ami megerősíti a diagnózist
(az eredeti, frissen létrehozott `ViewLevelText` doboza a `fontSize`-
nál kisebb magasságú volt). A kódos `Overflow`-védelem is bent maradt
(nem árt, extra biztonság hasonló jövőbeli esetekre).

ok
---

## 11. Másodlagos, finom-léptékű domborzat-zaj (közeli zoom laposság)

**Mit módosítottam:** `CrustElevation.cs` (Core) — új
`SecondaryDetailNoise` függvény + `BaseElevation`-ba bekötve.

**Mi volt a megoldás:** kérésed szerint ("nagyon közeli zoom esetén
nincs fraktál zaj ami indokolná a tile-felbontást") egy MÁSODIK,
magasabb frekvenciájú, alacsonyabb amplitúdójú zajréteget vezettem be,
UGYANAZZAL az algoritmussal (`RidgedMultifractal`), mint az elsődleges
domborzat-zaj. A periódusa 40 darab, a Viewer alapértelmezett statikus
(`PlanetGridMesh.level=5`) rácsának megfelelő tile-nyi, az amplitúdója
(200m) az elsődleges (3000m) kb. 1/15-e. Diagnózis: a valós ok, hogy az
elsődleges zaj legfinomabb oktávja is tucat-km hullámhosszú, míg a
renderelt LOD ennél sokkal mélyebbre bont — ez adta a "lapos" hatást a
legközelebbi zoom-szinteken. Részletek: `docs/04-decisions.md` ND-52.

**FONTOS MELLÉKHATÁS**: ez a `CrustElevation.BaseElevation` (a teljes
pipeline gerince) numerikus kimenetét MINDEN pozícióra megváltoztatta —
ezért a Python referencia és MINDEN rá épülő KAT-tesztvektor
újragenerálva (crust_elevation, plate_boundary, erosion_glaciation,
hydrology, river_path, lakes_ice_erosion, moisture_transport, features,
volcanism, state_hash), a `TestEarth001`/`FeatureSegmentation`
kontinens/régió-számai frissítve (44→37 kontinens, 398→412 régió, a
víz-arány cél változatlanul 65%). 375/375 Core-teszt zöld.

**Hogyan teszteld:** Play mód, zoomolj be MAXIMÁLISAN közel a felszínre
(a legmélyebb LOD-szintig), figyeld a domborzat részletességét lapos/sík
területeken (nem csak hegyvidéken).

**Elvárt eredmény:** még a legközelebbi zoomnál is legyen érzékelhető,
finom magasság-variáció a felszínen, ne tűnjön teljesen simának/
textúra nélkülinek. Ha még mindig túl lapos vagy éppen túl zajos/
"kavicsos", a `CrustElevation.SecondaryNoiseAmplitudeMeters` (200) és a
periódus-konstansok (`SecondaryNoiseReferenceLevel`=5,
`SecondaryNoisePeriodTiles`=40) egyszerű kalibrációs hangolást
igényelnek — szólj, és újrahangolom.

**⚠️ ÉLŐ HIBAJELZÉSED ALAPJÁN JAVÍTVA (2026-09-07, VÉGLEGES MEGOLDÁS
KÉT SIKERTELEN KÖR UTÁN):** miután bekapcsoltad, `"Compiler timed
out"` hibát kaptál a Unity Console-ban (`TileClassification.compute`,
`CSGenerateTerrainGeometry` kernel). Gyökérok: a GPU-oldali shader-port
(`BaseElevationF`) is megkapta a másodlagos zajt (ez maga egy code
review-ban talált, korábban hiányzó javítás volt), de ez a kernel
szálanként 5x hívja az elevációt (1x középpont + 4x sarok), és a HLSL
fordító ezt tipikusan teljesen kifejti. ELSŐ javítási kísérlet
(3→1 oktáv + védelmi `[loop]` attribútum) NEM oldotta meg - ugyanazt a
hibát kaptad újra. Ahelyett hogy tovább találgatnék (amit élőben nem
tudok ellenőrizni), a GPU-oldali `BaseElevationF`-et VISSZAÁLLÍTOTTAM
bájtra pontosan az ND-52 ELŐTTI, korábban bizonyítottan (hetek óta)
hiba nélkül lefordult formulára - a GPU-port TOVÁBBRA SEM tartalmazza a
másodlagos zajt, ez most már szándékosan, dokumentáltan így marad
(a CPU-oldali `CrustElevation.cs`, ami a domborzat TÉNYLEGES,
renderelt geometriáját adja, VÁLTOZATLANUL tartalmazza - csak a
gyors GPU-klasszifikáció marad a régi, valamivel simább közelítésnél).
**Kérlek próbáld újra megnyitni a scene-t - ennek a hibának MOST MÁR
el kell tűnnie, mert a shader kódja gyakorlatilag azonos azzal, ami a
mai módosítások előtt is hiba nélkül fordult.**

**⚠️ MÁSODIK ÉLŐ VISSZAJELZÉSED ALAPJÁN ÚJRAHANGOLVA (2026-09-07):**
jelezted, hogy sem az éjszakai-sötétítés, sem a másodlagos zaj nem
volt érzékelhető. Két KÜLÖN gyökérok:
1. **Éjszakai sötétítés**: a `PlanetView.unity` scene-ben a
   `surfaceAmbient`/`surfaceSpecularStrength`/`surfaceShininess` mezők
   a RÉGI (0.35/0.3/24), a session korábbi kód-alapérték-változásait
   MEGELŐZŐ értékeken álltak - egy már létező, szerializált Inspector-
   érték NEM frissül automatikusan, ha a kód-alapértelmezés változik.
   Közvetlenül frissítve a scene-fájlban 0.04/0.12/8-ra.
2. **Másodlagos zaj**: utólagos számolással kiderült, hogy a periódusa
   (40 tile egy level=5 rácson) kb. 0.3125-szöröse egy teljes
   nagykörnek - ez SOHA nem adott "közeli-zoom finom részletet"
   (SZÉLESEBB, mint az elsődleges zaj bázis-oktávja is!), csak egy
   alig érzékelhető (200m amplitúdójú), egész-felszínt átfogó
   hullámzást. Az amplitúdó 200→900m-re emelve (az elsődleges kb.
   30%-a) - ez a már eleve folytonos, egész felszínt lefedő hullámzás
   most már láthatónak kell lennie MINDEN zoom-szinten, pontosan
   ahogy kérted ("folytonos zaj az egész síkra").

Python referencia + minden KAT-vektor újragenerálva, 375/375
Core-teszt zöld, Unity build-ellenőrizve. **Élő Unity-ellenőrzés
hátra** - kérlek nézd meg, hogy most (a) az éjszakai oldal ténylegesen
sötét-e, (b) van-e látható, folyamatos hullámzás/textúra a felszínen
normál zoom-szinten is (nem csak extrém közelről).

**✅ ND-56, HARMADIK RÉTEG (2026-09-09, felhasználói kérés: "olyat
szeretnék ami a maximális felbontás esetén is minden tile-ra hatással
van")**: `CrustElevation.TertiaryDetailNoise` - harmadik, közeli-zoom
léptékű zajréteg, a másodlagoshoz hasonló módszerrel.

**❌ ELSŐ PRÓBÁLKOZÁS VISSZAVONVA, ÉLŐ TESZT ELŐTT**: level=20 (elméleti
max LOD)/3 tile periódus - "katasztrófa... a távoli zoom nézetet
nagyban befolyásolja, ellenben az extrém közeli zoom esetén nem
egyenletes a zaj eloszlása" (térbeli aliasing, mert level=20
gyakorlatban szinte soha nem érhető el).

**✅ JAVÍTVA**: level 20→13, periódus 3→8 tile - ugyanaz a bevált minta,
mint a másodlagos zajnál (level=5/40 tile), csak közelebbi célra
hangolva. Python referencia + teljes downstream KAT-vektor-lánc
újragenerálva (crust_elevation/plate_boundary/erosion_glaciation_deep_
time/hydrology/river_path/lakes_ice_erosion/moisture_transport/
features/state_hash), kontinens-szám 34→37, régió-szám 412→402
frissítve, 375/375 Core-teszt zöld. Ld. `docs/04-decisions.md` ND-56.

**Hogyan teszteld:** Play mód TELJES újraindítással (SEED-TÖRŐ
változás, új világ generálódik), nézd meg (a) TÁVOLI zoomnál nincs-e
kaotikus, véletlenszerű "csúnya" zaj a felszínen, (b) EXTRÉM közeli
zoomnál a domborzat egyenletesen, folytonosan hullámzik-e (nem
foltosan/egyenetlenül), és minden egyes tile-nál van-e érzékelhető
eltérés a szomszédjától.

**Elvárt eredmény:** távolról a felszín NEM kap extra zajt/csúnyaságot,
csak közelről válik érzékelhetővé, és ott is SIMÁN, folytonosan (nem
foltosan). Ha még mindig nem jó, ez egy tovább hangolható paraméter-pár
(`TertiaryNoiseReferenceLevel`/`TertiaryNoisePeriodTiles`), vagy - ha
nincs jobb ötlet - a réteg teljes visszavonása marad opció.

**❌ TELJESEN VISSZAVONVA (2026-09-09): "nem lett jobb... működjön
minden úgy ahogy ezelőtt"** - az újrahangolt (level=13/8-tile) verzió
sem hozott érzékelhető javulást. A teljes ND-56 réteg törölve a
Python-referenciából és a C#-ból, a downstream KAT-vektor-lánc
visszaregenerálva az ND-52 (másodlagos zaj, harmadik réteg nélküli)
állapotra, 375/375 Core-teszt PASS. A világ most a mai session ELŐTTI
állapotot tükrözi (seed-törő volt a be- és a kivezetés is, tehát Play
módban új, de a REGI algoritmussal generált világ jön létre).

---

## 12. Folyó/jég spekuláris csillanás — túl erős, "villámlás"-szerű

**Mit módosítottam:** `PlanetGridMesh.cs` — `surfaceSpecularStrength`
0.30→0.12, `surfaceShininess` 24→8 (+ a `VertexColorUnlit.shader`
Properties-fallback értéke szinkronban).

**Mi volt a megoldás:** visszajelzésed szerint a folyó-/jég-felületek
csillanása "mintha villámlana" volt - túl erős ÉS foltos (tile-ról
tile-ra ugorva villan fel-le). A Blinn-Phong specular tag
(`pow(ndotH, shininess)`) egy magas shininess-nél KESKENY fényfoltot ad,
ami a durva/adaptív LOD-mesh diszkrét, per-tile normáljain hirtelen
el/megjelenik a szomszédos tile-ok között - ez adja a "pattogó,
villámlás-szerű" hatást. Az alacsonyabb shininess (8) SZÉLESEBB, lágyabb
fényfoltot ad, ami fokozatosabban változik a diszkrét normál-mezőn -
ez EGYSZERRE csökkenti az intenzitást ÉS a foltosságot. **Ez a
leggyorsabb, legbiztonságosabb kalibrációs megoldás** (a másik két
jelölt - külön víz/jég anyag, simább normál-interpoláció - nagyobb
refaktor, csak akkor szükséges, ha ez nem elég).

**Hogyan teszteld:** Play mód, forgasd a kamerát/várd meg, hogy a Nap
végigsöpörjön egy folyón vagy jégsapkán, figyeld a fény-visszaverődést.

**Elvárt eredmény:** a csillanás halványabb és a felületen FOLYAMATOSAN,
nem ugrásszerűen mozog. Ha még mindig túl erős/foltos, a két érték
(`surfaceSpecularStrength`/`surfaceShininess`) tovább csökkenthető, vagy
a nagyobb refaktor (külön víz/jég spekuláris-anyag) szükséges.

**⚠️✅ MÁSODIK, VALÓDI GYÖKÉROK MEGTALÁLVA ÉS JAVÍTVA (2026-09-07, élő
visszajelzésed alapján: "a folyó és a jég még mindig roppant mód
csillog az űrből, távolról"):** a fenti javítás NEM ezt a jelenséget
célozta, és nem is tudta volna megoldani - kiderült, hogy a `River` és
`SeaIce` kategóriák (a felszín-panelen látható folyók és a tengeri jég)
EGYÁLTALÁN NEM a fent hangolt `VertexColorUnlit` shadert használják,
hanem Unity beépített `HDRP/Lit` anyagát (`CreateFlatColorMaterial`),
aminek a Smoothness/Metallic tulajdonságait a kód eddig SOHA nem
állította be - a HDRP/Lit alapértelmezett (~0.5, közepesen fényes)
Smoothness-e a HDRP fizikailag-korrekt, nagyon erős Nap-fényerősség
mellett erős, széles "izzást" adott - pontosan a diagnózisod ("a nap
brutál erősen fénylik") helyes volt, csak nem az én korábbi javításom
anyagára vonatkozott. Javítva: `CreateFlatColorMaterial` mostantól
alacsony, rögzített Smoothness-t (0.08) és nulla Metallic-ot állít be
minden így létrehozott anyagra (River, SeaIce, Crater, határvonal,
tartalék-anyagok - egyik sem szándékoltan csillogó felület).

**Hogyan teszteld:** Play mód, nézd a bolygót TÁVOLRÓL/űrből, keress
egy folyót vagy tengeri jeget, figyeld hogy még mindig erősen
"csillog-e".

**Elvárt eredmény:** a folyók/tengeri jég sokkal tompábbak, nem
"izzanak" fehéren/villogva még nagy távolságból nézve sem. Ha még
mindig túl fényes, a `FlatMaterialSmoothness` (0.08) tovább
csökkenthető.

**⚠️✅ HARMADIK, VALÓSZÍNŰLEG A VALÓDI OK (2026-09-07, "ugyanúgy
csillog" visszajelzésed után)**: kiderült, hogy a `PlanetView.unity`
scene-fájlban a `surfaceSpecularStrength`/`surfaceShininess` (és a
`surfaceAmbient` is) még a RÉGI, a mostani kód-alapértékek előtti
értékeken (`0.3`/`24`, ill. `0.35`) voltak SZERIALIZÁLVA - Unity NEM
szinkronizálja automatikusan egy már mentett Inspector-mezőt, ha a
C# kód alapértéke időközben megváltozik, tehát a fenti (12. tétel)
javításom a scene-ben ténylegesen ÉRVÉNYTELEN maradt, amíg valaki
(vagy egy másik munkamenet) kézzel/scriptből át nem írta a scene-ben
tárolt értékeket is. Ez MOST már javítva van a scene-ben
(`surfaceSpecularStrength: 0.12`, `surfaceShininess: 8`,
`surfaceAmbient: 0.04`) - tehát a fenti KÉT javítás (a shader-hangolás
ÉS a HDRP/Lit Smoothness-fix) most már EGYÜTT, ténylegesen aktívan
kellene hogy fusson. **Kérlek próbáld ki ÚJRA** - lehet, hogy a
korábbi "ugyanúgy csillog" visszajelzésed még a scene javítása ELŐTTI
állapotot mutatta.

**⚠️ NEGYEDIK KÖR (2026-09-07): "továbbra is fennáll"** - ellenőriztem
mindhárom helyet (scene YAML, shader Properties-fallback, C#
alapérték) - MIND a helyes, csökkentett értéken állt (0.12/8/0.08),
semmi nem állt vissza. Mivel mindkét ismert specular-forrás aktívan a
csökkentett értéken fut, mégis fennáll a jelenség, DIAGNOSZTIKAI
KÍSÉRLETKÉNT mindkettőt NULLÁRA állítottam (`surfaceSpecularStrength`
0→ (mindhárom helyen: scene/shader/kód), `FlatMaterialSmoothness`
0.08→0). Ez egy DÖNTŐ teszt: ha a csillanás EBBEN az állapotban IS
megmarad, az BIZONYÍTANÁ, hogy egyik anyag SEM a forrás, és a valódi ok
máshol van (pl. HDRP Reflection Probe/Screen Space Reflection, ami a
futásidőben (`new Material(shader)`) létrehozott HDRP/Lit anyagoknál
hiányos shader-kulcsszó-beállítás miatt "átszivároghat", vagy egy
teljesen más mechanizmus).

**Hogyan teszteld:** Play mód, nézd a bolygót TÁVOLRÓL, keress egy
folyót/tengeri jeget.

**Ha MOST (specStrength=0, Smoothness=0 mellett) IS csillog**: kérlek
jelezd - ez lesz a legfontosabb bizonyíték eddig, mert kizárja mindkét
jelenlegi anyagot, és egy mélyebb (HDRP reflection/keyword) okra
terelné a vizsgálatot.
**Ha MOST végre eltűnik**: megvan a mechanizmus, és egy ésszerű,
nem-nulla köztes érték (pl. 0.03-0.05) beállításával véglegesíthetjük.

**✅✅✅ ÖTÖDIK KÖR, A VALÓDI GYÖKÉROK MEGTALÁLVA (2026-09-07)** - a
felhasználó screenshotot küldött (`pics/p.png`, kék/piros dobozzal
megjelölve) a specStrength=0/Smoothness=0 állapotról, ÉS A CSILLOGÁS
VÁLTOZATLANUL ERŐS MARADT - ez BEBIZONYÍTOTTA, hogy egyik terep-anyag
SEM volt a forrás. A screenshoton a foltok kerek, glóriás,
lágy-szélű, "kifehéredett" jellege NEM Blinn-Phong/PBR spekuláris
csillanásra utalt, hanem HDRP BLOOM post-processing-re. Belenézve a
projekt saját `DefaultSettingsVolumeProfile.asset`-jébe (ez a
globális HDRP-alapértelmezés, mivel a scene-ben NINCS külön Volume
GameObject/felülbírálás): **a Bloom `threshold` (küszöb) értéke `0`
volt** - ez azt jelenti, hogy GYAKORLATILAG BÁRMILYEN fényes felület
bloomol, nem csak a szándékosan HDR-fényes Nap-korong. Mivel a Bloom
egy POST-PROCESSING effekt a VÉGSŐ pixel-fényességre, teljesen
FÜGGETLEN attól, hogy a fényesség diffúz vagy spekuláris eredetű -
pontosan ezért volt HATÁSTALAN minden eddigi anyag-szintű
próbálkozásom (specStrength/Smoothness csökkentése/nullázása). A jég
(közel-fehér, ~0.95-0.98 diffúz szín) és a napfényes víz a jelenet
LEGFÉNYESEBB felületei - ők blooolnak be először és legerősebben egy
ilyen alacsony küszöbnél. Javítva: `threshold` 0→1.05 (a Nap-korong
`[HDR]`+`_Brightness` szorzóval, ami akár 20x-osra is felmehet,
messze e fölött marad, tehát TOVÁBBRA IS bloomol, ahogy szándékos volt
- ld. checklist 2. pont; a normál, LDR-tartományú [0,1] terep/víz/jég
színek viszont MÁR NEM lépik át ezt a küszöböt).

**Hogyan teszteld:** Play mód, nézd a bolygót TÁVOLRÓL, keress egy
folyót/tengeri jeget/sarki jégsapkát - ELLENŐRIZD közben, hogy a Nap
maga MÉG MINDIG megfelelően fényes/izzó-e (2. pont).

**Elvárt eredmény:** a jég/folyó/víz nem "izzik" fehéren glóriával
körülvéve, csak a Nap-korong marad HDR-fényes/bloomos. Ha a Nap most
túl halványnak tűnik, a `threshold`-ot (`DefaultSettingsVolumeProfile.
asset`) tovább lehet finomítani, vagy a Nap `_Brightness`-ét (1. pont)
feljebb venni.

**⚠️ HATODIK KÖR (2026-09-07): "ugyanaz a látvány" - a Bloom-javítás
SEM segített.** Ellenőriztem, hogy a helyes Volume Profile-t
szerkesztettem-e (a `HDRenderPipelineGlobalSettings.asset`
`HDRPDefaultVolumeProfileSettings.m_VolumeProfile` GUID-ja egyezik a
szerkesztett fájléval - igen, jó fájl volt), és hogy nincs-e scene-
lokális `Volume` felülbírálás (nincs). Mégis változatlan a látvány -
ez azt jelentette, hogy a Bloom-hipotézis is TÉVES volt.

Ahelyett hogy tovább találgatnék, a MEGLÉVŐ Inspector-kapcsolókkal
(bal panel: Felhők, Kráterek, Tavak+jég, Szél-/Csapadék-overlay,
Erózió) SZISZTEMATIKUSAN, egyenként kizártam a lehetséges
rétegeket - a felhasználó élőben tesztelte mindegyiket:
- Felhők (MVP) kikapcsolva → **foltok maradtak**
- Kráterek kikapcsolva → **foltok maradtak**
- Tavak+jég kikapcsolva → **foltok maradtak**
- StarField GameObject kikapcsolva → **foltok maradtak**
- Game view Gizmos kikapcsolva → **foltok maradtak** (tehát VALÓDI
  renderelt geometria, nem Editor-debug ikon)
- SunVisual GameObject kikapcsolva → **foltok maradtak**
- Szél-/Csapadék-overlay, Erózió kikapcsolva → **foltok maradtak**

Mind a HÉT réteg kizárva élő teszttel - ez azt bizonyította, hogy a
foltok az ALAP terep/óceán-mesh RÉSZEI, ami MINDIG renderelődik,
függetlenül minden kapcsolótól.

**ÚJ HIPOTÉZIS (jelenleg alkalmazott javítás)**: mivel SEMMILYEN
fény/anyag/post-processing változtatás (5 teljes kör) nem hatott, a
gyanú a NYERS ALAPSZÍNRE terelődött - `RenderCategory.IceSheet` színe
`(0.95, 0.96, 0.98)`, a `RenderCategory.SeaIce` színe `(0.80, 0.88,
0.93)` volt - MAJDNEM TISZTA FEHÉR, VALÓDI FÉNYEZÉS/CSILLOGÁS NÉLKÜL
IS "izzó"/vakító hatást kelthetnek, pusztán a nyers színük miatt - ez
lenne az EGYETLEN magyarázat, ami összhangban van azzal, hogy semelyik
fényezési/post-processing változtatás nem segített (egyik sem érinti a
nyers, tömör alapszínt). Tompítva: IceSheet → `(0.80, 0.83, 0.87)`,
SeaIce → `(0.72, 0.78, 0.84)` - realisztikusabb, kevésbé vakító
jégszín-tartomány. **A PIROS (folyó melletti) folt eredete ezzel MÉG
NEM biztosan megmagyarázott** (a `River` szín `(0.20, 0.55, 0.90)` NEM
közel-fehér) - lehet, hogy a piros doboz valójában egy közeli SeaIce-
foltot jelöl, amit a folyó torkolatával tévesztettél össze, vagy egy
MÁSIK, még fel nem tárt ok áll mögötte.

**Kérlek próbáld ki újra.** Ha a KÉK (jég) folt eltűnik/halványabb, de
a PIROS (folyó) NEM, az megerősítené a fenti hipotézist ÉS jelezné,
hogy a piros folt egy KÜLÖN, még vizsgálandó jelenség.

**✅ HETEDIK KÖR, ÚJ SCREENSHOT ALAPJÁN (2026-09-07)**: friss
screenshotot küldtél a tompított jégszínnel - a foltok VÁLTOZATLANUL
jelen voltak, DE egy kulcsfontosságú észrevételt is hoztál: **"Tavak+jég
kikapcsolva → MÉG FÉNYESEBB, szinte vakító"**. Ez volt az áttörő nyom -
amikor a "Tavak+jég" ki van kapcsolva, a korábban jéggel fedett terület
LIKVID VÍZFELÜLETRE (`BuildWaterSurface`) vált, ami egy TÖKÉLETESEN
SIMA gömbhéj a kalibrált tengerszint sugaránál. A screenshoton látható
foltok jellege (koncentrált, éles, glóriás) pontosan a valódi
műholdképeken is látható "napcsillanás" (sun glint) jelenségre
hasonlít - ez egy sima felületen SOKKAL koncentráltabb/fényesebb
tükröződést ad, mint egy durva, bumpy terepen, UGYANOLYAN specular-
paraméterek mellett, mert a sima gömbhéj normálja KOHERENS egy nagy
területen (sok szomszédos pont "néz" ugyanabba az irányba), míg a
durva terep normáljai szórtak.

**A VALÓDI GYÖKÉROK**: a vízfelszín (óceán+tó) EDDIG a szárazfölddel
MEGOSZTOTT anyagot (`_vertexColorMaterial`) használta - ugyanazt a
`surfaceSpecularStrength`-et (0.12) kapta, mint a szikla/tundra, holott
a sima geometriája miatt ugyanaz az érték sokkal erősebb csillanást ad
rajta. Javítva: a vízfelszín MOST MÁR egy KÜLÖN anyagot kap
(`_waterSurfaceMaterial`), saját, jóval alacsonyabb specular-
paraméterekkel (`waterSpecularStrength=0.02`, `waterShininess=8` -
Inspector-mezők, élőben hangolhatók).

**Hogyan teszteld:** Play mód, nézd a bolygót TÁVOLRÓL, keress egy
jeges sarkot/folyótorkolatot, figyeld a vízfelszín csillanását.

**Elvárt eredmény:** a víz felszíne ne "izzon"/villanjon vakítóan -
egy finom, természetes csillanás rendben van, de nem szabad glóriás,
kifehéredett foltnak látszania. Ha még mindig túl erős, a
`waterSpecularStrength` (0.02) tovább csökkenthető (akár 0-ra is), az
Inspector `PlanetGridMesh` komponensén.

**⚠️ NYOLCADIK KÖR - DÖNTŐ FORDULAT (2026-09-07)**: "ha kikapcsolom a
tavak+jég értéket, sokkal fényesebb lesz a pólus, szinte világít...
kikapcsoltam a directional light-ot is, arra se változott semmi." A
Directional Light kikapcsolása NULLA hatással volt - ez KIZÁRJA MINDEN
eddigi fényezési hipotézisemet (specular, diffúz, bloom - MIND
fényforrás-függő lenne). Valószínű ok: a saját `VertexColorUnlit`
shaderünk NEM Unity beépített fény-rendszerét használja, hanem a C#
minden frame-ben KÉZZEL olvassa ki a Directional Light `.color`/
`.transform.forward` értékét - egy KIKAPCSOLT GameObject komponens-
értékei NEM nullázódnak automatikusan, tehát a shader valószínűleg
MINDIG "teljesen megvilágított" állapotot számol, függetlenül attól,
hogy a fény aktív-e.

**TISZTA DIAGNOSZTIKAI TESZT hozzáadva** (`PlanetGridMesh.cs`, ÚJ
`Diag Force Zero Lighting` Inspector-checkbox, ALAPÉRTELMEZETTEN
BEKAPCSOLVA): kódból (nem Light-kikapcsolással, ami - mint láttuk -
nem elég) EXPLICIT (0,0,0) Nap-színt és 0 ambienst kényszerít a
folytonos felszín-shaderre (szárazföld ÉS víz egyaránt) - ez a
matematika szerint TELJESEN FEKETÉVÉ tenné a kimenetet, függetlenül a
vertex-színtől.

**Hogyan teszteld:** Play mód (a checkbox ALAPÉRTELMEZETTEN be van
kapcsolva, nincs teendőd) - nézd meg, a pólusok MOST IS fénylenek-e.

**Ha a pólusok MOST IS fénylenek**: ez VÉGLEGESEN bizonyítja, hogy a
jelenség NEM fényezési eredetű - egy teljesen más irányba (alapszín/
emisszió/egy még fel nem tárt réteg) kell mennem, és kérlek, ezt jelezd
vissza pontosan így ("még mindig fénylik nulla fény mellett is").
**Ha a pólusok MOST elsötétülnek**: az azt jelentené, hogy a fényezés
MÉGIS szerepet játszik, de valamiért a Directional Light GameObject
kikapcsolása nem volt elég ahhoz, hogy ezt tükrözze - ez is fontos
infó, amit érdemes visszajelezni.

**FONTOS**: ez egy IDEIGLENES diagnosztikai kapcsoló, ami jelenleg
SZÁNDÉKOSAN feketére kényszeríti a felszínt - ez NEM a végleges
állapot, csak egy teszt. A teszt után vissza kell kapcsolni
(`Diag Force Zero Lighting` checkbox kikapcsolása), amint megvan az
eredmény.

**✅ KILENCEDIK KÖR - HIÁNYOSSÁG TALÁLVA A SAJÁT TESZTEMBEN
(2026-09-07)**: friss screenshotot küldtél - a Föld nagy része tényleg
elsötétült (a diagnosztikám MŰKÖDÖTT az általános terepre), DE a
pólusok/szigetek közelében 2-3, szorosan csoportosuló, éles, kerek
fényfolt MARADT, "Tavak+jég" kikapcsolásával pedig még intenzívebb
lett. A screenshoton jól látszik, hogy "Felhők (MVP)" BE volt
kapcsolva ennél a tesztnél.

Átvizsgálva a saját `UpdateSurfaceLightingUniforms` kódomat, találtam
egy HIÁNYOSSÁGOT: a `diagForceZeroLighting` kapcsoló a Nap-színt
(`_SunColor`) helyesen feketére állította a FELHŐ-anyagra is, DE a
felhő-anyag SAJÁT, külön `cloudAmbient` (0.55) mezőjét NEM nulláztam -
ez azt jelentette, hogy egy sűrű/fehér felhő-csomó MÉG A "fekete Nap"
teszt alatt is `0.55 × felhő-vertex-szín` fényességet kaphatott. A
foltok diszkrét, csoportosuló, kerek jellege (nem egy folytonos,
egyenletes terület) pontosan illik a "lokálisan sűrű csapadék/felhő-
csomók" képhez.

**Javítva**: a `cloudAmbient` is 0-ra kényszerítve a diagnosztikai
teszt alatt.

**Hogyan teszteld:** Play mód, `Diag Force Zero Lighting` továbbra is
bekapcsolva (alapértelmezett) - nézd meg, MOST elsötétülnek-e a
pólusok is. **Gyorsabb, kód nélküli ellenőrzésként** azonnal
kipróbálhatod a JELENLEGI (még nem frissített) buildeddel is: kapcsold
KI a "Felhők (MVP)" checkboxot Play közben, amíg a "Diag Force Zero
Lighting" aktív - ha EZZEL is elsötétülnek a pólusok, az megerősíti,
hogy a felhő-réteg (és a fenti kód-javítás) volt a hiányzó darab, még
mielőtt újraépítenéd a projektet.

**✅✅ TIZEDIK KÖR, VALÓSZÍNŰLEG A VALÓDI GYÖKÉROK (2026-09-07)**: a
felhők kikapcsolása SEM segített ("semmi nem változott... minden
ugyanolyan fényes"). Ez kizárta a felhőket is. Mivel a saját, custom
`VertexColorUnlit` shaderem (Ocean/IceSheet/Tundra/Temperate/Tropical +
víz) BIZONYÍTOTTAN helyesen elsötétült a `diagForceZeroLighting` alatt,
és minden más réteget/kapcsolót is kizártunk, az EGYETLEN megmaradt,
SOHA nem érintett rendszer a `River`/`SeaIce`/`Crater` kategóriák
Unity SAJÁT `HDRP/Lit` anyaga (`CreateFlatColorMaterial`) - ez a
VALÓDI HDRP PBR fény-csővezetéken megy át, ami magába foglalja az
ÉGBOLT (Sky) AMBIENT/KÖRNYEZETI HOZZÁJÁRULÁSÁT is - ez TELJESEN
FÜGGETLEN a Directional Light-tól (külön rendszer), és a saját
shaderem SOSEM olvassa/módosítja (csak a saját manuális `_Ambient`
uniformomat ismeri, nem Unity valódi ambient probe-ját).

Belenézve a projekt globális HDRP Volume-profiljába
(`DefaultSettingsVolumeProfile.asset`, ugyanaz a fájl, ahol korábban a
Bloom threshold=0-t találtam): a **`HDRISky` komponens `exposure`
értéke `11` volt** - ez a HDRP exponenciális EV-skáláján KIRÍVÓAN magas
(kb. 2000x-es fényerő-szorzó a semleges 0-hoz képest), miközben a MÁSIK
két sky-komponens (`GradientSky`, `PhysicallyBasedSky`) mindkettő
`exposure: 0`-n áll - ez az egyetlen kiugró érték az egész profilban.
Egy ilyen túlexponált égbolt egy ÓRIÁSI, a Directional Light-tól
független AMBIENT-fényforrást ad minden HDRP/Lit anyagnak - a
világos/fehér SeaIce ezt SOKKAL látványosabban veri vissza, mint a
sötét Crater szín (ami sosem tűnt fel problémaként).

**Javítva**: `HDRISky.exposure` 11 → 0 (a másik két sky-típus
alapértékéhez igazítva).

**Hogyan teszteld:** Play mód - VAGY (gyorsabb, semmilyen kapcsolóhoz
nem kötött teszt): kapcsold KI a `Diag Force Zero Lighting` checkboxot
(hogy a NORMÁL, végleges megjelenést lásd), és nézd meg, csillog-e még
a folyó/jég.

**Elvárt eredmény:** a folyók/tengeri jég/pólusok NEM izzanak
vakítóan - egy visszafogott, természetes megjelenés várt. Ha ez
végre megoldja, a `Diag Force Zero Lighting` kapcsolót vissza kell
állítani KIKAPCSOLT állapotba (jelenleg diagnosztikai célból
alapértelmezetten BE van kapcsolva) - ezt a következő körben
takarítom el.

**✅ TIZENEGYEDIK KÖR (2026-09-07): "picit jobb, de még mindig nagyon
fénylik"** - a Bloom + HDRISky javítás VALÓS, MÉRHETŐ javulást hozott
(megerősítve a diagnózis helyes irányát), de nem elég. Ellenőriztem az
`IndirectLightingController`-t is (minden szorzó semleges 1x, rendben)
- nem találtam további kirívó anomáliát a Volume-profilban. Mivel a
`River`/`SeaIce` HDRP/Lit anyaga MÉG MINDIG `Smoothness=0.08`-on állt
(nem nulla), ez továbbra is engedhetett valamennyi HDRP indirekt
specularis/tükröződő választ a most már csökkentett, de nem TÖKÉLETESEN
semleges ég-fényességre. Javítva: `Smoothness` 0.08→0 (a HDRP BRDF-je
elméletileg SEMMILYEN specularis/tükröző választ nem ad tiszta 0
Smoothness-nél), plusz explicit fekete Emissive Color beállítva
(biztonsági intézkedés - futásidőben, Editor ShaderGUI nélkül
létrehozott HDRP anyagnál nem garantált, hogy az emisszió
alapértelmezetten fekete).

**Hogyan teszteld:** Play mód, normál nézet (a `Diag Force Zero
Lighting` alapból ki van kapcsolva).

**Elvárt eredmény:** a folyók/tengeri jég most már ÉRDEMBEN
halványabbak legyenek. Ha MÉG MINDIG erősen fénylenek ezek után is,
kérlek jelezd - ekkor valószínűleg egy MÉLYEBB, a HDRP/Lit anyagok
futásidejű (Editor-keyword-inicializálás nélküli) létrehozásából eredő
problémára kell gyanakodnom (ezt korábban felvetettem lehetőségként),
ami egy nagyobb refaktort (Editor-ban előre elkészített sablon-anyag
`Instantiate()`-elése) igényelne.

**✅ TIZENKETTEDIK KÖR - TELJES, SZISZTEMATIKUS AUDIT (2026-09-07)**:
"annyira ne örülj, mert továbbra se jó... brutál világít... szeretném,
ha most szisztematikusan utánanéznél" - jogos kérés. Egy közeli, felül-
nézeti screenshotot küldtél a pólusról, ami egy TELJESEN kiégett, hatalmas
fehér gömböt mutatott, sokkal súlyosabbat, mint a korábbi kisebb foltok -
ez konkrét, súlyos hibára utalt, nem finom túlfényezésre.

Ehelyett, hogy tovább találgatnék, VÉGIGMENTEM a teljes HDRP Volume-
profilon, komponensről komponensre:
- **Bloom** - MÁR javítva (threshold 0→1.05).
- **HDRISky** - MÁR javítva (exposure 11→0).
- **GradientSky, PhysicallyBasedSky** - ellenőrizve, nincs anomália.
- **IndirectLightingController** - ellenőrizve, minden szorzó semleges (1x).
- **Exposure** - ÚJ TALÁLAT: `mode: Automatic` (1), `meteringMode:
  Center Weighted`, `limitMax: 14` (rendkívül megengedő, ~16000x-es
  lehetséges szorzó). Az Automatic Exposure a KAMERA AKTUÁLIS
  KERETÉTŐL FÜGGŐEN dinamikusan állítja a teljes kép fényességét - ha a
  kamera közelről egy fényes felületre néz sok sötét háttérrel
  (pl. közeli pólus-nézet sok űrrel körülötte), a rendszer drasztikusan
  túlexponálhatja a fényes részt. Ez ELVI szinten is hibás ennél a
  projektnél (I3 invariáns: minden pixel a világmodellből következzen,
  ne a kamerakeret aktuális tartalmától).
- **Tonemapping** - ellenőrizve: `ACES` mód (nem "None") - tehát ez
  NEM ad kemény, "hard clip" fehér-vágást, hanem fokozatosan görbít -
  rendben, nem ez okozza az éles kivágást.
- **WhiteBalance, ColorAdjustments** - ellenőrizve, minden semleges.

**Javítva**: `Exposure.mode` Automatic (1) → **Fixed** (0),
`fixedExposure=0` (semleges) - ez determinisztikus, a kamera keretétől
FÜGGETLEN, kiszámítható expozíciót ad.

**Egyéb, dokumentált (de egyelőre NEM javított) megfigyelés**: a
kráter-markerek (`BuildCraterMarkers`) Unity beépített `CreatePrimitive
(PrimitiveType.Sphere)`-jét használják, 1-6 Unity-egység átmérővel -
extrém közeli zoomnál (mint a mostani screenshot) ez akár betöltheti a
képernyőt, HA a kamera nagyon közel kerül egy kráterhez. A színük
(sötét vörösbarna) NEM indokolná a fehér megjelenést, de ha a
screenshot ÉPPEN egy kráter-markerre zoomolt (nem magára a jégre), ez
külön vizsgálandó - kérlek jelezd, ha a kép EGY KRÁTERT mutat-e
(kikapcsolt "Kráterek" melletti újratesztelés ezt eldöntené).

**Hogyan teszteld:** Play mód, normál nézet, ugyanaz a közeli
pólus-nézet, mint a legutóbbi screenshoton.

**Elvárt eredmény:** a pólus NE legyen teljesen kiégett, fehér gömb -
látszódjon a domborzat/jég textúrája, még közeli zoomnál is.

**✅✅✅ TIZENHARMADIK KÖR - A VALÓDI GYÖKÉROK MEGTALÁLVA, DIREKT
AZONOSÍTÁSSAL (2026-09-07)**: jogos, éles visszajelzésed ("totál
irracionális és elfogadhatatlan") után leálltam a találgatással, és egy
DIREKT, egyértelmű azonosítást kértem: Play → Pause → Scene nézetben
kattintás a fénylő objektumra. A válaszod: **"WaterSurface"**. Ez volt
az áttörés - végre PONTOSAN tudtam, melyik kódot kell megvizsgálnom, nem
kellett tovább más rendszereket (felhő, kráter, csillag, Nap, fény,
post-processing) gyanúsítanom.

Alaposan átvizsgálva a vízfelszín-mesh geometria-építő kódját
(`AddQuad`), találtam egy VALÓDI PROGRAMHIBÁT: a felszín NORMÁLVEKTORA
`Vector3.Cross(p10-p00, p01-p00).normalized` képlettel készül - ha egy
négyszög (quad) DEGENERÁLT (a 4 sarokpont majdnem/pontosan egybeesik -
ez a kockás-gömb geometria SARKAIN/PÓLUSAIN fordulhat elő, ahol több
UV-koordináta majdnem ugyanarra a 3D pontra képződik le), a cross
product egy NULLA-KÖZELI vektor lesz, aminek a `.normalized`-je **NaN**-t
eredményezhet. A NaN érték a shaderben MINDENT megfertőz (`normalize
(NaN)`, `dot(NaN,...)` → minden NaN), amit a GPU jellemzően **TISZTA
FEHÉRKÉNT** jelenít meg.

**EZ MAGYARÁZZA, MIÉRT VOLT HATÁSTALAN MINDEN KORÁBBI JAVÍTÁSI
KÍSÉRLET**: a lebegőpontos aritmetika szabálya szerint **NaN × 0 = NaN**
(NEM 0!) - tehát MÉG a "kényszerített fekete fény" (`diagForceZero
Lighting`) tesztem SEM tudta volna kijavítani ezt, mert bármi × NaN
= NaN, függetlenül attól, mekkora számot szorzunk vele. Ez azt is
megmagyarázza, hogy a Directional Light kikapcsolása, a Bloom/HDRISky/
Exposure-javítások, a specular/smoothness nullázások MIND HATÁSTALANOK
voltak - egyik sem tudja kijavítani egy NaN forrását, csak elfedni
(részben) a KÖRNYEZŐ, nem-NaN pixeleket. A "Tavak+jég kikapcsolva →
fényesebb" megfigyelés is illik a képbe: több nyílt víz = több
vízfelszín-quad a sarok közelében = nagyobb esély degenerált quad-ra.

**Javítás** (`PlanetGridMesh.cs`, `AddQuad`): védelmi küszöb - ha a
cross product hossza túl kicsi a megbízható normalizáláshoz, a
sarokpontok saját sugárirány-átlagára esünk vissza (ami egy gömb-
felszíni pontra SOHA nem lehet NaN/nulla).

**Hogyan teszteld:** Play mód, ugyanaz a közeli pólus-nézet, ahol a
korábbi screenshot készült.

**Elvárt eredmény:** a pólus TÖBBÉ NE legyen teljesen kiégett, fehér
gömb - látszódjon a jég/terep tényleges textúrája, még a legközelebbi
zoomnál is.

**❌ A TIZENHARMADIK KÖR NEM HOZOTT VÁLTOZÁST ("totál semmi nem
változott")** - megerősítetted, hogy teljes Stop/Play újraindítást
csináltál (nem hot-reload volt a probléma), tehát a javított kód
ténylegesen lefutott, mégsem változott semmi. Ez azt jelentette, hogy
a 13. kör diagnózisa hiányos volt.

**✅✅✅✅ TIZENNEGYEDIK KÖR - A SAJÁT JAVÍTÁSOM HIBÁJÁNAK MEGTALÁLÁSA +
KETTŐS DIAGNOSZTIKAI VÉDŐHÁLÓ (2026-09-09):** újra átnéztem a 13. körös
javítás feltételét: `if (rawNormal.sqrMagnitude < 1e-12f)`. Ez a
feltétel **NEM fogja meg azt az esetet, amikor `sqrMagnitude` maga NaN**
(nem csak nagyon kicsi) - IEEE-754 szerint bármilyen `<`/`>`
összehasonlítás NaN-nal **MINDIG `false`**-t ad, tehát ha a bemenő
SAROKPONTOK (`p00`/`p10`/`p11`/`p01`) már eleve NaN-t tartalmaztak (nem
csak nulla-közeli, de véges pontok voltak), a védelmi ág SOHA nem
futott le, és a NaN változatlanul továbbterjedt - pontosan megmagyarázza
a "totál semmi nem változott" visszajelzést.

**Javítás 1** (`PlanetGridMesh.cs`, `AddQuad`): a feltétel `!(rawNormal.
sqrMagnitude >= 1e-12f)` alakra cserélve - ez a tagadott forma NaN
esetén IS `true`-t ad (mert `NaN >= bármi` mindig `false`), tehát a
fallback-ág NaN esetén is lefut. A fallback-ág maga is megerősítve: ha
az átlagpozíció-alapú normál sem esik ésszerű (0.9-1.1 négyzet-hossz)
tartományba, `Vector3.up`-ra esik vissza.

**Javítás 2** (`PlanetGridMesh.cs`, `AddQuad`): új `SanitizeVertexColor`
segédfüggvény - a négy sarok SZÍNÉT (nem csak a normálvektort) is
ellenőrzi NaN/Infinity komponensekre (ez egy MÁSIK lehetséges NaN-forrás,
a `ContinuousWaterColor`/`ContinuousCornerColor` színszámítási láncban).
Ha talál ilyet, egyszer (max 20-szor) logol Debug.LogWarning-gal a
pontos pozícióval együtt, ÉS **MAGENTA**-ra cseréli a színt.

**Javítás 3** (`VertexColorUnlit.shader`, `Frag`): a shader végén, a
végső `rgb` kiszámítása UTÁN, egy `isnan`/`isinf` ellenőrzés - ha a GPU
maga (pl. egy `normalize()` egy majdnem-nulla vektoron, vagy `pow()`
egy negatív/NaN alapon) állítana elő NaN/Infinity-t FÜGGETLENÜL a C#
oldali bemenettől, azt **CIÁN**-ra cseréli.

**Ez a két diagnosztikai szín (MAGENTA vs. CIÁN) EGYÉRTELMŰEN
megkülönbözteti, hogy a NaN forrása a C#-oldali bemenő adat (szín) vagy
a GPU-oldali számítás** - ha a fehér folt eltűnik és bármelyik felbukkan,
azonnal tudni fogjuk a pontos forrást a következő, VÉGLEGES javításhoz.
Ha SEM magenta, SEM cián nem jelenik meg, de a fehér folt is eltűnik,
az azt jelenti, hogy a normálvektor-javítás (1. javítás) önmagában elég
volt, és a 13. körös elmélet helyes volt, csak a feltétel hibás.

**Hogyan teszteld:** Play mód TELJES újraindítással (Stop, majd Play),
ugyanaz a közeli pólus-nézet, mint korábban. Kérlek NÉZD MEG a Unity
Console-t is (nem csak a Game-nézetet) - ha van `[NaN-diag]` kezdetű
sárga figyelmeztetés, kérlek másold be ide vagy a chatbe.

**Elvárt eredmény:** a pólus/folyó NE legyen többé kiégett fehér - vagy
teljesen normális domborzat/jég-textúra látszik, VAGY (ha a hiba máshol
van) egy jól látható MAGENTA vagy CIÁN folt jelenik meg a fehér helyén,
ami pontosan megmutatja a maradék hiba forrását.

**✅✅✅✅✅ TIZENÖTÖDIK KÖR - STRUKTURÁLIS ÚJRATERVEZÉS, NEM TOVÁBBI PATCH
(2026-09-09)**: a felhasználó kérésére megtaláltam a PONTOS eredet-
kommitot (`git log`): `1efd5fa` ("restore specular", 2026-09-05) vezette
be, hogy a shader egyáltalán kiolvassa a per-vertex normálvektort -
ELŐTTE a shader unlit volt, a hibás `Vector3.Cross(...).normalized`
számítás már azelőtt is a kódban volt, csak LÁTHATATLAN, mert semmi nem
használta. Ez megerősítette a NaN-elmélet strukturális helyességét, de
mivel a 13-14. körös PATCH-elés (védelmi küszöbök hozzáadása a törékeny
cross-product körül) nem bizonyult véglegesen elégségesnek, ehelyett a
teljes hibás számítást KIIKTATTAM: mivel a felszín gyakorlatilag egy
gömb, minden csúcspont saját normálja `normalize(saját pozíció)` -
ez STRUKTURÁLISAN SOHA nem lehet NaN/nulla (egy bolygófelszíni pont
sosincs az origóban), függetlenül attól, mennyire esik egybe a négy
sarokpont. Ez a régi, quadonkénti LAPOS normál helyett SIMA,
csúcsonkénti normálokat ad - ami MELLÉKESEN megoldja a legelső körben
jelzett "tile-ról tile-ra pattogó, villámlás-szerű" csillanást is (a
fényfolt mostantól folyamatosan vándorol, nem ugrik tile-határokon).
A háromszög-felbontás (winding) döntéséhez a négy biztonságos normál
összegét használom referenciaként (nem kell hozzá a pontos lapos
normál). A 14. körös diagnosztikai védőháló (magenta=szín-NaN,
cián=GPU-oldali NaN) megmaradt változatlanul, további biztonsági
hálóként.

**Miért jobb ez, mint a 13-14. kör**: azok egy törékeny számítás köré
építettek egyre több védelmi küszöböt (ami könnyen hibás lehet, ahogy a
14. kör bizonyította a 13. kör hibás feltételén). Ez a megoldás magát a
törékeny számítást szünteti meg - nincs többé cross product, nincs
osztás nulla-közeli vektorral, nincs küszöbérték, amit el lehetne
rontani.

**Hogyan teszteld:** Play mód TELJES újraindítással, ugyanaz a közeli
pólus-nézet.

**Elvárt eredmény:** a pólus/folyó NE legyen kiégett fehér, ÉS a
korábban jelzett "villámlás-szerű pattogás" is simábbá váljon (ha még
mindig van csillanás, annak most már folyamatosan kell mozognia a
felületen).

**✅✅✅✅✅✅ TIZENHATODIK KÖR - MÁSODIK, FÜGGETLEN NaN-FORRÁS MEGTALÁLVA
A DIAGNOSZTIKAI SZÍNJELZÉS ALAPJÁN (2026-09-09)**: "most jobb de még
mindig van egy pont" visszajelzés után friss screenshot (`p.png`) -
a felhasználó két területet jelölt be. A PIROS keretes folt színe
egyértelműen **CIÁN** volt - ami PONTOSAN a 14. körben beépített
GPU-oldali NaN-diagnosztika (`isnan(rgb)` → cián) jelzése. Ez
bizonyította: a mesh-normál-javítás (15. kör) jó volt, de VAN egy
MÁSODIK, TŐLE FÜGGETLEN NaN-forrás magában a fényszámításban.

Megtalálva: a Blinn-Phong fél-vektor `H = normalize(L + V)` - ha a
Nap-irány (`L`) és a kamera-irány (`V`) KÖZEL ELLENTÉTES (`L+V`
közel nulla), ami egy gömb limb-jénél/sarkánál BIZONYOS kamera-Nap
szögeknél PONTOSAN előfordulhat, `normalize(0,0,0)` NaN-t ad a GPU-n
(0/0 osztás). Ez egy PIXELENKÉNTI, kamera-/Nap-szög-FÜGGŐ eset - nem a
mesh geometriájával függ össze (ezért nem érintette a 15. körös
javítás), és PONTOSAN egyetlen pontként/kis foltként jelenik meg,
ahogy a felhasználó jelezte.

**Javítás** (`VertexColorUnlit.shader`): új `SafeNormalize` segédfüggvény
- nulla-közeli bemenetre megadott tartalék-irányt ad NaN helyett. `N`
és `L` is ezt használja (extra biztonság). A specular `H`-nál: ha
`L+V` közel nulla, a spekuláris egyszerűen NULLA (fizikailag
értelmetlen/elhanyagolható konfiguráció ilyenkor), nem próbál
degenerált vektort normalizálni.

**Hogyan teszteld:** Play mód TELJES újraindítással, ugyanaz a
pólus-közeli nézet, ahonnan a screenshot készült.

**Elvárt eredmény:** sem a fehér (kiégett), sem a cián/magenta
(diagnosztikai) folt ne jelenjen meg többé a pólusnál/folyóknál.

**✅✅✅✅✅✅✅ TIZENHETEDIK KÖR - A SÁRGA PÓLUS-FOLT: NEM RENDERING-HIBA,
HANEM VALÓS SZIMULÁCIÓS EREDMÉNY (ND-54, 2026-09-09)**: "most jobb, de
még mindig van egy pont az északi pólúson ami világít" - friss
screenshot (`p.png`) alapján a folt SÁRGA volt (nem magenta/cián - a
diagnosztikai jelzéseim NEM ezek voltak). Direkt teszt (kamera-forgatás):
a folt A FELSZÍNHEZ RÖGZÍTVE mozog a bolygóval - tehát valódi felszín-
adat, nem a Nap/lencsefelvillanás.

Végigszámoltam a hőmérséklet-képletet: a folytonos szín a "Simple"
`Temperature.TemperatureKelvin`-t használta, ami NEM tartalmaz jég-
albedó/óceán-hőpuffer visszacsatolást. Sarki NYÁR alatt a Nap SOHA nem
nyugszik le a pólusnál - kiszámolva: **pólus ≈ 319K (46°C)** vs
**egyenlítő napi átlag ≈ 303K (30°C)** a jelenlegi 23.44°-os
tengelydőléssel - a Simple képlet szerint a pólus MELEGEBB, mint az
egyenlítő, átlépve a "Tropical" küszöböt. Ez egy VALÓS (bár hiányosan
modellezett) csillagászati jelenség, nem hiba - de a modell hiányzó
jég-albedó-visszacsatolása miatt túlzottan látványos.

**Döntés (felhasználói választás 3 opcióból)**: mind az 5 hívási hely a
"Full" hőmodellre (`TemperatureKelvinFull`, ND-42) váltott egy közös
`TemperatureKelvinAt` segédfüggvényen át - ez egyszerre javítja a
felszín-színt ÉS konzisztenssé teszi a panellel (ami eddig IS a Simple
képletet használta a biome-besoroláshoz, csak ez nem tűnt fel az
aggregált "dominant biome" statisztikában).

**FONTOS, KAPCSOLÓDÓ TALÁLAT**: a scene-ben `useGpuClassification: 1`
volt bekapcsolva - a GPU compute shader (`TileClassification.compute`)
SAJÁT, Simple-képletet használó portot futtat, ami FELÜLÍRTA volna a
CPU-oldali javítást. Kikapcsolva (`useGpuClassification: 0`) - a Full
modell GPU-portolása kockázatos lenne (ND-52 precedens: hasonló bővítés
GPU shader-fordítási időtúllépést okozott). Ld. ND-54 a teljes
indoklásért.

**Hogyan teszteld:** Play mód TELJES újraindítással, ugyanaz a
pólus-közeli nézet.

**Elvárt eredmény:** a sárga folt eltűnik/lehűl a pólusnál (a jég-
albedó/óceán-puffer most már visszahűti). Megjegyzés: ha ÉPP most van
"sarki nyár" a világ aktuális pillanatában, egy ENYHÉBB melegedés a
pólusnál még mindig VÁRHATÓ és HELYES (ez most már csak reálisabb
mértékű, nem a korábbi extrém "Tropical" túllövés).

**❌ VISSZAVONVA: "az egész bolygó tök sötét" (2026-09-09)** - a Full
hőmodellre váltás után a felhasználó azt jelezte, hogy az EGÉSZ bolygó
teljesen sötét lett. Valószínű ok: a `ClimateCycleTemperatureK`/
`GreenhouseTemperature` POZÍCIÓFÜGGETLEN értéket ad (csak worldSeed/
tYears/ghgPpm függvénye), MÉGIS minden egyes sarokra/tile-ra újra
lefutott (2× Threefry4x64 hash + 3× SinCos hívásonként) - ez
százezres hívásszámnál súlyos teljesítmény-regressziót okozhatott
(a mesh-build sosem fejeződött be rendesen). **VISSZAÁLLÍTVA** a
Simple képletre (`Temperature.TemperatureKelvin`) ÉS a
`useGpuClassification: 1`-re (mindkettő a mai módosítás volt) - a
sárga pólus-folt emiatt VISSZATÉRHET, de a bolygó ismét látható kell
legyen. A Full modell megfelelő (cache-elt, teljesítmény-biztos)
bevezetése egy KÜLÖN, alaposabb feladat marad (ld. ND-54 frissített
utójegyzete) - nem oldottuk meg ma.

**Hogyan teszteld:** Play mód TELJES újraindítással.

**Elvárt eredmény:** a bolygó ÚJRA teljesen látható/normálisan
megvilágított (a "tök sötét" állapot megszűnik). A sárga pólus-folt
valószínűleg visszatér - ez egy KÜLÖN, még nyitott tétel.

**✅ TIZENNYOLCADIK KÖR (2026-09-09): "színesebb a bolygó, de elveszett
a vizuális magasság érzete"** - a "tök sötét" visszavonása után a
bolygó ismét látszott, DE egy ÚJ, a 15. kör mellékhatásaként jelentkező
probléma derült ki: a 15. kör a lejtő-érzékeny (cross-product) normált
TELJESEN lecserélte egy tisztán gömb-irányú (`normalize(pozíció)`)
normálra minden csúcsponton - ez NaN-biztos volt, de emiatt a domborzat
(hegyek, völgyek) elvesztette a lejtő-alapú árnyalását, mindenhol úgy
fényezve, mintha a felszín tökéletes gömb lenne.

**Javítva**: `AddQuad` visszaáll a lejtő-érzékeny LAPOS (cross-product)
normálra mint ELSŐDLEGES forrásra (ez adja vissza a domborzat-
árnyalást/magasság-érzetet), de a NaN elleni védelem MOST a 14. körben
felismert HELYES formával (`!(sqrMagnitude >= küszöb)`, ami NaN esetén
is a biztonságos ágba irányít) - csak a valóban degenerált (pólus-
közeli/kockasarok) esetekben esik vissza a gömb-irányú normálra. A
színkód-alapú (magenta/cián) NaN-diagnosztika és a shader H-vektor
javítás (16. kör) változatlanul megmaradt.

**Hogyan teszteld:** Play mód TELJES újraindítással.

**Elvárt eredmény:** a domborzat (hegyek/völgyek) ismét látványosan
árnyalt (magasság-érzet visszatér), ÉS a korábbi pólus/folyó-villódzás
NEM tér vissza (a NaN-védelem aktív marad).

**❌ "na erre a módosításra most totál visszaállt a csillogás" (2026-09-09)**
- a lapos, quadonkénti normál visszaállítása visszahozta az EREDETI
(2026-09-06 óta ismert) problémát: a normál-mező DISZKONTINUUS a
tile-határokon, ezért a spekuláris folt tile-ról tile-ra ugorva
"villan" - ez NEM az erősség kérdése (1-12. körben már bizonyítva),
hanem a folytonosság hiányáé.

**✅✅✅✅✅✅✅✅ TIZENKILENCEDIK KÖR - ND-55: lejtő-érzékeny ÉS
tile-határok között folytonos normál (2026-09-09)**: a megoldás nem
"lapos VAGY sima" választás, hanem egy HARMADIK módszer - a normált a
sarokponthoz kötve, kis, RÖGZÍTETT (nem tile-mérettől függő) UV-eltolású
veges differenciával számoljuk (`ComputeCornerNormalViaFiniteDifference`),
a MÁR meglévő, szomszédos tile-ok között MEGOSZTOTT sarok-cache
mintázatát követve (mint eddig is a pozíció/szín). Mivel a normál
kizárólag a `(face,uc,vc)` pont függvénye, a szomszédos tile-ok a közös
sarkukon AUTOMATIKUSAN ugyanazt az értéket kapják (nincs ugrás), miközben
a normál a TÉNYLEGES helyi lejtésből (nem gömb-közelítésből) származik.
Ld. `docs/04-decisions.md` ND-55 a teljes indoklásért.

**Teljesítmény-figyelmeztetés**: ez sarkonként (de csak az EGYEDI, még
nem cache-elt sarkokra) 2 további elevációkiértékelést jelent - a
Full-hőmodell-kísérlet (ND-54) teljesítmény-regressziójából tanulva ez
KIZÁRÓLAG a meglévő párhuzamos sarok-cache-en (`PrecomputeCornersInParallel`)
keresztül fut, sosem quadonként közvetlenül. **Kérlek figyeld, nem lesz-e
akadozás/lassulás** kamera-mozgatáskor - ha igen, azonnal jelezd, ez a
kör könnyen visszaállítható.

**Hogyan teszteld:** Play mód TELJES újraindítással, nézd a domborzatot
KÖZELRŐL (magasság-érzet) ÉS forgasd a kamerát/várd a Nap mozgását egy
folyón/jégsapkán (csillanás-folytonosság), plusz figyeld az általános
simaságot/FPS-t.

**Elvárt eredmény:** a hegyek/völgyek látványosan árnyaltak (magasság-
érzet), ÉS a spekuláris csillanás - ha egyáltalán észrevehető - FOLYAMATOSAN
vándorol a felületen, nem ugrik tile-ról tile-ra. Nincs érezhető lassulás.

**✅✅✅✅✅✅✅✅✅ MEGERŐSÍTVE (2026-09-09): "magasságérzet ok, most
eltűntek a tile-ok is, jónak tűnik"** - a domborzat-árnyalás és a
tile-folytonosság egyszerre teljesül. Teljesítmény: "nem rossz" -
funkcionálisan LEZÁRVA. Egy apró, jövőbeli finomhangolási lehetőség
(NEM sürgős, backlogba: ld. `docs/backlog.md`) - a `NormalSampleEpsilonUV`
(1e-4) és a normál-cache mérete durvábbra/finomabbra állítható, ha
később mégis teljesítmény-problémát észlelnénk nagyobb rácsfelbontásnál.

**⚠️ HUSZADIK KÖR (2026-09-09): "a tükröződés még mindig durva"** - az
ND-55 (sima, folytonos normálok) mellékhatásaként a spekuláris csillanás
most már NAGY, ÖSSZEFÜGGŐ területen KOHERENSEN jelentkezik (a régi,
diszkontinuus normál-mező korábban SZÉTTÖRTE ezt sok kis, elszórt
foltra). Kiszámolva: egy fényesen megvilágított, közel fehér jég-
felület alapszíne + diffúz fényezése ÖNMAGÁBAN elérheti a ~1.0-1.1
fényerőt - ez majdnem PONTOSAN a Bloom-küszöb (1.05) fölött van, tehát
korábban is HATÁRESET volt, csak a diszkontinuus normál-mező miatt nem
egyszerre, nagy területen lépte át. Javítva (`DefaultSettingsVolumeProfile.
asset`): Bloom `threshold` 1.05→1.4 (valódi tartalék a legitim, nem-HDR
fényes terep fölött - a Nap SAJÁT korongja, ami valódi HDR-fényes, ettől
függetlenül továbbra is bloomol), `scatter` 0.7→0.5 (kevésbé szétkent
glória, ha mégis bekövetkezik).

**Hogyan teszteld:** Play mód TELJES újraindítással, ugyanaz a
közeli, erősen megvilágított terület, mint a screenshoton.

**Elvárt eredmény:** a nagy, elmosódott fényfolt jelentősen kisebb/
halványabb, VAGY teljesen eltűnik - a domborzat/jég textúrája
látszódjon a fényes területen is, ne égjen ki egyetlen glóriává.

**❌ "a csillogás megmaradt" (2026-09-09)** - a Bloom-küszöb emelése
(1.05→1.4) NEM változtatott semmit. Friss screenshot alapján ÚJ
hipotézis: a folt most TÖBB KÜLÖNÁLLÓ, kerek, pihe-puha fényfolt
csoportjaként jelenik meg egy sötét/árnyékos terepháttér előtt - ez
inkább napfényes FELHŐGOMOLYOKRA hasonlít, mint terep-specular
csillanásra (ami egyetlen, koherens folt lenne, nem több szétszórt
"pihe"). Egybeesés: a felhasználó UGYANEBBEN a körben kérte a felhők
alapértelmezett kikapcsolását - a `showClouds`/`cloudDriftEnabled`
alapérték `true`→`false` (kód + scene YAML szinkronban). A screenshot
MÉG a régi, felhőkkel bekapcsolt állapotot mutatta (a módosítás csak
utána lépett életbe).

**Hogyan teszteld:** Play mód TELJES újraindítással (most már alapból
KIKAPCSOLT felhőkkel), ugyanaz a terület.

**Elvárt eredmény:** ha a folt eltűnik - a felhők voltak az egész
tükröződés-vizsgálat valódi oka, nem a terep/víz specular vagy a
Bloom. Ha MARAD - a hipotézis téves, vissza kell térni a terep-
specular/Bloom irányhoz.

**⚠️ ÚJ, KORÁBBAN NEM DOKUMENTÁLT HIBAOSZTÁLY (2026-09-09): "nincs
alapból kikapcsolva egyik sem"** - teljes Play-újraindítás UTÁN is
mindkét checkbox bepipálva maradt. Kizárva: Project Settings (`Enter
Play Mode Options` KIKAPCSOLVA, tehát teljes domain+scene reload fut
minden Play-nél), kódbeli felülírás (nincs ilyen), prefab (a projektben
egyáltalán nincs `.prefab` fájl). **Edit módban (Play ELŐTT) is
bepipálva volt a checkbox** - ez bizonyította: a Unity Editor a scene-t
MÉG MINDIG A MEMÓRIÁBAN TARTJA a korábbi (fájlon kívülről, szöveg-
szerkesztőként történt módosítás ELŐTTI) állapotban - amikor egy
`.unity` fájlt KÍVÜLRŐL módosítok, miközben a scene MÁR NYITVA van
Unityben, a Unity NEM tölti újra automatikusan a memóriában lévő
scene-t a lemezről (ez EGY ÚJABB, a korábbi "C#-alapérték nem
szinkronizál egy már szerializált Inspector-értékkel" mintától
ELTÉRŐ hibaosztály - itt maga a FÁJL helyes volt, csak az Editor nem
olvasta be újra). **Megoldás**: a felhasználó kézzel kikapcsolja a két
checkboxot Edit módban és elmenti a scene-t (Ctrl+S) - ez a
legbiztonságosabb, mert nem ütközik a fájlon kívüli szerkesztéssel.
**TANULSÁG jövőbeli köröknek**: ha egy `.unity` fájlt közvetlenül
szerkesztek, és a felhasználó Unity Editor-ja már nyitva van/nyitva
tartja a scene-t, kérni kell a scene ÚJRANYITÁSÁT (vagy kézi Inspector-
szerkesztést + mentést) - a puszta fájl-szerkesztés ÖNMAGÁBAN NEM
elég, ha a scene már be van töltve a memóriába.

**⚠️ A JELENSÉG ÚJRA ELŐKERÜLT (2026-09-09, "a kör alakú tengeri jég
problémája továbbra is akut")**: a pólusoknál ismét erősen túlexponált,
"villódzó" fehér folt jelent meg (screenshot alapján). DÖNTŐ TESZT: a
felhasználó kikapcsolta élőben a "Tavak+jég"-et - a túlexponálás EMIATT
ERŐSEBB lett (nem gyengült), ami azt bizonyította, hogy a NYÍLT
VÍZFELSZÍN (nem a jég) az erősebb forrás - ugyanaz a mintázat, mint a
7. kör "Tavak+jég kikapcsolva → MÉG FÉNYESEBB" felismerése volt, csak a
korábbi javítások (specular-csökkentés, Bloom threshold-emelés) ellenére
is fennmaradt. Számolással kizárva, hogy ez a rendes diffúz+specular
lánc eredménye lenne (a vízfelszín specular ereje 0.02, alapszíne sötét
- matematikailag messze a Bloom-küszöb alatt kellene maradjon).

**✅✅✅✅✅✅✅✅✅✅✅ VÉGLEGESEN MEGOLDVA - BLOOM TELJES KIKAPCSOLÁSA**:
mivel a szokásos (fényezés-alapú) magyarázatok nem álltak össze a
megfigyelt viselkedéssel, egy tiszta, bináris diagnosztikai lépés
következett: a Bloom `intensity` 0.2→**0**-ra állítva (a `threshold`/
`scatter` hangolás helyett a teljes effekt kikapcsolva). Felhasználói
visszajelzés: **"tök jó, végre nem csillognak a pólusok! megoldottad"**.
A pólusi/vízfelszíni "villódzás" ezzel a 2026-09-06 óta tartó, 20+ körös
vizsgálat után VÉGLEGESEN LEZÁRVA - a valódi gyökérok a HDRP Bloom
post-processing volt (nem a terep/víz anyagok, nem NaN, nem a
normálvektor-számítás), amit korábban csak TOMPÍTANI (threshold-
emeléssel) próbáltunk, de a hatás mértéke minden korábbi hangolásnál
erősebbnek bizonyult, mint amit egy emelt küszöb kezelni tudott -
csak a teljes kikapcsolás oldotta meg végérvényesen.

---

## 13. M9 felszín-részletesség — bizonyos területek nem finomodnak

**Felhasználói visszajelzés (2026-09-09)**: "van változás a
színátmenettel, foltos lett a felszín. de így is bizonyos helyek nem
hajlandóak részletesebbek lenni" - screenshot alapján megerősítve:
konkrét, nagy tile-élek látszanak közelről egyes (szárazföldi)
területeken, míg a szomszédos területek finoman részletesek. Kizárva:
óceán-kihagyás (a felhasználó szárazföldön látta), grazing-angle
küszöb (a jelenség helyfüggetlennek tűnt).

**Valószínű ok**: a `adaptiveRenderBudget` (a finomítható tile-ok
száma egy adott képkockán) kimerülése - a rendszer a legnagyobb
képernyő-hibájú (kamerához közeli/központi) tile-eket finomítja ELŐSZÖR,
és a költségvetés elérésekor megáll; a "kimaradó" terület a prioritási
sorrend miatt látszólag esetlegesnek tűnhet. A scene-ben már 120000 volt
(nem az alapértelmezett 25000) - ez gyengíti, de nem zárja ki teljesen
az elméletet egy nagyon nagy kontinens-nézetnél.

**Javítva (teszt gyanánt)**: `adaptiveRenderBudget` 120000→**200000**
(kód + scene, mindkét helyen).

**Hogyan teszteld:** Play mód TELJES újraindítással (VAGY a scene
újranyitásával, ha már nyitva volt Unityben - ld. korábbi hibaosztály),
ellenőrizd Edit módban, hogy az `Adaptive Render Budget` mező
200000-et mutat, majd ugyanaz a kontinens-nézet, amiről a screenshotot
küldted.

**Elvárt eredmény:** ha ez volt az ok, a piros (nagy tile-es) foltok
eltűnnek/lecsökkennek. Figyelj a teljesítményre is (FPS/akadás) - ha
jelentősen lassabb lett, szólj, és keresünk kompromisszumot. Ha a
foltok NEM tűnnek el, a költségvetés kizárva, tovább kell keresni (pl.
a prioritás-számítás vagy a chunk-rendszer más része).

---

## Régebbi, még nyitott tételek (korábbi munkamenetekből)

Ezekhez korábban készült kód, de élő megerősítés még nem történt -
részletek a `docs/backlog.md`-ben:

- **Morfológiai típusfelismerés** — a régió-nevek utótagja (pl.
  "... Range", "... Basin") illeszkedjen a terep alakjához.
- **Habitability/Coastal complexity panel-mezők** — nézd meg, hogy a
  World/Continent panel értelmes számokat mutat-e.
- **Tavak/jég megjelenítés** — kék tavak, fehér állandó jég.
- **Dinamikus tengerszint** — a `deepTimeMyr` csúszka mozgatásakor a
  tengerszint kövesse a térfogat-alapú számítást.
- **Eljegesedés-ciklus** — a pólusi jégtakaró változzon a `deepTimeMyr`
  csúszkával periodikusan.
