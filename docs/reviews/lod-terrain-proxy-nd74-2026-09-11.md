# Tereptávolság-proxy — ND-74, első ellenőrzési lépés

2026-09-11. Állapot: implementált, felhasználói Unity-próbára vár.
A következő backlog-tétel nem kezdődött el.

**Utólagos visszajelzés:** távol megfelelő kép, közepes zoomnál elmaradó
finomodás, mélyen ismét frissül, de a részletesség elégtelen. Az ND-74 nem
elfogadott lezárás. A felhasználó új javítás helyett tényleges tile-méretlogot
kért; ez az ND-75, utána közös log-/látványértékelés következik.

## Mi változott?

A LOD eddig a 100 sugarú alapgömbhöz mérte a közelséget, miközben a
megjelenített szárazföld jellemzően ezen kívül van. A közeli terep így a
kiválasztó számára távolibbnak látszott. A sikertelen ND-71 minden meglátogatott
tile-nál új modellmintákkal korrigált; ezt nem kapcsoltuk vissza.

Az új `TerrainLodProxy` a Buildben már elkészült statikus sarokpozíciók
sugarából készül. A base alatti keresés mintamaximum-piramist, base-től
helyi bilineáris sugárinterpolációt használ. A tengernél mélyebb sugarak
a vízfelszín sugarára korlátozottak. A cut és a morph azonos snapshotból
számol. A renderelt mesh továbbra is a valódi világmezőből készül, nem
ebből a közelítésből. Core, seed, zaj, pixelcél és 200 000-es budget nem változott.

Az új `Use Terrain Lod Proxy` kapcsoló alapból bekapcsolt a kódban és
a scene fájlban. Kikapcsolása a korábbi gömbös metrikát adja; álló kameránál
is új cutot kér. Futó worker az indításkor rögzített példányt használja,
a kapcsolóváltást a következő kérés követi. Build invalidálja és újraalkotja
a proxyt a friss magasságokból (seed/idő/tengerszint/relief/skála/sugár/base).
Base > 8, base > maxLevel vagy GPU-geometria mellett gömbös fallback van.

## Azonos kamerás offline próba

Reprodukció:

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj -c Release --no-restore -p:_EnableDefaultWindowsPlatform=false -p:UseAppHost=false -- --proxy
```

Scene-seed 184482873278464, 20 lemez, t=0, kráter nélkül; 688 px magas nézet,
FOV 1,047197543 rad, a naplózott nézetkúp. Két irány normalizálva:
`(31.581,-127.684,49.049)` és `(-81.192,-47.702,50.118)`.
Mindkét profil 10 px első / 12 px további split és 0,35 morph-range,
azonos távolságsorral és külön explicit cut-előzménnyel.

| Irány / középponttávolság | Régi → proxy nadír-LOD | Régi → proxy cutméret | Régi → proxy morph-alfa |
|---|---|---|---|
| 1 / 122,161 | L9 → L10 | 66 812 → 78 193 | 1,000 → 0,214 |
| 1 / 103,663 | L12 → L13 | 74 184 → 125 829 | 0,914 → 0,983 |
| 2 / 106,675 | L11 → L12 | 94 538 → 126 047 | 1,000 → 0,518 |
| 2 / 103,663 | L12 → L13 | 87 424 → 150 766 | 0,984 → 1,000 |

Eltérő szint alfái nem közvetlen minőségi összehasonlítások: az új gyermek
a már finomabb szülőről indul. Ezek kiválasztott levelek, nem képi
élességmérések. Az óceánszűrés, közösél-feloldás és Unity-upload nincs a próbában.

A valódi felszín felett 0,1 egységnél mindkét irány L13→L16, de a proxy
itt már kitölti a 200 000-es budgetet. A teljes be-/visszazoom csúcsai:
135 463→199 999, illetve 159 232→200 000. Mindkettő üres dinamikus cutra
tér vissza 173,576 távolságnál. Ez nem ingyenes részletnövelés: pl.
103,663-nál kb. 70%-kal több kiválasztott levél készülhet; az emit-költség
és a késés növekedhet. A részleges mesh-előállítás a következő feladatok egyike.

Az első mérésben a proxy építése 9,0 ms, saját tömbadata 7 364 640 byte
(kb. 7,02 MiB). A külön offline Core-minta-előkészítés 1665 ms volt;
ez a viewerben már elkészült statikus adat, nem új modellpassz.
Az új cutok kb. 15–277 ms-osak voltak a parancssori próbában, a régi
profilé kb. 15–233 ms. Egyetlen futás, nem izolált benchmark és nem FPS.
A proxyhoz kapcsolódó zoomkori új Core-minták száma a kódútból következően nulla.

## Fontos korlátok

- Nem szigorú terrain-error bound vagy teljes viewport-pixelgarancia.
  A két nadír becsült sugara a valódi modellhez képest −0,0550 és −0,0452
  Unity-egységgel tért el. Nagyon közel ez is számottevő; a nem mintázott
  base-en belüli csúcsok/határok továbbra is eltérhetnek.
- A gömbös horizont-/backface-/nézetkúpszűrés most változatlan. A képszéli,
  horizontközeli és parti bizonytalanságok a következő külön feladatban maradnak.
- A kamera felszín alá kerülését ez nem javítja, új modellrészletet nem ad.
- A folytonos mozgás késése és a feltöltési akadás nem oldódott meg.
- Az ND-73 élő eredménye elutasított. A legfrissebb régi logban a morph-range
  még 0,600, a fájlban 0,35 volt; az új próba aktív értékeit ellenőrizni kell.

## Unity-ellenőrzés, itt megállunk

1. Play leállítva, a `Planet Grid Mesh` Inspectorában ellenőrizd:
   `Use Terrain Lod Proxy` be; `Target Tile Pixel Size` 12;
   `Initial Refinement Pixel Size` 10; `Geomorph Range Fraction` 0,35.
   A memóriában nyitott scene eltérhet a fájltól; a saját, nem mentett
   scene-módosításaidat ne dobd el egy újratöltéssel.
2. Ugyanazon szárazföldi nézeten közelíts, időnként várd ki a mesh-frissítést,
   majd zoomolj vissza. Korábban és közelről részletesebb-e, van-e pattogás,
   lyuk vagy számottevő többletkésés?
3. Álló kameránál a proxy ki-/bekapcsolásával is összehasonlítható a két út.
   A hiszterézis és meleg cache miatt ez nem hideg teljesítménybenchmark;
   pontos összehasonlításhoz azonos indulásból kell be-/visszazoomolni.
4. Kérjük a látvány-visszajelzést és a friss PerfLogot. Az új marker:
   `[async apply ND-74]`; kickoff: `terrainProxy=True`, `morphRange=0.350`.
   Build: `terrainProxy=...ms`, `proxyBytes=7364640` base 8-on.
   A `[ND-72 render]` sor `proxyRadiusErrorUnits` mezője a helyi közelítési
   hibát is mutatja; nem km és nem a látható víz-quad hibája.

## Automatizált ellenőrzés

A végleges eredmények a kísérő history-bejegyzésben szerepelnek. A tesztek
ellenőrzik a bilineáris mezőt, a mintamaximum-hierarchiát, a tengeri alsó
korlátot, a snapshot-izolációt/párhuzamos olvasást, L0/L28 széleket,
budgetet, visszazoomot, régi gömb ekvivalenciáját és a hibás API-bemeneteket.
Unity C#-fordítás szükséges, de nem helyettesíti a fenti élő próbát.

Durva, tartalmilag súlyozott M9: 60–70% (élő elfogadásig nem emelve).
E lépés fejlesztői ráfordítás-egyenértéke durván 3–6 óra; a még nyitott
zoommunka 12–28 óra, km-UI és új Core-részletmodell nélkül. Nem mért idők.
