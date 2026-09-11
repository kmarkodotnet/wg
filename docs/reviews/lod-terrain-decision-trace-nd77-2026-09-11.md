# ND-77 — A túl nagy tereptile célzott azonosítása

**Új próba kiértékelve:** az [ND-78 elemzés és részjavítás](lod-early-ocean-exclusion-nd78-2026-09-11.md)
azonosította a méretdefiníciós eltérést és az eldobott tengerfenék által
elfogyasztott osztáskeretet. Az alábbi szöveg az eredeti átadás állapota.

2026-09-11. **Az 1. lépés diagnosztikai része implementált; a terephiba
javítása még nincs kész.** Élő Unity-próba kell a hiányzó azonosításhoz.

## Miért nem következett újabb LOD-hangolás?

A `PerfLog_20260911_205525.txt` ND-76-os futásában 20:56:23.318-kor,
álló kameránál, befejezett finomítás mellett, a képernyőrács alsó sorának
6. pontjában 88,64 px átmérőjű dinamikus tereptile szerepel. A középső
tile 11,16 px. Tehát nem csak víz vagy késő eredmény okozza az eltérést.

Az ND-75 a quad mesh-en belüli sorszámát belül őrizte, de a konkrét mesht
és a TileId-t nem kapcsolta hozzá. A logból a 88,64 px-es tile nem
azonosítható vissza egyértelműen. A proxy alábecslése, a szülő-morph és a
durvább szomszédra illesztés külön hipotézis; egyik sem bizonyított gyökérok.
Ezért az engedélyezett 1. lépésben először ezt a bizonyítékhiányt pótoljuk.
Az implementáció **nem növeli a felbontást**, nem változtat küszöböt/budgetet.

## Implementáció és adatbiztonság

- A CPU-terep meshéhez quadonként explicit TileId-tömb készül. A sorrend
  ugyanaz a material-bucketenkénti emissziós és konkatenálási sorrend,
  mint a vertex/index bufferben. Nem pozícióból becsült tile-azonosság.
- A statikus alap és a CPU-worker chunkolt/nem chunkolt kimenete kap
  azonosítást. Azonos topológiájú pozíciófrissítés az eredeti indexekkel
  együtt őrzi a TileId-ket; az emit-cache a mesh metaadatait is újrahasználja.
  Nem támogatott/régi tartalékút vagy hiányzó besorolás esetén `tile=unknown`,
  nem kitalált azonosító kerül a naplóba.
- A kiválasztás logoláskor tényleges megállásokat rögzít: `below-threshold`,
  `outside-view`, `grazing`, `horizon`, `max-level`, `leaf-budget`,
  `split-quota`. Az utóbbi három különválik a méretbecsléstől. Az aktív
  ND-76 proxy-úton a régi horizon/grazing szűrés nem fut.
- A trace a **sikeresen alkalmazott** kéréshez kötött. A folyamatban lévő
  worker döntései nem keverednek a régi, még látható képpel. A snapshot
  változatlan listákat, értékmátrixokat, kész proxyt és kész trace-t olvas.
- A balance vagy a fedéskiegészítés a kiválasztás után módosíthatja a
  leveleket. Emiatt a megtalált megállási őst külön TileId és `stopRelation`
  jelöli; egy gyermek nem kaphat hamisan saját döntést.
- A közös sarokgazdák az éles `LodCornerResolver` ugyanazon kereséséből
  jönnek; ez nem kér nyers geometriát és nem indít Core-mintavételt.

## Új naplómezők

Az ND-75 mérési definíciója és két másodperces gyakorisága marad.
A fejlécben `terrainIdentity=ND77`, `selectionTraceStops` és
`refinementPending` jelenik meg, továbbá seed/idő/tengerszint/relief adatok.
**Csak `lodPending=False` ÉS `refinementPending=False` jelent üres
kérésláncot.** Ez nem garantál jó felbontást: budget vagy hibás becslés
miatt is megállhat a folyamat.

Snapshotonként a legnagyobb mintázott tereptile mindig kap részletes sort;
további, 24 px fölötti különböző tereptalálatokból összesen legfeljebb nyolc.
A víztalálatok nem kerülnek e listába, az ND-75 vízmérése változatlan.

| Mező | Jelentés |
|---|---|
| `sampleRow`, `sampleCol` | 1-től számozott sor/oszlop, fentről lefelé, mint az ND-75 térképen |
| `mesh`, `quad` | Snapshoton belüli mesh és annak tényleges quad-indexe; nem globális/perzisztens mesh-ID |
| `tile`, `face`, `level`, `u`, `v` | Az explicit emissziós TileId hexában és felbontva |
| `drawnDiameterPx`, `drawnWidthPx`, `drawnHeightPx` | A feltöltött geometriából, az aktuális kamerával mért, clipped értékek |
| `stop`, `stopTile`, `stopRelation` | A valódi kiválasztási megállás, saját tile-on vagy utólag felosztott/kiegészített ősnél |
| `stopErrorRad`, `stopThresholdRad` | Az adott megállás tényleges hibája/küszöbe, nem későbbi visszabecslés; cullnál a nem használt küszöb NaN |
| `stopErrorPx`, `stopThresholdPx` | Ugyanezek a kéréskori kamera pixelskálájára átszámolva |
| `requestProxyVisible`, `requestProxyPx` | A megjelenített tile proxyjának újraértékelése a sikeres kérés kamerájával; nem tényleges mesh-méret |
| `requestTileMorphAlpha` | A tile saját morph-képletének request-kori értéke; a sarokgazdák további hatását nem helyettesíti |
| `cornerOwners` | A négy sarok közvetlen gazdatile-ja és szintje, 00/10/11/01 sorrendben |
| `uploadedQuadCore` | A tényleges feltöltött négy sarok testkoordinátában, round-trip számformátummal |

A `requestProxyPx` teljes proxy-quad méret, míg a `drawnDiameterPx` a
viewporttal vágott, cullingolt feltöltött quad mérete. Mozgó kamera mellett
a request és a pillanatnyi nézet is eltérhet. Ezeket nem szabad automatikusan
azonos nézetű, azonos definíciójú pixelhibaként kivonni egymásból.

## Költség és korlátok

Nincs új világmodell-mintavétel. A TileId-tömb az alap L8-as terepnél kb.
3 MiB, dinamikusan 8 bájt/quad. A megállási dictionary és a fedés megtartása
további CPU-/memóriaköltség; élő költségük még nincs mérve. A kiválasztási
trace csak bekapcsolt `logDrawnTileSizes` esetén készül; a mesh-azonosság
metaadata önállóan megmarad. Nem korlátlan generációtörténetet őrzünk.
A meglévő `cut`, `emit`, `captureMs` és `measureMs` tartalmazza a kapcsolódó
munkát. A 17×9 mintázás nem minden pixel és nem végső GPU-kép.

## Ellenőrzés és következő kapu

- LOD-tesztek: Debug és Release **161/161**, ebből 17 új ND-77 eset.
  Bucket-sorrend, hibás/hiányos ID-map, azonos quad-sorszám külön mesheken,
  megállási okok, ősszint, sarokgazda mintavétel nélkül és a trace ki/be
  kapcsolása mellett változatlan cut/munkaszámlálók.
- Solution build: 0 hiba, 0 figyelmeztetés.
- Unity assemblyk parancssori fordítása: 0 hiba, 83 korábbi figyelmeztetés.
  Ez nem élő Editor-/scene-/vizuális ellenőrzés.
- A teljes Core/CLI tesztfutás eredménye a history naplóban.
- Python nincs telepítve (`py --list` is ezt mutatja); KAT és
  vektorregenerálás nem futott. Core, referencia és tesztvektor nem változott.

**Próba:** Play leállítása → újrafordulás → új Play. Távoli nézetből
közepes, majd mély zoom; a problémás pontoknál 10–15 s megállás, végül
visszazoom. Inspector-hangolás ne történjen közben. A friss logban
`[ND-77 terrain]` soroknak kell megjelenniük. Nem várunk e lépéstől jobb
képet: a pontos hibás tile és döntés azonosítását várjuk.

A tényleges terepjavítás az új bizonyíték után következik. A vízfelszín
leválasztása és az általános optimalizálás nem része ennek a lépésnek.
M9 tartalmi becslése továbbra is 60–70%. Durva, nem mért ráfordítás-egyenérték
erre a diagnosztikai csomagra 2–4 óra, a zoomjavítás/validáció hátralévő
munkájára 8–20 óra, km-UI és új Core-részletmodell nélkül.
