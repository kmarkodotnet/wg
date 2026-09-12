# ND-97 — Három további cache-/allokációjavítás

2026-09-12. A felhasználó további feladatokat kért, a kézi ellenőrzést
későbbre hagyva. Az ND-96 kész-definícióját nem tekintjük elfogadottnak.
Ebben a körben a bizonyítható számítási/memóriaköltséget csökkentettük,
változatlan **8/7 px**, tile-/split-keretek, világmodell és relief mellett.

## Végrehajtott feladatok

1. **Metrikatároló megőrzése kameramozgáskor.** A terrain és water worker
   explicit `ResetView` hívással törli a régi vetített értékeket, de a
   Dictionary tárolóját megtartja. Az azonos vetület nem ürít. Null vagy
   eltérő forrás nem keverhető a cache-be. A régi, külön cache-t készítő
   `Reproject` API megmarad referenciának. A runtime single-flight ágai
   csak lezárt worker/staging után jutnak a következő nézet előkészítéséhez;
   futó request alatt nincs reset. A vízforrás továbbra is immutábilis,
   a resetelt cache kizárólag a hívó worker tulajdona.
2. **Helyi érvénytelenítés feedback után.** A geometria helyi revíziója
   a módosított tile és ősei mentén változik. Független ágak metrikája
   cache-találat marad. A telített cache elavult bejegyzése helyben
   frissül, nem vész el a kapacitáskorlát miatt. Az ősi bounds továbbra
   is tartalmazza a később megismert gyermekeket; a metrikai eredmény
   nem változik attól, hogy kevesebb másik bejegyzést érvénytelenítünk.
3. **Allokációmentes azonos-quad összehasonlítás.** A cache korábban az
   örökölt `ValueType.Equals` hívást használta egymásba ágyazott structokon.
   A reprodukció 1000 azonos feedbacknél **1368000 byte** allokációval
   bukott el Release módban. A helyi, komponensenkénti `double.Equals`
   összehasonlítás ugyanebben a próbában **0 byte**, változatlan revízióval.
   Nincs közelítő tolerancia vagy Core-/általános quad-típus módosítás.

## Páros mérés

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --storage-cache
```

Az ND-96 diagnosztikai snapshotja: 0 Myr, seed 184482873278464,
1238×688, 60° FOV, 26 oda-vissza kameraállás. Váltakozó futási sorrend;
**52/52 cut, új-split és halasztott-split egyezés** a két pixelbeállításon.
Az ismétlődő azonos kameraállást a referencia sem reseteli.

A referencia ugyanazt az új kódot futtatja, de kameramozgáskor új
metrika-szótárat épít. Mindkét ág megőrzi a geometriát, és ugyanazt a
helyi érvénytelenítési kódot használja. Ez a **tároló újrahasználatának
izolált összehasonlítása**, nem az ND-96 régi binárisának teljes A/B mérése.

| Cél | Referencia cut-allokáció | Új cut-allokáció | Referencia összes cut | Új összes cut |
|---|---:|---:|---:|---:|
| 12/10 px, kontroll | 504949672 byte | 244141880 byte | 1457,45 ms | 1329,90 ms |
| 8/7 px, aktuális | 785637840 byte | 302906120 byte | 1966,04 ms | 1873,04 ms |

Kerekítve **52–61% kevesebb allokáció**, **5–9% kisebb cut-idő** ebben
a helyi próbában. A GC-mérés a cutot végző szálon történik, a modell-
előkészítés és a request-/kameraobjektumok létrehozása kívül van rajta.
Nem memória-csúcs vagy VRAM-mérés; nem tartalmaz Unity-emissziót, uploadot,
FPS-t vagy a valódi mesh-feedback hullámait. A tároló megőrzése nagyobb
megmaradó kapacitást tarthat életben a világváltásig — a kisebb átmeneti
allokáció nem egyenlő kisebb teljes rezidens memóriával.

## Tesztek és ellenőrzési határ

- 15 új .NET-eset: 8 apró vetületváltozás, azonos/érvénytelen reset,
  helyi őslánc-érvénytelenítés, 3 kapacitáseset, két szigorú 0-byte próba.
- Viewer-LOD: **355/355 Debug és Release**. A teljes solution **747/747**:
  Core 384, CLI 8, viewer 355. Build 0 hiba / 0 warning. Unity runtime
  forrásfordítás 0 hiba / 83 meglévő warning; Editor-tesztprojekt 0 hiba /
  4 package-reference warning. A natív Editor-futást ez nem helyettesíti.
- A meleg metrika-cache 30 nézetváltása és 2880 tile-kiértékelése a
  célzott tesztben nulla byte-ot foglal; nem az egész renderer lett nullallokációs.
- Új Core-numerikus modul vagy módosított tesztvektor nincs ebben a körben;
  Python-orákulumot nem futtattunk újra. Az előző körben nem volt elérhető
  Python-telepítés; a külön referencia-/vektormunka érintetlen.
- Nincs új kézi Unity-próba, natív Editor-teszteredmény vagy elfogadott
  látvány-/FPS-eredmény. Nem indítottunk újabb rejtett Editor-automatizmust.

## Új log és későbbi közös próba

`metricStorage=ND97` jelzi az új kódot. A `metricViewResets` a tényleges
nézetváltások, a `metricFeedbackRefreshes` a geometria miatt újramért,
korábban eltárolt tile-ok száma. Mindkettő az aktuális terrain-cache
életciklusán belül összesített érték, nem frame-idő vagy teljes kérésdarabszám.
A `metricEntries` eltárolt bejegyzést jelent: lehet köztük helyi revízió
miatt újraszámolásra váró érték, amelyet a kód nem használhat cache-találatként.

Most nincs új kézi kapu. A [teljes közös lista](lod-final-batch-nd96-2026-09-12.md)
érvényben marad. Később ugyanazon útvonalon a gyors forgatás/zoom és a
megállás GC-/reakcióideje érdekes; a tile-minőségnek nem szabad megváltoznia
ettől a csomagtól. A sűrűbb végállapot több finomítási hulláma továbbra is
külön, nyitott teljesítménykérdés.

## Haladás

M9 súlyozott **61,25% (kb. 61%) marad**: konkrét költségcsökkentés és
regressziós bizonyíték készült, de az erőforrás/reakcióidő csoport nem
lépett teljes élő elfogadási szintre. E csomag durva ráfordítás-egyenértéke
**1–3 óra**; a teljes fennmaradó validáció/korrekció továbbra is **5–11 óra**.
Ezt újraértékeltük, nem vontunk le mértnek beállított időt: a fő maradék
a teljes felzárkózás és a natív/vizuális próba, amelyhez még nincs új adat.
