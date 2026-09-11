# ND-80 élő próba — feltöltési nyereség, megmaradó háttérköltség

Forrás: `unity/WorldGenViewer/Logs/PerfLog_20260911_220452.txt`.
Összehasonlítás: `PerfLog_20260911_214108.txt` (ND-78, régi chunkolás).
A felhasználó elvégezte a próbát; kifejezett vizuális elfogadást nem adott.
Ebben a körben csak elemzés történt, runtime-módosítás nincs.

## A bekötés és a korlát működik

48 sikeres alkalmazásból mind a 48 `chunkPacking=ND80`, `chunkMinLevel=6`,
`chunkLeafLimit=256`. A tényleges `maxChunkLeaves` egyszer sem nagyobb
256-nál. A teljes aktív chunkcsoportok csúcsa 656, a régi próbában 6094.
A két kameraút eltér, ezért ez nem azonos területű A/B objektumszám-mérés.

25 rajzoltmesh-mintában `invalidQuads=0`, `malformedQuads=0`.
A PerfLogban nincs Exception, capture-error vagy diagnostic-error.
Ez nem bizonyítja, hogy a GPU-kép mindenhol résmentes vagy vizuálisan jó.

## Feltöltés és teljes kérésidő

Csak a nem üres dinamikus terepet tartalmazó sikeres alkalmazások:
régi 42, új 47 minta. A p90 a rendezett minták felső, 90%-os rangja.

| Mérőszám | Régi próba | ND-80 próba |
|---|---:|---:|
| Főszálas mesh-alkalmazás mediánja | 3,41 ms | 2,99 ms |
| Főszálas mesh-alkalmazás p90 | 25,44 ms | 5,29 ms |
| Főszálas mesh-alkalmazás maximuma | 27,77 ms | 6,63 ms |
| Teljes kéréskor mediánja | 545,55 ms | 534,60 ms |
| Teljes kéréskor maximuma | 992,50 ms | 982,60 ms |

**Kedvező élő bizonyíték a feltöltési csúcsok mérséklődésére, nem
általános FPS-gyorsulás vagy kontrollált százalékos benchmark.** A teljes
kérés késése lényegében megmaradt. A jelenlegi szűk keresztmetszet alapján
a több-frame-es upload már nem az első optimalizálási cél ebben a próbában.

Új próba háttérfázisai: cut medián 261,47 ms, maximum 530,33 ms;
emit medián 144,92 ms, maximum 358,68 ms; sarok-előkészítés medián
87,58 ms, maximum 144,15 ms. A csoportosítás medián 2,53 ms,
maximum 51,61 ms; ez az emit részhalmaza, nem külön hozzáadandó idő.
A kiugrás okát (GC, ütemezés vagy algoritmus) ebből a logból nem bizonyítjuk.

## Több másodperces finomítás álló kameránál

| Középponttávolság | Változatlan kamerához tartozó hullámok | Kéréslánc kb. ideje |
|---|---:|---:|
| 122,161 | 8 | 4,24 s |
| 108,152 | 8 | 5,10 s |
| 105,465 | 7 | 4,43 s |

Az idő a legelső kickoff és az utolsó kickoff + annak requestAge mezője
közötti becslés. Nem egyetlen főszálas megakadás; a köztes mesh-ek láthatók.
A 104,474-es mély állásban visszazoom előtt még 1973 osztás várakozott,
azt nem tekintjük befejezett mérésnek.

Konkrét példa: a 22:05:31.773-as kérés 27 700 tereplevélből 21 210-et
újrahasznosít, és csak 6490-et emittál; a főszálas alkalmazás 1,68 ms.
Mégis 711,5 ms a teljes kérés: cut 420,94 ms, emit 207,84 ms,
sarok-előkészítés 58,30 ms. Az új chunkolás tehát nem oldja meg a teljes
kiválasztás és geometria-előkészítés ismétlését.

Kódellenőrzés: minden folytató kérés ismét `BuildCut`-ot futtat;
az emit-cache egyezése előtt a `CaptureResolvedPositions` minden csoport
leveleit ismét bejárja. A reuse számláló csak az emisszió kihagyását jelzi,
nem azt, hogy a kiválasztás/feloldás is ingyen volt. Az emit alatti pontos
költségmegoszlás még külön mérés kell, nem tulajdonítjuk mindet a feloldásnak.

## Az élességi panasz továbbra is nyitott

- 22:05:14.314, közepes zoom, nulla kameraeltérés, nincs függő munka:
  képközépi statikus terep 9,13 px, a legnagyobb mintázott terep 9,407 px.
  Megállása `below-threshold`, proxyja is 9,407 px. A 10 px-es kapu maradt.
- 22:05:12.314: a korábban azonosított `6800000000006270` statikus
  parti quad 28,816 px, vízszintre emelt proxyja 9,194 px. A teljes quad
  víz alatti részét is beleszámító mérés ismert korlátja változatlan.
- 22:05:20.326, befejezett állás: dinamikus L9 tile
  `690000000001A135` 17,335 px, proxy 11,575 px. A proxy/fine geometria
  eltérése is megmaradt; a méretkülönbség okát ez a próba nem oldja fel.

Visszazoom után a kisebb tile-ok részben a változatlan LOD-hiszterézisből
erednek; azonos távolság nem garantál azonos előzményű cutot.

A rajzoltmesh-diagnosztika 2 másodpercenként 144–334 ms **háttérmunkát**
végez; főszálas snapshotja 0,03–2,22 ms. A measureMs-t nem szabad
144–334 ms-os render-frame megakadásként értelmezni. A méréseknek saját
terhelésük van, későbbi teljesítmény-acceptance-ben ezt is kontrollálni kell.

## Második élő próba: 22:31:19

Új forrás: `PerfLog_20260911_223119.txt`, 57 sikeres alkalmazás,
ebből 56 nem üres dinamikus terep; 21 rajzoltmesh-minta. Továbbra is
ND80/L6/256, változatlan 12/10 pixeles cél. Ez nem egy újabb implementáció
tesztje: az előző elemzés óta itt nem módosítottuk a runtime-ot.

- Minden chunk maximum 256 leveles; nincs naplózott Exception vagy
  diagnosztikai hiba, invalid/malformed quad egyik mintában sincs.
- Feltöltés: medián 3,41 ms, p90 5,68 ms, maximum 8,60 ms.
- Teljes kérés: medián 506,50 ms, maximum 933,40 ms.
- Cut: medián 245,515 ms, maximum 425,38 ms. Emit: medián 135,50 ms,
  maximum 417,75 ms. Csoportosítás: medián 1,99 ms, maximum 36,64 ms.
- A 130-as távolságú első állókamerás lánc 22:31:38.678-tól nyolc
  hullámban kb. 4,32 s alatt fejeződik be. A második, szintén 130-as
  sorozat 22:32:02.730-tól hét hullámban kb. 4,00 s-os. A megelőző
  mozgó kamerás kérések ideje nincs ezekbe beleszámítva.

**Új, különösen erős bizonyíték:** a 22:32:06.115-ös kérésben már csak
6 új osztás marad; a 31 946 tereplevélből 31 882 újrahasznosul és csak
64 emittálódik. 624 chunkból 5 változott topológiájú és 1 position-only.
Mégis **616,9 ms** a teljes kérés: **323,68 ms cut**, 69,12 ms sarok-
előkészítés, **188,82 ms emit**, mindössze **2,48 ms feltöltés**.
Ez megerősíti, hogy a ritka valódi változás ellenére megmaradó teljes
kiválasztási és cache-ellenőrzési/előkészítési munka a következő célpont.
A 188,82 ms nem pusztán 64 tile kiírása: a mért emit fázisban van a
csoportosítás, az összes feloldott pozíció ellenőrzése és a segédrétegek
összeállítása is; ezek egymás közti költségét még nem különíti el a log.

A 22:31:44.549-es és 22:32:08.635-ös kész, 130-as nézet közepén a
terep 10,18 px marad, a legnagyobb mintázott tereptile 12,868 px
(`below-threshold`, proxy 11,143 px). A mélyebb sorozatok közül több a
következő kameramozgásig nem készül el; a log végén is van függő munka.
Ez továbbra sem élességi lezárás vagy felhasználói vizuális acceptance.

## Javasolt következő lépés

Először az állókamerás finomítás ismételt teljes kiválasztási munkájának
csökkentése: folytatható kiválasztási állapot, nézet-/modellváltáskor
egyértelmű invalidálással és megőrzött teljes fedéssel. Előtte a cut belső
fázisait (kiválasztás/balance) érdemes külön mérni. Utána az emit/reuse
előtti teljes pozícióbejárás célzott csökkentése, pontos geometriai
azonosságot megtartva. Új algoritmus implementáció előtt új ND szükséges.

A pixelcél további csökkentését és a vízfinomítást még nem indítjuk.
M9 tartalmi becslés 60–70%; durva, nem mért hátralévő munka 8–20 óra.
