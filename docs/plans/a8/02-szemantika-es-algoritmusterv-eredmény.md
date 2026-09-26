# A8/2 — Szemantika és algoritmusterv: eredmény

**Dátum:** 2026-09-23. **Döntés:** a kör szerinti forrássorrend **elvetve**.
Az első kapu után új, scene-pontos párhuzamos mérés indult; ennek
eredménye lent szerepel. Az architekturális döntés: [ND-146](../../04-decisions.md).

## Azonossági szerződés

Az [A8/1 alapvonal](01-meresi-alap-es-dontesi-kapu-eredmény.md) forráslistájának
elemei és indexei, minden folyó `SourceIndex` mezője, az XYZ `double` pontok
bitsora, a befejezési ok, `MergedIntoRiverIndex` és a
`ComputeDischargeWeights` teljes vektora változatlan. A viewer a végleges
hálózatot csak az aktuális Build-generációból veszi át. A [t=0
lenyomat](../../../history/a8-2026-09-23/t0-fingerprint.txt)
`3a7940c6b2b0faab4f624297493588cf4193ed755db1154693cbc0bf7dd046d8`;
a [t=22 Myr lenyomat](../../../history/a8-2026-09-23/t22-fingerprint.txt)
`c0c1a8ff3b6ad283e5fc6f8451c4c5e856459e67c171b6dbd53fef64d52a4293`.

## Megengedett ütemezés és ellenpélda

Az aktuális Core-ban a `claimed` kizárólag a követés leállását érinti. Így
a teljes nyomvonalak számíthatók foglalás nélkül, párhuzamosan vagy más
prioritási sorrendben. A végleges hálózatot azonban csak növekvő eredeti
forrásindexben lehet csonkolni és lefoglalni. A következő forrásnak a korábbi
források véglegesítése előtt nincs biztonságos `claimed` pillanatképe.

A t=0 mérés konkrét ellenpéldája: a 43-as forrás a 32-es ágába olvad, holott
külön medenceblokkban vannak. Körsorrendben a 43-as korábban következne, így
az összefolyás és a vízhozamsúly megváltozhat. A durva vízgyűjtő szerinti
függetlenség a finom pályákra nem igaz.

Az egyetlen bitazonos spekulatív terv lépései:

```text
sources := eredeti, medence szerint csoportosított lista
for i in sources, tetszőleges számítási sorrendben, korlátos workerekkel:
    cancel ellenőrzés
    full[i] := TraceRiverPathContinuous(i, claimed nélkül)
    cancel ellenőrzés a nyomkövetés és pit-escape belső ciklusaiban
for i = 0..sources.Count-1:
    cancel ellenőrzés
    full[i] ClaimCheckIndices pontjain keresd az első claimed találatot
    a pontsor csonkolása és a befogadó tényleges pontjának hozzáfűzése
    kizárólag a végleges pontsor tile-jainak lefoglalása
ha a teljes lista kész és a Build-generáció azonos: főszálú átadás
egyébként: eredmény eldobása
```

Ezt a szemantikát a meglévő
`BuildContinuousRiverNetworkFromSourcesParallel` már implementálja. A
felvázolt új, korlátos változatnál legfeljebb **2 folyó-worker** és
**128 MiB spekulatív eredménykeret** lett volna az első kísérleti határ; ez
tervkeret, nem kimért fogyasztás vagy elfogadott implementáció. A `claimed`
térkép, a már végleges utak és a Unity mesh további memóriát használnának.
Az ND-132 tapasztalata miatt a szabad `Parallel.For` viewerbe visszakötése
nem engedhető át külön terep-/folyó CPU-versengési mérés nélkül.

## Költség és kapu

A scene-pontos t=0 alapvonal: 96 forrás, **149,984 s** teljes szekvenciális
Core-idő, 400 698 végleges pont. Az első medenceforrások együtt 33,732 s-t
tesznek ki (22,5%), de ez önmagában nem megtakarítás. A 17 beolvadó forrás
foglalás nélküli végigkövetése **38 797 további lépés** (+10,0%) volna. A
végleges pontok nyers XYZ `double` adata 9,62 MB; a listák, a spekulatív
többlet és a foglalási térkép ezen felül jelentkeznek. Az A8/1 nem mért
stabil teljes elkészülési gyorsulást a korlátos spekulatív tervhez. A
korábbi párhuzamos API teljesítményéről szóló régi adat nem az aktuális
96 forrásos viewer és ND-132 környezete.

Az A8/1 alapján ezért **nem indult azonnal A8/3 Core-implementáció vagy A8/4
viewer-átállítás**. Újranyitáshoz azonos scene-adatokon, korlátos workerszámmal, teljes
elkészülési időt, főszálú Buildet, CPU-versengést és csúcsmemóriát kell
mérni, majd a fenti teljes bitazonossági szerződést differenciálisan
igazolni. Ha csak a források átrendezése volna gyors, az kimenetváltozás:
külön döntést és generátorverziós vizsgálatot igényel.

A felhasználó Play alatt eltűnt folyókról számolt be. Erre külön
[ND-145/B3 előnézeti javítás](../../../history/2026-09-23-a8-b3-river-preview.md)
készült; a Unity Play-beli láthatósága még nincs igazolva. Ez az A8/2
gyorsítási kapuját nem változtatja meg.

## Utólagos mérés és módosított kapu

A következő feladatra adott felhasználói jelzés után a meglévő párhuzamos
Core-utat mértük ugyanazon 96 forrásos scene-bemenettel. A t=0 korlátlan
változat **26,378 s** alatt készült el, a teljes lenyomat bitre egyezett.
A Core API explicit worker-korlátot kapott; két workerrel t=0
**129,422 s**, néggyel **70,487 s**, t=22 Myr-nál néggyel **47,434 s**.
Mindhárom lenyomat egyezik a szekvenciális alapvonallal. A t=0 szekvenciális
alapvonal 141,808–149,984 s, a t=22 alapvonal 101,891 s. A 4-worker
változat ezért az **A8/3 offline Core-jelöltje**; a körsorrend elvetése
változatlan. A viewerbe kötés és az élő Unity-elfogadás külön A8/4 kapu.
