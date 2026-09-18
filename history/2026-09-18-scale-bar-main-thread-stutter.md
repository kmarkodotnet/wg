# A rotáció/zoom szaggatás valódi oka: a léptékcsík a főszálon (2026-09-18)

## Előzmény és a saját hibám

A 09-13-i diagnózis a LOD-cut növekedésére mutatott, és két irányt
javasolt; én az egyiket (`adaptiveRenderBudget` 200 000 → 8 000)
implementáltam **más gépen mért logból extrapolálva**, élő mérés nélkül.
A felhasználó visszajelzése: "így is szaggat". Ez helyes kritika volt — a
projekt saját retrospektívájának #1 tanulsága (élő mérés, ne kódelemzés)
pont ezt írja elő, és nem tartottam be.

## A mérés, ami eldöntötte (a felhasználó gépén, a javítás UTÁNI futásból)

`unity/WorldGenViewer/Logs/PerfLog_20260918_170408.txt` (budget=8000 már
érvényben) és `PerfLog_20260918_164558.txt`:

| | 16:45-es futás | 17:04-es futás |
|---|---|---|
| `[ND-84 scale diag]` hívás | 360 | 30 |
| ezek ÖSSZES ideje a FŐSZÁLON | **44 402 ms (44,4 s)** | 3 046 ms |
| egy hívás | 30–180 ms, 658–3 393 `ComputeElevationAtPoint` | ugyanaz |
| mesh-feltöltés (`uploadMs`) | **0,04–0,76 ms** | – |
| `cut.Count` maximum | – | 2 721 (a 8 000-es budget **nem is kötött**) |

**A léptékcsík egyetlen munkamenetben 44 másodperc főszál-időt vitt el,
miközben a mesh-feltöltés — amit körökön át optimalizáltunk — 1 ms alatt
van.** 180 ms egyetlen hívásban = 11 kihagyott képkocka egy hitch-ben,
másodpercenként 6-7-szer.

Ez azt is megmagyarázza, miért nem segített a geometria 25×-ös
csökkentése: a költség a cut méretétől FÜGGETLEN.

Időzítés: a léptékcsík (ND-84) **2026-09-13 reggel** került be — pontosan
akkor, amikor a felhasználó szerint "visszajött a jelenség".

A 09-13-i jegyzet ezt "másodlagos leletként" elintézte *becsléssel*
("max néhány száz felszín-mintavétel/hívás"). A valóság 658–3 393
mintavétel és 30–180 ms. A diagnosztikai naplózás, amit akkor beépítettek,
**megírta a választ — csak senki nem olvasta el a számait.**

## Gyökérok

`PlanetOrbitCamera.OnGUI` → `RecomputePhysicalScaleBar` →
`ScaleBarMath.TryFindMeasurableWidth` + `TrySolveScale` (egymásba ágyazott
felezések) → `TryMeasureCenteredSurfaceDistance` → 2 × sugár-metszés a
DOMBORZATTAL → `TryGetScaleSurfaceRadius` → `ComputeElevationAtPoint`
(teljes Core-lánc: lemez-hozzárendelés + domain warp + ridged multifractal
+ másodlagos zaj + kráterek + erózió) — **mintavételenként**.

Két külön pazarlás:

1. **Mozgás közben az eredmény kárba megy.** Az `IsScaleBarContextCurrent()`
   minden képkockán érvényteleníti a számot (ezért látszik forgatás közben
   "Lépték: —"), tehát 30–180 ms-ot fizettünk egy azonnal eldobott értékért,
   pont a forgatás/zoom alatt.
2. **Álló kamerával is újraszámolt** 0,15 s-onként, pedig a korábbi érték
   érvényes maradt — a régi kód csak időzítőt nézett, nem érvényességet.

## Javítás (`PlanetOrbitCamera.ScaleBar.cs`)

Két kapu a drága számítás elé:

- `IsScaleBarViewSettled()`: csak akkor számol, ha a nézet (kamera- +
  bolygó-transzform + viewport + képernyőmagasság) legalább
  `scaleBarSettleSeconds` (új mező, alap 0,12 s) ideig **változatlan**.
  Csak mátrix-összehasonlítás, nulla felszín-kiértékelés. Szándékosan
  külön pillanatkép-mezőkben, mert a meglévő `_scaleSample*` mezők a MÁR
  kiszámolt eredmény érvényességét kötik a nézethez — ha ugyanazokat
  használnám, a recompute saját mentése azonnal "mozdulatlannak" jelentené
  a kamerát, és a kapu sose fogna.
- `_scaleBarComputedForCurrentView`: egy változatlan nézethez pontosan
  **egy** számítás tartozik, akár sikerült, akár nem. A sikertelen esetet
  is le kell fedni (pl. az ég felé nézve a sugár nem talál felszínt),
  különben ott maradt volna a másodpercenkénti újrapróbálkozás.

Nettó hatás: a költség **kameramegállásonként egy** számítás, a korábbi
másodpercenkénti 6-7 helyett; forgatás/zoom közben **nulla**. Vizuális
veszteség nincs: mozgás közben eddig is "—" volt a felirat (sőt eddig
villogott érték és "—" között, most stabilan "—", majd megállás után
~0,12 s-mal megjelenik a szám).

## Az `adaptiveRenderBudget`-ről (az előző körös változtatásom)

A 17:04-es logban a `cut.Count` maximuma **2 721** volt — a 8 000-es
budget a felhasználó tényleges kameraállásainál **nem is kötött**. Tehát
az a változtatás sem nem segített, sem nem rontott (vizuális minőséget sem
vett el). Meghagyható biztosítékként, vagy visszaállítható 200 000-re —
ez a felhasználó döntése, a szaggatáshoz nincs köze. A 09-13-i, 33 000
levelet mutató log valószínűleg más zoom-szinten készült.

## Ellenőrzés

- Unity `Assembly-CSharp.csproj` fordítás: 180 hiba, mind a 8 MÁR ISMERT,
  elfogadott osztályban (CS0618 + CS8600-8625 nullable) — **nulla új
  hibaosztály, nulla hiba a módosított fájlban.**
- `dotnet test tests/WorldGen.Viewer.LodChunking.Tests`: 391/391 PASS
  (a `ScaleBarMath` tiszta matek-tesztjei érintetlenek; a kapu a
  MonoBehaviour-ban van, azt Unity nélkül nem lehet unit-tesztelni).
- Core nem érintett, nem seed-törő.

## ELFOGADÁSI KRITÉRIUM (élő, mérhető — nem "szerintem jó")

Play módban forgatás/zoom közben a PerfLogban **nem jelenhet meg egyetlen
`[ND-84 scale diag]` sor sem**; megállás után pontosan egy. Ha a szaggatás
így is megmarad, a következő mérési lépés a Unity Profiler főszálas
idővonala — de akkor már tudjuk, hogy nem ez a 44 másodperc a maradék ok.

## MÁSODIK KÖR: "a forgatás/zoom ok, de a léptékcsík eltűnt"

A felhasználó visszajelzése az első kör után: a szaggatás megoldva, de a
csík **eltűnt**. Ez az én kapumnak két hibája volt, mindkettő a
**bitpontos** összehasonlításból:

1. **A kapu sosem nyílt ki.** A `PlanetOrbitCamera.Update()` MINDEN
   képkockában lefuttatja a `RefreshLocalSurface()` +
   `ConstrainSurfaceDistance()` párt, ami a terep alapján aprót állít a
   `distance`-en (ND-91 felszínkövetés). Ezért a kameramátrix két
   képkocka között **soha nem bitazonos** → a "mozdulatlan" teszt soha nem
   teljesült → soha nem futott újraszámolás.
2. **Ha mégis lefutott volna, a következő képkocka eldobja.** Az
   `IsScaleBarContextCurrent()` szintén bitpontos mátrix-egyenlőséget
   vizsgált, tehát a frissen számolt érték egyetlen képkocka után
   érvénytelen lett. A régi kód ezt azzal "kezelte", hogy másodpercenként
   6-7-szer újraszámolt (innen a villogás — és a 44 másodperc).
   Ráadásul a két külön pillanatkép (érvényesség vs. mozgás) el tudott
   csúszni egymástól, ami véglegesen be is fagyaszthatta a "—" állapotot.

### Javítás (második kör)

- **Toleranciás összehasonlítás** a kamera SAJÁT állapotán (yaw, pitch,
  distance, fov + bolygó-transzform, viewport, képernyőmagasság) a
  levezetett mátrix helyett: szögnél 0,01°, `distance`-nál relatív 1e-4.
  Ezek a nagyságrendek a **kerekített** feliratot ("10 km") nem
  befolyásolják, a felszínkövetés apró korrekcióját viszont elnyelik.
- **Egyetlen közös segédfüggvény** (`IsSameScaleBarView`) szolgálja az
  érvényesség-ellenőrzést ÉS a mozgásérzékelést, hogy a kettő ne tudjon
  elcsúszni — pont ez a divergencia volt az egyik hibaforrás.
- **Aktív bevitel alatt nulla számítás**: amíg a bal gomb le van nyomva
  vagy görgetés történik, semmi nem fut (ez az az interakció, amit a
  szaggatás érintett).
- **Garancia (`scaleBarMaxWaitSeconds`, alap 1,0 s)**: ha a nézet SOSEM
  nyugszik meg (tengelyforgás-mód, FlyTo-animáció), és épp nincs érvényes
  szám, akkor is lefut egy számítás — így a csík nem tűnhet el véglegesen.
  Aktív bevitel alatt ez az ág is tiltva van, hogy hosszú húzás közben ne
  jöjjön vissza másodpercenként egy hitch.

### Várt viselkedés

| helyzet | csík | főszál-költség |
|---|---|---|
| húzás/zoom közben | "Lépték: —" | **0** |
| elengedés után ~0,12 s | szám megjelenik és **marad** | 1 számítás |
| álló kamera, tovább | változatlan szám | **0** (eddig: 6-7/s) |
| tengelyforgás-mód | ~1 s-onként frissülő szám | ≤1 számítás/s |

## Nyitott follow-up (nem ebben a körben)

Ha a csík mozgás közben is frissüljön, a számítást érdemben olcsóbbá kell
tenni. A legígéretesebb: a sugár-metszés az ALAP GÖMBRE analitikusan
(zárt formula, nulla terep-kiértékelés) — a kijelzett érték amúgy is
KEREKÍTETT ("10 km"), a domborzat miatti eltérés (néhány km a 6371 km-hez)
messze a kerekítés alatt van, tehát a mostani, mintavételes pontosság
olyan precizitást vásárol 30–180 ms-ért, amit a felirat eldob.
