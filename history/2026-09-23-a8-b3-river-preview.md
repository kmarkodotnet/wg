# 2026-09-23 — B3: eltűnt folyók, durva modell-előnézet (ND-145)

## Élő visszajelzés és diagnózis

A felhasználó a B3 kérdésre azt jelezte, hogy Play alatt jelenleg egyáltalán
nem lát folyókat; a korábban látott vonalak is eltűntek. A friss
[PerfLog_20260923_113651.txt](../unity/WorldGenViewer/Logs/PerfLog_20260923_113651.txt)
hat egymást követő `Build()` után 96 forrásos `[ND-132 river start]` sort,
majd mind a hat generációra `river cancel completed=False` sort tartalmaz.
Egyetlen `river ready` vagy `river mesh` sincs benne. Az elsőt 32,6 s,
a többit 6,7 / 4,8 / 2,5 / 0,04 / 19,7 s után szakította meg a következő
Build vagy a Play-menet vége. A `showRivers=True`, `count=96` a logban.

A kódút alapján minden Build törli az `_adaptiveRefinedRiverPaths` mezőt,
majd az üres `BuildRiverNetwork()` kikapcsolja a `Rivers` GameObjectet.
Eddig csak a teljes szekvenciális finomítás után volt új mesh. Az A8/1
scene-pontos offline mérés 101,9–150,0 s; a meglévő egyetlen kész Unity
menet `river ready` ideje 1477,6 s. A felhasználó megfigyelése és a log
együtt igazolja, hogy a folyóhiány elsődleges oka a későn elkészülő,
ismételten megszakított finomítás. Az, hogy az egyetlen kész Unity-menet
képe végül helyes volt-e, továbbra sincs vizuálisan igazolva.

## Változás

Az [ND-145](../docs/04-decisions.md) szerint az ND-132 dedikált szál
előbb a meglévő Core `BuildRiverNetworkFromSources` durva nyomvonalait
számolja ugyanarra a világrevízióra. A tile-középpontok az eredeti
közé eredetileg legfeljebb 1 km-es gömbfelszíni renderpontok kerültek
(az ND-147 4 km-re ritkította a mért főszálú mesh-költség miatt), és az eredeti
`BuildRiverNetwork` szalaggeometriáján jelennek meg, a durva hálózatból
származó összefolyási súlyokkal. A főszál csak egyező generációnál
veszi át az előnézetet. A finomítás ugyanazon dedikált szálon, változatlan
Core-hívással és sorrendben folytatódik, majd lecseréli az előnézetet.
Megszakítás vagy új Build érvényteleníti a függő előnézetet. A Build
főszálú idejébe nem került vissza a durva hálózatépítés.

## Mérés és ellenőrzés

- Scene-pontos offline durva Core-hálózat: **0 Myr 679,7 ms**, 96 út,
  2883 alappont, 17 beolvadás; **22 Myr 401,3 ms**, 96 út, 1907 alappont,
  18 beolvadás. Az összes beolvadó durva út utolsó tile-jának korábbi
  tulajdonosa megtalálható volt.
- `dotnet build tests/WorldGen.Viewer.Compile --no-restore`: 0 hiba,
  122 meglévő nullable figyelmeztetés. A Core algoritmusa numerikusan
  változatlan; az A8/1 alatt futtatott Core Release teszt 596/596.
- Új, előnézet utáni Unity Editor-fordításról, Play-képről és friss
  `[ND-145 river preview]` PerfLog-sorról még nincs bizonyíték. A
  viewer-oldali módosítás vizuális elfogadása nyitott. A friss
  `river preview` PerfLog a köztes renderpontok számát és a mesh idejét is
  kiírja; az utolsó változtatás után az offline viewer-fordítás ismét
  sikeres volt, 0 hibával.

## Következő ellenőrzés

Play indítás után az első `Build() TELJES` → `[ND-132 river start]` után
várható `[ND-145 river preview]`, és látható durva folyóhálózat.
Deep-time váltáskor az előző világ vonalai tűnjenek el, majd az új
világ előnézete jelenjen meg. Hosszabb, megszakítás nélküli menetben a
`[ND-132 river ready]` és `[ND-132 river mesh]` után a finom vonalak
váltsák fel az előnézetet. Ha a `river preview` log megjelenik, de a
vonalszalag nem látszik, a következő diagnózis a `Rivers` GameObject,
anyag, takarás és kamera/LOD vizsgálata legyen.

Commit és push nem készült.
