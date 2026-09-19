# Claude-specifikus állományok

Ez a fájl azokat a nem-kód állományokat sorolja fel, amelyek kifejezetten a
**Claude Code**-hoz tartoznak — vagy azért, mert Claude Code natívan beolvassa
őket (agent/skill/command-definíciók, beállítások), vagy azért, mert a
tartalmuk kifejezetten Claude-visszavételre (session-indításra) készült. Ezek
nem Codex-eszközök, és Codex nem tölti be automatikusan őket — ha Codex
szerkeszti a repót, ezeket neki külön el kell olvasnia ahhoz, hogy tudja, mit
vár tőle Claude Code oldala, vagy egyáltalán nem kell foglalkoznia velük.

A közösen (mindkét ágenssel) kezelendő állományok listáját lásd:
[common-ai-files.md](common-ai-files.md) — ez a fájl annak egy szűkebb,
Claude-specifikus alhalmaza.

## 1. Claude Code natívan beolvasott konfigurációja

- `.claude/agents/*.md` — subagent-definíciók (architect, ci-engineer,
  code-reviewer, core-dev, doc-writer, factory-engineer, reference-dev,
  triage)
- `.claude/commands/*.md` — slash parancsok (fix-bug, milestone, retro,
  review, status)
- `.claude/skills/*/SKILL.md` — skill-definíciók (csharp-core,
  determinism-ci, python-reference)
- `.claude/settings.json`, `.claude/settings.local.json` — engedélyek,
  hook-ok, session-beállítások

## 2. Claude Code működési szabályzata

- `CLAUDE.md` — a projekt működési szabályzata. Claude Code minden session
  elején automatikusan beolvassa; ez a legfelsőbb szintű instrukció, felülír
  minden alapértelmezett viselkedést.

## 3. Claude session-indítás és -folytonosság

- `readmefirst.md` — mit olvasson fel Claude egy ÚJ session elején
  (kötelező/ajánlott/referencia doksik listája, prioritás szerint)
- `KICKOFF.md` — eredeti indító promptok Claude Code-hoz (történeti, de a
  "Tippek a további munkához" szakasza még releváns)
- `kt_2_co2cl.md` — Codex → Claude tudásátadás (aktuális kiindulópont
  Claude-visszavételkor; lásd `readmefirst.md` §1)

## 4. Ami NEM ide tartozik

- `AGENTS.md` és `kt_1_cl2co.md` (Claude → Codex tudásátadás) — ezek
  Codex-oldali, nem Claude-specifikusak, bár tartalmilag rokonok.
- `docs/`, `history/` alatti anyagok — projekt-szintű, mindkét ágensnek
  szól, lásd [common-ai-files.md](common-ai-files.md).
