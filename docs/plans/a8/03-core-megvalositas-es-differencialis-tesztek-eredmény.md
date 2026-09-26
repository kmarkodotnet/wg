# A8/3 — Core-megvalósítás és differenciális tesztek: eredmény

**Dátum:** 2026-09-23. **Állapot:** az offline Core-jelölt elkészült;
viewer-átállítás és élő Unity-elfogadás még nincs. Döntés: [ND-146](../../04-decisions.md).

## Változás

A meglévő `BuildContinuousRiverNetworkFromSourcesParallel` API opcionális
`maxDegreeOfParallelism` paramétert kapott (`-1`: meglévő, korlátlan működés;
pozitív szám: explicit felső worker-korlát). A 0 és a -1 alatti érték
hibát ad. A `TraceRiverPathContinuous` numerikus útja, a foglalás nélküli
teljes követés, a `ClaimCheckIndices` szerinti kanonikus csonkolás és a
forrásindex szerinti `claimed` commit nem változott. A szekvenciális API
érintetlen referencia-orákulum maradt. A jelenleg mért jelölt: **4 worker**.

Az A8/1 diagnosztikai futtató `parallel`, `parallel2`, `parallel4` módja
ugyanazt a scene-bemenetet és bitpontos lenyomatképzést használja. A mért
processz-munkakészlet a teljes diagnosztikai processz csúcsa, nem kizárólag
a hálózaté; az eredeti szekvenciális menetekhez nem állt rendelkezésre
ugyanígy mért memóriaérték.

## Teljes hálózat: azonos bemenet és lenyomat

Konfiguráció: `0xA7C944210000` seed, 20 lemez, `level=5`, 0,65 vízarány,
`climateDayT=10`, 16×6 forrás, `fineDepth=4`, 50 m lépés; AMD Ryzen 7 5700X,
16 logikai processzor, Windows x64, .NET 8 Release. A mérések külön
menetben futottak, a Unity előtérterhelése nem kontrollált. A t=0
szekvenciális baseline 141,808–149,984 s, a t=22 Myr baseline 101,891 s.

| Mód | t | Teljes idő | Processz-CPU-idő | Csúcs munkakészlet | Lenyomat |
|---|---:|---:|---:|---:|---|
| korlátlan (16 logikai processzor) | 0 | 26,378 s | nem mért | 168,972 MB | t=0 egyező |
| 2 worker | 0 | 129,422 s | nem mért | 117,301 MB | t=0 egyező |
| 4 worker | 0 | 70,487 s | nem mért | 106,390 MB | t=0 egyező |
| 4 worker, ismétlés | 0 | 67,347 s | 222,203 s | 118,952 MB | t=0 egyező |
| 4 worker | 22 Myr | 47,434 s | nem mért | 107,676 MB | t=22 egyező |

Mindkét t=0 négyworker-es menet, a kétworker-es és a korlátlan menet teljes
SHA-256-a:
`3a7940c6b2b0faab4f624297493588cf4193ed755db1154693cbc0bf7dd046d8`.
A t=22 Myr négyworker-es teljes SHA-256:
`c0c1a8ff3b6ad283e5fc6f8451c4c5e856459e67c171b6dbd53fef64d52a4293`.
A hash az összes forrásazonosítót, `SourceIndex`-et, végállapotot,
`MergedIntoRiverIndex`-et, vízhozamsúlyt és minden XYZ pont `double`
bitmintáját tartalmazza. A 96 folyó pontszáma t=0: 400 698, t=22: 284 834.
A nyers lenyomatfájlok a [history adatmappában](../../../history/a8-2026-09-23/)
vannak.

A négyworker-es t=0 teljes idő az alapvonalhoz képest **2,0–2,2×**,
t=22-nél **2,15×** offline gyorsulást mutat. A 2-worker t=0 mérés csak
~1,1× volt, ezért nem ezt választottuk. A korlátlan 16 szálas út gyorsabb,
de az ND-132 miatt viewerben CPU-versengési kockázatot hordoz.

## Tesztek és fennmaradó kapu

- A worker-korlát 1/2/4 értékét a szekvenciális orákulummal, az összefolyási
  mezőkkel, pontokkal és vízhozamsúlyokkal összevető célzott teszt; 0 és -2
  elutasítása. Célzott hidrológiai tesztcsoport: **13/13**.
- Teljes `WorldGen.Core.Tests` Release: **600/600**.
- `dotnet build WorldGen.sln --no-restore`: **0 hiba, 0 figyelmeztetés**.
- A teljes 96 forrásos t=0, ismételt t=0 és t=22 Myr hálózat SHA-256
  egyezése fent. A numerikus szerződés nem változott, új referencia-vektor
  vagy generátorverzió nem szükséges.

Az A8/4 előtt a 4-worker út viewer előtérrel együtt mért teljesítménye,
memóriája, az ND-145 előnézetének megtartása, gyors deep-time váltáskor a
megszakítás és az élő Play-beli folyómegjelenés ellenőrzése szükséges. A
Core-gyorsulás nem bizonyít Unity-gyorsulást.

Újrafuttatás:

```powershell
dotnet run --project tools/diagnostics/RiverBaseline/RiverBaseline.csproj -c Release -- 0 artifacts/a8-parallel-probe parallel4
dotnet run --project tools/diagnostics/RiverBaseline/RiverBaseline.csproj -c Release -- 22 artifacts/a8-parallel-probe parallel4
dotnet test tests/WorldGen.Core.Tests/WorldGen.Core.Tests.csproj -c Release --filter FullyQualifiedName~ParallelRiverNetworkTests
```

Branch `main`, kiinduló HEAD `e3713bf`, előzetesen is módosított munkafa.
Commit és push nem készült.
