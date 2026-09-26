# A8/2 — Szemantika és algoritmusterv

## Előfeltétel és cél

Az A8/1 mérési kapuja a folytatás mellett szól. Ebben a sessionben a kör-alapú feldolgozás pontos szemantikáját és a bitazonosság bizonyítási feltételeit kell rögzíteni **a kódolás előtt**. Új architekturális döntéshez a döntésnaplóban ellenőrzött, egyedi ND-szám kell.

## A kritikus ütközés

`SelectRiverSourcesPerBasin` ma medence szerint csoportosított listát ad: az első medence legfeljebb hat forrása, majd a másodiké stb. A folytonos hálózatépítés ebben az indexsorrendben foglalja a finom tile-okat a `claimed` térképen. Az első találat határozza meg a beolvadási pontot és a `MergedIntoRiverIndex` értékét; ebből számolódik a vízhozam-súly és a renderelt szélesség.

Az egyszerű 1. kör = minden medence első forrása, 2. kör = minden medence második forrása **átindexeli a folyókat és megváltoztathatja a lefoglalási sorrendet**. A durva vízgyűjtők különállása nem bizonyítja, hogy a finom, folytonos nyomvonalak nem keresztezik egymást. Ezért az ND-124 „bitre azonos” várakozása jelen állapotban hipotézis.

## Teendők

1. Definiáld az azonossági szerződést: ugyanaz a forráslista és index, `SourceIndex`, pontsor, megállási ok, `MergedIntoRiverIndex`, vízhozam-súly; a viewer csak a teljes, aktuális generációhoz tartozó hálózatot kapja meg.
2. Válaszd szét a **számítás ütemezését** a **kanonikus lefoglalási sorrendtől**. Ha a körsorrend csak előreszámolási prioritás, a végső `claimed` írás maradjon az eredeti forrásindex sorrendjében. Határozd meg, hogyan kap a későbbi forrás biztonságos, változatlan `claimed` pillanatképet. Korábbi forrás befejezése előtt az előrehozott forrás legfeljebb spekulatív teljes nyomvonalat számolhat; ebből korai megállás nem következik automatikusan.
3. Vizsgáld meg a lehetséges kivitelezést az ND-132 szálkészlet-telítődésének megismétlése nélkül: korlátozott worker-szám, dedikált háttérmunka, rendezett commit, megszakítás a belső ciklusokban. Készíts költség- és memória-becslést a teljesen követett, de később levágott szakaszokra.
4. Ha a tényleges medencefüggetlenséget használnád ki, bizonyítsd a finom pályákra is. Ellenpélda esetén marad a globális forrásindex szerinti commit.
5. Állíts fel implementációs kaput: csak olyan terv mehet tovább, amely konkrétan megmondja, miért lesz rövidebb a teljes elkészülési idő, miközben a fenti lenyomat bitazonos marad. Ha csak a forráslista körsorrendbe rendezése gyorsítana, az már numerikus/rendereredmény-változás: külön ND-döntés, kompatibilitási vizsgálat és szükség esetén generátorverzió-emelés kell. Ez nem fér bele az „azonos kimenetű A8” munkába hallgatólagosan.

## Kimenet és kész feltétel

- Döntésnapló-bejegyzés a bizonyított szemantikával, alternatívákkal és a mérési kapu eredményével; szükség szerint `docs/01-architecture.md` adatfolyam-frissítés.
- Rövid pszeudokód a nyomkövetés, commit, megszakítás és eredményátadás sorrendjére.
- Kifejezett **megvalósítható / nem megvalósítható a jelen A8-feltételekkel** döntés. A 3. rész csak megvalósítható tervvel indul.

## Átadás a következő sessionnek

Az ND-bejegyzésre és az A8/1 mérésre hivatkozz; add át a pontos összehasonlítási szerződést és a szál-/memóriakeretet. Ne számozd át előre a folyóforrásokat pusztán a feldolgozási sorrend kedvéért.
