# Unity 6 + HDRP kiértékelés — ND-01 addendum

**Kontextus:** a viewer-stack döntése. Korábban Godot 4 + C# volt az ajánlásom, a Rust + wgpu az alternatíva. Ez a dokumentum a harmadik utat méri fel.

> **Megbízhatósági megjegyzés:** a HDRP API-k és feature-készlet gyorsan változnak, a tudásom 2026 májusáig terjed. Az alábbi technikai állítások közül a *koncepcionálisakat* (mit tud a rendszer elvben) magabiztosan állítom; a *konkrét API-neveket és verziószámokat* ellenőrizni kell az aktuális dokumentációban. Ahol különösen bizonytalan vagyok, jelölöm.

---

## 1. Amit a HDRP valóban ad ehhez a projekthez

### 1.1 Physically Based Sky — ez a legerősebb érv

A HDRP `PhysicallyBasedSky` nem egy skybox, hanem **valódi atmoszférikus szórás-modell bolygóparaméterekkel**:

| Paraméter | Jelentés | A mi modellünkben |
|---|---|---|
| Planetary radius | bolygósugár | `PlanetDefinition.RadiusKm` — **közvetlen megfeleltetés** |
| Atmospheric thickness | légkör vastagsága | levezethető a felszíni nyomásból |
| Air density / tint | Rayleigh-szórás | légkörösszetételből |
| Aerosol density / anisotropy | Mie-szórás | por, páratartalom |
| Ground albedo / tint | felszíni visszaverés | biome-albedo átlag |
| Spherical mode | űrből nézhető-e | **igen** — ez a kulcs |

Ez pontosan az, amit a Godotban shaderből kellene megírni. Az űrből nézett kék perem, a terminátor lágy átmenete, a napkelte vörös szórása — mind kész, fizikailag megalapozott, és a bemenetei megegyeznek a generátor paramétereivel.

**Ez önmagában több hét munkát spórol**, és a minősége magasabb, mint amit reálisan kézzel írnál.

### 1.2 Volumetric Clouds

A HDRP volumetrikus felhőrendszere raymarch-alapú, támogatja a bolygógörbületet, és cloud map textúrával vezérelhető — ami pontosan illeszkedik ahhoz, amit a §3.4-ben terveztünk (a `CloudDensity` mező hajtja, nem dekoratív noise).

> **Bizonytalan:** hogy az űrből nézett teljes bolygó felhőzete mennyire jól működik. A rendszer eredetileg földfelszíni nézetre készült, és a „globális" mód képességei verziónként változtak. **Ellenőrizendő prototípussal**, mielőtt erre alapoznánk.

### 1.3 Water System

A régiónézethez (Silvertide Delta) a HDRP vízrendszere ad hullámokat, habot, mélységfüggő abszorpciót, part menti deformációt. Ez a fidelity-terv A4 pontja, készen.

### 1.4 Ami a szimulációs oldalon számít: Burst + Job System

Ez a nem nyilvánvaló előny, és lehet, hogy fontosabb a rendernél.

A **Burst compiler** a C# egy részhalmazát (`Unity.Mathematics` típusokkal) natív, SIMD-optimalizált kódra fordítja. A gyakorlatban ez **2–10× gyorsulást** ad a sima C#-hoz képest numerikus kódon — ami közel viszi a Rust teljesítményéhez.

Ez érdemben megváltoztatja a v0.1 ND-01 mérlegét: ott azt írtam, hogy „C#-ban 2–5× lassabb lesz, mint Rustban". **Burst-tel ez a különbség nagyrészt eltűnik.**

> **Kritikus determinizmus-figyelmeztetés:** a Burst alapértelmezés szerint `FloatMode.Default` módban fordít, ami **engedélyezi a lebegőpontos műveletek átrendezését** (fast-math). Ez determinizmus-szempontból végzetes. A szimulációs kódra kötelezően `[BurstCompile(FloatMode = FloatMode.Strict)]` kell. Ezt **CI-ban ellenőrizni kell**, mert egyetlen hiányzó attribútum csendben elrontja a reprodukálhatóságot, és csak a platformok közötti hash-eltérésnél derül ki.

### 1.5 Compute shader ergonómia

A Unity `ComputeShader` API érettebb és kényelmesebb, mint a Godot `RenderingDevice`-a: `SetBuffer`, `Dispatch`, `AsyncGPUReadback`, tisztább hibaüzenetek, jobb profilozás. Az A1 eróziós pass szempontjából ez érzékelhető különbség.

---

## 2. Amit a HDRP NEM ad — és amit sokan félreértenek

| Elvárás | Valóság |
|---|---|
| „Unity Terrain majd megoldja a domborzatot" | **Nem.** A Unity Terrain sík heightmap-alapú, gömbön nem működik. Saját mesh-generálás és LOD kell, ugyanúgy, mint Godotban. |
| „A HDRP nagy világokat kezel" | Részben. Camera-relative rendering van, de **nincs double precision**. Lásd 2.1. |
| „A PBS majd megjeleníti a bolygót" | A PBS a *légkört* adja. A felszínt egy egyszerű gömbtextúrával közelíti — a valódi domborzatot te renderelted. |
| „HDRP = fotorealizmus automatikusan" | A HDRP jó alapanyagot ad, de a bolygóléptékű shading, LOD-átmenetek, textúra-streaming a te dolgod. |

### 2.1 A precíziós probléma — ez a legsúlyosabb hátrány

Ez a Godot melletti egyetlen valóban erős érv, és fontos:

| | Godot 4 | Unity 6 |
|---|---|---|
| Double precision build | **Igen** (`precision=double` fordítási opció) | **Nincs** |
| Camera-relative rendering | Igen | Igen (alapértelmezett HDRP-ben) |
| Floating origin | Kézzel | Kézzel |

7 420 km sugárnál a `float32` a felszín közelében kb. **0.5–1 m** felbontást ad a világkoordinátákon. Ez látható vertex-remegést és z-fightingot okoz a régiónézetben.

A Unityben a megoldás **floating origin** (a világot mozgatod a kamera körül, nem fordítva) plusz **logaritmikus depth buffer**. Mindkettő bevett technika, de:

- kézzel implementálandó,
- minden rendszernek tudnia kell róla (fizika, particle, UI-világhorgony),
- és a HDRP belső rendszereivel néha ütközik.

Godotban a `precision=double` build ezt nagyrészt megszünteti. **Ez reálisan 1–3 hét munka és egy állandó kockázati felület a Unity oldalán.**

> **ND-19 — nyitott:** ha Unity mellett döntünk, a floating origin stratégiát **M2-ben** kell megtervezni, nem később. Utólag beépíteni fájdalmas.

---

## 3. Licencelés — 2026-os állapot

<cite index="6-1,10-1">A vitatott Runtime Fee-t a Unity 2024 szeptemberében teljes egészében visszavonta, és visszatért a licencszám-alapú előfizetéses modellhez.</cite> A jelenlegi helyzet:

| Tier | Feltétel | Ár |
|---|---|---|
| **Personal** | <cite index="4-1">Éves bevétel és forrásbevonás összesen 200 000 USD alatt</cite> | **Ingyenes** |
| **Pro** | <cite index="1-1">200 001 – 24 999 999 USD</cite> | Előfizetés, <cite index="8-1">2026. január 12-től 5%-kal emelve</cite> |
| **Enterprise** | <cite index="4-1">25 M USD felett</cite> | Egyedi |

<cite index="8-1">A Unity eltávolította a Runtime Fee-re vonatkozó szövegrészeket az Editor Software Terms-ből.</cite>

**A te helyzetedben:** Personal tier, ingyenes. A projekt évekig ebben a sávban marad.

**A valódi kockázat nem az ár, hanem a kiszámíthatóság.** A 2023-as epizód megmutatta, hogy a feltételek egyoldalúan változhatnak egy már futó projekt alatt. Egy 3–5 éves fejlesztésnél ez nem nulla kockázat. A Godot MIT-licence ezzel szemben visszavonhatatlan.

Ez nem technikai, hanem stratégiai megfontolás — de valós, és a te döntésed, mennyire súlyozod.

---

## 4. Háromutas összehasonlítás

| Szempont | Godot 4 + C# | **Unity 6 + HDRP** | Rust + wgpu |
|---|---|---|---|
| Atmoszféra űrből | Saját shader (~3-4 hét) | **Kész (PBS)** | Saját shader |
| Volumetrikus felhő | Saját (~3-4 hét) | **Kész, de ellenőrizendő** | Saját |
| Víz-shader | Saját | **Kész (Water System)** | Saját |
| Gömb-LOD terep | Saját | Saját | Saját |
| **Double precision** | **Beépített** | Nincs — floating origin kell | Natív `f64` |
| Compute ergonómia | Közepes | **Jó** | **Kiváló** |
| Szimuláció perf | Sima C# | **Burst: közel Rust-szint** | **Legjobb** |
| Determinizmus | Jó | Jó, **de Burst FloatMode.Strict kötelező** | **Kiváló** |
| Adatsűrű UI | **Jó (Control+Theme)** | **Jó (UI Toolkit)** | Gyenge (egui) |
| Iterációs sebesség | Jó | **Kiváló** | Gyenge |
| Licenckockázat | **Nincs (MIT)** | Alacsony, de nem nulla | **Nincs** |
| Build-méret / indulás | **Könnyű** | Nehéz | Könnyű |
| Editor stabilitás nagy projekten | Közepes | **Jó** | — |

### 4.1 Mérleg

**Unity mellett:** a PBS + Water System + volumetrikus felhő együtt reálisan **6–10 hét megspórolt renderelési munka**, magasabb kimeneti minőséggel. A Burst ráadásul a szimulációs oldalt is felgyorsítja, ami korábban a Rust fő érve volt. Ez a projekt két legnagyobb technikai kockázatát egyszerre csökkenti.

**Unity ellen:** a precíziós probléma valós és állandó (1–3 hét + folyamatos figyelem), a Burst determinizmus-buktatója csendes hibaforrás, és a licenc hosszú távon nem teljesen kiszámítható.

**A meglepetés:** a Burst miatt a Unity most **nem csak render-, hanem teljesítményoldalon is versenyképes a Rust-tal**. Ez érdemben más helyzet, mint amit a v0.1-ben felvázoltam — ott a Rust volt a perf-válasz.

### 4.2 Módosított ajánlásom

**Unity 6 + HDRP**, a következő feltételekkel:

1. A `WorldGen.Core` és a szimulációs modulok **tiszta .NET osztálykönyvtárak**, Unity-referencia nélkül, `netstandard2.1` targettel. Csak a viewer és a `Presentation` réteg Unity-projekt. Így a stack később cserélhető.
2. `Unity.Mathematics` + `[BurstCompile(FloatMode = FloatMode.Strict)]` **kötelező** minden szimulációs kódon, CI-ellenőrzéssel.
3. A floating origin stratégia **M2-ben** eldöntve és megvalósítva.
4. A volumetrikus felhő űrből-nézeti működése **M2-ben prototípussal ellenőrizve** — ha nem működik jól, a Planet nézet felhői saját shaderrel készülnek, és ez befolyásolja a becslést.

Ha az 1. pont teljesül, a döntés **visszafordítható**: rossz esetben a viewert cseréled, a magot nem.

---

## 5. Új nyitott döntések

| ID | Kérdés | Javaslatom | Mikor |
|---|---|---|---|
| **ND-19** | Floating origin stratégia | Kamera-központú világeltolás + logaritmikus depth; M2-ben megtervezve | M2 |
| **ND-20** | Burst determinizmus-kikényszerítés | Roslyn analyzer vagy CI-szkript, ami hibát dob `FloatMode.Strict` nélküli `[BurstCompile]`-ra a `WorldGen.*` névtérben | M1 |
| **ND-21** | HDRP volumetrikus felhő űrből | M2 prototípus dönti el; fallback: saját felhő-shader a Planet nézetre | M2 |
| **ND-22** | Core assembly-izoláció | `netstandard2.1`, nulla Unity-referencia, külön solutionben építve és tesztelve | M0 |
