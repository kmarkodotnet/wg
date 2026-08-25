---
description: Következő milestone-lépés (vagy megadott feature) implementálása a docs-first, referencia-előbb munkarend szerint
---
Implementáld: $ARGUMENTS (alapértelmezés: a `docs/05-milestones.md` szerinti
következő nyitott milestone-lépés).

Kövesd a CLAUDE.md "Munkamódszer" szakaszát:
1. Ha a lépés architekturális kérdést vet fel (nyitott ND vagy új ND-jelölt):
   indítsd az `architect` agentet — várd meg, mielőtt implementáció kezdődik.
   Nyitott, blokkoló ND (pl. ND-01) esetén állj meg és kérdezz.
2. Ha a lépés numerikus algoritmust igényel: indítsd a `reference-dev` agentet
   a `tools/reference/` alatt — Python implementáció, KAT-verifikáció (ha van
   hivatalos forrás), tesztvektor-generálás. NE ugorj ez fölött C#-ba.
3. `core-dev` agent: C# implementáció a tesztvektorokhoz mérve,
   `src/WorldGen.Core` alatt, a `.claude/skills/csharp-core/SKILL.md` szerint.
4. `code-reviewer` agent a diffre — REJECT esetén vissza a 3. ponthoz.
5. Ha CI-t érint (új job, mátrix-változás): `ci-engineer` agent.
6. `doc-writer` agent: README/CLAUDE.md "Állapot" és `docs/05-milestones.md`
   frissítése, ha egy "Kész, ha" kritérium teljesült.

Minden milestone-lépés után commit. Ha új nyitott architekturális kérdés
merült fel útközben, ellenőrizd, hogy bekerült-e `docs/04-decisions.md`-be
ND-számmal — csendes döntés nem elfogadható.

A végén rövid összefoglaló: mi készült el, milyen tesztek futottak le (C# +
Python), maradt-e nyitott ND.
