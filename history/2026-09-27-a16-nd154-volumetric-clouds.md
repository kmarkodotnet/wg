# A16 — M13 térfogati felhő + AO (ND-154, ND-155, ND-156)

**Dátum:** 2026-09-27
**Ág:** `a19-plate-frame-noise`
**Tétel:** todo2 A16 — „M13 — volumetrikus felhő + AO, színkalibráció"

## Mi volt a kiindulás

Az A14 (ND-21) a HDRP volumetrikus felhőjét **elutasította** négy mért
blokkolóval, és ezzel rögzítette, hogy a Planet nézet felhői saját úton
maradnak. Ami addig létezett: az M6-os felhő-MVP, egyetlen **lapos quad-héj**,
a csapadék a vertex-alfában — a limbnél nincs vastagsága, nem árnyékol, és
egyetlen Lambert-tag világítja meg.

## Mi készült el

**Térfogati felhő (ND-154)** — saját, gömbi raymarch. A lefedettség, a felhő
alja és a vastagsága a MÁR KISZÁMÍTOTT csapadék- és eleváció-mezőből jön
(nulla új kiértékelés), egyetlen négycsatornás RGBA32 kocka-atlaszban
(396×66 = 104 KB), a hő-overlay (ND-104) már tesztelt atlasz-geometriájával.
A felhőalap a Lawrence-féle LCL-ből SZÁRMAZIK és a talajjal együtt emelkedik,
ezért a takaró ráborul a hegyláncra. A shaderben csak a függőleges profil
ALAKJA és a lefedettség-mező felbontása alatti részlet van — a számok
uniformként a Core-ból jönnek.

**AO (ND-155)** — és itt a mérés fordította meg a tervet. Az új
`SurfaceSkyOpenness` (zárt alakú, tisztán racionális `cos²α` sky view factor)
a valódi világon mérve level 6-on 0,999999, level 10-en 0,999992 szárazföldi
átlagot ad: **makro-léptéken okkludáló domborzat nem létezik**, mert a modell
relief-létrája ~1564 km-nél véget ér. Az AO ezért oda került, ahol meredek
relief ténylegesen van: az ND-151 per-pixel mikro-részletébe. A planetáris
léptékű valódi okkluder a FELHŐ — a terep-shader ugyanabból az atlaszból
árnyékol.

**Színkalibráció (ND-156)** — a felhő saját kalibrációja elkészült (fizikai
kioltás, HG-aszimmetria, többszörös szórás, borult diffúz padló); a
terep-paletta újrafokozása tudatosan a B17 (felhasználói vizuális átvétel)
utánra halasztva, dokumentált eljárással. Ok: a jelenlegi paletta élőben,
szemre hangolt lineáris érték, és a felhő megváltoztatja a referenciát.

## Hat hiba, amit a munka közben a MÉRÉS talált

1. **A pass nem rajzolódott.** `Cull Front` + `ZTest Always` → a megmaradó
   hátsó héjlapokat a bolygó mélysége eldobta. A beépített diagnosztika
   1-es módja (tömör kitöltés) üres képet adott → `Cull Off` + `ZTest LEqual`
   + geometriai lapválasztás.
2. **A menet átlépett a felhő fölött.** A héj 24 km, a dekk 500–2000 m; 12
   lépés nem mintázta meg → a menetet a helyi aljra/tetőre szorítjuk.
3. **Szögletes felhőárnyék-foltok.** A level 5-ös forrásmező nearest átvétele
   → bilineáris felskálázás (`UpsampleTable`), laphatárokkal együtt.
4. **Egyenletes szürke fátyol.** A multiplikatív részlet fizikailag rossz →
   sub-grid küszöbözés, zárt alakban levezetett, cella-átlag-tartó küszöbbel.
5. **Sötét szürke felhő.** Egyszeres szórással az optikailag vastag felhő
   definíció szerint fekete → oktávos többszörös-szórás közelítés.
6. **Az éjszakai oldal felhői is világítottak.** A Nap irányú optikai mélység
   nem tudja, hogy a Nap a horizont alatt van-e → nappali tényező, ami a
   szórt (égbolt-) tagot is kapuzza, mert az is szórt napfény. Az éjszakai
   felhő így sziluettként takar.

Plusz egy robusztussági hiba: Play közbeni újrafordítás után a réteg némán
eltűnt (a domain-reload a nem szerializált mezőket a típus alapértékére
állítja, tehát a `-1`-es őrszem elveszett).

## Mérés (élő Play, 1600×900, befagyasztott Nap)

| Mit | Felhő KI | Felhő BE (review előtt) | Felhő BE (végleges) |
|---|---|---|---|
| Átlagos luminancia a korongon | 75,55 | 80,93 | 96,89 |
| Fényes (L > 140) pixelek | 14,01% | 16,22% | 25,13% |
| Eltérő pixelek a teljes képen | — | 10,57% | — |
| GPU-idő (16 minta átlaga) | 2,46 ms | 2,67 ms | — |

A harmadik oszlop a code review utáni állapot. A naiv zaj-leképezés a
modellezett felhő kb. négyötödét elnyomta — a korábbi, „szebb" kép nem jobb
volt, hanem hiányos.

Az égbolt-nyitottság mérése négy szinten (6/8/9/10) a `SurfaceSkyOpennessTests`
csapdazsinór-tesztjében rögzítve.

## Code review

Egy `/code-review high` kör 11 megállapítást hozott, mind megalapozott volt.
Kilenc javítva (keret-eltérés a felhőárnyékban; a nyitottság-csatorna
számolva de nem olvasva ÉS sodródásonként újraszámolva; a sub-grid
mean-preservation egyenletes zajt feltételez, a shader viszont normálisat
adott; a menet ablaka egyetlen minta a húr közepén; a héj-anyag elvesztése
domain-reload után; mesh-szivárgás; hiányzó életciklus-takarítás; uniform-
kiadás rossz frissítési fázisban; holt státusz-property; félrevezető doksi a
transzcendensekről). Kettő dokumentált korlát maradt: a mélységtextúra
hiánya és az árnyék/felhő felbontás-eltérése. Részletek: ND-154 táblázat.

## Ellenőrzés

- `dotnet test WorldGen.sln` → **1901/1901 zöld** (Core 769, Viewer 656,
  App 452, CLI 24); +68 új teszt.
- `dotnet build tests/WorldGen.Viewer.Compile` és
  `tests/WorldGen.App.UnityBinding.Compile` → 0 hiba.
- `python tools/ci/check_burst_strict.py` → OK (314 fájl).
- **Nem seed-törő:** `worldgen hash --seed A7C944210000 --plates 20 --level 6`
  mindhárom időpontban bitre a dokumentált érték; `WorldGeneratorVersion`
  marad `"5"`.
- Élő Unity: `compilationFailed: false`, 0 konzol-hiba, mindhárom shader
  `msgCount = 0`.
- Képek: `artifacts/a16/` (A/B be-ki, kontinens-nézet, a fejlesztés közbeni
  állapotok).

## Ami nyitva maradt

- **B17** — a vizuális átvétel (felhasználói ítélet a fényességről/sűrűségről).
- **Mélységtextúra** — űrből nézve egy a dekkbe emelkedő hegy nem takarja el a
  mögötte lévő felhőt (HDRP custom pass kellene hozzá).
- **A terep-paletta újrafokozása** (ND-156, todo2 B18), a B17 után.
