# 2026-09-20 — Deep-time-invariáns csapadék-cache

## A feladat és az előírt sorrend

A todo.md 1. tábla 5. sora kikötötte: **előbb tételesen rögzíteni a
cache-kulcsot és minden invalidáló Inspector-paramétert, csak utána
implementálni.** Ezt a sorrendet tartottam.

**A mérés.** A `Build() precipitation(enabled=True)` sor a naplókban
**322–410 ms**, minden Build-ben újra (a todo 295 ms-os becslésénél is több).

## Miért cache-elhető egyáltalán

A `MoisturePrecipitation.Compute` a **deep-time értéket meg sem kapja**. Saját,
t=0-ás mezőt számol (`SeaLevelCalibration.ComputeElevationField`), kráter és
erózió nélkül — klíma-közelítés, nem a deepTime-eltolt domborzat. Ezt a hívás
feletti komment már eddig is rögzítette. Vagyis a `deepTimeMyr` változása nem
változtatja meg az eredményt, **csak újraszámoltatja** — épp ez a deep-time
csúszkázás fő vesztesége.

## A kulcs tételes leltára

A Core-függvény tiszta (I2), tehát a kulcs pontosan az átadott argumentumok
halmaza. A hívás **nyolc** értéket ad át, mindegyik Inspector-mező:

| # | Compute-paraméter | Inspector-mező |
|---|---|---|
| 1 | `worldSeed` | `worldSeed` |
| 2 | `plateCount` | `plateCount` |
| 3 | `level` | `level` |
| 4 | `dayT` | `climateDayT` |
| 5 | `orbitalPeriodDays` | `climateOrbitalPeriodDays` |
| 6 | `rotationPeriodDays` | `climateRotationPeriodDays` |
| 7 | `axialTiltDegrees` | `climateAxialTiltDegrees` |
| 8 | `targetWaterFraction` | `targetWaterFraction` |

A maradék négy paraméter (`iterations`, `precipBaseFraction`,
`orographicCoeff`, `orographicElevScale`) **alapértelmezett** marad, tehát
fordítási idejű konstans — nincs Inspector-mező, ami invalidálná.

**Szándékosan nincs a kulcsban:** `deepTimeMyr` (a függvény meg sem kapja),
`showCraters` / `showDeepTimeErosion` / `terrainReliefExaggeration` /
`elevationScale` / `adaptiveBaseLevel` / `hydrologyLevel` (a Core-út egyiket
sem látja), valamint `precipitationOverlay` / `showRivers` / `showClouds`
(csak azt döntik el, *kell-e* a mező).

## Megosztott objektum — külön ellenőrizve

A cache ugyanazt a `PrecipitationField` példányt adja vissza több Build-nek,
tehát csak akkor helyes, ha **senki nem írja** a tartalmát. Tételesen
ellenőriztem: a viewer minden használata olvasás (`TryGetValue`, `foreach`,
továbbadás), és a `RiverPathTracing.SelectRiverSources` is kizárólag saját,
lokális gyűjteményekbe ír. Ez a kódban is rögzítve van, azzal együtt, hogy aki
ezen változtat, annak a cache-t másolóra kell cserélnie.

**Nincs életciklus-alapú ürítése** (a `Build`/`ResetWorldState` nem törli): a
kulcs mindent tartalmaz, ami az értéket meghatározza, tehát egy „régi"
bejegyzés azonos kulcson azonos érték. Az életciklus-alapú ürítésnek épp az
lenne a hibája, amit elkerülünk.

## Igazolás

`PrecipitationCacheKeyTests` — pontosan a CLAUDE.md teszt-mátrixának két sora
szerint:

- **Tisztaság**: kétszer ugyanaz a bemenet → **bitre azonos** mező. Enélkül a
  cache eleve helytelen lenne.
- **Minden kulcs-elem érdemben hat**: mind a nyolc megváltoztatása más mezőt
  ad. Ha valamelyik nem hatna, az vagy fölösleges a kulcsban, vagy a Compute
  viselkedése változott meg.
- **A nem-kulcs alapértékek is hatnának**: `iterations`,
  `precipBaseFraction`, `orographicCoeff`, `orographicElevScale` mind
  megváltoztatja a kimenetet — tehát ha valaha Inspector-mezővé válnának, a
  kulcsba is be KELL kerülniük. Ez így bizonyítva van, nem csak egy komment
  állítja.
- **A Compute-nak nincs deep-time paramétere**: reflexiós ellenőrzés, ami
  elbukna, ha valaki később felvenne egyet — onnantól a kulcs hiányos lenne.

Core 499 teszt zöld (495 → +4). Offline viewer-kapu 0 hiba.

A PerfLog `precipitation(...)` sora mostantól kiírja a `cacheHit=` értéket.

**Futásidejű visszamérés hátravan** — a Unity Editor főszála ebben a
munkamenetben nem válaszolt.

## Tanulság

**A cache-kulcs hibája kétféle, és mindkettő csendes**: ha kimarad egy
paraméter, a cache elavult értéket ad, és a hiba csak egy konkrét
paraméter-váltás után, jóval később jelentkezik; ha a függvény mégsem tiszta,
ugyanaz a kulcs más értéket takar. Egyik sem bukna el a szokásos „lefut-e"
ellenőrzéseken — ezért ért annyit az előírt sorrend, hogy előbb a leltár, és
csak utána a kód.
