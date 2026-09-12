# Deep-time léptetőgombok (2026-09-11)

## Kérés

A Game view deep-time csúszkája és Gyr számmezője közé kerüljenek kis
léptetőgombok. Az első változat `1y`, `100y`, `10ky`, `1my`, `100my`
lépései a felhasználói ellenőrzés alapján kiegészülnek a köztes `10y`, `1ky`
és `10my` lépésekkel, mind negatív, mind pozitív irányban.

## Megvalósítás

- A panel a felhasználói visszajelzés alapján 340-ről 600 px-re szélesedett.
  Mind a nyolc pozitív lépés a felső sorban, mind a nyolc negatív lépés az
  alsó sorban jelenik meg; a `-100my`/`+100my` feliratok sem csonkolódnak.
  Scene- vagy prefab-módosítás nem kellett.
- A lépések a `deepTimeMyr` belső Myr egységében pontos `double` értékek.
- A teljes sor: `1y`, `10y`, `100y`, `1ky`, `10ky`, `1my`, `10my`, `100my`;
  Myr-ben rendre `0.000001`, `0.00001`, `0.0001`, `0.001`, `0.01`, `1`, `10`,
  `100`.
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

## Köztes lépések kiegészítésének ellenőrzése

- `git diff --check`: PASS.
- Unity C# build: PASS, 0 error, 83 meglévő warning. Az első kísérletet az
  egyidejű ND-83 háttérmunka átmeneti, még nem importált partial fájlja
  blokkolta; a Unity projekt frissülése után a teljes fordítás sikerült.
- Élő Game View ellenőrzés még szükséges a 600 px széles panel, a felső
  pozitív és az alsó negatív gombsor vizuális jóváhagyásához.
