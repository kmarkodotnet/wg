# ND-71 regresszió — okok és javítás (ND-72, 2026-09-11)

## Következtetés

A felhasználó élő visszajelzése igazolt: az utolsó módosítás rossz
költség–haszon döntés volt. A drága, öt domborzati pontból számolt bounds
a kvadfa-kiválasztás belső ciklusába került. A javítás a bizonyított
regressziót visszavonja; **nem állít új, látványos felbontásjavulást**.

Az ND-69/70 zoom-, visszazoom-, budget-frontier-, fedéscsere-, közösél- és
pozícióérzékeny chunk-javításai megmaradnak. A km-lépték igénye a backlogban
változatlanul megmarad. A Core, seed és megjelenített modell nem változott.

## 1. Mi lassult le?

Elsődleges forrás: `unity/WorldGenViewer/Logs/PerfLog_20260911_172850.txt`.

| Kéréskori kameratávolság | Bounds darab | Cut idő | Teljes requestAge | Nadir cut LOD |
|---|---:|---:|---:|---:|
| 122,161 | 191 620 | 6820 ms | 8010 ms | 10 |
| 118,144 | 241 408 | 6712 ms | 8738 ms | 10 |
| 106,675 | 239 172 | 9584 ms | 11 767 ms | 12 |

A 106,675-ös kérésnél a cut 9,584 s, míg classification 0,499 s,
corners 0,992 s, emit 0,668 s, mesh-upload 0,025 s. A regresszió domináns
része tehát **nem GPU-feltöltés vagy több háromszög kirajzolása**, hanem
a kiválasztás CPU-oldali terepkiértékelése.

Az ND-71 minden meglátogatott csomóponthoz négy sarkot és egy középpontot
kért. A base rácsból származó pontok olcsók voltak, de a finomabb minták
cache-miss esetén teljes terepkiértékelést indítottak. Mindez egy szálon,
a rangsorolás és az utólagos óceáni szűrés előtt történt. A bounds-cache
kérésenként újra épült; a pozíció-cache részben segített, de nem vette ki
a munkát a belső ciklusból. Az alapgömb horizont-/backface-tesztjének
elhagyása növelte a vizsgált tartományt. Ez önmagában nem indokolatlan
geometriailag, de nem kapott helyette olcsó, domborzatra érvényes szűrést.

Már 160,239 távolságnál 783 ms cut-munka történt **nulla dinamikus levélért**.
Ez különösen világos bizonyíték a látható eredmény nélkül elvégzett munkára.

## 2. Miért nem lett látványosan részletesebb?

1. Az előző L14→L17 eredmény egy külön kiválasztott szárazföldi pontnál,
   0,5→0,05 render-egység felszíntávolságnál született. Nem a felhasználó
   kameráit, és nem a teljes renderelt képet mérte. A mélyzoom-képességből
   nem következett javulás a tényleges használati tartományban.
2. A friss élő log csak L10–L12 nadír-cutot mutat. Az alábbi azonos-bemenetű
   próba egyik szárazföldi pontján ugyanaz az L10 maradt, a másikon csak
   L11→L12 eltérés volt, sok másodpercnyi többletért.
3. A cut nem renderlista. A vizsgált kamerák egyikének nadírja a meglévő
   modellben víz alatti; a terrain finomítását az óceáni szűrés korlátozhatja,
   és a látható vízréteg nem azonos a terrainnel. Ez nem állítás a teljes
   felhasználói képről: a log mellett nincs screenshot/takarásmérés.
4. A geometria sűrűsödése nem új geológiai információ. A Core másodlagos
   zajrétege dokumentáltan regionális hullámzás, nem tetszőleges zoomon új
   mikrorészlet. A morph szintén késleltetheti a finom minta teljes megjelenését.
   E tényezők tényleges súlyát a renderadatból kell vizsgálni, nem LOD-számból
   következtetni kész látványra.

## 3. Azonos kamerás parancssori ellenpróba

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj -c Release --no-restore -p:_EnableDefaultWindowsPlatform=false -p:UseAppHost=false -- --replay
```

A három kamera a friss logból jön, 3 tizedesre kerekítve. Mindkét változat
ugyanazt a kamerát, seedet, 200 000-es budgetet, 0,010070 szögküszöböt és
üres előzményt kapja. A log régi változata nem tárolta a fél-FOV-t, ezért
közös **explicit 1,1 rad** próbabemenetet használunk: nem állítunk bitpontos
Unity-replayt. A kráter nélküli t=0 modell és tengerszint egyezik a loggal.
A szükséges statikus mintákat a mért futás előtt előmelegítjük; a finom
minták hidegek. Nincs mesh-emit, upload, LRU-könyvelés vagy Unity rendering.
Az időmérés nem izolált benchmark (a solution-tesztek is futhattak közben),
ezért nagyságrendi bizonyíték, nem garantált gyorsulási szorzó.

| Kameratávolság | ND-71 cut | Helyreállított ND-70 cut | ND-71 új modellpont | Nadir LOD: ND-71 / helyreállított |
|---|---:|---:|---:|---:|
| 122,160 | 6056 ms | 75,6 ms | 137 755 | 10 / 9 |
| 118,143 | 7248 ms | 97,3 ms | 201 798 | 10 / 10 |
| 106,675 | 7237 ms | 92,2 ms | 212 426 | 12 / 11 |

A helyreállított kiválasztó mindhárom futásban **0 modellpontot** értékel.
A három helyi felszíntávolság a modell alapján 20,5122 / 15,8666 / 4,5949
render-egység; az első nadír víz alatti, a másik kettő szárazföld. Ez mutatja
a korábbi 0,05 egységes próba és az aktuális használat közti lényeges eltérést.
**Ezek cut-idők, nem az alkalmazás teljes reakcióideje vagy FPS-e.**

## 4. Mi változott a javításban?

- A `PlanetGridMesh` aktív kiválasztásából és morphjából kikerült az ND-71
  callback és bounds-cache. Ismét az ND-70 gömbös metrikája/cullingja fut.
- A tiszta `SurfaceLodBounds` és az opcionális API offline regressziós
  kísérletként maradt meg; nincs automatikus vagy Inspectoros aktiválás.
- Az ND-69/70 működő javításai és a részleges szín/normál-cache helyes
  teljességvizsgálata megmaradnak.
- A kéréskori FOV/aspect/viewport bekerül a kickoff-logba, hogy újabb
  reprodukcióhoz ne kelljen feltételezni a vetítési paramétereket.
- Kérésenként egy nadír-diagnosztika készül, külön időzítve, egy új
  modellponttal. Az óceáni szűrés UTÁNI renderpartíció levelét és az emitben
  használt közösélhez igazított/morpholt quadot méri.

Új logjel: `[async apply ND-72]`, mellette `[ND-72 render]`:

| Mező | Jelentés |
|---|---|
| `surfaceMetric=False`, `surfaceBounds=0` | A drága kiválasztási út nem fut. |
| `nadirTerrainL` | Tényleges terrain-renderlevél; óceáni szűrés után akár base L8. |
| `nadirOceanBlocked` | A base-ős óceáni szabálya letiltja-e a terrain finomítását. |
| `nadirUnderWater` | A nadír modellpontja a tengerszint alatt van-e. |
| `nadirSurfaceClearanceUnits` | Radiális távolság a helyi modellfelszíntől/víztől, render-egységben, nem km. |
| `nadirMorphAlpha` | A kiválasztott terrain-levél nyers morph-alfája; a közösél-feloldás további illesztést végezhet. |
| `nadirTerrainQuadPx` | A feloldott terrain-quad vetített sarokátmérője a kéréskori kamerával. |
| `diagnostic` | A workerbeli mintavétel saját ideje ms-ban. |

A pixelérték nem a vízméret, nem viewport-hibakorlát és nem raycast/takarás-
ellenőrzés. Near-plane/kamera mögötti sarok esetén `NaN` az érvénytelen jelzés.
A vetítéshez rögzített mátrixot és viewportot használunk, nem az alkalmazás
idejére esetleg elmozdult kamerát. Unity API nem fut worker-szálon.

## 5. Lehetséges további javítási sorrend

1. **Élő regresszióellenőrzés:** új loggal igazolni, hogy a többmásodperces
   cut-többlet megszűnt és az ND-70 zoom/visszazoom látványa megmaradt.
2. **A kívánt zoomhely mérhető azonosítása:** screenshot és a fenti nadír-
   adatok alapján szétválasztani a túl nagy render-quadot, a morphot,
   víz alatti tiltást és a mező alacsony részlettartalmát.
3. **Ha a háló a korlát:** Build-kor létrehozott, világváltozáskor invalidált
   hierarchikus proxy/error-adatok, olcsó konzervatív előszűrés, és csak a
   releváns látható ágakon végzett adaptív frissítés. Kötelező azonos kamerás
   költség-, viewportfedés- és képi acceptance; budgetemelés nem javítás.
4. **Ha a kamera a korlát:** a helyi felszínhez és near-plane-hez illeszkedő
   zoomminimum és mozgatási skála, domborzat alá jutás elleni ellenőrzéssel.
5. **Ha a modellrészlet a korlát:** külön, explicit modell-/seed-kompatibilitási
   döntés kell, nem dekoratív zaj hozzáadása a viewerben.

A 3–5. pont nincs csendben implementálva e regressziójavítással együtt.
A felbontási plafon nyitott marad, amíg az élő kép és renderadat nem igazolja
a megfelelő következő lépést.

## Ellenőrzés és becslés

Solution build: 0 hiba / 0 figyelmeztetés. Core 381, CLI 7, LOD 83 teszt;
LOD Release is 83/83. Unity `Assembly-CSharp` fordítás: 0 hiba / 83
figyelmeztetés. Ez nem élő Editor-/vizuális/FPS-ellenőrzés. A javítás utáni
élő visszajelzés még hiányzik. `git diff --check` tiszta. Python nincs
telepítve, KAT/vektor-regenerálás nem futott; Core és tesztvektor nem változott.

Korrigált, durva M9-becslés **60–70%**; a mostani regresszióvizsgálat/javítás
nagyságrendje **2–4 fejlesztői óra**, hátralévő zoommunka **16–32 óra**,
km-lépték UI nélkül. Ezek nem mért munkaidők; az ND-71 elutasítását nem
számítjuk teljesült vizuális előrelépésnek.
