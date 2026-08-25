---
name: python-reference
description: Python referencia-orákulum szabványok a tools/reference/ alatt. Mindig használd új numerikus algoritmus verifikálásakor vagy tesztvektor-generáláskor.
---
# Python referencia-orákulum szabványok

## Miért létezik
A `CLAUDE.md` szerint: "A Python referencia az igazság." Ha a C# és a Python
eltér, a Python a helyes, hacsak nem bizonyítod az ellenkezőjét. Minden új
numerikus algoritmus itt kezdődik, nem C#-ban.

## Munkarend (kötelező sorrend)
1. Implementáció `tools/reference/`-ben
2. Verifikálás hivatalos, publikált tesztvektorokhoz (pl. Random123 KAT) —
   **soha ne idézz konstanst vagy vektort emlékezetből**, mindig forrásból
3. Tesztvektor-generálás a C# port számára (`gen_vectors.py` mintára,
   kimenet: `testvectors.json`)
4. C# implementáció a vektorokhoz mérve

## Elvárások
- `verify_kat.py`-szerű szkript minden algoritmushoz, amit a CI
  (`reference-oracle` job) is futtat
- A generált tesztvektorok determinisztikusak: kétszeri futtatás bitre azonos
  kimenetet ad
- A `tests/.../testdata/testvectors.json` mindig a referenciából generálódik,
  soha kézzel nem szerkesztett
- Ha nincs elérhető hivatalos referencia egy konstanshoz, jelezd explicit és
  kérj megerősítést — ne találj ki "valószínűnek tűnő" értéket

## Kész-definíció
A referencia-szkript önállóan lefut Python 3.12 alatt, a `verify_kat.py`
zöld, és a CI-ban a `diff testvectors.json ../../tests/.../testvectors.json`
nem mutat eltérést.
