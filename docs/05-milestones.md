# Milestone-terv

A render **nem a végén van**. M2-től minden fázisnak van vizuális kimenete, mert
enélkül nem derül ki időben, ha valami rossz irányba megy.

| M | Név | Tartalom | Vizuális kimenet | Kész, ha |
|---|---|---|---|---|
| M0 | Döntések | ND-01, repo, CI | — | ✅ repo és CI kész; ND-01 **nyitott** |
| **M1** | **Determinisztikus alap** | PRNG, tesztvektorok | — | ✅ **Kész** |
| **M2** | **Grid + első render** | Cubed sphere, TileId, LOD, szomszédság, nyers gömb-render | Szürke gömb, tile-határokkal | ✅ **Vizuálisan megerősítve** |
| M3 | Csillagászat + világítás | Csillagok, pálya, rotáció, insoláció | Megvilágított gömb, terminátorral | Évszakok látszanak a terminátor mozgásán |
| M4 | Geológia + domborzat | Lemezek, kéreg, elevation, tengerszint | Kontinensek, óceánok, árnyékolt hegyek | `TEST-EARTH-001`: 50–75% víz, több kontinens |
| M5 | Klíma | Hőmérséklet, szél, nedvesség, csapadék | Biome-színek, hó, jégsapkák | Éghajlati övek felismerhetők |
| M6 | Atmoszféra-render | Rayleigh-szórás, felhők, ciklonok | Planet nézet lényegében kész | Referenciakép 2 szintjén ~80% |
| M7 | Hidrológia + erózió | Folyók, tavak, gleccser, A1 eróziós pass | Folyók a kontinensnézeten, mikro-vízrajz | Folyók hegyből tengerbe futnak |
| M8 | Features + panelek | Szegmentálás, névadás, aggregált metrikák | World/Continent/Region panelek élesben | Minden panelmezőnek valós forrása van (I4) |
| M9 | Continent + Region nézet | Magas LOD, displacement, kamera-átmenetek | Referenciakép 1, 3, 4 szintje | Zoom-átmenet folyamatos |
| M10 | Deep time | Lemezmozgás, erózió, eljegesedés, tengerszint | Az időcsúszka él | TimeTravel + TimestepInvariance zöld |
| M11 | Események | Becsapódás, vulkán, rift, split/merge | Kráterek, kitörések láthatók | Acceptance A–E zöld |
| M12 | Perzisztencia + CLI | Checkpoint, .worldpkg, state hash | — | `worldgen verify` reprodukál |
| M13 | Polish | Volumetrikus felhő, AO, víz-shader, színkalibráció | Végleges látvány | Vizuális acceptance (spec §73) |

---

## M1 — Kész

`src/WorldGen.Core/Random/` — Threefry-4x64-20, `Sample()` API, 512 tesztvektor.

Verifikációs státusz:

| Elem | Státusz |
|---|---|
| Threefry algoritmus | ✅ 9/9 hivatalos Random123 KAT (13, 20, 72 kör) |
| Tesztvektorok | ✅ verifikált referenciából generálva |
| Eloszlás | ✅ 200k minta: átlag 0.50051, szórásnégyzet 0.08316 |
| Gömbi mintavétel | ✅ 1.903 átlagos iteráció (elmélet 6/π = 1.910) |
| **C# fordíthatóság** | ⚠️ **nem ellenőrizve** — nem volt .NET a generáló környezetben |

**Az első teendő Claude Code-ban: `dotnet test`.** Ha a KAT-tesztek átmennek, a
C# port helyes. Ha nem, a Python referencia az igazság.

---

## M2 — Következő, részletes terv

### Cél

Cubed sphere rács, `TileId` séma, LOD-hierarchia, szomszédság — plusz egy nyers,
forgatható gömb-render, ami vizuálisan validálja a rácsot.

### 2.1 TileId bit-layout

```
 63          61 60      56 55                                        0
┌──────────────┬───────────┬──────────────────────────────────────────┐
│  face (3b)   │ level(5b) │  Morton-kód, u/v interleaved (56b)       │
└──────────────┴───────────┴──────────────────────────────────────────┘
```

- `face`: 0–5, a kocka lapja
- `level`: 0–28
- Morton: az `(u,v)` tile-koordináták bitenként összefésülve

**Miért Morton:** a szülő `TileId` puszta bit-művelettel adódik (`morton >> 2`),
ami a LOD-invarianciát (spec §70.5) triviálissá teszi. A gyerek-tile-ok
determinisztikus mintája a szülő ID-ból származik, tehát a hierarchikus noise
(spec §52) ingyen jön.

### 2.2 Gömbre vetítés

**Egyenszögű (tangens-alapú)**, nem lineáris:

```
Egy [-1,1] tartományú kocka-koordinátára:
    s = tan(u * pi/4)
```

Ez kiegyenlíti a tile-területeket a naiv lineáris vetítés kb. **5.03**-as
arányához képest — de a max/min területarány **LOD-szinttől függ** és
√2 ≈ **1.4142**-höz tart a felbontással (mérve: `tools/reference/
cubed_sphere_ref.py`, ld. ND-24 a `docs/04-decisions.md`-ben). Level 6-nál
1.3969, level 11-nél már 1.4137 — ezért a `AreaDistribution` teszt küszöbe
LOD-szint szerint differenciált (ld. lent).

> A `tan` transzcendens függvény (ND-23b osztály) kockázatát az ND-24 zárja
> le: a `TileId -> pozíció` leképezés **konstrukciós (baked)** számítás —
> egyszer kiszámolva, verzióhoz kötve, hash-elve; a szimuláció ebből olvas,
> nem újraszámolja.

### 2.3 LOD-szintek

| Level | Tile / face | Össz tile | Tile-él (7420 km sugárnál) | Használat |
|---|---|---|---|---|
| 5 | 1 024 | 6 144 | ~410 km | Deep-time tektonika |
| 6 | 4 096 | 24 576 | ~205 km | **Globális klíma alapfutás** |
| 7 | 16 384 | 98 304 | ~102 km | Bolygónézet render |
| 9 | 262 144 | 1 572 864 | ~26 km | Kontinensnézet |
| 11 | 4 194 304 | 25 165 824 | ~6 km | Régiónézet (streaming) |

### 2.4 Szomszédság — itt lesz a hiba

A cube-lapok találkozásánál a szomszédság nem triviális:

- A 12 él mentén az `u/v` tengelyek **átfordulnak** — a szomszéd tile
  koordinátáit transzformálni kell.
- A kocka 8 sarkánál 3 LAP találkozik.

Ha ez rossz, folyólefolyásnál és szélmezőnél azonnal látható műterméket okoz —
folyók, amik "elakadnak" a lap határán.

> A "8 sarkánál 3 tile, nem 4" eredeti állítás **NEM igazolódott** kimerítő
> méréssel (`tools/reference/neighbor_ref.py`, level 3/4/5, minden tile,
> 4-szomszédos él-adjacencia): a 24 lap-sarok-tile mindegyikének is
> pontosan 4 éle (és 4 él-szomszédja) van, szimmetria-hiba nélkül — egy
> négyzet cella mindig 4 élű, sarkon is. A "3 szomszéd" valószínűleg egy
> MÁSIK, 8-szomszédos (átlós) kapcsolódási módra igaz, ami a kocka
> csúcsainál lesz releváns (ott ténylegesen csak 3 lap találkozik egy
> pontban) — ez a hidrológia/szél D8-jellegű algoritmusainál (M7+) merülhet
> fel újra, nem a jelen (él-alapú) szomszédsági táblánál. Ld. lezárt
> döntésként `docs/04-decisions.md`.

**Kötelező tesztek:**

| Teszt | Elvárás |
|---|---|
| `RoundTrip` | `TileId → pozíció → TileId` azonos, minden szinten, minden lapon |
| `NeighborSymmetry` | ha B szomszédja A-nak, akkor A is szomszédja B-nek — **kivétel nélkül** |
| `NeighborCount` | minden tile-nak (a 24 lap-sarok-tile is) pontosan 4 él-szomszédja van — mérve, nincs kivétel (ld. fenti megjegyzés) |
| `NoGaps` | a 6 lap tile-jainak uniója lefedi a gömböt, átfedés nélkül |
| `AreaDistribution` | max/min tile-terület arány: level ≤ 6 esetén < 1.40, level ≥ 7 esetén < 1.42 (ND-24) |
| `ParentChild` | `Parent(Child(t, i)) == t` minden i-re; a 4 gyerek uniója a szülő |
| `LodInvariance` | level 6 makrostruktúra == level 9-ből aggregálva |

### 2.5 Render (a stack-döntés után) — ✅ vizuálisan megerősítve

Forgatható gömb, tile-határok láthatók, LOD kamera-távolság szerint vált.
Ennek a célja nem a szépség, hanem a **vizuális rács-validáció**: ha a
szomszédság rossz, a tile-határokon látszani fog.

A `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs` (rács →
Unity mesh híd) Game módban ellenőrizve: szürke gömb, tile-határokkal,
hézagok/geometriai hibák nélkül level 2, 5 és 6-nál. Level 7-nél
(98 304 tile) a sok vékony határvonal moiré-mintázatot ad a képernyőn — ez
**optikai aliasing**, nem geometriai hiba (a rács helyessége már unit
tesztekkel és alacsonyabb LOD-on vizuálisan is bizonyított; a
`PlanetGridMesh` időközben `showBorders` kapcsolót is kapott, hogy magas
LOD-on ki lehessen kapcsolni a határvonalakat). A "Kész, ha" kritérium
(szürke gömb, tile-határokkal) ezzel teljesült.

### 2.6 Javasolt sorrend

1. `tools/reference/` — Python cubed sphere referencia, terület-eloszlás mérése
2. `TileId` struct + Morton kódolás/dekódolás + tesztek
3. Koordináta-konverziók + round-trip tesztek
4. Szomszédsági tábla + a fenti tesztek
5. Csak ezután a render

Ez ugyanaz a minta, mint M1-nél: **referencia → verifikálás → C# → mérés**.
