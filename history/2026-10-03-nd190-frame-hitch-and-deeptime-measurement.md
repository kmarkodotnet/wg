# 2026-10-03 — A kép-szaggatás és a deep-time lassúság MÉRÉSE (ND-190, ND-191 nyitva)

Felhasználói jelzés: „A kép generálás megint rendkívüli ütemben szaggat.
Emellett a deep time is megint lassú. Előbb mérd meg mindkettőt Unityben, ha
ezzel megvagy, kezdd el az elsőt javítani."

Környezet: élő Unity Editor Play mód, 6000.0.77f1, Ryzen 7 5700X (8c/16t),
Game nézet 1275×809, seed 184482873278464, t=0 majd t=200 Myr, 66 folyóág.

## 1. Kép-szaggatás — mérve

| Állapot | Frame-idő |
|---|---|
| Nyugalom | p50 **3,7-5,2 ms** (190-270 fps), GPU 3,3 ms, 1609 draw call, 3,87 M vertex |
| Hideg LOD-újraépítés (85 s kamerakör) | **52-159 hitch >60 ms**, 14-59 db >100 ms, max 220-526 ms, 4,7-15,2 s hitchben |

Kizárva méréssel: GC (hitch-frame-ekben 0 kollekció), a szeletelt feltöltés
CPU-ideje (1-2 ms), a commit (max 7,6 ms; egy esetben 56,6 ms), a script
LateUpdate (0-3 ms), a `[ND-75 drawn]` diagnosztika (fő szálon p50 1,04 ms),
és a rajzolt mennyiség növekedése (+20-60% vertex 12-28× frame-idő mellett).

Profiler-bontás elkapott hitch-frame-eken: a fő szál 121-134 ms-ot áll
`GfxDeviceD3D12.WaitForLastPresentation`-ben (saját munkája 7 ms), a render
szál 96-134 ms-ot `WaitForGPU`-ban ill. `RenderLoop` saját időben, a HDRP
tényleges rajzolása ebből 3,5 ms. A GPU-frame 3,3 → 72-103 ms.
Következtetés: nem rajzolás, hanem **GPU-erőforrás-előkészítés** — az
atomikus chunk-publikálás miatt több száz új vertex-buffer első használata
egyetlen frame-re esik.

Részletek, táblázatok, a két elvetett javítás: `docs/04-decisions.md` ND-190.

## 2. Deep time — mérve (0 → 200 Myr, Planet nézet)

Egy fázis-követő szonda 330 s-on, 0,2 s-os mintavétellel:

| t (a csúszka állítása után) | Esemény |
|---|---|
| 0 → 9,6 s | **egyetlen 9617 ms-os frame**: a `Build()` a fő szálon → a kép megfagy |
| 9,6 s | cut indul, folyó-finomítás indul; 10,2 s: első áttekintő folyóág |
| ~13,6 → 23,2 s | **MÁSODIK `Build()`** (9288 ms) — a félkész folyómunkát ELDOBJA, újraindul |
| 23,8 → 86 s | áttekintő folyók 1 → 66 ág |
| **191,2 s** | finom hálózat kész (66 ág) |
| **243,0 s** | a folyómesh kirajzolása befejeződik → a kép ekkor teljes |

A `Build()` fázisbontása (PerfLog, első menet):

| Fázis | Idő | Arány |
|---|---|---|
| **BuildStaticBaseLayer(level 8)** | **6935 ms** | **72%** |
| hydrology (level 8, 153 tó) | 1818 ms | 19% |
| ebből: field 1499 ms, flood 316 ms, lakes 3 ms | | |
| precipitation | 302 ms | 3% |
| elevation+crater+erosion (level 5) | 88 ms | 1% |
| ice / biome / riverSources / riverNetwork | 28 / 6 / 19 / 71 ms | <2% |
| **TELJES** | **9598 ms** | |

Két külön probléma:
1. **19 s fő szálú fagyás**, mert a `Build()` kétszer fut le ugyanarra az
   időpontra (9598 + 9288 ms), és a második eldobja az első folyómunkáját.
   A duplikáció okát MÉG NEM azonosítottam (jelölt: a hőmodell
   készültekor kiváltott `ConsumeThermalClimateRebuildRequest`).
2. A teljes kép **243 s**, amiből ~190 s a folyó-finomítás 4 workeren.

## 3. Javítási kísérletek — mindkettő MÉRVE és VISSZAVONVA

* **Vertex-alapú szelet-kapu** (16 000 vertex/frame): négy kamerakörön
  ellentmondó (>100 ms: 59 → 26, majd 14 → 24). Visszavonva.
* **`Mesh.UploadMeshData(false)` a staging közben**: négy menet, a párok
  második felében megfordított kapcsoló-sorrenddel — **mindkét párban a
  második menet lett jobb, a kapcsoló állásától függetlenül**, azaz a
  cache-melegedést mértem. A nyugalmi p50 ráadásul rosszabb lett
  (5,62 → 6,29 ms). Visszavonva.

**Ami a kódban MARADT (mérés, nem viselkedés):** a PerfLog
`[ND-85 upload slice]` sora mostantól `sliceVertices`-t ír, a kérés-összegző
`maxSliceVertices`-t, és a víz-/határ-mesh vertexei is beszámítanak a
feltöltési könyvelésbe. Mért eloszlás: p50 19 012, p90 35 692, max 54 548
vertex/frame.

## 4. A mérési tanulság (ez a legfontosabb eredmény)

A 85 s-os kamerakör mint mérőpad **nem elég felbontású**: ugyanazzal a kóddal
3× szórás (52 vs 159 hitch), mert a geometria-/metrika-/chunk-cache melegedése
dominál. **Ezen a zajon egy 10-30%-os javítás nem kimutatható**, tehát a
további hitch-javítás előtt hideg kiindulású, ismételt, medián-alapú mérőpad
kell (ND-191).

Ez ugyanaz a hibaosztály, mint az ND-189-nél: ott egy „nyilvánvalóan jó"
javítás bitre azonos kimenetet adott, itt két javítás a zajba esett. Mindkettő
visszavonva — mérhetetlen hatású kódot nem tartunk fenn a kritikus úton.


---

## 5. UTÓLAG, UGYANEBBEN A KÖRBEN: a deep-time dupla Build MEGSZÜNTETVE (ND-192)

A felhasználó a mérések után ezt az irányt választotta. A javítás MÉRVE és
bitazonos; a részletek `docs/04-decisions.md` ND-192.

**Diagnózis.** Az első Build analitikus jég-előnézettel fut, és a kész
hőmodell egy MÁSODIK, teljes Buildet kér (11 697 + 10 449 ms = 22,1 s). Az
ND-162 fejléce szerint cache-találatnál ennek nem így kellene lennie („a Build
MAGA tölti be"), de a kód csak rögzítette a bemenetet; a betöltés a következő
Update háttérszálán indult.

**Miért nem volt „ms" a betöltés.** Új fázismérés a jobra: cache-találatnál a
`SurfaceTemperatureField` felépítése 5838-8487 ms (91-94%), a solver-lenyomat
485-805 ms, a TÉNYLEGES lemez-olvasás **5,7 ms (0,09%)**. A mező eredménye
találat esetén eldobódik — csak a cache-kulcshoz kellett.

**Javítás.** (1) A cache-kulcs (modellazonosító + lenyomat) session-memóriája
a bemenet-azonosító mellé, és a mező KÉSŐI felépítése; (2) a Build maga tölti
be az éghajlatot, ha a kulcs memóriából jön és a lemez talál.

**Mért eredmény (200 ↔ 400 Myr, level 5):**

| menet | klíma-betöltés | Buildek | teljes |
|---|---|---|---|
| kulcs nincs memóriában | 8991 ms | 2 | **22,1 s** |
| kulcs memóriában | **13,2 ms** | **1** | **9,0 s** |

**−13,1 s (−59%)**, és a kép BITAZONOS: `posHash=D91E47AA8ACEB83E`,
`colorHash=055FD0F4C5AACBB7` mindkét úton. Nincs generátorverzió-emelés.

**Ellenőrzés:** offline viewer-kapu 0 hiba, LodChunking 664/664, Editor-
fordítás 0 hiba, Unity konzol 0 piros, `_climateRebuildRequested=False` a
javított menet után (nincs várakozó második Build).

**Két feltevés, amit a mérés megdöntött:**
1. „A hőmodell cache-találata ms" (CLAUDE.md Állapot) — valójában 6,4-9,0 s
   volt, mert a kulcs előállítása dominált. Session első látogatásánál ez
   továbbra is 6-9 s.
2. „A második Build csak színez" — nem: a statikus alapréteg pozíció-hashe is
   megváltozik (692FE8CB… → 441365C9…), tehát egy csak-színező újraépítés
   helytelen lett volna.
