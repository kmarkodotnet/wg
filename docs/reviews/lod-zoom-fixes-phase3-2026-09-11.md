# Zoomjavítás — 3. csomag: domborzati LOD-metrika (ND-71)

> **Utólagos élő eredmény: elutasítva, aktív integráció visszavonva (ND-72).**
> A felhasználó nem látott érdemi élességnövekedést, viszont súlyos lassulást
> tapasztalt. Az alábbi offline eredmények nem igazolták a saját zoomtartományát.
> [Diagnózis, visszavonás és további lépések](lod-zoom-regression-nd72-2026-09-11.md).

## Felhasználói visszajelzés és hatókör

A zoom és visszazoom az ND-70 után szépen működik. A felhasználó kb. 13
görgetés után nem lát további élesedést; a lassulás javítását most halasztja.
A precíz km-léptékcsík **bekerült a backlogba**, mérési definícióval,
perspektíva-/domborzat-/fizikai sugár követelményekkel és ellenőrzési kapukkal.
A lépték UI-ja nem készült el ebben a csomagban.

## Mit bizonyít a friss napló?

`unity/WorldGenViewer/Logs/PerfLog_20260911_144159.txt`:

- 14:42:32: kamera középponttávolság 114,855, cut 93 345 levél;
- 14:42:35–52: további közelítés 112,162 → 103,663 és ismételt cut/mesh-apply;
- az utolsó ilyen cut 98 274 levél, ebből 68 625 dinamikus renderlevél;
- a korábbi logban nincs tényleges maxLOD/nadirLOD, csak beállított maxLevel=20.

Tehát nem igazolt a kamera vagy a kiválasztás teljes leállása a 13. görgetésnél.
A 100 sugarú referencia-gömbön számolt LOD-távolság viszont bizonyíthatóan
eltér az eltolt tereptől. Ez önállóan reprodukálható finomodási plafont okoz.
**Nem állítjuk, hogy a felhasználó összes látványbeli tapasztalatát kizárólag
ez okozta.** A meglévő mező részlettartalma és a ténylegesen renderelt, nem
csupán kiválasztott LOD is számít.

## Implementáció

Az `AdaptiveQuadTree.BuildCut` prioritásos útja opcionális domborzati
bounds-callbacket fogad. Callback nélkül a régi gömbös API megmarad; a legacy
DFS-út explicit hibát ad, ha támogatás nélkül kapna domborzati callbacket.

A `PlanetGridMesh` CPU-útja a négy morph nélküli sarok és a középpont tényleges
pozícióját adja át, ugyanabból a Core-mezőből, azonos tengerszinttel és
relief-túlrajzolással. A világmodell és a seed nem változott.

A `SurfaceLodBounds` két külön szerepet kezel:

- **Láthatóság:** a minták teljes 3D befoglaló gömbje; a nézetkúpnál az érintő
  által adott szögkiterjedés számít. A kamera bounds-on belüli helyzete nem
  dobhatja el a patch-et. A sík referencia-gömb horizont-/backface-tiltása
  nem alkalmazható a valódi hegyoldalra, ezért ezen az úton kimarad.
- **Mintasűrűség és morph:** az érintősíkbeli sarokkiterjedés / az eltolt
  középponttól vett kameratávolság. A split és a morph ugyanazt a metrikát
  használja. A magasságugrás nem válik nem csökkenő tesszellációs hibává.

Az első próba a teljes 3D sugarat tesszellációs hibaként is használta: a
valós mező magasságugrásai L20-ig hajtottak ágakat és elvitték a budgetet.
Ez a változat **nem maradt meg**. A magasságugrásos regresszió őrzi a javítást.

A bounds-cache kérésenként ürül, a minták a meglévő sarok-cache-t használják.
A base-nél durvább dyadikus pontok közvetlenül a kész statikus rácsból jönnek;
az egyező koordináták bitazonosságát külön teszt ellenőrzi. A pozíció-cache
találata önmagában nem jelent kész színt/normált: a párhuzamos render-előkészítés
ezeket is ellenőrzi és kiegészíti, duplikált LRU-bejegyzés nélkül.

Az új logjelölő: `[async apply ND-71]`; új mezők: `surfaceMetric`,
`deepestCutL`, `nadirCutL`, `surfaceBounds`. A LOD-szintek a cutot jellemzik,
**óceáni szűrés előtt**, nem a renderelt felszín minden pixelét.

## Reprodukálható mérés

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj -c Release --no-restore -p:_EnableDefaultWindowsPlatform=false -p:UseAppHost=false -- --surface
```

Kráter nélküli t=0 Core-mező, seed `184482873278464`, 20 lemez,
tengerszint 1648,048745047 m. A próba determinisztikusan szárazföldi pontot
keres: irány `(0.630505141, -0.492051041, 0.600290796)`, terepsugár
102,686247878 megjelenítési egység. 688 px magas nézet, 60° FOV, 12 px cél,
25 000 beállított budget, hideg cut-előzmény mindkét távolságnál.

| Tereptől mért radiális távolság | Régi nadir LOD | Új nadir LOD | Új legmélyebb LOD | Új cut levélszám |
|---|---:|---:|---:|---:|
| 0,5 render-egység | 12 | 14 | 15 | 25 136 |
| 0,05 render-egység | 12 | 17 | 18 | 25 240 |

A 25 000 feletti kis többlet a korábban is külön engedett 2:1 balance-passz;
nem emeltük a konfigurált budgetet. Három további szint nyolcszor sűrűbb
oldalmenti mintázást jelent a nadír környezetében; **nem nyolcszor több valódi
geológiai információt**, és nem garantált képernyőélességi szorzót.

Az önálló próba új kiválasztási ideje 4,79 s, majd részben meleg cache-sel
1,19 s volt, a régi gömbösé 89,7 / 26,4 ms. Ezek nem Unity/FPS számok:
a próba saját pont-cache-t használ, nincs előre felépített statikus mesh/cache,
nincs render-emisszió és upload. A viewer újrahasználja a már kész statikus
mintákat. **A többletmunka ettől még valós kockázat; gyorsulást nem állítunk.**
A felhasználó által halasztott teljesítménymunka ettől nem tekinthető késznek.

## Ellenőrzés és nyitott kapuk

- Solution build: 0 hiba, 0 figyelmeztetés.
- Core: 381/381; CLI: 7/7; végleges linkelt LOD: 72/72 Debug és Release.
  Összesen 460 különböző .NET-teszt, ebből 15 új LOD-regresszió ebben a csomagban.
- Unity `Assembly-CSharp` parancssori fordítás: 0 hiba, 83 figyelmeztetés.
  Ez nem élő Unity Editor-ellenőrzés.
- `git diff --check`: tiszta. Core-forrás és verziózott tesztvektor nem változott.
- Python nincs telepítve: KAT/vektor-regenerálás nem futott.

Még nincs ND-71 utáni élő visszajelzés. Kért próba: szárazföldre közelítés
13 görgetésen túl, az egyes mesh-frissítések kivárásával, majd visszazoom;
hegyoldal/part/lapél ellenőrzése és új PerfLog. A kiválasztott LOD növekedése
mellett a szín/normál átmeneteket és a felület folytonosságát is látni kell.

Nyitva marad: a kameraminimum valódi felszínhez igazítása (jelenleg 100,1,
ami hegyen/tengernél a felszín alá is eshet), a minták közti rejtett csúcsok
bizonyított korlátja, meredek falak garantált vetített hibakorlátja,
terrain-occlusion, inkrementális emisszió/upload és a fizikai lépték UI-ja.
A mintaalapú bounds nem bizonyított teljes folytonos-terrain bound.

## Durva projektbecslés

M9 tartalmilag súlyozva **65–75%**. A csomag nagyságrendi ráfordítása
**4–8 fejlesztői óra**, nem mért időkimutatás; a zoomjavítások és validáció
hátralévő munkája **14–28 óra**, a külön km-lépték UI nélkül. Élő regresszió
vagy teljesítménymérés ezt érdemben módosíthatja.
