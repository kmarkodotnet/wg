# A8 — tavakon átfolyó folyók és „sehol sem kezdődő" ágak (ND-187, ND-189)

2026-10-03, a felhasználó vizuális visszajelzése az ND-186 + láthatósági kör
után: *„a folyók mennyisége kb ok, bár bizonyos folyók nagyon rövidek. ami
viszont zavaró, hogy látványosan átfolynak a tavakon a folyók, illetve arra is
van példa, hogy szárazföldön kezdődik és ott is van vége a folyónak."*

Mindhárom észrevétel igaznak bizonyult, és **mérhető** volt.

## 1. Mérés — új `lakecross` diagnosztika

A `RiverBaseline` új módja a folyópontokat level 8-as tile-ra képezi, és a
`LakesIceErosion.IdentifyLakes` + `TileCount >= 6 && MaxDepth >= 40` szűrővel
kapott LÁTHATÓ tavakkal metszi (ugyanaz a tó-definíció, amit a viewer rajzol).
Seed 0xA7C944210000, t=0, 96 ág, 46 641,008 km, 531 látható tó (11 505 tile):

| Mérőszám | Érték |
|---|---|
| tó-tile-okon futó hossz | **19 283,466 km = 41,34%** |
| ágak, amik ÁTVÁGNAK legalább egy tavon | 27 / 96 (51 átvágás) |
| leghosszabb egyetlen tó-átvágás | **512,937 km** |
| ágak, amiknek a hossza >90%-ban tavon fut | **13** |
| tóban VÉGZŐDŐ ágak | 32 (a 11 `Pit` **mind** valódi, látható tóban — `endcheck`) |
| tóban KEZDŐDŐ ágak | **17** |
| tengerszint alatt kezdődő ágak | **2** (ezek a 0,00 km hosszú „folyók": #72, #81) |
| 20 km alatti ágak | 6 |

Vagyis a megjelenített „folyó" 41%-a valójában tófelület volt. A „szárazföldön
kezdődik és ott is van vége" a 17 tóból induló + a Pit-végű ágakból jött, a
„nagyon rövid" pedig a tengerszint alól induló forrásokból.

## 2. ND-187 — a tavi szakasz nem folyóvonal (NEM seed-törő)

**Core.** A `ContinuousRiverPath` új mezője a `SubmergedSpans`: azok a
pont-tartományok, amelyek egy zárt medence feltöltési szintje alatt futnak. A
követő már eddig is ismerte ezt a szintet (`RefineEscapePath` a `lakeLevel`
alatti egyeneseket vonja össze), csak nem adta ki. A tartomány a
BEERESZKEDÉSSEL kezdődik (visszamenőleg, amíg a pontok a szint alatt vannak)
és az escape-szakasz végéig tart; az egymásba érő medencék összevonódnak, a
párhuzamos csonkolás pedig ugyanott vágja el őket, ahol az ágat.

Munkarend a szabályzat szerint: **előbb Python-orákulum**
(`river_continuous_ref.py` + invariáns-ellenőrzések: a tartományok a
`Points`-on belül, diszjunktak, növekvők), aztán C# port, és a
`ContinuousRiverV2Tests` mind a tartományhatárokat, mind a küszöb-konstansokat
BITRE az orákulumhoz méri. Új kapu: az orákulum `defaults` blokkja és a C#
konstansok (`escapeEmitMeters`, `submergedMinDepthMeters`, `mergeRadiusMeters`)
nem csúszhatnak el csendben egymástól.

**Mélység-küszöb — MÉRÉSSEL, nem becsléssel.** Küszöb nélkül a jelölés a
hossz 71,80%-át fedte. Söpörve a valódi t=0 hálózaton:

| küszöb | jelölt hossz | % | tartományok | ebből látható tavon | a tó-hossz fedése |
|---|---|---|---|---|---|
| 0 m | 33 490 km | 71,80% | 309 | 16 100 km | 83,49% |
| 10 m | 32 015 km | 68,64% | 214 | 15 924 km | 82,58% |
| **40 m** | **26 025 km** | **55,80%** | **110** | **15 402 km** | **79,87%** |
| 100 m | 16 986 km | 36,42% | 46 | 11 177 km | 57,96% |

40 m lett (ugyanaz, mint a megjelenített tó-réteg mélység-küszöbe): kiszűri az
apró mélyedéseket (309 → 110 tartomány), és a látható tavak 79,9%-át még fedi.

**A vágás kapuja viszont NEM a Core jelölése lett — ezt a mérés döntötte el.**
A Core még 40 m-rel is 55,80%-ot jelöl, a látható tavak viszont csak 41,34%-ot:
a követő a 2 km-es escape-rácson minden lokális mélyedést medencének lát, a
tó-réteg pedig level 8-as tile-okon és tile-szám-szűrővel készül. A span
alapján vágva a folyóhossz 55-72%-a tűnt volna el — jóval több, mint amit a
felhasználó tavakként lát. Ezért a viewer a MEGJELENÍTETT tó-rétegre
(`_adaptiveLakeTiles`, ugyanaz a halmaz, amit a `LakeSurface` rajzol) vág: a
szalag ott szakad meg, ahol a felhasználó tavat LÁT, és nem szakad meg ott,
ahol nincs kirajzolt tó. A `SubmergedSpans` a modell-oldali igazság marad.

Geometria nem változott → **nincs generátorverzió-emelés** ettől a résztől.

## 3. ND-189 — forrás ne induljon tó alól vagy tengerszint alól (SEED-TÖRŐ)

A `SelectRiverSourcesPerBasin` a DURVA (level 5) mezőn döntött, a nyomkövető
viszont a FINOM mezőt látja. Mostantól a jelöltet UGYANAZON a finom ponton
ellenőrizzük, ahonnan a követő indul (bitre ugyanaz a lánc: finom TileId +
`WorldElevationSampler`), és a durva tó-mezőn (`flood.Filled`) is. Aki
elbukik, azt a kvóta a következő jelölttel tölti fel.

Mérve a valódi t=0 hálózaton (ugyanaz a seed, 4 worker):

| Mérőszám | ND-189 előtt | ND-189 után |
|---|---|---|
| ágak | 96 | 96 |
| **tengerszint alatt kezdődő ág** | **2** | **0** |
| tóban kezdődő ág | 17 | 15 |
| tóban végződő ág | 32 | 29 |
| teljes hossz | 46 641,008 km | **47 713,082 km** (+2,3%) |
| tó-tile-okon futó hossz | 41,34% | 40,58% |

A 0,00 km-es „folyók" eltűntek. A maradék 15 tóban kezdődő ág azért marad,
mert a szűrés a DURVA tó-mezőn néz, a mérés viszont a level 8-as tó-rétegen —
de ez a felhasználó szempontjából megoldódik: a tó-tile-okon futó szakaszt a
viewer nem rajzolja, tehát az a folyó a tó PARTJÁN kezdődik a képen.

Generátorverzió **9 → 10**: minden folyóhálózat új, a régi `worldpkg`
elutasítása és a hőmodell-gyorsítótár érvénytelenítése automatikus.

## Ellenőrzés

- Python-orákulum: 8 eset, 1002 pont, kétszeri futás **bitre azonos**; a
  `tools/reference` és a `tests/testdata` vektorfájl egyezik (CI-kapu).
- `ContinuousRiverV2Tests` 5/5, `PerBasinRiverSourceTests` 9/9 (két új eset az
  ND-189-re: tengerszint alatti forrás kiszűrése valódi világon, és a
  tó-küszöb érdemi hatása szintetikus világon), folyó-szűrésű menet 74/74.
- Teljes megoldás **Release: 2007/2007 PASS** (Core 867, viewer LOD 664,
  app 452, CLI 24), EXIT=0; **Core Debug: 867/867** — nincs Debug/Release
  eltérés, a determinizmus-kapu zöld.
- CI-egyenértékű orákulum-ellenőrzés: a regenerált `river_continuous_vectors.json`
  és `thermal_checkpoint_vectors.json` diffje a `tests/testdata` ellen TISZTA.
- Burst-kapu (`check_burst_strict.py`): OK, 343 fájl.
- `dotnet build tests/WorldGen.Viewer.Compile`: 0 hiba.
- Élő Unity Editor: recompile 0 hiba, Play-menet lefutott.

## Nyitott

- A Core escape-`lakeLevel`-je és a `LakesIceErosion` tószintje más
  felbontás (55,80% vs 41,34%) — amíg a tó-réteg durvább, nem esnek egybe.
- A 15 tóban kezdődő ág modell-oldali rendezése (finomabb tó-mező a
  forrás-szűréshez).
- A látvány elfogadása továbbra is a felhasználóé.

## Durva becslés

E kör durván **4-6 emberóra** (becslés, nincs időnaplózás). Az A8 tartalmilag
**~90% → ~93%**; hátra a felhasználói látvány-átvétel és az ND-188.

## Egy hibát a saját javításomban a MÉRÉS fogott meg

Az első Editor-menetben a folyómesh építése megállt: `RiverIndex = 1/96`,
`ProjectedPoints = 1738`, miközben az `ActiveMs` 43 másodpercre nőtt — vagyis
a ciklus pörgött, de nem haladt. **Végtelen ciklus** volt: a tavi szakaszok
kihagyása miatt a felhalmozott szalag lehet 0-1 pontos (ha egy ág utolsó
pontjai mind tó alá esnek), és a meglévő `count < 2 → FlushRiverMeshChunk;
continue;` ág ilyenkor újra és újra ugyanazt az üres állapotot dolgozta fel,
mert a `RiverIndex` nem lépett. A régi kódban ez nem fordulhatott elő, mert a
szalagba MINDEN pont bekerült.

Javítás: a két eset szétválasztása — üres/egypontos szalag → továbbléptetés
(ágra vagy a tó utáni szakaszra), tele chunk → feltöltés. Tanulság a
következő körre: **ha egy képkockákra osztott állapotgép bemenetét szűröm,
az „üres részeredmény" ágat külön kell kezelni** — a `continue` önmagában nem
garantálja a haladást.

## Két teszt elbukott a verzióemelés miatt — mindkettő tanulságos

A teljes Release-menet **865/867** lett. Egyik bukás sem a terméké:

1. **`ThermalCheckpointTests` KAT.** A `tools/reference/thermal_checkpoint_ref.py`
   a generátorverziót BEÉGETVE tartalmazza (`generator = "9"`), mert a
   hőmodell-checkpoint azonosítója tartalmazza. A fájl saját kommentje előre
   figyelmeztetett erre („Verzioemeleskor ITT is emelni kell ES ujra kell
   generalni a vektorokat"), és ez történt az ND-136/ND-137 emeléseknél is.
   Javítás: `"10"`, vektorok újragenerálva, a `tests/testdata` másolat
   szinkronban.

2. **`InvalidCheckpointLeavesDestinationUntouched(damage: "generator")`.** A
   teszt a verziósztring első byte-ját `'1'`-re cserélte, hogy érvénytelen
   verziót állítson elő. A `"10"`-es verziónál az első byte MÁR `'1'`, tehát a
   „rontás" nem rontott semmit, és a várt kivétel elmaradt. Javítás: XOR
   (`bytes[16] ^= 0xFF`), ami bármilyen verziónál garantáltan más értéket ad.
   **Tanulság: egy „rontsuk el az adatot" tesztben a rontás soha ne fix
   literál legyen, ha a rontott mező értéke változhat** — különben a teszt egy
   napon némán zöldre fordul.

## Végeredmény az élő Editorban

| Mérőszám | ND-187 előtt | után |
|---|---|---|
| áttekintő folyómesh | 212 342 vertex, 13 chunk | **110 986 vertex, 7 chunk** |
| finom folyómesh | 919 946 vertex, 57 chunk | **583 780 vertex, 36 chunk** |

A csökkenés (−36,5%) egybevág a 40,58%-os tó-aránnyal (a megtört szalagok
végei plusz vertexeket adnak). A javítás előtti állapotban ugyanezen a tavon
a folyóvonalak keresztülfutottak; most a tó felszíne tiszta, és a folyók a
partnál állnak meg. Lemezre mentett natív képek (mind a VÉGLEGES kóddal, egy szünet nélküli
menetből): `pics/a8-final-lake-overview.png` (tó, áttekintő hálózat),
`pics/a8-final-lake-fine.png` (ugyanaz a tó, finom hálózattal),
`pics/a8-final-river-region.png` (kanyargó folyó hegyvidéken, regionális
nézet), `pics/a8-final-continent.png` (kontinens-nézet: tavak és köztük a
kék folyóvonalak), `pics/a8-final-overview-planet.png` (bolygónézet).

**A képmentésről egy figyelmeztetés a következő körre:** a Unity-híd
`capture_game_view` parancsa az `outputPath`-ra NEM ír fájlt — a képet csak
visszaadja. Az itt hivatkozott képek ezért a kamera RenderTexture-be
renderelésével és `EncodeToPNG`-vel készültek.


---

# A8 zárás: ND-188 és az ND-189 második köre (2026-10-03, ugyanaz a nap)

A felhasználó kérése: *„csináld végig az a8-at és csak akkor szólj, amikor
minden kész."* Ez a szakasz az A8 maradék, általam elvégezhető tételeiről
szól.

## ND-188 LEZÁRVA — és a bejegyzés eredeti feltevése MEGDŐLT

Az ND-188 azt feltételezte, hogy az „áttekintés" azért alig gyorsabb a
véglegesnél, mert az escape-szakaszokat fix 250 m-rel mintavételezzük, és
hogy lépésköz-arányos emisszióval gyorsulna. A `RiverBaseline` `lakecross`
módját kibővítettem a lépésköz ÉS az emisszió söprésére (seed
0xA7C944210000, t=0, 96 ág, 4 worker, ugyanaz a bináris és gép):

| lépésköz | escape-emisszió | hálózatidő | pont | csúcsmemória |
|---|---|---|---|---|
| 1 km | 250 m (eddigi) | 40,3 s | 127 514 | 111,4 MB |
| 1 km | **5 km** | 44,5 s | **25 715** | **89,6 MB** |
| 1 km | 20 km | 42,4 s | 22 263 | 88,9 MB |
| 50 m (finom) | 250 m | **85,0 s** | 512 398 | 164,1 MB |

**Az emisszió a hálózatidőt EGYÁLTALÁN nem befolyásolja** (40,3 / 44,5 /
42,4 s — a futások közti zaj szintje): a költség a lokális priority-floodban
van, nem a pontok kiírásában. Az eredeti indoklás tehát hibás volt. Viszont
az áttekintés a mért **2,0×** nyereséget megadja (40,3 vs 85,0 s), tehát
önálló áttekintő menetre van értelme.

A döntés ezért (A) lett, de MÁS indokkal: a **mesh-költségért**. A pontszám
ötödére esik, és azt a viewer fizeti meg (minden pontot a felszínre kell
vetíteni és szalaggá építeni). Mérve az élő Editorban: az áttekintő
folyómesh **110 986 vertex / 7 chunk → 14 126 vertex / 1 chunk** (−87%), és
az áttekintés ~100 s-nál EGY darabban készen áll.

## ND-189 második kör: egy javítás, amit a MÉRÉS visszavont

A 15 „tóban kezdődő" ág miatt megírtam egy második szűrőt — álló-víz teszt:
a jelölt akkor használható forrás, ha a követő saját jelölt-köréből van
lefelé vezető irány. Determinisztikus, a modellből jön, és szándékosan NEM a
megjelenített tó-rétegből (abból szűrni I1-sértés lenne: a `hydrologyLevel`
megjelenítési beállítás befolyásolná a VILÁGOT).

A valódi t=0 hálózaton mérve a kimenet **bitre változatlan** lett
(47 713,082 km, 512 398 pont, `startsInLake = 15`) — **a szűrő egyetlen
forrást sem utasított el**. Ezért visszavontam a kódot és a hozzá írt
tesztet is.

Ez nem kudarc, hanem eredmény: kiderült, hogy a „tóban kezdődik" NEM
modellhiba, hanem felbontás-különbség. A mérés a level 8-as tó-RÉTEGEN
történik, a forráspont viszont a level 9-es finom mezőn van, ahol van lefelé
vezető irány. A felhasználó ebből semmit nem lát, mert a tó-tile-okon futó
szakaszt az ND-187 óta nem rajzoljuk — a folyó a tó PARTJÁN kezdődik a képen.

**Tanulság: egy javítást akkor is meg kell mérni, ha „nyilvánvalóan" jó —
és ha nem mér semmit, vissza kell vonni.** Nem tartunk fenn mérhetetlen
hatású kódot a kritikus úton (forrásonként 8 plusz eleváció-hívás lett volna).

## Szünet nélküli Editor-mérés és deep-time kapu (a todo2 „ami hátravan" tételei)

Tiszta, Play Pause nélküli menet a VÉGLEGES kóddal (Unity 6000.0.77f1,
`runInBackground` be, konzol a Play előtt törölve):

| Mérföldkő | Idő a Play-től | Állapot |
|---|---|---|
| első ág a képen | ~10 s | `refined = 2` |
| áttekintés fele | ~43 s | `refined = 6`, mesh 5/6 |
| **áttekintés teljes (96 ág)** | **~100 s** | mesh **14 126 vertex / 1 chunk** |
| **finom hálózat teljes (96 ág)** | **~254 s** | `preview = False`, `fine = 96` |
| **teljes finom folyómesh** | **~272 s** | **583 780 vertex / 36 chunk** |

Végállapot: **296 fps**, 847 MB allokált / 1306 MB foglalt (Profiler), a
mesh-építés nem okozott érzékelhető akadást (a legnagyobb szelet a 4 ms-os
budgeten belül).

**Deep-time kapu.** `deepTimeMyr` 0 → 200 futás közben, `Build()`:
új generáció indult (`_riverRefinementGeneration` 2 → 3), a régi folyómesh
eldobva, a hálózat újraszámolt (az áttekintés 66 ágnál 90 s-nál, mesh
17 260 vertex / 2 chunk), **nulla piros kivétel a viewer kódjából**. A
konzolban megjelent két piros sor a saját MCP-hívásom 5 másodperces
timeoutja (`/api/exec`, a szinkron `Build()` miatt) — nem a viewer hibája.

## Záró ellenőrzés

- Teljes megoldás **Release: 2007/2007 PASS** (Core 867, viewer LOD 664,
  app 452, CLI 24); **Core Debug: 867/867** — nincs Debug/Release eltérés.
- Offline Unity-kapuk: `WorldGen.Viewer.Compile` és
  `WorldGen.App.UnityBinding.Compile` 0 hiba.
- Burst-kapu: OK (343 fájl).
- CI-egyenértékű orákulum-diff (`river_continuous_vectors.json`,
  `thermal_checkpoint_vectors.json`): tiszta.
- Élő Editor recompile: 0 hiba.
