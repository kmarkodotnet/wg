# ND-81 élő visszamérés — Az első zoomok továbbra is akadozó finomodása

Forrás: `unity/WorldGenViewer/Logs/PerfLog_20260911_224948.txt`, 527 sor,
52 alkalmazott kérés (51 nem üres), 23 kirajzoltgeometria-mintavétel.
Az ND-81 aktív; 34 kérésben van cache-találat, 18-ban nincs. A cache
maximuma 150 751 bejegyzés, nem telített. A chunkmaximum 256, nincs naplózott
kivétel vagy érvénytelen/hibás diagnosztikai quad. A felhasználó szerint az
alaptávolság utáni első zoomok továbbra is beragadnak, az átmenet nem sima.

## 1. Mozgáskor a drága kiválasztás megmaradt

| Kérés indulása | Távolság, viewer-egység | Valódi új metrikaszámítás | Selection | Teljes kérés | Upload |
|---|---:|---:|---:|---:|---:|
| 22:50:08.291 | 263,746 | 57 901 | 93,97 ms | 129,8 ms | 3,68 ms |
| 22:50:08.602 | 173,576 | 128 567 | 374,20 ms | 459,9 ms | 4,83 ms |
| 22:50:09.224 | 173,576, elfordított kamera | 130 332 | 332,24 ms | 387,6 ms | 4,13 ms |
| 22:50:09.622 | 173,576, újabb elfordulás | 129 927 | 285,09 ms | 305,5 ms | 1,05 ms |
| 22:50:10.498 | 160,239 | 127 065 | 307,25 ms | 418,3 ms | 6,01 ms |

Mindegyik sorban `metricHits=0`. A 173,576-os első kérésben mindössze
196 új split és 777 dinamikus levél készül, mégis a selection viszi el
a teljes késés nagy részét. Ez nem splitkvóta- vagy nagy mesh-upload probléma:
ott `deferredSplits=0`, balance 3,95 ms, emit 29,65 ms.

A kód ezt megmagyarázza: a `PrepareTerrainEvaluationCache` minden eltérő
kameránál új cache-t készít. A `SelectCutPrioritized` a hat lap durva
gyökereitől újra bejár; a cache-miss út `BoundsAt`, majd szükség esetén
`QuadAt` és új vetítés. Ezek egy része kamerafüggetlen geometriai munka,
de az ND-81 a teljes, kamerafüggő eredményt tárolta együtt. A legutóbbi
gyorsítás tehát működik, de az első, mozgás közbeni zoomok fő költségét
nem szüntette meg. Tíz elavult kérés megszakadt/eldobódott a teljes próbában;
ez védelem, nem önmagában bizonyított hibás megszakítási szabály.

## 2. Álló kameránál is másodperces, adagolt felzárkózás

| Távolság | Első indulás → utolsó alkalmazás, közelítőleg | Körök |
|---|---|---:|
| 149,319 | 22:50:10.929 → 22:50:13.939, **3,01 s** | 6 |
| 105,465 | 22:50:26.282 → 22:50:30.234, **3,95 s** | 7 |
| 140,379 | 22:50:58.771 → 22:51:02.956, **4,18 s** | 9 |

Az utolsó alkalmazás ideje a kickoff + `requestAge` alapján becsült;
nem az egérgörgetés időpontjához képest mért input-latencia.

149,319-nél a második hullám 126 043 cache-találat mellett csak 4096 új
metrikát számol, selection 128,58 ms az első 273,17 ms helyett. Más bemenetű
körök, nem kontrollált A/B; a kihagyott számítás tény, az arány nem általános
gyorsulási ígéret. Még ez a kérés is 376,7 ms, a későbbiek 462–586 ms-osak.

Az 1024 új split/kérés után minden adag újra végigmegy a többi fázison.
Mélyen az utolsó kérés selectionje 94,29 ms, balance-e 201,78 ms,
sarok-előkészítése 75,55 ms, geometria-feloldás/ellenőrzése 190,16 ms,
valódi új chunk-emissziója csak 15,27 ms. Az aux-másolás itt 0,43 ms.
A kvóta egyszerű megemelése nagyobb egyszeri munkát is okozhat; kisebb
kvótánál pedig a teljes ismétlődő fázisok száma nőne.

A geomorph a kód szerint a kérés kameraállásából, mesh-előállításkor
számolódik (`ComputeGeomorphAlpha` → `GetUnstitchedAdaptiveCorners`).
Nem minden renderframe-ben történő időbeli animáció. A későn érkező
mesh-cseréket önmagában ezért nem teszi folytonossá.

## 3. A kész állapot minőségi küszöbe továbbra is túl későn enged osztást

A tényleges feltöltött geometria mintái:

- Alapnézetben a kiválasztott terepminta **2,715 px**; 173,576-os nézetben
  már **7,647 px**, még statikus L8. Utóbbinál régebbi kamera cutja van
  feltöltve: az ottani trace/proxy nem a jelenlegi képernyőre vonatkozik.
- 149,319-nél 22:50:10.755-kor a középső tereptile **11,40 px**;
  ugyanazon kameránál 22:50:12.759-kor már dinamikus **5,70 px**.
  A középen álló kép is késleltetett geometriafrissítést mutat. A kétmásodperces
  mintavétel nem mutatja az összes köztes frame-et.
- 149,319-nél befejezett állapotban is van **10,127 px** statikus terepminta,
  a proxy 9,892 px a 10 px-es határ alatt.
- 140,379-nél 22:51:04.116 és 22:51:06.120 időpontban egyaránt **9,881 px**
  a statikus terepminta, `below-threshold`, 10 px-es határ, nulla kameraeltérés,
  nincs függő kérés vagy finomítás. **Ez már nem háttérmunka-késés.**

A 12/10 px küszöb továbbra sem őrzi az alapnézet 2–3 px körüli sűrűségét.
Gyorsabb számítás önmagában nem változtat ezen. A korábbi ND-79 szerint a
globális 6/5 px-re váltás súlyos közepes-zoom költségnövekedést is okozhat;
ezért nem indokolt ellenőrizetlenül egyszerűen lefelezni a célértéket.

## 4. Külön geometriai eltérés is látszik

Visszazoomkor, 22:50:58.090-nél, már befejezett állapotban a
`6900000000038882` tile kirajzolt átmérője **34,969 px**, proxyja **6,931 px**,
morphja 0,766, vegyes L8/L9 saroktulajdonosokkal. Másik minta 27,387 vs
6,037 px. Mélyen 15,582 vs 11,877 px eltérés is van.

Ez nem magyarázható régi kamerával (`appliedCameraDeltaUnits=0`) vagy
függő munkával. A végső feloldott/morpholt geometria és a kiválasztási proxy
eltérése külön reprodukálandó. A log **nem bizonyítja**, hogy az ND-81
vezette be, sem hogy kizárólag a közösél-resolver a hibás. Nem szabad
megoldottnak tekinteni a teljes tile-méret problémát.

## Összkép és javasolt sorrend

Az 51 nem üres alkalmazás requestAge mediánja **484,7 ms**, maximuma
**990,4 ms**. Upload medián **3,71 ms**, p90 **7,43 ms**, maximum **9,04 ms**.
Selection medián 144,36 ms, balance 47,91 ms, corners 92,21 ms,
resolveCheck 74,05 ms. A mediánokat nem szabad összeadni egy fiktív kéréshez.
A korábbi ND-80 út eltérő volt, ezért a korábbi 506,5 ms-os mediánnal való
összevetés nem kontrollált gyorsulásmérés.

1. Következő teljesítménylépésként a **kameramozgás közbeni selection**
   költségét célozni: a kamerafüggetlen bounds/quad-adatok külön újrahasználata,
   majd a teljes bejárás csökkentésének mérése. A képernyővetítés mindig friss
   maradjon; memóriakorlát és azonos cut/trace regresszió szükséges.
2. A nagy geometria-ellenőrzési és balance munkát változásfüggővé tenni,
   megőrzött fedéssel/közös élekkel. Ettől lehet olcsóbb, gyakoribb kis
   frissítéseket küldeni. A fenti geometria/proxy eltérést célzottan izolálni.
3. A korábbi osztási küszöböt és a tényleges időbeli átmenetet külön kezelni:
   az olcsóbb építés után mért minőségi cél, valamint a mesh-cserék közötti
   simítás. A küszöb csökkentése részletet, az átmenet simítása folytonosságot
   ad; egyik sem helyettesíti a másikat.

Ezek javaslatok, most nincs kódváltozás. A log LOD-frissítési késést bizonyít,
nem 300–500 ms-os főszálas frame-fagyást. A diagnosztika `measureMs` ideje
háttérszálas munka (maximum 363,99 ms), nem renderframe-idő; a főszálas capture
maximuma 6,90 ms. A hosszabb naplóhézag önmagában nem bizonyít fagyást.

M9 továbbra is durván **60–70%**, nincs vizuális elfogadás. Nem mért,
durva ráfordítás-egyenérték: ez az elemzés **0,5–1,5 óra**;
további zoom-/megjelenítési munka és validáció **8–20 óra**.
