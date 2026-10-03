# A8 — korlátos folyómesh-feltöltés és B3 modellhiba

A felhasználó a kézi tesztet későbbre halasztotta és további munkát kért.
A következő összevont kézi tesztelési átadásnál emlékeztetni kell rá;
rögzítve a `docs/06-user-verification-checklist.md` elején. Nem elfogadás.

## Elkészült — ND-179

Az ND-178 teljes Unity-menetében 51,1 ms volt az egyetlen nagy
folyómesh-feltöltés. A viewer most legfeljebb 16 384 vertexes részeket
épít, majd képkockánként egy részt tölt fel inaktív gyökér alatt.
A teljes aktuális generáció atomikusan cseréli a régi látható gyökeret.
A régi gyökér átnevezése a deferred Destroy előtt megakadályozza, hogy
egy ugyanazon frame-beli `Find("Rivers")` a már leváltott réteget találja.

A részek határán megismételt vertexpár normálját/tangensét a TELJES
modellútból számoljuk: nem keletkezik rés vagy megváltozott szalagforma.
A Core-pontok és topológia változatlanok. Saját mesh-regiszter gondoskodik
a megszakított, lecserélt és megszűnő viewer GPU-erőforrásainak elengedéséről;
idegen mesh-t a gyökér törlése nem tulajdonít magának.

## Ellenőrzés és mérés

- Teljes solution és offline viewer Release build: 0 hiba / 0 warning.
- Valódi Unity-regresszió: szalag háromszögpozíciók/normálok azonossága,
  egzakt rész-határ, vertexkorlát, rejtett feltöltés, atomikus csere,
  megszakítás és leváltott mesh felszabadítása PASS.
- Tartós Unity EditMode tesztek bekerültek: egyszeri és 20 ismételt
  megszakítás, két sikeres eset. Ezek Unity-tesztek, nem CLI-xUnit tesztek.
- Viewer-LOD Release: 664/664 PASS az ismételt teljes futásban. Az első
  futás 663/664 volt: a nem módosított `WarmViewResetAndEvaluationReuseStorageWithoutAllocating`
  teszt 0 helyett 7984 byte-ot mért. Izolált futásban PASS, majd a teljes
  készletben is PASS; a kezdeti többlet okát nem bizonyítottuk. A teszt és
  a cache-kód nem változott ebben a körben, nem fedtük el küszöbemeléssel.
- Teljes HDRP Play: 96 ág, 60 Ocean / 11 Pit / 25 Merged / 0 MaxSteps;
  **47 rész**, legnagyobb rész 16 384 vertex. 766 162 vertex;
  **2 297 646 index = modellútból várt 2 297 646**. A 92 extra vertex
  a 46 rész-határ valódi pontpárjának ismétlése.
- Finom követő kész 161,908 s; mesh 22,560 s falidő / 19,636 s aktív.
  Legnagyobb előkészítési szelet **5,7 ms**. Feltöltés összesen **52,4 ms**,
  de a legnagyobb frame-szelet **1,9 ms**; atomikus publikáció **0,2 ms**.
  A nyereség a feltöltési CSÚCS bontása, nem kevesebb összes uploadmunka.
- A mesh-részek több draw callt jelentenek; a teljes steady-state GPU- és
  memóriaösszehasonlítás még hátra. A teljes követési idő nem kontrollált
  A/B benchmark, a két menetet nem állítjuk közvetlenül összevethetőnek.
- A nappali `artifacts/a8-chunk-fine.png` képet megtekintettük: a folyók
  továbbra is láthatók, a már ismert szögletes modellút megmaradt.

Bizonyíték: `artifacts/a8-chunk-play.log`,
`artifacts/a8-chunk-play-result.txt`,
`artifacts/a8-chunk-regression.txt`, `artifacts/a8-river-mesh-tests.xml`.
A teljes Play a végső tulajdonosi regiszter/átnevezési védőkorrekció előtt
futott; a végleges erőforráskezelést az új EditMode tesztek és build ellenőrizték.

## Új, mért modellhiba — ND-180 / B3

A `RiverBaseline` teljes, aktuális analitikus t=0 menetére geometriai
diagnosztika készült. Változatlan A8 teljes-hash:
`65cb11f22ff6ad204ec325bc474e30bb4429b22d5105fd6b3dde749184cd07af`.

- 415 293 pont, 48 879,793 km összhossz.
- A hossz **58,77%-a** 75 m-nél hosszabb éleken fut, a normál lépés 50 m.
- Az escape a 2 km-es nyolcszomszédos rács 2 / 2,828 km-es éleit közvetlenül
  fűzi a pontsorba. A korábbi „mindig ritka/rövid escape” állítás nem áll meg.
- A 29. ág utolsó összefolyási éle **24,113 km**; az L9 claimed cella első
  befogadó pontja valódi közelségvizsgálat nélkül kapcsolódik. Ez túl korai,
  hosszú záróéleket és rövid mellékágakat eredményezhet.
- A normál lejtési irány nyolc lehetősége további kvantálást okoz.

CSV: `artifacts/a8-geometry-current/t0-geometry.csv`. Ez modellhiba,
viewer-spline nem oldja meg. Az ND-180 rögzíti a fizikai közelségvizsgálat,
folytonos lejtésirány és medencehű követés javasolt javítási irányát.
Az új numerikus modell Python-orákulumot és aktiváláskor generátorverzió-
emelést igényel. Nincs csendes modellcsere: a futó Core még a 8-as modell.

## Állapot

A8 továbbra is nyitott, tartalmilag súlyozva durván **80–85%**. A bitazonos
Core-gyorsítás és feltöltési csúcs javítva; kontrollált memória/GPU-profil,
valódi gyors deep-time-váltás és felhasználói elfogadás hátra. B3-ban a
természetes folyóalak nem kész, az ND-180 numerikus javítási kapu nyitott.
E kör durva ráfordítása **2–4 emberóra**, A8 átvétel/profil hátralévő
**3–6 óra**; az ND-180 modelljavítás külön, durván **6–12 óra** becslés.
Commit/push nincs, az A24 másik munkáját nem vettük bele.
