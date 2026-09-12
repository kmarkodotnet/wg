# ND-86 — Víz és határvonal az előkészített feltöltési sorban

## Kiinduló élő mérés

Forrás: `unity/WorldGenViewer/Logs/PerfLog_20260912_013133.txt`, ND-85 út.
86 sikeres async alkalmazás és 284 staging-szelet; a kvantilisek a rendezett
minták lefelé kerekített indexéből származnak. Nem kontrollált FPS-benchmark.

| Mért szakasz | Medián | p90 | Maximum |
|---|---:|---:|---:|
| Staging-szelet | 0,76 ms | 1,29 ms | 3,65 ms |
| Végső commit (`mesh-feltoltes`) | 2,65 ms | 5,62 ms | 15,83 ms |
| Víz publikálása (`upload`) | 0,42 ms | 0,95 ms | 10,71 ms |
| Staging frame-ek száma | 3 | 6 | 14 |

A leglassabb commitban a víz szakasza 10,71 ms volt: 6892 vízlevél,
1723 lecserélt base-tile, 2202 módosított maszkindex. Ugyanebben a kérésben
a terep stagingje összesen 2,80 ms, legnagyobb szelete 1,66 ms volt.
A második leglassabb, 8,44 ms-os commitot 14 staging-frame előzte meg;
ott 16752 terepmaszk-index változott. A késleltetés tehát nem szűnt meg.

**A régi víz-`upload` maszkfrissítést is tartalmazott.** A teljes 10,71 ms
nem tulajdonítható bizonyítottan mesh-másolásnak; az ND-86 ezért bontja tovább
a mérést. A log az ND-85 működésének bizonyítéka, nem felhasználói vizuális
elfogadás, és nem az új ND-86 gyorsulásának mérése.

## Elkészült lépés

- A vízadatok rendezett összefűzése, indexeltolása és bounds-számítása a
  háttérszálra került. Nem változott a geometria, színforrás vagy víz-cut.
- A parti víz, a változott önálló víz és a határvonal feltöltése a tereppel
  közös, 2 ms/64 munka puha keretű sorban történik, leválasztott mesh-ekbe.
- A régi kép marad látható, amíg minden munka elkészül; a mesh-ek és maszkok
  közösen, külön commit-frame-ben váltanak. Üres víz/kikapcsolt border is
  explicit eredmény, így a régi réteg eltüntetése sem történik túl korán.
- A változatlan önálló víz újrahasználja a korábbi mesh-t. A megszakítás,
  világ-/konfigurációváltás és hibafallback az ND-85 szabályait követi.
- Újrahasználható tartalékok: egy-egy a parti vízhez, önálló vízhez és
  borderhez. A statikus és a nem staginges út változatlan marad.

A meglévő `Use Staged Terrain Upload` kapcsoló az új rétegeket is vezérli.
Scene- vagy Inspector-átállítás nem történt. A Core és a seed változatlan.
Az első zoomok beragadása és a pixelküszöb továbbra is halasztott backlog.

## Várható hatás és korlát

A végső commitból kikerül a víz összefűzése és az aux-mesh-ek feltöltése.
A cél a rövidebb képcsere-frame; konkrét gyorsulást csak új élő mérés igazol.
Egy vízmesh vagy border feltöltése még egyetlen, meg nem szakítható munka:
egy nagy példány túllépheti a keretet, és a csúcs egy korábbi frame-re
helyeződhet át. A maszk, objektumaktiválás, referencia-/anyagcsere és eviction
továbbra is egy frame. Több staging-munka hosszabb megjelenési késést és
több átmenetileg tárolt geometriát jelenthet; nincs új részletességi ígéret.

## Új naplózás és próbamenet

Az `[ND-85 upload begin/slice]` sor megmarad, `pipeline=ND86` jelöléssel.
A `jobs` minden feltöltési munkát számol, az explicit üres eredményt is.
Az async apply `auxPipeline=ND86` mellett naplózza:

- `auxPack`: workeroldali összefűzés/bounds;
- `auxStage`, `auxJobs`: víz/border staging összideje és munkaszáma;
- `terrainPublish`, `legacyAuxPublish`: terep és parti víz/border publikálása;
- `terrainMask`, `waterPublish`, `eviction`: a commit további részei;
- a víz saját sorában `waterMask`: csak a vízmaszk ideje, `staged`: az új út.

A víz `upload` továbbra is a teljes vízpublikálást méri, benne a maszkkal;
a `waterMask` ennek részhalmaza, nem hozzáadandó költség. A külső
`waterPublish` a víz naplózását is tartalmazza. A `maxSlice` továbbra is a
teljes staging-szelet, nem csak a terep ideje. A `stagedVertices` terepszámláló.

Kérjük ugyanazzal a világbeállítással a távoli → közepes → mély zoomot,
megállásokkal és visszazoommal; legyen víz, part és bekapcsolt border is.
Ellenőrizendő: nincs eltűnő víz, lyuk, régi határvonal vagy villanó fedés;
staging közben világváltáskor nem jelenik meg elavult eredmény.
A következő PerfLogból a commitot **és** a legnagyobb staging-szeletet,
valamint a teljes `requestAge` késést együtt kell összevetni a fenti alappal.

## Ellenőrzés és készültség

Solution build: 0 hiba/0 warning. Teljes .NET: **659/659 PASS**
(381 Core + 271 viewer-LOD + 7 CLI); viewer-LOD Release: **271/271 PASS**.
A célzott feltöltési sor tesztjei 19/19 sikeresek, ebből öt új eset a
vegyes rétegek hibás előkészítését és az üres rétegek közös váltását fedi le.
Unity-forrásfordítás: 0 hiba/83 meglévő warning; Editor LOD-tesztassembly:
0 hiba/4 meglévő warning. A valódi vízcsomagolást ellenőrző új Editor-teszt
fordított, **nem futott Unity Editorban**. Nincs élő ND-86 vizuális/FPS-elfogadás.

M9 tartalmilag durván 60–70%. Ráfordítás-egyenérték e lépésre 3–5 óra,
élő validáció/korrekció 1–3 óra, a fennmaradó zoom/render munka 8–20 óra:
durva becslések, nem mért munkaidő, nem vállalási határidő.
