# ND-87 — Terep-rendercélok előkészítése

## Kiindulás és változás

Az [ND-86 élő logban](lod-auxiliary-upload-live-2026-09-12.md) a leglassabb
commit 7,88 ms, ebből 6,30 ms tereppublikálás. A vízpublikálás már legfeljebb
0,50 ms, de a teljes kérés mediánja még 669,3 ms. A felhasználó a terep
képcseréjének részletes mérését és előkészítésének leválasztását kérte.

A staging most a mesh mellett a rendercélt is előkészíti: új GameObject,
MeshFilter/MeshRenderer, komponensreferenciák és diagnosztikai adatburkoló.
Az új cél a komponensek felvétele előtt inaktív, nincs rajzolt mesh-e.
Meglévő cél esetén csak referenciát olvasunk, a régi mesh/anyag/aktivitás
érintetlen. A diagnosztika sem látja idő előtt az új geometriát.

A commitkor mesh-/anyagcsere, diagnosztikai térképfrissítés és szükséges
aktiválás marad. A már aktív chunkot nem aktiváljuk újra. A víz, border,
maszk és terep továbbra is egy kész eredményként vált, külön commit-frame-ben.
A staged rekord a komponenst közvetlenül tárolja, így ott nem ismétlődik
meg a `GetComponent`. A legacy/szinkron út kívülről változatlanul működik.

Commit előtti eldobás csak az adott kérés által létrehozott új rendercélokat
távolítja el és szabadítja fel. A korábbi cache-objektumok és látható kép
maradnak; a leválasztott mesh-tartalékok a meglévő újrahasználati rendet
követik. Commit-hibánál a meglévő statikus fallback érvényesül. Ez nem
általános objektumpool vagy a chunk-cache teljes memóriakorlátozása.

## Napló és értelmezés

Az upload begin/slice `pipeline=ND87`, a darabkorlát neve most pontosabban
`maxJobsPerFrame`. Az async apply megtartja az ND-85/86 korábbi mezőit,
és `terrainPipeline=ND87` mellett ezeket adja:

| Mező | Mit mér? |
|---|---|
| `stageMesh` | Terepmesh létrehozás/feltöltés és anyaglista előkészítése, összes staging-frame együtt |
| `stageTarget` | Célkeresés/létrehozás, komponensreferenciák és diagnosztikai rekord előkészítése |
| `newTargets` / `reusedTargets` | A staging során újonnan létrehozott / már létező célok száma |
| `terrainSwap` | Mesh-/anyagreferenciák cseréje és a spare-cache frissítése |
| `terrainDiagnostic` | A már előkészített diagnosztikai rekord publikálása |
| `terrainActivate` | Aktivitás ellenőrzése és a szükséges bekapcsolás |
| `terrainDeactivate` | A cutból eltűnő chunkok keresése és kikapcsolása |

A staging-részidők a `stageTotal`, a commit-részidők a `terrainPublish`
részhalmazai. A külső mérés egyéb ciklus-/cache-költséget és mérési overheadet
is tartalmaz, ezért az összeg nem feltétlen egyezik pontosan. A `single` úton
a staged részidők nem értelmezhetők teljes bontásként. A mérés chunkonként
monoton tick-leolvasást használ, nem új Stopwatch-allokációt.

## Várható hatás és korlátok

A cél kisebb commit-csúcs az új területre lépéskor, mert az objektumkészítés
és a komponensek/rekordok előkészítése már nem a képcsere része. Hogy a
korábbi 6,30 ms-ból mennyit nyerünk, még nem mért tény. Ha az aktiválás vagy
natív referenciacsere dominál, a nyereség kisebb lesz; ezt választják szét
az új mezők. A staging összideje vagy frame-száma növekedhet. A közös
2 ms/64 job keret puha, egy natív művelet továbbra is túllépheti.

A háttérben végzett cut/geometria számítása, a zoomküszöb és a proxyhiba
változatlan. Ettől a lépéstől önmagában nem várható a ~669 ms-os teljes
frissítés vagy az élességi panasz megszűnése. Core/seed/scene nem változott.

## Ellenőrzés és élő próba

Solution build: 0 hiba/0 warning. Teljes .NET-teszt: **659/659 PASS**
(381 Core + 271 viewer-LOD + 7 CLI), viewer-LOD Release: **271/271 PASS**.
Unity-forrásfordítás: 0 hiba, 83 meglévő
warning; Editor LOD-tesztassembly: 0 hiba, 4 meglévő warning. A Unity
generált projektekhez az ignored validációs targetet és a korábbi
`TreatWarningsAsErrors=false` parancssori beállítást használtuk; a repo
közös szigorú beállítását nem módosítottuk.

Négy új Editor-eset a tényleges viewer privát metódusait hívja: új/meglévő
cél × commit/eldobás. Ellenőrzés: staging előtt/után nincs korai mesh- vagy
diagnosztikacsere, a commit aktivál és publikál, az eldobás csak az új célt
törli, a régi mesh vertexe megmarad. **Ezek csak fordított tesztek, Unity
Editorban még nem futottak.** A meglévő .NET-sorütemezési tesztek nem
helyettesítik ezt a Unity-integrációs ellenőrzést.

Kérjük ugyanazon világon a zoom/visszazoom próbát, oldalirányú forgatással
korábban nem látott területre, majd visszatéréssel (új és cache-elt célok).
Legyen part/víz és bekapcsolt határvonal is. Staging alatti világváltás vagy
Play leállítás után ne maradjon félkész/idegen felület. Figyelendő: lyuk,
villanás, idő előtt megjelenő vagy eltűnő chunk. A következő logból a
`terrainPublish`, részidői, `maxSlice`, `stageFrames` és `requestAge` együtt
értékelendő, lehetőleg hasonló chunk-/vertexterhelés mellett.

M9 tartalmilag durván 60–70%. E lépés ráfordítás-egyenértéke 2–4 óra,
élő validáció/korrekció 1–3 óra, fennmaradó zoom/render munka 8–20 óra:
durva becslések, nem mért munkaidők vagy vállalt határidők.
