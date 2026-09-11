# Zoom-LOD — első javítási csomag

Dátum: 2026-09-11. Kiinduló commit: `195875e`, branch: `codex-handoff`.
Előzmény: [diagnózis](lod-zoom-diagnosis-2026-09-11.md). Döntés: ND-69.

Utókövetés: elkészült az [ND-70 második javítási csomag](lod-zoom-fixes-phase2-2026-09-11.md).
Az alábbi eredmények és nyitott tételek az első csomag átadáskori állapotát írják le.

## Mi változott?

1. **A budget nem dobja el a már létrehozott finomabb fedést.** A prioritásos
   kiválasztó a függő dinamikus levelek helyét is lefoglalja. Split előtt
   négy gyermeknek kell elférnie; ha nincs elég hely, a szülő marad levél.
   A sor többi része is feldolgozódik, nem vész el egy `break` miatt.
2. **A kis zoomlépés után is érkezik új kérés.** Az abszolút mozgásküszöb
   már nem végleges tiltás: kisebb mozgás legfeljebb 0,25 s indítási
   késleltetést kap. Az időkapu és a futó worker ezt tovább késleltetheti;
   ez nem 250 ms-os megjelenési garancia. Irány/FOV/aspect/pixelméret változása
   elmozdulás nélkül is frissít. A kész kérés a kéréskori nézetet igazolja,
   így a worker közben történt kameramozgás következő kérést eredményez.
3. **A CPU-geometria besorolása is teljes CPU-modellből készül.** A statikus
   és dinamikus klasszifikáció többé nem használja a secondary detail nélküli
   GPU-eredményt. A scene-ben mentett `useGpuClassification=1` ezt nem írja
   felül. Az ND-63–68 terrain-bázis/dense cache gyorsítások megmaradtak.
4. **A látható parti sarok feloldja az óceáni ős tiltását.** A középpont
   mellett mind a négy base-saroknak víz alatt kell lennie a kihagyáshoz.
   A meglévő CPU-s sarokadatokat használjuk; a base-tile döntése világváltásig
   cache-elt. Float-kerekítés közelében a szűrés inkább engedi a finomítást.
5. **A pixelküszöb perspektivikus képletet használ.**
   `f = H / (2*tan(FOV/2))`, `szögsugár = atan((pixelátmérő/2)/f)`.
   Ez képközépi kalibráció, nem teljes, eltolt terepre bizonyított hibakorlát.
   60°-nál a régi lineáris szög/pixel közelítésnél nagyobb szögküszöböt ad:
   önmagában nem korábbi osztást céloz, hanem a 12 px jelentését korrigálja.
6. **A PerfLog az async worker munkáját is bontja.** Az új
   `[async apply ND-69]` sorban cut/filter/classification/corners/emit,
   kihagyott óceáni levelek, változott/összes chunk, mesh-upload és a kérés
   kora olvasható. A requestAge a sorba állást, workert és alkalmazást is
   tartalmazza, nem pusztán a kiválasztó ideje.

Core, világverzió, shader és scene nem változott. A kísérleti
`useGpuGeometry=true` út GPU-modellhibáit ez a csomag nem javítja; az élő
ellenőrzés a scene jelenlegi `useGpuGeometry=false` beállításával történjen.

## Mérhető eredmény

Ugyanaz a linkelt kvadfa-próba, azonos bemenetek, az eredeti szögküszöbbel.
Így itt kizárólag a **budget/frontier javítás** hatását mérjük, nem a teljes
Unity-pipeline-t és nem az új pixelképletet. Sugár 100, kamera 100,6,
1080 px magasság, FOV 60°, 943 gömbfelszíni képernyőminta:

| Tile-keret | Korábban alapfelbontásra eső minták | Most | Mostani cut-méret |
|---|---:|---:|---:|
| 4 000 | 943 / 943 | 0 / 943 | 3 998 |
| 25 000 | 752 / 943 | 0 / 943 | 24 998 |
| 50 000 | 468 / 943 | 0 / 943 | 50 000 |

A 4000-es keretnél a nadir most L13, korábban L8 volt. A 2160 px magas,
200 000-es keretű próbákban kamera 103-nál a korábbi 833/943, kamera
100,1-nél a korábbi 858/943 alapfelbontású minta egyaránt **0/943** lett.

Ez nem bizonyítja, hogy minden tile eléri a 12 px célt szűk keret mellett.
Azt bizonyítja, hogy a vizsgált esetekben a budget miatti sor-eldobás nem
semmisíti meg a már felépített dinamikus fedést. A külön 2:1 balance-passz
általánosan továbbra is túllépheti az elsődleges keretet; ez nyitott feladat.

## Ellenőrzés

- Solution Debug build: 0 warning, 0 error.
- Teljes solution-teszt: **381 Core + 43 LOD + 7 CLI = 431/431 sikeres**.
- Az új 29 LOD-teszt közül 9 közvetlenül a tényleges kvadfát teszteli;
  további 12 a nézetfrissítést/pixelképletet, 8 a parti kihagyási szabályt.
- Negatív kontroll: a 9 budget-tesztet a `195875e` eredeti kvadfájával,
  külön ignorált tesztbemenetről is lefuttattuk: **8 elbukik, 1 átmegy**.
  Utána az aktuális forrásra visszatérve **43/43** ismét sikeres.
  A production forrást nem cseréltük vissza a kontrollhoz.
- Unity-generált C# projekt, tényleges Unity DLL-ekkel: **0 error, 85 warning**
  (meglévő nullable/obsolete/assembly-konfliktus figyelmeztetések). A két új
  LOD-forrást az offline ellenőrzéskor külön MSBuild-target vette fel,
  a generált projektfájlok szerkesztése nélkül.
- `git diff --check`: tiszta.
- Python KAT és vektor-újragenerálás: nem futott, a `py -0p` szerint nincs
  telepített Python. Core-/numerikus változás nincs.
- **Új élő Unity-vizuális vagy FPS-mérés nincs.** Az Editor.log vége nem
  igazolt új, ND-69-es futást; az offline C# build ezt nem helyettesíti.

## Következő élő ellenőrzés

Állítsd le és indítsd újra a Play módot, hogy az új CPU-bázisbesorolás is
felépüljön. Ugyanazzal a seeddel, változatlan budgettel és chunk-szinttel:

1. Szárazföld fölött fokozatos zoom, majd egy apró zoomlépés után megállás.
   Várd meg a folyamatban levő kérés befejezését és az esetleges utókérést.
2. Partvonal fölött ugyanez; figyeld a korábban foltosan durva területeket.
3. Mély zoom, majd visszazoom; azonos helyen ablak/Game-view méretváltás.
4. Küldj képet a megmaradó hibáról és az új PerfLogot. A logban legyen
   `[async apply ND-69]`; a statikus classification sorban `usedGpu=False`.

A több érvényesen finomodó szárazföld több CPU- és upload-munkát jelenthet.
Az első csomagtól ezért **nem állítunk bizonyított FPS-javulást**; a fenti
fázisidők mutatják meg, hol kell a következő optimalizálást elvégezni.

## Még nincs kijavítva

A durva alapmesh továbbra is eltakarhatja a finomabb völgyeket. A chunk-diff
még nem érvénytelenít pusztán geomorph-változásra. Hiányzik a teljes fedéscsere,
varratkezelés, a domborzati displacementet követő kiválasztási korlát, a
valóban inkrementális emisszió és a frame-enként korlátozott feltöltés.
Az öt mintából álló parti védelem sem bizonyítja, hogy a tile belsejében nincs
apró szárazföldi részlet. Ezek miatt a teljes zoomminőség továbbra is nyitott.

Következő implementációs csomag: **durva/fine fedéscsere és a chunk-geomorph
összhangja**, az átmeneti lyukak és a feltöltési költség együttes kezelésével.

Durva, tartalmilag súlyozott M9-becslés: **55–65%**, nem vizuális acceptance.
Az első csomag hagyományos mérnöki ráfordításának becslése 4–8 óra, nem mért
munkaidő; a zoomjavításokból és validációból hátra durván **20–40 óra**.
