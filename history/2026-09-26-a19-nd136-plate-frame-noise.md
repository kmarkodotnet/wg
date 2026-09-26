# 2026-09-26 — A19 / ND-136: a domborzati zaj a lemez keretében

**Kérés:** „olvasd fel a todo2.md-t és oldd meg a A19 – a domborzati zaj nem
mozog a lemezzel feladatot".

## A hiba

Deep-time-ban csak a lemez-magok forogtak el (`PlateMotion.MovedSeeds`). A
domborzat három zajtagja — elsődleges ridged multifractal, `MountainMask`,
ND-52 másodlagos részletzaj — a **rögzített világ-pozícióból** számolódott
(`CrustElevation.ComputeNoiseBasis`), tehát a lemezhatár átcsúszott egy álló
textúra felett: a hegyvonulat helyben maradt, a lemez elvándorolt alóla. Ez
az ND-63-ban szándékként is rögzítve volt, és pontosan ez tette lehetővé az
ND-122/ND-131 pozíció-kulcsú lemez-gyorsítótárat.

## A megoldás — ND-136 (C) opció

A mintavételi pontot a lemez saját vonatkoztatási rendszerébe forgatjuk vissza
(`R(−ωt)` az Euler-pólus körül), és a zajt ott mintavételezzük.

| Új / módosult | Hol |
|---|---|
| `PlateMotion.ToPlateFrame` | visszaforgatás; `t = 0`-ra egzakt rövidzár |
| `CrustElevation.ComputeNoiseBasisInPlateFrame` | a három zajtag a lemez keretében |
| `CrustElevation.MountainMaskInPlateFrame` | csak a maszk (az uplift-út ND-35 szerint csak ezt kéri) |
| `CrustElevation.BlendedBaseElevationFromPlateFrameBases` | keverés **minden** határon, két külön zaj-bázisból |
| `PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime` / `…FromWarpedAtTime` | a két bázis összerakása |
| `TerrainPointBasis.EvaluateAtTime` | cache-elt warp + újraszámolt zaj |
| `DeepTimeErosionGlaciation.ElevationAtTime`, `SeaLevelCalibration`, `RiverPathTracing` | `timeMyr` átvezetve |
| viewer: `_adaptivePlateTimeMyr` | a **lemez-idő** külön a kopás-időtől |

### Három dolog, ami másképp jött ki, mint a döntés tervezete jósolta

1. **Nem duplázódik a zaj-költség.** A második lemez zaj-bázisa csak akkor
   kell, ha a pont a `0.005`-ös keverősávon belül van — azon kívül a függvény
   amúgy is a nyertes értékét adja. Mérve (level 7, 98 304 tile):
   `t = 0` 368 ms, `t = 300` 351 ms, `t = 1000` 325 ms — a futásonkénti
   szóráson belül, **nincs mérhető többletköltség**.
2. **A lemez-cache megmaradt.** A `TerrainPointBasis` warp-része tisztán
   pozíció-függő (a lemez-topológia a világ kerete, ND-36), tehát az ND-122/131
   cache `t > 0`-nál is érvényes rá — épp az a drága, három fBm-es tag. Nem
   kellett a cache-t `t = 0`-ra korlátozni.
3. **A hidrológia is kellett hozzá.** A `RiverPathTracing` folytonos
   nyomvonalkövetése saját, pontszerű elevációkiértékelést használ; enélkül
   `t > 0`-nál a folyó egy másik (világ-keretes) terepen futott volna, mint
   amit a felhasználó lát — ugyanaz a hibaosztály, amit ez a javítás felszámol.

### Egy szándékos viselkedésváltás az ND-90-ben

A keverésből kikerült a „csak eltérő kéregtípusnál" kikötés. Az korábban helyes
volt (azonos típusnál a két lemez ugyanabból a három számból számolt, tehát a
függvény amúgy is folytonos volt); lemez-keretes zajjal viszont **minden** határ
szakadásossá válna nélküle. `t = 0`-nál ez bitre semleges: ott a két bázis
azonos, tehát `best + (best − best) * w == best`.

## Bizonyíték

**Python orákulum** (`tools/reference/plate_frame_noise_ref.py`, a
CLAUDE.md „referencia előbb" rendje szerint; a visszaforgatás a
`deterministic_math_ref.sin_cos`-t használja, hogy a C# `DeterministicMath`-
szal bitre egyezzen):

```
1. t=0 bitre valtozatlan            288 minta, max elteres: 0.0 (egzakt)
2. a zaj egyutt utazik a lemezzel   215 minta, max elteres: 9.99e-15
   kontroll (regi, vilag-keretes)   ugyanitt 0.814-et ugrik
3. hatar-folytonossag               suly 0.5 a hataron, 0 a sav szelen
4. determinizmus                    ismetelt hivas bitre azonos
5. minden parameter hat             timeMyr es worldSeed is
400 lemez-keretes + 100 t=0 tesztvektor generalva
```

**C# tesztek** — `tests/WorldGen.Core.Tests/Tectonics/PlateFrameNoiseTests.cs`
(KAT a 400+100 vektorra, `NoiseTravelsWithThePlate` + ellenpélda, `t = 0`
bit-regresszió a RÉGI keverési szabály külön újraimplementálásával, tisztaság,
párhuzamos = szekvenciális, paraméter-érzékenység, keverés-folytonosság,
élesetek, eloszlás-plauzibilitás).

```
dotnet test WorldGen.sln
  WorldGen.Core.Tests            611/611
  WorldGen.Viewer.LodChunking    526/526
  WorldGen.App.Foundation        452/452
  WorldGen.Cli.Tests              24/24
dotnet build tests/WorldGen.Viewer.Compile             0 error
dotnet build tests/WorldGen.App.UnityBinding.Compile   0 error
Unity 6000.0.77f1 elo Editor recompile                 0 error (133 orokolt warning)
```

**Egy tovabbgyuruzo hiba, elkapva:** a `WorldGeneratorVersion` bumpra a
`ThermalCheckpoint` binaris formatuma is valtozik (a generatorverzio beleszamol
a checkpoint-hashbe — szandekosan). A `thermal_checkpoint_ref.py` a "2"-t
hardcode-olta; atirva "3"-ra, a vektorok ujragenerálva, es a fajlba kerult egy
figyelmezteto komment, hogy a kovetkezo verzioemelesnel is emelni kell.

**Mérési szonda** (`SeaLevelCalibration.ComputeElevationFieldAtTime`,
seed `0xA7C944210000`, 20 lemez):

```
level=7 tiles=98304  t=0: 368ms  t=300: 351ms  t=1000: 325ms
    tengerszint  t=0: 1622.3m  t=300: 1695.1m  t=1000: 1712.2m
    UJ  (lemez-keretes)  t=0 -> t=300: atlag |d| = 2454.6m, szarazfold/ocean valtas = 45.3%
    REGI (vilag-keretes) t=0 -> t=300: atlag |d| = 2226.3m, szarazfold/ocean valtas = 25.6%
```

A régi út is változtatott 25,6%-ot (a lemez-hozzárendelés, és vele a kéregtípus,
elmozdult) — de a textúra maga állt. Az új úton a teljes kéreg-textúra is
vándorol, innen a 45,3%.

## Verziózás

`WorldGeneratorVersion.Current` **2 → 3** (ND-108). A `t = 0` kimenet bitre
változatlan; csak a `t > 0` deep-time világ módosul. Az
`erosion_glaciation_deep_time_vectors.json` újragenerálva — mostantól a
ténylegesen elmozdított magokkal mér, nem a `t = 0` magokkal vett hibriddel.

## Ami nyitva maradt

Az ND-136 alkérdése: nagy `t`-nél (500–1000 Myr) a szomszédos keretek több tíz
fokkal is elfordulnak egymáshoz képest, tehát a keverősáv látható **nyírási
sáv** lesz. Ez fizikailag nem rossz (egy transzform határ valóban egymás mellé
tol nem összetartozó kérget), de a sáv szélességét kalibrálni kell — és ez az
**ND-138**-cal (határ-besorolás) együtt értelmes, mert ott dől el, melyik határ
transzform.

Vizuális megerősítés (Unity Play, deep-time csúszka) még nem történt meg.
