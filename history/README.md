# History — munkamenet-napló

Cél: ha egy jövőbeli Claude Code munkamenet elveszti a kontextust (compaction,
session-restore hiba, párhuzamos ágak összemosódása — ahogy 2026-08-31-én
történt), ebből a mappából gyorsan rekonstruálható, **mi valósult meg és
miért**, a puszta git-loggal szemben, ami csak a *mit*-et mutatja, az *miért*-et
nem.

## Formátum

Egy fájl = egy összefüggő munkamenet (nem feltétlenül egy naptári nap — egy
nap alatt több párhuzamos munkamenet/ág is futhat, ld. `2026-08-30-reconstructed.md`).

Minden bejegyzés:
```
## [sorszám/idő] — rövid téma
**Felhasználói kérés:** (közel szó szerint)
**Történt:** mit döntöttem/csináltam, miért, mi lett az eredmény
**Eredmény:** commit hash(ek), teszt-szám, státusz
```

`-reconstructed` jelölésű fájlok: NEM egy éles beszélgetésből származnak,
hanem utólag, git-előzményből (commit üzenetek, diff-ek, `docs/04-decisions.md`)
lettek összeállítva, mert az eredeti beszélgetés-kontextus elveszett. Ezek
kevésbé megbízhatóak a "miért" kérdésre, mint a valódi napló-bejegyzések —
jelezd ezt, ha ezekre hivatkozol.

## Ismert probléma, ami miatt ez a mappa létrejött

2026-08-31: a felhasználó észlelte, hogy egy visszatöltött (compactált)
munkamenet jelentős, korábban ténylegesen megtörtént munkát (M9 adaptív LOD
teljes C# implementációja, ND-41–45 öt új Python-referencia, GPU-calc
párhuzamosítás, GPU compute pipeline kezdeménye) egyáltalán nem tükrözött —
az asszisztens úgy viselkedett, mintha ezek nem is léteznének, és részben
ütköző ND-számozást hozott létre (saját "ND-41" a már foglalt ND-41 mellé).
A tanulság: **git ground truth mindig elsőbbséget élvez a beszélgetés-
emlékezettel szemben**, és ez a napló a jövőbeli helyreállítást gyorsítja.
