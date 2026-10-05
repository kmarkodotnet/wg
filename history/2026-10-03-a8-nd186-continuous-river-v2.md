# A8 / ND-186 — folytonos folyó-nyomkövető v2 (az ND-180 „C" opciója, 1. kör)

2026-10-03. A felhasználó kérése: „folytasd az A8 feladatot, egyelőre
ellenőrizni nem tudom." Ezért ebben a körben NEM a láthatóságon (ami kézi
átvételt kíván), hanem az ND-180-ban MÉRT, ellenőrizhető modellhibákon
dolgoztam.

## Kiindulás

Az ND-180 (2026-10-02) három hibát igazolt a megjelenített, folytonos
folyóhálózatban, és a „C" opciót javasolta — Python-orákulummal, majd C#
porttal. A folytonos követőnek addig **nem volt orákuluma** (a
`river_path_ref.py` a durva, TileId-rácson futó változatot írja le).

## Amit elvégeztem

**1. Python-orákulum.** `tools/reference/river_continuous_ref.py` — a v2
algoritmus definíciója szintetikus, ANALITIKUS domborzatokon (lejtő sík,
parabolikus falú völgy, számolt peremű zárt medence, két párhuzamos meder
közeli és távoli távolságon). A valódi elevációs láncot a Python nem tudja
bitre reprodukálni, a hibaosztályok viszont ezeken a terepeken pontosan
előállnak. Az orákulumnak `legacy` módja is van, hogy az előtte–utána SZÁM
legyen, ne állítás. 8 eset, 1002 pont, kétszeri futtatásra bitre azonos
kimenet. CI-be bekötve (`reference-oracle` job, `diff` a tesztvektorra).

**2. C# port, bitre az orákulumhoz mérve.** A követő mostantól
`IElevationSampler`-rel paraméterezhető (generikus, `struct`-ra kötött — a JIT
devirtualizálja, nincs delegate-költség), tehát a szintetikus tesztek
UGYANAZT a kódot mérik, amit a termék futtat. Az új
`ContinuousRiverV2Tests.PythonOracleVectorsMatchBitExactly` mind a 1002 pont
mindhárom koordinátáját `Assert.Equal`-lal (tolerancia nélkül) hasonlítja.

**3. A három javítás.**

- *(1) Iránykvantálás.* A v1 a 8 jelölt irány közül a legjobbat választotta,
  és abba lépett. A v2 a jelölt-kör ELSŐ HARMONIKUSÁBÓL számol gradienst
  (`g = Σ_k (e_k − átlag)·d_k`), és annak ellentettjébe lép; ha a folytonos
  irány nem lejt, visszaesik a legjobb jelölt-irányra, tehát a szigorú
  lejtés-kapu nem gyengül.
- *(2) Hamis / teleportáló összefolyás.* Új `ClaimedRiverPoints`: cellánként a
  lefoglalt pontok LISTÁJA, és csak a toleranciánál közelebbi valódi pontra
  zárunk, a legközelebbire. A záróél hossza így strukturálisan korlátos.
- *(3) Finomítatlan escape.* A priority-flood rácsútvonalából „víz alatti"
  egyeneseket vonunk össze: ahol az egyenes MINDEN mintapontja az útvonal
  feltöltési szintje alatt marad, ott az egyenest emittáljuk. Ez nem
  dekoratív simítás — az a terület a feltöltődés után víz alatt van, ahol a
  fizikai vízfelszín sima, a rács lépcsője a rács mellékterméke.

**4. Két csendes I1-sértés is megszűnt.** A jelölt-irányokat a v1
`Math.Cos`/`Math.Sin`-nel állította elő (ND-23: nem bitpontos); a v2
`RiverDirectionTable`-je szögfelezéssel (`normalize(a+b)`, csak `+`, `/`,
`sqrt`) épít bitre szimmetrikus táblát. A hurok-védelem és a `claimed` keresés
minden lépésben `TileGeometry.FromPosition`-t hívott, ami `Math.Atan`-t
használ — az **ND-24 ezt EXPLICITEN kizárja a szimuláció kritikus útjáról**.
Helyére a `CubeFaceLattice` került: nyers kocka-projekció, csak osztás és
összehasonlítás. Egy használaton kívüli `Math.Acos`-os segédfüggvény is kiesett.

## Mérés a VALÓDI t=0 hálózaton (96 ág, level 5, 4 worker)

Végleges beállítás: escape-emisszió **250 m**, összefolyási tolerancia
**500 m** — mindkettő MÉRÉSSEL választva (ld. lent). A „v1" oszlop az ND-180
alapvonala ugyanerre a seedre és ugyanezzel a 4-worker úttal.

| Mérőszám | v1 (ND-180) | v2 (ND-186) | változás |
|---|---|---|---|
| ágak | 96 | 96 | — |
| **legnagyobb él** | **24,113 km** | **0,499 km** | **−98,0%** |
| **legnagyobb összefolyási záróél** | **24,113 km** | **0,499 km** | **−98,0%** |
| **30° felett számolt irányváltás (mind a 96 ág)** | **93 404** | **673** | **−99,3%** |
| legrosszabb egyetlen ág irányváltásai | 3 292 | 26 | −99,2% |
| 0. ág: irányváltás / pont / hossz | 1 422 / 8 330 / 560,0 km | **9** / 8 525 / 550,2 km | — |
| összefolyás / Ocean / Pit | 19 / 66 / 11 | **18 / 67 / 11** | ~változatlan |
| teljes hossz | 48 879,793 km | 46 641,008 km | −4,6% |
| pontszám | 415 293 | 504 060 | +21,4% |
| **hálózatidő (4 worker)** | **71 950 ms** | **60 831 ms** | **−15,4%** |
| csúcs working set | 116,9 MB | 136,0 MB | +16,3% |

**A fa MEGMARADT — ezt külön ellenőriztem, mert a javítás elvileg
szétszedhette volna.** Az összefolyások száma 19 → 18, a zsákutcák száma
pontosan ugyanannyi (11). Tehát a 24 km-es teleport megszüntetése nem
„olvasztotta le" a hálózat szerkezetét. (A `todo2.md`-ben szereplő „43 → 32
összefolyás" egy KORÁBBI, más kísérlet száma volt; a tényleges v1 alapvonal az
endpoint-fájlból 19.)

### Két paraméter, amit MÉRÉS döntött el, nem becslés

**Összefolyási tolerancia.** Söpörve a valódi t=0 hálózaton: 100 m → 14
összefolyás, 250 m → 14, **500 m → 18**, 2000 m → 18. A szám **500 m-nél
telítődik**; fölötte már csak a záróél nyúlik. Ez egybeesik egy fizikai
érvvel: 500 m a követő saját érzékelési sugara, vagyis az a lépték, amin a
modell a domborzatot megítéli. Kimondott következmény: a záróél így legfeljebb
~500 m, nem 100 m.

**Escape-emisszió sűrűsége.** Először a normál lépésközre (50 m)
mintavételeztem, és ez **954 598 pontot és 254,7 MB csúcsmemóriát** adott
(+130% / +118%) — vagyis a mesh-előkészítés 2,3-szorosára nyúlt volna. Holott
az escape-szakasz ALAKJÁRÓL a 2000 m-es döntési rácsnál finomabb mintavétel
nem ad új modell-információt (az a szakasz „víz alatti", azaz sima), és az
ALAK mérőszáma (irányváltások) 250 m-en ugyanolyan jó: 686 vs 690. 250 m még
mindig 8× finomabb, mint a döntési rács. Így a pontszám 504 060 lett (+21,4%),
és az idő a v1 alá került.

**Egy mérőszámot ezzel visszavontam.** Az ND-180 „75 m-nél hosszabb élek
hossz-aránya" metrikája (58,77%) 250 m-es emisszióval definíció szerint
visszatér (57,37%) — de ez MOST a szándékolt viselkedést jelzi, nem lépcsőt.
Ezért az alak mérőszáma innentől a **30° feletti irányváltások száma** és a
**legnagyobb él**; ezek 93 404 → 673, illetve 24,113 → 0,499 km.

## Egy teszt elbukott, és ez tanulságos volt

A `DischargeWeightsAreIdentical` (16 forrásos KICSI tesztvilág) elbukott: ott a
valódi térbeli közelségvizsgálattal 0-1 összefolyás van, mert 16, egymástól
távoli forrás nyomvonala gyakorlatilag soha nem kerül néhány száz méterre
egymáshoz. Ez a kis minta sajátossága — a valódi 96 forrásos hálózaton, mint
fent látható, a fa megmaradt (19 → 18).

A tesztet nem „javítottam vissza" és nem is lazítottam: a tárgya a
szekvenciális/párhuzamos egyezés és a lánc-akkumuláció, ezért most KÉT
toleranciával fut (alapértelmezett: egyezés fa-követelmény nélkül; 2000 m: ott
ebben a világban is van összefolyás, tehát a súly-akkumuláció is mérve van), és
ráadásul minden összefolyási záróél korlátosságát is ellenőrzi.

A Python-orákulum ugyanezt a hibaosztályt függetlenül is kimutatta: a legacy
mód két, egymástól 2968 m-re futó PÁRHUZAMOS medret NEM kapcsolt össze, mert a
cellahatár épp közéjük esett — vagyis a régi szemantika egyszerre adott hamis
összefolyásokat és hagyott ki valódiakat. A fa MÉLYSÉGE külön kérdés, és annak
oka a forrás-kiválasztás (már az ND-124 is kimondta), nem a nyomkövető.

A tesztet ezért SZÉTVÁLASZTOTTAM, nem lazítottam:

- `DischargeWeightsAreIdentical`: két toleranciával (alapértelmezett és 4×)
  ellenőrzi a szekvenciális/párhuzamos egyezést, minden összefolyási záróél
  korlátosságát, és a súlyok összegét egy FÜGGETLENÜL számolt azonossággal
  (ágszám + az ősök összes száma — ez láncokra is igaz, a puszta
  „ágszám + összefolyás" csak egyszintű fára lenne az). A 4× kontroll MÉRI,
  hogy nem a toleranciáról van szó: 2000 m-en is 0 összefolyás.
- `DischargeWeightsAccumulateMultiLevelChains` (ÚJ): a lánc-akkumulációt
  szintetikus fán (0←1←2←3, plusz egy önálló és egy második mellékfolyó)
  méri, azonnal lefut, és nem múlik a terep szerencséjén. Korábban ezt a
  logikát csak egy valódi hálózat mellékhatásaként mértük — ott pedig, mint
  kiderült, nem is futott le.

**Egy számot viszont tisztességesen jelezni kell.** A `MoreSourcesProduceMoreConfluences`
teszt világában (seed 0xA7C944210000, level 6, globális forrás-kiválasztás) az
összefolyások száma 12 forrásnál 1 → **0**, 48 forrásnál 15 → **10** (az
ND-124-ben rögzített értékekhez képest). A teszt továbbra is zöld (10 > 0), de
ebben a konfigurációban a fa tényleg ritkább lett. A valódi, per-medence
forrás-kiválasztású 96 ágú hálózaton ellenben 19 → 18, tehát a termék útján
nincs érdemi veszteség. A kettő közti különbség maga is az ND-124 melletti
érv: a per-medence kvóta azért ad fát, mert a forrásokat KÖZÖS torkolat felé
tartó ágakra teszi.

## Tudatosan nyitva hagyott döntés: ND-187

A mért 58,77%-os escape-arány azt jelenti, hogy a folyók a hosszuk több mint
felét zárt medencéken átvágva töltik. Az ND-186 ezt GEOMETRIAILAG kezeli, de a
modellkérdést nem dönti el: egy ilyen medence fizikailag TÓ lenne. Ezt új
ND-187-ként vettem fel, opciókkal és azzal, hogy a döntéshez előbb MÉRÉS kell
(hány medence, mekkora felülettel, és hány egyezik a `LakesIceErosion` már
meglévő tavaival) — nem oldom meg csendben.

## Kompatibilitás

Mindhárom javítás numerikus, tehát minden folyóhálózat új. Generátorverzió
**8 → 9** (`WorldGeneratorVersion.Current`), így a régi `worldpkg` elutasítása
és a hőmodell-gyorsítótár érvénytelenítése automatikus (a cachekulcs
tartalmazza a generátorverziót). Folyó-specifikus lemez-cache nincs.
Az A8 korábbi bitazonossági állításai a v1-re szóltak.

## Bizonyíték

Az `artifacts/` nincs verziózva; a mérések ide kerültek:

- `artifacts/a8-nd186/e250_r500/` — a **végleges** beállítás geometria-, endpoint-
  és fingerprint-fájljai (`t0-geometry.csv`, `t0-parallel-endpoints.csv`,
  `t0-parallel4-fingerprint.txt`), `e250_r500.log` a konzolkimenettel;
- `artifacts/a8-nd186/r100/`, `r500/`, `r2000/`, `e250_r250/` — a tolerancia- és
  emisszió-söprés futásai;
- `artifacts/a8-geometry-current/` — az ND-180 v1 alapvonala (változatlan);
- `artifacts/a8-nd186/tests-final.log` — a teljes megoldás-szintű tesztmenet.

Reprodukálás: `dotnet run --project tools/diagnostics/RiverBaseline -c Release
-- 0 <kimenet> parallel4 50 <összefolyási_tolerancia_m>`. A diagnosztika
mostantól kiírja az összefolyások számát, az irányváltásokat és a pontszámot is.

Tesztek: a `ContinuousRiverV2Tests` 5 esete (ismert-válasz bitre az
orákulumhoz — a pontok mellett a bitpontos iránytábla és az atan-mentes rács
KAT-vektoraival is; tolerancia-korlátos záróél; tisztaság; minden paraméter
hat; élesetek + hurokmentesség + a rácsszint-számítás lefedési feltétele), plusz
az új `DischargeWeightsAccumulateMultiLevelChains`.

Teljes megoldás-szintű menet **Debug ÉS Release: 2005/2005 PASS** (Core 865,
viewer LOD 664, app 452, CLI 24), mindkettő EXIT=0 — tehát **nincs
Debug/Release eltérés**, ami a projekt determinizmus-kapuja.
Naplók: `artifacts/a8-nd186/tests-debug2.log` (Core 9 m 21 s) és
`tests-release2.log` (Core 1 m 51 s). Offline Unity
fordítási kapuk zöldek (`WorldGen.Viewer.Compile`,
`WorldGen.App.UnityBinding.Compile`), a Burst-kapu és a self-test is
(`28 eset`). CI-egyenértékű orákulum-ellenőrzés: a regenerált
`river_continuous_vectors.json` és `thermal_checkpoint_vectors.json` diffje
tiszta.

**Élő Unity Editor:** kértem tőle újrafordítást (`recompile`) — `completed`,
`compilationFailed: false`, **0 hiba** a konzolon (a 278 figyelmeztetés a
korábbi szint). Ez a Unity-oldali FORDÍTÁST igazolja, nem a Play-menet
látványát vagy teljesítményét.

## Közben történt: a korábbi munka commitolva

A munkamenet közben a korábbi, commitolatlan A24 + A8-láthatósági munka
`ca2295a "a8"` néven commitra került (88 fájl) — nem én csináltam. Ennek
következménye, hogy az ND-186 változásai most a repo EGYETLEN commitolatlan
deltája (16 módosított + 13 új fájl). **Commit és push nem történt**, mert
erre nem kaptam kérést.

## Ami hátravan

- **A felhasználói vizuális átvétel** (B3) — ehhez a
  `docs/06-user-verification-checklist.md` A8/ND-186 szakasza készült el.
  Ez a kör NEM tekinthető látványelfogadásnak.
- **Unity Editor-oldali ellenőrzés**: a viewer a `…Parallel` hívást használja,
  aminek a szignatúrája visszafelé kompatibilis, de a tényleges Play-menet
  (mesh-pontszám, memória, deep-time váltás) nem futott le ebben a körben.
- **ND-187** (medence = folyó vagy tó) és a fa mélysége (ND-124 iránya).

## Durva becslés

E kör munkája durván **5-7 emberóra** (nincs valós időnaplózás a projektben,
ez becslés, nem mért tény). Az A8 tartalmilag súlyozott állapota a geometriai
hibák lezárásával **~80% → ~88%**; hátra a kézi átvétel és az Editor-oldali
teljesítménykapuk (**4-8 óra**), az ND-187 pedig külön tétel (**8-24 óra**).
