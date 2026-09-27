# A11 / ND-153 — Pálya menti (éves) kameramód

**Dátum:** 2026-09-27
**Ág:** `a19-plate-frame-noise`
**Kiváltó:** „todo2.md-ből olvasd fel A11-et és implementáld”

## Mit kért a feladat

Az ND-62 (2026-09-10) a kameramód-kapcsolót és a tengelyforgásos módot
lezárta, a harmadik enum-értéket (`CameraViewMode.OrbitalFollow`) viszont
szándékosan **no-opként** hagyta — a tooltipje maga vallotta be, hogy
„egyelőre Szabad kameraként viselkedik". A backlog három nyitott kérdést
kötött hozzá: befagyasztjuk-e a kameracélpontot, fusson-e közben a napi
ciklus, és hogyan kalibráljuk a nem valós lépték miatt (ND-19).

## A döntés (ND-153)

A mód célja az **éves** jel: a terminátor észak–déli vándorlása, a sarki
nappal/éjszaka megjelenése, a Nap-korong körbefordulása. Mindhárom csak a
pálya-szögtől és a tengelydőléstől függ, a napi forgástól nem.

1. **Lépték (ND-19):** nem merül fel. A bolygó a világ origójában áll, a
   kamera körülötte orbitál, a Nap-korong fix sugarú körön mozog — vagyis a
   bolygóval együtt mozgó, nem forgó (inerciális) **pálya-keretben**
   rendereljük. Kör pálya mellett (ND-26) a csillag-távolság konstans, tehát
   ebben a keretben a látvány exakt, valós léptékű pálya-koordináta nélkül.
   Az ND-19/A12 geometria-eltolása ettől a módtól függetlenül marad az A11
   közeli zoomjának a kérdése.
2. **Kameracélpont:** nincs mit befagyasztani — a `PlanetOrbitCamera` sosem
   olvassa a `target.rotation`-t. A mód a VILÁGOT teszi inerciálissá, az
   egér végig a felhasználónál marad. (A Naphoz rögzített kamera pont azt
   tüntetné el, amit meg akarunk mutatni.)
3. **Napi ciklus:** NEM fut. 60 s/év mellett 60 fps-en ~36°/képkocka a spin —
   ez alias-ol, nem „gyors". A mesh a módba lépés spin-szögén áll meg (nincs
   ugrás váltáskor), a dőlés viszont él. A **modellidő továbbfut**: csak a
   kép spin-fázisa konstans. Emiatt a panel kizárólag a szub-napponti
   SZÉLESSÉGET írja ki (az spin-független, tehát igaz — I4), a hosszúságot
   nem. Kikapcsolható: `orbitalFollowFreezeSpin`.

## Mi készült

| Fájl | Mi |
|---|---|
| `docs/04-decisions.md` | ND-153 (a fenti levezetés, élő ellenőrzési kritériummal) |
| `Assets/Scripts/Viewer/OrbitalFollowMath.cs` | ÚJ, motorfüggetlen: éves időlépték, hitch-korlát, év-arány, spin-szög |
| `Assets/Scripts/Viewer/SunController.cs` | a pálya-keretes ág `OrbitalFollow`-ra is fut, befagyasztott spinnel; éves időléptetés; `YearFraction`/`DayOfYear`/`SubsolarLatitudeDegrees` |
| `Assets/Scripts/Viewer/PlanetGridMesh.cs` | harmadik váltógomb saját sorban + pálya-állás kijelzés; enum-doksi és tooltip átírva |
| `tests/.../OrbitalFollowMathTests.cs` | 24 új teszt (összesen 30 assert-csoport a fájlban) |

A pálya-keretes renderelés ága **nem íródott újra**: az ND-62 ott már élesben
ellenőrzött dőlés-/spin-előjele hasznosul újra, a két mód kizárólag a
felhasznált spin-szögben tér el.

## Verifikáció

Offline kapuk:

```
dotnet test tests/WorldGen.Viewer.LodChunking.Tests   # 617 zöld (ebből 30 az új fájlból)
dotnet build tests/WorldGen.Viewer.Compile            # 0 error
python tools/ci/check_burst_strict.py                 # OK, 303 fájl
```

Élő Unity (6000.0.77f1, `PlanetView`, Play mód). Az új `.cs`-t
`AssetDatabase.Refresh(ForceUpdate)` importálta (a `.meta` commitolva — ld.
CLAUDE.md figyelmeztetése), a recompile hibátlan, a konzolon 0 futásidejű
hiba (a meglévő CS8632/CS0618 figyelmeztetéseken kívül semmi új).

Mért bizonyíték `OrbitalFollow` módban:

| Mérés | Érték |
|---|---|
| időlépték | 283,57 → 393,77 nap ~18 s alatt = **6,09 nap/s** (= 365,25/60 ✔) |
| szub-napponti szélesség | −23,10° → +10,80° → **+23,434°** (napforduló, dőlés 23,44 ✔) → +11,41° |
| Planet-rotáció | `(0.174650, -0.499988, -0.103725, 0.841873)` **bitre azonos** két, ~60 modellnappal eltérő mintavételnél ✔ (spin befagyva) |
| csillagmező | világtéri identitás ✔ |
| visszaváltás `Free`-re | a Planet-rotáció azonnal identitás ✔ (nincs regresszió) |

Két Game-nézeti kép ugyanarról a nézőpontból (`pics/a11-orbital-1.png`,
`a11-orbital-2.png`): a partvonalak **pixelre ugyanott** vannak (a felszín
nem pörög), a megvilágítás viszont teljesen átrendeződött — pontosan ez a mód
célja.

## Amit tudatosan NEM oldottunk meg

- **A hőoverlay lemarad.** A pillanatnyi hőmodell (ND-104) képkockánként
  `thermalMaxTicksPerJob` tickre korlátozott; éves ütemben a cél-tick ennél
  gyorsabban nő. Szándékosan nem gyorsítottuk fel — az a hőmodell
  időléptékét hamisítaná meg. A két funkciót együtt nem érdemes használni.
- **A panel IMGUI-sora nincs képpel bizonyítva**: a `capture_game_view` és a
  `screenshot` út sem viszi bele az OnGUI-réteget. Hogy a sor nem dob
  kivételt, azt a percekig futó Play mód 0 futásidejű hibája mutatja; a
  vizuális átvétel a felhasználóé.
