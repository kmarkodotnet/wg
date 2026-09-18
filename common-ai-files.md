# Közös AI-állományok

Ez a fájl azokat a nem-kód állományokat sorolja fel, amelyeket **mindkét
ágensnek** (Codex és Claude) konzisztensen kell kezelnie, mivel felváltva
dolgoznak ugyanezen a repón. Ide tartozik minden leíró dokumentáció és minden
olyan fájl, ami közvetlenül befolyásolja egy AI-ágens működését (instrukciók,
szabályzatok, tudásátadások, döntési nyilvántartás, állapotjelentések).

**Alapszabály:** ha bármelyik ágens módosít egy alábbi fájlt, a másiknak a
következő session elején el kell olvasnia — ezek nem lokális munkafájlok,
hanem megosztott állapot. Ütközés esetén a frissebb dátumú/commitú verzió
nyer, de a tartalmi ellentmondást fel kell oldani, nem felülírni csendben.

## 1. AI-instrukciós és konfigurációs fájlok

Ezek közvetlenül szabályozzák, hogyan viselkedik egy ágens ebben a repóban.
Mindkét ágens ugyanazokat a szabályokat kell hogy kövesse — eltérés esetén a
két ágens inkonzisztens módon dolgozna ugyanazon a kódon.

- `CLAUDE.md` — Claude Code működési szabályzata (invariánsok, munkamódszer,
  repo-struktúra, tesztelési elvárások)
- `AGENTS.md` — Codex működési szabályzata (ugyanaz a szerep, más ágensnek)
- `.claude/agents/*.md` — subagent-definíciók (architect, ci-engineer,
  code-reviewer, core-dev, doc-writer, factory-engineer, reference-dev, triage)
- `.claude/commands/*.md` — slash parancsok (fix-bug, milestone, retro,
  review, status)
- `.claude/skills/*/SKILL.md` — skill-definíciók (csharp-core, determinism-ci,
  python-reference)
- `.claude/settings.json`, `.claude/settings.local.json` — engedélyek,
  hook-ok

## 2. Tudásátadás és session-folytonosság

Ezek biztosítják, hogy amikor az egyik ágens átadja a munkát a másiknak, a
kontextus ne vesszen el. **Kiemelten fontos**, hogy váltáskor a legfrissebb
ilyen fájlt mindkét fél elolvassa, mielőtt módosít bármit.

- `kt_1_cl2co.md` — Claude → Codex tudásátadás (történeti, eredetileg
  `docs/07-handover-2026-09-10.md`)
- `kt_2_co2cl.md` — Codex → Claude tudásátadás (aktuális kiindulópont)
- `docs/reviews/codex-handoff-2026-09-10.md`
- `history/2026-09-12-codex-to-claude-handover.md`
- `history/README.md` — a history/ mappa szerepének leírása
- `history/RETROSPECTIVE-2026-09-02-adaptive-lod-saga.md`
- `history/*.md` — session-naplók (kronologikus munkajegyzőkönyv, minden
  jelentősebb munka után bővítendő; lásd a `history/README.md`-t a
  konvencióért)

## 3. Specifikáció, architektúra, döntési nyilvántartás

Ezek a repó "forrásigazsága" a tervezésről. Egy ágens sem hozhat csendben
olyan architekturális döntést, ami ellentmond ezeknek — új nyitott kérdést
`docs/04-decisions.md`-be kell felvenni, nem a kódban eldönteni.

- `docs/00-spec-v1.0.md` — eredeti specifikáció
- `docs/01-architecture.md` — architektúra, modultérkép, panel-leképezés
- `docs/02-fidelity-strategy.md` — vizuális minőség stratégia
- `docs/03-unity-hdrp-evaluation.md` — stack-elemzés (ND-01)
- `docs/04-decisions.md` — **ND-döntési nyilvántartás** — minden nyitott
  architekturális kérdés ide kerül, opciókkal és javaslattal
- `docs/05-milestones.md` — milestone-terv (M0–M13), státusztáblázat
- `docs/06-user-verification-checklist.md` — felhasználói ellenőrzési lista
- `docs/08-development-idea-2-non-earthlike-planets.md`
- `docs/09-app-shell-architecture.md`
- `docs/backlog.md` — nyitott feladatok, ötletek
- `docs/base_models/*.md` (README, 01-planet-orbit-insolation,
  02-tectonics-terrain-deep-time)
- `docs/app_base_features/*.md` (roadmap, roadmap-revised,
  application_base_features, release-qa-checklist)
- `docs/templates/requirements-template.md`
- `docs/reviews/*.md` — code review jegyzőkönyvek (ND-számozott témák
  szerint; a `codex-handoff-2026-09-10.md` az 2. pontban is szerepel)

## 4. Projekt-szintű meta dokumentáció

- `README.md` — belépési pont, struktúra-áttekintés, állapot
- `KICKOFF.md` — indító promptok Claude Code-hoz
- `readmefirst.md`
- `VERSION` — aktuális verziószám (seed-kompatibilitáshoz kötött, lásd
  CLAUDE.md "Verziózás és seed-kompatibilitás")

## 5. Alprojekt-dokumentáció

- `unity/README.md`
- `unity/WorldGenViewer/Assets/Scripts/Viewer/README.md`
- `tools/release/README.md`
- `tools/release/THIRD-PARTY-NOTICES.md`

---

**Mi NEM tartozik ide:** tiszta kódfájlok (`.cs`, `.py`), build-konfigurációk
amik nem AI-instrukciók (`.editorconfig`, `.gitattributes`, `.gitignore`,
`.asmdef`, `.csproj`, `.sln`), Unity-motor generált/harmadik féltől származó
állományok (TextMesh Pro fontok, shaderek, attribúciós szövegek), és a
`.vs/`, `artifacts/`, `bin/`, `obj/` alatti build-kimenetek.
