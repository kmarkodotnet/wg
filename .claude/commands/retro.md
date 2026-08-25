---
description: Önfejlesztési kör a factory-engineer agenttel a .claude/ konfigurációra
---
Futtass önfejlesztési kört ($ARGUMENTS fókusszal, ha van):
1. Indítsd a `factory-engineer` agentet: elemzés a `docs/reviews/`,
   `docs/bugs/` és korábbi `docs/retros/` alapján → RETRO-fájl → max 3
   módosítás a `factory/<datum>-<tema>` ágon.
2. `code-reviewer` agent a diffre; APPROVE → merge main-be, REJECT → ok a
   retróba.
3. Zárás: 5 soros összefoglaló — mi változott a `.claude/` konfigban és mit
   mérünk legközelebb.
