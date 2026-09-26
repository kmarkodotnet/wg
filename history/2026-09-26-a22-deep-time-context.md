# A22 — `DeepTimeContext`: a deep-time hármas egy struct-ban

**Dátum:** 2026-09-26
**Ág:** `a19-plate-frame-noise`
**Kiindulás:** `todo2.md` → „A22 🟡 — Deep-time kontextus-struct" (felhasználói kérés)
**Döntés:** nincs új ND — az ND-137 nyitott adóssága, tisztán kozmetikai

---

## Mi volt a baj

Az ND-136 (A19) és az ND-137 (A20, két kör) után ugyanaz a három érték
utazott végig a teljes elevációs láncon:

```
(plateTimeMyr, erosionTimeMyr, staticSeaLevelMeters)
```

Érintett: `PlateBoundaryEffect.ElevationWithBoundaryFromWarpedAtTime`
(**16 paraméter**), `PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime`,
`CrustElevation.TerrainPointBasis.EvaluateAtTime`,
`DeepTimeErosionGlaciation.ElevationAtTime`,
`SeaLevelCalibration.ComputeElevationFieldAtTime`, a `RiverPathTracing`
nyolc metódusa (`ElevationCache` három mezőjével együtt), plusz a viewer
négy statikus segédfüggvénye és három `_adaptive…` mezője.

Az A22 azért nem az ND-137 második körében készült el, mert egy ilyen
szélességű átvezetés elrejtette volna a valódi funkcionális diffet.

## Amit csináltunk

`src/WorldGen.Core/Tectonics/DeepTimeContext.cs` — `readonly struct`,
netstandard2.1 / C# 9 konform:

| tag | jelentés |
|---|---|
| `PlateTimeMyr` | a lemezmozgás és a lemez-keretes zaj kora (ND-136) |
| `ErosionTimeMyr` | a kopás/bevágódás/uplift-relaxáció kora — SZÁNDÉKOSAN külön (a viewer „Erózió (kopás)" kapcsolója) |
| `StaticSeaLevelMeters` | a parti abrázió statikus (t = 0) referencia-szintje; NaN = kikapcsolva |

Gyárimetódusok: `Static`, `AtPlateTime(t)`, `Uniform(t)`,
`WithStaticSeaLevel(…)`, `WithErosionTime(…)`; kényelmi predikátum:
`IsStatic` (a `TerrainPointBasis` gyors, cache-elt útjának feltétele).

Az elevációs belépő **16 → 12 paraméter**. A viewer három mezője
(`_adaptiveErosionTimeMyr`, `_adaptivePlateTimeMyr`,
`_adaptiveStaticSeaLevel`) egyetlen `_adaptiveDeepTime`-ra olvadt — a három
érték innentől nem tud széttartani.

## A refaktor EGYETLEN valódi hibalehetősége — és ahogy kizártuk

A parti abrázió konvenciója `staticSeaLevelMeters = NaN` → kikapcsolva. Ha a
struct nyers `double` mezőt tárolna, a `default(DeepTimeContext)` **0,0 m-es
tengerszintet** adna, azaz CSENDBEN bekapcsolná az abráziót a 0 m-es szint
körül — pont az a hibaosztály, amit egy ilyen „kozmetikai" változásnál senki
nem keresne.

Ezért a tengerszint **property**, egy `_hasStaticSeaLevel` zászló mögött:
a nullérték NaN-ra képződik. A `DeepTimeContextTests.DefaultContextDisablesCoastalAbrasion`
ezt köti ki, a `FactoriesSetExactlyTheIntendedFields` pedig azt, hogy a
`WithErosionTime` a meglévő tengerszintet megtartja, de nem talál ki 0,0-t,
ha nem volt.

## Bizonyíték: BIT-SEMLEGES

Nem elég a tesztek zöldje — a hármas a világ-hash útján is átmegy, tehát a
világ szintjén mértük, a változás előtti HEAD-et külön git-worktree-ben
lefordítva (`git worktree add ../wg-a22-base HEAD`):

| `worldgen hash --seed 12345 --plates 20 --level 5` | HEAD (869808f) | A22 után |
|---|---|---|
| `--time 0` | `bf7ac2b2…de7252a7` | **azonos** |
| `--time 400` | `b35e3223…5aa50f12` | **azonos** |
| `--time 3000` | `d4526336…eca73a55` | **azonos** |

Ezért **nincs `WorldGeneratorVersion`-emelés** (CLAUDE.md: bitazonos
optimalizálás nem igényel generátorverzió-emelést).

Fordítási/teszt-kapuk:

- `dotnet build WorldGen.sln --no-incremental` → 0 hiba, 0 figyelmeztetés
- `dotnet test tests/WorldGen.Core.Tests` → **673 zöld** (a refaktor előtti
  szám), plusz az 5 új `DeepTimeContextTests`
- `dotnet build tests/WorldGen.Viewer.Compile` és
  `tests/WorldGen.App.UnityBinding.Compile` → zöld
- élő Unity Editor `recompile` → `compilationFailed: false`, konzol 0 error

## Amit majdnem kihagytunk

A `tools/diagnostics/RiverBaseline` **nincs a `WorldGen.sln`-ben**, tehát sem
a megoldás-build, sem a CI nem fogta volna meg, hogy a `timeMyr:` névvel
átadott argumentuma megszűnt. Külön lefordítva vezettük át. Ugyanez a
csapda az `artifacts/lod-zoom-probe`-nál nem ütött be, mert az a
csak-lemez-idős (`double`) túlterhelést hívja, ami megmaradt.

## Nyitva marad

Semmi az A22-ből. A következő deep-time tag (pl. a lemez klímatörténetének
integrálása, ld. ND-137 nyitott fele) mostantól egy struct-mező plusz a
kiértékelés, nem ~25 hívási hely átvezetése.
