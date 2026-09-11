# ND-75 — Zoom és tényleges tile-pixelméret napló

2026-09-11. Állapot: implementált, felhasználói próbára vár.
Ez kizárólag diagnosztika; az ND-74 utáni LOD-, kamera-, morph- és
budget-beállításokat nem módosítja.

## Miért más, mint az eddigi napló?

Az ND-72 nadírmérése egyetlen helyet nézett, a LOD-kéréskori kamerával,
és csak új mesh alkalmazásakor írt. Nem látszott benne, hogyan nő a még
régi mesh vetített mérete, amíg a kamera tovább közelít.

Az ND-75 legfeljebb másodpercenként új, **aktuális kamerás** snapshotot
vesz, a LOD-workertől függetlenül. A forrás az a végleges vertex-/indexlista,
amelyet a renderer ténylegesen megkapott a sikeres Unity-feltöltésben.
Nem kér új terrain-pontot, és nem a proxyból vagy a LOD-célból számol méretet.
A position-only frissítés is cseréli a diagnosztika pozíciólistáját.
A statikus alap kikapcsolt quadjai a tényleges indexmaszkból kimaradnak.

## Mit jelent a méret és a láthatóság?

A tile a mesh négysarkú, két indexelt háromszögből álló eleme. A végleges,
morpholt és közösélhez illesztett csúcsait vetítjük ki. 17×9 egyenletes,
viewport-helyi mintapontban CPU-s háromszög-clipping és mélységteszt választja
ki az elöl lévő terrain/water tile-t. Az anyag winding/cull módja, a renderer
aktív/engedélyezett állapota, kameramaszkja és frustumboundsa is számít.

- `clippedWidthPx`, `clippedHeightPx`: a képernyőre/near/far síkra vágott tile
  vetített befoglaló téglalapjának szélessége, magassága, pixelben.
- `clippedDiameterPx`: a vágott háromszögpoligonok pontjai közti legnagyobb
  képernyőtávolság. **Nem élhossz és nem négyzetgyökölt terület.**
- `fullQuadDiameterPx`: a négy eredeti sarok teljes vetített átmérője,
  a viewporton kívüli résszel együtt. Near-plane metszés/érvénytelen
  vetítés esetén `NaN`; a vágott méret ilyenkor még mérhető.
- Egy mintapontban a legelöl levő tile méretét látjuk. A méret a kiválasztott
  tile kiterjedése, nem a más tárgyak takarása után megmaradó pixelhalmazé.
- A p50/p90/max a **találatos képernyőminták** statisztikája. Nem minden
  látható tile felsorolása vagy a teljes képernyő garantált maximuma.
  Egy nagy tile több mintapontban is megjelenhet; ez szándékosan
  képterületi mintázás, nem az egyedi tile-ok egyenletes súlyozása.
- A mélység a terrain és water között is dönt: a víz alatti tengerfenék
  nem írja felül az előtte levő vízfelszín tile-méretét.

**Nem GPU-pixel-visszaolvasás.** UI, felhő, marker, folyóvonal, TAA,
blend/alpha-effekt, későbbi shader-displacement nincs leképezve. A jelenlegi
felszínshaderek nem mozgatják a vertexeket. A mérés a feltöltött felszíni
geometria bizonyítéka, nem a végső kompozit kép teljes szoftveres másolata.
Nem adaptív LOD / GPU-geometria / ortografikus kamera esetén explicit
`status=unsupported`; találat nélkül `no-surface-hit`, nem nulla tile-méret.

## Hol és mit keress a logban?

Fájl: `unity/WorldGenViewer/Logs/PerfLog_*.txt`, új `[ND-75 ...]` blokkok.

| Sor / mező | Jelentés |
|---|---|
| `[ND-75 drawn]` | Méréskori frame, idő, mesh-revízió, kamera és zoom |
| `cameraDistanceUnits` | Valódi kamera–bolygóközéppont távolság, testkoordinátás Unity-egység |
| `altitudeAboveBaseUnits` | Ebből levonva az alapgömb sugara; **nem valódi terep feletti magasság** |
| `zoomRatioRoverAltitude` | `R/(d−R)`; kétszeres érték az alapgömb feletti magasság felezését jelenti. Nem görgetésszám, nem tile-LOD; FOV külön logolt. |
| `viewClass` | A kamera meglévő Planet / Continent / Region nézetosztálya |
| `lodPending`, `pendingMs` | Fut-e LOD-kérés, mióta? |
| `lastLodApplyAgeMs` | Mennyi idő telt el az utolsó LOD-alkalmazás óta? A statikus kezdőállapot Buildhez kötött. |
| `appliedCutCameraDistanceUnits`, `appliedCameraDeltaUnits` | A legutóbb alkalmazott cut kamerája, illetve eltérése a mostani kamerától; kezdetben lehet NaN |
| `[ND-75 size]` | Találatok, mintázott méretek, forrásszámlálók, érvénytelen quadok és saját mérési idő |
| `[ND-75 center]` | A középső mintapontban a tile pontos vetített szélessége, magassága és átmérője |
| `[ND-75 row]` | Kilenc sor, soronként 17 méret: felülről lefelé, balról jobbra |

A térkép jelölései: `S` statikus terep, `D` dinamikus terep, `W` statikus víz,
`w` dinamikus víz, `.` nincs felszíni találat. Minden találat után a mért
vágott tile-átmérő áll pixelben. A sorok a közvetlenül előző `drawn` blokk
frame-jéhez tartoznak; a worker befejezése után kerülnek fájlba.

## Próbamenet

1. Play előtt ellenőrizd a `Planet Grid Mesh` Inspectorában:
   **Log Drawn Tile Sizes** bekapcsolva. Kód- és scene-alapértéke is bekapcsolt.
   A többi zoombeállítást most ne változtasd, hogy az eddigi jelenséget mérjük.
2. Ugyanazon irányban távoli → közepes → mély zoom, majd vissza.
   Mindhárom tartományban, különösen a hibás közepesnél, állj meg 3–5 másodpercre.
   Így a frissítés előtti és utáni tényleges méret egyaránt bekerülhet.
3. Írd le, hol láttad a megtorpanást. Képernyőkép/időpont segít a térképet
   a képpel összerendelni, de nem kötelező. Utána a friss PerfLoggal együtt
   elemezzük; addig nincs következő finomítási változtatás.

## Költség és ellenőrzés

Egyszerre legfeljebb egy diagnosztikai worker, maximum 1 Hz indítás.
Nem várja meg/blokkolja a LOD-workert, nem használ collidert vagy extra draw-t.
A főszál a kész listákra és a mátrixokra mutató snapshotot adja át;
`captureMs` ennek előkészítése, `measureMs` a háttérbeli geometriai mérés.
A szövegformázás/fájlírás nem része a `measureMs` mezőnek. A kész listák
megőrzése plusz memóriát köt le (különösen a statikus mesh pozíciói/indexei);
a logger kikapcsolása a mintavételt állítja le, az upload-követés megmarad.

Önálló próba: a `ViewerLodDiagnostic.csproj` `--drawn` módja 409 600 quados
síkot mér 153 pontban. Három futás: 64,79 / 56,40 / 57,16 ms; a mérőciklus
40 byte-ot allokált, az a Stopwatch. Nincs tile-onkénti foglalás. Ez a próba
nem tartalmaz Unity snapshot-előkészítést, objektummátrix-vetítést és logírást,
nem bizonyít élő FPS-költséget. A napló saját idői alapján ez is vizsgálható.

15 új teszteset: tényleges pixelméret, változatlan mesh zoomolása, viewport,
mélységi sorrend, képszél/near/far/behind-camera, winding, perspektivikus
osztás, indexmaszk-snapshot, közép/periféria és allokáció. A végleges
build/teszteredmények a history-bejegyzésben vannak. Élő Unity-próba nincs.

M9 durva tartalmi készültsége változatlanul 60–70%; a zoomhiba nincs lezárva.
A diagnosztika fejlesztői ráfordítás-egyenértéke durván 3–5 óra, a korábbi
12–28 órás zoommunka-becslés az új mérésig bizonytalan. Ezek nem mért idők.
