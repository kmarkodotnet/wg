# A8/4 — Viewer-bekötés és élő átvétel

## Előfeltétel és cél

Az A8/3 Core-útja bitazonos és azonos konfigurációban mérhetően gyorsabb. Ebben a sessionben a viewert állítsd át rá, majd ellenőrizd a valódi elkészülési időt és a megjelenített hálózatot Unity Play módban.

## Teendők

1. A `PlanetGridMesh.StartRiverRefinement` hívási pontján válts az új Core-útra. Tartsd meg az ND-132 szerződését: indítás a teljes `Build()` után, a terep ThreadPooljának kímélése, régi munka leállítása új Buildnél, generációellenőrzés és teljes hálózat egyszeri főszálú átvétele. A `TryApplyCompletedRiverRefinement` továbbra is ugyanabból a listából számolja a vízhozam-súlyokat és építse a folyószalagokat.
2. A PerfLog külön mutassa a forrásszámot, ütemezési módot, folyómunka indítását/elkészülését/megszakítását, a teljes elapsed időt és a folyó-mesh idejét. Méréshez ne keverd össze a szinkron `Build()` és a háttérben elkészülő folyók idejét.
3. Futtasd a viewer offline fordítási kapuját: `dotnet build tests/WorldGen.Viewer.Compile`. Ha elérhető a Unity Editor/Pipeline, ellenőrizd a Unity saját fordítását, scene-bekötést és Play futást is. Offline fordítás önmagában nem bizonyítja a kész megjelenítést.
4. Élő Play-ben azonos seednél és időnél hasonlítsd össze a baseline és az új változatot: folyóágak, találkozások, szélességek, tavakhoz/óceánhoz kapcsolódás. Egy Build után várd meg a `[river ready]` és `[river mesh]` eseményt; gyors egymás utáni deep-time váltásnál az elavult eredmény ne jelenjen meg. A B3 vizuális ítéletet és a felhasználó képernyőképét elsődleges bizonyítékként kezeld.
5. Ismételt mérésben hasonlítsd az A8/1 alapvonalhoz a teljes hálózat megjelenéséig tartó időt, a főszálú `Build()`-et és a frame-csúcsokat. Ha az új ütemezés gyorsítja a folyókat, de lassítja az előteret vagy növeli a memóriát, ezt mérlegeld és szükség esetén térj vissza a referenciaútra.
6. Frissítsd a `todo2.md` A8/B3 státuszt, a releváns ND-bejegyzést és a `history/` naplót a tényleges eredménnyel. Ne jelöld vizuálisan késznek az A8-at élő Unity-bizonyíték nélkül.

## Kész feltétel

- Core differenciális kapu és viewer fordítás zöld; az új út bitre azonos a referenciahálózattal.
- Aktuális Unity Play- és PerfLog-bizonyíték van a teljes folyómegjelenési időről, a törlés/generációváltás helyességéről és a B3 látványáról.
- A gyorsulás számszerű, a mérés konfigurációja rögzített. Ha nincs valós nyereség, dokumentáltan visszaáll a korábbi út, és A8-at nem jelentjük sikeres gyorsításnak.

## Átadás

Add meg a mérési napló és a módosított fájlok helyét, a futtatott teszteket és az élő átvétel eredményét. Commit és push csak külön felhasználói kérésre.
