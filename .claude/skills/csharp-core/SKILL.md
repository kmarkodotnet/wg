---
name: csharp-core
description: C#/.NET determinisztikus-mag fejlesztési szabványok. Mindig használd, ha a src/WorldGen.Core alatt írsz, módosítasz vagy reviewolsz kódot.
---
# C# Core szabványok (`src/WorldGen.Core`)

## Motorfüggetlenség (nem tárgyalható, amíg ND-01 nyitott)
- Target: **netstandard2.1**, nyelv: **C# 9** — nincs file-scoped namespace,
  nincs `record`, nincs `required`, nincs primary constructor
- Nulla hivatkozás motor-assemblyre (Unity/Godot/stb.)
- Nincs `System.Text.Json` a `src/`-ben (tesztekben szabad)

## Determinizmus (CLAUDE.md I1-I2)
- Minden fizikai mennyiség `double`, soha `float`
- Tilos: `System.Random`, `Guid.NewGuid`, `DateTime.Now`, `Environment.TickCount`
- `+ - * /` és `Math.Sqrt` bitpontos IEEE-754 — szabadon használható
- `Math.Log/Exp/Sin/Cos/Pow` NEM garantáltan bitpontos platformok között —
  csak akkor a kritikus úton, ha az adott ND-döntés ezt explicit engedi
  (ld. ND-23b)
- A random réteg (`DeterministicRandom` és rá épülő kód) kizárólag tiszta
  függvényekből áll: nincs mező, nincs állapot, nincs inicializálási
  sorrend-függés

## Projektszerkezet
`src/WorldGen.Core/<Modul>/` — domainenként (pl. `Random/`, `Grid/`)
`tests/WorldGen.Core.Tests/` — xUnit, `testdata/testvectors.json` a Python
referenciából generálva

## Tesztek (CLAUDE.md tesztelési táblázat — mind kötelező új modulnál)
| Teszt | Mit fog meg |
|---|---|
| Ismert-válasz (KAT), ha van külső referencia | Algoritmus-hiba |
| Tisztaság: ismételt hívás azonos | Rejtett állapot |
| Párhuzamos vs. szekvenciális egyezés | Sorrendfüggés |
| Minden paraméter érdemben hat a kimenetre | Kimaradt paraméter |
| Eloszlás/plauzibilitás | Statisztikai hiba |
| Éles esetek: 0, MaxValue, negatív, üres tartomány | Túlcsordulás, határhiba |

## Kész-definíció
`dotnet build` és `dotnet test` zöld **mind a négy CI platformon** (Linux
x64/ARM64, Windows, macOS) és mindkét konfigurációban (Debug/Release). A C#
eredmény egyezik a Python referenciával a megosztott tesztvektorokon.
