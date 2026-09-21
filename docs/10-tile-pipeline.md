# A tile-képzés teljes logikája — az alapoktól a mélységekig

Ez a dokumentum azt írja le, hogyan lesz egy matematikai gömbből képernyőre
rajzolt, háromszögekből álló bolygó. Az elején nincs szükség előismeretre; a
végére eljutunk odáig, hogy hol megy el az idő és mit érdemes optimalizálni.

A kód, amire hivatkozik:

| Réteg | Hely |
|---|---|
| Rács-matek (motorfüggetlen) | `src/WorldGen.Core/Grid/` |
| LOD-kiválasztás (motorfüggetlen) | `unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/` |
| Renderelés, feltöltés (Unity) | `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh*.cs` |

---

## 1. Az alapprobléma: a gömböt nem lehet jól felosztani

Egy síkot triviális egyenlő négyzetekre osztani. Egy gömböt nem. Ez nem
kényelmi kérdés, hanem topológia: a gömbnek nincs olyan felosztása, ami
egyszerre lenne **egyenlő területű**, **egyenlő alakú** és **szabályos
szomszédsági szerkezetű**. Bármelyik kettőt megkaphatod, a harmadikat
elveszíted.

Három szokásos válasz:

**Szélesség/hosszúság rács.** Egyszerű, de a pólusoknál minden cella egy
pontba fut össze. Az Egyenlítőnél 100 km-es cella a pólus közelében
centiméteres. Használhatatlan szimulációhoz, mert a szomszédság és a
cellaméret drasztikusan torzul.

**Geodetikus (ikozaéder) gömb.** Területileg a legegyenletesebb, és a
klímamodellezés szereti. De: a cellák hatszögek, 12 helyen ötszöggel, a
szomszédság nem négyzetrácsos, és nincs egyszerű „szülő/gyerek"
bitművelet. A hierarchikus finomítás (amire a LOD épül) bonyolult.

**Kocka-gömb (cubed sphere).** Hat négyzetes lap, mindegyik saját 2D
rácsot kap, aztán a kockát „felfújjuk" gömbbé. Ezt választja a projekt.

### Miért a kocka-gömb

Mert az egyetlen, ami egyszerre adja meg mind a hármat, amire a rendszernek
szüksége van:

1. **Lapon belül szabályos négyzetrács** → a szomszédság `u±1`, `v±1`, a
   hierarchia négyfelé osztás. Ez teszi lehetővé a kvadfát.
2. **Négyfelé osztás = 2 bit** → a szülő egyetlen `>> 2` shift (ld. 3. rész).
   Ez nem szépészeti kérdés: a LOD-bejárás milliószor kérdezi le.
3. **Korlátos torzítás** → a cellaméret aránya kb. 1,3–1,4× a legnagyobb és
   legkisebb között, nem végtelen, mint a lat/lon rácsnál.

Az ára: a hat lap **élei mentén** a szomszédság nem triviális, és a lapok
**sarkainál** (8 kocka-csúcs) a rács szinguláris — négy cella találkozik ott,
ahol máshol nyolc. Ez a projekt egyik visszatérő hibaforrása (ld. 5. rész).

### A tan-warp: miért nem elég a kockát felfújni

Ha a kocka lapját egyenletes rácsra osztod és a pontokat egyszerűen
normalizálod a gömbre, a **lap közepén** lévő cellák jóval nagyobbak
lesznek, mint a **lap szélén** lévők — kb. 1,9× az arány. A cellák a lap
széle felé összenyomódnak.

A javítás (ND-24): ne az egyenletes `t ∈ [-1,1]` koordinátát vetítsd, hanem
annak érintő-torzítását:

```
warp(t) = tan(t · π/4)
```

Ez a lap közepén szétnyomja, a szélén összehúzza a rácsot — pont ellentétesen
a vetítés torzításával. Az eredmény: a terület-arány 1,9×-ről ~1,3–1,4×-re
csökken. `TileGeometry.WarpTan` / `UnwarpTan`.

> **Determinizmus-csapda.** A `Math.Tan`/`Math.Atan` a CLAUDE.md táblázata
> szerint **nem garantáltan bitpontos** platformok között. Ezért az ND-24
> kimondja: ez a leképezés **konstrukciós** — egyszer kiszámolandó és
> tárolandó, nem a szimuláció kritikus útján, futásidőben újraszámolva. Aki
> ezt elfelejti, platformfüggő világot kap.

---

## 2. A két réteg, amiből a kép áll

Mielőtt a részletekbe mennénk, a legfontosabb szerkezeti tény: **két külön
geometria van**, és sok félreértés abból ered, ha összekeverjük őket.

```
                 ┌─────────────────────────────────────────┐
                 │  STATIKUS ALAPRÉTEG (base layer)        │
                 │  - fix szint (adaptiveBaseLevel = 8)    │
                 │  - a TELJES gömböt lefedi, hézagmentesen│
                 │  - Build()-kor egyszer épül             │
                 │  - kameramozgásra NEM változik          │
                 └─────────────────────────────────────────┘
                                   ▲ fölé rajzol
                 ┌─────────────────────────────────────────┐
                 │  DINAMIKUS ADAPTÍV RÉTEG                │
                 │  - CSAK a base szint FÖLÖTTI szintek    │
                 │  - csak ott, ahova a kamera néz         │
                 │  - minden kameramozgásnál újraszámolódik│
                 │  - budget-korlátos                      │
                 └─────────────────────────────────────────┘
```

Ebből következik néhány dolog, ami elsőre meglepő:

- A dinamikus vágás (`cut`) **soha nem tartalmaz** `level <= 8` tile-t. Ha a
  bejárás egy level-6 csomópontnál eldönti, hogy „ez elég durva", azt
  egyszerűen **eldobja** — nem kerül a kimenetbe, mert a statikus réteg
  úgyis fedi.
- Ha a budget elfogy, nem keletkezik lyuk. Csak kevésbé finom a kép.
- A „hány tile van a képernyőn" kérdésnek két válasza van, és a naplóban
  mindkettő szerepel (`cut.Count` a dinamikus, a statikus külön).

---

## 3. TileId: az egész rendszer gerince

Egy tile azonosítója **egyetlen 64 bites egész**. Nincs mögötte objektum,
nincs allokáció, összehasonlítani egy `==`.

```
 63  61 60      56 55                                              0
┌──────┬──────────┬──────────────────────────────────────────────────┐
│ face │  level   │            Morton-kód (56 bit)                   │
│ 3 bit│  5 bit   │   u és v bitjei felváltva összefésülve           │
└──────┴──────────┴──────────────────────────────────────────────────┘
```

### Mi az a Morton-kód

Az `u` és `v` koordináta bitjeit felváltva fésüljük össze:

```
u = 1 0 1 1        (bináris)
v = 0 1 1 0
morton = v3 u3 v2 u2 v1 u1 v0 u0 = 0 1 1 0 1 1 0 1
```

Miért jó ez? Mert a **szülő = `morton >> 2`**. Egy shift. Ez azért van, mert
a legalsó két bit pontosan azt kódolja, hogy a négy gyerek közül melyik
vagy: bit0 = `u` legalsó bitje, bit1 = `v` legalsó bitje.

```csharp
public TileId Parent() =>
    (face << 61) | ((level - 1) << 56) | (Morton >> 2);

public TileId Child(int index) =>
    (face << 61) | ((level + 1) << 56) | ((Morton << 2) | index);
```

Az `u`/`v` kinyerése (`GetUV`) a szokásos bit-szétterítés/összetömörítés
maszkos trükkje (`SpreadBits`/`CompactBits`) — konstans idejű, elágazás nélkül.

### Miért van a face és a level FELÜL

Mert így a `TileId.Value` szerinti egyszerű **egész-rendezés** egyben
kanonikus, hordozható sorrend is: először lap, aztán szint, aztán Morton
(ami térben összefüggő „Z-görbe" bejárás). A kód sok helyen erre támaszkodik:

> „a holtverseny legkisebb `TileId.Value` szerint" — ez teszi a párhuzamos
> és szekvenciális futást **bitre azonossá**. Ha a sorrend a `Dictionary`
> bejárási sorrendjétől függne, a világ nem lenne determinisztikus.

Ez az I1 invariáns egyik legfontosabb technikai horgonya.

---

## 4. Geometria: TileId ↔ 3D pozíció

```
TileId ──GetUV──▶ (u,v) egész ──▶ (uc,vc) ∈ [-1,1] ──tan-warp──▶
   ──▶ kocka-lap pont ──normalizál──▶ egységgömb-pont (x,y,z)
```

`TileGeometry.PositionFromFaceUV` a lényeg:

```csharp
wx = tan(uc · π/4);  wy = tan(vc · π/4);
p[normálTengely] = normálElőjel;        // pl. +X lap: p.x = +1
p[jobbTengely]  += wx · jobbElőjel;
p[felTengely]   += wy · felElőjel;
(x,y,z) = p / |p|;                       // vetítés a gömbre
```

A hat lap tengely-hozzárendelése (`NormalAxis`, `RightAxis`, `UpAxis`) a
projekt **saját, önkényes de következetes** konvenciója — nem külső
szabvány. Ezt fontos tudni: nincs értelme más forrásból vett cubed-sphere
képlettel összevetni.

A visszaút (`FromPosition`) a **domináns tengely** trükkje: amelyik
koordináta abszolút értéke a legnagyobb, az mondja meg a lapot; utána
leosztunk vele (ez a „vetítés a lapra"), és `atan`-nal visszacsináljuk a
warpot.

> Figyeld meg, hogy `PositionFromFaceUV` **elfogadja a [-1,1]-en kívüli
> bemenetet** is. Ez nem hanyagság — a szomszédkeresés épp erre épül.

---

## 5. Szomszédság: a legtanulságosabb rész

A naiv megoldás egy 24 esetes táblázat (6 lap × 4 irány), ami megmondja,
melyik lapra és milyen orientációval lépünk át a lap szélén. **A projekt
szándékosan nem ezt csinálja**, mert ezt a táblázatot könnyű elrontani, és
a hiba csak a lapok élén jelentkezik — ritkán, nehezen észrevehetően.

Helyette (`TileNeighbors.Neighbor`):

```
1. Vedd a tile közepét a SAJÁT lapjának (uc,vc) koordinátájában.
2. Lépj EGY tile-nyit a kívánt irányba — akár a [-1,1] tartományon túlra.
3. Vetítsd a kapott pontot 3D-be (PositionFromFaceUV).
4. Kérdezd vissza, melyik tile az (FromPosition).
```

Ha a lépés átlépte a lap élét, a 3. lépés eleve a szomszédos lap fölé eső
pontot ad, és a 4. lépés domináns-tengely-detektálása **magától** a helyes
lapra „landol". Nincs kézzel írt élkezelés.

Ezt kimerítően ellenőrizték (`tools/reference/neighbor_ref.py`): level 3/4/5
minden tile-jának pontosan 4 szomszédja van, és a szomszédság kivétel nélkül
szimmetrikus.

### Az átlós szomszéd — és miért NINCS használva

Ez a kód egyik legértékesebb kommentje, érdemes elolvasni a forrásban is.
Röviden:

- **A két lépés nem cserélhető fel.** `Neighbor(Neighbor(t, Right), Up)` és
  `Neighbor(Neighbor(t, Up), Right)` egy level-5 rácson a tile-ok **3,12%-ánál
  különbözik** — pontosan a lap szélén lévőknél. Ok: a köztes tile már egy
  másik, **eltérően orientált** lapon van, ahol a „felfelé" nem ugyanaz a
  térbeli irány.
- **Az egylépéses átlós változat sem tökéletes.** Oda-vissza körteszttel
  24576 esetből 768 (3,125%) **nem tér vissza** a kiinduló tile-ra, a kocka
  élei/csúcsai közelében.

A következtetés nem az, hogy rosszul írták meg — hanem hogy **az „átlós
szomszéd" fogalma matematikailag sem egyértelmű** a kocka csúcsainál. Ezért
a hívó (`PrecipAndOceanFractionAtCorner`) lap-határ átlépésénél
**egyszerűen kihagyja** a bizonytalan negyedik mintát, ahelyett hogy egy
pontatlan értéket erőltetne bele.

**Optimalizálási tanulság:** a `Neighbor` hívás nem olcsó (tan, atan, sqrt,
normalizálás). A 2:1 kiegyensúlyozás levelenként **négyszer** hívja, minden
körben. Ha valaha gyorsítani kell, itt van a legnagyobb egyszerű nyereség —
de csak cache-eléssel vagy a bejárás csökkentésével, **nem** a táblázatos
megoldásra váltással.

---

## 6. A LOD alapkérdése: mikor elég finom egy tile?

A cél egyetlen mondatban: **minden rajzolt tile kb. ugyanakkora legyen a
képernyőn**, függetlenül attól, milyen messze van. A cél-méret a
`targetTilePixelSize = 8` pixel.

Ebből adódik a metrika: egy tile akkor kell felosztani, ha a **képernyőre
vetített mérete** nagyobb a célnál. A kód ezt szögben méri:

```
szögsugár = atan2(tile_befoglaló_sugara, kamera_távolság)
```

és ha ez nagyobb egy küszöbnél, osztunk. A küszöb a cél-pixelméretből és a
kamera látószögéből jön.

### Az ND-46 csapda: miért NINCS grazing-korrekció

Kézenfekvőnek tűnik, hogy a **súrlódó szögben** látott tile-ok (a horizont
közelében) kapjanak magasabb prioritást, hiszen ott a felület összenyomódik.
Ezt egyszer beépítették egy `1/cos(grazing)` szorzóval. **Katasztrófa lett**:

> a budget a HORIZONT-SÁVRA ment, NEM oda, ahova a felhasználó néz

Mert a horizont közelében a szorzó felrobban, és minden budget oda folyt. A
mostani kód szándékosan a **nyers** szögsugarat használja, ami a kamerához
legközelebbi (tipikusan a képernyő közepén lévő) tile-t részesíti előnyben.

A terep-proxy (`TerrainLodProxy`) ezt finomítja: a domborzat magasságát is
figyelembe veszi, mert egy hegy közelebb van a kamerához, mint a
tengerszint, és a kiterjedése is nagyobb.

---

## 7. A vágás algoritmusa (`SelectCutPrioritized`)

Ez a rendszer szíve. Prioritásos (best-first) kvadfa-finomítás költségvetéssel.

```
bemenet: kamera, előző vágás, küszöbök, budget
kimenet: levelek halmaza (a "cut")

1.  Kupac ← a gyökér-szint (traversalRootLevel = 3) minden tile-ja,
    de CSAK a láthatóak (lásd alább a szűrőket)
2.  amíg a kupac nem üres:
3.      csomópont ← a LEGNAGYOBB hibájú elem  (ezért "best-first")
4.      küszöb ← ha a csomópont az ELŐZŐ vágásban is fel volt osztva,
                 akkor a lazább "merge" küszöb, különben a "split"
5.      osztanánk? ← szint < maxLevel ÉS hiba > küszöb
6.      ha result.Count + függőben_lévő + 4 > budget:  osztanánk ← nem
                                                       (ok: "leaf-budget")
7.      ha új felosztás ÉS elfogyott a kérésenkénti kvóta: osztanánk ← nem
                                                       (ok: "split-quota")
8.      ha NEM osztunk:
9.          ha szint > alapszint: a csomópont LEVÉL lesz
            (különben eldobjuk — a statikus réteg fedi)
10.     különben: a 4 gyerek a kupacba
11. 2:1 kiegyensúlyozás (lásd 8. rész)
```

### A négy szűrő a bejárás elején

Egy csomópont négyféleképpen eshet ki, **mielőtt** a kupacba kerülne:

| Szűrő | Mit jelent | Következmény |
|---|---|---|
| `horizon` | a bolygó túloldalán van | eldobjuk, nem is értékeljük ki |
| `grazing` | súrlódó szögben | végleges szint, nem finomodik |
| `outside-view` | a látókúpon kívül | végleges szint |
| ND-78 ocean | a renderer úgyis kihagyja | nem építünk alá leszármazottat |

> **2026-09-19-ig a horizont-szűrő a produkciós úton egyáltalán nem futott.**
> Az `EvaluateNodeForPriority` korán visszatért, ha vetített nézet ÉS
> terep-proxy is jelen volt — márpedig éles futásban mindig mindkettő van.
> Következmény mérve: 3 egység magasságban, ahol a látható sapka a gömb
> ~1,4%-a, a vágás **123 152** level-8 csomópontot járt be ~5 500 láthatóra.
> A javítás (ND-115) a kiértékelések **75,5%-át** eltüntette. Ld.
> `history/2026-09-19-horizon-cull.md`.

### A horizont-teszt helyes alakja

Ez azért érdekes, mert az első, kézenfekvő megoldás **hibás volt**. Az
érintősík-teszt (`C·X + |C|·r < R²`) csak a gömb **felszínén** lévő pontra
helyes. Egy hegycsúcs, ami magasabban van, **átkukucskál a horizonton** —
ezért a sík-teszt a limbus menti, valóban látható csempéket is kivágta (54
próbanézetből 6-ban megváltoztatta a vágást, kettőben 5636 levélről 0-ra).

A helyes feltétel szögekben: egy `rP` sugarú pont akkor van az `R` sugarú
takaró mögött `d` távolságból, ha a szöge nagyobb, mint

```
acos(R/d) + acos(R/rP)
```

Koszinuszban kifejtve ez csak szorzást és `sqrt`-et igényel — **transzcendens
függvényt nem**, ami azért kritikus, mert a vágás determinizmusa dokumentált
garancia.

### Hiszterézis: miért két küszöb

Ha egyetlen küszöb lenne, a kamera apró mozgása a küszöb körül oszcilláló
felosztást okozna („villogás"). Ezért a már felosztott csomópont **lazább**
küszöböt kap (`mergeThreshold < splitThreshold`): csak akkor vonjuk össze,
ha érdemben durvább lett. Ez az `previousExpanded` halmaz szerepe.

---

## 8. 2:1 kiegyensúlyozás és a T-csomópontok

Ha két szomszédos tile szintje **kettővel** tér el, a határukon a finomabb
oldal középső csúcsa a durvább oldal élének **közepére** esik — de a durvább
oldalnak ott nincs csúcsa. Ez a **T-csomópont**, és látható repedést okoz a
felszínen.

Két, egymást kiegészítő védelem:

**(a) Restricted quadtree (`EnforceRestrictedBalance`).** Ha egy levél
él-szomszédja több mint 1 szinttel durvább, a durvábbat kényszer-felosztjuk.
Fixpontig iterálva, mert egy felosztás egy másik szomszédnál újabb
egyensúlytalanságot okozhat.

```
ismételd:
    jelöltek ← {}
    MINDEN levélre a cutban:            ← ITT A KÖLTSÉG
        a 4 szomszédjára:
            ős ← a szomszédot fedő levél
            ha levél.szint - ős.szint > 1: jelöltek += ős
    a jelölteket TileId.Value szerint RENDEZVE oszd fel
amíg volt változás
```

**(b) `LodCornerResolver`.** Ami marad, azt geometriailag illesztjük: a
finomabb oldal él-menti csúcsát **nem** a saját modelljéből vesszük, hanem a
durvább szomszéd **már renderelt** élére interpoláljuk. Ez a
`Corner(face, level, u, v)` rekurzió: megkeresi a sarok „gazdáját" (a
legdurvább levelet, ami tartalmazza), és ha az durvább, lineárisan
interpolál a gazda két sarka között.

Fontos következmény: **a `CaptureResolvedPositions` teherhordó** — a
szomszédok geometriája egymástól függ, tehát egy chunk újraépítésekor a
szomszéd már feloldott pozícióit ismerni kell.

### A balance költség-szerkezete (mért)

| budget | levelek | balance | körök | felosztások |
|---|---|---|---|---|
| 20 000 | 19 998 | 31 ms | 1 | **0** |
| 48 000 | 48 000 | 62 ms | 1 | **0** |
| 96 000 | 66 900 | 332 ms | 4 | 108 |

Ahol a budget megköt, a ciklus **nulla felosztást** végez — de ezért
végigszkenneli a teljes cutot. Ez volt az ND-116 javítása: ha az őr már
belépéskor blokkol, örökre blokkol (a `SplitOnce` nettó +3, a méret csak nő),
tehát a szkennelésnek nincs kimenete. 180 próbaeseten **nulla megváltozott
vágás**, a balance összideje **9068 → 1832 ms**.

**Ami nyitva maradt:** ahol van fejtér, 108 felosztás 332 ms (~3 ms
felosztásonként), mert minden kör újraszkennel mindent. Munkalista
megoldaná — de a jelenlegi kimenet **sorrend-függő** (körönként gyűjt és
rendezve oszt, az őrök mid-iterációban is blokkolhatnak), tehát előbb azt
kell tisztázni, egyértelmű-e a fixpont.

---

## 9. Geomorph: a pattanás elsimítása

Amikor egy tile felosztódik, a geometria hirtelen megváltozik — ez látható
„pattanás". A megoldás: a gyerek csúcsai **nem ugranak** a végleges helyükre,
hanem a szülő felületéről indulva átúsznak.

```csharp
GeomorphAlpha(distance, splitDistance, rangeFraction)
    = clamp((splitDistance - distance) / (rangeFraction · splitDistance), 0, 1)
```

`alpha = 0`-nál a gyerek még pontosan a szülő felületén ül, `alpha = 1`-nél
már a saját helyén. A `distance` a kamera aktuális távolsága, tehát a
morfolás a kamera mozgásával folytonos.

---

## 10. Chunkolás: miért nem épül újra minden

Ez az ND-47 „igazi fix". A probléma: ha minden kameramozgásnál a **teljes**
dinamikus geometria újraépül és feltöltődik, a főszálas
`Mesh.SetVertices`/`SetTriangles` költsége a **teljes cut** méretével
arányos, nem a ténylegesen változott résszel. Ezért nem lehetett a budgetet
emelni anélkül, hogy minden mozgás akadjon.

A megoldás: a vágást rögzített méretű **chunk**-okra bontjuk (egy chunk egy
`chunkLevel`-szintű tile leszármazottainak halmaza a cutban), és kiszámoljuk,
**mely chunk-ok változtak** az előző kerethez képest. A változatlan chunk-ok
Unity mesh-objektumához hozzá sem nyúlunk.

`DynamicMeshChunking.GroupByLeafBudget` — területi csomagolás kemény
levélszám-korláttal (ND-80, alapból 256 levél/chunk). Egy korábban osztott
chunk csak fél kapacitásnál vonódik össze (szintén hiszterézis, ugyanazért,
amiért a LOD-nál).

### Feltöltés: időszeletelve

`PlanetGridMesh.Upload.cs`:

```csharp
private const double TerrainUploadSliceMs = 2;    // főszál, KERETENKÉNT
private const int TerrainUploadsPerFrame = 128;
```

Ez a legfontosabb egyetlen szám a szaggatás szempontjából: a feltöltés
keretenként **2 ms**-ra van szeletelve. Ebből következik, hogy egy nagyobb
mesh **több staging-frame**, nem nagyobb akadás — az ára latencia, nem
stutter. Ez az érv tette lehetővé a budget emelését 8 000-ről 39 807-re.

---

## 11. A teljes Build() futás fázisai

A `PerfLog` sorai pontosan ezt a sorrendet mutatják:

```
seeds           lemez-magok
elevation       kéreg + domborzat mező           ← a legdrágább, hidegen
seaLevel        tengerszint-kalibráció
ice             évi hőmérséklet-statisztika       ← 936 ms / 8602 tile
precipitation   nedvesség-advekció
clouds          felhő-réteg (a csapadék-mezőből)
dendriticRivers folyóhálózat
BuildStaticBaseLayer  a fix, level-8 geometria
adaptive        az első dinamikus vágás
riverNetwork    folyó-vonalak
TELJES
```

A dinamikus vágás ezután **minden kameramozgásnál** újrafut, de a Build
többi fázisa nem — az csak akkor, ha a világ-konfiguráció változik
(`WorldConfigChangedSinceBuild`).

> **Ismert hiba (ND-50):** a `windSpeedOverlay`/`precipitationOverlay`
> kapcsolgatása **teljes `Build()`-et** indít, pedig csak színt cserél. A
> felhő és a folyó-vonal már kapott kivételt; ezek nem, mert nem külön
> réteg, hanem a fő terep-mesh színét változtatják.

---

## 12. A GPU-ág — MÁR NINCS (ND-120, ND-128)

Volt egy `Gpu/TileClassification.compute` shader két kernellel
(`CSClassifyTiles` tile-osztályozás, `CSGenerateTerrainGeometry` geometria),
és két kapcsoló, ami rájuk kötött (`useGpuClassification`, `useGpuGeometry`).
**2026-09-21-én mindkettő törölve lett**, a shaderrel együtt. Érdemes tudni,
miért — mert a tanulság általános.

A shader **újraimplementálta** a Threefry4x64 PRNG-t (32 bites párokon, mert
HLSL-ben nincs natív 64 bites egész) és a trigonometriát Taylor-sorral. Vagyis
a világmodell egy darabja **kétszer** létezett: egyszer C#-ban, egyszer
HLSL-ben. Két, egymástól független implementáció pedig elcsúszik — és itt el
is csúszott: a shader `BaseElevationF`-je az ND-52 (másodlagos részlet-zaj) és
az ND-90 (lemezhatár-keverés) ELŐTTI állapotban maradt.

**Mennyit jelent ez számokban?** `GpuShaderElevationParityTests`: átlagos
eltérés 300,8 m, a tile-ok **~22%-a** kerülne a tengerszint másik oldalára,
**~24%-a** más biome-ot kapna. Ez nem árnyalatnyi hiba — más bolygó.

A törlés mellett szólt az is, hogy a GPU-geometria élő mérésben **lassabb**
volt a CPU-útnál (~50 s/újraépítés): a kernel tile-onként külön számolta mind
a négy sarkot, míg a CPU-út a szomszédos tile-ok között megosztott sarkakat
csak egyszer (sarok-cache).

**Ha valaha újra kell:** sarok-szintű (nem tile-szintű) dispatch, a Core
elevációjából származó KÖZÖS adat (nem újraimplementált képlet), és egy
CPU/GPU egyezési kapu, ami a különbséget méri. A régi shader ehhez nem alap.

---

## 13. Determinizmus: a keresztmetsző követelmény

Minden fenti réteget átjár négy szabály (CLAUDE.md I1–I4). A tile-pipeline
szempontjából a fontosak:

| Szabály | Mit jelent a gyakorlatban |
|---|---|
| Bitpontos alapműveletek | `+ - * /` és `Math.Sqrt` igen; `Sin/Cos/Exp/Log/Pow/Tan` **nem** |
| Rendezés | soha nem `Dictionary`-sorrend; mindig explicit `TileId.Value` |
| Párhuzamosság | a párhuzamos és szekvenciális futásnak bitre egyeznie kell |
| `float` tilalom | fizikai mennyiségre mindig `double` |

Ezért van az, hogy:
- a tan-warp **baked**, nem futásidejű (ND-24),
- a vágás best-first bejárása **egyszálú**, teljes rendezéssel,
- a horizont-teszt koszinuszban van kifejtve, hogy elkerülje az `acos`-t,
- és ezért nincs többé GPU-oldali, ÚJRAÍRT világmodell-matek (§12).

---

## 14. Hol megy el az idő (mért számok, 2026-09-19)

Level 6, meleg cache, a horizont-vágás és a balance-javítás után:

| Fázis | Költség | Megjegyzés |
|---|---|---|
| Vágás (selection), meleg | 7–22 ms | a horizont-vágás előtt 157–183 ms |
| Vágás, hideg metrika-cache | 35–103 ms | 482–689 ms volt |
| Balance, budget megköt | ~0 ms | 31–78 ms volt |
| Balance, fejtérrel | 332 ms / 108 felosztás | **nyitott** |
| Mesh-feltöltés, főszál | 0,57–2,67 ms | 2 ms/keret szeletelve |
| Statikus base-layer (meleg) | 1,602 s | klasszifikáció 483 + emit 664 ms |
| Évi hőmérséklet (ice fázis) | 936 ms | 8602 szárazföld-tile |
| Hideg Build, terrain-bázis | 6,9 s a 13,0-ból | **nyitott** |

A vágás **látogatás/levél** aránya ~25-ről **~3,4**-re esett. Ez a szám a
legjobb egyetlen mérőszáma annak, hogy a bejárás mennyire pazarol.

---

## 15. Hogyan optimalizálj — módszertan

Ez a rész a legfontosabb, mert a mai nap minden nagy nyeresége ugyanabból a
mintából jött, és minden zsákutca ugyanabból a hibából.

### A négy lépés, ebben a sorrendben

1. **Állapítsd meg**, mi történik — a kódból, ne emlékezetből.
2. **Mérd meg** — és a mérés a *dolgozó* kódból jöjjön, ne izolált próbából.
3. **Javasolj**, a mérésre hivatkozva.
4. **Implementálj**, és **ellenőrizd újra** ugyanazzal a méréssel.

### Öt konkrét csapda, amibe ma beleestem

**(1) Lineáris extrapoláció mérés helyett.** A fix 8000-es budget egy
lineáris extrapolációból született („8000 levél ≈ 400–500 ms"). A valóság:
a bejárás költségét a **látott felület** hajtja, nem a budget — 3× budget
csak **+28%** worker-idő. Ugyanezt a hibát én is elkövettem, amikor azt
mondtam, „16× budget = többmásodperces latencia".

**(2) Marginális eloszlásból következtetés.** A `nadirCutL=8` és
`deepestCutL=20` külön-külön nézve „hibás budget-elosztásnak" látszott.
Valójában mindkettő helyes viselkedés (ND-78 óceán-kizárás, illetve ferde
kamera — a nadír a kamera *alatti* pont, nem a képernyő közepe). Az együttes
eloszlás és négy offline próba cáfolta.

**(3) Az első bukó assert ≠ a maximum.** A toleranciát az első hibaüzenetből
becsültem (3,4e-13), holott a valódi maximum **2,99e-09** — négy
nagyságrenddel nagyobb. A teszt értékenként áll meg.

**(4) Izolált mérés a dolgozó kód helyett.** A balance-t a `BuildCut` *által
visszaadott* cuton mértem, ami már kiegyensúlyozott — nulla felosztást
jelentett minden budgetnél. A mérés semmit nem mondott. Számlálót kellett
tenni a dolgozó kódba.

**(5) A `SerializeField` alapérték nem az igazi érték.** A scene tárolja a
szerializált értéket, ami felülírja a mező kezdőértékét. A viewport-budget
változtatásom **néma no-op** lett volna, ha nem írom át a `PlanetView.unity`-t is.

### Amit ellenőrizz, mielőtt bármit átírsz

- **Megváltozik-e a vágás?** A `history/`-ban van hozzá minta: N nézetre
  FNV-hash a rendezett `TileId.Value`-kból, előtte/utána összehasonlítva.
- **Ha megváltozik, romlik-e a látható kép?** Ez a *valódi* kérdés. A
  horizont-vágás nem adott bitre azonos cutot (48/54 nézet igen, 6 nem), de
  a látható fedettség 54 nézet × 21×11 mintán **pontosan változatlan** maradt.
  Ez elég volt a szállításhoz; a halmaz-azonosság nem lett volna elérhető.
- **Sorrend-függő-e a kimenet?** Ha igen (mint a balance-nál), egy
  „ekvivalens" átírás nem feltétlenül ekvivalens.

### Hol keress nyereséget most

| Hely | Miért |
|---|---|
| Balance munkalista | 332 ms / 108 felosztás, ~3 ms felosztásonként |
| `TileNeighbors.Neighbor` cache | a balance levelenként 4×, minden körben hívja |
| Statikus mesh direct-array emit | 1,602 s-ból 664 ms az emit |
| Hideg terrain-bázis lemezcache | 6,9 s a 13,0-ból |
| ND-50 (overlay ≠ Build) | nem gyorsítás, hanem elkerülés |

Amit **ne** csinálj: ne emeld a `MaximumRenderBudget`-et, amíg a balance
munkalistás átírása nincs meg. A mérés szerint a balance csak akkor dolgozik,
ha a budget a metrika telítődése **fölött** van — nagyobb plafon tehát
gyakrabban visz a drága tartományba, nem ritkábban. Ez ellentétes az
intuícióval, és pontosan ezért érdemes mérni.
