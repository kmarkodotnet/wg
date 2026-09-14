# 2026-09-13 — Üres léptékmező forgatás után (ND-84)

## Tünet

A felhasználó jelezte: alap zoomon a km-lépték értéket mutat, kontinensre
kattintás és forgatás után „Lépték: —” marad, visszazoomolva sem jön vissza.

## Diagnózis naplóból

A `PlanetOrbitCamera.ScaleBar.cs` ideiglenes `[ND-84 scale diag]` naplót kapott
(kiírt felirat, érvénytelenítés oka, kontextus-eltérés részei, repülés, távolság).
Az élő próba (`Logs/PerfLog_20260913_120745.txt`, 51 diagnosztikai sor):

- Zoom közben a mérés rendben: a kontextus csak mozgás alatt avul el, utána
  újra `measured`.
- Forgatás után (frame 996-tól) minden újraszámítás
  `solve-pixel-width-failed(maxWidth=180, maxDist≈1 958 000 m, nice=1 000 000 m)`,
  300-as távolságból is (`maxDist≈4 400 000, nice=2 000 000`).
- A maximális szélesség tehát mérhető, csak a kerek értékhez tartozó
  pixelszélesség felezéses keresése bukik: 111× domborzat-túlrajzolás mellett a
  képernyőpontok radiális felszínmetszése hegygerincen átugrik, a távolság a
  szélesség szerint nem folytonos, így nincs 10 m-en belüli megoldás.

## Javítás

- `ScaleBarMath.TrySolveScale`: legfeljebb 4 egyre kisebb 1/2/5 kerek érték;
  az első pontosan megoldható nyer. Ha egyik sem oldható meg pontosan, a
  felirat a legkisebb relatív eltérésű **kerek érték** marad, és a csík
  szélessége a hozzá legközelebbi mért távolságú szélesség (felhasználói
  kérés: nem kerek szám ne jelenjen meg, a csík igazodjon). Sikertelen
  mintavétel nem szakítja meg a felezést.
- A diagnosztikai ok: `measured(round-exact,attempts=n)` vagy
  `measured(round-nearest-width,relErr=…,attempts=n)`.
- Regressziós tesztek: folytonos felszín (első kerek érték, pontos),
  gerinc-ugrás (kisebb kerek érték, pontos), lépcsős távolság (kerek felirat,
  igazított szélesség, relErr < 10⁻³), sikertelen mintavétel átlépése,
  érvénytelen bemenet.

## Ellenőrzés

- `WorldGen.Viewer.LodChunking.Tests`: 383/383 (4 új eset).
- Offline Unity Assembly-CSharp: 0 hiba.
- Élő újrapróba: a felhasználó megerősítette, hogy hegyvidék fölött is kerek
  érték és igazított csík jelenik meg. Ezután a `[ND-84 scale diag]` napló és
  a `logScaleBarDiagnostics` mező teljesen eltávolítva (a diagnosztikai
  sztringépítés mozgás közben képkockánként allokált volna).

Nincs commit.
