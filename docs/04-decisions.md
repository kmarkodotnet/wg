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
