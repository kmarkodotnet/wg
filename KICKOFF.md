# Indító prompt Claude Code-hoz

Az alábbi blokkot másold be a Claude Code első üzenetének. A `CLAUDE.md`-t
automatikusan beolvassa, tehát az invariánsokat és a konvenciókat nem kell
megismételned — ez a prompt a konkrét feladatot adja meg.

---

```
Ezt a repót veszem át fejlesztésre. Olvasd el a CLAUDE.md-t, a
docs/04-decisions.md-t és a docs/05-milestones.md-t, mielőtt bármihez hozzáérsz.

Kontextus: az M1 (determinisztikus random réteg) kódja készen áll, de egy
olyan környezetben készült, ahol NEM volt .NET telepítve — tehát soha nem
fordult le és nem futott. Az algoritmus maga viszont verifikált: a
tools/reference/ alatti Python referencia a hivatalos Random123 KAT-vektorok
9/9-ével egyezik.

Első feladat — M1 lezárása:

1. Ellenőrizd, hogy a solution lefordul: `dotnet build`
2. Futtasd a teszteket: `dotnet test`
3. Ha bármi elbukik:
   - A ThreefryKatTests a legfontosabb. Ha ez piros, a C# port hibás,
     NEM a tesztvektorok. A tools/reference/threefry_ref.py az igazság —
     ahhoz igazítsd a C#-ot, ne fordítva.
   - Ellenőrizd, hogy a testdata/testvectors.json bemásolódik-e a build
     kimeneti könyvtárba.
   - Fordítási hibáknál tartsd meg a netstandard2.1 + C# 9 korlátot
     (lásd CLAUDE.md) — ne emeld a LangVersion-t a megoldás kedvéért.
4. Futtasd le a Python referencia-ellenőrzést is:
   `cd tools/reference && python verify_kat.py`
5. Ha minden zöld, jelezd, és foglald össze röviden, mi futott le.

Ezután NE kezdj bele az M2-be automatikusan — előbb beszéljük meg.

Két dolgot tarts szem előtt végig:

- Az ND-01 (technológiai stack: Unity 6 / Godot 4 / Rust) MÉG NYITOTT. A
  src/ maradjon motorfüggetlen: netstandard2.1, C# 9, nulla motor-referencia.
  Ne hozz olyan döntést, ami ezt előre eldönti.

- Ha algoritmus-konstansra, mágikus számra vagy tesztvektorra van szükség,
  verifikáld forrásból, ne emlékezetből. A projekt előéletében ez már kétszer
  okozott hibát. A tools/reference/kat_vectors pont ezért van a repóban.
```

---

## Miután az M1 zöld

A második session promptja körülbelül ez lehet:

```
Az M1 zöld. Kezdjük az M2-t a docs/05-milestones.md §M2 terve szerint.

Tartsd a bevált sorrendet:
  1. Python referencia a tools/reference/ alatt — cubed sphere vetítés,
     terület-eloszlás mérése (a max/min aránynak 1.4 alatt kell lennie)
  2. TileId struct + Morton kódolás/dekódolás + tesztek
  3. Koordináta-konverziók + round-trip tesztek
  4. Szomszédsági tábla + a §2.4-ben felsorolt tesztek
  5. Render csak ezután — és csak ha az ND-01 addigra eldőlt

Külön figyelj a §2.4-re: a kocka 8 sarkánál 3 tile találkozik, nem 4, és a
12 él mentén az u/v tengelyek átfordulnak. Ez a klasszikus hibaforrás.

A §2.2-ben van egy nyitott kérdés: az egyenszögű vetítés `tan`-t használ, ami
transzcendens függvény (ND-23b). Döntsd el, hogy ez konstrukciós vagy
szimulációs számításnak minősül-e, és vedd fel ND-24-ként a
docs/04-decisions.md-be. Ne oldd meg csendben.

Kezdd az 1. ponttal, és mutasd meg a terület-eloszlás mérését, mielőtt
továbbmennénk.
```

## Tippek a további munkához

**A dokumentáció karbantartása.** Ha új architekturális kérdés merül fel, kérd,
hogy vegye fel `docs/04-decisions.md`-be ND-számmal, opciókkal és javaslattal.
A `CLAUDE.md` külön kimondja, hogy csendes döntés nem elfogadható.

**Milestone-onként commit.** A CI négy platform-kombinációt futtat; egy piros
determinizmus-teszt blokkoló, nem flaky teszt.

**Ha az ND-01 eldől**, frissítsd a `docs/04-decisions.md`-t és a `CLAUDE.md`
"Állapot" szakaszát. Unity esetén az ND-19/20/21 azonnal aktívvá válik — a
Burst `FloatMode.Strict` kikényszerítése (ND-20) még M2 előtt kell.

**Ami nem Claude Code-ba való:** a vizuális megítélés. Amikor a render része
elindul, a "jól néz ki?" kérdésre neked kell válaszolnod — az eszköz nem látja
a kimenetet. Ezért van a rácsvalidáció tesztekkel megtámogatva, nem
szemrevételezéssel.
