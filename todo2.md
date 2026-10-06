# TODO2 — aktuális, ellenőrzött feladatlista

**2026-10-06 / A26 LEZÁRVA — ND-196 (b), SEED-TÖRŐ (generátor 10 → 11).** A
folyó-forrás kvóta mostantól a vízgyűjtők csapadék-összegével arányos,
abszolút alsó kapuval. MÉRVE a HŐMODELL mezőjén (ez van a képernyőn):
nulla csapadékú forrás **48/96 (50,0%) → 0**, a nyomvonal-pontok
**55,6% → 8,1%**-a fut nulla csapadékú szárazföldön, a medián
csapadék-percentilis **0,0 → 75,4**, a forrás-szám (96) és a hossz
(43 852 → 44 995 km) változatlan. A mérőpad előbb újrafuttatható lett
(`RiverBaseline thermal`), és BITRE reprodukálja a felvételi Editor-mérést.
**Nyitva ebből:** a viewer interpolált biome-táblája (a 2,45×-ös `Desert`)
élő Play-menetet kér — ez a te átvételed.
[Döntés](docs/04-decisions.md) ND-196,
[napló](history/2026-10-06-a26-nd196-precipitation-weighted-sources.md).

**2026-10-05 / A8 ELFOGADVA (B3 is).** A felhasználó az ND-193 után
elfogadta a folyóhálózatot: *„A8-at elfogadom, regisztráld."* Az A8 és a B3
sor ezzel lezárva. NYITVA marad belőle a hosszú, egyenes medence-átvágó
(„escape") szakaszok kérdése — ez külön, mérendő modellkérdés, nem az A8
elfogadásának feltétele.

**2026-10-05 / A8 — ND-193 KÉSZ: a folyószalag a RENDERELT felszínre vetül.**
A felhasználó képi visszajelzése (piros: „összevissza folyók", lila: „nem éri
el a tavat / nagyon belenyúlik") és a hipotézise („zoomoláskor a végpont nincs
újraszámolva") MÉRÉSSEL igazolva — és a mérés egy nagyobb okot is talált:
a folyóvonal **37,04 km-rel a renderelt terep FÖLÖTT lebegett**, mert a
`riverLineRadialBias = 0,5` Unity-egység a RÉGI `elevationScale = 0,01`-hez
készült („~50 m"), a mai lánccal (1,3477e-5 × relief 111) viszont 334
világ-méter. Ferde rálátásnál ez **9,87 átlagos / 22,7 maximális képpont**
parallaxis — pont a lila jelölések. A vágás ráadásul level 8-as tó-TILE
granularitású volt: **715,1 km** olyan szakasz tűnt el, ahol a renderelt
terep a víz fölött van, és **32,9 km** maradt kirajzolva a víz felett.
**Javítva:** a szalag a renderelt terep-háromszögekre vetül, a kirajzolt
vízvonalnál vágódik, és MINDEN LOD-alkalmazás után újravetül.
Mérve utána: eltérés **10,00 világ-méter**, képernyő-elcsúszás
**0,30 / 0,69 px**, víz feletti kirajzolt vertex **0**, `projectionMisses`
és `projectionFallbacks` **0**. Zoom 988 → 222 km: a mesh tényleg
újravetült (`surfaceRevision` 13 → 15). Újravetítés ~0,42 s, képkockákra
osztva (`maxSliceMs = 8,4`). Nincs Core-/seed-változás.
[Döntés](docs/04-decisions.md) ND-193,
[napló](history/2026-10-05-a8-nd193-river-on-rendered-surface.md),
képek: `pics/nd193_before.png`, `pics/nd193_after.png`,
`pics/nd193_after_zoom.png`.
**NYITVA a piros jelölésekből:** a hosszú, EGYENES medence-átvágó
(„escape") szakaszok továbbra is átszelik a terepet — ez külön, mérendő
modellkérdés (ND-188 környéke), nem javítottam csendben.

**2026-10-03 / Deep time — ND-192 KÉSZ (bitazonos): 22,1 s → 9,0 s.** A
felhasználó választása alapján a dupla Build megszüntetése volt a következő
lépés, és megvan. A mért ok: a hőmodell cache-„találata" 6,4-9,0 s-ot vett,
mert a `SurfaceTemperatureField` felépítése (5838-8487 ms, 91-94%) kellett a
cache-KULCSHOZ, miközben a tényleges lemez-olvasás 5,7 ms (0,09%) — és a mező
eredménye találatkor eldobódik. Javítás: a kulcs session-memóriája, a mező
kései felépítése, és a Build MAGA tölti be az éghajlatot (ez az ND-162
fejlécében dokumentált, de addig nem teljesülő szándék). Klíma-betöltés
8991 → **13,2 ms**, Buildek 2 → **1**, és a kép bitre azonos
(`posHash=D91E47AA8ACEB83E`). Nincs verzió-emelés.
Ellenőrzés: viewer-kapu 0 hiba, LodChunking 664/664, Editor 0 hiba, konzol
0 piros. [Döntés](docs/04-decisions.md) ND-192,
[napló](history/2026-10-03-nd190-frame-hitch-and-deeptime-measurement.md).

**Ami a deep-time oldalon hátravan (mérve, sorrendben):**
1. `BuildStaticBaseLayer` **6935-7251 ms** — a Build 72%-a. Ez most már a
   LEGNAGYOBB egyetlen tétel a léptetésben.
2. A session ELSŐ léptetésénél a cache-kulcs előállítása **6-9 s** (a
   `SurfaceTemperatureField` konstruktora). A lenyomat a futó kódot
   ellenőrzi, tehát nem hagyható el - a konstruktor költsége viszont még
   nincs lebontva.
3. A folyó-finomítás **~190 s** a teljes képig (4 worker).

**2026-10-03 / Teljesítmény — A SZAGGATÁS ÉS A DEEP TIME MEGMÉRVE (ND-190),
a javítás MÉRŐPADRA VÁR (ND-191).** Felhasználói jelzés alapján mindkettőt
megmértem az élő Editorban.

*Szaggatás:* nyugalomban p50 **3,7-5,2 ms** (190-270 fps), hideg
LOD-újraépítéskor viszont 85 s alatt **52-159 hitch >60 ms**, max 220-526 ms.
A Profiler szerint a fő szál 121-134 ms-ot **present-várakozásban** áll, a
render szál 96-134 ms-ot GPU-várakozásban, miközben a HDRP rajzolás 3,5 ms és
a GPU-frame 3,3 → 72-103 ms-ra ugrik. Kizárva: GC (0 kollekció a
hitch-frame-ekben), a feltöltés CPU-szelete (1-2 ms), a commit, a
LateUpdate, a diagnosztika, a rajzolt mennyiség (+20-60% vertex 12-28×
frame-idő mellett). A legjobb magyarázat: az **atomikus chunk-publikálás**
miatt több száz új vertex-buffer első használata egy frame-re esik.

*Deep time (0 → 200 Myr):* egyetlen **9617 ms-os frame** (a Build a fő
szálon), majd a `Build()` **MÉGEGYSZER lefut** (9288 ms) és eldobja az első
folyómunkáját → **19 s fagyás**; a teljes kép **243 s**-nál áll össze (191 s a
folyó-finomítás). A Build 72%-a a **BuildStaticBaseLayer (6935 ms)**, 19%-a a
hidrológia (1818 ms, ebből field 1499 ms).

*Két javítási kísérlet MÉRVE és VISSZAVONVA:* a vertex-alapú szelet-kapu és a
`Mesh.UploadMeshData` a staging közben — mindkettő hatása a mérés zajában
maradt (a párok MÁSODIK menete lett jobb a kapcsoló állásától függetlenül: a
cache-melegedést mértem). A kódban csak a MÉRÉS maradt (`sliceVertices`,
`maxSliceVertices`; p50 19 012, max 54 548 vertex/frame).

**Következő lépés — ND-191: hideg kiindulású, ismételt, medián-alapú
hitch-mérőpad.** Amíg nincs, 10-30%-os javítás nem kimutatható (ugyanazzal a
kóddal 3× a szórás). Utána jön az érdemi jelölt: a publikálás
atomikusságának felbontása — ez viszont KÉPI kompromisszum (fedés-lyuk vagy
átlapolás), tehát felhasználói döntés.
A deep-time oldalon a legjobb arányú cél a **dupla `Build()` megszüntetése**
(azonnali 9,3 s nyereség, nincs képi kompromisszum) és a
**BuildStaticBaseLayer** (6,9 s, 72%).
[Napló](history/2026-10-03-nd190-frame-hitch-and-deeptime-measurement.md),
[döntések](docs/04-decisions.md) ND-190, ND-191.

**2026-10-03 / A8 — minden általam elvégezhető tétel KÉSZ, a sor a
felhasználói átvételre vár.** Ebben a körben lezárult az **ND-187** (tavi
szakasz nem folyóvonal), az **ND-188** (áttekintés escape-emissziója) és az
**ND-189** (forrás-szűrés, SEED-TÖRŐ, generátor 9 → 10), plusz megtörtént a
szünet nélküli, tiszta Editor-mérés és a deep-time ellenőrzés.

Mért végállapot (seed 0xA7C944210000, t=0, 96 ág): **47 713 km**, ebből
40,58% látható tavakon fut, és ezt a viewer NEM rajzolja folyóként —
marad **28 352 km** látható vonal ~147 darabban. Tengerszint alatt kezdődő
ág **2 → 0**. Élő Editor, szünet nélküli menet: áttekintés 96 ág ~100 s-nál
(mesh **14 126 vertex / 1 chunk**, az ND-188 előtt 110 986 / 7), finom
hálózat ~254 s-nál, teljes folyómesh ~272 s-nál (**583 780 vertex / 36
chunk**, az ND-187 előtt 919 946 / 57), 296 fps, 847 MB allokált.
Deep-time 0 → 200 Myr: új generáció indul, a hálózat újraszámol, **nulla
piros kivétel**.

Az ND-189 második körében megírt „álló-víz" forrásszűrőt a MÉRÉS visszavonta
(bitre azonos kimenet, egyetlen forrást sem utasított el) — a „tóban
kezdődik" nem modellhiba, hanem a level 8-as tó-réteg és a level 9-es finom
mező felbontás-különbsége.

**Ami hátravan: KIZÁRÓLAG a te vizuális átvételed (B3).** A lista:
`docs/06-user-verification-checklist.md` legfelső szakasza.
[Napló](history/2026-10-03-a8-nd187-nd189-lakes-and-sources.md).

**2026-10-03 / A8, ND-187 + ND-189 (SEED-TÖRŐ, generátor 9 → 10):** a
felhasználó vizuális visszajelzése („látványosan átfolynak a tavakon a folyók,
illetve arra is van példa, hogy szárazföldön kezdődik és ott is van vége")
MÉRÉSSEL igazolva: a t=0 hálózat hosszának **41,34%-a (19 283 km) látható
tavakon** futott, a leghosszabb tó-átvágás **512,9 km**, 13 ág hossza >90%-ban
tavon volt; 17 ág tóból, 2 a tengerszint alól indult (ez a kettő adta a 0,00
km-es „folyókat"). Új `lakecross` diagnosztika a `RiverBaseline`-ban.
**ND-187 (nem seed-törő):** a `ContinuousRiverPath.SubmergedSpans` megjelöli a
feltöltési szint alatti szakaszokat (Python-orákulum → bitre egyező C# port →
`ContinuousRiverV2Tests`), a mélység-küszöb **söpréssel** 40 m (0/10/40/100 m:
71,80 / 68,64 / **55,80** / 36,42% jelölt hossz). A vizuális vágás viszont a
MEGJELENÍTETT tó-rétegre épül, mert a Core jelölése 40 m-rel is 55,80%-ot fed
a látható 41,34% helyett (a követő 2 km-es escape-rácsa minden mélyedést lát).
**ND-189 (seed-törő):** a forrás-jelölt mostantól a KÖVETŐ finom mezőjén és a
durva tó-mezőn is átmegy — tengerszint alatt kezdődő ág **2 → 0**, tóban
kezdődő 17 → 15, teljes hossz 46 641 → **47 713 km**, a hálózat 96 ág maradt.
Tesztek: ContinuousRiverV2 5/5, PerBasinRiverSource 9/9 (2 új), folyó-szűrésű
menet 74/74. **A látvány elfogadása a felhasználóé; A8 ~93%.**
[Mérések](history/2026-10-03-a8-nd187-nd189-lakes-and-sources.md).

**2026-10-03 / A8 — megtalálva, miért nem látott a felhasználó EGYETLEN
folyót sem.** Nem a modellben volt: a 96 ágú hálózat elkészült, csak nem
jutott ki a képre. Két ok, mindkettő MÉRVE az élő Editorban. (1) A Play-menet
megáll, amint a Unity ablak elveszti a fókuszt (`runInBackground` = 0): a
háttérszál végzett (96 ág, 114 352 pont a várakozó sorban), de a fő szál a
Play 50,9. másodpercén ragadt, miközben a folyó-stopper 306,8 s-on állt.
Futás közben `runInBackground = true`-ra állítva 9 másodperc alatt
`refined 6 → 96`, `preview → False`, és felépült a 919 946 vertexes finom
mesh — más változtatás nélkül. (2) A fix ágszám-küszöbök (1/4/6/16/48) a mért
commit-időkkel 1,6 s és 21,1 s között EGY frissítést sem adtak (a commit
determinizmus-okból szigorúan forrás-sorrendű), tehát a kép 6 ágon ragadt —
és azok mind a kamerától elfordult féltekén voltak. **Javítva:**
`runInBackground: 1`, és időalapú (1 s) preview-publikálás. A hálózat
kimenete BITRE változatlan, nincs generátorverzió-emelés. Új nyitott
**ND-188**: az 1 km-es „áttekintés" csak 1,7× gyorsabb a véglegesnél
(36,5 s / 123 926 pont vs 60,8 s / 504 060 pont), mert a hossz 58,77%-át adó
escape-szakaszok mintavétele lépésköz-független. **A látvány-elfogadás (B3)
továbbra is a felhasználóé; A8 ~90%.**
[Mérések](history/2026-10-03-a8-river-visibility-runinbackground.md).

**2026-10-03 / A8, ND-186 (SEED-TÖRŐ, generátor 8 → 9):** az ND-180 három
MÉRT modellhibája javítva a folytonos folyó-nyomkövetőben — iránykvantálás,
összefolyási „teleport" és finomítatlan medence-átvágás. Új Python-orákulum
(`river_continuous_ref.py`, szintetikus terepek, legacy előtte–utána móddal),
C# port BITRE az orákulumhoz mérve. A valódi t=0 hálózaton: **legnagyobb él
24,113 → 0,499 km**, **30° feletti irányváltás 93 404 → 673**, **hálózatidő
71 950 → 60 831 ms**, és **a fa megmaradt** (összefolyás 19 → 18, Pit 11 → 11).
Két csendes I1-sértés is megszűnt (`Math.Cos/Sin` a jelölt-irányokban,
`Math.Atan` a hurok-védelemben — ND-23 / ND-24). Az **ND-180 LEZÁRVA**;
a medence = tó modellkérdés **ND-187**-ként nyitva. **Debug ÉS Release 2005/2005 PASS (nincs eltérés); a futó Unity Editor újrafordítása 0 hiba.
A kézi átvétel és az Editor-oldali Play-mérés továbbra is hátra; A8 ~88%.**
[Mérések, korrekciók, nyitott munka](history/2026-10-03-a8-nd186-continuous-river-v2.md).

**2026-10-03 / A8, ND-181/185:** új felhasználói elutasítás után külön
HDRP folyóshader és korai, kamera felőli Core-áttekintés készült.
A régi abszolút képkülönbség sötétedést is „látható folyó”-nak számolt;
ez a bizonyíték érvénytelen. Az új shaderrel az eredeti 50 m-es finom
hálózat valódi kék vonala alap-, távoli és közeli natív képen megjelenik.
Az áttekintést a kész finom hálózat váltja fel (ND-184 visszavonva).
**Új kézi átvétel szükséges; A8 ~80%, B3 / ND-180 továbbra is nyitott.**
A korábbi teszthalasztást a felhasználó új próbája felülírta.
[Javítás, bizonyíték és korlátok](history/2026-10-03-a8-hdrp-river-visibility.md).

**2026-10-02 / ND-179–180:** a folyómesh feltöltése 47 részben,
legfeljebb 1,9 ms-os szeletekben történik (teljes Unity-menet). A szalag
geometriája változatlan, atomikus csere és ismételt megszakítás tesztelve.
A geometriai diagnózis 24,113 km-es összefolyási ugrást és jelentős durva
escape-szakaszokat igazolt: B3 modelljavítás szükséges, az ND-180 nyitott.
**A felhasználó a kézi próbát későbbre halasztotta; a következő összevont
tesztelési átadásnál emlékeztetni kell rá.**
[Ellenőrzés, mérések, hátralévő munka](history/2026-10-02-a8-chunked-upload-and-model-diagnosis.md).

**A8, 2026-10-02 / ND-178:** az ND-177 láthatósági javítás elutasítva.
Most minimum 2 pixel teljes szalagszélesség készült; valódi Unity Play
és közeli/távoli előtte–utána képpár ellenőrizve. A felhasználói átvétel,
a szögletes nyomvonal és a teljesítménykapuk nyitottak. A lentebb szereplő
ND-177 műszaki PASS nem vizuális elfogadás.
[Új javítás és korlátok](history/2026-10-02-a8-river-visibility.md).

**Készült: 2026-09-21**, az ND-127 lezárása után. Ez a lap váltja a `todo.md`-t
és a `docs/backlog.md` feladat-tábláját mint *élő* lista. Az a két fájl
**történeti napló marad** (a hosszú diagnózisok, mérési sorozatok és a
lezárt tételek indoklása ott van) — de „mi van hátra" kérdésre innentől **ez**
a válasz.

**Hogyan készült.** Nem átmásoltam a két régi listát: minden tételt
visszaellenőriztem a kódban, a `docs/04-decisions.md` ND-állapotában, a
tesztekben és a mért naplókban. Ahol a régi lista mást állított, mint a kód,
ott a KÓD az igazság, és külön jelzem. Ellenőrzött források: `todo.md`,
`docs/backlog.md` (47 táblasor + 3 kidolgozott terv), `docs/04-decisions.md`
(ND-01…ND-127), `docs/app_base_features/WorldGen_Desktop_Release_Roadmap_revised.md`,
`docs/11-play-check.md`, `history/` naplók, `src/`, `unity/…/Assets/Scripts/`.

> **Elavult forrás, ne használd:** `docs/06-user-verification-checklist.md`
> (2026-09-07-es állapot, 1545 sor). Amit még élő belőle, az itt szerepel.

**Jelölés.** 🔴 kritikus/helyességi · 🟠 fontos · 🟡 ráér · 🔒 blokkolt (más
modul vagy döntés hiányzik) · 👁 élő Unity Play kell hozzá.

---

## A. Nyitott — ezeket önállóan el tudom végezni

**A7 történeti átadás (2026-09-29, ND-168–169; a 2026-10-01-i A24/B20 állapot felülírja az ellenőrzési részt):** a teljes implementáció
elkészült: az éves energiamegmaradó meridionális mérleg leváltja a hőmodell
`40 K · z⁴` tagját, az órás bázis és a termikus szél ugyanazt a korrekciót
olvassa, a végleges B menet éves szele a csapadék bemenete lett. A Python
referencia és vektorok, a Core vektorteszt és a lemez-cache új formátuma
elkészült. **Átvétel még nyitott:** a felhasználó a buildet, teszteket,
KAT-ot és az élő Unity A/B-t az átadás után végzi. Ezért a lenti A7 sor
korábbi mérései és százalékai a régi modellverziók történetei; az új
verziókra nem tekinthetők igazolásnak. Implementáció ~100%, bizonyított
átvétel ~90%; hátralévő **~1–4 munkaóra durva becslés** a visszajelzéstől
függően. [Részletes átadás](history/2026-09-29-a7-nd168-169-annual-transport-wind-consumer.md).

| # | Feladat | Mi a tényleges állapot (ellenőrizve) | Hivatkozás |
|---|---|---|---|
| A1 ✅ | **~~`useGpuGeometry` eleváció-eltérése~~** | **KÉSZ (2026-09-21, ND-128): az út törölve.** A `TileClassification.compute` a Core-eleváció ÚJRAÍRT (HLSL) mása volt, az ND-52 és ND-90 előtti állapotban — mérve a tile-ok ~22%-a került volna a tengerszint másik oldalára. A törlés mellett döntött az is, hogy a GPU-ág élő mérésben LASSABB volt (~50 s/újraépítés, sarok-dedup nélkül), geomorphing nélkül futott, és öt másik utat (aszinkron újraépítés, szakaszolt upload, víz-finomítás, terep-LOD-proxy, ND-75 diagnosztika) `!useGpuGeometry` feltétellel béklyózott. Ráadásul a `RebuildAdaptiveMesh` CPU-ága ELÉRHETETLEN (halott) kód volt. −398 sor a viewerben, a `Gpu/` mappa megszűnt. | ND-128, ND-120 |
| A2 ✅ | **~~Hidrológia tile-középpont terrain-bázisa (hideg Build)~~** | **KÉSZ (2026-09-22, ND-131).** A tile-középpont bázis is a validált LEMEZ-gyorsítótárba került. A formátum N TÖMBÖSSÉ általánosítva (`ArrayCount` + `Kind` a kulcsban), mert ennek a bázisnak EGY tömbje van a sarok-bázis HÁROM-ja helyett — így 18,0 MiB a fájl 54,4 helyett, és az ND-122 három biztosítéka (kulcs, ellenőrzőösszeg, 1024 bejegyzés újraszámolása) egy helyen marad. MÉRVE: `hydrology(...) terrainBasis=` **2 295 ms → 98–104 ms** (~22×), a teljes hidrológia-fázis 2 625 → 417 ms; a `lakes=11505` bitre azonos a számoló és a lemezről töltő ágon. A formátum-váltás miatt árván maradó régi fájlokat munkamenetenként egyszeri takarítás törli (a BETÖLTÉS útjáról is — mérve: csak a mentés útjáról nem futott le). Tesztek: 7 → 15, 490/490 zöld. | ND-131, ND-122, ND-64 |
| A3 ✅ | **~~Klíma-konstansok hangolása (ND-126b)~~** | **KÉSZ (2026-09-22).** A hőmérsékletutak `40 K * sin⁴(szélesség)` meridionális hőszállítás-proxyt kaptak; a termikus szél iránytartó `30 m/s * tanh(|v| / 30 m/s)` korlátot. Kanonikus mérés: hideg szárazföld **40,71% → 16,91%**, 70–90° szárazföldi átlag **−62,21 → −26,31 °C**, egyenlítő **27,58 °C**; szélmaximum **36,79 m/s**, sarki átlag **34,66 m/s**. Python-orákulum és 9 érintett vektorfájl újragenerálva; a csapadék-percentilisek változatlanok, vizuális ítéletük továbbra is B4. | ND-126b, ND-41, ND-118 |
| A4 ✅ | **~~Deep-time újraépítés: cél <1 s~~** | **LEZÁRVA (2026-09-22), felhasználói kérésre.** Az ND-132 második javítása utáni `PerfLog_20260922_163455.txt`: hideg **3,257 s**, 12 meleg Build **2,251–2,635 s**, átlag **2,479 s**; klasszifikáció **460–556 ms**. A 27 s-os regresszió ebben a menetben nem ismétlődött; az új folyómunka a teljes Build után indul. A lezárás az elért eredmény elfogadása: **a <1 s küszöböt a mérés nem igazolja**. További variancia-/allokációvizsgálat külön az A5-ben. | ND-132, [A5 mérési napló](history/2026-09-22-deep-time-allocation-profile.md) |
| A5 ✅ | **~~Deep-time variancia és allokációprofil~~** | **LEZÁRVA (2026-09-22, ND-133–135).** Főszálú Build-allokáció **~692 → ~515 MB (−25,6%)**, Editor-kontrollátlag **2375 → 2150 ms**, bitazonos mesh-hash. Részletes víz-/CPU-profil és worker-hívásláncok elkészültek; Development Player 10 kontrollja **2038 ± 33 ms**. Záró Editor-próba: 20 időváltásos Build **1,87–2,37 s**, azonos világidőhöz visszatérve legfeljebb **116 kB** követett memóriaingadozás; 0/100/500 Myr páronként azonos terep-/víz-/tó-hash. A rövid sorozat nem hosszú szivárgásteszt vagy vizuális átvétel. Az Editor belső GC-/allokátor-oka és natív eseményprofil opcionális további vizsgálat, nem elvégzett munka; a pontosított A5-határ az ND-133-ban. **A <1 s cél nincs teljesítve.** | ND-133–135, [első javítás](history/2026-09-22-deep-time-allocation-profile.md), [CPU-/Player-mérés és lezárás](history/2026-09-22-a5-water-cpu-player-profile.md) |
| A6 ✅ | **~~ND-108 — generátorverzió a mentéshez~~** | **LEZÁRVA (2026-09-22).** Közös Core `WorldGeneratorVersion` (A6 alapvonal: `"1"`, A7-től `"2"`), CLI `.worldpkg` v3, valódi Core-verzióra kötött app `CoreSavePolicy`; a helyőrző megszűnt. Hiányzó/eltérő generátorral nincs állapotbetöltés; appban a konfiguráció külön kiolvasható. A repository közvetlen Load-kapuja a szekcióadatok előtt újra ellenőriz. **1531/1531** Debug-teszt, app **451/451** + CLI **23/23** Release-ben is; élő Unity-fordítás és adapterpróba sikeres. A B2/C3/C5 verziózási előfeltétele teljesült, a seed-törő döntések és a teljes állapotmentés külön feladatok maradnak. | ND-108, [megvalósítás és ellenőrzés](history/2026-09-22-a6-generator-version.md) |
| A7 ✅ | **Hőmodell: 6. és 7. fázis + egységes overlay-enum** | **IMPLEMENTÁCIÓ LEZÁRVA (2026-09-29, ND-168–169); felhasználói ellenőrzés következik.** Overlay (ND-141) és a 7. fázis hő–szél csatolása (ND-142) korábban kész; checkpoint (ND-143) és napi adatút (ND-144) kész. **ND-158:** éves hőstatisztika, a jégmaszk körfüggése feloldva KÉT RÖGZÍTETT MENETTEL (jégmentes bemenet kikényszerítve, nincs tolerancia-függő fixpont), biome-adapter az éves levegő-átlagra, párolgás-overload explicit hőmérséklet-mezővel. **ND-159 (LEZÁRVA, a blokkoló feloldva):** a β-söprés kimutatta, hogy a mai jégtérkép egy −218,8 °C-os sarki éjszakájú mezőből készül, tehát a β = 0 nem megoldás. Választott út: **percentilis tartós-jég küszöb** (q = 0,07, az ND-126 mintájára), a szezonális hó marad abszolút. **MÉRVE:** level 6-on **1720 jégcella (7,00%)** a cél 1727 (7,03%) mellett, egyezés 69,9% → 72,6%, a pillanatnyi mező érintetlen (−34,2 °C); idő **117,1 s**. **ND-160 (ÚJ, nyitott):** a bázis radiatív tagja FELSZÍNI albedót használ bolygó-energiamérlegben → +10,3 K globális többlet. **ND-161 (ELUTASÍTVA, mérés alapján):** a durvább rácson számolt éghajlat nem járható — a legjobb változat (level 5 + magasságkorrekció) is csak 85,5% Jaccardot ad a tartós jégre, 4× gyorsulásért. **ND-162 (KÉSZ, élőben igazolva):** a viewer JÉG-maszkja átállt a hőmodellre — cache-találatnál a Build maga tölti be, egyébként az analitikus út rajzol ELŐNÉZETET, és a háttérszál készülte után jön egy újabb (már találati) Build. Két csapdát fogott meg a bekötés: az érvénytelenítés végtelen köre (→ bemenet-azonosító), és a KÉT deep-time forcing (ND-44 150 Myr vs. Milanković 10–500 kyr — utóbbi a Myr-es csúszkán aliasol, ezért az ND-44 egyelőre marad). **ÉLŐBEN IGAZOLVA:** előnézet (258,150 K) → háttérszál (6,8 s level 3-on) → cache-írás (38 kB) → újabb Build → hőmodell jege aktív (281,445 K, percentilis); új Play cache-találattal **65 ms**. Az élő ellenőrzés fogott meg egy CSENDES hibát is: az `Application.persistentDataPath` csak főszálról olvasható, ezért a cache soha nem írt és soha nem talált — az offline kapúk ezt nem mutatták (javítva). Editor-költség level 5-on ~105 s (a CLI 39,8 s-éhoz képest 2,6×); a Parallel.For Monóban nem gyorsít, ezért a worker szekvenciális. **ND-163 (KÉSZ a Core-részre; fogyasztói kapu SZÁNDÉKOSAN zárva):** a csapadék szél-tagja. A várttal ellentétben NEM kellett új numerika: a csatolt szél belső `Wind(...)` függvénye már kiszámolta a cellaközéppontbeli 3D vektort, csak a `SampleCoupled` eldobta (`out _`). Kivezetve + `SurfaceWindSample` értéktípus + `MoisturePrecipitation.ComputeFromFields` (hőmérséklet ÉS szél kívülről). A meglévő kimenetek BITRE azonossága tesztben bizonyítva; 9 új teszt. A szél-bemenet kérdése MEGMÉRVE: választott út az ÉVES átlag (ingyen jön az éghajlat-futással), szél-állandóság 0,746. A FOGYASZTÓI A/B (level 5, 2151 szárazföldi tile, negyed-besorolás): csak hőmérséklet → **86,2%** egyezés (mérsékelt változás); + szél is → **67,7%** (nagy változás, átlagosan 41%-kal más csapadék, max 37-szeres eltérés a kioltódó éves szélű helyeken). **ND-164 (KÉSZ, élőben igazolva):** a (b) kapu KINYITVA — a biome hőmérséklet-tengelye és a csapadék párolgás-tagja átállt a hőmodellre. A mérés egy valódi akadályt fogott meg: a hőmodell éves LEVEGŐ-átlaga **−1,7 °C-nál nem megy lejjebb**, tehát a `BiomeClassification` abszolút hidegvége (IceSheet −10 °C, SeaIce −2 °C) ELÉRHETETLEN — a puszta tengely-csere némán kiürítette volna a jég-biome-ot. Megoldás: a hideg vég a PERCENTILIS JÉGOSZTÁLYBÓL jön (ND-159 mintájára). Ezt egyetlen szám döntötte el: a „tundrának" minősülő 254 cellából **254 (100%) a jégmaszkon BELÜL van**, tehát a képből nem vesz el semmit — a panelt hozza összhangba a képpel (I4), és megszünteti az ND-59 óta élő KÉT jégréteget. A szezonális hóból származó tundra ELUTASÍTVA (48,4% tundra lenne). **Szárazföldi egyezés a maival: 78,3%.** **Veszteség, kimondva:** a tengeri jég 0,8% → 0,0% (a hőmodellnek nincs fagypont alatti óceánja). **ND-160 mérőkampó** (`--baseline-albedo`, alapértelmezésben bitre a mai): bolygó-albedóval a globális medián 33,7 → 18,1 °C, a régi jégosztállyal az egyezés 72,7 → 86,4%, és **64 óceáni cella lesz jég** — tehát az ND-160 VISSZAHOZNÁ a tengeri jeget; a hideg VÉG viszont nem tér vissza (a minimum bitre ugyanott marad), tehát a jégosztályos hidegvég az ND-160 után is kell. ÉLŐBEN: sarok-tábla aktív (level 5), jégküszöb 280,168 K és a párolgási mező [−1,7; +45,9] °C — bitre a CLI értékei; 438 jég-tile (430 percentilis + jitter), 0 hiba. **VIZUÁLIS ÍTÉLET (2026-09-28): a felhasználó szerint KIKAPCSOLVA fest jobban** — az átállás implementációja kész és helyes, de a mögöttes hőmodell fizikai hitelessége a szűk keresztmetszet; ld. **A24**. **ND-160 (LEZÁRVA 2026-09-29, (1) opció, SEED-TÖRŐ):** a bázis radiatív tagja BOLYGÓ-albedóval (`Temperature.AlbedoPlanet = 0,30`) számol, felszíntípus-függetlenül; a felszíni albedó a solver anomália-tagjában marad. A kódolvasás egy második helyet is talált: a `ThermalWind.PointTemperature` BEDRÓTOZVA használta a felszíni albedót, holott a bázis KÉPLETÉT differenciázza — a két hely most ugyanabból a forrásból jön (ezért a régi `--baseline-albedo` kampó a szelet nem érte el). **MÉRVE** (level 5, a „régi" oszlop a mai kódból, `--legacy-baseline-albedo` kampóval, bitre reprodukálva): T_rad 264,86 → **252,19 K**, bázis-átlag +29,74 → **+17,07 °C**, medián P50 33,7 → **18,1 °C**, maximum 45,9 → **25,5 °C**, szezonális hó 1108 → **1943**, egyezés a régi jégosztállyal 72,7 → **86,4%**, **tengeri jég 0 → 64 cella (1,6%)**, szárazföldi biome-egyezés 78,3 → **80,1%**. **A MINIMUM BITRE UGYANOTT, −1,7 °C-on** — mert a szárazföldi bázis bitazonos (a szárazföld-albedó eddig is 0,30 volt); tesztben kikötve. Tehát a SZINT rendben, a hideg vég ÖSSZENYOMOTTSÁGA nem, és az ND-159/ND-164 percentilis-küszöbök továbbra is kellenek (→ A24 (b)). Csendes hiba javítva: a modell-azonosító nem tartalmazta a bázis-albedót, tehát két A/B-futás ugyanarra a gyorsítótárra/checkpointra talált volna (ND-162 csapdaosztály). Verziók: generátor 5 → **6**, hőmodell 3 → **4**; Python-referencia és vektorok újragenerálva; **1962/1962** teszt Debugban és Release-ben. **ND-165 (ÚJ, nyitott):** ugyanaz a keverés az ANALITIKUS úton is ott van — nem javítottam csendben. **Hátravan (nem kód):** (a) a jég vizuális átvétele **az ND-160 UTÁNI modellel** (minden hőmodell-cache érvénytelen, az első Play hideg), (c) a csapadék szél-cseréje (ND-163, 67,7%). | ND-100…104, ND-141…144, **ND-158…164**; [napló 1](history/2026-09-28-a7-nd158-annual-climate.md), [napló 2](history/2026-09-28-a7-nd159-160-ice-threshold.md), [napló 3](history/2026-09-28-a7-nd163-thermal-wind-vector.md), [napló 4](history/2026-09-28-a7-nd164-biome-evaporation-switch.md), [napló 5](history/2026-09-29-a7-nd160-planetary-baseline-albedo.md) |
| A8 ✅ | **~~Folyóhálózat: gyorsítás + az alak modellhibái~~** | **LEZÁRVA (2026-10-05): a felhasználó ELFOGADTA.** Az utolsó kör az ND-193 volt (a folyószalag a RENDERELT felszínre vetül, a kirajzolt vízvonalnál vágódik, és minden LOD-alkalmazás után újravetül): sugár-irányú lebegés 37,04 km → **10,00 világ-méter**, képernyő-elcsúszás 9,87 / 22,7 px → **0,30 / 0,69 px**, víz fölé rajzolt vertex **0**. [ND-193 napló](history/2026-10-05-a8-nd193-river-on-rendered-surface.md). Az elfogadás NEM zárja le a hosszú, egyenes medence-átvágó („escape") szakaszok kérdését — az külön, mérendő modellkérdés marad (ND-188 környéke). Korábbi állapot: **2026-10-03, ND-186 (seed-törő, generátor 8 → 9): az ND-180 mindhárom hibaosztálya javítva, méréssel.** Új Python-orákulum a FOLYTONOS követőhöz (addig nem volt), szintetikus terepeken + `legacy` előtte–utána móddal; a C# port mind az 1002 vektorpontot BITRE adja vissza. Valódi t=0 hálózat (96 ág, 4 worker): legnagyobb él **24,113 → 0,499 km**, 30° feletti irányváltás **93 404 → 673** (a 0. ágon 1422 → 9), hálózatidő **71 950 → 60 831 ms**, pontszám 415 293 → 504 060, csúcsmemória 116,9 → 136,0 MB. **A fa megmaradt: összefolyás 19 → 18, Pit 11 → 11** — ezt külön ellenőriztem, mert a javítás elvileg szétszedhette volna. Két paramétert MÉRÉS döntött el: az összefolyási tolerancia 500 m (a söprés szerint ott telítődik: 100→14, 250→14, 500→18, 2000→18; és ez a követő saját érzékelési sugara), az escape-emisszió 250 m (50 m-rel 954 598 pont és 254,7 MB lett volna, az ALAK mérőszáma viszont azonos: 686 vs 690). Két csendes I1-sértés is megszűnt: `Math.Cos/Sin` a jelölt-irányokban (ND-23) és `Math.Atan` a hurok-védelemben (ND-24) — helyettük szögfelezett iránytábla és atan-mentes `CubeFaceLattice`. Egy teszt elbukott és tanulságos volt (a 16 forrásos KICSI világban nincs összefolyás 500 m-en) — a tesztet nem lazítottam, hanem két toleranciával futtatom és a záróél korlátosságát is ellenőrzöm. **2026-10-03 (ND-187 LEZÁRVA, ND-189 SEED-TÖRŐ, generátor 9 → 10):** a felhasználói visszajelzés mindhárom pontja mérve és javítva — a folyóhossz 41,34%-a látható tavakon futott (leghosszabb átvágás 512,9 km), 17 ág tóból, 2 a tengerszint alól indult. A tavi szakaszt a viewer nem rajzolja (a vágás a MEGJELENÍTETT tó-rétegre épül, mert a Core saját jelölése 40 m-es küszöbbel is 55,80%-ot fed a látható 41,34% helyett), a forrás-szűrés után tengerszint alatti ág 2 → 0, teljes hossz 46 641 → 47 713 km. [ND-187/189 napló](history/2026-10-03-a8-nd187-nd189-lakes-and-sources.md). **Hátra: a kézi átvétel és az ND-188.** [ND-186 napló](history/2026-10-03-a8-nd186-continuous-river-v2.md). Korábbi állapot (láthatóság, ND-177/178/181/185): [progresszív ágak](history/2026-10-02-a8-progressive-fine-rivers.md), [HDRP shader](history/2026-10-03-a8-hdrp-river-visibility.md). | **ND-186**, ND-180 (lezárva), ND-187 (lezárva), **ND-189**, ND-188 (nyitott); ND-124, ND-145, ND-146, ND-147; [A8/1](docs/plans/a8/01-meresi-alap-es-dontesi-kapu-eredmény.md), [A8/2](docs/plans/a8/02-szemantika-es-algoritmusterv-eredmény.md), [A8/3](docs/plans/a8/03-core-megvalositas-es-differencialis-tesztek-eredmény.md), [A8/4](docs/plans/a8/04-viewer-bekotes-es-elo-atvetel-eredmény.md) |
| A9 ✅ | **~~Tó-blokkosság~~** | **KÉSZ (2026-09-22, ND-129).** MÉRT gyökérok: a vízfelszín a level-8 (~36 km) tó-tile-ok unióját rajzolta, és a tile-határon VÉGET ÉRT — a partvonal 90%-a nyers tile-él volt (csak a tó-sarkok 10,3%-ánál metszette a terep). Javítás: a vízfelszín 1 gyűrűvel TÚLNYÚLIK a valódi parton, és a már renderelt, adaptív terep vágja ki belőle a partot — **nulla új eleváció-kiértékelés**, a részletesség a terep-LOD-dal (level 20-ig) zoomra magától finomodik. Plusz N=4 al-osztás a part menti tile-okon a 25 m-es húr-behúrás ellen. Mérve: 18 307 rajzolt tile, 240 202 quad, 477 ms. | ND-129, ND-49 |
| A17 ✅ | **~~Biome-blokkosság: „nagy négyszög alakú régiók"~~** | **KÉSZ (2026-09-22, ND-130).** Felhasználói visszajelzés + kép alapján felvéve. MÉRT gyökérok: az ND-126 óta a biome-osztályozás bemenete a csapadék, az viszont a `level`=5 REFERENCIA-rács **diszkrét, ~288 km-es tile-értéke** volt (a hőmérséklet ezzel szemben pontszerű, folytonos) — egy küszöb-osztályozó bemeneteként ez a biome-határt pontosan a tile-élekre teszi. Javítás: a mező BILINEÁRIS interpolációja a tile-sarkok között (ugyanaz a minta, amit a felhő-réteg már használ), plusz a vágópontok és a csapadék-overlay ugyanerre a függvényre állítva. Core-kiegészítés: `TileGeometry.ToFaceUV` (a `FromPosition` első fele kiemelve) + tesztek. Mérve: a mintapontok **21,0%-a** vált csapadék-sávot. | ND-130, ND-126 |
| A18 ✅ | **~~Óceán-partvonal blokkosság~~** | **KÉSZ (2026-09-25, ND-149) — a vizuális átvétel hátra.** MÉRT gyökérok: a látható él a VÍZFELSZÍN pereme, nem a terep-szín váltása — a vízlap azoknak a level-8 tile-oknak az uniója volt, amelyeknek a KÖZÉPPONTJA óceáni, és opak quadként a tile-határon ért véget (A/B: vízrétegek elrejtve a színhatár SIMA). Javítás az ND-129 mintájára: a vízfelszín = óceáni tile-ok + **parti gyűrű**, a partot a már renderelt terep vágja ki a z-bufferrel — nulla új eleváció-kiértékelés. A terjesztés **ÉLENKÉNTI** (egy él akkor nyers perem, ha mindkét közös sarka víz alatt van), és az óceán globális, állandó vízszintje miatt KONVERGÁL (a tavaknál nem — ez a döntő eltérés). Al-osztás nem kell: a terep- és a víz-quad azonos UV-n és azonos háromszögeléssel fekszik, a ~25 m-es húr-behúrás kiesik a különbségképzésnél. **MÉRVE:** nyers vízperem **22,34% → 0,00%**, a terep által teljesen takart perem **24,28% → 94,73%**; +14 268 víz-tile (+5,57%), a maszk **4,2 ms**, a `BuildStaticBaseLayer` mediánja 1333 → 1394 ms (a fázisok futtatásonkénti szórása ENNÉL NAGYOBB). Inspector-kapcsoló: `coastalWaterRing` (A/B-hez). Nem seed-törő. | ND-149, ND-129, ND-82/ND-83; [diagnózis](history/2026-09-25-a18-ocean-shoreline-diagnosis.md), [megvalósítás](history/2026-09-25-nd149-ocean-coastal-water-ring.md) |
| A10 ✅ | **Régió-szegmentálás: az ND-05 hibrid maradék két tagja** | **Kész (ND-152, 2026-09-27).** A biome-klaszter és a domborzati törés az összevonás ÉLSÚLYÁBAN: alapsúly 4, azonos biome +3, domborzati törés −3 (a törés a cella-közi magasságkülönbségek p75-e, tehát relatív/szintfüggetlen). Mérve a legnagyobb landmasson: a régióhatár-élek 21,0% → **43,2%**-a ül törésen, 25,4% → **41,2%**-a biome-váltáson, a kompaktság nem romlik (2,42 → 2,39). `SoilFertilityThresholds` v3-ra kalibrálva. Az ND-05 ezzel LEZÁRVA. | ND-152, ND-127, ND-05 |
| A11 ✅ | **Kameramód: pálya menti (éves) követés** | **Kész (2026-09-27, ND-153).** A világ a pálya-keretben renderelődik, a spin a módba lépéskor befagy, az idő éves ütemben fut (60 s/év). Mért: 6,09 nap/s, a szub-napponti szélesség ±23,4°-ig söpör, a Planet-rotáció bitre állandó, `Free`-re visszaváltva identitás. A három nyitott kérdés lezárva (kameracélpont: nincs mit fagyasztani; napi ciklus: nem fut; lépték: a pálya-keret miatt az ND-19 nem blokkol). | ND-153, ND-62 |
| A12/2 ✅ | **~~ND-19 — floating origin, geometria-eltolás~~** | **KÉSZ (2026-09-27, ND-19 LEZÁRVA).** Megdőlt az 1. kör saját állítása: NEM kellett „test-keretes renderelésre" váltani — az origó a test-keretben van, ezért `A + s·R·(p − O)` maga is egy Unity-transzform, a Planet `rotation`-ja változatlanul a spin hordozója (ND-153 érintetlen). A konvenció KÉT réteg-gyökér: a **Planet** lokális tere ABSZOLÚT marad (ezért EGYETLEN `InverseTransformPoint`-hívást sem kellett átírni a LOD-ban, az overlay-ekben, a diagnosztikában), az eltolást a `localPosition = A − s·R·O` hordozza; a **finomított réteg** új, TESTVÉR gyökér alá került (`PlanetRefinedLayer`), origó-relatív csúcsokkal. Gyerekként a `s·R·O + (A − s·R·O)` kioltás ~0,57 m-t hagyna és `R`-rel változna → forgás közben remegés. A statikus alapréteget azért NEM toljuk el, mert rebase-enként ~1,3–1,6 s újraemisszió kellene (mérve) — a kamera közelében úgyis a finomított réteg takarja. **MÉRVE élő Play-ben:** 28,1 km-en `gainFactor=256,0` (0,566 m → **2,21 mm**), 89 km-en 64,0 (8,8 mm); `planetLocalPosition = A − O` BITRE; a kamera 0,13 egységre (9,6 km) a render-origótól; a finomított chunkok mesh-bounds középpontja 0,6–2,7 egység (korábban ~100). **Kép A/B, fagyasztott idővel:** 89 km-en átlag **0,0037/255** eltérés (a pixelek 0,001%-a >8), bolygó-nézetben 0,65/255. **Bit-azonossági szerződés:** bolygóközepű origónál minden csúcs bitre a korábbi (2 teszt köti ki); a finomított réteg viszont MOST végig double (`ComputeDisplacedRadius` → `double` volt a rejtett szűk keresztmetszet: a SUGÁR kerekedett float32-re), ezért ott ≤1 ULP eltérés lehet. Az élő mérés KÉT valódi hibát fogott: (1) domain reload után visszamaradt, aktív `IndependentWater1` a Planet alatt → `MigrateRefinedChildrenFromPlanet`; (2) a független vízréteg nem épült újra rebase-nél (az origó nem része a víz-kiválasztásnak) → mély óceán fölött „vízfelszín helyett tengerfenék"; javítás előtt 65,2/255, utána 0,0037/255. Tesztek: +19 (`FloatingOriginGeometryTests`), 1817/1817 zöld, Unity 0 hiba. | **ND-19** (lezárva); [napló](history/2026-09-27-a12-2-nd19-floating-origin-geometry.md) |
| A13 ✅ | **ND-20 — Burst `FloatMode.Strict` CI-kikényszerítés** | **Lezárva (2026-09-27).** A kapu `tools/ci/check_burst_strict.py`, a CI-ben önálló `burst-strict` job. CI-szkript, nem Roslyn analyzer — az analyzer nem látná az `Assets/`-et (a CI-gépeken nincs Unity). A kapu előre, nulla találaton készült el, `--self-test` 28 fixture-on bizonyítja, hogy fog. | ND-20, CLAUDE.md |
| A14 ✅ | **~~ND-21 — HDRP volumetrikus felhő űrből~~** | **KÉSZ (2026-09-27, ND-21 lezárva: ELUTASÍTVA).** A prototípus lefutott élő Editorban (Unity 6000.0.77f1 + HDRP 17.0.1, `supportVolumetricClouds` ideiglenesen be, eldobható Volume, `planetRadius = 100`, `planetCenter = 0`), utána MINDEN visszaállt (`git status` tiszta, a flag újra `0` mind a NÉGY assetben — a `todo2` korábban hármat mondott). Négy független, mért blokkoló: (1) **az `altitudeRange` alsó korlátja 100 m, keményen vágva** — MÉRVE `kért 0,05 → tényleges 100`, azaz a mi léptékünkben (1 egység = 74,2 km) a legvékonyabb héj `r = 100,02…200,02`, **7420 km vastag** felhő; (2) a `ComputeNormalizationFactor` **a Föld sugarát drótozza be** (`k_EarthRadius = 6378100`), ezért a felhőtérkép UV-je elfajul — MÉRVE: `Advanced` módban NULLA felhő, kívülről, belülről, északról és délről is; (3) a felhőtérképes út — az EGYETLEN, amit a csapadék-mezőnk hajthatna — a shaderben kizárja a fél bolygót (`„we cannot support the full planet due to UV issues"`, `if (positionPS.y < 0.0f) return;`); (4) ami renderel, az a `Simple` preset, ahol a lefedettség **konstans a shaderben** (`float4(0.9, 0, 0.25, 1)`) + procedurális zaj — **I3-sértés**, és MÉRVE fekete pettyeket ad a felszínen, mert a méter-alapú zajfrekvencia nálunk ~74 km-es szemcse. Járulékos: a felhő behúzza a HDRP ég-/bolygó-modelljét, ami MÉRVE átrendezte a saját M3-as megvilágításunkat. **GPU-költség nem mérhető** a rendelkezésre álló műszerrel (3,4–6,1 ms szórás); egyetlen tiszta pár 2,71 → 3,16 ms, tájékoztató. Következmény: a Planet nézet felhői a saját úton maradnak, az A16/M13 volumetrikus tétele innentől saját, gömbi raymarch — nem „kapcsoljuk be a HDRP-t". | **ND-21** (lezárva), ND-01, ND-19; [napló](history/2026-09-27-a14-nd21-hdrp-volumetric-clouds.md); ld. A16 |
| A15 ✅ | **~~M13 Fázis 2–4~~** | **KÉSZ (2026-09-27, ND-151) — a vizuális átvétel hátra (B16).** A 2. és 3. fázis (GPU compute a teljes kiértékelésre, GPU-vezérelt mesh) **ELVETVE, nem halasztva**: arra a pipeline-ra épültek, amit az ND-128 MÉRÉS ALAPJÁN törölt (a `Gpu/` mappa nincs meg), és a hibaosztályuk azonos — a GPU-n számolt érték a MODELL BEMENETE lenne, ami az I1-et és a HLSL lebegőpontos szabadságát állítja szembe egymással. Amit a 3. fázis valójában akart (mesh-felbontás ALATTI felszíni részlet), azt a 4. fázis megadja per-pixel árnyalással, ahol a GPU-érték KIZÁRÓLAG kimenet. Megvalósítva: Core `SurfaceMicroDetail` (konstansok + válasz-leképezés + seedből a zaj-fázis, `RandomDomain.Decorative`), motorfüggetlen `MicroDetailBand` (nézetfüggő sávválasztás), `PlanetGridMesh.MicroDetail.cs` (uniformok), és a `VertexColorUnlit` per-pixel fBm-je. A létra két vége SZÁRMAZTATOTT: alul a modell saját másodlagos relief-zaja utáni következő oktáv (4,0744 ciklus/radián), felül a float-pontosságból adódó 2^17 (~49 m). A sávváltás folytonossága bizonyított (átúsztatott oktáv-súlyok, külön teszt). **MÉRVE:** helyi kontraszt bolygó-nézetben 7,18 → 10,27 (1,43×), közeli nézetben 3,20 → 18,46 (5,76×); GPU-időben nincs mérhető különbség (a képkocka-szórás nagyobb); memória-költség NULLA (se mesh-csatorna, se textúra). Menet közben két, méréssel talált hiba javítva: a 3 pixel/legfinomabb-oktáv homokpapír-szemcsét adott (→ 8,0), és a limbnél súroló nézetben villogott (→ `N·V`-halványítás ugyanabból a lábnyom-kritériumból). Overlay alatt és a vízfelszínen kikapcsol; kikapcsolva a kép BITRE a korábbi. **Nem seed-törő, bizonyítva:** mindhárom világ-hash az ND-150 óta dokumentált érték; `WorldGeneratorVersion` marad `"5"`. Tesztek: +20 Core, +13 viewer; 1761/1761 zöld, Unity 0 hiba, shader `msgCount=0`. | **ND-151** (lezárva), ND-128, ND-19; ld. B16, A16 |
| A16 ✅ | **~~M13 — volumetrikus felhő + AO~~** | **KÉSZ (2026-09-27, ND-154 + ND-155), egy felhasználói visszajelzés-körrel**; a SZÍNKALIBRÁCIÓ tudatosan halasztva (ND-156), ld. lentebb B18.** Saját, gömbi raymarch: a lefedettség, a felhő ALJA (Lawrence-féle LCL, a talajjal együtt emelkedve — a takaró ráborul a hegyláncra) és a VASTAGSÁGA (konvektív + orografikus tag) a MÁR KISZÁMÍTOTT csapadék-/eleváció-mezőből, **nulla új kiértékeléssel**, egyetlen négycsatornás RGBA32 kocka-atlaszban (396×66 = 104 KB, a hő-overlay ND-104-es, már tesztelt atlasz-geometriájával). A shaderben csak a függőleges profil ALAKJA és a lefedettség-mező felbontása ALATTI részlet van, a számok uniformként a Core-ból (`CloudVolume`). **Az AO-t a MÉRÉS fordította meg:** az új, zárt alakú `SurfaceSkyOpenness` szerint a szárazföldi égbolt-nyitottság level 6-on 0,999999, level 10-en 0,999992 — makro-léptéken okkludáló domborzat NEM LÉTEZIK (a relief-létra ~1564 km-nél véget ér), ezért az AO az ND-151 per-pixel mikro-reliefjébe került, a planetáris okkluder pedig a FELHŐ (Beer–Lambert árnyék a direkt tagon, borult diffúz padlóval). **Hat hibát a mérés talált menet közben:** a pass nem rajzolódott (`Cull Front`+`ZTest Always` → a hátsó lapokat a mélységteszt eldobta); a menet átlépett a 24 km-es héjban a 700 m-es dekk fölött; a level 5-ös forrásmező nearest átvétele szögletes árnyék-foltokat adott; a multiplikatív részlet egyenletes szürke fátyolt adott (→ zárt alakban levezetett, cella-átlag-tartó sub-grid küszöb); az egyszeres szórás definíció szerint feketének mutatta az optikailag vastag felhőt (→ oktávos többszörös-szórás); és az ÉJSZAKAI oldal felhői is teljes napfényt kaptak (→ nappali tényező, ami a szórt tagot is kapuzza — az éjszakai felhő sziluettként takar). **MÉRVE:** korong-luminancia 75,55 → 96,89, fényes pixelek 14,01% → 25,13%; GPU +0,21 ms (2,46 → 2,67 ms, a képkocka-szórás felső határán). Egy `/code-review high` kör 11 megalapozott megállapítást hozott, kilenc javítva (köztük: a felhőárnyék világ-téri Nap-iránnyal számolt bolygó-lokál `up`-ot; a nyitottság-csatorna számolva volt, de a shader nem olvasta, ÉS sodródásonként újraszámolt ~786 000 mintát; a sub-grid mean-preservation egyenletes zajt feltételez, a shader viszont közel normálisat adott — ez a modellezett felhő ~négyötödét elnyomta), kettő dokumentált korlát maradt. **MÁSODIK felhasználói visszajelzés-kör (2026-09-28):** a felhők kipúpo sodtak a bolygóból (a VASTAGSÁGRA is a terep 111-szeres nagyítását alkalmaztam — most külön, kisebb skála), adott felhő magassága ugrált kameramozgatásra (a nézetfüggő lépésszám eltolta a mintavételi magasságokat — most FIX 32 lépés, teszt őrzi), és a felhők álltak: kiderült, hogy a modell beépített időjárás-sodródása additív eltolás + újranormálás, ami π/2-nél TELÍTŐDIK (t ≳ 100 fölött a zajmező elfajul), tehát hosszú távú óraként használhatatlan — helyette gömbi FORGATÁS (`CloudVolume.Advect`, izometria), a Nap órájáról hajtva. MÉRVE: egy napforduló alatt a korong változó hányada 1,91% → **25,89%**. A „deep time-ban változatlanok” észrevétel NEM a felhőréteg hibája — ld. B19. **HARMADIK kör (2026-09-28):** a mozgás észrevehetővé téve és a deep time valódi bemenetté vált. A mérésem félrevezető volt (egy TELJES nap ugrásával mértem, a nézőben viszont egy nap 100 másodperc): valójában 0,9 pixel/s sebességű mozgás volt, 1,35 pixeles ugrásokban, mert az advekciót az atlasz 1,5 s-os újraépítése hajtotta. Most a SHADER forgatja az atlasz mintavételi irányát (folytonos, nulla CPU-költségű), az alapértelmezett sebesség pedig prezentációs érték (1,5 rad/nap a fizikai 0,3 helyett — ugyanolyan tudatos torzítás, mint a domborzat 111-szeres nagyítása). MÉRVE azonos Nap-álláson, 8 másodpercnyi advekció-különbséggel: a korong **77,6%-a** változik. A deep time-ra ld. ND-157 és B19. **NEGYEDIK kör:** a shaderbe költöztetéskor csak az ATLASZ mintavételi irányát forgattam el, a cellán BELÜLI részlet-zajét nem — a felhőfoltok apró mintázata a bolygóhoz volt szögezve, miközben a lefedettség elcsúszott fölötte (a tengeren a legfeltűnőbb, mert ott a lefedettség sima). MÉRVE, eltolás-kereső illesztéssel egy tenger fölötti folton: előtte a legjobb illeszkedés **(0,0) px** (a mintázat nem transzlálódott), utána **(+8,+8) px** 68,7%-os hibacsökkenéssel. **Első visszajelzés-kör (2026-09-27):** kiderült, hogy a jelenetben a domborzat 111-SZERES függőleges nagyítással van rajzolva, a felhő magasságát viszont nem szoroztam vele — a dekk a rajzolt hegyek 1/111-ed magasságában, gyakorlatilag a felszínen ült. Javítva; ezen felül a felhőalap terepkövetőből VÍZSZINTES középszintű lappá vált (2500 m, a WMO 2–7 km-es osztályának alsó pereme, a mért max. 2308 m-es domborzat fölött), létezhet vékony felhő is (80 m, τ = 1,1), és az „opacitás” csúszka az OPTIKAI MÉLYSÉGET skálázza (0,45 alapérték, 1,0 = fizikai). **Nem seed-törő, bizonyítva:** mindhárom világ-hash bitre a dokumentált érték, `WorldGeneratorVersion` marad `"5"`. Tesztek: +68; 1901/1901 zöld, Unity 0 hiba, mindhárom shader `msgCount=0`. Kikapcsolva a kép BITRE az ND-154 előtti. | **ND-154**, **ND-155**, **ND-156** (mind lezárva), ND-21, ND-151, ND-104; [napló](history/2026-09-27-a16-nd154-volumetric-clouds.md); ld. B17, B18 |
| A24 ✅ | **A hőmodell fizikai hitelessége** | **LEZÁRVA (2026-10-02, ND-165/174/175).** Periodikus szezonális energiamérleg tárolással és konzervatív transzporttal; fizikai hó-utánpótlás/olvadás, külön vízi fagyási feltétel, percentilis nélkül. Viewer, biome, talaj, párolgás és climate_k3 cache bekötve; az analitikus albedóhiba javítva. Generátor 8, hőmodell 6. **1995/1995 teljes regresszió + 1/1 külön L6 jéghatárteszt Debug és Release**, Python KAT 9/9, újragenerált vektorok byte-egyezők. Tényleges Unity 6000.0.77f1 Play és HDRP-kép ellenőrizve külön jelenetmásolaton, hideg és meleg cache-sel. L5/L6 éves globális Ta 11,702/11,668 °C; max szezonális reziduum 5,1·10⁻¹⁰ W/m² alatt. Az aktuális seednél nincs tartós jég; L6-on 8204 szezonális hó/fagyás cella. **A24 100%** a dokumentált csökkentett modell keretében; nem teljes gleccser-/tengerijég-dinamika. Durva ráfordításbecslés e körre 12–20 emberóra, hátra 0 óra. | ND-165, ND-174/175; [lezárási napló](history/2026-10-02-a24-seasonal-physical-ice.md) |
| A23 ✅ | **~~A panel biome-térképe az ELŐZŐ Build csapadék-vágópontjaival készül~~** | **LEZÁRVA (2026-10-05, ND-194).** A csapadék-mező, a sarok-tábla és a három percentilis-vágópont a `Build()`-ben a geometria-ciklus ELÉ került, ahol a `biomeOf` feltöltődik. MÉRVE friss `PlanetGridMesh` példány ELSŐ Buildjén (level 5, 6144 tile) — előtte: `Desert=1672 Tundra=255 IceSheet=224` (a teljes meleg szárazföld sivatag, `(0,0,0)` vágóponttal); utána: `Grassland=418 Rainforest=417 Savanna=386 Desert=335 TemperateForest=116`, ahol a 335 PONTOSAN annyi, amennyit az ugyanabban a Buildben érvényes arid vágópont (0,1536) kijelöl. Teljes I4-ellenőrzés (`Classify` újrafuttatása a visszaolvasott bemenetekkel): **mismatch = 0 / 6144**. A mozgatás bitazonos a folyó-forrás kiválasztás és a felhő-réteg felé, mert a blokk minden bemenete már korábban elő volt állítva. | I4; ND-194, [napló](history/2026-10-05-a23-precipitation-before-biome.md), [előzmény](history/2026-09-28-a7-nd164-biome-evaporation-switch.md) |
| A25 ✅ | **~~Indítás után a biome átrendeződik (zöld → sárga), mert az éghajlat-kulcs nem perzisztens~~** | **LEZÁRVA (2026-10-05, ND-195, az (a) opció).** A gyorsítótár-kulcstábla mostantól lemezen is megmarad (`climate_keys.v1.txt`), a fejlécében egy modell-próba azonosítóval, amit UGYANAZ a `ComputeModelIdentity` számol, mint a valódi kulcsot — így a paraméter-lista nincs kétszer leírva. MÉRVE friss példány első Buildjén (level 5, 6144 tile): a betöltés **6662/7119/7872 ms → 14/16 ms**, `rebuildRequested` `True` → **`False`**, a háttérszálas job el sem indul, a biome-átrendeződés **1402 tile → 0 tile**, és az első Build biome-térképe **0/6144 eltéréssel** egyezik a korábbi kétlépéses út végállapotával. A mérés közben feltárt második rés is lezárva: a kulcs túléli a sessiont, a `.bin`-t viszont a kvóta törölheti — ezért a szinkron út kapuja a fájl létezését is ellenőrzi (`ClimateCacheFileExists`), különben a főszálon épült volna mező (6,2–7,4 s) és teljes éghajlat (~118 s). Mindhárom út megmérve: találat, elavult fejléc (0 bejegyzés), hiányzó `.bin` (4171 ms, előnézet, nem fagyott). 664/664 teszt zöld, Unity 0 hiba. | ND-195, [napló](history/2026-10-05-nd195-startup-biome-flip.md) |
| A26 ✅ | **~~A folyók a SZÁRAZ biome-okban gazdagodnak (a medence-kvótának nincs csapadék-kapuja)~~** | **LEZÁRVA (2026-10-06, ND-196, a (b) opció; SEED-TÖRŐ, generátor 10 → 11).** A forrás-kvóta a vízgyűjtők CSAPADÉK-ÖSSZEGÉVEL arányos, abszolút alsó kapuval. Először a MÉRŐPAD lett újrafuttathatóvá: a `RiverBaseline` új `thermal` módja ugyanazt a Core-láncot futtatja, amit a viewer háttérszála, és BITRE ugyanazt a három számot adja, mint a felvételi Editor-mérés (52,3% nulla csapadékú szárazföld, 48/96 forrás, 468 619 nyomvonal-pont). MÉRVE előtte → utána: nulla csapadékú forrás **48 (50,0%) → 0 (0,0%)**, a nyomvonal-pontok **55,6% → 8,1%**-a fut nulla csapadékú szárazföldön, medián csapadék-percentilis **0,0 → 75,4**, miközben a forrás-szám (96) és a hossz (43 852 → 44 995 km) MEGMARADT. A naiv (b) — előre kiosztott kvóta — ezt NEM adta: 96 → 66 forrás (−31%), ezért a száraz medence nem foglal medence-helyet (66 → 70) és a kiosztás inkrementális (70 → 96). 1995/1995 Release ÉS Debug, 4 új teszt, a hőmodell-checkpoint vektorai újragenerálva (a diff pontosan a verzió-sztring). **Nyitva:** a viewer interpolált biome-táblája (a 2,45×-ös `Desert`) csak élő Play-menetben mérhető — felhasználói átvétel. | ND-196 (lezárva), [napló](history/2026-10-06-a26-nd196-precipitation-weighted-sources.md); kapcsolódik A24 / ND-165 |
| A22 ✅ | **Deep-time paraméterlista: kontextus-struct kellene (kódolvasás, 2026-09-26)** | **KÉSZ (2026-09-26).** Az ND-137 két köre után az `ElevationWithBoundaryFromWarpedAtTime` **16 paraméteres** volt, és ugyanez a hármas — `(plateTimeMyr, erosionTimeMyr, staticSeaLevelMeters)` — végigvonult a `BaseAndUpliftFromWarpedAtTime`, a `TerrainPointBasis.EvaluateAtTime`, a `SeaLevelCalibration` és a `RiverPathTracing` teljes láncán, plusz a viewer négy statikus segédfüggvényén. Mostantól egyetlen `readonly struct DeepTimeContext { PlateTimeMyr, ErosionTimeMyr, StaticSeaLevelMeters }` utazik (`src/WorldGen.Core/Tectonics/DeepTimeContext.cs`) — az elevációs belépő 16 → **12 paraméter**, és a következő deep-time tag hozzáadása egy struct-mező, nem ~25 call-site-nyi átvezetés. **A tengerszint SZÁNDÉKOSAN property, nem mező:** az abrázió konvenciója NaN = kikapcsolva, és nyers `double` mezővel a `default(DeepTimeContext)` 0,0 m-t adna, azaz CSENDBEN bekapcsolná az abráziót a 0 m-es szint körül — a struct ezért egy `_hasStaticSeaLevel` zászlóval a nullértéket helyesen NaN-ra képezi (külön teszt fogja). Gyárimetódusok: `Static`, `AtPlateTime(t)`, `Uniform(t)`, `WithStaticSeaLevel`, `WithErosionTime`. A viewer három `_adaptive…` mezője (`ErosionTimeMyr`/`PlateTimeMyr`/`StaticSeaLevel`) egyetlen `_adaptiveDeepTime`-ra olvadt, tehát a három érték már nem tud széttartani. **BIT-SEMLEGES, verifikálva:** `worldgen hash --seed 12345 --plates 20 --level 5` a refaktor előtt és után `--time 0` → `bf7ac2b2…de7252a7`, `--time 400` → `b35e3223…5aa50f12`, `--time 3000` → `d4526336…eca73a55` (HEAD-worktree összevetés). Nincs `WorldGeneratorVersion`-emelés (bitazonos optimalizálás). 673 + 5 új teszt zöld; Unity Editor újrafordítás 0 hiba; a két offline kapu (`WorldGen.Viewer.Compile`, `WorldGen.App.UnityBinding.Compile`) zöld. Menet közben a `tools/diagnostics/RiverBaseline` (nincs a sln-ben, tehát a CI nem fogta volna meg) is átvezetve. | ND-137, C# 9 / netstandard2.1 |
| A21 ✅ | **~~`DeterministicMath.Exp` alulcsordulása: szemét 0 helyett~~** | **KÉSZ (2026-09-26, ND-150).** Az ND-137 éleset-tesztje fogta meg; a vizsgálat SZÉLESEBB hibát talált, mint a felvételkor gondoltuk. MÉRT gyökérok: a `ScaleByPowerOfTwo` a `rawExponent + k` összeget közvetlenül az IEEE-754 exponens-mezőbe írja, és nem ellenőrizte, hogy bent marad-e a normál tartományban (`[1, 2046]`) — kicsúszáskor a bitek átfolynak lefelé az **előjelbitbe**, felfelé a NaN-mintákba. Mérve előtte: `exp(-710)` = `-1,4466e+308`, `exp(-750)` = `-6,1457e+290`, `exp(-4e6)` = `+4,4595e+145`, **`exp(710)` = `NaN`**, `exp(711)` = `-1,5331e-308`, `exp(1e6)` = `6,9566e+271`, `exp(NaN)` = **kivétel**; `pow(10, 400)` = `-3,0943e-217` (rossz ELŐJEL!), `pow(10, -400)` = `-3,2317e+216`; és a `Ln` is: `ln(+∞)` = `709,78`, `ln(NaN)` = `710,19`, `ln(5e-324)` = `-709,09` a helyes `-744,44` helyett (35 nagyságrend). Javítás a `DeterministicMath`-ban, NEM modulonként: új `ScaleByPowerOfTwoChecked` (alul `0,0`, felül `+∞`), `Exp` NaN-kapu + durva `±800` argumentum-kapu (a `double → long` konverzió biztonságához — a valódi határt a pontos exponens-ellenőrzés adja), `Ln` NaN/`+∞`/**subnormális** kezelés (exakt `× 2^54` felskálázás), `Pow` `y == 0 → 1,0` rövidzár (feloldja a `0 · ∞ = NaN` csapdát). Elvetve a **fokozatos alulcsordulás** (subnormális eredmény): a bit-eltolás nem tudja előállítani, a `0,0`-ra vágás dokumentált konvenció. Az ND-137 helyi `MaxDecayExponent`-je szándékosan MARAD (eltávolítása bit-változás lenne a `(-708,396; -700)` sávban). **NEM SEED-TÖRŐ, bizonyítva:** 3000 KAT-vektor újraszámolva 0 eltéréssel, a `deterministic_math_vectors.json` újragenerálva bitre azonos (`git diff` üres), és a világ-hash `git stash`-szel mérve **mind a három időpontban változatlan** (`t=0` `2b98af9a…6213738b`, `t=400` `14dc8ad2…59a7ea07`, `t=3000` `823e8ba0…cdcfe7ab`). `WorldGeneratorVersion` marad `"5"`. Tesztek: új `DeterministicMathEdgeCaseTests` (240 mintás monotonitás- és nemnegativitás-kapu az alulcsordulási átmeneten — ez fogja meg az előjelváltást); Release-ben **1675/1675** zöld (Core 673, Viewer 526, App 452, CLI 24), Unity `compilationFailed: false`, 0 konzol-hiba. | **ND-150** (lezárva), ND-27, ND-118, ND-137; [napló](history/2026-09-26-a21-nd150-deterministic-math-edge-cases.md) |
| A19 ✅ | **A domborzati zaj nem mozog a lemezzel (felhasználói elvárás, 2026-09-22)** | **KÉSZ (2026-09-26, ND-136 (C) opció).** A zaj a lemez saját vonatkoztatási rendszerében értékelődik ki: `PlateMotion.ToPlateFrame` a pontot `R(−ωt)`-vel visszaforgatja, `CrustElevation.ComputeNoiseBasisInPlateFrame` ott mintavételez. A határon a keverés kiterjed **minden** lemezpárra (`BlendedBaseElevationFromPlateFrameBases`) — a régi „csak eltérő kéregtípusnál” kikötés kikerült, különben az azonos típusú határ is varratos lenne. Az ND-35 uplift-maszk szintén a nyertes lemez keretéből jön. **A két előzetes aggály nem igazolódott:** (a) a második zaj-bázis CSAK a keverosávon belül (`gap < 0.005`) számolódik, tehát a költség ~1× + egy keskeny sávnyi extra, nem 2×; (b) a `TerrainPointBasis` WARP-része pozíció-függő maradt, tehát az ND-122/131 lemez-cache `t > 0`-nál is érvényes — csak a három zajtag számolódik újra (`EvaluateAtTime`). Ráadásul a `RiverPathTracing` folytonos nyomvonalkövetése is megkapta a `timeMyr`-t, különben a folyó más terepen futna, mint amit a felhasználó lát. `t = 0` bitre változatlan (külön regressziós teszt); `WorldGeneratorVersion` 2 → 3. | **ND-136** (lezárva), ND-63, ND-122/131, ND-108; ld. A20, C7 |
| A20 ✅ | **Deep-time erózió: ma csak uplift-relaxáció, nem erózió (felhasználói észrevétel, 2026-09-22)** | **KÉSZ (2026-09-26, ND-137 (B) opció).** A két domborzati relief-zajtag amplitúdója mostantól **külön időállandóval** kopik: `h(t) = base_c + T_p(t)·A_p + T_s(t)·A_s`, ahol `T_i` a tag saját gömbfelszíni átlaga felé relaxál. A hullámhossz-szelektivitás a lényeg — `tau_secondary/tau_primary = f_primary/f_secondary = 5π` a **meglévő zaj-frekvenciákból származtatva**, nem új szabad paraméter —, ettől néz ki egy öreg pajzs simának, de nem laposnak. Vezérlő: `t_eff = (W(|lat|) + 3·f_ice(|lat|))·t`, ahol `W` három-cellás, pontonként kiértékelhető, bit-egzakt csapadék-proxy, `f_ice` pedig a jeges időhányad **zárt alakja** (`0,5 + asin(u)/π`) — így a `GlaciationPeriodMyr`/`AmplitudeK` ciklus végre koptat is, nem csak a jégvonalat mozgatja. **Lerakódás van benne:** ahol a tag az átlaga alatt van (medence, völgytalp), a csillapítás FELEMELI — 500 Myr-nél 161 minta emelkedett, 127 süllyedt, a relief szórása −16,5%. **Átlagtartó** (implicit izosztázia): nullához relaxálva a kontinensek ~490 m-t süllyednének és az ND-38 elárasztaná a világot; mérve a globális eltolódás 3000 Myr-nél −13,8 m. Mérhetően érezhető: kontinentális átlagos elmozdulás 117 m (100 Myr) → 222 m (2 Gyr), max ~950 m. **Menet közben javítva egy pre-existing hiba:** az uplift-relaxációt eddig CSAK a viewer adta hozzá (külön mezőpassz), a `SeaLevelCalibration`, a `RiverPathTracing` és a `worldgen hash` CLI kihagyta — `t > 0`-nál a folyó relaxáció nélküli hegyeken keresett lejtőt, a determinizmus-eszköz meg olyan világot hashelt, amit a viewer nem is jelenít meg. A kopás mostantól TELJES EGÉSZÉBEN a Core-ban van (`ElevationWithBoundaryFromWarpedAtTime`), a viewer külön passza törölve, és a három elevációs út ugyanabban a műveleti sorrendben dolgozik. Az ND-04 sértetlen: zárt alakú, a láncolás max eltérése 1,5e-14. A modul átállt a `DeterministicMath` Exp/Sin/Cos/Asin-jára, tehát **most már bit-determinisztikus**. `t = 0` bitre változatlan — a teljes világ szintjén is igazolva: `worldgen hash --time 0` a változás előtt és után egyaránt `2b98af9a…6213738b` (`--time 400` változik, ahogy kell). `WorldGeneratorVersion` 3 → 4. **2. kör (2026-09-26, „fejezd be ND-137-t"):** megjött az erózió hiányzó FELE, a **folyóvízi bevágódás** — a völgy mélyebbre vágódik, a gerinc marad, tehát a relief NŐ: `D_primary = D_p(t) · (1 + 0,6·fluvialFraction·(1 − exp(−W·t/40 Myr)))`. Mért görbe az egyenlítőn: 1,0000 → **1,2441 (t ≈ 40 Myr, csúcs)** → 0,9252 (200) → 0,4800 (3000), azaz **relief-emelkedés, majd -hanyatlás** — eddig a csúszka csak lapítani tudott. A `fluvialFraction = W/(W+3·f_ice)` **származtatott** (nincs saját konstansa): a gleccser planál, nem szabdal, tehát a jég elnyomja a bevágódást (folyóvízi hányad 1,0000 → 0,0549 a 80. foknál). **Kulcs-belátás:** a (C) opció rács-alapú `FlowAccumulation`-je nem kell — egy procedurális világban a lefolyás-hálózatot maga a zaj HATÁROZZA MEG, tehát a `primary` zajérték nem proxyja, hanem OKA a vízgyűjtőnek. Plusz **parti abrázió**: a tengerszint körüli Gauss-sávban a felszín a STATIKUS (t=0) tengerszint felé planálódik (szirt vissza, self fel), jégtakaró alatt kikapcsol, és **soha nem lő túl** a tengerszinten (külön teszt — egy villogó partvonal lenne a legszembetűnőbb hibaosztály). Két éleset a tesztekből: a negatív idő ±végtelenbe csordult (mostantól „nulla előtt nincs erózió"), és a parti abrázió `progress` kitevője is alulcsordult (ugyanaz az A21). `WorldGeneratorVersion` 4 → 5; `t = 0` bitre változatlan MINDKÉT kör után (`worldgen hash --time 0` = `2b98af9a…6213738b` végig). **Nyitva marad — döntés-köteles:** hálózat menti hordalékszállítás / delta-építés (az ND-137 (C) opciója: `O(N × tile)` minden időlekérdezésnél, és MEGTÖRI a láncolhatóságot, tehát az ND-04 értelmezését módosítja), valamint a lemez vándorlási klímatörténetének integrálása (a `W` nem integrálható analitikusan → fix-`N` kvadratúra, ugyanaz az ND-04 kérdés). | **ND-137** (lezárva, 2 körben), ND-44, ND-04, ND-108, ND-124; ld. B14, A21, A22 |

---

## B. Nyitott — te kellesz hozzá (élő Unity Play / döntés)

| # | Feladat | Mit nézz / mit döntesz | Miért nem tudom én |
|---|---|---|---|
| B1 👁 | **Lemez-overlay újranézése** | Play → tektonikus overlay. Az ND-125 óta ELŐSZÖR a valódi, szabálytalan lemezhatárokat látod (eddig az overlay a warpolatlan Voronoit rajzolta, a tile-ok 19,5–26,4%-án más felosztást). Még mindig „négyszög/háromszög"? | A B2 (méret-eloszlás) döntés előfeltétele. |
| B2 👁🔴 | **Lemez MÉRET-eloszlás: kell-e beavatkozás?** | A mérés szerint a mi eloszlásunk **egyenletesebb**, mint a Földé (a legnagyobb lemezünk 8,7–12,1%, a Csendes-óceáni 20,4%). Kell-e szabálytalanabb? | **Seed-törő**: verzió-emelés + ND-09 ordinális kalibráció újrafuttatása + minden meglévő világ domborzata változik. Ezt nem hozom meg egyedül. Előfeltétele az A6 (ND-108). |
| B3 ✅ | **~~Folyó- és tó-hálózat vizuális elfogadása~~** | **ELFOGADVA (2026-10-05).** A felhasználó az ND-193 előtte/utána képei (`pics/nd193_before.png` ↔ `pics/nd193_after.png`) után elfogadta az A8-at. Korábbi állapot: **2026-10-03: az ND-180 numerikus modelljavítása MEGTÖRTÉNT (ND-186), tehát ez a sor már nem modellhibára vár, hanem a te ítéletedre.** Mérve: a 24,113 km-es összefolyási ugrás 0,499 km-re, a 30° feletti irányváltások 93 404 → 673. A medence-átvágás most sima, egyenes vonal — ha ez egy nagy tó helyén folyóvonalnak LÁTSZIK, az a tudatosan nyitva hagyott **ND-187** (medence = folyó vagy tó?), pont erre kérek visszajelzést. Korábbi megfigyelés: a felhasználó 2026-09-23-án előbb eltűnt folyókat jelzett, majd az ND-145/A8/4 után megerősítette, hogy az előnézeti folyók megjelentek; az ND-147 után a finom folyók is látszanak, az ND-148 után a zoomolási lemaradás és akadás is javult. A finom fa alakja/sűrűsége és a tópart szubjektív elfogadása nyitott. | A metrikák nem helyettesítik a vizuális ítéletet. [ND-186 napló](history/2026-10-03-a8-nd186-continuous-river-v2.md), [A8/4 átadás](docs/plans/a8/04-viewer-bekotes-es-elo-atvetel-eredmény.md). |
| B4 👁 | **Új biome-térkép megítélése** | Play → felszín. Öt szárazföldi biome (sivatag/sztyeppe/szavanna/mérsékelt erdő/esőerdő). (a) Jó-e az arányuk, (b) sok-e a jég, (c) jók-e a színek? | A 20/45/75 percentilis konstrukció szerint 20/25/30/25 arányt ad — hogy ez jó-e, arra nincs numerikus kritérium. A jégtúlsúly külön, MÉRT ok → A3. |
| B5 👁 | **Régió-lista megítélése (ND-127)** | Play → navigációs menü → nagy kontinens. Most ~10 régió van a korábbi 65 helyett, mindegyik összefüggő, a szárazföld 100%-a benne van. (a) Jó-e a felbontás (`RegionTargetLandSharePercent = 3`, egy szám), (b) illenek-e a nevek/alaktípusok? | A darabolás finomsága ízlés. A lefedettséget/összefüggőséget én mérem. **FIGYELEM: az ND-152 (2026-09-27) óta más a felosztás** — a legnagyobb landmasson 11 helyett 9 régió, más tile-halmazokkal és ezért más nevekkel; a B5-höz friss Play-menet kell. |
| B6 👁 | **Navigációs menü teljes élő elfogadása** | Mind a négy szint (bolygó → kontinens → régió → terület): kattintás, „Vissza", breadcrumb-ugrás, hosszú lista görgetése, panel-átfedés keskeny nézetnél, stílus-egyezés a Deep time dobozzal. | A menü kódból kész (`NavigationLevel`, `AreaPanelData`), de **élő Play-ellenőrzés soha nem futott rajta**. Kódból nem nyilvánítható késznek. |
| B7 👁 | **Kilométer-léptékcsík (ND-84) élő validációja** | Több zoom/FOV/felbontás/képarány, pólus, lapél, part; ég/horizont esetén érvénytelen állapotot kell mutatnia, nem hamis számot. | Implementálva (`PlanetOrbitCamera.ScaleBar.cs`, `ScaleBarMath.cs`), de élőben nem igazolt; a hibakeret csak méréssel rögzíthető. |
| B8 👁 | **Látható hegységek, természetes partok** | Ugyanazon a helyen, több zoomszinten és megvilágításnál: felismerhetők-e a hegyvonulatok és völgyek, és a part alacsony/természetes marad-e? | Az ND-88 (1:1 relief) + ND-90 (folytonos kéregátmenet, 1 km uplift-plafon) kódban kész, élőben nincs igazolva. Ha lapos marad, az M13-árnyalás-kérdés; ha falszerű, Core-hangolás. Egyetlen globális szorzó nem oldja meg mindkettőt. |
| B9 👁 | **Hőoverlay élő elfogadása** | Play → hőoverlay: halad-e a napi ciklus, a maximum a helyi dél UTÁN van-e, kisebb-e az óceán napi amplitúdója, mozog-e a szélirányba a meleg anomália; overlay-váltáskor nincs-e akadás. | A „Kész-definíció" kimondja: élő képernyőkép, napszak-/szélteszt és friss PerfLog nélkül a tétel nem jelölhető késznek. |
| B10 👁 | **Atmoszféra- és felhő-MVP megítélése** | Play → a bolygó pereme (Rayleigh-közelítés) és a felhőréteg mozgása. | Három kalibrációs kör futott, **mindegyik csak számítással** ellenőrizve. Ez nem a teljes HDRP-modell, tudatos MVP. |
| B11 👁 | **Panel-mezők vizuális ellenőrzése** | Habitability, Coastal complexity, **Soil fertility (ma v2 küszöbökkel)**, morfológiai alaktípus-nevek („… Range", „… Basin"). | A számok forrása igazolt (I4), de a panel-sávok („Kiemelkedő"/„Alacsony") értelmessége csak élőben ítélhető meg. |
| B12 👁 | **Tavak, jég, erózió + dinamikus tengerszint** | Play → tavak (kék) és állandó jég (fehér); majd a deep-time csúszkával: mozdul-e a tengerszint (ND-38, térfogat-megmaradás). | A Core-oldal tesztelt, de a tengerszint-mechanizmus **élőben, időcsúszkával soha nem futott**. |
| B13 👁 | **Pólusi jég partvonala, éjszakai oldal sötétsége** | ND-57/58 (tengeri jég folytonos kategória, zajos partvonal) és a `surfaceAmbient = 0.04` éjszakai érték. | Mindkettő numerikusan kész, csak szemmel dönthető el (túl sötét / túl világos, elég szabálytalan-e a jégperem). |
| B14 | **Konstans-megerősítések** | (a) Eljegesedési ciklus: `GLACIATION_PERIOD_MYR = 150`, `AMPLITUDE_K = 6` — illusztratív értékek (ND-44 nyitott pontja). (b) Üvegházhatás-konstansok a teljes hőmodellben (ND-42). | Ezek modellezési ízlés-döntések, nem levezethetők — a spec sem rögzíti őket. |
| B15 | **Kiadási identitás és UI-technológia** | ND-111: végleges név, cég, ikon, splash. ND-110 döntés megvan (UI Toolkit), de a nézetek megépítésének sorrendje a te prioritásod. Plusz: Inno Setup telepítése a telepítő fordításához, és a `tools/release/THIRD-PARTY-NOTICES.md` jogi átnézése (Random123-attribúció). | Névadás/jogi/telepítői döntés. |
| B16 👁 | **Mikro-részlet látvány-ítélete (ND-151)** | Play → felszín, overlay nélkül, közeli zoom. A `microDetailStrength` (Inspector, 0-2) élőben hangolható, a `surfaceMicroDetail` pedig A/B-hez kapcsolható. Kérdés: az 1,0-s erősség domborzatnak látszik-e vagy zajnak; a súroló megvilágításnál (terminátor) nem túl erős-e; zoomolás közben nem "lélegzik"-e a minta. | A mérés (kontraszt 1,43× / 5,76×, nulla memória, nincs mérhető GPU-költség) azt mutatja, hogy a részlet OTT VAN és a modellt követi — hogy SZÉP-e, az felhasználói ítélet. A 3 → 8 pixel/oktáv javítás már egy körben megtörtént a saját mérésem alapján. |
| B17 👁 | **Térfogati felhő látvány-ítélete (ND-154)** | Play → bolygó- és kontinens-nézet, `cloudVolumetric` A/B-hez, `cloudVolumeOpacity` (0-2) és `cloudVolumeAmbient` (0-0,5) élőben hangolható, `cloudShadowStrength` (0-1) az árnyékhoz. Kérdés: a felhő elég FEHÉR-e, a lefedettség nem túl sok/kevés-e (a modellhez hű érték a `cloudDensityThreshold`/`cloudDensityGamma`-val hangolható), az árnyék nem túl erős-e, és — a legvalószínűbb kifogás — nem SZEMCSÉS-e bolygó-nézetben: a részlet-létra legfinomabb oktávja ~23 km, ami 1600 px-en ~5 pixel, tehát az ND-151 MÉRÉSSEL megállapított 8 pixel/oktáv kritériuma alatt van. Kontinens-nézetben ez nem probléma. A javítás ugyanaz a nézetfüggő sáv-eltolás lenne, mint az ND-151-nél, vagy kevesebb oktáv. A `cloudVolumeDiagnostic` (0-4) mutatja a burkolót, a menet ablakát, a nyers lefedettséget és az alfát. | A mérés (korong-luminancia 75,55 → 80,93, a kép 10,57%-a változik, +0,21 ms GPU) azt mutatja, hogy a felhő OTT VAN, szerkezete van és a modellt követi — hogy SZÉP-e, felhasználói ítélet. Öt hibát már a saját mérésem talált és javított. |
| B18 🟡 | **Terep-paletta újrafokozása (ND-156)** | A színkalibráció maradék fele: minden render-kategóriához dokumentált FIZIKAI albedó (spec §29), a paletta lineáris luminanciáinak RENDEZÉSE egyezzen az albedókéval, és egyetlen globális expozíciós tényezővel illeszkedjenek — ami kilóg, azt kell újrafokozni. | Szándékosan a B17 UTÁN: a jelenlegi paletta élőben, szemre hangolt LINEÁRIS érték (nem sRGB-albedó), tehát egy „színtér-javítás" a hangolást dobná el; ráadásul a felhő megváltoztatja a referenciát, mert a kép jelentős részét elfoglalja. |
| B19 🟠 | **A csapadék-lánc deep-time-osítása (a FELHŐNÉL MEGOLDVA, a többi hátra)** | FELHASZNÁLÓI ÉSZLELÉS (2026-09-28): „deep time-ban totál változatlanok" a felhők. A gyökérok NEM a felhőréteg: a `MoisturePrecipitation.Compute` a deep time értékét **meg sem kapja** — saját, t=0-ás eleváció-mezőből számol, kráter és erózió nélkül (a `GetOrComputePrecipitationField` cache-kommentje ezt expliciten rögzíti). A csapadék-mező tehát deep-time-invariáns, és a felhő ezt hűen tükrözi: a lemezek szétválása és a hegységek felemelkedése nem látszik sem a csapadékon, sem a felhőn, sem (közvetve) a biome-okon. **ND-157 (2026-09-28): a FELHŐ már deep-time-os.** Az új, additív `MoisturePrecipitation.ComputeFromElevationField` overload beengedi a hívó saját eleváció-mezőjét, és a felhő-atlasz ezzel a viewer deep-time mezőjéből építi a saját csapadék-mezőjét (MÉRVE: lefedettség-átlag 0,081 → 0,088 600 Myr-nál, más mintázattal). A MEGOSZTOTT mező ÉRINTETLEN, tehát a csapadék-overlay, a lapos felhőréteg és minden régi hívó bitre a korábbi. | **AMI HÁTRA VAN**: a klíma-mező numerikus viselkedését változtatja, tehát döntést igényel (mi legyen a deep-time-os domborzat forrása a klimához, és seed-törő-e). Hatóköre túl mutat az A16-on: a hidrológiát és a biome-okat is érinti. |
| B20 ✅ | **Az ND-168/169 utáni hőmodell általános vizuális/működési átvétele** | A futó alkalmazásban kért jég/biome A/B és Console-ellenőrzés után a felhasználó 2026-10-01-én: „minden rendben, mehgetsz a következő lépésre”. | Általános felhasználói elfogadás; részletes képernyőkép/perem- és performance-mérés nincs. A24 fizikai jégmodellje ettől még nyitott. |

---

**A7/B20 utóállapot (2026-09-29).** A felhasználó az ND-160 utáni
`useThermalClimateBiome` A/B-ről is azt jelezte, hogy **kikapcsolva jobb**, és
a hőmodell fizikai javítását kéri. Ez az elsődleges B20 látványítélet;
képernyőkép és részletes jégperem-átvétel nem érkezett. Az A24 hidegvég-
diagnózisa (ND-166) szerint β=0,5 → 0,2 mellett az éves minimum −1,7 →
−16,4 °C, de a meridionális K-proxy globálisan +8 K-t ad a bázishoz.
A talaj- és óceáni hőkapacitás csökkentése az éves minimumot nem javította.
A meridionális skála csak A/B-mérőparaméter; a világ alapértelmezett kimenete
bitre változatlan. Az ND-163 csapadékszél-kapuját az új, végleges B-menetes
mérés is zárva tartja: a szárazföld 29,6%-a csapadéknegyedet váltana.
Következő numerikus munka: energiamegmaradó meridionális fluxus a hőmodellben,
Python-referenciával és külön seed-verziós döntéssel.

**A7 következő részlépése (ND-167, 2026-09-29):** elkészült az abszolút
hőmérséklet-különbségből és élvezetőképességből páronként ellentétes
teljesítményt képző Core-kernel, Python-referencia és vektorteszt. A teljes
élfluxus összege numerikus hibán belül nulla. A vezetőképesség földrajzi
szabálya, a stabil időléptetés és a hősolverbe kötés **nyitott ND-168**;
ezért a hőmodell látványa és a seedkimenet még nem változott.

## C. Blokkolt — nem ütemezhető, amíg a feltétel hiányzik

| # | Feladat | Mi hiányzik alóla |
|---|---|---|
| C1 🔒 | **Talaj: maradék 7 regolit-mező** (MineralDiversity, Phosphorus/Nitrogen, Iron, Sulfur, Salinity, pHProxy) | Ellenőrizve: a `RegolithProfile` ma 3 mezős (Depth, Porosity, WaterRetention). A többihez **kőzettípus-/litológia-modul** ÉS **perzisztált vulkáni hamu-/tefra-mező** kellene — egyik sem létezik. Az I4 szerint kitalálni tilos. |
| C2 🔒 | **A többi ordinális panel-mező** (Climate variability, Tectonic activity stb.) | Nincs alattuk folytonos metrika, amit kvantálni lehetne. Előbb a metrika, utána az ND-09 mintájú kalibráció. |
| C3 🔒 | **Mentés `simulation-state` szekciója** | Az ND-108 generátorverzió-kapu (A6) elkészült. Hátravan az állapottartalom és szerializálás eldöntése, majd a session-host bekötése (a Core ma tiszta függvény a paraméterekből — lehet, hogy az állapot újraszámolható). |
| C4 🔒 | **Hangrendszer** | A modell és a lejátszók készek (`MusicDirector`, `UiSoundPlayer`, AudioMixer-applier), de **maguk a hangfájlok és a licencük hiányoznak**, és az AudioMixer asset sincs meg. |
| C5 🔒 | **M11 rift + lemez-hasadás/egyesülés rendszer-integráció** | A Core kész (`PlateLifecycle`, idő-lekérdezhető topológia, bit-egzakt vektorok), de a globális `PlateId` → `ulong` váltás és a split/merge bekötése a fő elevation-láncba **seed-törő**. A6 (ND-108) elkészült; a B2-szintű döntés és a tényleges integráció még hátravan. |
| C6 🔒 | **Extrém landmass méret-eloszlás strukturális javítása** | A diagnózis lezárva: a sima part-átmenet és a kiegyensúlyozott kontinensméret a jelenlegi **bináris** lemez-kéreg modellben ütköző cél. Csak egy lemezen belüli kevert-kéreg modell oldaná fel — nagy munka, seed-törő. |
| C7 🔴 | **Sebesség-alapú határ-interakció (konvergens / divergens / transform)** | **Döntés: [ND-138](docs/04-decisions.md) — a záródási ráta képlete, a háromirányú besorolás és az opciók ott.** Felhasználói kérés, 2026-09-22. Az M10 óta megvan az ω és az Euler-pólus, de a `PlateBoundaryEffect` **nem használja** őket: az uplift kizárólag a határtól mért távolságból (a két legnagyobb dot-product különbsége) jön, kéregtípussal (ND-32) és `MountainMask`-kal (ND-35) modulálva. A modul saját doksija is ezt mondja ki: „nincs valódi konvergens/divergens/transform megkülönböztetés — az M10 adja majd hozzá a sebesség-adatot". **Következmény: egy széttartó (rift) határ ugyanazt a hegység-bónuszt kapja, mint egy ütköző.** A spec §14.3 divergens ága (rifting, új kéreg, medence-képződés) és a transform ág (vetődés, lokális relief) nincs implementálva. **Amit kell hozzá:** a relatív sebességvektor a két lemez Euler-forgásából a határponton, vetítve a határ normálisára → előjeles besorolás; ebből ágazik szét az uplift/rifting/vetődés. **Blokkoló:** seed-törő (minden meglévő világ domborzata változik) → ND-döntés + ND-108 generátorverzió-emelés, a B2-vel azonos súlyú felhasználói döntés. |
| C8 🔴 | **Lemez-lemez ütközés és kéreg-megmaradás** | **Döntés: [ND-139](docs/04-decisions.md) — a lagrange-i reprezentáció ára és a fél-analitikus torlódás-proxy ott.** Felhasználói kérés, 2026-09-22. A lemezek a Voronoi-hozzárendelés miatt **szabadon áthaladnak egymáson**: minden tile mindig a legközelebbi (elmozdított) maghoz tartozik, tehát nincs átfedés, nincs ütközés, nincs kéreg-megmaradás, nincs szubdukciós fogyasztás és nincs deformáció-visszacsatolás a mozgásra. A kontinensek „átúsznak" egymáson ahelyett, hogy összetorlódnának. **Amit kell hozzá:** a Voronoi-pillanatkép helyett kéreg-anyagot követő reprezentáció (tile-onkénti kéreg-kor/vastagság, ami a lemezzel utazik), plusz konvergens határon vastagodás/szubdukció. **Blokkoló:** ez a legnagyobb egyedi tektonikai átalakítás, erősen seed-törő, és a C5 (lemez-életciklus integráció) valamint a C6 (kevert kéreg) ugyanebbe a reprezentáció-váltásba fut bele — érdemes egy tervben kezelni őket. |
| C9 🟡 | **Előírt vs. számított mozgás (köpenyáramlás)** | **Döntés: [ND-140](docs/04-decisions.md) — az `omega`-tartomány táblázata és a zárt alakú `omega(t)` ott.** Felhasználói kérés, 2026-09-22. A mozgás ma **kinematikai**: minden lemez a seedből kapott fix Euler-pólust és fix ω-t kap (0,01–0,09 rad/Myr), és ez a világ teljes 1 milliárd évére **konstans**. Nincs köpenyáramlás, nincs hajtóerő, nincs ellenállás, nincs visszacsatolás a lemezek kölcsönhatásából. Megjegyzés a konstansokhoz: a 7420 km-es sugárral ω felső határa ~67 cm/év kerületi sebesség (földi maximum ~15 cm/év), az alsó határ ~7,4 cm/év reális — a felső vég külön felülvizsgálatot érdemel. **Blokkoló:** a C8 reprezentáció-váltása nélkül nincs mire visszacsatolni; sorrendben C7 → C8 → C9. |

---

## D. Alkalmazásréteg (asztali kiadás) — külön sáv

Forrás: `docs/app_base_features/WorldGen_Desktop_Release_Roadmap_revised.md`,
`docs/09-app-shell-architecture.md` §15. Jelölés: **F** = motorfüggetlen C#
(dotnet-tel tesztelt), **U** = Unity-kötés, **C** = Core-kötés.

**Ahol tartunk:** az F réteg gyakorlatilag kész (`WorldGen.App.Foundation`,
438 zöld teszt), az U rétegből megvannak az *osztályok*
(`AppBootstrap`, `SceneFlow`, `SettingsAppliers`, `UnityLogBridge`,
`UnityScreenshotService`, `KeyBindingInput`, `AudioPlayers`), a C réteg
blokkolt.

| # | Nyitott tétel | Állapot |
|---|---|---|
| D1 🟠 | **Scene-szerkezet** | Ellenőrizve: az `Assets` alatt **egyetlen scene van** (`PlanetView.unity`). A Bootstrap / MainMenu / WorldSimulation scene-ek nem léteznek, tehát az `AppBootstrap` nincs bekötve. Ez az egész app-sáv első lépése. |
| D2 🟠 | **Menü-, Settings-, Load World-nézetek** | A nézetmodellek készek (`SettingsScreenModel`, `SaveSlotRows`, `MenuModel`); maguk a nézetek az ND-110 (UI Toolkit) szerint megépítendők. Ide tartozik a kamera-input tiltása modális UI alatt. |
| D3 🟠 | **Loading / Pause bekötése a valódi Buildhez** | A súlyozott szakaszok és a megszakítás F-ben kész; a valódi szakaszok a `PlanetGridMesh.Build`-ből jönnének, és kellene hozzá háttérszálas build + megszakítási pontok. 🔒 részben Core-függő. |
| D4 🟠 | **Save/Load C-oldal** | → C3. A fejléc, CRC32-szekciók, rotáció, autosave, thumbnail F-ben kész; ND-108 verziókapu kész. A teljes állapotmentés/session-host nincs bekötve. |
| D5 🟡 | **Planet Profile / Help szövegek** | A `WorldGenPanelData` → `PlanetProfile` leképezés hiányzik; a Deep Time help-szöveg felhasználói átnézést kér. |
| D6 🟡 | **Debug overlay (F3) tartalma** | A keret kész, a meglévő PerfLog/LOD-diagnosztika átvezetése hiányzik. |
| D7 🟡 | **Build és telepítő** | A build-script és a portable ZIP-script kész (kamu buildön kipróbálva), **élő Unity-build még nem futott**; az Inno-script kész, de **Inno Setup nincs telepítve** → B15. |
| D8 🟡 | **Release-QA** | `docs/app_base_features/release-qa-checklist.md` kész; az A/B/F szakasz futtatható ma, a többi a D1–D4 után. |

---

## E. Lezárt tételek — ellenőrizve (hogy semmi ne vesszen el)

Ezek a `todo.md`-ből és a backlogból **kikerülnek**; a részletes indoklás a
`docs/04-decisions.md`-ben és a `history/` naplókban marad.

**2026-09-21 — reggel, az előző napi munka élő igazolása:** ND-119 szél-overlay
(a látott szél ≠ a szimulált szél) · ND-120 GPU-osztályozó út törlése · ND-121
`MaximumRenderBudget` 48 000 → **96 000** (`starvedBudget` 6943 → 0, a
tile-méret-panasz mindkét ága lezárva) · ND-123 párhuzamos statikus emit
ön-ellenőrzése EGYEZÉST adott (a kapcsoló kikapcsolva).

**2026-09-21 — a mai fejlesztés:** ND-128 a `useGpuGeometry` út törlése (az
ND-120 másik fele; a GPU-oldali, újraírt világmodell-matek megszűnt) · ND-124 vízgyűjtő-alapú folyó-forrás (fa-alak:
összefolyás 17% → 42%, max vízhozam-súly 2 → 6) · ND-125 1. rész: a
lemez-overlay warpolatlan pozícióval rajzolt (javítva) · ND-126 kétdimenziós
biome-osztályozás (az Egyenlítő sávjában 1 → 4 biome) · **ND-127 régió-összevonás**
(lefedettség 50,1% → 100%, szétesett régió 35,7% → 0, menüpont 65 → 10) ·
`SoilFertilityThresholds` v2 újrakalibrálva (500 világ, 23 492 minta).

**2026-09-20:** ND-122 terrain-bázis LEMEZ-gyorsítótár újraszámolásos
validációval (7,4–8,5 s → ~0,2 s) · ND-50 overlay-only újraépítés (overlay ≠
teljes Build) · sűrű tó-pipeline (180–190 → 6,2–9,5 ms) · csapadék-cache
(322–410 → 0,0 ms).

**2026-09-19:** ND-115 horizont-vágás a vetített terep-úton · ND-116 a 2:1
kiegyensúlyozás korai kilépése blokkolt budgetnél · budget-újrahangolás a
vágás után.

**Korábban, felhasználó által megerősítve:** északi sarki jég-artefakt ·
vertex-szín árnyalás-regresszió · folyó/jég spekuláris „villámlás" (ND-53,
Bloom=0) · kontinens/régió kamera-átmenetek · csillagos háttér · kráter tartós
hatása · `.worldpkg` + `worldgen hash`/`verify` CLI (M12) · Continent/Island
szétválasztás · eltűnő Region/Area kis landmasson (az ND-127 óta strukturálisan
sem fordulhat elő) · tektonikus lemez-overlay · hierarchikus navigációs menü
kódja · M1–M5, M7, M10 alap, M8 numerikus rész.

**Visszavont / megszűnt:** ND-56 harmadik zaj-réteg (felhasználói döntés:
„működjön minden úgy, ahogy ezelőtt") · ND-71 mintavételezett LOD-metrika
(regresszió) · kattintható panel-nevek (a navigációs menü váltotta ki,
2026-09-13).

---

## F. Sorrend-javaslat

1. ~~**A1** (`useGpuGeometry`)~~ — **kész** (ND-128, 2026-09-21): az út
   törölve, a helyességi kockázat megszűnt.
2. **A3** (jég-túlsúly + sarki szél lefogása) — MÉRT hibák, a te ítéleted
   nélkül is javíthatók, és a B4 megítélését is megkönnyítik. Érdemes
   egyszerre a csapadék-percentilisekkel, különben kétszer kalibrálunk.
3. **A2** (hidrológia-bázis lemez-cache) — a hideg Build legnagyobb egyedi
   maradék tétele, kész mintát másol (ND-122).
4. ~~**A6** (ND-108)~~ — **kész (2026-09-22)**: B2, C3/D4 és C5 közös
   verziózási előfeltétele teljesült. Minden seed-törő változáskor emelendő.
5. Közben, amikor van Play-menetd: **B1 → B2**, **B3**, **B4**, **B5**, **B6**.
   Ez az öt visszajelzés dönti el az A8, A9 sorsát is. (Az A10 2026-09-27-én
   a B5 nélkül elkészült, felhasználói kérésre — a B5 tárgya ettől nem szűnt
   meg, csak az ítélet immár az ND-152 szerinti felosztásra vonatkozik.)
6. **Tektonika-blokk (2026-09-22, felhasználói kérés) — ND-136…ND-140.** A
   javasolt sorrend **~~A19 (ND-136)~~ → ~~A20 (ND-137)~~ → C7 (ND-138) → C8 (ND-139)
   → C9 (ND-140)**, és a döntő szempont a **seed-törés hatóköre**:

   | Tétel | ND | `t = 0` törik? | Kell ND-09 újrakalibrálás? |
   |---|---|---|---|
   | ~~A19 zaj a lemez keretében~~ **kész** | ND-136 | **nem** | nem |
   | ~~A20 deep-time erózió~~ **kész** | ND-137 | **nem** | nem |
   | C9 `omega`-tartomány / `omega(t)` | ND-140 | **nem** | nem |
   | C7 határ-besorolás | ND-138 | **igen** | **igen** |
   | C8 ütközés, kéreg-megmaradás | ND-139 | **igen** | **igen** |

   Az első három csoport egyetlen ND-108 verzióemelésbe összefogható, és a
   statikus (`t = 0`) világokat bitre érintetlenül hagyja — ezeket el tudom
   végezni. A C7/C8 a statikus világot is átrendezi, tehát a B2-vel azonos
   súlyú **felhasználói döntés**, és a C8-at a C5-tel és C6-tal egy tervbe
   kell vonni (mindhárom ugyanazt a tile-onkénti kéreg-állapotot kéri).

---

## G. Mi került át honnan

- `todo.md` 1. tábla 13 sora → A1 (4.), A2 (5.), A3 (2.), A4 (8.), A6 (9.),
  A8 (7.), A9 (6.), A12 (11.), A13/A14 (10.), B15 (12.), C1 (13.). Az 1. sor
  (lemez-ALAK) LEZÁRVA, a maradéka B2; a 3. sor (régiók) LEZÁRVA (ND-127), a
  maradéka A10 + B5.
- `todo.md` 2. tábla 6 sora → B1, B2, B3, B4 és B5 (a 4. sor, „blokkosság",
  beolvadt B3-ba).
- `docs/backlog.md` 47 táblasora → E (28 lezárt/visszavont), A4/A5/A11/A15/A16,
  B7/B8/B9/B10/B11/B12/B13/B14, C2/C5/C6.
- `docs/backlog.md` három kidolgozott terve → hőmérséklet-overlay: A7 + B9;
  navigációs menü: B6; „látható hegységek, természetes partok": B8.
- `docs/app_base_features/…Roadmap_revised.md` + `release-qa-checklist.md` → D.
- `docs/04-decisions.md` ND-jei → A6 (108, azóta lezárva), A13 (20), A14 (21),
  A12 (19), B15 (110, 111), B2 (125 2. rész), A10 (05).
- **2026-09-22, felhasználói kérés (tektonika):** A19 (a domborzati zaj
  mozogjon együtt a lemezzel), A20 (deep-time erózió hiánya), C7–C9 (a
  sebesség-alapú határ-interakció, a lemez-ütközés és az előírt mozgás
  hiánya). A C7–C9 szövegét a felhasználó idézetként adta meg; a tényállítások
  kódból ellenőrizve (`PlateMotion.cs`, `PlateBoundaryEffect.cs`,
  `DeepTimeErosionGlaciation.cs`, `CrustElevation.ComputeNoiseBasis`).
  Mind az öthöz készült döntés-bejegyzés opciókkal és javaslattal:
  **ND-136** (A19), **ND-137** (A20), **ND-138** (C7), **ND-139** (C8),
  **ND-140** (C9) — `docs/04-decisions.md`. A felmérés naplója:
  [tektonikai hiányosságok](history/2026-09-22-tectonics-gap-nd136-140.md).
















----------------------------






-

Félbehagyott munka (ezek a legfontosabbak)

A7 🟡 — Hőmodell 6–7. fázis
A hőmodell visszacsatolása (levegő-anomália → szél → hőszállítás) megvan és kalibrálva van. Az ND-158-ban elkészült az éves adatút és a jégmaszk körfüggésének feloldása (két rögzített menet), az ND-159-ben pedig a blokkoló kalibrációs kérdés is eldőlt: **percentilis tartós-jég küszöb**, mert a β-söprés kimutatta, hogy a mai jégtérkép egy fizikailag tarthatatlan (−218,8 °C-os sarki éjszakájú) mezőből készül, tehát a β csökkentése a hibát vinné át a hőmodellbe. Level 6-on a percentilis küszöb 1720 jégcellát ad a cél 1727 mellett, változatlan (ép) pillanatnyi mezővel. A Core adatút tehát KÉSZ. Ami hátravan: a viewer tényleges átkötése és vizuális átvétele, valamint a 117 s-os éves számítás beillesztése a Buildbe (szinkron nem fér bele — háttérszál és/vagy lemez-cache kell, az ND-122/131 mintájára). A korábbi „62%” nem mért szám volt; a mostani, funkcionálisan súlyozott becslés kb. 75%.

*(Utóirat 2026-09-29: ez a bekezdés a 2026-09-21-i állapotot írja le. Azóta a viewer átkötése megtörtént (ND-162 jégmaszk, ND-164 biome/párolgás), a 117 s-os számítás háttérszálra + lemez-cache-re került, és az ND-160 kalibrációs javítása is kész. Az élő, mért állapot a fenti A7 és A24 sorban van; ami hátravan: a vizuális átvétel (B20), a csapadék szele (ND-163) és a hideg vég fizikája (A24 (b)). A becslés így ~88%.)*

A8 🟡 — Folyóhálózat gyorsítása és a folyóalak modellhibái

**2026-10-03, ND-186: az ND-180 numerikus javítása KÉSZ, méréssel.** Ez a kör
nem a láthatóságról szólt (azt nem tudtam ellenőrizni), hanem a mért
modellhibákról. Mindhárom javítva: a lépésirány már nem a 8 jelölt irányra
kvantált, hanem a jelölt-kör első harmonikusából számolt folytonos
lejtésirány; az összefolyás valódi térbeli közelségvizsgálat, nem
„ugyanabban a 13-18 km-es cellában"; az escape-szakasz „víz alatti"
egyenesekre van összevonva, nem a 2 km-es rácslépcső. Új Python-orákulum
készült (a folytonos követőnek addig NEM volt), és a C# port bitre
visszaadja mind az 1002 vektorpontot.

A valódi t=0 hálózaton: legnagyobb él 24,113 → 0,499 km, 30° feletti
irányváltás 93 404 → 673, hálózatidő 71 950 → 60 831 ms, és a fa megmaradt
(összefolyás 19 → 18, Pit 11 → 11). Mellékhozadék: két csendes I1-sértés is
megszűnt a folyó kritikus útján (`Math.Cos/Sin` és `Math.Atan`). Generátor
8 → 9, tehát minden folyóhálózat új és a régi mentések/cache-ek érvénytelenek.

Ami hátravan: a KÉZI átvétel (B3), az Editor-oldali mesh/memória/deep-time
kapuk, és az **ND-187** (egy feltöltődő medence fizikailag tó — folyóvonalat
vagy tófelületet rajzoljunk?), amit szándékosan nem döntöttem el csendben.
[Napló](history/2026-10-03-a8-nd186-continuous-river-v2.md).

**2026-10-02, ND-177: javítás műszakilag ellenőrizve.** A rácsos előnézet megszűnt; már kész finom ágak jelennek meg számítás közben. A fehér/túl széles szalag helyett a közös víz-shader és 0,008 fél-szélesség fut. A panel valódi ágdarabszámot és kész állapotot jelez. Új felhasználói képi átvétel következik; A8 továbbra sem lezárt. [Mérések](history/2026-10-02-a8-progressive-fine-rivers.md). Az alábbi elutasítás a javítás előtti állapot.


**2026-10-02, élő átvétel: NEM ELFOGADOTT.** A felhasználó `pics/p.png`
képe lépcsős/téglalapos folyó-előnézetet mutat; a rövid, nem összefüggő
ágak és a torkolatok hiánya nem elfogadható. A folyók megjelennek és zoomkor
a felszínen maradnak. A Console 63 `KeyNotFoundException` hibát jelzett a
Build fizikai klíma-/regolit-adapterében. A friss
`PerfLog_20261002_202422.txt` csak a durva előnézetet igazolja, kész finom
hálózatot nem. ND-176: a klímabemenet ellenőrzése a fogyasztás ELŐTT,
és a regolit valódi K adatának olvasása a teljes felszíni mezőből.
A javítás nem teszi késznek a folyólátványt. A korábbi 80–85%-os A8
összbecslés nem tekinthető vizuális átvételnek.
[Hibafeltárási napló](history/2026-10-02-a8-failed-live-acceptance.md).

A Core oldali 4-workeres út bitazonos és offline 2,0–2,2× gyorsabb, a viewerben az előnézet is látszik, és a főszálú mesh-akadást (3,2 / 18,5 s!) az ND-147 képkockákra bontott építése megoldotta (7,8 ms a legnagyobb szelet). Ami hátra van: szünet nélküli, tiszta mérés (az eddigi menetekben Play Pause volt, így a teljes folyóidő nem összevethető), deep-time viselkedés, memóriaprofil, és a B3 látványítélet — ez utóbbi hozzád tartozik.

---

Helyességi tétel — ez a legalattomosabb

A21 ✅ — DeterministicMath.Exp/Ln/Pow tartomány-élesetek (ND-150, 2026-09-26)
A `ScaleByPowerOfTwoChecked` az alulcsordulást 0,0-ra, a túlcsordulást
pozitív végtelenre képezi. Az `Exp` NaN- és argumentumkaput kapott; az `Ln`
kezeli a pozitív végtelent és a szubnormális bemenetet; a `Pow` nulla
kitevőnél 1,0-t ad. Az ND-137 helyi `MaxDecayExponent`-védelme szándékosan
megmaradt, mert eltávolítása numerikus változás lenne. A 3000 KAT-vektor
és a három mért világ-hash változatlan maradt; a részletes bizonyíték a fenti
A21 táblasorban és a `history/2026-09-26-a21-nd150-deterministic-math-edge-cases.md`
naplóban található.

---

Kozmetika, de gyűlik a kamat

A22 ✅ — Deep-time kontextus-struct
Az `ElevationWithBoundaryFromWarpedAtTime` 16 paraméteres volt, és ugyanaz a hármas — (plateTimeMyr, erosionTimeMyr, staticSeaLevelMeters) — vonult végig öt Core-osztályon és négy viewer-segédfüggvényen. Mostantól egy `readonly struct DeepTimeContext` utazik helyettük (12 paraméter), a viewer három `_adaptive…` mezője pedig egyetlen `_adaptiveDeepTime`-ra olvadt. A refaktor bit-semleges: a `worldgen hash` t = 0 / 400 / 3000-nél bitre ugyanaz, mint a HEAD-en (külön worktree-ben mérve), tehát nincs verzióemelés. A tengerszint property, nem mező — különben a `default(DeepTimeContext)` 0,0 m-t adna, ami csendben bekapcsolná a parti abráziót. Napló: `history/2026-09-26-a22-deep-time-context.md`.

---

Infrastruktúra / döntéshez kötött

A13 ✅ — ND-20, Burst FloatMode.Strict CI-kapu
Lezárva (2026-09-27). A repoban továbbra sincs egyetlen [BurstCompile] sem — épp ezért készült el a kapu ELŐRE, az első használat előtt: `tools/ci/check_burst_strict.py` + `burst-strict` CI-job. A szabály: minden [BurstCompile] explicit FloatMode = FloatMode.Strict-et kap, és a FloatPrecision nem lehet Low vagy Medium (azok approximációt engedélyeznek, ugyanabba az I1-osztályba tartoznak). CI-szkript lett, nem Roslyn analyzer, mert az analyzert csak a `dotnet build` futtatja — a Unity-oldali `Assets/`-et a CI-gépeken nincs mivel lefordítani; a szöveges scanner egy futással látja mind a 301 `.cs`-t. Mivel egy üres halmazon mindig zöld kapu értéktelen, a `--self-test` 28 fixture-on (13 átengedendő, 14 elutasítandó, 1 sorszám) bizonyítja, hogy valóban fog — és ez a self-test fogott is egy valódi hibát a kapuban fejlesztés közben (összevont attribútum-lista, `[StructLayout(...), BurstCompile]`: a visszafelé-olvasás megállt az előző attribútum záró zárójelén, és a sértés átment). Napló: `history/2026-09-27-a13-nd20-burst-strict-gate.md`.

A12 ✅ — ND-19, floating origin (MINDKÉT kör kész, az ND-19 LEZÁRVA)
Az 1. kör (2026-09-27) mérte ki, hogy a float32 felszíni hibája LÉPTÉK-INVARIÁNS (0,566 m radius=100-nál, 0,500 m a valós léptéknél, 0,885 m egység-gömbön), tehát a kis Unity-lépték NEM védelem, és a méterszintű zoom csak origó-eltolással érhető el. A technika: kamera-illesztett, 2-hatvány rácsra illesztett origó, 1,5×cella hiszterézissel, felülről a bolygósugárhoz korlátozva.
A 2. kör (A12/2) elvégezte a tényleges eltolást — és MEGDÖNTÖTTE az 1. kör saját állítását, hogy ehhez „test-keretes renderelésre" kellene váltani. Az origó a test-keretben van (a bolygóval EGYÜTT forog), ezért `A + s·R·(p − O)` maga is egy forgatás+eltolás, azaz egy Unity-transzform: a Planet `rotation`-ja változatlanul a spin/dőlés hordozója maradt, csak a `localPosition`-ja kapott eltolást. A kameramódok (Free/AxialRotation/OrbitalFollow) egyetlen sorral sem változtak.
A konvenció KÉT réteg-gyökér, KÉT precízió. A **Planet** lokális tere szándékosan ABSZOLÚT maradt — ezért egyetlen `InverseTransformPoint` / `localToWorldMatrix` hívást sem kellett átírni a LOD-ban, az overlay-ekben, a diagnosztikában és a léptékvonalzóban (nyolc hely, mind némán rossz eredményt adott volna). A **finomított réteg** új, TESTVÉR gyökér alá került (`PlanetRefinedLayer`, `localPosition = A`), origó-relatív csúcsokkal. Testvér, mert gyerekként a `s·R·O + (A − s·R·O)` float32-kioltás ~0,57 m maradékot hagyna, ami ráadásul `R`-rel változik → forgás közben remegés. A statikus alapréteg SZÁNDÉKOSAN abszolút marad: eltolása rebase-enként ~1,3–1,6 s újraemissziót kérne (mérve), és a kamera közelében úgyis a finomított réteg takarja.
MÉRVE, élő Play-ben: 28,1 km magasságon `gainFactor=256,0` (0,566 m → 2,21 mm), 89 km-en 64,0 (8,8 mm); `planetLocalPosition = A − O` BITRE a várt érték; a kamera 0,13 egységre (9,6 km) van a render-origótól (korábban ~100 egység); a finomított chunkok mesh-bounds középpontja 0,6–2,7 egység. Kép-A/B fagyasztott idővel: 89 km-en átlag 0,0037/255 (a pixelek 0,001%-a tér el 8-nál többet), bolygó-nézetben 0,65/255.
Bit-azonossági szerződés: bolygóközepű origónál minden emittált csúcs BITRE a korábbi (két teszt köti ki); ezért maradt a statikus/másodlagos emit-út a régi float32-es alakon. A finomított réteg viszont most végig double-ban számol — itt volt a rejtett szűk keresztmetszet: a `ComputeDisplacedRadius` `float`-ot adott vissza, tehát MAGA A SUGÁR kerekedett 0,57 m-re, még a pozíció-kivonás előtt.
Az élő mérés KÉT valódi hibát fogott (egyiket sem találta volna meg teszt, mert jelenet-állapotról szólnak): (1) a domain reloadot túlélő finomított GameObject-ek a Planet alatt maradtak, és egy aktív `IndependentWater1` duplán renderelt → `MigrateRefinedChildrenFromPlanet`; (2) a független vízréteg nem épült újra rebase-nél, mert az origó nem része a víz-kiválasztásnak — mély óceán fölött ez „vízfelszín helyett tengerfenék"-ként jelentkezett, ami simán elmehetett volna helyes látványnak; javítás előtt az A/B 65,2/255, utána 0,0037/255.
Mérési csapda, rögzítve: az első A/B-k drámai fényesség-különbséget mutattak — az ok a futó idő volt (`daysPerSecond = 0,05`, egy nap 20 s alatt), nem a változtatás. Élő vizuális A/B-hez `daysPerSecond = 0` + fix `currentTimeDays` + `followLocalSurface = false` + fix `distance` kell.
Rebase-költség mérve: teljes finomított újraemisszió (`changedChunks=1138/1138`), bolygó-nézeti cut-méretnél `workerMs=2336`, de a FŐ SZÁL érintetlen (ND-147 szeletelt feltöltés, `maxSlice=3,01 ms`, `commitMs=6,9`).
Hátra (nem blokkoló): a durva rétegek 0,57 m-es kvantálása 1 km alatti zoomnál; a `nearClip` (0,3 egység ≈ 22 km) lejjebb vitele (logaritmikus depth, külön tétel); a felhasználói vizuális átvétel. Naplók: history/2026-09-27-a12-nd19-floating-origin.md (1. kör), history/2026-09-27-a12-2-nd19-floating-origin-geometry.md (2. kör).

A11 ✅ — Pálya menti (éves) kameramód
LEZÁRVA (ND-153, 2026-09-27). A mód célja az ÉVES jel, ami csak a pálya-szögtől és a dőléstől függ — ezért a világot a bolygóval együtt mozgó, nem forgó pálya-keretben rendereljük (a bolygó az origóban, a Nap-korong fix sugarú körön): kör pálya mellett ez EXAKT, valós léptékű pálya-koordináta nélkül, tehát az ND-19 nem blokkolja. A napi forgás BEFAGY a módba lépés szögén (60 s/év mellett ~36°/képkocka volna, azaz alias) — a modellidő viszont továbbfut, csak a kép spin-fázisa konstans, ezért a panel kizárólag a szub-napponti SZÉLESSÉGET írja ki (az spin-független, tehát igaz). A kameracélpontot nem kellett befagyasztani: a PlanetOrbitCamera sosem olvassa a target.rotation-t. Mérve élő Play módban: 6,09 nap/s (= 365,25/60), a szub-napponti szélesség −23,1° → +23,434° (napforduló), a Planet-rotáció két, ~60 modellnappal eltérő mintavételnél BITRE azonos, Free-re visszaváltva identitás. Új, motorfüggetlen `OrbitalFollowMath` + 24 teszt. Hátra: a felhasználói vizuális átvétel. Napló: history/2026-09-27-a11-nd153-orbital-follow-camera.md.

---

Látvány (a hátsó sor)

A15 ✅ — M13 Fázis 2–4: lezárva (ND-151, 2026-09-27). A GPU compute és a GPU-vezérelt mesh fázisát az ND-128 után elvetettük; a modellből származó, nézetfüggő per-pixel mikro-részlet elkészült. A megvalósítás és a korábbi élő Play mérés részletei a fenti A15 sorban és a `history/2026-09-27-a15-nd151-surface-micro-detail.md` naplóban vannak. A látvány felhasználói átvétele külön B16 tétel.

A16 ✅ — M13 volumetrikus felhő + AO: kész (ND-154, ND-155). Saját, gömbi raymarch a csapadék-mezőből; a felhőalap a Lawrence-féle LCL-ből származik és a talajjal emelkedik; egyetlen 104 KB-os négycsatornás atlasz viszi a lefedettséget, az aljat, a vastagságot és az égbolt-nyitottságot. Az AO helyét a mérés döntötte el: a szárazföldi nyitottság level 6-on 0,999999, level 10-en 0,999992, tehát makro-léptéken nincs okkludáló domborzat — az AO a per-pixel mikro-reliefbe került, a planetáris okkluder a felhő. A színkalibráció (ND-156) tudatosan a vizuális átvétel utánra halasztva, dokumentált eljárással. Napló: history/2026-09-27-a16-nd154-volumetric-clouds.md.

A14 ✅ — ND-21, HDRP volumetrikus felhő űrből: a prototípus 2026-09-27-én lefutott, a döntés ELUTASÍTÁS. Négy mért blokkoló: a rétegvastagság alsó korlátja (100 m, keményen vágva) a mi léptékünkben 7420 km vastag felhőhéjat ír elő; a sűrűség-normalizálás a Föld sugarát drótozza be, amitől a felhőtérkép UV-je elfajul (Advanced módban nulla felhő); a felhőtérképes út — az egyetlen, amit a csapadék-mezőnk hajthatna — a shaderben kizárja a déli féltekét; és ami renderel (Simple preset), annak a lefedettsége konstans a shaderben, tehát I3-sértés. A Planet nézet felhői a saját úton maradnak; az A16 volumetrikus tétele saját, gömbi raymarch. Napló: history/2026-09-27-a14-nd21-hdrp-volumetric-clouds.md.

A10 ✅ — ND-05 hibrid régió-szegmentálás: LEZÁRVA (ND-152, 2026-09-27). A vízgyűjtő maradt az atom (ND-127), a biome-klaszter és a domborzati törés az agglomeratív összevonás élsúlyaként jött be, ugyanazon a cella-gráfon. Mérve: a régióhatárok 21,0% → 43,2%-a ül domborzati törésen, 25,4% → 41,2%-a biome-váltáson, a kompaktság nem romlik. Nem seed-törő, de a régiónevek és a SoilFertility-küszöbök (v3) változtak. Napló: history/2026-09-27-a10-nd152-region-hybrid.md.
