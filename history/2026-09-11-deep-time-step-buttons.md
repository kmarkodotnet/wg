# Deep-time léptetőgombok (2026-09-11)

## Kérés

A Game view deep-time csúszkája és Gyr számmezője közé kerüljenek kis
léptetőgombok: `-1y`, `-100y`, `-10ky`, `-1my`, `-100my`, valamint ezek
pozitív párjai.

## Megvalósítás

- A két ötgombos sor a meglévő IMGUI panel része, ezért scene- vagy
  prefab-módosítás nem kellett.
- A lépések a `deepTimeMyr` belső Myr egységében pontos `double` értékek.
- A gombok egyszerre frissítik az autoritatív időt, az Inspector-csúszka
  tükrözött értékét és a Gyr szövegmezőt.
- Az eredmény a `0..onScreenDeepTimeMaxMyr` tartományra korlátozott.
- A Gyr mező legfeljebb kilenc tizedest mutat, így az egyéves lépés is
  látható (`0.000000001 Gyr`).

## Ellenőrzés

- `dotnet build WorldGen.sln`: PASS, 0 warning, 0 error.
- `dotnet test tests/WorldGen.Viewer.LodChunking.Tests/WorldGen.Viewer.LodChunking.Tests.csproj --no-build`:
  PASS, 205/205.
- `git diff --check`: PASS.
- Élő Unity Game view ellenőrzés még szükséges: a futó Editor naplója a
  fájlmódosítás után nem frissült, a jelen környezet pedig nem adott natív
  ablakhozzáférést.

