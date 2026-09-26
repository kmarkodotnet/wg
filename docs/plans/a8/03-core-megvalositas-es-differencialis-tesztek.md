# A8/3 — Core-megvalósítás és differenciális tesztek

## Előfeltétel és cél

Az A8/2 egy konkrét, méréssel indokolt, bitazonos algoritmust jóváhagyott a döntésnaplóban. Ebben a sessionben azt a `WorldGen.Core`-ban implementáld, a jelenlegi szekvenciális hálózatépítőt változatlan referencia-orákulumként megtartva.

## Teendők

1. A jóváhagyott terv szerint vezess be külön, egyértelműen elnevezett Core-útvonalat vagy belső ütemezőt. A forráslista és a végső `claimed` lefoglalás kanonikus indexrendje maradjon az ND-döntésben rögzített. Ne kerüljön Unity-függés a Core-ba; maradjon `netstandard2.1` és C# 9.
2. A spekulatív vagy hullámokban végzett munkából csak olyan rész legyen megtartható, amely nem függ egy közben megváltozó `claimed` térképtől. A beolvadáskor hozzáfűzött befogadó-pont, a tengerszint-ellenőrzés és a `ClaimCheckIndices` sorrendje egyezzen a szekvenciális úttal. Rendezett commitnál csak a végleges, levágott pontok foglalhatnak tile-t.
3. Vidd át a `CancellationToken`-t minden új ütemezési és belső nyomkövetési cikluson. Megszakításkor ne jelenjen meg részleges hálózat, ne maradjon futó munka vagy megfigyeletlen kivétel.
4. Adj célzott differenciális teszteket a `tests/WorldGen.Core.Tests/Hydrology/` alatt. Az orákulum a jelenlegi `BuildContinuousRiverNetworkFromSources`: teljes pontsor bitpontos összevetése, `SourceIndex`, `Termination`, `MergedIntoRiverIndex`, `ComputeDischargeWeights`. Legyen több seed, eltérő forrásszám, valódi 16×6 medenceforrás, azonos/közeli forrás, külön medencéket keresztező pálya, ismételt futás és eltérő worker-szám. A hosszú teljesvilág-próbát külön, ésszerű futásidejű regressziós kapuként kezeld.
5. Ellenőrizd a megszakítást már indítás előtt és futás közben. Mérd a teljes elapsed időt, CPU-terhelést és csúcs-memóriát az A8/1 baseline-hoz azonos konfiguráción. A gyorsulás csak mért, stabil eredményként állítható; sikertelen próbát ne kösd be a viewerbe.
6. Ha a folyó numerikus eredménye mégis változik, állj meg: ez új döntés és kompatibilitási/verziózási munka, nem az A8 bitazonos optimalizálásának csendes módosítása.

## Ellenőrzés és kész feltétel

- Célzott hidrológiai tesztek, majd `dotnet build WorldGen.sln` és az érintett teljes Core-tesztprojekt zöld.
- A Python referencia és a verziózott vektorok csak akkor érintettek, ha numerikus szerződés változik; ilyen változás az A8 célján kívül esik. Ha új vektor keletkezik, csak generálással, majd byte-összevetéssel kerüljön a repóba.
- Az A8/1 baseline-nal szembeni mérés és a teljes bitazonossági bizonyíték dátumozott `history/` naplóban szerepel.

## Átadás a következő sessionnek

Add át az API szignatúráját, a teszt- és mérési parancsokat, a referencia és az új út összehasonlító eredményét, valamint a fennmaradó teljesítménykockázatokat. Commit és push csak külön kérésre.
