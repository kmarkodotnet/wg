# Döntési nyilvántartás

Minden architekturális döntés egy ND-számot kap. A nyitottakat **ne oldd meg
csendben** implementáció közben — ha egy kérdés eldöntésre szorul, vagy döntsd el
explicit és írd ide, vagy vedd fel új ND-ként.

## Lezárt döntések

| ID | Kérdés | Döntés | Indok |
|---|---|---|---|
| **ND-07** | A fotorealisztikus render része-e a projektnek? | **Igen, M2-től folyamatosan** | A kép a generálás elsődleges kimenete (I3). Ha a render a végére kerülne, 8 milestone-nyi hibás irány derülne ki későn. |
| **ND-23a** | Egységvektor-mintavétel a gömbön | **Elutasításos módszer**, csak `Math.Sqrt`-tel | Bitpontos, mert az IEEE-754 a `sqrt`-re korrekt kerekítést ír elő. Mérve: 1.903 átlagos iteráció (elmélet: 6/π = 1.910). |

### ND-01 — Technológiai stack: Unity 6 + HDRP

Részletes elemzés: `docs/03-unity-hdrp-evaluation.md`. Háromutas mérlegelés
(Unity 6 + HDRP / Godot 4 + C# / Rust + wgpu) — a döntő érv: a HDRP kész,
fizikailag megalapozott atmoszféra- (`PhysicallyBasedSky`), felhő- és
víz-rendszere reálisan 6-10 hét renderelési munkát spórol, és a Burst
compiler a szimulációs oldalon is közel Rust-szintű teljesítményt ad — a
projekt két legnagyobb technikai kockázatát csökkenti egyszerre. A hiányzó
double precision (floating origin kézzel) és a Burst determinizmus-csapdája
valós, kezelt kockázat, nem elutasítási ok.

**Döntés 4 feltétellel** (mind érvényben, az utolsó 3 új ND-ként nyitva):
1. A `src/WorldGen.Core` és minden szimulációs modul motorfüggetlen marad
   (netstandard2.1, nulla Unity-referencia) — **ez már érvényben van**
   (ND-22), és a Unity-projekt (`unity/WorldGenViewer/`) ezt egy helyi
   Unity package-ként (forrás szerint, nem másolva) hivatkozza.
2. `Unity.Mathematics` + `[BurstCompile(FloatMode = FloatMode.Strict)]`
   kötelező minden szimulációs kódon, CI-ellenőrzéssel → **ND-20**.
3. A floating origin stratégia M2-ben megtervezve/megvalósítva → **ND-19**.
4. A HDRP volumetrikus felhő űrből-nézeti működése M2-ben prototípussal
   ellenőrizve → **ND-21**.

Ha az 1. feltétel bármikor sérülne, a döntés visszafordítható: a viewer
cserélődik, a mag nem.

### ND-24 — Cubed sphere vetítés: transzcendens függvény + a területarány valós viselkedése

A §2.2 (`docs/05-milestones.md`) szerinti egyenszögű vetítés
`s = tan(u * pi/4)`-et használ a tile-területek kiegyenlítésére. A
`tools/reference/cubed_sphere_ref.py` referencia-méréssel validáltuk (nem
emlékezetből, ld. CLAUDE.md munkamódszer) — a mért érték eltért a dokumentum
korábbi, emlékezetből idézett "~1.3" állításától:

| Level | n×n (lap) | Mért max/min arány |
|---|---|---|
| 5 | 32×32 | 1.3795 |
| 6 (klíma-alapszint) | 64×64 | 1.3969 |
| 7 (bolygónézet render) | 128×128 | 1.4055 |
| 9 (kontinensnézet) | 512×512 | 1.4120 |
| 11 (régiónézet) | 2048×2048 | 1.4137 |
| ∞ (kontinuum-határérték) | — | → √2 ≈ 1.41421 |

**Döntés (mindkét részkérdésre):**

1. **A `tan` transzcendens kockázata (ND-23b osztály):** a `TileId -> 3D
   pozíció` leképezés **konstrukciós (baked)** számításnak minősül, nem
   szimulációsnak. A pozíciókat egyszer, a rács felépítésekor számoljuk ki
   `tan`-nal, verzióhoz kötött, hash-elt táblaként kezeljük — a szimuláció
   (klíma, inszoláció) ebből olvas, nem újraszámolja. Így a `tan`
   platformfüggése a rács-metaadatba szigetelődik, nem a szimuláció
   kritikus útjába.
2. **Az `AreaDistribution` teszt küszöbe LOD-szint szerint differenciált**
   (a felhasználó által jóváhagyott "B" opció): a szigorú `< 1.40` küszöb
   csak a szimuláció bázis-szintjére (level ≤ 6) vonatkozik, ahol a mérés
   szerint van rá tartalék (1.3795–1.3969). A magasabb, render/nézet-célú
   LOD-szinteken (level ≥ 7) a küszöb `< 1.42`, ami a mért kontinuum-
   határértékhez (√2 ≈ 1.4142) igazodik, kis tartalékkal.

**Indok:** a szigorú küszöb megtartása pont ott hozott volna hamis piros
tesztet, ahol a vetítés matematikailag nem tud jobbat nyújtani (a `tan`-warp
ismert tulajdonsága, hogy a lap-sarok/lap-közép Jacobi-arány √2-höz tart) —
ez nem hiba, hanem a választott vetítés natúr viselkedése. Jobb vetítés
keresése (a korábban felmerült "C" opció) külön munkát igényelt volna
ismeretlen nyereségért, ezért elutasítva.

### ND-19 — Floating origin implementációja: HALASZTVA M9-re

Az ND-01 (Unity 6 + HDRP) aktiválta, M2/M3-ra sürgősnek jelölve — újragondolva
M3 (csillagászat + fény) tervezésekor.

**A felismerés:** a HDRP `Directional Light` (a Nap-szimulációhoz) fizikailag
**nem rendelkezik érdemi pozícióval**, csak **iránnyal** (rotációval), mert
végtelen távoli fényforrást modellez. Ebből következik: a Core-oldali
(`double` pontosságú, motorfüggetlen) csillagászat-számítás sosem kell, hogy
nyers, nagy-értékű pozícióként (pl. "150 millió km-re a Nap") kerüljön
Unity-koordinátába — elég egy **normalizált irányvektor**, ami `float32`-ben
is pontos, mérettől függetlenül.

A `float32` precíziós probléma (amiért ND-19 eredetileg felmerült) csak akkor
jelentkezne, ha a kamera **valós, km-skálájú bolygófelszín közelébe** kerülne.
A bolygó jelenleg (és M3 után is) önkényes `radius=100` Unity-egységben van,
nem valós méretben.

**Döntés:** az implementáció **halasztva M9-re** (Continent + Region nézet),
vagy amikorra ténylegesen éles bolygóméretre váltunk — M2/M3 nem függ tőle.
A konkrét technika (kamera-központú eltolás + logaritmikus depth, vagy
szektorált rebase — ld. `JakubNei/UnityProceduralPlanets` kutatás) továbbra
is nyitott, csak nem sürgős.

### ND-25 — Szomszédszám a kocka sarkainál: 4, nem 3

A §2.4 (`docs/05-milestones.md`) eredeti állítása szerint a 8 kocka-sarkot
tartalmazó tile-oknak 3 szomszédja van 4 helyett. Kimerítő méréssel
(`tools/reference/neighbor_ref.py`, level 3/4/5, MINDEN tile, 4-szomszédos
él-adjacencia: jobbra/balra/fel/le a folytonos uv-térben, majd a már
verifikált vetítéssel visszaprojektálva) ez **nem igazolódott**: mind a 24
lap-sarok-tile pontosan 4 disztinkt, szimmetrikus szomszéddal rendelkezik,
kivétel nélkül, minden mért szinten.

**Döntés:** a `NeighborCount` teszt egységesen `== 4`-et vár el, kivétel
nélkül (`docs/05-milestones.md` frissítve). Indoklás: egy négyzet alakú
cella geometriailag mindig 4 éllel rendelkezik, a kocka sarkán ülő cella
sem kivétel — a 2 lapváltó és 2 lapon-belüli él mind disztinkt szomszédhoz
vezet. A "3" valószínűleg egy MÁSIK, 8-szomszédos (átlós/vertex-adjacencia)
kapcsolódási módra vonatkozik, ahol a kocka geometriai csúcsánál ténylegesen
csak 3 lap találkozik egy pontban — ez akkor válik relevánssá, ha M7-nél
(hidrológia) D8-jellegű átlós folyásirány-modell kerül bevezetésre. Ha ez
bekövetkezik, új ND szükséges a 8-szomszédos séma sarok-viselkedésére; ez
NEM blokkolja a jelen (él-alapú) szomszédsági táblát.

### ND-26 — Trigonometria a csillagászati modul kritikus útján: kockázat elfogadva M3-ra

Az M3 csillagászati modulja (`tools/reference/astronomy_ref.py`: nap-irány,
tengelydőlés, forgás, szub-napponti pont, §27 inszoláció) `Sin`/`Cos`/
`Atan2`/`Asin` függvényeket használ, amik NEM garantáltan bitre azonosak
platformok között (ld. CLAUDE.md táblázat, ND-23b osztály).

**Ellentétben a rács-vetítéssel (ND-24), ez NEM "baked"-elhető egyszer** —
az idő előrehaladtával folyamatosan újraszámolódik (a nap iránya változik),
tehát valódi szimulációs-kritikus úton fut, amint a kimenete (§27 fluxus/
inszoláció) tényleges szimulációs bemenetté válik.

**Döntés (felhasználó jóváhagyta):** a kockázat elfogadva, **M3-ra
korlátozva** — a jelenlegi felhasználás kizárólag a Unity `Directional
Light` rotációját vezérli (vizuális, nem checkpointolt/hash-elt
szimulációs állapot), tehát a platformfüggő ULP-eltérés nem sérti I1-et.

**Sürgősség:** M5 (klíma) előtt **véglegesen** dönteni kell, amikor a §27
fluxus/inszoláció ténylegesen szimulációs bemenetté (és checkpointolt
állapottá) válik — analóg az ND-23b döntéssel (saját polinomiális
implementáció vagy előre számolt tábla + interpoláció közül választva).

**M5-nél visszatérve: ld. ND-27** — a kockázat továbbra is elfogadva marad,
de a hatókör kibővül és egy kemény, visszavonhatatlan határidő kerül rá.

### ND-27 — ND-26 visszatérése M5-nél → VÉGLEGESEN LEZÁRVA (B opció: saját polinomiális implementáció)

Az ND-26-ban rögzített visszatérési pont: a hőmérséklet-modell (§28.2,
Stefan–Boltzmann sugárzási egyensúly, `Math.Pow(x, 0.25)`) ténylegesen a
`OrbitalMechanics.Insolation`-t (Sin/Cos-alapú) használja szimulációs
bemenetként, aminek kimenete (`TemperatureField`, ld. spec §5.4) idővel
checkpointolt állapottá válik.

**Közbenső döntés (M5-nél, felhasználó jóváhagyta):** a kockázat
átmenetileg elfogadva maradt (A opció), MOST MÁR a klíma-modulra is
kiterjesztve. Utána M10-nél (lemezmozgás) és M11-nél (becsapódás) is
kiterjedt ugyanerre a kockázati osztályra, három-négy modulra nőve.

**VÉGLEGES DÖNTÉS: B opció (saját polinomiális implementáció).**
`src/WorldGen.Core/Numerics/DeterministicMath.cs` + Python-referencia
(`tools/reference/deterministic_math_ref.py`) — csak a CLAUDE.md
táblázat szerint GARANTÁLTAN bitpontos alapműveletekre épül
(`+ - * /`, `Math.Sqrt`, `Math.Floor`/`Math.Round` — IEEE-754
roundToIntegral EXAKT specifikáció, nem transzcendens közelítés —, és
nyers bit-manipuláció `BitConverter`-rel):

- **Sin/Cos**: oktáns-redukció (legközelebbi k·π/4-re redukálva
  [-π/8,π/8] tartományba, kizárólag /, -, Floor-lal) + Taylor-polinom +
  szög-összeg azonosság a 8 oktáns EXAKT értékével (0, ±1, ±√2/2).
  Plauzibilitás valódi `Math.Sin/Cos`-hoz képest: < 2e-14.
- **Exp/Ln/Pow**: IEEE-754 bit-dekompozíció (mantissza/exponens
  szétválasztás, mint a C standard frexp/ldexp) + Taylor-sor szűk
  tartományon. `Pow(x,y) = Exp(y·Ln(x))`, x=0,y&gt;0 esetén 0
  (dokumentált konvenció). Plauzibilitás: Exp &lt; 3e-13 relatív, Ln
  &lt; 2e-9 abszolút, Pow &lt; 3e-9 relatív hiba a valódi
  függvényekhez képest.
- A C#/Python implementáció **BITPONTOSAN** (0 tolerancia) egyezik
  1000 tesztvektoron (500 sin/cos + 500 pow) — ez NEM
  tolerancia-alapú teszt, mert mindkét oldal ugyanazt a saját
  algoritmust futtatja, nem a rendszer könyvtárát.

**Bekötve:** `OrbitalMechanics` (RotX/RotZ mátrixok, pálya-irány),
`PlateMotion` (Rodrigues-forgatás), `ImpactCratering` (kráter-képlet
Pow-jai + `cosAngularRadius`), `Temperature` (a negyedik-gyök
sqrt(sqrt(x))-ként EGZAKT — nem is közelítés, jobb mint a
DeterministicMath.Pow).

**ALGORITMUS-VÁLTÁS (dokumentált, szándékos):** az `ImpactCratering`
szög-mintavételezése emellett ÁT LETT TERVEZVE: az eredeti
`angle = 0.5·Acos(1-2u)` helyett korong-alapú elutasításos mintavétel
(Malley-módszer, ugyanaz az elv, mint `SampleUnitVector3`-nál) — ez
KÖZVETLENÜL sin(θ)-t adja, Acos és utólagos Sin nélkül: (x,y) egyenletes
az egységkorongon → sin(θ)=√(1-x²-y²). A kapott szög sűrűsége
bizonyíthatóan pontosan sin(2θ) (levezetés a forráskódban), ugyanaz,
mint az eredeti acos-inverzióé — de MÁS véletlenszám-fogyasztás, tehát
más seed→esemény leképezés, mint a korábbi verzióban. Elfogadható,
mert még nincs perzisztált világ, amit védeni kellene.

**KIVÉTEL — `OrbitalMechanics.SubsolarPoint`:** Math.Asin/Atan2-t
használ, SZÁNDÉKOSAN NEM cserélve. Jelenleg sehol nincs bekötve
szimulációs kritikus útra (csak tesztekben hívott — az `Insolation`
közvetlen pontszorzatot használ, nem szélesség/hosszúság koordinátán
megy át). Ha ez változik, az ND-27 osztálya ide is kiterjed, és ekkor
az `Atan2`/`Asin` is meg kell kapja a `DeterministicMath`-beli
megfelelőjét (jelenleg nincs implementálva — nyitva marad, ha
szükségessé válik).

**Eredmény:** M12 (checkpoint) előtti kötelezettség **teljesítve** —
nincs Math.Sin/Cos/Pow/Acos a jelenlegi szimulációs kritikus úton
(csillagászat, klíma, lemezmozgás, becsapódás). 208/208 teszt zöld.

### ND-28 — M11 becsapódások: hatókör-szűkítés + valós bolygó-sugár bevezetése

**Kérdés:** az M11 (Események) spec-hatóköre széles (becsapódás, vulkán,
rift, lemez-hasadás/egyesülés — §22-23 és a milestone-tábla). Emellett a
kráter geometriájának a rács-térbe (angular méret) illesztéséhez szükség
van egy **valós bolygó-sugárra méterben** — ez eddig sehol nem szerepelt
explicit Core-konstansként (a Unity `radius=100f` szándékosan tetszőleges
vizuális egység, ND-19 miatt még nem valós lépték).

**Döntés (hatókör-szűkítés, a korábbi mérföldkövekével azonos mintát
követve — ld. M5 szél/csapadék, M7 tavak/jég, M10 erózió/eljegesedés
halasztása):**

1. **M11 MVP = csak becsapódás.** Vulkán, rift, lemez-hasadás/egyesülés
   halasztva — nincs önálló numerikus alapjuk még, és a spec-ben is
   kevésbé kidolgozottak, mint a becsapódás (§22, konkrét képletekkel).
2. **Kráter-paraméterek közül csak `diameter` + `depth`.** A `rimHeight`
   és `ejectaRadius` (spec §22.4) halasztva — vizuálisan nem
   elengedhetetlen az első checkpointhoz ("kráterek látszanak"), és
   külön geometria-munkát igényelne (gyűrű alakú kiemelkedés a
   tile-mesh-en).
3. **Új `PlanetConstants.RadiusMeters = 7_420_000.0`** (a spec kanonikus
   példa-bolygója, `docs/00-spec-v1.0.md:2170,2253` — `radiusKm: 7420`).
   Ez **nem** oldja meg ND-19-et (floating origin, Unity-oldali render-
   precízió nagy léptéken) — az változatlanul M9-re marad. Ez csak azt
   mondja ki, hogy a Core matematikája (ami az elevation-t is már most
   is méterben kezeli, ld. M4 dokumentáció) mostantól egy konkrét
   bolygóméretet is ismer, a kráter valós fizikai méretének a rács
   szögtartományára (radián) való átváltásához.
4. **A becsapódás fizikája forrásból ellenőrzött, nem kitalált érték**
   (a projekt "ne bízz emlékezetben" elve szerint, WebSearch-csel
   verifikálva, két független találat egyezésével):
   - Tranziens kráter átmérő: Schmidt & Housen (1987) / Collins, Melosh
     & Marcus (2005) skálázás — `D_tr = 1.161 · (ρᵢ/ρₜ)^(1/3) · L^0.78 ·
     v^0.44 · g^-0.22 · sin(θ)^(1/3)`.
   - Mélység: egyszerű kráterekre mélység/átmérő ≈ 1:5 (közismert,
     széles körben idézett arány).
   - Becsapódási gyakoriság: `ρ(≥D) = 20 · D^-2.4` [1/év], D méterben —
     hatványtörvény NEO-becsapódási ráta-modell.
   - Sebesség: 15-25 km/s (a szakirodalomban idézett átlagos NEO-Föld
     ütközési sebesség 15-21 km/s köré esik).
   - Szög: `P(θ) ∝ sin(2θ)` — ez NEM empirikus, hanem geometriai tény
     (véletlen irányú becsapódás a gömbön), zárt alakban invertálható.
   - Sűrűségek: kőzet becsapódó ~3000 kg/m³, kéreg cél ~2700 kg/m³
     (Föld kontinentális kéreg átlaga, jól ismert érték).
5. **Trigonometria-kockázat (ND-27 osztálya kiterjesztve, nem új ND):** a
   kráter-képlet `Math.Pow`/`Math.Sin`/`Math.Cos`-t használ, ugyanaz a
   kockázati kategória és M12 előtti lezárási határidő vonatkozik rá,
   mint a klímára és a lemezmozgásra.

**Indoklás:** ez a minta megegyezik minden korábbi milestone
hatókör-szűkítésével — kisebb, de fizikailag/matematikailag megalapozott
MVP, explicit deferrállal, nem csendes leegyszerűsítéssel.

## Nyitott döntések

### ND-20 — Burst `FloatMode.Strict` kikényszerítése ⚠️ M2, korai

Az ND-01 miatt aktív. A Burst alapból `FloatMode.Default`-ban fordít, ami
engedélyezi a lebegőpontos műveletek átrendezését — ez csendben megsérti
I1-et. Egyetlen hiányzó `[BurstCompile(FloatMode = FloatMode.Strict)]` csak
platformok közötti hash-eltérésnél derül ki, ami nagyon drága hibakeresés.

**Javaslat:** CI-szkript vagy Roslyn analyzer, ami hibát dob minden
`WorldGen.*` névtérbeli `[BurstCompile]`-ra, aminek nincs
`FloatMode = FloatMode.Strict` paramétere.

**Sürgősség:** amint az első Burst-kód megjelenik a szimulációs oldalon —
ne utólag foltozzuk be.

### ND-21 — HDRP volumetrikus felhő űrből ⚠️ M2

Az ND-01 miatt aktív. A HDRP volumetrikus felhőrendszere eredetileg
földfelszíni nézetre készült; bizonytalan, hogy az űrből nézett teljes
bolygó felhőzete milyen minőségű.

**Javaslat:** M2-ben prototípussal ellenőrizni. Ha nem működik jól, a
Planet nézet felhői saját shaderrel készülnek — ez befolyásolja a
fidelity-becslést (`docs/02-fidelity-strategy.md`).

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
| ND-22 | Core assembly-izoláció | netstandard2.1, nulla motor-referencia | **Érvényben** |
