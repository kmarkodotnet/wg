# Deep-time hiányzó lépték + "Kamera állása" panel (2026-09-13)

## Kérés

A [Planet Navigator mockup](../docs/backlog.md) jóváhagyása után a felhasználó
két konkrét javítást kért, amit azonnal implementálni is lehetett (nem
függenek a Core "Terület"-szint tervezésétől):

1. A Deep time léptetőgombokból hiányzott egy tizes-lépték a millió év
   környékén (10ky → 1my egy 100x-os ugrás volt, nem 10x-os).
2. Hiányzott egy "kamera állása" (nézetszint, magasság, nézetirány) olvasó
   panel, jobb felül.

## Megvalósítás

- `PlanetGridMesh.cs`: `DeepTimeStepLabels`/`DeepTimeStepMyr` kiegészítve a
  hiányzó `100ky` (0.1 Myr) lépéssel - a teljes, immár valódi tizes-lépéskű
  sor: `1y, 10y, 100y, 1ky, 10ky, 100ky, 1my, 10my, 100my` (9 gomb, korábban
  8). `DeepTimeButtonsPerRow` 8→9, a panel szélessége 600→675px (arányosan,
  hogy a gombfelirat ne csonkolódjon - ugyanaz a minta, mint a
  2026-09-11-i 340→600px bővítés).
- Új `DrawCameraStatePanel` - a Deep time doboz FÖLÉ kerül, ugyanabban a
  jobb felső oszlopban. Forrás: a MÁR MEGLÉVŐ `PlanetOrbitCamera.CurrentViewLevel`
  / `AltitudeAboveSurface` / `CurrentViewDirection` (I4 - nincs új, kitalált
  mező). A magasságot SZÁNDÉKOSAN "Unity egység"-ként címkézve, NEM
  km-ként - a Unity-egység → valós km átváltás önálló, nyitott feladat
  (ld. backlog "Precíz, kamerafüggő kilométeres léptékcsík" sor), itt nem
  szabad hamis pontosságú km-számot mutatni.
- `_hudOrbitCamera` mező + önfeloldás, ugyanaz a minta, mint a
  `WorldGenPanelUI.EnsureOrbitCameraReference` (hot-reload után is pótolja
  magát).

## Elmaradt/nyitva hagyott rész

A teljes navigációs menü (breadcrumb, kontinens/régió lista, vissza-gomb,
bal oldali dobozkonszolidáció) a mockupban látott formában **nincs
implementálva ebben a lépésben** - az függ a docs/backlog.md "Navigációs
menü és panel-elrendezés" szakaszban leírt "Terület" (negyedik szint)
docs-first Core-tervezésétől, és egy külön élő Unity layout-egyeztetéstől
(a jelenlegi valós játékban a World/Continent/Region Canvas-listák a
képernyő BAL oldalán vannak, a Deep time/rétegek pedig JOBB oldalon - ez
eltér a mockup elrendezésétől, amit a felhasználó a screenshot alapján
hagyott jóvá). Ez a két tétel (deep-time lépték, kamera-panel) szándékosan
független, azonnal szállítható rész volt.

## Ellenőrzés

- `dotnet build unity/WorldGenViewer/Assembly-CSharp.csproj -p:Nullable=disable -p:NoWarn=618`:
  a build a `WorldGen.Viewer.Lod.csproj` függőségnél 26 CS8632 hibával
  áll meg - **ugyanez a 26 hiba, ugyanazokban a (nem általam módosított)
  Lod-fájlokban, ugyanazokon a sorokon** akkor is lefut, ha a
  `PlanetGridMesh.cs`-t `git stash`-sel visszaállítom az én
  módosításom NÉLKÜLI állapotba - tehát ez egy tőlem FÜGGETLEN,
  előzetesen is meglévő hiba (feltehetően SDK/LangVersion-eltérés az
  offline generált csproj és a jelen `dotnet` verzió között - ld.
  `docs/backlog.md`/history ND-90 megjegyzése: "az offline, Unity által
  generált Assembly-CSharp.csproj nem tiszta kapu"). A saját új kódom
  (`DrawCameraStatePanel`, a step-tömbök bővítése) nem használ nullable
  annotációt (`?`), ezért nem lehet ennek az hibaosztálynak a része.
- `dotnet test tests/WorldGen.Viewer.LodChunking.Tests`: PASS, 373/373
  (nem érinti a módosítás, de a megosztott Lod-fájlok épsége is igazolva).
- **Élő Unity Game view ellenőrzés hátra**: a 9 gombos sor tényleges
  elrendezése (nem csonkolódik-e a felirat 675px-en) és az új "Kamera
  állása" doboz vizuális elhelyezkedése/átfedésmentessége a Deep time
  dobozzal.
