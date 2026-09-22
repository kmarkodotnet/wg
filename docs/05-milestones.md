# Milestone-terv

**2026-09-22, M12 / A6 verziókapu: lezárva (100% ebben a részfeladatban).**
ND-108: közös Core-generátorazonosító, `.worldpkg` v3, app `CoreSavePolicy`,
közvetlen betöltéskori ellenőrzés. Hiányzó/eltérő azonosítóval az állapot
nem tölthető; konfiguráció az app fejlécéből külön olvasható. **1531/1531**
Debug-teszt; app 451/451 és CLI 23/23 Release-ben is. Élő Unity-fordítás
és adapterpróba kész. A teljes app-állapotmentés/session-host/UI továbbra
is C3/D4, nem A6. Durva ráfordítás **0,5–1 munkaóra**, hátralévő **0 óra**.
[A6 napló](../history/2026-09-22-a6-generator-version.md).

**2026-09-22, aktuális M9/M10-részfeladat:** a `todo2.md` A4 deep-time
újraépítési tétele felhasználói kérésre lezárva (12 meleg Build átlaga
2,479 s; az eredeti <1 s küszöb nem igazolt). A5 variancia-/allokációprofil
lezárva (a pontosított profilozási részfeladat 100%): ND-133–135, kontrollált mérés és a mesh-listák pontos
előfoglalása elkészült. Azonos konfigurációjú 10–10 kontroll átlaga
2375 → 2150 ms, főszálú allokáció ~692 → ~515 MB; három mesh-hash azonos.
A részletes víz- és CPU-profil, worker-hívásláncok és Development Player-
kontroll elkészült: a Player 10 mért Buildje 2038 ± 33 ms, a víz 62–71 ms.
A Build alatti GC-kikapcsolás rövid próbája csak ~2,4% időeltérést adott,
nagy managed heap mellett; termékbeli GC-változás nincs. Az Editorban mért
CPU-csúcsok belső futtatókörnyezeti oka és a natív allokációs események
részletes követése opcionális további vizsgálat, nem elvégzett munka.
Záró ellenőrzés: 20 időváltásos Editor-Build 1,87–2,37 s, ugyanahhoz az
időponthoz visszatérve legfeljebb 116 kB követett memóriaingadozás;
0/100/500 Myr ismételt terep-/víz-/tó-hash-e azonos. A rövid mérés nem
hosszú szivárgásteszt vagy vizuális átvétel. Durva összes ráfordítás
5–7 munkaóra, ebben a profilozási feladatban hátralévő 0 óra.
[Második mérési napló](../history/2026-09-22-a5-water-cpu-player-profile.md).
[Első mérési kör naplója](../history/2026-09-22-deep-time-allocation-profile.md).
A lentebbi történeti milestone-sorok nem friss teljesprojektes auditok.

A render **nem a végén van**. M2-től minden fázisnak van vizuális kimenete, mert
enélkül nem derül ki időben, ha valami rossz irányba megy.

| M | Név | Tartalom | Vizuális kimenet | Kész, ha |
|---|---|---|---|---|
| M0 | Döntések | ND-01, repo, CI | — | ✅ repo és CI kész; ND-01 **nyitott** |
| **M1** | **Determinisztikus alap** | PRNG, tesztvektorok | — | ✅ **Kész** |
| **M2** | **Grid + első render** | Cubed sphere, TileId, LOD, szomszédság, nyers gömb-render | Szürke gömb, tile-határokkal | ✅ **Vizuálisan megerősítve** |
| **M3** | **Csillagászat + világítás** | Csillagok, pálya, rotáció, insoláció | Megvilágított gömb, terminátorral | ✅ **Vizuálisan megerősítve** |
| **M4** | **Geológia + domborzat** | Lemezek, kéreg, elevation, tengerszint | Kontinensek, óceánok, árnyékolt hegyek | ✅ **Vizuálisan megerősítve** — `TEST-EARTH-001` numerikusan is teljesítve (249/249 teszt). A §13.2 fraktál-zaj korábbi hiánya (Voronoi-cella-szerű szabályosság) ✅ **pótolva** (ND-31, térben koherens fBm). A lemez-Voronoi-HATÁR geometrikussága ✅ **pótolva** (ND-36, domain warping). A "part túl magas" panasz első köre ND-37-tel javult; az új precíz km-lépték által feltárt falszerű vegyes kéregperem ✅ **Core-oldalon javítva** (ND-88/ND-90: 1:1 relief, folytonos `0.005` gap-sáv, 1000 m uplift-plafon, `.worldpkg` v2). Az új állapot élő Unity-vizuális ellenőrzése hátra. |
| **M5** | **Klíma** | Hőmérséklet, szél, nedvesség, csapadék | Biome-színek, hó, jégsapkák | ✅ **Vizuálisan megerősítve** (hőmérséklet+biome-sáv, 138/138 teszt); szél/nedvesség/csapadék halasztva (ld. hatókör) |
| M6 | Atmoszféra-render | Rayleigh-szórás, felhők, ciklonok | Planet nézet lényegében kész | Referenciakép 2 szintjén ~80% |
| **M7** | **Hidrológia + erózió** | Folyók, tavak, gleccser, A1 eróziós pass | Folyók a kontinensnézeten, mikro-vízrajz | ✅ **Vizuálisan megerősítve** ("folyók hegyből tengerbe futnak" strukturálisan bizonyítva, 146/146 teszt); tavak/jég/erózió halasztva |
| **M8** | **Features + panelek** | Szegmentálás, névadás, aggregált metrikák | World/Continent/Region panelek élesben | Kontinens/régió-szegmentálás + névgenerálás + aggregált metrikák (Area, BiomeDiversity, RiverMouthCount) ✅ **numerikusan kész** (190/190 teszt); a legtöbb panel-mező (Habitability, Coastal complexity stb.) halasztva; vizuális render hátra |
| M9 | Continent + Region nézet | Magas LOD, displacement, kamera-átmenetek | Referenciakép 1, 3, 4 szintje | Adaptív terep/víz-LOD, chunk-csomagolás, több frame-es upload, nézetszint/FlyTo és pontmintás kamerakorlát implementált. A korai élesség és sima zoom nem elfogadott. [Újraértékelt, súlyozott állapot: kb. 61%](reviews/m9-progress-audit-2026-09-12.md), nem az előző becsléssel összevethető mérés. |
| **M10** | **Deep time** | Lemezmozgás, erózió, eljegesedés, tengerszint | Az időcsúszka él | Lemezmozgás ✅ **vizuálisan megerősítve** (163/163 teszt, TimestepInvariance egzakt; `deepTimeMyr` Unity idő-csúszka - domborzat ÉS biome egyaránt elmozdul, felhasználó által tesztelve). Dinamikus (térfogat-megmaradás alapú) tengerszint ✅ **numerikusan kész** (ND-38). Az ND-90 a deep-time elevációs útba is bekötötte a folytonos vegyes kéregátmenetet és az 1000 m uplift-plafont; a teljes Python/KAT-lánc és 384/384 Core-teszt zöld, élő peremellenőrzés hátra. Az erózió/eljegesedés teljes spec-lefedettsége továbbra is halasztott. |
| **M11** | **Események** | Becsapódás, vulkán, rift, split/merge | Kráterek, kitörések láthatók | Becsapódás ✅ **vizuálisan megerősítve**; szuper-vulkán (VEI8) ✅ **numerikusan kész** (220/220 teszt, ND-29); rift/split-merge halasztva — strukturálisan más (folytonos, nem diszkrét esemény-alapú) modellt igényelnek, önálló tervezést érdemelnek |
| **M12** | **Perzisztencia + CLI** | Checkpoint, .worldpkg, state hash | — | `WorldStateHash`, definíció-checkpoint és CLI verify implementált. ND-108 / A6: közös generátorverzió, `.worldpkg` v3 és app-kompatibilitási kapu ✅ **kész**. A teljes app-állapotszerializáló és session/UI-bekötés továbbra is C3/D4; az A6 lezárása nem teljes M12-átvétel. |
| M13 | Polish | Volumetrikus felhő, AO, víz-shader, színkalibráció | Végleges látvány | Vizuális acceptance (spec §73). Víz-shader: a `PlanetGridMesh` mostantól a tile-rács `field`/`isOceanField`/`seaLevel` adatából épít egy külön vízfelszín-réteget (a kalibrált tengerszint sugaránál, mélységfüggő, telítődő szín-görbével) a korábbi, tile-rácstól független flat kék primitív gömb helyett; a tengerfenék is finom fényesség-variációt kapott a meglévő fraktál-zajból. Fresnel/csillanás, felhő, AO, végleges színkalibráció továbbra is halasztva — vizuális ellenőrzés Unityben hátra. |

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
T_transport + T_ocean - T_altitude + T_weather + T_cycle`) a későbbi
ND-126b kalibráció óta meridionális hőszállítás-proxyt is tartalmaz. M5
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
T_transport = 40 K * sin⁴(latitude)
T = T_eq + T_greenhouse + T_transport - T_altitude
```

`sigma` a Stefan-Boltzmann állandó, `A` egyelőre fix albedo-közelítés
(óceán/szárazföld szerint, később a §29 teljes albedo-modell), `Γ` a
lapse rate (~6.5 °C/km, Föld-szerű illusztrációhoz).

**2026-09-22-i kalibráció (ND-126b):** a kanonikus világ hideg
szárazföld-aránya 40,71%-ról 16,91%-ra csökkent; a termikus szélkomponens
30 m/s-os sima `tanh`-korlátot kapott. A csapadék-percentilisek vizuális
elfogadása ettől külön, a `todo2.md` B4 tétele.

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
  **ND-127 (2026-09-21) óta a vízgyűjtő nem nyersen a régió**: a torkolat
  óceán-tile-ja szerinti kulcsolás a lefolyás azonosítója, nem földrajzi
  egységé (level 5-ön a szárazföld 50%-a kimaradt a panelről, a régiók
  35,7%-a térben szétesett), ezért a panel-régió a vízgyűjtő-komponensek
  összevonása a szárazföld 3%-áig — `MergeWatershedsIntoRegions`.
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

### 8.3 Aggregált metrikák — kész

`src/WorldGen.Core/Features/FeatureMetrics.cs`: `AreaTiles` (tile-számlálás,
dokumentált közelítés — ld. modul-fejléc és ND-24), `BiomeDiversity`
(distinct biome-ok száma), `RiverMouthCount` (a régióba/kontinensbe eső
folyó-tile-ok, amik KÖZVETLENÜL óceánba folynak).

A folyó-tile kiválasztás (`FlowNetwork.SelectRiverTiles`) ennek
melléktermékeként a Core-ba került — korábban ez a logika csak a Unity
`PlanetGridMesh.cs`-ben (megjelenítési célra) létezett, duplikálva; most
mindkét hely (metrika-számítás ÉS render) ugyanazt a Core-függvényt hívja.

190/190 teszt zöld (181 korábbi + 9 új): Python-referenciával (18
kontinens/régió rekord) bitpontos egyezés (nincs transzcendens függvény
ebben a láncban), tisztaság, minden paraméter hat, élesetek (üres
tile-halmaz, nincs szárazföld).

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
eljegesedés (jégkorszak-ciklusok), lemez-születés/-halál (§16).

**Dinamikus, térfogat-megmaradás alapú tengerszint — MEGVALÓSULT (ND-38,
utólagos kiegészítés).** Eredetileg ez a lépés is halasztva volt (a
tengerszint a MINDENKORI elevation-mező percentilise maradt volna, mint
M4-ben, ami azt jelentette volna, hogy a víz-arány örökké pontosan
`targetWaterFraction` marad, függetlenül a domborzat változásától — ez
fizikailag hibás). A `t=0` állapotból számolt, rögzített víztérfogathoz
(`SeaLevelCalibration.ComputeFloodedVolumeProxy`) tartozó egyensúlyi
tengerszintet minden későbbi `t`-re fix (60) iterációjú bináris kereséssel
oldjuk vissza (`CalibrateSeaLevelByVolume`) — a `t=0` render bitre
változatlan marad (a régi percentilis-hívás fut tovább), `t>0`-nál viszont
a víz-arány ténylegesen elmozdul (mérve: 65%→41.8% 50 Myr alatt, →93.7%
250 Myr alatt). Részletek: `docs/04-decisions.md` ND-38.

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

## M11 — Következő, részletes terv (autonóm folytatás, "csináld magadtól")

### Cél

Az M10-hez hasonlóan a spec-hatókör (becsapódás, vulkán, rift, lemez-
hasadás/egyesülés) itt is túl széles egyetlen lépéshez — ld. ND-28
(`docs/04-decisions.md`) a hatókör-szűkítés indoklásáért.

### Hatókör (tudatosan szűkítve — ND-28)

**Benne van:** meteor-/üstökös-becsapódás (spec §22) — epoch-alapú
determinisztikus eseménygenerálás, hatványtörvény méreteloszlás
(nehéz-farkú: sok kicsi, kevés nagy), tranziens kráter átmérő+mélység
(Schmidt & Housen / Collins-Melosh-Marcus skálázás), a magasság-mezőn
tartósan alkalmazva (§22.4 "a height field tartósan módosul").

**Halasztva** (dokumentált, ND-28 részletezi): vulkán, rift, lemez-
hasadás/egyesülés (nincs numerikus alapjuk még); kráter `rimHeight` +
`ejectaRadius` (csak `diameter`+`depth` az MVP-ben).

**Új:** `PlanetConstants.RadiusMeters` (7420 km, a spec kanonikus
példa-bolygója) — az első valós fizikai bolygóméret-konstans a
Core-ban, a kráter méterben mért méretének a rács szögtartományára
váltásához. NEM oldja meg ND-19-et (Unity render-precízió) — az
M9-re marad.

**ND-27 osztálya kiterjesztve (nem új döntés):** a kráter-képlet
Math.Pow/Sin/Cos-t használ — ugyanaz a trigonometria-kockázat és M12
előtti lezárási határidő, mint a klímánál és a lemezmozgásnál.

### 11.1 Becsapódás — kész (numerikusan)

`src/WorldGen.Core/Events/ImpactCratering.cs` — `TryGenerateImpact`
epoch-index alapján (`EpochYears = 10 000`), forrásból ellenőrzött
fizikával:

- Gyakoriság: ρ(≥D) = 20·D^-2.4 [1/év], D méterben (NEO-becsapódási
  hatványtörvény), Bernoulli-közelítésben epochonként.
- Méret: Pareto-eloszlás (α=2.4), 1 km - 100 km tartomány (felső sapka
  dokumentáltan, nem újra-mintavétellel).
- Sebesség: egyenletes 15-25 km/s.
- Szög: P(θ) ∝ sin(2θ) (geometriai tény, zárt alakban invertálva).
- Kráter: `TransientCraterDiameter` (Schmidt & Housen / Collins-Melosh-
  Marcus), mélység = átmérő × 0.2 (1:5 arány).

173/173 teszt zöld (163 korábbi + 10 új): Python-referenciával 20 000
epoch-os "történelem" tolerancia-egyezés, tisztaság, minden paraméter
hat a kimenetre (méret/sebesség/szög mind növeli a krátert), plauzibilis
gyakoriság + nehéz-farkú méreteloszlás, élesetek.

**Hátra van:** a kráter tartós alkalmazása a magasság-mezőn (jelenleg
`TryGenerateImpact` önmagában áll, nincs bekötve a
`SeaLevelCalibration`/`ComputeElevationFieldAtTime` láncba) + Unity
vizuális megjelenítés (kráterek látszanak a domborzaton) — ez a
következő lépés, és a vizuális rész a te ellenőrzésedet igényli.

### 11.2 Javasolt sorrend

1. ~~`tools/reference/impacts_ref.py` — Python becsapódás-modell +
   verifikáció~~ ✅
2. ~~C# port (`ImpactCratering`) + tesztek~~ ✅
3. A kráterek bekötése a magasság-mezőbe (deep-time-integrált: adott
   `timeMyr`-ig lezajlott epoch-ok kráterei mind alkalmazva, additív
   mélyedésként a `ComputeElevationFieldAtTime` eredményén)
4. Unity render-kiegészítés + vizuális ellenőrzésed — ekkor jelentkezem

Ugyanaz a minta, mint eddig mindig: **referencia → verifikálás → C# → mérés**.

## M9 — Következő, részletes terv (autonóm folytatás, "csináld magadtól")

### Aktuális állapot — 2026-09-12-i becslési korrekció

**Aktuális Claude-visszaadás:** [kt_2_co2cl.md](../kt_2_co2cl.md).
A 20:23-as új log részben igazolja az ND-96–98 aktív runtime útját,
de a vége még finomítás közbeni állapot (64220 levél, pending=True),
a teljes kérés p50/p90 746/1073 ms. Nem kész vizuális/FPS-elfogadás;
M9 marad 61,25%. Az 5–11 órás maradék performance-sora újrabecslendő,
ha a mért worker-költséghez új architektúra szükséges. Ez az aktuális
pillanatkép felülírja a régi „nincs új log” állapotot.

**ND-98 folytatás:** [stabil vízkiválasztás újrahasználata](reviews/lod-water-reuse-nd98-2026-09-12.md),
18 új teszteset; viewer 373/373 Debug és Release. Az álló kamerás páros
vízpróba 74% idő-/65% allokációcsökkenést adott pontos fedésegyezéssel.
Mozgó nézetnél nincs gyors út. A teljes reakcióidő és élő elfogadás nincs
igazolva: súlyozott M9 továbbra is 61,25%, durva maradék 5–11 óra.

**ND-97 folytatás:** [metrikatároló, helyi feedback-revízió és összehasonlítás](reviews/lod-cache-allocation-nd97-2026-09-12.md).
Három további költségcsökkentés, változatlan kiválasztási célokkal; 15 új
.NET-eset. Páros offline tárolópróba: 52–61% kevesebb allokáció, 5–9%
kisebb cut-idő. Nem teljes FPS-/felzárkózási mérés. M9 marad 61,25%,
durva maradék 5–11 óra, mert a végső natív/vizuális kapuhoz nincs új adat.
A kézi próbát a felhasználó későbbre hagyta; a közös lista továbbra is érvényes.

**ND-96, aktuális összevont átadás:** [teljes implementációs csomag, mérés és végső próbalista](reviews/lod-final-batch-nd96-2026-09-12.md).
Checkpoint `99b3ac4`, majd kameramozgáskor megőrzött geometria-cache,
kész mesh visszacsatolása, egyszeres balance, közvetlen sarok-újrafelhasználás,
víz-cache/hiszterézis, 8/7 px korai minőség. A nyers mélység-proxy kísérlete
túlosztás miatt visszavonva; a nagyobb végső részletesség kimért többletmunkát
kér, teljes zoomgyorsulás nem igazolt. 732 .NET-eset részenként zöld,
viewer 340/340 Debug/Release; 4 új Editor-eset csak fordított. Natív,
vizuális és memória/FPS ellenőrzés egyben következik, nem köztes kapukban.
A korai minőségi csoport 25 → 50 pont: **súlyozott M9 61,25%, kb. 61%**.
Durva maradék **5–11 óra**, nem mért munkaidő; a részbecslés az auditban.
Ez felülírja a lentebbi ND-95/korábbi aktuális százalék- és órasorokat.

#### Korábbi állapotok (történeti pillanatképek)

**ND-95, három korrekció implementált:** [mérési óra, lépték érvényessége,
FlyTo helyi magassága](reviews/lod-navigation-measurement-batch-nd95-2026-09-12.md).
707/707 .NET PASS, viewer Release 315/315; 17 új Editor-eset csak fordított.
A víz/kamera/lépték korrekciós maradék 2–4 → 1–3 óra, teljes durva
maradék **11–23 óra**. A súlyozott 55%-os bázis marad: a korrekciók
nem helyettesítik az élő elfogadást. Ez felülírja a lentebbi korábbi
ráfordítás-pillanatképeket, a korai élesség/selection továbbra is halasztott.

A korábbi, sok bejegyzésben ismételt **60–70% / 8–20 óra nem volt
újraszámolt becslés**. Aktuális előrejelzésként visszavonva; az alábbi régi
bejegyzések történeti állapotok, nem újra felhasználható státuszsablonok.
[Tételes audit és rögzített súlyozás](reviews/m9-progress-audit-2026-09-12.md):
a szűkített M9 + jóváhagyott zoom-kiegészítések kész-definícióhoz viszonyított
állapota **kb. 55% (durva 50–60%-os sáv)**. Nem visszafejlődés: most először
explicit súlyozást használunk, az ND-k darabszáma helyett.

Az upload/csomagolás/víz/kamera implementáció tényleges előrelépés;
a korai részletesség, sima átmenet és teljes teljesítmény-elfogadás külön
nyitott kapu. A 12:51-es log aktív ND-91-et mutat, de minimum 25,29 egység
modellmagassággal, tehát nem tesztelte a 0,33-as közeli kamerakorlátot.
Az új, tételes hátralévő ráfordítás-alapbecslés kezdetben 14–28 munkaóra;
az ND-92 implementációs csomagja után 13–26, az ND-93/94 után **12–24 munkaóra**,
felhasználói várakozás nélkül; nem mért idő és nem vállalt határidő.
A részfeladatok lezárásával ezt a táblát frissítjük, nem a sávot másoljuk.

**ND-93/94, két feladat egyben:** korlátos inaktív renderchunk-cache,
saját runtime mesh-ek felszabadítása és két lépésre bontott terepstaging
implementált. Kilenc új .NET-eset; viewer 304/304 Debug/Release, teljes
solution teszt 696/696. Kilenc új Editor-eset csak fordított. A
[közös ellenőrzési lista](reviews/lod-resource-staging-batch-nd93-94-2026-09-12.md)
szerinti élő próba kell. A kb. 55%-os állapotbázis nem emelkedett: az
új részfeladatok nem zárják le a teljes reakcióidő- és minőségi kaput.

**ND-92:** a [részlegesen hibás upload utáni terep-/vízmaszk-helyreállítás](reviews/lod-mask-recovery-nd92-2026-09-12.md)
implementált és reprodukciós tesztekkel ellenőrzött. A teljes indexállapot
csak a hibaágban töltődik vissza; normál zoomköltség és élesség nem változik.
Ez a hibabiztonsági részfeladat lezárása, nem a fő vizuális kapu elfogadása.
Az új natív Editor-esetek futtatása még hátra van.

### Korábbi lépések és átadási pillanatképek

**2026-09-12, ND-91:** a tile/zoom sorrend következő önálló kapuja,
a [helyi felszínkövető kamerakorlát](reviews/lod-camera-clearance-nd91-2026-09-12.md)
implementált. 12 új .NET-eset; viewer-LOD 290/290 Debug/Release sikeres,
Unity-forrás és három új Editor-eset fordítva, élőben még nem futott.
Nem teljes mesh-ütközésvédelem vagy élességjavítás. A teljes solution
tesztfutás külön Core referenciaeltéréseket jelez a párhuzamos modellmunka
mellett; ezért nem minősítjük az egész projektet zöldnek. M9 durván 60–70%.

**2026-09-12, ND-89:** az ND-87 log alapján a következő
[maszk-előkészítési kapu](reviews/lod-prepared-mask-nd89-2026-09-12.md)
implementált: előkészítéskor nincs élő indexváltozás, a commit tulajdonos- és
revízióvédett. Hét célzott .NET-eset sikeres; az új natív indexbufferes
Editor-teszt csak fordított. Maszk CPU/natív bontás és új élő próba kell.
A 25,76 ms-os objektum-előkészítési tüske és a teljes zoomminőség nyitott;
M9 tartalmilag továbbra is durván 60–70%.

**2026-09-12, ND-87:** [terep-rendercél staging és publikációs részidők](reviews/lod-terrain-publication-nd87-2026-09-12.md)
implementált, új élő próbára vár. Négy új valódi Unity stage/publish/discard
teszteset fordított, Editorban még nem futott. A kép minősége, a ~669 ms-os
kéréskésés és a teljes FPS-elfogadás nem lezárt; M9 tartalmilag 60–70%.

**ND-86 élő visszamérés:** az [új log](reviews/lod-auxiliary-upload-live-2026-09-12.md)
71 commitban igazolja az új utat. A vízpublikálás maximuma 0,50 ms, a
commité 7,88 ms, de a teljes kérés mediánja még 669 ms. A minőségi és
proxy-/geometriaeltérési backlog nem zárult le, teljes vizuális elfogadás
nincs. Következő upload-javaslat a tereppublikálás részletes felbontása;
most csak elemzés készült, M9 becslése marad 60–70%.

**2026-09-12, ND-86:** az [ND-85 log és a következő upload-kapu](reviews/lod-auxiliary-upload-nd86-2026-09-12.md)
alapján a víz-/határvonal-mesh is a tereppel közös előkészítési sorba került,
a vízadatok összefűzése a workerre. A végső váltás továbbra is közös;
külön terep-/vízmaszk- és publikálási időmérés készült. Öt új ütemezési
teszteset sikeres, az összes aktuális .NET-teszt 659/659, LOD Release 271/271.
A valódi vízadat-csomagolási Editor-teszt csak fordított, nem futtatott.
Élő ND-86 teljesítmény-/vizuális elfogadás kell; a korai zoompanasz halasztva.
M9 tartalmilag továbbra is durván 60–70%, nem teljes spec-lefedettség.

**2026-09-12, ND-85:** a [több frame-es terep-upload első kapuja](reviews/lod-staged-upload-nd85-2026-09-12.md)
implementált: láthatatlan tartalékok, 2 ms/64 mesh puha adagolás, külön
commit-frame, konfiguráció-/Build-megszakítás, 14 új ütemezési teszteset.
Az aux-feltöltés és a commit további bontása, az FPS-hatás és a teljes
vizuális elfogadás még nyitott. M9 tartalmilag továbbra is durván 60–70%.

**Aktuális felhasználói sorrend, ND-82 (2026-09-11):** az első zoomok
akadását/küszöbét backlogra halasztottuk. Következő önálló feladat a
[vízfelszín saját finomítása](reviews/water-lod-selection-nd82-2026-09-11.md).
Első kapuja implementált: külön víz-cut, költségkeret, teljes base-fedés és
illesztett geometriai terv, 24 új célzott teszttel. Teljes regresszió 617/617,
LOD Release 229/229, Unity-forrásfordítás 0 hiba az első kapunál.
**ND-83: a vízmaszk/szín/mesh-publikáció már renderbe kötve**, hét új
motorfüggetlen szerződésteszttel és egy külön Editor-maszkteszttel.
[Átadás és ellenőrzési kapu](reviews/water-lod-render-nd83-2026-09-11.md).
A vizuális és teljesítmény-elfogadás továbbra is élő Unity-próbára vár.
Ez nem a zoomhiba megoldása vagy új vizuális elfogadás; M9 továbbra is
durván 60–70%. A korábbi „következő prioritás” sorokat ez a döntés felülírja.

**ND-81 aktuális átadás (2026-09-11):** az ismételt metrikaszámítás
[pontos cache-e](reviews/lod-work-cache-nd81-2026-09-11.md) implementált,
a selection/balance és emit belső részidői naplózottak. Offline 40 páros
finomítási kör azonos eredményt ad; a kiválasztási lánc 27–37%-kal rövidebb
ebben a próbában. LOD 205/205 Debug/Release, Unity-forrásfordítás 0 hiba.
A pixelcél változatlan; élő gyorsulás/visual acceptance még nincs.
M9 tartalmilag továbbra is durván 60–70%; további munka és próbák 8–20 óra
(durva becslés, nem mérés). Következő lépés csak felhasználói próba után.

**ND-80 élő eredmény (2026-09-11):** a [logelemzés](reviews/lod-bounded-chunks-live-2026-09-11.md)
igazolja a 256-os chunkkorlátot és kedvezőbb feltöltési csúcsokat.
A háttérkérések ~0,54 s-os mediánja, 4–5 s-os állókamerás finomítása és
a késői élesedés megmaradt. Következő prioritás a cut/előállítás ismételt
munkája; vizuális elfogadás nincs, M9 továbbra is durván 60–70%.

**ND-80 átadva próbára (2026-09-11):** a megjelenítés átalakításának első
lépése a [256 levélre korlátozott hierarchikus chunk-csomagolás](reviews/lod-bounded-chunks-nd80-2026-09-11.md).
A pixelcél és a geometria változatlan; nem élességjavításként adjuk át.
Offline 104 állásban azonos fedés, LOD 190/190 Debug/Release; Unity-fordítás
0 hiba. Élő teljesítmény/visual acceptance kell. M9 tartalmilag továbbra
is durván 60–70%; upload és minőségi cél folytatása hátra.

**ND-79 aktuális kapu (2026-09-11):** az ND-78 élő próba után a korai
finomodás hiánya továbbra is igazolt. Közepesen kész állapotban ~9,9 px-es
statikus terep marad a 10 px-es küszöb miatt. A kisebb célok offline próbája
korábbi részletet, de súlyos geometria-/chunkszám-növekedést mutatott;
nem aktiváltuk őket. [Elemzés és javasolt előfeltételek](reviews/lod-onset-cost-analysis-nd79-2026-09-11.md).
Nincs új vizuális acceptance; M9 továbbra is durván 60–70%.

**ND-78 részjavítás (2026-09-11):** a megjelenítő által kizárt tengerfenék
már nem fogyaszt kiválasztási osztáskeretet. Offline azonos renderelt
terep mellett közepesen 11→8, mélyebben 14→13 hullám; élő próba hátra.
A teljes mélységű proxy-kísérlet visszavonva, mert tömeges túlosztást adott.
Nem lezárt élességjavítás; M9 60–70% marad.
[Részletes bizonyíték és korlátok](reviews/lod-early-ocean-exclusion-nd78-2026-09-11.md).

**ND-77 aktuális kapu (2026-09-11):** az ND-76 élő log gyorsabb reakciót,
de befejezett finomítás mellett 88,64 px-es dinamikus tereptile-t is mutat.
Az engedélyezett 1. lépés [tile-azonosító és megállási trace része](reviews/lod-terrain-decision-trace-nd77-2026-09-11.md)
implementált. Új próba kell a konkrét hibaforrás bizonyításához; terepjavítás
még nincs. LOD 161/161 Debug/Release, Unity-fordítás 0 hiba. M9 60–70% marad.

**ND-76 aktuális állapot (2026-09-11):** az ND-75 élő próba alapján elkészült
a [nézetfrustum/proxy-quad méretű, adagolt finomítás és teljes chunk emit-cache](reviews/lod-progressive-projected-nd76-2026-09-11.md).
A régi nézetű kérés kooperatívan megszakítható; álló kameránál a halasztott
finomítás folytatódik. A tiszta kiválasztási próba mélyen ~75%-kal kisebb
cutot adott azonos középső LOD mellett, közepes zoomnál viszont többlet is
lehet. Élő Unity-acceptance nincs; a tartalmi becslés továbbra is 60–70%.
A főszálas upload több frame-re bontása még nyitott, nem része a cache-fixnek.

**2026-09-11, aktuális korrekció:** a korábbi „kész” alfejezetek nem jelentik
a zoomminőség lezárását. A diagnózis alapján elkészült az
[első javítási csomag](reviews/lod-zoom-fixes-2026-09-11.md) (ND-69): hiteles
CPU-besorolás, parti védelem, nézetfrissítés és megőrzött budget-frontier.
Az [ND-70 második csomag](reviews/lod-zoom-fixes-phase2-2026-09-11.md) teljes
base-fedéscserét, közösél-feloldást és pozícióérzékeny chunk-frissítést ad.
445/445 .NET-teszt sikeres; két új Unity Mesh API-teszt lefordult, de még nem
futott. **Friss élő visszajelzés:** a zoom/visszazoom szépen működik, de kb.
13 görgetés után nincs érzékelt további élesedés; a gyorsítás most halasztva.
Az [ND-71 harmadik csomag](reviews/lod-zoom-fixes-phase3-2026-09-11.md)
élőben nem hozott érdemi látványjavulást, viszont súlyos lassulást okozott.
Az [ND-72 javítás](reviews/lod-zoom-regression-nd72-2026-09-11.md) az aktív
kiválasztást/morphot visszaállítja az ND-70 útra, egyetlen nadírpontot mérő
renderdiagnosztikával. Ez regressziójavítás, nem új felbontási áttörés.
Az új élő visszajelzés szerint valamivel jobb, de késői a finomodás kezdete.
Az [ND-73 hangolás](reviews/lod-zoom-onset-nd73-2026-09-11.md) korábbi első
base-osztást és 0,6→0,35 morph-range-et ad; a mélyebb cél és budget nem nő.
**Élő eredménye elutasítva.** A 18:39:49-es logban ráadásul még 0,600 volt
a morph-range a scene fájl 0,35 értéke helyett; ez nem cáfolja a panaszt.
Az [ND-74 első terep-proxy lépés](reviews/lod-terrain-proxy-nd74-2026-09-11.md)
Buildkor a kész sarokmintákból készít távolságközelítést, amit a cut és morph
közösen használ, új zoomkori Core-minták nélkül. Ez élő próbára átadott,
nem elfogadott megoldás; nem szigorú képernyőhiba-korlát. A további tételek
csak a felhasználó lépésenkénti ellenőrzése után következnek.
**Új ND-74 visszajelzés:** távol jó, közepes zoomnál nem érzékelt finomodás,
mélyebben ismét javul, de elégtelen. Emiatt a további javítások előtt az
[ND-75 mérőnapló](reviews/lod-drawn-size-log-nd75-2026-09-11.md) készült:
az aktuális kamerával vetített, ténylegesen feltöltött terrain/water mesh
17×9-es mélységtesztelt mintázása. Most csak diagnosztika, LOD-hangolás nélkül;
a felhasználó új próbája és annak értelmezése következik.
Az upload-időkeret, inkrementális emisszió, precíz km-lépték (backlog),
felszínkövető kamerakorlát, szigorú terepbounds és az új csomag élő
vizuális/performance elfogadása nyitott.
Tartalmilag súlyozott, korrigált durva M9-becslés: 60–70%; a zoomjavításokból és
validációból további 14–30 óra (nem mért ráfordítás; új lépték-UI nélkül).

### Cél

Kontinens- és régiónézet: a domborzat zoomolással ténylegesen finomodik
(nem marad a jelenlegi, fix LOD-szintű, pixeles/lépcsős felület), a
kamera-átmenet folyamatos (a "Zoom-átmenet folyamatos" M9 "Kész, ha"
kritérium). A felhasználói panasz konkrétan ez: közelítéskor a mostani
rács durvának látszik, mert egyetlen fix szinten, egyszerre az egész
gömbre épül, és nem sűrűsödik a kamera közelében.

### Kulcs-megállapítás (ez szűkíti drasztikusan a hatókört)

**A domborzat-mező LOD-FÜGGETLEN, és ezt már a jelenlegi kód is
kihasználja.** A `CrustElevation.BaseElevation` / `PlateBoundaryEffect.
ElevationWithBoundaryFromWarped` / `DomainWarp.WarpPosition` tiszta
függvények a `(worldSeed, x, y, z)` pozícióból — NEM a `level`-től vagy a
tile-mérettől. Ugyanaz a fizikai pont ugyanazt az elevációt adja, akár
level 5-ös, akár level 11-es tile részeként kérdezzük le. A viewer
`ComputeDisplacedRadius(x, y, z, ...)` pontosan ezt a pont-alapú
kiértékelést valósítja már meg — ez az a primitív, amit egy adaptív
renderer igényel.

Ebből következik: **az adaptív LOD NEM igényel új szimulációs
algoritmust, sem Python-referenciát, sem `src/WorldGen.Core` numerikus
munkát.** Ez a milestone kizárólag arról szól, hogy Unity-oldalon MELYIK
`(x,y,z)` pontokat és MILYEN SŰRŰN mintavételezzük/jelenítjük meg a
kamera függvényében. Emiatt a szokásos "referencia → verifikálás → C# →
mérés" ciklus itt NEM alkalmazandó a domborzatra (nincs új numerika); a
munka a viewer-ben (`PlanetGridMesh` és környéke) zajlik, és az
ellenőrzés túlnyomórészt vizuális/manuális (ld. lent), egy vékony,
unit-tesztelhető kvadfa-kiválasztási logikával kiegészítve.

### Hatókör (tudatosan szűkítve, a §51-52 + a teljes M9 tartalomhoz képest)

**Benne van:** adaptív, kamera-vezérelt kvadfa-LOD a domborzat-
geometriára; folyamatos zoom geomorphinggal (popping-mentesség); a
meglévő rétegek (víz-felszín, tile-határ, kráter-marker, folyó-highlight)
korrekt viselkedése változó LOD mellett.

**Halasztva** (dokumentált, nem hiányosság — mindegyik önálló munka):
- **Hierarchikus fraktál-részlet a bázisszint alatt (§52).** A jelenlegi
  mező már ad tetszőleges pontban választ, de a magas-frekvenciás
  "landolási" mikro-domborzat (§52 `H_L2, H_L3...` tagok, ND-18 erózió
  cél-LOD 12) új frekvenciasáv hozzáadását jelentené — ez a
  mező-tartalom bővítése, külön ciklus, nem a megjelenítési LOD dolga. A
  jelen lépés a MEGLÉVŐ mezőt jeleníti meg finomabban, nem tesz hozzá új
  frekvenciát.
- **Normal-map / bake sub-tile részlet.** A geomorphing kiegészíthető
  bakeolt normal-map réteggel (a LOD-független mezőből finomabb
  mintákkal sütve), de ez optimalizáció, nem a folytonos zoom feltétele.
- **Vékony folyó-vonalak (él-topológia).** A `PlanetGridMesh` fejléce
  maga jelzi, hogy a valódi vékony folyóvonalak "M9-nél indokoltak" — ez
  igaz, de önálló render-feature (a FlowNetwork él-topológiájából), nem a
  domborzat-finomodás része; a felhasználói panasz a terepre vonatkozik.
  A meglévő tile-alapú folyó-highlight a magasabb LOD-on automatikusan
  finomodik (ld. 9.3), a vékony-vonal upgrade halasztva.
- **Aszinkron/streaming háttérszálas építés Job System-mel.** Csak akkor,
  ha a profilozás (9.5) főszál-akadást mutat — ld. ND-40 és a kapcsolat
  az ND-39 "A" (Burst/Job) opcióval.

Ez ugyanaz a mintázat, mint M4/M5/M7/M10-nél: a milestone saját "Kész,
ha" kritériumát (folyamatos zoom-átmenet) elégítjük ki, nem a teljes
spec-tartalmat egyszerre.

### 9.1 Kvadfa-LOD kiválasztás

**Adatszerkezet.** Laponként egy kvadfa, egy durva bázis-csomópontból
(pl. level 2-3) a max-mélységig (régiónézet: level 11, ld. M2.3
LOD-tábla) lebontva. Minden csomópont egy `TileId` — a Morton-kódolás
miatt a szülő/gyerek reláció ingyen adódik (`TileId.Parent()` /
`TileId.Child(i)`, ld. M2.1), tehát a kvadfa a MEGLÉVŐ rács-sémára ül rá,
nem kell új koordináta-rendszer. A ténylegesen renderelt csomópontok
halmaza a fán átvágott "cut" (az aktív levelek), nem a teljes fa.

**Finomítási kritérium (képernyő-hiba / távolság-arány).** Egy csomópontot
FELBONTUNK (a 4 gyerekére), ha a kamera-középpont távolsága kisebb, mint
a csomópont befoglaló-sugarának egy konstans-szorosa:

```
felbontás, ha   d(kamera, tile_közép) < K_split · r_tile
összevonás, ha  d(kamera, tile_közép) > K_merge · r_tile
```

ahol `r_tile` a csomópont befoglaló sugara (fél-átló a gömbön) és
`K_merge = 1.5 · K_split` (hiszterézis — enélkül a küszöb két oldalán a
csomópont minden frame-ben oda-vissza pattogna: "thrashing"). Kiinduló
érték: `K_split ≈ 2.5` (a 9.5 profilozásban hangolandó a kívánt
képernyő-élhosszhoz). Ez ekvivalens egy egyszerű képernyő-tér
él-szögküszöbbel.

**2:1 kiegyensúlyozott (restricted) kvadfa.** A varratmentességhez
kikényszerítjük, hogy két él-szomszédos aktív levél között legfeljebb egy
szint különbség legyen: ha egy finomított csomópont él-szomszédja több
mint egy szinttel durvább, azt is kényszer-felbontjuk. Ehhez a MÁR
verifikált `TileNeighbors` táblát (M2.4) használjuk — nem kell új
szomszédság-matek. A durva/finom találkozásnál keletkező T-csomópontot
él-illesztéssel (stitching) oldjuk meg: a durva tile érintett élét a
finomabb szomszéd közös csúcsaihoz igazítjuk (ez a sarok-cache
koordináta-rendszerét használja újra, ld. 9.4).

**Futásidejű viselkedés — állásfoglalás: INKREMENTÁLIS frissítés, NEM
teljes rebuild.** A mostani `Build()` a teljes gömböt (6·n² tile)
egyetlen, a főszálat blokkoló hívásban építi — az ND-39 ezt level 8-ra
~780 millió Threefry-kiértékelésre mérte. Ehelyett:
- A kvadfa "cut"-ja állapotként megmarad frame-ek között.
- Csak akkor futunk (kamera-mozgás-küszöb felett vagy N frame-enként
  throttle-ölve), amikor a kamera érdemben mozdul; ekkor a jelenlegi
  cut-tól indulva alkalmazzuk a split/merge-et.
- **Csak azok a csomópontok épülnek újra (mesh-elődnek), amelyek LOD-ja
  ténylegesen változott** — sima zoomnál frame-enként néhány, nem a teljes
  gömb.

Ez a kulcs teljesítmény-nyereség: az adaptív rendszerben az EGYIDEJŰLEG
kiértékelt pontok száma a kamera látóterétől függ, nem a max-mélységtől —
nagyságrendekkel kevesebb, mint egy egyenletes magas-LOD gömb (a távoli
tile-ok durvák maradnak). A régiónézet (level 11, összesen 25M tile) így
válik egyáltalán megjeleníthetővé anélkül, hogy 25M tile-t egyszerre
kéne felépíteni.

### 9.2 Geomorphing (popping-mentesség)

**A probléma.** Split/merge pillanatában az újonnan megjelenő csúcsok
hirtelen a valódi (elevációval eltolt) pozíciójukba ugranának, ami eltér
attól, ahol a durvább tile lapján interpoláltan "voltak" — ez a látható
"pattanás" (popping).

**Megoldás — folytonos vertex-morph a kamera-távolságból.** Minden olyan
csúcsra, amely az L szinten JELENIK MEG (a szülő L-1 quadban nem
létezett — tehát él-felezőpont vagy a szülő-quad közepe):
- `p_fine` = a csúcs valódi, eltolt pozíciója (a mező az adott pontban).
- `p_coarse` = ahol a csúcs a SZÜLŐ quad lapján lenne — a szülő két
  megfelelő sarok-eltolt pozíciójának lineáris interpolációja (olcsó, 1
  lerp).
- `α ∈ [0,1]` morph-faktor a FOLYTONOS kamera-távolságból, a `K_split` és
  `K_merge` közötti sávra normálva.
- A megjelenített pozíció: `p = lerp(p_coarse, p_fine, α)`.

Mivel `α` a kamera-távolság folytonos függvénye, és a küszöböket úgy
választjuk, hogy az átmenet BEFEJEZŐDIK, mielőtt a diszkrét topológia-
váltás (split/merge) bekövetkezne, a geometria sosem ugrik — átcsúszik.
Ez a bevált CDLOD-jellegű geomorphing. A szín/normál olcsóbban kezelhető:
a geometriát morphingoljuk, a material-kategória (biome-szín) a
split-nél vált — ez elfogadható, mert a biome-színfoltok egy csúcshoz
képest nagyok; ha vizuálisan zavaró, a normál is ugyanígy blendelhető.

**Elvetett alternatíva:** tisztán képernyő-tér normal-map durva mesh-en
(új geometria nélkül). Elvetve elsődleges módszerként, mert
landolási/régió-léptéken a valódi domborzat sziluettje és parallaxisa
számít — normal-map nem ad egy hegynek sziluettet. Kiegészítőként
(sub-tile részlet) később bevonható (halasztva, ld. hatókör).

**Reprodukálhatóság (I1 a megjelenítési oldalon).** A morph-faktor
KIZÁRÓLAG a kamera-távolság és a (fix) küszöbök függvénye — nincs
frame-rátától függő akkumuláció. A csúcspozíciók a mező tiszta függvényei,
tehát oda-vissza zoomolás bitre ugyanoda tér vissza (nincs "eldriftelés",
nincs remegés). A hiszterézis CSAK a topológia-váltás IDŐZÍTÉSÉT
befolyásolja, a csúcspozíciókat nem. Kikötés: a split/merge döntés a
kamera-távolság és a tile tiszta függvénye legyen, ne halmozódó állapot —
így "ugyanaz a kamera-pozíció → ugyanaz a mesh" garantált. (Platformok
közötti BITPONTOSSÁG itt NEM követelmény: ez nem checkpointolt szimulációs
állapot, csak render — ugyanaz a besorolás, mint a viewer meglévő
`WaterDepthBucket` `Math.Exp`-jénél. A CLAUDE.md lebegőpontos-táblázata
értelmében a display-oldali `float`-aritmetika — távolság-arány, morph-
lerp, küszöbök — nem tartozik az I1 bitpontosság hatálya alá; a mögöttes
elevációt továbbra is a meglévő, ND-23/26/27/39 alá tartozó Core-lánc
adja, új transzcendens függvény a kritikus úton NINCS.)

### 9.3 Adatfolyam

Az adaptív renderer PONTOSAN ugyanazokat a Core-függvényeket hívja, mint
a mostani `Build()`, csak MÁS (finomabb, kamera-közeli) pontokra:
- `PlateGeneration.GenerateSeeds` + `PlateMotion.MovedSeeds` (időfüggő,
  `deepTimeMyr`) — világonként egyszer.
- Csomópontonként/csúcsonként a MÁR meglévő `ComputeDisplacedRadius`-lánc:
  `DomainWarp.WarpPosition` → `PlateGeneration.AssignPlate` →
  `PlateBoundaryEffect.ElevationWithBoundaryFromWarped` (+ `ImpactCratering.
  ElevationDelta`). Ez már pont-alapú és LOD-független — nincs új hívási
  minta.
- A csomópont-KÖZÉP biome-színéhez a meglévő `Temperature.
  TemperatureKelvin` + `BiomeClassification.Classify` lánc, az adott
  szinten kiszámolt közép-elevációval.

**Tengerszint-kalibráció — kritikus állásfoglalás.** A
`SeaLevelCalibration.CalibrateSeaLevel` a TELJES bolygó eleváció-
eloszlásának percentilisét igényli. Ezt **TILOS** az adaptív (változó-LOD)
ponthalmazból újraszámolni — akkor a tengerszint attól függne, hova néz a
kamera, és a partvonal remegne (sérti a §70.5 "a fő partvonal ne
változzon LOD-váltáskor" előírását ÉS az I1 display-konzisztencia
szellemét). Ezért: a tengerszint (és az ND-38 térfogat-cache) EGYSZER, a
FIX referencia-szinten (level 6, az ND-02 szerint) számolódik — ugyanaz a
`_lastSeaLevel` —, és az adaptív renderer konstans bemenetként kezeli. Ez
közvetlenül teljesíti a §70.5-öt, és a display-oldali visszhangja az
ND-02-nek ("a szimuláció bázis-LOD fix level 6, a LOD csak lekérdezésre/
renderre").

**Kapcsolat a `_lastField`/`_lastIsOcean`/`_lastBiomeOf`/`_lastSeaLevel`
gyorsítótárhoz (M8 panel-adatok).** Ezek referencia-szintű (level 6)
szótárak, a `ComputePanelData` ezekre épül — a panelek a világ
MAKROSTRUKTÚRÁJÁT írják le, nem a pillanatnyi zoomot. Ezért ezek
VÁLTOZATLANUL a fix referencia-szinten maradnak; az adaptív magas-LOD
adat egy KÜLÖN, tranziens, csomópont-szintű gyorsítótárban él (ld. 9.4),
NEM a `_lastField`-ben. Így tisztán szétválik "amit a panelek olvasnak"
(fix, teljes-világ, level 6) és "amit a kamera renderel" (adaptív,
részleges, level 11-ig). A `Build()` továbbra is lefuttatja a
referencia-szintű passzt (tengerszint + panel-cache), az adaptív mesh
erre a rögzített alapra épül.

### 9.4 Kapcsolódás a meglévő optimalizációkhoz (ND-39 "C")

**Sarok-cache.** Jelenleg `Dictionary<(int Face, uint CornerU, uint
CornerV), Vector3>`, egyetlen szinten (`n = 1<<level`), a `Build()`
élettartamára (utána eldobva). Adaptív rendszerben különböző régiók
különböző szinten vannak, így az egyszintű `(u,v)` kulcs önmagában nem
elég. Bővítés:
- A kulcs **`(face, level, cornerU, cornerV)`**-re bővül — egy adott szint
  lap-lokális egész rácspontjához kötve (bitre azonos bemenetet ad a
  tiszta `ToDisplacedVector3`-nak, mint ma, csak a szintet is
  megkülönbözteti). A 2:1 stitching a szomszédos csomópont közös
  él-csúcsait ezen a kulcson keresztül osztja meg.
- Az **élettartam** megváltozik: a per-`Build()` lokálisból **perzisztens,
  méret-korlátos (LRU) mezővé** lép elő, amely frame-ek között megmarad. A
  cut-on kívülre került, régen nem használt csomópontok kikerülnek
  (eviction). Ez adja az inkrementális frissítés fő nyereségét: panning/
  zoom közben a már kiszámolt sarkok újrahasznosulnak, nem számolódnak
  újra. (A lap-határokon átnyúló sarkok kezelése változatlan: ott a kód ma
  is laponként, függetlenül számolja ugyanazt a pontot — ez a viselkedés
  megmarad.)

**Warp-hoisting.** Változatlan — ez a `ComputeDisplacedRadius`/
`ElevationWithBoundaryFromWarped` belső, pontonkénti optimalizációja,
LOD-tól független. Csomópontonként/csúcsonként ugyanúgy alkalmazódik, és
mivel az adaptív LOD egyszerre sokkal kevesebb sarkot értékel ki, az
abszolút költsége csökken.

### 9.5 Teljesítmény és a meglévő rétegek

**Amortizált frame-költségvetés.** Ha egy nagy új régió gördül a látótérbe
(pl. gyors zoom level 7-ről 11-re), egyszerre sok csomópont igényelne
mesh-elést → frame-akadás. Kezelés: **frame-enként korlátozott számú
(budget) csomópont-finomítás**, a maradék a következő frame-ekre halasztva
(a durvább szint közben látható marad, geomorphinggal áthidalva). Mivel az
adaptív ponthalmaz eleve kicsi, ez a főszálon is elég. Aszinkron/Job-
alapú háttérépítés (a Core-lánc tiszta és szálbiztos, I2) CSAK akkor, ha a
profilozás akadást mutat — ld. ND-40 és a kapcsolat az ND-39 "A"-hoz.

**Meglévő rétegek változó LOD mellett:**
- **Tile-határ (`showBorders`).** Aktív-levelenként egy vonal-hurok, tehát
  automatikusan követi a LOD-ot. Mély zoomnál újra értelmessé válik
  (kevés, nagy tile tölti ki a képet); a meglévő kapcsoló megmarad.
- **Víz-felszín (`BuildWaterSurface`).** A víz továbbra is a FIX kalibrált
  tengerszint sugarán, sík negyszögként épül (9.3 szerint a tengerszint
  LOD-invariáns) — az adaptív tengerfenék-geometriához a víz-negyszögek is
  a levél-csomópontok élein illeszkednek, így a LOD-határon sem nyílik rés.
- **Kráter-markerek (`BuildCraterMarkers`).** Már rácsfelbontástól
  függetlenek (világ-pozíciós gömbök); a felszínre-ültetésük ugyanazt a
  `ComputeDisplacedRadius`-t használja, változatlanul. Mély LOD-on a
  kráter a mezőben (`ElevationDelta`) valódi mélyedésként is megjelenhet,
  ekkor a marker elhalványítható — jövőbeli finomítás (halasztva).
- **Folyó-highlight.** A tile-alapú színezés magasabb LOD-on finomabb
  (keskenyebb) folyó-sávot ad; a vékony él-vonalas upgrade halasztva.

### Kötelező tesztek / ellenőrzési szempontok

Ez Unity-oldali, nem `dotnet test`-elhető numerikus modul — de a **kvadfa
kiválasztási/kiegyensúlyozási logika TISZTA és unit-tesztelhető** (Unity
EditMode teszt vagy sima C#, mert csak `TileId`-ket és a `TileNeighbors`
Core-táblát használja):

| Teszt (automatizálható rész) | Elvárás |
|---|---|
| Restricted-balance invariáns | két él-szomszédos aktív levél között ≤ 1 szint különbség — kivétel nélkül, minden kamera-pozícióra |
| Hiszterézis / nincs oszcilláció | monoton kamera-út mellett egy csomópont nem vált oda-vissza (K_merge > K_split garantálja) |
| Determinizmus (reprodukálhatóság) | ugyanaz a kamera-pozíció → bitre ugyanaz a cut és ugyanazok a csúcspozíciók (tiszta függvény, nincs halmozott állapot) |
| Cut lefedettség / nincs rés | az aktív levelek uniója hézag/átfedés nélkül lefedi a gömböt (a NoGaps M2-teszt adaptív analógja) |

Vizuális/manuális ellenőrzés (a te szemeddel, ez a milestone lényege):

1. **Varratmentesség a LOD-határon** — durva/finom találkozásnál nincs
   repedés/rés (restricted quadtree + stitching működik).
2. **Nincs pattogás zoom közben** — egy kiszemelt hegyet/csúcsot figyelve
   a geometria CSÚSZIK, nem ugrik (geomorphing működik).
3. **Reprodukálhatóság** — beközelítés egy régióra, kizoomolás, majd újra
   be: ugyanaz a geometria, nincs remegés/drift.
4. **Teljesítmény** — zoom közben a frame-idő interaktív marad (nincs a
   mostani level-8 egészgömbös, több másodperces blokkoló build).
5. **Partvonal-invariancia (§70.5)** — a kontinens fő partvonala NEM
   tolódik el zoomoláskor (a tengerszint fix referencia-szintű). Bolygó-
   vs. kontinensnézet: azonos makro-alak.
6. **Rétegek együttállása** — víz-felszín illeszkedik a LOD-határon;
   kráter-markerek a felszínen ülnek; határvonalak követik a LOD-ot.

### Javasolt sorrend

1. **Kvadfa-adatszerkezet + kiválasztási logika** (9.1) a `TileId.Parent()/
   Child()`-re és a `TileNeighbors`-ra építve, restricted-balance-szel —
   ELŐBB unit-tesztelve (kiválasztás, balance, hiszterézis, reprodukálás),
   render nélkül.
2. **Inkrementális mesh-frissítés** — a meglévő fix, egészgömbös
   `Build()` helyett a cut-alapú, csak-a-változott-csomópont építés; a
   sarok-cache perzisztens LRU-vá emelése (9.4). A referencia-szintű
   tengerszint/panel-passz megtartva (9.3).
3. **Geomorphing** (9.2) — a csúcs-morph a folytonos kamera-távolságból,
   `p_coarse` a szülő-quad interpolációjából.
4. **Teljesítmény-finomhangolás/profiling** (9.5) — `K_split` hangolása,
   frame-költségvetés, és döntés arról, kell-e egyáltalán Job/Burst az
   interaktivitáshoz (ND-40).

Mivel ez tisztán megjelenítési munka a LOD-független mezőn, itt NEM fut a
"Python-referencia → C#" ciklus a domborzatra (nincs új numerika); a
verifikáció a fenti tiszta kvadfa-tesztek + a vizuális ellenőrzésed. A
vizuális lépéseknél jelentkezem.

### Állapot: 1-3. lépés implementálva, 4. (élő hangolás) hátra

**1. Kvadfa-adatszerkezet + kiválasztás + restricted-balance — kész.**
`unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/AdaptiveQuadTree.cs`, önálló
`WorldGen.Viewer.Lod` assembly (nincs UnityEngine-referenciája — a
`noEngineReferences=true` asmdef-beállítás kikényszeríti), a `WorldGen.Core`
`TileId`/`TileNeighbors`/`TileGeometry`-re építve. `SelectCut`/`EnforceRestrictedBalance`
top-down bejárással, K_split/K_merge hiszterézissel. **A hiszterézis és a
"ugyanaz a kamera-pozíció → ugyanaz a cut" reprodukálhatósági követelmény
látszólagos ellentmondását** dokumentáltan úgy oldottuk fel, hogy az előző
keret cut-ja EXPLICIT, hívó által átadott paraméter (nem rejtett állapot) —
részletek a modul XML-doksijában. Unit-tesztek:
`Assets/Tests/EditMode/Lod/AdaptiveQuadTreeTests.cs` (2:1 egyensúly,
hiszterézis/nincs oszcilláció, determinizmus hideg indítással és
előzménnyel, teljes gömb-lefedettség/nincs hézag-átfedés kis level-
tartományon kimerítően ellenőrizve).

**2. Inkrementális (cut-alapú) mesh-építés + perzisztens LRU sarok-cache — kész.**
A `PlanetGridMesh.Build()` a referencia-szintű (fix `level`) passzt
VÁLTOZATLANUL futtatja (tengerszint + panel-cache, ld. 9.3), utána — ha
`useAdaptiveLod` (alapból be) — a kamera pozíciójából számolt cut-ot épít
mesh-hé (`RebuildAdaptiveMesh`/`EmitAdaptiveTile`), NEM a teljes gömböt egy
fix, magas szinten. A per-tile elevation/hőmérséklet/biome/óceán pontszerűen,
a levél saját szintjén számolódik (`ComputeElevationAtPoint`, a régi
`ComputeDisplacedRadius`-ból kiemelve) — ugyanazok a Core-függvények, mint a
fix útvonalon. `Update()` csak akkor számol újra cut-ot, ha a kamera egy
küszöbnél (`adaptiveCameraMoveThreshold`) többet mozdult. A sarok-cache
kulcsa `(face, level, cornerU, cornerV)`-re bővült és perzisztens LRU-vá vált
(`cornerCacheMaxSize`-ig), ahogy a §9.4 kéri.

**3. Geomorphing — kész, de a szokásostól ELTÉRŐ ütemezéssel (dokumentált
kompromisszum).** A §9.2 eredeti elképzelése a SZÜLŐ csomópont saját belső
felosztásán belüli, FRAME-enkénti (a tényleges split ELŐTT befejeződő)
morphingot ír le — ez a szülő quad-ját intra-node résztesszelációval kellene
felruházza, ami jelentősen nagyobb, kockázatosabb átalakítás lett volna élő
vizuális hangolás nélkül. Ehelyett: a gyerek csomópontok a SPLIT
PILLANATÁBAN pontosan a szülő bilineárisan interpolált felületén jelennek
meg (`alpha=0`, pozíció-folytonos, nincs pop), majd a KÖVETKEZŐ
újraszámolásokkal (ahogy a kamera tovább közelít) fokozatosan morphol a
valódi, finom pozícióra (`alpha→1`) — tehát az ütemezés az
`adaptiveCameraMoveThreshold` szerinti újraépítésekhez kötött, NEM
független, per-frame folytonos. Ez a pop-mentességet valóban biztosítja,
de a morph simasága a mozgás-küszöb finomságától függ — élő teszttel
hangolható/finomítható, ha szükséges.

**4. Teljesítmény-finomhangolás (9.5) — HÁTRA, ehhez a te élő Unity-
munkameneted kell.** Ami MEGVAN: időzítési diagnosztika
(`adaptiveRebuildWarningMs`, `Debug.LogWarning` ha egy újraépítés túllépi).
Ami NINCS MEG (szándékosan, ld. a §9.5 saját szövege — "csak akkor, ha a
profilozás főszál-akadást mutat"): frame-költségvetés-alapú, több frame-re
elosztott csomópont-finomítás nagy hirtelen cut-ugrásnál, és Job/Burst-alapú
aszinkron építés. Ezek bevezetése éles profilozási adatot igényelne, amit
csak a Unity Editorban, a te kezedben lehet megszerezni.

**Kamera-hatótáv korrekció (nem szerepelt az eredeti tervben, de szükséges
volt).** A `PlanetOrbitCamera.minDistance` régi értéke (120, `surfaceRadius`
100 mellett) SOHA nem engedte a kamerát elég közel ahhoz, hogy akár csak a
level 5-6-os LOD aktiválódjon (mért: level 6-hoz ~5 egységnyi magasság kell
a felszín felett, a régi minimum ~20 volt) — enélkül az egész adaptív
rendszer hatása láthatatlan maradt volna. Csökkentve 100.1-re. **Ha a
`PlanetView.unity` jelenetben ez az érték már felül van írva az
Inspectorban, ott is kézzel csökkentendő** — a script-beli alapérték nem
írja felül a jelenetbe mentett értéket.

**Verifikációs módszer, amit ez a lépés (élő Unity Editor hiányában)
használt:** a kvadfa-logikát (UnityEngine-független) egy különálló .NET
konzolos harness ÉS a hozzá tartozó `AdaptiveQuadTreeTests` NUnit-teszt
ténylegesen LEFUTTATVA (nem csak lefordítva) igazolta helyesnek; a teljes
`PlanetGridMesh.cs`/`PlanetOrbitCamera.cs` Unity-integrációt egy offline
csproj a valódi `UnityEngine.*.dll` referenciákkal fordította hibátlanra,
ÉS a felhasználó saját, live Unity Editor-munkamenete a szerkesztés közben
magától újrafordította a teljes projektet hiba nélkül (`Editor.log`
ellenőrizve, `LogAssemblyErrors (0ms)`, nincs `error CS`). Élő vizuális/
interaktív ellenőrzés (varratmentesség, pattogásmentesség, tényleges
frame-idő) NEM történt meg — ez a te következő lépésed.

### Komplexitás-becslés (durva, nem mért)

Nagyságrendi becslés, NEM idő-naplózott tény: a kvadfa + kiválasztás +
balance ~8-14 óra, az inkrementális építés + perzisztens sarok-cache
~10-16 óra, a geomorphing ~6-10 óra, profilozás/hangolás ~6-10 óra —
összesen **durván 30-50 óra** sávban, kizárólag viewer-oldalon
(`src/WorldGen.Core` érintetlen). A sáv felső vége akkor, ha a
profilozás után mégis Job/Burst-alapú aszinkron építés kell (ND-40) — az
alsó vége, ha a főszálas, amortizált inkrementális frissítés elég.
