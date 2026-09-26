# A8/1 — Mérési alap és döntési kapu: eredmény

**Dátum:** 2026-09-23. **Döntés:** az A8/2-ben felvetett egyszerű, kör szerinti forrás-átrendezést **elvetjük**. A szekvenciális finomító változatlan. A B3 Play-visszajelzés szerint jelenleg hiányoznak a folyóvonalak; erre külön, modellalapú durva előnézet készült (ND-145), élő vizuális elfogadása nyitott. A részletes, reprodukálható alapmérés és a nyers adatok: [2026-09-23-a8-river-baseline.md](../../../history/2026-09-23-a8-river-baseline.md).

## Mérési eredmény

| Környezet és állapot | `Build() TELJES` | Kész folyóhálózat az indítástól | Folyó-mesh | Megjegyzés |
|---|---:|---:|---:|---|
| Offline .NET, 0 Myr, első 96 forrás | nem mért | **150,0 s** | nincs Unity mesh | Teljes Core-hálózat |
| Offline .NET, 0 Myr, ismétlés | nem mért | **141,8 s** | nincs Unity mesh | Bitazonos lenyomat |
| Offline .NET, 22 Myr | nem mért | **101,9 s** | nincs Unity mesh | Ugyanaz a forráslista |
| Meglévő Unity PerfLog, 2026-09-22, generáció 6 | **3,012 s** | **1 477,6 s** (`river ready`) | **13,236 s** | Az egyetlen talált befejezett Play-menet; a világidő nincs a `river start` sorban rögzítve |

A 2026-09-23-as két Unity PerfLogban a Build 3,385 és 2,970 s volt, de a folyófeladat 245,0, illetve 140,6 s után **megszakadt**. Ezekből nem számoltunk elkészülési időt. Azonos paraméterű, kontrollált hideg/meleg Unity-mérés és deep-time váltás utáni kész mesh jelenleg nincs. A Unity-falióraidő és az offline Core-idő eltérésének oka nem igazolt; a `Build()` rövidülése önmagában nem folyógyorsulás.

Az offline konfiguráció a scene alapértékeit követi: `0xA7C944210000` seed, 20 lemez, `level=5`, 0,65 vízarány, `climateDayT=10`, `riverRefinementStepMeters=50`, `fineDepth=4`, 16×6 forrás; AMD Ryzen 7 5700X, 16 logikai processzor, Windows x64, .NET 8 Release. A 96 elemű forráslista [itt](../../../history/a8-2026-09-23/sources.csv) olvasható. A `ComputeDischargeWeights` értékeit és minden nyomvonalpont X/Y/Z koordinátájának teljes `double` bitmintáját tartalmazó t=0 lenyomat mindkét teljes futásban:

`3a7940c6b2b0faab4f624297493588cf4193ed755db1154693cbc0bf7dd046d8`

A t=22 Myr lenyomat: `c0c1a8ff3b6ad283e5fc6f8451c4c5e856459e67c171b6dbd53fef64d52a4293`. A hat első forrásnál a diagnosztikai és az eredeti Core-hálózatépítő minden topológiai mezője és koordinátája bitre egyezett. A mérőeszköz csak egy opcionális pit-escape számlálót kapcsol a nyomkövetéshez; a számítás sorrendjét és képleteit nem módosítja.

## Költség és döntési ok

0 Myr-nál 400 698 pont, 388 012 követési lépés, 398 pit-escape és 17 beolvadás keletkezett. A nyomkövetés 149,9 s volt, a `claimed` térkép feltöltése összesen 36 ms. A legdrágább források: **12: 5,95 s**, **14: 4,84 s**, **48: 4,82 s**, **32: 4,23 s**, **21: 3,72 s**. Forrásonként és medencénként a teljes [profil](../../../history/a8-2026-09-23/t0-profile.csv), a 22 Myr-os [profil](../../../history/a8-2026-09-23/t22-profile.csv) és a csatlakozási pontindexek is rendelkezésre állnak.

Az első medenceforrások munkája összesen 33,7 s, a teljes t=0 nyomkövetés 22,5%-a. A 17 beolvadó ág teljes, `claimed` nélküli végigkövetése további **38 797 lépést** végezne (a jelenlegi lépésszám +10,0%-a). A független követés tehát lehetséges, de a változatlan kimenethez kanonikus sorrendű csonkolás kell; ezt a már létező offline párhuzamos API végzi.

A kör szerinti sorrend önmagában bizonyíthatóan kimenetváltoztató ennél a seednél: 0 Myr-nál a **43-as** (a 7-es medence második forrása) a **32-es** (az 5-ös medence harmadik forrása) korábban lefoglalt ágába olvad. Kör szerinti feldolgozásban a 43-as a 32-es elé kerülne, ezért az összefolyási ponton más lenne a `claimed` térkép. A forrássorrend átírását bitazonos optimalizálásként nem lehet elfogadni. A meglévő párhuzamos API viewerbe kötése külön ND-132 szerinti szálkészlet- és teljes hálózatidő-kontrollt igényel; erre ez a terv nem adott gyorsulási bizonyítékot.

## B3 visszajelzés és előnézeti javítás

A felhasználó jelezte, hogy Play módban egyelőre **egyáltalán nem jelennek meg folyók**; a korábban látott vonalak eltűntek. A friss [Unity PerfLog](../../../unity/WorldGenViewer/Logs/PerfLog_20260923_113651.txt) hat 96 forrásos indítást és hat, elkészülés előtti megszakítást mutat, `river ready` és `river mesh` nélkül. A kód minden Buildben törli a korábbi folyóhálózatot, és a teljes finomításig nem rajzol újat. Ez a jelenlegi folyóhiány igazolt magyarázata; a B3 várakozási kérdése így még nem ítélhető meg.

Az [ND-145](../../../docs/04-decisions.md) szerint a dedikált folyószál most először egy ugyanabból a világmodellből számolt, durva előnézetet ad át, majd változatlanul folytatja a finomítást. Offline mérve az előnézeti Core-hálózat **0 Myr-nál 680 ms**, **22 Myr-nál 401 ms** volt; a viewer forrásfordítása sikeres. A részletek a [B3 javítási naplóban](../../../history/2026-09-23-a8-b3-river-preview.md) vannak. Az új `[ND-145 river preview]` log és a vonalak láthatósága **még élő Play-ellenőrzésre vár**. A kontrollált hideg/meleg/deep-time Unity-időmérés és a B3 vizuális elfogadás továbbra sem teljesült.

**Átadás:** `main` / `e3713bf`, előzetesen is módosított munkafa; commit és push nem készült. Újrafuttatási parancsok, teljes forráslista, lenyomatképzés, nyers PerfLog-hivatkozás és a legdrágább források a dátumozott [mérési naplóban](../../../history/2026-09-23-a8-river-baseline.md) vannak.

Az ezt követő [A8/2 szemantikai kapu](02-szemantika-es-algoritmusterv-eredmény.md)
az ND-146-ban rögzítette a bitazonos szerződést. A későbbi, külön
[A8/3 Core-mérés](03-core-megvalositas-es-differencialis-tesztek-eredmény.md)
4 workerrel igazolt offline gyorsulást; a viewer- és Unity-kapu továbbra is
nyitott.
