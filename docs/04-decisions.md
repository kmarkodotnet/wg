# Döntési nyilvántartás

Minden architekturális döntés egy ND-számot kap. A nyitottakat **ne oldd meg
csendben** implementáció közben — ha egy kérdés eldöntésre szorul, vagy döntsd el
explicit és írd ide, vagy vedd fel új ND-ként.

## Lezárt döntések

| ID | Kérdés | Döntés | Indok |
|---|---|---|---|
| **ND-07** | A fotorealisztikus render része-e a projektnek? | **Igen, M2-től folyamatosan** | A kép a generálás elsődleges kimenete (I3). Ha a render a végére kerülne, 8 milestone-nyi hibás irány derülne ki későn. |
| **ND-23a** | Egységvektor-mintavétel a gömbön | **Elutasításos módszer**, csak `Math.Sqrt`-tel | Bitpontos, mert az IEEE-754 a `sqrt`-re korrekt kerekítést ír elő. Mérve: 1.903 átlagos iteráció (elmélet: 6/π = 1.910). |

## Nyitott döntések

### ND-01 — Technológiai stack ⚠️ BLOKKOLÓ

| Opció | Fő előny | Fő hátrány |
|---|---|---|
| **Unity 6 + HDRP** | `PhysicallyBasedSky` kész bolygó-atmoszférát ad; Burst közel Rust-szintű perf | Nincs double precision → floating origin kézzel; Burst `FloatMode` csapda; licenc-kiszámíthatóság |
| **Godot 4 + C#** | `precision=double` build; MIT licenc | Atmoszféra, felhő, víz mind saját shader |
| **Rust + wgpu** | Legjobb determinizmus és perf | Adatsűrű UI-ra gyenge; lassabb fejlesztés |

Részletes elemzés: `docs/03-unity-hdrp-evaluation.md`.

**Amíg nyitott:** a `src/` netstandard2.1 + C# 9 marad, motor-referencia nélkül.
A döntés így visszafordítható — rossz esetben a viewer cserélődik, a mag nem.

### ND-23b — Transzcendens függvények a kritikus úton

`Math.Log`, `Math.Exp`, `Math.Sin`, `Math.Cos`, `Math.Pow` nem garantáltan
bitre azonos platformok között. A `SampleGaussianUnsafe` emiatt `Unsafe` jelölésű.

| Opció | Előny | Hátrány |
|---|---|---|
| Saját polinomiális implementáció, verziózva | Teljes kontroll, bitpontos | Meg kell írni és validálni |
| Előre számolt inverz-CDF tábla + lineáris interpoláció | Csak összeadás/szorzás | Korlátozott pontosság |
| Elfogadni a kockázatot | Nincs munka | I1 sérül |

**Sürgősség:** M4 (tektonika) előtt kell dönteni. A Gauss-eloszlás a lemez-
sebességeknél és az esemény-magnitúdóknál jön elő.

### ND-24 — Cubed sphere vetítés: transzcendens függvény + a területarány valós viselkedése ⚠️ BLOKKOLÓ (M2)

A §2.2 (`docs/05-milestones.md`) szerinti egyenszögű vetítés
`s = tan(u * pi/4)`-et használ a tile-területek kiegyenlítésére. Két külön
kérdés merült fel, mindkettőt a `tools/reference/cubed_sphere_ref.py`
referencia-méréssel validáltuk (nem emlékezetből, ld. CLAUDE.md munkamódszer):

**1. A `tan` transzcendens (ND-23b kockázati osztály).** Nem garantáltan
bitpontos platformok között. Kérdés: a `TileId -> 3D pozíció` leképezés
**konstrukciós** (egyszer kiszámolva, verzióhoz kötve, hash-elve) vagy
**szimulációs** (minden betöltésnél újraszámolt, ahol a platformfüggő eltérés
összeadódhat, pl. inszoláció-számításnál) számításnak minősül?

**2. A területarány mért értéke ELTÉR a dokumentált "~1.3"-tól, és
LOD-szinttől függ — ez korábban nem volt mérve, csak emlékezetből idézve:**

| Level | n×n (lap) | Mért max/min arány |
|---|---|---|
| 5 | 32×32 | 1.3795 |
| 6 | 64×64 (klíma alapszint) | 1.3969 |
| 7 | 128×128 (bolygónézet render) | **1.4055** |
| 9 | 512×512 (kontinensnézet) | 1.4120 |
| 11 | 2048×2048 (régiónézet) | 1.4137 |
| ∞ (kontinuum-határérték) | — | → **√2 ≈ 1.41421** |

Az arány a felbontással monoton nő, és **√2-höz tart** — ez a `tan`-warp
ismert tulajdonsága (a lap-közép és a lap-sarok Jacobi-determinánsának
aránya). A `docs/05-milestones.md` `AreaDistribution` tesztje `< 1.4`-et ír
elő — ez level 5-6-nál épphogy teljesül (1.38-1.40, minimális tartalékkal),
de **level 7-től ténylegesen elbukik** a jelenlegi vetítéssel, vagyis pont a
render- és a magasabb LOD-szinteken, ahol a leginkább számít.

| Opció | Előny | Hátrány |
|---|---|---|
| **A: `AreaDistribution` küszöb emelése ~1.42-1.45-re**, a mért kontinuum-határértékhez igazítva | Legkevesebb munka; a jelenlegi vetítés marad | A küszöb "post-hoc" a mért eredményhez igazodik, nem előre rögzített cél — gyengébb minőségi garancia |
| **B: A tesztet csak a tényleges szimuláció-alapszintre (level 6) kötni**, magasabb LOD-okra nem terjeszteni ki | A dokumentált ~1.3-1.4 tartomány ott tartható | A render-szinteken (7+) a torzítás nem ellenőrzött, pedig ott vizuálisan jobban látszik |
| **C: Jobb vetítés keresése** (pl. a COBE-féle kvadrilaterizált gömb-leképezés, vagy iteratív egyenlő-területű korrekció a `tan`-warp fölött) | Valódi javulás minden LOD-on | Több munka, új referencia-implementáció és -mérés kell, a `tan` transzcendens-kockázata (1. pont) is megmarad vagy nő |
| **D: A pozíció konstrukciós (baked) kezelése** (a `tan`-kockázatra, 1. pont) — a pozíciókat egyszer számoljuk, hash-eljük, verzióhoz kötjük; a szimuláció ebből olvas, nem újraszámol | Kiiktatja a futásidejű platform-eltérés kockázatát | A world package mérete nő a pozíció-táblával |

**Javaslat:** D-t (baked pozíció) elfogadásra javaslom az 1. kérdésre — ez
nem tárgya vitának, egyértelműen jobb, mint futásidőben újraszámolni. A 2.
kérdésre (a tényleges arány) **explicit felhasználói döntés kell** — ez nem
konstrukciós apróság, hanem a render-minőség és a teszt-szigorúság közötti
kompromisszum, amit nem szabad csendben eldönteni.

**Sürgősség:** M2-ben, a `TileId`/Morton-implementáció előtt el kell dönteni,
mert a választott vetítés hatással van a szomszédsági logikára is.

### A többi nyitott döntés

| ID | Kérdés | Javaslat | Mikor |
|---|---|---|---|
| ND-02 | Szimuláció bázis-LOD: fix vagy adaptív | Fix level 6; LOD csak lekérdezésre/renderre | M2 |
| ND-03 | Köztes időpont interpolációja | Engedett, HUD-on jelölve | M10 |
| ND-04 | Timestep-invariancia toleranciái | `docs/01-architecture.md` §9 kiindulásnak, M10 után revideálni | M10 |
| ND-05 | Régiószegmentálás algoritmusa | Hibrid: vízgyűjtő + biome-klaszter + domborzati törés | M8 |
| ND-06 | Névgenerálás módszere | Szótag-templétek + hangulati készlet a feature tulajdonságaiból | M8 |
| ND-08 | Óceáni áramlatok | 1.0-ban egyszerűsített gyre-modell M6-tól (a ciklonokhoz kell) | M6 |
| ND-09 | Ordinális kvantálás referencia-eloszlása | Előre kalibrált, ~1000 világból, verziózva | M8 |
| ND-10 | Biome-küszöbök bolygóparaméterrel skálázva? | Nem az 1.0-ban; fix Föld-küszöbök, de konfigban | M5 |
| ND-11 | Feature-identitás perzisztálása | Checkpointolt, a többi layerrel | M8 |
| ND-12 | Kettőscsillag: kettős árnyék renderelése | Igen, 2 fényforrás támogatása | M3 |
| ND-13 | Neurális textúra-réteg | Csak M13 után; előbb A1–A5 (olcsóbb, többet ad) | M13 |
| ND-14 | Neurális determinizmus | Bake + tartalom-hash a package-be | ND-13-mal |
| ND-15 | Neurális modell forrása | Kész alapmodell + ControlNet | ND-13 után |
| ND-16 | Neurális sütés hatóköre | Progresszív, zoomra — a teljes level 12 nem fér el | ND-13-mal |
| ND-17 | Denoise strength plafon | 0.35, elevation-korrelációs teszttel | ND-13 után |
| ND-18 | Erózió cél-LOD | 12 | M7 |
| ND-19 | Floating origin (ha Unity) | Kamera-központú eltolás + logaritmikus depth | M2, ha Unity |
| ND-20 | Burst `FloatMode.Strict` kikényszerítése (ha Unity) | Roslyn analyzer vagy CI-szkript | M1, ha Unity |
| ND-21 | HDRP volumetrikus felhő űrből (ha Unity) | Prototípussal ellenőrizni; fallback saját shader | M2, ha Unity |
| ND-22 | Core assembly-izoláció | netstandard2.1, nulla motor-referencia | **Érvényben** |
