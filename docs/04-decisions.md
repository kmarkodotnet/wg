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

### ND-29 — M11 vulkánosság: hatókör szuper-vulkáni (VEI8) eseményekre szűkítve

**Kérdés (architekturális felismerés implementáció közben):** a valós
vulkáni gyakoriság (VEI3-7, forrás: több független cikk, ld. lent)
NAGYSÁGRENDEKKEL magasabb, mint amit egy becsapódás-stílusú, "epochonként
legfeljebb egy esemény" Bernoulli-modell kezelni tud. Számpélda: VEI5
(≥10⁹ m³) gyakorisága kb. 1-2/évtized — 10 000 éves epoch-ra vetítve ez
~1500 várható esemény EGYETLEN epoch alatt, ami szétfeszíti az
egy-epoch-egy-Bernoulli-döntés modellt (amit a becsapódásoknál pont az
tett működővé, hogy ott a ráta valóban `≪ 1`/epoch).

**Döntés (hatókör-szűkítés + a spec saját kategorizálásának követése):**
csak a **szuper-vulkáni (VEI8) eseményeket** modellezzük ezzel az
architektúrával. A "háttér" vulkánosság (VEI3-7, gyakori, apránként
építkező) egy STRUKTURÁLISAN MÁS modellt igényelne (folytonos/perzisztens
vulkáni központ, nem diszkrét ritka esemény) — ez NEM épül meg most,
külön, jövőbeli feladat. Ez a szűkítés NEM önkényes: a spec maga is külön
kategóriaként kezeli a `SuperVolcanicEruption`/`SuperVolcano` fogalmat
(`docs/00-spec-v1.0.md:418,1225-1228,1807`) a §21 általános
"Vulkánosság"-tól elkülönítve — a döntés a spec saját szerkezetét követi,
nem attól idegen egyszerűsítés.

VEI8-nál a gyakoriság (~1-2/millió év) már természetesen illeszkedik a
10 000 éves epoch-modellbe (várható érték ~0.015-0.03/epoch) — ugyanaz a
nagyságrend, mint a becsapódásoknál (0.0126/epoch).

**Fizika (forrásból ellenőrzött, WebSearch, több független forrás
egyezésével):**
- VEI-skála: minden lépés (VEI8=≥10¹² m³ tefra) a valódi, hivatalos
  osztályozás alsó határa (Wikipédia/USGS-szintű konszenzus).
- Méreteloszlás: minden VEI-lépés (10x térfogat) kb. 6-7x ritkább —
  hatványtörvény `N(≥V) ∝ V^-β`, β = log₁₀(6.5) ≈ 0.8129 (a 6-7
  tartomány geometriai közepéből).
- Gyakoriság-kalibráció: VEI8 (~1-2/millió év, több forrás) → ráta
  ≈ 1.5×10⁻⁶/év a küszöbnél.
- Felső biztonsági sapka (dokumentált, nem újra-mintavételezett, ld.
  ImpactCratering ugyanezen mintája): 5×10¹² m³ — a La Garita
  Caldera/Fish Canyon Tuff kitörés (kb. 28 millió éve), a valaha ismert
  LEGNAGYOBB vulkánkitörés valódi becsült térfogata.
- Geometria: pajzsvulkán-kúp, lejtőszög ~6° (idézett tartomány 2-10°
  ill. 4-8°, a kettő közepéből) — `V = π·h³/(3·tan²θ)` kúp-térfogat
  azonosságból `h = (3·V·tan²θ/π)^(1/3)`, sugár `r = h/tanθ`. A `tanθ`
  fix, egyszer kiszámolt konstans (nem futásidejű trigonometria — ld.
  ND-24 "baked" mintája), a köbgyök `DeterministicMath.Pow(x, 1/3)`.
- Pozíció: `PlateBoundaryEffect.TwoBestDots` ÚJRAFELHASZNÁLVA (nem
  duplikálva) — a "gap" (két legközelebbi lemez-mag dot-product
  különbsége) már bevezetett, bitpontos lemezhatár-közelség proxy.
  Elutasításos mintavétel: egyenletes gömbi pont, elfogadva
  `1 - gap/gapScale` valószínűséggel (0, ha `gap ≥ gapScale`) — így a
  pozíciók a lemezhatárok köré koncentrálódnak, tisztán exakt
  aritmetikával (nincs új transzcendens-kockázat).

**Trigonometria-kockázat:** nincs — a `DeterministicMath.Pow`
(köbgyök) és egy fix, konstrukciós idejű `tan(6°)` az egyetlen
nem-egész-aritmetikai elem, ugyanaz az ND-27 lezárt megoldása.

### ND-30 — M12 perzisztencia: hatókör a determinisztikus hash-függvényre szűkítve

**Kérdés:** a spec (§47, §60, §64) egy teljes event-sourcing +
checkpoint + `.worldpkg` fájlformátum + `worldgen verify` CLI rendszert
ír le. Ez a jelenlegi projekt-állapothoz képest (a legtöbb spec-beli
réteg — `AtmosphereLayer`, `ResourceLayer`, `CryosphereLayer` stb. —
még meg sem épült) messze aránytalan lenne egyetlen lépésben megépíteni.

**Döntés (hatókör-szűkítés, a korábbi mérföldkövekével azonos mintát
követve):** ez a lépés CSAK a determinisztikus **hash-függvényt**
(`WorldStateHash`) adja — a `.worldpkg` fájlformátum, a CLI
(`worldgen verify`), az event-sourcing/replay rendszer HALASZTVA.

**Indoklás, ami ezt NEM csonka félmegoldássá teszi:** a Core minden
része MÁR MOST is tiszta függvénye a `(worldSeed, paraméterek, idő)`
hármasnak (ld. `SeaLevelCalibration.ComputeElevationFieldAtTime`,
`ImpactCratering.GenerateCratersUpToTime` stb.) — nincs
"irreverzibilis" szimulációs állapot, amit event-sourcing-gal kellene
tárolni. Ebből következik, hogy a "világ mentése" valójában már ma is
triviális (elég a `WorldDefinition`-t, azaz a bemeneti paramétereket
elmenteni — a state ebből újraszámolható), és amire TÉNYLEGESEN szükség
van már MOST, az egy módszer annak automatizált ellenőrzésére, hogy
"ugyanaz a definíció ugyanazt a világot adja-e minden platformon" (I1).
Pont ezt adja a hash-függvény, a nagyobb fájlformátum/CLI-réteg nélkül is.

**Módszer:** SHA-256 egy `(TileId → double)` mezőn, KANONIKUS
(`TileId.Value` szerint növekvő, nem `Dictionary` bejárási sorrend)
sorrendben, EXPLICIT big-endian bájtsorrendben (nem a platform natív
bájtsorrendjére támaszkodva). SHA-256 GARANTÁLTAN determinisztikus
(bit-manipuláció, nem transzcendens közelítés) — más kockázati
osztály, mint a Math.Sin/Cos/Pow (ND-27), nem igényel ND-kockázatvállalást.

227/227 teszt zöld, a Python-referenciával (`state_hash_ref.py`)
BITPONTOS SHA-256 egyezés.

### ND-31 — M13 fraktál-zaj: a CrustElevation fehér zaja lecserélve térben koherens fBm-re

**Kérdés:** a `CrustElevation` osztály saját dokumentációja MÁR M4 óta
explicit módon felvetette (nem ND-ként, hanem "később finomítható
egyszerűsítésként"): az §13.2 "fraktál részlet" (F) helyett egyszerű,
tile-onként FÜGGETLEN (fehér zaj-szerű) magasság-jitter volt használva —
NEM térben koherens fBm/Perlin. Ez okozta a korábban (M4/M13
vizsgálatnál) dokumentált "túl szabályos" Voronoi-cella-szerű
partvonal/hegylánc-benyomást. A felhasználó explicit kérésére ez a
lépés MOST lezárva (korábban "A" opcióval — halasztás — döntött).

**Döntés:** valódi 3D gradiens-zaj (`FractalNoise`, Ken Perlin
"Improving Noise" 2002 kvintikus fade-görbével) + fBm (5 oktáv,
persistence=0.5, lacunarity=2.0 — standard, széles körben idézett
paraméterek) váltja fel a fehér zajt. A rácspont-gradiensek hash-elése
ÚJRAFELHASZNÁLJA a már verifikált `SampleUnitVector3`-at (nincs új,
ellenőrizetlen hash-függvény). A kvintikus fade-görbe és a trilineáris
interpoláció TISZTA POLINOM — nincs új transzcendens-kockázat (ND-27
osztálya nem bővül).

**Hatókör:** csak az "F: fractal detail" komponens — a §13.2 teljes
`H0(p) = w_c·C(p) + w_f·F(p) + w_r·R(p) + w_v·V(p)` képletéből a
kontinens-maszk (C) továbbra is a lemez-Voronoi-struktúra, a ridged
mountains (R) és volcanic field (V) komponensek NEM külön modellezettek
most (R részben már létezik `PlateBoundaryEffect` uplift formájában, V
az M11 szuper-vulkán eseményekben — ezek nem lettek most újratervezve).

**BLAST RADIUS (dokumentált, szándékos):** ez ELTÉRŐ, magasabb kockázatú
kategória, mint a korábbi ND-k, mert az `M4` bázis-elevációt módosítja,
amire SZINTE MINDEN azóta épült modul épül. Érintett, újragenerált
tesztvektorok: `crust_elevation_vectors.json`, `plate_boundary_vectors.json`,
`hydrology_vectors.json`, `features_vectors.json`, `state_hash_vectors.json`.
`TEST-EARTH-001` (50-75% víz, ≥2 kontinens) VÁLTOZATLANUL teljesül (65.00%
víz, 2 kontinens — a pontos tile-számok kis mértékben eltolódtak:
7346+1221 → 7343+1220). A régió-szegmentálás régió-száma is változott
(16→8) — ez a koherens zaj miatt módosult vízgyűjtő-topológia
természetes következménye, nem hiba.

**API-változás:** `CrustElevation.BaseElevation` és
`PlateBoundaryEffect.ElevationWithBoundary` mostantól a pozíciót
(x,y,z) használja a zaj-kiértékeléshez a korábbi `tileIdValue` helyett
(a `tileIdValue` paraméter megmaradt `ElevationWithBoundary`-n, csak
belül nem használt — visszamenőleges hívási kompatibilitás). A Unity
oldal NEM érintett (csak `ElevationWithBoundary`-t hívja, aminek a
publikus szignatúrája változatlan).

234/234 teszt zöld, a Python-referenciával BITPONTOS egyezés minden
érintett láncban.

### ND-32 — ND-31 vizuális visszajelzés alapján: erősebb zaj + kéreg-típus-tudatos határhatás

**Kérdés:** az ND-31 Unity-beli vizuális ellenőrzésekor a felhasználó
konkrét, jól diagnosztizálható hibákat talált:
1. A fraktál-zaj gyakorlatilag észrevehetetlen volt.
2. A kontinensek továbbra is szinte pontosan a lemez-Voronoi-cellákkal
   egyeztek.
3. A lemezhatárokon fix magasságú, "falszerű" kiemelkedés látszott.
4. **Óceáni-óceáni lemezhatárokon is** megemelkedett az óceán
   látszólagos szintje.
5. Óceán-kontinens határon a szárazföld irreálisan kidudorodott.

**Diagnózis (mérve, nem találgatva):** egy statisztikai teszt
kimutatta, hogy a "part-vonal - lemezhatár egyezés" mértéke (0.28%
eltérés) FÜGGETLEN volt a zaj-amplitúdótól (500-3500m tartományban
tesztelve) — ez bizonyította, hogy nem a zaj gyengesége az elsődleges
ok, hanem a `PlateBoundaryEffect.BoundaryUplift` egységes (kéreg-
típustól független) alkalmazása, ami minden határon (óceáni-óceáni is)
ugyanazt a max. 1500m-es kiemelkedést adja hozzá — ez már önmagában a
tengerszint fölé emelhet óceáni tile-okat.

**Döntés (két összehangolt javítás):**
1. **`CrustElevation.NoiseAmplitudeMeters`: 500 → 2000m.** A korábbi
   érték eltörpült a határ-uplift (1500m) és az óceán/kontinens
   alapszint-különbség (4800m) mellett — Unity-oldalon
   `elevationScale`-lel szorozva a zaj-hozzájárulás vizuálisan
   gyakorlatilag nem volt megkülönböztethető a sima felülettől.
2. **`PlateBoundaryEffect.BoundaryUplift` kéreg-típus-tudatos:** ha a
   két legközelebbi lemez MINDKETTŐ óceáni, az uplift
   `DefaultOceanicOceanicUpliftFactor = 0.15`-tel szorzódik (nem
   nullázva — a valóságban óceáni-óceáni konvergens határon vulkáni
   szigetívek épülnek, csak keskenyebb/alacsonyabb relieffel, mint egy
   kontinentális ütközési zóna). `TwoBestDots` új túlterhelést kapott,
   ami a két legközelebbi lemez INDEXÉT is visszaadja (a kéreg-típus
   lekérdezéséhez) — a régi, csak dot-productot visszaadó verzió
   megmaradt (backward-kompatibilis, `VolcanicEruption` ezt hívja
   tovább, mert annak nincs szüksége kéreg-típusra).

**MÉG NYITVA (a felhasználóval egyeztetve, külön kérésre indítva):** a
"kontinensek pontosan a lemez-Voronoi-cellákkal egyeznek" probléma
STRUKTURÁLIS — a kéreg-típus jelenleg lemez-szinten bináris (§14.1
Plate struct mintájára), a nearest-seed plate-hozzárendelés pedig
mindig geometriailag sima (nagykör-szerű) határvonalat ad. Ennek valódi
javítása **domain warping**-ot igényelne (a spec §13.1 is név szerint
említi) — a pozíciót magát eltorzítani zajjal, MIELŐTT a lemez-
hozzárendelés/gap-számítás megtörténne. Ez jóval nagyobb hatókörű
változás lenne (a `PlateGeneration.AssignPlate` szignatúráját és
gyakorlatilag minden hívóját érintené) — külön döntés kell róla, nem
ennek az ND-nek a része.

235/235 teszt zöld, Python-referenciával bitpontos egyezés.

### ND-33 — ND-32 után is "katasztrofálisan" gyenge zaj: sima fBm lecserélve ridged multifractalra

**Kérdés:** az ND-32 (4x amplitúdó + kéreg-típus-tudatos uplift) Unity-
beli ellenőrzésekor a felhasználó szerint a zaj továbbra is
"katasztrofálisan" gyenge/észrevehetetlen maradt.

**Diagnózis:** a sima fBm (Perlin-alapú gradiens-zaj összege) ELVE
lekerekített, sima dombokat ad — ez a technika lényegéből fakad, nem
hangolási hiba. Bármennyire is nő az amplitúdó, a JELLEGE (lágy,
folytonos átmenetek) vizuálisan "simának" hat, nem "zajosnak".

**Döntés:** a sima fBm helyett **ridged multifractal** (a spec §13.1
maga is név szerint felsorolja, mint külön noise-családot) —
oktávonként `(1-|zaj|)²`, ami ÉLES gerinceket ad ott, ahol az alap
gradiens-zaj nullát metsz, alapvetően más — sokkal kontrasztosabb —
vizuális jelleggel, mint a sima fBm. Emellett az amplitúdó tovább nőtt
(2000→3000m).

**Módszer:** `FractalNoise.RidgedMultifractal` — ugyanazt a már
verifikált `GradientNoise3D` primitívet és oktáv-összegzési vázat
használja, mint az `Fbm` (nincs új hash-függvény, nincs új
transzcendens-kockázat) — csak az oktávonkénti kombinálás formulája más
(`(1-|n|)²` `n` helyett). A `CrustElevation.BaseElevation` az `Fbm`
hívást `RidgedMultifractal`-ra cserélte, `(r-0.5)·2` előjeles
átalakítással (a ridged kimenet [0,1]-hez közeli, ~0.7 átlaggal — a
`(r-0.5)·2` visszaadja a szimmetrikus, mindkét irányba ható
perturbáció-jelleget).

**Hatás (mérve):** a nyers elevációtartomány -4872…+3674m-re nőtt
(korábban -4746…+1599m) — a legmagasabb kontinentális csúcs több mint
duplájára nőtt az alapszinthez (800m) képest. A vízgyűjtő-topológia is
jelentősen összetettebbé vált (11→35 régió) — közvetlen jele annak,
hogy a domborzat ténylegesen sokkal tagoltabb lett.

**Még mindig nyitva (változatlanul, ld. ND-32):** a "kontinensek
pontosan a lemez-Voronoi-cellákkal egyeznek" strukturális kérdés —
domain warping nélkül ez továbbra is fennáll, függetlenül a zaj
erősségétől/jellegétől, mert a lemez-HOZZÁRENDELÉS (nem csak az
eleváció) geometriailag sima marad.

237/237 teszt zöld, Python-referenciával bitpontos egyezés (`ridged`
mező hozzáadva a meglévő zaj-tesztvektorokhoz).

### ND-34 — Óceán-fenék tompítása + regionális "hegyvidékiség" maszk

**Kérdés:** az ND-33 (ridged multifractal) Unity-beli ellenőrzésekor a
felhasználó két további, konkrét problémát talált: (a) az óceánfenék is
túl erősen "hegyesnek" látszott (irreális — a valóságban az óceáni
relief szelídebb, a középóceáni hátak/árkok kivételével, amiket nem
modellezünk külön); (b) a durvaság EGYENLETES volt az egész
szárazföldön, holott realisztikusabb, ha van sík/fennsík ÉS hegyvidék
is, nem mindenhol ugyanolyan "zajos" a felszín.

**Döntés (két összehangolt javítás):**
1. **`OceanicNoiseFactor = 0.25`** — óceáni tile-ok a zaj negyedét
   kapják csak.
2. **`MountainMask`** — külön, ALACSONY FREKVENCIÁS (2.5, szemben a
   ridged részlet 8-as alapfrekvenciájával), 3 oktávos, SIMA fBm (nem
   ridged!) adja meg REGIONÁLISAN [0,1] tartományban, mennyire
   érvényesüljön a ridged részlet-zaj adott a helyen — `normalized =
   clamp01(m·1.3+0.5)`, majd `normalized^1.5` (a hatványozás a legtöbb
   területet inkább sík felé tolja, ritkábban ad dramatikusan durva
   zónát). Mérve: a maszk kb. 6.5%-a esik <0.1 alá (gyakorlatilag sík),
   ~20%-a >0.5 fölé (kifejezetten hegyvidéki) — valódi, nem egyenletes
   eloszlás.

**Módszer:** mindkét primitívet (`FractalNoise.Fbm` a maszkhoz,
`RidgedMultifractal` a részlethez) ÚJRAFELHASZNÁLJA — nincs új
hash-függvény vagy transzcendens-kockázat, csak eltérő
frekvencia/oktáv-paraméterezéssel és a végén szorzással kombinálva:
`noise · mask · amplitude`.

**Hatás (mérve):** a nyers elevációtartomány -4119…+2790m-re szűkült
(kevésbé szélsőséges, mint ND-33 -4872…+3674m-je — a maszk miatt csak a
terület egy RÉSZÉN érvényesül a teljes ridged amplitúdó). A stray
(5 tile alatti) szigetek eltűntek (10→2 nyers komponens), a
vízgyűjtő-szám 35→12-re csökkent — mérsékeltebb, de még mindig sokkal
tagoltabb domborzat, mint az ND-31 előtti fehér zaj.

`TEST-EARTH-001` VÁLTOZATLANUL teljesül (65.00% víz, 2 kontinens,
7375+1227 tile).

237/237 teszt zöld, Python-referenciával bitpontos egyezés minden
érintett láncban.

### ND-35 — Lemezhatár-kiemelkedés is a regionális hegyvidékiség-maszkkal szorozva

**Kérdés:** a vízfelszín-réteg (lásd fentebb, a `PlanetGridMesh`
`BuildOceanShell`-je) hozzáadása után a felhasználó észrevette: a
szárazföld PARTI SÁVJA a tengerszinthez képest irreálisan magas maradt.

**Diagnózis:** az óceán-kontinens határ MINDIG lemezhatár is — a
`PlateBoundaryEffect.BoundaryUplift` viszont az ND-34 `MountainMask`
bevezetése ELLENÉRE sem volt vele modulálva, csak a domborzat
fraktál-részlete. Emiatt a parti sáv MINDIG maximális (akár 1500m-es)
kiemelkedést kapott, függetlenül attól, hogy ott sík vidéknek vagy
hegyvidéknek "kellene" lennie — irreális "falat" húzva a tengerszint
fölé szinte minden parton.

**Döntés:** a `BoundaryUplift` mostantól UGYANAZZAL a
`CrustElevation.MountainMask` regionális maszkkal szorzódik, mint a
domborzat ridged részlete — sík régióban (a maszk ~6.5%-a) a parti sáv
sem kap érdemi kiemelkedést, hegyvidéki régióban (a maszk ~20%-a)
viszont továbbra is a teljes, dramatikus kiemelkedést kapja (mint a
valóságban pl. az Andok a csendes-óceáni parton — kivétel, nem
általános szabály).

**Mérve:** az érintett tile-okon az ÁTLAGOS uplift 543.1m-ről
192.1m-re csökkent (a maximum plafonérték, 1500m, változatlan maradt
ott, ahol a maszk épp magas). A vízgyűjtő-szám 12→51-re nőtt — a parti
sáv most sokkal tagoltabb, kevésbé egyenletesen "fal-szerű".

`TEST-EARTH-001` VÁLTOZATLANUL teljesül.

237/237 teszt zöld, Python-referenciával bitpontos egyezés.

### ND-36 — Domain warping: a lemez-Voronoi HATÁRA is organikusan hullámzik, nem csak a rárakott zaj

**Kérdés (a felhasználó ismételt visszajelzése, ND-32-ben már diagnosztizálva,
de akkor külön döntésre halasztva):** a lemez-HOZZÁRENDELÉS
(`PlateGeneration.AssignPlate`) egy tiszta "legközelebbi mag" (nearest-seed)
Voronoi-felosztás a gömbön — ez MATEMATIKAILAG MINDIG sima, nagykör-ív-szerű
határvonalat ad, FÜGGETLENÜL attól, mennyi zajt teszünk az elevációra
utólag (ND-31→ND-35 mind csak az ELEVÁCIÓT tette változatosabbá). Emiatt a
kontinensek/lemezhatárok (és ezzel a partvonal) továbbra is irreálisan
"geometrikusnak" hatottak.

**Döntés:** **domain warping** (a spec §13.1 is név szerint említi) — a
lemez-hozzárendeléshez és a lemezhatár-közelség ("gap",
`PlateBoundaryEffect.TwoBestDots`) számításhoz használt POZÍCIÓT egy
zaj-alapú eltolással torzítjuk el, MIELŐTT a legközelebbi-mag
keresés/gap-számítás megtörténne. Új, motorfüggetlen modul:
`src/WorldGen.Core/Terrain/DomainWarp.cs` (`DomainWarp.WarpPosition`).

**Módszer:** három FÜGGETLEN `FractalNoise.Fbm`-kiértékelés (dx,dy,dz) —
a MÁR VERIFIKÁLT primitívet ÚJRAFELHASZNÁLJA VÁLTOZATLAN szignatúrával
(nincs új hash-függvény, nincs új `RandomProperty`). A három komponens
dekorrelációját KIZÁRÓLAG fix, egymástól és nullától távoli bemeneti
koordináta-eltolással oldottuk meg (pl. `fbm(seed, x+7.13, y+2.71, z+9.01, ...)`
a dx-hez), mert a `Fbm` API nem vesz fel `property_id` paramétert. Az
eltolt (wx,wy,wz) vektort a nyers (x,y,z)-hez adva, majd egységvektorra
renormalizálva kapjuk a warpolt pozíciót — a renormalizálás
**KÖZVETLEN OSZTÁSSAL** történik (`wx/length`, nem `wx*(1/length)`
reciprok-szorzással), hogy bitre egyezzen a Python referenciával (ez volt
az első portolási kísérlet egyetlen ULP-eltérése, ld. lent).

**Paraméter-hangolás (empirikusan mérve, `tools/reference/domain_warp_ref.py`):**
`Strength=1.0, Frequency=2.0, Octaves=3` → átlagos szögeltolódás ~9.0 fok
(8000 véletlen ponton mérve, célzott nagyságrend 5-15 fok volt), és a
lemezhatárok közelében (gap < `PlateBoundaryEffect.DefaultGapScale`=0.04)
lévő pontok ~40%-ánál változik meg az `AssignPlate` eredménye a warp
hatására — érdemi, de nem kaotikus/domináló hatás.

**Hatókör (szándékos, indokolt):** a lemez-HOZZÁRENDELÉS és a
HATÁR-KÖZELSÉG kapja a warpolt pozíciót. A TÉNYLEGES ELEVÁCIÓ-ZAJ
kiértékelése (`CrustElevation.BaseElevation`, `CrustElevation.MountainMask`)
VÁLTOZATLANUL a NYERS pozíciót kapja — a már jól hangolt (ND-31→ND-35)
zaj-textúra ne változzon meg alapvetően emiatt. Négy hívási hely lett
konzisztensen bekötve: `SeaLevelCalibration.ComputeElevationFieldWithSeeds`
(AssignPlate előtt), `PlateBoundaryEffect.BoundaryUplift` (TwoBestDots
előtt, a `MountainMask` hívás NYERS pozíción marad), `VolcanicEruption.
SamplePositionNearBoundary` (az elfogadási gap-döntés előtt, a
VISSZAADOTT pozíció nyers marad), és a Unity `PlanetGridMesh.
ComputeDisplacedRadius` (AssignPlate előtt, konzisztensen a Core-oldali
logikával — különben a sarok-alapú megjelenítés nem-warpolt határokat
mutatna a tile-közepű adatokhoz képest).

**Hibakeresési tanulság (dokumentálva, mert újra elő fog jönni):** az első
C# port a Python `wx/length` osztást tévesen `wx*(1.0/length)`
reciprok-szorzásra cserélte (más C#-beli mintákat, pl.
`DeterministicRandom.SampleUnitVector3`-ot követve, ahol a Python IS
reciprok-szorzást használ) — ez 3, egymástól függő tesztfájlban okozott
egyetlen-ULP-differenciát (`PlateBoundaryEffectVectorFileTests`,
`FlowNetworkVectorFileTests`, `WorldStateHashVectorFileTests`), amíg ki
nem derült, hogy a `domain_warp_ref.py` KÖZVETLEN osztást használ, NEM a
projekt többi helyén megszokott reciprok-szorzás mintát. A tanulság:
**minden egyes osztás/normalizálás műveletet külön ellenőrizni kell a
Python referenciában**, nem szabad feltételezni, hogy egy korábban látott
minta (reciprok-szorzás) mindenhol érvényes — ez pontosan az a fajta
csendes IEEE-754 eltérés, amit a CLAUDE.md lebegőpontos táblázata figyelmeztet.

**Mért hatás:** `TEST-EARTH-001` VÁLTOZATLANUL teljesül (65.00% víz), de a
kontinens-szám 2→6-ra nőtt (7462+1053+37+30+7+6 tile, a korábbi 2 nagy
kontinens több, kisebb darabra esett szét — ez a warp SZÁNDÉKOLT hatása,
nem hiba). A régió-szám 51→100-ra nőtt. A parti magasság (tengerszinthez
képesti relatív eleváció) statisztikailag alig változott (ld. ND-37 —
kiderült, hogy ez NEM elsősorban a lemezhatár-geometria problémája volt).

249/249 teszt zöld (12 új: `DomainWarpTests.cs`), Python-referenciával
BITPONTOS egyezés minden érintett láncban (`domain_warp_vectors.json`,
és az újragenerált `plate_boundary_vectors.json`, `hydrology_vectors.json`,
`features_vectors.json`, `state_hash_vectors.json`, `volcanism_vectors.json`).
A `plate_vectors.json` és `crust_elevation_vectors.json` VÁLTOZATLAN
maradt (a mögöttes `AssignPlate`/`BaseElevation` függvények szignatúrája
és logikája nem változott — csak a HÍVÓ oldal ad nekik más pozíciót).

### ND-37 — a kalibrált tengerszint mélyen az óceáni-kéreg elevációtartományba esett, nem egy valódi part-átmenetnél — MEGOLDVA (2. irány: decorrelálás)

**Kérdés (ND-36 domain warping vizsgálata közben derült ki, mérve, nem
találgatva):** a felhasználó eredeti panasza ("a part túl magas a
tengerszinthez képest") a domain warping UTÁN is fennállt — a parti sáv
(óceáni szomszéddal rendelkező szárazföld-tile-ok) átlagos elevációja a
kalibrált tengerszinthez képest ELŐTTE ~4255m, UTÁNA ~4072m volt (level 6,
teljes mező) — a warp csak ~4%-ot javított, elhanyagolható.

**Gyökérok (mérve):** a `SeaLevelCalibration.CalibrateSeaLevel`
percentilis-módszere a `TargetWaterFraction`=0.65-nél kalibrál. A
`CrustElevation.DefaultOceanicProbability`=0.55 (lemez-szintű Bernoulli-
valószínűség) miatt a TILE-SÚLYOZOTT óceáni-lemez-arány a mért világon
véletlenül ~66.7% — gyakorlatilag EGYBEESIK a célzott víz-aránnyal (65%).
Emiatt a kalibrált tengerszint (mérve: -3220.7m) NEM egy valódi
kontinentális-perem elevációs átmenetnél metsz, hanem MÉLYEN az óceáni
kéreg elevációtartományán (`OceanicBaseMeters`=-4000m körül) BELÜL — a
"parti" tile-ok (elevation ≥ sea_level) valójában az óceáni-kéreg-eloszlás
FELSŐ SZÉLÉN lévő tile-ok, aminek a tengerszinthez viszonyított relatív
magassága ezért matematikailag nagy szám lesz, még ha a nyers eleváció
önmagában plauzibilis (néhány száz-pár ezer méteres) tartományban is van.

**Miért nem oldható meg egyszerű konstans-hangolással (mérve, kizárva):**
a `ContinentalBaseMeters`/`DefaultUpliftMaxMeters`/`NoiseAmplitudeMeters`
együttes DRASZTIKUS csökkentése is csak ~4240m→~3712m-re (kb. 12%)
mozdítja az átlagot — mert a probléma nem ezekben a konstansokban van,
hanem magában a percentilis-kalibráció és a lemez-szintű bináris
kéreg-típus KÖLCSÖNHATÁSÁBAN. Az `OceanicBaseMeters` közvetlen
csökkentése (pl. -2000m-re) erősen hat (~2460m-re csökkenti az átlagot),
de irreálisan sekély óceánt eredményezne — fizikailag rosszabb
kompromisszum, mint a jelenlegi állapot.

**Eredetileg NYITVA HAGYVA, NEM egyetlen konstans átírásával csendben
"lezárva".** Három lehetséges jövőbeli irányt jegyeztünk fel:
1. A kéreg-típus finomítása tile-szintű gradiensre (nem bináris
   lemezenkénti Bernoulli) a lemezhatárok közelében.
2. A `TargetWaterFraction` és `DefaultOceanicProbability` explicit
   szétválasztása/decorrelálása.
3. A "parti magasság" metrika újragondolása.

**MEGOLDVA — a 2. irány (decorrelálás) empirikusan bevált.** Python
referenciában (`tools/reference/crust_elevation_ref.py`,
`sea_level_ref.py`) lemértük `OCEANIC_PROBABILITY` ∈
{0.55, 0.50, 0.45, 0.40, 0.35, 0.30} értékekre (world_seed=0xA7C944210000,
plateCount=20, level=6; a `TargetWaterFraction` változatlanul 0.65
maradt). A plate-szintű Bernoulli-döntés miatt lépcsős platókban változik
az eredmény (0.55≡0.50, 0.45 önálló, 0.40 önálló, 0.35≡0.30):

| oceanic_prob | tile-súlyozott óceáni-arány | tengerszint | parti sáv átlagos relatív magassága | kontinensek (≥5 tile) | TEST-EARTH-001 |
|---|---|---|---|---|---|
| 0.55 (régi alap) | 66.1% | -3220.7 m | **4072.0 m** | 6 | PASS |
| 0.45 | 36.7% | +1217.1 m | 252.9 m | 41 | PASS |
| **0.40 (választott)** | **35.1%** | **+1235.1 m** | **242.6 m** | **44** | **PASS** |
| 0.35 / 0.30 | (level-5 mintán mérve, azonos plató) | — | ~292.9 m (level 5) | — | PASS |

**Választás: `DefaultOceanicProbability` = 0.40.** Indoklás: a 0.45/0.40
pár közel azonos, drasztikus javulást ad (~93-94%-os csökkenés a parti
magasságban) — 0.40 mérve minimálisan jobb (242.6 vs 252.9 m) ÉS a
0.35/0.30 platóhoz képest kevésbé fragmentált világot ad. A hipotézis
igazolódott: 0.40-nél a tile-súlyozott óceáni-arány (35.1%) messze a
65%-os víz-cél ALATT van, ezért a percentilis-kalibráció kénytelen a
legalacsonyabb fekvésű KONTINENTÁLIS tile-okba is belenyúlni ("kontinentális
self" hatás) ahelyett, hogy az óceáni-kéreg-eloszlás tetejénél állna meg —
ez adja a drámai javulást, fizikailag is plauzibilis "elárasztott
kontinentális-perem" értelmezéssel.

**Mellékhatás (tudatosan vállalt kompromisszum):** a kontinensszám 6→44-re
nő (a 41 kisebb sziget/kontinens mérete 5-617 tile között, a 3 legnagyobb
[3837, 1582, 1321] adja a szárazföld ~67%-át) — ez a magasabb, kontinentális
tartományba eső tengerszint természetes következménye: sok alacsony fekvésű
terület elárasztásra kerül, szigetvilágot hozva létre a korábbi 2 nagy
kontinens helyén (ND-36 már megkezdte ezt a fragmentációs trendet
2→6-tal, ND-37 tovább viszi 6→44-re). Ez NEM hiba, dokumentált,
szándékos kompromisszum a realisztikusabb part-átmenetért cserébe.

**Kódváltozás:** `src/WorldGen.Core/Tectonics/CrustElevation.cs` —
`DefaultOceanicProbability` 0.55→0.40. Downstream Python
tesztvektor-fájlok újragenerálva és bitre lemásolva a C# tesztadatokba:
`crust_elevation_vectors.json`, `plate_boundary_vectors.json`,
`hydrology_vectors.json`, `features_vectors.json`,
`state_hash_vectors.json` (mind változott). `volcanism_vectors.json` és
`plate_vectors.json` VÁLTOZATLAN maradt (ellenőrizve `git diff`-fel) — sem
a szuper-vulkán pozíció/gyakoriság-mintavétel, sem a lemez-mag-generálás
nem függ a kéreg-típustól. Hardcode-olt teszt-elvárások frissítve:
`SeaLevelCalibrationTests.ContinentSizesMatchPythonReferenceExactly`
(új 44-elemű méretlista), `FeaturesTests.
MatchesPythonReferenceContinentsAndRegionsExactly` (6→44 kontinens,
100→398 régió).

**`TEST-EARTH-001` állapota:** VÁLTOZATLANUL teljesül (65.00% víz, 44
kontinens ≥2).

249/249 teszt zöld (Debug és Release is) — nincs új tesztfájl, a meglévő
KAT/struktúra-tesztek a frissített vektorokkal és elvárásokkal futnak.

### ND-38 — Térfogat-megmaradás alapú tengerszint a deep-time (M10) láncban — MEGOLDVA

**Kérdés:** a `SeaLevelCalibration.CalibrateSeaLevel` percentilis-módszere
minden lekérdezéskor PONTOSAN a `targetWaterFraction` (0.65) arányú tile-t
teszi víz alá, FÜGGETLENÜL attól, hogy a domborzat hogyan alakul. Az M10
deep-time láncban (`PlanetGridMesh.Build()`) minden `deepTimeMyr` értékre
újra lefut ez a kalibráció a lemezmozgás miatt megváltozott elevációs
mezőn — ez azt jelenti, hogy a víz-arány MINDIG pontosan 65% marad, akárhogy
is nőnek a hegyek vagy ütköznek a kontinensek. Fizikailag ez hibás: a
víz TÉRFOGATÁNAK kéne megmaradnia, nem az aránynak.

**Döntés:** a `t=0` világállapotból (a meglévő, változatlan percentilis-
kalibrációval) egy dimenziómentes víztérfogat-proxyt számolunk (`V0`,
`ComputeFloodedVolumeProxy` — a már bevett tile-egyenletes-terület
közelítés, ld. `FeatureMetrics.AreaTiles` és ND-24 precedense, NINCS
valódi gömbfelszín-területsúlyozás). Minden későbbi `t`-nél ehhez a
RÖGZÍTETT `V0`-hoz tartozó egyensúlyi tengerszintet keressük meg
(`CalibrateSeaLevelByVolume`) — a `Σ max(0, H - elevation)` monoton növekvő
függvénye `H`-nak, ezért egyértelműen konvergál egyetlen FIX (60)
iterációjú bináris kereséssel.

**Determinizmus-érvelés (I1):** a bináris kereső FIX iterációszámú `for`
ciklus, NEM tolerancia-alapú `while` — egy tolerancia-alapú leállás
platformfüggő lebegőpontos kerekítési különbségek miatt eltérő
lépésszámban állhatna meg (pl. egy `Math.Abs(vol - target) < eps`
feltétel az utolsó bitben ingadozhat két platform között), míg a fix
iterációszám mindig ugyanazt az útvonalat futja be, bitre reprodukálhatóan
minden platformon és szálszámon.

**`t=0` visszamenőleges kompatibilitás — bitre garantált, NEM csak mérve
közelítő:** a `PlanetGridMesh.Build()`-ben a `deepTimeMyr == 0.0` ág
VÁLTOZATLANUL a régi `CalibrateSeaLevel` percentilis-hívást futtatja —
nincs új számítási lánc `t=0`-nál, tehát a már vizuálisan jóváhagyott
render bitre ugyanaz marad. A térfogat-alapú visszaoldás csak `t>0`-nál
fut. Ettől függetlenül Python referenciával (`tools/reference/
sea_level_ref.py`, "ND-38" szakasz) LEMÉRTÜK, hogy a két módszer `t=0`-nál
mennyire közelít egymáshoz — a numerikus pontosság dokumentálásához: 60
lépéses bináris kereséssel a visszaoldott szint és a percentilis-szint
közötti eltérés `6.82e-13 m` volt (world_seed=0xA7C944210000,
plateCount=20, level=6, sea_level≈1235.106 m) — gyakorlatilag a dupla
lebegőpontos kerekítési zaj szintjén, messze a méteres nagyságrendű
elevációs skálához képest elhanyagolható. A C# oldali teszt
(`SeaLevelCalibrationVolumeBasedTests.
AtTimeZeroVolumeBasedLevelMatchesPercentileLevel`) `1e-6 m` toleranciát
követel meg, jóval a mért pontosság felett hagyva biztonsági margót.

**Mért hatás — a víz-arány TÉNYLEGESEN elmozdul 65%-tól (a funkció
bizonyítéka, nem csak elméleti lehetőség):** Python referenciával mérve
(world_seed=0xA7C944210000, plateCount=20, level=5, `V0`=11 359 571.377
proxy-egység, a `t=0` percentilis-kalibrált 64.9902%-os víz-arányból
számolva):

| `timeMyr` | tengerszint (rögzített `V0` mellett) | mért víz-arány | eltolódás a 65%-os t=0 céltól |
|---|---|---|---|
| 0 | 1244.78 m | 64.99% | (bázis) |
| 50 | 849.38 m | 41.81% | **−23.19 százalékpont** |
| 100 | 1652.15 m | 85.66% | **+20.66 százalékpont** |
| 250 | 1969.43 m | 93.70% | **+28.70 százalékpont** |
| 500 | 1206.86 m | 64.18% | −0.82 százalékpont |

Minden mért `t`-nél a bináris kereső a `V0`-hoz `≤3.73e-9` abszolút
eltéréssel konvergált (ellenőrizve: `flooded_volume_proxy` a visszaoldott
szintnél). A víz-arány egyetlen mért pontnál sem omlott össze 0%-ra vagy
100%-ra (fizikailag plauzibilis tartományban maradt), miközben jól látszik,
hogy a korábbi, örökké-pontosan-65%-os viselkedés megszűnt.

**Kódváltozás:**
- `tools/reference/sea_level_ref.py`: `flooded_volume_proxy`,
  `calibrate_sea_level_by_volume` (60 lépés alapértelmezett), valamint
  `compute_elevation_field_at_time` (a `plate_motion_ref.plate_seed_at_time`
  felhasználásával) + `__main__` verifikációs szakasz.
- `src/WorldGen.Core/Tectonics/SeaLevelCalibration.cs`:
  `ComputeFloodedVolumeProxy`, `CalibrateSeaLevelByVolume` (tiszta, statikus
  függvények, ugyanaz a stílus, mint a meglévő `CalibrateSeaLevel`).
- `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`: `V0`
  gyorsítótárazása (`EnsureInitialWaterVolumeCache`) — csak seed/
  plateCount/level/targetWaterFraction változásakor számol újra, a
  `deepTimeMyr` csúszka mozgatásakor NEM; `Build()`-ben `deepTimeMyr==0`
  esetén a régi percentilis-hívás, egyébként a térfogat-alapú visszaoldás.
- Nincs downstream Python tesztvektor-változás (a `CrustElevation`/
  `PlateBoundaryEffect`/stb. numerikus viselkedése nem módosult, csak a
  tengerszint-kalibráció egy ÚJ, opcionális módja került hozzá).

**`TEST-EARTH-001` állapota:** VÁLTOZATLANul teljesül `t=0`-nál mindkét
módszerrel (65.00% víz, 44 kontinens ≥2 — a percentilis-módszer bitre
változatlan; a térfogat-alapú visszaoldással is teljesül, ld.
`SeaLevelCalibrationVolumeBasedTests.
TestEarth001HoldsWithVolumeBasedCalibrationAtTimeZero`).

261/261 teszt zöld (12 új: `SeaLevelCalibrationVolumeBasedTests`), Debug és
Release konfigurációban egyaránt.

## Nyitott döntések

### ND-39 — Level 8 rács-betöltés gyorsítása: CPU Job System + Burst elsőként, GPU compute shader csak külön bitpontossági validációval

**Kérdés (felhasználói visszajelzés):** a level 8 rács generálása/betöltése a Unity-nézőben (`PlanetGridMesh.Build()`) nagyon lassú. Gyorsítható-e, és bevonható-e a GPU a számításba az I1 (bitpontos determinizmus minden platformon) megsértése nélkül?

**Mennyiségi becslés (forrásból levezetve, nem találgatva).** Level 8: `n = 1<<8 = 256`, `tiles = 6·256² = 393 216` tile. A domborzat-lánc egyetlen pontkiértékelése (a `DeterministicRandom.SampleUnitVector3` ~1,9 átlagos iterációjával, ND-23a — mindegyik iteráció 1 Threefry-4x64-20 blokk):

- `FractalNoise.GradientNoise3D` = 8 sarok × `SampleUnitVector3` ≈ 8·1,9 = **~15,2 Threefry**
- egy `Fbm`/`RidgedMultifractal` oktávonként 1 `GradientNoise3D`
- `DomainWarp.WarpPosition` = 3 független `Fbm` (3 oktáv) ≈ 3·(3·15,2) = **~137 Threefry**

Tile-középre (`ComputeElevationFieldWithSeeds`): `WarpPosition` (AssignPlate előtt, ~137) + `BaseElevation` [`IsOceanic` 1 + `RidgedMultifractal` 5 oktáv ~76 + `MountainMask` 3 oktáv ~46 = ~123] + `BoundaryUplift` [még egy `WarpPosition` ~137] ≈ **~396 Threefry/tile-közép**. A Unity `Build()` ezen felül tile-onként **4 sarkot** is kiszámol (`ToDisplacedVector3 → ComputeDisplacedRadius`), egyenként ~396 Threefry, cache nélkül: **~1585 Threefry/tile**. Összesen **~1980 Threefry/tile**.

Level 8-ra: **393 216 · ~1980 ≈ ~780 millió Threefry-4x64-20 kiértékelés** egyetlen betöltésre. Egy Threefry-4x64-20 = 20 kör ARX (add/rotate/xor) 4×64-bites szavakon (~120 64-bites ALU-művelet), tehát nagyságrendileg **~90 milliárd 64-bites egészművelet**, ráadásul a `SampleUnitVector3` elutasításos ága elágazásokkal. Ez futásidőben mind **egyetlen szálon, a Unity fő szálán** történik.

**Jelenlegi végrehajtási modell:** tisztán szekvenciális. `Build()` egy `for face / for u / for v` háromszoros ciklus, a `ComputeElevationFieldWithSeeds` ugyanígy — **nincs `Parallel.For`, nincs Job System, nincs Burst**. A teljes lánc a fő szálat blokkolja betöltés alatt.

**Opciók:**

| Opció | Előny | Hátrány / kockázat |
|---|---|---|
| **A: CPU Job System + Burst (`IJobParallelFor` tile-onként)** | A lánc tiszta függvény (I2) → triviálisan tile-párhuzamos. Marad CPU IEEE-754 szemantikában, `FloatMode.Strict`-tel (ND-20) bitpontos. Reális ~10-20× gyorsulás (magszám × SIMD `double4`). | A `DeterministicRandom`/`FractalNoise`/`CrustElevation` lánc Burst-kompatibilissá tétele: a `Dictionary<TileId,double>`, a `(double,double,double)[]` managed tömbök és az `out`-tuple minta helyett `NativeArray`/blittable value-típusok kellenek. A forró úton nincs kivétel és nincs transzcendens (csak `+ - * / sqrt abs floor`), tehát Strict-kompatibilis. |
| **B: GPU compute shader (HLSL)** | A Threefry counter-based, állapotmentes, tiszta (kulcs,számláló)→kimenet — kifejezetten masszív GPU-párhuzamosításra tervezve. Nagyságrendekkel nagyobb áteresztés. | **Komoly I1-kockázat.** A GPU lebegőpontos aritmetika gyártók/driverek/shader-fordító közt NEM garantáltan bitpontosan azonos a CPU IEEE-754-gyel (ugyanaz az osztály, mint a Burst `FloatMode.Default` — ND-20 — és a transzcendens-kockázat, ND-26/27). A mező táplálja az óceán/biome/tengerszint/`WorldStateHash`-t → checkpointolt, kritikus út. |
| **C: Algoritmikus — sarok-deduplikáció + warp-hoisting** | Determinizmus-semleges, azonnali nyereség. | Nem old meg mindent önmagában. |

**Determinizmus-semleges algoritmikus nyereségek (C, bármelyik úttal kombinálható):** (1) **Sarok-cache** — jelenleg minden sarok tile-onként újraszámolódik, holott egy sarkot legfeljebb 4 tile oszt: a megosztott sarkok deduplikálása a ~1585 Threefry/tile sarok-költség kb. 75%-át megszünteti (azonos bemenet → bitre azonos kimenet, nem seed-törő). (2) **Warp-hoisting** — a `WarpPosition` tile-onként kétszer fut (AssignPlate-hez és `BoundaryUplift`-ben); egyszeri kiszámítás bitre azonos értéket ad (nem seed-törő), a közép-költség ~30%-át megspórolva.

**JAVASLAT.** Elsőként az **A opció (CPU Job System + Burst, `FloatMode.Strict`)**, előtte/mellette a **C** olcsó, determinizmus-semleges nyereségekkel (sarok-cache, warp-hoisting) — ezek együtt reálisan több tízszeres gyorsulást adnak I1-kockázat nélkül, a projekt már meglévő ND-20 kötelezettségére építve. **B (GPU) csak KÜLÖN, óvatos munka**, és **kizárólag megjelenítési célra** (a nem-checkpointolt sarok-eltolás/mesh vizualizáció, analóg az ND-26 „csak Directional Light" és az ND-27 kivétel-kezelésével), VAGY ha bizonyítottan bitpontos minden támogatott GPU-n — a checkpointolt/hash-elt mezőt GPU nem adhatja, amíg ez nincs igazolva. A hierarchikus/LOD-alapú lusta betöltés (csak a kamerához közeli tile-ok finom szinten) **kapcsolódó, de külön munka** (egy korábbi LOD-ötletelésben már felmerült), nem ennek az ND-nek a hatóköre.

**Verziózás:** A és C bitre azonos mezőt ad (tiszta függvény, változatlan számítási lánc) → **nem seed-törő**, verzióemelés nem szükséges. B akkor és csak akkor vezethető be checkpointolt útra, ha bitpontos — különben I1-sértés, nem verzió-kérdés.

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs` (`Build()`, `ComputeDisplacedRadius`), `src/WorldGen.Core/Tectonics/SeaLevelCalibration.cs`, `src/WorldGen.Core/Terrain/FractalNoise.cs`, `src/WorldGen.Core/Terrain/DomainWarp.cs`, `src/WorldGen.Core/Tectonics/CrustElevation.cs`, `src/WorldGen.Core/Tectonics/PlateBoundaryEffect.cs`, `src/WorldGen.Core/Random/DeterministicRandom.cs`, `src/WorldGen.Core/Random/Threefry4x64.cs`.

### ND-40 — A viewer mesh-architektúrája: adaptív kvadfa-LOD a fix egészgömbös build helyett — és viszonya az ND-39 "A" (Burst) opcióhoz

**Kérdés (felhasználói visszajelzés + M9 tervezés).** Zoomoláskor a
`PlanetGridMesh` domborzata pixeles/durva marad, mert egyetlen fix
LOD-szinten, egyszerre a TELJES gömbre épül, és nem sűrűsödik a kamera
közelében. Két, egymással összefüggő döntés kell: (1) a viewer megtartsa-e
a mostani egészgömbös `Build()`-et (csak magasabb fix szintre emelve),
vagy váltson perzisztens, kamera-vezérelt adaptív kvadfára inkrementális
frissítéssel; és (2) hogyan viszonyul ez az ND-39 "A" (CPU Job System +
Burst) tervéhez — az adaptív LOD feleslegessé/halaszthatóvá teszi-e a
Burst-öt a viewer interaktivitásához?

**Kontextus (forrásból).** A domborzat-mező LOD-független tiszta függvény
`(seed, x, y, z)`-ből (`CrustElevation`/`PlateBoundaryEffect`/`DomainWarp`),
és a viewer `ComputeDisplacedRadius` már pont-alapon értékeli ki — tehát az
adaptív LOD **nem igényel új Core-numerikát vagy Python-referenciát**. Az
ND-39 az egészgömbös level 8 buildet ~780M Threefry-kiértékelésre mérte,
egyetlen főszálon. Az ND-02 rögzíti: a szimuláció bázis-LOD fix level 6, a
LOD csak lekérdezésre/renderre. A §70.5 megköveteli, hogy a fő partvonal ne
változzon LOD-váltáskor.

**Opciók:**

| Opció | Előny | Hátrány / kockázat |
|---|---|---|
| **A: Marad a fix egészgömbös `Build()`, magasabb szintre emelve, ND-39-A (Burst) gyorsítással** | Legkevesebb új viewer-kód; a meglévő szerkezet marad. | Az idő/memória 6·n²-nel skálázik a kamerától FÜGGETLENÜL. Level 11 (25M tile) egyszerre felépítése Burst-tel is irreális. A felhasználói panaszt (kamera-közeli finomodás) nem oldja meg — a részletesség globális, nem a nézetre koncentrált. |
| **B: Adaptív, perzisztens kvadfa, inkrementális frissítés + geomorphing (az M9-terv)** | Az egyidejűleg kiértékelt pontszám a látótértől függ, nem a max-mélységtől → nagyságrendekkel kevesebb. Level 11 zoom így válik egyáltalán lehetségessé. Közvetlenül megoldja a panaszt (kamera-közeli finomodás, folyamatos átmenet). | Több új viewer-kód (kvadfa, 2:1 balance, stitching, geomorphing, LRU sarok-cache). Tisztán megjelenítési, de nem triviális. |
| **C: Hibrid — adaptív kvadfa a geometriára, MEGTARTOTT fix level-6 referencia-passz a tengerszinthez / panel-cache-hez / biome-hoz** | B minden előnye, PLUSZ a §70.5 (partvonal-invariancia) és az ND-02 (fix bázis-LOD) strukturálisan garantált: a tengerszint és a panel-adat a fix referencia-szintről jön, nem a változó-LOD ponthalmazból, tehát a partvonal nem remeg zoomkor. | Két adat-út (referencia-szintű + adaptív) párhuzamos kezelése — de ezek tisztán szétválnak (panel vs. render). |

**JAVASLAT: C opció (adaptív kvadfa-render egy megtartott, fix level-6
referencia-passz felett).** Ez oldja meg a felhasználói panaszt anélkül,
hogy a partvonal/tengerszint zoomkor elmozdulna (§70.5, ND-02). A
`_lastField`/`_lastIsOcean`/`_lastBiomeOf`/`_lastSeaLevel` referencia-cache
változatlanul level 6-on marad (a panelek makrostruktúrát írnak le), az
adaptív magas-LOD geometria külön, tranziens, LRU-korlátos csomópont-
cache-ben él.

**A Burst-kérdés (ND-39-A viszonya) — állásfoglalás:** az adaptív LOD az
egyidejű ponthalmazt a kamera látóterére korlátozza (nagyságrendileg
konstans, a max-mélységtől független), így a per-frame inkrementális
frissítés főszálas, amortizált (frame-költségvetéses) végrehajtással is
elég gyors az interaktív zoomhoz. Ezért **az ND-39-A (Burst/Job) NEM
előfeltétele a viewer interaktivitásának** — halasztható, és csak akkor
aktiválandó, ha a profilozás (M9 9.5) főszál-akadást mutat nagy régió
belépésekor, VAGY ha egy külön, egészgömbös level-8 "dump" (nem
interaktív) igényként előjön. A két munka tehát szétcsatolva: az M9
adaptív LOD önmagában, Burst nélkül szállítja a folyamatos zoomot; az
ND-39-A opcionális gyorsítás marad a nem-adaptív utakra.

**Determinizmus / lebegőpont.** Ez KIZÁRÓLAG megjelenítési munka, nem
érint egyetlen Core-numerikát sem, nem változtat semmilyen mezőt, óceán/
biome-osztályozást, tengerszintet vagy `WorldStateHash`-t. A display-
oldali aritmetika (kamera-távolság-arány, morph-lerp, LOD-küszöbök) `float`
és NEM tartozik az I1 platformok-közötti bitpontosság hatálya alá (ugyanaz
a besorolás, mint a viewer meglévő `WaterDepthBucket` `Math.Exp`-je) —
követelmény csak a **session-en belüli reprodukálhatóság** (ugyanaz a
kamera-pozíció → ugyanaz a mesh; oda-vissza zoom → nincs drift/remegés),
amit a tiszta-függvény kiválasztás + hiszterézis biztosít. Új transzcendens
függvény a szimulációs kritikus úton NINCS.

**Verziózás: NEM seed-törő.** Az adaptív LOD bitre azonos mezőt jelenít
meg (a Core-lánc változatlan, csak MÁS pontokon és MÁS sűrűséggel
mintavételezve) — nem módosít semmilyen seed-hez kötött numerikus
viselkedést. Verzióemelés nem szükséges.

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/
PlanetGridMesh.cs` (`Build()`, `ComputeDisplacedRadius`,
`GetOrComputeCorner`, `ToDisplacedVector3`, cornerCache,
`_lastField`/`_lastSeaLevel`), `src/WorldGen.Core/Grid/TileId.cs`
(`Parent()`/`Child()`), `src/WorldGen.Core/Grid/TileNeighbors` (2:1
balance), `src/WorldGen.Core/Tectonics/SeaLevelCalibration.cs`
(referencia-szintű tengerszint). Kapcsolódó döntések: ND-02 (fix bázis-
LOD), ND-39 (Burst/Job és a sarok-cache/warp-hoisting), spec §51-52 és
§70.5.

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

### ND-41 — Szél, párolgás/csapadék, időjárás-zaj: egyszerűsített, dokumentált zárt modell zárt formula nélküli spec-szakaszokhoz

**Kérdés:** a spec §30 ("Szél"), §31 ("Nedvesség és csapadék") és §32
("Időjárás") csak minőségi komponenslistát ad, zárt formula nélkül —
pl. `Evaporation = f(temperature, wind, surfaceWater)`, "moist air →
mountain → uplift → precipitation", `W(p,t) = Noise(Warp(p,t), seed)`.
A `tools/reference/wind_precipitation_ref.py` Python-orákulumhoz
konkrét, determinisztikus képleteket kellett választani öt olyan
ponton, ahol a spec nem specifikál egyértelműen.

**Döntés (öt alpont):**

1. **Háromsávos zonális index alakja.** Háromszög-hullám (0 → csúcs → 0)
   minden 30 fokos sávban, előjellel váltakozva (Hadley: negatív/keleti,
   Ferrel: pozitív/nyugati, Polar: negatív/keleti), nullátmenettel
   pontosan 0/30/60/90 foknál. **Indoklás:** ez kvalitatívan megfelel a
   valódi "csendöveknek" (doldrums ~0°, ló-szélességek ~30°, szubpoláris
   mélynyomás ~60°) és a köztük váltakozó szélöveknek — nem mérési érték,
   csak kvalitatív proxy, de nem önkényes: a nullátmenetek a valódi
   sávhatárokra esnek.

2. **Coriolis-proxy formája.** A hőmérsékleti nyomásgradiensből számolt
   termikus szélkomponenst egy FIX szögű forgatással térítjük el
   (`CORIOLIS_DEFLECTION_DEG_DEFAULT = 30°`), előjele `-sign(szélesség)`
   — jobbra az É-, balra a D-féltekén, a geosztrofikus szél klasszikus
   kvalitatív viselkedésének megfelelően. **Indoklás:** a szélességi
   alapcellák már saját sávos előjelváltással rendelkeznek (1. pont), a
   Coriolis-hatást csak a termikus komponensre alkalmazzuk, hogy a két
   hatás külön tesztelhető és dokumentálható legyen. A konkrét szögérték
   (30°) illusztratív, nem mérés — később finomítható.

3. **Hőmérséklet-gradiens számítási módja.** Véges differencia a már
   verifikált `temperature_kelvin`-ből, a lokális kelet/észak érintő-
   irányban, `GRADIENT_EPS = 1e-3` radián lépéssel, a szomszéd pontokon
   is a HÍVÓ tile aktuális `is_oceanic`/`elevation_m` értékét feltételezve
   (a valódi szomszéd-tile adatok nem elérhetők ebben az orákulumban,
   mert az nem fér hozzá a tile-rácshoz/szomszédsági táblához).
   **Indoklás:** ez dokumentált egyszerűsítés, nem hiba — a tényleges
   motor-integrációkor a hívó könnyen cserélheti valódi szomszéd-
   lekérdezésre.

4. **Elevációgradiens mint közvetlen bemenet.** A hegyi eltérítéshez és
   az orografikus csapadékhoz szükséges elevációgradienst (kelet/észak
   komponens) a modul KÖZVETLEN BEMENETKÉNT várja, nem számolja újra a
   kéregmodellből. **Indoklás:** ugyanaz a minta, mint ahogy
   `temperature_ref.py` is közvetlen bemenetként várja az
   `elevation_m`/`sea_level_m` értéket a kéregmodell újraszámolása
   helyett — a döntés a moduláris, réteg-független orákulum-tesztelést
   szolgálja, a motor-integrációkor triviálisan csatolható a valódi
   szomszéd-elevációhoz.

5. **Weather-deviáció korlátozásának módja (§32.3).** `tanh()`, nem kemény
   `clamp()`. **Indoklás:** a `noise_ref.fbm` saját plauzibilitás-tesztje
   szerint az fBm-érték ritkán enyhén túllépheti a [-1,1] tartományt
   (ld. `noise_ref.py` __main__, "[-1.5,1.5] körüli tartomány"). Kemény
   `clamp()` esetén ez egy látható, éles "plafont" adna a deviáció-
   mezőben (mesterségesen lapos foltok ott, ahol a zaj épp túllépte a
   határt) — a `tanh()` ehelyett simán, aszimptotikusan telítődik, nincs
   élesség-artefaktum, és `|tanh(x)| < 1` bármely véges x-re, tehát a
   deviáció szigorúan korlátos marad a klimatikus átlag körül, ahogy a
   §32.3 kifejezetten megköveteli ("a weather noise nem írhatja felül a
   klímát").

**Verziózás:** ez egy ÚJ modul (nincs korábbi C#-implementáció, amit
felülírna), ezért nem seed-törő a meglévő világokra nézve — de a modul
saját belső konstansai (a fenti 5 pont + a `BASE_WIND_SPEED_DEFAULT`,
`OROGRAPHIC_COEFF_DEFAULT` stb. numerikus alapértékek) a jövőben
finomíthatók, amíg a C#-port el nem készül és be nem kerül a
`testvectors.json`-ba — utána bármely módosításuk verzióemelést igényel.

**Verifikálva:** `tools/reference/wind_precipitation_ref.py` önállóan
lefut, 7 plauzibilitás-blokk zöld (zonális index, Coriolis-előjel,
hegyi eltérítés, determinizmus, párolgás-monotonitás, orografikus
csapadék/rain-shadow, weather-korlátosság), és két egymást követő
futtatás bitre azonos `wind_precipitation_vectors.json`-t generál
(SHA-256 egyezés).

**Releváns fájlok:** `tools/reference/wind_precipitation_ref.py` (import:
`temperature_ref.temperature_kelvin`, `noise_ref.fbm`,
`domain_warp_ref.warp_position`).

### ND-42 — M5 teljes hőmérséklet-modell: T_greenhouse/T_ocean/T_weather/T_cycle modellezési választásai

**Kérdés.** A backlog "M5 | Teljes hőmérséklet-modell" tétele a spec §28.1
teljes egyenletét kéri:

```
T = T_radiative + T_greenhouse + T_ocean - T_altitude + T_weather + T_cycle
```

A meglévő `tools/reference/temperature_ref.py` addig csak
`T_radiative + T_greenhouse(fix 33K) - T_altitude`-ot valósította meg. A spec
sem §28-nál, sem §29-nél (albedo-feedback) nem ad zárt formulát a hiányzó
három tagra — csak minőségi leírást (`cooling → more ice → higher albedo →
more cooling`, ill. §32 "advected procedural field", §25 "Milanković-szerű
komponensek, ne földi periódusokkal"). Ez a döntés rögzíti, milyen konkrét,
zárt modellt választottunk mind a négy tagra, mert a spec ezt nyitva hagyta.

**Réteg-döntés (nem tárgyalható, csak dokumentált): a régi `temperature_kelvin`
függvény VÁLTOZATLAN maradt.** A már generált 150 elemű
`temperature_vectors.json` és a rá épülő C# `Temperature.cs`/
`TemperatureTests.cs` bitre azonos maradt (ellenőrizve: a fájl `git diff`-je
üres). A teljes egyenlet egy ÚJ függvényben (`temperature_kelvin_full`)
készült el, saját, KÜLÖN tesztvektor-fájllal (`temperature_full_vectors.json`)
— ez még NINCS C#-portolva, az egy külön, későbbi lépés (lásd "Hátralévő" lent).

**1. T_greenhouse — logaritmikus, CO2-szerű koncentráció-proxy.**

| Opció | Előny | Hátrány |
|---|---|---|
| A: marad fix 33K konstans | Nincs új kockázat | Nem "modell", nem függ semmilyen bolygóparamétertől — a backlog tétel ezt kifejezetten kéri bővíteni |
| **B: `T_greenhouse = 33K + sensitivity · log2(ghg_ppm / 280ppm)`** | Fizikailag ismert kvalitatív minta (radiative forcing ~ln(concentráció)); a referencia-koncentráción PONTOSAN visszaadja a régi 33K-t (ln(1)=0 egzaktul) | A `sensitivity_k_per_doubling=3.0` és a `280 ppm` referencia valós Föld-adatok (preindusztriális CO2, IPCC "equilibrium climate sensitivity" középbecslése ~1.5–4.5K sávból), de itt egy szintetikus bolygó PROXY-jaként, nem mérésként használjuk |
| C: lineáris arányosság a koncentrációval | Nincs új transzcendens kockázat | Fizikailag kevésbé indokolt (a valós üvegházhatás jól ismerten logaritmikus, nem lineáris) |

**JAVASLAT: B.** A `dm.ln`/`dm.exp` (a már lezárt ND-27 `deterministic_math_ref`
modulja) használatával nem nyit új, nem-dokumentált transzcendens-kockázatot.
**MEGERŐSÍTÉST IGÉNYEL:** a `280 ppm` és a `3.0 K/duplázódás` valós fizikai
becslések, nem KAT-szerűen verifikálható algoritmus-konstansok — ha a
felhasználó más értéket akar, ez paraméterezhető (`ghg_ppm`,
`sensitivity_k_per_doubling`), a névleges alapértelmezettek csak egy
kiindulási javaslat.

**2. T_ocean — kontinentalitás/hőtehetetlenség, évi átlaghoz húzás.**

Az óceáni tile pillanatnyi `T_radiative`-ját az évi (12 havi mintás) átlaga
felé húzzuk egy `buffering_strength` (alapértelmezett 0.3) együtthatóval;
szárazföldön pontosan 0 (nincs változás a régi viselkedéshez képest).
Ellenőrizve (`__main__` plauzibilitás-teszt): 45°-on az óceán évszakos
szórásnégyzete (~500) érdemben kisebb, mint a szárazföldé (~880) ugyanazon a
szélességen. **MEGERŐSÍTÉST IGÉNYEL:** a `0.3` együttható és a 12 mintás évi
átlagolás felbontása tisztán modellezési választás, nincs spec-forrás vagy
mért Föld-adat mögötte.

**3. T_weather — §32.2 stateless időnoise, ÖNÁLLÓ MINIMÁLIS PLACEHOLDER.**

A feladat idején a testvér-feladat (szél/nedvesség/csapadék,
`wind_precipitation_ref.py`) párhuzamosan, még nem lezártan fut. Emiatt a
`weather_deviation_k` a spec §32.2 mintáját (`W(p,t) = Noise(Warp(p,t),
seed)`) a már verifikált `noise_ref.fbm`-mel valósítja meg, ahol a "Warp"
egy egyszerű, determinisztikus idő-eltolás (`WEATHER_TIME_DRIFT · day_t ·
WEATHER_TIME_SCALE_PER_DAY`) — NEM a teljes szél/nyomás/nedvesség-alapú
időjárás-mező. **HATÁRVONAL, NEM TÖRLENDŐ CSENDBEN:** amint a
`wind_precipitation_ref.py` elkészül, ezt a függvényt át kell nézni/
egyesíteni azzal (ne maradjon két független időjárás-forrás a rendszerben).
Az amplitúdó (`WEATHER_AMPLITUDE_K_DEFAULT = 4.0K`, a spec §32.3 saját
példájából: "-4°C" tipikus deviáció) és a zaj-frekvencia/oktávszám
modellezési választás. Ellenőrizve: az átlagos/max abszolút deviáció jóval a
tipikus egyenlítő-pólus klímakülönbség alatt marad (§32.3 "a weather noise
nem írhatja felül a klímát" — additív, nem domináns).

**4. T_cycle — Milanković-szerű additív kényszerítő oszcilláció, SZŰKÍTETT
HATÓKÖRREL.**

Három szinuszos komponens (eccentricity/obliquity/precession-szerű, spec
§25), seedelt periódusokkal "fizikailag ésszerű tartományból"
(50k–500k / 20k–150k / 10k–50k év — NEM a valódi Föld 100k/41k/23k éves
Milanković-periódusok, a spec kifejezetten tiltja a földi periódusok
másolását). **HATÓKÖR-HATÁR A TESTVÉR-FELADATTAL (M10 erózió+eljegesedés-
ciklusok) SZEMBEN, EXPLICITEN RÖGZÍTVE:** ez a modul KIZÁRÓLAG a
hőmérséklet-egyenlet additív, időfüggő bemeneti tagját számolja
(`climate_cycle_temperature_k`). A jégtakaró/eljegesedés TÉNYLEGES
következménye (jégmennyiség, albedo-visszacsatolás, tengerszint-hatás — §29
feedback-hurok) NEM ennek a modulnak a hatásköre, azt az M10 (erózió+
eljegesedés-ciklusok) feladat implementálja majd, ennek a `T_cycle` kimenetét
mint bemenetet felhasználva. **MEGERŐSÍTÉST IGÉNYEL:** a három periódus-
tartomány és a három amplitúdó (2.0/3.0/1.0 K) tisztán modellezési választás,
nincs mögötte spec-adat vagy hivatalos forrás.

**Determinizmus.** Minden új tag tiszta függvény (nincs mutable állapot);
`dm.sin_cos`/`dm.ln`/`dm.exp` (a lezárt ND-27 polinomiális implementációja)
használatával, NEM nyers `math.sin/log/exp`-pel — így az új ágak nem nyitnak
új, dokumentálatlan transzcendens-kockázatot. Megjegyzés: a MEGLÉVŐ
T_radiative/T_altitude lánc (`astronomy_ref.sun_direction_body_frame`, ill. a
`raw ** 0.25` negyedik gyök Python beépített `**`-tal) továbbra is nyers
`math.sin/cos`-t és `**`-ot használ — ez az ND-27 lezárása ELŐTTI állapotot
tükrözi, és jelen feladat kifejezett kérésére (a meglévő bázisréteg
érintetlenül hagyása) nem lett javítva; külön nyomon követendő, ha az ND-27
hatóköre újra napirendre kerül.

**Verziózás: NEM seed-törő (egyelőre).** A `temperature_kelvin` (a jelenleg
C#-ban is élő, seed-hez kötött függvény) bitre változatlan. A
`temperature_kelvin_full` egy ÚJ függvény, aminek még nincs C#
megfelelője — amikor portolásra kerül, AKKOR válik ez a döntés seed-törővé
(a `Temperature.cs` numerikus viselkedésének módosítása), és akkor kell a
megfelelő verziószámot emelni, nem most.

**Hátralévő (nem ennek a feladatnak a hatóköre):** (1) a fenti négy
"MEGERŐSÍTÉST IGÉNYEL" paraméter felhasználói jóváhagyása vagy módosítása;
(2) `temperature_kelvin_full` C# portolása + `temperature_full_vectors.json`
KAT-ellenőrzése (`WorldGen.Core.Tests`); (3) `weather_deviation_k` egyesítése
a testvér-feladat `wind_precipitation_ref.py`-jával, amint az elkészül; (4) a
`docs/05-milestones.md`/`docs/backlog.md` frissítése (szándékosan NEM ennek a
feladatnak a része, hogy elkerüljük az ütközést a párhuzamosan futó
testvér-feladatokkal).

**Releváns fájlok:** `tools/reference/temperature_ref.py`
(`greenhouse_temperature`, `ocean_buffering_temperature`,
`weather_deviation_k`, `climate_cycle_temperature_k`,
`temperature_kelvin_full`), `tools/reference/temperature_full_vectors.json`,
`tools/reference/deterministic_math_ref.py` (ND-27), `tools/reference/
noise_ref.py` (`fbm`, ND-31/32/33), `docs/00-spec-v1.0.md` §25, §28, §29,
§32.

### ND-43 — M7 hátralévő rész (tavak, jég/hó, statikus A1 eróziós pass): modellezési küszöbök és egyszerűsítések

M7 hátralévő tétele (`docs/backlog.md`: "Tavak, jég/hó, eróziós visszahatás")
a `tools/reference/hydrology_ref.py` már kész priority-flood/flow
accumulation eredményére épül (`tools/reference/lakes_ice_erosion_ref.py`).
A spec (§35 Tavak, §36 Jég és hó, §18 Erózió) egyik résznél sem ad zárt
numerikus küszöböt vagy együtthatót — az alábbi döntések mind ebből a
hiányból fakadnak, és mind **egyszerű, statikus, egyetlen elevációmezőre
ható közelítések**, nem az M10 deep-time lánc része.

**1. Tavak (§35) — melyik tile "tó".**

A depresszió-feltöltés (priority-flood) melléktermékeként minden tile-ra
ismert a feltöltött ("víz-") szint. Egy tile tó, ha `filled > raw_elevation
+ LAKE_MIN_DEPTH_M` és nem óceán. A `LAKE_MIN_DEPTH_M = 0.5` (méter) egy
numerikus zaj-küszöb, nem fizikai állítás — enélkül a lebegőpontos
kerekítés miatt szinte minden sík tile "tóként" jelenne meg egy epsilonnyi
feltöltéssel. A tavakat a már verifikált `neighbor()` függvénnyel BFS-sel
összefüggő komponensekbe csoportosítjuk. A priority-flood korrektségi
tulajdonsága miatt egy medence belső tile-jai jellemzően egyetlen közös
feltöltött szintet (a kifolyási/sill-pont magasságát) kapják — ritka,
többszintű (teraszos) medencéknél ez nem szigorúan igaz, ezért a
`surfaceElevation` mezőt a komponens átlagaként adjuk vissza, a min/max
szórást pedig diagnosztikaként jelentjük (a script kiírja, hány "nem
egyszikű" tavat talált — a jelenlegi teszt-világon 0-t).

A tavak kialakulásának többi módja (gleccser, kráter, tektonikus medence,
folyóelzárás — §35 felsorolása) NEM külön logika: ezek már MOST is
implicit módon topográfiai mélyedésként jelennek meg a domborzatban (pl. a
becsapódási kráterek már bevésik magukat az elevációba), ezért ez a
detektor őket is megtalálja, csak nem a keletkezési ok szerint különíti el
— ez tudatos hatókör-szűkítés, nem hiányzó eset. A "tavak időben"
alfejezet (feltöltődhet, kiszáradhat, túlfolyhat, tengerrel kapcsolatba
kerülhet) időfüggő állapot, ezért NEM ennek a statikus passznak a része.

**2. Jég/hó (§36) — statikus osztályozás küszöbei.**

A §36.2 "Accumulation > Melt" feltételt egy éves átlaghőmérséklet-küszöbre
egyszerűsítjük. A `temperature_ref.temperature_kelvin` egy adott naphoz
(`day_t`) ad napi átlagot; ezt 12 ponton (`NUM_ANNUAL_SAMPLES`) tovább
mintavételezzük a keringési periódus mentén — ugyanaz az elv, mint a
`temperature_ref.py`-ban a napi mintavételezésnél (sűrű mintavétel zárt
formula helyett, mert az utóbbi szinguláris a pólusoknál).

- **Permanens jég**: éves átlaghőmérséklet `< 258.15 K` (-15 °C).
- **Szezonális hó**: az éves átlag e fölött van, de a leghidegebb
  mintavett hónap `< 273.15 K` (0 °C, a víz fagyáspontja — ez fizikai
  állandó, nem becsült érték).
- **Nincs**: egyik feltétel sem teljesül.

A -15 °C-os küszöböt **empirikusan illesztettük** a `temperature_ref.py`
jelenlegi paramétereihez (Föld-szerű napállandó, 23.44°-os tengelydőlés,
33 K fix üvegházhatás): a modul saját szélesség-táblázata szerint ez kb.
50-55 fok szélesség fölött ad permanens jeget, ami plauzibilis analógia a
valódi sarkköri jégsapkákhoz, de **nem hivatkozott klimatológiai
konstans** — csak ehhez az egyszerűsített hőmérséklet-modellhez illesztett
heurisztika. Ha a `temperature_ref.py` alapmodellje változik (pl. a
33 K-es fix üvegházhatás finomodik, ld. `docs/backlog.md` M5 tétele), ezt a
küszöböt újra kell hangolni.

A 36.3 gleccseráramlás-diffúzió (jégvastagság+lejtő alapú modell, ami
eróziót/völgyeket/morénákat/tengerszintet is befolyásol) **explicit módon
HALASZTVA** — ez önálló, nagyobb feladat, nem fér bele ebbe a statikus
osztályozási passzba.

**3. Statikus (A1) eróziós pass (§18.2) — proxyk és a "k" együttható.**

`ErosionRate = k · Rainfall^α · Slope^β · MaterialFactor` egyetlen additív
korrekcióként alkalmazva (nem idő-integrált differenciálegyenlet):

- `Rainfall` proxy → normalizált flow accumulation (`[0,1]`) — ugyanaz a
  proxy, amit a `hydrology_ref.py` már használ a folyó-küszöbölésnél.
- `Slope` proxy → `|raw_elevation(k) - raw_elevation(parent(k))|`,
  normalizálva a szárazföldi maximummal. A `parent` a priority-flood
  folyásirány-célpontja — ez a FELTÖLTÖTT magasság szerint monoton csökken
  a cél felé, a NYERS elevációkülönbség előjele ezért nem feltétlenül
  "lefele" mutat, ezért abszolút értékkel dolgozunk (csak a meredekség
  mértéke érdekel, nem az iránya).
- `MaterialFactor = 1.0` (konstans) — nincs még külön kőzettípus/litológia
  mező a specifikációban implementálva; ha lesz, ez lesz a csatlakozási
  pont.
- `α = 0.5`, `β = 1.0` — a "stream power law" (`E = K·A^m·S^n`, tipikusan
  `m≈0.5`, `n≈1`) néven ismert, a folyóvölgy-bevágódás modellezésében
  általánosan használt **egyenletalak** átvétele. Fontos: ez NEM egy adott
  publikációból idézett számérték, csak a függvény alakja — a tényleges
  "k" együtthatót (`EROSION_MAX_DEPTH_M`) önállóan kalibráltuk.
- `EROSION_MAX_DEPTH_M = 250.0` méter: az elméleti maximális egyszeri-pass
  bevágódás (amikor a normalizált accumulation ÉS slope is 1.0 — ez a két
  szélsőség a gyakorlatban ritkán esik egybe, a ténylegesen megfigyelt
  maximum ez alatt marad). Úgy választottuk, hogy a jelenlegi szárazföldi
  elevációtartomány (kb. 2500-3800 m, ld. `crust_elevation_ref.py`
  `OCEANIC_BASE_M`/`CONTINENTAL_BASE_M`/`NOISE_AMPLITUDE_M`) kis törtrészét
  tegye ki egyetlen statikus passzban — egy valódi folyóvölgy több
  geológiai kor alatt alakul ki, nem egy lépésben.
- `DEPOSIT_FRACTION = 0.3`: az eróziós anyag ekkora hányada rakódik le a
  KÖZVETLEN lefele-szomszédon (a priority-flood `parent`-jén), ha az
  szárazföld; a maradék 70% "tovább szállítódik" (ebben az egylépéses
  közelítésben egyszerűen elvész / a tengerbe jut, nem követi tovább a
  teljes láncot). Ha a parent óceán, az üledék a tengerfenékre kerül, ami
  NEM része ennek a szárazföldi elevációmezőnek.

**Fontos, verifikációkor felszínre került viselkedés:** a legnagyobb
flow-accumulationú tile (jellemzően a torkolat/delta közelében) SAJÁT
bevágódása kicsi lehet (ha ott a lejtő lapos), miközben a VÉGSŐ
elevációja mégis NŐHET, mert a felvízi (magas erózióhozamú) szomszédai ide
rakják le az üledék egy részét — ez fizikailag helyes viselkedés
(deltaképződés, ld. a spec 18.2 utolsó mondata: "Az üledék alacsonyabb
helyeken lerakódhat"), nem hiba. A `lakes_ice_erosion_ref.py` plauzibilitás-
assertjei ezért a nyers `erosion[]` szótáron ellenőrzik a formula
accumulation/lejtő-monotonitását, a végső mezőn pedig csak azt, hogy
valahol tiszta bevágódás, valahol tiszta feltöltődés történik.

**Ha ez a heurisztika téves iránynak bizonyul** (pl. Unity-vizuális
ellenőrzésnél irreálisan mély kanyonok vagy irreális tófelszín jönne ki),
ezt a bekezdést kell frissíteni és a küszöböket/együtthatókat újrahangolni
— ne csendben, kódban módosítva.

### ND-44 — M10 erózió idővel + eljegesedés-ciklusok: zárt alakú relaxáció numerikus PDE helyett, illusztratív klíma-forcing a T_cycle helyett

**Kérdés.** Az M10 hátralévő fele (`docs/05-milestones.md` M10 sora):
"Erózió idővel, eljegesedés-ciklusok". A spec §17 `dH/dt = UpliftRate -
ErosionRate` és §18.2 `ErosionRate = k · Rainfall^α · Slope^β ·
MaterialFactor` egyenleteket kellene deep-time-ban (`timeMyr`)
kiértékelhetővé tenni. Két probléma: (1) a §18.2 teljes alakja a `Slope`
tagon keresztül ÖNMAGA a domborzattól függ, ami idővel maga is változik —
ez csatolt, nemlineáris PDE, aminek nincs általános zárt megoldása; (2)
nincs kész `Rainfall` mező (a hidrológia, `hydrology_ref.py`, statikus,
egyetlen elevációs mezőn dolgozik) és nincs kész klíma-modul `T_cycle`
tagja (`temperature_ref.py` explicit halasztja).

**Az ND-04 (nyitott, "Timestep-invariancia toleranciái", M10-re
revideálandó) kontextusa.** Ez a munka pont az az M10 lépés, ami az
ND-04 revideálását kellene, hogy megalapozza. A tapasztalat: egy naiv
Euler-lépegetéssel megvalósított `dH/dt = k(H_eq-H)` (ld.
`_naive_euler_relaxation` a referenciában) UGYANAZON `t=365 Myr`
végpontra `n_steps=1`-nél `-4623m`-et, `n_steps=2000`-nél `432.6m`-et ad —
tehát **több ezer méteres eltérés** pusztán a lépésszám miatt, miközben a
"helyes" (zárt alakú) válasz `432.617069 m`. Ez konkrét, mért bizonyíték
arra, hogy az I1 determinizmus miért sérülne egy iteratív integrátorral:
két, egyébként azonos seedű világ MÁS eredményt adna, ha a mérnöki kód
más `timeMyr` felbontásban kérdezné le a mezőt (pl. a renderelő 50 Myr-es
lépésekben, egy teszt 1 Myr-esben).

**Döntés — 1. Erózió: lineáris relaxáció, ZÁRT alakban, NEM a teljes
§18.2 formula.** A hegység-relief (a lemezhatár statikus uplift-bónusza,
`plate_boundary_ref.boundary_uplift`) exponenciálisan relaxál egy
egyensúlyi érték felé:

```
H(t) = H_eq + (H0 - H_eq) * exp(-t / tau)
H_eq = EQUILIBRIUM_FRACTION * H0      (H0 = boundary_uplift(...), a t=0 M4 érték)
```

Ez a `dH/dt = (1/tau)(H_eq - H)` lineáris ODE egzakt megoldása — a
`Rainfall`/`Slope`/`MaterialFactor` szorzat-modell HELYETT egy egyszerűsített,
"topográfiai relaxációs idő" jellegű közelítés (a geomorfológiában használt
koncepció: egy reliefzóna karakterisztikus ideje, amíg megközelíti az új
egyensúlyi állapotát egy tektonikai perturbáció után). **A konkrét
paraméterek (`OROGENIC_RELAXATION_TAU_MYR = 50`, `EQUILIBRIUM_FRACTION =
0.35`) ILLUSZTRATÍV MODELLEZÉSI VÁLASZTÁSOK, NEM egy publikált geológiai
mérésből verifikált szám** — nincs a `tools/reference/kat_vectors`-hoz
hasonló hivatalos forrás egy "mennyi idő alatt erodálódik egy hegylánc a
felére" konstansra, ezért ez **explicit megerősítést igényel**, mielőtt
C#-ba kerülne (a python-reference skill "ha nincs hivatalos forrás,
jelezd és kérj megerősítést" szabálya szerint). A `t=0` eset bitre
(mért: `4.55e-13 m` numerikus zaj) visszaadja a meglévő statikus M4
eredményt (`elevation_with_boundary`) — nincs seed-törő hatás a meglévő
world-öknél `t=0`-nál.

**A `Rainfall`/`Slope`/`MaterialFactor` teljes csatolt modellje EXPLICIT
HALASZTVA marad** (nincs `rainfall_ref.py`, nincs iteratív domborzat-
visszahatás) — ez egy tudatos hatókör-szűkítés, nem hallgatólagos
egyszerűsítés, mert nincs jelenleg megvalósítható zárt alak rá, és egy
numerikus PDE-megoldó direktben sértené I1-et (ld. fent).

**Döntés — 2. Eljegesedés: önálló, illusztratív periodikus forcing, NEM a
valódi T_cycle.** `GlobalTempOffset(t) = A · sin(2π·t/T)`, `A = 6K`, `T =
150 Myr` — **szintén illusztratív, nem verifikált geológiai/csillagászati
adatból levezetett szám** (a valódi icehouse/greenhouse szuperkontinens-
ciklusok időskálája nagyságrendileg hasonló, de ez NEM azt jelenti, hogy a
150 Myr egy konkrét, forrásból idézett érték — explicit megerősítést
igényel). Egy idealizált, szélesség-lineáris hőmérséklet-profillal
(`T_EQUATOR_K`, `LATITUDE_TEMP_GRADIENT_K_PER_RAD` — szintén illusztratív)
kombinálva a jégvonal szélessége zárt alakban, analitikusan (nem numerikus
gyökkereséssel) számolható. `t=0`-nál az eltolás 0 (visszamenőlegesen
kompatibilis a statikus M5 hőmérséklet-modellel, ha valaki hozzáadja az
eltolást). **Ezt később egyesíteni kell a klíma-modul valódi `T_cycle`
tagjával**, ha az elkészül (`temperature_ref.py` docstringje explicit
"T_cycle halasztva"-ként jelzi) — ez a modul nem helyettesíti azt, csak
egy ideiglenes, önmagában is tesztelhető proxy addig.

**A két alrendszer szándékosan NINCS összekapcsolva** (pl. "jégkorszakban
gyorsabb a glaciális erózió") — ez egy további, külön dokumentálandó
modellezési döntés lenne, amit itt nyitva hagyunk.

**Timestep-invariancia bizonyítéka (I1/ND-04).** A `_relax_towards(h0,
h_eq, t, tau)` függvényt `n_steps` egyenlő részintervallumra láncolva
(`chain_relaxation`, minden lépésben az előző kimenet az új `h0`, de a
`h_eq` FIX marad az EREDETI `h0`-ból számolva) az exponenciális relaxáció
félcsoport-tulajdonsága (`exp(-a(t1+t2)) = exp(-a·t1)·exp(-a·t2)`) miatt
`n_steps ∈ {1,2,3,5,13,47,101,500}`-ra mérve **max `5.68e-14 m` eltérést**
adott az egylépéses direkt kiértékeléshez képest (`h0=1234.5`, `t=365
Myr`, direkt érték `432.6170692017 m`) — ez a dupla lebegőpontos
kerekítés zajszintje, NEM diszkretizációs hiba. **Fontos implementációs
csapda, amit menet közben találtunk és javítottunk:** az első próbálkozás
a `h_eq`-t minden lépésben ÚJRASZÁMOLTA a pillanatnyi (már relaxált)
`h`-ból (`h_eq = eq_fraction * h_pillanatnyi`) — ez elrontotta a
félcsoport-tulajdonságot, és a lánc `n_steps`-től függő, akár több száz
méteres eltérést adott (mért: `n_steps=2`-nél `421.75 m` eltérés az
egylépéses eredménytől). A javítás: `h_eq` a teljes láncon át FIX, az
EREDETI `h0`-ból számolva. Ez önmagában egy élő demonstrációja annak,
milyen könnyű csendben timestep-függő modellt építeni, ha az egyensúlyi
cél nem marad invariáns a felbontással szemben — pontosan az a hiba-
osztály, amire a feladatkiírás figyelmeztetett.

**Referencia:** `tools/reference/erosion_glaciation_deep_time_ref.py`.
Tesztvektorok: `erosion_glaciation_deep_time_vectors.json`
(`erosionVectors`: 400 minta `(face,level,u,v,plateId,timeMyr) ->
(elevation,isOceanic)`; `glaciationVectors`: 200 minta `timeMyr ->
(globalTempOffsetK, iceLineAbsLatitudeRad, isIced)`). A script kétszeri
futtatása bitre azonos JSON-t ad (ellenőrizve).

**Nyitott, megerősítést igénylő pontok (a C# port ELŐTT eldöntendő):**
1. `OROGENIC_RELAXATION_TAU_MYR = 50` és `EQUILIBRIUM_FRACTION = 0.35` —
   illusztratív, nem forrásból verifikált.
2. `GLACIATION_PERIOD_MYR = 150`, `GLACIATION_AMPLITUDE_K = 6` —
   illusztratív, nem forrásból verifikált.
3. Kell-e a glaciális erózió és az orogén relaxáció összekapcsolása
   (jelenleg szándékosan szétválasztva).
4. A `Rainfall`/`Slope`/`MaterialFactor` teljes §18.2 modell továbbra is
   halasztva marad — mikor (melyik milestone) kerüljön napirendre, és
   milyen zárt-alakú vagy dokumentáltan-elfogadott-kockázatú megoldással.

### ND-45 — Lemez-életciklus (M10+M11 összevonva): split/merge/rift ütemezése timestep-invariáns módon

**Kérdés.** A backlog két tétele ("M10 | Lemez-születés/-halál (§16) | Fix
plateCount a világ elejétől" és "M11 | Rift-zóna + lemez-hasadás/egyesülés |
A spec ezt folytonos modellként írja le") ugyanaz a spec-szakasz
(`docs/00-spec-v1.0.md` §16, 963-980. sor): `PlateSplitEvent`,
`PlateMergeEvent`, `SubductionTermination`, `RiftActivation`, `HotspotBirth`,
`HotspotDeath`, "a world seed és a geodinamikai állapot alapján ütemezve". A
spec **nem ad zárt képletet** ezekhez (szemben pl. a becsapódásokkal, ahol
Schmidt & Housen (1987) skálázás van, ld. ND-28) — minden időzítési/
geometriai szabályt itt kellett megtervezni. Referencia:
`tools/reference/plate_lifecycle_ref.py`.

**A vezérlő korlát: ND-04 (timestep-invariancia).** Egy futásidőben
akkumulált "stressz-számláló" (pl. "minden Myr-ben +x esély a hasadásra,
összegezve") **lépésköz-függővé tenné a történelmet** — más dt mellett más
esemény-idő jönne ki. Ehelyett minden lemez a **saját, zárt-formájú
"életrajzi sorsát"** egyetlen Threefry-hívásból kapja, kizárólag a saját
`plateId`-jából és a world seedből (`plate_lifecycle_roll`) — a "születési
idő" (mikor jött létre egy korábbi split révén) csak egy ADDITÍV ELTOLÁS a
már rögzített élethosszhoz, nem egy másik állapotfüggő bemenet.
`PlateTopologyAtTime(seed, t)` (`resolve_topology`) minden hívásnál a
TELJES leszármazási fát újraépíti a gyökerektől — nincs memoizálás, nincs
modul-szintű mutable állapot. Ezt konkrét számpéldával is bizonyítottuk: a
`world_seed=0xA7C944210000, plateCount=10` világban közvetlenül lekérdezve
`t=725 Myr`-t 11 aktív lemezt kapunk; ha előtte a kód "lépésenként"
(t=50, 123, 200, 333, 500, 600, 700 Myr) is lekérdezi az állapotot (mintha
egy step-based szimulátor lenne), a `t=725`-nél kapott lista **bitre
azonos** marad (pl. lemez 1 pozíciója mindkét esetben pontosan
`(-0.7312988446535255, 0.3106893665156318, 0.6071854060684712)`).

**Döntések (hatókör-szűkítés, a korábbi milestone-ok mintáját követve):**

1. **RiftActivation + PlateSplitEvent implementálva.** Minden lemez
   `plate_lifecycle_roll(seed, plateId)` hívásból kap egy `kind`
   (`split`/`merge`/`none`, valószínűségek `P_SPLIT=0.45`, `P_MERGE=0.25`),
   egy `lifespanMyr`-t (`[80, 400]` Myr sávból, a Wilson-ciklus
   nagyságrendje) és egy `riftFractionOfLifespan`-t (`[0.40, 0.85]`) — a
   `RiftActivation` időpontja `birth + lifespan*riftFraction`, korábbi mint
   maga a split (`birth + lifespan`). **Ezek a numerikus sávok (P_SPLIT,
   P_MERGE, lifespan-tartomány, rift-frakció-tartomány) NEM hivatalos
   geológiai forrásból verifikált értékek, hanem plauzibilitásra hangolt
   MVP világtervezési konstansok** — a CLAUDE.md "ha nincs elérhető
   hivatalos forrás, jelezd explicit" szabálya szerint ez itt explicit
   jelezve van, és **felhasználói megerősítést igényel**, mielőtt a C#
   portban "véglegesnek" tekintenénk.
2. **Split geometria: két új mag ± `SPLIT_HALF_ANGLE_RAD` (0.12 rad, ~6.9°)
   szögeltolással a szülő split-időponti pozíciójától, egy véletlen
   merőleges tengely körül.** Mivel a Voronoi-hozzárendelés a legközelebbi
   maghoz köt, ez a régi cellát a két új mag felező-síkja mentén
   automatikusan kb. felezi — nincs szükség explicit poligon-vágásra. Mérve
   (world_seed=0xA7C944210000, plate 0 split t≈184 Myr-nél, level 4
   Voronoi-mintavétel): szülő terület splitkor 0.1003 (a gömb töredéke), a
   két gyermek együtt 0.0618+0.0553=0.1172 közvetlenül utána — közel a
   szülőéhez, nagyjából egyenlő arányban osztva.
3. **PlateMergeEvent és SubductionTermination mechanikailag EGYSÉGESÍTVE**:
   mindkettő "lemez-eltávolítás" — a lemez magja egyszerűen törlődik, a
   területe a megmaradt szomszédok között a szokásos Voronoi-szabály révén
   automatikusan újraoszlik, nincs külön nyilvántartott "győztes" lemez. A
   spec fogalmilag megkülönbözteti a kettőt (két lemez egyesülése vs. egy
   lemez teljes elnyelése), de MVP-szinten a mechanika azonos — egy valódi,
   két lineage-t egyetlen továbbélő azonosítóba olvasztó egyesülés
   halasztva, mert tömeg-/fluxus-követést igényelne.
4. **HotspotBirth/HotspotDeath HALASZTVA.** A projektben egyáltalán nincs
   még hotspot-modell (sem statikus, sem dinamikus) — ez önmagában külön
   milestone-nyi munka. Csak a `RandomProperty` tartomány (17-19) van
   fenntartva a jövőre.
5. **Leszármazási fa mélysége `MAX_GENERATION=3`-nál levágva.** Ez véges
   korlát egy véges teszt-horizonton (elkerüli a korlátlan elágazást), NEM
   fizikai állítás arról, hogy a lemezek 3 hasadás után mindig
   stabilizálódnak.
6. **Új `PlateId` séma: gyökér-lemezek megtartják a kis szekvenciális int
   azonosítót (0..N-1, visszamenőleg kompatibilis az M4 statikus listával);
   gyermek-lemezek nyers 64-bites Threefry-hash-t kapnak azonosítóként**
   (`_block(...)` kimenetének első szava, nem [0,1)-be skálázva). Ez azt
   jelenti, hogy a `PlateId` típusának a C# portban `ulong`-gá kell válnia
   (feltehetően jelenleg `int`) — ez FÜGGETLEN a `TileId` bit-layout
   invariánstól, jelzés a core-dev felé a porthoz. Ütközés-valószínűség a
   szimuláció léptékén (≪10^6 csomópont) elhanyagolható.
7. **Új `RandomProperty` azonosítók a Tectonics doménben: 14 =
   LifecycleRoll, 15 = SplitAxisHint, 16 = ChildPlateId** (a meglévő 10-13
   után a következő szabad sorszámok) — MÉG NINCSENEK felvéve a
   `src/WorldGen.Core/Random/RandomDomain.cs`-be, ez a C# port feladata.
8. **Terület `estimate_areas`-ben a gömb felületének törtrészeként (0..1),
   nem abszolút km²-ben** — nincs még elfogadott bolygó-sugár-konstans
   ehhez a modulhoz (a `PlanetConstants.RadiusMeters`, ld. ND-28, csak a
   becsapódás-modulban létezik eddig).

**Indoklás:** ez a minta megegyezik minden korábbi milestone
hatókör-szűkítésével (ND-28, ND-29, ND-30 stb.) — kisebb, de tesztelhető,
timestep-invariáns MVP, explicit deferrállal és explicit jelzett,
megerősítést igénylő tervezési konstansokkal, nem csendes leegyszerűsítéssel.

**Verziózás:** ha ez a C# portba kerül, a `RandomDomain`/`RandomProperty`
bővítés (7. pont) és a `PlateId` típusváltás (6. pont) **seed-törő**
változás — verzióemelést igényel, ld. CLAUDE.md "Verziózás és
seed-kompatibilitás".

### ND-46 — Screen-space LOD metrika: távolság-alapú, NEM nézési-szög-tudatos (felfedezett korlát, nem hiba)

**Felfedezés (felhasználói jelentés, 2026-09-02, screenshot-alapú diagnózis).**
A felhasználó azt észlelte, hogy egy közeli zoomnál a látott bolygófelszín
egy része finoman, más része durván (bázis-szintű) tile-okkal jelenik meg,
FÜGGETLENÜL a kamera forgatásától/mozgatásától — azaz a mintázat a bolygó
FELSZÍNÉHEZ, nem a kamera nézőpontjához kötött (ezt kísérletileg is
megerősítettük: forgatás/ki-be zoom nem mozdítja a mintázatot). Több
hipotézist (hiszterézis-kaszkád, "olcsó előszűrés" alulbecslése, nézőkúp-
lefedettség szélsőséges képernyő-aránynál) sorra kizártunk mérésekkel és
egy standalone teszt-harness-szel (`AdaptiveQuadTree.BuildCut` közvetlen
hívása a pontos élő paraméterekkel). A végső, felhasználó által is
megerősített megfigyelés: a durvábban maradó (kék) terület a bolygó
LÁTHATÓ KÖRVONALÁHOZ/HORIZONTJÁHOZ közelebb esik, mint a finomabb (piros)
terület.

**Gyökérok.** Az `AdaptiveQuadTree.Visit()` finomítási döntése
`atan2(rTile, distance)` — a csomópont befoglaló-gömbjének SZÖGSUGARA a
kamerától mért EGYENES-VONALÚ 3D TÁVOLSÁG alapján. Ez a metrika NEM veszi
figyelembe a felület-normál és a nézési irány szögét (a rálátás
"súroló"-ságát). Egy görbült felszínen (bolygó) ez azt jelenti: a látható
körvonalhoz/horizonthoz közeli terep ÉRDEMBEN NAGYOBB egyenes-vonalú
távolságra van a kamerától, mint a "elölnézeti" terep — annak ellenére,
hogy a súroló rálátás miatt (erős foreshortening ellenére) még mindig
jelentős képernyő-területet foglalhat el. A rendszer emiatt a saját belső
logikája szerint HELYESEN dönt (kevesebb finomítás a távolabbi
csomópontoknak), de ez a döntés NEM követi a tényleges vetített
képernyő-méretet ezen a tartományon.

**Ez MÁR KORÁBBAN DOKUMENTÁLT, ismert hiányosság** — az
`AdaptiveQuadTree.DefaultMaxLeafCount` biztonsági-korlát doksija
(`unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/AdaptiveQuadTree.cs`)
explicit megemlíti: *"A GYÖKÉROK (a helyes screen-space terület-vetítés,
ami figyelembe venné a nézési szöget is) MÉG NINCS megoldva... dokumentáltan
NYITOTT következő lépés: helyes szög-függő/vetített-terület metrika vagy
explicit horizont-vágás."* Ez az ND csak formálisan rögzíti ezt a
korábban csak kód-kommentben élő nyitott kérdést, a felhasználói
diagnózis alapján megerősítve, hogy a hatása ÉRDEMBEN ÉSZLELHETŐ vizuálisan.

**Opciók (A opció VÁLASZTVA és MEGVALÓSÍTVA, ld. lent):**

| Opció | Előny | Hátrány / kockázat |
|---|---|---|
| **A: Nézési-szög-korrekció a metrikában** — `angularRadius` szorzása egy, a felület-normál/nézési-irány szögéből (`cosTheta`) származó tényezővel, hogy súroló rálátásnál is nagyobb "hatékony" szögsugarat adjon | Közvetlenül a gyökérokot orvosolja, viszonylag kis kódváltozás | A felület-normál kiszámítása (vagy közelítése a gömb-középponttól a tile-központig húzott sugárból) minden Visit()-hívásnál plusz költség; hiszterézis-interakció újra-tesztelendő |
| **B: Explicit horizont-vágás/-súlyozás** — a horizonthoz közeli tile-okat egy külön, EGYSZERŰBB szabály finomítja (pl. mindig legalább N szinttel a bázis fölé, ha látókúpban van) | Egyszerű, kiszámítható | Nem "helyes" fizikai metrika, csak tapasz; új konstans(ok) hangolást igényelnek |
| **C: Marad, ahogy van (dokumentált korlát)** | Nulla kockázat, nulla munka | A felhasználó által ténylegesen észlelt vizuális hiányosság megmarad |

**Megvalósítás (2026-09-02, A opció).** `AdaptiveQuadTree.Visit()`-ben, a
nyers `angularRadius` kiszámítása után: a felület-NORMÁL egy gömbön
egyszerűen a tile-középpont egységvektora (`(cx,cy,cz)/planetRadius`,
nincs külön költség), `cosGrazing` = a normál és a "csomópont → kamera"
irány skaláris szorzata. `cosGrazing <= MinUsefulCosGrazing` (0.02, kb.
88.9°) esetén a csomópont a látható felszín túloldalán van VAGY majdnem
tökéletesen súroló — ilyenkor azonnal "nem bővül"-ként zárjuk le (ingyenes
hátterlap-oldali korlátozás is egyben). Egyébként `effectiveAngularRadius
= min(angularRadius / cosGrazing, π/2)` — a π/2-es felső korlát UGYANAZ a
védelem, mint az `IsWithinViewCone` korábbi (8480cbe) javításánál, hogy a
korrekció maga ne okozhasson degenerációt. A `SelectCut` "olcsó előszűrése"
(gyökér-szintű távolság-alapú rövidzár) is frissítve: a
`maxRelevantDistance`-t el kellett osztani `MinUsefulCosGrazing`-gal,
különben pont azokat a távoli, de súroló szögű gyökereket zárta volna ki
idő előtt, amiket a korrekció finomítani akarna.

**Verifikáció (1. kör, `MinUsefulCosGrazing=0.02`):** a pontos élő
(felhasználó Console-DIAG-jából vett) kamera-paraméterekkel reprodukálva:
a javítás előtti `cut.Count=393651` (legmélyebb szint 9) helyett a
javítás után `cut.Count≈401286`, legmélyebb szint 15 — a súroló szögű
terület érdemben mélyebbre finomodik. Új regressziós teszt:
`GrazingAngleCorrection_RefinesFarButStillVisibleTerrain`
(`AdaptiveQuadTreeTests.cs`). A teljes meglévő tesztkészlet (23/23,
standalone `lod-verify` harness) és mindkét érintett Unity-assembly
(`Assembly-CSharp.csproj`, `WorldGen.Viewer.Lod.Tests.csproj`) fordítása
zöld/tiszta volt.

**ÉLŐ TELJESÍTMÉNY-REGRESSZIÓ (ugyanaznap, `useGpuGeometry` KIKAPCSOLVA
mellett is):** a felhasználó élesben `cut.Count=479661`-et és
`adaptiv ujraepites 2341.7ms`-et mért — használhatatlanul lassú, a
CPU-s renderelési út (nem a Fázis 3 GPU-probléma) nem bírta el a
megnövekedett tile-számot. **A hiba az volt, hogy a verifikáció CSAK a
`BuildCut` (kiválasztás) mélységét/méretét ellenőrizte, a RENDERELÉS
tényleges költségét sosem élő Unity Editorban** — ld.
`history/RETROSPECTIVE-2026-09-02-adaptive-lod-saga.md` a teljes
elemzésért és tanulságokért.

**2. kör (`MinUsefulCosGrazing=0.02` → `0.3`, jelentősen szigorítva).** A
standalone harness-ben lemért, konzervatívabb (0.3/0.2/0.12) jelöltek
közül a legszigorúbbat választva: a legutóbbi élő kamera-paraméterekkel
`cut.Count≈401484` (base+~8268, közel a régi, elfogadható tartományhoz),
legmélyebb szint továbbra is 15 ott, ahol ténylegesen súroló a rálátás —
sokkal kevesebb többlet-tile, mint a 0.02-nél. A teljes tesztkészlet
(23/23) és mindkét assembly fordítása továbbra is zöld/tiszta.
**EZ A HANGOLÁS MÉG NINCS ÉLŐ UNITY EDITORBAN MEGERŐSÍTVE** — a fenti
hiba tanulsága szerint ez explicit így is marad jelezve, amíg nincs élő
visszajelzés a renderelési költségről, nem csak a kiválasztási logikáról.

**Hatás/prioritás:** vizuális minőség, NEM determinizmus/I1-I4-sértés (a
`src/WorldGen.Core`-t nem érinti, tisztán `unity/WorldGenViewer` renderelési
döntés). Élő Unity Editor-teszt (mind a vízszintes sáv eltűnése, MIND a
renderelési sebesség) MÉG MEGERŐSÍTENDŐ.

**Verziózás:** nem seed-törő (a világmodellt nem érinti, csak a viewer LOD-
kiválasztást).

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/AdaptiveQuadTree.cs`
(`Visit()`, `IsWithinViewCone()`), `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`
(`RecomputeCutAndRebuildAdaptiveMesh`).

### ND-47 — Adaptív LOD újratervezés: a bejárás leválasztása a statikus base-ről + kétszámlálós költségvetés + fázisterv

**Állapot:** Fázis 1 **és Fázis 2 implementálva és a kiválasztási szinten
verifikálva** (standalone harness + EditMode tesztek). Egy élő Unity-mérési kör
megtörtént (2026-09-03) — ez vezetett a Fázis 2-höz. Fázis 3–5 **nyitott**. A
**render-simaság (Fázis 3) a fő hátralévő elintézendő** — ld. lent.

**Kontextus / a felfedezett probléma.** A felhasználói jelentés: a bolygó
felszíne zoomra „töredezett" marad, a tile-okat nem szedi 4 részre, holott a
mélység indokolná; a képernyő egy kisebb, *esetleges* (futásonként változó)
részén finomodik csak. A `docs/04-decisions.md` ND-40/ND-46 és a kód elemzése
két, egymásra rakódó gyökérokot tárt fel:

1. **A `maxLeafCount` biztonsági korlát (200 000) kisebb volt, mint a
   `adaptiveBaseLevel=8` base-gyökereinek száma (6·4⁸ = 393 216).** A régi
   `SelectCut` a base-szintről indult, és a `leafCounter` MINDEN meglátogatott
   csomópontot számolt — így a korlát már a base-felsorolás közben, garantáltan
   bevágott, mielőtt bármi érdemi finomodás történt. A `Parallel.For`
   nemdeterminisztikus sorrendje miatt a keret-kimerülésig eljutott ~51%
   véletlenszerű, foltszerű finomodást adott → ez az „esetleges" tünet.
2. **A fél-ív `ComputeHalfArcMinUsefulCosGrazing` küszöb** a látható felszín
   horizont-felőli felét eleve durván hagyta (másodlagos, kisebb hatás).

**Vezérelv (a helyes screen-space-error kvadfa).** Egyetlen metrika (vetített
pixel-hiba) dönt; a bejárás költsége a LÁTHATÓ, finomítandó régió méretével
arányos, NEM a bolygóéval; a részletesség folytonosan nő popping nélkül. Négy
pillér / öt fázis:

- **Fázis 1 — a bejárás gyökérszintjének leválasztása a statikus base-ről.**
  A `SelectCut`/`Visit` új paramétert kap: `traversalRootLevel` (alacsony
  induló szint, elesben 3) és `staticBaseLevel` (a statikus réteg fedi a
  base-t és alatta). A bejárás az alacsony szintről indul, és CSAK oda
  ereszkedik le, ahol a base FÖLÉ kell finomítani; a sík régióban a rekurzió
  azonnal leáll és SEMMIT nem emittál (azt a statikus réteg fedi). A base-alatti
  393 216 csempe így fel sem merül a bejárásban. **A default-ok visszafelé-
  kompatibilisek** (`traversalRootLevel = -1` → base; `staticBaseLevel = -1` →
  teljes partíció), a régi hívók/tesztek változatlanul működnek.
- **Kétszámlálós költségvetés.** A `maxLeafCount` mostantól a KIMENETET
  (emittált `level>base` leaf-ek, ≈ render-költség) korlátozza, nem a
  meglátogatott csomópontokat; a base-alatti leereszkedést a látókúp+horizont-
  culling korlátozza (screen-space). Külön, generózus `workCap`
  (= `maxLeafCount*8`) őrzi a rendszert bármilyen bejárás-robbanás ellen. Ez
  szünteti meg a starvation-t: a produktív (nadírhoz közeli) ág nem éhezik ki
  a base-alatti bejárás miatt.
- **Kiterjedés-tudatos horizont-cull.** Egy csomópont CSAK akkor esik ki
  horizont mögöttiként, ha a befoglaló-gömbje TELJESEN a `C·P = R²` horizont-
  sík mögött van (`cDotP + camLen·rTile < R²`) — így a durva, a nadír fölé
  nyúló, de középpontjukkal már horizonton túli tile-ok NEM esnek ki (ez volt
  egy megtalált hiba: közeli zoomnál a level-3 nadír-tile középpontja már
  ~10°-ra, a horizont ~6°-ra volt → a teljes nadír-oszlop kiesett). Az
  agresszív anizotrop (`1/cosGrazing`) korrekció csak a base-szinttől lefelé
  hat, ahol a tile már elég kicsi, hogy a középpont-alapú szög értelmes legyen.

- **Fázis 2 — prioritásos, költségvetett finomítás — IMPLEMENTÁLVA.**
  Az élő Unity-mérés (2026-09-03, `PerfLog_20260903_222334`) megmutatta, hogy a
  Fázis 1 után a cut `targetTilePixelSize=12`-nél (a felhasználó által beállított
  agresszív érték) legitim módon ~160–200 000 csempét kér → a hard output-cap
  bevágott, ismét *haphazard* (a nézett közép nem osztódott), és a render
  (`RebuildAdaptiveMesh` O(cut)) 4–6 s lett. Ezért a `SelectCutPrioritized`
  best-first (SortedSet-alapú prioritási sor, kulcs: effektív szögsugár-hiba,
  holtverseny `TileId.Value` — **egyszálú, determinisztikus**): a legnagyobb
  képernyő-hibájú (kamerához legközelebbi, központi) csempét finomítja előbb, és
  a `maxLeafCount` (= új `adaptiveRenderBudget` mező, default 25 000) kimerülésekor
  a periféria durvább marad. Ez megszünteti a haphazard-ot és tunolható fékké
  teszi a render-költséget. A régi mód (`staticBaseLevel<0`) a párhuzamos DFS-t
  tartja. **Fontos:** a cél-tuning ÖNMAGÁBAN nem old meg (48px → 0 finomodás
  ezen a zoomon, 12px → túl sok); a priorizálás + budget a helyes mechanizmus.

  **Fázis 2 korrekció (2026-09-03, második élő mérés — „nem jó helyen
  osztódik"):** az első prioritás-kulcs az `angularRadius / cosGrazing` (ND-46
  anizotrop) hiba volt — ez a SÚROLÓ (horizont-menti) csempéknek adott magasabb
  prioritást, mint a nadírnak, ezért a budget a horizont-sávra ment, nem oda,
  ahová a felhasználó néz (a kamera a logban ≈ nadírba nézett). Javítás: a
  prioritás és a felbontás-döntés a **nyers** `angularRadius`-ból (boost nélkül)
  születik → a legközelebbi (képernyő-középi, nadír) csempe kapja a budgetet
  előbb; a súroló csempék nyers szögmérete kicsi → kevés budgetet visznek. Ez
  egyúttal **csökkenti** a kért csempeszámot is (a boost ~7×-esen felfújta a
  grazing-sávot: 24px-nél 40k → 5,5k). Az `EvaluateNodeForPriority` már csak
  backface-cull-t (`cosGrazing<=0`) használ, agresszív `minUsefulCosGrazing`
  küszöböt nem. Verifikálva (harness H.3): a legmélyebb csempék átlagos
  `cosGrazing`-je 0,806 (a központ felé, nem a súroló sávban).
- **Fázis 3 — aszinkron BuildCut + EMIT worker szálon IMPLEMENTÁLVA; csak a
  mesh-upload main-thread.** (2026-09-04, 2. lépés.) Az élő mérés megmutatta, hogy
  a fix `adaptiveRenderBudget` (50k) a kötő korlát: közelebb zoomolva a mélyülő
  központ elszívja a budgetet a perifériától, ami emiatt VISSZAOLVAD (a „zoom
  közben összevonja a tile-okat" tünet). A megoldás: a budgetet nagyra kell
  venni, hogy ne legyen kötő — de ez csak akkor megfizethető, ha az EMIT (elesben
  ~230ms 50k-nál) nem a fő szálon fut. Mivel a `ComputeTileClassification` és a
  sarok/emit teljes lánc igazoltan TISZTA (Core + BodyFrameConversion axis-swap,
  nulla Unity-API; a GPU-klasszifikáció csak opcionális gyorsítás), a worker
  `Task` most a teljes geometriát előállítja (`ComputeAdaptiveMeshBuffersCpu`,
  `forceCpu` klasszifikáció), a fő szál pedig csak feltölti a Unity mesh-eket
  (`ApplyAdaptiveMeshBuffers`). Single-flight (a cache-írás így soha nem
  konkurens), robusztus fallback: bármilyen worker-hiba TARTÓSAN visszaáll a
  szinkron útra (`_asyncMeshRebuildDisabledAfterError`). Így a fő szál hitchje ~a
  mesh-upload (SetVertices, tíz-száz ms a méret függvényében). **A merge-on-zoom
  megszüntetéséhez a felhasználónak fel kell vinnie az `adaptiveRenderBudget`-et
  (~100-150k), most már megfizethető.** Trade-off: nagyobb budget → a worker
  tovább számol → a LOD kicsit jobban lemarad gyors mozgásnál (de a kamera sima).
  A `BuildCut` tiszta statikus függvény (nulla megosztott állapot/Unity-API), ezért
  worker szálon (`Task.Run`) fut — a kiválasztás költsége (magas részletnél
  200-590ms) így NEM okoz frame-akadást, a régi mesh látszik, amíg az új cut
  elkészül. Single-flight (`_cutTask`, egyszerre egy), az alkalmazás (a
  `RebuildAdaptiveMesh` = emit + Unity mesh-upload) a fő szálon, az `Update()`-ben,
  amint a task kész (`TryApplyCompletedAsyncCut`). Try/catch fallback: bármilyen
  hiba esetén a következő kör a régi szinkron úton fut. Kapcsoló:
  `useAsyncMeshRebuild` (default be; GPU-geometria mellett kikapcsol). **Hátralévő:**
  az EMIT (a geometria-dictionary-k építése, `RebuildAdaptiveMesh` compute-része)
  is worker szálra vihető (az emit-út igazoltan tiszta: BodyFrameConversion axis-
  swap + Core + cache, nincs Unity-API), csak a végső mesh-upload marad main-thread
  — ez viszi a fő szál maradék hitchét ~a mesh-feltöltésre. **Régi log-bottleneck** (a
  teljes rész):
  `RebuildAdaptiveMesh`, ami a TELJES dinamikus mesh-t újraépíti a fő szálon
  minden mozgásnál (`emitLoop` O(cut): 200k csempénél ~2 s, plusz classification/
  corners cache-miss az új csempékre). A `adaptiveRenderBudget` csak a hitch
  NAGYSÁGÁT csökkenti (kevesebb csempe), NEM szünteti meg (minden mozgásnál
  újraépít). Az igazi fix: az előző és új cut DIFF-jét számolva CSAK a
  hozzáadott csempékre emittálni geometriát, a többit változatlanul hagyni, és
  a `BuildCut`-ot worker szálon futtatni. Ez viszi a per-frame költséget
  O(cut)-ról O(változás)-ra — a `screen-space-lod` cél („ne akadozzon") ezen áll.
- **Fázis 4** — a fél-ív `MinUsefulCosGrazing` kemény levágás lazítása valódi
  horizont/hátlap-cull + büdzsé-rangsorra (a horizont-felőli realizmusért). **Nyitott.**
- **Fázis 5** — geomorph/skirt a popping és a dinamikus↔statikus LOD-ugrás
  (mérve: a grazing-frontnál akár 4 szint, DE lyuk nélkül — a statikus base
  mindig fed) simítására. Részben van már (`geomorphRangeFraction`). **Nyitott.**

**Miért NEM hidaljuk át a dinamikus↔statikus varratot a balance-ban.** A
`BuildStaticBaseLayer` a gömb MINDEN pontját mindig lefedi a level-8 statikus
meshsel; a dinamikus finomítás csak FÖLÉ rajzolódik. Ezért a határnál SOHA nincs
lyuk/rés — csak esetleges vizuális LOD-ugrás. Egy korábbi kísérlet (a statikus
ős promótálása a dinamikus rétegbe) átfedést okozott (mind a 4 gyereket
hozzáadta, akkor is, ha némelyik gyerek-régió már finomítva volt), ezért
elvetettük. A dinamikus↔dinamikus 2:1 balance megmarad (varratmentes mesh); a
statikus-varrat simítása Fázis 5.

**Verifikáció (kiválasztási szint).** Standalone harness (`dotnet`, Unity
nélkül — az `AdaptiveQuadTree` tiszta, `noEngineReferences`): régi mód
változatlan (base-8 pontosan 393 216, tiny-cap runaway megfogva, 6.29M
degenerált previousCut-ból visszaáll); új mód: távolról üres cut, közelről
`level>base`-re finomodik (base+3..5 mélységig), determinisztikus, korlátos
(~16k a ~400k+ helyett), nincs ős-leszármazott átfedés, dinamikus↔dinamikus
varrat ≤1. EditMode NUnit-tesztek hozzáadva ugyanerre (`Phase1_*`).

> **ELINTÉZENDŐ (a retrospektív fő tanulsága, kötelező):** egy LOD-változás
> NEM nyilvánítható „késznek" pusztán a kiválasztási teszt alapján — a
> megnövekedett/megváltozott tile-halmaz RENDERELÉSI (frame-) költségét CSAK
> élő Unity Editor-mérés adja meg. **Fázis 1-et élő Editorban meg kell mérni**
> (BuildCut + RebuildAdaptiveMesh + frame-idő, zoom és rotáció közben),
> mielőtt bármelyik további fázis (2–5) indul.

**Hatás/prioritás:** teljesítmény + vizuális minőség; **NEM seed-törő**, a
`src/WorldGen.Core`-t nem érinti (tisztán `unity/WorldGenViewer` renderelési
döntés, I1–I4 érintetlen). A Fázis 1 mellékesen JAVÍTJA a korábbi rejtett
nemdeterminizmust (a megosztott számláló szál-ütemezéstől függő cutját).

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/AdaptiveQuadTree.cs`
(`SelectCut`/`Visit`/`EmitLeaf`, `traversalRootLevel`/`staticBaseLevel`,
kétszámlálós korlát, kiterjedés-tudatos horizont-cull),
`unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`
(`adaptiveTraversalRootLevel` mező, `RecomputeCutAndRebuildAdaptiveMesh`),
`unity/WorldGenViewer/Assets/Tests/EditMode/Lod/AdaptiveQuadTreeTests.cs` (`Phase1_*`).

### ND-48 — Dinamikus mesh chunking: inkrementális (diff-alapú) mesh-frissítés a teljes újraépítés helyett

**Kérdés (felhasználói kérés, 2026-09-05): "a tile-ok még mindig nagyon
nagyok és lassan töltődnek be… ez lenne a legnagyobb feladat, amit meg
kellene ugrani."** Az ND-47 Fázis 1-3 (bejárás leválasztása, prioritásos
költségvetés, aszinkron worker) után a rendszer már nagyon agresszíven
hangolt (`targetTilePixelSize=12`, `adaptiveRenderBudget=120000`,
`adaptiveMaxLevel=18`), mégis a felhasználó továbbra is nagy, blokkos
tile-okat lát. A gyökérok: **minden egyes cut-változáskor a TELJES
dinamikus mesh (`DynamicRefined` GameObject) újraépül és feltöltődik**
(`Mesh.SetVertices`/`SetTriangles`), akkor is, ha a cutnak csak töredéke
változott a kamera kis elmozdulása miatt. A mesh-feltöltés Unity API,
tehát ELKERÜLHETETLENÜL a fő szálon fut, és költsége a TELJES feltöltött
csúcspont-számmal arányos — ez az igazi plafon, ami korlátozza, mennyi
tile fér bele egy frame-be `adaptiveRenderBudget` további emelése nélkül
(amit viszont a fő-szál-akadás korlátoz). Az ND-47 Fázis 3 dokumentációja
saját maga is ezt nevezte meg "az igazi fix"-ként, de sosem valósult meg.

**Kizárt alternatíva: `useGpuGeometry` bekapcsolása.** Van egy meglévő GPU
compute pipeline (`GpuTerrainGeometryGenerator.cs`), de a doksija explicit
leszögezi: "SZÁNDÉKOSAN NEM zéró-másolásos (nincs DrawProceduralIndirect) -
az eredmény visszaolvasódik a CPU-ra". A visszaolvasás
(`ComputeBuffer.GetData`) SZINKRON, fő szálon fut, és a `useGpuGeometry`
be is kapcsolva KIKAPCSOLJA az aszinkron worker-utat (ld.
`useAsyncMeshRebuild && !useGpuGeometry` feltétel) — tehát ez minden
frame-ben egy GPU-pipeline-stallt vezetne be a fő szálra, valószínűleg
ROSSZABB, nem jobb. Nem ajánlott megoldás.

**Döntés: chunkolt, diff-alapú inkrementális frissítés.** A dinamikus
cutot egy rögzített `dynamicChunkLevel` szintű ős szerint "chunk"-okra
bontjuk (`DynamicMeshChunking.GroupByChunk`, motorfüggetlen, `dotnet
test`-tel tesztelt - `tests/WorldGen.Viewer.LodChunking.Tests`). Minden
chunk a SAJÁT Unity GameObject/Mesh-ét kapja
(`GetOrCreateChunkRenderTarget`, `_dynamicChunkGameObjects` explicit
térkép). Két egymást követő keret chunk-csoportosítását összehasonlítva
(`DynamicMeshChunking.DiffChunks`, HashSet-egyenlőség a levélhalmazokon)
CSAK a ténylegesen változott (új vagy más levélhalmazú) chunk-ok kapnak
új, konkatenált geometriát és feltöltést — a változatlan chunk-ok
GameObject-jéhez a fő szál HOZZÁ SEM NYÚL. A már nem használt chunk-ok
deaktiválódnak (nem törlődnek - ha a kamera visszatér, a GameObject/Mesh
újra bekapcsolható, nincs újraallokáció).

**Hatókör (tudatosan szűkítve, mint minden korábbi M9/ND-lépésnél):** EZ A
LÉPÉS CSAK a fő terep-mesh-et chunkolja. A vízfelszín (`DynamicWater`) és a
határvonalak (`DynamicBorders`) TOVÁBBRA IS globálisan, minden
cut-változáskor teljesen újraépülnek (a hívó `EmitAdaptiveTile`-t minden
levélre meghívja, a nem-változott chunk-ok terep-kimenete egy eldobandó
"scratch" bucketbe kerül, hogy a víz/határ-adatuk ennek ellenére
elkészüljön). Ez dokumentált, nem hallgatólagos egyszerűsítés - ha a
víz/határ réteg is szűk keresztmetszetnek bizonyul (élő méréssel
igazolandó), külön lépésben chunkolható ugyanezzel a mintával.

**Biztonsági háló - kapcsoló + teljes-törlés új világnál.** `useChunkedDynamicMesh`
(alapértelmezett: be) - ha regressziót okoz, kikapcsolható, és a rendszer
visszaesik a régi, teljes-újraépítéses viselkedésre (a kód mindkét utat
megtartja). `InvalidateAdaptiveCaches()` (minden `Build()`-nél, azaz
világ-paraméter-változáskor fut) mostantól `ClearAllDynamicChunks()`-t is
hív - enélkül egy ÚJ világ (más seed/deepTimeMyr) chunk-jai a RÉGI
világ geometriáját mutatnák tovább minden olyan chunk-nál, amit az új cut
inkrementális diffje épp nem érintene.

**Verifikáció.** A chunk-csoportosítás/diff logika (`DynamicMeshChunking`)
motorfüggetlen (nulla `UnityEngine`-referencia, mint az `AdaptiveQuadTree`),
ezért `dotnet test`-tel közvetlenül tesztelhető Unity nélkül - 14 unit
teszt (`ChunkRootOf` határesetek, csoportosítás helyessége, diff minden
kombinációja: változatlan/új/megváltozott/törölt chunk, tisztaság). A
tényleges Unity mesh-feltöltési út (`ApplyAdaptiveMeshBuffers`,
`GetOrCreateChunkRenderTarget`) Unity-API-t használ, ezért ide NEM
alkalmazható a `dotnet test` - a szokásos módon, ÉLŐ Unity Editor
mérés/vizuális ellenőrzés szükséges (ld. a retrospektíva fő tanulsága:
"egy LOD-változás NEM nyilvánítható késznek pusztán a kiválasztási teszt
alapján"). **Ez MÉG NINCS élő Unity Editorban megerősítve.**

**Hatás/prioritás:** teljesítmény + vizuális minőség; NEM seed-törő (a
`src/WorldGen.Core`-t nem érinti). Ha élesben beválik, várhatóan
lehetővé teszi az `adaptiveRenderBudget` további emelését (jelenleg a
fő-szál-feltöltés a kötő korlát) anélkül, hogy minden kameramozgás
akadna - ez közvetlenül oldja a felhasználó által jelzett "nagy, pixeles
tile" panaszt.

**Releváns fájlok:** `unity/WorldGenViewer/Assets/Scripts/Viewer/Lod/DynamicMeshChunking.cs`
(új), `unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`
(`useChunkedDynamicMesh`, `dynamicChunkLevel`, `ComputeAdaptiveMeshBuffersCpu`,
`ApplyAdaptiveMeshBuffers`, `GetOrCreateChunkRenderTarget`,
`ClearAllDynamicChunks`), `tests/WorldGen.Viewer.LodChunking.Tests/` (új).

### ND-49 — Dendritikus folyó-nyomvonal: forrás + finom-szintű lejtő-követés lokális pit-escape-pel, a globális felbontás-emelés helyett

**Kérdés (felhasználói kérés, 2026-09-05, MAGAS prioritás).** A meglévő
hidrológia (`FlowNetwork`, M7) a REFERENCIA szinten (level 6-8) számol
priority-flood-ot és flow-accumulation-t - ezen a felbontáson (tile ~100-800
km) egy vízgyűjtő-területnek túl kevés tile jut ahhoz, hogy a kirajzolt
folyók valódi, elágazó mellékfolyó-mintázatot mutassanak: rövidek (mért
átlag: 5.9 tile), alig ágaznak. A felhasználó két megoldást vetett fel: (A)
lokális, csak a folyó-sáv mentén futtatott magas-LOD újraértékelés, vagy (B)
a globális `hydrologyLevel` emelése. B elvetve: level 10+-nál 6·4¹⁰ ≈ 6.3M
tile-ra nőne a mező, és a SZEKVENCIÁLIS priority-flood (`FlowNetwork.
PriorityFlood`, `SortedSet`-alapú) belátha­tatlanul lassulna ezen a méreten.
**A választás: A (lokális).**

**Módszer.** A REFERENCIA szinten (level 6) csak a folyó-FORRÁSOKAT
választjuk ki: csapadékos ÉS hegyvidéki tile-ok (`RiverPathTracing.
SelectRiverSources` - a meglévő `MoisturePrecipitation`-ból kapott
csapadék-mező felső percentilise ÉS egy tengerszint feletti magasság-küszöb
EGYSZERRE, top-K determinisztikus kiválasztással, döntetlennél
`TileId.Value` szerint). Minden forrásból EGYENKÉNT, egy SOKKAL FINOMABB
szinten (`fineLevel = level + fineDepth`, alapértelmezetten level 6+4=10)
lejtő-menti (steepest descent) lépésekkel követjük az utat az óceánig.

**A felfedezett probléma és a megoldás: lokális pit-escape.** Az első
(naiv, tisztán mohó lejtő-követéses) próba KATASZTROFÁLIS eredményt adott:
a finom szintű fraktál-zaj miatt a legtöbb forrás egy apró, zaj-méretű helyi
mélyedésbe akadt már néhány lépés után (mért: 8/12 forrás azonnal "pit",
átlagos hossz 5.9 tile - NEM lett jobb, mint az eredeti probléma!). Ez
PONTOSAN az az ok, ami miatt az eredeti hidrológia priority-flood-ot
használ naiv lejtő-követés helyett. **Megoldás:** minden "pit"-nél egy
LOKÁLIS, korlátozott csomópont-számú (`escapeNodeBudget`, alapértelmezetten
400) priority-flood (`RiverPathTracing.FindLocalSpillway`) keresi meg a
legközelebbi túlcsordulási pontot - UGYANAZ az elv, mint `FlowNetwork.
PriorityFlood`-ban, csak IGÉNY SZERINT, egy kis környékre futtatva, nem
előre az egész bolygóra. Ezzel a javítással: átlagos hossz 5.9 → 23.7 tile,
ocean-elérés 4/12 → 9/12, ÉS megjelent az első valódi összefolyás (merge)
is. Ez konkrét, mért bizonyíték arra, hogy a "docs-first, referencia-előbb"
munkarend miért éri meg: a Python-referencia futtatása FELTÁRTA a hibát,
MIELŐTT a C#-portba (vagy rosszabb esetben élő Unity-tesztbe) került volna.

**Hurokvédelem.** A pit-escape kereséssel a nyers eleváció NEM feltétlenül
csökken szigorúan MINDEN egyes lépésnél (egy meder rövid szakaszon át egy
alacsonyabb nyeregponton kelhet át, mielőtt tovább esne - mint a valódi
folyóknál) - ez elsőre egy REGRESSZIÓT is okozott (egy útvonal a saját,
korábban bejárt medrébe futott vissza, hurkot képezve). A javítás: a teljes
útvonal (fő ág + minden escape-kitérő) egy `pathVisited` halmazzal védett -
a lokális kereső SEM terjeszkedik, SEM fogad el túlcsordulási pontként már
bejárt tile-t. A strukturális garancia, amit ténylegesen ellenőrzünk (a
vektor-teszt): nincs ismétlődő tile, ÉS a végpont alacsonyabban van, mint a
kezdőpont (nettó esés) - NEM a szigorú lépésenkénti monotonitás.

**Dendritikus elágazás.** Ha egy KÉSŐBBI forrás útja egy MÁR MEGLÁTOGATOTT
(korábbi forrás által "lefoglalt", `claimed` térkép) finom tile-ba fut, ott
MEGÁLL ("merged") - a két folyó onnantól ugyanazt a meder-szakaszt
"használja" (a viewer két külön vonalszakaszként rajzolja, ami közös
végponton találkozik).

**Verifikáció.** `tools/reference/river_path_ref.py` (Python-referencia,
12 tesztvektor: forrás + útvonal + végződés-ok). A C# port
(`RiverPathTracing`, `tests/WorldGen.Core.Tests/Hydrology/
RiverPathTracingTests.cs`) BITRE EGZAKT egyezést ad a nyomvonal-követésre
(az eleváció-lánc már bizonyítottan bit-egzakt cross-platform, ld. ND-24/
ND-36) - a tesztek a Python-vektor RÖGZÍTETT forrás-listáját használják
(`BuildRiverNetworkFromSources`), FÜGGETLENÜL a csapadék-modell (nyers
Math.Sin/Cos, NEM garantáltan bit-egzakt) tolerancia-kockázatától; a
forrás-kiválasztás logikáját (`SelectRiverSources`) külön, szintetikus
adatos egység-tesztek fedik. 341/341 Core-teszt zöld.

**Hatókör (tudatosan szűkítve):** a forrás-kiválasztás a `MoisturePrecipitation`
SAJÁT (t=0, klíma-közelítés) elevációját/tengerszintjét használja, NEM a
deep-time-mozgatott domborzatot (mint a csapadék-overlay is, ld. M5
dokumentáció) - a TÉNYLEGES nyomvonal-követés viszont a JELENLEGI
(`_adaptiveSeeds`) lemez-pozíciókat kapja, hogy a megjelenített
domborzattal konzisztens legyen. A folyó-vonal réteg a régi, referencia-
szintű vízgyűjtő-fa-alapú rajzolást (`_adaptiveRiverTiles`/
`_adaptiveRiverParent`) VÁLTOTTA FEL a vizuális megjelenítésben; ezek a
mezők továbbra is számolódnak (a jövőbeli M8 panel-metrikákhoz még
hasznosak lehetnek), de a vonal-rajzolás már nem őket használja.

**Verziózás:** nem seed-törő (tisztán megjelenítési/kiválasztási döntés, a
`src/WorldGen.Core`-t motorfüggetlenül bővíti, I1-I4 érintetlen).

**Releváns fájlok:** `tools/reference/river_path_ref.py` (új),
`src/WorldGen.Core/Hydrology/RiverPathTracing.cs` (új),
`tests/WorldGen.Core.Tests/Hydrology/RiverPathTracingTests.cs` (új),
`unity/WorldGenViewer/Assets/Scripts/Viewer/PlanetGridMesh.cs`
(`_adaptiveDendriticRivers`, `BuildRiverNetwork`, a csapadék-blokk Build()-ben).

**Kiegészítés (2026-09-06, felhasználói KÖTELEZŐ elvárás): a tile-középpontok
összekötése helyett folytonos, kb. 10 m-es pontosságú nyomvonal.** A
felhasználó jelezte, hogy a ~13 km-es finom tile-középpontok Catmull-Rom
simítással összekötött vonala "rossz irányba" megy - a folyónak a tile-on
BELÜL, a kalkuláció szerinti pontos helyen kellene futnia, ~10 m-es
pontossággal.

*Módszer:* a durva (tile-láncolt) útvonal MEGMARAD a topológia (irány,
összefolyás, pit-escape) eldöntésére, de a MEGJELENÍTÉSHEZ minden egymást
követő durva tile-pár között egy FOLYTONOS, a felszín érintő-síkjában futó
lejtő-követő sétát végez (`RiverPathTracing.RefinePathContinuous`) - a MÁR
bit-egzakt pontszerű elevációfüggvényt (`ElevationAtPosition`, `tileIdValue`
paraméter NEM használt a jelenlegi képletben - a modell LOD-/rács-független)
tetszőleges, nem tile-rácshoz kötött pontban kiértékelve. Minden lépésnél N
jelölt irányt próbál az érintő-síkban (egyenletes szögosztás), a legjobbat
választva; helyi zaj-pöttyöknél (ahol egyik jelölt sem jobb) a durva célpont
felé lép tovább, hogy ne ragadjon be.

*Miért NEM egyszerűen a finom szint mélyítése:* a `FindLocalSpillway`
pit-escape keresés csomópont-költségvetése egy FIX FIZIKAI keresési
sugárhoz van hangolva a jelenlegi (~13 km-es) rácson - egy ~1300×-os
rácsfinomítás (13 km → 10 m) a keresési csomópontszámot a felülettel
arányosan, ~1300²-szeresére növelné ugyanahhoz a fizikai kereséshez,
használhatatlanná téve azt.

*Mért teljesítmény-korlát (kötelező mérés, nem becslés, ld. munkamódszer):*
12 folyóra, párhuzamosítva - 100m: ~1.9s, 50m: ~3.5s, 25m: **7.4s**, 10m
(a szigorú kért érték): **16.6s**. Ez a durva nyomvonal-követés (~125ms)
töredéke, DE összemérhető azzal a Build()-idővel, amit a session korábbi
részében (ND-48 előtti perf-munka) 60s-ról 15s-re vittünk le - egy
literális 10m-es szinkron lépés komoly regressziót jelentene minden
deep-time-váltásnál.

*Döntés (felhasználói választás a mért adatok alapján):* **HÁTTÉR-SZÁLON
(Task.Run) futó számítás, 50 méteres alapértelmezett lépésközzel.** A
számítás NEM blokkolja a Build()-et/a fő szálat - amíg fut, a régi (durva,
Catmull-Rom-simított) vonal látszik átmenetileg, majd a `LateUpdate()`-ben
(`TryApplyCompletedRiverRefinement`) a fő szál átveszi az eredményt és
újraépíti a folyó-mesh-t. Generáció-számlálóval védett (ha egy ÚJABB
Build() fut le, mire egy KORÁBBI finomítás befejeződik, az elavult
eredményt eldobjuk). A lépésköz (`riverRefinementStepMeters`) Inspector-
állítható, ha a felhasználó szigorúbb (pl. 10m) vagy lazább pontosságot
akar a sebesség rovására/javára.

*Kivétel a "referencia-előbb" munkarend alól (dokumentált, nem
hallgatólagos):* a `RefinePathContinuous` NEM kapott Python-referenciát -
új fizikai képletet NEM vezet be (a MÁR bit-egzakt pontszerű
elevációfüggvényt hívja), csak egy geometriai/algoritmikus (gyűrű-keresés
az érintő-síkban) lépést ad hozzá. Egy Python-oraklum futtatása (a mért
~7ms/pontszerű-kiértékelés Pythonban, több százezer kiértékeléssel) órákig
tartana. Helyette közvetlen, geometriai invarianciákat ellenőrző C#-tesztek
verifikálják (7 teszt: tisztaság, gömbön-maradás, végpontok pontossága,
lépésköz-monotonitás, éles esetek, végpontig-futás egy valódi folyón).

**Második kiegészítés (2026-09-06, ugyanazon a napon, felhasználói
visszajelzés utáni javítás): a fenti "hop-anchored" változat MÉG MINDIG
"tile-középpontok összekötésének" hatott.** A felhasználó a commitolt
verziót megnézve jelezte: *"nem jó ez a durva megjelenés... ne függjön
tile-októl... másképp nem elfogadható"*. Két, EGYMÁSTÓL FÜGGETLEN, méréssel
feltárt hiba állt emögött:

1. *Architekturális:* az első változat a durva tile-középpontokat KÖZBENSŐ
   KÉNYSZER-útpontként kezelte (minden hop végén explicit ráugrott a
   következő tile középpontjára) - ez, ha a finom keresés nem talált
   javulást, gyakorlatilag a régi, kifogásolt egyenes-szakaszos rajzolatot
   adta vissza, csak sűrűbb bontásban. **Javítás:** a köztes tile-pontok
   MOSTANTÓL EGYÁLTALÁN NEM szerepelnek útpontként - a séta kizárólag a
   forrás- és a torkolat-/nyelő-pozíció között, folytonos lejtő-követéssel
   halad.
2. *Numerikus (mérve, nem elméletben):* a folytonos séta bevezetése után
   egy diagnosztikai méréssel kiderült, hogy a lépésenkénti irányváltás
   ÁTLAGA ~170° volt - a séta gyakorlatilag lépésről lépésre oda-vissza
   lengett, nem sima kanyart rajzolt. Ok: egy fix (`stepMeters`) sugarú,
   "emlékezet nélküli" lejtő-keresés egy a lépésköznél szélesebb völgy
   aljánál a völgy egyik oldaláról a másikra pattog. **Javítás, két rész:**
   (a) az IRÁNY-érzékelés sugarát (`DefaultDirectionSensingRadiusMeters`,
   800 m) LEVÁLASZTOTTUK a tényleges lépésköztől (`stepMeters`, 50 m) - a
   lejtő irányát egy nagyobb, a finom zajt átlagoló körben keressük, a
   pozíció pontossága viszont változatlanul `stepMeters` marad; (b)
   IRÁNY-PERZISZTENCIA - a jelölt-irányok köréből kizárjuk azokat, amik
   `MaxHeadingTurnRadians`-nál (100°) jobban eltérnek az előző lépés
   irányától.

*Újabb mérési csapda (ugyanitt feltárva):* a javított verzió egyik közbenső
állapota a jelölt-irányokat a JELENLEGI pont elevációjához hasonlította
("csak akkor lépek, ha szigorúan jobb, mint ahol állok") - egy völgy
ALJÁN, ahol a jelenlegi pont már maga is keresztirányú helyi minimum, ez
majdnem MINDEN lépésnél "nincs javulás"-t jelzett, ami minden második
lépésnél elindította a drága, 64×-ig növekvő sugarú kiútkeresést: a teljes
12 folyós hálózatra a finomítás 3.5 s-ról **42 s-ra** nőtt. A végleges
megoldás a jelölt-irányokat EGYMÁSHOZ hasonlítja (a kúpon belüli
legalacsonyabb nyer, nem a jelenlegi ponthoz képest), a drága kiútkeresést
pedig csak akkor hívja, ha a végponttól vett távolság `StallWindowSteps`
(24) lépésen át NEM csökkent - ez a teljes hálózatra **7.3 s**-ra hozta
vissza a (párhuzamosított) számítási időt, a háttér-szálas
architektúra változatlanul hagyása mellett.

*Utólagos mérés (a végleges algoritmuson, a session korábbi 51 tile-os
referencia-folyóján):* lépésenkénti átlagos irányváltás 170° → **~40°**
(sima, folyamatos kanyar), a legnagyobb, egyenes vonaltól való oldalirányú
eltérés (kanyargás mértéke) ~13 km egy ~176 km hosszú folyón - fizikailag
plauzibilis meander, nem műtermék. Regresszió ellen új teszt:
`RefinePathContinuousTests.DoesNotOscillateBackAndForth` (átlagos
irányváltás < 90°-ra zárva).

**Harmadik kiegészítés (2026-09-06, ugyanazon a napon, MÉLYEBB diagnózis a
felhasználói kérésre: "a folyó nyomvonal minőségi javítása még nincs
befejezve, nézd meg alaposan").** A #2 pontban leírt, akkor "lezártnak"
hitt verziót egy dedikált C# diagnosztikai harness-szel (nem feltételezéssel)
lemérve a MÁSODIK verzió is súlyosan hibásnak bizonyult - de egy MÁS okból,
mint amit az irányváltás-átlag teszt (`DoesNotOscillateBackAndForth`) mérni
tudott.

*A mért tény:* a 12 referencia-folyóból az elsőn (51 finom tile, durva
lánc-hossz 551 km, forrás-torkolat légvonal 205 km) a finomítás **1804 km**
utat járt be - 3,3×-a a durva láncnak. Minden mért paraméter-kombinációnál
(50m/25m lépésköz, 800m/3200m/6400m érzékelési sugár) a séta **KIMERÍTETTE
a `maxSteps` korlátot**, és az utolsó lépés egy **~150 km-es mesterséges
"safety net" ugrás** volt a torkolatra (`if (AngularDistance(current, end) >
1e-12) refined.Add(end);`) - a séta SOHA nem érte el a végpontot
természetes úton. Az érzékelési sugár 8×-os növelése (800m→6400m) a
teljes úthosszat GYAKORLATILAG NEM változtatta (1804.18 → 1804.45 km) -
ez zárta ki, hogy finomhangolási kérdés lenne: az algoritmus STRUKTURÁLISAN
soha nem törekedett a végpont felé, kizárólag egy irány-kúpon belüli lokális
gradienskövetést végzett, ami egy nagy, enyhén zajos terepszakaszon
céltalanul bolyongott.

*Felhasználói válasz a diagnózisra - ÚJ tervezési elv:* ne legyen befagyasztott
lépésszám-korlát; a folyó kövesse a lejtőt, amíg TÉNYLEGESEN el nem ér egy
állóvízhez (óceánhoz vagy egy zárt medencéhez/tóhoz) - ha egy magasföldi tóba
fut, onnan (miután "megtelt") tovább kell folynia, amíg végül tengerhez ér.
Ez PONTOSAN a már meglévő, validált durva `TraceRiverPath`/`FindLocalSpillway`
elve (lejtő-követés + lokális priority-flood, ha elakad) - a korábbi hibás
verzió ezt az elvet ELVETETTE egy előre ismert végpont felé navigáló,
irány-perzisztens heurisztika javára.

*Új algoritmus (`TraceRiverPathContinuous` + `FindContinuousLocalSpillway`,
felváltja a törölt `RefinePathContinuous`-t):* nincs előre megadott végpont -
a séta a FORRÁSBÓL indul, és maga dönti el a természetes véget
(Ocean/Pit/Merged), pontosan a durva algoritmus mintáját követve:
1. **Normál lépés:** csak akkor lép, ha a jelölt-körben (nagyobb, a zajt
   átlagoló érzékelési sugárban) van a JELENLEGI pontnál SZIGORÚAN
   alacsonyabb pont - ez a döntő eltérés a korábbi ("körön belüli relatív
   legjobb", ami felfelé is léphetett) verzióhoz képest.
2. **Escape:** ha nincs alacsonyabb jelölt, egy VALÓDI lokális priority-flood
   (`FindContinuousLocalSpillway`) keresi meg a tényleges túlcsordulási
   pontot egy, a pit pontban rögzített érintő-síkbeli (i,j) egészrácson -
   ugyanaz az elv, mint `FindLocalSpillway`-ben, csak folytonos térben.

*Kalibrációs mérés (mért, nem becsült):* az escape-rács cellamérete
KRITIKUS - 50m-es cellával (a kirajzolási lépésközzel megegyezővel) egy
30 000-es csomópont-költségvetés csak ~8,7 km sugarú területet fed le,
ami 2/3 tesztelt forrást tévesen "Pit"-nek minősített, amit egy nagyobb
budget-tel (200 000, de ~9-12s/folyó) helyesen "Ocean"-ként zárt le. A
végleges megoldás: az escape-rács cellamérete `escapeCellMeters=2000m`
(16× nagyobb terület ugyanazzal a csomópont-számmal, mint 500m-nél) - ezzel
mind a 12 referencia-forrás helyesen, TERMÉSZETES úton ért véget (Ocean vagy
Pit, SOHA MaxSteps), összesen **~6,4 másodperc** alatt (szekvenciálisan,
mert a megosztott `claimed` térkép miatt a folyók nem párhuzamosíthatók,
ahogy a durva rétegnél sem). Az escape-szakaszok ritkák (folyónként 0-6
alkalom) és rövidek a teljes úthoz képest, ezért a nagyobb cellaméretük nem
áll össze látványos "durva" hatássá - a normál lépések (a folyó túlnyomó
része) változatlanul `stepMeters` (50m) pontosságúak.

*Tudatosan vállalt következmény:* mivel a folytonos elevációfüggvény
LOD-független, a finomabb (folytonos) keresés ELTÉRHET a durva (13 km-es
tile-átlagolt) döntéstől - a mért referencia-hálózatból két forrás, amit a
durva `TraceRiverPath` "Pit"-nek minősített (mert a 13 km-es tile-ok
elfedtek egy keskeny, ténylegesen lejtő hágót/nyerget), a folytonos verzió
helyesen "Ocean"-ként zárta le. Ez NEM hiba, hanem a már dokumentált
LOD-független modell következetes alkalmazása finomabb felbontáson - a
kontinuus réteg ÁTVESZI a topológiai döntés szerepét a MEGJELENÍTETT
hálózatra nézve; a durva réteg továbbra is a forrás-kiválasztáshoz és a
`claimed`-alapú dendritikus összefolyáshoz kell (amit a folytonos réteg is
megtart, saját, finom-tile alapú `claimed` térképpel).

*Verifikáció:* `TraceRiverPathContinuousTests` (a törölt
`RefinePathContinuousTests` helyén) - determinizmus, gömbön-maradás, forrás-
pozíció pontossága, ÉS a legfontosabb regresszió-védő teszt:
`ReachesNaturalTerminationNotMaxSteps` mind a 12 referencia-forrásra
(`[Theory]`), ami explicit ellenőrzi, hogy a termination SOHA nem
`MaxSteps` - ez a teszt a korábbi (hibás) implementációval MEGBUKOTT volna.
359/359 Core-teszt zöld.

*Verziózás:* nem seed-törő (tisztán megjelenítési döntés, ld. #1
kiegészítés indoklása - a `src/WorldGen.Core`-t motorfüggetlenül bővíti).

**Vizuális ellenőrzés Unityben MÉG MINDIG hátra** - ez a HARMADIK iteráció,
mérési bizonyítékkal alátámasztva, de élő Unity-nézetben még nem látott.

**HATODIK KIEGÉSZÍTÉS (2026-09-06, önálló munkamenetben, felhasználói
"a folyók már egészen jók" visszajelzés utáni két finomítási megjegyzés
egyikének diagnózisa)**: a backlogban rögzített "az útvonal a felületen
HELYENKÉNT MEGSZAKADNAK tűnik" megjegyzés gyökéroka megtalálva és
javítva. A `BuildContinuousRiverNetworkFromSources` a dendritikus
összefolyást egy `claimed: Dictionary<TileId, int>` térképpel oldja meg
(finom-tile → melyik folyó járt már ott) - amikor egy KÉSŐBBI folyó egy
MÁR CLAIMED finom-tile-ba lép, "Merged"-ként megáll. A PROBLÉMA: a
megálló folyó UTOLSÓ pontja csak "valahol EBBEN a finom-tile-ban" volt
(a saját mintavételi lépése alapján), NEM a befogadó folyó TÉNYLEGES
(folytonos térbeli) pontján - egy finom-tile mérete akár több száz méter
is lehet, tehát a két vonal a találkozásnál akár ennyivel is elválhatott
egymástól, vizuálisan "megszakadó útvonalnak" látszva. Ez PONTOSAN
egyezik az osztály-doksi EREDETI tervezési szándékával ("a hívó ezt egy
KÖZÖS PONTBAN végződő két vonalszakaszként rajzolja") - tehát valódi
regresszió/hiányosság volt, nem szándékos egyszerűsítés.

*Javítás:* a `claimed` térkép ÉRTÉKE egy új `ClaimedTileInfo` struct
(folyó-index + a TÉNYLEGES pozíció, amivel a lefoglaló folyó áthaladt
azon a tile-on) - amikor egy folyó "Merged"-ként megáll, az UTOLSÓ
pontja előtt hozzáadja ezt a TÁROLT, PONTOS pozíciót is a saját
útvonalához, mielőtt visszatérne. Ez a két vonal metszéspontját
PONTOSAN (nem csak "ugyanabban a durva tile-ban") közössé teszi - a
`ClaimedTileEndsAsMerged` teszt egy ÚJ, explicit egzakt-egyezés
assertion-nel ezt közvetlenül ellenőrzi (1e-12 toleranciával). A
`TraceRiverPath`/`BuildRiverNetworkFromSources` (a DISZKRÉT, `TileId`-
alapú, korábbi/durva algoritmus) NEM érintett - ott a "claimed" tile
MAGA a megosztott pont, nincs folytonos-térbeli rés.

*Hatókör:* csak a `RiverPathTracing.cs` (Core) belső logikája
változott, a Viewer (`PlanetGridMesh.BuildRiverNetwork`) hívási módja
VÁLTOZATLAN (a `claimed` map a `BuildContinuousRiverNetworkFromSources`-
on BELÜL, nem a hívó oldalán épül). 363/363 Core-teszt zöld (a bővített
`ClaimedTileEndsAsMerged` is, ami korábban NEM ellenőrizte az egzakt
egyezést - csak a bővített assertion bizonyítja, hogy a javítás
ténylegesen működik, nem csak "nem tört el semmit").

*Verziózás:* nem seed-törő (a folyó-VÉGPONTOK pozíciója marginálisan
változik az összefolyási pontoknál - ez tisztán vizuális pontosítás,
nem a világmodell állapotát érinti; a `ClaimedTileInfo` egy ÚJ,
publikus típus, nem számoz át semmit).

**Élő Unity-ellenőrzés hátra** - a MÁSIK nyitott finomítási megjegyzés
(a folyó-vonal szélessége nem korrelál a vízhozammal) TOVÁBBRA IS
nyitott, külön (nagyobb, mesh-alapú "szalag"-rajzolást igénylő) munka.

### ND-50 — Build()/renderelés szétválasztás: csak a felhő és a folyó-vonal kapott kivételt, a szél-/csapadék-overlay még nem (nyitott)

**Kérdés (önálló code-review-ban feltárt hiányosság, 2026-09-06).** A
felhasználói visszajelzés nyomán bevezetett `ApplyCloudOnlyRebuild`/
`CloudConfigChangedSinceBuild` (ld. fenti #4 kiegészítés az ND-49-hez,
illetve ugyanez a minta a `riverLineRadialBias`-ra is kiterjesztve) csak
KÉT tisztán vizuális paraméter-csoportot választott le a teljes
`WorldConfigChangedSinceBuild()`-ről. Egy alapos (8 szempontú, majd
verifikált) code-review rámutatott: a `windSpeedOverlay`,
`precipitationOverlay`, `windSpeedColorMaxMs`, `precipitationColorMax`
mezők TOVÁBBRA IS a teljes `WorldConfigChangedSinceBuild()`-en mennek át
- ha valaki ezeket kapcsolgatja/hangolja, MÉG MINDIG teljes `Build()` fut,
ami eldobja a háttérszálon futó, több másodperces folyó-finomítást,
UGYANAZZAL a mechanizmussal, amit a felhőre/folyó-vonalra már kijavítottunk.

**Miért nem oldottuk meg ugyanúgy, mint a felhőt/folyó-vonalat.** A felhő
és a folyó-vonal KÜLÖN RÉTEG (saját GameObject/Mesh), ami a MÁR meglévő,
cache-elt adatokból (csapadék-mező, illetve a finomított folyó-pontok)
olcsón újraépíthető a teljes statikus/dinamikus terep-mesh érintése
nélkül. A `windSpeedOverlay`/`precipitationOverlay` viszont NEM külön
réteg - a FŐ TEREP-MESH tile-jainak SZÍNÉT cseréli le (a biome-szín
helyett szél-/csapadék-szín), a színezési logika mélyen beágyazva fut a
statikus alapréteg ÉS a dinamikus (kamera-vezérelt) LOD-mesh
építésébe egyaránt. Egy "csak újraszínezés" gyors útvonal bevezetése
(a fizikai szimuláció - domain warp, lemez-hozzárendelés, eleváció -
újraszámítása NÉLKÜL) egy jelentős, a mesh-építési kódot mélyen érintő
refaktor lenne, amit NEM végeztünk el önállóan (a felhasználó jelenléte
nélkül) egy ilyen kritikus, sokat használt kódútra.

**Javaslat (nem implementálva, döntésre vár):**
- A) Külön "csak-újraszínezés" függvény bevezetése, ami a MÁR kiszámolt
  eleváció-/óceán-/biome-mezőkből újraszínezi a statikus+dinamikus
  mesh-eket a szimuláció újrafuttatása nélkül - a `windSpeedOverlay`/
  `precipitationOverlay`-t is a cloud-mintához hasonló, olcsó ágra
  terelné. Nagyobb munka, de általánosan megoldja a problémát.
- B) Amint az altitude-szempontú review is javasolta: a folyó-finomítási
  generáció-számláló invalidálását ne ahhoz kössük, hogy "fut-e
  `Build()`", hanem közvetlenül ahhoz, hogy a finomítás TÉNYLEGES
  bemenetei (forrás-lista, tengerszint, lépésköz) változtak-e - ekkor
  BÁRMILYEN jövőbeli, pusztán vizuális paraméter automatikusan
  ártalmatlan lenne, hand-maintained snapshot-párok szaporítása nélkül.
- C) Egyelőre hagyjuk így (a szél-/csapadék-overlay ritkán használt
  fejlesztői/diagnosztikai kapcsoló, nem a fő munkafolyamat része) - csak
  dokumentáljuk a korlátozást (ez a jelen bejegyzés).

**Javaslat:** B) a legrobusztusabb (kizárja az egész hibaosztályt jövőre
nézve is), de nagyobb refaktor a `_riverRefinementTask`/generáció-kezelés
körül - felhasználói megerősítést igényel, mielőtt hozzákezdenénk.

**Verziózás:** nem seed-törő (tisztán teljesítmény-/UX-kérdés).

---

**LEZÁRVA (2026-09-20): A) megvalósítva, lényegesen olcsóbban, mint a fenti
becslés.**

A bejegyzés azt feltételezte, hogy A)-hoz "a mesh-építési kódot mélyen érintő
refaktor" kell. Ez **tévedés volt**: a `BuildStaticBaseLayer()` már akkor is
önálló volt, és kizárólag a CACHE-ELT világállapotból dolgozik
(`_adaptiveSeed` / `_adaptiveSeeds` / `_adaptiveCraters` / `_adaptiveSeaLevel`
+ a terrain-bázis cache) — **szimulációt nem futtat**. Elég volt tehát:

1. az öt overlay-mezőt (`windSpeedOverlay`, `precipitationOverlay`,
   `tectonicPlateOverlay`, `windSpeedColorMaxMs`, `precipitationColorMax`)
   kivenni a `WorldConfigChangedSinceBuild()`-ből egy saját
   `OverlayConfigChangedSinceBuild()` / `SnapshotOverlayConfig()` párba —
   ugyanaz a minta, mint a felhőnél és a folyó-vonalnál;
2. egy `ApplyOverlayOnlyRebuild()`, ami üríti a szín-gyorsítótárakat és
   **csak** a statikus alapréteget építi újra, a dinamikus chunkokat pedig az
   `_adaptiveConfigDirty` viszi;
3. a klasszifikáció újrahasznosítása (`BuildStaticBaseLayer(reuseClassifications: true)`):
   a tile-besorolás az elevációból/tengerszintből/biome-ból jön, overlay-től
   független — ez önmagában 453 ms volt.

**Mérve élő Editorban** (level 8 alapszint, 396 294 statikus sarok):

| overlay-váltás | régi (teljes `Build()`) | új (overlay-út) | folyó-generáció |
|---|---|---|---|
| tektonikus / csapadék / overlay ki | 3147–3826 ms | **897–1595 ms** | régen bumpolt, most **változatlan** |
| szél-overlay be (drága színezés) | 5718 ms | **2986 ms** | **változatlan** |

A sebesség a kisebbik nyereség. A lényeg, hogy a folyó- és felhő-generáció
**érintetlen marad**, tehát az overlay kapcsolgatása többé nem dobja el a
háttérszálon futó, többmásodperces folyó-finomítást — ez volt a bejegyzés
eredeti panasza.

**Egy meglévő rés is bezárult.** A `_waterCornerColors` (a vízfelszín
sarok-színei, szintén overlay-függő) eddig CSAK a teljes világ-reset során
ürült. Amíg minden overlay-változás teljes `Build()`-et indított, ez nem
látszott; az olcsó úton látszana. Most az `InvalidateOverlayColorCaches()`
egy helyen üríti az összes overlay-függő szín-cache-t.

**B) TOVÁBBRA IS ÉRDEMES, de már nem sürgős.** A mostani javítás a KONKRÉT
öt mezőt kezeli. Ha valaki a jövőben új, tisztán vizuális paramétert vesz fel
a `WorldConfigChangedSinceBuild()`-be, a hibaosztály visszatér. B) (a
generáció-invalidálást a finomítás TÉNYLEGES bemeneteihez kötni) ezt
szerkezetileg zárná ki.

### ND-51 — Csillagos háttér + látható Nap: determinisztikus, de NEM a világmodell része

**Kérdés (felhasználói kérés, 2026-09-06).** "mi a helyzet azzal hogy a
hatter a csillagos eg legyen" - a backlog már rögzítette ezt a tételt
(M3/M13, Alacsony/Kicsi), egyetlen nyitott kérdéssel: vonatkozik-e az I3
("nincs kézzel festett textúra, minden pixel a világmodellből
következik") a háttér-csillagokra, amik NEM a bolygó része, hanem a
Naprendszeren KÍVÜLI, dekoratív kontextus.

**Válasz (felhasználói döntés):** procedurális, determinisztikus
csillagmező, ÉS emellett explicit két új követelmény: (1) a csillagok a
bolygó tengelyforgása miatt LÁTHATÓAN mozogjanak, (2) legyen egy LÁTHATÓ
Nap-korong is, aminek a szöge az ÉV során (évszakok) változzon.

**Felfedezés implementáció közben: a Nap-mechanika MÁR LÉTEZETT.** A
`SunController.cs` (Directional Light-ra tett komponens) MÁR a
`WorldGen.Core.Astronomy.OrbitalMechanics.SunDirectionBodyFrame`-et
hívta minden képkockán, saját `currentTimeDays`/`autoAdvance`/
`daysPerSecond` idő-akkumulátorral - ez pontosan a hiányzó M3 vizuális
cél ("megvilágított gömb, terminátorral - évszakok látszanak a
terminátor mozgásán") kész, de eddig LÁTHATATLAN (csak a fény irányát
állította) megvalósítása volt. Erre ÉPÍTETTEM, nem újraterveztem.

**Miért NEM a világmodell része, mégis determinisztikus (a `RandomDomain`
bővítése).** A `src/WorldGen.Core/Random/RandomDomain.cs`-ben új,
KÜLÖN tartomány: `RandomDomain.Decorative = 32` (+ `RandomProperty.
StarPosition = 40`, `StarBrightness = 41`) - explicit dokumentálva, hogy
erre NEM vonatkoznak ugyanazok a seed-kompatibilitási garanciák, mint az
1-8 (világmodell-) domainekre, mert a csillagkép nem a szimulált bolygó
állapotának része (nincs mit checkpointolni/verifyelni rá). A
`DeterministicRandom.SampleUnitVector3`/`Sample` MÁR verifikált
primitíveket hívja újra (nincs új hash-függvény) - a hozzáadás pusztán
additív enum-bővítés, NEM számoz át/használ fel meglévő értéket, tehát a
CLAUDE.md "seed-törő változás" kritériuma NEM teljesül (nem kell
verzió-emelés).

**Geometriai/forgatási konvenció (ld. kód-kommentek részletesen,
`StarField.cs`/`SunController.cs`):**
- A projekt konvenciója: a terep-mesh test-keretben FIX, a nap/éj
  ciklust a Directional Light FORGATÁSA szimulálja (nem a mesh forog).
- A csillagok (közelítőleg) az inercia-keretben fixek - hogy a FIX
  terephez képest mégis "végigsöpörjenek az égen" a bolygó
  tengelyforgása miatt, a csillag-mezőnek a nap napi (tengelyforgás-
  eredetű) szögével MEGEGYEZŐ nagyságú, de ELLENTÉTES irányú forgást
  kell kapnia a pólustengely (Unity Y, ld. `BodyFrameConversion`) körül.
  `SunController.ApplySunDirection` ugyanabból a `rotationPeriodDays`/
  `rotationPhase0`/`currentTimeDays`-ból számolja ezt a szöget, amit a
  Nap-irányhoz is használ - a napi ÉS évi (szezonális) mozgás UGYANABBÓL
  az idő-változóból adódik, a helyes (fizikai) arányban, mert mindkettő
  ugyanazon a `OrbitalMechanics.SunDirectionBodyFrame` hívásláncon megy át.
- A csillagok geometriája (kis, gömb-középpontból KIFELÉ néző kvadok,
  nem kamera-követő klasszikus billboard) és a Nap-korong pozicionálása
  (a bolygó-középponttól a Nap-irányba, fix távolságra) EGYSZERŰSÍTÉS -
  a csillagszféra sugara (5000) sok ezerszerese a kamera lehetséges
  elmozdulásának, ezért vizuálisan megkülönböztethetetlen egy valódi
  kamera-billboardtól, de nem igényel per-frame újraszámítást minden
  csillagra.

**Shader:** `WorldGen/StarUnlit` (`StarUnlit.shader`) - a MEGLÉVŐ
`CloudUnlit`/`VertexColorUnlit` "best-effort HDRP CG" mintáját követi
(egyszerű CGPROGRAM, nincs élő Unity-teszttel megerősítve), additív
keveréssel (`Blend One One`), a vertex-szín hordozza a fényességet -
mind a csillagokhoz, MIND a Nap-koronghoz újrahasznosítva (nincs
szükség két külön anyagra egy ilyen egyszerű MVP-hez).

**Scope/korlátok:**
- A `SunController` SAJÁT (a `PlanetGridMesh.climate*` mezőktől
  FÜGGETLEN, ld. a `SunController`-t megelőző eredeti fejléc-kommentet)
  orbitális paramétereit használja - ha a kettő szét van hangolva
  (pl. eltérő `axialTiltDegrees`), a látható Nap/csillagok NEM feltétlenül
  egyeznek a klíma-alapú terminátor-effekttel. Ez MÁR ÍGY volt a
  `SunController` eredeti tervezésében, nem ezzel a munkával vezettük be.
- A scene-be történő tényleges bekötés (a `StarField` GameObject
  létrehozása, a `SunController` új mezőinek - `sunVisual`, `starField`,
  `planetTransform` - Inspector-beli kitöltése) NEM történt meg
  scene-YAML-szerkesztéssel (a kockázata - hibás fileID/GUID - nem érte
  meg egy élőben nem tesztelhető változtatásnál) - ez felhasználói
  Unity-Editor lépés, ld. a session-napló pontos utasítását.

**Verziózás:** nem seed-törő (a `RandomDomain`/`RandomProperty`
bővítés ADDITÍV, nem számoz át semmit; a `src/`-et érintő rész
motorfüggetlen marad).

**Élő Unity-ellenőrzés MÉG NINCS** - ez egy vadonatúj vizuális modul,
build-ellenőrizve (a generált Assembly-CSharp.csproj-hoz manuálisan
hozzáadott `StarField.cs` bejegyzéssel - Unity a saját scene-megnyitásakor
úgyis újragenerálja ezt a fájlt, tehát ez csak ideiglenes, helyi
ellenőrzési segédlet volt), 363/363 Core-teszt zöld.

### ND-52 — Másodlagos, finom-léptékű részlet-zaj a közeli zoom laposságára

**Kérdés (felhasználói kérés, 2026-09-07).** "fraktál zaj generálás
nagyon közeli zoom esetén nem jó. bizonyos tile-okat azért nem bont meg,
mert nincs fraktál zaj ami indokolná ezt" - a felhasználó egy MÁSODIK,
alacsony amplitúdójú zajréteg bevezetését kérte, ami a tile-ok "eredeti
mérete szerint" 40×40 tile-on ismétlődik, ugyanazzal az algoritmussal,
mint az elsődleges dombormlat-zaj.

**Vizsgálat: a tünet oka MÁS, mint a felhasználó feltételezése, de a
kért megoldás helyes.** Az `AdaptiveQuadTree` tile-felbontási döntése
(`Lod/AdaptiveQuadTree.cs`) TISZTÁN képernyő-téri/szögméret-alapú
(`Math.Atan2(rTile, distance)` vs. küszöb) - NEM néz zaj-tartalmat, tehát
"nem bontja meg, mert nincs zaj ami indokolná" szó szerint NEM ez
történik. A VALÓS ok: a `CrustElevation.BaseElevation` elsődleges
`FractalNoise.RidgedMultifractal`-jának (BaseFrequency=8, 5 oktáv,
lacunarity=2) legfinomabb oktávja is még több tucat km hullámhosszú,
miközben a renderelt adaptív LOD ENNÉL sokkal mélyebbre bontja a
geometriát (a legmélyebb szinteken egy tile akár méteres nagyságrendű) -
így a legmélyebb LOD-szinteken a felszín a domborzat-zaj szempontjából
GYAKORLATILAG SIMA, függetlenül attól, hány geometriai háromszögre van
felbontva. A felhasználó által kért megoldás (egy magasabb frekvenciájú,
alacsonyabb amplitúdójú második zajréteg) ERRE a valós okra is helyes
válasz, csak a diagnózis pontosítása fontos a jövőbeli hangoláshoz.

**Megoldás:** `CrustElevation.SecondaryDetailNoise` - UGYANAZ a már
verifikált `FractalNoise.RidgedMultifractal` primitív (nincs új
hash-függvény, ld. `DomainWarp` azonos precedense), de:
- **Frekvencia:** a periódus `SecondaryNoisePeriodTiles=40` darab, a
  renderer `PlanetGridMesh.cs` alapértelmezett, adaptív felbontás ELŐTTI
  statikus rács-szintjének (`level=5`, dokumentáltan "a tile-ok EREDETI
  mérete") megfelelő tile-nyi. Átszámítás: egy kockalap éle kb. π/2
  radiánt fed le, 2^level tile-ra osztva → egy referencia-tile szögmérete
  kb. (π/2)/32 rad; a periódus ennek 40-szerese; a `FractalNoise`
  frekvenciája a periódus reciproka (ld. `FractalNoise.Fbm`/
  `RidgedMultifractal` doksija: a frekvencia közvetlenül szorozza a
  bemeneti koordinátákat, tehát hullámhossz = 1/frekvencia).
- **Amplitúdó:** `SecondaryNoiseAmplitudeMeters=200.0` (az elsődleges
  `NoiseAmplitudeMeters=3000.0` kb. 1/15-e - "jóval alacsonyabb", ahogy a
  felhasználó kérte).
- **Dekorreláció:** fix koordináta-eltolással (`SecondaryNoiseOffsetX/Y/Z`),
  ugyanaz a minta, mint a `DomainWarp` három komponensének
  dekorrelációja - különben a második réteg csak erősítené/gyengítené az
  elsőt ugyanazokon a helyeken, nem adna FÜGGETLEN részletet.
- **NEM kapja meg a `MountainMask`-ot** (szándékos, dokumentálva a
  kódban) - épp a "sík" régiókban a legfontosabb, hogy legyen közeli-zoom
  textúra, a maszk pont ott nyomná el a legjobban.
- Az óceáni szelídítés (`OceanicNoiseFactor`) ugyanúgy vonatkozik rá.

**Ez SZÁMSZERŰEN MEGVÁLTOZTATJA a `BaseElevation` kimenetét MINDEN
pozícióra** - a CLAUDE.md "Bármely szimulációs algoritmus numerikus
viselkedésének módosítása" kritériuma teljesül. Nem vezettünk be külön
verzió-mezőt (nincs ilyen a projektben elevációra - ld. ND-31/33/34
precedens, amik szintén hangolták a formulát ND-dokumentálással, külön
version-gate nélkül, mert a rendszer mindig újragenerál seedből, nem
savegame-kompatibilitást őriz). A Python referencia
(`tools/reference/crust_elevation_ref.py`) EGYIDEJŰLEG frissült, és a
`crust_elevation_vectors.json`, valamint a rá épülő
`erosion_glaciation_deep_time_vectors.json` és `plate_boundary_vectors.json`
KAT-vektorok mind ÚJRAGENERÁLVA lettek (mindhárom Python-referencia
hívja `base_elevation`-t) - ez KÖVETI a CLAUDE.md "a Python a helyes"
elvét, nem egy új algoritmus, hanem egy MÁR verifikált primitív
újrafelhasználása más paraméterekkel, tehát NEM igényelt friss
Python-oráklum-tervezést a nulláról.

**Nyitott, dokumentált feltételezés:** a "tile-ok eredeti mérete" =
`PlanetGridMesh.level=5` (a Viewer statikus, adaptív felbontás előtti
alap-rácsa) - ez egy ÉSSZERŰ, de NEM az egyetlen lehetséges értelmezés
(az ND-02 Core-oldali "szimuláció bázis-LOD"-ja level 6). Mivel a két
konstans (`SecondaryNoiseReferenceLevel`, `SecondaryNoisePeriodTiles`)
külön áll, élő Unity-visszajelzés alapján szabadon újrahangolható KAT-
vektor-újragenerálás nélkül is (csak az AMPLITÚDÓ/FREKVENCIA hangolása
nem érinti a KAT-fájlokat, ha a felhasználó a jelenlegi arányt jónak
találja - de MAGÁT a formulát/paramétert módosítani újra KAT-regenerálást
igényel, ld. fent).

**Élő Unity-ellenőrzés MÉG NINCS** - ez egy Core-only numerikus
változtatás, build+teszt-ellenőrizve (ld. session-napló), de a
VIZUÁLIS hatást (van-e érdemi különbség a legmélyebb zoom-szinten) csak
élő Unity Play-módban lehet megerősíteni.

**Kiegészítés (2026-09-07): GPU-oldali port + fordítási-idő regresszió,
VÉGSŐ MEGOLDÁS.** Code review feltárta, hogy `TileClassification.compute`
(a Viewer `useGpuClassification`/`useGpuGeometry` gyorsítóútja) NEM
kapta meg a másodlagos zajt - pótolva (`SecondaryDetailNoiseF`, 1:1 a
frekvencia/amplitúdó/eltolás paraméterekben). EZUTÁN a felhasználó élő
Unity-ben "Compiler timed out" hibát kapott a `CSGenerateTerrainGeometry`
kernelre - ez a kernel szálanként 5x hívja az elevációt (1 közép + 4
sarok, a HLSL fordító által jellemzően teljesen kifejtett ciklusban), és
a hozzáadott 3 oktáv × 5 hívás × 8 sarok-hash már túl sok volt a
fordítónak. ELSŐ javítási kísérlet: a GPU-oldali oktáv-szám 3→1-re
csökkentve, plusz egy `[loop]` attribútum a sarok-ciklusra - a
felhasználó ÚJRA tesztelte, UGYANAZT a "Compiler timed out" hibát kapta.
**Ahelyett hogy tovább próbálkoznánk verifikálhatatlan félmegoldásokkal,
a `BaseElevationF` TELJESEN VISSZAÁLLÍTVA az ND-52 ELŐTTI formulára**
(`git diff HEAD` szerint a függvény törzse bájtra megegyezik a legutóbbi
commit-tal, csak egy magyarázó kommentár maradt) - a GPU-port
VÉGLEGESEN, SZÁNDÉKOSAN NEM tartalmazza a másodlagos zajt. Ez egy
elfogadott, dokumentált korlát: a `useGpuClassification`/`useGpuGeometry`
bekapcsolásakor a GPU-úton számolt eleváció a finom részlet-zaj nélkül,
valamivel simább, mint a CPU-é (ami a TÉNYLEGES, alapértelmezett
renderelt geometriát adja - a GPU-gyorsítás jelenleg alapból KI van
kapcsolva). Tanulság: két egymást követő, magam által nem
verifikálható HLSL-fordítási-idő-optimalizálási kísérlet helyett a
BIZTOSAN MŰKÖDŐ állapotra való teljes visszaállás volt a helyes döntés,
amint az első próbálkozás nem vált be.

**Kiegészítés (2026-09-07): amplitúdó-újrahangolás, tervezési hiba
korrigálva.** A felhasználó jelezte: "nem jött be... a második szintű
zaj... lehet e az egész síkra kiterjedő folytonos zajt hozzáadni?".
Utólagos számolás feltárta, hogy az EREDETI ND-52 terv számolási hibán
alapult: `SecondaryNoisePeriodTiles=40` × egy level=5 tile szögmérete
együtt ~1.9635 radián, ami egy teljes nagykör ~0.3125-öd része -
SZÉLESEBB periódus, mint akár az elsődleges zaj BÁZIS-oktávja
(periódus=0.125 rad). A "másodlagos, közeli-zoom finom részlet-zaj"
koncepció tehát TÉVES premisszán alapult - a zaj sosem adott finom
részletet, csak egy nagyon halvány (200m), regionális léptékű
hullámzást, ami minden zoom-szinten gyakorlatilag láthatatlan maradt.

Mivel a periódus MÁR EGYFAJTA egész-felszínt átfogó, folytonosan
ismétlődő lépteket ad, a javítás NEM a frekvencián, hanem KIZÁRÓLAG az
amplitúdón múlt: `SecondaryNoiseAmplitudeMeters` 200→900m (az
elsődleges 3000m kb. 30%-a). A funkció neve/paraméterei változatlanok
(elkerülve egy újabb átnevezési kaszkádot), de a dokumentált SZEREPE
mostantól "másodlagos, folytonos, regionális léptékű domborzat-
textúra", nem "közeli-zoom részlet". Ugyanaz a teljes KAT-vektor-
regenerálási lánc futott le, mint az első ND-52 körben (lásd fent) -
a kontinens-szám 37→34-re módosult, a `TestEarth001` habitability-
sávja Moderate→Low-ra tolódott (a víz-arány/kontinens-szám invariáns
továbbra is teljesül). A `PlanetView.unity` scene stale (a session
korábbi kód-alapérték-változásait nem követő) `surfaceAmbient`/
`surfaceSpecularStrength`/`surfaceShininess` mezői is közvetlenül
frissítve a scene-fájlban - ez magyarázza, miért nem volt észrevehető
az éjszakai-sötétítés javítás sem (egy már szerializált Inspector-
érték nem frissül automatikusan kód-alapérték-változáskor).

### ND-53 — HDRP Bloom küszöb (threshold=0) volt a "folyó/jég brutálisan csillog" jelenség valódi oka, NEM a terep-anyagok

**Kérdés (felhasználói visszajelzés, 2026-09-06 óta ismétlődő,
2026-09-07-én lezárva).** A folyó- és jég-felületek fény-visszaverődése
a felhasználó szerint "mintha villámlana" - túl erős, nem folyamatos.
Négy egymást követő javítási kör (a `VertexColorUnlit.shader`
Blinn-Phong `_SpecStrength`/`_Shininess` csökkentése 0.30/24-ről
0.12/8-ra; a `River`/`SeaIce` kategóriák HDRP/Lit `_Smoothness`-ének
beállítása 0.08-ra, amit korábban a kód SOHA nem állított be; a
`PlanetView.unity` scene stale, kód-alapérték-változás előtti
szerializált értékeinek frissítése; végül DIAGNOSZTIKAI KÍSÉRLETKÉNT
mindkét anyag specularis/smoothness paraméterének NULLÁRA állítása)
**egyik sem oldotta meg a problémát** - a felhasználó screenshotot
küldött (`pics/p.png`), ami bebizonyította, hogy a csillogás a
specStrength=0/Smoothness=0 állapotban IS változatlanul erős maradt.

**Gyökérok.** A screenshoton a "csillanó" foltok kerek, lágy-szélű,
glóriás, "kifehéredett" jellege NEM Blinn-Phong/PBR spekuláris
csillanásra utalt (ami tile-diszkrét, keskeny fényfoltokat adna),
hanem HDRP BLOOM post-processing-re. A projekt globális HDRP
alapértelmezés-profiljában (`unity/WorldGenViewer/Assets/Settings/
HDRPDefaultResources/DefaultSettingsVolumeProfile.asset` - ez
érvényesül, mert a `PlanetView.unity` scene-ben NINCS külön `Volume`
GameObject/felülbírálás) a **Bloom `threshold` (küszöb) értéke `0`
volt**. Ez azt jelenti, hogy GYAKORLATILAG BÁRMILYEN nem-teljesen-
fekete felület hozzájárul a bloom-hatáshoz, nem csak a szándékosan
HDR-fényes elemek (pl. a Nap-korong, `StarUnlit.shader` `[HDR]`
`_Color` + `_Brightness` szorzó, ami akár 20x-osra is felmehet). Mivel
a Bloom egy POST-PROCESSING effekt a VÉGSŐ renderelt pixel-
fényességre, TELJESEN FÜGGETLEN attól, hogy a fényesség diffúz vagy
spekuláris eredetű - ez magyarázza, miért volt HATÁSTALAN minden
anyag-szintű próbálkozás (a Bloom ugyanúgy bevilágította a jeget/vizet
akár volt specular, akár nem). A jég (közel-fehér, `IceSheet` szín
≈(0.95, 0.96, 0.98)) és a napfényes víz a jelenet LEGFÉNYESEBB LDR-
tartományú felületei - ők lépik át elsőként és legerősebben egy ilyen
kritikusan alacsony küszöböt.

**Javítás.** `threshold` 0 → 1.05 (`DefaultSettingsVolumeProfile.
asset`). Ez a normál, `[0,1]` LDR-tartományú terep/víz/jég színeket
MÁR NEM engedi bloomolni, de a Nap-korong (ami `_Brightness`-sel
szándékosan 1.0 fölé van tolva) TOVÁBBRA IS bloomol, ahogy az eredetileg
kívánt volt (ld. checklist 2. pont, "Nap-korong fényessége"). A
korábban diagnosztikai célból lecsökkentett/nullázott terep-anyag
paraméterek (`surfaceSpecularStrength`, `FlatMaterialSmoothness`)
visszaállítva az eredeti, ésszerű kalibrációra (0.12/8, ill. 0.08) -
ezek soha nem voltak hibásak, csak a Bloom maszkolta el a hatásukat.

**Tanulság.** Egy vizuális "csillanás" tünet ELSŐ RÁNÉZÉSRE a
leginkább kézenfekvő, hasonló nevű mechanizmusra (anyag-specular)
utalt, és 4 kör alatt sem derült ki tévesen, MERT minden egyes
anyag-szintű változtatás valóban VALAMENNYIT csökkentette a
látványt is (a bloom-hatás input-fényessége részben az anyag saját
diffúz+specular kimenete) - csak a VALÓDI, domináns forrás (a
post-processing lánc) sosem került szóba, amíg egy DÖNTŐ, nulla-
állapotú kísérlet (screenshot-tal dokumentálva) véglegesen ki nem
zárta az anyagokat. Ez a projekt saját "ne találgass, szerezz konkrét
bizonyítékot" elvének egy újabb megerősítése - itt a bizonyíték egy
felhasználói screenshot volt, nem egy Debug.Log.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-beállítás,
a Core-t nem érinti). Élő Unity-ellenőrzés hátra.

**✅✅✅✅✅✅✅✅✅✅✅ VÉGLEGES LEZÁRÁS (2026-09-09), TELJES BLOOM-
KIKAPCSOLÁS:** a jelenség UGYANEBBEN a formában (pólusi/vízfelszíni
túlexponálás) többször is visszatért a threshold-emelés (1.05→1.4)
ELLENÉRE - a döntő diagnosztikai adat: a "Tavak+jég" kikapcsolása a
nyílt vízfelszínt tárta fel, ami MÉG ERŐSEBB túlexponálást adott, noha
számolással a vízfelszín-shader kimenete (specular=0.02, sötét
alapszín) messze a Bloom-küszöb alatt kellene maradjon - ez kizárta,
hogy a rendes diffúz+specular lánc lenne a magyarázat. Végső, döntő
lépés: `Bloom.intensity` 0.2→**0** (a teljes effekt kikapcsolva, nem
csak a küszöb hangolva). Felhasználói visszaigazolás: **"tök jó, végre
nem csillognak a pólusok! megoldottad"**. A 2026-09-06 óta tartó,
20+ körös "csillogás" vizsgálat ezzel VÉGLEGESEN LEZÁRVA - retrospektíve
a Bloom hatása minden korábbi hangolásnál (threshold/scatter emelés)
erősebbnek bizonyult, mint amit egy csökkentett, de aktív Bloom
kezelni tudott; csak a teljes kikapcsolás oldotta meg. **Nyitott
kérdés a jövőre**: ha valaha legitim HDR fényforrás (pl. a látható
Napkorong) Bloom-glóriáját vissza szeretnénk kapni, azt a Bloom
UJRA bekapcsolásával, de a terep/víz shaderek kimenetének SZIGORÚBB
[0,1] tartományra clamp-elésével kellene megoldani, nem a jelenlegi
(bizonyítottan elégtelen) küszöb-hangolással.

### ND-54 — Folytonos felszín-színezés a "Full" hőmodellt használja, NEM a "Simple"-t; GPU-klasszifikáció kikapcsolva

**Kontextus:** a 13-16. körös "folyó/jég villódzás" vizsgálat során
(ld. `history/2026-09-06-lod-rivers-clouds-session.md`) egy ÚJABB,
minden korábbitól ELTÉRŐ jelenség került elő: egy stabil, a bolygó
forgásával együtt mozgó **sárga folt pontosan az északi pólusnál**.
Ez NEM rendering-hiba (nem NaN, nem shader-degenerálódás) - számolással
igazolt, valódi szimulációs eredmény.

**A jelenség oka:** a folytonos felszín-színezéshez (`ContinuousCornerColor`
és az adaptív LOD tile-klasszifikáció) használt `Temperature.
TemperatureKelvin` ("Simple" képlet, ld. `src/WorldGen.Core/Climate/
Temperature.cs`) NEM tartalmaz jég-albedó visszacsatolást vagy óceáni
hő-puffert - azok csak a "Full" modellben vannak (`TemperatureKelvinFull`,
ND-42/§28.1). Sarki NYÁR alatt (a tengely a Nap felé billen) a Nap SOHA
nem nyugszik le a pólusnál - a napi átlagos `cos(θ)` ott folyamatosan
magas marad, míg az egyenlítőnél az idő kb. felében leáll (éjszaka).
Kiszámolva a jelenlegi 23.44°-os tengelydőléssel: **pólus (sarki nappal)
≈ 319K (46°C)** vs **egyenlítő (napi átlag) ≈ 303K (30°C)** - a Simple
képlet szerint a pólus MELEGEBB, mint az egyenlítő, ami átlépi a
`TemperateThresholdK`-t (293.15K), "Tropical" (sárga) színt adva a
fizikailag leghidegebbnek szánt pontnak.

**Kapcsolódó inkonzisztencia:** a fő biome-besorolás (a kontinens-lista/
panel "dominant: X" mezője) UGYANEZT a Simple képletet használta - tehát
a panel és a renderelt szín korábban KONZISZTENSEN hibás volt együtt
(nem vették észre, mert a panel csak az AGGREGÁLT domináns biome-ot
mutatja, egy kis sarki "Tropical" folt eltűnik az átlagban).

**Döntés:** mind az 5 hívási hely (`ComputeTileClassification`, a GPU-
klasszifikáció CPU-fallback ága, `PrecomputeCornersInParallel`,
`ContinuousCornerColor`, a habitability-számítás) egységesen egy új
`PlanetGridMesh.TemperatureKelvinAt(...)` segédfüggvényen át
`TemperatureKelvinFull`-t hív, `tYears = deepTimeMyr * 1e6`-tal és a
`_adaptiveSeed`-del (a T_weather zajhoz). Ez egyszerre javítja a
felszín-színt ÉS teszi konzisztenssé a panel-besorolással.

**GPU-klasszifikáció kikapcsolva (`useGpuClassification: 1→0` a
`PlanetView.unity`-ban).** A `TileClassification.compute` GPU-shader
saját, portolt Simple-képletet használ (`TemperatureKelvinF`) - a Full
modell GPU-portolása (időjárás-zaj + Milankovics-ciklusok + 12-mintás
óceáni átlag) valószínűleg reprodukálná az ND-52-ben már dokumentált
GPU shader-fordítási időtúllépést (ahol egy JÓVAL kisebb bővítés miatt
is vissza kellett vonni a változtatást). Felhasználói döntés (2026-09-09,
3 opció közül választva): a biztos, azonnal helyes CPU-path-ot
választottuk a kockázatos GPU-portolás helyett. **Nyitott, dokumentált
korlát**: ha valaki később újra bekapcsolja a `useGpuClassification`-t,
a GPU-oldali klasszifikáció/szín ismét a Simple képletet fogja
használni (a sarki nyár-jelenség visszatér) - a GPU-shader Full-portolása
külön, elkülönült feladat, csak akkor éri meg, ha a CPU-path teljesítménye
nem elég egy nagyobb rácsfelbontáshoz.

**Teljesítmény-megjegyzés:** a Full modell drágább, mint a Simple (extra
`AnnualMeanRadiativeTemperature` 12-mintás hívás OCEANI pontokra - de
a `ContinuousCornerColor`/`TemperatureKelvinAt` hívások SOHA nem
oceáni pontra futnak, mert az óceáni ág korábban visszatér
`ContinuousOceanRockColor`-ral, tehát ott a t_ocean tag mindig 0,
extra költség nélkül; a `ComputeTileClassification`/GPU-fallback ágakon
viszont IGEN, ott érdemi extra költség jelentkezhet óceáni tile-oknál).
Élő teljesítmény-ellenőrzés hátra (`adaptiveRebuildWarningMs` naplóval).

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render/klasszifikációs
változás, a Core-t/a mentett világállapotot nem érinti - a `Temperature.
TemperatureKelvinFull` már létező, tesztelt Core-függvény, nem új
algoritmus). Élő Unity-ellenőrzés hátra.

**❌ UTÓJEGYZET, UGYANAZNAP: VISSZAVONVA teljesítmény-regresszió miatt.**
Élő tesztelés során a felhasználó jelezte: a fenti váltás után "az egész
bolygó tök sötét" lett. Valószínű ok: `ClimateCycleTemperatureK` és
`GreenhouseTemperature` **pozíciófüggetlen** értéket adnak (kizárólag
`worldSeed`/`tYears`/`ghgPpm` függvényei, az `(x,y,z)` paraméterektől
teljesen függetlenek), MÉGIS a `TemperatureKelvinFull` minden egyes
híváskor újraszámolja őket - és mivel ezt a hívást `ContinuousCornerColor`/
`ComputeTileClassification`/stb. **SORONKÉNT, SAROKONKÉNT** hívja (a
`PrecomputeCornersInParallel` könnyen százezres nagyságrendű hívásszámot
jelent), a hozzáadott 2× `Threefry4x64` hash + 3× `SinCos` hívás
hívásonként valószínűleg katasztrofális lassulást okozott - a mesh-build
feltehetően sosem futott le rendesen, ezért maradt a bolygó "sötét"
(befejezetlen/hiányos geometria-frissítés, nem tényleges fényezési hiba).

**Visszaállítva**: `TemperatureKelvinAt` ismét a "Simple" `Temperature.
TemperatureKelvin`-t hívja, `useGpuClassification` vissza `1`-re. A
"sárga pólus" jelenség (a döntés eredeti oka) emiatt VISSZATÉRHET - ez
egy külön, még nyitott tétel.

**Helyes következő lépés (NEM implementálva)**: a Full modell
pozíciófüggetlen tagjait (`tGreenhouse`, `tCycle`) egy `Build()`-enkénti
CACHE-elt mezőben egyszer kiszámolni (a `worldSeed`/`tYears`/`ghgPpm`
úgyis csak world-generáláskor/deep-time-csúszka-mozgatáskor változik),
és a per-vertex hívásba már csak a valóban pozíciófüggő tagokat
(`tRadiative`, `tAltitude`, `tWeather`) átadni. Ez megőrizné a
pólus-javítást ÉS a teljesítményt is - de ez egy külön, alaposabb
implementációs feladat, nem oldottuk meg ma.

### ND-55 — Lejtő-érzékeny, DE tile-határok között folytonos felszín-normál (veges differencia, megosztott sarok-cache-ben)

**Kontextus:** a 13-16. körös NaN-vizsgálat (ld. ND-54 fölötti szakasz)
után a 15. kör a felszín-normált a durva, quadonkénti cross-product
számításról egy tisztán gömb-irányú (`normalize(pozíció)`) közelítésre
váltotta - ez NaN-biztos volt, de a felhasználó jelezte: "elveszett a
vizuális magasság érzete" (a domborzat lejtés-árnyalása eltűnt, minden
pont úgy fényezett, mintha tökéletes gömb lenne). A lejtő-érzékeny
cross-product normál visszaállítása (helyes NaN-védelemmel) viszont
visszahozta az EREDETI (2026-09-06 óta dokumentált) problémát: "totál
visszaállt a csillogás" - mert a LAPOS, quadonkénti normál a szomszédos
tile-ok között DISZKONTINUUS, és a spekuláris fényfolt emiatt
tile-ról tile-ra ugorva "villan" (nem a fényerő a probléma, hanem a
normál-mező FOLYTONOSSÁGÁNAK hiánya - ezt már az intenzitás-csökkentés
1-12. körben sem oldotta meg véglegesen).

**Felismerés:** a probléma NEM "lapos VAGY sima normál" választás,
hanem hogy a KORÁBBI két megoldás egyike sem volt egyszerre sima ÉS
lejtés-érzékeny. A `_persistentCornerCache`/`cornerCache` már ma is
MEGOSZTJA a sarokpontok POZÍCIÓJÁT a szomszédos tile-ok között (a kulcs
`(Face, Level, CornerU, CornerV)`, független attól, MELYIK tile kéri) -
ha a NORMÁLT is ugyanígy, a sarokponthoz kötve, egy KIS, RÖGZÍTETT (nem
a hívó quad tile-méretétől függő) UV-eltolással vett veges differenciával
számoljuk (nem a hívó quad SAJÁT, tile-méretű sarok-távolságával), akkor
a normál TISZTÁN a `(face,uc,vc)` pont függvénye - a szomszédos tile-ok
automatikusan UGYANAZT az értéket kapják a közös sarkukon (nincs ugrás),
miközben a normál továbbra is a TÉNYLEGES helyi lejtésből származik (nem
egy lejtés-vak gömb-közelítésből).

**Implementáció** (`PlanetGridMesh.cs`):
- `ComputeCornerNormalViaFiniteDifference(face, uc, vc, ...)`: a
  `ToDisplacedVector3`-at (a MÁR létező, egyetlen pozíció-forrás
  függvényt) hívja a `(uc,vc)`, `(uc+ε,vc)`, `(uc,vc+ε)` pontokra
  (`ε = NormalSampleEpsilonUV = 1e-4`), a két érintő-vektor cross
  szorzatából normál - degenerált/NaN esetben `SafeSurfaceNormal`
  (gömb-irányú) tartalékra esik vissza (a 14. körben felismert HELYES,
  `!(x >= küszöb)` NaN-védelemmel).
- Ugyanaz a MEGOSZTOTT sarok-cache-mintázat, mint a színnél
  (`_persistentCornerColorCache`/`GetOrComputePersistentCornerColor`):
  új `_persistentCornerNormalCache` (adaptív út) + egy helyi
  `cornerNormalCache` (statikus alapréteg-ciklus) + a GPU-geometria
  útvonalhoz is bekötve. A `PrecomputeCornersInParallel` MOST már a
  normált is a MEGLÉVŐ párhuzamos ciklusban számolja (nem külön,
  szekvenciális lépésben).
- Új `AddQuad` túlterhelés, ami a 4 csúcs-normált KÍVÜLRŐL, előre
  kiszámítva kapja (a szárazföldi kategóriák hívják) - a víz/tó/folyó/
  kráter továbbra is a RÉGI, belsőleg számolt (lapos/degenerált-védett)
  `AddQuad`-ot használja, mert azoknál a folytonosság kevésbé kritikus
  (víz eleve gömb, a többi kis, jelölő jellegű terület) - ld. a korábbi
  ND-54-es döntés kapcsán már dokumentált precedens.

**TELJESÍTMÉNY, KÜLÖN KIEMELVE (az ND-54 alatti Full-modell-
regresszióból tanulva)**: ez EGYEDI, MÉG NEM CACHE-ELT sarkonként 2
TOVÁBBI teljes elevációkiértékelést (`ToDisplacedVector3`) jelent - de
KIZÁRÓLAG a megosztott sarok-cache-en KERESZTÜL érhető el
(`ComputeCornerNormal`/`GetOrComputePersistentCornerNormal`), SOHA nem
quadonként közvetlenül, tehát a többletköltség pontosan úgy korlátozott,
mint a már bevált szín-cache költsége (`PrecomputeCornersInParallel`,
Parallel.For, csak a hiányzó sarkokra). **Élő teljesítmény-ellenőrzés
hátra** - ha az `adaptiveRebuildWarningMs` naplóban tartós lassulás
jelentkezne, az `ε` durvábbra állítása vagy a normál-cache külön
LRU-mérete (jelenleg a pozíció-cache-ével közös) hangolható.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-változás, a
Core-t nem érinti). Élő Unity-ellenőrzés hátra.

### ND-56 — Harmadik, közeli-zoom léptékű részlet-zaj réteg (`CrustElevation.TertiaryDetailNoise`)

**Kontextus:** felhasználói kérés (2026-09-09): "eddig fraktál alapú volt
[a másodlagos zaj], most olyat szeretnék ami a maximális felbontás esetén
is minden tile-ra hatással van". A meglévő másodlagos zaj (ND-52) periódusa
a level=5 STATIKUS rácshoz van hangolva - regionális léptékű, folytonos
domborzat-textúrát ad, de a legmélyebb adaptív LOD-nál (nagyságrendekkel
finomabb tile-méret) több ezer szomszédos tile esik ugyanabba a hullámba,
ezért ott ismét laposnak/részlettelennek tűnhet a felszín.

**Megvitatott alternatívák** (felhasználóval egyeztetve, `1` opció
választva):
1. **Harmadik, magasabb frekvenciájú koherens zajréteg** (ugyanaz a
   `RidgedMultifractal`, csak közeli-zoom léptékre hangolva) - a
   választott irány.
2. Sarokponthoz kötött, determinisztikus hash-alapú "mikro-jitter" (nem
   folytonos zajfüggvény) - elvetve, mert "szemcsés/kráteres" hatást ad,
   nem "domborzat-szerű" mintázatot.
3. GPU-alapú zajszámítás kiterjesztése - a felhasználó felvetette, de
   elvetve: a `WorldGen.Core` motorfüggetlensége miatt a GPU sosem
   lehet a HITELES modell forrása (csak Viewer-oldali közelítés); az
   ND-52-ben már dokumentált GPU shader-fordítási időtúllépés-korlát
   minden további réteggel súlyosbodna; a finom (magas frekvenciás) zaj
   pontossága a `float` (32 bit, GPU) precízióval ROSSZABB, nem jobb,
   mint `double`-lel (CPU) - pont ott, ahol a legfinomabb részlet
   számítana.

**Implementáció**: `CrustElevation.TertiaryDetailNoise` - ugyanaz a
`RidgedMultifractal` algoritmus, mint az elsődleges/másodlagos réteg,
harmadik, független koordináta-eltolással dekorrelálva, `MountainMask`
nélkül (ugyanazon okból, mint a másodlagos - sík régiókban a
legfontosabb, hogy legyen közeli-zoom textúra).

**❌ ELSŐ PRÓBÁLKOZÁS VISSZAVONVA, ÉLŐ TESZT ELŐTT (2026-09-09):**
`TertiaryNoiseReferenceLevel=20` (a Unity Viewer `PlanetGridMesh.
adaptiveMaxLevel` ELMÉLETI felső korlátja), `TertiaryNoisePeriodTiles=3`.
Felhasználói visszajelzés élő teszt után: **"katasztrófa... a távoli zoom
nézetet nagyban befolyásolja, ellenben az extrém közeli zoom esetén nem
egyenletes a zaj eloszlása"**. Diagnózis: level=20 a GYAKORLATBAN szinte
soha nem éretik el (elméleti felső korlát, nem tényleges zoom-mélység),
ezért a zaj hullámhossza a gyakorlati LOD-oknál sokkal kisebb, mint egy
tile - ez **térbeli ALIASING**-ot okozott két irányban: (1) távoli/durva
LOD-nál a kevés, egymástól távoli sarokpont a hullám véletlenszerű
fázisait találta el → kaotikus zaj ott, ahol semminek nem kellett volna
látszania; (2) közeli, de a ténylegesen elért (nem elméleti max) LOD-nál
egyetlen tile-on belül több teljes hullámciklus is belefért → egyenetlen,
foltos hatás sima hullámzás helyett.

**✅ JAVÍTVA, ÉLŐ TESZT ELŐTT**: a másodlagos zaj sikeres mintáját
ismételtük meg (level a STATIKUS/gyakran-elért rácshoz hangolva, nem az
elméleti maximumhoz) - `TertiaryNoiseReferenceLevel` 20→**13** (~ND-18
"Erózió cél-LOD: 12" értékéhez közeli), `TertiaryNoisePeriodTiles` 3→**8**
(hogy egy tile-on belül ne törjön több teljes hullámciklus).
`TertiaryNoiseAmplitudeMeters=50`, `TertiaryNoiseOctaves=2` változatlan.

**Regenerálási lánc**: a teljes downstream kaszkád újrafuttatva (Python
referencia + minden függő KAT-vektor: crust_elevation, plate_boundary,
erosion_glaciation_deep_time, hydrology, river_path, lakes_ice_erosion,
moisture_transport, features, state_hash - a `volcanism`/`sea_level` nem
függ közvetlenül a `base_elevation`-től, TEST-EARTH-001 továbbra is PASS,
65.0% víz-arány). Kontinens-szám 34→**37**, régió-szám 412→**402** (a
`TestEarth001Tests.ContinentSizesMatchPythonReferenceExactly` és
`FeatureSegmentationStructuralTests.MatchesPythonReferenceContinentsAndRegionsExactly`
tesztek frissítve a Python referenciával újramért, pontos értékekre).
375/375 Core-teszt PASS.

**Verziózás:** SEED-TÖRŐ (a `CrustElevation.BaseElevation` numerikus
kimenete minden pozícióra megváltozik - ugyanaz a besorolás, mint ND-52).

**❌ TELJESEN VISSZAVONVA, UGYANAZNAP (2026-09-09):** élő Unity-teszt
után a felhasználó visszajelzése: "nem lett jobb, szeretném visszavonni
a 3. fokú zajgenerálást. működjön minden úgy ahogy ezelőtt". A level=13/
8-tile újrahangolás sem hozott érzékelhető javulást a korábbi
(level=20/3-tile, már korábban "katasztrófaként" elutasított) állapothoz
képest. A teljes ND-56 réteg (Python `tertiary_detail_noise`/
`TERTIARY_NOISE_*` konstansok, C# `TertiaryDetailNoise`/
`TertiaryNoise*` mezők, mindkét helyen a `base_elevation`/`BaseElevation`
visszatérési sorból az additív tag) KITÖRÖLVE. A teljes downstream
KAT-vektor-lánc újra regenerálva az ND-52-es (másodlagos zaj, harmadik
réteg nélküli) állapotra, a két érintett teszt-elvárás visszaállítva
(kontinens-szám 37→**34**, régió-szám 402→**412**, kontinens-méret-lista
visszaállítva). 375/375 Core-teszt PASS. **Tanulság**: ez a második eset
ebben a session-ben (az első az ND-54 Full-hőmodell-kísérlet volt), hogy
egy jól megindokolt, referencia-szinten plauzibilis numerikus finomítás
élő Unity-tesztelésen egyszerűen NEM hozott érzékelhető/kívánt vizuális
javulást - a `CrustElevation.BaseElevation` további finomítása inkább
VIZUÁLIS, élő Unity-vissza csatolással vezérelt iterációt igényelne
(pl. egyenesen Unityben, a tényleges renderelt eredményt figyelve), nem
tisztán referencia-szintű (Python/C# plauzibilitás-teszt) tervezést.

### ND-57 — Tengeri jég (SeaIce/Ocean) határ zaj-jitterrel, a szárazföldi jégsapka-mintát követve

**Kontextus:** felhasználói visszajelzés (2026-09-09): "van az északi és
déli póluson is egy fix, adott magassági foknál lévő jég kirajzolás, kör
alakú, a pólus a r sugarú körben, belül van a jég" - kapcsolódik a
korábbi #7-es checklist-tételhez ("Pólusi jég — zajos partvonal"), ahol
a felhasználó jelezte: "a sarkvidéki kontinens ok, a konstans fehér
sapka még mindig ott van".

**Gyökérok:** a `Biome.SeaIce`/`Biome.Ocean` határ a Core-ban
(`BiomeClassification.Classify`) TISZTA hőmérséklet-küszöb
(`OceanFreezingK`=271.15K), zaj/jitter NÉLKÜL - ellentétben a
szárazföldi jégsapka-határral, amit a Viewer korábban (ND-nem-számozott,
`IsAdaptiveIceTile`) már zajjal perturbált. Mivel az óceáni hőmérséklet
(a jelenlegi egyszerű, inszolláció-alapú modellben) majdnem tökéletesen
szélesség-szimmetrikus, ez egy geometriailag tökéletes kört adott a
tengeri jég határának mindkét pólusnál.

**Javítás** (`PlanetGridMesh.cs`): új `IsAdaptiveSeaIce(x,y,z,
temperatureK)` - UGYANAZ a jitter-minta (`IceBoundaryJitterAmplitudeK`/
`Frequency`/`Octaves`, `FractalNoise.Fbm`), mint a szárazföldi
`IsAdaptiveIceTile`, csak az `OceanFreezingK` küszöbre alkalmazva. FONTOS:
ez KIZÁRÓLAG a RENDER-kategória (`RenderCategory.SeaIce` vs `.Ocean`)
döntését módosítja - a Core `biome`/`temperatureK` (és az ezekből
számolt statisztikák, pl. panel-adatok) VÁLTOZATLANOK maradnak,
ugyanazon elv szerint, mint a szárazföldi jég jitterje. Mindhárom
érintett hely frissítve: a statikus alapréteg, az adaptív
`ComputeTileClassification`, és MINDKÉT vízfelszín-szín-döntés (korábban
`biome == Biome.SeaIce`-t néztek, most a már jitterelt kategóriát/
`isSeaIceRendered`-et).

**Dokumentált, el nem hárított korlát**: a GPU compute shader port
(`TileClassification.compute`, `ClassifyBiomeF`) NEM kapott jittert -
`useGpuGeometry` alapértelmezetten ki van kapcsolva, és a
`GpuQuadResult` nem is ad vissza `temperatureK`-t a hívó oldalnak (csak
elevation/isOceanic/biome-ot) - a jitter hozzáadásához ez is bővítendő
lenne, külön feladat.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-döntés, a
Core `biome`/`temperatureK`/statisztikák változatlanok). **Élő
Unity-ellenőrzés hátra.**

### ND-58 — SeaIce is a "folytonos" (nem lapos HDRP/Lit) terep-kategóriák közé került

**Kontextus:** az ND-57 jitter hozzáadása UTÁN a felhasználó jelezte,
hogy a kör alakúság problémája fennmaradt, DE egy screenshot alapján
kiderült, hogy a valódi, elsődleges probléma NEM a határvonal
szabálytalansága volt - a nyílt óceán fölött (nincs alatta kontinens)
egy ÉLES, SOKSZÖGLETES (jól látható háromszög-facettákkal), és
ÉJSZAKA is világosszürke maradó folt jelent meg.

**Gyökérok:** a `RenderCategory.SeaIce` a `GetOrCreateCategoryMaterial`
és `IsContinuousTerrainCategory` szerint a `River`/`Crater`
kategóriákkal egy csoportba tartozott - "kis terület/jelölő jellegű",
ezért a RÉGI, lapos `CategoryColor` + `CreateFlatColorMaterial` (Unity
beépített HDRP/Lit) útvonalat kapta, NEM a folytonos
`VertexColorUnlit`-et. Ez a feltételezés az ND-57 ELŐTT ésszerű volt
(a tengeri jég ritkán/kis foltokban fordult elő), de az ND-57 (jitterelt
SeaIce/Ocean határ) óta a `SeaIce` EGÉSZ SARKI JÉGSAPKÁNYI, nagy,
összefüggő területet fedhet le. A HDRP/Lit anyag két, egymástól
független problémát okozott: (1) NEM használja a `surfaceAmbient`-et
(saját HDRP sky-ambient-jét kapja), ezért éjszaka sem sötétedett el
rendesen; (2) quadonként EGYETLEN, egységes színt ad (nincs
sarkonkénti interpoláció a szomszédokkal, szemben a folytonos
kategóriákkal), ami az éles, sokszögletes határvonalat okozta - ez
volt a "kör alakúság" észlelt oka is, mert a durva, egyenlő szélességű
kvadrátrács quad-hataraí adták a látszólagos geometrikus mintázatot,
nem maga a fagyási-küszöb.

**Javítás**: `RenderCategory.SeaIce` felvéve az
`IsContinuousTerrainCategory`/`ContinuousSurfaceColor` közé, az
`Ocean`-nal azonos módon (`ContinuousOceanRockColor` - mivel a
`ContinuousCornerColor` már eddig is PURE `isOceanic`-alapon döntött,
nem kategórián, ez a hívó-oldali útvonal-döntés módosítása volt
elegendő, a szín-számítás logikája változatlan). A tényleges
jég-vs-víz szín továbbra is a KÜLÖN vízfelszín-rétegből jön
(`ContinuousWaterCornerColor`/ND-57 `isSeaIceRendered`), ami MÁR
eddig is a folytonos, ambient-helyes `_waterSurfaceMaterial`-t
használta - ez a javítás a TEREP/óceánfenék-réteg (a víz alatt, illetve
egy esetleges rés/Z-fighting esetén átcsúszó) SeaIce-kategóriájú
quad-jait érinti.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-útvonal
döntés). **Élő Unity-ellenőrzés hátra.**

### ND-59 — A szárazföldi jég/tundra biome-fallback is szabályos kört adott (a Core biome, nem a jitterelt réteg, volt a valódi ok)

**Kontextus:** az ND-57/ND-58 UTÁN a felhasználó egy ÚJ screenshottal
jelezte, hogy a probléma továbbra sem a színről/anyagról szól: a
pólusoknál egy szabályos KÖR alakú, világosszürke RÉTEG látszik a
domborzat "alatt", amit a valódi terep (hegy, tó, kráter) itt-ott
felülír. Közvetlen Hierarchy-kiválasztással kizárta: `WaterSurface`,
egy "DynamicRefined"-szerű objektum, `Rivers`, `LakeSurface` - a
gyanú a `Planet` (fő terep-) objektumra esett.

**Gyökérok:** a render-kategória ternárius minden classification
helyen (`PlanetGridMesh.cs` statikus alapréteg + CPU-adaptív ág, ill.
a két GPU-táplált adaptív ág) `ToRenderCategory(biome)`-ra esik
vissza, ha a tile nem kráter/tó/óceán és a jitterelt `isIce`
(évi-átlag alapú, `IsAdaptiveIceTile`) hamis. Ez a `biome` viszont a
Core `BiomeClassification.Classify(temperatureK, isOceanic)`
PILLANATNYI, ZAJ/JITTER NÉLKÜLI hőmérsékletéből jön
(`BiomeClassification.cs`: `if (temperatureK < IceSheetThresholdK)
return Biome.IceSheet;`). Két, egymástól FÜGGETLEN jégréteg létezett
tehát: (1) a jitterelt, évi-átlag `isIce`, és (2) a nyers `biome` saját
IceSheet-besorolása. Mivel `isIce` csak HOZZÁAD jeget, sosem vesz el,
a nyers, jitter nélküli réteg mindig "átsejlik", ahol `isIce` épp
hamis - és mivel a szárazföldi pillanatnyi hőmérséklet a pólusoknál
(sík, alacsony domborzatú területeken) közel tisztán
szélesség/évszak-függő, ez a réteg geometriailag majdnem tökéletes
kör. Ugyanaz a hibaosztály, mint az ND-57 (tengeri jég), csak a
SZÁRAZFÖLDI biome-eldöntésnél, és korábban rejtve maradt, mert a
domborzat/hegy/tó véletlenszerűen gyakran felülírta.

**Javítás:** új `JitteredRenderBiome(x,y,z,temperatureK,isOceanic,seed)`
helper (`PlanetGridMesh.cs`) - ugyanazt az `IceBoundaryJitterAmplitudeK`/
`Frequency`/`Octaves` zajt alkalmazza a hőmérsékletre, mint az
ND-57/`IsAdaptiveIceTile`, MIELŐTT a `BiomeClassification.Classify`-t
hívja. Bekötve a két CPU-oldali fallback-ágba (statikus alapréteg és a
CPU-adaptív `ComputeTileClassification`) - ez érinti azt, ami a
"Bolygó" nézetben (mindig CPU, ez volt a screenshoten látható). A két
GPU-táplált adaptív ág (`useGpuClassification: 1` a scene-ben) NEM
kapta meg ezt a javítást, mert a `GpuQuadResult`/`GpuClassificationResult`
csak a már kész, diszkrét `biome`-ot adja vissza a CPU-nak, nem a
nyers `temperatureK`-t - ugyanaz az elfogadott, dokumentált korlát,
mint az ND-57 GPU-oldali hiánya (ld. `TileClassification.compute`
kommentje `ClassifyBiomeF` fölött). A Core `biomeOf[id]`/statisztikák
(kontinens-osztályozás, névgenerálás stb.) ÉRINTETLENEK - kizárólag a
Viewer render-kategória döntése változott.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-útvonal
döntés). **Élő Unity-ellenőrzés hátra.**

### ND-60 — A "kör alakú pólusi jég" végleges oka: az óceán SOSEM finomodik adaptívan, és a víz FEHÉR jég-színe volt a látott réteg

**Kontextus:** az ND-59 UTÁN a felhasználó megerősítette: közelről zoomolva
IS tökéletes kör maradt a pólusi "jég", és ha a `WaterSurface` réteget
kikapcsolta, ELTŰNT vele együtt ez a zavaró félkörös folt is. Saját
hipotézise: "nem lehet, hogy egy adott magassági foktól a víz/óceán
kirajzolása nem kék hanem fehér/halványszürkével történik?"

**Gyökérok (kódból igazolva, KÉT rétegben):**

1. **Az adaptív (finomodó) réteg explicit módon kizárja az óceáni
   tile-okat a finomodásból** (`IsBaseAncestorOceanic`, egy 2026-09-02-i
   szándékos teljesítmény-döntés): ha egy tile durva-szintű őse óceáni
   (`elevation < seaLevel`), SOSEM kerül finomítandó listába -
   FÜGGETLENÜL a kameratávolságtól. A tengeri jég geometriailag óceán
   (fagyott víz), tehát ez a szabály rá is érvényes - a pólusi
   víz/jég-felszín emiatt MINDIG a durva statikus alaphálón (level=5)
   renderelődik, közelről is. Egy önálló, offline C# próbaszkripttel
   (a valós seeddel/paraméterekkel, Unity nélkül) számszerűen igazolva:
   a pólus közelében a szomszédos tile-ok hőmérséklete 20-26K-t ugrik,
   amit sem K-alapú jitter (tesztelve 4→50K), sem pozíció-alapú
   szélesség-jitter (2°→35°) nem tud megtörni - a burkoló forma minden
   tesztelt amplitúdónál lényegében változatlan kör maradt.
2. **A víz-quad SZÍNE korábban bináris kapcsolóval dőlt el**:
   `isSeaIceRendered`/`category==SeaIce` esetén a TELJES quad egyetlen,
   lapos, fehér "jég" színt kapott (`CategoryColor(RenderCategory.
   SeaIce, 0)`) a normál, mélység-alapú kék óceánszín
   (`ContinuousWaterCornerColor`) helyett. Mivel (1) miatt ez a
   kapcsoló mindig a durva racson dőlt el, a fehér folt éles, szabályos
   kör alakú lett - ÉS mivel ez a `WaterSurface` mesh-en (nem a terep-
   meshen) történt, a `WaterSurface` kikapcsolása vele együtt eltüntette.

**Felhasználói döntés a javítás irányáról:** a finomodási architektúra
(1. pont) MÓDOSÍTÁSA ELUTASÍTVA ("nagyon nem jó irány... a tile bontás
most nem érdekel") - a felhasználó kifejezetten azt kérte, hogy az
óceán SZÍNÉT ne befolyásolja a pólusközelség, a tile-felbontás
kérdésétől függetlenül.

**Javítás (kizárólag a 2. pont, a víz SZÍNE):** mindhárom vízépítő
helyen (statikus alapréteg, `EmitAdaptiveTile` CPU-adaptív ág,
`EmitAdaptiveTilesGpu` GPU-adaptív ág) törölve a fehér jég-szín ág - a
víz MOSTANTÓL MINDIG `ContinuousWaterCornerColor`-t (valódi mélység-
alapú kék) kap, hőmérséklettől/szélességtől függetlenül. Emellett az
`EmitAdaptiveTilesGpu` víz-LÉTEZÉSI feltétele (`biome == Biome.Ocean`)
ki lett egészítve `|| biome == Biome.SeaIce`-szel - ez a GPU-s ág
korábban EGYÁLTALÁN nem épített vízfelszínt SeaIce-tile-okra (a
CPU-s `EmitAdaptiveTile` már korábban helyesen tartalmazta mindkét
esetet), ami a mély óceánfenék-terepet hagyta volna fedetlenül azokon
a tile-okon - ez NEM a tile-felbontásról szól, csak arról, hogy a víz
egyáltalán megépüljön-e (a látvány konzisztenciájához kellett, a
felhasználó kérésén nem változtat).

A `RenderCategory.SeaIce`/`isSeaIceRendered` kategória-eldöntés
(ND-57/ND-59 jitter) VÁLTOZATLANUL megmaradt - ez mostantól csak a
TEREP (óceánfenék) kategória-besorolásán él tovább, ami ND-58 óta
ugyanazt a színt adja, mint `Ocean` (`ContinuousOceanRockColor`),
tehát vizuálisan nincs hatása. Nem törölve, mert a `bucket`/
statisztikai célú megkülönböztetés máshol még hasznos lehet, és a
törlése nagyobb, itt nem kért átalakítás lenne.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-útvonal
döntés). **Élő Unity-ellenőrzés hátra.**

### ND-62 — Kameramód-kapcsoló + "Tengelyforgás megfigyelése" (a Planet mesh tényleges forgatása)

**Kontextus:** felhasználói kérés (2026-09-10) - a backlog két kamera-
mód tételét (pálya menti mozgás követése; tengelyforgás megfigyelése)
egy közös panel-kapcsolóval szeretné, ebben a sorrendben: (1) kapcsoló-
infrastruktúra, (2) tengelyforgás-mód, (3) pálya menti mód (ez utóbbi
KÖVETKEZŐ kör, a backlog saját nyitott kérdése miatt - fusson-e a
tengelyforgás egyidejűleg -, amit csak akkor kell eldönteni).

**Architekturális ütközés (a backlog is jelezte)**: a `SunController`
eddig SZÁNDÉKOSAN és DOKUMENTÁLTAN a `Planet` GameObject rotációját
mindig identitáson tartotta - a nap/éj ciklust a Directional Light
forgatása szimulálta, a bolygó SAJÁT (forgó) test-keretében számolt
Nap-irány (`OrbitalMechanics.SunDirectionBodyFrame`) alapján. A
"tengelyforgás megfigyelése" mód ezt PONT megfordítja: a bolygónak
TÉNYLEGESEN forognia kell, a Nap/csillagok maradjanak fixek.

**Megoldás**: mivel a `PlanetGridMesh` a mesh-csúcsokat mindig LOKÁLIS
(test-keret) koordinátában építi, a `Planet.transform.rotation`
beállítása Unity-szinten automatikusan elforgatja a KÉSZ mesh-et - a
geometria-számítás egyáltalán nem módosult. Új `PlanetGridMesh.
CameraViewMode` enum (`Free`/`AxialRotation`/`OrbitalFollow`) + panel-
kapcsoló (3 kölcsönösen kizáró `GUI.Toggle`, a `windSpeedOverlay`/
`precipitationOverlay` kizárás mintáján). `SunController.
ApplySunDirection()` mód-elágazása:
- **`Free`** (alapértelmezett): változatlan - `SunDirectionBodyFrame`,
  Planet identitáson, csillagmező ellentétes irányban forog.
- **`AxialRotation`**: `OrbitalMechanics.SunDirectionOrbitalFrame`
  (a MÁR publikus, "nem forgó pálya-keret" függvény) közvetlenül, test-
  keret-transzformáció NÉLKÜL, mint világtér-irány (fény/nap-korong/
  csillagmező ezt kapja, forgatás nélkül - a csillagmező `SetRotationAngleRadians(0)`-t
  kap, fixen áll). A `Planet.transform.rotation` a Core `R_tilt * R_spin`
  (test→pálya) mátrix Unity-megfelelőjét kapja: `Quaternion.AngleAxis(
  axialTiltDegrees, Vector3.right) * Quaternion.AngleAxis(rotationAngle
  *Rad2Deg, Vector3.up)`. A SPIN előjele a `StarField.
  SetRotationAngleRadians` MÁR élesben bevált, ELLENTÉTES irányú
  forgatásából levezetve (nagy bizonyossággal helyes) - a DŐLÉS előjele
  viszont a tengelycsere-konvenció (`BodyFrameConversion`: Core (x,y,z)
  → Unity (x,z,y), ami egy páratlan permutáció/tükrözés) miatt csak
  ELMÉLETBEN levezetett, **élő Unity-vizuális teszttel ellenőrizendő**
  - pontosan ugyanaz a kockázati osztály, mint a MÁR MEGLÉVŐ, dokumentált
  `PlanetOrbitCamera.FlyToDirection` bizonytalansága.
- **`OrbitalFollow`**: egyelőre a `Free`-vel azonos (nincs még
  implementálva - 2. kör).

**`PlanetOrbitCamera`-t NEM kellett módosítani**: a `target.position`
körül forog, a `target.rotation`-t sosem olvassa - a Planet forgása
ortogonális a szabad egérrel-nézegetéshez.

**Ellenőrzési kritérium**: ugyanannál a `currentTimeDays`-nál a
megvilágított kontinensek/terep `AxialRotation` és `Free` módban
UGYANAZOK legyenek. Ha tükrözöttnek/eltoltnak tűnik, a legvalószínűbb
javítás a dőlés-komponens előjelváltása vagy a szorzási sorrend
felcserélése.

**Verziózás:** nem seed-törő (tisztán Viewer-oldali render-útvonal
döntés, Core-t nem érinti - csak egy MÁR publikus függvényt hívunk
újonnan). **Élő Unity-ellenőrzés hátra** - a `SunController`
Inspectorában be kell kötni az új `planetGridMesh` mezőt.

**Élő teszt kiegészítése (2026-09-11):** a csillagmező tengelyforgás
módban lassan a bolygóval együtt mozgott. Gyökérok: a `StarField` a
`Planet` gyereke, ezért a `SetRotationAngleRadians(0)` csak a lokális
forgatást nullázta, a szülő forgását továbbra is örökölte. Javítás:
`StarField.KeepFixedInWorldSpace()` minden képkockán világkoordinátában
identitáson tartja. A scene-ben talált `rotationPeriodDays=100` érték a
világ `climateRotationPeriodDays=1` értékével is ellentmondott és a tesztet
százszor lassította; 1 napra összehangolva. További élő ellenőrzés hátra.

### ND-63 — Deep-time exact rebuild: időfüggetlen terrain-bázis cache, változatlan időfüggő kiértékeléssel

**Kontextus és mérés (2026-09-11):** a legacy geometriahurok, a nem használt
coarse folyófelhalmozás és a redundáns normál-középpont kiértékelésének
eltávolítása után két élő deep-time rebuild 18,997 s és 19,138 s volt. A
domináns részfázis a statikus sarkok pozíció/szín/normál számítása, átlag
7,511 s. Minden időlépésnél 396 294 sarok cache miss keletkezik, miközben a
sarok nyers koordinátája és a következő drága részeredmények nem függenek a
deep-time értéktől: `DomainWarp.WarpPosition`, az elsődleges ridged zaj, a
`MountainMask` és a másodlagos zaj.

**Döntés:** a viewer a statikus base-level sarkok középpontjához és a két
ND-55 normálmintájához perzisztens, `(worldSeed, adaptiveBaseLevel)` kulcsú,
tömör tömbös `TerrainPointBasis` cache-t tart fenn. A bázis kizárólag a
warpolt koordinátát és a kéreg-eleváció időfüggetlen zajtagjait tárolja.
Deep-time váltáskor továbbra is újrafut:

- a mozgó lemezmagok előállítása és a legközelebbi/két legközelebbi lemez
  meghatározása;
- a kéregtípusból következő base elevation összeállítása;
- a lemezhatár-uplift és annak időbeli relaxációja;
- a kráterkorrekció, hőmérséklet, biome és minden modellbesorolás.

A `TwoBestDots` eredményének `bestIndex` tagja ugyanabban a seed-sorrendben,
ugyanazzal a szigorú `>` összehasonlítással adja azt a plate ID-t, mint az
eddigi külön `AssignPlate` passz. Ezért a két seed-szkennelés egyetlen
passzba vonható össze. A `MountainMask` ugyanaz a double részeredmény a base
elevation és az uplift számára, ezért újraszámítás helyett megosztható.

**Hatókör első fázisa:** csak a legfeljebb level-8 statikus base-level
sarokút használja az új cache-t. Level 9-10 esetén a memória 4x/16x lenne,
ezért ott az általános exact út marad. A dinamikus, változó LOD-sarkok a
meglévő általános útvonalon maradnak; a level-8 hidrológia és tile-
klasszifikáció közös bázistömbje egy
következő, külön mérhető fázis. Így az első változtatás kis felületű, és a
7,511 s-os legnagyobb blokkot célozza.

**Determinizmus és verziózás:** az új út ugyanazokat a részműveleteket és
azonos lebegőpontos műveleti sorrendet használja; csak a tiszta,
időfüggetlen részeredményt tárolja el. Core-teszt hasonlítja össze az eredeti
és a bázisból történő kiértékelés `double` bitmintáját több seed/pont esetén.
Nem seed-törő, világverzió-emelés nem szükséges. A cache nem kerül
perzisztálásra és nem része a world state hashnek.

**Memória:** level 8-on `6*(256+1)^2 = 396 294` sarok, három minta és mintánként
hat `double` körülbelül 54,4 MiB nyers tömbmemória. Dictionary nem használható
ehhez a bázishoz; a `(face,u,v)` determinisztikus tömbindexre képeződik.

**Elfogadási kritérium:** Core bitazonossági tesztek, teljes build/test,
Unity C# fordítás, majd élő PerfLog. Vizuális késznek csak változatlan felszín
és az új log alapján nyilvánítható.

### ND-64 — Deep-time exact rebuild: közös tile-középpont bázis és előállított napi Nap-minták

**Élő mérési alap (2026-09-11):** az ND-63 első fázisa után a két deep-time
rebuild 12 158,5 ms és 11 976,2 ms volt (átlag 12 067,4 ms). A statikus
sarokfázis 7 511,4 ms-ról 438,8 ms-ra csökkent, miközben a terrain-bázis
mindkét rebuildben `reused=True`. A maradék idő három domináns blokkja a
level-8 hidrológia (átlag 4 912,2 ms), a tile-klasszifikáció (3 026,0 ms) és
a mesh-emisszió (2 521,6 ms).

**Döntés, terrain:** az ND-63 második fázisában a level-8 tile-középpontokhoz
is tömör, `(worldSeed, level)` kulcsú `TerrainPointBasis` tömb készül. Ugyanezt
olvassa a hidrológiai elevation field és a base-level tile-klasszifikáció.
Az aktuálisan mozgatott plate-ek, uplift, relaxáció, kráter és óceán/biome
döntések továbbra is minden időpontban frissen értékelődnek. Az eredeti field
lebegőpontos műveleti sorrendje megmarad: először `base + uplift`, utána
kráterkorrekció, végül `+(relaxedUplift-uplift)`.

**Döntés, hőmérséklet:** a `Temperature.DailyAverageInsolationFactor` napi 24
mintájának Nap-iránya kizárólag a Build-szintű idő/pálya/forgás/dőlés
paraméterektől függ, a felszíni ponttól nem. Ezek az irányok egyszer készülnek
el, majd minden tile ugyanabban a sorrendben végzi el a 24 dot-productot és
összegzést. A hőmérséklet képlete, mintaszáma és összeadási sorrendje nem
változik. Az élő log közvetlen bizonyítéka: a t=0 GPU-klasszifikáció dispatch-e
37,2 ms, az utána futó, eleváció-zaj nélküli CPU hőmérséklet-loop 3 677,8 ms.

**Determinizmus és verziózás:** mindkét változtatás tiszta közös-részkifejezés
kiemelés; sem random mapping, sem numerikus képlet, sem mintavétel nem változik.
A régi és az előállított-bázisú utak `double` bitmintáját teszt fedi. Nem
seed-törő, világverzió-emelés nem szükséges. A tile-középpont cache level 8-on
393 216 × 48 byte, körülbelül 18 MiB nyers tömbmemória; level 8 fölött nem
épül fel.

**Elfogadási kapu:** teljes build/test és Unity C# fordítás után új élő PerfLog.
A `<1 s` cél ettől még nem tekinthető elértnek; a mérés után a mesh-emisszió és
a priority-flood maradékát külön kell kezelni.

### ND-65 — Exact maradék: kikapcsolt border-work elhagyása és determinisztikus priority-flood heap

**Kontextus (2026-09-11):** az ND-64 utáni két élő deep-time rebuild átlaga
5 781,2 ms. A két domináns blokk a statikus mesh-emisszió (2 464,2 ms) és a
level-8 priority-flood (1 248,8 ms). A scene-ben `showBorders=0`, mégis minden
393 216 tile négy border-vertexet és nyolc vonalindexet ír listákba; a
`BuildBorders` ezt az egész eredményt felhasználás nélkül eldobja. A flood
`SortedSet<(double Elevation,long Counter)>` mellett külön
`Dictionary<long,TileId>` leképezést tart fenn, noha a tile közvetlenül a
prioritási sor elemében tárolható.

**Döntés:** kikapcsolt border-rendernél az emit-loop nem állít elő border-
geometriát. Bekapcsolt állapotban az út változatlan. A priority-flood rendezett
halmaza egy belső bináris minimum-heapre cserélődik, amely közvetlenül az
`(Elevation, Counter, Tile)` hármast tárolja. Az összehasonlítás először
`double.CompareTo`-val az elevációt, majd `long.CompareTo`-val az egyedi,
monoton számlálót vizsgálja; ez ugyanaz a teljes rendezés, mint az eddigi
ValueTuple/SortedSet kulcsé. Így a pop-sorrend, a szomszédok fix
right/left/up/down bejárása, a parent, filled és floodOrder eredmény változatlan.

**Determinizmus és verziózás:** sem szimulációs képlet, sem tie-break, sem
bejárási sorrend nem változik; a heap csak az azonos prioritási sor más
adatszerkezeti reprezentációja. Nem seed-törő, világverzió-emelés nem kell.
Teszt hasonlítja össze a teljes `Filled`, `Parent` és `FloodOrder` kimenetet a
korábbi rendezett-halmaz referenciaúttal.

### ND-66 — A teljes statikus base-grid tömör, közvetlenül indexelt render-cache-e

**Kontextus (2026-09-11):** az ND-65 utáni két élő deep-time rebuild átlaga
5 276,6 ms. A priority-flood 31,7%-kal, az emisszió csak 4,2%-kal gyorsult; az
utóbbi továbbra is 2 361,0 ms, a teljes idő 44,7%-a. A base-grid nem ritka vagy
változó topológia: mind a 393 216 level-8 tile, illetve mind a 396 294 face-local
sarok jelen van, mégis a statikus emit minden tile-nál egy TileId-kulcsú
klasszifikációs Dictionary+LRU találatot, valamint 4-4 tuple-kulcsú pozíció-,
normál- és szín-Dictionary találatot végez. A pozíciótalálatok ezen felül a
LinkedList-alapú LRU-t is átrendezik. Ez több millió főszálas hash-/lista-
művelet olyan adatokon, amelyek indexe közvetlenül levezethető
`face * stride + u * side + v` alakban.

**Döntés:** a teljes statikus base-grid klasszifikációja, sarokpozíciója,
normálja és színe tömör tömbökbe kerül, a már rögzített face/u/v bejárási
sorrenddel. A statikus emit közvetlen tömbindexet használ; nem tölti fel és nem
érinti az általános dinamikus Dictionary/LRU cache-eket. A tömbök a Build után
is megmaradnak, ezért a dinamikus LOD base-szintű szülő-sarok és oceanic-ős
lekérdezése ugyanazt az adatot közvetlenül eléri. Level 8 felett a már meglévő
memóriakorlát miatt az általános cache-út marad.

**Egzakt viselkedés:** a tömbös út ugyanazokat a
`ComputeTileClassification`, `ToDisplacedVector3FromBasis`,
`ContinuousCornerColorAuto` és
`ComputeCornerNormalViaFiniteDifferenceFromBasis` függvényeket hívja, ugyanarra
a TileId-/face/u/v-sorrendre. Csak a tárolás és a visszakeresés változik; nincs
új numerikus képlet, random mapping vagy seedfüggés. Nem seed-törő,
világverzió-emelés nem kell. Az élő Unity-kapu: változatlan látvány és új
PerfLog; a parancssori C# build ezt nem helyettesíti.

### ND-67 — Exact sűrű priority-flood és újrahasznált cubed-sphere topológia

**Kontextus (2026-09-11):** az ND-66 utáni négy meleg deep-time rebuild átlaga
3 564,3 ms. A teljes hydrology 1 003,5 ms, ebből a priority-flood 748,0 ms. A
heap már az ND-65 szerinti tömör bináris heap, de a level-8 rács minden egyes
tile-jához továbbra is Dictionary/HashSet alapú `filled`, `parent`, `order` és
`visited` állapotot kezel, illetve minden rebuildben négyszer geometriai úton
újraszámolja a világidőtől teljesen független szomszédságot.

**Döntés:** a `FlowNetwork` kap egy fix level teljes cubed-sphere rácsát
face/u/v sorrendben reprezentáló, világfüggetlen `DenseGridTopology` típust. A
topológia egyszer állítja elő a TileId-ket és a right/left/up/down
szomszédindexeket, majd deep-time rebuildenként újrahasználható. Az új
`PriorityFloodDense` az elevációt, ocean flaget, visited állapotot, filled
magasságot, parent indexet és flood ordert tömbökben kezeli. A régi Dictionary-
alapú publikus út változatlanul megmarad a referencia-szintű/paneles
fogyasztóknak és egzakt összehasonlítási orákulumnak.

**Determinizmus:** a sűrű index pontosan a meglévő beszúrási sorrend
(`face`, majd `u`, majd `v`). Az óceáni gyökerek enqueue-sorrendje, a heap
`(elevation,counter)` összehasonlítása és a szomszédok right/left/up/down
sorrendje változatlan. Teszt veti össze minden tile `Filled`, `Parent` és
`FloodOrder` értékét a Dictionary-úttal, a `double` értékeket bitmintára.
Nincs numerikus vagy seed-viselkedés változás; világverzió-emelés nem kell.

### ND-68 — Statikus mesh-bucketek pontos előméretezése és közvetlen elérése

**Kontextus (2026-09-11):** az ND-66 után a statikus emisszió még átlag
944,7 ms. A klasszifikáció már teljes egészében tömbben rendelkezésre áll az
emit előtt, mégis minden tile négy `(category,bucket)` Dictionary-lookupot
végez, a terrain- és vízlisták pedig alapkapacitásról ismételten növekednek és
másolódnak.

**Döntés:** a statikus emit előtt egy lineáris számlálópassz meghatározza minden
terrain- és water-bucket quad-számát. A listák pontos `4*quadCount` vertex/
normal/color és `6*quadCount` index kapacitással jönnek létre, egyszer kerülnek
be a meglévő Dictionary-kimenetbe, az emit pedig közvetlen tömbreferenciával
éri el őket. A dinamikus, ritka és változó LOD-út továbbra is a meglévő
Dictionary-alapú `GetOrAddLists` megoldást használja.

**Egzakt viselkedés:** a bucket kulcsa, a tile-ok face/u/v emit-sorrendje, az
`AddQuad` hívások és a későbbi determinisztikus submesh-sorrend változatlan.
Az előpassz csak kapacitást és referenciát készít elő; nem hoz létre új
renderkategóriát, nem módosít színt, geometriát vagy szimulációs adatot.

**Élő mérés (2026-09-11):** öt meleg deep-time rebuild átlaga 2 834,7 ms,
az ND-66 utáni 3 564,3 ms-hoz képest további 20,5% javulás. A dense flood
átlag 257,5 ms (`topology reused=True`), 65,6%-kal kevesebb az előző
748,0 ms-nál. A statikus base-layer 1 602,4 ms, az emit 664,4 ms; rendre
13,0% és 29,7% javulás. A felhasználó ezt az állapotot ideiglenesen
elfogadta; a `<1 s` cél nyitott backlog marad. A következő kör előtt a nagy
13,4–243,2 ms `bucketPrepare`, 97,0–309,2 ms mesh és 25,9–283,7 ms víz-
szórás miatt Unity Profiler/GC-allokációs mérés szükséges.

### ND-69 — Zoom-LOD javítás, 1. csomag: hiteles besorolás, nézetfrissítés és megőrzött frontier

**Kontextus (2026-09-11):** a külön zoom-diagnózis után a felhasználó kérte a
javítások megkezdését. Kiindulás: `195875e`; az ND-63–68 exact gyorsításai
megmaradnak. A t=0 GPU-klasszifikáció továbbra is kihagyja a secondary detail
zajt, miközben a CPU-s geometria tartalmazza. A kamerakapu csak abszolút
elmozdulást figyel; a prioritásos cut pedig budgetnél eldobja a még függő leveleket.

**Döntés:** az adaptív CPU-geometria statikus és dinamikus klasszifikációja
egyaránt a teljes CPU-modellt használja. Az óceáni ős középpontja önmagában
nem tilthatja a már az alapmesh sarkain is látható szárazföld finomítását:
a szűréshez a négy saroknak is víz alatt kell lennie. A sarokteszt cache-elt,
világváltáskor ürül; ez mintavételes védelem, **nem** bizonyított felső korlát
a tile teljes belsejére (apró szigetek konzervatív korlátja külön feladat).

A nézet aláírása testkoordinátás pozíciót/irányt, FOV-t, képarányt és
pixelméretet tartalmaz. Kis mozgás sem veszhet el végleg: legfeljebb 0,25 s
indítási késleltetés után új kérés kell, a meglévő időkapu és single-flight
megtartásával. A kész mesh a **kéréskori**, nem az alkalmazáskori nézetet igazolja.
A pixelküszöb a perspektivikus fókusztávolságból számolódik.

A prioritásos kiválasztó a függő dinamikus leveleknek előre fenntartja a
budgetet. Split csak négy gyermek számára elegendő hellyel indulhat; telített
budgetnél a már létrejött frontier durvább levelei megmaradnak, nem dobódnak el.
A külön 2:1 balance-passz korábbi pótköltségkerete egyelőre megmarad.

**Határ:** nincs Core-/seed-/világverzió-változás. Ez az első csomag, nem a teljes
M9 lezárása: statikus/dinamikus fedéscsere, beragadt chunk-geomorph, varratok,
eltolt domborzatot követő kiválasztási korlátok és upload-időkeret még külön
javítást igényelnek. Elfogadási kapu: linkelt LOD-regressziók, solution-tesztek,
Unity C# fordítás, majd élő zoom/PerfLog. Automata teszt nem igazol látványt/FPS-t.

### ND-70 — Teljes base-tile fedéscsere és geometriaérzékeny chunk-frissítés

**Kontextus (2026-09-11):** a felhasználó engedélyezte az ND-69 utáni lépést.
A mindig megmaradó statikus háromszögek eltakarhatják a finom völgyeket;
a levélhalmazra épülő chunk-diff pedig nem követi a kamerafüggő geomorphot.

**Döntés:** a CPU-s dinamikus út minden érintett base-tile alatt teljes
kvadfa-partíciót készít: a kiválasztó által kihagyott ágakat durva pótló
levelek fedik. Ezek nem új világadatok, ugyanazt a Core-mezőt mintázzák.
Csak az így teljesen kiváltott base-tile statikus terrain-indexei kapcsolhatók
ki, és csak a dinamikus mesh-ek sikeres feltöltése után. A base vertex-,
normal-, color- és submesh-adatok megmaradnak; az érintett indexintervallumok
degenerált háromszögekre cserélődnek, visszazoomnál az eredeti indexek állnak
vissza. Nincs shader/discard-maszk, globális mélységi bias vagy teljes base-remesh.

A renderelt közös sarok gazdája a legdurvább érintkező renderlevél, azonos
szintnél a legkisebb TileId. Finom–durva élen a finom pont a durva él két
feloldott végpontjából interpolálódik; a rekurzió csak csökkenő LOD-szinteken
haladhat. A morph coarse-felülete az `AddQuad` valódi 00–11 átlójú
háromszögpárja, nem bilineáris nyeregfelület. A statikus szomszéd ugyanennek
a közösél-szabálynak a base-szintű esete. A terrain radiális bias ezen az
úton megszűnik; a külön vízréteg meglévő viselkedése megmarad.

A chunkon belüli emit-sorrend stabil. A topológia-diff mellett az elkészült
csúcspozíciókat is összevetjük az utolsó sikeres feltöltéssel. Változatlan
topológiánál pozíció és bounds frissül, nem index/szín/material. Az async és
szinkron CPU-út közös előállítás/alkalmazás kódot használ. A kísérleti GPU-
geometria nem kap csendben részleges fedéscserét.

**Határ és kockázat:** a fedéspótlás és a közösél-feloldás többletmunka;
a feltöltési időkeret és a teljesen inkrementális emisszió még nincs lezárva.
A kamerafüggő morph továbbra is LOD-kérésenként, nem shaderben frame-enként
frissül. A víz/alap-border külön rétege nem része a terrain-indexcserének.
Nincs Core-/seed-változás. Kötelező a partíció-, index-visszaállítási,
közösél-/kockalapél- és morph-regresszió, Unity C# fordítás, majd élő kép/PerfLog.

### ND-71 — Mintavételezett domborzatot követő LOD-metrika

**2026-09-11, élő teszt UTÁNI státusz: aktív viewer-integráció visszavonva
az ND-72-ben.** A felhasználó nem látott érdemi élességnövekedést, viszont
súlyos lassulást mért. Az alábbi döntés a kísérlet történeti leírása.

**Kontextus (2026-09-11):** az ND-70 után a felhasználó a zoom/visszazoom
működését megerősítette, de kb. 13 görgetés után nem lát további élesedést.
A friss `PerfLog_20260911_144159.txt` szerint a kamera 114,855 után egészen
103,663 egységig közeledik és új cutok készülnek; nem igazolt hard zoom-stop.
A kiválasztó és a morph ugyanakkor még a 100 sugarú alapgömböt méri, nem
a megjelenített terepet. A pontos km-lépték külön backlog, a gyorsítás halasztva.

**Döntés:** a prioritásos CPU-út opcionális, testkoordinátás bounds-lekérdezést
kap. A viewer a négy tényleges, morph nélküli terepsarkot és a középpontot
mintázza a meglévő Core-mezőből, azonos tengerszint/relief skálával; ebből
számol középpontot és befoglaló sugarat. A láthatóság a teljes befoglaló gömböt
használja; a mintasűrűség és a morph az érintősíkbeli sarokkiterjedést méri,
az eltolt középponttól vett kameratávolsággal. A radiális magasságugrás nem
lehet szintfüggetlen tesszellációs hiba: a teljes 3D kiterjedést hibának véve
a valós mezős próba már 0,5 egység távolságnál L20 ágakra költötte a budgetet.
Ez mintasűrűségi cél, nem garantált háromszög-képernyőátmérő meredek falakon.
A kiválasztás és a geomorph ugyanazt a metrikát használja.
Az alapgömb horizont-/radiális backface-tiltása ezen az
úton nem alkalmazható: nem bizonyítja egy eltolt, lejtős patch láthatatlanságát.
A nézetkúp, pixelküszöb, hiszterézis, maxLevel és budget megmarad.
A mintapontok a meglévő sarok-cache-t használják, a bounds kérésenként cache-elt.

**Korlát:** ez a megjelenítés mintavételezett geometriai metrikája, nem a teljes
folytonos mező matematikailag bizonyított intervallumbecslése. Az öt pont
között rejlő, még nem mintázott csúcsot nem garantálja; szigorú hierarchikus
terrain-error bound későbbi munka. Nincs új zaj, Core-/seed-változás vagy
budgetemelés. A kamera alapgömbhöz kötött minimuma/felszínkövetése külön nyitott
lépés; az új metrika önmagában nem collision-megoldás. A PerfLog kapjon tényleges
legmélyebb LOD-szintet, hogy a hard limit és a látható modellrészlet elkülönüljön.
Kötelező: eltolt gömbös közelítési regresszió, ismételhetőség/budget,
morph-metrika egyezés, solution és Unity-fordítás, majd élő zoom-ellenőrzés.

### ND-72 — Az ND-71 regresszió visszavonása és renderoldali diagnosztika

**Bizonyíték (2026-09-11):** `PerfLog_20260911_172850.txt`, aktív ND-71:
122,161 / 118,144 / 106,675 kameratávolságnál a cut rendre 6820 / 6712 /
9584 ms; a teljes requestAge 8010 / 8738 / 11767 ms. Kérésenként 191 620 /
241 408 / 239 172 bounds, nadir L10 / L10 / L12. A magasságkiértékelés a
kiválasztás belső ciklusába került, még az óceáni szűrés és renderdöntés előtt.
A horizont-/backface-szűrés elhagyása tovább növelte a vizsgált tartományt.
Az ND-71 külön L14–L17 próbája nem a felhasználó kameráit mérte és nem
igazolta az elvárt látványjavulást. Az élő visszajelzés alapján a tradeoff hibás.

**Döntés, implementáció előtt:** az aktív CPU-kiválasztóból és morphból
eltávolítjuk az ND-71 domborzati callback/bounds-cache integrációját. Visszaáll
az ND-70 gömbös metrikája és korábbi cullingja. A fedéscsere, közösél-feloldás,
pozícióérzékeny upload, CPU-besorolás és budget-frontier marad. A tiszta
`SurfaceLodBounds` és opcionális API csak explicit offline kísérletként marad;
nem kerül alapértelmezett vagy Inspector-kapcsolós útra.

Az élesség további munkája előtt kérésenként **egyetlen nadír-diagnosztika**
készül: cut és tényleges terrain-renderlevél szintje, óceáni finomítás-tiltás,
helyi víz/land állapot, modellfelszíntől vett radiális távolság, morph-alfa,
a feloldott terrain-quad négy sarkának kéréskori vetített pixelátmérője.
Ez nem raycast és nem takarásvizsgálat: víz alatt a terrain-quad nem a látható
vízfelszín. Clip/near-plane keresztezésnél a pixelmérés érvénytelen, nem hamis
szám. A mátrix/viewport a kérés elején rögzített, Unity API nem fut workerben.
A diagnosztika külön időzített; legfeljebb egy új modellpontot értékel, nem
áganként terepet. A km-lépték továbbra is külön backlog.

**Következő lehetséges lépések:** (1) élő idő és kép ellenőrzése a helyreállított
úton; (2) renderhiba vs. modellrészlet vs. kamera/morph szétválasztása a
diagnosztikából; (3) indokolt domborzati finomítás esetén Build-kori hierarchikus
proxy/error-adatok, olcsó konzervatív előszűrés, majd csak érintett látható
ágak frissítése, azonos kamerás költség- és képi elfogadási kapuval.
Nem cseréljük a regressziót új, mérés nélkül aktivált heurisztikára.
Nincs új zaj, Core-/seed-változás vagy budgetemelés. A felbontási plafon
ettől még nem tekinthető megoldottnak; most a bizonyított regresszió javul.

### ND-73 — Korábbi első felosztás, rövidebb geomorph-átmenet

**Kontextus (2026-09-11):** az ND-72 után a felhasználó szerint valamivel jobb,
de későn indul a látható finomodás. A `PerfLog_20260911_182258.txt` szárazföldi
nadírja 149,319 távolságnál még L8 / 11,17 px; 140,379-nél már L9, de a morph
alfája csak 0,105. 133,060-nál az alfa még 0,388; a kész finom geometria
nagy része a durva felületre van visszahúzva. A diagnosztika olcsó (jellemzően
0,06–0,10 ms), a cut ismét tizedmásodperces, nem az ND-71 többmásodperces útja.

**Vizsgált, nem aktivált változat:** minden szinten 12→10 px csökkentés.
Azonos be-/visszazoom-próbában a csúcslevélszám 107 739→195 118 lett;
ezt a közel kétszeres többletet nem vállaljuk fel a korábbi regresszió után.

**Szűkített döntés, implementáció előtt:** csak a statikus base csomópont
első felosztásának célja legyen 10 px a scene jelenlegi 12 px-éhez képest.
A mélyebb szintek küszöbe 12 px marad, a budget 200 000. A merge-küszöb
változatlan (12/1,5=8 px), így a base tényleges hiszterézise 10/8=1,25;
visszazoomnál nem hosszabbítjuk meg az előre létrehozott levelek megőrzését.
Az első split-küszöbnek a merge-küszöb felett kell maradnia.
Az opcionális base-küszöb nélkül a LOD API bitre a korábbi viselkedést adja.
A base szülő morphja ugyanazt az új base-küszöböt használja, mint az osztás;
nem változhat csak a kiválasztó vagy csak a morph távolsága.
A geomorph tartománya 0,6→0,35: a teljes finom pozíció a split-távolság
65%-ánál elérhető a korábbi 40% helyett, születéskor továbbra is alfa=0.
Ez hangolási paraméter, nem fizikai konstans vagy új szimulációs algoritmus.

A scene szerializált mezői és a komponens alapértékei együtt frissüljenek.
Nincs új domborzati mintavétel/culling, zaj, Core-/seed-változás vagy nagyobb
budget. Aktiválás előtt azonos kamerás be-/visszazoom levélszám-ellenőrzés kell;
ha az elsőszintű előrehozás is aránytalanul drága, nem maradhat bekapcsolva.
A rövidebb morph snapshotok között észrevehetőbb átmenetet adhat: élő kép,
visszazoom és PerfLog-ellenőrzés nélkül nem vizuálisan kész.

### ND-74 — Build-kori radiális terep-proxy a LOD távolságához

**Állapot, implementáció előtt (2026-09-11):** a felhasználó a hátralévő
zoomfeladatokat sorrendben kéri, minden átadás után saját ellenőrzéssel.
Az ND-73 eredményét elutasította; a 18:39:49-es logban ugyanakkor 0,600 a
morph-range, a fájlban 0,35: az élő beállításeltérést külön ellenőrizni kell.

**Első, korlátozott lépés:** a már előállított statikus terepsarkok sugarából
immutábilis, sűrű `TerrainLodProxy` épül. A base alatti keresés a minták
maximumát összegző hierarchiát használja; base-től a helyi négy sugár bilineáris
interpolációja adja a tile-középpont becsült sugarát. A vízszint alatti proxy
a tenger sugarára korlátozott. A LOD és a morph ugyanazzal az eltolt
középponttal és sugararányosan skálázott patch-mérettel számol.
Ez mintasűrűségi/távolsági proxy, NEM új elevációmező, NEM bizonyított
képernyőhiba vagy a minták között rejlő terepcsúcs felső korlátja.

**Költség és élettartam:** level 8-on kb. 7 MiB állandó sugaradat; Buildkor
lineáris tömbfeldolgozás, nulla új Core-mintavétel. Zoomkor csak tömbolvasás
és interpoláció; nincs az ND-71-féle per-node magasságkiértékelés. Build és
cache-invalidálás cseréli/törli a proxyt. A worker kéréskori referenciát kap.
Base > 8 és GPU-geometria esetén explicit gömbös fallback, nem nagyobb rejtett
cache. A funkció külön kapcsolható összehasonlításhoz, a kapcsolóváltás
új cutot kér. Pixelcél, budget, kamera és világmodell változatlan.

**Határ:** az alapgömbös culling most megmarad; a láthatósági/parti garancia
a következő külön feladat. A proxy csak ezen az előszűrésen átjutó patch-ek
prioritását és osztását javítja. Az új út először offline, azonos kamerás
összehasonlítást kap (cut, levélszám, valódi modellhez mért proxyeltérés),
majd feltételes élő próba következik. Nem állítunk FPS- vagy képi sikert
Unity-visszajelzés nélkül. A teljes első tétel elfogadása ehhez kötött;
szigorú terrain-error bound továbbra is nyitott.

### ND-75 — A ténylegesen feltöltött tile-ok képernyőméretének naplózása

**Felhasználói kérés, 2026-09-11:** az ND-74 után a közepes zoom mintha
nem reagálna, mélyebben ismét finomodik, de a kép nem megfelelő. Most
kizárólag diagnosztika készül, nincs új LOD-, morph-, kamera- vagy budgethangolás.

**Mérési szerződés, implementáció előtt:** a sikeres Unity mesh-feltöltéshez
tartozó végleges vertex-/indexlisták megőrzött snapshotja a forrás, nem a
kért cut, a proxy vagy egy újonnan kiszámolt modellfelszín. A position-only
feltöltés és a statikus indexmaszk is követett. Csak aktív, engedélyezett,
a mérőkamerának szánt terrain/water rendererek kerülnek a snapshotba.
Az aktuális kamera és objektummátrixokkal, legfeljebb másodpercenként egy
háttérfeladatban mérünk, a LOD-worker állapotától függetlenül.

17×9 képernyőpontban a tényleges indexelt háromszögek CPU-s vetítése,
frustum-clippingje, winding-szűrése és mélységtesztje választja ki az
elöl levő tile-t. A tile a mesh négycsúcsos, kétháromszöges quadja.
Log: képernyőre vágott szélesség/magasság/átmérő pixelben; teljes vetített
quad-átmérő külön, ha érvényes; statikus/dinamikus terep és víz forrása;
mintaponttérkép, mintázott p50/p90/max. Ezek NEM minden tile-ra kiterjedő
statisztikák vagy globális maximumok. A középpont külön pontos mérete szerepel.
Zoom: tényleges középponttávolság, alapgömb feletti magasság, `R/(d-R)`
arány és a kamera meglévő nézetosztálya, FOV és viewport. Nincs kitalált
görgetésszám vagy LOD-célból visszakövetkeztetett tile-méret.

A kamera/mesh állapotának frame-je, ideje, mesh-revíziója, a LOD-kérés
folyamatban léte/kora és a diagnosztika saját ideje is logolt. Futó
háttérmérés snapshotja nem keveredhet frissebb indexekkel vagy vertexekkel.
Near-plane metszésnél clipping történik; a teljes, nem vágott quadméret
érvénytelen lehet, ezt külön jelöljük. A CPU-mérés nem GPU-pixel-visszaolvasás:
UI, felhő, marker, folyóvonal, transzparens blend, TAA és shaderbeli
vertex-mozgatás nincs modellezve. Nem adaptív/GPU-geometriai út explicit
nem támogatott, nem ad hamis érvényes mérést. Az aktuális felszínshaderek
nem mozgatják a vertexpozíciót. A teszt célja a feltöltött felszíni
geometria nagyságának és frissítési késésének elválasztása.

### ND-76 — Nézethez kötött, adagolt finomítás és valódi inkrementális emisszió

**Döntés implementáció előtt, 2026-09-11:** az ND-75 élő logban 180 px-es
középső tile és 2,5–10,6 s-os régi nézetű kérések látszanak. Álló kameránál
105,51 px-es statikus terep is marad. A felhasználó engedélyezte a javítást.

- A CPU/proxy út terepkiterjedést is tartalmazó boundsot és a tényleges
  kamera téglalap alakú perspektivikus frustumát használja. A régi alapgömbös
  horizont/backface-elutasítás ezen az úton nem előzheti meg a tereptesztet.
  A bounds kész base-sugarakból származik, nem új Core-mintákból; az ismert
  base-mintákat fedi, a finomabb valódi mezőre továbbra sem szigorú korlát.
  A bounds csak láthatóságra szolgál; az osztási méret a négy proxy-sarok
  tényleges perspektivikus vetülete. Az első offline próba elvetette a teljes
  befoglaló gömb pixelsugarát mint osztási hibát: súroló lapoknál túlosztott
  (159,34 távolságnál 109 319 levél a quad-metrika 7 045 levele helyett).
- Kérésenként korlátos számú új osztás, az előző felosztások megőrzésével.
  Kezdeti keret 1024 új osztás/kérés; a korábbi 256-os offline próbának túl
  sok hullám kellett. Ez nem milliszekundumos határ: balance és cache-miss
  további munkát okozhat. A főszálas upload továbbra is atomikus, nem streaming.
  A halasztott finomítás álló kameránál is folytatódik. A tile-budget és a
  maximális LOD nem nő. Az egyes publikált állapotok teljes fedést adnak.
- A változatlan topológiájú chunk teljes emitje csak akkor hagyható ki,
  ha a végleges, közösélekkel feloldott csúcspozíciói is egzaktul azonosak.
  A víz/border adatai is a cache részei; Build és konfigurációváltás invalidál.
- A lényegesen elavult kérés kooperatívan megszakítható. Egy sikeres
  alkalmazás előtt legfeljebb egy ilyen megszakítás engedett, hogy folyamatos
  mozgatás se éheztesse ki a megjelenítést. Cache-hez továbbra is egy worker fér.
- ND-75 megmarad összehasonlításra; a saját költségét ritkább mintavétel
  mérsékli. Új napló: feldolgozott/újrahasznált levelek, halasztott osztások,
  megszakítás és az új nézetmetrika aktív állapota.

Ez viewer-változás, nincs Core-, seed- vagy modellváltozás. A pixelcél
egységes hangolása és további optimalizáció csak a friss élő próba alapján;
a tesztek/Unity-fordítás nem helyettesítik a vizuális elfogadást.

### ND-77 — Tereptile-azonosság és kiválasztási megállás összekötése

**Döntés implementáció előtt, 2026-09-11:** az ND-76 élő próbában
(`PerfLog_20260911_205525.txt`, 20:56:23) befejezett finomítás mellett is
88,64 px-es dinamikus tereptile szerepel. Az ND-75 nem őrizte meg a tile
azonosságát és a konkrét kiválasztási megállást; a proxy/morph/szomszéd
hibaforrások között ebből nem lehet bizonyítékkal választani.

- A CPU terep konkatenált quadjai explicit TileId-t kapnak, az emissziós
  bucketek sorrendjében. A hozzárendelés nem vertex-pozícióból visszabecsült.
  Pozíciófrissítés és cache-újrahasználat megőrzi a hozzárendelést.
- A kiválasztás opcionálisan rögzíti a tényleges megállási okot, hibát és
  küszöböt. Csak sikeresen alkalmazott kérés trace-e kerül a snapshotba;
  a worker nem olvas Unity objektumot vagy változó cache-t a mérés során.
- A ritka ND-75 mélységtesztelt minták legnagyobb tereptalálataihoz
  mesh-azonosság, TileId, request-metrika, megállási ok és feltöltött
  sarokpozíciók kerülnek. A balance/fedés által létrehozott levél nem
  kaphat hamisan saját kiválasztási döntést: az őst külön jelöljük.
- A `lodPending=False` önmagában nem kész állapot: a napló a halasztott
  finomítás folytatását is jelzi. Mérési költség továbbra is külön látható.

Ez az engedélyezett 1. lépés bizonyítékgyűjtő része. A finomítás, morph,
víz, budget és Core változatlan; javítást csak az azonosított okra végzünk.
Új élő próba szükséges, ez önmagában nem felbontásjavítás.

### ND-78 — Későn eldobott tengerfenék-finomítás előzetes kizárása

**Döntés és méréssel korrigált terv, 2026-09-11.** Az ND-77 élő logban
(`PerfLog_20260911_212059.txt`, 21:21:17 és 21:21:53) álló kameránál,
befejezett kéréslánc mellett két statikus tereptile 28,816 / 31,678 px.
Becslésük 9,194 / 7,176 px, megállásuk `below-threshold`. A feltöltött
sarkok újravetítése mindkét értékpárt reprodukálja: a kis érték oka a mély
sarkok `max(seaRadius, cornerRadius)` helyettesítése. Ez méretdefiníciós
eltérés is: az ND-75 a teljes clipped quadot méri, a víz által takart
részeket nem vágja le a tile méretéből.

**Elvetett kezdeti változat:** a teljes nyers mélység használata a proxy
quadjában/boundsában. A mérés már 300-as távolságnál 17 ezer, közepesen
60–67 ezer tereplevelet adott, a régi 0 / ~1 ezer helyett, a későbbi
óceáni kizárást előrehozva is. A mély, részben víz alá nyúló partfalak
tömeges felosztása így nem arányos javítás. A kísérleti runtime-módosítás
visszavonva; a proxy, morph és távolságmetrika az ND-76 állapot marad.
A kísérlet a diagnosztikai próbában reprodukálható, nem éles feature.

**Átadásra választott részjavítás:** a renderer meglévő
`IsBaseAncestorOceanic` kizárása már a statikus alapszint kiválasztásánál
lefut, mielőtt leszármazottak vagy új osztási kvóta fogyna. Pontosan a
meglévő feltétel: víz alatti középpont és mind a négy base-sarok víz alatt.
Vegyes parti tile-t nem zárunk ki. A későbbi renderoldali szűrő megmarad.
A callback csak a base-szinten és csak a CPU/perspektivikus proxy-úton
aktív, a kész statikus besorolást/sarkakat olvassa, új Core-minta nélkül.

Az 1024-es munkakeret így a valóban megjeleníthető terepet szolgálja.
Új trace-ok: `renderer-base-exclusion`, `skippedSelectionBases`,
`earlyOceanExclusion=ND78`. A vízréteg, budget, pixelcél és seed változatlan.

Az offline, két nézetirányos, 14 állásos próbában az érdemben renderelt
terep TileId-halmaza azonos maradt. Egy közepes állásnál 11→8, mélyebben
14→13 hullám kellett. Ezek kiválasztási adatok, nem Unity frame-idők.
Az ND-77-ben állandó `split-quota` miatt váró levelek késésére célzott
részjavítás; nem oldja meg a 28–32 px-es teljes-quad eltérést, a statikus
víz durvaságát vagy a teljes látható-terep hibakorlátját. Élő próba kell.

### ND-79 — Korai zoom minőségi küszöbének költségvizsgálata

**2026-09-11, mérési döntés; nem runtime-javítás.** Az ND-78 utáni
`PerfLog_20260911_214108.txt` álló, befejezett közepes nézetében a
9,939 és 9,705 pixeles statikus tereptile proxyja pontosan ugyanekkora.
A `below-threshold` megállás 10 pixeles célt használ. A korai finomodás
hiánya itt igazolt minőségi küszöbkérdés, nem proxyhiba vagy workerkésés.

Az offline `--quality` próba kisebb, egységes céljai korábbi finomítást
hoznak, de a 6/5 pixeles cél egyik közepes állásában 2 930→96 969
dinamikus levelet és 728→23 640 L8-chunkot adnak. A globális küszöb
csökkentését ezért **nem aktiváljuk teljesítményelfogadás nélkül**.
Ez nem a 6/5 cél végleges elvetése: a jelenlegi feldolgozási/renderer
szerkezettel túl nagy regressziós kockázatot jelent.

Javaslat a következő implementációra: korlátos méretű hierarchikus
chunk-csomagolás, majd építési/upload-költségkorlát és a kisebb pixelcél
együttes validációja. A fix L6-chunk nem elegendő, mélyen ismét több
tízezer levelet vonna egyetlen újraépítésbe. A felhasználótól a megjelenítés
előzetes átalakítása és az azonnali, lassabb minőség között irányt kértünk.
A pontos algoritmus implementáció előtt külön döntést igényel.

[Logelemzés, összehasonlítás és korlátok](reviews/lod-onset-cost-analysis-nd79-2026-09-11.md).
A runtime, scene, seed és világmodell ebben a lépésben változatlan.

### ND-80 — Levélszámmal korlátozott hierarchikus renderchunkok

**Döntés implementáció előtt, 2026-09-11.** A felhasználó az ND-79
költségvizsgálat után előbb a megjelenítés átalakítását választotta.
Az első, külön átadandó lépés a CPU-terep csomagolása; a LOD-kiválasztás,
pixelcél, világmodell, morph és vízgeometria változatlan marad.

- A teljes fedés/közösél-feloldás után a leveleket a megadott legdurvább
  chunkszint ősei alá gyűjtjük (kezdőérték L6). Az ennél durvább bemeneti
  levél önálló marad. A 256 levélnél nagyobb csoport négy gyermekterületre
  oszlik rekurzívan; nem bontjuk magukat a terepleveleket.
- Stabil kulcs a chunk területének TileId-ja. Az előző sikeresen feltöltött
  partíció osztásait megtartjuk 128 levél felett; 128 vagy kevesebb levélnél
  engedünk összevonást. A 256 kemény korlát, a 128 hiszterézis, nem pixelcél.
  Üres csoport nincs; minden bemeneti levél pontosan egy csoportba kerül.
- A már működő topológia/pozíció-diff és emit-cache változatlanul a teljes
  csoportot kezeli. Split/merge esetén az új kulcsok felépülnek, a régiek
  kikapcsolódnak ugyanabban a főszálas alkalmazásban. Megszakított munka
  nem módosítja a publikált partíciót. A régi fix csoportosítás kapcsolóval
  elérhető összehasonlításhoz, a scene meglévő értékeit nem írjuk felül.
- Naplózzuk a csoportosítás idejét, a tényleges csoportméret-maximumot,
  a használt korlátot és a módot. A csoportszám nem GPU draw-call mérés.

A korlát egy chunk emissziójának/feltöltésének méretét fogja meg, nem egy
teljes kérés milliszekundumos költségét. Több kis chunk összevonása több
változatlan levél újraemisszióját is okozhatja; ezt az élő logból külön
ellenőrizzük. A főszálas upload több frame-re bontása, objektumpool és a
minőségi küszöb csökkentése nem része ennek az első lépésnek.

### ND-81 — Pontos vetület-cache az álló kamerás finomítási hullámokhoz (2026-09-11)

Az ND-80 második élő próbájában 31 946 levélből csak 64 igényelt új
emissziót, mégis 323,68 ms volt a teljes cut és 188,82 ms az emit szakasz.
Első, korlátozott lépésként a változatlan vetület ismételt kiértékelését
hagyjuk el; nem vezetünk be korábbi döntéseket megőrző kiválasztási frontot.

- Egyetlen workerhez tartozó cache tárolja a `(látható, szöghiba)` eredményt
  TileId-nként. A kamera pozíciója, normalizált tengelyei, vetítési és vágási
  paraméterei pontosan egyezzenek; a terep-proxy objektumazonossága kötelező.
  Kameraváltás, proxycsere, Build és nem perspektivikus mód érvénytelenít.
- Legfeljebb 262 144 bejegyzés; telítettségnél a hiányzó értéket továbbra is
  kiszámoljuk, csak nem tároljuk. A korlát nem változtathatja meg a cutot.
- A hiszterézis, prioritási sor, splitkvóta, óceánkizárás, trace és 2:1 balance
  továbbra is minden kérésben lefut. Nem tárolunk split/merge döntéseket.
  A geomorph ugyanebből a pontos metrikából olvashat a geometria workerében;
  a párhuzamos képernyődiagnosztika kizárólag az immutábilis view-t olvassa.
- Külön mérjük a selection/balance és geometria-feloldás/aux-másolás idejét,
  valamint a cache találatait és a valódi metrikaszámításokat.

A 12/10 px cél, az ND-80 chunkcsomagolás, a terepgeometria, a víz és a Core
változatlan. Ez az ismételt munka első csökkentése, nem a teljes késleltetés
megoldása. Azonos bemenetsorozatra azonos cutot és trace-t kell igazolni;
az élő gyorsulás és a vizuális eredmény külön felhasználói próbát igényel.

### ND-82 — Önálló vízfelszín-LOD, külön kiválasztási és renderkapu

**2026-09-11, döntés implementáció előtt.** A felhasználó az első zoomok
akadásának javítását backlogra halasztotta, és a következő tervezett tile-
feladatot kérte. A vízfelszín jelenleg a terep emissziójához kötött, miközben
a mély óceáni terep finomítását szándékosan kihagyjuk (ND-78).

1. Első, külön átadandó kapu: motorfüggetlen viewer-modul a víz saját cutjához.
   Bemenet a ténylegesen emittált statikus víz-base-tile-ok halmaza, a modellből
   származó, rendererrel azonos tengerszintsugár és a kamera. Nem a tengerfenék
   kihagyási maszkja, és nem új, közelítő óceán-/jég-besorolás.
2. Újrahasználjuk a tesztelt prioritásos kvadfát, pontos perspektivikus
   quad-metrikát, hiszterézist, splitkvótát, teljes fedést és közösél-resolvert.
   A vízhez külön állapot és külön levél-/munkakeret tartozik. A víz-proxy
   minden sugarán a megadott tengerszint áll; nem hívunk elevációt, klímát,
   hydrologyt vagy terep-emissziót. Új Core-algoritmus/seedváltozás nincs.
3. A part/alap vízmaszkja ebben a kapuban változatlan. Csak létező víz-base
   alatt keletkezhet gyermek. Minden lecserélhető base teljesen fedett;
   nincs részleges base-elrejtés. A fel nem osztott alap marad helyettesítő.
   A geometriai terv a finom és durva vízszéleket közös élre illeszti.
4. A következő kapu kötelező a runtime aktiváláshoz: víz-attribútumok hiteles
   forrása, vízre külön indexmaszk, a régi/dinamikus víz kettős rajzolásának
   megszüntetése, atomikus csere és visszaállítás. Ezt nem kapcsoljuk be
   egy pusztán tesztelt kiválasztó alapján. A GPU-kísérleti út és tavak saját
   vízszintje külön kompatibilitási ellenőrzést igényel.

Az első kapu önállóan tesztelhető, a futó viewernek nem ad új munkát és nem
változtat képet. A teljes vízfinomítás csak a második kapu és élő Unity-próba
után tekinthető késznek. A korai zoomküszöb és a halasztott ND-81 panasz
ebben a feladatban nem módosul.

### ND-83 — Önálló víz-LOD renderbekötése

**2026-09-11, döntés a kód előtt.** Az ND-82 második kapuja a CPU,
perspektivikus adaptív útba kerül, ugyanabba a single-flight workerbe.
Külön víz-cut: legfeljebb 8192 levél, kérésenként 256 új osztás, a meglévő
normál pixelcél/hiszterézis mellett. A korai terep-zoomküszöb változatlan.

- A statikus emit víz-bucketenként feljegyzi az emittált TileId-kat; ezekből
  készül a vízforrás és a ritka indexmaszk. Hiányzó (száraz) base nem rejthető
  el. A sugár a statikus renderer pontos float értéke.
- A víz geometriájához nincs tengerfenék-normál vagy biome-kiértékelés.
  A szín a nyers, morph nélküli modellmagasság és a meglévő ND-60/overlay
  színfüggvény eredménye; korlátos, Buildenként érvénytelenített cache.
  A pozíció és az RGB azonos topológiájú közösél-resolveren megy át.
- A statikus vízmaszk alatti terrain-vezérelt víz-emisszió kimarad;
  száraz base alatt a meglévő finom parti víz viselkedése megmarad.
  A tavak külön objektuma/vízszintje nem változik. A GPU és nem támogatott
  nézet a régi vízútra áll vissza, új vízmaszkolás nélkül.
- Két váltott vízobjektum: az új mesh teljes feltöltése után, egy főszálas
  alkalmazáson belül történik a statikus maszk és a látható objektum cseréje.
  Megszakított worker nem publikál. Uploadhibánál teljes statikus vízre
  állunk vissza, mindkét dinamikus vízobjektum kikapcsolásával.
- A víz külön selection/emit/upload ideje és levelei naplózottak. Az ND-75
  tényleges geometriamérése a maszkolt statikus vízquadokat nem számolja.
  A víz további osztásigénye álló kameránál is új munkakört indít.

A víz geometriai finomítása nem ígér új fizikai részleteket a modell
felbontásán túl. A több frame-es feltöltés, víz-geomorph és az első zoomok
halasztott hibája nem része ennek a lépésnek. Élő part/óceán/visszazoom és
paraméterváltás-próba kell a vizuális és teljesítmény-elfogadáshoz.

### ND-84 — Kamerafüggő, fizikai kilométer-lépték a megjelenített felszínen

**2026-09-11, döntés implementáció előtt.** A lépték nem görgetésszámból,
LOD-szintből vagy tile-méretből következik. Referenciapontja a kamera
viewportjának láthatóan megjelölt közepe. A kívánt képernyőszélesség két
végpontjából képzett sugarakat a viewer aktuális radiális felszínével metsszük:
szárazföldön a renderrel azonos, tengerszint körül túlrajzolt domborzattal,
vízen a renderelt tengerszintsugárral. A bolygó pozícióját és forgását az
inverz transzformáció kezeli; nem egységes skála esetén a kijelzés érvénytelen.

A két metszéspont normalizált iránya közti középponti szöget a Core kanonikus
`PlanetConstants.RadiusMeters` értékével szorozzuk. Így a kijelzett hossz a
referencia-gömb nagy köríve, nem a túlrajzolt terepen megtett út és nem
Unity-egység. A domborzat csak azt határozza meg, hogy a képernyősugár hol éri
el a látható felszínt. A felirat 1/2/5-ös „szép” m vagy km értéket mutat; a
hozzá tartozó pixelszélességet determinisztikus felezéssel oldjuk meg, ezért
a kerekítés nem válik pontatlan, lineáris pixelbecsléssé.

Ha a középső referencia környezetében bármelyik sugár nem metszi a felszínt,
a kamera nem perspektivikus, a szükséges modell-snapshot még nem él, vagy a
transzformáció skálája nem egységes, a panel explicit érvénytelen jelet mutat.
A számítás tisztán viewer/UI-oldali, a tile-kiválasztást, mesh-emissziót,
világmodellt és seed-kompatibilitást nem változtatja. A gömbmetszés,
nagy köríves távolság, szépérték-választás és megoldási hibakorlát külön
UnityEngine-független regressziókat kap. Élő Unity-próba kell az elrendezés,
a középjel és a teljes zoomtartomány ellenőrzéséhez.

Az első élő próba visszajelzése alapján egyetlen átmeneti felszínmetszési hiba
nem törölheti azonnal az utolsó hiteles értéket. A kijelzés nyolc egymást követő
sikertelen mintáig megtartja azt; biztos konfigurációs hiba esetén továbbra is
azonnal érvénytelen. A radiális iteráció tűrése a renderelt `float` háló
pontosságához igazodik és csillapítást használ a part-/domborzatperemeken.
Közeli kameránál, ha a gyors segédgömb-iteráció egy köztes sugár miatt feladja
a metszést, előjeles sugárparaméteres gyökkeresés adja az első tényleges
radiális felszínmetszést. Ez csak tartalék út, ezért a szokásos közepes és távoli
nézet mintavételi költségét nem növeli.

### ND-85 — Több képkockás terep-upload, egyetlen fedésváltással

**2026-09-12, döntés implementáció előtt.** Következő tile-feladat az
ND-83 után: a CPU async, chunkolt út terepfeltöltésének szétosztása.
Nem módosítjuk a cutot, a pixelcélt, a morphot vagy a Core-t.

- A kész workereredmény feltöltési tranzakcióvá válik. Legfeljebb 64 mesh
  és puha 2 ms keret jut egy Update-ra; legalább egy mesh elkészül, mert
  egy Unity uploadhívást nem lehet megszakítani. A 2 ms nem kemény FPS-garancia.
- Minden megváltozott/pozíciófrissített chunk inaktív tartalék Mesh-be kerül.
  Nem rajzolható és nem kerül a tényleges geometriadiagnosztikába. A normál
  position-only út ebben a módban teljes tartalékfeltöltés: több összmunka,
  cserébe a régi mesh a következő frame-ekben is változatlan marad.
- Az összes terepmesh elkészülte után külön frame egyben váltja a mesh-
  referenciákat, anyagokat, aux-rétegeket és statikus fedésmaszkokat.
  A cut/cache/diagnosztikai kamera csak ekkor válik publikálttá.
- Új worker nem indul staging közben. Világ-/LOD-konfiguráció-váltás,
  explicit Build és adaptív mód kikapcsolása eldobja a még nem publikált
  tranzakciót. Puszta kameramozgás nem éhezteti ki az uploadot: az elkészült
  kérés megjelenik, majd a következő kérés felzárkózik az aktuális kamerához.
- Staginghiba nem érinti a régi képet; commit-hiba a meglévő statikus
  fallbackot használja. A tartalék mesh chunkonként újrahasználható; a
  meglévő chunk-cache mellett legfeljebb egy további mesh/chunk marad.
  Build/komponens-megszűnés felszabadítja a tartalékokat. Ez többletmemória,
  nem általános chunk-cache memóriakorlát vagy objektumpool-javítás.
- Külön stageFrames/stageTotal/maxSlice/commit és feltöltött chunk/vertex
  mérés szükséges. A water/border upload, a maszk és aktiválás ebben az
  első kapuban még egyetlen commit-frame költsége. Ezek további bontása és
  a teljes főszálas keret csak élő mérés alapján következik.

Az új út kapcsolható, az alapérték bekapcsolt; szinkron/GPU/nem chunkolt
út változatlan. A vizuális elfogadás (lyuk, villanás, zoom/visszazoom,
világváltás) és az FPS-hatás élő Unity-próbát igényel.

### ND-86 — Víz és határvonal előkészített feltöltése

**2026-09-12, döntés implementáció előtt.** Az ND-85 élő próbájában
284 staging-adag p90 ideje 1,29 ms, maximuma 3,65 ms; 86 commit maximuma
15,83 ms. A külön vízpublikációs szakasz maximuma 10,71 ms (maszkolással
együtt, nem tiszta GPU-upload mérés). Következő kapu az aux-feltöltés.

- A dinamikus víz-bucketek konkatenálása és bounds-számítása a meglévő CPU
  workerre kerül. A sorrend, vertex/szín/normal és indexek nem változnak.
  A statikus/legacy szinkron vízépítést nem módosítjuk.
- A dinamikus parti víz, az önálló óceánvíz és a border külön staging-
  munkaként követi a terepet, ugyanabban a 2 ms/64 feladatos puha keretben.
  Üres/kikapcsolt réteg explicit üres eredményt kap; csak a commit rejti el
  az előzőt. Azonos önálló víz-cut továbbra sem kér új vízmesh-t.
- Az aux-mesh-ek sem kapnak renderert a feltöltés során. A commit csak
  mesh/anyag/diagnosztika-referenciát és láthatóságot vált, majd alkalmazza
  a fedésmaszkokat. A teljes terep+víz+border egy tranzakció.
- Rétegenként egy plusz tartalék mesh tárolható, Build/OnDestroy takarítja.
  Az önálló víz meglévő két bufferén felül egy közös tartalék használható.
  Staginghiba/megszakítás a régi képet hagyja; commit-hiba a meglévő statikus
  fallback. Nem változik a LOD-kiválasztás, színmodell, tavak vagy pixelcél.
- A vízkonkatenálás, aux-staging, terepcsere, legacy aux-publikáció,
  terrain/water maszk és cache-eviction külön mérhető lesz.

A natív meshhívások még nem részekre bontott bufferfeltöltések: egy nagy
víz/border job túllépheti a puha keretet. A maszk és a referenciaváltások
egy frame-ben maradnak. A 10,71 ms okának pontosabb elválasztását és az
új költségeloszlást élő loggal kell igazolni; FPS-javulás nem előlegezhető.

### ND-87 — Terep-rendercélok előkészítése és a publikálás felbontása

**2026-09-12, implementáció előtt.** Az ND-86 élő logban a 7,88 ms-os
commitból 6,30 ms a tereppublikálás; belső költségei még nincsenek külön mérve.
A felhasználó jóváhagyta e szakasz mérését és előkészítésének leválasztását.

- A meglévő staging-job a mesh mellett a rendercélt is előkészíti. Új
  GameObject még a komponensek felvétele előtt inaktív; meglévő célhoz
  staging alatt sem mesh-, sem anyag-, sem aktivitásmódosítás nem tartozik.
- A MeshFilter/MeshRenderer referenciája és a diagnosztikai adatburkoló
  előre készül. A publikált diagnosztikai térkép csak commitkor változik.
- Új célok a meglévő chunk-cache-be kerülnek. Commit előtti megszakításkor
  csak e kérés új céljai törlődnek; a korábbi cache és a látható kép marad.
  Commit-hiba után a meglévő statikus fallback takarítja a fedést, a célok
  a normál cache-életciklusban maradnak. Nem általános objektumpool.
- Külön idő: staging mesh/anyag, cél-/diagnosztika-előkészítés; commit
  mesh-/anyagcsere, diagnosztikai publikálás, aktiválás és eltűnő célok
  kikapcsolása. Új/újrahasznált célok száma is naplózandó.
- A közös terep/víz/border/maszk fedésváltás, puha 2 ms/64 job keret,
  a legacy út, Core, kiválasztás és zoomküszöb változatlan.

Az objektumkészítés dominanciája hipotézis; a részidők és új élő próba
igazolják a hatást. Egy job továbbra is túllépheti a keretet, a staging
összideje nőhet; a teljes ~669 ms-os kéréskésés megoldása nem e lépés célja.

### ND-89 — Előkészített terepfedés-maszk, változatlan GPU-publikálással

**2026-09-12, implementáció előtt.** A friss ND-87 logban 257 commit során
a tereppublikálás maximuma 1,98 ms; a 9,85 ms-os commitból 7,95 ms a maszk.
Nem ismert még e maszkidő CPU/natív bontása. Következő lépés csak a terepmaszk.

- A maszk következő állapota előkészíthető tranzakció: validált, másolt
  gyökérhalmaz, elrejtési/visszaállítási offsetek és rendezett upload-range-ek.
  Az előkészítés sem a jelenlegi indexeket, sem a rejtett tile-okat nem írja.
- Az ND-85–87 főszálas staging-sor végére kerül egy maszktervezési job, a
  következő diagnosztikai snapshot is ott készül. Nincs worker-hozzáférés
  az élő maszkhoz, teljes statikus indexbuffer-másolat vagy új GPU-mesh.
- A terv tulajdonoshoz és monoton revízióhoz kötött; idegen/elavult vagy
  már alkalmazott terv az indexek írása előtt elutasítandó. Eldobáskor nincs
  visszagörgetendő maszkállapot. A közös commit írja a CPU-indexeket és
  végzi a régi részleges SetIndexBufferData hívásokat, azonos tartományokkal.
- Változatlan fedésnél nincs új diagnosztikai snapshot és natív feltöltés.
  Legacy SetHidden megmarad prepare+apply kompozícióként; hibafallback
  továbbra is visszaállítja a statikus fedést. A vízmaszk út nem változik.
- Külön mérés: maszktervezés/snapshot előkészítése, CPU-alkalmazás, natív
  indexfeltöltés, snapshot-publikálás és range-szám. A 7,95 ms csökkenése
  csak élő mérésből állítható; a natív feltöltés még nem több frame-es.

A mask-job is monolitikus, túllépheti a puha 2 ms-ot. Az ND-87 25,76 ms-os
stageTarget-tüskéje külön nyitott kockázat, oka nincs izolálva. A teljes
kéréskésés, mozgókamerás selection és élességi/proxyhiba továbbra is backlog.

### ND-88 — Fizikai 1:1 függőleges relief a kilométer-lépték mellett

**2026-09-12, felhasználói vizuális visszajelzés alapján.** Az ND-84 pontos
vízszintes léptéke láthatóvá tette, hogy a scene korábbi domborzati beállítása
nem fizikai skálát használt. A `radius=100`, `elevationScale=0.001` és
`terrainReliefExaggeration=1.5` együtt a kanonikus 7 420 km-es bolygón
111,3-szoros függőleges túlrajzolást jelentett. Emiatt egy körülbelül 1,8 km-es
modellbeli tektonikus perem nagyjából 200 km magasnak látszhatott a léptékhez
viszonyítva.

A viewer új `usePhysicalReliefScale` kapcsolója alapból igaz. Ebben a módban
az `elevationScale = radius / PlanetConstants.RadiusMeters`, a külön relief-
szorzó pedig 1; a `Start` és az Inspector-változások `OnValidate` útja is
szinkronizálja a szerializált értékeket. Kikapcsolva a korábbi művészi skála
és függőleges túlrajzolás továbbra is használható. Ez kizárólag viewer-
geometriai változás: a Core elevációt, tengerszintet, hidrológiát, seedet és
world package-et nem módosítja.

A mérés egy másik, valódi modellhiányt is feltárt: a deep-time erózió csak a
legfeljebb 1500 m-es lemezhatár-upliftet relaxálja, miközben az óceáni
(-4000 m) és kontinentális (+800 m) kéregbázis a legközelebbi lemez ID-jével
diszkréten válthat. Ennek folytonos átmenete külön, seed-kompatibilitást és
referenciavektorokat érintő Core-döntés; az ND-88 nem rejti el rendereroldali
clamp-pal. Előbb az 1:1 megjelenítést kell élő Unityban ugyanazon a helyen
ellenőrizni, utána a megmaradó fizikai peremmagasság mérhető és kalibrálható.

### ND-91 — Helyi modellfelszínt követő orbitkamerakorlát, első kapu

**2026-09-12, implementáció előtt.** Az upload után a felhasználói sorrend
következő önálló tétele a felszínkövető kamerakorlát. A ND-89 logokban a
natív terepmaszk maximuma 0,59 ms; további upload-átalakítást ebből nem
indokolunk. A különböző relief/viewport miatt nincs kontrollált A/B mérés.

- A kamera a nadír irányában az ND-84 meglévő modellfelszín-lekérdezését
  használja (tenger vagy túlrajzolt szárazföld), nem új Core-algoritmust.
  Pozitív, egyenletes target-skálát támogat; más transzformációnál fallback.
- A zoom és forgás helyi felszín feletti magassággal arányos. A minimális
  rés a korábbi `minDistance - surfaceRadius`, legalább 0,001 világ-egység
  és a near clip 1,1-szerese. Ütközéskor kifelé korrekció történik; ha a
  maxDistance ennél kisebb, a felszínkorlát élvez elsőbbséget.
- A korlát minden transzformációra érvényes, beleértve a FlyTo-t és LateUpdate
  újraellenőrzését. Nem módosítjuk a near clipet, scene-t vagy reliefet.
- Pontosan azonos lokális irány + világ-snapshot esetén cache-találat;
  puszta zoom nem indít ismételt modellmintavételt. Konfigurációváltás alatt
  nincs kevert modellminta; átmenetileg az előző sugár/alapgömb a fallback.
- Kapcsolható, alapból aktív. Külön log jelzi a modell/fallback forrást,
  mintavételi időt, tényleges távolságot, magasságot és korrekciót.

Ez **első, pontmintás kapu**, nem teljes rendergeometriai ütközésgarancia:
a durva/morpholt háromszög, meredek oldal és a near-plane sarkainak
ütközése eltérhet a nadír modellmagasságától. Tavak külön vízszintje sem
része az ND-84 lekérdezésnek. Ezek, a forgás közbeni mintavételi költség és
az élő zoom/FlyTo elfogadás nyitott. Nem tile-élesség- vagy selection-javítás.

### ND-90 — Folytonos vegyes kéregátmenet és 1 km-es uplift-plafon

**2026-09-12, döntés implementáció előtt, felhasználói élő megfigyelés
alapján.** Az ND-88 megszünteti a 111,3-szoros megjelenítési túlrajzolást,
de a Core elevációmezőjében is van falszerű lemezhatár. A legközelebbi lemez
ID-jének átbillenésekor az óceáni és kontinentális kéreg teljes alap-elevációja
(-4000/+800 m bázis és eltérő zajamplitúdó) egyetlen pontban válthat. A
deep-time erózió eddig csak a külön uplift-bónuszt relaxálta, ezt a lépcsőt
nem érintette.

A két legközelebbi lemez gap értékét a báziselevációhoz is felhasználjuk.
Csak eltérő kéregtípusnál, `0.005` gap-határzónán belül a nyertes és
második lemez ugyanazon pozícióban számolt báziselevációját keverjük. A második
lemez súlya a határon 0,5, a zóna külső szélén 0; a kettő között polinomiális
smoothstep fut. Így a két oldal ugyanahhoz az átlaghoz tart, a zóna szélén a
meredekség is folytonos, és nincs új transzcendens művelet. Az `isOceanic`
továbbra is a legközelebbi lemez anyagtulajdonsága; a keverés csak az
elevációt folytonosítja. A sáv szándékosan keskenyebb a `0.04` uplift-zónánál:
elég széles a pontszerű 4,8 km-es lépcső megszüntetéséhez, de kevésbé írja át
a kontinens- és fix víztérfogat-topológiát.

A kanonikus level-6 fix víztérfogat-próba 250 Myr-nél 95,036% vizet ad.
Ez a korábbi 95%-os durva összeomlásőr határát mindössze 0,036 százalékponttal
lépi át, miközben a 0 szélességű (diszkontinuus) változat átmegy. A kapu felső
határa ezért dokumentáltan 96%-ra módosul; nem termékcél vagy célzott vízarány,
csak annak őre, hogy a megőrzött víztérfogat ne omoljon 100%-os borításba.

A tektonikus „felgyűrődés” külön komponensének felső korlátja 1500 m-ről
1000 m-re csökken. Ez pontosan érvényesíti a felhasználó által kért legfeljebb
1 km-es upliftet; a teljes felszíni eleváció természetesen lehet magasabb a
kontinentális alap és a valódi domborzati zaj miatt. Nem alkalmazunk
tengerszinthez kötött renderer-clampet, mert az elrejtené a modellhibát és
szétválasztaná a render/panel/hidrológia forrását.

Ez numerikus világkép- és seed-kompatibilitást törő módosítás. A `.worldpkg`
formátum 2-re emelkedik és az 1-es csomagok betöltése explicit hibát ad. A
Python referencia az elsődleges orákulum; a crust/plate-boundary/deep-time,
hidrológia, tavak, folyók, feature, vulkanizmus és state-hash downstream
vektorait újra kell generálni, majd byte-szinten ellenőrizni. Külön regresszió
igazolja a határon vett kétoldali folytonosságot, az 1 km-es uplift-plafont,
a cache-elt/nem cache-elt út bitazonosságát és a világplauzibilitást. Élő Unity
vizuális elfogadás továbbra is szükséges ugyanazon problémás peremnél.

**Implementáció utáni ellenőrzés (2026-09-12):** a teljes Python downstream
lánc újragenerálva; a level-5 lemezhatár-minta 16,4%-a kap upliftet, mért
maximuma 726,6 m, míg a szintetikus határteszt egzakt 1000 m-es plafont
igazol. A kanonikus level-6 világ 31 kontinenst és 421 legalább öt tile-os
régiót ad, a state hash
`dd685aac9df276053fcb6bb58cffa38100a725961065d0a7688b1aff19472f12`.
A referencia- és tesztpéldányok bájtra azonosak; Core 384/384, CLI 8/8 és
viewer-LOD 290/290 teszt zöld, a Release solution build 0 warning/0 error.

### ND-92 — Teljes statikus terepmaszk-helyreállítás commit-hibánál

**2026-09-12, javítás előtt.** Az ND-89 `ApplyPrepared` már átírja a CPU
indexeket és a rejtett halmazt a részleges natív feltöltés előtt. Ha a
feltöltés félbeszakad, a GPU még olyan régi rejtett quadot tartalmazhat,
amelyet a CPU már visszaállítottnak tekint. A korábbi `SetHidden(empty)`
hibaág ezt nem küldi újra: három eltérő megszakítási ponttal reprodukált
tesztben maradnak degenerált GPU-indexek a statikus fallbackon.

- Csak a commit hibaágában explicit `RestoreAll` állítja vissza a teljes
  CPU-indexbuffert az eredeti másolatból, üríti a rejtett halmazt és új
  revízióval érvényteleníti a függő terveket. A visszaadott teljes tartomány
  akkor is feltöltendő, ha a CPU szerint már semmi sem rejtett.
- A viewer ezt egy teljes natív indexfeltöltéssel publikálja. A diagnosztika
  csak a sikeres feltöltés után válik teljesen láthatóvá. A szokásos sikeres
  commit részleges feltöltése és annak költsége nem változik.
- A vízmaszk ugyanazt a CPU-előbb / részleges GPU-utána protokollt használja,
  ezért a közös commit hibaágában az is `RestoreAll`-t kap. A két réteget
  egymástól függetlenül próbáljuk helyreállítani; egyik hibája nem állítja
  meg a másik kísérletét. A víz normál commitja változatlan.
- Ha a helyreállítás is hibázik, a félkész dinamikus célok kikapcsolása és
  a korábbi cut-cache érvénytelenítése akkor is lefut; az eredeti és a
  helyreállítási kivétel együtt megmarad. Tartós natív hiba esetén nincs
  helyreállítási garancia vagy sikeres statikus fedést állító diagnosztika.

Ez nézetoldali hibabiztonság, nem a normál zoomélesség/késés javítása.
Core, relief, kamera és kiválasztási küszöb változatlan. A natív integráció
Editor-tesztje külön futtatást igényel; a szimulált bufferpróba nem GPU-teszt.

### ND-93 — Nem használt terep-renderer cache és mesh-életciklus

**2026-09-12, implementáció előtt.** A felhasználó 2–3 feladat együttes
átadását kérte. Első tétel a nyitott rendercache-erőforráskockázat: a
`RemovedChunkRoots` csak kikapcsolta az objektumokat, a chunk- és spare-
térkép világváltásig növekedhetett. GameObject törlése önmagában nem
helyettesíti az általunk létrehozott runtime Mesh explicit felszabadítását.

- Legfeljebb 128 nem használt chunk-kulcs megtartása a cél. A legrégebben
  használaton kívülivé vált kulcsok kiürítése puha 0,5 ms / 8 kulcs/frame;
  ez nem azonnali kemény memóriakorlát. Aktív/publikált fedés nem eviktálható.
- Staging közben nincs cache-takarítás; worker közben kizárólag a Unity-
  erőforrásokat érinti, a worker által olvasott CPU chunk-cache-t nem.
  Újrahasználat kiveszi a kulcsot az inaktív sorból; megszakítás visszateszi
  a nem publikált, már nem szükséges tartalékokat. Nincs teljes frame-enkénti
  objektumtérkép-bejárás vagy rendezés.
- Eviction eltávolítja a célobjektumot, saját runtime mesh-eit, tartalékát
  és diagnosztikai hivatkozását. Külön tulajdonosi mesh-nyilvántartás védi
  a megosztott asseteket, és Build/OnDestroy felszabadítja a saját mesh-eket
  akkor is, ha egy célobjektum korábban már eltűnt.
- Darabszám/eviction-idő naplózott; nem állítunk ezekből pontos GPU-byte-
  költséget. Az aktív fedés, CPU-mintacache és aux-mesh memóriája külön marad.

Várható hatás: hosszú területváltások után kevesebb megőrzött inaktív
geometria. Kiürített területre visszatérés új mesh-t/objektumot készíthet,
ami többletköltség; vizuális és memóriahatás élő próbával igazolandó.

### ND-94 — Külön ütemezett terepmesh és rendercél-előkészítés

**2026-09-12, implementáció előtt.** A csomag második feladata az ND-87
monolitikus terep-job két részre bontása. Az ismert 7,10 ms-os staging-
csúcs és a korábbi célkészítési tüskék nem tekinthetők megoldottnak.

- Chunkenként mesh-feltöltés/anyag-előkészítés, majd külön rendercél/
  diagnosztikai burkoló készül. A két lépés között is érvényesül a meglévő
  2 ms puha időkeret; a normál mesh API-hívás továbbra sem megszakítható.
- A legfeljebb 64 chunk/frame elméleti átvitelt 128 részfeladat/frame
  tartja meg. Aux/maszk a sor végén marad, közös commit külön Update-ban.
  Félkész mesh vagy cél nem kerül a rendererbe.
- Megszakítás a fél pár után is biztonságos. Részfeladattípus és tényleges
  legdrágább job-idő kerül a slice-logba, nem feltételezett terhelés.

Ez finomabb főszálas ütemezés, nem csökkenti szükségszerűen az összmunkát,
és nem oldja meg a mozgókamerás selectiont vagy az élességi küszöböt.
Core/relief/scene és a kiválasztott tile-fedés mindkét tételben változatlan.

### ND-95 — Három következő mérési/kamera/lépték korrekció

**2026-09-12, implementáció előtt.** A felhasználó három következő feladatot
kér egy csomagban. A korai élesség/selection halasztása megmarad.

1. A kérésdiagnosztika `Stopwatch` monotón időbélyeget kap: kickoff,
   worker belépés/kész, főszálas átvétel, commit eleje/vége. A frame-hez
   kötött `Time.unscaledTime` nem mérhet tiszta kéréskort a Build közben.
   Sorban állás, worker falióra, kész eredmény várakozása, staging falióra
   és commit külön jelenik meg. A scheduling/cancellation küszöb nem változik.
2. A km-lépték hitelessége kamera/target transzformációhoz, vetülethez,
   viewporthoz és világ-revízióhoz kötött. A nyolc hibányi türelmi idő
   csak ugyanazon mérési helyzetben őrizhet értéket; változáskor azonnal
   érvénytelen. A közös felszínlekérdezés visszautasítja a függő világváltást,
   nem keverhet régi snapshotot új relief/radius mezőkkel. Másik targethez
   nem használhatjuk az előző vagy egy tetszőleges bolygó modelljét.
3. A FlyTo interpoláció a helyi felszín feletti magasságot viszi át,
   az út közben is az aktuális irány sugarával. Nem egyszer előre mért
   cél-sugárból interpolált abszolút középponttávolságot. A min/near/max
   korlát, kézi megszakítás és a kapcsolható alapgömb mód megmarad.

Ez nem új Core/numerikus világmodell, nem teljes mesh-kamera ütközésvédelem,
nem relief- vagy tile-küszöbhangolás. Tesztelés: mesterséges időbélyeges
fázisösszeg, valódi viewer metódusok Editor-fixture-rel, meglévő regressziók;
offline fordítás nem helyettesít natív/vizuális ellenőrzést.

### ND-96 — Teljes tile/zoom zárócsomag, köztes kézi kapuk nélkül

**2026-09-12, implementáció előtt.** A felhasználó a checkpoint commit
után a fennmaradó teljes tile/zoom munkát kéri, egyetlen végső kézi próbával.
Ez feloldja a selection és korai élesség korábbi halasztását, nem bővíti
a feladatot új Core-mikrodomborzattal vagy a teljes M13-mal.

- Kamerafüggetlen proxy-geometria cache megőrzése nézetváltáskor; a
  vetített metrika továbbra is az aktuális kamerával számolandó. Korlátos,
  egy worker tulajdonú cache, világváltáskor eldobva.
- A kész CPU-terepquadok visszacsatolása a következő kiválasztáshoz,
  új Core-minták nélkül. Az ismert terep kiterjedése az ősi cull-boundsot
  is bővíti; a kész quad vetülete nem maradhat kisebb proxy mögé rejtve.
  Új geometriai információ revízióváltással érvényteleníti a metrika-cache-t.
  A korrekció progresszív és korlátos, nem folytonos terepmaximum-bizonyíték.
- Balance: a kettős szomszédbejárás helyett egyszeri, determinisztikus
  bejárás, változatlan rendezett split-sorrend és budget. Páros regresszió.
- A korai minőség és a kész geometria ellenőrzése, kamera/víz/lépték/
  upload életciklus regresszió és egységes végső próbalista egy csomag.
  A natív/élő bizonyítékot külön kell kezelni a CLI-fordítástól.

**Végrehajtás és visszamérés:** [ND-96 csomag és teljes próbalista](reviews/lod-final-batch-nd96-2026-09-12.md).
A geometria újrafelhasználása a víz külön workerében is működik. Az emit
megkapja a már feloldott saroklistát. Az Inspector 1-es/nem véges merge-
faktora érvényes, pozitív hiszterézist ad. A végső pixelcél 8/7: a 8/6
drágább korai hullámai miatt a base-célt 7-re módosítottuk. A mindenhol
nyers mélységű proxy kísérletét 200 ezres túlosztás miatt visszavontuk.
Az új cache gyorsulása nem egyenlő a sűrűbb teljes kép gyorsabb elérésével;
az offline költségtáblázat és a végső élő kapu ezt külön kezeli.

### ND-97 — Metrikatároló újrahasználata és helyi feedback-érvénytelenítés

2026-09-12, implementáció előtt. A felhasználó kézi próba nélkül kér
folytatást. Az ND-96 után is minden kameraváltás új metrika-szótárat épít,
és minden egyes feedback-rekord az egész szótárat törli. A következő két
költségcsökkentés változatlan 8/7 pixelcéllal és tile-/split-keretekkel:

- Explicit, egy worker által használt nézet-reset megtartja a szótár
  tárolóját, de minden régi vetített értéket töröl. A meglévő, új cache-t
  készítő `Reproject` megmarad referenciának; a runtime az új resetet használja.
  Régi request nem használhat tovább egy következő nézetre resetelt cache-t.
- A geometria-revízió tile-onként, a visszacsatolt tile és ősei mentén
  érvénytelenít. Független ág metrikája érvényben marad. Egy bejegyzés
  vetületváltás után továbbra sem használható, geometriafrissítés után
  pedig csak azonos helyi revíziónál cache-találat.
- Pontos metrika-/cut-/split-egyezés, korlát- és nullallokációs próba,
  valamint páros offline idő/allokációmérés szükséges. Nem ígérünk ebből
  teljes FPS- vagy vizuális elfogadást; nincs Core-/relief-változás.

Harmadik, méréssel azonosított részfeladat: a `SurfaceQuad` örökölt
értéktípus-egyenlősége 1000 azonos feedbacknél 1368000 byte-ot allokált a
Release reprodukcióban. A cache helyi, komponensenkénti, tolerancia nélküli
összehasonlítást kap; a Core és az általános quad-típus nem változik.

Átadás: [ND-97 mérések és tesztbizonyíték](reviews/lod-cache-allocation-nd97-2026-09-12.md).
A közös későbbi kézi próbalista megmarad; nincs új minőségi cél vagy
elfogadottnak jelentett FPS-eredmény.

### ND-98 — Igazoltan stabil vízkiválasztás újrahasználata

2026-09-12, implementáció előtt. A terep további finomítási körei jelenleg
változatlan kameránál a már stabil víz-cutot is újraépítik. Az immutábilis
vízforrás és az előző eredmény mellett explicit újrahasználatot vezetünk be:

- Csak azonos teljes vetület, maxLevel, split-/merge-küszöb, levélkeret és
  splitkvóta mellett; forrásváltás továbbra is új kiválasztást igényel.
- Nem elég a `DeferredSplits == 0`: egy további teljes kiválasztásnak azonos
  rendezett leveleket kell adnia. Ez igazolja a determinisztikus fixpontot,
  a hiszterézis és a kiegyensúlyozás utóhatásait is figyelembe véve.
- A megosztott fedés immutábilis; az új kérés statisztikái nullázottak,
  külön `selectionReusePolicy=ND98 reusedSelection=True` naplójelöléssel.
  Nincs régi munkaidő újramérése.
- Az argumentum-/cache-validálás és a megszakítás ellenőrzése megelőzi a
  gyors utat. Külső geometriafeedbacket tartalmazó cache-nél nincs gyors út.
- Kikapcsolható újrahasználattal páros, pontos eredmény-összehasonlítás és
  álló/módosuló nézetes regresszió igazolja a változatlan fedést. Ez csak a
  vízkiválasztás költségét csökkenti; nem teljes FPS- vagy vizuális igazolás.

### ND-99 — Fenntartva: a testvérág ND-62-je (tengeri jég) merge-kor ezt a számot kapja

**2026-09-13, azonosító-ütközés feloldása.** Az ND-62 azonosító két ágon
két különböző döntést jelöl:

| Ág | ND-62 tartalma | Utána következő fővonali döntések |
|---|---|---|
| `codex-handoff` (fővonal) | Kameramód-kapcsoló, „Tengelyforgás megfigyelése” | ND-63–98 erre épülve, commitolva |
| `experiment/full-temperature-model` (`e93b909`, nincs merge-elve) | Tengeri jég: mélységmodulált fagyási küszöb, folytonos sarok-blend | nincs |

**Döntés:** a fővonal megtartja a kameramódos ND-62-t, mert arra 36 további,
már commitolt döntés és számos dokumentum hivatkozik. A testvérág tengeri-jég
döntése merge vagy cherry-pick esetén **ND-99** számot kap; a merge-commitban
az ottani fejléc, a backlog-sor és a kódkommentek `ND-62` hivatkozásait
ND-99-re kell írni. A testvérág ND-61-e a fővonalon nem létezik, ezért
változatlanul megtarthatja a számát.

A pillanatnyi hőmodell terve (backlog 24. döntés) a testvérág releváns
Full-klímabázis-munkájának átvizsgálását kérte. A közös ős (`4b3ac02`) óta az
ág `src/`, `tools/reference/` és `tests/` alatt egyetlen Core-változást hoz:
`Temperature.TemperatureKelvinFullPrecomputed`. Ez a pozíciófüggetlen
`tGreenhouse` és `tCycle` tagot kívülről fogadja, az eredeti
`TemperatureKelvinFull` bitazonosan erre delegál. Nem új fizika, csak
hívásonkénti ismételt számítás megszüntetése. Az ND-100 lassú klímabázisa
ugyanezt a mintát követi (snapshotonként egyszer számolt pozíciófüggetlen
tagok); ha a C#-ban szükséges, a függvény átvehető, de a merge nem feltétele
az ND-100–104 munkának.

Nem seed-törő, nincs kódváltozás. A testvérág vizuális tesztje a felhasználó
2026-09-12-i döntése szerint jelenleg nincs napirenden.

### ND-100 — Pillanatnyi kétállapotú hőmodell: lassú klímabázis + felszín- és levegőanomália

**2026-09-13, implementáció előtt. Az irány a felhasználó által 2026-09-11-én
jóváhagyva** ([backlog döntésjegyzék](backlog.md) 1, 9, 13, 14, 16, 17, 18,
26, 27, 28. pont). Ez a bejegyzés rögzíti a modellt; a numerikus paraméterek
nyitottak, csak forrás + Python-referencia + mérés után kerülhetnek be.

**Kérdés.** A `Temperature.TemperatureKelvin` és `TemperatureKelvinFull` a
radiatív tagot egy teljes forgás 24 mintájának átlagából számolja, ezért a
nappali és éjszakai oldal ugyanazon a napon azonos értéket kap. A felhasználói
cél pillanatnyi, hőtehetetlenséggel késleltetett nappal/éjszaka-hőmérséklet,
amelyet a szél is szállít. Hogyan épüljön ez a meglévő klímára anélkül, hogy a
napi átlagos besugárzást kétszer számolnánk?

**Opciók:**

| Opció | Előny | Hátrány |
|---|---|---|
| A: Teljes hőmérséklet-állapot saját energiamérleggel, a bázis nélkül | Egyetlen konzisztens egyenlet | Újrakalibrálandó a meglévő §28.1 egyensúly, üvegház, lapse rate és ciklus; a biome-/jég-kalibráció elszakad |
| **B: Lassú bázis (`Bs`, `Ba`) + gyors anomália (`θs`, `θa`)** | A meglévő kalibrált klíma marad az egyensúly; a solver csak a napi eltérést integrálja | A két réteg határát pontosan kell definiálni (mi van a bázisban, mi az anomáliában) |
| C: A meglévő napi átlagot pillanatnyi `max(0, n·s)`-re cserélni | Kevés kód | Nincs hőtehetetlenség, éles terminátor; ellentmond a felhasználói célnak |

**Javaslat / jóváhagyott irány: B.**

- `Ts = Bs + θs` (felszíni skin-hőmérséklet), `Ta = Ba + θa`
  (felszínközeli levegő). Mindkettő Kelvin, `double`.
- Referenciaegyenletek (egységek: `J m⁻² K⁻¹`, `W m⁻²`, `K`, `s`):

  ```text
  Cs(kind) · dθs/dt = ΔQsolar − λs·θs − ksa·(θs − θa)
  Ca       · dθa/dt = ksa·(θs − θa) − λa·θa + advekció (ND-102) + keveredés
  ΔQsolar  = F·(1 − albedo(kind))·( max(0, n·s(t)) − dailyAverageFactor )
  λs       = 4·ε·σ·Bs³      (csak szorzás; a bázis körüli linearizálás)
  ```

- A `dailyAverageFactor` ugyanaz a 24 mintás függvény, amelyből a bázis
  radiatív tagja készül, így a napi átlagos elnyelt sugárzás csak a bázisban
  szerepel. Az `n` az ND-02 szerinti level-6 cella gömbi normálja (27. pont);
  lejtő és hegyárnyék későbbi mikroklíma-réteg.
- `s(t)` kizárólag `OrbitalMechanics.SunDirectionBodyFrame`-ből jön, amely
  `DeterministicMath.SinCos`-t használ. A solver kritikus útján nincs nyers
  `Math.Sin/Cos/Exp/Pow`; a `Bs³` és minden együttható szorzás/osztás.
- A procedurális `T_weather` kimarad a bázisból (14. pont), a felhőproxy nem
  árnyékol (17. pont), a jég albedója/hőtehetetlensége olvasható, de a jég nem
  olvad és nem nő visszacsatoltan (16. pont).
- Magas render-LOD-on egyszeri, determinisztikus magasságkorrekció:
  `T(vertex) = T(cella) − Γ·(h(vertex) − h(cella))`. A cella saját magassága
  csak a bázisban szerepel lapse rate-tel, így nincs kettős alkalmazás (18. pont).
- A klímabázis pozíciófüggetlen tagjait (`tGreenhouse`, `tCycle`)
  snapshotonként egyszer kell számolni (ND-99 átvizsgálás).

**Nyitott kérdések — a Python-referencia és forrásolt értékek döntik el:**

| # | Kérdés | Javaslat, amit mérni kell |
|---:|---|---|
| 1 | `Bs` és `Ba` viszonya | Első változatban `Bs = Ba = TemperatureKelvinFull` a weather tag nélkül; külön skin–levegő offset csak forrásolt értékkel |
| 2 | `Cs` felszíntípusonként | Óceán: kevert réteg `ρ·cp·h`; szárazföld: napi hőbehatolási mélység; édesvíz és jég: forrásolt mélység/anyagérték. Mindegyik tartománnyal és hivatkozással |
| 3 | `Ca` (felszínközeli levegőoszlop) | `ρair·cp·H`, a határréteg-vastagság forrásból |
| 4 | `ksa`, `λa`, `ε` | Forrásolt nagyságrend; `λa` newtoni relaxációs időállandóként |
| 5 | Jég és édesvíz albedója | A meglévő `AlbedoOcean`/`AlbedoLand` mellett forrásolt `Ice`, `Freshwater` érték |
| 6 | A tickben mintázott `max(0, n·s)` napi átlaga eltér-e mérhetően a 24 mintás `dailyAverageFactor`-tól | **Mérve (2026-09-13, `tools/reference/thermal_anomaly_column_ref.py`):** 3600 s-os ticknél a mintaátlag a 24 mintás faktorral pontosan egyezik; 1800/900 s-nál 0,8·10⁻³ … 1,5·10⁻³ az eltérés (a 24 mintás átlag kvadratúrahibája), ami ~1–2 W m⁻² és a kiépült napi átlagos `θs`-ben ≤ 0,14 K. Elfogadható, ha a bázis és az anomália ugyanazt a 24 mintás faktort használja; nem nő időben |
| 7 | Évszakos bázisváltozás kezelése a solver alatt | **Mérve:** egyszer, a kezdőnapra rögzített faktorral 30 nap alatt a napi átlagos `θs` 45°-on +2,3 K (óceán) / +8,3 K (szárazföld) — évszakos drift. Naponta, a nap elején újraszámolt faktorral ≤ 0,14 K, óránként, középre igazított 24 mintás ablakkal és lineáris interpolációval ≤ 0,09 K. **Javaslat: az óránként középre igazított faktor**; a bázis (`Bs`, `Ba`) frissítése ugyanehhez az órás rácshoz igazodjon |

**Elfogadási feltétel (Python, még C# előtt):** kontrollált, szél nélküli
esetben a napsütötte oldal melegebb, a maximum a helyi dél után jelentkezik,
az óceán napi amplitúdója kisebb a szárazföldénél; nulla besugárzásnál
fizikailag ésszerű tartomány felé hűl.

**Verziózás:** additív, diagnosztikai mező (ND-103). Amíg más fogyasztó nem
olvassa, nem seed-törő; a hőmodellnek saját modellverziója van.

**Érintett fájlok:** `tools/reference/` (új hőanomália-orákulum),
`src/WorldGen.Core/Climate/Temperature.cs` (bázis elérése), új
`src/WorldGen.Core/Climate/SurfaceTemperatureField.cs`.

**Módosítás mérés alapján (2026-09-13, implementáció közben).** A level-6
referencia első futása (`tools/reference/thermal_field_ref.py`) két hibát
mutatott a fenti bázis-definícióval:

1. A napi faktoros `TemperatureKelvinFull`-szerkezet sarki éjszakán a radiatív
   tag nullára esése miatt ~29 K-es bázist adott — sérti a kész-definíciót.
2. A szél termikus tagja a sarki éjszaka határán a `T^(1/4)` végtelen
   deriváltja miatt ~5700 m/s-os sebességet adott (a meglévő `WindVector`
   ugyanezt a hibát hordozza).

Két javítási irány egycellás mérése (`tools/reference/thermal_seasonal_column_ref.py`):

| Változat | Eredmény | Döntés |
|---|---|---|
| A: éves átlagos bázis, az évszakot a solver integrálja | Az óceán ~40 napos memóriája miatt a kanonikus újraindítás hibája 60 nap spin-up után is 5,3 K (45°) / 11,9 K (80°); 80°-os óceán nyáron +22 °C | Elvetve |
| **S: évszakos bázis, simított radiatív faktor** `f_eff = (1 − β)·f_napi + β·f_éves` | β = 0,5: 80°-os szárazföld −58 … +21 °C, 45° −18 … +37 °C; 10 napos spin-up hiba ≤ 0,18 K. β = 0,3: sarki minimum −80 °C | **Elfogadva, β = 0,5 (M13, ideiglenes)** |

Érvényes definíció: `Bs = Ba = T_rad(f_eff) + T_greenhouse + T_ocean − T_alt + T_cycle`,
ahol `T_ocean = 0,3·(mean_j T_rad(f_eff_j) − T_rad(f_eff))` csak óceánon; a
forcing változatlanul `ΔQ = F·(1 − albedo)·(max(0, n·s) − f_napi)`. A szél
termikus tagja a `T_rad(f_eff) + 33 − T_alt` pont-hőmérséklet gradienséből
készül (level 6-on max. 61 m/s). A simítás a meridionális hőszállítás és a
hőtehetetlenség proxyja, nem forrásolt mérés — megerősítendő.

Ideiglenes, még megerősítendő további választások: **M11** — hőcserében
`U_eff = max(U, 1 m/s)` (COARE gustiness-érv, Fairall et al. 2003); **M12** —
édesvíz hőkapacitása az óceáni képlet, jégé a szárazföldi.

Ismert, a meglévő §28-kalibrációból öröklött korlát: a 0,06-os óceáni albedó
és a +33 K additív üvegház miatt az egyenlítői óceán bázisa ~48 °C. Ez nem a
hőmodell hibája; a klímamodell kalibrációja külön feladat.

Bizonyíték: Python-referencia kétszeri futása bájtra azonos
(`thermal_field_vectors.json`); C# `SurfaceTemperatureFieldTests` 22/22
(rács, bázis, szél, rövid és kanonikus futás a vektorokhoz mérve,
determinizmus, párhuzamos = szekvenciális, konstansmegőrzés, energia,
paraméterhatás, napi ciklus, sarki korlát, élesetek).

### ND-101 — Hőmodell rács, idő, tick, spin-up és checkpoint

**2026-09-13, implementáció előtt. Jóváhagyott irány** (backlog 3, 4, 5, 20,
21. pont). Az ND-02 (fix level-6 szimulációs bázis) és ND-03 (köztes idő csak
jelölt prezentációs interpoláció) alkalmazása a hőmodellre.

**Döntés:**

- Fix level-6 egészgömbös rács: `6 · 64 · 64 = 24 576` cella, sűrű `double[]`
  mezők, kanonikus index `face · 64² + u · 64 + v` (az ND-66 sűrű rácsának
  konvenciója). Átlagos cellaél ~168 km (mérve, ND-101 3. nyitott kérdés). A render-LOD nem módosítja az
  állapotot és a költséget.
- Egyetlen Core `SimulationTime`: egész tickszám (`long`), verziózott fix
  tickhosszal. A másodperc `tick · tickSeconds` (egész), a csillagászati nap
  `seconds / 86400.0`. Unity `deltaTime` csak az idősebesség-gyűjtőt táplálja,
  solver-lépés soha.
- Két puffer (előző → következő), tickenként csere. Minden cella csak az előző
  pufferből és rögzített sorrendű szomszédokból számol, ezért szekvenciális és
  párhuzamos futás bitazonos.
- Fizikai tick nem hagyható ki. Lemaradáskor a legutóbbi kész snapshot látszik
  időbélyeggel, és az időgyorsítás lassul.
- Kezdőállapot: `θs = θa = 0` a cél-időbucket lassú bázisán, utána dokumentált,
  egész forgásszámú determinisztikus spin-up. Nagy deep-time ugrásnál nem fut
  milliónyi tick: az új bucket bázisából új, fix hosszú spin-up indul.
- A hőmező verziózott, eldobható cache/checkpoint (modellverzió + paraméter-
  hash + tick). Autoritatív world state/hash csak külön döntéssel (ND-103).

**Nyitott kérdések:**

| # | Kérdés | Opciók / javaslat |
|---:|---|---|
| 1 | Időintegrátor | A: teljesen explicit Euler — egyszerű, de a kis hőkapacitású szárazföldi cella stabilitása kis ticket kényszeríthet. B: IMEX, a cellánkénti lineáris tagok (`λs`, `λa`, `ksa`) implicit 2×2 megoldása csak `+ − × /` műveletekkel, az advekció explicit upwind. **Mérve (2026-09-13, egycellás, RK4 30 s referencia):** visszafelé Euler-IMEX (forcing a lépés végén) elsőrendű — szárazföldön (0,12 m) 2,0 K / 1,0 K max. hiba 1800 / 900 s-nál; **Crank–Nicolson-IMEX (forcing a lépés közepén) másodrendű — 0,16 K / 0,025 K**, 0,5 m-es szárazföldön 0,052 / 0,0072 K, óceánon ≤ 0,001 K. Mindkettő minden vizsgált ticknél stabil. **Javaslat: Crank–Nicolson-IMEX** |
| 2 | Tickhossz | A lokális tagok a CN-IMEX-szel nem korlátozzák a ticket; 900 s mellett a szárazföldi hiba ≤ 0,025 K, 1800 s mellett ≤ 0,16 K (mérve). A végleges tick az advekció CFL-korlátjától függ (legkisebb cellaél / max. szélsebesség, ND-102), és a forgási periódus egész osztója legyen. Jelölt: 900 s |
| 3 | Rácsmetrika (cellaterület, élhossz, élnormál) előállítása | **Mérve (2026-09-13, `tools/reference/thermal_grid_metrics_ref.py`):** level 6-on a húrsokszög-terület a pontos gömbi területtől −1,5·10⁻⁴ … −7,1·10⁻⁵ relatív eltérésű, a teljes összegre normalizálva −2,3·10⁻⁵ … +5,6·10⁻⁵; a húr és a gömbi ív élhossza 1,3·10⁻⁵ … 2,5·10⁻⁵ relatív eltérésű; a cellaterület max/min aránya 1,3969 (az ND-24 mért értékével egyező); átlagos cellaél ~168 km. A cellasarkok az ND-24 szerint konstrukciós (baked) `tan`-warp pozíciók, tehát a metrika is konstrukciós adat: egyszer épül, a szimuláció csak olvassa, a táblahash-t a CI platformmátrixa ellenőrzi. **Javaslat: B** — húrsokszög-terület és húr-élhossz csak `+ − × / sqrt` műveletekkel, a teljes területre normalizálva; nem vezet be új transzcendens függvényt az ND-24 `tan`-ján túl. A konzervativitáshoz antiszimmetrikusan használt, pozitív súly elegendő, a ~10⁻⁵ nagyságrendű eltérés a paraméterek bizonytalanságánál jóval kisebb |
| 4 | Spin-up hossza | A leglassabb (óceáni) relaxációs időállandóból és a mért napi periodikus konvergenciából |
| 5 | Időbucket és checkpoint-gyakoriság | Mérés után; a spin-up költsége határozza meg |

**Verziózás:** a tickhossz, a spin-up és a rácsmetrika a hőmodell verziójának
része; változásuk a hőmező-cache érvénytelenítését jelenti, a világ seedjét nem.

### ND-102 — Egyirányú széladvekció a levegőanomáliára, külön `WindTick`

**2026-09-13, implementáció előtt. Jóváhagyott irány** (backlog 2, 15, 19,
26, 28. pont). A kétirányú hő→szél csatolás későbbi, külön ND.

**Kérdés.** Hogyan szállítsa a szél a felszínközeli levegő hőjét determinisztikusan,
a jelenlegi szélmodell megváltoztatása nélkül?

**Döntés:**

- A szél a levegőanomália energiatartalmát (`Ea = Ca · θa`) szállítja, nem a
  talajt és nem a teljes kalibrált bázist (26. pont).
- Véges térfogatú, upwind fluxus: élenként egyszer számolt normálsebesség
  `u_e` és élhossz `L_e`; a fluxus a szél felőli cella értékét viszi. Rögzített
  élsorrend (right, left, up, down), két puffer. Előbb a séma saját numerikus
  diffúzióját mérjük; explicit fizikai keveredés csak forrásolt célértékhez
  kerül be (28. pont).
- Külön, ritkább `WindTick`; két kész wind snapshot között determinisztikus
  lineáris interpoláció ugyanazon Core-időre (19. pont).
- A jelenlegi `WindPrecipitation.WindVector` nyers `Math.Asin/Atan2/Sin/Cos/Tanh`
  hívásokat használ, ezért a hőmodell kritikus bemeneteként változatlanul nem
  vehető át. A hőmodellhez **új, determinisztikus szélsnapshot-út** készül.
  A mostani csapadék- és biome-fogyasztók a régi úton maradnak; azok átállítása
  seed-törő és külön döntés.

**Nyitott kérdések:**

| # | Kérdés | Opciók / javaslat |
|---:|---|---|
| 1 | Konzervativitás és konstans mező megőrzése divergens szélnél | A felszínközeli szél nem divergenciamentes, ezért a tiszta fluxusforma konvergenciazónában felhalmozza az energiát, a tiszta advektív forma pedig nem konzervatív. **A:** fluxusforma + `θa·div(u)` kompenzáció. **B:** a szél diszkrét divergenciamentes vetítése (iteratív Poisson, drága). **C:** tiszta fluxusforma. **Mérve (2026-09-13, `tools/reference/thermal_advection_ref.py`):** pólus felé összetartó széllel, level 4-en 200 lépés után a C-ben a konstans mező max. eltérése 316 (a pólusnál felhalmozódik), az A-ban pontosan 0. Merevtest-forgással, élközépponti sebességgel mindkét forma energiaváltozása ≤ 1,4·10⁻¹⁶ egy teljes körülfordulás alatt. **Javaslat: A** |
| 1b | Numerikus diffúzió (28. döntés) | **Mérve:** elsőrendű upwind, merevtest-forgás 20 m s⁻¹-mal, egy teljes körülfordulás (~27 nap): a folt csúcsa level 4-en 0,151×, level 5-ön 0,266× (kockasarkokon áthaladó tengellyel level 4-en 0,082×); monoton, negatív érték nincs; tömegközéppont-hiba 22–76 km. A séma saját diffúziója nagy, ezért explicit keveredési tag nem kerül be. Ha az élő ellenőrzés túl elkenődött hőanomáliát mutat, egy másodrendű, limiteres séma külön döntés |
| 2 | A determinisztikus szél képlete | A meglévő zonális sávok és Coriolis-proxy átírása `z = sin(lat)` alapú vagy `DeterministicMath` + új determinisztikus inverz függvényekkel; Python-KAT a régi úttal való eltérés mérésével |
| 3 | `WindTick` hossza | A szélforrás (napi átlagos hőgradiens) változási sebességéből; kezdeti jelölt a forgási periódus egész osztója. **Költség mérve (2026-09-13):** a jelenlegi `WindVector` a teljes level-6 rácson 156 ms egy szálon (6,3 µs/cella), tehát egy snapshot a termikus ticknél nagyságrendekkel drágább; az új determinisztikus szélútnak ennél ne legyen lassabb |
| 4 | Élnormál-sebesség | **Mérve (2026-09-13):** level 6-on merevtest-forgásnál (z és kockasarkokon áthaladó tengely) az élközépponti analitikus kiértékelés diszkrét divergenciája max. 6,9·10⁻¹⁵ (U·√A-hoz mérve), a két cellaközép átlaga 3,5·10⁻³ … 5,9·10⁻³ hamis divergenciát ad, és a kompenzált formában egy körülfordulás alatt 2,8·10⁻⁴ … 9,1·10⁻⁴ energiát veszít; tömegközéppont-hibája is nagyobb (39–133 km vs. 22–76 km). A két oldalról számolt élhossz max. 7,5·10⁻¹⁶ relatív eltérésű, ezért a fluxust élenként egyszer kell számolni. **Javaslat: élközépponti kiértékelés.** A legkisebb level-6 cellaél 128,8 km |

**Elfogadási feltétel:** szél nélkül egy lokalizált `θa` anomália nem mozdul;
egyenletes széllel a szélirányba mozdul és a felszínre hőcserével hat; konstans
mező a kockalap-éleken átlépve sem változik; divergenciamentes széllel az
energia a dokumentált numerikus tolerancián belül megmarad.

### ND-103 — Termikus felszíntípus és a diagnosztikai/autoritatív határ

**2026-09-13, implementáció előtt. Jóváhagyott irány** (backlog 6, 7, 16, 21. pont).

**Döntés:**

- Új `SurfaceThermalKind`: `Land`, `Ocean`, `Freshwater`, `Ice`, saját
  albedóval és hőkapacitással (ND-100 nyitott 2. és 5. kérdés). A jelenlegi
  `isOceanic` boolean nem elég.
- Forrás: a meglévő óceánmaszk, tóazonosítás és jégbesorolás. A hőmodell a
  jeget csak olvassa (16. pont).
- A hőmező **párhuzamos diagnosztikai modell**: nem írja át a biome-ot, jeget,
  csapadékot, hidrológiát, panelmetrikákat vagy a `WorldStateHash`-t. Más
  fogyasztó csak külön validációs és modellverziós kapun állhat át rá.

**Nyitott kérdések:**

| # | Kérdés | Opciók / javaslat |
|---:|---|---|
| 1 | Level-6 cellatípus a finomabb maszkokból | **A:** többségi típus — egyszerű, de a part lépcsős. **B:** területarányos keverés (albedó és hőkapacitás súlyozott átlaga). Javaslat: B, ha a Python a part menti napi amplitúdót ésszerűbbnek méri |
| 2 | A jég forrása deep-time és évszak mellett | A meglévő jégbesorolás időpillanata a hőmodell bázisidejével legyen azonos; nem a render-LOD-ból |

**Verziózás:** amíg diagnosztikai, a world hash változatlan. Az autoritatív
átállás modellverzió-, betöltési hiba-, checkpoint- és hash-frissítést igényel.

### ND-104 — Hőoverlay adatút, UI és teljesítménycélok a Viewerben

**2026-09-13, implementáció előtt. Jóváhagyott irány** (backlog 8, 9, 10, 11,
12, 20, 22, 23. pont). A Core-fázisok (ND-100–103) után következik; élő Unity-
ellenőrzést igényel.

**Döntés:**

- A Core számolja a teljes level-6 `double` mezőt. A GPU csak interpolál és
  palettáz; HLSL-ben nincs második sugárzás-, szél- vagy hőképlet.
- Adatút: hat szelet, face-enként 64×64, CPU-n explicit skálával fixpontosra
  kvantált scalar textúra, egycellás, szomszédmezőből töltött gutterrel; a
  terep- és vízvertexek face-UV-t kapnak. A prezentációs textúra byte-
  reprodukálható.
- A solver háttérszálon, kettős snapshot-pufferrel halad az overlay állapotától
  függetlenül; a főszál csak kész, időbélyegzett snapshotot cserél.
- A `SunController`, a szél, a solver és az overlay ugyanazt a Core
  `SimulationTime`-ot olvassa; a `currentTimeDays` és a `climateDayT` nem
  maradhat két független óra.
- Egyetlen, kölcsönösen kizáró `SurfaceOverlayMode` (`None`,
  `SurfaceTemperature`, `AirTemperature`, `WindSpeed`, `Precipitation`) a
  mostani booleanok helyett, tesztelt migrációval. Overlay-váltás nem indít
  `Build()`-et, hidrológiát vagy mesh-geometriát (az ND-50 invalidáció
  általánosítása).
- Világításfüggetlen alapszín, opcionális külön hillshade; fix abszolút °C
  skála külön 0 °C jelöléssel, automatikus min/max nélkül.
- A közös futásidejű panel kap összecsukható blokkot a kurzor alatti
  komponensbontással (`Ts`, `Ta`, bázis, besugárzás, napszög, hőcsere,
  advekció, keveredés, felszíntípus, magassági korrekció).
- Teljesítménycél élő gépen mérve: kész snapshotnál egy képkockás váltás,
  16,7 ms alatti főszálú csere/feltöltés, normál időhaladásnál legalább 5 Hz.

**Nyitott kérdések:**

| # | Kérdés | Javaslat |
|---:|---|---|
| 1 | Kvantálási skála és bitmélység | 16 bit, a fizikailag lehetséges tartományra és a panelen szükséges felbontásra méretezve; a Python P1/P99 burkoló alapján |
| 2 | Paletta alapvégpontjai | Több seed, évszak, nappal/éjszaka és spin-up utáni P1/P99 alapján kalibrálva |
| 3 | Snapshot-átvételi gyakoriság gyorsított időnél | Mérés az 5 Hz-es cél és a háttérszál terhelése alapján |

**Implementáció (2026-09-13), élő Unity-ellenőrzés előtt.** Kvantálás:
`q = floor((K − 150) / 0,01 + 0,5)` 16 biten (150,00 … 805,35 K, 0,01 K
lépés). Atlasz: 396 × 66 R16 texel, lapanként 64×64 cella + egycellás, a
topológiai szomszéddal töltött gutter; a gutter-sarok a legközelebbi belső
cella. Paletta: fix alapvégpontok −60 °C / +50 °C (Inspectorban állítható),
kék → cián → 0 °C világos semleges → sárga → piros, 0 °C-os kontúrvonal. A
snapshot-feltöltés alapból legfeljebb 5 Hz, a solver háttérmunkája legfeljebb
96 tick/munka, a lokális lépés párhuzamos (bitazonos a szekvenciálissal).
Az idő forrása a `SunController.CurrentTimeDays` (egész tickre lefelé
kerekítve); a `climateDayT` Build-kori biome-paraméter marad. Eltérések és
nyitott pontok: `docs/01-architecture.md` §11.6. A nyitott kérdések 1–3.
pontja (bitmélység és alapvégpontok véglegesítése, gyorsított idő) az élő
PerfLog (`[ND-104 thermal]` sor) és vizuális próba után zárható.

### ND-105 — Az alkalmazásréteg (App Shell) helye és függetlensége

**2026-09-13. Megvalósítva (Foundation-rész).** Részletek:
`docs/09-app-shell-architecture.md`. **Az ND-105–114 blokk az app-rétegé**
(2026-09-13-án ND-105–109-ről bővítve); a párhuzamos Core-munka ND-115-től folytassa.

**Döntés:** a motor- és Core-független alkalmazáslogika (állapotgép, session,
settings, mentési konténer, UI-modellek, hang-matek) a
`unity/WorldGenViewer/Assets/Scripts/App/Foundation/` alatt él,
`WorldGen.App.Foundation` asmdef-fel (`noEngineReferences: true`, **nincs
`WorldGen.Core` referencia**). Fordítási kapu: `tests/WorldGen.App.Foundation.Compile`
(netstandard2.1, C# 9, linkelt forrás), tesztek: `tests/WorldGen.App.Foundation.Tests`.

| Opció | Előny | Hátrány |
|---|---|---|
| **A: Unity Assets + noEngineReferences asmdef (választott)** | a viewer-LOD bevált mintája; Unity azonnal látja | a csproj a forrástól külön mappában |
| B: `src/WorldGen.App` package | szimmetrikus a Core-ral | a `src/` a determinisztikus mag helye (CLAUDE.md), manifest-módosítás |

**Verziózás:** nem seed-törő, szimulációt nem érint.

### ND-106 — Saját minimál JSON az alkalmazásrétegben

**2026-09-13. Megvalósítva.** A Unity alatt nincs `System.Text.Json`, a
`JsonUtility` motorfüggő és nem kezeli a szótárakat, verziómigrációt.
Döntés: saját, szigorú RFC 8259 parser/writer (`WorldGen.App.Serialization`),
mélységkorláttal, pozíciós hibával; a szám eredeti szövegként is megmarad
(64 bites seed pontosan). Alternatíva: `com.unity.nuget.newtonsoft-json`
— elvetve, mert a Foundation így külső csomag nélkül, dotnet alatt is
tesztelhető.

### ND-107 — Mentési konténer, szöveges seed-leképezés

**2026-09-13. Megvalósítva (Foundation), a szekciók tartalma Core-függő.**

- Konténer: `WGSV` magic, u16 konténerverzió, UTF-8 JSON fejléc CRC32-vel,
  szekciótábla (név, hossz, CRC32), nyers szekcióadatok. Little-endian.
  A betöltési lista csak a fejlécet olvassa. Alternatíva: ZIP — elvetve
  (lassabb fejléc-only listázás, nagyobb felület).
- Atomi írás: ideiglenes fájl → flush lemezre → replace, előző változat `.bak`.
- A szöveges seed (nem szám) → **FNV-1a 64** az UTF-8 bájtokon, normalizálás
  nélkül. Ez stabil szerződés (megosztott szöveges seedek); módosítása
  verzióemelés. A decimális és a `0x` hex seed változatlanul az `ulong` érték.

### ND-108 — Generátorverzió a mentés kompatibilitásához (NYITOTT)

**2026-09-13, nyitott, a Save Core-kötését blokkolja.**

A mentés fejlécébe `worldGeneratorVersion` kerül. A Core-ban ma nincs ilyen
azonosító (a `VERSION` fájl emberi checkpoint; a spec
generator/simulation/schema verziói nem implementáltak).

| Opció | Leírás |
|---|---|
| **A (javaslat)** | Core-konstans `WorldGeneratorVersion` (egész vagy SemVer), minden seed-törő ND-nél emelve |
| B | A spec hármasa (generator / simulation / schema) külön mezőként |
| C | `WorldStateHash` egy kanonikus seedre, mint ujjlenyomat (automatikus, de drága és nem mond migrálhatóságot) |

Amíg nincs döntés: generátor-eltérés → `ConfigurationOnly` (a seed és a
paraméterek újra felhasználhatók, az állapot nem töltődik).

### ND-109 — Nem determinisztikus API-k az alkalmazásrétegben

**2026-09-13. Megvalósítva.** A CLAUDE.md lebegőpontos és véletlen-tiltásai a
**szimulációs kritikus útra** vonatkoznak. Az app-réteg használhat
`DateTime`-ot (injektált `IClock`), `Guid`-ot (ideiglenes fájlnév),
`Math.Log10`-et (hangerő-dB) és kriptográfiai entrópiát (új seed sorsolása).
Határ: a szimulációba csak explicit, tárolt érték lép be (seed, paraméterek,
szimulációs idő). A véletlen preset is konkrét értékeket sorsol, amik a
kérésbe és a mentésbe kerülnek.

### ND-110 — A menürendszer UI-technológiája: UI Toolkit

**2026-09-13 nyitva, 2026-09-20 ELDÖNTVE (felhasználói döntés): UI Toolkit,
az IMGUI megtartásával a fejlesztői overlayekhez.**

**Hatókör.** A döntés CSAK a nézet-réteget érinti. A Foundation-modellek
(`MenuModel`, `SettingsScreenModel`, `SaveSlotRows`, `Dialogs`, `ToastQueue`)
szándékosan UI-technológia-függetlenek — a `SettingsScreenModel` doksija ezt
ki is mondja —, ezért a 433 Foundation-teszt egyikét sem érinti, és a döntés
később mérsékelt költséggel visszavonható.

**Mi kapja az UI Toolkitet:** a kiadható héj — Main Menu, Settings,
Load/Save, Pause, dialógusok, toastok.

**Mi MARAD IMGUI-ban, szándékosan:** a fejlesztői overlayek — navigációs
menü, réteg-kapcsolók, deep-time panel, F3 diagnosztika. Ezek működnek, nem
részei a kiadható héjnak, és az átírásuk tiszta veszteség lenne. A
`WorldGenPanelUI` (Canvas+TMP, 175 sor) sorsa a Main Menu munkájakor dől el.

**A DÖNTŐ ÉRV — nem az esztétika, hanem a munkamegosztás.** A uGUI
prefab-alapú: minden képernyő kézi Unity-Editor szerkesztés, amit CSAK a
felhasználó tud elvégezni. A projekt története során ez többször beragadt
("új Unity-Editor lépést igényel" → sokáig nyitva marad). Az UI Toolkit
ezzel szemben UXML + USS SZÖVEGFÁJLOKON áll: ezeket az agent írja meg,
verziókezeljük és offline ellenőrizzük; a felhasználóra scene-enként egyetlen
`UIDocument` komponens beállítása marad.

**Mérési alap a döntéshez (2026-09-20):** IMGUI 41 hívási hely a
`PlanetGridMesh`-ben, Canvas+TMP 175 sor a `WorldGenPanelUI`-ban, UI Toolkit
használat: NULLA. Unity 6000.0.77f1.

**Vállalt hátrány:** új technológia a projektben, tehát az ELSŐ képernyő
lassabb lesz a többinél; a USS-stíluslap miatt a másodiktól gyorsul. Az UI
Toolkit world-space UI-ra gyengébb — a terv szerint ilyen nem kell; ha
mégis felmerülne (a bolygó felszínéhez kötött, lebegő panelek), az ÚJ
döntést igényel, nem ennek a kiterjesztését.

---

**Az eredeti mérlegelés (2026-09-13), megtartva:**

A viewer ma kétféle UI-t használ: IMGUI (`PlanetGridMesh.DrawNavigationPanel`)
és Canvas + TextMeshPro (`WorldGenPanelUI`).

| Opció | Előny | Hátrány |
|---|---|---|
| **A: UI Toolkit (javaslat)** | stíluslap (USS), felbontás- és UI-scale-kezelés beépítve, billentyű-/egérnavigáció, jól illeszkedik a modell/nézet szétválasztáshoz | új technológia a projektben; world-space UI-ra gyengébb (itt nem kell) |
| B: uGUI + TextMeshPro | a projektben már van; sok minta | prefab-alapú, a skálázás és a navigáció több kézi munka |
| C: IMGUI | a navigációs panel már ilyen | kiadható menürendszerhez nem ajánlott (stílus, akadálymentesség, teljesítmény) |

(Az eredeti jegyzet itt "döntés kell a felhasználótól"-lal zárult; a döntés
2026-09-20-án megszületett — ld. a szakasz elején.)

### ND-111 — Kiadási identitás és alkalmazásverzió (RÉSZBEN NYITOTT)

**2026-09-13.**

- **Megvalósítva:** az alkalmazásverzió és a kiadási identitás egyetlen fájlban
  él: `tools/release/release-identity.json` (termék- és cégnév, SemVer
  `0.1.0-alpha`). A repo-gyökér `VERSION` fájl emberi checkpoint marad
  (a saját leírása szerint nem kiadási verzió), ezért NEM ebből jön az app-verzió.
  A build-script az identitást csak a build idejére állítja be a
  PlayerSettings-ben, utána visszaállítja (a ProjectSettings nem lesz koszos),
  és `StreamingAssets/build-info.json`-t ír a futásidejű `BuildInfo`-hoz.
- **Nyitott, felhasználói döntés:** a végleges **terméknév** és **cégnév**. Ez nem
  kozmetika: a Unity `persistentDataPath` (`%USERPROFILE%\AppData\LocalLow\<Company>\<Product>`)
  ebből képződik, tehát az első kiadás után a módosítása a mentések és beállítások
  „eltűnését” okozza (migráció nélkül). Jelenleg `WorldGen` / `WorldGen`
  helyőrzővel. A mostani `ProjectSettings` értékei (`Unity Technologies` /
  `com.unity.template.hdrp-blank`) a sablonból maradtak.
- Ikon és splash screen: asset kell a felhasználótól.

### ND-112 — Windows-csomagolás: portable ZIP és Inno Setup

**2026-09-13. Megvalósítva (scriptek), a telepítő fordítása Inno Setup 6
telepítését igényli.**

- Portable: `tools/release/package-portable.ps1` →
  `WorldGen-<verzió>-win64.zip`, a Unity „DoNotShip” / „DontShip” mappái nélkül.
- Telepítő: `tools/release/WorldGen.iss` (Inno Setup 6). Az `AppId` GUID
  **rögzített, soha nem változhat** (különben a frissítés nem ismeri fel a
  meglévő telepítést). Alapértelmezett könyvtár `{autopf}\WorldGen`, Start
  menü, opcionális asztali ikon, indítás a telepítés végén.
- Uninstall: a felhasználói adat a `LocalLow` alatt van, a telepítési
  könyvtáron kívül, így alapból megmarad. Egy kérdés (alapértelmezett: **Nem**)
  felajánlja a mentések, beállítások, képernyőképek és naplók törlését (WF-INSTALL-002).
- Alternatíva: MSI (WiX). Elvetve az MVP-hez, mert az Inno egyszerűbb, és a roadmap is ezt javasolja.

### ND-118 — A determinisztikus matek kiterjesztése: Atan/Atan2/Asin/Acos/Tanh

**2026-09-20. Megvalósítva (a függvények); a modulok átállítása KÜLÖN lépés.**

**Előzmény.** Az ND-27 eldöntötte, hogy saját polinomiális implementációt
írunk a transzcendens függvényekre, és a `DeterministicMath` meg is épült:
`Sin`, `Cos`, `SinCos`, `Ln`, `Exp`, `Pow`. Nyolc Core-modul használja.

**A feltárt hiányosság (2026-09-20).** A klíma-lánc
(`WindPrecipitation`: `Asin`/`Atan2`/`Tanh` ×4), a folyó-nyomvonal
(`RiverPathTracing`: `Acos`) és a csillagászat (`OrbitalMechanics.SubsolarPoint`:
`Asin`/`Atan2`) máig NYERS `System.Math`-ot hív a kritikus úton — de NEM
feledékenységből: **ezeknek a függvényeknek egyszerűen nem volt
determinisztikus párjuk.** A `DeterministicMath` API-ja nem tartalmazta
őket, tehát nem is lehetett mire cserélni.

Ennek a következménye mérhető: **tizenkét KAT-teszt toleranciával mér**, nem
bitpontosan (`OrbitalMechanics`, `Temperature`, `WindPrecipitation`,
`MoisturePrecipitation`, `LakesIceErosion`, `DeepTimeErosionGlaciation`,
`PlateMotion`, `TileGeometry`, `SeaLevelCalibration`,
`SurfaceTemperatureField`, `RegolithModel`, `DeterministicMath` maga a
plauzibilitásra). Vagyis az I1 ("bitre ugyanaz a világ minden platformon")
ezekre a láncokra ma **nincs kikényszerítve**, csak ~1e-6-ig.

**Döntés.** Megírjuk az öt hiányzó függvényt, ugyanazzal a módszerrel, mint
az ND-27: csak garantáltan bitpontos műveletekből (`+ - * /`, `Math.Sqrt`),
Python-orákulum → KAT-vektorok → C#.

| Függvény | Módszer |
|---|---|
| `Atan` | reciprok-redukció (\|x\|>1), majd HÁROMSZOROS felezés `a/(1+sqrt(1+a²))`-vel \|t\|≤0,0985-ig, majd 10 tagú Taylor hátulról előre összegezve |
| `Atan2` | kvadráns-logika az `Atan`-ra |
| `Asin` | \|x\|≤0,5: `atan(x/sqrt(1-x²))`; \|x\|>0,5: félszög-azonosság, mert a naiv képlet a tartomány SZÉLÉN kioltana |
| `Acos` | `pi/2 - Asin(x)` |
| `Tanh` | `(1-e^{-2\|x\|})/(1+e^{-2\|x\|})` a determinisztikus `Exp`-ből, kis- és nagy-argumentumú átváltással |

**Mért pontosság** (20 000 minta/függvény, a valódi `math.*`-hoz mérve):
`atan` 3,3e-16 · `asin` 6,7e-16 · `acos` 8,9e-16 · `atan2` 4,4e-16 ·
`tanh` 8,0e-13 (relatív). A kitűzött cél 1e-9 volt. A tartomány szélén
(\|x\|→1) az `asin` hibája 2,2e-16 — ezért kell a félszög-ág.

**A C# BITRE egyezik a Pythonnal**, tolerancia nélküli `Assert.Equal`-lal,
2000 új vektoron (500 függvényenként). Ez azért lehetséges, mert mindkét
oldal UGYANAZT a műveleti sorrendet futtatja, csak IEEE-754 szerint
korrekt kerekítésű műveletekből.

**AMI EBBŐL NEM KÖVETKEZIK.** Ez a lépés **önmagában semmit nem változtat a
világon**: egyetlen meglévő modul sem állt át, a meglévő 1000
`sinCos`/`pow` vektor bitre változatlan (ellenőrizve). A modulok átállítása
**SEED-TÖRŐ**, mert minden ráépülő KAT-vektort újra kell generálni
(domborzat, lemezhatár, hidrológia, folyók, tavak, nedvesség, features,
vulkanizmus, state hash) — és az ND-52 tapasztalata szerint az **ordinális
kalibrációt is** (ld. ND-09 v2, 2026-09-19: az ND-52 észrevétlenül tette
elavulttá). Az átállás külön döntést és külön, összevont lépést igényel.

### ND-115 — Horizont-vágás a vetített terep-úton

**2026-09-19. Megvalósítva.** Részletek és mérések:
`history/2026-09-19-horizon-cull.md`.

**Probléma:** az `AdaptiveQuadTree.EvaluateNodeForPriority` korán visszatér,
ha vetített nézet ÉS terep-proxy is jelen van, ezért a lentebb álló,
kiterjedés-tudatos horizont-teszt ezen az ágon soha nem futott le. Márpedig
ez a produkciós ág. Következmény mérve: 3 egység magasságban, ahol a látható
sapka a gömb ~1,4%-a, a vágás 123 152 level-8 csomópontot járt be ~5 500
látható csempére; a 191 084 kiértékelésből 180 564 már a base-szint
eléréséig megtörtént.

**Döntés:** a proxy-ágon is fut horizont-vágás, `node.Level <
staticBaseLevel` hatókörrel. A feltétel SZÖGALAPÚ, nem érintősíkos: egy
`rP` sugarú pont akkor van az `R` sugarú takaró mögött `d` távolságból, ha
szöge > `acos(R/d) + acos(R/rP)`. Koszinuszban kifejtve csak szorzás és
`Math.Sqrt` kell, tehát a cut determinizmusa nem sérül (transzcendens
függvény nem bitpontos, ld. CLAUDE.md).

**Elvetett változat:** az érintősík-teszt (`C·X + |C|·r < R²`) átvétele a
nem-proxys ágból. Az CSAK a gömb felszínén lévő pontra helyes; emelt pontra
hamis, ezért 54 próbanézetből 6-ban megváltoztatta a cutot, kettőben
katasztrofálisan (5 636 levél → 0), a limbus menti látható csempéket kivágva.

**A garancia, amit vállalunk:** a cut NEM bitre azonos (48/54 nézet az, 6
eltér, mert telített budgetnél a best-first határa eltolódik). Helyette a
LÁTHATÓ FEDETTSÉG védett: 54 nézet × 21×11 képernyő-minta alapján a
finomított látható minták száma 6621/9561 a vágás előtt ÉS után is,
nézetenként pontosan egyezően, nulla romlott nézettel.

**Hatás:** metrika-kiértékelés −75,5%, a hideg selection 482-689 ms-ról
35-103 ms-ra, a meleg 157-183 ms-ról 7-22 ms-ra.

### ND-116 — A 2:1 kiegyensúlyozás korai kilépése blokkolt budgetnél

**2026-09-19. Megvalósítva.** Részletek és mérések:
`history/2026-09-19-balance-early-exit.md`.

**Probléma:** az `EnforceRestrictedBalance` fixpont-ciklusa minden körben
végigszkenneli a teljes cutot (|cut| × 4 szomszéd). Szoros budgetnél az őr
(`cut.Count + 3 > maxLeafCount`) minden felosztást blokkol, tehát a
szkennelésnek nincs kimenete, csak költsége: 20 000 levélnél 31 ms,
64 000-nél 78 ms. Ez a produkciós eset - a 2026-09-18-i naplóban 68
vágásból 44 volt telített.

**Döntés:** ugyanezt a feltételt belépéskor is ellenőrizzük. Ha igaz, örökre
igaz marad (a `SplitOnce` nettó +3, a `cut.Count` csak nő), tehát a korai
kilépés kimenete azonos. Igazolva 180 próbaeseten: nulla megváltozott cut,
a balance összideje 9068 ms → 1832 ms.

**NYITOTT marad:** ha a metrika a budget ALATT telítődik, van fejtér, és a
teljes újraszkennelés miatt 108 felosztás 332 ms-ba kerül (~3 ms/felosztás).
Munkalistás átírás megoldaná, DE a jelenlegi ciklus körönként gyűjt és
`TileId.Value` szerint rendezve oszt, a budget-őrök mid-iterációban is
blokkolhatnak - a kimenet tehát sorrend-függő. Az átírás előtt tisztázandó,
hogy a fixpont egyértelmű-e a korlátok nélkül, és hogyan viselkedik azokkal.
Ez egyben ok arra, hogy a `MaximumRenderBudget` NE emelkedjen: a balance
csak a "budget > telítődés" tartományban dolgozik, tehát nagyobb plafon
gyakrabban visz a drága esetbe.


### ND-117 — Talaj/regolit (`RegolithProfile`) MVP-hatókör és a maradék mezők halasztása

**2026-09-19.** A `docs/backlog.md` M8 sora és a
`src/WorldGen.Core/Features/OrdinalQuantization.cs` doksija régóta jelzi:
"Soil fertility TOVÁBBRA IS BLOKKOLT (nincs talaj-modul)". Részletes terv:
`docs/01-architecture.md` §13.

**Döntés — hatókör-szűkítés.** A spec §39 `RegolithProfile`-jának 10
mezőjéből (Depth, Porosity, WaterRetention, MineralDiversity,
PhosphorusAvailability, NitrogenAvailability, Iron, Sulfur, Salinity,
pHProxy) az MVP **csak hármat** (Depth, Porosity, WaterRetention) számol
— a `tools/reference/regolith_ref.py`-ban, C# nélkül (a C#-port külön
lépés). A döntő szűrő: a spec 6 forrása (alapkőzet, vulkanizmus, erózió,
üledék, víz, hőmérséklet) közül melyiknek van MÁR MOST valódi,
tile-onként VÁLTOZÓ Core-kimenete.

**Fedett forrás → felhasznált Core-kimenet:**
- erózió/üledék → `LakesIceErosion.ApplyStaticErosionPass`
  (`Erosion[]`/`DepositionGain[]`, M7, már KAT-vektoros);
- víz → a csapadék-mező (`moisture_transport_ref.compute_precipitation_field`
  / a C# oldalon `MoisturePrecipitation`, M5, már KAT-vektoros);
- hőmérséklet → `LakesIceErosion.AnnualTemperatureStats` (éves átlag, már
  KAT-vektoros).

**NEM fedett forrás, és MIÉRT:**
- **alapkőzet** — a Core-ban CSAK egy plate-szintű `isOceanic` bool létezik
  (`CrustElevation.cs`); minden szárazföldi tile-on ez UGYANAZ, tehát nulla
  tile-közi varianciát ad szárazföldön belül. Valódi kőzettípus/litológia
  NINCS modellezve. Amit ebből felhasználtunk: a LEJTŐ (elevációgradiens)
  mint csupasz-kőzet-kitettség proxy a `Depth`-hez — ez a MÁR dokumentált
  biome-táblázati kapcsolatot (§5: "Csupasz szikla … lejtő > 25°") követi,
  nem új feltalálás.
- **vulkanizmus** — `VolcanicEruption.cs` (ND-29) csak epizodikus,
  RITKA VEI8-eseményeket generál (várhatóan 1-2/millió év), nincs
  PERZISZTÁLT, tile-onkénti hamu-/tefra-lerakódás mező, amit fel tudnánk
  használni. Egy ilyen mező (távolság-alapú lecsengéssel, deep-time-ban
  felhalmozva) ÖNÁLLÓ, jövőbeli feladat lenne.

**Ezért a maradék 7 mező (`MineralDiversity`, `PhosphorusAvailability`,
`NitrogenAvailability`, `Iron`, `Sulfur`, `Salinity`, `pHProxy`) MIND
HALASZTVA marad** — mindegyik ténylegesen litológia/vulkanizmus-függő
lenne. Az I4 invariáns szerint inkább hiányozzon a mező (a C#
`RegolithProfile` struct egyelőre nem is tartalmazza őket), mint kitalált
érték szerepeljen rajta. **Előfeltétel a feloldásukhoz:** (1) egy
kőzettípus/litológia-modul, ami tile-szinten megkülönbözteti a
szárazföldi alapkőzetet (jelenleg nincs ütemezve), ÉS (2) egy perzisztált,
deep-time-ban felhalmozott vulkáni hamu-/tefra-lerakódás mező (a jelenlegi
epizodikus `VolcanicEruption` kiterjesztése).

**Nincs seed-törő hatás.** Ez a modul (`regolith_ref.py` +
`compute_regolith_profile`) kizárólag a CLAUDE.md táblázata szerint
garantáltan bitpontos műveleteket használ (`+ − × /`, `abs`, `min`, `max`
— nincs `Sin`/`Cos`/`Exp`/`Log`/`Pow` a láncban) és **nem igényel új
véletlenszám-mintavételt** (nincs új `RandomDomain`/`RandomProperty`) — a
három kimenet tisztán a már verifikált Core-kimenetek (elevéció/lejtő,
erózió, üledék, csapadék, hőmérséklet) algebrai függvénye. Ezért a C#-port
(amikor elkészül) ELVBEN bitpontosan, tolerancia nélkül egyezhet a
Python-referenciával — ezt a C#-implementáció dönti el véglegesen, nem ez
a döntés.

**Nyitva marad:** az MVP-konstansok (`DEPTH_MAX_M`, `FREEZE_THAW_HALF_RANGE_K`,
`PRECIP_REFERENCE` stb., ld. `docs/01-architecture.md` §13.2) illusztratívak,
vizuális kalibrálást igényelnek (ugyanaz a minta, mint az ND-41 szél/
csapadék-konstansai) — csak Unity-render után finomíthatók érdemben. A
§2.3 panel-táblázat "Soil fertility: mélység × minerality × nedvesség"
képlete mostantól `Depth × WaterRetention`-re mutasson (a "minerality" tag
kimarad, amíg a kémiai mezők blokkoltak — ld. `docs/01-architecture.md`
§13.6 táblázata). Az ordinális "Soil fertility" panelmező tényleges
kalibrálása (ND-09 mintájára, N≈500 világ, kvintilis-küszöbök) csak a
C#-port elkészülte UTÁN lehetséges — ez NEM ennek a döntésnek a
hatóköre.


### ND-119 — A szél-overlay NEM azt a szelet mutatja, amit a szimuláció használ (NYITOTT)

**2026-09-20.** A todo.md #9 visszajelzés ("szél-overlay befagyasztja a
nézegetőt") teljesítmény-vizsgálata közben derült ki egy tartalmi
eltérés, ami I3/I4-et érint.

**A tény.** A `WindPrecipitation.WindVector` utolsó tagja a
*hegy-eltérés* (`ApplyMountainDeflection`), ami egy elevációs gradienst
vár. A világmodell saját csapadék-mezője —
`MoisturePrecipitation.Compute` — ezt a két paramétert **nullával** hívja
(`MoisturePrecipitation.cs`, a `WindVector`-hívás `0.0, 0.0` argumentuma),
tehát a szimuláció szele hegy-eltérés NÉLKÜL készül. A **megjelenítő**
szél-overlay viszont sarkonként kiszámolja a gradienst, és ÁTADJA.
Következésképp az overlay egy olyan szélmezőt rajzol ki, amit a
világmodellben semmi nem használ.

**Miért számít.** I3: "a képen látható minden pixel a világmodellből
következik". Egy overlay, ami a modellétől eltérő mennyiséget mutat, ezt
formálisan sérti — nem kitalált érték, de nem is a modell értéke.

**Másodlagos tény (méréssel).** A hegy-eltérés mértéke
`maxFraction * tanh(slopeMag / 0.5)`. A valós gradiens-nagyságok
10^4 nagyságrendűek, tehát a `tanh` MINDIG telítésben van: a tag
kizárólag a gradiens IRÁNYÁTÓL függ. Az irány viszont erősen skálafüggő
(a domborzat nagyfrekvenciás), így az overlay képe azon múlik, milyen
lépésközzel deriválunk — ami eddig egy tetszőleges konstans volt
(`GradientEps = 1e-3` radián), nem a megjelenített LOD felbontása.
Mért hatás a szín-rámpán (400 minta, `windSpeedColorMaxMs=15`):

| gradiens forrása | lépésköz | átlagos rámpa-eltolódás | minták >5% |
|---|---|---|---|
| pontonkénti véges differencia (régi) | 1e-3 rad | — (referencia) | — |
| base-szintű sarokrács (level 8, MOST ez fut) | 6,1e-3 rad | 0,090 | 27,8% |
| referencia-tile-onként (level 5, ELVETVE) | 4,9e-2 rad | 0,124 | 36,8% |

**Ami MOST történt (nem döntés, teljesítmény-javítás).** A
`WindSpeedColorAt` 238 us/sarokról 6,3 us/sarokra csökkent (38x): az
elevációs gradiens a MÁR KISZÁMOLT base-szintű sarok-pozíciókból jön, a
hőmérséklet-gradiens pedig az ND-64 előre számolt napi Nap-irányaiból
(ez utóbbi BITRE azonos). Az overlay ettől használható lett (82 FPS).
A fenti táblázat középső sora a mostani állapot.

**Opciók.**

- **(A) Az overlay a szimuláció szelét mutassa** — a hegy-eltérés-tagot
  nullával hívjuk, ahogy a `MoisturePrecipitation` is. I3/I4-tiszta,
  INGYEN van (a gradiens-számítás teljesen elmarad), és a kép
  skálafüggetlenné válik. Ára: eltűnik a domborzati részlet az
  overlayről, a kép zonálisabb/simább lesz.
- **(B) Marad a hegy-eltérés az overlayen, de RÖGZÍTETT skálával** — a
  mostani állapot dokumentálva: a gradiens a base-szint rácsosztásán
  értendő, és ezt az overlay felirata is közli. Az I3-eltérés megmarad,
  csak explicit lesz.
- **(C) A hegy-eltérés kerüljön be a SZIMULÁCIÓBA is** — a
  `MoisturePrecipitation` is adja át a gradienst. Ez fizikailag a
  legerősebb (az orografikus csapadék így kap szél-oldali erősítést a
  már meglévő `uplift` tagon felül is), de **SEED-TÖRŐ**: minden
  csapadék-mező, minden folyó-forrás és így minden régió-név megváltozik,
  és az ND-09 ordinális kalibrációt is újra kell futtatni.

**Javaslat: (A).** Az overlay feladata az, hogy a modellt mutassa; a
skálafüggő, telítésben lévő hegy-eltérés-tag ma inkább zajt ad, mint
információt, és épp ez a tag volt a költség 89%-a. (C) önmagában is
védhető fizikailag, de seed-törő, tehát külön, tudatos lépés kell hozzá —
nem egy overlay-hiba mellékterméke.

**LEZÁRVA (2026-09-21): (A) — a felhasználó döntése.** Az overlay mostantól
a hegy-eltérés tagot NULLÁVAL hívja, pontosan úgy, ahogy a
`MoisturePrecipitation.Compute` is — az overlay tehát a modell szelét mutatja
(I3/I4 helyreállt).

**Mellékhatás:** a sarkonkénti elevációs gradiens számítása teljesen elmarad.
Ez volt a `WindSpeedColorAt` költségének 89%-a, és emiatt kiesett a
`TryStaticCornerElevationGradient` / `ElevationGradientTangent` pár is
(126 sor törölve). A szél többi tagja (zonális alap, termikus szél, Coriolis)
változatlanul sarkonként számolódik az ND-64 mintákból — az overlay tehát nem
lesz blokkos.

A kép ezzel **skálafüggetlen** lett: korábban azon múlt, milyen lépésközzel
deriváltunk, mert a `maxFraction * tanh(slopeMag / 0.5)` tag a valós, 10^4
nagyságrendű gradienseknél MINDIG telítésben volt, tehát kizárólag a gradiens
IRÁNYÁTÓL függött. Ez zajt adott, nem információt.

### ND-120 — A GPU-osztályozó shader két algoritmus-generációval le van maradva (NYITOTT)

**2026-09-20.** A todo.md úgy fogalmazott, hogy a jelenetben
`useGpuClassification: 1` **aktív**, tehát a besorolás és a geometria eltérő
elevációt lát. A vizsgálat ezt **részben cáfolta, részben súlyosbította.**

**1. A GPU-ág ma ELÉRHETETLEN.** A `PrecomputeClassificationsInParallel`
mindhárom hívója `forceCpu: true`-t ad (az ND-47 3. fázisa óta: worker
szálról a GPU-dispatch tilos), a `BuildStaticBaseLayer` sűrű ága pedig eleve
a tiszta CPU-s `PrecomputeStaticClassificationsInParallel`-t hívja. A 310
PerfLog átvizsgálása megerősíti: `usedGpu=True` **utoljára 2026-09-11-én**
fordult elő, azóta egyszer sem. Tehát **nincs élő CPU/GPU eltérés** — a
jelenetbeli `1` egy halott kapcsoló volt, ami élőnek látszott.

**2. Ha viszont bárki visszakapcsolná, az nem „gyorsítás" lenne.** A
`TileClassification.compute` `BaseElevationF`-je **két** Core-újítást nem
tartalmaz:
- **ND-52** másodlagos részletzaj (`SecondaryNoiseAmplitudeMeters = 900`);
- **ND-90** lemezhatár-keverés (`BlendedBaseElevationFromNoiseBasis`).

**Mérés** (`GpuShaderElevationParityTests`, seed `0xA7C944210000`, 20 lemez,
tengerszint mindkét oldalon a CPU-ból):

| hiányzó tag | \|Δ\| átlag | \|Δ\| max | óceán/szárazföld átfordulás | biome-átfordulás |
|---|---|---|---|---|
| csak ND-52 (másodlagos zaj) | 282,0 m | 890 m | **21,83%** | 23,43% |
| csak ND-90 (határkeverés) | 23,4 m | 3071 m | 0,39% | 0,40% |
| **a shader tényleges állapota (mindkettő hiányzik)** | **300,8 m** | **3116 m** | **21,99%** | **23,60%** |

Level 8-on (393 216 tile) gyakorlatilag ugyanez: 22,13% / 23,78% — az arány
**skála-stabil**. A két tag jellege eltér: a másodlagos zaj GLOBÁLIS (mindent
elmozdít, korlátos amplitúdóval), a határkeverés LOKÁLIS (kevés tile, de ott
nagyobb ugrás).

**A szám ALSÓ KORLÁT.** A mérés mindkét oldalon float64-gyel fut, tehát a
shader float32-es pontosságvesztése és bármilyen egyéb elcsúszás **nincs
benne**; kráter és deep-time erózió nélküli, t=0 alapdomborzatot hasonlít.

**Amit most tettem (nem döntés):**
- a jelenetbeli `useGpuClassification` 1 → **0** (ma viselkedésben semleges,
  mert az ág elérhetetlen — pusztán megszünteti a félrevezető állapotot, és
  összhangba hozza a C#-alapértékkel meg a shader saját kommentjével);
- a mérés `GpuShaderElevationParityTests`-be zárva, hogy a szám ne avuljon el;
- a mező mellé figyelmeztető blokk került a kóddal együtt olvasható helyre.

**Opciók.**

- **(A) A GPU-osztályozó út törlése** (shader, `GpuTileClassifier`, a mező és
  a jelenetbeli kapcsoló). Indok: ma is halott kód; kétszer futott
  „Compiler timed out"-ba; és amíg létezik, MINDEN jövőbeli Core-algoritmus-
  változást kézzel kellene utánavezetni — ez állandó I1-kockázat (egy
  elfelejtett port csendben más világot osztályozna). A klasszifikáció amúgy
  is 453 ms, amit az ND-50 óta overlay-váltáskor már át is ugrunk.
- **(B) A shader felzárkóztatása** (ND-52 + ND-90 portolása HLSL-be), majd
  egy CPU/GPU egyezési teszt, ami CI-ban fut. Ez megtartja a jövőbeli
  gyorsítási lehetőséget, de a fordítási időtúllépés kockázata megmarad, és
  a tesztnek valódi GPU kell — a CI-gépeken nincs.
- **(C) Marad úgy, ahogy most van**: halott kód, kikapcsolt kapcsoló,
  figyelmeztető komment és a mérést rögzítő teszt.

**Javaslat: (A).** A GPU-ág egy meg nem valósult optimalizáció maradványa,
ami nem termel értéket, viszont folyamatos determinizmus-kockázatot igen. Ha
a klasszifikáció később tényleg szűk keresztmetszet lesz, akkor érdemes
újraírni — a mai, elavult shader nem alap ehhez. (C) elfogadható átmenet;
(B) csak akkor védhető, ha valaki ténylegesen vállalja a folyamatos
karbantartást ÉS van hol futtatni az egyezési tesztet.

**Verziózás:** önmagában nem seed-törő (a GPU-ág ma nem fut, tehát egyetlen
világ sem függ tőle). (B) viszont azzá tenné, ha a portolás közben a
CPU-oldalt is hozzányúlnánk — nem szabad.

---

**LEZÁRVA (2026-09-21): (A) — a felhasználó döntése.** Törölve:
- `PrecomputeClassificationsOnGpu` (a dispatch-ág és a metódus),
- a `useGpuClassification` mező és a jelenetbeli kapcsoló,
- `Assets/Scripts/Viewer/Gpu/GpuTileClassifier.cs` (nem volt más hívója).

**A `forceCpu` paraméter szándékosan MEGMARADT**: az async emit-út ezzel
jelzi, hogy worker szálról fut. Ma már nincs másik ág, de a hívási felület
így változatlan, és egy jövőbeli, ELLENŐRZÖTT GPU-út ide illeszkedne vissza.

**AMIT NEM TÖRÖLTEM, ÉS MIÉRT — ezt érdemes tudni.** A
`TileClassification.compute` asset **megmaradt**, mert a `useGpuGeometry` út
is használja (`GpuTerrainGeometryGenerator`), és **az is ugyanazt az elavult
`BaseElevationF`-et** hívja. Vagyis a 22%-os eltérés kockázata a
`useGpuGeometry` bekapcsolásával **ma is él**. Ez nem volt része a
döntésnek, ezért nem nyúltam hozzá; a mező tooltipje viszont mostantól
tételesen kiírja a mért számokat. A shaderhez amúgy is óvatosan kell érni:
kétszer futott „Compiler timed out"-ba, és a mostantól nem hívott
`CSClassifyTiles` kernelt épp ezért hagytam benne.

A `GpuShaderElevationParityTests` **MARAD**, újrakeretezve: az állítása
független attól, hogy van-e GPU-út — ez a két tag nem elhanyagolható.
Konkrétan a `useGpuGeometry` utat védi, és minden jövőbeli „egyszerűsített"
eleváció-közelítést (GPU, előre számolt textúra, LOD-proxy).

### ND-121 — A balance munkalistás átírása kész; a `MaximumRenderBudget` plafon emelése döntést kér (NYITOTT)

**2026-09-20.** Az `AdaptiveQuadTree.MaximumRenderBudget = 48 000` doksija
**két** okot nevez meg, és a másodikat kifejezetten feltételhez köti:
„Amíg a ciklus teljes újraszkennelés helyett nem munkalistával dolgozik, a
48 000 marad." Ez a feltétel most teljesült.

**1. Előfeltétel tisztázva: a fixpont korlátok nélkül EGYÉRTELMŰ.**
A saját magamnak előírt előfeltétel az volt, hogy a kimenet sorrend-függő-e.
`BalanceFixpointUniquenessTests`: öt gyökeresen eltérő végrehajtási sorrend —
köztük egy teljesen aszinkron, véletlen választással dolgozó fixpont-kereső —
**azonos vágást ÉS azonos lépésszámot** ad, és megegyezik a termelési,
körökben dolgozó implementációval.

Az ok szerkezeti, és külön tesztben is rögzítve: a felosztás **monoton** — egy
tile felosztása soha nem szüntet meg másik, fennálló szintkülönbség-sértést
(a fedő ős csak finomabb lehet, a különbség tehát csak csökken). A lezárás
ezért sorrendtől független legkisebb fixpont.

**Sorrend-függőség CSAK a korlátoknál van** (`WithATightBudgetTheOutcomeDoesDependOnOrder`):
ott a ciklus félbeszakad, és számít, melyik felosztások fértek be. Az átírás
ezért nem hivatkozhatott pusztán a fixpont egyértelműségére — a kör-szemantikát
is meg kellett őriznie.

**2. Az átírás.** Az első kör változatlanul a teljes vágást járja be; a
többi csak a *frontier*-t: az előző körben keletkezett gyerekeket **és a
sértést kiváltó leveleket**. A második fél nélkülözhetetlen, és az első,
hibás változatomból hiányzott: a sértés MINDIG a finom oldalról látszik, egy
felosztás viszont csak egy szintet javít, tehát a kiváltó finom levél a
következő körben is sérthet — a durva oldalról ez nem vehető észre (a
szomszéd-területnek nincs fedő őse a vágásban). Hat egyenértékűségi teszt
bukott el rá, mielőtt kijavítottam.

**Igazolás.** `BalanceWorklistEquivalenceTests`: 600 véletlen konfiguráción
(40 seed × 3 mélység × 5 budget, a szorító eseteket is beleértve) a kimenet
**minden esetben azonos** a régi, teljes szkennelésű referenciával, 3,5×
kevesebb bejárt levél mellett. Nagy kaszkádon: 4032 → 14 070 levél,
**11 kör mindkettőnél**, bejárt levelek 129 381 → 25 683 (5,0×), idő
53,0 ms → 12,4 ms (4,3×).

**A költség-modell változása:** `körök × O(|cut|)` helyett
`1 × O(|cut|) + O(felosztások)`.

**3. A plafon — ITT KELL DÖNTENI.** A `RenderBudgetForViewport` a
`MaximumRenderBudget`-tel **vág**, és ez ma köt:

| felbontás | a képlet kérése (8 px cél, 3,0 overhead) | ténylegesen kapott | vágás |
|---|---|---|---|
| 1920×1080 | 97 200 | 48 000 | **2,0×** |
| 2560×1440 | 172 800 | 48 000 | 3,6× |
| 3840×2160 | 388 800 | 48 000 | 8,1× |

Ez közvetlenül a felhasználó #1 visszajelzése („brutál nagyok a tile-ok").

**Ami az emelés ellen szól, és NEM oldódott meg:** a doksi 1. oka, a
**költség-paritás**. 48 000 levélnél a worker-költség 150–158 ms, ami pont
annyi, amennyi a változtatások előtt 8000 levéllel volt. A plafon emelése
ezt a paritást lépné túl — ez UX-kompromisszum, nem technikai kérdés.

**A 2. ok mostani állapota — BECSLÉS, nem mérés.** A doksi szerint 96 000-es
budgetnél a vágás 66 900-nál telítődik, és a balance 4 kör / 108 felosztás /
320 ms. Az új költség-modellből ez ≈ egy kör bejárása + elhanyagolható maradék,
vagyis nagyságrendileg **80–110 ms**. Ezt offline NEM tudtam reprodukálni (a
konkrét eset a Unity-oldali terep-proxit és felszíni metrikát igényli; az én
offline nézetemben a vágás pont a budgeten telítődik, így az ND-116 korai
kilépés lép életbe és a balance 0 kört fut). **A számot a felhasználó
PerfLogja tudja megerősíteni.**

**Opciók.**

- **(A) Marad 48 000.** A költség-paritás sértetlen. A tile-méret-panasz
  megoldását máshonnan kell hozni (pl. a már megemelt split-kvóta, ND-76).
- **(B) Emelés 96 000-re.** A képlet 1080p-s kérésének (97 200) gyakorlatilag
  a teljes kielégítése. Várható worker-költség a doksi táblája + az új
  balance-modell alapján: selection ~73 ms + balance ~80–110 ms ≈ **150–185 ms**
  — vagyis nagyjából a MAI 48 000-es összköltség, mert a selection a
  telítődés miatt nem nő tovább. Ez a becslés a megerősítendő pont.
- **(C) Felbontás-arányos plafon** (pl. `pixelWidth * pixelHeight / 24`), hogy
  4K-n se legyen 8× vágás. Nagyobb munka, és a gyengébb gépeken kockázatos.

**Javaslat: (B), de CSAK élő visszamérés után.** A `adaptiveRenderBudget`
SerializeField Play közben felülírható, tehát a felhasználó ki tudja próbálni
96 000-rel, mielőtt a konstans változik. Ha a PerfLog `balance` sora tényleg
100 ms körül marad, az emelés indokolt; ha 300 ms marad, (A) a helyes.

**Verziózás:** nem seed-törő (a vágás megjelenítési döntés, nem világmodell).

### ND-122 — A terrain-bázis lemez-gyorsítótár érvényesítése: újraszámolás, nem verziókonstans (LEZÁRVA)

**2026-09-20.** A hideg Build statikus sarok-terrain-bázisa élesben mérve
**7,4–8,5 s** (a `terrainBasis=` PerfLog-sor), level 8-on 54,4 MiB nyers adat.
A todo.md 9. sora lemez-gyorsítótárat javasolt, három kikötéssel: **nem lehet
world-state**, **eltérő algoritmusverziót nem tölthet be**, és kell
méret-/I/O-/invalidációs terv.

**A döntés, amit meghozni kellett:** miből tudja a betöltő, hogy a fájl a
*mostani* algoritmussal készült?

- **(A) Kézzel emelt verziókonstans.** A szokásos megoldás, és pont az a
  törékeny: akkor bukik el, amikor valaki a `DomainWarp`-ot vagy a
  `CrustElevation.ComputeNoiseBasis`-t módosítja és **elfelejti** emelni a
  számot. A következmény csendes: egy másik világ domborzata töltődne be,
  látható hibaüzenet nélkül. Ez az I1 (determinizmus) invariáns sérülése
  lenne, a legrosszabb fajtából — észrevehetetlen.
- **(B) A forrásfájlok hash-e.** Automatikus, de túl érzékeny (egy komment
  átírása is érvénytelenít) ÉS nem elég pontos (a Core NuGet-/fordítóverzió
  változását nem látja).
- **(C) Újraszámolásos validáció.** Betöltéskor a fájlból vett, széles szórású
  mintát ÚJRASZÁMOLJUK és bitre hasonlítjuk.

**Választás: (C).** Ez az egyetlen, ami nem emberi figyelemre épít: az
ellenőrzés *ugyanazt a függvényt* futtatja, amit a cache tárol, tehát bármely
algoritmus-változás automatikusan érvényteleníti a fájlt.

**Paraméter:** 1024 minta (`ValidationSampleCount`), Knuth-féle szorzóprím
lépésközzel szórva, az első és utolsó indexszel kiegészítve. Mérve: a
validáció ~18 ms a 7400–8500 ms helyett — 0,25% ráfordítás.

**A maradék kockázat, kimondva.** Ha egy változás a bejegyzéseknek csak
töredékét érinti, a mintavétel elvileg átengedheti. 1024 minta mellett egy
1%-nyi bejegyzést érintő változás észlelési valószínűsége ~99,996%; egyetlen
bejegyzést érintőé viszont elhanyagolható. A teljes újraszámolás elvenné a
gyorsítótár értelmét, ezért ez **tudatos kompromisszum**, nem figyelmetlenség.
Aki ennél szigorúbbat akar, emelje a mintaszámot — a költség lineáris.

**A másik két kikötés.**
- *Nem world-state:* a betöltés bármilyen kétségnél `null`-t ad; nincs
  „javítás" útvonal és nincs részleges betöltés. A fájl törlése csak lassít.
  Minden I/O-hiba elnyelt: a Build sosem bukhat el a gyorsítótáron.
- *Méret/I/O:* a fájlméret előre kiszámítható (`ExpectedFileSize`); a
  könyvtár kvótája 512 MiB (≈9 level-8-as világ), a legrégebben írt fájlok
  esnek ki. Az írás ideiglenes fájlba megy, majd átnevezés — egy félbeszakadt
  írás nem hagy hátra betölthető fél-fájlt.

**Mérés (level 8, 396 294 bejegyzés, 54,4 MiB, memóriában):** szerializálás
118 ms (ebből ellenőrzőösszeg 52 ms), visszaolvasás + ellenőrzés 99 ms.
Ehhez jön a fizikai lemez-olvasás és a valós újraszámolásos validáció
(~18 ms). Várható összes: **~120–230 ms a 7,4–8,5 s helyett.**

**Verziózás:** nem seed-törő — a gyorsítótár származtatott adat, a világmodell
nem függ tőle.

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
