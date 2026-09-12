# ND-96 — Összevont tile/zoom csomag és teljes végső próbalista

**ND-98 kiegészítés:** [stabil vízkiválasztás újrahasználata](lod-water-reuse-nd98-2026-09-12.md).
A víz/part és zoom/világváltás ellenőrzési pontjai lefedik ezt is;
a víz-apply logban `selectionReusePolicy=ND98 reusedSelection=True` jelzi
a kihagyott kiválasztási munkát. Új nézet első kérésénél `False` várható.
Nincs új köztes kézi kapu, a minőségi beállítások változatlanok.

**Későbbi kiegészítés, ND-97:** [három cache-/allokációjavítás](lod-cache-allocation-nd97-2026-09-12.md)
elkészült köztes kézi próba nélkül. A 8/7 cél és az alábbi teljes lista
változatlan; az ND-96 mérések történeti eredmények. Az új kódot a
`metricStorage=ND97` logmező jelöli.

2026-09-12. A felhasználó a meglévő módosítások commitját, majd a fennmaradó
tile/zoom munka folytatását kérte, köztes kézi kapuk nélkül. A korai
élesség és mozgókamerás kiválasztás halasztása ezzel megszűnt. Az új
Core-mikroterep, a természetes kéreg-/partátmenet és a teljes M13 nem
került ebbe a viewer-feladatba.

## Commit és megőrzött munkák

- Checkpoint: `99b3ac4` — `feat(viewer): checkpoint staged terrain uploads and navigation fixes`.
- Ebben a korábbi viewer/tile változások, tesztek, kapcsolódó dokumentáció
  és a viewer scene aktuális beállításai szerepelnek. Push nem történt.
- A külön Core/reference/CLI munkaszál módosításai megmaradtak, nem kerültek
  ebbe a commitba. Az ND-96 új módosításai a checkpoint után, munkafában vannak.
- A relief **111**, az `elevationScale` és a fizikai relief kapcsoló nem
  változott ebben a csomagban. Nem lapítottuk le a hegységeket.

## Mi változott most?

| Részfeladat | Implementáció | Korlát / bizonyíték |
|---|---|---|
| Mozgókamerás kiválasztás | A proxy bounds/sarkok megmaradnak nézetváltáskor, a vetület mindig újraszámolódik | Pontos, nem toleranciás cache; páros cut/split/halasztás-egyezés |
| Geometria/proxy eltérés | Az elkészült terepmesh sarokpozícióiból további finomítási igény; a korrigált bounds az ősökig terjed | Nincs új Core-minta vagy GPU-readback; következő kérésben hat, nem azonnali képernyőgarancia |
| Koordinátabekötés | Unity lokális csúcsok → Core tengelykonvenció a visszacsatolás előtt | Külön Editor-fixture a valódi buffer/metódus bekötésre; fordított, még nem natívan futott |
| Balance | Egy szomszédbejárás a korábbi kettő helyett | Hat páros, korábbi algoritmussal összevetett eset; rendezett split és budget változatlan |
| Emisszió | A chunk-egyezéshez már feloldott sarkok közvetlenül az emitbe kerülnek | Nincs második sarokfeloldás ugyanabban az emitben; szín/normál/modellút nem változik |
| Víz | Saját worker-cache nézetváltáskor is; forrás-/vetület-azonosság ellenőrzése | A forrás továbbra is immutábilis; 8192 levél / 256 új split változatlan |
| Hiszterézis | Az Inspector 1-es vagy nem véges értéke sem ad érvénytelen víz-LOD kérést | Hat új víz-cache/paraméter regressziós eset |
| Korai finomodás | Végleges csomagbeállítás: **8 px normál / 7 px első split**, korábban 12/10 | Kód-default és PlanetView scene együtt változott; többletmunka mérve, FPS-elfogadás nincs |
| Végső regresszió | 25 új .NET-eset, 4 új Editor-eset, közös lista az ND-82–96 teljes útjához | A natív Editor-/vizuális kapu továbbra is külön szükséges |

A geometria-cache és a visszacsatolt ősi bounds legfeljebb 262144 bejegyzést
tárol külön-külön, workerenként. A metrika-cache ugyancsak korlátos.
Ez **nem 262144 byte**, nem teljes RAM-/VRAM-korlát: Dictionary-overhead,
aktív mesh-ek és a korábbi modellminta-cache-ek külön költségek. A víz és
a terep cache-e külön van. Világváltáskor a régi forráshoz tartozó cache
nem használható tovább. Telített feedback-cache nem ír fél ősláncot;
az elutasítást külön számláljuk.

A visszacsatolás a kész emissziós quadot méri, nem a végső GPU-kép takart
pixeleit. Nem bizonyított bounds az összes még nem mintázott tereppontra.
Budget, max-level, takarás és a progresszív késés miatt nem ígérünk minden
képernyőponton szigorúan legfeljebb 8 pixeles tile-t.

## Amit a mérések miatt nem hagytunk bekapcsolva

A nyers tengerfenék-sarkokra mindenhol finomító proxy változatot
**visszavontuk**. Az offline teljes kifutás 8/6 célnál 160-as távolságon
199998, 127-nél 200000 dinamikus levelet és 13,57 / 13,95 s összes cut-időt
adott. Már távol, 300-nál 45191 levelet kért. Ez megismételte volna az
ND-78-ban feltárt túlosztást. A runtime a korábbi tengerszintre korlátozott
proxyt tartja meg; a javítás a ténylegesen elkészült geometria célzott
visszacsatolása. A nyers változat kizárólag a diagnosztikai kísérletben marad.

A 6 pixeles első célt is 7-re módosítottuk a teljes kifutás után. Ugyanazon
irányon, 160-as távolságnál 94779 → 70051 levél és 23 → 18 hullám adódott.
A normál cél mindkét esetben 8. Ez költségcsökkentő kompromisszum,
nem a teljes részletességi cél vizuális elfogadása.

## Offline mérések — mit jelentenek és mit nem?

Reprodukció:

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --moving-cache
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --closure-tuned
```

A próba a meglévő diagnosztikai Core-snapshotot használja: seed
184482873278464, 0 Myr, 1238×688, 60° FOV, eltúlzott referenciageometria.
**Nem** a 14:02-es próba 585 Myr-es világának teljes replaye. A modell-
előkészítés, emisszió, upload és FPS nincs a cut-időben. Helyi mérések,
nem stabil órajelezésű benchmark-labor; az abszolút idők tájékoztatók.

Mozgó kamera: 26 közelítő/távolító állás, váltakozó referencia/új futási
sorrend. A referencia megtartja a nézeten belüli ND-81 metrika-cache-t,
csak a nézetek közötti geometriatárolás hiányzik. Mindkét oldalon az új
balance fut, tehát a táblázat **csak a cache különbségét** méri.

| Cél | Referencia összes cut | Új összes cut | Eltérés | Cut/split/halasztás |
|---|---:|---:|---:|---|
| 12/10 px | 1782,81 ms | 1395,06 ms | −21,7% | 26/26 azonos |
| 8/7 px | 2693,25 ms | 1990,45 ms | −26,1% | 26/26 azonos |

Teljesen kifutott, korlátos 1024-split hullámok, megőrzött előzménnyel:

| Távolság | 12/10 levelek / hullám | 8/7 levelek / hullám | 12/10 összes cut | 8/7 összes cut |
|---|---:|---:|---:|---:|
| 300, indulás | 0 / 1 | 12 / 1 | 66,60 ms | 49,04 ms |
| 210 | 156 / 1 | 1060 / 1 | 178,30 ms | 90,86 ms |
| 160 | 3379 / 1 | 70051 / 18 | 191,13 ms | 1166,27 ms |
| 127 | 30474 / 8 | 91046 / 22 | 253,82 ms | 1499,12 ms |
| 105 | 50130 / 17 | 79397 / 26 | 987,35 ms | 2546,99 ms |
| 160, kifelé | 3844 / 1 | 70610 / 17 | 47,07 ms | 1372,07 ms |
| 300, kifelé | 0 / 1 | 168 / 1 | 12,17 ms | 33,03 ms |

**Fontos ellenpélda:** az olcsóbb kiválasztás ellenére a sűrűbb végállapot
teljes elérése közepes zoomnál több munkát és több hullámot igényel.
Nem állítunk teljes zoomgyorsulást. A főszálas staging/commit keretek
megmaradnak, de a felzárkózás idejét élőben ellenőrizni kell. A távoli
kifelé megmaradó kevés dinamikus levél a hiszterézis következménye,
nem önmagában leak. A fenti kifutás nem tartalmazza a mesh-feedback új
hullámait; azok tényleges költségét az új log méri.

## Tesztbizonyíték és hiányok

- Solution build: 0 hiba, 0 figyelmeztetés.
- Core: 384/384; CLI: 8/8; viewer-LOD: 340/340 Debug és Release.
  Összesen 732 .NET-eset a végső forrásállapot részenkénti futtatásaiban.
- Új 25 eset: geometria-reprojekció/feedback/korlát/előrehozott split,
  korábbi balance-egyezés, víz-cache és hiszterézis.
- A logbeli valódi quad sarokadataival külön .NET-regresszió igazolja,
  hogy a korrigált metrika nem marad a kisebb proxy alatt. A kameratengelyek
  hiányában középre néző közelítést használ; nem pixelpontos log-replay.
- Unity runtime forrásfordítás: 0 hiba, 83 meglévő warning.
  Editor-tesztprojekt: 0 hiba, 4 csomag/referencia warning. Négy új
  `GeometryFeedbackTests` eset fordított; a natív tesztfuttatás nem igazolt.
- Egy teljes Debug-futásban a diagnosztikai nullallokációs teszt 7360 byte
  eltérést jelzett, miközben az izolált eset nulla volt. A mérés külön
  szkennelő szálra került, az assert/tesztrunner szálán kívülre; a szigorú
  **0 byte / 1000 quad** feltétel megmaradt, nem emeltünk toleranciát.
  A renderer szkennelőkódja ebben a korrekcióban nem változott.
- Python KAT itt nem futott: `python` nincs a PATH-on, a `py -3` launcher
  nem talál telepítést. Referenciavektorokat nem írtunk át/generáltunk újra.
  Ebben a viewer-csomagban nincs új numerikus Core-modul vagy seed-változás.
- A nyitott Editor nem hajtotta végre az egyszeri tesztindítási kérést.
  A próbakérést és ideiglenes indítóhidat eltávolítottuk; nincs későbbre
  bent hagyott automatikus tesztindítás. Nincs új élő ND-96 PerfLog vagy
  vizuális elfogadás.

## Teljes végső ellenőrzési lista

Egyetlen alkalommal, az alábbi sorrendben végezhető. A kézi próba kb.
15–25 perc, az Editor-tesztek/Profiler ettől külön futnak. Jegyezd fel az
érintett pont számát és a hozzávetőleges időt; hibánál egy képernyőkép
és az új PerfLog elég, nem szükséges a technikai logmezőket értelmezned.

### 1. Indulás és natív regresszió

- [ ] Állítsd le a Play módot, várd meg a script-importot/fordítást. A
  Console-ban ne legyen új piros fordítási vagy futási hiba.
- [ ] A Test Runner **EditMode / WorldGen.Viewer.Lod.Tests** teljes
  csoportját futtasd. Külön legyen benne `GeometryFeedbackTests`,
  `NavigationMeasurementTests`, `ChunkUploadResourceTests` és
  `TerrainIndexMaskMeshTests`. Hibás eredményt ne tekints vizuális próbával kiváltottnak.
- [ ] A PlanetView scene-ben ellenőrizd: `Target Tile Pixel Size = 8`,
  `Initial Refinement Pixel Size = 7`; CPU-geometria, terep-proxy,
  bounded chunking és staged upload bekapcsolva. A relief a saját
  megőrzött beállításod legyen (az átadott scene-ben 111).
- [ ] Indíts Play módot. A távoli bolygón nincs új lyuk, villogás,
  eltűnt hegység vagy hibás szín-/vízréteg.

### 2. Zoom, részletesség és átmenet

- [ ] Szárazföld fölött az első 1–6 közelítő görgetést egyesével végezd,
  köztük rövid megállásokkal. Korábban kezdjen finomodni; ne csak a tile-ok nőjenek.
- [ ] Közepes zoomnál állj meg 5–10 másodpercre. A kép teljes látható
  területe zárkózzon fel, ne maradjon indokolatlan durva folt a középen vagy széleken.
- [ ] Menj tovább mély zoomig, az előző „kb. 13 görgetés” tartományán túl
  is. Nézd meg, hogy osztódik-e a rács, és hogy a modellben meglévő részlet
  láthatóbb lesz-e. A tile-finomság nem hoz létre új modellfüggetlen hegyeket.
- [ ] Gyorsan görgess befelé, majd állj meg. Ne fagyjon le a navigáció;
  a finomítás néhány további hulláma után álljon nyugalomba.
- [ ] Ugyanezt végezd el kifelé, majd közepes zoomnál azonnal fordítsd
  vissza az irányt. Ne legyen tartós régi kameraállásra beragadt kép.
- [ ] A split/merge váltáskor ne legyen feltűnő alakpattanás, sávos
  visszadurvulás vagy periodikusan pulzáló rács.
- [ ] Mély zoomnál forgasd a kamerát több szomszédos területre. A
  képszélen beérkező terep is finomodjon; visszatérve a korábbi rész ne tűnjön el.

### 3. Tereptípusok és összefüggő rétegek

- [ ] Ismételd meg a közepes → mély → távoli utat hegységen, sík vidéken
  és meredek parton. Ne maradjon nagy lap/egyszínű folt csak egy-egy területen.
- [ ] Ellenőrizd a partot mindkét zoomirányban: nincs rés, kettős villogó
  felület vagy szárazföldre kerülő víz.
- [ ] Nyílt víz és tengeri jég felett is próbáld. A víz saját finomítása
  követi a nézetet, visszazoomkor nincs beszakadás vagy eltűnő tenger.
- [ ] Bolygóforgatással érints pólust és kockalap-élt is. A szomszédos
  LOD-szintek között nincs tartós repedés, lebegő perem vagy hiányzó tile.
- [ ] Határvonalakat ki/be kapcsolva a vonalak a terepet követik;
  a víz/terep/határ ugyanahhoz a kész állapothoz tartozik.

### 4. Kamera, FlyTo, fizikai lépték

- [ ] Próbáld elérni a minimum zoomot síkságon, hegyen, parton és tavon.
  Ne menjen a kamera a felület alá. A képernyő oldalain/near-plane mentén
  látható bevágást is jelezd: a jelenlegi védelem pontmintás, nem teljes mesh-ütközés.
- [ ] FlyTo szárazföldről magasabb hegységre és vissza: az út közben is
  tartsa a helyi magasságot, ne csak a végpontban korrigáljon.
- [ ] FlyTo közben görgetéssel/kézi mozgatással szakítsd meg; az irányítás
  visszakerül hozzád, nincs utólagos váratlan ugrás.
- [ ] A km-lépték zoomra és oldalirányú mozgásra frissül. Azonos nézetben
  nem villog; érvénytelen mérésnél nem mutat korábbi, más helyhez tartozó számot.
- [ ] Változtasd meg a Game view méretét/aspektusát, majd térj vissza.
  A lépték és a LOD alkalmazkodik. FOV-változtatás után se maradjon régi érték.
- [ ] A lépték a csík két végpontjának modellfelszíni helye közötti
  **fizikai gömbi távolságot** jelöli, nem a 111-szeres domborzat hegy-völgy
  útvonalhosszát. Ne ezt a kettőt hasonlítsd össze pontossági tesztként.

### 5. Világváltás, erőforrások és hosszabb használat

- [ ] Folyamatban lévő zoom közben lépj deep-time időt előre, majd vissza.
  A régi világ tile-jai/vize/léptéke ne keveredjenek az új világgal.
- [ ] Járj be 5–10 egymástól távoli területet, majd állj meg. Ne nőjön
  korlátlanul az inaktív terepobjektumok száma; az átmeneti többlet csengjen le.
- [ ] Play Stop → Play újra. Ne maradjon sérült maszk, hiányzó víz,
  elveszett mesh vagy új Console-hiba.
- [ ] Ha használsz Profilert: rögzíts egy gyors zoomutat és egy megállást.
  CPU frame-idő, GC, mesh/objektumszám és memória érdekes. A 2 ms staging
  puha keret, egyetlen natív mesh-job túllépheti; ne csak FPS-átlagot nézz.
- [ ] Végül küldd el az új PerfLogot és azt, melyik fenti pont volt rossz.
  Teljesen jó próba esetén is hasznos a log: a vizuális élmény és a
  tényleges tile-/időmérés együtt zárhatja le a csomagot.

### Mit keresünk majd az új logban?

- `geometryCache=ND96`: világon belüli összesített `geometryEntries`,
  `geometryHits`, `geometryComputed`; ezek nem kérésenkénti cache-találatok.
- Kérésenként: `feedbackMeasured`, `feedbackAdded`, `feedbackMs`,
  `feedbackPending`, `feedbackUnidentified`. Az utolsó normálisan 0;
  azonosító nélküli mesh nem vehet részt a korrekcióban.
- Világon belül összesített: `feedbackEntries`, `feedbackBounds`,
  `feedbackRejected`. Telítettségkor elutasítás lehetséges; ezt nem
  lehet befejezett pixelminőségnek tekinteni.
- ND-75: **tényleges** mesh-quad méretek, hely és réteg; a 17×9-es
  mérőháló nem teljes képernyős maximum és nem végső GPU-readback.
- ND-95: request/queue/worker/ready-wait/staging-wall/commit idővonal;
  nem a régi frame-órás requestAge alapján mérjük a felzárkózást.
- ND-93/94: inaktív cache lecsengése, eviction, staging/commit csúcsok.
  A számlálók önmagukban nem helyettesítik a memória-/FPS-ellenőrzést.

## Lezárási állapot

A tervezett implementációs csomag összeállt; a teljes tile/zoom cél
**nem vizuálisan elfogadott**. A sűrűbb minőség felzárkózási költsége,
a natív Editor-regresszió, a közeli kamerabiztonság és a teljes
memória-/FPS-kapu a fenti egyetlen végső próbában dől el. Ha a sűrűbb
állapot késése nem elfogadható, az további mérésalapú teljesítménymunka,
nem azzal lezárt hiba, hogy a cache önmagában gyorsult.

Súlyozott M9: **61,25%, kerekítve kb. 61%**. Csak a minőségi csoport
25 → 50 pontja változik: korábbi diagnózisból célzott implementáció és
regresszió lett, de lényegi élő elfogadás hiányzik. A többi csoport nem
lépett új elfogadási szintre. [Friss részbecslés](m9-progress-audit-2026-09-12.md):
**5–11 fejlesztői óra** a végső ellenőrzésre és az ismert kockázatok
esetleges korrekciójára; durva ráfordítás-egyenérték, nem mért munkaidő
vagy garantált határidő.
