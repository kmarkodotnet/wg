# Milestone-terv

A render **nem a végén van**. M2-től minden fázisnak van vizuális kimenete, mert
enélkül nem derül ki időben, ha valami rossz irányba megy.

| M | Név | Tartalom | Vizuális kimenet | Kész, ha |
|---|---|---|---|---|
| M0 | Döntések | ND-01, repo, CI | — | ✅ repo és CI kész; ND-01 **nyitott** |
| **M1** | **Determinisztikus alap** | PRNG, tesztvektorok | — | ✅ **Kész** |
| **M2** | **Grid + első render** | Cubed sphere, TileId, LOD, szomszédság, nyers gömb-render | Szürke gömb, tile-határokkal | ✅ **Vizuálisan megerősítve** |
| **M3** | **Csillagászat + világítás** | Csillagok, pálya, rotáció, insoláció | Megvilágított gömb, terminátorral | ✅ **Vizuálisan megerősítve** |
| **M4** | **Geológia + domborzat** | Lemezek, kéreg, elevation, tengerszint | Kontinensek, óceánok, árnyékolt hegyek | ✅ **Vizuálisan megerősítve** — `TEST-EARTH-001` numerikusan is teljesítve (117/117 teszt). Ismert korlát: a §13.2 fraktál-zaj hiánya miatt a partvonalak/hegyláncok Voronoi-cella-szerűen szabályosak — tudatosan M13 (Polish)-ra halasztva. |
| **M5** | **Klíma** | Hőmérséklet, szél, nedvesség, csapadék | Biome-színek, hó, jégsapkák | ✅ **Vizuálisan megerősítve** (hőmérséklet+biome-sáv, 138/138 teszt); szél/nedvesség/csapadék halasztva (ld. hatókör) |
| M6 | Atmoszféra-render | Rayleigh-szórás, felhők, ciklonok | Planet nézet lényegében kész | Referenciakép 2 szintjén ~80% |
| **M7** | **Hidrológia + erózió** | Folyók, tavak, gleccser, A1 eróziós pass | Folyók a kontinensnézeten, mikro-vízrajz | ✅ **Vizuálisan megerősítve** ("folyók hegyből tengerbe futnak" strukturálisan bizonyítva, 146/146 teszt); tavak/jég/erózió halasztva |
| **M8** | **Features + panelek** | Szegmentálás, névadás, aggregált metrikák | World/Continent/Region panelek élesben | Kontinens/régió-szegmentálás + névgenerálás ✅ **numerikusan kész** (153/153 teszt); a legtöbb panel-mező (Habitability, Coastal complexity stb.) halasztva; vizuális render hátra |
| M9 | Continent + Region nézet | Magas LOD, displacement, kamera-átmenetek | Referenciakép 1, 3, 4 szintje | Zoom-átmenet folyamatos |
| **M10** | **Deep time** | Lemezmozgás, erózió, eljegesedés, tengerszint | Az időcsúszka él | Lemezmozgás ✅ **vizuálisan megerősítve** (163/163 teszt, TimestepInvariance egzakt; `deepTimeMyr` Unity idő-csúszka - domborzat ÉS biome egyaránt elmozdul, felhasználó által tesztelve); erózió/eljegesedés/dinamikus tengerszint halasztva |
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

---

## M4 — Következő, részletes terv

### Cél

Kontinensek, óceánok, árnyékolt hegyek — `TEST-EARTH-001` teljesítése
(50–75% víz, több kontinens).

### Hatókör (tudatosan szűkítve, a §14-21 teljes spec-tartalmához képest)

A specifikáció (§14 Tektonikai modell) teljes verziója **lemezmozgást**
(§14.2, Euler-rotáció idővel), **lemez-születést/-halált** (§16),
**differenciál-egyenletes uplift/erózió egyensúlyt** (§17-18) ír le — ez
mind **deep-time** tartalom, amit a milestone-terv saját maga M10-re
(Deep time) sorol. M4 "Kész, ha" kritériuma egy **statikus pillanatkép**:
nem kell mozgó lemez ahhoz, hogy 50-75% víz és több kontinens meglegyen.

M4-ben ezért:
- **Lemezek helyzete fix** (nincs `P(t) = R(ωt)P_0` mozgás — az M10-re marad).
- **Uplift/erózió statikus közelítés** (lemezhatár-típus szerinti fix
  magasság-hozzájárulás, nem a §17.2 differenciálegyenlet id��ben integrálva).
- **Lemez-születés/-halál (§16) nincs** — fix `plateCount` a világ elejétől.

Ez ugyanaz a mintázat, mint az M3-nál (kör pálya, nem teljes Kepler-ellipszis)
— a cél a milestone saját elfogadási kritériumának teljesítése, nem a teljes
spec egyszerre.

### 4.1 Lemez-generálás (§14.1)

`plateCount` (tipikusan 6-30, ld. spec) Euler-pólus + szögsebesség
lemezenként, a már meglévő `RandomDomain.Tectonics` / `RandomProperty.
PlateSeedPoint`, `EulerPole`, `PlateVelocity` konstansokkal (ezek M1-ben
már fenn vannak tartva, ld. `RandomDomain.cs`) és a már verifikált
`DeterministicRandom.SampleUnitVector3`-mal (gömbi egyenletes mintavétel).

Minden tile a **legközelebbi lemez-mag** alapján kap `PlateId`-t (gömbi
Voronoi — legközelebbi Euler-pólus nagykör-távolság szerint).

**Kötelező tesztek:** minden tile pontosan egy lemezhez tartozik; a
lemezterületek eloszlása plauzibilis (nincs egy lemez, ami elnyeli az
egész gömböt); a `PlateId`-hozzárendelés tiszta függvény (determinisztikus,
sorrend-független).

### 4.2 Kéregtípus és alap-elevation (§13, §17)

Lemezenként `CrustType` (óceáni/kontinentális), seedelve úgy, hogy a
végső víz-arány a 4.4 lépésben kalibrálható legyen. Alap-magasság
kéregtípus szerint (óceáni: negatív bázis, kontinentális: pozitív bázis)
+ fraktál-zaj részlet (§13.2 `H_0` egyszerűsített, `w_c·C + w_f·F` tagokkal
kezdve, `R`/ridged-hegység és `V`/vulkáni mező később, 4.3-ban).

### 4.3 Lemezhatár-hatás (§14.3, statikus közelítés)

Konvergens határ közelében uplift-bónusz (hegység-proxy), divergens
határ közelében enyhe süllyedés (rift-proxy) — fix, távolság-alapú
csillapítással a határtól, NEM időben integrált differenciálegyenlet.

### 4.4 Tengerszint-kalibráció + `TEST-EARTH-001`

A tengerszintet úgy állítjuk be (a magasság-eloszlás percentilise alapján),
hogy a víz-arány a 50-75%-os célsávba essen. Ez teszi determinisztikusan
mérhetővé és ismételhetővé a `TEST-EARTH-001` kritériumot.

**Kötelező teszt:** `TEST-EARTH-001` — a világ víz-aránya 50-75% között,
és legalább N (pl. 2) diszjunkt, minimális méretet meghaladó szárazföld-
kontinens azonosítható (flood-fill / összefüggő komponens számlálással).

### 4.5 Render (Unity-vizuális ellenőrzés)

A `PlanetGridMesh` kiegészítése: a tile-vertexek radiálisan eltolva az
elevation-nel arányosan (hegyek/óceánmedencék láthatóvá válnak), szín
kéreg-típus/magasság szerint (kék óceán, zöld-barna szárazföld) — ez az a
pont, ahol megint vizuálisan be kell kapcsolódnod.

### 4.6 Javasolt sorrend

1. `tools/reference/` — Python lemez-generálás (Voronoi-hozzárendelés) +
   területeloszlás mérése
2. C# port + tesztek (4.1)
3. Python + C# kéregtípus/alap-elevation (4.2)
4. Python + C# lemezhatár-hatás (4.3)
5. Python + C# tengerszint-kalibráció + `TEST-EARTH-001` (4.4)
6. Unity render-kiegészítés (4.5) — vizuális ellenőrzésed szükséges

Ugyanaz a minta, mint M1-M3-nál: **referencia → verifikálás → C# → mérés**,
csak itt több al-lépésre bontva a nagyobb terjedelem miatt.

---

## M5 — Következő, részletes terv

### Cél

Éghajlati övek felismerhetők — biome-színek, hó/jégsapkák (vizuális
kimenet, render csak a Unity-lépésben).

### Hatókör (tudatosan szűkítve, a §28-32 teljes spec-tartalmához képest)

A §28.1 teljes hőmérséklet-egyenlete (`T = T_radiative + T_greenhouse +
T_ocean - T_altitude + T_weather + T_cycle`) hat komponensből áll. M5
első körben csak a **fizikailag legmeghatározóbb kettőt** implementálja:

- **T_radiative** (§28.2): Stefan–Boltzmann sugárzási egyensúly, a már
  meglévő `OrbitalMechanics.Insolation`-ból (M3) számolt fluxusból.
- **T_altitude** (§28.3): lapse rate × magasság, a már meglévő
  `PlateBoundaryEffect`/`CrustElevation` (M4) elevációjából.

**Halasztva** (dokumentált, nem hiányosság):
- `T_greenhouse`, `T_ocean`, `T_weather`, `T_cycle` — finomítás, ha a
  vizuális/numerikus eredmény indokolja.
- Szél (§30), nedvesség/csapadék (§31), időjárás (§32) — ezek külön
  al-rendszerek, saját referencia-előbb ciklust igényelnek; M5 első
  köre a HŐMÉRSÉKLETI övekre és az ebből adódó jég/hó-classifikációra
  szorítkozik, ami már önmagában kielégíti a "Kész, ha" kritériumot
  (éghajlati övek felismerhetők = hideg pólus, meleg egyenlítő, hideg
  magashegység).

Ez ugyanaz a mintázat, mint M3-nál (kör pálya) és M4-nél (statikus
lemez-pillanatkép): a milestone saját elfogadási kritériumát elégítjük ki
először, nem a teljes spec-tartalmat egyszerre.

### 5.1 Hőmérséklet (§28.2-28.3)

```
T_eq(F, A) = C * (F * (1 - A) / (4 * sigma))^(1/4)
T_altitude = Γ * elevation
T = T_eq - T_altitude
```

`sigma` a Stefan-Boltzmann állandó, `A` egyelőre fix albedo-közelítés
(óceán/szárazföld szerint, később a §29 teljes albedo-modell), `Γ` a
lapse rate (~6.5 °C/km, Föld-szerű illusztrációhoz).

**FIGYELEM:** a negyedik gyök (`^(1/4)` = `Math.Pow(x, 0.25)`) **transzcendens
függvény** — ND-23b/26 osztály. Ugyanazt a mintát követjük, mint ND-26-nál:
M5-ben a kimenet egyelőre csak vizuális/biome-osztályozás bemenete, nem
checkpointolt szimulációs állapot — ha ez változik, új ND-döntés kell.

### 5.2 Biome/jég-osztályozás

Egyszerű küszöb-alapú osztályozás a hőmérséklet + víz/szárazföld (M4)
alapján: jégsapka (nagyon hideg), tundra, sivatag, erdő, óceán — ND-10
("Biome-küszöbök") már nyitott döntésként szerepel erre.

### 5.3 Javasolt sorrend

1. `tools/reference/` — Python hőmérséklet-referencia, ismert fizikai
   értékekhez mérve (pl. Föld átlaghőmérséklete becsült paraméterekkel)
2. C# port + tesztek (5.1)
3. Biome-osztályozás (5.2)
4. Unity render-kiegészítés — vizuális ellenőrzésed szükséges

Ugyanaz a minta, mint eddig mindig: **referencia → verifikálás → C# → mérés**.

---

## M7 — Következő, részletes terv

### Cél

Folyók hegyből tengerbe futnak (nem akadnak el, nem hurkolnak vissza) —
mikro-vízrajz a kontinensnézeten.

### Hatókör (tudatosan szűkítve, a §33-36 teljes spec-tartalmához képest)

**Benne van:** depresszió-feltöltés (§33.1), flow direction (D4, a már
verifikált `TileNeighbors`-ra építve), flow accumulation, folyó-küszöb
osztályozás.

**Halasztva** (dokumentált, nem hiányosság): folyók időbeli változása
(§34 — medervándorlás, deltaépülés, folyóelfogás — ezek deep-time
tartalom, M10-re illenek), tavak (§35 — külön kialakulás-logika kell),
jég/hó (§36 — a hőmérséklet-modellre épül, de külön al-rendszer), és a
tényleges eróziós visszahatás a domborzatra (a folyó bevágja a terepet —
ez a "A1 eróziós pass" a milestone-táblázatban, de a *statikus* folyó-
hálózat felismerése nem igényli az iteratív eróziót, csak az elevation-t
olvassa).

### 7.1 Depresszió-feltöltés

A nyers elevation-mezőn helyi mélyedések (lokális minimumok, amikbe a
víz "beragadna") lehetnek — priority-flood algoritmus (óceán-tile-októl
indulva, mindig a legalacsonyabb, még feltöltetlen szomszédot választva)
biztosítja, hogy minden szárazföld-tile-nak legyen monoton lejtő útvonala
a tenger felé.

### 7.2 Flow direction + flow accumulation

A feltöltött mezőn minden tile a legmeredekebb lejtésű szomszédja felé
folyik (D4, a 4 szomszédos tile közül). Az accumulation csökkenő
magasság sorrendben számolható (topologikus rendezés, mert a feltöltött
mezőn a folyásirány-gráf ciklusmentes).

### 7.3 Folyó-küszöb osztályozás

Egy tile "folyó", ha a flow accumulation egy küszöböt meghalad.

**Kötelező teszt:** minden szárazföld-tile-ból a folyásirányt követve
véges lépésben óceánba (vagy a térkép szélébe) kell jutni — nincs
végtelen ciklus, nincs helyben ragadás.

### 7.4 Javasolt sorrend

1. `tools/reference/` — Python priority-flood + flow direction/accumulation
2. C# port + tesztek
3. Unity render-kiegészítés (kék folyóvonalak) — vizuális ellenőrzésed
   szükséges

Ugyanaz a minta, mint eddig mindig: **referencia → verifikálás → C# → mérés**.

---

## M8 — Következő, részletes terv

### Cél

Minden panelmezőnek valós forrása van (I4) — World/Continent/Region
panelek élesben (`docs/01-architecture.md` §2).

### Hatókör (tudatosan szűkítve, a §2 + §6 teljes tartalmához képest)

**Benne van:**
- **Kontinens-szegmentálás** — már megvan (`SeaLevelCalibration.
  CountContinents`, M7-ből újrahasznosítva), csak metrikákkal bővítve
  (terület, biome-diverzitás).
- **Régió-szegmentálás EGYSZERŰSÍTVE**: csak vízgyűjtő-alapú (a
  `FlowNetwork` már meglévő szülő-fájából — minden szárazföld-tile
  ugyanahhoz a régióhoz tartozik, mint az óceán-"gyökér" tile, amihez
  végül lefolyik). A spec ND-05 hibrid kritériuma (vízgyűjtő ∪ biome-
  klaszter ∪ domborzati törés) közül csak az első van benne — a
  biome-klaszter/domborzati törés finomítás később.
- **Alap névgenerálás**: szótag-tő (seedelt minta) + biome-alapú (nem
  teljes morfológiai tipizálású) utótag-készlet.
- **Néhány panel-mező**, aminek MÁR VAN valós forrása: Ocean coverage
  (World), Name/Area/Biomes/River basins (Continent + Region).

**Halasztva** (dokumentált, nem hiányosság):
- **Morfológiai típusfelismerés** (§6.2 — delta/hegylánc/medence
  mintafelismerés) — a biome-alapú utótag ennek egyszerűsített
  közelítése.
- **Deep-time identitáskövetés** (§6.4, ND-11) — a spec maga is "a
  legnehezebb rész"-nek nevezi, és M10 (deep time) nélkül nincs mit
  követni (nincs még lemezmozgás/szétszakadás esemény).
- **Ordinális kvantálás kalibrálása** (§2.4, ND-09 — ~1000 generált
  világ referencia-eloszlása kellene) — a legtöbb "Low/Moderate/High"
  jellegű mező kimarad ebből a körből, csak a közvetlenül számolható
  (terület, darabszám, %) mezők kerülnek be.
- A legtöbb World/Continent/Region panel-mező (pl. Habitability,
  Coastal complexity, Soil fertility) — ezek más, még nem épített
  modulokra épülnek (talaj, részletes klíma).

### 8.1 Régió-szegmentálás (vízgyűjtő-alapú)

Minden szárazföld-tile "régiója" = az óceán-tile, amihez a `FlowNetwork`
szülő-láncán végül lefolyik. Ez már rendelkezésre áll a priority-flood
eredményéből, csak csoportosítani kell rá.

### 8.2 Névgenerálás

`Sample(seed, "NAME", featureId)` → szótagválasztás a `RandomDomain.
Naming` doménből (M1-ben már fenntartva) + biome-alapú "hangulati"
utótag-készlet (§6.3 mintájára: hideg → Frost-/Rime-, vizes → Silver-/
Tide-).

### 8.3 Aggregált metrikák

Kontinensenként/régiónként: terület (Σ tile-terület), biome-diverzitás
(distinct biome-ok száma), folyó-torkolatok száma (a régióba eső folyó-
tile-ok, amik óceánba folynak).

**Kötelező teszt:** minden generált panel-mezőre van egyértelmű,
visszakövethető számítási lánc (I4) — nincs kitalált/placeholder érték.

### 8.4 Javasolt sorrend

1. `tools/reference/` — Python szegmentálás + névgenerálás + metrikák
2. C# port + tesztek
3. Unity render-kiegészítés (kontinens/régió-határok kiemelése,
   esetleg egy egyszerű debug-panel a nevekkel/metrikákkal) —
   vizuális ellenőrzésed szükséges

Ugyanaz a minta, mint eddig mindig: **referencia → verifikálás → C# → mérés**.

---

## M10 — Következő, részletes terv (autonóm folytatás, felhasználói jóváhagyással: "csináld magadtól")

### Cél

Az időcsúszka él — a lemezek ténylegesen mozognak deep-time-ban (§14.2:
`P(t) = R(ωt)P₀`), nem statikus pillanatkép, mint M4-ben.

### Hatókör (tudatosan szűkítve, a teljes M10 tartalmához képest)

**Benne van:** lemezmozgás (Euler-pólus + szögsebesség, Rodrigues-
forgatás), a `TileId -> plate -> elevation` lánc időfüggővé tétele.

**Halasztva** (dokumentált, nem hiányosság — mindegyik önálló,
referencia-előbb ciklust igényelne): erózió idővel felhalmozódó hatása,
eljegesedés (jégkorszak-ciklusok), dinamikus tengerszint (térfogat-
megmaradás alapú, nem csak percentilis-újrakalibráció), lemez-születés/
-halál (§16). A tengerszint egyelőre továbbra is a MINDENKORI
elevation-mező percentilise (mint M4-ben), csak az elevation-mező maga
változik idővel a lemezmozgás miatt.

**Új időtengely:** a lemezmozgás időegysége **millió év (Myr)**, külön
a csillagászat/klíma "nap" (day_t) tengelyétől — geológiai időskála,
nem napi/évi ciklus.

**ND-27 kiterjesztése (nem új döntés, ugyanaz a már jóváhagyott elv):**
a Rodrigues-forgatás Sin/Cos-t használ — ugyanaz a trigonometria-
kockázati kategória, mint az ND-26/27-nél, ugyanazzal a határidővel
(M12, checkpoint-rendszer előtt kötelező lezárni).

### 10.1 Lemezmozgás

Lemezenként Euler-pólus (`RandomProperty.EulerPole`, M1-ben már
fenntartva) + szögsebesség (`RandomProperty.PlateVelocity`, már
fenntartva, illusztratív tartomány: 0.01-0.09 rad/Myr, kb. 0.5-5°/Myr —
nagyságrendileg reális Föld-analógia). A lemez-mag pozíciója időben:
Rodrigues-forgatás az Euler-pólus körül, `θ = ω·t` szöggel.

**Kötelező teszt:** a lemez-mag `t=0`-nál megegyezik az M4 statikus
pozícióval (visszamenőleges kompatibilitás); `t>0`-nál a pozíció
ténylegesen elmozdul; a forgatás egységvektort ad vissza (nem
degenerálódik); a forgatás explicit, zárt függvénye `t`-nek (nem
iteratív akkumulátor), ezért a "timestep-invariancia" (ND-04)
triviálisan, szerkezetileg garantált — ezt egy konkrét teszt is
bizonyítja (ugyanaz az állapot érkezik meg, függetlenül attól, hogy
egy nagy lépésben vagy sok kis lépés összegeként kérdezzük le `t`-t).

### 10.2 Javasolt sorrend

1. `tools/reference/` — Python lemezmozgás (Rodrigues-forgatás) +
   verifikáció (t=0 visszakompatibilis, timestep-invariancia)
2. C# port + tesztek
3. Unity render-kiegészítés (időcsúszka/idő-mező a `PlanetGridMesh`-en,
   hogy lásd a kontinensek elmozdulását) — vizuális ellenőrzésed
   szükséges, ekkor jelentkezem

Ugyanaz a minta, mint eddig mindig: **referencia → verifikálás → C# → mérés**.
