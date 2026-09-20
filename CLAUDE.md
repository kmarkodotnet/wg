# CLAUDE.md

Ez a fájl a projekt működési szabályzata. Minden session elején olvasd el.

Aktuális tudásátadás (2026-09-12): [Codex → Claude, Sonnet/Opus](kt_2_co2cl.md).
Korábbi pillanatkép: [Claude → Codex](kt_1_cl2co.md), eredetileg
`docs/07-handover-2026-09-10.md`. A lentebbi Állapot rész történeti;
a tényleges branch, munkafa és az új átadás az aktuális kiindulópont.

## Mi ez a projekt

Determinisztikus bolygó-világgenerátor. Egyetlen seedből teljes, fizikailag
összefüggő bolygót állít elő: lemeztektonika, domborzat, klíma, hidrológia,
jég, talaj, erőforrások — deep-time-ban, 0-tól 1 milliárd évig.

A kimenet kettős:
1. **Adatmodell** — később egy evolúciós szimuláció környezeti forrása.
2. **Renderelt kép + adatpanelek** — a `docs/01-architecture.md` §1 leírja a
   három nézetszintet (bolygó / kontinens / régió).

Teljes specifikáció: `docs/00-spec-v1.0.md`.

## A négy invariáns — ezeket soha ne sértsd meg

**I1. Determinizmus.** Ugyanaz a seed + ugyanaz a verzió → bitre ugyanaz a
világ. Minden platformon (Windows/Linux/macOS, x64/ARM64), minden szálszámon,
minden kiértékelési sorrendben.

**I2. A random réteg tiszta függvényekből áll.** Nincs állapot, nincs mező,
nincs thread-lokális seed, nincs inicializálási sorrend. Ha bárhol állapotot
tennél a `DeterministicRandom`-ba, az egész rendszer determinizmusa összeomlik.

**I3. A képen látható minden pixel a világmodellből következik.** Nincs kézzel
festett textúra, nincs dekoratív felhőréteg, nincs olyan vizuális elem, ami nem
valamelyik generált mezőből származik.

**I4. A panelen látható minden szám a világmodellből olvasható ki.** Nincs
kitalált érték, nincs placeholder. Minden panelmezőnek van forrása, egysége és
számítási lánca — lásd `docs/01-architecture.md` §2.

## Lebegőpontos determinizmus — a leggyakoribb csendes hibaforrás

| Művelet | Bitpontos? | Használható a kritikus úton? |
|---|---|---|
| `+ - * /` double-on | Igen, IEEE-754 | Igen |
| `Math.Sqrt` | Igen, IEEE-754 korrekt kerekítés | Igen |
| `Math.Log`, `Math.Exp`, `Math.Sin`, `Math.Cos`, `Math.Pow` | **NEM garantált** | **NEM** — lásd ND-23 |
| `float` (32 bit) fizikai mennyiségre | — | **NEM** — mindig `double` |
| `System.Random` | Nem | **SOHA** |
| `Guid.NewGuid`, `DateTime.Now`, `Environment.TickCount` | Nem | **SOHA** |

Ha transzcendens függvényre van szükség a szimulációban, az ND-23 döntést
igényel — ne kerüld meg csendben.

**Ha Unity mellett dőlne el az ND-01:** a Burst compiler alapból `FloatMode.Default`
módban fordít, ami engedélyezi a lebegőpontos műveletek átrendezését. Minden
szimulációs kódra `[BurstCompile(FloatMode = FloatMode.Strict)]` kötelező, és ezt
CI-ban ki kell kényszeríteni (ND-20).

## Repo-struktúra

```
docs/           specifikáció, architektúra, döntések
src/            a determinisztikus mag (motorfüggetlen!)
tests/          xUnit tesztek + testdata/testvectors.json
tools/reference Python orákulum — a C# ellen ezzel verifikálunk
```

### A `src/` motorfüggetlensége nem tárgyalható

A `WorldGen.Core` és minden szimulációs modul **netstandard2.1 + C# 9**.
Ez az a metszet, ami Unity 6, Godot 4 és sima .NET 8 alatt is fordul.

- **Ne** emeld a `LangVersion`-t (nincs file-scoped namespace, nincs `record`
  a `src/`-ben, nincs `required`, nincs primary constructor).
- **Ne** hivatkozz semmilyen motor-assembly-re a `src/`-ből.
- **Ne** használj `System.Text.Json`-t a `src/`-ben (a tesztekben szabad).

Ez azért fontos, mert az ND-01 (stack-döntés) **még nyitott**. Amíg így marad,
a mag mindhárom irányba nyitva áll.

## Munkamódszer

**Kód előtt dokumentáció.** Új modulnál előbb az architektúra-doksiba kerül a
terv (interfészek, adatfolyam, nyitott kérdések táblázatban), aztán a kód.

**Nyitott döntést dokumentálj, ne oldd meg csendben.** Ha implementáció közben
architekturális kérdés merül fel, vedd fel `docs/04-decisions.md`-be új
ND-számmal, opciókkal és javaslattal. Ne hozz csendben döntést, ami később
seed-törő változás lenne.

**Ne bízz az emlékezetedben algoritmus-konstansoknál.** A fejlesztés során a
Random123 KAT-vektorokat kétszer is hibásan idéztük fel emlékezetből, és
mindkétszer a hivatalos fájl derítette ki. Ha rotációs konstanst, mágikus
számot vagy tesztvektort írsz, **verifikáld forrásból** — a
`tools/reference/kat_vectors` pont ezért van a repóban.

**A Python referencia az igazság.** Ha a C# és a `tools/reference/` eltér, a
Python a helyes, hacsak nem bizonyítod az ellenkezőjét. A referencia hivatalos
KAT-vektorokhoz van mérve.

**Új numerikus algoritmusnál előbb referencia, aztán C#.** Ez a bevált sorrend:
Python orákulum → verifikálás ismert vektorokhoz → tesztvektorok generálása →
C# implementáció → C# a vektorokhoz mérve.

**Haladás-jelentés minden lépésnél.** A záró-összefoglalóhoz tartozik egy
mérföldkő-alapú % becslés (`docs/05-milestones.md` M0-M13 táblázatából,
tartalmi súlyozással, NE puszta darabszám-arány) és egy munkaóra-becslés
(eddig ráment / hátralévő). A munkaóra-szám **mindig jelezve legyen durva
becslésként**, nem mért tényként — nincs valós idő-naplózás a projektben,
ne állíts hamis pontosságot.

## Tesztelési elvárások

Minden új modulhoz kötelező:

| Teszt | Mit fog meg |
|---|---|
| Ismert-válasz teszt (ha van külső referencia) | Algoritmus-hiba |
| Tisztaság: ismételt hívás azonos | Rejtett állapot |
| Párhuzamos vs szekvenciális egyezés | Sorrendfüggés |
| Minden paraméter érdemben hat a kimenetre | Kimaradt paraméter a leképezésből |
| Eloszlás/plauzibilitás | Statisztikai hiba |
| Élesetek: 0, MaxValue, negatív, üres tartomány | Túlcsordulás, határhiba |

A CI négy platform-kombinációt futtat (Linux x64/ARM64, Windows, macOS) Debug
és Release módban. **Egyetlen eltérés is blokkoló** — nem "flaky teszt", hanem
determinizmus-sérülés.

## Verziózás és seed-kompatibilitás

Ezek **seed-törő** változások, amik verzió-emelést és a betöltésnél explicit
hibát igényelnek:

- A `DeterministicRandom` kulcs/counter leképezésének módosítása
- A `RandomDomain` / `RandomProperty` konstansok átszámozása vagy újrafelhasználása
- A Threefry körszám vagy konstansok módosítása
- A `TileId` bit-layout változása
- Bármely szimulációs algoritmus numerikus viselkedésének módosítása

Ha ilyet csinálsz, emeld a megfelelő verziószámot és írd le a döntésekben.

## Állapot

**Kész:** M1 — determinisztikus random réteg. M2 rács-matek — `TileId`,
Morton-kódolás, koordináta-konverzió, szomszédsági tábla
(`src/WorldGen.Core/Grid/`), mind tesztelve. M2 render-lépése —
`unity/WorldGenViewer/` (`unity-viewer` ágon): a `PlanetGridMesh` rács →
Unity mesh híd Game módban vizuálisan megerősítve — szürke gömb,
tile-határokkal, hézagok nélkül level 2/5/6-nál (level 7-nél a sűrű
határvonalak optikai aliasingot adnak, ami nem geometriai hiba). Az M2
"Kész, ha" kritériuma (szürke gömb, tile-határokkal) teljesült.
**Következő:** M3 — csillagászat + világítás (megvilágított gömb,
terminátorral). Az ND-19 (floating origin) NEM blokkolja: a HDRP
`Directional Light` csak irányt igényel, nem pozíciót, tehát M3-nak nem
kell valós léptékű koordináta — ND-19 implementációja M9-re halasztva.

Részletek: `docs/05-milestones.md`.

**ND-01 lezárva: Unity 6 + HDRP.** A `src/` motorfüggetlensége (netstandard2.1,
nulla Unity-referencia) ettől függetlenül megmarad — a Unity-projekt a
`src/WorldGen.Core`-t helyi package-ként, forrás szerint hivatkozza, nem
másolja. Aktív nyitott döntések a választás miatt: **ND-20** (Burst
`FloatMode.Strict` CI-kikényszerítés), **ND-21**
(HDRP felhő űrből — prototípussal ellenőrizendő).

## Nyelv

A kód, az azonosítók és a commit-üzenetek **angolul**. A kommentek, a
dokumentáció és a beszélgetés **magyarul**.



## Unity / WorldGenViewer

Unity project path:
F:\Claude\wg\unity\WorldGenViewer

A Unity Editor is normally running with the Unity Pipeline package.

For Unity-related work:
- Prefer Unity CLI / Pipeline for interacting with the live Editor.
- Use `unity status` before Editor operations.
- Use `unity command` or `unity list` to discover available commands instead of guessing command names.
- Target:
  `--project-path "F:\Claude\wg\unity\WorldGenViewer"`
- After C# changes, verify that Unity recompiles successfully.
- **Offline fordítási kapuk (Unity Editor NÉLKÜL is futnak).** Ha az Editor
  nem elérhető — vagy egyszerűen gyorsabb visszajelzés kell —, ezek fordítják
  le a Unity-oldali forrást a helyi Unity-DLL-ek ellen:

  ```
  dotnet build tests/WorldGen.Viewer.Compile          # Assets/Scripts/Viewer/ (+ Lod, Gpu)
  dotnet build tests/WorldGen.App.UnityBinding.Compile # Assets/Scripts/App/UnityBinding + EditorTools
  ```

  Nincsenek a `WorldGen.sln`-ben, mert a CI-gépeken nincs Unity. **Nem
  helyettesítik a Unity saját fordítását** (asmdef-szabályok, platform-define-ok),
  de az API-hibákat és a C# 9 / netstandard2.1 sértéseket azonnal megfogják.
- Check Unity Console errors before considering a task complete.
- When appropriate, run relevant Unity tests.
- Do not edit Library/PackageCache manually.