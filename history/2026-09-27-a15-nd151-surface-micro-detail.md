# A15 / ND-151 — M13 2–4. fázis: per-pixel felszíni mikro-részlet

**Dátum:** 2026-09-27
**Ág:** `a19-plate-frame-noise`
**Kiváltó:** „todo2.md-ből olvasd fel A15 M13 fázis 2-4et, és implementáld”

## Amit a feladat kért, és amit a kód mondott

A `todo2.md` A15 sora a `docs/backlog.md` 297. sorára mutat: **2. fázis** — a
meglévő GPU compute pipeline kiterjesztése a teljes kiértékelésre; **3. fázis**
— GPU-vezérelt mesh; **4. fázis** — procedurális mikro-részlet textúra.

A 2–3. fázis **nem implementálható a leírás szerint**, és ez nem ízlés kérdése:
az a GPU compute pipeline, amit ki kellene terjeszteni, az **ND-128 óta nem
létezik** — mérés alapján törölték (a tile-ok ~22%-a a tengerszint másik
oldalára került, és lassabb is volt). A gyökérhiba osztálya: a GPU-n számolt
érték a MODELL BEMENETE lett. Egy GPU-vezérelt mesh ugyanezt kérné (a
displacementhez eleváció kell), tehát ugyanabba a falba futna: az I1 (bitre
azonos világ) és a HLSL lebegőpontos szabadsága kizárja egymást.

Ezért az ND-151 a 2–3. fázist **elveti, nem halasztja**, és kimondja, amit a
3. fázis valójában akart: a mesh-felbontás ALATTI felszíni részletet. Azt a
4. fázis megadja — per-pixel árnyalással, ahol a GPU-érték kizárólag KIMENET.

## Mi készült el

| Réteg | Fájl |
|---|---|
| Core | `src/WorldGen.Core/Terrain/SurfaceMicroDetail.cs` (+ `RandomProperty.MicroDetailPhase = 43`) |
| Viewer, motorfüggetlen | `Assets/Scripts/Viewer/Lod/MicroDetailBand.cs` |
| Viewer, Unity | `Assets/Scripts/Viewer/PlanetGridMesh.MicroDetail.cs` |
| Shader | `Assets/Shaders/VertexColorUnlit.shader` (mikro-részlet blokk, `#pragma target 3.5`) |
| Tesztek | `SurfaceMicroDetailTests` (20), `MicroDetailBandTests` (13) |

A létra két vége **származtatott**, nem szemre választott: alul a modell saját
másodlagos relief-zaja utáni következő oktáv (4,0744 ciklus/radián), felül a
32 bites float pontosságából adódó `2^17` (~49 m Föld-méreten). A sávváltás
folytonosságát átúsztatott oktáv-súlyok adják, és külön teszt bizonyítja.

## Két hiba, amit csak az élő menet mutatott meg

1. **„Homokpapír”.** Az első kép egyenletes, sűrű szemcsét adott — a
   legfinomabb oktáv pont a Nyquist-határra esett (3 pixel/hullámhossz).
   8 pixelre emelve a legfinomabb oktáv 8 px, az alap-oktáv 64 px: ez már
   felismerhető részlet-domborzat.
2. **Villogás a limbnél.** A sáv a legközelebbi felszínpont lábnyomából
   számol; a limb felé a felszín `1/cos`-szor akkora szöget fed egy pixelen.
   A `N·V`-halványítás ugyanabból a lábnyom-kritériumból következik.

## Mérés

| Mit | KI | BE |
|---|---|---|
| Helyi kontraszt (átlagos abs. Laplace), bolygó-nézet | 7,18 | 10,27 (**1,43×**) |
| Helyi kontraszt, közeli nézet (~1,15 R) | 3,20 | 18,46 (**5,76×**) |
| GPU-idő | 3,66–5,79 ms | 3,09–3,16 ms (nincs mérhető különbség) |
| Memória | — | nulla |

Képek (nincsenek verziókövetve): `pics/nd151/nd151-b-{off,on}.png` (bolygó-nézet,
terminátor) és `pics/nd151/nd151-c-{off,on}.png` (közeli).

## Determinizmus

`worldgen hash --seed A7C944210000 --plates 20 --level 6` mindhárom időpontban
bitre az ND-150 óta dokumentált érték (`t=0` `2b98af9a…6213738b`, `t=400`
`14dc8ad2…59a7ea07`, `t=3000` `823e8ba0…cdcfe7ab`). `WorldGeneratorVersion`
marad `"5"`. Teljes teszt: **1761/1761 zöld**; Unity `compilationFailed:false`,
0 konzol-hiba; shader `msgCount=0`.

## Módszertani tanulság a következő körre

A Unity Editor **Play módban nem tölti újra a lefordított assembly-ket**, és
fókusz nélkül nem is rajzol új képkockát. Az első A/B-párom emiatt két BITRE
AZONOS PNG lett (ugyanaz a befagyott képkocka), ami könnyen „nincs hatása”
téves diagnózisra vezetett volna. Amit érdemes megjegyezni: `Time.frameCount`
két hívás között — ha nem nő, `Application.runInBackground = true`; és
szkript-változás után **Play ki/be**, különben a régi kódot mérjük. A mozgó
Nap miatt a képpárokat SunController kikapcsolva kell készíteni, különben a
terminátor elmozdulása belekeveredik a különbségbe.

## Nyitva marad

- **B16** (új tétel): a látvány felhasználói elfogadása — az erősség (1,0)
  élőben hangolható (`microDetailStrength`), az A/B kapcsoló `surfaceMicroDetail`.
- **A16** (változatlan): volumetrikus felhő + AO, színkalibráció.
