---
name: determinism-ci
description: GitHub Actions CI szabványok a determinisztikus mag számára - platform-mátrix, referencia-orákulum job. Használd ci.yml módosításakor vagy CI-hibáknál.
---
# CI szabványok (`ci.yml`)

## A mátrix miért van (nem csökkenthető indoklás nélkül)
A determinizmus (I1) megköveteli, hogy ugyanaz a seed **minden platformon és
architektúrán** bitre ugyanazt a világot adja. Ezért a `test` job mátrixa:
`ubuntu-latest, windows-latest, macos-latest, ubuntu-24.04-arm` ×
`Debug, Release`. **Egyetlen eltérés blokkoló, nem "flaky teszt."**

## Jobok
- `test`: `dotnet restore` → `dotnet build --no-restore` →
  `dotnet test --no-build`, mind a 8 kombinációban, `fail-fast: false`
- `reference-oracle`: Python 3.12, `verify_kat.py` (hivatalos KAT-vektorok),
  majd `gen_vectors.py` + `diff` a bemásolt `testdata/testvectors.json`
  ellen — eltérésnél a teljes tesztvektor-készlet érvénytelen, a job hibával
  áll le

## Új platform/job hozzáadásakor
- Indokold `docs/04-decisions.md`-ben, ha érinti a determinizmus-garanciát
- Tartsd meg a `fail-fast: false`-t — egy piros kombináció ne takarja el a
  többit

## Kész-definíció
A workflow YAML szintaktikailag helyes, és a lokálisan futtatott
`dotnet test` / `cd tools/reference && python verify_kat.py` ugyanazt az
eredményt adja, amit a CI adna.
