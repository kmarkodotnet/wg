# Dynamic Planet World Generator

Determinisztikus bolygó-világgenerátor. Egyetlen seedből teljes, fizikailag
összefüggő bolygót állít elő — lemeztektonika, domborzat, klíma, hidrológia,
jég, talaj, erőforrások —, deep-time-ban 0-tól 1 milliárd évig.

A kimenet kettős: **adatmodell** egy későbbi evolúciós szimulációnak, és
**renderelt kép + adatpanelek** három nézetszinten (bolygó / kontinens / régió).

## Indulás

```bash
dotnet build
dotnet test

# A referencia-orákulum külön ellenőrzése
cd tools/reference && python verify_kat.py
```

Claude Code-dal: olvasd el a [KICKOFF.md](KICKOFF.md)-t.

## Struktúra

```
CLAUDE.md          Működési szabályzat — Claude Code ezt olvassa be
KICKOFF.md         Indító promptok
docs/
  00-spec-v1.0.md            Eredeti specifikáció
  01-architecture.md         Architektúra, modultérkép, panel-leképezés
  02-fidelity-strategy.md    Vizuális minőség: klasszikus vs neurális
  03-unity-hdrp-evaluation.md  Stack-elemzés (ND-01)
  04-decisions.md            Döntési nyilvántartás
  05-milestones.md           Milestone-terv + M2 részletes terv
src/WorldGen.Core/           A determinisztikus mag (motorfüggetlen)
tests/                       xUnit tesztek + tesztvektorok
tools/reference/             Python orákulum a verifikációhoz
```

## Állapot

| Milestone | Státusz |
|---|---|
| M0 — Repo, CI | ✅ (ND-01 lezárva: Unity 6 + HDRP) |
| **M1 — Determinisztikus random** | ✅ `dotnet test` zöld, Python referenciával verifikálva |
| **M2 — Grid** | ✅ `TileId`, Morton, koordináta-konverzió, szomszédság — mind tesztelve |
| M2 — Render | Folyamatban, `unity-viewer` ágon |

## A négy invariáns

1. **Determinizmus** — ugyanaz a seed + verzió → bitre ugyanaz a világ, minden
   platformon, minden szálszámon, minden kiértékelési sorrendben.
2. **A random réteg tiszta függvényekből áll** — nincs állapot, nincs
   sorrendfüggés.
3. **A képen minden pixel a világmodellből következik** — nincs dekoratív elem.
4. **A panelen minden szám a világmodellből olvasható ki** — nincs placeholder.

Részletek: [CLAUDE.md](CLAUDE.md).

## Licenc

Nincs meghatározva.
