# Milestone-terv

A render **nem a végén van**. M2-től minden fázisnak van vizuális kimenete, mert
enélkül nem derül ki időben, ha valami rossz irányba megy.

| M | Név | Tartalom | Vizuális kimenet | Kész, ha |
|---|---|---|---|---|
| M0 | Döntések | ND-01, repo, CI | — | ✅ repo és CI kész; ND-01 **nyitott** |
| **M1** | **Determinisztikus alap** | PRNG, tesztvektorok | — | ✅ **Kész** |
| **M2** | **Grid + első render** | Cubed sphere, TileId, LOD, szomszédság, nyers gömb-render | Szürke gömb, tile-határokkal | **Következő** |
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

Ez kiegyenlíti a tile-területeket: a max/min területarány kb. **1.3**, szemben a
naiv lineáris vetítés kb. **5.2**-es arányával.

> ⚠️ A `tan` transzcendens függvény (ND-23b). A vetítést vagy előre kiszámolt
> táblából interpoláljuk, vagy elfogadjuk, hogy ez **konstrukciós**, nem
> szimulációs számítás. **Ezt M2-ben el kell dönteni** — vedd fel ND-24-ként,
> ha nem triviális.

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
- A kocka **8 sarkánál 3 tile találkozik, nem 4**. Ez az eset külön kezelést
  igényel.

Ha ez rossz, folyólefolyásnál és szélmezőnél azonnal látható műterméket okoz —
folyók, amik "elakadnak" a lap határán.

**Kötelező tesztek:**

| Teszt | Elvárás |
|---|---|
| `RoundTrip` | `TileId → pozíció → TileId` azonos, minden szinten, minden lapon |
| `NeighborSymmetry` | ha B szomszédja A-nak, akkor A is szomszédja B-nek — **kivétel nélkül** |
| `NeighborCount` | minden tile-nak 4 szomszédja van, **kivéve a 8 sarok-tile-t, amiknek 3** |
| `NoGaps` | a 6 lap tile-jainak uniója lefedi a gömböt, átfedés nélkül |
| `AreaDistribution` | max/min tile-terület arány < 1.4 |
| `ParentChild` | `Parent(Child(t, i)) == t` minden i-re; a 4 gyerek uniója a szülő |
| `LodInvariance` | level 6 makrostruktúra == level 9-ből aggregálva |

### 2.5 Render (a stack-döntés után)

Forgatható gömb, tile-határok láthatók, LOD kamera-távolság szerint vált.
Ennek a célja nem a szépség, hanem a **vizuális rács-validáció**: ha a
szomszédság rossz, a tile-határokon látszani fog.

### 2.6 Javasolt sorrend

1. `tools/reference/` — Python cubed sphere referencia, terület-eloszlás mérése
2. `TileId` struct + Morton kódolás/dekódolás + tesztek
3. Koordináta-konverziók + round-trip tesztek
4. Szomszédsági tábla + a fenti tesztek
5. Csak ezután a render

Ez ugyanaz a minta, mint M1-nél: **referencia → verifikálás → C# → mérés**.
