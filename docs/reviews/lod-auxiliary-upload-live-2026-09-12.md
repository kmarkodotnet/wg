# ND-86 — Élő feltöltési mérés

## Forrás és módszer

Új log: `unity/WorldGenViewer/Logs/PerfLog_20260912_015131.txt`.
Összehasonlítás: `PerfLog_20260912_013133.txt` (ND-85).
Aktuális branch `codex-handoff`, HEAD `0f9cd4b`, a korábbi nem commitolt
ND-82–86 és egyéb módosításokkal. Ebben a körben nincs kódváltozás.

71 sikeres új commit mind `auxPipeline=ND86`, nincs `single` alkalmazás.
43 önálló vízpublikáció staged, 28 változatlan vízmesh újrahasznált.
206 staging-szelet, 32 rajzdiagnosztikai pillanatkép. Az előző mintában
86 commit és 284 szelet volt. A kvantilis mindenütt rendezett tömbből,
`floor((n-1)*p)` indexszel számolt. Emiatt az előző átadás 1,29 ms-os
szelet-p90 értéke itt egységes újraszámítással 1,28 ms; nem új mérési adat.

Azonos fő konfiguráció: base 8, max 20, pixelcél 12, CPU-geometria,
1238×689 viewport. A kameraút és a terhelés eltér: a dinamikus tereplevelek
mediánja 16477→10703, maximuma 36025→32466. **Nem kontrollált A/B vagy
FPS-benchmark; a különbségek nem mind tulajdoníthatók az ND-86-nak.**

## Eredmény

| Szakasz | ND-85 medián / p90 / max | ND-86 medián / p90 / max |
|---|---:|---:|
| Végső commit | 2,65 / 5,62 / 15,83 ms | 2,01 / 4,97 / 7,88 ms |
| Víz publikálása, maszkkal | 0,42 / 0,95 / 10,71 ms | 0,10 / 0,34 / 0,50 ms |
| Egy staging-szelet | 0,76 / 1,28 / 3,65 ms | 0,85 / 1,19 / 4,02 ms |
| Staging összmunka kérésenként | 2,59 / 4,85 / 10,25 ms | 2,10 / 4,93 / 8,20 ms |
| Staging-frame-ek | 3 / 6 / 14 | 2 / 6 / 10 |
| Teljes kérés kora alkalmazáskor | 659,2 / 851,1 / 1354,3 ms | 669,3 / 823,2 / 1404,7 ms |

A víz publikálása ebben a futásban már olcsó, és az összesített aux-staging
mediánja 0,19 ms, p90-e 0,35 ms, maximuma 1,49 ms. A vízmaszk maximuma
0,43 ms; a parti víz/border publikáció maximuma 0,27 ms. Az ND-85-beli
10,71 ms eredeti belső összetételét utólag ez sem bizonyítja.

A 206 szeletből kettő lépi túl a 2 ms-ot. A legnagyobb, 4,02 ms-os szelet
`frame=731 jobs=1 done=1/108`: az első terepmunka, **nem víz/border**.
A másik a legelső, 2,44 ms-os indulási szelet. A puha keret továbbra sem
teljes frame-időgarancia; a log nem méri a teljes render/GPU frame-et.

## Mi maradt költséges?

A leglassabb, 7,88 ms-os commitból **6,30 ms a terep publikálása**;
106 változott chunk, 0,79 ms terepmaszk, 0,34 ms külső vízszakasz.
A második, 6,79 ms-os commitban 5,42 ms a tereppublikálás. A forrás szerint
ebben objektumkeresés/létrehozás, aktiválás, mesh-/anyagreferenciacsere,
diagnosztikai regisztráció és a régi chunkok kikapcsolása van. Ezen belül
az objektum-létrehozás dominanciája még **hipotézis**, nem külön mért tény.

A teljes zoomreakció más nagyságrend: a leglassabb kérés 1404,7 ms, miközben
a staging összesen 3,25 ms, a commit 2,14 ms. Ugyanebben a kérésben a cut
250,37 ms, a sarok-előkészítés 178,33 ms, az emit 658,12 ms; az utóbbin
belül a közösél-/pozícióellenőrzés (`resolveCheck`) 416,36 ms. Ezekből nem
szabad az emit és a resolveCheck idejét kétszer összeadni. A fennmaradó
időben további worker-szakaszok és ütemezési várakozás is szerepel.
Az új workeroldali vízcsomagolás p90-e 2,91 ms, maximuma 24,09 ms.

Következtetés: az aux-upload kivétele a commitból működik, de a teljes
frissítési késést nem oldotta meg. Annak meghatározó költsége továbbra is
a háttérmunka és az ismételt finomítás, nem a néhány ms-os vízfeltöltés.

## Tényleges tile-méret és élesség

A rajzdiagnosztika a publikált mesh-ekből mintázott átmérőket méri, nem
a végső GPU-képet. A 32 mintában invalid/malformed quad száma nulla;
ez önmagában nem bizonyítja a lyuk- és villanásmentes vizuális megjelenést.

- Távol, 300 egységnél: medián 2,29 px, p90 2,58 px.
- Közepes, 173,576 egységnél, három egymást követő kész pillanatképben:
  medián 6,44 px, p90 7,73 px, maximum 8,63 px. Nincs további finomítás.
- 122,161 egységnél, álló kamerával 01:52:36→01:52:44 között a medián
  15,61→9,38 px-re csökken. Ez nyolc másodperces mintavételi ablak,
  nem pontos bemenettől-készállapotig mért konvergenciaidő.
- Ugyanott a kész állapotban is marad 20,804 px-es terepquad:
  `tile=8900000000002FF2`, `stop=below-threshold`, a proxy 11,804 px a
  12 px cél alatt, `appliedCameraDeltaUnits=0`, `refinementPending=False`.
  Ez nem pusztán még folyamatban lévő upload: a kiválasztási proxy és a
  végül kirajzolt geometria méreteltérése továbbra is fennáll.
- Mozgás közben 75,758 px-es statikus terepquad is mintázódik:
  `tile=0800000000000471`, `outside-view` az alkalmazott kérés nézetében;
  a kamera már 1,548 egységgel eltér. Ez az eset nem állókamerás bizonyíték.

Az ND-86 nem módosította a kiválasztási küszöböt vagy a proxygeometriát.
A korai élesedés és a geometria/proxy eltérés a korábbi backlog része,
nem ettől a feltöltési lépéstől várt és most elmaradt felbontásjavítás.

## Hibák, következő lépés

A PerfLogban nincs exception/error vagy egylépéses uploadra visszaesés.
21 workereredmény eldobása kameraváltás miatti superseded/discarded pár.
A log végén egy félkész staging-sor `published=False` mellett eldobódik;
az ok nincs naplózva, ezért nem állítjuk biztosan, hogy Play leállítás volt.
Csak egy világkonfiguráció látszik, világváltás-validáció nem igazolt.
A statikus build 13,183 s (előző 13,143 s), ez nem javult ettől a lépéstől.

Az upload-feladat következő indokolt kapuja a **terep publikálási szakasz
felbontott mérése, majd a drága előkészítés áthelyezése a közös váltás elé**
(például inaktív renderobjektumok előkészítése, ha a mérés ezt igazolja).
A víz további bontása most nem elsődleges. A teljes késés javítása külön
worker-/selection-/geometriafeladat; a felhasználó által halasztott zoom-
küszöböket ebben a körben nem változtattuk meg.

Ez logelemzés, nem új implementáció vagy teljes vizuális elfogadás. Build/
teszt újrafuttatás nem történt, mert csak dokumentáció változott. M9
tartalmilag továbbra is durván 60–70%; e vizsgálat ráfordítás-egyenértéke
0,5–1,5 óra, a hátralévő zoom/render munka 8–20 óra, nem mért becslések.
