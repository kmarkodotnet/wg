# ND-98 — Stabil víz-cut ismételt munkájának elhagyása

2026-09-12. A felhasználó további implementációt kér; a kézi ellenőrzés
később, egyben történik. Ez a csomag nem módosítja a 8/7 pixelcélt,
a terep-/vízkereteket, a reliefet, a Core-t vagy a világmodell értékeit.

## Ok és javítás

A terep finomítási hullámai közben `ComputeIndependentWater` mindig teljes
vízkiválasztást, balance-t, fedéskiegészítést és snapshotrendezést végzett.
A korábbi azonos-cut ellenőrzés csak ezek után, az emissziót hagyta ki.

Most a víz eredményében tárolt, pontos paraméterkulcs és egy igazolt
fixpont engedi a kiválasztási munka kihagyását. Nem elég a nulla halasztott
split: azonos nézeten egy további teljes újraszámolásnak azonos rendezett
leveleket kell adnia. Az ezt követő azonos kérés megosztja az immutábilis
fedést/snapshotokat. Kérésenként kis eredményobjektum készül; nincs új
növekvő cache vagy régi eredményeket megtartó előzménylánc.

Minden vetületkomponens és LOD-paraméter változása teljes kiválasztást
eredményez. Forrás- és cache-validálás, megszakításellenőrzés a gyors út
előtt is fut. Geometriafeedbackes cache-nél a gyors út tiltott. A víz
runtime-forrása alapvetően állandó sugarú, feedback nélküli.

Az új kérés `NewSplits`, `DeferredSplits`, `SelectionMs`, `BalanceMs`
értéke újrahasználatkor nulla, nem az előző kérés munkája. A külső
`selectionTotal` stopper továbbra is az aktuális hívást méri. A víz-apply
log új mezői: `selectionReusePolicy=ND98 reusedSelection=True/False`.
A `reusedMesh` külön jelzi az emisszió/feltöltés kihagyását.

## Páros mérés

Reprodukció:

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj --no-restore -c Release -p:_EnableDefaultWindowsPlatform=false -- --water-reuse
```

Szintetikus teljes vízgömb: R=100, minden L8 gyökér víz, FOV=60°,
1238×688, 8 px küszöb, merge=split/1,5, maxL20, 8192 levél,
256 új split/kérés. Nem egy konkrét világ partvonalának élő visszajátszása.
Külön előzménylánc és külön ND-97 metrika-cache a két oldalon, váltakozó
végrehajtási sorrend. Kontroll: `reuseStableSelection:false`.

| Sorozat | Párok / újrahasználat | Kontroll idő | ND-98 idő | Kontroll allokáció | ND-98 allokáció |
|---|---:|---:|---:|---:|---:|
| 300, 160, 120, 105 távolság, egyenként 30 kérés | 120 / 96 | 3622,61 ms | 959,74 ms | 815256856 B | 283173504 B |
| Folytonos közelítés/visszatávolítás, 200→113→200 | 60 / 0 | 4047,71 ms | 4060,85 ms | 626263208 B | 626263208 B |

Minden párban pontosan azonos levelek, rejtett base-gyökerek és pending
állapot; maximum 8192 levél. Az álló kamerás sorozat 16, a mozgó 13
kérésben még további finomítást igényelt. A teljes beállási munka is
benne van a táblázatban; nem csak a már meleg gyors ág.

Álló kameránál ebben a próbában kb. **74% idő- és 65% allokációcsökkenés
a vízkiválasztásban**. Mozgáskor azonos allokáció és közel azonos idő;
a 0,3%-os időeltérésből nem állítunk érdemi regressziót vagy gyorsulást.
A mérés egy helyi futás, nem statisztikai teljesítménygarancia.
Forráskészítés, kamerához igazított cache-reset, összehasonlítás,
emisszió, GPU-upload és FPS nincs a mért szakaszban. A kisebb teljes
zoomreakció-időt ebből nem tekintjük igazoltnak.

## Teszt és későbbi kapu

18 új .NET-eset: fixpont-igazolás, friss munkastatisztika, három kvóta
melletti álló/mozgó páros cut, 12 vetület-/paraméterváltozás és kvótaváltás,
cancellation, érvénytelen/eltérő forrás és cache, feedbackes cache,
párhuzamos immutábilis felhasználás és pontos sarokgeometria-egyezés.
Viewer **373/373 Debug és Release**; Unity runtime forrásfordítás
0 hiba / 83 meglévő warning; Editor-tesztfordítás 0 hiba / 4 warning.
Solution build 0 hiba / 0 warning; teljes solution **765/765 teszt zöld**
(384 Core, 373 viewer, 8 CLI). `git diff --check` tiszta. Python/KAT
ellenőrzést ebben a csomagban nem futtattunk: a környezetben nincs
Python-telepítés; e módosítás nem érint Core-t vagy tesztvektort.

Élő Unity Editor-, vizuális és FPS-validáció nem történt. A
[közös teljes ellenőrzőlista](lod-final-batch-nd96-2026-09-12.md) marad:
különösen víz/part fedés, zoomfordítás, világváltás, hosszú memória/FPS
próba. Későbbi logban álló kamera mellett a stabil víz újrahasználatát
a fenti marker mutatja; egy új nézet első kérésénél `False` várható.

M9 súlyozottan **61,25% marad**: az offline részfolyamat-költség csökkent,
de teljes reakcióidő és élő elfogadás még nincs. A csomag durva ráfordítás-
egyenértéke 1–2 óra; a fennmaradó natív/vizuális mérések és szükséges
korrekciók becslése továbbra is 5–11 óra, nem mért munkaidő.
